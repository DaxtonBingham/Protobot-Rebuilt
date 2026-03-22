using System.Collections;
using System.Collections.Generic;
using Protobot.ChainSystem;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.UI {
    public class AddPropertiesUI : MonoBehaviour {
        [SerializeField] private Placement placement;

        [SerializeField] private CanvasGroup upperAddMenu;
        [SerializeField] private Text titleText;

        [SerializeField] private float singleParamSize = 335;
        [SerializeField] private float doubleParamSize = 515;

        [SerializeField] private ParamDisplay param1Display;
        [SerializeField] private ParamDisplay param2Display;
        [SerializeField] private InsertChainTool chainTool;

        private PartType partType;
        private PartGenerator generator;

        void Start() {
            PartDisplayUI.OnChangeSelected += UpdateDisplay;
            upperAddMenu.alpha = 0;
        }

        public void UpdateDisplay(PartDisplayUI partDisplayUI) {
            partType = partDisplayUI.partType;
            generator = partType != null ? partType.gameObject.GetComponent<PartGenerator>() : null;

            if (generator == null) {
                upperAddMenu.alpha = 0;
                StopPlacementIfNeeded();
                return;
            }

            if (generator.UsesParams) {
                titleText.text = "Add " + partType.name;
                UpdateDisplayedDropdowns(generator.UsesTwoParams);
                upperAddMenu.alpha = 1;
            }
            else {
                upperAddMenu.alpha = 0;
            }

            if (IsChainToolPart) {
                ApplyChainSettingsFromParam();
                StopPlacementIfNeeded();
                return;
            }

            UpdatePlacement();
        }

        public void UpdateDisplayedDropdowns(bool usesTwoParams) {
            RectTransform rectTransform = upperAddMenu.GetComponent<RectTransform>();
            float sizeY = doubleParamSize;

            param1Display.gameObject.SetActive(true);
            param2Display.gameObject.SetActive(true);
            SetDropdown1();

            if (!usesTwoParams) {
                sizeY = singleParamSize;
                param2Display.gameObject.SetActive(false);
            }

            rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, sizeY);
        }

        public void SetDropdown1() {
            param1Display.SetDisplay(generator.param1, generator.GetParam1Options());

            if (generator.UsesTwoParams)
                SetDropdown2();
        }

        public void SetDropdown2() {
            param2Display.SetDisplay(generator.param2, generator.GetParam2Options());
        }

        public void UpdateParam1(string value) {
            generator.param1.value = value;

            if (IsChainToolPart) {
                ApplyChainSettingsFromParam();
                StopPlacementIfNeeded();
                return;
            }

            SetDropdown2();
            UpdatePlacement();
        }

        public void UpdateParam2(string value) {
            generator.param2.value = value;

            if (IsChainToolPart) {
                ApplyChainSettingsFromParam();
                StopPlacementIfNeeded();
                return;
            }

            UpdatePlacement();
        }

        private void UpdatePlacement() {
            if (IsChainToolPart || generator == null || partType == null) {
                return;
            }

            var placementData = new PartPlacementData(generator, partType, placement.transform);
            placement.StartPlacing(placementData);
        }

        private bool IsChainToolPart => partType != null && partType.id == PartsManager.ChainToolPartId;

        private InsertChainTool ResolveChainTool() {
            if (chainTool == null) {
                chainTool = FindObjectOfType<InsertChainTool>();
            }

            return chainTool;
        }

        private void ApplyChainSettingsFromParam() {
            InsertChainTool resolvedTool = ResolveChainTool();
            if (resolvedTool == null || generator == null) {
                return;
            }

            string selectedValue = generator.param1.value == null ? string.Empty : generator.param1.value.ToUpperInvariant();
            if (selectedValue.Contains("0.148") || selectedValue.Contains("3.75") || selectedValue.Contains("3P75")) {
                resolvedTool.SetPitch3p75();
                return;
            }

            if (selectedValue.Contains("0.250") || selectedValue.Contains("6.35") || selectedValue.Contains("6P35") || selectedValue.Contains("25")) {
                resolvedTool.SetPitch6p35();
                return;
            }

            if (selectedValue.Contains("0.385") || selectedValue.Contains("9.79") || selectedValue.Contains("9P79") || selectedValue == "#35") {
                resolvedTool.SetPitch9p79();
                return;
            }

            resolvedTool.SetPitch6p35();
        }

        private void StopPlacementIfNeeded() {
            if (placement != null && placement.placing) {
                placement.StopPlacing();
            }
        }
    }
}
