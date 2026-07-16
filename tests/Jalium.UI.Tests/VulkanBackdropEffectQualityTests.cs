using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class VulkanBackdropEffectQualityTests
{
    private const int Width = 320;
    private const int Height = 128;
    private const int PanelX = 24;
    private const int PanelY = 24;
    private const int PanelWidth = 272;
    private const int PanelHeight = 80;
    private const int CoordinateCellSize = 16;
    private const float RotatedPanelWidth = 160f;
    private const float RotatedPanelHeight = 64f;
    private const float RotationCos = 0.95105654f;
    private const float RotationSin = 0.30901699f;
    private const float RotationTranslateX = 75f;
    private const float RotationTranslateY = 8f;

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void BackdropBlur_LargerRadiusProducesWiderSmoothTransition()
    {
        byte[] radius8 = RenderBackdrop(radius: 8f, forceOversizedUpload: false);
        byte[] radius32 = RenderBackdrop(radius: 32f, forceOversizedUpload: false);

        int narrowWidth = MeasureTransitionWidth(radius8, Height / 2);
        int wideWidth = MeasureTransitionWidth(radius32, Height / 2);

        Assert.True(narrowWidth >= 3,
            $"8px backdrop blur did not soften the edge: width={narrowWidth}px");
        Assert.True(wideWidth >= narrowWidth + 10,
            $"32px backdrop blur was still effectively clamped near 8px: " +
            $"narrow={narrowWidth}px, wide={wideWidth}px");
    }

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void BackdropSampling_IsInvariantToOversizedSharedUploadImage()
    {
        byte[] normal = RenderBackdrop(radius: 24f, forceOversizedUpload: false);
        byte[] oversized = RenderBackdrop(radius: 24f, forceOversizedUpload: true);

        AssertImagesNear(normal, oversized,
            "backdrop sampled outside its valid capture when the shared upload image grew");
    }

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void BackdropSampling_IsInvariantToLargerSharedBlurScratch()
    {
        byte[] normal = RenderBackdrop(radius: 24f, forceOversizedUpload: false);
        byte[] largerScratch = RenderBackdrop(
            radius: 24f,
            forceOversizedUpload: false,
            forceLargerBlurScratch: true);

        AssertImagesNear(normal, largerScratch,
            "backdrop UVs changed when another effect made the shared blur scratch larger");
    }

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void BackdropSampling_RotatedLiveBackdropAtZeroRadiusUsesScreenCoordinates()
    {
        byte[] background = RenderRotatedLiveBackdrop(drawBackdrop: false);
        byte[] actual = RenderRotatedLiveBackdrop(drawBackdrop: true);

        AssertRotatedInteriorMatchesScreen(background, actual);
    }

    private static byte[] RenderBackdrop(
        float radius,
        bool forceOversizedUpload,
        bool forceLargerBlurScratch = false)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        Assert.Equal(RenderBackend.Vulkan, context.Backend);

        using var renderTarget = context.CreateRenderTarget(window.Hwnd, Width, Height);
        Assert.True(renderTarget.IsValid);
        using var black = context.CreateSolidBrush(0f, 0f, 0f, 1f);
        using var white = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        Assert.True(black.IsValid && white.IsValid);

        using NativeBitmap? oversizedBitmap = forceOversizedUpload
            ? context.CreateBitmapFromPixels(new byte[512 * 512 * 4], 512, 512)
            : null;
        if (oversizedBitmap is not null)
        {
            Assert.True(oversizedBitmap.IsValid);
        }

        int frameCount = forceLargerBlurScratch ? 4 : 2;
        for (int frame = 0; frame < frameCount; frame++)
        {
            Assert.True(renderTarget.TryBeginDraw());
            renderTarget.Clear(0f, 0f, 0f, 1f);

            // This command only grows the shared upload allocation. Opaque
            // background draws fully cover it before the backdrop is captured.
            if (oversizedBitmap is not null)
            {
                renderTarget.DrawBitmap(oversizedBitmap, 0f, 0f, 1f, 1f);
            }

            renderTarget.FillRectangle(0f, 0f, Width, Height, black);
            renderTarget.FillRectangle(Width / 2f, 0f, Width / 2f, Height, white);
            if (forceLargerBlurScratch && frame >= 2)
            {
                // Radius 8 keeps the full-resolution capture and therefore
                // grows scratch wider than the radius-24 downsampled capture.
                // Waiting until frames 2/3 forces both in-flight slots through
                // descriptor-preserving image growth. Repainting the opaque
                // step removes its visual contribution; only allocation
                // pressure remains for the target effect.
                DrawBackdrop(renderTarget, 8f);
                renderTarget.FillRectangle(0f, 0f, Width, Height, black);
                renderTarget.FillRectangle(Width / 2f, 0f, Width / 2f, Height, white);
            }
            DrawBackdrop(renderTarget, radius);

            if (frame == frameCount - 1)
            {
                Assert.Equal(JaliumResult.Ok, renderTarget.RequestReadback());
            }
            Assert.Equal(JaliumResult.Ok, renderTarget.TryEndDraw());
        }

        var pixels = new byte[Width * Height * 4];
        Assert.Equal(JaliumResult.Ok,
            renderTarget.FetchReadback(pixels, Width * 4u, out int capturedWidth, out int capturedHeight));
        Assert.True(capturedWidth >= Width && capturedHeight >= Height);
        return pixels;
    }

    private static void DrawBackdrop(RenderTarget renderTarget, float radius)
    {
        renderTarget.DrawBackdropFilterEx(
            PanelX, PanelY, PanelWidth, PanelHeight,
            backdropFilter: null,
            material: null,
            materialTint: "#000000",
            materialTintOpacity: 0f,
            materialBlurRadius: radius,
            noiseIntensity: 0f,
            saturation: 1f,
            luminosity: 1f,
            cornerRadiusTL: 0f,
            cornerRadiusTR: 0f,
            cornerRadiusBR: 0f,
            cornerRadiusBL: 0f);
    }

    private static byte[] RenderRotatedLiveBackdrop(bool drawBackdrop)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        Assert.Equal(RenderBackend.Vulkan, context.Backend);

        using var renderTarget = context.CreateRenderTarget(window.Hwnd, Width, Height);
        Assert.True(renderTarget.IsValid);

        int columns = (Width + CoordinateCellSize - 1) / CoordinateCellSize;
        int rows = (Height + CoordinateCellSize - 1) / CoordinateCellSize;
        var brushes = new NativeBrush[columns * rows];
        try
        {
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float red = column / (float)Math.Max(1, columns - 1);
                    float green = row / (float)Math.Max(1, rows - 1);
                    float blue = ((column * 5) + (row * 3)) % columns /
                        (float)Math.Max(1, columns - 1);
                    NativeBrush brush = context.CreateSolidBrush(red, green, blue, 1f);
                    Assert.True(brush.IsValid);
                    brushes[(row * columns) + column] = brush;
                }
            }

            for (int frame = 0; frame < 2; frame++)
            {
                Assert.True(renderTarget.TryBeginDraw());
                renderTarget.Clear(0f, 0f, 0f, 1f);

                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        renderTarget.FillRectangle(
                            column * CoordinateCellSize,
                            row * CoordinateCellSize,
                            CoordinateCellSize,
                            CoordinateCellSize,
                            brushes[(row * columns) + column]);
                    }
                }

                if (drawBackdrop)
                {
                    renderTarget.PushTransform([
                        RotationCos,
                        RotationSin,
                        -RotationSin,
                        RotationCos,
                        RotationTranslateX,
                        RotationTranslateY]);
                    renderTarget.DrawBackdropFilterEx(
                        0f, 0f, RotatedPanelWidth, RotatedPanelHeight,
                        backdropFilter: null,
                        material: null,
                        materialTint: "#000000",
                        materialTintOpacity: 0f,
                        materialBlurRadius: 0f,
                        noiseIntensity: 0f,
                        saturation: 1f,
                        luminosity: 1f,
                        cornerRadiusTL: 0f,
                        cornerRadiusTR: 0f,
                        cornerRadiusBR: 0f,
                        cornerRadiusBL: 0f);
                    renderTarget.PopTransform();
                }

                if (frame == 1)
                {
                    Assert.Equal(JaliumResult.Ok, renderTarget.RequestReadback());
                }
                Assert.Equal(JaliumResult.Ok, renderTarget.TryEndDraw());
            }

            var pixels = new byte[Width * Height * 4];
            Assert.Equal(JaliumResult.Ok,
                renderTarget.FetchReadback(
                    pixels,
                    Width * 4u,
                    out int capturedWidth,
                    out int capturedHeight));
            Assert.True(capturedWidth >= Width && capturedHeight >= Height);
            return pixels;
        }
        finally
        {
            foreach (NativeBrush? brush in brushes)
            {
                brush?.Dispose();
            }
        }
    }

    private static void AssertRotatedInteriorMatchesScreen(byte[] expected, byte[] actual)
    {
        const float edgeMargin = 8f;
        const int cellBoundaryMargin = 3;
        const float determinant =
            (RotationCos * RotationCos) - (-RotationSin * RotationSin);
        const float legacyAabbLeft = RotationTranslateX - (RotationSin * RotatedPanelHeight);
        const float legacyAabbTop = RotationTranslateY;
        const float legacyAabbWidth =
            (RotationCos * RotatedPanelWidth) + (RotationSin * RotatedPanelHeight);
        const float legacyAabbHeight =
            (RotationSin * RotatedPanelWidth) + (RotationCos * RotatedPanelHeight);

        int maxDifference = 0;
        long totalDifference = 0;
        long legacyMappingDifference = 0;
        int comparedChannels = 0;
        int comparedPixels = 0;
        for (int y = 0; y < Height; y++)
        {
            int cellY = y % CoordinateCellSize;
            if (cellY < cellBoundaryMargin ||
                cellY >= CoordinateCellSize - cellBoundaryMargin)
            {
                continue;
            }

            for (int x = 0; x < Width; x++)
            {
                int cellX = x % CoordinateCellSize;
                if (cellX < cellBoundaryMargin ||
                    cellX >= CoordinateCellSize - cellBoundaryMargin)
                {
                    continue;
                }

                float screenX = (x + 0.5f) - RotationTranslateX;
                float screenY = (y + 0.5f) - RotationTranslateY;
                float localX = ((RotationCos * screenX) + (RotationSin * screenY)) /
                    determinant;
                float localY = ((-RotationSin * screenX) + (RotationCos * screenY)) /
                    determinant;
                if (localX < edgeMargin || localX >= RotatedPanelWidth - edgeMargin ||
                    localY < edgeMargin || localY >= RotatedPanelHeight - edgeMargin)
                {
                    continue;
                }

                int offset = (y * Width + x) * 4;
                // Before the shader fix, input.uv remapped this local point over
                // the transformed quad's AABB. Keep the test pattern honest by
                // proving that those legacy source coordinates are measurably
                // different from the screen-space pixel we expect.
                int legacyX = Math.Clamp(
                    (int)MathF.Floor(
                        legacyAabbLeft + (localX / RotatedPanelWidth * legacyAabbWidth)),
                    0,
                    Width - 1);
                int legacyY = Math.Clamp(
                    (int)MathF.Floor(
                        legacyAabbTop + (localY / RotatedPanelHeight * legacyAabbHeight)),
                    0,
                    Height - 1);
                int legacyOffset = (legacyY * Width + legacyX) * 4;
                for (int channel = 0; channel < 3; channel++)
                {
                    int difference = Math.Abs(expected[offset + channel] - actual[offset + channel]);
                    maxDifference = Math.Max(maxDifference, difference);
                    totalDifference += difference;
                    legacyMappingDifference += Math.Abs(
                        expected[offset + channel] - expected[legacyOffset + channel]);
                    comparedChannels++;
                }
                comparedPixels++;
            }
        }

        Assert.True(comparedPixels >= 1_000,
            $"rotated backdrop comparison covered too few stable pixels: {comparedPixels}");
        double legacyMeanDifference = (double)legacyMappingDifference / comparedChannels;
        Assert.True(legacyMeanDifference >= 12.0,
            "coordinate-grid fixture would not distinguish the legacy local-UV mapping: " +
            $"meanLegacyDiff={legacyMeanDifference:F3}");
        double meanDifference = (double)totalDifference / comparedChannels;
        Assert.True(maxDifference <= 3 && meanDifference <= 0.5,
            "zero-radius rotated live backdrop did not preserve screen-space background " +
            $"sampling: pixels={comparedPixels}, maxDiff={maxDifference}, " +
            $"meanDiff={meanDifference:F3}");
    }

    private static void AssertImagesNear(byte[] expected, byte[] actual, string message)
    {
        int maxDifference = 0;
        long totalDifference = 0;
        int comparedChannels = 0;
        for (int y = PanelY + 8; y < PanelY + PanelHeight - 8; y++)
        {
            for (int x = PanelX + 8; x < PanelX + PanelWidth - 8; x++)
            {
                int offset = (y * Width + x) * 4;
                for (int channel = 0; channel < 3; channel++)
                {
                    int difference = Math.Abs(expected[offset + channel] - actual[offset + channel]);
                    maxDifference = Math.Max(maxDifference, difference);
                    totalDifference += difference;
                    comparedChannels++;
                }
            }
        }

        double meanDifference = (double)totalDifference / comparedChannels;
        Assert.True(maxDifference <= 3 && meanDifference <= 0.5,
            $"{message}: maxDiff={maxDifference}, meanDiff={meanDifference:F3}");
    }

    private static int MeasureTransitionWidth(byte[] pixels, int y)
    {
        int low = -1;
        int high = -1;
        for (int x = PanelX; x < PanelX + PanelWidth; x++)
        {
            int offset = (y * Width + x) * 4;
            int luminance = (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3;
            if (low < 0 && luminance >= 26)
            {
                low = x;
            }
            if (high < 0 && luminance >= 230)
            {
                high = x;
                break;
            }
        }

        Assert.True(low >= 0 && high >= low,
            $"could not find a complete black-to-white transition: low={low}, high={high}");
        return high - low;
    }
}
