using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

/// <summary>
/// Locks the frame-presented handshake: successful Presents are queued by
/// serial, a compositor retire signal pops the oldest serial, and callers can
/// wait for a serial beyond the one they captured when committing dry ink.
/// </summary>
/// <remarks>
/// [Collection("Application")] is mandatory for the Window tests: they
/// construct Window instances and would race DependencyProperty metadata
/// registration with other Window-constructing collections.
/// </remarks>
[Collection("Application")]
public class PresentHandshakeTests
{
    [Fact]
    public void Tracker_RetiresQueuedSerialsInOrder()
    {
        var tracker = new PresentCompletionTracker();

        tracker.Queue(1);
        tracker.Queue(2);
        tracker.Queue(3);

        Assert.Equal(1, tracker.Retire());
        Assert.Equal(1, tracker.LastPresentedSerial);
        Assert.Equal(2, tracker.PendingCount);

        Assert.Equal(2, tracker.Retire());
        Assert.Equal(2, tracker.LastPresentedSerial);
        Assert.Equal(1, tracker.PendingCount);

        Assert.Equal(3, tracker.Retire());
        Assert.Equal(3, tracker.LastPresentedSerial);
        Assert.Equal(0, tracker.PendingCount);
        Assert.Equal(3, tracker.LastQueuedSerial);
    }

    [Fact]
    public void Tracker_RetireWithoutPendingPresentIsIgnored()
    {
        var tracker = new PresentCompletionTracker();

        Assert.Null(tracker.Retire());
        Assert.Equal(0, tracker.LastPresentedSerial);
    }

    [Fact]
    public void Tracker_NonPositiveSerialIsIgnored()
    {
        var tracker = new PresentCompletionTracker();

        tracker.Queue(0);
        tracker.Queue(-1);

        Assert.Equal(0, tracker.PendingCount);
        Assert.Null(tracker.Retire());
    }

    [Fact]
    public void Tracker_ClearDropsPendingWithoutAdvancingPresented()
    {
        var tracker = new PresentCompletionTracker();

        tracker.Queue(1);
        tracker.Clear();

        Assert.Equal(0, tracker.PendingCount);
        Assert.Equal(0, tracker.LastPresentedSerial);
        Assert.Null(tracker.Retire());
    }

    [Fact]
    public void Window_DisabledHandshake_DoesNotQueueOrRaise()
    {
        var (window, _) = CreateWindowWithWaitable(new nint(0x9001));
        window.IsPresentHandshakeEnabled = false;

        int raised = 0;
        window.FramePresented += (_, _) => raised++;

        InvokePrivate(window, "NoteSuccessfulPresent");
        InvokePrivate(window, "NoteSuccessfulPresent");
        InvokePrivate(window, "NotePresentRetired");

        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);
        Assert.Equal(0, raised);
        Assert.Equal(2L, GetPrivateField<long>(window, "_successfulPresentCount"));
    }

    [Fact]
    public void Window_EnabledHandshake_QueuesOnPresentAndRaisesOnRetire()
    {
        var (window, _) = CreateWindowWithWaitable(new nint(0x9002));
        window.IsPresentHandshakeEnabled = true;

        var serials = new List<long>();
        window.FramePresented += (_, args) => serials.Add(args.PresentSerial);

        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(1, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);
        Assert.Empty(serials);

        InvokePrivate(window, "NotePresentRetired");
        Assert.Equal(1, window.LastPresentedFrameSerial);
        Assert.Equal(new long[] { 1 }, serials);

        InvokePrivate(window, "NoteSuccessfulPresent");
        InvokePrivate(window, "NotePresentRetired");
        Assert.Equal(2, window.LastQueuedFrameSerial);
        Assert.Equal(2, window.LastPresentedFrameSerial);
        Assert.Equal(new long[] { 1, 2 }, serials);
    }

    [Fact]
    public void Window_NoWaitable_DoesNotQueue()
    {
        var (window, _) = CreateWindowWithWaitable(nint.Zero);
        window.IsPresentHandshakeEnabled = true;

        int raised = 0;
        window.FramePresented += (_, _) => raised++;

        Assert.False(window.IsPresentHandshakeSupported);

        InvokePrivate(window, "NoteSuccessfulPresent");
        InvokePrivate(window, "NotePresentRetired");

        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Window_DisableDropsPendingSerials()
    {
        var (window, _) = CreateWindowWithWaitable(new nint(0x9003));
        window.IsPresentHandshakeEnabled = true;

        int raised = 0;
        window.FramePresented += (_, _) => raised++;

        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(1, window.LastQueuedFrameSerial);

        window.IsPresentHandshakeEnabled = false;
        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);

        InvokePrivate(window, "NotePresentRetired");
        Assert.Equal(0, raised);

        window.IsPresentHandshakeEnabled = true;
        InvokePrivate(window, "NoteSuccessfulPresent");
        Assert.Equal(0, window.LastPresentedFrameSerial);
        // Present serials are monotonic for the window lifetime, so the
        // re-enabled tracker starts at 2 after the earlier successful Present.
        Assert.Equal(2, window.LastQueuedFrameSerial);
    }

    [Fact]
    public void Window_PresentFailure_DoesNotQueue()
    {
        var (window, native) = CreateWindowWithWaitable(new nint(0x9004));
        window.IsPresentHandshakeEnabled = true;
        native.EndDrawResult = (int)JaliumResult.PresentFailed;

        var renderTarget = (RenderTarget)GetPrivateProperty(window, "RenderTarget")!;
        Assert.True(renderTarget.TryBeginDraw());

        int raised = 0;
        window.FramePresented += (_, _) => raised++;

        bool presented = (bool)InvokePrivate(window, "CompleteEndDrawOrHandleFailure")!;

        Assert.False(presented);
        Assert.Equal(0, window.LastQueuedFrameSerial);
        Assert.Equal(0, window.LastPresentedFrameSerial);
        Assert.Equal(0, raised);
        Assert.Equal(0L, GetPrivateField<long>(window, "_successfulPresentCount"));
    }

    [Fact]
    public void Window_SubscriberFailure_DoesNotBlockRemainingSubscribers()
    {
        var (window, _) = CreateWindowWithWaitable(new nint(0x9005));
        window.IsPresentHandshakeEnabled = true;

        int healthyCalls = 0;
        window.FramePresented += (_, _) => throw new InvalidOperationException("Injected test failure.");
        window.FramePresented += (_, _) => healthyCalls++;

        InvokePrivate(window, "NoteSuccessfulPresent");
        InvokePrivate(window, "NotePresentRetired");

        Assert.Equal(1, healthyCalls);
        Assert.Equal(1, window.LastPresentedFrameSerial);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static (Window window, RenderTargetTestNative native) CreateWindowWithWaitable(nint waitable)
    {
        var window = new Window { Width = 300, Height = 200 };
        var native = new RenderTargetTestNative { FrameLatencyWaitableHandle = waitable };
        var renderTarget = new RenderTarget(
            backend: RenderBackend.D3D12,
            contextHandle: new nint(0x1234),
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(new nint(0x2001)),
            width: 300,
            height: 200,
            useComposition: false,
            native: native);

        SetPrivateField(window, "_dispatcher", Dispatcher.GetForCurrentThread());
        SetPrivateProperty(window, "RenderTarget", renderTarget);
        return (window, native);
    }

    private static object? InvokePrivate(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try
        {
            return method!.Invoke(instance, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(instance)!;
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void SetPrivateProperty(object instance, string propertyName, object? value)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private static object? GetPrivateProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return property!.GetValue(instance);
    }
}
