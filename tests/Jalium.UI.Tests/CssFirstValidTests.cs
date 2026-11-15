using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssFirstValidTests
{
    [Theory]
    [InlineData("first-valid(unsupported, 12px)", 12)]
    [InlineData("first-valid(10px, 20px)", 10)]
    [InlineData("first-valid(unsupported, first-valid(invalid, 14px))", 14)]
    [InlineData("first-valid(var(), 16px)", 16)]
    [InlineData("FIRST-VALID(unsupported, 18px)", 18)]
    public void SelectsFirstSupportedWholeValue(string value, double expected)
    {
        Assert.True(Css.Supports("width", value));
        var border = new Border();
        Css.SetStyle(border, "width:" + value);
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(expected, border.Width);
    }

    [Fact]
    public void SelectsACompleteShorthandValue()
    {
        Assert.True(Css.Supports("margin", "first-valid(invalid, 2px 4px)"));
        var border = new Border();
        Css.SetStyle(border, "margin:first-valid(invalid, 2px 4px)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(new Thickness(4, 2, 4, 2), border.Margin);
    }

    [Fact]
    public void KeepsCommasInsideNestedColorFunctions()
    {
        var border = new Border();
        Assert.True(Css.Supports("background-color", "first-valid(invalid, rgb(255, 0, 0))"));
        Css.SetStyle(border, "background-color:first-valid(invalid, rgb(255, 0, 0))");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(border.Background).Color);
    }

    [Fact]
    public void UnwrapsBracedCandidatesAndKeepsTheirCommas()
    {
        var border = new Border();
        Assert.True(Css.Supports("width", "first-valid({invalid, 8px}, {12px}, 20px)"));
        Css.SetStyle(border, "width:first-valid({invalid, 8px}, {12px}, 20px)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(12, border.Width);
    }

    [Fact]
    public void BracedFontListIsOneCandidate()
    {
        var text = new TextBlock();
        Css.SetStyle(text, "font-family:first-valid({Arial, Georgia}, serif)");
        CssEvaluationScheduler.FlushIfPending(text.Dispatcher);
        Assert.Equal("Arial, Georgia", text.FontFamily.Source);
    }

    [Fact]
    public void PreservesImportantPriorityForTheSelectedCandidate()
    {
        var border = new Border();
        Css.SetStyle(border, "width:first-valid(invalid, 15px)!important;width:7px");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(15, border.Width);
    }

    [Fact]
    public void ChosenVariableDoesNotFallThroughAfterSubstitutionFailure()
    {
        var border = new Border();
        Css.SetStyle(border, "width:7px;width:first-valid(var(--missing), 12px)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.True(double.IsNaN(border.Width));
    }

    [Fact]
    public void UnregisteredCustomPropertySelectsItsFirstSyntacticallyValidCandidate()
    {
        var border = new Border();
        Css.SetStyle(border, "--choice:first-valid(15px, invalid);width:var(--choice)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(15, border.Width);

        Css.SetStyle(border, "--choice:first-valid(invalid, 15px);width:var(--choice)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.True(double.IsNaN(border.Width));
    }

    [Fact]
    public void RegisteredCustomPropertySelectsFirstValueMatchingItsSyntax()
    {
        var border = new Border();
        Css.SetStyleSheet(border, "@property --choice { syntax:'<length>'; inherits:false; initial-value:1px }");
        Css.SetStyle(border, "--choice:first-valid(invalid, 15px);width:var(--choice)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(15, border.Width);

        Css.SetStyle(border, "--choice:first-valid(var(--missing), 15px);width:var(--choice)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(1, border.Width);
    }

    [Fact]
    public void RegisteredCustomPropertyAcceptsNestedFirstValidAsTheFirstCandidate()
    {
        var border = new Border();
        Css.SetStyleSheet(border, "@property --choice { syntax:'<length>'; inherits:false; initial-value:1px }");
        Css.SetStyle(border, "--choice:first-valid(first-valid(invalid, 15px), 20px);width:var(--choice)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(15, border.Width);
    }

    [Fact]
    public void NestedSelectionSkipsMalformedSubstitutionsInUnusedCandidates()
    {
        var unregistered = new Border();
        Css.SetStyle(unregistered,
            "--choice:first-valid(first-valid(12px, attr()), 20px);width:var(--choice)");
        CssEvaluationScheduler.FlushIfPending(unregistered.Dispatcher);
        Assert.Equal(12, unregistered.Width);

        var registered = new Border();
        Css.SetStyleSheet(registered,
            "@property --choice { syntax:'<length>'; inherits:false; initial-value:1px }");
        Css.SetStyle(registered,
            "--choice:first-valid(first-valid(12px, attr()), 20px);width:var(--choice)");
        CssEvaluationScheduler.FlushIfPending(registered.Dispatcher);
        Assert.Equal(12, registered.Width);
    }

    [Fact]
    public void UnselectedCandidateDoesNotCreateACustomPropertyCycle()
    {
        var border = new Border();
        Css.SetStyle(border, "--choice:first-valid(12px, var(--choice));width:var(--choice)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(12, border.Width);

        Css.SetStyleSheet(border, "@property --choice { syntax:'<length>'; inherits:false; initial-value:1px }");
        Css.SetStyle(border, "--choice:first-valid(invalid, 17px, var(--choice));width:var(--choice)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(17, border.Width);
    }

    [Fact]
    public void UnusedCandidateDoesNotResolveMissingVariable()
    {
        var border = new Border();
        Css.SetStyle(border, "width:first-valid(10px, var(--missing))");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(10, border.Width);

        Css.SetStyle(border, "width:env(unknown, first-valid(invalid, 12px))");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(12, border.Width);
    }

    [Fact]
    public void RegisteredListOnlyResolvesTheSelectedAttributeCandidate()
    {
        var border = new Border();
        Css.SetStyleSheet(border, "@property --choice { syntax:'<length>'; inherits:false; initial-value:1px }");
        Css.SetStyle(border, "--choice:first-valid(10px, attr(missing px, var(--missing)));" +
            "width:var(--choice)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(10, border.Width);

        Css.SetStyle(border, "--choice:first-valid(invalid, attr(missing px, 13px));" +
            "width:var(--choice)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(13, border.Width);
    }

    [Fact]
    public void NoSupportedCandidateIsInvalidAtComputedValueTime()
    {
        Assert.True(Css.Supports("width", "first-valid(invalid, also-invalid)"));
        var border = new Border();
        Css.SetStyle(border, "width:7px;width:first-valid(invalid, also-invalid)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.True(double.IsNaN(border.Width));
    }

    [Fact]
    public void SelectedRevertRuleReentersTheCascade()
    {
        var child = new Border { Name = "hero" };
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "#hero { width:20px } #hero { width:40px;" +
            "width:first-valid(invalid, revert-rule) }");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(20, child.Width);
    }

    [Fact]
    public void SelectedCustomPropertyRevertRuleReentersTheCascade()
    {
        var child = new Border { Name = "hero" };
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "#hero { --choice:20px } #hero { --choice:first-valid(revert-rule, 30px);" +
            "width:var(--choice) }");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(20, child.Width);
    }

    [Fact]
    public void RegisteredCustomPropertyCanSelectRevertRuleAfterInvalidCandidate()
    {
        var child = new Border { Name = "hero" };
        var root = new Border { Child = child };
        Css.SetStyleSheet(root,
            "@property --choice { syntax:'<length>'; inherits:false; initial-value:1px } " +
            "#hero { --choice:20px } #hero { --choice:first-valid(invalid, revert-rule);" +
            "width:var(--choice) }");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(20, child.Width);
    }

    [Fact]
    public void EnvironmentFallbackCanIntroduceARevertRuleChoice()
    {
        var child = new Border { Name = "hero" };
        var root = new Border { Child = child };
        Css.SetStyleSheet(root,
            "#hero { width:20px } #hero { width:env(unknown, first-valid(invalid, revert-rule)) }");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(20, child.Width);
    }

    [Theory]
    [InlineData("first-valid()")]
    [InlineData("first-valid(12px, 20px) 3px")]
    [InlineData("calc(first-valid(12px, 20px))")]
    public void RejectsInvalidWholeValuePlacement(string value)
        => Assert.False(Css.Supports("width", value));
}
