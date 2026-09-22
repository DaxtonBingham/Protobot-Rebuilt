using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Builds;
using Protobot.SelectionSystem;
using UnityEngine;

namespace Protobot.ChainSystem {
    public static class ChainGuideRuntimeAuthoring {
        public const string RuntimeGuideSocketId = "runtime-guide";
        private const string RuntimeGuideName = "Runtime Chain Guide";
        private const float RadiusStep = 0.05f;
        private static SelectionManager[] cachedSelectionManagers;

        public static bool TryGetSelectedPart(out GameObject selectedPart) {
            selectedPart = null;

            SelectionManager[] selectionManagers = GetSelectionManagers();
            var uniqueCandidates = new HashSet<GameObject>();

            for (int i = 0; i < selectionManagers.Length; i++) {
                GameObject selectedObject = selectionManagers[i]?.current?.gameObject;
                GameObject resolvedPart = ResolveSavedPartFromSelection(selectedObject);
                if (resolvedPart != null) {
                    uniqueCandidates.Add(resolvedPart);
                }
            }

            if (uniqueCandidates.Count == 0) {
                return false;
            }

            if (uniqueCandidates.Count == 1) {
                selectedPart = uniqueCandidates.First();
                return true;
            }

            Camera cam = PivotCamera.Main != null ? PivotCamera.Main.camera : Camera.main;
            if (cam == null) {
                selectedPart = uniqueCandidates.First();
                return true;
            }

            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float bestScore = float.MaxValue;

            foreach (GameObject candidate in uniqueCandidates) {
                Vector3 screenPoint = cam.WorldToScreenPoint(GetReferencePoint(candidate));
                float distance = Vector2.Distance(new Vector2(screenPoint.x, screenPoint.y), screenCenter);
                float score = screenPoint.z > 0f ? distance : distance + 100000f;
                if (score < bestScore) {
                    bestScore = score;
                    selectedPart = candidate;
                }
            }

            return selectedPart != null;
        }

        public static ChainGuide GetRuntimeGuide(GameObject partObject) {
            if (partObject == null) {
                return null;
            }

            return partObject.GetComponentsInChildren<ChainGuide>(true)
                .FirstOrDefault(guide => guide != null && guide.gameObject.activeInHierarchy && guide.MatchesSocket(RuntimeGuideSocketId));
        }

        public static bool GuideUsesProjectedShape(ChainGuide guide) {
            GameObject shapeSource = ResolveGuideShapeSourceObject(guide);
            return shapeSource != null && HasRenderableGeometry(shapeSource);
        }

        public static bool CycleRuntimeGuideRoutingBias(GameObject partObject, out ChainGuideRoutingBias newBias) {
            newBias = ChainGuideRoutingBias.Auto;
            ChainGuide guide = GetRuntimeGuide(partObject);
            if (guide == null) {
                return false;
            }

            newBias = guide.RoutingBias switch {
                ChainGuideRoutingBias.Auto => ChainGuideRoutingBias.PushInward,
                ChainGuideRoutingBias.PushInward => ChainGuideRoutingBias.PushOutward,
                _ => ChainGuideRoutingBias.Auto
            };

            guide.SetRoutingBias(newBias);
            ChainManager.NotifyEndpointObjectChanged(partObject);
            return true;
        }

        public static bool ToggleRuntimeGuideSideFlip(GameObject partObject, out bool flipSide) {
            flipSide = false;
            ChainGuide guide = GetRuntimeGuide(partObject);
            if (guide == null) {
                return false;
            }

            flipSide = !guide.FlipSide;
            guide.SetFlipSide(flipSide);
            ChainManager.NotifyEndpointObjectChanged(partObject);
            return true;
        }

        public static ChainGuide EnsureRuntimeGuide(GameObject partObject) {
            if (partObject == null) {
                return null;
            }

            ChainGuide existingGuide = GetRuntimeGuide(partObject);
            if (existingGuide != null) {
                existingGuide.SetSaveWithBuild(true);
                return existingGuide;
            }

            Vector3 worldCenter = GetReferencePoint(partObject);
            Vector3 worldAxis = ResolveGuideAxis(partObject);
            float radius = Mathf.Max(ChainSprocketUtility.EstimateRadius(partObject, worldAxis, worldCenter), 0.05f);

            GameObject guideObject = new GameObject(RuntimeGuideName);
            guideObject.transform.SetParent(partObject.transform, false);
            guideObject.transform.position = worldCenter;
            guideObject.transform.rotation = Quaternion.LookRotation(worldAxis.normalized, ResolveGuideUp(worldAxis));

            ChainGuide guide = guideObject.AddComponent<ChainGuide>();
            guide.ConfigureManual(
                RuntimeGuideSocketId,
                guideObject.transform.localPosition,
                guideObject.transform.localRotation,
                radius,
                true,
                ChainGuideRoutingBias.Auto,
                false);
            return guide;
        }

