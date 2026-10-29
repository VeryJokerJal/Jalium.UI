using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssAdvancedSelectorTests
{
    static CssAdvancedSelectorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    private static StackPanel Make(params FrameworkElement[] elements)
    { var panel = new StackPanel(); foreach (var element in elements) panel.Children.Add(element); return panel; }
    private static void Flush(FrameworkElement element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);

    [Theory]
    [InlineData("[status]")]
    [InlineData("[status='Ready file']")]
    [InlineData("[status^='Ready']")]
    [InlineData("[status$='file']")]
    [InlineData("[status*='dy fi']")]
    [InlineData("[status~='file']")]
    [InlineData("[status='ready FILE' i]")]
    public void AttributeSelectors_MatchAndRespondToCollectionUpdates(string selector)
    {
        var child = new Border(); var root = Make(child);
        Css.SetAttribute(child, "status", "Ready file");
        Css.SetStyleSheet(root, selector + " { opacity:.4 }");
        Flush(root);
        Assert.Equal(.4, child.Opacity);
        Css.GetAttributes(child).Remove("status");
        Flush(root);
        Assert.Equal(1, child.Opacity);
    }

    [Fact]
    public void SiblingSelectors_RespondToClassAndTreeChanges()
    {
        var first = new Border(); var second = new Border(); var third = new Border();
        var root = Make(first, second, third);
        Css.SetClass(first, "selected");
        Css.SetStyleSheet(root, ".selected + Border { opacity:.5 } .selected ~ Border { width:30px }");
        Flush(root);
        Assert.Equal(.5, second.Opacity); Assert.Equal(1, third.Opacity);
        Assert.Equal(30, second.Width); Assert.Equal(30, third.Width);
        Css.SetClass(first, ""); Flush(root);
        Assert.Equal(1, second.Opacity); Assert.True(double.IsNaN(third.Width));
    }

    [Fact]
    public void NthSelectors_RecountAfterInsertionAndRemoval()
    {
        var first = new Border(); var second = new Border(); var third = new Border();
        var root = Make(first, second, third);
        Css.SetStyleSheet(root, "Border:nth-child(2n + 1) { opacity:.5 } Border:last-child { width:30px }");
        Flush(root);
        Assert.Equal(.5, first.Opacity); Assert.Equal(1, second.Opacity); Assert.Equal(.5, third.Opacity);
        root.Children.Remove(first); Flush(root);
        Assert.Equal(.5, second.Opacity); Assert.Equal(1, third.Opacity);
        Assert.Equal(30, third.Width);
    }

    [Fact]
    public void Functions_HaveCorrectSpecificityAndNestedCommaHandling()
    {
        var child = new Border { Name = "hero" }; var root = Make(child);
        Css.SetClass(child, "card");
        Css.SetStyleSheet(root, "Border { opacity:.8 } :where(#hero, .card) { opacity:.2 } :is(#hero, .other) { width:30px } .card { width:60px } :not(.off, .hidden) { height:20px }");
        Flush(root);
        Assert.Equal(.8, child.Opacity); Assert.Equal(30, child.Width); Assert.Equal(20, child.Height);
        Css.SetClass(child, "card off"); Flush(root);
        Assert.True(double.IsNaN(child.Height));
    }

    [Fact]
    public void Has_RelativeChildAndFollowingSiblingSelectors()
    {
        var child = new Border(); var container = Make(child); var sibling = new Border();
        var root = Make(container, sibling);
        Css.SetClass(child, "error"); Css.SetClass(sibling, "next");
        Css.SetStyleSheet(root, "StackPanel:has(> .error) { opacity:.5 } StackPanel:has(+ .next) { width:100px }");
        Flush(root);
        Assert.Equal(.5, container.Opacity); Assert.Equal(1, root.Opacity); Assert.Equal(100, container.Width);
        Css.SetClass(child, ""); Css.SetClass(sibling, ""); Flush(root);
        Assert.Equal(1, container.Opacity); Assert.True(double.IsNaN(container.Width));
    }

    [Fact]
    public void NotWithDynamicState_IsTrackedEvenWhenInitiallyUnmatched()
    {
        var child = new Border { IsEnabled = false }; var root = Make(child);
        Css.SetStyleSheet(root, "Border:not(:disabled) { opacity:.5 }");
        Flush(root); Assert.Equal(1, child.Opacity);
        child.IsEnabled = true; Flush(root); Assert.Equal(.5, child.Opacity);
    }

    [Fact]
    public void NthChildOfSelectorList_CountsOnlyMatchingSiblings()
    {
        var a = new Border(); var b = new Border(); var c = new Border();
        var root = Make(a, b, c); Css.SetClass(a, "item"); Css.SetClass(c, "item");
        Css.SetStyleSheet(root, "Border:nth-child(2 of .item) { opacity:.5 }");
        Flush(root);
        Assert.Equal(1, a.Opacity); Assert.Equal(1, b.Opacity); Assert.Equal(.5, c.Opacity);
    }

    [Fact]
    public void EscapedClassNamesAndAttributeStrings_PreserveCssTokenSemantics()
    {
        var child = new Border(); var root = Make(child);
        Css.SetClass(child, "w-1/2"); Css.SetAttribute(child, "state", "ready");
        Css.SetStyleSheet(root, @".w-1\/2[state='\72 eady']:n\6f t(.hidden) {opacity:.4}");
        Flush(root); Assert.Equal(.4, child.Opacity);
    }
}
