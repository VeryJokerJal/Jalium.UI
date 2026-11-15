using Android.App;
using Jalium.UI.AndroidDemo.Themes;

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
        builder.ConfigureApplication(app => app.MainWindow = new Window
        {
            Title = "Jalium Keyboard Demo",
            Style = KeyboardDemoTheme.WindowStyle,
            Content = new KeyboardDemoSurface(),
        });
        return builder.Build();
    }
}
