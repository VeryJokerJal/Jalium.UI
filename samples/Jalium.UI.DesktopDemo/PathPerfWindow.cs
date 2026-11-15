using Jalium.UI.DesktopDemo.Themes;

namespace Jalium.UI.DesktopDemo;

internal static class PathPerfWindow
{
    public static Window Build() => new() { Title = "Jalium.UI - Path 性能测试", Width = 1024, Height = 768, Style = PathPerfTheme.WindowStyle, Content = new PathPerfSurface() };
}
