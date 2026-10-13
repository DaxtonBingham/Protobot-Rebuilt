using UnityEngine;
using UnityEngine.UI;

namespace Protobot {
    public sealed class FrameRatePreferences : MonoBehaviour {
        public const string PreferenceKey = "Rendering.FrameRateLimit";
        private static readonly int[] Limits = { 0, 30, 45, 60, 90, 120, 144, 165, 240, -1 };
        public Dropdown dropdown;

        public static int SelectedIndex {
            get {
                int index = System.Array.IndexOf(Limits, PlayerPrefs.GetInt(PreferenceKey, 0));
                return index < 0 ? 0 : index;
            }
        }
        public static int Limit => Limits[SelectedIndex];
        public static int OptionCount => Limits.Length;
        public static string OptionLabel(int index) => Limits[index] == 0 ? "Display Sync"
            : Limits[index] < 0 ? "Unlimited" : Limits[index] + " FPS";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Apply() {
            int limit = Limit;
            // Unity ignores targetFrameRate when VSync is enabled.
            IdleRendering.SetActiveFrameRate(limit == 0 ? 1 : 0, limit > 0 ? limit : -1);
        }

        private void OnEnable() {
            dropdown.ClearOptions();
            for (int i = 0; i < Limits.Length; i++) dropdown.options.Add(new Dropdown.OptionData(OptionLabel(i)));
            dropdown.SetValueWithoutNotify(SelectedIndex);
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(SetOption);
        }
        private void OnDisable() { dropdown.onValueChanged.RemoveListener(SetOption); }

        public static void SetOption(int index) {
            if (index < 0 || index >= Limits.Length) return;
            PlayerPrefs.SetInt(PreferenceKey, Limits[index]);
            PlayerPrefs.Save();
            Apply();
        }
    }
}
