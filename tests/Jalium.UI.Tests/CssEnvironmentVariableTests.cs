using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssEnvironmentVariableTests
{
    private static void Layout(FrameworkElement root, double width = 400, double height = 300)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new(width, height));
        root.Arrange(new(0, 0, width, height));
        root.UpdateLayout();
    }

    [Theory]
    [Trait("WPT", "css/css-env/env-parsing.html")]
    [InlineData("env(safe-area-inset-top)", true)]
    [InlineData("env(safe-area-inset-top,)", true)]
    [InlineData("env(safe-area-inset-top, )", true)]
    [InlineData("env(safe-area-inset-top ())", false)]
    [InlineData("env(safe-area-inset-top () )", false)]
    [InlineData("env(safe-area-inset-top() )", false)]
    [InlineData("env(safe-area-inset-top (),)", false)]
    [InlineData("env(safe-area-inset-top(),)", false)]
    [InlineData("env(safe-area-inset-top -1, 9px)", false)]
    [InlineData("env(safe-area-inset-top 0, 9px)", true)]
    public void EnvironmentFunctionFollowsArgumentGrammar(string value, bool valid)
        => Assert.Equal(valid, Css.Supports("width", value));

    [Theory]
    [Trait("WPT", "css/css-env/indexed-env.tentative.html")]
    [InlineData("env(test1 test2, green)", false)]
    [InlineData("env(test1 10 20 test2, green)", false)]
    [InlineData("env(test 0.1, green)", false)]
    [InlineData("env(test -1, green)", false)]
    [InlineData("env(test 0, green)", true)]
    [InlineData("env(test 0,)", true)]
    [InlineData("env(test 0)", true)]
    [InlineData("env(test 0 1 2 3 4, green)", true)]
    public void IndexedEnvironmentGrammarMatchesWpt(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("width", value));

    [Theory]
    [Trait("WPT", "css/css-env/env-fallback-substitution.html")]
    [InlineData("env(unknown-env-name,10px)")]
    [InlineData("env(unknown-env-name, 10px)")]
    [InlineData("env(unknown-env-name,\n   10px)")]
    [InlineData("env(unknown-env-name, /* comment */ 10px)")]
    [InlineData("env(unknown-env-name, env(other-unknown-env-name, 10px))")]
    [InlineData("env(viewport-segment-width 99 99, 10px)")]
    public void EnvironmentFallbackDropsLeadingWhitespaceAndComments(string value)
    {
        Assert.True(CssEnvironmentVariables.TrySubstitute(value, null, out var substituted));
        Assert.Equal("10px", CssParser.StripComments(substituted).Trim().ToString());
    }

    [Theory]
    [Trait("WPT", "css/css-env/env-fallback-substitution.html")]
    [InlineData("env(unknown, 10px   )", "var(--missing, 10px   )")]
    [InlineData("env(unknown, 10px /* comment */)", "var(--missing, 10px /* comment */)")]
    [InlineData("env(unknown, 10px   ) 5px", "var(--missing, 10px   ) 5px")]
    public void EnvironmentAndVariableFallbacksPreserveTheSameTrailingTokens(string environment, string variable)
    {
        Assert.True(CssCustomProperties.TrySubstitute(environment,
            (IReadOnlyDictionary<string, string>?)null, out var environmentValue));
        Assert.True(CssCustomProperties.TrySubstitute(variable,
            (IReadOnlyDictionary<string, string>?)null, out var variableValue));
        Assert.Equal(variableValue, environmentValue);
    }

    [Fact]
    [Trait("WPT", "css/css-env/env-in-custom-properties.tentative.html")]
    public void MissingEnvironmentInvalidatesLocalCustomPropertyInsteadOfInheriting()
    {
        var child = new Border();
        var parent = new Border { Child = child };
        Css.SetStyle(parent, "--var1:inherited");
        Css.SetStyle(child, "--my-width:env(test, 100px);width:var(--my-width);--var1:env(nonexistent)");
        Layout(parent);
        Assert.Equal(100, child.Width);
        Assert.False(CssNode.Get(child).CssRuntimeState!.CustomProperties!.ContainsKey("--var1"));
    }

    [Theory]
    [InlineData("env(safe-area-inset-top)", true)]
    [InlineData("ENV(safe-area-inset-right)", true)]
    [InlineData("env(safe-area-inset-bottom)", true)]
    [InlineData("env(safe-area-inset-left)", true)]
    [InlineData("env(SAFE-AREA-INSET-TOP)", false)]
    [InlineData("env(unknown)", false)]
    [InlineData("env()", false)]
    [InlineData("env(safe-area-inset-top, 0px)", false)]
    [InlineData("env(safe-area-inset-top 0)", false)]
    [InlineData("(not env(unknown)) and env(safe-area-inset-top)", true)]
    public void SupportsEnvironmentQueryReportsOnlyKnownScalarNames(string condition, bool expected)
        => Assert.Equal(expected, Css.Supports(condition));

    [Fact]
    public void SupportsEnvironmentQueryControlsConditionalRules()
    {
        var border = new Border();
        Css.SetStyleSheet(border, """
            @supports env(safe-area-inset-top) { Border { width:27px } }
            @supports env(unknown) { Border { height:90px } }
            @supports not env(unknown) { Border { height:11px } }
            """);
        Layout(border);
        Assert.Equal(27, border.Width);
        Assert.Equal(11, border.Height);
    }

    [Theory]
    [InlineData("1env(unknown)")]
    [InlineData("#env(unknown)")]
    [InlineData("@env(unknown)")]
    [InlineData("calc(1env(unknown))")]
    public void EnvironmentSubstitutionDoesNotEnterOtherCssTokens(string token)
    {
        Assert.True(CssEnvironmentVariables.TrySubstitute(token, null, out var substituted));
        Assert.Equal(token, substituted);

        var border = new Border();
        Css.SetStyle(border, "--token:" + token);
        Layout(border);
        Assert.Equal(token, CssNode.Get(border).CssRuntimeState!.CustomProperties!["--token"]);
    }

    [Fact]
    public void UnknownEnvironmentNamesUseFallbackAndMissingFallbackInvalidatesAtComputedTime()
    {
        var border = new Border();
        Assert.True(Css.Supports("width", "env(unknown)"));
        Css.SetStyle(border, "--items:env(unknown, red, blue);margin:env(unknown, 3px 5px);" +
            "height:env(unknown, env(safe-area-inset-bottom, 9px));" +
            "width:77px;width:env(unknown)");

        Layout(border);
        Assert.Equal(new Thickness(5, 3, 5, 3), border.Margin);
        Assert.Contains("red, blue", CssNode.Get(border).CssRuntimeState!.CustomProperties!["--items"]);
        Assert.Equal(0, border.Height);
        Assert.True(double.IsNaN(border.Width));

        Css.SetStyle(border, "width:env(unknown, 12px);height:env(unknown,);" +
            "margin-left:env(safe-area-inset-top 0, 9px)");
        Layout(border);
        Assert.Equal(12, border.Width);
        Assert.True(double.IsNaN(border.Height));
        Assert.Equal(9, border.Margin.Left);
    }

    [Fact]
    public void KnownEnvironmentValueSkipsFallbackCyclesInCustomProperties()
    {
        var border = new Border();
        Css.SetStyle(border, "--size:env(safe-area-inset-left, var(--size));" +
            "width:calc(10px + var(--size));height:env(absent, var(--height, 13px));" +
            "margin-left:attr(missing px, env(safe-area-inset-right, 8px))");

        Layout(border);
        Assert.Equal(10, border.Width);
        Assert.Equal(13, border.Height);
        Assert.Equal(0, border.Margin.Left);
    }

    [Fact]
    public void EnvironmentSubstitutionPreservesTokenBoundaries()
    {
        var border = new Border();
        Css.SetStyle(border, "width:env(unknown, 1)px");
        Layout(border);
        Assert.True(double.IsNaN(border.Width));

        Css.SetStyle(border, "width:calc(env(unknown, 1px) + 2px)");
        Layout(border);
        Assert.Equal(3, border.Width);
    }

    [Fact]
    public void SafeAreaEnvironmentValuesRefreshWithTheHostWindow()
    {
        var border = new Border();
        var window = new Window { Content = border, Width = 200, Height = 100 };
        Css.SetStyleSheet(window, "Border { --top:env(safe-area-inset-top);" +
            "width:calc(10px + env(safe-area-inset-left, var(--missing)));" +
            "height:calc(5px + var(--top)) }");

        Layout(window, 200, 100);
        Assert.Equal(10, border.Width);
        Assert.Equal(5, border.Height);

        window.UpdateSafeAreaInsets(new Thickness(4, 7, 8, 9));
        Layout(window, 200, 100);
        Assert.Equal(14, border.Width);
        Assert.Equal(12, border.Height);

        window.UpdateSafeAreaInsets(new Thickness(2, 3, 1, 0));
        Layout(window, 200, 100);
        Assert.Equal(12, border.Width);
        Assert.Equal(8, border.Height);
    }

    [Fact]
    public void RegisteredEnvironmentValueComputesOnOwnerBeforeInheritance()
    {
        var child = new Border();
        var window = new Window { Content = child, Width = 200, Height = 100 };
        Css.SetStyleSheet(window, """
            @property --inset { syntax:'<length>'; inherits:true; initial-value:1px }
            Window { --inset:env(safe-area-inset-left) }
            Border { width:var(--inset) }
            """);

        window.UpdateSafeAreaInsets(new Thickness(6, 0, 0, 0));
        Layout(window, 200, 100);
        Assert.Equal(6, child.Width);

        window.UpdateSafeAreaInsets(new Thickness(2, 0, 0, 0));
        Layout(window, 200, 100);
        Assert.Equal(2, child.Width);
    }
}
