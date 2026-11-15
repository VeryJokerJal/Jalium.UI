using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using RenderTargetDrawingContext = Jalium.UI.Interop.RenderTargetDrawingContext;

namespace Jalium.UI.Tests;

/// <summary>
/// Pins the "effect capture under a live non-translate transform" contract of
/// <see cref="RenderTargetDrawingContext"/>: an element effect captured while an
/// ancestor RenderTransform / Viewbox zoom is live must be composited back as a
/// 1:1 SCREEN-space blit (the capture already applied the matrix once), so
/// BeginEffectCapture records the screen-space capture origin and
/// ApplyElementEffect neutralizes the live matrix and hands native screen-space
/// geometry. Before this contract the D3D12 backend transformed the captured
/// content a second time on the way back and cropped it to the singly-transformed
/// rect (a shadow layer under a 1.08 hover zoom showed a square dark slab behind
/// its pill).
/// </summary>
[Collection("Application")]
public sealed class EffectCaptureTransformCompensationTests
{
    private const double Tolerance = 1e-9;

    // ── pure math ─────────────────────────────────────────────────────────

    [Fact]
    public void ScreenCaptureRect_ScaleAboutOrigin_IsTransformedAabb()
    {
        var m = new Matrix(2, 0, 0, 2, 0, 0);
        var r = RenderTargetDrawingContext.ComputeScreenEffectCaptureRect(
            new Rect(10, 10, 20, 20), m, 1.0, 1.0);

        Assert.Equal(20, r.X, Tolerance);
        Assert.Equal(20, r.Y, Tolerance);
        Assert.Equal(40, r.Width, Tolerance);
        Assert.Equal(40, r.Height, Tolerance);
    }

    [Fact]
    public void ScreenCaptureRect_ScaleAboutCentre_SnapsOutwardToPixelGrid()
    {
        // The reported case: a 460×54 capture scaled 1.08 about its centre
        // (230, 27) → AABB x = -18.4 .. 478.4, y = -2.16 .. 56.16.
        var m = Matrix.Identity;
        m.ScaleAt(1.08, 1.08, 230, 27);
        var r = RenderTargetDrawingContext.ComputeScreenEffectCaptureRect(
            new Rect(0, 0, 460, 54), m, 1.0, 1.0);

        Assert.Equal(-19, r.X, Tolerance);
        Assert.Equal(-3, r.Y, Tolerance);
        Assert.Equal(479 - (-19), r.Width, Tolerance);
        Assert.Equal(57 - (-3), r.Height, Tolerance);
    }

    [Fact]
    public void ScreenCaptureRect_SnapsToPhysicalPixelGrid_AtFractionalDpi()
    {
        // At 150 % the grid step is 1/1.5 DIP: 10.4 × 1.5 = 15.6 → floor 15 → 10.0;
        // (10.4 + 20) × 1.5 = 45.6 → ceil 46 → 30.666…
        var m = Matrix.Identity;
        m.Translate(0.4, 0.4);
        var r = RenderTargetDrawingContext.ComputeScreenEffectCaptureRect(
            new Rect(10, 10, 20, 20), m, 1.5, 1.5);

        Assert.Equal(15.0 / 1.5, r.X, Tolerance);
        Assert.Equal(15.0 / 1.5, r.Y, Tolerance);
        Assert.Equal(46.0 / 1.5 - 15.0 / 1.5, r.Width, Tolerance);
        Assert.Equal(46.0 / 1.5 - 15.0 / 1.5, r.Height, Tolerance);
        // Origin lands on the physical grid → the composite samples texel centres.
        Assert.Equal(0.0, (r.X * 1.5) % 1.0, Tolerance);
    }

    [Fact]
    public void ScreenCaptureRect_Rotation_UsesAxisAlignedBoundsOfRotatedRect()
    {
        // 90° about the origin: (x, y) → (-y, x). Rect (10,0)-(30,10) →
        // x ∈ [-10, 0], y ∈ [10, 30]. cos(90°) is not exactly 0 in double, so
        // the outward pixel snap may grow the box by one pixel on a side —
        // harmless over-capture; assert containment plus the 1px bound.
        var m = Matrix.Identity;
        m.Rotate(90);
        var r = RenderTargetDrawingContext.ComputeScreenEffectCaptureRect(
            new Rect(10, 0, 20, 10), m, 1.0, 1.0);

        Assert.InRange(r.X, -11, -10);
        Assert.InRange(r.Y, 9, 10);
        Assert.InRange(r.X + r.Width, 0, 1);
        Assert.InRange(r.Y + r.Height, 30, 31);
    }

