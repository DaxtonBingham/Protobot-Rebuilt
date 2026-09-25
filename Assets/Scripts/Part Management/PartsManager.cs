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
            OverrideCatalog.Register();
        }

        public static PartType GetPartType(string id) {
            if (string.IsNullOrWhiteSpace(id) || partTypes == null) return null;
            string typeId = id.Split('-')[0];
            PartType first = null;
            foreach (var candidate in partTypes) {
                if (candidate.id != typeId) continue;
                if (first == null) first = candidate;
                else if (TryResolvePart(id, out var resolved, out _, out _)) return resolved;
            }
            return first;
        }

        public static GameObject GeneratePart(string id, Vector3 pos, Quaternion rot) {
            if (!TryResolvePart(id, out var type, out string first, out string second)) return null;
            var generator = type.GetComponent<PartGenerator>();
            string previous1 = generator.param1.value, previous2 = generator.param2.value;
            try {
                generator.param1.value = first;
                generator.param2.value = second;
                return generator.Generate(pos, rot);
            } finally {
                generator.param1.value = previous1;
                generator.param2.value = previous2;
            }
        }

        public static bool TryValidateId(string id, out string error) {
            bool valid = TryResolvePart(id, out _, out _, out _);
            error = valid ? null : "Unknown or unsupported part: " + id;
            return valid;
        }

        private static bool TryResolvePart(string id, out PartType type, out string first, out string second) {
            type = null; first = second = "";
            if (string.IsNullOrWhiteSpace(id) || partTypes == null) return false;
            var fields = id.Split('-');
            if (fields.Length > 3 || fields[0] == ChainToolPartId) return false;
            first = fields.Length > 1 ? fields[1] : "";
            second = fields.Length > 2 ? fields[2] : "";
            if (id == "NUT") first = "Lock";
            if ((fields[0] == "OMNI" || fields[0] == "TWHL") && second == "") { second = first; first = "V1"; }
            if (fields[0] == "MOTR" && first == "") first = "11W";
            if (fields[0] == "BLCK" && first == "") first = "Normal";
            if (fields[0] == "PNMT" && second == "") second = "Normal";
            if (fields[0] == "RING" && first == "") first = "Red";
            foreach (var candidate in partTypes) {
                if (candidate.id != fields[0]) continue;
                var generator = candidate.GetComponent<PartGenerator>();
                if (generator == null) continue;
                string previous1 = generator.param1.value, previous2 = generator.param2.value;
                try {
                    generator.param1.value = first;
                    generator.param2.value = second;
                    if (!ValidParameter(generator.param1, generator.GetParam1Options(), first, generator.param1.customLimits)) continue;
                    var secondLimits = generator.param2.customLimits;
                    if (generator is ShaftPartGenerator) secondLimits.y = first == "High Strength" ? 24 : 12;
                    if (!ValidParameter(generator.param2, generator.GetParam2Options(), second, secondLimits)) continue;
                    type = candidate;
                    return true;
                } catch (Exception) { }
                finally { generator.param1.value = previous1; generator.param2.value = previous2; }
            }
            return false;
        }

        private static bool ValidParameter(Parameter parameter, List<string> options, string value, Vector2 limits) {
            if (string.IsNullOrEmpty(parameter.name)) return true;
            if (!parameter.custom) return options != null && options.Contains(value);
            if (!PartParameterValue.TryParse(value, out float number) || number <= 0) return false;
            return number >= limits.x && number <= limits.y
                && (parameter.customUnit != "Holes" || number == Mathf.Floor(number));
        }

        /// <Summary> Returns a list of all loaded parts in the current scene </Summary>
        public static List<GameObject> FindLoadedObjects() {
            return RobotDocument.GetObjects();
        }

        /// <Summary> Destroys all loaded parts in the current scene </Summary>
        public static void DestroyLoadedObjects() {
            RobotDocument.Clear();
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
            var icon = Resources.Load<Sprite>("Chain/ChainIcon");
            if (icon != null) return icon;
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
