using System;
using System.Collections.Generic;
using Protobot.Builds;
using UnityEngine;

namespace Protobot.ChainSystem {
    public class ChainConnection : MonoBehaviour {
        [SerializeField] private List<ChainEndpoint> endpoints = new List<ChainEndpoint>();
        [SerializeField] private ChainEndpoint endpointA;
        [SerializeField] private ChainEndpoint endpointB;
        [SerializeField] private ChainSettings settings;
        [SerializeField] private float resolvedPitch;
        [SerializeField] private float totalLength;
        [SerializeField] private bool initialized;
        [SerializeField] private bool previewMode;

        private ChainDimensions dimensions;

        private readonly List<Transform> linkInstances = new List<Transform>();
        private readonly List<Vector3> previousPositions = new List<Vector3>();
        private readonly List<Quaternion> previousRotations = new List<Quaternion>();
        private GameObject linkPrototype;
        private Material chainMaterial;
        private bool usingResourcePrototype;
        private Vector3 resourcePrototypeBaseSize = Vector3.one;
        private Vector3 resourcePrototypeScale = Vector3.one;
        private Quaternion resourcePrototypeRotationOffset = Quaternion.identity;
        private string resourcePrototypeSourceName = string.Empty;
        private int selectableLayer = 0;
        private static bool missingResourcePrototypeLogged;
        private static readonly string[] Chain3p75ResourceCandidates = {
            "Chain/Chain3p75Link",
            "Chain/Chain3_75Link",
            "Chain/ChainLink3p75",
            "Chain/VEXChain3p75Link",
            "Chain/VEX_Chain_3p75",
            "Chain/ChainLink"
        };
        private static readonly string[] Chain6p35ResourceCandidates = {
            "Chain/Chain6p35Link",
            "Chain/Chain6_35Link",
            "Chain/ChainLink6p35",
            "Chain/VEXChain6p35Link",
            "Chain/VEX_Chain_6p35",
            "Chain/Chain25Link",
            "Chain/ChainLink"
        };
        private static readonly string[] Chain9p79ResourceCandidates = {
            "Chain/Chain9p79Link",
            "Chain/Chain9_79Link",
            "Chain/ChainLink9p79",
            "Chain/VEXChain9p79Link",
            "Chain/VEX_Chain_9p79",
            "Chain/Chain35Link",
            "Chain/ChainLink"
        };

        private bool pendingRebuild;

        public IReadOnlyList<ChainEndpoint> Endpoints => endpoints;
        public ChainEndpoint EndpointA => endpoints.Count > 0 ? endpoints[0] : endpointA;
        public ChainEndpoint EndpointB => endpoints.Count > 1 ? endpoints[1] : endpointB;
        public bool IsPreview => previewMode;
        public ChainSettings Settings => settings;

        private void Awake() {
            selectableLayer = ResolveSelectableLayer(previewMode);
            ChainManager.Register(this);
            SetLayerRecursive(gameObject, selectableLayer);
        }

        private void OnDestroy() {
            ChainManager.Unregister(this);

            if (chainMaterial != null) {
                Destroy(chainMaterial);
            }
        }

        public void SetPreviewMode(bool value) {
            if (previewMode == value) {
                return;
            }

            previewMode = value;
            selectableLayer = ResolveSelectableLayer(previewMode);
            SetLayerRecursive(gameObject, selectableLayer);

            for (int i = 0; i < linkInstances.Count; i++) {
                Transform link = linkInstances[i];
                if (link == null) {
                    continue;
                }

                SetLayerRecursive(link.gameObject, selectableLayer);
                ConfigureLinkSelectableCollider(link.gameObject);
            }
        }

        public bool Initialize(ChainEndpoint newEndpointA, ChainEndpoint newEndpointB, ChainSettings newSettings) {
            return Initialize(new[] { newEndpointA, newEndpointB }, newSettings);
        }

        public bool Initialize(IReadOnlyList<ChainEndpoint> newEndpoints, ChainSettings newSettings) {
            if (newEndpoints == null || newEndpoints.Count < 2) {
                return false;
            }

            endpoints.Clear();
            for (int i = 0; i < newEndpoints.Count; i++) {
                ChainEndpoint endpoint = newEndpoints[i];
                if (endpoint == null || endpoints.Contains(endpoint)) {
                    return false;
                }

                endpoints.Add(endpoint);
            }

            endpointA = endpoints[0];
            endpointB = endpoints[1];
            settings = newSettings ?? ChainSettings.CreateDefault();

            for (int i = 0; i < endpoints.Count; i++) {
                endpoints[i].ConfigureFromObject();
            }

            resolvedPitch = ChainSprocketUtility.ResolvePitch(endpoints, settings.standard);
            dimensions = ChainDimensions.FromPitch(resolvedPitch, settings.standard);

            CachePreviousTransforms();

            initialized = true;
            pendingRebuild = true;
            return RebuildVisual();
        }

