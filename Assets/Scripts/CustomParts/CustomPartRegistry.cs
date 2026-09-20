using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;

namespace Protobot.CustomParts {
    public static class CustomPartRegistry {
        private static readonly Dictionary<string, CustomPartDefinition> Definitions =
            new Dictionary<string, CustomPartDefinition>(StringComparer.Ordinal);

        private static readonly Dictionary<string, CustomPartDefinition> Library = new Dictionary<string, CustomPartDefinition>(StringComparer.Ordinal);
        private static bool libraryLoaded;
        private static string LibraryDirectory => Path.Combine(Application.persistentDataPath, "CustomParts");

        private static void LoadLibrary() {
            if (libraryLoaded) return;
            libraryLoaded = true;
            if (!Directory.Exists(LibraryDirectory)) return;
            foreach (string path in Directory.GetFiles(LibraryDirectory, "*.json")) {
                try {
                    var definition = JsonUtility.FromJson<CustomPartDefinition>(File.ReadAllText(path));
                    if (definition != null && !string.IsNullOrWhiteSpace(definition.definitionId) && definition.sketch != null)
                        Library[definition.definitionId] = definition;
                } catch (Exception ex) { Debug.LogWarning("Could not load custom part library item: " + Path.GetFileName(path) + " (" + ex.Message + ")"); }
            }
        }

        public static bool SaveToLibrary(CustomPartDefinition definition) {
            if (definition == null) return false;
            LoadLibrary();
            try {
                Directory.CreateDirectory(LibraryDirectory);
                // IDs normally use GUIDs; encode any imported ID so it cannot become a path.
                string fileName = string.Concat(System.Text.Encoding.UTF8.GetBytes(definition.definitionId).Select(b => b.ToString("x2"))) + ".json";
                string path = Path.Combine(LibraryDirectory, fileName);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(definition, true));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                Library[definition.definitionId] = definition.CloneDeep();
                RegisterDefinition(definition.CloneDeep());
                return true;
            } catch (Exception ex) { Debug.LogError("Could not save the custom part library: " + ex.Message); return false; }
        }

        public static event Action OnRegistryChanged;

        public static int Count => Definitions.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Init() {
            Clear();
        }

        public static void Clear() {
            LoadLibrary();
            Definitions.Clear();
            foreach (var item in Library) Definitions[item.Key] = item.Value.CloneDeep();
            OnRegistryChanged?.Invoke();
        }

        public static bool Contains(string definitionId) {
            return !string.IsNullOrWhiteSpace(definitionId) && Definitions.ContainsKey(definitionId);
        }

        public static bool TryGetDefinition(string definitionId, out CustomPartDefinition definition) {
            if (string.IsNullOrWhiteSpace(definitionId)) {
                definition = null;
                return false;
            }

            return Definitions.TryGetValue(definitionId, out definition);
        }

        public static CustomPartDefinition RegisterDefinition(CustomPartDefinition definition, bool overwrite = true) {
            if (definition == null) return null;

            if (string.IsNullOrWhiteSpace(definition.definitionId)) {
                definition.definitionId = Guid.NewGuid().ToString("N");
            }

            if (!overwrite && Definitions.ContainsKey(definition.definitionId)) {
                return Definitions[definition.definitionId];
            }

            definition.Touch();
            Definitions[definition.definitionId] = definition;
            OnRegistryChanged?.Invoke();
            return definition;
        }

        public static void RegisterDefinitions(IEnumerable<CustomPartDefinition> definitions, bool overwrite = true) {
            if (definitions == null) return;

            bool changed = false;
            foreach (CustomPartDefinition definition in definitions) {
                if (definition == null) continue;

                if (string.IsNullOrWhiteSpace(definition.definitionId)) {
                    definition.definitionId = Guid.NewGuid().ToString("N");
                }

                if (!overwrite && Definitions.ContainsKey(definition.definitionId)) {
                    continue;
                }

                definition.Touch();
                Definitions[definition.definitionId] = definition;
                changed = true;
            }

            if (changed) {
                OnRegistryChanged?.Invoke();
            }
        }

        public static CustomPartDefinition[] GetAllDefinitions() {
            return Definitions.Values
                .Select(def => def)
                .OrderBy(def => def.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static CustomPartDefinition[] GetDefinitions(IEnumerable<string> definitionIds) {
            if (definitionIds == null) return Array.Empty<CustomPartDefinition>();

            var definitions = new List<CustomPartDefinition>();
            foreach (string definitionId in definitionIds.Distinct()) {
                if (TryGetDefinition(definitionId, out CustomPartDefinition definition)) {
                    definitions.Add(definition);
                }
            }

            return definitions.ToArray();
        }

    }
}
