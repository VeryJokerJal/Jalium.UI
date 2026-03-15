using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ThemeColorCompatibilityTests
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
    public void AccentCompatibilityTokens_ShouldBeAvailableInDarkTheme()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        _ = new Application();

        try
        {
            AssertBrushResourceExists("AccentFillColorDefaultBrush");
            AssertBrushResourceExists("AccentFillColorSecondaryBrush");
            AssertBrushResourceExists("AccentFillColorTertiaryBrush");
            AssertBrushResourceExists("AccentTextFillColorPrimaryBrush");
            AssertBrushResourceExists("AccentTextFillColorSecondaryBrush");
            AssertBrushResourceExists("AccentTextFillColorTertiaryBrush");
            AssertBrushResourceExists("SystemFillColorAttentionBrush");
            AssertColorResourceExists("SystemAccentColor");
            AssertColorResourceExists("SystemAccentColorLight1");
            AssertColorResourceExists("SystemAccentColorLight2");
            AssertColorResourceExists("SystemAccentColorLight3");
            AssertColorResourceExists("SystemAccentColorDark1");
            AssertColorResourceExists("SystemAccentColorDark2");
            AssertColorResourceExists("SystemAccentColorDark3");
            AssertColorResourceExists("AccentTextFillColorPrimary");
            AssertColorResourceExists("AccentTextFillColorSecondary");
            AssertColorResourceExists("AccentTextFillColorTertiary");
            AssertColorResourceExists("AccentTextFillColorDisabled");
            AssertColorResourceExists("AccentFillColorDefault");
            AssertColorResourceExists("AccentFillColorSecondary");
            AssertColorResourceExists("AccentFillColorTertiary");
            AssertColorResourceExists("AccentFillColorDisabled");
            AssertColorResourceExists("AccentFillColorSelectedTextBackground");
            AssertColorResourceExists("SystemFillColorAttention");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void AccentCompatibilityTokens_ShouldUpdateWhenThemeChanges()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var darkBrush = Assert.IsType<SolidColorBrush>(app.Resources["AccentFillColorDefaultBrush"]);
            var darkColor = Assert.IsType<Color>(app.Resources["AccentFillColorDefault"]);

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            var lightBrush = Assert.IsType<SolidColorBrush>(app.Resources["AccentFillColorDefaultBrush"]);
            var lightColor = Assert.IsType<Color>(app.Resources["AccentFillColorDefault"]);
            Assert.NotEqual(darkBrush.Color, lightBrush.Color);
            Assert.NotEqual(darkColor, lightColor);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CorePaletteAliases_ShouldBeAvailableAndSwitchWithTheme()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var darkTextPrimary = Assert.IsType<SolidColorBrush>(app.Resources["TextPrimary"]);
            var darkControlBackground = Assert.IsType<SolidColorBrush>(app.Resources["ControlBackground"]);
            var darkBackground = Assert.IsType<SolidColorBrush>(app.Resources["Background"]);
            var darkForeground = Assert.IsType<SolidColorBrush>(app.Resources["Foreground"]);
            var darkBorderBrush = Assert.IsType<SolidColorBrush>(app.Resources["BorderBrush"]);
            var darkWindowBackground = Assert.IsType<SolidColorBrush>(app.Resources["WindowBackground"]);
            var darkMenuBackground = Assert.IsType<SolidColorBrush>(app.Resources["MenuFlyoutPresenterBackground"]);
            var darkCommandBarBackground = Assert.IsType<SolidColorBrush>(app.Resources["CommandBarBackground"]);

            Assert.Equal(Color.FromRgb(255, 255, 255), darkTextPrimary.Color);
            Assert.Equal(Color.FromRgb(0x45, 0x45, 0x45), darkControlBackground.Color);
            Assert.Equal(darkControlBackground.Color, darkBackground.Color);
            Assert.Equal(darkTextPrimary.Color, darkForeground.Color);
            Assert.Equal(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), darkBorderBrush.Color);
            Assert.Equal(Color.FromRgb(0x20, 0x20, 0x20), darkWindowBackground.Color);
            Assert.Equal(Color.FromRgb(0x1C, 0x1C, 0x1C), darkMenuBackground.Color);
            Assert.Equal(Color.FromRgb(0x1C, 0x1C, 0x1C), darkCommandBarBackground.Color);

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            var lightTextPrimary = Assert.IsType<SolidColorBrush>(app.Resources["TextPrimary"]);
            var lightControlBackground = Assert.IsType<SolidColorBrush>(app.Resources["ControlBackground"]);
            var lightBackground = Assert.IsType<SolidColorBrush>(app.Resources["Background"]);
            var lightForeground = Assert.IsType<SolidColorBrush>(app.Resources["Foreground"]);
            var lightBorderBrush = Assert.IsType<SolidColorBrush>(app.Resources["BorderBrush"]);
            var lightWindowBackground = Assert.IsType<SolidColorBrush>(app.Resources["WindowBackground"]);
            var lightMenuBackground = Assert.IsType<SolidColorBrush>(app.Resources["MenuFlyoutPresenterBackground"]);
            var lightCommandBarBackground = Assert.IsType<SolidColorBrush>(app.Resources["CommandBarBackground"]);

            Assert.Equal(Color.FromArgb(0xE4, 0x00, 0x00, 0x00), lightTextPrimary.Color);
            Assert.Equal(Color.FromRgb(255, 255, 255), lightControlBackground.Color);
            Assert.Equal(lightControlBackground.Color, lightBackground.Color);
            Assert.Equal(lightTextPrimary.Color, lightForeground.Color);
            Assert.Equal(Color.FromArgb(0x0F, 0x00, 0x00, 0x00), lightBorderBrush.Color);
            Assert.Equal(Color.FromRgb(0xF3, 0xF3, 0xF3), lightWindowBackground.Color);
            Assert.Equal(Color.FromRgb(255, 255, 255), lightMenuBackground.Color);
            Assert.Equal(Color.FromRgb(255, 255, 255), lightCommandBarBackground.Color);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ButtonPressedBrush_ShouldBeDeeperThanHover_AndSwitchWithTheme()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var darkHover = Assert.IsType<SolidColorBrush>(app.Resources["ControlBackgroundHover"]);
            var darkPressed = Assert.IsType<SolidColorBrush>(app.Resources["ButtonBackgroundPressed"]);

            Assert.Equal(Color.FromArgb(0x19, 0x00, 0x00, 0x00), darkPressed.Color);
            Assert.NotEqual(darkHover.Color, darkPressed.Color);

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            var lightHover = Assert.IsType<SolidColorBrush>(app.Resources["ControlBackgroundHover"]);
            var lightPressed = Assert.IsType<SolidColorBrush>(app.Resources["ButtonBackgroundPressed"]);

            Assert.Equal(Color.FromArgb(0x06, 0x00, 0x00, 0x00), lightPressed.Color);
            Assert.NotEqual(lightHover.Color, lightPressed.Color);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CoreColorTokens_ShouldExist_AndMatchBrushes()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            AssertColorMatchesBrush(app, "TextFillColorPrimary", "TextFillColorPrimaryBrush");
            AssertColorMatchesBrush(app, "ControlFillColorDefault", "ControlFillColorDefaultBrush");
            AssertColorMatchesBrush(app, "ControlStrokeColorDefault", "ControlStrokeColorDefaultBrush");
            AssertColorMatchesBrush(app, "SubtleFillColorSecondary", "SubtleFillColorSecondaryBrush");
            AssertColorMatchesBrush(app, "DividerStrokeColorDefault", "DividerStrokeColorDefaultBrush");
            AssertColorMatchesBrush(app, "CardBackgroundFillColorDefault", "CardBackgroundFillColorDefaultBrush");
            AssertColorMatchesBrush(app, "LayerFillColorDefault", "LayerFillColorDefaultBrush");
            AssertColorMatchesBrush(app, "SolidBackgroundFillColorBase", "SolidBackgroundFillColorBaseBrush");
            AssertColorMatchesBrush(app, "SystemFillColorSuccess", "SystemFillColorSuccessBrush");
            AssertColorMatchesBrush(app, "SystemFillColorNeutralBackground", "SystemFillColorNeutralBackgroundBrush");
            AssertColorMatchesBrush(app, "SystemFillColorSolidNeutral", "SystemFillColorSolidNeutralBrush");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void LegacyAliasColors_ShouldExist_AndMatchBrushes()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            AssertColorMatchesBrush(app, "CommandBarBorder", "CommandBarBorderBrush");
            AssertColorMatchesBrush(app, "MenuFlyoutPresenterBorder", "MenuFlyoutPresenterBorderBrush");
            AssertColorMatchesBrush(app, "InfoBarInfo", "InfoBarInfoBrush");
            AssertColorMatchesBrush(app, "InfoBarSuccess", "InfoBarSuccessBrush");
            AssertColorMatchesBrush(app, "InfoBarWarning", "InfoBarWarningBrush");
            AssertColorMatchesBrush(app, "InfoBarError", "InfoBarErrorBrush");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void HighContrastCoreColorTokens_ShouldExist_AndMatchBrushes()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();
        var previousThemeKey = ResourceDictionary.CurrentThemeKey;

        try
        {
            ResourceDictionary.CurrentThemeKey = "HighContrast";

            AssertColorMatchesBrush(app, "TextFillColorPrimary", "TextFillColorPrimaryBrush");
            AssertColorMatchesBrush(app, "TextFillColorSecondary", "TextFillColorSecondaryBrush");
            AssertColorMatchesBrush(app, "TextFillColorTertiary", "TextFillColorTertiaryBrush");
            AssertColorMatchesBrush(app, "TextFillColorDisabled", "TextFillColorDisabledBrush");
            AssertColorMatchesBrush(app, "TextOnAccentFillColorPrimary", "TextOnAccentFillColorPrimaryBrush");
            AssertColorMatchesBrush(app, "ControlFillColorDefault", "ControlFillColorDefaultBrush");
            AssertColorMatchesBrush(app, "ControlStrokeColorDefault", "ControlStrokeColorDefaultBrush");
            AssertColorMatchesBrush(app, "SolidBackgroundFillColorBase", "SolidBackgroundFillColorBaseBrush");
        }
        finally
        {
            ResourceDictionary.CurrentThemeKey = previousThemeKey;
            ResetApplicationState();
        }
    }

    [Fact]
    public void SystemColorCompatibilityResources_ShouldExist_AndMatchBrushes()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();
        var previousThemeKey = ResourceDictionary.CurrentThemeKey;

        try
        {
            foreach (var key in new[]
                     {
                         "SystemColorWindowTextColor",
                         "SystemColorWindowColor",
                         "SystemColorButtonFaceColor",
                         "SystemColorButtonTextColor",
                         "SystemColorHighlightColor",
                         "SystemColorHighlightTextColor",
                         "SystemColorHotlightColor",
                         "SystemColorGrayTextColor"
                     })
            {
                AssertColorResourceExists(key);
                AssertBrushResourceExists($"{key}Brush");
            }

            ResourceDictionary.CurrentThemeKey = "HighContrast";
            AssertColorMatchesBrush(app, "SystemColorWindowTextColor", "SystemColorWindowTextColorBrush");
            AssertColorMatchesBrush(app, "SystemColorWindowColor", "SystemColorWindowColorBrush");
            AssertColorMatchesBrush(app, "SystemColorButtonFaceColor", "SystemColorButtonFaceColorBrush");
            AssertColorMatchesBrush(app, "SystemColorButtonTextColor", "SystemColorButtonTextColorBrush");
            AssertColorMatchesBrush(app, "SystemColorHighlightColor", "SystemColorHighlightColorBrush");
            AssertColorMatchesBrush(app, "SystemColorHighlightTextColor", "SystemColorHighlightTextColorBrush");
            AssertColorMatchesBrush(app, "SystemColorHotlightColor", "SystemColorHotlightColorBrush");
            AssertColorMatchesBrush(app, "SystemColorGrayTextColor", "SystemColorGrayTextColorBrush");
        }
        finally
        {
            ResourceDictionary.CurrentThemeKey = previousThemeKey;
            ResetApplicationState();
        }
    }

    [Fact]
    public void ElevationBorderCompatibilityBrushes_ShouldMatchThemeDefinitions()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var darkControl = Assert.IsType<LinearGradientBrush>(app.Resources["ControlElevationBorderBrush"]);
            var darkCircle = Assert.IsType<LinearGradientBrush>(app.Resources["CircleElevationBorderBrush"]);
            var darkAccent = Assert.IsType<LinearGradientBrush>(app.Resources["AccentControlElevationBorderBrush"]);

            Assert.Equal(2, darkControl.GradientStops.Count);
            Assert.Equal(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), darkControl.GradientStops[0].Color);
            Assert.Equal(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), darkControl.GradientStops[1].Color);
            Assert.Equal(2, darkCircle.GradientStops.Count);
            Assert.Equal(2, darkAccent.GradientStops.Count);

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            var lightControl = Assert.IsType<LinearGradientBrush>(app.Resources["ControlElevationBorderBrush"]);
            var lightCircle = Assert.IsType<LinearGradientBrush>(app.Resources["CircleElevationBorderBrush"]);
            var lightAccent = Assert.IsType<LinearGradientBrush>(app.Resources["AccentControlElevationBorderBrush"]);

            Assert.Equal(Color.FromArgb(0x29, 0x00, 0x00, 0x00), lightControl.GradientStops[0].Color);
            Assert.Equal(Color.FromArgb(0x0F, 0x00, 0x00, 0x00), lightControl.GradientStops[1].Color);
            Assert.Equal(2, lightCircle.GradientStops.Count);
            Assert.Equal(2, lightAccent.GradientStops.Count);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void HighContrastElevationBorderCompatibilityBrushes_ShouldResolveToSolidBrushes()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();
        var previousThemeKey = ResourceDictionary.CurrentThemeKey;

        try
        {
            ResourceDictionary.CurrentThemeKey = "HighContrast";

            Assert.IsType<SolidColorBrush>(app.Resources["ControlElevationBorderBrush"]);
            Assert.IsType<SolidColorBrush>(app.Resources["CircleElevationBorderBrush"]);
            Assert.IsType<SolidColorBrush>(app.Resources["AccentControlElevationBorderBrush"]);
        }
        finally
        {
            ResourceDictionary.CurrentThemeKey = previousThemeKey;
            ResetApplicationState();
        }
    }

    [Fact]
    public void ControlOnImageTokens_ShouldExist_AndMatchBrushesAcrossThemes()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            AssertColorMatchesBrush(app, "ControlOnImageFillColorDefault", "ControlOnImageFillColorDefaultBrush");
            AssertColorMatchesBrush(app, "ControlOnImageFillColorSecondary", "ControlOnImageFillColorSecondaryBrush");
            AssertColorMatchesBrush(app, "ControlOnImageFillColorTertiary", "ControlOnImageFillColorTertiaryBrush");
            AssertColorMatchesBrush(app, "ControlOnImageFillColorDisabled", "ControlOnImageFillColorDisabledBrush");

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            AssertColorMatchesBrush(app, "ControlOnImageFillColorDefault", "ControlOnImageFillColorDefaultBrush");
            AssertColorMatchesBrush(app, "ControlOnImageFillColorSecondary", "ControlOnImageFillColorSecondaryBrush");
            AssertColorMatchesBrush(app, "ControlOnImageFillColorTertiary", "ControlOnImageFillColorTertiaryBrush");
            AssertColorMatchesBrush(app, "ControlOnImageFillColorDisabled", "ControlOnImageFillColorDisabledBrush");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void AssertBrushResourceExists(string key)
    {
        Assert.True(Application.Current!.Resources.TryGetValue(key, out var value));
        Assert.IsType<SolidColorBrush>(value);
    }

    private static void AssertColorResourceExists(string key)
    {
        Assert.True(Application.Current!.Resources.TryGetValue(key, out var value));
        Assert.IsType<Color>(value);
    }

    private static void AssertColorMatchesBrush(Application app, string colorKey, string brushKey)
    {
        var color = Assert.IsType<Color>(app.Resources[colorKey]);
        var brush = Assert.IsType<SolidColorBrush>(app.Resources[brushKey]);
        Assert.Equal(color, brush.Color);
    }
}
