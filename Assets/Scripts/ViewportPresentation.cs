using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Protobot.ChainSystem;
using Protobot.CustomParts;

namespace Protobot {
    // UI can keep animating at the display rate without rendering an unchanged
    // robot again. The cache contains only the main camera, never the UI cameras.
    [RequireComponent(typeof(Camera)), DefaultExecutionOrder(9800)]
    public sealed class ViewportPresentation : MonoBehaviour {
        private static uint visualRevision;
        private Camera world;
        private RenderTexture image;
        private RetainedViewportEffect present;
        private readonly List<Behaviour> suspended = new List<Behaviour>();
        private Behaviour[] effects;
        private Light[] lights;
        private Matrix4x4 view, projection;
        private Rect viewport;
        private Vector2Int size;
        private Color background;
        private int lightingHash, qualityHash;
        private uint sceneRevision, appearanceRevision;
        private float stableAfter;
        private bool capturePending, captured, cached;
        private int savedMask;
        private CameraClearFlags savedClear;
        private DepthTextureMode savedDepthMode;
        public bool IsCached => cached;
        public int VisibleLayers => cached ? savedMask : world.cullingMask;
        public int SceneFrames { get; private set; }
        public int ReusedFrames { get; private set; }

        public static void Changed() {
            unchecked { visualRevision++; }
            IdleRendering.Changed();
        }

        private void Awake() {
            world = GetComponent<Camera>();
            var list = new List<Behaviour>();
            foreach (var component in GetComponents<Behaviour>()) {
                var name = component.GetType().FullName;
                if (name == "EPOOutline.Outliner" || name == "UnityEngine.Rendering.PostProcessing.PostProcessLayer" || component is SimpleViewportFxaa) list.Add(component);
            }
            effects = list.ToArray();
            lights = FindObjectsOfType<Light>(true);
            stableAfter = Time.realtimeSinceStartup + 1f;
        }

        private void OnApplicationFocus(bool focused) { Changed(); stableAfter = Time.realtimeSinceStartup + .15f; }
        private void OnApplicationPause(bool paused) { Changed(); }

        private void LateUpdate() {
            // Off-screen output and alternate pipelines retain their existing path.
            if (world.targetTexture != null || GraphicsSettings.renderPipelineAsset != null || world.rect != new Rect(0, 0, 1, 1)) {
                Restore(); return;
            }
            bool changed = sceneRevision != SceneActivity.Revision || appearanceRevision != visualRevision;
            sceneRevision = SceneActivity.Revision; appearanceRevision = visualRevision;
            var currentSize = new Vector2Int(Screen.width, Screen.height);
            var currentView = world.worldToCameraMatrix;
            var currentProjection = world.projectionMatrix;
            int currentQuality = QualitySettings.antiAliasing * 31 + (int)QualitySettings.activeColorSpace * 7 + (world.allowHDR ? 1 : 0);
            int currentLighting = GetLightingHash();
            changed |= currentView != view || currentProjection != projection || viewport != world.rect || background != world.backgroundColor || lightingHash != currentLighting || qualityHash != currentQuality;
            view = currentView; projection = currentProjection; viewport = world.rect; background = world.backgroundColor; lightingHash = currentLighting; qualityHash = currentQuality;
            if (size != currentSize) { Restore(); ReleaseImage(); size = currentSize; changed = true; }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse != null) {
                // Picking continues to run, but a pointer move alone cannot change
                // the image. Hover outlines and transient tools publish their own
                // visual changes; camera motion is detected by the matrices above.
                // Clicks can change rendering preferences as well as placed parts.
                if (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed || mouse.leftButton.wasReleasedThisFrame || mouse.rightButton.wasReleasedThisFrame || mouse.middleButton.wasReleasedThisFrame) changed = true;
            }
            if (keyboard != null && (keyboard.anyKey.isPressed || keyboard.anyKey.wasReleasedThisFrame)) changed = true;
            if (InsertChainTool.IsInteracting || CustomPartStudioController.IsStudioOpen) changed = true;
            if (changed) {
                Restore();
                stableAfter = Time.realtimeSinceStartup + .15f;
                return;
            }
            if (cached || Time.realtimeSinceStartup < stableAfter) return;
            if (captured) { StartPresentation(); return; }
            if (!capturePending) CaptureNextFrame();
        }

