using Models;
using UnityEngine;
using Protobot.Outlining;
using UnityEngine.UIElements;

namespace Protobot.SelectionSystem
{
    public class ColorSelectionResponse : SelectionResponse
    {
        public override bool RespondOnlyToSelectors => false;

        //This feels so wrong feel free to PR it if you have a better solution this is being called by HoleFaceResponseSelector.getresponseslection 
        public void HoleColliderException(ISelection sel)
        {
            OnSet(sel);
        }
        public override void OnSet(ISelection sel)
        {
            if (sel == null)  
                return;
            ChangeColor(sel.gameObject);
        }
        public void ChangeColor(GameObject targetGameObject)
        {
            if (targetGameObject == null) return;
            var component = targetGameObject.GetComponent<Renderer>();
            if (component == null && targetGameObject.transform.parent != null)
                component = targetGameObject.transform.parent.GetComponent<Renderer>();
            if (component == null || component.sharedMaterial == null) return;
            var material = component.sharedMaterial;
            if (!material.HasProperty("_Metallic") || material.GetFloat("_Metallic") != .754f) return;
            // Reading a selected color must not instantiate a material for every hovered part.
            if (ColorToolActiveCheck.colorToolActive) {
                material = component.material;
                material.color = ColorTool.ColorToSet;
                var view = component.GetComponent<SavedObject>();
                if (view != null) RobotDocument.Synchronize(view, PartChange.Appearance);
                else SceneActivity.Changed();
            }
            ColorTool.Material = material;
        }

        public override void OnClear(ClearInfo info)
        {
            info.selection.Deselect();
        }
    }
}
