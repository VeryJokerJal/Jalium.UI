using Jalium.UI.Controls;
using Jalium.UI.HostingDemo.Themes;
using Microsoft.Extensions.Logging;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>DI-created native window; the shell control owns the reusable UI.</summary>
public sealed class MainWindow : Window
{
    public MainWindow(ILogger<MainWindow> logger)
    {
        Title = "Jalium.UI AppBuilder 全家桶演示";
        Width = 1100; Height = 720;
        Style = HostingDemoTheme.WindowStyle;
        Content = new HostingDemoShell { Logger = logger };
    }
}
