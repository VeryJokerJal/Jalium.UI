using Android.App;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Window = Jalium.UI.Controls.Window;
using Button = Jalium.UI.Controls.Button;
using TextBlock = Jalium.UI.Controls.TextBlock;
using StackPanel = Jalium.UI.Controls.StackPanel;
using Orientation = Jalium.UI.Controls.Orientation;

namespace Jalium.UI.NuGetTest.Android;

[Activity(
    Label = "NuGet Test",
    MainLauncher = true,
    Theme = "@android:style/Theme.NoTitleBar.Fullscreen")]
public class MainActivity : JaliumActivity
{
    protected override Application CreateApplication()
    {
        var app = new Application();

        var window = new Window
        {
            Title = "NuGet Package Test - Android",
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 46))
        };

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        root.Children.Add(new TextBlock
        {
            Text = "Jalium.UI.Android NuGet 包测试成功！",
            FontSize = 28,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        root.Children.Add(new Button
        {
            Content = new TextBlock
            {
                Text = "点击测试",
                FontSize = 20,
                Foreground = new SolidColorBrush(Colors.White)
            },
            Margin = new Thickness(0, 24, 0, 0),
            Padding = new Thickness(24, 12, 24, 12)
        });

        window.Content = root;
        app.MainWindow = window;
        return app;
    }
}
