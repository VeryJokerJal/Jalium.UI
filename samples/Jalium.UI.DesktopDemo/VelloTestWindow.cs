using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using ShapePath = Jalium.UI.Shapes.Path;

namespace Jalium.UI.DesktopDemo;

// Vello 引擎静态验证场景：一屏覆盖 path 渲染特性矩阵（填充规则、曲线、描边
// join/cap/dash、线性/径向渐变 + spread、变换、半透明叠加），内容完全静态，
// 便于 Impeller / Vello 双引擎截图逐像素对比。
// 运行方式: JALIUM_DEMO_WINDOW=vellotest [JALIUM_RENDERING_ENGINE=vello]
internal static class VelloTestWindow
{
    private const string kStar =
        "M 60,4 L 73,44 L 116,44 L 81,68 L 94,108 L 60,84 L 26,108 L 39,68 L 4,44 L 47,44 Z";

    private const string kGear =
        "M19.43 12.98 C19.47 12.66 19.5 12.34 19.5 12 C19.5 11.66 19.47 11.34 19.43 11.02 L21.54 9.37 C21.73 9.22 21.78 8.95 21.66 8.73 L19.66 5.27 C19.54 5.05 19.27 4.97 19.05 5.05 L16.56 6.05 C16.04 5.66 15.48 5.32 14.87 5.07 L14.49 2.42 C14.46 2.18 14.25 2 14 2 H10 C9.75 2 9.54 2.18 9.51 2.42 L9.13 5.07 C8.52 5.32 7.96 5.66 7.44 6.05 L4.95 5.05 C4.73 4.97 4.46 5.05 4.34 5.27 L2.34 8.73 C2.22 8.95 2.27 9.22 2.46 9.37 L4.57 11.02 C4.53 11.34 4.5 11.67 4.5 12 C4.5 12.33 4.53 12.66 4.57 12.98 L2.46 14.63 C2.27 14.78 2.22 15.05 2.34 15.27 L4.34 18.73 C4.46 18.95 4.73 19.03 4.95 18.95 L7.44 17.95 C7.96 18.34 8.52 18.68 9.13 18.93 L9.51 21.58 C9.54 21.82 9.75 22 10 22 H14 C14.25 22 14.46 21.82 14.49 21.58 L14.87 18.93 C15.48 18.68 16.04 18.34 16.56 17.95 L19.05 18.95 C19.27 19.03 19.54 18.95 19.66 18.73 L21.66 15.27 C21.78 15.05 21.73 14.78 21.54 14.63 L19.43 12.98 Z M12 15.5 C10.07 15.5 8.5 13.93 8.5 12 C8.5 10.07 10.07 8.5 12 8.5 C13.93 8.5 15.5 10.07 15.5 12 C15.5 13.93 13.93 15.5 12 15.5 Z";

    private const string kZigzag = "M 0,60 L 30,10 L 60,60 L 90,10 L 120,60";

    private const string kSCurve = "M 5,70 C 45,-20 75,140 115,30";

