namespace PhasmaStrap.Models
{
    // a known third-party tool PhasmaStrap can locate and launch alongside Roblox/Studio.
    // PhasmaStrap doesn't bundle or download these itself - the user points it at an
    // existing install, matching how Voidstrap's own extension manifests work (they only
    // check whether the native asset already exists locally, they don't fetch it)
    public class Extension
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Description { get; init; } = "";
        public string ExecutableName { get; init; } = "";
    }
}
