using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot {
    // Transform uploads happen on edits. Visibility stays on the GPU: no
    // readback, per-part draw calls, or changed geometry are required.
    internal sealed class IndirectInstanceBatch : IDisposable {
        [StructLayout(LayoutKind.Sequential)]
        private struct Instance { public Matrix4x4 world, inverse; public Vector4 center, extents; }
        public static bool Enabled = true;
        public static int DrawCalls { get; private set; }
        private static ComputeShader culler;
        private static readonly Plane[] planes = new Plane[6];
        private static readonly Vector4[] planeVectors = new Vector4[6];
        private static int planeFrame = -1;
        private static Camera planeCamera;
        private ComputeBuffer instances, visible, arguments;
        private Instance[] upload;
        private readonly uint[] args = new uint[5];
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private int capacity, count;
        private Mesh argumentMesh;
        private int argumentSubmesh = -1;
        private Bounds bounds;
        private Matrix4x4 culledView;
        private bool visibilityDirty = true;

        public void Update(Mesh mesh, Matrix4x4[] matrices, int count) {
            this.count = count;
            visibilityDirty = true;
            if (count == 0) return;
            if (capacity < count) {
                Dispose(); capacity = Mathf.NextPowerOfTwo(count); upload = new Instance[capacity];
                instances = new ComputeBuffer(capacity, 160, ComputeBufferType.Structured);
                visible = new ComputeBuffer(capacity, 4, ComputeBufferType.Append);
                arguments = new ComputeBuffer(1, 20, ComputeBufferType.IndirectArguments);
                properties.SetBuffer("_ProtobotInstances", instances); properties.SetBuffer("_ProtobotVisible", visible);
                argumentMesh = null;
            }
            for (int i = 0; i < count; i++) {
                var worldBounds = GeometryQuery.TransformBounds(mesh.bounds, matrices[i]);
                upload[i] = new Instance { world = matrices[i], inverse = matrices[i].inverse, center = worldBounds.center, extents = worldBounds.extents };
                if (i == 0) bounds = worldBounds; else bounds.Encapsulate(worldBounds);
            }
            instances.SetData(upload, 0, 0, count);
        }
        public static bool Supports(Material material, int count) => Enabled && count >= 8 && SystemInfo.supportsComputeShaders && SystemInfo.supportsInstancing && SystemInfo.graphicsShaderLevel >= 45
            && (LightmapSettings.lightProbes == null || LightmapSettings.lightProbes.count == 0)
            && material != null && material.shader.name == "Protobot/Cached Standard" && material.renderQueue <= 2500;

        public bool Draw(Mesh mesh, int submesh, Material material, Camera camera, bool receiveShadows, int layer, LightProbeUsage probes = LightProbeUsage.BlendProbes) {
            if (count == 0 || !Supports(material, count) || camera == null) return false;
            if (planeFrame != Time.frameCount || planeCamera != camera) {
                GeometryUtility.CalculateFrustumPlanes(camera, planes);
                for (int i = 0; i < 6; i++) planeVectors[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
                planeFrame = Time.frameCount; planeCamera = camera;
            }
            // Most overview batches are wholly visible. Avoid a compute dispatch
            // for those, and reject wholly offscreen batches before submission.
            int visibility = 1;
            foreach (var plane in planes) {
                var normal = plane.normal;
                float radius = Mathf.Abs(normal.x) * bounds.extents.x + Mathf.Abs(normal.y) * bounds.extents.y + Mathf.Abs(normal.z) * bounds.extents.z;
                float distance = plane.GetDistanceToPoint(bounds.center);
                if (distance + radius < -.0001f) return true;
                if (distance - radius < .0001f) visibility = 0;
            }
            // Ordinary instancing is cheaper when every member is visible.
            // Reserve indirect submission for batches that benefit from culling.
            if (visibility == 1) return false;
            if (argumentMesh != mesh || argumentSubmesh != submesh) {
                args[0] = mesh.GetIndexCount(submesh); args[1] = 0; args[2] = mesh.GetIndexStart(submesh); args[3] = (uint)mesh.GetBaseVertex(submesh); args[4] = 0;
                arguments.SetData(args); argumentMesh = mesh; argumentSubmesh = submesh; visibilityDirty = true;
            }
            var currentView = camera.projectionMatrix * camera.worldToCameraMatrix;
            if (visibilityDirty || currentView != culledView) {
                if (culler == null) culler = Resources.Load<ComputeShader>("Rendering/InstanceVisibility");
                if (culler == null) return false;
                visible.SetCounterValue(0);
                culler.SetInt("_InstanceCount", count); culler.SetVectorArray("_FrustumPlanes", planeVectors);
                culler.SetBuffer(0, "_Instances", instances); culler.SetBuffer(0, "_Visible", visible);
                culler.Dispatch(0, (count + 63) / 64, 1, 1);
                ComputeBuffer.CopyCount(visible, arguments, 4);
                visibilityDirty = false; culledView = currentView;
            }
            Graphics.DrawMeshInstancedIndirect(mesh, submesh, material, bounds, arguments, 0, properties, ShadowCastingMode.Off, receiveShadows, layer, camera, probes);
            DrawCalls++;
            return true;
        }
        public void Dispose() {
            instances?.Release(); visible?.Release(); arguments?.Release();
            instances = visible = arguments = null; capacity = 0; argumentMesh = null;
        }
    }
}
