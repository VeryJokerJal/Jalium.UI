using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Rendering;

namespace Jalium.UI.Tests.RenderThread;

[Collection("Application")]
public sealed class PooledDrawingLifetimeTests
{
    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(1025)]
    [InlineData(4097)]
    public void LiveSnapshotsRemainStableAcrossBufferChurnAndHideUnusedCapacity(int count)
    {
        var recorder = new DrawingRecorder();
        var snapshot = Record(recorder, count);
        Assert.Equal(count, snapshot.Commands.Length);
        Assert.Throws<IndexOutOfRangeException>(() => { _ = snapshot.Commands[count]; });
        snapshot.Retain();
        snapshot.Release();
        for (int i = 0; i < 8; i++) Record(recorder, count, 1000).Release();
        var sink = new RecordingRenderSink();
        new MediaRenderCacheHost().Replay(snapshot, sink);
        Assert.Equal(count, sink.Events.Count(e => e.StartsWith("DrawRectangle")));
        for (int i = 0; i < count; i++)
            Assert.EndsWith($"({i},0,1,1)", sink.Events[i]);
        snapshot.Release();
        Assert.True(snapshot.IsReleased);
        Assert.Throws<ObjectDisposedException>(snapshot.Retain);
    }

    [Fact]
    public async Task ParentOwnsEveryNestedReferenceUntilItsFinalCrossThreadRelease()
    {
        var recorder = new DrawingRecorder();
        var child = Record(recorder, 64);
        recorder.BindWholeFrame();
        Assert.True(recorder.TryAppendRecordedDrawing(child));
        Assert.True(recorder.TryAppendRecordedDrawing(child));
        child.Release();
        var parent = recorder.Commit();
        Assert.False(child.IsReleased);
        var sink = new RecordingRenderSink();
        new MediaRenderCacheHost().Replay(parent, sink);
        Assert.Equal(128, sink.Events.Count);
        await Task.Run(parent.Release);
        Assert.True(parent.IsReleased);
        Assert.True(child.IsReleased);
    }

    [Fact]
    public void AbandonedRecorderReleasesItsNestedReferences()
    {
        var recorder = new DrawingRecorder();
        var child = Record(recorder, 64);
        recorder.BindWholeFrame();
        recorder.TryAppendRecordedDrawing(child);
        child.Release();
        recorder.Reset();
        Assert.True(child.IsReleased);
        recorder.BindWholeFrame();
        Assert.Same(RecordedDrawing.Empty, recorder.Commit());
    }

    [Fact]
    public void ReplaySurvivesReentrantOwnerReleaseAndNewRecordings()
    {
        var recorder = new DrawingRecorder();
        var drawing = Record(recorder, 64);
        var sink = new ReentrantSink(() =>
        {
            drawing.Release();
            for (int i = 0; i < 8; i++) Record(recorder, 64, 1000).Release();
        });
        DrawingReplayer.Replay(drawing, sink);
        Assert.Equal(64, sink.Count);
        Assert.Equal(63, sink.LastX);
        Assert.True(drawing.IsReleased);
    }

    [Fact]
    public void ReplacingVisualCacheDoesNotInvalidateAnOlderCapturedFrame()
    {
        MediaRenderCacheHost.Bootstrap();
        var visual = new TileVisual { Width = 128, Height = 64 };
        visual.Measure(new Size(128, 64));
        visual.Arrange(new Rect(0, 0, 128, 64));
        var host = new MediaRenderCacheHost();
        RecordedDrawing Capture()
        {
            var recorder = host.CreateFrameRecorder()!;
            visual.Render(recorder);
            return (RecordedDrawing)host.FinishRecord(recorder);
        }
        var first = Capture();
        var oldCache = (RecordedDrawing)Assert.Single(first.Commands,
            c => c.Kind == DrawCommandKind.DrawRecordedDrawing).A!;
        visual.Blue = true;
        visual.InvalidateVisual();
        var second = Capture();
        Assert.False(oldCache.IsReleased);
        var sink = new RecordingRenderSink();
        host.Replay(first, sink);
        Assert.Equal(64, sink.Events.Count(e => e.StartsWith("DrawRectangle") && e.Contains("#FFFF0000")));
        first.Release();
        Assert.True(oldCache.IsReleased);
        second.Release();
        Visual.ReleaseRetainedLayersRecursive(visual);
    }

    [Fact]
    public void WarmLargeSnapshotsAvoidAllocatingCommandArraysPerCapture()
    {
        var recorder = new DrawingRecorder();
        for (int i = 0; i < 64; i++) Record(recorder, 1025).Release();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) Record(recorder, 1025).Release();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 128 * 2048, $"Allocated {allocated / 128} bytes per capture.");
    }

    [Fact]
    public void ThrowingOnRenderFinishesAndReleasesItsAbandonedRecording()
    {
        var previous = Visual.RenderCacheHost;
        var host = new TrackingRenderCacheHost();
        var visual = new TileVisual { Width = 128, Height = 64, AfterDraw = () => throw new InvalidOperationException("Render failure") };
        visual.Measure(new Size(128, 64));
        visual.Arrange(new Rect(0, 0, 128, 64));
        var recorder = host.CreateFrameRecorder();
        Visual.RenderCacheHost = host;
        try
        {
            Assert.Throws<InvalidOperationException>(() => visual.Render(recorder));
            Assert.NotNull(host.LastVisualDrawing);
            Assert.True(host.LastVisualDrawing!.IsReleased);
        }
        finally
        {
            DrawingLifetime.Release(host.FinishRecord(recorder));
            Visual.RenderCacheHost = previous;
            Visual.ReleaseRetainedLayersRecursive(visual);
        }
    }

    private static readonly SolidColorBrush Red = new(Colors.Red);
    private static RecordedDrawing Record(DrawingRecorder recorder, int count, int offset = 0)
    {
        recorder.BindWholeFrame();
        for (int i = 0; i < count; i++)
            recorder.DrawRectangle(Red, null, new Rect(offset + i, 0, 1, 1));
        return recorder.Commit();
    }

    private sealed class ReentrantSink(Action firstDraw) : RecordingRenderSink
    {
        private Action? _first = firstDraw;
        public int Count;
        public double LastX;
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
            var callback = _first;
            _first = null;
            callback?.Invoke();
            Count++;
            LastX = rectangle.X;
        }
    }

    private sealed class TileVisual : FrameworkElement
    {
        internal bool Blue;
        internal Action? AfterDraw;
        protected override void OnRender(DrawingContext context)
        {
            var brush = Blue ? new SolidColorBrush(Colors.Blue) : Red;
            for (int i = 0; i < 64; i++) context.DrawRectangle(brush, null, new Rect(i, 0, 1, 1));
            AfterDraw?.Invoke();
        }
    }
}

internal sealed class TrackingRenderCacheHost : IRenderCacheHost
{
    private readonly MediaRenderCacheHost _inner = new();
    private DrawingContext? _frame;
    internal RecordedDrawing? LastFrameDrawing;
    internal RecordedDrawing? LastVisualDrawing;
    internal Action<DrawingContext>? ReplayFrame;
    public DrawingContext CreateRecorder(DrawingContext target) => _inner.CreateRecorder(target);
    public DrawingContext CreateFrameRecorder() => CreateFrameRecorder(false);
    public DrawingContext CreateFrameRecorder(bool simplifyElementEffects) => _frame = _inner.CreateFrameRecorder(simplifyElementEffects);
    public object FinishRecord(DrawingContext recorder)
    {
        var drawing = (RecordedDrawing)_inner.FinishRecord(recorder);
        if (ReferenceEquals(_frame, recorder)) { LastFrameDrawing = drawing; _frame = null; }
        else LastVisualDrawing = drawing;
        return drawing;
    }
    public void Replay(object drawing, DrawingContext target)
    {
        if (ReferenceEquals(drawing, LastFrameDrawing) && ReplayFrame is { } replay)
            replay(target);
        else _inner.Replay(drawing, target);
    }
}
