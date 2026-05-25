using System.Diagnostics;
using Jalium.UI;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class RealTimeStylusAsyncTests
{
    private static StylusPointCollection P(double x, double y) =>
        new(new[] { new StylusPoint(x, y, 0.5f) });

    [Fact]
    public void BeginProcess_WithRtsPlugIn_ReturnsBeforeRtsPlugInCompletes()
    {
        var root = new AsyncRtsTestElement();
        var sleeper = new SleepingRtsPlugIn(durationMs: 150);
        root.StylusPlugIns.Add(sleeper);

        using var rts = new RealTimeStylus(root) { UseRealTimeThread = true };

        // Wait for the RTS thread to be ready before timing so the first-call
        // thread-spinup cost doesn't pollute the measurement.
        var warmup = new ManualResetEventSlim();
        rts.BeginProcess(
            pointerId: 99, target: root, action: StylusInputAction.Down,
            stylusPoints: P(0, 0), timestamp: 0,
            inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false,
            inverted: false, pointerCanceled: false,
            onCompleted: _ => warmup.Set());
        warmup.Wait(TimeSpan.FromSeconds(2));

        // Real timing measurement.
        var sw = Stopwatch.StartNew();
        rts.BeginProcess(
            pointerId: 100, target: root, action: StylusInputAction.Down,
            stylusPoints: P(0, 0), timestamp: 0,
            inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false,
            inverted: false, pointerCanceled: false,
            onCompleted: _ => { });
        sw.Stop();

        // BeginProcess must return well before the 150-ms sleeping plug-in finishes.
        // Allow a 50-ms ceiling for the enqueue + first-time RTS-thread wakeup.
        Assert.True(sw.ElapsedMilliseconds < 50,
            $"BeginProcess should be non-blocking, took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void BeginProcess_Completion_RunsOnUiDispatcher()
    {
        var root = new AsyncRtsTestElement();
        var probe = new ThreadCapturePlugIn { ForceRealTime = true };
        root.StylusPlugIns.Add(probe);

        using var rts = new RealTimeStylus(root) { UseRealTimeThread = true };
        var completedThreadId = 0;
        var done = new ManualResetEventSlim();
        int uiThreadId = Environment.CurrentManagedThreadId;

        rts.BeginProcess(
            pointerId: 101, target: root, action: StylusInputAction.Down,
            stylusPoints: P(0, 0), timestamp: 0,
            inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false,
            inverted: false, pointerCanceled: false,
            onCompleted: _ =>
            {
                completedThreadId = Environment.CurrentManagedThreadId;
                done.Set();
            });

        // Pump dispatcher until the continuation runs.
        var dispatcher = Dispatcher.CurrentDispatcher ?? Dispatcher.GetForCurrentThread();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!done.IsSet && DateTime.UtcNow < deadline)
        {
            dispatcher.ProcessQueue();
            Thread.Sleep(5);
        }

        Assert.True(done.IsSet, "Continuation did not run within 2 s");
        Assert.Equal(uiThreadId, completedThreadId);
        Assert.NotEqual(uiThreadId, probe.CapturedThreadId!.Value);
    }

    [Fact]
    public void BeginProcess_PlugInMutationVisibleInContinuation()
    {
        var root = new AsyncRtsTestElement();
        Guid key = Guid.NewGuid();
        var mutator = new MutateAndCarryPlugIn(key, payload: "from-rts");
        root.StylusPlugIns.Add(mutator);

        using var rts = new RealTimeStylus(root) { UseRealTimeThread = true };
        StylusPointCollection? observedPoints = null;
        object? observedPayload = null;
        var done = new ManualResetEventSlim();

        rts.BeginProcess(
            pointerId: 102, target: root, action: StylusInputAction.Down,
            stylusPoints: P(0, 0), timestamp: 0,
            inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false,
            inverted: false, pointerCanceled: false,
            onCompleted: r =>
            {
                observedPoints = r.RawStylusInput.GetStylusPoints();
                observedPayload = r.RawStylusInput.GetCustomData<object>(key);
                done.Set();
            });

        var dispatcher = Dispatcher.CurrentDispatcher ?? Dispatcher.GetForCurrentThread();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!done.IsSet && DateTime.UtcNow < deadline)
        {
            dispatcher.ProcessQueue();
            Thread.Sleep(5);
        }

        Assert.True(done.IsSet, "Continuation did not run");
        Assert.NotNull(observedPoints);
        Assert.Single(observedPoints!);
        Assert.Equal(999, observedPoints![0].X, precision: 3); // mutator set X = 999
        Assert.Equal("from-rts", observedPayload);
    }

    [Fact]
    public void BeginProcess_NoRtsCapablePlugIns_RunsSynchronously()
    {
        var root = new AsyncRtsTestElement();
        var ui = new ThreadCapturePlugIn(); // IsRealTimeCapable = false by default
        root.StylusPlugIns.Add(ui);

        using var rts = new RealTimeStylus(root) { UseRealTimeThread = true };
        int uiThreadId = Environment.CurrentManagedThreadId;
        bool completedSynchronously = false;

        rts.BeginProcess(
            pointerId: 103, target: root, action: StylusInputAction.Down,
            stylusPoints: P(0, 0), timestamp: 0,
            inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false,
            inverted: false, pointerCanceled: false,
            onCompleted: _ => completedSynchronously = true);

        Assert.True(completedSynchronously, "No-RTS-plug-in path should run continuation inline before BeginProcess returns");
        Assert.Equal(uiThreadId, ui.CapturedThreadId!.Value);
    }

    private sealed class AsyncRtsTestElement : FrameworkElement
    {
    }

    private sealed class SleepingRtsPlugIn : StylusPlugIn
    {
        private readonly int _durationMs;
        public SleepingRtsPlugIn(int durationMs)
        {
            _durationMs = durationMs;
            IsRealTimeCapable = true;
        }
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            Thread.Sleep(_durationMs);
        }
    }

    private sealed class ThreadCapturePlugIn : StylusPlugIn
    {
        public int? CapturedThreadId { get; private set; }
        public bool ForceRealTime
        {
            get => IsRealTimeCapable;
            set => IsRealTimeCapable = value;
        }
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
            => CapturedThreadId = Environment.CurrentManagedThreadId;
    }

    private sealed class MutateAndCarryPlugIn : StylusPlugIn
    {
        private readonly Guid _key;
        private readonly object _payload;
        public MutateAndCarryPlugIn(Guid key, object payload)
        {
            _key = key;
            _payload = payload;
            IsRealTimeCapable = true;
        }
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            rawStylusInput.SetStylusPoints(new StylusPointCollection(new[] { new StylusPoint(999, 999, 1.0f) }));
            rawStylusInput.AddCustomData(_key, _payload);
        }
    }
}
