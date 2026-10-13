using System.Collections;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSVirtualizedAccessibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AccessibilityReadsOnlyRealizedRowsAndFollowsRecycling(bool self)
    {
        var source = new CountingRange(1_000_000);
        var template = new DataTemplate();
        template.SetVisualTree(() =>
        {
            var row = new Button { Height = 30, MinHeight = 30 };
            row.SetBinding(ContentControl.ContentProperty, new Binding { Path = new PropertyPath("."), StringFormat = "第 {0} 项" });
            return row;
        });
        var rows = new RazorItemsHost { ItemsSource = source, ItemTemplate = template };
        var outer = self ? null : new ScrollViewer { CanContentScroll = true, Content = rows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var window = new DisplayedTestWindow { TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 300, Height = 300, Content = outer ?? (UIElement)rows };
        try
        {
            var tree = new MacOSAccessibilityTree(window);
            window.Measure(new Size(300, 300)); window.Arrange(new Rect(0, 0, 300, 300)); window.UpdateLayout();
            var viewer = outer ?? Find<ScrollViewer>(rows)!;
            Assert.True(tree.TryGetId(rows.GetAutomationPeer()!, out ulong list));
            source.Reset();
            var first = Children(tree, list);
            Assert.InRange(first.Count, 10, 100);
            Assert.Contains(first, id => Text(tree, id) == "第 0 项");
            Assert.Equal(0, source.Enumerated);
            Assert.Equal(0, source.Reads);

            viewer.ScrollToBottom(); window.UpdateLayout(); source.Reset();
            var last = Children(tree, list);
            Assert.InRange(last.Count, 10, 100);
            Assert.Contains(last, id => Text(tree, id) == "第 999999 项");
            Assert.DoesNotContain(last, id => Text(tree, id) == "第 0 项");
            Assert.Equal(0, source.Enumerated);
            Assert.Equal(0, source.Reads);

            viewer.ScrollToTop(); window.UpdateLayout(); source.Reset();
            var restored = Children(tree, list);
            Assert.Contains(restored, id => Text(tree, id) == "第 0 项");
            Assert.DoesNotContain(restored, id => Text(tree, id) == "第 999999 项");
            Assert.Equal(0, source.Enumerated);
            Assert.Equal(0, source.Reads);
        }
        finally { window.Close(); }
    }

    private static List<ulong> Children(MacOSAccessibilityTree tree, ulong id)
    {
        var info = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Info };
        Assert.True(tree.Handle(ref info));
        var result = new List<ulong>();
        for (int i = 0; i < info.ChildCount; i++)
        {
            var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Child, Index = i };
            Assert.True(tree.Handle(ref request)); result.Add(request.ResultId);
        }
        return result;
    }
    private static string Text(MacOSAccessibilityTree tree, ulong id)
    {
        char* buffer = stackalloc char[128];
        var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.String,
            Index = (int)MacOSAXString.Name, Text = buffer, TextCapacity = 128 };
        Assert.True(tree.Handle(ref request)); return new string(buffer, 0, request.TextCount);
    }
    private static T? Find<T>(Visual root) where T : Visual
    {
        if (root is T match) return match;
        for (int i = 0; i < root.VisualChildrenCount; i++)
            if (root.GetVisualChild(i) is { } child && Find<T>(child) is { } found) return found;
        return null;
    }
    private sealed class CountingRange(int count) : IList
    {
        internal int Enumerated { get; private set; }
        internal int Reads { get; private set; }
        internal void Reset() { Enumerated = 0; Reads = 0; }
        public object this[int index] { get { Reads++; return index; } set => throw new NotSupportedException(); }
        public int Count => count;
        public bool IsReadOnly => true;
        public bool IsFixedSize => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public bool Contains(object? value) => IndexOf(value) >= 0;
        public int IndexOf(object? value) => value is int i && (uint)i < (uint)count ? i : -1;
        public IEnumerator GetEnumerator()
        { for (int i = 0; i < count; i++) { Enumerated++; yield return i; } }
        public void CopyTo(Array array, int index) => throw new NotSupportedException();
        public int Add(object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }
}
