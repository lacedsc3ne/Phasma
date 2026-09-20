namespace PhasmaStrap.Enums
{
    public enum HomepageBackgroundMode
    {
        [EnumSort(Order = 1)]
        [EnumName(StaticName = "Off")]
        None,

        [EnumSort(Order = 2)]
        [EnumName(StaticName = "Solid color")]
        Solid,

        [EnumSort(Order = 3)]
        [EnumName(StaticName = "Gradient")]
        Gradient
    }
}