        private void LateUpdate() {
            if (!initialized) {
                return;
            }

            if (!AreEndpointsValid()) {
                Destroy(gameObject);
                return;
            }

            List<Transform> bindingTransforms = GetBindingTransforms();
            bool anyChanged = false;
            var changedIndices = new List<int>();

            for (int i = 0; i < bindingTransforms.Count; i++) {
                if (TransformChanged(bindingTransforms[i], previousPositions[i], previousRotations[i])) {
                    changedIndices.Add(i);
                    anyChanged = true;
                }
            }

            if (anyChanged) {
                EnforceCoplanarDepth(bindingTransforms, changedIndices);
            }

            if (anyChanged || pendingRebuild) {
                RebuildVisual();
            }

            CachePreviousTransforms();
        }

        private bool AreEndpointsValid() {
            if (endpoints == null || endpoints.Count < 2) {
                return false;
            }

            for (int i = 0; i < endpoints.Count; i++) {
                ChainEndpoint endpoint = endpoints[i];
                if (endpoint == null || !endpoint.gameObject.activeInHierarchy) {
                    return false;
                }
            }

            return true;
        }

        private bool RebuildVisual() {
            pendingRebuild = false;

            Vector3 normal = GetPlaneNormal();
            if (normal.sqrMagnitude < 0.0001f) {
                return false;
            }

            var centers = new List<Vector3>(endpoints.Count);
            var radii = new List<float>(endpoints.Count);
            Vector3 centroid = Vector3.zero;

            for (int i = 0; i < endpoints.Count; i++) {
                ChainEndpoint endpoint = endpoints[i];
                centers.Add(endpoint.WorldCenter);
                radii.Add(ChainSprocketUtility.ResolvePitchRadius(endpoint, settings.standard));
                centroid += endpoint.WorldCenter;
            }

            centroid /= Mathf.Max(1, endpoints.Count);

            if (!ChainPathSolver.TrySolveLoop(
                centers,
                radii,
                normal,
                dimensions.pitch,
                settings.slack,
                out List<ChainPathSolver.ChainPose> poses,
                out totalLength,
                out float effectivePitch)) {
                SetLinksActive(false);
                return false;
            }

            resolvedPitch = effectivePitch;
            dimensions = ChainDimensions.FromPitch(resolvedPitch, settings.standard);
            UpdateResourcePrototypeScale();

            transform.position = centroid;

            EnsureLinkInstances(poses.Count);

            for (int i = 0; i < poses.Count; i++) {
                Transform linkTransform = linkInstances[i];
                int nextIndex = (i + 1) % poses.Count;
                Vector3 backPosition = poses[i].position;
                Vector3 frontPosition = poses[nextIndex].position;
                Vector3 segment = frontPosition - backPosition;
                if (segment.sqrMagnitude < 0.0000001f) {
                    segment = poses[i].tangent * Mathf.Max(dimensions.pitch, 0.0001f);
                }

                float segmentLength = segment.magnitude;
                Vector3 segmentDirection = segmentLength > 0.000001f
                    ? segment / segmentLength
                    : poses[i].tangent;

                // Place each link at the midpoint between consecutive pitch points so curved
                // wraps sit correctly on sprocket teeth instead of offsetting outward.
                linkTransform.position = (backPosition + frontPosition) * 0.5f;

                Quaternion rotation = Quaternion.LookRotation(segmentDirection, normal);
                // Rotate links 90 degrees about travel direction so chain plates lay flat
                // across the sprocket plane instead of standing vertically.
                rotation = rotation * Quaternion.AngleAxis(90f, Vector3.forward);
                if (usingResourcePrototype) {
                    rotation = rotation * resourcePrototypeRotationOffset;
                }
                else if ((i & 1) == 1) {
                    rotation = rotation * Quaternion.AngleAxis(180f, Vector3.forward);
                }

                linkTransform.rotation = rotation;
                if (usingResourcePrototype) {
                    float stretch = dimensions.pitch > 0.0001f
                        ? Mathf.Clamp(segmentLength / dimensions.pitch, 0.9f, 1.1f)
                        : 1f;
                    linkTransform.localScale = Vector3.Scale(resourcePrototypeScale, new Vector3(1f, 1f, stretch));
                }
                else {
                    float stretch = dimensions.pitch > 0.0001f
                        ? Mathf.Clamp(segmentLength / dimensions.pitch, 0.9f, 1.1f)
                        : 1f;
                    linkTransform.localScale = new Vector3(1f, 1f, stretch);
                }
                linkTransform.gameObject.SetActive(true);
            }

            for (int i = poses.Count; i < linkInstances.Count; i++) {
                linkInstances[i].gameObject.SetActive(false);
            }

            return true;
        }

