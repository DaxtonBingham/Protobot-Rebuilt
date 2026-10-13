using Protobot;
using UnityEngine;

namespace Protobot.ChainSystem {
    public enum ChainGuideRoutingBias {
        Auto = 0,
        PushOutward = 1,
        PushInward = 2
    }

    public class ChainGuide : MonoBehaviour {
        [SerializeField] private string socketId = "guide";
        [SerializeField] private Vector3 localCenterOffset = Vector3.zero;
        [SerializeField] private Vector3 localAxisNormal = Vector3.forward;
        [SerializeField] private float guideRadius = 0.5f;
        [SerializeField] private ChainGuideRoutingBias routingBias = ChainGuideRoutingBias.Auto;
        [SerializeField] private bool flipSide;
        [SerializeField] private bool saveWithBuild = false;
        [SerializeField] private bool autoConfigure = true;
        [SerializeField] private bool usePrimaryHoleAxis = true;
        [SerializeField] private bool useBoundsCenter = true;
        [SerializeField] private bool autoEstimateRadius = true;
        [SerializeField] private bool configured = false;

        public string SocketId => string.IsNullOrWhiteSpace(socketId) ? "guide" : socketId;
        public Vector3 WorldCenter => transform.TransformPoint(localCenterOffset);
        public Vector3 WorldAxis => transform.TransformDirection(localAxisNormal).normalized;
        public float Radius => Mathf.Max(0.05f, guideRadius);
        public ChainGuideRoutingBias RoutingBias => routingBias;
        public bool FlipSide => flipSide;
        [SerializeField] private Vector3 localContactHint;
        public Vector3 LocalContactHint => localContactHint;
        internal object RouteGeometryCache;
        public bool HasContactHint => localContactHint.sqrMagnitude > 0.5f;
        public Vector3 WorldContactHint => transform.TransformDirection(localContactHint);
        public void SetLocalContactHint(Vector3 direction) { localContactHint = direction.normalized; }
        public void SetWorldContactHint(Vector3 direction) { SetLocalContactHint(transform.InverseTransformDirection(direction)); }
        public bool SaveWithBuild => saveWithBuild;

        private void Awake() {
            if (autoConfigure) {
                AutoConfigureIfNeeded();
            }
        }

        public bool MatchesSocket(string requestedSocketId) {
            string normalizedRequested = string.IsNullOrWhiteSpace(requestedSocketId)
                ? SocketId
                : requestedSocketId.Trim().ToLowerInvariant();
            return SocketId.Trim().ToLowerInvariant() == normalizedRequested;
        }

        public void AutoConfigureIfNeeded() {
            if (configured) {
                return;
            }

            ConfigureFromObject();
        }

        public void ConfigureFromObject() {
            Vector3 worldAxis = transform.forward.sqrMagnitude > 0.0001f ? transform.forward : Vector3.forward;
            Vector3 worldCenter = transform.position;
            bool usedPrimaryHoleCenter = false;

            if (TryResolvePrimaryHole(out PartData primaryHolePartData) && primaryHolePartData.PrimaryHole != null) {
                worldAxis = primaryHolePartData.PrimaryHole.forward;
                if (!useBoundsCenter) {
                    worldCenter = primaryHolePartData.PrimaryHole.position;
                    usedPrimaryHoleCenter = true;
                }
            }

            if (worldAxis.sqrMagnitude < 0.0001f) {
                worldAxis = Vector3.forward;
            }
            localAxisNormal = transform.InverseTransformDirection(worldAxis).normalized;

            if (useBoundsCenter && TryResolveRendererCenter(out Vector3 renderedCenter)) {
                worldCenter = renderedCenter;
            }
            else if (!usedPrimaryHoleCenter && TryResolvePrimaryHole(out PartData fallbackPartData) && fallbackPartData.PrimaryHole != null) {
                worldCenter = fallbackPartData.PrimaryHole.position;
            }

            localCenterOffset = transform.InverseTransformPoint(worldCenter);

            if (autoEstimateRadius) {
                guideRadius = ChainSprocketUtility.EstimateRadius(gameObject, worldAxis, worldCenter);
            }

            guideRadius = Mathf.Max(guideRadius, 0.05f);
            configured = true;
        }

