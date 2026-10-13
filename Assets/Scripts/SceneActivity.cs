using System;
using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
    // A scene revision is independent of UI activity and camera motion. One native
    // callback observes part transforms, including movement inherited from groups.
    [DefaultExecutionOrder(9000)]
    public sealed class SceneActivity : MonoBehaviour {
        private static SceneActivity instance;
        private static int lastFlushFrame = -1;
        public static uint Revision { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() {
            instance = null;
            lastFlushFrame = -1;
            Revision = 0;
        }

        public static void Changed() {
            unchecked { Revision++; }
            IdleRendering.Changed();
        }

        internal static void Register(SavedObject part) {
            if (instance == null) {
                var host = new GameObject("Scene activity");
                DontDestroyOnLoad(host);
                instance = host.AddComponent<SceneActivity>();
            }
            RobotDocument.Register(part);
        }

        internal static void Unregister(SavedObject part) {
            RobotDocument.Deactivate(part);
        }

        private void LateUpdate() => Flush(true);

        // One bridge observes legacy Transform-based tools and tween animations.
        // Consumers receive changes for their own part instead of polling bindings.
        public static void Flush(bool force = false) {
            if (!force && lastFlushFrame == Time.frameCount) return;
            lastFlushFrame = Time.frameCount;
            var snapshot = RobotDocument.ActiveSnapshot(out int count);
            for (int i = 0; i < count; i++) {
                var part = snapshot[i].View;
                if (part == null || !part.isActiveAndEnabled) continue;
                var pose = part.CachedTransform;
                if (!pose.hasChanged) continue;
                pose.hasChanged = false;
                RobotDocument.Synchronize(part, PartChange.Pose);
            }
        }
    }
}
