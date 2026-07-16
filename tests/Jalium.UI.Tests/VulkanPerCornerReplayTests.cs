using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class VulkanPerCornerReplayTests
{
    private const int Width = 64;
    private const int Height = 48;

    [RequiresBackendFact(RenderBackend.Vulkan)]
    public void NoOpDraws_DoNotMutatePreviousStroke()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Vulkan);
        using var stroke = context.CreateSolidBrush(1f, 1f, 1f, 1f);
        using var fill = context.CreateSolidBrush(0f, 1f, 0f, 1f);
        using var transparentFill = context.CreateSolidBrush(0f, 1f, 0f, 0f);

        Assert.Equal(RenderBackend.Vulkan, context.Backend);
        Assert.True(stroke.IsValid);
        Assert.True(fill.IsValid);
        Assert.True(transparentFill.IsValid);

        var baseline = RenderBorderWithTrailingNoOp(
            context,
            window.Hwnd,
            stroke,
            fill,
            transparentFill,
            TrailingNoOp.None);

        foreach (var trailingNoOp in new[]
                 {
                     TrailingNoOp.CulledFill,
                     TrailingNoOp.TransparentFill,
                     TrailingNoOp.CulledStroke,
                 })
        {
            var withNoOp = RenderBorderWithTrailingNoOp(
                context,
                window.Hwnd,
                stroke,
                fill,
                transparentFill,
                trailingNoOp);

            var maxDifference = 0;
            var differentChannels = 0;
            for (var i = 0; i < baseline.Length; i++)
            {
                var difference = Math.Abs(baseline[i] - withNoOp[i]);
                maxDifference = Math.Max(maxDifference, difference);
                if (difference != 0)
                {
                    differentChannels++;
                }
            }

            Assert.True(
                maxDifference == 0,
                $"A {trailingNoOp} per-corner draw changed the preceding border: " +
                $"maxDiff={maxDifference}, differentPixels~={differentChannels / 4}");
        }
    }

    private static byte[] RenderBorderWithTrailingNoOp(
        RenderContext context,
        nint hwnd,
        NativeBrush stroke,
        NativeBrush fill,
        NativeBrush transparentFill,
        TrailingNoOp trailingNoOp)
    {
        using var target = context.CreateRenderTarget(hwnd, Width, Height);
        Assert.True(target.IsValid);

        var pixels = new byte[Width * Height * 4];
        for (var frame = 0; frame < 2; frame++)
        {
            Assert.True(target.TryBeginDraw());
            target.Clear(0f, 0f, 0f);

            target.DrawPerCornerRoundedRectangle(
                x: 8.5f,
                y: 8.5f,
                width: 47f,
                height: 31f,
                tl: 15.5f,
                tr: 15.5f,
                br: 15.5f,
                bl: 15.5f,
                stroke,
                strokeWidth: 1f);

            if (trailingNoOp == TrailingNoOp.TransparentFill)
            {
                DrawPerCornerFill(target, transparentFill);
            }
            else if (trailingNoOp is TrailingNoOp.CulledFill or TrailingNoOp.CulledStroke)
            {
                // Smooth scrolling combines the dirty-region clip with the
                // moving content's clip. Their intersection can be empty even
                // though earlier sibling commands remain in the replay list.
                target.PushClipAliased(0f, 0f, 4f, 4f);
                target.PushClip(56f, 40f, 4f, 4f);
                if (trailingNoOp == TrailingNoOp.CulledFill)
                {
                    DrawPerCornerFill(target, fill);
                }
                else
                {
                    target.DrawPerCornerRoundedRectangle(
                        x: 8.5f,
                        y: 8.5f,
                        width: 47f,
                        height: 31f,
                        tl: 4f,
                        tr: 4f,
                        br: 4f,
                        bl: 4f,
                        fill,
                        strokeWidth: 1f);
                }
                target.PopClip();
                target.PopClip();
            }

            if (frame == 1)
            {
                Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            }

            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        }

        Assert.Equal(
            JaliumResult.Ok,
            target.FetchReadback(pixels, Width * 4u, out _, out _));
        return pixels;
    }

    private static void DrawPerCornerFill(RenderTarget target, NativeBrush brush) =>
        target.FillPerCornerRoundedRectangle(
            x: 8f,
            y: 8f,
            width: 48f,
            height: 32f,
            tl: 4f,
            tr: 4f,
            br: 4f,
            bl: 4f,
            brush);

    private enum TrailingNoOp
    {
        None,
        CulledFill,
        TransparentFill,
        CulledStroke,
    }
}
