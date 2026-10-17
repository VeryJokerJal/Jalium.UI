using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSVirtualizedSelectionIdentityTests
{
    [Fact]
    public void CachedRowNameCannotChangeToAnotherLogicalItemAfterReuse()
    {
        using var f = new Fixture(); ulong original = f.Id(f.First);
        Assert.Equal("item-0", f.Name(original)); f.Bottom();
        Assert.False(f.TryName(original, out string name) && name != "item-0", "retained row was retargeted to " + name);
    }

    [Theory] [InlineData(SelectionMode.Single)] [InlineData(SelectionMode.Multiple)] [InlineData(SelectionMode.Extended)]
    public void CachedRowPressCannotSelectTheReplacementItem(SelectionMode mode)
    {
        using var f = new Fixture(mode); ulong original = f.Id(f.First); f.Bottom();
        Assert.False(f.Action(original), "an obsolete row accepted selection after its container was reused");
        Assert.Empty(f.List.SelectedItems);
        ulong current = f.Id(f.List.ItemContainerGenerator.ContainerFromIndex(499)!);
        Assert.True(f.Action(current)); Assert.Equal("item-499", f.List.SelectedItem);
    }

    [Fact]
    public void BatchWithARecycledMemberCannotPartiallyReplaceSelection()
    {
        using var f = new Fixture(); ulong old = f.Id(f.First); f.Bottom();
        f.List.SelectedIndex = 499; ulong current = f.Id(f.List.ItemContainerGenerator.ContainerFromIndex(499)!);
        Assert.False(f.Replace(current, old)); Assert.Equal("item-499", f.List.SelectedItem);
        Assert.Equal(new object[] { "item-499" }, f.List.SelectedItems.Cast<object>());
    }

    [Fact]
    public void ScrollingBackDoesNotReviveAnOldPhysicalRowReference()
    {
        using var f = new Fixture(); ulong old = f.Id(f.First); f.Bottom(); f.Top();
        Assert.False(f.Action(old)); Assert.Empty(f.List.SelectedItems);
        ulong current = f.Id(f.List.ItemContainerGenerator.ContainerFromIndex(0)!);
        Assert.NotEqual(old, current); Assert.True(f.Action(current)); Assert.Equal("item-0", f.List.SelectedItem);
    }

    [Fact]
    public void NewRegistrationRetiresTheOldIdOfTheSamePhysicalContainer()
    {
        using var f = new Fixture(); ulong old = f.Id(f.First); f.Bottom();
        ulong current = f.Id(f.First); Assert.NotEqual(old, current);
        Assert.False(f.TryName(old, out _));
        int index = f.List.ItemContainerGenerator.IndexFromContainer(f.First);
        Assert.Equal("item-" + index, f.Name(current));
    }

    [Fact]
    public void CachedDescendantCannotInvokeForTheReplacementItem()
    {
        using var f = new Fixture(descendant: true); ulong old = f.Id(f.First.Child);
        f.Bottom(); Assert.False(f.Action(old)); Assert.Equal(0, f.First.Clicks);
        int replacement = f.List.ItemContainerGenerator.IndexFromContainer(f.First);
        f.List.ScrollIntoView("item-" + replacement); f.Layout();
        ulong current = f.Id(f.First.Child); Assert.NotEqual(old, current);
        Assert.False(f.TryName(old, out _));
        var info = new MacOSAXRequest { NodeId = current, Operation = MacOSAXOperation.Info };
        Assert.True(f.Tree.Handle(ref info)); Assert.True(info.Flags.HasFlag(MacOSAXFlags.Pressable));
        // Invoke needs a live platform handle; the external AX host covers its
        // positive path for both the recycled and the newly realized buttons.
    }

    [Fact]
    public void NameReadThatRecyclesItsRowCannotPublishTheReplacementName()
    {
        using var f = new Fixture(); ulong old = f.Id(f.First);
        f.First.ReadName = f.Bottom;
        Assert.False(f.TryName(old, out _));
        Assert.Empty(f.List.SelectedItems);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ProviderCallbackCannotSelectAfterARecyclingRoundTrip(bool returnToTop)
    {
        using var f = new Fixture(); ulong old = f.Id(f.First);
        f.First.ReadSelection = () => { f.Bottom(); if (returnToTop) f.Top(); };
        Assert.False(f.Action(old)); Assert.Empty(f.List.SelectedItems);
    }

    [Fact]
    public void VirtualizingTheSelectedItemDoesNotClearPublicSelection()
    {
        using var f = new Fixture(); f.List.SelectedIndex = 0; ulong old = f.Id(f.First);
        f.Bottom(); Assert.Equal("item-0", f.List.SelectedItem);
        Assert.Equal(new object[] { "item-0" }, f.List.SelectedItems.Cast<object>());
        Assert.False(f.Action(old)); Assert.Equal("item-0", f.List.SelectedItem);
    }

    [Fact]
    public void AnIndexShiftKeepsTheIdentityOfASurvivingRealization()
    {
        using var f = new Fixture(); ulong original = f.Id(f.First);
        f.List.Items.Insert(0, "inserted"); f.Layout();
        Assert.Same(f.First, f.List.ItemContainerGenerator.ContainerFromIndex(1));
        Assert.Equal(original, f.Id(f.First)); Assert.Equal("item-0", f.Name(original));
        Assert.True(f.Action(original)); Assert.Equal("item-0", f.List.SelectedItem);
    }

    [Fact]
    public void ARejectedChildSnapshotDoesNotLeaveALiveToken()
    {
        using var f = new Fixture(descendant: true); ulong old = f.Id(f.First);
        f.First.ReadChildren = f.Bottom;
        var request = new MacOSAXRequest { NodeId = old, Operation = (MacOSAXOperation)15 };
        Assert.False(f.Tree.Handle(ref request));
        // The local retained history has no snapshot protocol; PR history must
        // also retire a token created before a reentrant realization change.
        request.Operation = (MacOSAXOperation)17;
        Assert.False(f.Tree.Handle(ref request));
    }

    [Fact]
    public void RemovingAndReinsertingTheSameItemDoesNotReviveItsOldReference()
    {
        using var f = new Fixture(); ulong old = f.Id(f.First); object item = f.List.Items[0];
        f.List.Items.RemoveAt(0); f.List.Items.Insert(0, item); f.Layout();
        Assert.Same(f.First, f.List.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.False(f.Action(old)); Assert.Empty(f.List.SelectedItems);
        ulong current = f.Id(f.First); Assert.NotEqual(old, current);
        Assert.Equal("item-0", f.Name(current)); Assert.True(f.Action(current));
    }

    private sealed class ProbeRow : ListBoxItem
    {
        internal Action? ReadName, ReadSelection, ReadChildren;
        internal readonly Button Child = new() { Height = 32 };
        internal int Clicks;
        internal ProbeRow(bool descendant)
        {
            Child.Click += (_, _) => Clicks++;
            if (descendant)
            {
                var template = new ControlTemplate(typeof(ListBoxItem));
                template.SetVisualTree(() => Child); Template = template; ApplyTemplate();
            }
        }
        protected override AutomationPeer OnCreateAutomationPeer() => new ProbePeer(this);
        private sealed class ProbePeer(ProbeRow row) : ListBoxItemAutomationPeer(row)
        {
            protected override string GetNameCore()
            {
                var read = row.ReadName; row.ReadName = null; read?.Invoke();
                return base.GetNameCore();
            }
            protected override List<AutomationPeer> GetChildrenCore()
            {
                var read = row.ReadChildren; row.ReadChildren = null; read?.Invoke();
                return base.GetChildrenCore();
            }
            protected override object? GetPatternCore(PatternInterface pattern)
            {
                if (pattern == PatternInterface.SelectionItem)
                { var read = row.ReadSelection; row.ReadSelection = null; read?.Invoke(); }
                return base.GetPatternCore(pattern);
            }
        }
    }

    private sealed class InputList : ListBox
    {
        internal Panel? Host => ItemsHost;
        internal bool Descendant;
        protected override FrameworkElement GetContainerForItem(object item) => new ProbeRow(Descendant);
        protected override void PrepareContainerForItem(FrameworkElement container, object item)
        {
            base.PrepareContainerForItem(container, item);
            container.Height = 32;
            if (Descendant && container is ProbeRow row)
            { row.Child.Content = item; }
        }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly InputList List;
        internal readonly DisplayedTestWindow Window;
        internal readonly MacOSAccessibilityTree Tree;
        internal readonly ProbeRow First;
        private readonly ulong _list;
        internal Fixture(SelectionMode mode = SelectionMode.Multiple, bool descendant = false)
        {
            List = new InputList { SelectionMode = mode, Height = 180, Width = 360, Descendant = descendant };
            for (int i = 0; i < 500; i++) List.Items.Add("item-" + i);
            Window = new DisplayedTestWindow { Content = List, TitleBarStyle = WindowTitleBarStyle.Native };
            Layout(); First = Assert.IsType<ProbeRow>(List.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.InRange(List.Host!.Children.Count, 1, 100);
            Tree = new MacOSAccessibilityTree(Window); Assert.True(Tree.TryGetId(List.GetAutomationPeer()!, out _list));
        }
        internal ulong Id(DependencyObject element) { Assert.True(Tree.TryGetId(((UIElement)element).GetAutomationPeer()!, out ulong id)); return id; }
        internal void Bottom()
        {
            List.ScrollIntoView("item-250"); Layout();
            List.ScrollIntoView("item-499"); Layout();
            Assert.NotNull(List.ItemContainerGenerator.ContainerFromIndex(499));
            int index = List.ItemContainerGenerator.IndexFromContainer(First);
            Assert.InRange(index, 1, 499); Assert.Null(List.ItemContainerGenerator.ContainerFromIndex(0));
        }
        internal void Top() { List.ScrollIntoView("item-0"); Layout(); Assert.NotNull(List.ItemContainerGenerator.ContainerFromIndex(0)); }
        internal void Layout()
        {
            Window.Measure(new Size(360, 180)); Window.Arrange(new Rect(0, 0, 360, 180));
            if (List.Host is { } panel) { panel.Measure(new Size(360, 180)); panel.Arrange(new Rect(0, 0, 360, 180)); }
            Dispatcher.GetForCurrentThread().ProcessQueue();
        }
        internal bool TryName(ulong id, out string name)
        {
            char* buffer = stackalloc char[128]; var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.String, Index = (int)MacOSAXString.Name, Text = buffer, TextCapacity = 128 };
            bool result = Tree.Handle(ref request); name = result ? new string(buffer, 0, request.TextCount) : ""; return result;
        }
        internal string Name(ulong id) { Assert.True(TryName(id, out string text)); return text; }
        internal bool Action(ulong id) { var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Action, Index = (int)MacOSAXAction.Press }; return Tree.Handle(ref request); }
        internal bool Replace(params ulong[] ids)
        {
            string value = JsonSerializer.Serialize(ids);
            fixed (char* text = value)
            { var request = new MacOSAXRequest { NodeId = _list, Operation = (MacOSAXOperation)65, Text = text, TextCount = value.Length, TextCapacity = value.Length }; return Tree.Handle(ref request); }
        }
        public void Dispose() { Tree.Dispose(); Window.Close(); }
    }
}
