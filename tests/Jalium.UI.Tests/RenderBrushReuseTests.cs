using System.Collections.ObjectModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Ink;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

/// <summary>
/// 渲染后端（<c>RenderTargetDrawingContext</c>）按 <see cref="Brush"/> <b>实例身份</b>缓存原生画刷，
/// 上限 256 条、超限时按 LRU 砍掉一半。在 OnRender 里每帧 new 一支画刷 = 每帧新增一条必然落空的
/// 条目，同时把别的控件的有效条目挤掉；命中后还会按内容（颜色/不透明度、渐变内容哈希）校验，
/// 所以「复用实例、只改颜色」是被完整支持的。
///
/// 这里改的全是绘制路径，所以每一处都要逐像素证明画面没变：复用实例渲染出来的结果，必须和
/// 一个全新实例第一次渲染（也就是改动前每帧的状态）逐字节相同。
/// </summary>
public class RenderBrushReuseTests
{
    private const int Bpp = 4;

    #region 光栅化与比对基础设施

    private static readonly Color CanvasBackground = Color.FromArgb(255, 0, 0, 0);

    private static byte[] Rasterize(Action<DrawingContext> draw, int width, int height, out int stride)
        => Rasterize(draw, width, height, CanvasBackground, out stride);

    private static byte[] Rasterize(
        Action<DrawingContext> draw, int width, int height, Color background, out int stride)
    {
        var visual = new DrawingVisual();
        var dc = visual.RenderOpen();
        try
        {
            draw(dc);
        }
        finally
        {
            dc.Close();
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32);
        target.Clear(background);
        target.Render(visual);

        stride = width * Bpp;
        var pixels = new byte[stride * height];
        target.CopyPixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        return pixels;
    }

    /// <summary>
    /// 防假绿：两张全黑图当然逐像素相等。任何一条比对都必须先证明画面上真的画了东西。
    /// </summary>
    private static void AssertNotBlank(byte[] pixels, string what)
        => AssertNotBlank(pixels, CanvasBackground, what);

    private static void AssertNotBlank(byte[] pixels, Color background, string what)
    {
        for (var i = 0; i < pixels.Length; i += Bpp)
        {
            if (pixels[i] != background.B || pixels[i + 1] != background.G || pixels[i + 2] != background.R)
                return;
        }

        Assert.Fail($"{what}：整张画面都是背景色，这条比对什么也没证明。");
    }

