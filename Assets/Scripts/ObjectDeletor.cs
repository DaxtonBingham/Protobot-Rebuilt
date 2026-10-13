using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Protobot.StateSystems;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Protobot.ChainSystem;

namespace Protobot {
    public class ObjectDeletor : ObjectLinkAction {
        private void DeleteObject() {
            if (!refObj.active) return;
            //there if check could def be simplifed but there is no real reason as performance impact is minimal
            if (EventSystem.current != null && 
                EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.GetComponent<InputField>()?.isFocused == true) return; 
            var obj = refObj.obj;

            if (obj.TryGetComponent(out HoleFace holeFace)) {
                obj = holeFace.hole.part;
            }

            ChainConnection selectedChain = obj.GetComponentInParent<ChainConnection>();
            if (selectedChain != null) {
                ObjectElement.SetExistence(selectedChain.gameObject, true, false);
                return;
            }

            Pivot pivot = obj.GetComponent<Pivot>();

            ChainManager.NotifyEndpointObjectDeleted(obj);

            if (pivot != null && pivot.tag != "Group")
                ObjectElement.SetExistence(pivot, true, false);
            else
                ObjectElement.SetExistence(obj, true, false);
        }

        public override void Execute() {
            DeleteObject();
            OnExecute?.Invoke();
            // Call OutputPartsList to update the weight display after placing a part
                PartListOutput partListOutput = FindObjectOfType<PartListOutput>();
                if (partListOutput != null) {
                    partListOutput.CalculatePartsList(); // Recalculate and notify weight update
                }
        }
    }
}
