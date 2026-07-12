using Jalium.UI.Controls;
using Jalium.UI.Media.Effects;
using DrawingContext = Jalium.UI.Media.DrawingContext;
using DrawingContextAdapter = Jalium.UI.Media.DrawingContextAdapter;
using Pen = Jalium.UI.Media.Pen;
using Brush = Jalium.UI.Media.Brush;
using Geometry = Jalium.UI.Media.Geometry;
using Transform = Jalium.UI.Media.Transform;
using ImageSource = Jalium.UI.Media.ImageSource;
using FormattedText = Jalium.UI.Media.FormattedText;
using IBackdropEffect = Jalium.UI.IBackdropEffect;
using RectangleGeometry = Jalium.UI.Media.RectangleGeometry;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression guard for the D3D12 stencil-path offscreen-capture leak fixed in
/// <c>exitPathMode()</c> (src/native/jalium.native.d3d12/src/d3d12_direct_renderer.cpp):
/// a <c>FillPath</c>/<c>Geometry</c> drawn inside an element-effect capture used to
/// resolve+blit onto the swap-chain back buffer (displaced/shrunk) instead of the
/// active offscreen/retained capture texture.
/// <para>
/// The defect itself lives entirely in native C++ and is only observable via
/// GPU pixel readback — there is no readback ABI in the suite today and no
/// real-GPU pixel-assertion precedent (the closest is the gated, resource-only
/// VulkanBackendSmokeTests). So this test deliberately does NOT assert pixels.
/// </para>
/// <para>
/// What it DOES pin is the managed-layer contract the native fix relies on:
/// when an element carries an <see cref="Jalium.UI.UIElement.Effect"/>, any vector
/// (Path/Geometry) its <c>OnRender</c> emits must be recorded strictly INSIDE the
/// <c>BeginEffectCapture</c>/<c>EndEffectCapture</c> window — so the native
/// renderer sees the stencil-path batch while the offscreen RT is bound, which is
/// the precondition for the capture-aware blit to route it into the capture
/// texture. If a future refactor let those draws escape the capture scope, the
/// native fix would silently stop covering vector content. A full pixel-level
/// guard is tracked as a follow-up GPU integration test.
/// </para>
/// </summary>
// Joins the serialized "Application" collection: the framework's
// DependencyProperty.GetMetadata uses a non-thread-safe static dictionary, so
// touching DPs (Width/Effect/Visibility) concurrently with other collections
// can corrupt it. This is the suite's established isolation pattern.
[Collection("Application")]
public class PathDuringEffectCaptureScopeTests
{
    [Fact]
    public void PathDraw_UnderElementEffect_IsRecordedInsideCaptureScope()
    {
        var element = new GeometryElement
        {
            Width = 40,
            Height = 20,
            Effect = new BlurEffect(3.0),
        };

        element.Measure(new Size(40, 20));
        element.Arrange(new Rect(0, 0, 40, 20));

        var context = new RecordingEffectContext { Offset = new Point(12, 8) };
        element.Render(context);

        // Exactly one capture window opened/closed and one effect applied.
        Assert.Equal(1, context.Events.Count(e => e == Begin));
        Assert.Equal(1, context.Events.Count(e => e == End));
        Assert.Equal(1, context.Events.Count(e => e == Apply));

        // The element actually emitted a path/geometry draw.
        Assert.Contains(Path, context.Events);

        int begin = context.Events.IndexOf(Begin);
        int end = context.Events.IndexOf(End);
        int apply = context.Events.IndexOf(Apply);

        // Every path draw lands strictly between begin and end — i.e. it is part
        // of the captured subtree (the native path batch is recorded while the
        // offscreen RT is bound), never after the capture closes.
        for (int i = 0; i < context.Events.Count; i++)
        {
            if (context.Events[i] == Path)
            {
                Assert.True(begin < i && i < end,
                    $"geometry draw at index {i} escaped the effect-capture scope (begin={begin}, end={end})");
            }
        }

        // The effect composites after the capture is closed.
        Assert.True(end < apply, "ApplyElementEffect must run after EndEffectCapture");
    }

    private const string Begin = "begin";
    private const string End = "end";
    private const string Apply = "apply";
    private const string Path = "path";

    /// <summary>Element whose content is a single vector geometry — the managed
    /// stand-in for native FillPath/AddStencilPath content under an effect.</summary>
    private sealed class GeometryElement : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawGeometry(null, null, new RectangleGeometry { Rect = new Rect(0, 0, 40, 20) });
        }
    }

    /// <summary>Recording context modeled on EffectCaptureBoundsTests; logs the
    /// ordered sequence of capture/draw/apply events instead of any bounds.</summary>
    private sealed class RecordingEffectContext : DrawingContextAdapter, IOffsetDrawingContext, IEffectDrawingContext
    {
        public Point Offset { get; set; }

        public List<string> Events { get; } = [];

        public void BeginEffectCapture(float x, float y, float w, float h) => Events.Add(Begin);

        public void EndEffectCapture() => Events.Add(End);

        public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
            float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
            => Events.Add(Apply);

        public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
            float captureOriginX = 0, float captureOriginY = 0,
            float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
            // Visual.RenderDirect dispatches to this 11-arg variant.
            => Events.Add(Apply);

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => Events.Add(Path);

        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
