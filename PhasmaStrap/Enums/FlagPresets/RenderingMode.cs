namespace PhasmaStrap.Enums.FlagPresets
{
    public enum RenderingMode
    {
        [EnumName(StaticName = "Automatic")]
        Default,
        [EnumName(StaticName = "Vulkan")]
        Vulkan,
        [EnumName(StaticName = "OpenGL")]
        OpenGL,
        [EnumName(StaticName = "Direct3D 11")]
        D3D11
    }
}
