"""Offline rigid registration for wheels/gears with an arbitrary tooth phase.

Searching only signed coordinate axes can miss a perfectly matching CAD part.
Search rotation about the axle and both reflection choices without changing any
source dimensions. Display vertices are comparison points, never CAD faces.
"""
import numpy as np
import trimesh
from scipy.spatial import cKDTree
from scipy.optimize import minimize_scalar


def register_axial(shape, data, initial, axis=2):
    from build_library import tessellate, reference
    vertices, triangles = tessellate(shape, .025)
    vertices = np.array([v.toTuple() for v in vertices])
    target = reference(data)
    base = np.array(initial).reshape(4, 4)[:3, :3]
    axes = np.argmax(abs(base), axis=1)
    if len(set(axes)) != 3:
        raise ValueError('Axial registration needs an approximately axis-aligned model.')
    base = np.zeros((3, 3))
    base[np.arange(3), axes] = np.sign(np.array(initial).reshape(4, 4)[np.arange(3), axes])
    center = (target.min(0) + target.max(0)) / 2
    sample = target[np.linspace(0, len(target)-1, min(600, len(target)), dtype=int)]
    a, b = [i for i in range(3) if i != axis]

    def rotation(degrees):
        radians = np.deg2rad(degrees)
        c, s = np.cos(radians), np.sin(radians)
        result = np.eye(3)
        result[a,a] = result[b,b] = c
        result[a,b], result[b,a] = -s, s
        return result

    best = None
    for radial_sign in (-1, 1):
        for axial_sign in (-1, 1):
            reflection = np.eye(3)
            reflection[a,a], reflection[axis,axis] = radial_sign, axial_sign
            basis = reflection @ base
            source = vertices @ basis.T
            shift = center - (source.min(0)+source.max(0))/2
            source += shift
            mesh = trimesh.Trimesh(vertices=source, faces=triangles, process=False)
            tree = cKDTree(source)

            def points(degrees):
                return (sample-center) @ rotation(degrees) + center

            def score(degrees):
                _, distances, _ = trimesh.proximity.closest_point(mesh, points(degrees))
                return float(np.mean(np.minimum(distances, np.percentile(distances, 98))**2))

            ranked = []
            for angle in range(360):
                distances, _ = tree.query(points(angle))
                ranked.append((np.mean(np.minimum(distances, np.percentile(distances, 95))**2), angle))
            candidates = {angle for _,angle in sorted(ranked)[:12]} | set(range(0,360,10))
            coarse = sorted((score(angle), angle) for angle in candidates)
            for _, angle in coarse[:3]:
                fit = minimize_scalar(score, bounds=(angle-5, angle+5), method='bounded', options={'xatol':.001})
                if best is None or fit.fun < best[0]:
                    rot = rotation(fit.x)
                    matrix = np.eye(4)
                    matrix[:3,:3] = rot @ basis
                    matrix[:3,3] = rot @ (shift-center) + center
                    best = (fit.fun, matrix)
    return best[1].reshape(-1).tolist()
