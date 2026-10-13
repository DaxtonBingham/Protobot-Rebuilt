"""Build original Override CAD with native catalog placements and colors."""
from pathlib import Path
import sys,json,re,hashlib,gzip
import numpy as np
from build_library import surface_check,face_materials,reference,tessellate
from kernel import *
from catalog_recipes import healed,rigid_catalog_matrix,write_verified_template
from OCP.TopAbs import TopAbs_FACE

import argparse
parser=argparse.ArgumentParser(description='Build reviewed Override templates from original CAD and app catalog captures.')
parser.add_argument('sources',type=Path)
parser.add_argument('extracted',type=Path)
parser.add_argument('catalog',type=Path)
parser.add_argument('references',type=Path)
parser.add_argument('output',type=Path)
parser.add_argument('--fields',action='store_true')
args=parser.parse_args()
catalog=json.loads(args.catalog.read_text())
out=args.output;out.mkdir(parents=True,exist_ok=True)
meshout=out/'meshes';meshout.mkdir(exist_ok=True)
meshes={m['id']:m for m in catalog['meshes']}
report_path=out/'meshes.json'
reports=json.loads(report_path.read_text()) if report_path.exists() else {}
source='VEX/276-9250-000 (2026-04-26).STEP'
source_sha=hashlib.sha256((args.sources/source).read_bytes()).hexdigest()

for key,mesh in meshes.items():
    number=re.match(r'0:1:1:(\d+)',key)[1]
    if key in reports:continue
    shape=healed(read_brep(args.extracted/(number+'.brep')))
    validate(shape,key,True)
    # The app trims the foam tiles to their square interior so they tile without
    # rendering the overlapping interlocking teeth. Preserve this configuration.
    if key=='0:1:1:32_field':shape=box_crop(shape,[-298.958,-1,-298.958],[298.958,17,298.958])
    verts=np.array(mesh['vertices']).reshape(-1,3)
    data={'parts':[{'surfaces':[dict(geometry=i,matrix=IDENTITY,color=dict(zip('rgba',g['color']))) for i,g in enumerate(mesh['groups'])]}],
          'geometry':[dict(vertices=[dict(zip('xyz',v)) for v in verts],triangles=g['triangles']) for g in mesh['groups']]}
    stats=surface_check(shape,data,IDENTITY)
    indices=face_materials(shape,data,IDENTITY)
    if not indices:indices=[0]*len(list(children(shape,TopAbs_FACE)))
    name=hashlib.sha256(key.encode()).hexdigest()[:20]+'.brep'
    write_brep(shape,meshout/name)
    reports[key]=dict(file=name,validation=stats,faceMaterials=indices)
    report_path.write_text(json.dumps(reports,indent=2))
    print('MESH',key,'p95',round(stats['surface_p95_mm'],5),'max',round(stats['surface_max_mm'],5),flush=True)

index_path=out/'index.json'
index=json.loads(index_path.read_text()) if index_path.exists() else dict(version=1,lengthUnit='mm',parts={},unsupported={})
shape_cache={}
for entry in catalog['entries']:
    if entry['id']=='OVFL' and not args.fields:continue
    for variant in entry['variants']:
        key=entry['id']+('-'+variant['value'] if entry['parameter'] else '')
        if key in index['parts']:continue
        refpath=args.references/(key+'.json')
        data=json.loads(refpath.read_text())
        palette=np.array([[s['color'][c] for c in 'rgba'] for s in data['parts'][0]['surfaces']])
        bodies=[];material_indices=[];errors=[]
        for node in variant['nodes']:
            key_mesh=node['mesh'];report=reports[key_mesh];mesh=meshes[key_mesh]
            if key_mesh not in shape_cache:
                shape_cache[key_mesh]=read_brep(meshout/report['file'])
                validate(shape_cache[key_mesh],key_mesh,True)
            shape=shape_cache[key_mesh]
            matrix=np.eye(4);matrix[:3,:4]=np.array(node['matrix']).reshape(3,4)
            matrix[:3,3]=(matrix[:3,3]-variant['offset'])*MM
            moved=transform(shape,rigid_catalog_matrix(matrix),copy=False)
            if np.linalg.det(matrix[:3,:3])<0:
                try:validate(moved,key_mesh,True)
                except ValueError:
                    moved=healed(moved)
                    validate(moved,key_mesh,True)
            bodies.append(moved)
            material_map=[]
            for group in mesh['groups']:
                distance=np.max(abs(palette-np.array(group['color'])),axis=1);chosen=int(np.argmin(distance))
                if distance[chosen]>1e-4:raise ValueError('Unmatched palette '+key+' '+key_mesh)
                material_map.append(chosen)
            if len(report['faceMaterials'])!=len(list(children(moved,TopAbs_FACE))):
                tessellate(moved,.025)
                material_indices.extend(face_materials(moved,data,IDENTITY))
            else:material_indices.extend(material_map[i] for i in report['faceMaterials'])
            errors.append(report['validation']['surface_p95_mm'])
        combined=compound(bodies);validate(combined,key,True)
        assert len(material_indices)==len(list(children(combined,TopAbs_FACE)))
        name=hashlib.sha256(key.encode()).hexdigest()[:20]+'.brep'
        material_indices=write_verified_template(combined,material_indices,out/name,key)
        # Per-source checks remain auditable; assembly comparison checks placement.
        stats=surface_check(combined,data,IDENTITY) if entry['id']!='OVFL' else {'component_p95_max_mm':max(errors)}
        index['parts'][key]=dict(file=name,source=source,sourceSHA256=source_sha,sourceRoots=1,
          faceMaterials=material_indices,validation=stats,registration=IDENTITY,
          recipe='Override catalog node transforms and original manufacturer BReps',nodeCount=len(bodies))
        index_path.write_text(json.dumps(index,indent=2))
        print('ASSEMBLY',key,stats,'nodes',len(bodies),flush=True)
