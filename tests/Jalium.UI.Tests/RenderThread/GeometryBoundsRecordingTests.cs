using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests.RenderThread;

public sealed class GeometryBoundsRecordingTests
{
    [Fact]
    public void WholeFrameSolidStrokeDoesNotQueryUnusedGeometryBounds()
    {
        var counter = new BoundsCounter();
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        recorder.DrawGeometry(null, new Pen(new SolidColorBrush(Colors.Red), 2), new CountingGeometry(counter));
        var drawing = recorder.Commit();

        Assert.Equal(0, counter.Reads);
        Assert.Null(drawing.Bounds);
        Assert.Equal(1, drawing.Count);
    }

    [Fact]
    public void PerVisualRecordingKeepsItsGeometryBounds()
    {
        var counter = new BoundsCounter();
        var recorder = new DrawingRecorder();
        recorder.Bind(new RecordingRenderSink());
        recorder.DrawGeometry(new SolidColorBrush(Colors.Red), null, new CountingGeometry(counter));
        var drawing = recorder.Commit();

        Assert.True(counter.Reads > 0);
        Assert.Equal(new Rect(0, 0, 12, 8), drawing.Bounds);
    }

    private sealed class BoundsCounter { internal int Reads; }

    private sealed class CountingGeometry(BoundsCounter counter) : Geometry
    {
        public override Rect Bounds
        {
            get { counter.Reads++; return new Rect(0, 0, 12, 8); }
        }
        protected override Freezable CreateInstanceCore() => new CountingGeometry(counter);
    }
}
