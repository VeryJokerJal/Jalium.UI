using Jalium.UI.Controls;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// CSS layout state: percentage lengths (width/height/min/max/margin/padding),
/// aspect-ratio, box-sizing, and the sentinel precedence coordination.
/// </summary>
public sealed class CssLayoutTests
{
    static CssLayoutTests()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);
    }

    private static void MeasureArrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void WidthPercent_ResolvesAgainstAvailableSize()
    {
        var border = new Border();
        Css.SetStyle(border, "width: 50%; height: 25%");
        MeasureArrange(border, 400, 200);

        Assert.Equal(200.0, border.ActualWidth);
        Assert.Equal(50.0, border.ActualHeight);
    }

    [Fact]
    public void WidthPercent_InfiniteBasisDegradesToAuto()
    {
        var inner = new Border { Width = 30, Height = 10 };
        var border = new Border { Child = inner };
        Css.SetStyle(border, "width: 50%");
        border.Measure(new Size(double.PositiveInfinity, 100));

        // ∞ basis → auto → content size.
        Assert.Equal(30.0, border.DesiredSize.Width);
    }

    [Fact]
    public void LocalWidth_OutranksPercent_AndFallsBackOnClear()
    {
        var border = new Border();
        Css.SetStyle(border, "width: 50%");
        border.Width = 120;
        MeasureArrange(border, 400, 200);
        Assert.Equal(120.0, border.ActualWidth);

        border.ClearValue(FrameworkElement.WidthProperty);
        MeasureArrange(border, 400, 200);
        Assert.Equal(200.0, border.ActualWidth);
    }

    [Fact]
    public void PercentCleared_WhenRuleRemoved()
    {
        var border = new Border();
        Css.SetStyle(border, "width: 50%");
        MeasureArrange(border, 400, 200);
        Assert.Equal(200.0, border.ActualWidth);
        Assert.NotNull(border.CssLayout);

        Css.SetStyle(border, string.Empty);
        Assert.Null(border.CssLayout);
        MeasureArrange(border, 400, 200);
        Assert.Equal(400.0, border.ActualWidth); // Stretch default fills the slot again.
    }

    [Fact]
    public void MinMaxPercent_ClampAgainstBasis()
    {
        var border = new Border();
        Css.SetStyle(border, "min-width: 50%; min-height: 10%");
        MeasureArrange(border, 300, 200);
        Assert.Equal(300.0, border.ActualWidth); // Stretch keeps the full slot; min holds it.
        Assert.True(border.DesiredSize.Width >= 150);

        var capped = new Border();
        Css.SetStyle(capped, "max-width: 25%");
        MeasureArrange(capped, 400, 100);
        Assert.Equal(100.0, capped.ActualWidth);
    }

    [Fact]
    public void MarginPercent_ResolvesAgainstContainingBlockWidth()
    {
        var border = new Border();
        Css.SetStyle(border, "margin: 10%; width: 100px; height: 20px");
        border.Measure(new Size(400, 400));

        // All four edges resolve against the WIDTH (CSS rule): 40 each.
        Assert.Equal(180.0, border.DesiredSize.Width);  // 100 + 40 + 40
        Assert.Equal(100.0, border.DesiredSize.Height); // 20 + 40 + 40

        border.Arrange(new Rect(0, 0, 400, 400));
        Assert.Equal(100.0, border.ActualWidth);
    }

    [Fact]
    public void MarginPercent_LocalMarginWins()
    {
        var border = new Border { Margin = new Thickness(5) };
        Css.SetStyle(border, "margin: 10%; width: 100px; height: 20px");
        border.Measure(new Size(400, 400));
        Assert.Equal(110.0, border.DesiredSize.Width); // local 5+5 wins over 10%.
    }

    [Fact]
    public void PaddingPercent_MaterializesIntoPaddingDp()
    {
        var border = new Border();
        Css.SetStyle(border, "padding: 10% 5%");
        border.Measure(new Size(200, 100));

        // vertical 10% and horizontal 5% of width(200) → (10, 20, 10, 20).
        Assert.Equal(new Thickness(10, 20, 10, 20), border.Padding);
    }

    [Theory]
    [InlineData("aspect-ratio: 2; width: 100px", 100.0, 50.0)]
    [InlineData("aspect-ratio: 4 / 3; height: 90px", 120.0, 90.0)]
    [InlineData("aspect-ratio: 2; width: 50%", 150.0, 75.0)]
    public void AspectRatio_DerivesAutoAxis(string style, double expectedWidth, double expectedHeight)
    {
        var border = new Border();
        Css.SetStyle(border, style);
        MeasureArrange(border, 300, 300);
        Assert.Equal(expectedWidth, border.ActualWidth, 6);
        Assert.Equal(expectedHeight, border.ActualHeight, 6);
    }

    [Fact]
    public void AspectRatio_MinMaxClampsAfterDerivation()
    {
        var border = new Border();
        Css.SetStyle(border, "aspect-ratio: 2; width: 100px; min-height: 80px");
        MeasureArrange(border, 300, 300);
        Assert.Equal(80.0, border.ActualHeight);
    }

    [Fact]
    public void BoxSizing_ContentBox_AddsChrome()
    {
        var border = new Border
        {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(2),
        };
        Css.SetStyle(border, "box-sizing: content-box; width: 100px; height: 40px");
        MeasureArrange(border, 400, 300);

        // content 100 + padding 20 + border 4 = 124 outer.
        Assert.Equal(124.0, border.ActualWidth);
        Assert.Equal(64.0, border.ActualHeight);
    }

    [Fact]
    public void BoxSizing_BorderBox_IsNativeSemantics()
    {
        var border = new Border { Padding = new Thickness(10) };
        Css.SetStyle(border, "box-sizing: border-box; width: 100px");
        MeasureArrange(border, 400, 300);
        Assert.Equal(100.0, border.ActualWidth);
    }

    [Fact]
    public void PercentInsideStackPanel_CrossAxisWorks()
    {
        var child = new Border { Height = 20 };
        Css.SetStyle(child, "width: 50%");
        var panel = new StackPanel();
        panel.Children.Add(child);
        MeasureArrange(panel, 400, 300);

        Assert.Equal(200.0, child.ActualWidth);
    }

    [Fact]
    public void PureDeclarations_LeaveLayoutStateNull()
    {
        var border = new Border();
        Css.SetStyle(border, "width: 120px; margin: 4px; opacity: .5");
        Assert.Null(border.CssLayout);
    }
}