        private void EnsureLinkInstances(int targetCount) {
            if (linkPrototype == null) {
                BuildLinkPrototype();
            }

            while (linkInstances.Count < targetCount) {
                GameObject clone = Instantiate(linkPrototype, transform);
                clone.name = "Chain Link";
                clone.SetActive(true);
                linkInstances.Add(clone.transform);
            }

            while (linkInstances.Count > targetCount) {
                int lastIndex = linkInstances.Count - 1;
                Transform last = linkInstances[lastIndex];
                linkInstances.RemoveAt(lastIndex);
                if (last != null) {
                    Destroy(last.gameObject);
                }
            }

            for (int i = 0; i < linkInstances.Count; i++) {
                Transform link = linkInstances[i];
                if (link == null) {
                    continue;
                }

                SetLayerRecursive(link.gameObject, selectableLayer);
                ConfigureLinkSelectableCollider(link.gameObject);
            }
        }

        private void SetLinksActive(bool value) {
            for (int i = 0; i < linkInstances.Count; i++) {
                if (linkInstances[i] != null) {
                    linkInstances[i].gameObject.SetActive(value);
                }
            }
        }

        private void BuildLinkPrototype() {
            chainMaterial = BuildChainMaterial();

            if (TryBuildResourcePrototype()) {
                return;
            }

            usingResourcePrototype = false;
            resourcePrototypeBaseSize = Vector3.one;
            resourcePrototypeScale = Vector3.one;
            resourcePrototypeRotationOffset = Quaternion.identity;
            resourcePrototypeSourceName = string.Empty;
            linkPrototype = new GameObject("Chain Link Prototype");
            linkPrototype.transform.SetParent(transform, false);
            linkPrototype.SetActive(false);
            SetLayerRecursive(linkPrototype, selectableLayer);

            float halfWidth = dimensions.width * 0.5f;
            float outerY = halfWidth - dimensions.plateThickness * 0.5f;
            float innerY = halfWidth * 0.45f;

            CreatePrimitivePart(linkPrototype.transform, PrimitiveType.Cube, "Outer Plate L",
                new Vector3(0f, -outerY, 0f),
                new Vector3(dimensions.plateHeight, dimensions.plateThickness, dimensions.outerPlateLength),
                Vector3.zero);

            CreatePrimitivePart(linkPrototype.transform, PrimitiveType.Cube, "Outer Plate R",
                new Vector3(0f, outerY, 0f),
                new Vector3(dimensions.plateHeight, dimensions.plateThickness, dimensions.outerPlateLength),
                Vector3.zero);

            CreatePrimitivePart(linkPrototype.transform, PrimitiveType.Cube, "Inner Plate L",
                new Vector3(0f, -innerY, 0f),
                new Vector3(dimensions.plateHeight * 0.92f, dimensions.plateThickness, dimensions.innerPlateLength),
                Vector3.zero);

            CreatePrimitivePart(linkPrototype.transform, PrimitiveType.Cube, "Inner Plate R",
                new Vector3(0f, innerY, 0f),
                new Vector3(dimensions.plateHeight * 0.92f, dimensions.plateThickness, dimensions.innerPlateLength),
                Vector3.zero);

            CreateCylinderPair(linkPrototype.transform, "Roller", dimensions.rollerDiameter, dimensions.width * 0.92f, dimensions.jointOffset);
            CreateCylinderPair(linkPrototype.transform, "Pin", dimensions.pinDiameter, dimensions.width, dimensions.jointOffset);
        }

        private static string[] GetCandidatesForStandard(ChainStandard standard) {
            switch (standard) {
                case ChainStandard.Pitch3p75:
                    return Chain3p75ResourceCandidates;
                case ChainStandard.Pitch9p79:
                    return Chain9p79ResourceCandidates;
                default:
                    return Chain6p35ResourceCandidates;
            }
        }

