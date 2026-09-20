using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using UnityEngine.Events;

namespace Protobot.UI {
    public class AddPartsUI : MonoBehaviour {
        public GameObject lastAddedObj;

        [Header("UI")]
        public Text EmptyListText;
        public string EmptySearchMessage;
        public Text searchText; //the text typed in the searchbar
        [SerializeField] private InputField searchInput;
        private string prevSearch; //the text typed in the searchbar
        public Toggle searchToggle;
        public Dropdown groupDropdown;
        [SerializeField] private float spacing;

        [Space(10)]
        public GameObject partUI; //the UI for individual packets
        public RectTransform partUIsContainer; //used for parenting

        public ToggleGroup partDisplayToggleGroup;
        private int toggleCount => partDisplayToggleGroup.ActiveToggles().Count<Toggle>();
        private int prevToggleCount;
        private bool started;
        private string selectedCategory = "";
        private readonly HashSet<string> expandedCategories = new HashSet<string>();

        [Space(10)]

        public UnityEvent OnSelectPartDisplay;
        public UnityEvent OnDeselectPartDisplay;


        void OnEnable() {
            GameElementPreferences.Changed += RefreshBrowser;
            if (started) RefreshBrowser();
        }

        void OnDisable() {
            GameElementPreferences.Changed -= RefreshBrowser;
        }

        void OnDestroy() {
            PartDisplayUI.OnChangeSelected -= HandlePartSelected;
        }

        void HandlePartSelected(PartDisplayUI display) {
            var chainTool = FindObjectOfType<Protobot.ChainSystem.InsertChainTool>();
            if (chainTool != null && chainTool.GestureActive) chainTool.EndGesture(false);
            OnSelectPartDisplay?.Invoke();
        }

        void RefreshBrowser() {
            DeslectSelected();
            if (string.IsNullOrEmpty(selectedCategory)) DisplaySearchResults();
            else DisplayListGroup(selectedCategory);
        }

        void Start() {
            EnsureSearchInput();
            EnsurePartTypesLoaded();

            PartDisplayUI.OnChangeSelected += HandlePartSelected;
            ConfigureBrowserLayout();
            if (searchInput != null && searchInput.placeholder is Text placeholder)
                placeholder.text = "Search parts...";

            DisplaySearchResults();
            started = true;
        }
        
        void Update() {
            string currentSearch = GetSearchTerm();

            if (currentSearch != prevSearch) {
                selectedCategory = "";
                DisplaySearchResults();
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
                && keyboard.fKey.wasPressedThisFrame && !Protobot.CustomParts.CustomPartStudioController.IsStudioOpen) {
                EnsureSearchInput();
                searchInput?.Select();
                searchInput?.ActivateInputField();
            }

            prevSearch = currentSearch;
            
            if (toggleCount == 0 && prevToggleCount != 0)
                OnDeselectPartDisplay?.Invoke();

            prevToggleCount = toggleCount;
        }

        public void DeslectSelected() {
            if (toggleCount != 0 && PartDisplayUI.selected != null)
                PartDisplayUI.selected.GetComponent<Toggle>().isOn = false;
        }

        public void SetEmptyListText(string message) {
            EmptyListText.gameObject.SetActive(true);
            EmptyListText.text = message;
        }

        public void DisplayListGroup(string group) {
            selectedCategory = group;
            searchToggle.SetIsOnWithoutNotify(false);
            List<PartType> groupList = GetAvailablePartTypes()
                .Where(p => PartBrowserCategories.For(p) == group)
                .ToList();
            UpdateDisplayedParts(groupList);
        }

        public void DisplaySearchResults() {
            selectedCategory = "";
            searchToggle.SetIsOnWithoutNotify(true);
            string search = GetSearchTerm().ToLowerInvariant();
            List<PartType> searchList = GetAvailablePartTypes().Where(p =>
                p != null
                && p.group != PartType.PartGroup.None
                &&
                CompareSearch(search, p.name)
            ).ToList();
                
            UpdateDisplayedParts(searchList);
        }

        public bool CompareSearch(string search, string compare) {
            compare = (compare ?? "").ToLowerInvariant();
            return search.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries)
                .All(word => compare.Contains(word));
        }

        public void DestroyDisplayedParts() {
            int prevListLength = partUIsContainer.childCount;

            for (int c = 1; c < prevListLength; c++) {
                var item = partUIsContainer.GetChild(c).gameObject;
                item.SetActive(false);
                Destroy(item);
            }
        }

