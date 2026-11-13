using Jalium.UI.Rendering;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class RenderPipelineTraceTests
{
    [Fact]
    public void DisabledTraceDoesNotCreateACapture()
    {
        string? previous = Environment.GetEnvironmentVariable("JALIUM_RENDER_PIPELINE_TRACE");
        try
        {
            Environment.SetEnvironmentVariable("JALIUM_RENDER_PIPELINE_TRACE", null);
            Assert.Null(RenderPipelineTrace.TryCreate());
            Environment.SetEnvironmentVariable("JALIUM_RENDER_PIPELINE_TRACE", "  ");
            Assert.Null(RenderPipelineTrace.TryCreate());
        }
        finally { Environment.SetEnvironmentVariable("JALIUM_RENDER_PIPELINE_TRACE", previous); }
    }

    [Fact]
    public void BoundedCaptureExportsOnlyNewestEventsAndReportsLoss()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            var trace = new RenderPipelineTrace(path, capacity: 4);
            for (int i = 1; i <= 7; i++) trace.Mark(RenderPipelineStage.RecordStart, i, timestampTicks: i * 10);
            Assert.False(File.Exists(path)); // Event collection never writes to disk.
            trace.Export();
            var lines = File.ReadAllLines(path);
            Assert.Contains("total=7,dropped=3", lines[0]);
            Assert.Equal("timestamp,ticket,stage,detail", lines[1]);
            Assert.Equal(["40,4,RecordStart,0", "50,5,RecordStart,0", "60,6,RecordStart,0", "70,7,RecordStart,0"], lines.Skip(2));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ConcurrentCaptureRetainsBothThreadStreams()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            var trace = new RenderPipelineTrace(path, capacity: 4096);
            Parallel.For(1, 2001, i => trace.Mark(i % 2 == 0 ? RenderPipelineStage.Published : RenderPipelineStage.WorkerClaim, i));
            trace.Export();
            var lines = File.ReadAllLines(path);
            Assert.Contains("total=2000,dropped=0", lines[0]);
            var tickets = lines.Skip(2).Select(x => long.Parse(x.Split(',')[1])).Order().ToArray();
            Assert.Equal(Enumerable.Range(1, 2000).Select(x => (long)x), tickets);
        }
        finally { File.Delete(path); }
    }
}
