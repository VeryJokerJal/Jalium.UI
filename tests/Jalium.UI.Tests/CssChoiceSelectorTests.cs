using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Ribbon;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssChoiceSelectorTests
{
    static CssChoiceSelectorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    [Theory]
    [InlineData(":checked", true)]
    [InlineData(":unchecked", true)]
    [InlineData(":UNCHECKED", true)]
    [InlineData(":indeterminate", true)]
    [InlineData(":unchecked()", false)]
    public void SelectorSupport_UsesSimplePseudoClasses(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Fact]
    public void NativeCheckAndSelectionStates_AreMutuallyExclusive()
    {
        var check = new CheckBox();
        AssertState(check, CssChoiceValue.Unchecked);
        check.IsChecked = true;
        AssertState(check, CssChoiceValue.Checked);
        check.IsChecked = null;
        AssertState(check, CssChoiceValue.Indeterminate);

        FrameworkElement[] selectedItems =
        [
            new ListBoxItem(), new TabItem(), new TreeViewItem(), new DataGridRow(),
            new DataGridCell(), new DockItem(), new NavigationViewItem(),
            new CalendarDayButton(), new TreeDataGridRow(), new RibbonTab(),
            new RibbonGalleryItem(),
        ];
        foreach (var item in selectedItems)
        {
            AssertState(item, CssChoiceValue.Unchecked);
            var selectedProperty = CssDependencyPropertyLookup.Find(item.GetType(), "IsSelected");
            Assert.NotNull(selectedProperty);
            item.SetValue(selectedProperty!, true);
            AssertState(item, CssChoiceValue.Checked);
        }

        var treeItem = new TreeSelectorItem { IsSelected = true, IsChecked = false };
        AssertState(treeItem, CssChoiceValue.Unchecked);
        treeItem.IsChecked = null;
        AssertState(treeItem, CssChoiceValue.Indeterminate);
        treeItem.IsChecked = true;
        AssertState(treeItem, CssChoiceValue.Checked);

        AssertState(new Border(), CssChoiceValue.NotApplicable);
        AssertState(new TextBox(), CssChoiceValue.NotApplicable);
    }

    [Fact]
    public void ConditionalCheckControls_AndProgress_UseTheirVisibleNativeState()
    {
        var menu = new MenuItem { IsChecked = true };
        AssertState(menu, CssChoiceValue.NotApplicable);
        menu.IsCheckable = true;
        AssertState(menu, CssChoiceValue.Checked);
        menu.IsChecked = false;
        AssertState(menu, CssChoiceValue.Unchecked);

        var split = new RibbonSplitButton { IsChecked = true };
        AssertState(split, CssChoiceValue.NotApplicable);
        split.IsCheckable = true;
        AssertState(split, CssChoiceValue.Checked);

        var marker = new MarkdownListItemPresenter { IsChecked = true };
        AssertState(marker, CssChoiceValue.NotApplicable);
        marker.MarkerKind = MarkdownListMarkerKind.Task;
        AssertState(marker, CssChoiceValue.Checked);

        var progress = new ProgressBar();
        AssertState(progress, CssChoiceValue.NotApplicable);
        progress.IsIndeterminate = true;
        AssertState(progress, CssChoiceValue.Indeterminate);
    }

    [Fact]
    public void ChoiceChanges_UpdateStylesAndDependentSelectors()
    {
        var check = new CheckBox();
        var item = new ListBoxItem();
        var sibling = new Border();
        var progress = new ProgressBar();
        var root = new StackPanel();
        root.Children.Add(check);
        root.Children.Add(item);
        root.Children.Add(sibling);
        root.Children.Add(progress);
        Css.SetStyleSheet(root,
            "CheckBox:unchecked { opacity:.4 } CheckBox:checked { opacity:.8 } " +
            "CheckBox:indeterminate { opacity:.6 } ListBoxItem:unchecked { opacity:.3 } " +
            "ListBoxItem:checked { opacity:.7 } ListBoxItem:checked + Border { opacity:.5 } " +
            "ProgressBar:indeterminate { opacity:.2 }");
        Flush(root);
        Assert.Equal(.4, check.Opacity);
        Assert.Equal(.3, item.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, progress.Opacity);

        check.IsChecked = true;
        item.IsSelected = true;
        progress.IsIndeterminate = true;
        Flush(root);
        Assert.Equal(.8, check.Opacity);
        Assert.Equal(.7, item.Opacity);
        Assert.Equal(.5, sibling.Opacity);
        Assert.Equal(.2, progress.Opacity);

        check.IsChecked = null;
        item.IsSelected = false;
        progress.IsIndeterminate = false;
        Flush(root);
        Assert.Equal(.6, check.Opacity);
        Assert.Equal(.3, item.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, progress.Opacity);
    }

    [Fact]
    public void CheckabilityAndTaskMarkerChanges_UpdateStyles()
    {
        var menu = new MenuItem { IsChecked = true };
        var task = new MarkdownListItemPresenter { IsChecked = true };
        var root = new StackPanel();
        root.Children.Add(menu);
        root.Children.Add(task);
        Css.SetStyleSheet(root,
            "MenuItem:checked { opacity:.4 } MarkdownListItemPresenter:checked { opacity:.6 }");
        Flush(root);
        Assert.Equal(1, menu.Opacity);
        Assert.Equal(1, task.Opacity);

        menu.IsCheckable = true;
        task.MarkerKind = MarkdownListMarkerKind.Task;
        Flush(root);
        Assert.Equal(.4, menu.Opacity);
        Assert.Equal(.6, task.Opacity);

        menu.IsCheckable = false;
        task.MarkerKind = MarkdownListMarkerKind.Bullet;
        Flush(root);
        Assert.Equal(1, menu.Opacity);
        Assert.Equal(1, task.Opacity);
    }

    private static void AssertState(FrameworkElement element, CssChoiceValue expected)
    {
        Assert.Equal(expected, CssChoiceState.GetState(CssNode.Get(element)));
        foreach (var (selectorText, state) in new[]
        {
            (":checked", CssChoiceValue.Checked),
            (":unchecked", CssChoiceValue.Unchecked),
            (":indeterminate", CssChoiceValue.Indeterminate),
        })
        {
            var selector = Assert.Single(CssSelectorParser.ParseGroup(selectorText, out _)!);
            Assert.Equal(expected == state, CssMatcher.Matches(element, selector, evaluateStates: true));
        }
    }

    private static void Flush(FrameworkElement element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
}
