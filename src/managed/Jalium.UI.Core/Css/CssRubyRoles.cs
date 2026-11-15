namespace Jalium.UI.Styling;

internal static class CssRubyRoles
{
    internal static bool IsPart(CssDisplayMode mode) => mode is CssDisplayMode.RubyBase or CssDisplayMode.RubyText
        or CssDisplayMode.RubyBaseContainer or CssDisplayMode.RubyTextContainer;
    internal static bool IsBox(CssDisplayMode mode) => mode == CssDisplayMode.Ruby || IsPart(mode);
    internal static CssDisplayMode Role(UIElement element) => element is FrameworkElement host ? host.CssDisplayMode : CssDisplayMode.Native;
}
