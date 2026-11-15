using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    private sealed class InlinePaint(Inline source)
    {
        internal Inline Source => source;
        internal int Depth { get; } = InlineDepth(source);
        internal readonly Dictionary<int, List<Rect>> Lines = [];
        internal Rect? CellClip;
        internal CssInlineBoxFragment[] Fragments = [];
    }

    private static int InlineDepth(Inline source)
    {
        var depth = 0;
        for (var parent = source.Parent as Inline; parent is not null; parent = parent.Parent as Inline) depth++;
        return depth;
    }

    private List<InlinePaint> DocumentInlinePaints(FlowDocumentLayoutInfo layout)
    {
        if (layout.InlinePaints is { } cached) return cached;
        var lines = new List<(LineLayoutInfo line, double y, double x)>();
        var y = 0d;
        CollectAllLines(layout.Blocks, 0, ref y, lines);
        var paints = new Dictionary<Inline, InlinePaint>(ReferenceEqualityComparer.Instance);
        Dictionary<int, Inline>? objects = null;
        for (var index = 0; index < lines.Count; index++)
        {
            var entry = lines[index];
            foreach (var run in entry.line.Runs)
            {
                Inline? inline = run.Run;
                inline ??= (objects ??= BuildInlineBackgroundObjectSources()).GetValueOrDefault(run.StartOffset);
                if (inline is null || !(run.Width > 0)) continue;
                var rect = new Rect(entry.x + run.X, entry.y, run.Width, entry.line.Height);
                for (Inline? source = inline; source is not null; source = source.Parent as Inline)
                {
                    if (source.Background is null && source.GetValue(CssBorderImageProperties.SourceProperty) is null) continue;
                    if (!paints.TryGetValue(source, out var paint))
                    {
                        paints.Add(source, paint = new(source));
                        if (layout.CellClips.TryGetValue(entry.line, out var clip)) paint.CellClip = clip;
                    }
                    if (!paint.Lines.TryGetValue(index, out var pieces)) paint.Lines.Add(index, pieces = []);
                    pieces.Add(rect);
                }
            }
        }
        foreach (var paint in paints.Values)
        {
            var rtl = CssNode.Get(paint.Source).FrameworkParent?.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft;
            var clone = CssBoxDecorationBreakProperties.Value(paint.Source) == CssBoxDecorationBreak.Clone;
            var ordered = new List<Rect>();
            foreach (var pieces in paint.Lines.OrderBy(pair => pair.Key).Select(pair => pair.Value))
            {
                // A source can occupy several separated visual ranges on one bidi line.
                // Join adjacent runs within the source, but leave intervening siblings clear.
                pieces.Sort(static (a, b) => a.X.CompareTo(b.X));
                var merged = new List<Rect>();
                foreach (var rect in pieces)
                {
                    if (merged.Count > 0 && rect.Left <= merged[^1].Right + .01)
                    {
                        var previous = merged[^1];
                        merged[^1] = new(previous.X, previous.Y, Math.Max(previous.Right, rect.Right) - previous.X, previous.Height);
                    }
                    else merged.Add(rect);
                }
                if (rtl) merged.Reverse();
                ordered.AddRange(merged);
            }
            paint.Fragments = CssInlineBoxFragment.Join(ordered.Select((rect, index) => new CssInlineBoxFragment(rect,
                clone || (rtl ? index == ordered.Count - 1 : index == 0),
                clone || (rtl ? index == 0 : index == ordered.Count - 1))).ToArray(), clone, rtl);
        }
        return layout.InlinePaints = paints.Values.OrderBy(paint => paint.Depth).ToList();
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

    private void DrawDocumentInlineBackgrounds(DrawingContext context, FlowDocumentLayoutInfo layout, Rect contentBounds)
    {
        if (MacOSWritingToolsPresentation.IsPreviewing(this)) return;
        foreach (var paint in DocumentInlinePaints(layout))
        {
            if (paint.CellClip is { } cell)
                context.PushClip(new RectangleGeometry(new Rect(cell.X + contentBounds.Left - _horizontalOffset,
                    cell.Y + contentBounds.Top - _verticalOffset, cell.Width, cell.Height)));
            try
            {
            var outset = CssBorderImageProperties.Outset(paint.Source, default);
            foreach (var piece in paint.Fragments)
            {
                var fragment = ToViewport(piece, contentBounds);
                var rect = fragment.Rect;
                var ink = new Rect(rect.X - outset.Left, rect.Y - outset.Top,
                    rect.Width + outset.Left + outset.Right, rect.Height + outset.Top + outset.Bottom);
                if (!ink.IntersectsWith(contentBounds)) continue;
                if (paint.Source.Background is { } brush) context.DrawRectangle(brush, null, rect);
                CssBorderImagePainter.DecorationFragment(this, paint.Source, context, fragment, default);
            }
            }
            finally { if (paint.CellClip is not null) context.Pop(); }
        }
    }

    internal CssInlineBoxFragment[] GetDocumentDecorationFragments(Inline source)
    {
        var bounds = GetContentBounds();
        return EnsureLayout(bounds.Width) is { } layout
            ? DocumentInlinePaints(layout).FirstOrDefault(paint => ReferenceEquals(paint.Source, source))?.Fragments.Select(piece => ToViewport(piece, bounds)).ToArray() ?? [] : [];
    }

    private CssInlineBoxFragment ToViewport(CssInlineBoxFragment piece, Rect bounds) => piece with
    { Rect = new(piece.Rect.X + bounds.Left - _horizontalOffset, piece.Rect.Y + bounds.Top - _verticalOffset, piece.Rect.Width, piece.Rect.Height) };
}
