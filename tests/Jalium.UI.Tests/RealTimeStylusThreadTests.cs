using Jalium.UI;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class RealTimeStylusThreadTests
{
    [Fact]
    public void Constructor_WithoutRealTimeWork_DoesNotStartWorkerThread()
    {
        int baseline = RealTimeStylus.ActiveThreadCount;

        using var rts = new RealTimeStylus(new RtsTestElement());

        Assert.Equal(baseline, RealTimeStylus.ActiveThreadCount);
    }

    [Fact]
    public void Dispose_AfterRealTimeWork_StopsWorkerThread_AndIsIdempotent()
    {
        int baseline = RealTimeStylus.ActiveThreadCount;
        var root = new RtsTestElement();
        root.GetStylusPlugIns(createIfMissing: true)!
            .Add(new ThreadCapturePlugIn(realTime: true));
        var rts = new RealTimeStylus(root);

        rts.Process(
            pointerId: 700, target: root, action: StylusInputAction.Down,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(0, 0, 0.5f) }),
            timestamp: 0, inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false, inverted: false, pointerCanceled: false);

        Assert.Equal(baseline + 1, RealTimeStylus.ActiveThreadCount);

        rts.Dispose();
        rts.Dispose();

        Assert.True(
            SpinWait.SpinUntil(
                () => RealTimeStylus.ActiveThreadCount == baseline,
                TimeSpan.FromSeconds(2)),
            $"RTS worker count remained {RealTimeStylus.ActiveThreadCount}; expected {baseline}.");
    }

    [Fact]
    public void AbandonedOwnerCycle_IsCollectible_AndFinalizerStopsWorkerThread()
    {
        int baseline = RealTimeStylus.ActiveThreadCount;
        (WeakReference owner, WeakReference rts) = CreateAbandonedOwnerCycle();

        Assert.Equal(baseline + 1, RealTimeStylus.ActiveThreadCount);

        for (int attempt = 0;
             attempt < 5 && (owner.IsAlive || rts.IsAlive || RealTimeStylus.ActiveThreadCount != baseline);
             attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(20);
        }

        Assert.False(
            owner.IsAlive,
            $"The RTS worker retained its owner/root element; rtsAlive={rts.IsAlive}, " +
            $"activeThreads={RealTimeStylus.ActiveThreadCount}, baseline={baseline}.");
        Assert.False(
            rts.IsAlive,
            $"The RTS worker retained RealTimeStylus; activeThreads={RealTimeStylus.ActiveThreadCount}, " +
            $"baseline={baseline}.");
        Assert.True(
            SpinWait.SpinUntil(
                () => RealTimeStylus.ActiveThreadCount == baseline,
                TimeSpan.FromSeconds(2)),
            $"Finalized RTS worker count remained {RealTimeStylus.ActiveThreadCount}; expected {baseline}.");
    }

    [Fact]
    public void CustomStylusData_AddedFromOnePlugIn_IsVisibleToLaterPlugIns()
    {
        var root = new RtsTestElement();
        Guid key = Guid.NewGuid();
        var writer = new CustomDataWriterPlugIn(key, "hello");
        var reader = new CustomDataReaderPlugIn(key);
        root.GetStylusPlugIns(createIfMissing: true)!.Add(writer);
        root.GetStylusPlugIns(createIfMissing: true)!.Add(reader);

        using var rts = new RealTimeStylus(root) { UseRealTimeThread = false };
        rts.Process(
            pointerId: 1, target: root, action: StylusInputAction.Down,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(0, 0, 0.5f) }),
            timestamp: 0, inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false, inverted: false, pointerCanceled: false);

        Assert.Equal("hello", reader.Observed);
    }

    [Fact]
    public void RealTimeThread_RtsCapablePlugIn_RunsOffUiThread()
    {
        var root = new RtsTestElement();
        var probe = new ThreadCapturePlugIn(realTime: true);
        root.GetStylusPlugIns(createIfMissing: true)!.Add(probe);

        using var rts = new RealTimeStylus(root) { UseRealTimeThread = true };
        int uiThreadId = Environment.CurrentManagedThreadId;
        rts.Process(
            pointerId: 2, target: root, action: StylusInputAction.Down,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(0, 0, 0.5f) }),
            timestamp: 0, inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false, inverted: false, pointerCanceled: false);

        Assert.True(probe.CapturedThreadId.HasValue, "plug-in did not run");
        Assert.NotEqual(uiThreadId, probe.CapturedThreadId!.Value);
        Assert.Equal("Jalium.RTS", probe.CapturedThreadName);
    }

    [Fact]
    public void RealTimeThread_NonRtsCapablePlugIn_RunsOnCallingThread()
    {
        var root = new RtsTestElement();
        var probe = new ThreadCapturePlugIn(realTime: false);
        root.GetStylusPlugIns(createIfMissing: true)!.Add(probe);

        using var rts = new RealTimeStylus(root) { UseRealTimeThread = true };
        int uiThreadId = Environment.CurrentManagedThreadId;
        rts.Process(
            pointerId: 3, target: root, action: StylusInputAction.Down,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(0, 0, 0.5f) }),
            timestamp: 0, inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false, inverted: false, pointerCanceled: false);

        Assert.True(probe.CapturedThreadId.HasValue, "plug-in did not run");
        Assert.Equal(uiThreadId, probe.CapturedThreadId!.Value);
    }

    [Fact]
    public void CustomStylusData_FromRtsThreadPlugIn_VisibleInProcessedCallbackOnUi()
    {
        var root = new RtsTestElement();
        Guid key = Guid.NewGuid();
        var rtsWriter = new CustomDataWriterPlugIn(key, "rts-payload") { ForceRealTime = true };
        var uiReader = new ProcessedCallbackReaderPlugIn(key);
        root.GetStylusPlugIns(createIfMissing: true)!.Add(rtsWriter);
        root.GetStylusPlugIns(createIfMissing: true)!.Add(uiReader);

        using var rts = new RealTimeStylus(root) { UseRealTimeThread = true };
        var result = rts.Process(
            pointerId: 4, target: root, action: StylusInputAction.Down,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(0, 0, 0.5f) }),
            timestamp: 0, inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false, inverted: false, pointerCanceled: false);

        // QueueProcessedCallbacks dispatches to the UI Dispatcher.BeginInvoke;
        // pump it synchronously by running pending dispatcher operations.
        rts.QueueProcessedCallbacks(result);
        Dispatcher.CurrentDispatcher.ProcessQueue();

        Assert.Equal("rts-payload", uiReader.Observed);
    }

    [Fact]
    public void RtsThread_PlugInException_CancelsSession_DoesNotCrashLoop()
    {
        var root = new RtsTestElement();
        var bad = new ThrowingPlugIn { ForceRealTime = true };
        var after = new MarkerPlugIn();
        root.GetStylusPlugIns(createIfMissing: true)!.Add(bad);
        root.GetStylusPlugIns(createIfMissing: true)!.Add(after);

        using var rts = new RealTimeStylus(root) { UseRealTimeThread = true };
        var result = rts.Process(
            pointerId: 5, target: root, action: StylusInputAction.Down,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(0, 0, 0.5f) }),
            timestamp: 0, inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false, inverted: false, pointerCanceled: false);

        Assert.True(result.Canceled);
        if (after.WasInvoked)
        {
            throw new Exception(
                $"after.WasInvoked={after.WasInvoked} onThread={after.InvokedOnThread} " +
                $"rawIsCanceledAtInvoke={after.RawIsCanceledAtInvoke}; " +
                $"result.Canceled={result.Canceled}; rawIsCanceled={result.RawStylusInput.IsCanceled}");
        }

        // A subsequent packet must still work (loop survived).
        var ok = rts.Process(
            pointerId: 6, target: root, action: StylusInputAction.Down,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(1, 1, 0.5f) }),
            timestamp: 1, inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false, inverted: false, pointerCanceled: false);
        Assert.False(ok.Canceled);
    }

    private sealed class RtsTestElement : FrameworkElement
    {
        public void AddChild(UIElement child) => AddVisualChild(child);
    }

    private sealed class OwningRtsTestElement : FrameworkElement
    {
        public RealTimeStylus? RealTimeStylus { get; set; }
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference owner, WeakReference rts) CreateAbandonedOwnerCycle()
    {
        var owner = new OwningRtsTestElement();
        owner.GetStylusPlugIns(createIfMissing: true)!
            .Add(new ThreadCapturePlugIn(realTime: true));
        var rts = new RealTimeStylus(owner);
        owner.RealTimeStylus = rts;

        rts.Process(
            pointerId: 701, target: owner, action: StylusInputAction.Up,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(0, 0, 0.5f) }),
            timestamp: 0, inAir: false, inRange: false,
            barrelButtonPressed: false, eraserPressed: false, inverted: false, pointerCanceled: false);

        return (new WeakReference(owner), new WeakReference(rts));
    }

    private sealed class CustomDataWriterPlugIn : StylusPlugIn
    {
        private readonly Guid _id;
        private readonly object _payload;
        public CustomDataWriterPlugIn(Guid id, object payload) { _id = id; _payload = payload; }
        public bool ForceRealTime
        {
            get => IsRealTimeCapable;
            set => IsRealTimeCapable = value;
        }
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            rawStylusInput.AddCustomData(_id, _payload);
            rawStylusInput.NotifyWhenProcessed(rawStylusInput);
        }
    }

    private sealed class CustomDataReaderPlugIn : StylusPlugIn
    {
        private readonly Guid _id;
        public CustomDataReaderPlugIn(Guid id) { _id = id; }
        public object? Observed { get; private set; }
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            Observed = rawStylusInput.GetCustomData<object>(_id);
        }
    }

    private sealed class ProcessedCallbackReaderPlugIn : StylusPlugIn
    {
        private readonly Guid _id;
        public ProcessedCallbackReaderPlugIn(Guid id) { _id = id; }
        public object? Observed { get; private set; }
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            rawStylusInput.NotifyWhenProcessed(rawStylusInput);
        }
        protected override void OnStylusDownProcessed(object callbackData, bool targetVerified)
        {
            if (callbackData is RawStylusInput rawStylusInput)
                Observed = rawStylusInput.GetCustomData<object>(_id);
        }
    }

    private sealed class ThreadCapturePlugIn : StylusPlugIn
    {
        public ThreadCapturePlugIn(bool realTime) { IsRealTimeCapable = realTime; }
        public int? CapturedThreadId { get; private set; }
        public string? CapturedThreadName { get; private set; }
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            CapturedThreadId = Environment.CurrentManagedThreadId;
            CapturedThreadName = Thread.CurrentThread.Name;
        }
    }

    private sealed class ThrowingPlugIn : StylusPlugIn
    {
        private int _count;
        public bool ForceRealTime
        {
            get => IsRealTimeCapable;
            set => IsRealTimeCapable = value;
        }
        public bool IsRealTimeCapableTest => IsRealTimeCapable;
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            // Throw on the first packet only — second packet returns normally so
            // the test can assert the RTS loop kept running.
            if (System.Threading.Interlocked.Increment(ref _count) == 1)
                throw new InvalidOperationException("plug-in failure");
        }
    }

    private sealed class MarkerPlugIn : StylusPlugIn
    {
        public bool WasInvoked { get; private set; }
        public string? InvokedOnThread { get; private set; }
        public bool RawIsCanceledAtInvoke { get; private set; }
        public bool IsRealTimeCapableTest => IsRealTimeCapable;
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            WasInvoked = true;
            InvokedOnThread = Thread.CurrentThread.Name ?? $"unnamed#{Environment.CurrentManagedThreadId}";
            RawIsCanceledAtInvoke = rawStylusInput.IsCanceled;
        }
    }
}
