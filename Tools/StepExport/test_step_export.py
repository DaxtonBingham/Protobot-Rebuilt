import copy
import json
import math
from pathlib import Path
import tempfile
import unittest
from kernel import *
from step_export import Library, export, placement
from OCP.Bnd import Bnd_Box
from OCP.BRepBndLib import BRepBndLib
from OCP.STEPControl import STEPControl_Reader
from OCP.BRepAdaptor import BRepAdaptor_Surface
from OCP.GeomAbs import GeomAbs_Cylinder
from OCP.TopoDS import TopoDS
from OCP.IFSelect import IFSelect_RetDone
from OCP.STEPCAFControl import STEPCAFControl_Reader
from OCP.TDocStd import TDocStd_Document
from OCP.TCollection import TCollection_ExtendedString
from OCP.TDF import TDF_LabelSequence,TDF_Label
from OCP.XCAFDoc import XCAFDoc_DocumentTool,XCAFDoc_ColorSurf
from OCP.Quantity import Quantity_ColorRGBA

def loop(points):
    return {'closed':True,'anchors':[{'position':{'x':x,'y':y},'inHandle':{'x':0,'y':0},'outHandle':{'x':0,'y':0}} for x,y in points], 'segmentKinds':[0]*len(points)}

def definition():
    return {'definitionId':'plate','name':'Test plate','thicknessInches':.125,
            'sketch':{'outerLoop':loop([(-2,-1),(2,-1),(2,1),(-2,1)]),'cutoutLoops':[]},
            'holes':[{'position':{'x':0,'y':0},'size':{'x':.25,'y':.25},'shape':0,'rotationDegrees':0}]}

def bounds(shape):
    box=Bnd_Box();BRepBndLib.AddOptimal_s(shape,box,False,False)
    return box.Get()

class ExportTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
        self.library_root=self.root/'Library';self.library_root.mkdir()
        shape=BRepPrimAPI_MakeBox(gp_Pnt(0,0,0),gp_Pnt(25.4,12.7,6.35)).Shape()
        write_brep(shape,self.library_root/'box.brep')
        (self.library_root/'index.json').write_text(json.dumps({'version':1,'parts':{'BOX':{'file':'box.brep'}}}))
        self.part={'id':'part-1','catalogId':'BOX','name':'Box','matrix':IDENTITY.copy(),
                   'surfaces':[{'color':{'r':.8,'g':.1,'b':.2}}]}
        self.data={'version':1,'lengthUnit':'inch','name':'Robot','parts':[self.part],'customParts':[]}
        self.output=self.root/'Robot.step'
    def tearDown(self):self.tmp.cleanup()
    def read(self):
        reader=STEPControl_Reader();self.assertEqual(reader.ReadFile(str(self.output)),IFSelect_RetDone)
        reader.TransferRoots();return reader.OneShape()
    def test_smooth_holes_and_volume(self):
        shape=custom_part(definition())
        expected=(8-math.pi*.125**2)*.125*MM**3
        self.assertAlmostEqual(volume(shape),expected,places=5)
        cylinders=[f for f in children(shape,TopAbs_FACE) if BRepAdaptor_Surface(TopoDS.Face_s(f)).GetType()==GeomAbs_Cylinder]
        self.assertEqual(len(cylinders),1)
    def test_bezier_outline_is_not_tessellated(self):
        data=definition();data['holes']=[]
        data['sketch']['outerLoop']['segmentKinds'][0]=1
        a,b=data['sketch']['outerLoop']['anchors'][:2]
        a['outHandle']={'x':1,'y':-.5};b['inHandle']={'x':-1,'y':-.5}
        shape=custom_part(data)
        self.assertTrue(BRepCheck_Analyzer(shape).IsValid())
        self.assertEqual(len(list(children(shape,TopAbs_FACE))),6)
        self.assertGreater(volume(shape),8*.125*MM**3)
    def test_square_and_elliptical_holes(self):
        data=definition();data['holes'][0]['size']={'x':.5,'y':.25};data['holes'][0]['rotationDegrees']=37
        self.assertAlmostEqual(volume(custom_part(data)),(8-math.pi*.25*.125)*.125*MM**3,delta=1e-4)
        data['holes'][0]['shape']=1
        self.assertAlmostEqual(volume(custom_part(data)),(8-.5*.25)*.125*MM**3,places=5)
    def test_cutout_loop(self):
        data=definition();data['holes']=[];data['sketch']['cutoutLoops']=[loop([(-.5,-.5),(.5,-.5),(.5,.5),(-.5,.5)])]
        self.assertAlmostEqual(volume(custom_part(data)),7*.125*MM**3,places=5)
    def test_instances_units_and_axes(self):
        self.part['matrix'][3]=2;self.part['matrix'][7]=3;self.part['matrix'][11]=4
        result=export(self.data,Library(self.library_root,self.data),self.output)
        shape=self.read();self.assertEqual(result['parts'],1)
        expected=[50.8,101.6,76.2,76.2,107.95,88.9]
        for value,target in zip(bounds(shape),expected):self.assertAlmostEqual(value,target,places=5)
        content=self.output.read_text();self.assertIn('Box [part-1]',content);self.assertIn('MILLI',content)
    def test_identical_parts_share_definition(self):
        p=copy.deepcopy(self.part);p['id']='part-2';p['matrix'][3]=5
        self.data['parts'].append(p)
        result=export(self.data,Library(self.library_root,self.data),self.output)
        self.assertEqual(result['definitions'],1);self.assertEqual(result['parts'],2)
        self.assertEqual(len(list(children(self.read()))),2)
    def test_reflection_scale_and_shear(self):
        matrix=IDENTITY.copy();matrix[0]=-2;matrix[1]=.3;matrix[5]=3;matrix[10]=4
        self.part['matrix']=matrix
        export(self.data,Library(self.library_root,self.data),self.output)
        shape=self.read();validate(shape,'mirrored assembly')
        self.assertAlmostEqual(volume(shape),25.4*12.7*6.35*24,places=4)
    def test_missing_parts_never_replace_file(self):
        self.output.write_text('existing file');self.part['catalogId']='MISSING'
        with self.assertRaisesRegex(ValueError,'MISSING'):
            export(self.data,Library(self.library_root,self.data),self.output)
        self.assertEqual(self.output.read_text(),'existing file')

    def test_pin_variants_require_their_exact_material_template(self):
        index={'version':1,'parts':{'OVPN-Red Blue':{'file':'box.brep'},
                                  'OVPN-Yellow':{'file':'box.brep'}}}
        (self.library_root/'index.json').write_text(json.dumps(index))
        library=Library(self.library_root,self.data)
        self.part['catalogId']='OVPN-Yellow'
        self.assertEqual(library.key(self.part),('stock','OVPN-Yellow'))
        self.part['catalogId']='OVPN-Unknown'
        with self.assertRaisesRegex(ValueError,'OVPN-Unknown'):library.key(self.part)
    def test_cancel_never_replaces_file(self):
        self.output.write_text('existing file')
        calls=[0]
        def cancel():
            calls[0]+=1;return calls[0]>=3
        with self.assertRaises(InterruptedError):
            export(self.data,Library(self.library_root,self.data),self.output,cancel)
        self.assertEqual(self.output.read_text(),'existing file')
        self.assertEqual(list(self.root.glob('*.tmp')),[])
    def test_zero_scale_rejected(self):
        matrix=IDENTITY.copy();matrix[5]=0
        with self.assertRaises(ValueError):placement(matrix)

    def test_repeated_placements_do_not_hide_a_reversed_solid(self):
        shape=BRepPrimAPI_MakeBox(gp_Pnt(0,0,0),gp_Pnt(2,3,4)).Shape()
        matrix=IDENTITY.copy();matrix[3]=1000
        moved=transform(shape,matrix,copy=False)
        validate(compound([shape,moved]),'repeated solid')
        with self.assertRaisesRegex(ValueError,'valid closed solid'):
            validate(compound([shape,moved,moved.Reversed()]),'reversed solid')
    def test_custom_export_is_solid(self):
        self.data['customParts']=[definition()];self.part['customDefinitionId']='plate';self.part['catalogId']='CPLY-plate'
        export(self.data,Library(self.library_root,self.data),self.output)
        validate(self.read(),'custom part')

    def test_compressed_templates_are_analytic(self):
        source=self.library_root/'box.brep'
        packed=source.with_suffix('.brep.gz');packed.write_bytes(gzip.compress(source.read_bytes()))
        shape=read_brep(packed)
        self.assertEqual(len(list(children(shape,TopAbs_FACE))),6)
        self.assertAlmostEqual(volume(shape),25.4*12.7*6.35,places=5)

    def test_unity_rotation_noise_does_not_create_shear(self):
        matrix=[.7071068,0,.7071068,0,0,1,0,0,-.7071068,0,.7071068,0,0,0,0,1]
        residual,_=placement(matrix)
        self.assertEqual(residual,[1.,0.,0.,0.,0.,1.,0.,0.,0.,0.,-1.,0.,0.,0.,0.,1.])

    def test_face_colors_and_component_structure_survive_step(self):
        index={'version':1,'parts':{'BOX':{'file':'box.brep','faceMaterials':[0,1,0,1,0,1]}}}
        (self.library_root/'index.json').write_text(json.dumps(index))
        self.part['surfaces']=[{'color':{'r':1.,'g':0.,'b':0.}}, {'color':{'r':0.,'g':0.,'b':1.}}]
        export(self.data,Library(self.library_root,self.data),self.output)
        doc=TDocStd_Document(TCollection_ExtendedString('XmlXCAF'))
        reader=STEPCAFControl_Reader();reader.SetColorMode(True);reader.SetNameMode(True)
        self.assertEqual(reader.ReadFile(str(self.output)),IFSelect_RetDone);self.assertTrue(reader.Transfer(doc))
        shapes=XCAFDoc_DocumentTool.ShapeTool_s(doc.Main());colors=XCAFDoc_DocumentTool.ColorTool_s(doc.Main())
        roots=TDF_LabelSequence();shapes.GetFreeShapes(roots);self.assertEqual(roots.Length(),1)
        components=TDF_LabelSequence();shapes.GetComponents_s(roots.Value(1),components)
        self.assertEqual(components.Length(),1)
        definition=TDF_Label();shapes.GetReferredShape_s(components.Value(1),definition)
        found=[]
        for face in children(shapes.GetShape_s(definition),TopAbs_FACE):
            color=Quantity_ColorRGBA();self.assertTrue(colors.GetColor(face,XCAFDoc_ColorSurf,color))
            rgb=color.GetRGB();found.append(tuple(round(v,5) for v in (rgb.Red(),rgb.Green(),rgb.Blue())))
        self.assertEqual(found.count((1.,0.,0.)),3);self.assertEqual(found.count((0.,0.,1.)),3)

    def test_invalid_material_map_leaves_existing_file_untouched(self):
        (self.library_root/'index.json').write_text(json.dumps({'version':1,'parts':{'BOX':{'file':'box.brep','faceMaterials':[0]}}}))
        self.output.write_text('existing file')
        with self.assertRaisesRegex(ValueError,'material map'):
            export(self.data,Library(self.library_root,self.data),self.output)
        self.assertEqual(self.output.read_text(),'existing file')

if __name__=='__main__':
    from OCP.IFSelect import IFSelect_RetDone
    unittest.main()
