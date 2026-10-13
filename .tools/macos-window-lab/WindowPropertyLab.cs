using AppKit;
using Foundation;
using ObjCRuntime;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace MacOSWindowLab;

internal static class WindowPropertyLab
{
    internal static void Show(Window owner)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x16, 0x32, 0x42));
        var window = new Window
        {
            Title = "窗口属性 · Window 验证", Owner = owner,
            Width = 680, Height = 640, MinWidth = 540, MinHeight = 540,
            MaxWidth = 960, MaxHeight = 840,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc)),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var editor = new TextBox { Text = "窗口属性修改后继续编辑🙂", Height = 48, FontSize = 16 };
        Jalium.UI.Automation.AutomationProperties.SetName(editor, "窗口属性验证编辑框");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink, MinHeight = 100 };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "窗口属性与状态同步", FontSize = 26, Foreground = ink });
        body.Children.Add(new TextBlock
        {
            Text = "在 Accessibility Inspector 中修改窗口的 Position、Size 与 Minimized；核对这里的实际位置、尺寸和内容。尺寸范围为 540–960 × 540–840 DIP。",
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
            NSWindow? native = window.Handle == 0 ? null : Runtime.GetNSObject<NSView>(window.Handle)?.Window;
            status.Text = $"位置：{window.Left:0.##}, {window.Top:0.##} DIP · 客户区：{window.Width:0.##} × {window.Height:0.##} DIP\n"
                + $"状态：{window.WindowState} · 标题栏：{window.TitleBarStyle}\n"
                + $"尺寸模式：{window.ResizeMode} · 自动尺寸：{window.SizeToContent}\n"
                + $"原生活动：Key={native?.IsKeyWindow == true} · Main={native?.IsMainWindow == true}";
            File.AppendAllText(Program.LogPrefix + "-property.jsonl",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    pid = Environment.ProcessId, utc = DateTimeOffset.UtcNow, action,
                    window.Left, window.Top, window.Width, window.Height, window.IsEnabled,
                    state = window.WindowState.ToString(), titleBar = window.TitleBarStyle.ToString(),
                    resize = window.ResizeMode.ToString(), sizing = window.SizeToContent.ToString(),
                    text = editor.Text, focused = editor.IsKeyboardFocused,
                    active = window.IsActive, nativeKey = native?.IsKeyWindow == true,
                    nativeMain = native?.IsMainWindow == true,
                    restore = window.RestoreBounds.ToString()
                }) + Environment.NewLine);
        }
        void Add(string label, Action<Button> action)
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => { action(button); editor.Focus(); Record(label); };
            controls.Children.Add(button);
        }
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
        Add("自动尺寸", b =>
        {
            window.SizeToContent = window.SizeToContent == SizeToContent.Manual ? SizeToContent.WidthAndHeight : SizeToContent.Manual;
            b.Content = window.SizeToContent == SizeToContent.Manual ? "自动尺寸" : "手动尺寸";
        });
        Add("常规尺寸", _ =>
        {
            window.WindowState = WindowState.Normal; window.SizeToContent = SizeToContent.Manual;
            window.Width = 680; window.Height = 640;
        });
        Add("最小尺寸", _ =>
        {
            window.WindowState = WindowState.Normal; window.SizeToContent = SizeToContent.Manual;
            window.Width = 540; window.Height = 540;
        });
        Add("打开模态", _ =>
        {
            var dialog = new Window { Title = "窗口属性模态验证", Owner = window, Width = 420, Height = 280, Background = window.Background, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
            panel.Children.Add(new TextBlock { Text = "此时父窗口的 Position、Size、Minimized、Main 和 Focused 应不可写。", TextWrapping = TextWrapping.Wrap, Foreground = ink, FontSize = 16 });
            var done = new Button { Content = "返回编辑", IsDefault = true, IsCancel = true, MinHeight = 44 };
            done.Click += (_, _) => dialog.DialogResult = true;
            panel.Children.Add(done); dialog.Content = panel;
            dialog.Loaded += (_, _) => Record("模态已加载");
            dialog.ShowDialog(); Record("模态返回");
        });
        window.Content = body;
        window.LocationChanged += (_, _) => Record("位置变化");
        window.SizeChanged += (_, _) => Record("尺寸变化");
        window.StateChanged += (_, _) => Record("状态变化");
        window.Activated += (_, _) => Record("激活");
        var notifications = new List<NSObject>();
        foreach (var name in new[] { NSWindow.DidBecomeMainNotification, NSWindow.DidResignMainNotification,
            NSWindow.DidBecomeKeyNotification, NSWindow.DidResignKeyNotification })
        {
            notifications.Add(NSNotificationCenter.DefaultCenter.AddObserver(name, note =>
            {
                if (window.Handle != 0 && note.Object?.Handle == Runtime.GetNSObject<NSView>(window.Handle)?.Window?.Handle)
                    Record(name.ToString());
            }));
        }
        window.Closed += (_, _) =>
        {
            foreach (var observer in notifications) { NSNotificationCenter.DefaultCenter.RemoveObserver(observer); observer.Dispose(); }
            Record("已关闭");
        };
        window.Show(); WindowInputTrace.Attach(window); editor.Focus(); Record("已显示");
    }
}
