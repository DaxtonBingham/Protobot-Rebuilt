using System;

namespace Protobot.UI {
    public static class PartBrowserCategories {
        public static readonly string[] Names = {
            "Game Elements", "Structure", "Motion", "Hardware", "Electronics", "Pneumatics"
        };

        public static string For(PartType part) {
            if (part.id.StartsWith("OV", StringComparison.Ordinal)) return Names[0];
            switch (part.id) {
                case "PUBA": case "BLOK": case "CGOL": case "LOAD": case "LGOL":
                case "FELD": case "RING": case "SAKE": case "DISC": return Names[0];
                case "SCRW": case "NUT": case "SNDF": case "WSHR": case "SPCR":
                case "HexNR": return Names[3];
                case "PNMT": case "TANK": return Names[5];
            }
            if (part.group == PartType.PartGroup.Electronics) return Names[4];
            if (part.group == PartType.PartGroup.Motion) return Names[2];
            return Names[1];
        }
    }
}
