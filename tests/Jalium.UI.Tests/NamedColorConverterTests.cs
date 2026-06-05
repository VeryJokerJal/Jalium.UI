using System;
using System.Reflection;
using Jalium.UI.Media;
using Xunit;

// Both Jalium.UI.Markup and Jalium.UI.Media define a ColorConverter / BrushConverter. These tests
// target the Markup (jalxaml value-pipeline) converters, so alias them explicitly to avoid the
// CS0104 ambiguity that importing both namespaces would cause.
using ColorConverter = Jalium.UI.Markup.ColorConverter;
using BrushConverter = Jalium.UI.Markup.BrushConverter;
using TypeConverterRegistry = Jalium.UI.Markup.TypeConverterRegistry;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers named-color parsing in the jalxaml value pipeline's <see cref="ColorConverter"/> and
/// <see cref="BrushConverter"/> (Jalium.UI.Markup). These converters keep a small hand-tuned set of
/// common color names as a fast path and then fall back to the full standard set mirrored from
/// <see cref="Colors"/>, so a bare name such as <c>Foreground="Crimson"</c> resolves instead of
/// throwing. The fast-path and # hex behaviours must stay byte-for-byte unchanged.
/// </summary>
public class NamedColorConverterTests
{
    // Names that exist on Colors but are NOT in the converters' 24-entry fast path; before the
    // fallback these threw FormatException.
    [Theory]
    [InlineData("Crimson", 220, 20, 60)]
    [InlineData("Gold", 255, 215, 0)]
    [InlineData("ForestGreen", 34, 139, 34)]
    [InlineData("SkyBlue", 135, 206, 235)]
    public void ColorConverter_ResolvesExtendedNamedColor(string name, int r, int g, int b)
    {
        var result = new ColorConverter().ConvertFrom(name);

        var color = Assert.IsType<Color>(result);
        Assert.Equal(Color.FromRgb((byte)r, (byte)g, (byte)b), color);
    }

    [Theory]
    [InlineData("Crimson", 220, 20, 60)]
    [InlineData("Gold", 255, 215, 0)]
    [InlineData("ForestGreen", 34, 139, 34)]
    [InlineData("SkyBlue", 135, 206, 235)]
    public void BrushConverter_ResolvesExtendedNamedColor(string name, int r, int g, int b)
    {
        var result = new BrushConverter().ConvertFrom(name);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.FromRgb((byte)r, (byte)g, (byte)b), brush.Color);
    }

    [Theory]
    [InlineData("crimson")]
    [InlineData("CRIMSON")]
    [InlineData("  Crimson  ")]
    public void ColorConverter_ExtendedNamedColor_IsCaseAndWhitespaceInsensitive(string name)
    {
        var color = Assert.IsType<Color>(new ColorConverter().ConvertFrom(name));
        Assert.Equal(Colors.Crimson, color);
    }

    // Regression: the hand-tuned fast-path colors must behave exactly as before.
    [Theory]
    [InlineData("Red", 255, 0, 0)]
    [InlineData("Blue", 0, 0, 255)]
    [InlineData("Green", 0, 128, 0)]
    [InlineData("LightGray", 211, 211, 211)]
    public void ColorConverter_FastPathColors_Unchanged(string name, int r, int g, int b)
    {
        var color = Assert.IsType<Color>(new ColorConverter().ConvertFrom(name));
        Assert.Equal(Color.FromRgb((byte)r, (byte)g, (byte)b), color);
    }

    [Fact]
    public void ColorConverter_Transparent_Unchanged()
    {
        var color = Assert.IsType<Color>(new ColorConverter().ConvertFrom("Transparent"));
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), color);
    }

    // Regression: # hex parsing must behave exactly as before.
    [Theory]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("#00FF00", 0, 255, 0)]
    public void ColorConverter_HexPath_Unchanged(string hex, int r, int g, int b)
    {
        var color = Assert.IsType<Color>(new ColorConverter().ConvertFrom(hex));
        Assert.Equal(Color.FromRgb((byte)r, (byte)g, (byte)b), color);
    }

    [Fact]
    public void ColorConverter_HexWithAlpha_Unchanged()
    {
        var color = Assert.IsType<Color>(new ColorConverter().ConvertFrom("#8012FF34"));
        Assert.Equal(Color.FromArgb((byte)0x80, (byte)0x12, (byte)0xFF, (byte)0x34), color);
    }

    [Theory]
    [InlineData("NotAColor")]
    [InlineData("Bogus")]
    [InlineData("CrimsonX")]
    public void ColorConverter_UnknownName_StillThrows(string name)
    {
        Assert.Throws<FormatException>(() => new ColorConverter().ConvertFrom(name));
    }

    [Theory]
    [InlineData("NotAColor")]
    [InlineData("Bogus")]
    public void BrushConverter_UnknownName_StillThrows(string name)
    {
        Assert.Throws<FormatException>(() => new BrushConverter().ConvertFrom(name));
    }

    // End-to-end: the real jalxaml value path (XamlReader -> TypeConverterRegistry.ConvertValue)
    // used for Foreground="Crimson" / Background="Crimson".
    [Fact]
    public void TypeConverterRegistry_ResolvesExtendedNamedColor_ForColorAndBrush()
    {
        var color = Assert.IsType<Color>(TypeConverterRegistry.ConvertValue("ForestGreen", typeof(Color)));
        Assert.Equal(Colors.ForestGreen, color);

        var brush = Assert.IsType<SolidColorBrush>(TypeConverterRegistry.ConvertValue("ForestGreen", typeof(Brush)));
        Assert.Equal(Colors.ForestGreen, brush.Color);
    }

    // Completeness guard: every name defined on Colors must resolve through both converters to the
    // exact same Color value (via the fast path or the full-set fallback). Fails loudly if Colors
    // ever gains an entry that neither the fast path nor the fallback table covers.
    [Fact]
    public void EveryColorsName_ResolvesThroughBothConverters_ToMatchingValue()
    {
        var colorConverter = new ColorConverter();
        var brushConverter = new BrushConverter();
        var checkedCount = 0;

        foreach (var prop in typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (prop.PropertyType != typeof(Color))
                continue;

            var expected = (Color)prop.GetValue(null)!;

            var color = Assert.IsType<Color>(colorConverter.ConvertFrom(prop.Name));
            Assert.Equal(expected, color);

            var brush = Assert.IsType<SolidColorBrush>(brushConverter.ConvertFrom(prop.Name));
            Assert.Equal(expected, brush.Color);

            checkedCount++;
        }

        Assert.True(checkedCount >= 140,
            $"Expected the full standard named-color set to resolve; only {checkedCount} names were checked.");
    }
}
