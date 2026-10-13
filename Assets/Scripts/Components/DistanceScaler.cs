using UnityEngine;

namespace Protobot {
    // Size handles after camera tweens, projection blending and reference cameras.
    [DefaultExecutionOrder(9200)]
    public class DistanceScaler : MonoBehaviour {
        public Transform target;
        public float scaleFactor = 0.15f;

        // Preserve the original handle size at the default 60-degree lens.
        private const float DefaultLensTangent = .5773502692f;
        private Transform cachedTarget;
        private Camera targetCamera;

        private void LateUpdate() {
            if (target == null) return;
            if (cachedTarget != target) {
                cachedTarget = target;
                targetCamera = target.GetComponent<Camera>();
            }

            float size;
            if (targetCamera != null) {
                // Clip w / vertical projection scale is the visible half-height
                // at the handle's depth, including blended/orthographic views.
                // Camera depth also avoids handles growing toward screen edges.
                var projection = targetCamera.projectionMatrix;
                var view = targetCamera.worldToCameraMatrix.MultiplyPoint3x4(transform.position);
                float w = projection.m30 * view.x + projection.m31 * view.y
                    + projection.m32 * view.z + projection.m33;
                size = Mathf.Abs(w / projection.m11) * scaleFactor / DefaultLensTangent;
            }
            else {
                size = Vector3.Distance(transform.position, target.position) * scaleFactor;
            }

            if (float.IsNaN(size) || float.IsInfinity(size)) return;
            var scale = Vector3.one * size;
            if (!transform.localScale.Equals(scale)) transform.localScale = scale;
        }
    }
}
