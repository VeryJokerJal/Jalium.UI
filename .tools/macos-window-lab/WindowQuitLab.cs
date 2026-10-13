using AppKit;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using ObjCRuntime;

namespace MacOSWindowLab;

internal static class WindowQuitLab
{
    private static Action<string, bool?>? _record;

    internal static void RecordQuitDecision(NSApplicationTerminateReply reply) => _record?.Invoke($"退出答复 {reply}", null);
    internal static void RecordReopen(bool visible, bool defaultHandling) =>
        _record?.Invoke($"Dock 重开，AppKit 可见标记 {visible}，默认处理 {defaultHandling}", visible);

    internal static Window Create(bool nativeTitleBar, bool small)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x16, 0x32, 0x42));
        var window = new Window
        {
            Title = "退出与重开 · Window 验证",
            Width = small ? 520 : 680, Height = small ? 580 : 760, MinWidth = 520, MinHeight = 580,
            TitleBarStyle = nativeTitleBar ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
        };
        var editor = new TextBox { Text = "退出取消后继续编辑中文🙂", MinHeight = 48, FontSize = 16 };
        AutomationProperties.SetName(editor, "退出与重开验证编辑框");
        var cancel = new CheckBox { Content = "取消这次关闭", IsChecked = true, MinHeight = 44, FontSize = 15 };
        var reentrant = new CheckBox { Content = "关闭回调中发起退出", MinHeight = 44, FontSize = 15 };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink, MinHeight = 84 };
        var history = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = ink, MinHeight = 64 };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "退出取消与窗口重开", FontSize = 28, Foreground = ink });
        body.Children.Add(new TextBlock
        {
            Text = "按 ⌘Q 取消一次退出，再继续编辑。勾选关闭回调中发起退出后，关闭窗口，再重试 ⌘Q。",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink
        });
        var label = new TextBlock { Text = "验证编辑内容", FontSize = 15, Foreground = ink };
        AutomationProperties.SetLabeledBy(editor, label);
        body.Children.Add(label);
        body.Children.Add(editor);
        body.Children.Add(cancel);
        body.Children.Add(reentrant);
        body.Children.Add(new TextBlock { Text = "关闭回调中设置的退出策略", FontSize = 15, Foreground = ink });
        var choices = new StackPanel { Spacing = 4 };
        ShutdownMode chosen = ShutdownMode.OnExplicitShutdown;
        ShutdownMode? observed = null;
        int closing = 0;
        long initialHandle = 0;
        var events = new List<string>();

        static string Policy(ShutdownMode mode) => mode switch
        {
            ShutdownMode.OnLastWindowClose => "关闭最后窗口时退出",
            ShutdownMode.OnMainWindowClose => "关闭主窗口时退出",
            _ => "仅显式退出"
        };
        void Record(string action, bool? reopenVisible = null)
        {
            var application = Jalium.UI.Application.Current!;
            var native = window.Handle == 0 ? null : Runtime.GetNSObject<NSView>(window.Handle)?.Window;
            status.Text = $"当前策略：{Policy(application.ShutdownMode)}\n"
                + $"回调选择：{Policy(chosen)} · 关闭请求：{closing}\n"
                + $"回调读到：{(observed is null ? "尚未关闭" : Policy(observed.Value))}\n"
                + $"窗口焦点：{(native?.IsKeyWindow == true ? "有" : "无")} · 编辑焦点：{(editor.IsKeyboardFocused ? "有" : "无")}";
            events.Add(action);
            history.Text = string.Join(Environment.NewLine, events.TakeLast(3));
            var snapshot = new WindowQuitSnapshot(Environment.ProcessId, DateTimeOffset.UtcNow, action, reopenVisible,
                application.ShutdownMode.ToString(), chosen.ToString(), observed?.ToString(), closing,
                cancel.IsChecked == true, editor.Text, editor.IsKeyboardFocused, reentrant.IsChecked == true, (long)window.Handle, initialHandle,
                window.IsActive, window.Visibility.ToString(), window.Width, window.Height, window.TitleBarStyle.ToString(),
                window.WindowState.ToString(), native?.IsKeyWindow == true, native?.IsMainWindow == true,
                native?.IsMiniaturized == true, native?.IsVisible == true);
            File.AppendAllText(Program.LogPrefix + "-quit.jsonl",
                System.Text.Json.JsonSerializer.Serialize(snapshot, WindowLabJsonContext.Default.WindowQuitSnapshot) + Environment.NewLine);
        }
        foreach (var policy in new[] { ShutdownMode.OnLastWindowClose, ShutdownMode.OnMainWindowClose, ShutdownMode.OnExplicitShutdown })
        {
            var choice = new RadioButton
            {
                Content = Policy(policy), GroupName = "QuitPolicy", MinHeight = 44, FontSize = 15,
                IsChecked = policy == chosen
            };
            choice.Checked += (_, _) => { chosen = policy; Record("选择关闭回调策略"); };
            choices.Children.Add(choice);
        }
        body.Children.Add(choices);
        body.Children.Add(new Border
        {
            Child = status, Background = new SolidColorBrush(Color.FromRgb(0xe5, 0xf4, 0xf8)),
            Padding = new Thickness(14), CornerRadius = new CornerRadius(8)
        });
        var actions = new WrapPanel();
        void AddAction(string title, Action action)
        {
            var button = new Button { Content = title, MinHeight = 44, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(16, 8, 16, 8) };
            button.Click += (_, _) => action();
            actions.Children.Add(button);
        }
        AddAction("隐藏窗口", () => { Record("隐藏请求"); window.Hide(); Record("窗口已隐藏"); });
        AddAction("最小化窗口", () => { Record("最小化请求"); window.WindowState = WindowState.Minimized; });
        AddAction("退出应用", () => NSApplication.SharedApplication.Terminate(null));
        AddAction("关闭窗口", window.Close);
        body.Children.Add(actions);
        body.Children.Add(history);
        window.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _record = Record;
        window.Closing += (_, args) =>
        {
            var application = Jalium.UI.Application.Current!;
            observed = application.ShutdownMode;
            application.ShutdownMode = chosen;
            closing++;
            if (reentrant.IsChecked == true)
            {
                Record("关闭回调中请求退出");
                NSApplication.SharedApplication.Terminate(null);
            }
            args.Cancel = cancel.IsChecked == true;
            Record(args.Cancel ? "关闭已取消" : "关闭已接受");
        };
        window.Loaded += (_, _) => { initialHandle = (long)window.Handle; editor.Focus(); Record("窗口已显示"); };
        window.Activated += (_, _) => Record("窗口已激活");
        window.Deactivated += (_, _) => Record("窗口已失活");
        editor.TextChanged += (_, _) => Record("编辑内容变化");
        editor.IsKeyboardFocusedChanged += (_, _) =>
            Record(editor.IsKeyboardFocused ? "编辑焦点取得" : "编辑焦点离开");
        cancel.Checked += (_, _) => Record("取消关闭保护已开启");
        cancel.Unchecked += (_, _) => Record("取消关闭保护已关闭");
        reentrant.Checked += (_, _) => Record("关闭回调退出已开启");
        reentrant.Unchecked += (_, _) => Record("关闭回调退出已关闭");
        window.StateChanged += (_, _) => Record("窗口状态变化");
        window.Closed += (_, _) => { Record("窗口已关闭"); _record = null; };
        return window;
    }
}

internal sealed record WindowQuitSnapshot(int pid, DateTimeOffset utc, string action, bool? reopenVisible,
    string policy, string chosen, string? observed, int closing, bool cancel, string text, bool editorFocused, bool reentrant,
    long handle, long initialHandle, bool IsActive, string visibility, double Width, double Height,
    string titleBar, string state, bool nativeKey, bool nativeMain, bool nativeMinimized, bool nativeVisible);
