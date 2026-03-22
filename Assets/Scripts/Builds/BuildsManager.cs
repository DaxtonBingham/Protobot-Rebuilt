using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.UI;
using SFB;

using Protobot.UI;
using Protobot.InputEvents;
using Protobot.CustomParts;
using UnityEngine.SceneManagement;

namespace Protobot.Builds {
    public class BuildsManager : MonoBehaviour {
        [SerializeField] private InputEvent saveInput;

        [SerializeField] private UnsavedChangesUI unsavedChangesMenu;
        
        /// <summary>
        /// Stores the path where the current build is located
        /// </summary>
        public string buildPath = "";
        
        /// <summary>
        /// Stores the currently saved data for the loaded build
        /// </summary>
        private BuildData savedBuildData;

        private string attemptPath = "";
        private BuildData attemptData;
        
        public BuildDataUnityEvent OnLoadBuild;
        public BuildDataUnityEvent OnSaveBuild;

        private bool avoidingQuit = false;
        private bool forceQuit = false;

        private bool IsNotSaved => buildPath == "";

        private void Awake() {
            saveInput.performed += Save;
        }

        public void Start() {
            buildPath = "";
            
            SceneBuild.OnGenerateBuild += (data) => {
                OnLoadBuild.Invoke(data);
            };
            

            Application.wantsToQuit += () => {
                avoidingQuit = HasUnsavedChanges() && !forceQuit;
                if (avoidingQuit) {
                    unsavedChangesMenu.Enable(IsNotSaved);
                }

                return !avoidingQuit;
            };

            unsavedChangesMenu.OnPressDiscard += () => {
                if (avoidingQuit) 
                    Quit();
                else
                    LoadAttempt();
            };

            unsavedChangesMenu.OnPressSave += () => {
                if (avoidingQuit)
                    SaveAndQuit();
                else
                    SaveAndLoadAttempt();
            };
            
            string[] arguments = Environment.GetCommandLineArgs();
            string initPath = arguments[0];
            var initData = ParsePath(initPath);

            if (initData != null)
                AttemptLoad(initData, initPath);
        }

        public string GetFileName() => PathToFileName(buildPath);
        
        public static string PathToFileName(string path) => (path.Length > 0) ? path.Split('\\')[^1] : "";
        
        public void Save() {
            if (buildPath == "") {
                SaveAs();
                return;
            }

            var sceneBuildData = SceneBuild.ToBuildData();
            if (!BuildSerialization.TrySerializeBuild(buildPath, sceneBuildData)) {
                return;
            }

            savedBuildData = sceneBuildData;
            OnSaveBuild?.Invoke(sceneBuildData);
        }

        public void SaveAs() {
            var path = StandaloneFileBrowser.SaveFilePanel("Save Build File", "", "", "pbb");

            if (path == "") return;

            buildPath = path;
            Save();
        }

        public void SaveAndQuit() {
            Save();
            Quit();
        }

        public void Quit() {
            forceQuit = true;
            Application.Quit();
        }

        public void AttemptQuit() {
            Application.Quit();
        }

        private bool HasUnsavedChanges() {
            var sceneBuild = SceneBuild.ToBuildData();
            
            if (IsNotSaved) {
                return sceneBuild.parts.Length > 0;
            }

            return !sceneBuild.CompareData(savedBuildData);
        }

        public void OpenBuild() {
            var paths = StandaloneFileBrowser.OpenFilePanel("Open Build File", "", "pbb", false);

            if (paths.Length == 0 || paths[0] == "") return;

            var path = paths[0];

            var build = ParsePath(path);
            if (build == null) {
                return;
            }

            AttemptLoad(build, path);
        }
        
        /// <summary>
        /// Converts a given file path to BuildData
        /// </summary>
        public static BuildData ParsePath(string filePath) {
            return BuildSerialization.TryDeserializeBuild(filePath, out BuildData build)
                ? build
                : null;
        }
        
        /// <summary>
        /// Sets the attempt variables to be ready once a load call is given
        /// </summary>
        /// <param name="newData"></param>
        /// <param name="newPath"></param>
        public void AttemptLoad(BuildData newData, string newPath) {
            attemptData = newData;
            attemptPath = newPath;

            if (HasUnsavedChanges()) {
                unsavedChangesMenu.Enable(IsNotSaved);
            }
            else {
                LoadAttempt();
            }
        }

        public void SaveAndLoadAttempt() {
            Save();
            LoadAttempt();
        }

        public void LoadAttempt() {
            savedBuildData = attemptData;
            buildPath = attemptPath;
            SceneBuild.GenerateBuild(attemptData);

            // Call OutputPartsList to update the weight display after placing a part
                PartListOutput partListOutput = FindObjectOfType<PartListOutput>();
                if (partListOutput != null) {
                    partListOutput.CalculatePartsList(); // Recalculate and notify weight update
                }
        }

        /// <summary>
        /// Loads an empty build with an untitled.pbb
        /// </summary>
        public void CreateNewBuild() {
            AttemptLoad(SceneBuild.DefaultBuild, "");
        }
    }

