using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Ink;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;

namespace Jalium.UI.Tests;

/// <summary>
/// Verifies that <see cref="InkCanvas"/> is wired into the real-time stylus
/// background-thread pipeline via <see cref="RealTimeInkPreviewStylusPlugIn"/>.
/// Run on the Jalium.RTS thread for OnStylus*, on the UI dispatcher for the
/// Processed callbacks + commit.
/// </summary>
[Collection("Application")]
public class InkCanvasRealTimeStylusPreviewTests
{
    [Fact]
    public void InkCanvas_HasRtsCapableFirstPlugIn()
    {
        var canvas = CreateInkCanvas();

        Assert.NotEmpty(canvas.StylusPlugIns);
        var first = canvas.StylusPlugIns[0];
        Assert.IsType<RealTimeInkPreviewStylusPlugIn>(first);
        Assert.True(first.IsRealTimeCapable, "Real-time preview plug-in must run on the RTS thread");
    }

    [Fact]
    public void StylusDown_OnBackgroundThread_StartsPreviewSession()
    {
        var canvas = CreateInkCanvas();
        var plugin = GetPreviewPlugIn(canvas);
        int uiThreadId = Environment.CurrentManagedThreadId;
        string? capturedThread = null;

        using var rts = new RealTimeStylus(canvas) { UseRealTimeThread = true };
        // Wrap the plug-in with a thread-capture probe inserted before it so
        // we can assert OnStylusDown actually ran off the UI thread.
        canvas.StylusPlugIns.Insert(0, new ThreadProbe(t => capturedThread = t) { ForceRealTime = true });

        var result = rts.Process(
            pointerId: 100, target: canvas, action: StylusInputAction.Down,
            stylusPoints: new StylusPointCollection(new[] { new StylusPoint(10, 10, 0.5f) }),
            timestamp: 0, inAir: false, inRange: true,
            barrelButtonPressed: false, eraserPressed: false, inverted: false, pointerCanceled: false);
        Pump(rts, result);

        Assert.NotNull(capturedThread);
        Assert.Equal("Jalium.RTS", capturedThread);
        // The preview plug-in opened a session for pointer 100.
        Assert.True(plugin.HasActiveSessions);
        var session = plugin.SnapshotSessions().Single(s => s.PointerId == 100);
        Assert.Equal(1, session.PointCount);
        // First Processed callback should have populated attrs on the UI thread.
        Assert.NotNull(session.Attributes);
        Assert.False(session.IsEraser);
    }

    [Fact]
    public void StylusMove_AppendsPointsAcrossPackets()
    {
        var canvas = CreateInkCanvas();
        var plugin = GetPreviewPlugIn(canvas);

        using var rts = new RealTimeStylus(canvas) { UseRealTimeThread = true };

        SendPacket(rts, canvas, pointerId: 7, action: StylusInputAction.Down, new[] { new StylusPoint(0, 0) });
        SendPacket(rts, canvas, pointerId: 7, action: StylusInputAction.Move, new[] { new StylusPoint(10, 10) });
        SendPacket(rts, canvas, pointerId: 7, action: StylusInputAction.Move, new[] { new StylusPoint(20, 20), new StylusPoint(30, 30) });

        var session = plugin.SnapshotSessions().Single(s => s.PointerId == 7);
        Assert.Equal(4, session.PointCount); // Down + Move(1) + Move(2)
    }

    [Fact]
    public void StylusUp_CommitsStrokeViaProcessedCallback()
    {
        var canvas = CreateInkCanvas();
        Stroke? collected = null;
        canvas.StrokeCollected += (_, e) => collected = e.Stroke;

        using var rts = new RealTimeStylus(canvas) { UseRealTimeThread = true };

        SendPacket(rts, canvas, pointerId: 11, action: StylusInputAction.Down, new[] { new StylusPoint(0, 0) });
        SendPacket(rts, canvas, pointerId: 11, action: StylusInputAction.Move, new[] { new StylusPoint(50, 50) });
        SendPacket(rts, canvas, pointerId: 11, action: StylusInputAction.Up, new[] { new StylusPoint(100, 100) });

        Assert.Single(canvas.Strokes);
        Assert.NotNull(collected);
        var plugin = GetPreviewPlugIn(canvas);
        Assert.False(plugin.HasActiveSessions);
        // Stroke has at least 3 points (the jitter floor only drops sub-0.5-px deltas).
        Assert.True(collected!.StylusPoints.Count >= 3);
    }

