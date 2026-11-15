using System.Collections.ObjectModel;
using Jalium.UI.Diagnostics;

namespace Jalium.UI.Controls.DevTools;

internal sealed record DevToolsPerformanceCapture(RenderDiagnostics.GpuResourceSnapshot? Gpu,
    RenderDiagnostics.GpuTimingSnapshot? Timing, RenderDiagnostics.TextCacheFrameStats? Text,
    RenderDiagnostics.PathCacheFrameStats? Path, RenderDiagnostics.RetainedCacheFrameStats? Retained,
    RenderDiagnostics.FramePacingSnapshot? Pacing, RenderDiagnostics.BitmapUploadFrameStats? Bitmap,
    RenderDiagnostics.LayoutPassFrameStats? Layout, RenderDiagnostics.DrawApiStats? Api)
{
    public static DevToolsPerformanceCapture Read() => new(RenderDiagnostics.LatestGpuSnapshot, RenderDiagnostics.LatestGpuTiming,
        RenderDiagnostics.LatestTextCacheStats, RenderDiagnostics.LatestPathCacheStats, RenderDiagnostics.LatestRetainedCacheStats,
        RenderDiagnostics.LatestFramePacing, RenderDiagnostics.LatestBitmapUploadStats, RenderDiagnostics.LatestLayoutPassStats, RenderDiagnostics.LatestDrawApiStats);
}

