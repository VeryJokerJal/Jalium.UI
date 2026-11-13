using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private DependencyObject TextWritingSource => DocumentFontSource ?? this;
    private CssWritingMode TextWritingMode => _cssInlineParagraph?.WritingMode ?? CssWritingModeProperties.GetWritingMode(TextWritingSource);
    // Shared sources retain logical text coordinates inside physical allocations.
    // Only the owning formatter converts element slots and decoration rectangles.
    private bool IsVerticalTextLayout => CssWritingModeProperties.IsVertical(TextWritingMode) &&
        !HasCssInlineBoxes && !HasCssColumnText;
    private bool UsesVerticalTypography => IsVerticalTextLayout &&
        TextWritingMode is CssWritingMode.VerticalRl or CssWritingMode.VerticalLr;
    private bool UsesVerticalGlyphForms => UsesVerticalTypography &&
        CssWritingModeProperties.GetTextOrientation(TextWritingSource) != CssTextOrientation.Sideways;
    private bool UsesCentralTextBaseline => UsesVerticalTypography &&
        CssWritingModeProperties.GetTextOrientation(_cssInlineParagraph?.Owner ?? TextWritingSource) != CssTextOrientation.Sideways;

    private bool UsesUprightGlyphForms => UsesVerticalGlyphForms &&
        CssWritingModeProperties.GetTextOrientation(TextWritingSource) == CssTextOrientation.Upright;
    private FlowDirection UsedTextFlowDirection => UsesUprightGlyphForms ? FlowDirection.LeftToRight : FlowDirection;

    private Size LogicalTextSize(Size physical) => IsVerticalTextLayout
        ? new Size(physical.Height, physical.Width) : physical;
    private Size PhysicalTextSize(Size logical) => LogicalTextSize(logical);

    private Thickness LogicalTextInsets(Thickness physical) => !IsVerticalTextLayout ? physical : TextWritingMode switch
    {
        CssWritingMode.VerticalLr => new(physical.Top, physical.Left, physical.Bottom, physical.Right),
        CssWritingMode.SidewaysLr => new(physical.Bottom, physical.Left, physical.Top, physical.Right),
        _ => new(physical.Top, physical.Right, physical.Bottom, physical.Left),
    };

    private Matrix TextWritingMatrix(double top, double height) => !IsVerticalTextLayout ? Matrix.Identity : TextWritingMode switch
    {
        CssWritingMode.VerticalLr => new(0, 1, -1, 0, 2 * top + height, 0),
        CssWritingMode.SidewaysLr => new(0, -1, 1, 0, 0, PhysicalTextContentSize.Height),
        _ => new(0, 1, -1, 0, PhysicalTextContentSize.Width, 0),
    };

    private readonly struct TextWritingScope(DrawingContext? context) : IDisposable
    {
        public void Dispose() => context?.Pop();
    }

    private TextWritingScope PushTextWritingLine(DrawingContext context, double top, double height)
    {
        if (!IsVerticalTextLayout) return default;
        context.PushTransform(new MatrixTransform(TextWritingMatrix(top, height)));
        return new(context);
    }

    private static Rect TextMatrixBounds(Matrix matrix, Rect rectangle)
    {
        var a = matrix.Transform(rectangle.TopLeft); var b = matrix.Transform(rectangle.TopRight);
        var c = matrix.Transform(rectangle.BottomLeft); var d = matrix.Transform(rectangle.BottomRight);
        var left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        var top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        return new(left, top, Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) - left,
            Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) - top);
    }

    private Rect TextWritingRect(Rect logical, double top, double height)
        => IsVerticalTextLayout ? TextMatrixBounds(TextWritingMatrix(top, height), logical) : logical;

    private Rect TextWritingClip(Rect logical)
    {
        if (!IsVerticalTextLayout) return logical;
        // Bounds may be reflected when columns progress rightwards. Glyphs
        // always use the per-column rotation above and are never mirrored.
        var matrix = TextWritingMode == CssWritingMode.VerticalLr
            ? new Matrix(0, 1, 1, 0, 0, 0) : TextWritingMatrix(0, 0);
        return TextMatrixBounds(matrix, logical);
    }

    private Point LogicalTextPoint(Point physical)
    {
        if (!IsVerticalTextLayout) return physical;
        if (TextWritingMode == CssWritingMode.SidewaysLr)
            return new(PhysicalTextContentSize.Height - physical.Y, physical.X);
        if (TextWritingMode != CssWritingMode.VerticalLr)
            return new(physical.Y, PhysicalTextContentSize.Width - physical.X);
        var top = GetVerticalContentOffset(GetLineHeight());
        var bestTop = top; var bestHeight = 0d; var nearest = double.PositiveInfinity;
        foreach (var line in _layoutLines)
        {
            var current = TextLineTop(line, top); top += line.Height;
            var distance = physical.X < current ? current - physical.X : Math.Max(0, physical.X - current - line.Height);
            if (distance < nearest) { nearest = distance; bestTop = current; bestHeight = line.Height; }
        }
        return new(physical.Y, 2 * bestTop + bestHeight - physical.X);
    }

    internal readonly record struct SourceBaselineGeometry(Point Start, Point End, Rect Bounds);

    internal IReadOnlyList<SourceBaselineGeometry> GetSourceRangeGeometry(int start, int length)
    {
        var segments = GetLogicalSourceRangeSegments(start, length);
        var result = new List<SourceBaselineGeometry>(segments.Count);
        var offset = TextContentOffset;
        var rows = new Dictionary<double, (double Top, double Height)>();
        var y = GetVerticalContentOffset(GetLineHeight());
        foreach (var line in _layoutLines)
        {
            var top = TextLineTop(line, y); y += line.Height;
            rows.TryAdd(Math.Round(top + line.Baseline, 6), (top, line.Height));
        }
        foreach (var segment in segments)
        {
            var row = rows.GetValueOrDefault(Math.Round(segment.Baseline - offset.Y, 6),
                (GetVerticalContentOffset(GetLineHeight()), GetLineHeight()));
            var bestTop = row.Item1; var bestHeight = row.Item2;
            var matrix = TextWritingMatrix(bestTop, bestHeight);
            var a = matrix.Transform(new Point(segment.StartX - offset.X, segment.Baseline - offset.Y));
            var b = matrix.Transform(new Point(segment.EndX - offset.X, segment.Baseline - offset.Y));
            var bounds = TextMatrixBounds(matrix, new Rect(segment.StartX - offset.X, bestTop,
                Math.Max(0, segment.EndX - segment.StartX), bestHeight));
            bounds.Offset(offset.X, offset.Y);
            result.Add(new(new(a.X + offset.X, a.Y + offset.Y), new(b.X + offset.X, b.Y + offset.Y), bounds));
        }
        return result;
    }

    internal IReadOnlyList<(double StartX, double EndX, double Baseline)> GetSourceRangeSegments(int start, int length)
    {
        if (!IsVerticalTextLayout) return GetLogicalSourceRangeSegments(start, length);
        return GetSourceRangeGeometry(start, length).Select(segment =>
            (segment.Bounds.Left, segment.Bounds.Right, segment.Start.Y)).ToArray();
    }
}
