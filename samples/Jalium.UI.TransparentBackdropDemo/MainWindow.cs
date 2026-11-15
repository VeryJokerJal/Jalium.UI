using Jalium.UI.Controls;

namespace Jalium.UI.TransparentBackdropDemo;

/// <summary>Transparent native host; the reusable control owns the sample UI.</summary>
public sealed class MainWindow : Window
{
    public MainWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null;
        Title = "Jalium.UI 透明窗口 · 背景特效切换";
        Width = 880;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new TransparentBackdropSurface { OwnerWindow = this };
    }
}
