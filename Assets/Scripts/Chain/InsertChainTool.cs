using System.Collections.Generic;
using Protobot.InputEvents;
using Protobot.StateSystems;
using Protobot.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Protobot.ChainSystem {
    public class InsertChainTool : MonoBehaviour {
        private enum ChainSizeMode {
            Auto,
            Pitch3p75,
            Pitch6p35,
            Pitch9p79
        }

        [SerializeField] private ToolToggle toolToggle;
        [SerializeField] private MouseCast mouseCast;
        [SerializeField] private InputEvent selectInput;
        [SerializeField] private InputEvent cancelInput;

        [Header("Chain Settings")]
        [SerializeField] private ChainSizeMode selectedSizeMode = ChainSizeMode.Auto;
        [SerializeField] private ChainStandard selectedStandard = ChainStandard.Pitch6p35;
        [SerializeField] private float slack = 0f;
        [SerializeField] private bool autoAlignSecondEndpoint = true;
        [SerializeField] private float autoAlignMaxDistance = 20f;
        [SerializeField] private bool singleChainPerEndpoint = true;

        [Header("Fallback Input")]
        [SerializeField] private bool enableHotkeyFallback = true;
        [SerializeField] private Key holdToChainKey = Key.C;

        private readonly List<ChainEndpoint> pendingEndpoints = new List<ChainEndpoint>();
        private readonly List<TextMeshPro> orderMarkers = new List<TextMeshPro>();
        private ChainConnection draftConnection;
        private ChainConnection sourceConnection;
        private bool fallbackWasHoldingChainKey;
        private int lastSelectionFrame = -1;
        private Transform orderMarkerRoot;

        private bool ToolActive => toolToggle == null || toolToggle.active;

        private void Awake() {
            if (selectInput != null) {
                selectInput.performed += OnSelectInput;
            }

            if (cancelInput != null) {
                cancelInput.performed += CancelSelection;
            }

            SetToolToggle(toolToggle);
        }

        private void OnDestroy() {
            if (selectInput != null) {
                selectInput.performed -= OnSelectInput;
            }

            if (cancelInput != null) {
                cancelInput.performed -= CancelSelection;
            }

            SetToolToggle(null);

            DestroyOrderMarkers();
            RestoreSourceConnection();
            DestroyDraftChain();
        }

        private void Update() {
            bool holdingChainKey = false;
            bool mouseClicked = false;
            bool escapePressed = false;
            bool backspacePressed = false;

            if (Keyboard.current != null) {
                holdingChainKey = Keyboard.current[holdToChainKey].isPressed;
                escapePressed = Keyboard.current.escapeKey.wasPressedThisFrame;
                backspacePressed = Keyboard.current.backspaceKey.wasPressedThisFrame;
            }

            if (enableHotkeyFallback && Mouse.current != null) {
                mouseClicked = Mouse.current.leftButton.wasPressedThisFrame;
            }

            if (enableHotkeyFallback) {
                if (!holdingChainKey) {
                    try {
                        holdingChainKey = UnityEngine.Input.GetKey(KeyCode.C);
                    }
                    catch {
                        // Ignore when legacy input backend is disabled.
                    }
                }

                if (!mouseClicked) {
                    try {
                        mouseClicked = UnityEngine.Input.GetMouseButtonDown(0);
                    }
                    catch {
                        // Ignore when legacy input backend is disabled.
                    }
                }
            }

            if (!escapePressed) {
                try {
                    escapePressed = UnityEngine.Input.GetKeyDown(KeyCode.Escape);
                }
                catch {
                    // Ignore when legacy input backend is disabled.
                }
            }

            if (!backspacePressed) {
                try {
                    backspacePressed = UnityEngine.Input.GetKeyDown(KeyCode.Backspace);
                }
                catch {
                    // Ignore when legacy input backend is disabled.
                }
            }

            bool overUI = IsPointerOverUi();

            if (enableHotkeyFallback && mouseClicked && !overUI) {
                if (ToolActive) {
                    bool consumed = TryHandleSelectInput(false);
                    if (!consumed && pendingEndpoints.Count >= 2) {
                        FinalizePendingSelection();
                    }
                }
                else if (holdingChainKey) {
                    TryHandleSelectInput(true);
                }
            }

            if (cancelInput == null && escapePressed) {
                CancelSelection();
            }

            if (ToolActive && backspacePressed) {
                RemoveLastPendingEndpoint();
            }

            if (enableHotkeyFallback && fallbackWasHoldingChainKey && !holdingChainKey && !ToolActive) {
                FinalizePendingSelection();
            }

            fallbackWasHoldingChainKey = holdingChainKey;
        }

        private void LateUpdate() {
            UpdateOrderMarkers();
        }

        public void SetToolToggle(ToolToggle newToolToggle) {
            if (toolToggle == newToolToggle) {
                return;
            }

            if (toolToggle != null) {
                toolToggle.EnsureEventsInitialized();
                toolToggle.OnDeactivate.RemoveListener(HandleToolDeactivated);
            }

            toolToggle = newToolToggle;
            if (toolToggle != null) {
                toolToggle.EnsureEventsInitialized();
                toolToggle.OnDeactivate.AddListener(HandleToolDeactivated);
            }
        }

        public void SetStandardAuto() {
            selectedSizeMode = ChainSizeMode.Auto;
            selectedStandard = ChainStandard.Pitch6p35;
            RebuildDraftChain();
        }

        public void SetPitch3p75() {
            selectedSizeMode = ChainSizeMode.Pitch3p75;
            selectedStandard = ChainStandard.Pitch3p75;
            RebuildDraftChain();
        }

        public void SetPitch6p35() {
            selectedSizeMode = ChainSizeMode.Pitch6p35;
            selectedStandard = ChainStandard.Pitch6p35;
            RebuildDraftChain();
        }

        public void SetPitch9p79() {
            selectedSizeMode = ChainSizeMode.Pitch9p79;
            selectedStandard = ChainStandard.Pitch9p79;
            RebuildDraftChain();
        }

        public void SetToolbarStandardIndex(int index) {
            switch (index) {
                case 1:
                    SetPitch6p35();
                    break;
                case 2:
                    SetPitch9p79();
                    break;
                default:
                    SetStandardAuto();
                    break;
            }
        }

        public int GetToolbarStandardIndex() {
            switch (selectedSizeMode) {
                case ChainSizeMode.Pitch6p35:
                    return 1;
                case ChainSizeMode.Pitch9p79:
                    return 2;
                default:
                    return 0;
            }
        }

        public void SetMouseCast(MouseCast newMouseCast) {
            mouseCast = newMouseCast;
        }

        public void CancelSelection() {
            pendingEndpoints.Clear();
            RefreshOrderMarkers();
            RestoreSourceConnection();
            DestroyDraftChain();
        }

        private void OnSelectInput() {
            TryHandleSelectInput(false);
        }

        private bool TryHandleSelectInput(bool allowHotkeyBypass) {
            if (Time.frameCount == lastSelectionFrame) {
                return false;
            }

            bool interactionEnabled = ToolActive || allowHotkeyBypass;
            if (!interactionEnabled || mouseCast == null || IsPointerOverUi() || !mouseCast.overObj) {
                return false;
            }

            ChainEndpoint hoveredEndpoint = ChainSprocketUtility.GetOrCreateEndpoint(mouseCast.gameObject);
            if (hoveredEndpoint == null) {
                return false;
            }

            if (pendingEndpoints.Count == 0 && TryBeginEditingExistingChain(hoveredEndpoint)) {
                lastSelectionFrame = Time.frameCount;
                RefreshOrderMarkers();
                return true;
            }

            if (pendingEndpoints.Count > 0 && hoveredEndpoint == pendingEndpoints[pendingEndpoints.Count - 1]) {
                return true;
            }

            if (pendingEndpoints.Contains(hoveredEndpoint)) {
                return true;
            }

            pendingEndpoints.Add(hoveredEndpoint);
            lastSelectionFrame = Time.frameCount;
            RefreshOrderMarkers();

            if (pendingEndpoints.Count == 1) {
                return true;
            }

            if (!RebuildDraftChain()) {
                pendingEndpoints.RemoveAt(pendingEndpoints.Count - 1);
                RefreshOrderMarkers();
                RebuildDraftChain();
                return true;
            }

            return true;
        }

        private bool TryBeginEditingExistingChain(ChainEndpoint endpoint) {
            if (!ChainManager.TryGetConnectionForEndpoint(endpoint, out ChainConnection existingConnection) || existingConnection == null) {
                return false;
            }

            sourceConnection = existingConnection;
            pendingEndpoints.Clear();

            for (int i = 0; i < existingConnection.Endpoints.Count; i++) {
                pendingEndpoints.Add(existingConnection.Endpoints[i]);
            }

            sourceConnection.gameObject.SetActive(false);
            if (RebuildDraftChain()) {
                RefreshOrderMarkers();
                return true;
            }

            sourceConnection.gameObject.SetActive(true);
            sourceConnection = null;
            pendingEndpoints.Clear();
            RefreshOrderMarkers();
            DestroyDraftChain();
            return false;
        }

        private void FinalizePendingSelection() {
            if (pendingEndpoints.Count < 2) {
                CancelSelection();
                return;
            }

            if (PendingSelectionMatchesSourceConnection()) {
                CancelSelection();
                return;
            }

            if (draftConnection == null && !RebuildDraftChain()) {
                CancelSelection();
                return;
            }

            if (draftConnection == null) {
                CancelSelection();
                return;
            }

            draftConnection.SetPreviewMode(false);

            ChainConnection finalizedConnection = draftConnection;
            draftConnection = null;

            if (sourceConnection != null) {
                CommitReplaceState(sourceConnection, finalizedConnection);
                new ObjectElement(sourceConnection.gameObject).ApplyExistence(false);
                sourceConnection = null;
            }
            else {
                CommitCreateState(finalizedConnection);
            }

            pendingEndpoints.Clear();
            RefreshOrderMarkers();
        }

        private ChainSettings BuildCurrentSettings() {
            bool autoResolveStandard = selectedSizeMode == ChainSizeMode.Auto;
            ChainStandard requestedStandard = autoResolveStandard
                ? ChainSprocketUtility.ResolveAutoStandard(pendingEndpoints, ChainStandard.Pitch6p35)
                : selectedStandard;

            ChainSettings settings = ChainSettings.CreateDefault(requestedStandard, autoResolveStandard);
            settings.slack = slack;
            settings.autoAlignSecondEndpoint = autoAlignSecondEndpoint && pendingEndpoints.Count == 2;
            settings.autoAlignMaxDistance = autoAlignMaxDistance;
            settings.singleChainPerEndpoint = sourceConnection == null && singleChainPerEndpoint;
            return settings;
        }

        private bool RebuildDraftChain() {
            if (pendingEndpoints.Count < 2) {
                DestroyDraftChain();
                return true;
            }

            ChainConnection previousDraft = draftConnection;
            if (previousDraft != null) {
                ChainManager.Unregister(previousDraft);
                previousDraft.gameObject.SetActive(false);
            }

            bool created = ChainManager.TryCreateBoundChain(
                pendingEndpoints,
                BuildCurrentSettings(),
                out ChainConnection rebuiltConnection,
                out string _);

            if (!created || rebuiltConnection == null) {
                if (previousDraft != null) {
                    previousDraft.gameObject.SetActive(true);
                    ChainManager.Register(previousDraft);
                    draftConnection = previousDraft;
                }

                return false;
            }

            rebuiltConnection.SetPreviewMode(true);
            draftConnection = rebuiltConnection;

            if (previousDraft != null && previousDraft != rebuiltConnection) {
                Destroy(previousDraft.gameObject);
            }

            return true;
        }

        private void DestroyDraftChain() {
            if (draftConnection == null) {
                return;
            }

            Destroy(draftConnection.gameObject);
            draftConnection = null;
        }

        private void RestoreSourceConnection() {
            if (sourceConnection == null) {
                return;
            }

            sourceConnection.gameObject.SetActive(true);
            sourceConnection = null;
        }

        private static void CommitCreateState(ChainConnection connection) {
            if (StateSystem.instance == null || StateSystem.states == null || StateSystem.states.Count == 0 || connection == null) {
                return;
            }

            ObjectElement previousElement = new ObjectElement(connection.gameObject);
            previousElement.existing = false;
            StateSystem.AddElement(previousElement);
            StateSystem.AddState(new ObjectElement(connection.gameObject));
        }

        private static void CommitReplaceState(ChainConnection oldConnection, ChainConnection newConnection) {
            if (StateSystem.instance == null
                || StateSystem.states == null
                || StateSystem.states.Count == 0
                || oldConnection == null
                || newConnection == null) {
                return;
            }

            var previousElements = new List<IElement>();
            var nextElements = new List<IElement>();

            ObjectElement previousOld = new ObjectElement(oldConnection.gameObject);
            previousOld.existing = true;
            previousElements.Add(previousOld);

            ObjectElement previousNew = new ObjectElement(newConnection.gameObject);
            previousNew.existing = false;
            previousElements.Add(previousNew);

            ObjectElement nextOld = new ObjectElement(oldConnection.gameObject);
            nextOld.existing = false;
            nextElements.Add(nextOld);

            ObjectElement nextNew = new ObjectElement(newConnection.gameObject);
            nextNew.existing = true;
            nextElements.Add(nextNew);

            StateSystem.AddElements(previousElements);
            StateSystem.AddState(new State(nextElements));
        }

        private void HandleToolDeactivated() {
            if (pendingEndpoints.Count >= 2) {
                FinalizePendingSelection();
                return;
            }

            CancelSelection();
        }

        private void RemoveLastPendingEndpoint() {
            if (pendingEndpoints.Count == 0) {
                return;
            }

            pendingEndpoints.RemoveAt(pendingEndpoints.Count - 1);
            RefreshOrderMarkers();

            if (pendingEndpoints.Count < 2) {
                DestroyDraftChain();
                return;
            }

            if (!RebuildDraftChain()) {
                CancelSelection();
            }
        }

        private bool PendingSelectionMatchesSourceConnection() {
            if (sourceConnection == null || pendingEndpoints.Count != sourceConnection.Endpoints.Count) {
                return false;
            }

            for (int i = 0; i < pendingEndpoints.Count; i++) {
                if (pendingEndpoints[i] != sourceConnection.Endpoints[i]) {
                    return false;
                }
            }

            return true;
        }

        private bool IsPointerOverUi() {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private void RefreshOrderMarkers() {
            while (orderMarkers.Count < pendingEndpoints.Count) {
                orderMarkers.Add(CreateOrderMarker());
            }

            for (int i = 0; i < orderMarkers.Count; i++) {
                if (orderMarkers[i] == null) {
                    continue;
                }

                bool active = i < pendingEndpoints.Count;
                orderMarkers[i].gameObject.SetActive(active);
                if (active) {
                    orderMarkers[i].text = (i + 1).ToString();
                }
            }
        }

        private void UpdateOrderMarkers() {
            if (pendingEndpoints.Count == 0) {
                return;
            }

            Camera activeCamera = Camera.main;
            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < pendingEndpoints.Count; i++) {
                centroid += pendingEndpoints[i].WorldCenter;
            }
            centroid /= pendingEndpoints.Count;

            for (int i = 0; i < pendingEndpoints.Count; i++) {
                TextMeshPro marker = i < orderMarkers.Count ? orderMarkers[i] : null;
                ChainEndpoint endpoint = pendingEndpoints[i];
                if (marker == null || endpoint == null) {
                    continue;
                }

                Vector3 axis = endpoint.WorldAxis.sqrMagnitude > 0.0001f
                    ? endpoint.WorldAxis.normalized
                    : Vector3.up;
                Vector3 radial = Vector3.ProjectOnPlane(endpoint.WorldCenter - centroid, axis);

                if (radial.sqrMagnitude < 0.0001f && activeCamera != null) {
                    radial = Vector3.ProjectOnPlane(activeCamera.transform.right, axis);
                }
                if (radial.sqrMagnitude < 0.0001f) {
                    radial = Vector3.ProjectOnPlane(Vector3.right, axis);
                }
                if (radial.sqrMagnitude < 0.0001f) {
                    radial = Vector3.forward;
                }

                radial.Normalize();

                float offsetDistance = Mathf.Max(endpoint.PitchRadius * 0.55f, 0.12f);
                Vector3 markerPosition = endpoint.WorldCenter + (radial * offsetDistance) + (axis * 0.03f);
                marker.transform.position = markerPosition;

                if (activeCamera != null) {
                    marker.transform.rotation = Quaternion.LookRotation(markerPosition - activeCamera.transform.position, activeCamera.transform.up);
                    float distance = Vector3.Distance(activeCamera.transform.position, markerPosition);
                    float scale = Mathf.Clamp(distance * 0.0045f, 0.03f, 0.18f);
                    marker.transform.localScale = Vector3.one * scale;
                }
            }
        }

        private TextMeshPro CreateOrderMarker() {
            if (orderMarkerRoot == null) {
                GameObject rootObject = new GameObject("Chain Order Markers");
                orderMarkerRoot = rootObject.transform;
            }

            var markerObject = new GameObject("Order Marker");
            markerObject.transform.SetParent(orderMarkerRoot, false);

            TextMeshPro marker = markerObject.AddComponent<TextMeshPro>();
            marker.text = "1";
            marker.alignment = TextAlignmentOptions.Center;
            marker.enableWordWrapping = false;
            marker.fontSize = 5f;
            marker.color = Color.white;
            marker.outlineColor = new Color(0.1f, 0.1f, 0.1f, 1f);
            marker.outlineWidth = 0.15f;
            marker.raycastTarget = false;
            return marker;
        }

        private void DestroyOrderMarkers() {
            for (int i = 0; i < orderMarkers.Count; i++) {
                if (orderMarkers[i] != null) {
                    Destroy(orderMarkers[i].gameObject);
                }
            }

            orderMarkers.Clear();

            if (orderMarkerRoot != null) {
                Destroy(orderMarkerRoot.gameObject);
                orderMarkerRoot = null;
            }
        }
    }
}
