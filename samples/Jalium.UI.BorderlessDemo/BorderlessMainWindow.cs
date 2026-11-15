using Jalium.UI.Controls;

namespace Jalium.UI.BorderlessDemo;

/// <summary>
/// 无边框 + 可切换全屏的主窗口演示。
/// </summary>
public class BorderlessMainWindow : Window
{
    public BorderlessMainWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        TitleBarStyle = WindowTitleBarStyle.Native;
        ResizeMode = ResizeMode.CanResize;
        Title = "Jalium.UI 无边框 / 全屏演示";
        Width = 820;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Style = Themes.BorderlessDemoTheme.WindowStyle;
        Content = new BorderlessDemoSurface { OwnerWindow = this };
    }
}
