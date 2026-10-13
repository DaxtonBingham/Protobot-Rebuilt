using System;
using System.Runtime.Serialization;
using UnityEngine;

namespace Protobot.Builds {
    [Serializable]
    public class ChainGuideData {
        [OptionalField] public int partIndex = -1;
        [OptionalField] public string socketId = "runtime-guide";
        [OptionalField] public double localX;
        [OptionalField] public double localY;
        [OptionalField] public double localZ;
        [OptionalField] public double rotX;
        [OptionalField] public double rotY;
        [OptionalField] public double rotZ;
        [OptionalField] public double rotW = 1d;
        [OptionalField] public float radius = 0.5f;
        [OptionalField] public int routingBias = 0;
        [OptionalField] public bool flipSide = false;
        [OptionalField] public float contactX, contactY, contactZ;
        public Vector3 ContactHint => new Vector3(contactX, contactY, contactZ);

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            socketId ??= "runtime-guide";
            if (Math.Abs(rotW) < 0.000001d && Math.Abs(rotX) < 0.000001d && Math.Abs(rotY) < 0.000001d && Math.Abs(rotZ) < 0.000001d) {
                rotW = 1d;
            }
        }

        public Vector3 GetLocalPosition() {
            return new Vector3((float)localX, (float)localY, (float)localZ);
        }

        public Quaternion GetLocalRotation() {
            Quaternion rotation = new Quaternion((float)rotX, (float)rotY, (float)rotZ, (float)rotW);
            float magnitudeSquared =
                (rotation.x * rotation.x)
                + (rotation.y * rotation.y)
                + (rotation.z * rotation.z)
                + (rotation.w * rotation.w);
            return magnitudeSquared > 0.000001f ? rotation.normalized : Quaternion.identity;
        }

        public override bool Equals(object obj) {
            var data = obj as ChainGuideData;
            if (data == null) {
                return false;
            }

            return partIndex == data.partIndex
                && socketId == data.socketId
                && GetLocalPosition() == data.GetLocalPosition()
                && GetLocalRotation() == data.GetLocalRotation()
                && Mathf.Abs(radius - data.radius) < 0.0001f
                && routingBias == data.routingBias
                && flipSide == data.flipSide && ContactHint == data.ContactHint;
        }

        public override int GetHashCode() {
            int hash = 17;
            hash = hash * 23 + partIndex.GetHashCode();
            hash = hash * 23 + (socketId == null ? 0 : socketId.GetHashCode());
            hash = hash * 23 + GetLocalPosition().GetHashCode();
            hash = hash * 23 + GetLocalRotation().GetHashCode();
            hash = hash * 23 + radius.GetHashCode();
            hash = hash * 23 + routingBias.GetHashCode();
            hash = hash * 23 + flipSide.GetHashCode();
            return hash * 23 + ContactHint.GetHashCode();
        }
    }
}
