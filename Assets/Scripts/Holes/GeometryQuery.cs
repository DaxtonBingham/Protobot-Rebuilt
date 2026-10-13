using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    internal sealed class QueryMesh {
        public Vector3[] vertices;
        public int[] triangles;
        public Bounds bounds;
        private static readonly Dictionary<Mesh, QueryMesh> cache = new Dictionary<Mesh, QueryMesh>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => cache.Clear();
        public static QueryMesh For(Mesh mesh) {
            if (mesh == null || !mesh.isReadable) return null;
            if (!cache.TryGetValue(mesh, out var value)) {
                value = new QueryMesh { vertices = mesh.vertices, triangles = mesh.triangles, bounds = mesh.bounds };
                cache.Add(mesh, value);
            }
            return value;
        }
    }

    internal static class GeometryQuery {
        public static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix) {
            var ex = matrix.MultiplyVector(new Vector3(bounds.extents.x, 0, 0));
            var ey = matrix.MultiplyVector(new Vector3(0, bounds.extents.y, 0));
            var ez = matrix.MultiplyVector(new Vector3(0, 0, bounds.extents.z));
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center), 2 * new Vector3(
                Mathf.Abs(ex.x) + Mathf.Abs(ey.x) + Mathf.Abs(ez.x),
                Mathf.Abs(ex.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y),
                Mathf.Abs(ex.z) + Mathf.Abs(ey.z) + Mathf.Abs(ez.z)));
        }

        public static bool RayBounds(Ray ray, Bounds b, ref float entry, ref float exit) {
            var min = b.min; var max = b.max;
            for (int axis = 0; axis < 3; axis++) {
                float d = ray.direction[axis], p = ray.origin[axis];
                if (Mathf.Abs(d) < 1e-12f) { if (p < min[axis] || p > max[axis]) return false; continue; }
                float t0 = (min[axis] - p) / d, t1 = (max[axis] - p) / d;
                if (t0 > t1) { float t = t0; t0 = t1; t1 = t; }
                entry = Mathf.Max(entry, t0); exit = Mathf.Min(exit, t1);
                if (entry > exit) return false;
            }
            return exit >= 0;
        }

        // Unity normalizes Ray.direction; compensate so returned distances remain
        // in world units under non-uniform owner/hole scaling.
        public static bool RayMesh(QueryMesh mesh, Matrix4x4 world, Ray ray, float maxDistance, out float distance, out Vector3 normal, bool backfaces = false) {
            distance = maxDistance; normal = Vector3.zero;
            var inverse = world.inverse;
            var localDirection = inverse.MultiplyVector(ray.direction);
            float factor = localDirection.magnitude;
            if (factor < 1e-12f) return false;
            var local = new Ray(inverse.MultiplyPoint3x4(ray.origin), localDirection);
            float entry = 0, exit = maxDistance * factor;
            if (!RayBounds(local, mesh.bounds, ref entry, ref exit)) return false;
            bool found = false;
            for (int i = 0; i < mesh.triangles.Length; i += 3) {
                Vector3 a = mesh.vertices[mesh.triangles[i]], b = mesh.vertices[mesh.triangles[i + 1]], c = mesh.vertices[mesh.triangles[i + 2]];
                if (!RayTriangle(local, a, b, c, backfaces, out float t) || t / factor > distance) continue;
                distance = t / factor; normal = Vector3.Cross(b - a, c - a); found = true;
            }
            if (found) normal = inverse.transpose.MultiplyVector(normal).normalized;
            return found;
        }

        private static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, bool backfaces, out float distance) {
            distance = 0;
            var e1 = b - a; var e2 = c - a; var p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (backfaces ? Mathf.Abs(det) < 1e-10f : det < 1e-10f) return false;
            float inv = 1 / det; var s = ray.origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < -1e-6f || u > 1.000001f) return false;
            var q = Vector3.Cross(s, e1); float v = Vector3.Dot(ray.direction, q) * inv;
            if (v < -1e-6f || u + v > 1.000001f) return false;
            distance = Vector3.Dot(e2, q) * inv;
            return distance >= 0;
        }

        public static bool CapsuleMesh(QueryMesh mesh, Matrix4x4 world, Vector3 p, Vector3 q, float radius, bool solid = false) {
            var inverse = world.inverse;
            // The stock hole meshes are pick surfaces, including open end caps.
            // Treating their bounds/planes as a closed solid fills missing sides.
            if (solid && (InsideConvex(mesh, inverse.MultiplyPoint3x4(p)) || InsideConvex(mesh, inverse.MultiplyPoint3x4(q)))) return true;
            float r2 = radius * radius;
            var direction = q - p;
            float length = direction.magnitude;
            var ray = new Ray(p, length > 1e-9f ? direction / length : Vector3.forward);
            for (int i = 0; i < mesh.triangles.Length; i += 3) {
                Vector3 a = world.MultiplyPoint3x4(mesh.vertices[mesh.triangles[i]]),
                    b = world.MultiplyPoint3x4(mesh.vertices[mesh.triangles[i + 1]]),
                    c = world.MultiplyPoint3x4(mesh.vertices[mesh.triangles[i + 2]]);
                if (length > 1e-9f && RayTriangle(ray, a, b, c, true, out float t) && t <= length) return true;
                if ((ClosestTriangle(p, a, b, c) - p).sqrMagnitude <= r2 || (ClosestTriangle(q, a, b, c) - q).sqrMagnitude <= r2
                    || SegmentDistanceSquared(p, q, a, b) <= r2 || SegmentDistanceSquared(p, q, b, c) <= r2 || SegmentDistanceSquared(p, q, c, a) <= r2) return true;
            }
            return false;
        }

        private static bool InsideConvex(QueryMesh mesh, Vector3 point) {
            if (!mesh.bounds.Contains(point)) return false;
            for (int i = 0; i < mesh.triangles.Length; i += 3) {
                var a = mesh.vertices[mesh.triangles[i]];
                var n = Vector3.Cross(mesh.vertices[mesh.triangles[i + 1]] - a, mesh.vertices[mesh.triangles[i + 2]] - a);
                if (Vector3.Dot(mesh.bounds.center - a, n) > 0) n = -n;
                if (Vector3.Dot(point - a, n) > 1e-7f * n.magnitude) return false;
            }
            return true;
        }

        private static float SegmentDistanceSquared(Vector3 p, Vector3 q, Vector3 a, Vector3 b) {
            Vector3 d1 = q - p, d2 = b - a, r = p - a;
            float aa = Vector3.Dot(d1, d1), ee = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
            if (aa <= 1e-12f && ee <= 1e-12f) return r.sqrMagnitude;
            if (aa <= 1e-12f) { s = 0; t = Mathf.Clamp01(f / ee); }
            else {
                float c = Vector3.Dot(d1, r);
                if (ee <= 1e-12f) { t = 0; s = Mathf.Clamp01(-c / aa); }
                else {
                    float bb = Vector3.Dot(d1, d2), denominator = aa * ee - bb * bb;
                    s = denominator > 1e-12f ? Mathf.Clamp01((bb * f - c * ee) / denominator) : 0;
                    t = (bb * s + f) / ee;
                    if (t < 0) { t = 0; s = Mathf.Clamp01(-c / aa); }
                    else if (t > 1) { t = 1; s = Mathf.Clamp01((bb - c) / aa); }
                }
            }
            return (p + d1 * s - a - d2 * t).sqrMagnitude;
        }

        private static Vector3 ClosestTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c) {
            var ab = b - a; var ac = c - a; var ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            var bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            var cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denominator = va + vb + vc;
            if (Mathf.Abs(denominator) < 1e-20f) return a;
            return a + ab * (vb / denominator) + ac * (vc / denominator);
        }
    }
}
