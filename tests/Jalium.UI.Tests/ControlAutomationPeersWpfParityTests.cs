using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Raw = Jalium.UI.Automation.Provider;

namespace Jalium.UI.Tests;

public sealed class ControlAutomationPeersWpfParityTests
{
    [Fact]
    public void CoreControlPeers_UseWpfMappedNamespaceAndHierarchy()
    {
        Type[] peers =
        [
            typeof(ButtonAutomationPeer), typeof(ButtonBaseAutomationPeer), typeof(CalendarAutomationPeer),
            typeof(ComboBoxAutomationPeer), typeof(DataGridAutomationPeer), typeof(ItemsControlAutomationPeer),
            typeof(ListBoxAutomationPeer), typeof(ListBoxItemAutomationPeer), typeof(ListViewAutomationPeer),
            typeof(PasswordBoxAutomationPeer), typeof(ProgressBarAutomationPeer), typeof(RangeBaseAutomationPeer),
            typeof(ScrollBarAutomationPeer), typeof(SelectorAutomationPeer), typeof(SelectorItemAutomationPeer),
            typeof(TabControlAutomationPeer), typeof(TabItemAutomationPeer), typeof(TreeViewAutomationPeer),
            typeof(TreeViewItemAutomationPeer), typeof(WindowAutomationPeer),
        ];

        Assert.All(peers, type => Assert.Equal("Jalium.UI.Automation.Peers", type.Namespace));
        Assert.True(typeof(ButtonBaseAutomationPeer).IsAbstract);
        Assert.True(typeof(SelectorAutomationPeer).IsAssignableFrom(typeof(ListBoxAutomationPeer)));
        Assert.True(typeof(SelectorItemAutomationPeer).IsAssignableFrom(typeof(ListBoxItemAutomationPeer)));
        Assert.True(typeof(ListBoxAutomationPeer).IsAssignableFrom(typeof(ListViewAutomationPeer)));
        Assert.True(typeof(RangeBaseAutomationPeer).IsAssignableFrom(typeof(ScrollBarAutomationPeer)));
        Assert.True(typeof(TextAutomationPeer).IsAssignableFrom(typeof(PasswordBoxAutomationPeer)));
    }

    [Fact]
    public void MissingPeerTypes_AreMaterialized()
    {
        string[] names =
        [
            "CalendarButtonAutomationPeer", "ContentTextAutomationPeer", "ContextMenuAutomationPeer",
            "DataGridColumnHeaderItemAutomationPeer", "DataGridColumnHeadersPresenterAutomationPeer",
            "DataGridDetailsPresenterAutomationPeer", "DataGridItemAutomationPeer", "DataGridRowHeaderAutomationPeer",
            "DateTimeAutomationPeer", "DocumentAutomationPeer", "DocumentPageViewAutomationPeer",
            "DocumentViewerBaseAutomationPeer", "FixedPageAutomationPeer", "FlowDocumentPageViewerAutomationPeer",
            "FlowDocumentReaderAutomationPeer", "FlowDocumentScrollViewerAutomationPeer",
            "FrameworkContentElementAutomationPeer", "GridViewCellAutomationPeer", "GridViewColumnHeaderAutomationPeer",
            "GridViewHeaderRowPresenterAutomationPeer", "GridViewItemAutomationPeer", "HyperlinkAutomationPeer",
            "InkPresenterAutomationPeer", "ItemAutomationPeer", "ListBoxItemWrapperAutomationPeer",
            "RangeBaseAutomationPeer", "SelectorAutomationPeer", "SelectorItemAutomationPeer",
            "TabItemWrapperAutomationPeer", "TableAutomationPeer", "TableCellAutomationPeer",
            "TextAutomationPeer", "TextElementAutomationPeer", "TreeViewDataItemAutomationPeer",
            "UIElement3DAutomationPeer",
        ];

        Assembly assembly = typeof(CalendarAutomationPeer).Assembly;
        Assert.All(names, name => Assert.NotNull(assembly.GetType($"Jalium.UI.Automation.Peers.{name}")));
    }

