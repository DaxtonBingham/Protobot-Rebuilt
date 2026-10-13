using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot {
    public sealed class CameraPreferences : MonoBehaviour {
        public const string PreferenceKey = "Camera.FieldOfView";
        public const float DefaultFieldOfView = 60;
        public InputField fieldOfViewInput;
        public Button resetButton;

        public static float FieldOfView => Normalize(PlayerPrefs.GetFloat(PreferenceKey, DefaultFieldOfView));

        private static float Normalize(float value) {
            if (float.IsNaN(value) || float.IsInfinity(value)) return DefaultFieldOfView;
            return value;
        }

        private void OnEnable() {
            RefreshInput();
            fieldOfViewInput.onEndEdit.AddListener(CommitInput);
            resetButton.onClick.AddListener(ResetFieldOfView);
        }

        private void OnDisable() {
            fieldOfViewInput.onEndEdit.RemoveListener(CommitInput);
            resetButton.onClick.RemoveListener(ResetFieldOfView);
        }

        private void CommitInput(string text) {
            if (!fieldOfViewInput.wasCanceled &&
                (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out float value) ||
                 float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
                !float.IsNaN(value) && !float.IsInfinity(value)) SetFieldOfView(value);
            RefreshInput();
        }

        private void RefreshInput() {
            fieldOfViewInput.SetTextWithoutNotify(FieldOfView.ToString("0.########", CultureInfo.InvariantCulture));
        }

        public static void SetFieldOfView(float value) {
            value = Normalize(value);
            if (PivotCamera.Main != null) {
                PivotCamera.Main.SetFieldOfView(value);
                // Persist Unity's accepted value, without imposing an app limit.
                value = PivotCamera.Main.camera.fieldOfView;
            }
            PlayerPrefs.SetFloat(PreferenceKey, value);
            PlayerPrefs.Save();
        }

        private void ResetFieldOfView() {
            SetFieldOfView(DefaultFieldOfView);
            RefreshInput();
        }
    }
}
