using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>
/// Demo 专用的页面布局辅助:统一的标题 + 描述 + 内容区样式,让几个示例页面保持一致外观。
/// </summary>
internal static class PageLayout
{
    public static StackPanel Build(string title, string description, params FrameworkElement[] content)
    {
        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(24)
        };

        root.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 24,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 4)
        });

        root.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        foreach (var child in content)
        {
            root.Children.Add(child);
        }

        return root;
    }

    public static TextBlock Body(string text) => new()
    {
        Text = text,
        FontSize = 14,
        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 4)
    };

    public static TextBlock Mono(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontFamily = new FontFamily("Consolas"),
        Foreground = new SolidColorBrush(Color.FromRgb(250, 179, 135)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2)
    };

    public static Button DemoButton(string text, Action onClick)
    {
        var btn = new Button
        {
            Background = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            MinWidth = 180,
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 4, 8, 4),
            Content = new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White)
            }
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
