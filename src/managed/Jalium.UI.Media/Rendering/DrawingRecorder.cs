using System;
using System.Collections.Generic;

namespace Jalium.UI.Media.Rendering;

/// <summary>
/// <see cref="DrawingContext"/> implementation that captures every draw /
/// push / pop into a command list instead of issuing them live. Produced by
/// <see cref="MediaRenderCacheHost.CreateRecorder"/>, consumed by
/// <see cref="MediaRenderCacheHost.FinishRecord"/>.
/// </summary>
/// <remarks>
/// <para>
/// The recorder mirrors but does not forward to the live drawing context.
/// Ambient state read during <c>OnRender</c> — <c>Offset</c>, clip bounds —
/// is proxied through to the live context so user code that queries it
/// still observes correct values. <c>PushTransform</c> / <c>PushClip</c> /
/// <c>PushOpacity</c> and their <c>Pop</c> counterparts are recorded as
/// commands; they do not mutate the live context at record time. Effects
/// (<c>PushEffect</c> / <c>PopEffect</c>) are likewise recorded.
/// </para>
/// <para>
/// In parallel with command recording the recorder drives a
/// <see cref="BoundsAccumulator"/> that computes the world-space bounding
/// box of everything drawn. The bounds are stored on the committed
/// <see cref="Drawing"/> and used by <see cref="DrawingReplayer"/> to
/// short-circuit replay when the cached bounds don't intersect the target
/// context's current clip — mostly useful for oversized custom canvases
/// (Jalium.One's NodeCanvas / BlockCanvas) that draw content far outside
/// the visible viewport.
/// </para>
/// <para>
/// Recorders are pooled by the host. After <see cref="Commit"/> returns the
/// caller must release the recorder through the host's
/// <c>FinishRecord</c> entrypoint so its command list is cleared and it is
/// returned to the pool.
/// </para>
/// </remarks>
internal sealed class DrawingRecorder : DrawingContext, IOffsetDrawingContext, IClipBoundsDrawingContext
{
    private readonly List<DrawCommand> _commands = new(32);
    private readonly BoundsAccumulator _bounds = new();

    private IOffsetDrawingContext? _offsetProxy;
    private IClipBoundsDrawingContext? _clipBoundsProxy;

    /// <summary>
    /// Prepares the recorder for a fresh recording scope. Clears any
    /// residual command / bounds state and snapshots the Core-side ambient
    /// interfaces from <paramref name="target"/> so ambient reads during
    /// user <c>OnRender</c> observe the live render's state.
    /// </summary>
    public void Bind(object target)
    {
        _commands.Clear();
        _bounds.Reset();
        _offsetProxy = target as IOffsetDrawingContext;
        _clipBoundsProxy = target as IClipBoundsDrawingContext;
    }

    /// <summary>
    /// Finalizes the current recording and returns an immutable
    /// <see cref="Drawing"/> with its bounds populated (or null when the
    /// recording contains content whose extent could not be bounded).
    /// </summary>
    public Drawing Commit()
    {
        if (_commands.Count == 0)
        {
            _offsetProxy = null;
            _clipBoundsProxy = null;
            _bounds.Reset();
            return Drawing.Empty;
        }

        var arr = _commands.ToArray();
        var drawingBounds = _bounds.GetBounds();

        _commands.Clear();
        _bounds.Reset();
        _offsetProxy = null;
        _clipBoundsProxy = null;

        return new Drawing(arr, arr.Length, drawingBounds);
    }

    /// <summary>
    /// Alternate release path for recorders that are abandoned without a
    /// successful <see cref="Commit"/> (e.g. the caller threw).
    /// </summary>
    public void Reset()
    {
        _commands.Clear();
        _bounds.Reset();
        _offsetProxy = null;
        _clipBoundsProxy = null;
    }

    // ── Ambient-state proxies ────────────────────────────────────────────

