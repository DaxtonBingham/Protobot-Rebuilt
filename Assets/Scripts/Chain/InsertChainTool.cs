using System;
using System.Collections.Generic;
using System.Linq;
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

        private sealed class GuideSettingsElement : IElement {
            readonly ChainGuide guide;
            readonly ChainGuideRoutingBias bias;
            readonly bool flip;
            readonly Vector3 hint;
            public GuideSettingsElement(ChainGuide target, ChainGuideRoutingBias routing, bool side, Vector3 contact) { guide = target; bias = routing; flip = side; hint = contact; }
            public void Load() { if (guide != null) { guide.SetRoutingBias(bias); guide.SetFlipSide(flip); guide.SetLocalContactHint(hint); ChainManager.NotifyEndpointObjectChanged(guide.gameObject); } }
        }

        private sealed class ChainTransformElement : IElement {
            readonly Transform target;
            readonly Vector3 position;
            readonly Quaternion rotation;
            public ChainTransformElement(Transform t, Vector3 p, Quaternion r) { target = t; position = p; rotation = r; }
            public void Load() {
                if (target == null) return;
                DG.Tweening.DOTween.Kill(target);
                target.SetPositionAndRotation(position, rotation);
                ChainManager.NotifyEndpointObjectChanged(target.gameObject);
            }
        }

        private sealed class GuideExistenceElement : IElement {
            readonly ChainGuide guide;
            readonly bool exists;
            public GuideExistenceElement(ChainGuide target, bool value) { guide = target; exists = value; }
            public void Load() { if (guide != null) guide.gameObject.SetActive(exists); }
        }

        private struct TransformSnapshot {
            public Vector3 position;
            public Quaternion rotation;

            public TransformSnapshot(Vector3 newPosition, Quaternion newRotation) {
                position = newPosition;
                rotation = newRotation;
            }
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


        private readonly List<ChainEndpoint> pendingEndpoints = new List<ChainEndpoint>();
        private readonly List<TextMeshPro> orderMarkers = new List<TextMeshPro>();
        private readonly Dictionary<Transform, TransformSnapshot> adjustedDraftTransforms = new Dictionary<Transform, TransformSnapshot>();
        private readonly Dictionary<ChainGuide, (ChainGuideRoutingBias bias, bool flip, Vector3 hint)> originalGuideSettings = new Dictionary<ChainGuide, (ChainGuideRoutingBias, bool, Vector3)>();
        private readonly List<GameObject> draftGuideParts = new List<GameObject>();
        public bool AutoAlign => autoAlignSecondEndpoint;
        public void SetAutoAlign(bool value) { autoAlignSecondEndpoint = value; RefreshDraftPreview(); }
        public void RememberGuideSettings(ChainGuide guide) {
            if (guide != null && !originalGuideSettings.ContainsKey(guide)) originalGuideSettings.Add(guide, (guide.RoutingBias, guide.FlipSide, guide.LocalContactHint));
        }
        private void RestoreGuideSettings() {
            foreach (var pair in originalGuideSettings) if (pair.Key != null) {
                pair.Key.SetRoutingBias(pair.Value.bias); pair.Key.SetFlipSide(pair.Value.flip); pair.Key.SetLocalContactHint(pair.Value.hint);
            }
            originalGuideSettings.Clear();
            foreach (var part in draftGuideParts) if (part != null) ChainGuideRuntimeAuthoring.RemoveRuntimeGuide(part);
            draftGuideParts.Clear();
        }
        private ChainConnection draftConnection;
        private ChainConnection sourceConnection;
        private int lastSelectionFrame = -1;
        private Transform orderMarkerRoot;
        private int selectedEndpointIndex = -1;
        private string lastDraftErrorMessage = string.Empty;

        private bool ToolActive => toolToggle == null || toolToggle.active;

        public event Action StateChanged;

        public bool IsToolActive => ToolActive;
        public bool IsToolToggled => toolToggle == null || toolToggle.toggled;
        public bool HasValidDraft => draftConnection != null && pendingEndpoints.Count >= 2 && string.IsNullOrEmpty(CurrentValidationMessage);
        public ChainConnection EditingSourceConnection => sourceConnection;
        public bool IsDraftGuide(GameObject part) => draftGuideParts.Contains(part);
        public Vector3 GetCommittedContactHint(ChainGuide guide) => originalGuideSettings.TryGetValue(guide, out var value) ? value.hint : guide.LocalContactHint;
        public void GetCommittedGuideSettings(ChainGuide guide, out ChainGuideRoutingBias bias, out bool flip) {
            if (originalGuideSettings.TryGetValue(guide, out var value)) { bias = value.bias; flip = value.flip; }
            else { bias = guide.RoutingBias; flip = guide.FlipSide; }
        }
        public void GetCommittedTransform(Transform target, ref Vector3 position, ref Quaternion rotation) {
            if (adjustedDraftTransforms.TryGetValue(target, out TransformSnapshot value)) { position = value.position; rotation = value.rotation; }
        }
        public bool IsEditingExisting => sourceConnection != null;
        public IReadOnlyList<ChainEndpoint> PendingEndpoints => pendingEndpoints;
        public int SelectedEndpointIndex => selectedEndpointIndex;
        public ChainGuide SelectedGuide => selectedEndpointIndex >= 0 && selectedEndpointIndex < pendingEndpoints.Count
            ? pendingEndpoints[selectedEndpointIndex]?.GetComponent<ChainGuide>() : null;
        public string CurrentValidationMessage => !string.IsNullOrEmpty(lastDraftErrorMessage) ? lastDraftErrorMessage : draftConnection != null ? draftConnection.ValidationMessage : string.Empty;

        private void Awake() {
            instance = this;
            if (selectInput != null) {
                selectInput.performed += OnSelectInput;
            }

            if (cancelInput != null) {
                cancelInput.performed += CancelSelection;
            }

            SetToolToggle(toolToggle);
        }

        private void OnDestroy() {
            if (instance == this) instance = null;
            if (selectInput != null) {
                selectInput.performed -= OnSelectInput;
            }

            if (cancelInput != null) {
                cancelInput.performed -= CancelSelection;
            }

            SetToolToggle(null);

            DestroyOrderMarkers();
            RestoreAdjustedDraftTransforms();
            RestoreGuideSettings();
            RestoreSourceConnection();
            DestroyDraftChain();
        }

        private bool gestureActive;
        private bool browserPlacement;
        private static InsertChainTool instance;
        private bool waitForKeyRelease;
        private string interactionHint = "";
        private float hintUntil;
        public bool GestureActive => gestureActive;
        public bool BrowserPlacement => browserPlacement;
        public static bool IsInteracting => IsGestureKeyHeld || (instance != null && instance.gestureActive);
        public string InteractionHint => Time.unscaledTime < hintUntil ? interactionHint : "";

        public static bool IsGestureKeyHeld {
            get {
                var key = Keyboard.current;
                return key != null && key.cKey.isPressed
                    && !key.leftCtrlKey.isPressed && !key.rightCtrlKey.isPressed
                    && !key.leftAltKey.isPressed && !key.rightAltKey.isPressed
                    && !IsTyping() && !Protobot.CustomParts.CustomPartStudioController.IsStudioOpen;
            }
        }

        private static bool IsTyping() {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null) return false;
            var input = selected.GetComponent<UnityEngine.UI.InputField>();
            var tmp = selected.GetComponent<TMP_InputField>();
            return (input != null && input.isFocused) || (tmp != null && tmp.isFocused);
        }

        public void BeginGesture() {
            StartGesture(false);
        }

        public void BeginPlacement() {
            StartGesture(true);
        }

        private void StartGesture(bool fromBrowser) {
            if (gestureActive) return;
            browserPlacement = fromBrowser;
            waitForKeyRelease = false;
            gestureActive = true;
            interactionHint = "";
            if (toolToggle != null) toolToggle.Toggle(true);
            NotifyStateChanged();
        }

        public void EndGesture(bool commit) {
            if (!gestureActive) return;
            bool valid = HasValidDraft;
            bool editing = IsEditingExisting;
            string error = CurrentValidationMessage;
            if (commit && valid) ApplyDraft(); else CancelSelection();
            gestureActive = false;
            browserPlacement = false;
            if (toolToggle != null) toolToggle.Toggle(false);
            if (!commit) ShowInteractionHint("Chain canceled");
            else if (valid) ShowInteractionHint((editing ? "Chain updated" : "Chain added") + "  /  Ctrl+Z to undo");
            else if (!string.IsNullOrEmpty(error)) ShowInteractionHint(error);
            NotifyStateChanged();
        }

        private void ShowInteractionHint(string message) { interactionHint = message; hintUntil = Time.unscaledTime + 3f; }

        private void Update() {
            bool held = IsGestureKeyHeld;
            var key = Keyboard.current;
            var mouse = Mouse.current;
            if (IsTyping() || Protobot.CustomParts.CustomPartStudioController.IsStudioOpen) {
                if (gestureActive) EndGesture(false);
                return;
            }
            if (waitForKeyRelease) { if (!held) waitForKeyRelease = false; return; }
            if (held && !gestureActive) BeginGesture();
            if (!gestureActive) return;
            if ((key != null && key.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame)) {
                EndGesture(false); waitForKeyRelease = true; return;
            }
            if (!held && !browserPlacement) { EndGesture(true); return; }
            if (key != null) {
                if (browserPlacement && (key.enterKey.wasPressedThisFrame || key.numpadEnterKey.wasPressedThisFrame) && HasValidDraft) { EndGesture(true); return; }
                if (key.backspaceKey.wasPressedThisFrame) RemovePendingEndpointAt(pendingEndpoints.Count - 1);
                if (key.tabKey.wasPressedThisFrame) SetToolbarStandardIndex((GetToolbarStandardIndex() + 1) % 4);
                if (key.aKey.wasPressedThisFrame) SetAutoAlign(!AutoAlign);
                if (key.fKey.wasPressedThisFrame) FlipSelectedGuideSide();
                if (key.rKey.wasPressedThisFrame) MoveSelectedGuideToOtherRun();
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !IsPointerOverUi()) TryHandleSelectInput(false);
        }

        private void OnApplicationFocus(bool focused) {
            if (!focused && gestureActive) { EndGesture(false); waitForKeyRelease = true; }
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
                    SetPitch3p75();
                    break;
                case 2:
                    SetPitch6p35();
                    break;
                case 3:
                    SetPitch9p79();
                    break;
                default:
                    SetStandardAuto();
                    break;
            }
        }

        public int GetToolbarStandardIndex() {
            switch (selectedSizeMode) {
                case ChainSizeMode.Pitch3p75:
                    return 1;
                case ChainSizeMode.Pitch6p35:
                    return 2;
                case ChainSizeMode.Pitch9p79:
                    return 3;
                default:
                    return 0;
            }
        }

        public void SetMouseCast(MouseCast newMouseCast) {
            mouseCast = newMouseCast;
        }

        public void CancelSelection() {
            pendingEndpoints.Clear();
            selectedEndpointIndex = -1;
            lastDraftErrorMessage = string.Empty;
            RefreshOrderMarkers();
            RestoreAdjustedDraftTransforms();
            RestoreGuideSettings();
            RestoreSourceConnection();
            DestroyDraftChain();
            NotifyStateChanged();
        }

        public void ApplyDraft() {
            if (HasValidDraft) FinalizePendingSelection();
        }

        public void SelectPendingEndpoint(int index) {
            selectedEndpointIndex = index >= 0 && index < pendingEndpoints.Count ? index : -1;
            NotifyStateChanged();
        }

        public bool RemovePendingEndpointAt(int index) {
            if (index < 0 || index >= pendingEndpoints.Count) {
                return false;
            }

            ChainEndpoint removedEndpoint = pendingEndpoints[index];
            pendingEndpoints.RemoveAt(index);
            RestoreAdjustedDraftTransformIfUnused(removedEndpoint);

            if (selectedEndpointIndex == index) {
                selectedEndpointIndex = -1;
            }
            else if (selectedEndpointIndex > index) {
                selectedEndpointIndex--;
            }

            RefreshOrderMarkers();

            if (pendingEndpoints.Count < 2) {
                lastDraftErrorMessage = string.Empty;
                DestroyDraftChain();
                NotifyStateChanged();
                return true;
            }

            RebuildDraftChain();
            NotifyStateChanged();

            return true;
        }

        public bool RemoveGuideAt(int index) {
            if (index < 0 || index >= pendingEndpoints.Count) {
                return false;
            }

            ChainEndpoint endpoint = pendingEndpoints[index];
            if (endpoint == null || !endpoint.IsGuideEndpoint) {
                return RemovePendingEndpointAt(index);
            }

            return RemovePendingEndpointAt(index);
        }

        public void RefreshDraftPreview() {
            if (pendingEndpoints.Count >= 2) {
                if (RebuildDraftChain()) {
                    NotifyStateChanged();
                }
            }
            else {
                lastDraftErrorMessage = string.Empty;
                NotifyStateChanged();
            }
        }

        public bool TryGetHoveredGuideablePart(out GameObject partObject, out Vector3 anchorPoint) {
            partObject = null;
            anchorPoint = Vector3.zero;

            if (!ToolActive || pendingEndpoints.Count < 1 || mouseCast == null || !mouseCast.overObj || IsPointerOverUi()) {
                return false;
            }

            partObject = ResolveGuideablePart(mouseCast.gameObject);
            if (partObject == null) {
                return false;
            }

            anchorPoint = GetPartAnchorPoint(partObject);
            return true;
        }

        public bool TryAddGuideForPart(GameObject guideTarget) {
            if (!gestureActive || !ToolActive || pendingEndpoints.Count < 1 || guideTarget == null) {
                return false;
            }

            GameObject partObject = ResolveGuideablePart(guideTarget);
            if (partObject == null) {
                return false;
            }

            ChainGuide existingGuide = ChainGuideRuntimeAuthoring.GetRuntimeGuide(partObject);
            bool createdGuide = existingGuide == null;
            ChainGuide guide = existingGuide ?? ChainGuideRuntimeAuthoring.EnsureRuntimeGuide(partObject);
            if (guide == null) {
                return false;
            }

            ChainEndpoint guideEndpoint = ChainSprocketUtility.GetOrCreateEndpoint(guide.gameObject, guide.WorldCenter);
            if (guideEndpoint == null) {
                if (createdGuide) {
                    ChainGuideRuntimeAuthoring.RemoveRuntimeGuide(partObject);
                }
                return false;
            }

            int existingIndex = pendingEndpoints.IndexOf(guideEndpoint);
            if (existingIndex >= 0) {
                selectedEndpointIndex = existingIndex;
                NotifyStateChanged();
                return true;
            }

            if (createdGuide) draftGuideParts.Add(partObject);
            RememberGuideSettings(guide);
            Vector3 previousHint = guide.LocalContactHint;
            bool previousFlip = guide.FlipSide;
            int insertIndex = pendingEndpoints.Count;
            if (TryGetNearestSpan(guide.WorldCenter, out var nearest)) {
                insertIndex = nearest.afterEndpoint + 1;
                guide.SetWorldContactHint(nearest.ClosestPoint(guide.WorldCenter) - guide.WorldCenter);
                guide.SetFlipSide(false);
            }
            pendingEndpoints.Insert(insertIndex, guideEndpoint);
            selectedEndpointIndex = insertIndex;
            RefreshOrderMarkers();

            if (!RebuildDraftChain()) {
                string error = lastDraftErrorMessage;
                pendingEndpoints.RemoveAt(insertIndex);
                guide.SetLocalContactHint(previousHint); guide.SetFlipSide(previousFlip);
                if (createdGuide) {
                    ChainGuideRuntimeAuthoring.RemoveRuntimeGuide(partObject);
                    draftGuideParts.Remove(partObject);
                }
                selectedEndpointIndex = -1;
                RefreshOrderMarkers();
                RebuildDraftChain();
                ShowInteractionHint(error);
                return false;
            }
            else {
                ShowInteractionHint("Tensioner added to the nearest run. Flip contact or switch runs below.");
                NotifyStateChanged();
            }

            return true;
        }

        private void OnSelectInput() {
            if (gestureActive && IsGestureKeyHeld) TryHandleSelectInput(false);
        }

        private bool TryHandleSelectInput(bool allowHotkeyBypass) {
            if (Time.frameCount == lastSelectionFrame) {
                return false;
            }

            bool interactionEnabled = ToolActive || allowHotkeyBypass;
            if (!interactionEnabled || mouseCast == null || IsPointerOverUi() || !mouseCast.overObj) {
                return false;
            }

            if (pendingEndpoints.Count == 0) {
                ChainConnection hoveredConnection = mouseCast.gameObject.GetComponentInParent<ChainConnection>();
                if (TryBeginEditingExistingChain(hoveredConnection)) {
                    lastSelectionFrame = Time.frameCount;
                    RefreshOrderMarkers();
                    return true;
                }
            }

            ChainEndpoint hoveredEndpoint = ChainSprocketUtility.GetOrCreateEndpoint(mouseCast.gameObject, mouseCast.hit.point);
            bool remove = Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
            if (hoveredEndpoint == null) {
                if (remove) return false;
                if (TryAddGuideForPart(mouseCast.gameObject)) {
                    lastSelectionFrame = Time.frameCount;
                    return true;
                }

                return false;
            }

            return AddGestureEndpoint(hoveredEndpoint, remove);
        }

        public bool AddGestureEndpoint(ChainEndpoint endpoint, bool remove = false) {
            if (!gestureActive || endpoint == null) return false;
            lastSelectionFrame = Time.frameCount;
            if (pendingEndpoints.Count == 0 && TryBeginEditingExistingChain(endpoint)) {
                selectedEndpointIndex = pendingEndpoints.IndexOf(endpoint);
                if (remove && selectedEndpointIndex >= 0) RemovePendingEndpointAt(selectedEndpointIndex);
                return true;
            }
            int existingIndex = pendingEndpoints.IndexOf(endpoint);
            if (existingIndex >= 0) {
                selectedEndpointIndex = existingIndex;
                if (remove) return RemovePendingEndpointAt(existingIndex);
                if (browserPlacement && existingIndex == 0 && HasValidDraft) { EndGesture(true); return true; }
                var guide = endpoint.GetComponent<ChainGuide>();
                if (guide != null) {
                    ShowInteractionHint("Tensioner selected. Choose its wrap below.");
                }
                NotifyStateChanged(); return true;
            }
            if (remove) return false;
            pendingEndpoints.Add(endpoint);
            selectedEndpointIndex = pendingEndpoints.Count - 1;
            RefreshOrderMarkers();
            if (pendingEndpoints.Count == 1) { NotifyStateChanged(); return true; }
            if (!RebuildDraftChain()) {
                string error = lastDraftErrorMessage;
                pendingEndpoints.RemoveAt(pendingEndpoints.Count - 1);
                selectedEndpointIndex = pendingEndpoints.Count - 1;
                RefreshOrderMarkers(); RebuildDraftChain();
                ShowInteractionHint(error);
                return false;
            }
            NotifyStateChanged(); return true;
        }

        public void CycleSelectedGuideWrap() {
            var guide = SelectedGuide;
            if (guide == null) return;
            var next = guide.RoutingBias == ChainGuideRoutingBias.Auto ? ChainGuideRoutingBias.PushOutward
                : guide.RoutingBias == ChainGuideRoutingBias.PushOutward ? ChainGuideRoutingBias.PushInward : ChainGuideRoutingBias.Auto;
            UpdateSelectedGuide(next, false);
        }

        public void FlipSelectedGuideSide() {
            var guide = SelectedGuide;
            if (guide == null) return;
            if (!guide.HasContactHint && draftConnection != null && draftConnection.Route != null) {
                RememberGuideSettings(guide);
                guide.SetWorldContactHint(draftConnection.Route.contactDirections[selectedEndpointIndex]);
                guide.SetFlipSide(false);
            }
            UpdateSelectedGuide(guide.RoutingBias, !guide.FlipSide);
        }

        private void UpdateSelectedGuide(ChainGuideRoutingBias bias, bool flip) {
            var guide = SelectedGuide;
            if (guide == null) return;
            var oldBias = guide.RoutingBias;
            bool oldFlip = guide.FlipSide;
            RememberGuideSettings(guide);
            guide.SetRoutingBias(bias); guide.SetFlipSide(flip);
            if (!RebuildDraftChain()) {
                string error = lastDraftErrorMessage;
                guide.SetRoutingBias(oldBias); guide.SetFlipSide(oldFlip);
                RebuildDraftChain(); ShowInteractionHint(error);
            } else {
                ShowInteractionHint("Tensioner contact side flipped.");
            }
            NotifyStateChanged();
        }

        private bool TryBeginEditingExistingChain(ChainEndpoint endpoint) {
            if (!ChainManager.TryGetConnectionForEndpoint(endpoint, out ChainConnection existingConnection) || existingConnection == null) {
                return false;
            }

            return TryBeginEditingExistingChain(existingConnection);
        }

        private bool TryBeginEditingExistingChain(ChainConnection existingConnection) {
            if (existingConnection == null || existingConnection.IsPreview || !existingConnection.gameObject.activeInHierarchy) {
                return false;
            }

            sourceConnection = existingConnection;
            selectedStandard = existingConnection.Settings.standard;
            selectedSizeMode = existingConnection.Settings.autoResolveStandard ? ChainSizeMode.Auto
                : selectedStandard == ChainStandard.Pitch3p75 ? ChainSizeMode.Pitch3p75
                : selectedStandard == ChainStandard.Pitch9p79 ? ChainSizeMode.Pitch9p79 : ChainSizeMode.Pitch6p35;
            slack = existingConnection.Settings.slack;
            pendingEndpoints.Clear();
            selectedEndpointIndex = -1;

            for (int i = 0; i < existingConnection.Endpoints.Count; i++) {
                pendingEndpoints.Add(existingConnection.Endpoints[i]);
            }


            sourceConnection.gameObject.SetActive(false);
            if (RebuildDraftChain()) {
                RefreshOrderMarkers();
                NotifyStateChanged();
                return true;
            }

            sourceConnection.gameObject.SetActive(true);
            RefreshOrderMarkers();
            ShowInteractionHint(lastDraftErrorMessage);
            NotifyStateChanged();
            return true;
        }

        private void FinalizePendingSelection() {
            FinalizePendingSelection(false);
        }

        private void FinalizePendingSelection(bool keepEditingAfterApply) {
            if (pendingEndpoints.Count < 2) {
                CancelSelection();
                return;
            }

            if (!string.IsNullOrEmpty(CurrentValidationMessage)) return;

            if (PendingSelectionMatchesSourceConnection()) {
                if (!keepEditingAfterApply) {
                    CancelSelection();
                }
                else {
                    NotifyStateChanged();
                }
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

            ChainConnection previousConnection = sourceConnection;

            if (previousConnection != null) {
                CommitReplaceState(previousConnection, finalizedConnection);
                new ObjectElement(previousConnection.gameObject).ApplyExistence(false);
            }
            else {
                CommitCreateState(finalizedConnection);
            }

            adjustedDraftTransforms.Clear();
            originalGuideSettings.Clear();
            foreach (var part in draftGuideParts) {
                var guide = part != null ? ChainGuideRuntimeAuthoring.GetRuntimeGuide(part) : null;
                if (guide != null && !pendingEndpoints.Any(e => e != null && e.GetComponent<ChainGuide>() == guide)) ChainGuideRuntimeAuthoring.RemoveRuntimeGuide(part);
            }
            draftGuideParts.Clear();
            lastDraftErrorMessage = string.Empty;

            if (keepEditingAfterApply) {
                sourceConnection = finalizedConnection;
                finalizedConnection.gameObject.SetActive(true);
                selectedEndpointIndex = Mathf.Clamp(selectedEndpointIndex, -1, pendingEndpoints.Count - 1);
                RefreshOrderMarkers();
                NotifyStateChanged();
                return;
            }

            sourceConnection = null;
            pendingEndpoints.Clear();
            selectedEndpointIndex = -1;
            RefreshOrderMarkers();
            NotifyStateChanged();
        }

        private ChainSettings BuildCurrentSettings() {
            bool autoResolveStandard = selectedSizeMode == ChainSizeMode.Auto;
            ChainStandard requestedStandard = autoResolveStandard
                ? ChainSprocketUtility.ResolveAutoStandard(pendingEndpoints, ChainStandard.Pitch6p35)
                : selectedStandard;

            ChainSettings settings = ChainSettings.CreateDefault(requestedStandard, autoResolveStandard);
            settings.slack = slack;
            settings.autoAlignSecondEndpoint = autoAlignSecondEndpoint && pendingEndpoints.Count == 2 && !pendingEndpoints[1].IsGuideEndpoint;
            settings.autoAlignMaxDistance = autoAlignMaxDistance;
            settings.singleChainPerEndpoint = sourceConnection == null && singleChainPerEndpoint;
            return settings;
        }

        private bool RebuildDraftChain() {
            if (pendingEndpoints.Count < 2) {
                lastDraftErrorMessage = string.Empty;
                DestroyDraftChain();
                NotifyStateChanged();
                return true;
            }

            ChainConnection previousDraft = draftConnection;
            if (previousDraft != null) {
                ChainManager.Unregister(previousDraft);
                previousDraft.gameObject.SetActive(false);
            }

            bool hidSourceConnection = false;
            if (sourceConnection != null && previousDraft == null) {
                sourceConnection.gameObject.SetActive(false);
                hidSourceConnection = true;
            }

            int newlyAddedIndex = previousDraft != null
                ? pendingEndpoints.FindIndex(endpoint => !previousDraft.Endpoints.Contains(endpoint))
                : pendingEndpoints.Count - 1;
            bool shouldSnapNewEndpointToPlane = autoAlignSecondEndpoint && pendingEndpoints.Count > 2
                && newlyAddedIndex >= 0
                && previousDraft != null
                && previousDraft.Endpoints.Count == pendingEndpoints.Count - 1;

            Transform snappedBindingTransform = null;
            Vector3 snappedBindingStartPosition = Vector3.zero;
            Quaternion snappedBindingStartRotation = Quaternion.identity;
            bool snappedBindingHadExistingSnapshot = false;

            if (shouldSnapNewEndpointToPlane) {
                snappedBindingTransform = ChainManager.GetBindingTransformForEndpoint(pendingEndpoints[newlyAddedIndex]);
                if (snappedBindingTransform != null) {
                    snappedBindingStartPosition = snappedBindingTransform.position;
                    snappedBindingStartRotation = snappedBindingTransform.rotation;
                    snappedBindingHadExistingSnapshot = adjustedDraftTransforms.ContainsKey(snappedBindingTransform);

                    if (ChainManager.SnapEndpointsToPlane(pendingEndpoints, new[] { newlyAddedIndex })) {
                        RememberAdjustedDraftTransform(
                            snappedBindingTransform,
                            snappedBindingStartPosition,
                            snappedBindingStartRotation);
                    }
                    else {
                        snappedBindingTransform = null;
                    }
                }
            }

            Transform secondTarget = autoAlignSecondEndpoint && pendingEndpoints.Count == 2 && !pendingEndpoints[1].IsGuideEndpoint
                ? ChainManager.GetBindingTransformForEndpoint(pendingEndpoints[1]) : null;
            Vector3 secondPosition = secondTarget != null ? secondTarget.position : Vector3.zero;
            Quaternion secondRotation = secondTarget != null ? secondTarget.rotation : Quaternion.identity;
            bool created = ChainManager.TryCreateBoundChain(
                pendingEndpoints,
                BuildCurrentSettings(),
                out ChainConnection rebuiltConnection,
                out string errorMessage, previousDraft, true);

            if (!created || rebuiltConnection == null) {
                lastDraftErrorMessage = errorMessage;
                if (snappedBindingTransform != null) {
                    snappedBindingTransform.SetPositionAndRotation(snappedBindingStartPosition, snappedBindingStartRotation);
                    if (!snappedBindingHadExistingSnapshot) {
                        adjustedDraftTransforms.Remove(snappedBindingTransform);
                    }
                }

                if (previousDraft != null) {
                    previousDraft.gameObject.SetActive(true);
                    ChainManager.Register(previousDraft);
                    draftConnection = previousDraft;
                }
                else if (hidSourceConnection && sourceConnection != null) {
                    sourceConnection.gameObject.SetActive(true);
                }

                NotifyStateChanged();
                return false;
            }

            lastDraftErrorMessage = string.Empty;
            if (secondTarget != null && (secondTarget.position != secondPosition || secondTarget.rotation != secondRotation))
                RememberAdjustedDraftTransform(secondTarget, secondPosition, secondRotation);
            rebuiltConnection.SetPreviewMode(true);
            draftConnection = rebuiltConnection;

            if (previousDraft != null && previousDraft != rebuiltConnection) {
                Destroy(previousDraft.gameObject);
            }

            NotifyStateChanged();
            return true;
        }

        private void DestroyDraftChain() {
            if (draftConnection == null) {
                return;
            }

            ChainManager.Unregister(draftConnection);
            draftConnection.gameObject.SetActive(false);
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

        private void RememberAdjustedDraftTransform(Transform target, Vector3 originalPosition, Quaternion originalRotation) {
            if (target == null || adjustedDraftTransforms.ContainsKey(target)) {
                return;
            }

            adjustedDraftTransforms[target] = new TransformSnapshot(originalPosition, originalRotation);
        }

        private void RestoreAdjustedDraftTransforms() {
            foreach (KeyValuePair<Transform, TransformSnapshot> entry in adjustedDraftTransforms) {
                Transform target = entry.Key;
                if (target == null) {
                    continue;
                }

                target.SetPositionAndRotation(entry.Value.position, entry.Value.rotation);
            }

            adjustedDraftTransforms.Clear();
        }

        private void RestoreAdjustedDraftTransformIfUnused(ChainEndpoint endpoint) {
            Transform target = ChainManager.GetBindingTransformForEndpoint(endpoint);
            if (target == null || !adjustedDraftTransforms.TryGetValue(target, out TransformSnapshot snapshot)) {
                return;
            }

            for (int i = 0; i < pendingEndpoints.Count; i++) {
                if (ChainManager.GetBindingTransformForEndpoint(pendingEndpoints[i]) == target) {
                    return;
                }
            }

            target.SetPositionAndRotation(snapshot.position, snapshot.rotation);
            adjustedDraftTransforms.Remove(target);
        }

        private void CommitCreateState(ChainConnection connection) {
            if (StateSystem.instance == null || StateSystem.states == null || StateSystem.states.Count == 0 || connection == null) {
                return;
            }

            var previousElements = new List<IElement>();
            var nextElements = new List<IElement>();

            AppendAdjustedTransformStateChanges(previousElements, nextElements);

            ObjectElement previousElement = new ObjectElement(connection.gameObject);
            previousElement.existing = false;
            previousElements.Add(previousElement);
            nextElements.Add(new ObjectElement(connection.gameObject));

            StateSystem.AddElements(previousElements);
            StateSystem.AddState(new State(nextElements));
        }

        private void CommitReplaceState(ChainConnection oldConnection, ChainConnection newConnection) {
            if (StateSystem.instance == null
                || StateSystem.states == null
                || StateSystem.states.Count == 0
                || oldConnection == null
                || newConnection == null) {
                return;
            }

            var previousElements = new List<IElement>();
            var nextElements = new List<IElement>();

            AppendAdjustedTransformStateChanges(previousElements, nextElements);

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

        private void AppendAdjustedTransformStateChanges(List<IElement> previousElements, List<IElement> nextElements) {
            foreach (var part in draftGuideParts) {
                var guide = part != null ? ChainGuideRuntimeAuthoring.GetRuntimeGuide(part) : null;
                if (guide == null || !pendingEndpoints.Any(e => e != null && e.GetComponent<ChainGuide>() == guide)) continue;
                previousElements.Add(new GuideExistenceElement(guide, false));
                nextElements.Add(new GuideExistenceElement(guide, true));
            }
            foreach (var entry in originalGuideSettings) if (entry.Key != null) {
                previousElements.Add(new GuideSettingsElement(entry.Key, entry.Value.bias, entry.Value.flip, entry.Value.hint));
                nextElements.Add(new GuideSettingsElement(entry.Key, entry.Key.RoutingBias, entry.Key.FlipSide, entry.Key.LocalContactHint));
            }
            foreach (KeyValuePair<Transform, TransformSnapshot> entry in adjustedDraftTransforms) {
                Transform target = entry.Key;
                if (target == null) {
                    continue;
                }

                previousElements.Add(new ChainTransformElement(target, entry.Value.position, entry.Value.rotation));
                nextElements.Add(new ChainTransformElement(target, target.position, target.rotation));
            }
        }

        private void HandleToolDeactivated() {
            CancelSelection();
        }

        public bool TryGetNearestSpan(Vector3 point, out ChainPathSolver.Span nearest) {
            nearest = default;
            if (!HasValidDraft || draftConnection.Route == null) return false;
            float best = float.MaxValue;
            foreach (var span in draftConnection.Route.spans) {
                float distance = (span.ClosestPoint(point) - point).sqrMagnitude;
                if (distance < best) { best = distance; nearest = span; }
            }
            return best < float.MaxValue;
        }

        public void MoveSelectedGuideToOtherRun() {
            var guide = SelectedGuide;
            if (guide == null || pendingEndpoints.Count < 3) return;
            RememberGuideSettings(guide);
            int oldIndex = selectedEndpointIndex;
            var endpoint = pendingEndpoints[oldIndex];
            var oldHint = guide.LocalContactHint;
            bool oldFlip = guide.FlipSide;
            var remaining = new List<ChainEndpoint>(pendingEndpoints);
            remaining.RemoveAt(oldIndex);
            var settings = BuildCurrentSettings();
            if (!ChainPathSolver.TrySolve(remaining, settings.standard,
                ChainSprocketUtility.ResolvePitch(remaining, settings.standard), settings.slack, out var bare, out var error)) {
                ShowInteractionHint(error); return;
            }
            int previousEdge = (oldIndex + remaining.Count - 1) % remaining.Count;
            var alternatives = new List<ChainPathSolver.Span>(bare.spans);
            alternatives.Sort((a, b) => (a.ClosestPoint(guide.WorldCenter) - guide.WorldCenter).sqrMagnitude.CompareTo((b.ClosestPoint(guide.WorldCenter) - guide.WorldCenter).sqrMagnitude));
            foreach (var span in alternatives) {
                if (span.afterEndpoint == previousEdge) continue;
                pendingEndpoints.Clear(); pendingEndpoints.AddRange(remaining);
                selectedEndpointIndex = span.afterEndpoint + 1;
                pendingEndpoints.Insert(selectedEndpointIndex, endpoint);
                guide.SetWorldContactHint(span.ClosestPoint(guide.WorldCenter) - guide.WorldCenter); guide.SetFlipSide(false);
                if (RebuildDraftChain()) { RefreshOrderMarkers(); ShowInteractionHint("Tensioner moved to another run."); return; }
            }
            pendingEndpoints.Clear(); pendingEndpoints.AddRange(remaining); pendingEndpoints.Insert(oldIndex, endpoint);
            selectedEndpointIndex = oldIndex; guide.SetLocalContactHint(oldHint); guide.SetFlipSide(oldFlip);
            RebuildDraftChain(); RefreshOrderMarkers(); ShowInteractionHint("No clear route to another run here. Move the tensioner first.");
        }

        private bool PendingSelectionMatchesSourceConnection() {
            if (sourceConnection == null || pendingEndpoints.Count != sourceConnection.Endpoints.Count) {
                return false;
            }

            if (draftConnection != null && draftConnection != sourceConnection) {
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

        private GameObject ResolveGuideablePart(GameObject obj) {
            GameObject partObject = ChainSprocketUtility.ResolvePartObject(obj);
            if (partObject == null || partObject.GetComponent<SavedObject>() == null) {
                return null;
            }

            if (ChainSprocketUtility.IsSprocket(partObject)) {
                return null;
            }

            ChainGuide existingGuide = ChainGuideRuntimeAuthoring.GetRuntimeGuide(partObject);
            if (existingGuide != null) {
                ChainEndpoint existingEndpoint = ChainSprocketUtility.GetOrCreateEndpoint(existingGuide.gameObject, existingGuide.WorldCenter);
                if (existingEndpoint != null && pendingEndpoints.Contains(existingEndpoint)) {
                    return null;
                }
            }

            return partObject;
        }

        private static Vector3 GetPartAnchorPoint(GameObject partObject) {
            if (partObject == null) {
                return Vector3.zero;
            }

            if (partObject.TryGetComponent(out PartData partData) && partData.primaryHole != null) {
                return partData.primaryHole.transform.position;
            }

            Renderer[] renderers = partObject.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0) {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) {
                    if (renderers[i] != null) {
                        bounds.Encapsulate(renderers[i].bounds);
                    }
                }

                return bounds.center;
            }

            return partObject.transform.position;
        }

        private void NotifyStateChanged() {
            StateChanged?.Invoke();
        }
    }
}