        private int GetLightingHash() {
            unchecked {
                int hash = RenderSettings.ambientLight.GetHashCode();
                hash = hash * 31 + RenderSettings.ambientIntensity.GetHashCode();
                hash = hash * 31 + QualitySettings.shadowDistance.GetHashCode();
                hash = hash * 31 + (int)QualitySettings.shadows;
                foreach (var light in lights) {
                    if (light == null) continue;
                    hash = hash * 31 + light.isActiveAndEnabled.GetHashCode();
                    hash = hash * 31 + light.color.GetHashCode();
                    hash = hash * 31 + light.intensity.GetHashCode();
                    hash = hash * 31 + light.shadowStrength.GetHashCode();
                    hash = hash * 31 + (int)light.shadows;
                    hash = hash * 31 + light.transform.localToWorldMatrix.GetHashCode();
                }
                return hash;
            }
        }

        private void CaptureNextFrame() {
            if (image == null) {
                image = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.Default) {
                    name = "Unchanged viewport", hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
                if (!image.Create()) { ReleaseImage(); stableAfter = Time.realtimeSinceStartup + 5; return; }
            }
            // Capture the final image effect during an ordinary camera render.
            // Never force Camera.Render while presentation is paused: doing so
            // crosses the engine's on-demand rendering/presentation boundary.
            if (present == null) present = gameObject.AddComponent<RetainedViewportEffect>();
            present.owner = this;
            present.image = null;
            present.enabled = true;
            capturePending = true;
        }

        internal void Capture(RenderTexture source) {
            if (!capturePending || image == null) return;
            Graphics.Blit(source, image);
            captured = true;
        }

        private void OnPostRender() {
            if (cached) ReusedFrames++;
            else SceneFrames++;
        }

        private void StartPresentation() {
            capturePending = false;
            savedMask = world.cullingMask;
            savedClear = world.clearFlags;
            savedDepthMode = world.depthTextureMode;
            suspended.Clear();
            foreach (var effect in effects) if (effect != null && effect.enabled) { suspended.Add(effect); effect.enabled = false; }
            world.cullingMask = 0;
            world.clearFlags = CameraClearFlags.SolidColor;
            world.depthTextureMode = DepthTextureMode.None;
            if (present == null) present = gameObject.AddComponent<RetainedViewportEffect>();
            present.image = image;
            present.enabled = true;
            cached = true;
        }

        private void Restore() {
            capturePending = captured = false;
            if (present != null) { present.enabled = false; present.image = null; }
            if (!cached) return;
            world.cullingMask = savedMask;
            world.clearFlags = savedClear;
            world.depthTextureMode = savedDepthMode;
            foreach (var effect in suspended) if (effect != null) effect.enabled = true;
            suspended.Clear();
            cached = false;
        }

        private void ReleaseImage() {
            if (image != null) { image.Release(); Destroy(image); image = null; }
        }
        private void OnDisable() { Restore(); ReleaseImage(); }
        private void OnDestroy() { Restore(); ReleaseImage(); }
    }

    // An image effect presents before overlay canvases. AfterEverything on the
    // shared screen buffer runs too late and can leave previous UI pixels behind.
    public sealed class RetainedViewportEffect : MonoBehaviour {
        internal ViewportPresentation owner;
        internal RenderTexture image;
        private void OnRenderImage(RenderTexture source, RenderTexture destination) {
            if (owner != null) owner.Capture(source);
            Graphics.Blit(image != null ? image : source, destination);
        }
    }

}
