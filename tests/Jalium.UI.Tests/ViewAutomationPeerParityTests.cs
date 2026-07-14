using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class ViewAutomationPeerParityTests
{
    [Fact]
    public void ViewBaseAndGridViewExposeProtectedInternalAutomationFactory()
    {
        var baseMethod = typeof(ViewBase).GetMethod(
            "GetAutomationPeer",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            [typeof(ListView)],
            null)!;
        var gridMethod = typeof(GridView).GetMethod(
            "GetAutomationPeer",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            [typeof(ListView)],
            null)!;

        Assert.Equal(typeof(IViewAutomationPeer), baseMethod.ReturnType);
        Assert.True(baseMethod.IsFamilyOrAssembly);
        Assert.True(baseMethod.IsVirtual);
        Assert.Equal(typeof(IViewAutomationPeer), gridMethod.ReturnType);
        Assert.True(gridMethod.IsFamilyOrAssembly);
        Assert.True(gridMethod.IsVirtual);

        var parent = new ListView();
        Assert.Null(new ProbeView().CreateAutomationPeer(parent));

        var gridView = new GridView();
        IViewAutomationPeer first = gridView.GetAutomationPeer(parent);
        IViewAutomationPeer second = gridView.GetAutomationPeer(parent);
        Assert.IsType<GridViewAutomationPeer>(first);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void GridViewAutomationPeerSuppliesGridAndTablePatternsToListView()
    {
        var gridView = new GridView();
        gridView.Columns.Add(new GridViewColumn());
        gridView.Columns.Add(new GridViewColumn());
        var listView = new ListView { View = gridView };
        listView.Items.Add("one");
        listView.Items.Add("two");

        var peer = Assert.IsType<Jalium.UI.Automation.Peers.ListViewAutomationPeer>(
            listView.GetAutomationPeer());

        Assert.Equal(AutomationControlType.DataGrid, peer.GetAutomationControlType());
        var gridPattern = Assert.IsType<GridViewAutomationPeer>(
            peer.GetPattern(PatternInterface.Grid));
        Assert.Same(gridPattern, peer.GetPattern(PatternInterface.Table));
        Assert.Equal(2, gridPattern.RowCount);
        Assert.Equal(2, gridPattern.ColumnCount);

        listView.View = null;

        Assert.Equal(AutomationControlType.List, peer.GetAutomationControlType());
        Assert.Null(peer.GetPattern(PatternInterface.Grid));
    }

    private sealed class ProbeView : ViewBase
    {
        public IViewAutomationPeer? CreateAutomationPeer(ListView parent) =>
            GetAutomationPeer(parent);
    }
}
