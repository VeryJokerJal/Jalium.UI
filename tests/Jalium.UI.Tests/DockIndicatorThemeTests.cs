using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DockIndicatorThemeTests
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
    public void DockIndicator_Resolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            AssertBrushMatches(app.Resources["DockIndicatorChromeBackground"], InvokeStaticBrushResolver("ResolveChromeBackgroundBrush"));
            AssertBrushMatches(app.Resources["DockIndicatorChromeBorder"], InvokeStaticBrushResolver("ResolveChromeBorderBrush"));
            AssertBrushMatches(app.Resources["DockIndicatorButtonBackground"], InvokeStaticBrushResolver("ResolveButtonBackgroundBrush", false));
            AssertBrushMatches(app.Resources["DockIndicatorButtonHoverBackground"], InvokeStaticBrushResolver("ResolveButtonBackgroundBrush", true));
            AssertPenMatches(app.Resources["DockIndicatorButtonBorder"], InvokeStaticPenResolver("ResolveButtonBorderPen", false), 1);
            AssertPenMatches(app.Resources["DockIndicatorButtonHoverBackground"], InvokeStaticPenResolver("ResolveButtonBorderPen", true), 1);
            Assert.Equal(((SolidColorBrush)app.Resources["DockIndicatorIconForeground"]!).Color, InvokeStaticColorResolver("ResolveIconColor", false));
            Assert.Equal(((SolidColorBrush)app.Resources["DockIndicatorIconHoverForeground"]!).Color, InvokeStaticColorResolver("ResolveIconColor", true));
            AssertBrushMatches(app.Resources["DockIndicatorPreviewBackground"], InvokeStaticBrushResolver("ResolvePreviewBackgroundBrush"));
            AssertPenMatches(app.Resources["DockIndicatorPreviewBorder"], InvokeStaticPenResolver("ResolvePreviewBorderPen"), 2);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokeStaticBrushResolver(string methodName, params object[] args)
    {
        var method = typeof(DockIndicator).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(null, args));
    }

    private static Color InvokeStaticColorResolver(string methodName, params object[] args)
    {
        var method = typeof(DockIndicator).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<Color>(method!.Invoke(null, args)!);
    }

    private static Pen InvokeStaticPenResolver(string methodName, params object[] args)
    {
        var method = typeof(DockIndicator).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<Pen>(method!.Invoke(null, args));
    }

    private static void AssertBrushMatches(object? expectedObj, Brush actual)
    {
        var expected = Assert.IsAssignableFrom<Brush>(expectedObj);

        if (expected is SolidColorBrush expectedSolid && actual is SolidColorBrush actualSolid)
        {
            Assert.Equal(expectedSolid.Color, actualSolid.Color);
            return;
        }

        Assert.Same(expected, actual);
    }

    private static void AssertPenMatches(object? expectedObj, Pen actual, double expectedThickness)
    {
        AssertBrushMatches(expectedObj, Assert.IsAssignableFrom<Brush>(actual.Brush));
        Assert.Equal(expectedThickness, actual.Thickness);
    }
}