        private bool TryBuildResourcePrototype() {
            string[] candidates = GetCandidatesForStandard(settings != null ? settings.standard : ChainStandard.Pitch6p35);

            for (int i = 0; i < candidates.Length; i++) {
                GameObject sourcePrefab = Resources.Load<GameObject>(candidates[i]);
                if (sourcePrefab == null) {
                    continue;
                }

                if (TryAssignResourcePrototype(sourcePrefab)) {
                    return true;
                }
            }

            GameObject[] chainResources = Resources.LoadAll<GameObject>("Chain");
            for (int i = 0; i < chainResources.Length; i++) {
                GameObject sourcePrefab = chainResources[i];
                if (sourcePrefab == null || !IsChainPrefabName(sourcePrefab.name)) {
                    continue;
                }

                if (!MatchesRequestedStandard(sourcePrefab.name, settings != null ? settings.standard : ChainStandard.Pitch6p35)) {
                    continue;
                }

                if (TryAssignResourcePrototype(sourcePrefab)) {
                    return true;
                }
            }

            if (!missingResourcePrototypeLogged) {
                missingResourcePrototypeLogged = true;
            }

            return false;
        }

        private bool TryAssignResourcePrototype(GameObject sourcePrefab) {
            if (sourcePrefab == null) {
                return false;
            }

            linkPrototype = Instantiate(sourcePrefab, transform);
            linkPrototype.name = "Chain Link Prototype";
            linkPrototype.transform.localPosition = Vector3.zero;
            linkPrototype.transform.localRotation = Quaternion.identity;
            linkPrototype.transform.localScale = Vector3.one;
            resourcePrototypeSourceName = sourcePrefab.name == null ? string.Empty : sourcePrefab.name;

            Collider[] colliders = linkPrototype.GetComponentsInChildren<Collider>(true);
            for (int j = 0; j < colliders.Length; j++) {
                Destroy(colliders[j]);
            }

            Renderer[] renderers = linkPrototype.GetComponentsInChildren<Renderer>(true);
            for (int j = 0; j < renderers.Length; j++) {
                if (renderers[j] != null && chainMaterial != null) {
                    renderers[j].sharedMaterial = chainMaterial;
                }
            }

            if (IsOfficialPitchLinkModel(sourcePrefab.name)) {
                // Runtime-generated official pitch models are already authored with +Z chain travel.
                // Keep native axes and proportions. 0.385" link needs an extra 90 deg twist
                // relative to the other official pitch models.
                resourcePrototypeBaseSize = Vector3.one;
                resourcePrototypeRotationOffset = settings != null && settings.standard == ChainStandard.Pitch9p79
                    ? Quaternion.AngleAxis(90f, Vector3.forward)
                    : Quaternion.identity;
            }
            else if (TryMeasureMeshBoundsLocal(linkPrototype.transform, out Vector3 measuredSize)) {
                if (IsKnownConvertedChainModel(sourcePrefab.name)) {
                    ConfigureKnownConvertedPrototypeAxes(measuredSize);
                }
                else {
                    int widthAxis = GetMinAxisIndex(new[] { measuredSize.x, measuredSize.y, measuredSize.z });
                    if (TryEstimateWidthAxisByFaceArea(linkPrototype.transform, out int detectedWidthAxis)) {
                        widthAxis = detectedWidthAxis;
                    }

                    ConfigureResourcePrototypeAxes(measuredSize, widthAxis);
                }
            }
            else {
                resourcePrototypeBaseSize = Vector3.one;
                resourcePrototypeRotationOffset = Quaternion.identity;
            }

            usingResourcePrototype = true;
            UpdateResourcePrototypeScale();
            linkPrototype.SetActive(false);
            SetLayerRecursive(linkPrototype, selectableLayer);
            return true;
        }

        private static bool IsKnownConvertedChainModel(string prefabName) {
            if (string.IsNullOrWhiteSpace(prefabName)) {
                return false;
            }

            string normalized = NormalizeName(prefabName);
            return normalized.Contains("chain25link")
                || normalized.Contains("chain35link");
        }

        private static bool IsOfficialPitchLinkModel(string prefabName) {
            if (string.IsNullOrWhiteSpace(prefabName)) {
                return false;
            }

            string normalized = NormalizeName(prefabName);
            return normalized.Contains("chain3p75link")
                || normalized.Contains("chain6p35link")
                || normalized.Contains("chain9p79link");
        }

        private void ConfigureKnownConvertedPrototypeAxes(Vector3 measuredSize) {
            // Converted CAD meshes are authored with:
            // local X = chain width, local Y = plate height, local Z = chain pitch.
            // Runtime expects local Y = width and local Z = pitch.
            resourcePrototypeRotationOffset = Quaternion.AngleAxis(90f, Vector3.forward);
            resourcePrototypeBaseSize = new Vector3(
                Mathf.Abs(measuredSize.y),
                Mathf.Abs(measuredSize.x),
                Mathf.Abs(measuredSize.z));
        }

