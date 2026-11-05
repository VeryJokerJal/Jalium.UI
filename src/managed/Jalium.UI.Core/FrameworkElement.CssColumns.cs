using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI;

public partial class FrameworkElement
{
    internal CssColumnBox? CssColumnBox;
    internal volatile int CssColumnInkPadding;
    internal bool IsCssColumnFrame => CssColumnBox?.IsActive == true;
    internal bool CanFragmentCssColumns => SupportsCssInlineContentLayout && LayoutTransform is null && RenderTransform is null &&
        !CssReplacedContent.IsActive(this) &&
        CssDisplayMode is CssDisplayMode.Native or CssDisplayMode.Block &&
        (this is Controls.TextBlock || CssDisplayLayout.IsFlow(this));
}

/// <summary>A source visual keeps its parent and owns the union of its column fragments.</summary>
internal interface ICssColumnLayout
{
    bool IsApplied { get; }
    bool Contains(FrameworkElement source);
}

internal sealed class CssColumnBox(FrameworkElement source, ICssColumnLayout layout, Size naturalSize,
    Size size, CssColumnPiece[] pieces, Rect[]? clipRegions = null, CssInlineBoxFragment[]? inlineFragments = null)
{
    private readonly Visual? _parent = source.VisualParent;
    private readonly WeakReference<ICssColumnLayout> _layout = new(layout);
    internal ICssColumnLayout? Layout => _layout.TryGetTarget(out var current) ? current : null;
    internal Size NaturalSize => naturalSize;
    internal Size Size => size;
    internal CssColumnPiece[] Pieces => pieces;
    internal CssInlineBoxFragment[]? InlineFragments => inlineFragments;
    internal double InkPadding { get; } = ComputeInkPadding(clipRegions ?? pieces.Select(piece => piece.Destination), size);
    internal bool IsActive => ReferenceEquals(source.CssColumnBox, this) && ReferenceEquals(source.VisualParent, _parent) &&
        _layout.TryGetTarget(out var current) && current.IsApplied && current.Contains(source);
    internal Geometry Clip { get; } = ClipGeometry(clipRegions ?? pieces.Select(piece => piece.Destination));

    private static double ComputeInkPadding(IEnumerable<Rect> rectangles, Size size)
    {
        var padding = 0d;
        foreach (var rect in rectangles)
            padding = Math.Max(padding, Math.Max(Math.Max(-rect.Left, -rect.Top), Math.Max(rect.Right - size.Width, rect.Bottom - size.Height)));
        return padding;
    }

    private static Geometry ClipGeometry(IEnumerable<Rect> rectangles)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var rect in rectangles) if (rect.Width > 0 && rect.Height > 0) group.Children.Add(new RectangleGeometry(rect));
        group.Freeze(); return group;
    }
}

internal readonly record struct CssColumnPiece(Rect Source, Rect Destination);
