"""Developer tool: register source STEP solids against the original Unity catalog.

Registration is offline, never on the user's export path. Review the generated
alignment report before releasing an updated library. No display mesh becomes a
CAD solid: the meshes are used only as registration/verification references.
"""
import argparse, hashlib, itertools, json, math, sys
from pathlib import Path
import numpy as np
from scipy.spatial import cKDTree
import cadquery as cq
import trimesh
from OCP.STEPControl import STEPControl_Reader
from OCP.IFSelect import IFSelect_RetDone
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.BRepTools import BRepTools
from OCP.BRep import BRep_Tool
from OCP.TopLoc import TopLoc_Location
from OCP.TopAbs import TopAbs_FACE
from OCP.TopoDS import TopoDS
from catalog_sources import mappings
from catalog_recipes import prepare_configuration, healed
from kernel import transform, read_brep, write_brep, validate, solidify, box_crop, children

def reference(data):
    vertices=[]
    for surface in data['parts'][0]['surfaces']:
        geometry=data['geometry'][surface['geometry']]
        v=np.array([[p['x'],p['y'],p['z'],1.] for p in geometry['vertices']])
        v=v[np.unique(geometry['triangles'])]
        v=(v @ np.array(surface['matrix']).reshape(4,4).T)[:,:3]*25.4
        vertices.extend(v)
    return np.unique(np.round(vertices,5),axis=0)

def tessellate(shape,deflection=.08):
    # CAD files contain tiny fillets. Relative meshing makes them unnecessarily
    # dense and can consume gigabytes during catalog registration. Absolute mm
    # deflection bounds the comparison error without changing exported BReps.
    BRepTools.Clean_s(shape)
    BRepMesh_IncrementalMesh(shape,deflection,False,.3,True)
    return cq.Shape.cast(shape).tessellate(100,1)

def cad_vertices(shape):
    vertices,triangles=tessellate(shape)
    return np.array([p.toTuple() for p in vertices])

def reference_mesh(data):
    vertices=[];triangles=[]
    for surface in data['parts'][0]['surfaces']:
        geometry=data['geometry'][surface['geometry']]
        v=np.array([[p['x'],p['y'],p['z'],1.] for p in geometry['vertices']])
        v=(v @ np.array(surface['matrix']).reshape(4,4).T)[:,:3]*25.4
        triangles.extend(np.array(geometry['triangles']).reshape(-1,3)+len(vertices))
        vertices.extend(v)
    return trimesh.Trimesh(vertices=np.array(vertices),faces=np.array(triangles),process=False)

def surface_check(shape,data,matrix):
    vertices,triangles=tessellate(shape,.025)
    v=np.array([p.toTuple() for p in vertices]);m=np.array(matrix).reshape(4,4)
    v=v@m[:3,:3].T+m[:3,3]
    cad=trimesh.Trimesh(vertices=v,faces=triangles,process=False)
    target=reference_mesh(data)
    target.remove_unreferenced_vertices()
    points=target.vertices[np.linspace(0,len(target.vertices)-1,min(len(target.vertices),1000),dtype=int)]
    _,distances,_=trimesh.proximity.closest_point(cad,points)
    return {'surface_p95_mm':float(np.percentile(distances,95)), 'surface_max_mm':float(max(distances))}

def prepare_shape(shape,key):
    bounds=cq.Shape.cast(shape).BoundingBox()
    lower=np.array([bounds.xmin,bounds.ymin,bounds.zmin]);upper=np.array([bounds.xmax,bounds.ymax,bounds.zmax])
    if key=='CHAIN-Pitch6p35':
        # Protobot's .250-inch display link is the .148-inch link scaled to
        # that pitch. Apply the same uniform scale to its analytic CAD source.
        # This is an app reference shape, not a separate manufacturer SKU.
        matrix=np.eye(4);matrix[:3,:3]*=.250/.148
        shape=transform(shape,matrix.reshape(-1).tolist())
    elif key.startswith('SHFT-'):
        # The Unity one-inch master is the full 12-inch shaft compressed along
        # its length; placement applies the user's chosen length as object scale.
        axis=int(np.argmax(upper-lower));matrix=np.eye(4)
        matrix[axis,axis]=25.4/(upper[axis]-lower[axis])
        shape=transform(shape,matrix.reshape(-1).tolist())
    elif key.startswith('PLTE-'):
        # The central five-hole section is a complete period of the VEX plate.
        axis=int(np.argmax(upper-lower));center=(lower[axis]+upper[axis])/2
        lower-=1;upper+=1;lower[axis]=center-31.75;upper[axis]=center+31.75
        shape=box_crop(shape,lower,upper)
    return shape

