using System.Text;
using System.Text.Json;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Input;
using Window = Jalium.UI.Window;

namespace MacOSWindowLab;

internal sealed class WindowDragLab : Window
{
    private static readonly SolidColorBrush Ink = new(Color.FromRgb(0x16, 0x32, 0x42));
    private static readonly HashSet<IDataObject> Samples = [];
    private readonly TextBlock _status = new() { Text = "尚未接收 · 可从另一窗口或 Finder 拖入", FontSize = 15, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _history = new() { FontSize = 13, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
    private readonly List<string> _rows = [];
    private readonly CheckBox _cancel = new() { Content = "移动时取消" };
    private readonly CheckBox _requestDrop = new() { Content = "移动时请求放下" };
    private readonly Border _source;
    private readonly Border _target;
    private IDataObject? _retained;
    private Point? _pressed;
    private bool _dragging;
    private int _drops;
    private readonly string _identity = Guid.NewGuid().ToString("N")[..6];
    private static string Log => Program.LogPrefix + "-drag.jsonl";

    internal static Window Create(bool native = false, bool small = false)
    {
        var window = new WindowDragLab(native);
        if (small) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
        return window;
    }
    private WindowDragLab(bool native)
    {
        Title = native ? "拖放验证 · 原生标题栏" : "拖放验证 · 自定义标题栏";
        TitleBarStyle = native ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom;
        Width = 560; Height = 680; MinWidth = 520; MinHeight = 580; Left = 40; Top = 70;
        Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc));
        var body = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = "拖放 · 数据与窗口", FontSize = 28, Foreground = Ink });
        body.Children.Add(new TextBlock { Text = "拖动样例至接收区。Option 复制；Option + ⌘ 链接；Esc 取消。", FontSize = 15, Foreground = Ink, TextWrapping = TextWrapping.Wrap });
        _source = new Border { Height = 88, Padding = new Thickness(16), CornerRadius = new CornerRadius(8), Focusable = true,
            Background = new SolidColorBrush(Color.FromRgb(0xde, 0xef, 0xf4)),
            Child = new TextBlock { Text = "拖动样例 →\n中文🙂 · HTML · 两个文件 · 自定义对象", FontSize = 17, Foreground = Ink, TextWrapping = TextWrapping.Wrap } };
        AutomationProperties.SetName(_source, "拖放样例来源");
        _source.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            _pressed = e.GetPosition(_source); _source.CaptureMouse();
            Record("SourcePressed", new { point = _pressed.ToString(), Mouse.LeftButton });
        };
        _source.MouseUp += (_, _) => { _pressed = null; _source.ReleaseMouseCapture(); };
        _source.MouseMove += (_, e) =>
        {
            if (_dragging || _pressed is not { } pressed || e.LeftButton != MouseButtonState.Pressed) return;
            Point point = e.GetPosition(_source);
            if (Math.Abs(point.X - pressed.X) + Math.Abs(point.Y - pressed.Y) < 6) return;
            _pressed = null; _dragging = true; e.Handled = true;
            IDataObject sample = MakeSample(); Samples.Add(sample);
            Record("SourceBegin", new { formats = sample.GetFormats() });
            try
            {
                var effect = DragDrop.DoDragDrop(_source, sample, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                Record("SourceReturned", new { effect = effect.ToString(), Mouse.LeftButton });
            }
            finally { _dragging = false; _source.ReleaseMouseCapture(); }
        };
        _source.QueryContinueDrag += (_, e) =>
        {
            if (_cancel.IsChecked == true) { e.Action = DragAction.Cancel; e.Handled = true; }
            else if (_requestDrop.IsChecked == true) { e.Action = DragAction.Drop; e.Handled = true; }
            Record("Query", new { keys = e.KeyStates.ToString(), e.EscapePressed, action = e.Action.ToString(), e.Handled });
        };
        _source.GiveFeedback += (_, e) => Record("Feedback", new { effect = e.Effects.ToString() });
        body.Children.Add(_source);
        _target = new Border { Height = 126, AllowDrop = true, Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x29, 0x68, 0x82)), BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromRgb(0xed, 0xf5, 0xf8)) };
        AutomationProperties.SetName(_target, "拖放接收区");
        var targetBody = new StackPanel { Spacing = 10 };
        targetBody.Children.Add(new TextBlock { Text = "在此放下", FontSize = 21, Foreground = Ink }); targetBody.Children.Add(_status);
        _target.Child = targetBody; body.Children.Add(_target);
        _target.DragEnter += (_, e) => { e.Effects = e.AllowedEffects; Record("Enter", new { formats = e.Data.GetFormats(), keys = e.KeyStates.ToString() }); };
        _target.DragOver += (_, e) => e.Effects = e.AllowedEffects;
        _target.DragLeave += (_, _) => Record("Leave");
        _target.Drop += (_, e) => { _retained = e.Data; _drops++; e.Effects = e.AllowedEffects; ReadRetained("Drop"); };
        var options = new WrapPanel { HorizontalSpacing = 20, VerticalSpacing = 10 }; options.Children.Add(_cancel); options.Children.Add(_requestDrop); body.Children.Add(options);
        var buttons = new WrapPanel { HorizontalSpacing = 10, VerticalSpacing = 10 };
        AddButton(buttons, "打开另一窗口", () => { var next = new WindowDragLab(TitleBarStyle != WindowTitleBarStyle.Native) { Left = 640, Top = 70 }; next.Show(); });
        AddButton(buttons, "复查接收数据", () => ReadRetained("RetainedRead"));
        AddButton(buttons, "本地读取样例", () => { _retained = MakeSample(); ReadRetained("LocalSample"); });
        body.Children.Add(buttons);
        body.Children.Add(new TextBlock { Text = "事件记录", FontSize = 16, Foreground = Ink }); body.Children.Add(_history);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += (_, _) => { WindowInputTrace.Attach(this); Record("Loaded"); };
        Closed += (_, _) => Record("Closed");
    }

    private static void AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, MinHeight = 40, Padding = new Thickness(12, 6, 12, 6) };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }

    private static IDataObject MakeSample()
    {
        const string dir = "/Users/jal/Documents/Repos/Jalium.UI/artifacts/macos-window-v87/drag-files";
        Directory.CreateDirectory(dir);
        string[] files = [Path.Combine(dir, "第一份 样例.txt"), Path.Combine(dir, "second.txt")];
        foreach (string file in files) File.WriteAllText(file, "Jalium 拖放样例🙂");
        var data = new DataObject(); data.SetData(DataFormats.UnicodeText, "跨窗口保留中文🙂");
        data.SetData(DataFormats.Html, "<b>跨窗口保留中文🙂</b>"); data.SetData(DataFormats.FileDrop, files);
        data.SetData("Jalium.WindowDrag.Sample", new Sample("原始对象", 87));
        return data;
    }
    private sealed record Sample(string Name, int Version);

    private void ReadRetained(string kind)
    {
        if (_retained == null) { _status.Text = "还没有数据，请先拖入样例或文件。"; return; }
        string? text = _retained.GetData(DataFormats.UnicodeText) as string;
        string? html = _retained.GetData(DataFormats.Html) as string;
        string[] files = _retained.GetData(DataFormats.FileDrop) as string[] ?? [];
        object? custom = _retained.GetData("Jalium.WindowDrag.Sample");
        _status.Text = $"已接收 {_drops} 次 · {files.Length} 个文件\n{text ?? "无文本"} · HTML {(html == null ? "无" : "有")} · 对象 {(custom is Sample ? "保留" : "外部数据")}";
        Record(kind, new { text, html, files, custom = custom?.ToString(), original = Samples.Contains(_retained), formats = _retained.GetFormats() });
    }
    private void Record(string kind, object? detail = null)
    {
        File.AppendAllText(Log, JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, pid = Environment.ProcessId,
            window = _identity, Title, kind, drops = _drops, detail }) + "\n");
        if (kind is "Query" or "Feedback") return;
        _rows.Add($"{kind} · 接收 {_drops} 次");
        _history.Text = string.Join("\n", _rows.TakeLast(5));
    }
}
