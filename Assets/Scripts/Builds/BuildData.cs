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
        public ChainGuideData[] chainGuides = Array.Empty<ChainGuideData>();
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
            chainGuides ??= Array.Empty<ChainGuideData>();
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
            if (data == null) return false;
            var left = parts ?? Array.Empty<ObjectData>();
            var right = data.parts ?? Array.Empty<ObjectData>();
            if (left.Length != right.Length) return false;
            var matched = new bool[right.Length];
            var indices = new int[left.Length];
            for (int i = 0; i < left.Length; i++) {
                int found = -1;
                for (int j = 0; j < right.Length; j++) {
                    if (!matched[j] && Equals(left[i], right[j])) { found = j; break; }
                }
                if (found < 0) return false;
                matched[found] = true;
                indices[i] = found;
            }
            var remappedChains = (chains ?? Array.Empty<ChainData>()).Select(chain => {
                if (chain == null) return null;
                int count = chain.OrderedEndpointCount;
                var references = new int[count];
                var sockets = new string[count];
                for (int i = 0; i < count; i++) {
                    chain.TryGetEndpointReference(i, out int index, out sockets[i]);
                    references[i] = index >= 0 && index < indices.Length ? indices[index] : -1;
                }
                return new ChainData { endpointIndices = references, endpointSockets = sockets, standard = chain.standard, slack = chain.slack };
            }).ToArray();
            var remappedGuides = (chainGuides ?? Array.Empty<ChainGuideData>()).Select(guide => guide == null ? null : new ChainGuideData {
                partIndex = guide.partIndex >= 0 && guide.partIndex < indices.Length ? indices[guide.partIndex] : -1,
                socketId = guide.socketId, localX = guide.localX, localY = guide.localY, localZ = guide.localZ,
                rotX = guide.rotX, rotY = guide.rotY, rotZ = guide.rotZ, rotW = guide.rotW,
                radius = guide.radius, routingBias = guide.routingBias, flipSide = guide.flipSide,
                contactX = guide.contactX, contactY = guide.contactY, contactZ = guide.contactZ
            }).ToArray();
            return SameMultiset(remappedChains, data.chains)
                && SameMultiset(remappedGuides, data.chainGuides)
                && CustomPartDefinitionUtility.SequenceEquivalent(customDefinitions, data.customDefinitions);
        }

        private static bool SameMultiset<T>(T[] left, T[] right) {
            left ??= Array.Empty<T>(); right ??= Array.Empty<T>();
            if (left.Length != right.Length) return false;
            var used = new bool[right.Length];
            foreach (var item in left) {
                int found = -1;
                for (int i = 0; i < right.Length; i++)
                    if (!used[i] && Equals(item, right[i])) { found = i; break; }
                if (found < 0) return false;
                used[found] = true;
            }
            return true;
        }
    }
}
