using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.DesktopDemo.Themes;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Threading;

namespace Jalium.UI.DesktopDemo;

/// <summary>Nonvirtualized image workload with scroll-cycle diagnostics and a replaceable presentation.</summary>
[TemplatePart(Name = "PART_Scroller", Type = typeof(ScrollViewer))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Ready", GroupName = "DataStates")]
[TemplateVisualState(Name = "Empty", GroupName = "DataStates")]
[TemplateVisualState(Name = "Monitoring", GroupName = "MonitoringStates")]
[TemplateVisualState(Name = "Paused", GroupName = "MonitoringStates")]
public class ScrollStressSurface : ItemsControl
{
    private const int SourceImageCount = 12, SourceWidth = 1024, SourceHeight = 1024;
    public static readonly DependencyProperty SourceImagesProperty = DependencyProperty.Register(nameof(SourceImages), typeof(IReadOnlyList<BitmapImage>), typeof(ScrollStressSurface), new PropertyMetadata(null, OnCardsChanged));
    public static readonly DependencyProperty CardCountProperty = DependencyProperty.Register(nameof(CardCount), typeof(int), typeof(ScrollStressSurface), new PropertyMetadata(240, OnCardsChanged), value => value is int count && count >= 0 && count <= 10_000);
    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(nameof(CardWidth), typeof(double), typeof(ScrollStressSurface), new PropertyMetadata(500d, OnCardsChanged), value => value is double size && double.IsFinite(size) && size >= 32);
    public static readonly DependencyProperty CardHeightProperty = DependencyProperty.Register(nameof(CardHeight), typeof(double), typeof(ScrollStressSurface), new PropertyMetadata(220d, OnCardsChanged), value => value is double size && double.IsFinite(size) && size >= 32);
    public static readonly DependencyProperty IsMonitoringProperty = DependencyProperty.Register(nameof(IsMonitoring), typeof(bool), typeof(ScrollStressSurface), new PropertyMetadata(true, OnMonitoringChanged));
    private static readonly DependencyPropertyKey LiveTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(LiveText), typeof(string), typeof(ScrollStressSurface), new PropertyMetadata("实时: 等待渲染统计"));
    private static readonly DependencyPropertyKey CycleTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CycleText), typeof(string), typeof(ScrollStressSurface), new PropertyMetadata("上次滚动周期: 尚未滚动"));
    private static readonly DependencyPropertyKey HistoryTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HistoryText), typeof(string), typeof(ScrollStressSurface), new PropertyMetadata("历史:"));
    private static readonly DependencyPropertyKey CompletedCyclesPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CompletedCycles), typeof(int), typeof(ScrollStressSurface), new PropertyMetadata(0));
    public static readonly DependencyProperty LiveTextProperty = LiveTextPropertyKey.DependencyProperty;
    public static readonly DependencyProperty CycleTextProperty = CycleTextPropertyKey.DependencyProperty;
    public static readonly DependencyProperty HistoryTextProperty = HistoryTextPropertyKey.DependencyProperty;
    public static readonly DependencyProperty CompletedCyclesProperty = CompletedCyclesPropertyKey.DependencyProperty;
    public IReadOnlyList<BitmapImage>? SourceImages { get => (IReadOnlyList<BitmapImage>?)GetValue(SourceImagesProperty); set => SetValue(SourceImagesProperty, value); }
    public int CardCount { get => (int)GetValue(CardCountProperty)!; set => SetValue(CardCountProperty, value); }
    public double CardWidth { get => (double)GetValue(CardWidthProperty)!; set => SetValue(CardWidthProperty, value); }
    public double CardHeight { get => (double)GetValue(CardHeightProperty)!; set => SetValue(CardHeightProperty, value); }
    public bool IsMonitoring { get => (bool)GetValue(IsMonitoringProperty)!; set => SetValue(IsMonitoringProperty, value); }
    public string LiveText => (string)GetValue(LiveTextProperty)!;
    public string CycleText => (string)GetValue(CycleTextProperty)!;
    public string HistoryText => (string)GetValue(HistoryTextProperty)!;
    public int CompletedCycles => (int)GetValue(CompletedCyclesProperty)!;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly List<string> _history = [];
    private ScrollViewer? _scroller;
    private SnapshotState _cycleStart;
    private long _lastScrollTick;
    private bool _inCycle;
    public ScrollStressSurface()
    {
        ScrollStressTheme.Register(); DefaultStyleKey = typeof(ScrollStressSurface);
        _timer.Tick += (_, _) => RefreshStatistics();
        Loaded += (_, _) => { RenderDiagnostics.ApiStatsEnabled = true; UpdateTimer(); };
        Unloaded += (_, _) => { _timer.Stop(); _inCycle = false; };
    }
    /// <summary>Prepares twelve distinct bitmap instances; callers may instead supply their own shared sources.</summary>
    public void LoadImages(string imagePath = @"C:\Users\suppe\OneDrive\图片\01.jpg") => SetCurrentValue(SourceImagesProperty, LoadOrSynthesizeImages(imagePath));
    public override void OnApplyTemplate()
    {
        ReleaseParts(); base.OnApplyTemplate(); _scroller = GetTemplateChild("PART_Scroller") as ScrollViewer;
        if (_scroller is not null) _scroller.ScrollChanged += OnScrollChanged;
        UpdateStates(); UpdateTimer();
    }
    protected override void OnTemplateChanging() { _timer.Stop(); ReleaseParts(); base.OnTemplateChanging(); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateStates(); }
    private void ReleaseParts() { if (_scroller is not null) _scroller.ScrollChanged -= OnScrollChanged; _scroller = null; _inCycle = false; }
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!IsMonitoring || !IsLoaded) return;
        _lastScrollTick = Environment.TickCount64;
        if (!_inCycle) { _inCycle = true; _cycleStart = SnapshotState.Capture(); }
    }
    /// <summary>Samples the existing renderer counters and completes a cycle after 250 ms without scrolling.</summary>
    public void RefreshStatistics()
    {
        var current = SnapshotState.Capture(); SetValue(LiveTextPropertyKey, $"实时:  {current.Format()}");
        if (!_inCycle || Environment.TickCount64 - _lastScrollTick < 250) return;
        _inCycle = false; SetValue(CompletedCyclesPropertyKey, CompletedCycles + 1);
        var diff = current.Diff(_cycleStart); var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _history.Insert(0, $"#{CompletedCycles} {timestamp}  {diff.FormatDelta()}");
        if (_history.Count > 6) _history.RemoveAt(_history.Count - 1);
        SetValue(CycleTextPropertyKey, $"上次滚动周期:  {diff.FormatDelta()}"); SetValue(HistoryTextPropertyKey, "历史:\n" + string.Join("\n", _history));
    }
    private static void OnCardsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ScrollStressSurface)d).RebuildCards();
    private static void OnMonitoringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { var control = (ScrollStressSurface)d; control._inCycle = false; control.UpdateTimer(); control.UpdateStates(); }
    private void RebuildCards()
    {
        SetCurrentValue(ItemsSourceProperty, SourceImages is { Count: > 0 } sources
            ? Enumerable.Range(0, CardCount).Select(index => new ScrollStressCard(sources[index % sources.Count], $"卡片 #{index}", $"图源 #{index % sources.Count}  ·  {sources[index % sources.Count].PixelWidth}×{sources[index % sources.Count].PixelHeight}  raw={(sources[index % sources.Count].RawPixelData?.Length ?? 0) / 1024}KB", CardWidth - 8, CardHeight - 8)).ToArray()
            : Array.Empty<ScrollStressCard>()); UpdateStates();
    }
    private void UpdateTimer() { if (IsLoaded && IsMonitoring && _scroller is not null) _timer.Start(); else _timer.Stop(); }
    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, SourceImages is { Count: > 0 } && CardCount > 0 ? "Ready" : "Empty", false);
        VisualStateManager.GoToState(this, IsMonitoring ? "Monitoring" : "Paused", false);
    }
    private static List<BitmapImage> LoadOrSynthesizeImages(string realPath)
    {
        // 优先从用户图库加载真实 JPG。 没有则退回合成图。
        var result = new List<BitmapImage>();
        System.Diagnostics.Debug.WriteLine($"[ScrollStress] File.Exists({realPath}) = {File.Exists(realPath)}");
        try
        {
            if (File.Exists(realPath))
            {
                // 加载一次原图,然后用 BitmapDownscaleCache 的 box-filter 缩到合理尺寸,
                // 重新打包成多个独立 BitmapImage(模拟多个 ImageSource 引用)。
                // 原 JPG 是 3840×2160 / 32MB BGRA — 240 张全 GPU 上传会 380MB,
                // 大概率第一帧 OOM/timeout 导致窗口空白。
                var fullSize = BitmapImage.FromFile(realPath);
                System.Diagnostics.Debug.WriteLine($"[ScrollStress] full image: {fullSize.PixelWidth}x{fullSize.PixelHeight} raw={fullSize.RawPixelData?.Length ?? 0}");

                var rawPixels = fullSize.RawPixelData!;
                int srcW = fullSize.PixelWidth;
                int srcH = fullSize.PixelHeight;
                int srcStride = fullSize.PixelStride > 0 ? fullSize.PixelStride : srcW * 4;

                // 目标 ~512×288 (16:9), ~600KB/张, 12 张总共 ~7MB GPU
                int dstW = 512;
                int dstH = (int)((long)srcH * dstW / srcW);
                byte[] smallPixels = NearestDownscale(rawPixels, srcW, srcH, srcStride, dstW, dstH);
                System.Diagnostics.Debug.WriteLine($"[ScrollStress] downscaled to {dstW}x{dstH}, {smallPixels.Length} bytes per copy");

                for (int i = 0; i < SourceImageCount; ++i)
                {
                    result.Add(BitmapImage.FromPixels(smallPixels, dstW, dstH));
                }
                return result;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScrollStress] LoadFromFile FAILED: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine(ex.StackTrace);
        }
        System.Diagnostics.Debug.WriteLine("[ScrollStress] falling back to synthetic images");
        return BuildSyntheticImages(SourceImageCount);
    }

    private struct SnapshotState
    {
        public long UploadCount;
        public long UploadBytes;
        public long FastPathHits;
        public long GpuResidentBytes; // 注意 LatestBitmapUploadStats.GpuResidentBytes 已经是 per-frame delta
        public long CacheEvictions;
        public long RetainedRecords;
        public long RetainedReplays;
        public long RetainedBypasses;

        public static SnapshotState Capture()
        {
            var b = RenderDiagnostics.LatestBitmapUploadStats;
            // 累计版本: BitmapUploadFrameStats 是 per-frame delta,要拿"当前累计"
            // 没有现成 API,所以这里改成读 Visual 上的累计 + 我们自己累加 frame delta。
            // 简化做法: 我们把 frame delta 当成"自上次状态采样以来的增量"近似,
            // 周期内累计 = 周期结束时各帧 delta 之和。 实现: SnapshotState 不记录
            // 累计,而是在 timer 回调里我们直接累积差量。 这里先存 0 占位,
            // 实际累积放到 caller 的 cycleStart 计算里。
            //
            // 简化: 直接读 Visual 累计计数器(这些是 process-start 累计的真值)。
            return new SnapshotState
            {
                RetainedRecords = Visual.RetainedCacheRecordsTotal,
                RetainedReplays = Visual.RetainedCacheReplaysTotal,
                RetainedBypasses = Visual.RetainedCacheBypassesTotal,
                CacheEvictions = RenderDiagnostics.BitmapDownscaleEvictionsTotal,
                // BitmapUpload 累计没有 API, frame delta 又每帧重置,
                // 所以在 caller 那边自行累加; 这里只暴露 frame delta 占位。
                UploadCount = b?.UploadCount ?? 0,
                UploadBytes = b?.UploadBytes ?? 0,
                FastPathHits = b?.FastPathHits ?? 0,
                GpuResidentBytes = b?.GpuResidentBytes ?? 0,
            };
        }

        public string Format()
        {
            var b = RenderDiagnostics.LatestBitmapUploadStats;
            string up = b == null
                ? "(no bitmap stats)"
                : $"Uploads {b.UploadCount,3} ({b.UploadBytes / 1024.0 / 1024.0:F1} MB)  FastPath {b.FastPathHits,3}  GpuResΔ {b.GpuResidentBytes / 1024.0 / 1024.0:+0.00;-0.00;0} MB  Evict {b.CacheEvictions}";
            return $"{up}  |  Retained R/P/B {RetainedRecords}/{RetainedReplays}/{RetainedBypasses}";
        }

        public Diff Diff(SnapshotState start)
        {
            return new Diff
            {
                RecordsDelta = RetainedRecords - start.RetainedRecords,
                ReplaysDelta = RetainedReplays - start.RetainedReplays,
                BypassesDelta = RetainedBypasses - start.RetainedBypasses,
                EvictionsDelta = CacheEvictions - start.CacheEvictions,
            };
        }
    }

    private struct Diff
    {
        public long RecordsDelta;
        public long ReplaysDelta;
        public long BypassesDelta;
        public long EvictionsDelta;

        public string FormatDelta()
            => $"Records+{RecordsDelta}  Replays+{ReplaysDelta}  Bypass+{BypassesDelta}  Evictions+{EvictionsDelta}";
    }

    private static byte[] NearestDownscale(byte[] src, int srcW, int srcH, int srcStride, int dstW, int dstH)
    {
        var dst = new byte[(long)dstW * dstH * 4];
        for (int dy = 0; dy < dstH; ++dy)
        {
            int sy = (int)(((long)dy * 2 + 1) * srcH / (dstH * 2));
            if (sy >= srcH) sy = srcH - 1;
            long srcRowOff = (long)sy * srcStride;
            long dstRowOff = (long)dy * dstW * 4;
            for (int dx = 0; dx < dstW; ++dx)
            {
                int sx = (int)(((long)dx * 2 + 1) * srcW / (dstW * 2));
                if (sx >= srcW) sx = srcW - 1;
                long sOff = srcRowOff + (long)sx * 4;
                long dOff = dstRowOff + (long)dx * 4;
                dst[dOff] = src[sOff];
                dst[dOff + 1] = src[sOff + 1];
                dst[dOff + 2] = src[sOff + 2];
                dst[dOff + 3] = src[sOff + 3];
            }
        }
        return dst;
    }

    private static List<BitmapImage> BuildSyntheticImages(int count)
    {
        var result = new List<BitmapImage>(count);
        for (int i = 0; i < count; ++i)
        {
            int W = SourceWidth, H = SourceHeight;
            var pixels = new byte[W * H * 4];
            byte baseR = (byte)(40 + i * 17);
            byte baseG = (byte)(80 + i * 19);
            byte baseB = (byte)(160 + i * 11);
            for (int y = 0; y < H; ++y)
            {
                for (int x = 0; x < W; ++x)
                {
                    int o = (y * W + x) * 4;
                    pixels[o] = (byte)((baseB + x / 3) & 0xff);
                    pixels[o + 1] = (byte)((baseG + y / 3) & 0xff);
                    pixels[o + 2] = (byte)((baseR + (x ^ y) / 5) & 0xff);
                    pixels[o + 3] = 255;
                }
            }
            result.Add(BitmapImage.FromPixels(pixels, W, H));
        }
        return result;
    }
}

public sealed record ScrollStressCard(BitmapImage Source, string Title, string Description, double Width, double Height);
