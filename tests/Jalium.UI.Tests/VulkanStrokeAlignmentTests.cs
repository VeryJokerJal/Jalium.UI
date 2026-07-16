using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class VulkanStrokeAlignmentTests
{
    private const int Width = 512;
    private const int Height = 64;
    private const int SampleY = 28;

    // These are the exact float values emitted by the Gallery title-bar TextBox at 96 DPI.
    private const float FillX = 165.56836f;
    private const float StrokeX = 166.06836f;

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void GallerySearchBorder_HasMirrorSymmetricSideCoverage()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        using var fill = context.CreateSolidBrush(28f / 255f, 28f / 255f, 28f / 255f, 1f);
        using var stroke = context.CreateSolidBrush(1f, 1f, 1f, 1f);

        Assert.Equal(RenderBackend.Vulkan, context.Backend);
        Assert.True(fill.IsValid);
        Assert.True(stroke.IsValid);

        var withoutStroke = RenderGallerySearchChrome(context, window.Hwnd, fill, stroke, drawStroke: false);
        var withStroke = RenderGallerySearchChrome(context, window.Hwnd, fill, stroke, drawStroke: true);

        var leftCoverage = SumStrokeCoverage(withoutStroke, withStroke, 163, 169);
        var rightCoverage = SumStrokeCoverage(withoutStroke, withStroke, 482, 488);

        Assert.True(
            Math.Abs(leftCoverage - rightCoverage) <= 0.08,
            $"Gallery search border is asymmetric: left={leftCoverage:F3}, right={rightCoverage:F3}; " +
            $"leftPixels=[{DescribePixels(withoutStroke, withStroke, 163, 169)}], " +
            $"rightPixels=[{DescribePixels(withoutStroke, withStroke, 482, 488)}]");
    }

    private static byte[] RenderGallerySearchChrome(
        RenderContext context,
        nint hwnd,
        NativeBrush fill,
        NativeBrush stroke,
        bool drawStroke)
    {
        using var target = context.CreateRenderTarget(hwnd, Width, Height);
        Assert.True(target.IsValid);

        for (var frame = 0; frame < 2; frame++)
        {
            Assert.True(target.TryBeginDraw());
            target.Clear(32f / 255f, 32f / 255f, 32f / 255f);
            target.FillPerCornerRoundedRectangle(
                FillX, 10f, 320f, 36f,
                8f, 8f, 8f, 8f,
                fill);

            if (drawStroke)
            {
                target.DrawPerCornerRoundedRectangle(
                    StrokeX, 10.5f, 319f, 35f,
                    7.5f, 7.5f, 7.5f, 7.5f,
                    stroke,
                    strokeWidth: 1f);
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
        return pixels;
    }

    private static double SumStrokeCoverage(byte[] baseline, byte[] rendered, int startX, int endX)
    {
        var coverage = 0.0;
        for (var x = startX; x <= endX; x++)
        {
            var before = GetBlue(baseline, x, SampleY);
            var after = GetBlue(rendered, x, SampleY);
            coverage += Math.Max(0.0, (after - before) / (255.0 - before));
        }

        return coverage;
    }

    private static string DescribePixels(byte[] baseline, byte[] rendered, int startX, int endX) =>
        string.Join(", ", Enumerable.Range(startX, endX - startX + 1).Select(
            x => $"x{x}:{GetBlue(baseline, x, SampleY)}->{GetBlue(rendered, x, SampleY)}"));

    private static byte GetBlue(byte[] pixels, int x, int y) => pixels[(y * Width + x) * 4];
}
