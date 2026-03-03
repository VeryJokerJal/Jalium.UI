using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public class TrackTests
{
    [Fact]
    public void Vertical_ValueFromDistance_DragDown_ShouldIncrease_WhenNotReversed()
    {
        var track = new Track
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 100,
            Value = 50
        };

        track.Arrange(new Rect(0, 0, 12, 100));

        var delta = track.ValueFromDistance(0, 10);

        Assert.True(delta > 0);
    }

    [Fact]
    public void Vertical_ValueFromDistance_DragDown_ShouldDecrease_WhenReversed()
    {
        var track = new Track
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            IsDirectionReversed = true
        };

        track.Arrange(new Rect(0, 0, 12, 100));

        var delta = track.ValueFromDistance(0, 10);

        Assert.True(delta < 0);
    }

    [Fact]
    public void Vertical_Arrange_WithZeroRange_ShouldKeepThumbVisible()
    {
        var thumb = new Thumb();
        var track = new Track
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 0,
            Value = 0,
            Thumb = thumb
        };

        track.Measure(new Size(12, 120));
        track.Arrange(new Rect(0, 0, 12, 120));

        Assert.True(thumb.RenderSize.Height > 0);
    }

    [Fact]
    public void Vertical_Arrange_ShouldRespectThumbMinHeight_WhenViewportIsVerySmall()
    {
        var thumb = new Thumb
        {
            MinHeight = 20
        };
        var track = new Track
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 5000,
            Value = 0,
            ViewportSize = 1,
            Thumb = thumb
        };

        track.Measure(new Size(12, 160));
        track.Arrange(new Rect(0, 0, 12, 160));

        Assert.True(thumb.RenderSize.Height >= 20);
    }

    [Fact]
    public void Vertical_Arrange_WithInfiniteViewport_ShouldKeepThumbFinite()
    {
        var thumb = new Thumb
        {
            MinHeight = 20
        };
        var track = new Track
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 1000,
            Value = 500,
            ViewportSize = double.PositiveInfinity,
            Thumb = thumb
        };

        track.Measure(new Size(12, 160));
        track.Arrange(new Rect(0, 0, 12, 160));

        Assert.True(double.IsFinite(thumb.RenderSize.Height));
        Assert.InRange(thumb.RenderSize.Height, 20, 160);
    }

    [Fact]
    public void ScrollBar_ThumbLength_ShouldFollowTrack_WhenThumbStyleSetsFixedHeight()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 100,
            ViewportSize = 100,
            Value = 0
        };

        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var thumb = track.Thumb;
        Assert.NotNull(thumb);

        var fixedThumbStyle = new Style(typeof(Thumb));
        fixedThumbStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 20.0));
        fixedThumbStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 12.0));
        thumb!.Style = fixedThumbStyle;

        scrollBar.Measure(new Size(12, 220));
        scrollBar.Arrange(new Rect(0, 0, 12, 220));

        Assert.True(thumb.RenderSize.Height > 40);
    }

    [Fact]
    public void ScrollBar_CornerRadiusStyleSetter_ShouldApply()
    {
        var scrollBar = new ScrollBar();
        var style = new Style(typeof(ScrollBar));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        scrollBar.Resources["ScrollBarStyle"] = style;

        scrollBar.Measure(new Size(12, 120));
        scrollBar.Arrange(new Rect(0, 0, 12, 120));

        Assert.Equal(8, scrollBar.CornerRadius.TopLeft);
    }

    [Fact]
    public void ScrollBar_ThumbCornerRadiusStyleSetter_ShouldApply()
    {
        var scrollBar = new ScrollBar();
        var thumbStyle = new Style(typeof(Thumb));
        thumbStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        scrollBar.Resources["ScrollBarThumbStyle"] = thumbStyle;

        scrollBar.Measure(new Size(12, 120));
        scrollBar.Arrange(new Rect(0, 0, 12, 120));

        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var thumb = track.Thumb;
        Assert.NotNull(thumb);
        Assert.Equal(8, thumb!.CornerRadius.TopLeft);
    }

    [Fact]
    public void ScrollBar_IsThumbSlim_ShouldReduceThumbCrossAxisWidth()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 1000,
            ViewportSize = 200,
            Value = 100
        };

        scrollBar.Measure(new Size(12, 220));
        scrollBar.Arrange(new Rect(0, 0, 12, 220));

        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var thumb = Assert.IsType<Thumb>(track.Thumb);
        var expandedWidth = thumb.RenderSize.Width;

        scrollBar.IsThumbSlim = true;

        // Force end state (animation is timer-driven and non-deterministic in unit tests).
        var applyMethod = typeof(ScrollBar).GetMethod(
            "ApplyAutoHideVisualState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(applyMethod);
        applyMethod!.Invoke(scrollBar, new object?[] { 1.0, 12.0, true });
        Assert.InRange(track.ThumbCrossAxisThickness, 1.5, 2.5);

        // Arrange once more so updated thumb cross-axis thickness is reflected in layout.
        scrollBar.InvalidateArrange();
        scrollBar.Arrange(new Rect(0, 0, 12, 220));
        Assert.True(track.ViewportSize > 0, $"ViewportSize={track.ViewportSize}");
        var slimWidth = thumb.RenderSize.Width;

        Assert.True(expandedWidth > slimWidth, $"Expanded={expandedWidth}, Slim={slimWidth}");
        Assert.InRange(slimWidth, 1.5, 2.5);
    }
}
