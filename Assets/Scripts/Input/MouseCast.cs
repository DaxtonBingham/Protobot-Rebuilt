using UnityEngine;

namespace Protobot {
    public struct SceneHit {
        public Collider collider;
        public Vector3 point, normal;
        public float distance;
        public Transform transform => collider != null ? collider.transform : null;
        public static SceneHit From(RaycastHit hit) => new SceneHit { collider = hit.collider, point = hit.point, normal = hit.normal, distance = hit.distance };
    }
[RequireComponent(typeof(Camera))]
    public class MouseCast : MonoBehaviour {
        [CacheComponent] private new Camera camera;
        public Ray ray {
            get {
                Camera targetCamera = GetOrResolveCamera();
                if (targetCamera == null) {
                    return new Ray();
                }

                return ProjectionSwitcher.ScreenPointToRay(targetCamera, MouseInput.Position);
            }
        }
        private MeshCollider holeProxy;
        private HoleCollider holeProxyData;
        private int cachedFrame = -1, cachedMask;
        private uint cachedRevision, cachedHoleRevision;
        private bool cachedBackfaces, cachedTriggers;
        public long QueryCount { get; private set; }
        private Ray cachedRay;
        private SceneHit cachedHit;
        public HoleRecord HoverHole { get; private set; }
        public SceneHit hit {
            get {
                if (GetOrResolveCamera() == null) {
                    return new SceneHit();
                }
                HoleWorld.Flush();
                var currentRay = ray;
                if (cachedFrame >= 0 && cachedMask == layerMask && cachedRevision == SceneActivity.Revision
                    && cachedHoleRevision == HoleWorld.Revision && cachedBackfaces == Physics.queriesHitBackfaces
                    && cachedTriggers == Physics.queriesHitTriggers && cachedRay.origin.Equals(currentRay.origin)
                    && cachedRay.direction.Equals(currentRay.direction)) return cachedHit;
                cachedFrame = Time.frameCount; cachedMask = layerMask; cachedRevision = SceneActivity.Revision; cachedRay = currentRay;
                cachedHoleRevision = HoleWorld.Revision; cachedBackfaces = Physics.queriesHitBackfaces;
                cachedTriggers = Physics.queriesHitTriggers; QueryCount++;
                cachedHit = new SceneHit(); HoverHole = null;
                if (Physics.Raycast(currentRay, out RaycastHit lastHit, Mathf.Infinity, layerMask & ~HoleCollider.HOLE_COLLISIONS_MASK) && lastHit.transform.root.gameObject.layer != Placement.PLACEMENT_LAYER)
                    cachedHit = SceneHit.From(lastHit);
                float maxDistance = cachedHit.collider != null ? cachedHit.distance : float.PositiveInfinity;
                if ((layerMask & 1) != 0) foreach (var chain in Protobot.ChainSystem.ChainManager.Connections) {
                    if (chain == null || chain.IsPreview || !chain.isActiveAndEnabled || chain.InstanceGeometry == null) continue;
                    if (chain.InstanceGeometry.Raycast(currentRay, maxDistance, out var chainHit)) { cachedHit = chainHit; maxDistance = chainHit.distance; }
                }
                if ((layerMask & HoleCollider.HOLE_COLLISIONS_MASK) != 0 && HoleWorld.Raycast(currentRay, out var holeHit, maxDistance)) {
                    SetHoleProxy(holeHit.hole); HoverHole = holeHit.hole;
                    cachedHit = new SceneHit { collider = holeProxy, point = holeHit.point, normal = holeHit.normal, distance = holeHit.distance };
                }
                return cachedHit;
            }
        }
        private void SetHoleProxy(HoleRecord hole) {
            if (holeProxy == null) {
                var proxy = new GameObject("Hole pick", typeof(MeshCollider));
                proxy.tag = "HoleCollider"; proxy.layer = HoleCollider.HOLE_COLLISIONS_LAYER;
                holeProxy = proxy.GetComponent<MeshCollider>(); holeProxy.enabled = false;
                holeProxyData = proxy.AddComponent<HoleCollider>(); holeProxyData.IsPickingProxy = true;
            }
            holeProxyData.BindRecord(hole);
            holeProxy.transform.SetParent(hole.Owner.transform, false);
            holeProxy.transform.localPosition = hole.Definition.localMatrix.GetColumn(3);
            holeProxy.transform.localRotation = hole.Definition.localRotation;
            holeProxy.transform.localScale = hole.Definition.localMatrix.lossyScale;
            holeProxy.sharedMesh = hole.Definition.mesh;
        }
        private void OnDestroy() { if (holeProxy != null) Destroy(holeProxy.gameObject); }
        public bool overObj => (hit.collider != null);
        public new GameObject gameObject {
            get { var collider = hit.collider; return collider != null ? collider.gameObject : null; }
        }
        public new Transform transform {
            get { var target = gameObject; return target != null ? target.transform : null; }
        }

        public int layerMask;

        private void Awake() {
            GetOrResolveCamera();
            ResetLayerMask();
        }

        private Camera GetOrResolveCamera() {
            if (camera == null) {
                camera = GetComponent<Camera>();
            }

            return camera;
        }

        public void ResetLayerMask() => layerMask = LayerMask.GetMask("Default", "HoleCollisions");

        public void SetHolesOnlyMode(bool value) {
            if (value)
                layerMask = LayerMask.GetMask("HoleCollisions");
            else
                ResetLayerMask();
        }
    }
}
