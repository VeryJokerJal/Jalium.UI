using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using System.Reflection;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssTemplateBorderTests
{
    [Fact]
    public void TypedTemplateBinding_PaintsFourCssBorderSides()
    {
        var owner = Owner("border:8px solid; border-color:red green blue yellow");
        var pixels = CssBorderRadiusTests.Pixels(owner, 80, 60);
        AssertSideColors(pixels);
    }

    [Fact]
    public void DeferredTemplateBinding_PaintsFourCssBorderSides()
    {
        var owner = Assert.IsType<Control>(XamlReader.Parse("""
            <Control xmlns="http://schemas.jalium.ui/2024"
                     Css.Style="border:8px solid; border-color:red green blue yellow">
              <Control.Template>
                <ControlTemplate TargetType="Control">
                  <Border BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}" />
                </ControlTemplate>
              </Control.Template>
            </Control>
            """));
        CssBorderRadiusTests.Layout(owner, 80, 60);
        AssertSideColors(CssBorderRadiusTests.Pixels(owner, 80, 60));
    }

    [Fact]
    public void NativeValuesAndStyleRemoval_TakeOverAndRestoreTemplateBorder()
    {
        var owner = Owner("border:8px solid; border-color:red green blue yellow");
        var chrome = Assert.IsType<Border>(owner.GetVisualChild(0));
        Assert.NotNull(CssBorderPaintProperties.Get(chrome));

        owner.BorderBrush = owner.BorderBrush;
        Assert.Null(CssBorderPaintProperties.Get(chrome));
        owner.ClearValue(Control.BorderBrushProperty);
        Assert.NotNull(CssBorderPaintProperties.Get(chrome));

        owner.SetCurrentValue(Control.BorderBrushProperty, Brushes.Purple);
        Assert.Null(CssBorderPaintProperties.Get(chrome));
        Css.SetStyle(owner, "border:8px solid; border-color:maroon green blue yellow");
        Css.SetStyle(owner, "border:8px solid; border-color:red green blue yellow");
        Assert.NotNull(CssBorderPaintProperties.Get(chrome));

        chrome.BorderBrush = Brushes.Purple;
        Assert.Null(CssBorderPaintProperties.Get(chrome));
        chrome.ClearValue(Border.BorderBrushProperty);
        Assert.NotNull(CssBorderPaintProperties.Get(chrome));

        chrome.SetLayerValue(Border.BorderBrushProperty, Brushes.Purple,
            DependencyObject.LayerValueSource.ParentTemplateTrigger, allowAutoTransition: false);
        Assert.Null(CssBorderPaintProperties.Get(chrome));
        chrome.ClearLayerValue(Border.BorderBrushProperty,
            DependencyObject.LayerValueSource.ParentTemplateTrigger, allowAutoTransition: false);
        Assert.NotNull(CssBorderPaintProperties.Get(chrome));

        owner.BorderThickness = owner.BorderThickness;
        Assert.Null(CssBorderStyleProperties.Get(chrome));
        owner.ClearValue(Control.BorderThicknessProperty);
        Assert.NotNull(CssBorderStyleProperties.Get(chrome));

        Css.SetStyle(owner, "");
        Assert.Null(CssBorderPaintProperties.Get(chrome));
    }

    [Fact]
    public void ChangingCssBorderStyle_UpdatesTemplatePaint()
    {
        var owner = Owner("border:8px solid; border-color:red green blue yellow");
        var chrome = Assert.IsType<Border>(owner.GetVisualChild(0));
        Css.SetStyle(owner, "border:9px double red");
        Assert.Equal(CssBorderLineStyle.Double, CssBorderStyleProperties.Get(chrome)?.Top);
        var pixels = CssBorderRadiusTests.Pixels(owner, 80, 60);
        Assert.Equal((byte)255, pixels[(1 * 80 + 40) * 4 + 2]);
        Assert.Equal((byte)0, pixels[(4 * 80 + 40) * 4 + 3]);
        Assert.Equal((byte)255, pixels[(7 * 80 + 40) * 4 + 2]);
    }

    [Fact]
    public void ReplacingTemplate_ClearsForwardedBorderPaint()
    {
        var owner = Owner("border:8px solid; border-color:red green blue yellow");
        var oldChrome = Assert.IsType<Border>(owner.GetVisualChild(0));
        var replacement = new ControlTemplate(typeof(Control));
        replacement.SetVisualTree(() => new Border());
        owner.Template = replacement;
        CssBorderRadiusTests.Layout(owner, 80, 60);
        Assert.Null(CssBorderPaintProperties.Get(oldChrome));
        Assert.Null(CssBorderStyleProperties.Get(oldChrome));
        Assert.Null(CssBorderPaintProperties.Get(Assert.IsType<Border>(owner.GetVisualChild(0))));
    }

    [Theory]
    [InlineData("Label", "LabelBorder")]
    [InlineData("TextBox", "OuterBorder")]
    [InlineData("NumberBox", "OuterBorder")]
    [InlineData("Button", "RootBorder")]
    public void BuiltInTemplate_PaintsCssBorderSides(string controlName, string chromeName)
    {
        ResetTheme();
        var app = new Application();
        try
        {
            Control control = controlName switch
            {
                "Label" => new Label(),
                "TextBox" => new TextBox(),
                "NumberBox" => new NumberBox(),
                _ => new Button(),
            };
            control.Background = null;
            var host = new Border { Child = control };
            Css.SetStyle(control, "border:8px solid; border-color:red green blue yellow");
            CssBorderRadiusTests.Layout(host, 80, 60);
            Assert.NotNull(CssBorderPaintProperties.Get(Assert.IsType<Border>(control.FindName(chromeName))));
            AssertSideColors(CssBorderRadiusTests.Pixels(host, 80, 60));
        }
        finally { ResetTheme(); GC.KeepAlive(app); }
    }

    private static Control Owner(string style)
    {
        var owner = new Control();
        var template = new ControlTemplate(typeof(Control));
        template.SetVisualTree(() =>
        {
            var chrome = new Border();
            chrome.SetTemplateBinding(Border.BorderBrushProperty, Control.BorderBrushProperty);
            chrome.SetTemplateBinding(Border.BorderThicknessProperty, Control.BorderThicknessProperty);
            return chrome;
        });
        owner.Template = template;
        Css.SetStyle(owner, style);
        CssBorderRadiusTests.Layout(owner, 80, 60);
        return owner;
    }

    private static void AssertSideColors(byte[] pixels)
    {
        Assert.Equal((byte)255, pixels[(4 * 80 + 40) * 4 + 2]);
        Assert.Equal((byte)128, pixels[(30 * 80 + 76) * 4 + 1]);
        Assert.Equal((byte)255, pixels[(56 * 80 + 40) * 4]);
        Assert.Equal((byte)255, pixels[(30 * 80 + 4) * 4 + 1]);
    }

    private static void ResetTheme()
    {
        typeof(Application).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, null);
        typeof(Jalium.UI.Controls.Themes.ThemeManager).GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
    }
}
