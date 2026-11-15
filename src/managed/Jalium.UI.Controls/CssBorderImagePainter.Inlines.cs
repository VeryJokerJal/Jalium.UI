using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal static partial class CssBorderImagePainter
{
    private static Axis VisibleTiles(Axis axis, double start, double end)
    {
        if (axis.Count == 0 || !(axis.Step > 0)) return default;
        // A saturated count represents a dense periodic pattern, not its end.
        // Keep its original phase and let the bounded sampler cover the visible part.
        if (axis.Count == int.MaxValue) return axis;
        var first = Math.Max(0, Math.Floor((start - axis.Start - axis.Tile) / axis.Step) + 1);
        var last = Math.Min(axis.Count, Math.Ceiling((end - axis.Start) / axis.Step));
        if (first >= last) return default;
        return new(axis.Start + first * axis.Step, axis.Tile, axis.Step, (int)Math.Min(int.MaxValue, last - first));
    }

    private static bool TryInlineFragment(FrameworkElement owner, Rect bounds, out CssInlineBoxFragment fragment)
    {
        if (owner.IsCssInlineFrame) return owner.CssInlineBox!.TryGetFragment(bounds, out fragment);
        if (owner is TextBlock { IsCssInlineBox: true } text) return text.TryGetCssDecorationFragment(bounds, out fragment);
        fragment = default;
        return false;
    }

    internal static void DecorationFragment(FrameworkElement visualOwner, DependencyObject styleOwner,
        DrawingContext context, CssInlineBoxFragment fragment, Thickness border)
    {
        if (HasTemplateRoute(visualOwner) && ReferenceEquals(visualOwner, styleOwner)) return;
        if (styleOwner.GetValue(CssBorderImageProperties.SourceProperty) is null) return;
        var sliced = fragment.DecorationWidth > 0;
        Rect? clip = null;
        if (sliced)
        {
            var outset = CssBorderImageProperties.Outset(styleOwner, border);
            var left = fragment.LeftEdge ? outset.Left : 0;
            var right = fragment.RightEdge ? outset.Right : 0;
            clip = new(fragment.Rect.X - left, fragment.Rect.Y - outset.Top,
                fragment.Rect.Width + left + right, fragment.Rect.Height + outset.Top + outset.Bottom);
            context.PushClip(new RectangleGeometry(clip.Value));
        }
        try
        {
            Draw(visualOwner, styleOwner, context, fragment.DecorationRect, border, true, clip);
            Draw(visualOwner, styleOwner, context, fragment.DecorationRect, border, false, clip);
        }
        finally { if (sliced) context.Pop(); }
    }
}
