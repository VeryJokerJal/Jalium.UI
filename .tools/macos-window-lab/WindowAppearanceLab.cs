using AppKit;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using System.ComponentModel;
using Window = Jalium.UI.Window;
using Orientation = Jalium.UI.Controls.Orientation;

namespace MacOSWindowLab;

internal static class WindowAppearanceLab
{
    private static SolidColorBrush Ink => new(Color.FromRgb(0x16, 0x32, 0x42));
    private static SolidColorBrush Paper => new(Color.FromRgb(0xf5, 0xfa, 0xfc));

    internal static void Show(Window owner)
    {
        var pattern = new Grid();
        for (int i = 0; i < 3; i++) pattern.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 2; i++) pattern.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Color[] colors = [Color.FromRgb(0x16, 0x32, 0x42), Color.FromRgb(0x08, 0x91, 0xb2),
            Color.FromRgb(0xee, 0xc1, 0x7e), Color.FromRgb(0xee, 0xc1, 0x7e),
            Color.FromRgb(0x16, 0x32, 0x42), Color.FromRgb(0x08, 0x91, 0xb2)];
        for (int i = 0; i < colors.Length; i++)
        {
            var block = new Border
            {
                Background = new SolidColorBrush(colors[i]),
                Child = new TextBlock
                {
                    Text = $"下层窗口 {(char)('A' + i)}", FontSize = 42,
                    Foreground = i is 2 or 3 ? Ink : Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(block, i % 3); Grid.SetRow(block, i / 3); pattern.Children.Add(block);
        }
        var reference = new Window
        {
            Title = "下层彩色窗口 · 合成对照", Width = 1000, Height = 900, MinWidth = 760, MinHeight = 680,
            Owner = owner, Content = pattern, Background = Paper, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        reference.Show();

        var status = new TextBlock { FontSize = 14, Foreground = Ink, TextWrapping = TextWrapping.Wrap };
        var editor = new TextBox { Text = "背景切换后继续编辑 · Window A", MinHeight = 44, FontSize = 17 };
        Jalium.UI.Automation.AutomationProperties.SetName(editor, "窗口外观验证编辑框");
        var body = new StackPanel { Spacing = 14 };
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(new TextBlock { Text = "透明与窗口背景", FontSize = 26, Foreground = Ink });
        heading.Children.Add(new TextBlock
        {
            Text = "切换透明、背景和原生材质，观察下层彩色窗口。窗口尺寸与编辑内容应保持。",
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Ink
        });
        heading.Children.Add(status);
        body.Children.Add(Card(heading));
        var observation = new Border
        {
            Height = 170, BorderBrush = Ink, BorderThickness = new Thickness(1), Padding = new Thickness(12),
            Child = new Border
            {
                Background = Paper, Padding = new Thickness(10, 6, 10, 6),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = "观察区：透明时应看到下层窗口", FontSize = 14, Foreground = Ink }
            }
        };
        body.Children.Add(observation);
        var controls = new StackPanel { Spacing = 12 };
        controls.Children.Add(new TextBlock { Text = "切换后检查输入与焦点", FontSize = 15, Foreground = Ink });
        controls.Children.Add(editor);
        body.Children.Add(Card(controls));
        var window = new Window
        {
            Title = "透明与背景 · Window 验证", Width = 700, Height = 830, MinWidth = 520, MinHeight = 560,
            Owner = reference, Background = Paper,
            Content = new ScrollViewer { Content = new Border { Padding = new Thickness(24), Child = body },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        string background = "不透明";
        Button? transparency = null, topmost = null, chrome = null;
        var opacitySource = new OpacitySource();

        void Refresh(string kind)
        {
            var bounds = window.RestoreBounds;
            var view = window.Handle == 0 ? null : ObjCRuntime.Runtime.GetNSObject<NSView>(window.Handle);
            var native = view?.Window;
            var material = native?.ContentView?.Subviews.OfType<NSVisualEffectView>().FirstOrDefault();
            status.Text = $"允许透明：{window.AllowsTransparency} · 背景：{background} · 整窗：{window.Opacity:P0} · 原生：{native?.AlphaValue:P0}\n" +
                $"材质：{window.SystemBackdrop} · 置顶：{window.Topmost} · 边框：{window.WindowStyle}\n" +
                $"客户区：{window.Width:0.##} × {window.Height:0.##} · 还原：{bounds.Width:0.##} × {bounds.Height:0.##}";
            if (transparency is not null) transparency.Content = window.AllowsTransparency ? "关闭透明" : "允许透明";
            if (topmost is not null) topmost.Content = window.Topmost ? "取消置顶" : "置顶";
            if (chrome is not null) chrome.Content = window.WindowStyle == WindowStyle.None ? "恢复边框" : "移除边框";
            var row = new WindowAppearanceSnapshot(kind, Environment.ProcessId, window.AllowsTransparency,
                background, window.Opacity, window.SystemBackdrop.ToString(), window.Topmost, window.WindowStyle.ToString(),
                window.Width, window.Height, bounds.IsEmpty ? null : bounds.Width, bounds.IsEmpty ? null : bounds.Height,
                editor.Text, editor.IsKeyboardFocused, native?.IsOpaque, view?.Layer?.Opaque,
                native?.AlphaValue, native is null ? null : (long)native.Level,
                material?.Material.ToString(), material?.Hidden, DateTimeOffset.UtcNow);
            File.AppendAllText(Program.LogPrefix + "-appearance.jsonl",
                System.Text.Json.JsonSerializer.Serialize(row, WindowLabJsonContext.Default.WindowAppearanceSnapshot) + "\n");
        }
        Button Action(string label, System.Action apply, bool keepAnimation = false)
        {
            var button = new Button { Content = label, MinHeight = 42, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 10) };
            button.Click += (_, _) =>
            {
                if (!keepAnimation) window.BeginAnimation(UIElement.OpacityProperty, (AnimationTimeline?)null);
                apply(); editor.Focus(); Refresh(label);
            };
            return button;
        }
        void AddRow(string label, params Button[] buttons)
        {
            controls.Children.Add(new TextBlock { Text = label, FontSize = 15, Foreground = Ink });
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var button in buttons) row.Children.Add(button);
            controls.Children.Add(row);
        }
        void Background(string label, Brush brush)
        {
            background = label; window.Background = brush;
        }
        transparency = Action("允许透明", () => window.AllowsTransparency = !window.AllowsTransparency);
        AddRow("窗口背景", transparency,
            Action("不透明背景", () => Background("不透明", Paper)),
            Action("透明背景", () => Background("透明", Brushes.Transparent)),
            Action("半透明背景", () => Background("半透明", new SolidColorBrush(Color.FromArgb(96, 245, 250, 252)))));
        AddRow("整窗透明度", Action("整窗 100%", () => window.Opacity = 1), Action("整窗 65%", () => window.Opacity = 0.65));
        AddRow("透明度属性更新",
            Action("依赖属性 35%", () => window.SetValue(UIElement.OpacityProperty, .35)),
            Action("绑定 35% / 80%", () =>
            {
                if (BindingOperations.GetBindingExpression(window, UIElement.OpacityProperty) is null)
                {
                    opacitySource.Value = .35;
                    BindingOperations.SetBinding(window, UIElement.OpacityProperty,
                        new Binding(nameof(OpacitySource.Value)) { Source = opacitySource, Mode = BindingMode.OneWay });
                }
                else opacitySource.Value = opacitySource.Value < .5 ? .8 : .35;
            }),
            Action("样式 50%", () =>
            {
                window.ClearValue(UIElement.OpacityProperty);
                var style = new Style { TargetType = typeof(Window) };
                style.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = .5 });
                window.Style = style;
            }));
        AddRow("动画期间应使用当前动画值",
            Action("3 秒透明度动画", () => window.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation { From = .3, To = .85, Duration = TimeSpan.FromSeconds(3), FillBehavior = FillBehavior.Stop })),
            Action("动画中改基值 65%", () => window.Opacity = .65, keepAnimation: true),
            Action("查看当前值", () => { }, keepAnimation: true));
        AddRow("原生材质",
            Action("无材质", () => window.SystemBackdrop = WindowBackdropType.None),
            Action("Auto", () => window.SystemBackdrop = WindowBackdropType.Auto),
            Action("Mica", () => window.SystemBackdrop = WindowBackdropType.Mica),
            Action("Mica Alt", () => window.SystemBackdrop = WindowBackdropType.MicaAlt),
            Action("Acrylic", () => window.SystemBackdrop = WindowBackdropType.Acrylic));
        topmost = Action("置顶", () => window.Topmost = !window.Topmost);
        chrome = Action("移除边框", () => window.WindowStyle = window.WindowStyle == WindowStyle.None ? WindowStyle.SingleBorderWindow : WindowStyle.None);
        AddRow("窗口参与", topmost, chrome,
            Action("最小尺寸", () => { window.Width = window.MinWidth; window.Height = window.MinHeight; }),
            Action("标准尺寸", () => { window.Width = 700; window.Height = 830; }),
            Action("关闭验证窗口", window.Close));
        window.Loaded += (_, _) => { editor.Focus(); Refresh("显示"); };
        window.Activated += (_, _) => Refresh("激活");
        window.Deactivated += (_, _) => Refresh("失活");
        window.SizeChanged += (_, _) => Refresh("尺寸");
        window.Closed += (_, _) => { Refresh("关闭"); if (reference.Handle != 0) reference.Close(); };
        window.Show(); Refresh("创建");
    }

    private static Border Card(UIElement child) => new()
    {
        Background = Paper, Padding = new Thickness(16), CornerRadius = new CornerRadius(8), Child = child
    };

    private sealed class OpacitySource : INotifyPropertyChanged
    {
        private double _value = .35;
        public event PropertyChangedEventHandler? PropertyChanged;
        public double Value
        {
            get => _value;
            set { if (_value == value) return; _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
    }
}

internal sealed record WindowAppearanceSnapshot(string kind, int pid, bool allowsTransparency,
    string background, double opacity, string backdrop, bool topmost, string style,
    double width, double height, double? restoreWidth, double? restoreHeight,
    string text, bool editorFocused, bool? nativeOpaque, bool? layerOpaque,
    double? nativeAlpha, long? nativeLevel, string? nativeMaterial, bool? nativeMaterialHidden, DateTimeOffset utc);
