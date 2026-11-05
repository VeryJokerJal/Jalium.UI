using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class PropertyGridTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DiscoveryBeforeATemplateCreatesOnlyModelsAndOptionalPartsAreSafe()
    {
        var grid = new PropertyGrid { SelectedObject = new Sample(), SearchText = "count" };
        Assert.Equal(0, VisualTreeHelper.GetChildrenCount(grid));
        Assert.Single(grid.VisibleProperties);
        Assert.Equal(nameof(Sample.Count), grid.VisibleProperties[0].Name);
        Assert.DoesNotContain(grid.Properties, item => item.Name == nameof(Sample.Hidden));
        Assert.DoesNotContain(grid.Properties, item => item.Name == "Item");
        var template = new ControlTemplate(typeof(PropertyGrid));
        template.SetVisualTree(() => new Border());
        grid.Template = template;
        Layout(grid);
        grid.SortMode = PropertyGridSortMode.Alphabetical;
        grid.ShowDescription = false;
        grid.SelectedObject = null;
        Assert.True(grid.IsEmpty);
        Assert.Null(grid.FindName("PART_PropertiesList"));
    }

    [Fact]
    public void AConsumerStyleCanOverrideTheRowKeyboardPolicy()
    {
        var row = new PropertyGridRow();
        Assert.True(row.Focusable);
        Assert.False(row.IsTabStop);
        var style = new Style(typeof(PropertyGridRow));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Setters.Add(new Setter(Control.IsTabStopProperty, true));
        row.Style = style;
        Layout(row);
        Assert.False(row.Focusable);
        Assert.True(row.IsTabStop);
    }

    [Fact]
    public void DefaultThemeProjectsCategoriesAndOnlyTheRelevantTypedEditor()
    {
        var grid = Create(new Sample());
        Assert.NotEmpty(grid.Entries.OfType<PropertyGridCategory>());
        Assert.Equal(PropertyGridEditorKind.Text, Editor(grid, nameof(Sample.Count)).EditorKind);
        Assert.IsType<TextBox>(Editor(grid, nameof(Sample.Count)).FindName("PART_TextBox"));
        Assert.IsType<CheckBox>(Editor(grid, nameof(Sample.Enabled)).FindName("PART_ValueControl"));
        Assert.IsType<ComboBox>(Editor(grid, nameof(Sample.Mode)).FindName("PART_ValueControl"));
        Assert.IsType<ColorPicker>(Editor(grid, nameof(Sample.Tint)).FindName("PART_ValueControl"));
        Assert.IsType<Expander>(Editor(grid, nameof(Sample.Child)).FindName("PART_Expander"));
        Assert.Empty(Descendants<ComboBox>(Editor(grid, nameof(Sample.Count))));
        Assert.Equal("Ready", VisualStateManager.GetCurrentStateName(grid, "DataStates"));
    }

    [Fact]
    public void TextValidationEnterAndEscapePreserveTheLastCommittedValueAndRaiseOneChange()
    {
        var model = new Sample();
        var grid = Create(model);
        var editor = Editor(grid, nameof(Sample.Count));
        var text = Assert.IsType<TextBox>(editor.FindName("PART_TextBox"));
        int changed = 0, started = 0, ended = 0;
        grid.PropertyValueChanged += (_, e) => { changed++; Assert.Equal(7, e.OldValue); Assert.Equal(42, e.NewValue); };
        grid.PropertyEditingStarted += (_, _) => started++;
        grid.PropertyEditingEnded += (_, _) => ended++;
        Assert.True(editor.BeginEdit());
        text.Text = "abc";
        Key(text, Jalium.UI.Input.Key.Enter);
        Assert.True(editor.HasError);
        Assert.True(editor.IsEditing);
        Assert.Equal(7, model.Count);
        Assert.Equal("Invalid", VisualStateManager.GetCurrentStateName(editor, "ValidationStates"));
        Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(editor.FindName("PART_ValidationMessage")).Visibility);
        text.Text = "42";
        Key(text, Jalium.UI.Input.Key.Enter);
        Assert.Equal(42, model.Count);
        Assert.False(editor.IsEditing);
        Assert.False(editor.HasError);
        Assert.Equal(1, changed);
        Assert.Equal(1, started);
        Assert.Equal(1, ended);
        editor.BeginEdit(); text.Text = "99";
        Key(text, Jalium.UI.Input.Key.Escape);
        text.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, text));
        Assert.Equal(42, model.Count);
        Assert.Equal("42", text.Text);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void NullableBooleanEnumColorAndNumberEditorsWriteTypedValues()
    {
        var model = new Sample();
        var grid = Create(model);
        var flag = Editor(grid, nameof(Sample.Enabled));
        var check = Assert.IsType<CheckBox>(flag.FindName("PART_ValueControl"));
        Assert.True(check.IsThreeState);
        check.IsChecked = true;
        Assert.True(model.Enabled);
        check.IsChecked = null;
        Assert.Null(model.Enabled);
        var mode = Editor(grid, nameof(Sample.Mode));
        var combo = Assert.IsType<ComboBox>(mode.FindName("PART_ValueControl"));
        combo.SelectedItem = Assert.Single(mode.EnumOptions, item => Equals(item.Value, SampleMode.Second));
        Assert.Equal(SampleMode.Second, model.Mode);
        combo.SelectedItem = Assert.Single(mode.EnumOptions, item => item.Value == null);
        Assert.Null(model.Mode);
        var color = Assert.IsType<ColorPicker>(Editor(grid, nameof(Sample.Tint)).FindName("PART_ValueControl"));
        color.Color = Colors.Red;
        Assert.Equal(Colors.Red, model.Tint);
        var nullable = Editor(grid, nameof(Sample.Optional));
        nullable.BeginEdit(); nullable.EditText = "23";
        Assert.True(nullable.CommitEdit());
        Assert.Equal(23, model.Optional);
        nullable.BeginEdit(); nullable.EditText = "";
        Assert.True(nullable.CommitEdit());
        Assert.Null(model.Optional);
    }

    [Fact]
    public void TypingAfterACommitDoesNotSelectOrOverwriteTheFirstCharacter()
    {
        var grid = Create(new Sample());
        var editor = Editor(grid, nameof(Sample.Name));
        var text = Assert.IsType<TextBox>(editor.FindName("PART_TextBox"));
        Assert.True(text.Focus());
        Assert.True(editor.BeginEdit());
        editor.EditText = "First";
        Assert.True(editor.CommitEdit());
        text.Text = "T";
        text.SelectionStart = 1;
        text.SelectionLength = 0;
        typeof(Jalium.UI.Threading.Dispatcher).GetMethod("ProcessQueue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(editor.Dispatcher, null);
        Assert.True(editor.IsEditing);
        Assert.Equal("T", text.Text);
        Assert.Equal(0, text.SelectionLength);
        Assert.Equal(1, text.SelectionStart);
        text.Text += "emplated value";
        Assert.True(editor.CommitEdit());
        Assert.Equal("Templated value", editor.Property!.Value);
    }

    [Fact]
    public void EditingASpaceOrEnterDoesNotToggleTheEnclosingCategory()
    {
        var grid = Create(new Sample());
        var category = Assert.Single(Descendants<Expander>(grid), item => Equals(item.Header, "General"));
        Assert.True(category.IsExpanded);
        var text = Assert.IsType<TextBox>(Editor(grid, nameof(Sample.Name)).FindName("PART_TextBox"));
        Key(text, Jalium.UI.Input.Key.Space);
        Assert.True(category.IsExpanded);
        Key(text, Jalium.UI.Input.Key.Enter);
        Assert.True(category.IsExpanded);
        Key(category, Jalium.UI.Input.Key.Space);
        Assert.False(category.IsExpanded);
    }

    [Theory]
    [InlineData(ThemeVariant.Light)]
    [InlineData(ThemeVariant.Dark)]
    public void ValidationTextRetainsNormalTextContrastOnTheSelectedRow(ThemeVariant theme)
    {
        var previous = ThemeManager.CurrentTheme;
        try
        {
            ThemeManager.ApplyTheme(theme);
            var grid = Create(new Sample());
            var row = Row(grid, nameof(Sample.Count));
            var editor = Editor(grid, nameof(Sample.Count));
            editor.BeginEdit(); editor.EditText = "invalid";
            Assert.False(editor.CommitEdit());
            var message = Assert.IsType<TextBlock>(editor.FindName("PART_ValidationMessage"));
            var foreground = Assert.IsType<SolidColorBrush>(message.Foreground).Color;
            var background = Assert.IsType<SolidColorBrush>(Assert.IsType<Border>(row.FindName("PART_Root")).Background).Color;
            var lighter = Math.Max(Luminance(foreground), Luminance(background));
            var darker = Math.Min(Luminance(foreground), Luminance(background));
            Assert.True((lighter + .05) / (darker + .05) >= 4.5, $"{theme}: {foreground} on {background}");
            editor.CancelEdit();
        }
        finally { ThemeManager.ApplyTheme(previous); }
    }

    [Fact]
    public void SourceNotificationsRefreshValuesWithoutRebuildingRowsOrDuplicatingNotifications()
    {
        var model = new Sample();
        var grid = Create(model);
        var row = Row(grid, nameof(Sample.Count));
        var item = row.Property!;
        var editor = Editor(grid, nameof(Sample.Count));
        int notifications = 0;
        item.PropertyChanged += (_, _) => notifications++;
        model.Count = 12;
        Assert.Equal("12", editor.EditText);
        Assert.Same(row, Row(grid, nameof(Sample.Count)));
        Assert.Equal(1, notifications);
        editor.BeginEdit(); editor.EditText = "22";
        Assert.True(editor.CommitEdit());
        Assert.Equal(22, model.Count);
        Assert.Equal(2, notifications);
        editor.BeginEdit(); editor.EditText = "pending";
        model.Count = 13;
        Assert.Equal("pending", editor.EditText);
        editor.CancelEdit();
        Assert.Equal("13", editor.EditText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointerOnAnEnumToggleMovesKeyboardInputFromThePreviousEditor(bool touch)
    {
        var model = new Sample();
        var grid = Create(model);
        var text = Assert.IsType<TextBox>(Editor(grid, nameof(Sample.Name)).FindName("PART_TextBox"));
        Assert.True(text.Focus());
        var combo = Assert.IsType<ComboBox>(Editor(grid, nameof(Sample.Mode)).FindName("PART_ValueControl"));
        var toggle = Assert.IsType<ToggleButton>(combo.FindName("PART_ToggleButton"));
        Assert.False(toggle.Focusable);
        if (touch) toggle.RaiseEvent(new RoutedEventArgs(UIElement.TouchDownEvent, toggle) { Handled = true });
        else toggle.RaiseEvent(new MouseButtonEventArgs(UIElement.MouseDownEvent, new Point(1, 1), MouseButton.Left,
            MouseButtonState.Pressed, 1, MouseButtonState.Pressed, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 1) { Source = toggle, Handled = true });
        Assert.True(combo.IsKeyboardFocusWithin);
        Assert.Same(combo, Keyboard.FocusedElement);
        combo.IsDropDownOpen = true;
        Key(Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement), Jalium.UI.Input.Key.Down);
        Key(Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement), Jalium.UI.Input.Key.Enter);
        Assert.Equal(SampleMode.Second, model.Mode);
        Assert.False(combo.IsDropDownOpen);
        Assert.NotNull(combo.GetBindingExpression(Selector.SelectedItemProperty));
    }

    [Fact]
    public void ReadOnlyChangesCancelPendingEditsAndPrivateSettersRemainReadOnly()
    {
        var model = new Sample();
        var grid = Create(model);
        var editor = Editor(grid, nameof(Sample.Count));
        Assert.False(Editor(grid, nameof(Sample.PrivateSetter)).CanEdit);
        Assert.False(Editor(grid, nameof(Sample.ReadOnly)).CanEdit);
        editor.BeginEdit(); editor.EditText = "99";
        grid.IsReadOnly = true;
        Assert.False(editor.CanEdit);
        Assert.False(editor.IsEditing);
        Assert.False(editor.CommitEdit());
        Assert.Equal(7, model.Count);
        Assert.True(Assert.IsType<TextBox>(editor.FindName("PART_TextBox")).IsReadOnly);
        Assert.False(Assert.IsType<CheckBox>(Editor(grid, nameof(Sample.Enabled)).FindName("PART_ValueControl")).IsEnabled);
        grid.IsReadOnly = false;
        Assert.True(editor.CanEdit);
        grid.IsEnabled = false;
        Assert.False(editor.BeginEdit());
    }

    [Fact]
    public void ToolbarFilterAndSelectionKeepTwoWayBindingsAndCategoryExpansion()
    {
        var state = new GridState();
        var grid = Create(new Sample());
        grid.SetBinding(PropertyGrid.SearchTextProperty, new Binding(nameof(GridState.Query)) { Source = state });
        grid.SetBinding(PropertyGrid.SortModeProperty, new Binding(nameof(GridState.Sort)) { Source = state });
        grid.SetBinding(PropertyGrid.SelectedPropertyProperty, new Binding(nameof(GridState.Selected)) { Source = state });
        var category = Assert.Single(grid.Entries.OfType<PropertyGridCategory>(), item => item.Name == "General");
        category.IsExpanded = false;
        Click(grid, "PART_AlphabeticalButton"); Layout(grid);
        Assert.Equal(PropertyGridSortMode.Alphabetical, state.Sort);
        Assert.All(grid.Entries, item => Assert.IsType<PropertyItem>(item));
        var row = Row(grid, nameof(Sample.Count));
        row.RaiseEvent(new MouseButtonEventArgs(UIElement.MouseDownEvent, new Point(1, 1), MouseButton.Left,
            MouseButtonState.Pressed, 1, MouseButtonState.Pressed, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 1) { Source = row });
        Assert.Same(row.Property, state.Selected);
        Assert.Equal("Item count", grid.SelectedPropertyDescription);
        Assert.Equal("DescriptionVisible", VisualStateManager.GetCurrentStateName(grid, "DescriptionStates"));
        Click(grid, "PART_CategorizedButton"); Layout(grid);
        Assert.False(category.IsExpanded);
        var search = Assert.IsType<TextBox>(grid.FindName("PART_SearchTextBox"));
        search.Text = "mode"; Layout(grid);
        Assert.Equal("mode", state.Query);
        Assert.Single(grid.VisibleProperties);
        Assert.Null(state.Selected);
        grid.PropertyFilter = item => item.Name != nameof(Sample.Mode);
        Layout(grid);
        Assert.True(grid.IsEmpty);
        Assert.Equal("Empty", VisualStateManager.GetCurrentStateName(grid, "DataStates"));
        Assert.NotNull(grid.GetBindingExpression(PropertyGrid.SelectedPropertyProperty));
        Assert.NotNull(grid.GetBindingExpression(PropertyGrid.SortModeProperty));
        Assert.NotNull(grid.GetBindingExpression(PropertyGrid.SearchTextProperty));
    }

    [Fact]
    public void TemplateReplacementRetiresOldPartsAndRestoresPendingText()
    {
        var model = new Sample();
        var grid = Create(model);
        var editor = Editor(grid, nameof(Sample.Count));
        var text = Assert.IsType<TextBox>(editor.FindName("PART_TextBox"));
        var list = Assert.IsType<ItemsControl>(grid.FindName("PART_PropertiesList"));
        var search = Assert.IsType<TextBox>(grid.FindName("PART_SearchTextBox"));
        var alphabetic = Assert.IsAssignableFrom<ButtonBase>(grid.FindName("PART_AlphabeticalButton"));
        editor.BeginEdit(); text.Text = "55";
        var template = grid.Template;
        grid.Template = null;
        Assert.Null(list.ItemsSource);
        Assert.False(editor.IsEditing);
        search.Text = "old";
        alphabetic.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, alphabetic));
        text.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, text));
        Assert.Equal(7, model.Count);
        Assert.Equal("", grid.SearchText);
        Assert.Equal(PropertyGridSortMode.Categorized, grid.SortMode);
        grid.Template = template;
        Layout(grid);
        var replacement = Editor(grid, nameof(Sample.Count));
        Assert.NotSame(editor, replacement);
        Assert.True(replacement.IsEditing);
        Assert.Equal("55", replacement.EditText);
        Assert.True(replacement.CommitEdit());
        Assert.Equal(55, model.Count);
        text.Text = "99";
        Assert.False(editor.CommitEdit());
        Assert.Equal(55, model.Count);
    }

    [Fact]
    public void AnEditorTemplateReplacementDisconnectsOldTwoWayParts()
    {
        var model = new Sample();
        var grid = Create(model);
        var editor = Editor(grid, nameof(Sample.Enabled));
        var old = Assert.IsType<CheckBox>(editor.FindName("PART_ValueControl"));
        var template = editor.Template;
        editor.Template = null;
        old.IsChecked = true;
        Assert.Null(model.Enabled);
        editor.Template = template;
        Layout(grid);
        var replacement = Assert.IsType<CheckBox>(editor.FindName("PART_ValueControl"));
        replacement.IsChecked = false;
        Assert.False(model.Enabled);
    }

    [Fact]
    public void AppearanceAndColumnChangesKeepPendingRowsAndCompactLayoutStacksTheEditor()
    {
        var grid = Create(new Sample());
        var row = Row(grid, nameof(Sample.Count));
        var editor = Editor(grid, nameof(Sample.Count));
        editor.BeginEdit(); editor.EditText = "33";
        grid.PropertyNameForeground = Brushes.Red;
        grid.NameColumnWidth = 180;
        Layout(grid, 260);
        Assert.Same(row, Row(grid, nameof(Sample.Count)));
        Assert.Same(editor, Editor(grid, nameof(Sample.Count)));
        Assert.True(editor.IsEditing);
        Assert.Equal("33", editor.EditText);
        Assert.Equal("Compact", VisualStateManager.GetCurrentStateName(row, "LayoutStates"));
        Assert.Equal(1, Grid.GetRow(editor));
        Assert.Equal(1, Grid.GetColumn(editor));
        Assert.Same(Brushes.Red, row.NameForeground);
        Layout(grid, 600);
        Assert.Equal("Wide", VisualStateManager.GetCurrentStateName(row, "LayoutStates"));
        Assert.Equal(0, Grid.GetRow(editor));
        Assert.Equal(2, Grid.GetColumn(editor));
        editor.CancelEdit();
    }

    [Fact]
    public void AThrowingGetterOrSetterShowsAnErrorAndCoercedValuesRefresh()
    {
        var model = new Fallible();
        var grid = Create(model);
        var getter = Editor(grid, nameof(Fallible.Unavailable));
        Assert.True(getter.HasError);
        Assert.False(getter.CanEdit);
        Assert.Contains("unavailable", getter.ErrorText);
        var setter = Editor(grid, nameof(Fallible.Count));
        setter.BeginEdit(); setter.EditText = "-1";
        Assert.False(setter.CommitEdit());
        Assert.Equal(5, model.Count);
        Assert.True(setter.HasError);
        setter.EditText = "999";
        Assert.True(setter.CommitEdit());
        Assert.Equal(10, model.Count);
        Assert.Equal("10", setter.EditText);
    }

    [Fact]
    public void ExpandableObjectsAreLazyAndCyclesDoNotBuildAnUnboundedTree()
    {
        var child = new Child();
        child.Next = child;
        var grid = Create(new Sample { Child = child });
        var editor = Editor(grid, nameof(Sample.Child));
        Assert.Empty(editor.SubProperties);
        editor.IsExpanded = true;
        Layout(grid);
        Assert.NotEmpty(editor.SubProperties);
        var number = Assert.Single(editor.SubProperties, item => item.Name == nameof(Child.Number));
        var nested = Assert.Single(Descendants<PropertyGridEditor>(editor), item => ReferenceEquals(item.Property, number));
        nested.BeginEdit(); nested.EditText = "12";
        Assert.True(nested.CommitEdit());
        Assert.Equal(12, child.Number);
        var cycle = Assert.Single(Descendants<PropertyGridEditor>(editor), item => item.Property?.Name == nameof(Child.Next));
        cycle.IsExpanded = true;
        Assert.Empty(cycle.SubProperties);
    }

    [Fact]
    public void CustomEditorFactoriesAndDataTemplatesAreReplaceableAndPerInstance()
    {
        var grid = Create(new Sample());
        grid.RegisterCustomEditor(typeof(string), (item, _) => new TextBlock { Text = "Custom: " + item.Value });
        Layout(grid);
        var editor = Editor(grid, nameof(Sample.Name));
        Assert.Equal(PropertyGridEditorKind.Custom, editor.EditorKind);
        Assert.Contains(Descendants<TextBlock>(editor), text => text.Text == "Custom: Initial");
        var template = new DataTemplate();
        template.SetVisualTree(() => new TextBlock { Text = "Template editor" });
        grid.EditorTemplate = template;
        Layout(grid);
        Assert.Same(template, editor.CustomTemplate);
        Assert.Same(editor.Property, editor.CustomContent);
        Assert.Contains(Descendants<TextBlock>(editor), text => text.Text == "Template editor");
        grid.EditorTemplate = null;
        Layout(grid);
        Assert.Null(editor.CustomTemplate);
        Assert.Contains(Descendants<TextBlock>(editor), text => text.Text == "Custom: Initial");
    }

    [Fact]
    public void NestedStructEditsWriteBackToTheParentAndReadOnlyStructsStayUnchanged()
    {
        var model = new StructSource();
        var grid = Create(model);
        var parent = Editor(grid, nameof(StructSource.Position));
        parent.IsExpanded = true;
        Layout(grid);
        var nested = Assert.Single(Descendants<PropertyGridEditor>(parent), item => item.Property?.Name == nameof(Position.X));
        Assert.True(nested.BeginEdit());
        nested.EditText = "19";
        Assert.True(nested.CommitEdit());
        Assert.Equal(19, model.Position.X);
        var readOnly = Editor(grid, nameof(StructSource.ReadOnlyPosition));
        readOnly.IsExpanded = true;
        Layout(grid);
        var locked = Assert.Single(Descendants<PropertyGridEditor>(readOnly), item => item.Property?.Name == nameof(Position.X));
        Assert.False(locked.CanEdit);
        Assert.False(locked.BeginEdit());
        Assert.Equal(3, model.ReadOnlyPosition.X);
    }

    [Fact]
    public void ReplacingTheInspectedObjectMakesRetainedEditorsInert()
    {
        var old = new Sample();
        var grid = Create(old);
        var editor = Editor(grid, nameof(Sample.Count));
        editor.BeginEdit(); editor.EditText = "99";
        var replacement = new Sample { Count = 2 };
        grid.SelectedObject = replacement;
        Layout(grid);
        Assert.False(editor.CommitEdit());
        editor.BooleanValue = false;
        Assert.Equal(7, old.Count);
        Assert.Equal(2, replacement.Count);
        Assert.NotSame(editor, Editor(grid, nameof(Sample.Count)));
    }

    [Fact]
    public void RowAndEditorTemplatesPreservePendingInputAndUseIndependentVisualInstances()
    {
        var grid = Create(new Sample());
        var row = Row(grid, nameof(Sample.Count));
        var editor = Editor(grid, nameof(Sample.Count));
        editor.BeginEdit(); editor.EditText = "44";
        var editorTemplate = editor.Template;
        var oldText = editor.FindName("PART_TextBox");
        editor.Template = null;
        editor.Template = editorTemplate;
        Layout(grid);
        Assert.True(editor.IsEditing);
        Assert.Equal("44", editor.EditText);
        Assert.NotSame(oldText, editor.FindName("PART_TextBox"));
        var rowTemplate = row.Template;
        row.Template = null;
        row.Template = rowTemplate;
        Layout(grid);
        var replacement = Editor(grid, nameof(Sample.Count));
        Assert.NotSame(editor, replacement);
        Assert.True(replacement.IsEditing);
        Assert.Equal("44", replacement.EditText);
        var second = Create(new Sample());
        Assert.NotSame(row.FindName("PART_Root"), Row(second, nameof(Sample.Count)).FindName("PART_Root"));
        Assert.True(replacement.CommitEdit());
    }

    private static PropertyGrid Create(object model)
    {
        // Explicit editor/search appearance styles still use TextBox's default
        // template layer when this fixture runs without an Application.
        ThemeManager.RegisterControlThemes(new ResourceDictionary { [typeof(TextBox)] = Defaults.Value[typeof(TextBox)] });
        var grid = new PropertyGrid { SelectedObject = model };
        grid.Resources.MergedDictionaries.Add(Defaults.Value);
        Layout(grid);
        Assert.NotNull(grid.PropertyTemplate);
        return grid;
    }
    private static void Layout(FrameworkElement control, double width = 600)
    {
        control.Measure(new Size(width, 700));
        control.Arrange(new Rect(0, 0, width, 700));
        control.UpdateLayout();
    }
    private static PropertyGridRow Row(PropertyGrid grid, string name) => Assert.Single(Descendants<PropertyGridRow>(grid), item => item.Property?.Name == name && item.Property.ParentProperty == null);
    private static PropertyGridEditor Editor(PropertyGrid grid, string name) => Assert.IsType<PropertyGridEditor>(Row(grid, name).FindName("PART_Editor"));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i)!;
            if (child is T target) yield return target;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Key(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        new Source { RootVisual = target }, 1, key) { RoutedEvent = UIElement.KeyDownEvent, Source = target });
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
    private static void Click(PropertyGrid grid, string name)
    {
        var button = Assert.IsAssignableFrom<ButtonBase>(grid.FindName(name));
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }
    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            double v = value / 255.0;
            return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    private enum SampleMode { First, Second }
    private struct Position { public int X { get; set; } }
    private sealed class StructSource
    {
        public Position Position { get; set; } = new() { X = 1 };
        public Position ReadOnlyPosition { get; } = new() { X = 3 };
    }
    private sealed class Child
    {
        public int Number { get; set; } = 1;
        public Child? Next { get; set; }
    }
    private sealed class Sample : INotifyPropertyChanged
    {
        private int _count = 7;
        [Category("General"), Description("Item count")]
        public int Count { get => _count; set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); } }
        [Category("General")]
        public string Name { get; set; } = "Initial";
        public int? Optional { get; set; }
        public bool? Enabled { get; set; }
        public SampleMode? Mode { get; set; } = SampleMode.First;
        public Color Tint { get; set; } = Colors.Blue;
        public Child Child { get; set; } = new();
        public int PrivateSetter { get; private set; } = 8;
        [ReadOnly(true)] public string ReadOnly { get; set; } = "unchanged";
        [Browsable(false)] public string Hidden { get; set; } = "hidden";
        public string this[int index] { get => "index"; set { } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private sealed class Fallible
    {
        private int _count = 5;
        public object Unavailable { get => throw new InvalidOperationException("unavailable getter"); set { } }
        public int Count { get => _count; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); _count = Math.Min(10, value); } }
    }
    private sealed class GridState : INotifyPropertyChanged
    {
        private string _query = "";
        private PropertyGridSortMode _sort;
        private PropertyItem? _selected;
        public string Query { get => _query; set { _query = value; PropertyChanged?.Invoke(this, new(nameof(Query))); } }
        public PropertyGridSortMode Sort { get => _sort; set { _sort = value; PropertyChanged?.Invoke(this, new(nameof(Sort))); } }
        public PropertyItem? Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
