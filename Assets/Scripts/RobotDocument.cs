using System;
using System.Collections.Generic;
using DG.Tweening;
using Protobot.Builds;
using UnityEngine;

namespace Protobot {
    [Flags]
    public enum PartChange { None = 0, Pose = 1, Geometry = 2, Appearance = 4, Existence = 8, Metadata = 16, All = 31 }

    // The document keeps stable identities and values independently of scene hierarchy.
    // SavedObject is the adapter for existing tools, animation and legacy prefabs.
    public sealed class RobotPart {
        public string Id { get; internal set; }
        public string CatalogId { get; internal set; }
        public string State { get; internal set; }
        public string CustomDefinitionId { get; internal set; }
        public string CustomInstanceId { get; internal set; }
        public Vector3 Position { get; internal set; }
        public Quaternion Rotation { get; internal set; }
        public Vector3 Scale { get; internal set; }
        public Color Color { get; internal set; }
        public bool Exists { get; internal set; }
        public SavedObject View { get; internal set; }
        public uint Revision { get; internal set; }
        public event Action<RobotPart, PartChange> Changed;
        internal void Notify(PartChange change) { unchecked { Revision++; } Changed?.Invoke(this, change); }

        public ObjectData Export(Vector3 position, Quaternion rotation) {
            var angles = rotation.eulerAngles;
            return new ObjectData {
                instanceId = Id, partId = CatalogId, states = State,
                customDefinitionId = CustomDefinitionId, customInstanceId = CustomInstanceId,
                xPos = position.x, yPos = position.y, zPos = position.z,
                xRot = angles.x, yRot = angles.y, zRot = angles.z,
                rColor = Color.r, gColor = Color.g, bColor = Color.b
            };
        }
    }

    public static class RobotDocument {
        private static readonly Dictionary<string, RobotPart> byId = new Dictionary<string, RobotPart>();
        private static readonly List<RobotPart> active = new List<RobotPart>();
        private static RobotPart[] snapshot = Array.Empty<RobotPart>();
        private static int snapshotCount;
        private static bool listChanged;
        public static uint Revision { get; private set; }
        public static event Action<RobotPart, PartChange> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() {
            byId.Clear(); active.Clear(); snapshot = Array.Empty<RobotPart>(); snapshotCount = 0;
            listChanged = false; Revision = 0; Changed = null;
        }

        internal static RobotPart Register(SavedObject view) {
            var part = view.DocumentPart;
            if (part == null) {
                string id = view.documentId;
                if (string.IsNullOrEmpty(id) || byId.ContainsKey(id)) id = Guid.NewGuid().ToString("N");
                view.documentId = id;
                part = new RobotPart { Id = id, View = view, Color = Color.white };
                view.DocumentPart = part;
                byId.Add(id, part);
            }
            if (!part.Exists) {
                part.Exists = true; active.Add(part); listChanged = true;
                Synchronize(view, PartChange.All);
            }
            return part;
        }

        internal static void Deactivate(SavedObject view) {
            var part = view.DocumentPart;
            if (part == null || !part.Exists) return;
            part.Exists = false; active.Remove(part); listChanged = true;
            Publish(part, PartChange.Existence);
        }

        internal static void Forget(SavedObject view) {
            Deactivate(view);
            var part = view.DocumentPart;
            if (part != null) { byId.Remove(part.Id); part.View = null; }
            view.DocumentPart = null;
        }

        public static RobotPart Find(string id) => id != null && byId.TryGetValue(id, out var part) ? part : null;

        public static void RestoreIdentity(SavedObject view, string id) {
            if (view == null || string.IsNullOrWhiteSpace(id)) return;
            var part = view.DocumentPart ?? Register(view);
            if (part.Id == id) return;
            // A malformed file must never alias two physical parts to one identity.
            if (byId.ContainsKey(id)) return;
            byId.Remove(part.Id); part.Id = id; view.documentId = id; byId.Add(id, part);
        }

