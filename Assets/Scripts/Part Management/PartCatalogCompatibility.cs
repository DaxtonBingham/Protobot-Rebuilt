namespace Protobot {
    /// <summary>Save identifiers that changed between the fork and upstream catalogs.</summary>
    public static class PartCatalogCompatibility {
        public static string CanonicalId(string id) {
            switch (id) {
                // The old catalog used RAIL for both structural and linear rails.
                // Only these two structural variants moved to RAILS upstream.
                case "RAIL-2x25": return "RAILS-2x25";
                case "RAIL-2x35": return "RAILS-2x35";
                case "OVTO": return "OVTO-Red side";
                default: return id;
            }
        }
    }
}
