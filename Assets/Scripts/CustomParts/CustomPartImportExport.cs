using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Protobot.CustomParts {
    public static class CustomPartImportExport {
        public static bool ExportJson(CustomPartDefinition definition, string filePath) {
            if (definition == null || string.IsNullOrWhiteSpace(filePath)) return false;
            string json = JsonUtility.ToJson(definition, true);
            File.WriteAllText(filePath, json);
            return true;
        }

        public static bool ImportJson(string filePath, out CustomPartDefinition definition) {
            definition = null;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;

            string json = File.ReadAllText(filePath);
            definition = JsonUtility.FromJson<CustomPartDefinition>(json);
            if (definition == null) return false;

            if (string.IsNullOrWhiteSpace(definition.definitionId)) {
                definition.definitionId = Guid.NewGuid().ToString("N");
            }

            definition.Touch();
            return true;
        }

        public static bool ExportSvg(CustomPartDefinition definition, string filePath) {
            if (definition?.sketch?.outerLoop == null || string.IsNullOrWhiteSpace(filePath)) return false;
            List<Vector2> outer = FlattenLoop(definition.sketch.outerLoop);
            if (outer.Count < 3) return false;

            var allLoops = new List<(List<Vector2> points, bool cutout)> {
                (outer, false)
            };
            if (definition.sketch.cutoutLoops != null) {
                foreach (LoopData loop in definition.sketch.cutoutLoops) {
                    List<Vector2> points = FlattenLoop(loop);
                    if (points.Count >= 3) {
                        allLoops.Add((points, true));
                    }
                }
            }

            (Vector2 min, Vector2 max) = GetBounds(allLoops.SelectMany(loop => loop.points));
            Vector2 size = max - min;
            if (size.x <= 0) size.x = 1;
            if (size.y <= 0) size.y = 1;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{ToInv(min.x)} {ToInv(-max.y)} {ToInv(size.x)} {ToInv(size.y)}\">");
            sb.AppendLine("  <g fill-rule=\"evenodd\" fill=\"#d0d0d0\" stroke=\"#202020\" stroke-width=\"0.01\">");

            // Outer path.
            sb.Append("    <path d=\"");
            AppendPath(sb, outer, true);
            // Cutouts.
            foreach ((List<Vector2> points, bool cutout) in allLoops.Where(loop => loop.cutout)) {
                AppendPath(sb, points, true);
            }

            sb.AppendLine("\" />");

            // Holes.
            if (definition.holes != null) {
                foreach (CustomHoleDefinition hole in definition.holes) {
                    if (hole == null) continue;
                    switch (hole.shape) {
                    case CustomHoleShape.Square:
                        sb.AppendLine(
                            $"    <rect x=\"{ToInv(hole.position.x - hole.size.x * 0.5f)}\" y=\"{ToInv(-(hole.position.y + hole.size.y * 0.5f))}\" width=\"{ToInv(hole.size.x)}\" height=\"{ToInv(hole.size.y)}\" fill=\"none\" />");
                        break;
                    default:
                        sb.AppendLine(
                            $"    <ellipse cx=\"{ToInv(hole.position.x)}\" cy=\"{ToInv(-hole.position.y)}\" rx=\"{ToInv(hole.size.x * 0.5f)}\" ry=\"{ToInv(hole.size.y * 0.5f)}\" fill=\"none\" />");
                        break;
                    }
                }
            }

            sb.AppendLine("  </g>");
            sb.AppendLine("</svg>");

            File.WriteAllText(filePath, sb.ToString());
            return true;
        }

        public static bool ImportSvg(string filePath, out CustomPartDefinition definition) {
            definition = null;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;

            string svg = File.ReadAllText(filePath);

            // Parse polygon points first (most deterministic).
            Match polygonMatch = Regex.Match(svg, "points\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (polygonMatch.Success) {
                List<Vector2> points = ParsePointsList(polygonMatch.Groups[1].Value);
                if (points.Count >= 3) {
                    definition = BuildDefinitionFromOutline(points, Path.GetFileNameWithoutExtension(filePath));
                    return true;
                }
            }

            // Fallback: parse first path commands containing M/L.
            Match pathMatch = Regex.Match(svg, "d\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (pathMatch.Success) {
                List<Vector2> points = ParseSimplePath(pathMatch.Groups[1].Value);
                if (points.Count >= 3) {
                    definition = BuildDefinitionFromOutline(points, Path.GetFileNameWithoutExtension(filePath));
                    return true;
                }
            }

            return false;
        }

        public static bool ExportDxf(CustomPartDefinition definition, string filePath) {
            if (definition?.sketch?.outerLoop == null || string.IsNullOrWhiteSpace(filePath)) return false;
            List<Vector2> outer = FlattenLoop(definition.sketch.outerLoop);
            if (outer.Count < 3) return false;

            var cutouts = new List<List<Vector2>>();
            if (definition.sketch.cutoutLoops != null) {
                foreach (LoopData loop in definition.sketch.cutoutLoops) {
                    List<Vector2> points = FlattenLoop(loop);
                    if (points.Count >= 3) {
                        cutouts.Add(points);
                    }
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("0");
            sb.AppendLine("SECTION");
            sb.AppendLine("2");
            sb.AppendLine("ENTITIES");

            AppendDxfPolyline(sb, outer, "OUTLINE");
            foreach (List<Vector2> cutout in cutouts) {
                AppendDxfPolyline(sb, cutout, "CUTOUT");
            }

            if (definition.holes != null) {
                foreach (CustomHoleDefinition hole in definition.holes) {
                    if (hole == null) continue;
                    sb.AppendLine("0");
                    sb.AppendLine("CIRCLE");
                    sb.AppendLine("8");
                    sb.AppendLine("HOLES");
                    sb.AppendLine("10");
                    sb.AppendLine(ToInv(hole.position.x));
                    sb.AppendLine("20");
                    sb.AppendLine(ToInv(hole.position.y));
                    sb.AppendLine("30");
                    sb.AppendLine("0");
                    sb.AppendLine("40");
                    sb.AppendLine(ToInv(Mathf.Max(hole.size.x, hole.size.y) * 0.5f));
                }
            }

            sb.AppendLine("0");
            sb.AppendLine("ENDSEC");
            sb.AppendLine("0");
            sb.AppendLine("EOF");

            File.WriteAllText(filePath, sb.ToString());
            return true;
        }

        public static bool ImportDxf(string filePath, out CustomPartDefinition definition) {
            definition = null;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length < 4) return false;

            // Very small parser for first LWPOLYLINE or POLYLINE using 10/20 pairs.
            List<Vector2> points = new List<Vector2>();
            for (int i = 0; i < lines.Length - 1; i++) {
                string code = lines[i].Trim();
                string value = lines[i + 1].Trim();
                if (code == "10" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) {
                    for (int j = i + 2; j < lines.Length - 1; j++) {
                        if (lines[j].Trim() == "20"
                            && float.TryParse(lines[j + 1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) {
                            points.Add(new Vector2(x, y));
                            i = j + 1;
                            break;
                        }
                    }
                }
            }

            // Deduplicate adjacent and ensure closed loop shape.
            points = points.Distinct().ToList();
            if (points.Count < 3) return false;

            definition = BuildDefinitionFromOutline(points, Path.GetFileNameWithoutExtension(filePath));
            return true;
        }

        private static void AppendPath(StringBuilder sb, IList<Vector2> points, bool close) {
            if (points == null || points.Count == 0) return;
            sb.Append($"M {ToInv(points[0].x)} {ToInv(-points[0].y)} ");
            for (int i = 1; i < points.Count; i++) {
                sb.Append($"L {ToInv(points[i].x)} {ToInv(-points[i].y)} ");
            }

            if (close) sb.Append("Z ");
        }

        private static void AppendDxfPolyline(StringBuilder sb, IList<Vector2> points, string layer) {
            sb.AppendLine("0");
            sb.AppendLine("LWPOLYLINE");
            sb.AppendLine("8");
            sb.AppendLine(layer);
            sb.AppendLine("90");
            sb.AppendLine(points.Count.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("70");
            sb.AppendLine("1"); // closed

            foreach (Vector2 point in points) {
                sb.AppendLine("10");
                sb.AppendLine(ToInv(point.x));
                sb.AppendLine("20");
                sb.AppendLine(ToInv(point.y));
            }
        }

        private static CustomPartDefinition BuildDefinitionFromOutline(IList<Vector2> points, string name) {
            AnchorData[] anchors = points.Select(point => new AnchorData {
                position = point,
                inHandle = Vector2.zero,
                outHandle = Vector2.zero,
                handleMode = HandleMode.Mirrored
            }).ToArray();

            SegmentKind[] segments = Enumerable.Repeat(SegmentKind.Line, anchors.Length).ToArray();
            CustomPartDefinition definition = CustomPartDefinition.CreateDefault();
            definition.definitionId = Guid.NewGuid().ToString("N");
            definition.name = string.IsNullOrWhiteSpace(name) ? "Imported Custom Part" : name;
            definition.sketch.outerLoop.anchors = anchors;
            definition.sketch.outerLoop.segmentKinds = segments;
            definition.sketch.cutoutLoops = Array.Empty<LoopData>();
            definition.holes = Array.Empty<CustomHoleDefinition>();
            definition.patterns = Array.Empty<PatternFeature>();
            definition.thicknessInches = CustomPartDefinition.DefaultThicknessInches;
            definition.metadata.appVersion = AppData.Version;
            definition.Touch();
            return definition;
        }

        private static List<Vector2> FlattenLoop(LoopData loop) {
            var points = new List<Vector2>();
            if (loop?.anchors == null || loop.anchors.Length < 2) return points;
            SegmentKind[] segmentKinds = loop.segmentKinds;
            if (segmentKinds == null || segmentKinds.Length != loop.anchors.Length) {
                segmentKinds = Enumerable.Repeat(SegmentKind.Line, loop.anchors.Length).ToArray();
            }

            for (int i = 0; i < loop.anchors.Length; i++) {
                int next = (i + 1) % loop.anchors.Length;
                AnchorData start = loop.anchors[i];
                AnchorData end = loop.anchors[next];
                if (segmentKinds[i] == SegmentKind.Bezier) {
                    float approximate = Vector2.Distance(start.position, start.position + start.outHandle)
                        + Vector2.Distance(start.position + start.outHandle, end.position + end.inHandle)
                        + Vector2.Distance(end.position + end.inHandle, end.position);
                    int subdivisions = Mathf.Clamp(Mathf.CeilToInt(approximate / 0.1f), 6, 64);
                    for (int s = 0; s < subdivisions; s++) {
                        float t = s / (float)subdivisions;
                        Vector2 p = EvaluateBezier(
                            start.position,
                            start.position + start.outHandle,
                            end.position + end.inHandle,
                            end.position,
                            t);
                        AppendPoint(points, p);
                    }
                }
                else {
                    AppendPoint(points, start.position);
                }
            }

            if (points.Count > 2 && Vector2.Distance(points[0], points[points.Count - 1]) < 0.0001f) {
                points.RemoveAt(points.Count - 1);
            }

            return points;
        }

        private static Vector2 EvaluateBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) {
            float omt = 1f - t;
            return omt * omt * omt * p0
                + 3f * omt * omt * t * p1
                + 3f * omt * t * t * p2
                + t * t * t * p3;
        }

        private static void AppendPoint(List<Vector2> points, Vector2 point) {
            if (points.Count == 0 || Vector2.Distance(points[points.Count - 1], point) > 0.0001f) {
                points.Add(point);
            }
        }

        private static (Vector2 min, Vector2 max) GetBounds(IEnumerable<Vector2> points) {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector2 point in points) {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            if (min.x == float.MaxValue) min = Vector2.zero;
            if (max.x == float.MinValue) max = Vector2.one;
            return (min, max);
        }

        private static List<Vector2> ParsePointsList(string pointsString) {
            var points = new List<Vector2>();
            string[] pairs = pointsString.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string pair in pairs) {
                string[] values = pair.Split(',');
                if (values.Length < 2) continue;
                if (float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) {
                    points.Add(new Vector2(x, -y));
                }
            }

            return points;
        }

        private static List<Vector2> ParseSimplePath(string pathData) {
            var points = new List<Vector2>();
            MatchCollection matches = Regex.Matches(pathData, @"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?");
            for (int i = 0; i + 1 < matches.Count; i += 2) {
                if (float.TryParse(matches[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(matches[i + 1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) {
                    points.Add(new Vector2(x, -y));
                }
            }

            return points;
        }

        private static string ToInv(float value) {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