    public static class BuildSerialization {
        public static bool TryDeserializeBuild(string filePath, out BuildData buildData) {
            buildData = null;
            if (!IsSupportedBuildPath(filePath) || !File.Exists(filePath)) {
                return false;
            }

            try {
                using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    if (file.Length <= 0) {
                        Debug.LogWarning($"Build load failed: '{filePath}' is empty.");
                        return false;
                    }

                    BinaryFormatter formatter = CreateFormatter();
                    BuildData parsed = formatter.Deserialize(file) as BuildData;
                    if (parsed == null) {
                        Debug.LogWarning($"Build load failed: '{filePath}' did not deserialize to BuildData.");
                        return false;
                    }

                    NormalizeBuildData(parsed);
                    buildData = parsed;
                }
                return true;
            }
            catch (Exception ex) {
                Debug.LogWarning($"Build load failed for '{filePath}': {ex.Message}");
                return false;
            }
        }

        public static bool TrySerializeBuild(string filePath, BuildData buildData) {
            if (string.IsNullOrWhiteSpace(filePath) || buildData == null) {
                return false;
            }

            string tempPath = filePath + ".tmp";
            try {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    BinaryFormatter formatter = CreateFormatter();
                    formatter.Serialize(file, buildData);
                    file.Flush();
                }

                if (File.Exists(filePath)) {
                    try {
                        File.Replace(tempPath, filePath, null);
                    }
                    catch {
                        File.Delete(filePath);
                        File.Move(tempPath, filePath);
                    }
                }
                else {
                    File.Move(tempPath, filePath);
                }

                return true;
            }
            catch (Exception ex) {
                Debug.LogWarning($"Build save failed for '{filePath}': {ex.Message}");
                if (File.Exists(tempPath)) {
                    try {
                        File.Delete(tempPath);
                    }
                    catch {
                        // Ignore temp cleanup failures.
                    }
                }

                return false;
            }
        }

        private static BinaryFormatter CreateFormatter() {
            var formatter = new BinaryFormatter();
            var selector = new SurrogateSelector();
            var context = new StreamingContext(StreamingContextStates.All);
            selector.AddSurrogate(typeof(Vector2), context, new Vector2Surrogate());
            selector.AddSurrogate(typeof(Vector3), context, new Vector3Surrogate());
            selector.AddSurrogate(typeof(Quaternion), context, new QuaternionSurrogate());
            selector.AddSurrogate(typeof(Color), context, new ColorSurrogate());
            formatter.SurrogateSelector = selector;
            return formatter;
        }

        private static bool IsSupportedBuildPath(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath)) {
                return false;
            }

            string extension = Path.GetExtension(filePath);
            return string.Equals(extension, ".pbb", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".Build", StringComparison.OrdinalIgnoreCase);
        }

        private static void NormalizeBuildData(BuildData buildData) {
            if (buildData == null) {
                return;
            }

            buildData.parts ??= Array.Empty<ObjectData>();
            buildData.chains ??= Array.Empty<ChainData>();
            buildData.customDefinitions ??= Array.Empty<CustomPartDefinition>();
            buildData.camera ??= new CameraData {
                xPos = 0d,
                yPos = 0d,
                zPos = 0d,
                xRot = 30d,
                yRot = 45d,
                zRot = 0d,
                zoom = -15d,
                isOrtho = false
            };

            for (int i = 0; i < buildData.parts.Length; i++) {
                NormalizeObjectData(buildData.parts[i]);
            }

            for (int i = 0; i < buildData.customDefinitions.Length; i++) {
                NormalizeCustomDefinition(buildData.customDefinitions[i]);
            }
        }

        private static void NormalizeObjectData(ObjectData objectData) {
            if (objectData == null) {
                return;
            }

            if (string.IsNullOrWhiteSpace(objectData.partId) && !string.IsNullOrWhiteSpace(objectData.meshId)) {
                objectData.partId = objectData.meshId;
            }

            objectData.states ??= string.Empty;
        }

        private static void NormalizeCustomDefinition(CustomPartDefinition definition) {
            if (definition == null) {
                return;
            }

            if (string.IsNullOrWhiteSpace(definition.definitionId)) {
                definition.definitionId = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(definition.name)) {
                definition.name = "Custom Part";
            }

            if (definition.thicknessInches <= 0f) {
                definition.thicknessInches = CustomPartDefinition.DefaultThicknessInches;
            }

            definition.materialName ??= "Custom Part Material";
            definition.sketch ??= new SketchData();
            definition.sketch.outerLoop ??= new LoopData {
                name = "Outline",
                closed = true,
                isCutout = false
            };
            definition.sketch.cutoutLoops ??= Array.Empty<LoopData>();
            definition.sketch.constraints ??= Array.Empty<ConstraintData>();
            NormalizeLoop(definition.sketch.outerLoop, isCutout: false, defaultName: "Outline");
            for (int i = 0; i < definition.sketch.cutoutLoops.Length; i++) {
                LoopData loop = definition.sketch.cutoutLoops[i] ?? new LoopData {
                    isCutout = true,
                    name = $"Cutout {i + 1}"
                };
                definition.sketch.cutoutLoops[i] = loop;
                NormalizeLoop(loop, isCutout: true, defaultName: $"Cutout {i + 1}");
            }

            definition.holes ??= Array.Empty<CustomHoleDefinition>();
            for (int i = 0; i < definition.holes.Length; i++) {
                CustomHoleDefinition hole = definition.holes[i];
                if (hole == null) {
                    hole = new CustomHoleDefinition();
                    definition.holes[i] = hole;
                }

                if (string.IsNullOrWhiteSpace(hole.id)) {
                    hole.id = Guid.NewGuid().ToString("N");
                }

                if (hole.size.x <= 0f || hole.size.y <= 0f) {
                    hole.size = new Vector2(0.182f, 0.182f);
                }

                if (hole.depthInches <= 0f) {
                    hole.depthInches = definition.thicknessInches;
                }
            }

            definition.patterns ??= Array.Empty<PatternFeature>();
            for (int i = 0; i < definition.patterns.Length; i++) {
                PatternFeature pattern = definition.patterns[i];
                if (pattern == null) {
                    pattern = new PatternFeature();
                    definition.patterns[i] = pattern;
                }

                if (string.IsNullOrWhiteSpace(pattern.id)) {
                    pattern.id = Guid.NewGuid().ToString("N");
                }

                pattern.transform ??= new PatternTransform2D();
                pattern.parameters ??= new PatternParams();
            }

            definition.metadata ??= new DefinitionMetadata();
            definition.metadata.notes ??= string.Empty;
        }

        private static void NormalizeLoop(LoopData loop, bool isCutout, string defaultName) {
            if (loop == null) {
                return;
            }

            if (string.IsNullOrWhiteSpace(loop.id)) {
                loop.id = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(loop.name)) {
                loop.name = defaultName;
            }

            loop.isCutout = isCutout;
            loop.anchors ??= Array.Empty<AnchorData>();
            for (int i = 0; i < loop.anchors.Length; i++) {
                AnchorData anchor = loop.anchors[i];
                if (anchor == null) {
                    anchor = new AnchorData();
                    loop.anchors[i] = anchor;
                }

                if (string.IsNullOrWhiteSpace(anchor.id)) {
                    anchor.id = Guid.NewGuid().ToString("N");
                }
            }

            int anchorCount = loop.anchors.Length;
            if (anchorCount <= 0) {
                loop.segmentKinds = Array.Empty<SegmentKind>();
                return;
            }

            if (loop.segmentKinds == null || loop.segmentKinds.Length != anchorCount) {
                SegmentKind[] kinds = new SegmentKind[anchorCount];
                for (int i = 0; i < anchorCount; i++) {
                    SegmentKind existing = loop.segmentKinds != null && i < loop.segmentKinds.Length
                        ? loop.segmentKinds[i]
                        : SegmentKind.Line;
                    kinds[i] = existing;
                }

                loop.segmentKinds = kinds;
            }
        }

        private sealed class Vector2Surrogate : ISerializationSurrogate {
            public void GetObjectData(object obj, SerializationInfo info, StreamingContext context) {
                var value = (Vector2)obj;
                info.AddValue("x", value.x);
                info.AddValue("y", value.y);
            }

            public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector) {
                return new Vector2(
                    info.GetSingle("x"),
                    info.GetSingle("y"));
            }
        }

        private sealed class Vector3Surrogate : ISerializationSurrogate {
            public void GetObjectData(object obj, SerializationInfo info, StreamingContext context) {
                var value = (Vector3)obj;
                info.AddValue("x", value.x);
                info.AddValue("y", value.y);
                info.AddValue("z", value.z);
            }

            public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector) {
                return new Vector3(
                    info.GetSingle("x"),
                    info.GetSingle("y"),
                    info.GetSingle("z"));
            }
        }

        private sealed class QuaternionSurrogate : ISerializationSurrogate {
            public void GetObjectData(object obj, SerializationInfo info, StreamingContext context) {
                var value = (Quaternion)obj;
                info.AddValue("x", value.x);
                info.AddValue("y", value.y);
                info.AddValue("z", value.z);
                info.AddValue("w", value.w);
            }

            public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector) {
                return new Quaternion(
                    info.GetSingle("x"),
                    info.GetSingle("y"),
                    info.GetSingle("z"),
                    info.GetSingle("w"));
            }
        }

        private sealed class ColorSurrogate : ISerializationSurrogate {
            public void GetObjectData(object obj, SerializationInfo info, StreamingContext context) {
                var value = (Color)obj;
                info.AddValue("r", value.r);
                info.AddValue("g", value.g);
                info.AddValue("b", value.b);
                info.AddValue("a", value.a);
            }

            public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector) {
                return new Color(
                    info.GetSingle("r"),
                    info.GetSingle("g"),
                    info.GetSingle("b"),
                    info.GetSingle("a"));
            }
        }
    }
}
