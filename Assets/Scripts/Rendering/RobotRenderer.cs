using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot {
    // Existing root renderers stay as editing/outline adapters. Steady rendering
    // uses cached instance batches; only changed parts update their matrix slots.
    [DefaultExecutionOrder(10100)]
    public sealed class RobotRenderer : MonoBehaviour {
        private sealed class Item {
            public RobotPart part;
            public MeshRenderer renderer;
            public Mesh mesh;
            public Material material;
            public Matrix4x4 matrix;
            public Batch batch;
            public bool outlined;
            public ShadowRenderProxy shadow;
        }
        private struct Key : IEquatable<Key> {
            public Mesh mesh; public Material material; public Vector3Int cell;
            public int layer; public ShadowCastingMode shadows; public bool receive;
            public LightProbeUsage probes;
            public bool Equals(Key b) => mesh == b.mesh && material == b.material && cell == b.cell && layer == b.layer && shadows == b.shadows && receive == b.receive && probes == b.probes;
            public override bool Equals(object other) => other is Key key && Equals(key);
            public override int GetHashCode() => (((mesh.GetInstanceID() * 397 ^ material.GetInstanceID()) * 397 ^ cell.GetHashCode()) * 397 ^ layer) * 397 ^ (int)shadows ^ (receive ? 1024 : 0) ^ ((int)probes << 12);
        }
        private sealed class Batch {
            public Key key;
            public Mesh shadowMesh;
            public bool nativeShadow;
            public readonly List<Item> items = new List<Item>();
            public readonly List<Matrix4x4[]> matrices = new List<Matrix4x4[]>();
            public readonly List<int> counts = new List<int>();
            public readonly List<IndirectInstanceBatch> indirect = new List<IndirectInstanceBatch>();
            private bool indirectDirty = true;
            public bool dirty = true;
            public void Refresh() {
                if (!dirty) return;
                dirty = false; indirectDirty = true; counts.Clear();
                int n = 0;
                foreach (var item in items) {
                    if (item.outlined || item.renderer == null || !item.renderer.enabled || !item.part.Exists) continue;
                    int group = n / 1023, slot = n % 1023;
                    if (group == matrices.Count) matrices.Add(new Matrix4x4[1023]);
                    matrices[group][slot] = item.matrix;
                    if (slot == 0) counts.Add(0);
                    counts[group]++; n++;
                }
            }
            public void Dispose() { foreach (var batch in indirect) batch.Dispose(); indirect.Clear(); }
            public void Draw(Camera camera) {
                Refresh();
                if (key.mesh == null || key.material == null) return;
                bool indirectReady = IndirectInstanceBatch.Supports(key.material, items.Count);
                if (indirectReady && (indirectDirty || indirect.Count < counts.Count)) {
                    while (indirect.Count < counts.Count) indirect.Add(new IndirectInstanceBatch());
                    for (int i = 0; i < counts.Count; i++) indirect[i].Update(key.mesh, matrices[i], counts[i]);
                    indirectDirty = false;
                }
                for (int i = 0; i < counts.Count; i++) {
                    bool nativeShadow = UseNativeShadows && this.nativeShadow;
                    bool compact = !nativeShadow && CompactShadowMesh.Enabled && shadowMesh != null && shadowMesh != key.mesh && key.shadows == ShadowCastingMode.On;
                    bool gpu = indirectReady && indirect[i].Draw(key.mesh, 0, key.material, camera, key.receive, key.layer, key.probes);
                    if (!gpu) Graphics.DrawMeshInstanced(key.mesh, 0, key.material, matrices[i], counts[i], null,
                        compact || nativeShadow ? ShadowCastingMode.Off : key.shadows, key.receive, key.layer, null, key.probes);
                    if (compact) Graphics.DrawMeshInstanced(shadowMesh, 0, key.material, matrices[i], counts[i], null, ShadowCastingMode.ShadowsOnly, false, key.layer, null, key.probes);
                    else if (gpu && !nativeShadow && key.shadows != ShadowCastingMode.Off)
                        Graphics.DrawMeshInstanced(key.mesh, 0, key.material, matrices[i], counts[i], null, ShadowCastingMode.ShadowsOnly, false, key.layer, null, key.probes);
                }
            }
        }
        private static RobotRenderer instance;
        private readonly Dictionary<RobotPart, Item> items = new Dictionary<RobotPart, Item>();
        private readonly Dictionary<Key, Batch> batches = new Dictionary<Key, Batch>();
        private readonly HashSet<RobotPart> pending = new HashSet<RobotPart>();
        private readonly List<RobotPart> work = new List<RobotPart>();
        public static bool Enabled = true;
        public static float CellSize = 0;
        public static bool UseNativeShadows = true;
        public static uint NativeShadowIndexThreshold = 0;
        public static bool UseNativeColorCulling = false;
        public static int ManagedPartCount => instance != null ? instance.items.Count : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { instance = null; Enabled = true; }
        [RuntimeInitializeOnLoadMethod]
        private static void Initialize() {
            if (instance != null) return;
            var obj = new GameObject("Robot renderer"); DontDestroyOnLoad(obj); instance = obj.AddComponent<RobotRenderer>();
        }
        private void OnEnable() {
            RobotDocument.Changed += OnPartChanged;
            var all = RobotDocument.ActiveSnapshot(out int count);
            for (int i = 0; i < count; i++) pending.Add(all[i]);
        }
        private void OnDisable() {
            RobotDocument.Changed -= OnPartChanged;
            foreach (var item in items.Values) {
                if (item.renderer != null) { item.renderer.forceRenderingOff = false; item.renderer.shadowCastingMode = item.batch.key.shadows; }
                DestroyShadow(item.shadow);
            }
            foreach (var batch in batches.Values) batch.Dispose();
            items.Clear(); batches.Clear(); pending.Clear();
        }
        private void OnPartChanged(RobotPart part, PartChange change) => pending.Add(part);
        private static Vector3Int Cell(Vector3 p) => CellSize <= 0 ? Vector3Int.zero : new Vector3Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize), Mathf.FloorToInt(p.z / CellSize));

        private void Refresh(RobotPart part) {
            bool outlined = false;
            ShadowRenderProxy shadow = null;
            if (items.TryGetValue(part, out var old)) {
                shadow = old.shadow;
                outlined = old.outlined;
                old.batch.items.Remove(old); old.batch.dirty = true;
                if (old.renderer != null) { old.renderer.forceRenderingOff = false; old.renderer.shadowCastingMode = old.batch.key.shadows; }
                items.Remove(part);
                if (old.batch.items.Count == 0) { old.batch.Dispose(); batches.Remove(old.batch.key); }
            }
            if (!Enabled || !SystemInfo.supportsInstancing || !part.Exists || part.View == null) { DestroyShadow(shadow); return; }
            var renderer = part.View.CachedRenderer as MeshRenderer;
            var filter = part.View.GetComponent<MeshFilter>();
            if (renderer == null || filter == null || filter.sharedMesh == null || !renderer.enabled || renderer.HasPropertyBlock()) { DestroyShadow(shadow); return; }
            var material = renderer.sharedMaterial; var mesh = filter.sharedMesh;
            // Keep special shaders, transparency, multi-material objects, baked
            // lightmaps and probe volumes on their original renderer path.
            if (material == null || !material.enableInstancing || material.renderQueue > 2500 || mesh.subMeshCount != 1
                || renderer.sharedMaterials.Length != 1 || renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 0xFFFE
                || renderer.lightProbeUsage == LightProbeUsage.UseProxyVolume || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) { DestroyShadow(shadow); return; }
            var existingOutline = renderer.GetComponent<EPOOutline.Outlinable>();
            outlined |= existingOutline != null && existingOutline.enabled;
            var matrix = renderer.localToWorldMatrix;
            var key = new Key { mesh = mesh, material = material, cell = Cell(renderer.bounds.center), layer = renderer.gameObject.layer,
                shadows = renderer.shadowCastingMode, receive = renderer.receiveShadows, probes = renderer.lightProbeUsage };
            if (!batches.TryGetValue(key, out var batch)) {
                // Native cascade culling avoids drawing every member of a color
                // batch into each shadow cascade. Keep exact geometry and bias.
                batch = new Batch { key = key, shadowMesh = CompactShadowMesh.Get(mesh, material),
                    nativeShadow = key.shadows == ShadowCastingMode.On && mesh.GetIndexCount(0) >= NativeShadowIndexThreshold };
                batches.Add(key, batch);
            }
            var item = new Item { part = part, renderer = renderer, mesh = mesh, material = material, matrix = matrix, batch = batch, outlined = outlined };
            if (UseNativeShadows && batch.nativeShadow) {
                if (shadow == null) shadow = ShadowRenderProxy.Create(part);
                shadow.Configure(batch.shadowMesh, material, key.layer, key.probes, !outlined);
                item.shadow = shadow;
            } else DestroyShadow(shadow);
            batch.items.Add(item); batch.dirty = true; items.Add(part, item);
            renderer.forceRenderingOff = !UseNativeColorCulling && !outlined;
            if (UseNativeColorCulling && item.shadow != null && !outlined) renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        private static void DestroyShadow(ShadowRenderProxy shadow) { if (shadow != null) Destroy(shadow.gameObject); }

        public static void SetOutlined(GameObject obj, bool outlined) {
            if (instance == null) return;
            var view = obj.GetComponent<SavedObject>();
            if (view == null || view.DocumentPart == null || !instance.items.TryGetValue(view.DocumentPart, out var item)) return;
            if (item.outlined == outlined) return;
            item.outlined = outlined; item.renderer.forceRenderingOff = !UseNativeColorCulling && !outlined; item.batch.dirty = true;
            if (UseNativeColorCulling) item.renderer.shadowCastingMode = item.shadow != null && !outlined ? ShadowCastingMode.Off : item.batch.key.shadows;
            if (item.shadow != null) item.shadow.gameObject.SetActive(!outlined);
        }
        public static void Invalidate(GameObject obj) {
            var view = obj.GetComponent<SavedObject>();
            if (instance != null && view != null && view.DocumentPart != null) instance.pending.Add(view.DocumentPart);
            SceneActivity.Changed();
        }
        private void LateUpdate() {
            if (!Enabled) { if (items.Count > 0) { OnDisable(); OnEnable(); } return; }
            work.Clear(); work.AddRange(pending); pending.Clear();
            foreach (var part in work) Refresh(part);
            // Native color submission retains Unity's per-instance visibility and
            // cached instancing. The document owns shared meshes and shadow data.
            if (UseNativeColorCulling) return;
            var camera = Camera.main;
            var presentation = camera != null ? camera.GetComponent<ViewportPresentation>() : null;
            if (IdleRendering.IsIdle || presentation != null && presentation.IsCached) return;
            foreach (var batch in batches.Values) batch.Draw(camera);
        }
    }
}
