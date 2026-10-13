using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SFB;
using Protobot.SelectionSystem;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.CustomParts {
    public class CustomPartStudioController : MonoBehaviour {
        private enum StudioTab {
            Outline = 0,
            Cutouts = 1,
            Holes = 2,
            Part = 3
        }

        private enum CanvasTool {
            Select = 0,
            AddPoint = 1,
            ToggleBezier = 2,
            AddHole = 3
        }

        private enum StudioLaunchMode {
            Definition = 0,
            EditSelectedInstance = 1
        }

        private struct SelectedHandle {
            public bool isValid;
            public int anchorIndex;
            public bool isOutHandle;
        }

        private struct CameraSessionState {
            public bool valid;
            public Vector3 focusPosition;
            public Vector3 lookAngle;
            public float focusDistance;
            public bool isOrtho;
        }

        private struct MouseInputSessionState {
            public MousePivotCameraInput input;
            public bool enabled;
            public bool allowOrbit;
            public bool allowPan;
            public bool allowZoom;
            public bool allowZoomWhenOverUI;
        }

        public static bool IsStudioOpen { get; private set; }

        private static readonly Color BackdropColor = new Color(0.1f, 0.1f, 0.1f, 0.42f);
        private static readonly Color CanvasColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        private static readonly Color GridMinorColor = new Color(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color GridMajorColor = new Color(0.24f, 0.24f, 0.24f, 1f);
        private static readonly Color OuterLoopColor = new Color(0.44f, 0.85f, 0.69f, 1f);
        private static readonly Color CutoutLoopColor = new Color(0.99f, 0.65f, 0.3f, 1f);
        private static readonly Color HoleColor = new Color(0.57f, 0.79f, 1f, 1f);
        private static readonly Color ReferenceOutlineColor = new Color(0.72f, 0.78f, 0.9f, 0.55f);
        private static readonly Color ReferenceHoleColor = new Color(0.74f, 0.84f, 1f, 0.52f);
        private static readonly Color SelectedColor = new Color(1f, 0.92f, 0.5f, 1f);
        private static readonly Color DimensionLineColor = new Color(0.83f, 0.93f, 1f, 1f);
        private static readonly Color DimensionGuideColor = new Color(0.5f, 0.61f, 0.74f, 1f);
        private static readonly Color DimensionLabelBackground = new Color(0.1f, 0.1f, 0.1f, 0.96f);
        private static readonly Color UiBlue = new Color(0f, 0.46666667f, 0.78431374f, 1f);
        private static readonly Color UiBlueDark = new Color(0f, 0.44744f, 0.752f, 1f);
        private static readonly Color UiBlueHot = new Color(0f, 0.595f, 1f, 1f);
        private static readonly Color UiButtonNormal = new Color(0.26f, 0.26f, 0.26f, 1f);
        private static readonly Color UiButtonHover = new Color(0.32f, 0.32f, 0.32f, 1f);
        private static readonly Color UiDark10 = new Color(0.1f, 0.1f, 0.1f, 1f);
        private static readonly Color UiBorder = new Color(0.26f, 0.26f, 0.26f, 1f);
        private static readonly Color UiInputNormal = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color UiInputHover = new Color(0.28f, 0.28f, 0.28f, 1f);
        private static readonly Color UiInputFocused = new Color(0f, 0.35301542f, 0.59599996f, 1f);
        private static readonly Color UiTextPrimary = new Color(0.9f, 0.9f, 0.9f, 1f);
        private static readonly Color UiTextMuted = new Color(0.77f, 0.8f, 0.84f, 1f);
        private static readonly Color UiWarning = new Color(1f, 0.86f, 0.35f, 1f);
        private static readonly Color UiDanger = new Color(0.66400003f, 0.246344f, 0.27062646f, 1f);
        private static readonly string[] TabNames = { "Outline", "Cutouts", "Holes", "Part" };
        private static readonly string[] ToolNames = { "Select", "Add Point", "Curve", "Hole" };
        private static readonly string[] HoleShapeNames = { "Circle", "Square" };
        private static readonly CustomHoleShape[] HoleShapeOptions = {
            CustomHoleShape.Circle,
            CustomHoleShape.Square
        };

        private const float TopBarHeight = 64f;
        private float LeftPanelWidth => Screen.width < 1150 ? 172f : 208f;
        private float RightPanelWidth => Screen.width < 1150 ? 264f : 300f;
        private const float BottomMargin = 42f;
        private const float CanvasPadding = 8f;
        private const float PanelDimMargin = 2f;
        private const float DefaultZoomScale = 90f;
        private const float MinZoom = 0.2f;
        private const float MaxZoom = 6f;
        private const float HandleHitRadiusPx = 14f;
        private const float BezierSegmentHitRadiusPx = 24f;
        private const float BezierDragStartThresholdPx = 4f;
        private const float DefaultSnapStepInches = 1f / 16f;
        private const float MinSnapStepInches = 1f / 128f;
        private const float MaxSnapStepInches = 2f;
        private const int MaxHistoryStates = 128;

        private Placement placement;
        private readonly List<Behaviour> disabledBehaviours = new List<Behaviour>();

        private bool studioOpen;
        private bool hasUnsavedChanges;
        private bool showCloseDialog;
        private bool showLibrary;
        private string librarySearch = string.Empty;
        private Action pendingStudioAction;
        private string validatedHash;
        private string requestedGeometryHash;
        private bool geometryPending;
        private CustomPartMeshBuilder.GeometryData validatedGeometry;
        private readonly CustomGeometryWorker geometryWorker = new CustomGeometryWorker();
        private bool geometryValid;
        private float nextValidationTime;
        private bool fitOnNextCanvas;
        private bool typingInField;
        private bool drawingCanvas;
        private bool showDimensionOverlay = true;
        private bool snapToGrid = true;
        private float snapStepInches = DefaultSnapStepInches;

        private StudioTab activeTab = StudioTab.Outline;
        private CanvasTool activeTool = CanvasTool.Select;

        private CustomPartDefinition workingDefinition;
        private string sourceDefinitionId = string.Empty;
        private StudioLaunchMode launchMode = StudioLaunchMode.Definition;
        private GameObject editTargetObject;
        private Transform editSurfaceTransform;
        private MeshFilter editSurfaceMeshFilter;
        private string editSourceDefinitionId = string.Empty;
        private string editSourceInstanceId = string.Empty;
        private CustomPartDefinition referenceDefinitionForEdit;

        private int activeLoopIndex = -1; // -1 is outer loop; 0+ are cutouts.
        private int selectedAnchorIndex = -1;
        private int selectedHoleIndex = -1;
        private int selectedSegmentIndex = -1;

        private bool draggingAnchor;
        private bool draggingCanvas;
        private bool draggingHole;
        private bool draggingBezierBend;
        private bool panningWithPrimaryDrag;
        private SelectedHandle draggingHandle;
        private Vector2 draggingHoleOffset;
        private Vector2 lastMousePosition;
        private int draggingBezierSegmentIndex = -1;
        private int draggingBezierLoopIndex = int.MinValue;
        private Vector2 bezierDragStartMouse;
        private bool bezierDragMoved;

        private Vector2 libraryScroll;
        private Vector2 rightPanelScroll;
        private Vector2 canvasOrigin = Vector2.zero;
        private float canvasZoom = 1f;
        private Rect canvasRect;

        private readonly Dictionary<string, string> fieldCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<CustomPartDefinition> history = new List<CustomPartDefinition>();
        private int historyIndex = -1;
        private bool historyPending;
        private bool applyingHistory;
        private string lastAnchorFieldSelectionKey = string.Empty;
        private string lastHoleFieldSelectionKey = string.Empty;

        private GUIStyle panelStyle;
        private GUIStyle headerStyle;
        private GUIStyle labelStyle;
        private GUIStyle mutedLabelStyle;
        private GUIStyle wrappedMutedLabelStyle;
        private GUIStyle buttonStyle;
        private GUIStyle primaryButtonStyle;
        private GUIStyle dangerButtonStyle;
        private GUIStyle miniButtonStyle;
        private GUIStyle selectedButtonStyle;
        private GUIStyle toolbarButtonStyle;
        private GUIStyle selectionGridStyle;
        private GUIStyle toggleStyle;
        private GUIStyle textFieldStyle;
        private GUIStyle dimensionLabelStyle;

        private Texture2D oneByOne;
        private readonly Dictionary<int, Texture2D> colorTextureCache = new Dictionary<int, Texture2D>();
        private Coroutine restoreAfterCloseRoutine;
        private CameraSessionState cameraSession;
        private bool restoreCameraTransformOnClose;
        private readonly List<MouseInputSessionState> cameraInputSessionStates = new List<MouseInputSessionState>();
        private bool livePreviewDirty;
        private bool livePreviewApplied;
        private bool livePreviewCommitted;
        private float livePreviewNextApplyTime;
        private string livePreviewOriginalDefinitionId = string.Empty;
        private string livePreviewOriginalInstanceId = string.Empty;

        private const float LivePreviewApplyIntervalSeconds = 1f / 30f;
        private const float LivePanelAlpha = 0.92f;
        private const float LiveCanvasAlpha = 0.55f;

        private bool IsStandaloneDefinitionMode => launchMode == StudioLaunchMode.Definition;

        private bool UseSceneAlignedCanvas =>
            launchMode == StudioLaunchMode.EditSelectedInstance
            && GetActiveSurfaceTransform() != null
            && PivotCamera.Main != null
            && PivotCamera.Main.camera != null;

        private float CurrentPanelAlpha => IsStandaloneDefinitionMode ? 1f : LivePanelAlpha;
        private float CurrentCanvasAlpha => IsStandaloneDefinitionMode ? 1f : LiveCanvasAlpha;

        private bool TryGetSceneSketchBasis(out Vector3 originWorld, out Vector3 axisXWorld, out Vector3 axisYWorld, out Vector3 normalWorld) {
            originWorld = Vector3.zero;
            axisXWorld = Vector3.right;
            axisYWorld = Vector3.up;
            normalWorld = Vector3.forward;

            Transform targetTransform = GetActiveSurfaceTransform();
            if (targetTransform == null) {
                return false;
            }

            originWorld = targetTransform.TransformPoint(Vector3.zero);
            Vector3 sampleXWorld = targetTransform.TransformPoint(Vector3.right);
            Vector3 sampleYWorld = targetTransform.TransformPoint(Vector3.up);

            axisXWorld = sampleXWorld - originWorld;
            axisYWorld = sampleYWorld - originWorld;
            if (axisXWorld.sqrMagnitude < 0.000001f) {
                axisXWorld = targetTransform.right;
            }
            if (axisYWorld.sqrMagnitude < 0.000001f) {
                axisYWorld = targetTransform.up;
            }

            normalWorld = Vector3.Cross(axisXWorld, axisYWorld);
            if (normalWorld.sqrMagnitude < 0.000001f) {
                normalWorld = targetTransform.forward;
            }

            axisXWorld.Normalize();
            axisYWorld.Normalize();
            normalWorld.Normalize();
            return true;
        }

        private float GetSceneSketchLocalZOffset() {
            if (!UseSceneAlignedCanvas || !TryGetSceneSketchBasis(out _, out _, out _, out Vector3 normalWorld)) {
                return 0f;
            }

            Camera cam = PivotCamera.Main != null ? PivotCamera.Main.camera : null;
            if (cam == null) {
                return 0f;
            }

            // In live edit mode, anchor the sketch directly to the selected mesh face so it stays glued to geometry.
            if (TryGetEditTargetMeshLocalFaceZ(out float positiveFaceZ, out float negativeFaceZ)) {
                return Vector3.Dot(cam.transform.forward, normalWorld) >= 0f ? negativeFaceZ : positiveFaceZ;
            }

            if (workingDefinition == null) {
                return 0f;
            }

            float halfThickness = Mathf.Max(0.001f, workingDefinition.thicknessInches) * 0.5f;
            return Vector3.Dot(cam.transform.forward, normalWorld) >= 0f ? -halfThickness : halfThickness;
        }

        private bool TryGetEditTargetMeshLocalFaceZ(out float positiveFaceZ, out float negativeFaceZ) {
            positiveFaceZ = 0f;
            negativeFaceZ = 0f;
            MeshFilter meshFilter = editSurfaceMeshFilter;
            if (meshFilter == null || meshFilter.sharedMesh == null) {
                Transform surface = GetActiveSurfaceTransform();
                if (surface != null) {
                    meshFilter = surface.GetComponent<MeshFilter>();
                    if (meshFilter == null || meshFilter.sharedMesh == null) {
                        meshFilter = surface.GetComponentInChildren<MeshFilter>();
                    }
                }
            }

            if (meshFilter == null || meshFilter.sharedMesh == null) {
                meshFilter = editTargetObject != null ? editTargetObject.GetComponent<MeshFilter>() : null;
                if (meshFilter == null || meshFilter.sharedMesh == null) {
                    meshFilter = editTargetObject != null ? editTargetObject.GetComponentInChildren<MeshFilter>() : null;
                }
            }

            if (meshFilter == null || meshFilter.sharedMesh == null) {
                return false;
            }

            Bounds bounds = meshFilter.sharedMesh.bounds;
            positiveFaceZ = bounds.max.z;
            negativeFaceZ = bounds.min.z;
            return positiveFaceZ - negativeFaceZ > 0.000001f;
        }

        private static Vector2 GuiToScreenPixels(Vector2 guiPoint) {
            Vector2 topLeftScreen = GUIUtility.GUIToScreenPoint(guiPoint);
            return new Vector2(topLeftScreen.x, Screen.height - topLeftScreen.y);
        }

        private static Vector2 ScreenPixelsToGui(Vector3 screenPoint) {
            Vector2 topLeftScreen = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return GUIUtility.ScreenToGUIPoint(topLeftScreen);
        }

        private Transform GetActiveSurfaceTransform() {
            if (editSurfaceTransform != null) {
                return editSurfaceTransform;
            }

            return editTargetObject != null ? editTargetObject.transform : null;
        }

        private void ResolveEditSurface(GameObject selectedObject) {
            editSurfaceTransform = null;
            editSurfaceMeshFilter = null;
            if (selectedObject == null) {
                return;
            }

            MeshFilter meshFilter = selectedObject.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) {
                meshFilter = selectedObject.GetComponentInChildren<MeshFilter>();
            }

            if (meshFilter != null && meshFilter.sharedMesh != null) {
                editSurfaceMeshFilter = meshFilter;
                editSurfaceTransform = meshFilter.transform;
                return;
            }

            editSurfaceTransform = selectedObject.transform;
        }

        private void Awake() {
            DontDestroyOnLoad(gameObject);
            EnsureReferences();
        }

        private void OnDestroy() {
            geometryWorker.Dispose();
            if (studioOpen) {
                studioOpen = false;
                IsStudioOpen = false;
            }

            if (restoreAfterCloseRoutine != null) {
                StopCoroutine(restoreAfterCloseRoutine);
                restoreAfterCloseRoutine = null;
            }

            EndCameraStudioSession();
            MouseInput.SetForceOverUI(false);
            RestoreAfterClose();
            DisposeGuiResources();
        }

        private void Update() {
            geometryWorker.Tick();
            EnsureReferences();

            if (!studioOpen) {
                return;
            }

            if (!showCloseDialog) {
                RefreshGeometryValidation(false);
                ApplyLivePreviewIfNeeded(force: false);
            }
            HandleUndoRedoHotkeys();
            CommitHistoryIfReady();

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) {
                if (showCloseDialog) {
                    showCloseDialog = false;
                    return;
                }
                RequestCloseStudio();
            }
        }

        private void EnsureReferences() {
            if (placement == null) {
                placement = FindObjectOfType<Placement>();
            }
        }

        private bool IsProjectContextReady() {
            return placement != null && PivotCamera.Main != null;
        }

        private void EnsureGuiResources() {
            if (oneByOne == null) {
                oneByOne = new Texture2D(1, 1, TextureFormat.RGBA32, false) {
                    hideFlags = HideFlags.HideAndDontSave
                };
                oneByOne.SetPixel(0, 0, Color.white);
                oneByOne.Apply();
            }

            if (panelStyle == null) {
                panelStyle = new GUIStyle(GUI.skin.box) {
                    padding = new RectOffset(12, 12, 10, 10)
                };
                panelStyle.normal.textColor = UiTextPrimary;
            }

            // In live edit mode we let DrawFilledRect control translucency, so the area style stays transparent.
            float panelStyleAlpha = IsStandaloneDefinitionMode ? 1f : 0f;
            panelStyle.normal.background = GetSolidTexture(new Color(UiDark10.r, UiDark10.g, UiDark10.b, panelStyleAlpha));

            if (headerStyle == null) {
                headerStyle = new GUIStyle(GUI.skin.label) {
                    fontSize = 15,
                    fontStyle = FontStyle.Bold
                };
                headerStyle.normal.textColor = UiTextPrimary;
            }

            if (labelStyle == null) {
                labelStyle = new GUIStyle(GUI.skin.label) {
                    fontSize = 13,
                    fontStyle = FontStyle.Normal
                };
                labelStyle.normal.textColor = UiTextPrimary;
            }

            if (mutedLabelStyle == null) {
                mutedLabelStyle = new GUIStyle(labelStyle);
                mutedLabelStyle.normal.textColor = UiTextMuted;
            }

            if (wrappedMutedLabelStyle == null) {
                wrappedMutedLabelStyle = new GUIStyle(mutedLabelStyle) {
                    wordWrap = true
                };
            }

            if (buttonStyle == null) {
                buttonStyle = CreateButtonStyle(UiButtonNormal, UiButtonHover, UiBlue, UiTextPrimary, FontStyle.Normal, 13);
            }

            if (primaryButtonStyle == null) {
                primaryButtonStyle = CreateButtonStyle(UiBlue, UiBlueHot, UiBlueDark, Color.white, FontStyle.Bold, 13);
            }

            if (dangerButtonStyle == null) {
                Color hover = Color.Lerp(UiDanger, Color.white, 0.15f);
                Color active = Color.Lerp(UiDanger, UiDark10, 0.2f);
                dangerButtonStyle = CreateButtonStyle(UiDanger, hover, active, Color.white, FontStyle.Bold, 13);
            }

            if (miniButtonStyle == null) {
                miniButtonStyle = new GUIStyle(dangerButtonStyle) {
                    fontSize = 11,
                    padding = new RectOffset(6, 6, 5, 5)
                };
            }

            if (selectedButtonStyle == null) {
                selectedButtonStyle = CreateButtonStyle(
                    UiBlueDark,
                    UiBlueHot,
                    UiBlue,
                    Color.white,
                    FontStyle.Bold,
                    12);
            }

            if (toolbarButtonStyle == null) {
                toolbarButtonStyle = CreateButtonStyle(UiButtonNormal, UiButtonHover, UiBlue, UiTextPrimary, FontStyle.Bold, 13);
                toolbarButtonStyle.onNormal.background = GetSolidTexture(UiBlueDark);
                toolbarButtonStyle.onHover.background = GetSolidTexture(UiBlueHot);
                toolbarButtonStyle.onActive.background = GetSolidTexture(UiBlue);
                toolbarButtonStyle.onFocused.background = GetSolidTexture(UiBlueDark);
                toolbarButtonStyle.padding = new RectOffset(8, 8, 6, 6);
            }

            if (selectionGridStyle == null) {
                selectionGridStyle = CreateButtonStyle(UiButtonNormal, UiButtonHover, UiBlue, UiTextPrimary, FontStyle.Normal, 13);
                selectionGridStyle.onNormal.background = GetSolidTexture(UiBlueDark);
                selectionGridStyle.onHover.background = GetSolidTexture(UiBlueHot);
                selectionGridStyle.onActive.background = GetSolidTexture(UiBlue);
                selectionGridStyle.onFocused.background = GetSolidTexture(UiBlueDark);
                selectionGridStyle.alignment = TextAnchor.MiddleLeft;
                selectionGridStyle.padding = new RectOffset(10, 8, 6, 6);
            }

            if (toggleStyle == null) {
                toggleStyle = new GUIStyle(GUI.skin.toggle) {
                    fontSize = 13,
                    fontStyle = FontStyle.Normal
                };
                SetAllTextColors(toggleStyle, UiTextPrimary);
            }

            if (textFieldStyle == null) {
                textFieldStyle = new GUIStyle(GUI.skin.textField) {
                    fontSize = 13,
                    padding = new RectOffset(8, 8, 5, 5)
                };

                Texture2D normalTex = GetSolidTexture(UiInputNormal);
                Texture2D hoverTex = GetSolidTexture(UiInputHover);
                Texture2D activeTex = GetSolidTexture(UiInputFocused);
                SetInteractiveBackgrounds(textFieldStyle, normalTex, hoverTex, activeTex);
                SetInteractiveTextColors(textFieldStyle, UiTextPrimary);
                textFieldStyle.alignment = TextAnchor.MiddleLeft;
            }

            if (dimensionLabelStyle == null) {
                dimensionLabelStyle = new GUIStyle(GUI.skin.label) {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                dimensionLabelStyle.normal.textColor = DimensionLineColor;
            }
        }

        private GUIStyle CreateButtonStyle(
            Color normalColor,
            Color hoverColor,
            Color activeColor,
            Color textColor,
            FontStyle fontStyle,
            int fontSize) {
            GUIStyle style = new GUIStyle(GUI.skin.button) {
                fontStyle = fontStyle,
                fontSize = fontSize,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(10, 10, 5, 5)
            };

            Texture2D normal = GetSolidTexture(normalColor);
            Texture2D hover = GetSolidTexture(hoverColor);
            Texture2D active = GetSolidTexture(activeColor);
            SetButtonBackgrounds(style, normal, hover, active);
            SetAllTextColors(style, textColor);
            return style;
        }

        private static void SetInteractiveBackgrounds(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active) {
            style.normal.background = normal;
            style.hover.background = hover;
            style.focused.background = active;
            style.active.background = active;
        }

        private static void SetButtonBackgrounds(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active) {
            style.normal.background = normal;
            style.hover.background = hover;
            style.focused.background = hover;
            style.active.background = active;
            style.onNormal.background = active;
            style.onHover.background = hover;
            style.onFocused.background = active;
            style.onActive.background = active;
        }

        private static void SetInteractiveTextColors(GUIStyle style, Color color) {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.focused.textColor = color;
            style.active.textColor = color;
        }

        private static void SetAllTextColors(GUIStyle style, Color color) {
            SetInteractiveTextColors(style, color);
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onFocused.textColor = color;
            style.onActive.textColor = color;
        }

        private Texture2D GetSolidTexture(Color color) {
            int key = GetColorCacheKey(color);
            if (!colorTextureCache.TryGetValue(key, out Texture2D texture) || texture == null) {
                texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) {
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.SetPixel(0, 0, color);
                texture.Apply();
                colorTextureCache[key] = texture;
            }

            return texture;
        }

        private static int GetColorCacheKey(Color color) {
            Color32 c = color;
            return c.r | (c.g << 8) | (c.b << 16) | (c.a << 24);
        }

        private void DisposeGuiResources() {
            if (oneByOne != null) {
                Destroy(oneByOne);
                oneByOne = null;
            }

            foreach (Texture2D texture in colorTextureCache.Values) {
                if (texture != null) {
                    Destroy(texture);
                }
            }
            colorTextureCache.Clear();
        }

        private void OnGUI() {
            if (!studioOpen) return;
            EnsureGuiResources();

            EnsureWorkingDefinition();
            GUI.enabled = !showCloseDialog;
            DrawStudioBackdrop();
            DrawTopBar();
            DrawLeftPanel();
            DrawRightPanel();
            DrawCanvas();
            DrawWorkspaceFooter();
            typingInField = !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());
            GUI.enabled = true;

            if (showCloseDialog) {
                DrawCloseDialog();
            }
        }

        public void OpenFromBrowser() {
            if (studioOpen) return;
            EnsureReferences();
            TryGetSelectedCustomPartFromSelectionManagers(out SavedObject selectedAtOpen);
            OpenStudio(selectedAtOpen);
        }

        private void DrawStudioBackdrop() {
            Rect topBarRect = new Rect(0f, 0f, Screen.width, TopBarHeight + PanelDimMargin);
            Rect leftPanelRect = new Rect(0f, TopBarHeight, LeftPanelWidth + PanelDimMargin, Screen.height - TopBarHeight);
            Rect rightPanelRect = new Rect(Screen.width - RightPanelWidth - PanelDimMargin, TopBarHeight, RightPanelWidth + PanelDimMargin, Screen.height - TopBarHeight);

            Color backdrop = IsStandaloneDefinitionMode ? UiDark10 : BackdropColor;
            DrawFilledRect(topBarRect, backdrop);
            DrawFilledRect(leftPanelRect, backdrop);
            DrawFilledRect(rightPanelRect, backdrop);
        }

        private void DrawTopBar() {
            DrawFilledRect(new Rect(0, 0, Screen.width, TopBarHeight), UiDark10);
            DrawFilledRect(new Rect(0, TopBarHeight - 1, Screen.width, 1), UiBorder);
            GUI.Label(new Rect(16, 10, 155, 24), "Poly Maker", headerStyle);
            GUI.Label(new Rect(16, 34, 220, 22), IsStandaloneDefinitionMode ? "Design a custom plate" : "Editing selected part", mutedLabelStyle);
            float right = Screen.width - 16;
            if (GUI.Button(new Rect(right - 68, 16, 68, 32), "Close", buttonStyle)) RequestCloseStudio();
            GUI.enabled = !showCloseDialog && geometryValid;
            if (GUI.Button(new Rect(right - 220, 16, 144, 32), IsStandaloneDefinitionMode ? "Insert part" : "Apply changes", primaryButtonStyle)) FinishAndInsert();
            GUI.enabled = !showCloseDialog;
            if (GUI.Button(new Rect(right - 362, 16, 134, 32), "Save to library", buttonStyle)) SaveWorkingDefinition();
            if (Screen.width > 950) GUI.Label(new Rect(LeftPanelWidth + 16, 21, Screen.width - LeftPanelWidth - 402, 24), workingDefinition.name + (hasUnsavedChanges ? "  *" : ""), labelStyle);
        }

        private void DrawLeftPanel() {
            var rect = new Rect(0, TopBarHeight, LeftPanelWidth, Screen.height - TopBarHeight - BottomMargin);
            DrawFilledRect(rect, new Color(UiDark10.r, UiDark10.g, UiDark10.b, CurrentPanelAlpha));
            GUILayout.BeginArea(rect, panelStyle);
            GUILayout.Label("WORKSPACE", mutedLabelStyle);
            string[] steps = { "Outline", "Cutouts", "Holes", "Part settings" };
            for (int i = 0; i < steps.Length; i++) {
                if (GUILayout.Button(steps[i], (int)activeTab == i ? selectedButtonStyle : buttonStyle, GUILayout.Height(34))) {
                    activeTab = (StudioTab)i;
                    rightPanelScroll = Vector2.zero;
                    ResetDragState();
                    GUI.FocusControl(null);
                    if (activeTab == StudioTab.Outline) activeLoopIndex = -1;
                    if (activeTab == StudioTab.Holes) SetActiveTool(CanvasTool.AddHole);
                    else SetActiveTool(CanvasTool.Select);
                }
                GUILayout.Space(3);
            }
            GUILayout.Space(18);
            GUILayout.Label("START A PART", mutedLabelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rectangle", buttonStyle, GUILayout.Height(32))) RequestStudioAction(() => CreatePreset(false));
            if (GUILayout.Button("Circle", buttonStyle, GUILayout.Height(32))) RequestStudioAction(() => CreatePreset(true));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Duplicate current", buttonStyle, GUILayout.Height(30))) DuplicateWorkingDefinition();
            GUILayout.Space(14);
            if (GUILayout.Button(showLibrary ? "Library  -" : "Library  +", showLibrary ? selectedButtonStyle : buttonStyle, GUILayout.Height(32))) showLibrary = !showLibrary;
            if (showLibrary) {
                GUI.SetNextControlName("library_search");
                librarySearch = GUILayout.TextField(librarySearch, textFieldStyle, GUILayout.Height(28));
                libraryScroll = GUILayout.BeginScrollView(libraryScroll);
                int count = 0;
                foreach (CustomPartDefinition definition in CustomPartRegistry.GetAllDefinitions().OrderBy(d => d.name)) {
                    if (definition.name.IndexOf(librarySearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    count++;
                    bool selected = workingDefinition.definitionId == definition.definitionId;
                    if (GUILayout.Button(new GUIContent(definition.name, definition.name), selected ? selectedButtonStyle : buttonStyle, GUILayout.Height(32))) {
                        var next = definition;
                        RequestStudioAction(() => LoadDefinition(next));
                    }
                }
                if (count == 0) GUILayout.Label("No saved parts yet.", wrappedMutedLabelStyle);
                GUILayout.EndScrollView();
            } else GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Import", buttonStyle, GUILayout.Height(30))) RequestStudioAction(ImportDefinition);
            if (GUILayout.Button("Export", buttonStyle, GUILayout.Height(30))) ExportDefinition();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawRightPanel() {
            var rect = new Rect(Screen.width - RightPanelWidth, TopBarHeight, RightPanelWidth, Screen.height - TopBarHeight - BottomMargin);
            DrawFilledRect(rect, new Color(UiDark10.r, UiDark10.g, UiDark10.b, CurrentPanelAlpha));
            GUILayout.BeginArea(rect, panelStyle);
            rightPanelScroll = GUILayout.BeginScrollView(rightPanelScroll);
            switch (activeTab) {
                case StudioTab.Outline: DrawOutlineInspector(); break;
                case StudioTab.Cutouts: DrawCutoutsInspector(); break;
                case StudioTab.Holes: DrawHolesInspector(); break;
                case StudioTab.Part: DrawPartInspector(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawOutlineInspector() {
            EnsureWorkingDefinition();
            GUILayout.Label("Outline", headerStyle);
            GUILayout.Label("Select a point to edit its coordinates, or choose a drawing tool above the canvas.", wrappedMutedLabelStyle);
            GUILayout.Space(12);
            if (TryGetOuterBounds(out Vector2 min, out Vector2 max)) {
                Vector2 size = max - min;
                float width = FloatField("outline_width", "Width (in)", size.x);
                float height = FloatField("outline_height", "Height (in)", size.y);
                if (width > 0.01f && height > 0.01f && (!Mathf.Approximately(width, size.x) || !Mathf.Approximately(height, size.y))) ResizeOutline(new Vector2(width, height));
            }
            GUILayout.Space(12);
            showDimensionOverlay = GUILayout.Toggle(showDimensionOverlay, "Show dimensions", toggleStyle);
            snapToGrid = GUILayout.Toggle(snapToGrid, "Snap to grid", toggleStyle);
            snapStepInches = Mathf.Clamp(FloatField("snap_step", "Grid step (in)", snapStepInches), MinSnapStepInches, MaxSnapStepInches);
            GUILayout.Space(12);
            string[] loopNames = BuildLoopNames();
            int previousLoopIndex = activeLoopIndex;
            bool hasLoopChoices = loopNames.Length > 1;
            if (hasLoopChoices) {
                GUILayout.Space(4f);
                GUILayout.Label("Active Loop", mutedLabelStyle);
                int loopSelection = Mathf.Clamp(activeLoopIndex + 1, 0, Mathf.Max(0, loopNames.Length - 1));
                loopSelection = GUILayout.SelectionGrid(loopSelection, loopNames, 1, selectionGridStyle);
                activeLoopIndex = loopSelection - 1;
            }
            else {
                activeLoopIndex = -1;
                GUILayout.Space(4f);
                GUILayout.Label("Active Loop: Outline", mutedLabelStyle);
            }

            if (activeLoopIndex != previousLoopIndex) {
                selectedAnchorIndex = -1;
                selectedSegmentIndex = -1;
                ResetDragState();
                lastAnchorFieldSelectionKey = string.Empty;
            }

            LoopData loop = GetActiveLoop();
            if (loop == null) {
                GUILayout.Label("No loop selected.", mutedLabelStyle);
                return;
            }

            GUILayout.Space(8f);
            GUILayout.Label($"Anchors: {loop.anchors.Length}", mutedLabelStyle);

            if (selectedAnchorIndex >= 0 && selectedAnchorIndex < loop.anchors.Length) {
                AnchorData anchor = loop.anchors[selectedAnchorIndex];
                SyncAnchorFieldsToSelection(anchor);
                GUILayout.Space(6f);
                GUILayout.Label("Selected Anchor", headerStyle);
                Vector2 oldPosition = anchor.position;

                float x = FloatField("anchor_x", "X (in)", anchor.position.x);
                float y = FloatField("anchor_y", "Y (in)", anchor.position.y);

                if (!Mathf.Approximately(x, oldPosition.x) || !Mathf.Approximately(y, oldPosition.y)) {
                    anchor.position = new Vector2(x, y);
                    MarkDirty();
                }
            }
            else {
                GUILayout.Space(8f);
                GUILayout.Label($"Select and drag a node. Add Point inserts on '{GetActiveLoopDisplayName()}'.", wrappedMutedLabelStyle);
                lastAnchorFieldSelectionKey = string.Empty;
            }

            GUILayout.Space(6f);
            if (activeTool == CanvasTool.Select) {
                GUILayout.Label("Select: click + drag nodes. Click handle dots to adjust curves. Delete: Delete/Backspace.", wrappedMutedLabelStyle);
            }
            else if (activeTool == CanvasTool.AddPoint) {
                GUILayout.Label("Add Point: click a segment to insert a node exactly where you click.", wrappedMutedLabelStyle);
            }
            else if (activeTool == CanvasTool.ToggleBezier) {
                GUILayout.Label("Curve: click a segment to select it, drag to bend. Shift+click a curved segment to make it straight.", wrappedMutedLabelStyle);
                GUILayout.Label("Drag the blue handle dots directly in Curve mode for fine control-point tweaks.", wrappedMutedLabelStyle);
            }
            else if (activeTool == CanvasTool.AddHole) {
                GUILayout.Label("Hole: click the canvas to add a new hole.", wrappedMutedLabelStyle);
            }
        }

        private void SetActiveTool(CanvasTool tool) {
            if (activeTool == tool) {
                return;
            }

            activeTool = tool;
            ResetDragState();
            if (activeTool != CanvasTool.ToggleBezier) {
                selectedSegmentIndex = -1;
            }
        }

        private void StopBezierBendDrag() {
            draggingBezierBend = false;
            draggingBezierSegmentIndex = -1;
            draggingBezierLoopIndex = int.MinValue;
            bezierDragMoved = false;
        }

        private void ResetDragState() {
            draggingAnchor = false;
            draggingHole = false;
            draggingHandle = default;
            StopBezierBendDrag();
        }

        private void ClearSegmentSelection() {
            selectedSegmentIndex = -1;
            StopBezierBendDrag();
        }

        private void ResetPanState() {
            draggingCanvas = false;
            panningWithPrimaryDrag = false;
        }

        private void DrawCutoutsInspector() {
            EnsureWorkingDefinition();
            GUILayout.Label("Cutout Loops", headerStyle);
            if (GUILayout.Button("Add Rect Cutout", primaryButtonStyle, GUILayout.Height(28f))) {
                AddDefaultCutout();
            }

            GUILayout.Space(6f);
            LoopData[] cutouts = workingDefinition.sketch.cutoutLoops ?? Array.Empty<LoopData>();
            for (int i = 0; i < cutouts.Length; i++) {
                LoopData loop = cutouts[i];
                GUILayout.BeginHorizontal();
                bool isActive = activeLoopIndex == i;
                GUIStyle style = isActive ? selectedButtonStyle : buttonStyle;
                if (GUILayout.Button(loop.name, style, GUILayout.Height(24f))) {
                    activeLoopIndex = i;
                    selectedAnchorIndex = -1;
                    ResetDragState();
                    selectedSegmentIndex = -1;
                }

                if (GUILayout.Button("X", miniButtonStyle, GUILayout.Width(26f), GUILayout.Height(24f))) {
                    DeleteCutoutAt(i);
                    break;
                }

                GUILayout.EndHorizontal();
            }

            if (cutouts.Length == 0) {
                GUILayout.Space(6f);
                GUILayout.Label("No cutouts yet.", mutedLabelStyle);
            }
        }

        private void DrawHolesInspector() {
            EnsureWorkingDefinition();
            GUILayout.Label("Holes", headerStyle);

            if (GUILayout.Button("Add Hole At Origin", primaryButtonStyle, GUILayout.Height(28f))) {
                AddHole(Vector2.zero);
            }

            GUILayout.Space(8f);
            CustomHoleDefinition[] holes = workingDefinition.holes ?? Array.Empty<CustomHoleDefinition>();
            for (int i = 0; i < holes.Length; i++) {
                CustomHoleShape displayShape = holes[i] != null
                    ? NormalizeHoleShape(holes[i].shape)
                    : CustomHoleShape.Circle;
                bool isSelected = i == selectedHoleIndex;
                GUIStyle style = isSelected ? selectedButtonStyle : buttonStyle;
                if (GUILayout.Button($"Hole {i + 1} - {displayShape}", style, GUILayout.Height(24f))) {
                    selectedHoleIndex = i;
                    SetActiveTool(CanvasTool.Select);
                    draggingHole = false;
                }
            }

            if (selectedHoleIndex < 0 || selectedHoleIndex >= holes.Length) {
                lastHoleFieldSelectionKey = string.Empty;
                return;
            }

            CustomHoleDefinition hole = holes[selectedHoleIndex];
            bool holePolicyChanged = false;
            CustomHoleShape normalizedShape = NormalizeHoleShape(hole.shape);
            if (hole.shape != normalizedShape) {
                hole.shape = normalizedShape;
                holePolicyChanged = true;
            }
            if (hole.holeType != HoleCollider.HoleType.Normal) {
                hole.holeType = HoleCollider.HoleType.Normal;
                holePolicyChanged = true;
            }
            if (!hole.twoSided) {
                hole.twoSided = true;
                holePolicyChanged = true;
            }
            if (holePolicyChanged) {
                MarkDirty();
            }

            SyncHoleFieldsToSelection(hole);
            GUILayout.Space(8f);
            GUILayout.Label("Selected Hole", headerStyle);
            int selectedShapeIndex = GetHoleShapeSelectionIndex(hole.shape);
            selectedShapeIndex = GUILayout.Toolbar(selectedShapeIndex, HoleShapeNames, toolbarButtonStyle);
            selectedShapeIndex = Mathf.Clamp(selectedShapeIndex, 0, HoleShapeOptions.Length - 1);
            CustomHoleShape newShape = HoleShapeOptions[selectedShapeIndex];
            float x = FloatField("hole_x", "Center X (in)", hole.position.x);
            float y = FloatField("hole_y", "Center Y (in)", hole.position.y);
            float width = Mathf.Max(0.02f, FloatField("hole_w", "Width (in)", hole.size.x));
            float height = Mathf.Max(0.02f, FloatField("hole_h", "Height (in)", hole.size.y));
            float depth = Mathf.Max(0.01f, FloatField("hole_d", "Depth (in)", hole.depthInches));
            float rotation = FloatField("hole_r", "Rotation", hole.rotationDegrees);

            if (newShape != hole.shape
                || !Mathf.Approximately(x, hole.position.x)
                || !Mathf.Approximately(y, hole.position.y)
                || !Mathf.Approximately(width, hole.size.x)
                || !Mathf.Approximately(height, hole.size.y)
                || !Mathf.Approximately(depth, hole.depthInches)
                || !Mathf.Approximately(rotation, hole.rotationDegrees)) {
                hole.shape = newShape;
                hole.position = new Vector2(x, y);
                hole.size = new Vector2(width, height);
                hole.depthInches = depth;
                hole.rotationDegrees = rotation;
                hole.holeType = HoleCollider.HoleType.Normal;
                hole.twoSided = true;
                MarkDirty();
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("Delete Hole", dangerButtonStyle, GUILayout.Height(26f))) {
                DeleteSelectedHole();
            }
        }

        private void DrawPartInspector() {
            GUILayout.Label("Part settings", headerStyle);
            GUILayout.Space(12);
            string name = TextField("part_name", "Name", workingDefinition.name);
            if (name != workingDefinition.name) { workingDefinition.name = name; MarkDirty(); }
            float thickness = Mathf.Clamp(FloatField("part_thickness", "Thickness (in)", workingDefinition.thicknessInches), 0.01f, 2f);
            if (!Mathf.Approximately(thickness, workingDefinition.thicknessInches)) { workingDefinition.thicknessInches = thickness; MarkDirty(); }
            GUILayout.Label("Quick thickness", mutedLabelStyle);
            GUILayout.BeginHorizontal();
            float[] thicknesses = { 0.0625f, 0.125f, 0.25f };
            string[] names = { "1/16 in", "1/8 in", "1/4 in" };
            for (int i = 0; i < names.Length; i++) if (GUILayout.Button(names[i], Mathf.Approximately(thickness, thicknesses[i]) ? selectedButtonStyle : buttonStyle, GUILayout.Height(30))) {
                workingDefinition.thicknessInches = thicknesses[i]; fieldCache.Remove("part_thickness"); MarkDirty();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(20);
            GUILayout.Label("Geometry", headerStyle);
            GUILayout.Label($"{workingDefinition.sketch.outerLoop.anchors.Length} outline points", mutedLabelStyle);
            GUILayout.Label($"{workingDefinition.sketch.cutoutLoops.Length} cutouts   /   {workingDefinition.holes.Length} holes", mutedLabelStyle);
            GUILayout.Space(12);
            GUILayout.Label(geometryPending ? "Checking geometry..." : geometryValid ? "Ready to insert. Your part will follow the cursor until you place it." : "Check for crossing edges, overlapping holes, or cutouts outside the outline.", wrappedMutedLabelStyle);
            GUILayout.Space(20);
            if (IsStandaloneDefinitionMode && GUILayout.Button("Update all placed copies", buttonStyle, GUILayout.Height(32))) {
                if (RefreshGeometryValidation(true)) { SaveWorkingDefinition(); CustomPartRuntimeUpdater.ApplyDefinitionToAllInstances(workingDefinition.definitionId); }
            }
        }


        private void DrawCanvasToolbar() {
            float x = canvasRect.x, y = TopBarHeight + 8;
            float buttonWidth = Mathf.Min(84, (canvasRect.width - 180) / 4);
            string[] tools = { "Select  V", "+ Point  P", "Curve  B", "Hole  H" };
            for (int i = 0; i < tools.Length; i++) {
                if (GUI.Button(new Rect(x, y, buttonWidth, 32), tools[i], (int)activeTool == i ? selectedButtonStyle : buttonStyle)) {
                    SetActiveTool((CanvasTool)i); GUI.FocusControl(null);
                    if (i == 3) activeTab = StudioTab.Holes;
                }
                x += buttonWidth + 4;
            }
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && historyIndex > 0;
            if (GUI.Button(new Rect(x, y, 50, 32), new GUIContent("Undo", "Ctrl+Z"), buttonStyle)) UndoHistory();
            x += 54;
            GUI.enabled = enabled && historyIndex < history.Count - 1;
            if (GUI.Button(new Rect(x, y, 50, 32), new GUIContent("Redo", "Ctrl+Y"), buttonStyle)) RedoHistory();
            GUI.enabled = enabled;
            if (GUI.Button(new Rect(x + 54, y, 50, 32), new GUIContent("Fit", "F"), buttonStyle)) fitOnNextCanvas = true;
        }

        private void DrawWorkspaceFooter() {
            Rect rect = new Rect(0, Screen.height - BottomMargin, Screen.width, BottomMargin);
            DrawFilledRect(rect, UiDark10);
            DrawFilledRect(new Rect(0, rect.y, rect.width, 1), UiBorder);
            var old = GUI.contentColor;
            GUI.contentColor = geometryValid ? OuterLoopColor : UiWarning;
            GUI.Label(new Rect(16, rect.y + 10, Screen.width - 220, 24), geometryPending ? "Checking geometry...  /  All dimensions in inches" : geometryValid ? "Ready to build  /  All dimensions in inches" : "Check geometry: edges must not cross; holes and cutouts must stay inside the outline.", labelStyle);
            GUI.contentColor = old;
            GUI.Label(new Rect(Screen.width - 180, rect.y + 10, 166, 24), hasUnsavedChanges ? "Unsaved design" : "Saved to library", mutedLabelStyle);
        }

        private void InvalidateGeometry() {
            geometryWorker.Cancel();
            validatedHash = requestedGeometryHash = null;
            validatedGeometry = null;
            geometryPending = true;
            geometryValid = false;
            nextValidationTime = 0;
        }

        private void AcceptGeometry(CustomPartMeshBuilder.GeometryData data, string hash) {
            if (!studioOpen || requestedGeometryHash != hash) return;
            validatedHash = hash;
            validatedGeometry = data;
            geometryPending = false;
            geometryValid = data != null && data.Valid;
        }

        private bool RefreshGeometryValidation(bool force) {
            if (workingDefinition == null) return false;
            if (!force && Time.unscaledTime < nextValidationTime) return geometryValid;
            nextValidationTime = Time.unscaledTime + .2f;
            string hash = CustomPartMeshBuilder.GeometryKey(workingDefinition);
            if (hash == validatedHash) return geometryValid;
            if (force) {
                geometryWorker.Cancel(); requestedGeometryHash = hash;
                AcceptGeometry(CustomPartMeshBuilder.Compile(workingDefinition, hash), hash);
                return geometryValid;
            }
            if (requestedGeometryHash == hash) return false;
            requestedGeometryHash = hash; geometryPending = true; geometryValid = false;
            if (CustomPartMeshBuilder.TryGetGeometry(hash, out var cached)) AcceptGeometry(cached, hash);
            else geometryWorker.RequestGeometry(workingDefinition, hash, data => AcceptGeometry(data, hash));
            return geometryValid;
        }

        private void FitCanvas() {
            if (UseSceneAlignedCanvas || !TryGetOuterBounds(out Vector2 min, out Vector2 max)) return;
            Vector2 size = max - min;
            canvasZoom = Mathf.Clamp(Mathf.Min((canvasRect.width - 120) / Mathf.Max(size.x, 0.1f), (canvasRect.height - 130) / Mathf.Max(size.y, 0.1f)) / DefaultZoomScale, MinZoom, MaxZoom);
            canvasOrigin = (min + max) * 0.5f;
        }

        private void CreatePreset(bool circle) {
            CreateNewWorkingDefinition();
            workingDefinition.name = BuildDefaultName(circle ? "Round plate" : "Rectangular plate");
            if (circle) {
                const float k = 0.55228475f;
                Vector2[] points = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
                var loop = workingDefinition.sketch.outerLoop;
                loop.anchors = points.Select(v => new AnchorData { position = v, inHandle = new Vector2(v.y, -v.x) * k, outHandle = new Vector2(-v.y, v.x) * k }).ToArray();
                loop.segmentKinds = Enumerable.Repeat(SegmentKind.Bezier, 4).ToArray();
            }
            activeTab = StudioTab.Outline;
            SetActiveTool(CanvasTool.Select);
            ResetHistoryWithCurrentDefinition();
            MarkDirty();
        }

        private void ResizeOutline(Vector2 size) {
            if (!TryGetOuterBounds(out Vector2 min, out Vector2 max)) return;
            Vector2 oldSize = max - min;
            if (oldSize.x <= 0 || oldSize.y <= 0) return;
            Vector2 scale = new Vector2(size.x / oldSize.x, size.y / oldSize.y);
            Vector2 center = (min + max) * 0.5f;
            foreach (AnchorData a in workingDefinition.sketch.outerLoop.anchors) {
                a.position = center + Vector2.Scale(a.position - center, scale);
                a.inHandle = Vector2.Scale(a.inHandle, scale);
                a.outHandle = Vector2.Scale(a.outHandle, scale);
            }
            MarkDirty();
        }

        private string GetModeBadgeText() {
            if (launchMode == StudioLaunchMode.EditSelectedInstance) {
                return "Mode: Edit Selected Instance (forks definition on Finish)";
            }

            return "Mode: Definition Editor";
        }

        private string GetContextBadgeText() {
            return launchMode == StudioLaunchMode.EditSelectedInstance
                ? "In-CAD live editing"
                : "Standalone definition editing";
        }

        private void ResolveLaunchModeAndDefinition(SavedObject selectedAtOpen) {
            launchMode = StudioLaunchMode.Definition;
            editTargetObject = null;
            editSurfaceTransform = null;
            editSurfaceMeshFilter = null;
            editSourceDefinitionId = string.Empty;
            editSourceInstanceId = string.Empty;
            referenceDefinitionForEdit = null;
            livePreviewDirty = false;
            livePreviewApplied = false;
            livePreviewCommitted = false;
            livePreviewNextApplyTime = 0f;
            livePreviewOriginalDefinitionId = string.Empty;
            livePreviewOriginalInstanceId = string.Empty;

            SavedObject selectedSavedObject = selectedAtOpen;
            if (selectedSavedObject != null
                && selectedSavedObject.gameObject != null
                && !string.IsNullOrWhiteSpace(selectedSavedObject.customDefinitionId)
                && CustomPartRegistry.TryGetDefinition(selectedSavedObject.customDefinitionId, out CustomPartDefinition selectedDefinition)) {
                launchMode = StudioLaunchMode.EditSelectedInstance;
                editTargetObject = selectedSavedObject.gameObject;
                ResolveEditSurface(editTargetObject);
                editSourceDefinitionId = selectedSavedObject.customDefinitionId;
                editSourceInstanceId = selectedSavedObject.customInstanceId ?? string.Empty;
                livePreviewOriginalDefinitionId = selectedSavedObject.customDefinitionId ?? string.Empty;
                livePreviewOriginalInstanceId = selectedSavedObject.customInstanceId ?? string.Empty;

                workingDefinition = selectedDefinition.CloneDeep();
                referenceDefinitionForEdit = selectedDefinition.CloneDeep();
                sourceDefinitionId = selectedDefinition.definitionId;
                hasUnsavedChanges = false;
                activeLoopIndex = -1;
                selectedAnchorIndex = -1;
                selectedHoleIndex = -1;
                ClearSegmentSelection();
                fieldCache.Clear();
                EnsureDefinitionIntegrity(workingDefinition);
                return;
            }

            if (workingDefinition == null) {
                if (CustomPartRegistry.Count > 0) {
                    CustomPartDefinition first = CustomPartRegistry.GetAllDefinitions()[0];
                    LoadDefinition(first);
                }
                else {
                    CreateNewWorkingDefinition();
                }
            }
            else {
                EnsureDefinitionIntegrity(workingDefinition);
            }
        }

        private bool TryGetSelectedCustomPartFromSelectionManagers(out SavedObject selectedSavedObject) {
            selectedSavedObject = null;
            SelectionManager[] selectionManagers = FindObjectsOfType<SelectionManager>(true);
            var uniqueCandidates = new HashSet<SavedObject>();
            for (int i = 0; i < selectionManagers.Length; i++) {
                SelectionManager manager = selectionManagers[i];
                GameObject selectedObject = manager?.current?.gameObject;
                SavedObject savedObject = ResolveSavedObjectFromSelection(selectedObject);
                if (savedObject == null) {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(savedObject.customDefinitionId)) {
                    uniqueCandidates.Add(savedObject);
                }
            }

            if (uniqueCandidates.Count == 0) {
                return false;
            }

            if (uniqueCandidates.Count == 1) {
                selectedSavedObject = uniqueCandidates.First();
                return true;
            }

            Camera cam = PivotCamera.Main != null ? PivotCamera.Main.camera : null;
            if (cam == null) {
                selectedSavedObject = uniqueCandidates.First();
                return true;
            }

            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float bestScore = float.MaxValue;
            SavedObject best = null;

            foreach (SavedObject candidate in uniqueCandidates) {
                if (candidate == null) {
                    continue;
                }

                Vector3 worldPoint = GetSavedObjectReferencePoint(candidate);
                Vector3 screenPoint = cam.WorldToScreenPoint(worldPoint);
                bool inFront = screenPoint.z > 0f;
                Vector2 screenPoint2D = new Vector2(screenPoint.x, screenPoint.y);
                float distanceToCenter = Vector2.Distance(screenPoint2D, screenCenter);
                float score = inFront ? distanceToCenter : distanceToCenter + 100000f;

                if (score < bestScore) {
                    bestScore = score;
                    best = candidate;
                }
            }

            selectedSavedObject = best ?? uniqueCandidates.First();
            return selectedSavedObject != null;
        }

        private static SavedObject ResolveSavedObjectFromSelection(GameObject selectedObject) {
            if (selectedObject == null) {
                return null;
            }

            SavedObject savedObject = selectedObject.GetComponent<SavedObject>();
            if (savedObject != null) {
                return savedObject;
            }

            savedObject = selectedObject.GetComponentInParent<SavedObject>();
            if (savedObject != null) {
                return savedObject;
            }

            savedObject = selectedObject.GetComponentInChildren<SavedObject>();
            return savedObject;
        }

        private static Vector3 GetSavedObjectReferencePoint(SavedObject savedObject) {
            if (savedObject == null) {
                return Vector3.zero;
            }

            Renderer renderer = savedObject.GetComponent<Renderer>();
            if (renderer == null) {
                renderer = savedObject.GetComponentInChildren<Renderer>();
            }

            return renderer != null ? renderer.bounds.center : savedObject.transform.position;
        }

        private void BeginCameraStudioSession() {
            PivotCamera pivotCamera = PivotCamera.Main;
            if (pivotCamera == null) {
                return;
            }

            if (cameraSession.valid) {
                EndCameraStudioSession();
            }

            ProjectionSwitcher projectionSwitcher = pivotCamera.GetComponent<ProjectionSwitcher>();
            cameraSession = new CameraSessionState {
                valid = true,
                focusPosition = pivotCamera.focusPosition,
                lookAngle = pivotCamera.lookAngle,
                focusDistance = pivotCamera.focusDistance,
                isOrtho = projectionSwitcher != null && projectionSwitcher.isOrtho
            };

            // Standalone mode should not force camera orientation/projection.
            restoreCameraTransformOnClose = false;

            ConfigureCameraInputForStudio();
        }

        private void EndCameraStudioSession() {
            RestoreCameraInputFromSession();

            if (!cameraSession.valid) {
                return;
            }

            PivotCamera pivotCamera = PivotCamera.Main;
            if (pivotCamera == null) {
                cameraSession = default;
                restoreCameraTransformOnClose = false;
                return;
            }

            if (restoreCameraTransformOnClose) {
                pivotCamera.SetTransform(cameraSession.focusPosition, cameraSession.lookAngle, cameraSession.focusDistance);
                ProjectionSwitcher projectionSwitcher = pivotCamera.GetComponent<ProjectionSwitcher>();
                if (projectionSwitcher != null) {
                    if (cameraSession.isOrtho) {
                        projectionSwitcher.SwitchToOrtho(0f);
                    }
                    else {
                        projectionSwitcher.SwitchToPers(0f);
                    }
                }
            }

            cameraSession = default;
            restoreCameraTransformOnClose = false;
        }

        private void ConfigureCameraInputForStudio() {
            cameraInputSessionStates.Clear();
            MousePivotCameraInput[] cameraInputs = FindObjectsOfType<MousePivotCameraInput>(true);
            for (int i = 0; i < cameraInputs.Length; i++) {
                MousePivotCameraInput input = cameraInputs[i];
                if (input == null) {
                    continue;
                }

                cameraInputSessionStates.Add(new MouseInputSessionState {
                    input = input,
                    enabled = input.enabled,
                    allowOrbit = input.allowOrbit,
                    allowPan = input.allowPan,
                    allowZoom = input.allowZoom,
                    allowZoomWhenOverUI = input.allowZoomWhenOverUI
                });

                if (!input.enabled) {
                    continue;
                }

                input.allowOrbit = launchMode == StudioLaunchMode.EditSelectedInstance;
                input.allowPan = true;
                input.allowZoom = true;
                input.allowZoomWhenOverUI = true;
            }
        }

        private void RestoreCameraInputFromSession() {
            for (int i = 0; i < cameraInputSessionStates.Count; i++) {
                MouseInputSessionState state = cameraInputSessionStates[i];
                if (state.input == null) {
                    continue;
                }

                state.input.enabled = state.enabled;
                state.input.allowOrbit = state.allowOrbit;
                state.input.allowPan = state.allowPan;
                state.input.allowZoom = state.allowZoom;
                state.input.allowZoomWhenOverUI = state.allowZoomWhenOverUI;
            }

            cameraInputSessionStates.Clear();
        }

        private void OpenStudio(SavedObject selectedAtOpen) {
            if (!IsProjectContextReady()) {
                return;
            }

            if (restoreAfterCloseRoutine != null) {
                StopCoroutine(restoreAfterCloseRoutine);
                restoreAfterCloseRoutine = null;
                EndCameraStudioSession();
                MouseInput.SetForceOverUI(false);
                RestoreAfterClose();
            }

            studioOpen = true;
            IsStudioOpen = true;
            showCloseDialog = false;
            hasUnsavedChanges = false;
            canvasZoom = 1f;
            canvasOrigin = Vector2.zero;
            ResetDragState();
            ResetPanState();
            draggingHoleOffset = Vector2.zero;
            selectedSegmentIndex = -1;

            if (placement != null && placement.placing) {
                placement.StopPlacing();
            }

            ResolveLaunchModeAndDefinition(selectedAtOpen);
            BeginCameraStudioSession();
            DisableWhileOpen();
            MouseInput.SetForceOverUI(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            ResetHistoryWithCurrentDefinition();
            fitOnNextCanvas = true;
            InvalidateGeometry();
        }

        private void RequestCloseStudio() {
            RequestStudioAction(ForceCloseStudio);
        }

        private void RequestStudioAction(Action action) {
            if (hasUnsavedChanges) {
                pendingStudioAction = action;
                showCloseDialog = true;
                ResetDragState();
            } else action();
        }

        private void ForceCloseStudio() {
            geometryWorker.Cancel();
            RestoreLivePreviewTargetIfNeeded();
            studioOpen = false;
            IsStudioOpen = false;
            showCloseDialog = false;
            ResetDragState();
            ResetPanState();
            draggingHoleOffset = Vector2.zero;
            selectedSegmentIndex = -1;

            if (restoreAfterCloseRoutine != null) {
                StopCoroutine(restoreAfterCloseRoutine);
            }

            restoreAfterCloseRoutine = StartCoroutine(RestoreAfterCloseDelayed());
        }

        private void DrawCloseDialog() {
            DrawFilledRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.65f));
            Rect rect = new Rect(Screen.width / 2f - 240, Screen.height / 2f - 100, 480, 200);
            GUILayout.BeginArea(rect, panelStyle);
            GUILayout.Label("Keep your changes?", headerStyle);
            GUILayout.Space(12);
            GUILayout.Label("Save this design to your library before continuing, or discard these changes.", wrappedMutedLabelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save & continue", primaryButtonStyle, GUILayout.Height(34))) {
                SaveWorkingDefinition();
                if (hasUnsavedChanges) { GUILayout.EndHorizontal(); GUILayout.EndArea(); return; }
                showCloseDialog = false;
                var next = pendingStudioAction; pendingStudioAction = null; next?.Invoke();
            }
            if (GUILayout.Button("Discard", buttonStyle, GUILayout.Height(34))) {
                hasUnsavedChanges = false;
                showCloseDialog = false;
                var next = pendingStudioAction; pendingStudioAction = null; next?.Invoke();
            }
            if (GUILayout.Button("Keep editing", buttonStyle, GUILayout.Height(34))) { showCloseDialog = false; pendingStudioAction = null; }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DisableWhileOpen() {
            disabledBehaviours.Clear();
            DisableBehaviourIfEnabled<GraphicRaycaster>();
            DisableBehaviourIfEnabled<BaseInputModule>();
        }

        private void RestoreAfterClose() {
            for (int i = 0; i < disabledBehaviours.Count; i++) {
                Behaviour behaviour = disabledBehaviours[i];
                if (behaviour != null) {
                    behaviour.enabled = true;
                }
            }

            disabledBehaviours.Clear();
        }

        private IEnumerator RestoreAfterCloseDelayed() {
            // Avoid click-through on the same frame as "Finish & Insert" or "Close".
            yield return new WaitForEndOfFrame();
            yield return null;
            EndCameraStudioSession();
            MouseInput.SetForceOverUI(false);
            RestoreAfterClose();
            restoreAfterCloseRoutine = null;
        }

        private void DisableBehaviourIfEnabled<T>() where T : Behaviour {
            T[] components = FindObjectsOfType<T>(true);
            for (int i = 0; i < components.Length; i++) {
                T component = components[i];
                if (component != null && component.enabled) {
                    component.enabled = false;
                    disabledBehaviours.Add(component);
                }
            }
        }

        private void EnsureWorkingDefinition() {
            if (workingDefinition != null) {
                EnsureDefinitionIntegrity(workingDefinition);
                return;
            }

            if (CustomPartRegistry.Count > 0) {
                LoadDefinition(CustomPartRegistry.GetAllDefinitions()[0]);
            }
            else {
                CreateNewWorkingDefinition();
            }
        }

        private void CreateNewWorkingDefinition() {
            RestoreLivePreviewTargetIfNeeded();
            fitOnNextCanvas = true;
            InvalidateGeometry();
            launchMode = StudioLaunchMode.Definition;
            editTargetObject = null;
            editSurfaceTransform = null;
            editSurfaceMeshFilter = null;
            editSourceDefinitionId = string.Empty;
            editSourceInstanceId = string.Empty;
            referenceDefinitionForEdit = null;
            livePreviewDirty = false;
            livePreviewApplied = false;
            livePreviewCommitted = false;
            livePreviewNextApplyTime = 0f;
            livePreviewOriginalDefinitionId = string.Empty;
            livePreviewOriginalInstanceId = string.Empty;
            workingDefinition = CustomPartDefinition.CreateDefault();
            workingDefinition.definitionId = Guid.NewGuid().ToString("N");
            workingDefinition.name = BuildDefaultName("Custom Part");
            workingDefinition.thicknessInches = CustomPartDefinition.DefaultThicknessInches;
            workingDefinition.Touch();
            sourceDefinitionId = string.Empty;
            activeLoopIndex = -1;
            selectedAnchorIndex = -1;
            selectedHoleIndex = -1;
            ClearSegmentSelection();
            fieldCache.Clear();
            hasUnsavedChanges = true;
            ResetHistoryWithCurrentDefinition();
        }

        private void DuplicateWorkingDefinition() {
            EnsureWorkingDefinition();
            RestoreLivePreviewTargetIfNeeded();
            fitOnNextCanvas = true;
            InvalidateGeometry();
            launchMode = StudioLaunchMode.Definition;
            editTargetObject = null;
            editSurfaceTransform = null;
            editSurfaceMeshFilter = null;
            editSourceDefinitionId = string.Empty;
            editSourceInstanceId = string.Empty;
            referenceDefinitionForEdit = null;
            livePreviewDirty = false;
            livePreviewApplied = false;
            livePreviewCommitted = false;
            livePreviewNextApplyTime = 0f;
            livePreviewOriginalDefinitionId = string.Empty;
            livePreviewOriginalInstanceId = string.Empty;
            workingDefinition = workingDefinition.CloneDeep();
            workingDefinition.definitionId = Guid.NewGuid().ToString("N");
            workingDefinition.name = BuildDefaultName($"{workingDefinition.name} Copy");
            sourceDefinitionId = string.Empty;
            activeLoopIndex = -1;
            selectedAnchorIndex = -1;
            selectedHoleIndex = -1;
            ClearSegmentSelection();
            hasUnsavedChanges = true;
            fieldCache.Clear();
            ResetHistoryWithCurrentDefinition();
        }

        private void LoadDefinition(CustomPartDefinition source) {
            if (source == null) {
                return;
            }

            RestoreLivePreviewTargetIfNeeded();
            fitOnNextCanvas = true;
            InvalidateGeometry();
            launchMode = StudioLaunchMode.Definition;
            editTargetObject = null;
            editSurfaceTransform = null;
            editSurfaceMeshFilter = null;
            editSourceDefinitionId = string.Empty;
            editSourceInstanceId = string.Empty;
            referenceDefinitionForEdit = null;
            livePreviewDirty = false;
            livePreviewApplied = false;
            livePreviewCommitted = false;
            livePreviewNextApplyTime = 0f;
            livePreviewOriginalDefinitionId = string.Empty;
            livePreviewOriginalInstanceId = string.Empty;

            workingDefinition = source.CloneDeep();
            sourceDefinitionId = source.definitionId;
            hasUnsavedChanges = false;
            activeLoopIndex = -1;
            selectedAnchorIndex = -1;
            selectedHoleIndex = -1;
            ClearSegmentSelection();
            fieldCache.Clear();
            EnsureDefinitionIntegrity(workingDefinition);
            ResetHistoryWithCurrentDefinition();
        }

        private void SaveWorkingDefinition() {
            EnsureWorkingDefinition();
            EnsureDefinitionIntegrity(workingDefinition);
            CustomPartDefinition definitionToSave = workingDefinition.CloneDeep();

            // In selected-instance mode, saving should not overwrite the shared source definition.
            if (launchMode == StudioLaunchMode.EditSelectedInstance
                && !string.IsNullOrWhiteSpace(editSourceDefinitionId)
                && string.Equals(definitionToSave.definitionId, editSourceDefinitionId, StringComparison.Ordinal)) {
                definitionToSave.definitionId = Guid.NewGuid().ToString("N");
            }

            definitionToSave.Touch();

            if (!CustomPartRegistry.SaveToLibrary(definitionToSave)) return;

            workingDefinition = definitionToSave.CloneDeep();
            sourceDefinitionId = workingDefinition.definitionId;
            hasUnsavedChanges = false;
        }

        private void FinishAndInsert() {
            if (workingDefinition == null) {
                return;
            }

            if (!RefreshGeometryValidation(true)) {
                return;
            }

            if (launchMode == StudioLaunchMode.EditSelectedInstance) {
                FinishEditSelectedInstance();
                return;
            }

            if (placement == null) {
                return;
            }

            SaveWorkingDefinition();
            if (hasUnsavedChanges) return;
            placement.StartPlacing(new CustomPartPlacementData(workingDefinition.definitionId, placement.transform));
            ForceCloseStudio();
        }

        private void FinishEditSelectedInstance() {
            if (editTargetObject == null) {
                return;
            }

            ApplyLivePreviewIfNeeded(force: true);

            CustomPartDefinition definitionToApply = workingDefinition.CloneDeep();
            EnsureDefinitionIntegrity(definitionToApply);
            if (!string.IsNullOrWhiteSpace(editSourceDefinitionId)
                && string.Equals(definitionToApply.definitionId, editSourceDefinitionId, StringComparison.Ordinal)) {
                definitionToApply.definitionId = Guid.NewGuid().ToString("N");
            }
            definitionToApply.Touch();
            if (!CustomPartRegistry.SaveToLibrary(definitionToApply)) return;

            if (!CustomPartRuntimeUpdater.ApplyDefinitionToObject(editTargetObject, definitionToApply.definitionId, editSourceInstanceId)) {
                return;
            }

            workingDefinition = definitionToApply.CloneDeep();
            sourceDefinitionId = workingDefinition.definitionId;
            hasUnsavedChanges = false;
            livePreviewCommitted = true;
            livePreviewDirty = false;
            livePreviewApplied = false;

            PartListOutput partListOutput = FindObjectOfType<PartListOutput>();
            if (partListOutput != null) {
                partListOutput.CalculatePartsList();
            }

            ForceCloseStudio();
        }

        private void ImportDefinition() {
            var filters = new[] {
                new ExtensionFilter("Custom Part Files", "json", "svg", "dxf"),
                new ExtensionFilter("JSON", "json"),
                new ExtensionFilter("SVG", "svg"),
                new ExtensionFilter("DXF", "dxf")
            };

            string[] paths = StandaloneFileBrowser.OpenFilePanel("Import Custom Part", "", filters, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrWhiteSpace(paths[0])) {
                return;
            }

            string path = paths[0];
            string extension = Path.GetExtension(path).ToLowerInvariant();
            CustomPartDefinition imported = null;
            bool importedOk = false;
            switch (extension) {
            case ".json":
                importedOk = CustomPartImportExport.ImportJson(path, out imported);
                break;
            case ".svg":
                importedOk = CustomPartImportExport.ImportSvg(path, out imported);
                break;
            case ".dxf":
                importedOk = CustomPartImportExport.ImportDxf(path, out imported);
                break;
            }

            if (!importedOk || imported == null) {
                return;
            }

            imported.definitionId = Guid.NewGuid().ToString("N");
            imported.name = BuildDefaultName(imported.name);
            imported.thicknessInches = imported.thicknessInches <= 0f
                ? CustomPartDefinition.DefaultThicknessInches
                : imported.thicknessInches;
            imported.Touch();

            RestoreLivePreviewTargetIfNeeded();
            fitOnNextCanvas = true;
            InvalidateGeometry();
            launchMode = StudioLaunchMode.Definition;
            editTargetObject = null;
            editSurfaceTransform = null;
            editSurfaceMeshFilter = null;
            editSourceDefinitionId = string.Empty;
            editSourceInstanceId = string.Empty;
            referenceDefinitionForEdit = null;
            livePreviewDirty = false;
            livePreviewApplied = false;
            livePreviewCommitted = false;
            livePreviewNextApplyTime = 0f;
            livePreviewOriginalDefinitionId = string.Empty;
            livePreviewOriginalInstanceId = string.Empty;
            workingDefinition = imported;
            sourceDefinitionId = string.Empty;
            activeLoopIndex = -1;
            selectedAnchorIndex = -1;
            selectedHoleIndex = -1;
            ClearSegmentSelection();
            hasUnsavedChanges = true;
            fieldCache.Clear();
            ResetHistoryWithCurrentDefinition();
        }

        private void ExportDefinition() {
            EnsureWorkingDefinition();
            string defaultName = SanitizeFileName(workingDefinition.name);
            var extensionList = new[] {
                new ExtensionFilter("JSON", "json"),
                new ExtensionFilter("SVG", "svg"),
                new ExtensionFilter("DXF", "dxf")
            };

            string path = StandaloneFileBrowser.SaveFilePanel("Export Custom Part", "", defaultName, extensionList);
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            switch (extension) {
            case ".json":
                CustomPartImportExport.ExportJson(workingDefinition, path);
                break;
            case ".svg":
                CustomPartImportExport.ExportSvg(workingDefinition, path);
                break;
            case ".dxf":
                CustomPartImportExport.ExportDxf(workingDefinition, path);
                break;
            }
        }

        private void DrawCanvas() {
            EnsureWorkingDefinition();

            float width = Screen.width - LeftPanelWidth - RightPanelWidth - (CanvasPadding * 2f);
            float height = Screen.height - TopBarHeight - BottomMargin - CanvasPadding - 44;
            if (width <= 20f || height <= 20f) {
                return;
            }

            canvasRect = new Rect(LeftPanelWidth + CanvasPadding, TopBarHeight + CanvasPadding + 44, width, height);
            DrawCanvasToolbar();
            if (fitOnNextCanvas) { FitCanvas(); fitOnNextCanvas = false; }
            drawingCanvas = true;
            float canvasAlpha = CurrentCanvasAlpha;
            DrawFilledRect(canvasRect, new Color(CanvasColor.r, CanvasColor.g, CanvasColor.b, canvasAlpha));
            DrawRectOutline(canvasRect, UiBorder, 1f);
            DrawCanvasGrid();
            DrawReferenceOverlay();
            DrawLoops();
            DrawBezierToolOverlay();
            DrawHolesOnCanvas();
            if (showDimensionOverlay) {
                DrawDimensionOverlay();
            }
            DrawAddPointPreview();
            DrawCanvasCursorLegend();
            drawingCanvas = false;
            if (!showCloseDialog) HandleCanvasInput();
        }

        private void DrawCanvasGrid() {
            float pixelsPerUnit = GetPixelsPerUnit();
            if (pixelsPerUnit <= 1f) {
                return;
            }

            Vector2 minPart = ScreenToPart(new Vector2(canvasRect.xMin, canvasRect.yMax));
            Vector2 maxPart = ScreenToPart(new Vector2(canvasRect.xMax, canvasRect.yMin));

            const float step = 0.25f;
            int startX = Mathf.FloorToInt(minPart.x / step) - 1;
            int endX = Mathf.CeilToInt(maxPart.x / step) + 1;
            for (int i = startX; i <= endX; i++) {
                float x = i * step;
                bool major = Mathf.Abs(x - Mathf.Round(x)) < 0.001f;
                Vector2 a = PartToScreen(new Vector2(x, minPart.y));
                Vector2 b = PartToScreen(new Vector2(x, maxPart.y));
                DrawLine(a, b, major ? GridMajorColor : GridMinorColor, Mathf.Abs(x) < 0.001f ? 2f : 1f);
            }

            int startY = Mathf.FloorToInt(minPart.y / step) - 1;
            int endY = Mathf.CeilToInt(maxPart.y / step) + 1;
            for (int i = startY; i <= endY; i++) {
                float y = i * step;
                bool major = Mathf.Abs(y - Mathf.Round(y)) < 0.001f;
                Vector2 a = PartToScreen(new Vector2(minPart.x, y));
                Vector2 b = PartToScreen(new Vector2(maxPart.x, y));
                DrawLine(a, b, major ? GridMajorColor : GridMinorColor, Mathf.Abs(y) < 0.001f ? 2f : 1f);
            }
        }

        private void DrawLoops() {
            DrawLoop(workingDefinition.sketch.outerLoop, OuterLoopColor, activeLoopIndex == -1);

            LoopData[] cutouts = workingDefinition.sketch.cutoutLoops ?? Array.Empty<LoopData>();
            for (int i = 0; i < cutouts.Length; i++) {
                bool isActive = activeLoopIndex == i;
                DrawLoop(cutouts[i], CutoutLoopColor, isActive);
            }
        }

        private void DrawReferenceOverlay() {
            if (launchMode != StudioLaunchMode.EditSelectedInstance || referenceDefinitionForEdit == null) {
                return;
            }

            SketchData referenceSketch = referenceDefinitionForEdit.sketch;
            if (referenceSketch == null) {
                return;
            }

            DrawLoopGeometry(referenceSketch.outerLoop, ReferenceOutlineColor, 1.5f);
            LoopData[] referenceCutouts = referenceSketch.cutoutLoops ?? Array.Empty<LoopData>();
            for (int i = 0; i < referenceCutouts.Length; i++) {
                DrawLoopGeometry(referenceCutouts[i], ReferenceOutlineColor, 1.5f);
            }

            CustomHoleDefinition[] referenceHoles = referenceDefinitionForEdit.holes ?? Array.Empty<CustomHoleDefinition>();
            for (int i = 0; i < referenceHoles.Length; i++) {
                DrawHoleShape(referenceHoles[i], ReferenceHoleColor, 1.5f);
            }
        }

        private void DrawLoop(LoopData loop, Color baseColor, bool isActiveLoop) {
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2) {
                return;
            }

            Color lineColor = isActiveLoop ? Color.Lerp(baseColor, Color.white, 0.2f) : baseColor;
            DrawLoopGeometry(loop, lineColor, 2f);
            AnchorData[] anchors = loop.anchors;

            if (!isActiveLoop) {
                return;
            }

            for (int i = 0; i < anchors.Length; i++) {
                AnchorData anchor = anchors[i];
                Vector2 anchorScreen = PartToScreen(anchor.position);
                bool isSelectedAnchor = i == selectedAnchorIndex;
                DrawPoint(anchorScreen, isSelectedAnchor ? SelectedColor : lineColor, isSelectedAnchor ? 11f : 9f);
            }

            if (activeTool == CanvasTool.Select && selectedAnchorIndex >= 0 && selectedAnchorIndex < anchors.Length) {
                DrawSelectedAnchorHandles(anchors[selectedAnchorIndex]);
            }
        }

        private void DrawLoopGeometry(LoopData loop, Color lineColor, float lineWidth) {
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2) {
                return;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            AnchorData[] anchors = loop.anchors;
            for (int i = 0; i < anchors.Length; i++) {
                int next = (i + 1) % anchors.Length;
                AnchorData start = anchors[i];
                AnchorData end = anchors[next];
                if (kinds[i] == SegmentKind.Bezier) {
                    Vector2 p0 = PartToScreen(start.position);
                    Vector2 p1 = PartToScreen(start.position + start.outHandle);
                    Vector2 p2 = PartToScreen(end.position + end.inHandle);
                    Vector2 p3 = PartToScreen(end.position);
                    DrawBezierPolyline(p0, p1, p2, p3, lineColor, lineWidth);
                }
                else {
                    DrawLine(PartToScreen(start.position), PartToScreen(end.position), lineColor, lineWidth);
                }
            }
        }

        private void DrawSelectedAnchorHandles(AnchorData anchor) {
            Vector2 anchorScreen = PartToScreen(anchor.position);
            Vector2 inHandle = PartToScreen(anchor.position + anchor.inHandle);
            Vector2 outHandle = PartToScreen(anchor.position + anchor.outHandle);
            if (anchor.inHandle.sqrMagnitude > 0.00001f) {
                DrawLine(anchorScreen, inHandle, new Color(0.66f, 0.71f, 1f, 1f), 1f);
                DrawPoint(inHandle, new Color(0.5f, 0.65f, 1f, 1f), 10f);
            }

            if (anchor.outHandle.sqrMagnitude > 0.00001f) {
                DrawLine(anchorScreen, outHandle, new Color(0.66f, 0.71f, 1f, 1f), 1f);
                DrawPoint(outHandle, new Color(0.5f, 0.65f, 1f, 1f), 10f);
            }
        }

        private void DrawHolesOnCanvas() {
            CustomHoleDefinition[] holes = workingDefinition.holes ?? Array.Empty<CustomHoleDefinition>();
            for (int i = 0; i < holes.Length; i++) {
                CustomHoleDefinition hole = holes[i];
                Color color = i == selectedHoleIndex ? SelectedColor : HoleColor;
                DrawHoleShape(hole, color, 2f);
            }

            if (selectedHoleIndex >= 0 && selectedHoleIndex < holes.Length) {
                DrawSelectedHoleAlignmentGuides(holes[selectedHoleIndex]);
            }
        }

        private void DrawHoleShape(CustomHoleDefinition hole, Color color, float width) {
            if (hole == null) {
                return;
            }

            Vector2 center = PartToScreen(hole.position);
            float rx = Mathf.Max(2f, hole.size.x * 0.5f * GetPixelsPerUnit());
            float ry = Mathf.Max(2f, hole.size.y * 0.5f * GetPixelsPerUnit());
            CustomHoleShape normalizedShape = NormalizeHoleShape(hole.shape);

            switch (normalizedShape) {
            case CustomHoleShape.Square:
                DrawRectOutline(new Rect(center.x - rx, center.y - ry, rx * 2f, ry * 2f), color, width);
                break;
            default:
                DrawEllipse(center, rx, ry, color, width, 20);
                break;
            }
        }

        private void DrawSelectedHoleAlignmentGuides(CustomHoleDefinition hole) {
            if (hole == null) {
                return;
            }

            Vector2 center = PartToScreen(hole.position);
            Color guideColor = new Color(0.84f, 0.9f, 1f, 0.45f);
            DrawLine(new Vector2(canvasRect.xMin, center.y), new Vector2(canvasRect.xMax, center.y), guideColor, 1f);
            DrawLine(new Vector2(center.x, canvasRect.yMin), new Vector2(center.x, canvasRect.yMax), guideColor, 1f);
            DrawPoint(center, SelectedColor, 9f);

            DrawDimensionLabel(new Vector2(center.x + 52f, center.y - 18f), $"Hole: {hole.position.x:0.###}, {hole.position.y:0.###}");
        }

        private void DrawDimensionOverlay() {
            if (!TryGetOuterBounds(out Vector2 min, out Vector2 max)) {
                return;
            }

            float widthIn = Mathf.Max(0f, max.x - min.x);
            float lengthIn = Mathf.Max(0f, max.y - min.y);
            if (widthIn <= 0.0001f || lengthIn <= 0.0001f) {
                return;
            }

            Vector2 topLeft = PartToScreen(new Vector2(min.x, max.y));
            Vector2 topRight = PartToScreen(new Vector2(max.x, max.y));
            Vector2 bottomRight = PartToScreen(new Vector2(max.x, min.y));

            const float offsetPx = 30f;
            float horizontalY = Mathf.Clamp(topLeft.y - offsetPx, canvasRect.yMin + 10f, canvasRect.yMax - 10f);
            float verticalX = Mathf.Clamp(topRight.x + offsetPx, canvasRect.xMin + 10f, canvasRect.xMax - 10f);

            Vector2 hStart = new Vector2(topLeft.x, horizontalY);
            Vector2 hEnd = new Vector2(topRight.x, horizontalY);
            Vector2 vStart = new Vector2(verticalX, topRight.y);
            Vector2 vEnd = new Vector2(verticalX, bottomRight.y);

            DrawLine(topLeft, hStart, DimensionGuideColor, 1f);
            DrawLine(topRight, hEnd, DimensionGuideColor, 1f);
            DrawLine(hStart, hEnd, DimensionLineColor, 2f);
            DrawDimensionTick(hStart, vertical: true);
            DrawDimensionTick(hEnd, vertical: true);

            DrawLine(topRight, vStart, DimensionGuideColor, 1f);
            DrawLine(bottomRight, vEnd, DimensionGuideColor, 1f);
            DrawLine(vStart, vEnd, DimensionLineColor, 2f);
            DrawDimensionTick(vStart, vertical: false);
            DrawDimensionTick(vEnd, vertical: false);

            DrawDimensionLabel(
                new Vector2((hStart.x + hEnd.x) * 0.5f, horizontalY - 14f),
                $"W {widthIn:0.###} in");
            DrawDimensionLabel(
                new Vector2(verticalX + 42f, (vStart.y + vEnd.y) * 0.5f),
                $"L {lengthIn:0.###} in");
        }

        private bool TryGetOuterBounds(out Vector2 min, out Vector2 max) {
            min = Vector2.zero;
            max = Vector2.zero;

            LoopData loop = workingDefinition?.sketch?.outerLoop;
            if (loop?.anchors == null || loop.anchors.Length < 2) {
                return false;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            AnchorData[] anchors = loop.anchors;
            bool hasPoint = false;

            for (int i = 0; i < anchors.Length; i++) {
                int next = (i + 1) % anchors.Length;
                AnchorData start = anchors[i];
                AnchorData end = anchors[next];
                if (kinds[i] == SegmentKind.Bezier) {
                    Vector2 p0 = start.position;
                    Vector2 p1 = start.position + start.outHandle;
                    Vector2 p2 = end.position + end.inHandle;
                    Vector2 p3 = end.position;
                    for (int s = 0; s <= 24; s++) {
                        float t = s / 24f;
                        Vector2 point = EvaluateBezier(p0, p1, p2, p3, t);
                        ExpandBounds(ref hasPoint, ref min, ref max, point);
                    }
                }
                else {
                    ExpandBounds(ref hasPoint, ref min, ref max, start.position);
                    ExpandBounds(ref hasPoint, ref min, ref max, end.position);
                }
            }

            return hasPoint;
        }

        private static void ExpandBounds(ref bool hasPoint, ref Vector2 min, ref Vector2 max, Vector2 point) {
            if (!hasPoint) {
                min = point;
                max = point;
                hasPoint = true;
                return;
            }

            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        private void DrawDimensionTick(Vector2 center, bool vertical) {
            const float halfTick = 5f;
            if (vertical) {
                DrawLine(
                    new Vector2(center.x, center.y - halfTick),
                    new Vector2(center.x, center.y + halfTick),
                    DimensionLineColor,
                    2f);
            }
            else {
                DrawLine(
                    new Vector2(center.x - halfTick, center.y),
                    new Vector2(center.x + halfTick, center.y),
                    DimensionLineColor,
                    2f);
            }
        }

        private void DrawDimensionLabel(Vector2 center, string text) {
            if (string.IsNullOrWhiteSpace(text) || dimensionLabelStyle == null) {
                return;
            }

            Vector2 textSize = dimensionLabelStyle.CalcSize(new GUIContent(text));
            Rect rect = new Rect(
                center.x - (textSize.x * 0.5f) - 6f,
                center.y - (textSize.y * 0.5f) - 2f,
                textSize.x + 12f,
                textSize.y + 4f);

            rect.x = Mathf.Clamp(rect.x, canvasRect.xMin + 2f, canvasRect.xMax - rect.width - 2f);
            rect.y = Mathf.Clamp(rect.y, canvasRect.yMin + 2f, canvasRect.yMax - rect.height - 2f);

            DrawFilledRect(rect, DimensionLabelBackground);
            DrawRectOutline(rect, DimensionGuideColor, 1f);
            GUI.Label(rect, text, dimensionLabelStyle);
        }

        private void DrawAddPointPreview() {
            if (activeTool != CanvasTool.AddPoint || Event.current == null) {
                return;
            }

            Vector2 mouse = Event.current.mousePosition;
            if (!canvasRect.Contains(mouse)) {
                return;
            }

            LoopData loop = GetActiveLoop();
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2) {
                return;
            }

            Vector2 clickPoint = SnapPartPoint(ScreenToPart(mouse), allowTemporaryOverride: true);
            if (!TryGetNearestSegment(loop, clickPoint, out int segmentIndex, out _)) {
                return;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            int next = (segmentIndex + 1) % loop.anchors.Length;
            AnchorData start = loop.anchors[segmentIndex];
            AnchorData end = loop.anchors[next];
            Color previewColor = new Color(1f, 0.86f, 0.35f, 1f);

            if (kinds[segmentIndex] == SegmentKind.Bezier) {
                Vector2 p0 = PartToScreen(start.position);
                Vector2 p1 = PartToScreen(start.position + start.outHandle);
                Vector2 p2 = PartToScreen(end.position + end.inHandle);
                Vector2 p3 = PartToScreen(end.position);
                DrawBezierPolyline(p0, p1, p2, p3, previewColor, 3f);
            }
            else {
                DrawLine(PartToScreen(start.position), PartToScreen(end.position), previewColor, 3f);
            }

            DrawPoint(PartToScreen(clickPoint), previewColor, 10f);
        }

        private void DrawBezierToolOverlay() {
            if (activeTool != CanvasTool.ToggleBezier) {
                return;
            }

            LoopData loop = GetActiveLoop();
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2) {
                return;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            int highlightedSegment = selectedSegmentIndex;
            if (draggingBezierBend && draggingBezierLoopIndex == activeLoopIndex) {
                highlightedSegment = draggingBezierSegmentIndex;
            }

            if (TryGetHoveredSegmentAtMouse(loop, out int hoveredSegment, out _)) {
                Color hoverColor = new Color(1f, 0.86f, 0.35f, 0.78f);
                DrawLoopSegmentGeometry(loop, hoveredSegment, hoverColor, 3f);
                Vector2 hoverHandleScreen = PartToScreen(GetSegmentBendPoint(loop, hoveredSegment));
                DrawPoint(hoverHandleScreen, hoverColor, 9f);
                if (highlightedSegment < 0) {
                    highlightedSegment = hoveredSegment;
                }
            }

            if (highlightedSegment < 0 || highlightedSegment >= loop.anchors.Length) {
                return;
            }

            Color selectedColor = new Color(1f, 0.92f, 0.5f, 1f);
            DrawLoopSegmentGeometry(loop, highlightedSegment, selectedColor, 3.25f);
            DrawSegmentControlOverlay(loop, highlightedSegment, kinds[highlightedSegment], selectedColor);
        }

        private bool TryGetHoveredSegmentAtMouse(LoopData loop, out int segmentIndex, out Vector2 nearestPoint) {
            segmentIndex = -1;
            nearestPoint = Vector2.zero;
            Event evt = Event.current;
            if (evt == null || !canvasRect.Contains(evt.mousePosition)) {
                return false;
            }

            Vector2 rawPartPoint = ScreenToPart(evt.mousePosition);
            if (!TryGetNearestSegment(loop, rawPartPoint, out int candidateSegment, out Vector2 candidateNearestPoint)) {
                return false;
            }

            float segmentDistancePx = Vector2.Distance(evt.mousePosition, PartToScreen(candidateNearestPoint));
            if (segmentDistancePx > BezierSegmentHitRadiusPx) {
                return false;
            }

            segmentIndex = candidateSegment;
            nearestPoint = candidateNearestPoint;
            return true;
        }

        private void DrawLoopSegmentGeometry(LoopData loop, int segmentIndex, Color color, float width) {
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2 || segmentIndex < 0 || segmentIndex >= loop.anchors.Length) {
                return;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            int next = (segmentIndex + 1) % loop.anchors.Length;
            AnchorData start = loop.anchors[segmentIndex];
            AnchorData end = loop.anchors[next];
            if (kinds[segmentIndex] == SegmentKind.Bezier) {
                Vector2 p0 = PartToScreen(start.position);
                Vector2 p1 = PartToScreen(start.position + start.outHandle);
                Vector2 p2 = PartToScreen(end.position + end.inHandle);
                Vector2 p3 = PartToScreen(end.position);
                DrawBezierPolyline(p0, p1, p2, p3, color, width);
            }
            else {
                DrawLine(PartToScreen(start.position), PartToScreen(end.position), color, width);
            }
        }

        private void DrawSegmentControlOverlay(LoopData loop, int segmentIndex, SegmentKind segmentKind, Color color) {
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2 || segmentIndex < 0 || segmentIndex >= loop.anchors.Length) {
                return;
            }

            int next = (segmentIndex + 1) % loop.anchors.Length;
            AnchorData start = loop.anchors[segmentIndex];
            AnchorData end = loop.anchors[next];
            Vector2 bendPart = GetSegmentBendPoint(loop, segmentIndex);
            Vector2 bendScreen = PartToScreen(bendPart);
            Vector2 chordMidScreen = PartToScreen((start.position + end.position) * 0.5f);
            DrawLine(chordMidScreen, bendScreen, new Color(color.r, color.g, color.b, 0.85f), 1f);
            DrawPoint(bendScreen, color, 11f);

            if (segmentKind != SegmentKind.Bezier) {
                return;
            }

            Color handleLineColor = new Color(0.66f, 0.71f, 1f, 0.95f);
            Color handlePointColor = new Color(0.5f, 0.65f, 1f, 1f);
            Vector2 startScreen = PartToScreen(start.position);
            Vector2 endScreen = PartToScreen(end.position);
            Vector2 outHandleScreen = PartToScreen(start.position + start.outHandle);
            Vector2 inHandleScreen = PartToScreen(end.position + end.inHandle);
            DrawLine(startScreen, outHandleScreen, handleLineColor, 1f);
            DrawLine(endScreen, inHandleScreen, handleLineColor, 1f);
            DrawPoint(outHandleScreen, handlePointColor, 8f);
            DrawPoint(inHandleScreen, handlePointColor, 8f);
        }

        private Vector2 GetSegmentBendPoint(LoopData loop, int segmentIndex) {
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2 || segmentIndex < 0 || segmentIndex >= loop.anchors.Length) {
                return Vector2.zero;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            int next = (segmentIndex + 1) % loop.anchors.Length;
            AnchorData start = loop.anchors[segmentIndex];
            AnchorData end = loop.anchors[next];
            if (kinds[segmentIndex] == SegmentKind.Bezier) {
                Vector2 p0 = start.position;
                Vector2 p1 = start.position + start.outHandle;
                Vector2 p2 = end.position + end.inHandle;
                Vector2 p3 = end.position;
                return EvaluateBezier(p0, p1, p2, p3, 0.5f);
            }

            return (start.position + end.position) * 0.5f;
        }

        private void DrawCanvasCursorLegend() {
            Rect status = new Rect(canvasRect.xMin + 10, canvasRect.yMax - 26, canvasRect.width - 20, 22);
            DrawFilledRect(new Rect(status.x - 4, status.y - 2, status.width + 8, 26), new Color(0.1f, 0.1f, 0.1f, 0.9f));
            Vector2 point = SnapPartPoint(ScreenToPart(Event.current.mousePosition), true);
            GUI.Label(status, $"{point.x:0.###}, {point.y:0.###} in    |    Scroll to zoom   /   Right-drag to pan", mutedLabelStyle);
        }

        private void HandleCanvasInput() {
            if (showCloseDialog) {
                return;
            }

            Event evt = Event.current;
            if (evt == null) {
                return;
            }

            if (HandleDeleteHotkey(evt)) {
                return;
            }

            bool inside = canvasRect.Contains(evt.mousePosition);
            if (!inside) {
                if (evt.type == EventType.MouseUp) {
                    ResetDragState();
                    ResetPanState();
                }
                return;
            }

            if (evt.type == EventType.ScrollWheel) {
                if (UseSceneAlignedCanvas) {
                    evt.Use();
                    return;
                }

                float prevZoom = canvasZoom;
                float delta = -evt.delta.y * 0.07f;
                canvasZoom = Mathf.Clamp(canvasZoom * (1f + delta), MinZoom, MaxZoom);

                if (!Mathf.Approximately(prevZoom, canvasZoom)) {
                    Vector2 before = ScreenToPart(evt.mousePosition, prevZoom);
                    Vector2 after = ScreenToPart(evt.mousePosition, canvasZoom);
                    canvasOrigin += before - after;
                }

                evt.Use();
                return;
            }

            bool panModifierPressed = IsPanModifierPressed();
            bool beginPanFromPrimary = evt.type == EventType.MouseDown && evt.button == 0 && panModifierPressed;
            if (!UseSceneAlignedCanvas && evt.type == EventType.MouseDown && (evt.button == 2 || evt.button == 1 || beginPanFromPrimary)) {
                draggingCanvas = true;
                panningWithPrimaryDrag = evt.button == 0;
                ResetDragState();
                lastMousePosition = evt.mousePosition;
                evt.Use();
                return;
            }

            bool draggingPanButton = evt.button == 2 || evt.button == 1 || (panningWithPrimaryDrag && evt.button == 0);
            if (!UseSceneAlignedCanvas && evt.type == EventType.MouseDrag && draggingCanvas && draggingPanButton) {
                Vector2 deltaPx = evt.mousePosition - lastMousePosition;
                canvasOrigin.x -= deltaPx.x / GetPixelsPerUnit();
                canvasOrigin.y += deltaPx.y / GetPixelsPerUnit();
                lastMousePosition = evt.mousePosition;
                evt.Use();
                return;
            }

            if (!UseSceneAlignedCanvas && evt.type == EventType.MouseUp && draggingCanvas && draggingPanButton) {
                ResetPanState();
                evt.Use();
                return;
            }

            if (evt.button != 0) {
                return;
            }

            if (evt.type == EventType.MouseDown) {
                Vector2 rawPartPoint = ScreenToPart(evt.mousePosition);
                Vector2 snappedPartPoint = SnapPartPoint(rawPartPoint, allowTemporaryOverride: true);
                switch (activeTool) {
                case CanvasTool.AddHole:
                    ClearSegmentSelection();
                    AddHole(snappedPartPoint);
                    evt.Use();
                    return;
                case CanvasTool.AddPoint:
                    ClearSegmentSelection();
                    InsertPointAtNearestSegment(snappedPartPoint);
                    evt.Use();
                    return;
                case CanvasTool.ToggleBezier:
                    HandleBezierToolMouseDown(rawPartPoint, evt.mousePosition);
                    evt.Use();
                    return;
                case CanvasTool.Select:
                default:
                    if (TrySelectHandle(evt.mousePosition, out SelectedHandle selectedHandle)) {
                        draggingHandle = selectedHandle;
                        draggingAnchor = false;
                        draggingHole = false;
                        evt.Use();
                        return;
                    }

                    if (TrySelectAnchorAnyLoop(evt.mousePosition, out int loopIndex, out int anchorIndex)) {
                        activeLoopIndex = loopIndex;
                        selectedAnchorIndex = anchorIndex;
                        selectedHoleIndex = -1;
                        draggingAnchor = true;
                        draggingHole = false;
                        draggingHandle = default;
                        ClearSegmentSelection();
                        evt.Use();
                        return;
                    }

                    if (TrySelectHole(evt.mousePosition, out int holeIndex)) {
                        selectedHoleIndex = holeIndex;
                        selectedAnchorIndex = -1;
                        draggingAnchor = false;
                        draggingHandle = default;
                        draggingHole = true;
                        ClearSegmentSelection();
                        CustomHoleDefinition[] holes = workingDefinition.holes ?? Array.Empty<CustomHoleDefinition>();
                        if (holeIndex >= 0 && holeIndex < holes.Length && holes[holeIndex] != null) {
                            draggingHoleOffset = holes[holeIndex].position - rawPartPoint;
                        }
                        else {
                            draggingHoleOffset = Vector2.zero;
                        }
                        evt.Use();
                        return;
                    }

                    selectedAnchorIndex = -1;
                    selectedHoleIndex = -1;
                    selectedSegmentIndex = -1;
                    ResetDragState();
                    break;
                }
            }

            if (evt.type == EventType.MouseDrag && evt.button == 0) {
                Vector2 rawPartPoint = ScreenToPart(evt.mousePosition);
                if (activeTool == CanvasTool.ToggleBezier && draggingBezierBend) {
                    HandleBezierToolMouseDrag(rawPartPoint, evt.mousePosition);
                    evt.Use();
                    return;
                }

                if (draggingHandle.isValid) {
                    MoveSelectedHandle(rawPartPoint);
                    evt.Use();
                    return;
                }

                if (draggingAnchor) {
                    MoveSelectedAnchor(SnapPartPoint(rawPartPoint, allowTemporaryOverride: true));
                    evt.Use();
                    return;
                }

                if (draggingHole) {
                    MoveSelectedHole(SnapPartPoint(rawPartPoint + draggingHoleOffset, allowTemporaryOverride: true));
                    evt.Use();
                    return;
                }
            }

            if (evt.type == EventType.MouseUp && evt.button == 0) {
                ResetDragState();
                evt.Use();
            }
        }

        private bool HandleDeleteHotkey(Event evt) {
            if (evt.type != EventType.KeyDown) {
                return false;
            }

            if (evt.keyCode != KeyCode.Delete && evt.keyCode != KeyCode.Backspace) {
                return false;
            }

            if (!string.IsNullOrEmpty(GUI.GetNameOfFocusedControl())) {
                return false;
            }

            if (activeTool == CanvasTool.ToggleBezier && selectedSegmentIndex >= 0) {
                LoopData loop = GetActiveLoop();
                if (SetSegmentLine(loop, selectedSegmentIndex)) {
                    MarkDirty();
                }
                evt.Use();
                return true;
            }

            if (selectedAnchorIndex >= 0) {
                DeleteAnchor(selectedAnchorIndex);
                evt.Use();
                return true;
            }

            if (selectedHoleIndex >= 0) {
                DeleteSelectedHole();
                evt.Use();
                return true;
            }

            return false;
        }

        private void MoveSelectedAnchor(Vector2 partPoint) {
            LoopData loop = GetActiveLoop();
            if (loop == null || selectedAnchorIndex < 0 || selectedAnchorIndex >= loop.anchors.Length) {
                return;
            }

            loop.anchors[selectedAnchorIndex].position = SnapPartPoint(partPoint, allowTemporaryOverride: true);
            MarkDirty();
        }

        private void MoveSelectedHandle(Vector2 partPoint) {
            LoopData loop = GetActiveLoop();
            if (loop == null || !draggingHandle.isValid || draggingHandle.anchorIndex < 0 || draggingHandle.anchorIndex >= loop.anchors.Length) {
                return;
            }

            AnchorData anchor = loop.anchors[draggingHandle.anchorIndex];
            Vector2 handleVector = partPoint - anchor.position;
            if (draggingHandle.isOutHandle) {
                anchor.outHandle = handleVector;
                ApplyHandleMode(anchor, true);
            }
            else {
                anchor.inHandle = handleVector;
                ApplyHandleMode(anchor, false);
            }

            MarkDirty();
        }

        private void MoveSelectedHole(Vector2 partPoint) {
            CustomHoleDefinition[] holes = workingDefinition.holes ?? Array.Empty<CustomHoleDefinition>();
            if (selectedHoleIndex < 0 || selectedHoleIndex >= holes.Length || holes[selectedHoleIndex] == null) {
                return;
            }

            holes[selectedHoleIndex].position = partPoint;
            MarkDirty();
        }

        private void ApplyHandleMode(AnchorData anchor, bool movedOutHandle) {
            if (anchor == null) {
                return;
            }

            if (anchor.handleMode == HandleMode.Free) {
                return;
            }

            if (anchor.handleMode == HandleMode.Mirrored) {
                if (movedOutHandle) {
                    anchor.inHandle = -anchor.outHandle;
                }
                else {
                    anchor.outHandle = -anchor.inHandle;
                }
                return;
            }

            if (movedOutHandle) {
                float inMag = anchor.inHandle.magnitude;
                if (inMag > 0.0001f && anchor.outHandle.sqrMagnitude > 0.0001f) {
                    anchor.inHandle = -anchor.outHandle.normalized * inMag;
                }
            }
            else {
                float outMag = anchor.outHandle.magnitude;
                if (outMag > 0.0001f && anchor.inHandle.sqrMagnitude > 0.0001f) {
                    anchor.outHandle = -anchor.inHandle.normalized * outMag;
                }
            }
        }

        private bool TrySelectAnchorAnyLoop(Vector2 screenPoint, out int loopIndex, out int anchorIndex) {
            loopIndex = int.MinValue;
            anchorIndex = -1;

            float bestDist = 12f;

            LoopData outerLoop = workingDefinition?.sketch?.outerLoop;
            if (outerLoop != null && outerLoop.anchors != null) {
                for (int i = 0; i < outerLoop.anchors.Length; i++) {
                    float dist = Vector2.Distance(PartToScreen(outerLoop.anchors[i].position), screenPoint);
                    if (dist < bestDist) {
                        bestDist = dist;
                        loopIndex = -1;
                        anchorIndex = i;
                    }
                }
            }

            LoopData[] cutouts = workingDefinition?.sketch?.cutoutLoops ?? Array.Empty<LoopData>();
            for (int c = 0; c < cutouts.Length; c++) {
                LoopData loop = cutouts[c];
                if (loop?.anchors == null) {
                    continue;
                }

                for (int i = 0; i < loop.anchors.Length; i++) {
                    float dist = Vector2.Distance(PartToScreen(loop.anchors[i].position), screenPoint);
                    if (dist < bestDist) {
                        bestDist = dist;
                        loopIndex = c;
                        anchorIndex = i;
                    }
                }
            }

            return anchorIndex >= 0;
        }

        private bool TrySelectHandle(Vector2 screenPoint, out SelectedHandle selectedHandle) {
            selectedHandle = default;
            if (selectedAnchorIndex < 0) {
                return false;
            }

            LoopData loop = GetActiveLoop();
            if (loop == null || selectedAnchorIndex >= loop.anchors.Length) {
                return false;
            }

            AnchorData anchor = loop.anchors[selectedAnchorIndex];
            Vector2 inHandle = PartToScreen(anchor.position + anchor.inHandle);
            Vector2 outHandle = PartToScreen(anchor.position + anchor.outHandle);

            if (anchor.outHandle.sqrMagnitude > 0.00001f && Vector2.Distance(screenPoint, outHandle) <= HandleHitRadiusPx) {
                selectedHandle = new SelectedHandle {
                    isValid = true,
                    anchorIndex = selectedAnchorIndex,
                    isOutHandle = true
                };
                return true;
            }

            if (anchor.inHandle.sqrMagnitude > 0.00001f && Vector2.Distance(screenPoint, inHandle) <= HandleHitRadiusPx) {
                selectedHandle = new SelectedHandle {
                    isValid = true,
                    anchorIndex = selectedAnchorIndex,
                    isOutHandle = false
                };
                return true;
            }

            return false;
        }

        private bool TrySelectHole(Vector2 screenPoint, out int holeIndex) {
            holeIndex = -1;
            CustomHoleDefinition[] holes = workingDefinition.holes ?? Array.Empty<CustomHoleDefinition>();
            float best = 28f;
            for (int i = 0; i < holes.Length; i++) {
                if (IsPointInHoleScreenBounds(holes[i], screenPoint, 8f)) {
                    holeIndex = i;
                    return true;
                }

                Vector2 center = PartToScreen(holes[i].position);
                float d = Vector2.Distance(center, screenPoint);
                if (d < best) {
                    best = d;
                    holeIndex = i;
                }
            }

            return holeIndex >= 0;
        }

        private bool IsPointInHoleScreenBounds(CustomHoleDefinition hole, Vector2 screenPoint, float paddingPx) {
            if (hole == null) {
                return false;
            }

            Vector2 center = PartToScreen(hole.position);
            float rx = Mathf.Max(2f, (hole.size.x * 0.5f * GetPixelsPerUnit()) + paddingPx);
            float ry = Mathf.Max(2f, (hole.size.y * 0.5f * GetPixelsPerUnit()) + paddingPx);
            Vector2 delta = screenPoint - center;

            if (hole.shape == CustomHoleShape.Square) {
                return Mathf.Abs(delta.x) <= rx && Mathf.Abs(delta.y) <= ry;
            }

            float nx = rx > 0.0001f ? delta.x / rx : 0f;
            float ny = ry > 0.0001f ? delta.y / ry : 0f;
            return (nx * nx) + (ny * ny) <= 1f;
        }

        private void InsertPointAtNearestSegment(Vector2 partPoint) {
            LoopData loop = GetActiveLoop();
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2) {
                return;
            }

            partPoint = SnapPartPoint(partPoint, allowTemporaryOverride: true);

            if (!TryGetNearestSegment(loop, partPoint, out int segmentIndex, out _)) {
                return;
            }

            var anchors = new List<AnchorData>(loop.anchors);
            var segmentKinds = new List<SegmentKind>(EnsureSegmentKinds(loop));
            SegmentKind oldKind = segmentKinds[segmentIndex];

            AnchorData newAnchor = new AnchorData {
                position = partPoint,
                inHandle = Vector2.zero,
                outHandle = Vector2.zero,
                handleMode = HandleMode.Mirrored
            };

            int insertIndex = segmentIndex + 1;
            anchors.Insert(insertIndex, newAnchor);
            segmentKinds.Insert(insertIndex, oldKind);

            NormalizeSegmentKinds(anchors, segmentKinds);
            loop.anchors = anchors.ToArray();
            loop.segmentKinds = segmentKinds.ToArray();
            selectedAnchorIndex = insertIndex;
            ClearSegmentSelection();
            MarkDirty();
        }

        private void DeleteAnchor(int anchorIndex) {
            LoopData loop = GetActiveLoop();
            if (loop == null || loop.anchors == null || loop.anchors.Length <= 3) {
                return;
            }

            var anchors = new List<AnchorData>(loop.anchors);
            var segmentKinds = new List<SegmentKind>(EnsureSegmentKinds(loop));
            anchors.RemoveAt(anchorIndex);
            if (segmentKinds.Count > anchorIndex) {
                segmentKinds.RemoveAt(anchorIndex);
            }

            NormalizeSegmentKinds(anchors, segmentKinds);
            loop.anchors = anchors.ToArray();
            loop.segmentKinds = segmentKinds.ToArray();
            selectedAnchorIndex = Mathf.Clamp(anchorIndex - 1, 0, loop.anchors.Length - 1);
            ClearSegmentSelection();
            MarkDirty();
        }

        private bool HandleBezierToolMouseDown(Vector2 partPoint, Vector2 screenPoint) {
            if (TryStartCurveHandleDrag(screenPoint)) {
                return true;
            }

            LoopData loop = GetActiveLoop();
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2) {
                ClearSegmentSelection();
                return false;
            }

            if (!TryGetNearestSegment(loop, partPoint, out int segmentIndex, out Vector2 nearestPoint)) {
                ClearSegmentSelection();
                return false;
            }

            float segmentDistancePx = Vector2.Distance(screenPoint, PartToScreen(nearestPoint));
            if (segmentDistancePx > BezierSegmentHitRadiusPx) {
                ClearSegmentSelection();
                return false;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            selectedSegmentIndex = segmentIndex;
            selectedAnchorIndex = -1;
            selectedHoleIndex = -1;
            draggingAnchor = false;
            draggingHole = false;
            draggingHandle = default;

            bool shiftHeld = Event.current != null && Event.current.shift;
            if (shiftHeld && kinds[segmentIndex] == SegmentKind.Bezier) {
                if (SetSegmentLine(loop, segmentIndex)) {
                    MarkDirty();
                }
                StopBezierBendDrag();
                return true;
            }

            if (EnsureSegmentBezier(loop, segmentIndex)) {
                MarkDirty();
            }

            draggingBezierBend = true;
            draggingBezierSegmentIndex = segmentIndex;
            draggingBezierLoopIndex = activeLoopIndex;
            bezierDragStartMouse = screenPoint;
            bezierDragMoved = false;
            return true;
        }

        private bool TryStartCurveHandleDrag(Vector2 screenPoint) {
            LoopData loop = GetActiveLoop();
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2) {
                return false;
            }

            int segmentIndex = selectedSegmentIndex;
            if (segmentIndex < 0 || segmentIndex >= loop.anchors.Length) {
                if (!TryGetHoveredSegmentAtMouse(loop, out int hoveredSegment, out _)) {
                    return false;
                }
                segmentIndex = hoveredSegment;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            if (segmentIndex < 0 || segmentIndex >= kinds.Length || kinds[segmentIndex] != SegmentKind.Bezier) {
                return false;
            }

            int next = (segmentIndex + 1) % loop.anchors.Length;
            AnchorData start = loop.anchors[segmentIndex];
            AnchorData end = loop.anchors[next];
            float outDistance = float.MaxValue;
            float inDistance = float.MaxValue;
            if (start.outHandle.sqrMagnitude > 0.00001f) {
                Vector2 outHandleScreen = PartToScreen(start.position + start.outHandle);
                outDistance = Vector2.Distance(screenPoint, outHandleScreen);
            }

            if (end.inHandle.sqrMagnitude > 0.00001f) {
                Vector2 inHandleScreen = PartToScreen(end.position + end.inHandle);
                inDistance = Vector2.Distance(screenPoint, inHandleScreen);
            }

            if (outDistance > HandleHitRadiusPx && inDistance > HandleHitRadiusPx) {
                return false;
            }

            bool useStartOut = outDistance <= inDistance;
            selectedSegmentIndex = segmentIndex;
            selectedHoleIndex = -1;
            selectedAnchorIndex = useStartOut ? segmentIndex : next;
            draggingAnchor = false;
            draggingHole = false;
            StopBezierBendDrag();
            draggingHandle = new SelectedHandle {
                isValid = true,
                anchorIndex = selectedAnchorIndex,
                isOutHandle = useStartOut
            };
            return true;
        }

        private bool HandleBezierToolMouseDrag(Vector2 partPoint, Vector2 screenPoint) {
            if (!draggingBezierBend) {
                return false;
            }

            if (draggingBezierLoopIndex != activeLoopIndex) {
                StopBezierBendDrag();
                return false;
            }

            LoopData loop = GetActiveLoop();
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2) {
                StopBezierBendDrag();
                return false;
            }

            if (draggingBezierSegmentIndex < 0 || draggingBezierSegmentIndex >= loop.anchors.Length) {
                StopBezierBendDrag();
                return false;
            }

            if (!bezierDragMoved && Vector2.Distance(screenPoint, bezierDragStartMouse) < BezierDragStartThresholdPx) {
                return true;
            }

            bezierDragMoved = true;
            if (SetSegmentBezierBulge(loop, draggingBezierSegmentIndex, partPoint)) {
                selectedSegmentIndex = draggingBezierSegmentIndex;
                selectedAnchorIndex = -1;
                selectedHoleIndex = -1;
                MarkDirty();
            }
            return true;
        }

        private bool EnsureSegmentBezier(LoopData loop, int segmentIndex) {
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2 || segmentIndex < 0 || segmentIndex >= loop.anchors.Length) {
                return false;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            int next = (segmentIndex + 1) % loop.anchors.Length;
            AnchorData start = loop.anchors[segmentIndex];
            AnchorData end = loop.anchors[next];
            Vector2 tangent = (end.position - start.position) * (1f / 3f);
            bool changed = false;
            if (kinds[segmentIndex] != SegmentKind.Bezier) {
                kinds[segmentIndex] = SegmentKind.Bezier;
                changed = true;
            }

            if (start.outHandle.sqrMagnitude < 0.00001f) {
                start.outHandle = tangent;
                changed = true;
            }

            if (end.inHandle.sqrMagnitude < 0.00001f) {
                end.inHandle = -tangent;
                changed = true;
            }

            loop.segmentKinds = kinds;
            return changed;
        }

        private bool SetSegmentLine(LoopData loop, int segmentIndex) {
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2 || segmentIndex < 0 || segmentIndex >= loop.anchors.Length) {
                return false;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            int next = (segmentIndex + 1) % loop.anchors.Length;
            AnchorData start = loop.anchors[segmentIndex];
            AnchorData end = loop.anchors[next];
            bool changed = false;
            if (kinds[segmentIndex] != SegmentKind.Line) {
                kinds[segmentIndex] = SegmentKind.Line;
                changed = true;
            }

            if (start.outHandle.sqrMagnitude > 0.00001f) {
                start.outHandle = Vector2.zero;
                changed = true;
            }

            if (end.inHandle.sqrMagnitude > 0.00001f) {
                end.inHandle = Vector2.zero;
                changed = true;
            }

            loop.segmentKinds = kinds;
            return changed;
        }

        private bool SetSegmentBezierBulge(LoopData loop, int segmentIndex, Vector2 targetPoint) {
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2 || segmentIndex < 0 || segmentIndex >= loop.anchors.Length) {
                return false;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            if (kinds[segmentIndex] != SegmentKind.Bezier) {
                return false;
            }

            int next = (segmentIndex + 1) % loop.anchors.Length;
            AnchorData start = loop.anchors[segmentIndex];
            AnchorData end = loop.anchors[next];
            Vector2 chord = end.position - start.position;
            float chordLength = chord.magnitude;
            if (chordLength < 0.0001f) {
                return false;
            }

            Vector2 chordMid = (start.position + end.position) * 0.5f;
            Vector2 dir = chord / chordLength;
            Vector2 normal = new Vector2(-dir.y, dir.x);
            float bulge = Vector2.Dot(targetPoint - chordMid, normal);
            float maxBulge = Mathf.Max(0.05f, chordLength * 1.5f);
            bulge = Mathf.Clamp(bulge, -maxBulge, maxBulge);

            Vector2 tangent = chord * (1f / 3f);
            Vector2 nextOutHandle = tangent + (normal * bulge);
            Vector2 nextInHandle = -tangent + (normal * bulge);
            if (Approximately(start.outHandle, nextOutHandle) && Approximately(end.inHandle, nextInHandle)) {
                return false;
            }

            start.outHandle = nextOutHandle;
            end.inHandle = nextInHandle;
            return true;
        }

        private bool TryGetNearestSegment(LoopData loop, Vector2 partPoint, out int segmentIndex, out Vector2 nearestPoint) {
            segmentIndex = -1;
            nearestPoint = Vector2.zero;
            if (loop == null || loop.anchors == null || loop.anchors.Length < 2) {
                return false;
            }

            SegmentKind[] kinds = EnsureSegmentKinds(loop);
            float bestDistance = float.MaxValue;
            for (int i = 0; i < loop.anchors.Length; i++) {
                int next = (i + 1) % loop.anchors.Length;
                AnchorData start = loop.anchors[i];
                AnchorData end = loop.anchors[next];

                float distance;
                Vector2 candidate;
                if (kinds[i] == SegmentKind.Bezier) {
                    distance = DistanceToBezier(partPoint, start, end, out candidate);
                }
                else {
                    distance = DistancePointToSegment(partPoint, start.position, end.position, out candidate);
                }

                if (distance < bestDistance) {
                    bestDistance = distance;
                    segmentIndex = i;
                    nearestPoint = candidate;
                }
            }

            return segmentIndex >= 0;
        }

        private void AddHole(Vector2 partPosition) {
            partPosition = SnapPartPoint(partPosition, allowTemporaryOverride: true);
            var holes = new List<CustomHoleDefinition>(workingDefinition.holes ?? Array.Empty<CustomHoleDefinition>());
            holes.Add(new CustomHoleDefinition {
                id = Guid.NewGuid().ToString("N"),
                position = partPosition,
                size = new Vector2(0.182f, 0.182f),
                depthInches = Mathf.Max(0.01f, workingDefinition.thicknessInches),
                shape = CustomHoleShape.Circle,
                holeType = HoleCollider.HoleType.Normal,
                twoSided = true
            });
            workingDefinition.holes = holes.ToArray();
            selectedHoleIndex = holes.Count - 1;
            selectedAnchorIndex = -1;
            ClearSegmentSelection();
            MarkDirty();
        }

        private void DeleteSelectedHole() {
            if (workingDefinition.holes == null || selectedHoleIndex < 0 || selectedHoleIndex >= workingDefinition.holes.Length) {
                return;
            }

            var holes = new List<CustomHoleDefinition>(workingDefinition.holes);
            holes.RemoveAt(selectedHoleIndex);
            workingDefinition.holes = holes.ToArray();
            selectedHoleIndex = Mathf.Clamp(selectedHoleIndex - 1, -1, holes.Count - 1);
            draggingHole = false;
            ClearSegmentSelection();
            MarkDirty();
        }

        private void AddDefaultCutout() {
            LoopData[] cutouts = workingDefinition.sketch.cutoutLoops ?? Array.Empty<LoopData>();
            var next = new List<LoopData>(cutouts);
            next.Add(new LoopData {
                id = Guid.NewGuid().ToString("N"),
                name = $"Cutout {cutouts.Length + 1}",
                closed = true,
                isCutout = true,
                anchors = new[] {
                    new AnchorData { position = new Vector2(-0.25f, -0.25f) },
                    new AnchorData { position = new Vector2(0.25f, -0.25f) },
                    new AnchorData { position = new Vector2(0.25f, 0.25f) },
                    new AnchorData { position = new Vector2(-0.25f, 0.25f) }
                },
                segmentKinds = new[] {
                    SegmentKind.Line,
                    SegmentKind.Line,
                    SegmentKind.Line,
                    SegmentKind.Line
                }
            });

            workingDefinition.sketch.cutoutLoops = next.ToArray();
            activeLoopIndex = next.Count - 1;
            selectedAnchorIndex = -1;
            ClearSegmentSelection();
            MarkDirty();
        }

        private void DeleteCutoutAt(int index) {
            LoopData[] cutouts = workingDefinition.sketch.cutoutLoops ?? Array.Empty<LoopData>();
            if (index < 0 || index >= cutouts.Length) {
                return;
            }

            var next = new List<LoopData>(cutouts);
            next.RemoveAt(index);
            workingDefinition.sketch.cutoutLoops = next.ToArray();
            activeLoopIndex = -1;
            selectedAnchorIndex = -1;
            ClearSegmentSelection();
            MarkDirty();
        }

        private string[] BuildLoopNames() {
            EnsureWorkingDefinition();
            var names = new List<string> { "Outline" };
            LoopData[] cutouts = workingDefinition.sketch.cutoutLoops ?? Array.Empty<LoopData>();
            for (int i = 0; i < cutouts.Length; i++) {
                names.Add(cutouts[i] != null ? cutouts[i].name : $"Cutout {i + 1}");
            }
            return names.ToArray();
        }

        private LoopData GetActiveLoop() {
            EnsureWorkingDefinition();
            if (activeLoopIndex < 0) {
                return workingDefinition.sketch.outerLoop;
            }

            LoopData[] cutouts = workingDefinition.sketch.cutoutLoops ?? Array.Empty<LoopData>();
            if (activeLoopIndex >= 0 && activeLoopIndex < cutouts.Length) {
                return cutouts[activeLoopIndex];
            }

            return workingDefinition.sketch.outerLoop;
        }

        private static SegmentKind[] EnsureSegmentKinds(LoopData loop) {
            if (loop == null || loop.anchors == null) {
                return Array.Empty<SegmentKind>();
            }

            int count = loop.anchors.Length;
            SegmentKind[] kinds = loop.segmentKinds;
            if (kinds != null && kinds.Length == count) {
                return kinds;
            }

            kinds = new SegmentKind[count];
            for (int i = 0; i < count; i++) {
                kinds[i] = SegmentKind.Line;
            }

            loop.segmentKinds = kinds;
            return kinds;
        }

        private static void NormalizeSegmentKinds(List<AnchorData> anchors, List<SegmentKind> kinds) {
            if (anchors == null || kinds == null) {
                return;
            }

            while (kinds.Count < anchors.Count) {
                kinds.Add(SegmentKind.Line);
            }

            while (kinds.Count > anchors.Count) {
                kinds.RemoveAt(kinds.Count - 1);
            }
        }

        private static CustomHoleShape NormalizeHoleShape(CustomHoleShape shape) {
            return shape == CustomHoleShape.Square ? CustomHoleShape.Square : CustomHoleShape.Circle;
        }

        private static int GetHoleShapeSelectionIndex(CustomHoleShape shape) {
            return NormalizeHoleShape(shape) == CustomHoleShape.Square ? 1 : 0;
        }

        private static void EnsureDefinitionIntegrity(CustomPartDefinition definition) {
            if (definition == null) {
                return;
            }

            if (definition.sketch == null) {
                definition.sketch = new SketchData();
            }

            definition.sketch.unitSystem = CustomPartUnitSystem.Inches;

            if (definition.sketch.outerLoop == null) {
                definition.sketch.outerLoop = new LoopData();
            }

            if (definition.sketch.outerLoop.anchors == null || definition.sketch.outerLoop.anchors.Length < 3) {
                definition.sketch.outerLoop.anchors = new[] {
                    new AnchorData { position = new Vector2(-1f, -0.5f) },
                    new AnchorData { position = new Vector2(1f, -0.5f) },
                    new AnchorData { position = new Vector2(1f, 0.5f) },
                    new AnchorData { position = new Vector2(-1f, 0.5f) }
                };
            }

            EnsureSegmentKinds(definition.sketch.outerLoop);
            if (definition.sketch.cutoutLoops == null) {
                definition.sketch.cutoutLoops = Array.Empty<LoopData>();
            }

            for (int i = 0; i < definition.sketch.cutoutLoops.Length; i++) {
                LoopData loop = definition.sketch.cutoutLoops[i];
                if (loop == null) {
                    continue;
                }

                if (loop.anchors == null) {
                    loop.anchors = Array.Empty<AnchorData>();
                }

                EnsureSegmentKinds(loop);
                loop.isCutout = true;
            }

            if (definition.holes == null) {
                definition.holes = Array.Empty<CustomHoleDefinition>();
            }
            else {
                for (int i = 0; i < definition.holes.Length; i++) {
                    CustomHoleDefinition hole = definition.holes[i];
                    if (hole == null) {
                        continue;
                    }

                    hole.shape = NormalizeHoleShape(hole.shape);
                    hole.holeType = HoleCollider.HoleType.Normal;
                    hole.twoSided = true;
                    if (hole.size.x <= 0f || hole.size.y <= 0f) {
                        hole.size = new Vector2(0.182f, 0.182f);
                    }
                    if (hole.depthInches <= 0f) {
                        hole.depthInches = Mathf.Max(0.01f, definition.thicknessInches);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(definition.definitionId)) {
                definition.definitionId = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(definition.name)) {
                definition.name = "Custom Part";
            }

            if (definition.thicknessInches <= 0f) {
                definition.thicknessInches = CustomPartDefinition.DefaultThicknessInches;
            }
        }

        private float GetPixelsPerUnit() {
            if (UseSceneAlignedCanvas) {
                Vector2 center = PartToScreen(Vector2.zero);
                Vector2 x1 = PartToScreen(Vector2.right);
                Vector2 y1 = PartToScreen(Vector2.up);
                float pxX = Vector2.Distance(center, x1);
                float pxY = Vector2.Distance(center, y1);
                float dynamic = Mathf.Max(pxX, pxY);
                if (dynamic > 0.0001f) {
                    return dynamic;
                }
            }

            return DefaultZoomScale * canvasZoom;
        }

        private Vector2 PartToScreen(Vector2 part) {
            if (UseSceneAlignedCanvas) {
                Camera cam = PivotCamera.Main.camera;
                float localZ = GetSceneSketchLocalZOffset();
                Transform surface = GetActiveSurfaceTransform();
                if (surface == null) {
                    return Vector2.zero;
                }

                Vector3 worldPoint = surface.TransformPoint(new Vector3(part.x, part.y, localZ));
                Vector3 screenPoint = cam.WorldToScreenPoint(worldPoint);
                return ScreenPixelsToGui(screenPoint);
            }

            float px = GetPixelsPerUnit();
            Vector2 center = canvasRect.center;
            return new Vector2(
                center.x + (part.x - canvasOrigin.x) * px,
                center.y - (part.y - canvasOrigin.y) * px);
        }

        private Vector2 ScreenToPart(Vector2 screen) {
            return ScreenToPart(screen, canvasZoom);
        }

        private Vector2 ScreenToPart(Vector2 screen, float zoom) {
            if (UseSceneAlignedCanvas) {
                Camera cam = PivotCamera.Main.camera;
                Transform surface = GetActiveSurfaceTransform();
                if (cam == null || surface == null) {
                    return Vector2.zero;
                }

                Vector2 screenPixels = GuiToScreenPixels(screen);
                Vector3 unityScreen = new Vector3(screenPixels.x, screenPixels.y, 0f);
                Ray ray = ProjectionSwitcher.ScreenPointToRay(cam, unityScreen);
                float localZ = GetSceneSketchLocalZOffset();
                Vector3 planePoint = surface.TransformPoint(new Vector3(0f, 0f, localZ));
                Vector3 planeNormal = surface.forward;
                if (TryGetSceneSketchBasis(out _, out _, out _, out Vector3 basisNormal)) {
                    planeNormal = basisNormal;
                }

                Plane sketchPlane = new Plane(planeNormal, planePoint);
                if (sketchPlane.Raycast(ray, out float enter)) {
                    Vector3 worldPoint = ray.GetPoint(enter);
                    Vector3 local = surface.InverseTransformPoint(worldPoint);
                    return new Vector2(local.x, local.y);
                }

                return Vector2.zero;
            }

            float px = DefaultZoomScale * zoom;
            Vector2 center = canvasRect.center;
            return new Vector2(
                ((screen.x - center.x) / px) + canvasOrigin.x,
                (-(screen.y - center.y) / px) + canvasOrigin.y);
        }

        private Vector2 SnapPartPoint(Vector2 point, bool allowTemporaryOverride) {
            if (!snapToGrid) {
                return point;
            }

            if (allowTemporaryOverride && IsSnapTemporarilyDisabled()) {
                return point;
            }

            float step = Mathf.Clamp(snapStepInches, MinSnapStepInches, MaxSnapStepInches);
            if (step <= 0.000001f) {
                return point;
            }

            return new Vector2(
                Mathf.Round(point.x / step) * step,
                Mathf.Round(point.y / step) * step);
        }

        private static bool IsPanModifierPressed() {
            if (Keyboard.current == null) {
                return false;
            }

            return Keyboard.current.spaceKey.isPressed;
        }

        private static bool IsSnapTemporarilyDisabled() {
            if (Keyboard.current == null) {
                return false;
            }

            return Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed;
        }

        private static bool Approximately(Vector2 a, Vector2 b) {
            return (a - b).sqrMagnitude <= 0.0000001f;
        }

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b, out Vector2 nearest) {
            Vector2 ab = b - a;
            float denom = ab.sqrMagnitude;
            if (denom < 1e-6f) {
                nearest = a;
                return Vector2.Distance(p, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            nearest = a + (ab * t);
            return Vector2.Distance(p, nearest);
        }

        private static float DistanceToBezier(Vector2 point, AnchorData start, AnchorData end, out Vector2 nearest) {
            Vector2 p0 = start.position;
            Vector2 p1 = start.position + start.outHandle;
            Vector2 p2 = end.position + end.inHandle;
            Vector2 p3 = end.position;

            nearest = p0;
            float best = float.MaxValue;
            Vector2 prev = p0;
            for (int i = 1; i <= 32; i++) {
                float t = i / 32f;
                Vector2 cur = EvaluateBezier(p0, p1, p2, p3, t);
                float d = DistancePointToSegment(point, prev, cur, out Vector2 segNearest);
                if (d < best) {
                    best = d;
                    nearest = segNearest;
                }
                prev = cur;
            }

            return best;
        }

        private static Vector2 EvaluateBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) {
            float omt = 1f - t;
            return omt * omt * omt * p0
                + 3f * omt * omt * t * p1
                + 3f * omt * t * t * p2
                + t * t * t * p3;
        }

        private void DrawBezierPolyline(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Color color, float width) {
            Vector2 prev = p0;
            for (int i = 1; i <= 24; i++) {
                float t = i / 24f;
                Vector2 cur = new Vector2(
                    Mathf.Pow(1 - t, 3) * p0.x + 3 * Mathf.Pow(1 - t, 2) * t * p1.x + 3 * (1 - t) * t * t * p2.x + t * t * t * p3.x,
                    Mathf.Pow(1 - t, 3) * p0.y + 3 * Mathf.Pow(1 - t, 2) * t * p1.y + 3 * (1 - t) * t * t * p2.y + t * t * t * p3.y);
                DrawLine(prev, cur, color, width);
                prev = cur;
            }
        }

        private void DrawFilledRect(Rect rect, Color color) {
            if (drawingCanvas) {
                float x = Mathf.Max(rect.xMin, canvasRect.xMin), y = Mathf.Max(rect.yMin, canvasRect.yMin);
                rect = Rect.MinMaxRect(x, y, Mathf.Min(rect.xMax, canvasRect.xMax), Mathf.Min(rect.yMax, canvasRect.yMax));
                if (rect.width <= 0 || rect.height <= 0) return;
            }
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, oneByOne);
            GUI.color = prev;
        }

        private void DrawPoint(Vector2 center, Color color, float diameter) {
            Rect rect = new Rect(center.x - diameter * 0.5f, center.y - diameter * 0.5f, diameter, diameter);
            DrawFilledRect(rect, color);
        }

        private void DrawRectOutline(Rect rect, Color color, float width) {
            DrawLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), color, width);
            DrawLine(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), color, width);
            DrawLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), color, width);
            DrawLine(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin), color, width);
        }

        private void DrawEllipse(Vector2 center, float rx, float ry, Color color, float width, int segments) {
            if (segments < 3) {
                return;
            }

            float step = Mathf.PI * 2f / segments;
            Vector2 prev = center + new Vector2(rx, 0f);
            for (int i = 1; i <= segments; i++) {
                float angle = i * step;
                Vector2 next = center + new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry);
                DrawLine(prev, next, color, width);
                prev = next;
            }
        }

        private void DrawLine(Vector2 a, Vector2 b, Color color, float width) {
            // Clip coordinates before rotating the IMGUI line. Rotating a GUI group also rotates its clip, cutting valid lines.
            if (drawingCanvas && !ClipCanvasLine(ref a, ref b)) return;
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;

            Vector2 delta = b - a;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            float length = delta.magnitude;

            GUI.color = color;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, length, width), oneByOne);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private bool ClipCanvasLine(ref Vector2 a, ref Vector2 b) {
            Vector2 delta = b - a;
            float enter = 0, exit = 1;
            if (!ClipEdge(-delta.x, a.x - canvasRect.xMin, ref enter, ref exit)
                || !ClipEdge(delta.x, canvasRect.xMax - a.x, ref enter, ref exit)
                || !ClipEdge(-delta.y, a.y - canvasRect.yMin, ref enter, ref exit)
                || !ClipEdge(delta.y, canvasRect.yMax - a.y, ref enter, ref exit)) return false;
            b = a + delta * exit;
            a += delta * enter;
            return true;
        }

        private static bool ClipEdge(float direction, float distance, ref float enter, ref float exit) {
            if (Mathf.Abs(direction) < 0.000001f) return distance >= 0;
            float ratio = distance / direction;
            if (direction < 0) enter = Mathf.Max(enter, ratio); else exit = Mathf.Min(exit, ratio);
            return enter <= exit;
        }

        private string TextField(string key, string label, string value) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(120f));
            string current = GetFieldCache(key, value);
            GUI.SetNextControlName(key);
            string edited = GUILayout.TextField(current, textFieldStyle, GUILayout.Height(24f));
            fieldCache[key] = edited;
            GUILayout.EndHorizontal();
            return edited;
        }

        private float FloatField(string key, string label, float value) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(120f));
            string current = GetFieldCache(key, value.ToString("0.########", CultureInfo.InvariantCulture));
            GUI.SetNextControlName(key);
            string edited = GUILayout.TextField(current, textFieldStyle, GUILayout.Height(24f));
            fieldCache[key] = edited;
            if (float.TryParse(edited, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) {
                value = parsed;
            }
            GUILayout.EndHorizontal();
            return value;
        }

        private string GetFieldCache(string key, string fallback) {
            if (!fieldCache.TryGetValue(key, out string current)) {
                current = fallback ?? string.Empty;
                fieldCache[key] = current;
            }
            return current;
        }

        private void SyncAnchorFieldsToSelection(AnchorData anchor) {
            if (anchor == null) {
                return;
            }

            string selectionKey = $"{activeLoopIndex}:{anchor.id}";
            bool editingAnchor = IsControlFocused("anchor_x") || IsControlFocused("anchor_y");
            if (!string.Equals(lastAnchorFieldSelectionKey, selectionKey, StringComparison.Ordinal) || !editingAnchor) {
                fieldCache["anchor_x"] = anchor.position.x.ToString("0.###", CultureInfo.InvariantCulture);
                fieldCache["anchor_y"] = anchor.position.y.ToString("0.###", CultureInfo.InvariantCulture);
                lastAnchorFieldSelectionKey = selectionKey;
            }
        }

        private void SyncHoleFieldsToSelection(CustomHoleDefinition hole) {
            if (hole == null) {
                return;
            }

            string selectionKey = hole.id ?? string.Empty;
            bool editingHole =
                IsControlFocused("hole_x")
                || IsControlFocused("hole_y")
                || IsControlFocused("hole_w")
                || IsControlFocused("hole_h")
                || IsControlFocused("hole_d")
                || IsControlFocused("hole_r");

            if (!string.Equals(lastHoleFieldSelectionKey, selectionKey, StringComparison.Ordinal) || !editingHole) {
                fieldCache["hole_x"] = hole.position.x.ToString("0.###", CultureInfo.InvariantCulture);
                fieldCache["hole_y"] = hole.position.y.ToString("0.###", CultureInfo.InvariantCulture);
                fieldCache["hole_w"] = hole.size.x.ToString("0.###", CultureInfo.InvariantCulture);
                fieldCache["hole_h"] = hole.size.y.ToString("0.###", CultureInfo.InvariantCulture);
                fieldCache["hole_d"] = hole.depthInches.ToString("0.###", CultureInfo.InvariantCulture);
                fieldCache["hole_r"] = hole.rotationDegrees.ToString("0.###", CultureInfo.InvariantCulture);
                lastHoleFieldSelectionKey = selectionKey;
            }
        }

        private static bool IsControlFocused(string controlName) {
            return string.Equals(GUI.GetNameOfFocusedControl(), controlName, StringComparison.Ordinal);
        }

        private string GetActiveLoopDisplayName() {
            if (activeLoopIndex < 0) {
                return "Outline";
            }

            LoopData[] cutouts = workingDefinition?.sketch?.cutoutLoops ?? Array.Empty<LoopData>();
            if (activeLoopIndex >= 0 && activeLoopIndex < cutouts.Length && cutouts[activeLoopIndex] != null) {
                return cutouts[activeLoopIndex].name;
            }

            return "Outline";
        }

        private void HandleUndoRedoHotkeys() {
            Keyboard key = Keyboard.current;
            if (key == null || showCloseDialog) return;
            bool ctrl = key.leftCtrlKey.isPressed || key.rightCtrlKey.isPressed;
            if (ctrl && key.sKey.wasPressedThisFrame) { SaveWorkingDefinition(); return; }
            if (typingInField) return;
            if (ctrl) {
                if (key.zKey.wasPressedThisFrame) { if (key.leftShiftKey.isPressed || key.rightShiftKey.isPressed) RedoHistory(); else UndoHistory(); }
                if (key.yKey.wasPressedThisFrame) RedoHistory();
                if (key.enterKey.wasPressedThisFrame) FinishAndInsert();
                return;
            }
            if (key.vKey.wasPressedThisFrame) SetActiveTool(CanvasTool.Select);
            if (key.pKey.wasPressedThisFrame) SetActiveTool(CanvasTool.AddPoint);
            if (key.bKey.wasPressedThisFrame) SetActiveTool(CanvasTool.ToggleBezier);
            if (key.hKey.wasPressedThisFrame) { SetActiveTool(CanvasTool.AddHole); activeTab = StudioTab.Holes; }
            if (key.fKey.wasPressedThisFrame) fitOnNextCanvas = true;
        }

        private void ResetHistoryWithCurrentDefinition() {
            history.Clear();
            historyIndex = -1;
            historyPending = false;
            applyingHistory = false;

            if (workingDefinition == null) {
                return;
            }

            history.Add(workingDefinition.CloneDeep());
            historyIndex = 0;
        }

        private void CommitHistoryIfReady() {
            if (!historyPending || applyingHistory || workingDefinition == null) {
                return;
            }

            Mouse mouse = Mouse.current;
            bool pointerBusy = mouse != null && (mouse.leftButton.isPressed || mouse.middleButton.isPressed);
            if (pointerBusy) {
                return;
            }

            CommitHistorySnapshot();
            historyPending = false;
        }

        private void CommitHistorySnapshot() {
            if (workingDefinition == null) {
                return;
            }

            string currentHash = workingDefinition.GetDeterministicHash();
            if (historyIndex >= 0 && historyIndex < history.Count) {
                string previousHash = history[historyIndex].GetDeterministicHash();
                if (string.Equals(currentHash, previousHash, StringComparison.Ordinal)) {
                    return;
                }
            }

            if (historyIndex < history.Count - 1) {
                history.RemoveRange(historyIndex + 1, history.Count - (historyIndex + 1));
            }

            history.Add(workingDefinition.CloneDeep());
            if (history.Count > MaxHistoryStates) {
                history.RemoveAt(0);
            }

            historyIndex = history.Count - 1;
        }

        private void UndoHistory() {
            if (historyIndex <= 0 || history.Count == 0) {
                return;
            }

            applyingHistory = true;
            historyIndex--;
            workingDefinition = history[historyIndex].CloneDeep();
            EnsureDefinitionIntegrity(workingDefinition);
            selectedAnchorIndex = -1;
            selectedHoleIndex = -1;
            ClearSegmentSelection();
            fieldCache.Clear();
            hasUnsavedChanges = true;
            historyPending = false;
            applyingHistory = false;
            InvalidateGeometry();
            QueueLivePreviewUpdate();
        }

        private void RedoHistory() {
            if (historyIndex < 0 || historyIndex >= history.Count - 1) {
                return;
            }

            applyingHistory = true;
            historyIndex++;
            workingDefinition = history[historyIndex].CloneDeep();
            EnsureDefinitionIntegrity(workingDefinition);
            selectedAnchorIndex = -1;
            selectedHoleIndex = -1;
            ClearSegmentSelection();
            fieldCache.Clear();
            hasUnsavedChanges = true;
            historyPending = false;
            applyingHistory = false;
            InvalidateGeometry();
            QueueLivePreviewUpdate();
        }

        private void MarkDirty() {
            InvalidateGeometry();
            fieldCache.Remove("outline_width");
            fieldCache.Remove("outline_height");
            hasUnsavedChanges = true;
            if (!applyingHistory) {
                historyPending = true;
            }
            QueueLivePreviewUpdate();
        }

        private void QueueLivePreviewUpdate() {
            if (launchMode != StudioLaunchMode.EditSelectedInstance || editTargetObject == null || workingDefinition == null) {
                return;
            }

            livePreviewDirty = true;
        }

        private void ApplyLivePreviewIfNeeded(bool force) {
            if (launchMode != StudioLaunchMode.EditSelectedInstance || editTargetObject == null || workingDefinition == null) {
                return;
            }

            if (!force && !livePreviewDirty) {
                return;
            }

            if (!force && Time.unscaledTime < livePreviewNextApplyTime) {
                return;
            }

            if (force) RefreshGeometryValidation(true);
            if (geometryPending || !geometryValid || validatedGeometry == null) return;
            bool applied = CustomPartRuntimeUpdater.ApplyCompiledPreview(editTargetObject, workingDefinition, validatedGeometry);
            if (applied) {
                ResolveEditSurface(editTargetObject);
                livePreviewDirty = false;
                livePreviewApplied = true;
            }
            livePreviewNextApplyTime = Time.unscaledTime + LivePreviewApplyIntervalSeconds;
        }

        private void RestoreLivePreviewTargetIfNeeded() {
            if (launchMode != StudioLaunchMode.EditSelectedInstance || editTargetObject == null) {
                return;
            }

            if (livePreviewCommitted || !livePreviewApplied) {
                return;
            }

            if (string.IsNullOrWhiteSpace(livePreviewOriginalDefinitionId)) {
                return;
            }

            CustomPartRuntimeUpdater.ApplyDefinitionToObject(editTargetObject, livePreviewOriginalDefinitionId, livePreviewOriginalInstanceId);
            ResolveEditSurface(editTargetObject);
            livePreviewDirty = false;
            livePreviewApplied = false;
        }

        private string BuildDefaultName(string baseName) {
            string safeBase = string.IsNullOrWhiteSpace(baseName) ? "Custom Part" : baseName.Trim();
            HashSet<string> existing = new HashSet<string>(
                CustomPartRegistry.GetAllDefinitions().Select(x => x.name),
                StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(safeBase)) {
                return safeBase;
            }

            for (int i = 2; i < 5000; i++) {
                string candidate = $"{safeBase} {i}";
                if (!existing.Contains(candidate)) {
                    return candidate;
                }
            }

            return $"{safeBase} {Guid.NewGuid().ToString("N").Substring(0, 4)}";
        }

        private static string SanitizeFileName(string rawName) {
            string name = string.IsNullOrWhiteSpace(rawName) ? "custom-part" : rawName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) {
                name = name.Replace(c, '-');
            }
            return name;
        }
    }
}
