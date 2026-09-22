using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    // Broad phase shared by picking and attachment queries. Large objects use a
    // separate bucket so an unusually long part cannot explode the cell count.
    internal sealed class SpatialGrid<T> where T : class {
        private const float CellSize = 2f;
        private readonly Dictionary<Vector3Int, HashSet<T>> cells = new Dictionary<Vector3Int, HashSet<T>>();
        private readonly Dictionary<T, Bounds> bounds = new Dictionary<T, Bounds>();
        private readonly HashSet<T> large = new HashSet<T>();
        private readonly HashSet<T> seen = new HashSet<T>();
        private Bounds world;
        private bool hasWorld;
        private static Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.y / CellSize), Mathf.FloorToInt(p.z / CellSize));
        private static long CellCount(Vector3Int min, Vector3Int max) => ((long)max.x - min.x + 1) * ((long)max.y - min.y + 1) * ((long)max.z - min.z + 1);

        public void Set(T item, Bounds value) {
            Remove(item); bounds[item] = value;
            if (!hasWorld) { world = value; hasWorld = true; } else world.Encapsulate(value);
            var min = Cell(value.min); var max = Cell(value.max);
            if (CellCount(min, max) > 512) { large.Add(item); return; }
            for (int x = min.x; x <= max.x; x++) for (int y = min.y; y <= max.y; y++) for (int z = min.z; z <= max.z; z++) {
                var key = new Vector3Int(x, y, z);
                if (!cells.TryGetValue(key, out var bucket)) cells.Add(key, bucket = new HashSet<T>());
                bucket.Add(item);
            }
        }

        public void Remove(T item) {
            if (!bounds.TryGetValue(item, out var old)) return;
            bounds.Remove(item);
            if (large.Remove(item)) return;
            var min = Cell(old.min); var max = Cell(old.max);
            for (int x = min.x; x <= max.x; x++) for (int y = min.y; y <= max.y; y++) for (int z = min.z; z <= max.z; z++) {
                var key = new Vector3Int(x, y, z);
                if (!cells.TryGetValue(key, out var bucket)) continue;
                bucket.Remove(item); if (bucket.Count == 0) cells.Remove(key);
            }
            if (bounds.Count == 0) hasWorld = false;
        }

        public void Query(Bounds area, List<T> result) {
            result.Clear(); seen.Clear();
            var min = Cell(area.min); var max = Cell(area.max);
            if (CellCount(min, max) > 4096) {
                foreach (var pair in bounds) if (pair.Value.Intersects(area)) result.Add(pair.Key);
                return;
            }
            for (int x = min.x; x <= max.x; x++) for (int y = min.y; y <= max.y; y++) for (int z = min.z; z <= max.z; z++) {
                if (!cells.TryGetValue(new Vector3Int(x, y, z), out var bucket)) continue;
                foreach (var item in bucket) if (seen.Add(item) && bounds[item].Intersects(area)) result.Add(item);
            }
            foreach (var item in large) if (bounds[item].Intersects(area)) result.Add(item);
        }

        public void Query(Ray ray, float distance, List<T> result) {
            result.Clear(); seen.Clear();
            if (!hasWorld || ray.direction.sqrMagnitude < 1e-12f) return;
            float entry = 0, exit = distance;
            if (!GeometryQuery.RayBounds(ray, world, ref entry, ref exit)) return;
            entry = Mathf.Max(0, entry - .0001f); exit = Mathf.Min(distance, exit + .0001f);
            var cell = Cell(ray.GetPoint(entry));
            var step = new Vector3Int(Math.Sign(ray.direction.x), Math.Sign(ray.direction.y), Math.Sign(ray.direction.z));
            Vector3 delta = new Vector3(StepDistance(ray.direction.x), StepDistance(ray.direction.y), StepDistance(ray.direction.z));
            Vector3 next = new Vector3(Next(ray, cell.x, step.x, 0), Next(ray, cell.y, step.y, 1), Next(ray, cell.z, step.z, 2));
            int visits = 0;
            while (entry <= exit && visits++ < 8192) {
                if (cells.TryGetValue(cell, out var bucket)) foreach (var item in bucket) {
                    float lo = 0, hi = distance;
                    if (seen.Add(item) && GeometryQuery.RayBounds(ray, bounds[item], ref lo, ref hi)) result.Add(item);
                }
                if (next.x <= next.y && next.x <= next.z) { entry = next.x; next.x += delta.x; cell.x += step.x; }
                else if (next.y <= next.z) { entry = next.y; next.y += delta.y; cell.y += step.y; }
                else { entry = next.z; next.z += delta.z; cell.z += step.z; }
            }
            if (visits >= 8192) foreach (var pair in bounds) {
                float lo = 0, hi = distance;
                if (seen.Add(pair.Key) && GeometryQuery.RayBounds(ray, pair.Value, ref lo, ref hi)) result.Add(pair.Key);
            }
            foreach (var item in large) {
                float lo = 0, hi = distance;
                if (seen.Add(item) && GeometryQuery.RayBounds(ray, bounds[item], ref lo, ref hi)) result.Add(item);
            }
        }

        private static float StepDistance(float d) => Mathf.Abs(d) < 1e-12f ? float.PositiveInfinity : CellSize / Mathf.Abs(d);
        private static float Next(Ray ray, int cell, int step, int axis) => step == 0 ? float.PositiveInfinity : ((cell + (step > 0 ? 1 : 0)) * CellSize - ray.origin[axis]) / ray.direction[axis];
    }
}
