using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Runtime.Serialization;
using Protobot.CustomParts;

namespace Protobot.Builds {
    [Serializable]
    public class BuildData {
        [OptionalField]
        public string name;
        [OptionalField]
        public string fileName; //the name used to determine path

        [OptionalField]
        public ObjectData[] parts = Array.Empty<ObjectData>();
        [OptionalField]
        public ChainData[] chains = Array.Empty<ChainData>();
        [OptionalField]
        public CustomPartDefinition[] customDefinitions = Array.Empty<CustomPartDefinition>();
        [OptionalField]
        public CameraData camera;

        [OptionalField]
        public string lastWriteTime;

        [OptionalField]
        public string version = AppData.Version;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            parts ??= Array.Empty<ObjectData>();
            chains ??= Array.Empty<ChainData>();
            customDefinitions ??= Array.Empty<CustomPartDefinition>();
            camera ??= new CameraData {
                xPos = 0d,
                yPos = 0d,
                zPos = 0d,
                xRot = 30d,
                yRot = 45d,
                zRot = 0d,
                zoom = -15d,
                isOrtho = false
            };
            version ??= AppData.Version;
        }

        public bool CompareData(BuildData data) {
            if (data == null) {
                return false;
            }

            int thisPartCount = parts == null ? 0 : parts.Length;
            int dataPartCount = data.parts == null ? 0 : data.parts.Length;
            if (thisPartCount != dataPartCount) {
                return false;
            }

            if (parts != null) {
                foreach (ObjectData part in parts) {
                    if (!data.parts.Contains(part))
                        return false;
                }
            }

            int thisChainCount = chains == null ? 0 : chains.Length;
            int dataChainCount = data.chains == null ? 0 : data.chains.Length;
            if (thisChainCount != dataChainCount) {
                return false;
            }

            if (chains != null) {
                foreach (ChainData chain in chains) {
                    if (!data.chains.Contains(chain)) {
                        return false;
                    }
                }
            }

            int thisCustomCount = customDefinitions == null ? 0 : customDefinitions.Length;
            int dataCustomCount = data.customDefinitions == null ? 0 : data.customDefinitions.Length;
            if (thisCustomCount != dataCustomCount) {
                return false;
            }

            if (!CustomPartDefinitionUtility.SequenceEquivalent(customDefinitions, data.customDefinitions)) {
                return false;
            }

            return true;
        }
    }
}
