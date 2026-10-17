using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSSelectionCollectionTests
{
    // The appended operation uses the reserved extension range in both histories.
    private const MacOSAXOperation ReplaceSelection = (MacOSAXOperation)65;
    private const MacOSAXFlags SelectionWritable = (MacOSAXFlags)(1u << 25);

    [Theory]
    [InlineData(SelectionMode.Multiple, false)] [InlineData(SelectionMode.Extended, false)]
    [InlineData(SelectionMode.Multiple, true)] [InlineData(SelectionMode.Extended, true)]
    public void ListReplacementCommitsOneCompleteSelectionAndDeduplicates(SelectionMode mode, bool data)
    {
        using var f = new Fixture(mode, data);
        var events = new List<object[]>();
        f.List.SelectionChanged += (_, _) => events.Add(f.List.SelectedItems.Cast<object>().ToArray());
        Assert.True(f.Replace(f.ListId, f.RowIds[2], f.RowIds[0], f.RowIds[2]));
        Assert.Equal(new object[] { "Gamma", "Alpha" }, f.List.SelectedItems.Cast<object>());
        Assert.Single(events); Assert.Equal(events[0], f.List.SelectedItems.Cast<object>());
        Assert.True(f.Replace(f.ListId, f.RowIds[0])); Assert.Single(f.List.SelectedItems);
        Assert.True(f.Replace(f.ListId)); Assert.Empty(f.List.SelectedItems); Assert.Null(f.List.SelectedItem);
    }

    [Fact]
    public void SingleModeRejectsMultipleDistinctItemsBeforeMutation()
    {
        using var f = new Fixture(SelectionMode.Single);
        Assert.True(f.Replace(f.ListId, f.RowIds[1], f.RowIds[1]));
        Assert.False(f.Replace(f.ListId, f.RowIds[0], f.RowIds[2]));
        Assert.Equal("Beta", f.List.SelectedItem); Assert.Single(f.List.SelectedItems);
        Assert.True(f.Replace(f.ListId)); Assert.Empty(f.List.SelectedItems);
    }

    [Fact]
    public void NestedTreeUsesItsWholeSelectionContainerAndCanClear()
    {
        using var f = new Fixture();
        Assert.True(f.Replace(f.TreeId, f.TreeRowIds[1])); Assert.True(f.TreeRows[1].IsSelected);
        Assert.False(f.Replace(f.TreeId, f.TreeRowIds[0], f.TreeRowIds[1]));
        Assert.True(f.TreeRows[1].IsSelected); Assert.False(f.TreeRows[0].IsSelected);
        Assert.True(f.Replace(f.TreeId)); Assert.Null(f.Tree.SelectedItem);
    }

    [Theory] [InlineData("foreign")] [InlineData("button")] [InlineData("unknown")]
    [InlineData("disabled")] [InlineData("hidden")] [InlineData("detached")]
    public void InvalidMemberCannotPartiallyReplaceTheOldSelection(string kind)
    {
        using var f = new Fixture(); f.List.SelectedIndex = 1;
        ulong invalid = kind switch { "foreign" => f.TreeRowIds[0], "button" => f.ButtonId, "unknown" => ulong.MaxValue, _ => f.RowIds[2] };
        if (kind == "disabled") f.Rows[2].IsEnabled = false;
        if (kind == "hidden") f.Rows[2].Visibility = Visibility.Hidden;
        if (kind == "detached") f.List.Items.Remove(f.Rows[2]);
        Assert.False(f.Replace(f.ListId, f.RowIds[0], invalid));
        Assert.Same(f.Rows[1], f.List.SelectedItem); Assert.Equal(new object[] { f.Rows[1] }, f.List.SelectedItems.Cast<object>());
    }

    [Theory] [InlineData("null")] [InlineData("{}")] [InlineData("[null]")]
    [InlineData("[-1]")] [InlineData("[1.5]")] [InlineData("[\"2\"]")] [InlineData("[")]
    public void MalformedPayloadLeavesSelectionUntouched(string payload)
    {
        using var f = new Fixture(); f.List.SelectedIndex = 1;
        Assert.False(f.ReplaceText(f.ListId, payload)); Assert.Same(f.Rows[1], f.List.SelectedItem);
    }

    [Fact]
    public void BufferBoundsAreCheckedBeforeReading()
    {
        using var f = new Fixture();
        char* buffer = stackalloc char[2]; buffer[0] = '['; buffer[1] = ']';
        foreach (var (count, capacity) in new[] { (-1, 2), (3, 2), (2, -1), (int.MaxValue, int.MaxValue) })
        {
            var request = new MacOSAXRequest { NodeId = f.ListId, Operation = ReplaceSelection, Text = buffer, TextCount = count, TextCapacity = capacity };
            Assert.False(f.Bridge.Handle(ref request));
        }
        var missing = new MacOSAXRequest { NodeId = f.ListId, Operation = ReplaceSelection, TextCount = 2, TextCapacity = 2 };
        Assert.False(f.Bridge.Handle(ref missing));
    }

    [Theory] [InlineData("disabled")] [InlineData("hidden")] [InlineData("disposed")]
    public void RetiredOrUnavailableContainerCannotBeMutated(string kind)
    {
        using var f = new Fixture(); f.List.SelectedIndex = 1;
        if (kind == "disabled") f.List.IsEnabled = false;
        if (kind == "hidden") f.List.Visibility = Visibility.Hidden;
        if (kind == "disposed") f.Bridge.Dispose();
        Assert.False(f.Replace(f.ListId, f.RowIds[0])); Assert.Same(f.Rows[1], f.List.SelectedItem);
    }

    [Fact]
    public void SelectionHandlerCanChooseANewerSelection()
    {
        using var f = new Fixture();
        f.List.SelectionChanged += (_, _) => { if (Equals(f.List.SelectedItem, "Beta")) f.List.SelectedIndex = 2; };
        Assert.True(f.Replace(f.ListId, f.RowIds[1]));
        Assert.Same(f.Rows[2], f.List.SelectedItem); Assert.Equal(new object[] { f.Rows[2] }, f.List.SelectedItems.Cast<object>());
    }

    [Fact]
    public void OnlySupportedContainersAdvertiseTheExtensionAndTheAbiStaysStable()
    {
        using var f = new Fixture();
        Assert.True((f.Info(f.ListId).Flags & SelectionWritable) != 0);
        Assert.True((f.Info(f.TreeId).Flags & SelectionWritable) != 0);
        Assert.True((f.Info(f.ButtonId).Flags & SelectionWritable) == 0);
        Assert.False(f.Replace(f.ButtonId, f.RowIds[0])); Assert.Equal(136, Marshal.SizeOf<MacOSAXRequest>());
        Assert.Equal(64, (int)MacOSAXOperation.PopupCanBecomeKey); Assert.Equal(14, (int)MacOSAXOperation.TextStyleRange);
    }

    private sealed class InputList : ListBox { internal Panel? HostPanel => ItemsHost; }

    private sealed class Fixture : IDisposable
    {
        internal readonly InputList List;
        internal readonly ListBoxItem[] Rows = [new() { Content = "Alpha" }, new() { Content = "Beta" }, new() { Content = "Gamma" }];
        internal readonly TreeView Tree = new();
        internal readonly TreeViewItem[] TreeRows = [new() { Header = "One" }, new() { Header = "Two" }];
        internal readonly DisplayedTestWindow Window;
        internal readonly MacOSAccessibilityTree Bridge;
        internal readonly ulong ListId, TreeId, ButtonId;
        internal readonly ulong[] RowIds, TreeRowIds;
        internal Fixture(SelectionMode mode = SelectionMode.Multiple, bool data = false)
        {
            List = new InputList { SelectionMode = mode, Height = 200 };
            foreach (var row in Rows) List.Items.Add(data ? row.Content! : row);
            var group = new TreeViewItem { Header = "Group", IsExpanded = true };
            foreach (var row in TreeRows) group.Items.Add(row); Tree.Items.Add(group); Tree.Height = 200;
            var button = new Button { Content = "Button" }; var panel = new StackPanel();
            panel.Children.Add(List); panel.Children.Add(Tree); panel.Children.Add(button);
            Window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native, Content = panel, Width = 500, Height = 600 };
            Window.Measure(new Size(500, 600)); Window.Arrange(new Rect(0, 0, 500, 600)); Window.UpdateLayout();
            if (List.HostPanel is { } hostPanel)
            {
                hostPanel.Measure(new Size(500, 200)); hostPanel.Arrange(new Rect(0, 0, 500, 200));
            }
            Dispatcher.GetForCurrentThread().ProcessQueue();
            if (data) foreach (var item in List.Items) Assert.NotNull(List.ItemContainerGenerator.ContainerFromItem(item));
            Bridge = new MacOSAccessibilityTree(Window); ListId = Id(List.GetAutomationPeer()!); TreeId = Id(Tree.GetAutomationPeer()!); ButtonId = Id(button.GetAutomationPeer()!);
            var peer = (ListBoxAutomationPeer)List.GetAutomationPeer()!;
            RowIds = List.Items.Cast<object>().Select(item => Id(peer.FindOrCreateItemAutomationPeer(item))).ToArray();
            TreeRowIds = TreeRows.Select(row => Id(row.GetAutomationPeer()!)).ToArray();
        }
        private ulong Id(AutomationPeer peer) { Assert.True(Bridge.TryGetId(peer, out ulong id)); return id; }
        internal MacOSAXRequest Info(ulong id) { var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Info }; Assert.True(Bridge.Handle(ref request)); return request; }
        internal bool Replace(ulong id, params ulong[] members) => ReplaceText(id, JsonSerializer.Serialize(members));
        internal bool ReplaceText(ulong id, string payload)
        {
            fixed (char* value = payload)
            {
                var request = new MacOSAXRequest { NodeId = id, Operation = ReplaceSelection, Text = value, TextCount = payload.Length, TextCapacity = payload.Length };
                return Bridge.Handle(ref request);
            }
        }
        public void Dispose() { Bridge.Dispose(); Window.Close(); }
    }
}
