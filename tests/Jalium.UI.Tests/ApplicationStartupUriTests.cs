using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Tests.Startup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ApplicationStartupUriTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);

        Application.StartupObjectLoader = null;
    }

    [Fact]
    public void ResolveStartupWindow_WhenLoaderReturnsWindow_ShouldUseMainWindow()
    {
        ResetApplicationState();

        try
        {
            var expectedWindow = new Window();
            var app = new TestStartupApplication
            {
                StartupUri = "MainWindow.xaml"
            };

            Application.StartupObjectLoader = (_, _) => expectedWindow;
            var resolved = app.ResolveStartupWindow();

            Assert.NotNull(resolved);
            Assert.Same(app.MainWindow, resolved);
            Assert.Same(expectedWindow, resolved);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ResolveStartupWindow_WhenLoaderReturnsFrameworkElement_ShouldWrapInWindow()
    {
        ResetApplicationState();

        try
        {
            var app = new TestStartupApplication
            {
                StartupUri = "TestAssets/StartupStackRoot.xaml"
            };

            Application.StartupObjectLoader = static (_, _) => new StackPanel();
            var resolved = app.ResolveStartupWindow();

            Assert.NotNull(resolved);
            Assert.IsType<StackPanel>(resolved.Content);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ResolveStartupWindow_WhenLoaderReturnsUnsupportedObject_ShouldThrow()
    {
        ResetApplicationState();

        try
        {
            var app = new TestStartupApplication
            {
                StartupUri = "MainWindow.xaml"
            };

            Application.StartupObjectLoader = static (_, _) => new object();

            var ex = Assert.Throws<InvalidOperationException>(() => app.ResolveStartupWindow());
            Assert.Contains("StartupUri", ex.Message);
            Assert.Contains("MainWindow.xaml", ex.Message);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ResolveStartupWindow_WithPackUriAndXamlFallback_ShouldInstantiateXClass()
    {
        ResetApplicationState();

        try
        {
            ThemeLoader.Initialize();

            var app = new TestStartupApplication
            {
                StartupUri = "/Jalium.UI.Tests;component/TestAssets/StartupXClassWindow.xaml"
            };

            var resolved = app.ResolveStartupWindow();
            var startupWindow = Assert.IsType<TestStartupWindow>(resolved);

            Assert.Equal("StartupXClass", startupWindow.Marker);
            Assert.Equal("Startup XClass Window", startupWindow.Title);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ResolveStartupWindow_WithRelativeUriAndXamlFallback_ShouldWrapRootElement()
    {
        ResetApplicationState();

        try
        {
            ThemeLoader.Initialize();

            var app = new TestStartupApplication
            {
                StartupUri = "TestAssets/StartupStackRoot.xaml"
            };

            var resolved = app.ResolveStartupWindow();
            Assert.NotNull(resolved);
            Assert.IsType<StackPanel>(resolved.Content);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ResolveStartupWindow_WhenStartupUriResourceMissing_ShouldThrowWithStartupUriInMessage()
    {
        ResetApplicationState();

        try
        {
            ThemeLoader.Initialize();

            var app = new TestStartupApplication
            {
                StartupUri = "TestAssets/DoesNotExist.xaml"
            };

            var ex = Assert.ThrowsAny<Exception>(() => app.ResolveStartupWindow());
            Assert.Contains("StartupUri", ex.Message);
            Assert.Contains("TestAssets/DoesNotExist.xaml", ex.Message);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void XamlReader_ParseApplicationRoot_ShouldSupportStartupUriAndResources()
    {
        ResetApplicationState();

        try
        {
            const string xaml = """
                <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             StartupUri="MainWindow.xaml">
                    <Application.Resources>
                        <ResourceDictionary>
                            <SolidColorBrush x:Key="AppBrush" Color="#FF112233" />
                        </ResourceDictionary>
                    </Application.Resources>
                </Application>
                """;

            var app = Assert.IsType<Application>(XamlReader.Parse(xaml));
            Assert.Equal("MainWindow.xaml", app.StartupUri);
            var brush = Assert.IsType<SolidColorBrush>(app.Resources["AppBrush"]);
            Assert.Equal(Color.FromArgb(0xFF, 0x11, 0x22, 0x33), brush.Color);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