        private void ConfigureResourcePrototypeAxes(Vector3 measuredSize, int widthAxis) {
            if (measuredSize.x < 0.0001f || measuredSize.y < 0.0001f || measuredSize.z < 0.0001f) {
                resourcePrototypeBaseSize = Vector3.one;
                resourcePrototypeRotationOffset = Quaternion.identity;
                return;
            }

            float[] dims = { measuredSize.x, measuredSize.y, measuredSize.z };
            int pitchAxis = GetMaxAxisIndexExcluding(dims, widthAxis);
            int heightAxis = 3 - widthAxis - pitchAxis;

            Vector3 modelPitch = AxisVector(pitchAxis);
            Vector3 modelHeight = AxisVector(heightAxis);
            Vector3 modelWidthPositive = AxisVector(widthAxis);
            Vector3 modelWidthNegative = -modelWidthPositive;

            Quaternion offsetPositive = Quaternion.Inverse(Quaternion.LookRotation(modelPitch, modelWidthPositive));
            Quaternion offsetNegative = Quaternion.Inverse(Quaternion.LookRotation(modelPitch, modelWidthNegative));

            float positiveScore = Vector3.Dot(offsetPositive * modelHeight, Vector3.right);
            float negativeScore = Vector3.Dot(offsetNegative * modelHeight, Vector3.right);

            resourcePrototypeRotationOffset = positiveScore >= negativeScore
                ? offsetPositive
                : offsetNegative;

            resourcePrototypeBaseSize = new Vector3(
                dims[heightAxis],
                dims[widthAxis],
                dims[pitchAxis]);
        }

        private static bool TryEstimateWidthAxisByFaceArea(Transform root, out int widthAxis) {
            widthAxis = 0;
            if (root == null) {
                return false;
            }

            float scoreX = 0f;
            float scoreY = 0f;
            float scoreZ = 0f;

            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            if (meshFilters == null || meshFilters.Length == 0) {
                return false;
            }

            Matrix4x4 worldToRoot = root.worldToLocalMatrix;
            bool foundFace = false;

            for (int i = 0; i < meshFilters.Length; i++) {
                MeshFilter meshFilter = meshFilters[i];
                if (meshFilter == null || meshFilter.sharedMesh == null) {
                    continue;
                }

                Mesh mesh = meshFilter.sharedMesh;
                if (!mesh.isReadable) {
                    continue;
                }

                int[] triangles;
                Vector3[] vertices;
                try {
                    triangles = mesh.triangles;
                    vertices = mesh.vertices;
                }
                catch (Exception) {
                    continue;
                }

                if (triangles == null || vertices == null || triangles.Length < 3) {
                    continue;
                }

                Matrix4x4 meshToRoot = worldToRoot * meshFilter.transform.localToWorldMatrix;
                for (int t = 0; t <= triangles.Length - 3; t += 3) {
                    int i0 = triangles[t];
                    int i1 = triangles[t + 1];
                    int i2 = triangles[t + 2];

                    Vector3 v0 = meshToRoot.MultiplyPoint3x4(vertices[i0]);
                    Vector3 v1 = meshToRoot.MultiplyPoint3x4(vertices[i1]);
                    Vector3 v2 = meshToRoot.MultiplyPoint3x4(vertices[i2]);

                    Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
                    float doubledArea = cross.magnitude;
                    if (doubledArea < 0.000001f) {
                        continue;
                    }

                    Vector3 normal = cross / doubledArea;
                    float area = doubledArea * 0.5f;

                    scoreX += area * Mathf.Abs(normal.x);
                    scoreY += area * Mathf.Abs(normal.y);
                    scoreZ += area * Mathf.Abs(normal.z);
                    foundFace = true;
                }
            }

            if (!foundFace) {
                return false;
            }

            widthAxis = 0;
            float maxScore = scoreX;
            if (scoreY > maxScore) {
                widthAxis = 1;
                maxScore = scoreY;
            }
            if (scoreZ > maxScore) {
                widthAxis = 2;
            }

            return true;
        }

        private static int GetMinAxisIndex(float[] dims) {
            int index = 0;
            if (dims[1] < dims[index]) {
                index = 1;
            }
            if (dims[2] < dims[index]) {
                index = 2;
            }
            return index;
        }

        private static int GetMaxAxisIndexExcluding(float[] dims, int excludedIndex) {
            int index = excludedIndex == 0 ? 1 : 0;
            for (int i = 0; i < 3; i++) {
                if (i == excludedIndex || i == index) {
                    continue;
                }
                if (dims[i] > dims[index]) {
                    index = i;
                }
            }
            return index;
        }

        private static Vector3 AxisVector(int axisIndex) {
            switch (axisIndex) {
                case 0:
                    return Vector3.right;
                case 1:
                    return Vector3.up;
                default:
                    return Vector3.forward;
            }
        }

