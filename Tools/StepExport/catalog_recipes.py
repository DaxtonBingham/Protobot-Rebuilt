"""Offline corrections for catalog configurations of the original CAD assemblies.

These operations position/cut the manufacturer's BReps. Display triangles are
registration references only; they are never promoted to CAD faces.
"""
import numpy as np
from OCP.BRepAlgoAPI import BRepAlgoAPI_Fuse
from OCP.BRepPrimAPI import BRepPrimAPI_MakePrism
from OCP.gp import gp_Vec
from OCP.TopAbs import TopAbs_FACE
from OCP.ShapeFix import ShapeFix_Shape
from OCP.BRepCheck import BRepCheck_Analyzer
from OCP.BRepBuilderAPI import BRepBuilderAPI_Sewing
from OCP.BRepGProp import BRepGProp
from OCP.GProp import GProp_GProps
from kernel import (compound, children, box_crop, transform, translate, validate,
                    solidify, read_brep, write_brep, IDENTITY)

def healed(shape):
    shape=solidify(shape)
    try:return validate(shape,'source',True)
    except ValueError:pass
    fix=ShapeFix_Shape(shape);fix.SetPrecision(1e-6);fix.SetMaxTolerance(.01)
    fix.Perform()
    result=solidify(fix.Shape())
    if BRepCheck_Analyzer(result).IsValid():return result
    # Some original field CAD contains cylindrical seam slivers with exactly
    # zero area. They make an otherwise closed shell unorientable. Remove only
    # invalid zero-area faces, then sew the original analytic faces together.
    faces=list(children(result,TopAbs_FACE));bad=[]
    for face in faces:
        if BRepCheck_Analyzer(face).IsValid():continue
        props=GProp_GProps();BRepGProp.SurfaceProperties_s(face,props)
        if abs(props.Mass())>1e-8:return result
        bad.append(face)
    if bad:
        sewing=BRepBuilderAPI_Sewing(.0001)
        for face in faces:
            if not any(face.IsSame(f) for f in bad):sewing.Add(face)
        sewing.Perform();candidate=solidify(sewing.SewedShape())
        if BRepCheck_Analyzer(candidate).IsValid():return candidate
    return result

def rigid_catalog_matrix(matrix):
    """Remove single-precision rotation noise from Unity field node captures.

    These catalog placements are rigid. Feeding quaternion rounding noise to a
    general affine transform needlessly turns analytic surfaces into splines.
    Reflections are retained, and genuine scale/shear is rejected for review.
    """
    result=np.array(matrix,dtype=float).reshape(4,4).copy()
    u,s,vh=np.linalg.svd(result[:3,:3])
    if np.max(np.abs(s-1.))>1e-5:raise ValueError('Field node has non-rigid scale or shear.')
    result[:3,:3]=u@vh
    return result.reshape(-1).tolist()

def write_verified_template(shape,materials,path,label):
    """Check serialized topology, including repairs of relocated CAD p-curves.

    Surface repair may reorder faces. Match unchanged face centers and areas
    before carrying their colors across, rather than assuming their order.
    """
    from scipy.spatial import cKDTree
    def signature(body):
        result=[]
        for face in children(body,TopAbs_FACE):
            props=GProp_GProps();BRepGProp.SurfaceProperties_s(face,props)
            center=props.CentreOfMass();result.append([center.X(),center.Y(),center.Z(),props.Mass()])
        return np.array(result)
    for attempt in range(3):
        write_brep(shape,path);loaded=read_brep(path)
        if BRepCheck_Analyzer(loaded).IsValid():return materials
        old=signature(loaded);fixed=healed(loaded);validate(fixed,label+' repaired',True)
        new=signature(fixed)
        if len(old)!=len(new):raise ValueError(label+': repair changed face count; review material mapping')
        distances,indices=cKDTree(old[:,:3]).query(new[:,:3])
        if max(distances)>1e-4 or np.max(abs(old[indices,3]-new[:,3]))>1e-4:
            raise ValueError(label+': repair changed face geometry; review required')
        if materials:materials=[materials[int(i)] for i in indices]
        shape=fixed
    raise ValueError(label+': CAD topology did not survive serialization')

def extend_rod(shape,plane,length):
    import cadquery as cq
    left=box_crop(shape,[-1000,-1000,-1000],[plane,1000,1000])
    right=box_crop(shape,[plane,-1000,-1000],[1000,1000,1000])
    caps=[]
    for face in children(left,TopAbs_FACE):
        b=cq.Shape.cast(face).BoundingBox()
        if abs(b.xmin-plane)<1e-5 and abs(b.xmax-plane)<1e-5:caps.append(face)
    if len(caps)!=1:raise ValueError('The exposed piston rod must have one section.')
    bridge=BRepPrimAPI_MakePrism(caps[0],gp_Vec(length,0,0)).Shape()
    operation=BRepAlgoAPI_Fuse(left,bridge);operation.Build()
    operation=BRepAlgoAPI_Fuse(operation.Shape(),translate(right,x=length));operation.Build()
    operation.SimplifyResult(True,True)
    return validate(healed(operation.Shape()),'extended piston')

