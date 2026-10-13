using System;
using System.IO;
using System.Text;

namespace Protobot.Export {
    /// <summary>Writes immutable captured geometry. No scene, renderer or native CAD access.</summary>
    public static class StlExportWriter {
        public sealed class Result {
            public int Parts;
            public uint Triangles;
            public long DegenerateTriangles;
        }

        struct Point {
            public double x, y, z;
            public Point(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        }

        public static Result Write(StepExportSnapshot snapshot, string path, Func<bool> cancelled = null, Action<int> completedParts = null) {
            if (snapshot == null || snapshot.parts == null || snapshot.parts.Count == 0)
                throw new InvalidOperationException("Add or select some parts before exporting.");
            double unit = snapshot.lengthUnit == "inch" ? 25.4 : snapshot.lengthUnit == "mm" ? 1 : 0;
            if (unit == 0) throw new InvalidOperationException("Unsupported STL source units: " + snapshot.lengthUnit);
            path = Path.GetFullPath(path);
            string temporary = Path.Combine(Path.GetDirectoryName(path), "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            var result = new Result();
            try {
                CheckCancellation(cancelled);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
                using (var writer = new BinaryWriter(stream, Encoding.ASCII)) {
                    var header = new byte[80];
                    Encoding.ASCII.GetBytes("Protobot binary STL | units=mm | Z-up | assembled geometry").CopyTo(header, 0);
                    writer.Write(header); writer.Write((uint)0);
                    foreach (var part in snapshot.parts) {
                        CheckCancellation(cancelled);
                        uint before = result.Triangles;
                        if (part.surfaces == null) throw new InvalidOperationException("No geometry for " + part.name);
                        foreach (var surface in part.surfaces) {
                            CheckCancellation(cancelled);
                            if (snapshot.geometry == null || surface.geometry < 0 || surface.geometry >= snapshot.geometry.Count)
                                throw new InvalidOperationException("Missing STL geometry for " + part.name);
                            var mesh = snapshot.geometry[surface.geometry];
                            if (mesh.vertices == null || mesh.triangles == null || mesh.triangles.Length % 3 != 0)
                                throw new InvalidOperationException("Invalid triangles in " + part.name);
                            var matrix = Multiply(part.matrix, surface.matrix);
                            double determinant = matrix[0] * (matrix[5] * matrix[10] - matrix[6] * matrix[9])
                                - matrix[1] * (matrix[4] * matrix[10] - matrix[6] * matrix[8])
                                + matrix[2] * (matrix[4] * matrix[9] - matrix[5] * matrix[8]);
                            if (!Finite(determinant) || determinant == 0)
                                throw new InvalidOperationException("Invalid or zero-scale placement for " + part.name);
                            // Swapping Y/Z changes handedness; mirrored parts change it again.
                            bool reverse = determinant > 0;
                            var vertices = new Point[mesh.vertices.Length];
                            for (int i = 0; i < vertices.Length; i++) {
                                if ((i & 4095) == 0) CheckCancellation(cancelled);
                                var v = mesh.vertices[i];
                                double x = (matrix[0] * v.x + matrix[1] * v.y + matrix[2] * v.z + matrix[3]) * unit;
                                double y = (matrix[8] * v.x + matrix[9] * v.y + matrix[10] * v.z + matrix[11]) * unit;
                                double z = (matrix[4] * v.x + matrix[5] * v.y + matrix[6] * v.z + matrix[7]) * unit;
                                if (!Representable(x) || !Representable(y) || !Representable(z))
                                    throw new InvalidOperationException("Invalid STL vertex in " + part.name);
                                // Normals/degeneracy must match the actual float32 vertices in the file.
                                vertices[i] = new Point((float)x, (float)y, (float)z);
                            }
                            var triangles = mesh.triangles;
                            for (int i = 0; i < triangles.Length; i += 3) {
                                if ((i & 4095) == 0) CheckCancellation(cancelled);
                                int a = triangles[i], b = triangles[i + (reverse ? 2 : 1)], c = triangles[i + (reverse ? 1 : 2)];
                                if ((uint)a >= vertices.Length || (uint)b >= vertices.Length || (uint)c >= vertices.Length)
                                    throw new InvalidOperationException("Invalid triangle index in " + part.name);
                                var p = vertices[a]; var q = vertices[b]; var r = vertices[c];
                                double ux = q.x - p.x, uy = q.y - p.y, uz = q.z - p.z;
                                double vx = r.x - p.x, vy = r.y - p.y, vz = r.z - p.z;
                                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                                double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                                if (length == 0) { result.DegenerateTriangles++; continue; }
                                if (result.Triangles == uint.MaxValue) throw new InvalidOperationException("This assembly exceeds the STL triangle limit. Export a smaller selection.");
                                writer.Write((float)(nx / length)); writer.Write((float)(ny / length)); writer.Write((float)(nz / length));
                                WritePoint(writer, p); WritePoint(writer, q); WritePoint(writer, r);
                                writer.Write((ushort)0); result.Triangles++;
                            }
                        }
                        if (result.Triangles == before) throw new InvalidOperationException("No nonzero triangles for " + part.name);
                        result.Parts++;
                        completedParts?.Invoke(result.Parts);
                    }
                    writer.Flush(); stream.Position = 80; writer.Write(result.Triangles); writer.Flush();
                    stream.Flush(true);
                }
                CheckCancellation(cancelled);
                // Publish only a complete file, on the destination volume. Errors and
                // cancellation leave any previous export untouched.
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return result;
            } finally {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        static void CheckCancellation(Func<bool> cancelled) {
            if (cancelled != null && cancelled()) throw new OperationCanceledException("STL export cancelled.");
        }
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static bool Representable(double value) { return Finite(value) && Math.Abs(value) <= float.MaxValue; }
        static void WritePoint(BinaryWriter writer, Point value) {
            writer.Write((float)value.x); writer.Write((float)value.y); writer.Write((float)value.z);
        }
        static double[] Multiply(float[] a, float[] b) {
            ValidateMatrix(a); ValidateMatrix(b);
            var result = new double[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
                for (int k = 0; k < 4; k++) result[r * 4 + c] += (double)a[r * 4 + k] * b[k * 4 + c];
            return result;
        }
        static void ValidateMatrix(float[] matrix) {
            if (matrix == null || matrix.Length != 16 || matrix[12] != 0 || matrix[13] != 0 || matrix[14] != 0 || matrix[15] != 1)
                throw new InvalidOperationException("Invalid STL placement matrix.");
            foreach (float value in matrix) if (!Finite(value)) throw new InvalidOperationException("Invalid STL placement matrix.");
        }
    }
}
