"""Extract original named field CAD definitions for offline library builds."""
from pathlib import Path
import json,sys
from kernel import solidify,write_brep
from OCP.STEPCAFControl import STEPCAFControl_Reader
from OCP.TDocStd import TDocStd_Document
from OCP.TCollection import TCollection_ExtendedString,TCollection_AsciiString
from OCP.XCAFDoc import XCAFDoc_DocumentTool
from OCP.TDF import TDF_LabelSequence,TDF_Tool
from OCP.TDataStd import TDataStd_Name
from OCP.IFSelect import IFSelect_RetDone
from OCP.Bnd import Bnd_Box
from OCP.BRepBndLib import BRepBndLib

if len(sys.argv)!=3:raise SystemExit('Usage: extract_field_cad.py <source.step> <output-directory>')
path=Path(sys.argv[1]);out=Path(sys.argv[2]);out.mkdir(parents=True,exist_ok=True)

document=TDocStd_Document(TCollection_ExtendedString('XmlXCAF'))
reader=STEPCAFControl_Reader();reader.SetNameMode(True)
assert reader.ReadFile(str(path))==IFSelect_RetDone
assert reader.Transfer(document)
tool=XCAFDoc_DocumentTool.ShapeTool_s(document.Main())
labels=TDF_LabelSequence();tool.GetShapes(labels)
print('Definitions',labels.Length(),flush=True)
entries=[]
for i in range(1,labels.Length()+1):
    label=labels.Value(i);attribute=TDataStd_Name();name=''
    if label.FindAttribute(TDataStd_Name.GetID_s(),attribute):name=attribute.Get().ToExtString()
    assembly=tool.IsAssembly_s(label)
    if assembly:continue
    shape=tool.GetShape_s(label)
    bb=Bnd_Box();BRepBndLib.AddOptimal_s(shape,bb,False,False)
    if bb.IsVoid():
        print('SKIP EMPTY',i,name,flush=True)
        continue
    tag=TCollection_AsciiString();TDF_Tool.Entry_s(label,tag);tag=tag.ToCString()
    assembly=tool.IsAssembly_s(label)
    entries.append(dict(i=i,name=name,label=tag,assembly=assembly,bounds=list(bb.Get()),file=str(i)+'.brep'))
    write_brep(solidify(shape),out/(str(i)+'.brep'))
    print(i,name,'assembly' if assembly else 'part',tuple(round(v,2) for v in entries[-1]['bounds']),flush=True)
(out/'definitions.json').write_text(json.dumps(entries,indent=2))
print('Field CAD extracted',flush=True)
