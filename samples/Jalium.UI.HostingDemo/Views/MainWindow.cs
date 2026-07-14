using Jalium.UI.Controls;
using Jalium.UI.Hosting;
using Jalium.UI.HostingDemo.ViewModels;
using Jalium.UI.Media;
using Microsoft.Extensions.Logging;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>
/// Shell 窗口:左侧导航菜单 + <see cref="Frame"/> + 底部 FPS 状态栏。
/// 构造函数从 DI 注入 <see cref="IServiceProvider"/> 和 <see cref="ILogger{TCategoryName}"/>,
/// 演示 <c>builder.Services</c> 与 <c>builder.Logging</c> 的终点。
/// </summary>
public sealed class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly Frame _frame;
    private readonly TextBlock _fpsText;

    public MainWindow(ILogger<MainWindow> logger)
    {
        _logger = logger;

        Title = "Jalium.UI AppBuilder 全家桶演示";
        Width = 1100;
        Height = 720;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));

        // ── Grid: 左菜单 | 主内容;底部状态栏 ──
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

        // 左侧菜单
        var menu = BuildMenu();
        Grid.SetColumn(menu, 0);
        Grid.SetRow(menu, 0);
        root.Children.Add(menu);

        // 中间 Frame —— 导航宿主,Frame.Navigate(Type) 会通过 IViewFactory 解析 Page + VM
        _frame = new Frame
        {
            Margin = new Thickness(8)
        };
        Grid.SetColumn(_frame, 1);
        Grid.SetRow(_frame, 0);
        root.Children.Add(_frame);

        // 底部状态栏:FPS
        var status = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 17, 27)),
            Padding = new Thickness(12, 4, 12, 4)
        };
        _fpsText = new TextBlock
        {
            Text = "FPS: —",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161)),
            VerticalAlignment = VerticalAlignment.Center
        };
        status.Child = _fpsText;
        Grid.SetColumn(status, 0);
        Grid.SetColumnSpan(status, 2);
        Grid.SetRow(status, 1);
        root.Children.Add(status);

        Content = root;

        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;

        // 默认首页
        _frame.Navigate(typeof(HomePage));
    }

    private FrameworkElement BuildMenu()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
            Margin = new Thickness(0)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "AppBuilder 全家桶",
            FontSize = 16,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(16, 16, 16, 16)
        });

        panel.Children.Add(NavButton("首页 · Services / Environment", typeof(HomePage)));
        panel.Children.Add(NavButton("MVVM · AddView<T, VM>()", typeof(MvvmPage)));
        panel.Children.Add(NavButton("Logging · ILogger<T>", typeof(LoggingPage)));
        panel.Children.Add(NavButton("Metrics · JaliumMeter", typeof(MetricsPage)));
        panel.Children.Add(NavButton("Configuration · JaliumRuntimeOptions", typeof(OptionsPage)));
        panel.Children.Add(NavButton("IHostedService · BackgroundService", typeof(HostedServicesPage)));

        return panel;
    }

    private Button NavButton(string text, Type pageType)
    {
        var btn = new Button
        {
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 10, 16, 10),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244))
            }
        };
        btn.Click += (_, _) =>
        {
            _logger.LogDebug("导航到 {PageType}", pageType.Name);
            _frame.Navigate(pageType);
        };
        return btn;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        _fpsText.Text = $"FPS: {JaliumMeter.CurrentFps:F1}    Frame: {JaliumMeter.LastFrameDurationMs:F2} ms    Meter: {JaliumMeter.MeterName} ({(JaliumMeter.IsRunning ? "running" : "stopped")})";
    }
}
