namespace Jalium.UI.Styling;

/// <summary>Box edges shared by CSS formatters, while native dependency-property values keep their precedence.</summary>
internal static class CssBoxMetrics
{
    internal static Thickness Margin(FrameworkElement element, double containingWidth)
    {
        if (element.HasLocalOrAnimatedValue(FrameworkElement.MarginProperty) || element.CssLayout is not { HasMargin: true } state)
            return element.Margin;
        return new(state.MarginLeft.Resolve(containingWidth, 0), state.MarginTop.Resolve(containingWidth, 0),
            state.MarginRight.Resolve(containingWidth, 0), state.MarginBottom.Resolve(containingWidth, 0));
    }

    internal static Thickness ContentInsets(FrameworkElement element, double containingWidth)
    {
        var paddingProperty = CssDependencyPropertyLookup.Find(element.GetType(), "Padding");
        var padding = paddingProperty is not null && element.GetValue(paddingProperty) is Thickness nativePadding ? nativePadding : default;
        if (paddingProperty is null && element.CssLayout is { HasPadding: true } state)
            padding = new(Math.Max(0, state.PaddingLeft.Resolve(containingWidth, 0)), Math.Max(0, state.PaddingTop.Resolve(containingWidth, 0)),
                Math.Max(0, state.PaddingRight.Resolve(containingWidth, 0)), Math.Max(0, state.PaddingBottom.Resolve(containingWidth, 0)));
        var border = CssDependencyPropertyLookup.Find(element.GetType(), "BorderThickness") is { } borderProperty &&
            element.GetValue(borderProperty) is Thickness nativeBorder ? nativeBorder : default;
        return new(padding.Left + border.Left, padding.Top + border.Top, padding.Right + border.Right, padding.Bottom + border.Bottom);
    }

    internal static Size InnerSize(Size outer, Thickness insets) => new(
        Math.Max(0, outer.Width - insets.Left - insets.Right), Math.Max(0, outer.Height - insets.Top - insets.Bottom));
}
