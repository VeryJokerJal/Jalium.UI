using Jalium.UI;

namespace Jalium.UI.Media.Rendering;

/// <summary>
/// Tag for a single recorded draw command. Corresponds one-to-one with the
/// abstract / virtual methods on <see cref="DrawingContext"/> plus the
/// state-stack operations (push / pop variants, effect capture).
/// </summary>
internal enum DrawCommandKind : byte
{
    DrawLine,
    DrawRectangle,
    DrawRoundedRectangle,        // uniform (radiusX, radiusY)
    DrawRoundedRectangleCorner,  // non-uniform CornerRadius
    DrawEllipse,
    DrawText,
    DrawGeometry,
    DrawImage,
    DrawBackdropEffect,
    PushTransform,
    PushClip,
    PushOpacity,
    Pop,
    PushEffect,
    PopEffect,
    DrawContentBorder,
    DrawPoints,
    DrawLines,
    DrawLiquidGlass,
    SetOffset,                   // whole-frame recorder: absolute IOffsetDrawingContext.Offset
    BeginEffectCapture,          // IEffectDrawingContext (element BlurEffect/DropShadow)
    EndEffectCapture,
    ApplyElementEffect,
    SetShapeType,                // SuperEllipse shape-type state (V0=type, V1=exponent)
}

