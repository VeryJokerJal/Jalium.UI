using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
public sealed class ColorPickerColorMathTests
{
    [Theory]
    [InlineData("#abcdef", 255, 171, 205, 239)]
    [InlineData("80112233", 128, 17, 34, 51)]
    [InlineData("  #112233  ", 77, 17, 34, 51)]
    public void HexParserPreservesChannelsAndUsesTheProvidedDefaultAlpha(string text, byte alpha, byte red, byte green, byte blue)
    {
        Assert.True(ColorPickerColorMath.TryParseHex(text, alpha, out var color));
        Assert.Equal(Color.FromArgb(alpha, red, green, blue), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#bad")]
    [InlineData("#GG1122")]
    [InlineData("FF 000")]
    [InlineData("#123456789")]
    public void HexParserRejectsIncompleteAndMalformedValues(string text) => Assert.False(ColorPickerColorMath.TryParseHex(text, 255, out _));
    [Fact]
    public void HsvRoundTripPreservesByteChannelsAndAlpha()
    {
        var random = new Random(173);
        for (int index = 0; index < 512; index++)
        {
            var original = Color.FromArgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            var state = ColorPickerColorMath.ToHsv(original);
            Assert.Equal(original, ColorPickerColorMath.FromHsv(state.Hue, state.Saturation, state.Value, original.A));
        }
    }

    [Fact]
    public void AchromaticColorsRetainTheChosenHue()
    {
        var gray = Color.FromArgb(70, 128, 128, 128);
        var state = ColorPickerColorMath.ToHsv(gray, 270);
        Assert.Equal(270, state.Hue);
        Assert.Equal(0, state.Saturation);
        Assert.Equal(gray, ColorPickerColorMath.FromHsv(state.Hue, state.Saturation, state.Value, gray.A));
        Assert.Equal(270, ColorPickerColorMath.ToHsv(Color.Black, 270).Hue);
    }

    [Fact]
    public void HueEndpointsAndPrimaryColorsAgree()
    {
        Assert.Equal(Color.Red, ColorPickerColorMath.FromHsv(0, 1, 1));
        Assert.Equal(Color.Red, ColorPickerColorMath.FromHsv(360, 1, 1));
        Assert.Equal(Color.FromRgb(0, 255, 0), ColorPickerColorMath.FromHsv(120, 1, 1));
        Assert.Equal(Color.FromRgb(0, 0, 255), ColorPickerColorMath.FromHsv(240, 1, 1));
    }

    [Fact]
    public void OutOfRangeAndNonfiniteInputsProduceBoundedColors()
    {
        Assert.Equal(Color.Black, ColorPickerColorMath.FromHsv(double.NaN, 1, double.PositiveInfinity));
        Assert.Equal(Color.Red, ColorPickerColorMath.FromHsv(-100, 2, 2));
        Assert.Equal(Color.White, ColorPickerColorMath.FromHsv(1000, -1, 2));
        Assert.Equal(0, ColorPickerColorMath.Unit(double.NegativeInfinity));
    }
}
