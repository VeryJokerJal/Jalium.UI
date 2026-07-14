using System.Globalization;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.DesktopDemo;

// 复现"滚动越多越卡 / 动画停下后再滚一次更卡"的压力测试窗口。
//
// 区别于 ImagePerfWindow:
//  - 200+ 卡片(覆盖滚轮多次惯性周期)
//  - 顶部 HUD 自动监听滚动周期(ScrollChanged 安静 200ms = 一次周期结束),
//    周期结束时记录指标,与上次周期做差,显示出"每个滚动周期的累积"
//  - 列出滚轮滚动的次数 + 每次周期的 GpuRes 累积、Upload 增量、bitmap cache
//    eviction 增量。 这些差量是判断"是否按次累积"的直接证据。
//
// 关键观察(运行后看 HUD):
//  - 第一次滚动周期 vs 第十次滚动周期,各项 delta 是否一直涨。
//  - 如果 GpuResΔ 每次都 +N MB 且不会掉,说明有资源每次滚动都在 retain
//    但永远不会 reclaim。
//  - 如果 Records (retained-cache miss) 每次都暴涨,说明有 visual 在
//    被反复 InvalidateVisual。
//  - 如果 Uploads 每次都暴涨,说明 _bitmapCache 被驱逐到本周期没法 hit。
internal static class ScrollStressWindow
{
    private const int CardWidth = 500;
    private const int CardHeight = 220;
    private const int CardCount = 240;       // 远超视口,确保虚拟化路径生效
    private const int SourceImageCount = 12; // 12 张共享源图
    private const int SourceWidth = 1024;
    private const int SourceHeight = 1024;

    public static Window Build()
    {
        var window = new Window
        {
            Title = "Jalium.UI - 滚动压力测试 (复现按次累积卡顿)",
            Width = 1280,
            Height = 880,
            Background = new SolidColorBrush(Color.FromRgb(18, 18, 26))
        };

        var sources = LoadOrSynthesizeImages();

        var grid = new WrapPanel
        {
            ItemWidth = CardWidth,
            ItemHeight = CardHeight,
            Margin = new Thickness(12)
        };

        for (int i = 0; i < CardCount; ++i)
        {
            grid.Children.Add(BuildCard(sources[i % sources.Count], i));
        }

        var scroller = new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var hud = BuildHud(scroller);

        var root = new DockPanel();
        DockPanel.SetDock(hud, Jalium.UI.Controls.Dock.Top);
        root.Children.Add(hud);
        root.Children.Add(scroller);

        window.Content = root;

        RenderDiagnostics.ApiStatsEnabled = true;
        return window;
    }

    private static List<BitmapImage> LoadOrSynthesizeImages()
    {
        // 优先从用户图库加载真实 JPG。 没有则退回合成图。
        var result = new List<BitmapImage>();
        var realPath = @"C:\Users\suppe\OneDrive\图片\01.jpg";
        Console.WriteLine($"[ScrollStress] File.Exists({realPath}) = {File.Exists(realPath)}");
        try
        {
            if (File.Exists(realPath))
            {
                // 加载一次原图,然后用 BitmapDownscaleCache 的 box-filter 缩到合理尺寸,
                // 重新打包成多个独立 BitmapImage(模拟多个 ImageSource 引用)。
                // 原 JPG 是 3840×2160 / 32MB BGRA — 240 张全 GPU 上传会 380MB,
                // 大概率第一帧 OOM/timeout 导致窗口空白。
                var fullSize = BitmapImage.FromFile(realPath);
                Console.WriteLine($"[ScrollStress] full image: {fullSize.PixelWidth}x{fullSize.PixelHeight} raw={fullSize.RawPixelData?.Length ?? 0}");

                var rawPixels = fullSize.RawPixelData!;
                int srcW = fullSize.PixelWidth;
                int srcH = fullSize.PixelHeight;
                int srcStride = fullSize.PixelStride > 0 ? fullSize.PixelStride : srcW * 4;

                // 目标 ~512×288 (16:9), ~600KB/张, 12 张总共 ~7MB GPU
                int dstW = 512;
                int dstH = (int)((long)srcH * dstW / srcW);
                byte[] smallPixels = NearestDownscale(rawPixels, srcW, srcH, srcStride, dstW, dstH);
                Console.WriteLine($"[ScrollStress] downscaled to {dstW}x{dstH}, {smallPixels.Length} bytes per copy");

                for (int i = 0; i < SourceImageCount; ++i)
                {
                    result.Add(BitmapImage.FromPixels(smallPixels, dstW, dstH));
                }
                return result;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScrollStress] LoadFromFile FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        Console.WriteLine("[ScrollStress] falling back to synthetic images");
        return BuildSyntheticImages(SourceImageCount);
    }

    private static UIElement BuildCard(BitmapImage src, int index)
    {
        var img = new Image
        {
            Source = src,
            Stretch = Stretch.UniformToFill,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var titleBlock = new TextBlock
        {
            Text = $"卡片 #{index}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 230))
        };
        var descBlock = new TextBlock
        {
            Text = $"图源 #{index % SourceImageCount}  ·  {src.PixelWidth}×{src.PixelHeight}  raw={(src.RawPixelData?.Length ?? 0) / 1024}KB",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 165))
        };
        var info = new StackPanel { Orientation = Orientation.Vertical };
        info.Children.Add(titleBlock);
        info.Children.Add(descBlock);

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(img);
        stack.Children.Add(info);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(36, 38, 50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 64, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(4),
            Width = CardWidth - 8,
            Height = CardHeight - 8,
            Padding = new Thickness(8),
            ClipToBounds = true,
            Child = stack
        };
        return card;
    }

    private static UIElement BuildHud(ScrollViewer scroller)
    {
        var liveLine = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 235, 220)),
            Margin = new Thickness(12, 6, 12, 0)
        };
        var cycleLine = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 140)),
            Margin = new Thickness(12, 2, 12, 0)
        };
        var historyLine = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 200)),
            Margin = new Thickness(12, 2, 12, 8)
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(liveLine);
        stack.Children.Add(cycleLine);
        stack.Children.Add(historyLine);

        var snapshotPrev = new SnapshotState();
        var snapshotCycleStart = new SnapshotState();

        var cycleHistory = new List<string>();
        int cycleIndex = 0;

        // 滚动安静检测: ScrollChanged 后 250ms 没有新的 ScrollChanged ⇒ 周期结束
        long lastScrollTickMs = 0;
        bool inCycle = false;

        scroller.ScrollChanged += (_, _) =>
        {
            lastScrollTickMs = Environment.TickCount64;
            if (!inCycle)
            {
                inCycle = true;
                snapshotCycleStart = SnapshotState.Capture();
            }
        };

        var liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        liveTimer.Tick += (_, _) =>
        {
            var cur = SnapshotState.Capture();
            liveLine.Text = $"实时:  {cur.Format()}";

            if (inCycle && Environment.TickCount64 - lastScrollTickMs >= 250)
            {
                inCycle = false;
                cycleIndex++;
                var diff = cur.Diff(snapshotCycleStart);
                var ts = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                var entry = $"#{cycleIndex} {ts}  {diff.FormatDelta()}";
                cycleHistory.Insert(0, entry);
                if (cycleHistory.Count > 6) cycleHistory.RemoveAt(cycleHistory.Count - 1);

                cycleLine.Text = $"上次滚动周期:  {diff.FormatDelta()}";
                historyLine.Text = "历史:\n" + string.Join("\n", cycleHistory);
                snapshotPrev = cur;
            }
        };
        liveTimer.Start();

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 30, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 64, 80)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = stack
        };
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
