using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.CustomParts {
    public static class CustomPartRuntimeUpdater {
        public static bool ApplyDefinitionToObject(GameObject target, string definitionId, string customInstanceId = null) {
            if (target == null) return false;
            if (!CustomPartRegistry.TryGetDefinition(definitionId, out CustomPartDefinition definition)) return false;
            if (!ApplyDefinitionPreviewToObject(target, definition)) return false;
            CustomPartGenerator.AddPartMetadata(target, definition, customInstanceId);
            return true;
        }

        public static bool ApplyDefinitionPreviewToObject(GameObject target, CustomPartDefinition definition) {
            if (target == null || definition == null) return false;
            if (!CustomPartMeshBuilder.BuildMeshes(definition, out Mesh renderMesh, out Mesh colliderMesh, out var holes)) return false;

            MeshFilter meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = target.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = renderMesh;

            MeshRenderer meshRenderer = target.GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = target.AddComponent<MeshRenderer>();
            if (meshRenderer.sharedMaterial == null) {
                meshRenderer.sharedMaterial = CustomPartGenerator.GetRuntimeMaterial();
            }

            MeshCollider meshCollider = target.GetComponent<MeshCollider>();
            if (meshCollider == null) meshCollider = target.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = colliderMesh;

            RemoveExistingHoleColliders(target);
            CustomPartGenerator.AddHoleColliders(target, holes);

            target.name = string.IsNullOrWhiteSpace(definition.name) ? "Custom Part" : definition.name;
            return true;
        }

        public static int ApplyDefinitionToAllInstances(string oldDefinitionId, string newDefinitionId = null) {
            if (string.IsNullOrWhiteSpace(oldDefinitionId)) return 0;
            if (string.IsNullOrWhiteSpace(newDefinitionId)) newDefinitionId = oldDefinitionId;

            SavedObject[] savedObjects = Object.FindObjectsOfType<SavedObject>();
            int updated = 0;
            foreach (SavedObject savedObject in savedObjects) {
                if (savedObject == null) continue;
                if (!string.Equals(savedObject.customDefinitionId, oldDefinitionId)) continue;
                if (ApplyDefinitionToObject(savedObject.gameObject, newDefinitionId, savedObject.customInstanceId)) {
                    updated++;
                }
            }

            return updated;
        }

        private static void RemoveExistingHoleColliders(GameObject target) {
            var holeObjects = target.GetComponentsInChildren<HoleCollider>(true)
                .Select(hole => hole.gameObject)
                .Where(go => go != null)
                .ToList();

            foreach (GameObject holeObject in holeObjects) {
                if (holeObject.transform.parent == target.transform) {
                    HoleCollider holeCollider = holeObject.GetComponent<HoleCollider>();
                    if (holeCollider != null) {
                        holeCollider.DetachAllDetectors();
                    }

                    Object.Destroy(holeObject);
                }
            }
        }
    }
}