def face_materials(shape,data,matrix):
    surfaces=data['parts'][0]['surfaces']
    if len(surfaces)<2:return []
    points=[]
    for face in children(shape,TopAbs_FACE):
        loc=TopLoc_Location();poly=BRep_Tool.Triangulation_s(TopoDS.Face_s(face),loc)
        if poly is None or not poly.NbTriangles():
            center=cq.Shape.cast(face).Center();points.append(center.toTuple());continue
        triangle=poly.Triangle((poly.NbTriangles()+1)//2)
        vertices=[poly.Node(triangle.Value(i)).Transformed(loc.Transformation()) for i in (1,2,3)]
        points.append([sum(getattr(p,axis)() for p in vertices)/3 for axis in ('X','Y','Z')])
    m=np.array(matrix).reshape(4,4);points=np.array(points)@m[:3,:3].T+m[:3,3]
    _,_,triangles=trimesh.proximity.closest_point(reference_mesh(data),points)
    boundaries=np.cumsum([len(data['geometry'][s['geometry']]['triangles'])//3 for s in surfaces])
    return np.searchsorted(boundaries,triangles,side='right').tolist()

def read_step(path):
    reader=STEPControl_Reader()
    if reader.ReadFile(str(path))!=IFSelect_RetDone:raise ValueError('Cannot read STEP source.')
    reader.TransferRoots()
    return reader.OneShape(),reader.NbRootsForTransfer()

def register(source,target):
    source=np.unique(np.round(source,5),axis=0)
    source_center=(source.min(0)+source.max(0))/2
    target_center=(target.min(0)+target.max(0))/2
    target_tree=cKDTree(target)
    # Deterministic samples; retain extrema in the full trees for verification.
    sample=source[np.linspace(0,len(source)-1,min(len(source),4000),dtype=int)]
    candidates=[]
    for permutation in itertools.permutations(range(3)):
        for signs in itertools.product([-1,1],repeat=3):
            rotation=np.eye(3)[list(permutation)]*np.array(signs)[:,None]
            translation=target_center-rotation@source_center
            moved=sample@rotation.T+translation
            distances,_=target_tree.query(moved,workers=1)
            score=np.mean(np.minimum(distances,np.percentile(distances,90))**2)
            candidates.append((score,rotation,translation))
    candidates.sort(key=lambda x:x[0])
    best=None
    for initial,rotation,translation in candidates[:4]:
        exact_rotation,exact_translation=rotation.copy(),translation.copy()
        prior=math.inf
        for iteration in range(35):
            moved=sample@rotation.T+translation
            distances,idx=target_tree.query(moved,workers=1)
            mask=distances<=np.percentile(distances,90)
            a,b=moved[mask],target[idx[mask]]
            ac,bc=a.mean(0),b.mean(0)
            u,_,vh=np.linalg.svd((a-ac).T@(b-bc))
            correction=vh.T@u.T
            if np.linalg.det(correction)<0:
                vh[-1]*=-1;correction=vh.T@u.T
            shift=bc-correction@ac
            rotation=correction@rotation
            translation=correction@translation+shift
            score=np.mean(distances[mask]**2)
            if abs(prior-score)<1e-10:break
            prior=score
        # Prefer exact orthogonal registration when tessellation causes a tiny
        # artificial ICP tilt. Never stretch a CAD source to force a match.
        if np.linalg.norm(rotation-exact_rotation)<.008 and np.linalg.norm(translation-exact_translation)<.08:
            rotation,translation=exact_rotation,exact_translation
        moved=source@rotation.T+translation
        forward,_=target_tree.query(moved,workers=1)
        reverse,_=cKDTree(moved).query(target,workers=1)
        score=np.mean(np.minimum(reverse,np.percentile(reverse,95))**2)
        if best is None or score<best[0]:
            best=(score,rotation.copy(),translation.copy(),forward,reverse)
    _,rotation,translation,forward,reverse=best
    matrix=np.eye(4);matrix[:3,:3]=rotation;matrix[:3,3]=translation
    moved=source@rotation.T+translation
    stats=dict(referenceVertices=len(target),cadVertices=len(source),
               referenceToCAD_p95_mm=float(np.percentile(reverse,95)),
               cadToReference_p95_mm=float(np.percentile(forward,95)),
               extentError_mm=np.abs(np.ptp(moved,axis=0)-np.ptp(target,axis=0)).tolist(),
               centerError_mm=(((moved.min(0)+moved.max(0))-(target.min(0)+target.max(0)))/2).tolist())
    return matrix.reshape(-1).tolist(),stats

def register_surfaces(shape,data):
    """Resolve symmetric/low-detail display meshes using actual CAD surfaces.

    A nearest-vertex fit can tilt large flat faces toward densely tessellated
    screw holes. Evaluate signed orthogonal placements on triangle surfaces;
    keep ICP only when it measurably improves the surface fit.
    """
    vertices,triangles=tessellate(shape,.04)
    source=np.array([v.toTuple() for v in vertices]);target=reference(data)
    matrix,stats=register(source,target);candidates=[np.array(matrix).reshape(4,4)]
    sc=(source.min(0)+source.max(0))/2;tc=(target.min(0)+target.max(0))/2
    tree=cKDTree(source)
    points=target[np.linspace(0,len(target)-1,min(len(target),800),dtype=int)]
    ranked=[]
    for permutation in itertools.permutations(range(3)):
        for signs in itertools.product([-1,1],repeat=3):
            r=np.eye(3)[list(permutation)]*np.array(signs)[:,None];t=tc-r@sc
            distances,_=tree.query((points-t)@r)
            score=np.mean(np.minimum(distances,np.percentile(distances,95))**2)
            m=np.eye(4);m[:3,:3]=r;m[:3,3]=t;ranked.append((score,m))
    candidates.extend(m for _,m in sorted(ranked,key=lambda x:x[0])[:16])
    cad=trimesh.Trimesh(vertices=source,faces=triangles,process=False)
    best=None
    for m in candidates:
        _,distances,_=trimesh.proximity.closest_point(cad,(points-m[:3,3])@m[:3,:3])
        score=np.mean(np.minimum(distances,np.percentile(distances,98))**2)
        if best is None or score<best[0]:best=(score,m)
    m=best[1];moved=source@m[:3,:3].T+m[:3,3]
    stats['extentError_mm']=np.abs(np.ptp(moved,axis=0)-np.ptp(target,axis=0)).tolist()
    stats['centerError_mm']=(((moved.min(0)+moved.max(0))-(target.min(0)+target.max(0)))/2).tolist()
    return m.reshape(-1).tolist(),stats

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('sources',type=Path);parser.add_argument('catalog',type=Path);parser.add_argument('output',type=Path)
    parser.add_argument('--only',nargs='*')
    parser.add_argument('--reviewed',type=Path,help='Use previously reviewed source hashes and rigid placements.')
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    files=[p for p in args.sources.rglob('*') if p.suffix.lower() in ('.step','.stp')]
    source_map=mappings(files)
    reviewed=json.loads(args.reviewed.read_text()) if args.reviewed else {}
    index_path=args.output/'index.json'
    index=json.loads(index_path.read_text()) if index_path.exists() else {'version':1,'parts':{}}
    for path in sorted(args.catalog.glob('*.json')):
        data=json.loads(path.read_text());key=data['parts'][0]['catalogId']
        if args.only and key not in args.only:continue
        if key in index['parts']:continue
        approved=reviewed.get(key)
        source=args.sources/approved['source'] if approved else source_map.get(key)
        if source is None:continue
        try:
            if approved and hashlib.sha256(source.read_bytes()).hexdigest()!=approved['sourceSHA256']:
                raise ValueError('Reviewed source hash changed.')
            shape,roots=read_step(source)
            shape=healed(shape)
            shape=prepare_shape(shape,key)
            shape,dependencies,already_aligned=prepare_configuration(shape,key,data,files)
            validate(shape,key,allow_surfaces=True)
            target=reference(data)
            matrix,stats=(approved['registration'],{}) if approved else register_surfaces(shape,data)
            if already_aligned:matrix=np.eye(4).reshape(-1).tolist()
            stats.update(surface_check(shape,data,matrix))
            if not approved and key.startswith(('GEAR-','SPKT-','OMNI-','TWHL-')) and stats['surface_p95_mm']>.25:
                from axial_registration import register_axial
                candidate=register_axial(shape,data,matrix)
                comparison=surface_check(shape,data,candidate)
                if comparison['surface_p95_mm']<stats['surface_p95_mm']:
                    matrix=candidate;stats.update(comparison)
            material_indices=face_materials(shape,data,matrix)
            aligned=transform(shape,matrix);validate(aligned,key,allow_surfaces=True)
            filename=hashlib.sha256(key.encode()).hexdigest()[:20]+'.brep'
            write_brep(aligned,args.output/filename)
            validate(read_brep(args.output/filename),key+' serialized',allow_surfaces=True)
            index['parts'][key]={'file':filename,'source':str(source.relative_to(args.sources)),
                'sourceSHA256':hashlib.sha256(source.read_bytes()).hexdigest(),
                'registration':matrix,'sourceRoots':roots,'faceMaterials':material_indices,'validation':stats,'referenceBounds':[target.min(0).tolist(),target.max(0).tolist()]}
            index['parts'][key]['dependencies']=[{'source':str(p.relative_to(args.sources)),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in dependencies]
            if key=='CHAIN-Pitch6p35':index['parts'][key]['notes']='App reference link: the original .148-inch CAD link uniformly scaled to .250-inch pitch, matching Protobot. Not a separate manufacturer SKU.'
            if approved and approved.get('notes'):index['parts'][key]['notes']=approved['notes']
            index_path.write_text(json.dumps(index,indent=2))
            print(key,'surface p95',round(stats['surface_p95_mm'],3), flush=True)
        except Exception as ex:
            print('FAILED',key,str(ex),flush=True)
    print('Registered',len(index['parts']),'solid templates',flush=True)

if __name__=='__main__':main()
