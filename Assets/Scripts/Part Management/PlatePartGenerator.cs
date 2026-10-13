using System.Collections;
using System.Collections.Generic;
using Parts_List;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot {
    public class PlatePartGenerator : PartGenerator {
        [SerializeField] private GameObject plateTemplate;
        [SerializeField] private HoleCollider plateHole;
        
        [SerializeField] private Material material;
        
        private List<string> EmptyList => new List<string>{" "};
        public override List<string> GetParam1Options() => EmptyList;

        public override List<string> GetParam2Options() => EmptyList;

        private int Length => int.Parse(param1.value);
        private int Width => int.Parse(param2.value);
        
        private int HoleCount => Length * Width;
        private readonly Dictionary<Vector2Int, Mesh> meshCache = new Dictionary<Vector2Int, Mesh>();

        private float GetPos(int val, int max) => 0.5f * ((-max + 1) / 2f + val);

        public override Mesh GetMesh() {
            var key = new Vector2Int(Length, Width);
            if (meshCache.TryGetValue(key, out var cached) && cached != null) return cached;
            CombineInstance[] combine = new CombineInstance[HoleCount];

            var i = 0;
            for (var x = 0; x < Length; x++) {
                for (var y = 0; y < Width; y++) {
                    var meshFilter = plateTemplate.GetComponent<MeshFilter>();

                    combine[i].mesh = meshFilter.sharedMesh;

                    var tMatrix = meshFilter.transform.localToWorldMatrix;
                    tMatrix[0, 3] = GetPos(x, Length);
                    tMatrix[1, 3] = GetPos(y, Width);
                    tMatrix[2, 3] = 0; // resets Z pos
                    
                    combine[i].transform = tMatrix;

                    i++;
                }
            }
            
            var newMesh = new Mesh {
                indexFormat = IndexFormat.UInt32
            };
            
            newMesh.CombineMeshes(combine);
            newMesh.RecalculateNormals();
            if (Length >= 1 && Length <= 25 && Width >= 1 && Width <= 5) meshCache[key] = newMesh;

            return newMesh;
        }

        private void OnDestroy() {
            foreach (var mesh in meshCache.Values) if (mesh != null) Destroy(mesh);
            meshCache.Clear();
        }

        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            var newPart = new GameObject("Plate (" + Length + "x" + Width + ")");
            
            Mesh mesh = GetMesh();
            newPart.AddComponent<MeshFilter>().sharedMesh = mesh;
            newPart.AddComponent<MeshCollider>().sharedMesh = mesh;
            
            newPart.AddComponent<MeshRenderer>().material = material;
            
            GenerateHoles(newPart);

            newPart.transform.position = position;
            newPart.transform.rotation = rotation;
    
            var partList = newPart.AddComponent<PartName>();

            partList.name = newPart.name;
            partList.weightInGrams = Length * Width * .53f;
            
            RemoveDataScripts(newPart);
            SetId(newPart);
            
            return newPart;
        }
        
        private void GenerateHoles(GameObject obj) {
            for (var x = 0; x < Length; x++) {
                for (var y = 0; y < Width; y++) {
                    var pos = plateHole.transform.localPosition;
                    pos.x = GetPos(x, Length); // correctly positions hole
                    pos.y = GetPos(y, Width); // correctly positions hole

                    var rot = plateHole.transform.rotation;

                    PartHoles.AddTemplate(obj, plateHole, pos, rot);
                }
            }
        }
    }
}
