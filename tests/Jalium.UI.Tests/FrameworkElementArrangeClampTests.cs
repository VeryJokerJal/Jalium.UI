using System.Reflection;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers both the public arrange guards and the final
/// <c>Math.Max(0, DesiredSize - margin)</c> clamp in
/// <see cref="FrameworkElement"/>.ArrangeCore.
/// </summary>
public class FrameworkElementArrangeClampTests
{
    private static void SetDesiredSize(UIElement element, Size value)
    {
        var field = typeof(UIElement).GetField("_desiredSize", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(element, value);
    }

    [Fact]
    public void ArrangeCore_ShouldNotThrow_WhenArrangedBeforeMeasure_WithMargin()
    {
        // Regression: a theme switch / template rebuild can arrange an element whose
        // measure pass has not run yet, so DesiredSize is still (0,0). With a
        // horizontal margin of 9+9 and a non-stretch alignment the desired-minus-margin
        // subtraction used to produce -18 and throw from the Size constructor
        // ("Width and Height cannot be negative."). ArrangeCore must clamp instead.
        var element = new Border
        {
            Margin = new Thickness(9, 0, 9, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        element.Arrange(new Rect(0, 0, 100, 50));

        Assert.Equal(0, element.RenderSize.Width);
        Assert.Equal(0, element.RenderSize.Height);
    }

    [Fact]
    public void ArrangeCore_ShouldClampStaleDesiredSize_WhenMarginGrewAfterMeasure()
    {
        // DesiredSize is captured at measure time with the old margin. Growing the
        // margin afterwards (style/theme change) makes DesiredSize - margin negative
        // until the pending re-measure runs; the arrange that happens in between must
        // clamp rather than throw.
        var element = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 10,
            Height = 10,
        };

        element.Measure(new Size(100, 50));
        element.Margin = new Thickness(12, 0, 12, 0);

        element.Arrange(new Rect(0, 0, 100, 50));

        Assert.Equal(10, element.RenderSize.Width);
        Assert.Equal(10, element.RenderSize.Height);
    }

    [Fact]
    public void ArrangeCore_ShouldNotThrow_WhenDesiredSizeSmallerThanMargin()
    {
        // Reflection isolates the final clamp from the measure-dirty guard: measure is
        // valid, but DesiredSize is smaller than the margin.
        var element = new Border
        {
            Margin = new Thickness(9, 0, 9, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        element.Measure(new Size(100, 50));
        SetDesiredSize(element, new Size(0, 0));

        element.Arrange(new Rect(0, 0, 100, 50));

        Assert.Equal(0, element.RenderSize.Width);
        Assert.Equal(0, element.RenderSize.Height);
    }

    [Fact]
    public void ArrangeCore_ShouldClampBothAxes_WhenDesiredSizeSmallerThanMargin()
    {
        var element = new Border
        {
            Margin = new Thickness(12, 7, 12, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        element.Measure(new Size(100, 50));
        SetDesiredSize(element, new Size(4, 3));

        element.Arrange(new Rect(0, 0, 100, 50));

        Assert.Equal(0, element.RenderSize.Width);
        Assert.Equal(0, element.RenderSize.Height);
    }

    [Fact]
    public void ArrangeCore_ShouldClampSingleAxis_WhenOnlyOneAxisGoesNegative()
    {
        var element = new Border
        {
            Margin = new Thickness(9, 0, 9, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        element.Measure(new Size(100, 50));
        SetDesiredSize(element, new Size(2, 20));

        element.Arrange(new Rect(0, 0, 100, 50));

        Assert.Equal(0, element.RenderSize.Width);
        Assert.Equal(20, element.RenderSize.Height);
    }
}
