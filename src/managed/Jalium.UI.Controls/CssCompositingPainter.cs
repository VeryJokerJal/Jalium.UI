using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal static class CssCompositingPainter
{
    internal static CssBlendGroup.Frame? Begin(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement { Visibility: Visibility.Visible } owner ||
            CssContentsLayout.IsContents(owner) || CssContentsLayout.Suppressed(owner) || !CssCompositingProperties.CreatesGroup(owner) ||
            context is not IOffsetDrawingContext offset) return null;
        var bounds = CssMaskPainter.InkBounds(owner);
        if (CssMaskPainter.InlineFragments(owner) is { } fragments)
        {
            foreach (var fragment in fragments)
                bounds.Union(fragment.Rect);
        }
        if (!(bounds.Width > 0 && bounds.Height > 0)) return null;
        bounds.Offset(offset.Offset.X, offset.Offset.Y);
        return CssBlendGroup.Begin(context, bounds, CssCompositingProperties.Mix(owner), CssCompositingProperties.Isolate(owner) ? "isolation" : "mix-blend-mode");
    }
}
