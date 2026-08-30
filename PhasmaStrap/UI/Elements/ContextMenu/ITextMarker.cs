using System.Windows.Media;

// Ported from Voidstrap's UI/Elements/ContextMenu/ITextMarker.cs.
namespace PhasmaStrap.UI.Elements.ContextMenu
{
    public interface ITextMarker
    {
        int StartOffset { get; }

        int Length { get; }

        Color? BackgroundColor { get; set; }

        Color? ForegroundColor { get; set; }

        string? ToolTip { get; set; }
    }
}
