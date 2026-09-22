using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    public struct HoleHit {
        public HoleRecord hole;
        public Vector3 point, normal;
        public float distance;
    }

    [DefaultExecutionOrder(9200)]
    public sealed class HoleWorld : MonoBehaviour {
        private static HoleWorld instance;
        private static SpatialGrid<HoleRecord> index = new SpatialGrid<HoleRecord>();
        private static SpatialGrid<HoleDetector> detectorIndex = new SpatialGrid<HoleDetector>();
        private static readonly HashSet<PartHoles> dirtyParts = new HashSet<PartHoles>();
        private static readonly HashSet<HoleDetector> dirtyDetectors = new HashSet<HoleDetector>();
        private static readonly HashSet<HoleDetector> events = new HashSet<HoleDetector>();
        private static readonly List<PartHoles> partWork = new List<PartHoles>();
        private static readonly List<HoleDetector> detectorWork = new List<HoleDetector>();
        private static readonly List<HoleDetector> neighbors = new List<HoleDetector>();
        private static readonly List<HoleRecord> candidates = new List<HoleRecord>();
        private static readonly List<HoleRecord> rayCandidates = new List<HoleRecord>();
        private static bool updating;
        public static int IndexedHoleCount { get; private set; }
        public static long ContactQueryCount { get; private set; }
        public static uint Revision { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() {
            instance = null; index = new SpatialGrid<HoleRecord>(); detectorIndex = new SpatialGrid<HoleDetector>();
            dirtyParts.Clear(); dirtyDetectors.Clear(); events.Clear(); partWork.Clear(); detectorWork.Clear(); neighbors.Clear(); candidates.Clear(); rayCandidates.Clear();
            updating = false; IndexedHoleCount = 0; ContactQueryCount = 0; Revision = 0;
        }
        private static void EnsureHost() {
            if (instance != null) return;
            var obj = new GameObject("Spatial connections"); DontDestroyOnLoad(obj); instance = obj.AddComponent<HoleWorld>();
        }
        public static void Register(PartHoles part) { EnsureHost(); dirtyParts.Add(part); }
        public static void MarkDirty(PartHoles part) { if (part != null) { EnsureHost(); dirtyParts.Add(part); } }
        public static void Unregister(PartHoles part) {
            dirtyParts.Remove(part);
            if (!part.WasIndexed) return;
            foreach (var hole in part.Holes) index.Remove(hole);
            IndexedHoleCount -= part.IndexedCount; part.IndexedCount = 0;
            part.WasIndexed = false; MarkNeighbors(part.IndexedBounds); unchecked { Revision++; }
        }
        public static void Register(HoleDetector detector) { EnsureHost(); dirtyDetectors.Add(detector); }
        public static void MarkDirty(HoleDetector detector) { if (detector != null) { EnsureHost(); dirtyDetectors.Add(detector); } }
        public static void Unregister(HoleDetector detector) { dirtyDetectors.Remove(detector); detectorIndex.Remove(detector); }
        internal static void QueueEvents(HoleDetector detector) { EnsureHost(); events.Add(detector); }
        internal static void Released(HoleRecord hole) { MarkNeighbors(hole.Bounds); }
        private static void MarkNeighbors(Bounds bounds) {
            detectorIndex.Query(bounds, neighbors);
            foreach (var detector in neighbors) dirtyDetectors.Add(detector);
        }
        private void LateUpdate() => Flush();

        public static void Flush() {
            if (updating) return;
            SceneActivity.Flush();
            if (dirtyParts.Count == 0 && dirtyDetectors.Count == 0 && events.Count == 0) return;
            updating = true;
            try {
                partWork.Clear(); partWork.AddRange(dirtyParts); dirtyParts.Clear();
                foreach (var part in partWork) {
                    if (part == null) continue;
                    Unregister(part);
                    if (!part.isActiveAndEnabled || part.gameObject.IsDeleted()) continue;
                    bool first = true;
                    var matrix = part.transform.localToWorldMatrix;
                    foreach (var hole in part.Holes) {
                        if (!hole.Definition.enabled) continue;
                        hole.Refresh(matrix); index.Set(hole, hole.Bounds); IndexedHoleCount++; part.IndexedCount++;
                        if (first) { part.IndexedBounds = hole.Bounds; first = false; } else part.IndexedBounds.Encapsulate(hole.Bounds);
                    }
                    part.WasIndexed = !first;
                    if (!first) MarkNeighbors(part.IndexedBounds);
                }
                detectorWork.Clear(); detectorWork.AddRange(dirtyDetectors); dirtyDetectors.Clear();
                // Release invalid contacts across the batch before assigning any new
                // occupancy. Existing valid contacts keep priority during movement.
                foreach (var detector in detectorWork) {
                    if (detector == null || !detector.isActiveAndEnabled) continue;
                    detector.RefreshGeometry(); detectorIndex.Set(detector, detector.QueryBounds);
                    detector.RemoveInvalidContacts();
                }
                foreach (var detector in detectorWork) {
                    if (detector == null || !detector.isActiveAndEnabled) continue;
                    index.Query(detector.QueryBounds, candidates); ContactQueryCount++;
                    detector.AddCandidates(candidates);
                }
                if (partWork.Count > 0 || detectorWork.Count > 0) unchecked { Revision++; }
                detectorWork.Clear(); detectorWork.AddRange(events); events.Clear();
                foreach (var detector in detectorWork) if (detector != null) detector.FlushEvents();
            } finally { updating = false; }
        }

        public static bool Raycast(Ray ray, out HoleHit hit, float distance = float.PositiveInfinity, GameObject ignore = null) {
            Flush(); hit = new HoleHit();
            index.Query(ray, distance, rayCandidates);
            bool found = false;
            foreach (var hole in rayCandidates) {
                if (!CanPick(hole, ignore) || hole.QueryMesh == null) continue;
                if (!GeometryQuery.RayMesh(hole.QueryMesh, hole.WorldMatrix, ray, distance, out float t, out var normal, Physics.queriesHitBackfaces)) continue;
                distance = t; hit = new HoleHit { hole = hole, point = ray.GetPoint(t), normal = normal, distance = t }; found = true;
            }
            return found;
        }
        public static List<HoleHit> RaycastAll(Ray ray, float distance = float.PositiveInfinity) {
            Flush(); var result = new List<HoleHit>(); index.Query(ray, distance, rayCandidates);
            foreach (var hole in rayCandidates) if (CanPick(hole, null) && hole.QueryMesh != null && GeometryQuery.RayMesh(hole.QueryMesh, hole.WorldMatrix, ray, distance, out float t, out var normal, Physics.queriesHitBackfaces))
                result.Add(new HoleHit { hole = hole, point = ray.GetPoint(t), normal = normal, distance = t });
            result.Sort((a, b) => a.distance.CompareTo(b.distance)); return result;
        }
        public static List<GameObject> PartsInBox(Bounds bounds) {
            Flush(); var result = new List<GameObject>(); index.Query(bounds, rayCandidates);
            foreach (var hole in rayCandidates) if (CanPick(hole, null) && !result.Contains(hole.holeData.part)) result.Add(hole.holeData.part);
            return result;
        }
        public static List<GameObject> PartsAlongSegment(Vector3 a, Vector3 b, float radius) {
            Flush(); var result = new List<GameObject>();
            var bounds = new Bounds(a, Vector3.zero); bounds.Encapsulate(b); bounds.Expand(radius * 2);
            index.Query(bounds, rayCandidates);
            foreach (var hole in rayCandidates)
                if (CanPick(hole, null) && hole.QueryMesh != null && !result.Contains(hole.holeData.part)
                    && GeometryQuery.CapsuleMesh(hole.QueryMesh, hole.WorldMatrix, a, b, radius, hole.Definition.convex)) result.Add(hole.holeData.part);
            return result;
        }
        private static bool CanPick(HoleRecord hole, GameObject ignore) => hole.IsActive && hole.holeData.part != ignore && hole.Owner.transform.root.gameObject.layer != Placement.PLACEMENT_LAYER;
    }
}
