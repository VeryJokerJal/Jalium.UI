using Jalium.UI.Styling;
using Jalium.UI.Media;

namespace Jalium.UI;

public partial class FrameworkElement
{
    /// <summary>
    /// Opts a content host into non-atomic CSS inline formatting. Its paint
    /// implementation must draw the allocated fragments instead of one box.
    /// </summary>
    protected virtual bool SupportsCssInlineContentLayout => false;

    internal bool CanFragmentCssInlineContent => SupportsCssInlineContentLayout &&
        CssInlineOuter && CssDisplayMode is CssDisplayMode.Native or CssDisplayMode.Block or CssDisplayMode.Ruby &&
        LayoutTransform is null;
    internal CssInlineBoxAllocation? CssInlineBox;
    internal bool IsCssInlineFrame => CssInlineBox?.IsActive == true;
    internal bool CssInlineFrameContains(Point point) => !IsCssInlineFrame ||
        CssInlineBox!.Fragments.Any(fragment => CssRoundedRectangleGeometry.Contains(
            fragment.Rect, fragment.Radii(this), point));

    internal void ReleaseCssInlineFrame(FrameworkElement owner)
    {
        if (CssInlineBox?.Owner != owner) return;
        CssInlineBox = null;
        if (CssInlineLayoutOwner == owner) CssInlineLayoutOwner = null;
        InvalidateCssAllocation();
        InvalidateVisual();
    }
}

internal readonly record struct CssInlineBoxFragment(Rect Rect, bool LeftEdge, bool RightEdge,
    double DecorationOffset = 0, double DecorationWidth = 0, CssWritingMode WritingMode = CssWritingMode.HorizontalTb)
{
    internal Rect DecorationRect => DecorationWidth > 0
        ? WritingMode == CssWritingMode.SidewaysLr
            ? new(Rect.X, Rect.Bottom + DecorationOffset - DecorationWidth, Rect.Width, DecorationWidth)
            : CssWritingModeProperties.IsVertical(WritingMode)
                ? new(Rect.X, Rect.Y - DecorationOffset, Rect.Width, DecorationWidth)
                : new(Rect.X - DecorationOffset, Rect.Y, DecorationWidth, Rect.Height) : Rect;

    // Join the used, justified boxes rather than remeasuring their source text.
    // Each piece keeps its own block-axis extent (CSS Break 3, section 5.4.1).
    internal static CssInlineBoxFragment[] Join(CssInlineBoxFragment[] fragments, bool clone, bool rightToLeft)
    {
        if (clone || fragments.Length < 2) return fragments;
        var vertical = CssWritingModeProperties.IsVertical(fragments[0].WritingMode);
        var width = fragments.Sum(fragment => vertical ? fragment.Rect.Height : fragment.Rect.Width);
        if (!(width > 0 && double.IsFinite(width))) return fragments;
        var offset = rightToLeft ? width : 0;
        for (var index = 0; index < fragments.Length; index++)
        {
            var fragment = fragments[index];
            var advance = vertical ? fragment.Rect.Height : fragment.Rect.Width;
            if (rightToLeft) offset -= advance;
            fragments[index] = fragment with { DecorationOffset = offset, DecorationWidth = width };
            if (!rightToLeft) offset += advance;
        }
        return fragments;
    }

    internal Thickness Edges(Thickness edges)
    {
        var axes = new CssWritingAxes(WritingMode);
        var logical = axes.Logical(edges);
        return axes.Physical(new Thickness(LeftEdge ? logical.Left : 0, logical.Top,
            RightEdge ? logical.Right : 0, logical.Bottom));
    }
    internal CssUsedBorderRadii Radii(FrameworkElement element)
    {
        var radii = CssBorderRadiusProperties.Get(element)?.Resolve(Rect.Size) ??
            (element is Controls.Border border ? Controls.CssBackgroundPainter.CircularRadii(border.CornerRadius).Normalize(Rect.Size) : default);
        if (CssWritingModeProperties.IsVertical(WritingMode))
        {
            var top = WritingMode == CssWritingMode.SidewaysLr ? RightEdge : LeftEdge;
            var bottom = WritingMode == CssWritingMode.SidewaysLr ? LeftEdge : RightEdge;
            return new(top ? radii.TopLeft : default, top ? radii.TopRight : default,
                bottom ? radii.BottomRight : default, bottom ? radii.BottomLeft : default);
        }
        return new(LeftEdge ? radii.TopLeft : default, RightEdge ? radii.TopRight : default,
            RightEdge ? radii.BottomRight : default, LeftEdge ? radii.BottomLeft : default);
    }
}

/// <summary>Local fragments refer back to an unchanged native visual tree.</summary>
internal sealed class CssInlineBoxAllocation(FrameworkElement element, FrameworkElement owner,
    Func<bool> active, Size size, CssInlineBoxFragment[] fragments, double firstBaseline, double lastBaseline)
{
    private readonly Visual? _parent = element.VisualParent;
    internal FrameworkElement Owner => owner;
    internal Size Size => size;
    internal CssInlineBoxFragment[] Fragments { get; } = CssInlineBoxFragment.Join(fragments,
        CssBoxDecorationBreakProperties.Value(element) == CssBoxDecorationBreak.Clone,
        element.FrameworkParent is { } parent && CssWritingAxes.UsedDirection(parent) == FlowDirection.RightToLeft);
    private Dictionary<Rect, CssInlineBoxFragment>? _byRect;
    internal bool TryGetFragment(Rect rect, out CssInlineBoxFragment fragment)
    {
        if (_byRect is null)
        {
            _byRect = [];
            foreach (var piece in Fragments) _byRect[piece.Rect] = piece;
        }
        return _byRect.TryGetValue(rect, out fragment);
    }
    internal double FirstBaseline => firstBaseline;
    internal double LastBaseline => lastBaseline;
    internal bool IsActive => ReferenceEquals(element.CssInlineBox, this) &&
        ReferenceEquals(element.VisualParent, _parent) && element.CanFragmentCssInlineContent && active();
}
