using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Pixel-level coverage for the native glyph-atlas paths. Both backends must
/// rasterize at the render-target DPI and keep fixed, axis-aligned text stable
/// within one physical-pixel rounding bucket.
/// </summary>
[Collection("Application")]
public sealed class GpuTextDpiRenderingTests
{
    private const int Width = 512;
    private const int Height = 160;
    private const string TestText = "HMWX 012345";

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_FixedAliasedText_TracksDpiAndSnapsToPhysicalPixels() =>
        AssertHighDpiTextContract(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_FixedAliasedText_TracksDpiAndSnapsToPhysicalPixels() =>
        AssertHighDpiTextContract(RenderBackend.Vulkan);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_AnisotropicSmallText_PreservesGlyphHeightAndStems() =>
        AssertAnisotropicSmallTextContract(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_AnisotropicSmallText_PreservesGlyphHeightAndStems() =>
        AssertAnisotropicSmallTextContract(RenderBackend.Vulkan);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_RotatedText_SnapsTheRunAsOneUnit() =>
        AssertRotatedTextUsesOnePixelPhase(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_RotatedText_SnapsTheRunAsOneUnit() =>
        AssertRotatedTextUsesOnePixelPhase(RenderBackend.Vulkan);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_StaticRotatedText_PreservesSharpCoreCoverage() =>
        AssertStaticRotatedTextSharpness(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_StaticRotatedText_PreservesSharpCoreCoverage() =>
        AssertStaticRotatedTextSharpness(RenderBackend.Vulkan);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_RotationTurnsTheGlyphOutlineItself() =>
        AssertRotationTurnsTheGlyphOutline(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_RotationTurnsTheGlyphOutlineItself() =>
        AssertRotationTurnsTheGlyphOutline(RenderBackend.Vulkan);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_AtlasGrowth_ReemitsGlyphsDroppedByFullInitialAtlas() =>
        AssertAtlasGrowthReemitsDroppedGlyphs(RenderBackend.Vulkan);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_AtlasGrowth_ReemitsGlyphsDroppedByFullInitialAtlas() =>
        AssertAtlasGrowthReemitsDroppedGlyphs(RenderBackend.D3D12);

    private static void AssertAtlasGrowthReemitsDroppedGlyphs(RenderBackend backend)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var blackBrush = context.CreateSolidBrush(0f, 0f, 0f, 1f);
        using var whiteBrush = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var sentinelFormat = context.CreateTextFormat("Consolas", 20f);

        var initialGlyphCapacity = 0;

        const string warmupText =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var warmupFormats = new List<NativeTextFormat>();
        var sawAtlasRetry = false;
        try
        {
            for (var size = 9; size <= 30; size++)
            {
                var format = context.CreateTextFormat("Segoe UI", size);
                format.SetTextRenderingMode(2);
                format.SetTextHintingMode(1);
                warmupFormats.Add(format);
            }

            sentinelFormat.SetTextRenderingMode(2);
            sentinelFormat.SetTextHintingMode(1);

            for (var frame = 0; frame < 4; frame++)
            {
                target.SetFullInvalidation();
                Assert.True(
                    TryBeginDrawWithRetry(target),
                    $"{target.Backend}: atlas-growth frame {frame} remained busy.");
                target.Clear(0f, 0f, 0f);
                foreach (var format in warmupFormats)
                {
                    // Opaque black still rasterizes and caches every glyph but
                    // remains invisible on the black capture background.
                    target.DrawText(
                        warmupText, format, 0f, 0f, Width, Height, blackBrush);

                    // Vulkan creates its atlas on the first text draw. Capture
                    // the actual initial capacity before any frame-boundary
                    // growth instead of assuming a backend's startup size.
                    if (initialGlyphCapacity == 0)
                    {
                        Assert.True(target.TryQueryGpuStats(out var initialStats));
                        initialGlyphCapacity = initialStats.GlyphSlotsTotal;
                        Assert.True(initialGlyphCapacity > 0,
                            $"{backend}: the first text draw did not initialize the glyph atlas.");
                    }
                }

                // This glyph is deliberately requested after the warm-up has
                // exhausted the initial atlas. It must be retried after growth,
                // not retained forever as an invalid cache entry.
                target.DrawText(
                    "@", sentinelFormat, 440f, 112f, 64f, 40f, whiteBrush);
                if (frame == 3)
                {
                    Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                }

                var endResult = target.TryEndDraw();
                if (backend == RenderBackend.Vulkan &&
                    endResult == JaliumResult.PresentFailed)
                {
                    sawAtlasRetry = true;
                }
                else
                {
                    Assert.Equal(JaliumResult.Ok, endResult);
                }

                if (frame == 1)
                {
                    Assert.True(target.TryQueryGpuStats(out var stats));
                    Assert.True(
                        stats.GlyphSlotsTotal > initialGlyphCapacity,
                        $"{backend}: stress run did not grow the initial atlas " +
                        $"(initial={initialGlyphCapacity}, capacity={stats.GlyphSlotsTotal}).");
                }
            }

            var pixels = new byte[Width * Height * 4];
            Assert.Equal(
                JaliumResult.Ok,
                target.FetchReadback(
                    pixels,
                    Width * 4u,
                    out var capturedWidth,
                    out var capturedHeight));
            Assert.Equal(Width, capturedWidth);
            Assert.Equal(Height, capturedHeight);

            var brightPixelCount = 0;
            for (var y = 108; y < Height; y++)
            {
                for (var x = 432; x < Width; x++)
                {
                    var offset = (y * Width + x) * 4;
                    if (Math.Max(
                            pixels[offset],
                            Math.Max(pixels[offset + 1], pixels[offset + 2])) > 16)
                    {
                        brightPixelCount++;
                    }
                }
            }

            Assert.True(
                brightPixelCount >= 8,
                $"{backend}: glyph requested after atlas overflow remained missing " +
                $"after growth ({brightPixelCount} bright pixels).");
            if (backend == RenderBackend.Vulkan)
            {
                Assert.True(
                    sawAtlasRetry,
                    "Vulkan: atlas overflow was presented without requesting a full retry.");
            }
        }
        finally
        {
            foreach (var format in warmupFormats)
            {
                format.Dispose();
            }
        }
    }

    private static void AssertAnisotropicSmallTextContract(RenderBackend backend)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var brush = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var format = context.CreateTextFormat("Microsoft YaHei UI", 14f);

        format.SetTextRenderingMode(2);
        format.SetTextHintingMode(1);
        float[] transform = [0.63f, 0f, 0f, 2.17f, 0f, 0f];
        var father = RenderCapture(target, brush, format, dpi: 96f, x: 24f, y: 6f,
            text: "父", transform: transform);
        var child = RenderCapture(target, brush, format, dpi: 96f, x: 24f, y: 6f,
            text: "子", transform: transform);
        var latinU = RenderCapture(target, brush, format, dpi: 96f, x: 24f, y: 6f,
            text: "U", transform: transform);
        var regularU = RenderCapture(target, brush, format, dpi: 96f, x: 24f, y: 6f,
            text: "U");

        Assert.True(father.Bounds.Height > 0 && child.Bounds.Height > 0,
            $"{backend}: transformed CJK glyphs produced no visible pixels.");
        Assert.InRange(
            Math.Abs(father.Bounds.Height - child.Bounds.Height),
            0,
            2);

        var (leftStem, rightStem) = MeasureUpperStemWidths(latinU);
        var (regularLeftStem, regularRightStem) = MeasureUpperStemWidths(regularU);
        const double minimumStemWidth = 1.25;
        Assert.True(leftStem >= minimumStemWidth && rightStem >= minimumStemWidth,
            $"{backend}: squeezed U stems collapsed " +
            $"(left={leftStem:F2}px, right={rightStem:F2}px).");
        Assert.True(
            Math.Min(leftStem, rightStem) / Math.Max(leftStem, rightStem) >= 0.8,
            $"{backend}: U stem coverage became asymmetric " +
            $"(left={leftStem:F2}px, right={rightStem:F2}px).");

        const double minimumRegularStemWidth = 1.35;
        Assert.True(
            regularLeftStem >= minimumRegularStemWidth &&
            regularRightStem >= minimumRegularStemWidth,
            $"{backend}: regular U stems rendered too lightly " +
            $"(left={regularLeftStem:F2}px, right={regularRightStem:F2}px).");
    }

    private static (double Left, double Right) MeasureUpperStemWidths(TextCapture capture)
    {
        var bounds = capture.Bounds;
        Assert.True(bounds.Width >= 4 && bounds.Height >= 4,
            $"U glyph bounds were too small to inspect ({bounds.Width}x{bounds.Height}).");

        // Use only the upper 60% of U, before its bottom curve joins the two
        // stems. Summed coverage / row count is the equivalent full-coverage
        // width in physical pixels, so a one-column stem measures about 1.0.
        var firstY = bounds.Y;
        var lastYExclusive = firstY + Math.Max(2, (int)Math.Floor(bounds.Height * 0.6));
        var middleX = bounds.X + bounds.Width / 2;
        var left = MeasureEquivalentInkWidth(
            capture.Pixels, bounds.X, middleX, firstY, lastYExclusive);
        var right = MeasureEquivalentInkWidth(
            capture.Pixels, middleX, bounds.X + bounds.Width, firstY, lastYExclusive);
        return (left, right);
    }

    private static double MeasureEquivalentInkWidth(
        byte[] pixels,
        int firstX,
        int lastXExclusive,
        int firstY,
        int lastYExclusive)
    {
        long intensitySum = 0;
        for (var y = firstY; y < lastYExclusive; y++)
        {
            for (var x = firstX; x < lastXExclusive; x++)
            {
                var offset = (y * Width + x) * 4;
                intensitySum += Math.Max(
                    pixels[offset],
                    Math.Max(pixels[offset + 1], pixels[offset + 2]));
            }
        }

        return intensitySum / (255.0 * (lastYExclusive - firstY));
    }

    private static void AssertHighDpiTextContract(RenderBackend backend)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var brush = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var format = context.CreateTextFormat("Segoe UI", 18f);

        Assert.Equal(backend, context.Backend);
        Assert.True(target.IsValid);
        Assert.True(brush.IsValid);
        Assert.True(format.IsValid);

        // Use the discrete display-text mode so the test exercises the fixed
        // path rather than an animation-oriented continuously positioned run.
        // The exact coverage values remain backend/DirectWrite dependent.
        format.SetTextRenderingMode(1); // TextRenderingMode.Aliased
        format.SetTextHintingMode(1);   // TextHintingMode.Fixed

        var dpi96 = RenderCapture(target, brush, format, dpi: 96f, x: 8.25f, y: 8.25f);
        var dpi144A = RenderCapture(target, brush, format, dpi: 144f, x: 8.10f, y: 8.10f);
        var dpi144B = RenderCapture(target, brush, format, dpi: 144f, x: 8.20f, y: 8.10f);
        var dpi192A = RenderCapture(target, brush, format, dpi: 192f, x: 8.10f, y: 8.10f);
        var dpi192B = RenderCapture(target, brush, format, dpi: 192f, x: 8.20f, y: 8.10f);

        Assert.True(dpi96.Bounds.Width > 0 && dpi96.Bounds.Height > 0,
            $"{backend}: 96-DPI text produced no visible pixels.");
        AssertScaleNear(backend, dpi96.Bounds, dpi144A.Bounds, dpi: 144, expectedScale: 1.5);
        AssertScaleNear(backend, dpi96.Bounds, dpi192A.Bounds, dpi: 192, expectedScale: 2.0);

        // Both origins fall into the same physical-pixel rounding bucket at
        // 150% and 200% DPI. A fixed display run must therefore be byte-
        // identical instead of drifting continuously between pixels.
        Assert.Equal(dpi144A.Pixels, dpi144B.Pixels);
        Assert.Equal(dpi192A.Pixels, dpi192B.Pixels);
    }

    private static void AssertScaleNear(
        RenderBackend backend,
        PixelBounds dpi96,
        PixelBounds scaled,
        int dpi,
        double expectedScale)
    {
        const double tolerance = 0.25;
        Assert.True(scaled.Width > dpi96.Width * (expectedScale - tolerance) &&
                    scaled.Width < dpi96.Width * (expectedScale + tolerance),
            $"{backend}: physical text width did not follow 96->{dpi} DPI " +
            $"({dpi96.Width}->{scaled.Width}).");
        Assert.True(scaled.Height > dpi96.Height * (expectedScale - tolerance) &&
                    scaled.Height < dpi96.Height * (expectedScale + tolerance),
            $"{backend}: physical text height did not follow 96->{dpi} DPI " +
            $"({dpi96.Height}->{scaled.Height}).");
    }

    private static TextCapture RenderCapture(
        RenderTarget target,
        NativeBrush brush,
        NativeTextFormat format,
        float dpi,
        float x,
        float y,
        string text = TestText,
        float[]? transform = null)
    {
        target.SetDpi(dpi, dpi);
        for (var frame = 0; frame < 2; frame++)
        {
            // Direct render-target tests do not have Window's dirty-region
            // coordinator. Force the same full repaint a DPI change requests
            // in production so Vulkan does not seed this frame from its
            // retained pre-DPI image.
            target.SetFullInvalidation();
            Assert.True(
                TryBeginDrawWithRetry(target),
                $"{target.Backend}: TryBeginDraw remained busy at dpi={dpi}, " +
                $"frame={frame}.");
            target.Clear(0f, 0f, 0f);
            if (transform is not null)
            {
                target.PushTransform(transform);
            }
            target.DrawText(text, format, x, y, 230f, 60f, brush);
            if (transform is not null)
            {
                target.PopTransform();
            }
            if (frame == 1)
            {
                Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            }

            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        }

        var pixels = new byte[Width * Height * 4];
        Assert.Equal(
            JaliumResult.Ok,
            target.FetchReadback(pixels, Width * 4u, out var capturedWidth, out var capturedHeight));
        Assert.Equal(Width, capturedWidth);
        Assert.Equal(Height, capturedHeight);

        var minX = Width;
        var minY = Height;
        var maxX = -1;
        var maxY = -1;
        for (var py = 0; py < Height; py++)
        {
            for (var px = 0; px < Width; px++)
            {
                var offset = (py * Width + px) * 4;
                var intensity = Math.Max(pixels[offset],
                    Math.Max(pixels[offset + 1], pixels[offset + 2]));
                if (intensity <= 2)
                {
                    continue;
                }

                minX = Math.Min(minX, px);
                minY = Math.Min(minY, py);
                maxX = Math.Max(maxX, px);
                maxY = Math.Max(maxY, py);
            }
        }

        var bounds = maxX >= minX && maxY >= minY
            ? new PixelBounds(minX, minY, maxX - minX + 1, maxY - minY + 1)
            : new PixelBounds(0, 0, 0, 0);
        return new TextCapture(pixels, bounds);
    }

    private static void AssertRotatedTextUsesOnePixelPhase(RenderBackend backend)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var brush = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var format = context.CreateTextFormat("Microsoft YaHei UI", 14f);

        format.SetTextRenderingMode(2);
        format.SetTextHintingMode(1);

        // Gallery repro: transform: rotate(-4deg) scale(1.05). Construct five
        // local origins whose transformed run origins all round to the same
        // physical pixel (30, 6). A run-level phase therefore produces five
        // byte-identical captures. Per-glyph AABB snapping does not: different
        // glyphs cross their own half-pixel boundary at different origins and
        // the rotated baseline turns into a staircase.
        const double angle = -4.0 * Math.PI / 180.0;
        const double scale = 1.05;
        var m11 = Math.Cos(angle) * scale;
        var m12 = Math.Sin(angle) * scale;
        var m21 = -Math.Sin(angle) * scale;
        var m22 = Math.Cos(angle) * scale;
        float[] transform =
        [
            (float)m11, (float)m12,
            (float)m21, (float)m22,
            0f, 0f,
        ];

        const float y = 8f;
        var transformedOriginFractions = new[] { 0.08, 0.18, 0.28, 0.38, 0.48 };
        var captures = transformedOriginFractions
            .Select(fraction =>
            {
                var transformedX = 30.0 + fraction;
                var localX = (transformedX - m21 * y) / m11;
                return RenderCapture(
                    target, brush, format, dpi: 96f,
                    x: (float)localX, y,
                    text: "transform 不影响布局 MWMW",
                    transform: transform);
            })
            .ToArray();

        Assert.True(captures[0].Bounds.Width > 0 && captures[0].Bounds.Height > 0,
            $"{backend}: rotated text produced no visible pixels.");
        for (var i = 1; i < captures.Length; i++)
        {
            Assert.Equal(captures[0].Pixels, captures[i].Pixels);
        }
    }

    private static void AssertStaticRotatedTextSharpness(RenderBackend backend)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var brush = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var staticFormat = context.CreateTextFormat("Microsoft YaHei UI", 14f);

        staticFormat.SetTextRenderingMode(2);
        // Leave TextHintingMode at Auto: this is the normal TextBlock/CSS path
        // from the Gallery repro.

        const double angle = -4.0 * Math.PI / 180.0;
        const double scale = 1.05;
        float[] transform =
        [
            (float)(Math.Cos(angle) * scale),
            (float)(Math.Sin(angle) * scale),
            (float)(-Math.Sin(angle) * scale),
            (float)(Math.Cos(angle) * scale),
            0f, 0f,
        ];

        var staticCapture = RenderCapture(target, brush, staticFormat, 96f,
            28.08f, 8f, "transform 不影响布局 MWMW", transform);
        var staticSharpness = MeasureTextSharpness(staticCapture);
        Assert.True(
            staticSharpness.CoreRatio >= 0.30,
            $"{backend}: static rotated text lost its solid stroke cores " +
            $"(core ratio={staticSharpness.CoreRatio:F4}).");
        Assert.True(
            staticSharpness.Variation >= 1.20,
            $"{backend}: static rotated text was still bilinear-softened " +
            $"(variation={staticSharpness.Variation:F4}).");
    }

