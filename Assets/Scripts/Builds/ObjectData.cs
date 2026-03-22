using UnityEngine;
using System.Collections;
using System;
using System.Runtime.Serialization;
using UnityEngine.Serialization;

namespace Protobot.Builds {
    [Serializable]
    public class ObjectData {
        [OptionalField] public double xPos, yPos, zPos;
        [OptionalField] public double xRot, yRot, zRot;
        [OptionalField] public double rColor, gColor, bColor;
        [OptionalField] public string states;
        [OptionalField]
        [FormerlySerializedAs("meshId")] public string partId;
        [OptionalField] public string meshId;
        [OptionalField] public string customDefinitionId;
        [OptionalField] public string customInstanceId;
        public Vector3 GetPos() => new Vector3((float)xPos, (float)yPos, (float)zPos);
        public Quaternion GetRot() => Quaternion.Euler((float)xRot, (float)yRot, (float)zRot);
        public Color GetColor() => new Color((float)rColor, (float)gColor, (float)bColor, 1);

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            if (string.IsNullOrWhiteSpace(partId) && !string.IsNullOrWhiteSpace(meshId)) {
                partId = meshId;
            }

            states ??= string.Empty;
        }

        public override bool Equals(object obj) {
            var data = obj as ObjectData;
            if (data == null) {
                return false;
            }
             
            if (data.GetPos() != GetPos()) return false;
            if (data.GetRot() != GetRot()) return false;
            if (data.partId != partId) return false;
            if (data.customDefinitionId != customDefinitionId) return false;
            if (data.customInstanceId != customInstanceId) return false;

            return true;
        }
    }
}
