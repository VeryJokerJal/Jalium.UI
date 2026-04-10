using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.NuGetTest.Desktop;

class Program
{
    static void Main()
    {
        // 验证核心类型可以从 NuGet 包解析
        var app = new Application();
        var window = new Window
        {
            Title = "NuGet Package Test - Desktop",
            Width = 800,
            Height = 600,
            Background = new SolidColorBrush(Colors.White)
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Jalium.UI.Desktop NuGet 包测试成功！ Test",
            FontSize = 24,
            Foreground = new SolidColorBrush(Colors.Black)
        });

        panel.Children.Add(new Button
        {
            Content = new TextBlock { Text = "点击测试", FontSize = 16 },
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(16, 8, 16, 8)
        });

        window.Content = panel;
        app.MainWindow = window;

        Console.WriteLine("[Desktop NuGet Test] 所有类型解析成功:");
        Console.WriteLine($"  Application: {app.GetType().FullName}");
        Console.WriteLine($"  Window: {window.GetType().FullName}");
        Console.WriteLine($"  StackPanel: {panel.GetType().FullName}");
        Console.WriteLine($"  TextBlock: {typeof(TextBlock).FullName}");
        Console.WriteLine($"  Button: {typeof(Button).FullName}");
        Console.WriteLine($"  SolidColorBrush: {typeof(SolidColorBrush).FullName}");
        Console.WriteLine("[Desktop NuGet Test] 通过！");
        app.Run();
    }
}