        //updates list of objects shown given a list of PartPackets
        public void UpdateDisplayedParts(List<PartType> partsToDisplay) {
            partsToDisplay = partsToDisplay.Where(p => p != null && GameElementPreferences.IsVisible(p)).ToList();
            EmptyListText.gameObject.SetActive(false);
            DestroyDisplayedParts();
            partUIsContainer.anchoredPosition = Vector2.zero;
            float y = 0;
            bool searching = !string.IsNullOrWhiteSpace(GetSearchTerm());
            foreach (string category in PartBrowserCategories.Names) {
                var items = partsToDisplay.Where(p => PartBrowserCategories.For(p) == category)
                    .OrderBy(p => p.name).ToList();
                if (items.Count == 0) continue;
                bool expanded = searching || selectedCategory == category || expandedCategories.Contains(category);
                CreateCategoryHeader(category, items.Count, expanded, searching, y);
                y += 35;
                if (expanded) foreach (PartType part in items) {
                    GameObject item = Instantiate(partUI, partUIsContainer, false);
                    PositionRow(item.GetComponent<RectTransform>(), y, 30);
                    var display = item.GetComponent<PartDisplayUI>();
                    display.SetDisplay(part);
                    display.mainIcon.preserveAspect = true;
                    display.nameText.fontSize = 14;
                    var label = display.nameText.rectTransform;
                    label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one;
                    label.offsetMin = new Vector2(35, 0); label.offsetMax = new Vector2(-5, 0);
                    display.nameText.alignment = TextAnchor.MiddleLeft;
                    var toggle = item.GetComponent<Toggle>();
                    toggle.group = partDisplayToggleGroup;
                    if (part.id == PartsManager.ChainToolPartId) {
                        toggle.onValueChanged = new Toggle.ToggleEvent();
                        toggle.onValueChanged.AddListener(on => {
                            if (!on) return;
                            toggle.SetIsOnWithoutNotify(false);
                            DeslectSelected();
                            OnDeselectPartDisplay?.Invoke();
                            FindObjectOfType<Protobot.ChainSystem.InsertChainTool>()?.BeginPlacement();
                            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
                        });
                    }
                    y += 35;
                }
                y += 5;
            }
            partUIsContainer.sizeDelta = new Vector2(partUIsContainer.sizeDelta.x, Mathf.Max(0, y));
            if (partsToDisplay.Count == 0) SetEmptyListText("No matching parts.\nTry another search or category.");
        }

