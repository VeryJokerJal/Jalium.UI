using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssVariableNameSubstitutionTests
{
    private static Border Apply(string style, string? registrations = null)
    {
        var border = new Border { Name = "hero" };
        Css.SetStyleSheet(border, (registrations ?? string.Empty) + "#hero {--other:10px}");
        Css.SetStyle(border, style);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        return border;
    }

    [Theory]
    [Trait("WPT", "css/css-variables/variable-reference-name-substitution.html")]
    [InlineData("--name:--other;width:var(var(--name))", 10)]
    [InlineData("--name:--other;--alias:--name;width:var(var(var(--alias)))", 10)]
    [InlineData("--name:bad;width:var(var(--name),20px)", 20)]
    [InlineData("width:var(var(--missing),30px)", 30)]
    [InlineData("--name:--other;width:var( var(--name) )", 10)]
    [InlineData("--name:bad;width:var(var(--name))", double.NaN)]
    [InlineData("--empty:;width:var(var(--empty),40px)", 40)]
    [InlineData("--name:--other --other;width:var(var(--name),50px)", 50)]
    [InlineData("--name:10px;width:var(var(--name),60px)", 60)]
    [InlineData("--name:'--other';width:var(var(--name),70px)", 70)]
    [InlineData("--name:--;width:var(var(--name),80px)", 80)]
    [InlineData("width:var({--other})", 10)]
    [InlineData("--name:--other;width:var({var(--name)})", 10)]
    [InlineData("width:var( { --other } )", 10)]
    [InlineData("width:var({--missing},90px)", 90)]
    [InlineData("width:var(foo,100px)", 100)]
    [InlineData("width:var(--other extra,110px)", 110)]
    public void SubstitutedNameSelectsCustomPropertyOrFallback(string style, double expected)
    {
        var border = Apply(style);
        if (double.IsNaN(expected)) Assert.True(double.IsNaN(border.Width));
        else Assert.Equal(expected, border.Width);
    }

    [Theory]
    [Trait("WPT", "css/css-variables/variable-reference-name-substitution.html")]
    [InlineData("--self:var(var(--self));width:var(--self,110px)", 110)]
    [InlineData("--name:--cyclic;--cyclic:var(var(--name));width:var(--cyclic,120px)", 120)]
    public void NameSubstitutionPreservesCycleInvalidation(string style, double expected)
    {
        Assert.Equal(expected, Apply(style).Width);
        Assert.Equal(expected, Apply(style,
            "@property --registered {syntax:'<length>';inherits:false;initial-value:1px}").Width);
    }

    [Fact]
    [Trait("WPT", "css/css-variables/variable-reference-name-substitution.html")]
    public void SubstitutedNameCanResolveRegisteredOrInvalidUniversalProperties()
    {
        const string registrations = """
            @property --registered-length {syntax:'<length>';inherits:false;initial-value:7px}
            @property --registered-universal {syntax:'*';inherits:false}
            """;
        Assert.Equal(7, Apply("--name:--registered-length;width:var(var(--name))", registrations).Width);
        Assert.Equal(130, Apply("--name:--registered-universal;width:var(var(--name),130px)", registrations).Width);
        Assert.Equal(140, Apply("--name:bad;width:var(var(--name),140px)", registrations).Width);
    }

    [Fact]
    [Trait("WPT", "css/css-variables/variable-reference-name-substitution.html")]
    public void AttributeCanProvideVariableName()
    {
        var border = new Border { Name = "hero" };
        Css.SetStyleSheet(border, "#hero {--other:10px}");
        Css.SetAttribute(border, "data-name", "--other");
        Css.SetStyle(border, "width:var(attr(data-name type(*)))");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(10, border.Width);

        Css.SetAttribute(border, "data-name", "10px");
        Css.SetStyle(border, "width:var(attr(data-name type(*)),100px)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(100, border.Width);
    }

    [Fact]
    public void DynamicNameIsAcceptedBeforeComputedValueResolution()
    {
        Assert.Single(CssParser.ParseInlineDeclarations("width:var(var(--name),20px)", null));
        Assert.Single(CssParser.ParseInlineDeclarations("width:var(foo,20px)", null));
        Assert.True(Css.Supports("width", "var(var(--name),20px)"));
        Assert.True(Css.Supports("width", "var(foo,20px)"));
    }

    [Fact]
    public void AttributeTaintedNameCannotSelectUrlContent()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--name"] = "--image",
            ["--image"] = "url(asset.png)"
        };
        string? Resolve(string name) => values.GetValueOrDefault(name);
        bool Tainted(string name) => name == "--name";

        Assert.False(CssCustomProperties.Substitute("var(var(--name))", Resolve, out _, isTainted: Tainted));
        values["--image"] = "asset.png";
        Assert.False(CssCustomProperties.Substitute("url(var(var(--name)))", Resolve, out _, isTainted: Tainted));
    }
}
