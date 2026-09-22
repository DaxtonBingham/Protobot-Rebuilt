using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Protobot.ChainSystem;

namespace Protobot.Rendering {
    // A versioned, read-only rendering boundary. No Unity object identities or
    // editor state are required to consume the exported buffers in another host.
    public sealed class RenderSceneSnapshot {
        private sealed class Draw {
            public int mesh, material, submesh, layer;
            public Matrix4x4 matrix;
            public string id;
            public bool cast, receive;
        }
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private readonly Dictionary<Mesh, int> meshIds = new Dictionary<Mesh, int>();
        private readonly Dictionary<Material, int> materialIds = new Dictionary<Material, int>();
        private readonly Dictionary<Texture2D, int> textureIds = new Dictionary<Texture2D, int>();
        private readonly List<Draw> draws = new List<Draw>();
        private readonly Dictionary<Material, int> receiverFlags = new Dictionary<Material, int>();
        private sealed class ShadowBatch { public Mesh mesh; public Material material; public int submesh, pass, count; public Matrix4x4[] matrices; }
        private List<ShadowBatch> shadowBatches;
        private int shadowMask;
        private static readonly string[] TextureSlots = { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap", "_DetailMask", "_DetailAlbedoMap", "_DetailNormalMap", "_ParallaxMap" };
        public int DrawCount => draws.Count;
        public int MeshCount => meshes.Count;
        public long TriangleCount { get; private set; }
        internal bool SupportsShadowCache { get; private set; } = true;
        internal IReadOnlyList<Material> Materials => materials;
        internal bool TryGetReceivesShadows(Material material, out bool receives) {
            receiverFlags.TryGetValue(material, out int flags);
            receives = (flags & 1) != 0;
            return flags != 3;
        }

        internal bool CasterBounds(int lightMask, out Bounds bounds) {
            bounds = new Bounds(); bool found = false;
            foreach (var draw in draws) {
                if (!draw.cast || (lightMask & (1 << draw.layer)) == 0) continue;
                var transformed = GeometryQuery.TransformBounds(meshes[draw.mesh].bounds, draw.matrix);
                if (transformed.size.sqrMagnitude < 1e-10f) continue;
                if (!found) { bounds = transformed; found = true; } else bounds.Encapsulate(transformed);
            }
            return found;
        }

        internal void DrawShadows(CommandBuffer command, int lightMask) {
            if (shadowBatches == null || shadowMask != lightMask) PrepareShadowBatches(lightMask);
            foreach (var batch in shadowBatches) {
                if (SystemInfo.supportsInstancing && batch.material.enableInstancing)
                    command.DrawMeshInstanced(batch.mesh, batch.submesh, batch.material, batch.pass, batch.matrices, batch.count);
                else for (int i = 0; i < batch.count; i++) command.DrawMesh(batch.mesh, batch.matrices[i], batch.material, batch.submesh, batch.pass);
            }
        }
        private void PrepareShadowBatches(int lightMask) {
            shadowBatches = new List<ShadowBatch>(); shadowMask = lightMask;
            var groups = new Dictionary<(int mesh, int material, int submesh), List<Matrix4x4>>();
            foreach (var draw in draws) {
                if (!draw.cast || (lightMask & (1 << draw.layer)) == 0) continue;
                var key = (draw.mesh, draw.material, draw.submesh);
                if (!groups.TryGetValue(key, out var group)) { group = new List<Matrix4x4>(); groups.Add(key, group); }
                group.Add(draw.matrix);
            }
            foreach (var group in groups) {
                var material = materials[group.Key.material];
                int pass = material.FindPass("ShadowCaster"); if (pass < 0) continue;
                for (int start = 0; start < group.Value.Count; start += 1023) {
                    int count = Math.Min(1023, group.Value.Count - start);
                    var matrices = new Matrix4x4[count];
                    group.Value.CopyTo(start, matrices, 0, count);
                    shadowBatches.Add(new ShadowBatch { mesh = meshes[group.Key.mesh], material = material, submesh = group.Key.submesh, pass = pass, count = count, matrices = matrices });
                }
            }
        }

        public void Add(Mesh mesh, int submesh, Material material, Matrix4x4 matrix, string id, int layer, bool cast, bool receive) {
            if (mesh == null || material == null || submesh >= mesh.subMeshCount) return;
            if (mesh.GetTopology(submesh) != MeshTopology.Triangles) throw new NotSupportedException("Snapshot requires triangle topology: " + mesh.name);
            if (!meshIds.TryGetValue(mesh, out int meshId)) { meshId = meshes.Count; meshIds.Add(mesh, meshId); meshes.Add(mesh); }
            if (!materialIds.TryGetValue(material, out int materialId)) {
                materialId = materials.Count; materialIds.Add(material, materialId); materials.Add(material);
                foreach (string slot in TextureSlots) {
                    var texture = material.HasProperty(slot) ? material.GetTexture(slot) as Texture2D : null;
                    if (texture == null || textureIds.ContainsKey(texture)) continue;
                    textureIds.Add(texture, textures.Count); textures.Add(texture);
                }
            }
            draws.Add(new Draw { mesh = meshId, material = materialId, submesh = submesh, matrix = matrix, id = id ?? "", layer = layer, cast = cast, receive = receive });
            receiverFlags.TryGetValue(material, out int flags); receiverFlags[material] = flags | (receive ? 1 : 2);
            shadowBatches = null;
            TriangleCount += mesh.GetIndexCount(submesh) / 3;
        }

        public static RenderSceneSnapshot Capture(Camera camera) {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            SceneActivity.Flush(true); HoleWorld.Flush();
            var snapshot = new RenderSceneSnapshot();
            var presentation = camera.GetComponent<ViewportPresentation>();
            int mask = presentation != null ? presentation.VisibleLayers : camera.cullingMask;
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>()) {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || (mask & (1 << renderer.gameObject.layer)) == 0
                    || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly || renderer.GetComponent<ShadowRenderProxy>() != null
                    || renderer.GetComponent<ChainInstances>() != null) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                if (renderer.shadowCastingMode == ShadowCastingMode.TwoSided || renderer.HasPropertyBlock()) snapshot.SupportsShadowCache = false;
                var owner = renderer.GetComponentInParent<SavedObject>();
                string id = owner != null && owner.DocumentPart != null ? owner.DocumentPart.Id : renderer.name;
                var shared = renderer.sharedMaterials;
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount && sub < shared.Length; sub++)
                    snapshot.Add(filter.sharedMesh, sub, shared[sub], renderer.localToWorldMatrix, id, renderer.gameObject.layer,
                        renderer.shadowCastingMode != ShadowCastingMode.Off, renderer.receiveShadows);
            }
            foreach (var chain in UnityEngine.Object.FindObjectsOfType<ChainInstances>())
                if ((mask & (1 << chain.gameObject.layer)) != 0) chain.AppendSnapshot(snapshot);
            return snapshot;
        }

        public void Write(string path, Camera camera) {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            using (var writer = new BinaryWriter(File.Create(path))) {
                writer.Write("PRBSNAP1"); writer.Write(1);
                writer.Write(Screen.width); writer.Write(Screen.height);
                writer.Write((int)QualitySettings.activeColorSpace); writer.Write(QualitySettings.antiAliasing);
                Write(writer, camera.backgroundColor); Write(writer, camera.transform.position);
                Write(writer, camera.worldToCameraMatrix); Write(writer, camera.projectionMatrix);
                Write(writer, GL.GetGPUProjectionMatrix(camera.projectionMatrix, false));
                writer.Write(camera.fieldOfView); writer.Write(camera.nearClipPlane); writer.Write(camera.farClipPlane);
                writer.Write(camera.orthographic); writer.Write(camera.orthographicSize);
                var pivot = PivotCamera.Main;
                Write(writer, pivot != null ? pivot.focusPosition : Vector3.zero);
                Write(writer, pivot != null ? pivot.lookAngle : Vector3.zero);
                writer.Write(pivot != null ? pivot.focusDistance : -30f);
                Write(writer, RenderSettings.ambientLight); writer.Write(RenderSettings.ambientIntensity);
                var probe = RenderSettings.ambientProbe;
                for (int channel = 0; channel < 3; channel++) for (int coefficient = 0; coefficient < 9; coefficient++) writer.Write(probe[channel, coefficient]);
                var lights = UnityEngine.Object.FindObjectsOfType<Light>();
                var active = new List<Light>();
                foreach (var light in lights) if (light.isActiveAndEnabled) active.Add(light);
                writer.Write(active.Count);
                foreach (var light in active) {
                    writer.Write((int)light.type); Write(writer, light.transform.position); Write(writer, -light.transform.forward);
                    Write(writer, light.color); writer.Write(light.intensity); writer.Write(light.range);
                    writer.Write(light.spotAngle); writer.Write((int)light.shadows); writer.Write(light.shadowStrength);
                    writer.Write(light.shadowBias); writer.Write(light.shadowNormalBias); writer.Write(light.cullingMask);
                }
                writer.Write(meshes.Count);
                foreach (var mesh in meshes) {
                    writer.Write(mesh.name); var positions = mesh.vertices; var normals = mesh.normals; var uv = mesh.uv; var tangents = mesh.tangents;
                    writer.Write(positions.Length);
                    for (int i = 0; i < positions.Length; i++) {
                        Write(writer, positions[i]); Write(writer, i < normals.Length ? normals[i] : Vector3.up);
                        Write(writer, i < uv.Length ? uv[i] : Vector2.zero); Write(writer, i < tangents.Length ? tangents[i] : new Vector4(1, 0, 0, 1));
                    }
                    writer.Write(mesh.subMeshCount);
                    for (int sub = 0; sub < mesh.subMeshCount; sub++) {
                        var indices = mesh.GetIndices(sub); writer.Write(indices.Length);
                        foreach (var index in indices) writer.Write(index);
                    }
                }
                writer.Write(textures.Count);
                foreach (var texture in textures) {
                    writer.Write(texture.name); writer.Write((int)texture.wrapMode); writer.Write((int)texture.filterMode);
                    var previous = RenderTexture.active;
                    var temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);
                    try {
                        Graphics.Blit(texture, temporary); RenderTexture.active = temporary;
                        copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); copy.Apply(false, false);
                        byte[] png = copy.EncodeToPNG(); writer.Write(png.Length); writer.Write(png);
                    } finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); UnityEngine.Object.Destroy(copy); }
                }
                writer.Write(materials.Count);
                foreach (var material in materials) {
                    writer.Write(material.name); writer.Write(material.shader.name); writer.Write(material.renderQueue);
                    Write(writer, material.HasProperty("_Color") ? material.color : Color.white);
                    foreach (var property in new[] { "_Metallic", "_Glossiness", "_GlossMapScale", "_Mode", "_Cutoff", "_BumpScale", "_OcclusionStrength", "_Parallax", "_UVSec" })
                        writer.Write(material.HasProperty(property) ? material.GetFloat(property) : 0f);
                    Write(writer, material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black);
                    foreach (string slot in TextureSlots) {
                        var texture = material.HasProperty(slot) ? material.GetTexture(slot) as Texture2D : null;
                        writer.Write(texture != null && textureIds.TryGetValue(texture, out int textureId) ? textureId : -1);
                        Write(writer, material.HasProperty(slot) ? material.GetTextureScale(slot) : Vector2.one);
                        Write(writer, material.HasProperty(slot) ? material.GetTextureOffset(slot) : Vector2.zero);
                    }
                    writer.Write(string.Join(";", material.shaderKeywords));
                }
                writer.Write(draws.Count);
                foreach (var draw in draws) {
                    writer.Write(draw.mesh); writer.Write(draw.submesh); writer.Write(draw.material); writer.Write(draw.id);
                    writer.Write(draw.layer); writer.Write(draw.cast); writer.Write(draw.receive); Write(writer, draw.matrix);
                }
            }
        }
        private static void Write(BinaryWriter writer, Vector2 value) { writer.Write(value.x); writer.Write(value.y); }
        private static void Write(BinaryWriter writer, Vector3 value) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); }
        private static void Write(BinaryWriter writer, Vector4 value) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); writer.Write(value.w); }
        private static void Write(BinaryWriter writer, Color value) { writer.Write(value.r); writer.Write(value.g); writer.Write(value.b); writer.Write(value.a); }
        private static void Write(BinaryWriter writer, Matrix4x4 value) { for (int i = 0; i < 16; i++) writer.Write(value[i]); }
    }
}
