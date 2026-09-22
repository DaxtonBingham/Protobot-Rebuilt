using UnityEngine;

namespace Protobot {
    public class PivotCameraFocusMousePoint : MonoBehaviour {
        [SerializeField] private PivotCamera cam; 
        [SerializeField] private MouseCast mousecast;
        [SerializeField] private KeyCode keyInput;

        public void Update() {
            if (Input.GetKeyDown(keyInput))
                Execute();
        }

        public void Execute() {
            var mouseHit = mousecast.hit;

            if (mouseHit.collider != null)
                cam.MoveFocusPosition(mouseHit.point);
        }
    }
}
