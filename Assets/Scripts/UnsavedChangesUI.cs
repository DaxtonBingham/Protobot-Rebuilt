using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.UI {
    /// <summary>
    /// UI that displays whenever the user attempts to exit or load with unsaved changes on the current build
    /// </summary>
    public class UnsavedChangesUI : MonoBehaviour {
        [SerializeField] private TMP_Text warningText;
        
        [SerializeField] private Button saveButton, discardButton;
        private string UnsavedChangesText => "This build has unsaved changes. ";
        private string NotSavedYetText => "This build has not been saved yet.";
        

        public Action OnPressSave;
        public Action OnPressDiscard;

        private void OnEnable() {
            saveButton.onClick.AddListener(() => {
                gameObject.SetActive(false);
                // A failed/cancelled save can reopen this prompt for retry or cancel.
                OnPressSave?.Invoke();
            });
            discardButton.onClick.AddListener(() => {
                gameObject.SetActive(false);
                OnPressDiscard?.Invoke();
            });
        }
        
        private void OnDisable() {
            saveButton.onClick.RemoveAllListeners();
            discardButton.onClick.RemoveAllListeners();
        }

        public void Enable(bool noFilePath) {
            warningText.text = noFilePath ? NotSavedYetText : UnsavedChangesText;
            gameObject.SetActive(true);
        }
    }
}
