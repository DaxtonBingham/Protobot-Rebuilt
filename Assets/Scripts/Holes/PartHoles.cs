using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    [Serializable]
    public sealed class HoleDefinition {
        public Mesh mesh;
        public Matrix4x4 localMatrix;
        public Quaternion localRotation;
        public Vector3 size;
        public HoleCollider.HoleType type;
        public bool twoSided;
        public bool convex;
        public bool enabled = true;
    }

    // A hole is a value in its owner's coordinate system, not a physics body.
    public sealed class HoleRecord {
        public PartHoles Owner { get; private set; }
        public int Index { get; private set; }
        public HoleDefinition Definition { get; private set; }
        public readonly HoleData holeData;
        public readonly List<HoleDetector> detectors = new List<HoleDetector>(2);
        public event Action<HoleDetector> OnSetDetector;
        public HoleCollider.HoleType holeType => Definition.type;
        public bool IsOccupied => detectors.Count >= (Definition.twoSided ? 2 : 1);
        public bool IsEmpty => detectors.Count == 0;
        private bool retired;
        public bool IsActive => !retired && Owner != null && Owner.isActiveAndEnabled && Definition.enabled && !Owner.gameObject.IsDeleted();
        public Matrix4x4 WorldMatrix { get; internal set; }
        public Bounds Bounds { get; internal set; }
        internal QueryMesh QueryMesh { get; private set; }

        internal HoleRecord(PartHoles owner, HoleDefinition definition, int index) {
            Owner = owner; Definition = definition; Index = index;
            QueryMesh = Protobot.QueryMesh.For(definition.mesh);
            holeData = new HoleData { shape = definition.mesh, size = new Vector2(definition.size.x, definition.size.y), depth = definition.size.z, part = owner.gameObject };
            holeData.Bind(this);
            Refresh(owner.transform.localToWorldMatrix);
        }
        internal void Refresh(Matrix4x4 ownerMatrix) {
            WorldMatrix = ownerMatrix * Definition.localMatrix;
            Bounds = GeometryQuery.TransformBounds(Definition.mesh.bounds, WorldMatrix);
        }
        public Vector3 Position => Owner != null ? Owner.transform.localToWorldMatrix.MultiplyPoint3x4(Definition.localMatrix.GetColumn(3)) : WorldMatrix.GetColumn(3);
        public Quaternion Rotation => Owner != null ? Owner.transform.rotation * Definition.localRotation : Quaternion.identity;
        public bool IsOccupiedBy(HoleDetector detector) => detectors.Contains(detector);
        public bool AddDetector(HoleDetector detector) {
            if (!IsActive) return false;
            if (IsOccupiedBy(detector)) return true;
            if (IsOccupied) return false;
            detectors.Add(detector); OnSetDetector?.Invoke(detector); return true;
        }
        public void RemoveDetector(HoleDetector detector) {
            if (detectors.Remove(detector)) { OnSetDetector?.Invoke(detectors.Count > 0 ? detectors[0] : null); HoleWorld.Released(this); }
        }
        public void DetachAllDetectors() {
            while (detectors.Count > 0) {
                var detector = detectors[detectors.Count - 1];
                if (detector != null) detector.RemoveHole(this); else detectors.RemoveAt(detectors.Count - 1);
            }
        }
        internal void Retire() { retired = true; DetachAllDetectors(); }
    }

    public sealed class PartHoles : MonoBehaviour {
        [SerializeField] private List<HoleDefinition> definitions = new List<HoleDefinition>();
        private readonly List<HoleRecord> holes = new List<HoleRecord>();
        private RobotPart documentPart;
        private bool initialized;
        public IReadOnlyList<HoleRecord> Holes { get { Initialize(); return holes; } }
        internal Bounds IndexedBounds;
        internal bool WasIndexed;
        internal int IndexedCount;
        private void Awake() => Initialize();
        private void OnEnable() { Initialize(); BindDocument(); HoleWorld.Register(this); }
        private void Start() { BindDocument(); HoleWorld.MarkDirty(this); }
        private void OnDisable() { HoleWorld.Unregister(this); foreach (var hole in holes) hole.DetachAllDetectors(); }
        private void OnDestroy() { if (documentPart != null) documentPart.Changed -= OnPartChanged; HoleWorld.Unregister(this); }
        private void Initialize() {
            if (initialized) return;
            initialized = true;
            for (int i = 0; i < definitions.Count; i++) if (definitions[i].mesh != null) holes.Add(new HoleRecord(this, definitions[i], holes.Count));
        }
        internal void BindDocument() {
            var view = GetComponent<SavedObject>();
            var current = view != null ? view.DocumentPart : null;
            if (documentPart == current) return;
            if (documentPart != null) documentPart.Changed -= OnPartChanged;
            documentPart = current;
            if (documentPart != null) documentPart.Changed += OnPartChanged;
        }
        private void OnPartChanged(RobotPart part, PartChange change) {
            if ((change & (PartChange.Pose | PartChange.Geometry | PartChange.Existence)) != 0) HoleWorld.MarkDirty(this);
        }
        public static PartHoles GetOrCreate(GameObject owner) => owner.GetComponent<PartHoles>() ?? owner.AddComponent<PartHoles>();
        public HoleRecord Add(HoleDefinition definition) {
            Initialize(); definitions.Add(definition);
            var hole = new HoleRecord(this, definition, holes.Count); holes.Add(hole);
            HoleWorld.MarkDirty(this); return hole;
        }
        public void Clear() {
            HoleWorld.Unregister(this);
            foreach (var hole in holes) hole.Retire();
            holes.Clear(); definitions.Clear();
            if (isActiveAndEnabled) HoleWorld.Register(this);
        }
        public static HoleRecord AddTemplate(GameObject owner, HoleCollider template, Vector3 localPosition, Quaternion localRotation) {
            return GetOrCreate(owner).Add(new HoleDefinition {
                mesh = template.GetComponent<MeshCollider>().sharedMesh,
                localMatrix = Matrix4x4.TRS(localPosition, localRotation, template.transform.localScale),
                localRotation = localRotation, size = template.transform.localScale,
                type = template.holeType, twoSided = template.twoSided, convex = template.GetComponent<MeshCollider>().convex
            });
        }
        // Prefabs remain readable by the original editor. At runtime their authoring
        // components are collapsed once; only optional visible insert hosts survive.
        public static void Compact(GameObject owner) {
            var authoring = owner.GetComponentsInChildren<HoleCollider>(true);
            PartHoles collection = owner.GetComponent<PartHoles>();
            foreach (var source in authoring) {
                if (source.Record != null || source.IsPickingProxy) continue;
                var meshCollider = source.GetComponent<MeshCollider>();
                if (meshCollider == null || meshCollider.sharedMesh == null) continue;
                if (collection == null) collection = GetOrCreate(owner);
                var definition = new HoleDefinition {
                    mesh = meshCollider.sharedMesh,
                    localMatrix = owner.transform.worldToLocalMatrix * source.transform.localToWorldMatrix,
                    localRotation = Quaternion.Inverse(owner.transform.rotation) * source.transform.rotation,
                    size = source.transform.localScale, type = source.holeType, twoSided = source.twoSided, convex = meshCollider.convex,
                    enabled = source.gameObject.activeSelf
                };
                var record = collection.Add(definition);
                source.BindRecord(record);
                foreach (var motor in owner.GetComponentsInChildren<Motor>(true)) motor.BindHole(source, record.Index);
                foreach (var data in owner.GetComponentsInChildren<PartData>(true)) data.BindHole(source, collection, record.Index);
                meshCollider.enabled = false;
                if (source.GetComponent<HoleInsert>() == null) Destroy(source.gameObject);
                else Destroy(meshCollider);
            }
            if (collection != null) collection.BindDocument();
        }
    }
}
