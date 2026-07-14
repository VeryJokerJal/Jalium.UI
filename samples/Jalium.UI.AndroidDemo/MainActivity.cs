using Android.App;
using Jalium.UI.Media;
using Button = Jalium.UI.Controls.Button;
using Orientation = Jalium.UI.Controls.Orientation;
using Window = Jalium.UI.Window;
using TextBlock = Jalium.UI.Controls.TextBlock;
using StackPanel = Jalium.UI.Controls.StackPanel;

namespace Jalium.UI.AndroidDemo;

[Activity(
    Label = "Jalium Demo",
    MainLauncher = true,
    Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
    ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation
        | Android.Content.PM.ConfigChanges.ScreenSize
        | Android.Content.PM.ConfigChanges.KeyboardHidden)]
public class MainActivity : JaliumActivity
{
    protected override JaliumApp CreateHostedApp()
    {
        var builder = AppBuilder.CreateBuilder();

        builder.ConfigureApplication(app =>
        {
            app.MainWindow = BuildMainWindow();
        });

        return builder.Build();
    }

    private static Window BuildMainWindow()
    {
        var window = new Window
        {
            Title = "Jalium Android Demo",
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 46))
        };

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24)
        };

        root.Children.Add(new TextBlock
        {
            Text = "Jalium.UI on Android",
            FontSize = 36,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });

        root.Children.Add(new TextBlock
        {
            Text = "Cross-platform .NET UI Framework",
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 180)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 32)
        });

        int counter = 0;
        var counterText = new TextBlock
        {
            Text = "Taps: 0",
            FontSize = 24,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 220)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24)
        };
        root.Children.Add(counterText);

        var button = new Button
        {
            Background = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            MinWidth = 200,
            MinHeight = 56,
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(24, 12, 24, 12),
            Content = new TextBlock
            {
                Text = "Tap Me!",
                FontSize = 20,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        button.Click += (_, _) =>
        {
            counter++;
            counterText.Text = $"Taps: {counter}";
            window.RequestFullInvalidation();
            window.InvalidateWindow();
        };
        root.Children.Add(button);

        window.Content = root;
        return window;
    }
}