        private static void PositionRow(RectTransform rect, float y, float height) {
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1); rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(-4, height);
            rect.anchoredPosition = new Vector2(0, -y);
        }

        private void CreateCategoryHeader(string category, int count, bool expanded, bool searching, float y) {
            var obj = new GameObject(category + " Category", typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(partUIsContainer, false);
            PositionRow(obj.GetComponent<RectTransform>(), y, 30);
            var background = obj.GetComponent<Image>();
            var template = partUI.GetComponent<Image>();
            background.sprite = template.sprite;
            background.type = template.type;
            background.pixelsPerUnitMultiplier = template.pixelsPerUnitMultiplier;
            background.color = template.color;
            var button = obj.GetComponent<Button>();
            button.targetGraphic = background;
            button.colors = partUI.GetComponent<Toggle>().colors;
            button.onClick.AddListener(() => {
                if (searching) return;
                selectedCategory = "";
                if (expanded) expandedCategories.Remove(category); else expandedCategories.Add(category);
                RefreshBrowser();
            });
            var title = new GameObject("Category label", typeof(RectTransform), typeof(Text));
            title.transform.SetParent(obj.transform, false);
            var label = title.GetComponent<Text>();
            label.font = partUI.GetComponent<PartDisplayUI>().nameText.font;
            label.fontSize = 14; label.fontStyle = FontStyle.Normal;
            label.text = category;
            label.color = Color.white; label.raycastTarget = false;
            label.alignment = TextAnchor.MiddleLeft;
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(25, 0); rect.offsetMax = new Vector2(-32, 0);
            var arrowTemplate = groupDropdown.transform.Find("Arrow")?.GetComponent<Image>();
            if (arrowTemplate != null) {
                var arrow = new GameObject("Expand category", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                arrow.transform.SetParent(obj.transform, false);
                arrow.sprite = arrowTemplate.sprite; arrow.color = arrowTemplate.color;
                arrow.preserveAspect = true; arrow.raycastTarget = false;
                var arrowRect = arrow.rectTransform;
                arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(0, .5f);
                arrowRect.sizeDelta = new Vector2(10, 10); arrowRect.anchoredPosition = new Vector2(12, 0);
                arrowRect.localRotation = Quaternion.Euler(0, 0, expanded ? 180 : -90);
            }
            var countText = Instantiate(label, obj.transform, false);
            countText.name = "Part count"; countText.text = count.ToString();
            countText.color = new Color(1, 1, 1, .55f); countText.fontSize = 12;
            countText.alignment = TextAnchor.MiddleRight;
            countText.rectTransform.offsetMin = new Vector2(0, 0);
            countText.rectTransform.offsetMax = new Vector2(-10, 0);
        }

        private void ConfigureBrowserLayout() {
            var root = (RectTransform)transform;
            root.sizeDelta = new Vector2(240, root.sizeDelta.y);
            var properties = transform.Find("Add Properties Menu") as RectTransform;
            if (properties != null) properties.sizeDelta = new Vector2(240, properties.sizeDelta.y);
            searchToggle.gameObject.SetActive(false);
            groupDropdown.gameObject.SetActive(false);
            var options = groupDropdown.transform.parent as RectTransform;
            options.sizeDelta = new Vector2(options.sizeDelta.x, 55);
            var viewport = partUIsContainer.parent as RectTransform;
            viewport.anchoredPosition = new Vector2(0,-32.5f);
            viewport.sizeDelta = new Vector2(0,-65);
            CreatePolyMakerButton();
        }

        private void CreatePolyMakerButton() {
            if (searchInput == null) return;
            var searchRect = (RectTransform)searchInput.transform;
            searchRect.offsetMax = new Vector2(-30, searchRect.offsetMax.y);
            var searchIcon = searchRect.Find("TabIcon") as RectTransform;
            if (searchIcon != null) {
                searchIcon.anchorMin = searchIcon.anchorMax = new Vector2(1, .5f);
                searchIcon.anchoredPosition = new Vector2(-13, 0);
                searchIcon.sizeDelta = new Vector2(22, 22);
                searchIcon.GetComponent<Image>().raycastTarget = false;
            }
            searchInput.textComponent.rectTransform.offsetMax = new Vector2(-26, -3);
            if (searchInput.placeholder != null)
                searchInput.placeholder.rectTransform.offsetMax = new Vector2(-26, -3);

            var obj = new GameObject("Open Poly Maker", typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(searchRect.parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(25, 25);
            var background = obj.GetComponent<Image>();
            var template = partUI.GetComponent<Image>();
            background.sprite = template.sprite;
            background.type = template.type;
            background.pixelsPerUnitMultiplier = template.pixelsPerUnitMultiplier;
            background.color = template.color;
            var button = obj.GetComponent<Button>();
            button.targetGraphic = background;
            button.colors = partUI.GetComponent<Toggle>().colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var tooltip = Resources.FindObjectsOfTypeAll<TooltipUI>()
                .FirstOrDefault(t => t.gameObject.scene.IsValid());
            if (tooltip != null)
                obj.AddComponent<Tooltip>().Initialize(tooltip, TooltipUI.Direction.Left, "Open Poly Maker");
            button.onClick.AddListener(() => {
                tooltip?.HideToolTip();
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
                FindObjectOfType<Protobot.CustomParts.CustomPartStudioController>()?.OpenFromBrowser();
            });
            for (int i = 0; i < 2; i++) {
                var stroke = new GameObject(i == 0 ? "Plus horizontal" : "Plus vertical", typeof(RectTransform), typeof(Image));
                stroke.transform.SetParent(rect, false);
                var strokeRect = stroke.GetComponent<RectTransform>();
                strokeRect.anchorMin = strokeRect.anchorMax = new Vector2(.5f, .5f);
                strokeRect.sizeDelta = i == 0 ? new Vector2(13, 2) : new Vector2(2, 13);
                var strokeImage = stroke.GetComponent<Image>();
                strokeImage.color = Color.white;
                strokeImage.raycastTarget = false;
            }
        }

        private void EnsurePartTypesLoaded() {
            if (PartsManager.partTypes == null || PartsManager.partTypes.Length == 0) {
                PartsManager.LoadPartTypes();
            }
        }

        private IEnumerable<PartType> GetAvailablePartTypes() {
            EnsurePartTypesLoaded();
            return (PartsManager.partTypes ?? Enumerable.Empty<PartType>())
                .Where(partType => partType != null
                    && partType.group != PartType.PartGroup.None && GameElementPreferences.IsVisible(partType));
        }

        private void EnsureSearchInput() {
            if (searchInput == null && searchText != null) {
                searchInput = searchText.GetComponentInParent<InputField>();
            }

            if (searchInput == null) {
                searchInput = GetComponentInChildren<InputField>(true);
            }
        }

        private string GetSearchTerm() {
            EnsureSearchInput();

            if (searchInput != null) {
                return searchInput.text == null ? string.Empty : searchInput.text;
            }

            return string.Empty;
        }
    }
}
