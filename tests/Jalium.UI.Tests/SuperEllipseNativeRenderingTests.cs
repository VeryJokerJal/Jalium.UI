using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class SuperEllipseNativeRenderingTests
{
    private const int Width = 176;
    private const int Height = 128;
    private static readonly float[] s_themeGradientStops =
    [
        0f, 0.00f, 0.35f, 1.00f, 1f,
        1f, 0.05f, 0.95f, 1.00f, 1f
    ];


    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Fill_UsesLocalContinuousCorners()
    {
        AssertLocalCornersAndExponent(RenderBackend.D3D12);
    }

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Fill_UsesLocalContinuousCorners()
    {
        AssertLocalCornersAndExponent(RenderBackend.Vulkan);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_Stroke_IsSymmetricAndAnalyticallyAntialiased()
    {
        AssertAnalyticStroke(RenderBackend.D3D12);
    }

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_Stroke_IsSymmetricAndAnalyticallyAntialiased()
    {
        AssertAnalyticStroke(RenderBackend.Vulkan);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_RoundedRectangleStroke_IsSymmetricAndConstantWidth()
    {
        AssertAnalyticStroke(
            RenderBackend.D3D12,
            continuousCorners: false);
    }

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_RoundedRectangleStroke_IsSymmetricAndConstantWidth()
    {
        AssertAnalyticStroke(
            RenderBackend.Vulkan,
            continuousCorners: false);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_FilledRoundedBorder_HasNoInnerCornerSeam()
    {
        AssertFilledRoundedBorderMatchesSolidSilhouette(RenderBackend.D3D12);
    }

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_FilledRoundedBorder_HasNoInnerCornerSeam()
    {
        AssertFilledRoundedBorderMatchesSolidSilhouette(RenderBackend.Vulkan);
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_GradientFillAndStroke_UseLocalContinuousCorners()
    {
        AssertGradientFillAndStroke(RenderBackend.D3D12);
    }

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_GradientFillAndStroke_UseLocalContinuousCorners()
    {
        AssertGradientFillAndStroke(RenderBackend.Vulkan);
    }


    private static void AssertLocalCornersAndExponent(RenderBackend backend)
    {
        var pixels = Render(backend, (target, brush) =>
        {
            target.SetShapeType(type: 1, n: 4f);
            target.FillPerCornerRoundedRectangle(
                16.25f, 16.25f, 144f, 36f,
                10f, 10f, 10f, 10f,
                brush);
            target.FillPerCornerRoundedRectangle(
                12f, 72f, 64f, 40f,
                4f, 4f, 4f, 4f,
                brush);
            target.FillPerCornerRoundedRectangle(
                100f, 72f, 64f, 40f,
                16f, 16f, 16f, 16f,
                brush);
            target.SetShapeType(type: 0, n: 4f);
        });

        // Regression for the 440x36 Gallery search field: once past the 10px
        // corner patch, the top edge must be straight instead of bowing across
        // the complete control width.
        Assert.True(GetBlue(pixels, 30, 16) >= 150);
        Assert.True(GetBlue(pixels, 16, 16) <= 20);
        Assert.True(GetBlue(pixels, 88, 34) >= 250);

        // CornerRadius is geometry, not a transport-only value. At the same
        // 1.5px corner offset a radius-4 patch contains the pixel, while a
        // radius-16 patch still excludes it.
        Assert.True(GetBlue(pixels, 13, 73) >= 200);
        Assert.True(GetBlue(pixels, 101, 73) <= 80);

        var exponentPixels = Render(backend, (target, brush) =>
        {
            target.SetShapeType(type: 1, n: 2f);
            target.FillPerCornerRoundedRectangle(
                12f, 40f, 64f, 40f,
                16f, 16f, 16f, 16f,
                brush);
            target.SetShapeType(type: 1, n: 4f);
            target.FillPerCornerRoundedRectangle(
                100f, 40f, 64f, 40f,
                16f, 16f, 16f, 16f,
                brush);
            target.SetShapeType(type: 0, n: 4f);
        });

        // Local normalized corner coordinate is approximately (0.78, 0.78):
        // outside n=2 but inside n=4.
        Assert.True(GetBlue(exponentPixels, 15, 43) <= 80);
        Assert.True(GetBlue(exponentPixels, 103, 43) >= 200);

        var partialCoveragePixels = CountPartialCoverage(
            pixels, left: 9, top: 13, right: 167, bottom: 59);
        Assert.True(
            partialCoveragePixels >= 30,
            $"{backend} local continuous corner has a hard edge: " +
            $"partialCoveragePixels={partialCoveragePixels}.");
    }

    private static void AssertAnalyticStroke(
        RenderBackend backend,
        bool continuousCorners = true)
    {
        var pixels = Render(backend, (target, brush) =>
        {
            target.SetShapeType(type: continuousCorners ? 1 : 0, n: 4f);
            target.DrawPerCornerRoundedRectangle(
                x: 16f,
                y: 28f,
                width: 144f,
                height: 72f,
                tl: 24f,
                tr: 24f,
                br: 24f,
                bl: 24f,
                brush,
                strokeWidth: 1f);
            target.SetShapeType(type: 0, n: 4f);
        });
        var shapeName =
            continuousCorners ? "continuous-corner" : "rounded-rectangle";

        Assert.True(GetBlue(pixels, 88, 28) >= 100);
        Assert.True(GetBlue(pixels, 16, 64) >= 100);
        Assert.True(GetBlue(pixels, 88, 64) <= 10);
        Assert.True(GetBlue(pixels, 88, 25) <= 10);

        var largestMirrorDifference = 0;
        var mirrorDifferencesOverTolerance = 0;
        var largestDifferenceLocation = string.Empty;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var value = GetBlue(pixels, x, y);
                var horizontal = GetBlue(pixels, Width - 1 - x, y);
                var vertical = GetBlue(pixels, x, Height - 1 - y);
                var horizontalDifference = Math.Abs(value - horizontal);
                var verticalDifference = Math.Abs(value - vertical);
                var difference = Math.Max(horizontalDifference, verticalDifference);
                if (difference > largestMirrorDifference)
                {
                    largestMirrorDifference = difference;
                    largestDifferenceLocation = horizontalDifference >= verticalDifference
                        ? $"horizontal ({x},{y})={value} vs " +
                          $"({Width - 1 - x},{y})={horizontal}"
                        : $"vertical ({x},{y})={value} vs " +
                          $"({x},{Height - 1 - y})={vertical}";
                }
                if (difference > 4)
                {
                    mirrorDifferencesOverTolerance++;
                }
            }
        }
        Assert.True(
            largestMirrorDifference <= 8 && mirrorDifferencesOverTolerance <= 8,
            $"{backend} {shapeName} stroke is not four-corner symmetric: " +
            $"largestDifference={largestMirrorDifference} at {largestDifferenceLocation}, " +
            $"pixelsOverTolerance={mirrorDifferencesOverTolerance}.");

        var verticalAxisCoverage = 0;
        for (var y = 0; y < Height; y++)
        {
            verticalAxisCoverage += GetBlue(pixels, 88, y);
        }
        var horizontalAxisCoverage = 0;
        for (var x = 0; x < Width; x++)
        {
            horizontalAxisCoverage += GetBlue(pixels, x, 64);
        }
        Assert.InRange(
            Math.Abs(verticalAxisCoverage - horizontalAxisCoverage),
            0,
            64);

        if (!continuousCorners)
        {
            // A one-pixel circular-corner stroke must not read heavier than its
            // straight segment. fwidth(distance) is not isotropic here: its L1
            // derivative grows from 1 on an axis to sqrt(2) at 45 degrees.
            //
            // Do not compare the support of the faintest reconstructed samples
            // directly: bilinear reconstruction itself adds a wider low-alpha
            // support to every diagonal line. Instead, constrain the 25% band
            // and its integrated normal-profile ink. The latter catches the old
            // L1 shader on both backends while preserving smooth corner AA.
            var straightProfile = MeasureStrokeProfile(
                pixels,
                centerX: 88f,
                centerY: 28f,
                normalX: 0f,
                normalY: 1f);
            const float cornerCenterX = 40f;
            const float cornerCenterY = 52f;
            const float radius = 24f;
            var diagonal = MathF.Sqrt(0.5f);
            var cornerProfile = MeasureStrokeProfile(
                pixels,
                centerX: cornerCenterX - radius * diagonal,
                centerY: cornerCenterY - radius * diagonal,
                normalX: diagonal,
                normalY: diagonal);

            var cornerInkRatio = cornerProfile.Ink / straightProfile.Ink;
            Assert.True(
                cornerProfile.QuarterSpan <= straightProfile.QuarterSpan + 0.05f &&
                cornerInkRatio is >= 0.72f and <= 0.91f &&
                cornerProfile.Peak >= straightProfile.Peak * 0.85f,
                $"{backend} rounded corner AA is optically wider than the straight edge: " +
                $"straight span={straightProfile.VisibleSpan:F3}, " +
                $"corner span={cornerProfile.VisibleSpan:F3}, " +
                $"straight 25% span={straightProfile.QuarterSpan:F3}, " +
                $"corner 25% span={cornerProfile.QuarterSpan:F3}, " +
                $"straight variance={straightProfile.Variance:F3}, " +
                $"corner variance={cornerProfile.Variance:F3}, " +
                $"straight ink={straightProfile.Ink:F3}, " +
                $"corner ink={cornerProfile.Ink:F3}, " +
                $"ink ratio={cornerInkRatio:F3}, " +
                $"straight peak={straightProfile.Peak:F3}, " +
                $"corner peak={cornerProfile.Peak:F3}.");
        }

        var partialCoveragePixels = CountPartialCoverage(
            pixels, left: 13, top: 25, right: 163, bottom: 103);
        Assert.True(
            partialCoveragePixels >= 30,
            $"{backend} {shapeName} stroke bypassed analytic AA: " +
            $"partialCoveragePixels={partialCoveragePixels}.");
    }

    private static void AssertFilledRoundedBorderMatchesSolidSilhouette(
        RenderBackend backend)
    {
        const int referenceX = 8;
        const int compositeX = 96;
        const int y = 40;
        const int width = 72;
        const int height = 36;
        const int radius = 8;
        const float strokeWidth = 1f;
        const float halfStroke = strokeWidth * 0.5f;

        var pixels = Render(backend, (target, brush) =>
        {
            // Reference: the intended opaque button silhouette.
            target.FillRoundedRectangle(
                referenceX,
                y,
                width,
                height,
                radius,
                radius,
                brush);

            // Border composition used by Border: background reaches the stroke
            // centre line and the outline is drawn later over that underlap.
            target.FillRoundedRectangle(
                compositeX + halfStroke,
                y + halfStroke,
                width - strokeWidth,
                height - strokeWidth,
                radius - halfStroke,
                radius - halfStroke,
                brush);
            target.DrawRoundedRectangle(
                compositeX + halfStroke,
                y + halfStroke,
                width - strokeWidth,
                height - strokeWidth,
                radius - halfStroke,
                radius - halfStroke,
                brush,
                strokeWidth);
        });

        var largestDifference = 0;
        var pixelsOverTolerance = 0;
        var largestDifferenceLocation = string.Empty;
        for (var sampleY = y - 1; sampleY <= y + height; sampleY++)
        {
            for (var localX = -1; localX <= width; localX++)
            {
                var expected = GetBlue(
                    pixels,
                    referenceX + localX,
                    sampleY);
                var actual = GetBlue(
                    pixels,
                    compositeX + localX,
                    sampleY);
                var difference = Math.Abs(expected - actual);
                if (difference > largestDifference)
                {
                    largestDifference = difference;
                    largestDifferenceLocation =
                        $"({localX},{sampleY - y}) expected={expected}, actual={actual}";
                }
                if (difference > 12)
                {
                    pixelsOverTolerance++;
                }
            }
        }

        Assert.True(
            largestDifference <= 28 && pixelsOverTolerance <= 12,
            $"{backend} filled rounded border leaks the surface between fill " +
            $"and stroke: largestDifference={largestDifference} at " +
            $"{largestDifferenceLocation}, pixelsOverTolerance={pixelsOverTolerance}.");
    }

    private static void AssertGradientFillAndStroke(RenderBackend backend)
    {
        var pixels = Render(backend, (target, brush) =>
        {
            target.SetShapeType(type: 1, n: 4f);
            target.FillPerCornerRoundedRectangle(
                16.25f, 16.25f, 144f, 36f,
                1f, 15f, 4f, 10f,
                brush);
            target.DrawPerCornerRoundedRectangle(
                16.25f, 72.25f, 144f, 32f,
                15f, 1f, 12f, 5f,
                brush,
                strokeWidth: 2f);
            target.SetShapeType(type: 0, n: 4f);
        }, useGradient: true);

        Assert.True(GetBlue(pixels, 30, 17) >= 80);
        Assert.True(GetBlue(pixels, 88, 17) >= 80);
        Assert.True(GetBlue(pixels, 30, 24) >= 80);
        Assert.True(GetBlue(pixels, 30, 72) >= 80);
        Assert.True(GetBlue(pixels, 88, 72) >= 80);
        var leftStrokeCoverage = 0;
        for (var y = 84; y <= 92; y++)
        {
            for (var x = 14; x <= 18; x++)
            {
                leftStrokeCoverage = Math.Max(
                    leftStrokeCoverage,
                    GetBlue(pixels, x, y));
            }
        }
        Assert.True(leftStrokeCoverage >= 80);
        Assert.True(GetBlue(pixels, 88, 88) <= 10);

        var partialCoveragePixels = CountPartialCoverage(
            pixels, left: 13, top: 13, right: 163, bottom: 108);
        Assert.True(
            partialCoveragePixels >= 40,
            $"{backend} gradient SuperEllipse has a hard edge: " +
            $"partialCoveragePixels={partialCoveragePixels}.");
    }


    private static byte[] Render(
        RenderBackend backend,
        Action<RenderTarget, NativeBrush> draw,
        bool useGradient = false)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(
            backend,
            GpuPreference.Auto,
            RenderingEngine.Impeller);
        using var brush = useGradient
            ? context.CreateLinearGradientBrush(
                16f, 16f, 160f, 104f,
                s_themeGradientStops,
                stopCount: 2)
            : context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);

        Assert.Equal(backend, context.Backend);
        Assert.True(brush.IsValid);
        Assert.True(target.IsValid);

        // Vulkan readback is armed on the second replay frame; a hidden D3D12
        // HWND can report occluded after its first Present, so capture it once.
        var frameCount = backend == RenderBackend.Vulkan ? 2 : 1;
        for (var frame = 0; frame < frameCount; frame++)
        {
            Assert.True(target.TryBeginDraw());
            target.Clear(0f, 0f, 0f);
            draw(target, brush);
            if (frame == frameCount - 1)
            {
                Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            }
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
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
        return pixels;
    }

    private static int CountPartialCoverage(
        byte[] pixels,
        int left,
        int top,
        int right,
        int bottom)
    {
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var blue = GetBlue(pixels, x, y);
                if (blue is > 0 and < 255)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static (
        float VisibleSpan,
        float QuarterSpan,
        float Variance,
        float Ink,
        float Peak) MeasureStrokeProfile(
        byte[] pixels,
        float centerX,
        float centerY,
        float normalX,
        float normalY)
    {
        const float start = -4f;
        const float end = 4f;
        const float step = 1f / 64f;
        const float visibleThreshold = 8f / 255f;

        var firstVisible = float.PositiveInfinity;
        var lastVisible = float.NegativeInfinity;
        var firstQuarter = float.PositiveInfinity;
        var lastQuarter = float.NegativeInfinity;
        var ink = 0f;
        var peak = 0f;
        var firstMoment = 0f;
        var secondMoment = 0f;

        for (var distance = start; distance <= end; distance += step)
        {
            var coverage = SampleBlue(
                pixels,
                centerX + normalX * distance,
                centerY + normalY * distance) / 255f;
            if (coverage >= visibleThreshold)
            {
                firstVisible = MathF.Min(firstVisible, distance);
                lastVisible = MathF.Max(lastVisible, distance);
            }
            if (coverage >= 0.25f)
            {
                firstQuarter = MathF.Min(firstQuarter, distance);
                lastQuarter = MathF.Max(lastQuarter, distance);
            }
            peak = MathF.Max(peak, coverage);

            var weightedCoverage = coverage * step;
            ink += weightedCoverage;
            firstMoment += distance * weightedCoverage;
            secondMoment += distance * distance * weightedCoverage;
        }

        var mean = ink > 0f ? firstMoment / ink : 0f;
        var variance = ink > 0f
            ? MathF.Max(0f, secondMoment / ink - mean * mean)
            : 0f;
        var visibleSpan =
            float.IsFinite(firstVisible) && float.IsFinite(lastVisible)
                ? lastVisible - firstVisible
                : 0f;
        var quarterSpan =
            float.IsFinite(firstQuarter) && float.IsFinite(lastQuarter)
                ? lastQuarter - firstQuarter
                : 0f;
        return (visibleSpan, quarterSpan, variance, ink, peak);
    }

    private static float SampleBlue(byte[] pixels, float x, float y)
    {
        // Native raster samples live at pixel centres. Shift to an integer
        // sample lattice, then bilinearly reconstruct the profile so axis and
        // diagonal probes use the same continuous measurement.
        var sampleX = x - 0.5f;
        var sampleY = y - 0.5f;
        var x0 = Math.Clamp((int)MathF.Floor(sampleX), 0, Width - 1);
        var y0 = Math.Clamp((int)MathF.Floor(sampleY), 0, Height - 1);
        var x1 = Math.Min(x0 + 1, Width - 1);
        var y1 = Math.Min(y0 + 1, Height - 1);
        var tx = Math.Clamp(sampleX - MathF.Floor(sampleX), 0f, 1f);
        var ty = Math.Clamp(sampleY - MathF.Floor(sampleY), 0f, 1f);

        var top = float.Lerp(
            GetBlue(pixels, x0, y0),
            GetBlue(pixels, x1, y0),
            tx);
        var bottom = float.Lerp(
            GetBlue(pixels, x0, y1),
            GetBlue(pixels, x1, y1),
            tx);
        return float.Lerp(top, bottom, ty);
    }

    private static byte GetBlue(byte[] pixels, int x, int y) =>
        pixels[(y * Width + x) * 4];
}
