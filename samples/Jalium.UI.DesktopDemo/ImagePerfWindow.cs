using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.DesktopDemo;

// 临时 image 性能测试页:模拟 Jalium.One CreateSolutionView 的核心模式 —
// 一批高分辨率 BitmapImage 在网格中铺开,实际显示远小于源尺寸。
//
// 测试重点:
//   - BitmapDownscaleCache:每张源图 800×800 显示 168×190。首次画一帧后,
//     后续帧应该看到 GpuRes Δ ≈ 0,UploadCount 不再增长。
//   - 切分类 (Clear+Add):应触发"thumb cache 命中"而不是从头上传。
//   - Bitmap memcmp 短路 / Vulkan 同款 (engine 切换时验证)。
//
// 打开 DevTools (F3) Perf tab 看 "Bitmap upload (per frame)" 节。
internal static class ImagePerfWindow
{
    private const int CardWidth = 168;
    private const int CardHeight = 190;
    private const int CardsPerCategory = 31;

    public static Window Build()
    {
        var window = new Window
        {
            Title = "Jalium.UI - Image 性能测试",
            Width = 1100,
            Height = 800,
            Background = new SolidColorBrush(Color.FromRgb(20, 20, 28))
        };

        var statusText = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 220, 200)),
            Margin = new Thickness(12, 8, 12, 8),
            Text = "5 种 800×800 BitmapImage × 31 卡片,每张显示 168×190。打开 F3 看 GpuRes / Cache 指标。"
        };

        // 程序生成 5 张 800×800 BitmapImage(~2.4MB BGRA each,远大于 168×190 显示)
        // 这才能触发 BitmapDownscaleCache 收益(像素比 ~18.6×)
        var sources = BuildSyntheticImages(5);

        // 三个分类各 31 张,每次切换全量 Clear+Add 模拟 Jalium.One 用户场景
        var categoryA = BuildCategoryItems(sources, CardsPerCategory, seed: 0);
        var categoryB = BuildCategoryItems(sources, CardsPerCategory, seed: 1);
        var categoryC = BuildCategoryItems(sources, CardsPerCategory, seed: 2);

        // 直接 WrapPanel 作 cards 容器 — 简单可控。VirtualizingWrapPanel 的
        // 虚拟化效益用户可在 ItemsControl + DataTemplate 场景中验证(超出此 demo
        // 范围)。这里我们重点 demo BitmapDownscaleCache 的"上传一次复用多次"。
        var grid = new WrapPanel
        {
            ItemWidth = CardWidth,
            ItemHeight = CardHeight,
            Margin = new Thickness(12)
        };

        var scroller = new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        void Swap(List<BitmapImage> items)
        {
            grid.Children.Clear();
            foreach (var bmp in items)
            {
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(40, 45, 55)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(70, 80, 95)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(4),
                    Width = CardWidth - 8,
                    Height = CardHeight - 8,
                };
                var img = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(8)
                };
                card.Child = img;
                grid.Children.Add(card);
            }
        }

        Swap(categoryA);

        var btnA = MakeButton("分类 A", () => Swap(categoryA));
        var btnB = MakeButton("分类 B", () => Swap(categoryB));
        var btnC = MakeButton("分类 C", () => Swap(categoryC));
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8)
        };
        btnRow.Children.Add(btnA);
        btnRow.Children.Add(btnB);
        btnRow.Children.Add(btnC);

        var root = new DockPanel();
        DockPanel.SetDock(statusText, Jalium.UI.Controls.Dock.Top);
        DockPanel.SetDock(btnRow, Jalium.UI.Controls.Dock.Top);
        root.Children.Add(statusText);
        root.Children.Add(btnRow);
        root.Children.Add(scroller);

        window.Content = root;

        RenderDiagnostics.ApiStatsEnabled = true;

        // 状态条 2Hz 刷新 stats
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            var s = RenderDiagnostics.LatestBitmapUploadStats;
            if (s != null)
            {
                long total = s.UploadCount + s.FastPathHits;
                double fast = total == 0 ? 0 : s.FastPathHits * 100.0 / total;
                statusText.Text =
                    $"Uploads {s.UploadCount}  ({s.UploadBytes / 1024.0 / 1024.0:F1} MB)   " +
                    $"FastPath {fast:F0}%   Memcmp {s.MemcmpShortCircuits}   " +
                    $"GpuRes Δ{s.GpuResidentBytes / 1024.0 / 1024.0:+0.00;-0.00;0} MB   " +
                    $"CacheEvict {s.CacheEvictions}";
            }
        };
        timer.Start();

        return window;
    }

    private static List<BitmapImage> BuildSyntheticImages(int count)
    {
        var result = new List<BitmapImage>(count);
        for (int i = 0; i < count; ++i)
        {
            const int W = 800, H = 800;
            var pixels = new byte[W * H * 4];
            byte baseR = (byte)(80 + i * 30);
            byte baseG = (byte)(120 + i * 25);
            byte baseB = (byte)(160 + i * 20);
            for (int y = 0; y < H; ++y)
            {
                for (int x = 0; x < W; ++x)
                {
                    int o = (y * W + x) * 4;
                    pixels[o]     = (byte)((baseB + x / 4) & 0xff);
                    pixels[o + 1] = (byte)((baseG + y / 4) & 0xff);
                    pixels[o + 2] = (byte)((baseR + (x + y) / 8) & 0xff);
                    pixels[o + 3] = 255;
                }
            }
            result.Add(BitmapImage.FromPixels(pixels, W, H));
        }
        return result;
    }

    private static List<BitmapImage> BuildCategoryItems(List<BitmapImage> sources, int count, int seed)
    {
        var list = new List<BitmapImage>(count);
        for (int i = 0; i < count; ++i)
        {
            list.Add(sources[(i + seed) % sources.Count]);
        }
        return list;
    }

    private static Button MakeButton(string label, Action onClick)
    {
        var b = new Button
        {
            Margin = new Thickness(4),
            Padding = new Thickness(16, 6, 16, 6),
            Background = new SolidColorBrush(Color.FromRgb(60, 80, 100)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 100, 120)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Content = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 13
            }
        };
        b.Click += (_, _) => onClick();
        return b;
    }
}
