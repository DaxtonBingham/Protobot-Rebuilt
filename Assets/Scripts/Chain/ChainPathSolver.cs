using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.ChainSystem {
    public static class ChainPathSolver {
        public struct ChainPose {
            public Vector3 position;
            public Vector3 tangent;

            public ChainPose(Vector3 newPosition, Vector3 newTangent) {
                position = newPosition;
                tangent = newTangent.normalized;
            }
        }

        private struct ArcDefinition {
            public Vector2 center;
            public float radius;
            public float startAngle;
            public float deltaAngle;
            public float length;
        }

        private struct TangentSegment {
            public Vector2 start;
            public Vector2 end;
            public float length;
        }

        private struct LoopSection {
            public bool isArc;
            public Vector2 lineStart;
            public Vector2 lineEnd;
            public ArcDefinition arc;
            public float length;
        }

        public static bool TrySolveLoop(
            Vector3 centerA,
            float radiusA,
            Vector3 centerB,
            float radiusB,
            Vector3 planeNormal,
            float preferredPitch,
            float slack,
            out List<ChainPose> poses,
            out float totalLength,
            out float effectivePitch) {
            return TrySolveLoop(
                new[] { centerA, centerB },
                new[] { radiusA, radiusB },
                planeNormal,
                preferredPitch,
                slack,
                out poses,
                out totalLength,
                out effectivePitch);
        }

        public static bool TrySolveLoop(
            IReadOnlyList<Vector3> centers,
            IReadOnlyList<float> radii,
            Vector3 planeNormal,
            float preferredPitch,
            float slack,
            out List<ChainPose> poses,
            out float totalLength,
            out float effectivePitch) {
            poses = new List<ChainPose>();
            totalLength = 0f;
            effectivePitch = 0f;

            if (centers == null || radii == null || centers.Count != radii.Count || centers.Count < 2) {
                return false;
            }

            if (centers.Count == 2) {
                return TrySolvePairLoop(
                    centers[0],
                    radii[0],
                    centers[1],
                    radii[1],
                    planeNormal,
                    preferredPitch,
                    slack,
                    out poses,
                    out totalLength,
                    out effectivePitch);
            }

            Vector3 n = planeNormal.normalized;
            if (n.sqrMagnitude < 0.0001f) {
                return false;
            }

            if (!TryBuildPlaneBasis(centers, n, out Vector3 origin, out Vector3 basisX, out Vector3 basisY)) {
                return false;
            }

            var centers2D = new List<Vector2>(centers.Count);
            for (int i = 0; i < centers.Count; i++) {
                Vector3 offset = centers[i] - origin;
                centers2D.Add(new Vector2(Vector3.Dot(offset, basisX), Vector3.Dot(offset, basisY)));
            }

            if (!TrySolveMultiLoop2D(centers2D, radii, preferredPitch, slack, out List<LoopSection> bestSections, out totalLength, out effectivePitch)) {
                return false;
            }

            int linkCount = Mathf.Max(3, Mathf.RoundToInt(totalLength / Mathf.Max(effectivePitch, 0.0001f)));
            for (int i = 0; i < linkCount; i++) {
                float distance = i * effectivePitch;
                Evaluate(bestSections, distance, out Vector2 point2D, out Vector2 tangent2D);

                Vector3 position = origin + (basisX * point2D.x) + (basisY * point2D.y);
                Vector3 tangent = ((basisX * tangent2D.x) + (basisY * tangent2D.y)).normalized;
                poses.Add(new ChainPose(position, tangent));
            }

            return poses.Count > 0;
        }

        private static bool TrySolveMultiLoop2D(
            IReadOnlyList<Vector2> centers,
            IReadOnlyList<float> radii,
            float preferredPitch,
            float slack,
            out List<LoopSection> bestSections,
            out float totalLength,
            out float effectivePitch) {
            bestSections = null;
            totalLength = 0f;
            effectivePitch = 0f;

            List<int[]> candidateOrders = BuildCandidateOrders(centers);
            bool foundSolution = false;
            float bestLength = float.MaxValue;

            for (int orderIndex = 0; orderIndex < candidateOrders.Count; orderIndex++) {
                int[] order = candidateOrders[orderIndex];
                List<Vector2> orderedCenters = Reorder(centers, order);
                List<float> orderedRadii = Reorder(radii, order);
                HashSet<int> hullIndices = ComputeConvexHullIndices(orderedCenters);
                List<int[]> circleSignAssignments = BuildCircleSignAssignments(orderedCenters.Count, hullIndices);
                float bestScoreForOrder = float.MaxValue;
                List<LoopSection> bestSectionsForOrder = null;
                float bestLengthForOrder = 0f;
                float bestPitchForOrder = 0f;

                for (int signIndex = 0; signIndex < circleSignAssignments.Count; signIndex++) {
                    int[] circleSigns = circleSignAssignments[signIndex];

                    for (int sideIndex = 0; sideIndex < 2; sideIndex++) {
                        float tangentSide = sideIndex == 0 ? -1f : 1f;
                        if (!TrySolveOrderedLoop2D(
                            orderedCenters,
                            orderedRadii,
                            circleSigns,
                            tangentSide,
                            preferredPitch,
                            slack,
                            out List<LoopSection> sections,
                            out float candidateLength,
                            out float candidatePitch)) {
                            continue;
                        }

                        float candidateScore = candidateLength + ComputeRoutePenalty(circleSigns, hullIndices);
                        if (candidateScore < bestScoreForOrder) {
                            bestScoreForOrder = candidateScore;
                            bestSectionsForOrder = sections;
                            bestLengthForOrder = candidateLength;
                            bestPitchForOrder = candidatePitch;
                        }
                    }
                }

                if (bestSectionsForOrder != null) {
                    if (!foundSolution || bestScoreForOrder < bestLength) {
                        foundSolution = true;
                        bestSections = bestSectionsForOrder;
                        totalLength = bestLengthForOrder;
                        effectivePitch = bestPitchForOrder;
                        bestLength = bestScoreForOrder;
                    }
                }

                if (foundSolution && orderIndex == 0) {
                    // Prefer the user's clicked order if it can produce a valid route.
                    return true;
                }
            }

            return foundSolution;
        }

        private static bool TrySolveOrderedLoop2D(
            IReadOnlyList<Vector2> centers,
            IReadOnlyList<float> radii,
            IReadOnlyList<int> circleSigns,
            float tangentSide,
            float preferredPitch,
            float slack,
            out List<LoopSection> sections,
            out float totalLength,
            out float effectivePitch) {
            sections = null;
            totalLength = 0f;
            effectivePitch = 0f;

            if (centers == null || radii == null || circleSigns == null || centers.Count != radii.Count || centers.Count != circleSigns.Count || centers.Count < 3) {
                return false;
            }

            Vector2 centroid = ComputeAverageCenter(centers);
            if (!TryBuildTangentSegments(
                centers,
                radii,
                circleSigns,
                tangentSide,
                out TangentSegment[] tangentSegments)) {
                return false;
            }

            var arcs = new ArcDefinition[centers.Count];
            for (int i = 0; i < centers.Count; i++) {
                int previousIndex = (i - 1 + centers.Count) % centers.Count;
                Vector2 incomingPoint = tangentSegments[previousIndex].end;
                Vector2 outgoingPoint = tangentSegments[i].start;
                Vector2 awayDirection = circleSigns[i] >= 0
                    ? centers[i] - centroid
                    : centroid - centers[i];
                if (awayDirection.sqrMagnitude < 0.0001f) {
                    Vector2 neighborAverage = (centers[previousIndex] + centers[(i + 1) % centers.Count]) * 0.5f;
                    awayDirection = circleSigns[i] >= 0
                        ? centers[i] - neighborAverage
                        : neighborAverage - centers[i];
                }
                if (awayDirection.sqrMagnitude < 0.0001f) {
                    awayDirection = new Vector2(0f, tangentSide * circleSigns[i]);
                }

                arcs[i] = BuildArc(
                    centers[i],
                    Mathf.Max(0.01f, radii[i]),
                    incomingPoint - centers[i],
                    outgoingPoint - centers[i],
                    awayDirection.normalized);
            }

            sections = new List<LoopSection>(centers.Count * 2);
            for (int i = 0; i < tangentSegments.Length; i++) {
                sections.Add(new LoopSection {
                    isArc = false,
                    lineStart = tangentSegments[i].start,
                    lineEnd = tangentSegments[i].end,
                    length = tangentSegments[i].length
                });
                totalLength += tangentSegments[i].length;

                int nextIndex = (i + 1) % tangentSegments.Length;
                sections.Add(new LoopSection {
                    isArc = true,
                    arc = arcs[nextIndex],
                    length = arcs[nextIndex].length
                });
                totalLength += arcs[nextIndex].length;
            }

            if (totalLength < 0.0001f) {
                sections = null;
                return false;
            }

            float pitch = Mathf.Max(0.05f, preferredPitch);
            int linkCount = Mathf.Max(3, Mathf.RoundToInt((totalLength + Mathf.Max(0f, slack)) / pitch));
            effectivePitch = totalLength / linkCount;
            return true;
        }

        private static bool TryBuildTangentSegments(
            IReadOnlyList<Vector2> centers,
            IReadOnlyList<float> radii,
            IReadOnlyList<int> circleSigns,
            float preferredBranch,
            out TangentSegment[] tangentSegments) {
            tangentSegments = new TangentSegment[centers.Count];
            if (centers == null || radii == null || circleSigns == null || centers.Count != radii.Count || centers.Count != circleSigns.Count || centers.Count < 3) {
                return false;
            }

            if (TryBuildTangentSegmentsWithBranch(
                centers,
                radii,
                circleSigns,
                preferredBranch,
                tangentSegments)) {
                return true;
            }

            if (TryBuildTangentSegmentsWithBranch(
                centers,
                radii,
                circleSigns,
                -preferredBranch,
                tangentSegments)) {
                return true;
            }

            return TryBuildTangentSegmentsRecursive(
                centers,
                radii,
                circleSigns,
                preferredBranch,
                tangentSegments,
                0);
        }

        private static bool TryBuildTangentSegmentsWithBranch(
            IReadOnlyList<Vector2> centers,
            IReadOnlyList<float> radii,
            IReadOnlyList<int> circleSigns,
            float tangentBranch,
            TangentSegment[] tangentSegments) {
            for (int i = 0; i < centers.Count; i++) {
                int nextIndex = (i + 1) % centers.Count;
                if (!TryBuildSignedTangent(
                    centers[i],
                    Mathf.Max(0.01f, radii[i]),
                    circleSigns[i],
                    centers[nextIndex],
                    Mathf.Max(0.01f, radii[nextIndex]),
                    circleSigns[nextIndex],
                    tangentBranch,
                    out tangentSegments[i])) {
                    return false;
                }
            }

            return !HasSelfIntersectingSegments(tangentSegments);
        }

        private static bool TryBuildTangentSegmentsRecursive(
            IReadOnlyList<Vector2> centers,
            IReadOnlyList<float> radii,
            IReadOnlyList<int> circleSigns,
            float preferredBranch,
            TangentSegment[] tangentSegments,
            int segmentIndex) {
            if (segmentIndex >= centers.Count) {
                return !HasSelfIntersectingSegments(tangentSegments);
            }

            int nextIndex = (segmentIndex + 1) % centers.Count;
            float[] branchOptions = { preferredBranch, -preferredBranch };

            for (int optionIndex = 0; optionIndex < branchOptions.Length; optionIndex++) {
                float tangentBranch = branchOptions[optionIndex];
                if (!TryBuildSignedTangent(
                    centers[segmentIndex],
                    Mathf.Max(0.01f, radii[segmentIndex]),
                    circleSigns[segmentIndex],
                    centers[nextIndex],
                    Mathf.Max(0.01f, radii[nextIndex]),
                    circleSigns[nextIndex],
                    tangentBranch,
                    out tangentSegments[segmentIndex])) {
                    continue;
                }

                if (HasSelfIntersectingSegmentsPartial(tangentSegments, segmentIndex + 1)) {
                    continue;
                }

                if (TryBuildTangentSegmentsRecursive(
                    centers,
                    radii,
                    circleSigns,
                    preferredBranch,
                    tangentSegments,
                    segmentIndex + 1)) {
                    return true;
                }
            }

            tangentSegments[segmentIndex] = default;
            return false;
        }

        private static bool HasSelfIntersectingSegmentsPartial(IReadOnlyList<TangentSegment> segments, int assignedCount) {
            for (int i = 0; i < assignedCount; i++) {
                for (int j = i + 1; j < assignedCount; j++) {
                    if (AreAdjacentEdges(i, j, segments.Count)) {
                        continue;
                    }

                    if (SegmentsIntersect(segments[i].start, segments[i].end, segments[j].start, segments[j].end)) {
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<int[]> BuildCircleSignAssignments(int count, HashSet<int> hullIndices) {
            var assignments = new List<int[]>();
            if (count <= 0) {
                return assignments;
            }

            var interiorIndices = new List<int>();
            for (int i = 0; i < count; i++) {
                if (hullIndices == null || !hullIndices.Contains(i)) {
                    interiorIndices.Add(i);
                }
            }

            int variants = 1 << interiorIndices.Count;
            for (int mask = 0; mask < variants; mask++) {
                var signs = new int[count];
                for (int i = 0; i < count; i++) {
                    signs[i] = 1;
                }

                for (int i = 0; i < interiorIndices.Count; i++) {
                    int index = interiorIndices[i];
                    signs[index] = ((mask >> i) & 1) == 0 ? 1 : -1;
                }

                assignments.Add(signs);
            }

            assignments.Sort((a, b) => CountSignChanges(a).CompareTo(CountSignChanges(b)));
            return assignments;
        }

        private static int CountSignChanges(IReadOnlyList<int> signs) {
            if (signs == null || signs.Count == 0) {
                return 0;
            }

            int changes = 0;
            for (int i = 0; i < signs.Count; i++) {
                int nextIndex = (i + 1) % signs.Count;
                if (signs[i] != signs[nextIndex]) {
                    changes++;
                }
            }

            return changes;
        }

        private static float ComputeRoutePenalty(IReadOnlyList<int> circleSigns, HashSet<int> hullIndices) {
            if (circleSigns == null || hullIndices == null) {
                return 0f;
            }

            float penalty = 0f;
            for (int i = 0; i < circleSigns.Count; i++) {
                bool onHull = hullIndices.Contains(i);
                if (onHull && circleSigns[i] < 0) {
                    penalty += 100000f;
                }
                else if (!onHull && circleSigns[i] > 0) {
                    penalty += 5000f;
                }
            }

            penalty += CountSignChanges(circleSigns) * 10f;
            return penalty;
        }

        private static HashSet<int> ComputeConvexHullIndices(IReadOnlyList<Vector2> points) {
            var hullIndices = new HashSet<int>();
            if (points == null || points.Count == 0) {
                return hullIndices;
            }

            if (points.Count <= 3) {
                for (int i = 0; i < points.Count; i++) {
                    hullIndices.Add(i);
                }
                return hullIndices;
            }

            var sorted = Enumerable.Range(0, points.Count)
                .OrderBy(index => points[index].x)
                .ThenBy(index => points[index].y)
                .ToList();

            var lower = new List<int>();
            for (int i = 0; i < sorted.Count; i++) {
                int index = sorted[i];
                while (lower.Count >= 2 && Cross(points[lower[lower.Count - 2]], points[lower[lower.Count - 1]], points[index]) <= 0f) {
                    lower.RemoveAt(lower.Count - 1);
                }
                lower.Add(index);
            }

            var upper = new List<int>();
            for (int i = sorted.Count - 1; i >= 0; i--) {
                int index = sorted[i];
                while (upper.Count >= 2 && Cross(points[upper[upper.Count - 2]], points[upper[upper.Count - 1]], points[index]) <= 0f) {
                    upper.RemoveAt(upper.Count - 1);
                }
                upper.Add(index);
            }

            for (int i = 0; i < lower.Count; i++) {
                hullIndices.Add(lower[i]);
            }
            for (int i = 0; i < upper.Count; i++) {
                hullIndices.Add(upper[i]);
            }

            return hullIndices;
        }

        private static List<int> ComputeConvexHullOrder(IReadOnlyList<Vector2> points) {
            var hullOrder = new List<int>();
            if (points == null || points.Count == 0) {
                return hullOrder;
            }

            if (points.Count <= 3) {
                for (int i = 0; i < points.Count; i++) {
                    hullOrder.Add(i);
                }
                return hullOrder;
            }

            var sorted = Enumerable.Range(0, points.Count)
                .OrderBy(index => points[index].x)
                .ThenBy(index => points[index].y)
                .ToList();

            var lower = new List<int>();
            for (int i = 0; i < sorted.Count; i++) {
                int index = sorted[i];
                while (lower.Count >= 2 && Cross(points[lower[lower.Count - 2]], points[lower[lower.Count - 1]], points[index]) <= 0f) {
                    lower.RemoveAt(lower.Count - 1);
                }
                lower.Add(index);
            }

            var upper = new List<int>();
            for (int i = sorted.Count - 1; i >= 0; i--) {
                int index = sorted[i];
                while (upper.Count >= 2 && Cross(points[upper[upper.Count - 2]], points[upper[upper.Count - 1]], points[index]) <= 0f) {
                    upper.RemoveAt(upper.Count - 1);
                }
                upper.Add(index);
            }

            for (int i = 0; i < lower.Count - 1; i++) {
                hullOrder.Add(lower[i]);
            }

            for (int i = 0; i < upper.Count - 1; i++) {
                int index = upper[i];
                if (!hullOrder.Contains(index)) {
                    hullOrder.Add(index);
                }
            }

            if (hullOrder.Count == 0) {
                hullOrder.AddRange(Enumerable.Range(0, points.Count));
            }

            return hullOrder;
        }

        private static float ComputeInsertionCost(Vector2 start, Vector2 point, Vector2 end) {
            float replacementCost = Vector2.Distance(start, point) + Vector2.Distance(point, end) - Vector2.Distance(start, end);
            Vector2 closestPoint = ClosestPointOnSegment(start, end, point);
            return replacementCost + (point - closestPoint).sqrMagnitude;
        }

        private static Vector2 ClosestPointOnSegment(Vector2 start, Vector2 end, Vector2 point) {
            Vector2 segment = end - start;
            float segmentLengthSq = segment.sqrMagnitude;
            if (segmentLengthSq < 0.0001f) {
                return start;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentLengthSq);
            return start + (segment * t);
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        private static List<int[]> BuildCandidateOrders(IReadOnlyList<Vector2> centers) {
            var candidateOrders = new List<int[]>();
            int count = centers.Count;

            AddCandidateOrder(candidateOrders, Enumerable.Range(0, count).ToArray());
            AddCandidateOrder(candidateOrders, Enumerable.Range(0, count).Reverse().ToArray());

            Vector2 centroid = ComputeAverageCenter(centers);
            int[] angleAscending = Enumerable.Range(0, count)
                .OrderBy(index => Mathf.Atan2(centers[index].y - centroid.y, centers[index].x - centroid.x))
                .ToArray();
            AddCandidateOrder(candidateOrders, angleAscending);
            AddCandidateOrder(candidateOrders, angleAscending.Reverse().ToArray());

            int[] hullInsertionOrder = BuildHullInsertionOrder(centers);
            AddCandidateOrder(candidateOrders, hullInsertionOrder);
            AddCandidateOrder(candidateOrders, hullInsertionOrder.Reverse().ToArray());

            int initialCandidateCount = candidateOrders.Count;
            for (int i = 0; i < initialCandidateCount; i++) {
                AddInsertionVariants(candidateOrders, candidateOrders[i]);
            }

            return candidateOrders;
        }

        private static int[] BuildHullInsertionOrder(IReadOnlyList<Vector2> centers) {
            if (centers == null || centers.Count == 0) {
                return System.Array.Empty<int>();
            }

            List<int> hullOrder = ComputeConvexHullOrder(centers);
            if (hullOrder.Count == 0) {
                return Enumerable.Range(0, centers.Count).ToArray();
            }

            var remaining = Enumerable.Range(0, centers.Count)
                .Where(index => !hullOrder.Contains(index))
                .ToList();

            while (remaining.Count > 0) {
                float bestCost = float.MaxValue;
                int bestPoint = remaining[0];
                int bestInsertIndex = hullOrder.Count - 1;

                for (int remainingIndex = 0; remainingIndex < remaining.Count; remainingIndex++) {
                    int pointIndex = remaining[remainingIndex];
                    for (int edgeIndex = 0; edgeIndex < hullOrder.Count; edgeIndex++) {
                        int startIndex = hullOrder[edgeIndex];
                        int endIndex = hullOrder[(edgeIndex + 1) % hullOrder.Count];
                        float insertionCost = ComputeInsertionCost(
                            centers[startIndex],
                            centers[pointIndex],
                            centers[endIndex]);

                        if (insertionCost < bestCost) {
                            bestCost = insertionCost;
                            bestPoint = pointIndex;
                            bestInsertIndex = edgeIndex;
                        }
                    }
                }

                hullOrder.Insert(bestInsertIndex + 1, bestPoint);
                remaining.Remove(bestPoint);
            }

            return hullOrder.ToArray();
        }

        private static void AddInsertionVariants(List<int[]> candidateOrders, int[] baseOrder) {
            if (candidateOrders == null || baseOrder == null || baseOrder.Length < 4) {
                return;
            }

            for (int sourceIndex = 0; sourceIndex < baseOrder.Length; sourceIndex++) {
                int movedPoint = baseOrder[sourceIndex];
                var remaining = new List<int>(baseOrder.Length - 1);
                for (int i = 0; i < baseOrder.Length; i++) {
                    if (i != sourceIndex) {
                        remaining.Add(baseOrder[i]);
                    }
                }

                for (int insertIndex = 0; insertIndex <= remaining.Count; insertIndex++) {
                    var variant = new List<int>(remaining);
                    variant.Insert(insertIndex, movedPoint);
                    AddCandidateOrder(candidateOrders, variant.ToArray());
                }
            }
        }

        private static void AddCandidateOrder(List<int[]> candidateOrders, int[] order) {
            int[] normalized = NormalizeCyclicOrder(order);
            for (int i = 0; i < candidateOrders.Count; i++) {
                if (OrdersMatch(candidateOrders[i], normalized)) {
                    return;
                }
            }

            candidateOrders.Add(normalized);
        }

        private static int[] NormalizeCyclicOrder(int[] order) {
            if (order == null || order.Length == 0) {
                return System.Array.Empty<int>();
            }

            int minValue = order[0];
            int minIndex = 0;
            for (int i = 1; i < order.Length; i++) {
                if (order[i] < minValue) {
                    minValue = order[i];
                    minIndex = i;
                }
            }

            var normalized = new int[order.Length];
            for (int i = 0; i < order.Length; i++) {
                normalized[i] = order[(minIndex + i) % order.Length];
            }

            return normalized;
        }

        private static bool OrdersMatch(int[] a, int[] b) {
            if (a == null || b == null || a.Length != b.Length) {
                return false;
            }

            for (int i = 0; i < a.Length; i++) {
                if (a[i] != b[i]) {
                    return false;
                }
            }

            return true;
        }

        private static List<T> Reorder<T>(IReadOnlyList<T> items, int[] order) {
            var reordered = new List<T>(order.Length);
            for (int i = 0; i < order.Length; i++) {
                reordered.Add(items[order[i]]);
            }

            return reordered;
        }

        private static bool TrySolvePairLoop(
            Vector3 centerA,
            float radiusA,
            Vector3 centerB,
            float radiusB,
            Vector3 planeNormal,
            float preferredPitch,
            float slack,
            out List<ChainPose> poses,
            out float totalLength,
            out float effectivePitch) {
            poses = new List<ChainPose>();
            totalLength = 0f;
            effectivePitch = 0f;

            Vector3 n = planeNormal.normalized;
            Vector3 delta = Vector3.ProjectOnPlane(centerB - centerA, n);
            float d = delta.magnitude;

            if (d < 0.0001f) {
                return false;
            }

            float rA = Mathf.Max(0.01f, radiusA);
            float rB = Mathf.Max(0.01f, radiusB);
            float diff = Mathf.Abs(rA - rB);
            if (d <= diff + 0.0001f) {
                return false;
            }

            Vector3 u = delta / d;
            Vector3 v = Vector3.Cross(n, u).normalized;

            float r = (rA - rB) / d;
            float hSq = 1f - (r * r);
            if (hSq <= 0f) {
                return false;
            }

            float h = Mathf.Sqrt(hSq);

            Vector2 aTop = new Vector2(rA * r, rA * h);
            Vector2 aBottom = new Vector2(rA * r, -rA * h);
            Vector2 bTop = new Vector2(d + (rB * r), rB * h);
            Vector2 bBottom = new Vector2(d + (rB * r), -rB * h);

            Vector2 line1Start = aTop;
            Vector2 line1End = bTop;
            Vector2 line2Start = bBottom;
            Vector2 line2End = aBottom;

            Vector2 centerA2 = Vector2.zero;
            Vector2 centerB2 = new Vector2(d, 0f);

            Vector2 awayFromA = (centerA2 - centerB2).normalized;
            Vector2 awayFromB = (centerB2 - centerA2).normalized;

            ArcDefinition arcB = BuildArc(centerB2, rB, bTop - centerB2, bBottom - centerB2, awayFromB);
            ArcDefinition arcA = BuildArc(centerA2, rA, aBottom - centerA2, aTop - centerA2, awayFromA);

            float line1Length = Vector2.Distance(line1Start, line1End);
            float line2Length = Vector2.Distance(line2Start, line2End);

            totalLength = line1Length + arcB.length + line2Length + arcA.length;
            if (totalLength < 0.0001f) {
                return false;
            }

            float pitch = Mathf.Max(0.05f, preferredPitch);
            int linkCount = Mathf.Max(3, Mathf.RoundToInt((totalLength + Mathf.Max(0f, slack)) / pitch));
            effectivePitch = totalLength / linkCount;

            for (int i = 0; i < linkCount; i++) {
                float distance = i * effectivePitch;
                Evaluate(
                    line1Start,
                    line1End,
                    line1Length,
                    arcB,
                    line2Start,
                    line2End,
                    line2Length,
                    arcA,
                    distance,
                    out Vector2 point2D,
                    out Vector2 tangent2D);

                Vector3 position = centerA + (u * point2D.x) + (v * point2D.y);
                Vector3 tangent = ((u * tangent2D.x) + (v * tangent2D.y)).normalized;
                poses.Add(new ChainPose(position, tangent));
            }

            return poses.Count > 0;
        }

        private static bool TryBuildPlaneBasis(
            IReadOnlyList<Vector3> centers,
            Vector3 planeNormal,
            out Vector3 origin,
            out Vector3 basisX,
            out Vector3 basisY) {
            origin = centers[0];
            basisX = Vector3.zero;
            basisY = Vector3.zero;

            for (int i = 1; i < centers.Count; i++) {
                Vector3 delta = Vector3.ProjectOnPlane(centers[i] - origin, planeNormal);
                if (delta.sqrMagnitude > 0.0001f) {
                    basisX = delta.normalized;
                    basisY = Vector3.Cross(planeNormal, basisX).normalized;
                    return basisY.sqrMagnitude > 0.0001f;
                }
            }

            return false;
        }

        private static bool TryBuildSignedTangent(
            Vector2 centerA,
            float radiusA,
            int sideA,
            Vector2 centerB,
            float radiusB,
            int sideB,
            float tangentBranch,
            out TangentSegment tangentSegment) {
            tangentSegment = default;

            Vector2 delta = centerB - centerA;
            float distance = delta.magnitude;
            if (distance < 0.0001f) {
                return false;
            }

            float signedDistance = (sideA * radiusA) - (sideB * radiusB);
            if (Mathf.Abs(signedDistance) >= distance - 0.0001f) {
                return false;
            }

            Vector2 u = delta / distance;
            Vector2 v = new Vector2(-u.y, u.x);

            float r = signedDistance / distance;
            float hSq = 1f - (r * r);
            if (hSq <= 0f) {
                return false;
            }

            float h = Mathf.Sqrt(hSq);
            Vector2 normal = (u * r) + (v * h * tangentBranch);
            tangentSegment.start = centerA + (normal * radiusA * sideA);
            tangentSegment.end = centerB + (normal * radiusB * sideB);
            tangentSegment.length = Vector2.Distance(tangentSegment.start, tangentSegment.end);
            return tangentSegment.length > 0.0001f;
        }

        private static bool IsSimpleLoopOrder(IReadOnlyList<Vector2> centers) {
            if (centers == null || centers.Count < 3) {
                return true;
            }

            for (int i = 0; i < centers.Count; i++) {
                Vector2 a1 = centers[i];
                Vector2 a2 = centers[(i + 1) % centers.Count];

                for (int j = i + 1; j < centers.Count; j++) {
                    if (AreAdjacentEdges(i, j, centers.Count)) {
                        continue;
                    }

                    Vector2 b1 = centers[j];
                    Vector2 b2 = centers[(j + 1) % centers.Count];
                    if (SegmentsIntersect(a1, a2, b1, b2)) {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool HasSelfIntersectingSegments(IReadOnlyList<TangentSegment> segments) {
            for (int i = 0; i < segments.Count; i++) {
                for (int j = i + 1; j < segments.Count; j++) {
                    if (AreAdjacentEdges(i, j, segments.Count)) {
                        continue;
                    }

                    if (SegmentsIntersect(segments[i].start, segments[i].end, segments[j].start, segments[j].end)) {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool AreAdjacentEdges(int indexA, int indexB, int count) {
            if (indexA == indexB) {
                return true;
            }

            if (((indexA + 1) % count) == indexB || ((indexB + 1) % count) == indexA) {
                return true;
            }

            return indexA == 0 && indexB == count - 1 || indexB == 0 && indexA == count - 1;
        }

        private static bool SegmentsIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2) {
            float o1 = Orientation(a1, a2, b1);
            float o2 = Orientation(a1, a2, b2);
            float o3 = Orientation(b1, b2, a1);
            float o4 = Orientation(b1, b2, a2);

            if (o1 * o2 < 0f && o3 * o4 < 0f) {
                return true;
            }

            const float epsilon = 0.0001f;
            if (Mathf.Abs(o1) <= epsilon && OnSegment(a1, a2, b1)) {
                return true;
            }
            if (Mathf.Abs(o2) <= epsilon && OnSegment(a1, a2, b2)) {
                return true;
            }
            if (Mathf.Abs(o3) <= epsilon && OnSegment(b1, b2, a1)) {
                return true;
            }
            if (Mathf.Abs(o4) <= epsilon && OnSegment(b1, b2, a2)) {
                return true;
            }

            return false;
        }

        private static float Orientation(Vector2 a, Vector2 b, Vector2 c) {
            return ((b.x - a.x) * (c.y - a.y)) - ((b.y - a.y) * (c.x - a.x));
        }

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 point) {
            return point.x <= Mathf.Max(a.x, b.x) + 0.0001f
                && point.x >= Mathf.Min(a.x, b.x) - 0.0001f
                && point.y <= Mathf.Max(a.y, b.y) + 0.0001f
                && point.y >= Mathf.Min(a.y, b.y) - 0.0001f;
        }

        private static float ComputeSignedArea(IReadOnlyList<Vector2> points) {
            float area = 0f;
            for (int i = 0; i < points.Count; i++) {
                Vector2 current = points[i];
                Vector2 next = points[(i + 1) % points.Count];
                area += (current.x * next.y) - (next.x * current.y);
            }

            return area * 0.5f;
        }

        private static Vector2 ComputeAverageCenter(IReadOnlyList<Vector2> points) {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < points.Count; i++) {
                sum += points[i];
            }

            return sum / Mathf.Max(1, points.Count);
        }

        private static ArcDefinition BuildArc(Vector2 center, float radius, Vector2 startVector, Vector2 endVector, Vector2 awayDirection) {
            float startAngle = Mathf.Atan2(startVector.y, startVector.x);
            float endAngle = Mathf.Atan2(endVector.y, endVector.x);

            float ccwDelta = Repeat(endAngle - startAngle, Mathf.PI * 2f);
            float cwDelta = ccwDelta - (Mathf.PI * 2f);

            float midCcw = startAngle + (ccwDelta * 0.5f);
            float midCw = startAngle + (cwDelta * 0.5f);

            Vector2 ccwMidDir = new Vector2(Mathf.Cos(midCcw), Mathf.Sin(midCcw));
            Vector2 cwMidDir = new Vector2(Mathf.Cos(midCw), Mathf.Sin(midCw));

            float ccwScore = Vector2.Dot(ccwMidDir, awayDirection);
            float cwScore = Vector2.Dot(cwMidDir, awayDirection);

            float chosenDelta = ccwScore >= cwScore ? ccwDelta : cwDelta;

            return new ArcDefinition {
                center = center,
                radius = radius,
                startAngle = startAngle,
                deltaAngle = chosenDelta,
                length = Mathf.Abs(chosenDelta) * radius
            };
        }

        private static void Evaluate(
            IReadOnlyList<LoopSection> sections,
            float distance,
            out Vector2 position,
            out Vector2 tangent) {
            float cursor = distance;

            for (int i = 0; i < sections.Count; i++) {
                LoopSection section = sections[i];
                if (cursor <= section.length || i == sections.Count - 1) {
                    if (section.isArc) {
                        float t = section.length > 0f ? Mathf.Clamp01(cursor / section.length) : 0f;
                        position = EvaluateArcPosition(section.arc, t);
                        tangent = EvaluateArcTangent(section.arc, t);
                        return;
                    }

                    float lineT = section.length > 0f ? Mathf.Clamp01(cursor / section.length) : 0f;
                    position = Vector2.Lerp(section.lineStart, section.lineEnd, lineT);
                    tangent = (section.lineEnd - section.lineStart).normalized;
                    return;
                }

                cursor -= section.length;
            }

            position = Vector2.zero;
            tangent = Vector2.right;
        }

        private static void Evaluate(
            Vector2 line1Start,
            Vector2 line1End,
            float line1Length,
            ArcDefinition arcB,
            Vector2 line2Start,
            Vector2 line2End,
            float line2Length,
            ArcDefinition arcA,
            float distance,
            out Vector2 position,
            out Vector2 tangent) {
            float cursor = distance;

            if (cursor <= line1Length) {
                float t = line1Length > 0f ? cursor / line1Length : 0f;
                position = Vector2.Lerp(line1Start, line1End, t);
                tangent = (line1End - line1Start).normalized;
                return;
            }

            cursor -= line1Length;
            if (cursor <= arcB.length) {
                float t = arcB.length > 0f ? cursor / arcB.length : 0f;
                position = EvaluateArcPosition(arcB, t);
                tangent = EvaluateArcTangent(arcB, t);
                return;
            }

            cursor -= arcB.length;
            if (cursor <= line2Length) {
                float t = line2Length > 0f ? cursor / line2Length : 0f;
                position = Vector2.Lerp(line2Start, line2End, t);
                tangent = (line2End - line2Start).normalized;
                return;
            }

            cursor -= line2Length;
            float arcAT = arcA.length > 0f ? Mathf.Clamp01(cursor / arcA.length) : 0f;
            position = EvaluateArcPosition(arcA, arcAT);
            tangent = EvaluateArcTangent(arcA, arcAT);
        }

        private static Vector2 EvaluateArcPosition(ArcDefinition arc, float t) {
            float angle = arc.startAngle + (arc.deltaAngle * t);
            return arc.center + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * arc.radius);
        }

        private static Vector2 EvaluateArcTangent(ArcDefinition arc, float t) {
            float angle = arc.startAngle + (arc.deltaAngle * t);
            float sign = Mathf.Sign(arc.deltaAngle);
            return new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * sign;
        }

        private static float Repeat(float value, float length) {
            return Mathf.Repeat(value, length);
        }
    }
}
