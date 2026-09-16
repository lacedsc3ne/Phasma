namespace PhasmaStrap.Models.Persistable
{
    public class DistributionState
    {
        public string VersionGuid { get; set; } = string.Empty;

        public Dictionary<string, string> PackageHashes { get; set; } = new();

        public int Size { get; set; }

        public List<string> ModManifest { get; set; } = new();

        // relative paths (under Paths.Modifications) that were most recently materialized from
        // enabled Mod Management packages, so a disabled/removed/edited managed mod's files can be
        // cleaned up on the next apply even though they physically live in the shared flat mod
        // folder - see Bootstrapper.ApplyModifications and Utility.ManagedModStore
        public List<string> ManagedModManifest { get; set; } = new();
    }
}
