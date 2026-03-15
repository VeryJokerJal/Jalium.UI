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
    public void SystemColors_BrushProperties_ShouldUseApplicationThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.Same(app.Resources["SystemColorWindowColorBrush"], SystemColors.WindowBrush);
            Assert.Same(app.Resources["SystemColorWindowTextColorBrush"], SystemColors.WindowTextBrush);
            Assert.Same(app.Resources["SystemColorButtonFaceColorBrush"], SystemColors.ControlBrush);
            Assert.Same(app.Resources["SystemColorButtonTextColorBrush"], SystemColors.ControlTextBrush);
            Assert.Same(app.Resources["SystemColorHighlightColorBrush"], SystemColors.HighlightBrush);
            Assert.Same(app.Resources["SystemColorHighlightTextColorBrush"], SystemColors.HighlightTextBrush);
            Assert.Same(app.Resources["SystemColorHotlightColorBrush"], SystemColors.HotTrackBrush);
            Assert.Same(app.Resources["SystemColorGrayTextColorBrush"], SystemColors.GrayTextBrush);
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
            Assert.Equal(Assert.IsType<SolidColorBrush>(app.Resources["SystemColorWindowColorBrush"]).Color, SystemColors.WindowColor);
            Assert.Equal(Assert.IsType<SolidColorBrush>(app.Resources["SystemColorWindowTextColorBrush"]).Color, SystemColors.WindowTextColor);
            Assert.Equal(Assert.IsType<SolidColorBrush>(app.Resources["SystemColorHighlightColorBrush"]).Color, SystemColors.HighlightColor);
            Assert.Equal(Assert.IsType<SolidColorBrush>(app.Resources["SystemColorHighlightTextColorBrush"]).Color, SystemColors.HighlightTextColor);
            Assert.Equal(Assert.IsType<SolidColorBrush>(app.Resources["SystemColorGrayTextColorBrush"]).Color, SystemColors.GrayTextColor);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