        private void UpdateResourcePrototypeScale() {
            if (!usingResourcePrototype) {
                resourcePrototypeScale = Vector3.one;
                return;
            }

            if (IsOfficialPitchLinkModel(resourcePrototypeSourceName)) {
                resourcePrototypeScale = Vector3.one;
                return;
            }

            Vector3 baseSize = resourcePrototypeBaseSize;
            if (baseSize.x < 0.0001f || baseSize.y < 0.0001f || baseSize.z < 0.0001f) {
                resourcePrototypeScale = Vector3.one;
                return;
            }

            float targetHeight = Mathf.Max(dimensions.plateHeight, 0.01f);
            float targetWidth = Mathf.Max(dimensions.width, 0.01f);
            float targetPitch = Mathf.Max(dimensions.pitch, 0.01f);

            resourcePrototypeScale = new Vector3(
                ComputeAxisScale(baseSize.x, targetHeight),
                ComputeAxisScale(baseSize.y, targetWidth),
                ComputeAxisScale(baseSize.z, targetPitch));
        }

        private static float ComputeAxisScale(float actualSize, float targetSize) {
            if (actualSize < 0.0001f || targetSize < 0.0001f) {
                return 1f;
            }

            float ratio = targetSize / actualSize;
            if (ratio > 0.85f && ratio < 1.15f) {
                return 1f;
            }

            return Mathf.Clamp(ratio, 0.35f, 2.5f);
        }

        private static bool TryMeasureMeshBoundsLocal(Transform root, out Vector3 size) {
            size = Vector3.zero;
            if (root == null) {
                return false;
            }

            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            bool foundMesh = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            Matrix4x4 worldToRoot = root.worldToLocalMatrix;

            for (int i = 0; i < meshFilters.Length; i++) {
                MeshFilter meshFilter = meshFilters[i];
                if (meshFilter == null || meshFilter.sharedMesh == null) {
                    continue;
                }

                Bounds bounds = meshFilter.sharedMesh.bounds;
                Vector3 boundsMin = bounds.min;
                Vector3 boundsMax = bounds.max;

                Matrix4x4 meshToRoot = worldToRoot * meshFilter.transform.localToWorldMatrix;
                Vector3[] corners = {
                    new Vector3(boundsMin.x, boundsMin.y, boundsMin.z),
                    new Vector3(boundsMin.x, boundsMin.y, boundsMax.z),
                    new Vector3(boundsMin.x, boundsMax.y, boundsMin.z),
                    new Vector3(boundsMin.x, boundsMax.y, boundsMax.z),
                    new Vector3(boundsMax.x, boundsMin.y, boundsMin.z),
                    new Vector3(boundsMax.x, boundsMin.y, boundsMax.z),
                    new Vector3(boundsMax.x, boundsMax.y, boundsMin.z),
                    new Vector3(boundsMax.x, boundsMax.y, boundsMax.z),
                };

                for (int j = 0; j < corners.Length; j++) {
                    Vector3 point = meshToRoot.MultiplyPoint3x4(corners[j]);
                    if (!foundMesh) {
                        min = point;
                        max = point;
                        foundMesh = true;
                    }
                    else {
                        min = Vector3.Min(min, point);
                        max = Vector3.Max(max, point);
                    }
                }
            }

            if (!foundMesh) {
                return false;
            }

            size = max - min;
            return size.x > 0.0001f && size.y > 0.0001f && size.z > 0.0001f;
        }

        private static bool IsChainPrefabName(string prefabName) {
            if (string.IsNullOrWhiteSpace(prefabName)) {
                return false;
            }

            string normalized = NormalizeName(prefabName);
            return normalized.Contains("chain") || normalized.Contains("link");
        }

        private static bool MatchesRequestedStandard(string prefabName, ChainStandard requestedStandard) {
            string normalized = NormalizeName(prefabName);
            bool mentions3p75 = normalized.Contains("3p75") || normalized.Contains("3.75") || normalized.Contains("375");
            bool mentions6p35 = normalized.Contains("6p35")
                || normalized.Contains("6.35")
                || normalized.Contains("635")
                || normalized.Contains("chain25")
                || normalized.Contains("link25");
            bool mentions9p79 = normalized.Contains("9p79")
                || normalized.Contains("9.79")
                || normalized.Contains("979")
                || normalized.Contains("chain35")
                || normalized.Contains("link35");

            if (!mentions3p75 && !mentions6p35 && !mentions9p79) {
                return true;
            }

            switch (requestedStandard) {
                case ChainStandard.Pitch3p75:
                    return mentions3p75 && !mentions6p35 && !mentions9p79;
                case ChainStandard.Pitch9p79:
                    return mentions9p79 && !mentions3p75 && !mentions6p35;
                default:
                    return mentions6p35 && !mentions3p75 && !mentions9p79;
            }
        }

