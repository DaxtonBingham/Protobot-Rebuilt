using System.Collections.Generic;
using System.Linq;
using Protobot.Tools;
using Protobot.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.ChainSystem {
    public static class ChainRuntimeBootstrap {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init() {
            InsertChainTool existingTool = Object.FindObjectOfType<InsertChainTool>();
            if (existingTool != null) {
                MouseCast existingMouseCast = Object.FindObjectOfType<MouseCast>();
                if (existingMouseCast != null) {
                    existingTool.SetMouseCast(existingMouseCast);
                }

                ChainToolbarUI.Ensure(existingTool);
                return;
            }

            MouseCast mouseCast = Object.FindObjectOfType<MouseCast>();
            if (mouseCast == null) {
                Camera mainCamera = Camera.main;
                if (mainCamera != null) {
                    mouseCast = mainCamera.GetComponent<MouseCast>();
                    if (mouseCast == null) {
                        mouseCast = mainCamera.gameObject.AddComponent<MouseCast>();
                    }
                }
            }

            if (mouseCast == null) {
                return;
            }

            GameObject hostObject = ((Component)mouseCast).gameObject;
            InsertChainTool tool = hostObject.AddComponent<InsertChainTool>();
            tool.SetMouseCast(mouseCast);
            ChainToolbarUI.Ensure(tool);
        }
    }

    public class ChainToolbarUI : MonoBehaviour {
        private static readonly List<string> SizeOptions = new List<string> {
            "Auto",
            "#25 / 0.250\"",
            "#35 / 0.385\""
        };

        private InsertChainTool chainTool;
        private ToolToggle toolToggle;
        private Toggle buttonToggle;
        private GameObject optionsPanel;
        private Dropdown sizeDropdown;

        public static void Ensure(InsertChainTool chainTool) {
            if (chainTool == null) {
                return;
            }

            ChainToolbarUI existing = Object.FindObjectOfType<ChainToolbarUI>();
            if (existing != null) {
                existing.Initialize(chainTool);
                return;
            }

            GameObject template = GameObject.Find("Color Tool");
            if (template == null) {
                return;
            }

            Transform sidebar = template.transform.parent;
            if (sidebar == null) {
                return;
            }

            GameObject clone = Object.Instantiate(template, sidebar);
            clone.name = "Chain Tool";

            RectTransform cloneRect = clone.GetComponent<RectTransform>();
            RectTransform templateRect = template.GetComponent<RectTransform>();
            if (cloneRect != null && templateRect != null) {
                float lowestY = templateRect.anchoredPosition.y;
                for (int i = 0; i < sidebar.childCount; i++) {
                    RectTransform child = sidebar.GetChild(i) as RectTransform;
                    if (child != null && child.gameObject.activeSelf) {
                        lowestY = Mathf.Min(lowestY, child.anchoredPosition.y);
                    }
                }

                cloneRect.anchoredPosition = new Vector2(templateRect.anchoredPosition.x, lowestY - 42f);
            }

            clone.transform.SetSiblingIndex(sidebar.childCount - 1);

            ChainToolbarUI chainToolbarUi = clone.GetComponent<ChainToolbarUI>();
            if (chainToolbarUi == null) {
                chainToolbarUi = clone.AddComponent<ChainToolbarUI>();
            }

            chainToolbarUi.Initialize(chainTool);
        }

        public void Initialize(InsertChainTool newChainTool) {
            chainTool = newChainTool;
            if (chainTool == null) {
                return;
            }

            buttonToggle = GetComponent<Toggle>();
            if (buttonToggle == null) {
                return;
            }

            toolToggle = GetComponent<ToolToggle>();
            if (toolToggle == null) {
                toolToggle = gameObject.AddComponent<ToolToggle>();
            }
            toolToggle.EnsureEventsInitialized();
            chainTool.SetToolToggle(toolToggle);

            // Cloning the scene Color Tool button also clones its serialized UnityEvents.
            // Replace them outright so the Chain button does not keep driving Color Tool state.
            buttonToggle.onValueChanged = new Toggle.ToggleEvent();
            buttonToggle.onValueChanged.AddListener(HandleToggleChanged);
            toolToggle.toggled = buttonToggle.isOn;

            foreach (Tooltip tooltip in GetComponentsInChildren<Tooltip>(true)) {
                tooltip.text = "Chain";
            }

            StripInheritedColorBehavior();

            Transform optionsTransform = transform.Find("Image");
            optionsPanel = optionsTransform != null ? optionsTransform.gameObject : null;

            Transform iconTransform = transform.Find("Chain Icon");
            if (iconTransform == null) {
                iconTransform = transform.Find("Color Icon");
            }
            if (iconTransform != null) {
                iconTransform.name = "Chain Icon";
                Image iconImage = iconTransform.GetComponent<Image>();
                Sprite chainSprite = ResolveChainSprite();
                if (iconImage != null && chainSprite != null) {
                    iconImage.sprite = chainSprite;
                }
            }

            if (optionsPanel == null) {
                return;
            }

            RectTransform optionsRect = optionsPanel.GetComponent<RectTransform>();
            if (optionsRect != null) {
                optionsRect.sizeDelta = new Vector2(optionsRect.sizeDelta.x, 58f);
            }

            Transform labelTransform = optionsTransform.Find("Text (TMP)");
            if (labelTransform != null) {
                TMP_Text label = labelTransform.GetComponent<TMP_Text>();
                if (label != null) {
                    label.text = "Chain:";
                }
            }

            Transform customTransform = optionsTransform.Find("Custom Color");
            if (customTransform != null) {
                Object.Destroy(customTransform.gameObject);
            }

            Transform dropdownTransform = optionsTransform.Find("Dropdown");
            sizeDropdown = dropdownTransform != null ? dropdownTransform.GetComponent<Dropdown>() : null;
            if (sizeDropdown != null) {
                RectTransform dropdownRect = sizeDropdown.GetComponent<RectTransform>();
                if (dropdownRect != null) {
                    dropdownRect.anchoredPosition = new Vector2(dropdownRect.anchoredPosition.x, -8f);
                }

                sizeDropdown.onValueChanged = new Dropdown.DropdownEvent();
                sizeDropdown.ClearOptions();
                sizeDropdown.AddOptions(SizeOptions);
                sizeDropdown.SetValueWithoutNotify(chainTool.GetToolbarStandardIndex());
                sizeDropdown.onValueChanged.AddListener(chainTool.SetToolbarStandardIndex);
                StripDropdownIconPresentation(sizeDropdown);
            }

            optionsPanel.SetActive(buttonToggle.isOn);
        }

        private void HandleToggleChanged(bool isOn) {
            if (toolToggle != null) {
                toolToggle.Toggle(isOn);
            }

            if (optionsPanel != null) {
                optionsPanel.SetActive(isOn);
            }

            if (isOn && sizeDropdown != null && chainTool != null) {
                sizeDropdown.SetValueWithoutNotify(chainTool.GetToolbarStandardIndex());
            }
        }

        private static Sprite ResolveChainSprite() {
            if (Protobot.PartsManager.partTypes == null || Protobot.PartsManager.partTypes.Length == 0) {
                Protobot.PartsManager.LoadPartTypes();
            }

            PartType chainPartType = Protobot.PartsManager.partTypes
                .FirstOrDefault(partType => partType != null && partType.id == Protobot.PartsManager.ChainToolPartId);
            if (chainPartType != null && chainPartType.icon != null) {
                return chainPartType.icon;
            }

            PartType sprocketPart = Protobot.PartsManager.partTypes
                .FirstOrDefault(partType =>
                    partType != null
                    && !string.IsNullOrWhiteSpace(partType.id)
                    && partType.id.StartsWith("SPKT")
                    && partType.icon != null);
            return sprocketPart != null ? sprocketPart.icon : null;
        }

        private void StripInheritedColorBehavior() {
            foreach (ColorTool inheritedColorTool in GetComponentsInChildren<ColorTool>(true)) {
                Object.Destroy(inheritedColorTool);
            }

            foreach (ColorToolActiveCheck activeCheck in GetComponentsInChildren<ColorToolActiveCheck>(true)) {
                activeCheck.enabled = false;
                Object.Destroy(activeCheck);
            }
        }

        private static void StripDropdownIconPresentation(Dropdown dropdown) {
            if (dropdown == null) {
                return;
            }

            if (dropdown.captionImage != null) {
                dropdown.captionImage.enabled = false;
                dropdown.captionImage.sprite = null;
                dropdown.captionImage.gameObject.SetActive(false);
                dropdown.captionImage = null;
            }

            if (dropdown.itemImage != null) {
                dropdown.itemImage.enabled = false;
                dropdown.itemImage.sprite = null;
                dropdown.itemImage.gameObject.SetActive(false);
                dropdown.itemImage = null;
            }
        }
    }
}
