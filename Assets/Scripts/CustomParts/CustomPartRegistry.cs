using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.CustomParts {
    public static class CustomPartRegistry {
        private static readonly Dictionary<string, CustomPartDefinition> Definitions =
            new Dictionary<string, CustomPartDefinition>(StringComparer.Ordinal);

        public static event Action OnRegistryChanged;

        public static int Count => Definitions.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Init() {
            Clear();
        }

        public static void Clear() {
            Definitions.Clear();
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
