using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class InputDetachSchedulingTests
{
    [Fact]
    public void LateAbort_FrameFallbackPreservesReparentThenCleansFinalDetach()
    {
        UIElement.ForceReleaseMouseCapture();
        var leaf = new Border { Width = 80, Height = 50 };
        var root = new Grid();
        root.Children.Add(leaf);
        var host = new ContentControl { Content = root };
        var window = new Window { Content = host, Width = 200, Height = 150 };
        try
        {
            window.Measure(new Size(200, 150));
            window.Arrange(new Rect(0, 0, 200, 150));
            var input = Read<WindowInputDispatcher>(window, "_inputDispatcher");
            input.UpdateMouseOverState(leaf, 1);
            Assert.True(leaf.CaptureMouse());

            host.Content = null;
            var first = Assert.IsType<DispatcherOperation>(Read<object>(window, "_inputDetachOperation"));
            Assert.True(first.Abort());
            host.Content = root;
            FrameBoundary(window);
            Assert.Same(leaf, Mouse.Captured);
            Assert.Same(leaf, input.LastMouseOverElement);

            host.Content = null;
            var last = Assert.IsType<DispatcherOperation>(Read<object>(window, "_inputDetachOperation"));
            Assert.True(last.Abort());
            FrameBoundary(window);
            Assert.Null(Mouse.Captured);
            Assert.Null(input.LastMouseOverElement);
            Assert.Null(Read<object?>(window, "_pendingInputDetachRoots"));
            Assert.Null(Read<object?>(window, "_inputDetachOperation"));
        }
        finally
        {
            window.Close();
            UIElement.ForceReleaseMouseCapture();
            Mouse.OnMouseLeaveWindow();
        }
    }

    private static T Read<T>(Window window, string name) => (T)typeof(Window)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static void FrameBoundary(Window window) => typeof(Window)
        .GetMethod("ProcessPendingInputDetachesForFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, null);
}
