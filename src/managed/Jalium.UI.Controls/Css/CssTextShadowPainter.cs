using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Isolates one painted text fragment and its decorations for CSS shadows.</summary>
internal static class CssTextShadowPainter
{
    internal static Capture? Begin(DrawingContext context, DependencyObject source, Rect bounds)
        => Begin(context, CssTextShadowProperties.Value(source), bounds, shadowsOnly: false);

    internal static Capture? Begin(DrawingContext context, Effect? effect,
        Rect bounds, bool shadowsOnly)
    {
        if (effect?.HasEffect != true ||
            context is not IEffectDrawingContext { IsElementEffectCaptureEnabled: true } capture ||
            context is not IOffsetDrawingContext offset ||
            (shadowsOnly && !capture.SupportsCssTextShadowsOnly) ||
            bounds.Width <= 0 || bounds.Height <= 0)
            return null;
        var padding = effect.EffectPadding;
        var x = (float)Math.Floor(offset.Offset.X + bounds.X - padding.Left);
        var y = (float)Math.Floor(offset.Offset.Y + bounds.Y - padding.Top);
        var right = (float)Math.Ceiling(offset.Offset.X + bounds.Right + padding.Right);
        var bottom = (float)Math.Ceiling(offset.Offset.Y + bounds.Bottom + padding.Bottom);
        if (right <= x || bottom <= y) return null;
        capture.BeginEffectCapture(x, y, right - x, bottom - y);
        return new Capture(capture, effect, x, y, right - x, bottom - y, shadowsOnly);
    }

    internal readonly struct Capture(IEffectDrawingContext context, Effect effect,
        float x, float y, float width, float height, bool shadowsOnly)
    {
        internal void End()
        {
            context.EndEffectCapture();
            if (shadowsOnly)
                context.ApplyCssTextShadowsOnly(effect, x, y, width, height, x, y);
            else
                context.ApplyElementEffect(effect, x, y, width, height, x, y);
        }
    }
}
