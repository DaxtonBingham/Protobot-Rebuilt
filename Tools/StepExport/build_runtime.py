"""Bundle the isolated worker. A user's Python/CAD installation is never used."""
import argparse
import importlib.util
import importlib.metadata
from pathlib import Path
import shutil
import subprocess
import sys
import pefile

def dependencies(extension):
    site=extension.parent.parent
    folders=[site/'cadquery_ocp.libs',site/'vtk.libs']
    available={p.name.lower():p for folder in folders for p in folder.glob('*.dll')}
    pending=[extension];seen=set();result=[]
    while pending:
        path=pending.pop()
        pe=pefile.PE(str(path),fast_load=True)
        pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_IMPORT'],pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT']])
        for entry in list(getattr(pe,'DIRECTORY_ENTRY_IMPORT',[]))+list(getattr(pe,'DIRECTORY_ENTRY_DELAY_IMPORT',[])):
            name=entry.dll.decode().lower()
            if name in available and name not in seen:
                seen.add(name);dependency=available[name];pending.append(dependency);result.append(dependency)
        pe.close()
    return result

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--output',required=True,type=Path);parser.add_argument('--work',required=True,type=Path)
    args=parser.parse_args();here=Path(__file__).resolve().parent
    package=Path(importlib.util.find_spec('OCP').origin).parent
    extension=next(package.glob('*.pyd'));binaries=dependencies(extension)
    print('Bundling CAD worker with',len(binaries),'native dependencies;',round((extension.stat().st_size+sum(p.stat().st_size for p in binaries))/1e6,1),'MB',flush=True)
    command=[sys.executable,'-m','PyInstaller','--noconfirm','--onedir','--console','--name','Protobot CAD',
        '--distpath',str(args.output),'--workpath',str(args.work/'build'),'--specpath',str(args.work),
        '--hidden-import','OCP.OCP','--paths',str(here),'--exclude-module','cadquery','--exclude-module','numpy',
        '--exclude-module','vtkmodules','--exclude-module','tkinter']
    for binary in binaries:command.extend(['--add-binary',str(binary)+';'+binary.parent.name])
    command.append(str(here/'step_export.py'))
    subprocess.run(command,check=True)
    root=args.output/'Protobot CAD';licenses=root/'Licenses';licenses.mkdir(exist_ok=True)
    for name in ['cadquery-ocp','vtk','pyinstaller']:
        distribution=importlib.metadata.distribution(name)
        for item in distribution.files or []:
            if 'license' in str(item).lower() or 'copying' in str(item).lower():
                source=Path(distribution.locate_file(item))
                if source.is_file():
                    destination=licenses/name/str(item);destination.parent.mkdir(parents=True,exist_ok=True)
                    shutil.copy2(source,destination)
    shutil.copy2(Path(sys.base_prefix)/'LICENSE.txt',licenses/'Python-LICENSE.txt')
    extra=here/'licenses'
    if extra.exists():shutil.copytree(extra,licenses,dirs_exist_ok=True)
    (licenses/'Sources.txt').write_text('Protobot CAD export worker\n\nOpen CASCADE Technology 7.9.3: https://github.com/Open-Cascade-SAS/OCCT/tree/V7_9_3\nOCP bindings: https://github.com/CadQuery/OCP\nVTK: https://gitlab.kitware.com/vtk/vtk\nPython: https://www.python.org/\nPyInstaller: https://pyinstaller.org/\n\nCAD model provenance is recorded in Library/sources.json. VEX product geometry belongs to its respective rights holders.\n')
    for path in licenses.rglob('*'):
        if path.is_file():
            content=path.read_text(encoding='utf-8')
            path.write_text('\n'.join(line.rstrip() for line in content.splitlines()).rstrip()+'\n',encoding='utf-8')
    print('Built standalone worker at',root,flush=True)

if __name__=='__main__':main()
