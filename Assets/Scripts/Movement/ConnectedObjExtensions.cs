using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Protobot.ChainSystem;

namespace Protobot {
    public static class ConnectedObjExtensions {
        public static List<GameObject> GetConnectedObjects(this GameObject obj, bool includeObj = false, bool onlyParts = false) {
            if (obj == null) return null;

            var connectedObjs = new List<GameObject>();

            if (obj.TryGetComponent(out ChainConnection chainConnection)) {
                for (int i = 0; i < chainConnection.Endpoints.Count; i++) {
                    AddChainEndpointObject(chainConnection.Endpoints[i], connectedObjs);
                }
            }
            
            if (obj.TryGetComponent(out Pivot pivot)) {
                connectedObjs.AddRange(pivot.objects);
            }

            if (includeObj && !obj.CompareTag("Group"))
                connectedObjs.Add(obj);

            if (obj.TryGetComponent(out HoleFace holeFace)) {
                if (onlyParts && includeObj)
                    connectedObjs.Remove(obj);

                connectedObjs.Add(holeFace.hole.part.gameObject);

                var holeObjs = holeFace.hole.part.GetConnectedObjects(true, onlyParts);
                connectedObjs.AddRange(holeObjs);
            }

            if (obj.TryGetGroup(out List<GameObject> groupObjs)) {
                connectedObjs.AddRange(groupObjs);
                if (!includeObj) {
                    connectedObjs.Remove(obj);
                }
            }

            return connectedObjs.Distinct().ToList();
        }

        private static void AddChainEndpointObject(ChainEndpoint endpoint, List<GameObject> connectedObjs) {
            if (endpoint == null || connectedObjs == null) {
                return;
            }

            GameObject endpointObject = endpoint.gameObject;
            if (endpointObject == null) {
                return;
            }

            if (endpointObject.TryGetGroup(out Transform groupTransform) && groupTransform != null) {
                connectedObjs.Add(groupTransform.gameObject);
                return;
            }

            connectedObjs.Add(endpointObject);
        }
    }
}