    [Fact]
    public void MultiPointer_ProducesIndependentSessions()
    {
        var canvas = CreateInkCanvas();
        var plugin = GetPreviewPlugIn(canvas);

        using var rts = new RealTimeStylus(canvas) { UseRealTimeThread = true };

        SendPacket(rts, canvas, pointerId: 1, action: StylusInputAction.Down, new[] { new StylusPoint(0, 0) });
        SendPacket(rts, canvas, pointerId: 2, action: StylusInputAction.Down, new[] { new StylusPoint(100, 100) });
        SendPacket(rts, canvas, pointerId: 1, action: StylusInputAction.Move, new[] { new StylusPoint(20, 20) });
        SendPacket(rts, canvas, pointerId: 2, action: StylusInputAction.Move, new[] { new StylusPoint(120, 120) });

        var sessions = plugin.SnapshotSessions();
        Assert.Equal(2, sessions.Length);
        Assert.Contains(sessions, s => s.PointerId == 1 && s.PointCount == 2);
        Assert.Contains(sessions, s => s.PointerId == 2 && s.PointCount == 2);

        SendPacket(rts, canvas, pointerId: 1, action: StylusInputAction.Up, new[] { new StylusPoint(30, 30) });
        Assert.Single(canvas.Strokes);
        Assert.Single(plugin.SnapshotSessions());

        SendPacket(rts, canvas, pointerId: 2, action: StylusInputAction.Up, new[] { new StylusPoint(130, 130) });
        Assert.Equal(2, canvas.Strokes.Count);
        Assert.Empty(plugin.SnapshotSessions());
    }

    [Fact]
    public void EditingModeChange_DropsInflightSession()
    {
        var canvas = CreateInkCanvas();
        var plugin = GetPreviewPlugIn(canvas);

        using var rts = new RealTimeStylus(canvas) { UseRealTimeThread = true };
        SendPacket(rts, canvas, pointerId: 42, action: StylusInputAction.Down, new[] { new StylusPoint(0, 0) });
        Assert.True(plugin.HasActiveSessions);

        canvas.EditingMode = InkCanvasEditingMode.EraseByStroke;

        Assert.False(plugin.HasActiveSessions);
        Assert.Empty(canvas.Strokes);
    }

    [Fact]
    public void EraseByPoint_FlagsSessionAsEraser_AndCommitsEraserStroke()
    {
        var canvas = CreateInkCanvas();
        canvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
        var plugin = GetPreviewPlugIn(canvas);

        using var rts = new RealTimeStylus(canvas) { UseRealTimeThread = true };
        SendPacket(rts, canvas, pointerId: 1, action: StylusInputAction.Down, new[] { new StylusPoint(0, 0) });
        SendPacket(rts, canvas, pointerId: 1, action: StylusInputAction.Move, new[] { new StylusPoint(50, 50) });

        var session = plugin.SnapshotSessions().Single();
        Assert.True(session.IsEraser);

        SendPacket(rts, canvas, pointerId: 1, action: StylusInputAction.Up, new[] { new StylusPoint(100, 100) });
        Assert.Single(canvas.Strokes);
        Assert.Same(
            Jalium.UI.Controls.Ink.Shaders.EraserBrushShader.Instance,
            canvas.Strokes[0].DrawingAttributes.BrushShader);
    }

    [Fact]
    public void UseRealTimeThread_False_StillRoutesThroughPreviewPlugIn()
    {
        // When the background thread is disabled (tests / debugging) the plug-in
        // still owns the preview + commit pipeline — it just runs synchronously.
        var canvas = CreateInkCanvas();
        using var rts = new RealTimeStylus(canvas) { UseRealTimeThread = false };

        SendPacket(rts, canvas, pointerId: 9, action: StylusInputAction.Down, new[] { new StylusPoint(0, 0) });
        SendPacket(rts, canvas, pointerId: 9, action: StylusInputAction.Up, new[] { new StylusPoint(50, 50) });

        Assert.Single(canvas.Strokes);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static InkCanvas CreateInkCanvas()
    {
        var canvas = new InkCanvas();
        canvas.Arrange(new Rect(0, 0, 400, 400));
        return canvas;
    }

    private static RealTimeInkPreviewStylusPlugIn GetPreviewPlugIn(InkCanvas canvas)
    {
        var field = typeof(InkCanvas).GetField("_realTimePreviewPlugIn",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (RealTimeInkPreviewStylusPlugIn)field!.GetValue(canvas)!;
    }

    private static void SendPacket(RealTimeStylus rts, InkCanvas target, uint pointerId,
        StylusInputAction action, IEnumerable<StylusPoint> points)
    {
        var collection = new StylusPointCollection(points);
        bool isUp = action == StylusInputAction.Up;
        var result = rts.Process(
            pointerId: pointerId, target: target, action: action,
            stylusPoints: collection,
            timestamp: 0, inAir: false, inRange: !isUp,
            barrelButtonPressed: false, eraserPressed: false,
            inverted: false, pointerCanceled: false);
        Pump(rts, result);
    }

    private static void Pump(RealTimeStylus rts, RealTimeStylusProcessResult result)
    {
        rts.QueueProcessedCallbacks(result);
        var dispatcher = Dispatcher.CurrentDispatcher ?? Dispatcher.GetForCurrentThread();
        dispatcher.ProcessQueue();
    }

    private sealed class ThreadProbe : StylusPlugIn
    {
        private readonly Action<string?> _capture;
        public ThreadProbe(Action<string?> capture) { _capture = capture; }
        public bool ForceRealTime
        {
            get => IsRealTimeCapable;
            set => IsRealTimeCapable = value;
        }
        protected override void OnStylusDown(RawStylusInput rawStylusInput)
            => _capture(Thread.CurrentThread.Name);
        protected override void OnStylusMove(RawStylusInput rawStylusInput)
            => _capture(Thread.CurrentThread.Name);
        protected override void OnStylusUp(RawStylusInput rawStylusInput)
            => _capture(Thread.CurrentThread.Name);
    }
}
