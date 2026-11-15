using Jalium.UI.DesktopDemo.Themes;

namespace Jalium.UI.DesktopDemo;

internal static class VelloTestWindow
{
    public static Window Build() => new() { Title = "Vello 0.10 验证", Width = 1040, Height = 720, Style = VelloTestTheme.WindowStyle, Content = new VelloTestSurface() };
}