/// <summary>
/// One recorded drawing call, stored in a <see cref="Drawing"/>'s command
/// array. Designed as a fixed-size 120-byte readonly struct so a List of
/// commands is dense and allocation-free per-command (the variable-length
/// payload — brushes, pens, geometries — is carried as <see cref="object"/>
/// references that are shared with the caller's originals by default, and
/// may be canonicalized by an object pool in a later phase).
/// </summary>
/// <remarks>
/// Field roles depend on <see cref="Kind"/>. Only documented slots are used
/// for each kind; unused slots default to zero / null. Keeping a single
/// layout avoids per-kind class hierarchies and keeps the replay inner loop
/// branch-light.
/// </remarks>
internal readonly struct DrawCommand
{
    public readonly DrawCommandKind Kind;
    public readonly object? A;
    public readonly object? B;
    public readonly object? C;
    public readonly double V0;
    public readonly double V1;
    public readonly double V2;
    public readonly double V3;
    public readonly double V4;
    public readonly double V5;
    public readonly double V6;
    public readonly double V7;

    private DrawCommand(
        DrawCommandKind kind,
        object? a, object? b, object? c,
        double v0, double v1, double v2, double v3,
        double v4, double v5, double v6, double v7)
    {
        Kind = kind;
        A = a; B = b; C = c;
        V0 = v0; V1 = v1; V2 = v2; V3 = v3;
        V4 = v4; V5 = v5; V6 = v6; V7 = v7;
    }

    public static DrawCommand Line(Pen pen, Point p0, Point p1) =>
        new(DrawCommandKind.DrawLine, pen, null, null,
            p0.X, p0.Y, p1.X, p1.Y, 0, 0, 0, 0);

    public static DrawCommand Rectangle(Brush? brush, Pen? pen, Rect rect) =>
        new(DrawCommandKind.DrawRectangle, brush, pen, null,
            rect.X, rect.Y, rect.Width, rect.Height, 0, 0, 0, 0);

    public static DrawCommand RoundedRectangle(Brush? brush, Pen? pen, Rect rect, double radiusX, double radiusY) =>
        new(DrawCommandKind.DrawRoundedRectangle, brush, pen, null,
            rect.X, rect.Y, rect.Width, rect.Height, radiusX, radiusY, 0, 0);

    public static DrawCommand RoundedRectangleCorner(Brush? brush, Pen? pen, Rect rect, CornerRadius cr) =>
        new(DrawCommandKind.DrawRoundedRectangleCorner, brush, pen, null,
            rect.X, rect.Y, rect.Width, rect.Height,
            cr.TopLeft, cr.TopRight, cr.BottomRight, cr.BottomLeft);

    public static DrawCommand Ellipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) =>
        new(DrawCommandKind.DrawEllipse, brush, pen, null,
            center.X, center.Y, radiusX, radiusY, 0, 0, 0, 0);

    public static DrawCommand Text(FormattedText ft, Point origin) =>
        new(DrawCommandKind.DrawText, ft, null, null,
            origin.X, origin.Y, 0, 0, 0, 0, 0, 0);

    public static DrawCommand GeometryCmd(Brush? brush, Pen? pen, Geometry geometry) =>
        new(DrawCommandKind.DrawGeometry, brush, pen, geometry,
            0, 0, 0, 0, 0, 0, 0, 0);

    public static DrawCommand Image(ImageSource image, Rect rect, BitmapScalingMode scalingMode) =>
        new(DrawCommandKind.DrawImage, image, null, null,
            rect.X, rect.Y, rect.Width, rect.Height, (double)(int)scalingMode, 0, 0, 0);

    public static DrawCommand BackdropEffect(IBackdropEffect effect, Rect rect, CornerRadius cr) =>
        new(DrawCommandKind.DrawBackdropEffect, effect, null, null,
            rect.X, rect.Y, rect.Width, rect.Height,
            cr.TopLeft, cr.TopRight, cr.BottomRight, cr.BottomLeft);

    public static DrawCommand PushTransformCmd(Transform transform) =>
        new(DrawCommandKind.PushTransform, transform, null, null,
            0, 0, 0, 0, 0, 0, 0, 0);

    public static DrawCommand PushClipCmd(Geometry geometry) =>
        new(DrawCommandKind.PushClip, geometry, null, null,
            0, 0, 0, 0, 0, 0, 0, 0);

    public static DrawCommand PushOpacityCmd(double opacity) =>
        new(DrawCommandKind.PushOpacity, null, null, null,
            opacity, 0, 0, 0, 0, 0, 0, 0);

    public static DrawCommand PopCmd() =>
        new(DrawCommandKind.Pop, null, null, null,
            0, 0, 0, 0, 0, 0, 0, 0);

    public static DrawCommand PushEffectCmd(IEffect effect, Rect captureBounds) =>
        new(DrawCommandKind.PushEffect, effect, null, null,
            captureBounds.X, captureBounds.Y, captureBounds.Width, captureBounds.Height,
            0, 0, 0, 0);

    public static DrawCommand PopEffectCmd() =>
        new(DrawCommandKind.PopEffect, null, null, null,
            0, 0, 0, 0, 0, 0, 0, 0);

    public static DrawCommand ContentBorder(Brush? fill, Pen? stroke, Rect rect, double bottomLeftRadius, double bottomRightRadius) =>
        new(DrawCommandKind.DrawContentBorder, fill, stroke, null,
            rect.X, rect.Y, rect.Width, rect.Height,
            bottomLeftRadius, bottomRightRadius, 0, 0);

    /// <summary>
    /// Batch of identical filled circles. <c>A</c> carries the fill brush,
    /// <c>C</c> carries a <see cref="Point"/>[] of centres (the recorder
    /// materialises the span into an array so the command can outlive the
    /// caller's stack frame), <c>V0</c> carries the shared radius.
    /// </summary>
    public static DrawCommand Points(Brush brush, Point[] centers, double radius) =>
        new(DrawCommandKind.DrawPoints, brush, null, centers,
            radius, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Batch of identical-pen line segments. <c>A</c> carries the shared
    /// pen, <c>C</c> carries a flat <see cref="Point"/>[] of start/end
    /// pairs (owned by the command for lifetime reasons).
    /// </summary>
    public static DrawCommand Lines(Pen pen, Point[] endpoints) =>
        new(DrawCommandKind.DrawLines, pen, null, endpoints,
            0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Liquid-glass effect with all its parameters aggregated in the
    /// payload object (~20 scalars plus the neighbor fusion buffer don't
    /// fit in the fixed V0-V7 slots). <c>A</c> owns the payload outright —
    /// the recorder constructs a fresh <see cref="LiquidGlassParameters"/>
    /// instance per capture so replays don't observe a subsequent frame's
    /// mutated state.
    /// </summary>
    public static DrawCommand LiquidGlass(LiquidGlassParameters parameters) =>
        new(DrawCommandKind.DrawLiquidGlass, parameters, null, null,
            0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Whole-frame recorder only: records an absolute set of
    /// <see cref="IOffsetDrawingContext.Offset"/> (the per-child translation that
    /// <c>Visual.RenderChildVisualInline</c> applies via save/set/restore). Replay
    /// re-applies it to the target context's Offset so raw-local draw coords land
    /// in the same place the live render put them. <c>V0/V1</c> carry X/Y.
    /// </summary>
    public static DrawCommand SetOffsetCmd(Point offset) =>
        new(DrawCommandKind.SetOffset, null, null, null,
            offset.X, offset.Y, 0, 0, 0, 0, 0, 0);

    // ── IEffectDrawingContext (element effects: BlurEffect / DropShadowEffect) ──
    // Captured by the whole-frame recorder so the offscreen-capture + apply
    // sequence RenderDirect emits replays identically on the render thread.

    public static DrawCommand BeginEffectCaptureCmd(float x, float y, float w, float h) =>
        new(DrawCommandKind.BeginEffectCapture, null, null, null,
            x, y, w, h, 0, 0, 0, 0);

    public static DrawCommand EndEffectCaptureCmd() =>
        new(DrawCommandKind.EndEffectCapture, null, null, null,
            0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// SuperEllipse shape-type state for the subsequent rounded-rectangle
    /// draw(s). <c>V0</c> = type (0 = rounded rect, 1 = SuperEllipse),
    /// <c>V1</c> = Lamé exponent. Replayed verbatim so a cached/whole-frame
    /// Border reproduces its squircle in draw order instead of falling back to
    /// an out-of-order geometry fill.
    /// </summary>
    public static DrawCommand SetShapeTypeCmd(int type, float exponent) =>
        new(DrawCommandKind.SetShapeType, null, null, null,
            type, exponent, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// <c>A</c> = the <see cref="IEffect"/>; V0-V7 carry x,y,w,h,captureOriginX,
    /// captureOriginY,cornerTL,cornerTR; <c>C</c> = double[2] { cornerBR, cornerBL }
    /// (10 floats don't fit the 8 V-slots).
    /// </summary>
    public static DrawCommand ApplyElementEffectCmd(IEffect effect,
        float x, float y, float w, float h,
        float captureOriginX, float captureOriginY,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL) =>
        new(DrawCommandKind.ApplyElementEffect, effect, null, new double[] { cornerBR, cornerBL },
            x, y, w, h, captureOriginX, captureOriginY, cornerTL, cornerTR);
}
