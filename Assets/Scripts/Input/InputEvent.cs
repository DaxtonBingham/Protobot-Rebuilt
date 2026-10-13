using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System;

namespace Protobot.InputEvents {
    public class InputEvent : MonoBehaviour {
        public InputAction defaultAction;
        public RebindAction rebindAction;

        public Action performed;
        public Action canceled;
        public bool IsPressed = false;

        private bool prevPressStatus = false;
        private bool missingDefaultActionLogged = false;

        public void Awake() {
            //performed += () => Debug.Log("Performed " + name + " input event!");

            rebindAction = new RebindAction(name);

            rebindAction.OnCompleteRebind += () => {
                if (defaultAction != null) {
                    defaultAction.Disable();
                }
            };
            rebindAction.OnSaveRebinds += () => {
                if (defaultAction != null) {
                    defaultAction.Disable();
                }
            };

            rebindAction.OnResetRebinds += () => {
                if (defaultAction != null) {
                    defaultAction.Enable();
                }
            };

            rebindAction.OnLoadRebinds += hasRebinds => {
                if (hasRebinds && defaultAction != null) {
                    defaultAction.Disable();
                }
            };

            if (rebindAction.IsEmpty && defaultAction != null) {
                defaultAction.Enable();
            }
        }

        public void Update() {
            if (RebindAction.Rebinding) return;

            if (defaultAction == null && !missingDefaultActionLogged) {
                Debug.LogWarning($"InputEvent '{name}' has no default action assigned.", this);
                missingDefaultActionLogged = true;
            }

            bool defaultPressed = defaultAction != null && defaultAction.AllControlsPressed();
            bool reboundPressed = rebindAction != null && rebindAction.action != null && rebindAction.action.AllControlsPressed();

            IsPressed = defaultPressed || reboundPressed;

            if (IsPressed != prevPressStatus) {
                if (IsPressed)
                    performed?.Invoke();
                else
                    canceled?.Invoke();
            }

            prevPressStatus = IsPressed;
        }

        public void OnDisable() {
            if (defaultAction != null) {
                defaultAction.Disable();
            }
        }
        
        public string GetCurrentKeybind()
        {
            return defaultAction != null ? defaultAction.GetBindingDisplayString() : string.Empty;
        }
        
        public bool IsKeyPressed(string keyName)
        {
            var key = Keyboard.current.FindKeyOnCurrentKeyboardLayout(keyName);
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
