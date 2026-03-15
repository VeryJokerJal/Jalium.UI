using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ScrollBarThemeResourceTests
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
    public void ThemeResources_ShouldProvideScrollBarStyles()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var scrollBar = new ScrollBar
            {
                Orientation = Orientation.Vertical
            };

            var host = new Grid { Width = 40, Height = 220 };
            host.Children.Add(scrollBar);
            host.Measure(new Size(40, 220));
            host.Arrange(new Rect(0, 0, 40, 220));

            Assert.NotNull(scrollBar.TryFindResource("ScrollBarStyle") as Style);
            Assert.NotNull(scrollBar.TryFindResource("ScrollBarThumbStyle") as Style);
            Assert.NotNull(scrollBar.TryFindResource("ScrollBarLineButtonStyle") as Style);
            Assert.NotNull(scrollBar.TryFindResource("ScrollBarPageButtonStyle") as Style);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ScrollBar_ThemedParts_ShouldAvoidLocalFallbackBrushes()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var scrollBar = new ScrollBar
            {
                Orientation = Orientation.Vertical,
                Minimum = 0,
                Maximum = 100,
                ViewportSize = 20,
                Value = 10
            };

            var host = new Grid { Width = 40, Height = 220 };
            host.Children.Add(scrollBar);
            host.Measure(new Size(40, 220));
            host.Arrange(new Rect(0, 0, 40, 220));

            var lineUpButton = Assert.IsType<RepeatButton>(scrollBar.GetVisualChild(0));
            var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
            var lineDownButton = Assert.IsType<RepeatButton>(scrollBar.GetVisualChild(2));
            var thumb = Assert.IsType<Thumb>(track.Thumb);

            Assert.False(scrollBar.HasLocalValue(Control.BackgroundProperty));
            Assert.False(lineUpButton.HasLocalValue(Control.ForegroundProperty));
            Assert.False(lineDownButton.HasLocalValue(Control.ForegroundProperty));
            Assert.False(thumb.HasLocalValue(Control.BackgroundProperty));

            Assert.Same(app.Resources["ScrollBarTrack"], scrollBar.Background);
            Assert.Same(app.Resources["ScrollBarArrow"], lineUpButton.Foreground);
            Assert.Same(app.Resources["ScrollBarArrow"], lineDownButton.Foreground);
            Assert.Same(app.Resources["ScrollBarThumb"], thumb.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ScrollBar_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var scrollBar = new ScrollBar();

            Assert.Same(app.Resources["ScrollBarTrack"], InvokePrivateBrushResolver(scrollBar, "ResolveTrackBrush"));
            Assert.Same(app.Resources["ScrollBarThumb"], InvokePrivateBrushResolver(scrollBar, "ResolveThumbBrush"));
            Assert.Same(app.Resources["ScrollBarArrow"], InvokePrivateBrushResolver(scrollBar, "ResolveArrowBrush"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokePrivateBrushResolver(ScrollBar scrollBar, string methodName)
    {
        var method = typeof(ScrollBar).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(scrollBar, null));
    }
}
