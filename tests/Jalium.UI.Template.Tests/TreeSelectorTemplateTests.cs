using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Data;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class TreeSelectorTemplateTests
{
    [Fact]
    public void SelectedValuesUseObservableTemplateItemsAndPublishPresentationStates()
    {
        var selector = CreateDefaultSelector(); selector.SelectionMode = SelectionMode.Multiple;
        Assert.True(selector.IsPlaceholderVisible); Assert.Equal("Empty", VisualStateManager.GetCurrentStateName(selector, "SelectionStates"));
        selector.SelectedItems.Add("first"); selector.SelectedItems.Add("second"); Layout(selector);
        var tags = Assert.IsType<ItemsControl>(selector.FindName("PART_TagsPanel"));
        Assert.Same(selector.SelectedItems, tags.ItemsSource); Assert.Equal(2, tags.Items.Count);
        Assert.Equal(new[] { "first", "second" }, Walk(tags).OfType<TreeSelectorSelectionItem>().Select(item => item.Content));
        Assert.True(selector.ShowSelectionTags); Assert.False(selector.ShowSelectionPath); Assert.False(selector.IsPlaceholderVisible);
        Assert.Equal("MultipleSelection", VisualStateManager.GetCurrentStateName(selector, "SelectionStates"));
        selector.IsSearchEnabled = true; Assert.Equal("SearchEnabled", VisualStateManager.GetCurrentStateName(selector, "SearchStates"));
        selector.UnselectAll(); Assert.True(selector.IsPlaceholderVisible); Assert.False(selector.ShowSelectionTags);
    }

    [Fact]
    public void SingleSelectionCollectionChangesDoNotReenterTheObservedTagSource()
    {
        var selector = CreateDefaultSelector();
        var source = new TreeSelector();
        selector.SetBinding(TreeSelector.SelectedItemProperty, new Binding(nameof(TreeSelector.SelectedItem)) { Source = source, Mode = BindingMode.TwoWay });
        selector.SelectedItems.Add("value");
        Assert.Equal("value", selector.SelectedItem); Assert.Equal("value", selector.SelectionPath);
        Assert.Equal("value", source.SelectedItem); Assert.NotNull(BindingOperations.GetBindingBase(selector, TreeSelector.SelectedItemProperty));
        Assert.True(selector.ShowSelectionPath); Assert.False(selector.ShowSelectionTags);
        selector.SelectedItems.Clear();
        Assert.Null(selector.SelectedItem); Assert.True(selector.IsPlaceholderVisible);
        source.SelectedItem = "external"; Assert.Equal("external", Assert.Single(selector.SelectedItems.Cast<object>()));
    }

    [Fact]
    public void RemoveActionUsesTheCurrentTagAndRetiredSelectorTemplatesCannotRemove()
    {
        var selector = CreateDefaultSelector(); selector.SelectionMode = SelectionMode.Multiple;
        selector.SelectedItems.Add("first"); Layout(selector);
        var oldTag = Assert.Single(Walk(selector).OfType<TreeSelectorSelectionItem>());
        var original = selector.Template; var minimal = new ControlTemplate(typeof(TreeSelector)); minimal.SetVisualTree(() => new Border());
        selector.Template = minimal; selector.ApplyTemplate(); selector.Template = original; Layout(selector);
        oldTag.RaiseEvent(new RoutedEventArgs(TreeSelectorSelectionItem.RemoveRequestedEvent, oldTag));
        Assert.Equal("first", Assert.Single(selector.SelectedItems.Cast<object>()));
        var current = Assert.Single(Walk(selector).OfType<TreeSelectorSelectionItem>());
        var remove = Assert.IsType<Button>(current.FindName("PART_RemoveButton"));
        Assert.Equal("Remove selection", Jalium.UI.Automation.AutomationProperties.GetName(remove));
        ((IInvokeProvider)remove.GetAutomationPeer()!).Invoke();
        Assert.Empty(selector.SelectedItems); Assert.True(selector.IsPlaceholderVisible);
    }

    [Fact]
    public void RetiredTagRemoveButtonIsDetachedAndConsumerTemplatesRemainUsable()
    {
        var tag = new TreeSelectorSelectionItem { Content = "value", Style = (Style)Defaults.Value[typeof(TreeSelectorSelectionItem)] };
        tag.ApplyTemplate(); var old = Assert.IsType<Button>(tag.FindName("PART_RemoveButton")); var original = tag.Template;
        int requests = 0; tag.RemoveRequested += (_, _) => requests++;
        var minimal = new ControlTemplate(typeof(TreeSelectorSelectionItem)); minimal.SetVisualTree(() => new Border());
        tag.Template = minimal; tag.ApplyTemplate(); old.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, old)); Assert.Equal(0, requests);
        tag.Template = original; tag.ApplyTemplate(); var current = Assert.IsType<Button>(tag.FindName("PART_RemoveButton"));
        current.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, current)); Assert.Equal(1, requests);
        tag.IsEnabled = false; current.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, current)); Assert.Equal(1, requests);
    }

    [Fact]
    public void ConsumersCanReplaceTheSelectionBoxItemTemplate()
    {
        var selector = CreateDefaultSelector(); selector.SelectionMode = SelectionMode.Multiple;
        var custom = new DataTemplate(); custom.SetVisualTree(() => new TextBlock { Text = "Consumer selection" });
        selector.SelectionBoxItemTemplate = custom; selector.SelectedItems.Add("value"); Layout(selector);
        Assert.Contains(Walk(selector).OfType<TextBlock>(), text => text.Text == "Consumer selection");
        Assert.Empty(Walk(selector).OfType<TreeSelectorSelectionItem>());
        Assert.Equal("value", Assert.Single(selector.SelectedItems.Cast<object>()));
    }

    private static TreeSelector CreateDefaultSelector()
    {
        ThemeManager.RegisterControlThemes(new ResourceDictionary { [typeof(TreeSelectorSelectionItem)] = Defaults.Value[typeof(TreeSelectorSelectionItem)] });
        var selector = new TreeSelector { Style = (Style)Defaults.Value[typeof(TreeSelector)] }; Layout(selector); return selector;
    }

    private static void Layout(UIElement element)
    {
        for (int pass = 0; pass < 3; pass++) { element.Measure(new Size(280,180)); element.Arrange(new Rect(0,0,280,element.DesiredSize.Height)); element.UpdateLayout(); }
    }

    private static IEnumerable<Jalium.UI.Media.Visual> Walk(Jalium.UI.Media.Visual root)
    {
        yield return root;
        for (int index = 0; index < root.VisualChildrenCount; index++) if (root.GetVisualChild(index) is { } child) foreach (var item in Walk(child)) yield return item;
    }
    [Fact]
    public void BranchAndExpansionVisibilityBelongToTheDefaultTemplate()
    {
        var item = new TreeSelectorItem { Header = "root", Style = (Style)Defaults.Value[typeof(TreeSelectorItem)] };
        item.Items.Add(new TreeSelectorItem { Header = "leaf" }); item.ApplyTemplate();
        var expander = Assert.IsType<Grid>(item.FindName("PART_ExpanderBorder"));
        var children = Assert.IsType<ItemsPresenter>(item.FindName("PART_ItemsHost"));
        Assert.Equal(Visibility.Visible, expander.Visibility); Assert.Equal(Visibility.Collapsed, children.Visibility);
        item.IsExpanded = true; Assert.Equal(Visibility.Visible, children.Visibility);
        item.IsExpanded = false; Assert.Equal(Visibility.Collapsed, children.Visibility);
        item.Items.Clear(); Assert.Equal(Visibility.Collapsed, expander.Visibility);
    }
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void CheckBoxesFollowTheOwnerBeforeLoadedAndAfterTemplateReplacement()
    {
        var selector = new TreeSelector { ShowCheckBoxes = true };
        var item = new TreeSelectorItem { Header = "root", Style = (Style)Defaults.Value[typeof(TreeSelectorItem)] };
        selector.Items.Add(item); item.ApplyTemplate();
        var first = Assert.IsType<CheckBox>(item.FindName("PART_CheckBox"));
        Assert.False(item.IsLoaded); Assert.True(item.IsCheckBoxVisible);
        Assert.Equal(Visibility.Visible, first.Visibility);
        selector.ShowCheckBoxes = false; Assert.Equal(Visibility.Collapsed, first.Visibility);
        var original = item.Template; var minimal = new ControlTemplate(typeof(TreeSelectorItem)); minimal.SetVisualTree(() => new Border());
        item.Template = minimal; item.ApplyTemplate(); selector.ShowCheckBoxes = true;
        item.Template = original; item.ApplyTemplate();
        Assert.Equal(Visibility.Visible, Assert.IsType<CheckBox>(item.FindName("PART_CheckBox")).Visibility);
        Assert.Equal(Visibility.Collapsed, first.Visibility);
    }

    [Fact]
    public void RetiredCheckBoxesCannotChangeTheCurrentItem()
    {
        var selector = new TreeSelector { ShowCheckBoxes = true };
        var item = new TreeSelectorItem { Header = "root", Style = (Style)Defaults.Value[typeof(TreeSelectorItem)] };
        selector.Items.Add(item); item.ApplyTemplate();
        var old = Assert.IsType<CheckBox>(item.FindName("PART_CheckBox")); var original = item.Template;
        var minimal = new ControlTemplate(typeof(TreeSelectorItem)); minimal.SetVisualTree(() => new Border());
        item.Template = minimal; item.ApplyTemplate();
        old.IsChecked = true; old.RaiseEvent(new RoutedEventArgs(Jalium.UI.Controls.Primitives.ButtonBase.ClickEvent, old));
        Assert.False(item.IsChecked); Assert.Empty(selector.CheckedItems);
        item.Template = original; item.ApplyTemplate();
        var current = Assert.IsType<CheckBox>(item.FindName("PART_CheckBox")); current.IsChecked = true;
        current.RaiseEvent(new RoutedEventArgs(Jalium.UI.Controls.Primitives.ButtonBase.ClickEvent, current));
        Assert.True(item.IsChecked); Assert.Equal("root", Assert.Single(selector.CheckedItems.Cast<object>()));
    }

    [Fact]
    public void MissingSelectorPartsPreserveSourceSelectionAndCheckState()
    {
        var selector = new TreeSelector { SelectionMode = SelectionMode.Multiple, ShowCheckBoxes = true };
        var item = new TreeSelectorItem { Header = "root" }; selector.Items.Add(item);
        selector.SelectedItems.Add("root"); item.IsChecked = true;
        var minimal = new ControlTemplate(typeof(TreeSelector)); minimal.SetVisualTree(() => new Border());
        selector.Template = minimal; selector.ApplyTemplate(); selector.IsDropDownOpen = true; selector.IsDropDownOpen = false;
        Assert.Same(item, selector.Items[0]); Assert.True(item.IsSelected); Assert.True(item.IsChecked);
        Assert.Equal("root", Assert.Single(selector.SelectedItems.Cast<object>()));
        Assert.Equal("root", Assert.Single(selector.CheckedItems.Cast<object>()));
    }
}
