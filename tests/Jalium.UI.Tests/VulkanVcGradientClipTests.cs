using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class VulkanVcGradientClipTests
{
    private const int Width = 64;
    private const int Height = 64;

    private static readonly float[] s_blueGradientStops =
    [
        0f, 0.00f, 0.25f, 1.00f, 1f,
        1f, 0.20f, 0.85f, 1.00f, 1f
    ];

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void GradientFan_PerCornerRoundedClip_PreservesIndependentCornersAndAntialiases()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        using var gradient = context.CreateLinearGradientBrush(
            8f, 8f, 56f, 56f, s_blueGradientStops, stopCount: 2);

        var pixels = RenderGradient(
            context,
            window.Hwnd,
            gradient,
            target => target.PushPerCornerRoundedRectClip(
                16f, 16f, 32f, 32f,
                tl: 12f, tr: 0f, br: 12f, bl: 0f));

        Assert.True(GetBlue(pixels, 17, 17) <= 10,
            "The rounded top-left corner should clip the gradient fan.");
        Assert.True(GetBlue(pixels, 47, 17) >= 240,
            "The square top-right corner must not collapse to the largest radius.");
        Assert.True(GetBlue(pixels, 47, 47) <= 10,
            "The rounded bottom-right corner should clip the gradient fan.");
        Assert.True(GetBlue(pixels, 17, 47) >= 240,
            "The square bottom-left corner must remain filled.");
        Assert.True(CountPartialCoverage(pixels, 15, 49) >= 8,
            "The Vc rounded clip should retain a fractional-coverage AA fringe.");
    }

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void GradientFan_InverseRoundedClip_ExcludesInteriorAndAntialiases()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        using var gradient = context.CreateLinearGradientBrush(
            8f, 8f, 56f, 56f, s_blueGradientStops, stopCount: 2);

        var pixels = RenderGradient(
            context,
            window.Hwnd,
            gradient,
            target => NativeMethods.PushRoundedRectClipExclude(
                target.Handle, 20f, 20f, 24f, 24f, 8f, 8f));

        Assert.True(GetBlue(pixels, 32, 32) <= 10,
            "The inverse clip must remove the rounded rectangle interior.");
        Assert.True(GetBlue(pixels, 12, 32) >= 240,
            "Pixels outside the inverse clip rectangle must remain visible.");
        Assert.True(GetBlue(pixels, 21, 21) >= 240,
            "Pixels outside the rounded corner but inside its AABB must remain visible.");
        Assert.True(CountPartialCoverage(pixels, 18, 46) >= 8,
            "The inverse Vc rounded clip should retain a fractional-coverage AA fringe.");
    }

    private static byte[] RenderGradient(
        RenderContext context,
        nint hwnd,
        NativeBrush gradient,
        Action<RenderTarget> pushClip)
    {
        using var target = context.CreateRenderTarget(hwnd, Width, Height);
        Assert.Equal(RenderBackend.Vulkan, context.Backend);
        Assert.True(gradient.IsValid);
        Assert.True(target.IsValid);

        for (var frame = 0; frame < 2; frame++)
        {
            Assert.True(target.TryBeginDraw());
            target.Clear(0f, 0f, 0f);
            pushClip(target);
            target.FillRectangle(8f, 8f, 48f, 48f, gradient);
            target.PopClip();

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

    private static int CountPartialCoverage(byte[] pixels, int min, int max)
    {
        var count = 0;
        for (var y = min; y <= max; y++)
        {
            for (var x = min; x <= max; x++)
            {
                if (GetBlue(pixels, x, y) is > 0 and < 250)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static byte GetBlue(byte[] pixels, int x, int y) =>
        pixels[(y * Width + x) * 4];
}
