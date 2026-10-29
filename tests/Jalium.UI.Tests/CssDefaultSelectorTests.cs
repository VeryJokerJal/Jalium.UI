using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssDefaultSelectorTests
{
    static CssDefaultSelectorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    [Theory]
    [InlineData(":default", true)]
    [InlineData(":DEFAULT", true)]
    [InlineData(":default()", false)]
    public void SelectorSupport_UsesSimplePseudoClass(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Fact]
    public void Default_UsesButtonConfigurationRatherThanCurrentActivation()
    {
        var selector = Assert.Single(CssSelectorParser.ParseGroup(":default", out _)!);
        var button = new Button();
        Assert.False(CssMatcher.Matches(button, selector, evaluateStates: true));

        button.IsDefault = true;
        Assert.False(button.IsDefaulted);
        Assert.True(CssMatcher.Matches(button, selector, evaluateStates: true));

        button.IsEnabled = false;
        Assert.True(CssMatcher.Matches(button, selector, evaluateStates: true));

        Assert.False(CssMatcher.Matches(new Border(), selector, evaluateStates: true));
        Assert.False(CssMatcher.Matches(new CheckBox(), selector, evaluateStates: true));
    }

    [Fact]
    public void NativeDefaultChange_UpdatesOwnAndDependentStyles()
    {
        var button = new Button();
        var sibling = new Border();
        var root = new StackPanel();
        root.Children.Add(button);
        root.Children.Add(sibling);
        Css.SetStyleSheet(root,
            "Button:default { opacity:.4 } Button:default + Border { opacity:.6 } " +
            "StackPanel:has(> Button:default) { opacity:.8 }");
        Flush(root);
        Assert.Equal(1, button.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, root.Opacity);

        button.IsDefault = true;
        Flush(root);
        Assert.Equal(.4, button.Opacity);
        Assert.Equal(.6, sibling.Opacity);
        Assert.Equal(.8, root.Opacity);

        button.IsEnabled = false;
        Flush(root);
        Assert.Equal(.4, button.Opacity);
        Assert.Equal(.6, sibling.Opacity);

        button.IsDefault = false;
        Flush(root);
        Assert.Equal(1, button.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, root.Opacity);
    }

    private static void Flush(FrameworkElement element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
}
