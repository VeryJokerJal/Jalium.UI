using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Xunit;
using Drawing = Jalium.UI.Media.Rendering.Drawing;

namespace Jalium.UI.Tests.RenderThread;

/// <summary>
/// Increment-1 verification for the JALIUM_RENDER_THREAD whole-frame capture
/// path: a tree recorded with the whole-frame recorder and replayed must
/// produce the same draw stream as a direct <c>Render</c> walk, the schema-gap
/// guard must flag un-recordable frames, and no new <see cref="DrawingContext"/>
/// virtual may ship without a recordable command.
/// </summary>
public sealed class WholeFrameRecorderTests : System.IDisposable
{
    // Reset the whole-frame thread-static between tests so a mid-record assertion
    // failure can't leak s_wholeFrameRecording=true into a sibling test.
    public void Dispose() => DrawingContext.EndWholeFrameRecordingScope((false, false));

    private static Border BuildTree()
    {
        return new Border
        {
            Background = new SolidColorBrush { Color = Color.FromArgb(255, 240, 240, 240) },
            BorderBrush = new SolidColorBrush { Color = Color.FromArgb(255, 0, 0, 0) },
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Hello", Width = 120, Height = 20 },
                    new Border
                    {
                        Background = new SolidColorBrush { Color = Color.FromArgb(255, 200, 40, 40) },
                        Width = 80,
                        Height = 24,
                        Opacity = 0.5,
                    },
                    new TextBlock { Text = "World", Width = 120, Height = 20 },
                },
            },
        };
    }

    [Fact]
    public void WholeFrameCapture_ReplayMatchesDirectRender()
    {
        var root = BuildTree();
        root.Measure(new Size(400, 300));
        root.Arrange(new Rect(0, 0, 400, 300));

        // DIRECT: walk the tree straight into a recording sink (no per-visual
        // cache, since the sink is not ICacheableDrawingContext).
        var direct = new RecordingRenderSink();
        root.Render(direct);

        // RECORD → REPLAY through the whole-frame recorder. Use a local host so
        // the test never perturbs the process-wide Visual.RenderCacheHost.
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder();
        Assert.NotNull(recorder);
        root.Render(recorder!);
        var drawing = (Drawing)host.FinishRecord(recorder!);

        Assert.True(drawing.IsFullyRecordable);

        var replay = new RecordingRenderSink();
        host.Replay(drawing, replay);

        // Primary gate: the full ordered, value-tokenised stream is identical.
        Assert.Equal(direct.Events, replay.Events);
    }

    [Fact]
    public void WholeFrameCapture_TransformOrigin_RoundTripsIdentically()
    {
        // Exercises ITransformDrawingContext.PushTransform(transform, originX, originY):
        // the recorder composes T(-o)*M*T(+o) into a matrix; RecordingRenderSink mirrors
        // it. A divergence in that origin-composition would diverge the streams here.
        // RenderTransform must be on a CHILD: a top-level visual's own transform is
        // applied by its parent, and the root has none — so put it on a child Border
        // whose parent emits ITransformDrawingContext.PushTransform(transform, ox, oy).
        var root = new Border
        {
            Width = 100,
            Height = 100,
            Background = new SolidColorBrush { Color = Color.FromArgb(255, 1, 2, 3) },
            Child = new Border
            {
                Width = 50,
                Height = 50,
                Background = new SolidColorBrush { Color = Color.FromArgb(255, 9, 8, 7) },
                RenderTransform = new ScaleTransform { ScaleX = 1.5, ScaleY = 2.0 },
                RenderTransformOrigin = new Point(0.5, 0.5),
            },
        };
        root.Measure(new Size(200, 200));
        root.Arrange(new Rect(0, 0, 200, 200));

        var direct = new RecordingRenderSink();
        root.Render(direct);

        var host = new MediaRenderCacheHost();
        var rec = host.CreateFrameRecorder()!;
        root.Render(rec);
        var drawing = (Drawing)host.FinishRecord(rec);

        var replay = new RecordingRenderSink();
        host.Replay(drawing, replay);

        Assert.Equal(direct.Events, replay.Events);
        Assert.Contains(replay.Events, e => e.StartsWith("PushTransform"));
    }

    [Fact]
    public void WholeFrameCapture_PushPopBalanced_AndOffsetsRoundTrip()
    {
        var root = BuildTree();
        root.Measure(new Size(400, 300));
        root.Arrange(new Rect(0, 0, 400, 300));

        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder()!;
        root.Render(recorder);
        var drawing = (Drawing)host.FinishRecord(recorder);

        var replay = new RecordingRenderSink();
        host.Replay(drawing, replay);

        int pushes = replay.Events.Count(e =>
            e.StartsWith("PushTransform") || e.StartsWith("PushClip") || e.StartsWith("PushOpacity") || e.StartsWith("PushEffect"));
        int pops = replay.Events.Count(e => e == "Pop" || e == "PopEffect");
        Assert.Equal(pushes, pops);

        // The opacity child forces at least one SetOffset (StackPanel positions
        // its children) and one PushOpacity round-trip.
        Assert.Contains(replay.Events, e => e.StartsWith("SetOffset"));
        Assert.Contains(replay.Events, e => e.StartsWith("PushOpacity"));
    }

    [Fact]
    public void FullyRecordableTree_DoesNotFlagFallback()
    {
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder()!;
        recorder.DrawRectangle(new SolidColorBrush { Color = Color.FromArgb(255, 10, 20, 30) }, null, new Rect(0, 0, 10, 10));
        var drawing = (Drawing)host.FinishRecord(recorder);
        Assert.True(drawing.IsFullyRecordable);
    }

    [Fact]
    public void UnrecordableContent_FlagsFrameForFallback()
    {
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder()!;
        recorder.DrawRectangle(new SolidColorBrush { Color = Color.FromArgb(255, 10, 20, 30) }, null, new Rect(0, 0, 10, 10));
        // Simulate a call site that hit content with no DrawCommand
        // representation (e.g. WebView windowless punch) during the capture.
        DrawingContext.MarkCurrentFrameUnrecordable();
        var drawing = (Drawing)host.FinishRecord(recorder);
        Assert.False(drawing.IsFullyRecordable);
    }

    [Fact]
    public void MarkUnrecordable_IsNoOp_OutsideWholeFrameScope()
    {
        // No active whole-frame recording → must not throw or leak state.
        Assert.False(DrawingContext.IsWholeFrameRecording);
        DrawingContext.MarkCurrentFrameUnrecordable();
        Assert.False(DrawingContext.IsWholeFrameRecording);
    }

    [Fact]
    public void EmptyButUnrecordable_StillFlagsFallback()
    {
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder()!;
        // No draws at all, but a gap was hit → must not return the shared
        // fully-recordable Empty.
        DrawingContext.MarkCurrentFrameUnrecordable();
        var drawing = (Drawing)host.FinishRecord(recorder);
        Assert.False(drawing.IsFullyRecordable);
    }

    /// <summary>
    /// Durable defense against future silent drops: every method on
    /// RenderTargetDrawingContext that OVERRIDES a <see cref="DrawingContext"/>
    /// virtual must have a recordable DrawCommand equivalent, else the
    /// whole-frame recorder would drop it on the render-thread path. RTDC-only
    /// methods (no base virtual) are out of scope here — they are guarded at
    /// their call sites via MarkCurrentFrameUnrecordable.
    /// </summary>
    [Fact]
    public void EveryRtdcOverrideOfDrawingContext_HasARecordableCommand()
    {
        // Names backed 1:1 by a DrawCommandKind (see DrawCommand.cs) — the draw
        // surface the recorder captures. Keep in sync when the schema grows.
        var recordable = new HashSet<string>
        {
            nameof(DrawingContext.DrawLine),
            nameof(DrawingContext.DrawRectangle),
            nameof(DrawingContext.DrawRoundedRectangle),
            nameof(DrawingContext.DrawContentBorder),
            nameof(DrawingContext.DrawEllipse),
            nameof(DrawingContext.DrawPoints),
            nameof(DrawingContext.DrawLines),
            nameof(DrawingContext.DrawText),
            nameof(DrawingContext.DrawGeometry),
            nameof(DrawingContext.DrawImage),
            nameof(DrawingContext.DrawBackdropEffect),
            nameof(DrawingContext.DrawLiquidGlass),
            nameof(DrawingContext.PushTransform),
            nameof(DrawingContext.PushClip),
            nameof(DrawingContext.PushOpacity),
            nameof(DrawingContext.Pop),
            nameof(DrawingContext.PushEffect),
            nameof(DrawingContext.PopEffect),
            nameof(DrawingContext.Close),
        };

        var dc = typeof(DrawingContext);
        var rtdc = typeof(Jalium.UI.Interop.RenderTargetDrawingContext);

        var offenders = new List<string>();
        foreach (var m in rtdc.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.IsSpecialName) continue;             // skip property accessors
            if (m.Name == "Dispose") continue;         // IDisposable lifecycle, not a draw
            var baseDef = m.GetBaseDefinition();
            if (baseDef.DeclaringType == dc)           // overrides a DrawingContext virtual
            {
                if (!recordable.Contains(m.Name))
                    offenders.Add(m.Name);
            }
        }

        Assert.True(offenders.Count == 0,
            "RenderTargetDrawingContext overrides DrawingContext method(s) with no recordable DrawCommand — " +
            "extend DrawCommandKind + the recorder/replayer, or the whole-frame (render-thread) path will silently drop them: " +
            string.Join(", ", offenders.Distinct()));
    }
}
