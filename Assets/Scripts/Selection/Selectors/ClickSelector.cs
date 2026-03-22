using System;
using UnityEngine;
using Protobot.InputEvents;
using Protobot.ChainSystem;

namespace Protobot.SelectionSystem {
    public class ClickSelector : Selector {
        public override event Action<ISelection> setEvent;
        public override event Action clearEvent;
        
        [SerializeField] private MouseCast mouseCast = null;
        [SerializeField] private InputEvent input;

        public void Awake() {
            input.performed += () => OnPerformInput();
        }

        private void OnPerformInput() {
            if (!MouseInput.overUI) {
                if (mouseCast.overObj) {
                    GameObject selectedObject = ChainManager.ResolveSelectableObject(mouseCast.gameObject);
                    var selection = new ObjectSelection {
                        gameObject = selectedObject,
                        selector = this
                    };

                    setEvent?.Invoke(selection);
                }
                else {
                    clearEvent?.Invoke();
                }
            }
        }
    }
}
