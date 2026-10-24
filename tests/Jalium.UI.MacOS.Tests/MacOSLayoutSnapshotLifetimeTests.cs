using System.Runtime.CompilerServices;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSLayoutSnapshotLifetimeTests
{
    [Theory]
    [InlineData("normal")]
    [InlineData("measure")]
    [InlineData("arrange")]
    [InlineData("reenter")]
    public void FinishedLayoutDoesNotRetainDetachedDataOrElements(string mode)
    {
        var host = new LayoutHost();
        var references = LayoutThenDetach(host, mode);
        // Other tests may have activated CSS. Complete its ordinary deferred
        // styling without running another layout pass on the manager under test.
        Jalium.UI.Styling.CssEvaluationScheduler.FlushIfPending(host.Dispatcher);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(references.Element.IsAlive);
        Assert.False(references.Data.IsAlive);
        // The same manager remains usable after normal, exceptional and nested work.
        var current = new LayoutChild(); host.Add(current); current.InvalidateMeasure();
        host.LayoutManager.UpdateLayout(host, new Size(300, 200));
        Assert.True(current.IsMeasureValid); Assert.True(current.IsArrangeValid);
        GC.KeepAlive(host);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Element, WeakReference Data) LayoutThenDetach(LayoutHost host, string mode)
    {
        var data = new object();
        var child = new LayoutChild { DataContext = data };
        host.Add(child);
        child.MeasureAction = mode == "measure" ? () => throw new InvalidOperationException("measure")
            : mode == "reenter" ? () => host.LayoutManager.UpdateLayout(host,new Size(300,200)) : null;
        child.ArrangeAction = mode == "arrange" ? () => throw new InvalidOperationException("arrange") : null;
        child.InvalidateMeasure();
        if (mode is "measure" or "arrange")
        {
            Assert.Throws<InvalidOperationException>(() => host.LayoutManager.UpdateLayout(host,new Size(300,200)));
            Assert.Same(child, host.Dispatcher.LastLayoutExceptionElement);
            // LayoutInformation intentionally exposes the last failed element until
            // a later real measure starts. Complete that diagnostic lifetime on a
            // separate tree, without draining or replacing this manager's snapshots.
            new FrameworkElement().Measure(new Size(10,10));
            Assert.Null(host.Dispatcher.LastLayoutExceptionElement);
        }
        else host.LayoutManager.UpdateLayout(host,new Size(300,200));
        var references = (new WeakReference(child), new WeakReference(data));
        host.Remove(child);
        return references;
    }

    private sealed class LayoutHost : FrameworkElement, ILayoutManagerHost
    {
        public LayoutManager LayoutManager { get; } = new();
        internal void Add(UIElement child) => AddVisualChild(child);
        internal void Remove(UIElement child) => RemoveVisualChild(child);
        protected override Size MeasureOverride(Size availableSize) => default;
        protected override Size ArrangeOverride(Size finalSize) => finalSize;
    }
    private sealed class LayoutChild : FrameworkElement
    {
        internal Action? MeasureAction;
        internal Action? ArrangeAction;
        protected override Size MeasureOverride(Size availableSize) { MeasureAction?.Invoke(); return default; }
        protected override Size ArrangeOverride(Size finalSize) { ArrangeAction?.Invoke(); return finalSize; }
    }
}
