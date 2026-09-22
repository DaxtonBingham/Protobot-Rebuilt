using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    public class HoleDetector : MonoBehaviour {
        public readonly HashSet<HoleRecord> holes = new HashSet<HoleRecord>();
        [SerializeField] private HoleCollider.HoleType targetHoleType;
        [SerializeField] private float capsuleLength, capsuleRadius = .01f;
        private readonly List<HoleRecord> scratch = new List<HoleRecord>();
        private RobotPart owner;
        private Vector3 capsuleA, capsuleB;
        private float worldRadius;
        private int targetCount;
        private bool added, removed, addedTarget, removedTarget;
        internal Bounds QueryBounds { get; private set; }
        public bool TargetHoleFound { get { HoleWorld.Flush(); return targetCount > 0; } }
        public int TargetHoleCount { get { HoleWorld.Flush(); return targetCount; } }
        public Action OnAddHole, OnAddTargetHole, OnRemoveHole, OnRemoveTargetHole;

        private void Awake() {
            var collider = GetComponent<CapsuleCollider>();
            if (collider != null) { capsuleLength = collider.height; capsuleRadius = collider.radius; collider.enabled = false; Destroy(collider); }
            var body = GetComponent<Rigidbody>(); if (body != null) Destroy(body);
        }
        private void Start() { BindOwner(); HoleWorld.MarkDirty(this); }
        private void OnEnable() { BindOwner(); HoleWorld.Register(this); }
        private void OnDisable() { HoleWorld.Unregister(this); RemoveAll(); }
        private void OnDestroy() { if (owner != null) owner.Changed -= OnPartChanged; HoleWorld.Unregister(this); RemoveAll(); }
        private void BindOwner() {
            var view = GetComponentInParent<SavedObject>(); var part = view != null ? view.DocumentPart : null;
            if (owner == part) return;
            if (owner != null) owner.Changed -= OnPartChanged;
            owner = part;
            if (owner != null) owner.Changed += OnPartChanged;
        }
        private void OnPartChanged(RobotPart part, PartChange change) {
            if ((change & (PartChange.Pose | PartChange.Geometry | PartChange.Existence)) != 0) HoleWorld.MarkDirty(this);
        }
        internal void RefreshGeometry() {
            BindOwner();
            var scale = transform.lossyScale;
            worldRadius = capsuleRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float half = Mathf.Max(0, capsuleLength * Mathf.Abs(scale.y) * .5f - worldRadius);
            capsuleA = transform.position - transform.up * half; capsuleB = transform.position + transform.up * half;
            QueryBounds = new Bounds((capsuleA + capsuleB) * .5f, new Vector3(Mathf.Abs(capsuleB.x - capsuleA.x), Mathf.Abs(capsuleB.y - capsuleA.y), Mathf.Abs(capsuleB.z - capsuleA.z)) + Vector3.one * (worldRadius * 2));
        }
        private bool Touches(HoleRecord hole) => hole.IsActive && hole.QueryMesh != null && QueryBounds.Intersects(hole.Bounds)
            && GeometryQuery.CapsuleMesh(hole.QueryMesh, hole.WorldMatrix, capsuleA, capsuleB, worldRadius, hole.Definition.convex);
        internal void RemoveInvalidContacts() {
            scratch.Clear();
            foreach (var hole in holes) if (!Touches(hole)) scratch.Add(hole);
            foreach (var hole in scratch) RemoveHole(hole);
        }
        internal void AddCandidates(List<HoleRecord> candidates) {
            var origin = transform.parent != null ? transform.parent.position : transform.position;
            candidates.Sort((a,b) => {
                int order = (a.Position - origin).sqrMagnitude.CompareTo((b.Position - origin).sqrMagnitude);
                if (order != 0) return order;
                order = a.Owner.GetInstanceID().CompareTo(b.Owner.GetInstanceID());
                return order != 0 ? order : a.Index.CompareTo(b.Index);
            });
            foreach (var hole in candidates) if (!holes.Contains(hole) && !hole.IsOccupied && Touches(hole) && !IsHoleIntersecting(hole)) AddHole(hole);
        }
        public bool IsHoleIntersecting(HoleRecord other) {
            if (holes.Contains(other)) return false;
            var bounds = other.Bounds; bounds.Expand(-.01f);
            foreach (var hole in holes) if (hole.Bounds.Intersects(bounds)) return true;
            return false;
        }
        public List<GameObject> GetObjects() {
            HoleWorld.Flush(); var result = new List<GameObject>(holes.Count);
            foreach (var hole in holes) if (hole.IsActive) result.Add(hole.holeData.part);
            return result;
        }
        public List<HoleRecord> GetOrderedHoles() {
            HoleWorld.Flush(); var result = new List<HoleRecord>(holes);
            var origin = transform.parent != null ? transform.parent.position : transform.position;
            result.Sort((a,b) => (a.Position - origin).sqrMagnitude.CompareTo((b.Position - origin).sqrMagnitude));
            return result;
        }
        public void RemoveAll() {
            scratch.Clear(); scratch.AddRange(holes);
            foreach (var hole in scratch) RemoveHole(hole);
        }
        public bool IsTargetHole(HoleRecord hole) => hole != null && hole.holeType == targetHoleType;
        public void AddHole(HoleRecord hole) {
            if (hole == null || holes.Contains(hole) || !hole.AddDetector(this)) return;
            holes.Add(hole); added = true;
            if (IsTargetHole(hole)) { targetCount++; addedTarget = true; }
            HoleWorld.QueueEvents(this);
        }
        public void RemoveHole(HoleRecord hole) {
            if (hole == null || !holes.Remove(hole)) return;
            hole.RemoveDetector(this); removed = true;
            if (IsTargetHole(hole)) { targetCount = Mathf.Max(0, targetCount - 1); removedTarget = true; }
            HoleWorld.QueueEvents(this);
        }
        internal void FlushEvents() {
            bool a = added, r = removed, at = addedTarget, rt = removedTarget;
            added = removed = addedTarget = removedTarget = false;
            if (r) OnRemoveHole?.Invoke();
            if (a) OnAddHole?.Invoke();
            // Subscribers see the final contact set, never an intermediate state.
            if (rt) OnRemoveTargetHole?.Invoke();
            if (at) OnAddTargetHole?.Invoke();
        }
        public static HoleDetector Create(Transform parent, float length, HoleCollider.HoleType type) {
            var obj = new GameObject("Hole Detector"); obj.layer = HoleCollider.HOLE_COLLISIONS_LAYER;
            obj.transform.position = parent.position; obj.transform.up = parent.forward; obj.transform.SetParent(parent);
            var detector = obj.AddComponent<HoleDetector>(); detector.capsuleLength = length; detector.targetHoleType = type;
            HoleWorld.MarkDirty(detector); return detector;
        }
        public void ClearEvents() { OnAddHole = OnAddTargetHole = OnRemoveHole = OnRemoveTargetHole = null; }
    }
}
