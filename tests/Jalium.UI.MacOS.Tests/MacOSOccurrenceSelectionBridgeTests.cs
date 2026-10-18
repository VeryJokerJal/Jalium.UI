using System.Collections.ObjectModel;
using System.Text.Json;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSOccurrenceSelectionBridgeTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int kind in new[] { 0, 1, 2 })
        foreach (var mode in new[] { SelectionMode.Single, SelectionMode.Multiple, SelectionMode.Extended }) yield return [kind, mode];
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativeSelectionBatchesAddressOccurrencesRatherThanEqualValues(int kind, SelectionMode mode)
    {
        object one = kind == 2 ? new EqualContainer { Content = "重复🙂" } : new Row(7);
        object two = kind == 1 ? one : kind == 2 ? new EqualContainer { Content = "重复🙂" } : new Row(7);
        var list = new ListBox { Height = 180, SelectionMode = mode, ItemsSource = new ObservableCollection<object> { one, two } };
        var window = new DisplayedTestWindow { Width = 400, Height = 300, Content = list, TitleBarStyle = WindowTitleBarStyle.Native };
        MacOSAccessibilityTree? tree = null;
        try
        {
            window.Measure(new Size(400, 300)); window.Arrange(new Rect(0, 0, 400, 300)); window.UpdateLayout();
            Dispatcher.GetForCurrentThread().ProcessQueue();
            tree = new MacOSAccessibilityTree(window);
            var peer = Assert.IsType<ListBoxAutomationPeer>(list.GetAutomationPeer());
            var rows = peer.GetChildren().OfType<ListBoxItemAutomationPeer>().ToArray();
            Assert.Equal(2, rows.Length); Assert.True(tree.TryGetId(peer, out ulong owner));
            Assert.True(tree.TryGetId(rows[0], out ulong first)); Assert.True(tree.TryGetId(rows[1], out ulong second));
            Assert.NotEqual(first, second);
            Assert.True(Replace(tree, owner, second)); Assert.Equal(1, list.SelectedIndex);
            Assert.Same(rows[1], Assert.Single(peer.GetSelection())); Assert.False(rows[0].IsSelected);
            bool both = Replace(tree, owner, first, second);
            Assert.Equal(mode != SelectionMode.Single, both);
            Assert.Equal(mode == SelectionMode.Single ? 1 : 2, list.SelectedItems.Count);
            Assert.True(Replace(tree, owner, second)); Assert.Same(rows[1], Assert.Single(peer.GetSelection()));
            Assert.True(Replace(tree, owner)); Assert.Empty(list.SelectedItems); Assert.Empty(peer.GetSelection());
        }
        finally { tree?.Dispose(); window.Close(); }
    }

    private static bool Replace(MacOSAccessibilityTree tree, ulong owner, params ulong[] rows)
    {
        string text = JsonSerializer.Serialize(rows);
        fixed (char* buffer = text)
        {
            var request = new MacOSAXRequest { NodeId = owner, Operation = (MacOSAXOperation)65,
                Text = buffer, TextCount = text.Length, TextCapacity = text.Length };
            return tree.Handle(ref request);
        }
    }
    private sealed record Row(int Value) { public override string ToString() => "重复🙂"; }
    private sealed class EqualContainer : ListBoxItem
    {
        public override bool Equals(object? other) => other is EqualContainer;
        public override int GetHashCode() => 7;
    }
}
