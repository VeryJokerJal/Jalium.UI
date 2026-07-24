using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using StatusBar = Jalium.UI.Controls.Primitives.StatusBar;
using StatusBarItem = Jalium.UI.Controls.Primitives.StatusBarItem;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class StatusBarThemeTests
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
    public void StatusBar_ImplicitThemeStyle_ShouldProvideDefaultsWithoutLocalOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var statusBar = new StatusBar();
            var item = new StatusBarItem { Content = "Ready" };
            statusBar.Items.Add(item);

            var host = new StackPanel { Width = 400, Height = 120 };
            host.Children.Add(statusBar);
            host.Measure(new Size(400, 120));
            host.Arrange(new Rect(0, 0, 400, 120));

            Assert.True(app.Resources.TryGetValue(typeof(StatusBar), out var statusBarStyleObj));
            Assert.IsType<Style>(statusBarStyleObj);

            Assert.False(statusBar.HasLocalValue(Control.BackgroundProperty));
            Assert.False(statusBar.HasLocalValue(Control.BorderBrushProperty));
            Assert.False(statusBar.HasLocalValue(FrameworkElement.HeightProperty));
            Assert.NotNull(statusBar.Background);
            Assert.NotNull(statusBar.BorderBrush);
            Assert.Equal(26, statusBar.Height);

            Assert.False(item.HasLocalValue(Control.PaddingProperty));
            Assert.False(item.HasLocalValue(Control.ForegroundProperty));
            Assert.Equal(10, item.Padding.Left);
            Assert.Equal(3, item.Padding.Top);
            Assert.NotNull(item.Foreground);
            Assert.Equal(0, item.VisualChildrenCount);
            Assert.True(item.DesiredSize.Height >= 24);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void StatusBarItem_ElementContent_ShouldRemainInVisualTree()
    {
        var item = new StatusBarItem
        {
            Padding = new Thickness(4),
            Content = new Border
            {
                Width = 12,
                Height = 12
            }
        };

        item.Measure(new Size(100, 40));
        item.Arrange(new Rect(0, 0, 100, 40));

        Assert.Equal(1, item.VisualChildrenCount);
        Assert.NotNull(item.GetVisualChild(0));
        Assert.True(item.DesiredSize.Height >= 24);
    }

    [Fact]
    public void StatusBarItem_InternalForegroundResolver_ShouldUseThemeResource()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var item = new StatusBarItem { Content = "Ready" };
            var resolveMethod = typeof(StatusBarItem).GetMethod("ResolveForegroundBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(resolveMethod);

            var brush = resolveMethod!.Invoke(item, null);
            Assert.Same(app.Resources["TextSecondary"], brush);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
