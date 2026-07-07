using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Stage-4 GPU offscreen effect RT smoke (JALIUM_VK_EFFECT_GPU_RT): one test
/// per effect-region code path, each driving two real Vulkan frames against a
/// hidden native window and asserting both presents report OK. Covers the
/// OffscreenBegin/End markers, the per-effect composite suppression (blur /
/// color-matrix / emboss / custom shader replace the element), the drop-shadow
/// halo splice around the marker pair, engine-batch spans recorded inside an
/// offscreen region (FillPath), the nested-region degrade, and the
/// zero-staging-bytes frame (a frame whose only upload-family commands are
/// offscreen/live-sourced and stage no CPU pixels).
/// </summary>
/// <remarks>
/// The env gate is latched by the native CRT at process start, so run the
/// suite with JALIUM_VK_EFFECT_GPU_RT=1 in the process environment to exercise
/// the offscreen paths; without it the same calls exercise (and guard) the
/// default pass-through. The color-matrix / emboss / custom-shader paths
/// additionally need dxcompiler.dll to be loadable to compile their shaders —
/// when it is absent they degrade to the composite-back (element unmodified)
/// and the assertions still hold.
/// </remarks>
[Collection("Application")]
public sealed class VulkanEffectGpuRtSmokeTests
{
    private const string InvertShaderHlsl = @"
cbuffer Params : register(b0) { float4 p0; };
Texture2D content : register(t0);
SamplerState samp : register(s0);
struct PsIn { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
float4 main(PsIn i) : SV_Target
{
    float4 c = content.Sample(samp, i.uv);
    return float4(c.a - c.r, c.a - c.g, c.a - c.b, c.a);
}
";

    private static void RunTwoFrames(Action<RenderTarget, NativeBrush> body)
    {
        const int width = 256;
        const int height = 256;

        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        Assert.Equal(RenderBackend.Vulkan, context.Backend);

        using var renderTarget = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.True(renderTarget.IsValid);

        using var fill = context.CreateSolidBrush(0.9f, 0.2f, 0.1f);
        Assert.True(fill.IsValid);

        for (int frame = 0; frame < 2; frame++)
        {
            Assert.True(renderTarget.TryBeginDraw());
            renderTarget.Clear(0.12f, 0.12f, 0.18f);
            body(renderTarget, fill);
            Assert.Equal(JaliumResult.Ok, renderTarget.TryEndDraw());
        }
    }

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Stage4_Blur_PresentsOk() => RunTwoFrames((rt, fill) =>
    {
        rt.BeginEffectCapture(8f, 8f, 72f, 56f);
        rt.FillRectangle(16f, 16f, 48f, 32f, fill);
        rt.EndEffectCapture();
        rt.DrawBlurEffect(8f, 8f, 72f, 56f, 5f);
    });

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Stage4_ColorMatrix_PresentsOk() => RunTwoFrames((rt, fill) =>
    {
        rt.BeginEffectCapture(8f, 72f, 72f, 56f);
        rt.FillRectangle(16f, 80f, 48f, 32f, fill);
        rt.EndEffectCapture();
        ReadOnlySpan<float> grayscale =
        [
            0.33f, 0.33f, 0.33f, 0f, 0f,
            0.33f, 0.33f, 0.33f, 0f, 0f,
            0.33f, 0.33f, 0.33f, 0f, 0f,
            0f, 0f, 0f, 1f, 0f,
        ];
        rt.DrawColorMatrixEffect(8f, 72f, 72f, 56f, grayscale);
    });

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Stage4_Emboss_PresentsOk() => RunTwoFrames((rt, fill) =>
    {
        rt.BeginEffectCapture(88f, 8f, 72f, 56f);
        rt.FillRectangle(96f, 16f, 48f, 32f, fill);
        rt.EndEffectCapture();
        rt.DrawEmbossEffect(88f, 8f, 72f, 56f, 1f, 1f, 0.5f, 2f);
    });

