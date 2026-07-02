using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Tests.RenderThread;

/// <summary>
/// A pure-managed <see cref="DrawingContext"/> that records the ordered,
/// value-based call stream so tests can assert that a whole-frame
/// record→replay produces the SAME sequence as a direct <c>Render</c> walk
/// (the increment-1 "pixel-identical" proxy for JALIUM_RENDER_THREAD).
/// </summary>
/// <remarks>
/// <para>
/// It mirrors <c>DrawingRecorder</c>'s normalisation so the two streams line up:
/// <list type="bullet">
///   <item><see cref="IClipBoundsDrawingContext.CurrentClipBounds"/> is null, so
///   the walk makes identical viewport-cull decisions as the recorder (which
///   also returns null in whole-frame mode).</item>
///   <item><see cref="IOpacityDrawingContext.PopOpacity"/> collapses to
///   <see cref="Pop"/>, and <see cref="ITransformDrawingContext.PushTransform(Transform, double, double)"/>
///   composes the origin into a matrix and forwards to <see cref="PushTransform(Transform)"/>
///   — exactly as the recorder does — so direct vs replayed tokens match.</item>
/// </list>
/// </para>
/// <para>
/// It deliberately does NOT implement <c>ICacheableDrawingContext</c>, so the
/// direct walk runs <c>OnRender</c> straight into this sink (no nested
/// per-visual cache) — the same dispatch the whole-frame recorder sees.
/// Payloads are tokenised by VALUE (brush colour, pen thickness, geometry
/// bounds, image identity) so DrawingObjectPool canonicalisation does not cause
/// a false mismatch.
/// </para>
/// </remarks>
internal sealed class RecordingRenderSink : DrawingContext,
    IOffsetDrawingContext, IClipBoundsDrawingContext,
    IOpacityDrawingContext, ITransformDrawingContext, IEffectDrawingContext
{
    public List<string> Events { get; } = new();

    private readonly Dictionary<object, int> _ids = new();
    private int Id(object o)
    {
        if (!_ids.TryGetValue(o, out var id)) { id = _ids.Count; _ids[o] = id; }
        return id;
    }

    private static string F(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
    private static string B(Brush? b) => b switch
    {
        null => "none",
        SolidColorBrush s => $"#{s.Color.A:X2}{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}",
        GradientBrush g => $"grad[{string.Join(";", g.GradientStops.Select(s => $"{s.Color.A:X2}{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}@{F(s.Offset)}"))}]",
        _ => b.GetType().Name,
    };
    private static string P(Pen? p) => p is null ? "none" : $"{B(p.Brush)}@{F(p.Thickness)}";
    private static string R(Rect r) => $"({F(r.X)},{F(r.Y)},{F(r.Width)},{F(r.Height)})";
    private static string M(Transform t)
    {
        var m = t.Value;
        return $"[{F(m.M11)},{F(m.M12)},{F(m.M21)},{F(m.M22)},{F(m.OffsetX)},{F(m.OffsetY)}]";
    }

    // ── IOffsetDrawingContext ────────────────────────────────────────────
    private Point _offset;
    public Point Offset
    {
        get => _offset;
        set { _offset = value; Events.Add($"SetOffset({F(value.X)},{F(value.Y)})"); }
    }

    // ── IClipBoundsDrawingContext ────────────────────────────────────────
    // Null mirrors the whole-frame recorder, so cull decisions are identical.
    public Rect? CurrentClipBounds => null;

    // ── DrawingContext abstract/virtual draw surface ─────────────────────
    public override void DrawLine(Pen pen, Point p0, Point p1) =>
        Events.Add($"DrawLine pen={P(pen)} {F(p0.X)},{F(p0.Y)}->{F(p1.X)},{F(p1.Y)}");

    public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) =>
        Events.Add($"DrawRectangle b={B(brush)} p={P(pen)} {R(rectangle)}");

    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) =>
        Events.Add($"DrawRoundedRectangle b={B(brush)} p={P(pen)} {R(rectangle)} rx={F(radiusX)} ry={F(radiusY)}");

    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius cr) =>
        Events.Add($"DrawRoundedRectangleCorner b={B(brush)} p={P(pen)} {R(rectangle)} {F(cr.TopLeft)},{F(cr.TopRight)},{F(cr.BottomRight)},{F(cr.BottomLeft)}");

    public override void DrawContentBorder(Brush? fillBrush, Pen? strokePen, Rect rectangle,
        double bottomLeftRadius, double bottomRightRadius) =>
        Events.Add($"DrawContentBorder f={B(fillBrush)} s={P(strokePen)} {R(rectangle)} bl={F(bottomLeftRadius)} br={F(bottomRightRadius)}");

    public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) =>
        Events.Add($"DrawEllipse b={B(brush)} p={P(pen)} {F(center.X)},{F(center.Y)} rx={F(radiusX)} ry={F(radiusY)}");

    public override void DrawPoints(Brush? brush, ReadOnlySpan<Point> centers, double radius) =>
        Events.Add($"DrawPoints b={B(brush)} n={centers.Length} r={F(radius)}");

    public override void DrawLines(Pen pen, ReadOnlySpan<Point> endpoints) =>
        Events.Add($"DrawLines pen={P(pen)} n={endpoints.Length}");

    public override void DrawText(FormattedText formattedText, Point origin) =>
        Events.Add($"DrawText '{formattedText.Text}' @{F(origin.X)},{F(origin.Y)}");

    public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) =>
        Events.Add($"DrawGeometry b={B(brush)} p={P(pen)} bounds={R(geometry.Bounds)}");

    public override void DrawImage(ImageSource imageSource, Rect rectangle) =>
        Events.Add($"DrawImage #{Id(imageSource)} {R(rectangle)}");

    public override void DrawImage(ImageSource imageSource, Rect rectangle, BitmapScalingMode scalingMode) =>
        Events.Add($"DrawImage #{Id(imageSource)} {R(rectangle)} mode={(int)scalingMode}");

    public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) =>
        Events.Add($"DrawBackdropEffect {R(rectangle)}");

    public override void DrawLiquidGlass(LiquidGlassParameters parameters) =>
        Events.Add($"DrawLiquidGlass {R(parameters.Rectangle)}");

    public override void SetShapeType(int type, float exponent) =>
        Events.Add($"SetShapeType {type} n={F(exponent)}");

    public override void PushTransform(Transform transform) =>
        Events.Add($"PushTransform {M(transform)}");

    public override void PushClip(Geometry clipGeometry) =>
        Events.Add($"PushClip bounds={R(clipGeometry.Bounds)}");

    public override void PushOpacity(double opacity) =>
        Events.Add($"PushOpacity {F(opacity)}");

    public override void Pop() => Events.Add("Pop");

    public override void PushEffect(IEffect effect, Rect captureBounds) =>
        Events.Add($"PushEffect {R(captureBounds)}");

    public override void PopEffect() => Events.Add("PopEffect");

    public override void Close() { }

    // ── IOpacityDrawingContext ───────────────────────────────────────────
    public void PopOpacity() => Pop();   // mirror DrawingRecorder (PopOpacity => Pop)

    // ── ITransformDrawingContext ─────────────────────────────────────────
    public void PushTransform(Transform transform, double originX, double originY)
    {
        if (originX != 0 || originY != 0)
        {
            // Compose T(-origin) * transform * T(+origin) — identical to
            // DrawingRecorder / RenderTargetDrawingContext so tokens match.
            var m = transform.Value;
            var pre = new Matrix(1, 0, 0, 1, -originX, -originY);
            var post = new Matrix(1, 0, 0, 1, originX, originY);
            PushTransform(new MatrixTransform(Matrix.Multiply(Matrix.Multiply(pre, m), post)));
        }
        else
        {
            PushTransform(transform);
        }
    }

    public void PopTransform() => Pop();

    // ── IEffectDrawingContext ────────────────────────────────────────────
    public void BeginEffectCapture(float x, float y, float w, float h) =>
        Events.Add($"BeginEffectCapture {F(x)},{F(y)},{F(w)},{F(h)}");

    public void EndEffectCapture() => Events.Add("EndEffectCapture");

    public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
        float captureOriginX = 0, float captureOriginY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0) =>
        Events.Add($"ApplyElementEffect {F(x)},{F(y)},{F(w)},{F(h)}");
}