def subset(data,indices):
    result=dict(data);result['parts']=[dict(data['parts'][0],surfaces=[data['parts'][0]['surfaces'][i] for i in indices])]
    return result

def prepare_configuration(shape,key,data,files):
    from build_library import read_step,reference,cad_vertices,register,register_surfaces,reference_mesh,surface_check
    dependencies=[]
    def load(token):
        matches=[p for p in files if token.lower() in p.name.lower()]
        matches.sort(key=lambda p:(not 'TlorschCode' in str(p),len(str(p))))
        if not matches:raise ValueError('Missing CAD assembly component: '+token)
        path=matches[0];dependencies.append(path)
        return solidify(read_step(path)[0])
    def aligned(body,target):
        matrix,_=register_surfaces(body,target)
        return transform(body,matrix)
    if key=='SNSR-Potentiometer-V5':
        # The Unity asset ends at the strain relief; the CAD includes a cable.
        shape=box_crop(shape,[-1000,-1000,-1000],[1000,12.625,1000])
    elif key=='SWCH-Bumper-V5':
        shape=box_crop(shape,[-25.278,-1000,-1000],[1000,1000,1000])
    elif key.startswith('PNMT-') and key.endswith('-Extended'):
        stroke=int(key.split('-')[1][:-2])
        shape=extend_rod(shape,stroke+72.,stroke)
    elif key.startswith('FWHL-With Adapters-'):
        surfaces=data['parts'][0]['surfaces']
        wheel_index=max(range(len(surfaces)),key=lambda i:surfaces[i]['color']['r'])
        wheel=aligned(shape,subset(data,[wheel_index]))
        bodies=[wheel]
        for i in range(len(surfaces)):
            if i==wheel_index:continue
            mesh=reference_mesh(subset(data,[i]))
            mesh.remove_unreferenced_vertices()
            # Each side is a complete CAD subassembly. Splitting by mesh
            # connectivity would incorrectly split UV seams and coincident faces.
            adapter=load('217-8079' if np.ptp(mesh.vertices[:,0])>30 else '217-5950')
            for sign in (-1,1):
                selected=np.all(mesh.vertices[mesh.faces][:,:,2]*sign>=-.0001,axis=1)
                segment=mesh.submesh([np.flatnonzero(selected)],append=True)
                ref={'parts':[{'surfaces':[{'geometry':0,'matrix':IDENTITY}]}],
                     'geometry':[{'vertices':[dict(zip(('x','y','z'),v/25.4)) for v in segment.vertices],
                                  'triangles':segment.faces.reshape(-1).tolist()}]}
                fitted=aligned(adapter,ref)
                score=surface_check(fitted,ref,IDENTITY)['surface_p95_mm']
                if score>.25:raise ValueError('No matching original CAD adapter (deviation %.3f mm)'%score)
                bodies.append(fitted)
        shape=compound(bodies)
        return shape,dependencies,True
    elif key=='TANK-V5':
        reservoir=aligned(shape,subset(data,[0,3,4,5]))
        valve=aligned(load('Schrader Valve.step'),subset(data,[1,2]))
        return compound([reservoir,valve]),dependencies,True
    elif key.startswith('MCMS-Worm and Wheel-'):
        bodies=list(children(shape))
        if len(bodies)!=2:raise ValueError('Expected worm and wheel CAD pair.')
        # The worm is the narrow helix along the source Y axis. The wheel is
        # the second body, whose 27.5 mm diameter is in the Y/Z plane.
        shape=bodies[0 if key.endswith('-Worm') else 1]
    elif key=='UCHL-2x2-35':
        target=reference(data);target=target[(target[:,0]>=-222.2501)&(target[:,0]<=31.7501)]
        target[:,0]+=95.25
        matrix,_=register(cad_vertices(shape),target)
        master=transform(shape,matrix)
        cell=box_crop(translate(master,x=95.25),[-31.75,-1000,-1000],[31.75,1000,1000])
        parts=[translate(cell,x=i*63.5) for i in range(-3,4)]
        shape=parts[0]
        for other in parts[1:]:
            op=BRepAlgoAPI_Fuse(shape,other);op.Build();op.SimplifyResult(True,True);shape=op.Shape()
        return validate(shape,key),dependencies,True
    return shape,dependencies,False
