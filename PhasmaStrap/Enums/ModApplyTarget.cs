namespace PhasmaStrap.Enums
{
    public enum ModApplyTarget
    {
        [EnumSort(Order = 1)]
        [EnumName(StaticName = "Player and Studio")]
        Both,

        [EnumSort(Order = 2)]
        [EnumName(StaticName = "Player only")]
        Player,

        [EnumSort(Order = 3)]
        [EnumName(StaticName = "Studio only")]
        Studio
    }
}
