using Jalium.UI.Controls;
using Jalium.UI.Styling;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssRandomItemTests
{
    private static Border Apply(string style)
    {
        var border = new Border();
        Css.SetStyle(border, style);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        return border;
    }

    [Theory]
    [InlineData("fixed 0", 10)]
    [InlineData("fixed 0.5", 20)]
    [InlineData("fixed 0.999", 30)]
    [InlineData("fixed 1", 30)]
    [InlineData("fixed calc(1 / 4)", 10)]
    public void FixedKeySelectsTheSpecifiedItem(string key, double expected)
        => Assert.Equal(expected, Apply($"width:random-item({key},10px,20px,30px)").Width);

    [Fact]
    public void OnlyTheSelectedItemIsSubstituted()
    {
        Assert.Equal(10, Apply("width:random-item(fixed 0,10px,var(--missing))").Width);
        Assert.True(double.IsNaN(Apply("width:random-item(fixed 0.9,10px,var(--missing))").Width));
        Assert.True(double.IsNaN(Apply("width:random-item(fixed 0.5,10px,,20px)").Width));
    }

    [Fact]
    public void BracedItemsKeepInternalCommasAndExpandShorthands()
    {
        var border = Apply("margin:random-item(fixed 0,{1px 2px},{3px 4px})");
        Assert.Equal(1, border.Margin.Top);
        Assert.Equal(2, border.Margin.Right);
    }

    [Fact]
    public void RandomItemCanSupplyVariableName()
        => Assert.Equal(15, Apply("--size:15px;--name:--size;width:var(random-item(fixed 0,var(--name),--other))").Width);

    [Fact]
    public void KeyAndSelectedItemSubstituteAfterArgumentSplitting()
    {
        Assert.Equal(10, Apply("--key:fixed 0;--size:10px;width:random-item(var(--key),var(--size),var(--missing))").Width);
        Assert.Equal(10, Apply("width:var(--missing,random-item(fixed 0,10px,20px))").Width);
        Assert.Equal(20, Apply("width:random-item(fixed 0,random-item(fixed 0.9,10px,20px),30px)").Width);
    }

    [Fact]
    public void OnlyTheChosenCustomPropertyOptionCanFormACycle()
    {
        Assert.Equal(10, Apply("--size:random-item(fixed 0,10px,var(--size));width:var(--size)").Width);
        Assert.True(double.IsNaN(Apply("--size:random-item(fixed 0.9,10px,var(--size));width:var(--size)").Width));
    }

    [Fact]
    public void SpreadCanSupplyAnEntireArgumentList()
    {
        Assert.Equal(10, Apply("--args:fixed 0,10px,20px;width:random-item(...var(--args))").Width);
        Assert.Equal(10, Apply("width:if(...random-item(fixed 0,else:10px,else:20px))").Width);
    }

    [Fact]
    public void ParseTimeChecksOnlyTheArgumentGrammar()
    {
        Assert.True(Css.Supports("width", "random-item(bad-key,10px,20px)"));
        Assert.False(Css.Supports("width", "random-item()"));
        Assert.False(Css.Supports("width", "random-item(auto)"));
        Assert.False(Css.Supports("width", "random-item(,10px)"));
        Assert.False(Css.Supports("width", "random-item(auto,{10px} extra)"));
        Assert.False(Css.Supports("width", "random-item(...var(--args), !)"));
        Assert.False(Css.Supports("width", "random-item(...var(--args), {10px;20px})"));
        Assert.True(double.IsNaN(Apply("width:random-item(bad-key,10px,20px)").Width));
    }

    [Fact]
    public void NamedKeySharesAStableSelectionAcrossProperties()
    {
        var widths = string.Join(',', Enumerable.Range(1, 32).Select(index => $"{index}px"));
        var heights = string.Join(',', Enumerable.Range(1, 32).Select(index => $"{index * 10}px"));
        var border = Apply($"width:random-item(--pair,{widths});height:random-item(--pair,{heights})");
        Assert.InRange(border.Width, 1, 32);
        Assert.Equal(border.Width * 10, border.Height);
        var original = border.Width;
        Css.SetStyle(border, $"width:random-item(--pair,{widths});height:random-item(--pair,{heights});opacity:.75");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(original, border.Width);
    }

    [Fact]
    public void KeyframesSelectTheirValueAndTimingFunction()
    {
        var rule = Assert.Single(CssStyleSheet.Parse("""
            @keyframes fade {
              from {
                opacity: random-item(fixed 0.5, .1, .5, .9);
                animation-timing-function: random-item(fixed 0, steps(2, start), ease)
              }
            }
            """).Keyframes);
        var frame = Assert.Single(CssKeyframes.EvaluateFrames(CssNode.Get(new Border()), rule));
        Assert.Equal(.5, frame.Values[UIElement.OpacityProperty]);
        Assert.Equal("steps", frame.TimingFunction?.Kind);
        Assert.Equal(2, frame.TimingFunction?.Steps);
    }

    [Fact]
    public void RegisteredCustomPropertySelectsBeforeInheritance()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@property --size {syntax:'<length>';inherits:true;initial-value:3px}");
        Css.SetStyle(root, "--size:random-item(fixed 0.999,5px,15px)");
        Css.SetStyle(child, "width:var(--size)");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(15, child.Width);
    }

    [Theory]
    [InlineData("random(fixed 0,10px,20px)", 10)]
    [InlineData("random(fixed 0.5,10px,20px)", 15)]
    [InlineData("random(fixed 1,10px,20px)", 20)]
    [InlineData("random(fixed 0.5,10px,20px,5px)", 15)]
    [InlineData("random(fixed 0.5,10px,20px,6px)", 16)]
    [InlineData("random(fixed 0.5,20px,10px)", 20)]
    [InlineData("calc(5px + random(fixed 0.5,10px,20px))", 20)]
    public void NumericRandomResolvesRangesAndMath(string value, double expected)
        => Assert.Equal(expected, Apply($"width:{value}").Width);

    [Fact]
    public void NumericRandomSharesNamedBaseWithRandomItem()
    {
        var options = string.Join(',', Enumerable.Range(0, 101).Select(index => $"{index}px"));
        var border = Apply($"width:random(--pair,0px,100px,1px);height:random-item(--pair,{options})");
        Assert.InRange(border.Width, 0, 100);
        Assert.Equal(border.Width, border.Height);
    }

    [Fact]
    public void UnregisteredRandomIsEvaluatedWhenUsed()
    {
        Assert.Equal(15, Apply("--size:random(fixed 0.5,10px,20px);width:var(--size)").Width);
        Assert.InRange(Apply("--size:random(10px,20px);width:var(--size)").Width, 10, 20);
        var border = Apply("width:random(10px,20px)");
        var first = border.Width;
        Assert.InRange(first, 10, 20);
        Css.SetStyle(border, "width:random(10px,20px);opacity:.75");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(first, border.Width);
    }

    [Fact]
    public void TypedCustomPropertyComputesNumericRandomBeforeInheritance()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@property --size {syntax:'<length>';inherits:true;initial-value:3px}");
        Css.SetStyle(root, "--size:random(fixed 0.5,10px,20px)");
        Css.SetStyle(child, "width:var(--size)");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(15, child.Width);

        Css.SetStyle(root, "--size:random(10px,20px);width:var(--size)");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.InRange(root.Width, 10, 20);
        Assert.Equal(root.Width, child.Width);
    }

    [Fact]
    public void InvalidNumericRandomArgumentsAreRejected()
    {
        Assert.False(Css.Supports("width", "random()"));
        Assert.False(Css.Supports("width", "random(10px)"));
        Assert.False(Css.Supports("width", "random(10px,20deg)"));
        Assert.False(Css.Supports("width", "random(bad-key,10px,20px)"));
    }

    [Theory]
    [InlineData("random(fixed 0.5,0deg,180deg)", 90)]
    [InlineData("random(fixed 0.5,1s,3s)", 2000)]
    [InlineData("random(fixed 0.5,50px,100%,1em)", 78)]
    [InlineData("random(fixed 1,0px,100px,100px / 3)", 100)]
    [InlineData("random(fixed 0.5,10px,20px,infinity * 1px)", 10)]
    public void NumericRandomRetainsUnitsUntilEvaluation(string text, double expected)
    {
        var reader = new CssTokenReader(text);
        Assert.True(reader.TryReadFunction(out var name, out var args));
        var expression = Assert.IsType<CssMathExpression>(CssMathExpression.Parse(name.ToString(), args.Remaining));
        Assert.True(expression.TryEvaluate(CssLengthContext.Default, 100, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IfConditionsUseEarlyRandomIndexesSeparateFromSelectedValues()
    {
        var node = CssNode.Get(new Border());
        Assert.True(CssRandomItem.TryBase("element-scoped ua-early-width-1", node, "width", 0, out var early1));
        Assert.True(CssRandomItem.TryBase("element-scoped ua-early-width-2", node, "width", 0, out var early2));
        var source = $$"""
            if(style(random(element-scoped property-index-scoped,0,1) = {{early1.ToString("R", CultureInfo.InvariantCulture)}}):10px;else:20px)
            random(element-scoped property-index-scoped,0px,100px)
            if(style(random(element-scoped property-index-scoped,0,1) = {{early2.ToString("R", CultureInfo.InvariantCulture)}}):30px;else:40px)
            random(element-scoped property-index-scoped,0px,100px)
            """;
        Assert.True(CssCustomProperties.TrySubstitute(source, node, out var result, "width"));
        Assert.Contains("10px", result);
        Assert.Contains("30px", result);

        var randoms = Regex.Matches(result, @"random\(fixed ([0-9.E-]+),");
        Assert.Equal(2, randoms.Count);
        for (var index = 0; index < randoms.Count; index++)
        {
            Assert.True(CssRandomItem.TryBase($"element-scoped ua-width-{index + 1}", node,
                "width", index, out var expected));
            Assert.Equal(expected, double.Parse(randoms[index].Groups[1].Value, CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void UnregisteredCustomPropertyEvaluatesConditionButDefersSelectedRandom()
    {
        var node = CssNode.Get(new Border());
        Assert.True(CssRandomItem.TryBase("element-scoped ua-early---size-1", node,
            "--size", 0, out var early));
        var declarations = new Dictionary<string, string>
        {
            ["--size"] = $$"""
                if(style(random(element-scoped property-index-scoped,0,1) = {{early.ToString("R", CultureInfo.InvariantCulture)}}):
                    random(10px,20px);else:random(30px,40px))
                """
        };
        var computed = CssCustomProperties.Compute(null, declarations, node)["--size"];
        Assert.Contains("random(10px,20px)", computed);
        Assert.DoesNotContain("fixed ", computed);
    }

    [Fact]
    public void UnusedRegisteredFallbackDoesNotConsumeRandomIndex()
    {
        var node = CssNode.Get(new Border());
        Assert.True(CssRandomItem.TryBase("element-scoped ua-width-1", node, "width", 0, out var expected));
        var source = "var(--value,random(element-scoped property-index-scoped,0px,100px)) " +
            "random(element-scoped property-index-scoped,0px,100px)";
        Assert.True(CssCustomProperties.Substitute(source,
            name => name == "--value" ? "5px" : null, out var result,
            validateFallback: (_, _) => true, validateFallbackFor: _ => true,
            element: node, randomContext: new("width")));
        var remaining = Assert.Single(Regex.Matches(result, @"random\(fixed ([0-9.E-]+),").Cast<Match>());
        Assert.Equal(expected, double.Parse(remaining.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void UnchosenSpreadOptionDoesNotEvaluateItsNumericRandom()
    {
        var node = CssNode.Get(new Border());
        Assert.True(CssRandomItem.TryBase("element-scoped ua-width-2", node, "width", 1, out var expected));
        var source = "random-item(fixed 0,10px,...var(--option)) " +
            "random(element-scoped property-index-scoped,0px,100px)";
        Assert.True(CssCustomProperties.Substitute(source,
            name => name == "--option" ? "random(element-scoped property-index-scoped,0px,100px)" : null,
            out var result, element: node, randomContext: new("width")));
        Assert.Contains("10px", result);
        var remaining = Assert.Single(Regex.Matches(result, @"random\(fixed ([0-9.E-]+),").Cast<Match>());
        Assert.Equal(expected, double.Parse(remaining.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ChosenSpreadOptionIsEvaluatedInPropertyOrder()
    {
        var node = CssNode.Get(new Border());
        var source = "random-item(fixed 0,...var(--option),20px) " +
            "random(element-scoped property-index-scoped,0px,100px)";
        Assert.True(CssCustomProperties.Substitute(source,
            name => name == "--option" ? "random(element-scoped property-index-scoped,0px,100px)" : null,
            out var result, element: node, randomContext: new("width")));
        var randoms = Regex.Matches(result, @"random\(fixed ([0-9.E-]+),");
        Assert.Equal(2, randoms.Count);
        for (var index = 0; index < randoms.Count; index++)
        {
            Assert.True(CssRandomItem.TryBase($"element-scoped ua-width-{index + 2}", node,
                "width", index + 1, out var expected));
            Assert.Equal(expected, double.Parse(randoms[index].Groups[1].Value, CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void SpreadIfConditionEvaluatesNumericRandomEarly()
    {
        var node = CssNode.Get(new Border());
        Assert.True(CssRandomItem.TryBase("element-scoped ua-early-width-1", node,
            "width", 0, out var early));
        var clause = $"style(random(element-scoped property-index-scoped,0,1) = " +
            $"{early.ToString("R", CultureInfo.InvariantCulture)}):10px";
        var source = "if(...var(--clause);else:20px) " +
            "random(element-scoped property-index-scoped,0px,100px)";
        Assert.True(CssCustomProperties.Substitute(source,
            name => name == "--clause" ? clause : null,
            out var result, element: node, randomContext: new("width")));
        Assert.Contains("10px", result);
        var remaining = Assert.Single(Regex.Matches(result, @"random\(fixed ([0-9.E-]+),").Cast<Match>());
        Assert.True(CssRandomItem.TryBase("element-scoped ua-width-1", node, "width", 0, out var expected));
        Assert.Equal(expected, double.Parse(remaining.Groups[1].Value, CultureInfo.InvariantCulture));
    }
}
