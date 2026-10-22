using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    private sealed class InlineBackgroundPaint(Inline source, Rect? cellClip)
    {
        internal Inline Source { get; } = source;
        internal Rect? CellClip { get; } = cellClip;
        internal int Depth { get; } = BackgroundDepth(source);
        internal SortedDictionary<int, List<Rect>> Lines { get; } = [];
    }

    private static int BackgroundDepth(Inline inline)
    {
        int depth = 0;
        for (var parent = inline.Parent as Inline; parent is not null; parent = parent.Parent as Inline) ++depth;
        return depth;
    }

    private Dictionary<int, Inline> BuildInlineBackgroundObjectSources()
    {
        var result = new Dictionary<int, Inline>();
        void Add(Inline inline)
        {
            if (inline is Span span)
                foreach (var child in span.Inlines) Add(child);
            else if (inline is InlineUIContainer or LineBreak)
                result[inline.ContentStart.DocumentOffset] = inline;
        }
        foreach (var paragraph in _document.EnumerateParagraphs())
            foreach (var inline in paragraph.Inlines) Add(inline);
        return result;
    }

    private void DrawDocumentInlineBackgrounds(DrawingContext context, FlowDocumentLayoutInfo layout, Rect viewport)
    {
        // Animation previews contain glyph ink; document surfaces stay in the editor.
        if (MacOSWritingToolsPresentation.IsPreviewing(this)) return;
        var lines = new List<(LineLayoutInfo line, double y, double x)>();
        double y = 0;
        CollectAllLines(layout.Blocks, 0, ref y, lines);
        var paints = new Dictionary<Inline, InlineBackgroundPaint>(ReferenceEqualityComparer.Instance);
        Dictionary<int, Inline>? objects = null;
        for (int index = 0; index < lines.Count; ++index)
        {
            var entry = lines[index];
            foreach (var run in entry.line.Runs)
            {
                if (!(run.Width > 0) || !(entry.line.Height > 0)) continue;
                // Shared insertion boundaries can belong to either adjacent Run. Resolve non-text spans by node identity.
                Inline? inline = run.Run;
                inline ??= (objects ??= BuildInlineBackgroundObjectSources()).GetValueOrDefault(run.StartOffset);
                var bounds = new Rect(entry.x + run.X, entry.y, run.Width, entry.line.Height);
                for (var source = inline; source is not null; source = source.Parent as Inline)
                {
                    if (source.Background is null) continue;
                    if (!paints.TryGetValue(source, out var paint))
                    {
                        var clip = layout.CellClips.TryGetValue(entry.line, out var cell) ? cell : (Rect?)null;
                        paints.Add(source, paint = new(source, clip));
                    }
                    if (!paint.Lines.TryGetValue(index, out var pieces)) paint.Lines.Add(index, pieces = []);
                    pieces.Add(bounds);
                }
            }
        }

        foreach (var paint in paints.Values.OrderBy(item => item.Depth))
        {
            Rect ToViewport(Rect rect) => new(rect.X + viewport.Left - _horizontalOffset,
                rect.Y + viewport.Top - _verticalOffset, rect.Width, rect.Height);
            if (paint.CellClip is { } cell) context.PushClip(new RectangleGeometry(ToViewport(cell)));
            try
            {
                foreach (var pieces in paint.Lines.Values)
                {
                    pieces.Sort(static (a, b) => a.X.CompareTo(b.X));
                    // Merge touching visual fragments once, preserving separated bidi ranges.
                    var merged = new List<Rect>();
                    foreach (var piece in pieces)
                    {
                        if (merged.Count > 0 && piece.Left <= merged[^1].Right + .01)
                        {
                            var previous = merged[^1];
                            merged[^1] = new(previous.X, previous.Y, Math.Max(previous.Right, piece.Right) - previous.X, previous.Height);
                        }
                        else merged.Add(piece);
                    }
                    foreach (var piece in merged)
                    {
                        var rect = ToViewport(piece);
                        if (rect.IntersectsWith(viewport)) context.DrawRectangle(paint.Source.Background, null, rect);
                    }
                }
            }
            finally { if (paint.CellClip is not null) context.Pop(); }
        }
    }
}
