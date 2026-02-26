using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class AppBarButtonTests
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
    public void AppBarButton_ShouldApplyItsOwnTemplate_AndBindIcon()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var button = new AppBarButton
            {
                Icon = new SymbolIcon(Symbol.Save),
                Label = "Save",
                IsCompact = true
            };

            var host = new StackPanel { Width = 300, Height = 80, Orientation = Orientation.Horizontal };
            host.Children.Add(button);
            host.Measure(new Size(300, 80));
            host.Arrange(new Rect(0, 0, 300, 80));

            Assert.True(button.VisualChildrenCount > 0);
            Assert.NotNull(button.Template);

            var iconPresenter = button.FindName("IconPresenter") as ContentPresenter;
            Assert.NotNull(iconPresenter);
            Assert.NotNull(iconPresenter!.Content);
            Assert.IsType<SymbolIcon>(iconPresenter.Content);

            var style = button.TryFindResource(typeof(AppBarButton)) as Style;
            Assert.NotNull(style);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void AppBarButton_XamlStringIcon_ShouldConvertToSymbolIcon()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            const string xaml = """
                <AppBarButton xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                              Label="Save"
                              Icon="Save" />
                """;

            var button = Assert.IsType<AppBarButton>(XamlReader.Parse(xaml));
            var symbolIcon = Assert.IsType<SymbolIcon>(button.Icon);
            Assert.Equal(Symbol.Save, symbolIcon.Symbol);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void AppBarButton_ImplicitOverrideWithoutTemplate_ShouldKeepDefaultTemplate()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            // Simulate IDE-level implicit style override that only customizes colors.
            var overrideStyle = new Style(typeof(AppBarButton));
            overrideStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            overrideStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xDD))));
            app.Resources[typeof(AppBarButton)] = overrideStyle;

            var button = new AppBarButton
            {
                Icon = new SymbolIcon(Symbol.Save),
                Label = "Save"
            };

            var host = new StackPanel { Width = 300, Height = 80, Orientation = Orientation.Horizontal };
            host.Children.Add(button);
            host.Measure(new Size(300, 80));
            host.Arrange(new Rect(0, 0, 300, 80));

            Assert.NotNull(button.Template);
            Assert.True(button.VisualChildrenCount > 0);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