    [Fact]
    public void TransformScales_ReportPerAxisMagnitudesAndAverage()
    {
        RenderTargetDrawingContext.GetTransformScales(new Matrix(2, 0, 0, 3, 5, 7),
            out var sx, out var sy, out var avg);
        Assert.Equal(2, sx, Tolerance);
        Assert.Equal(3, sy, Tolerance);
        Assert.Equal(2.5, avg, Tolerance);

        var rot = Matrix.Identity;
        rot.Rotate(37);
        RenderTargetDrawingContext.GetTransformScales(rot, out sx, out sy, out avg);
        Assert.Equal(1, sx, 1e-9);
        Assert.Equal(1, sy, 1e-9);
        Assert.Equal(1, avg, 1e-9);

        // A flip keeps a positive magnitude (the AABB handles the mirroring).
        RenderTargetDrawingContext.GetTransformScales(new Matrix(-1, 0, 0, 1, 0, 0),
            out sx, out sy, out avg);
        Assert.Equal(1, sx, Tolerance);
        Assert.Equal(1, sy, Tolerance);
    }

    // ── live drawing-context state ────────────────────────────────────────

    private static void RunWithDrawingContext(Action<RenderTarget, RenderTargetDrawingContext> body,
        RenderBackend backend = RenderBackend.Auto)
    {
        const int width = 256;
        const int height = 256;

        // Backend Auto (platform default, Software fallback on a GPU-less host):
        // the assertions below read managed state only; the native calls made
        // along the way must simply not fail the frame.
        using var window = new HiddenNativeWindow(width, height);
        using var context = backend == RenderBackend.Auto ? new RenderContext() : new RenderContext(backend);
        if (backend != RenderBackend.Auto)
        {
            // The ctor silently falls back when the requested backend cannot
            // materialize — a gated test must really run on what it asked for.
            Assert.Equal(backend, context.Backend);
        }
        using var renderTarget = context.CreateRenderTarget(window.Hwnd, width, height);
        Assert.True(renderTarget.IsValid);

        Assert.True(renderTarget.TryBeginDraw());
        var drawingContext = new RenderTargetDrawingContext(renderTarget, context);
        try
        {
            body(renderTarget, drawingContext);
        }
        finally
        {
            drawingContext.Close();
            Assert.Equal(JaliumResult.Ok, renderTarget.TryEndDraw());
        }
    }

    [Fact]
    public void BeginEndCapture_AtIdentity_RecordsUntransformedOrigin()
    {
        RunWithDrawingContext((_, dc) =>
        {
            dc.BeginEffectCapture(5f, 6f, 70f, 80f);
            dc.EndEffectCapture();

            var frame = dc.LastEndedEffectCaptureForTests;
            Assert.False(frame.Transformed);
            Assert.Equal(5, frame.ScreenX, Tolerance);
            Assert.Equal(6, frame.ScreenY, Tolerance);
            Assert.Equal(70, frame.ScreenWidth, Tolerance);
            Assert.Equal(80, frame.ScreenHeight, Tolerance);
        });
    }

    [Fact]
    public void BeginEndCapture_UnderScale_RecordsScreenSpaceOrigin()
    {
        RunWithDrawingContext((rt, dc) =>
        {
            ((ITransformDrawingContext)dc).PushTransform(new ScaleTransform(2, 2), 0, 0);
            dc.BeginEffectCapture(10f, 12f, 20f, 20f);
            dc.EndEffectCapture();
            ((ITransformDrawingContext)dc).PopTransform();

            var frame = dc.LastEndedEffectCaptureForTests;
            Assert.True(frame.Transformed);
            var expected = RenderTargetDrawingContext.ComputeScreenEffectCaptureRect(
                new Rect(10, 12, 20, 20), new Matrix(2, 0, 0, 2, 0, 0),
                rt.DpiScaleX, rt.DpiScaleY);
            Assert.Equal(expected.X, frame.ScreenX, Tolerance);
            Assert.Equal(expected.Y, frame.ScreenY, Tolerance);
            Assert.Equal(expected.Width, frame.ScreenWidth, Tolerance);
            Assert.Equal(expected.Height, frame.ScreenHeight, Tolerance);
        });
    }

