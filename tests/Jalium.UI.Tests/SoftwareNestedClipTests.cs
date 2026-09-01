using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Pixel-level contract of nested clips on the Software backend. The clip
/// stack stores rectangle intersections, but corner rounding cannot be folded
/// into an intersection: pushing a plain rectangular clip inside a
/// rounded-rect clip used to drop the ancestor's rounding entirely (only the
/// stack top was tested per pixel), and intersecting could shift the corner
/// circles away from the rounded level's own corners.
/// </summary>
[Collection("Application")]
public sealed class SoftwareNestedClipTests
{
    private const int Width = 160;
    private const int Height = 160;

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_NestedRectClip_KeepsAncestorCornerRounding()
    {
        byte[] pixels = Render(static rt =>
        {
            rt.PushRoundedRectClip(20f, 20f, 100f, 100f, 30f, 30f);
            rt.PushClip(20f, 20f, 60f, 60f);
        }, popCount: 2);

        // Inside both the rectangle intersection and the rounded area: painted.
        Assert.True(Luma(pixels, 60, 60) > 200,
            $"center of the intersection must be painted (luma={Luma(pixels, 60, 60):F0})");
        // The ancestor's rounded corner cuts off (24,24) even though the inner
        // rectangular clip contains it.
        Assert.True(Luma(pixels, 24, 24) < 40,
            $"ancestor corner rounding must survive a nested rect clip (luma={Luma(pixels, 24, 24):F0})");
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_PoppedRoundedClip_StopsClipping()
    {
        byte[] pixels = Render(static rt =>
        {
            rt.PushRoundedRectClip(20f, 20f, 100f, 100f, 30f, 30f);
            rt.PopClip();
        }, popCount: 0);

        Assert.True(Luma(pixels, 24, 24) > 200,
            $"after PopClip the corner must paint again (luma={Luma(pixels, 24, 24):F0})");
    }

    private static byte[] Render(Action<RenderTarget> pushClips, int popCount)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        Assert.Equal(RenderBackend.Software, context.Backend);

        using var renderTarget = context.CreateRenderTarget(window.Hwnd, Width, Height);
        Assert.True(renderTarget.IsValid);
        using var white = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        Assert.True(white.IsValid);

        for (int frame = 0; frame < 2; frame++)
        {
            Assert.True(renderTarget.TryBeginDraw());
            renderTarget.Clear(0f, 0f, 0f, 1f);
            pushClips(renderTarget);
            renderTarget.FillRectangle(0f, 0f, Width, Height, white);
            for (int i = 0; i < popCount; i++) renderTarget.PopClip();
            if (frame == 1)
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

    private static double Luma(byte[] pixels, int x, int y)
    {
        int offset = (y * Width + x) * 4;
        return (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3.0;
    }
}
