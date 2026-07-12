using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class SystemColorsThemeTests
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
    public void SystemColors_BrushProperties_ShouldUseCompatibleApplicationThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            AssertThemeBrush(app, "SystemColorWindowColorBrush", "SystemColorWindowColor", SystemColors.WindowBrush);
            AssertThemeBrush(app, "SystemColorWindowTextColorBrush", "SystemColorWindowTextColor", SystemColors.WindowTextBrush);
            AssertThemeBrush(app, "SystemColorButtonFaceColorBrush", "SystemColorButtonFaceColor", SystemColors.ControlBrush);
            AssertThemeBrush(app, "SystemColorButtonTextColorBrush", "SystemColorButtonTextColor", SystemColors.ControlTextBrush);
            AssertThemeBrush(app, "SystemColorHighlightColorBrush", "SystemColorHighlightColor", SystemColors.HighlightBrush);
            AssertThemeBrush(app, "SystemColorHighlightTextColorBrush", "SystemColorHighlightTextColor", SystemColors.HighlightTextBrush);
            AssertThemeBrush(app, "SystemColorHotlightColorBrush", "SystemColorHotlightColor", SystemColors.HotTrackBrush);
            AssertThemeBrush(app, "SystemColorGrayTextColorBrush", "SystemColorGrayTextColor", SystemColors.GrayTextBrush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void SystemColors_ColorProperties_ShouldMirrorApplicationThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.Equal(Assert.IsType<Color>(app.Resources["SystemColorWindowColor"]), SystemColors.WindowColor);
            Assert.Equal(Assert.IsType<Color>(app.Resources["SystemColorWindowTextColor"]), SystemColors.WindowTextColor);
            Assert.Equal(Assert.IsType<Color>(app.Resources["SystemColorHighlightColor"]), SystemColors.HighlightColor);
            Assert.Equal(Assert.IsType<Color>(app.Resources["SystemColorHighlightTextColor"]), SystemColors.HighlightTextColor);
            Assert.Equal(Assert.IsType<Color>(app.Resources["SystemColorGrayTextColor"]), SystemColors.GrayTextColor);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void AssertThemeBrush(
        Application app,
        string brushResourceKey,
        string colorResourceKey,
        SolidColorBrush actual)
    {
        object? themedBrush = app.Resources[brushResourceKey];
        Assert.NotNull(themedBrush);
        Color themedColor = Assert.IsType<Color>(app.Resources[colorResourceKey]);

        if (themedBrush is SolidColorBrush solidColorBrush)
        {
            Assert.Same(solidColorBrush, actual);
        }
        else
        {
            Assert.IsAssignableFrom<Brush>(themedBrush);
            Assert.NotSame(themedBrush, actual);
            Assert.Equal(themedColor, actual.Color);
        }
    }
}
