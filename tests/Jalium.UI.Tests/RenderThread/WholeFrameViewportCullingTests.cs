using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests.RenderThread;

[Collection("Application")]
public sealed class WholeFrameViewportCullingTests
{
    [Fact]
    public void ScrollCapture_SkipsOffscreenRows_AndRecordsNewlyVisibleDirtyRows()
    {
        var rows = Enumerable.Range(0, 205).Select(_ => new CountingElement { Height = 24 }).ToArray();
        var content = new StackPanel();
        foreach (var row in rows) content.Children.Add(row);
        var scroll = new ScrollViewer
        {
            Content = content,
            Width = 240, Height = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var host = new MediaRenderCacheHost();
        Layout(scroll);

        var first = Capture(host, scroll);
        Assert.InRange(rows.Sum(row => row.RenderCount), 1, 12);
        Assert.Equal(0, rows[100].RenderCount);

        // An unseen row must retain its pending render work when ancestors clear
        // their aggregate dirty flags at the end of a frame.
        rows[100].InvalidateVisual();
        scroll.ScrollToVerticalOffset(100 * 24);
        Layout(scroll);
        var second = Capture(host, scroll);
        Assert.Equal(1, rows[100].RenderCount);
        Assert.Equal(0, rows[200].RenderCount);
        Assert.InRange(first.Count, 1, 150);
        Assert.InRange(second.Count, 1, 150);
    }

    [Fact]
    public void NestedClips_UseAbsoluteOffsets_AndPopRestoresSiblingViewport()
    {
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder();
        try
        {
            var offset = (IOffsetDrawingContext)recorder;
            var clip = (IClipBoundsDrawingContext)recorder;
            offset.Offset = new Point(20, 30);
            recorder.PushClip(new RectangleGeometry(new Rect(0, 0, 200, 100)));
            Assert.Equal(new Rect(20, 30, 200, 100), clip.CurrentClipBounds);
            recorder.PushOpacity(.5);
            offset.Offset = new Point(40, 50);
            recorder.PushClip(new RectangleGeometry(new Rect(0, 0, 300, 200)));
            Assert.Equal(new Rect(40, 50, 180, 80), clip.CurrentClipBounds);
            recorder.Pop();
            Assert.Equal(new Rect(20, 30, 200, 100), clip.CurrentClipBounds);
            recorder.Pop();
            Assert.Equal(new Rect(20, 30, 200, 100), clip.CurrentClipBounds);
            recorder.Pop();
            Assert.Null(clip.CurrentClipBounds);
        }
        finally { host.FinishRecord(recorder); }
    }

    [Fact]
    public void DisjointNestedClips_CullTheEntireChildSubtree()
    {
        var root = new Canvas { Width = 100, Height = 100, ClipToBounds = true };
        var container = new Canvas { Width = 100, Height = 100, Clip = new RectangleGeometry(new Rect(200, 0, 20, 20)) };
        var child = new CountingElement { Width = 40, Height = 40 };
        container.Children.Add(child);
        root.Children.Add(container);
        root.Measure(new Size(100, 100));
        root.Arrange(new Rect(0, 0, 100, 100));
        Capture(new MediaRenderCacheHost(), root);
        Assert.Equal(0, child.RenderCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransformScopes_DeferCulling_AndRestoreTheUntransformedClip(bool translate)
    {
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder();
        try
        {
            var clip = (IClipBoundsDrawingContext)recorder;
            var transform = (ITransformDrawingContext)recorder;
            var viewport = new Rect(10, 20, 100, 80);
            recorder.PushClip(new RectangleGeometry(viewport));
            transform.PushTransform(translate ? new TranslateTransform(500, 0) : new ScaleTransform(2, 2), 0, 0);
            Assert.Null(clip.CurrentClipBounds);
            recorder.PushClip(new RectangleGeometry(new Rect(600, 0, 20, 20)));
            recorder.PushOpacity(.5);
            Assert.Null(clip.CurrentClipBounds);
            recorder.Pop();
            recorder.Pop();
            transform.PopTransform();
            Assert.Equal(viewport, clip.CurrentClipBounds);
            recorder.Pop();
        }
        finally { host.FinishRecord(recorder); }
    }

    [Fact]
    public void EffectScopes_KeepCompleteCaptureInput_AndRestoreTheViewport()
    {
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder();
        try
        {
            var clip = (IClipBoundsDrawingContext)recorder;
            var effects = (IEffectDrawingContext)recorder;
            var viewport = new Rect(0, 0, 100, 50);
            recorder.PushClip(new RectangleGeometry(viewport));
            effects.BeginEffectCapture(0, 0, 200, 200);
            recorder.PushEffect(new BlurEffect { Radius = 5 }, new Rect(0, 0, 200, 200));
            recorder.PushClip(new RectangleGeometry(new Rect(0, 0, 10, 10)));
            Assert.Null(clip.CurrentClipBounds);
            recorder.Pop();
            recorder.PopEffect();
            Assert.Null(clip.CurrentClipBounds);
            effects.EndEffectCapture();
            Assert.Equal(viewport, clip.CurrentClipBounds);
            recorder.Pop();
        }
        finally { host.FinishRecord(recorder); }
    }

    [Fact]
    public void PartialEdgeClip_DoesNotCullAlongAnUnclippedAxis()
    {
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder();
        try
        {
            var viewport = new Rect(0, 0, 240, 240);
            recorder.PushClip(new RectangleGeometry(viewport));
            recorder.PushClip(new RectangleGeometry(new Rect(20, 30, 100, 20))
            {
                BoundsClipEdges = ClipEdges.Top | ClipEdges.Bottom,
                BoundsClipRect = new Rect(20, 30, 100, 20)
            });
            var clip = ((IClipBoundsDrawingContext)recorder).CurrentClipBounds;
            Assert.True(clip is null || clip.Value.Contains(new Point(200, 40)));
            recorder.Pop();
            Assert.Equal(viewport, ((IClipBoundsDrawingContext)recorder).CurrentClipBounds);
            recorder.Pop();
        }
        finally { host.FinishRecord(recorder); }
    }

    [Fact]
    public void PerVisualCache_RecordsAllTextLines_AndReplaysThemAfterScrolling()
    {
        var host = new MediaRenderCacheHost();
        var frame = host.CreateFrameRecorder();
        var text = new TextBlock { Text = "Line 1\nLine 2\nLine 3\nLine 4", Width = 200, VerticalAlignment = VerticalAlignment.Top };
        text.Measure(new Size(200, double.PositiveInfinity));
        text.Arrange(new Rect(0, 0, 200, text.DesiredSize.Height));
        RecordedDrawing cached;
        try
        {
            frame.PushClip(new RectangleGeometry(new Rect(0, 0, 200, 16)));
            var perVisual = host.CreateRecorder(frame);
            try
            {
                Assert.Null(((IClipBoundsDrawingContext)perVisual).CurrentClipBounds);
                text.Render(perVisual);
            }
            finally { cached = (RecordedDrawing)host.FinishRecord(perVisual); }
            frame.Pop();
        }
        finally { host.FinishRecord(frame); }

        var replay = new RecordingRenderSink();
        host.Replay(cached, replay);
        Assert.Equal(4, replay.Events.Count(entry => entry.StartsWith("DrawText")));
    }

    [Fact]
    public void PooledRecorder_DoesNotLeakUnbalancedClipTransformOrEffectScopes()
    {
        var host = new MediaRenderCacheHost();
        var frame = host.CreateFrameRecorder();
        frame.PushClip(new RectangleGeometry(new Rect(0, 0, 20, 20)));
        frame.PushTransform(new ScaleTransform(2, 2));
        ((IEffectDrawingContext)frame).BeginEffectCapture(0, 0, 40, 40);
        host.FinishRecord(frame);

        var next = host.CreateFrameRecorder();
        try
        {
            Assert.Same(frame, next);
            Assert.Null(((IClipBoundsDrawingContext)next).CurrentClipBounds);
            var viewport = new Rect(0, 0, 240, 240);
            next.PushClip(new RectangleGeometry(viewport));
            Assert.Equal(viewport, ((IClipBoundsDrawingContext)next).CurrentClipBounds);
            next.Pop();
        }
        finally { host.FinishRecord(next); }
    }

    private static void Layout(UIElement element)
    {
        for (int i = 0; i < 3; i++)
        {
            element.Measure(new Size(240, 240));
            element.Arrange(new Rect(0, 0, 240, 240));
        }
    }

    private static RecordedDrawing Capture(MediaRenderCacheHost host, Visual visual)
    {
        var recorder = host.CreateFrameRecorder();
        RecordedDrawing drawing;
        try { visual.Render(recorder); }
        finally { drawing = (RecordedDrawing)host.FinishRecord(recorder); }
        return drawing;
    }

    private sealed class CountingElement : FrameworkElement
    {
        public int RenderCount { get; private set; }
        protected override void OnRender(DrawingContext dc)
        {
            RenderCount++;
            dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        }
    }
}
