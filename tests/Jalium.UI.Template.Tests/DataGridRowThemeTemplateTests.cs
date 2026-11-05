using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DataGridRowThemeTemplateTests
{
    [Fact]
    public void BeginningAnEditArrangesTheEditorBeforeFocusObserversQueryItsImeGeometry()
    {
        WithGrid(false, control =>
        {
            var grid = Assert.IsType<DataGrid>(control);
            grid.SelectedIndex = 0;
            _ = Keyboard.PrimaryDevice;
            Assert.True(grid.Focus());
            var queries = 0;
            grid.LostKeyboardFocus += (_, e) =>
            {
                if (e.NewFocus is not TextBox editor) return;
                ++queries;
                var caret = editor.GetRectFromCharacterIndex(editor.CaretIndex);
                Assert.True(double.IsFinite(caret.X) && double.IsFinite(caret.Y));
                Assert.True(editor.ActualHeight > 0);
            };
            try
            {
                Assert.True(grid.BeginEdit());
                var editor = Assert.IsType<TextBox>(Keyboard.FocusedElement);
                Assert.True(editor.IsKeyboardFocused);
                Assert.Equal(editor.Text.Length, editor.SelectionLength);
                Assert.Equal(1, queries);
            }
            finally { grid.CancelEdit(); Keyboard.ClearFocus(); }
        });
    }

    [Fact]
    public void AnAppliedTextTemplateCanReportImeGeometryBeforeItsFirstArrange()
    {
        var template = new ControlTemplate(typeof(TextBox));
        template.SetVisualTree(() => new Border { Name = "PART_ContentHost" });
        var editor = new TextBox { Text = "Alice", Template = template };
        editor.ApplyTemplate();
        Assert.IsType<Border>(editor.FindName("PART_ContentHost"));
        var caret = editor.GetRectFromCharacterIndex(editor.CaretIndex);
        Assert.True(double.IsFinite(caret.X) && double.IsFinite(caret.Y));
        Assert.True(caret.Width >= 0 && caret.Height > 0);
        editor.SelectAll();
        Assert.Equal(editor.Text.Length, editor.SelectionLength);
    }

    [Fact]
    public void RealizationRefreshDoesNotDetachContainersThatRemainInTheViewport()
    {
        WithGrid(false, grid =>
        {
            var rows = Rows(grid).Cast<TrackingRow>().ToArray();
            var changes = rows.Select(row => row.ParentChanges).ToArray();
            typeof(DataGrid).GetMethod("UpdateRealizedRows", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, new object[] { true });
            Layout(grid);
            Assert.Equal(rows, Rows(grid));
            Assert.Equal(changes, rows.Select(row => row.ParentChanges).ToArray());
        }, trackRows: true);
    }

    [Fact]
    public void RefreshingTheRealizedRangeKeepsAnExistingEditorFocusedAndSelected()
    {
        WithGrid(false, control =>
        {
            var grid = Assert.IsType<DataGrid>(control);
            grid.SelectedIndex = 0;
            _ = Keyboard.PrimaryDevice;
            Assert.True(grid.BeginEdit());
            try
            {
                Layout(grid);
                var row = Rows(grid).OfType<DataGridRow>().Single(candidate => candidate.IsEditing);
                var cell = Assert.IsType<StackPanel>(row.FindName("PART_CellsPanel")).Children.OfType<DataGridCell>().Single(candidate => candidate.IsEditing);
                var editor = Assert.IsType<TextBox>(cell.Content);
                Assert.True(editor.Focus()); editor.SelectAll();
                typeof(DataGrid).GetMethod("UpdateRealizedRows", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, new object[] { true });
                Layout(grid);
                Assert.Same(editor, Keyboard.FocusedElement);
                Assert.Equal(editor.Text.Length, editor.SelectionLength);
            }
            finally { grid.CancelEdit(); Keyboard.ClearFocus(); }
        });
    }

    [Theory]
    [InlineData(false, 0, 0, 0, 1)]
    [InlineData(false, 0, 1, 1, 0)]
    [InlineData(true, 1, 0, 0, 1)]
    public void TabContinuesEditingInVisibleDisplayOrderAndWrapsRows(bool backwards, int initialRow, int initialColumn, int expectedRow, int expectedColumn)
    {
        WithGrid(false, control =>
        {
            var grid = Assert.IsType<DataGrid>(control);
            var hidden = new DataGridTextColumn { Header = "Hidden", Binding = new Binding(nameof(RowData.Name)), Visibility = Visibility.Collapsed };
            var other = new DataGridTextColumn { Header = "Other", Binding = new Binding(nameof(RowData.Name)), Width = 100 };
            grid.Columns.Add(hidden); grid.Columns.Add(other);
            other.DisplayIndex = 0;
            grid.Columns[0].DisplayIndex = 2;
            Layout(grid);
            var ordered = grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).ToArray();
            var items = grid.ItemsSource!.Cast<object>().ToArray();
            grid.SelectedIndex = initialRow;
            grid.CurrentCell = new DataGridCellInfo(items[initialRow], ordered[initialColumn]);
            _ = Keyboard.PrimaryDevice;
            Assert.True(grid.BeginEdit());
            try
            {
                var editor = Assert.IsType<TextBox>(Keyboard.FocusedElement);
                var key = new KeyEventArgs(new TabKeyboard(backwards), new Source { RootVisual = grid }, 1, Key.Tab)
                    { RoutedEvent = UIElement.KeyDownEvent, Source = editor };
                editor.RaiseEvent(key);
                Assert.True(key.Handled);
                Assert.Same(items[expectedRow], grid.CurrentCell.Item);
                Assert.Same(ordered[expectedColumn], grid.CurrentCell.Column);
                var next = Assert.IsType<TextBox>(Keyboard.FocusedElement);
                Assert.NotSame(editor, next);
                Assert.True(next.IsKeyboardFocused);
                Assert.Equal(next.Text.Length, next.SelectionLength);
            }
            finally { grid.CancelEdit(); Keyboard.ClearFocus(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TabAtTheGridBoundaryCommitsThenMovesToTheAdjacentControl(bool backwards)
    {
        WithGrid(false, control =>
        {
            var grid = Assert.IsType<DataGrid>(control);
            grid.Height = 120;
            grid.ItemsSource = Enumerable.Range(0, 200).Select(index => new RowData { Name = $"Row {index}" }).ToArray();
            var before = new Button { Content = "Before" };
            var after = new Button { Content = "After" };
            var root = new StackPanel();
            root.Children.Add(before); root.Children.Add(grid); root.Children.Add(after);
            Layout(root);
            var items = grid.ItemsSource!.Cast<object>().ToArray();
            grid.SelectedIndex = backwards ? 0 : items.Length - 1;
            grid.CurrentCell = new DataGridCellInfo(items[grid.SelectedIndex], grid.Columns[0]);
            _ = Keyboard.PrimaryDevice;
            Assert.True(grid.BeginEdit());
            try
            {
                var editor = Assert.IsType<TextBox>(Keyboard.FocusedElement);
                var key = new KeyEventArgs(new TabKeyboard(backwards), new Source { RootVisual = root }, 1, Key.Tab)
                    { RoutedEvent = UIElement.KeyDownEvent, Source = editor };
                editor.RaiseEvent(key);
                Assert.True(key.Handled);
                Assert.Same(backwards ? before : after, Keyboard.FocusedElement);
                Assert.False(Rows(grid).OfType<DataGridRow>().Any(row => row.IsEditing));
            }
            finally { grid.CancelEdit(); Keyboard.ClearFocus(); root.Children.Clear(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NarrowViewportsExposeTheFullColumnExtentAndKeepHeadersSynchronized(bool tree)
    {
        WithGrid(tree, grid =>
        {
            Columns(grid).Add(new DataGridTextColumn { Header = "Other", Binding = new Binding(nameof(RowData.Name)), Width = 200 });
            Columns(grid).Add(new DataGridTextColumn { Header = "Last", Binding = new Binding(nameof(RowData.Name)), Width = 200 });
            Layout(grid);
            var body = Assert.IsType<ScrollViewer>(grid.FindName("PART_DataScrollViewer"));
            var headers = Assert.IsType<ScrollViewer>(grid.FindName("PART_ColumnHeadersScrollViewer"));
            Assert.True(body.ScrollableWidth > 100, $"extent={body.ExtentWidth} viewport={body.ViewportWidth}");
            body.ScrollToHorizontalOffset(body.ScrollableWidth);
            Layout(grid);
            Assert.True(body.HorizontalOffset > 100);
            Assert.Equal(body.HorizontalOffset, headers.HorizontalOffset);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GridForegroundFlowsIntoDefaultRowsAndExplicitRowStylesWin(bool tree)
    {
        WithGrid(tree, grid =>
        {
            var rows = Rows(grid);
            var ownerColor = new SolidColorBrush(Color.FromRgb(140, 30, 150));
            grid.Foreground = ownerColor;
            Layout(grid);
            Assert.Same(ownerColor, FindText(rows[0], "Alpha")!.Foreground);
            ThemeManager.ApplyTheme(ThemeVariant.Dark);
            Layout(grid);
            Assert.Same(ownerColor, FindText(rows[0], "Alpha")!.Foreground);
            var rowColor = new SolidColorBrush(Color.FromRgb(10, 160, 90));
            var style = new Style(rows[0].GetType());
            style.Setters.Add(new Setter(Control.ForegroundProperty, rowColor));
            rows[0].Style = style;
            Layout(grid);
            Assert.Same(rowColor, FindText(rows[0], "Alpha")!.Foreground);
            Assert.Equal(rows, Rows(grid));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VirtualizationReusesTemplateSpacersAndMissingPartsRealizeAllRows(bool tree)
    {
        WithGrid(tree, grid =>
        {
            var top = Assert.IsType<Grid>(grid.FindName("PART_TopSpacer"));
            var bottom = Assert.IsType<Grid>(grid.FindName("PART_BottomSpacer"));
            var items = Enumerable.Range(0, 200).Select(i => new RowData { Name = $"Row {i}" }).ToArray();
            if (grid is DataGrid data) data.ItemsSource = items; else ((TreeDataGrid)grid).ItemsSource = items;
            Layout(grid);
            Assert.Same(top, grid.FindName("PART_TopSpacer"));
            Assert.Same(bottom, grid.FindName("PART_BottomSpacer"));
            Assert.True(Rows(grid).Length < 200);
            var minimal = new ControlTemplate(grid.GetType());
            minimal.SetVisualTree(() => new ScrollViewer
            {
                Name = "PART_DataScrollViewer", Content = new StackPanel { Name = "PART_RowsHost" }
            });
            grid.Template = minimal;
            Layout(grid);
            Assert.Equal(200, Rows(grid).Length);
            Assert.Null(grid.FindName("PART_TopSpacer"));
            Assert.Null(grid.FindName("PART_BottomSpacer"));
        });
    }

    [Theory]
    [InlineData(Key.Home, 0)]
    [InlineData(Key.End, 1)]
    [InlineData(Key.Up, 0)]
    public void KeyboardRowNavigationMovesTheEditTargetToTheSelectedRow(Key key, int expectedIndex)
    {
        WithGrid(false, control =>
        {
            var grid = Assert.IsType<DataGrid>(control);
            var items = grid.ItemsSource!.Cast<object>().ToArray();
            var column = grid.Columns[0];
            grid.SelectedIndex = 1;
            grid.CurrentCell = new DataGridCellInfo(items[1], column);
            grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new Source { RootVisual = grid }, 1, key)
                { RoutedEvent = UIElement.KeyDownEvent, Source = grid });
            Assert.Equal(expectedIndex, grid.SelectedIndex);
            Assert.Same(items[expectedIndex], grid.CurrentCell.Item);
            Assert.Same(column, grid.CurrentCell.Column);
            Assert.True(grid.BeginEdit());
            var row = Rows(grid).OfType<DataGridRow>().Single(candidate => candidate.IsEditing);
            Assert.Equal(expectedIndex, row.GetIndex());
            Layout(grid);
            var cell = Assert.IsType<StackPanel>(row.FindName("PART_CellsPanel")).Children.OfType<DataGridCell>().Single(candidate => candidate.IsEditing);
            var editor = Assert.IsType<TextBox>(cell.Content);
            Assert.Equal(editor.Text.Length, editor.SelectionLength);
            Assert.InRange(editor.ActualHeight, 1, cell.ActualHeight);
            Assert.True(grid.CancelEdit());
        });
    }

    [Fact]
    public void FullRowSelectionUsesOneTintAndCellSelectionRemainsVisible()
    {
        WithGrid(false, grid =>
        {
            var row = Assert.IsType<DataGridRow>(Rows(grid)[0]);
            var cell = Assert.Single(Assert.IsType<StackPanel>(row.FindName("PART_CellsPanel")).Children.OfType<DataGridCell>());
            row.IsSelected = true;
            cell.IsSelected = true;
            Layout(grid);
            var tint = Assert.IsType<Grid>(cell.FindName("PART_CellSelectionTint"));
            Assert.Equal(Visibility.Collapsed, tint.Visibility);
            row.IsSelected = false;
            Layout(grid);
            Assert.Equal(Visibility.Visible, tint.Visibility);
            cell.IsSelected = false;
            Assert.Equal(Visibility.Collapsed, tint.Visibility);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RowTemplateReplacementDetachesOldCellsAndPreservesContainers(bool tree)
    {
        WithGrid(tree, grid =>
        {
            var row = Rows(grid)[0];
            var original = row.Template;
            var oldPanel = Assert.IsType<StackPanel>(row.FindName("PART_CellsPanel"));
            var children = oldPanel.Children.Cast<UIElement>().ToArray();
            Assert.NotEmpty(children);
            var minimal = new ControlTemplate(row.GetType());
            minimal.SetVisualTree(() => new Grid());
            row.Template = minimal;
            Layout(grid);
            Assert.Empty(oldPanel.Children);
            row.Template = original;
            Layout(grid);
            var newPanel = Assert.IsType<StackPanel>(row.FindName("PART_CellsPanel"));
            Assert.NotSame(oldPanel, newPanel);
            Assert.Equal(children, newPanel.Children.Cast<UIElement>().ToArray());
            Assert.Same(row, Rows(grid)[0]);
        });
    }

    [Fact]
    public void AutomationExposesRealizedHeadersRowsAndCellValues()
    {
        WithGrid(false, grid =>
        {
            var peer = Assert.IsType<DataGridAutomationPeer>(grid.GetAutomationPeer());
            var children = peer.GetChildren();
            Assert.Contains(children, child => child.GetName() == "Name" && !child.GetBoundingRectangle().IsEmpty);
            var rows = children.OfType<DataGridItemAutomationPeer>().ToArray();
            Assert.Equal(2, rows.Length);
            Assert.All(rows, row => Assert.False(row.IsOffscreen()));
            var cell = Assert.Single(rows[0].GetChildren());
            Assert.Equal("Alpha", cell.GetName());
            Assert.False(cell.GetBoundingRectangle().IsEmpty);
            Assert.Same(rows[0], cell.GetParent());
        });
    }

    [Fact]
    public void RowHeaderContentAndSelectionStatesSurviveRetemplating()
    {
        WithGrid(false, grid =>
        {
            var row = Assert.IsType<DataGridRow>(Rows(grid)[0]);
            var header = Assert.IsType<Jalium.UI.Controls.Primitives.DataGridRowHeader>(
                Assert.IsType<StackPanel>(row.FindName("PART_CellsPanel")).Children[0]);
            Assert.False(header.HasHeaderContent);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<ContentPresenter>(header.FindName("PART_ContentPresenter")).Visibility);
            row.Header = "1";
            row.IsSelected = true;
            Layout(grid);
            Assert.True(header.HasHeaderContent);
            Assert.Equal(Visibility.Visible, Assert.IsType<ContentPresenter>(header.FindName("PART_ContentPresenter")).Visibility);
            Assert.Equal(Visibility.Visible, Assert.IsType<Grid>(header.FindName("PART_SelectionTint")).Visibility);
            var original = header.Template;
            var minimal = new ControlTemplate(header.GetType());
            minimal.SetVisualTree(() => new Grid());
            header.Template = minimal;
            Layout(grid);
            row.Header = null;
            row.IsSelected = false;
            header.Template = original;
            Layout(grid);
            Assert.False(header.HasHeaderContent);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Grid>(header.FindName("PART_SelectionTint")).Visibility);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultTextUsesLiveThemeForegroundAcrossPaletteChanges(bool tree)
    {
        WithGrid(tree, grid =>
        {
            var rows = Rows(grid);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark, ThemeVariant.Light })
            {
                ThemeManager.ApplyTheme(theme);
                Layout(grid);
                Assert.Equal(rows, Rows(grid));
                var expected = ColorOf(grid.TryFindResource("TextPrimary"));
                Assert.Equal(expected, ColorOf(FindText(rows[0], "Alpha")!.Foreground));
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlternatingBrushChangesRepaintExistingRowsWithoutRecreatingThem(bool tree)
    {
        WithGrid(tree, grid =>
        {
            var rows = Rows(grid);
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                ThemeManager.ApplyTheme(theme);
                Layout(grid);
                Assert.Equal(rows, Rows(grid));
                var expected = grid is DataGrid data ? data.AlternatingRowBackground : ((TreeDataGrid)grid).AlternatingRowBackground;
                Assert.Equal(ColorOf(expected), ColorOf(rows[1].Background));
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitElementStylesAndColumnForegroundRemainAuthoritative(bool tree)
    {
        WithGrid(tree, grid =>
        {
            var column = (DataGridTextColumn)Columns(grid)[0];
            var styleColor = new SolidColorBrush(Color.FromRgb(150, 30, 170));
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, styleColor));
            column.ElementStyle = style;
            Layout(grid);
            Assert.Same(styleColor, FindText(Rows(grid)[0], "Alpha")!.Foreground);
            var columnColor = new SolidColorBrush(Color.FromRgb(0, 110, 70));
            column.Foreground = columnColor;
            Layout(grid);
            Assert.Same(columnColor, FindText(Rows(grid)[0], "Alpha")!.Foreground);
        });
    }

    private static void WithGrid(bool tree, Action<Control> action, bool trackRows = false)
    {
        var originalTheme = ThemeManager.CurrentTheme;
        Jalium.UI.Markup.ThemeLoader.Initialize();
        var app = Application.Current ?? new Application();
        try
        {
            ThemeManager.ApplyTheme(ThemeVariant.Light);
            Control grid = tree ? new TreeDataGrid() : trackRows ? new TrackingGrid() : new DataGrid();
            Columns(grid).Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(RowData.Name)), Width = 200 });
            var items = new[] { new RowData { Name = "Alpha" }, new RowData { Name = "Beta" } };
            if (grid is DataGrid data) data.ItemsSource = items; else ((TreeDataGrid)grid).ItemsSource = items;
            Layout(grid);
            var realized = Rows(grid);
            var itemCount = grid is DataGrid dg ? ((ICollection)typeof(DataGrid).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dg)!).Count : -1;
            Assert.True(realized.Length == 2, $"{grid.GetType().Name}: rows={realized.Length}, items={itemCount}, template={grid.Template != null}, host={grid.FindName("PART_RowsHost") != null}");
            action(grid);
        }
        finally { ThemeManager.ApplyTheme(originalTheme); GC.KeepAlive(app); }
    }
    private static Control[] Rows(Control grid) => ((IDictionary)(grid is DataGrid ? typeof(DataGrid) : typeof(TreeDataGrid))
        .GetField("_realizedRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!).Values.Cast<Control>().ToArray();
    private static IList<DataGridColumn> Columns(Control grid) => grid is DataGrid data ? data.Columns : ((TreeDataGrid)grid).Columns;
    private static Color ColorOf(object? brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static TextBlock? FindText(DependencyObject root, string value)
    {
        if (root is TextBlock text && text.Text == value) return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
            if (VisualTreeHelper.GetChild(root, i) is { } child && FindText(child, value) is { } found) return found;
        return null;
    }
    private static void Layout(UIElement element)
    {
        element.Measure(new Size(400, 250)); element.Arrange(new Rect(0, 0, 400, 250)); element.UpdateLayout();
    }
    private sealed class TabKeyboard(bool backwards) : KeyboardDevice
    {
        public override IInputElement? Target => null;
        public override PresentationSource? ActiveSource => null;
        protected override KeyStates GetKeyStatesFromSystem(Key key) =>
            backwards && key is Key.LeftShift or Key.RightShift ? KeyStates.Down : KeyStates.None;
    }
    private sealed class RowData { public string Name { get; set; } = ""; }
    private sealed class TrackingGrid : DataGrid
    {
        protected override DependencyObject GetContainerForItemOverride() => new TrackingRow();
    }
    private sealed class TrackingRow : DataGridRow
    {
        public int ParentChanges { get; private set; }
        protected override void OnVisualParentChanged(DependencyObject? oldParent)
        {
            ++ParentChanges;
            base.OnVisualParentChanged(oldParent);
        }
    }
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
}