        internal static RobotPart[] ActiveSnapshot(out int count) {
            if (listChanged) {
                int oldCount = snapshotCount;
                snapshotCount = active.Count;
                if (snapshot.Length < snapshotCount) snapshot = new RobotPart[Math.Max(snapshotCount, snapshot.Length * 2)];
                active.CopyTo(snapshot);
                if (oldCount > snapshotCount) Array.Clear(snapshot, snapshotCount, oldCount - snapshotCount);
                listChanged = false;
            }
            count = snapshotCount;
            return snapshot;
        }

        public static List<GameObject> GetObjects() {
            var result = new List<GameObject>(active.Count);
            for (int i = 0; i < active.Count; i++)
                if (active[i].View != null && active[i].View.gameObject.activeInHierarchy) result.Add(active[i].View.gameObject);
            return result;
        }

        public static void Clear() {
            // Include inactive undo records, and release identities immediately.
            // Unity destroys views at end of frame; a same-frame PBB reload may
            // already need those identities for the new document.
            var previous = new List<RobotPart>(byId.Values);
            foreach (var part in previous) {
                var view = part.View;
                if (view == null) continue;
                view.gameObject.SetActive(false);
                Forget(view);
                UnityEngine.Object.Destroy(view.gameObject);
            }
        }

        public static void Synchronize(SavedObject view, PartChange requested = PartChange.All) {
            if (view == null) return;
            var part = view.DocumentPart;
            if (part == null) { if (view.isActiveAndEnabled) Register(view); return; }
            PartChange change = requested & (PartChange.Geometry | PartChange.Existence);
            if ((requested & PartChange.Pose) != 0) {
                var t = view.CachedTransform;
                var position = t.position; var rotation = t.rotation; var scale = t.lossyScale;
                if (part.Position != position || part.Rotation != rotation || part.Scale != scale) {
                    part.Position = position; part.Rotation = rotation; part.Scale = scale; change |= PartChange.Pose;
                }
            }
            if ((requested & PartChange.Appearance) != 0) {
                var renderer = view.CachedRenderer;
                var material = renderer != null ? renderer.sharedMaterial : null;
                var color = material != null && material.HasProperty("_Color") ? material.color : Color.white;
                if (part.Color != color) { part.Color = color; change |= PartChange.Appearance; }
            }
            if ((requested & PartChange.Metadata) != 0) {
                if (part.CatalogId != view.id || part.State != view.state || part.CustomDefinitionId != view.customDefinitionId || part.CustomInstanceId != view.customInstanceId) {
                    part.CatalogId = view.id; part.State = view.state;
                    part.CustomDefinitionId = view.customDefinitionId; part.CustomInstanceId = view.customInstanceId;
                    change |= PartChange.Metadata;
                }
            }
            if (change != PartChange.None) Publish(part, change);
        }

        public static bool SetPose(string id, Vector3 position, Quaternion rotation, bool animate = false) {
            var part = Find(id);
            if (part?.View == null) return false;
            var t = part.View.CachedTransform;
            t.DOKill();
            if (animate) {
                if (t.position != position) t.DOMove(position, .25f);
                if (t.rotation != rotation) t.DORotateQuaternion(rotation, .25f);
            } else {
                t.SetPositionAndRotation(position, rotation);
                Synchronize(part.View, PartChange.Pose);
            }
            return true;
        }

        public static bool SetExistence(string id, bool exists) {
            var part = Find(id);
            if (part?.View == null) return false;
            var obj = part.View.gameObject;
            obj.layer = exists ? 0 : LayerMask.NameToLayer("Deleted");
            obj.SetActive(exists);
            return true;
        }

        public static void SetColor(SavedObject view, Color color) {
            if (view == null || view.CachedRenderer == null) return;
            view.CachedRenderer.material.color = color;
            Synchronize(view, PartChange.Appearance);
        }

        private static void Publish(RobotPart part, PartChange change) {
            unchecked { Revision++; }
            part.Notify(change);
            Changed?.Invoke(part, change);
            SceneActivity.Changed();
        }
    }
}
