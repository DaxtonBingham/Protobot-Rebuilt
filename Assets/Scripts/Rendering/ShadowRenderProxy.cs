using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot {
    // Native per-object shadow culling avoids sending every color-batch instance
    // to every cascade. The compact mesh preserves the source triangles/normals.
    internal sealed class ShadowRenderProxy : MonoBehaviour {
        [SerializeField] private bool initialized;
        [NonSerialized] internal RobotPart owner;
        private MeshFilter filter;
        private MeshRenderer meshRenderer;
        private void Awake() { if (initialized) Destroy(gameObject); }

        internal static ShadowRenderProxy Create(RobotPart part) {
            var obj = new GameObject("Shadow renderer") { hideFlags = HideFlags.HideAndDontSave };
            obj.transform.SetParent(part.View.transform, false);
            var proxy = obj.AddComponent<ShadowRenderProxy>();
            proxy.owner = part; proxy.initialized = true;
            proxy.filter = obj.AddComponent<MeshFilter>();
            proxy.meshRenderer = obj.AddComponent<MeshRenderer>();
            return proxy;
        }
        internal void Configure(Mesh mesh, Material material, int layer, LightProbeUsage probes, bool visible) {
            filter.sharedMesh = mesh; meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            meshRenderer.receiveShadows = false; meshRenderer.lightProbeUsage = probes;
            gameObject.layer = layer; gameObject.SetActive(visible);
        }
        internal static void RemoveClones(SavedObject view) {
            foreach (var proxy in view.GetComponentsInChildren<ShadowRenderProxy>(true))
                if (proxy.transform.parent == view.transform && proxy.owner == null) Destroy(proxy.gameObject);
        }
    }
}
