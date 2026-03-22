using System.Text.RegularExpressions;
using System.Collections.Generic;
using Parts_List;
using UnityEngine;

namespace Protobot.ChainSystem {
    public enum SprocketFamily {
        Unknown = 0,
        Normal = 1,
        HighStrength = 2
    }

    public static class ChainSprocketUtility {
        private static readonly Regex NumberPattern = new Regex(@"\d+");
        // Official VEX pitch values from the VEX Library table:
        // 3.75mm / 0.148", 6.35mm / 0.250", 9.79mm / 0.385".
        private const float Pitch3p75Inches = 0.148f;
        private const float Pitch6p35Inches = 0.250f;
        private const float Pitch9p79Inches = 0.385f;

        public static float GetNominalPitch(ChainStandard standard) {
            switch (standard) {
                case ChainStandard.Pitch3p75:
                    return Pitch3p75Inches;
                case ChainStandard.Pitch9p79:
                    return Pitch9p79Inches;
                default:
                    return Pitch6p35Inches;
            }
        }

        public static GameObject ResolvePartObject(GameObject obj) {
            if (obj == null) {
                return null;
            }

            if (obj.TryGetComponent(out HoleFace holeFace)) {
                return holeFace.hole.part;
            }

            if (obj.TryGetComponent(out SavedObject savedObject)) {
                return savedObject.gameObject;
            }

            var parentSavedObject = obj.GetComponentInParent<SavedObject>();
            if (parentSavedObject != null) {
                return parentSavedObject.gameObject;
            }

            return obj.transform.root.gameObject;
        }

        public static bool IsSprocket(GameObject obj) {
            GameObject partObject = ResolvePartObject(obj);
            if (partObject == null || !partObject.TryGetComponent(out SavedObject savedObject)) {
                if (partObject != null && partObject.TryGetComponent(out PartName partNameComp)) {
                    string partNameUpper = partNameComp.name == null ? string.Empty : partNameComp.name.ToUpperInvariant();
                    return partNameUpper.Contains("SPROCKET");
                }
                return false;
            }

            string id = savedObject.id == null ? string.Empty : savedObject.id.ToUpperInvariant();
            string nameId = savedObject.nameId == null ? string.Empty : savedObject.nameId.ToUpperInvariant();

            if (nameId == "SPKT" || nameId == "HSPK" || id.Contains("SPKT") || id.Contains("HSPK")) {
                return true;
            }

            if (partObject.TryGetComponent(out PartName partNameComponent)) {
                string displayName = partNameComponent.name == null ? string.Empty : partNameComponent.name.ToUpperInvariant();
                if (displayName.Contains("SPROCKET")) {
                    return true;
                }
            }

            return false;
        }

        public static ChainEndpoint GetOrCreateEndpoint(GameObject obj) {
            GameObject partObject = ResolvePartObject(obj);
            if (partObject == null || !IsSprocket(partObject)) {
                return null;
            }

            if (!partObject.TryGetComponent(out ChainEndpoint endpoint)) {
                endpoint = partObject.AddComponent<ChainEndpoint>();
            }

            endpoint.ConfigureFromObject();
            return endpoint;
        }

        public static SprocketFamily ResolveSprocketFamily(ChainEndpoint endpoint) {
            return endpoint == null ? SprocketFamily.Unknown : ResolveSprocketFamily(endpoint.gameObject);
        }

        public static SprocketFamily ResolveSprocketFamily(GameObject obj) {
            GameObject partObject = ResolvePartObject(obj);
            if (partObject == null) {
                return SprocketFamily.Unknown;
            }

            if (partObject.TryGetComponent(out SavedObject savedObject)) {
                SprocketFamily fromSavedObject = ParseFamilyFromText(savedObject.id);
                if (fromSavedObject != SprocketFamily.Unknown) {
                    return fromSavedObject;
                }
            }

            if (partObject.TryGetComponent(out PartData partData)) {
                SprocketFamily fromPartData = ParseFamilyFromText(partData.param1Value);
                if (fromPartData != SprocketFamily.Unknown) {
                    return fromPartData;
                }
            }

            if (partObject.TryGetComponent(out PartName partName)) {
                SprocketFamily fromName = ParseFamilyFromText(partName.name);
                if (fromName != SprocketFamily.Unknown) {
                    return fromName;
                }
            }

            return SprocketFamily.Unknown;
        }

        public static ChainStandard PreferredStandardForFamily(SprocketFamily family) {
            return family == SprocketFamily.HighStrength
                ? ChainStandard.Pitch9p79
                : ChainStandard.Pitch6p35;
        }

