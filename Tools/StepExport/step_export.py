"""Isolated, offline STEP assembly writer. No mesh-to-STEP fallback is allowed."""
import argparse
import json
import math
import os
from pathlib import Path
import sys
import tempfile
import time
from kernel import (MM, IDENTITY, custom_part, read_brep, transform, validate,
                    box_crop, translate, tile_plate, children, volume)
from OCP.gp import gp_Trsf
from OCP.TopLoc import TopLoc_Location
from OCP.TDocStd import TDocStd_Document
from OCP.TCollection import TCollection_ExtendedString
from OCP.TDataStd import TDataStd_Name
from OCP.XCAFDoc import XCAFDoc_DocumentTool, XCAFDoc_ColorGen, XCAFDoc_ColorSurf
from OCP.STEPCAFControl import STEPCAFControl_Writer
from OCP.STEPControl import STEPControl_AsIs
from OCP.IFSelect import IFSelect_RetDone
from OCP.Interface import Interface_Static
from OCP.Quantity import Quantity_Color, Quantity_ColorRGBA, Quantity_TOC_RGB
from OCP.TopAbs import TopAbs_FACE

def emit(status, progress=0., **fields):
    print(json.dumps(dict(status=status,progress=progress,**fields)),flush=True)

def dot(a,b): return sum(x*y for x,y in zip(a,b))
def cross(a,b): return [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]]
def normalized(v):
    length=math.sqrt(dot(v,v))
    if length<1e-10:raise ValueError('A part has zero scale and cannot form a CAD body.')
    return [x/length for x in v]

def placement(matrix):
    """Unity inches/Y-up/left-handed -> CAD mm/Z-up/right-handed.

    QR decomposition keeps rotation/translation as an assembly placement and
    bakes reflection/scale/shear into a reusable definition. STEP placements must
    be rigid. This also handles mirrored parts and scaled parents correctly.
    """
    if len(matrix)!=16 or not all(math.isfinite(v) for v in matrix):
        raise ValueError('A part has an invalid transform.')
    rows=[matrix[0:4],matrix[8:12],matrix[4:8]]
    cols=[[rows[r][c] for r in range(3)] for c in range(3)]
    x=normalized(cols[0]);xy=dot(cols[1],x)
    y=normalized([cols[1][i]-xy*x[i] for i in range(3)])
    z=cross(x,y);basis=[x,y,z]
    residual=IDENTITY.copy()
    for r in range(3):
        # Unity matrices are single precision. Remove sub-micron scale/shear
        # noise introduced by quaternion multiplication before choosing OCCT's
        # analytic rigid-transform path. Real nonuniform scaling is retained.
        for c in range(3): residual[r*4+c]=round(dot(basis[r],cols[c]),6)
    if abs(residual[10])<1e-10:raise ValueError('A part has zero scale and cannot form a CAD body.')
    location=gp_Trsf()
    location.SetValues(*[basis[c][r] if c<3 else rows[r][3]*MM for r in range(3) for c in range(4)])
    return residual,TopLoc_Location(location)

class Library:
    def __init__(self,root,data):
        self.root=root.resolve()
        index=json.loads((self.root/'index.json').read_text())
        if index.get('version')!=1:raise ValueError('Unsupported CAD library version.')
        self.entries=index['parts'];self.cache={}
        self.custom={p['definitionId']:p for p in data.get('customParts',[])}

    def key(self,part):
        custom=part.get('customDefinitionId')
        if custom:
            if custom not in self.custom:raise ValueError('Missing Poly Maker definition: '+part['name'])
            return ('custom',custom)
        key=part['catalogId']
        if key in self.entries:return ('stock',key)
        if key.startswith(('CCHL-','ANGL-','UCHL-')):
            prefix,count=key.rsplit('-',1)
            count=int(count)
            if prefix+'-35' in self.entries and 1<=count<=35:return ('cut',prefix+'-35',count)
        if key.startswith('SHFT-'):
            prefix,_=key.rsplit('-',1)
            if prefix+'-1' in self.entries:return ('stock',prefix+'-1')
        if key.startswith('PLTE-') and 'PLTE-5-5' in self.entries:
            _,length,width=key.split('-');length,width=int(length),int(width)
            if 1<=length<=100 and 1<=width<=100 and length*width<=2500:return ('plate','PLTE-5-5',length,width)
        raise ValueError('No verified solid CAD model for '+key)

    def stock(self,key):
        entry=self.entries[key]
        path=(self.root/entry['file']).resolve()
        if self.root not in path.parents:raise ValueError('Invalid CAD library path.')
        return read_brep(path)

    def shape(self,key):
        if key in self.cache:return self.cache[key]
        if key[0]=='custom':shape=custom_part(self.custom[key[1]])
        elif key[0]=='stock':shape=self.stock(key[1])
        elif key[0]=='cut':
            shape=self.stock(key[1]);count=key[2]
            shape=translate(shape,x=(35-count)*MM/4)
            half=count*MM/4
            shape=box_crop(shape,[-half,-1000,-1000],[half,1000,1000])
        elif key[0]=='plate':shape=tile_plate(self.stock(key[1]),key[2],key[3])
        else:raise ValueError('Unsupported CAD recipe.')
        validate(shape,key[1],allow_surfaces=key[0]!='custom')
        self.cache[key]=shape
        return shape

