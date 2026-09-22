using UnityEngine;

namespace Protobot.ChainSystem {
    public class ChainEndpoint : MonoBehaviour {
        [SerializeField] private string socketId = "main";
        [SerializeField] private Vector3 localCenterOffset = Vector3.zero;
        [SerializeField] private Vector3 localAxisNormal = Vector3.forward;
        [SerializeField] private float pitchRadius = 0.5f;
        [SerializeField] private int toothCount = 0;
        [SerializeField] private bool guideEndpoint = false;
        [SerializeField] private bool autoConfigure = true;
        [SerializeField] private bool configured = false;

        public string SocketId {
            get {
                if (TryGetComponent(out ChainGuide guide)) {
                    return guide.SocketId;
                }

                return string.IsNullOrWhiteSpace(socketId) ? "main" : socketId;
            }
        }
        public int ToothCount => toothCount;
        public float PitchRadius => TryGetComponent(out ChainGuide guide) ? guide.Radius : Mathf.Max(0.01f, pitchRadius);
        public Vector3 WorldCenter => TryGetComponent(out ChainGuide guide) ? guide.WorldCenter : transform.TransformPoint(localCenterOffset);
        public Vector3 WorldAxis => TryGetComponent(out ChainGuide guide) ? guide.WorldAxis : transform.TransformDirection(localAxisNormal).normalized;
        public bool IsGuideEndpoint => TryGetComponent(out ChainGuide _) || guideEndpoint;
        public bool ParticipatesInStandardCompatibility => !IsGuideEndpoint;

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
            if (TryGetComponent(out ChainGuide guide)) {
                ConfigureFromGuide(guide);
                return;
            }

            guideEndpoint = false;
            socketId = "main";

            Vector3 worldAxis = transform.forward;
            Vector3 worldCenter = transform.position;
            bool usedPrimaryHoleCenter = false;

            if (TryGetComponent(out PartData partData) && partData.PrimaryHole != null) {
                worldAxis = partData.PrimaryHole.forward;
                worldCenter = partData.PrimaryHole.position;
                usedPrimaryHoleCenter = true;
            }

            if (worldAxis.sqrMagnitude < 0.0001f) {
                worldAxis = Vector3.forward;
            }
            localAxisNormal = transform.InverseTransformDirection(worldAxis).normalized;

            toothCount = ChainSprocketUtility.ParseToothCount(gameObject);

            if (!usedPrimaryHoleCenter) {
                if (TryGetComponent(out Renderer singleRenderer)) {
                    worldCenter = singleRenderer.bounds.center;
                }
                else {
                    Renderer[] renderers = GetComponentsInChildren<Renderer>();
                    if (renderers.Length > 0) {
                        Bounds bounds = renderers[0].bounds;
                        for (int i = 1; i < renderers.Length; i++) {
                            bounds.Encapsulate(renderers[i].bounds);
                        }
                        worldCenter = bounds.center;
                    }
                    else {
                        worldCenter = transform.position;
                    }
                }
            }

            localCenterOffset = transform.InverseTransformPoint(worldCenter);
            pitchRadius = ChainSprocketUtility.EstimateRadius(gameObject, worldAxis, worldCenter);
            configured = true;
        }

        public void ConfigureFromGuide(ChainGuide guide) {
            if (guide == null) {
                return;
            }

            guide.AutoConfigureIfNeeded();

            guideEndpoint = true;
            socketId = guide.SocketId;
            localCenterOffset = transform.InverseTransformPoint(guide.WorldCenter);

            Vector3 worldAxis = guide.WorldAxis;
            if (worldAxis.sqrMagnitude < 0.0001f) {
                worldAxis = transform.forward.sqrMagnitude > 0.0001f ? transform.forward : Vector3.forward;
            }

            localAxisNormal = transform.InverseTransformDirection(worldAxis).normalized;
            pitchRadius = guide.Radius;
            toothCount = 0;
            configured = true;
        }
    }
}