        public static bool RemoveRuntimeGuide(GameObject partObject) {
            ChainGuide guide = GetRuntimeGuide(partObject);
            if (guide == null) {
                return false;
            }

            guide.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(guide.gameObject);
            return true;
        }

        public static bool AdjustRuntimeGuideRadius(GameObject partObject, int direction, out float newRadius) {
            newRadius = 0f;
            ChainGuide guide = GetRuntimeGuide(partObject);
            if (guide == null) {
                return false;
            }

            guide.SetRadius(guide.Radius + (RadiusStep * Mathf.Sign(direction)));
            newRadius = guide.Radius;
            ChainManager.NotifyEndpointObjectChanged(partObject);
            return true;
        }

        public static ChainGuideData[] ExportBuildData(Func<GameObject, int> objectToIndex) {
            if (objectToIndex == null) {
                return System.Array.Empty<ChainGuideData>();
            }

            List<GameObject> loadedObjects = PartsManager.FindLoadedObjects();
            var editor = UnityEngine.Object.FindObjectOfType<InsertChainTool>();
            var data = new List<ChainGuideData>();

            for (int i = 0; i < loadedObjects.Count; i++) {
                GameObject partObject = loadedObjects[i];
                if (partObject == null) {
                    continue;
                }

                if (editor != null && editor.IsDraftGuide(partObject)) continue;
                int partIndex = objectToIndex(partObject);
                if (partIndex < 0) {
                    continue;
                }

                ChainGuide[] guides = partObject.GetComponentsInChildren<ChainGuide>(true);
                for (int j = 0; j < guides.Length; j++) {
                    ChainGuide guide = guides[j];
                    if (guide == null || !guide.gameObject.activeInHierarchy || !guide.SaveWithBuild) {
                        continue;
                    }

                    Transform guideTransform = guide.transform;
                    ChainGuideRoutingBias bias = guide.RoutingBias;
                    bool flip = guide.FlipSide;
                    Vector3 hint = editor != null ? editor.GetCommittedContactHint(guide) : guide.LocalContactHint;
                    if (editor != null) editor.GetCommittedGuideSettings(guide, out bias, out flip);
                    data.Add(new ChainGuideData {
                        partIndex = partIndex,
                        socketId = guide.SocketId,
                        localX = guideTransform.localPosition.x,
                        localY = guideTransform.localPosition.y,
                        localZ = guideTransform.localPosition.z,
                        rotX = guideTransform.localRotation.x,
                        rotY = guideTransform.localRotation.y,
                        rotZ = guideTransform.localRotation.z,
                        rotW = guideTransform.localRotation.w,
                        radius = guide.Radius,
                        routingBias = (int)bias,
                        flipSide = flip, contactX = hint.x, contactY = hint.y, contactZ = hint.z
                    });
                }
            }

            return data.ToArray();
        }

        public static void LoadBuildData(ChainGuideData[] guideData, Func<int, GameObject> indexToObject) {
            if (guideData == null || guideData.Length == 0 || indexToObject == null) {
                return;
            }

            for (int i = 0; i < guideData.Length; i++) {
                ChainGuideData data = guideData[i];
                if (data == null || data.partIndex < 0) {
                    continue;
                }

                GameObject partObject = indexToObject(data.partIndex);
                if (partObject == null) {
                    continue;
                }

                ChainGuide existingGuide = partObject.GetComponentsInChildren<ChainGuide>(true)
                    .FirstOrDefault(guide => guide != null && guide.MatchesSocket(data.socketId));
                if (existingGuide != null) {
                    existingGuide.ConfigureManual(
                        data.socketId,
                        data.GetLocalPosition(),
                        data.GetLocalRotation(),
                        data.radius,
                        true,
                        ResolveRoutingBias(data.routingBias),
                        data.flipSide);
                    existingGuide.SetLocalContactHint(data.ContactHint);
                    continue;
                }

                GameObject guideObject = new GameObject(RuntimeGuideName);
                guideObject.transform.SetParent(partObject.transform, false);

                ChainGuide guideComponent = guideObject.AddComponent<ChainGuide>();
                guideComponent.ConfigureManual(
                    data.socketId,
                    data.GetLocalPosition(),
                    data.GetLocalRotation(),
                    data.radius,
                    true,
                    ResolveRoutingBias(data.routingBias),
                    data.flipSide);
                guideComponent.SetLocalContactHint(data.ContactHint);
            }
        }