    private static (double CoreRatio, double Variation) MeasureTextSharpness(TextCapture capture)
    {
        long ink = 0;
        long coreInk = 0;
        long variation = 0;
        var bounds = capture.Bounds;
        for (var y = bounds.Y; y < bounds.Y + bounds.Height; y++)
        {
            for (var x = bounds.X; x < bounds.X + bounds.Width; x++)
            {
                var intensity = PixelIntensity(capture.Pixels, x, y);
                ink += intensity;
                if (intensity >= 224)
                {
                    coreInk += intensity;
                }
                if (x + 1 < Width)
                {
                    variation += Math.Abs(intensity - PixelIntensity(capture.Pixels, x + 1, y));
                }
                if (y + 1 < Height)
                {
                    variation += Math.Abs(intensity - PixelIntensity(capture.Pixels, x, y + 1));
                }
            }
        }

        Assert.True(ink > 0, "Sharpness capture contained no ink.");
        return (coreInk / (double)ink, variation / (double)ink);
    }

    private static void AssertRotationTurnsTheGlyphOutline(RenderBackend backend)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var brush = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var format = context.CreateTextFormat("Consolas", 28f);

        format.SetTextRenderingMode(2);
        var upright = RenderCapture(target, brush, format, 96f,
            12f, 12f, "I");
        var rotated = RenderCapture(target, brush, format, 96f,
            12f, 12f, "I", [0f, 1f, -1f, 0f, 96f, 0f]);
        using var smallFormat = context.CreateTextFormat("Consolas", 14f);
        smallFormat.SetTextRenderingMode(2);
        const double smallAngle = -4.0 * Math.PI / 180.0;
        var smallRotated = RenderCapture(target, brush, smallFormat, 96f,
            24f, 12f, "I",
            [
                (float)Math.Cos(smallAngle), (float)Math.Sin(smallAngle),
                (float)-Math.Sin(smallAngle), (float)Math.Cos(smallAngle),
                0f, 0f,
            ]);

