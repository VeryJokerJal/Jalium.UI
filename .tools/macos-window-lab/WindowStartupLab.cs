using AppKit;
using CoreGraphics;
using ObjCRuntime;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace MacOSWindowLab;

internal static class WindowStartupLab
{
    internal static Window Create(bool nativeTitleBar = false, bool maximizeInitialized = false)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x16, 0x32, 0x42));
        var window = new Window
        {
            Title = "初始定位 · Window 验证", Width = 720, Height = 640, MinWidth = 600, MinHeight = 560,
            TitleBarStyle = nativeTitleBar ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
        };
        var editor = new TextBox { Text = "移动和重开后继续编辑🙂", Height = 48, FontSize = 16 };
        AutomationProperties.SetName(editor, "初始定位验证编辑框");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink, MinHeight = 110 };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "窗口初始定位", FontSize = 28, Foreground = ink });
        body.Children.Add(new TextBlock
        {
            Text = "首次显示时外框居中；移动后隐藏重开，应保留位置、尺寸与内容。子窗口按父窗口外框居中，并保持在工作区内。",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink
        });
        body.Children.Add(editor);
        body.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xe5, 0xf4, 0xf8)),
            Padding = new Thickness(14), CornerRadius = new CornerRadius(8), Child = status
        });
        var controls = new WrapPanel(); body.Children.Add(controls);
        void Record(string action)
        {
            NSWindow? native = Native(window);
            CGRect frame = native?.Frame ?? CGRect.Empty;
            CGRect work = native?.Screen?.VisibleFrame ?? CGRect.Empty;
            double dx = (double)(frame.GetMidX() - work.GetMidX());
            double dy = (double)(frame.GetMidY() - work.GetMidY());
            status.Text = $"位置：{window.Left:0.##}, {window.Top:0.##} DIP · 客户区：{window.Width:0.##} × {window.Height:0.##} DIP\n"
                + $"原生外框：{frame.Width:0.##} × {frame.Height:0.##} 点 · 标题栏：{window.TitleBarStyle}\n"
                + $"相对工作区中心：{dx:0.##}, {dy:0.##} 点 · 状态：{window.WindowState}\n"
                + $"最近操作：{action}";
            Write(window, editor, action, frame, work, dx, dy);
        }
        void Add(string label, Action<Button> action)
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => { action(button); editor.Focus(); Record(label); };
            controls.Children.Add(button);
        }
        Add("移动 137×89", _ => { window.Left += 137; window.Top += 89; });
        Add("隐藏并重开", _ =>
        {
            window.Hide();
            NSApplication.SharedApplication.BeginInvokeOnMainThread(() => { window.Show(); editor.Focus(); Record("重开完成"); });
        });
        Add("原生标题栏", b =>
        {
            window.TitleBarStyle = window.TitleBarStyle == WindowTitleBarStyle.Custom ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
            b.Content = window.TitleBarStyle == WindowTitleBarStyle.Custom ? "原生标题栏" : "自定义标题栏";
        });
        Add("居中原生子窗口", _ => ShowChild(window, nativeTitleBar: true));
        Add("居中自定义子窗口", _ => ShowChild(window, nativeTitleBar: false));
        Add("还原窗口", _ => window.WindowState = WindowState.Normal);
        Add("最小尺寸", _ => { window.Width = 600; window.Height = 560; });
        window.Content = body;
        if (maximizeInitialized) window.SourceInitialized += (_, _) => window.WindowState = WindowState.Maximized;
        window.Loaded += (_, _) => { editor.Focus(); Record("显示"); };
        window.LocationChanged += (_, _) => Record("位置");
        window.SizeChanged += (_, _) => Record("尺寸");
        window.StateChanged += (_, _) => Record("状态");
        window.Closed += (_, _) => Record("关闭");
        return window;
    }

    private static void ShowChild(Window owner, bool nativeTitleBar)
    {
        var child = new Window
        {
            Title = nativeTitleBar ? "原生子窗口 · 定位验证" : "自定义子窗口 · 定位验证",
            Owner = owner, Width = 480, Height = 420,
            TitleBarStyle = nativeTitleBar ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = owner.Background
        };
        var editor = new TextBox { Text = "混合标题栏后继续编辑🙂", Height = 48, FontSize = 16 };
        AutomationProperties.SetName(editor, "居中子窗口验证编辑框");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "父子窗口外框居中", FontSize = 26 });
        body.Children.Add(editor); body.Children.Add(status);
        var close = new Button { Content = "关闭子窗口", MinHeight = 44, IsDefault = true, IsCancel = true };
        close.Click += (_, _) => child.Close(); body.Children.Add(close); child.Content = body;
        child.Loaded += (_, _) =>
        {
            CGRect frame = Native(child)!.Frame, parent = Native(owner)!.Frame, work = Native(child)!.Screen!.VisibleFrame;
            double dx = (double)(frame.GetMidX() - parent.GetMidX()), dy = (double)(frame.GetMidY() - parent.GetMidY());
            status.Text = $"父标题栏：{owner.TitleBarStyle} · 子标题栏：{child.TitleBarStyle}\n外框中心差：{dx:0.##}, {dy:0.##} 点\n客户区：{child.Width:0.##} × {child.Height:0.##} DIP";
            editor.Focus(); Write(child, editor, "子窗口显示", frame, work, dx, dy);
        };
        child.Show();
    }

    private static NSWindow? Native(Window window) => window.Handle == 0 ? null : Runtime.GetNSObject<NSView>(window.Handle)?.Window;
    private static void Write(Window window, TextBox editor, string action, CGRect frame, CGRect work, double dx, double dy) =>
        File.AppendAllText(Program.LogPrefix + "-startup.jsonl", System.Text.Json.JsonSerializer.Serialize(new
        {
            pid = Environment.ProcessId, utc = DateTimeOffset.UtcNow, action, window.Title,
            window.Left, window.Top, window.Width, window.Height, titleBar = window.TitleBarStyle.ToString(),
            state = window.WindowState.ToString(), frameX = (double)frame.X, frameY = (double)frame.Y,
            frameWidth = (double)frame.Width, frameHeight = (double)frame.Height,
            workX = (double)work.X, workY = (double)work.Y, workWidth = (double)work.Width, workHeight = (double)work.Height,
            dx, dy, text = editor.Text, focused = editor.IsKeyboardFocused, nativeKey = Native(window)?.IsKeyWindow == true
        }) + Environment.NewLine);
}
