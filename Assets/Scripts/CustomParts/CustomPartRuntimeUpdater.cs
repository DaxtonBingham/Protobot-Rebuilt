using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.CustomParts {
    // Only meshes built for a live edit are owned here. Shared library meshes are never destroyed.
    public sealed class CustomPartPreviewMeshes : MonoBehaviour {
        private Mesh renderMesh, colliderMesh;
        public void Replace(Mesh render, Mesh collider) {
            if (renderMesh != null && renderMesh != render) { CompactShadowMesh.Release(renderMesh); Destroy(renderMesh); }
            if (colliderMesh != null && colliderMesh != collider && colliderMesh != renderMesh) Destroy(colliderMesh);
            renderMesh = render; colliderMesh = collider;
        }
        private void OnDestroy() { Replace(null, null); }
    }

    public static class CustomPartRuntimeUpdater {
        public static bool ApplyDefinitionToObject(GameObject target, string definitionId, string customInstanceId = null) {
            if (target == null) return false;
            if (!CustomPartRegistry.TryGetDefinition(definitionId, out CustomPartDefinition definition)) return false;
            if (!ApplyDefinitionPreviewToObject(target, definition, true)) return false;
            CustomPartGenerator.AddPartMetadata(target, definition, customInstanceId);
            return true;
        }

        public static bool ApplyDefinitionPreviewToObject(GameObject target, CustomPartDefinition definition, bool useCache = false) {
            if (target == null || definition == null) return false;
            var geometry = CustomPartMeshBuilder.Compile(definition, CustomPartMeshBuilder.GeometryKey(definition));
            return ApplyCompiledPreview(target, definition, geometry, useCache);
        }

        public static bool ApplyCompiledPreview(GameObject target, CustomPartDefinition definition, CustomPartMeshBuilder.GeometryData geometry, bool useCache = false) {
            if (target == null || definition == null) return false;
            if (!CustomPartMeshBuilder.CreateMeshes(geometry, definition.name, out Mesh renderMesh, out Mesh colliderMesh, out var holes, useCache)) return false;

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

            var owned = target.GetComponent<CustomPartPreviewMeshes>();
            if (!useCache && owned == null) owned = target.AddComponent<CustomPartPreviewMeshes>();
            if (owned != null) owned.Replace(useCache ? null : renderMesh, useCache ? null : colliderMesh);

            RemoveExistingHoleColliders(target);
            CustomPartGenerator.AddHoleColliders(target, holes);

            target.name = string.IsNullOrWhiteSpace(definition.name) ? "Custom Part" : definition.name;
            RobotDocument.Synchronize(target.GetComponent<SavedObject>(), PartChange.Geometry | PartChange.Appearance | PartChange.Metadata);
            SceneActivity.Changed();
            return true;
        }

        public static int ApplyDefinitionToAllInstances(string oldDefinitionId, string newDefinitionId = null) {
            if (string.IsNullOrWhiteSpace(oldDefinitionId)) return 0;
            if (string.IsNullOrWhiteSpace(newDefinitionId)) newDefinitionId = oldDefinitionId;

            var records = RobotDocument.ActiveSnapshot(out int count);
            int updated = 0;
            for (int i = 0; i < count; i++) {
                var savedObject = records[i].View;
                if (savedObject == null) continue;
                if (!string.Equals(savedObject.customDefinitionId, oldDefinitionId)) continue;
                if (ApplyDefinitionToObject(savedObject.gameObject, newDefinitionId, savedObject.customInstanceId)) {
                    updated++;
                }
            }

            return updated;
        }

        private static void RemoveExistingHoleColliders(GameObject target) {
            target.GetComponent<PartHoles>()?.Clear();
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
