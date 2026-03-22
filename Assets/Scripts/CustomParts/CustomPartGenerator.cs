using System;
using Parts_List;
using UnityEngine;

namespace Protobot.CustomParts {
    public static class CustomPartGenerator {
        private static Material runtimeMaterial;

        private static Material RuntimeMaterial {
            get {
                if (runtimeMaterial != null) return runtimeMaterial;

                Shader shader = Shader.Find("Standard");
                runtimeMaterial = new Material(shader) {
                    name = "Custom Part Runtime Material",
                    color = new Color(0.52f, 0.52f, 0.52f, 1f)
                };

                if (runtimeMaterial.HasProperty("_Glossiness")) {
                    runtimeMaterial.SetFloat("_Glossiness", 0.2f);
                }
                if (runtimeMaterial.HasProperty("_Metallic")) {
                    runtimeMaterial.SetFloat("_Metallic", 0.754f);
                }
                return runtimeMaterial;
            }
        }

        public static Material GetRuntimeMaterial() {
            return RuntimeMaterial;
        }

        public static GameObject Generate(string definitionId, Vector3 position, Quaternion rotation, string customInstanceId = null) {
            if (!CustomPartRegistry.TryGetDefinition(definitionId, out CustomPartDefinition definition)) {
                Debug.LogError($"Custom part definition not found: {definitionId}");
                return null;
            }

            if (!CustomPartMeshBuilder.BuildMeshes(definition, out Mesh renderMesh, out Mesh colliderMesh, out var holes)) {
                Debug.LogError($"Custom part mesh build failed for definition: {definitionId}");
                return null;
            }

            GameObject newPart = new GameObject(string.IsNullOrWhiteSpace(definition.name) ? "Custom Part" : definition.name);
            if (TagExists("Object")) {
                newPart.tag = "Object";
            }

            newPart.transform.position = position;
            newPart.transform.rotation = rotation;

            MeshFilter meshFilter = newPart.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = renderMesh;

            MeshRenderer meshRenderer = newPart.AddComponent<MeshRenderer>();
            meshRenderer.material = RuntimeMaterial;

            MeshCollider meshCollider = newPart.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = colliderMesh;

            AddHoleColliders(newPart, holes);
            AddPartMetadata(newPart, definition, customInstanceId);

            return newPart;
        }

        public static void AddPartMetadata(GameObject newPart, CustomPartDefinition definition, string customInstanceId) {
            SavedObject savedObject = newPart.GetComponent<SavedObject>();
            if (savedObject == null) {
                savedObject = newPart.AddComponent<SavedObject>();
            }

            if (string.IsNullOrWhiteSpace(customInstanceId)) {
                customInstanceId = Guid.NewGuid().ToString("N");
            }

            savedObject.id = definition.GetPartId();
            savedObject.customDefinitionId = definition.definitionId;
            savedObject.customInstanceId = customInstanceId;

            PartName partName = newPart.GetComponent<PartName>();
            if (partName == null) {
                partName = newPart.AddComponent<PartName>();
            }

            partName.name = string.IsNullOrWhiteSpace(definition.name) ? "Custom Part" : definition.name;
            partName.weightInGrams = EstimateWeight(newPart, definition.thicknessInches);
        }

        private static float EstimateWeight(GameObject gameObject, float thicknessInches) {
            MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) {
                return 0f;
            }

            Bounds bounds = meshFilter.sharedMesh.bounds;
            float width = Mathf.Abs(bounds.size.x);
            float height = Mathf.Abs(bounds.size.y);
            float thickness = Mathf.Max(0.001f, thicknessInches);

            // Approximate aluminum-like plate density scaling for sensible part-list output.
            float volume = width * height * thickness;
            return volume * 55f;
        }

        public static void AddHoleColliders(GameObject parent, System.Collections.Generic.List<CustomPartMeshBuilder.HoleRuntimeData> holes) {
            if (holes == null || holes.Count == 0) return;

            foreach (CustomPartMeshBuilder.HoleRuntimeData hole in holes) {
                Mesh shapeMesh = ResolveHoleMesh(hole.shape);
                if (shapeMesh == null) {
                    continue;
                }

                GameObject holeObject = new GameObject("HoleCollider", typeof(MeshCollider));
                if (TagExists("HoleCollider")) {
                    holeObject.tag = "HoleCollider";
                }

                holeObject.layer = HoleCollider.HOLE_COLLISIONS_LAYER;

                holeObject.transform.SetParent(parent.transform, false);
                holeObject.transform.localPosition = hole.localPosition;
                holeObject.transform.localRotation = hole.localRotation;
                holeObject.transform.localScale = hole.localScale;

                MeshCollider meshCollider = holeObject.GetComponent<MeshCollider>();
                meshCollider.sharedMesh = shapeMesh;

                HoleCollider holeCollider = holeObject.AddComponent<HoleCollider>();
                holeCollider.holeType = HoleCollider.HoleType.Normal;
                holeCollider.twoSided = true;
            }
        }

        private static Mesh ResolveHoleMesh(CustomHoleShape shape) {
            if (HoleShapes.instance == null) return null;

            switch (shape) {
            case CustomHoleShape.Square:
                return HoleShapes.instance.GetShapeMesh("square");
            default:
                return HoleShapes.instance.GetShapeMesh("circle");
            }
        }

        private static bool TagExists(string tag) {
            if (string.IsNullOrWhiteSpace(tag)) return false;
            if (string.Equals(tag, "Untagged", StringComparison.Ordinal)) return true;

            try {
                GameObject.FindGameObjectWithTag(tag);
                return true;
            }
            catch {
                return false;
            }
        }
    }
}
