using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot.ChainSystem {
    // A closed, ordered route around convex contact surfaces. Circles are exact;
    // solid guides use their mesh cross-section, rounded by the roller radius.
    // No route is accepted by changing a guide's shape, order, or contact side.
    public static class ChainPathSolver {
        const float Epsilon = 0.0001f;
        const float Tau = Mathf.PI * 2;
        const int CandidatesPerState = 12;
        public struct ChainPose {
            public Vector3 position, tangent;
            public ChainPose(Vector3 p, Vector3 t) { position = p; tangent = t.normalized; }
        }
        public struct Span {
            public Vector3 start, end;
            public int afterEndpoint;
            public Vector3 ClosestPoint(Vector3 p) {
                var d = end - start;
                return start + d * Mathf.Clamp01(Vector3.Dot(p - start, d) / Mathf.Max(d.sqrMagnitude, Epsilon));
            }
        }
        public sealed class Route {
            public readonly List<ChainPose> poses = new List<ChainPose>();
            public readonly List<Span> spans = new List<Span>();
            public readonly List<Vector3> contactDirections = new List<Vector3>();
            public float length, spacing;
        }
        struct Piece {
            public Vector2 a, b, center;
            public float radius, angle, sweep, length;
            public int owner, edge;
            public bool deformed;
            public bool Arc => radius > 0;
            public Vector2 Point(float distance) {
                float t = Mathf.Clamp01(distance / Mathf.Max(length, Epsilon));
                return Arc ? center + Direction(angle + sweep * t) * radius : Vector2.Lerp(a, b, t);
            }
            public Vector2 Tangent(float distance) {
                return Arc ? Left(Direction(angle + sweep * Mathf.Clamp01(distance / Mathf.Max(length, Epsilon)))) * Mathf.Sign(sweep) : (b - a).normalized;
            }
        }
        sealed class Shape {
            public Vector2[] vertices;
            public Vector2 center, hint;
            public float radius, perimeter;
            public bool guide;
            public readonly List<Piece> boundary = new List<Piece>();
            public float[] cornerStart;
            public Vector2[] normals;
            public void BuildBoundary() {
                cornerStart = new float[vertices.Length];
                normals = new Vector2[vertices.Length];
                if (vertices.Length == 1) {
                    boundary.Add(Arc(vertices[0], radius, 0, Tau)); perimeter = Tau * radius; return;
                }
                for (int i = 0; i < vertices.Length; i++) normals[i] = Right((vertices[(i + 1) % vertices.Length] - vertices[i]).normalized);
                for (int i = 0; i < vertices.Length; i++) {
                    int previous = (i + vertices.Length - 1) % vertices.Length, next = (i + 1) % vertices.Length;
                    cornerStart[i] = perimeter;
                    var arc = Arc(vertices[i], radius, Angle(normals[previous]), Positive(Angle(normals[i]) - Angle(normals[previous])));
                    boundary.Add(arc); perimeter += arc.length;
                    var line = Line(vertices[i] + normals[i] * radius, vertices[next] + normals[i] * radius);
                    boundary.Add(line); perimeter += line.length;
                }
            }
            public Contact Support(Vector2 normal, Vector2 travel, bool outgoing) {
                // CAD bevels can be smaller than the route's collision tolerance.
                // Compare support heights in local double precision so nearby bevel
                // vertices are not incorrectly treated as one flat contact face.
                int best = 0;
                double value = SupportHeight(vertices[0], normal);
                for (int i = 1; i < vertices.Length; i++) {
                    double v = SupportHeight(vertices[i], normal);
                    float tie = Vector2.Dot(vertices[i] - vertices[best], travel) * (outgoing ? 1 : -1);
                    if (v > value + 1e-9 || (Math.Abs(v - value) <= 1e-9 && tie > 0)) { best = i; value = v; }
                }
                float coordinate = Positive(Angle(normal)) * radius;
                if (vertices.Length > 1) {
                    Vector2 previousNormal = normals[(best + vertices.Length - 1) % vertices.Length];
                    float cornerSweep = Positive(Angle(normals[best]) - Angle(previousNormal));
                    float offset = Mathf.Clamp(Mathf.Atan2(Cross(previousNormal, normal), Vector2.Dot(previousNormal, normal)), 0, cornerSweep);
                    // A tiny negative angle at a shared edge is zero, not a full
                    // revolution around the corner. Keep point and distance paired.
                    normal = Direction(Angle(previousNormal) + offset);
                    coordinate = cornerStart[best] + offset * radius;
                }
                return new Contact { point = vertices[best] + radius * normal, distance = coordinate % perimeter };
            }
            double SupportHeight(Vector2 vertex, Vector2 normal) =>
                ((double)vertex.x - center.x) * normal.x + ((double)vertex.y - center.y) * normal.y;
        }
        struct Contact { public Vector2 point; public float distance; }
        sealed class Edge { public Contact from, to; public Piece line; }
        sealed class Candidate { public int[] signs; public float cost; }
        sealed class SliceCache {
            public Vector3 origin, normal;
            public Mesh[] meshes;
            public Matrix4x4[] transforms;
            public Bounds[] bounds;
            public Vector2[] hull;
        }
        static Vector2 Left(Vector2 v) => new Vector2(-v.y, v.x);
        static Vector2 Right(Vector2 v) => new Vector2(v.y, -v.x);
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x);
        static Vector2 Direction(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        static float Positive(float a) => Mathf.Repeat(a, Tau);
        static Piece Line(Vector2 a, Vector2 b) => new Piece { a = a, b = b, length = Vector2.Distance(a, b), owner = -1, edge = -1 };
        static Piece Arc(Vector2 c, float r, float a, float sweep) => new Piece { center = c, radius = r, angle = a, sweep = sweep, length = Mathf.Abs(sweep) * r, a = c + Direction(a) * r, b = c + Direction(a + sweep) * r, owner = -1, edge = -1 };
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        public static bool TrySolve(IReadOnlyList<ChainEndpoint> endpoints, ChainStandard standard, float pitch, float slack, out Route route, out string error) {
            route = null; error = "Select at least two sprockets.";
            if (endpoints == null || endpoints.Count < 2) return false;
            ChainEndpoint reference = null;
            foreach (var endpoint in endpoints) if (endpoint != null && !endpoint.IsGuideEndpoint) { reference = endpoint; break; }
            if (reference == null) return false;
            Vector3 origin = reference.WorldCenter, normal = reference.WorldAxis.normalized;
            if (normal.sqrMagnitude < 0.9f) { error = "The sprocket axis is invalid."; return false; }
            Basis(normal, out var x, out var y);
            var shapes = new List<Shape>(endpoints.Count);
            var unique = new HashSet<ChainEndpoint>();
            for (int i = 0; i < endpoints.Count; i++) {
                var endpoint = endpoints[i];
                if (endpoint == null || !unique.Add(endpoint)) { error = "Each path part must be different."; return false; }
                Vector2 center = Project(endpoint.WorldCenter, origin, x, y);
                var shape = new Shape { center = center, guide = endpoint.IsGuideEndpoint };
                if (!shape.guide) {
                    if (Mathf.Abs(Vector3.Dot(endpoint.WorldAxis.normalized, normal)) < 0.99f || Mathf.Abs(Vector3.Dot(endpoint.WorldCenter - origin, normal)) > 0.05f) {
                        error = "Align the sprocket axes and chain planes."; return false;
                    }
                    shape.vertices = new[] { center };
                    shape.radius = ChainSprocketUtility.ResolvePitchRadius(endpoint, standard);
                } else {
                    var guide = endpoint.GetComponent<ChainGuide>();
                    if (!SliceGuide(endpoint, origin, normal, x, y, out shape.vertices)) {
                        error = "Tensioner misses the chain plane. Move it into the plane."; return false;
                    }
                    shape.radius = ChainDimensions.FromPitch(pitch, standard).rollerDiameter * 0.5f;
                    if (guide.HasContactHint) shape.hint = Project(guide.WorldContactHint, Vector3.zero, x, y).normalized;
                    else if (guide.RoutingBias != ChainGuideRoutingBias.Auto) {
                        var a = Project(endpoints[(i + endpoints.Count - 1) % endpoints.Count].WorldCenter, origin, x, y);
                        var b = Project(endpoints[(i + 1) % endpoints.Count].WorldCenter, origin, x, y);
                        var d = b - a;
                        var near = a + d * Mathf.Clamp01(Vector2.Dot(center - a, d) / Mathf.Max(Epsilon, d.sqrMagnitude));
                        shape.hint = (guide.RoutingBias == ChainGuideRoutingBias.PushInward ? near - center : center - near).normalized;
                        if (shape.hint.sqrMagnitude < 0.5f) shape.hint = Left(d.normalized);
                    }
                    if (guide.FlipSide) shape.hint = -shape.hint;
                }
                if (!Finite(shape.radius) || shape.radius < Epsilon) { error = "A part has an invalid contact radius."; return false; }
                shape.BuildBoundary(); shapes.Add(shape);
            }
            return Solve(shapes, origin, x, y, pitch, slack, out route, out error);
        }

        public static bool TrySolveLoop(IReadOnlyList<ChainEndpoint> endpoints, ChainStandard standard, Vector3 normal, float pitch, float slack, out List<ChainPose> poses, out float length, out float spacing) {
            bool ok = TrySolve(endpoints, standard, pitch, slack, out var route, out _);
            poses = ok ? route.poses : new List<ChainPose>(); length = ok ? route.length : 0; spacing = ok ? route.spacing : 0; return ok;
        }
        public static bool TrySolveLoop(Vector3 a, float ra, Vector3 b, float rb, Vector3 normal, float pitch, float slack, out List<ChainPose> poses, out float length, out float spacing) => TrySolveLoop(new[] { a, b }, new[] { ra, rb }, normal, pitch, slack, out poses, out length, out spacing);
        public static bool TrySolveLoop(IReadOnlyList<Vector3> centers, IReadOnlyList<float> radii, Vector3 normal, float pitch, float slack, out List<ChainPose> poses, out float length, out float spacing) {
            poses = new List<ChainPose>(); length = spacing = 0;
            if (centers == null || radii == null || centers.Count < 2 || centers.Count != radii.Count || normal.sqrMagnitude < Epsilon) return false;
            normal.Normalize(); Basis(normal, out var x, out var y); var shapes = new List<Shape>();
            for (int i = 0; i < centers.Count; i++) {
                if (!Finite(radii[i]) || radii[i] <= Epsilon || Mathf.Abs(Vector3.Dot(centers[i] - centers[0], normal)) > 0.05f) return false;
                var c = Project(centers[i], centers[0], x, y); var s = new Shape { center = c, vertices = new[] { c }, radius = radii[i] }; s.BuildBoundary(); shapes.Add(s);
            }
            if (!Solve(shapes, centers[0], x, y, pitch, slack, out var route, out _)) return false;
            poses = route.poses; length = route.length; spacing = route.spacing; return true;
        }
        static void Basis(Vector3 n, out Vector3 x, out Vector3 y) { x = Vector3.Cross(Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right, n).normalized; y = Vector3.Cross(n, x); }
        static Vector2 Project(Vector3 p, Vector3 origin, Vector3 x, Vector3 y) { p -= origin; return new Vector2(Vector3.Dot(p, x), Vector3.Dot(p, y)); }

        static bool SliceGuide(ChainEndpoint endpoint, Vector3 origin, Vector3 normal, Vector3 x, Vector3 y, out Vector2[] hull) {
            var part = ChainSprocketUtility.ResolvePartObject(endpoint.gameObject);
            var guide = endpoint.GetComponent<ChainGuide>();
            var filters = Array.FindAll(part.GetComponentsInChildren<MeshFilter>(), filter => filter.GetComponent<ShadowRenderProxy>() == null);
            var cached = guide.RouteGeometryCache as SliceCache;
            bool reusable = cached != null && cached.origin == origin && cached.normal == normal && cached.meshes.Length == filters.Length;
            if (reusable) for (int i = 0; i < filters.Length; i++) {
                var mesh = filters[i].sharedMesh;
                if (cached.meshes[i] != mesh || cached.transforms[i] != filters[i].transform.localToWorldMatrix || (mesh != null && cached.bounds[i] != mesh.bounds)) { reusable = false; break; }
            }
            if (reusable) { hull = cached.hull; return hull.Length >= 3; }
            var points = new List<Vector2>();
            foreach (var filter in filters) {
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable || filter.GetComponentInParent<ChainConnection>() != null) continue;
                var vertices = mesh.vertices; var triangles = mesh.triangles;
                var world = new Vector3[vertices.Length]; var depth = new float[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) { world[i] = filter.transform.TransformPoint(vertices[i]); depth[i] = Vector3.Dot(world[i] - origin, normal); }
                for (int i = 0; i + 2 < triangles.Length; i += 3) {
                    for (int j = 0; j < 3; j++) {
                        int a = triangles[i + j], b = triangles[i + (j + 1) % 3];
                        if (Mathf.Abs(depth[a]) <= Epsilon) points.Add(Project(world[a], origin, x, y));
                        if ((depth[a] < 0 && depth[b] > 0) || (depth[a] > 0 && depth[b] < 0)) points.Add(Project(Vector3.Lerp(world[a], world[b], depth[a] / (depth[a] - depth[b])), origin, x, y));
                    }
                }
            }
            hull = Hull(points);
            cached = new SliceCache { origin = origin, normal = normal, meshes = new Mesh[filters.Length], transforms = new Matrix4x4[filters.Length], bounds = new Bounds[filters.Length], hull = hull };
            for (int i = 0; i < filters.Length; i++) { cached.meshes[i] = filters[i].sharedMesh; cached.transforms[i] = filters[i].transform.localToWorldMatrix; if (cached.meshes[i] != null) cached.bounds[i] = cached.meshes[i].bounds; }
            guide.RouteGeometryCache = cached;
            return hull.Length >= 3;
        }
        static Vector2[] Hull(List<Vector2> points) {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var unique = new List<Vector2>(); foreach (var p in points) if (unique.Count == 0 || (p - unique[unique.Count - 1]).sqrMagnitude > Epsilon * Epsilon) unique.Add(p);
            if (unique.Count < 3) return unique.ToArray();
            var hull = new List<Vector2>();
            foreach (var p in unique) { while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= Epsilon * Epsilon) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            int lower = hull.Count;
            for (int i = unique.Count - 2; i >= 0; i--) { var p = unique[i]; while (hull.Count > lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= Epsilon * Epsilon) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            hull.RemoveAt(hull.Count - 1); return hull.ToArray();
        }

        // Within each interval the support vertex is constant, so the tangent
        // equation is analytic. There is no angular scan or recursive sign search.
        static Edge Tangent(Shape a, Shape b, int sa, int sb) {
            var breaks = new List<float> { 0, Tau };
            AddBreaks(a, sa, breaks); AddBreaks(b, sb, breaks); breaks.Sort();
            for (int i = 0; i < breaks.Count - 1; i++) {
                float lo = breaks[i], hi = breaks[i + 1]; if (hi - lo < 0.000001f) continue;
                var middle = Direction((lo + hi) * 0.5f);
                var ca = a.Support(middle * sa, Left(middle), true).point - middle * (sa * a.radius);
                var cb = b.Support(middle * sb, Left(middle), false).point - middle * (sb * b.radius);
                var delta = ca - cb; float magnitude = delta.magnitude;
                if (magnitude < Epsilon) continue;
                float q = (sb * b.radius - sa * a.radius) / magnitude; if (Mathf.Abs(q) >= 1 - 0.000001f) continue;
                float root = Mathf.Acos(q), baseAngle = Angle(delta);
                for (int branch = -1; branch <= 1; branch += 2) {
                    float angle = Positive(baseAngle + branch * root);
                    if (angle < lo - Epsilon || angle > hi + Epsilon) continue;
                    var n = Direction(angle); var travel = Left(n);
                    var from = a.Support(n * sa, travel, true); var to = b.Support(n * sb, travel, false);
                    var d = to.point - from.point;
                    if (Vector2.Dot(d, travel) <= Epsilon || Mathf.Abs(Vector2.Dot(d, n)) > 0.001f) continue;
                    return new Edge { from = from, to = to, line = Line(from.point, to.point) };
                }
            }
            return null;
        }
        static void AddBreaks(Shape s, int sign, List<float> breaks) { if (s.vertices.Length > 1) foreach (var normal in s.normals) breaks.Add(Positive(Angle(normal * sign))); }
        static List<Piece> Wrap(Shape shape, Contact incoming, Contact outgoing, int sign, int owner) {
            float start = incoming.distance, length = Mathf.Repeat((outgoing.distance - start) * sign, shape.perimeter);
            if (Vector2.Distance(incoming.point, outgoing.point) < Epsilon && (length < Epsilon || length > shape.perimeter - Epsilon)) {
                // Collinear standoffs may touch a straight run without bending it.
                if (shape.hint.sqrMagnitude > 0.5f && Vector2.Dot((incoming.point - shape.center).normalized, shape.hint) <= 0.05f) return null;
                return new List<Piece>();
            }
            if (length < Epsilon) return new List<Piece>();
            if (length > shape.perimeter - Epsilon) return null;
            var result = new List<Piece>(); float remaining = length, cursor = start;
            for (int guard = 0; remaining > Epsilon && guard < shape.boundary.Count + 3; guard++) {
                float offset = 0; int found = -1; float within = 0;
                float probe = Mathf.Repeat(cursor + sign * Epsilon * 0.1f, shape.perimeter);
                for (int i = 0; i < shape.boundary.Count; i++) {
                    if (probe < offset + shape.boundary[i].length) { found = i; within = Mathf.Clamp(cursor - offset, 0, shape.boundary[i].length); if (sign < 0 && cursor < Epsilon && i == shape.boundary.Count - 1) within = shape.boundary[i].length; break; }
                    offset += shape.boundary[i].length;
                }
                if (found < 0) return null;
                var source = shape.boundary[found]; float take = Mathf.Min(remaining, sign > 0 ? source.length - within : within);
                if (take < Epsilon * 0.01f) return null;
                Piece piece = source.Arc ? Arc(source.center, source.radius, source.angle + within / source.radius, sign * take / source.radius) : Line(source.Point(within), source.Point(within + sign * take));
                piece.owner = owner; result.Add(piece); cursor = Mathf.Repeat(cursor + sign * take, shape.perimeter); remaining -= take;
            }
            if (remaining > 0.001f) return null;
            if (shape.hint.sqrMagnitude > 0.5f) {
                float half = length * 0.5f; Vector2 contact = incoming.point;
                foreach (var piece in result) { if (half <= piece.length) { contact = piece.Point(half); break; } half -= piece.length; }
                if (Vector2.Dot((contact - shape.center).normalized, shape.hint) <= 0.05f) return null;
            }
            return result;
        }

        static bool Solve(List<Shape> shapes, Vector3 origin, Vector3 x, Vector3 y, float pitch, float slack, out Route route, out string error) {
            route = null; error = "No clear path. Move the tensioner, flip contact, or switch runs.";
            int count = shapes.Count;
            if (count > 128 || !Finite(pitch) || pitch <= 0 || !Finite(slack) || slack < 0) { error = "Invalid chain dimensions or too many path parts (maximum 128)."; return false; }
            for (int i = 0; i < count; i++) for (int j = i + 1; j < count; j++) {
                bool overlap = Inside(shapes[i].vertices[0], shapes[j]) || Inside(shapes[j].vertices[0], shapes[i]);
                if (!overlap) foreach (var a in shapes[i].boundary) {
                    foreach (var b in shapes[j].boundary) if (Intersect(a, b, false)) { overlap = true; break; }
                    if (overlap) break;
                }
                if (overlap) { error = "Path parts overlap. Separate them in the chain plane."; return false; }
            }
            var edges = new Edge[count, 2, 2]; var wraps = new List<Piece>[count, 2, 2, 2];
            for (int i = 0; i < count; i++) for (int a = 0; a < 2; a++) for (int b = 0; b < 2; b++) {
                var edge = Tangent(shapes[i], shapes[(i + 1) % count], a * 2 - 1, b * 2 - 1);
                if (edge != null && ClearOfShapes(edge.line, shapes, i, (i + 1) % count)) { edge.line.edge = i; edges[i, a, b] = edge; }
            }
            for (int i = 0; i < count; i++) for (int a = 0; a < 2; a++) for (int b = 0; b < 2; b++) for (int c = 0; c < 2; c++) {
                var incoming = edges[(i + count - 1) % count, a, b]; var outgoing = edges[i, b, c];
                if (incoming == null || outgoing == null) continue;
                var wrap = Wrap(shapes[i], incoming.to, outgoing.from, b * 2 - 1, i);
                if (wrap == null) continue;
                bool clear = true; foreach (var piece in wrap) if (!ClearOfShapes(piece, shapes, i, -1)) { clear = false; break; }
                if (clear) wraps[i, a, b, c] = wrap;
            }
            var candidates = new List<Candidate>();
            for (int first = 0; first < 2; first++) for (int second = 0; second < 2; second++) {
                if (edges[0, first, second] == null) continue;
                var current = new List<Candidate> { new Candidate { signs = new[] { first, second }, cost = edges[0, first, second].line.length } };
                for (int index = 2; index < count && current.Count > 0; index++) {
                    var buckets = new List<Candidate>[4]; for (int k = 0; k < 4; k++) buckets[k] = new List<Candidate>();
                    foreach (var candidate in current) for (int next = 0; next < 2; next++) {
                        int a = candidate.signs[index - 2], b = candidate.signs[index - 1]; var wrap = wraps[index - 1, a, b, next]; var edge = edges[index - 1, b, next];
                        if (wrap == null || edge == null) continue;
                        int[] signs = new int[index + 1]; Array.Copy(candidate.signs, signs, index); signs[index] = next;
                        buckets[b * 2 + next].Add(new Candidate { signs = signs, cost = candidate.cost + Length(wrap) + edge.line.length });
                    }
                    current.Clear(); foreach (var bucket in buckets) { bucket.Sort((a, b) => a.cost.CompareTo(b.cost)); for (int k = 0; k < Mathf.Min(CandidatesPerState, bucket.Count); k++) current.Add(bucket[k]); }
                }
                foreach (var candidate in current) {
                    var signs = candidate.signs; int last = signs[count - 1], prev = signs[count - 2];
                    var close = edges[count - 1, last, first]; var lastWrap = wraps[count - 1, prev, last, first]; var firstWrap = wraps[0, last, first, second];
                    if (close == null || lastWrap == null || firstWrap == null) continue;
                    candidate.cost += close.line.length + Length(lastWrap) + Length(firstWrap); candidates.Add(candidate);
                }
            }
            candidates.Sort((a, b) => a.cost.CompareTo(b.cost));
            foreach (var candidate in candidates) {
                var pieces = new List<Piece>();
                for (int i = 0; i < count; i++) {
                    int prev = (i + count - 1) % count, next = (i + 1) % count;
                    pieces.AddRange(wraps[i, candidate.signs[prev], candidate.signs[i], candidate.signs[next]]);
                    pieces.Add(edges[i, candidate.signs[i], candidate.signs[next]].line);
                }
                if (slack > Epsilon) AddSlack(pieces, slack);
                if (!Validate(pieces, shapes)) continue;
                float length = Length(pieces);
                int links = Mathf.Max(4, Mathf.RoundToInt(length / pitch));
                if (links > 8192) { error = "This chain exceeds the 8,192-link limit. Shorten the route."; return false; }
                route = new Route { length = length, spacing = length / links };
                for (int i = 0; i < count; i++) {
                    var contact = wraps[i, candidate.signs[(i + count - 1) % count], candidate.signs[i], candidate.signs[(i + 1) % count]];
                    float half = Length(contact) * 0.5f;
                    Vector2 direction = (edges[(i + count - 1) % count, candidate.signs[(i + count - 1) % count], candidate.signs[i]].to.point - shapes[i].center).normalized;
                    foreach (var piece in contact) { if (half <= piece.length) { direction = (piece.Point(half) - shapes[i].center).normalized; break; } half -= piece.length; }
                    route.contactDirections.Add(x * direction.x + y * direction.y);
                }
                int section = 0; float consumed = 0;
                for (int i = 0; i < links; i++) {
                    float distance = i * route.spacing;
                    while (section < pieces.Count - 1 && consumed + pieces[section].length < distance) consumed += pieces[section++].length;
                    var p = pieces[section].Point(distance - consumed); var t = pieces[section].Tangent(distance - consumed);
                    route.poses.Add(new ChainPose(origin + x * p.x + y * p.y, x * t.x + y * t.y));
                }
                foreach (var piece in pieces) if (piece.edge >= 0) route.spans.Add(new Span { start = origin + x * piece.a.x + y * piece.a.y, end = origin + x * piece.b.x + y * piece.b.y, afterEndpoint = piece.edge });
                error = string.Empty; return true;
            }
            return false;
        }
        static float Length(List<Piece> pieces) { float sum = 0; foreach (var p in pieces) sum += p.length; return sum; }
        static void AddSlack(List<Piece> pieces, float slack) {
            int longest = -1; float area = 0;
            for (int i = 0; i < pieces.Count; i++) { area += Cross(pieces[i].a, pieces[i].b); if (!pieces[i].Arc && (longest < 0 || pieces[i].length > pieces[longest].length)) longest = i; }
            if (longest < 0) return;
            var original = pieces[longest]; var outward = Right((original.b - original.a).normalized) * (area >= 0 ? 1 : -1);
            float low = 0, high = original.length + slack; var bowed = new List<Piece>();
            for (int iteration = 0; iteration < 24; iteration++) {
                float amplitude = (low + high) * 0.5f; bowed.Clear(); Vector2 previous = original.a;
                for (int i = 1; i <= 64; i++) { float t = i / 64f; float bump = 16 * t * t * (1 - t) * (1 - t); var point = Vector2.Lerp(original.a, original.b, t) + outward * (amplitude * bump); var p = Line(previous, point); p.edge = original.edge; p.deformed = true; bowed.Add(p); previous = point; }
                if (Length(bowed) < original.length + slack) low = amplitude; else high = amplitude;
            }
            pieces.RemoveAt(longest); pieces.InsertRange(longest, bowed);
        }
        static bool Validate(List<Piece> pieces, List<Shape> shapes) {
            for (int i = 0; i < pieces.Count; i++) {
                var p = pieces[i]; var next = pieces[(i + 1) % pieces.Count];
                if (!Finite(p.length) || p.length < Epsilon * 0.01f || Vector2.Distance(p.b, next.a) > 0.002f) return false;
                if (!ClearOfShapes(p, shapes, p.owner >= 0 ? p.owner : p.edge, p.edge >= 0 ? (p.edge + 1) % shapes.Count : -1)) return false;
                for (int j = i + 1; j < pieces.Count; j++) {
                    bool adjacent = j == i + 1 || (i == 0 && j == pieces.Count - 1);
                    if (Intersect(p, pieces[j], adjacent)) return false;
                }
            }
            return true;
        }
        static bool ClearOfShapes(Piece piece, List<Shape> shapes, int exceptA, int exceptB) {
            for (int i = 0; i < shapes.Count; i++) {
                bool ownContact = i == exceptA || i == exceptB;
                if (ownContact && !piece.deformed) continue;
                var shape = shapes[i];
                if (Inside(piece.a, shape) || Inside(piece.b, shape) || Inside(piece.Point(piece.length * 0.5f), shape)) return false;
                foreach (var boundary in shape.boundary) if (Intersect(piece, boundary, false, ownContact)) return false;
            }
            return true;
        }
        static bool Inside(Vector2 p, Shape shape) {
            if (shape.vertices.Length == 1) return Vector2.Distance(p, shape.center) < shape.radius - Epsilon;
            bool inside = true; float nearest = float.MaxValue;
            for (int i = 0; i < shape.vertices.Length; i++) {
                var a = shape.vertices[i]; var d = shape.vertices[(i + 1) % shape.vertices.Length] - a;
                if (Cross(d, p - a) < 0) inside = false;
                nearest = Mathf.Min(nearest, (p - a - d * Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude)).sqrMagnitude);
            }
            return inside || nearest < (shape.radius - Epsilon) * (shape.radius - Epsilon);
        }
        static bool OnArc(Vector2 p, Piece arc) {
            float angle = Positive((Angle(p - arc.center) - arc.angle) * Mathf.Sign(arc.sweep));
            return angle <= Mathf.Abs(arc.sweep) + Epsilon || angle >= Tau - Epsilon;
        }
        static bool AllowedJoin(Vector2 p, Piece a, Piece b, bool adjacent, bool surfaceContact) =>
            (surfaceContact && (Vector2.Distance(p, a.a) < Epsilon || Vector2.Distance(p, a.b) < Epsilon)) ||
            (adjacent && ((Vector2.Distance(p, a.a) < 0.001f || Vector2.Distance(p, a.b) < 0.001f) && (Vector2.Distance(p, b.a) < 0.001f || Vector2.Distance(p, b.b) < 0.001f)));
        static bool Intersect(Piece a, Piece b, bool adjacent, bool surfaceContact = false) {
            if (!a.Arc && !b.Arc) {
                var u = a.b - a.a; var v = b.b - b.a; float cross = Cross(u, v);
                if (Mathf.Abs(cross) < 0.000001f) {
                    if (Mathf.Abs(Cross(b.a - a.a, u)) > Epsilon * u.magnitude) return false;
                    float t0 = Vector2.Dot(b.a - a.a, u) / u.sqrMagnitude, t1 = Vector2.Dot(b.b - a.a, u) / u.sqrMagnitude;
                    float lo = Mathf.Max(0, Mathf.Min(t0, t1)), hi = Mathf.Min(1, Mathf.Max(t0, t1));
                    return hi >= lo && (hi - lo > Epsilon || !AllowedJoin(a.a + u * lo, a, b, adjacent, surfaceContact));
                }
                float t = Cross(b.a - a.a, v) / cross, s = Cross(b.a - a.a, u) / cross;
                return t >= -Epsilon && t <= 1 + Epsilon && s >= -Epsilon && s <= 1 + Epsilon && !AllowedJoin(a.a + u * t, a, b, adjacent, surfaceContact);
            }
            if (!a.Arc || !b.Arc) {
                var line = a.Arc ? b : a; var arc = a.Arc ? a : b; var d = line.b - line.a; var f = line.a - arc.center;
                float aa = d.sqrMagnitude, bb = 2 * Vector2.Dot(f, d), cc = f.sqrMagnitude - arc.radius * arc.radius, discriminant = bb * bb - 4 * aa * cc;
                if (discriminant < -Epsilon * aa) return false;
                float root = Mathf.Sqrt(Mathf.Max(0, discriminant));
                for (int sign = -1; sign <= 1; sign += 2) { float t = (-bb + sign * root) / (2 * aa); var p = line.a + d * t; if (t >= -Epsilon && t <= 1 + Epsilon && OnArc(p, arc) && !AllowedJoin(p, a, b, adjacent, surfaceContact)) return true; }
                return false;
            }
            var delta = b.center - a.center; float distance = delta.magnitude;
            if (distance < Epsilon) {
                if (Mathf.Abs(a.radius - b.radius) > Epsilon) return false;
                return (OnArc(a.Point(a.length * 0.5f), b) || OnArc(b.Point(b.length * 0.5f), a));
            }
            if (distance > a.radius + b.radius + Epsilon || distance < Mathf.Abs(a.radius - b.radius) - Epsilon) return false;
            float along = (a.radius * a.radius - b.radius * b.radius + distance * distance) / (2 * distance);
            float height = Mathf.Sqrt(Mathf.Max(0, a.radius * a.radius - along * along)); var center = a.center + delta * (along / distance); var offset = Left(delta) * (height / distance);
            for (int sign = -1; sign <= 1; sign += 2) { var p = center + offset * sign; if (OnArc(p, a) && OnArc(p, b) && !AllowedJoin(p, a, b, adjacent, surfaceContact)) return true; }
            return false;
        }
    }
}
