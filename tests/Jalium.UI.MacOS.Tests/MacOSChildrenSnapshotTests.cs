using System.Diagnostics;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Xunit.Abstractions;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSChildrenSnapshotTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(128)]
    [InlineData(1024)]
    [InlineData(4096)]
    public void BulkQueryMatchesIndexedQueryWithLinearRequestWork(int count)
    {
        using var lifetime = new Lifetime();
        var window = lifetime.Window;
        var panel = new SemanticPanel();
        AutomationProperties.SetName(panel, "items");
        for (int i = 0; i < count; i++) panel.Children.Add(new Button { Content = i.ToString() });
        window.Content = panel;
        var tree = new MacOSAccessibilityTree(window);
        ulong panelId = Assert.Single(Read(tree, 1));
        // Warm IDs and peer caches equally before comparing callback workloads.
        var expected = Read(tree, panelId);
        (ulong[] ids, long ticks, long bytes) Measure(bool bulk)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            ulong[] ids;
            if (bulk) ids = Read(tree, panelId);
            else
            {
                int n = Request(tree, panelId, MacOSAXOperation.Info).ChildCount;
                ids = Enumerable.Range(0, n).Select(i => Request(tree, panelId, MacOSAXOperation.Child, i).ResultId).ToArray();
            }
            return (ids, Stopwatch.GetTimestamp() - start, GC.GetAllocatedBytesForCurrentThread() - allocated);
        }
        var before = Measure(false);
        var after = Measure(true);
        Assert.Equal(expected, before.ids);
        Assert.Equal(expected, after.ids);
        Assert.Equal(count, after.ids.Length);
        Assert.True(after.bytes < before.bytes);
        output.WriteLine($"N={count}; full child enumerations {count + 1}->1; callbacks {count + 1}->3; " +
            $"elapsed ms {before.ticks * 1000.0 / Stopwatch.Frequency:F3}->{after.ticks * 1000.0 / Stopwatch.Frequency:F3}; " +
            $"allocated bytes {before.bytes}->{after.bytes}");
        foreach (ulong id in after.ids)
            Assert.Equal(panelId, Request(tree, id, MacOSAXOperation.Info).ParentId);
        tree.Dispose();
    }

    [Fact]
    public void SnapshotIsConsistentAcrossMutationsAndNewRequestsSeeCurrentVisibility()
    {
        using var lifetime = new Lifetime();
        var window = lifetime.Window;
        var panel = new SemanticPanel();
        AutomationProperties.SetName(panel, "outer");
        var nested = new SemanticPanel();
        AutomationProperties.SetName(nested, "inner");
        var retained = new Button { Content = "retained" };
        var removed = new Button { Content = "removed" };
        nested.Children.Add(retained); nested.Children.Add(removed); panel.Children.Add(nested);
        window.Content = panel;
        var tree = new MacOSAccessibilityTree(window);
        ulong outer = Assert.Single(Read(tree, 1));
        ulong inner = Assert.Single(Read(tree, outer));
        var original = Read(tree, inner);
        var snapshot = Request(tree, inner, MacOSAXOperation.BeginChildren);
        nested.Children.Remove(removed);
        retained.Visibility = Visibility.Collapsed;
        nested.Children.Add(new Button { Content = "added" });
        Assert.Equal(original, Copy(tree, ref snapshot));
        Assert.True(tree.Handle(ref snapshot)); // release is independent of parent visibility
        var current = Read(tree, inner);
        Assert.Single(current);
        Assert.DoesNotContain(current[0], original);
        Assert.False(Try(tree, original[1], MacOSAXOperation.Info));
        retained.Visibility = Visibility.Visible;
        Assert.Contains(original[0], Read(tree, inner));
        Assert.Equal(inner, Request(tree, original[0], MacOSAXOperation.Info).ParentId);
        snapshot = Request(tree, inner, MacOSAXOperation.BeginChildren);
        panel.Children.Remove(nested);
        snapshot.Operation = MacOSAXOperation.ReleaseChildren;
        Assert.True(tree.Handle(ref snapshot));
        Assert.False(tree.Handle(ref snapshot));
        Assert.False(Try(tree, inner, MacOSAXOperation.BeginChildren));
        tree.Dispose();
        Assert.False(Try(tree, 1, MacOSAXOperation.BeginChildren));
        Assert.False(Try(tree, original[0], MacOSAXOperation.Info));
    }

    [Fact]
    public void IndependentSnapshotsRejectWrongOwnerAndShortBuffersAndDisposeInvalidatesTokens()
    {
        using var lifetime = new Lifetime();
        var window = lifetime.Window;
        window.Content = new Button { Content = "child" };
        var tree = new MacOSAccessibilityTree(window);
        var first = Request(tree, 1, MacOSAXOperation.BeginChildren);
        var second = Request(tree, 1, MacOSAXOperation.BeginChildren);
        Assert.NotEqual(first.ResultId, second.ResultId);
        var invalid = first; invalid.Operation = MacOSAXOperation.ReadChildren;
        Assert.False(tree.Handle(ref invalid));
        invalid.Operation = MacOSAXOperation.ReleaseChildren; invalid.NodeId = 999;
        Assert.False(tree.Handle(ref invalid));
        Assert.Equal(Copy(tree, ref first), Copy(tree, ref second));
        Assert.True(tree.Handle(ref first));
        tree.Dispose();
        Assert.False(tree.Handle(ref second));
    }

    private sealed class SemanticPanel : StackPanel
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    }
    private sealed class TestListBox : ListBox
    {
        internal Panel? Host => ItemsHost;
    }

    [Fact]
    public void VirtualizedListExposesOnlyRealizedContainersAndRefreshesAfterScrolling()
    {
        using var lifetime = new Lifetime();
        var list = new TestListBox { Width = 320, Height = 120 };
        for (int i = 0; i < 500; i++) list.Items.Add($"Item {i}");
        lifetime.Window.Content = list;
        list.Measure(new Size(320, 120)); list.Arrange(new Rect(0, 0, 320, 120));
        var tree = new MacOSAccessibilityTree(lifetime.Window);
        ulong id = Assert.Single(Read(tree, 1));
        var before = Read(tree, id);
        Assert.NotEmpty(before); Assert.True(before.Length < 500);
        Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(400));
        list.ScrollIntoView("Item 400");
        var host = Assert.IsType<VirtualizingStackPanel>(list.Host);
        host.Measure(new Size(320, 120)); host.Arrange(new Rect(0, 0, 320, 120));
        var container = Assert.IsAssignableFrom<UIElement>(list.ItemContainerGenerator.ContainerFromIndex(400));
        var after = Read(tree, id);
        Assert.True(tree.TryGetId(container.GetAutomationPeer()!, out ulong realizedId));
        Assert.Contains(realizedId, after);
        Assert.Equal(id, Request(tree, realizedId, MacOSAXOperation.Info).ParentId);
        Assert.DoesNotContain(realizedId, before);
        tree.Dispose();
    }

    private sealed class Lifetime : IDisposable
    {
        internal DisplayedTestWindow Window { get; } = new() { TitleBarStyle = WindowTitleBarStyle.Native };
        public void Dispose() => Window.Close();
    }

    private static bool Try(MacOSAccessibilityTree tree, ulong id, MacOSAXOperation op)
    { var request = new MacOSAXRequest { NodeId = id, Operation = op }; return tree.Handle(ref request); }
    private static MacOSAXRequest Request(MacOSAccessibilityTree tree, ulong id, MacOSAXOperation op, int index = 0)
    {
        var request = new MacOSAXRequest { NodeId = id, Operation = op, Index = index };
        Assert.True(tree.Handle(ref request)); return request;
    }
    private static ulong[] Copy(MacOSAccessibilityTree tree, ref MacOSAXRequest request)
    {
        var ids = new ulong[request.ChildCount];
        fixed (ulong* buffer = ids)
        {
            request.Operation = MacOSAXOperation.ReadChildren; request.Text = (char*)buffer; request.TextCapacity = ids.Length;
            Assert.True(tree.Handle(ref request));
        }
        request.Operation = MacOSAXOperation.ReleaseChildren;
        return ids;
    }
    private static ulong[] Read(MacOSAccessibilityTree tree, ulong id)
    {
        var request = Request(tree, id, MacOSAXOperation.BeginChildren);
        try { return Copy(tree, ref request); }
        finally { request.Operation = MacOSAXOperation.ReleaseChildren; Assert.True(tree.Handle(ref request)); }
    }
}
