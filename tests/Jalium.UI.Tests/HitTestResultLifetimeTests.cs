using System.Runtime.CompilerServices;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class HitTestResultLifetimeTests
{
    [Fact]
    public void ReusableResult_DoesNotRootTheLastVisualWithoutACaller()
    {
        var reference = QueryTemporaryVisual();
        Collect();
        Assert.False(reference.IsAlive);
    }

    [Fact]
    public void CallerHoldingAResult_KeepsItsVisualValid()
    {
        var element = new UIElement();
        var result = HitTestResult.GetReusable(element);
        Collect();
        Assert.Same(element, result.VisualHit);
        Assert.Same(result, HitTestResult.GetReusable(element));
        GC.KeepAlive(result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference QueryTemporaryVisual()
    {
        var element = new UIElement();
        Assert.Same(element, HitTestResult.GetReusable(element).VisualHit);
        return new WeakReference(element);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
