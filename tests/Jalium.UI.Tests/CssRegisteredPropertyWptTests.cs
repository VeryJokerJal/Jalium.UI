using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssRegisteredPropertyWptTests
{
    static CssRegisteredPropertyWptTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    [Fact]
    [Trait("WPT", "css/css-properties-values-api/registered-property-change-style-001.html")]
    public void AddingARegistration_UpdatesPreviouslyInheritedColors()
    {
        // Declarative @property replaces the upstream JS registration. The same three
        // native nodes and original computed-color expectations are retained.
        var inner = new Border(); var between = new Border { Child = inner }; var root = new Border { Child = between };
        // Represent the upstream document's UA foreground explicitly on the native root.
        root.SetValue(Jalium.UI.Documents.TextElement.ForegroundProperty, Brushes.Black);
        Css.SetStyle(inner, "color:var(--color1)"); Check(Colors.Black);
        Css.SetStyle(between, "color:green"); Check(Colors.Green);
        Css.SetStyleSheet(root, "@property --color1 {syntax:'<color>';inherits:true;initial-value:red}"); Check(Colors.Red);

        Css.SetStyleSheet(root, ""); Css.SetStyle(root, ""); Css.SetStyle(between, "");
        Css.SetStyle(inner, "color:var(--color2)"); Check(Colors.Black);
        Css.SetStyle(root, "--color2:blue"); Css.SetStyle(between, "color:green"); Check(Colors.Blue);
        Css.SetStyle(root, "");
        Css.SetStyleSheet(root, "@property --color2 {syntax:'<color>';inherits:true;initial-value:red}"); Check(Colors.Red);

        void Check(Color expected)
        {
            root.Measure(new(100,100)); root.UpdateLayout();
            var property = CssDependencyPropertyLookup.Find(inner.GetType(), "Foreground")!;
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(inner.GetValue(property)).Color);
        }
    }

    [Fact]
    [Trait("WPT", "css/css-properties-values-api/registered-property-change-style-002.html")]
    public void RegisteringVisibilityAndDisplay_RemovesTheFailureBoxesFromPaintingAndHitTesting()
    {
        var hidden = new Border { Name = "visibility", Width = 40, Height = 20, Background = Brushes.Pink };
        var collapsed = new Border { Name = "display", Width = 40, Height = 20, Background = Brushes.Pink };
        var root = new StackPanel(); root.Children.Add(hidden); root.Children.Add(collapsed);
        Css.SetStyleSheet(root, "#visibility {visibility:var(--my-visibility,visible)} #display {display:var(--my-display,inline-block)}");
        Layout(); Assert.Equal(Visibility.Visible, hidden.Visibility); Assert.Equal(Visibility.Visible, collapsed.Visibility);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
            @property --my-visibility {syntax:'*';inherits:false;initial-value:hidden}
            @property --my-display {syntax:'*';inherits:false;initial-value:none}
            """));
        Layout(); Assert.Equal(Visibility.Hidden, hidden.Visibility); Assert.Equal(Visibility.Collapsed, collapsed.Visibility);
        Assert.NotSame(hidden, root.InputHitTest(new Point(50,10))); Assert.NotSame(collapsed, root.InputHitTest(new Point(50,30)));
        var reference = new Canvas(); reference.Measure(new(100,60)); reference.Arrange(new(0,0,100,60));
        Assert.Equal(Pixels(reference), Pixels(root));
        void Layout() { root.Measure(new(100,60)); root.Arrange(new(0,0,100,60)); root.UpdateLayout(); }
    }

    [Theory]
    [Trait("WPT", "css/css-properties-values-api/registered-property-computation.html")]
    [InlineData("<length>", "14em", "140px")]
    [InlineData("<length>", "calc(16px - 7em + 10vh)", "-24px")]
    [InlineData("<length-percentage>", "calc(19em - 2%)", "calc(-2% + 190px)")]
    [InlineData("<length>#", "4em ,9px", "40px, 9px")]
    [InlineData("<length-percentage>#", "calc(50% + 1em), 4px", "calc(50% + 10px), 4px")]
    [InlineData("<length>+", "10px 3em", "10px 30px")]
    [InlineData("<transform-function>", "translateX(10em)", "translateX(100px)")]
    [InlineData("<transform-function>", "translateX(calc(11em + 10%))", "translateX(calc(10% + 110px))")]
    [InlineData("<integer>+", "15 calc(2.4) calc(2.6)", "15 2 3")]
    [InlineData("<number>+", "15 calc(15 + 15) calc(24 / 10)", "15 30 2.4")]
    [InlineData("<color>", "currentcolor", "rgb(0, 0, 255)")]
    [InlineData("<time>", "calc(1000ms + 1s)", "2s")]
    [InlineData("<resolution>", "calc(1dppx + 96dpi)", "2dppx")]
    public void ComputedValues_PreserveTheSupportedUpstreamExpectations(string syntax, string input, string expected)
    {
        // These assertions use the upstream 10px font and blue foreground, with a fixed
        // 400x300 native viewport. Other font units and modern color cases stay pending.
        var element = new Border(); var root = new Border { Child = element };
        var initial = syntax.StartsWith("<color>", StringComparison.Ordinal) ? "black" : syntax.StartsWith("<transform", StringComparison.Ordinal) ? "scale(1)"
            : syntax.StartsWith("<time>", StringComparison.Ordinal) ? "0s" : syntax.StartsWith("<resolution>", StringComparison.Ordinal) ? "1dppx"
            : syntax.StartsWith("<number>", StringComparison.Ordinal) || syntax.StartsWith("<integer>", StringComparison.Ordinal) ? "0" : "0px";
        Css.SetStyleSheet(root, $"@property --value {{syntax:'{syntax}';inherits:false;initial-value:{initial}}}");
        Css.SetStyle(element, "font-size:10px; color:blue; --value:" + input);
        root.Measure(new(400,300)); root.Arrange(new(0,0,400,300)); root.UpdateLayout();
        Assert.Equal(expected, CssNode.Get(element).CssRuntimeState!.CustomProperties!["--value"]);
    }

    private static byte[] Pixels(Visual visual)
    {
        var bitmap = new RenderTargetBitmap(100,60,96,96,PixelFormat.Bgra32); bitmap.Clear(Colors.White); bitmap.Render(visual);
        var pixels = new byte[100*60*4]; bitmap.CopyPixels(new(0,0,100,60), pixels,400,0); return pixels;
    }
}
