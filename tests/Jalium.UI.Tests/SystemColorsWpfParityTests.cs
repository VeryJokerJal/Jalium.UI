using System.Reflection;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class SystemColorsWpfParityTests
{
    private static readonly (string Color, string Brush)[] ColorBrushPairs =
    [
        ("ActiveBorderColor", "ActiveBorderBrush"),
        ("ActiveCaptionColor", "ActiveCaptionBrush"),
        ("ActiveCaptionTextColor", "ActiveCaptionTextBrush"),
        ("AppWorkspaceColor", "AppWorkspaceBrush"),
        ("ControlColor", "ControlBrush"),
        ("ControlDarkColor", "ControlDarkBrush"),
        ("ControlDarkDarkColor", "ControlDarkDarkBrush"),
        ("ControlLightColor", "ControlLightBrush"),
        ("ControlLightLightColor", "ControlLightLightBrush"),
        ("ControlTextColor", "ControlTextBrush"),
        ("DesktopColor", "DesktopBrush"),
        ("GradientActiveCaptionColor", "GradientActiveCaptionBrush"),
        ("GradientInactiveCaptionColor", "GradientInactiveCaptionBrush"),
        ("GrayTextColor", "GrayTextBrush"),
        ("HighlightColor", "HighlightBrush"),
        ("HighlightTextColor", "HighlightTextBrush"),
        ("HotTrackColor", "HotTrackBrush"),
        ("InactiveBorderColor", "InactiveBorderBrush"),
        ("InactiveCaptionColor", "InactiveCaptionBrush"),
        ("InactiveCaptionTextColor", "InactiveCaptionTextBrush"),
        ("InfoColor", "InfoBrush"),
        ("InfoTextColor", "InfoTextBrush"),
        ("MenuColor", "MenuBrush"),
        ("MenuBarColor", "MenuBarBrush"),
        ("MenuHighlightColor", "MenuHighlightBrush"),
        ("MenuTextColor", "MenuTextBrush"),
        ("ScrollBarColor", "ScrollBarBrush"),
        ("WindowColor", "WindowBrush"),
        ("WindowFrameColor", "WindowFrameBrush"),
        ("WindowTextColor", "WindowTextBrush"),
        ("AccentColor", "AccentColorBrush"),
        ("AccentColorLight1", "AccentColorLight1Brush"),
        ("AccentColorLight2", "AccentColorLight2Brush"),
        ("AccentColorLight3", "AccentColorLight3Brush"),
        ("AccentColorDark1", "AccentColorDark1Brush"),
        ("AccentColorDark2", "AccentColorDark2Brush"),
        ("AccentColorDark3", "AccentColorDark3Brush"),
    ];

    private static readonly string[] StandaloneBrushes =
    [
        "InactiveSelectionHighlightBrush",
        "InactiveSelectionHighlightTextBrush",
    ];

    [Fact]
    public void PublicSurfaceMatchesWpfSystemColorsFamilies()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in ColorBrushPairs)
        {
            AssertProperty(pair.Color, typeof(Color));
            AssertProperty(pair.Color + "Key", typeof(ResourceKey));
            AssertProperty(pair.Brush, typeof(SolidColorBrush));
            AssertProperty(pair.Brush + "Key", typeof(ResourceKey));

            expected.Add(pair.Color);
            expected.Add(pair.Color + "Key");
            expected.Add(pair.Brush);
            expected.Add(pair.Brush + "Key");
        }

        foreach (var brushName in StandaloneBrushes)
        {
            AssertProperty(brushName, typeof(SolidColorBrush));
            AssertProperty(brushName + "Key", typeof(ResourceKey));
            expected.Add(brushName);
            expected.Add(brushName + "Key");
        }

        var actual = typeof(SystemColors)
            .GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            expected.SetEquals(actual),
            $"Missing: {string.Join(", ", expected.Except(actual))}; Extra: {string.Join(", ", actual.Except(expected))}");

        Assert.Null(typeof(SystemColors).GetField("ControlBrushKey", BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void EveryResourceKeyResolvesToItsMatchingValue()
    {
        ResetApplicationState();
        try
        {
            foreach (var pair in ColorBrushPairs)
            {
                AssertKeyResolves(pair.Color, sameInstance: false);
                AssertKeyResolves(pair.Brush, sameInstance: true);
            }

            foreach (var brushName in StandaloneBrushes)
            {
                AssertKeyResolves(brushName, sameInstance: true);
            }

            var unknown = new ComponentResourceKey(typeof(SystemColors), "UnknownSystemColor");
            Assert.False(SystemColors.TryGetResource(unknown, out var missing));
            Assert.Null(missing);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void BrushColorsMatchColorsAndInstancesRemainStable()
    {
        ResetApplicationState();
        try
        {
            foreach (var pair in ColorBrushPairs)
            {
                var colorProperty = GetProperty(pair.Color);
                var brushProperty = GetProperty(pair.Brush);
                var keyProperty = GetProperty(pair.Brush + "Key");

                var color = Assert.IsType<Color>(colorProperty.GetValue(null));
                var firstBrush = Assert.IsType<SolidColorBrush>(brushProperty.GetValue(null));
                var secondBrush = Assert.IsType<SolidColorBrush>(brushProperty.GetValue(null));

                Assert.Equal(color, firstBrush.Color);
                Assert.Same(firstBrush, secondBrush);
                Assert.Same(keyProperty.GetValue(null), keyProperty.GetValue(null));
            }

            foreach (var brushName in StandaloneBrushes)
            {
                var property = GetProperty(brushName);
                Assert.Same(property.GetValue(null), property.GetValue(null));
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void GradientThemeBrushesAreConvertedFromTheirColorResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var highlight = Color.FromRgb(0x12, 0x34, 0x56);
            var gradient = new LinearGradientBrush(Color.Black, Color.White, 0);
            app.Resources["SystemColorHighlightColor"] = highlight;
            app.Resources["SystemColorHighlightColorBrush"] = gradient;

            var first = SystemColors.HighlightBrush;
            var second = SystemColors.HighlightBrush;

            Assert.NotSame(gradient, first);
            Assert.Same(first, second);
            Assert.Equal(highlight, SystemColors.HighlightColor);
            Assert.Equal(highlight, first.Color);

            var accent = Color.FromRgb(0x65, 0x43, 0x21);
            app.Resources["SystemAccentColor"] = accent;

            Assert.Equal(accent, SystemColors.AccentColor);
            Assert.Equal(accent, SystemColors.AccentColorBrush.Color);
            Assert.Same(SystemColors.AccentColorBrush, SystemColors.AccentColorBrush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void AssertProperty(string name, Type propertyType)
    {
        var property = GetProperty(name);
        Assert.Equal(propertyType, property.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.True(property.GetMethod!.IsPublic);
        Assert.True(property.GetMethod.IsStatic);
    }

    private static void AssertKeyResolves(string valuePropertyName, bool sameInstance)
    {
        var expected = GetProperty(valuePropertyName).GetValue(null);
        var key = Assert.IsAssignableFrom<ResourceKey>(GetProperty(valuePropertyName + "Key").GetValue(null));

        Assert.True(SystemColors.TryGetResource(key, out var actual));
        if (sameInstance)
        {
            Assert.Same(expected, actual);
        }
        else
        {
            Assert.Equal(expected, actual);
        }
    }

    private static PropertyInfo GetProperty(string name) =>
        typeof(SystemColors).GetProperty(name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new Xunit.Sdk.XunitException($"SystemColors.{name} was not found.");

    private static void ResetApplicationState()
    {
        typeof(Application)
            .GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);

        typeof(ThemeManager)
            .GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }
}
