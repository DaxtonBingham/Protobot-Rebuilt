using System.Collections.Generic;
using DG.Tweening;
using Protobot.Builds;
using Protobot.ChainSystem;
using Protobot.CustomParts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Protobot {
    // Keep input/physics running; omit only unchanged presentations once tools settle.
    [DefaultExecutionOrder(10000)]
    public sealed class IdleRendering : MonoBehaviour {
        private static IdleRendering instance;
        private const float SettleSeconds = 1f;
        private readonly HashSet<Graphic> graphics = new HashSet<Graphic>();
        private readonly List<Graphic> deadGraphics = new List<Graphic>();
        private Canvas[] canvases;
        private int[] graphicCounts;
        private Camera[] cameras;
        private Matrix4x4[] cameraViews, cameraProjections;
        private Vector2 pointer;
        private Vector2Int screenSize;
        private int activeVSync, activeTarget, activeInterval;
        private float activeUntil, nextDiscovery;
        private bool idle, refreshGraphics;
        public static bool IsIdle => instance != null && instance.idle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize() {
            if (instance != null) return;
            var host = new GameObject("Idle rendering");
            DontDestroyOnLoad(host);
            host.AddComponent<IdleRendering>();
        }

        // Call for changes that do not originate in input, UI graphics or a tween.
        public static void Changed() {
            if (instance != null) instance.activeUntil = Time.realtimeSinceStartup + SettleSeconds;
        }

        public static void SetActiveFrameRate(int vSync, int target) {
            // Restore first so waking from idle cannot put the previous cap back.
            if (instance != null) instance.RestoreRendering();
            QualitySettings.vSyncCount = vSync;
            Application.targetFrameRate = target;
            if (instance != null) {
                instance.activeVSync = vSync;
                instance.activeTarget = target;
            }
            Changed();
        }

        private void Awake() {
            instance = this;
            activeVSync = QualitySettings.vSyncCount;
            activeTarget = Application.targetFrameRate;
            activeInterval = OnDemandRendering.renderFrameInterval;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneBuild.OnGenerateBuild += BuildChanged;
            Discover();
            Changed();
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) { Discover(); Changed(); }
        private void BuildChanged(BuildData build) { refreshGraphics = true; Changed(); }
        private void OnApplicationFocus(bool focused) { Changed(); if (focused) RestoreRendering(); }
        private void OnApplicationPause(bool paused) { Changed(); if (!paused) RestoreRendering(); }

        private void Discover() {
            var main = Camera.main;
            if (main != null && main.GetComponent<ViewportEffects>() == null) main.gameObject.AddComponent<ViewportEffects>();
            if (main != null && main.GetComponent<ViewportPresentation>() == null) main.gameObject.AddComponent<ViewportPresentation>();
            if (main != null && main.GetComponent<WorldShadowCache>() == null) main.gameObject.AddComponent<WorldShadowCache>();
            canvases = FindObjectsOfType<Canvas>(true);
            graphicCounts = new int[canvases.Length];
            cameras = FindObjectsOfType<Camera>(true);
            cameraViews = new Matrix4x4[cameras.Length];
            cameraProjections = new Matrix4x4[cameras.Length];
            foreach (var graphic in FindObjectsOfType<Graphic>(true)) Watch(graphic);
            for (int i = 0; i < canvases.Length; i++)
                graphicCounts[i] = GraphicRegistry.GetGraphicsForCanvas(canvases[i]).Count;
            refreshGraphics = false;
            nextDiscovery = Time.realtimeSinceStartup + 2;
            deadGraphics.Clear();
            foreach (var graphic in graphics) if (graphic == null) deadGraphics.Add(graphic);
            foreach (var graphic in deadGraphics) graphics.Remove(graphic);
        }

        private void Watch(Graphic graphic) {
            if (graphic == null || !graphics.Add(graphic)) return;
            graphic.RegisterDirtyVerticesCallback(Changed);
            graphic.RegisterDirtyMaterialCallback(Changed);
            graphic.RegisterDirtyLayoutCallback(Changed);
        }

        private void LateUpdate() {
            var now = Time.realtimeSinceStartup;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse != null) {
                var current = mouse.position.ReadValue();
                if (Application.isFocused && (current != pointer || mouse.scroll.ReadValue() != Vector2.zero
                    || mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed
                    || mouse.leftButton.wasReleasedThisFrame || mouse.rightButton.wasReleasedThisFrame || mouse.middleButton.wasReleasedThisFrame))
                    Changed();
                pointer = current;
            }
            if (Application.isFocused && keyboard != null && (keyboard.anyKey.isPressed || keyboard.anyKey.wasReleasedThisFrame)) Changed();
            if (CustomPartStudioController.IsStudioOpen || InsertChainTool.IsInteracting || DOTween.TotalPlayingTweens() > 0) Changed();
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && (selected.GetComponent<InputField>()?.isFocused == true
                || selected.GetComponent<TMPro.TMP_InputField>()?.isFocused == true)) Changed();

            var size = new Vector2Int(Screen.width, Screen.height);
            if (size != screenSize) { screenSize = size; Changed(); }
            for (int i = 0; i < cameras.Length; i++) {
                var camera = cameras[i];
                if (camera == null || !camera.isActiveAndEnabled) continue;
                var view = camera.worldToCameraMatrix;
                var projection = camera.projectionMatrix;
                if (view != cameraViews[i] || projection != cameraProjections[i]) Changed();
                cameraViews[i] = view; cameraProjections[i] = projection;
            }
            for (int i = 0; i < canvases.Length; i++) {
                if (canvases[i] == null) { refreshGraphics = true; continue; }
                var registered = GraphicRegistry.GetGraphicsForCanvas(canvases[i]);
                if (registered.Count == graphicCounts[i]) continue;
                graphicCounts[i] = registered.Count;
                for (int j = 0; j < registered.Count; j++) Watch(registered[j]);
                Changed();
            }
            // Discovery is bounded to active intervals, never a whole-scene idle poll.
            if (refreshGraphics || (!idle && now >= nextDiscovery)) Discover();
            if (now < activeUntil) { RestoreRendering(); return; }
            if (!idle) {
                activeVSync = QualitySettings.vSyncCount;
                activeTarget = Application.targetFrameRate;
                activeInterval = OnDemandRendering.renderFrameInterval;
                idle = true;
            }
            QualitySettings.vSyncCount = 0;
            // Keep idle input responsive without exceeding the user's chosen cap.
            int idleTarget = Application.isFocused ? Mathf.Max(60, Screen.currentResolution.refreshRate) : 15;
            Application.targetFrameRate = activeTarget > 0 ? Mathf.Min(idleTarget, activeTarget) : idleTarget;
            OnDemandRendering.renderFrameInterval = int.MaxValue;
        }

        private void RestoreRendering() {
            if (!idle) return;
            QualitySettings.vSyncCount = activeVSync;
            Application.targetFrameRate = activeTarget;
            OnDemandRendering.renderFrameInterval = activeInterval;
            idle = false;
        }

        private void OnDisable() { RestoreRendering(); }
        private void OnDestroy() {
            RestoreRendering();
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneBuild.OnGenerateBuild -= BuildChanged;
            foreach (var graphic in graphics) if (graphic != null) {
                graphic.UnregisterDirtyVerticesCallback(Changed);
                graphic.UnregisterDirtyMaterialCallback(Changed);
                graphic.UnregisterDirtyLayoutCallback(Changed);
            }
            if (instance == this) instance = null;
        }
    }
}
