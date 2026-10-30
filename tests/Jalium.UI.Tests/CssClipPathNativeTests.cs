using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssClipPathNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_ClipsPaintAndDescendants() => Verify(RenderBackend.Software, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ClipsPaintAndDescendants() => Verify(RenderBackend.D3D12, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_VelloClipsPaintAndDescendants() => Verify(RenderBackend.D3D12, RenderingEngine.Vello);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_ClipsPaintAndDescendants() => Verify(RenderBackend.Vulkan, RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_VelloClipsPaintAndDescendants() => Verify(RenderBackend.Vulkan, RenderingEngine.Vello);

    private static void Verify(RenderBackend backend, RenderingEngine engine)
    {
        const int width = 100, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var panel = new Canvas { Width = 40, Height = 40, Background = Brushes.Red };
        panel.Children.Add(new Border { Width = 80, Height = 80, Background = Brushes.Red });

        byte[] Paint(string style, bool recorded, bool applyStyle = true)
        {
            if (applyStyle) Css.SetStyle(panel, style);
            CssBorderRadiusTests.Layout(panel, 40, 40);
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(0, 0, 0, 1);
            if (recorded)
            {
                var recorder = new DrawingRecorder();
                recorder.BindWholeFrame();
                ((IOffsetDrawingContext)recorder).Offset = new Point(20, 20);
                panel.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(20, 20);
                try { panel.Render(drawing); }
                finally { drawing.Offset = default; }
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Red(byte[] pixels, int x, int y) => pixels[(y * width + x) * 4 + 2];
        static byte Blue(byte[] pixels, int x, int y) => pixels[(y * width + x) * 4];

        foreach (var recorded in new[] { false, true })
        {
            var circle = Paint("clip-path:circle(15px at 20px 20px)", recorded);
            Assert.True(Red(circle, 40, 40) > 245, $"{backend} recorded={recorded}: circle center missing");
            Assert.True(Red(circle, 27, 27) < 8, $"{backend} recorded={recorded}: AABB corner leaked");

            var outsideCenter = Paint("clip-path:circle(closest-side at 50px 20px)", recorded);
            Assert.True(Red(outsideCenter, 70, 40) > 245 && Red(outsideCenter, 55, 40) < 8,
                $"{backend} recorded={recorded}: off-box closest-side circle lost overflow paint");
            Assert.True(Red(circle, 70, 40) < 8, $"{backend} recorded={recorded}: descendant escaped");

            var inset = Paint("clip-path:inset(5px);overflow:clip", recorded);
            Assert.True(Red(inset, 40, 40) > 245, $"{backend} recorded={recorded}: inset center missing");
            Assert.True(Red(inset, 22, 40) < 8, $"{backend} recorded={recorded}: inset left leaked");
            Assert.True(Red(inset, 58, 40) < 8, $"{backend} recorded={recorded}: inset right leaked");

            var rounded = Paint("clip-path:inset(5px round 15px)", recorded);
            Assert.True(Red(rounded, 26, 26) < 8, $"{backend} recorded={recorded}: rounded inset corner leaked");
            Assert.True(Red(rounded, 40, 27) > 245, $"{backend} recorded={recorded}: rounded inset center missing");

            var xywh = Paint("clip-path:xywh(5px 5px 30px 30px)", recorded);
            Assert.True(Red(xywh, 40, 40) > 245, $"{backend} recorded={recorded}: xywh center missing");
            Assert.True(Red(xywh, 22, 40) < 8, $"{backend} recorded={recorded}: xywh left leaked");
            Assert.True(Red(xywh, 58, 40) < 8, $"{backend} recorded={recorded}: xywh right leaked");

            var filtered = Paint("clip-path:circle(15px at 20px 20px);filter:blur(3px)", recorded);
            Assert.True(Red(filtered, 40, 40) > 100, $"{backend} recorded={recorded}: clipped filter lost its center");
            Assert.True(Red(filtered, 27, 27) < 8, $"{backend} recorded={recorded}: filtered output escaped its circle");

            var shadow = Paint("clip-path:circle(15px at 20px 20px);box-shadow:12px 0 0 red", recorded);
            Assert.True(Red(shadow, 40, 40) > 245, $"{backend} recorded={recorded}: clipped box lost its center");
            Assert.True(Red(shadow, 60, 40) < 8, $"{backend} recorded={recorded}: box shadow escaped its circle");

            var outlined = Paint("clip-path:circle(15px at 20px 20px);outline:4px solid red", recorded);
            Assert.True(Red(outlined, 40, 40) > 245, $"{backend} recorded={recorded}: outlined box lost its center");
            Assert.True(Red(outlined, 19, 40) < 8, $"{backend} recorded={recorded}: outline escaped its circle");

            if (backend is RenderBackend.Software or RenderBackend.D3D12 or RenderBackend.Vulkan)
            {
                var polygon = Paint("clip-path:polygon(0 0, 100% 0, 50% 100%)", recorded);
                Assert.True(Red(polygon, 40, 30) > 245, $"{backend} recorded={recorded}: polygon center missing");
                Assert.True(Red(polygon, 24, 54) < 8, $"{backend} recorded={recorded}: polygon AABB corner leaked");
                Assert.True(Red(polygon, 70, 30) < 8, $"{backend} recorded={recorded}: polygon descendant escaped");

                var roundedPolygon = Paint(
                    "clip-path:polygon(round 10px, 0 0, 100% 0, 100% 100%, 0 100%)", recorded);
                Assert.True(Red(roundedPolygon, 40, 40) > 245,
                    $"{backend} recorded={recorded}: rounded polygon center missing");
                Assert.True(Red(roundedPolygon, 21, 21) < 8,
                    $"{backend} recorded={recorded}: rounded polygon corner leaked");

                const string star = "50% 0, 79% 90%, 3% 35%, 97% 35%, 21% 90%";
                var nonzero = Paint($"clip-path:polygon(nonzero, {star})", recorded);
                var evenodd = Paint($"clip-path:polygon(evenodd, {star})", recorded);
                Assert.True(Red(nonzero, 40, 40) > 245,
                    $"{backend} recorded={recorded}: nonzero star center missing");
                Assert.True(Red(evenodd, 40, 40) < 8,
                    $"{backend} recorded={recorded}: evenodd star center leaked");

                var filteredPolygon = Paint(
                    "clip-path:polygon(0 0, 100% 0, 50% 100%);filter:blur(3px)", recorded);
                Assert.True(Red(filteredPolygon, 40, 30) > 100,
                    $"{backend} recorded={recorded}: filtered polygon lost its center");
                Assert.True(Red(filteredPolygon, 24, 54) < 8,
                    $"{backend} recorded={recorded}: filtered polygon leaked");

                var child = (Border)panel.Children[0];
                child.Width = child.Height = 40;
                child.Background = Brushes.Blue;
                Css.SetStyle(child, "clip-path:polygon(0 0, 100% 0, 0 100%)");
                var nested = Paint("clip-path:polygon(0 0, 100% 0, 50% 100%)", recorded);
                Assert.True(Blue(nested, 40, 35) > 245,
                    $"{backend} recorded={recorded}: nested polygons lost their intersection");
                Assert.True(Blue(nested, 50, 35) < 8 && Red(nested, 50, 35) > 245,
                    $"{backend} recorded={recorded}: child polygon leaked inside parent");
                child.Width = child.Height = 80;
                child.Background = Brushes.Red;
                Css.SetStyle(child, "clip-path:none");
            }

            var path = Paint("clip-path:path('M0 0 H40 L20 40')", recorded);
            Assert.True(Red(path, 40, 30) > 245,
                $"{backend} recorded={recorded}: implicit path closure lost its interior");
            Assert.True(Red(path, 24, 54) < 8,
                $"{backend} recorded={recorded}: path leaked outside its contour");

            const string nestedPath = "M0 0 H40 V40 H0 Z M10 10 H30 V30 H10 Z";
            var nonzeroPath = Paint($"clip-path:path(nonzero, '{nestedPath}')", recorded);
            var evenoddPath = Paint($"clip-path:path(evenodd, '{nestedPath}')", recorded);
            Assert.True(Red(nonzeroPath, 40, 40) > 245,
                $"{backend} recorded={recorded}: nonzero path lost its center");
            Assert.True(Red(evenoddPath, 40, 40) < 8 && Red(evenoddPath, 25, 40) > 245,
                $"{backend} recorded={recorded}: evenodd path failed to preserve its hole");

            var arcPath = Paint(
                "clip-path:path('M20 0 A20 20 0 1 1 20 40 A20 20 0 1 1 20 0')", recorded);
            Assert.True(Red(arcPath, 40, 40) > 245 && Red(arcPath, 23, 23) < 8,
                $"{backend} recorded={recorded}: arc path contour was not clipped");

            var shape = Paint(
                "clip-path:shape(from 0 0, hline to 100%, line to 50% 100%)", recorded);
            Assert.True(Red(shape, 40, 30) > 245,
                $"{backend} recorded={recorded}: responsive shape lost its interior");
            Assert.True(Red(shape, 24, 54) < 8 && Red(shape, 70, 30) < 8,
                $"{backend} recorded={recorded}: shape leaked through its contour");

            const string nestedShape = "from 0 0, hline to 100%, vline to 100%, hline to 0, " +
                "close, move to 25% 25%, line by 50% 0, line by 0 50%, line by -50% 0";
            var evenoddShape = Paint($"clip-path:shape(evenodd {nestedShape})", recorded);
            Assert.True(Red(evenoddShape, 40, 40) < 8 && Red(evenoddShape, 25, 40) > 245,
                $"{backend} recorded={recorded}: shape fill rule failed to preserve its hole");

            var shapeArc = Paint(
                "clip-path:shape(from 20px 0, arc to 20px 40px of 20px cw, " +
                "arc to 20px 0 of 20px cw)", recorded);
            Assert.True(Red(shapeArc, 40, 40) > 245 && Red(shapeArc, 23, 23) < 8,
                $"{backend} recorded={recorded}: shape arc contour was not clipped");

            var shapeCurve = Paint(
                "clip-path:shape(from 0 40px, curve to 40px 40px with 20px -40px)", recorded);
            Assert.True(Red(shapeCurve, 40, 30) > 245 && Red(shapeCurve, 25, 25) < 8,
                $"{backend} recorded={recorded}: shape curve contour was not clipped");

            var clipFile = Path.Combine(Path.GetTempPath(), "jalium-native-clip-" + Guid.NewGuid() + ".svg");
            try
            {
                File.WriteAllText(clipFile, "<svg xmlns='http://www.w3.org/2000/svg'><defs>" +
                    "<path id='glyphGuide' d='M0 28 H40'/>" +
                    "<clipPath id='parts'><rect width='20' height='40'/>" +
                    "<rect x='25' width='15' height='40'/></clipPath>" +
                    "<clipPath id='overlap'><path d='M0 0 H30 V30 H0 Z'/>" +
                    "<path d='M40 0 V30 H10 V0 Z'/></clipPath>" +
                    "<clipPath id='hole'><path clip-rule='evenodd' " +
                    "d='M0 0 H40 V40 H0 Z M10 10 H30 V30 H10 Z'/>" +
                    "<rect x='15' y='15' width='10' height='10'/></clipPath>" +
                    "<clipPath id='letter'><text x='0' y='38' font-size='42' " +
                    "font-family='Segoe UI' fill='none'>O</text></clipPath>" +
                    "<clipPath id='length'><text x='0' y='28' font-size='25' " +
                    "font-family='Segoe UI' textLength='50' " +
                    "lengthAdjust='spacingAndGlyphs'>OO</text></clipPath>" +
                    "<clipPath id='textPath'><text font-size='25' " +
                    "font-family='Segoe UI'><textPath href='#glyphGuide' " +
                    "startOffset='5'>O</textPath></text></clipPath>" +
                    "<clipPath id='empty'/></defs></svg>");
                var source = new Uri(clipFile).AbsoluteUri;
                Css.SetStyle(panel, $"clip-path:url('{source}#parts')");
                var value = Assert.IsType<CssClipPathValue>(panel.GetValue(CssClipPathProperties.ValueProperty));
                value.UrlResource!.Ready.GetAwaiter().GetResult();
                var parts = Paint("", recorded, applyStyle: false);
                Assert.True(Red(parts, 30, 40) > 245 && Red(parts, 50, 40) > 245 &&
                    Red(parts, 42, 40) < 8 && Red(parts, 70, 40) < 8,
                    $"{backend} recorded={recorded}: SVG child contours were not clipped");

                Css.SetStyle(panel, $"clip-path:url('{source}#overlap')");
                value = Assert.IsType<CssClipPathValue>(panel.GetValue(CssClipPathProperties.ValueProperty));
                value.UrlResource!.Ready.GetAwaiter().GetResult();
                var overlap = Paint("", recorded, applyStyle: false);
                Assert.True(Red(overlap, 40, 35) > 245,
                    $"{backend} recorded={recorded}: opposite-winding overlap disappeared");

                Css.SetStyle(panel, $"clip-path:url('{source}#hole')");
                value = Assert.IsType<CssClipPathValue>(panel.GetValue(CssClipPathProperties.ValueProperty));
                value.UrlResource!.Ready.GetAwaiter().GetResult();
                var hole = Paint("", recorded, applyStyle: false);
                Assert.True(Red(hole, 25, 40) > 245 && Red(hole, 32, 40) < 8 &&
                    Red(hole, 40, 40) > 245,
                    $"{backend} recorded={recorded}: child fill rules were not united");

                Css.SetStyle(panel, $"clip-path:url('{source}#letter')");
                value = Assert.IsType<CssClipPathValue>(panel.GetValue(CssClipPathProperties.ValueProperty));
                value.UrlResource!.Ready.GetAwaiter().GetResult();
                var letterGeometry = Assert.IsType<PathGeometry>(CssClipPathProperties.GetGeometry(panel));
                var letterBounds = letterGeometry.Bounds;
                var letterY = (int)Math.Floor(letterBounds.Y + letterBounds.Height / 2);
                var counterX = (int)Math.Floor(letterBounds.X + letterBounds.Width / 2);
                var strokeX = Enumerable.Range(0, 40).First(x =>
                    letterGeometry.FillContains(new Point(x + .5, letterY + .5)));
                Assert.False(letterGeometry.FillContains(new Point(counterX + .5, letterY + .5)));
                var letter = Paint("", recorded, applyStyle: false);
                Assert.True(Red(letter, 20 + strokeX, 20 + letterY) > 120 &&
                    Red(letter, 20 + counterX, 20 + letterY) < 8,
                    $"{backend} recorded={recorded}: text glyph counter was not clipped");

                Css.SetStyle(panel, $"clip-path:url('{source}#length')");
                value = Assert.IsType<CssClipPathValue>(panel.GetValue(CssClipPathProperties.ValueProperty));
                value.UrlResource!.Ready.GetAwaiter().GetResult();
                var lengthGeometry = Assert.IsType<PathGeometry>(CssClipPathProperties.GetGeometry(panel));
                var lengthY = (int)Math.Floor(lengthGeometry.Bounds.Y + lengthGeometry.Bounds.Height / 2);
                var secondStrokeX = Enumerable.Range(25, 15).First(x =>
                    lengthGeometry.FillContains(new Point(x + .5, lengthY + .5)));
                var adjustedText = Paint("", recorded, applyStyle: false);
                Assert.True(Red(adjustedText, 20 + secondStrokeX, 20 + lengthY) > 120 &&
                    Red(adjustedText, 20 + 55, 20 + lengthY) < 8,
                    $"{backend} recorded={recorded}: adjusted SVG text was not clipped at its glyph contour");

                Css.SetStyle(panel, $"clip-path:url('{source}#textPath')");
                value = Assert.IsType<CssClipPathValue>(panel.GetValue(CssClipPathProperties.ValueProperty));
                value.UrlResource!.Ready.GetAwaiter().GetResult();
                var pathTextGeometry = Assert.IsType<PathGeometry>(CssClipPathProperties.GetGeometry(panel));
                var inside = (from y in Enumerable.Range(2, 36)
                              from x in Enumerable.Range(2, 36)
                              where pathTextGeometry.FillContains(new Point(x + .5, y + .5)) &&
                                    pathTextGeometry.FillContains(new Point(x - .5, y + .5)) &&
                                    pathTextGeometry.FillContains(new Point(x + 1.5, y + .5))
                              select new Point(x, y)).First();
                Assert.False(pathTextGeometry.FillContains(new Point(35.5, 35.5)));
                var pathText = Paint("", recorded, applyStyle: false);
                Assert.True(Red(pathText, 20 + (int)inside.X, 20 + (int)inside.Y) > 120 &&
                    Red(pathText, 55, 55) < 8,
                    $"{backend} recorded={recorded}: SVG textPath glyph was not clipped at its contour");

                Css.SetStyle(panel, $"clip-path:url('{source}#empty')");
                value = Assert.IsType<CssClipPathValue>(panel.GetValue(CssClipPathProperties.ValueProperty));
                value.UrlResource!.Ready.GetAwaiter().GetResult();
                var empty = Paint("", recorded, applyStyle: false);
                Assert.True(Red(empty, 40, 40) < 8,
                    $"{backend} recorded={recorded}: empty SVG clip did not hide paint");
            }
            finally { File.Delete(clipFile); }

            var none = Paint("clip-path:none", recorded);
            Assert.True(Red(none, 70, 40) > 245, $"{backend} recorded={recorded}: removal did not restore overflow");
        }

        using var bitmap = context.CreateBitmapFromPixels([0, 0, 255, 255], 1, 1);
        using var redBrush = context.CreateSolidBrush(1, 0, 0, 1);
        using var font = context.CreateTextFormat("Arial", 24);
        float[] triangleCommands = [0, 60, 20, 0, 40, 60, 0, 20, 20, 5];

        byte[] PrimitivePixels(bool text, bool clipped, bool translatedClip = false)
        {
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(0, 0, 0, 1);
            if (translatedClip) target.PushTransform([1, 0, 0, 1, 10, 0]);
            if (clipped)
                Assert.True(target.TryPushPathClip(20, 20, triangleCommands,
                    triangleCommands.Length, 1));
            if (translatedClip) target.PopTransform();
            if (text) target.DrawText("MMMMMMMM", font, 20, 20, 80, 40, redBrush);
            else target.DrawBitmap(bitmap, 20, 20, 40, 40);
            if (clipped) target.PopClip();
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        var clippedBitmap = PrimitivePixels(text: false, clipped: true);
        Assert.True(Red(clippedBitmap, 40, 30) > 245, $"{backend} {engine}: bitmap center missing");
        Assert.True(Red(clippedBitmap, 24, 54) < 8, $"{backend} {engine}: bitmap escaped path");

        var translatedBitmap = PrimitivePixels(text: false, clipped: true, translatedClip: true);
        Assert.True(Red(translatedBitmap, 50, 30) > 245,
            $"{backend} {engine}: transformed clip lost its center");
        Assert.True(Red(translatedBitmap, 30, 30) < 8,
            $"{backend} {engine}: path did not retain its push transform");

        var textReference = PrimitivePixels(text: true, clipped: false);
        var clippedText = PrimitivePixels(text: true, clipped: true);
        var referenceOutside = 0;
        var clippedOutside = 0;
        var clippedInside = 0;
        for (var y = 23; y < 45; y++)
        for (var x = 25; x < 61; x++)
        {
            var inside = x > 20 + (y - 20) / 2.0 + 2 &&
                x < 60 - (y - 20) / 2.0 - 2;
            if (inside)
            {
                if (Red(clippedText, x, y) > 32) clippedInside++;
            }
            else if (x < 20 + (y - 20) / 2.0 - 2 || x > 60 - (y - 20) / 2.0 + 2)
            {
                if (Red(textReference, x, y) > 32) referenceOutside++;
                if (Red(clippedText, x, y) > 32) clippedOutside++;
            }
        }
        Assert.True(referenceOutside > 0, $"{backend} {engine}: text did not cover the clip exterior");
        Assert.True(clippedInside > 0, $"{backend} {engine}: clipped text lost its interior");
        Assert.Equal(0, clippedOutside);
    }
}
