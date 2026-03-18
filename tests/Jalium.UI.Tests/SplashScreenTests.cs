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

    [Fact]
    public void SplashScreen_ShowAndClose_ShouldTrackVisibility()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var splash = new SplashScreen(typeof(SplashScreenTests).Assembly, "Colors.jalxaml");

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

    [Fact]
    public void SplashScreen_AutoClose_ShouldWaitForMainWindowToLoad()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var splash = new SplashScreen(typeof(SplashScreenTests).Assembly, "Colors.jalxaml");

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

    [Fact]
    public void SplashScreen_AutoClose_ShouldArmWhenApplicationIsCreatedAfterShow()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();

        try
        {
            var splash = new SplashScreen(typeof(SplashScreenTests).Assembly, "Colors.jalxaml");

            splash.Show(autoClose: true, topMost: false);

            Assert.True(splash.IsVisible);

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
