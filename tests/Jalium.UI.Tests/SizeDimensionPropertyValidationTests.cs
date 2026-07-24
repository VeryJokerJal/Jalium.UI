using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

/// <summary>
/// WPF-style ValidateValueCallback coverage for the size-dimension dependency properties.
/// Width/Height accept NaN (auto) or a non-negative finite number; MinWidth/MinHeight must
/// be non-negative finite (NaN is not a valid minimum); MaxWidth/MaxHeight must be
/// non-negative and not NaN (PositiveInfinity is the "unconstrained" default).
/// WrapPanel.ItemWidth/ItemHeight share the Width contract; Separator.StrokeThickness must
/// be non-negative finite. Rejecting the value at the property boundary keeps it from
/// surfacing much later as a Size-constructor throw mid measure/arrange.
/// </summary>
public class SizeDimensionPropertyValidationTests
{
    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void WidthHeight_RejectNegativeAndInfinite(double value)
    {
        var element = new Border();
        Assert.Throws<ArgumentException>(() => element.Width = value);
        Assert.Throws<ArgumentException>(() => element.Height = value);
    }

    [Fact]
    public void WidthHeight_AcceptNaNZeroAndPositive()
    {
        var element = new Border();
        element.Width = double.NaN; // auto — also the registered default
        element.Height = double.NaN;
        element.Width = 0;
        element.Height = 42.5;
        Assert.Equal(0, element.Width);
        Assert.Equal(42.5, element.Height);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.5)]
    [InlineData(double.PositiveInfinity)]
    public void MinWidthHeight_RejectNaNNegativeAndInfinite(double value)
    {
        var element = new Border();
        Assert.Throws<ArgumentException>(() => element.MinWidth = value);
        Assert.Throws<ArgumentException>(() => element.MinHeight = value);
    }

    [Fact]
    public void MinWidthHeight_AcceptNonNegativeFinite()
    {
        var element = new Border { MinWidth = 0, MinHeight = 12 };
        Assert.Equal(12, element.MinHeight);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    public void MaxWidthHeight_RejectNaNAndNegative(double value)
    {
        var element = new Border();
        Assert.Throws<ArgumentException>(() => element.MaxWidth = value);
        Assert.Throws<ArgumentException>(() => element.MaxHeight = value);
    }

    [Fact]
    public void MaxWidthHeight_AcceptPositiveInfinity()
    {
        // PositiveInfinity is the registered default ("unconstrained") and must stay legal.
        var element = new Border
        {
            MaxWidth = double.PositiveInfinity,
            MaxHeight = double.PositiveInfinity,
        };
        Assert.Equal(double.PositiveInfinity, element.MaxWidth);
    }

    [Theory]
    [InlineData(-3.0)]
    [InlineData(double.PositiveInfinity)]
    public void WrapPanelItemSize_RejectsNegativeAndInfinite(double value)
    {
        var panel = new WrapPanel();
        Assert.Throws<ArgumentException>(() => panel.ItemWidth = value);
        Assert.Throws<ArgumentException>(() => panel.ItemHeight = value);
    }

    [Fact]
    public void WrapPanelItemSize_AcceptsNaNMeaningNaturalSize()
    {
        var panel = new WrapPanel { ItemWidth = double.NaN, ItemHeight = double.NaN };
        Assert.True(double.IsNaN(panel.ItemWidth));
        Assert.True(double.IsNaN(panel.ItemHeight));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SeparatorStrokeThickness_RejectsNegativeNaNAndInfinite(double value)
    {
        var separator = new Separator();
        Assert.Throws<ArgumentException>(() => separator.StrokeThickness = value);
    }

    [Fact]
    public void SeparatorStrokeThickness_AcceptsNonNegative_AndMeasureStaysValid()
    {
        // The thickness flows straight into the desired Size — a valid value must
        // still measure without throwing.
        var separator = new Separator { StrokeThickness = 2.5 };
        separator.Measure(new Size(100, 100));
        Assert.True(separator.DesiredSize.Width >= 0);
        Assert.True(separator.DesiredSize.Height >= 0);
    }
}
