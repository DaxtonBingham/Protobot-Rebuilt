using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Builds;
using UnityEngine;

namespace Protobot.ChainSystem {
    public class ChainManager : MonoBehaviour {
        private static ChainManager instance;
        private readonly List<ChainConnection> connections = new List<ChainConnection>();

        public static IReadOnlyList<ChainConnection> Connections => Instance.connections;

        [RuntimeInitializeOnLoadMethod]
        private static void RuntimeInit() {
            EnsureInstance();
        }

        private static void EnsureInstance() {
            if (instance != null) {
                return;
            }

            ChainManager existing = FindObjectOfType<ChainManager>();
            if (existing != null) {
                instance = existing;
                return;
            }

            var managerObject = new GameObject("Chain Manager");
            instance = managerObject.AddComponent<ChainManager>();
            DontDestroyOnLoad(managerObject);
        }

        public static ChainManager Instance {
            get {
                EnsureInstance();
                return instance;
            }
        }

        public static GameObject ResolveSelectableObject(GameObject obj) {
            if (obj == null) {
                return null;
            }

            ChainConnection chainConnection = obj.GetComponentInParent<ChainConnection>();
            return chainConnection != null ? chainConnection.gameObject : obj;
        }

        private void Awake() {
            if (instance != null && instance != this) {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy() {
            if (instance == this) {
                instance = null;
            }
        }

        internal static void Register(ChainConnection connection) {
            if (connection == null) {
                return;
            }

            if (!Instance.connections.Contains(connection)) {
                Instance.connections.Add(connection);
            }
        }

        internal static void Unregister(ChainConnection connection) {
            if (connection == null || instance == null) {
                return;
            }

            instance.connections.Remove(connection);
        }

        public static void ClearAll() {
            if (instance == null) {
                return;
            }

            var toRemove = instance.connections.Where(c => c != null).ToList();
            instance.connections.Clear();

            foreach (ChainConnection connection in toRemove) {
                if (connection != null) {
                    Destroy(connection.gameObject);
                }
            }
        }

        public static bool EndpointHasChain(ChainEndpoint endpoint) {
            if (endpoint == null || instance == null) {
                return false;
            }

            for (int i = 0; i < instance.connections.Count; i++) {
                ChainConnection connection = instance.connections[i];
                if (connection == null || !connection.gameObject.activeInHierarchy) {
                    continue;
                }

                if (connection.ContainsEndpoint(endpoint)) {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetConnectionForEndpoint(ChainEndpoint endpoint, out ChainConnection connection) {
            connection = null;
            if (endpoint == null || instance == null) {
                return false;
            }

            for (int i = 0; i < instance.connections.Count; i++) {
                ChainConnection current = instance.connections[i];
                if (current == null || !current.gameObject.activeInHierarchy) {
                    continue;
                }

                if (!current.ContainsEndpoint(endpoint)) {
                    continue;
                }

                connection = current;
                return true;
            }

            return false;
        }

        public static bool TryCreateBoundChain(
            ChainEndpoint endpointA,
            ChainEndpoint endpointB,
            ChainSettings settings,
            out ChainConnection connection,
            out string errorMessage) {
            return TryCreateBoundChain(new[] { endpointA, endpointB }, settings, out connection, out errorMessage);
        }

        public static bool TryCreateBoundChain(
            IReadOnlyList<ChainEndpoint> inputEndpoints,
            ChainSettings settings,
            out ChainConnection connection,
            out string errorMessage, ChainConnection reusablePreview = null, bool preview = false) {
            connection = null;
            errorMessage = string.Empty;

            if (!TryNormalizeEndpoints(inputEndpoints, out List<ChainEndpoint> endpoints, out errorMessage)) {
                return false;
            }

            settings = settings ?? ChainSettings.CreateDefault();

            if (!ChainSprocketUtility.TryResolveCompatibleStandard(
                endpoints,
                settings.standard,
                settings.autoResolveStandard,
                out ChainStandard resolvedStandard,
                out errorMessage)) {
                return false;
            }
            settings.standard = resolvedStandard;

            if (settings.singleChainPerEndpoint) {
                for (int i = 0; i < endpoints.Count; i++) {
                    if (EndpointHasChain(endpoints[i])) {
                        errorMessage = "One of the selected sprockets already has a chain.";
                        return false;
                    }
                }
            }

            bool canAutoAlign = settings.autoAlignSecondEndpoint && endpoints.Count == 2;
            Transform endpointBTransform = null;
            Vector3 endpointBStartPosition = Vector3.zero;
            Quaternion endpointBStartRotation = Quaternion.identity;
            bool endpointBAutoAligned = false;

            if (canAutoAlign) {
                endpointBTransform = GetBindingTransformForEndpoint(endpoints[1]);
                if (endpointBTransform == null) {
                    errorMessage = "Could not resolve a valid transform for the second sprocket.";
                    return false;
                }
                endpointBStartPosition = endpointBTransform.position;
                endpointBStartRotation = endpointBTransform.rotation;

                if (!TryAutoAlignSecondEndpoint(endpoints[0], endpoints[1], settings, out errorMessage)) {
                    return false;
                }
                endpointBAutoAligned = true;
            }

            if (!ChainPathSolver.TrySolve(endpoints, settings.standard, ChainSprocketUtility.ResolvePitch(endpoints, settings.standard), settings.slack, out var solvedRoute, out errorMessage)) {
                if (endpointBAutoAligned && endpointBTransform != null) {
                    endpointBTransform.SetPositionAndRotation(endpointBStartPosition, endpointBStartRotation);
                }
                return false;
            }

            connection = reusablePreview != null && reusablePreview.IsPreview && reusablePreview.Settings.standard == settings.standard ? reusablePreview : null;
            GameObject chainObject = connection != null ? connection.gameObject : new GameObject("Chain Connection");
            if (connection == null) connection = chainObject.AddComponent<ChainConnection>();
            connection.SetPreviewMode(preview);

            if (!connection.Initialize(endpoints, settings, solvedRoute)) {
                Destroy(chainObject);
                connection = null;
                if (endpointBAutoAligned && endpointBTransform != null) {
                    endpointBTransform.SetPositionAndRotation(endpointBStartPosition, endpointBStartRotation);
                }
                errorMessage = "Failed to initialize chain connection.";
                return false;
            }

            connection.gameObject.SetActive(true);
            Register(connection);
            return true;
        }

        private static bool TryAutoAlignSecondEndpoint(
            ChainEndpoint endpointA,
            ChainEndpoint endpointB,
            ChainSettings settings,
            out string errorMessage) {
            errorMessage = string.Empty;

            Transform secondTransform = GetBindingTransformForEndpoint(endpointB);
            if (secondTransform == null) {
                errorMessage = "Could not resolve a valid transform for auto-align.";
                return false;
            }
            Vector3 initialPosition = secondTransform.position;
            Quaternion initialRotation = secondTransform.rotation;

            Vector3 axisA = endpointA.WorldAxis.normalized;
            Vector3 axisBSame = endpointB.WorldAxis.normalized;

            Quaternion toSame = Quaternion.FromToRotation(axisBSame, axisA);
            Quaternion toOpposite = Quaternion.FromToRotation(axisBSame, -axisA);

            Quaternion chosen = Quaternion.Angle(Quaternion.identity, toSame) <= Quaternion.Angle(Quaternion.identity, toOpposite)
                ? toSame
                : toOpposite;

            secondTransform.rotation = chosen * secondTransform.rotation;

            Vector3 centerA = endpointA.WorldCenter;
            Vector3 centerB = endpointB.WorldCenter;

            Vector3 centerBProjected = centerB - axisA * Vector3.Dot(centerB - centerA, axisA);
            Vector3 radialDirection = centerBProjected - centerA;

            if (radialDirection.sqrMagnitude < 0.0001f) {
                radialDirection = Vector3.ProjectOnPlane(secondTransform.right, axisA);
                if (radialDirection.sqrMagnitude < 0.0001f) {
                    radialDirection = Vector3.ProjectOnPlane(secondTransform.up, axisA);
                }
            }

            if (radialDirection.sqrMagnitude < 0.0001f) {
                secondTransform.position = initialPosition;
                secondTransform.rotation = initialRotation;
                errorMessage = "Could not determine a valid chain direction for auto-align.";
                return false;
            }

            radialDirection.Normalize();
            float radiusA = ChainSprocketUtility.ResolvePitchRadius(endpointA, settings.standard);
            float radiusB = ChainSprocketUtility.ResolvePitchRadius(endpointB, settings.standard);
            float minCenterDistance = Mathf.Abs(radiusA - radiusB) + 0.05f;
            float currentDistance = Vector3.Distance(centerA, centerBProjected);
            float targetDistance = Mathf.Max(currentDistance, minCenterDistance);

            Vector3 targetCenter = centerA + radialDirection * targetDistance;
            Vector3 targetShift = targetCenter - centerB;

            if (targetShift.magnitude > settings.autoAlignMaxDistance) {
                secondTransform.position = initialPosition;
                secondTransform.rotation = initialRotation;
                errorMessage = "Second sprocket is too far to auto-align.";
                return false;
            }

            secondTransform.position += targetShift;
            return true;
        }

        internal static Transform GetBindingTransformForEndpoint(ChainEndpoint endpoint) {
            return ChainSprocketUtility.ResolveBindingTransform(endpoint);
        }

        internal static bool SnapEndpointsToPlane(IReadOnlyList<ChainEndpoint> endpoints, IReadOnlyList<int> changedIndices) {
            if (endpoints == null || changedIndices == null || endpoints.Count < 2 || changedIndices.Count == 0) {
                return false;
            }

            Vector3 planeNormal = Vector3.zero;
            for (int i = 0; i < endpoints.Count; i++) {
                ChainEndpoint endpoint = endpoints[i];
                if (endpoint == null) {
                    continue;
                }

                Vector3 axis = endpoint.WorldAxis;
                if (axis.sqrMagnitude > 0.0001f) {
                    planeNormal = axis.normalized;
                    break;
                }
            }

            if (planeNormal.sqrMagnitude < 0.0001f) {
                return false;
            }

            float referenceDepth = 0f;
            int referenceCount = 0;

            for (int i = 0; i < endpoints.Count; i++) {
                if (ContainsIndex(changedIndices, i)) {
                    continue;
                }

                ChainEndpoint endpoint = endpoints[i];
                if (endpoint == null || endpoint.IsGuideEndpoint) {
                    continue;
                }

                referenceDepth += Vector3.Dot(endpoint.WorldCenter, planeNormal);
                referenceCount++;
            }

            if (referenceCount == 0) {
                for (int i = 0; i < endpoints.Count; i++) {
                    ChainEndpoint endpoint = endpoints[i];
                    if (endpoint == null || endpoint.IsGuideEndpoint) {
                        continue;
                    }

                    referenceDepth += Vector3.Dot(endpoint.WorldCenter, planeNormal);
                    referenceCount++;
                }
            }

            if (referenceCount == 0) {
                return false;
            }

            referenceDepth /= referenceCount;

            bool adjusted = false;
            for (int i = 0; i < changedIndices.Count; i++) {
                int index = changedIndices[i];
                if (index < 0 || index >= endpoints.Count) {
                    continue;
                }

                ChainEndpoint endpoint = endpoints[index];
                Transform target = GetBindingTransformForEndpoint(endpoint);
                if (endpoint == null || endpoint.IsGuideEndpoint || target == null) {
                    continue;
                }

                float depth = Vector3.Dot(endpoint.WorldCenter, planeNormal);
                float depthDelta = referenceDepth - depth;
                if (Mathf.Abs(depthDelta) <= 0.0001f) {
                    continue;
                }

                target.position += planeNormal * depthDelta;
                adjusted = true;
            }

            return adjusted;
        }

        private static bool ContainsIndex(IReadOnlyList<int> indices, int value) {
            for (int i = 0; i < indices.Count; i++) {
                if (indices[i] == value) {
                    return true;
                }
            }

            return false;
        }

        public static void NotifyEndpointObjectDeleted(GameObject deletedObject) {
            if (instance == null || deletedObject == null) {
                return;
            }

            GameObject resolved = ChainSprocketUtility.ResolvePartObject(deletedObject);
            if (resolved == null) {
                return;
            }

            var toRemove = new List<ChainConnection>();
            for (int i = 0; i < instance.connections.Count; i++) {
                ChainConnection connection = instance.connections[i];
                if (connection == null || !connection.gameObject.activeInHierarchy) {
                    continue;
                }

                if (connection.ContainsEndpointObject(resolved)) {
                    toRemove.Add(connection);
                }
            }

            for (int i = 0; i < toRemove.Count; i++) {
                Destroy(toRemove[i].gameObject);
            }
        }

        public static void NotifyEndpointObjectChanged(GameObject changedObject) {
            if (changedObject == null) {
                return;
            }

            GameObject resolved = ChainSprocketUtility.ResolvePartObject(changedObject);
            if (resolved == null) {
                return;
            }

            foreach (var guide in resolved.GetComponentsInChildren<ChainGuide>(true)) guide.RouteGeometryCache = null;
            if (instance == null) return;

            for (int i = 0; i < instance.connections.Count; i++) {
                ChainConnection connection = instance.connections[i];
                if (connection == null || !connection.gameObject.activeInHierarchy) {
                    continue;
                }

                if (connection.ContainsEndpointObject(resolved)) {
                    connection.RequestRebuild();
                }
            }
        }

        public static ChainData[] ExportBuildData(Func<GameObject, int> objectToIndex) {
            if (instance == null) {
                return Array.Empty<ChainData>();
            }

            var editor = UnityEngine.Object.FindObjectOfType<InsertChainTool>();
            var dataList = new List<ChainData>();

            for (int i = 0; i < instance.connections.Count; i++) {
                ChainConnection connection = instance.connections[i];
                if (connection == null || connection.IsPreview || (!connection.gameObject.activeInHierarchy && (editor == null || editor.EditingSourceConnection != connection))) {
                    continue;
                }

                if (connection.TryCreateBuildData(objectToIndex, out ChainData chainData)) {
                    dataList.Add(chainData);
                }
            }

            return dataList.ToArray();
        }

        public static void LoadBuildData(ChainData[] chainData, Func<int, GameObject> indexToObject) {
            if (chainData == null || chainData.Length == 0) {
                return;
            }

            EnsureInstance();

            for (int i = 0; i < chainData.Length; i++) {
                ChainData data = chainData[i];
                if (!TryResolveEndpointsFromData(data, indexToObject, out List<ChainEndpoint> endpoints)) {
                    continue;
                }

                ChainSettings settings = ChainSettings.FromData(data);
                settings.autoAlignSecondEndpoint = false;
                settings.singleChainPerEndpoint = false;

                if (!TryCreateBoundChain(endpoints, settings, out ChainConnection _, out string error)) {
                    // Preserve a legacy binding even if its geometry needs correction.
                    // It must survive the next save instead of silently disappearing.
                    var retained = new GameObject("Chain Connection (needs adjustment)").AddComponent<ChainConnection>();
                    retained.Initialize(endpoints, settings);
                    Debug.LogWarning("Loaded chain needs adjustment: " + error);
                }
            }
        }

        private static bool TryNormalizeEndpoints(
            IReadOnlyList<ChainEndpoint> inputEndpoints,
            out List<ChainEndpoint> normalizedEndpoints,
            out string errorMessage) {
            normalizedEndpoints = new List<ChainEndpoint>();
            errorMessage = string.Empty;

            if (inputEndpoints == null || inputEndpoints.Count < 2) {
                errorMessage = "At least two valid sprockets are required.";
                return false;
            }

            for (int i = 0; i < inputEndpoints.Count; i++) {
                ChainEndpoint endpoint = inputEndpoints[i];
                if (endpoint == null) {
                    errorMessage = "A valid chain endpoint could not be found.";
                    return false;
                }

                if (normalizedEndpoints.Contains(endpoint)) {
                    errorMessage = "Cannot reuse the same sprocket in one chain loop.";
                    return false;
                }

                normalizedEndpoints.Add(endpoint);
            }

            return true;
        }

        private static bool TryResolveEndpointsFromData(
            ChainData data,
            Func<int, GameObject> indexToObject,
            out List<ChainEndpoint> endpoints) {
            endpoints = new List<ChainEndpoint>();
            if (data == null || indexToObject == null) {
                return false;
            }

            for (int i = 0; i < data.OrderedEndpointCount; i++) {
                if (!data.TryGetEndpointReference(i, out int endpointIndex, out string socketId)) {
                    return false;
                }

                GameObject endpointObject = indexToObject(endpointIndex);
                if (endpointObject == null) {
                    return false;
                }

                ChainEndpoint endpoint = ChainSprocketUtility.GetOrCreateEndpoint(endpointObject, socketId);
                if (endpoint == null) {
                    return false;
                }

                endpoints.Add(endpoint);
            }

            return endpoints.Count >= 2;
        }
    }
}
