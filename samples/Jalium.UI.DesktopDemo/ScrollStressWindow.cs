using Jalium.UI.Controls;
using Jalium.UI.DesktopDemo.Themes;

namespace Jalium.UI.DesktopDemo;

internal static class ScrollStressWindow
{
    public static Window Build()
    {
        var surface = new ScrollStressSurface(); surface.LoadImages();
        return new Window { Title = "Jalium.UI - 滚动压力测试 (复现按次累积卡顿)", Width = 1280, Height = 880, Style = ScrollStressTheme.WindowStyle, Content = surface };
    }
}
