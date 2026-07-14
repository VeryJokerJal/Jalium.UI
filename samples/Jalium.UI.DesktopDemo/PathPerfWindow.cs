using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ShapePath = Jalium.UI.Shapes.Path;

namespace Jalium.UI.DesktopDemo;

// 临时 path 性能测试页：渲染一片复杂的 SVG icon（每个图标都是多 figure /
// 含曲线的复合 Geometry.Parse），并通过定时器在 0.5×–4.0× 之间往返缩放。
// 打开 DevTools (F3) → Perf tab，可以同时看到：
//   - Stroke / Fill / Geometry cache hit %
//   - Flatten 总时间、平均耗时、输出顶点数
//   - Triangulate 成功 / 失败次数
// 是验证后续每一项 path 优化是否生效的基准画面。
internal static class PathPerfWindow
{
    // Material Icons 的几个高复杂度 path data（含多 figure / cubic bezier）。
    // 用 strings 直接喂 Path.Data 触发 PathGeometry 路径，避免 StreamGeometry
    // fast path 提前消除 marshal 开销 — 我们要测的是 native flatten / cache。
    private static readonly string[] s_iconData =
    {
        // play_circle (single figure 圆 + 内嵌三角)
        "M12 2 C6.48 2 2 6.48 2 12 C2 17.52 6.48 22 12 22 C17.52 22 22 17.52 22 12 C22 6.48 17.52 2 12 2 Z M10 16.5 V7.5 L16 12 Z",
        // home (mixed line + line)
        "M10 20 V14 H14 V20 H19 V12 H22 L12 3 L2 12 H5 V20 H10 Z",
        // settings (多 figure：外齿轮 + 内圆)
        "M19.43 12.98 C19.47 12.66 19.5 12.34 19.5 12 C19.5 11.66 19.47 11.34 19.43 11.02 L21.54 9.37 C21.73 9.22 21.78 8.95 21.66 8.73 L19.66 5.27 C19.54 5.05 19.27 4.97 19.05 5.05 L16.56 6.05 C16.04 5.66 15.48 5.32 14.87 5.07 L14.49 2.42 C14.46 2.18 14.25 2 14 2 H10 C9.75 2 9.54 2.18 9.51 2.42 L9.13 5.07 C8.52 5.32 7.96 5.66 7.44 6.05 L4.95 5.05 C4.73 4.97 4.46 5.05 4.34 5.27 L2.34 8.73 C2.22 8.95 2.27 9.22 2.46 9.37 L4.57 11.02 C4.53 11.34 4.5 11.67 4.5 12 C4.5 12.33 4.53 12.66 4.57 12.98 L2.46 14.63 C2.27 14.78 2.22 15.05 2.34 15.27 L4.34 18.73 C4.46 18.95 4.73 19.03 4.95 18.95 L7.44 17.95 C7.96 18.34 8.52 18.68 9.13 18.93 L9.51 21.58 C9.54 21.82 9.75 22 10 22 H14 C14.25 22 14.46 21.82 14.49 21.58 L14.87 18.93 C15.48 18.68 16.04 18.34 16.56 17.95 L19.05 18.95 C19.27 19.03 19.54 18.95 19.66 18.73 L21.66 15.27 C21.78 15.05 21.73 14.78 21.54 14.63 L19.43 12.98 Z M12 15.5 C10.07 15.5 8.5 13.93 8.5 12 C8.5 10.07 10.07 8.5 12 8.5 C13.93 8.5 15.5 10.07 15.5 12 C15.5 13.93 13.93 15.5 12 15.5 Z",
        // account_circle (多 figure：外圆 + 内人形复合 path)
        "M12 2 C6.48 2 2 6.48 2 12 C2 17.52 6.48 22 12 22 C17.52 22 22 17.52 22 12 C22 6.48 17.52 2 12 2 Z M12 5 C13.66 5 15 6.34 15 8 C15 9.66 13.66 11 12 11 C10.34 11 9 9.66 9 8 C9 6.34 10.34 5 12 5 Z M12 19.2 C9.5 19.2 7.29 17.92 6 15.98 C6.03 13.99 10 12.9 12 12.9 C13.99 12.9 17.97 13.99 18 15.98 C16.71 17.92 14.5 19.2 12 19.2 Z",
        // menu (3 short lines — 简单 path 作 baseline)
        "M3 6 H21 V8 H3 Z M3 11 H21 V13 H3 Z M3 17 H21 V19 H3 Z",
    };

    private const int kRows = 6;
    private const int kCols = 10;
    private const double kMinScale = 0.5;
    private const double kMaxScale = 4.0;
    private const double kCyclePeriodSeconds = 6.0;

    public static Window Build()
    {
        var window = new Window
        {
            Title = "Jalium.UI - Path 性能测试",
            Width = 1024,
            Height = 768,
            Background = new SolidColorBrush(Color.FromRgb(20, 20, 28))
        };

        var statusText = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
            Margin = new Thickness(12, 8, 12, 8),
            Text = "Scale 0.5× → 4.0× 来回缓动。打开 DevTools (F3) Perf tab 可以看 path 指标。"
        };

        var grid = new UniformGrid
        {
            Rows = kRows,
            Columns = kCols,
            Margin = new Thickness(12)
        };

        // 多倍 icon 摆出来,触发 cache hit / miss + flatten 工作。
        // 同一段 PathData 多份 instance,可以让缓存看到大量 redundant work。
        var scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
        for (int i = 0; i < kRows * kCols; ++i)
        {
            var path = new ShapePath
            {
                Data = s_iconData[i % s_iconData.Length],
                Fill = new SolidColorBrush(Color.FromRgb(
                    (byte)(120 + (i * 7) % 100),
                    (byte)(140 + (i * 11) % 80),
                    (byte)(180 + (i * 13) % 60))),
                Stroke = new SolidColorBrush(Color.FromRgb(80, 100, 140)),
                StrokeThickness = 1.5,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4),
                RenderTransform = scaleTransform,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            grid.Children.Add(path);
        }

        var root = new DockPanel();
        DockPanel.SetDock(statusText, Jalium.UI.Controls.Dock.Top);
        root.Children.Add(statusText);
        root.Children.Add(grid);

        window.Content = root;

        // Telemetry 必须 opt-in 才会从 native 拉数据。
        RenderDiagnostics.ApiStatsEnabled = true;

        // 时间驱动 scale 在 [0.5, 4.0] 之间正弦往返。每 16ms tick → 60fps 步进。
        var animStart = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double elapsed = (DateTime.UtcNow - animStart).TotalSeconds;
            double phase = elapsed * (2 * Math.PI) / kCyclePeriodSeconds;
            double t = (Math.Sin(phase) + 1.0) * 0.5;          // 0..1
            double scale = kMinScale + (kMaxScale - kMinScale) * t;
            scaleTransform.ScaleX = scale;
            scaleTransform.ScaleY = scale;
            var st = RenderDiagnostics.LatestPathCacheStats;
            if (st != null)
            {
                long total = st.FillHits + st.FillMisses;
                double fillPct = total == 0 ? 0 : st.FillHits * 100.0 / total;
                long geomTotal = st.GeometryHits + st.GeometryMisses;
                double geomPct = geomTotal == 0 ? 0 : st.GeometryHits * 100.0 / geomTotal;
                statusText.Text =
                    $"Scale {scale:F2}×   Fill {fillPct:F0}%   Geom {geomPct:F0}%   " +
                    $"Flatten {st.FlattenNs / 1_000_000.0:F1}ms   verts {st.FlattenOutputVerts}";
            }
        };
        timer.Start();

        return window;
    }
}