/// <summary>Converts diagnostic snapshots into stable semantic data. No visual elements are allocated here.</summary>
internal static class DevToolsPerformanceSnapshotBuilder
{
    private sealed record MetricValue(DevToolsPerformanceMetricKind Kind, string Label, string Text = "",
        double Fraction = 0, DevToolsPerformanceTone Tone = DevToolsPerformanceTone.Primary,
        IReadOnlyList<DevToolsPerformanceSegment>? Segments = null);
    private static MetricValue Value(string label, string text, DevToolsPerformanceTone tone = DevToolsPerformanceTone.Primary) => new(DevToolsPerformanceMetricKind.Value, label, text, Tone: tone);
    private static MetricValue Message(string text) => new(DevToolsPerformanceMetricKind.Message, text, Tone: DevToolsPerformanceTone.Muted);
    private static MetricValue MeterBar(string label, double fraction, string text, DevToolsPerformanceTone tone) => new(DevToolsPerformanceMetricKind.Meter, label, text, fraction, tone);
    private static MetricValue StackedBar(IEnumerable<(double Value, DevToolsPerformanceTone Tone)> values) => new(DevToolsPerformanceMetricKind.Bar, "Composition", Segments: values.Select(value => new DevToolsPerformanceSegment(value.Value, value.Tone)).ToArray());
    private static void GpuSection(RendererRows rows, string label) => rows.Add(new(DevToolsPerformanceMetricKind.Heading, label));
    private static void AddHitMeter(RendererRows rows, string label, long hits, long total)
    {
        double fraction = total > 0 ? (double)hits / total : 0;
        var tone = total == 0 ? DevToolsPerformanceTone.Muted : fraction >= .8 ? DevToolsPerformanceTone.Success : fraction >= .4 ? DevToolsPerformanceTone.Warning : DevToolsPerformanceTone.Error;
        rows.Add(MeterBar(label, fraction, total == 0 ? "—" : $"{hits}/{total} · {fraction * 100:F0}%", tone));
    }
    private static DevToolsPerformanceTone BreakdownTone(string name) => name switch
    {
        "Path" => DevToolsPerformanceTone.Accent, "Text" => DevToolsPerformanceTone.Info, "SdfRect" => DevToolsPerformanceTone.Success,
        "Bitmap" => DevToolsPerformanceTone.Enum, "Backdrop" => DevToolsPerformanceTone.Keyword, "LiquidGlass" => DevToolsPerformanceTone.Type, _ => DevToolsPerformanceTone.Muted
    };
    internal static void UpdateRenderer(ObservableCollection<DevToolsPerformanceMetric> target, IReadOnlyList<FrameHistory.Sample> buffer, DevToolsPerformanceCapture capture)
    {
        var p = new RendererRows(target); int count = buffer.Count; var snap = capture.Gpu;
        if (snap == null) { p.Add(Message("Waiting for renderer snapshot")); p.Finish(); return; }
        // 1. Last-frame breakdown — Layout / Render / Present as a stacked bar.
        if (count > 0)
        {
            var last = buffer[count - 1];
            double apiMs = capture.Api?.TotalMs ?? 0;
            // apiMs spans the whole frame's draw-API calls, including EndDraw
            // (which runs in the P segment after MarkRender) and the overlay's
            // own draws — so the managed-overhead gap must be measured against
            // R+P, not R alone. The old `RenderMs - apiMs` read -130 ms under a
            // slow compositor because EndDraw's Present stall lives in P.
            double gap = (last.RenderMs + last.PresentMs) - apiMs;
            GpuSection(p, "LAST FRAME");
            p.Add(StackedBar(new (double Value, DevToolsPerformanceTone Tone)[]
            {
                (last.LayoutMs,  DevToolsPerformanceTone.Info),
                (last.RenderMs,  DevToolsPerformanceTone.Accent),
                (last.PresentMs, DevToolsPerformanceTone.Success),
            }));
            p.Add(Value("L / R / P", $"{last.LayoutMs:F1} / {last.RenderMs:F1} / {last.PresentMs:F1} ms"));
            p.Add(Value("Total frame", $"{last.TotalMs:F1} ms"));
            p.Add(Value("API / gap", $"{apiMs:F1} / {gap:F1} ms"));
        }

        // 2. Software raster breakdown. GPU backends leave the appended ABI
        // fields at zero, so this section appears only for CPU Raster.
        if (snap.SoftwareRasterNs > 0)
        {
            GpuSection(p, "SOFTWARE RASTER");
            p.Add(Value(
                "Raster wall", $"{snap.SoftwareRasterNs / 1_000_000.0:F2} ms"));
            p.Add(Value(
                "Visited / blended",
                $"{snap.SoftwarePixelsVisited:N0} / {snap.SoftwarePixelsBlended:N0} px"));
            double clipCull = snap.SoftwarePixelsVisited > 0
                ? (double)snap.SoftwareClipRejectedPixels / snap.SoftwarePixelsVisited
                : 0;
            p.Add(Value(
                "Clip rejected",
                $"{snap.SoftwareClipRejectedPixels:N0} · {clipCull * 100:F1}%"));
            p.Add(Value(
                "AA edge samples", $"{snap.SoftwareAaSamples:N0}"));
            p.Add(Value(
                "Workers",
                $"{snap.SoftwareWorkerCount} · {snap.SoftwareWorkerUtilizationPermille / 10.0:F1}% parallel"));
            p.Add(Value(
                "Parallel wall", $"{snap.SoftwareParallelNs / 1_000_000.0:F2} ms"));
            long effectLookups = snap.SoftwareEffectCacheHits +
                snap.SoftwareEffectCacheMisses;
            string effectRate = effectLookups > 0
                ? $"{snap.SoftwareEffectCacheHits}/{effectLookups} · " +
                  $"{(double)snap.SoftwareEffectCacheHits / effectLookups * 100:F0}%"
                : "—";
            p.Add(Value(
                "Effect cache", $"{snap.SoftwareEffectCacheEntries} · {effectRate}"));
            p.Add(Value(
                "Gradient cache", snap.SoftwareGradientCacheEntries.ToString()));
            p.Add(Value(
                "Tracked memory",
                $"{snap.SoftwareCacheBytes / (1024.0 * 1024.0):F2} MB"));
        }

        // 3. GPU breakdown — the hero: hardware-timestamped split by category.
        var timing = capture.Timing;
        if (timing != null && timing.Valid)
        {
            GpuSection(p, "GPU BREAKDOWN");
            p.Add(Value("Total GPU", $"{timing.TotalGpuMs:F1} ms · {timing.BatchCount} batches"));
            p.Add(Value("Vello dispatches", snap.VelloDispatchCount.ToString()));
            var cats = new (string Name, long Ns)[]
            {
                ("Path",        timing.PathNs),
                ("Text",        timing.TextNs),
                ("SdfRect",     timing.SdfRectNs),
                ("Bitmap",      timing.BitmapNs),
                ("Backdrop",    timing.BackdropNs),
                ("LiquidGlass", timing.LiquidGlassNs),
                ("Other",       timing.OtherNs),
            };
            Array.Sort(cats, (a, b) => b.Ns.CompareTo(a.Ns));
            double totMs = timing.TotalGpuMs > 0 ? timing.TotalGpuMs : 1.0;

            var segs = new List<(double Value, DevToolsPerformanceTone Tone)>();
            foreach (var c in cats)
                if (c.Ns > 0) segs.Add((c.Ns / 1_000_000.0, BreakdownTone(c.Name)));
            p.Add(StackedBar(segs));

            foreach (var c in cats)
            {
                if (c.Ns <= 0) continue;
                double ms = c.Ns / 1_000_000.0;
                double frac = ms / totMs;
                p.Add(MeterBar(c.Name, frac, $"{ms:F1} ms · {frac * 100:F0}%", BreakdownTone(c.Name)));
            }
        }
        else if (timing != null)
        {
            GpuSection(p, "GPU BREAKDOWN");
            p.Add(Message("Waiting for GPU timestamps"));
        }

        // 3. Text caches — hit-rate meters (instant red/amber/green health).
        var tc = capture.Text;
        if (tc != null && (tc.DrawTextCalls > 0 || tc.LayoutHits + tc.LayoutMisses > 0))
        {
            GpuSection(p, "TEXT CACHES");
            AddHitMeter(p, "Layout", tc.LayoutHits, tc.LayoutHits + tc.LayoutMisses);
            AddHitMeter(p, "Instance", tc.InstanceHits, tc.InstanceHits + tc.InstanceMisses);
            AddHitMeter(p, "Glyph", tc.GlyphRasterHits, tc.GlyphRasterHits + tc.GlyphRasterMisses);
            p.Add(Value("DrawText / glyphs", $"{tc.DrawTextCalls} / {tc.EmittedGlyphs}"));
            if (tc.AtlasResets > 0)
                p.Add(Value("Atlas resets", tc.AtlasResets.ToString(), DevToolsPerformanceTone.Warning));
        }

        // 4. Path raster cache — hit-rate meters + CPU work.
        var pc = capture.Path;
        if (pc != null)
        {
            long st = pc.StrokeHits + pc.StrokeMisses;
            long ft = pc.FillHits + pc.FillMisses;
            long gt = pc.GeometryHits + pc.GeometryMisses;
            if (st + ft + gt > 0)
            {
                GpuSection(p, "PATH RASTER CACHE");
                if (st > 0) AddHitMeter(p, "Stroke", pc.StrokeHits, st);
                if (ft > 0) AddHitMeter(p, "Fill", pc.FillHits, ft);
                if (gt > 0) AddHitMeter(p, "Geometry", pc.GeometryHits, gt);
                if (pc.FlattenNs > 0 || pc.TriangulateNs > 0)
                {
                    p.Add(Value("Flatten", $"{pc.FlattenNs / 1_000_000.0:F2} ms"));
                    p.Add(Value("Triangulate", $"{pc.TriangulateNs / 1_000_000.0:F2} ms"));
                }
            }
        }

        // 5. Retained-mode cache — replay% is the win.
        var rc = capture.Retained;
        if (rc != null && rc.Records + rc.Replays + rc.Bypasses > 0)
        {
            GpuSection(p, "RETAINED-MODE CACHE");
            AddHitMeter(p, "Replays", rc.Replays, rc.Records + rc.Replays + rc.Bypasses);
            p.Add(Value("Records / Bypass", $"{rc.Records} / {rc.Bypasses}"));
        }

        // 6. Memory — atlas fill meter + cache/texture sizes.
        GpuSection(p, "MEMORY");
        double atlasFrac = snap.GlyphAtlasSlotsTotal > 0 ? (double)snap.GlyphAtlasSlotsUsed / snap.GlyphAtlasSlotsTotal : 0;
        p.Add(MeterBar("Glyph atlas", atlasFrac,
            $"{snap.GlyphAtlasSlotsUsed}/{snap.GlyphAtlasSlotsTotal} · {snap.GlyphAtlasBytes / (1024.0 * 1024.0):F1} MB",
            DevToolsPerformanceTone.Info));
        p.Add(Value("Path cache", $"{snap.PathCacheEntries} · {snap.PathCacheBytes / (1024.0 * 1024.0):F2} MB"));
        p.Add(Value("Textures", $"{snap.TextureCount} · {snap.TextureBytes / (1024.0 * 1024.0):F2} MB"));

        // 7. Frame pacing — scalar readouts.
        var pacing = capture.Pacing;
        if (pacing != null)
        {
            GpuSection(p, "FRAME PACING");
            p.Add(Value("Begin attempts",
                pacing.BeginFailures == 0 ? $"{pacing.BeginAttempts} clean" : $"{pacing.BeginAttempts} +{pacing.BeginFailures} retries",
                pacing.BeginFailures == 0 ? DevToolsPerformanceTone.Primary : DevToolsPerformanceTone.Warning));
            if (pacing.SwapBufferCount > 0)
            {
                p.Add(Value("Swap buffers", pacing.SwapBufferCount.ToString()));
                p.Add(Value("Waitable wait", $"{pacing.FrameWaitableWaitMs:F1} ms"));
                p.Add(Value("Fence wait", $"{pacing.FrameGpuWaitMs:F1} ms"));
            }
            if (pacing.LastFramePresentToReadyNs > 0)
                p.Add(Value("Present to ready", $"{pacing.LastFramePresentToReadyMs:F1} ms"));
        }

        // 8. Bitmap upload.
        var bmp = capture.Bitmap;
        if (bmp != null && (bmp.UploadCount > 0 || bmp.FastPathHits > 0))
        {
            GpuSection(p, "BITMAP UPLOAD");
            p.Add(Value("Uploads", $"{bmp.UploadCount} · {bmp.UploadBytes / (1024.0 * 1024.0):F2} MB"));
            AddHitMeter(p, "FastPath", bmp.FastPathHits, bmp.UploadCount + bmp.FastPathHits);
        }

        // 9. Layout pass.
        var lay = capture.Layout;
        if (lay != null && (lay.MeasureCount > 0 || lay.ArrangeCount > 0))
        {
            GpuSection(p, "LAYOUT");
            p.Add(Value("Measure", $"{lay.MeasureCount} · {lay.MeasureMs:F1} ms"));
            p.Add(Value("Arrange", $"{lay.ArrangeCount} · {lay.ArrangeMs:F1} ms"));
            if (lay.Iterations > 1)
                p.Add(Value("Iterations", $"{lay.Iterations} (thrash)", DevToolsPerformanceTone.Warning));
        }

        p.Finish();
    }
    internal static void UpdateApi(ObservableCollection<DevToolsDrawApiItem> target, RenderDiagnostics.DrawApiStats? snapshot)
    {
        var entries = snapshot?.Entries.OrderByDescending(entry => entry.TotalMs).ToArray() ?? [];
        var previous = target.ToDictionary(item => item.Name, StringComparer.Ordinal);
        double maximum = entries.Select(entry => entry.TotalMs).DefaultIfEmpty(0).Max(); if (!(maximum > 0)) maximum = 1;
        var desired = new List<DevToolsDrawApiItem>(entries.Length);
        foreach (var entry in entries)
        {
            if (!previous.TryGetValue(entry.Name, out var item)) item = new DevToolsDrawApiItem { Name = entry.Name };
            item.CallsText = entry.Count.ToString(); item.TotalText = entry.TotalMs.ToString("F2"); item.AverageText = entry.AvgUs.ToString("F1");
            item.Fraction = entry.TotalMs / maximum; item.Tone = item.Fraction >= .5 ? DevToolsPerformanceTone.Accent : item.Fraction >= .2 ? DevToolsPerformanceTone.Warning : DevToolsPerformanceTone.Primary;
            desired.Add(item);
        }
        Reconcile(target, desired);
    }
    private sealed class RendererRows(ObservableCollection<DevToolsPerformanceMetric> target)
    {
        private readonly Dictionary<string, DevToolsPerformanceMetric> _previous = target.ToDictionary(item => item.Key, StringComparer.Ordinal);
        private readonly List<DevToolsPerformanceMetric> _desired = [];
        private string _section = "Renderer";
        public void Add(MetricValue value)
        {
            if (value.Kind == DevToolsPerformanceMetricKind.Heading) _section = value.Label;
            string key = _section + "." + value.Kind + "." + value.Label;
            if (!_previous.TryGetValue(key, out var item)) item = new DevToolsPerformanceMetric { Key = key };
            item.Kind = value.Kind; item.Label = value.Kind == DevToolsPerformanceMetricKind.Bar ? _section + " composition" : value.Label; item.ValueText = value.Text; item.Fraction = value.Fraction; item.Tone = value.Tone;
            if (item.Segments == null || value.Segments == null || !item.Segments.SequenceEqual(value.Segments)) item.Segments = value.Segments;
            _desired.Add(item);
        }
        public void Finish() => Reconcile(target, _desired);
    }
    private static void Reconcile<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        for (int index = 0; index < desired.Count; index++)
        {
            if (index < target.Count && ReferenceEquals(target[index], desired[index])) continue;
            int previous = target.IndexOf(desired[index]); if (previous >= 0) target.Move(previous, index); else target.Insert(index, desired[index]);
        }
        while (target.Count > desired.Count) target.RemoveAt(target.Count - 1);
    }
}
