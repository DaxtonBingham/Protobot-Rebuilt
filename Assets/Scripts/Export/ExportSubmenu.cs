using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Export {
    // Follow the row's lifetime while drawing outside the parent clipping mask.
    // Keyboard polling only runs while the flyout is open.
    public sealed class ExportSubmenu : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IMoveHandler {
        RectTransform popup;
        Button trigger, firstOption;
        Coroutine keyboard;

        public void Initialize(RectTransform popup, Button firstOption) {
            this.popup = popup; this.firstOption = firstOption;
            trigger = GetComponent<Button>();
            popup.gameObject.SetActive(false);
        }

        public void Show(bool focus = false) {
            if (popup == null || !trigger.IsInteractable()) return;
            var row = (RectTransform)transform;
            popup.position = row.TransformPoint(new Vector3(row.rect.xMax - 2, row.rect.yMax, 0));
            popup.SetAsLastSibling();
            popup.gameObject.SetActive(true);
            if (keyboard == null) keyboard = StartCoroutine(WatchKeyboard());
            if (focus) firstOption.Select();
        }

        public void Close(bool focus = false) {
            if (keyboard != null) { StopCoroutine(keyboard); keyboard = null; }
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            bool selectedOption = selected != null && popup != null && selected.transform.IsChildOf(popup);
            if (popup != null) popup.gameObject.SetActive(false);
            if (focus && isActiveAndEnabled) trigger.Select();
            else if (selectedOption) EventSystem.current.SetSelectedGameObject(null);
        }

        public void OnPointerEnter(PointerEventData data) { Show(); }
        public void OnPointerExit(PointerEventData data) {
            // The row and flyout share a two-pixel edge to avoid a hover gap.
            if (popup != null && popup.gameObject.activeSelf &&
                (RectTransformUtility.RectangleContainsScreenPoint(popup, data.position, data.enterEventCamera)
                || RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, data.position, data.enterEventCamera))) return;
            Close();
        }
        public void OnMove(AxisEventData data) {
            if (data.moveDir == MoveDirection.Right) { Show(true); data.Use(); }
        }
        void OnDisable() { Close(); }

        IEnumerator WatchKeyboard() {
            while (true) {
                yield return null;
                var keys = Keyboard.current;
                if (keys != null && (keys.escapeKey.wasPressedThisFrame || keys.leftArrowKey.wasPressedThisFrame)) {
                    keyboard = null; Close(true); yield break;
                }
            }
        }
    }
}