    [Fact]
    public void NestedCaptures_UnwindToTheirOwnFrames()
    {
        RunWithDrawingContext((_, dc) =>
        {
            dc.BeginEffectCapture(0f, 0f, 100f, 100f);
            ((ITransformDrawingContext)dc).PushTransform(new ScaleTransform(2, 2), 0, 0);
            dc.BeginEffectCapture(30f, 40f, 10f, 10f);
            dc.EndEffectCapture();
            Assert.True(dc.LastEndedEffectCaptureForTests.Transformed);
            ((ITransformDrawingContext)dc).PopTransform();

            dc.EndEffectCapture();
            var outer = dc.LastEndedEffectCaptureForTests;
            Assert.False(outer.Transformed);
            Assert.Equal(0, outer.ScreenX, Tolerance);
            Assert.Equal(0, outer.ScreenY, Tolerance);
        });
    }

    [Fact]
    public void ApplyElementEffect_UnderTransform_LeavesManagedTransformStateIntact()
    {
        RunWithDrawingContext((_, dc) =>
        {
            const double eps = 0.25; // RenderTargetDrawingContext.ClipCullEpsilon
            dc.PushDirtyRegionClip(new Rect(0, 0, 200, 200));
            ((ITransformDrawingContext)dc).PushTransform(new ScaleTransform(1.08, 1.08), 230, 27);

            var before = dc.CurrentClipBounds;

            // Capture + apply exactly like Visual.RenderDirect: the inverse push
            // around the native composite must be balanced and must not disturb
            // the managed matrix mirror (CurrentClipBounds maps through it).
            dc.BeginEffectCapture(-8f, -8f, 476f, 70f);
            dc.EndEffectCapture();
            dc.ApplyElementEffect(new DropShadowEffect { BlurRadius = 8, ShadowDepth = 3 },
                0f, 0f, 460f, 54f, -8f, -8f, 27f, 27f, 27f, 27f);

            var after = dc.CurrentClipBounds;
            Assert.True(before.HasValue && after.HasValue);
            Assert.Equal(before.Value.X, after.Value.X, 1e-9);
            Assert.Equal(before.Value.Y, after.Value.Y, 1e-9);
            Assert.Equal(before.Value.Width, after.Value.Width, 1e-9);
            Assert.Equal(before.Value.Height, after.Value.Height, 1e-9);

            ((ITransformDrawingContext)dc).PopTransform();
            var popped = dc.CurrentClipBounds;
            Assert.True(popped.HasValue);
            Assert.Equal(-eps, popped.Value.X, 1e-9);
            Assert.Equal(200 + 2 * eps, popped.Value.Width, 1e-9);
            dc.PopDirtyRegionClip();
        });
    }

    /// <summary>
    /// End to end through the visual tree, in the shape the designer zoom and the
    /// home-page hover zoom share: an ancestor carrying RenderTransform=ScaleTransform
    /// with a shadowed element inside. Visual.RenderChildVisualInline pushes the
    /// transform, RenderDirect opens the capture, and the drawing context must record
    /// the SCREEN-space capture origin (i.e. the transformed rect) for the composite.
    /// </summary>
    [Fact]
    public void ShadowedElement_UnderScaledAncestor_CapturesInScreenSpaceThroughVisualRender()
        => RunWithDrawingContext(RenderScaledShadowedTreeAndAssert);

