using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class InputRouteLifetimeTests
{
    [Fact]
    public void CompletedTunnel_DoesNotRetainItsLastInputTarget()
    {
        WeakReference target = RouteToTemporaryElement();
        Assert.Empty(GetCachedRoute());
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(target.IsAlive);
    }

    [Fact]
    public void ThrowingHandler_StillClearsTheReusableRoute()
    {
        var target = new UIElement();
        target.PreviewMouseMove += (_, _) => throw new InvalidOperationException("route callback");
        Assert.Throws<InvalidOperationException>(() => target.RaiseEvent(new MouseEventArgs(UIElement.PreviewMouseMoveEvent)));
        Assert.Empty(GetCachedRoute());
    }

    [Fact]
    public void NestedTunnel_DoesNotClearTheOuterRouteBeforeItFinishes()
    {
        var root = new Grid();
        var outer = new UIElement();
        var inner = new UIElement();
        root.Children.Add(outer);
        root.Children.Add(inner);
        var calls = new List<string>();
        bool nested = false;
        root.PreviewMouseMove += (_, _) =>
        {
            calls.Add(nested ? "inner-root" : "outer-root");
            if (!nested)
            {
                nested = true;
                inner.RaiseEvent(new MouseEventArgs(UIElement.PreviewMouseMoveEvent));
            }
        };
        inner.PreviewMouseMove += (_, _) => calls.Add("inner");
        outer.PreviewMouseMove += (_, _) => calls.Add("outer");
        outer.RaiseEvent(new MouseEventArgs(UIElement.PreviewMouseMoveEvent));
        Assert.Equal(new[] { "outer-root", "inner-root", "inner", "outer" }, calls);
        Assert.Empty(GetCachedRoute());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RouteToTemporaryElement()
    {
        var target = new UIElement();
        target.RaiseEvent(new MouseEventArgs(UIElement.PreviewMouseMoveEvent));
        return new WeakReference(target);
    }

    private static System.Collections.IEnumerable GetCachedRoute() =>
        (System.Collections.IEnumerable)typeof(UIElement).GetField("_tunnelPath",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
}
