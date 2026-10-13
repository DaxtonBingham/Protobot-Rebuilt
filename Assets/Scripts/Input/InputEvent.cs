using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using System;
using UnityEngine.UI;
using TMPro;

namespace Protobot.InputEvents {
    public class InputEvent : MonoBehaviour, IInputStateChangeMonitor {
        public InputAction defaultAction;
        public RebindAction rebindAction;

        public Action performed;
        public Action canceled;
        public bool IsPressed = false;

        private readonly List<InputControl> monitoredControls = new List<InputControl>();
        private readonly Queue<bool> transitions = new Queue<bool>();
        private bool observedPressed;
        private bool refreshingMonitors;
        private bool missingDefaultActionLogged = false;
        private static GameObject focusedObject;
        private static InputField focusedInput;
        private static TMP_InputField focusedTmpInput;

        public void Awake() {
            //performed += () => Debug.Log("Performed " + name + " input event!");

            rebindAction = new RebindAction(name);

            rebindAction.OnCompleteRebind += () => {
                if (defaultAction != null) {
                    defaultAction.Disable();
                }
                RefreshMonitors();
            };
            rebindAction.OnSaveRebinds += () => {
                if (defaultAction != null) {
                    defaultAction.Disable();
                }
                RefreshMonitors();
            };

            rebindAction.OnResetRebinds += () => {
                if (defaultAction != null) {
                    defaultAction.Enable();
                }
                RefreshMonitors();
            };

            rebindAction.OnLoadRebinds += hasRebinds => {
                if (hasRebinds && defaultAction != null) {
                    defaultAction.Disable();
                }
                RefreshMonitors();
            };

            if (rebindAction.IsEmpty && defaultAction != null) {
                defaultAction.Enable();
            }
        }

        public void Update() {
            if (RebindAction.Rebinding) return;
            if (SuppressWhileTyping()) return;

            if (defaultAction == null && !missingDefaultActionLogged) {
                Debug.LogWarning($"InputEvent '{name}' has no default action assigned.", this);
                missingDefaultActionLogged = true;
            }

            ObserveControls();
            // Deliver on Update as before, but retain every transition between
            // frames. A fast press/release must still perform exactly once.
            while (isActiveAndEnabled && transitions.Count > 0) {
                IsPressed = transitions.Dequeue();
                if (IsPressed) performed?.Invoke(); else canceled?.Invoke();
            }
        }

        public void OnEnable() {
            if (rebindAction == null) return;
            if (rebindAction.IsEmpty) defaultAction?.Enable();
            else { defaultAction?.Disable(); rebindAction.action?.Enable(); }
            InputSystem.onActionChange += ActionChanged;
            RefreshMonitors();
        }

        public void OnDisable() {
            InputSystem.onActionChange -= ActionChanged;
            RemoveMonitors();
            defaultAction?.Disable();
            rebindAction?.action?.Disable();
            transitions.Clear();
            observedPressed = false;
            bool wasPressed = IsPressed;
            IsPressed = false;
            if (wasPressed) canceled?.Invoke();
        }

        private void OnDestroy() {
            rebindAction?.CancelRebind();
            defaultAction?.Dispose();
            rebindAction?.action?.Dispose();
        }

        private void ActionChanged(object changed, InputActionChange change) {
            if (change == InputActionChange.BoundControlsChanged
                && (ReferenceEquals(changed, defaultAction) || ReferenceEquals(changed, rebindAction?.action))) RefreshMonitors();
        }

        private void RefreshMonitors() {
            if (!isActiveAndEnabled || refreshingMonitors) return;
            refreshingMonitors = true;
            try {
                RemoveMonitors();
                Monitor(defaultAction);
                Monitor(rebindAction?.action);
            } finally { refreshingMonitors = false; }
        }

        private void Monitor(InputAction action) {
            if (action == null || !action.enabled) return;
            foreach (var control in action.controls) {
                if (!control.device.added || monitoredControls.Contains(control)) continue;
                monitoredControls.Add(control);
                InputState.AddChangeMonitor(control, this);
            }
        }

        private void RemoveMonitors() {
            foreach (var control in monitoredControls)
                if (control.device.added) InputState.RemoveChangeMonitor(control, this);
            monitoredControls.Clear();
        }

        private static bool ChordPressed(InputAction action) {
            if (action == null || !action.enabled || action.controls.Count == 0) return false;
            foreach (var control in action.controls) if (!control.IsPressed()) return false;
            return true;
        }

        private void ObserveControls() {
            if (!isActiveAndEnabled || RebindAction.Rebinding) return;
            if (SuppressWhileTyping()) return;
            bool pressed = ChordPressed(defaultAction) || ChordPressed(rebindAction?.action);
            if (pressed == observedPressed) return;
            observedPressed = pressed;
            transitions.Enqueue(pressed);
        }

        private bool SuppressWhileTyping() {
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected == null) return false;
            if (focusedObject != selected) {
                focusedObject = selected;
                focusedInput = selected.GetComponent<InputField>();
                focusedTmpInput = selected.GetComponent<TMP_InputField>();
            }
            bool editing = (focusedInput != null && focusedInput.isFocused)
                || (focusedTmpInput != null && focusedTmpInput.isFocused);
            if (!editing || (!UsesKeyboard(defaultAction) && !UsesKeyboard(rebindAction?.action))) return false;
            transitions.Clear();
            observedPressed = ChordPressed(defaultAction) || ChordPressed(rebindAction?.action);
            bool wasPressed = IsPressed;
            IsPressed = false;
            if (wasPressed) canceled?.Invoke();
            return true;
        }

        private static bool UsesKeyboard(InputAction action) {
            if (action == null || !action.enabled) return false;
            foreach (var control in action.controls) if (control.device is Keyboard) return true;
            return false;
        }

        public void NotifyControlStateChanged(InputControl control, double time, InputEventPtr eventPtr, long monitorIndex) {
            // Input System invokes monitors after applying each individual event,
            // so modifiers are evaluated at key-down rather than at frame end.
            ObserveControls();
        }

        public void NotifyTimerExpired(InputControl control, double time, long monitorIndex, int timerIndex) { }
        
        public string GetCurrentKeybind()
        {
            return defaultAction != null ? defaultAction.GetBindingDisplayString() : string.Empty;
        }
        
        public bool IsKeyPressed(string keyName)
        {
            var key = Keyboard.current?.FindKeyOnCurrentKeyboardLayout(keyName);
            if (key == null) print("Key not found: " + keyName);
            return key != null && key.isPressed;
        }
        //i have no idea what im doing
        public void Rebind(string newBinding)
        {
            if (defaultAction != null)
            {
                defaultAction.ApplyBindingOverride(newBinding);
                Debug.Log($"Rebound to {newBinding}");
            }
            else
            {
                Debug.LogError("inputAction is null! Cannot rebind.");
            }
        }
    }
}
