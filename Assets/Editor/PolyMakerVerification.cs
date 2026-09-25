using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Protobot.CustomParts;
using UnityEditor;
using UnityEngine;

public static class PolyMakerVerification {
    [MenuItem("Tools/Verify Poly Maker")]
    public static void Verify() {
        string directory = Path.Combine(Path.GetTempPath(), "Protobot-Poly-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int checks = 0;
        void Require(bool result, string message) {
            if (!result) throw new Exception("Poly Maker: " + message);
            checks++;
        }
        try {
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0, -1 }) {
                var invalid = Plate(); invalid.thicknessInches = value;
                Require(!Compile(invalid).Valid, "invalid thickness " + value);
            }
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, 0, -1 }) {
                var invalid = Plate(); invalid.holes = new[] { new CustomHoleDefinition { size = new Vector2(value, .2f) } };
                Require(!Compile(invalid).Valid, "invalid hole size " + value);
            }
            var missing = Plate(); missing.sketch.outerLoop.anchors[1] = null;
            Require(!Compile(missing).Valid, "missing anchor must not change the outline");
            var nonfinite = Plate(); nonfinite.sketch.outerLoop.anchors[1].position = new Vector2(float.NaN, 1);
            Require(!Compile(nonfinite).Valid, "nonfinite anchor must not be dropped");
            var open = Plate(); open.sketch.outerLoop.closed = false;
            Require(!Compile(open).Valid, "open outline");
            var thin = Plate(); thin.thicknessInches = .0005f;
            Require(Math.Abs(Volume(Compile(thin)) - 24 * .0005) < 1e-7, "positive thickness must not be silently clamped");

            var source = Plate();
            source.sketch.cutoutLoops = new[] { Loop(new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f), new Vector2(.5f, .5f), new Vector2(-.5f, .5f)) };
            source.holes = new[] {
                new CustomHoleDefinition { position = new Vector2(-1.5f, 0), size = new Vector2(.8f, .4f), rotationDegrees = 37, shape = CustomHoleShape.Square },
                new CustomHoleDefinition { position = new Vector2(1.5f, 0), size = new Vector2(.9f, .4f), rotationDegrees = 53 }
            };
            var expected = Compile(source);
            foreach (string extension in new[] { "json", "svg", "dxf" }) {
                string path = Path.Combine(directory, "part." + extension);
                CustomPartDefinition actual;
                bool written, read;
                if (extension == "json") {
                    written = CustomPartImportExport.ExportJson(source, path);
                    read = CustomPartImportExport.ImportJson(path, out actual);
                } else if (extension == "svg") {
                    written = CustomPartImportExport.ExportSvg(source, path);
                    read = CustomPartImportExport.ImportSvg(path, out actual);
                } else {
                    written = CustomPartImportExport.ExportDxf(source, path);
                    read = CustomPartImportExport.ImportDxf(path, out actual);
                }
                Require(written && read, extension + " roundtrip: " + CustomPartImportExport.LastError);
                var mesh = Compile(actual);
                Require(mesh.Valid && Math.Abs(Volume(mesh) - Volume(expected)) < .00001, extension + " volume");
                bool same = true;
                for (float x = -3.07f; x < 3.1f; x += .067f)
                    for (float y = -2.03f; y < 2.1f; y += .073f)
                        same &= Solid(expected, new Vector2(x, y)) == Solid(mesh, new Vector2(x, y));
                Require(same, extension + " rotated holes and cutouts");
                if (extension == "dxf") Require(File.ReadAllText(path).Contains("ELLIPSE"), "DXF preserves analytic elliptical holes");
                if (extension == "svg") Require(File.ReadAllText(path).Contains("A "), "SVG preserves analytic elliptical arcs");
            }
            foreach (float angle in new[] { 1f, 17, 89, 123, 179, 359 }) {
                foreach (var size in new[] { new Vector2(.182f, .182f), new Vector2(.9f, .02f), new Vector2(.02f, .9f), new Vector2(.7f, .4f) }) {
                    var d = Plate();
                    d.holes = new[] { new CustomHoleDefinition { position = new Vector2(1.37f, -.51f), size = size, rotationDegrees = angle } };
                    double volume = Volume(Compile(d));
                    string path = Path.Combine(directory, "round.svg");
                    Require(CustomPartImportExport.ExportSvg(d, path) && CustomPartImportExport.ImportSvg(path, out var fromSvg)
                        && Math.Abs(Volume(Compile(fromSvg)) - volume) < .00001, "SVG narrow/rotated ellipse " + angle + " " + size);
                    path = Path.Combine(directory, "round.dxf");
                    Require(CustomPartImportExport.ExportDxf(d, path) && CustomPartImportExport.ImportDxf(path, out var fromDxf)
                        && Math.Abs(Volume(Compile(fromDxf)) - volume) < .00001, "DXF narrow/rotated ellipse " + angle + " " + size);
                    if (size.x == size.y) Require(File.ReadAllText(path).Contains("CIRCLE"), "round DXF holes stay circles");
                }
            }
            string badPath = Path.Combine(directory, "bad.json");
            File.WriteAllText(badPath, "{garbage");
            Require(!CustomPartImportExport.ImportJson(badPath, out _), "malformed JSON returns a failure");
            File.WriteAllText(badPath, "{}");
            Require(!CustomPartImportExport.ImportJson(badPath, out _), "unrelated JSON returns a failure");
            string svg = Path.Combine(directory, "units.svg");
            File.WriteAllText(svg, "<svg width='50.8mm' height='25.4mm' viewBox='0 0 50.8 25.4'><g transform='translate(12.7 6.35) scale(2)'><path d='M0 0 h12.7 v6.35 h-12.7 z'/></g></svg>");
            Require(CustomPartImportExport.ImportSvg(svg, out var scaled) && Math.Abs(Volume(Compile(scaled)) - .0625) < .00001, "SVG units, transforms, relative commands");
            File.WriteAllText(svg, "<svg><use href='#part'/></svg>");
            Require(!CustomPartImportExport.ImportSvg(svg, out _), "unsupported SVG references must not be omitted silently");
            File.WriteAllText(svg, "<svg><path d='M-1 0 A1 1 0 0 1 1 0 A1 1 0 0 1 -1 0 Z'/></svg>");
            Require(CustomPartImportExport.ImportSvg(svg, out var round) && Math.Abs(Volume(Compile(round)) - 12 * Math.Sin(Math.PI / 12) * .125) < .00001, "SVG circular arcs follow their actual curve");
            File.WriteAllText(svg, "previous output");
            open.thicknessInches = float.NaN;
            Require(!CustomPartImportExport.ExportSvg(open, svg) && File.ReadAllText(svg) == "previous output", "failed SVG preserves destination");
            Require(!CustomPartImportExport.ExportDxf(open, svg) && File.ReadAllText(svg) == "previous output", "failed DXF preserves destination");
            Require(Directory.GetFiles(directory, "*.tmp").Length == 0, "temporary outputs cleaned up");

            var tangent = Plate(); tangent.holes = new[] { new CustomHoleDefinition { shape = CustomHoleShape.Square, size = Vector2.one, position = new Vector2(2.5f, 0) } };
            Require(!Compile(tangent).Valid, "hole touching the outline");
            tangent.holes[0].position = new Vector2(2.49f, 0);
            Require(Compile(tangent).Valid, "hole near but not touching the outline");
            tangent.holes = new[] { new CustomHoleDefinition { position = Vector2.zero, size = Vector2.one }, new CustomHoleDefinition { position = new Vector2(.4f, 0), size = Vector2.one } };
            Require(!Compile(tangent).Valid, "overlapping contours after broad-phase culling");
            Debug.Log("POLY_MAKER_VERIFICATION passed checks=" + checks);
        } finally {
            // Only this verification's newly created, unique directory is removed.
            foreach (string path in Directory.GetFiles(directory)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static CustomPartDefinition Plate() {
        var d = CustomPartDefinition.CreateDefault();
        d.sketch.outerLoop = Loop(new Vector2(-3, -2), new Vector2(3, -2), new Vector2(3, 2), new Vector2(-3, 2));
        return d;
    }
    private static LoopData Loop(params Vector2[] points) => new LoopData {
        closed = true, anchors = points.Select(p => new AnchorData { position = p }).ToArray(), segmentKinds = new SegmentKind[points.Length]
    };
    private static CustomPartMeshBuilder.GeometryData Compile(CustomPartDefinition d) => CustomPartMeshBuilder.Compile(d, CustomPartMeshBuilder.GeometryKey(d));
    private static double Volume(CustomPartMeshBuilder.GeometryData mesh) {
        double result = 0;
        for (int i = 0; i < mesh.Triangles.Length; i += 3)
            result += Vector3.Dot(mesh.Vertices[mesh.Triangles[i]], Vector3.Cross(mesh.Vertices[mesh.Triangles[i + 1]], mesh.Vertices[mesh.Triangles[i + 2]])) / 6.0;
        return result;
    }
    private static bool Solid(CustomPartMeshBuilder.GeometryData mesh, Vector2 point) {
        for (int i = 0; i < mesh.Triangles.Length; i += 3) {
            var a = mesh.Vertices[mesh.Triangles[i]]; var b = mesh.Vertices[mesh.Triangles[i + 1]]; var c = mesh.Vertices[mesh.Triangles[i + 2]];
            if (a.z <= 0 || b.z != a.z || c.z != a.z) continue;
            float ab = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
            float bc = (c.x - b.x) * (point.y - b.y) - (c.y - b.y) * (point.x - b.x);
            float ca = (a.x - c.x) * (point.y - c.y) - (a.y - c.y) * (point.x - c.x);
            if (ab >= -1e-7 && bc >= -1e-7 && ca >= -1e-7) return true;
        }
        return false;
    }
}
