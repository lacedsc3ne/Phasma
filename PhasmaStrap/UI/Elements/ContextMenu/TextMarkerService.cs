using System.Windows;
using System.Windows.Media;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

// Ported from Voidstrap's UI/Elements/ContextMenu/TextMarkerService.cs.
// An AvalonEdit background renderer + line colorizer that draws a background/foreground over
// arbitrary text ranges. BootstrapperEditorWindow uses it to highlight the lines that differ
// from the last-saved baseline (see LineDiff).
namespace PhasmaStrap.UI.Elements.ContextMenu
{
    public class TextMarkerService : DocumentColorizingTransformer, IBackgroundRenderer, ITextMarkerService
    {
        private class TextMarker : ITextMarker
        {
            public int StartOffset { get; set; }

            public int Length { get; set; }

            public Color? BackgroundColor { get; set; }

            public Color? ForegroundColor { get; set; }

            public string? ToolTip { get; set; }
        }

        private readonly TextDocument _document;

        private readonly List<TextMarker> _markers = new();

        public IEnumerable<ITextMarker> TextMarkers => _markers;

        public KnownLayer Layer => KnownLayer.Selection;

        public TextMarkerService(TextDocument document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public ITextMarker Create(int startOffset, int length)
        {
            TextMarker textMarker = new()
            {
                StartOffset = startOffset,
                Length = length
            };
            _markers.Add(textMarker);
            return textMarker;
        }

        public void RemoveAll(Predicate<ITextMarker> predicate)
        {
            _markers.RemoveAll(m => predicate((ITextMarker)m));
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_markers.Count == 0)
                return;

            foreach (TextMarker marker in _markers)
            {
                TextSegment segment = new()
                {
                    StartOffset = marker.StartOffset,
                    Length = marker.Length
                };

                foreach (Rect item in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                    drawingContext.DrawRectangle(new SolidColorBrush(marker.BackgroundColor ?? Colors.Transparent), null, item);
            }
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            foreach (TextMarker marker in _markers)
            {
                if (line.EndOffset >= marker.StartOffset && line.Offset <= marker.StartOffset + marker.Length && marker.ForegroundColor.HasValue)
                {
                    ChangeLinePart(Math.Max(line.Offset, marker.StartOffset), Math.Min(line.EndOffset, marker.StartOffset + marker.Length), element =>
                    {
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(marker.ForegroundColor!.Value));
                    });
                }
            }
        }
    }
}
