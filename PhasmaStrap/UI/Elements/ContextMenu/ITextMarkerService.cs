// Ported from Voidstrap's UI/Elements/ContextMenu/ITextMarkerService.cs.
namespace PhasmaStrap.UI.Elements.ContextMenu
{
    public interface ITextMarkerService
    {
        IEnumerable<ITextMarker> TextMarkers { get; }

        ITextMarker Create(int startOffset, int length);

        void RemoveAll(Predicate<ITextMarker> predicate);
    }
}
