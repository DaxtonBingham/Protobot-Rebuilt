using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.UI {
    [RequireComponent(typeof(Toggle))]
    public class MenuBarToggle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
        private Toggle toggle;
        [SerializeField] private GameObject menu;
        public GameObject Menu => menu;
        public RectTransform Flyout { get; set; }

        public bool isMouseOver = false;
        private bool LeftMousePressed => Mouse.current.leftButton.wasReleasedThisFrame;
        private bool AnyMenuTogglesOn => toggle.group.AnyTogglesOn();
        
        private void Start() {
            toggle = GetComponent<Toggle>();

            toggle.onValueChanged.AddListener(value => {
                menu.SetActive(value);
            });
        }

        private void Update() {
            if (toggle.isOn && !isMouseOver && LeftMousePressed && !PointerInsideMenu())
                toggle.group.SetAllTogglesOff();
            }

        private bool PointerInsideMenu() {
            var canvas = menu.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var point = Mouse.current.position.ReadValue();
            return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)menu.transform, point, camera)
                || (Flyout != null && Flyout.gameObject.activeInHierarchy
                    && RectTransformUtility.RectangleContainsScreenPoint(Flyout, point, camera));
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (AnyMenuTogglesOn && !isMouseOver)
                toggle.isOn = true;

            isMouseOver = true;
        }

        public void OnPointerExit(PointerEventData eventData) {
            isMouseOver = false;
        }
    }
}
