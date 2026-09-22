using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot {
    // CAD-derived assets use the same part IDs, generators, placement and save path as stock parts.
    public static class OverrideCatalog {
        [Serializable] public class Catalog { public MeshData[] meshes; public Entry[] entries; }
        [Serializable] public class MeshData { public string id; public float[] vertices; public Surface[] groups; }
        [Serializable] public class Surface { public float[] color; public int[] triangles; }
        [Serializable] public class Entry { public string id, name, parameter, icon; public bool hidden; public Variant[] variants; }
        [Serializable] public class Variant { public string value; public Node[] nodes; public float[] offset; }
        [Serializable] public class Node { public string mesh; public float[] matrix; }
        private static GameObject library;
        private static Catalog catalog;
        private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Material[]> materials = new Dictionary<string, Material[]>();
        private static readonly Dictionary<string, Material> palette = new Dictionary<string, Material>();

        public static void Register() {
            if (library != null) UnityEngine.Object.Destroy(library);
            string path = Path.Combine(Application.streamingAssetsPath, "Override/catalog.json");
            if (!File.Exists(path)) { Debug.LogError("Override catalog is missing: " + path); return; }
            catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(path));
            library = new GameObject("Override catalog");
            library.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(library);
            var additions = new List<PartType>();
            foreach (Entry entry in catalog.entries) {
                var obj = new GameObject(entry.name);
                obj.transform.SetParent(library.transform, false);
                var type = obj.AddComponent<PartType>();
                type.id = entry.id;
                type.group = entry.hidden ? PartType.PartGroup.None : PartType.PartGroup.Structure;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.LoadImage(File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, "Override/" + entry.icon)));
                type.icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f,.5f), 100);
                var generator = obj.AddComponent<OverridePartGenerator>();
                generator.entry = entry;
                generator.param1 = new Parameter { name = entry.parameter, value = entry.variants[0].value };
                generator.param2 = new Parameter { name = "", value = "" };
                additions.Add(type);
            }
            // Insert alongside the existing competition items; retain their original order and IDs.
            var parts = PartsManager.partTypes.Where(p => !catalog.entries.Any(e => e.id == p.id)).ToList();
            int index = parts.FindIndex(p => p.id == "PUBA");
            parts.InsertRange(index < 0 ? 0 : index, additions);
            PartsManager.partTypes = parts.ToArray();
            Debug.Log("Override: registered " + additions.Count + " catalog entries.");
        }

        private static void LoadMesh(string id) {
            if (meshes.ContainsKey(id)) return;
            var data = catalog.meshes.First(m => m.id == id);
            var vertices = new Vector3[data.vertices.Length / 3];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(data.vertices[3*i], data.vertices[3*i+1], data.vertices[3*i+2]);
            var mesh = new Mesh { name = "Override " + id, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.subMeshCount = data.groups.Length;
            var mats = new Material[data.groups.Length];
            for (int i = 0; i < data.groups.Length; i++) {
                mesh.SetTriangles(data.groups[i].triangles, i);
                mats[i] = GetMaterial(data.groups[i].color);
            }
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            meshes.Add(id, mesh); materials.Add(id, mats);
        }

        private static Material GetMaterial(float[] c) {
            string key = String.Join(",", c.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).ToArray());
            Material mat;
            if (palette.TryGetValue(key, out mat)) return mat;
            mat = new Material(Shader.Find("Standard"));
            mat.enableInstancing = c[3] >= 1;
            mat.name = "Override " + key;
            mat.color = new Color(c[0],c[1],c[2],c[3]);
            mat.SetFloat("_Glossiness", .35f);
            if (c[3] < 1) {
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetFloat("_Mode", 2); mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_ALPHABLEND_ON"); mat.renderQueue = 3000;
            }
            palette.Add(key,mat); return mat;
        }

        public static GameObject CreateTemplate(Entry entry, Variant variant) {
            var obj = new GameObject(entry.name);
            obj.transform.SetParent(library.transform, false);
            var combine = new Dictionary<Material, List<CombineInstance>>();
            foreach (Node node in variant.nodes) {
                LoadMesh(node.mesh);
                var matrix = Matrix4x4.identity;
                for (int row=0;row<3;row++) for (int col=0;col<4;col++) matrix[row,col] = node.matrix[row*4+col];
                for (int row=0;row<3;row++) matrix[row,3] -= variant.offset[row];
                Mesh mesh = meshes[node.mesh];
                for (int i=0;i<mesh.subMeshCount;i++) {
                    var material = materials[node.mesh][i];
                    if (!combine.ContainsKey(material)) combine.Add(material,new List<CombineInstance>());
                    combine[material].Add(new CombineInstance { mesh=mesh, subMeshIndex=i, transform=matrix });
                }
            }
            var submeshes = new List<CombineInstance>();
            var usedMaterials = new List<Material>();
            // Stock saves store only the first material's RGB. Keep an opaque surface
            // first so reloading cannot turn the cup's clear half opaque.
            foreach (var group in combine.OrderByDescending(g => g.Key.color.a)) {
                var mesh = new Mesh { indexFormat=IndexFormat.UInt32 };
                mesh.CombineMeshes(group.Value.ToArray(),true,true);
                submeshes.Add(new CombineInstance { mesh=mesh, transform=Matrix4x4.identity });
                usedMaterials.Add(group.Key);
            }
            var combined = new Mesh { name=entry.name+" "+variant.value, indexFormat=IndexFormat.UInt32 };
            combined.CombineMeshes(submeshes.ToArray(),false,false);
            foreach (var item in submeshes) UnityEngine.Object.Destroy(item.mesh);
            obj.AddComponent<MeshFilter>().sharedMesh=combined;
            obj.AddComponent<MeshRenderer>().sharedMaterials=usedMaterials.ToArray();
            obj.AddComponent<MeshCollider>().sharedMesh=combined;
            var partData=obj.AddComponent<PartData>();
            partData.param1Value=variant.value; partData.param2Value=""; partData.holeData="";
            // Some bundled players predate the optional parts-list component.
            var partNameType=typeof(PartsManager).Assembly.GetType("Parts_List.PartName");
            if (partNameType!=null) {
                var partName=obj.AddComponent(partNameType);
                partNameType.GetField("name").SetValue(partName,entry.name);
            }
            return obj;
        }
    }
}
