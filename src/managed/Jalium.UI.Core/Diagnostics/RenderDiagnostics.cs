using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Diagnostics;

/// <summary>
/// Per-frame render diagnostics: Overdraw map, dirty region history, GPU resource stats.
/// Populated by the rendering pipeline; consumed by DevTools overlays.
/// </summary>
public static class RenderDiagnostics
{
    public enum OverlayMode
    {
        None,
        Overdraw,
        DirtyRegions,
    }

    public sealed class OverdrawCell
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int DrawCount;
    }

    public sealed class DirtyRegionSnapshot
    {
        public DateTime Timestamp { get; }
        public Rect Region { get; }
        public int FrameIndex { get; }
        internal DirtyRegionSnapshot(Rect region, int frameIndex)
        {
            Timestamp = DateTime.Now;
            Region = region;
            FrameIndex = frameIndex;
        }
    }

    public sealed class GpuResourceSnapshot
    {
        public DateTime Timestamp { get; }
        public int GlyphAtlasSlotsUsed { get; }
        public int GlyphAtlasSlotsTotal { get; }
        public long GlyphAtlasBytes { get; }
        public int PathCacheEntries { get; }
        public long PathCacheBytes { get; }
        public int TextureCount { get; }
        public long TextureBytes { get; }

        internal GpuResourceSnapshot(
            int glyphUsed, int glyphTotal, long glyphBytes,
            int pathEntries, long pathBytes,
            int textureCount, long textureBytes)
        {
            Timestamp = DateTime.Now;
            GlyphAtlasSlotsUsed = glyphUsed;
            GlyphAtlasSlotsTotal = glyphTotal;
            GlyphAtlasBytes = glyphBytes;
            PathCacheEntries = pathEntries;
            PathCacheBytes = pathBytes;
            TextureCount = textureCount;
            TextureBytes = textureBytes;
        }
    }

    public const int OverdrawGridCells = 32;
    private const int DirtyHistoryCapacity = 128;

    private static OverlayMode s_mode;
    private static GpuResourceSnapshot? s_latestGpuSnapshot;
    private static readonly ConcurrentQueue<DirtyRegionSnapshot> s_dirtyHistory = new();
    private static int s_frameCounter;
    private static readonly object s_overdrawLock = new();
    private static int[,]? s_overdrawBins;
    private static int s_overdrawBinWidth;
    private static int s_overdrawBinHeight;
    private static double s_overdrawCellW;
    private static double s_overdrawCellH;
    private static double s_overdrawWindowW;
    private static double s_overdrawWindowH;

    public static OverlayMode Mode
    {
        get => s_mode;
        set
        {
            if (s_mode != value)
            {
                s_mode = value;
                OverlayModeChanged?.Invoke(null, EventArgs.Empty);
            }
        }
    }

    public static event EventHandler? OverlayModeChanged;

    public static GpuResourceSnapshot? LatestGpuSnapshot => s_latestGpuSnapshot;

    public static void PublishGpuSnapshot(GpuResourceSnapshot snapshot)
    {
        s_latestGpuSnapshot = snapshot;
    }

    public static void PublishGpuSnapshot(
        int glyphUsed, int glyphTotal, long glyphBytes,
        int pathEntries = 0, long pathBytes = 0,
        int textureCount = 0, long textureBytes = 0)
    {
        s_latestGpuSnapshot = new GpuResourceSnapshot(
            glyphUsed, glyphTotal, glyphBytes,
            pathEntries, pathBytes, textureCount, textureBytes);
    }

    public static void RecordDirtyRegion(Rect region)
    {
        if (region.IsEmpty) return;
        int index = Interlocked.Increment(ref s_frameCounter);
        s_dirtyHistory.Enqueue(new DirtyRegionSnapshot(region, index));
        while (s_dirtyHistory.Count > DirtyHistoryCapacity && s_dirtyHistory.TryDequeue(out _)) { }
    }

    public static IReadOnlyList<DirtyRegionSnapshot> SnapshotDirtyHistory() => s_dirtyHistory.ToArray();

    public static void ResetOverdrawForFrame(double windowWidth, double windowHeight)
    {
        if (Mode != OverlayMode.Overdraw) return;
        if (windowWidth <= 0 || windowHeight <= 0) return;
        lock (s_overdrawLock)
        {
            if (s_overdrawBins == null ||
                s_overdrawBinWidth != OverdrawGridCells ||
                s_overdrawBinHeight != OverdrawGridCells)
            {
                s_overdrawBins = new int[OverdrawGridCells, OverdrawGridCells];
                s_overdrawBinWidth = OverdrawGridCells;
                s_overdrawBinHeight = OverdrawGridCells;
            }
            Array.Clear(s_overdrawBins);
            s_overdrawWindowW = windowWidth;
            s_overdrawWindowH = windowHeight;
            s_overdrawCellW = windowWidth / OverdrawGridCells;
            s_overdrawCellH = windowHeight / OverdrawGridCells;
        }
    }

    public static void RecordDraw(Rect bounds)
    {
        if (Mode != OverlayMode.Overdraw) return;
        lock (s_overdrawLock)
        {
            if (s_overdrawBins == null || s_overdrawCellW <= 0 || s_overdrawCellH <= 0) return;
            double maxX = Math.Min(bounds.X + bounds.Width, s_overdrawWindowW);
            double maxY = Math.Min(bounds.Y + bounds.Height, s_overdrawWindowH);
            if (maxX <= 0 || maxY <= 0) return;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Max(0, bounds.X) / s_overdrawCellW));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Max(0, bounds.Y) / s_overdrawCellH));
            int x1 = Math.Min(OverdrawGridCells - 1, (int)Math.Floor((maxX - 0.01) / s_overdrawCellW));
            int y1 = Math.Min(OverdrawGridCells - 1, (int)Math.Floor((maxY - 0.01) / s_overdrawCellH));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    s_overdrawBins[x, y]++;
                }
            }
        }
    }

    public static IReadOnlyList<OverdrawCell> SnapshotOverdraw()
    {
        lock (s_overdrawLock)
        {
            if (s_overdrawBins == null) return Array.Empty<OverdrawCell>();
            var list = new List<OverdrawCell>();
            for (int y = 0; y < OverdrawGridCells; y++)
            {
                for (int x = 0; x < OverdrawGridCells; x++)
                {
                    int count = s_overdrawBins[x, y];
                    if (count == 0) continue;
                    list.Add(new OverdrawCell
                    {
                        X = (int)(x * s_overdrawCellW),
                        Y = (int)(y * s_overdrawCellH),
                        Width = (int)Math.Ceiling(s_overdrawCellW),
                        Height = (int)Math.Ceiling(s_overdrawCellH),
                        DrawCount = count,
                    });
                }
            }
            return list;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Per-frame draw-API call counters + cumulative wall-clock time.
    //
    // Wraps every native draw entry point in RenderTarget.cs (FillPath,
    // StrokePath, DrawLine, …) so DevTools can show which APIs dominate the
    // frame — counts and total managed+native time side-by-side make it easy
    // to spot, e.g., a viewport that issues 28 StrokePathAtOffset calls
    // taking 50 ms vs 500 short DrawLine calls taking 0.2 ms.
    //
    // Single-threaded by design: the UI thread is the only producer/consumer.
    // Disabled by default so production apps pay no overhead; DevTools sets
    // ApiStatsEnabled=true while the Perf tab is visible.
    // ─────────────────────────────────────────────────────────────────────
    public sealed class DrawApiEntry
    {
        public string Name { get; init; } = "";
        public long Count { get; init; }
        public long TotalTicks { get; init; }
        public double TotalMs => TotalTicks * 1000.0 / Stopwatch.Frequency;
        public double AvgUs => Count == 0 ? 0 : (TotalTicks * 1_000_000.0 / Stopwatch.Frequency) / Count;
    }

    public sealed class DrawApiStats
    {
        public DateTime Timestamp { get; init; }
        public IReadOnlyList<DrawApiEntry> Entries { get; init; } = Array.Empty<DrawApiEntry>();
        public long TotalTicks { get; init; }
        public double TotalMs => TotalTicks * 1000.0 / Stopwatch.Frequency;
    }

    public static bool ApiStatsEnabled { get; set; }

    public static DrawApiStats? LatestDrawApiStats => s_latestDrawApiStats;

    /// <summary>
    /// Per-frame native path telemetry published by the unified core API
    /// (jalium_query_path_stats). Helps DevTools tell apart "the pipeline is
    /// fast but called too many times" from "every call is a cache miss
    /// running the full flatten + triangulate + rasterize pipeline".
    ///
    /// Sourced from the cross-backend atomics in jalium.native.core:
    ///  • Stroke / Fill: pixel-space rect-cache hit/miss (D3D12 Impeller).
    ///  • Geometry:      source-space PathGeometryCache hit/miss (Vulkan
    ///                   GPU-replay path; D3D12 second-tier in commit 2).
    ///  • Flatten / Triangulate: cumulative ns + counts across all backends.
    /// </summary>
    public sealed class PathCacheFrameStats
    {
        public DateTime Timestamp { get; init; }
        public long StrokeHits { get; init; }
        public long StrokeMisses { get; init; }
        public long FillHits { get; init; }
        public long FillMisses { get; init; }
        public long StrokeRectsTotal { get; init; }   // sum of rect count over hits this frame
        public long FillRectsTotal { get; init; }

        // Source-space geometry cache (PathGeometryCache).
        public long GeometryHits { get; init; }
        public long GeometryMisses { get; init; }

        // Per-frame flatten work. FlattenNs is the cumulative wall time spent
        // inside the bezier-decompose entry points across all paths this
        // frame; FlattenInputSegments is the total path-command count fed in
        // (proportional to source complexity); FlattenOutputVerts is the
        // total polyline vertices produced (proportional to on-screen
        // complexity). The ratio tells you whether transform-scale-aware
        // tolerance is doing its job (verts ≈ scale × input).
        public long FlattenNs { get; init; }
        public long FlattenInputSegments { get; init; }
        public long FlattenOutputVerts { get; init; }

        // Per-frame triangulate work (Impeller fallback path: scanline raster
        // empty → ear-clip recovery, plus Vulkan PathGeometryCache miss).
        // Fail count is the loud signal: a path that consistently fails
        // triangulation means the cache stores an empty result and never
        // helps — see project_vulkan_path_cache memory.
        public long TriangulateNs { get; init; }
        public long TriangulateOk { get; init; }
        public long TriangulateFail { get; init; }

        // Number of PathGeometryCache (and any future LRU using path_stats)
        // evictions this frame. A nonzero value while geometry-hit rate is
        // low says the cache capacity is undersized for the current path
        // working set.
        public long CacheEvictions { get; init; }
    }

    public static PathCacheFrameStats? LatestPathCacheStats => s_latestPathCacheStats;
    private static PathCacheFrameStats? s_latestPathCacheStats;

    public static void PublishPathCacheStats(PathCacheFrameStats s)
    {
        s_latestPathCacheStats = s;
    }

    /// <summary>
    /// Per-frame native bitmap upload telemetry, sourced from the unified
    /// core API (jalium_query_bitmap_stats — D3D12 + Vulkan + software all
    /// feed the same atomic state in core.dll).
    ///
    ///  • UploadCount / UploadBytes — full upload pipeline ran
    ///    (CreateCommittedResource if non-dynamic + Map + memcpy +
    ///    CopyTextureRegion + barriers).
    ///  • FastPathHits — cached GPU texture returned immediately.
    ///  • DynamicReuses — upload reused existing texture + upload buffer
    ///    (no CreateCommittedResource; video frame / WriteableBitmap path).
    ///  • MemcmpShortCircuits — SetBitmapData / UpdatePackedPixels saw
    ///    identical content and bailed.
    ///  • GpuResidentBytes — net live bytes pinned in GPU heaps across all
    ///    bitmaps (delta since last frame; signed, can decrease on release).
    ///  • AtlasHits — reserved for future texture-atlas paths.
    ///  • CacheEvictions — bitmap-side LRU evictions (e.g. downscale cache).
    ///
    /// Use to confirm whether DrawBitmap CPU cost is upload-dominated and
    /// whether the various caches are doing their job.
    /// </summary>
    public sealed class BitmapUploadFrameStats
    {
        public DateTime Timestamp { get; init; }
        public long UploadCount { get; init; }
        public long UploadBytes { get; init; }
        public long FastPathHits { get; init; }
        public long DynamicReuses { get; init; }
        public long MemcmpShortCircuits { get; init; }
        public long GpuResidentBytes { get; init; }   // delta (signed) since last frame
        public long AtlasHits { get; init; }
        public long CacheEvictions { get; init; }
    }

    public static BitmapUploadFrameStats? LatestBitmapUploadStats => s_latestBitmapUploadStats;
    private static BitmapUploadFrameStats? s_latestBitmapUploadStats;

    public static void PublishBitmapUploadStats(BitmapUploadFrameStats s)
    {
        s_latestBitmapUploadStats = s;
    }

    /// <summary>
    /// Per-frame native text-cache telemetry, sourced from the unified core
    /// API (jalium_query_text_stats — D3D12 + Vulkan + software all feed the
    /// same atomic state in core.dll). Covers the three caches on the
    /// DrawText hot path:
    ///
    ///  • Layout cache — text+maxLines key memoises the IDWriteTextLayout so
    ///    width/height fluctuations don't re-run DirectWrite shaping.
    ///  • Instance cache — resolved-glyph quads + decorations for a (layout,
    ///    origin, dpi, aa, hinting) tuple. Hit skips layout->Draw + per-glyph
    ///    atlas walk; emit just copies the prepared instances and applies the
    ///    caller's premultiplied colour.
    ///  • Glyph raster cache — per-(fontFace, glyphIndex, fontSize, subpixel,
    ///    aa, hinting) atlas slot. Hit reuses the already-rasterized bitmap;
    ///    miss runs DirectWrite glyph rasterization and packs a new atlas
    ///    rectangle.
    ///
    /// AtlasResets fires whenever the atlas was wiped (overflow, AA mode
    /// swap, generation bump) — a nonzero value here while instance hit-rate
    /// is also low explains why the cache "looks broken": every entry built
    /// in the previous frame got invalidated.
    ///
    /// DrawTextCalls is the raw RenderText entry-point count — this should
    /// match the managed DrawText API counter on the Perf tab; a divergence
    /// points at a non-managed text path (e.g. the inverse-transform fast
    /// path going through DrawTextWithInverseTransform).
    /// </summary>
    public sealed class TextCacheFrameStats
    {
        public DateTime Timestamp { get; init; }
        public long LayoutHits { get; init; }
        public long LayoutMisses { get; init; }
        public long LayoutEvictions { get; init; }
        public long InstanceHits { get; init; }
        public long InstanceMisses { get; init; }
        public long InstanceEvictions { get; init; }
        public long GlyphRasterHits { get; init; }
        public long GlyphRasterMisses { get; init; }
        public long AtlasResets { get; init; }
        public long DrawTextCalls { get; init; }
        public long EmittedGlyphs { get; init; }
        public long EmittedDecorations { get; init; }
    }

    public static TextCacheFrameStats? LatestTextCacheStats => s_latestTextCacheStats;
    private static TextCacheFrameStats? s_latestTextCacheStats;

    public static void PublishTextCacheStats(TextCacheFrameStats s)
    {
        s_latestTextCacheStats = s;
    }

    /// <summary>
    /// Per-frame layout pass breakdown sourced from <c>LayoutManager.UpdateLayout</c>.
    /// Lets DevTools tell apart "measure is heavy", "arrange is heavy", and the
    /// red-flag "tree is fighting itself" case where Iterations &gt; 1 means the
    /// measure / arrange of one element queued another for re-measure within the
    /// same UpdateLayout call.
    ///
    /// MeasureMs / ArrangeMs together don't include the sort / depth-precompute /
    /// queue-drain overhead — those sit in (TotalMs − MeasureMs − ArrangeMs) and
    /// usually round to zero. When the gap is large, the next-step investigation
    /// is layout-internal bookkeeping, not user MeasureOverride code.
    /// </summary>
    public sealed class LayoutPassFrameStats
    {
        public DateTime Timestamp { get; init; }
        public long TotalNs { get; init; }
        public long MeasureNs { get; init; }
        public long ArrangeNs { get; init; }
        public int MeasureCount { get; init; }
        public int ArrangeCount { get; init; }
        public int Iterations { get; init; }

        public double TotalMs => TotalNs / 1_000_000.0;
        public double MeasureMs => MeasureNs / 1_000_000.0;
        public double ArrangeMs => ArrangeNs / 1_000_000.0;
    }

    public static LayoutPassFrameStats? LatestLayoutPassStats => s_latestLayoutPassStats;
    private static LayoutPassFrameStats? s_latestLayoutPassStats;

    public static void PublishLayoutPassStats(LayoutPassFrameStats s)
    {
        s_latestLayoutPassStats = s;
    }

    // Managed-side bitmap downscale cache eviction counter. Producer is
    // BitmapDownscaleCache; consumer is Window.cs's per-frame publish which
    // folds it into BitmapUploadFrameStats.CacheEvictions so DevTools shows a
    // single "Cache" line regardless of which side (native LRU / managed
    // thumbnail LRU) did the eviction.
    private static long s_bitmapDownscaleEvictionsTotal;

    public static long BitmapDownscaleEvictionsTotal
        => System.Threading.Interlocked.Read(ref s_bitmapDownscaleEvictionsTotal);

    public static void OnBitmapDownscaleEviction(int count)
    {
        if (count > 0)
            System.Threading.Interlocked.Add(ref s_bitmapDownscaleEvictionsTotal, count);
    }

    /// <summary>
    /// Per-frame retained-mode drawing-cache hit rate. Records = visuals
    /// whose OnRender ran AND emitted into a fresh recorder (cache miss);
    /// Replays = visuals served straight from the cached Drawing (the win);
    /// Bypasses = visuals whose OnRender ran without caching at all (host
    /// not installed / Visual opted out / DC isn't ICacheableDrawingContext).
    /// When Records dominate, the visual tree is being marked dirty every
    /// frame — the next optimisation is finding the invalidation source,
    /// not improving the cache implementation.
    /// </summary>
    public sealed class RetainedCacheFrameStats
    {
        public DateTime Timestamp { get; init; }
        public long Records { get; init; }
        public long Replays { get; init; }
        public long Bypasses { get; init; }
    }

    public static RetainedCacheFrameStats? LatestRetainedCacheStats => s_latestRetainedCacheStats;
    private static RetainedCacheFrameStats? s_latestRetainedCacheStats;

    public static void PublishRetainedCacheStats(RetainedCacheFrameStats s)
    {
        s_latestRetainedCacheStats = s;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Frame-pacing diagnostics — answer "why does BeginDraw look slow?"
    //
    // BeginAttempts / BeginFailures: counted on the managed side because
    // only the Window render loop knows how many TryBeginDraw attempts a
    // single logical frame produced (the 50 ms native fence-wait timeout
    // returns InvalidState and the loop retries via ScheduleDeferredRender).
    // A "frame" here = one successful BeginDraw + EndDraw pair; the
    // counters accumulate failures across the retries that preceded it.
    //
    // FrameGpuWaitNs / SwapBufferCount / LastFrameGpuWorkNs: pulled from
    // the backend through TryQueryGpuStats; semantics described on
    // GpuResourceStats. The publish call rolls them up together with the
    // managed counters so DevTools renders a single "Frame pacing" block.
    // ─────────────────────────────────────────────────────────────────────
    public sealed class FramePacingSnapshot
    {
        public DateTime Timestamp { get; init; }
        public int BeginAttempts { get; init; }
        public int BeginFailures { get; init; }
        public long FrameGpuWaitNs { get; init; }
        public int SwapBufferCount { get; init; }
        public long LastFramePresentToReadyNs { get; init; }
        public long FrameWaitableWaitNs { get; init; }
        public double FrameGpuWaitMs => FrameGpuWaitNs / 1_000_000.0;
        public double LastFramePresentToReadyMs => LastFramePresentToReadyNs / 1_000_000.0;
        public double FrameWaitableWaitMs => FrameWaitableWaitNs / 1_000_000.0;
    }

    public static FramePacingSnapshot? LatestFramePacing => s_latestFramePacing;
    private static FramePacingSnapshot? s_latestFramePacing;
    private static int s_frameBeginAttempts;
    private static int s_frameBeginFailures;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void OnBeginDrawAttempt(bool success)
    {
        if (!ApiStatsEnabled) return;
        Interlocked.Increment(ref s_frameBeginAttempts);
        if (!success) Interlocked.Increment(ref s_frameBeginFailures);
    }

    public static void PublishFramePacing(
        long frameGpuWaitNs,
        int swapBufferCount,
        long lastFramePresentToReadyNs,
        long frameWaitableWaitNs)
    {
        if (!ApiStatsEnabled) return;
        int attempts = Interlocked.Exchange(ref s_frameBeginAttempts, 0);
        int failures = Interlocked.Exchange(ref s_frameBeginFailures, 0);
        s_latestFramePacing = new FramePacingSnapshot
        {
            Timestamp = DateTime.Now,
            BeginAttempts = attempts,
            BeginFailures = failures,
            FrameGpuWaitNs = frameGpuWaitNs,
            SwapBufferCount = swapBufferCount,
            LastFramePresentToReadyNs = lastFramePresentToReadyNs,
            FrameWaitableWaitNs = frameWaitableWaitNs,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Per-category GPU timing — sourced from hardware timestamp queries on
    // the graphics queue. Pairs with FramePacingSnapshot for the full
    // story: pacing tells *whether* the UI thread is GPU-bound; timing
    // tells *which categories* of GPU work are the cause. The breakdown
    // is for the *previous* frame, since timestamp readback is fence-gated.
    // ─────────────────────────────────────────────────────────────────────
    public sealed class GpuTimingSnapshot
    {
        public DateTime Timestamp { get; init; }
        public bool Valid { get; init; }
        public long TotalGpuNs { get; init; }
        public long SdfRectNs { get; init; }
        public long TextNs { get; init; }
        public long BitmapNs { get; init; }
        public long PathNs { get; init; }
        public long BackdropNs { get; init; }
        public long LiquidGlassNs { get; init; }
        public long OtherNs { get; init; }
        public int BatchCount { get; init; }
        public double TotalGpuMs => TotalGpuNs / 1_000_000.0;
    }

    public static GpuTimingSnapshot? LatestGpuTiming => s_latestGpuTiming;
    private static GpuTimingSnapshot? s_latestGpuTiming;

    public static void PublishGpuTiming(
        bool valid,
        long totalGpuNs,
        long sdfRectNs, long textNs, long bitmapNs, long pathNs,
        long backdropNs, long liquidGlassNs, long otherNs,
        int batchCount)
    {
        if (!ApiStatsEnabled) return;
        s_latestGpuTiming = new GpuTimingSnapshot
        {
            Timestamp = DateTime.Now,
            Valid = valid,
            TotalGpuNs = totalGpuNs,
            SdfRectNs = sdfRectNs,
            TextNs = textNs,
            BitmapNs = bitmapNs,
            PathNs = pathNs,
            BackdropNs = backdropNs,
            LiquidGlassNs = liquidGlassNs,
            OtherNs = otherNs,
            BatchCount = batchCount,
        };
    }

    // Map name → (count, ticks). Plain dictionary because all access is on UI thread.
    private static readonly Dictionary<string, (long count, long ticks)> s_currentApiStats = new();
    private static DrawApiStats? s_latestDrawApiStats;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RecordApi(string name, long elapsedTicks)
    {
        if (!ApiStatsEnabled) return;
        ref var entry = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
            s_currentApiStats, name, out _);
        entry.count++;
        entry.ticks += elapsedTicks;
    }

    public static void PublishAndResetApiStats()
    {
        if (!ApiStatsEnabled || s_currentApiStats.Count == 0) return;
        long totalTicks = 0;
        var entries = new List<DrawApiEntry>(s_currentApiStats.Count);
        foreach (var kv in s_currentApiStats)
        {
            entries.Add(new DrawApiEntry
            {
                Name = kv.Key,
                Count = kv.Value.count,
                TotalTicks = kv.Value.ticks,
            });
            totalTicks += kv.Value.ticks;
        }
        // Sort hot-first so DevTools renders the worst offenders at the top.
        entries.Sort((a, b) => b.TotalTicks.CompareTo(a.TotalTicks));
        s_latestDrawApiStats = new DrawApiStats
        {
            Timestamp = DateTime.Now,
            Entries = entries,
            TotalTicks = totalTicks,
        };
        s_currentApiStats.Clear();
    }
}
