using System.Reflection;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class SystemFontsWpfParityTests
{
    private const string FontKinds = "Icon Caption SmallCaption Menu Status Message";
    private const string FontFacets = "Size Family Style Weight TextDecorations";

    [Fact]
    public void PublicSurface_HasAllWpfFontValuesAndResourceKeys()
    {
        Assert.Equal("Jalium.UI.SystemFonts", typeof(SystemFonts).FullName);
        Assert.Equal("Jalium.UI", typeof(FontStyle).Namespace);
        Assert.Equal("Jalium.UI", typeof(FontWeight).Namespace);
        Assert.Equal("Jalium.UI", typeof(FontStretch).Namespace);
        Assert.Equal("Jalium.UI", typeof(TextDecorationCollection).Namespace);

        foreach (var kind in Names(FontKinds))
        {
            AssertProperty<double>($"{kind}FontSize");
            AssertProperty<FontFamily>($"{kind}FontFamily");
            AssertProperty<FontStyle>($"{kind}FontStyle");
            AssertProperty<FontWeight>($"{kind}FontWeight");
            AssertProperty<TextDecorationCollection>($"{kind}FontTextDecorations");

            foreach (var facet in Names(FontFacets))
            {
                AssertProperty<ResourceKey>($"{kind}Font{facet}Key");
            }
        }

        var publicProperties = typeof(SystemFonts)
            .GetProperties(BindingFlags.Public | BindingFlags.Static);
        Assert.Equal(60, publicProperties.Length);
    }

    [Fact]
    public void EveryResourceKey_IsStableAndResolvesToItsFontValue()
    {
        foreach (var kind in Names(FontKinds))
        {
            foreach (var facet in Names(FontFacets))
            {
                var valueName = $"{kind}Font{facet}";
                var keyProperty = typeof(SystemFonts).GetProperty(
                    valueName + "Key",
                    BindingFlags.Public | BindingFlags.Static)!;
                var valueProperty = typeof(SystemFonts).GetProperty(
                    valueName,
                    BindingFlags.Public | BindingFlags.Static)!;

                var first = Assert.IsAssignableFrom<ResourceKey>(keyProperty.GetValue(null));
                var second = Assert.IsAssignableFrom<ResourceKey>(keyProperty.GetValue(null));
                Assert.Same(first, second);

                var componentKey = Assert.IsType<ComponentResourceKey>(first);
                Assert.Equal(typeof(SystemFonts), componentKey.TypeInTargetAssembly);
                Assert.Equal(valueName, componentKey.ResourceId);

                Assert.True(SystemFonts.TryGetResource(first, out var actual));
                var expected = valueProperty.GetValue(null);
                if (expected is FontFamily or TextDecorationCollection)
                {
                    Assert.Same(expected, actual);
                }
                else
                {
                    Assert.Equal(expected, actual);
                }
            }
        }

        var unknown = new ComponentResourceKey(typeof(SystemFonts), "UnknownSystemFont");
        Assert.False(SystemFonts.TryGetResource(unknown, out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void Values_AreUsableAndReferenceValuesRemainStable()
    {
        foreach (var kind in Names(FontKinds))
        {
            var familyProperty = typeof(SystemFonts).GetProperty($"{kind}FontFamily")!;
            var sizeProperty = typeof(SystemFonts).GetProperty($"{kind}FontSize")!;
            var styleProperty = typeof(SystemFonts).GetProperty($"{kind}FontStyle")!;
            var weightProperty = typeof(SystemFonts).GetProperty($"{kind}FontWeight")!;
            var decorationsProperty = typeof(SystemFonts).GetProperty($"{kind}FontTextDecorations")!;

            var family = Assert.IsType<FontFamily>(familyProperty.GetValue(null));
            Assert.False(string.IsNullOrWhiteSpace(family.Source));
            Assert.Same(family, familyProperty.GetValue(null));

            var size = Assert.IsType<double>(sizeProperty.GetValue(null));
            Assert.InRange(size, 1.0, 200.0);

            var style = Assert.IsType<FontStyle>(styleProperty.GetValue(null));
            Assert.True(style == FontStyles.Normal || style == FontStyles.Italic);

            var weight = Assert.IsType<FontWeight>(weightProperty.GetValue(null));
            Assert.InRange(weight.ToOpenTypeWeight(), 1, 999);

            var decorations = Assert.IsType<TextDecorationCollection>(decorationsProperty.GetValue(null));
            Assert.Same(decorations, decorationsProperty.GetValue(null));
            Assert.All(
                decorations,
                static decoration => Assert.True(
                    decoration.Location is TextDecorationLocation.Underline
                        or TextDecorationLocation.Strikethrough));
        }
    }

    [Fact]
    public void NonWindowsFallback_PreservesTheFrameworkDefaults()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(FrameworkElement.DefaultFontFamilyName, SystemFonts.MessageFontFamily.Source);
        Assert.Equal(FrameworkElement.DefaultFontFamilyName, SystemFonts.CaptionFontFamily.Source);
        Assert.Equal(FrameworkElement.DefaultFontFamilyName, SystemFonts.SmallCaptionFontFamily.Source);
        Assert.Equal(FrameworkElement.DefaultFontFamilyName, SystemFonts.MenuFontFamily.Source);
        Assert.Equal(FrameworkElement.DefaultFontFamilyName, SystemFonts.StatusFontFamily.Source);
        Assert.Equal(FrameworkElement.DefaultFontFamilyName, SystemFonts.IconFontFamily.Source);

        Assert.Equal(14.0, SystemFonts.MessageFontSize);
        Assert.Equal(14.0, SystemFonts.CaptionFontSize);
        Assert.Equal(11.0, SystemFonts.SmallCaptionFontSize);
        Assert.Equal(14.0, SystemFonts.MenuFontSize);
        Assert.Equal(14.0, SystemFonts.StatusFontSize);
        Assert.Equal(9.0, SystemFonts.IconFontSize);
    }

    private static void AssertProperty<T>(string name)
    {
        var property = typeof(SystemFonts).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(property);
        Assert.Equal(typeof(T), property!.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.True(property.GetMethod!.IsPublic);
    }

    private static string[] Names(string names) =>
        names.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
