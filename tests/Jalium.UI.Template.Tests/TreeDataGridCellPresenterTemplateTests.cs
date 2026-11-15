using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class TreeDataGridCellPresenterTemplateTests
{
    [Fact]
    public void ColumnUpdatesPreserveHeadersAndTemplateRetirementReleasesOwners()
    {
        Initialize();
        var grid = new TreeDataGrid { ItemsSource = new[] { new Node("Root", []) } };
        var column = new DataGridTextColumn { Header = "Name", Binding = new Jalium.UI.Data.Binding(nameof(Node.Name)), Width = 120 };
        grid.Columns.Add(column); Layout(grid);
        var header = Descendants<DataGridColumnHeader>(grid).Single();
        var row = Descendants<TreeDataGridRow>(grid).Single();
        var cell = Descendants<DataGridCell>(row).Single();
        column.Header = "名称";
        column.Width = 180;
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(50, 110, 70))));
        column.CellStyle = style;
        Layout(grid);
        Assert.Same(header, Descendants<DataGridColumnHeader>(grid).Single());
        Assert.Equal("名称", header.Content);
        Assert.Equal(180, header.Width);
        Assert.Equal(180, cell.Width);
        Assert.Same(style, cell.Style);
        grid.Template = null;
        Layout(grid);
        Assert.Null(header.Column);
        Assert.Null(row.ParentTreeDataGrid);
        Assert.Empty(Descendants<DataGridCell>(row));
    }

    [Fact]
    public void TreeCommandsTrackDataExpansionAndEnabledState()
    {
        var grid = new TreeDataGrid();
        Assert.False(grid.ExpandAllCommand.CanExecute(null));
        grid.ChildrenSelector = item => ((Node)item).Children;
        grid.ItemsSource = new[] { new Node("Root", [new Node("Child", [])]) };
        Assert.True(grid.ExpandAllCommand.CanExecute(null));
        Assert.False(grid.CollapseAllCommand.CanExecute(null));
        grid.ExpandAllCommand.Execute(null);
        Assert.False(grid.ExpandAllCommand.CanExecute(null));
        Assert.True(grid.CollapseAllCommand.CanExecute(null));
        grid.CollapseAllCommand.Execute(null);
        Assert.True(grid.ExpandAllCommand.CanExecute(null));
        grid.IsEnabled = false;
        Assert.False(grid.ExpandAllCommand.CanExecute(null));
        grid.IsEnabled = true;
        grid.ItemsSource = Array.Empty<Node>();
        Assert.False(grid.ExpandAllCommand.CanExecute(null));
        Assert.False(grid.CollapseAllCommand.CanExecute(null));
    }

    [Fact]
    public void ButtonFocusDefaultsRemainStyleOverridable()
    {
        var button = new Button();
        Assert.True(button.Focusable);
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        button.Style = style;
        Assert.False(button.Focusable);
        button.Focusable = true;
        Assert.True(button.Focusable);
    }

    [Fact]
    public void PresenterUsesTemplateIndentationAndOptionalParts()
    {
        Initialize();
        var presenter = new TreeDataGridCellPresenter { Level = 3, IndentSize = 18, Content = new TextBlock { Text = "Node" } };
        Assert.Equal(0, VisualTreeHelper.GetChildrenCount(presenter));
        Layout(presenter);
        var spacer = Assert.IsType<Grid>(presenter.FindName("PART_IndentSpacer"));
        var toggle = Assert.IsType<ToggleButton>(presenter.FindName("PART_Expander"));
        Assert.Equal(54, spacer.Width);
        Assert.Equal(Visibility.Hidden, toggle.Visibility);
        Assert.False(toggle.Focusable);
        presenter.HasChildren = true;
        Layout(presenter);
        Assert.Equal(16, toggle.ActualWidth);
        Assert.Equal(16, toggle.ActualHeight);
        Assert.Equal("Node", AutomationProperties.GetName(toggle));
        presenter.Level = -1;
        Assert.Equal(0, presenter.IndentWidth);
        presenter.IndentSize = double.NaN;
        Assert.Equal(0, presenter.IndentSize);
        var minimal = new ControlTemplate(typeof(TreeDataGridCellPresenter));
        minimal.SetVisualTree(() => new Grid());
        presenter.Template = minimal;
        presenter.HasChildren = true;
        presenter.IsExpanded = true;
        Layout(presenter);
        Assert.Null(presenter.FindName("PART_Expander"));
    }

    [Fact]
    public void ReplacingTemplateRetiresOldToggleAndKeepsExpansionState()
    {
        Initialize();
        var presenter = new TreeDataGridCellPresenter { HasChildren = true };
        Layout(presenter);
        var original = presenter.Template;
        var oldToggle = Assert.IsType<ToggleButton>(presenter.FindName("PART_Expander"));
        oldToggle.IsChecked = true;
        Assert.True(presenter.IsExpanded);
        var minimal = new ControlTemplate(typeof(TreeDataGridCellPresenter));
        minimal.SetVisualTree(() => new Grid());
        presenter.Template = minimal;
        Layout(presenter);
        oldToggle.IsChecked = false;
        Assert.True(presenter.IsExpanded);
        presenter.Template = original;
        Layout(presenter);
        var newToggle = Assert.IsType<ToggleButton>(presenter.FindName("PART_Expander"));
        Assert.NotSame(oldToggle, newToggle);
        Assert.True(newToggle.IsChecked);
        presenter.IsEnabled = false;
        newToggle.IsChecked = false;
        Assert.True(presenter.IsExpanded);
        Assert.True(newToggle.IsChecked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpansionUpdatesTheTreeAndCancelableRequestsRestoreTheToggle(bool cancel)
    {
        Initialize();
        var root = new Node("Root", [new Node("Child", [])]);
        var grid = new TreeDataGrid { ItemsSource = new[] { root }, ChildrenSelector = item => ((Node)item).Children };
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Jalium.UI.Data.Binding(nameof(Node.Name)), Width = 250 });
        if (cancel) grid.NodeExpanding += (_, e) => e.Cancel = true;
        Layout(grid);
        var presenter = Descendants<TreeDataGridCellPresenter>(grid).Single();
        Assert.IsType<ToggleButton>(presenter.FindName("PART_Expander")).IsChecked = true;
        Layout(grid);
        var current = Descendants<TreeDataGridCellPresenter>(grid).ToArray();
        Assert.Equal(cancel ? 1 : 2, current.Length);
        Assert.Equal(!cancel, current[0].IsExpanded);
        Assert.Equal(!cancel, Assert.IsType<ToggleButton>(current[0].FindName("PART_Expander")).IsChecked);
        if (!cancel)
        {
            Assert.Equal(grid.IndentSize, current[1].IndentWidth);
            Assert.IsType<ToggleButton>(current[0].FindName("PART_Expander")).IsChecked = false;
            Layout(grid);
            Assert.Single(Descendants<TreeDataGridCellPresenter>(grid));
        }
    }

    [Fact]
    public void AutomationExpandsBranchesAndReportsLeavesAndDisabledState()
    {
        var presenter = new TreeDataGridCellPresenter { HasChildren = true, Content = new TextBlock { Text = "Folder" } };
        var peer = Assert.IsType<TreeDataGridCellPresenterAutomationPeer>(presenter.GetAutomationPeer());
        var pattern = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
        Assert.Equal("Folder", peer.GetName());
        Assert.Equal(ExpandCollapseState.Collapsed, pattern.ExpandCollapseState);
        pattern.Expand();
        Assert.True(presenter.IsExpanded);
        pattern.Collapse();
        Assert.False(presenter.IsExpanded);
        presenter.HasChildren = false;
        Assert.Equal(ExpandCollapseState.LeafNode, pattern.ExpandCollapseState);
        Assert.Throws<InvalidOperationException>(() => pattern.Expand());
        presenter.HasChildren = true;
        presenter.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(() => pattern.Expand());
    }

    private static void Initialize() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    private static void Layout(UIElement element) { element.Measure(new Size(400, 250)); element.Arrange(new Rect(0, 0, 400, 250)); element.UpdateLayout(); }
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
            if (VisualTreeHelper.GetChild(root, i) is Visual child)
            {
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
    }
    private sealed record Node(string Name, Node[] Children);
}
