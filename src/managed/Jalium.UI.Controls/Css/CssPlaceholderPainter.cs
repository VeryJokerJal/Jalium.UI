using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Uses the normal text renderer for authored placeholder fragments.</summary>
internal static class CssPlaceholderPainter
{
    private static CssPlaceholder.View? Prepare(FrameworkElement source, Control owner, string text, Brush fallback)
    {
        var view = CssPlaceholder.Prepare(source, owner, text, fallback);
        if (view is null) return null;
        view.TextWrapping = source is TextBox box ? box.TextWrapping : source is TextBlock block ? block.TextWrapping : TextWrapping.NoWrap;
        view.TextAlignment = source is TextBlock textBlock ? textBlock.TextAlignment : CssFlowProperties.ToNativeTextAlignment(CssFlowProperties.LineAlignment(owner, true), owner.FlowDirection == FlowDirection.RightToLeft);
        view.TextTrimming = source switch { TextBlock placeholder => placeholder.TextTrimming, TextBoxBase input => input.TextTrimming,
            PasswordBox password => password.TextTrimming, _ => TextTrimming.None };
        return view;
    }

    internal static bool Draw(Control source, DrawingContext context, string text, Brush fallback, Rect viewport,
        bool centered = false, double scrollX = 0, double scrollY = 0)
    {
        var owner = CssPlaceholder.AliasOwner(source) ?? source;
        var view = Prepare(source, owner, text, fallback);
        if (view is null) return false;
        DrawView(view, context, viewport, centered, scrollX, scrollY);
        return true;
    }

    internal static bool MeasureAlias(TextBlock source, Size available, out Size result)
    {
        result = default;
        var owner = CssPlaceholder.TextOwner(source);
        if (owner is null) return false;
        var view = Prepare(source, owner, source.Text, source.Foreground ?? Brushes.Gray);
        if (view is null) return false;
        view.Measure(available); result = view.DesiredSize; return true;
    }

    internal static bool DrawAlias(TextBlock source, DrawingContext context)
    {
        var owner = CssPlaceholder.TextOwner(source);
        if (owner is null) return false;
        var view = Prepare(source, owner, source.Text, source.Foreground ?? Brushes.Gray);
        if (view is null) return false;
        DrawView(view, context, new Rect(default, source.RenderSize), false, 0, 0);
        return true;
    }

    private static void DrawView(TextBlock view, DrawingContext context, Rect viewport, bool centered, double scrollX, double scrollY)
    {
        var width = Math.Max(0, viewport.Width);
        view.Measure(new(width, double.PositiveInfinity));
        var height = Math.Max(1, view.DesiredSize.Height);
        var usedWidth = Math.Min(width, Math.Ceiling(Math.Max(0, view.DesiredSize.Width)));
        var x = viewport.X + (view.TextAlignment == TextAlignment.Right ? width - usedWidth :
            view.TextAlignment == TextAlignment.Center ? (width - usedWidth) / 2 : 0) - Math.Round(scrollX);
        var y = viewport.Y + (centered ? (viewport.Height - height) / 2 : 0) - Math.Round(scrollY);
        view.Arrange(new(0, 0, usedWidth, height));
        if (view.Opacity <= 0) return;
        context.PushClip(new RectangleGeometry(viewport));
        // OnRender can itself be recorded as one control fragment. A temporary
        // ambient Offset is only proxied by that recorder; a transform survives
        // replay and keeps the hint in its field instead of at the fragment origin.
        context.PushTransform(new TranslateTransform(x, y));
        var translucent = view.Opacity < 1;
        if (translucent) context.PushOpacity(view.Opacity);
        try { view.Render(context); }
        finally
        {
            if (translucent) context.Pop();
            context.Pop(); context.Pop();
        }
    }
}
