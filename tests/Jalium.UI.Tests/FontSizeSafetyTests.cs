using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class FontSizeSafetyTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.001)]
    [InlineData(35791.0001)]
    public void ControlFontSizeRejectsValuesOutsideWpfRange(double value)
    {
        var control = new Button();

        Assert.Throws<ArgumentException>(() => control.FontSize = value);
        Assert.Equal(14.0, control.FontSize);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.001)]
    [InlineData(35791.0001)]
    public void FormattedTextRejectsUnsafeEmSizes(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FormattedText("text", "Arial", value));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.0011)]
    [InlineData(14.0)]
    [InlineData(35791.0)]
    public void FontSizeBoundaryValuesRemainAccepted(double value)
    {
        var control = new Button { FontSize = value };
        var formatted = new FormattedText("text", "Arial", value);

        Assert.Equal(value, control.FontSize);
        Assert.Equal(value, formatted.FontSize);
    }
}
