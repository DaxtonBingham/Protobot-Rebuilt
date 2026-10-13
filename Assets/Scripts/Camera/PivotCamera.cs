using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;

namespace Protobot {
    public class PivotCamera : MonoBehaviour {
        public static PivotCamera Main;
        [SerializeField] public new Camera camera;

        //Moving values
        public bool moving => DOTween.IsTweening(transform);

        private Tweener orbitTween;
        public bool orbiting => orbitTween != null ? orbitTween.active : false;

        private Tweener zoomTween;
        public bool zooming => zoomTween != null ? zoomTween.active : false;

        public Vector3 cameraPosition => camera.transform.localPosition;
        public Vector3 focusPosition => transform.position;
        public float focusDistance => cameraPosition.z;
        // CameraData.zoom remains in the original 60-degree camera's units so
        // old PBBs retain their framing and new saves still open in older builds.
        public float LensTangent => Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
        private const float LegacyPerspectiveTangent = .5773502692f;
        public float DistanceFromSavedZoom(float zoom, bool orthographic) {
            return zoom * (orthographic ? .5f : LegacyPerspectiveTangent) / LensTangent;
        }
        public float SavedZoom(bool orthographic) {
            return focusDistance * LensTangent / (orthographic ? .5f : LegacyPerspectiveTangent);
        }
        public Vector3 lookAngle => transform.eulerAngles;
        public Vector3 orbitRot;

        private Vector3 panPos = Vector3.zero;


        [CacheComponent] private ProjectionSwitcher projectionSwitcher;
        private IPivotCameraInput[] inputs;
        private float zoomDistance;
        private readonly List<Tween> rotations = new List<Tween>(3);
        private readonly List<Tween> positions = new List<Tween>(3);
        private readonly List<Tween> distances = new List<Tween>(3);
        public bool invertZoom = false;
        public float snapSensitivity;

        void Start() {
            orbitRot = transform.eulerAngles;

            var mainCams = GameObject.FindGameObjectsWithTag("MainCamera");

            foreach (GameObject cam in mainCams) {
                if (cam.GetComponent<PivotCamera>() != null) {
                    Main = cam.GetComponent<PivotCamera>();
                    break;
                }
            }

            if (Main == null) Debug.LogError("MAIN PIVOT CAMERA REFERENCE MISSING");

            SetupInputs();

            zoomDistance = -cameraPosition.z;
            SetFieldOfView(CameraPreferences.FieldOfView);
        }

        void Update() {
            if (projectionSwitcher != null && projectionSwitcher.isOrtho) {
                OrbitSnap();
            }
        }

        private void LateUpdate() {
            // Let the new tween sample exactly the same starting pose as before.
            // Older tweens write earlier in the same channel and are then fully
            // overwritten, so they can be retired after that first update.
            RetireOverwritten(rotations);
            RetireOverwritten(positions);
            RetireOverwritten(distances);
        }

        private static void RetireOverwritten(List<Tween> channel) {
            if (channel.Count < 2) return;
            var newest = channel[channel.Count - 1];
            if (!newest.IsActive() || !newest.IsInitialized()) return;
            for (int i = 0; i < channel.Count - 1; i++)
                if (channel[i].IsActive()) channel[i].Kill();
            channel.Clear();
            channel.Add(newest);
        }

        private static void Stop(List<Tween> channel) {
            foreach (var tween in channel) if (tween.IsActive()) tween.Kill();
            channel.Clear();
        }

        private void OnDestroy() { Stop(rotations); Stop(positions); Stop(distances); }

        public void SetupInputs() {
            inputs = GetComponents<IPivotCameraInput>();

            foreach (IPivotCameraInput input in inputs) {
                input.updateOrbit += OrbitControl;
                input.updatePan += PanControl;
                input.updateZoom += ZoomControl;
            }
        }

        public void SetTransform(Vector3 newPos, Vector3 newAngle, float newDistance) {
            Stop(rotations); Stop(positions); Stop(distances);
            transform.position = newPos;

            transform.eulerAngles = newAngle;
            orbitRot = transform.eulerAngles;

            Vector3 newCamPos = camera.transform.localPosition;
            newCamPos.z = newDistance;
            zoomDistance = -newDistance;

            camera.transform.localPosition = newCamPos;

            panPos = transform.position;
        }

        public void MoveFocusPosition(Vector3 newFocusPos) {
            panPos = newFocusPos;
            positions.Add(transform.DOMove(newFocusPos, 0.4f));
        }

        public void SetFieldOfView(float value) {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = CameraPreferences.DefaultFieldOfView;
            if (camera.fieldOfView == value) return;
            // Retain the visible size at the pivot while changing perspective.
            // A pending zoom must not restore a distance from the previous lens.
            Stop(distances);
            float previousTangent = LensTangent;
            camera.fieldOfView = value;
            var position = cameraPosition;
            position.z *= previousTangent / LensTangent;
            camera.transform.localPosition = position;
            zoomDistance = -position.z;
            var projection = projectionSwitcher != null ? projectionSwitcher : GetComponent<ProjectionSwitcher>();
            if (projection != null) projection.RefreshProjection();
            IdleRendering.Changed();
        }

        //ZOOM
        public void ZoomControl(float input) {
            input *= (zoomDistance * 0.3f);
            
            int inverted = 1;
            if (invertZoom) inverted = -1;

            zoomDistance += -input * inverted;
            float lensScale = LegacyPerspectiveTangent / LensTangent;
            zoomDistance = Mathf.Clamp(zoomDistance, 0.5f * lensScale, 1000f * lensScale);

            Vector3 newPos = cameraPosition;
            newPos.z = -zoomDistance;
            zoomTween = camera.transform.DOLocalMove(newPos, 0.4f);
            distances.Add(zoomTween);
        }
        
        public void SetInvertZoom(bool value) {
            invertZoom = value;
        }


        //ORBIT
        public void OrbitControl(Vector2 orbitValue) {
            orbitRot += new Vector3(orbitValue.x, orbitValue.y, 0);

            orbitTween = transform.DORotateQuaternion(Quaternion.Euler(orbitRot), 0.4f);
            rotations.Add(orbitTween);
        }

        public void OrbitSnap() {
            Vector3 snapVector = lookAngle.Round(90);
            if (Vector3.Distance(snapVector, lookAngle) < snapSensitivity && !moving)
                rotations.Add(transform.DOLocalRotate(snapVector, 0.25f));
        }

        //PAN
        public void PanControl(Vector2 panValue) {
            if (panValue == Vector2.zero) return;
            // Preserve screen-space pan speed when the same framing uses a
            // longer camera distance with a narrower lens.
            float zoomFactor = zoomDistance * LensTangent / LegacyPerspectiveTangent / 5;
            panPos += ((transform.right * -panValue.x) + (transform.up * -panValue.y)) * zoomFactor;
            positions.Add(transform.DOMove(panPos, 0.25f));
        }
    }
}
