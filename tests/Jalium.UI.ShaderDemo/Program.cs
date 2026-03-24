using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.ShaderDemo;

// ── 创建窗口 ──
var app = new Application();

var window = new Window
{
    Title = "Shader Demo — Sepia + Vignette",
    Width = 720,
    Height = 520
};

// ── 创建自定义 Shader Effect ──
var effect = new SepiaVignetteEffect
{
    Intensity = 0.8,
    Vignette = new Point(0.75, 0.45)
};

// ── 被施加效果的内容区域 ──
// 用一个带渐变背景的 Border 模拟图片内容
var contentBorder = new Border
{
    Background = new LinearGradientBrush(
        Color.FromRgb(30, 120, 200),   // 蓝
        Color.FromRgb(240, 180, 50),   // 橙
        0),                             // 角度
    CornerRadius = new CornerRadius(12),
    Width = 500,
    Height = 300,
    Effect = effect,
    Child = new TextBlock
    {
        Text = "Custom Shader Effect",
        FontSize = 28,
        Foreground = new SolidColorBrush(Colors.White),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    }
};

// ── 控制面板：滑块调参 ──
var intensityLabel = new TextBlock { Text = "Intensity: 0.80", FontSize = 14, Margin = new Thickness(0, 0, 0, 4) };
var intensitySlider = new Slider { Minimum = 0, Maximum = 100, Value = 80, Width = 400 };
intensitySlider.ValueChanged += (s, e) =>
{
    var val = e.NewValue / 100.0;
    effect.Intensity = val;
    intensityLabel.Text = $"Intensity: {val:F2}";
};

var radiusLabel = new TextBlock { Text = "Vignette Radius: 0.75", FontSize = 14, Margin = new Thickness(0, 8, 0, 4) };
var radiusSlider = new Slider { Minimum = 0, Maximum = 100, Value = 75, Width = 400 };
radiusSlider.ValueChanged += (s, e) =>
{
    var r = e.NewValue / 100.0;
    effect.Vignette = new Point(r, effect.Vignette.Y);
    radiusLabel.Text = $"Vignette Radius: {r:F2}";
};

var softnessLabel = new TextBlock { Text = "Vignette Softness: 0.45", FontSize = 14, Margin = new Thickness(0, 8, 0, 4) };
var softnessSlider = new Slider { Minimum = 0, Maximum = 100, Value = 45, Width = 400 };
softnessSlider.ValueChanged += (s, e) =>
{
    var s2 = e.NewValue / 100.0;
    effect.Vignette = new Point(effect.Vignette.X, s2);
    softnessLabel.Text = $"Vignette Softness: {s2:F2}";
};

// ── 布局 ──
var controlPanel = new StackPanel
{
    Orientation = Orientation.Vertical,
    Margin = new Thickness(20, 12, 20, 0)
};
controlPanel.Children.Add(intensityLabel);
controlPanel.Children.Add(intensitySlider);
controlPanel.Children.Add(radiusLabel);
controlPanel.Children.Add(radiusSlider);
controlPanel.Children.Add(softnessLabel);
controlPanel.Children.Add(softnessSlider);

var root = new StackPanel
{
    Orientation = Orientation.Vertical,
    HorizontalAlignment = HorizontalAlignment.Center,
    Margin = new Thickness(0, 20, 0, 0)
};
root.Children.Add(contentBorder);
root.Children.Add(controlPanel);

window.Content = root;

app.Run(window);
