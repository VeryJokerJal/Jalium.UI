using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class NavigationThemeTests
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
    public void NavigationViewItemHeader_ImplicitThemeStyle_ShouldApplyWithoutLocalOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var header = new NavigationViewItemHeader { Content = "Header" };
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(header);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(NavigationViewItemHeader), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(header.HasLocalValue(FrameworkElement.HeightProperty));
            Assert.False(header.HasLocalValue(FrameworkElement.MarginProperty));
            Assert.False(header.HasLocalValue(Control.ForegroundProperty));
            Assert.Equal(40, header.Height);
            Assert.Equal(14, header.Margin.Left);
            Assert.NotNull(header.Foreground);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void NavigationViewItemSeparator_ImplicitThemeStyle_ShouldApplyWithoutLocalOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var separator = new NavigationViewItemSeparator();
            var host = new StackPanel { Width = 320, Height = 40 };
            host.Children.Add(separator);

            host.Measure(new Size(320, 40));
            host.Arrange(new Rect(0, 0, 320, 40));

            Assert.True(app.Resources.TryGetValue(typeof(NavigationViewItemSeparator), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(separator.HasLocalValue(FrameworkElement.HeightProperty));
            Assert.False(separator.HasLocalValue(FrameworkElement.MarginProperty));
            Assert.False(separator.HasLocalValue(Control.BackgroundProperty));
            Assert.Equal(1, separator.Height);
            Assert.Equal(18, separator.Margin.Left);
            Assert.NotNull(separator.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void NavigationViewItemResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var header = new NavigationViewItemHeader { Content = "Header" };
            var separator = new NavigationViewItemSeparator();

            var foregroundMethod = typeof(NavigationViewItemHeader).GetMethod("ResolveForegroundBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var backgroundMethod = typeof(NavigationViewItemSeparator).GetMethod("ResolveBackgroundBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(foregroundMethod);
            Assert.NotNull(backgroundMethod);

            Assert.Same(app.Resources["TextSecondary"], foregroundMethod!.Invoke(header, null));
            Assert.Same(app.Resources["ControlBorder"], backgroundMethod!.Invoke(separator, null));
        }
        finally
        {
            ResetApplicationState();
        }
    }

}