    /// <summary>
    /// Same tree on each real backend the host offers. D3D12 receives the screen-space
    /// AABB at BeginEffectCapture; Vulkan keeps the untransformed rect (it maps it
    /// itself) — both must complete the frame with the composite under the inverse push.
    /// </summary>
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void ShadowedElement_UnderScaledAncestor_RendersOnD3D12()
        => RunWithDrawingContext(RenderScaledShadowedTreeAndAssert, RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void ShadowedElement_UnderScaledAncestor_RendersOnVulkan()
        => RunWithDrawingContext(RenderScaledShadowedTreeAndAssert, RenderBackend.Vulkan);

    private static void RenderScaledShadowedTreeAndAssert(RenderTarget rt, RenderTargetDrawingContext dc)
    {
        var shadowed = new WhiteRectElement
        {
            Width = 80,
            Height = 48,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 3 },
        };
        var scaledHost = new Grid { Width = 80, Height = 48, RenderTransform = new ScaleTransform(2, 2) };
        scaledHost.Children.Add(shadowed);
        var root = new Grid { Width = 200, Height = 200 };
        root.Children.Add(scaledHost);
        root.Measure(new Size(200, 200));
        root.Arrange(new Rect(0, 0, 200, 200));

        root.Render(dc);

        var frame = dc.LastEndedEffectCaptureForTests;
        Assert.True(frame.Transformed);

        // Visual.RenderDirect snaps the untransformed capture rect (element rect
        // inflated by the effect padding, here 8 + 3 = 11 on every side, floored)
        // and this context maps it through the ×2 matrix: the recorded origin is
        // the transformed AABB origin, not the untransformed one.
        var padding = ((IEffect)shadowed.Effect!).EffectPadding;
        // FrameworkElement.TransformToAncestor returns the transform-aware position of
        // the local origin; a scale about (0,0) leaves that origin fixed, so this is
        // the layout offset the drawing context saw when the transform was pushed.
        var arranged = scaledHost.TransformToAncestor(root);
        var untransformed = new Rect(
            Math.Floor(arranged.X - padding.Left), Math.Floor(arranged.Y - padding.Top),
            Math.Ceiling(arranged.X + 80 + padding.Right) - Math.Floor(arranged.X - padding.Left),
            Math.Ceiling(arranged.Y + 48 + padding.Bottom) - Math.Floor(arranged.Y - padding.Top));
        var m = Matrix.Identity;
        m.ScaleAt(2, 2, arranged.X, arranged.Y);
        var expected = RenderTargetDrawingContext.ComputeScreenEffectCaptureRect(
            untransformed, m, rt.DpiScaleX, rt.DpiScaleY);
        Assert.Equal(expected.X, frame.ScreenX, 1e-6);
        Assert.Equal(expected.Y, frame.ScreenY, 1e-6);
        Assert.Equal(expected.Width, frame.ScreenWidth, 1e-6);
        Assert.Equal(expected.Height, frame.ScreenHeight, 1e-6);
        Assert.True(Math.Abs(untransformed.X - frame.ScreenX) > 1e-6,
            "the recorded capture origin must be the transformed one");
    }

    private sealed class WhiteRectElement : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawingContext) =>
            drawingContext.DrawRectangle(
                Brushes.White,
                null,
                new Rect(0, 0, RenderSize.Width, RenderSize.Height));
    }

    [Fact]
    public void ApplyElementEffect_UnderTransform_HandlesEveryEffectKindWithoutFailingTheFrame()
    {
        RunWithDrawingContext((_, dc) =>
        {
            ((ITransformDrawingContext)dc).PushTransform(new ScaleTransform(1.5, 1.5), 50, 50);

            IEffect[] effects =
            {
                new DropShadowEffect(),
                new BlurEffect { Radius = 6 },
                new OuterGlowEffect(),
                new InnerShadowEffect(),
                new EmbossEffect(),
                new ColorMatrixEffect(),
                new EffectGroup { Children = { new DropShadowEffect(), new BlurEffect { Radius = 3 } } },
            };
            foreach (var effect in effects)
            {
                dc.BeginEffectCapture(-10f, -10f, 120f, 120f);
                dc.EndEffectCapture();
                dc.ApplyElementEffect(effect, 0f, 0f, 100f, 100f, -10f, -10f, 4f, 4f, 4f, 4f);
            }

            ((ITransformDrawingContext)dc).PopTransform();
        });
    }
}
