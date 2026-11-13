using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.MillionScroll.Themes;

namespace Jalium.UI.MillionScroll;

public static class MillionScrollWindow
{
    public static Window Build(MillionScrollRunOptions options, RenderBackend actualBackend, RenderingEngine actualEngine)
    {
        var surface = new MillionScrollSurface(); surface.Configure(options, actualBackend, actualEngine);
        surface.ShutdownOnBenchmarkCompletion = true;
        return new Window
        {
            Title = "Jalium.UI Million Row Scroll Lab", Width = 1280, Height = 860,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Style = MillionScrollTheme.WindowStyle, Content = surface
        };
    }
}
