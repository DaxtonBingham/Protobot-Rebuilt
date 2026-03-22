using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Protobot {
    public class ChainPartGenerator : PartGenerator {
        private static readonly List<string> StandardOptions = new List<string> {
            "0.148\" Pitch",
            "0.250\" Pitch",
            "0.385\" Pitch"
        };
        private static Mesh previewMesh;

        private void Awake() {
            EnsureDefaults();
        }

        public void EnsureDefaults() {
            // Runtime-created generators start with null parameter containers.
            if (param1 == null) {
                param1 = new Parameter();
            }

            if (param2 == null) {
                param2 = new Parameter();
            }

            if (string.IsNullOrWhiteSpace(param1.name)) {
                param1.name = "Chain";
            }

            param1.custom = false;

            if (string.IsNullOrWhiteSpace(param1.value)) {
                param1.value = StandardOptions[0];
            }

            param2.name = string.Empty;
            param2.value = string.Empty;
            param2.custom = false;
        }

        public override List<string> GetParam1Options() {
            return StandardOptions;
        }

        public override List<string> GetParam2Options() {
            return new List<string>();
        }

        public override Mesh GetMesh() {
            if (previewMesh != null) {
                return previewMesh;
            }

            // Lightweight flat preview mesh; placement is disabled for chain tool entries.
            previewMesh = new Mesh {
                name = "ChainToolPreviewMesh",
                vertices = new[] {
                    new Vector3(-0.4f, -0.05f, 0f),
                    new Vector3(0.4f, -0.05f, 0f),
                    new Vector3(-0.4f, 0.05f, 0f),
                    new Vector3(0.4f, 0.05f, 0f)
                },
                triangles = new[] { 0, 2, 1, 1, 2, 3 }
            };
            previewMesh.RecalculateBounds();
            previewMesh.RecalculateNormals();
            return previewMesh;
        }

        public override GameObject Generate(Vector3 position, Quaternion rotation) {
            // Chain is a tool entry, not a placeable part.
            return new GameObject("ChainToolProxy");
        }
    }

    public static class PartsManager {
        public const string ChainToolPartId = "CHAIN";

        public static PartType[] partTypes;
        private static PartType runtimeChainPartType;

        [RuntimeInitializeOnLoadMethod]
        public static void LoadPartTypes() {
            var loadedPartTypes = Resources.LoadAll<GameObject>("Part Prefabs")
                .Select(p => p != null ? p.GetComponent<PartType>() : null)
                .Where(p => p != null)
                .ToList();

            foreach (PartType partType in loadedPartTypes) {
                PartGenerator generator = partType.GetComponent<PartGenerator>();
                if (generator != null) {
                    try {
                        generator.InitParamValues();
                    }
                    catch (Exception ex) {
                        Debug.LogWarning($"Failed to initialize part params for {partType.name}: {ex.Message}");
                    }
                }
            }

            try {
                PartType chainToolPart = GetOrCreateChainToolPart(loadedPartTypes);
                if (chainToolPart != null) {
                    loadedPartTypes.Add(chainToolPart);
                }
            }
            catch (Exception ex) {
                Debug.LogError($"Failed to create chain tool part: {ex}");
            }

            partTypes = loadedPartTypes.ToArray();
        }

        public static PartType GetPartType(string id) {
            var idSplit = id.Split('-');
            var typeId = idSplit[0];
            return partTypes.FirstOrDefault(partType => partType.id == typeId);
        }
        
        public static GameObject GeneratePart(string id, Vector3 pos, Quaternion rot) {
            var idSplit = id.Split('-');
            var typeId = idSplit[0];

            var p1Val = "";
            var p2Val = "";

            if (idSplit.Length >= 2) p1Val = idSplit[1];
            if (idSplit.Length == 3) p2Val = idSplit[2];
    
            if (id == "NUT") { //VERY TEMPORARY ONLY FOR BETA 1.3.1 PLEASE FIX WITH VERSION CONTROL
                p1Val = "Lock";
            }

            GameObject partObj = null;
            
            foreach (var partType in partTypes) {
                if (partType.id == typeId) {
                    var gen = partType.GetComponent<PartGenerator>();
                    
                    Parameter p1 = gen.param1;
                    Parameter p2 = gen.param2;

                    gen.param1.value = p1Val;
                    gen.param2.value = p2Val;
                    
                    //TODO This is awful and should be compared against an array in another file/method to make sure legacy files work but this is a quick fix
                    if (gen.name == "Omni Wheel" || gen.name == "Traction Wheel")
                    {
                        if (p2Val == "")
                        {
                            p2.value = p1Val;
                            p1.value = "V1";
                        }
                    }
                    if (gen.name == "Motor" && p1Val == "")
                    {
                        p1.value = "11W";
                    }
                    if (gen.name == "Block Bearing" && p1Val == "")
                    {
                        p1.value = "Normal";
                    }
                    if (gen.name == "Cylinder" && p2Val == "")
                    {
                        p2.value = "Normal";
                    }
                    if (gen.name == "Ring" && p1Val == "")
                    {
                        p1.value = "Red";
                    }

                    partObj = gen.Generate(pos, rot);

                    gen.param1.value = p1.value;
                    gen.param2.value = p2.value;
                }
            }

            return partObj;
        }

        /// <Summary> Returns a list of all loaded parts in the current scene </Summary>
        public static List<GameObject> FindLoadedObjects() {
            return GameObject.FindObjectsOfType<SavedObject>().Select(x => x.gameObject).ToList();
        }

        /// <Summary> Destroys all loaded parts in the current scene </Summary>
        public static void DestroyLoadedObjects() {
            foreach (var obj in FindLoadedObjects())
                GameObject.Destroy(obj);
        }

        private static PartType GetOrCreateChainToolPart(List<PartType> loadedPartTypes) {
            if (runtimeChainPartType != null) {
                return runtimeChainPartType;
            }

            ChainPartGenerator existingGenerator = Resources.FindObjectsOfTypeAll<ChainPartGenerator>()
                .FirstOrDefault(generator => {
                    if (generator == null) {
                        return false;
                    }

                    PartType partType = generator.GetComponent<PartType>();
                    return partType != null && partType.id == ChainToolPartId;
                });

            if (existingGenerator != null) {
                runtimeChainPartType = existingGenerator.GetComponent<PartType>();
                existingGenerator.EnsureDefaults();
                existingGenerator.InitParamValues();
                return runtimeChainPartType;
            }

            GameObject chainObject = new GameObject("Chain");
            chainObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

            PartType chainPartType = chainObject.AddComponent<PartType>();
            chainPartType.id = ChainToolPartId;
            chainPartType.connectingPart = false;
            chainPartType.group = PartType.PartGroup.Motion;
            chainPartType.icon = ResolveChainIcon(loadedPartTypes);

            ChainPartGenerator chainGenerator = chainObject.AddComponent<ChainPartGenerator>();
            chainGenerator.EnsureDefaults();
            chainGenerator.InitParamValues();

            runtimeChainPartType = chainPartType;
            return runtimeChainPartType;
        }

        private static Sprite ResolveChainIcon(IEnumerable<PartType> loadedPartTypes) {
            PartType sprocketPart = loadedPartTypes.FirstOrDefault(p =>
                p != null
                && !string.IsNullOrWhiteSpace(p.id)
                && p.id.ToUpperInvariant().Contains("SPKT")
                && p.icon != null);

            if (sprocketPart != null) {
                return sprocketPart.icon;
            }

            PartType motionPart = loadedPartTypes.FirstOrDefault(p =>
                p != null
                && p.group == PartType.PartGroup.Motion
                && p.icon != null);

            return motionPart != null ? motionPart.icon : null;
        }
    }
}
