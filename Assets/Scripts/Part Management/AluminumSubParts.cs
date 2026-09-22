using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Protobot {
    [Serializable]
    public class AluminumSubParts : MonoBehaviour {
        [SerializeField] private GameObject start;
        [SerializeField] private GameObject mid;
        [SerializeField] private GameObject mid5Start;
        [SerializeField] private GameObject mid5End;
        [SerializeField] private GameObject end;
        [SerializeField] private Material material;
        [SerializeField] private int maxHoleCount = 35;
        private readonly Dictionary<int, Mesh> meshCache = new Dictionary<int, Mesh>();
        public Mesh GetMesh(int holeCount) {
            if (meshCache.TryGetValue(holeCount, out var cached) && cached != null) return cached;
            CombineInstance[] combine = new CombineInstance[holeCount];

            for (int i = 0; i < holeCount; i++) {
                var subPart = GetSubPart(i + 1);
                var meshFilter = subPart.GetComponent<MeshFilter>();

                combine[i].mesh = meshFilter.sharedMesh;

                var tMatrix = meshFilter.transform.localToWorldMatrix;
                tMatrix[0, 3] = GetXPos(i, holeCount);
                tMatrix[1, 3] = 0; // resets Y pos
                tMatrix[2, 3] = 0; // resets Z pos

                combine[i].transform = tMatrix;
            }

            Mesh newMesh = new Mesh();
            newMesh.indexFormat = IndexFormat.UInt32;
            newMesh.CombineMeshes(combine);
            newMesh.RecalculateNormals();
            if (holeCount > 0 && holeCount <= maxHoleCount) meshCache[holeCount] = newMesh;

            return newMesh;
        }

        private void OnDestroy() {
            foreach (var mesh in meshCache.Values) if (mesh != null) Destroy(mesh);
            meshCache.Clear();
        }

        public float GetXPos(int holeIndex, int holeCount) => 0.5f * ((-holeCount + 1) / 2f + holeIndex);

        public GameObject GeneratePart(int holeCount) {
            // make a new game object with a mesh from GetMesh(holeCount)
            // iterate thru all sub parts and add their holes with their LOCAL POSITIONS shifted by x values

            var newPart = new GameObject("Aluminum Part (" + holeCount + ")");
            Mesh mesh = GetMesh(holeCount);
            newPart.AddComponent<MeshFilter>().sharedMesh = mesh;
            newPart.AddComponent<MeshCollider>().sharedMesh = mesh;
            newPart.AddComponent<MeshRenderer>().material = material;
            
            GenerateHoles(newPart, holeCount);
            
            return newPart;
        }
        
        public void GenerateHoles(GameObject obj, int holeCount) {
            // iterate thru each subpart
            for (int i = 1; i <= holeCount; i++) {
                GameObject subPart = GetSubPart(i);

                foreach (var holeCollider in subPart.GetComponentsInChildren<HoleCollider>()) {
                    var pos = holeCollider.transform.localPosition;
                    pos.x += GetXPos(i - 1, holeCount); // correctly positions hole

                    var rot = holeCollider.transform.rotation;
                    
                    PartHoles.AddTemplate(obj, holeCollider, pos, rot);
                }
            }
        }

        public GameObject GetSubPart(int curHole) {
            if (curHole == 1) 
                return start;
            
            if (curHole == maxHoleCount)
                return end;

            if (curHole % 5 == 0)
                return mid5Start;

            if ((curHole - 1) % 5 == 0)
                return mid5End;

            return mid;
        }
    }
}
