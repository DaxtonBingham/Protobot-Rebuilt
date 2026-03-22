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

        [Space(10)]

        public UnityEvent OnSelectPartDisplay;
        public UnityEvent OnDeselectPartDisplay;


        void Start() {
            EnsureSearchInput();
            EnsurePartTypesLoaded();

            PartDisplayUI.OnChangeSelected += _ => {
                OnSelectPartDisplay?.Invoke();
            };

            groupDropdown.onValueChanged.AddListener(index => {
                string group = groupDropdown.options[index].text;

                if (group == "None") {
                    DisplaySearchResults();
                }
                else {
                    DisplayListGroup(group);
                }
            });
            
            DisplaySearchResults();
        }
        
        void Update() {
            string currentSearch = GetSearchTerm();

            if (searchToggle.isOn && currentSearch != prevSearch) {
                DisplaySearchResults();
            }

            prevSearch = currentSearch;
            
            if (toggleCount == 0 && prevToggleCount != 0)
                OnDeselectPartDisplay?.Invoke();

            prevToggleCount = toggleCount;
        }

        public void DeslectSelected() {
            if (toggleCount != 0)
                PartDisplayUI.selected.GetComponent<Toggle>().isOn = false;
        }

        public void SetEmptyListText(string message) {
            EmptyListText.gameObject.SetActive(true);
            EmptyListText.text = message;
        }

        public void DisplayListGroup(string group) {
            List<PartType> groupList = GetAvailablePartTypes()
                .Where(p => p != null && p.group.ToString() == group)
                .ToList();
            UpdateDisplayedParts(groupList);
        }

        public void DisplaySearchResults() {
            searchToggle.isOn = true;
            string search = GetSearchTerm().ToLowerInvariant();
            List<PartType> searchList = GetAvailablePartTypes().Where(p =>
                p != null
                && p.group != PartType.PartGroup.None
                &&
                CompareSearch(search, p.name)
            ).ToList();
                
            UpdateDisplayedParts(searchList);

            if (searchList.Count == 0)
                SetEmptyListText(EmptySearchMessage);
        }

        public bool CompareSearch(string search, string compare) {
            compare = compare.ToLower();
            return (search.Contains(compare) || compare.Contains(search));
        }

        public void DestroyDisplayedParts() {
            int prevListLength = partUIsContainer.childCount;

            for (int c = 1; c < prevListLength; c++)
                Destroy(partUIsContainer.GetChild(c).gameObject);
        }

        //updates list of objects shown given a list of PartPackets
        public void UpdateDisplayedParts(List<PartType> partsToDisplay) {
            EmptyListText.gameObject.SetActive(false);

            DestroyDisplayedParts();

            for (int i = 0; i < partsToDisplay.Count; i++) {
                GameObject newItem = Instantiate(partUI);
                newItem.transform.SetParent(partUIsContainer);

                RectTransform newRectTransform = newItem.GetComponent<RectTransform>();
                newRectTransform.localScale = Vector3.one;
                newRectTransform.anchoredPosition = new Vector2(0 ,i * (partUI.GetComponent<RectTransform>().sizeDelta.y + spacing));

                PartDisplayUI newPartDisplayUI = newItem.GetComponent<PartDisplayUI>();
                newPartDisplayUI.SetDisplay(partsToDisplay[i]);

                Toggle newToggle = newItem.GetComponent<Toggle>();
                newToggle.group = partDisplayToggleGroup;
            }
            partUIsContainer.sizeDelta = new Vector2(partUIsContainer.sizeDelta.x, (partsToDisplay.Count) * (partUI.GetComponent<RectTransform>().sizeDelta.y + spacing) - spacing);
        }

        private void EnsurePartTypesLoaded() {
            if (PartsManager.partTypes == null || PartsManager.partTypes.Length == 0) {
                PartsManager.LoadPartTypes();
            }
        }

        private IEnumerable<PartType> GetAvailablePartTypes() {
            EnsurePartTypesLoaded();
            return (PartsManager.partTypes ?? Enumerable.Empty<PartType>())
                .Where(partType => partType != null && partType.id != PartsManager.ChainToolPartId);
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
