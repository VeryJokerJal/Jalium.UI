using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Hosting;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.TransparentBackdropDemo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var builder = AppBuilder.CreateBuilder(args);
        builder.ConfigureApplication(app => app.MainWindow = new MainWindow());
        using var host = builder.Build();
        return host.Run();
    }
}

/// <summary>
/// 透明窗口 + 可在运行时切换的窗口背景特效（DWM 系统材质）演示。
/// </summary>
public sealed class MainWindow : Window
{
    // 可切换的窗口背景特效（DWM 系统材质；Windows 11 22H2+ 原生，Windows 10 回退为 Acrylic 模糊）。
    private static readonly (string Label, WindowBackdropType Type)[] Effects =
    {
        ("无 · 纯透明", WindowBackdropType.None),
        ("Mica 云母", WindowBackdropType.Mica),
        ("Mica Alt", WindowBackdropType.MicaAlt),
        ("Acrylic 亚克力", WindowBackdropType.Acrylic),
        ("Auto 自动", WindowBackdropType.Auto),
    };

    private readonly Dictionary<WindowBackdropType, Button> _effectButtons = new();
    private TextBlock _statusText = null!;
    private bool _tintBackground;

    public MainWindow()
    {
        // ── 透明窗口 + 框架内建标题栏 ──
        // WindowStyle=None    去掉系统边框；标题栏改由框架的 Custom 标题栏（TitleBarStyle 默认即 Custom）提供，
        //                     自带标题文字 / 最小化-最大化-关闭按钮 / 圆角 / 拖动 / 双击最大化 / Win11 Snap，无需自绘。
        // AllowsTransparency  走 WS_EX_NOREDIRECTIONBITMAP + DirectComposition 合成，客户区可逐像素透明，
        //                     桌面 / DWM 系统材质才能真正透出。
        // Background=null     让窗口清屏走 Clear(0,0,0,0) 透明路径；若设（半）透明纯色会在材质之上铺一层颜色，
        //                     把系统材质 / 桌面盖住（这是"切了特效却看不到"的常见原因）。
        // 关键：DWM 系统材质（Mica/Acrylic）依赖 Custom 标题栏触发的 DwmExtendFrameIntoClientArea 才会画进
        //       客户区——所以保持默认 TitleBarStyle=Custom，切勿改成 Native，否则材质会静默失效。
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null;

        Title = "Jalium.UI 透明窗口 · 背景特效切换";
        Width = 880;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildContentCard();

        KeyDown += OnKeyDown;

        // 初始为「无」：窗口纯透明，先让用户看到"透明"本身（直接透出桌面）。
        ApplyEffect(WindowBackdropType.None);
    }

    // 中央"玻璃卡片"：半透明圆角 Border，保证在任意特效 / 桌面背景下文字都清晰可读。
    private FrameworkElement BuildContentCard()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        stack.Children.Add(new TextBlock
        {
            Text = "窗口背景特效",
            FontSize = 24,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        });

        stack.Children.Add(new TextBlock
        {
            Text = "WindowStyle=None · AllowsTransparency=True · TitleBarStyle=Custom · Background=null",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 22),
        });

        // 一排特效切换按钮（用默认 Button 样式，保留主题自带的 hover/pressed 反馈）。
        var effectRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18),
        };
        foreach (var (label, type) in Effects)
        {
            var btn = MakeEffectButton(label, type);
            _effectButtons[type] = btn;
            effectRow.Children.Add(btn);
        }
        stack.Children.Add(effectRow);

        _statusText = new TextBlock
        {
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18),
        };
        stack.Children.Add(_statusText);

        stack.Children.Add(MakeWideButton("切换窗口背景：透明 ↔ 半透明纯色", ToggleBackground));

        stack.Children.Add(new TextBlock
        {
            Text = "Mica / Acrylic 为 DWM 系统材质，需 Windows 11 22H2+（Win10 回退为 Acrylic 模糊）。\n" +
                   "选「无」时窗口纯透明、直接透出桌面；材质需在透明背景下才会显现。",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(127, 132, 156)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
        });

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(140, 30, 30, 46)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(36, 28, 36, 28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = stack,
        };
    }

    // 运行时切换背景特效：SystemBackdrop 是依赖属性，赋值即触发
    // OnSystemBackdropChanged → ApplySystemBackdrop（DwmSetWindowAttribute），窗口已显示时立即生效。
    private void ApplyEffect(WindowBackdropType type)
    {
        SystemBackdrop = type;
        RefreshEffectButtons();
        UpdateStatus();
    }

    // 用文字前缀标记当前选中，而不是覆盖按钮 Background——后者会以本地值屏蔽默认模板的 hover/pressed 触发器。
    private void RefreshEffectButtons()
    {
        foreach (var (label, type) in Effects)
        {
            if (_effectButtons.TryGetValue(type, out var btn))
                btn.Content = (type == SystemBackdrop ? "● " : "") + label;
        }
    }

    // 演示关键陷阱：Background=null 时特效 / 桌面透出；半透明纯色会盖住它们。
    private void ToggleBackground()
    {
        _tintBackground = !_tintBackground;
        // 设置 Window.Background 本身即触发重绘，无需再 InvalidateVisual。
        Background = _tintBackground
            ? new SolidColorBrush(Color.FromArgb(160, 24, 24, 37))
            : null;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        string effect = SystemBackdrop switch
        {
            WindowBackdropType.None => "无（窗口纯透明，透出桌面）",
            WindowBackdropType.Mica => "Mica 云母（采样桌面壁纸）",
            WindowBackdropType.MicaAlt => "Mica Alt（云母变体）",
            WindowBackdropType.Acrylic => "Acrylic 亚克力（模糊背后内容）",
            WindowBackdropType.Auto => "Auto（由 DWM 自动决定）",
            _ => SystemBackdrop.ToString(),
        };
        string bg = _tintBackground ? "半透明纯色（会盖住材质 / 桌面）" : "null（透出特效 / 桌面）";
        _statusText.Text = $"当前特效：{effect}    ·    窗口背景：{bg}";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            e.Handled = true;
        }
    }

    // 默认 Button 样式：只设内容 / 尺寸 / 字号，不碰 Background，保留主题的 hover / pressed 反馈。
    private Button MakeEffectButton(string text, WindowBackdropType type)
    {
        var btn = new Button
        {
            MinWidth = 120,
            MinHeight = 40,
            FontSize = 13,
            Margin = new Thickness(5, 0, 5, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Content = text,
        };
        btn.Click += (_, _) => ApplyEffect(type);
        return btn;
    }

    private Button MakeWideButton(string text, Action onClick)
    {
        var btn = new Button
        {
            MinWidth = 360,
            MinHeight = 38,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 6, 16, 6),
            Content = text,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
