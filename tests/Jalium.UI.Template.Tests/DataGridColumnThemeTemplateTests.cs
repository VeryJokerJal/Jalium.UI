using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jalium.UI.Input;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DataGridColumnThemeTemplateTests
{
    [Fact]
    public void DefaultStylesAreSealedHaveTheCorrectTargetsAndDoNotCreateAnApplication()
    {
        var application = Application.Current;
        (Style Style, Type Target)[] defaults =
        [
            (DataGridTextColumn.DefaultElementStyle, typeof(TextBlock)),
            (DataGridTextColumn.DefaultEditingElementStyle, typeof(TextBox)),
            (DataGridCheckBoxColumn.DefaultElementStyle, typeof(CheckBox)),
            (DataGridCheckBoxColumn.DefaultEditingElementStyle, typeof(CheckBox)),
            (DataGridComboBoxColumn.DefaultElementStyle, typeof(ComboBox)),
            (DataGridComboBoxColumn.DefaultEditingElementStyle, typeof(ComboBox)),
            (DataGridHyperlinkColumn.DefaultElementStyle, typeof(HyperlinkButton)),
            (DataGridHyperlinkColumn.DefaultEditingElementStyle, typeof(TextBox))
        ];
        foreach (var (style, target) in defaults)
        {
            Assert.Equal(target, style.TargetType);
            Assert.True(style.IsSealed);
        }
        Assert.Same(DataGridTextColumn.DefaultElementStyle, DataGridTextColumn.DefaultElementStyle);
        Assert.Same(application, Application.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryColumnTypeCreatesIndependentThemedContentAndConsumerStylesWin(bool tree)
    {
        WithGrid(tree, (grid, columns, items) =>
        {
            var rows = Rows(grid);
            var first = Cells(rows[0]);
            var second = Cells(rows[1]);
            Assert.Equal(4, first.Length);
            var text = Find<TextBlock>(first[0]).Single(candidate => candidate.Text == "Alpha");
            var check = Assert.Single(Find<CheckBox>(first[1]));
            var combo = Assert.Single(Find<ComboBox>(first[2]));
            var link = Assert.Single(Find<HyperlinkButton>(first[3]));
            Assert.True(check.IsChecked);
            Assert.False(check.IsEnabled);
            Assert.False(check.Focusable);
            Assert.False(combo.Focusable);
            Assert.False(combo.IsHitTestVisible);
            Assert.Equal(0, combo.MinWidth);
            Assert.Equal("Engineering", combo.Text);
            Assert.Contains(Find<TextBlock>(combo), label => label.Text == "Engineering");
            Assert.Equal("Alpha", link.Content);
            Assert.Equal(items[0].Address, link.NavigateUri);
            Assert.Same(DataGridHyperlinkColumn.DefaultElementStyle, link.Style);
            Assert.NotSame(check, Assert.Single(Find<CheckBox>(second[1])));
            Assert.NotSame(combo, Assert.Single(Find<ComboBox>(second[2])));
            Assert.NotSame(link, Assert.Single(Find<HyperlinkButton>(second[3])));
            Assert.Equal("Beta", Find<TextBlock>(second[0]).Single(candidate => candidate.Text == "Beta").Text);

            var foreground = new SolidColorBrush(Color.FromRgb(160, 40, 140));
            grid.Foreground = foreground;
            grid.FontSize = 19;
            Layout(grid);
            text = Find<TextBlock>(Cells(Rows(grid)[0])[0]).Single(candidate => candidate.Text == "Alpha");
            Assert.Same(foreground, text.Foreground);
            Assert.Equal(19, text.FontSize);
            var custom = new Style(typeof(TextBlock)) { BasedOn = DataGridTextColumn.DefaultElementStyle };
            var customBrush = new SolidColorBrush(Color.FromRgb(10, 130, 80));
            custom.Setters.Add(new Setter(TextBlock.ForegroundProperty, customBrush));
            Assert.IsType<DataGridTextColumn>(columns[0]).ElementStyle = custom;
            Layout(grid);
            Assert.Same(customBrush, Find<TextBlock>(Cells(Rows(grid)[0])[0]).Single(candidate => candidate.Text == "Alpha").Foreground);
            ThemeManager.ApplyTheme(ThemeVariant.Dark);
            Layout(grid);
            Assert.Same(customBrush, Find<TextBlock>(Cells(Rows(grid)[0])[0]).Single(candidate => candidate.Text == "Alpha").Foreground);
        });
    }

    [Fact]
    public void EditingTemplatesKeepNativeEditorTypesAndFitInsideShortRows()
    {
        WithGrid(false, (control, columns, items) =>
        {
            var grid = Assert.IsType<DataGrid>(control);
            var cells = Cells(Rows(grid)[0]);
            for (var index = 0; index < cells.Length; ++index)
            {
                var cell = cells[index];
                cell.IsEditing = true;
                Layout(grid);
                var editor = Assert.IsAssignableFrom<FrameworkElement>(cell.Content);
                var expected = index switch { 1 => typeof(CheckBox), 2 => typeof(ComboBox), _ => typeof(TextBox) };
                Assert.Equal(expected, editor.GetType());
                Assert.InRange(editor.ActualHeight, 1, cell.ActualHeight);
                Assert.Equal(0, editor.MinWidth);
                if (editor is ComboBox combo)
                {
                    Assert.True(combo.Focusable);
                    Assert.Equal("Engineering", combo.Text);
                    Assert.Contains(Find<TextBlock>(combo), label => label.Text == "Engineering");
                }
                if (editor is CheckBox check) Assert.True(check.IsEnabled);
                cell.IsEditing = false;
                Layout(grid);
            }
            Assert.Equal("Alpha", items[0].Name);
            Assert.Equal("eng", items[0].Department);
        });
    }

    [Fact]
    public void ThemedEditorsCommitValuesUsingTheBoundPropertyTypes()
    {
        WithGrid(false, (grid, columns, items) =>
        {
            var cells = Cells(Rows(grid)[0]);
            object[] values = ["Updated", false, "ops", "https://example.invalid/updated"];
            for (var index = 0; index < cells.Length; ++index)
            {
                var cell = cells[index];
                cell.IsEditing = true;
                Layout(grid);
                var editor = Assert.IsAssignableFrom<FrameworkElement>(cell.Content);
                if (editor is TextBox text) text.Text = (string)values[index];
                else if (editor is CheckBox check) check.IsChecked = (bool)values[index];
                else if (editor is ComboBox combo) combo.SelectedValue = values[index];
                var committed = typeof(DataGridColumn).GetMethod("CommitCellEditInternal", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(columns[index], [editor, items[0]]);
                Assert.Equal(true, committed);
                cell.IsEditing = false;
            }
            Assert.Equal("Updated", items[0].Name);
            Assert.False(items[0].Enabled);
            Assert.Equal("ops", items[0].Department);
            Assert.Equal(new Uri("https://example.invalid/updated"), items[0].Address);
        });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void EnterAndEscapeFinishEveryEditorBeforeChildControlsHandleTheKey(int index, bool cancel)
    {
        WithGrid(false, (control, columns, items) =>
        {
            var grid = Assert.IsType<DataGrid>(control);
            grid.SelectedIndex = 0;
            grid.CurrentCell = new DataGridCellInfo(items[0], columns[index]);
            _ = Keyboard.PrimaryDevice;
            Assert.True(grid.BeginEdit());
            try
            {
                var cell = Cells(Rows(grid)[0])[index];
                var editor = Assert.IsAssignableFrom<FrameworkElement>(cell.Content);
                if (editor is TextBox text) text.Text = index == 0 ? "Updated" : "https://example.invalid/updated";
                else if (editor is CheckBox check) check.IsChecked = false;
                else if (editor is ComboBox combo) combo.SelectedValue = "ops";
                // Editing changes stay inside the editor until the transaction is committed.
                Assert.Equal("Alpha", items[0].Name);
                Assert.True(items[0].Enabled);
                Assert.Equal("eng", items[0].Department);
                Assert.Equal(new Uri("https://example.invalid/grid"), items[0].Address);
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, new KeySource { RootVisual = grid }, 1,
                    cancel ? Key.Escape : Key.Enter) { RoutedEvent = UIElement.PreviewKeyDownEvent, Source = editor };
                editor.RaiseEvent(key);
                Assert.True(key.Handled);
                Assert.False(cell.IsEditing);
                Assert.Same(grid, Keyboard.FocusedElement);
                Layout(grid);
                Assert.Equal(!cancel && index == 0 ? "Updated" : "Alpha", items[0].Name);
                Assert.Equal(cancel || index != 1, items[0].Enabled);
                Assert.Equal(!cancel && index == 2 ? "ops" : "eng", items[0].Department);
                Assert.Equal(new Uri(!cancel && index == 3 ? "https://example.invalid/updated" : "https://example.invalid/grid"), items[0].Address);
            }
            finally { grid.CancelEdit(); Keyboard.ClearFocus(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayElementsFollowSourceNotificationsWithoutWritingBack(bool tree)
    {
        WithGrid(tree, (grid, columns, items) =>
        {
            items[0].Name = "Changed";
            items[0].Enabled = false;
            items[0].Department = "ops";
            items[0].Address = new Uri("https://example.invalid/new");
            Layout(grid);
            var cells = Cells(Rows(grid)[0]);
            Assert.Contains(Find<TextBlock>(cells[0]), text => text.Text == "Changed");
            Assert.False(Assert.Single(Find<CheckBox>(cells[1])).IsChecked);
            var combo = Assert.Single(Find<ComboBox>(cells[2]));
            Assert.Equal("Operations", combo.Text);
            var link = Assert.Single(Find<HyperlinkButton>(cells[3]));
            Assert.Equal("Changed", link.Content);
            Assert.Equal(items[0].Address, link.NavigateUri);
            combo.SelectedValue = "eng";
            Assert.Equal("ops", items[0].Department);
            Assert.Equal(BindingMode.Default, Assert.IsType<Binding>(((DataGridComboBoxColumn)columns[2]).SelectedValueBinding).Mode);
        });
    }

    [Fact]
    public void DisplayBindingsPreserveColumnFormattingAndFallbackConfiguration()
    {
        WithGrid(false, (grid, columns, items) =>
        {
            var column = Assert.IsType<DataGridTextColumn>(columns[0]);
            var declaration = new Binding(nameof(RowData.Name)) { StringFormat = "Item {0}", FallbackValue = "Missing" };
            column.Binding = declaration;
            Layout(grid);
            Assert.Contains(Find<TextBlock>(Cells(Rows(grid)[0])[0]), text => text.Text == "Item Alpha");
            items[0].Name = "Changed";
            Layout(grid);
            Assert.Contains(Find<TextBlock>(Cells(Rows(grid)[0])[0]), text => text.Text == "Item Changed");
            Assert.Equal(BindingMode.Default, declaration.Mode);
            column.Binding = new Binding("MissingProperty") { FallbackValue = "Unavailable" };
            Layout(grid);
            Assert.Contains(Find<TextBlock>(Cells(Rows(grid)[0])[0]), text => text.Text == "Unavailable");
            column.Binding = new Binding(nameof(RowData.Name)) { TargetNullValue = "Empty" };
            items[0].Name = null!;
            Layout(grid);
            Assert.Contains(Find<TextBlock>(Cells(Rows(grid)[0])[0]), text => text.Text == "Empty");
        });
    }

    [Fact]
    public void OneTimeDisplayBindingsKeepTheirInitialValueAndColumnRemainsReadOnly()
    {
        WithGrid(false, (grid, columns, items) =>
        {
            var column = Assert.IsType<DataGridTextColumn>(columns[0]);
            column.Binding = new Binding(nameof(RowData.Name)) { Mode = BindingMode.OneTime };
            Layout(grid);
            Assert.True(column.IsReadOnly);
            Assert.Contains(Find<TextBlock>(Cells(Rows(grid)[0])[0]), text => text.Text == "Alpha");
            items[0].Name = "Changed";
            Layout(grid);
            Assert.Contains(Find<TextBlock>(Cells(Rows(grid)[0])[0]), text => text.Text == "Alpha");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrEmptyTemplateEditorsDeclineEditingAndKeepTheirDisplay(bool emptyTemplate)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        var app = Application.Current ?? new Application();
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { "Row item" } };
        var column = new DataGridTemplateColumn { Header = "Template", Width = 200 };
        if (emptyTemplate) column.CellEditingTemplate = new DataTemplate();
        grid.Columns.Add(column);
        Layout(grid);
        grid.SelectedIndex = 0;
        grid.CurrentCell = new DataGridCellInfo("Row item", column);
        Assert.False(grid.BeginEdit());
        var row = Assert.IsType<DataGridRow>(Rows(grid)[0]);
        var cell = Assert.Single(Cells(row));
        Assert.False(row.IsEditing);
        Assert.False(cell.IsEditing);
        Assert.Equal("Row item", Assert.IsType<TextBlock>(cell.Content).Text);
        GC.KeepAlive(app);
    }

    [Fact]
    public void ComboBoxPopupMinimumTracksItsEditorWithoutClampingContentOrTemplateOverrides()
    {
        WithGrid(false, (grid, columns, items) =>
        {
            var cell = Cells(Rows(grid)[0])[2];
            cell.IsEditing = true;
            Layout(grid);
            var combo = Assert.Single(Find<ComboBox>(cell));
            var popup = Assert.IsType<Jalium.UI.Controls.Primitives.Popup>(combo.FindName("PART_Popup"));
            Assert.True(double.IsNaN(popup.Width));
            Assert.True(double.IsPositiveInfinity(popup.MaxWidth));
            Assert.Equal(combo.ActualWidth, popup.MinWidth);
            popup.Width = 260;
            popup.MaxWidth = 300;
            columns[2].Width = 130;
            Layout(grid);
            Assert.Equal(260, popup.Width);
            Assert.Equal(300, popup.MaxWidth);
            Assert.Equal(combo.ActualWidth, popup.MinWidth);
        });
    }

    [Fact]
    public void ComboBoxConstructorDefaultsCanBeReplacedByAnExplicitStyle()
    {
        var app = Application.Current ?? new Application();
        var combo = new ComboBox();
        Assert.True(combo.Focusable);
        Assert.Equal(120, combo.MinWidth);
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 20.0));
        combo.Style = style;
        Layout(combo);
        Assert.False(combo.Focusable);
        Assert.Equal(20, combo.MinWidth);
        GC.KeepAlive(app);
    }

    private static void WithGrid(bool tree, Action<Control, DataGridColumn[], RowData[]> action)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        var app = Application.Current ?? new Application();
        var original = ThemeManager.CurrentTheme;
        try
        {
            ThemeManager.ApplyTheme(ThemeVariant.Light);
            Control grid = tree ? new TreeDataGrid() : new DataGrid();
            var items = new[] { new RowData { Name = "Alpha" }, new RowData { Name = "Beta", Enabled = false } };
            DataGridColumn[] columns =
            [
                new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(RowData.Name)), Width = 110 },
                new DataGridCheckBoxColumn { Header = "Enabled", Binding = new Binding(nameof(RowData.Enabled)), Width = 70 },
                new DataGridComboBoxColumn { Header = "Department", Width = 100,
                    ItemsSource = new[] { new Department("eng", "Engineering"), new Department("ops", "Operations") },
                    DisplayMemberPath = nameof(Department.Label), SelectedValuePath = nameof(Department.Code),
                    SelectedValueBinding = new Binding(nameof(RowData.Department)) },
                new DataGridHyperlinkColumn { Header = "Address", Binding = new Binding(nameof(RowData.Address)),
                    ContentBinding = new Binding(nameof(RowData.Name)), Width = 110 }
            ];
            var targetColumns = grid is DataGrid data ? data.Columns : ((TreeDataGrid)grid).Columns;
            foreach (var column in columns) targetColumns.Add(column);
            if (grid is DataGrid dataGrid) dataGrid.ItemsSource = items; else ((TreeDataGrid)grid).ItemsSource = items;
            Layout(grid);
            Assert.Equal(2, Rows(grid).Length);
            action(grid, columns, items);
        }
        finally { ThemeManager.ApplyTheme(original); GC.KeepAlive(app); }
    }

    private static Control[] Rows(Control grid) => ((IDictionary)grid.GetType()
        .GetField("_realizedRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!).Values.Cast<Control>().ToArray();
    private static DataGridCell[] Cells(Control row) => Assert.IsType<StackPanel>(row.FindName("PART_CellsPanel"))
        .Children.OfType<DataGridCell>().ToArray();
    private static IEnumerable<T> Find<T>(Visual root) where T : Visual
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            if (VisualTreeHelper.GetChild(root, index) is Visual child)
                foreach (var descendant in Find<T>(child)) yield return descendant;
    }
    private static void Layout(UIElement control)
    {
        control.Measure(new Size(480, 250)); control.Arrange(new Rect(0, 0, 480, 250)); control.UpdateLayout();
    }
    private sealed record Department(string Code, string Label);
    private sealed class RowData : INotifyPropertyChanged
    {
        private string _name = "";
        private bool _enabled = true;
        private string _department = "eng";
        private Uri _address = new("https://example.invalid/grid");
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Name { get => _name; set => Set(ref _name, value); }
        public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
        public string Department { get => _department; set => Set(ref _department, value); }
        public Uri Address { get => _address; set => Set(ref _address, value); }
        private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value; PropertyChanged?.Invoke(this, new(property));
        }
    }
    private sealed class KeySource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
}
