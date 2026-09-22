using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Protobot.Rendering;

namespace Protobot {
    // Compile scene shadow batches on edits. Retain their map while the light
    // is unchanged; a camera-following light refreshes one scene projection.
    [DefaultExecutionOrder(9700), RequireComponent(typeof(Camera))]
    public sealed class WorldShadowCache : MonoBehaviour {
        public static bool Enabled = true;
        private static uint transientRevision;
        public static void Changed() { unchecked { transientRevision++; } }
        public int Updates { get; private set; }
        public int GeometryUpdates { get; private set; }
        public bool Active { get; private set; }
        private Camera camera;
        private Light light;
        private LightShadows originalShadows;
        private Shader cachedShader;
        private RenderTexture map;
        private uint revision = uint.MaxValue;
        private uint observedTransient;
        private int lightingHash;
        private int casterMask;
        private int snapshotMask;
        private Vector4 shadowParameters;
        private Vector3 lastCameraPosition;
        private int lastCameraHash;
        private RenderSceneSnapshot snapshot;
        private Bounds casterBounds;
        private CommandBuffer shadowCommands;
        private bool blocked;
        private uint blockedRevision, blockedTransient;
        private int blockedMask;
        private RenderSceneSnapshot installedSnapshot;
        private ViewportPresentation presentation;
        private readonly Dictionary<Material, Shader> originals = new Dictionary<Material, Shader>();
        private readonly List<Material> retired = new List<Material>();

