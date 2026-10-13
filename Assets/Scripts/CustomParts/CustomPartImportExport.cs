using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;

namespace Protobot.CustomParts {
    public static class CustomPartImportExport {
        public static string LastError { get; private set; } = string.Empty;
        private const string NumberPattern = @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?";

        public static bool ExportJson(CustomPartDefinition definition, string filePath) =>
            Write(filePath, () => {
                if (definition == null) throw new FormatException("There is no design to export.");
                return JsonUtility.ToJson(definition, true);
            });

        public static bool ImportJson(string filePath, out CustomPartDefinition definition) =>
            Read(filePath, text => {
                var result = JsonUtility.FromJson<CustomPartDefinition>(text);
                if (!CustomPartMeshBuilder.HasValidInputs(result))
                    throw new FormatException("This JSON does not contain a complete, finite Poly Maker design.");
                if (string.IsNullOrWhiteSpace(result.definitionId)) result.definitionId = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(result.name)) result.name = Path.GetFileNameWithoutExtension(filePath);
                result.Touch();
                return result;
            }, out definition);

        public static bool ExportSvg(CustomPartDefinition definition, string filePath) => Write(filePath, () => {
            var contours = Contours(definition);
            Vector2 min = contours[0].Aggregate(Vector2.Min), max = contours[0].Aggregate(Vector2.Max), size = max - min;
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Inv(size.x)}in\" height=\"{Inv(size.y)}in\" viewBox=\"{Inv(min.x)} {Inv(-max.y)} {Inv(size.x)} {Inv(size.y)}\">");
            sb.Append("  <path fill-rule=\"evenodd\" fill=\"#d0d0d0\" stroke=\"#202020\" stroke-width=\"0.01\" d=\"");
            int sketchLoops = 1 + (definition.sketch.cutoutLoops?.Length ?? 0);
            for (int contourIndex = 0; contourIndex < contours.Count; contourIndex++) {
                var hole = contourIndex >= sketchLoops ? definition.holes[contourIndex - sketchLoops] : null;
                if (hole != null && hole.shape != CustomHoleShape.Square) {
                    float radians = hole.rotationDegrees * Mathf.Deg2Rad;
                    Vector2 offset = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (hole.size.x * .5f);
                    Vector2 start = hole.position + offset, opposite = hole.position - offset;
                    string arc = $"A {Inv(hole.size.x * .5f)} {Inv(hole.size.y * .5f)} {Inv(-hole.rotationDegrees)} 0 0 ";
                    sb.Append($"M {Inv(start.x)} {Inv(-start.y)} {arc}{Inv(opposite.x)} {Inv(-opposite.y)} {arc}{Inv(start.x)} {Inv(-start.y)} Z ");
                    continue;
                }
                var loop = contours[contourIndex];
                for (int i = 0; i < loop.Count; i++)
                    sb.Append($"{(i == 0 ? "M" : "L")} {Inv(loop[i].x)} {Inv(-loop[i].y)} ");
                sb.Append("Z ");
            }
            sb.AppendLine("\" />");
            sb.AppendLine("</svg>");
            return sb.ToString();
        });

        public static bool ImportSvg(string filePath, out CustomPartDefinition definition) => Read(filePath, text => {
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Ignore, XmlResolver = null
            })) doc.Load(reader);
            var root = doc.DocumentElement;
            if (root == null || root.LocalName != "svg") throw new FormatException("Expected an SVG drawing.");
            var contours = new List<List<Vector2>>();
            ReadSvg(root, SvgUnits(root), contours, true);
            // SVG's Y axis points down; Protobot's sketch Y axis points up.
            foreach (var loop in contours) for (int i = 0; i < loop.Count; i++) loop[i] = new Vector2(loop[i].x, -loop[i].y);
            return FromContours(contours, Path.GetFileNameWithoutExtension(filePath));
        }, out definition);

        public static bool ExportDxf(CustomPartDefinition definition, string filePath) => Write(filePath, () => {
            var contours = Contours(definition);
            var sb = new StringBuilder();
            sb.AppendLine("0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1015\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES");
            for (int i = 0; i < contours.Count; i++) {
                int holeIndex = i - 1 - (definition.sketch.cutoutLoops?.Length ?? 0);
                var hole = holeIndex >= 0 ? definition.holes[holeIndex] : null;
                if (hole != null && hole.shape != CustomHoleShape.Square) {
                    bool circle = hole.size.x == hole.size.y;
                    sb.AppendLine("0\n" + (circle ? "CIRCLE" : "ELLIPSE") + "\n100\nAcDbEntity\n8\nHOLES\n100\n" + (circle ? "AcDbCircle" : "AcDbEllipse"));
                    sb.AppendLine("10\n" + Inv(hole.position.x) + "\n20\n" + Inv(hole.position.y) + "\n30\n0");
                    if (circle) sb.AppendLine("40\n" + Inv(hole.size.x * .5f));
                    else {
                        float angle = (hole.rotationDegrees + (hole.size.y > hole.size.x ? 90 : 0)) * Mathf.Deg2Rad;
                        float major = Mathf.Max(hole.size.x, hole.size.y) * .5f;
                        sb.AppendLine("11\n" + Inv(Mathf.Cos(angle) * major) + "\n21\n" + Inv(Mathf.Sin(angle) * major) + "\n31\n0\n40\n" + Inv(Mathf.Min(hole.size.x, hole.size.y) / (major * 2)) + "\n41\n0\n42\n" + Inv(Mathf.PI * 2));
                    }
                    continue;
                }
                var loop = contours[i];
                sb.AppendLine("0\nLWPOLYLINE\n100\nAcDbEntity\n8\n" + (i == 0 ? "OUTLINE" : "CUTOUT") + "\n100\nAcDbPolyline\n90\n" + loop.Count + "\n70\n1");
                foreach (var p in loop) sb.AppendLine("10\n" + Inv(p.x) + "\n20\n" + Inv(p.y));
            }
            sb.AppendLine("0\nENDSEC\n0\nEOF");
            return sb.ToString();
        });

        public static bool ImportDxf(string filePath, out CustomPartDefinition definition) => Read(filePath, text => {
            var lines = text.Replace("\r", "").Split('\n').ToList();
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1])) lines.RemoveAt(lines.Count - 1);
            if (lines.Count % 2 != 0) throw new FormatException("DXF contains an incomplete group-code pair.");
            var pairs = new List<KeyValuePair<int, string>>();
            for (int i = 0; i < lines.Count; i += 2) {
                if (!int.TryParse(lines[i].Trim(), out int code)) throw new FormatException("Invalid DXF group code.");
                pairs.Add(new KeyValuePair<int, string>(code, lines[i + 1].Trim()));
            }
            float scale = 1;
            for (int i = 0; i + 1 < pairs.Count; i++) if (pairs[i].Key == 9 && pairs[i].Value == "$INSUNITS") {
                int unit = int.Parse(pairs[i + 1].Value, CultureInfo.InvariantCulture);
                switch (unit) {
                    case 0: case 1: scale = 1; break; // Legacy unitless Protobot drawings use inches.
                    case 2: scale = 12; break;
                    case 4: scale = 1 / 25.4f; break;
                    case 5: scale = 1 / 2.54f; break;
                    case 6: scale = 1000 / 25.4f; break;
                    default: throw new FormatException("DXF units are unsupported. Export in inches or millimeters.");
                }
            }
            bool entities = false;
            List<Vector2> legacyPolyline = null;
            var contours = new List<List<Vector2>>();
            for (int i = 0; i < pairs.Count;) {
                if (pairs[i].Key != 0) { i++; continue; }
                string type = pairs[i++].Value.ToUpperInvariant();
                var data = new List<KeyValuePair<int, string>>();
                while (i < pairs.Count && pairs[i].Key != 0) data.Add(pairs[i++]);
                if (type == "SECTION") { entities = data.Any(p => p.Key == 2 && p.Value == "ENTITIES"); continue; }
                if (type == "ENDSEC" || type == "EOF") { entities = false; continue; }
                if (!entities) continue;
                if (data.Any(p => (p.Key == 30 || p.Key == 31 || p.Key == 38 || p.Key == 210 || p.Key == 220
                    || (p.Key == 42 && (type == "LWPOLYLINE" || type == "VERTEX"))) && Number(p.Value) != 0)
                    || data.Any(p => p.Key == 230 && Number(p.Value) != 1))
                    throw new FormatException("DXF requires flat contours without polyline bulges. Convert arcs to polylines first.");
                switch (type) {
                    case "LWPOLYLINE": {
                        var points = DxfPoints(data);
                        if (data.Any(p => p.Key == 90) && Scalar(data, 90) != points.Count)
                            throw new FormatException("DXF polyline vertex count is incorrect.");
                        CloseContour(points, ((int)Scalar(data, 70) & 1) != 0);
                        contours.Add(points); break;
                    }
                    case "POLYLINE":
                        if (legacyPolyline != null || (((int)Scalar(data, 70) & 1) == 0))
                            throw new FormatException("DXF polylines must be closed.");
                        legacyPolyline = new List<Vector2>(); break;
                    case "VERTEX":
                        if (legacyPolyline == null) throw new FormatException("DXF vertex has no polyline.");
                        legacyPolyline.Add(new Vector2(Required(data, 10), Required(data, 20))); break;
                    case "SEQEND":
                        if (legacyPolyline == null) throw new FormatException("DXF polyline end has no polyline.");
                        CloseContour(legacyPolyline, true); contours.Add(legacyPolyline); legacyPolyline = null; break;
                    case "CIRCLE": {
                        float radius = Required(data, 40);
                        contours.Add(Ellipse(new Vector2(Required(data, 10), Required(data, 20)), new Vector2(radius, radius), 0)); break;
                    }
                    case "ELLIPSE": {
                        if (Mathf.Abs(Scalar(data, 41)) > .00001f || Mathf.Abs(Scalar(data, 42, Mathf.PI * 2) - Mathf.PI * 2) > .00001f)
                            throw new FormatException("DXF elliptical arcs must be closed.");
                        Vector2 major = new Vector2(Required(data, 11), Required(data, 21));
                        contours.Add(Ellipse(new Vector2(Required(data, 10), Required(data, 20)), new Vector2(major.magnitude, major.magnitude * Required(data, 40)), Mathf.Atan2(major.y, major.x)));
                        break;
                    }
                    default: throw new FormatException("Unsupported DXF entity: " + type + ". Export closed 2D polylines.");
                }
            }
            if (legacyPolyline != null) throw new FormatException("Unfinished DXF polyline.");
            foreach (var loop in contours) for (int i = 0; i < loop.Count; i++) loop[i] *= scale;
            return FromContours(contours, Path.GetFileNameWithoutExtension(filePath));
        }, out definition);

        private static bool Read(string path, Func<string, CustomPartDefinition> parse, out CustomPartDefinition definition) {
            definition = null; LastError = string.Empty;
            try {
                if (string.IsNullOrWhiteSpace(path)) throw new FormatException("Choose a file to import.");
                definition = parse(File.ReadAllText(path));
                return true;
            } catch (Exception e) {
                LastError = "Import failed: " + e.Message;
                definition = null;
                return false;
            }
        }

        private static bool Write(string path, Func<string> create) {
            LastError = string.Empty;
            string temporary = null;
            try {
                if (string.IsNullOrWhiteSpace(path)) throw new FormatException("Choose an export filename.");
                // Complete validation before touching the destination; failed exports preserve it.
                string content = create();
                path = Path.GetFullPath(path);
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                return true;
            } catch (Exception e) {
                LastError = "Export failed: " + e.Message;
                return false;
            } finally {
                if (temporary != null && File.Exists(temporary)) {
                    try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static List<List<Vector2>> Contours(CustomPartDefinition d) {
            if (!CustomPartMeshBuilder.TryGetContours(d, out var contours))
                throw new FormatException("Fix crossing edges, invalid dimensions, or overlapping cutouts before exporting.");
            return contours;
        }

        private static CustomPartDefinition FromContours(List<List<Vector2>> contours, string name) {
            if (contours.Count == 0) throw new FormatException("No closed outline was found.");
            contours = contours.OrderByDescending(p => Mathf.Abs(Area(p))).ToList();
            var d = CustomPartDefinition.CreateDefault();
            d.name = name;
            d.sketch.outerLoop = ToLoop(contours[0], false);
            d.sketch.cutoutLoops = contours.Skip(1).Select(p => ToLoop(p, true)).ToArray();
            if (!CustomPartMeshBuilder.TryGetContours(d, out _))
                throw new FormatException("Use one closed outline with separate, non-overlapping cutouts inside it.");
            return d;
        }
        private static LoopData ToLoop(List<Vector2> points, bool cutout) => new LoopData {
            name = cutout ? "Cutout" : "Outline", closed = true, isCutout = cutout,
            anchors = points.Select(p => new AnchorData { position = p }).ToArray(),
            segmentKinds = new SegmentKind[points.Count]
        };
        private static float Area(List<Vector2> points) {
            double result = 0;
            for (int i = 0; i < points.Count; i++) { var a = points[i]; var b = points[(i + 1) % points.Count]; result += (double)a.x * b.y - (double)a.y * b.x; }
            return (float)(result / 2);
        }
        private static float Number(string text) {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || float.IsNaN(v) || float.IsInfinity(v))
                throw new FormatException("Expected a finite number.");
            return v;
        }
        private static float[] Numbers(string text) {
            var matches = Regex.Matches(text, NumberPattern);
            if (Regex.Replace(Regex.Replace(text, NumberPattern, ""), @"[\s,]", "").Length != 0)
                throw new FormatException("Invalid coordinate list.");
            return matches.Cast<Match>().Select(m => Number(m.Value)).ToArray();
        }
        private static string Inv(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static float Scalar(List<KeyValuePair<int, string>> data, int code, float fallback = 0) {
            foreach (var pair in data) if (pair.Key == code) return Number(pair.Value);
            return fallback;
        }
        private static float Required(List<KeyValuePair<int, string>> data, int code) {
            if (!data.Any(p => p.Key == code)) throw new FormatException("DXF is missing a coordinate.");
            return Scalar(data, code);
        }
        private static List<Vector2> DxfPoints(List<KeyValuePair<int, string>> data) {
            var result = new List<Vector2>(); float? x = null;
            foreach (var p in data) {
                if (p.Key == 10) { if (x.HasValue) throw new FormatException("DXF vertex is missing Y."); x = Number(p.Value); }
                if (p.Key == 20) { if (!x.HasValue) throw new FormatException("DXF vertex is missing X."); result.Add(new Vector2(x.Value, Number(p.Value))); x = null; }
            }
            if (x.HasValue) throw new FormatException("DXF vertex is missing Y.");
            return result;
        }
        private static void CloseContour(List<Vector2> points, bool explicitlyClosed) {
            bool repeated = points.Count > 1 && (points[0] - points[points.Count - 1]).sqrMagnitude < 1e-10f;
            if (!explicitlyClosed && !repeated) throw new FormatException("Every outline and cutout must be closed.");
            if (repeated) points.RemoveAt(points.Count - 1);
            if (points.Count < 3) throw new FormatException("An outline needs at least three points.");
        }
        private static List<Vector2> Ellipse(Vector2 center, Vector2 radii, float rotation) {
            if (radii.x <= 0 || radii.y <= 0) throw new FormatException("Ellipse radii must be positive.");
            var points = new List<Vector2>();
            float c = Mathf.Cos(rotation), s = Mathf.Sin(rotation);
            for (int i = 0; i < 24; i++) {
                float angle = i * Mathf.PI * 2 / 24;
                var p = new Vector2(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y);
                points.Add(center + new Vector2(p.x * c - p.y * s, p.x * s + p.y * c));
            }
            return points;
        }

        private static float Attr(XmlElement element, string name, float fallback = 0) =>
            element.HasAttribute(name) ? Number(element.GetAttribute(name)) : fallback;
        private static void ReadSvg(XmlElement element, Matrix4x4 parent, List<List<Vector2>> output, bool root = false) {
            string name = element.LocalName;
            if (name == "defs" || name == "metadata" || name == "title" || name == "desc") return;
            if (element.GetAttribute("display") == "none" || Regex.IsMatch(element.GetAttribute("style"), @"display\s*:\s*none")) return;
            if (element.HasAttribute("clip-path") || element.HasAttribute("mask"))
                throw new FormatException("SVG clipping and masks are unsupported. Convert to closed paths.");
            Matrix4x4 transform = parent * SvgTransform(element.GetAttribute("transform"));
            var loops = new List<List<Vector2>>();
            switch (name) {
                case "svg": if (!root) throw new FormatException("Nested SVG viewports are unsupported."); break;
                case "g": break;
                case "path": loops = SvgPath(element.GetAttribute("d")); break;
                case "polygon": case "polyline": {
                    var values = Numbers(element.GetAttribute("points"));
                    if (values.Length % 2 != 0) throw new FormatException("SVG points must be X/Y pairs.");
                    var points = new List<Vector2>();
                    for (int i = 0; i < values.Length; i += 2) points.Add(new Vector2(values[i], values[i + 1]));
                    CloseContour(points, name == "polygon"); loops.Add(points); break;
                }
                case "rect": {
                    if (Attr(element, "rx") != 0 || Attr(element, "ry") != 0) throw new FormatException("Convert rounded SVG rectangles to paths first.");
                    float x = Attr(element, "x"), y = Attr(element, "y"), w = Attr(element, "width"), h = Attr(element, "height");
                    if (w <= 0 || h <= 0) throw new FormatException("SVG rectangle dimensions must be positive.");
                    loops.Add(new List<Vector2> { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) }); break;
                }
                case "circle": case "ellipse": {
                    float r = Attr(element, "r");
                    loops.Add(Ellipse(new Vector2(Attr(element, "cx"), Attr(element, "cy")), name == "circle" ? new Vector2(r, r) : new Vector2(Attr(element, "rx"), Attr(element, "ry")), 0));
                    break;
                }
                default: throw new FormatException("Unsupported SVG element: " + name + ". Convert to closed paths.");
            }
            foreach (var loop in loops) {
                for (int i = 0; i < loop.Count; i++) loop[i] = transform.MultiplyPoint3x4(loop[i]);
                output.Add(loop);
            }
            foreach (XmlNode child in element.ChildNodes) if (child is XmlElement e) ReadSvg(e, transform, output);
        }

        private static Matrix4x4 SvgUnits(XmlElement root) {
            // Earlier Protobot SVGs were unitless inch drawings. Explicit physical
            // dimensions/viewBox take precedence, so CAD millimeter files scale correctly.
            var box = Numbers(root.GetAttribute("viewBox"));
            if (box.Length != 0 && (box.Length != 4 || box[2] <= 0 || box[3] <= 0)) throw new FormatException("Invalid SVG viewBox.");
            float? width = PhysicalLength(root.GetAttribute("width")), height = PhysicalLength(root.GetAttribute("height"));
            if (!width.HasValue && !height.HasValue) return Matrix4x4.identity;
            if (box.Length == 0) return Matrix4x4.Scale(new Vector3(1 / 96f, 1 / 96f, 1));
            float sx = width.HasValue ? width.Value / box[2] : height.Value / box[3];
            float sy = height.HasValue ? height.Value / box[3] : sx;
            if (root.GetAttribute("preserveAspectRatio") != "none") sx = sy = Mathf.Min(sx, sy);
            // Preserve sketch coordinates; only physical scale matters for a part.
            return Matrix4x4.Scale(new Vector3(sx, sy, 1));
        }
        private static float? PhysicalLength(string text) {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = Regex.Match(text, @"^\s*(" + NumberPattern + @")\s*(in|mm|cm|px|pt|pc)\s*$", RegexOptions.IgnoreCase);
            if (!m.Success) {
                if (Regex.IsMatch(text, @"^\s*" + NumberPattern + @"\s*$")) return null;
                throw new FormatException("SVG dimensions must use inches, millimeters, centimeters, pixels, or points.");
            }
            float v = Number(m.Groups[1].Value);
            if (v <= 0) throw new FormatException("SVG physical dimensions must be positive.");
            switch (m.Groups[2].Value.ToLowerInvariant()) {
                case "mm": return v / 25.4f; case "cm": return v / 2.54f; case "px": return v / 96;
                case "pt": return v / 72; case "pc": return v / 6; default: return v;
            }
        }
        private static Matrix4x4 SvgTransform(string text) {
            Matrix4x4 result = Matrix4x4.identity;
            var matches = Regex.Matches(text, @"([A-Za-z]+)\s*\(([^)]*)\)");
            if (Regex.Replace(Regex.Replace(text, @"([A-Za-z]+)\s*\(([^)]*)\)", ""), @"[\s,]", "").Length != 0)
                throw new FormatException("Invalid SVG transform.");
            foreach (Match match in matches) {
                var v = Numbers(match.Groups[2].Value); var t = Matrix4x4.identity;
                switch (match.Groups[1].Value) {
                    case "translate":
                        if (v.Length < 1 || v.Length > 2) throw new FormatException("Invalid SVG translation.");
                        t = Matrix4x4.Translate(new Vector3(v[0], v.Length == 2 ? v[1] : 0, 0)); break;
                    case "scale":
                        if (v.Length < 1 || v.Length > 2) throw new FormatException("Invalid SVG scale.");
                        t = Matrix4x4.Scale(new Vector3(v[0], v.Length == 2 ? v[1] : v[0], 1)); break;
                    case "rotate":
                        if (v.Length != 1 && v.Length != 3) throw new FormatException("Invalid SVG rotation.");
                        var c = v.Length == 3 ? new Vector3(v[1], v[2], 0) : Vector3.zero;
                        t = Matrix4x4.Translate(c) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, v[0])) * Matrix4x4.Translate(-c); break;
                    case "matrix":
                        if (v.Length != 6) throw new FormatException("Invalid SVG matrix.");
                        t.m00 = v[0]; t.m10 = v[1]; t.m01 = v[2]; t.m11 = v[3]; t.m03 = v[4]; t.m13 = v[5]; break;
                    default: throw new FormatException("Unsupported SVG transform: " + match.Groups[1].Value);
                }
                result *= t;
            }
            return result;
        }

        private static List<List<Vector2>> SvgPath(string text) {
            string pattern = @"[A-Za-z]|" + NumberPattern;
            if (Regex.Replace(Regex.Replace(text, pattern, ""), @"[\s,]", "").Length != 0) throw new FormatException("Invalid SVG path.");
            var tokens = Regex.Matches(text, pattern).Cast<Match>().Select(m => m.Value).ToArray();
            var result = new List<List<Vector2>>();
            List<Vector2> points = null;
            Vector2 current = Vector2.zero, start = Vector2.zero, control = Vector2.zero;
            char command = '\0', previous = '\0';
            int index = 0;
            float Next() {
                if (index >= tokens.Length || char.IsLetter(tokens[index][0])) throw new FormatException("Incomplete SVG path command.");
                return Number(tokens[index++]);
            }
            Vector2 Pair(bool relative) { var p = new Vector2(Next(), Next()); return relative ? current + p : p; }
            while (index < tokens.Length) {
                if (char.IsLetter(tokens[index][0])) command = tokens[index++][0];
                if (command == '\0') throw new FormatException("SVG path is missing a command.");
                char kind = char.ToUpperInvariant(command);
                bool relative = char.IsLower(command);
                if (kind == 'Z') {
                    if (points == null) throw new FormatException("SVG close command has no path.");
                    CloseContour(points, true); result.Add(points); points = null; current = start; command = '\0'; previous = 'Z'; continue;
                }
                if (kind == 'M') {
                    if (points != null) { CloseContour(points, false); result.Add(points); }
                    current = Pair(relative); start = current; points = new List<Vector2> { current };
                    command = relative ? 'l' : 'L'; previous = 'M'; continue;
                }
                if (points == null) throw new FormatException("SVG path must begin with a move.");
                Vector2 end;
                switch (kind) {
                    case 'L': end = Pair(relative); points.Add(end); break;
                    case 'H': end = new Vector2(Next() + (relative ? current.x : 0), current.y); points.Add(end); break;
                    case 'V': end = new Vector2(current.x, Next() + (relative ? current.y : 0)); points.Add(end); break;
                    case 'C': case 'S': {
                        Vector2 c1 = kind == 'C' ? Pair(relative) : (previous == 'C' || previous == 'S' ? 2 * current - control : current);
                        Vector2 c2 = Pair(relative); end = Pair(relative);
                        Curve(points, current, c1, c2, end); control = c2; break;
                    }
                    case 'Q': case 'T': {
                        Vector2 c = kind == 'Q' ? Pair(relative) : (previous == 'Q' || previous == 'T' ? 2 * current - control : current);
                        end = Pair(relative); Curve(points, current, current + (c - current) * (2 / 3f), end + (c - end) * (2 / 3f), end);
                        control = c; break;
                    }
                    case 'A': {
                        float rx = Next(), ry = Next(), rotation = Next(), large = Next(), sweep = Next();
                        if ((large != 0 && large != 1) || (sweep != 0 && sweep != 1)) throw new FormatException("SVG arc flags must be 0 or 1.");
                        end = Pair(relative);
                        Arc(points, current, end, rx, ry, rotation, large == 1, sweep == 1);
                        break;
                    }
                    default: throw new FormatException("Unsupported SVG path command: " + command + ". Convert arcs to polylines first.");
                }
                current = end; previous = kind;
            }
            if (points != null) { CloseContour(points, false); result.Add(points); }
            return result;
        }
        private static void Curve(List<Vector2> points, Vector2 a, Vector2 b, Vector2 c, Vector2 d) {
            float length = Vector2.Distance(a, b) + Vector2.Distance(b, c) + Vector2.Distance(c, d);
            int steps = Mathf.Clamp(Mathf.CeilToInt(length / .1f), 6, 64);
            for (int i = 1; i <= steps; i++) {
                float t = i / (float)steps, u = 1 - t;
                points.Add(u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d);
            }
        }

        // SVG endpoint-to-center conversion (SVG 1.1 implementation notes F.6).
        // Preserve analytic arcs in exported files; sample them only for the app mesh.
        private static void Arc(List<Vector2> points, Vector2 from, Vector2 to, float radiusX, float radiusY,
            float rotation, bool large, bool sweep) {
            if (from == to) return;
            double rx = Math.Abs(radiusX), ry = Math.Abs(radiusY);
            if (rx == 0 || ry == 0) { points.Add(to); return; }
            double angle = rotation * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
            double dx = ((double)from.x - to.x) / 2, dy = ((double)from.y - to.y) / 2;
            double x = c * dx + s * dy, y = -s * dx + c * dy;
            double correction = x * x / (rx * rx) + y * y / (ry * ry);
            if (correction > 1) { correction = Math.Sqrt(correction); rx *= correction; ry *= correction; }
            double denominator = rx * rx * y * y + ry * ry * x * x;
            double factor = Math.Sqrt(Math.Max(0, (rx * rx * ry * ry - denominator) / denominator));
            if (large == sweep) factor = -factor;
            double centerX = factor * rx * y / ry, centerY = -factor * ry * x / rx;
            double worldX = c * centerX - s * centerY + ((double)from.x + to.x) / 2;
            double worldY = s * centerX + c * centerY + ((double)from.y + to.y) / 2;
            double ux = (x - centerX) / rx, uy = (y - centerY) / ry;
            double vx = (-x - centerX) / rx, vy = (-y - centerY) / ry;
            double start = Math.Atan2(uy, ux), extent = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            if (!sweep && extent > 0) extent -= 2 * Math.PI;
            if (sweep && extent < 0) extent += 2 * Math.PI;
            int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(extent) * 12 / Math.PI - 1e-6));
            for (int i = 1; i <= count; i++) {
                double t = start + extent * i / count;
                double px = rx * Math.Cos(t), py = ry * Math.Sin(t);
                points.Add(i == count ? to : new Vector2((float)(worldX + c * px - s * py), (float)(worldY + s * px + c * py)));
            }
        }
    }
}