    /// <summary>
    /// F7 verification: the built-in ColorMatrix effect must run from EMBEDDED
    /// SPIR-V (kEffectBuiltinColorMatrixPsSpv) with NO runtime DXC dependency.
    /// Fills the capture region with a saturated BLUE, applies a grayscale
    /// ColorMatrix, reads the finished back buffer back, and asserts the region
    /// is now grey (R≈G≈B) rather than blue (B≫R). The "degraded to
    /// composite-back" fallback (which the effect took when DXC was missing
    /// before this change) would leave the region blue, so this pixel assertion
    /// distinguishes "effect actually applied" from "silently skipped".
    ///
    /// Only asserts the transform when the GPU-RT path is active
    /// (JALIUM_VK_EFFECT_GPU_RT=1, latched at process start); otherwise the
    /// composite-back is the correct behaviour and only present-OK is checked.
    /// Run this suite with the env set AND with the DXC (dxcompiler.dll)
    /// unreachable to prove the embedded pipeline carries the built-in.
    /// </summary>
    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Stage4_ColorMatrix_EmbeddedShader_ProducesGrayscale_WithoutDxc()
    {
        const int width = 256;
        const int height = 256;
        const int rx = 64, ry = 64, rw = 64, rh = 64;   // capture region
        const int cx = rx + rw / 2, cy = ry + rh / 2;    // sampled center

        bool gpuRtActive = string.Equals(
            Environment.GetEnvironmentVariable("JALIUM_VK_EFFECT_GPU_RT"),
            "1",
            StringComparison.Ordinal);

        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        Assert.Equal(RenderBackend.Vulkan, context.Backend);

        using var renderTarget = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.True(renderTarget.IsValid);

        // Saturated opaque blue: if the matrix does NOT run the readback stays
        // blue (B channel ≫ R); if it runs the region collapses to grey.
        using var blue = context.CreateSolidBrush(0.05f, 0.15f, 0.95f, 1f);
        Assert.True(blue.IsValid);

        ReadOnlySpan<float> grayscale =
        [
            0.33f, 0.33f, 0.33f, 0f, 0f,
            0.33f, 0.33f, 0.33f, 0f, 0f,
            0.33f, 0.33f, 0.33f, 0f, 0f,
            0f, 0f, 0f, 1f, 0f,
        ];

        // Two frames (matches the other smokes: the GPU-RT offscreen path warms
        // up on frame 0); readback is armed on the second.
        var buffer = new byte[width * height * 4];
        int capturedW = 0, capturedH = 0;
        for (int frame = 0; frame < 2; frame++)
        {
            Assert.True(renderTarget.TryBeginDraw());
            // Neutral-grey clear so any background bleed at the region edges does
            // not tint the grayscale assertion toward one channel.
            renderTarget.Clear(0.12f, 0.12f, 0.12f);
            renderTarget.BeginEffectCapture(rx, ry, rw, rh);
            renderTarget.FillRectangle(rx, ry, rw, rh, blue);
            renderTarget.EndEffectCapture();
            renderTarget.DrawColorMatrixEffect(rx, ry, rw, rh, grayscale);
            if (frame == 1)
            {
                Assert.Equal(JaliumResult.Ok, renderTarget.RequestReadback());
            }
            Assert.Equal(JaliumResult.Ok, renderTarget.TryEndDraw());
        }

        var fetch = renderTarget.FetchReadback(buffer, (uint)(width * 4), out capturedW, out capturedH);
        Assert.Equal(JaliumResult.Ok, fetch);
        Assert.True(capturedW >= width && capturedH >= height);

        // BGRA8, top-down, tightly packed at stride = width*4.
        int o = (cy * width + cx) * 4;
        byte bch = buffer[o + 0];
        byte gch = buffer[o + 1];
        byte rch = buffer[o + 2];

        if (gpuRtActive)
        {
            // Grey: channels within a small tolerance of each other, and clearly
            // NOT the original blue (which had B ≈ 0.95, R ≈ 0.05 → B - R ≫ 0).
            Assert.True(Math.Abs(rch - gch) <= 12,
                $"expected grey (R≈G) after built-in ColorMatrix, got R={rch} G={gch} B={bch}");
            Assert.True(Math.Abs(rch - bch) <= 12,
                $"expected grey (R≈B) after built-in ColorMatrix, got R={rch} G={gch} B={bch}");
            Assert.True(bch - rch < 40,
                $"region still looks blue (B≫R) — built-in ColorMatrix did not run: R={rch} G={gch} B={bch}");
        }
    }

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Stage4_CustomShader_PresentsOk() => RunTwoFrames((rt, fill) =>
    {
        rt.BeginEffectCapture(88f, 72f, 72f, 56f);
        rt.FillRectangle(96f, 80f, 48f, 32f, fill);
        rt.EndEffectCapture();
        rt.DrawShaderEffectFromSource(88f, 72f, 72f, 56f, InvertShaderHlsl, new float[4]);
    });

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Stage4_DropShadowWithEnginePath_PresentsOk() => RunTwoFrames((rt, fill) =>
    {
        rt.BeginEffectCapture(8f, 136f, 88f, 72f);
        rt.FillRectangle(16f, 144f, 56f, 40f, fill);
        float[] triangle = [0f, 60f, 200f, 0f, 20f, 200f, 5f]; // LineTo, LineTo, Close
        rt.FillPath(20f, 148f, triangle, fill, fillRule: 1);
        rt.EndEffectCapture();
        rt.DrawDropShadowEffect(8f, 136f, 88f, 72f, 6f, 3f, 3f, 0f, 0f, 0f, 0.6f);
    });

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Stage4_NestedRegions_PresentsOk() => RunTwoFrames((rt, fill) =>
    {
        rt.BeginEffectCapture(104f, 136f, 88f, 72f);
        rt.FillRectangle(112f, 144f, 24f, 24f, fill);
        rt.BeginEffectCapture(140f, 150f, 40f, 40f);
        rt.FillRectangle(144f, 154f, 24f, 24f, fill);
        rt.EndEffectCapture();
        rt.DrawBlurEffect(140f, 150f, 40f, 40f, 3f);
        rt.EndEffectCapture();
        rt.DrawBlurEffect(104f, 136f, 88f, 72f, 4f);
    });

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void Stage4_ZeroRadiusBlurKeepsComposite_PresentsOk() => RunTwoFrames((rt, fill) =>
    {
        rt.BeginEffectCapture(200f, 8f, 48f, 48f);
        rt.FillRectangle(204f, 12f, 32f, 32f, fill);
        rt.EndEffectCapture();
        rt.DrawBlurEffect(200f, 8f, 48f, 48f, 0f);
    });
}
