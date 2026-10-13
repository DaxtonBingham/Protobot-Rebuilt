using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    [RequireComponent(typeof(Camera)), DefaultExecutionOrder(9100)]
    public class ReferenceCamera : MonoBehaviour {
        private new Camera camera;
        public Camera referenceCamera;

        [Header("Reference: ")]
        public bool projectionMatrix = true;
        public bool viewportRect = true;

        void Awake() {
            camera = GetComponent<Camera>();
        }

        void LateUpdate() {
            if (projectionMatrix) {
                if (camera.orthographic != referenceCamera.orthographic) camera.orthographic = referenceCamera.orthographic;
                if (camera.orthographicSize != referenceCamera.orthographicSize) camera.orthographicSize = referenceCamera.orthographicSize;
                if (camera.fieldOfView != referenceCamera.fieldOfView) camera.fieldOfView = referenceCamera.fieldOfView;
                if (camera.nearClipPlane != referenceCamera.nearClipPlane) camera.nearClipPlane = referenceCamera.nearClipPlane;
                if (camera.farClipPlane != referenceCamera.farClipPlane) camera.farClipPlane = referenceCamera.farClipPlane;
                if (camera.projectionMatrix != referenceCamera.projectionMatrix) camera.projectionMatrix = referenceCamera.projectionMatrix;
            }
            if (viewportRect && camera.rect != referenceCamera.rect) camera.rect = referenceCamera.rect;
        }
    }
}
