using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace MacOSWindowLab;

internal static class WindowCaptionLab
{
    internal static void Show(Window owner)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x16, 0x32, 0x42));
        var window = new Window
        {
            Title = "标题栏 · Window 验证", Owner = owner,
            Width = 680, Height = 620, MinWidth = 540, MinHeight = 540,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc)),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var editor = new TextBox { Text = "标题栏切换后继续编辑🙂", Height = 48, FontSize = 16 };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink, MinHeight = 88 };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = "标题栏动作与恢复", FontSize = 26, Foreground = ink });
        body.Children.Add(new TextBlock
        {
            Text = "使用窗口按钮关闭、最小化和还原；首次关闭会取消。切换后继续编辑，检查焦点和内容是否保留。",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = ink
        });
        body.Children.Add(editor);
        body.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xe5, 0xf4, 0xf8)),
            Padding = new Thickness(14), CornerRadius = new CornerRadius(8), Child = status
        });
        var controls = new WrapPanel(); body.Children.Add(controls);
        int closing = 0;
        void Record(string action)
        {
            var bounds = window.RestoreBounds;
            status.Text = $"状态：{window.WindowState} · 标题栏：{window.TitleBarStyle}\n"
                + $"客户区：{window.Width:0.##} × {window.Height:0.##} DIP · 关闭次数：{closing}\n"
                + $"关闭 {window.IsShowCloseButton} · 最小化 {window.IsShowMinimizeButton} · 缩放 {window.IsShowMaximizeButton}";
            File.AppendAllText(Program.LogPrefix + "-caption.jsonl",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    pid = Environment.ProcessId, utc = DateTimeOffset.UtcNow, action, closing,
                    state = window.WindowState.ToString(), titleBar = window.TitleBarStyle.ToString(),
                    width = window.Width, height = window.Height,
                    restoreWidth = bounds.IsEmpty ? (double?)null : bounds.Width,
                    restoreHeight = bounds.IsEmpty ? (double?)null : bounds.Height,
                    window.IsShowCloseButton, window.IsShowMinimizeButton, window.IsShowMaximizeButton,
                    window.IsShowTitleBar, window.IsEnabled, text = editor.Text, focused = editor.IsKeyboardFocused
                }) + Environment.NewLine);
        }
        void Add(string label, Action<Button> action)
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(14, 8, 14, 8) };
            button.Click += (_, _) => { action(button); editor.Focus(); Record(label); };
            controls.Children.Add(button);
        }
        Add("隐藏关闭", b => { window.IsShowCloseButton = !window.IsShowCloseButton; b.Content = window.IsShowCloseButton ? "隐藏关闭" : "显示关闭"; });
        Add("隐藏最小化", b => { window.IsShowMinimizeButton = !window.IsShowMinimizeButton; b.Content = window.IsShowMinimizeButton ? "隐藏最小化" : "显示最小化"; });
        Add("隐藏缩放", b => { window.IsShowMaximizeButton = !window.IsShowMaximizeButton; b.Content = window.IsShowMaximizeButton ? "隐藏缩放" : "显示缩放"; });
        Add("隐藏标题栏", b => { window.IsShowTitleBar = !window.IsShowTitleBar; b.Content = window.IsShowTitleBar ? "隐藏标题栏" : "显示标题栏"; });
        Add("原生标题栏", b =>
        {
            window.TitleBarStyle = window.TitleBarStyle == WindowTitleBarStyle.Custom ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
            b.Content = window.TitleBarStyle == WindowTitleBarStyle.Custom ? "原生标题栏" : "自定义标题栏";
        });
        Add("最小尺寸", b =>
        {
            window.WindowState = WindowState.Normal;
            bool minimum = window.Width <= 540.1;
            window.Width = minimum ? 680 : 540; window.Height = minimum ? 620 : 540;
            b.Content = minimum ? "最小尺寸" : "常规尺寸";
        });
        Add("还原窗口大小", _ => window.WindowState = WindowState.Normal);
        Add("打开模态", _ =>
        {
            var dialog = new Window { Title = "标题栏模态验证", Owner = window, Width = 420, Height = 280, Background = window.Background, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
            panel.Children.Add(new TextBlock { Text = "当前窗口和标题栏按钮应保持禁用，直到此对话框结束。", TextWrapping = TextWrapping.Wrap, Foreground = ink, FontSize = 16 });
            var done = new Button { Content = "返回编辑", IsDefault = true, IsCancel = true, MinHeight = 44 };
            done.Click += (_, _) => dialog.DialogResult = true;
            panel.Children.Add(done); dialog.Content = panel;
            Record("打开模态"); dialog.ShowDialog(); Record("模态返回");
        });
        window.Content = body;
        window.StateChanged += (_, _) => Record("状态变化");
        window.SizeChanged += (_, _) => Record("尺寸变化");
        window.Activated += (_, _) => Record("激活");
        window.Closing += (_, e) => { closing++; e.Cancel = closing == 1; Record(e.Cancel ? "关闭取消" : "关闭接受"); };
        window.Closed += (_, _) => Record("已关闭");
        window.Show(); editor.Focus(); Record("已显示");
    }
}
