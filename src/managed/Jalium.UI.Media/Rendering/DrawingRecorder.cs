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
internal sealed class DrawingRecorder : DrawingContextAdapter,
    IOffsetDrawingContext, IClipBoundsDrawingContext,
    IClipDrawingContext, IOpacityDrawingContext, ITransformDrawingContext, IEffectDrawingContext
{
    private readonly List<DrawCommand> _commands = new(32);
    private readonly BoundsAccumulator _bounds = new();

    private IOffsetDrawingContext? _offsetProxy;

    // Whole-frame mode (BindWholeFrame): capture the ENTIRE visual tree as a
    // self-contained command list with NO live target — Offset sets are RECORDED
    // as SetOffset commands (so Visual.RenderChildVisualInline's per-child offsets
    // are captured) instead of proxied, and bounds/clip culling is disabled.
    // Used by the render-thread path (record on UI thread, replay on render thread).
    private bool _wholeFrame;
    private Point _recordedOffset;
    private (bool recording, bool unrecordable) _wholeFrameSavedScope;

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
        _wholeFrame = false;
    }

    /// <summary>
    /// Prepares the recorder to capture an ENTIRE frame (the whole visual tree)
    /// as a self-contained command list with NO live target. Offset sets are
    /// recorded as <c>SetOffset</c> commands (not proxied to a live context), and
    /// bounds/clip culling is disabled (the recorded bounds would be meaningless
    /// once per-child offsets vary across the frame). Consumed via <see cref="Commit"/>.
    /// </summary>
    public void BindWholeFrame()
    {
        _commands.Clear();
        _bounds.Reset();
        _offsetProxy = null;
        _wholeFrame = true;
        _recordedOffset = default;
        // Enter the whole-frame recordability scope so call sites that hit
        // un-representable content (via a failed `is RenderTargetDrawingContext`
        // cast) can flag this frame for direct-render fallback.
        _wholeFrameSavedScope = DrawingContext.BeginWholeFrameRecordingScope();
    }

    /// <summary>
    /// Finalizes the current recording and returns an immutable
    /// <see cref="Drawing"/> with its bounds populated (or null when the
    /// recording contains content whose extent could not be bounded).
    /// </summary>
    public Drawing Commit()
    {
        // Close the whole-frame recordability scope (no-op for per-visual mode)
        // and learn whether any un-recordable content was seen this frame.
        bool fullyRecordable = !(_wholeFrame
            && DrawingContext.EndWholeFrameRecordingScope(_wholeFrameSavedScope));

        if (_commands.Count == 0)
        {
            _offsetProxy = null;
            _bounds.Reset();
            _wholeFrame = false;
            // An empty-but-unrecordable capture must still force a fallback, so
            // don't return the shared (fully-recordable) Empty in that case.
            return fullyRecordable
                ? Drawing.Empty
                : new Drawing(System.Array.Empty<DrawCommand>(), 0, null, fullyRecordable: false);
        }

        var arr = _commands.ToArray();
        // Whole-frame captures span many per-child offsets, so the accumulator's
        // single bounds rect is meaningless (it ignores SetOffset) — null it to
        // disable the replay-time AABB short-circuit and replay everything.
        var drawingBounds = _wholeFrame ? (Rect?)null : _bounds.GetBounds();

        _commands.Clear();
        _bounds.Reset();
        _offsetProxy = null;
        _wholeFrame = false;

        return new Drawing(arr, arr.Length, drawingBounds, fullyRecordable);
    }

    /// <summary>
    /// Alternate release path for recorders that are abandoned without a
    /// successful <see cref="Commit"/> (e.g. the caller threw).
    /// </summary>
    public void Reset()
    {
        // Close the whole-frame scope if we were abandoned mid-capture.
        if (_wholeFrame) DrawingContext.EndWholeFrameRecordingScope(_wholeFrameSavedScope);
        _commands.Clear();
        _bounds.Reset();
        _offsetProxy = null;
        _wholeFrame = false;
    }

    // ── Ambient-state proxies ────────────────────────────────────────────

    Point IOffsetDrawingContext.Offset
    {
        get => _wholeFrame ? _recordedOffset : (_offsetProxy?.Offset ?? default);
        set
        {
            if (_wholeFrame)
            {
                // Record the absolute per-child offset so replay re-applies it.
                _recordedOffset = value;
                _commands.Add(DrawCommand.SetOffsetCmd(value));
            }
            else if (_offsetProxy != null)
            {
                _offsetProxy.Offset = value;
            }
        }
    }

    // A recorded Drawing is replayed at ANY position/clip later — a per-visual cache
    // replays as its element scrolls; the whole-frame cache replays the whole tree — so
    // OnRender must NOT observe the live viewport clip and cull content OUT of the
    // recording. Baking the record-time clip stranded clipped-at-record-time content
    // (TextBlock's per-line cull, etc.): a NavigationView label recorded while below the
    // fold cached an EMPTY line set and then replayed BLANK after being scrolled into
    // view — content-clean, so OnRender never re-ran — until a click marked it
    // render-dirty and forced a re-record (the "scrolled-in nav item blank until clicked"
    // bug). Returning null for BOTH modes makes the cache position/clip-independent;
    // viewport culling happens correctly at REPLAY time via the DrawingReplayer AABB
    // short-circuit + the native GPU scissor. Slight over-record, always correct — the
    // whole-frame path already relied on this; the per-visual path needs it too.
    Rect? IClipBoundsDrawingContext.CurrentClipBounds => null;

    // ── Whole-frame freeze-clone (increment 3) ──────────────────────────
    // On the whole-frame (render-thread) path, mutable live payloads (gradient/
    // image brushes, transforms, video bitmaps) are snapshotted at record time
    // so a later UI-thread mutation can't corrupt an in-flight replay on the
    // render thread. Immutable / pooled inputs (SolidColorBrush, simple Pen,
    // FormattedText) are already value-snapshotted by DrawingObjectPool and pass
    // through untouched. No-op on the default per-visual path (zero added cost).
    private Brush? SnapBrush(Brush? b) => _wholeFrame ? DrawInputSnapshotter.SnapshotBrush(b) : b;
    private Pen? SnapPen(Pen? p) => _wholeFrame ? DrawInputSnapshotter.SnapshotPen(p) : p;
    private Transform SnapTransform(Transform t) => _wholeFrame ? DrawInputSnapshotter.SnapshotTransform(t) : t;
    private ImageSource SnapImage(ImageSource i) => _wholeFrame ? DrawInputSnapshotter.SnapshotImage(i) : i;

    // ── Draw calls ──────────────────────────────────────────────────────
    //
    // Each Draw* method canonicalises its Brush / Pen / FormattedText
    // arguments through DrawingObjectPool before recording, then extends
    // the bounds accumulator with the draw's local-space extent (pen
    // thickness half-spilled on each side for stroked primitives).

    public override void DrawLine(Pen pen, Point point0, Point point1)
    {
        var canonicalPen = SnapPen(DrawingObjectPool.CanonicalizePen(pen)!)!;
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
        var canonicalBrush = SnapBrush(DrawingObjectPool.CanonicalizeBrush(brush));
        var canonicalPen = SnapPen(DrawingObjectPool.CanonicalizePen(pen));
        _commands.Add(DrawCommand.Rectangle(canonicalBrush, canonicalPen, rectangle));
        _bounds.AccumulateRect(rectangle, StrokeSlop(canonicalPen));
    }

    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        var canonicalBrush = SnapBrush(DrawingObjectPool.CanonicalizeBrush(brush));
        var canonicalPen = SnapPen(DrawingObjectPool.CanonicalizePen(pen));
        _commands.Add(DrawCommand.RoundedRectangle(canonicalBrush, canonicalPen, rectangle, radiusX, radiusY));
        _bounds.AccumulateRect(rectangle, StrokeSlop(canonicalPen));
    }

    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius cornerRadius)
    {
        // Avoid the base-class fast-path that calls DrawRectangle /
        // DrawGeometry and loses the "this was originally a rounded rect
        // with a CornerRadius" intent. Record the high-level call verbatim
        // so replay re-dispatches to the target context's own fast paths.
        var canonicalBrush = SnapBrush(DrawingObjectPool.CanonicalizeBrush(brush));
        var canonicalPen = SnapPen(DrawingObjectPool.CanonicalizePen(pen));
        _commands.Add(DrawCommand.RoundedRectangleCorner(canonicalBrush, canonicalPen, rectangle, cornerRadius));
        _bounds.AccumulateRect(rectangle, StrokeSlop(canonicalPen));
    }

    public override void DrawContentBorder(Brush? fillBrush, Pen? strokePen, Rect rectangle,
        double bottomLeftRadius, double bottomRightRadius)
    {
        var canonicalFill = SnapBrush(DrawingObjectPool.CanonicalizeBrush(fillBrush));
        var canonicalStroke = SnapPen(DrawingObjectPool.CanonicalizePen(strokePen));
        _commands.Add(DrawCommand.ContentBorder(canonicalFill, canonicalStroke, rectangle, bottomLeftRadius, bottomRightRadius));
        _bounds.AccumulateRect(rectangle, StrokeSlop(canonicalStroke));
    }

    public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
    {
        var canonicalBrush = SnapBrush(DrawingObjectPool.CanonicalizeBrush(brush));
        var canonicalPen = SnapPen(DrawingObjectPool.CanonicalizePen(pen));
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
        var canonicalBrush = SnapBrush(DrawingObjectPool.CanonicalizeBrush(brush)!)!;
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

        var canonicalPen = SnapPen(DrawingObjectPool.CanonicalizePen(pen)!)!;
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
        // A gradient/image text foreground is shared by-reference (the pool only
        // canonicalizes a solid foreground); on the whole-frame path a later UI
        // mutation would race the render-thread replay. Rare enough (most text is
        // solid-colored) to fall back to direct render rather than snapshot the
        // whole FormattedText.
        if (_wholeFrame && canonical.Foreground is not (null or SolidColorBrush))
            DrawingContext.MarkCurrentFrameUnrecordable();
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
        var canonicalBrush = SnapBrush(DrawingObjectPool.CanonicalizeBrush(brush));
        var canonicalPen = SnapPen(DrawingObjectPool.CanonicalizePen(pen));
        _commands.Add(DrawCommand.GeometryCmd(canonicalBrush, canonicalPen, geometry));
        _bounds.AccumulateRect(geometry.Bounds, StrokeSlop(canonicalPen));
    }

    public override void SetShapeType(int type, float exponent)
    {
        // Record the shape-type state so a replay reproduces the SuperEllipse
        // intent in draw order (Border sets it before its rounded-rect fill,
        // then resets to 0). No bounds contribution — it draws nothing.
        _commands.Add(DrawCommand.SetShapeTypeCmd(type, exponent));
    }


    public override void DrawImage(ImageSource imageSource, Rect rectangle)
    {
        _commands.Add(DrawCommand.Image(SnapImage(imageSource), rectangle, BitmapScalingMode.Unspecified));
        _bounds.AccumulateRect(rectangle);
    }

    public override void DrawImage(ImageSource imageSource, Rect rectangle, BitmapScalingMode scalingMode)
    {
        _commands.Add(DrawCommand.Image(SnapImage(imageSource), rectangle, scalingMode));
        _bounds.AccumulateRect(rectangle);
    }

    public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
    {
        // KNOWN LIMITATION (render-thread path): IBackdropEffect / IEffect (here and in
        // PushEffect/ApplyElementEffect) are recorded BY REFERENCE — they have no generic
        // value-clone. A static effect replays correctly; an ANIMATED effect (e.g. a blur-
        // radius tween) read on the render thread while the UI thread mutates it can show a
        // 1-frame torn value. Not a crash/freeze. Per-effect-type snapshotting is a follow-up;
        // until then animated effects are best used with JALIUM_RENDER_THREAD off.
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
        _commands.Add(DrawCommand.PushTransformCmd(SnapTransform(transform)));
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

    // ── Ambient typed-interface impls (whole-frame recording) ───────────
    // RenderDirect / RenderChildVisualInline apply per-element opacity, render
    // transform (around an origin), and element effects through these typed
    // interfaces (guarded by `is IXxxDrawingContext`). A recorder lacking them
    // would silently DROP those operations. PushOpacity(double) / PushClip(Geometry)
    // / Pop() are already provided by the base DrawingContext overrides above;
    // only the members below are new. Harmless in per-visual mode (never called).

    void IOpacityDrawingContext.PopOpacity() => Pop();

    void ITransformDrawingContext.PushTransform(Transform transform, double originX, double originY)
    {
        if (originX != 0 || originY != 0)
        {
            // Compose T(-origin) * transform * T(+origin) — identical to
            // RenderTargetDrawingContext so replay reproduces it exactly.
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

    void ITransformDrawingContext.PopTransform() => Pop();

    void IEffectDrawingContext.BeginEffectCapture(float x, float y, float w, float h) =>
        _commands.Add(DrawCommand.BeginEffectCaptureCmd(x, y, w, h));

    void IEffectDrawingContext.EndEffectCapture() =>
        _commands.Add(DrawCommand.EndEffectCaptureCmd());

    void IEffectDrawingContext.ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
        float captureOriginX, float captureOriginY,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL) =>
        _commands.Add(DrawCommand.ApplyElementEffectCmd(effect, x, y, w, h,
            captureOriginX, captureOriginY, cornerTL, cornerTR, cornerBR, cornerBL));

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
