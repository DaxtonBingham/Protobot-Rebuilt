using System.Collections.Generic;
using System.Linq;
using System;
using System.Collections;
using UnityEngine;
using Protobot.StateSystems;

namespace Protobot {
    public class HoleDetector : MonoBehaviour {
        public HashSet<HoleCollider> holes = new HashSet<HoleCollider>();
        [SerializeField] HoleCollider.HoleType targetHoleType;

        private void Update() {
            var allHoles = new List<HoleCollider>(holes);
            foreach (var holeCollider in allHoles) {
                if (holeCollider == null) {
                    RemoveHole(holeCollider);
                    continue;
                }

                if (holeCollider.gameObject.IsDeleted())
                    RemoveHole(holeCollider);
            }
        }

        public bool TargetHoleFound => TargetHoleCount > 0;
        public int TargetHoleCount { get; private set; } = 0;

        public Action OnAddHole; // Runs whenever any hole gets added to this hole detector
        public Action OnAddTargetHole; // Runs when a target hole is added to this hole detector
        public Action OnRemoveHole; // Runs whenever any hole gets removed from this hole detector
        public Action OnRemoveTargetHole; // Runs when a target hole is added to this hole detector
        
        private void Start() {
            TargetHoleCount = 0;
            holes.Clear();
        }
        
        /// <summary>
        /// Makes sure to wait until all holes have been added before deciding to add a target hole
        /// Due to the randomness of OnTriggerStay
        /// </summary>
        /// <returns></returns>
        IEnumerator WaitAddTargetHole() {
            yield return new WaitForEndOfFrame();
            OnAddTargetHole?.Invoke();
        }

        public List<GameObject> GetObjects() {
            return holes.Where(col => col != null && col.holeData != null && col.holeData.part != null)
                        .Select(col => col.holeData.part)
                        .ToList();
        }

        /// <summary>
        /// Returns the list of holes detected in order from distance to the connecting part
        /// </summary>
        /// <returns></returns>
        public List<HoleCollider> GetOrderedHoles() {
            return holes.Where(x => x != null)
                        .OrderBy(x => Vector3.Distance(transform.parent.position, x.transform.position))
                        .ToList();
        }

        public void RemoveAll() {
            var allHoles = new List<HoleCollider>(holes);
            
            foreach (HoleCollider hole in allHoles) {
                RemoveHole(hole);
            }
        }

        //Returns true if a hole is intersecting any other hole current detected by holeDetector
        public bool IsHoleIntersecting(HoleCollider otherHole) {
            if (otherHole == null) return false;

            if (!holes.Contains(otherHole)) {
                MeshCollider otherMeshCollider = otherHole.GetComponent<MeshCollider>();
                if (otherMeshCollider == null) return false;

                var otherHoleBounds = otherMeshCollider.bounds;
                otherHoleBounds.Expand(-0.01f);

                foreach (HoleCollider hole in holes) {
                    if (hole == null) continue;

                    MeshCollider holeMeshCollider = hole.GetComponent<MeshCollider>();
                    if (holeMeshCollider == null) continue;

                    var holeBounds = holeMeshCollider.bounds;
                    if (holeBounds.Intersects(otherHoleBounds))
                        return true;
                }
            }

            return false;
        }

        private void OnTriggerStay(Collider other) {
            if (other.TryGetComponent(out HoleCollider hole))
                if (!hole.IsOccupied && !IsHoleIntersecting(hole))
                    AddHole(hole);
        }

        private void OnTriggerExit(Collider other) {
            if (other.TryGetComponent(out HoleCollider hole))
                if (hole.IsOccupiedBy(this))
                    RemoveHole(hole);
        }

        public void RemoveHole(HoleCollider hole) {
            if (hole == null) {
                int removedCount = holes.RemoveWhere(existingHole => existingHole == null);
                if (removedCount == 0) return;

                int previousTargetCount = TargetHoleCount;
                RecalculateTargetHoleCount();
                if (TargetHoleCount < previousTargetCount) {
                    OnRemoveTargetHole?.Invoke();
                }

                OnRemoveHole?.Invoke();
                return;
            }

            if (!holes.Remove(hole)) return;

            hole.RemoveDetector(this);

            if (IsTargetHole(hole)) {
                TargetHoleCount = Mathf.Max(0, TargetHoleCount - 1);
                OnRemoveTargetHole?.Invoke();
            }
            
            OnRemoveHole?.Invoke();
        }

        public bool IsTargetHole(HoleCollider hole) => hole != null && hole.holeType == targetHoleType;

        public void AddHole(HoleCollider hole) {
            if (hole == null) return;
            if (!holes.Add(hole)) return;

            hole.AddDetector(this);

            if (IsTargetHole(hole)) {
                TargetHoleCount++;
                StartCoroutine(WaitAddTargetHole());
            }
            
            OnAddHole?.Invoke();
        }

        private void RecalculateTargetHoleCount() {
            TargetHoleCount = holes.Count(IsTargetHole);
        }

        public static HoleDetector Create(Transform parent, float length, HoleCollider.HoleType targetHoleType) {
            var newGameObject = new GameObject("Hole Detector");
            newGameObject.layer = HoleCollider.HOLE_COLLISIONS_LAYER;

            Rigidbody rb = newGameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            CapsuleCollider collider = newGameObject.AddComponent<CapsuleCollider>();
            collider.isTrigger = true;
            collider.radius = 0.01f;
            collider.height = length;

            newGameObject.transform.position = parent.position;
            newGameObject.transform.up = parent.forward;

            newGameObject.transform.SetParent(parent);

            HoleDetector holeDetector = newGameObject.AddComponent<HoleDetector>();
            holeDetector.targetHoleType = targetHoleType;

            return holeDetector;
        }

        public void ClearEvents() {
            OnAddHole = null;
            OnAddTargetHole = null;
            
            OnRemoveHole = null;
            OnRemoveTargetHole = null;
        }
    }
}
