using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class SplashScreenTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);
        typeof(ThemeManager).GetField("_controlThemeDictionaries", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, Array.Empty<ResourceDictionary>());

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void SplashScreen_ShowWithMissingResource_ShouldRemainHidden()
    {
        var splash = new SplashScreen(typeof(SplashScreenTests).Assembly, "missing-resource.bin");

        splash.Show(autoClose: false, topMost: false);

        Assert.False(splash.IsVisible);
        splash.Close(TimeSpan.Zero);
        Assert.False(splash.IsVisible);
    }

    [SplashWindowHostFact]
    public void SplashScreen_ShowAndClose_ShouldTrackVisibility()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var splash = new SplashScreen(typeof(SplashScreenTests).Assembly, "SplashScreen.bmp");

            splash.Show(autoClose: false, topMost: true);

            Assert.True(splash.IsVisible);

            splash.Close(TimeSpan.Zero);

            Assert.False(splash.IsVisible);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [SplashWindowHostFact]
    public void SplashScreen_AutoClose_ShouldWaitForMainWindowToLoad()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var splash = new SplashScreen(typeof(SplashScreenTests).Assembly, "SplashScreen.bmp");

            splash.Show(autoClose: true, topMost: false);

            Assert.True(splash.IsVisible);

            var mainWindow = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 160,
                Height = 120
            };

            app.MainWindow = mainWindow;

            Assert.True(splash.IsVisible);

            mainWindow.Show();

            Assert.False(splash.IsVisible);

            mainWindow.Close();
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [SplashWindowHostFact]
    public void SplashScreen_AutoClose_ShouldArmWhenApplicationIsCreatedAfterShow()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();

        try
        {
            var splash = new SplashScreen(typeof(SplashScreenTests).Assembly, "SplashScreen.bmp");

            splash.Show(autoClose: true, topMost: false);

            Assert.True(splash.IsVisible);

            Assert.Null(Application.Current);
            var splashWindow = Assert.IsType<Window>(typeof(SplashScreen).GetField("_window",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(splash));
            var presenter = Assert.IsType<SplashScreenPresenter>(splashWindow.Content);
            presenter.ApplyTemplate();
            Assert.NotNull(Assert.IsType<Image>(presenter.FindName("PART_Image")).Source);
            splash.Show(autoClose: true, topMost: false);
            Assert.Same(splashWindow, typeof(SplashScreen).GetField("_window",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(splash));

            var app = new Application();
            var mainWindow = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 160,
                Height = 120
            };

            app.MainWindow = mainWindow;

            Assert.True(splash.IsVisible);

            mainWindow.Show();

            Assert.False(splash.IsVisible);

            mainWindow.Close();
        }
        finally
        {
            ResetApplicationState();
        }
    }
}

// These shared tests use the desktop Window.Show path. The macOS managed suite
// has no initialized AppKit host; its native SplashScreenQA exercises the same flows.
internal sealed class SplashWindowHostFactAttribute : FactAttribute
{
    public SplashWindowHostFactAttribute()
    {
        if (OperatingSystem.IsMacOS())
            Skip = "Requires an initialized AppKit Window host; exercised by native SplashScreenQA.";
    }
}
