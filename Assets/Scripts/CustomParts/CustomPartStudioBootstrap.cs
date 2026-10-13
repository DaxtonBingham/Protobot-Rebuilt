using UnityEngine;

namespace Protobot.CustomParts {
    public static class CustomPartStudioBootstrap {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureStudioController() {
            if (Object.FindObjectOfType<CustomPartStudioController>() != null) {
                return;
            }

            var host = new GameObject("Custom Part Studio");
            host.AddComponent<CustomPartStudioController>();
        }
    }
}
