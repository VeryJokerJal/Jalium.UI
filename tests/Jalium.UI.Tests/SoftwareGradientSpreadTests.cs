using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Pixel-level contract of gradient SpreadMethod (Pad/Repeat/Reflect) and the
/// radial focal point (GradientOrigin) on the Software backend. Both used to be
/// silently discarded by the native CPU rasterizer: extendMode arrived as an
/// ignored parameter and the radial sampler read only center+radius, so
/// Repeat/Reflect degraded to Pad and an off-center origin rendered as a
/// perfectly centered gradient.
/// </summary>
[Collection("Application")]
public sealed class SoftwareGradientSpreadTests
{
    private const int Width = 256;
    private const int Height = 64;

    // Black→white gradient line spanning x ∈ [0, 64].
    private const float GradEndX = 64f;

    private static readonly float[] BlackToWhite =
    {
        0f, 0f, 0f, 0f, 1f,   // position, r, g, b, a
        1f, 1f, 1f, 1f, 1f,
    };

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_LinearPad_ClampsPastGradientEnd()
    {
        byte[] pixels = RenderLinear(extendMode: 0);

        Assert.True(LumaAt(pixels, 200) > 245,
            $"Pad: far past the gradient end must clamp to the last stop (luma={LumaAt(pixels, 200):F0})");
        double mid = LumaAt(pixels, 32);
        Assert.True(mid > 40 && mid < 215,
            $"Pad: mid-gradient should be an intermediate value (luma={mid:F0})");
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_LinearRepeat_WrapsPeriodically()
    {
        byte[] pixels = RenderLinear(extendMode: 1);

        // Second and third periods must repeat the first period's phase.
        AssertNear(LumaAt(pixels, 32), LumaAt(pixels, 96), 10,
            "Repeat: x=96 (second period) must match x=32");
        AssertNear(LumaAt(pixels, 32), LumaAt(pixels, 160), 10,
            "Repeat: x=160 (third period) must match x=32");
        // Just past the end the gradient restarts dark — the visible difference
        // from Pad (which stays white ≈255). Compare against the same phase in
        // the first period rather than an absolute value: the linear-light stop
        // interpolation brightens the dark end fast (t≈0.1 already reads ~90).
        AssertNear(LumaAt(pixels, 6), LumaAt(pixels, 70), 10,
            "Repeat: x=70 must match the same phase at x=6");
        Assert.True(LumaAt(pixels, 70) < 180,
            $"Repeat: x=70 must restart dark, not clamp white (luma={LumaAt(pixels, 70):F0})");
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_LinearReflect_MirrorsAlternatePeriods()
    {
        byte[] pixels = RenderLinear(extendMode: 2);

        // Reflected period: x ∈ [64,128] maps t → (128-x)/64.
        Assert.True(LumaAt(pixels, 70) > 200,
            $"Reflect: x=70 mirrors x=58 and stays bright (luma={LumaAt(pixels, 70):F0})");
        AssertNear(LumaAt(pixels, 96), LumaAt(pixels, 32), 10,
            "Reflect: x=96 mirrors onto x=32");
        AssertNear(LumaAt(pixels, 140), LumaAt(pixels, 12), 10,
            "Reflect: x=140 (third period, forward again) must match x=12");
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_RadialFocalOrigin_ShiftsGradientStart()
    {
        // Center (64,32), radius 48/24, focal origin displaced left to (40,32).
        byte[] pixels = RenderRadial(originX: 40f, originY: 32f);

        // t = 0 at the ORIGIN, not the center. The half-pixel sampling offset
        // under linear-light interpolation already reads ~40, so the bound only
        // needs to separate this from the old center-distance sampler, which
        // put t=0.5 here (luma ≈ 188).
        Assert.True(LumaAt(pixels, 40, 32) < 70,
            $"Focal origin must be the gradient start (luma={LumaAt(pixels, 40, 32):F0})");
        // The old center-distance sampler made (40,32) a mid grey and rendered
        // left/right of the CENTER symmetrically. With the focus displaced left,
        // equal distances from the focus are asymmetric: the short left side
        // compresses (reaches white sooner) than the long right side.
        double left = LumaAt(pixels, 20, 32);   // 20px left of focus, 4px from ellipse edge
        double right = LumaAt(pixels, 60, 32);  // 20px right of focus, 52px from right edge
        Assert.True(left > right + 30,
            $"Focal gradient must be asymmetric around the focus (left={left:F0}, right={right:F0})");
    }

    // --- Rendering ----------------------------------------------------------

    private static byte[] RenderLinear(uint extendMode)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        Assert.Equal(RenderBackend.Software, context.Backend);

        using var renderTarget = context.CreateRenderTarget(window.Hwnd, Width, Height);
        Assert.True(renderTarget.IsValid);
        using var gradient = context.CreateLinearGradientBrush(
            0f, 0f, GradEndX, 0f, BlackToWhite, 2, extendMode);
        Assert.True(gradient.IsValid);

        return RenderAndFetch(renderTarget, gradient);
    }

    private static byte[] RenderRadial(float originX, float originY)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        Assert.Equal(RenderBackend.Software, context.Backend);

        using var renderTarget = context.CreateRenderTarget(window.Hwnd, Width, Height);
        Assert.True(renderTarget.IsValid);
        using var gradient = context.CreateRadialGradientBrush(
            64f, 32f, 48f, 24f, originX, originY, BlackToWhite, 2, 0);
        Assert.True(gradient.IsValid);

        return RenderAndFetch(renderTarget, gradient);
    }

    private static byte[] RenderAndFetch(RenderTarget renderTarget, NativeBrush gradient)
    {
        for (int frame = 0; frame < 2; frame++)
        {
            Assert.True(renderTarget.TryBeginDraw());
            renderTarget.Clear(0f, 0f, 0f, 1f);
            renderTarget.FillRectangle(0f, 0f, Width, Height, gradient);
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

    // --- Measurement --------------------------------------------------------

    private static double LumaAt(byte[] pixels, int x, int y = 32)
    {
        int offset = (y * Width + x) * 4;
        return (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3.0;
    }

    private static void AssertNear(double expected, double actual, double tolerance, string what)
    {
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"{what}: expected ≈{expected:F0}, got {actual:F0}");
    }
}
