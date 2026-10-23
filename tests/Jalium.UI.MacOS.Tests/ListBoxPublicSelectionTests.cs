using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ListBoxPublicSelectionTests
{
    [Theory]
    [InlineData(SelectionMode.Single, false)] [InlineData(SelectionMode.Multiple, false)] [InlineData(SelectionMode.Extended, false)]
    [InlineData(SelectionMode.Single, true)] [InlineData(SelectionMode.Multiple, true)] [InlineData(SelectionMode.Extended, true)]
    public void PublicIndexAndItemKeepTheLogicalSelectionCollectionInSync(SelectionMode mode, bool ownContainers)
    {
        var list = Create(mode, ownContainers);
        list.SelectedIndex = 1;
        object beta = ownContainers ? list.Items[1] : "Beta";
        Assert.Equal(beta, list.SelectedItem);
        Assert.Equal(new[] { beta }, list.SelectedItems.Cast<object>());
        Assert.Equal("Beta", Assert.Single(((ListBoxAutomationPeer)list.GetAutomationPeer()!).GetSelection()).GetName());
        list.SelectedItem = ownContainers ? list.Items[2] : "Gamma";
        object gamma = ownContainers ? list.Items[2] : "Gamma";
        Assert.Equal(2, list.SelectedIndex); Assert.Equal(gamma, list.SelectedItem);
        Assert.Equal(new[] { gamma }, list.SelectedItems.Cast<object>());
        list.SelectedIndex = -1;
        Assert.Null(list.SelectedItem); Assert.Empty(list.SelectedItems);
    }

    [Theory]
    [InlineData(SelectionMode.Multiple)] [InlineData(SelectionMode.Extended)]
    public void PublicScalarSelectionReplacesTheOldMultipleSelection(SelectionMode mode)
    {
        var list = Create(mode, true);
        Assert.True(list.Choose("Alpha", "Gamma"));
        list.SelectedIndex = 2;
        Assert.Equal(new object[] { list.Items[2] }, list.SelectedItems.Cast<object>());
        Assert.Equal(2, list.SelectedIndex);
    }

    [Fact]
    public void SelectionChangedHandlersObserveCommittedStateAndCanChooseANewerSelection()
    {
        var list = Create(SelectionMode.Multiple, true);
        var observed = new List<object[]>();
        list.SelectionChanged += (_, _) =>
        {
            observed.Add(list.SelectedItems.Cast<object>().ToArray());
            if (ReferenceEquals(list.SelectedItem, list.Items[1])) list.SelectedIndex = 2;
        };
        list.SelectedIndex = 1;
        Assert.Equal(new object[] { list.Items[1] }, observed[0]);
        Assert.Equal(new object[] { list.Items[2] }, observed[^1]);
        Assert.Same(list.Items[2], list.SelectedItem); Assert.Equal(2, list.SelectedIndex);
        Assert.Equal(new object[] { list.Items[2] }, list.SelectedItems.Cast<object>());
    }

    [Fact]
    public void ACompletedBatchAllowsItsHandlerToApplyANewerBatch()
    {
        var list = Create(SelectionMode.Multiple, false);
        list.SelectionChanged += (_, _) =>
        {
            if (Equals(list.SelectedItem, "Beta")) Assert.True(list.Choose("Gamma"));
        };
        Assert.True(list.Choose("Beta"));
        Assert.Equal("Gamma", list.SelectedItem);
        Assert.Equal(new object[] { "Gamma" }, list.SelectedItems.Cast<object>());
    }

    private static SelectionList Create(SelectionMode mode, bool own)
    {
        var list = new SelectionList { SelectionMode = mode };
        foreach (string text in new[] { "Alpha", "Beta", "Gamma" })
            list.Items.Add(own ? new ListBoxItem { Content = text } : text);
        return list;
    }
    private sealed class SelectionList : ListBox
    {
        internal bool Choose(params object[] values) => SetSelectedItems(values);
    }
}
