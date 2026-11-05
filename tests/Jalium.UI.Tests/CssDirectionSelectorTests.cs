using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssDirectionSelectorTests
{
    static CssDirectionSelectorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    [Theory]
    [InlineData(":dir(ltr)", true)]
    [InlineData(":dir(RTL)", true)]
    [InlineData(":dir(\\72 tl)", true)]
    [InlineData(":dir(other)", true)]
    [InlineData(":dir(/* comment */ rtl /**/)", true)]
    [InlineData(":dir()", false)]
    [InlineData(":dir('rtl')", false)]
    [InlineData(":dir(rtl,ltr)", false)]
    [InlineData(":dir(rtl ltr)", false)]
    [InlineData(":dir(7)", false)]
    public void SelectorSupport_RequiresOneIdentifier(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Fact]
    public void CssDirection_DoesNotChangeDocumentDirectionOrSelectorSpecificity()
    {
        var child = new Border();
        var root = new StackPanel();
        root.Children.Add(child);
        Css.SetStyleSheet(root, "StackPanel, Border { direction:rtl } " +
            "Border:dir(ltr) { opacity:.4 } Border { opacity:.8 } Border:dir(rtl) { width:99px }");
        Flush(root);
        Assert.Equal(FlowDirection.RightToLeft, child.FlowDirection);
        Assert.Equal(.4, child.Opacity);
        Assert.True(double.IsNaN(child.Width));

        root.FlowDirection = FlowDirection.RightToLeft;
        Flush(root);
        Assert.Equal(.8, child.Opacity);
        Assert.Equal(99, child.Width);

        child.FlowDirection = FlowDirection.LeftToRight;
        Flush(root);
        Assert.Equal(.4, child.Opacity);
        Assert.True(double.IsNaN(child.Width));

        child.ClearValue(FrameworkElement.FlowDirectionProperty);
        Flush(root);
        Assert.Equal(.8, child.Opacity);
        Assert.Equal(99, child.Width);

        root.ClearValue(FrameworkElement.FlowDirectionProperty);
        Flush(root);
        Assert.Equal(.4, child.Opacity);
        Assert.True(double.IsNaN(child.Width));
    }

    [Fact]
    public void UnknownDirectionIdentifier_IsValidAndNeverMatches()
    {
        var selector = Assert.Single(CssSelectorParser.ParseGroup(":dir(auto)", out _)!);
        var border = new Border { FlowDirection = FlowDirection.RightToLeft };
        Assert.False(CssMatcher.Matches(border, selector, evaluateStates: true));
    }

    [Fact]
    public void NativeStyleAndCurrentLayers_RemainSemanticWhenCssMasksTheirValue()
    {
        var selector = Assert.Single(CssSelectorParser.ParseGroup(":dir(rtl)", out _)!);
        var styled = new Border();
        styled.SetLayerValue(FrameworkElement.FlowDirectionProperty, FlowDirection.RightToLeft,
            DependencyObject.LayerValueSource.StyleSetter);
        Css.SetStyle(styled, "direction:ltr");
        Assert.Equal(FlowDirection.LeftToRight, styled.FlowDirection);
        Assert.True(CssMatcher.Matches(styled, selector, evaluateStates: true));

        var current = new Border();
        current.SetCurrentValue(FrameworkElement.FlowDirectionProperty, FlowDirection.RightToLeft);
        Css.SetStyle(current, "direction:ltr");
        Assert.Equal(FlowDirection.LeftToRight, current.FlowDirection);
        Assert.True(CssMatcher.Matches(current, selector, evaluateStates: true));
    }

    [Fact]
    public void NativeStyleDirectionChanges_ReevaluateDescendantsWhenCssMasksEffectiveValue()
    {
        var child = new Border();
        var root = new StackPanel();
        root.Children.Add(child);
        Css.SetStyleSheet(root, "StackPanel, Border { direction:ltr } Border:dir(rtl) { opacity:.4 }");
        Flush(root);
        Assert.Equal(FlowDirection.LeftToRight, root.FlowDirection);
        Assert.Equal(1, child.Opacity);

        root.SetLayerValue(FrameworkElement.FlowDirectionProperty, FlowDirection.RightToLeft,
            DependencyObject.LayerValueSource.StyleSetter);
        Flush(root);
        Assert.Equal(FlowDirection.LeftToRight, root.FlowDirection);
        Assert.Equal(.4, child.Opacity);

        root.ClearLayerValue(FrameworkElement.FlowDirectionProperty,
            DependencyObject.LayerValueSource.StyleSetter);
        Flush(root);
        Assert.Equal(1, child.Opacity);
    }

    [Fact]
    public void DocumentRun_InheritsNativeDirectionWithoutFollowingCssDirection()
    {
        var run = new Run("text");
        var block = new TextBlock(run) { FontSize = 14, FlowDirection = FlowDirection.RightToLeft };
        Css.SetStyleSheet(block, "Run { direction:ltr } Run:dir(rtl) { font-size:24px }");
        Flush(block);
        Assert.Equal(FlowDirection.LeftToRight, run.FlowDirection);
        Assert.Equal(24, run.FontSize);

        block.FlowDirection = FlowDirection.LeftToRight;
        Flush(block);
        Assert.Equal(14, run.FontSize);
    }

    private static void Flush(FrameworkElement element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
}
