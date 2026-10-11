using AppKit;
using ObjCRuntime;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace MacOSWindowLab;

internal static class WindowMenuLab
{
    internal static Window Create()
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x16, 0x32, 0x42));
        var window = new Window
        {
            Title = "窗口菜单 · Window 验证", Width = 660, Height = 600,
            MinWidth = 540, MinHeight = 540,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
        };
        var editor = new TextBox { Text = "菜单取消后继续编辑🙂", Height = 48, FontSize = 16 };
        AutomationProperties.SetName(editor, "窗口菜单验证编辑框");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink, MinHeight = 110 };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "原生窗口菜单", FontSize = 28, Foreground = ink });
        body.Children.Add(new TextBlock
        {
            Text = "打开菜单检查最小化、Zoom、全屏与关闭；Escape 取消后继续编辑。自定义标题栏也可右键打开。第一次关闭默认取消。",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink
        });
        body.Children.Add(editor);
        body.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xe5, 0xf4, 0xf8)),
            Padding = new Thickness(14), CornerRadius = new CornerRadius(8), Child = status
        });
        var controls = new WrapPanel(); body.Children.Add(controls);
        bool cancelClose = true;
        int closeRequests = 0;
        void Record(string action)
        {
            NSWindow? native = window.Handle == 0 ? null : Runtime.GetNSObject<NSView>(window.Handle)?.Window;
            Point origin = window.PointToScreen(new Point(0, 0));
            status.Text = $"标题栏：{window.TitleBarStyle} · 状态：{window.WindowState}\n"
                + $"客户区：{window.Width:0.##} × {window.Height:0.##} DIP · 屏幕原点：{origin.X:0.##}, {origin.Y:0.##} px\n"
                + $"菜单：{window.HasSystemMenu} · 缩放：{window.ResizeMode} · 关闭请求：{closeRequests}\n"
                + $"取消关闭：{cancelClose} · 最近操作：{action}";
            File.AppendAllText(Program.LogPrefix + "-window-menu.jsonl",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    pid = Environment.ProcessId, utc = DateTimeOffset.UtcNow, action,
                    window.Width, window.Height, window.Left, window.Top, window.HasSystemMenu,
                    state = window.WindowState.ToString(), titleBar = window.TitleBarStyle.ToString(),
                    resize = window.ResizeMode.ToString(), closeRequests, cancelClose, origin.X, origin.Y,
                    text = editor.Text, focused = editor.IsKeyboardFocused, active = window.IsActive,
                    nativeKey = native?.IsKeyWindow == true, nativeMain = native?.IsMainWindow == true
                }) + Environment.NewLine);
        }
        void Add(string label, Action<Button> action)
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => { action(button); editor.Focus(); Record(label); };
            controls.Children.Add(button);
        }
        Add("打开窗口菜单", _ => NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            Record("菜单请求");
            SystemCommands.ShowSystemMenu(window, window.PointToScreen(new Point(48, 102)));
            Record("菜单返回");
        }));
        Add("原生标题栏", b =>
        {
            window.TitleBarStyle = window.TitleBarStyle == WindowTitleBarStyle.Custom ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
            b.Content = window.TitleBarStyle == WindowTitleBarStyle.Custom ? "原生标题栏" : "自定义标题栏";
        });
        Add("固定大小", b =>
        {
            window.ResizeMode = window.ResizeMode == ResizeMode.NoResize ? ResizeMode.CanResize : ResizeMode.NoResize;
            b.Content = window.ResizeMode == ResizeMode.NoResize ? "允许缩放" : "固定大小";
        });
        Add("禁用窗口菜单", b => { window.HasSystemMenu = !window.HasSystemMenu; b.Content = window.HasSystemMenu ? "禁用窗口菜单" : "启用窗口菜单"; });
        Add("允许关闭", b => { cancelClose = !cancelClose; b.Content = cancelClose ? "允许关闭" : "取消关闭"; });
        Add("还原窗口", _ => window.WindowState = WindowState.Normal);
        Add("最小尺寸", _ => { window.WindowState = WindowState.Normal; window.Width = 540; window.Height = 540; });
        window.Content = body;
        window.Loaded += (_, _) => { WindowInputTrace.Attach(window); editor.Focus(); Record("显示"); };
        window.SizeChanged += (_, _) => Record("尺寸");
        window.StateChanged += (_, _) => Record("状态");
        window.Activated += (_, _) => Record("激活");
        window.Closing += (_, args) => { closeRequests++; args.Cancel = cancelClose; Record("关闭请求"); };
        window.Closed += (_, _) => Record("关闭");
        return window;
    }
}
