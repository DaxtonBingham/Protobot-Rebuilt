using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Protobot;
using Protobot.Builds;
using Protobot.Export;
using UnityEditor;
using UnityEngine;

public static class UpstreamCompatibilityVerification {
    [MenuItem("Tools/Verify Upstream Compatibility")]
    public static void Verify() {
        int checks = 0;
        void Require(bool condition, string message) {
            if (!condition) throw new Exception("Upstream compatibility: " + message);
            checks++;
        }
        PartsManager.LoadPartTypes();
        Require(PartsManager.partTypes.GroupBy(p => p.id).All(g => g.Count() == 1), "unique catalog identifiers");
        foreach (var type in PartsManager.partTypes.Where(p => p.id.StartsWith("OV", StringComparison.Ordinal)))
            Require(AssetDatabase.GetAssetPath(type).StartsWith("Assets/Resources/Part Prefabs/"), "Override uses upstream prefab: " + type.id);
        foreach (string oldId in new[] { "RAIL-2x25", "RAIL-2x35", "OVTO" }) {
            string current = PartCatalogCompatibility.CanonicalId(oldId);
            Require(PartsManager.TryValidateId(oldId, out _) && PartsManager.TryValidateId(current, out _), "both generations of " + oldId);
            Require(PartsManager.GetPartType(oldId) == PartsManager.GetPartType(current), "same generator for " + oldId);
            var a = new ObjectData { partId = oldId }; var b = new ObjectData { partId = current };
            Require(a.Equals(b) && a.GetHashCode() == b.GetHashCode(), "migration does not falsely dirty " + oldId);
            if (Application.isPlaying) {
                var part = PartsManager.GeneratePart(oldId, new Vector3(1,2,3), Quaternion.Euler(10,20,30));
                try { Require(part != null && part.GetComponent<SavedObject>().id == current, "canonical resave of " + oldId); }
                finally { if (part != null) UnityEngine.Object.DestroyImmediate(part); }
            }
        }
        Require(PartCatalogCompatibility.CanonicalId("RAIL-Rail-24") == "RAIL-Rail-24", "linear rail is not renamed");
        Require(!PartsManager.TryValidateId("RAIL-2x99", out _) && !PartsManager.TryValidateId("RAILS-Rail-24", out _), "unknown rails are not guessed");
        Require(!PartsManager.TryValidateId("OVTO-invalid", out _), "unknown toggle variant rejected");
        Require(StepExportController.IsStepPlatform(RuntimePlatform.WindowsPlayer, 8)
            && StepExportController.IsStepPlatform(RuntimePlatform.WindowsEditor, 8), "64-bit Windows STEP support");
        Require(!StepExportController.IsStepPlatform(RuntimePlatform.OSXPlayer, 8)
            && !StepExportController.IsStepPlatform(RuntimePlatform.LinuxPlayer, 8)
            && !StepExportController.IsStepPlatform(RuntimePlatform.WindowsPlayer, 4), "unsupported STEP platforms excluded");

        string output = Environment.GetEnvironmentVariable("PROTOBOT_COMPATIBILITY_OUTPUT");
        if (!string.IsNullOrEmpty(output)) Directory.CreateDirectory(Path.Combine(output, "catalog"));
        foreach (var type in PartsManager.partTypes.Where(p => p.id.StartsWith("OV", StringComparison.Ordinal) || p.id == "RAIL" || p.id == "RAILS")) {
            var generator = type.GetComponent<PartGenerator>();
            string oldFirst = generator.param1.value, oldSecond = generator.param2.value;
            try {
                var first = generator.UsesParams ? generator.GetParam1Options() : new List<string> { "" };
                foreach (string value in first) {
                    generator.param1.value = value;
                    generator.param2.value = generator.UsesTwoParams ? generator.param2.customDefault : "";
                    string id = generator.GetId();
                    Require(PartsManager.TryValidateId(id, out _), "prefab validates: " + id);
                    if (!Application.isPlaying) continue;
                    var part = PartsManager.GeneratePart(id, Vector3.zero, Quaternion.identity);
                    try {
                        Require(part != null && part.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0, "prefab geometry: " + id);
                        var snapshot = StepExportSnapshot.Capture(id, new[] { part.GetComponent<SavedObject>() });
                        Require(snapshot.parts.Count == 1 && snapshot.parts[0].surfaces.Count > 0, "export geometry: " + id);
                        if (!string.IsNullOrEmpty(output)) snapshot.Write(Path.Combine(output, "catalog", id + ".json"));
                    } finally { if (part != null) UnityEngine.Object.DestroyImmediate(part); }
                }
            } finally { generator.param1.value = oldFirst; generator.param2.value = oldSecond; }
        }
        if (Application.isPlaying) {
            var previous = SceneBuild.ToBuildData();
            string path = Path.Combine(Path.GetTempPath(), "Protobot-compatibility-" + Guid.NewGuid().ToString("N") + ".pbb");
            try {
                var ids = new[] { "RAIL-2x25", "RAIL-2x35", "RAIL-Rail-24", "OVTO", "OVTO-Red side", "OVTO-Blue side" };
                var build = new BuildData { version = null, camera = SceneBuild.DefaultBuild.camera,
                    parts = ids.Select((id, i) => new ObjectData { partId = id, instanceId = "compatibility-" + i, xPos = i * 30, yRot = i * 15 }).ToArray() };
                SceneBuild.GenerateBuild(build);
                var saved = SceneBuild.ToBuildData();
                Require(saved.parts.Length == ids.Length, "legacy project retains every part");
                for (int i = 0; i < ids.Length; i++) {
                    var part = saved.parts.Single(p => p.instanceId == "compatibility-" + i);
                    Require(part.partId == PartCatalogCompatibility.CanonicalId(ids[i]), "saved ID " + ids[i]);
                    Require(Mathf.Abs((float)part.xPos - i * 30) < .0001f && Quaternion.Angle(part.GetRot(), Quaternion.Euler(0, i * 15, 0)) < .01f, "saved placement " + ids[i]);
                }
                Require(BuildSerialization.TrySerializeBuild(path, saved), "write migrated PBB");
                Require(BuildSerialization.TryDeserializeBuild(path, out var loaded), "read migrated PBB");
                SceneBuild.GenerateBuild(loaded);
                Require(saved.CompareData(SceneBuild.ToBuildData()), "PBB round trip retains migrated parts and colors");
            } finally {
                SceneBuild.GenerateBuild(previous);
                if (File.Exists(path)) File.Delete(path);
            }
        }
        Debug.Log("UPSTREAM_COMPATIBILITY_VERIFICATION passed runtime=" + Application.isPlaying + " checks=" + checks);
    }
}
