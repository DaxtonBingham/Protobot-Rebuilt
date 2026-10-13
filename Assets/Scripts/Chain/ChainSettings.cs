using System;
using Protobot.Builds;

namespace Protobot.ChainSystem {
    [Serializable]
    public class ChainSettings {
        public ChainStandard standard = ChainStandard.Pitch6p35;
        public bool autoResolveStandard = true;
        public float slack = 0f;
        public bool autoAlignSecondEndpoint = true;
        public float autoAlignMaxDistance = 20f;
        public bool singleChainPerEndpoint = true;

        public static ChainSettings CreateDefault(ChainStandard chainStandard = ChainStandard.Pitch6p35, bool autoResolve = true) {
            return new ChainSettings {
                standard = chainStandard,
                autoResolveStandard = autoResolve,
                slack = 0f,
                autoAlignSecondEndpoint = true,
                autoAlignMaxDistance = 20f,
                singleChainPerEndpoint = true
            };
        }

        public static ChainSettings FromData(ChainData data) {
            var settings = CreateDefault();
            if (data == null) {
                return settings;
            }

            if (TryParseStandard(data.standard, out ChainStandard parsedStandard)) {
                settings.standard = parsedStandard;
            }

            settings.autoResolveStandard = false;
            settings.slack = data.slack;
            settings.autoAlignSecondEndpoint = false;
            settings.singleChainPerEndpoint = false;

            return settings;
        }

        private static bool TryParseStandard(string value, out ChainStandard parsedStandard) {
            parsedStandard = ChainStandard.Pitch6p35;

            if (string.IsNullOrWhiteSpace(value)) {
                return false;
            }

            if (Enum.TryParse(value, true, out ChainStandard enumValue)) {
                parsedStandard = enumValue;
                return true;
            }

            string normalized = value.Trim().ToLowerInvariant();
            if (normalized.Contains("0.148") || normalized.Contains("3.75") || normalized.Contains("3p75")) {
                parsedStandard = ChainStandard.Pitch3p75;
                return true;
            }

            if (normalized.Contains("0.250") || normalized.Contains("6.35") || normalized.Contains("6p35") || normalized.Contains("25")) {
                parsedStandard = ChainStandard.Pitch6p35;
                return true;
            }

            if (normalized.Contains("0.385") || normalized.Contains("9.79") || normalized.Contains("9p79") || normalized == "chain35" || normalized == "#35") {
                parsedStandard = ChainStandard.Pitch9p79;
                return true;
            }

            return false;
        }
    }
}