    private static void AssertSamePixels(
        byte[] expected, byte[] actual, int width, int height, int stride, string what)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * stride) + (x * Bpp);
                Assert.True(
                    actual[offset] == expected[offset] &&
                    actual[offset + 1] == expected[offset + 1] &&
                    actual[offset + 2] == expected[offset + 2] &&
                    actual[offset + 3] == expected[offset + 3],
                    $"{what}：({x},{y}) 处像素不一致——" +
                    $"复用实例 {actual[offset + 3]:X2}{actual[offset + 2]:X2}{actual[offset + 1]:X2}{actual[offset]:X2}，" +
                    $"全新实例 {expected[offset + 3]:X2}{expected[offset + 2]:X2}{expected[offset + 1]:X2}{expected[offset]:X2}");
            }
        }
    }

    /// <summary>
    /// 通用判据：<paramref name="warmUp"/> 先让被测实例把可复用的画刷/画笔建起来并跑一遍绘制，
    /// 之后那一次绘制的输出，必须和一个全新实例（等价于改动前每帧的状态）的首次绘制逐像素相同。
    /// </summary>
    private static void AssertReuseMatchesFreshInstance<T>(
        Func<T> factory,
        Action<T> warmUp,
        Action<T, DrawingContext> draw,
        int width,
        int height,
        string what,
        params string[] penCacheFields)
        where T : notnull
    {
        var reused = factory();
        warmUp(reused);
        var actual = Rasterize(dc => draw(reused, dc), width, height, out var stride);

        // 防假绿之二：如果这条绘制路径根本没跑到被改的那一行，缓存槽会是空的，
        // 「两次渲染一致」就什么也没证明。
        foreach (var field in penCacheFields)
        {
            Assert.True(
                ExtractPen(reused, field) != null,
                $"{what}：{field} 仍为空，说明这次绘制没走到被改的那条路径。");
        }

        var fresh = factory();
        var expected = Rasterize(dc => draw(fresh, dc), width, height, out _);

        AssertNotBlank(expected, what);
        AssertSamePixels(expected, actual, width, height, stride, what);
    }

    private static T? GetField<T>(object instance, string name) where T : class
    {
        var field = FindField(instance.GetType(), name);
        Assert.NotNull(field);
        return field!.GetValue(instance) as T;
    }

    private static object? GetFieldValue(object instance, string name)
    {
        var field = FindField(instance.GetType(), name);
        Assert.NotNull(field);
        return field!.GetValue(instance);
    }

    private static FieldInfo? FindField(Type? type, string name)
    {
        while (type != null)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
            type = type.BaseType;
        }

        return null;
    }

    private static void Invoke(object instance, string method, params object?[] args)
    {
        var mi = instance.GetType().GetMethod(
            method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(mi);
        mi!.Invoke(instance, args);
    }

    #endregion

    #region RenderPenCache 语义

    [Fact]
    public void RenderPenCache_KeepsOnePenUntilBrushOrThicknessChanges()
    {
        var cache = new RenderPenCache();
        var brushA = new SolidColorBrush(Color.FromRgb(10, 20, 30));
        var brushB = new SolidColorBrush(Color.FromRgb(40, 50, 60));

        var first = cache.Get(brushA, 1.0);
        Assert.Same(first, cache.Get(brushA, 1.0));
        Assert.Same(brushA, first.Brush);
        Assert.Equal(1.0, first.Thickness);

        var thicker = cache.Get(brushA, 2.0);
        Assert.NotSame(first, thicker);
        Assert.Equal(2.0, thicker.Thickness);

        var swapped = cache.Get(brushB, 2.0);
        Assert.NotSame(thicker, swapped);
        Assert.Same(brushB, swapped.Brush);

        // 同一支画刷改颜色不需要换画笔——渲染后端按实例身份命中后会自行按内容重建原生画刷。
        brushB.Color = Color.FromRgb(70, 80, 90);
        Assert.Same(swapped, cache.Get(brushB, 2.0));
    }

    [Fact]
    public void RenderPenCache_RebuildsWhenCachedPenIsFrozen()
    {
        var cache = new RenderPenCache();
        var brush = new SolidColorBrush(Color.FromRgb(10, 20, 30));

        var pen = cache.Get(brush, 1.0);
        pen.Freeze();

        var rebuilt = cache.Get(brush, 1.0);
        Assert.NotSame(pen, rebuilt);
        Assert.False(rebuilt.IsFrozen);
    }

    #endregion

    #region Stroke：湿笔迹每采样一个点就重建一次渲染缓存

    private static Stroke CreateStroke(BrushType brushType, Color color, bool varyPressure = false)
    {
        var points = new StylusPointCollection();
        for (var i = 0; i < 12; i++)
        {
            var pressure = varyPressure ? 0.2f + (i * 0.06f) : 0.5f;
            points.Add(new StylusPoint(12 + (i * 6), 30 + (i % 3 == 0 ? 8 : -6), pressure));
        }

        return new Stroke(points, new DrawingAttributes
        {
            Color = color,
            Width = 6,
            Height = 6,
            BrushType = brushType,
            IgnorePressure = !varyPressure,
        });
    }

    [Theory]
    [InlineData(BrushType.Round, false)]
    [InlineData(BrushType.Pen, false)]
    [InlineData(BrushType.Calligraphy, false)]
    // 有压感变化时走 BuildVariableWidthCache：椭圆链 + DrawingGroup 回退，两处都用同一支画刷。
    [InlineData(BrushType.Round, true)]
    public void Stroke_RecoloredReuse_MatchesFreshStroke(BrushType brushType, bool varyPressure)
    {
        var target = Color.FromArgb(255, 0x2E, 0x86, 0xC1);

        // 先用另一种颜色画一遍，把 _renderBrush/_renderPen 建起来，再改色重画——
        // 这正是拖动过程中「实例复用 + 改色」的状态。
        var reused = CreateStroke(brushType, Color.FromArgb(255, 0xC0, 0x39, 0x2B), varyPressure);
        Rasterize(reused.Draw, 120, 80, out _);
        reused.DrawingAttributes.Color = target;
        var actual = Rasterize(reused.Draw, 120, 80, out var stride);

        var fresh = CreateStroke(brushType, target, varyPressure);
        var expected = Rasterize(fresh.Draw, 120, 80, out _);

        var what = $"Stroke({brushType}, varyPressure={varyPressure})";
        AssertNotBlank(expected, what);
        AssertSamePixels(expected, actual, 120, 80, stride, what);
    }

    [Fact]
    public void Stroke_RepeatedRebuild_KeepsOneBrushAndPenInstance()
    {
        var stroke = CreateStroke(BrushType.Round, Color.FromArgb(255, 0xC0, 0x39, 0x2B));
        Rasterize(stroke.Draw, 120, 80, out _);

        var brush = GetField<SolidColorBrush>(stroke, "_renderBrush");
        var pen = GetField<Pen>(stroke, "_renderPen");
        Assert.NotNull(brush);
        Assert.NotNull(pen);

        // 追加采样点 = 缓存置脏 = 整份渲染缓存重建，这是拖动期间每帧都会发生的事。
        for (var i = 0; i < 5; i++)
        {
            stroke.StylusPoints.Add(new StylusPoint(90 + (i * 4), 50, 0.5f));
            Rasterize(stroke.Draw, 120, 80, out _);

            Assert.Same(brush, GetField<SolidColorBrush>(stroke, "_renderBrush"));
            Assert.Same(pen, GetField<Pen>(stroke, "_renderPen"));
        }

        // 复用的同时颜色必须真的跟上 DrawingAttributes。
        var newColor = Color.FromArgb(255, 0x11, 0x99, 0x44);
        stroke.DrawingAttributes.Color = newColor;
        Rasterize(stroke.Draw, 120, 80, out _);

        Assert.Same(brush, GetField<SolidColorBrush>(stroke, "_renderBrush"));
        Assert.Equal(newColor, brush!.Color);
        Assert.Same(brush, pen!.Brush);
    }

    /// <summary>
    /// DynamicRenderer 的湿笔迹预览走 TryGetDynamicRendererDrawing，每次指针移动重跑一次。
    /// </summary>
    [Fact]
    public void StrokeDynamicRendererDrawing_ReusesFillBrushAcrossPreviewFrames()
    {
        var stroke = CreateStroke(BrushType.Round, Color.FromArgb(255, 0xC0, 0x39, 0x2B), varyPressure: true);

        Assert.True(stroke.TryGetDynamicRendererDrawing(out var firstGeometry, out var firstBrush));
        Assert.NotNull(firstGeometry);

        // 追加采样点 = 预览的下一帧。
        stroke.StylusPoints.Add(new StylusPoint(90, 50, 0.9f));
        Assert.True(stroke.TryGetDynamicRendererDrawing(out var secondGeometry, out var secondBrush));

        Assert.Same(firstBrush, secondBrush);
        // 几何每帧都是新的一份，所以复用画刷不会让上一帧的图形改色。
        Assert.NotSame(firstGeometry, secondGeometry);

        var newColor = Color.FromArgb(255, 0x11, 0x99, 0x44);
        stroke.DrawingAttributes.Color = newColor;
        Assert.True(stroke.TryGetDynamicRendererDrawing(out _, out var recolored));
        Assert.Same(firstBrush, recolored);
        Assert.Equal(newColor, ((SolidColorBrush)recolored).Color);
    }

    [Fact]
    public void Stroke_FrozenRenderBrush_FallsBackToNewInstance()
    {
        var stroke = CreateStroke(BrushType.Round, Color.FromArgb(255, 0xC0, 0x39, 0x2B));
        Rasterize(stroke.Draw, 120, 80, out _);

        var frozen = GetField<SolidColorBrush>(stroke, "_renderBrush")!;
        frozen.Freeze();

        var newColor = Color.FromArgb(255, 0x11, 0x99, 0x44);
        stroke.DrawingAttributes.Color = newColor;
        var actual = Rasterize(stroke.Draw, 120, 80, out var stride);

        var replacement = GetField<SolidColorBrush>(stroke, "_renderBrush")!;
        Assert.NotSame(frozen, replacement);
        Assert.Equal(newColor, replacement.Color);

        var fresh = CreateStroke(BrushType.Round, newColor);
        var expected = Rasterize(fresh.Draw, 120, 80, out _);
        AssertNotBlank(expected, "Stroke(冻结回退)");
        AssertSamePixels(expected, actual, 120, 80, stride, "Stroke(冻结回退)");
    }

    #endregion

    #region InkCanvas 选择框：选择拖动期间每次指针移动都整块重画

    /// <summary>改动前的实现：每次绘制都新建 accent 画刷与画笔。</summary>
    private static void DrawSelectionAdornerReference(DrawingContext dc, Rect bounds)
    {
        var accent = new SolidColorBrush(Color.FromArgb(220, 30, 120, 220));
        dc.DrawRectangle(null, new Pen(accent, 1.0), bounds);

        const double offset = 5.0;
        const double size = 6.0;
        var centerX = bounds.X + (bounds.Width * 0.5);
        var centerY = bounds.Y + (bounds.Height * 0.5);

        void Handle(double cx, double cy) =>
            dc.DrawRectangle(accent, null, new Rect(cx - (size * 0.5), cy - (size * 0.5), size, size));

        Handle(bounds.Left - offset, bounds.Top - offset);
        Handle(centerX, bounds.Top - offset);
        Handle(bounds.Right + offset, bounds.Top - offset);
        Handle(bounds.Right + offset, centerY);
        Handle(bounds.Right + offset, bounds.Bottom + offset);
        Handle(centerX, bounds.Bottom + offset);
        Handle(bounds.Left - offset, bounds.Bottom + offset);
        Handle(bounds.Left - offset, centerY);
    }

    [Fact]
    public void InkCanvasSelectionAdorner_SharedBrush_MatchesPerFrameReference()
    {
        var bounds = new Rect(20, 16, 90, 60);
        const int width = 140;
        const int height = 100;

        var accentBrush = GetStaticField<SolidColorBrush>(typeof(InkCanvas), "s_selectionAccentBrush");
        var accentPen = GetStaticField<Pen>(typeof(InkCanvas), "s_selectionAccentPen");
        Assert.NotNull(accentBrush);
        Assert.NotNull(accentPen);
        Assert.Same(accentBrush, accentPen!.Brush);

        var actual = Rasterize(
            dc =>
            {
                dc.DrawRectangle(null, accentPen, bounds);
                const double offset = 5.0;
                const double size = 6.0;
                var centerX = bounds.X + (bounds.Width * 0.5);
                var centerY = bounds.Y + (bounds.Height * 0.5);
                void Handle(double cx, double cy) =>
                    dc.DrawRectangle(accentBrush, null, new Rect(cx - (size * 0.5), cy - (size * 0.5), size, size));
                Handle(bounds.Left - offset, bounds.Top - offset);
                Handle(centerX, bounds.Top - offset);
                Handle(bounds.Right + offset, bounds.Top - offset);
                Handle(bounds.Right + offset, centerY);
                Handle(bounds.Right + offset, bounds.Bottom + offset);
                Handle(centerX, bounds.Bottom + offset);
                Handle(bounds.Left - offset, bounds.Bottom + offset);
                Handle(bounds.Left - offset, centerY);
            },
            width, height, out var stride);

        var expected = Rasterize(dc => DrawSelectionAdornerReference(dc, bounds), width, height, out _);

        AssertNotBlank(expected, "InkCanvas 选择框");
        AssertSamePixels(expected, actual, width, height, stride, "InkCanvas 选择框");
    }

    private static T? GetStaticField<T>(Type type, string name) where T : class
    {
        var field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(null) as T;
    }

    #endregion

    #region GeographicHeatmap 图例：一次绘制原本新建 120 支画刷

    [Fact]
    public void GeographicHeatmapLegend_ReusedSliceBrushes_MatchFreshInstance()
    {
        const int width = 160;
        const int height = 200;
        var bounds = new Rect(0, 0, width, height);

        AssertReuseMatchesFreshInstance(
            () => new GeographicHeatmap(),
            heatmap => Rasterize(dc => Invoke(heatmap, "DrawLegend", dc, bounds), width, height, out _),
            (heatmap, dc) => Invoke(heatmap, "DrawLegend", dc, bounds),
            width, height,
            "GeographicHeatmap 图例");
    }

    [Fact]
    public void GeographicHeatmapLegend_KeepsOneBrushPerSliceAcrossRenders()
    {
        var heatmap = new GeographicHeatmap();
        var bounds = new Rect(0, 0, 160, 200);

        Rasterize(dc => Invoke(heatmap, "DrawLegend", dc, bounds), 160, 200, out _);
        var first = (SolidColorBrush[]?)GetFieldValue(heatmap, "_legendSliceBrushes");
        Assert.NotNull(first);
        Assert.NotEmpty(first!);

        var snapshot = (SolidColorBrush[])first!.Clone();
        Rasterize(dc => Invoke(heatmap, "DrawLegend", dc, bounds), 160, 200, out _);

        var second = (SolidColorBrush[]?)GetFieldValue(heatmap, "_legendSliceBrushes");
        Assert.Same(first, second);
        for (var i = 0; i < snapshot.Length; i++)
        {
            Assert.Same(snapshot[i], second![i]);
        }
    }

    #endregion

    #region MapView / MiniMap：平移与拖动期间每次指针移动都整块重画

    [Fact]
    public void MapViewMarkers_SharedOutlinePen_MatchesPerFrameReference()
    {
        const int width = 120;
        const int height = 90;
        var fill = new SolidColorBrush(Color.FromRgb(220, 50, 50));
        var outlineBrush = GetStaticField<SolidColorBrush>(typeof(MapView), "s_scaleBarBackground")!;
        var outlinePen = GetStaticField<Pen>(typeof(MapView), "s_markerOutlinePen")!;
        Assert.Same(outlineBrush, outlinePen.Brush);
        Assert.Equal(1.5, outlinePen.Thickness);

        var centers = new[] { new Point(24, 30), new Point(58, 52), new Point(92, 28) };

        var actual = Rasterize(
            dc =>
            {
                foreach (var c in centers)
                    dc.DrawEllipse(fill, outlinePen, c, 10, 10);
            },
            width, height, out var stride);

        var expected = Rasterize(
            dc =>
            {
                foreach (var c in centers)
                    dc.DrawEllipse(fill, new Pen(outlineBrush, 1.5), c, 10, 10);
            },
            width, height, out _);

        AssertNotBlank(expected, "MapView 标记描边");
        AssertSamePixels(expected, actual, width, height, stride, "MapView 标记描边");
    }

    [Fact]
    public void MapViewZoomControls_SharedBorderPen_MatchesPerFrameReference()
    {
        const int width = 80;
        const int height = 80;
        var background = GetStaticField<SolidColorBrush>(typeof(MapView), "s_zoomButtonBackground")!;
        var borderBrush = GetStaticField<SolidColorBrush>(typeof(MapView), "s_scaleBarBrush")!;
        var borderPen = GetStaticField<Pen>(typeof(MapView), "s_zoomButtonBorderPen")!;
        Assert.Same(borderBrush, borderPen.Brush);
        Assert.Equal(0.5, borderPen.Thickness);

        var rect = new Rect(10, 10, 28, 28);
        var actual = Rasterize(dc => dc.DrawRoundedRectangle(background, borderPen, rect, 4, 4), width, height, out var stride);
        var expected = Rasterize(
            dc => dc.DrawRoundedRectangle(background, new Pen(borderBrush, 0.5), rect, 4, 4), width, height, out _);

        AssertNotBlank(expected, "MapView 缩放按钮");
        AssertSamePixels(expected, actual, width, height, stride, "MapView 缩放按钮");
    }

    [Fact]
    public void MapView_PolylinePenCache_ReusesOnePenAcrossFramesAndPolylines()
    {
        const int width = 200;
        const int height = 160;
        var bounds = new Rect(0, 0, width, height);

        var map = new MapView { ZoomLevel = 6, Center = new GeoPoint(11, 21) };
        map.Measure(new Size(width, height));
        map.Arrange(bounds);

        // 三条折线共用同一支默认描边画刷 —— 改动前每条都要新建一支画笔，每帧重来。
        for (var i = 0; i < 3; i++)
        {
            var polyline = new MapPolyline
            {
                Points = new ObservableCollection<GeoPoint>
                {
                    new(10 + i, 20 + i),
                    new(11 + i, 22 + i),
                    new(12 + i, 21 + i),
                },
                StrokeThickness = 2,
            };
            map.Polylines.Add(polyline);
        }

        Rasterize(dc => Invoke(map, "DrawPolylines", dc, bounds), width, height, out _);
        var first = ExtractPen(map, "_polylinePen");
        Assert.True(first != null, "DrawPolylines 没走到 _polylinePen，这条测试什么也没证明。");

        Rasterize(dc => Invoke(map, "DrawPolylines", dc, bounds), width, height, out _);
        Assert.Same(first, ExtractPen(map, "_polylinePen"));

        // 换一支画刷必须换画笔，否则线会画错颜色。
        map.Polylines[1].Stroke = new SolidColorBrush(Color.FromRgb(0xE0, 0x30, 0x30));
        Rasterize(dc => Invoke(map, "DrawPolylines", dc, bounds), width, height, out _);
        Assert.NotSame(first, ExtractPen(map, "_polylinePen"));
    }

    [Fact]
    public void MapViewPolylines_ReusedPen_MatchesFreshInstancePerPixel()
    {
        const int width = 200;
        const int height = 160;
        var bounds = new Rect(0, 0, width, height);

        static MapView Build()
        {
            var map = new MapView { ZoomLevel = 6, Center = new GeoPoint(11, 21) };
            map.Measure(new Size(width, height));
            map.Arrange(new Rect(0, 0, width, height));
            for (var i = 0; i < 3; i++)
            {
                map.Polylines.Add(new MapPolyline
                {
                    Points = new ObservableCollection<GeoPoint>
                    {
                        new(10 + i, 20 + i),
                        new(11 + i, 22 + i),
                        new(12 + i, 21 + i),
                    },
                    StrokeThickness = 2,
                });
            }

            return map;
        }

        AssertReuseMatchesFreshInstance(
            Build,
            map => Rasterize(dc => Invoke(map, "DrawPolylines", dc, bounds), width, height, out _),
            (map, dc) => Invoke(map, "DrawPolylines", dc, bounds),
            width, height,
            "MapView 折线",
            "_polylinePen");
    }

    [Fact]
    public void MapViewBorder_ReusedPen_MatchesFreshInstance()
    {
        const int width = 160;
        const int height = 120;

        AssertReuseMatchesFreshInstance(
            () =>
            {
                var map = new MapView
                {
                    ZoomLevel = 6,
                    Center = new GeoPoint(11, 21),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xA0, 0x60)),
                    BorderThickness = new Thickness(2),
                    ShowAttribution = false,
                    ShowZoomControls = false,
                };
                map.Measure(new Size(width, height));
                map.Arrange(new Rect(0, 0, width, height));
                return map;
            },
            map => Rasterize(map.Render, width, height, out _),
            (map, dc) => map.Render(dc),
            width, height,
            "MapView 边框",
            "_chromeBorderPen");
    }

    [Fact]
    public void MiniMapChrome_ReusedBorderPen_MatchesFreshInstance()
    {
        const int width = 160;
        const int height = 120;

        AssertReuseMatchesFreshInstance(
            () =>
            {
                var map = new MiniMap();
                map.Measure(new Size(width, height));
                map.Arrange(new Rect(0, 0, width, height));
                return map;
            },
            map => Rasterize(map.Render, width, height, out _),
            (map, dc) => map.Render(dc),
            width, height,
            "MiniMap 边框",
            "_chromeBorderPen");
    }

    [Fact]
    public void MiniMapSimplifiedContent_SharedChildBrush_IsAStableInstance()
    {
        var childBrush = GetStaticField<SolidColorBrush>(typeof(MiniMap), "s_simplifiedChildBrush");
        var outlinePen = GetStaticField<Pen>(typeof(MiniMap), "s_outlineStrokePen");
        var outlineBrush = GetStaticField<SolidColorBrush>(typeof(MiniMap), "s_outlineStrokeBrush");

        Assert.NotNull(childBrush);
        Assert.Equal(Color.FromArgb(60, 100, 100, 100), childBrush!.Color);
        Assert.NotNull(outlinePen);
        Assert.Same(outlineBrush, outlinePen!.Brush);
        Assert.Equal(0.5, outlinePen.Thickness);
    }

    #endregion

    #region 文本类：光标闪烁定时器每个周期都触发一次整控件重绘

    [Fact]
    public void RichTextBoxCaret_FadeBrush_IsReusedAndRecolored()
    {
        var box = new RichTextBox();
        var caretBrush = new SolidColorBrush(Color.FromArgb(255, 0x20, 0x30, 0x40));

        var half = (SolidColorBrush)Fade(box, caretBrush, 0.5);
        Assert.NotSame(caretBrush, half);
        Assert.Equal((byte)(255 * 0.5), half.Color.A);
        Assert.Same(half, GetField<SolidColorBrush>(box, "_caretFadeBrush"));

        var quarter = (SolidColorBrush)Fade(box, caretBrush, 0.25);
        Assert.Same(half, quarter);
        Assert.Equal((byte)(255 * 0.25), quarter.Color.A);
        Assert.Equal(caretBrush.Color.R, quarter.Color.R);
        Assert.Equal(caretBrush.Color.G, quarter.Color.G);
        Assert.Equal(caretBrush.Color.B, quarter.Color.B);

        // 不透明时直接用原画刷，不该多建一支。
        Assert.Same(caretBrush, Fade(box, caretBrush, 1.0));

        // 冻结后必须退回新建，否则改色会抛。
        quarter.Freeze();
        var rebuilt = (SolidColorBrush)Fade(box, caretBrush, 0.5);
        Assert.NotSame(quarter, rebuilt);
        Assert.Equal((byte)(255 * 0.5), rebuilt.Color.A);
    }

    [Fact]
    public void RichTextBoxCaret_ReusedFadeBrush_MatchesFreshBrushPerPixel()
    {
        const int width = 40;
        const int height = 40;
        var caretRect = new Rect(12, 6, 2, 28);
        var caretBrush = new SolidColorBrush(Color.FromArgb(255, 0xE0, 0xE0, 0xE0));
        var canvas = Color.FromArgb(255, 0x20, 0x20, 0x20);

        var box = new RichTextBox();
        Fade(box, caretBrush, 0.75);
        Fade(box, caretBrush, 0.5);
        var reused = Fade(box, caretBrush, 0.375);

        var actual = Rasterize(dc => dc.DrawRectangle(reused, null, caretRect), width, height, canvas, out var stride);

        var color = caretBrush.Color;
        var freshBrush = new SolidColorBrush(
            Color.FromArgb((byte)(color.A * 0.375), color.R, color.G, color.B));
        var expected = Rasterize(
            dc => dc.DrawRectangle(freshBrush, null, caretRect), width, height, canvas, out _);

        AssertNotBlank(expected, canvas, "RichTextBox 光标淡入淡出");
        AssertSamePixels(expected, actual, width, height, stride, "RichTextBox 光标淡入淡出");
    }

    private static Brush Fade(RichTextBox box, Brush caretBrush, double opacity)
    {
        var mi = typeof(RichTextBox).GetMethod(
            "ApplyCaretFade", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(mi);
        return (Brush)mi!.Invoke(box, new object[] { caretBrush, opacity })!;
    }

    [Fact]
    public void RichTextBoxBorder_ReusedPen_MatchesFreshInstance()
    {
        const int width = 180;
        const int height = 60;

        AssertReuseMatchesFreshInstance(
            () =>
            {
                var box = new RichTextBox
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x80, 0xC0)),
                    BorderThickness = new Thickness(2),
                    Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
                };
                box.Measure(new Size(width, height));
                box.Arrange(new Rect(0, 0, width, height));
                return box;
            },
            box => Rasterize(box.Render, width, height, out _),
            (box, dc) => box.Render(dc),
            width, height,
            "RichTextBox 边框",
            "_borderPen");
    }

    [Fact]
    public void EditControlBorder_ReusedPen_MatchesFreshInstance()
    {
        const int width = 220;
        const int height = 80;

        AssertReuseMatchesFreshInstance(
            () =>
            {
                var edit = new EditControl
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x60, 0x20)),
                    BorderThickness = new Thickness(2),
                    Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
                };
                edit.Measure(new Size(width, height));
                edit.Arrange(new Rect(0, 0, width, height));
                return edit;
            },
            edit => Rasterize(edit.Render, width, height, out _),
            (edit, dc) => edit.Render(dc),
            width, height,
            "EditControl 边框",
            "_borderPen");
    }

    [Fact]
    public void EditControlSignatureHelp_SharedActiveParamPen_MatchesFreshPen()
    {
        var brush = GetStaticField<SolidColorBrush>(typeof(EditControl), "s_signatureActiveParamBrush")!;
        var pen = GetStaticField<Pen>(typeof(EditControl), "s_signatureActiveParamPen")!;
        Assert.Same(brush, pen.Brush);
        Assert.Equal(1.0, pen.Thickness);

        const int width = 120;
        const int height = 40;
        var rect = new Rect(8, 8, 100, 20);

        var actual = Rasterize(dc => dc.DrawRectangle(null, pen, rect), width, height, out var stride);
        var expected = Rasterize(dc => dc.DrawRectangle(null, new Pen(brush, 1), rect), width, height, out _);

        AssertNotBlank(expected, "EditControl 签名帮助高亮");
        AssertSamePixels(expected, actual, width, height, stride, "EditControl 签名帮助高亮");
    }

    #endregion

    #region MediaElement 字幕：视频播放时随每一帧重跑

    [Fact]
    public void MediaElementSubtitleOverlay_SharedBrushes_MatchPerFrameReference()
    {
        var foreground = GetStaticField<SolidColorBrush>(typeof(MediaElement), "s_subtitleForegroundBrush")!;
        var background = GetStaticField<SolidColorBrush>(typeof(MediaElement), "s_subtitleBackgroundBrush")!;
        var placeholder = GetStaticField<SolidColorBrush>(typeof(MediaElement), "s_placeholderTextBrush")!;

        Assert.Equal(Color.FromRgb(255, 255, 255), foreground.Color);
        Assert.Equal(Color.FromArgb(180, 0, 0, 0), background.Color);
        Assert.Equal(Color.FromRgb(200, 200, 200), placeholder.Color);

        const int width = 200;
        const int height = 80;
        var rect = new Rect(20, 40, 160, 28);

        // 字幕底是半透明黑，铺在黑底上依然是黑——必须换个非黑底才能证明这条比对有内容。
        var canvas = Color.FromArgb(255, 0x40, 0x80, 0xC0);
        var actual = Rasterize(
            dc => dc.DrawRectangle(background, null, rect), width, height, canvas, out var stride);
        var expected = Rasterize(
            dc => dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), null, rect),
            width, height, canvas, out _);

        AssertNotBlank(expected, canvas, "MediaElement 字幕底");
        AssertSamePixels(expected, actual, width, height, stride, "MediaElement 字幕底");
    }

    #endregion

    #region Docking

    [Fact]
    public void DockLayout_ReusedPens_MatchFreshInstance()
    {
        const int width = 180;
        const int height = 120;

        AssertReuseMatchesFreshInstance(
            () =>
            {
                var layout = new DockLayout();
                layout.Measure(new Size(width, height));
                layout.Arrange(new Rect(0, 0, width, height));
                return layout;
            },
            layout => Rasterize(layout.Render, width, height, out _),
            (layout, dc) => layout.Render(dc),
            width, height,
            "DockLayout 边框",
            "_borderPen");
    }

    [Fact]
    public void DockTabPanel_ReusedContentBorderPen_MatchesFreshInstance()
    {
        const int width = 240;
        const int height = 160;

        AssertReuseMatchesFreshInstance(
            () =>
            {
                var host = new DockTabPanelHost();
                // 只有非 Top 摆放才会走到 _contentBorderPen 那条分支：
                // Top 摆放另有一条把标签与内容区连成一体的几何路径，会提前 return。
                var panel = new DockTabPanel { TabStripPlacement = Dock.Bottom };
                panel.Items.Add(new DockItem { CanClose = false });
                panel.Items.Add(new DockItem { CanClose = false });
                panel.SelectedIndex = 0;
                host.Children.Add(panel);
                host.UpdateLayoutPass(new Size(width, height));
                return panel;
            },
            // Render 会一路跑到 OnPostRender，内容区边框正是在那里画的。
            panel => Rasterize(panel.Render, width, height, out _),
            (panel, dc) => panel.Render(dc),
            width, height,
            "DockTabPanel 内容区边框",
            "_contentBorderPen");
    }

    private sealed class DockTabPanelHost : Panel, ILayoutManagerHost
    {
        private readonly LayoutManager _layoutManager = new();

        LayoutManager ILayoutManagerHost.LayoutManager => _layoutManager;

        public void UpdateLayoutPass(Size availableSize) => _layoutManager.UpdateLayout(this, availableSize);

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (UIElement child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                    child.Measure(availableSize);
            }

            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (UIElement child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                    child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            }

            return finalSize;
        }
    }

    [Fact]
    public void DockLayout_BorderPenCache_KeepsOnePenWhileTheThemeBrushIsStable()
    {
        var layout = new DockLayout();
        layout.Measure(new Size(180, 120));
        layout.Arrange(new Rect(0, 0, 180, 120));

        Rasterize(layout.Render, 180, 120, out _);
        var first = ExtractPen(layout, "_borderPen");
        Assert.NotNull(first);

        Rasterize(layout.Render, 180, 120, out _);
        Assert.Same(first, ExtractPen(layout, "_borderPen"));
    }

    private static Pen? ExtractPen(object owner, string cacheFieldName)
    {
        var cache = GetFieldValue(owner, cacheFieldName);
        Assert.NotNull(cache);
        var penField = cache!.GetType().GetField("_pen", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(penField);
        return penField!.GetValue(cache) as Pen;
    }

    #endregion
}
