"""Solid CAD operations used by the isolated STEP export worker (millimetres)."""
import math
import gzip
import tempfile
from pathlib import Path
from OCP.BRep import BRep_Builder
from OCP.BRepAlgoAPI import BRepAlgoAPI_Cut, BRepAlgoAPI_Common, BRepAlgoAPI_Fuse
from OCP.BRepBuilderAPI import (BRepBuilderAPI_MakeEdge, BRepBuilderAPI_MakeWire,
    BRepBuilderAPI_MakeFace, BRepBuilderAPI_Transform, BRepBuilderAPI_GTransform)
from OCP.BRepCheck import BRepCheck_Analyzer, BRepCheck_Shell, BRepCheck_NoError
from OCP.BRepGProp import BRepGProp
from OCP.BRepPrimAPI import BRepPrimAPI_MakePrism, BRepPrimAPI_MakeBox
from OCP.BRepTools import BRepTools
from OCP.Geom import Geom_BezierCurve, Geom_Ellipse
from OCP.GProp import GProp_GProps
from OCP.gp import gp_Pnt, gp_Vec, gp_Trsf, gp_GTrsf, gp_Ax2, gp_Dir, gp_Circ
from OCP.TColgp import TColgp_Array1OfPnt
from OCP.TopoDS import TopoDS, TopoDS_Shape, TopoDS_Compound, TopoDS_Iterator
from OCP.TopAbs import TopAbs_SOLID, TopAbs_FACE, TopAbs_SHELL, TopAbs_COMPOUND, TopAbs_COMPSOLID
from OCP.TopExp import TopExp_Explorer
from OCP.TopTools import TopTools_ListOfShape
from OCP.TopLoc import TopLoc_Location
from OCP.ShapeFix import ShapeFix_Face, ShapeFix_Solid

MM = 25.4
IDENTITY = [1.,0.,0.,0., 0.,1.,0.,0., 0.,0.,1.,0., 0.,0.,0.,1.]

def compound(shapes):
    builder, result = BRep_Builder(), TopoDS_Compound()
    builder.MakeCompound(result)
    for shape in shapes:
        builder.Add(result, shape)
    return result

def children(shape, kind=TopAbs_SOLID):
    iterator = TopExp_Explorer(shape, kind)
    while iterator.More():
        yield iterator.Current()
        iterator.Next()

def volume(shape):
    props = GProp_GProps()
    BRepGProp.VolumeProperties_s(shape, props, 1e-9)
    return props.Mass()

def validate(shape, label, allow_surfaces=False):
    solids = list(children(shape))
    if (not solids and not allow_surfaces) or not BRepCheck_Analyzer(shape).IsValid():
        raise ValueError(label + ': CAD geometry is not a valid closed solid.')
    # Rigid placements share the same geometry and signed volume. A field may
    # repeat a detailed pin hundreds of times; integrate each underlying solid
    # once. IsEqual also checks orientation, so reversed bodies still fail.
    checked={}
    for solid in solids:
        local=solid.Located(TopLoc_Location());bucket=checked.setdefault(hash(local),[])
        if any(local.IsEqual(previous) for previous in bucket):continue
        if volume(local)<=1e-8:raise ValueError(label + ': CAD geometry is not a valid closed solid.')
        bucket.append(local)
    return shape

def solidify(shape):
    """Close already watertight source shells; retain genuine CAD sheet bodies.

    Manufacturer models sometimes use zero-thickness screen/label surfaces.
    Inventing a thickness or silently removing those faces would change the model.
    """
    kind = shape.ShapeType()
    if kind in (TopAbs_COMPOUND, TopAbs_COMPSOLID):
        iterator = TopoDS_Iterator(shape)
        result = []
        while iterator.More():
            result.append(solidify(iterator.Value()))
            iterator.Next()
        return compound(result)
    if kind == TopAbs_SHELL:
        shell = TopoDS.Shell_s(shape)
        if BRepCheck_Shell(shell).Closed() == BRepCheck_NoError:
            solid = ShapeFix_Solid().SolidFromShell(shell)
            if BRepCheck_Analyzer(solid).IsValid() and volume(solid) > 1e-8:
                return solid
    return shape

