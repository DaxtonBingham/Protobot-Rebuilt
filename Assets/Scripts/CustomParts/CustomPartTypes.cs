using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Protobot.CustomParts {
    public enum CustomPartUnitSystem {
        Inches = 0,
        Millimeters = 1
    }

    public enum HandleMode {
        Mirrored = 0,
        Aligned = 1,
        Free = 2
    }

    public enum SegmentKind {
        Line = 0,
        Bezier = 1
    }

    public enum CustomHoleShape {
        Circle = 0,
        Square = 1,
        Slot = 2
    }

    public enum CustomPatternKind {
        Linear = 0,
        Grid = 1,
        Radial = 2,
        AlongCurve = 3
    }

    [Serializable]
    public class AnchorData {
        [OptionalField] public string id = Guid.NewGuid().ToString("N");
        [OptionalField] public Vector2 position;
        [OptionalField] public Vector2 inHandle;
        [OptionalField] public Vector2 outHandle;
        [OptionalField] public HandleMode handleMode = HandleMode.Mirrored;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            if (string.IsNullOrWhiteSpace(id)) {
                id = Guid.NewGuid().ToString("N");
            }
        }
    }

    [Serializable]
    public class LoopData {
        [OptionalField] public string id = Guid.NewGuid().ToString("N");
        [OptionalField] public string name = "Loop";
        [OptionalField] public bool closed = true;
        [OptionalField] public bool isCutout;
        [OptionalField] public AnchorData[] anchors = Array.Empty<AnchorData>();
        [OptionalField] public SegmentKind[] segmentKinds = Array.Empty<SegmentKind>();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            if (string.IsNullOrWhiteSpace(id)) {
                id = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(name)) {
                name = isCutout ? "Cutout" : "Outline";
            }

            anchors ??= Array.Empty<AnchorData>();
            segmentKinds ??= Array.Empty<SegmentKind>();
            if (segmentKinds.Length != anchors.Length) {
                var kinds = new SegmentKind[anchors.Length];
                for (int i = 0; i < kinds.Length; i++) {
                    kinds[i] = i < segmentKinds.Length ? segmentKinds[i] : SegmentKind.Line;
                }

                segmentKinds = kinds;
            }
        }
    }

    [Serializable]
    public class ConstraintData {
        [OptionalField] public string type;
        [OptionalField] public string a;
        [OptionalField] public string b;
        [OptionalField] public float value;
    }

    [Serializable]
    public class SketchData {
        [OptionalField] public LoopData outerLoop = new LoopData();
        [OptionalField] public LoopData[] cutoutLoops = Array.Empty<LoopData>();
        [OptionalField] public ConstraintData[] constraints = Array.Empty<ConstraintData>();
        [OptionalField] public CustomPartUnitSystem unitSystem = CustomPartUnitSystem.Inches;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            outerLoop ??= new LoopData {
                name = "Outline",
                closed = true,
                isCutout = false
            };
            cutoutLoops ??= Array.Empty<LoopData>();
            constraints ??= Array.Empty<ConstraintData>();
        }
    }

    [Serializable]
    public class CustomHoleDefinition {
        [OptionalField] public string id = Guid.NewGuid().ToString("N");
        [OptionalField] public CustomHoleShape shape = CustomHoleShape.Circle;
        [OptionalField] public Vector2 position;
        [OptionalField] public Vector2 size = new Vector2(0.182f, 0.182f);
        [OptionalField] public float depthInches = 0.125f;
        [OptionalField] public float rotationDegrees;
        [OptionalField] public HoleCollider.HoleType holeType = HoleCollider.HoleType.Normal;
        [OptionalField] public bool twoSided = true;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            if (string.IsNullOrWhiteSpace(id)) {
                id = Guid.NewGuid().ToString("N");
            }

            shape = shape == CustomHoleShape.Square ? CustomHoleShape.Square : CustomHoleShape.Circle;
            holeType = HoleCollider.HoleType.Normal;
            twoSided = true;

            if (size.x <= 0f || size.y <= 0f) {
                size = new Vector2(0.182f, 0.182f);
            }

            if (depthInches <= 0f) {
                depthInches = 0.125f;
            }
        }
    }

    [Serializable]
    public class PatternTransform2D {
        [OptionalField] public Vector2 origin;
        [OptionalField] public float rotationDegrees;
    }

    [Serializable]
    public class PatternParams {
        [OptionalField] public int countX = 1;
        [OptionalField] public int countY = 1;
        [OptionalField] public int countRadial = 1;
        [OptionalField] public float spacingX = 0.5f;
        [OptionalField] public float spacingY = 0.5f;
        [OptionalField] public float radius = 1f;
    }

    [Serializable]
    public class PatternFeature {
        [OptionalField] public string id = Guid.NewGuid().ToString("N");
        [OptionalField] public CustomPatternKind kind = CustomPatternKind.Grid;
        [OptionalField] public PatternTransform2D transform = new PatternTransform2D();
        [OptionalField] public PatternParams parameters = new PatternParams();
        [OptionalField] public string sourceHoleId;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            if (string.IsNullOrWhiteSpace(id)) {
                id = Guid.NewGuid().ToString("N");
            }

            transform ??= new PatternTransform2D();
            parameters ??= new PatternParams();
        }
    }

    [Serializable]
    public class DefinitionMetadata {
        [OptionalField] public string createdAtUtc = DateTime.UtcNow.ToString("o");
        [OptionalField] public string modifiedAtUtc = DateTime.UtcNow.ToString("o");
        [OptionalField] public string appVersion;
        [OptionalField] public string notes = string.Empty;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            if (string.IsNullOrWhiteSpace(createdAtUtc)) {
                createdAtUtc = DateTime.UtcNow.ToString("o");
            }

            if (string.IsNullOrWhiteSpace(modifiedAtUtc)) {
                modifiedAtUtc = createdAtUtc;
            }

            notes ??= string.Empty;
        }
    }

    [Serializable]
    public class CustomPartDefinition {
        public const float DefaultThicknessInches = 0.125f;
        public const string CustomPartIdPrefix = "CPLY";

        [OptionalField] public string definitionId = Guid.NewGuid().ToString("N");
        [OptionalField] public string name = "Custom Part";
        [OptionalField] public float thicknessInches = DefaultThicknessInches;
        [OptionalField] public string materialName = "Custom Part Material";

        [OptionalField] public SketchData sketch = new SketchData();
        [OptionalField] public CustomHoleDefinition[] holes = Array.Empty<CustomHoleDefinition>();
        [OptionalField] public PatternFeature[] patterns = Array.Empty<PatternFeature>();
        [OptionalField] public DefinitionMetadata metadata = new DefinitionMetadata();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) {
            if (string.IsNullOrWhiteSpace(definitionId)) {
                definitionId = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(name)) {
                name = "Custom Part";
            }

            if (thicknessInches <= 0f) {
                thicknessInches = DefaultThicknessInches;
            }

            materialName ??= "Custom Part Material";
            sketch ??= new SketchData();
            holes ??= Array.Empty<CustomHoleDefinition>();
            patterns ??= Array.Empty<PatternFeature>();
            metadata ??= new DefinitionMetadata();
        }

        public CustomPartDefinition CloneDeep() {
            string json = JsonUtility.ToJson(this);
            return JsonUtility.FromJson<CustomPartDefinition>(json);
        }

        public void Touch() {
            if (metadata == null) metadata = new DefinitionMetadata();
            metadata.modifiedAtUtc = DateTime.UtcNow.ToString("o");
        }

        public string GetDeterministicHash() {
            string json = JsonUtility.ToJson(this);
            using MD5 md5 = MD5.Create();
            byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(json));
            StringBuilder sb = new StringBuilder(hashBytes.Length * 2);
            foreach (byte b in hashBytes) {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        public string GetPartId() {
            return $"{CustomPartIdPrefix}-{definitionId}";
        }

        public static CustomPartDefinition CreateDefault() {
            AnchorData[] anchors = new[] {
                new AnchorData { position = new Vector2(-1f, -0.5f) },
                new AnchorData { position = new Vector2(1f, -0.5f) },
                new AnchorData { position = new Vector2(1f, 0.5f) },
                new AnchorData { position = new Vector2(-1f, 0.5f) },
            };

            SegmentKind[] segmentKinds = new[] {
                SegmentKind.Line,
                SegmentKind.Line,
                SegmentKind.Line,
                SegmentKind.Line
            };

            return new CustomPartDefinition {
                name = "Custom Plate",
                thicknessInches = DefaultThicknessInches,
                sketch = new SketchData {
                    unitSystem = CustomPartUnitSystem.Inches,
                    outerLoop = new LoopData {
                        name = "Outline",
                        closed = true,
                        isCutout = false,
                        anchors = anchors,
                        segmentKinds = segmentKinds
                    },
                    cutoutLoops = Array.Empty<LoopData>(),
                    constraints = Array.Empty<ConstraintData>()
                },
                holes = Array.Empty<CustomHoleDefinition>(),
                patterns = Array.Empty<PatternFeature>(),
                metadata = new DefinitionMetadata {
                    appVersion = AppData.Version
                }
            };
        }
    }

    public static class CustomPartDefinitionUtility {
        public static bool SequenceEquivalent(IReadOnlyList<CustomPartDefinition> a, IReadOnlyList<CustomPartDefinition> b) {
            int aCount = a == null ? 0 : a.Count;
            int bCount = b == null ? 0 : b.Count;
            if (aCount != bCount) return false;
            if (aCount == 0) return true;

            var map = new Dictionary<string, string>(aCount, StringComparer.Ordinal);
            for (int i = 0; i < aCount; i++) {
                CustomPartDefinition def = a[i];
                if (def == null) continue;
                map[def.definitionId] = def.GetDeterministicHash();
            }

            for (int i = 0; i < bCount; i++) {
                CustomPartDefinition def = b[i];
                if (def == null) continue;
                if (!map.TryGetValue(def.definitionId, out string hash)) return false;
                if (!string.Equals(hash, def.GetDeterministicHash(), StringComparison.Ordinal)) return false;
            }

            return true;
        }
    }
}
