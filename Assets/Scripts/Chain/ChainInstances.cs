using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot.ChainSystem {
    [DefaultExecutionOrder(10110)]
    public sealed class ChainInstances : MonoBehaviour {
        private sealed class MeshPart {
            public Mesh mesh, shadow;
            public Material material;
            public int submesh;
            public Matrix4x4 local;
            public float displayScale;
            public readonly List<Matrix4x4[]> batches = new List<Matrix4x4[]>();
            public readonly List<IndirectInstanceBatch> indirect = new List<IndirectInstanceBatch>();
            public bool indirectDirty = true;
        }
        private sealed class Link {
            public Matrix4x4 local, world;
            public Bounds bounds;
        }
        private const int BatchSize = 1023;
        private readonly List<MeshPart> parts = new List<MeshPart>();
        private readonly List<Link> links = new List<Link>();
        private readonly List<Link> candidates = new List<Link>();
        private SpatialGrid<Link> index = new SpatialGrid<Link>();
        private Bounds pickShape;
        private Bounds displayBounds;
        private Matrix4x4 rootMatrix;
        private int linkCount;
        private bool visible = true, outlined, nativeDirty = true;
        private Mesh nativeMesh;
        private MeshRenderer nativeRenderer;
        private BoxCollider pickProxy;
        public int LinkCount => linkCount;
        public long DisplayTriangleCount { get; private set; }
        // Export original link geometry, independent of display LOD, camera culling,
        // or the temporary combined renderer used for selection outlines.
        public void VisitExportGeometry(System.Action<int, Mesh, int, Material, Matrix4x4, Matrix4x4> visitor) {
            RefreshPose();
            foreach (var part in parts) for (int i = 0; i < linkCount; i++)
                visitor(i, part.mesh, part.submesh, part.material, links[i].world, part.local);
        }
        public void AppendSnapshot(Protobot.Rendering.RenderSceneSnapshot snapshot) {
            if (!visible || !isActiveAndEnabled) return;
            RefreshPose();
            var view = GetComponent<SavedObject>();
            string id = view != null && view.DocumentPart != null ? view.DocumentPart.Id : name;
            foreach (var part in parts) for (int i = 0; i < linkCount; i++)
                snapshot.Add(part.mesh, part.submesh, part.material, links[i].world * part.local, id, gameObject.layer, true, true);
        }
        public IReadOnlyList<Matrix4x4> LinkMatrices {
            get { RefreshPose(); var result = new Matrix4x4[linkCount]; for (int i = 0; i < linkCount; i++) result[i] = links[i].world; return result; }
        }
        public void Configure(GameObject prototype, Vector3 colliderSize) {
            if (parts.Count > 0) { pickShape = new Bounds(Vector3.zero, colliderSize); return; }
            var inverse = prototype.transform.worldToLocalMatrix;
            foreach (var filter in prototype.GetComponentsInChildren<MeshFilter>(true)) {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null) continue;
                var materials = renderer.sharedMaterials;
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount && sub < materials.Length; sub++) {
                    var material = materials[sub]; if (material == null) continue; material.enableInstancing = true;
                    parts.Add(new MeshPart { mesh = filter.sharedMesh, shadow = CompactShadowMesh.Get(filter.sharedMesh, material),
                        material = material, submesh = sub, local = inverse * filter.transform.localToWorldMatrix });
                }
            }
            pickShape = new Bounds(Vector3.zero, colliderSize);
            pickProxy = gameObject.AddComponent<BoxCollider>(); pickProxy.enabled = false;
        }
        public void SetMatrices(List<Matrix4x4> matrices) {
            rootMatrix = transform.localToWorldMatrix;
            var inverse = rootMatrix.inverse;
            linkCount = matrices.Count;
            while (links.Count < linkCount) links.Add(new Link());
            for (int i = 0; i < linkCount; i++) links[i].local = inverse * matrices[i];
            RebuildBatches();
            visible = true;
            UpdateNativeVisibility();
        }
        private void RefreshPose() {
            if (rootMatrix == transform.localToWorldMatrix) return;
            rootMatrix = transform.localToWorldMatrix;
            RebuildBatches();
            SceneActivity.Changed();
        }
        private void RebuildBatches() {
            nativeDirty = true;
            bool displayBoundsInitialized = false;
            index = new SpatialGrid<Link>();
            int batches = (linkCount + BatchSize - 1) / BatchSize;
            foreach (var part in parts) while (part.batches.Count < batches) part.batches.Add(new Matrix4x4[BatchSize]);
            foreach (var part in parts) part.indirectDirty = true;
            for (int i = 0; i < linkCount; i++) {
                var link = links[i]; link.world = rootMatrix * link.local;
                link.bounds = GeometryQuery.TransformBounds(pickShape, link.world); index.Set(link, link.bounds);
                foreach (var part in parts) {
                    var matrix = link.world * part.local; part.batches[i / BatchSize][i % BatchSize] = matrix;
                    var surfaceBounds = GeometryQuery.TransformBounds(part.mesh.bounds, matrix);
                    if (!displayBoundsInitialized) { displayBounds = surfaceBounds; displayBoundsInitialized = true; }
                    else displayBounds.Encapsulate(surfaceBounds);
                    // Frobenius norm is a conservative bound even under shear.
                    float scale = Mathf.Sqrt(matrix.GetColumn(0).sqrMagnitude + matrix.GetColumn(1).sqrMagnitude + matrix.GetColumn(2).sqrMagnitude);
                    part.displayScale = i == 0 ? scale : Mathf.Max(part.displayScale, scale);
                }
            }
        }
        public void SetVisible(bool value) {
            if (visible == value) return;
            visible = value; UpdateNativeVisibility(); SceneActivity.Changed();
        }
        public void SetOutlined(bool value) {
            if (outlined == value) return;
            outlined = value; UpdateNativeVisibility();
        }
        private void UpdateNativeVisibility() {
            bool native = outlined || !SystemInfo.supportsInstancing;
            if (native && visible) BuildNativeMesh();
            if (nativeRenderer != null) nativeRenderer.enabled = native && visible;
        }
        private void BuildNativeMesh() {
            if (!nativeDirty && nativeRenderer != null) return;
            if (nativeRenderer == null) {
                nativeRenderer = gameObject.AddComponent<MeshRenderer>();
                gameObject.AddComponent<MeshFilter>();
            }
            var materials = new List<Material>();
            var groups = new List<List<CombineInstance>>();
            foreach (var part in parts) {
                int group = materials.IndexOf(part.material);
                if (group < 0) { group = materials.Count; materials.Add(part.material); groups.Add(new List<CombineInstance>()); }
                for (int i = 0; i < linkCount; i++) groups[group].Add(new CombineInstance {
                    mesh = part.mesh, subMeshIndex = part.submesh, transform = links[i].local * part.local });
            }
            var next = new Mesh { name = "Selected chain geometry", indexFormat = IndexFormat.UInt32 };
            if (groups.Count == 1) next.CombineMeshes(groups[0].ToArray(), true, true, false);
            else {
                var submeshes = new CombineInstance[groups.Count];
                for (int i = 0; i < groups.Count; i++) {
                    var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(groups[i].ToArray(), true, true, false);
                    submeshes[i] = new CombineInstance { mesh = mesh, transform = Matrix4x4.identity };
                }
                next.CombineMeshes(submeshes, false, false, false);
                foreach (var submesh in submeshes) Destroy(submesh.mesh);
            }
            gameObject.GetComponent<MeshFilter>().sharedMesh = next;
            nativeRenderer.sharedMaterials = materials.ToArray();
            if (nativeMesh != null) Destroy(nativeMesh);
            nativeMesh = next; nativeDirty = false;
        }
        private void LateUpdate() {
            RefreshPose();
            if (!visible || linkCount == 0) return;
            if (outlined || !SystemInfo.supportsInstancing) { UpdateNativeVisibility(); return; }
            var camera = Camera.main;
            var presentation = camera != null ? camera.GetComponent<ViewportPresentation>() : null;
            if (IdleRendering.IsIdle || presentation != null && presentation.IsCached) return;
            int batchCount = (linkCount + BatchSize - 1) / BatchSize;
            DisplayTriangleCount = 0;
            foreach (var part in parts) {
              bool indirectReady = IndirectInstanceBatch.Supports(part.material, linkCount);
              if (indirectReady && (part.indirectDirty || part.indirect.Count < batchCount)) {
                  while (part.indirect.Count < batchCount) part.indirect.Add(new IndirectInstanceBatch());
                  for (int group = 0; group < batchCount; group++) part.indirect[group].Update(part.mesh, part.batches[group], Mathf.Min(BatchSize, linkCount - group * BatchSize));
                  part.indirectDirty = false;
              }
              var displayMesh = ChainDisplayMesh.Select(part.mesh, camera, displayBounds, part.displayScale);
              for (int group = 0; group < batchCount; group++) {
                int count = Mathf.Min(BatchSize, linkCount - group * BatchSize);
                DisplayTriangleCount += (long)(displayMesh.GetIndexCount(part.submesh) / 3) * count;
                bool compact = CompactShadowMesh.Enabled && part.shadow != null && part.shadow != part.mesh;
                bool gpu = indirectReady && part.indirect[group].Draw(displayMesh, part.submesh, part.material, camera, true, gameObject.layer);
                bool separateShadow = gpu || compact || displayMesh != part.mesh;
                if (!gpu) Graphics.DrawMeshInstanced(displayMesh, part.submesh, part.material, part.batches[group], count, null,
                    separateShadow ? ShadowCastingMode.Off : ShadowCastingMode.On, true, gameObject.layer);
                if (separateShadow) Graphics.DrawMeshInstanced(compact ? part.shadow : part.mesh, part.submesh, part.material, part.batches[group], count, null,
                    ShadowCastingMode.ShadowsOnly, false, gameObject.layer);
              }
            }
        }
        public bool Raycast(Ray ray, float maxDistance, out SceneHit hit) {
            hit = new SceneHit();
            if (!visible || !isActiveAndEnabled || gameObject.layer != 0) return false;
            RefreshPose(); index.Query(ray, maxDistance, candidates);
            bool found = false;
            foreach (var link in candidates) {
                var inverse = link.world.inverse; var localDirection = inverse.MultiplyVector(ray.direction);
                float scale = localDirection.magnitude;
                if (scale < 1e-12f) continue;
                var localRay = new Ray(inverse.MultiplyPoint3x4(ray.origin), localDirection);
                // Match the original BoxCollider pick volume, including rays that
                // start inside a link not selecting that link's exit surface.
                if (pickShape.Contains(localRay.origin)) continue;
                float entry = 0, exit = maxDistance * scale;
                if (!GeometryQuery.RayBounds(localRay, pickShape, ref entry, ref exit) || entry / scale > maxDistance) continue;
                maxDistance = entry / scale;
                var p = localRay.GetPoint(entry); var delta = p - pickShape.center;
                var relative = new Vector3(Mathf.Abs(delta.x) / pickShape.extents.x, Mathf.Abs(delta.y) / pickShape.extents.y, Mathf.Abs(delta.z) / pickShape.extents.z);
                int axis = relative.x >= relative.y && relative.x >= relative.z ? 0 : relative.y >= relative.z ? 1 : 2;
                var normal = Vector3.zero; normal[axis] = Mathf.Sign(delta[axis]);
                hit = new SceneHit { collider = pickProxy, point = ray.GetPoint(maxDistance), normal = inverse.transpose.MultiplyVector(normal).normalized, distance = maxDistance };
                found = true;
            }
            return found;
        }
        private void OnDestroy() {
            if (nativeMesh != null) Destroy(nativeMesh);
            foreach (var part in parts) foreach (var batch in part.indirect) batch.Dispose();
        }
    }
}