def read_brep(path):
    if str(path).endswith('.gz'):
        # Templates are compressed on disk; OCCT receives the original analytic
        # BRep, never a display mesh. Decompress only when this part is exported.
        with tempfile.TemporaryDirectory(prefix='protobot-cad-') as folder:
            source=Path(folder)/'part.brep'
            with gzip.open(path,'rb') as data:source.write_bytes(data.read())
            return read_brep(source)
    shape = TopoDS_Shape()
    if not BRepTools.Read_s(shape, str(path), BRep_Builder()):
        raise ValueError('Cannot read CAD template: ' + str(path))
    return shape

def write_brep(shape, path):
    # Rendering triangulations are not CAD data and can dwarf the analytic model.
    BRepTools.Clean_s(shape)
    if not BRepTools.Write_s(shape, str(path)):
        raise OSError('Cannot write CAD template: ' + str(path))

def transform(shape, matrix, copy=True):
    # GTransform preserves nonuniform scale, reflection and parent-induced shear.
    # Prefer the rigid/uniform path to preserve analytic cylinders and circles.
    cols = [[matrix[r*4+c] for r in range(3)] for c in range(3)]
    lengths = [sum(v*v for v in col) for col in cols]
    orthogonal = all(abs(sum(cols[a][r]*cols[b][r] for r in range(3))) < 1e-9 for a,b in [(0,1),(0,2),(1,2)])
    if orthogonal and max(lengths)-min(lengths) < 1e-9:
        trsf = gp_Trsf()
        trsf.SetValues(*[matrix[r*4+c] for r in range(3) for c in range(4)])
        # Offline field builds can share original geometry for rigid placements.
        # Runtime definitions retain independent topology/material ownership.
        # OCCT copies geometry for reflections/scaling regardless of this flag.
        return BRepBuilderAPI_Transform(shape, trsf, copy).Shape()
    trsf = gp_GTrsf()
    for r in range(3):
        for c in range(4):
            trsf.SetValue(r+1, c+1, matrix[r*4+c])
    return BRepBuilderAPI_GTransform(shape, trsf, True).Shape()

def translate(shape, x=0., y=0., z=0.):
    matrix = IDENTITY.copy()
    matrix[3], matrix[7], matrix[11] = x,y,z
    return transform(shape,matrix)

def point(value, z):
    return gp_Pnt(value['x']*MM, value['y']*MM, z)

def sketch_wire(loop, z):
    anchors = [a for a in loop['anchors'] if a is not None]
    if len(anchors) < 3:
        raise ValueError('A Poly Maker outline needs at least three anchors.')
    kinds = loop.get('segmentKinds', [])
    if len(kinds) != len(anchors):
        kinds = [0]*len(anchors)
    wire = BRepBuilderAPI_MakeWire()
    for i,a in enumerate(anchors):
        b = anchors[(i+1)%len(anchors)]
        if kinds[i] == 1:
            poles = TColgp_Array1OfPnt(1,4)
            poles.SetValue(1,point(a['position'],z))
            poles.SetValue(2,point({k:a['position'][k]+a['outHandle'][k] for k in ('x','y')},z))
            poles.SetValue(3,point({k:b['position'][k]+b['inHandle'][k] for k in ('x','y')},z))
            poles.SetValue(4,point(b['position'],z))
            edge = BRepBuilderAPI_MakeEdge(Geom_BezierCurve(poles)).Edge()
        else:
            edge = BRepBuilderAPI_MakeEdge(point(a['position'],z),point(b['position'],z)).Edge()
        wire.Add(edge)
    if not wire.IsDone():
        raise ValueError('The Poly Maker outline is not a connected wire.')
    return wire.Wire()