def export(data,library,destination,cancel=lambda:False):
    if data.get('version')!=1 or data.get('lengthUnit')!='inch':raise ValueError('Unsupported export snapshot.')
    parts=data.get('parts',[])
    if not parts:raise ValueError('There are no parts to export.')
    missing={}
    for part in parts:
        try:library.key(part)
        except ValueError:missing[part['catalogId']]=part.get('name') or part['catalogId']
    if missing:
        descriptions=[missing[key]+' ('+key+')' if missing[key]!=key else key for key in sorted(missing)]
        raise ValueError('Export needs verified CAD models for: '+', '.join(descriptions)+'. No parts were omitted and no STEP file was replaced. Use STL to export their display geometry.')
    document=TDocStd_Document(TCollection_ExtendedString('XmlXCAF'))
    shapes=XCAFDoc_DocumentTool.ShapeTool_s(document.Main())
    colors=XCAFDoc_DocumentTool.ColorTool_s(document.Main())
    root=shapes.NewShape()
    TDataStd_Name.Set_s(root,TCollection_ExtendedString(data.get('name') or 'Protobot assembly'))
    definitions={};solid_count=0
    for i,part in enumerate(parts):
        if cancel():raise InterruptedError('Export cancelled.')
        key=library.key(part)
        residual,location=placement(part['matrix'])
        palette=[tuple(max(0.,min(1.,float(s.get('color',{}).get(c,1. if c=='a' else .6)))) for c in ('r','g','b','a')) for s in part.get('surfaces',[])]
        if not palette:palette=[(.6,.6,.6,1.)]
        def cad_color(rgba):return Quantity_ColorRGBA(Quantity_Color(*rgba[:3],Quantity_TOC_RGB),rgba[3])
        definition_key=(key,tuple(round(v,7) for v in residual),tuple(palette))
        if definition_key not in definitions:
            shape=transform(library.shape(key),residual)
            validate(shape,part['name'],allow_surfaces=key[0]!='custom')
            label=shapes.AddShape(shape,False)
            TDataStd_Name.Set_s(label,TCollection_ExtendedString(part['catalogId']))
            colors.SetColor(label,cad_color(palette[0]),XCAFDoc_ColorGen)
            material_indices=library.entries[key[1]].get('faceMaterials',[]) if key[0]=='stock' else []
            if material_indices:
                faces=list(children(shape,TopAbs_FACE))
                if len(faces)!=len(material_indices):raise ValueError('The CAD material map is inconsistent for '+part['catalogId'])
                for face,index in zip(faces,material_indices):
                    if index<0 or index>=len(palette):raise ValueError('The part materials differ from the CAD template: '+part['catalogId'])
                    colors.SetColor(shapes.AddSubShape(label,face),cad_color(palette[index]),XCAFDoc_ColorSurf)
            definitions[definition_key]=(label,len(list(children(shape))))
        label,count=definitions[definition_key];solid_count+=count
        instance=shapes.AddComponent(root,label,location)
        TDataStd_Name.Set_s(instance,TCollection_ExtendedString(part['name']+' ['+part['id']+']'))
        if i%10==0 or i==len(parts)-1:emit('Building CAD assembly',.1+.7*(i+1)/len(parts),parts=i+1,total=len(parts))
    shapes.UpdateAssemblies()
    if cancel():raise InterruptedError('Export cancelled.')
    writer=STEPCAFControl_Writer()
    writer.SetColorMode(True);writer.SetNameMode(True);writer.SetLayerMode(True)
    Interface_Static.SetCVal_s('write.step.schema','AP214IS')
    Interface_Static.SetCVal_s('xstep.cascade.unit','MM')
    Interface_Static.SetCVal_s('write.step.unit','MM')
    Interface_Static.SetIVal_s('write.surfacecurve.mode',1)
    emit('Writing STEP assembly',.85)
    if not writer.Transfer(document,STEPControl_AsIs):raise ValueError('The CAD assembly could not be transferred to STEP.')
    destination=Path(destination).resolve()
    if destination.suffix.lower() not in ('.step','.stp'):raise ValueError('Choose a .step or .stp file.')
    # The temporary file is beside the target so publication is an atomic rename.
    # Cancellation/errors leave an existing user's file untouched.
    fd,temporary=tempfile.mkstemp(prefix='.'+destination.stem+'-',suffix='.step.tmp',dir=destination.parent)
    os.close(fd)
    try:
        if writer.Write(temporary)!=IFSelect_RetDone:raise OSError('The STEP file could not be written.')
        if cancel():raise InterruptedError('Export cancelled.')
        with open(temporary,'rb') as check:
            if check.read(13)!=b'ISO-10303-21;':raise OSError('The CAD writer produced an invalid STEP header.')
        os.replace(temporary,destination)
    finally:
        if os.path.exists(temporary):os.unlink(temporary)
    return dict(parts=len(parts),solidInstances=solid_count,definitions=len(definitions),path=str(destination))

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--job',required=True,type=Path)
    parser.add_argument('--library',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    parser.add_argument('--cleanup-job',action='store_true')
    args=parser.parse_args()
    started=time.monotonic()
    try:
        emit('Reading assembly',.02)
        data=json.loads(args.job.read_text(encoding='utf-8-sig'))
        library=Library(args.library,data)
        result=export(data,library,args.output,lambda:args.job.with_suffix('.cancel').exists())
        emit('complete',1.,seconds=round(time.monotonic()-started,2),**result)
        return 0
    except InterruptedError as ex:
        emit('cancelled',message=str(ex));return 2
    except Exception as ex:
        emit('error',message=str(ex));return 1
    finally:
        if args.cleanup_job:
            for path in (args.job,args.job.with_suffix('.cancel')):
                try:path.unlink(missing_ok=True)
                except OSError:pass

if __name__=='__main__':sys.exit(main())
