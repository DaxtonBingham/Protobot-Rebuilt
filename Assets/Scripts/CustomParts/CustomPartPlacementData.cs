using UnityEngine;

namespace Protobot {
    public interface IGeneratedPlacementData {
        GameObject GeneratePlacedObject(Vector3 position, Quaternion rotation);
    }
}

namespace Protobot.CustomParts {
    public class CustomPartPlacementData : PlacementData, IGeneratedPlacementData {
        public string definitionId { get; }
        public override Transform placementTransform { get; set; }
        public override string objectId => CustomPartDefinition.CustomPartIdPrefix;

        public CustomPartPlacementData(string definitionId, Transform placementTransform) {
            this.definitionId = definitionId;
            this.placementTransform = placementTransform;
        }

        public override Mesh GetDisplayMesh() {
            if (!CustomPartRegistry.TryGetDefinition(definitionId, out CustomPartDefinition definition)) {
                return new Mesh();
            }

            if (CustomPartMeshBuilder.BuildMeshes(definition, out Mesh renderMesh, out _, out _)) {
                return renderMesh.CombineSubmeshes();
            }

            return new Mesh();
        }

        public GameObject GeneratePlacedObject(Vector3 position, Quaternion rotation) {
            return CustomPartGenerator.Generate(definitionId, position, rotation);
        }
    }
}
