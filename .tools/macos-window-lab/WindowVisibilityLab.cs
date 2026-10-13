using AppKit;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using ObjCRuntime;
using System.ComponentModel;
using System.Text.Json;

namespace MacOSWindowLab;

internal static class WindowVisibilityLab
{
    internal static void Show(Window owner)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x16, 0x32, 0x42));
        var paper = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc));
        var body = new StackPanel { Spacing = 16 };
        var status = new TextBlock { FontSize = 15, Foreground = ink, TextWrapping = TextWrapping.Wrap };
        var closedResult = new TextBlock { FontSize = 15, Foreground = ink, TextWrapping = TextWrapping.Wrap };
        var panel = new Window
        {
            Title = "显示与隐藏 · 控制面板", Width = 650, Height = 640, MinWidth = 420, MinHeight = 380,
            Owner = owner, Background = paper,
            Content = new ScrollViewer { Content = new Border { Padding = new Thickness(24), Child = body }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
        Window? target = null;
        VisibilitySource? source = null;
        int creates = 0, closes = 0;
        string closedCheck = "尚未检查";
        int? rejectedClosedRequests = null;
        bool? closedStateReadable = null;
        body.Children.Add(new TextBlock { Text = "窗口显示与隐藏", FontSize = 28, Foreground = ink });
        body.Children.Add(new TextBlock
        {
            Text = "先打开编辑窗口并输入文字，再从这里隐藏和恢复它。隐藏后应保留文字和窗口句柄；控制面板继续可用。",
            FontSize = 15, Foreground = ink, TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0xe5, 0xf4, 0xf8)),
            Padding = new Thickness(16), CornerRadius = new CornerRadius(8), Child = status });
        Row(("首次显示（绑定）", () => Bound(Visibility.Visible)),
            ("隐藏（绑定）", () => Bound(Visibility.Hidden)),
            ("折叠（绑定）", () => Bound(Visibility.Collapsed)),
            ("恢复显示（绑定）", () => Bound(Visibility.Visible)));
        Row(("Show()", () => EnsureTarget().Show()), ("Hide()", () => EnsureTarget().Hide()),
            ("清除 Visibility", () => EnsureTarget().ClearValue(UIElement.VisibilityProperty)),
            ("清除绑定", () => BindingOperations.ClearBinding(EnsureTarget(), UIElement.VisibilityProperty)));
        Row(("应用隐藏样式并解绑", () =>
        {
            var window = EnsureTarget(); var style = new Style(typeof(Window));
            style.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Hidden)); window.Style = style;
            window.ClearValue(UIElement.VisibilityProperty);
        }), ("通过样式显示", () =>
        {
            var window = EnsureTarget(); var style = new Style(typeof(Window));
            style.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible)); window.Style = style;
            window.ClearValue(UIElement.VisibilityProperty);
        }));
        Row(("查看状态", () => { }), ("关闭编辑窗口", () => target?.Close()), ("关闭控制面板", panel.Close));
        Row(("关闭后再显示检查", VerifyClosedTarget));
        body.Children.Add(closedResult);
        Row(("最小尺寸", () => { panel.Width = panel.MinWidth; panel.Height = panel.MinHeight; }),
            ("标准尺寸", () => { panel.Width = 650; panel.Height = 640; }));
        body.Children.Add(new TextBlock
        {
            Text = "Show() 和 Hide() 应保留绑定。清值或解绑后采用样式；没有样式时回到 Collapsed。首次通过属性显示在下一轮调度创建窗口。",
            FontSize = 15, Foreground = ink, TextWrapping = TextWrapping.Wrap
        });
        Refresh("创建控制面板"); panel.Show();

        Window EnsureTarget()
        {
            if (target != null) return target;
            closedCheck = "尚未检查";
            rejectedClosedRequests = null; closedStateReadable = null;
            var editor = new TextBox { Text = "请输入并保留：中文 alpha 👩‍👩‍👧‍👦", AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, MinHeight = 140, FontSize = 18, Padding = new Thickness(12) };
            Jalium.UI.Automation.AutomationProperties.SetName(editor, "显示隐藏验证编辑器");
            target = new Window { Title = "显示与隐藏 · 编辑窗口", Width = 520, Height = 380, MinWidth = 320, MinHeight = 260,
                Owner = panel, Background = paper, Content = new Border { Padding = new Thickness(24), Child = editor } };
            source = new VisibilitySource { Value = Visibility.Hidden };
            BindingOperations.SetBinding(target, UIElement.VisibilityProperty, new Binding(nameof(VisibilitySource.Value)) { Source = source });
            target.SourceInitialized += (_, _) => { creates++; Refresh("原生窗口已创建"); };
            target.Shown += (_, _) => Refresh("编辑窗口已显示");
            target.Hiding += (_, _) => Refresh("Hide() 请求");
            target.Closed += (_, _) => { closes++; target = null; source = null; Refresh("编辑窗口已关闭"); };
            return target;
        }
        void Bound(Visibility value)
        {
            var window = EnsureTarget();
            if (BindingOperations.GetBindingExpression(window, UIElement.VisibilityProperty) == null)
                BindingOperations.SetBinding(window, UIElement.VisibilityProperty, new Binding(nameof(VisibilitySource.Value)) { Source = source });
            source!.Value = value;
        }
        void VerifyClosedTarget()
        {
            if (target is null || target.Handle == 0)
            {
                closedCheck = "请先显示编辑窗口，再检查关闭后的行为。";
                rejectedClosedRequests = null; closedStateReadable = null;
                return;
            }
            var closedWindow = target;
            closedWindow.Close();
            int rejected = 0;
            foreach (var writer in new Action[]
            {
                () => closedWindow.SetValue(UIElement.VisibilityProperty, Visibility.Visible),
                () => closedWindow.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible),
                () => closedWindow.Visibility = Visibility.Visible
            })
            {
                try { writer(); }
                catch (InvalidOperationException) { rejected++; }
            }
            bool readable;
            try
            {
                readable = closedWindow.Visibility == Visibility.Collapsed && !closedWindow.IsVisible &&
                    closedWindow.Handle == 0 && closedWindow.RestoreBounds.IsEmpty;
            }
            catch (Exception) { readable = false; }
            rejectedClosedRequests = rejected; closedStateReadable = readable;
            closedCheck = rejected == 3 && readable
                ? "通过：3 种显示请求均被拒绝；关闭状态仍可读取，句柄为 0。"
                : $"失败：拒绝 {rejected}/3；关闭状态可读取：{readable}。";
        }
        void Row(params (string Label, Action Apply)[] actions)
        {
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var (label, apply) in actions)
            {
                var button = new Button { Content = label, MinHeight = 44, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 12, 12) };
                button.Click += (_, _) => { apply(); Refresh(label); };
                row.Children.Add(button);
            }
            body.Children.Add(row);
        }
        void Refresh(string action)
        {
            var native = target?.Handle is { } handle && handle != 0 ? Runtime.GetNSObject<NSView>(handle)?.Window : null;
            bool bound = target != null && BindingOperations.GetBindingExpression(target, UIElement.VisibilityProperty) != null;
            string managedState = target?.Visibility.ToString() ?? (closes > 0 ? "编辑窗口已关闭" : "尚未创建");
            status.Text = $"托管状态：{managedState}\n原生可见：{native?.IsVisible.ToString() ?? "没有句柄"}　绑定：{(bound ? "保留" : "无")}\n原生创建 {creates} 次，关闭 {closes} 次\n最近动作：{action}\n关闭后检查：{closedCheck}";
            closedResult.Text = $"关闭后检查：{closedCheck}";
            File.AppendAllText(Program.LogPrefix + "-visibility.jsonl", JsonSerializer.Serialize(new
            { pid = Environment.ProcessId, at = DateTimeOffset.UtcNow, action, handle = target?.Handle.ToInt64(), visibility = target?.Visibility.ToString(),
                nativeVisible = native?.IsVisible, bound, creates, closes, closedCheck, rejectedClosedRequests, closedStateReadable }) + "\n");
        }
    }
    private sealed class VisibilitySource : INotifyPropertyChanged
    {
        private Visibility _value;
        public Visibility Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
