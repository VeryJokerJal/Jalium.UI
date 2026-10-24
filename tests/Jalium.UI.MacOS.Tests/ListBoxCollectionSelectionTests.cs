using System.Collections;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ListBoxCollectionSelectionTests
{
    [Theory]
    [InlineData(false, SelectionMode.Multiple)]
    [InlineData(true, SelectionMode.Multiple)]
    [InlineData(false, SelectionMode.Extended)]
    [InlineData(true, SelectionMode.Extended)]
    public void RemovingSelectedNonPrimaryItemDropsOnlyThatSelection(bool explicitItems, SelectionMode mode)
    {
        var list = Create(explicitItems, mode);
        var changes = new List<SelectionChangedEventArgs>();
        list.SelectionChanged += (_, e) => changes.Add(e);
        list.Items.RemoveAt(2);
        Assert.Equal(new object[] { "first" }, list.SelectedItems.Cast<object>());
        Assert.Equal("first", list.SelectedItem);
        Assert.Equal(0, list.SelectedIndex);
        Assert.Single(changes);
        Assert.Equal(new object[] { "third" }, changes[0].RemovedItems.Cast<object>());
        Assert.Empty(changes[0].AddedItems);
    }

    [Theory]
    [InlineData(false, SelectionMode.Multiple)]
    [InlineData(true, SelectionMode.Multiple)]
    [InlineData(false, SelectionMode.Extended)]
    [InlineData(true, SelectionMode.Extended)]
    public void InsertingBeforePrimaryPreservesSelectionIdentityAndDoesNotRaiseADelta(bool explicitItems, SelectionMode mode)
    {
        var list = Create(explicitItems, mode);
        int changes = 0;
        list.SelectionChanged += (_, _) => changes++;
        list.Items.Insert(0, explicitItems ? new ListBoxItem { Content = "zeroth" } : "zeroth");
        Assert.Equal(new object[] { "first", "third" }, list.SelectedItems.Cast<object>());
        Assert.Equal("first", list.SelectedItem);
        Assert.Equal("first", list.SelectedValue);
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(false, SelectionMode.Multiple)]
    [InlineData(true, SelectionMode.Multiple)]
    [InlineData(false, SelectionMode.Extended)]
    [InlineData(true, SelectionMode.Extended)]
    public void RemovingPrimaryRetainsOtherValidSelections(bool explicitItems, SelectionMode mode)
    {
        var list = Create(explicitItems, mode);
        var changes = new List<SelectionChangedEventArgs>();
        list.SelectionChanged += (_, e) => changes.Add(e);
        list.Items.RemoveAt(0);
        Assert.Equal(new object[] { "third" }, list.SelectedItems.Cast<object>());
        Assert.Equal("third", list.SelectedItem);
        Assert.Equal(1, list.SelectedIndex);
        Assert.Single(changes);
        Assert.Equal(new object[] { "first" }, changes[0].RemovedItems.Cast<object>());
        Assert.Empty(changes[0].AddedItems);
    }

    [Theory]
    [InlineData(false, SelectionMode.Single)]
    [InlineData(true, SelectionMode.Single)]
    [InlineData(false, SelectionMode.Multiple)]
    [InlineData(true, SelectionMode.Multiple)]
    [InlineData(false, SelectionMode.Extended)]
    [InlineData(true, SelectionMode.Extended)]
    public void ReplacingTheSelectedItemPreservesTheExistingSelectorBehavior(bool explicitItems, SelectionMode mode)
    {
        var list = new InputList { SelectionMode = mode };
        list.Items.Add(explicitItems ? new ListBoxItem { Content = "old" } : "old");
        list.Items.Add(explicitItems ? new ListBoxItem { Content = "other" } : "other");
        Assert.True(list.Seed(new object[] { "old" }));
        var changes = new List<SelectionChangedEventArgs>();
        list.SelectionChanged += (_, e) => changes.Add(e);
        list.Items[0] = explicitItems ? new ListBoxItem { Content = "replacement" } : "replacement";
        Assert.Equal(new object[] { "replacement" }, list.SelectedItems.Cast<object>());
        Assert.Equal("replacement", list.SelectedItem);
        Assert.Equal(0, list.SelectedIndex);
        Assert.Single(changes);
        Assert.Equal(new object[] { "old" }, changes[0].RemovedItems.Cast<object>());
        Assert.Equal(new object[] { "replacement" }, changes[0].AddedItems.Cast<object>());
    }

    [Theory]
    [InlineData(false, SelectionMode.Multiple)]
    [InlineData(true, SelectionMode.Multiple)]
    [InlineData(false, SelectionMode.Extended)]
    [InlineData(true, SelectionMode.Extended)]
    public void ClearRemovesEverySelectionInOneNotification(bool explicitItems, SelectionMode mode)
    {
        var list = Create(explicitItems, mode);
        var changes = new List<SelectionChangedEventArgs>();
        list.SelectionChanged += (_, e) => changes.Add(e);
        list.Items.Clear();
        Assert.Empty(list.SelectedItems);
        Assert.Null(list.SelectedItem);
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Single(changes);
        Assert.Equal(new object[] { "first", "third" }, changes[0].RemovedItems.Cast<object>());
        Assert.Empty(changes[0].AddedItems);
    }

    private static InputList Create(bool explicitItems, SelectionMode mode)
    {
        var list = new InputList { SelectionMode = mode };
        foreach (var value in new[] { "first", "second", "third" })
            list.Items.Add(explicitItems ? new ListBoxItem { Content = value } : value);
        Assert.True(list.Seed(new object[] { "first", "third" }));
        Assert.Equal(new object[] { "first", "third" }, list.SelectedItems.Cast<object>());
        Assert.Equal("first", list.SelectedItem);
        return list;
    }

    private sealed class InputList : ListBox
    {
        public bool Seed(IEnumerable items) => SetSelectedItems(items);
    }
}

