using System.Collections;
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using Protobot.ChainSystem;
using Protobot.CustomParts;

namespace Protobot.Builds {
    public static class SceneBuild {
        public static Action<BuildData> OnGenerateBuild;

        public static BuildData DefaultBuild => new BuildData {
            name = "DefaultBuild",
            camera = new CameraData {
                xPos = 0,
                yPos = 0,
                zPos = 0,
                xRot = 30,
                yRot = 45,
                zRot = 0,
                zoom = -15,
                isOrtho = false
            },
            lastWriteTime = DateTime.Now.ToString("MMMM dd, yyyy h:mm tt")
        };

        /// <summary>
        /// Generates all the objects into the scene using given BuildData
        /// </summary>
        public static void GenerateBuild(BuildData buildData) {
            Debug.Log("Generating a build with " +
                      (buildData.parts == null ? 0 : buildData.parts.Length) + " parts");

            CustomPartRegistry.Clear();
            if (buildData.customDefinitions != null && buildData.customDefinitions.Length > 0) {
                CustomPartRegistry.RegisterDefinitions(buildData.customDefinitions);
            }

            ChainManager.ClearAll();
            PartsManager.DestroyLoadedObjects();

            //Camera Data
            CameraData camData = buildData.camera;

            Vector3 savedCamPos = new Vector3((float)camData.xPos, (float)camData.yPos, (float)camData.zPos);
            Vector3 savedCamAngle = new Vector3((float)camData.xRot, (float)camData.yRot, (float)camData.zRot);

            PivotCamera.Main.SetTransform(savedCamPos, savedCamAngle, (float)camData.zoom);

            var projectionSwitcher = PivotCamera.Main.GetComponent<ProjectionSwitcher>();

            if (camData.isOrtho)
                projectionSwitcher.SwitchToOrtho(0);
            else
                projectionSwitcher.SwitchToPers(0);

            //Parts
            if (buildData.parts != null) {
                Dictionary<int, GameObject> generatedObjects = new Dictionary<int, GameObject>();
                List<KeyValuePair<int, ObjectData>> connectingObjects = new List<KeyValuePair<int, ObjectData>>();

                for (int i = 0; i < buildData.parts.Length; i++) {
                    ObjectData part = buildData.parts[i];
                    if (part.partId == "Error") continue; //this is jank but I'm avoiding editing main scene
                    bool isCustomPart = !string.IsNullOrWhiteSpace(part.customDefinitionId);
                    if (isCustomPart) {
                        GameObject generatedCustom = GenerateObject(part, buildData);
                        if (generatedCustom != null) {
                            generatedObjects[i] = generatedCustom;
                        }

                        continue;
                    }

                    PartType partType = PartsManager.GetPartType(part.partId);
                    if (partType == null) {
                        continue;
                    }

                    if (partType.connectingPart) {
                        connectingObjects.Add(new KeyValuePair<int, ObjectData>(i, part));
                    }
                    else {
                        GameObject generated = GenerateObject(part, buildData);
                        if (generated != null) {
                            generatedObjects[i] = generated;
                        }
                    }
                }

                foreach (KeyValuePair<int, ObjectData> part in connectingObjects) {
                    GameObject generated = GenerateObject(part.Value, buildData);
                    if (generated != null) {
                        generatedObjects[part.Key] = generated;
                    }
                }

                ChainManager.LoadBuildData(
                    buildData.chains,
                    index => generatedObjects.ContainsKey(index) ? generatedObjects[index] : null);

            }
            
            OnGenerateBuild?.Invoke(buildData);

            if (AppPlatform.OnMac)
            {
                try
                {
                    GameObject.Find("Mac Support Check").GetComponent<MacRebind>().RebindCheck();
                }
                catch (Exception ex)
                {
                    Debug.LogError("An error occurred while performing Mac rebind check: " + ex.Message);
                }
            }
        }

