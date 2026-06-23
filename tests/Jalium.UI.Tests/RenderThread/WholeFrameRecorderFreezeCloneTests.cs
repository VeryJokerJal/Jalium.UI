using Jalium.UI;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Xunit;
using Drawing = Jalium.UI.Media.Rendering.Drawing;

namespace Jalium.UI.Tests.RenderThread;

/// <summary>
/// Increment-3 (freeze-clone) verification: on the whole-frame capture path,
/// mutable live draw inputs are snapshotted at record time, so a later
/// UI-thread mutation cannot corrupt the captured Drawing replayed on the
/// render thread. Immutable / pooled inputs pass through untouched.
/// </summary>
public sealed class WholeFrameRecorderFreezeCloneTests : System.IDisposable
{
    // Guard against a mid-record assertion leaking s_wholeFrameRecording=true into a
    // sibling test (FinishRecord exits the scope on the happy path).
    public void Dispose() => DrawingContext.EndWholeFrameRecordingScope((false, false));

    [Fact]
    public void Transform_IsSnapshotted_ImmuneToLaterMutation()
    {
        var scale = new ScaleTransform { ScaleX = 2, ScaleY = 2 };

        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder()!;
        recorder.PushTransform(scale);
        recorder.Pop();
        var drawing = (Drawing)host.FinishRecord(recorder);

        scale.ScaleX = 99;   // mutate AFTER record — must not leak into the snapshot
        scale.ScaleY = 99;

        var sink = new RecordingRenderSink();
        host.Replay(drawing, sink);

        var push = Assert.Single(sink.Events, e => e.StartsWith("PushTransform"));
        Assert.Contains("[2,", push);          // record-time scale preserved
        Assert.DoesNotContain("99", push);     // mutation did not leak
    }

    [Fact]
    public void GradientBrush_IsSnapshotted_ImmuneToLaterStopMutation()
    {
        var grad = new LinearGradientBrush(
            Color.FromArgb(255, 255, 0, 0),
            Color.FromArgb(255, 0, 0, 255), 0);

        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder()!;
        recorder.DrawRectangle(grad, null, new Rect(0, 0, 10, 10));
        var drawing = (Drawing)host.FinishRecord(recorder);

        grad.GradientStops[0].Color = Color.FromArgb(255, 0, 255, 0);  // red → green AFTER record

        var sink = new RecordingRenderSink();
        host.Replay(drawing, sink);

        var draw = Assert.Single(sink.Events, e => e.StartsWith("DrawRectangle"));
        Assert.Contains("FFFF0000", draw);        // original red stop preserved
        Assert.DoesNotContain("FF00FF00", draw);  // mutated green did not leak
    }

    [Fact]
    public void GradientSnapshot_IsStable_AcrossUnchangedFrames()
    {
        // Native brush cache is keyed by managed identity, so an UNCHANGED
        // gradient must yield the SAME snapshot instance across frames (else
        // every frame would miss the native cache and rebuild the gradient).
        var grad = new LinearGradientBrush(
            Color.FromArgb(255, 1, 2, 3),
            Color.FromArgb(255, 4, 5, 6), 0);

        var host = new MediaRenderCacheHost();

        var r1 = host.CreateFrameRecorder()!;
        r1.DrawRectangle(grad, null, new Rect(0, 0, 10, 10));
        var d1 = (Drawing)host.FinishRecord(r1);

        var r2 = host.CreateFrameRecorder()!;
        r2.DrawRectangle(grad, null, new Rect(0, 0, 10, 10));
        var d2 = (Drawing)host.FinishRecord(r2);

        Assert.Same(d1.Commands[0].A, d2.Commands[0].A);  // same memoised snapshot
    }

    [Fact]
    public void SolidColorBrush_PassesThrough_NotOverCloned()
    {
        // Solids are already pooled value copies; the snapshot must return them
        // unchanged so the native brush identity cache keeps hitting.
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder()!;
        var solid = new SolidColorBrush { Color = Color.FromArgb(255, 10, 20, 30) };
        recorder.DrawRectangle(solid, null, new Rect(0, 0, 10, 10));
        var drawing = (Drawing)host.FinishRecord(recorder);

        var sink = new RecordingRenderSink();
        host.Replay(drawing, sink);

        Assert.Contains(sink.Events, e => e.Contains("#FF0A141E"));  // color preserved
    }

    [Fact]
    public void GradientScalarAnimation_NotFrozen_ReSnapshotsAcrossFrames()
    {
        // Regression for the memo-stale bug (reviewer3#3): gradient scalar setters
        // (StartPoint/EndPoint/Center/Radius/Opacity) do NOT call InvalidateContentHash,
        // so a CACHED ComputeContentHash would freeze an animated gradient on the
        // render-thread path. SnapshotGradient now keys on the UNCACHED
        // ComputeContentHashCore, so an animated StartPoint must re-snapshot each frame.
        var grad = new LinearGradientBrush(
            Color.FromArgb(255, 255, 0, 0), Color.FromArgb(255, 0, 0, 255), 0)
        { StartPoint = new Point(0, 0) };
        var host = new MediaRenderCacheHost();

        var r1 = host.CreateFrameRecorder()!;
        r1.DrawRectangle(grad, null, new Rect(0, 0, 10, 10));
        var d1 = (Drawing)host.FinishRecord(r1);
        var snap1 = d1.Commands[0].A;

        grad.StartPoint = new Point(1, 1);   // scalar animation (no GradientStops.Changed)

        var r2 = host.CreateFrameRecorder()!;
        r2.DrawRectangle(grad, null, new Rect(0, 0, 10, 10));
        var d2 = (Drawing)host.FinishRecord(r2);
        var snap2 = d2.Commands[0].A;

        Assert.NotSame(snap1, snap2);   // re-snapshotted (Same == frozen — the pre-fix bug)
        Assert.Equal(new Point(1, 1), ((LinearGradientBrush)snap2!).StartPoint);
    }
}