        private static GameObject ResolveSavedPartFromSelection(GameObject selectedObject) {
            if (selectedObject == null) {
                return null;
            }

            GameObject resolved = ChainSprocketUtility.ResolvePartObject(selectedObject);
            if (resolved != null && resolved.GetComponent<SavedObject>() != null) {
                return resolved;
            }

            SavedObject savedObject = selectedObject.GetComponent<SavedObject>()
                ?? selectedObject.GetComponentInParent<SavedObject>()
                ?? selectedObject.GetComponentInChildren<SavedObject>();
            return savedObject != null ? savedObject.gameObject : null;
        }

        private static SelectionManager[] GetSelectionManagers() {
            if (cachedSelectionManagers == null
                || cachedSelectionManagers.Length == 0
                || cachedSelectionManagers.Any(manager => manager == null)) {
                cachedSelectionManagers = UnityEngine.Object.FindObjectsOfType<SelectionManager>(true);
            }

            return cachedSelectionManagers;
        }

        private static Vector3 GetReferencePoint(GameObject partObject) {
            if (partObject == null) {
                return Vector3.zero;
            }

            if (partObject.TryGetComponent(out PartData partData) && partData.PrimaryHole != null) {
                return partData.PrimaryHole.position;
            }

            Renderer[] renderers = partObject.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0) {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) {
                    if (renderers[i] != null) {
                        bounds.Encapsulate(renderers[i].bounds);
                    }
                }
                return bounds.center;
            }

            return partObject.transform.position;
        }

        private static Vector3 ResolveGuideAxis(GameObject partObject) {
            if (partObject != null && partObject.TryGetComponent(out PartData partData) && partData.PrimaryHole != null) {
                Vector3 primaryAxis = partData.PrimaryHole.forward;
                if (primaryAxis.sqrMagnitude > 0.0001f) {
                    return primaryAxis.normalized;
                }
            }

            Vector3 fallback = partObject != null ? partObject.transform.forward : Vector3.forward;
            return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
        }

        private static Vector3 ResolveGuideUp(Vector3 axis) {
            Vector3 up = Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up)) > 0.95f
                ? Vector3.right
                : Vector3.up;
            Vector3 projected = Vector3.ProjectOnPlane(up, axis);
            return projected.sqrMagnitude > 0.0001f ? projected.normalized : Vector3.up;
        }

        private static ChainGuideRoutingBias ResolveRoutingBias(int serializedValue) {
            return Enum.IsDefined(typeof(ChainGuideRoutingBias), serializedValue)
                ? (ChainGuideRoutingBias)serializedValue
                : ChainGuideRoutingBias.Auto;
        }

        private static GameObject ResolveGuideShapeSourceObject(ChainGuide guide) {
            if (guide == null) {
                return null;
            }

            GameObject guideObject = guide.gameObject;
            if (HasRenderableGeometry(guideObject)) {
                return guideObject;
            }

            GameObject partObject = ChainSprocketUtility.ResolvePartObject(guideObject);
            return partObject != null && HasRenderableGeometry(partObject)
                ? partObject
                : null;
        }

        private static bool HasRenderableGeometry(GameObject sourceObject) {
            if (sourceObject == null) {
                return false;
            }

            MeshFilter[] meshFilters = sourceObject.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++) {
                MeshFilter meshFilter = meshFilters[i];
                if (meshFilter != null && meshFilter.sharedMesh != null) {
                    return true;
                }
            }

            Renderer[] renderers = sourceObject.GetComponentsInChildren<Renderer>(true);
            return renderers != null && renderers.Length > 0;
        }
    }
}