        private static GameObject GenerateObject(ObjectData objectData, BuildData buildData)
        {
            GameObject generatedObject = null;
            if (!string.IsNullOrWhiteSpace(objectData.customDefinitionId)) {
                generatedObject = CustomPartGenerator.Generate(
                    objectData.customDefinitionId,
                    objectData.GetPos(),
                    objectData.GetRot(),
                    objectData.customInstanceId);
            }
            else {
                generatedObject = PartsManager.GeneratePart(objectData.partId, objectData.GetPos(), objectData.GetRot());
            }

            if (generatedObject == null) {
                return null;
            }

            //there are only 2 versions of Protobot legacy publicly released that I could find most before Beta 1.3.1 are just guesses
            string[] versionsNoColor = new string[] {"1.0", "1.1", "1.1.1", "Beta 1.2", "Beta 1.3", "Beta 1.3.1", "1.3.2", "1.3.3", "1.3.4" };
            //Debug.Log(buildData.version);    
            if(!versionsNoColor.Contains(buildData.version) && buildData.version != null)
            { 
                generatedObject.GetComponent<Renderer>().material.color = objectData.GetColor();
            }
            return generatedObject;
        }
            


        /// <summary>
        /// Generates an empty build with default camera data into the scene
        /// </summary>
        public static BuildData GenerateDefault(string buildName) {
            var newBuild = DefaultBuild;
            newBuild.name = buildName;

            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            newBuild.fileName = new string(buildName.Where(m => !invalidChars.Contains(m)).ToArray<char>());

            GenerateBuild(newBuild);

            return newBuild;
        }

        /// <summary>
        /// Converts scene objects into BuildData
        /// </summary>
        /// <remarks>Does not contain lastWriteTime, createTime, fileName, or name</remarks>
        public static BuildData ToBuildData() {
            //Camera
            PivotCamera cam = PivotCamera.Main;
            ProjectionSwitcher projectionSwitcher = cam.GetComponent<ProjectionSwitcher>();

            CameraData newCameraData = new CameraData {
                xPos = cam.focusPosition.x,
                yPos = cam.focusPosition.y,
                zPos = cam.focusPosition.z,

                xRot = cam.lookAngle.x,
                yRot = cam.lookAngle.y,
                zRot = cam.lookAngle.z,

                zoom = cam.focusDistance,

                isOrtho = projectionSwitcher.isOrtho
            };

            //Parts
            List<GameObject> sceneObjs = PartsManager.FindLoadedObjects();

            ObjectData[] newParts = new ObjectData[sceneObjs.Count];
            Dictionary<GameObject, int> objectIndices = new Dictionary<GameObject, int>();
            var customDefinitionIds = new HashSet<string>();

            for (int i = 0; i < newParts.Length; i++) {
                Transform tForm = sceneObjs[i].transform;
                SavedObject savedData = tForm.GetComponent<SavedObject>();
                Renderer savedColor = tForm.GetComponent<Renderer>();

                var position = tForm.position;
                var eulerAngles = tForm.eulerAngles;
                newParts[i] = new ObjectData {
                    partId = savedData.id,
                    states = savedData.state,
                    customDefinitionId = savedData.customDefinitionId,
                    customInstanceId = savedData.customInstanceId,

                    xPos = position.x,
                    yPos = position.y,
                    zPos = position.z,

                    xRot = eulerAngles.x,
                    yRot = eulerAngles.y,
                    zRot = eulerAngles.z,

                    rColor = savedColor.material.color.r,
                    bColor = savedColor.material.color.b,
                    gColor = savedColor.material.color.g,
                };

                if (!string.IsNullOrWhiteSpace(savedData.customDefinitionId)) {
                    customDefinitionIds.Add(savedData.customDefinitionId);
                }

                objectIndices[sceneObjs[i]] = i;
            }

            CustomPartDefinition[] customDefinitions = CustomPartRegistry.GetDefinitions(customDefinitionIds);

            return new BuildData {
                camera = newCameraData,
                parts = newParts,
                chains = ChainManager.ExportBuildData(obj => objectIndices.ContainsKey(obj) ? objectIndices[obj] : -1),
                customDefinitions = customDefinitions
            };
        }
    }
}
