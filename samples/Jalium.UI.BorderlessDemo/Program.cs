using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Hosting;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.BorderlessDemo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // WPF 风格：子类化 Application、重写 OnStartup/OnExit/OnSessionEnding。
        // AppBuilder.UseApplication<T>() 负责在 Build() 时通过无参构造创建实例。
        var builder = AppBuilder.CreateBuilder(args);
        //builder.UseJaliumMetrics();
        builder.UseApplication<BorderlessDemoApp>();

        using var host = builder.Build();
        return host.Run();
    }
}

/// <summary>
/// 演示 Application 生命周期重写：OnStartup / OnExit / OnSessionEnding。
/// </summary>
public class BorderlessDemoApp : Application
{
    private DateTime _startedAt;

    protected override void OnStartup(StartupEventArgs e)
    {
        _startedAt = DateTime.Now;
        Console.WriteLine($"[OnStartup] args=[{string.Join(", ", e.Args)}]");
        Console.WriteLine($"[OnStartup] started at {_startedAt:HH:mm:ss.fff}");

        MainWindow = new BorderlessMainWindow();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var elapsed = DateTime.Now - _startedAt;
        Console.WriteLine($"[OnExit] exitCode={e.ApplicationExitCode}, elapsed={elapsed.TotalSeconds:F2}s");

        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Console.WriteLine($"[OnSessionEnding] reason={e.ReasonSessionEnding}");
        base.OnSessionEnding(e);
    }
}

/// <summary>
/// 无边框 + 可切换全屏的主窗口演示。
/// </summary>
public class BorderlessMainWindow : Window
{
    private readonly TextBlock _statusText;

    public BorderlessMainWindow()
    {
        // 核心：无边框 + 逐像素透明。AllowsTransparency 触发 WS_EX_NOREDIRECTIONBITMAP
        // 路径，走 DirectComposition 合成 D3D12 swap chain，因此背景 alpha 会真正透出桌面。
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        TitleBarStyle = WindowTitleBarStyle.Native; // 不要框架自带的自定义 TitleBar
        ResizeMode = ResizeMode.CanResize;

        Title = "Jalium.UI 无边框 / 全屏演示";
        Width = 820;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // 半透明背景：alpha=200 让桌面在窗口下方若隐若现
        Background = new SolidColorBrush(Color.FromArgb(200, 24, 24, 37));

        // ── 根布局：上方自定义标题栏 + 下方内容区 ──
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });

        root.Children.Add(BuildCustomTitleBar());

        _statusText = new TextBlock
        {
            Text = "当前状态：Normal",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(32)
        };

        content.Children.Add(new TextBlock
        {
            Text = "WindowStyle=None · AllowsTransparency=True",
            FontSize = 22,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });

        content.Children.Add(new TextBlock
        {
            Text = "拖动顶部区域可移动窗口；拖动四边可调整大小。",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24)
        });

        content.Children.Add(_statusText);

        content.Children.Add(MakeActionButton("进入全屏 (F11)", () =>
        {
            WindowState = WindowState.FullScreen;
            UpdateStatus();
        }));

        content.Children.Add(MakeActionButton("退出全屏", () =>
        {
            WindowState = WindowState.Normal;
            UpdateStatus();
        }));

        content.Children.Add(MakeActionButton("切换 WindowStyle (None ↔ SingleBorderWindow)", () =>
        {
            WindowStyle = WindowStyle == WindowStyle.None
                ? WindowStyle.SingleBorderWindow
                : WindowStyle.None;
            UpdateStatus();
        }));

        content.Children.Add(MakeActionButton("最大化 / 还原", () =>
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            UpdateStatus();
        }));

        content.Children.Add(MakeActionButton("关闭窗口", Close));

        Grid.SetRow(content, 1);
        root.Children.Add(content);

        Content = root;

        // F11 切换全屏
        KeyDown += OnKeyDown;
        StateChanged += (_, _) => UpdateStatus();
    }

    private FrameworkElement BuildCustomTitleBar()
    {
        var bar = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 17, 17, 27))
        };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(bar, 0);

        var titleText = new TextBlock
        {
            Text = "  ⬢  Jalium.UI BorderlessDemo",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(titleText, 0);
        bar.Children.Add(titleText);

        // 点住标题栏拖动移动窗口
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(MakeCaptionButton("—", () => WindowState = WindowState.Minimized));
        buttons.Children.Add(MakeCaptionButton("▢", () =>
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized));
        buttons.Children.Add(MakeCaptionButton("✕", Close));
        Grid.SetColumn(buttons, 1);
        bar.Children.Add(buttons);

        return bar;
    }

    private static Button MakeCaptionButton(string glyph, Action onClick)
    {
        var btn = new Button
        {
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Width = 46,
            Height = 44,
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static Button MakeActionButton(string text, Action onClick)
    {
        var btn = new Button
        {
            Background = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            MinWidth = 320,
            MinHeight = 38,
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 4, 0, 4),
            Content = new TextBlock
            {
                Text = text,
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            e.Handled = true;
        }
    }

    private void UpdateStatus()
    {
        _statusText.Text = $"当前状态：WindowState={WindowState}, WindowStyle={WindowStyle}";
    }
}
