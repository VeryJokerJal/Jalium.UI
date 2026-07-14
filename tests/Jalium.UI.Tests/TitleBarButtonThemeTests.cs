using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using ShapePath = Jalium.UI.Shapes.Path;

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

            var path = Assert.IsType<ShapePath>(button.FindName("path"));
            AssertPathData(button, path, TitleBarButtonKind.Minimize, "M14 8v1H3V8h11z");
            AssertPathData(button, path, TitleBarButtonKind.Maximize, "M3 3v10h10V3H3zm9 9H4V4h8v8z");
            AssertPathData(button, path, TitleBarButtonKind.Restore, "M3 5v9h9V5H3zm8 8H4V6h7v7z M5 5h1V4h7v7h-1v1h2V3H5v2z");
            AssertPathData(button, path, TitleBarButtonKind.Close, "M7.116 8l-4.558 4.558.884.884L8 8.884l4.558 4.558.884-.884L8.884 8l4.558-4.558-.884-.884L8 7.116 3.442 2.558l-.884.884L7.116 8z");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void AssertPathData(
        TitleBarButton button,
        ShapePath path,
        TitleBarButtonKind kind,
        string expected)
    {
        button.Kind = kind;
        Geometry actual = Assert.IsAssignableFrom<Geometry>(path.Data);
        Assert.Equal(
            Geometry.Parse(expected).ToString(System.Globalization.CultureInfo.InvariantCulture),
            actual.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Brush InvokePrivateBrushResolver(TitleBarButton button, string methodName)
    {
        var method = typeof(TitleBarButton).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(button, null));
    }
}
