using System;
using UnityEngine;
using Protobot.ChainSystem;

namespace Protobot.SelectionSystem {
    public class HoverSelector : Selector {
        public override event Action<ISelection> setEvent;
        public override event Action clearEvent;
        [SerializeField] private MouseCast mouseCast = null;
        [SerializeField] private bool checkPrevObj;

        private GameObject prevObj;
        private HoleRecord prevHole;

        public void Update() {
            GameObject mouseCastObj = ChainManager.ResolveSelectableObject(mouseCast.gameObject);

            if (mouseCastObj != null) {
                if (prevObj != mouseCastObj || prevHole != mouseCast.HoverHole || !checkPrevObj) {
                    var selection = new ObjectSelection {
                        gameObject = mouseCastObj,
                        selector = this
                    };

                    setEvent?.Invoke(selection);
                }
            }
            else
                clearEvent?.Invoke();

            prevObj = mouseCastObj;
            prevHole = mouseCast.HoverHole;
        }
    }
}