def prism(wire, depth):
    maker = BRepBuilderAPI_MakeFace(wire, True)
    if not maker.IsDone():
        raise ValueError('Cannot form a planar CAD face.')
    fix = ShapeFix_Face(maker.Face())
    fix.FixOrientation()
    return BRepPrimAPI_MakePrism(fix.Face(), gp_Vec(0,0,depth)).Shape()

def custom_part(definition):
    depth = max(.001, definition['thicknessInches'])*MM
    z = -depth/2
    sketch = definition['sketch']
    shape = prism(sketch_wire(sketch['outerLoop'],z),depth)
    cutters = [prism(sketch_wire(loop,z-1),depth+2) for loop in sketch.get('cutoutLoops',[]) if loop]
    for hole in definition.get('holes',[]):
        if not hole or hole['size']['x'] <= 0 or hole['size']['y'] <= 0:
            continue
        x,y = hole['position']['x']*MM, hole['position']['y']*MM
        w,h = hole['size']['x']*MM, hole['size']['y']*MM
        angle = math.radians(hole.get('rotationDegrees',0))
        if hole.get('shape') == 1:
            points = [(-w/2,-h/2),(w/2,-h/2),(w/2,h/2),(-w/2,h/2)]
            points = [gp_Pnt(x+u*math.cos(angle)-v*math.sin(angle), y+u*math.sin(angle)+v*math.cos(angle), z-1) for u,v in points]
            wire = BRepBuilderAPI_MakeWire()
            for a,b in zip(points, points[1:]+points[:1]):
                wire.Add(BRepBuilderAPI_MakeEdge(a,b).Edge())
            wire = wire.Wire()
        else:
            # Matches Poly Maker: legacy Slot enum values normalize to Circle;
            # unequal dimensions represent an ellipse, not a capsule slot.
            if h > w:
                angle += math.pi/2
            axis = gp_Ax2(gp_Pnt(x,y,z-1), gp_Dir(0,0,1), gp_Dir(math.cos(angle),math.sin(angle),0))
            curve = gp_Circ(axis,w/2) if abs(w-h)<1e-10 else Geom_Ellipse(axis,max(w,h)/2,min(w,h)/2)
            wire = BRepBuilderAPI_MakeWire(BRepBuilderAPI_MakeEdge(curve).Edge()).Wire()
        cutters.append(prism(wire,depth+2))
    if cutters:
        operation = BRepAlgoAPI_Cut(shape,compound(cutters))
        operation.Build()
        if not operation.IsDone():
            raise ValueError('Cannot cut the Poly Maker holes.')
        shape = operation.Shape()
    return validate(shape,definition.get('name','Poly Maker part'))

def box_crop(shape, lower, upper):
    box = BRepPrimAPI_MakeBox(gp_Pnt(*lower),gp_Pnt(*upper)).Shape()
    operation = BRepAlgoAPI_Common(shape,box)
    operation.Build()
    if not operation.IsDone():
        raise ValueError('Cannot cut the CAD template to length.')
    return operation.Shape()

def tile_plate(template, length, width):
    cell=box_crop(template,[-6.35,-6.35,-100],[6.35,6.35,100])
    parts=[translate(cell,x=(x-(length-1)/2)*12.7,y=(y-(width-1)/2)*12.7)
           for x in range(length) for y in range(width)]
    if len(parts)==1:return validate(parts[0],'plate')
    arguments,tools=TopTools_ListOfShape(),TopTools_ListOfShape()
    arguments.Append(parts[0])
    for part in parts[1:]:tools.Append(part)
    operation=BRepAlgoAPI_Fuse();operation.SetArguments(arguments);operation.SetTools(tools)
    operation.Build()
    if not operation.IsDone():raise ValueError('Cannot join the plate cells into a solid.')
    operation.SimplifyResult(True,True)
    return validate(operation.Shape(),'plate')