    Point IOffsetDrawingContext.Offset
    {
        get => _offsetProxy?.Offset ?? default;
        set
        {
            if (_offsetProxy != null)
            {
                _offsetProxy.Offset = value;
            }
        }
    }

    Rect? IClipBoundsDrawingContext.CurrentClipBounds => _clipBoundsProxy?.CurrentClipBounds;

    // ── Draw calls ──────────────────────────────────────────────────────
    //
    // Each Draw* method canonicalises its Brush / Pen / FormattedText
    // arguments through DrawingObjectPool before recording, then extends
    // the bounds accumulator with the draw's local-space extent (pen
    // thickness half-spilled on each side for stroked primitives).

    public override void DrawLine(Pen pen, Point point0, Point point1)
    {
        var canonicalPen = DrawingObjectPool.CanonicalizePen(pen)!;
        _commands.Add(DrawCommand.Line(canonicalPen, point0, point1));

        var minX = Math.Min(point0.X, point1.X);
        var minY = Math.Min(point0.Y, point1.Y);
        var maxX = Math.Max(point0.X, point1.X);
        var maxY = Math.Max(point0.Y, point1.Y);
        _bounds.AccumulateRect(
            new Rect(minX, minY, maxX - minX, maxY - minY),
            strokeSlop: StrokeSlop(canonicalPen));
    }

