using System;
using System.Linq;
using System.Runtime.Serialization;

namespace Protobot.Builds {
    [Serializable]
    public class ChainData {
        [OptionalField] public int[] endpointIndices = Array.Empty<int>();
        [OptionalField] public string[] endpointSockets = Array.Empty<string>();
        [OptionalField] public int endpointAIndex = -1;
        [OptionalField] public int endpointBIndex = -1;
        [OptionalField] public string endpointASocket = "main";
        [OptionalField] public string endpointBSocket = "main";
        [OptionalField] public string standard = "Pitch6p35";
        [OptionalField] public float slack = 0f;
        [Obsolete("Legacy field kept for backward build compatibility.")]
        [OptionalField] public bool bidirectionalBind = true;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            endpointIndices ??= Array.Empty<int>();
            endpointSockets ??= Array.Empty<string>();
            endpointASocket ??= "main";
            endpointBSocket ??= "main";
            standard ??= "Pitch6p35";
        }

        public int OrderedEndpointCount {
            get {
                if (endpointIndices != null && endpointIndices.Length > 0) {
                    return endpointIndices.Length;
                }

                return endpointAIndex >= 0 && endpointBIndex >= 0 ? 2 : 0;
            }
        }

        public bool TryGetEndpointReference(int orderedIndex, out int endpointIndex, out string socketId) {
            endpointIndex = -1;
            socketId = "main";

            if (orderedIndex < 0) {
                return false;
            }

            if (endpointIndices != null && endpointIndices.Length > 0) {
                if (orderedIndex >= endpointIndices.Length) {
                    return false;
                }

                endpointIndex = endpointIndices[orderedIndex];
                if (endpointSockets != null && orderedIndex < endpointSockets.Length && !string.IsNullOrWhiteSpace(endpointSockets[orderedIndex])) {
                    socketId = endpointSockets[orderedIndex];
                }

                return endpointIndex >= 0;
            }

            switch (orderedIndex) {
                case 0:
                    endpointIndex = endpointAIndex;
                    socketId = string.IsNullOrWhiteSpace(endpointASocket) ? "main" : endpointASocket;
                    return endpointIndex >= 0;
                case 1:
                    endpointIndex = endpointBIndex;
                    socketId = string.IsNullOrWhiteSpace(endpointBSocket) ? "main" : endpointBSocket;
                    return endpointIndex >= 0;
                default:
                    return false;
            }
        }

        private int[] GetNormalizedIndices() {
            if (endpointIndices != null && endpointIndices.Length > 0) {
                return endpointIndices;
            }

            return new[] { endpointAIndex, endpointBIndex };
        }

        private string[] GetNormalizedSockets() {
            if (endpointSockets != null && endpointSockets.Length > 0) {
                return endpointSockets.Select(socket => string.IsNullOrWhiteSpace(socket) ? "main" : socket).ToArray();
            }

            return new[] {
                string.IsNullOrWhiteSpace(endpointASocket) ? "main" : endpointASocket,
                string.IsNullOrWhiteSpace(endpointBSocket) ? "main" : endpointBSocket
            };
        }

        public override bool Equals(object obj) {
            var data = obj as ChainData;
            if (data == null) {
                return false;
            }

            return GetNormalizedIndices().SequenceEqual(data.GetNormalizedIndices())
                && GetNormalizedSockets().SequenceEqual(data.GetNormalizedSockets())
                && standard == data.standard
                && Math.Abs(slack - data.slack) < 0.0001f;
        }

        public override int GetHashCode() {
            int hash = 17;
            int[] indices = GetNormalizedIndices();
            for (int i = 0; i < indices.Length; i++) {
                hash = hash * 23 + indices[i].GetHashCode();
            }

            string[] sockets = GetNormalizedSockets();
            for (int i = 0; i < sockets.Length; i++) {
                hash = hash * 23 + (sockets[i] == null ? 0 : sockets[i].GetHashCode());
            }

            hash = hash * 23 + (standard == null ? 0 : standard.GetHashCode());
            hash = hash * 23 + slack.GetHashCode();
            return hash;
        }
    }
}
