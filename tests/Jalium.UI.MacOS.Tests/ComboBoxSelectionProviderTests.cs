using System.Collections.ObjectModel;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ComboBoxSelectionProviderTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void SelectedLogicalItemIsReturnedBeforeRealization(bool own)
    {
        var combo = Create(own);
        var peer = (ComboBoxAutomationPeer)combo.GetAutomationPeer()!;
        var children = peer.GetChildren();
        Assert.Empty(peer.GetSelection());
        Assert.All(children, child => Assert.Equal(AutomationControlType.ListItem, child.GetAutomationControlType()));
        combo.SelectedIndex = 1;
        Assert.Same(children[1], Assert.Single(peer.GetSelection()));
        Assert.True(Selection(children[1]).IsSelected);
        Assert.Same(peer, ((IAutomationPeerRawProvider)Selection(children[1]).SelectionContainer!).Peer);
        var selected = ((ISelectionProvider)peer).GetSelection();
        Assert.Same(children[1], ((IAutomationPeerRawProvider)Assert.Single(selected)).Peer);
        combo.SelectedIndex = -1;
        Assert.Empty(peer.GetSelection());
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void LogicalActionsCommitThePublicSingleSelection(bool own)
    {
        var combo = Create(own);
        var peer = (ComboBoxAutomationPeer)combo.GetAutomationPeer()!;
        var children = peer.GetChildren();
        var events = new List<object?>();
        combo.SelectionChanged += (_, _) => events.Add(combo.SelectedItem);
        Selection(children[1]).Select();
        Assert.Equal(1, combo.SelectedIndex); Assert.Same(combo.Items[1], combo.SelectedItem);
        Assert.True(Selection(children[1]).IsSelected); Assert.False(Selection(children[0]).IsSelected);
        Selection(children[2]).AddToSelection();
        Assert.Equal(2, combo.SelectedIndex); Assert.True(Selection(children[2]).IsSelected);
        Assert.False(Selection(children[1]).IsSelected);
        Selection(children[1]).RemoveFromSelection(); Assert.Equal(2, combo.SelectedIndex);
        Selection(children[2]).RemoveFromSelection(); Assert.Equal(-1, combo.SelectedIndex);
        Assert.Null(combo.SelectedItem); Assert.Empty(peer.GetSelection()); Assert.Equal(3, events.Count);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void DisabledOwnerRejectsEveryLogicalSelectionAction(bool own)
    {
        var combo = Create(own); combo.SelectedIndex = 0;
        var item = Selection(combo.GetAutomationPeer()!.GetChildren()[1]);
        combo.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(() => item.Select());
        Assert.Throws<InvalidOperationException>(() => item.AddToSelection());
        Assert.Throws<InvalidOperationException>(() => item.RemoveFromSelection());
        Assert.Equal(0, combo.SelectedIndex);
    }

    [Fact]
    public void DisabledOwnContainerCannotBeSelectedThroughItsLogicalPeer()
    {
        var combo = Create(true); combo.SelectedIndex = 0;
        ((ComboBoxItem)combo.Items[1]!).IsEnabled = false;
        var item = Selection(combo.GetAutomationPeer()!.GetChildren()[1]);
        Assert.Throws<InvalidOperationException>(() => item.Select()); Assert.Equal(0, combo.SelectedIndex);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AccessibleValueAndRowsUseTheDisplayedItemText(bool own)
    {
        var combo = Create(own); combo.DisplayMemberPath = nameof(Entry.Caption); combo.SelectedIndex = 1;
        var peer = combo.GetAutomationPeer()!;
        Assert.Equal("中文🙂", ((IValueProvider)peer).Value);
        Assert.Equal("中文🙂", peer.GetChildren()[1].GetName());
        Assert.True(((IValueProvider)peer).IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => ((IValueProvider)peer).SetValue("other"));
    }

    [Fact]
    public void OwnItemNameDoesNotReplaceItsVisibleValue()
    {
        var item = new ComboBoxItem { Content = "visible 中文🙂" };
        AutomationProperties.SetName(item, "semantic name");
        var combo = new ComboBox { Items = { item }, SelectedIndex = 0 };
        var peer = combo.GetAutomationPeer()!;
        Assert.Equal("semantic name", peer.GetChildren()[0].GetName());
        Assert.Equal("visible 中文🙂", ((IValueProvider)peer).Value);
    }

    [Fact]
    public void TextSearchPathPrecedesDisplayMemberForSelectionText()
    {
        var combo = Create(false); combo.DisplayMemberPath = nameof(Entry.Id);
        TextSearch.SetTextPath(combo, nameof(Entry.Caption)); combo.SelectedIndex = 1;
        var peer = combo.GetAutomationPeer()!;
        Assert.Equal("中文🙂", ((IValueProvider)peer).Value);
        Assert.Equal("中文🙂", peer.GetChildren()[1].GetName());
    }

    [Fact]
    public void ClearingNonEditableSelectionClearsItsAccessibleValue()
    {
        var combo = new ComboBox { Items = { "first", "中文🙂" }, SelectedIndex = 1 };
        var value = (IValueProvider)combo.GetAutomationPeer()!; Assert.Equal("中文🙂", value.Value);
        combo.SelectedIndex = -1;
        Assert.Equal(string.Empty, value.Value); Assert.Equal(string.Empty, combo.Text);
    }

    [Fact]
    public void EditableValueRemainsEditableWhenTheSelectionIsCleared()
    {
        var combo = new ComboBox { IsEditable = true, Items = { "first", "中文🙂" } };
        var value = (IValueProvider)combo.GetAutomationPeer()!;
        Assert.False(value.IsReadOnly); value.SetValue("中文🙂"); Assert.Equal(1, combo.SelectedIndex);
        combo.SelectedIndex = -1; Assert.Equal("中文🙂", value.Value);
        value.SetValue("custom 🧪"); Assert.Equal(-1, combo.SelectedIndex); Assert.Equal("custom 🧪", combo.Text);
    }

    [Theory] [InlineData("redirect")] [InlineData("clear")] [InlineData("throw")]
    public void SelectionCallbacksKeepTheirCommittedOrNewerState(string kind)
    {
        var combo = Create(false); var item = Selection(combo.GetAutomationPeer()!.GetChildren()[1]);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex != 1) return;
            if (kind == "redirect") combo.SelectedIndex = 2;
            else if (kind == "clear") ((ObservableCollection<object>)combo.ItemsSource!).Clear();
            else throw new ApplicationException("handler");
        };
        if (kind == "throw") Assert.Throws<ApplicationException>(() => item.Select()); else item.Select();
        Assert.Equal(kind == "redirect" ? 2 : kind == "clear" ? -1 : 1, combo.SelectedIndex);
        Assert.Equal(kind != "clear", combo.SelectedItem != null);
    }

    [Fact]
    public void RemovedItemCannotAffectTheSurvivingSelection()
    {
        var entries = new ObservableCollection<object> { new Entry(0, "first"), new Entry(1, "second") };
        var combo = new ComboBox { ItemsSource = entries }; var peer = (ComboBoxAutomationPeer)combo.GetAutomationPeer()!;
        var old = Selection(peer.GetChildren()[0]); entries.RemoveAt(0); combo.SelectedIndex = 0;
        var error = Record.Exception(() => old.Select()); Assert.True(error is null or InvalidOperationException);
        Assert.Same(entries[0], combo.SelectedItem); Assert.False(old.IsSelected);
    }

    [Fact]
    public void SelectionTextUsesExplicitVisualContent()
    {
        var combo = new ComboBox { Items = { new ComboBoxItem { Content = new TextBlock { Text = "visible 中文🙂" } } }, SelectedIndex = 0 };
        var peer = combo.GetAutomationPeer()!;
        Assert.Equal("visible 中文🙂", peer.GetChildren()[0].GetName());
        Assert.Equal("visible 中文🙂", ((IValueProvider)peer).Value);
    }

    [Fact]
    public void ItemChangesRefreshLogicalChildrenAndRetireRemovedItems()
    {
        var items = new ObservableCollection<object> { "first", "second" };
        var combo = new ComboBox { ItemsSource = items };
        var peer = combo.GetAutomationPeer()!; var original = peer.GetChildren();
        items.RemoveAt(0); items.Add("third");
        Assert.Equal(new[] { "second", "third" }, peer.GetChildren().Select(x => x.GetName()));
        Assert.Same(original[1], peer.GetChildren()[0]);
    }

    [Fact]
    public void OwnContainerStateFollowsSelectionBeforeItsPopupExists()
    {
        var combo = Create(true);
        combo.SelectedIndex = 0; Assert.True(((ComboBoxItem)combo.Items[0]!).IsSelected);
        combo.SelectedIndex = 1; Assert.False(((ComboBoxItem)combo.Items[0]!).IsSelected);
        Assert.True(((ComboBoxItem)combo.Items[1]!).IsSelected);
        combo.SelectedIndex = -1; Assert.False(((ComboBoxItem)combo.Items[1]!).IsSelected);
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void EqualDisplayStringsDoNotRetargetAnExplicitSelection(bool editable, bool own)
    {
        object first = own ? new ComboBoxItem { Content = "same" } : new Entry(1, "same");
        object second = own ? new ComboBoxItem { Content = "same" } : new Entry(2, "same");
        var combo = new ComboBox { IsEditable = editable, ItemsSource = new[] { first, second } };
        combo.SelectedIndex = 1;
        Assert.Equal(1, combo.SelectedIndex); Assert.Same(second, combo.SelectedItem);
        Assert.True(Selection(((ComboBoxAutomationPeer)combo.GetAutomationPeer()!).GetSelection()[0]).IsSelected);
    }

    [Fact]
    public void LogicalSelectionUpdatesSelectedValueThroughTheExistingPropertyPath()
    {
        var combo = Create(false); combo.SelectedValuePath = nameof(Entry.Id);
        Selection(combo.GetAutomationPeer()!.GetChildren()[1]).Select();
        Assert.Equal(1, combo.SelectedValue); Assert.Equal(1, combo.SelectedIndex);
        Assert.Same(combo.Items[1], combo.SelectedItem);
    }

    [Theory] [InlineData(UpdateSourceTrigger.Default)] [InlineData(UpdateSourceTrigger.PropertyChanged)]
    [InlineData(UpdateSourceTrigger.LostFocus)] [InlineData(UpdateSourceTrigger.Explicit)]
    public void AutomationSelectionPreservesTheIndexBindingAndUpdatesItsSource(UpdateSourceTrigger trigger)
    {
        var model = new BindingModel(); var combo = Create(false);
        var binding = combo.SetBinding(ComboBox.SelectedIndexProperty,
            new Binding(nameof(BindingModel.Index)) { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = trigger });
        Selection(combo.GetAutomationPeer()!.GetChildren()[1]).Select();
        if (trigger is UpdateSourceTrigger.Default or UpdateSourceTrigger.PropertyChanged)
            Assert.Equal(1, model.Index);
        else
        {
            Assert.Equal(0, model.Index);
            if (trigger == UpdateSourceTrigger.LostFocus) combo.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, combo));
            else binding.UpdateSource();
            Assert.Equal(1, model.Index);
        } Assert.Same(binding, combo.GetBindingExpression(ComboBox.SelectedIndexProperty));
    }

    [Theory] [InlineData(UpdateSourceTrigger.Default)] [InlineData(UpdateSourceTrigger.PropertyChanged)]
    [InlineData(UpdateSourceTrigger.LostFocus)] [InlineData(UpdateSourceTrigger.Explicit)]
    public void AutomationTextPreservesItsBindingAndUpdatesItsSource(UpdateSourceTrigger trigger)
    {
        var model = new BindingModel(); var combo = new ComboBox { IsEditable = true };
        var binding = combo.SetBinding(ComboBox.TextProperty,
            new Binding(nameof(BindingModel.Text)) { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = trigger });
        ((IValueProvider)combo.GetAutomationPeer()!).SetValue("编辑 🧪");
        if (trigger is UpdateSourceTrigger.Default or UpdateSourceTrigger.PropertyChanged)
            Assert.Equal("编辑 🧪", model.Text);
        else
        {
            Assert.Equal(string.Empty, model.Text);
            if (trigger == UpdateSourceTrigger.LostFocus) combo.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, combo));
            else binding.UpdateSource();
            Assert.Equal("编辑 🧪", model.Text);
        } Assert.Same(binding, combo.GetBindingExpression(ComboBox.TextProperty));
    }

    private sealed class BindingModel
    {
        public int Index { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ReadOnlyEditingRejectsValueWritesAndStillAllowsItemSelection(bool own)
    {
        var combo = Create(own); combo.IsEditable = true; combo.IsReadOnly = true; combo.SelectedIndex = 1;
        var peer = combo.GetAutomationPeer()!; var value = (IValueProvider)peer;
        Assert.True(value.IsReadOnly);
        string before = value.Value;
        Assert.Throws<InvalidOperationException>(() => value.SetValue("forbidden")); Assert.Equal(before, value.Value);
        Selection(peer.GetChildren()[2]).Select(); Assert.Equal(2, combo.SelectedIndex);
        combo.IsReadOnly = false; Assert.False(value.IsReadOnly);
        value.SetValue("custom 🧪"); Assert.Equal("custom 🧪", combo.Text); Assert.Equal(-1, combo.SelectedIndex);
    }

    private static ComboBox Create(bool own)
    {
        var entries = new[] { new Entry(0, "first"), new Entry(1, "中文🙂"), new Entry(2, "last") };
        return new ComboBox { ItemsSource = new ObservableCollection<object>(own
            ? entries.Select(x => (object)new ComboBoxItem { Content = x.Caption }).ToArray()
            : entries.Cast<object>().ToArray()) };
    }
    private static ISelectionItemProvider Selection(AutomationPeer peer) =>
        Assert.IsAssignableFrom<ISelectionItemProvider>(peer.GetPattern(PatternInterface.SelectionItem));
    private sealed record Entry(int Id, string Caption)
    {
        public override string ToString() => "opaque model";
    }
}
