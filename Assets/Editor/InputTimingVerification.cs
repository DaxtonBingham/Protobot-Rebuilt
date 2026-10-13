using System;
using Protobot.InputEvents;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class InputTimingVerification {
    [MenuItem("Tools/Verify Shortcut Timing")]
    public static void Verify() {
        if (Application.isPlaying) throw new InvalidOperationException("Run shortcut verification outside Play Mode.");
        string name = "Protobot shortcut QA " + Guid.NewGuid().ToString("N");
        var keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.SetDeviceUsage(keyboard, "ShortcutQA");
        var go = new GameObject(name);
        go.SetActive(false);
        var input = go.AddComponent<Protobot.InputEvents.InputEvent>();
        input.defaultAction = new InputAction();
        input.defaultAction.AddBinding("<Keyboard>{ShortcutQA}/ctrl");
        input.defaultAction.AddBinding("<Keyboard>{ShortcutQA}/f12");
        int presses = 0, releases = 0, checks = 0;
        input.performed += () => presses++;
        input.canceled += () => releases++;
        void Require(bool value, string message) {
            if (!value) throw new Exception("Shortcut timing: " + message);
            checks++;
        }
        void Send(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        void Tick() { InputSystem.Update(); input.Update(); }
        try {
            // Runtime lifecycle calls are explicit because this is an editor-only check.
            input.Awake(); go.SetActive(true); input.OnEnable();
            Send(Key.LeftCtrl); Send(Key.LeftCtrl, Key.F12); Send(); Tick();
            Require(presses == 1 && releases == 1 && !input.IsPressed, "complete left-Ctrl chord between frames");
            Send(Key.RightCtrl); Send(Key.RightCtrl, Key.F12); Send(); Tick();
            Require(presses == 2 && releases == 2, "synthetic Ctrl accepts the right-hand modifier");
            Send(Key.F12); Tick(); Send(); Tick();
            Require(presses == 2, "modifier required");
            Send(Key.LeftCtrl, Key.F12); Tick(); Tick();
            Require(presses == 3 && input.IsPressed, "holding does not repeat performed");
            input.OnDisable();
            Require(!input.IsPressed && releases == 3, "disable releases held action");
            Send(); InputSystem.Update(); input.OnEnable();
            Send(Key.LeftCtrl, Key.F12); Tick();
            Require(input.IsPressed && presses == 4, "reenable restores bindings");
            Send(); Tick();
            Require(releases == 4, "release after reenable");
            input.defaultAction.ApplyBindingOverride(1, "<Keyboard>{ShortcutQA}/f11");
            Send(Key.LeftCtrl, Key.F12); Send(); Tick();
            Require(presses == 4, "old binding removed");
            Send(Key.LeftCtrl, Key.F11); Send(); Tick();
            Require(presses == 5 && releases == 5, "changed binding tracked immediately");
            input.rebindAction.action.ApplyBindingOverride(3, "<Keyboard>{ShortcutQA}/f10");
            input.rebindAction.SaveRebinds();
            Send(Key.LeftCtrl, Key.F11); Send(); Tick();
            Require(presses == 5, "saved rebind disables the default chord");
            Send(Key.F10); Send(); Tick();
            Require(presses == 6 && releases == 6, "fast rebound shortcut");
            input.rebindAction.ResetRebinds();
            Send(Key.LeftCtrl, Key.F11); Send(); Tick();
            Require(presses == 7 && releases == 7, "reset restores the default chord");
            Debug.Log("INPUT_TIMING_VERIFICATION passed checks=" + checks);
        } finally {
            input.OnDisable();
            UnityEngine.Object.DestroyImmediate(go);
            InputSystem.RemoveDevice(keyboard);
            PlayerPrefs.DeleteKey(name);
        }
    }
}