    [Fact]
    public void ItemsControlPeer_CachesItemPeersAndExposesItemContainerPattern()
    {
        var list = new ListBox();
        list.Items.Add("one");
        list.Items.Add("two");

        var peer = Assert.IsType<ListBoxAutomationPeer>(list.GetAutomationPeer());

        Assert.Equal(2, peer.GetChildren().Count);
        Assert.Same(peer.GetChildren()[0], peer.GetChildren()[0]);
        Assert.Same(peer, peer.GetPattern(PatternInterface.Selection));
        Assert.NotNull(peer.GetPattern(PatternInterface.ItemContainer));
    }

    [Fact]
    public void ComboBoxPeer_PreservesSelectionExpandAndEditableValuePatterns()
    {
        var combo = new ComboBox { IsEditable = true, Text = "before" };
        var peer = Assert.IsType<ComboBoxAutomationPeer>(combo.GetAutomationPeer());

        Assert.Same(peer, peer.GetPattern(PatternInterface.ExpandCollapse));
        Assert.Same(peer, peer.GetPattern(PatternInterface.Selection));
        var value = Assert.IsAssignableFrom<IValueProvider>(peer.GetPattern(PatternInterface.Value));
        value.SetValue("after");

        Assert.Equal("after", combo.Text);
    }

    [Fact]
    public void RangeAndPasswordPeers_ExposeWpfSemantics()
    {
        var scrollBar = new ScrollBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 10, Value = 4 };
        var scrollPeer = Assert.IsType<ScrollBarAutomationPeer>(scrollBar.GetAutomationPeer());
        var passwordPeer = Assert.IsType<PasswordBoxAutomationPeer>(new PasswordBox().GetAutomationPeer());

        Assert.Equal(AutomationOrientation.Vertical, scrollPeer.GetOrientation());
        Assert.Equal(4, Assert.IsAssignableFrom<IRangeValueProvider>(scrollPeer.GetPattern(PatternInterface.RangeValue)).Value);
        Assert.True(passwordPeer.IsPassword());
    }

    [Fact]
    public void CalendarPeer_ImplementsGridTableSelectionAndMultipleViewProviders()
    {
        var calendar = new Calendar { DisplayMode = CalendarMode.Month };
        var peer = Assert.IsType<CalendarAutomationPeer>(calendar.GetAutomationPeer());

        var grid = Assert.IsAssignableFrom<Raw.IGridProvider>(peer.GetPattern(PatternInterface.Grid));
        var table = Assert.IsAssignableFrom<Raw.ITableProvider>(peer.GetPattern(PatternInterface.Table));
        var views = Assert.IsAssignableFrom<Raw.IMultipleViewProvider>(peer.GetPattern(PatternInterface.MultipleView));

        Assert.Equal(6, grid.RowCount);
        Assert.Equal(7, grid.ColumnCount);
        Assert.NotNull(grid.GetItem(0, 0));
        Assert.Equal(RowOrColumnMajor.RowMajor, table.RowOrColumnMajor);
        Assert.Equal(3, views.GetSupportedViews().Length);
    }

    [Fact]
    public void DataGridPeer_ExposesGridAndTableDimensions()
    {
        var grid = new DataGrid();
        grid.Columns.Add(new DataGridTextColumn { Header = "Name" });
        grid.Items.Add(new { Name = "Jalium" });

        var peer = Assert.IsType<DataGridAutomationPeer>(grid.GetAutomationPeer());
        var gridProvider = Assert.IsAssignableFrom<Raw.IGridProvider>(peer.GetPattern(PatternInterface.Grid));
        var tableProvider = Assert.IsAssignableFrom<Raw.ITableProvider>(peer.GetPattern(PatternInterface.Table));

        Assert.Equal(1, gridProvider.RowCount);
        Assert.Equal(1, gridProvider.ColumnCount);
        Assert.Single(tableProvider.GetColumnHeaders());
    }
}