        public static bool TryResolveCompatibleStandard(
            ChainEndpoint endpointA,
            ChainEndpoint endpointB,
            ChainStandard requestedStandard,
            out ChainStandard resolvedStandard,
            out string message) {
            return TryResolveCompatibleStandard(new[] { endpointA, endpointB }, requestedStandard, true, out resolvedStandard, out message);
        }

        public static bool TryResolveCompatibleStandard(
            IReadOnlyList<ChainEndpoint> endpoints,
            ChainStandard requestedStandard,
            out ChainStandard resolvedStandard,
            out string message) {
            return TryResolveCompatibleStandard(endpoints, requestedStandard, true, out resolvedStandard, out message);
        }

        public static bool TryResolveCompatibleStandard(
            IReadOnlyList<ChainEndpoint> endpoints,
            ChainStandard requestedStandard,
            bool autoResolveRequestedStandard,
            out ChainStandard resolvedStandard,
            out string message) {
            resolvedStandard = requestedStandard;
            message = string.Empty;

            if (endpoints == null || endpoints.Count == 0) {
                return true;
            }

            SprocketFamily enforcedFamily = SprocketFamily.Unknown;
            for (int i = 0; i < endpoints.Count; i++) {
                ChainEndpoint endpoint = endpoints[i];
                if (endpoint == null) {
                    continue;
                }

                SprocketFamily family = ResolveSprocketFamily(endpoint);
                if (family == SprocketFamily.Unknown) {
                    continue;
                }

                if (enforcedFamily != SprocketFamily.Unknown && family != enforcedFamily) {
                    message = "Cannot chain Normal and High Strength sprockets together.";
                    return false;
                }

                enforcedFamily = family;
            }

            if (enforcedFamily == SprocketFamily.Unknown) {
                return true;
            }

            ChainStandard preferredStandard = PreferredStandardForFamily(enforcedFamily);
            if (autoResolveRequestedStandard) {
                resolvedStandard = preferredStandard;
                if (resolvedStandard != requestedStandard) {
                    message = enforcedFamily == SprocketFamily.HighStrength
                        ? "High Strength sprockets prefer #35 / 9.79mm chain."
                        : "Normal sprockets prefer #25 / 6.35mm chain.";
                }

                return true;
            }

            if (!IsStandardCompatibleWithFamily(requestedStandard, enforcedFamily)) {
                message = enforcedFamily == SprocketFamily.HighStrength
                    ? "High Strength sprockets require #35 / 9.79mm chain."
                    : "Normal sprockets require #25 / 6.35mm chain.";
                return false;
            }

            return true;
        }

        public static ChainStandard ResolveAutoStandard(IReadOnlyList<ChainEndpoint> endpoints, ChainStandard fallbackStandard = ChainStandard.Pitch6p35) {
            if (endpoints == null) {
                return fallbackStandard;
            }

            for (int i = 0; i < endpoints.Count; i++) {
                SprocketFamily family = ResolveSprocketFamily(endpoints[i]);
                if (family != SprocketFamily.Unknown) {
                    return PreferredStandardForFamily(family);
                }
            }

            return fallbackStandard;
        }

        public static int ParseToothCount(GameObject obj) {
            GameObject partObject = ResolvePartObject(obj);
            if (partObject == null) {
                return 0;
            }

            if (partObject.TryGetComponent(out PartData partData)) {
                int param2ToothCount = ParseFirstNumber(partData.param2Value);
                if (param2ToothCount > 0) {
                    return param2ToothCount;
                }

                int paramToothCount = ParseFirstNumber(partData.param1Value);
                if (paramToothCount > 0) {
                    return paramToothCount;
                }
            }

            if (partObject.TryGetComponent(out SavedObject savedObject)) {
                int idToothCount = ParseFirstNumber(savedObject.id);
                if (idToothCount > 0) {
                    return idToothCount;
                }
            }

            if (TryParseToothCountFromChildObjects(partObject, out int childToothCount)) {
                return childToothCount;
            }

            if (partObject.TryGetComponent(out PartName partName)) {
                int nameToothCount = ParseFirstNumber(partName.name);
                if (nameToothCount > 0) {
                    return nameToothCount;
                }
            }

            return 0;
        }

        private static int ParseFirstNumber(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return 0;
            }

            Match match = NumberPattern.Match(value);
            if (!match.Success) {
                return 0;
            }

            return int.TryParse(match.Value, out int parsedValue) ? parsedValue : 0;
        }

        private static SprocketFamily ParseFamilyFromText(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return SprocketFamily.Unknown;
            }

            string normalized = value.Trim().ToUpperInvariant();
            if (normalized.Contains("HSPK") || normalized.Contains("HIGH STRENGTH")) {
                return SprocketFamily.HighStrength;
            }

            if (normalized.Contains("SPKT") || normalized.Contains("NORMAL")) {
                return SprocketFamily.Normal;
            }

