using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using ShapePath = Jalium.UI.Controls.Shapes.Path;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class TitleBarButtonThemeTests
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
    public void TitleBarButton_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var hoverBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TitleBarButtonHover"]);
            var pressedBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TitleBarButtonPressed"]);
            var closeHoverBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TitleBarCloseButtonHover"]);
            var closePressedBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TitleBarCloseButtonPressed"]);
            var glyphBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TitleBarGlyph"]);

            var button = new TitleBarButton();

            Assert.Same(hoverBrush, InvokePrivateBrushResolver(button, "ResolveHoverBackgroundBrush"));
            Assert.Same(pressedBrush, InvokePrivateBrushResolver(button, "ResolvePressedBackgroundBrush"));
            Assert.Same(closeHoverBrush, InvokePrivateBrushResolver(button, "ResolveCloseHoverBackgroundBrush"));
            Assert.Same(closePressedBrush, InvokePrivateBrushResolver(button, "ResolveClosePressedBackgroundBrush"));
            Assert.Same(glyphBrush, InvokePrivateBrushResolver(button, "ResolveGlyphBrush"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TitleBarButton_TemplateGlyphs_ShouldUseInsetPathData()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var button = new TitleBarButton();
            var host = new StackPanel { Width = 80, Height = 40 };
            host.Children.Add(button);

            host.Measure(new Size(80, 40));
            host.Arrange(new Rect(0, 0, 80, 40));
            button.ApplyTemplate();

            Assert.Equal("M 1,0 L 9,0", Assert.IsType<ShapePath>(button.FindName("MinimizeGlyph")).Data);
            Assert.Equal("M 1,1 L 9,1 L 9,9 L 1,9 Z", Assert.IsType<ShapePath>(button.FindName("MaximizeGlyph")).Data);
            Assert.Equal("M 1,1 L 9,9 M 9,1 L 1,9", Assert.IsType<ShapePath>(button.FindName("CloseGlyph")).Data);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokePrivateBrushResolver(TitleBarButton button, string methodName)
    {
        var method = typeof(TitleBarButton).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(button, null));
    }
}