        private void Awake() { camera = GetComponent<Camera>(); cachedShader = Resources.Load<Shader>("Rendering/CachedStandard"); }
        private void LateUpdate() {
            if (!Enabled || cachedShader == null || !cachedShader.isSupported || GraphicsSettings.renderPipelineAsset != null || camera.actualRenderingPath != RenderingPath.Forward || QualitySettings.shadows == ShadowQuality.Disable || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Shadowmap)) { Restore(); return; }
            if (presentation == null) presentation = GetComponent<ViewportPresentation>();
            int mask = presentation != null ? presentation.VisibleLayers : camera.cullingMask;
            if (blocked && blockedRevision == SceneActivity.Revision && blockedTransient == transientRevision && blockedMask == mask) return;
            blocked = false;
            if (light == null || !light.isActiveAndEnabled) {
                Restore();
                foreach (var candidate in FindObjectsOfType<Light>()) {
                    if (!candidate.isActiveAndEnabled || (candidate.cullingMask & mask) == 0) continue;
                    // Additional lights need their original forward passes; the
                    // procedural color path intentionally serves a single sun.
                    if (light != null || candidate.type != LightType.Directional || candidate.shadows == LightShadows.None) { Block(mask); return; }
                    light = candidate;
                }
                if (light == null) { Block(mask); return; }
                originalShadows = light.shadows;
            }
            if (light.shadows != originalShadows) {
                originalShadows = light.shadows; lightingHash = int.MinValue;
                if (originalShadows == LightShadows.None) { Restore(); return; }
            }
            int hash;
            unchecked { hash = light.transform.rotation.GetHashCode(); hash = hash * 31 + light.shadowBias.GetHashCode(); hash = hash * 31 + light.shadowNormalBias.GetHashCode(); hash = hash * 31 + light.shadowStrength.GetHashCode(); hash = hash * 31 + light.cullingMask; hash = hash * 31 + (int)QualitySettings.shadowResolution; hash = hash * 31 + (int)QualitySettings.shadows; hash = hash * 31 + QualitySettings.shadowDistance.GetHashCode(); }
            var cameraPosition = camera.transform.position;
            int cameraHash = camera.projectionMatrix.GetHashCode() ^ camera.pixelHeight;
            if (Active && revision == SceneActivity.Revision && observedTransient == transientRevision && snapshotMask == mask && hash == lightingHash && cameraPosition == lastCameraPosition && cameraHash == lastCameraHash) return;
            if (snapshot == null || revision != SceneActivity.Revision || observedTransient != transientRevision || casterMask != light.cullingMask || snapshotMask != mask) {
                foreach (var other in FindObjectsOfType<Light>())
                    if (other != light && other.isActiveAndEnabled && (other.cullingMask & mask) != 0) { Block(mask); return; }
                snapshot = RenderSceneSnapshot.Capture(camera);
                if (!snapshot.SupportsShadowCache) { Block(mask); return; }
                foreach (var material in snapshot.Materials) {
                    if (material.shader != cachedShader && material.shader.name != "Standard" || !snapshot.TryGetReceivesShadows(material, out bool receives)) { Block(mask); return; }
                }
                if (!snapshot.CasterBounds(light.cullingMask, out casterBounds)) { Block(mask); return; }
                casterMask = light.cullingMask; snapshotMask = mask; GeometryUpdates++;
                revision = SceneActivity.Revision; observedTransient = transientRevision;
            }
            int resolution = QualitySettings.shadowResolution >= ShadowResolution.High ? 4096 : 2048;
            if (installedSnapshot != snapshot) {
                retired.Clear();
                foreach (var pair in originals) {
                    bool found = false;
                    foreach (var material in snapshot.Materials) if (material == pair.Key) { found = true; break; }
                    if (!found) {
                        if (pair.Key != null && pair.Key.shader == cachedShader) pair.Key.shader = pair.Value;
                        retired.Add(pair.Key);
                    }
                }
                foreach (var material in retired) originals.Remove(material);
                foreach (var material in snapshot.Materials) {
                    // Recoloring can clone a material while its cache shader is
                    // installed. The clone must restore to Standard as well.
                    if (!originals.ContainsKey(material)) originals[material] = material.shader == cachedShader ? Shader.Find("Standard") : material.shader;
                    if (material.shader != cachedShader) {
                        material.shader = cachedShader;
                    }
                    snapshot.TryGetReceivesShadows(material, out bool receives);
                    material.SetFloat("_ProtobotReceiveShadow", receives ? 1 : 0);
                }
            }
            installedSnapshot = snapshot;
            float radius = Mathf.Max(1, casterBounds.extents.magnitude + 1);
            float nearest = Mathf.Sqrt(casterBounds.SqrDistance(cameraPosition));
            float furthest = Vector3.Distance(casterBounds.center, cameraPosition) + casterBounds.extents.magnitude;
            float pixelsPerUnit = camera.orthographic ? camera.pixelHeight / (camera.orthographicSize * 2)
                : camera.pixelHeight / (2 * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) * Mathf.Max(.001f, nearest));
            // Close inspection needs the original near shadow cascades. Re-enter
            // the cache only where its world texels remain small on screen.
            if (radius * 2 / resolution * pixelsPerUnit > 1.5f || furthest > QualitySettings.shadowDistance * .8f) { Suspend(); return; }
            bool refresh = !Active || hash != lightingHash || snapshot != renderedSnapshot;
            lastCameraPosition = cameraPosition; lastCameraHash = cameraHash;
            if (!refresh) return;
            if (map == null || map.width != resolution) {
                if (map != null) { map.Release(); Destroy(map); }
                map = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.Shadowmap) { name = "Retained world shadows", hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                if (!map.Create()) { Restore(); return; }
            }
            var direction = -light.transform.forward;
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(casterBounds.center + direction * radius * 2, light.transform.rotation, Vector3.one).inverse;
            var projection = Matrix4x4.Ortho(-radius, radius, -radius, radius, .01f, radius * 4);
            var gpu = GL.GetGPUProjectionMatrix(projection, true);
            if (shadowCommands == null) shadowCommands = new CommandBuffer { name = "Refresh changed scene shadows" };
            var command = shadowCommands; command.Clear();
            {
                command.SetRenderTarget(map);
                // CommandBuffer applies the backend's reversed-Z conversion.
                command.ClearRenderTarget(true, false, Color.clear, 1);
                command.SetViewProjectionMatrices(view, projection);
                command.SetGlobalVector("_WorldSpaceLightPos0", new Vector4(direction.x, direction.y, direction.z, 0));
                float texel = radius * 2 / resolution;
                const float filterRadius = 3.5f;
                command.SetGlobalVector("unity_LightShadowBias", new Vector4((SystemInfo.usesReversedZBuffer ? -1 : 1) * light.shadowBias * texel * filterRadius / (radius * 4), 1, light.shadowNormalBias * texel * filterRadius, 0));
                snapshot.DrawShadows(command, light.cullingMask);
                command.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
                Graphics.ExecuteCommandBuffer(command);
            }
            Shader.SetGlobalTexture("_ProtobotShadowMap", map);
            Shader.SetGlobalMatrix("_ProtobotWorldToShadow", gpu * view);
            shadowParameters = new Vector4(1, light.shadowStrength, 1f / resolution, originalShadows == LightShadows.Soft && QualitySettings.shadows == ShadowQuality.All ? 1 : 0);
            Active = true; Updates++; revision = SceneActivity.Revision; observedTransient = transientRevision; lightingHash = hash;
            renderedSnapshot = snapshot;
            ViewportPresentation.Changed();
        }
        private RenderSceneSnapshot renderedSnapshot;
        // Keep shared materials and lights correct for preview/UI cameras too.
        // Only the owning viewport replaces the engine's shadow rendering.
        private void OnPreCull() {
            if (!Active || light == null) return;
            light.shadows = LightShadows.None;
            Shader.SetGlobalVector("_ProtobotShadowParams", shadowParameters);
        }
        private void OnPostRender() {
            if (!Active) return;
            Shader.SetGlobalVector("_ProtobotShadowParams", Vector4.zero);
            if (light != null) light.shadows = originalShadows;
        }
        private void Block(int mask) {
            Restore(); blocked = true; blockedRevision = SceneActivity.Revision; blockedTransient = transientRevision; blockedMask = mask;
        }
        private void Suspend(bool restoreMaterials = false) {
            if (!Active && (!restoreMaterials || originals.Count == 0)) return;
            Shader.SetGlobalVector("_ProtobotShadowParams", Vector4.zero);
            if (Active && light != null) light.shadows = originalShadows;
            if (restoreMaterials) {
                foreach (var pair in originals) if (pair.Key != null && pair.Key.shader == cachedShader) pair.Key.shader = pair.Value;
                originals.Clear();
            }
            Active = false;
            ViewportPresentation.Changed();
        }
        private void Restore() {
            Suspend(true); light = null; revision = uint.MaxValue; snapshot = renderedSnapshot = installedSnapshot = null; blocked = false;
            if (map != null) { map.Release(); Destroy(map); map = null; }
        }
        private void OnDisable() { Restore(); }
        private void OnDestroy() { Restore(); shadowCommands?.Dispose(); }
    }
}
