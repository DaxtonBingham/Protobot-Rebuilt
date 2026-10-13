using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot {
    // Opaque Standard shadow passes need position and normal (normal bias), but
    // not color, UVs or tangents. Preserve every triangle and hard normal exactly.
    internal static class CompactShadowMesh {
        private struct Vertex : IEquatable<Vertex> {
            public Vector3 position, normal;
            public bool Equals(Vertex other) => position.Equals(other.position) && normal.Equals(other.normal);
            public override bool Equals(object other) => other is Vertex vertex && Equals(vertex);
            public override int GetHashCode() => position.GetHashCode() * 397 ^ normal.GetHashCode();
        }
        private static readonly Dictionary<Mesh, Mesh> cache = new Dictionary<Mesh, Mesh>();
        public static bool Enabled = true;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { cache.Clear(); Enabled = true; }
        public static void Release(Mesh source) {
            if (source != null && cache.TryGetValue(source, out var shadow)) {
                cache.Remove(source);
                if (shadow != null) UnityEngine.Object.Destroy(shadow);
            }
        }
        public static Mesh Get(Mesh source, Material material) {
            if (!Enabled || source == null || !source.isReadable || material == null || (material.shader.name != "Standard" && material.shader.name != "Protobot/Cached Standard")
                || material.renderQueue > 2500 || material.IsKeywordEnabled("_ALPHATEST_ON") || material.IsKeywordEnabled("_ALPHABLEND_ON") || material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")) return source;
            if (cache.TryGetValue(source, out var mesh) && mesh != null) return mesh;
            var positions = source.vertices; var normals = source.normals;
            if (positions.Length != normals.Length) return source;
            var vertices = new List<Vector3>(); var compactNormals = new List<Vector3>();
            var unique = new Dictionary<Vertex, int>(); var remap = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++) {
                var vertex = new Vertex { position = positions[i], normal = normals[i] };
                if (!unique.TryGetValue(vertex, out int index)) {
                    index = vertices.Count; unique.Add(vertex, index); vertices.Add(vertex.position); compactNormals.Add(vertex.normal);
                }
                remap[i] = index;
            }
            mesh = new Mesh { name = source.name + " (shadow positions/normals)", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetNormals(compactNormals); mesh.subMeshCount = source.subMeshCount;
            for (int sub = 0; sub < source.subMeshCount; sub++) {
                if (source.GetTopology(sub) != MeshTopology.Triangles) { UnityEngine.Object.Destroy(mesh); return source; }
                var indices = source.GetIndices(sub);
                for (int i = 0; i < indices.Length; i++) indices[i] = remap[indices[i]];
                mesh.SetIndices(indices, MeshTopology.Triangles, sub, false);
            }
            mesh.bounds = source.bounds;
            mesh.OptimizeIndexBuffers();
            cache[source] = mesh;
            return mesh;
        }
    }
}
