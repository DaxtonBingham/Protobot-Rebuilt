using System;
using UnityEngine;

namespace Protobot.ChainSystem {
    [Serializable]
    public struct ChainDimensions {
        public float pitch;
        public float width;
        public float rollerDiameter;
        public float pinDiameter;
        public float plateThickness;
        public float plateHeight;
        public float outerPlateLength;
        public float innerPlateLength;
        public float jointOffset;
        // Official VEX pitch values from the VEX Library table:
        // 3.75mm / 0.148", 6.35mm / 0.250", 9.79mm / 0.385".
        private const float Pitch3p75Inches = 0.148f;
        private const float Pitch6p35Inches = 0.250f;
        private const float Pitch9p79Inches = 0.385f;

        public static ChainDimensions FromPitch(float resolvedPitch, ChainStandard standard) {
            float pitch = Mathf.Max(resolvedPitch, 0.05f);
            ChainDimensions nominal;
            float nominalPitch;

            switch (standard) {
                case ChainStandard.Pitch3p75:
                    nominal = Nominal3p75();
                    nominalPitch = Pitch3p75Inches;
                    break;
                case ChainStandard.Pitch9p79:
                    nominal = Nominal9p79();
                    nominalPitch = Pitch9p79Inches;
                    break;
                default:
                    nominal = Nominal6p35();
                    nominalPitch = Pitch6p35Inches;
                    break;
            }

            float scale = pitch / nominalPitch;

            return new ChainDimensions {
                pitch = pitch,
                width = nominal.width * scale,
                rollerDiameter = nominal.rollerDiameter * scale,
                pinDiameter = nominal.pinDiameter * scale,
                plateThickness = nominal.plateThickness * scale,
                plateHeight = nominal.plateHeight * scale,
                outerPlateLength = nominal.outerPlateLength * scale,
                innerPlateLength = nominal.innerPlateLength * scale,
                jointOffset = pitch * 0.5f
            };
        }

        private static ChainDimensions Nominal3p75() {
            // Approximated by scaling a 6.35mm pitch chain down to 3.75mm pitch.
            ChainDimensions nominal6p35 = Nominal6p35();
            float scale = Pitch3p75Inches / Pitch6p35Inches;
            return new ChainDimensions {
                pitch = Pitch3p75Inches,
                width = nominal6p35.width * scale,
                rollerDiameter = nominal6p35.rollerDiameter * scale,
                pinDiameter = nominal6p35.pinDiameter * scale,
                plateThickness = nominal6p35.plateThickness * scale,
                plateHeight = nominal6p35.plateHeight * scale,
                outerPlateLength = nominal6p35.outerPlateLength * scale,
                innerPlateLength = nominal6p35.innerPlateLength * scale,
                jointOffset = Pitch3p75Inches * 0.5f
            };
        }

        private static ChainDimensions Nominal6p35() {
            return new ChainDimensions {
                pitch = Pitch6p35Inches,
                width = 0.125f,
                rollerDiameter = 0.130f,
                pinDiameter = 0.091f,
                plateThickness = 0.030f,
                plateHeight = 0.228f,
                outerPlateLength = 0.230f,
                innerPlateLength = 0.180f,
                jointOffset = 0.125f
            };
        }

        private static ChainDimensions Nominal9p79() {
            float scaleToNominalPitch = Pitch9p79Inches / 0.375f;
            return new ChainDimensions {
                pitch = Pitch9p79Inches,
                width = 0.188f * scaleToNominalPitch,
                rollerDiameter = 0.200f * scaleToNominalPitch,
                pinDiameter = 0.141f * scaleToNominalPitch,
                plateThickness = 0.045f * scaleToNominalPitch,
                plateHeight = 0.340f * scaleToNominalPitch,
                outerPlateLength = 0.330f * scaleToNominalPitch,
                innerPlateLength = 0.270f * scaleToNominalPitch,
                jointOffset = Pitch9p79Inches * 0.5f
            };
        }
    }
}
