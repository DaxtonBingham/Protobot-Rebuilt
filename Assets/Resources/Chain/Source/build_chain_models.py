from pathlib import Path

import cadquery as cq
from cadquery import exporters, importers
import numpy as np
import trimesh


MM_TO_IN = 1.0 / 25.4
PITCH_3P75_IN = 0.148
PITCH_6P35_IN = 0.250
PITCH_SCALE_6P35_FROM_3P75 = PITCH_6P35_IN / PITCH_3P75_IN
TARGET_FACE_COUNT = 5500


def export_link(step_path: Path, output_name: str, pitch_scale: float = 1.0) -> None:
    source_dir = Path(__file__).resolve().parent
    chain_dir = source_dir.parent
    step_path = step_path.resolve()

    workplane = importers.importStep(str(step_path))
    solids = workplane.val().Solids()
    if not solids:
        raise RuntimeError(f"No solids found in {step_path.name}")

    # Some source files include extra solids (retaining clips, assemblies, etc.).
    # Keep the largest solid as the link geometry source.
    main_solid = max(solids, key=lambda solid: solid.Volume())
    single = cq.Workplane().newObject([main_solid])

    stl_path = source_dir / f"{output_name}_tmp.stl"
    exporters.export(single, str(stl_path))

    mesh = trimesh.load_mesh(stl_path, force="mesh")

    # Center around origin so runtime midpoint placement behaves predictably.
    bounds = mesh.bounds
    center = (bounds[0] + bounds[1]) * 0.5
    mesh.apply_translation(-center)

    # Unity runtime chain placement assumes local +Z points along chain motion.
    rotation = trimesh.transformations.rotation_matrix(
        angle=np.deg2rad(-90.0),
        direction=[0.0, 1.0, 0.0],
        point=[0.0, 0.0, 0.0],
    )
    mesh.apply_transform(rotation)

    # CAD source units are millimeters; project chain dimensions are inches.
    mesh.apply_scale(MM_TO_IN * pitch_scale)
    mesh = simplify_for_runtime(mesh, TARGET_FACE_COUNT)

    obj_path = chain_dir / f"{output_name}.obj"
    obj_text = trimesh.exchange.obj.export_obj(mesh)
    obj_path.write_text(obj_text, encoding="utf-8")

    if stl_path.exists():
        stl_path.unlink()

    size = mesh.bounds[1] - mesh.bounds[0]
    print(f"{output_name}: verts={len(mesh.vertices)} faces={len(mesh.faces)} size(in)={size}")
    print(f"Wrote {obj_path}")


def simplify_for_runtime(mesh: trimesh.Trimesh, target_faces: int) -> trimesh.Trimesh:
    if mesh is None or len(mesh.faces) <= target_faces:
        return mesh

    try:
        simplified = mesh.simplify_quadric_decimation(face_count=target_faces)
        if simplified is not None and len(simplified.faces) > 0:
            return simplified
    except Exception as exc:
        print(f"Warning: simplify failed ({exc}), writing original mesh.")

    return mesh


if __name__ == "__main__":
    source_dir = Path(__file__).resolve().parent

    source_3p75 = source_dir / "VEX-Chain-3p75.step"
    source_9p79 = source_dir / "VEX-Chain-9p79.step"

    export_link(source_3p75, "Chain3p75Link")
    export_link(source_3p75, "Chain6p35Link", pitch_scale=PITCH_SCALE_6P35_FROM_3P75)
    export_link(source_9p79, "Chain9p79Link")
