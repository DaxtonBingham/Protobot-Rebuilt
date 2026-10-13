using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Protobot;
using Protobot.Builds;
using Protobot.CustomParts;
using UnityEditor;
using UnityEngine;

public static class BuildWorkflowVerification {
    [MenuItem("Tools/Verify Project Workflows")]
    public static void Verify() {
        int checks = 0;
        void Require(bool condition, string message) {
            if (!condition) throw new Exception("Project workflow: " + message);
            checks++;
        }
        ObjectData Part(int index) => new ObjectData { partId = "WSHR-Steel", instanceId = "part-" + index, xPos = index, rColor = .3, gColor = .4, bColor = .5 };
        BuildData Build(params ObjectData[] parts) => new BuildData { parts = parts, camera = SceneBuild.DefaultBuild.camera };
        var first = Build(Part(0), Part(1), Part(2));
        var reordered = Build(Part(2), Part(0), Part(1));
        first.chains = new[] { new ChainData { endpointIndices = new[] { 0, 2, 1 } } };
        reordered.chains = new[] { new ChainData { endpointIndices = new[] { 1, 0, 2 }, endpointSockets = new[] { "main", "main", "main" } } };
        first.chainGuides = new[] { new ChainGuideData { partIndex = 1, radius = .25f } };
        reordered.chainGuides = new[] { new ChainGuideData { partIndex = 2, radius = .25f } };
        Require(first.CompareData(reordered) && reordered.CompareData(first), "reordering parts preserves chain and guide identity");
        reordered.chains[0].endpointIndices = new[] { 0, 1, 2 };
        Require(!first.CompareData(reordered), "reordered chain references change topology");
        Require(!Build(Part(0), Part(0)).CompareData(Build(Part(0), Part(1))), "duplicate multiplicity");
        var paint = Part(0); paint.rColor = .9;
        Require(!Build(Part(0)).CompareData(Build(paint)), "paint changes dirty state");
        var metadata = Part(0); metadata.states = "different";
        Require(!Build(Part(0)).CompareData(Build(metadata)), "metadata changes dirty state");
        Require(Build(Part(0)).CompareData(Build(Part(0))), "identical build remains clean");
        Require(BuildsManager.PathToFileName("C:/robots/test.pbb") == "test.pbb", "slash filename");
        Require(BuildsManager.PathToFileName(@"C:\robots\test.pbb") == "test.pbb", "Windows filename");
        Require(BuildsManager.PathToFileName(null) == "", "missing filename");
        Require(!BuildSerialization.TryDeserializeBuild("bad\0path.pbb", out _), "invalid path rejected");
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue }) {
            var part = Part(0); part.xPos = bad;
            Require(!BuildDataValidation.TryValidate(Build(part), out _), "invalid position " + bad);
            part = Part(0); part.zRot = bad;
            Require(!BuildDataValidation.TryValidate(Build(part), out _), "invalid rotation " + bad);
            var build = Build(Part(0)); build.camera.zoom = bad;
            Require(!BuildDataValidation.TryValidate(build, out _), "invalid camera " + bad);
        }
        Require(!BuildDataValidation.TryValidate(Build(new ObjectData[] { null }), out _), "null part");
        Require(!BuildDataValidation.TryValidate(Build(new ObjectData()), out _), "missing part data");
        Require(!BuildDataValidation.TryValidate(Build(Part(0), Part(0)), out _), "duplicate document identity");
        var definition = CustomPartDefinition.CreateDefault();
        var custom = Part(0); custom.customDefinitionId = definition.definitionId;
        Require(!BuildDataValidation.TryValidate(Build(custom), out _), "missing custom definition");
        var customBuild = Build(custom); customBuild.customDefinitions = new[] { definition };
        Require(BuildDataValidation.TryValidate(customBuild, out _), "valid custom geometry");
        definition.sketch.outerLoop.anchors[0].position = new Vector2(float.NaN, 0);
        Require(!BuildDataValidation.TryValidate(customBuild, out _), "invalid custom geometry");
        var culture = CultureInfo.CurrentCulture;
        try {
            foreach (string name in new[] { "en-US", "de-DE", "fr-FR" }) {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Require(PartParameterValue.Parse("6.5") == 6.5f, "portable decimal in " + name);
                Require(PartParameterValue.Parse("6,5") == 6.5f, "legacy comma decimal in " + name);
                Require(PartParameterValue.Format(6.5f) == "6.5", "canonical ID in " + name);
                Require(!PartParameterValue.TryParse("NaN", out _) && !PartParameterValue.TryParse("Infinity", out _), "nonfinite dimension in " + name);
            }
        } finally { CultureInfo.CurrentCulture = culture; }
        string directory = Path.Combine(Path.GetTempPath(), "Protobot-build-qa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            string path = Path.Combine(directory, "project.pbb"), sibling = path + ".tmp";
            File.WriteAllText(sibling, "keep");
            Require(BuildSerialization.TrySerializeBuild(path, Build(Part(0))), "first save");
            Require(File.Exists(sibling) && File.ReadAllText(sibling) == "keep", "unrelated temporary sibling survives");
            byte[] saved = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Require(!BuildSerialization.TrySerializeBuild(path, Build(Part(1))), "locked overwrite fails");
            Require(saved.SequenceEqual(File.ReadAllBytes(path)), "locked overwrite preserves original");
            Require(BuildSerialization.TrySerializeBuild(path, Build(Part(2))), "normal overwrite succeeds");
            Require(BuildSerialization.TryDeserializeBuild(path, out var loaded) && loaded.parts[0].xPos == 2, "replacement reads correctly");
            Require(!Directory.GetFiles(directory, "*.pending-*").Any(), "no leftover owned save temporary files");
        } finally {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
        Debug.Log("BUILD_WORKFLOW_VERIFICATION passed checks=" + checks);
    }
}