    public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
    {
        var canonicalBrush = DrawingObjectPool.CanonicalizeBrush(brush);
        var canonicalPen = DrawingObjectPool.CanonicalizePen(pen);
        _commands.Add(DrawCommand.Rectangle(canonicalBrush, canonicalPen, rectangle));
        _bounds.AccumulateRect(rectangle, StrokeSlop(canonicalPen));
    }

    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        var canonicalBrush = DrawingObjectPool.CanonicalizeBrush(brush);
        var canonicalPen = DrawingObjectPool.CanonicalizePen(pen);
        _commands.Add(DrawCommand.RoundedRectangle(canonicalBrush, canonicalPen, rectangle, radiusX, radiusY));
        _bounds.AccumulateRect(rectangle, StrokeSlop(canonicalPen));
    }

    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius cornerRadius)
    {
        // Avoid the base-class fast-path that calls DrawRectangle /
        // DrawGeometry and loses the "this was originally a rounded rect
        // with a CornerRadius" intent. Record the high-level call verbatim
        // so replay re-dispatches to the target context's own fast paths.
        var canonicalBrush = DrawingObjectPool.CanonicalizeBrush(brush);
        var canonicalPen = DrawingObjectPool.CanonicalizePen(pen);
        _commands.Add(DrawCommand.RoundedRectangleCorner(canonicalBrush, canonicalPen, rectangle, cornerRadius));
        _bounds.AccumulateRect(rectangle, StrokeSlop(canonicalPen));
    }

    public override void DrawContentBorder(Brush? fillBrush, Pen? strokePen, Rect rectangle,
        double bottomLeftRadius, double bottomRightRadius)
    {
        var canonicalFill = DrawingObjectPool.CanonicalizeBrush(fillBrush);
        var canonicalStroke = DrawingObjectPool.CanonicalizePen(strokePen);
        _commands.Add(DrawCommand.ContentBorder(canonicalFill, canonicalStroke, rectangle, bottomLeftRadius, bottomRightRadius));
        _bounds.AccumulateRect(rectangle, StrokeSlop(canonicalStroke));
    }

    public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
    {
        var canonicalBrush = DrawingObjectPool.CanonicalizeBrush(brush);
        var canonicalPen = DrawingObjectPool.CanonicalizePen(pen);
        _commands.Add(DrawCommand.Ellipse(canonicalBrush, canonicalPen, center, radiusX, radiusY));
        var rect = new Rect(center.X - radiusX, center.Y - radiusY, 2 * radiusX, 2 * radiusY);
        _bounds.AccumulateRect(rect, StrokeSlop(canonicalPen));
    }

    public override void DrawPoints(Brush? brush, ReadOnlySpan<Point> centers, double radius)
    {
        if (brush is null || centers.IsEmpty || !(radius > 0))
        {
            return;
        }

        // Materialise the span so the command can live past the caller's
        // stack frame. Canonicalise the brush so replay hits the native
        // brush cache; the array is owned outright by the recorded command.
        var canonicalBrush = DrawingObjectPool.CanonicalizeBrush(brush)!;
        var snapshot = centers.ToArray();
        _commands.Add(DrawCommand.Points(canonicalBrush, snapshot, radius));

        // Accumulate bounds: union of all (centre ± radius) rects. For a
        // dense grid this is still O(points), but the alternative — losing
        // bounds and disabling AABB cull — is worse when the batch fills
        // most of the viewport anyway.
        for (int i = 0; i < snapshot.Length; i++)
        {
            var p = snapshot[i];
            _bounds.AccumulateRect(
                new Rect(p.X - radius, p.Y - radius, 2 * radius, 2 * radius));
        }
    }

    public override void DrawLines(Pen pen, ReadOnlySpan<Point> endpoints)
    {
        if (pen is null || endpoints.Length < 2)
        {
            return;
        }

        var canonicalPen = DrawingObjectPool.CanonicalizePen(pen)!;
        var snapshot = endpoints.ToArray();
        _commands.Add(DrawCommand.Lines(canonicalPen, snapshot));

        // Bounds: AABB of all endpoints + pen-half-thickness slop.
        var slop = StrokeSlop(canonicalPen);
        var pairs = snapshot.Length / 2;
        for (int i = 0; i < pairs; i++)
        {
            var a = snapshot[2 * i];
            var b = snapshot[2 * i + 1];
            var minX = Math.Min(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxX = Math.Max(a.X, b.X);
            var maxY = Math.Max(a.Y, b.Y);
            _bounds.AccumulateRect(
                new Rect(minX, minY, maxX - minX, maxY - minY),
                slop);
        }
    }

    public override void DrawText(FormattedText formattedText, Point origin)
    {
        var canonical = DrawingObjectPool.CanonicalizeFormattedText(formattedText);
        _commands.Add(DrawCommand.Text(canonical, origin));

        // Text layout bounds aren't known until DirectWrite lays out the
        // glyphs. Use the wrap box (MaxTextWidth × MaxTextHeight) as a
        // conservative upper bound. If the wrap box is effectively
        // unlimited, bounds are unknowable — bail out cleanly so the
        // Drawing disables replay-time AABB culling for this recording.
        var w = canonical.MaxTextWidth;
        var h = canonical.MaxTextHeight;
        const double UnboundedTextThreshold = 1e6;
        if (double.IsInfinity(w) || double.IsNaN(w) || w > UnboundedTextThreshold ||
            double.IsInfinity(h) || double.IsNaN(h) || h > UnboundedTextThreshold)
        {
            _bounds.MarkUnknown();
        }
        else
        {
            _bounds.AccumulateRect(new Rect(origin.X, origin.Y, w, h));
        }
    }

    public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
    {
        var canonicalBrush = DrawingObjectPool.CanonicalizeBrush(brush);
        var canonicalPen = DrawingObjectPool.CanonicalizePen(pen);
        _commands.Add(DrawCommand.GeometryCmd(canonicalBrush, canonicalPen, geometry));
        _bounds.AccumulateRect(geometry.Bounds, StrokeSlop(canonicalPen));
    }


    public override void DrawImage(ImageSource imageSource, Rect rectangle)
    {
        _commands.Add(DrawCommand.Image(imageSource, rectangle, BitmapScalingMode.Unspecified));
        _bounds.AccumulateRect(rectangle);
    }

    public override void DrawImage(ImageSource imageSource, Rect rectangle, BitmapScalingMode scalingMode)
    {
        _commands.Add(DrawCommand.Image(imageSource, rectangle, scalingMode));
        _bounds.AccumulateRect(rectangle);
    }

    public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
    {
        _commands.Add(DrawCommand.BackdropEffect(effect, rectangle, cornerRadius));
        _bounds.AccumulateRect(rectangle);
    }

    public override void DrawLiquidGlass(LiquidGlassParameters parameters)
    {
        // The caller owns the LiquidGlassParameters instance they pass in,
        // and is free to mutate or reuse it across frames. Record a private
        // copy so a subsequent OnRender that rebuilds / mutates the caller's
        // object can't corrupt a replay that still references this command.
        // Neighbor data is likewise cloned — on the capture path Border.cs
        // fills a stackalloc'd span whose lifetime ends with OnRender.
        var captured = new LiquidGlassParameters
        {
            Rectangle = parameters.Rectangle,
            CornerRadius = parameters.CornerRadius,
            BlurRadius = parameters.BlurRadius,
            RefractionAmount = parameters.RefractionAmount,
            ChromaticAberration = parameters.ChromaticAberration,
            TintR = parameters.TintR,
            TintG = parameters.TintG,
            TintB = parameters.TintB,
            TintOpacity = parameters.TintOpacity,
            LightX = parameters.LightX,
            LightY = parameters.LightY,
            HighlightBoost = parameters.HighlightBoost,
            ShapeType = parameters.ShapeType,
            ShapeExponent = parameters.ShapeExponent,
            NeighborCount = parameters.NeighborCount,
            FusionRadius = parameters.FusionRadius,
            NeighborData = parameters.NeighborData is { } src
                ? (float[])src.Clone()
                : null,
        };
        _commands.Add(DrawCommand.LiquidGlass(captured));
        _bounds.AccumulateRect(captured.Rectangle);
    }

    // ── State stack ─────────────────────────────────────────────────────

    public override void PushTransform(Transform transform)
    {
        _commands.Add(DrawCommand.PushTransformCmd(transform));
        _bounds.PushTransform(transform);
    }

    public override void PushClip(Geometry clipGeometry)
    {
        _commands.Add(DrawCommand.PushClipCmd(clipGeometry));
        _bounds.PushClip(clipGeometry);
    }

    public override void PushOpacity(double opacity)
    {
        _commands.Add(DrawCommand.PushOpacityCmd(opacity));
        _bounds.PushOpacity();
    }

    public override void Pop()
    {
        _commands.Add(DrawCommand.PopCmd());
        _bounds.Pop();
    }

    public override void PushEffect(IEffect effect, Rect captureBounds)
    {
        _commands.Add(DrawCommand.PushEffectCmd(effect, captureBounds));
        // The captureBounds parameter tells us exactly how much area the
        // offscreen capture will cover, so contribute it directly. The
        // effect may expand that (shadow / glow padding) but callers are
        // expected to include effect padding in captureBounds already
        // (see Visual.RenderDirect where it adds eff.EffectPadding to the
        // element's size before BeginEffectCapture).
        _bounds.AccumulateRect(captureBounds);
    }

    public override void PopEffect()
    {
        _commands.Add(DrawCommand.PopEffectCmd());
        // PushEffect/PopEffect live on a separate stack from PushTransform
        // / PushClip / PushOpacity — they don't go through Pop(), so no
        // bounds-stack pop is needed here.
    }

    // ── Lifecycle ───────────────────────────────────────────────────────

    /// <summary>
    /// No-op. The recorder's lifecycle is driven by
    /// <see cref="MediaRenderCacheHost.FinishRecord"/>, not <c>IDisposable</c>.
    /// </summary>
    public override void Close()
    {
        // Intentionally empty. The host owns the commit/pool handshake.
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static double StrokeSlop(Pen? pen) =>
        pen is null ? 0 : Math.Max(0, pen.Thickness) / 2.0;
}
