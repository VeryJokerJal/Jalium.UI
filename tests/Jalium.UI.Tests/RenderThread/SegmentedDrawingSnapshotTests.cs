using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests.RenderThread;

public sealed class SegmentedDrawingSnapshotTests
{
    [Theory]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(1025)]
    [InlineData(4096)]
    public void LargeSnapshotPreservesDrawOrderAndStateAcrossSegments(int rectangles)
    {
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        var direct = new RecordingRenderSink();
        Emit(recorder, rectangles);
        Emit(direct, rectangles);

        var drawing = recorder.Commit();
        var replay = new RecordingRenderSink();
        DrawingReplayer.Replay(drawing, replay);

        Assert.Equal(direct.Events, replay.Events);
        Assert.Equal(drawing.Count, drawing.Commands.Count());

        recorder.BindWholeFrame();
        Emit(recorder, 3);
        recorder.Commit();
        var replayAfterReuse = new RecordingRenderSink();
        DrawingReplayer.Replay(drawing, replayAfterReuse);
        Assert.Equal(direct.Events, replayAfterReuse.Events);
    }

    private static void Emit(DrawingContext context, int rectangles)
    {
        var brush = new SolidColorBrush(Colors.Red);
        for (int i = 0; i < rectangles; i++)
        {
            if (i % 100 == 0)
            {
                ((IOffsetDrawingContext)context).Offset = new Point(i, -i);
                context.PushOpacity(0.5);
                context.PushClip(new RectangleGeometry(new Rect(0, 0, 20000, 20000)));
            }
            context.DrawRectangle(brush, null, new Rect(i, i / 2.0, 10, 20));
            if (i % 100 == 99 || i == rectangles - 1)
            {
                context.Pop();
                context.Pop();
            }
        }
    }
}
