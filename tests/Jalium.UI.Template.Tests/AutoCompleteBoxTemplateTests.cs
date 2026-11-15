using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class AutoCompleteBoxTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void EditingUsesTextBoxAndFilteringDoesNotConstructSuggestionVisuals()
    {
        var box = new AutoCompleteBox { ItemsSource = new[] { "apple", "apricot", "banana" }, Text = "ap" };
        Assert.IsAssignableFrom<TextBox>(box);
        Assert.Same(TextBox.TextProperty, AutoCompleteBox.TextProperty);
        Assert.Same(box.FilteredItems, box.GetValue(AutoCompleteBox.FilteredItemsProperty));
        Assert.Equal(new[] { "apple", "apricot" }, box.FilteredItems);
        Assert.True(box.HasSuggestions);
        Assert.Equal(0, box.SelectedSuggestionIndex);
        Assert.Equal(0, VisualTreeHelper.GetChildrenCount(box));
        var template = new ControlTemplate(typeof(AutoCompleteBox));
        template.SetVisualTree(() => new Grid());
        box.Template = template;
        Layout(box);
        box.Text = "ban";
        Assert.Equal("banana", Assert.Single(box.FilteredItems));
        Assert.Null(box.FindName("PART_SuggestionsList"));
        Assert.Equal(1, VisualTreeHelper.GetChildrenCount(box));
    }

    [Fact]
    public void DefaultTemplateProjectsRawItemsAndKeepsTheInputAsTheKeyboardTarget()
    {
        var item = new Item("apple");
        var box = Create(new[] { item, new Item("apricot") });
        box.TextMemberPath = nameof(Item.Name);
        box.Text = "ap";
        var list = List(box);
        Layout(list, 300, 180);
        Assert.Same(box.FilteredItems, list.ItemsSource);
        Assert.Same(item, list.Items[0]);
        Assert.Equal(nameof(Item.Name), list.DisplayMemberPath);
        Assert.Equal(0, list.SelectedIndex);
        var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(item, list.ItemContainerGenerator.ItemFromContainer(row));
        Assert.False(row.Focusable);
        Assert.False(row.IsTabStop);
        Assert.IsType<Border>(row.FindName("SuggestionSurface"));
        Assert.Same(item, Assert.IsType<ContentPresenter>(Find<ContentPresenter>(row)).Content);
        Assert.Equal("apple", Find<TextBlock>(row)!.Text);
    }

    [Fact]
    public void ItemTemplatesCanBeReplacedWithoutReplacingTheInputOrPopup()
    {
        var box = Create();
        box.Text = "ap";
        var list = List(box);
        var popup = box.FindName("PART_Popup");
        var inputHost = box.FindName("PART_ContentHost");
        var itemTemplate = new DataTemplate();
        itemTemplate.SetVisualTree(() => new TextBlock { Text = "Custom suggestion" });
        box.SuggestionItemTemplate = itemTemplate;
        Layout(list, 300, 180);
        Assert.Same(itemTemplate, list.ItemTemplate);
        Assert.Same(popup, box.FindName("PART_Popup"));
        Assert.Same(inputHost, box.FindName("PART_ContentHost"));
        Assert.Equal("Custom suggestion", Find<TextBlock>(list)!.Text);
    }

    [Fact]
    public void AcceptingASuggestionUpdatesBothBindingsAndRaisesOneSelectionEvent()
    {
        var box = Create();
        var model = new Model { Text = "ap" };
        Bind(box, AutoCompleteBox.TextProperty, model, nameof(Model.Text));
        Bind(box, AutoCompleteBox.SelectedItemProperty, model, nameof(Model.Selected));
        var textBinding = BindingOperations.GetBindingExpression(box, AutoCompleteBox.TextProperty);
        var selectedBinding = BindingOperations.GetBindingExpression(box, AutoCompleteBox.SelectedItemProperty);
        var selections = 0;
        box.SelectionChanged += (_, _) => selections++;
        Assert.True(SendKey(box, Key.Down));
        Assert.Equal(1, box.SelectedSuggestionIndex);
        Assert.True(SendKey(box, Key.Enter));
        Assert.Equal("apricot", box.Text);
        Assert.Equal("apricot", model.Text);
        Assert.Equal("apricot", model.Selected);
        Assert.Equal(1, selections);
        Assert.False(box.IsDropDownOpen);
        Assert.Same(textBinding, BindingOperations.GetBindingExpression(box, AutoCompleteBox.TextProperty));
        Assert.Same(selectedBinding, BindingOperations.GetBindingExpression(box, AutoCompleteBox.SelectedItemProperty));
        model.Selected = "banana";
        Assert.Equal("banana", box.Text);
        Assert.Equal("banana", model.Text);
        Assert.Equal(2, selections);
    }

    [Fact]
    public void TypingUndoAndRedoKeepTheDependencyPropertyAndBoundModelInSync()
    {
        var box = Create();
        var model = new Model { Text = "ap" };
        Bind(box, AutoCompleteBox.TextProperty, model, nameof(Model.Text));
        box.CaretIndex = 2;
        Text(box, "p");
        Assert.Equal("app", box.Text);
        Assert.Equal("app", box.GetValue(AutoCompleteBox.TextProperty));
        Assert.Equal("app", model.Text);
        Assert.Equal("apple", Assert.Single(box.FilteredItems));
        box.Undo();
        Assert.Equal("ap", model.Text);
        Assert.Equal(2, box.FilteredItems.Count);
        box.Redo();
        Assert.Equal("app", model.Text);
        Assert.Equal("app", box.Text);
    }

    [Fact]
    public void TextCompletionSelectsOnlyTheSuffixAndTabDoesNotInsertATab()
    {
        var box = Create();
        box.IsTextCompletionEnabled = true;
        box.CaretIndex = 0;
        Text(box, "ap");
        Assert.Equal("apple", box.Text);
        Assert.Equal("apple", box.GetValue(AutoCompleteBox.TextProperty));
        Assert.Equal(2, box.SelectionStart);
        Assert.Equal(3, box.SelectionLength);
        Assert.True(SendKey(box, Key.Tab));
        Text(box, "\t");
        Assert.Equal("apple", box.Text);
        Assert.Equal("apple", box.SelectedItem);
        Assert.False(box.IsDropDownOpen);
    }

    [Fact]
    public void CollectionChangesAndFilterOptionsUpdateExistingTemplateParts()
    {
        var source = new ObservableCollection<string> { "apple", "banana" };
        var box = Create(source);
        box.Text = "ap";
        var list = List(box);
        source.Add("apricot");
        Assert.Equal(2, box.FilteredItems.Count);
        box.FilterMode = AutoCompleteFilterMode.Contains;
        box.Text = "na";
        Assert.Equal("banana", Assert.Single(box.FilteredItems));
        box.ItemFilter = (_, item) => item.ToString()!.StartsWith("ap", StringComparison.Ordinal);
        Assert.Equal(2, box.FilteredItems.Count);
        box.ItemsSource = new[] { "apple" };
        source.Add("applesauce");
        Assert.Single(box.FilteredItems);
        Assert.Same(list, List(box));
        box.ItemFilter = null;
        box.MinimumPrefixLength = 0;
        box.Text = "";
        Assert.Equal("apple", Assert.Single(box.FilteredItems));
    }

    [Fact]
    public void AReentrantPopulatingHandlerCannotOverwriteNewerFilterResults()
    {
        var box = Create();
        var changed = false;
        box.Populating += (_, _) =>
        {
            if (!changed) { changed = true; box.Text = "ban"; }
        };
        box.Text = "ap";
        Assert.Equal("ban", box.Text);
        Assert.Equal("banana", Assert.Single(box.FilteredItems));
    }

    [Fact]
    public void RetemplatingRetiresTheOldItemsSourceAndPreservesLogicalSelection()
    {
        var box = Create();
        box.Text = "ap";
        SendKey(box, Key.Down);
        var oldList = List(box);
        var oldTextHost = Assert.IsType<Grid>(box.FindName("PART_ContentHost"));
        Assert.Single(oldTextHost.Children);
        var template = box.Template;
        box.Template = null;
        Assert.Empty(oldTextHost.Children);
        Assert.Null(oldList.ItemsSource);
        Assert.Null(box.FindName("PART_SuggestionsList"));
        box.Text = "ban";
        Assert.Empty(oldList.Items);
        box.Template = template;
        Layout(box);
        Assert.NotSame(oldList, List(box));
        Assert.Equal("banana", Assert.Single(List(box).Items.Cast<object>()));
        Assert.Equal(0, box.SelectedSuggestionIndex);
        Assert.Equal(0, List(box).SelectedIndex);
    }

    [Fact]
    public void ReadOnlyDisabledAndClosedKeyboardStatesRespectTheTextEditor()
    {
        var box = Create();
        Assert.False(SendKey(box, Key.Enter));
        box.Text = "ap";
        Assert.True(SendKey(box, Key.Escape));
        Assert.False(box.IsDropDownOpen);
        SendKey(box, Key.Down);
        box.IsReadOnly = true;
        SendKey(box, Key.Enter);
        Assert.Equal("ap", box.Text);
        Assert.Null(box.SelectedItem);
        box.IsEnabled = false;
        Assert.False(box.IsDropDownOpen);
        Assert.False(SendKey(box, Key.Down));
    }

    [Fact]
    public void AutomationRetainsTextAndValuePatternsAndAddsExpandCollapse()
    {
        var box = Create();
        box.PlaceholderText = "Search";
        AutomationProperties.SetName(box, "Language");
        box.Text = "ap";
        var peer = new AutoCompleteBoxAutomationPeer(box);
        Assert.Equal("Language", peer.GetName());
        Assert.Equal(nameof(AutoCompleteBox), peer.GetClassName());
        Assert.NotNull(peer.GetPattern(PatternInterface.Text));
        var value = Assert.IsAssignableFrom<IValueProvider>(peer.GetPattern(PatternInterface.Value));
        Assert.Equal("ap", value.Value);
        var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
        expand.Collapse();
        Assert.False(box.IsDropDownOpen);
        expand.Expand();
        Assert.True(box.IsDropDownOpen);
        Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
        box.IsReadOnly = true;
        Assert.Throws<InvalidOperationException>(() => value.SetValue("changed"));
    }

    private static AutoCompleteBox Create(IEnumerable<string>? source = null)
        => Create((System.Collections.IEnumerable)(source ?? new[] { "apple", "apricot", "banana" }));
    private static AutoCompleteBox Create(System.Collections.IEnumerable source)
    {
        var box = new AutoCompleteBox { Width = 320, ItemsSource = source };
        box.Resources.MergedDictionaries.Add(Defaults.Value);
        Layout(box);
        return box;
    }
    private static ListBox List(AutoCompleteBox box) => Assert.IsType<ListBox>(box.FindName("PART_SuggestionsList"));
    private static void Layout(UIElement element, double width = 320, double height = 80)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }
    private static T? Find<T>(Visual visual) where T : Visual
    {
        if (visual is T result) return result;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            if (Find<T>((Visual)VisualTreeHelper.GetChild(visual, index)) is { } found) return found;
        return null;
    }
    private static bool SendKey(UIElement target, Key key)
    {
        var args = (KeyEventArgs)Activator.CreateInstance(typeof(KeyEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { UIElement.KeyDownEvent, key, ModifierKeys.None, true, false, 0 }, null)!;
        target.RaiseEvent(args);
        return args.Handled;
    }
    private static void Text(UIElement target, string text)
    {
        var args = new TextCompositionEventArgs(UIElement.TextInputEvent, text, 0);
        target.RaiseEvent(args);
    }
    private static void Bind(DependencyObject target, DependencyProperty property, object source, string path)
        => BindingOperations.SetBinding(target, property, new Binding(path)
        { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
    private sealed record Item(string Name);
    private sealed class Model : INotifyPropertyChanged
    {
        private string _text = "";
        private object? _selected;
        public string Text { get => _text; set { _text = value; PropertyChanged?.Invoke(this, new(nameof(Text))); } }
        public object? Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