        public void ConfigureManual(
            string newSocketId,
            Vector3 localPosition,
            Quaternion localRotation,
            float newRadius,
            bool newSaveWithBuild = true,
            ChainGuideRoutingBias newRoutingBias = ChainGuideRoutingBias.Auto,
            bool newFlipSide = false) {
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;

            socketId = string.IsNullOrWhiteSpace(newSocketId) ? "guide" : newSocketId;
            localCenterOffset = Vector3.zero;
            localAxisNormal = Vector3.forward;
            guideRadius = Mathf.Max(newRadius, 0.05f);
            routingBias = newRoutingBias;
            flipSide = newFlipSide;
            saveWithBuild = newSaveWithBuild;
            autoConfigure = false;
            usePrimaryHoleAxis = false;
            useBoundsCenter = false;
            autoEstimateRadius = false;
            configured = true;
        }

        public void SetRadius(float newRadius) {
            guideRadius = Mathf.Max(newRadius, 0.05f);
            configured = true;
        }

        public void SetRoutingBias(ChainGuideRoutingBias newRoutingBias) {
            routingBias = newRoutingBias;
            configured = true;
        }

        public void SetFlipSide(bool value) {
            flipSide = value;
            configured = true;
        }

        public void SetSaveWithBuild(bool value) {
            saveWithBuild = value;
        }

        private bool TryResolvePrimaryHole(out PartData partData) {
            partData = null;
            if (!usePrimaryHoleAxis) {
                return false;
            }

            if (TryGetComponent(out partData) && partData.PrimaryHole != null) {
                return true;
            }

            partData = GetComponentInParent<PartData>();
            return partData != null && partData.PrimaryHole != null;
        }

        private bool TryResolveRendererCenter(out Vector3 center) {
            center = transform.position;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) {
                return false;
            }

            Renderer firstActiveRenderer = null;
            for (int i = 0; i < renderers.Length; i++) {
                Renderer renderer = renderers[i];
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy) {
                    firstActiveRenderer = renderer;
                    break;
                }
            }

            if (firstActiveRenderer == null) {
                return false;
            }

            Bounds bounds = firstActiveRenderer.bounds;
            for (int i = 0; i < renderers.Length; i++) {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer == firstActiveRenderer) {
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            center = bounds.center;
            return true;
        }

        private void OnDrawGizmosSelected() {
            Vector3 axis = WorldAxis;
            if (axis.sqrMagnitude < 0.0001f) {
                axis = Vector3.forward;
            }

            if (!TryBuildGuideBasis(axis, out Vector3 basisX, out Vector3 basisY)) {
                return;
            }

            const int segmentCount = 32;
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 1f);

            Vector3 previousPoint = WorldCenter + (basisX * Radius);
            for (int i = 1; i <= segmentCount; i++) {
                float angle = (Mathf.PI * 2f * i) / segmentCount;
                Vector3 currentPoint = WorldCenter
                    + (basisX * Mathf.Cos(angle) * Radius)
                    + (basisY * Mathf.Sin(angle) * Radius);
                Gizmos.DrawLine(previousPoint, currentPoint);
                previousPoint = currentPoint;
            }

            Gizmos.DrawLine(WorldCenter - (axis.normalized * 0.25f), WorldCenter + (axis.normalized * 0.25f));
        }

        private static bool TryBuildGuideBasis(Vector3 axis, out Vector3 basisX, out Vector3 basisY) {
            Vector3 normalizedAxis = axis.normalized;
            if (normalizedAxis.sqrMagnitude < 0.0001f) {
                basisX = Vector3.right;
                basisY = Vector3.up;
                return false;
            }

            basisX = Vector3.Cross(normalizedAxis, Vector3.up);
            if (basisX.sqrMagnitude < 0.0001f) {
                basisX = Vector3.Cross(normalizedAxis, Vector3.right);
            }

            if (basisX.sqrMagnitude < 0.0001f) {
                basisX = Vector3.right;
                basisY = Vector3.up;
                return false;
            }

            basisX.Normalize();
            basisY = Vector3.Cross(normalizedAxis, basisX).normalized;
            return true;
        }
    }
}
