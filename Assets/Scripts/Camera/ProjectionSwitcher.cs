using UnityEngine;
using UnityEngine.Events;

namespace Protobot {
    // Resolve after camera tweens and before reference cameras/retained rendering.
    [DefaultExecutionOrder(9000)]
    public class ProjectionSwitcher : MonoBehaviour {
        [Header("General")]
        public Camera cam;
        public bool isOrtho;

        [Header("Planes")]
        public GameObject orthoPlane;
        public GameObject persPlane;

        [Header("Switch Data")]
        public float switchDuration;
        public bool switching;

        [Space(10)]

        public UnityEvent OnSwitchToOrtho;
        public UnityEvent OnSwitchToPers;

        private PivotCamera pivot;
        private bool initialized, enableGrid = true;
        private float blend, blendStart, startedAt, duration, baseFar;
        private float lastFov, lastNear, lastFar, lastAspect, lastSize, lastBlend = -1;

        private void Initialize() {
            if (initialized) return;
            pivot = GetComponent<PivotCamera>();
            baseFar = cam.farClipPlane;
            blend = isOrtho ? 1 : 0;
            initialized = true;
        }

        void Start() { RefreshProjection(); }

        void LateUpdate() {
            if (switching) {
                float t = Mathf.Clamp01((Time.unscaledTime - startedAt) / duration);
                blend = Mathf.Lerp(blendStart, isOrtho ? 1 : 0, Mathf.SmoothStep(0, 1, t));
                if (t >= 1) switching = false;
            }
            RefreshProjection();
        }

        public void RefreshProjection() {
            Initialize();
            float tangent = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * .5f);
            float distance = pivot != null ? Mathf.Max(.001f, -pivot.focusDistance) : Mathf.Max(.001f, cam.orthographicSize / tangent);
            float size = distance * tangent;
            // Keep the focus plane visible when the narrower lens sits farther back.
            float far = Mathf.Max(baseFar, distance + baseFar * .25f);
            bool nativeOrtho = isOrtho && !switching;
            bool changed = cam.orthographic != nativeOrtho || lastFov != cam.fieldOfView || lastNear != cam.nearClipPlane
                || lastFar != far || lastAspect != cam.aspect || lastSize != size || lastBlend != blend;
            if (!changed) return;
            cam.orthographic = nativeOrtho;
            cam.orthographicSize = size;
            cam.farClipPlane = far;
            var perspective = Matrix4x4.Perspective(cam.fieldOfView, cam.aspect, cam.nearClipPlane, far);
            var ortho = Matrix4x4.Ortho(-size * cam.aspect, size * cam.aspect, -size, size, cam.nearClipPlane, far);
            if (!switching) cam.projectionMatrix = isOrtho ? ortho : perspective;
            else {
                // Equivalent homogeneous scale: both endpoints have w=1 at the
                // focus plane, so depth changes evenly through the transition.
                for (int i = 0; i < 16; i++) perspective[i] /= distance;
                cam.projectionMatrix = MatrixLerp(perspective, ortho, blend);
            }
            lastFov = cam.fieldOfView; lastNear = cam.nearClipPlane; lastFar = far;
            lastAspect = cam.aspect; lastSize = size; lastBlend = blend;
        }

        public static Matrix4x4 MatrixLerp(Matrix4x4 from, Matrix4x4 to, float time) {
            Matrix4x4 ret = new Matrix4x4();
            for (int i = 0; i < 16; i++)
                ret[i] = Mathf.Lerp(from[i], to[i], time);
            return ret;
        }

        public static Ray ScreenPointToRay(Camera camera, Vector3 screenPoint) {
            var projection = camera.projectionMatrix;
            // Unity's native ray method assumes a fully perspective or fully
            // orthographic camera. Unproject the actual matrix during the blend.
            if ((!camera.orthographic && projection.m33 == 0 && projection.m32 == -1)
                || (camera.orthographic && projection.m33 == 1 && projection.m32 == 0))
                return camera.ScreenPointToRay(screenPoint);
            var rect = camera.pixelRect;
            float x = (screenPoint.x - rect.x) / rect.width * 2 - 1;
            float y = (screenPoint.y - rect.y) / rect.height * 2 - 1;
            var inverse = projection.inverse;
            var nearPoint = inverse.MultiplyPoint(new Vector3(x, y, -1));
            var middlePoint = inverse.MultiplyPoint(new Vector3(x, y, 0));
            var world = camera.cameraToWorldMatrix;
            return new Ray(world.MultiplyPoint(nearPoint), world.MultiplyVector(middlePoint - nearPoint).normalized);
        }
    
        private void Switch(bool orthographic, float seconds) {
            Initialize();
            isOrtho = orthographic;
            blendStart = blend;
            duration = Mathf.Max(0, seconds);
            startedAt = Time.unscaledTime;
            switching = duration > 0 && blend != (isOrtho ? 1 : 0);
            if (!switching) blend = isOrtho ? 1 : 0;
            RefreshProjection();
            ApplyGrid();
        }

        public void SwitchToOrtho(float duration) {
            Switch(true, duration);
            OnSwitchToOrtho?.Invoke();
        }
        public void SwitchToPers(float duration) {
            Switch(false, duration);
            OnSwitchToPers?.Invoke();
        }

        public void ToggleProjection() {
            if (isOrtho) {
                SwitchToPers(switchDuration);
            }
            else {
                SwitchToOrtho(switchDuration);
            }
        }

        public void SetGridActive(bool value) {
            enableGrid = value;
            ApplyGrid();
        }

        private void ApplyGrid() {
            if (orthoPlane != null) orthoPlane.SetActive(enableGrid && isOrtho);
            if (persPlane != null) persPlane.SetActive(enableGrid && !isOrtho);
        }
    }
}
