using System.Collections.ObjectModel;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ComboBoxInvocationRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvokingAnOptionCommitsAndClosesWhileSelectionRemainsIndependent(bool own)
    {
        var combo = Create(own);
        var peer = combo.GetAutomationPeer()!.GetChildren()[1];
        var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(peer.GetPattern(PatternInterface.SelectionItem));
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
        combo.IsDropDownOpen = true;
        selection.Select();
        Assert.Equal(1, combo.SelectedIndex);
        Assert.True(combo.IsDropDownOpen);
        invoke.Invoke();
        Assert.Equal(1, combo.SelectedIndex);
        Assert.False(combo.IsDropDownOpen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledOptionsCannotBeInvoked(bool ownerDisabled)
    {
        var combo = Create(true);
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(combo.GetAutomationPeer()!.GetChildren()[1].GetPattern(PatternInterface.Invoke));
        if (ownerDisabled) combo.IsEnabled = false;
        else ((ComboBoxItem)combo.Items[1]!).IsEnabled = false;
        Assert.Throws<InvalidOperationException>(invoke.Invoke);
        Assert.Equal(-1, combo.SelectedIndex);
    }

    [Fact]
    public void RetiredOptionCannotCommitANewerPopupAfterTheSelectionCallback()
    {
        var items = new ObservableCollection<object> { "first", "second" };
        var combo = new ComboBox { ItemsSource = items };
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(combo.GetAutomationPeer()!.GetChildren()[1].GetPattern(PatternInterface.Invoke));
        combo.IsDropDownOpen = true;
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex == 1) items.Clear(); };
        invoke.Invoke();
        Assert.Equal(-1, combo.SelectedIndex);
        Assert.Empty(items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealizedPopupRowInvokesTheLogicalOccurrenceAndCloses(bool own)
    {
        var combo = Create(own);
        var row = Realize(combo, 1);
        var peer = row.GetAutomationPeer()!;
        var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(peer.GetPattern(PatternInterface.SelectionItem));
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
        combo.IsDropDownOpen = true;
        selection.Select();
        Assert.Equal(1, combo.SelectedIndex);
        Assert.True(combo.IsDropDownOpen);
        invoke.Invoke();
        Assert.Equal(1, combo.SelectedIndex);
        Assert.False(combo.IsDropDownOpen);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RealizedPopupRowCannotCommitWhenTheRowOrOwnerIsDisabled(bool own, bool ownerDisabled)
    {
        var combo = Create(own);
        var row = Realize(combo, 1);
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(row.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke));
        if (ownerDisabled) combo.IsEnabled = false;
        else row.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(invoke.Invoke);
        Assert.Equal(-1, combo.SelectedIndex);
    }

    [Fact]
    public void RetainedRealizedProviderCannotCommitAReplacementOccurrence()
    {
        var source = new ObservableCollection<object> { "first", "second" };
        var combo = new ComboBox { ItemsSource = source };
        var row = Realize(combo, 1);
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(row.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke));
        source.Clear();
        source.Add("first");
        source.Add("second");
        Assert.Throws<InvalidOperationException>(invoke.Invoke);
        Assert.Equal(-1, combo.SelectedIndex);
    }

    [Fact]
    public void PlainListRowsDoNotAdvertiseComboBoxCommit()
    {
        var list = new ListBox { Items = { new ListBoxItem { Content = "plain row" } } };
        var row = (ListBoxItem)list.Items[0]!;
        Assert.Null(row.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke));
    }

    private static ComboBoxItem Realize(ComboBox combo, int index)
    {
        var generator = combo.ItemContainerGenerator;
        using (generator.StartAt(generator.GeneratorPositionFromIndex(0), GeneratorDirection.Forward, true))
            for (int current = 0; current < combo.Items.Count; current++)
            {
                var row = Assert.IsAssignableFrom<ComboBoxItem>(generator.GenerateNext(out _));
                generator.PrepareItemContainer(row);
            }
        return Assert.IsAssignableFrom<ComboBoxItem>(generator.ContainerFromIndex(index));
    }

    private static ComboBox Create(bool own) => own
        ? new ComboBox { Items = { new ComboBoxItem { Content = "first" }, new ComboBoxItem { Content = "second" } } }
        : new ComboBox { ItemsSource = new object[] { "first", "second" } };
}