        Assert.True(upright.Bounds.Height > upright.Bounds.Width * 1.5,
            $"{backend}: upright probe glyph was not vertical " +
            $"({upright.Bounds.Width}x{upright.Bounds.Height}).");
        Assert.True(rotated.Bounds.Width > rotated.Bounds.Height * 1.5,
            $"{backend}: a 90-degree transform moved the glyph pen but left " +
            $"the glyph outline upright ({rotated.Bounds.Width}x{rotated.Bounds.Height}).");
        var smallAngleShear = MeasureVerticalStrokeShear(smallRotated);
        Assert.True(smallAngleShear > 0.05,
            $"{backend}: -4-degree glyph outline had no measurable clockwise " +
            $"shear (delta={smallAngleShear:F4}px).");
    }

    private static double MeasureVerticalStrokeShear(TextCapture capture)
    {
        var bounds = capture.Bounds;
        var bandHeight = Math.Max(1, bounds.Height / 3);
        static double Centroid(TextCapture c, int firstY, int lastY)
        {
            double weightedX = 0;
            double weight = 0;
            for (var y = firstY; y < lastY; y++)
            {
                for (var x = c.Bounds.X; x < c.Bounds.X + c.Bounds.Width; x++)
                {
                    var intensity = PixelIntensity(c.Pixels, x, y);
                    weightedX += x * intensity;
                    weight += intensity;
                }
            }
            return weight > 0 ? weightedX / weight : 0;
        }

        var top = Centroid(capture, bounds.Y, bounds.Y + bandHeight);
        var bottom = Centroid(capture,
            bounds.Y + bounds.Height - bandHeight,
            bounds.Y + bounds.Height);
        return bottom - top;
    }

    private static int PixelIntensity(byte[] pixels, int x, int y)
    {
        var offset = (y * Width + x) * 4;
        return Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2]));
    }

    private static bool TryBeginDrawWithRetry(RenderTarget target)
    {
        // TryBeginDraw deliberately reports transient swap-chain pressure
        // instead of blocking indefinitely. Window retries through its frame
        // scheduler; pixel tests need the same bounded contract when the full
        // GPU suite happens to miss a composition cycle.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            if (target.TryBeginDraw())
            {
                return true;
            }

            Thread.Sleep(1);
        }
        while (stopwatch.ElapsedMilliseconds < 250);

        return false;
    }

    private readonly record struct PixelBounds(int X, int Y, int Width, int Height);

    private readonly record struct TextCapture(byte[] Pixels, PixelBounds Bounds);
}
