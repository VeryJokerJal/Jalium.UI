using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.AotWindowDemo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var builder = AppBuilder.CreateBuilder(new AppBuilderSettings
        {
            Args = args,
            // Skip the Microsoft.Extensions.Hosting defaults (appsettings.json,
            // env-var configuration, default logging providers). The demo doesn't
            // need any of them and skipping keeps the AOT image lean.
            DisableDefaults = true,
        });

        builder.ConfigureApplication(app =>
        {
            app.MainWindow = BuildWindow();
        });

        using var host = builder.Build();
        return host.Run();
    }

    private static Window BuildWindow()
    {
        var window = new Window
        {
            Title = "Jalium.UI — NativeAOT single-exe demo",
            Width = 600,
            Height = 360,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(32),
        };

        stack.Children.Add(new TextBlock
        {
            Text = "Jalium.UI",
            FontSize = 36,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Native code linked statically. No DLLs alongside the exe.",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 180)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24),
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Click the button to confirm the message loop is alive.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 140)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var status = new TextBlock
        {
            Text = "(idle)",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 220, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        };
        stack.Children.Add(status);

        int clicks = 0;
        var button = new Button
        {
            Background = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            MinWidth = 220,
            MinHeight = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 8, 16, 8),
            Content = new TextBlock
            {
                Text = "Click me",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.Click += (_, _) =>
        {
            clicks++;
            status.Text = clicks == 1
                ? "1 click — UI thread is alive."
                : $"{clicks} clicks";
        };
        stack.Children.Add(button);

        window.Content = stack;
        return window;
    }
}
