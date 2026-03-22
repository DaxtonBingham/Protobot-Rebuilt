using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Protobot.CustomParts {
    public static class CustomPartMeshBuilder {
        private const int MeshBuildVersion = 3;
        private static readonly Dictionary<string, Mesh> RenderMeshCache =
            new Dictionary<string, Mesh>(StringComparer.Ordinal);

        private static readonly Dictionary<string, Mesh> ColliderMeshCache =
            new Dictionary<string, Mesh>(StringComparer.Ordinal);

        private static MethodInfo triangulateWithHolesMethod;
        private static MethodInfo triangulateSimpleMethod;

        public struct HoleRuntimeData {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public HoleCollider.HoleType holeType;
            public bool twoSided;
            public CustomHoleShape shape;
        }

        private struct CompiledLoop {
            public List<Vector2> points;
            public bool isCutout;
        }

        public static bool BuildMeshes(CustomPartDefinition definition, out Mesh renderMesh, out Mesh colliderMesh, out List<HoleRuntimeData> holes) {
            renderMesh = null;
            colliderMesh = null;
            holes = new List<HoleRuntimeData>();

            if (definition == null || definition.sketch == null || definition.sketch.outerLoop == null) {
                return false;
            }

            string hash = $"{MeshBuildVersion}:{definition.GetDeterministicHash()}";
            if (RenderMeshCache.TryGetValue(hash, out Mesh cachedRender) && cachedRender != null
                && ColliderMeshCache.TryGetValue(hash, out Mesh cachedCollider) && cachedCollider != null) {
                renderMesh = cachedRender;
                colliderMesh = cachedCollider;
                holes = BuildHoleRuntimeData(definition);
                return true;
            }

            if (!TryCompileLoops(definition, out List<CompiledLoop> loops)) {
                return false;
            }

            CompiledLoop outer = loops.FirstOrDefault(loop => !loop.isCutout);
            if (outer.points == null || outer.points.Count < 3) {
                return false;
            }

            List<List<Vector2>> cutouts = loops
                .Where(loop => loop.isCutout && loop.points != null && loop.points.Count >= 3)
                .Select(loop => loop.points)
                .ToList();

            EnsureCounterClockwise(outer.points);
            foreach (List<Vector2> cutout in cutouts) {
                EnsureClockwise(cutout);
            }

            if (!TryTriangulate(outer.points, cutouts, out List<int> topTriangles, out List<Vector2> allCapPoints)) {
                return false;
            }

            float thickness = Mathf.Max(0.001f, definition.thicknessInches);
            float halfThickness = thickness * 0.5f;

            int capPointCount = allCapPoints.Count;
            int sideSegmentCount = loops.Sum(loop => Mathf.Max(0, loop.points.Count));
            var vertices = new List<Vector3>(capPointCount * 2 + sideSegmentCount * 4);
            var triangles = new List<int>(topTriangles.Count * 2 + sideSegmentCount * 6);
            var uvs = new List<Vector2>(capPointCount * 2 + sideSegmentCount * 4);
            var normals = new List<Vector3>(capPointCount * 2 + sideSegmentCount * 4);

            // Top cap.
            for (int i = 0; i < capPointCount; i++) {
                Vector2 p = allCapPoints[i];
                vertices.Add(new Vector3(p.x, p.y, halfThickness));
                uvs.Add(p);
                normals.Add(Vector3.forward);
            }

            // Bottom cap.
            for (int i = 0; i < capPointCount; i++) {
                Vector2 p = allCapPoints[i];
                vertices.Add(new Vector3(p.x, p.y, -halfThickness));
                uvs.Add(p);
                normals.Add(Vector3.back);
            }

            // Cap triangles.
            for (int i = 0; i < topTriangles.Count; i += 3) {
                int a = topTriangles[i];
                int b = topTriangles[i + 1];
                int c = topTriangles[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= capPointCount || b >= capPointCount || c >= capPointCount) {
                    continue;
                }

                Vector2 pa = allCapPoints[a];
                Vector2 pb = allCapPoints[b];
                Vector2 pc = allCapPoints[c];
                float cross = ((pb.x - pa.x) * (pc.y - pa.y)) - ((pb.y - pa.y) * (pc.x - pa.x));
                if (cross < 0f) {
                    int tmp = b;
                    b = c;
                    c = tmp;
                }

                // Top (facing +Z).
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);

                // Bottom (facing -Z).
                triangles.Add(capPointCount + c);
                triangles.Add(capPointCount + b);
                triangles.Add(capPointCount + a);
            }

            // Side walls for outer and cutout loops.
            foreach (CompiledLoop loop in loops) {
                List<Vector2> points = loop.points;
                int pointCount = points.Count;
                if (pointCount < 2) {
                    continue;
                }

                float perimeter = 0f;
                for (int i = 0; i < pointCount; i++) {
                    int next = (i + 1) % pointCount;
                    perimeter += Vector2.Distance(points[i], points[next]);
                }

                float run = 0f;
                for (int i = 0; i < pointCount; i++) {
                    int next = (i + 1) % pointCount;
                    Vector2 p = points[i];
                    Vector2 pNext = points[next];
                    float segmentLength = Vector2.Distance(p, pNext);
                    if (segmentLength < 0.0001f) {
                        continue;
                    }

                    float u0 = perimeter > 0.0001f ? run / perimeter : i / (float)Mathf.Max(1, pointCount);
                    float u1 = perimeter > 0.0001f ? (run + segmentLength) / perimeter : (i + 1) / (float)Mathf.Max(1, pointCount);
                    Vector2 edge = pNext - p;
                    Vector3 outward = new Vector3(edge.y, -edge.x, 0f).normalized;

                    int aTop = vertices.Count;
                    int aBottom = vertices.Count + 1;
                    int bTop = vertices.Count + 2;
                    int bBottom = vertices.Count + 3;

                    vertices.Add(new Vector3(p.x, p.y, halfThickness));
                    vertices.Add(new Vector3(p.x, p.y, -halfThickness));
                    vertices.Add(new Vector3(pNext.x, pNext.y, halfThickness));
                    vertices.Add(new Vector3(pNext.x, pNext.y, -halfThickness));

                    uvs.Add(new Vector2(u0, 1f));
                    uvs.Add(new Vector2(u0, 0f));
                    uvs.Add(new Vector2(u1, 1f));
                    uvs.Add(new Vector2(u1, 0f));

                    normals.Add(outward);
                    normals.Add(outward);
                    normals.Add(outward);
                    normals.Add(outward);

                    // Side wall (flat shaded per segment, outward-facing winding).
                    triangles.Add(aTop);
                    triangles.Add(bBottom);
                    triangles.Add(bTop);

                    triangles.Add(aTop);
                    triangles.Add(aBottom);
                    triangles.Add(bBottom);

                    run += segmentLength;
                }
            }

            renderMesh = new Mesh {
                name = $"CustomPart_{definition.definitionId}"
            };
            renderMesh.SetVertices(vertices);
            renderMesh.SetTriangles(triangles, 0);
            renderMesh.SetUVs(0, uvs);
            renderMesh.SetNormals(normals);
            renderMesh.RecalculateBounds();
            renderMesh.RecalculateTangents();

            colliderMesh = UnityEngine.Object.Instantiate(renderMesh);
            colliderMesh.name = $"{renderMesh.name}_Collider";

            RenderMeshCache[hash] = renderMesh;
            ColliderMeshCache[hash] = colliderMesh;
            holes = BuildHoleRuntimeData(definition);
            return true;
        }

        public static List<HoleRuntimeData> BuildHoleRuntimeData(CustomPartDefinition definition) {
            var holes = new List<HoleRuntimeData>();
            if (definition?.holes == null) return holes;

            float fallbackDepth = Mathf.Max(0.001f, definition.thicknessInches);
            foreach (CustomHoleDefinition hole in definition.holes) {
                if (hole == null) continue;
                Vector2 size = hole.size;
                if (size.x <= 0 || size.y <= 0) continue;

                float depth = hole.depthInches > 0 ? hole.depthInches : fallbackDepth;
                holes.Add(new HoleRuntimeData {
                    localPosition = new Vector3(hole.position.x, hole.position.y, 0f),
                    localRotation = Quaternion.AngleAxis(hole.rotationDegrees, Vector3.forward),
                    localScale = new Vector3(size.x, size.y, depth),
                    holeType = HoleCollider.HoleType.Normal,
                    twoSided = true,
                    shape = NormalizeHoleShape(hole.shape)
                });
            }

            return holes;
        }

        private static bool TryCompileLoops(CustomPartDefinition definition, out List<CompiledLoop> loops) {
            loops = new List<CompiledLoop>();
            if (!TryCompileLoop(definition.sketch.outerLoop, false, out CompiledLoop outer)) {
                return false;
            }

            loops.Add(outer);

            if (definition.sketch.cutoutLoops != null) {
                foreach (LoopData loop in definition.sketch.cutoutLoops) {
                    if (loop == null) continue;
                    if (TryCompileLoop(loop, true, out CompiledLoop cutout)) {
                        loops.Add(cutout);
                    }
                }
            }

            AppendHoleCutoutLoops(definition, loops);

            return true;
        }

        private static void AppendHoleCutoutLoops(CustomPartDefinition definition, List<CompiledLoop> loops) {
            if (definition?.holes == null) {
                return;
            }

            for (int i = 0; i < definition.holes.Length; i++) {
                CustomHoleDefinition hole = definition.holes[i];
                if (hole == null) {
                    continue;
                }

                if (hole.size.x <= 0f || hole.size.y <= 0f) {
                    continue;
                }

                List<Vector2> points = BuildHoleCutoutPoints(hole);
                RemoveDuplicateAdjacent(points);
                if (points.Count < 3) {
                    continue;
                }

                loops.Add(new CompiledLoop {
                    points = points,
                    isCutout = true
                });
            }
        }

        private static List<Vector2> BuildHoleCutoutPoints(CustomHoleDefinition hole) {
            float halfW = Mathf.Abs(hole.size.x) * 0.5f;
            float halfH = Mathf.Abs(hole.size.y) * 0.5f;
            float radians = hole.rotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            CustomHoleShape normalizedShape = NormalizeHoleShape(hole.shape);

            var points = new List<Vector2>();
            switch (normalizedShape) {
            case CustomHoleShape.Square:
                points.Add(RotateAndTranslate(new Vector2(-halfW, -halfH), hole.position, cos, sin));
                points.Add(RotateAndTranslate(new Vector2(halfW, -halfH), hole.position, cos, sin));
                points.Add(RotateAndTranslate(new Vector2(halfW, halfH), hole.position, cos, sin));
                points.Add(RotateAndTranslate(new Vector2(-halfW, halfH), hole.position, cos, sin));
                break;
            default:
                const int segments = 24;
                for (int i = 0; i < segments; i++) {
                    float t = i / (float)segments;
                    float angle = t * Mathf.PI * 2f;
                    Vector2 local = new Vector2(Mathf.Cos(angle) * halfW, Mathf.Sin(angle) * halfH);
                    points.Add(RotateAndTranslate(local, hole.position, cos, sin));
                }
                break;
            }

            return points;
        }

        private static CustomHoleShape NormalizeHoleShape(CustomHoleShape shape) {
            return shape == CustomHoleShape.Square ? CustomHoleShape.Square : CustomHoleShape.Circle;
        }

        private static Vector2 RotateAndTranslate(Vector2 local, Vector2 center, float cos, float sin) {
            Vector2 rotated = new Vector2(
                (local.x * cos) - (local.y * sin),
                (local.x * sin) + (local.y * cos));
            return center + rotated;
        }

        private static bool TryCompileLoop(LoopData loop, bool isCutout, out CompiledLoop compiled) {
            compiled = default;
            if (loop?.anchors == null || loop.anchors.Length < 3) return false;

            AnchorData[] anchors = loop.anchors.Where(anchor => anchor != null).ToArray();
            if (anchors.Length < 3) return false;

            SegmentKind[] segmentKinds = loop.segmentKinds;
            if (segmentKinds == null || segmentKinds.Length != anchors.Length) {
                segmentKinds = new SegmentKind[anchors.Length];
                for (int i = 0; i < segmentKinds.Length; i++) segmentKinds[i] = SegmentKind.Line;
            }

            var points = new List<Vector2>(anchors.Length * 12);
            for (int i = 0; i < anchors.Length; i++) {
                int next = (i + 1) % anchors.Length;
                AnchorData start = anchors[i];
                AnchorData end = anchors[next];
                SegmentKind segmentKind = segmentKinds[Mathf.Clamp(i, 0, segmentKinds.Length - 1)];

                if (segmentKind == SegmentKind.Bezier) {
                    AppendBezier(points, start, end);
                }
                else {
                    AppendPoint(points, start.position);
                }
            }

            if (points.Count < 3) return false;
            RemoveDuplicateAdjacent(points);
            if (points.Count < 3) return false;

            compiled = new CompiledLoop {
                points = points,
                isCutout = isCutout
            };
            return true;
        }

        private static void AppendBezier(List<Vector2> points, AnchorData start, AnchorData end) {
            Vector2 p0 = start.position;
            Vector2 p1 = start.position + start.outHandle;
            Vector2 p2 = end.position + end.inHandle;
            Vector2 p3 = end.position;

            float approxLength = Vector2.Distance(p0, p1) + Vector2.Distance(p1, p2) + Vector2.Distance(p2, p3);
            int subdivisions = Mathf.Clamp(Mathf.CeilToInt(approxLength / 0.1f), 6, 64);

            for (int s = 0; s < subdivisions; s++) {
                float t = s / (float)subdivisions;
                Vector2 point = EvaluateCubicBezier(p0, p1, p2, p3, t);
                AppendPoint(points, point);
            }
        }

        private static Vector2 EvaluateCubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) {
            float omt = 1f - t;
            return omt * omt * omt * p0
                + 3f * omt * omt * t * p1
                + 3f * omt * t * t * p2
                + t * t * t * p3;
        }

        private static void AppendPoint(List<Vector2> points, Vector2 point) {
            if (points.Count == 0) {
                points.Add(point);
                return;
            }

            if (Vector2.Distance(points[points.Count - 1], point) > 0.0001f) {
                points.Add(point);
            }
        }

        private static void RemoveDuplicateAdjacent(List<Vector2> points) {
            if (points.Count < 2) return;

            for (int i = points.Count - 1; i > 0; i--) {
                if (Vector2.Distance(points[i], points[i - 1]) < 0.0001f) {
                    points.RemoveAt(i);
                }
            }

            if (points.Count > 2 && Vector2.Distance(points[0], points[points.Count - 1]) < 0.0001f) {
                points.RemoveAt(points.Count - 1);
            }
        }

        private static float SignedArea(IList<Vector2> points) {
            if (points == null || points.Count < 3) return 0f;
            float area = 0f;
            for (int i = 0; i < points.Count; i++) {
                int next = (i + 1) % points.Count;
                area += (points[i].x * points[next].y) - (points[next].x * points[i].y);
            }

            return area * 0.5f;
        }

        private static void EnsureCounterClockwise(List<Vector2> points) {
            if (SignedArea(points) < 0f) {
                points.Reverse();
            }
        }

        private static void EnsureClockwise(List<Vector2> points) {
            if (SignedArea(points) > 0f) {
                points.Reverse();
            }
        }

        private static bool TryTriangulate(List<Vector2> outer, List<List<Vector2>> holes, out List<int> indices, out List<Vector2> allPoints) {
            allPoints = new List<Vector2>(outer);
            if (holes != null) {
                foreach (List<Vector2> hole in holes) {
                    allPoints.AddRange(hole);
                }
            }

            indices = null;
            if (TryTriangulateViaProBuilder(outer, holes, out List<int> proBuilderIndices)) {
                indices = proBuilderIndices;
                return true;
            }

            // Fallback: ignore holes and triangulate outer contour only.
            if (TryTriangulateSimple(outer, out List<int> fallback)) {
                indices = fallback;
                allPoints = new List<Vector2>(outer);
                return true;
            }

            return false;
        }

        private static bool TryTriangulateViaProBuilder(IList<Vector2> outer, IList<List<Vector2>> holes, out List<int> indices) {
            indices = null;
            try {
                Type triangulationType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.Triangulation, Unity.ProBuilder");
                if (triangulationType == null) return false;

                if (triangulateWithHolesMethod == null) {
                    triangulateWithHolesMethod = triangulationType.GetMethod(
                        "Triangulate",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(IList<Vector2>), typeof(IList<IList<Vector2>>), typeof(List<int>).MakeByRefType() },
                        null);
                }

                if (triangulateWithHolesMethod == null) return false;

                var holesInterface = new List<IList<Vector2>>();
                if (holes != null) {
                    for (int i = 0; i < holes.Count; i++) {
                        holesInterface.Add(holes[i]);
                    }
                }

                object[] args = { outer, holesInterface, null };
                object result = triangulateWithHolesMethod.Invoke(null, args);
                if (result is bool success && success && args[2] is List<int> outIndices) {
                    indices = outIndices;
                    return indices.Count >= 3;
                }
            }
            catch (Exception ex) {
                Debug.LogWarning($"Custom part triangulation reflection failed: {ex.Message}");
            }

            return false;
        }

        private static bool TryTriangulateSimple(IList<Vector2> points, out List<int> indices) {
            indices = null;

            try {
                Type triangulationType = Type.GetType("UnityEngine.ProBuilder.MeshOperations.Triangulation, Unity.ProBuilder");
                if (triangulationType != null) {
                    if (triangulateSimpleMethod == null) {
                        triangulateSimpleMethod = triangulationType.GetMethod(
                            "Triangulate",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { typeof(IList<Vector2>), typeof(List<int>).MakeByRefType(), typeof(bool) },
                            null);
                    }

                    if (triangulateSimpleMethod != null) {
                        object[] args = { points, null, false };
                        object result = triangulateSimpleMethod.Invoke(null, args);
                        if (result is bool success && success && args[1] is List<int> outIndices) {
                            indices = outIndices;
                            return indices.Count >= 3;
                        }
                    }
                }
            }
            catch (Exception ex) {
                Debug.LogWarning($"Custom part simple triangulation reflection failed: {ex.Message}");
            }

            return TryEarClip(points, out indices);
        }

        private static bool TryEarClip(IList<Vector2> polygon, out List<int> indices) {
            indices = new List<int>();
            int count = polygon == null ? 0 : polygon.Count;
            if (count < 3) return false;

            var vertexIndices = new List<int>(count);
            for (int i = 0; i < count; i++) vertexIndices.Add(i);

            int guard = 0;
            while (vertexIndices.Count > 3 && guard < 5000) {
                guard++;
                bool earFound = false;
                for (int i = 0; i < vertexIndices.Count; i++) {
                    int prev = vertexIndices[(i - 1 + vertexIndices.Count) % vertexIndices.Count];
                    int cur = vertexIndices[i];
                    int next = vertexIndices[(i + 1) % vertexIndices.Count];

                    Vector2 a = polygon[prev];
                    Vector2 b = polygon[cur];
                    Vector2 c = polygon[next];

                    if (Vector3.Cross(b - a, c - b).z <= 0f) continue;
                    if (ContainsAnyPoint(polygon, vertexIndices, prev, cur, next)) continue;

                    indices.Add(prev);
                    indices.Add(cur);
                    indices.Add(next);
                    vertexIndices.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound) {
                    return false;
                }
            }

            if (vertexIndices.Count == 3) {
                indices.Add(vertexIndices[0]);
                indices.Add(vertexIndices[1]);
                indices.Add(vertexIndices[2]);
            }

            return indices.Count >= 3;
        }

        private static bool ContainsAnyPoint(IList<Vector2> polygon, IList<int> indices, int aIndex, int bIndex, int cIndex) {
            Vector2 a = polygon[aIndex];
            Vector2 b = polygon[bIndex];
            Vector2 c = polygon[cIndex];

            for (int i = 0; i < indices.Count; i++) {
                int pIndex = indices[i];
                if (pIndex == aIndex || pIndex == bIndex || pIndex == cIndex) continue;
                if (PointInTriangle(polygon[pIndex], a, b, c)) return true;
            }

            return false;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) {
            float area = 0.5f * (-b.y * c.x + a.y * (-b.x + c.x) + a.x * (b.y - c.y) + b.x * c.y);
            if (Mathf.Abs(area) < 1e-7f) return false;

            float s = 1f / (2f * area) * (a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y);
            float t = 1f / (2f * area) * (a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y);
            float u = 1f - s - t;
            return s >= 0f && t >= 0f && u >= 0f;
        }
    }
}
