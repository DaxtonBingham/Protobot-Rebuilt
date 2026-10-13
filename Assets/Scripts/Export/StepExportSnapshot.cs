using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Parts_List;
using Protobot.ChainSystem;
using Protobot.CustomParts;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot.Export {
    // An immutable, camera-independent handoff to the CAD process. All Unity access
    // happens on the main thread; the worker never opens or modifies the PBB.
    [Serializable]
    public sealed class StepExportSnapshot {
        public int version = 1;
        public string name;
        public string lengthUnit = "inch";
        public List<Part> parts = new List<Part>();
        public List<Geometry> geometry = new List<Geometry>();
        public List<CustomPartDefinition> customParts = new List<CustomPartDefinition>();

        [Serializable] public sealed class Part {
            public string id, catalogId, name, state, customDefinitionId;
            public float[] matrix;
            public List<Surface> surfaces = new List<Surface>();
        }
        [Serializable] public sealed class Surface {
            public int geometry;
            public float[] matrix;
            public Color color;
        }
        [Serializable] public sealed class Geometry {
            public string name;
            public Vector3[] vertices;
            public int[] triangles;
        }

        public static float[] Matrix(Matrix4x4 value) {
            var result = new float[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) result[r * 4 + c] = value[r, c];
            return result;
        }

        public static StepExportSnapshot Capture(string title, IEnumerable<SavedObject> selection = null, IEnumerable<ChainConnection> selectedChains = null) {
            SceneActivity.Flush(true);
            var result = new StepExportSnapshot { name = title };
            var meshes = new Dictionary<Tuple<Mesh, int>, int>();
            var definitions = new HashSet<string>();
            var objects = (selection ?? RobotDocument.GetObjects().Select(o => o.GetComponent<SavedObject>()))
                .Where(o => o != null && o.gameObject.activeInHierarchy).Distinct();
            foreach (var view in objects.OrderBy(o => o.DocumentPart != null ? o.DocumentPart.Id : o.id, StringComparer.Ordinal)) {
                var part = NewPart(view);
                if (!string.IsNullOrEmpty(view.customDefinitionId) && definitions.Add(view.customDefinitionId)) {
                    CustomPartDefinition definition;
                    if (!CustomPartRegistry.TryGetDefinition(view.customDefinitionId, out definition))
                        throw new InvalidOperationException("Missing Poly Maker definition: " + part.name);
                    result.customParts.Add(definition.CloneDeep());
                }
                var inverse = view.transform.worldToLocalMatrix;
                int insertNumber = 0;
                foreach (var filter in view.GetComponentsInChildren<MeshFilter>(false)) {
                    if (filter.GetComponentInParent<SavedObject>() != view) continue;
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null || filter.sharedMesh == null || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly
                        || filter.GetComponent<ShadowRenderProxy>() != null) continue;
                    // Batched renderers can be disabled while their document part is
                    // visible. Activity, not renderer.enabled or the camera frustum,
                    // determines whether geometry belongs to this export.
                    var materials = renderer.sharedMaterials;
                    Part target = part;
                    Matrix4x4 local = inverse * filter.transform.localToWorldMatrix;
                    var insert = filter.GetComponentInParent<HoleInsert>();
                    if (insert != null) {
                        bool plastic = false, recognized = false;
                        for (var parent = filter.transform; parent != null && parent != insert.transform; parent = parent.parent) {
                            if (parent.name == "Metal Inserts") { recognized = true; break; }
                            if (parent.name == "Plastic Inserts") { recognized = plastic = true; break; }
                        }
                        if (!recognized) throw new InvalidOperationException("Unrecognized shaft insert on " + part.name);
                        target = new Part { id = part.id + "/insert-" + (++insertNumber),
                            catalogId = plastic ? "INSERT-Plastic" : "INSERT-Metal",
                            name = plastic ? "Plastic shaft insert" : "Metal shaft insert",
                            matrix = Matrix(filter.transform.localToWorldMatrix) };
                        result.parts.Add(target); local = Matrix4x4.identity;
                    }
                    for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) {
                        var material = materials.Length == 0 ? null : materials[Math.Min(sub, materials.Length - 1)];
                        result.AddSurface(target, meshes, filter.sharedMesh, sub, material, local);
                    }
                }
                if (part.surfaces.Count == 0) throw new InvalidOperationException("No geometry for " + part.name + " (" + part.catalogId + ").");
                result.parts.Add(part);
            }
            var chains = selectedChains ?? (selection == null ? UnityEngine.Object.FindObjectsOfType<ChainConnection>() : Array.Empty<ChainConnection>());
            int chainNumber = 0;
            foreach (var connection in chains.Where(c => c != null && !c.IsPreview && c.gameObject.activeInHierarchy).Distinct()) {
                chainNumber++;
                var chain = connection.InstanceGeometry;
                if (chain == null || chain.LinkCount == 0)
                    throw new InvalidOperationException("A chain has no resolved links. Resolve its route before exporting.");
                var links = new Dictionary<int, Part>();
                chain.VisitExportGeometry((index, mesh, submesh, material, world, local) => {
                    Part link;
                    if (!links.TryGetValue(index, out link)) {
                        link = new Part { id = "chain-" + chainNumber + "/link-" + (index + 1),
                            catalogId = "CHAIN-" + connection.Settings.standard,
                            name = "Chain " + chainNumber + " / link " + (index + 1), matrix = Matrix(world) };
                        links.Add(index, link); result.parts.Add(link);
                    }
                    result.AddSurface(link, meshes, mesh, submesh, material, local);
                });
            }
            if (result.parts.Count == 0) throw new InvalidOperationException("Add or select some parts before exporting.");
            return result;
        }

        static Part NewPart(SavedObject view) {
            var label = view.GetComponent<PartName>();
            return new Part {
                id = view.DocumentPart != null ? view.DocumentPart.Id : view.GetInstanceID().ToString(),
                catalogId = view.id, state = view.state,
                name = label != null && !string.IsNullOrEmpty(label.name) ? label.name : view.id,
                customDefinitionId = view.customDefinitionId,
                matrix = Matrix(view.transform.localToWorldMatrix)
            };
        }

        void AddSurface(Part part, Dictionary<Tuple<Mesh, int>, int> meshes, Mesh mesh, int submesh, Material material, Matrix4x4 matrix) {
            if (mesh.GetTopology(submesh) != MeshTopology.Triangles) throw new InvalidOperationException("Unsupported geometry: " + mesh.name);
            var key = Tuple.Create(mesh, submesh); int index;
            if (!meshes.TryGetValue(key, out index)) {
                index = geometry.Count; meshes.Add(key, index);
                geometry.Add(new Geometry { name = mesh.name, vertices = mesh.vertices, triangles = mesh.GetTriangles(submesh) });
            }
            part.surfaces.Add(new Surface { geometry = index, matrix = Matrix(matrix),
                color = material != null && material.HasProperty("_Color") ? material.color : Color.gray });
        }

        public void Write(string path) { File.WriteAllText(path, JsonUtility.ToJson(this)); }
    }
}
