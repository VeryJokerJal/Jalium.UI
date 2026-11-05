using System.Collections.ObjectModel;
using System.Reflection;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DataGridAutomationViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedRowsProvidersAndBoundsFollowTheSortedView(bool manualItems)
    {
        WithGrid(new[] { new Row("z", 2), new Row("Alpha", 1) }, (grid, columns) =>
        {
            var peer = Assert.IsType<DataGridAutomationPeer>(grid.GetAutomationPeer());
            var provider = Assert.IsAssignableFrom<IGridProvider>(peer.GetPattern(PatternInterface.Grid));
            var before = Peers(peer);
            Assert.Equal(new[] { "z", "Alpha" }, Names(peer));
            var zCell = Assert.IsAssignableFrom<IRawElementProviderSimple>(provider.GetItem(0, 0));
            Sort(grid, columns[0]);
            Layout(grid);
            var sorted = Peers(peer);
            Assert.Equal(new[] { "Alpha", "z" }, Names(peer));
            Assert.Same(before[0], sorted[1]);
            Assert.Same(zCell, provider.GetItem(1, 0));
            Assert.Same(sorted[0].GetChildren()[0], Unwrap(provider.GetItem(0, 0)!));
            Assert.Equal("Cell_1_0", Unwrap(zCell).GetAutomationId());
            Assert.True(sorted[0].GetBoundingRectangle().Y < sorted[1].GetBoundingRectangle().Y);
            Sort(grid, columns[0]);
            Layout(grid);
            Assert.Equal(new[] { "z", "Alpha" }, Names(peer));
            Assert.Same(zCell, provider.GetItem(0, 0));
            grid.Columns.Move(1, 0);
            Layout(grid);
            Assert.Equal("2", Unwrap(provider.GetItem(0, 0)!).GetName());
            Assert.Equal("z", Unwrap(provider.GetItem(0, 1)!).GetName());
        }, manualItems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualAndRepeatedItemsKeepDistinctRowsCellsAndParents(bool sameReference)
    {
        Row first = new EqualRow("z", 2);
        var rows = new[] { first, new EqualRow("a", 1), sameReference ? first : new EqualRow("z", 3) };
        WithGrid(rows, (grid, columns) =>
        {
            var peer = Assert.IsType<DataGridAutomationPeer>(grid.GetAutomationPeer());
            Sort(grid, columns[0]);
            Layout(grid);
            Assert.Equal(new[] { "a", "z", "z" }, Names(peer));
            var rowPeers = Peers(peer);
            var provider = Assert.IsAssignableFrom<IGridProvider>(peer.GetPattern(PatternInterface.Grid));
            var firstCell = Assert.IsAssignableFrom<IRawElementProviderSimple>(provider.GetItem(1, 0));
            var secondCell = Assert.IsAssignableFrom<IRawElementProviderSimple>(provider.GetItem(2, 0));
            Assert.NotSame(rowPeers[1], rowPeers[2]);
            Assert.NotSame(firstCell, secondCell);
            Assert.Same(rowPeers[1], Unwrap(firstCell).GetParent());
            Assert.Same(rowPeers[2], Unwrap(secondCell).GetParent());
            Assert.Equal("Cell_1_0", Unwrap(firstCell).GetAutomationId());
            Assert.Equal("Cell_2_0", Unwrap(secondCell).GetAutomationId());
            Assert.True(Unwrap(firstCell).GetBoundingRectangle().Y < Unwrap(secondCell).GetBoundingRectangle().Y);
        });
    }

    [Fact]
    public void SourceMutationsKeepSortingAndRetireTheRemovedOccurrence()
    {
        var source = new ObservableCollection<Row> { new("z", 3), new("a", 1), new("c", 2) };
        WithGrid(source, (grid, columns) =>
        {
            var peer = Assert.IsType<DataGridAutomationPeer>(grid.GetAutomationPeer());
            var provider = Assert.IsAssignableFrom<IGridProvider>(peer.GetPattern(PatternInterface.Grid));
            Sort(grid, columns[0]);
            Layout(grid);
            Assert.Equal(new[] { "a", "c", "z" }, Names(peer));
            var removed = Unwrap(provider.GetItem(2, 0)!);
            source.RemoveAt(0);
            Layout(grid);
            Assert.Equal(new[] { "a", "c" }, Names(peer));
            Assert.True(removed.IsOffscreen());
            Assert.True(removed.GetBoundingRectangle().IsEmpty);
            source.Insert(0, new Row("b", 4));
            Layout(grid);
            Assert.Equal(new[] { "a", "b", "c" }, Names(peer));
            source[0] = new Row("y", 5);
            source.Move(2, 0);
            Layout(grid);
            Assert.Equal(new[] { "a", "c", "y" }, Names(peer));
            source.Clear();
            Layout(grid);
            Assert.Empty(Peers(peer));
            Assert.Equal(0, provider.RowCount);
            source.Add(new Row("d", 6));
            Layout(grid);
            Assert.Equal(new[] { "d" }, Names(peer));
            Assert.Equal(1, provider.RowCount);
        });
    }

    [Fact]
    public void ExplicitSortMemberPathControlsTheProviderView()
    {
        WithGrid(new[] { new Row("z", 1), new Row("Alpha", 2) }, (grid, columns) =>
        {
            columns[0].SortMemberPath = nameof(Row.Rank);
            Sort(grid, columns[0]);
            Layout(grid);
            var peer = Assert.IsType<DataGridAutomationPeer>(grid.GetAutomationPeer());
            Assert.Equal(new[] { "z", "Alpha" }, Names(peer));
            Sort(grid, columns[0]);
            Layout(grid);
            Assert.Equal(new[] { "Alpha", "z" }, Names(peer));
        });
    }

    [Fact]
    public void ValueTypeRowsAndNullVirtualRowsKeepTheirGridIndices()
    {
        WithGrid(new[] { new Row("z", 2) }, (grid, columns) =>
        {
            grid.ItemsSource = new[] { new ValueRow("z", 3), new ValueRow("a", 1), new ValueRow("a", 2) };
            Layout(grid);
            var peer = Assert.IsType<DataGridAutomationPeer>(grid.GetAutomationPeer());
            Sort(grid, columns[0]);
            Layout(grid);
            Assert.Equal(new[] { "a", "a", "z" }, Names(peer));
            Assert.NotSame(Peers(peer)[0], Peers(peer)[1]);
        });
        var app = Application.Current ?? new Application();
        var unhosted = new DataGrid { AutoGenerateColumns = false };
        unhosted.Columns.Add(new DataGridTextColumn { Binding = new Binding(nameof(Row.Name)) });
        unhosted.ItemsSource = new object?[] { new Row("a", 1), null, new Row("z", 2) };
        var provider = Assert.IsAssignableFrom<IGridProvider>(unhosted.GetAutomationPeer()!.GetPattern(PatternInterface.Grid));
        Assert.Equal(3, provider.RowCount);
        Assert.Null(provider.GetItem(1, 0));
        Assert.Equal("Cell_2_0", Unwrap(provider.GetItem(2, 0)!).GetAutomationId());
        GC.KeepAlive(app);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SortingFinishesTheCurrentEditOrHonorsItsCancellation(bool cancelCommit)
    {
        var rows = new[] { new Row("z", 2), new Row("a", 1) };
        WithGrid(rows, (grid, columns) =>
        {
            grid.SelectedIndex = 0;
            grid.CurrentCell = new DataGridCellInfo(rows[0], columns[0]);
            _ = Keyboard.PrimaryDevice;
            Assert.True(grid.BeginEdit());
            var editor = Assert.IsType<TextBox>(Keyboard.FocusedElement);
            editor.Text = "b";
            EventHandler<DataGridCellEditEndingEventArgs> ending = (_, args) => args.Cancel = cancelCommit;
            grid.CellEditEnding += ending;
            try
            {
                Sort(grid, columns[0]);
                Layout(grid);
                var peer = Assert.IsType<DataGridAutomationPeer>(grid.GetAutomationPeer());
                Assert.Equal(cancelCommit ? new[] { "z", "a" } : new[] { "a", "b" }, Names(peer));
                Assert.Equal(cancelCommit ? "z" : "b", rows[0].Name);
                Assert.Equal(cancelCommit, ReferenceEquals(editor, Keyboard.FocusedElement));
            }
            finally { grid.CellEditEnding -= ending; grid.CancelEdit(); Keyboard.ClearFocus(); }
        });
    }

    [Fact]
    public void EnterAfterChangingTheSortKeyMovesItsRowAndKeepsTheCurrentItem()
    {
        var rows = new[] { new Row("z", 2), new Row("Beta", 1) };
        WithGrid(rows, (grid, columns) =>
        {
            Sort(grid, columns[0]); Layout(grid);
            Begin(grid, rows[0], columns[0], 1);
            Assert.IsType<TextBox>(Keyboard.FocusedElement).Text = "a";
            try
            {
                SendKey(grid, Key.Enter, preview: true);
                Assert.Equal(new[] { "a", "Beta" }, Names(Assert.IsType<DataGridAutomationPeer>(grid.GetAutomationPeer())));
                Assert.Same(rows[0], grid.CurrentCell.Item);
                Assert.Equal(0, grid.SelectedIndex);
                Assert.Same(grid, Keyboard.FocusedElement);
            }
            finally { grid.CancelEdit(); Keyboard.ClearFocus(); }
        });
    }

    [Theory]
    [InlineData(0, false, 0, 1)]
    [InlineData(1, false, 1, 0)]
    [InlineData(1, true, 0, 0)]
    [InlineData(0, true, -1, -1)]
    public void TabTargetsAreComputedAfterTheEditedRowMoves(int initialColumn, bool backwards, int expectedRow, int expectedColumn)
    {
        var rows = new[] { new Row("z", 2), new Row("Beta", 1) };
        WithGrid(rows, (grid, columns) =>
        {
            Assert.IsType<DataGridTextColumn>(columns[1]).Binding = new Binding(nameof(Row.Name));
            Sort(grid, columns[0]); Layout(grid);
            Begin(grid, rows[0], columns[initialColumn], 1);
            var editor = Assert.IsType<TextBox>(Keyboard.FocusedElement);
            editor.Text = "a";
            try
            {
                SendKey(grid, Key.Tab, backwards: backwards);
                Layout(grid);
                Assert.Equal(new[] { "a", "Beta" }, Names(Assert.IsType<DataGridAutomationPeer>(grid.GetAutomationPeer())));
                if (expectedRow < 0)
                {
                    Assert.Same(grid, Keyboard.FocusedElement);
                    Assert.False(grid.CommitEdit());
                }
                else
                {
                    Assert.Same(rows[expectedRow == 0 ? 0 : 1], grid.CurrentCell.Item);
                    Assert.Same(columns[expectedColumn], grid.CurrentCell.Column);
                    Assert.Equal(expectedRow == 0 ? "a" : "Beta", Assert.IsType<TextBox>(Keyboard.FocusedElement).Text);
                }
            }
            finally { grid.CancelEdit(); Keyboard.ClearFocus(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndingObserversMayReplaceTheSourceWithoutCommittingARetiredRow(bool rowEnding)
    {
        var original = new Row("Original", 1);
        var replacement = new Row("Replacement", 2);
        WithGrid(new[] { original }, (grid, columns) =>
        {
            Begin(grid, original, columns[0], 0);
            Assert.IsType<TextBox>(Keyboard.FocusedElement).Text = "Uncommitted";
            EventHandler<DataGridCellEditEndingEventArgs> cellHandler = (_, _) => { if (!rowEnding) grid.ItemsSource = new[] { replacement }; };
            EventHandler<DataGridRowEditEndingEventArgs> rowHandler = (_, _) => { if (rowEnding) grid.ItemsSource = new[] { replacement }; };
            grid.CellEditEnding += cellHandler; grid.RowEditEnding += rowHandler;
            try
            {
                Assert.False(grid.CommitEdit(rowEnding ? DataGridEditingUnit.Row : DataGridEditingUnit.Cell, true));
                Assert.Equal("Original", original.Name);
                Assert.Equal("Replacement", replacement.Name);
                Layout(grid);
                Assert.Equal(new[] { "Replacement" }, Names(Assert.IsType<DataGridAutomationPeer>(grid.GetAutomationPeer())));
                grid.CellEditEnding -= cellHandler; grid.RowEditEnding -= rowHandler;
                Begin(grid, replacement, columns[0], 0);
                Assert.Equal("Replacement", Assert.IsType<TextBox>(Keyboard.FocusedElement).Text);
            }
            finally
            {
                grid.CellEditEnding -= cellHandler; grid.RowEditEnding -= rowHandler;
                grid.CancelEdit(); Keyboard.ClearFocus();
            }
        });
    }

    [Fact]
    public void AReplacementEditStartedByAnEndingObserverKeepsItsOwnTransaction()
    {
        var rows = new[] { new Row("Original", 1), new Row("Next", 2) };
        WithGrid(rows, (grid, columns) =>
        {
            Begin(grid, rows[0], columns[0], 0);
            Assert.IsType<TextBox>(Keyboard.FocusedElement).Text = "Uncommitted";
            EventHandler<DataGridCellEditEndingEventArgs> handler = (_, args) =>
            {
                if (args.EditAction != DataGridEditAction.Commit) return;
                grid.CancelEdit();
                Begin(grid, rows[1], columns[0], 1);
            };
            grid.CellEditEnding += handler;
            try
            {
                Assert.False(grid.CommitEdit());
                Assert.Equal("Original", rows[0].Name);
                Assert.Same(rows[1], grid.CurrentCell.Item);
                Assert.Equal("Next", Assert.IsType<TextBox>(Keyboard.FocusedElement).Text);
            }
            finally { grid.CellEditEnding -= handler; grid.CancelEdit(); Keyboard.ClearFocus(); }
        });
    }

    [Fact]
    public void ASourceReplacementDuringValueWriteDoesNotTouchTheNewRows()
    {
        var original = new CallbackRow("Original", 1);
        var replacement = new Row("Replacement", 2);
        WithGrid(new[] { original }, (grid, columns) =>
        {
            Begin(grid, original, columns[0], 0);
            Assert.IsType<TextBox>(Keyboard.FocusedElement).Text = "Committed";
            original.Changed = () => grid.ItemsSource = new[] { replacement };
            try
            {
                Assert.False(grid.CommitEdit());
                Layout(grid);
                Assert.Equal("Committed", original.Name);
                Assert.Equal("Replacement", replacement.Name);
                Begin(grid, replacement, columns[0], 0);
            }
            finally { original.Changed = null; grid.CancelEdit(); Keyboard.ClearFocus(); }
        });
    }

    [Fact]
    public void ScalarSelectionAndPendingValuesUseDisplayIndicesAfterSorting()
    {
        var rows = new ObservableCollection<Row> { new("z", 2), new("Beta", 1) };
        WithGrid(rows, (grid, columns) =>
        {
            grid.SelectedValuePath = nameof(Row.Rank);
            Sort(grid, columns[0]); Layout(grid);
            grid.SelectedIndex = 1;
            Assert.Same(rows[0], grid.SelectedItem);
            Assert.Equal(2, grid.SelectedValue);
            grid.SelectedValue = 1;
            Assert.Equal(0, grid.SelectedIndex);
            Assert.Same(rows[1], grid.SelectedItem);
            grid.SelectedItem = rows[0];
            Assert.Equal(1, grid.SelectedIndex);
            Assert.Equal(2, grid.SelectedValue);
            grid.SelectedValue = 99;
            Assert.Equal(-1, grid.SelectedIndex);
            Assert.Null(grid.SelectedItem);
            Assert.Equal(99, grid.SelectedValue);
            var pending = new Row("a", 99);
            rows.Add(pending); Layout(grid);
            Assert.Equal(0, grid.SelectedIndex);
            Assert.Same(pending, grid.SelectedItem);
            Assert.Equal(99, grid.SelectedValue);
            grid.SelectedValuePath = nameof(Row.Name);
            Assert.Equal("a", grid.SelectedValue);
            grid.SelectedValue = "Beta";
            Assert.Equal(1, grid.SelectedIndex);
            Assert.Same(rows[1], grid.SelectedItem);
            grid.SelectedValue = null;
            Assert.Equal(-1, grid.SelectedIndex);
            Assert.Null(grid.SelectedItem);
        });
    }

    [Fact]
    public void ExtendedSelectedItemsKeepTheirScalarValuesInDisplayOrder()
    {
        var rows = new[] { new Row("z", 2), new Row("Beta", 1) };
        WithGrid(rows, (grid, columns) =>
        {
            grid.SelectionMode = DataGridSelectionMode.Extended;
            grid.SelectedValuePath = nameof(Row.Rank);
            Sort(grid, columns[0]); Layout(grid);
            grid.SelectedItems.Add(rows[0]);
            Assert.Equal(1, grid.SelectedIndex);
            Assert.Equal(2, grid.SelectedValue);
            grid.SelectedItems.Add(rows[1]);
            Assert.Equal(2, grid.SelectedItems.Count);
            grid.SelectedItems.Remove(rows[0]);
            Assert.Equal(0, grid.SelectedIndex);
            Assert.Same(rows[1], grid.SelectedItem);
            Assert.Equal(1, grid.SelectedValue);
            grid.SelectedItems.Clear();
            Assert.Equal(-1, grid.SelectedIndex);
            Assert.Null(grid.SelectedItem);
            Assert.Null(grid.SelectedValue);
        });
    }

    [Fact]
    public void HeaderSortStatesSupportConsumerGlyphsAndTemplatesWithoutIndicators()
    {
        WithGrid(new[] { new Row("z", 2), new Row("Beta", 1) }, (grid, columns) =>
        {
            var headers = Assert.IsType<StackPanel>(grid.FindName("PART_ColumnHeadersHost"));
            var header = headers.Children.OfType<Jalium.UI.Controls.Primitives.DataGridColumnHeader>().First();
            header.Template = Assert.IsType<ControlTemplate>(Jalium.UI.Markup.XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="DataGridColumnHeader">
                    <Grid>
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="SortStates">
                                <VisualState x:Name="Unsorted" />
                                <VisualState x:Name="SortAscending"><VisualState.Setters>
                                    <Setter TargetName="ConsumerSymbol" Property="Text" Value="UP" />
                                </VisualState.Setters></VisualState>
                                <VisualState x:Name="SortDescending"><VisualState.Setters>
                                    <Setter TargetName="ConsumerSymbol" Property="Text" Value="DOWN" />
                                </VisualState.Setters></VisualState>
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>
                        <TextBlock Name="ConsumerSymbol" Text="IDLE" />
                    </Grid>
                </ControlTemplate>
                """));
            Layout(grid);
            Assert.Equal("IDLE", Assert.IsType<TextBlock>(header.FindName("ConsumerSymbol")).Text);
            Sort(grid, columns[0]); Layout(grid);
            Assert.Equal("UP", Assert.IsType<TextBlock>(header.FindName("ConsumerSymbol")).Text);
            Sort(grid, columns[0]); Layout(grid);
            Assert.Equal("DOWN", Assert.IsType<TextBlock>(header.FindName("ConsumerSymbol")).Text);
            header.Template = new ControlTemplate(typeof(Jalium.UI.Controls.Primitives.DataGridColumnHeader));
            header.Template.SetVisualTree(static () => new Border());
            Layout(grid); Sort(grid, columns[0]); Layout(grid);
            Assert.Null(header.FindName("ConsumerSymbol"));
        });
    }

    private static void Begin(DataGrid grid, object item, DataGridColumn column, int index)
    {
        grid.SelectedIndex = index;
        grid.CurrentCell = new DataGridCellInfo(item, column);
        _ = Keyboard.PrimaryDevice;
        Assert.True(grid.BeginEdit());
    }
    private static void SendKey(DataGrid grid, Key key, bool preview = false, bool backwards = false)
    {
        var editor = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
        editor.RaiseEvent(new KeyEventArgs(new ShiftKeyboard(backwards), new KeySource { RootVisual = grid }, 1, key)
            { RoutedEvent = preview ? UIElement.PreviewKeyDownEvent : UIElement.KeyDownEvent, Source = editor });
    }
    private sealed class ShiftKeyboard(bool backwards) : KeyboardDevice
    {
        public override IInputElement? Target => null;
        public override PresentationSource? ActiveSource => null;
        protected override KeyStates GetKeyStatesFromSystem(Key key) =>
            backwards && key is Key.LeftShift or Key.RightShift ? KeyStates.Down : KeyStates.None;
    }
    private sealed class KeySource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }

    private static void WithGrid(IEnumerable<Row> source, Action<DataGrid, DataGridColumn[]> action, bool manual = false)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        var app = Application.Current ?? new Application();
        var theme = ThemeManager.CurrentTheme;
        try
        {
            ThemeManager.ApplyTheme(ThemeVariant.Light);
            var grid = new DataGrid { AutoGenerateColumns = false };
            DataGridColumn[] columns = [new DataGridTextColumn { Header = "名称", Binding = new Binding(nameof(Row.Name)), Width = 150 },
                new DataGridTextColumn { Header = "序号", Binding = new Binding(nameof(Row.Rank)), Width = 100 }];
            foreach (var column in columns) grid.Columns.Add(column);
            if (manual) foreach (var item in source) grid.Items.Add(item); else grid.ItemsSource = source;
            Layout(grid);
            action(grid, columns);
        }
        finally { ThemeManager.ApplyTheme(theme); GC.KeepAlive(app); }
    }
    private static DataGridItemAutomationPeer[] Peers(DataGridAutomationPeer peer) => peer.GetChildren().OfType<DataGridItemAutomationPeer>().ToArray();
    private static string[] Names(DataGridAutomationPeer peer) => Peers(peer).Select(row => row.GetChildren()[0].GetName()).ToArray();
    private static void Sort(DataGrid grid, DataGridColumn column) => typeof(DataGrid)
        .GetMethod("SortByColumn", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, [column]);
    private static AutomationPeer Unwrap(IRawElementProviderSimple provider) => new ProviderInspector(new FrameworkElement()).Unwrap(provider);
    private static void Layout(UIElement element)
    {
        element.Measure(new Size(480, 250)); element.Arrange(new Rect(0, 0, 480, 250)); element.UpdateLayout();
    }
    private sealed class ProviderInspector(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
    {
        internal AutomationPeer Unwrap(IRawElementProviderSimple provider) => PeerFromProvider(provider);
    }
    private class Row(string name, int rank)
    {
        public virtual string Name { get; set; } = name;
        public int Rank { get; set; } = rank;
    }
    private sealed class CallbackRow(string name, int rank) : Row(name, rank)
    {
        private string _name = name;
        public Action? Changed { get; set; }
        public override string Name { get => _name; set { _name = value; Changed?.Invoke(); } }
    }
    private sealed class EqualRow(string name, int rank) : Row(name, rank)
    {
        public override bool Equals(object? obj) => obj is EqualRow;
        public override int GetHashCode() => 0;
    }
    private readonly record struct ValueRow(string Name, int Rank);
}
