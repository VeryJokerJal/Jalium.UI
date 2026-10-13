using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using ObjCRuntime;

namespace MacOSWindowLab;

internal static class WindowMinimizeLab
{
    internal static Window Create(bool nativeTitleBar = false)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x16, 0x32, 0x42));
        var window = new Window
        {
            Title = "最小化与还原 · Window 验证", Width = 800, Height = 760, MinWidth = 620, MinHeight = 700,
            TitleBarStyle = nativeTitleBar ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
        };
        var editor = new TextBox { Text = "最小化后继续编辑中文🙂", Height = 48, FontSize = 16 };
        AutomationProperties.SetName(editor, "最小化队列验证编辑框");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink, MinHeight = 130 };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "最小化中的状态请求", FontSize = 28, Foreground = ink });
        body.Children.Add(new TextBlock
        {
            Text = "点击最小化操作，窗口应在动画完成后执行最后一次还原或最大化请求。激活操作在重开后选择窗口，以实际焦点为准。切换标题栏重复验证。",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink
        });
        body.Children.Add(new TextBlock { Text = "验证编辑内容", FontSize = 16, Foreground = ink });
        body.Children.Add(editor);
        body.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xe5, 0xf4, 0xf8)), Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8), Child = status
        });
        var controls = new WrapPanel(); body.Children.Add(controls);
        WindowState? pending = null;
        WindowState? latest = null;
        bool activationOnStart = false;
        bool? activationAccepted = null;
        Rect saved = Rect.Empty;
        int starts = 0, completions = 0, restores = 0;
        NSObject? began = null, completed = null, restored = null;

        void Record(string action)
        {
            NSWindow? native = Native(window);
            CGRect frame = native?.Frame ?? CGRect.Empty;
            string actual = native?.IsMiniaturized == true ? "Minimized" : native?.IsZoomed == true ? "Maximized" : "Normal";
            Rect bounds = window.RestoreBounds;
            status.Text = $"托管状态：{window.WindowState} · 原生状态：{actual} · 最后请求：{latest?.ToString() ?? "未请求"}\n"
                + $"最小化开始 / 完成 / 重开：{starts} / {completions} / {restores} · 标题栏：{window.TitleBarStyle}\n"
                + $"还原客户区：{bounds.Width:0.##} × {bounds.Height:0.##} DIP · 当前外框：{frame.Width:0.##} × {frame.Height:0.##} 点\n"
                + $"激活请求：{(activationAccepted is null ? "未请求" : activationAccepted.Value ? "已接受" : "未接受")} · 窗口焦点：{(native?.IsKeyWindow == true ? "有" : "无")} · 编辑焦点：{(editor.IsKeyboardFocused ? "有" : "无")}\n"
                + $"最近操作：{action}";
            File.AppendAllText(Program.LogPrefix + "-minimize.jsonl", System.Text.Json.JsonSerializer.Serialize(new
            {
                pid = Environment.ProcessId, utc = DateTimeOffset.UtcNow, action, starts, completions, restores,
                managed = window.WindowState.ToString(), actual, latest = latest?.ToString(), titleBar = window.TitleBarStyle.ToString(),
                window.Left, window.Top, window.Width, window.Height,
                frameX = (double)frame.X, frameY = (double)frame.Y, frameWidth = (double)frame.Width, frameHeight = (double)frame.Height,
                restoreX = bounds.X, restoreY = bounds.Y, restoreWidth = bounds.Width, restoreHeight = bounds.Height,
                preserved = saved.IsEmpty || bounds == saved, text = editor.Text, focused = editor.IsKeyboardFocused,
                nativeKey = native?.IsKeyWindow == true, activationAccepted, window.IsActive
            }) + Environment.NewLine);
        }
        void Queue(WindowState target, bool nativeAction)
        {
            activationOnStart = false; activationAccepted = null;
            saved = window.RestoreBounds; latest = pending = target;
            Record(nativeAction ? "原生最小化请求" : "托管最小化请求");
            if (nativeAction) Native(window)?.PerformMiniaturize(null);
            else window.WindowState = WindowState.Minimized;
        }
        void QueueActivation(bool maximizeFirst)
        {
            if (maximizeFirst) window.WindowState = WindowState.Maximized;
            saved = window.RestoreBounds;
            latest = window.WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            pending = null; activationOnStart = true; activationAccepted = null;
            Record("最小化后激活请求");
            window.WindowState = WindowState.Minimized;
        }
        void Add(string label, Action<Button> action)
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) =>
            {
                action(button);
                Record(label == "原生标题栏" ? $"标题栏切换：{window.TitleBarStyle}" : label);
            };
            controls.Children.Add(button);
        }
        Add("最小化→还原", _ => Queue(WindowState.Normal, false));
        Add("最小化→最大化", _ => Queue(WindowState.Maximized, false));
        Add("原生命令→还原", _ => Queue(WindowState.Normal, true));
        Add("原生命令→最大化", _ => Queue(WindowState.Maximized, true));
        Add("最小化→激活", _ => QueueActivation(false));
        Add("最大化最小化→激活", _ => QueueActivation(true));
        Add("托管最大化", _ => window.WindowState = WindowState.Maximized);
        Add("托管还原", _ => window.WindowState = WindowState.Normal);
        Add("原生标题栏", button =>
        {
            window.TitleBarStyle = window.TitleBarStyle == WindowTitleBarStyle.Custom ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
            button.Content = window.TitleBarStyle == WindowTitleBarStyle.Custom ? "原生标题栏" : "自定义标题栏";
        });
        Add("最小尺寸", _ => { window.WindowState = WindowState.Normal; window.Width = 620; window.Height = 700; });
        window.Content = body;
        window.Loaded += (_, _) =>
        {
            if (began is null)
            {
                NSWindow native = Native(window)!;
                began = NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.WillMiniaturizeNotification, _ =>
                {
                    starts++;
                    if (activationOnStart)
                    {
                        activationOnStart = false;
                        activationAccepted = window.Activate();
                        Record("动画开始中请求激活");
                    }
                    else if (pending is { } target)
                    {
                        pending = null;
                        window.WindowState = WindowState.Normal;
                        window.WindowState = WindowState.Maximized;
                        window.WindowState = target;
                    }
                    Record("原生最小化开始");
                }, native);
                completed = NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.DidMiniaturizeNotification, _ =>
                {
                    completions++; Record("原生最小化完成");
                }, native);
                restored = NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.DidDeminiaturizeNotification, _ =>
                {
                    restores++; Record("原生重开完成");
                }, native);
            }
            editor.Focus(); Record("显示");
        };
        window.LocationChanged += (_, _) => Record("位置同步");
        window.SizeChanged += (_, _) => Record("尺寸同步");
        window.StateChanged += (_, _) => Record("状态同步");
        window.Activated += (_, _) => Record("实际激活");
        window.Deactivated += (_, _) => Record("实际失活");
        void RecordFocus(string action) => NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            if (window.IsVisible) Record(action);
        });
        editor.GotKeyboardFocus += (_, _) => RecordFocus("编辑焦点");
        editor.LostKeyboardFocus += (_, _) => RecordFocus("编辑失焦");
        editor.TextChanged += (_, _) => Record("编辑内容");
        window.Closed += (_, _) =>
        {
            foreach (NSObject? token in new[] { began, completed, restored })
                if (token is not null) { NSNotificationCenter.DefaultCenter.RemoveObserver(token); token.Dispose(); }
            Record("关闭");
        };
        return window;
    }

    private static NSWindow? Native(Window window) => window.Handle == 0 ? null : Runtime.GetNSObject<NSView>(window.Handle)?.Window;
}
