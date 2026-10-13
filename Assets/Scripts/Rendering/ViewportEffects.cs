using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace Protobot {
    // FXAA-only cameras do not need the general volume/effect pipeline or its
    // NaN-cleanup copy. Keep the original FXAA and dithering shader unchanged.
    [DefaultExecutionOrder(9750), RequireComponent(typeof(Camera))]
    public sealed class ViewportEffects : MonoBehaviour {
        public static bool Enabled = true;
        private PostProcessLayer legacy;
        private SimpleViewportFxaa effect;
        private Camera camera;
        private readonly List<PostProcessVolume> volumes = new List<PostProcessVolume>();
        private bool ownsLegacy;
        public bool Active => ownsLegacy;
        private void Awake() {
            camera = GetComponent<Camera>(); legacy = GetComponent<PostProcessLayer>();
            if (legacy == null) return;
            var resources = typeof(PostProcessLayer).GetField("m_Resources", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(legacy) as PostProcessResources;
            if (resources == null) return;
            effect = gameObject.AddComponent<SimpleViewportFxaa>(); effect.enabled = false;
            effect.Initialize(legacy, resources);
        }
        private void LateUpdate() {
            if (legacy == null || effect == null) return;
            var presentation = GetComponent<ViewportPresentation>();
            if (presentation != null && presentation.IsCached) return;
            bool eligible = Enabled && effect.Supported && (legacy.enabled || ownsLegacy)
                && GraphicsSettings.renderPipelineAsset == null && !camera.stereoEnabled
                && legacy.antialiasingMode == PostProcessLayer.Antialiasing.FastApproximateAntialiasing
                && !legacy.breakBeforeColorGrading && !legacy.finalBlitToCameraTarget;
            if (eligible) {
                volumes.Clear(); PostProcessManager.instance.GetActiveVolumes(legacy, volumes);
                eligible = volumes.Count == 0;
            }
            if (eligible) {
                if (!ownsLegacy) { legacy.enabled = false; ownsLegacy = true; effect.enabled = true; ViewportPresentation.Changed(); }
            } else Restore();
        }
        private void Restore() {
            if (!ownsLegacy) return;
            if (effect != null) effect.enabled = false;
            if (legacy != null) legacy.enabled = true;
            ownsLegacy = false; ViewportPresentation.Changed();
        }
        private void OnDisable() { Restore(); }
        private void OnDestroy() { Restore(); if (effect != null) Destroy(effect); }
    }

    public sealed class SimpleViewportFxaa : MonoBehaviour {
        private PostProcessLayer settings;
        private PostProcessResources resources;
        private PropertySheetFactory sheets;
        private PropertySheet preparation, final;
        private CommandBuffer commands;
        private Camera camera;
        private static readonly int LumaBuffer = Shader.PropertyToID("_ProtobotViewportLuma");
        private readonly System.Random random = new System.Random(1234);
        private int noiseIndex;
        public bool Supported { get; private set; }

        internal void Initialize(PostProcessLayer settings, PostProcessResources resources) {
            this.settings = settings; this.resources = resources;
            camera = GetComponent<Camera>();
            var prepare = Resources.Load<Shader>("Rendering/ViewportLuma");
            Supported = prepare != null && prepare.isSupported && resources.shaders.finalPass != null && resources.shaders.finalPass.isSupported && resources.blueNoise64 != null && resources.blueNoise64.Length > 0;
            if (!Supported) return;
            sheets = new PropertySheetFactory(); preparation = sheets.Get(prepare); final = sheets.Get(resources.shaders.finalPass);
            // The original layer normally initializes this shader uniform in
            // OnPreCull. Taking over before its first render must do so too.
            preparation.properties.SetFloat("_RenderViewportScaleFactor", 1);
            final.properties.SetFloat("_RenderViewportScaleFactor", 1);
            commands = new CommandBuffer { name = "Viewport FXAA and dithering" };
        }
        private void OnEnable() { if (commands != null) camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, commands); }
        private void OnDisable() { if (commands != null && camera != null) camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commands); }
        private void OnPreCull() {
            if (!Supported) return;
            {
                var flip = SystemInfo.graphicsUVStartsAtTop ? new Vector4(1, -1, 0, 1) : new Vector4(1, 1, 0, 0);
                preparation.properties.SetVector("_UVTransform", flip);
                preparation.properties.SetFloat("_StopNaN", settings.stopNaNPropagation ? 1 : 0);
                preparation.properties.SetFloat("_KeepAlpha", settings.fastApproximateAntialiasing.keepAlpha ? 1 : 0);
                final.ClearKeywords(); final.EnableKeyword(settings.fastApproximateAntialiasing.fastMode ? "FXAA_LOW" : "FXAA");
                if (settings.fastApproximateAntialiasing.keepAlpha) final.EnableKeyword("FXAA_KEEP_ALPHA");
                final.properties.SetVector("_UVTransform", flip);
                var noise = resources.blueNoise64[(++noiseIndex) % resources.blueNoise64.Length];
                final.properties.SetTexture("_DitheringTex", noise);
                final.properties.SetVector("_Dithering_Coords", new Vector4((float)camera.pixelWidth / noise.width, (float)camera.pixelHeight / noise.height, (float)random.NextDouble(), (float)random.NextDouble()));
                commands.Clear();
                commands.GetTemporaryRT(LumaBuffer, camera.pixelWidth, camera.pixelHeight, 0, FilterMode.Bilinear,
                    camera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default, RenderTextureReadWrite.Default);
                var target = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
                commands.BlitFullscreenTriangle(target, LumaBuffer, preparation, 0);
                commands.BlitFullscreenTriangle(LumaBuffer, target, final, 0);
                commands.ReleaseTemporaryRT(LumaBuffer);
            }
        }
        // EPO adds its outline buffer at OnPreRender. Running here preserves the
        // original AA-before-outlines order and shares the camera color target.
        private void OnRenderImage(RenderTexture source, RenderTexture destination) => Graphics.Blit(source, destination);
        private void OnDestroy() { OnDisable(); commands?.Dispose(); sheets?.Release(); }
    }
}
