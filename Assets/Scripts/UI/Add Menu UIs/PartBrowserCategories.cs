using System;

namespace Protobot.UI {
    public static class PartBrowserCategories {
        const string Structure = "Structure";
        const string Motion = "Motion";
        const string Hardware = "Hardware";
        const string Electronics = "Electronics";
        const string Pneumatics = "Pneumatics";
        const string GameElements = "Game Elements";

        public static readonly string[] Names = {
            Structure, Motion, Hardware, Electronics, Pneumatics, GameElements
        };

        public static string For(PartType part) {
            if (part.id.StartsWith("OV", StringComparison.Ordinal)) return GameElements;
            switch (part.id) {
                case "PUBA": case "BLOK": case "CGOL": case "LOAD": case "LGOL":
                case "FELD": case "RING": case "SAKE": case "DISC": return GameElements;
                case "SCRW": case "NUT": case "SNDF": case "WSHR": case "SPCR":
                case "HexNR": return Hardware;
                case "PNMT": case "TANK": return Pneumatics;
            }
            if (part.group == PartType.PartGroup.Electronics) return Electronics;
            if (part.group == PartType.PartGroup.Motion) return Motion;
            return Structure;
        }
    }
}
