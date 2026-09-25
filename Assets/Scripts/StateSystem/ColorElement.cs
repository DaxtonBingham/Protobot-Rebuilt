using UnityEngine;

namespace Protobot.StateSystems {
    public sealed class ColorElement : IElement {
        private readonly Renderer renderer;
        private readonly Color color;
        public ColorElement(Renderer renderer) {
            this.renderer = renderer;
            color = renderer.sharedMaterial.color;
        }
        public void Load() {
            if (renderer == null) return;
            var view = renderer.GetComponent<SavedObject>();
            if (view != null) RobotDocument.SetColor(view, color);
            else { renderer.material.color = color; SceneActivity.Changed(); }
        }
    }
}
