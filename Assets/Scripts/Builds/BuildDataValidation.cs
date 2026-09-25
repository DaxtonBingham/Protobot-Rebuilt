using System;
using System.Collections.Generic;
using Protobot.CustomParts;

namespace Protobot.Builds {
    public static class BuildDataValidation {
        private static bool Finite(params double[] values) {
            foreach (double value in values)
                if (double.IsNaN(value) || double.IsInfinity(value) || value > float.MaxValue || value < -float.MaxValue) return false;
            return true;
        }

        public static bool TryValidate(BuildData build, out string error) {
            error = "Invalid build data.";
            if (build == null) return false;
            var parts = build.parts ?? Array.Empty<ObjectData>();
            var definitions = new Dictionary<string, CustomPartDefinition>(StringComparer.Ordinal);
            foreach (var definition in build.customDefinitions ?? Array.Empty<CustomPartDefinition>()) {
                if (definition == null || string.IsNullOrWhiteSpace(definition.definitionId) || definitions.ContainsKey(definition.definitionId)) {
                    error = "A custom part definition is missing or duplicated."; return false;
                }
                definitions.Add(definition.definitionId, definition);
            }
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var checkedDefinitions = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < parts.Length; i++) {
                var part = parts[i];
                error = $"Part {i + 1} has invalid data.";
                if (part == null || (string.IsNullOrWhiteSpace(part.partId) && string.IsNullOrWhiteSpace(part.customDefinitionId))) return false;
                if (!Finite(part.xPos, part.yPos, part.zPos, part.xRot, part.yRot, part.zRot, part.rColor, part.gColor, part.bColor)) return false;
                if (!string.IsNullOrEmpty(part.instanceId) && !identities.Add(part.instanceId)) { error = "Two parts share the same identity."; return false; }
                if (string.IsNullOrWhiteSpace(part.customDefinitionId)) continue;
                if (!definitions.TryGetValue(part.customDefinitionId, out var definition)) { error = "A placed custom part is missing its definition."; return false; }
                if (checkedDefinitions.Add(part.customDefinitionId)) {
                    var key = CustomPartMeshBuilder.GeometryKey(definition);
                    if (!CustomPartMeshBuilder.Compile(definition, key).Valid) { error = "A placed custom part has invalid geometry."; return false; }
                }
            }
            var camera = build.camera;
            if (camera != null && !Finite(camera.xPos, camera.yPos, camera.zPos, camera.xRot, camera.yRot, camera.zRot, camera.zoom)) {
                error = "The saved camera has invalid coordinates."; return false;
            }
            foreach (var guide in build.chainGuides ?? Array.Empty<ChainGuideData>()) {
                error = "A chain guide has invalid data or refers to a missing part.";
                if (guide == null || guide.partIndex < 0 || guide.partIndex >= parts.Length || guide.radius <= 0
                    || !Finite(guide.localX, guide.localY, guide.localZ, guide.rotX, guide.rotY, guide.rotZ, guide.rotW,
                        guide.radius, guide.contactX, guide.contactY, guide.contactZ)) return false;
            }
            foreach (var chain in build.chains ?? Array.Empty<ChainData>()) {
                error = "A chain has invalid data or refers to a missing part.";
                if (chain == null || chain.OrderedEndpointCount < 2 || !Finite(chain.slack)) return false;
                for (int i = 0; i < chain.OrderedEndpointCount; i++)
                    if (!chain.TryGetEndpointReference(i, out int index, out _) || index >= parts.Length) return false;
            }
            error = null;
            return true;
        }
    }
}
