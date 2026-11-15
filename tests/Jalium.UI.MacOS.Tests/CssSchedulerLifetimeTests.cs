using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSchedulerLifetimeTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void PendingInvalidationsDoNotOwnDetachedVisualsOrTheirData(bool subtree)
    {
        var weak = EnqueueDetached(subtree);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(weak.Visual.IsAlive); Assert.False(weak.Data.IsAlive);
    }

    [Fact]
    public void QueuedChangesStillApplyAfterCollectionWhenTheVisualTreeIsAlive()
    {
        var child = new Border(); var root = new StackPanel(); root.Children.Add(child);
        Css.SetClass(child, "queued"); Css.SetStyleSheet(root, ".queued { opacity:.25 }");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher); Assert.Equal(.25, child.Opacity);
        Css.SetStyleSheet(root, ".queued { opacity:.75 }");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher); Assert.Equal(.75, child.Opacity);
        GC.KeepAlive(root); GC.KeepAlive(child);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Visual, WeakReference Data) EnqueueDetached(bool subtree)
    {
        var data = new object(); var visual = new Border { DataContext = data };
        Css.SetStyle(visual, "opacity:.5");
        if (subtree) CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(visual));
        else CssEvaluationScheduler.InvalidateElement(CssNode.Get(visual));
        return (new WeakReference(visual), new WeakReference(data));
    }
}
