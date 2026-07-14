using Jalium.UI.Interop;
using Jalium.UI.Media;
using RenderTargetDrawingContext = Jalium.UI.Interop.RenderTargetDrawingContext;

namespace Jalium.UI.Tests;

/// <summary>
/// Pins the effect-capture cull-override contract on
/// <see cref="RenderTargetDrawingContext.CurrentClipBounds"/>: while a
/// BeginEffectCapture..EndEffectCapture scope is open the getter must return
/// the CAPTURE RECT (grown by the sub-pixel cull epsilon) instead of the
/// window dirty-region clip — and instead of the historical <c>null</c>
/// ("no culling at all"), which exempted ancestor-viewport culling too and
/// made a long non-virtualized subtree under an effect re-emit in full on
/// every small-dirty-rect animation frame. Content outside the capture rect
/// can never land in the offscreen texture, so culling against the rect keeps
/// the capture complete by construction.
/// </summary>
// Joins the serialized "Application" collection — the suite's established
// isolation pattern for tests that create real render contexts/targets.
[Collection("Application")]
public sealed class EffectCaptureCullOverrideTests
{
    // Mirrors RenderTargetDrawingContext.ClipCullEpsilon (private const).
    private const double Epsilon = 0.25;

    private static void RunWithDrawingContext(Action<RenderTargetDrawingContext> body)
    {
        const int width = 256;
        const int height = 256;

        // Backend Auto: the assertions exercise pure managed clip-bounds state,
        // so any real backend works — Auto picks the platform default and falls
        // back to Software by itself on a GPU-less host (an explicit Software
        // request is rejected on hosts where the software context is not
        // registered as a standalone backend).
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext();
        using var renderTarget = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.True(renderTarget.IsValid);

        Assert.True(renderTarget.TryBeginDraw());
        var drawingContext = new RenderTargetDrawingContext(renderTarget, context);
        try
        {
            body(drawingContext);
        }
        finally
        {
            drawingContext.Close();
            renderTarget.TryEndDraw();
        }
    }

    private static void AssertRectEqual(Rect expected, Rect? actual)
    {
        Assert.True(actual.HasValue, "CurrentClipBounds unexpectedly null");
        Assert.Equal(expected.X, actual.Value.X, 9);
        Assert.Equal(expected.Y, actual.Value.Y, 9);
        Assert.Equal(expected.Width, actual.Value.Width, 9);
        Assert.Equal(expected.Height, actual.Value.Height, 9);
    }

    [Fact]
    public void BeginEffectCapture_ReplacesDirtyRegionClip_WithCaptureRect()
    {
        RunWithDrawingContext(dc =>
        {
            // Dirty region deliberately DISJOINT from the capture rect: the
            // override must return the full capture rect (capture completeness),
            // not the intersection with the damage clip (which would punch a
            // hole into the freshly-rebuilt capture texture) and not null
            // (which would exempt viewport culling for the whole subtree).
            dc.PushDirtyRegionClip(new Rect(10, 10, 20, 20));
            AssertRectEqual(new Rect(10 - Epsilon, 10 - Epsilon, 20 + 2 * Epsilon, 20 + 2 * Epsilon),
                dc.CurrentClipBounds);

            dc.BeginEffectCapture(100f, 50f, 64f, 32f);
            AssertRectEqual(new Rect(100 - Epsilon, 50 - Epsilon, 64 + 2 * Epsilon, 32 + 2 * Epsilon),
                dc.CurrentClipBounds);

            dc.EndEffectCapture();
            AssertRectEqual(new Rect(10 - Epsilon, 10 - Epsilon, 20 + 2 * Epsilon, 20 + 2 * Epsilon),
                dc.CurrentClipBounds);

            dc.PopDirtyRegionClip();
            Assert.Null(dc.CurrentClipBounds);
        });
    }

    [Fact]
    public void NestedCaptures_InnermostRectWins_AndUnwindsInOrder()
    {
        RunWithDrawingContext(dc =>
        {
            dc.BeginEffectCapture(0f, 0f, 100f, 100f);
            AssertRectEqual(new Rect(-Epsilon, -Epsilon, 100 + 2 * Epsilon, 100 + 2 * Epsilon),
                dc.CurrentClipBounds);

            // Inner capture rect is NOT intersected with the outer one: its
            // whole texture feeds the inner effect (a blur convolves edge
            // pixels inward), so each open scope culls against its own rect.
            dc.BeginEffectCapture(80f, 80f, 40f, 40f);
            AssertRectEqual(new Rect(80 - Epsilon, 80 - Epsilon, 40 + 2 * Epsilon, 40 + 2 * Epsilon),
                dc.CurrentClipBounds);

            dc.EndEffectCapture();
            AssertRectEqual(new Rect(-Epsilon, -Epsilon, 100 + 2 * Epsilon, 100 + 2 * Epsilon),
                dc.CurrentClipBounds);

            dc.EndEffectCapture();
            Assert.Null(dc.CurrentClipBounds);
        });
    }

    [Fact]
    public void CaptureRect_RoundTripsThroughTransform_PushedBeforeCapture()
    {
        RunWithDrawingContext(dc =>
        {
            // Non-translate transform (translations take the managed Offset
            // fast path and never enter the native matrix stack).
            ((ITransformDrawingContext)dc).PushTransform(new ScaleTransform(2, 2), 0, 0);

            // Coordinates are in the current (scaled) drawing space; the entry
            // is stored in surface space and mapped back on read — so inside
            // the same transform the value round-trips to the input rect.
            dc.BeginEffectCapture(10f, 10f, 20f, 20f);
            AssertRectEqual(new Rect(10 - Epsilon, 10 - Epsilon, 20 + 2 * Epsilon, 20 + 2 * Epsilon),
                dc.CurrentClipBounds);

            dc.EndEffectCapture();
            ((ITransformDrawingContext)dc).PopTransform();
        });
    }

    [Fact]
    public void CaptureRect_MapsIntoTransformSpace_PushedInsideCapture()
    {
        RunWithDrawingContext(dc =>
        {
            // Capture opened at identity: stored surface rect == input rect.
            dc.BeginEffectCapture(50f, 60f, 10f, 10f);

            // A scale pushed INSIDE the capture (e.g. a RenderTransform in the
            // captured subtree): the getter must map the stored surface rect
            // through the inverse of the current matrix, exactly like the
            // regular clip stack, so it stays comparable to child bounds.
            ((ITransformDrawingContext)dc).PushTransform(new ScaleTransform(2, 2), 0, 0);
            AssertRectEqual(new Rect(
                    (50 - Epsilon) / 2, (60 - Epsilon) / 2,
                    (10 + 2 * Epsilon) / 2, (10 + 2 * Epsilon) / 2),
                dc.CurrentClipBounds);

            ((ITransformDrawingContext)dc).PopTransform();
            dc.EndEffectCapture();
        });
    }
}
