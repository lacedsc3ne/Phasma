namespace PhasmaStrap.Enums.FlagPresets
{
    public enum LightingMode
    {
        [EnumName(StaticName = "Automatic")]
        Default,
        [EnumName(StaticName = "Voxel")]
        Voxel,
        [EnumName(StaticName = "ShadowMap")]
        ShadowMap,
        [EnumName(StaticName = "Future")]
        Future,
        [EnumName(StaticName = "Unified (Phase 4)")]
        Unified
    }
}
