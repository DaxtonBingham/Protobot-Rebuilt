using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace Protobot {
    // Offline, attribute-aware levels retain original positions/normals/UVs.
    // They affect display only: routing, picking, saved files, shadows and the
    // selected-chain adapter continue using the original source geometry.
    internal static class ChainDisplayMesh {
        private sealed class Level { public Mesh mesh; public float error; }
        private static readonly Dictionary<Mesh, Level[]> levels = new Dictionary<Mesh, Level[]>();
        public static bool Enabled = true;
        public static float PixelError = .05f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() {
            foreach (var choices in levels.Values) foreach (var level in choices)
                if (level.mesh != null) UnityEngine.Object.Destroy(level.mesh);
            levels.Clear();
        }

        public static Mesh Select(Mesh source, Camera camera, Bounds worldBounds, float worldScale) {
            if (!Enabled || source == null || camera == null || source.subMeshCount != 1) return source;
            if (!levels.TryGetValue(source, out var choices)) { choices = Load(source); levels.Add(source, choices); }
            if (choices.Length == 0) return source;
            float pixelsPerUnit;
            if (camera.orthographic) pixelsPerUnit = camera.pixelHeight / (camera.orthographicSize * 2);
            else {
                var forward = camera.transform.forward;
                float nearest = Vector3.Dot(worldBounds.center - camera.transform.position, forward)
                    - Vector3.Dot(worldBounds.extents, new Vector3(Mathf.Abs(forward.x), Mathf.Abs(forward.y), Mathf.Abs(forward.z)));
                if (nearest <= camera.nearClipPlane) return source;
                pixelsPerUnit = camera.pixelHeight / (2 * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) * nearest);
            }
            var result = source;
            foreach (var level in choices) {
                if (level.error * worldScale * pixelsPerUnit > PixelError) break;
                result = level.mesh;
            }
            return result;
        }

        private static Level[] Load(Mesh source) {
            if (!source.isReadable) return new Level[0];
            var positions = source.vertices; var normals = source.normals; var uv = source.uv; var tangents = source.tangents; var indices = source.GetIndices(0);
            byte[] hash;
            using (var bytes = new MemoryStream(positions.Length * 48 + indices.Length * 4)) {
                using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true)) {
                    for (int i = 0; i < positions.Length; i++) {
                        var p = positions[i]; var n = i < normals.Length ? normals[i] : Vector3.up;
                        var t = i < uv.Length ? uv[i] : Vector2.zero; var b = i < tangents.Length ? tangents[i] : new Vector4(1, 0, 0, 1);
                        writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); writer.Write(n.x); writer.Write(n.y); writer.Write(n.z);
                        writer.Write(t.x); writer.Write(t.y); writer.Write(b.x); writer.Write(b.y); writer.Write(b.z); writer.Write(b.w);
                    }
                    foreach (int index in indices) writer.Write(index);
                }
                using (var sha = SHA256.Create()) hash = sha.ComputeHash(bytes.GetBuffer(), 0, (int)bytes.Length);
            }
            string key = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            var asset = Resources.Load<TextAsset>("Rendering/ChainLod/" + key);
            if (asset == null) return new Level[0];
            var result = new List<Level>();
            try {
                using (var reader = new BinaryReader(new MemoryStream(asset.bytes))) {
                    if (System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8)) != "PBCHAIN1") return new Level[0];
                    for (int i = 0; i < hash.Length; i++) if (reader.ReadByte() != hash[i]) return new Level[0];
                    if (reader.ReadInt32() != positions.Length || reader.ReadInt32() != indices.Length) return new Level[0];
                    int count = reader.ReadInt32(); if (count < 0 || count > 16) return new Level[0];
                    float previous = -1;
                    for (int level = 0; level < count; level++) {
                        float error = reader.ReadSingle(); int length = reader.ReadInt32();
                        if (float.IsNaN(error) || float.IsInfinity(error) || error < previous || length < 3 || length >= indices.Length || length % 3 != 0 || length * 4L > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Invalid chain display mesh");
                        var triangles = new int[length];
                        for (int i = 0; i < length; i++) { triangles[i] = reader.ReadInt32(); if ((uint)triangles[i] >= positions.Length) throw new InvalidDataException("Invalid chain display index"); }
                        var mesh = UnityEngine.Object.Instantiate(source); mesh.name = source.name + " (chain display " + level + ")"; mesh.hideFlags = HideFlags.DontSave;
                        mesh.SetIndices(triangles, MeshTopology.Triangles, 0, false); mesh.bounds = source.bounds;
                        result.Add(new Level { mesh = mesh, error = error }); previous = error;
                    }
                    if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected chain display data");
                }
                return result.ToArray();
            } catch (Exception error) {
                foreach (var level in result) UnityEngine.Object.Destroy(level.mesh);
                Debug.LogWarning("Using original chain mesh: " + error.Message);
                return new Level[0];
            } finally { Resources.UnloadAsset(asset); }
        }
    }
}
