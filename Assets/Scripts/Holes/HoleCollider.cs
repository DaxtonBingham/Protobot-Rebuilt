using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Serialization;

namespace Protobot {
    // Authoring adapter for existing prefabs and the single mouse-picking proxy.
    // Runtime connections and hole poses live in PartHoles/HoleRecord.
    public class HoleCollider : MonoBehaviour {
        public static int HOLE_COLLISIONS_LAYER => 11;
        public static int HOLE_COLLISIONS_MASK => 1 << HOLE_COLLISIONS_LAYER;
        public enum HoleType { Normal, Threaded, Clamp }
        public HoleData holeData;
        public HoleType holeType;
        public bool twoSided;
        public Action<HoleDetector> OnSetDetector;
        public HoleRecord Record { get; private set; }
        [SerializeField] internal bool IsPickingProxy;
        [SerializeField] private PartHoles recordOwner;
        [SerializeField] private int recordIndex = -1;
        private readonly List<HoleDetector> emptyDetectors = new List<HoleDetector>();
        public List<HoleDetector> detectors => Record != null ? Record.detectors : emptyDetectors;
        public bool IsOccupied => Record != null && Record.IsOccupied;
        public bool IsEmpty => Record == null || Record.IsEmpty;
        private void Awake() {
            // A part can be duplicated while the shared picking proxy is attached.
            // Its clone is transient and must never become another authored hole.
            if (IsPickingProxy) { Destroy(gameObject); return; }
            var collider = GetComponent<MeshCollider>();
            holeData = new HoleData {
                shape = collider != null ? collider.sharedMesh : null,
                depth = transform.localScale.z,
                size = new Vector2(transform.localScale.x, transform.localScale.y),
                part = transform.parent != null ? transform.parent.gameObject : null
            };
            holeData.Bind(transform);
            if (recordOwner != null && recordIndex >= 0 && recordIndex < recordOwner.Holes.Count)
                BindRecord(recordOwner.Holes[recordIndex]);
        }
        public void BindRecord(HoleRecord record) {
            if (Record != null) Record.OnSetDetector -= Notify;
            Record = record;
            recordOwner = record != null ? record.Owner : null;
            recordIndex = record != null ? record.Index : -1;
            if (Record != null) {
                holeData = Record.holeData; holeType = Record.holeType; twoSided = Record.Definition.twoSided;
                Record.OnSetDetector += Notify;
            }
        }
        private void Notify(HoleDetector detector) => OnSetDetector?.Invoke(detector);
        private void OnDestroy() { if (Record != null) Record.OnSetDetector -= Notify; }
        public void DetachAllDetectors() => Record?.DetachAllDetectors();
    }

    [Serializable]
    public class HoleData {
        public Mesh shape;
        public float depth;
        public Vector2 size;
        public GameObject part;
        [NonSerialized] private Transform source;
        [NonSerialized] private HoleRecord record;
        [SerializeField, FormerlySerializedAs("position")] private Vector3 storedPosition;
        [SerializeField, FormerlySerializedAs("rotation")] private Quaternion storedRotation;
        [SerializeField, FormerlySerializedAs("forward")] private Vector3 storedForward;
        internal void Bind(Transform value) { source = value; record = null; }
        internal void Bind(HoleRecord value) { record = value; source = null; }
        public Vector3 position {
            get { if (record != null) storedPosition = record.Position; else if (source != null) storedPosition = source.position; return storedPosition; }
            set => storedPosition = value;
        }
        public Quaternion rotation {
            get { if (record != null) storedRotation = record.Rotation; else if (source != null) storedRotation = source.rotation; return storedRotation; }
            set => storedRotation = value;
        }
        public Vector3 forward {
            get { if (record != null) storedForward = record.Rotation * Vector3.forward; else if (source != null) storedForward = source.forward; return storedForward; }
            set => storedForward = value;
        }
        public bool IsHighStrength() => size == new Vector2(.25f, .25f) && shape == HoleShapes.instance.GetShapeMesh("square");
    }
}