        private static string NormalizeName(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return string.Empty;
            }

            return value.ToLowerInvariant().Replace("_", string.Empty).Replace(" ", string.Empty);
        }

        private void CreateCylinderPair(Transform parent, string prefix, float diameter, float span, float zOffset) {
            // Unity cylinder primitive has radius 0.5 and height 2 in local space.
            Vector3 scale = new Vector3(diameter, span * 0.5f, diameter);

            CreatePrimitivePart(parent, PrimitiveType.Cylinder, prefix + " Front",
                new Vector3(0f, 0f, zOffset),
                scale,
                Vector3.zero);

            CreatePrimitivePart(parent, PrimitiveType.Cylinder, prefix + " Back",
                new Vector3(0f, 0f, -zOffset),
                scale,
                Vector3.zero);
        }

        private GameObject CreatePrimitivePart(Transform parent, PrimitiveType type, string partName, Vector3 localPosition, Vector3 localScale, Vector3 localEuler) {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = partName;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localScale = localScale;
            primitive.transform.localEulerAngles = localEuler;

            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null) {
                Destroy(collider);
            }

            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null && chainMaterial != null) {
                renderer.sharedMaterial = chainMaterial;
            }

            SetLayerRecursive(primitive, selectableLayer);
            return primitive;
        }

        private void ConfigureLinkSelectableCollider(GameObject linkObject) {
            if (linkObject == null) {
                return;
            }

            BoxCollider collider = linkObject.GetComponent<BoxCollider>();
            if (previewMode) {
                if (collider != null) {
                    Destroy(collider);
                }
                return;
            }

            if (collider == null) {
                collider = linkObject.AddComponent<BoxCollider>();
            }

            float height = Mathf.Max(dimensions.plateHeight, 0.05f);
            float width = Mathf.Max(dimensions.width, 0.05f);
            float pitch = Mathf.Max(dimensions.pitch * 0.95f, 0.05f);

            collider.center = Vector3.zero;
            collider.size = new Vector3(height, width, pitch);
            collider.isTrigger = false;
        }

        private Material BuildChainMaterial() {
            Shader shader = Shader.Find("Standard");
            if (shader == null) {
                shader = Shader.Find("Legacy Shaders/Diffuse");
            }
            if (shader == null) {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null) {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null) {
                shader = Shader.Find("Hidden/InternalErrorShader");
            }
            if (shader == null) {
                return null;
            }

            Material material = new Material(shader);

            material.name = "Runtime Chain Material";
            material.color = new Color(0.63f, 0.65f, 0.67f, 1f);

            if (material.HasProperty("_Metallic")) {
                material.SetFloat("_Metallic", 0.8f);
            }

            if (material.HasProperty("_Glossiness")) {
                material.SetFloat("_Glossiness", 0.35f);
            }

            return material;
        }

        private bool EnforceCoplanarDepth(IReadOnlyList<Transform> bindingTransforms, IReadOnlyList<int> changedIndices) {
            if (bindingTransforms == null || changedIndices == null || bindingTransforms.Count != endpoints.Count || changedIndices.Count == 0) {
                return false;
            }

            Vector3 planeNormal = GetPlaneNormal();
            if (planeNormal.sqrMagnitude < 0.0001f) {
                return false;
            }

            float referenceDepth = 0f;
            int referenceCount = 0;

            for (int i = 0; i < endpoints.Count; i++) {
                if (ContainsIndex(changedIndices, i)) {
                    continue;
                }

                referenceDepth += Vector3.Dot(endpoints[i].WorldCenter, planeNormal);
                referenceCount++;
            }

            if (referenceCount == 0) {
                for (int i = 0; i < endpoints.Count; i++) {
                    referenceDepth += Vector3.Dot(endpoints[i].WorldCenter, planeNormal);
                }
                referenceDepth /= Mathf.Max(1, endpoints.Count);
            }
            else {
                referenceDepth /= referenceCount;
            }

            bool adjusted = false;
            for (int i = 0; i < changedIndices.Count; i++) {
                int index = changedIndices[i];
                Transform target = bindingTransforms[index];
                if (target == null) {
                    continue;
                }

                float depth = Vector3.Dot(endpoints[index].WorldCenter, planeNormal);
                float depthDelta = referenceDepth - depth;
                if (Mathf.Abs(depthDelta) <= 0.0001f) {
                    continue;
                }

                target.position += planeNormal * depthDelta;
                adjusted = true;
            }

            if (adjusted) {
                pendingRebuild = true;
            }

            return adjusted;
        }

        private static bool TransformChanged(Transform target, Vector3 prevPos, Quaternion prevRot) {
            if (target == null) {
                return false;
            }
            return Vector3.Distance(target.position, prevPos) > 0.0001f || Quaternion.Angle(target.rotation, prevRot) > 0.05f;
        }

        private static float MotionScore(Transform target, Vector3 prevPos, Quaternion prevRot) {
            if (target == null) {
                return 0f;
            }
            float translation = Vector3.Distance(target.position, prevPos);
            float rotation = Quaternion.Angle(target.rotation, prevRot) / 180f;
            return translation + rotation;
        }

        private void CachePreviousTransforms() {
            previousPositions.Clear();
            previousRotations.Clear();

            List<Transform> bindingTransforms = GetBindingTransforms();
            for (int i = 0; i < bindingTransforms.Count; i++) {
                Transform bindingTransform = bindingTransforms[i];
                if (bindingTransform == null) {
                    previousPositions.Add(Vector3.zero);
                    previousRotations.Add(Quaternion.identity);
                    continue;
                }

                previousPositions.Add(bindingTransform.position);
                previousRotations.Add(bindingTransform.rotation);
            }
        }

        private static void SetLayerRecursive(GameObject obj, int layer) {
            obj.layer = layer;
            foreach (Transform child in obj.transform) {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        private static Transform GetBindingTransform(ChainEndpoint endpoint) {
            if (endpoint == null) {
                return null;
            }

            Transform endpointTransform = endpoint.transform;
            if (endpointTransform.gameObject.TryGetGroup(out Transform groupTransform)) {
                return groupTransform;
            }

            return endpointTransform;
        }

        private static int ResolveSelectableLayer(bool useIgnoreRaycast) {
            string layerName = useIgnoreRaycast ? "Ignore Raycast" : "Default";
            int layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 ? layer : 0;
        }

        private List<Transform> GetBindingTransforms() {
            var bindingTransforms = new List<Transform>(endpoints.Count);
            for (int i = 0; i < endpoints.Count; i++) {
                bindingTransforms.Add(GetBindingTransform(endpoints[i]));
            }

            return bindingTransforms;
        }

        private Vector3 GetPlaneNormal() {
            for (int i = 0; i < endpoints.Count; i++) {
                ChainEndpoint endpoint = endpoints[i];
                if (endpoint == null) {
                    continue;
                }

                Vector3 axis = endpoint.WorldAxis;
                if (axis.sqrMagnitude > 0.0001f) {
                    return axis.normalized;
                }
            }

            return Vector3.zero;
        }

        private static bool ContainsIndex(IReadOnlyList<int> indices, int value) {
            for (int i = 0; i < indices.Count; i++) {
                if (indices[i] == value) {
                    return true;
                }
            }

            return false;
        }

        public bool ContainsEndpoint(ChainEndpoint endpoint) {
            if (endpoint == null) {
                return false;
            }

            for (int i = 0; i < endpoints.Count; i++) {
                if (endpoints[i] == endpoint) {
                    return true;
                }
            }

            return false;
        }

        public bool ContainsEndpointObject(GameObject obj) {
            if (obj == null) {
                return false;
            }

            GameObject resolvedObj = ChainSprocketUtility.ResolvePartObject(obj);
            for (int i = 0; i < endpoints.Count; i++) {
                ChainEndpoint endpoint = endpoints[i];
                if (endpoint != null && endpoint.gameObject == resolvedObj) {
                    return true;
                }
            }

            return false;
        }

        public bool TryCreateBuildData(Func<GameObject, int> objectToIndex, out ChainData chainData) {
            chainData = null;
            if (!initialized || endpoints.Count < 2) {
                return false;
            }

            var endpointIndices = new int[endpoints.Count];
            var endpointSockets = new string[endpoints.Count];

            for (int i = 0; i < endpoints.Count; i++) {
                ChainEndpoint endpoint = endpoints[i];
                if (endpoint == null) {
                    return false;
                }

                int endpointIndex = objectToIndex(endpoint.gameObject);
                if (endpointIndex < 0) {
                    return false;
                }

                endpointIndices[i] = endpointIndex;
                endpointSockets[i] = endpoint.SocketId;
            }

            chainData = new ChainData {
                endpointIndices = endpointIndices,
                endpointSockets = endpointSockets,
                endpointAIndex = endpointIndices[0],
                endpointBIndex = endpointIndices[1],
                endpointASocket = endpointSockets[0],
                endpointBSocket = endpointSockets[1],
                standard = settings.standard.ToString(),
                slack = settings.slack,
            };

            return true;
        }
    }
}
