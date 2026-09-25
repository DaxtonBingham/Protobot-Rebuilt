using System;
using System.IO;
using System.Linq;
using Protobot.Export;
using UnityEditor;
using UnityEngine;

public static class StlExportVerification {
    [MenuItem("Tools/Verify STL Export")]
    public static void Verify() {
        string directory = Path.Combine(Path.GetTempPath(), "Protobot-STL-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "assembly.stl");
        try {
            var snapshot = Tetrahedron();
            var result = StlExportWriter.Write(snapshot, path);
            Require(result.Parts == 1 && result.Triangles == 4 && new FileInfo(path).Length == 284, "binary header/count/record length");
            var mesh = Read(path);
            Require(Near(mesh.volume, Math.Pow(25.4, 3) / 6), "inch-to-mm scale and outward winding");
            Require(mesh.min == Vector3.zero && (mesh.max - Vector3.one * 25.4f).sqrMagnitude < 1e-8f, "millimeter bounds");
            Require(mesh.normalsValid, "facet normals match winding");

            // Exercise both nested placements, rotation, reflection and nonuniform scale.
            var root = Matrix4x4.TRS(new Vector3(4, 5, 6), Quaternion.Euler(0, 0, 90), new Vector3(-2, 3, .5f));
            var child = Matrix4x4.Translate(new Vector3(1, 2, 3));
            snapshot.parts[0].matrix = StepExportSnapshot.Matrix(root);
            snapshot.parts[0].surfaces[0].matrix = StepExportSnapshot.Matrix(child);
            StlExportWriter.Write(snapshot, path); mesh = Read(path);
            var expected = new Bounds(); bool first = true;
            foreach (var v in snapshot.geometry[0].vertices) {
                var p = root.MultiplyPoint3x4(child.MultiplyPoint3x4(v)); p = new Vector3(p.x, p.z, p.y) * 25.4f;
                if (first) { expected = new Bounds(p, Vector3.zero); first = false; } else expected.Encapsulate(p);
            }
            Require((mesh.min - expected.min).magnitude < .001f && (mesh.max - expected.max).magnitude < .001f, "nested placement, reflection and Z-up bounds");
            Require(Near(mesh.volume, Math.Pow(25.4, 3) / 2, .05), "mirrored nonuniform scaling preserves outward volume");
            Require(mesh.normalsValid, "transformed normals match winding");

            snapshot = Tetrahedron(); snapshot.lengthUnit = "mm";
            snapshot.parts[0].surfaces.Add(new StepExportSnapshot.Surface { geometry = 0, matrix = StepExportSnapshot.Matrix(Matrix4x4.Translate(Vector3.right * 2)) });
            snapshot.parts.Add(new StepExportSnapshot.Part { name = "Instance", matrix = StepExportSnapshot.Matrix(Matrix4x4.Translate(Vector3.up * 4)), surfaces = snapshot.parts[0].surfaces });
            StlExportWriter.Write(snapshot, path); mesh = Read(path);
            Require(mesh.triangles == 16 && Near(mesh.volume, 4.0 / 6), "all submeshes and repeated instances, millimeter input");

            snapshot = Tetrahedron();
            snapshot.geometry[0].triangles = snapshot.geometry[0].triangles.Concat(new[] { 0, 0, 1 }).ToArray();
            result = StlExportWriter.Write(snapshot, path);
            Require(result.Triangles == 4 && result.DegenerateTriangles == 1, "zero-area facets omitted from count");
            File.WriteAllText(path, "previous output");
            bool cancel = false;
            ExpectFailure<OperationCanceledException>(() => StlExportWriter.Write(snapshot, path, () => cancel, _ => cancel = true));
            Require(File.ReadAllText(path) == "previous output", "cancellation before publication preserves destination");
            ExpectFailure<OperationCanceledException>(() => StlExportWriter.Write(snapshot, path, () => true));
            Require(File.ReadAllText(path) == "previous output", "cancellation before writing preserves destination");

            snapshot.geometry[0].triangles[0] = 999;
            ExpectFailure<InvalidOperationException>(() => StlExportWriter.Write(snapshot, path));
            Require(File.ReadAllText(path) == "previous output", "invalid geometry preserves destination");
            snapshot = Tetrahedron(); snapshot.geometry[0].vertices[0] = new Vector3(float.NaN, 0, 0);
            ExpectFailure<InvalidOperationException>(() => StlExportWriter.Write(snapshot, path));
            snapshot = Tetrahedron(); snapshot.parts[0].matrix[0] = 0;
            ExpectFailure<InvalidOperationException>(() => StlExportWriter.Write(snapshot, path));
            snapshot = Tetrahedron(); snapshot.parts[0].matrix[15] = 0;
            ExpectFailure<InvalidOperationException>(() => StlExportWriter.Write(snapshot, path));
            snapshot = Tetrahedron(); snapshot.lengthUnit = "unknown";
            ExpectFailure<InvalidOperationException>(() => StlExportWriter.Write(snapshot, path));
            snapshot = Tetrahedron(); snapshot.parts.Clear();
            ExpectFailure<InvalidOperationException>(() => StlExportWriter.Write(snapshot, path));
            Require(Directory.GetFiles(directory).Length == 1, "failed and cancelled exports remove temporary files");
            // The destination is locked: failure must not replace or truncate it.
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                ExpectFailure<IOException>(() => StlExportWriter.Write(Tetrahedron(), path));
            Require(File.ReadAllText(path) == "previous output" && Directory.GetFiles(directory).Length == 1, "locked output is preserved without temporary files");
            StlExportWriter.Write(Tetrahedron(), path);
            Require(Read(path).triangles == 4, "successful replacement of an existing export");
            Debug.Log("STL_EXPORT_VERIFICATION passed");
        } finally {
            // This exact, uniquely created directory contains only this verification's files.
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    static StepExportSnapshot Tetrahedron() {
        var result = new StepExportSnapshot { name = "STL check" };
        result.geometry.Add(new StepExportSnapshot.Geometry { name = "Tetrahedron", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward }, triangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 } });
        var part = new StepExportSnapshot.Part { name = "No CAD template needed", catalogId = "STL-only", matrix = StepExportSnapshot.Matrix(Matrix4x4.identity) };
        part.surfaces.Add(new StepExportSnapshot.Surface { geometry = 0, matrix = StepExportSnapshot.Matrix(Matrix4x4.identity) });
        result.parts.Add(part); return result;
    }
    sealed class MeshResult { public uint triangles; public Vector3 min, max; public double volume; public bool normalsValid = true; }
    static MeshResult Read(string path) {
        using (var reader = new BinaryReader(File.OpenRead(path))) {
            reader.ReadBytes(80); var result = new MeshResult { triangles = reader.ReadUInt32() };
            for (uint i = 0; i < result.triangles; i++) {
                var normal = ReadPoint(reader); var a = ReadPoint(reader); var b = ReadPoint(reader); var c = ReadPoint(reader); reader.ReadUInt16();
                var cross = Vector3.Cross(b - a, c - a).normalized;
                result.normalsValid &= Vector3.Dot(normal, cross) > .9999f;
                result.volume += ((double)a.x * ((double)b.y * c.z - (double)b.z * c.y)
                    + (double)a.y * ((double)b.z * c.x - (double)b.x * c.z)
                    + (double)a.z * ((double)b.x * c.y - (double)b.y * c.x)) / 6;
                foreach (var v in new[] { a, b, c }) {
                    if (i == 0 && v == a) { result.min = result.max = a; }
                    result.min = Vector3.Min(result.min, v); result.max = Vector3.Max(result.max, v);
                }
            }
            Require(reader.BaseStream.Position == reader.BaseStream.Length, "no extra or truncated STL records"); return result;
        }
    }
    static Vector3 ReadPoint(BinaryReader reader) { return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); }
    static bool Near(double a, double b, double tolerance = .01) { return Math.Abs(a - b) <= tolerance; }
    static void Require(bool value, string name) { if (!value) throw new Exception("STL verification: " + name); }
    static void ExpectFailure<T>(Action action) where T : Exception {
        try { action(); } catch (T) { return; }
        throw new Exception("STL verification: expected " + typeof(T).Name);
    }
}
