using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Ribbon;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssOpenSelectorTests
{
    static CssOpenSelectorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    [Theory]
    [InlineData(":open", true)]
    [InlineData(":OPEN", true)]
    [InlineData(":open()", false)]
    public void SelectorSupport_UsesSimplePseudoClass(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Fact]
    public void HostControls_ExposeTheirOwnOpenStateProperty()
    {
        (FrameworkElement Element, string Property)[] controls =
        [
            (new Expander(), "IsExpanded"),
            (new TreeViewItem(), "IsExpanded"),
            (new TreeSelectorItem(), "IsExpanded"),
            (new NavigationViewItem(), "IsExpanded"),
            (new StickyNoteControl(), "IsExpanded"),
            (new ComboBox(), "IsDropDownOpen"),
            (new AutoCompleteBox(), "IsDropDownOpen"),
            (new DatePicker(), "IsDropDownOpen"),
            (new TimePicker(), "IsDropDownOpen"),
            (new TreeSelector(), "IsDropDownOpen"),
            (new Ribbon(), "IsDropDownOpen"),
            (new RibbonMenuButton(), "IsDropDownOpen"),
            (new MenuItem(), "IsSubmenuOpen"),
            (new NavigationView(), "IsPaneOpen"),
            (new Popup(), "IsOpen"),
            (new ContextMenu(), "IsOpen"),
            (new ToolTip(), "IsOpen"),
            (new CommandBar(), "IsOpen"),
            (new InfoBar(), "IsOpen"),
            (new ToastNotificationItem(), "IsOpen"),
        ];
        foreach (var (element, property) in controls)
        {
            Assert.Equal(property, CssOpenState.ObservedProperty(CssNode.Get(element)));
            Assert.NotNull(CssDependencyPropertyLookup.Find(element.GetType(), property));
        }

        Assert.Null(CssOpenState.ObservedProperty(CssNode.Get(new Border())));
        Assert.Null(CssOpenState.ObservedProperty(CssNode.Get(new TextBox())));
    }

    [Fact]
    public void Open_MatchesNativeStateEvenWhenElementIsHidden()
    {
        var selector = Assert.Single(CssSelectorParser.ParseGroup(":open", out _)!);
        var expander = new Expander();
        Assert.False(CssMatcher.Matches(expander, selector, evaluateStates: true));
        expander.IsExpanded = true;
        expander.Visibility = Visibility.Collapsed;
        Assert.True(CssMatcher.Matches(expander, selector, evaluateStates: true));
        expander.IsExpanded = false;
        Assert.False(CssMatcher.Matches(expander, selector, evaluateStates: true));

        var picker = new DatePicker { IsDropDownOpen = true };
        Assert.True(CssMatcher.Matches(picker, selector, evaluateStates: true));
        picker.IsDropDownOpen = false;
        Assert.False(CssMatcher.Matches(picker, selector, evaluateStates: true));

        Assert.False(CssMatcher.Matches(new Border(), selector, evaluateStates: true));
    }

    [Fact]
    public void OpenChanges_UpdateOwnAndDependentStyles()
    {
        var expander = new Expander();
        var sibling = new Border();
        var root = new StackPanel();
        root.Children.Add(expander);
        root.Children.Add(sibling);
        Css.SetStyleSheet(root,
            "Expander:open { opacity:.4 } Expander:open + Border { opacity:.6 } " +
            "StackPanel:has(> Expander:open) { opacity:.8 }");
        Flush(root);
        Assert.Equal(1, expander.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, root.Opacity);

        expander.IsExpanded = true;
        Flush(root);
        Assert.Equal(.4, expander.Opacity);
        Assert.Equal(.6, sibling.Opacity);
        Assert.Equal(.8, root.Opacity);

        expander.IsExpanded = false;
        Flush(root);
        Assert.Equal(1, expander.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, root.Opacity);
    }

    private static void Flush(FrameworkElement element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
}
