#if UNITY_EDITOR
using System;
using Protobot;
using UnityEditor;
using UnityEngine;

public static class DistanceScalerVerification {
    [MenuItem("Tools/Verify Transform Handle Scaling")]
    public static void Verify() {
        var cameraObject = new GameObject("Handle verification camera");
        var handleObject = new GameObject("Handle verification probe");
        var otherTarget = new GameObject("Handle verification fallback");
        int checks = 0;
        try {
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.pixelRect = new Rect(0, 0, 1600, 900);
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 10000;
            var scaler = handleObject.AddComponent<DistanceScaler>();
            scaler.target = camera.transform;
            float baseline = Mathf.Tan(30 * Mathf.Deg2Rad);
            foreach (float factor in new[] { .125f, .15f, .0045f }) {
                scaler.scaleFactor = factor;
                foreach (float fov in new[] { .5f, 5f, 35f, 60f, 90f, 120f, 179f }) {
                    float tangent = Mathf.Tan(fov * Mathf.Deg2Rad * .5f);
                    float focus = 24 * baseline / tangent;
                    float halfHeight = focus * tangent;
                    camera.fieldOfView = fov;
                    camera.orthographicSize = halfHeight;
                    var perspective = Matrix4x4.Perspective(fov, camera.aspect, .01f, 10000);
                    var orthographic = Matrix4x4.Ortho(-halfHeight * camera.aspect, halfHeight * camera.aspect,
                        -halfHeight, halfHeight, .01f, 10000);
                    var normalized = perspective;
                    for (int i = 0; i < 16; i++) normalized[i] /= focus;
                    foreach (float blend in new[] { 0f, .25f, .7f, 1f }) {
                        camera.orthographic = blend == 1;
                        camera.projectionMatrix = blend == 0 ? perspective : ProjectionSwitcher.MatrixLerp(normalized, orthographic, blend);
                        foreach (float depth in new[] { .4f, 1f, 2f }) {
                            handleObject.transform.position = new Vector3(halfHeight * .6f, -halfHeight * .3f, focus * depth);
                            handleObject.SendMessage("LateUpdate");
                            var origin = handleObject.transform.position;
                            float actual = Vector2.Distance(camera.WorldToScreenPoint(origin),
                                camera.WorldToScreenPoint(origin + camera.transform.up * handleObject.transform.localScale.y));
                            float expected = camera.pixelHeight * scaler.scaleFactor / (2 * baseline);
                            Require(Mathf.Abs(actual - expected) < .08f, "Handle pixel size: FOV=" + fov + " blend=" + blend + " depth=" + depth);
                            checks++;
                            handleObject.transform.hasChanged = false;
                            handleObject.SendMessage("LateUpdate");
                            Require(!handleObject.transform.hasChanged, "Stationary handle transform was rewritten");
                            checks++;
                        }
                    }
                }
            }
            scaler.target = otherTarget.transform;
            handleObject.transform.position = new Vector3(0, 0, 10);
            handleObject.SendMessage("LateUpdate");
            Require(Mathf.Abs(handleObject.transform.localScale.x - 10 * scaler.scaleFactor) < .00001f, "Non-camera target compatibility");
            checks++;
            scaler.target = null;
            var previous = handleObject.transform.localScale;
            handleObject.SendMessage("LateUpdate");
            Require(handleObject.transform.localScale.Equals(previous), "Missing target should retain scale");
            checks++;
            scaler.target = camera.transform;
            camera.projectionMatrix = new Matrix4x4();
            handleObject.SendMessage("LateUpdate");
            Require(handleObject.transform.localScale.Equals(previous), "Invalid projection should retain finite scale");
            checks++;
            Debug.Log("TRANSFORM_HANDLE_VERIFICATION passed " + checks + " checks");
        }
        finally {
            UnityEngine.Object.DestroyImmediate(handleObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(otherTarget);
        }
    }

    private static void Require(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
