using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowInputLifecycleTests
{
    private static readonly FieldInfo FrameStartingField = typeof(CompositionTarget)
        .GetField("FrameStarting", BindingFlags.Static | BindingFlags.NonPublic)!;

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void RepeatedShowAndHide_KeepsOneFrameSubscription_AndCloseRemovesIt()
    {
        var window = new Window { Width = 240, Height = 160, ShowActivated = false };
        try
        {
            for (int round = 0; round < 12; round++)
            {
                window.Show();
                window.Show();
                Assert.Equal(1, CountFrameSubscriptions(window));
                window.Hide();
            }
            window.Show();
            window.Close();
            Assert.Equal(0, CountFrameSubscriptions(window));
            Assert.Null(window.RenderTarget);
            Assert.Equal(nint.Zero, window.Handle);
        }
        finally { CloseAndRemoveTestSubscriptions(window); }
    }

    [Fact]
    public void CancelledCloseBeforeFirstShow_DoesNotCreateAFrameSubscription()
    {
        var window = new Window();
        EventHandler<CancelEventArgs> cancel = (_, e) => e.Cancel = true;
        window.Closing += cancel;
        try
        {
            for (int round = 0; round < 12; round++)
            {
                window.Close();
                Assert.Equal(0, CountFrameSubscriptions(window));
            }
        }
        finally
        {
            window.Closing -= cancel;
            CloseAndRemoveTestSubscriptions(window);
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void CancelledVisibleClose_RestoresExactlyOneFrameSubscription()
    {
        var window = new Window { Width = 240, Height = 160, ShowActivated = false };
        EventHandler<CancelEventArgs> cancel = (_, e) => e.Cancel = true;
        try
        {
            window.Show();
            window.Closing += cancel;
            for (int round = 0; round < 12; round++)
            {
                window.Close();
                Assert.Equal(1, CountFrameSubscriptions(window));
                Assert.NotEqual(nint.Zero, window.Handle);
                long before = window.FrameHistory.TotalFrames;
                window.ForceRenderFrame();
                Assert.True(window.FrameHistory.TotalFrames > before);
            }
        }
        finally
        {
            window.Closing -= cancel;
            CloseAndRemoveTestSubscriptions(window);
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void ThrowingClosingHandler_RestoresScheduling_AndCanCloseOnRetry()
    {
        var window = new Window { Width = 240, Height = 160, ShowActivated = false };
        EventHandler<CancelEventArgs> fail = (_, _) => throw new InvalidOperationException("closing callback");
        try
        {
            window.Show();
            window.Closing += fail;
            Assert.Throws<InvalidOperationException>(window.Close);
            Assert.Equal(1, CountFrameSubscriptions(window));
            long before = window.FrameHistory.TotalFrames;
            window.ForceRenderFrame();
            Assert.True(window.FrameHistory.TotalFrames > before);
            window.Closing -= fail;
            window.Close();
            Assert.Equal(nint.Zero, window.Handle);
            Assert.Equal(0, CountFrameSubscriptions(window));
        }
        finally
        {
            window.Closing -= fail;
            CloseAndRemoveTestSubscriptions(window);
        }
    }

    internal static int CountFrameSubscriptions(Window window)
        => (FrameStartingField.GetValue(null) as Delegate)?.GetInvocationList()
            .Count(callback => ReferenceEquals(callback.Target, window)) ?? 0;

    [Fact]
    public void ClosedWindow_CannotResurrectNativeResourcesOrFrameSubscription()
    {
        var window = new Window();
        window.Close();
        Assert.Throws<InvalidOperationException>(window.Show);
        Assert.Equal(nint.Zero, window.Handle);
        Assert.Null(window.RenderTarget);
        Assert.Equal(0, CountFrameSubscriptions(window));
    }

    private static void CloseAndRemoveTestSubscriptions(Window window)
    {
        // A failing pre-fix test must not retain its HWND or duplicate global
        // listeners and contaminate other tests in the Application collection.
        typeof(Window).GetField("_isClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, false);
        window.Close();
        foreach (var callback in (FrameStartingField.GetValue(null) as Delegate)?.GetInvocationList() ?? [])
        {
            if (ReferenceEquals(callback.Target, window))
                CompositionTarget.FrameStarting -= (Action)callback;
        }
    }
}
