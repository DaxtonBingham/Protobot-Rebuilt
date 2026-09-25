"""Reproduce reviewed legacy game templates from pinned original field CAD."""
import argparse,hashlib,json
from pathlib import Path
from build_library import surface_check,face_materials
from catalog_recipes import healed
from kernel import compound,transform,read_brep,write_brep,validate,IDENTITY
from OCP.STEPCAFControl import STEPCAFControl_Reader
from OCP.TDocStd import TDocStd_Document
from OCP.TCollection import TCollection_ExtendedString,TCollection_AsciiString
from OCP.TDF import TDF_LabelSequence,TDF_Label,TDF_Tool
from OCP.XCAFDoc import XCAFDoc_DocumentTool
from OCP.IFSelect import IFSelect_RetDone

def load(path):
    doc=TDocStd_Document(TCollection_ExtendedString('XmlXCAF'))
    reader=STEPCAFControl_Reader()
    if reader.ReadFile(str(path))!=IFSelect_RetDone or not reader.Transfer(doc):raise ValueError('Cannot read source CAD')
    tool=XCAFDoc_DocumentTool.ShapeTool_s(doc.Main());sequence=TDF_LabelSequence();tool.GetShapes(sequence)
    labels={}
    for i in range(1,sequence.Length()+1):
        label=sequence.Value(i);tag=TCollection_AsciiString();TDF_Tool.Entry_s(label,tag);labels[tag.ToCString()]=label
    return doc,tool,labels

def configured(tool,label,entry):
    included=entry.get('includedSourceLabels');excluded=entry.get('excludedSourceLabels',[])
    if not included and not excluded:return healed(tool.GetShape_s(label))
    sequence=TDF_LabelSequence();tool.GetComponents_s(label,sequence);bodies=[]
    for i in range(1,sequence.Length()+1):
        instance=sequence.Value(i);target=TDF_Label();assert tool.GetReferredShape_s(instance,target)
        tag=TCollection_AsciiString();TDF_Tool.Entry_s(target,tag);number=tag.ToCString().split(':')[-1]
        if number in excluded or (included and number not in included):continue
        shape=healed(tool.GetShape_s(target));tr=tool.GetLocation_s(instance).Transformation()
        matrix=[tr.Value(r,c) for r in range(1,4) for c in range(1,5)]+[0,0,0,1]
        bodies.append(transform(shape,matrix))
    return healed(compound(bodies))

def main():
    parser=argparse.ArgumentParser();parser.add_argument('sources',type=Path);parser.add_argument('references',type=Path);parser.add_argument('output',type=Path)
    parser.add_argument('--reviewed',type=Path,default=Path(__file__).with_name('reviewed_game_registration.json'))
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    entries=json.loads(args.reviewed.read_text());documents={};index={'version':1,'lengthUnit':'mm','parts':{}}
    for key,entry in entries.items():
        source=args.sources/entry['source']
        if hashlib.sha256(source.read_bytes()).hexdigest()!=entry['sourceSHA256']:raise ValueError('Reviewed source changed: '+key)
        if source not in documents:documents[source]=load(source)
        _,tool,labels=documents[source];shape=configured(tool,labels[entry['sourceLabel']],entry)
        shape=healed(transform(shape,entry['registration']));validate(shape,key,True)
        data=json.loads((args.references/(key+'.json')).read_text());stats=surface_check(shape,data,IDENTITY)
        if stats['surface_p95_mm']>entry['validation']['surface_p95_mm']+.02:raise ValueError('Registration changed: '+key)
        materials=face_materials(shape,data,IDENTITY);filename=hashlib.sha256(key.encode()).hexdigest()[:20]+'.brep'
        write_brep(shape,args.output/filename);validate(read_brep(args.output/filename),key+' serialized',True)
        index['parts'][key]=dict(entry,file=filename,faceMaterials=materials,validation=stats)
        (args.output/'index.json').write_text(json.dumps(index,indent=2)+'\n')
        print('Registered',key,flush=True)

if __name__=='__main__':main()
