using Jalium.UI.DesktopDemo.Themes;

namespace Jalium.UI.DesktopDemo;

internal static class ImagePerfWindow
{
    public static Window Build() => new() { Title = "Jalium.UI - Image 性能测试", Width = 1100, Height = 800, Style = ImagePerfTheme.WindowStyle, Content = new ImagePerfSurface() };
}
