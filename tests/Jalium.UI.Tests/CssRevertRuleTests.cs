using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssRevertRuleTests
{
    private static Border Apply(string stylesheet, string? inline = null)
    {
        var border = new Border { Name = "hero" };
        Css.SetStyleSheet(border, stylesheet);
        if (inline is not null) Css.SetStyle(border, inline);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        return border;
    }

    [Fact]
    [Trait("WPT", "css/css-cascade/revert-rule-basic.html")]
    public void RevertRuleSkipsItsEntireRuleAndRespectsCascadeOrder()
    {
        Assert.True(Css.Supports("width", "revert-rule"));
        Assert.False(Css.Supports("width", "env(revert-rule, 20px)"));
        Assert.Equal(20, Apply("#hero {width:20px} #hero {width:40px;width:revert-rule}").Width);
        Assert.Equal(20, Apply("""
            #hero {width:10px}
            #hero#hero#hero {width:40px;width:revert-rule}
            #hero#hero {width:20px}
            """).Width);
        Assert.Equal(40, Apply("#hero {width:20px} #hero {width:40px}",
            "width:50px;width:revert-rule").Width);
        Assert.Equal(20, Apply("""
            #hero {width:10px} #hero {width:20px}
            #hero {width:40px;width:revert-rule}
            #hero {width:50px;width:revert-rule}
            #hero {width:60px;width:revert-rule}
            """).Width);
    }

    [Fact]
    [Trait("WPT", "css/css-cascade/revert-rule-important.html")]
    public void ImportantRevertRuleCanRevealNormalDeclarations()
    {
        Assert.Equal(20, Apply("#hero {width:revert-rule !important} #hero {width:20px}").Width);
        Assert.Equal(20, Apply("#hero {width:20px} #hero {width:revert-rule !important}").Width);
        Assert.Equal(20, Apply("@layer early {#hero {width:revert-rule !important}} #hero {width:20px}").Width);
    }

    [Fact]
    [Trait("WPT", "css/css-cascade/revert-rule-layer.html")]
    public void RevertRuleCanCrossLayersOrRevealAnEarlierRuleInTheSameLayer()
    {
        Assert.Equal(20, Apply("""
            @layer first, second;
            @layer first {#hero {width:20px}}
            @layer second {#hero {width:40px;width:revert-rule}}
            """).Width);
        Assert.Equal(25, Apply("""
            @layer first, second;
            @layer first {#hero {width:20px}}
            @layer second {
                #hero {width:25px}
                #hero {width:40px;width:revert-rule}
            }
            """).Width);
    }

    [Fact]
    [Trait("WPT", "css/css-cascade/revert-rule-revert-layer.html")]
    public void RevertRuleAndRevertLayerCanChainWithoutDroppingOtherProperties()
    {
        var border = Apply("""
            @layer base, middle, top;
            @layer base {#hero {width:20px}}
            @layer middle {
                #hero {width:30px}
                #hero {width:revert-layer}
            }
            @layer top {#hero {width:revert-rule;height:44px}}
            """);
        Assert.Equal(20, border.Width);
        Assert.Equal(44, border.Height);
    }

    [Theory]
    [Trait("WPT", "css/css-cascade/revert-rule-cycle.tentative.html")]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportantRuleAndNormalLayerRollbackCycleUsesUnset(bool multipleNormalLayers)
    {
        var child = new TextBlock { Name = "hero" };
        var root = new Border { Child = child };
        Css.SetStyle(root, "--size:35px;color:green");
        var extraLayer = multipleNormalLayers ? "@layer c {#hero {--size:revert-layer;width:revert-layer;color:revert-layer}}" : "";
        Css.SetStyleSheet(root, $$$"""
            @layer base {#hero {--size:10px;width:20px;color:red}}
            @layer a {#hero {--size:revert-rule !important;width:revert-rule !important;color:revert-rule !important}}
            @layer b {#hero {--size:revert-layer;width:revert-layer;color:revert-layer}}
            {{{extraLayer}}}
            """);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.True(double.IsNaN(child.Width));
        Assert.Equal("35px", CssNode.Get(child).CssRuntimeState!.CustomProperties!["--size"]);
        Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(child.Foreground).Color);
    }

    [Fact]
    [Trait("WPT", "css/css-nesting/nesting-revert-rule.html")]
    public void NestedRulesKeepSeparateRuleIdentity()
    {
        var child = new Border { Name = "hero" };
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, ":root { #hero {width:20px} #hero {width:40px;width:revert-rule} }");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(20, child.Width);
    }

    [Fact]
    [Trait("WPT", "css/css-cascade/revert-rule-custom-property.html")]
    public void RevertRuleSelectsPreviousCustomPropertyRules()
    {
        var border = Apply("""
            #hero {--a:12px;--b:14px}
            #hero {--a:22px;--b:revert-rule}
            #hero {--a:revert-rule;--b:revert-rule}
            #hero {width:var(--a);height:var(--b)}
            """);
        Assert.Equal(22, border.Width);
        Assert.Equal(14, border.Height);
    }

    [Fact]
    [Trait("WPT", "css/css-env/env-revert-rule.html")]
    public void EnvironmentFallbackCanRevertTheCurrentRule()
        => Assert.Equal(20, Apply("#hero {width:20px} #hero {width:40px;width:env(unknown, revert-rule)}").Width);

    [Fact]
    [Trait("WPT", "css/css-values/attr-revert-rule.html")]
    public void AttributeFallbackCanRevertTheCurrentRuleAndRefresh()
    {
        var border = Apply("#hero {width:20px} #hero {width:40px;width:attr(data-width type(<length>), revert-rule)}");
        Assert.Equal(20, border.Width);
        Css.SetAttribute(border, "data-width", "35px");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(35, border.Width);
        Css.SetAttribute(border, "data-width", null);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(20, border.Width);
    }

    [Fact]
    public void VariableFallbackUsesCurrentCustomPropertiesWhenRollingBackRules()
    {
        var border = Apply("#hero {width:20px} #hero {width:40px;width:var(--chosen, revert-rule)}");
        Assert.Equal(20, border.Width);

        Css.SetStyle(border, "--chosen:35px");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(35, border.Width);

        Css.SetStyle(border, "--chosen:initial");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(20, border.Width);
    }

    [Fact]
    public void VariableFallbackCanSelectLayerOrOriginRollback()
    {
        Assert.Equal(20, Apply("""
            @layer base, app;
            @layer base {#hero {width:20px}}
            @layer app {#hero {width:40px;width:var(--missing, revert-layer)}}
            """).Width);
        Assert.True(double.IsNaN(Apply("#hero {width:20px} #hero {width:var(--missing, revert)}").Width));
    }

    [Fact]
    public void VariableRollbackUpdatesRegisteredFontRelativeCustomProperties()
    {
        var text = new TextBlock { Name = "hero" };
        Css.SetStyleSheet(text, """
            @property --relative {syntax:'<length>';inherits:false;initial-value:1px}
            #hero {font-size:20px}
            #hero {font-size:var(--missing, revert-rule);--relative:2em}
            #hero {width:var(--relative)}
            """);
        CssEvaluationScheduler.FlushIfPending(text.Dispatcher);
        Assert.Equal(20, text.FontSize);
        Assert.Equal(40, text.Width);
    }

    [Fact]
    [Trait("WPT", "css/css-variables/revert-rule-in-fallback.html")]
    public void VariableFallbackRevertsCustomShorthandAndLonghandDeclarations()
    {
        var child = new Border { Name = "child" };
        var parent = new Border { Name = "parent", Child = child };
        Css.SetStyleSheet(parent, """
            #child {--x:PASS;margin:1px;padding-left:1px}
            #parent {--x:FAIL;margin:-1px;padding-left:-1px}
            #child {
                --x:var(--unknown, revert-rule);
                margin:var(--unknown, revert-rule);
                padding-left:var(--unknown, revert-rule)
            }
            """);
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Assert.Equal("PASS", CssNode.Get(child).CssRuntimeState!.CustomProperties!["--x"]);
        Assert.Equal(new Thickness(1), child.Margin);
        Assert.Equal(1, child.Padding.Left);
    }

    [Fact]
    [Trait("WPT", "css/css-variables/revert-layer-in-fallback.html")]
    public void VariableFallbackRevertsCustomShorthandAndLonghandLayers()
    {
        var child = new Border { Name = "child" };
        var parent = new Border { Name = "parent", Child = child };
        Css.SetStyleSheet(parent, """
            @layer {#child {--x:PASS;margin:1px;padding-left:1px}}
            @layer {
                #parent {--x:FAIL;margin:-1px;padding-left:-1px}
                #child {
                    --x:var(--unknown, revert-layer);
                    margin:var(--unknown, revert-layer);
                    padding-left:var(--unknown, revert-layer)
                }
            }
            """);
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Assert.Equal("PASS", CssNode.Get(child).CssRuntimeState!.CustomProperties!["--x"]);
        Assert.Equal(new Thickness(1), child.Margin);
        Assert.Equal(1, child.Padding.Left);
    }

    [Fact]
    [Trait("WPT", "css/css-variables/revert-rule-to-var.html")]
    public void RevertRuleCanSelectEarlierVariableDeclaration()
    {
        var text = new TextBlock { Name = "target" };
        Css.SetStyleSheet(text, """
            #target {--green:green;color:var(--green)}
            #target {color:red;color:revert-rule}
            """);
        CssEvaluationScheduler.FlushIfPending(text.Dispatcher);
        Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(text.Foreground).Color);
    }

    [Fact]
    public void CustomPropertyVariableFallbackRollsBackItsRuleAndRefreshes()
    {
        var border = Apply("""
            #hero {--size:10px}
            #hero {--size:20px;--size:var(--choice, revert-rule)}
            #hero {width:var(--size)}
            """);
        Assert.Equal("10px", CssNode.Get(border).CssRuntimeState!.CustomProperties!["--size"]);
        Assert.Equal(10, border.Width);

        Css.SetStyle(border, "--choice:25px");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(25, border.Width);

        Css.SetStyle(border, "--choice:initial");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(10, border.Width);
    }

    [Fact]
    public void CustomPropertyVariableFallbackCanRollBackLayerAndOrigin()
    {
        Assert.Equal(10, Apply("""
            @layer base, app;
            @layer base {#hero {--size:10px}}
            @layer app {#hero {--size:20px;--size:var(--missing, revert-layer)}}
            #hero {width:var(--size)}
            """).Width);

        var child = new Border { Name = "hero" };
        var root = new Border { Child = child };
        Css.SetStyle(root, "--size:35px");
        Css.SetStyleSheet(root, "#hero {--size:10px} #hero {--size:var(--missing, revert)} #hero {width:var(--size)}");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(35, child.Width);
    }

    [Fact]
    public void RegisteredCustomPropertyVariableFallbackRollsBackItsRule()
    {
        var border = Apply("""
            @property --size {syntax:'<length>';inherits:false;initial-value:5px}
            #hero {--size:10px}
            #hero {--size:var(--missing, revert-rule)}
            #hero {width:var(--size)}
            """);
        Assert.Equal(10, border.Width);
    }

    [Fact]
    public void DependentCustomPropertyUsesValueAfterAnotherRuleRollsBack()
    {
        var border = Apply("""
            #hero {--first:10px;--second:20px}
            #hero {--second:var(--missing, revert-rule)}
            #hero {--first:var(--second)}
            #hero {width:var(--first)}
            """);
        Assert.Equal(20, border.Width);
        Assert.Equal("20px", CssNode.Get(border).CssRuntimeState!.CustomProperties!["--second"]);
    }

    [Theory]
    [InlineData("--size:var(--size, revert-rule)")]
    [InlineData("--size:var(--middle, revert-rule);--middle:var(--size)")]
    public void CyclicCustomPropertyVariableDoesNotRollBackItsRule(string declarations)
    {
        var border = Apply($"#hero {{--size:10px}} #hero {{{declarations}}} #hero {{width:var(--size, 17px)}}");
        Assert.Equal(17, border.Width);
    }

    [Fact]
    public void EnvironmentAndAttributeFallbacksCanRollBackCustomPropertyRules()
    {
        Assert.Equal(10, Apply("#hero {--size:10px} #hero {--size:env(unknown, revert-rule)} #hero {width:var(--size)}").Width);

        var border = Apply("""
            #hero {--size:10px}
            #hero {--size:attr(data-size type(<length>), revert-rule)}
            #hero {width:var(--size)}
            """);
        Assert.Equal(10, border.Width);
        Css.SetAttribute(border, "data-size", "25px");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(25, border.Width);
        Css.SetAttribute(border, "data-size", null);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(10, border.Width);
    }

    [Fact]
    public void LogicalAliasesAndAllShorthandUseRuleIdentity()
    {
        Assert.Equal(5, Apply("#hero {margin-left:5px} #hero {margin-inline-start:9px;margin-left:revert-rule}").Margin.Left);
        var border = Apply("#hero {width:20px;height:30px} #hero {width:40px;all:revert-rule}");
        Assert.Equal(20, border.Width);
        Assert.Equal(30, border.Height);
    }
}
