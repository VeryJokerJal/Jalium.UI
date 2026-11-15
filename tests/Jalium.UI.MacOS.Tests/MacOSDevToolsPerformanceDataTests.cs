using System.Collections.ObjectModel;
using System.Diagnostics;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Diagnostics;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsPerformanceDataTests
{
    [Fact]
    public void SnapshotBuilderPreservesAllMetricGroupsAndReusesTheirModels()
    {
        var snapshot = new RenderDiagnostics.GpuResourceSnapshot(10, 100, 0, 0, 0, 0, 0, softwareRasterNs: 1_000_000);
        var capture = new DevToolsPerformanceCapture(snapshot,
            new RenderDiagnostics.GpuTimingSnapshot { Valid = true, TotalGpuNs = 500_000, TextNs = 500_000, BatchCount = 2 },
            new RenderDiagnostics.TextCacheFrameStats { DrawTextCalls = 1, LayoutHits = 3, LayoutMisses = 1 },
            new RenderDiagnostics.PathCacheFrameStats { GeometryHits = 3, GeometryMisses = 1 },
            new RenderDiagnostics.RetainedCacheFrameStats { Records = 1, Replays = 9 },
            new RenderDiagnostics.FramePacingSnapshot { BeginAttempts = 1 },
            new RenderDiagnostics.BitmapUploadFrameStats { UploadCount = 1, FastPathHits = 9 },
            new RenderDiagnostics.LayoutPassFrameStats { MeasureCount = 1, ArrangeCount = 1 }, null);
        var rows = new ObservableCollection<DevToolsPerformanceMetric>(); var frames = new[] { new FrameHistory.Sample(1, 2, 3, 6, 0) };
        DevToolsPerformanceSnapshotBuilder.UpdateRenderer(rows, frames, capture);
        var headings = rows.Where(row => row.Kind == DevToolsPerformanceMetricKind.Heading).Select(row => row.Label).ToArray();
        Assert.Equal(new[] { "LAST FRAME", "SOFTWARE RASTER", "GPU BREAKDOWN", "TEXT CACHES", "PATH RASTER CACHE", "RETAINED-MODE CACHE", "MEMORY", "FRAME PACING", "BITMAP UPLOAD", "LAYOUT" }, headings);
        var atlas = Assert.Single(rows, row => row.Label == "Glyph atlas"); Assert.Equal(.1, atlas.Fraction);
        var original = rows.ToArray(); DevToolsPerformanceSnapshotBuilder.UpdateRenderer(rows, frames, capture);
        Assert.Equal(original.Length, rows.Count); for (int i = 0; i < rows.Count; i++) Assert.Same(original[i], rows[i]);
        DevToolsPerformanceSnapshotBuilder.UpdateRenderer(rows, frames, capture with { Gpu = null });
        Assert.Single(rows); Assert.Equal(DevToolsPerformanceMetricKind.Message, rows[0].Kind);
    }

    [Fact]
    public void DrawApiRowsKeepIdentityWhileSortingAndRefreshTheirValues()
    {
        var rows = new ObservableCollection<DevToolsDrawApiItem>();
        RenderDiagnostics.DrawApiEntry Entry(string name, int count, double milliseconds) => new() { Name = name, Count = count, TotalTicks = (long)(milliseconds * Stopwatch.Frequency / 1000) };
        DevToolsPerformanceSnapshotBuilder.UpdateApi(rows, new() { Entries = new[] { Entry("DrawText", 2, 2), Entry("FillPath", 3, 3) } });
        Assert.Equal(new[] { "FillPath", "DrawText" }, rows.Select(row => row.Name));
        var draw = rows[1]; var fill = rows[0];
        DevToolsPerformanceSnapshotBuilder.UpdateApi(rows, new() { Entries = new[] { Entry("DrawText", 4, 4), Entry("FillPath", 1, 1) } });
        Assert.Same(draw, rows[0]); Assert.Same(fill, rows[1]); Assert.Equal("4", draw.CallsText); Assert.Equal("4.00", draw.TotalText); Assert.Equal(1, draw.Fraction);
        Assert.Equal(DevToolsPerformanceTone.Accent, draw.Tone); Assert.Equal(DevToolsPerformanceTone.Warning, fill.Tone);
        DevToolsPerformanceSnapshotBuilder.UpdateApi(rows, null); Assert.Empty(rows);
    }
}