    public static Window Build()
    {
        var window = new Window
        {
            Title = "Vello 0.10 验证",
            Width = 1040,
            Height = 720,
            Background = new SolidColorBrush(Color.FromRgb(24, 26, 32))
        };

        var canvas = new Canvas();

        void Place(UIElement el, double x, double y)
        {
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            canvas.Children.Add(el);
        }

        // ── 行 1: 填充 ──────────────────────────────────────────────
        // 1a. 自相交五角星 EvenOdd（中心镂空）
        Place(new ShapePath
        {
            Data = Geometry.Parse(kStar),
            Fill = new SolidColorBrush(Color.FromRgb(255, 200, 40)),
        }, 30, 30);

        // 1b. 同星形 NonZero（中心填实）
        var starNz = Geometry.Parse(kStar);
        if (starNz is PathGeometry pg) pg.FillRule = FillRule.Nonzero;
        Place(new ShapePath
        {
            Data = starNz,
            Fill = new SolidColorBrush(Color.FromRgb(240, 120, 40)),
        }, 180, 30);

        // 1c. 多 figure 齿轮（cubic 曲线密集），放大 5x
        Place(new ShapePath
        {
            Data = Geometry.Parse(kGear),
            Fill = new SolidColorBrush(Color.FromRgb(120, 200, 255)),
            RenderTransform = new ScaleTransform(5.0, 5.0),
        }, 330, 30);

        // 1d. 旋转 30° 的齿轮（GPU 变换路径）
        Place(new ShapePath
        {
            Data = Geometry.Parse(kGear),
            Fill = new SolidColorBrush(Color.FromRgb(170, 255, 170)),
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(4.0, 4.0),
                    new RotateTransform(30, 48, 48),
                }
            },
        }, 490, 40);

        // 1e. 半透明叠加双圆（premul 混合正确性）
        Place(new ShapePath
        {
            Data = new EllipseGeometry(new Point(45, 45), 45, 45),
            Fill = new SolidColorBrush(Color.FromArgb(150, 255, 60, 60)),
        }, 640, 30);
        Place(new ShapePath
        {
            Data = new EllipseGeometry(new Point(45, 45), 45, 45),
            Fill = new SolidColorBrush(Color.FromArgb(150, 60, 60, 255)),
        }, 690, 60);

        // 1f. 大圆环 (EvenOdd 双圆)
        Place(new ShapePath
        {
            Data = Geometry.Parse(
                "M 60,0 A 60,60 0 1 0 60,120 A 60,60 0 1 0 60,0 Z " +
                "M 60,25 A 35,35 0 1 1 60,95 A 35,35 0 1 1 60,25 Z"),
            Fill = new SolidColorBrush(Color.FromRgb(220, 160, 255)),
        }, 830, 30);

        // ── 行 2: 描边 ──────────────────────────────────────────────
        // 2a. 锯齿 miter join / 平头 cap，宽 10
        Place(new ShapePath
        {
            Data = Geometry.Parse(kZigzag),
            Stroke = new SolidColorBrush(Color.FromRgb(255, 120, 160)),
            StrokeThickness = 10,
            StrokeLineJoin = PenLineJoin.Miter,
        }, 30, 210);

        // 2b. 锯齿 round join + round cap
        Place(new ShapePath
        {
            Data = Geometry.Parse(kZigzag),
            Stroke = new SolidColorBrush(Color.FromRgb(120, 255, 200)),
            StrokeThickness = 10,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        }, 190, 210);

        // 2c. 锯齿 bevel join + square cap
        Place(new ShapePath
        {
            Data = Geometry.Parse(kZigzag),
            Stroke = new SolidColorBrush(Color.FromRgb(255, 220, 120)),
            StrokeThickness = 10,
            StrokeLineJoin = PenLineJoin.Bevel,
            StrokeStartLineCap = PenLineCap.Square,
            StrokeEndLineCap = PenLineCap.Square,
        }, 350, 210);

        // 2d. S 曲线描边（GPU Euler 描边扩展）
        Place(new ShapePath
        {
            Data = Geometry.Parse(kSCurve),
            Stroke = new SolidColorBrush(Color.FromRgb(140, 180, 255)),
            StrokeThickness = 6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        }, 510, 210);

        // 2e. 虚线圆角矩形
        Place(new ShapePath
        {
            Data = new RectangleGeometry(new Rect(0, 0, 130, 80), 16, 16),
            Stroke = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            StrokeThickness = 4,
            StrokeDashArray = new DoubleCollection { 4, 2 },
        }, 660, 215);

        // 2f. 闭合三角形粗描边（closed-subpath join 全覆盖）
        Place(new ShapePath
        {
            Data = Geometry.Parse("M 60,0 L 120,100 L 0,100 Z"),
            Stroke = new SolidColorBrush(Color.FromRgb(200, 255, 120)),
            StrokeThickness = 12,
            StrokeLineJoin = PenLineJoin.Miter,
        }, 840, 205);

        // ── 行 3: 渐变 ──────────────────────────────────────────────
        // 3a. 线性渐变 Pad
        Place(MakeGradRect(GradientSpreadMethod.Pad, 0.0, 1.0), 30, 380);

        // 3b. 线性渐变 Repeat（短跨度重复）
        Place(MakeGradRect(GradientSpreadMethod.Repeat, 0.30, 0.45), 190, 380);

        // 3c. 线性渐变 Reflect
        Place(MakeGradRect(GradientSpreadMethod.Reflect, 0.35, 0.55), 350, 380);

        // 3d. 径向渐变（同心）
        Place(new ShapePath
        {
            Data = new EllipseGeometry(new Point(60, 60), 60, 60),
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(255, 255, 255), 0.0),
                    new GradientStop(Color.FromRgb(30, 120, 255), 0.7),
                    new GradientStop(Color.FromRgb(10, 20, 90), 1.0),
                }
            },
        }, 510, 380);

        // 3e. 径向渐变（focal 偏移 -> 两点圆锥）
        Place(new ShapePath
        {
            Data = new EllipseGeometry(new Point(60, 60), 60, 60),
            Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.30, 0.30),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(255, 250, 200), 0.0),
                    new GradientStop(Color.FromRgb(255, 130, 40), 0.6),
                    new GradientStop(Color.FromRgb(120, 30, 10), 1.0),
                }
            },
        }, 660, 380);

        // 3f. 径向 Reflect 环纹
        Place(new ShapePath
        {
            Data = new EllipseGeometry(new Point(60, 60), 60, 60),
            Fill = new RadialGradientBrush
            {
                RadiusX = 0.18,
                RadiusY = 0.18,
                SpreadMethod = GradientSpreadMethod.Reflect,
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(20, 220, 180), 0.0),
                    new GradientStop(Color.FromRgb(10, 40, 60), 1.0),
                }
            },
        }, 810, 380);

        // ── 行 4: 小图形 / 细线 ─────────────────────────────────────
        // 4a. 1px 细描边网格线
        for (int i = 0; i < 6; i++)
        {
            Place(new ShapePath
            {
                Data = Geometry.Parse($"M 0,{i * 12} L 120,{i * 12 + 6}"),
                Stroke = new SolidColorBrush(Color.FromRgb(160, 170, 190)),
                StrokeThickness = 1,
            }, 30, 545);
        }

        // 4b. 一排小号 play 图标（小 path 批量）
        for (int i = 0; i < 8; i++)
        {
            Place(new ShapePath
            {
                Data = Geometry.Parse(
                    "M12 2 C6.48 2 2 6.48 2 12 C2 17.52 6.48 22 12 22 C17.52 22 22 17.52 22 12 C22 6.48 17.52 2 12 2 Z M10 16.5 V7.5 L16 12 Z"),
                Fill = new SolidColorBrush(Color.FromRgb((byte)(90 + i * 20), 200, (byte)(255 - i * 20))),
                RenderTransform = new ScaleTransform(2.0, 2.0),
            }, 190 + i * 56, 545);
        }

        // 4c. 极细长三角（数值鲁棒性）
        Place(new ShapePath
        {
            Data = Geometry.Parse("M 0,0 L 320,3 L 0,6 Z"),
            Fill = new SolidColorBrush(Color.FromRgb(255, 90, 90)),
        }, 660, 620);

        window.Content = canvas;
        return window;
    }

    private static ShapePath MakeGradRect(GradientSpreadMethod spread, double start, double end)
    {
        return new ShapePath
        {
            Data = new RectangleGeometry(new Rect(0, 0, 130, 120)),
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(start, 0),
                EndPoint = new Point(end, 0),
                SpreadMethod = spread,
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(255, 80, 120), 0.0),
                    new GradientStop(Color.FromRgb(80, 120, 255), 1.0),
                }
            },
        };
    }
}
