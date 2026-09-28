using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssOverflowMode : byte
{
    Visible,
    Clip,
    Hidden,
    Auto,
    Scroll,
}

/// <summary>Computed two-axis overflow, retained separately from the native clip properties.</summary>
internal sealed record CssOverflowValue(CssOverflowMode X, CssOverflowMode Y)
{
    internal static CssOverflowValue Compute(CssOverflowMode x, CssOverflowMode y)
    {
        // A scrollable axis promotes visible to auto and clip to hidden on the other axis.
        if (x is not (CssOverflowMode.Visible or CssOverflowMode.Clip) ||
            y is not (CssOverflowMode.Visible or CssOverflowMode.Clip))
        {
            if (x == CssOverflowMode.Visible) x = CssOverflowMode.Auto;
            else if (x == CssOverflowMode.Clip) x = CssOverflowMode.Hidden;
            if (y == CssOverflowMode.Visible) y = CssOverflowMode.Auto;
            else if (y == CssOverflowMode.Clip) y = CssOverflowMode.Hidden;
        }
        return new(x, y);
    }

    internal ClipEdges Edges =>
        (X == CssOverflowMode.Visible ? ClipEdges.None : ClipEdges.Left | ClipEdges.Right) |
        (Y == CssOverflowMode.Visible ? ClipEdges.None : ClipEdges.Top | ClipEdges.Bottom);
}

internal sealed record CssOverflowClipMargin(CssBackgroundBox Box, double Length)
{
    internal static readonly CssOverflowClipMargin Initial = new(CssBackgroundBox.Padding, 0);

    internal Rect Apply(Rect outer, Thickness border, Thickness padding, out Thickness outsets)
    {
        var inset = Box switch
        {
            CssBackgroundBox.Border => default,
            CssBackgroundBox.Content => new Thickness(
                Math.Max(0, border.Left + padding.Left), Math.Max(0, border.Top + padding.Top),
                Math.Max(0, border.Right + padding.Right), Math.Max(0, border.Bottom + padding.Bottom)),
            _ => new Thickness(Math.Max(0, border.Left), Math.Max(0, border.Top),
                Math.Max(0, border.Right), Math.Max(0, border.Bottom)),
        };
        outsets = new Thickness(Length - inset.Left, Length - inset.Top,
            Length - inset.Right, Length - inset.Bottom);
        var left = outer.Left - outsets.Left;
        var top = outer.Top - outsets.Top;
        return new Rect(left, top,
            Math.Max(0, outer.Width + outsets.Left + outsets.Right),
            Math.Max(0, outer.Height + outsets.Top + outsets.Bottom));
    }
}

internal static class CssOverflowProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssOverflow", typeof(CssOverflowValue), typeof(CssOverflowProperties), new PropertyMetadata(null));

    internal static readonly DependencyProperty ClipMarginProperty = DependencyProperty.RegisterAttached(
        "CssOverflowClipMargin", typeof(CssOverflowClipMargin), typeof(CssOverflowProperties),
        new PropertyMetadata(null, static (target, _) =>
        {
            if (target is UIElement element) element.InvalidateVisual();
        }));

    internal static CssOverflowValue? Get(DependencyObject target)
        => target.GetEffectiveValueLayer(ValueProperty) is
            (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)
            ? target.GetValue(ValueProperty) as CssOverflowValue : null;

    internal static CssOverflowClipMargin? ClipMargin(DependencyObject target)
        => target.GetEffectiveValueLayer(ClipMarginProperty) is
            (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)
            ? target.GetValue(ClipMarginProperty) as CssOverflowClipMargin : null;

    internal static bool TryGetActiveClipMargin(UIElement target, out CssOverflowClipMargin margin)
    {
        margin = CssOverflowClipMargin.Initial;
        var overflow = Get(target);
        if (overflow is null || overflow.X != CssOverflowMode.Clip && overflow.Y != CssOverflowMode.Clip ||
            !target.ClipToBounds || target.ClipToBoundsEdges == ClipEdges.None ||
            target.GetEffectiveValueLayer(UIElement.ClipToBoundsProperty) is not
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState) ||
            target.GetEffectiveValueLayer(UIElement.ClipToBoundsEdgesProperty) is not
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
            return false;
        margin = ClipMargin(target) ?? margin;
        return true;
    }
}