            return SprocketFamily.Unknown;
        }

        private static bool IsStandardCompatibleWithFamily(ChainStandard standard, SprocketFamily family) {
            switch (family) {
                case SprocketFamily.HighStrength:
                    return standard == ChainStandard.Pitch9p79;
                case SprocketFamily.Normal:
                    return standard == ChainStandard.Pitch3p75 || standard == ChainStandard.Pitch6p35;
                default:
                    return true;
            }
        }

        public static float ResolvePitch(ChainEndpoint endpointA, ChainEndpoint endpointB, ChainStandard standard) {
            return ResolvePitch(new[] { endpointA, endpointB }, standard);
        }

        public static float ResolvePitch(IReadOnlyList<ChainEndpoint> endpoints, ChainStandard standard) {
            // Tier 1: chain standard explicitly defines pitch so visuals match real VEX chain.
            return GetNominalPitch(standard);
        }

        public static float ResolvePitchRadius(ChainEndpoint endpoint, ChainStandard standard) {
            if (endpoint == null) {
                return 0.5f;
            }

            int toothCount = endpoint.ToothCount;
            float pitch = GetNominalPitch(standard);
            float measuredOuterRadius = endpoint.PitchRadius;

            if (toothCount > 2) {
                float nominalPitchRadius = pitch / (2f * Mathf.Sin(Mathf.PI / toothCount));
                float planarScale = ResolvePlanarScale(endpoint);
                nominalPitchRadius *= planarScale;

                float expectedOuterRadius = ComputeExpectedOutsideRadius(pitch, toothCount) * planarScale;
                if (measuredOuterRadius > 0.05f && expectedOuterRadius > 0.05f) {
                    float modelScale = Mathf.Clamp(measuredOuterRadius / expectedOuterRadius, 0.75f, 1.25f);
                    float calibratedPitchRadius = nominalPitchRadius * modelScale;

                    float measuredUpper = measuredOuterRadius - (pitch * 0.16f * planarScale);
                    float measuredLower = measuredOuterRadius - (pitch * 0.40f * planarScale);

                    if (measuredUpper > 0.05f && measuredLower < measuredUpper) {
                        calibratedPitchRadius = Mathf.Clamp(calibratedPitchRadius, measuredLower, measuredUpper);
                    }

                    return Mathf.Max(calibratedPitchRadius, 0.05f);
                }

                return Mathf.Max(nominalPitchRadius, 0.05f);
            }

            // Unknown tooth count fallback: approximate pitch circle from measured outside radius.
            return Mathf.Max(measuredOuterRadius - (pitch * 0.30f), 0.05f);
        }

        private static float ComputeExpectedOutsideRadius(float pitch, int toothCount) {
            if (toothCount <= 2) {
                return pitch * 2f;
            }

            float toothAngle = Mathf.PI / toothCount;
            float cotangent = 1f / Mathf.Tan(toothAngle);
            return 0.5f * pitch * (0.6f + cotangent);
        }

        public static float EstimateRadius(GameObject obj, Vector3 axis, Vector3 center) {
            GameObject partObject = ResolvePartObject(obj);
            if (partObject == null) {
                return 0.5f;
            }

            if (!TryBuildPlaneBasis(axis, out Vector3 basisX, out Vector3 basisY)) {
                return 0.5f;
            }

            float meshRadius = EstimateRadiusFromMesh(partObject, center, basisX, basisY);
            if (meshRadius > 0.0001f) {
                return Mathf.Max(meshRadius, 0.05f);
            }

            float boundsRadius = EstimateRadiusFromBounds(partObject, center, basisX, basisY);
            return Mathf.Max(boundsRadius, 0.05f);
        }

        private static bool TryBuildPlaneBasis(Vector3 axis, out Vector3 basisX, out Vector3 basisY) {
            Vector3 normalizedAxis = axis.normalized;
            if (normalizedAxis.sqrMagnitude < 0.0001f) {
                basisX = Vector3.right;
                basisY = Vector3.up;
                return false;
            }

            basisX = Vector3.Cross(normalizedAxis, Vector3.up);
            if (basisX.sqrMagnitude < 0.0001f) {
                basisX = Vector3.Cross(normalizedAxis, Vector3.right);
            }

            if (basisX.sqrMagnitude < 0.0001f) {
                basisX = Vector3.right;
                basisY = Vector3.up;
                return false;
            }

            basisX.Normalize();
            basisY = Vector3.Cross(normalizedAxis, basisX).normalized;
            return true;
        }

        private static float EstimateRadiusFromMesh(GameObject partObject, Vector3 center, Vector3 basisX, Vector3 basisY) {
            float maxRadius = 0f;
            bool foundVertex = false;

            MeshFilter[] meshFilters = partObject.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++) {
                MeshFilter meshFilter = meshFilters[i];
                if (meshFilter == null || meshFilter.sharedMesh == null) {
                    continue;
                }

                if (!meshFilter.gameObject.activeInHierarchy) {
                    continue;
                }

                Renderer meshRenderer = meshFilter.GetComponent<Renderer>();
                if (meshRenderer != null && !meshRenderer.enabled) {
                    continue;
                }

                Vector3[] vertices = meshFilter.sharedMesh.vertices;
                Transform meshTransform = meshFilter.transform;

                for (int j = 0; j < vertices.Length; j++) {
                    Vector3 worldVertex = meshTransform.TransformPoint(vertices[j]);
                    float distance = DistanceInPlane(worldVertex - center, basisX, basisY);
                    if (distance > maxRadius) {
                        maxRadius = distance;
                    }
                    foundVertex = true;
                }
            }

            return foundVertex ? maxRadius : 0f;
        }

        private static float EstimateRadiusFromBounds(GameObject partObject, Vector3 center, Vector3 basisX, Vector3 basisY) {
            Renderer[] allRenderers = partObject.GetComponentsInChildren<Renderer>(true);
            Renderer firstActiveRenderer = null;

            for (int i = 0; i < allRenderers.Length; i++) {
                Renderer renderer = allRenderers[i];
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy) {
                    firstActiveRenderer = renderer;
                    break;
                }
            }

            if (firstActiveRenderer == null) {
                return 0.5f;
            }

            Bounds bounds = firstActiveRenderer.bounds;
            for (int i = 0; i < allRenderers.Length; i++) {
                Renderer renderer = allRenderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer == firstActiveRenderer) {
                    continue;
                }
                bounds.Encapsulate(renderer.bounds);
            }

            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3[] corners = {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
            };

            float maxRadius = 0f;
            for (int i = 0; i < corners.Length; i++) {
                float distance = DistanceInPlane(corners[i] - center, basisX, basisY);
                if (distance > maxRadius) {
                    maxRadius = distance;
                }
            }

            return maxRadius;
        }

        private static float DistanceInPlane(Vector3 offset, Vector3 basisX, Vector3 basisY) {
            float x = Vector3.Dot(offset, basisX);
            float y = Vector3.Dot(offset, basisY);
            return Mathf.Sqrt((x * x) + (y * y));
        }

        private static bool TryParseToothCountFromChildObjects(GameObject partObject, out int toothCount) {
            toothCount = 0;

            SavedObject[] childSavedObjects = partObject.GetComponentsInChildren<SavedObject>(true);

            // Prefer the active visual variant if multiple tooth variants exist under one root.
            for (int pass = 0; pass < 2; pass++) {
                bool requireActive = pass == 0;

                for (int i = 0; i < childSavedObjects.Length; i++) {
                    SavedObject childSavedObject = childSavedObjects[i];
                    if (childSavedObject == null || childSavedObject.gameObject == partObject) {
                        continue;
                    }

                    if (requireActive && !childSavedObject.gameObject.activeInHierarchy) {
                        continue;
                    }

                    int idToothCount = ParseFirstNumber(childSavedObject.id);
                    if (idToothCount > 0) {
                        toothCount = idToothCount;
                        return true;
                    }

                    if (childSavedObject.TryGetComponent(out PartName childPartName)) {
                        int nameToothCount = ParseFirstNumber(childPartName.name);
                        if (nameToothCount > 0) {
                            toothCount = nameToothCount;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static float ResolvePlanarScale(ChainEndpoint endpoint) {
            if (endpoint == null) {
                return 1f;
            }

            Transform target = endpoint.transform;
            Vector3 localAxis = target.InverseTransformDirection(endpoint.WorldAxis).normalized;
            if (localAxis.sqrMagnitude < 0.0001f) {
                return 1f;
            }

            Vector3 localReference = Mathf.Abs(Vector3.Dot(localAxis, Vector3.up)) > 0.95f
                ? Vector3.right
                : Vector3.up;

            Vector3 localTangentA = Vector3.Cross(localAxis, localReference);
            if (localTangentA.sqrMagnitude < 0.0001f) {
                return 1f;
            }
            localTangentA.Normalize();

            Vector3 localTangentB = Vector3.Cross(localAxis, localTangentA).normalized;

            float scaleA = target.TransformVector(localTangentA).magnitude;
            float scaleB = target.TransformVector(localTangentB).magnitude;
            float planarScale = (scaleA + scaleB) * 0.5f;

            if (float.IsNaN(planarScale) || float.IsInfinity(planarScale) || planarScale < 0.0001f) {
                return 1f;
            }

            return planarScale;
        }
    }
}
