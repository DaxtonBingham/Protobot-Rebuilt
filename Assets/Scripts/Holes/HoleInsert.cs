using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    [RequireComponent(typeof(HoleCollider))]
    public class HoleInsert : MonoBehaviour {
        private HoleCollider holeCol;
        
        [SerializeField] private GameObject metalInsertPrefab;
        [SerializeField] private GameObject plasticInsertPrefab;
        
        private const float InsertOffset = 0.05f;

        private GameObject metalInserts;
        private GameObject plasticInserts;
        
        private bool UsingPlastic => plasticInserts.activeInHierarchy;

        private void Start() {
            holeCol = GetComponent<HoleCollider>();
            // Duplicating a placed part also duplicates its generated children.
            metalInserts = transform.Find("Metal Inserts")?.gameObject ?? InstantiateInserts(metalInsertPrefab, "Metal Inserts");
            plasticInserts = transform.Find("Plastic Inserts")?.gameObject ?? InstantiateInserts(plasticInsertPrefab, "Plastic Inserts");

            holeCol.OnSetDetector += SetDetector;
            // Compact contacts can be resolved before this visual adapter starts.
            // Apply the current contact as well as subscribing to later changes.
            var detectors = holeCol.detectors;
            SetDetector(detectors.Count > 0 ? detectors[detectors.Count - 1] : null);
        }

        private void SetDetector(HoleDetector detector) {
            if (UsingPlastic) return;
            metalInserts.SetActive(detector != null && detector.transform.parent.name.Contains("Normal Shaft"));
        }
        private void OnDestroy() { if (holeCol != null) holeCol.OnSetDetector -= SetDetector; }

        private GameObject InstantiateInserts(GameObject insertPrefab, string insertName) {
            GameObject inserts = new GameObject(insertName);
            inserts.transform.SetParent(transform);
            inserts.transform.localPosition = Vector3.zero;

            Vector3 holeDir = holeCol.holeData.forward;
            float holeDepth = holeCol.holeData.depth / 2;

            var posInsert = Instantiate(insertPrefab, inserts.transform);
            Vector3 pos = holeDir * (holeDepth - InsertOffset);
            posInsert.transform.localPosition = pos;
            posInsert.transform.forward = -holeDir;
            
            var negInsert = Instantiate(insertPrefab, inserts.transform);
            pos = -holeDir * (holeDepth - InsertOffset);
            negInsert.transform.localPosition = pos;
            negInsert.transform.forward = holeDir;
            
            inserts.SetActive(false);

            return inserts;
        }
    }
}
