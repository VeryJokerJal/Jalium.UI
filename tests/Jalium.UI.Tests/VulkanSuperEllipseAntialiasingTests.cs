using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class VulkanSuperEllipseAntialiasingTests
{
    private const int Width = 64;
    private const int Height = 64;

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void GalleryTileFill_HasPartialCoverageAlongSuperEllipseEdge()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        using var fill = context.CreateSolidBrush(1f, 1f, 1f, 1f);

        Assert.Equal(RenderBackend.Vulkan, context.Backend);
        Assert.True(fill.IsValid);

        AssertGalleryTile(context, window.Hwnd, fill, x: 15f, y: 15f);
        AssertGalleryTile(context, window.Hwnd, fill, x: 15.35f, y: 15.2f);
    }

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Fill_RespectsAncestorRoundedClip()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        using var fill = context.CreateSolidBrush(1f, 1f, 1f, 1f);

        Assert.Equal(RenderBackend.Vulkan, context.Backend);
        Assert.True(fill.IsValid);

        var withoutClip = RenderWithOptionalRoundedClip(context, window.Hwnd, fill, pushClip: false);
        var withClip = RenderWithOptionalRoundedClip(context, window.Hwnd, fill, pushClip: true);

        // This pixel is inside the large SuperEllipse and the clip's AABB, but
        // outside the rounded clip corner. It therefore catches a regression
        // where the analytic shape steals roundedClipRect and leaves only the
        // rectangular scissor behind.
        Assert.True(GetBlue(withoutClip, 17, 17) >= 250);
        Assert.True(GetBlue(withClip, 17, 17) <= 5);
        Assert.True(GetBlue(withClip, 32, 32) >= 250);
    }

    private static void AssertGalleryTile(RenderContext context, nint hwnd, NativeBrush fill, float x, float y)
    {
        using var target = context.CreateRenderTarget(hwnd, Width, Height);
        Assert.True(target.IsValid);

        for (var frame = 0; frame < 2; frame++)
        {
            Assert.True(target.TryBeginDraw());
            target.Clear(0f, 0f, 0f);

            // Matches the Gallery's 34x34 `Razor @{ for }` tiles. Border sets
            // this state around the per-corner rounded-rectangle fill even
            // though its CornerRadius remains at the default zero value.
            target.SetShapeType(type: 1, n: 4f);
            target.FillPerCornerRoundedRectangle(
                x,
                y,
                width: 34f,
                height: 34f,
                tl: 0f,
                tr: 0f,
                br: 0f,
                bl: 0f,
                fill);
            target.SetShapeType(type: 0, n: 4f);

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

        var partialCoverageByQuadrant = new int[4];
        var centerX = x + 17f;
        var centerY = y + 17f;
        for (var pixelY = 12; pixelY <= 51; pixelY++)
        {
            for (var pixelX = 12; pixelX <= 51; pixelX++)
            {
                var blue = pixels[(pixelY * Width + pixelX) * 4];
                if (blue is > 0 and < 255)
                {
                    var quadrant = (pixelY + 0.5f >= centerY ? 2 : 0) +
                        (pixelX + 0.5f >= centerX ? 1 : 0);
                    partialCoverageByQuadrant[quadrant]++;
                }
            }
        }

        var partialCoveragePixels = partialCoverageByQuadrant.Sum();
        Assert.True(
            partialCoveragePixels >= 20,
            $"The 34x34 SuperEllipse has a hard raster edge at ({x}, {y}): " +
            $"partialCoveragePixels={partialCoveragePixels}.");
        Assert.All(
            partialCoverageByQuadrant,
            count => Assert.True(count >= 4, $"A SuperEllipse quadrant has only {count} partial-coverage pixels."));

        Assert.True(GetBlue(pixels, (int)MathF.Floor(x), (int)MathF.Floor(y)) <= 5,
            "The SuperEllipse's outer corner should remain transparent.");
        Assert.True(GetBlue(pixels, (int)MathF.Floor(centerX), (int)MathF.Floor(centerY)) >= 250,
            "The SuperEllipse's centre should remain fully opaque.");
    }

    private static byte[] RenderWithOptionalRoundedClip(
        RenderContext context,
        nint hwnd,
        NativeBrush fill,
        bool pushClip)
    {
        using var target = context.CreateRenderTarget(hwnd, Width, Height);
        Assert.True(target.IsValid);

        for (var frame = 0; frame < 2; frame++)
        {
            Assert.True(target.TryBeginDraw());
            target.Clear(0f, 0f, 0f);
            if (pushClip)
            {
                target.PushRoundedRectClip(16f, 16f, 32f, 32f, 12f, 12f);
            }

            target.SetShapeType(type: 1, n: 4f);
            target.FillPerCornerRoundedRectangle(
                x: 8f,
                y: 8f,
                width: 48f,
                height: 48f,
                tl: 0f,
                tr: 0f,
                br: 0f,
                bl: 0f,
                fill);
            target.SetShapeType(type: 0, n: 4f);

            if (pushClip)
            {
                target.PopClip();
            }
            if (frame == 1)
            {
                Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            }

            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        }

        var pixels = new byte[Width * Height * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, Width * 4u, out _, out _));
        return pixels;
    }

    private static byte GetBlue(byte[] pixels, int x, int y) => pixels[(y * Width + x) * 4];
}
