using UnityEngine;

namespace Protobot.ChainSystem {
    public class ChainEndpoint : MonoBehaviour {
        [SerializeField] private string socketId = "main";
        [SerializeField] private Vector3 localCenterOffset = Vector3.zero;
        [SerializeField] private Vector3 localAxisNormal = Vector3.forward;
        [SerializeField] private float pitchRadius = 0.5f;
        [SerializeField] private int toothCount = 0;
        [SerializeField] private bool autoConfigure = true;
        [SerializeField] private bool configured = false;

        public string SocketId => socketId;
        public int ToothCount => toothCount;
        public float PitchRadius => Mathf.Max(0.01f, pitchRadius);
        public Vector3 WorldCenter => transform.TransformPoint(localCenterOffset);
        public Vector3 WorldAxis => transform.TransformDirection(localAxisNormal).normalized;

        private void Awake() {
            if (autoConfigure) {
                AutoConfigureIfNeeded();
            }
        }

        public void AutoConfigureIfNeeded() {
            if (configured) {
                return;
            }

            ConfigureFromObject();
        }

        public void ConfigureFromObject() {
            Vector3 worldAxis = transform.forward;
            Vector3 worldCenter = transform.position;
            bool usedPrimaryHoleCenter = false;

            if (TryGetComponent(out PartData partData) && partData.primaryHole != null) {
                worldAxis = partData.primaryHole.transform.forward;
                worldCenter = partData.primaryHole.transform.position;
                usedPrimaryHoleCenter = true;
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

    }
}
