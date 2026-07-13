using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.LinuxDemo;

/// <summary>
/// Linux desktop showcase for Jalium.UI: a code-only application exercising
/// windowing (custom title bar + DragMove), text (CJK + IME), popups
/// (ComboBox/ContextMenu/ToolTip), input controls, clipboard, dialogs and
/// notifications on X11 or Wayland.
///
///   dotnet run -c Release
///   JALIUM_WINDOW_SYSTEM=wayland dotnet run   # force a window system
///   JALIUM_RENDER_BACKEND=software dotnet run # skip Vulkan
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var builder = AppBuilder.CreateBuilder(new AppBuilderSettings
        {
            Args = args,
            DisableDefaults = true,
        });

        builder.ConfigureApplication(app =>
        {
            app.MainWindow = BuildMainWindow();
        });

        using var host = builder.Build();
        return host.Run();
    }

    private static Window BuildMainWindow()
    {
        var window = new Window
        {
            Title = "Jalium.UI on Linux",
            Width = 720,
            Height = 560,
            MinWidth = 420,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(24, 26, 32)),
        };

        var root = new DockPanel();

        // ── Custom title bar (server decorations are also fine; this shows
        //    DragMove working through _NET_WM_MOVERESIZE / xdg_toplevel.move).
        var titleBar = BuildTitleBar(window);
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);

        // ── Status bar.
        var status = new TextBlock
        {
            Text = "就绪 — hover, click, right-click, type…",
            FontSize = 12,
            Margin = new Thickness(12, 6, 12, 8),
            Foreground = new SolidColorBrush(Color.FromRgb(140, 150, 165)),
        };
        var statusHost = new Border { Child = status };
        DockPanel.SetDock(statusHost, Dock.Bottom);
        root.Children.Add(statusHost);

        void Report(string message)
        {
            status.Text = message;
            Console.WriteLine($"[demo] {message}");
        }

        // ── Content: two columns of interactive controls.
        var grid = new Grid { Margin = new Thickness(16, 12, 16, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        root.Children.Add(grid);

        // Text + IME.
        left.Children.Add(Header("文本与输入法 (IME)"));
        left.Children.Add(new TextBlock
        {
            Text = "Hello, Linux 桌面! Grüße, Привет, こんにちは",
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap,
        });
        var textBox = new TextBox
        {
            Text = "在这里输入（支持 XIM / text-input-v3）",
            Margin = new Thickness(0, 0, 0, 12),
            MinHeight = 30,
        };
        left.Children.Add(textBox);

        // Buttons + clipboard.
        left.Children.Add(Header("按钮与剪贴板"));
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var clicks = 0;
        var clickButton = new Button { Content = "Click me", MinWidth = 110, MinHeight = 30 };
        clickButton.Click += (_, _) => Report($"按钮点击 x{++clicks}");
        buttonRow.Children.Add(clickButton);

        var copyButton = new Button
        {
            Content = "复制文本",
            MinWidth = 110,
            MinHeight = 30,
            Margin = new Thickness(8, 0, 0, 0),
        };
        copyButton.Click += (_, _) =>
        {
            Clipboard.SetText(textBox.Text ?? string.Empty);
            Report("已复制到系统剪贴板（wl_data_device / X11 selection）");
        };
        buttonRow.Children.Add(copyButton);

        var pasteButton = new Button
        {
            Content = "粘贴",
            MinWidth = 80,
            MinHeight = 30,
            Margin = new Thickness(8, 0, 0, 0),
        };
        pasteButton.Click += (_, _) =>
        {
            var text = Clipboard.GetText();
            textBox.Text = text;
            Report(string.IsNullOrEmpty(text) ? "剪贴板为空" : $"粘贴了 {text.Length} 个字符");
        };
        buttonRow.Children.Add(pasteButton);
        left.Children.Add(buttonRow);

        // Selection controls (popup path: ComboBox opens in the overlay).
        left.Children.Add(Header("选择控件（弹窗）"));
        var combo = new ComboBox { MinWidth = 220, Margin = new Thickness(0, 0, 0, 8) };
        combo.Items.Add("Wayland (xdg-shell)");
        combo.Items.Add("X11 (Xlib + EWMH)");
        combo.Items.Add("软件渲染 (wl_shm / XPutImage)");
        combo.Items.Add("Vulkan (llvmpipe 亦可)");
        combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) => Report($"选择了：{combo.SelectedItem}");
        left.Children.Add(combo);

        var checkRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var check = new CheckBox { Content = "启用特性", IsChecked = true };
        check.Checked += (_, _) => Report("CheckBox: on");
        check.Unchecked += (_, _) => Report("CheckBox: off");
        checkRow.Children.Add(check);
        var radioA = new RadioButton { Content = "A", GroupName = "g", IsChecked = true, Margin = new Thickness(16, 0, 0, 0) };
        var radioB = new RadioButton { Content = "B", GroupName = "g", Margin = new Thickness(8, 0, 0, 0) };
        checkRow.Children.Add(radioA);
        checkRow.Children.Add(radioB);
        left.Children.Add(checkRow);

        // Slider + progress.
        right.Children.Add(Header("Slider 与进度"));
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = 40, MinHeight = 8, Margin = new Thickness(0, 6, 0, 12) };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 40 };
        slider.ValueChanged += (_, e) =>
        {
            progress.Value = e.NewValue;
            Report($"Slider: {e.NewValue:0}");
        };
        right.Children.Add(slider);
        right.Children.Add(progress);

        // List.
        right.Children.Add(Header("列表"));
        var list = new ListBox { MaxHeight = 140, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var item in new[]
                 {
                     "输入：xkbcommon / XKB 键盘映射",
                     "剪贴板：文本双向",
                     "拖放：XDND v5 / wl_data_device",
                     "无障碍：AT-SPI2 桥",
                     "通知：org.freedesktop.Notifications",
                     "文件对话框：xdg-desktop-portal",
                 })
        {
            list.Items.Add(item);
        }
        list.SelectionChanged += (_, _) => Report($"列表选择：{list.SelectedItem}");
        right.Children.Add(list);

        // Dialogs & desktop integration.
        right.Children.Add(Header("桌面集成"));
        var integrationRow = new StackPanel { Orientation = Orientation.Horizontal };
        var openButton = new Button { Content = "打开文件…", MinWidth = 110, MinHeight = 30 };
        openButton.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择一个文件",
                Filter = "文本文件|*.txt;*.md|所有文件|*.*",
            };
            var ok = dialog.ShowDialog() == true;
            Report(ok ? $"选择了：{dialog.FileName}" : "对话框已取消（或无 portal 服务）");
        };
        integrationRow.Children.Add(openButton);

        var messageButton = new Button
        {
            Content = "消息框",
            MinWidth = 90,
            MinHeight = 30,
            Margin = new Thickness(8, 0, 0, 0),
        };
        messageButton.Click += (_, _) =>
        {
            var result = MessageBox.Show(
                "这是框架自绘的跨平台 MessageBox。", "Jalium.UI",
                MessageBoxButton.OKCancel, MessageBoxImage.Information);
            Report($"MessageBox 返回 {result}");
        };
        integrationRow.Children.Add(messageButton);
        right.Children.Add(integrationRow);

        // Context menu on the whole window content.
        var menu = new ContextMenu();
        var about = new MenuItem { Header = "关于 Jalium.UI on Linux" };
        about.Click += (_, _) => Report("ContextMenu → 关于");
        menu.Items.Add(about);
        var quit = new MenuItem { Header = "退出" };
        quit.Click += (_, _) => window.Close();
        menu.Items.Add(quit);
        root.ContextMenu = menu;

        clickButton.ToolTip = "一个 ToolTip（overlay 弹窗）";

        window.Loaded += (_, _) =>
        {
            Console.WriteLine("[demo] ready");

            // Headless CI drives the sample with JALIUM_DEMO_AUTOCLOSE_MS so a
            // real window + render loop is exercised, then exits cleanly.
            if (Environment.GetEnvironmentVariable("JALIUM_DEMO_AUTOCLOSE_MS") is { } autoCloseText &&
                int.TryParse(autoCloseText, out var autoCloseMs) && autoCloseMs > 0)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(autoCloseMs) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    Console.WriteLine("[demo] auto-close");
                    window.Close();
                };
                timer.Start();
            }
        };

        window.Content = root;
        return window;
    }

    private static Border BuildTitleBar(Window window)
    {
        var bar = new Grid { MinHeight = 36 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Jalium.UI — Linux 桌面示例",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(200, 208, 220)),
        };
        Grid.SetColumn(title, 0);
        bar.Children.Add(title);

        var closeButton = new Button
        {
            Content = "✕",
            MinWidth = 44,
            MinHeight = 28,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeButton.Click += (_, _) => window.Close();
        Grid.SetColumn(closeButton, 1);
        bar.Children.Add(closeButton);

        var host = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(32, 35, 44)),
            Child = bar,
        };

        // Window-system-driven interactive move (Wave B ABI): press anywhere on
        // the bar and the WM/compositor takes over the drag.
        host.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1)
                window.DragMove();
        };

        return host;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Margin = new Thickness(0, 4, 0, 6),
        Foreground = new SolidColorBrush(Color.FromRgb(110, 160, 255)),
    };
}
