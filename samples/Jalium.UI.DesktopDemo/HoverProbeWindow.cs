using Jalium.UI.DesktopDemo.Themes;
using Jalium.UI.Data;

namespace Jalium.UI.DesktopDemo;

// Keeps the original Mica hover/scroll reproduction in a native window.
internal static class HoverProbeWindow
{
    public static Window Build()
    {
        var surface = new HoverProbeSurface();
        var window = new Window { Width = 620, Height = 560, Style = HoverProbeTheme.WindowStyle, Content = surface };
        window.SetBinding(Window.TitleProperty, new Binding(nameof(HoverProbeSurface.Phase)) { Source = surface, StringFormat = "HoverProbe {0}" });
        return window;
    }
}
