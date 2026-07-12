using Jalium.UI;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media;

/// <summary>
/// Describes visual content using draw, push, and pop commands.
/// </summary>
public abstract class DrawingContext : IDisposable, IClipDrawingContext
{
    // ── Whole-frame recordability signal (JALIUM_RENDER_THREAD path) ──────
    // During whole-frame recording (DrawingRecorder.BindWholeFrame) the tree is
    // captured as pure data. Content reached ONLY through an
    // `is RenderTargetDrawingContext` cast (transparent punch, video surface,
    // ink-layer blit, transition shader, …) has no DrawCommand representation;
    // when that cast fails against the recorder the call site flips this
    // thread-static flag so the render loop discards the capture and
    // direct-renders the frame instead of silently dropping the content.
    // No-op outside a whole-frame scope, so the default UI path and the
    // per-visual retained cache are completely unaffected.
    [ThreadStatic] private static bool s_wholeFrameRecording;
    [ThreadStatic] private static bool s_wholeFrameUnrecordable;

    /// <summary>True while a whole-frame recorder is active on this thread.</summary>
    public static bool IsWholeFrameRecording => s_wholeFrameRecording;

    /// <summary>
    /// Enters a whole-frame recording scope and returns the previous state so
    /// the caller can restore it (reentrancy-safe). Used by
    /// <c>DrawingRecorder.BindWholeFrame</c>.
    /// </summary>
    public static (bool recording, bool unrecordable) BeginWholeFrameRecordingScope()
    {
        var prev = (s_wholeFrameRecording, s_wholeFrameUnrecordable);
        s_wholeFrameRecording = true;
        s_wholeFrameUnrecordable = false;
        return prev;
    }

    /// <summary>
    /// Exits the current whole-frame recording scope, restoring the saved outer
    /// state and returning whether any un-recordable content was seen.
    /// </summary>
    public static bool EndWholeFrameRecordingScope((bool recording, bool unrecordable) saved)
    {
        bool unrecordable = s_wholeFrameUnrecordable;
        s_wholeFrameRecording = saved.recording;
        s_wholeFrameUnrecordable = saved.unrecordable;
        return unrecordable;
    }

    /// <summary>
    /// Marks the in-progress whole-frame recording as not fully recordable, so
    /// the render loop falls back to a direct render for this frame. No-op when
    /// no whole-frame recording is active (default UI path / per-visual cache).
    /// </summary>
    public static void MarkCurrentFrameUnrecordable()
    {
        if (s_wholeFrameRecording) s_wholeFrameUnrecordable = true;
    }

    /// <summary>
    /// Draws a line between two points.
    /// </summary>
    /// <param name="pen">The pen to use.</param>
    /// <param name="point0">The start point.</param>
    /// <param name="point1">The end point.</param>
    public abstract void DrawLine(Pen pen, Point point0, Point point1);

    /// <summary>Draws an animated line, using the current values of its clocks.</summary>
    public abstract void DrawLine(
        Pen pen,
        Point point0,
        AnimationClock? point0Animations,
        Point point1,
        AnimationClock? point1Animations);

    /// <summary>Draws a retained drawing into this context.</summary>
    public abstract void DrawDrawing(Drawing drawing);

    /// <summary>
    /// Draws a rectangle.
    /// </summary>
    /// <param name="brush">The brush to fill with, or null for no fill.</param>
    /// <param name="pen">The pen for the outline, or null for no outline.</param>
    /// <param name="rectangle">The rectangle to draw.</param>
    public abstract void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle);

    /// <summary>Draws an animated rectangle using the current rectangle value.</summary>
    public abstract void DrawRectangle(
        Brush? brush,
        Pen? pen,
        Rect rectangle,
        AnimationClock? rectangleAnimations);

    /// <summary>
    /// Draws a rounded rectangle.
    /// </summary>
    /// <param name="brush">The brush to fill with, or null for no fill.</param>
    /// <param name="pen">The pen for the outline, or null for no outline.</param>
    /// <param name="rectangle">The rectangle to draw.</param>
    /// <param name="radiusX">The X radius of the corners.</param>
    /// <param name="radiusY">The Y radius of the corners.</param>
    public abstract void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY);

    /// <summary>Draws an animated rounded rectangle using the current supplied values.</summary>
    public abstract void DrawRoundedRectangle(
        Brush? brush,
        Pen? pen,
        Rect rectangle,
        AnimationClock? rectangleAnimations,
        double radiusX,
        AnimationClock? radiusXAnimations,
        double radiusY,
        AnimationClock? radiusYAnimations);

    /// <summary>
    /// Draws a rounded rectangle with potentially non-uniform corner radii.
    /// </summary>
    /// <param name="brush">The brush to fill with, or null for no fill.</param>
    /// <param name="pen">The pen for the outline, or null for no outline.</param>
    /// <param name="rectangle">The rectangle to draw.</param>
    /// <param name="cornerRadius">The corner radius for each corner.</param>
    public virtual void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius cornerRadius)
    {
        cornerRadius = cornerRadius.Normalize(rectangle.Width, rectangle.Height);

        // Fast path: no corner radius
        if (cornerRadius.TopLeft == 0 && cornerRadius.TopRight == 0 &&
            cornerRadius.BottomLeft == 0 && cornerRadius.BottomRight == 0)
        {
            DrawRectangle(brush, pen, rectangle);
            return;
        }

        // Fast path: uniform corner radius
        if (cornerRadius.TopLeft == cornerRadius.TopRight &&
            cornerRadius.TopLeft == cornerRadius.BottomRight &&
            cornerRadius.TopLeft == cornerRadius.BottomLeft)
        {
            DrawRoundedRectangle(brush, pen, rectangle, cornerRadius.TopLeft, cornerRadius.TopLeft);
            return;
        }

        // Non-uniform corner radius: use PathGeometry
        var geometry = CreateRoundedRectGeometry(rectangle, cornerRadius);
        DrawGeometry(brush, pen, geometry);
    }

    /// <summary>
    /// Draws a content area border: fills rect with bottom-only rounded corners,
    /// strokes a U-shape (left + bottom + right, no top) with smooth arcs.
    /// </summary>
    public virtual void DrawContentBorder(Brush? fillBrush, Pen? strokePen, Rect rectangle,
        double bottomLeftRadius, double bottomRightRadius)
    {
        if (fillBrush != null)
            DrawRoundedRectangle(fillBrush, null, rectangle, new CornerRadius(0, 0, bottomRightRadius, bottomLeftRadius));
        if (strokePen != null)
        {
            double x = rectangle.X, y = rectangle.Y;
            double w = rectangle.Width, h = rectangle.Height;
            double bl = Math.Min(bottomLeftRadius, Math.Min(w, h) / 2);
            double br = Math.Min(bottomRightRadius, Math.Min(w, h) / 2);

            var figure = new PathFigure
            {
                StartPoint = new Point(x, y),
                IsClosed = false,
                IsFilled = false,
            };

            const double k = 0.55228474983711; // cubic Bézier approximation of quarter circle: (4/3)*tan(π/8)

            // Left edge: top → bottom-left arc start
            figure.Segments.Add(new LineSegment(new Point(x, y + h - bl)));

            // Bottom-left arc (cubic Bézier)
            if (bl > 0)
            {
                figure.Segments.Add(new BezierSegment(
                    new Point(x, y + h - bl * (1 - k)),
                    new Point(x + bl * (1 - k), y + h),
                    new Point(x + bl, y + h)));
            }

            // Bottom edge
            figure.Segments.Add(new LineSegment(new Point(x + w - br, y + h)));

            // Bottom-right arc (cubic Bézier)
            if (br > 0)
            {
                figure.Segments.Add(new BezierSegment(
                    new Point(x + w - br * (1 - k), y + h),
                    new Point(x + w, y + h - br * (1 - k)),
                    new Point(x + w, y + h - br)));
            }

            // Right edge: bottom-right → top
            figure.Segments.Add(new LineSegment(new Point(x + w, y)));

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures.Add(figure);
            DrawGeometry(null, strokePen, pathGeometry);
        }
    }

    /// <summary>
    /// Creates a PathGeometry for a rounded rectangle with non-uniform corner radii.
    /// </summary>
    private static PathGeometry CreateRoundedRectGeometry(Rect rect, CornerRadius cornerRadius)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure();

        double x = rect.X;
        double y = rect.Y;
        double w = rect.Width;
        double h = rect.Height;

        cornerRadius = cornerRadius.Normalize(w, h);
        double tl = cornerRadius.TopLeft;
        double tr = cornerRadius.TopRight;
        double br = cornerRadius.BottomRight;
        double bl = cornerRadius.BottomLeft;

        // Start at top-left corner, after the arc
        figure.StartPoint = new Point(x + tl, y);
        figure.IsClosed = true;
        figure.IsFilled = true;

        // Top edge
        figure.Segments.Add(new LineSegment(new Point(x + w - tr, y), true));

        // Top-right corner
        if (tr > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x + w, y + tr),
                new Size(tr, tr),
                0, false, SweepDirection.Clockwise, true));
        }

        // Right edge
        figure.Segments.Add(new LineSegment(new Point(x + w, y + h - br), true));

        // Bottom-right corner
        if (br > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x + w - br, y + h),
                new Size(br, br),
                0, false, SweepDirection.Clockwise, true));
        }

        // Bottom edge
        figure.Segments.Add(new LineSegment(new Point(x + bl, y + h), true));

        // Bottom-left corner
        if (bl > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x, y + h - bl),
                new Size(bl, bl),
                0, false, SweepDirection.Clockwise, true));
        }

        // Left edge
        figure.Segments.Add(new LineSegment(new Point(x, y + tl), true));

        // Top-left corner
        if (tl > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x + tl, y),
                new Size(tl, tl),
                0, false, SweepDirection.Clockwise, true));
        }

        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>
    /// Draws an ellipse.
    /// </summary>
    /// <param name="brush">The brush to fill with, or null for no fill.</param>
    /// <param name="pen">The pen for the outline, or null for no outline.</param>
    /// <param name="center">The center point.</param>
    /// <param name="radiusX">The X radius.</param>
    /// <param name="radiusY">The Y radius.</param>
    public abstract void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY);

    /// <summary>Draws an animated ellipse using the current supplied values.</summary>
    public abstract void DrawEllipse(
        Brush? brush,
        Pen? pen,
        Point center,
        AnimationClock? centerAnimations,
        double radiusX,
        AnimationClock? radiusXAnimations,
        double radiusY,
        AnimationClock? radiusYAnimations);

    /// <summary>
    /// Draws a batch of identical filled circles in one call. All circles
    /// share the same brush and radius — intended for dense particle-like
    /// scenarios (grid dots, scatter plots, node ports, marker lists) where
    /// issuing thousands of individual <see cref="DrawEllipse(Brush,Pen,Point,double,double)"/> calls
    /// dominates frame time even though the GPU collapses them to one
    /// instanced draw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Optimised implementations (<c>RenderTargetDrawingContext</c>) forward
    /// the whole batch to the native side in one call via the
    /// already-existing <c>BeginEllipseBatch</c> / <c>EndEllipseBatch</c>
    /// mechanism, eliminating N managed→native round trips, N brush-cache
    /// lookups, and N native-vector push_backs in favour of a single
    /// <c>FillEllipseBatch</c>.
    /// </para>
    /// <para>
    /// The default implementation falls back to a loop over
    /// <see cref="DrawEllipse(Brush,Pen,Point,double,double)"/> so any <see cref="DrawingContext"/>
    /// subclass (test doubles, export contexts) keeps working without
    /// change.
    /// </para>
    /// <para>
    /// Stroke rendering is intentionally not supported here — the native
    /// batch path is fill-only. Callers needing stroked batches can loop
    /// <see cref="DrawEllipse(Brush,Pen,Point,double,double)"/> explicitly.
    /// </para>
    /// </remarks>
    /// <param name="brush">The fill brush. Must be non-null; passing null
    /// is a no-op (nothing to draw).</param>
    /// <param name="centers">Circle centre points.</param>
    /// <param name="radius">Circle radius (used for both X and Y — the
    /// batch API targets circles, not general ellipses).</param>
    public virtual void DrawPoints(Brush? brush, ReadOnlySpan<Point> centers, double radius)
    {
        if (brush is null || centers.IsEmpty || !(radius > 0))
        {
            return;
        }

        for (int i = 0; i < centers.Length; i++)
        {
            DrawEllipse(brush, pen: null, centers[i], radius, radius);
        }
    }

    /// <summary>
    /// Draws a batch of line segments that all share the same
    /// <see cref="Pen"/>. Intended for designer grids, rulers, charts, and
    /// any scenario that would otherwise loop <see cref="DrawLine(Pen,Point,Point)"/> with
    /// an unchanging pen — the loop's managed-to-native round-trips and
    /// per-call brush-cache lookups are the dominant cost at scale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="endpoints"/> is a flat list of start/end pairs:
    /// element at index <c>2k</c> is the start of segment <c>k</c>, index
    /// <c>2k+1</c> is its end. Odd-length spans are truncated by one.
    /// </para>
    /// <para>
    /// Optimised backends resolve the pen's native brush once up front and
    /// reuse it for every segment, saving N-1 brush-cache lookups. The
    /// default fallback loops <see cref="DrawLine(Pen,Point,Point)"/> so any
    /// <see cref="DrawingContext"/> subclass works unchanged.
    /// </para>
    /// </remarks>
    public virtual void DrawLines(Pen pen, ReadOnlySpan<Point> endpoints)
    {
        if (pen is null || endpoints.Length < 2)
        {
            return;
        }

        var pairs = endpoints.Length / 2;
        for (int i = 0; i < pairs; i++)
        {
            DrawLine(pen, endpoints[2 * i], endpoints[2 * i + 1]);
        }
    }

    /// <summary>
    /// Draws text at the specified location.
    /// </summary>
    /// <param name="formattedText">The formatted text to draw.</param>
    /// <param name="origin">The top-left origin point.</param>
    public void DrawText(FormattedText formattedText, Point origin)
    {
        ArgumentNullException.ThrowIfNull(formattedText);

        // DrawingContext.DrawText is concrete and non-virtual in WPF. Existing Jalium
        // backends historically overrode the abstract member, so dispatch through the
        // migration adapter while keeping DrawingContext's public metadata exact.
        if (this is DrawingContextAdapter adapter)
        {
            adapter.DrawText(formattedText, origin);
            return;
        }

        DrawGeometry(formattedText.Foreground, null, formattedText.BuildGeometry(origin));
    }

    /// <summary>Draws the positioned glyphs in a glyph run.</summary>
    public abstract void DrawGlyphRun(Brush? foregroundBrush, GlyphRun glyphRun);

    /// <summary>
    /// Draws text at the specified location using the given typeface and brush.
    /// </summary>
    /// <param name="text">The text string to draw.</param>
    /// <param name="typeface">The typeface to use.</param>
    /// <param name="emSize">The font size in device-independent pixels.</param>
    /// <param name="foreground">The brush used to paint the text.</param>
    /// <param name="origin">The top-left origin point.</param>
    public void DrawText(string text, Typeface typeface, double emSize, Brush foreground, Point origin)
    {
        var formattedText = new FormattedText(text, typeface.FontFamily.Source, emSize)
        {
            Foreground = foreground,
            FontWeight = typeface.Weight.ToOpenTypeWeight(),
            FontStyle = typeface.Style.ToOpenTypeStyle(),
            FontStretch = typeface.Stretch.ToOpenTypeStretch()
        };
        DrawText(formattedText, origin);
    }

    /// <summary>
    /// Draws a geometry.
    /// </summary>
    /// <param name="brush">The brush to fill with, or null for no fill.</param>
    /// <param name="pen">The pen for the outline, or null for no outline.</param>
    /// <param name="geometry">The geometry to draw.</param>
    public abstract void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry);


    /// <summary>
    /// Draws an image.
    /// </summary>
    /// <param name="imageSource">The image to draw.</param>
    /// <param name="rectangle">The destination rectangle.</param>
    public abstract void DrawImage(ImageSource imageSource, Rect rectangle);

    /// <summary>Draws an image into an animated rectangle using its current value.</summary>
    public abstract void DrawImage(
        ImageSource imageSource,
        Rect rectangle,
        AnimationClock? rectangleAnimations);

    /// <summary>Draws the current frame exposed by a media player.</summary>
    public abstract void DrawVideo(MediaPlayer player, Rect rectangle);

    /// <summary>Draws video into an animated rectangle using its current value.</summary>
    public abstract void DrawVideo(
        MediaPlayer player,
        Rect rectangle,
        AnimationClock? rectangleAnimations);

    /// <summary>
    /// Draws an image using the specified bitmap scaling mode.
    /// </summary>
    /// <param name="imageSource">The image to draw.</param>
    /// <param name="rectangle">The destination rectangle.</param>
    /// <param name="scalingMode">
    /// The algorithm used to scale the bitmap when its source pixel size differs
    /// from the destination rectangle. <see cref="BitmapScalingMode.Unspecified"/>
    /// resolves to <see cref="BitmapScalingMode.HighQuality"/> (anisotropic + mipmap)
    /// in the default backend pipeline.
    /// </param>
    /// <remarks>
    /// The default implementation forwards to the legacy <see cref="DrawImage(ImageSource, Rect)"/>
    /// overload so existing contexts (PDF export, headless test, etc.) keep working.
    /// Backends that honour scaling mode should override this method.
    /// </remarks>
    public virtual void DrawImage(ImageSource imageSource, Rect rectangle, BitmapScalingMode scalingMode)
    {
        DrawImage(imageSource, rectangle);
    }

    /// <summary>
    /// Draws a backdrop effect.
    /// </summary>
    /// <param name="rectangle">The area to apply the effect to.</param>
    /// <param name="effect">The backdrop effect to apply. Can be BlurEffect, AcrylicEffect, MicaEffect, or custom implementations.</param>
    /// <param name="cornerRadius">The corner radius for the effect area.</param>
    public abstract void DrawBackdropEffect(
        Rect rectangle,
        IBackdropEffect effect,
        CornerRadius cornerRadius);

    /// <summary>
    /// Selects the shape the next rounded-rectangle fills/strokes describe:
    /// <c>0</c> = ordinary rounded rectangle (default), <c>1</c> = SuperEllipse
    /// (iOS-style squircle) with the given Lamé exponent. State, not a draw —
    /// callers set it, draw one or more rounded rectangles, then reset to 0.
    /// </summary>
    /// <remarks>
    /// Virtual (not abstract) with a no-op default so contexts that don't model
    /// a superellipse simply render a rounded rectangle. The recording context
    /// captures it as a command so cached/whole-frame replays reproduce the
    /// SuperEllipse intent in draw order (otherwise <see cref="Jalium.UI.Controls"/>
    /// Border would fall back to a geometry fill that draws out of order).
    /// </remarks>
    public virtual void SetShapeType(int type, float exponent) { }

    /// <summary>
    /// Pushes a transform onto the transform stack.
    /// </summary>
    /// <param name="transform">The transform to push.</param>
    public abstract void PushTransform(Transform transform);

    /// <summary>
    /// Pushes a clip region onto the clip stack.
    /// </summary>
    /// <param name="clipGeometry">The clipping geometry.</param>
    public abstract void PushClip(Geometry clipGeometry);

    /// <summary>
    /// Pushes an opacity value onto the opacity stack.
    /// </summary>
    /// <param name="opacity">The opacity (0.0 - 1.0).</param>
    public abstract void PushOpacity(double opacity);

    /// <summary>Pushes animated opacity using its current supplied value.</summary>
    public abstract void PushOpacity(double opacity, AnimationClock? opacityAnimations);

    /// <summary>Pushes a guideline set onto the drawing stack.</summary>
    public abstract void PushGuidelineSet(GuidelineSet guidelines);

    /// <summary>Pushes an opacity mask onto the drawing stack.</summary>
    public abstract void PushOpacityMask(Brush opacityMask);

    /// <summary>Pushes a deprecated bitmap effect onto the drawing stack.</summary>
    [Obsolete("BitmapEffect is deprecated. Use Effect instead.")]
    public abstract void PushEffect(
        Effects.BitmapEffect effect,
        Effects.BitmapEffectInput effectInput);

    /// <summary>
    /// Pops the most recent transform, clip, or opacity.
    /// </summary>
    public abstract void Pop();

    /// <summary>
    /// Pushes a per-draw effect on top of the current render state. Subsequent
    /// draw calls are captured into an offscreen bitmap of <paramref name="captureBounds"/>,
    /// and the captured bitmap is piped through <paramref name="effect"/> when
    /// <see cref="PopEffect"/> is called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="UIElement"/>'s whole-element <c>Effect</c> (applied
    /// automatically by the render pipeline), <see cref="PushEffect(IEffect,Rect)"/> lets
    /// callers wrap an arbitrary range of draw calls — e.g. a single glyph in an
    /// animated text presenter — with its own effect. Each push allocates an
    /// offscreen capture target, so this is expensive per draw; use sparingly
    /// (per-cell rather than per-frame).
    /// </para>
    /// <para>
    /// Default implementation is a no-op, making this safe to call on drawing
    /// contexts that don't support effect capture (headless test contexts, PDF
    /// export, etc.). Implementations that honour it must match each
    /// <see cref="PushEffect(IEffect,Rect)"/> with exactly one <see cref="PopEffect"/>.
    /// </para>
    /// </remarks>
    /// <param name="effect">Effect to apply. Must be non-null; a null effect
    /// with a captureBounds push would still cost the capture without any
    /// shader pass, so we reject it outright.</param>
    /// <param name="captureBounds">Bounds (in local drawing coordinates) of
    /// the area to capture. Effect padding is applied on top by the context.</param>
    public virtual void PushEffect(IEffect effect, Rect captureBounds)
    {
        // Default no-op. Contexts that support offscreen capture override this
        // to open a capture scope.
    }

    /// <summary>
    /// Ends the most recent <see cref="PushEffect(IEffect,Rect)"/> scope: stops capturing,
    /// applies the pushed effect to the captured bitmap, and composes the
    /// result back onto the main render target at the captured bounds.
    /// </summary>
    /// <remarks>
    /// Must be paired one-for-one with <see cref="PushEffect(IEffect,Rect)"/> calls. Calling
    /// when no matching push is active is a no-op.
    /// </remarks>
    public virtual void PopEffect()
    {
        // Default no-op. See PushEffect.
    }

    /// <summary>
    /// Draws a liquid glass effect — SDF-based refraction with blur, tint,
    /// edge highlight, chromatic aberration, and optional fusion between
    /// sibling glass panels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default implementation is a no-op so drawing contexts that do not
    /// support the effect (test probes, PDF export, recorders with no
    /// override) simply skip it. The live render path in
    /// <c>RenderTargetDrawingContext</c> and the retained-mode recorder /
    /// replayer both override this to produce / record the draw.
    /// </para>
    /// <para>
    /// Parameters are carried in a <see cref="LiquidGlassParameters"/>
    /// reference object rather than an exploded argument list so the call
    /// survives the recorder round-trip without boxing each scalar into a
    /// fixed-size command payload.
    /// </para>
    /// </remarks>
    /// <param name="parameters">Effect parameters. The rectangle is in local
    /// drawing coordinates; implementations that render to an offset target
    /// must add <c>IOffsetDrawingContext.Offset</c> themselves.</param>
    public virtual void DrawLiquidGlass(LiquidGlassParameters parameters)
    {
        // Default no-op. See remarks.
    }

    /// <summary>
    /// Closes the drawing context.
    /// </summary>
    public abstract void Close();

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases resources owned by this drawing context.</summary>
    protected abstract void DisposeCore();

    /// <summary>Compatibility hook reserved for nonstructural API validation.</summary>
    protected virtual void VerifyApiNonstructuralChange()
    {
    }
}

/// <summary>
/// Migration adapter for Jalium drawing backends. DrawingContext itself exposes WPF's exact
/// abstract contract; existing backends inherit this adapter to map animated/current-value and
/// retained-drawing operations onto their real primitive implementations.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public abstract class DrawingContextAdapter : DrawingContext
{
    /// <summary>
    /// Compatibility dispatch point for Jalium backends that historically overrode
    /// <see cref="DrawingContext.DrawText(FormattedText, Point)"/>.
    /// </summary>
    public new virtual void DrawText(FormattedText formattedText, Point origin)
    {
        ArgumentNullException.ThrowIfNull(formattedText);
        DrawGeometry(formattedText.Foreground, null, formattedText.BuildGeometry(origin));
    }

    public override void DrawLine(
        Pen pen,
        Point point0,
        AnimationClock? point0Animations,
        Point point1,
        AnimationClock? point1Animations) => DrawLine(pen, point0, point1);

    public override void DrawDrawing(Drawing drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        drawing.RenderTo(this);
    }

    public override void DrawGlyphRun(Brush? foregroundBrush, GlyphRun glyphRun)
    {
        ArgumentNullException.ThrowIfNull(glyphRun);
        DrawGeometry(foregroundBrush, null, glyphRun.BuildGeometry());
    }

    public override void DrawRectangle(
        Brush? brush,
        Pen? pen,
        Rect rectangle,
        AnimationClock? rectangleAnimations) => DrawRectangle(brush, pen, rectangle);

    public override void DrawRoundedRectangle(
        Brush? brush,
        Pen? pen,
        Rect rectangle,
        AnimationClock? rectangleAnimations,
        double radiusX,
        AnimationClock? radiusXAnimations,
        double radiusY,
        AnimationClock? radiusYAnimations) =>
        DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);

    public override void DrawEllipse(
        Brush? brush,
        Pen? pen,
        Point center,
        AnimationClock? centerAnimations,
        double radiusX,
        AnimationClock? radiusXAnimations,
        double radiusY,
        AnimationClock? radiusYAnimations) =>
        DrawEllipse(brush, pen, center, radiusX, radiusY);

    public override void DrawImage(
        ImageSource imageSource,
        Rect rectangle,
        AnimationClock? rectangleAnimations) => DrawImage(imageSource, rectangle);

    public override void DrawVideo(MediaPlayer player, Rect rectangle)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.CurrentFrame is ImageSource frame)
        {
            DrawImage(frame, rectangle);
        }
    }

    public override void DrawVideo(
        MediaPlayer player,
        Rect rectangle,
        AnimationClock? rectangleAnimations) => DrawVideo(player, rectangle);

    public override void PushOpacity(double opacity, AnimationClock? opacityAnimations) =>
        PushOpacity(opacity);

    public override void PushGuidelineSet(GuidelineSet guidelines)
    {
        ArgumentNullException.ThrowIfNull(guidelines);
    }

    public override void PushOpacityMask(Brush opacityMask)
    {
        ArgumentNullException.ThrowIfNull(opacityMask);
    }

    [Obsolete("BitmapEffect is deprecated. Use Effect instead.")]
    public override void PushEffect(
        Effects.BitmapEffect effect,
        Effects.BitmapEffectInput effectInput)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(effectInput);
    }

    protected override void DisposeCore() => Close();
}

/// <summary>
/// Describes how a shape is outlined.
/// </summary>
public sealed class Pen : Animatable
{
    public static readonly DependencyProperty BrushProperty =
        DependencyProperty.Register(nameof(Brush), typeof(Brush), typeof(Pen), new PropertyMetadata(null));

    public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(Pen), new PropertyMetadata(1.0));

    public static readonly DependencyProperty StartLineCapProperty =
        DependencyProperty.Register(nameof(StartLineCap), typeof(PenLineCap), typeof(Pen), new PropertyMetadata(PenLineCap.Flat));

    public static readonly DependencyProperty EndLineCapProperty =
        DependencyProperty.Register(nameof(EndLineCap), typeof(PenLineCap), typeof(Pen), new PropertyMetadata(PenLineCap.Flat));

    public static readonly DependencyProperty DashCapProperty =
        DependencyProperty.Register(nameof(DashCap), typeof(PenLineCap), typeof(Pen), new PropertyMetadata(PenLineCap.Square));

    public static readonly DependencyProperty LineJoinProperty =
        DependencyProperty.Register(nameof(LineJoin), typeof(PenLineJoin), typeof(Pen), new PropertyMetadata(PenLineJoin.Miter));

    public static readonly DependencyProperty MiterLimitProperty =
        DependencyProperty.Register(nameof(MiterLimit), typeof(double), typeof(Pen), new PropertyMetadata(10.0));

    public static readonly DependencyProperty DashStyleProperty =
        DependencyProperty.Register(nameof(DashStyle), typeof(DashStyle), typeof(Pen), new PropertyMetadata(null));

    /// <summary>Gets or sets the brush for the stroke.</summary>
    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke thickness. Must be non-negative.
    /// </summary>
    public double Thickness
    {
        get => (double)(GetValue(ThicknessProperty) ?? 1.0);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the line cap style for the start of the line.
    /// </summary>
    public PenLineCap StartLineCap
    {
        get => (PenLineCap)(GetValue(StartLineCapProperty) ?? PenLineCap.Flat);
        set => SetValue(StartLineCapProperty, value);
    }

    /// <summary>
    /// Gets or sets the line cap style for the end of the line.
    /// </summary>
    public PenLineCap EndLineCap
    {
        get => (PenLineCap)(GetValue(EndLineCapProperty) ?? PenLineCap.Flat);
        set => SetValue(EndLineCapProperty, value);
    }

    /// <summary>
    /// Gets or sets the dash cap style.
    /// </summary>
    public PenLineCap DashCap
    {
        get => (PenLineCap)(GetValue(DashCapProperty) ?? PenLineCap.Square);
        set => SetValue(DashCapProperty, value);
    }

    /// <summary>
    /// Gets or sets the line join style.
    /// </summary>
    public PenLineJoin LineJoin
    {
        get => (PenLineJoin)(GetValue(LineJoinProperty) ?? PenLineJoin.Miter);
        set => SetValue(LineJoinProperty, value);
    }

    /// <summary>
    /// Gets or sets the miter limit.
    /// </summary>
    public double MiterLimit
    {
        get => (double)(GetValue(MiterLimitProperty) ?? 10.0);
        set => SetValue(MiterLimitProperty, value);
    }

    /// <summary>
    /// Gets or sets the dash style.
    /// </summary>
    public DashStyle DashStyle
    {
        get => (DashStyle?)GetValue(DashStyleProperty) ?? DashStyles.Solid;
        set => SetValue(DashStyleProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pen"/> class.
    /// </summary>
    public Pen()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pen"/> class.
    /// </summary>
    /// <param name="brush">The brush for the stroke.</param>
    /// <param name="thickness">The stroke thickness.</param>
    public Pen(Brush brush, double thickness)
    {
        Brush = brush;
        Thickness = thickness;
    }

    /// <summary>
    /// Creates a copy of this pen (mirrors WPF <c>Pen.Clone</c>). The <see cref="Brush"/> is
    /// shared (brushes are not yet cloneable); the <see cref="DashStyle"/> is deep-copied so
    /// the clone's dash pattern is independent of the original.
    /// </summary>
    public new Pen Clone() => (Pen)base.Clone();

    public new Pen CloneCurrentValue() => (Pen)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new Pen();
}

/// <summary>
/// Describes the dash pattern of a stroke.
/// </summary>
public sealed class DashStyle : Animatable
{
    public static readonly DependencyProperty DashesProperty =
        DependencyProperty.Register(nameof(Dashes), typeof(DoubleCollection), typeof(DashStyle), new PropertyMetadata(null));

    public static readonly DependencyProperty OffsetProperty =
        DependencyProperty.Register(nameof(Offset), typeof(double), typeof(DashStyle), new PropertyMetadata(0.0));

    /// <summary>Gets or sets the collection of dash lengths.</summary>
    public DoubleCollection Dashes
    {
        get
        {
            if (GetValue(DashesProperty) is DoubleCollection value)
            {
                return value;
            }

            value = new DoubleCollection();
            SetCurrentValue(DashesProperty, value);
            return value;
        }
        set => SetValue(DashesProperty, value);
    }

    /// <summary>
    /// Gets or sets the offset to start the dash pattern.
    /// </summary>
    public double Offset
    {
        get => (double)(GetValue(OffsetProperty) ?? 0.0);
        set => SetValue(OffsetProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DashStyle"/> class.
    /// </summary>
    public DashStyle()
    {
        SetCurrentValue(DashesProperty, new DoubleCollection());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DashStyle"/> class.
    /// </summary>
    /// <param name="dashes">The dash array.</param>
    /// <param name="offset">The dash offset.</param>
    public DashStyle(IEnumerable<double> dashes, double offset = 0)
    {
        ArgumentNullException.ThrowIfNull(dashes);
        Dashes = new DoubleCollection(dashes);
        Offset = offset;
    }

    public new DashStyle Clone() => (DashStyle)base.Clone();

    public new DashStyle CloneCurrentValue() => (DashStyle)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new DashStyle();

    /// <summary>
    /// Gets a solid (no dashes) style.
    /// </summary>
    public static DashStyle Solid => new();

    /// <summary>
    /// Gets a dash style.
    /// </summary>
    public static DashStyle Dash => new(new[] { 2.0, 2.0 });

    /// <summary>
    /// Gets a dot style.
    /// </summary>
    public static DashStyle Dot => new(new[] { 0.0, 2.0 });

    /// <summary>
    /// Gets a dash-dot style.
    /// </summary>
    public static DashStyle DashDot => new(new[] { 2.0, 2.0, 0.0, 2.0 });

    /// <summary>
    /// Gets a dash-dot-dot style.
    /// </summary>
    public static DashStyle DashDotDot => new(new[] { 2.0, 2.0, 0.0, 2.0, 0.0, 2.0 });
}

/// <summary>
/// Specifies the shape at the end of a line.
/// </summary>
public enum PenLineCap
{
    /// <summary>
    /// A cap that does not extend past the end point.
    /// </summary>
    Flat = 0,

    /// <summary>
    /// A semicircular cap.
    /// </summary>
    Round = 2,

    /// <summary>
    /// A triangular cap.
    /// </summary>
    Triangle = 3,

    /// <summary>
    /// A square cap that extends past the end point.
    /// </summary>
    Square = 1
}

/// <summary>
/// Specifies how lines are joined.
/// </summary>
public enum PenLineJoin
{
    /// <summary>
    /// Lines are joined with a sharp corner.
    /// </summary>
    Miter,

    /// <summary>
    /// Lines are joined with a beveled corner.
    /// </summary>
    Bevel,

    /// <summary>
    /// Lines are joined with a rounded corner.
    /// </summary>
    Round
}

/// <summary>
/// Represents formatted text for rendering and measurement.
/// </summary>
public sealed partial class FormattedText
{
    /// <summary>
    /// Gets the text content.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the font family name.
    /// </summary>
    public string FontFamily { get; private set; }

    /// <summary>
    /// Gets the font size.
    /// </summary>
    public double FontSize { get; private set; }

    /// <summary>
    /// Gets or sets the foreground brush.
    /// </summary>
    public Brush? Foreground { get; set; }

    /// <summary>
    /// Gets or sets the maximum width for text wrapping.
    /// </summary>
    public double MaxTextWidth
    {
        get => _maxTextWidth;
        set
        {
            ValidateMaxTextWidth(value, nameof(value));
            _maxTextWidth = value;
            _maxTextWidths = null;
            RecomputeApproximateMetrics();
        }
    }

    /// <summary>
    /// Gets or sets the maximum height.
    /// </summary>
    public double MaxTextHeight
    {
        get => _maxTextHeight;
        set
        {
            ValidateMaxTextHeight(value, nameof(value));
            _maxTextHeight = value;
            RecomputeApproximateMetrics();
        }
    }

    /// <summary>
    /// Gets or sets the text trimming mode.
    /// </summary>
    public TextTrimming Trimming { get; set; } = TextTrimming.None;

    /// <summary>
    /// Gets or sets the font weight (400 = normal, 700 = bold).
    /// </summary>
    public int FontWeight { get; set; } = 400;

    /// <summary>
    /// Gets or sets the font style (0 = normal, 1 = italic, 2 = oblique).
    /// </summary>
    public int FontStyle { get; set; } = 0;

    /// <summary>
    /// Gets or sets the font stretch (5 = normal). Values 1-9 map to DirectWrite font stretch.
    /// </summary>
    public int FontStretch { get; set; } = 5;

    /// <summary>
    /// Gets or sets the per-element text rendering (anti-alias) mode resolved
    /// from <c>TextOptions.TextRenderingMode</c> on the source element.
    /// 0 = Auto (process-wide fallback), 1 = Aliased, 2 = Grayscale, 3 = ClearType.
    /// The renderer forwards this to the native glyph atlas so each element
    /// can render in its own mode within the same frame instead of inheriting
    /// the process-wide value.
    /// </summary>
    public int TextRenderingMode { get; set; }

    /// <summary>
    /// Gets or sets the per-element text formatting mode resolved from
    /// <c>TextOptions.TextFormattingMode</c> on the source element.
    /// 0 = Ideal (WPF default — resolution-independent metrics),
    /// 1 = Display (pixel-snapped metrics — sharper at small sizes).
    /// </summary>
    public int TextFormattingMode { get; set; }

    /// <summary>
    /// Gets or sets the per-element text hinting mode resolved from
    /// <c>TextOptions.TextHintingMode</c> on the source element.
    /// 0 = Auto, 1 = Fixed (full hinting), 2 = Animated (no hinting — smoother
    /// sub-pixel motion through animations).
    /// </summary>
    public int TextHintingMode { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FormattedText"/> class.
    /// </summary>
    public FormattedText(string text, string fontFamily, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fontFamily);
        ValidateEmSize(fontSize, nameof(fontSize));

        Text = text;
        FontFamily = fontFamily;
        FontSize = fontSize;
        InitializeCompatibilityFormatting();
    }

    /// <summary>
    /// Gets the width of the text layout.
    /// </summary>
    public double Width { get; internal set; }

    /// <summary>
    /// Gets the height of the text layout.
    /// </summary>
    public double Height { get; internal set; }

    /// <summary>
    /// Gets the natural line height (ascent + descent + line gap).
    /// This is the WPF-style line height based on actual font metrics.
    /// </summary>
    public double LineHeight { get; internal set; }

    /// <summary>
    /// Gets the font ascent (distance from baseline to top of the tallest glyph).
    /// </summary>
    public double Ascent { get; internal set; }

    /// <summary>
    /// Gets the font descent (distance from baseline to bottom of the lowest glyph).
    /// </summary>
    public double Descent { get; internal set; }

    /// <summary>
    /// Gets the recommended line gap between lines.
    /// </summary>
    public double LineGap { get; internal set; }

    /// <summary>
    /// Gets the baseline offset from the top of the line.
    /// </summary>
    public double Baseline { get; internal set; }

    /// <summary>
    /// Gets the number of lines in the text layout.
    /// </summary>
    public int LineCount { get; internal set; } = 1;

    /// <summary>
    /// Gets whether this text has been measured using native text measurement.
    /// </summary>
    public bool IsMeasured { get; internal set; }
}

/// <summary>
/// Base class for geometry objects.
/// </summary>
public abstract class Geometry : Animatable, IFormattable
{
    /// <summary>Identifies the <see cref="Transform"/> dependency property.</summary>
    public static readonly DependencyProperty TransformProperty =
        DependencyProperty.Register(nameof(Transform), typeof(Transform), typeof(Geometry),
            new PropertyMetadata(null));

    /// <summary>The tolerance used by the parameterless flattening APIs.</summary>
    public static double StandardFlatteningTolerance => 0.25;

    /// <summary>
    /// Gets the bounding box of this geometry.
    /// </summary>
    public abstract Rect Bounds { get; }

    /// <summary>
    /// Gets or sets a Transform to apply to this geometry.
    /// </summary>
    public Transform? Transform
    {
        get => (Transform?)GetValue(TransformProperty);
        set
        {
            if (ReferenceEquals(Transform, value))
                return;
            SetValue(TransformProperty, value);
            WritePostscript();
        }
    }

    /// <summary>Returns whether a non-default transform should be serialized.</summary>
    public bool ShouldSerializeTransform() => Transform is not null;

    /// <summary>
    /// Creates a Geometry from a path markup mini-language string.
    /// </summary>
    /// <param name="source">The path data string (e.g., "M 0,0 L 100,100 Z").</param>
    /// <returns>A PathGeometry parsed from the string.</returns>
    public static Geometry Parse(string source)
    {
        return PathMarkupParser.Parse(source);
    }

    /// <summary>
    /// Preserves Jalium source compatibility for code that historically assigned path markup
    /// strings directly to Path.Data while the property itself now has WPF's Geometry type.
    /// </summary>
    public static implicit operator Geometry?(string? source) =>
        string.IsNullOrWhiteSpace(source) ? null : Parse(source);

    /// <summary>
    /// Gets an empty Geometry.
    /// </summary>
    public static Geometry Empty { get; } = new GeometryGroup();

    /// <summary>
    /// Returns true if this geometry contains no drawable content. Subclasses override
    /// with a cheaper/exact test; the default uses the bounding box.
    /// </summary>
    public virtual bool IsEmpty() => Bounds.IsEmpty;

    /// <summary>
    /// Returns true if this geometry may contain curved (non-line) segments. Subclasses
    /// that can hold curves override; the default (rectangles, lines, etc.) is false.
    /// </summary>
    public virtual bool MayHaveCurves() => false;

    /// <summary>
    /// Returns true if the specified point is within the filled area of the geometry.
    /// Default implementation uses bounding box; subclasses can override for precision.
    /// </summary>
    public virtual bool FillContains(Point point)
    {
        return Bounds.Contains(point);
    }

    /// <summary>
    /// Returns true if the specified point is within the filled area,
    /// using the specified tolerance and considering hit-test rules.
    /// </summary>
    public virtual bool FillContains(Point point, double tolerance, ToleranceType type)
    {
        return FillContains(point);
    }

    /// <summary>Returns whether this geometry completely contains another geometry.</summary>
    public bool FillContains(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return FillContains(geometry, StandardFlatteningTolerance, ToleranceType.Absolute);
    }

    /// <summary>Returns whether this geometry completely contains another geometry.</summary>
    public bool FillContains(Geometry geometry, double tolerance, ToleranceType type)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return FillContainsWithDetail(geometry, tolerance, type) == IntersectionDetail.FullyContains;
    }

    /// <summary>
    /// Returns true if the specified point is on the stroke outline.
    /// </summary>
    public virtual bool StrokeContains(Pen pen, Point point)
    {
        if (pen == null) return false;
        var bounds = Bounds;
        var half = pen.Thickness / 2;
        var outer = new Rect(bounds.X - half, bounds.Y - half,
            bounds.Width + pen.Thickness, bounds.Height + pen.Thickness);
        var inner = new Rect(bounds.X + half, bounds.Y + half,
            Math.Max(0, bounds.Width - pen.Thickness), Math.Max(0, bounds.Height - pen.Thickness));
        return outer.Contains(point) && !inner.Contains(point);
    }

    /// <summary>Tests a point against the stroked outline using the requested tolerance.</summary>
    public bool StrokeContains(Pen pen, Point hitPoint, double tolerance, ToleranceType type)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ValidateTolerance(tolerance);
        return StrokeContains(pen, hitPoint);
    }

    /// <summary>Gets the geometry bounds expanded by the supplied pen.</summary>
    public Rect GetRenderBounds(Pen pen) =>
        GetRenderBounds(pen, StandardFlatteningTolerance, ToleranceType.Absolute);

    /// <summary>Gets the geometry bounds expanded by the supplied pen.</summary>
    public virtual Rect GetRenderBounds(Pen pen, double tolerance, ToleranceType type)
    {
        ValidateTolerance(tolerance);
        Rect bounds = Bounds;
        if (bounds.IsEmpty || pen is null || pen.Thickness <= 0)
            return bounds;

        double half = pen.Thickness / 2.0;
        return new Rect(bounds.X - half, bounds.Y - half,
            bounds.Width + pen.Thickness, bounds.Height + pen.Thickness);
    }

    /// <summary>
    /// Gets a flattened version of this geometry (all curves approximated as line segments).
    /// </summary>
    public virtual PathGeometry GetFlattenedPathGeometry()
    {
        return GetFlattenedPathGeometry(0.25, ToleranceType.Absolute);
    }

    /// <summary>
    /// Gets a flattened version of this geometry with the specified tolerance.
    /// </summary>
    public virtual PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        // Default: return empty. Subclasses override with meaningful implementations.
        return new PathGeometry();
    }

    /// <summary>
    /// Gets the area of this geometry.
    /// </summary>
    public virtual double GetArea()
    {
        var flat = GetFlattenedPathGeometry();
        return flat.ComputeArea();
    }

    /// <summary>Gets the filled area using the requested flattening tolerance.</summary>
    public virtual double GetArea(double tolerance, ToleranceType type)
    {
        ValidateTolerance(tolerance);
        return GetFlattenedPathGeometry(tolerance, type).ComputeArea();
    }

    /// <summary>
    /// Gets a PathGeometry that is the outline of the filled region produced
    /// by stroking this geometry with the specified pen.
    /// </summary>
    /// <param name="pen">The pen describing the stroke to widen.</param>
    /// <returns>A PathGeometry representing the widened stroke outline.</returns>
    public PathGeometry GetWidenedPathGeometry(Pen pen)
    {
        return GetWidenedPathGeometry(pen, 0.25, ToleranceType.Absolute);
    }

    /// <summary>
    /// Gets a PathGeometry that is the outline of the filled region produced
    /// by stroking this geometry with the specified pen, using the given tolerance.
    /// </summary>
    /// <param name="pen">The pen describing the stroke to widen.</param>
    /// <param name="tolerance">The flattening tolerance.</param>
    /// <param name="toleranceType">How the tolerance value is interpreted.</param>
    /// <returns>A PathGeometry representing the widened stroke outline.</returns>
    public virtual PathGeometry GetWidenedPathGeometry(Pen pen, double tolerance, ToleranceType toleranceType)
    {
        // Default: flatten, then widen the flattened geometry
        var flat = GetFlattenedPathGeometry(tolerance, toleranceType);
        return flat.GetWidenedPathGeometry(pen, tolerance, toleranceType);
    }

    /// <summary>
    /// Gets a simplified PathGeometry that represents the outline of this geometry
    /// with self-intersections removed.
    /// </summary>
    /// <returns>A simplified PathGeometry.</returns>
    public PathGeometry GetOutlinedPathGeometry()
    {
        return GetOutlinedPathGeometry(0.25, ToleranceType.Absolute);
    }

    /// <summary>
    /// Gets a simplified PathGeometry that represents the outline of this geometry
    /// with self-intersections removed, using the given tolerance.
    /// </summary>
    /// <param name="tolerance">The flattening tolerance.</param>
    /// <param name="toleranceType">How the tolerance value is interpreted.</param>
    /// <returns>A simplified PathGeometry.</returns>
    public virtual PathGeometry GetOutlinedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        return GetFlattenedPathGeometry(tolerance, toleranceType);
    }

    /// <summary>
    /// Determines the intersection relationship between this geometry and another geometry
    /// based on their filled areas.
    /// </summary>
    public virtual IntersectionDetail FillContainsWithDetail(Geometry geometry)
    {
        var thisBounds = Bounds;
        var otherBounds = geometry.Bounds;

        // Check for no intersection
        var intersection = Rect.Intersect(thisBounds, otherBounds);
        if (intersection.Width <= 0 || intersection.Height <= 0)
            return IntersectionDetail.Empty;

        // Check if this geometry fully contains the other
        if (thisBounds.Contains(otherBounds))
            return IntersectionDetail.FullyContains;

        // Check if this geometry is fully inside the other
        if (otherBounds.Contains(thisBounds))
            return IntersectionDetail.FullyInside;

        return IntersectionDetail.Intersects;
    }

    /// <summary>Gets the fill intersection relationship using the requested tolerance.</summary>
    public virtual IntersectionDetail FillContainsWithDetail(
        Geometry geometry,
        double tolerance,
        ToleranceType type)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ValidateTolerance(tolerance);
        return FillContainsWithDetail(geometry);
    }

    /// <summary>
    /// Determines the intersection relationship between this geometry's stroke and another geometry.
    /// </summary>
    public virtual IntersectionDetail StrokeContainsWithDetail(Pen pen, Geometry geometry)
    {
        if (pen == null) return IntersectionDetail.Empty;

        var half = pen.Thickness / 2;
        var thisBounds = Bounds;
        var strokeBounds = new Rect(
            thisBounds.X - half, thisBounds.Y - half,
            thisBounds.Width + pen.Thickness, thisBounds.Height + pen.Thickness);
        var otherBounds = geometry.Bounds;

        var intersection = Rect.Intersect(strokeBounds, otherBounds);
        if (intersection.Width <= 0 || intersection.Height <= 0)
            return IntersectionDetail.Empty;

        if (strokeBounds.Contains(otherBounds))
            return IntersectionDetail.FullyContains;

        if (otherBounds.Contains(strokeBounds))
            return IntersectionDetail.FullyInside;

        return IntersectionDetail.Intersects;
    }

    /// <summary>Gets the stroke intersection relationship using the requested tolerance.</summary>
    public IntersectionDetail StrokeContainsWithDetail(
        Pen pen,
        Geometry geometry,
        double tolerance,
        ToleranceType type)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(geometry);
        ValidateTolerance(tolerance);
        return StrokeContainsWithDetail(pen, geometry);
    }

    /// <summary>
    /// Combines two geometries using the specified combine mode and optional transform.
    /// This is a simplified implementation; full CSG boolean operations are extremely complex.
    /// </summary>
    /// <param name="geometry1">The first geometry.</param>
    /// <param name="geometry2">The second geometry.</param>
    /// <param name="mode">The combine mode.</param>
    /// <param name="transform">An optional transform to apply to the result.</param>
    /// <returns>A PathGeometry representing the combined result.</returns>
    public static PathGeometry Combine(Geometry geometry1, Geometry geometry2, GeometryCombineMode mode, Transform? transform)
    {
        var flat1 = geometry1.GetFlattenedPathGeometry();
        var flat2 = geometry2.GetFlattenedPathGeometry();
        var result = new PathGeometry();

        switch (mode)
        {
            case GeometryCombineMode.Union:
                // Merge all figures from both geometries
                result.Figures.AddRange(flat1.Figures);
                result.Figures.AddRange(flat2.Figures);
                break;

            case GeometryCombineMode.Intersect:
                // Sutherland-Hodgman polygon clipping between figures
                foreach (var fig1 in flat1.Figures)
                {
                    if (!fig1.IsFilled) continue;
                    var subjectPoly = ExtractPolygonPoints(fig1);
                    if (subjectPoly.Count < 3) continue;

                    foreach (var fig2 in flat2.Figures)
                    {
                        if (!fig2.IsFilled) continue;
                        var clipPoly = ExtractPolygonPoints(fig2);
                        if (clipPoly.Count < 3) continue;

                        var clipped = SutherlandHodgmanClip(subjectPoly, clipPoly);
                        if (clipped.Count >= 3)
                        {
                            var figure = new PathFigure
                            {
                                StartPoint = clipped[0],
                                IsClosed = true,
                                IsFilled = true
                            };
                            for (int i = 1; i < clipped.Count; i++)
                                figure.Segments.Add(new LineSegment(clipped[i]));
                            result.Figures.Add(figure);
                        }
                    }
                }
                break;

            case GeometryCombineMode.Exclude:
                // Add geometry1 figures as-is
                result.Figures.AddRange(flat1.Figures);
                // Add geometry2 figures with reversed winding order
                foreach (var fig in flat2.Figures)
                {
                    var reversed = ReverseFigureWinding(fig);
                    result.Figures.Add(reversed);
                }
                break;

            case GeometryCombineMode.Xor:
                // Xor = Union minus Intersect: add all figures from both, then add
                // reversed intersection figures to cut out the overlap
                result.Figures.AddRange(flat1.Figures);
                result.Figures.AddRange(flat2.Figures);

                // Compute intersection and add reversed
                foreach (var fig1 in flat1.Figures)
                {
                    if (!fig1.IsFilled) continue;
                    var subjectPoly = ExtractPolygonPoints(fig1);
                    if (subjectPoly.Count < 3) continue;

                    foreach (var fig2 in flat2.Figures)
                    {
                        if (!fig2.IsFilled) continue;
                        var clipPoly = ExtractPolygonPoints(fig2);
                        if (clipPoly.Count < 3) continue;

                        var clipped = SutherlandHodgmanClip(subjectPoly, clipPoly);
                        if (clipped.Count >= 3)
                        {
                            // Add the intersection twice reversed to cancel out overlap
                            var figure = new PathFigure
                            {
                                StartPoint = clipped[0],
                                IsClosed = true,
                                IsFilled = true
                            };
                            for (int i = 1; i < clipped.Count; i++)
                                figure.Segments.Add(new LineSegment(clipped[i]));

                            result.Figures.Add(ReverseFigureWinding(figure));
                            result.Figures.Add(ReverseFigureWinding(figure));
                        }
                    }
                }
                break;
        }

        // Apply transform if provided
        if (transform != null)
        {
            var m = transform.Value;
            foreach (var figure in result.Figures)
            {
                figure.StartPoint = TransformPointByMatrix(figure.StartPoint, m);
                foreach (var segment in figure.Segments)
                {
                    if (segment is LineSegment ls)
                        ls.Point = TransformPointByMatrix(ls.Point, m);
                    else if (segment is PolyLineSegment pls)
                    {
                        for (int i = 0; i < pls.Points.Count; i++)
                            pls.Points[i] = TransformPointByMatrix(pls.Points[i], m);
                    }
                }
            }
        }

        // Boolean output is expressed with the nonzero winding rule: outer contours, holes,
        // and the reversed clip contours (Exclude/Xor) cancel by winding sign — which the
        // even-odd default cannot represent. Previously this was left at PathGeometry's
        // default (EvenOdd), so Exclude/Xor produced wrong results and Union hollowed out
        // overlaps. (Exact arbitrary concave/self-intersecting boolean still needs a full
        // plane-sweep solver; this correctly handles the common winding-regular inputs.)
        result.FillRule = FillRule.Nonzero;
        return result;
    }

    /// <summary>Combines two geometries using an explicit flattening tolerance.</summary>
    public static PathGeometry Combine(
        Geometry geometry1,
        Geometry geometry2,
        GeometryCombineMode mode,
        Transform? transform,
        double tolerance,
        ToleranceType type)
    {
        ArgumentNullException.ThrowIfNull(geometry1);
        ArgumentNullException.ThrowIfNull(geometry2);
        ValidateTolerance(tolerance);

        PathGeometry left = geometry1.GetFlattenedPathGeometry(tolerance, type);
        PathGeometry right = geometry2.GetFlattenedPathGeometry(tolerance, type);
        return Combine(left, right, mode, transform);
    }

    private static Point TransformPointByMatrix(Point p, Matrix m)
    {
        return new Point(
            p.X * m.M11 + p.Y * m.M21 + m.OffsetX,
            p.X * m.M12 + p.Y * m.M22 + m.OffsetY);
    }

    /// <summary>
    /// Extracts polygon points from a flattened path figure.
    /// </summary>
    private static List<Point> ExtractPolygonPoints(PathFigure figure)
    {
        var pts = new List<Point> { figure.StartPoint };
        foreach (var seg in figure.Segments)
        {
            if (seg is LineSegment ls) pts.Add(ls.Point);
            else if (seg is PolyLineSegment pls) pts.AddRange(pls.Points);
        }
        return pts;
    }

    /// <summary>
    /// Sutherland-Hodgman polygon clipping algorithm.
    /// Clips the subject polygon against the clip polygon.
    /// </summary>
    private static List<Point> SutherlandHodgmanClip(List<Point> subject, List<Point> clip)
    {
        var output = new List<Point>(subject);

        for (int i = 0; i < clip.Count && output.Count > 0; i++)
        {
            var input = new List<Point>(output);
            output.Clear();

            var edgeStart = clip[i];
            var edgeEnd = clip[(i + 1) % clip.Count];

            for (int j = 0; j < input.Count; j++)
            {
                var current = input[j];
                var previous = input[(j + input.Count - 1) % input.Count];

                bool currentInside = IsInsideEdge(current, edgeStart, edgeEnd);
                bool previousInside = IsInsideEdge(previous, edgeStart, edgeEnd);

                if (currentInside)
                {
                    if (!previousInside)
                    {
                        // Entering: add intersection point
                        var intersection = LineIntersection(previous, current, edgeStart, edgeEnd);
                        if (intersection.HasValue)
                            output.Add(intersection.Value);
                    }
                    output.Add(current);
                }
                else if (previousInside)
                {
                    // Leaving: add intersection point
                    var intersection = LineIntersection(previous, current, edgeStart, edgeEnd);
                    if (intersection.HasValue)
                        output.Add(intersection.Value);
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Determines if a point is on the inside (left side) of a directed edge.
    /// </summary>
    private static bool IsInsideEdge(Point p, Point edgeStart, Point edgeEnd)
    {
        return (edgeEnd.X - edgeStart.X) * (p.Y - edgeStart.Y) -
               (edgeEnd.Y - edgeStart.Y) * (p.X - edgeStart.X) >= 0;
    }

    /// <summary>
    /// Computes the intersection point of two line segments.
    /// </summary>
    private static Point? LineIntersection(Point p1, Point p2, Point p3, Point p4)
    {
        double x1 = p1.X, y1 = p1.Y, x2 = p2.X, y2 = p2.Y;
        double x3 = p3.X, y3 = p3.Y, x4 = p4.X, y4 = p4.Y;

        double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(denom) < 1e-10)
            return null;

        double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
        return new Point(x1 + t * (x2 - x1), y1 + t * (y2 - y1));
    }

    /// <summary>
    /// Reverses the winding order of a path figure.
    /// </summary>
    private static PathFigure ReverseFigureWinding(PathFigure figure)
    {
        var pts = ExtractPolygonPoints(figure);
        if (pts.Count < 2) return figure;

        pts.Reverse();

        var reversed = new PathFigure
        {
            StartPoint = pts[0],
            IsClosed = figure.IsClosed,
            IsFilled = figure.IsFilled
        };
        for (int i = 1; i < pts.Count; i++)
            reversed.Segments.Add(new LineSegment(pts[i]));

        return reversed;
    }

    /// <summary>
    /// Creates a deep copy of this geometry.
    /// </summary>
    public new Geometry Clone() => (Geometry)base.Clone();

    /// <summary>Creates a modifiable clone using current animated values.</summary>
    public new Geometry CloneCurrentValue() => (Geometry)base.CloneCurrentValue();

    /// <inheritdoc />
    public override string ToString() => ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Formats the geometry as path markup.</summary>
    public string ToString(IFormatProvider? provider) =>
        GetFlattenedPathGeometry(StandardFlatteningTolerance, ToleranceType.Absolute)
            .Figures.ToString(provider);

    string IFormattable.ToString(string? format, IFormatProvider? provider) =>
        ToString(provider);

    protected static void ValidateTolerance(double tolerance)
    {
        if (double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
    }

    /// <summary>
    /// Applies the geometry's Transform to a point.
    /// </summary>
    protected Point TransformPoint(Point p)
    {
        if (Transform == null) return p;
        var m = Transform.Value;
        return new Point(
            p.X * m.M11 + p.Y * m.M21 + m.OffsetX,
            p.X * m.M12 + p.Y * m.M22 + m.OffsetY);
    }
}

/// <summary>
/// Specifies how tolerance values are interpreted.
/// </summary>
public enum ToleranceType
{
    Absolute,
    Relative
}

/// <summary>
/// Represents a rectangle geometry with optional rounded corners.
/// </summary>
public sealed class RectangleGeometry : Geometry
{
    public static readonly DependencyProperty RectProperty =
        DependencyProperty.Register(nameof(Rect), typeof(Rect), typeof(RectangleGeometry),
            new PropertyMetadata(Rect.Empty));

    public static readonly DependencyProperty RadiusXProperty =
        DependencyProperty.Register(nameof(RadiusX), typeof(double), typeof(RectangleGeometry),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty RadiusYProperty =
        DependencyProperty.Register(nameof(RadiusY), typeof(double), typeof(RectangleGeometry),
            new PropertyMetadata(0.0));

    private CornerRadius _cornerRadius;

    /// <summary>
    /// Gets or sets the rectangle.
    /// </summary>
    public Rect Rect
    {
        get => (Rect)GetValue(RectProperty)!;
        set { SetValue(RectProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets the X radius of the corners.
    /// </summary>
    public double RadiusX
    {
        get => (double)GetValue(RadiusXProperty)!;
        set { SetValue(RadiusXProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets the Y radius of the corners.
    /// </summary>
    public double RadiusY
    {
        get => (double)GetValue(RadiusYProperty)!;
        set { SetValue(RadiusYProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Optional per-corner radii. When any component is non-zero, this overrides
    /// <see cref="RadiusX"/>/<see cref="RadiusY"/> for downstream consumers that
    /// support per-corner rounded rects (e.g. native rounded-rect clip).
    /// </summary>
    public CornerRadius CornerRadius
    {
        get => _cornerRadius;
        set
        {
            WritePreamble();
            _cornerRadius = value;
            WritePostscript();
        }
    }

    /// <summary>
    /// True when <see cref="CornerRadius"/> carries non-uniform per-corner data
    /// (i.e. the corners aren't all equal); consumers should honor it instead of
    /// the scalar <see cref="RadiusX"/>/<see cref="RadiusY"/>.
    /// </summary>
    public bool HasPerCornerRadii =>
        CornerRadius.TopLeft != 0 || CornerRadius.TopRight != 0 ||
        CornerRadius.BottomRight != 0 || CornerRadius.BottomLeft != 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangleGeometry"/> class.
    /// </summary>
    public RectangleGeometry()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangleGeometry"/> class.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    public RectangleGeometry(Rect rect)
    {
        Rect = rect;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangleGeometry"/> class with rounded corners.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="radiusX">The X radius of the corners.</param>
    /// <param name="radiusY">The Y radius of the corners.</param>
    public RectangleGeometry(Rect rect, double radiusX, double radiusY)
    {
        Rect = rect;
        RadiusX = radiusX;
        RadiusY = radiusY;
    }

    public RectangleGeometry(Rect rect, double radiusX, double radiusY, Transform transform)
        : this(rect, radiusX, radiusY)
    {
        Transform = transform;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangleGeometry"/> class with per-corner radii.
    /// </summary>
    public RectangleGeometry(Rect rect, CornerRadius cornerRadius)
    {
        Rect = rect;
        CornerRadius = cornerRadius;
        var maxR = Math.Max(
            Math.Max(cornerRadius.TopLeft, cornerRadius.TopRight),
            Math.Max(cornerRadius.BottomRight, cornerRadius.BottomLeft));
        RadiusX = maxR;
        RadiusY = maxR;
    }

    /// <inheritdoc />
    public override Rect Bounds => Rect;

    /// <inheritdoc />
    public override bool FillContains(Point point)
    {
        if (RadiusX > 0 || RadiusY > 0)
        {
            // For rounded rects, use flattened path hit test
            return GetFlattenedPathGeometry().FillContains(point);
        }
        return Rect.Contains(point);
    }

    /// <inheritdoc />
    public override PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        var r = Rect;
        if (r.Width <= 0 || r.Height <= 0) return new PathGeometry();

        var geom = new PathGeometry();
        var figure = new PathFigure { IsClosed = true, IsFilled = true };

        if (RadiusX <= 0 && RadiusY <= 0)
        {
            figure.StartPoint = new Point(r.X, r.Y);
            figure.Segments.Add(new LineSegment(new Point(r.Right, r.Y)));
            figure.Segments.Add(new LineSegment(new Point(r.Right, r.Bottom)));
            figure.Segments.Add(new LineSegment(new Point(r.X, r.Bottom)));
        }
        else
        {
            var rx = Math.Min(RadiusX, r.Width / 2);
            var ry = Math.Min(RadiusY, r.Height / 2);
            // Start at top-left after the corner arc
            figure.StartPoint = new Point(r.X + rx, r.Y);
            // Top edge
            figure.Segments.Add(new LineSegment(new Point(r.Right - rx, r.Y)));
            // Top-right corner
            FlattenArcToSegments(figure, new Point(r.Right - rx, r.Y), new Point(r.Right, r.Y + ry),
                rx, ry, tolerance);
            // Right edge
            figure.Segments.Add(new LineSegment(new Point(r.Right, r.Bottom - ry)));
            // Bottom-right corner
            FlattenArcToSegments(figure, new Point(r.Right, r.Bottom - ry), new Point(r.Right - rx, r.Bottom),
                rx, ry, tolerance);
            // Bottom edge
            figure.Segments.Add(new LineSegment(new Point(r.X + rx, r.Bottom)));
            // Bottom-left corner
            FlattenArcToSegments(figure, new Point(r.X + rx, r.Bottom), new Point(r.X, r.Bottom - ry),
                rx, ry, tolerance);
            // Left edge
            figure.Segments.Add(new LineSegment(new Point(r.X, r.Y + ry)));
            // Top-left corner
            FlattenArcToSegments(figure, new Point(r.X, r.Y + ry), new Point(r.X + rx, r.Y),
                rx, ry, tolerance);
        }

        geom.Figures.Add(figure);
        return geom;
    }

    private static void FlattenArcToSegments(PathFigure figure, Point start, Point end,
        double rx, double ry, double tolerance)
    {
        var segments = Math.Max(4, (int)(Math.PI / 2 * Math.Max(rx, ry) / tolerance));
        var cx = (start.X < end.X) ? end.X : start.X;
        if (Math.Abs(start.X - end.X) < 1e-10)
            cx = start.X;
        var cy = (start.Y < end.Y) ? start.Y : end.Y;
        if (Math.Abs(start.Y - end.Y) < 1e-10)
            cy = start.Y;

        // Determine start angle from the start point relative to the center
        var startAngle = Math.Atan2((start.Y - cy) / ry, (start.X - cx) / rx);
        var endAngle = Math.Atan2((end.Y - cy) / ry, (end.X - cx) / rx);
        var delta = endAngle - startAngle;
        if (delta > Math.PI) delta -= 2 * Math.PI;
        if (delta < -Math.PI) delta += 2 * Math.PI;

        for (int i = 1; i <= segments; i++)
        {
            var t = i / (double)segments;
            var angle = startAngle + delta * t;
            var px = cx + rx * Math.Cos(angle);
            var py = cy + ry * Math.Sin(angle);
            figure.Segments.Add(new LineSegment(new Point(px, py)));
        }
    }

    /// <inheritdoc />
    public override double GetArea(double tolerance, ToleranceType type)
    {
        ValidateTolerance(tolerance);
        double width = Math.Max(0, Rect.Width);
        double height = Math.Max(0, Rect.Height);
        double rx = Math.Min(Math.Abs(RadiusX), width / 2.0);
        double ry = Math.Min(Math.Abs(RadiusY), height / 2.0);
        return width * height - (4.0 - Math.PI) * rx * ry;
    }

    public new RectangleGeometry Clone() => (RectangleGeometry)base.Clone();
    public new RectangleGeometry CloneCurrentValue() => (RectangleGeometry)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new RectangleGeometry();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        _cornerRadius = ((RectangleGeometry)source)._cornerRadius;
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        _cornerRadius = ((RectangleGeometry)source)._cornerRadius;
    }
}

/// <summary>
/// Represents an ellipse geometry.
/// </summary>
public sealed class EllipseGeometry : Geometry
{
    public static readonly DependencyProperty CenterProperty =
        DependencyProperty.Register(nameof(Center), typeof(Point), typeof(EllipseGeometry),
            new PropertyMetadata(new Point()));

    public static readonly DependencyProperty RadiusXProperty =
        DependencyProperty.Register(nameof(RadiusX), typeof(double), typeof(EllipseGeometry),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty RadiusYProperty =
        DependencyProperty.Register(nameof(RadiusY), typeof(double), typeof(EllipseGeometry),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Gets or sets the center point.
    /// </summary>
    public Point Center
    {
        get => (Point)GetValue(CenterProperty)!;
        set { SetValue(CenterProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets the X radius.
    /// </summary>
    public double RadiusX
    {
        get => (double)GetValue(RadiusXProperty)!;
        set { SetValue(RadiusXProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets the Y radius.
    /// </summary>
    public double RadiusY
    {
        get => (double)GetValue(RadiusYProperty)!;
        set { SetValue(RadiusYProperty, value); WritePostscript(); }
    }

    public EllipseGeometry()
    {
    }

    public EllipseGeometry(Point center, double radiusX, double radiusY)
    {
        Center = center;
        RadiusX = radiusX;
        RadiusY = radiusY;
    }

    public EllipseGeometry(Point center, double radiusX, double radiusY, Transform transform)
        : this(center, radiusX, radiusY)
    {
        Transform = transform;
    }

    public EllipseGeometry(Rect rect)
        : this(new Point(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0),
            rect.Width / 2.0, rect.Height / 2.0)
    {
    }

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            var rx = Math.Abs(RadiusX);
            var ry = Math.Abs(RadiusY);
            return new Rect(Center.X - rx, Center.Y - ry, rx * 2, ry * 2);
        }
    }

    /// <inheritdoc />
    public override bool FillContains(Point point)
    {
        var rx = Math.Abs(RadiusX);
        var ry = Math.Abs(RadiusY);
        if (rx < 1e-10 || ry < 1e-10) return false;
        var dx = point.X - Center.X;
        var dy = point.Y - Center.Y;
        return (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry) <= 1.0;
    }

    /// <inheritdoc />
    public override PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        var rx = Math.Abs(RadiusX);
        var ry = Math.Abs(RadiusY);
        if (rx < 1e-10 || ry < 1e-10) return new PathGeometry();

        var geom = new PathGeometry();
        var figure = new PathFigure { IsClosed = true, IsFilled = true };

        var segments = Math.Max(16, (int)(2 * Math.PI * Math.Max(rx, ry) / tolerance));
        figure.StartPoint = new Point(Center.X + rx, Center.Y);

        for (int i = 1; i <= segments; i++)
        {
            var angle = 2 * Math.PI * i / segments;
            figure.Segments.Add(new LineSegment(new Point(
                Center.X + rx * Math.Cos(angle),
                Center.Y + ry * Math.Sin(angle))));
        }

        geom.Figures.Add(figure);
        return geom;
    }

    /// <inheritdoc />
    public override double GetArea()
    {
        return Math.PI * Math.Abs(RadiusX) * Math.Abs(RadiusY);
    }

    public override double GetArea(double tolerance, ToleranceType type)
    {
        ValidateTolerance(tolerance);
        return GetArea();
    }

    /// <inheritdoc />
    public new EllipseGeometry Clone() => (EllipseGeometry)base.Clone();
    public new EllipseGeometry CloneCurrentValue() => (EllipseGeometry)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new EllipseGeometry();
}

/// <summary>
/// Represents a line geometry.
/// </summary>
public sealed class LineGeometry : Geometry
{
    public static readonly DependencyProperty StartPointProperty =
        DependencyProperty.Register(nameof(StartPoint), typeof(Point), typeof(LineGeometry),
            new PropertyMetadata(new Point()));

    public static readonly DependencyProperty EndPointProperty =
        DependencyProperty.Register(nameof(EndPoint), typeof(Point), typeof(LineGeometry),
            new PropertyMetadata(new Point()));

    /// <summary>
    /// Gets or sets the start point of the line.
    /// </summary>
    public Point StartPoint
    {
        get => (Point)GetValue(StartPointProperty)!;
        set { SetValue(StartPointProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets the end point of the line.
    /// </summary>
    public Point EndPoint
    {
        get => (Point)GetValue(EndPointProperty)!;
        set { SetValue(EndPointProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LineGeometry"/> class.
    /// </summary>
    public LineGeometry()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LineGeometry"/> class.
    /// </summary>
    /// <param name="startPoint">The start point.</param>
    /// <param name="endPoint">The end point.</param>
    public LineGeometry(Point startPoint, Point endPoint)
    {
        StartPoint = startPoint;
        EndPoint = endPoint;
    }

    public LineGeometry(Point startPoint, Point endPoint, Transform transform)
        : this(startPoint, endPoint)
    {
        Transform = transform;
    }

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            var w = Math.Abs(EndPoint.X - StartPoint.X);
            var h = Math.Abs(EndPoint.Y - StartPoint.Y);
            // For perfectly horizontal/vertical lines, ensure non-zero size
            // so the geometry is not treated as empty.
            if (w < 1e-10 && h < 1e-10) return new Rect(StartPoint.X, StartPoint.Y, 0, 0);
            return new Rect(
                Math.Min(StartPoint.X, EndPoint.X),
                Math.Min(StartPoint.Y, EndPoint.Y),
                w, h);
        }
    }

    /// <inheritdoc />
    public override bool FillContains(Point point)
    {
        // Lines have no fill area
        return false;
    }

    /// <inheritdoc />
    public override PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        var geom = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = StartPoint,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new LineSegment(EndPoint));
        geom.Figures.Add(figure);
        return geom;
    }

    /// <inheritdoc />
    public override double GetArea(double tolerance, ToleranceType type)
    {
        ValidateTolerance(tolerance);
        return 0.0;
    }

    public new LineGeometry Clone() => (LineGeometry)base.Clone();
    public new LineGeometry CloneCurrentValue() => (LineGeometry)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new LineGeometry();
}

/// <summary>
/// Represents a composite geometry made up of other geometry objects.
/// </summary>
public sealed class GeometryGroup : Geometry
{
    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(nameof(Children), typeof(GeometryCollection), typeof(GeometryGroup),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FillRuleProperty =
        DependencyProperty.Register(nameof(FillRule), typeof(FillRule), typeof(GeometryGroup),
            new PropertyMetadata(FillRule.EvenOdd));

    public GeometryGroup()
    {
        Children = new GeometryCollection();
    }

    /// <summary>
    /// Gets the collection of child geometries.
    /// </summary>
    public GeometryCollection Children
    {
        get => (GeometryCollection)GetValue(ChildrenProperty)!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            GeometryCollection? old = (GeometryCollection?)GetValue(ChildrenProperty);
            OnFreezablePropertyChanged(old, value, ChildrenProperty);
            SetValue(ChildrenProperty, value);
            WritePostscript();
        }
    }

    /// <summary>
    /// Gets or sets the fill rule for this geometry.
    /// </summary>
    public FillRule FillRule
    {
        get => (FillRule)GetValue(FillRuleProperty)!;
        set { SetValue(FillRuleProperty, value); WritePostscript(); }
    }

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            if (Children.Count == 0) return Rect.Empty;

            var result = Children[0].Bounds;
            for (int i = 1; i < Children.Count; i++)
            {
                result = Rect.Union(result, Children[i].Bounds);
            }
            return result;
        }
    }

    /// <inheritdoc />
    public override bool FillContains(Point point)
    {
        if (FillRule == FillRule.EvenOdd)
        {
            int count = 0;
            foreach (var child in Children)
            {
                if (child.FillContains(point)) count++;
            }
            return (count % 2) != 0;
        }
        else
        {
            // Nonzero: any child containing the point counts
            foreach (var child in Children)
            {
                if (child.FillContains(point)) return true;
            }
            return false;
        }
    }

    /// <inheritdoc />
    public override PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        var result = new PathGeometry { FillRule = FillRule };
        foreach (var child in Children)
        {
            var flat = child.GetFlattenedPathGeometry(tolerance, toleranceType);
            result.Figures.AddRange(flat.Figures);
        }
        return result;
    }

    /// <inheritdoc />
    public new GeometryGroup Clone() => (GeometryGroup)base.Clone();
    public new GeometryGroup CloneCurrentValue() => (GeometryGroup)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new GeometryGroup();
}

/// <summary>
/// Represents a geometry that is the combination of two geometries.
/// </summary>
public sealed class CombinedGeometry : Geometry
{
    public static readonly DependencyProperty Geometry1Property =
        DependencyProperty.Register(nameof(Geometry1), typeof(Geometry), typeof(CombinedGeometry),
            new PropertyMetadata(null));

    public static readonly DependencyProperty Geometry2Property =
        DependencyProperty.Register(nameof(Geometry2), typeof(Geometry), typeof(CombinedGeometry),
            new PropertyMetadata(null));

    public static readonly DependencyProperty GeometryCombineModeProperty =
        DependencyProperty.Register(nameof(GeometryCombineMode), typeof(GeometryCombineMode), typeof(CombinedGeometry),
            new PropertyMetadata(GeometryCombineMode.Union));

    public CombinedGeometry()
    {
    }

    public CombinedGeometry(Geometry geometry1, Geometry geometry2)
        : this(GeometryCombineMode.Union, geometry1, geometry2)
    {
    }

    public CombinedGeometry(GeometryCombineMode geometryCombineMode, Geometry geometry1, Geometry geometry2)
    {
        GeometryCombineMode = geometryCombineMode;
        Geometry1 = geometry1;
        Geometry2 = geometry2;
    }

    public CombinedGeometry(
        GeometryCombineMode geometryCombineMode,
        Geometry geometry1,
        Geometry geometry2,
        Transform transform)
        : this(geometryCombineMode, geometry1, geometry2)
    {
        Transform = transform;
    }

    /// <summary>
    /// Gets or sets the first geometry.
    /// </summary>
    public Geometry? Geometry1
    {
        get => (Geometry?)GetValue(Geometry1Property);
        set
        {
            Geometry? old = Geometry1;
            OnFreezablePropertyChanged(old, value, Geometry1Property);
            SetValue(Geometry1Property, value);
            WritePostscript();
        }
    }

    /// <summary>
    /// Gets or sets the second geometry.
    /// </summary>
    public Geometry? Geometry2
    {
        get => (Geometry?)GetValue(Geometry2Property);
        set
        {
            Geometry? old = Geometry2;
            OnFreezablePropertyChanged(old, value, Geometry2Property);
            SetValue(Geometry2Property, value);
            WritePostscript();
        }
    }

    /// <summary>
    /// Gets or sets the method used to combine the two geometries.
    /// </summary>
    public GeometryCombineMode GeometryCombineMode
    {
        get => (GeometryCombineMode)GetValue(GeometryCombineModeProperty)!;
        set { SetValue(GeometryCombineModeProperty, value); WritePostscript(); }
    }

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            var bounds1 = Geometry1?.Bounds ?? Rect.Empty;
            var bounds2 = Geometry2?.Bounds ?? Rect.Empty;

            return GeometryCombineMode switch
            {
                GeometryCombineMode.Union => Rect.Union(bounds1, bounds2),
                GeometryCombineMode.Intersect => Rect.Intersect(bounds1, bounds2),
                GeometryCombineMode.Exclude => bounds1,
                GeometryCombineMode.Xor => Rect.Union(bounds1, bounds2),
                _ => Rect.Union(bounds1, bounds2)
            };
        }
    }

    /// <inheritdoc />
    public override bool FillContains(Point point)
    {
        if (Geometry1 == null && Geometry2 == null) return false;
        if (Geometry1 == null) return Geometry2!.FillContains(point);
        if (Geometry2 == null) return Geometry1.FillContains(point);

        bool in1 = Geometry1.FillContains(point);
        bool in2 = Geometry2.FillContains(point);

        return GeometryCombineMode switch
        {
            GeometryCombineMode.Union => in1 || in2,
            GeometryCombineMode.Intersect => in1 && in2,
            GeometryCombineMode.Exclude => in1 && !in2,
            GeometryCombineMode.Xor => in1 ^ in2,
            _ => in1 || in2
        };
    }

    /// <inheritdoc />
    public override PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        if (Geometry1 == null && Geometry2 == null) return new PathGeometry();
        if (Geometry1 == null) return Geometry2!.GetFlattenedPathGeometry(tolerance, toleranceType);
        if (Geometry2 == null) return Geometry1.GetFlattenedPathGeometry(tolerance, toleranceType);

        // Pass a null transform: like every other Geometry.GetFlattenedPathGeometry the result
        // stays in local space, and this geometry's own Transform is applied once by the consumer
        // (DrawGeometry's PushTransform / the software context's WithTransform). Baking Transform
        // in here would double-apply it (once baked, once pushed).
        return Geometry.Combine(Geometry1, Geometry2, GeometryCombineMode, null);
    }

    /// <inheritdoc />
    public override double GetArea(double tolerance, ToleranceType type) =>
        GetFlattenedPathGeometry(tolerance, type).ComputeArea();

    public new CombinedGeometry Clone() => (CombinedGeometry)base.Clone();
    public new CombinedGeometry CloneCurrentValue() => (CombinedGeometry)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new CombinedGeometry();
}

/// <summary>
/// Specifies the method used to combine two geometries.
/// </summary>
public enum GeometryCombineMode
{
    /// <summary>
    /// The two geometries are combined by taking their union.
    /// </summary>
    Union = 0,

    /// <summary>
    /// The two geometries are combined by taking their intersection.
    /// </summary>
    Intersect = 1,

    /// <summary>
    /// The second geometry is excluded from the first.
    /// </summary>
    Xor = 2,

    /// <summary>
    /// The two geometries are combined by taking the area that exists in the first geometry but not the second and the area that exists in the second geometry but not the first.
    /// </summary>
    Exclude = 3
}

/// <summary>
/// Specifies how the interior of a closed path is filled.
/// </summary>
public enum FillRule
{
    /// <summary>
    /// A point is inside if a ray from that point crosses an odd number of boundaries.
    /// </summary>
    EvenOdd,

    /// <summary>
    /// A point is inside if the winding number is non-zero.
    /// </summary>
    Nonzero
}

/// <summary>
/// Represents a complex shape composed of arcs, curves, lines, and rectangles.
/// </summary>
public sealed class PathGeometry : Geometry
{
    public static readonly DependencyProperty FiguresProperty =
        DependencyProperty.Register(nameof(Figures), typeof(PathFigureCollection), typeof(PathGeometry),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FillRuleProperty =
        DependencyProperty.Register(nameof(FillRule), typeof(FillRule), typeof(PathGeometry),
            new PropertyMetadata(FillRule.EvenOdd));

    public PathGeometry()
    {
        Figures = new PathFigureCollection();
    }

    public PathGeometry(IEnumerable<PathFigure> figures)
        : this(figures, FillRule.EvenOdd, null)
    {
    }

    public PathGeometry(IEnumerable<PathFigure> figures, FillRule fillRule, Transform? transform)
        : this()
    {
        ArgumentNullException.ThrowIfNull(figures);
        Figures = new PathFigureCollection(figures);
        FillRule = fillRule;
        Transform = transform;
    }

    /// <summary>
    /// Gets the collection of path figures.
    /// </summary>
    public PathFigureCollection Figures
    {
        get => (PathFigureCollection)GetValue(FiguresProperty)!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            PathFigureCollection? old = (PathFigureCollection?)GetValue(FiguresProperty);
            OnFreezablePropertyChanged(old, value, FiguresProperty);
            SetValue(FiguresProperty, value);
            WritePostscript();
        }
    }

    /// <summary>
    /// Gets or sets the fill rule for this geometry.
    /// </summary>
    public FillRule FillRule
    {
        get => (FillRule)GetValue(FillRuleProperty)!;
        set { SetValue(FillRuleProperty, value); WritePostscript(); }
    }

    public static PathGeometry CreateFromGeometry(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return geometry is PathGeometry path ? path.Clone() : geometry.GetFlattenedPathGeometry();
    }

    public void Clear()
    {
        WritePreamble();
        Figures.Clear();
        WritePostscript();
    }

    /// <inheritdoc />
    public override bool IsEmpty() => Figures.Count == 0;

    /// <inheritdoc />
    public override bool MayHaveCurves()
    {
        foreach (var figure in Figures)
            foreach (var segment in figure.Segments)
                if (segment is BezierSegment or QuadraticBezierSegment
                    or PolyBezierSegment or PolyQuadraticBezierSegment or ArcSegment)
                    return true;
        return false;
    }

    /// <summary>
    /// Appends the figures of <paramref name="geometry"/> to this path. A
    /// <see cref="PathGeometry"/> source contributes its figures directly (curves
    /// preserved); any other geometry is flattened first.
    /// </summary>
    public void AddGeometry(Geometry geometry)
    {
        if (geometry == null) return;
        // Deep-copy so the appended figures are independent of the source (no aliasing of the
        // source's mutable PathFigure instances): a PathGeometry source is cloned; any other
        // geometry is flattened (which already yields a fresh copy).
        var source = geometry is PathGeometry pg ? pg.ClonePathGeometry() : geometry.GetFlattenedPathGeometry();
        if (source == null) return;
        foreach (var figure in source.Figures)
            Figures.Add(figure);
    }

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            if (Figures.Count == 0) return Rect.Empty;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var figure in Figures)
            {
                UpdateBounds(figure.StartPoint, ref minX, ref minY, ref maxX, ref maxY);
                var currentPoint = figure.StartPoint;

                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case LineSegment line:
                            UpdateBounds(line.Point, ref minX, ref minY, ref maxX, ref maxY);
                            currentPoint = line.Point;
                            break;

                        case PolyLineSegment polyLine:
                            foreach (var pt in polyLine.Points)
                                UpdateBounds(pt, ref minX, ref minY, ref maxX, ref maxY);
                            if (polyLine.Points.Count > 0)
                                currentPoint = polyLine.Points[^1];
                            break;

                        case BezierSegment bezier:
                            UpdateCubicBezierBounds(currentPoint, bezier.Point1, bezier.Point2, bezier.Point3,
                                ref minX, ref minY, ref maxX, ref maxY);
                            currentPoint = bezier.Point3;
                            break;

                        case PolyBezierSegment polyBezier:
                        {
                            var pts = polyBezier.Points;
                            for (int i = 0; i + 2 < pts.Count; i += 3)
                            {
                                UpdateCubicBezierBounds(currentPoint, pts[i], pts[i + 1], pts[i + 2],
                                    ref minX, ref minY, ref maxX, ref maxY);
                                currentPoint = pts[i + 2];
                            }
                            break;
                        }

                        case QuadraticBezierSegment quad:
                            UpdateQuadBezierBounds(currentPoint, quad.Point1, quad.Point2,
                                ref minX, ref minY, ref maxX, ref maxY);
                            currentPoint = quad.Point2;
                            break;

                        case PolyQuadraticBezierSegment polyQuad:
                        {
                            var pts = polyQuad.Points;
                            for (int i = 0; i + 1 < pts.Count; i += 2)
                            {
                                UpdateQuadBezierBounds(currentPoint, pts[i], pts[i + 1],
                                    ref minX, ref minY, ref maxX, ref maxY);
                                currentPoint = pts[i + 1];
                            }
                            break;
                        }

                        case ArcSegment arc:
                            UpdateArcBounds(currentPoint, arc, ref minX, ref minY, ref maxX, ref maxY);
                            currentPoint = arc.Point;
                            break;

                        default:
                            foreach (var pt in segment.GetPoints())
                                UpdateBounds(pt, ref minX, ref minY, ref maxX, ref maxY);
                            currentPoint = segment.GetEndPoint(currentPoint);
                            break;
                    }
                }
            }

            if (minX > maxX || minY > maxY) return Rect.Empty;
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    private static void UpdateBounds(Point pt, ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        if (pt.X < minX) minX = pt.X;
        if (pt.Y < minY) minY = pt.Y;
        if (pt.X > maxX) maxX = pt.X;
        if (pt.Y > maxY) maxY = pt.Y;
    }

    /// <summary>
    /// Computes exact bounding box of a cubic bezier by finding parametric extrema.
    /// For P(t) = (1-t)³P0 + 3(1-t)²tP1 + 3(1-t)t²P2 + t³P3,
    /// extrema occur where dP/dt = 0, which is a quadratic in t.
    /// </summary>
    private static void UpdateCubicBezierBounds(Point p0, Point p1, Point p2, Point p3,
        ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        // Endpoints are always part of the curve
        UpdateBounds(p0, ref minX, ref minY, ref maxX, ref maxY);
        UpdateBounds(p3, ref minX, ref minY, ref maxX, ref maxY);

        // Find extrema for X: solve dX/dt = 0
        // dX/dt = 3[at² + bt + c] where:
        //   a = -P0.X + 3P1.X - 3P2.X + P3.X
        //   b = 2(P0.X - 2P1.X + P2.X)
        //   c = P1.X - P0.X
        CubicExtrema(p0.X, p1.X, p2.X, p3.X, ref minX, ref maxX);
        CubicExtrema(p0.Y, p1.Y, p2.Y, p3.Y, ref minY, ref maxY);
    }

    private static void CubicExtrema(double v0, double v1, double v2, double v3, ref double min, ref double max)
    {
        double a = -v0 + 3 * v1 - 3 * v2 + v3;
        double b = 2 * (v0 - 2 * v1 + v2);
        double c = v1 - v0;

        // Solve at² + bt + c = 0
        if (Math.Abs(a) < 1e-12)
        {
            // Linear: bt + c = 0
            if (Math.Abs(b) > 1e-12)
            {
                double t = -c / b;
                if (t > 0 && t < 1)
                {
                    double v = CubicAt(v0, v1, v2, v3, t);
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }
            return;
        }

        double disc = b * b - 4 * a * c;
        if (disc < 0) return;

        double sqrtDisc = Math.Sqrt(disc);
        double t1 = (-b + sqrtDisc) / (2 * a);
        double t2 = (-b - sqrtDisc) / (2 * a);

        if (t1 > 0 && t1 < 1)
        {
            double v = CubicAt(v0, v1, v2, v3, t1);
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (t2 > 0 && t2 < 1)
        {
            double v = CubicAt(v0, v1, v2, v3, t2);
            if (v < min) min = v;
            if (v > max) max = v;
        }
    }

    private static double CubicAt(double v0, double v1, double v2, double v3, double t)
    {
        double u = 1 - t;
        return u * u * u * v0 + 3 * u * u * t * v1 + 3 * u * t * t * v2 + t * t * t * v3;
    }

    /// <summary>
    /// Computes exact bounding box of a quadratic bezier.
    /// Extremum at t = (P0 - P1) / (P0 - 2P1 + P2).
    /// </summary>
    private static void UpdateQuadBezierBounds(Point p0, Point p1, Point p2,
        ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        UpdateBounds(p0, ref minX, ref minY, ref maxX, ref maxY);
        UpdateBounds(p2, ref minX, ref minY, ref maxX, ref maxY);

        QuadExtrema(p0.X, p1.X, p2.X, ref minX, ref maxX);
        QuadExtrema(p0.Y, p1.Y, p2.Y, ref minY, ref maxY);
    }

    private static void QuadExtrema(double v0, double v1, double v2, ref double min, ref double max)
    {
        double denom = v0 - 2 * v1 + v2;
        if (Math.Abs(denom) < 1e-12) return;

        double t = (v0 - v1) / denom;
        if (t > 0 && t < 1)
        {
            double u = 1 - t;
            double v = u * u * v0 + 2 * u * t * v1 + t * t * v2;
            if (v < min) min = v;
            if (v > max) max = v;
        }
    }

    /// <summary>
    /// Computes the bounding box of an elliptical arc segment using
    /// SVG endpoint-to-center parameterization, then checking axis-aligned
    /// extrema of the rotated ellipse within the swept angle range.
    /// </summary>
    private static void UpdateArcBounds(Point start, ArcSegment arc,
        ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        UpdateBounds(start, ref minX, ref minY, ref maxX, ref maxY);
        UpdateBounds(arc.Point, ref minX, ref minY, ref maxX, ref maxY);

        var rx = Math.Abs(arc.Size.Width);
        var ry = Math.Abs(arc.Size.Height);
        if (rx < 1e-10 || ry < 1e-10) return;

        var end = arc.Point;
        var dx = (start.X - end.X) / 2.0;
        var dy = (start.Y - end.Y) / 2.0;

        var rot = arc.RotationAngle * Math.PI / 180.0;
        var cosR = Math.Cos(rot);
        var sinR = Math.Sin(rot);

        var x1p = cosR * dx + sinR * dy;
        var y1p = -sinR * dx + cosR * dy;

        // Correct radii
        var x1pSq = x1p * x1p;
        var y1pSq = y1p * y1p;
        var rxSq = rx * rx;
        var rySq = ry * ry;
        var lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1)
        {
            var sqrtL = Math.Sqrt(lambda);
            rx *= sqrtL;
            ry *= sqrtL;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        // Center
        var sign = (arc.IsLargeArc != (arc.SweepDirection == SweepDirection.Clockwise)) ? 1.0 : -1.0;
        var sq = Math.Max(0, (rxSq * rySq - rxSq * y1pSq - rySq * x1pSq) / (rxSq * y1pSq + rySq * x1pSq));
        var coef = sign * Math.Sqrt(sq);

        var cxp = coef * rx * y1p / ry;
        var cyp = -coef * ry * x1p / rx;

        var cx = cosR * cxp - sinR * cyp + (start.X + end.X) / 2.0;
        var cy = sinR * cxp + cosR * cyp + (start.Y + end.Y) / 2.0;

        // Angle range
        var startAngle = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var endAngle = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
        var deltaAngle = endAngle - startAngle;

        if (arc.SweepDirection == SweepDirection.Clockwise && deltaAngle < 0)
            deltaAngle += 2 * Math.PI;
        else if (arc.SweepDirection == SweepDirection.Counterclockwise && deltaAngle > 0)
            deltaAngle -= 2 * Math.PI;

        // For a rotated ellipse x(θ) = cx + rx·cos(θ)·cos(rot) - ry·sin(θ)·sin(rot)
        //                        y(θ) = cy + rx·cos(θ)·sin(rot) + ry·sin(θ)·cos(rot)
        // Extrema of x: dx/dθ = 0 → tan(θ) = -(ry/rx)·tan(rot) → θ = atan2(-ry·sin(rot), rx·cos(rot))
        // Extrema of y: dy/dθ = 0 → tan(θ) =  (ry/rx)·cot(rot) → θ = atan2( ry·cos(rot), rx·sin(rot))
        double txBase = Math.Atan2(-ry * sinR, rx * cosR);
        double tyBase = Math.Atan2(ry * cosR, rx * sinR);

        // Check the 4 candidate angles (2 for x extrema, 2 for y extrema)
        double[] candidates = { txBase, txBase + Math.PI, tyBase, tyBase + Math.PI };

        foreach (var angle in candidates)
        {
            if (IsAngleInArc(angle, startAngle, deltaAngle))
            {
                var px = cx + rx * Math.Cos(angle) * cosR - ry * Math.Sin(angle) * sinR;
                var py = cy + rx * Math.Cos(angle) * sinR + ry * Math.Sin(angle) * cosR;
                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;
            }
        }
    }

    /// <summary>
    /// Checks whether a given angle falls within an arc defined by startAngle and deltaAngle.
    /// </summary>
    private static bool IsAngleInArc(double angle, double startAngle, double deltaAngle)
    {
        // Normalize angle relative to start
        double diff = angle - startAngle;
        // Bring into (-2π, 2π) range then check direction
        diff -= Math.Floor(diff / (2 * Math.PI)) * 2 * Math.PI;

        if (deltaAngle > 0)
        {
            // CW sweep: angle is in arc if 0 <= diff <= deltaAngle
            return diff >= -1e-10 && diff <= deltaAngle + 1e-10;
        }
        else
        {
            // CCW sweep: angle is in arc if deltaAngle <= diff <= 0
            // diff is in [0, 2π), so shift by -2π
            if (diff > Math.PI) diff -= 2 * Math.PI;
            return diff <= 1e-10 && diff >= deltaAngle - 1e-10;
        }
    }

    /// <summary>
    /// Gets the point and tangent vector at the specified fraction of the path length.
    /// </summary>
    /// <param name="progress">A value between 0.0 and 1.0 indicating the fraction of the path.</param>
    /// <param name="point">The point at the specified fraction.</param>
    /// <param name="tangent">The tangent vector at the specified fraction (normalized).</param>
    public void GetPointAtFractionLength(double progress, out Point point, out Point tangent)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);

        if (Figures.Count == 0)
        {
            point = new Point(0, 0);
            tangent = new Point(1, 0);
            return;
        }

        // Calculate total path length
        var totalLength = GetTotalLength();
        if (totalLength <= 0)
        {
            point = Figures[0].StartPoint;
            tangent = new Point(1, 0);
            return;
        }

        var targetLength = progress * totalLength;
        var currentLength = 0.0;

        foreach (var figure in Figures)
        {
            var currentPoint = figure.StartPoint;

            foreach (var segment in figure.Segments)
            {
                var segmentLength = GetSegmentLength(currentPoint, segment);

                if (currentLength + segmentLength >= targetLength)
                {
                    // The target point is within this segment
                    var segmentProgress = segmentLength > 0
                        ? (targetLength - currentLength) / segmentLength
                        : 0;

                    GetPointOnSegment(currentPoint, segment, segmentProgress, out point, out tangent);
                    return;
                }

                currentLength += segmentLength;
                currentPoint = segment.GetEndPoint(currentPoint);
            }

            // Handle closed figures
            if (figure.IsClosed)
            {
                var closeLength = Distance(currentPoint, figure.StartPoint);
                if (currentLength + closeLength >= targetLength)
                {
                    var closeProgress = closeLength > 0
                        ? (targetLength - currentLength) / closeLength
                        : 0;
                    point = Lerp(currentPoint, figure.StartPoint, closeProgress);
                    var diff = new Point(figure.StartPoint.X - currentPoint.X, figure.StartPoint.Y - currentPoint.Y);
                    var len = Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
                    tangent = len > 0 ? new Point(diff.X / len, diff.Y / len) : new Point(1, 0);
                    return;
                }
                currentLength += closeLength;
            }
        }

        // Return the end point
        var lastFigure = Figures[^1];
        if (lastFigure.Segments.Count > 0)
        {
            var lastPoint = lastFigure.StartPoint;
            foreach (var seg in lastFigure.Segments)
            {
                lastPoint = seg.GetEndPoint(lastPoint);
            }
            point = lastPoint;
        }
        else
        {
            point = lastFigure.StartPoint;
        }
        tangent = new Point(1, 0);
    }

    /// <summary>
    /// Gets the total length of the path.
    /// </summary>
    public double GetTotalLength()
    {
        var totalLength = 0.0;

        foreach (var figure in Figures)
        {
            var currentPoint = figure.StartPoint;

            foreach (var segment in figure.Segments)
            {
                totalLength += GetSegmentLength(currentPoint, segment);
                currentPoint = segment.GetEndPoint(currentPoint);
            }

            if (figure.IsClosed)
            {
                totalLength += Distance(currentPoint, figure.StartPoint);
            }
        }

        return totalLength;
    }

    private static double GetSegmentLength(Point startPoint, PathSegment segment)
    {
        return segment switch
        {
            LineSegment line => Distance(startPoint, line.Point),
            PolyLineSegment poly => GetPolyLineLength(startPoint, poly.Points),
            BezierSegment bezier => GetBezierLength(startPoint, bezier.Point1, bezier.Point2, bezier.Point3),
            QuadraticBezierSegment quad => GetQuadraticBezierLength(startPoint, quad.Point1, quad.Point2),
            PolyBezierSegment polyBezier => GetPolyBezierLength(startPoint, polyBezier.Points),
            PolyQuadraticBezierSegment polyQuad => GetPolyQuadraticBezierLength(startPoint, polyQuad.Points),
            ArcSegment arc => GetArcLength(startPoint, arc),
            _ => 0
        };
    }

    private static void GetPointOnSegment(Point startPoint, PathSegment segment, double progress, out Point point, out Point tangent)
    {
        switch (segment)
        {
            case LineSegment line:
                point = Lerp(startPoint, line.Point, progress);
                var lineDiff = new Point(line.Point.X - startPoint.X, line.Point.Y - startPoint.Y);
                var lineLen = Math.Sqrt(lineDiff.X * lineDiff.X + lineDiff.Y * lineDiff.Y);
                tangent = lineLen > 0 ? new Point(lineDiff.X / lineLen, lineDiff.Y / lineLen) : new Point(1, 0);
                break;

            case BezierSegment bezier:
                GetCubicBezierPointAndTangent(startPoint, bezier.Point1, bezier.Point2, bezier.Point3, progress, out point, out tangent);
                break;

            case QuadraticBezierSegment quad:
                GetQuadraticBezierPointAndTangent(startPoint, quad.Point1, quad.Point2, progress, out point, out tangent);
                break;

            case ArcSegment arc:
                GetArcPointAndTangent(startPoint, arc, progress, out point, out tangent);
                break;

            default:
                point = startPoint;
                tangent = new Point(1, 0);
                break;
        }
    }

    private static Point Lerp(Point a, Point b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double Distance(Point a, Point b) =>
        Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    private static double GetPolyLineLength(Point start, IList<Point> points)
    {
        var length = 0.0;
        var current = start;
        foreach (var pt in points)
        {
            length += Distance(current, pt);
            current = pt;
        }
        return length;
    }

    private static double GetBezierLength(Point p0, Point p1, Point p2, Point p3, int samples = 20)
    {
        var length = 0.0;
        var prev = p0;
        for (var i = 1; i <= samples; i++)
        {
            var t = i / (double)samples;
            var pt = GetCubicBezierPoint(p0, p1, p2, p3, t);
            length += Distance(prev, pt);
            prev = pt;
        }
        return length;
    }

    private static double GetQuadraticBezierLength(Point p0, Point p1, Point p2, int samples = 20)
    {
        var length = 0.0;
        var prev = p0;
        for (var i = 1; i <= samples; i++)
        {
            var t = i / (double)samples;
            var pt = GetQuadraticBezierPoint(p0, p1, p2, t);
            length += Distance(prev, pt);
            prev = pt;
        }
        return length;
    }

    private static double GetPolyBezierLength(Point start, IList<Point> points)
    {
        var length = 0.0;
        var current = start;
        for (var i = 0; i + 2 < points.Count; i += 3)
        {
            length += GetBezierLength(current, points[i], points[i + 1], points[i + 2]);
            current = points[i + 2];
        }
        return length;
    }

    private static double GetPolyQuadraticBezierLength(Point start, IList<Point> points)
    {
        var length = 0.0;
        var current = start;
        for (var i = 0; i + 1 < points.Count; i += 2)
        {
            length += GetQuadraticBezierLength(current, points[i], points[i + 1]);
            current = points[i + 1];
        }
        return length;
    }

    private static double GetArcLength(Point start, ArcSegment arc)
    {
        // Approximate arc length using line segments
        var length = 0.0;
        var prev = start;
        const int samples = 20;
        for (var i = 1; i <= samples; i++)
        {
            GetArcPointAndTangent(start, arc, i / (double)samples, out var pt, out _);
            length += Distance(prev, pt);
            prev = pt;
        }
        return length;
    }

    private static Point GetCubicBezierPoint(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var u = 1 - t;
        var tt = t * t;
        var uu = u * u;
        var uuu = uu * u;
        var ttt = tt * t;

        return new Point(
            uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X,
            uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y
        );
    }

    private static Point GetQuadraticBezierPoint(Point p0, Point p1, Point p2, double t)
    {
        var u = 1 - t;
        return new Point(
            u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X,
            u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y
        );
    }

    private static void GetCubicBezierPointAndTangent(Point p0, Point p1, Point p2, Point p3, double t, out Point point, out Point tangent)
    {
        point = GetCubicBezierPoint(p0, p1, p2, p3, t);

        // Derivative of cubic Bezier
        var u = 1 - t;
        var dx = 3 * u * u * (p1.X - p0.X) + 6 * u * t * (p2.X - p1.X) + 3 * t * t * (p3.X - p2.X);
        var dy = 3 * u * u * (p1.Y - p0.Y) + 6 * u * t * (p2.Y - p1.Y) + 3 * t * t * (p3.Y - p2.Y);

        var len = Math.Sqrt(dx * dx + dy * dy);
        tangent = len > 0 ? new Point(dx / len, dy / len) : new Point(1, 0);
    }

    private static void GetQuadraticBezierPointAndTangent(Point p0, Point p1, Point p2, double t, out Point point, out Point tangent)
    {
        point = GetQuadraticBezierPoint(p0, p1, p2, t);

        // Derivative of quadratic Bezier
        var dx = 2 * (1 - t) * (p1.X - p0.X) + 2 * t * (p2.X - p1.X);
        var dy = 2 * (1 - t) * (p1.Y - p0.Y) + 2 * t * (p2.Y - p1.Y);

        var len = Math.Sqrt(dx * dx + dy * dy);
        tangent = len > 0 ? new Point(dx / len, dy / len) : new Point(1, 0);
    }

    private static void GetArcPointAndTangent(Point start, ArcSegment arc, double progress, out Point point, out Point tangent)
    {
        var end = arc.Point;
        var rx = arc.Size.Width;
        var ry = arc.Size.Height;

        // Degenerate: zero radii or coincident endpoints → linear fallback
        if (rx == 0 || ry == 0 || (start.X == end.X && start.Y == end.Y))
        {
            point = Lerp(start, end, progress);
            var diff = new Point(end.X - start.X, end.Y - start.Y);
            var dLen = Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
            tangent = dLen > 0 ? new Point(diff.X / dLen, diff.Y / dLen) : new Point(1, 0);
            return;
        }

        // SVG endpoint → center parameterization
        var dx = (start.X - end.X) / 2;
        var dy = (start.Y - end.Y) / 2;
        var rotAngle = arc.RotationAngle * Math.PI / 180;
        var cosA = Math.Cos(rotAngle);
        var sinA = Math.Sin(rotAngle);
        var x1p = cosA * dx + sinA * dy;
        var y1p = -sinA * dx + cosA * dy;

        // Ensure radii are large enough
        var x1pSq = x1p * x1p;
        var y1pSq = y1p * y1p;
        var rxSq = rx * rx;
        var rySq = ry * ry;
        var lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1) { var sl = Math.Sqrt(lambda); rx *= sl; ry *= sl; rxSq = rx * rx; rySq = ry * ry; }

        var sign = (arc.IsLargeArc != (arc.SweepDirection == SweepDirection.Clockwise)) ? 1.0 : -1.0;
        var sq = Math.Max(0, (rxSq * rySq - rxSq * y1pSq - rySq * x1pSq) / (rxSq * y1pSq + rySq * x1pSq));
        var coef = sign * Math.Sqrt(sq);
        var cxp = coef * rx * y1p / ry;
        var cyp = -coef * ry * x1p / rx;
        var cx = cosA * cxp - sinA * cyp + (start.X + end.X) / 2;
        var cy = sinA * cxp + cosA * cyp + (start.Y + end.Y) / 2;

        var startAngle = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var endAngle = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
        var deltaAngle = endAngle - startAngle;
        if (arc.SweepDirection == SweepDirection.Clockwise && deltaAngle < 0) deltaAngle += 2 * Math.PI;
        else if (arc.SweepDirection == SweepDirection.Counterclockwise && deltaAngle > 0) deltaAngle -= 2 * Math.PI;

        var angle = startAngle + deltaAngle * progress;
        var px = rx * Math.Cos(angle);
        var py = ry * Math.Sin(angle);
        point = new Point(cosA * px - sinA * py + cx, sinA * px + cosA * py + cy);

        // Tangent: derivative of the parametric arc
        var tdx = -rx * Math.Sin(angle) * deltaAngle;
        var tdy = ry * Math.Cos(angle) * deltaAngle;
        var tx = cosA * tdx - sinA * tdy;
        var ty = sinA * tdx + cosA * tdy;
        var tLen = Math.Sqrt(tx * tx + ty * ty);
        tangent = tLen > 0 ? new Point(tx / tLen, ty / tLen) : new Point(1, 0);
    }

    /// <inheritdoc />
    public override bool FillContains(Point point)
    {
        // Ray-casting algorithm: cast a horizontal ray from point to the right.
        // Count crossings and track signed winding number for Nonzero fill rule.
        var flat = GetFlattenedPathGeometry();
        int crossings = 0;
        int winding = 0;

        foreach (var figure in flat.Figures)
        {
            if (!figure.IsFilled) continue;

            var pts = new List<Point> { figure.StartPoint };
            foreach (var seg in figure.Segments)
            {
                if (seg is LineSegment ls) pts.Add(ls.Point);
                else if (seg is PolyLineSegment pls) pts.AddRange(pls.Points);
            }

            // Close the figure
            if (figure.IsClosed && pts.Count > 1)
            {
                var first = pts[0];
                var last = pts[^1];
                if (Math.Abs(first.X - last.X) > 1e-10 || Math.Abs(first.Y - last.Y) > 1e-10)
                    pts.Add(first);
            }

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];

                // Check if the ray from point going right crosses edge (a, b)
                if (a.Y <= point.Y && b.Y > point.Y)
                {
                    // Upward crossing (bottom to top in screen coords)
                    var xIntersect = a.X + (point.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                    if (point.X < xIntersect)
                    {
                        crossings++;
                        winding++;
                    }
                }
                else if (b.Y <= point.Y && a.Y > point.Y)
                {
                    // Downward crossing (top to bottom in screen coords)
                    var xIntersect = a.X + (point.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                    if (point.X < xIntersect)
                    {
                        crossings++;
                        winding--;
                    }
                }
            }
        }

        if (FillRule == FillRule.EvenOdd)
            return (crossings % 2) != 0;
        else
            return winding != 0;
    }

    /// <summary>
    /// Computes the signed area of this flattened path geometry using the shoelace formula.
    /// </summary>
    internal double ComputeArea()
    {
        double area = 0;
        var flat = (Figures.Count > 0 && !HasCurves()) ? this : GetFlattenedPathGeometry();

        foreach (var figure in flat.Figures)
        {
            if (!figure.IsFilled) continue;
            var pts = new List<Point> { figure.StartPoint };
            foreach (var seg in figure.Segments)
            {
                if (seg is LineSegment ls) pts.Add(ls.Point);
                else if (seg is PolyLineSegment pls) pts.AddRange(pls.Points);
            }
            if (pts.Count < 3) continue;

            // Shoelace formula
            for (int i = 0; i < pts.Count; i++)
            {
                var j = (i + 1) % pts.Count;
                area += pts[i].X * pts[j].Y;
                area -= pts[j].X * pts[i].Y;
            }
        }

        return Math.Abs(area) / 2.0;
    }

    private bool HasCurves()
    {
        foreach (var figure in Figures)
        {
            foreach (var seg in figure.Segments)
            {
                if (seg is not LineSegment and not PolyLineSegment) return true;
            }
        }
        return false;
    }

    /// <inheritdoc />
    public override PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        var result = new PathGeometry { FillRule = FillRule };

        foreach (var figure in Figures)
        {
            var newFigure = new PathFigure
            {
                StartPoint = figure.StartPoint,
                IsClosed = figure.IsClosed,
                IsFilled = figure.IsFilled,
            };

            var currentPoint = figure.StartPoint;

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        newFigure.Segments.Add(new LineSegment(line.Point, line.IsStroked));
                        currentPoint = line.Point;
                        break;

                    case PolyLineSegment poly:
                        newFigure.Segments.Add(new PolyLineSegment(poly.Points, poly.IsStroked));
                        if (poly.Points.Count > 0) currentPoint = poly.Points[^1];
                        break;

                    case BezierSegment bezier:
                    {
                        var pts = new List<Point>();
                        FlattenCubicBezier(pts, currentPoint, bezier.Point1, bezier.Point2, bezier.Point3, tolerance, 0);
                        foreach (var pt in pts)
                            newFigure.Segments.Add(new LineSegment(pt, bezier.IsStroked));
                        currentPoint = bezier.Point3;
                        break;
                    }

                    case PolyBezierSegment polyBez:
                    {
                        var bpts = polyBez.Points;
                        for (int i = 0; i + 2 < bpts.Count; i += 3)
                        {
                            var pts = new List<Point>();
                            FlattenCubicBezier(pts, currentPoint, bpts[i], bpts[i + 1], bpts[i + 2], tolerance, 0);
                            foreach (var pt in pts)
                                newFigure.Segments.Add(new LineSegment(pt, polyBez.IsStroked));
                            currentPoint = bpts[i + 2];
                        }
                        break;
                    }

                    case QuadraticBezierSegment quad:
                    {
                        // Promote to cubic
                        var cp1 = new Point(
                            currentPoint.X + 2.0 / 3.0 * (quad.Point1.X - currentPoint.X),
                            currentPoint.Y + 2.0 / 3.0 * (quad.Point1.Y - currentPoint.Y));
                        var cp2 = new Point(
                            quad.Point2.X + 2.0 / 3.0 * (quad.Point1.X - quad.Point2.X),
                            quad.Point2.Y + 2.0 / 3.0 * (quad.Point1.Y - quad.Point2.Y));
                        var pts = new List<Point>();
                        FlattenCubicBezier(pts, currentPoint, cp1, cp2, quad.Point2, tolerance, 0);
                        foreach (var pt in pts)
                            newFigure.Segments.Add(new LineSegment(pt, quad.IsStroked));
                        currentPoint = quad.Point2;
                        break;
                    }

                    case PolyQuadraticBezierSegment polyQuad:
                    {
                        var qpts = polyQuad.Points;
                        for (int i = 0; i + 1 < qpts.Count; i += 2)
                        {
                            var ctrl = qpts[i];
                            var end = qpts[i + 1];
                            var cp1 = new Point(
                                currentPoint.X + 2.0 / 3.0 * (ctrl.X - currentPoint.X),
                                currentPoint.Y + 2.0 / 3.0 * (ctrl.Y - currentPoint.Y));
                            var cp2 = new Point(
                                end.X + 2.0 / 3.0 * (ctrl.X - end.X),
                                end.Y + 2.0 / 3.0 * (ctrl.Y - end.Y));
                            var pts = new List<Point>();
                            FlattenCubicBezier(pts, currentPoint, cp1, cp2, end, tolerance, 0);
                            foreach (var pt in pts)
                                newFigure.Segments.Add(new LineSegment(pt, polyQuad.IsStroked));
                            currentPoint = end;
                        }
                        break;
                    }

                    case ArcSegment arc:
                    {
                        var pts = FlattenArc(currentPoint, arc, tolerance);
                        foreach (var pt in pts)
                            newFigure.Segments.Add(new LineSegment(pt, arc.IsStroked));
                        currentPoint = arc.Point;
                        break;
                    }
                }
            }

            result.Figures.Add(newFigure);
        }

        return result;
    }

    private static void FlattenCubicBezier(List<Point> points, Point p0, Point p1, Point p2, Point p3,
        double tolerance, int depth)
    {
        if (depth > 12)
        {
            points.Add(p3);
            return;
        }

        // Flatness test
        double dx = p3.X - p0.X, dy = p3.Y - p0.Y;
        double len2 = dx * dx + dy * dy;
        double d1, d2;
        if (len2 < 1e-10)
        {
            d1 = Math.Sqrt((p1.X - p0.X) * (p1.X - p0.X) + (p1.Y - p0.Y) * (p1.Y - p0.Y));
            d2 = Math.Sqrt((p2.X - p0.X) * (p2.X - p0.X) + (p2.Y - p0.Y) * (p2.Y - p0.Y));
        }
        else
        {
            double invLen = 1.0 / Math.Sqrt(len2);
            double nx = -dy * invLen, ny = dx * invLen;
            d1 = Math.Abs(nx * (p1.X - p0.X) + ny * (p1.Y - p0.Y));
            d2 = Math.Abs(nx * (p2.X - p0.X) + ny * (p2.Y - p0.Y));
        }

        if (d1 + d2 <= tolerance)
        {
            points.Add(p3);
            return;
        }

        // De Casteljau subdivision
        var m01 = Lerp(p0, p1, 0.5);
        var m12 = Lerp(p1, p2, 0.5);
        var m23 = Lerp(p2, p3, 0.5);
        var m012 = Lerp(m01, m12, 0.5);
        var m123 = Lerp(m12, m23, 0.5);
        var mid = Lerp(m012, m123, 0.5);

        FlattenCubicBezier(points, p0, m01, m012, mid, tolerance, depth + 1);
        FlattenCubicBezier(points, mid, m123, m23, p3, tolerance, depth + 1);
    }

    private static List<Point> FlattenArc(Point start, ArcSegment arc, double tolerance)
    {
        var points = new List<Point>();
        var end = arc.Point;
        var rx = arc.Size.Width;
        var ry = arc.Size.Height;

        if (rx == 0 || ry == 0 || (start.X == end.X && start.Y == end.Y))
        {
            points.Add(end);
            return points;
        }

        var dx = (start.X - end.X) / 2;
        var dy = (start.Y - end.Y) / 2;
        var rot = arc.RotationAngle * Math.PI / 180;
        var cosA = Math.Cos(rot);
        var sinA = Math.Sin(rot);
        var x1p = cosA * dx + sinA * dy;
        var y1p = -sinA * dx + cosA * dy;

        var x1pSq = x1p * x1p;
        var y1pSq = y1p * y1p;
        var rxSq = rx * rx;
        var rySq = ry * ry;
        var lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1)
        {
            var sl = Math.Sqrt(lambda);
            rx *= sl; ry *= sl;
            rxSq = rx * rx; rySq = ry * ry;
        }

        var sign = (arc.IsLargeArc != (arc.SweepDirection == SweepDirection.Clockwise)) ? 1.0 : -1.0;
        var sq = Math.Max(0, (rxSq * rySq - rxSq * y1pSq - rySq * x1pSq) / (rxSq * y1pSq + rySq * x1pSq));
        var coef = sign * Math.Sqrt(sq);
        var cxp = coef * rx * y1p / ry;
        var cyp = -coef * ry * x1p / rx;
        var cx = cosA * cxp - sinA * cyp + (start.X + end.X) / 2;
        var cy = sinA * cxp + cosA * cyp + (start.Y + end.Y) / 2;

        var startAngle = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var endAngle = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
        var deltaAngle = endAngle - startAngle;
        if (arc.SweepDirection == SweepDirection.Clockwise && deltaAngle < 0) deltaAngle += 2 * Math.PI;
        else if (arc.SweepDirection == SweepDirection.Counterclockwise && deltaAngle > 0) deltaAngle -= 2 * Math.PI;

        var circumference = Math.Abs(deltaAngle) * Math.Max(rx, ry);
        var segments = Math.Clamp((int)(circumference / tolerance), 4, 256);
        for (int i = 1; i <= segments; i++)
        {
            var t = i / (double)segments;
            var angle = startAngle + deltaAngle * t;
            var px = rx * Math.Cos(angle);
            var py = ry * Math.Sin(angle);
            points.Add(new Point(cosA * px - sinA * py + cx, sinA * px + cosA * py + cy));
        }

        return points;
    }

    public new PathGeometry Clone() => (PathGeometry)base.Clone();
    public new PathGeometry CloneCurrentValue() => (PathGeometry)base.CloneCurrentValue();

    /// <summary>Creates a deep copy of this path geometry.</summary>
    public PathGeometry ClonePathGeometry() => Clone();

    protected override Freezable CreateInstanceCore() => new PathGeometry();

    protected override void OnChanged()
    {
        base.OnChanged();
    }

    /// <inheritdoc />
    public override PathGeometry GetWidenedPathGeometry(Pen pen, double tolerance, ToleranceType toleranceType)
    {
        if (pen == null) throw new ArgumentNullException(nameof(pen));
        if (pen.Thickness <= 0) return new PathGeometry();

        var flat = GetFlattenedPathGeometry(tolerance, toleranceType);
        var result = new PathGeometry { FillRule = FillRule.Nonzero };
        var halfWidth = pen.Thickness / 2.0;

        foreach (var figure in flat.Figures)
        {
            // Extract the polyline points from the flattened figure
            var points = new List<Point> { figure.StartPoint };
            foreach (var seg in figure.Segments)
            {
                if (seg is LineSegment ls) points.Add(ls.Point);
                else if (seg is PolyLineSegment pls) points.AddRange(pls.Points);
            }

            if (points.Count < 2) continue;

            // Close the figure if needed by appending start point
            bool isClosed = figure.IsClosed;
            if (isClosed)
            {
                var first = points[0];
                var last = points[^1];
                if (Math.Abs(first.X - last.X) > 1e-10 || Math.Abs(first.Y - last.Y) > 1e-10)
                    points.Add(first);
            }

            // Remove consecutive duplicate points
            var cleaned = new List<Point> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                if (Math.Abs(points[i].X - cleaned[^1].X) > 1e-10 ||
                    Math.Abs(points[i].Y - cleaned[^1].Y) > 1e-10)
                {
                    cleaned.Add(points[i]);
                }
            }
            points = cleaned;

            if (points.Count < 2) continue;

            // Compute normals for each segment
            int segCount = points.Count - 1;
            var normals = new (double nx, double ny)[segCount];
            for (int i = 0; i < segCount; i++)
            {
                var dx = points[i + 1].X - points[i].X;
                var dy = points[i + 1].Y - points[i].Y;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-10) { normals[i] = (0, 0); continue; }
                normals[i] = (-dy / len, dx / len);
            }

            // Compute left and right offset polylines
            var leftSide = new List<Point>();
            var rightSide = new List<Point>();

            for (int i = 0; i < segCount; i++)
            {
                var (nx, ny) = normals[i];
                if (nx == 0 && ny == 0) continue;

                var p0 = points[i];
                var p1 = points[i + 1];

                var l0 = new Point(p0.X + nx * halfWidth, p0.Y + ny * halfWidth);
                var l1 = new Point(p1.X + nx * halfWidth, p1.Y + ny * halfWidth);
                var r0 = new Point(p0.X - nx * halfWidth, p0.Y - ny * halfWidth);
                var r1 = new Point(p1.X - nx * halfWidth, p1.Y - ny * halfWidth);

                if (leftSide.Count == 0)
                {
                    leftSide.Add(l0);
                    rightSide.Add(r0);
                }
                else
                {
                    // Handle join between previous segment and this one
                    WidenAddJoin(leftSide, l0, points[i], halfWidth, pen.LineJoin, pen.MiterLimit);
                    WidenAddJoin(rightSide, r0, points[i], halfWidth, pen.LineJoin, pen.MiterLimit);
                }

                leftSide.Add(l1);
                rightSide.Add(r1);
            }

            if (leftSide.Count == 0 || rightSide.Count == 0) continue;

            if (isClosed)
            {
                // For closed paths: handle the join at the closure point
                if (leftSide.Count > 1 && rightSide.Count > 1)
                {
                    WidenAddJoin(leftSide, leftSide[0], points[0], halfWidth, pen.LineJoin, pen.MiterLimit);
                    WidenAddJoin(rightSide, rightSide[0], points[0], halfWidth, pen.LineJoin, pen.MiterLimit);
                }

                // Left side figure (outer contour)
                var leftFigure = new PathFigure
                {
                    StartPoint = leftSide[0],
                    IsClosed = true,
                    IsFilled = true,
                };
                for (int i = 1; i < leftSide.Count; i++)
                    leftFigure.Segments.Add(new LineSegment(leftSide[i]));
                result.Figures.Add(leftFigure);

                // Right side figure (inner contour) — reversed winding so Nonzero fill
                // rule creates a hole, producing a proper stroke outline.
                var rightFigure = new PathFigure
                {
                    StartPoint = rightSide[^1],
                    IsClosed = true,
                    IsFilled = true,
                };
                for (int i = rightSide.Count - 2; i >= 0; i--)
                    rightFigure.Segments.Add(new LineSegment(rightSide[i]));
                result.Figures.Add(rightFigure);
            }
            else
            {
                // Open path: build a single closed contour:
                // left side forward + end cap + right side reversed + start cap
                var contour = new PathFigure
                {
                    StartPoint = leftSide[0],
                    IsClosed = true,
                    IsFilled = true,
                };

                // Left side forward
                for (int i = 1; i < leftSide.Count; i++)
                    contour.Segments.Add(new LineSegment(leftSide[i]));

                // End cap
                var endLeft = leftSide[^1];
                var endRight = rightSide[^1];
                var endPoint = points[^1];
                WidenAddCap(contour, endLeft, endRight, endPoint, halfWidth, pen.EndLineCap, false);

                // Right side reversed
                for (int i = rightSide.Count - 1; i >= 0; i--)
                    contour.Segments.Add(new LineSegment(rightSide[i]));

                // Start cap
                var startRight = rightSide[0];
                var startLeft = leftSide[0];
                var startPoint = points[0];
                WidenAddCap(contour, startRight, startLeft, startPoint, halfWidth, pen.StartLineCap, true);

                result.Figures.Add(contour);
            }
        }

        return result;
    }

    /// <summary>
    /// Adds a line join between the previous offset point and the new offset point.
    /// </summary>
    private static void WidenAddJoin(List<Point> side, Point newPoint, Point vertex,
        double halfWidth, PenLineJoin joinType, double miterLimit)
    {
        if (side.Count == 0)
        {
            side.Add(newPoint);
            return;
        }

        var prevPoint = side[^1];
        var dx = newPoint.X - prevPoint.X;
        var dy = newPoint.Y - prevPoint.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < 1e-10)
            return;

        switch (joinType)
        {
            case PenLineJoin.Bevel:
                // Connect with a straight line (bevel)
                side.Add(newPoint);
                break;

            case PenLineJoin.Round:
            {
                // Generate arc points from prevPoint to newPoint around the vertex
                var angle1 = Math.Atan2(prevPoint.Y - vertex.Y, prevPoint.X - vertex.X);
                var angle2 = Math.Atan2(newPoint.Y - vertex.Y, newPoint.X - vertex.X);
                var deltaAngle = angle2 - angle1;

                // Normalize to [-PI, PI]
                while (deltaAngle > Math.PI) deltaAngle -= 2 * Math.PI;
                while (deltaAngle < -Math.PI) deltaAngle += 2 * Math.PI;

                // Only add arc for the outside of the turn
                if (Math.Abs(deltaAngle) > 1e-6 && Math.Abs(deltaAngle) < Math.PI * 1.999)
                {
                    var steps = Math.Max(2, (int)(Math.Abs(deltaAngle) / (Math.PI / 8)));
                    for (int j = 1; j <= steps; j++)
                    {
                        var t = j / (double)steps;
                        var angle = angle1 + deltaAngle * t;
                        side.Add(new Point(
                            vertex.X + halfWidth * Math.Cos(angle),
                            vertex.Y + halfWidth * Math.Sin(angle)));
                    }
                }
                else
                {
                    side.Add(newPoint);
                }
                break;
            }

            case PenLineJoin.Miter:
            default:
            {
                // Compute miter point by intersecting the two offset edges
                if (side.Count >= 2)
                {
                    var pp = side[^2];
                    var d1x = prevPoint.X - pp.X;
                    var d1y = prevPoint.Y - pp.Y;
                    var d2x = newPoint.X - prevPoint.X;
                    var d2y = newPoint.Y - prevPoint.Y;

                    var cross = d1x * d2y - d1y * d2x;

                    if (Math.Abs(cross) > 1e-10)
                    {
                        var t1x = newPoint.X - prevPoint.X;
                        var t1y = newPoint.Y - prevPoint.Y;
                        var t = (t1x * (prevPoint.Y - pp.Y) - t1y * (prevPoint.X - pp.X)) /
                                (d1x * t1y - d1y * t1x);

                        if (!double.IsNaN(t) && !double.IsInfinity(t))
                        {
                            var miterPt = new Point(
                                pp.X + d1x * t,
                                pp.Y + d1y * t);

                            // Check miter limit
                            var miterDx = miterPt.X - vertex.X;
                            var miterDy = miterPt.Y - vertex.Y;
                            var miterDist = Math.Sqrt(miterDx * miterDx + miterDy * miterDy);

                            if (miterDist <= halfWidth * miterLimit)
                            {
                                // Replace last point with miter point
                                side[^1] = miterPt;
                                side.Add(newPoint);
                            }
                            else
                            {
                                // Miter limit exceeded: fall back to bevel
                                side.Add(newPoint);
                            }
                        }
                        else
                        {
                            side.Add(newPoint);
                        }
                    }
                    else
                    {
                        side.Add(newPoint);
                    }
                }
                else
                {
                    side.Add(newPoint);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Adds a line cap between two offset endpoints at the end (or start) of an open path.
    /// </summary>
    private static void WidenAddCap(PathFigure figure, Point from, Point to, Point endPoint,
        double halfWidth, PenLineCap capType, bool isStart)
    {
        switch (capType)
        {
            case PenLineCap.Flat:
                // Direct connection
                figure.Segments.Add(new LineSegment(to));
                break;

            case PenLineCap.Square:
            {
                // Extend by halfWidth in the tangent direction
                var capDx = from.X - to.X;
                var capDy = from.Y - to.Y;
                var capLen = Math.Sqrt(capDx * capDx + capDy * capDy);
                if (capLen < 1e-10)
                {
                    figure.Segments.Add(new LineSegment(to));
                    break;
                }

                // Tangent direction perpendicular to from->to, pointing outward
                double tx, ty;
                if (isStart)
                {
                    tx = -(from.Y - to.Y) / capLen;
                    ty = (from.X - to.X) / capLen;
                }
                else
                {
                    tx = (from.Y - to.Y) / capLen;
                    ty = -(from.X - to.X) / capLen;
                }

                var ext1 = new Point(from.X + tx * halfWidth, from.Y + ty * halfWidth);
                var ext2 = new Point(to.X + tx * halfWidth, to.Y + ty * halfWidth);
                figure.Segments.Add(new LineSegment(ext1));
                figure.Segments.Add(new LineSegment(ext2));
                figure.Segments.Add(new LineSegment(to));
                break;
            }

            case PenLineCap.Round:
            {
                // Semicircular cap around endPoint
                var angle1 = Math.Atan2(from.Y - endPoint.Y, from.X - endPoint.X);
                var angle2 = Math.Atan2(to.Y - endPoint.Y, to.X - endPoint.X);
                var capDelta = angle2 - angle1;

                // Normalize and ensure we go around the outside (semicircle)
                if (capDelta > Math.PI) capDelta -= 2 * Math.PI;
                if (capDelta < -Math.PI) capDelta += 2 * Math.PI;

                if (Math.Abs(capDelta) < Math.PI - 0.01)
                {
                    if (capDelta > 0) capDelta -= 2 * Math.PI;
                    else capDelta += 2 * Math.PI;
                }

                var steps = Math.Max(4, (int)(Math.Abs(capDelta) / (Math.PI / 8)));
                for (int j = 1; j <= steps; j++)
                {
                    var t = j / (double)steps;
                    var angle = angle1 + capDelta * t;
                    figure.Segments.Add(new LineSegment(new Point(
                        endPoint.X + halfWidth * Math.Cos(angle),
                        endPoint.Y + halfWidth * Math.Sin(angle))));
                }
                break;
            }

            case PenLineCap.Triangle:
            {
                // Triangle cap: point extends halfWidth outward
                var capDx = from.X - to.X;
                var capDy = from.Y - to.Y;
                var capLen = Math.Sqrt(capDx * capDx + capDy * capDy);
                if (capLen < 1e-10)
                {
                    figure.Segments.Add(new LineSegment(to));
                    break;
                }

                double tx, ty;
                if (isStart)
                {
                    tx = -(from.Y - to.Y) / capLen;
                    ty = (from.X - to.X) / capLen;
                }
                else
                {
                    tx = (from.Y - to.Y) / capLen;
                    ty = -(from.X - to.X) / capLen;
                }

                var tip = new Point(endPoint.X + tx * halfWidth, endPoint.Y + ty * halfWidth);
                figure.Segments.Add(new LineSegment(tip));
                figure.Segments.Add(new LineSegment(to));
                break;
            }
        }
    }

    /// <inheritdoc />
    public override PathGeometry GetOutlinedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        // Flatten to remove curves, producing a simplified outline.
        // A full implementation would also resolve self-intersections;
        // for now, flattening is the primary simplification step.
        return GetFlattenedPathGeometry(tolerance, toleranceType);
    }
}

/// <summary>
/// Represents a subsection of a path geometry.
/// </summary>
public sealed class PathFigure : Animatable, IFormattable
{
    public static readonly DependencyProperty StartPointProperty =
        DependencyProperty.Register(nameof(StartPoint), typeof(Point), typeof(PathFigure),
            new PropertyMetadata(new Point()));

    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.Register(nameof(Segments), typeof(PathSegmentCollection), typeof(PathFigure),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsClosedProperty =
        DependencyProperty.Register(nameof(IsClosed), typeof(bool), typeof(PathFigure),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsFilledProperty =
        DependencyProperty.Register(nameof(IsFilled), typeof(bool), typeof(PathFigure),
            new PropertyMetadata(true));

    /// <summary>
    /// Gets or sets the start point of the figure.
    /// </summary>
    public Point StartPoint
    {
        get => (Point)GetValue(StartPointProperty)!;
        set { SetValue(StartPointProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets the collection of segments in this figure.
    /// </summary>
    public PathSegmentCollection Segments
    {
        get => (PathSegmentCollection)GetValue(SegmentsProperty)!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            PathSegmentCollection? old = (PathSegmentCollection?)GetValue(SegmentsProperty);
            OnFreezablePropertyChanged(old, value, SegmentsProperty);
            SetValue(SegmentsProperty, value);
            WritePostscript();
        }
    }

    /// <summary>
    /// Gets or sets whether this figure is closed.
    /// </summary>
    public bool IsClosed
    {
        get => (bool)GetValue(IsClosedProperty)!;
        set { SetValue(IsClosedProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets whether this figure is filled.
    /// </summary>
    public bool IsFilled
    {
        get => (bool)GetValue(IsFilledProperty)!;
        set { SetValue(IsFilledProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PathFigure"/> class.
    /// </summary>
    public PathFigure()
    {
        Segments = new PathSegmentCollection();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PathFigure"/> class.
    /// </summary>
    /// <param name="startPoint">The start point.</param>
    /// <param name="segments">The segments.</param>
    /// <param name="isClosed">Whether the figure is closed.</param>
    public PathFigure(Point startPoint, IEnumerable<PathSegment> segments, bool isClosed)
        : this()
    {
        StartPoint = startPoint;
        Segments.AddRange(segments);
        IsClosed = isClosed;
    }

    public bool MayHaveCurves() => Segments.Any(static segment =>
        segment is ArcSegment or BezierSegment or QuadraticBezierSegment
            or PolyBezierSegment or PolyQuadraticBezierSegment);

    public PathFigure GetFlattenedPathFigure() =>
        GetFlattenedPathFigure(Geometry.StandardFlatteningTolerance, ToleranceType.Absolute);

    public PathFigure GetFlattenedPathFigure(double tolerance, ToleranceType type)
    {
        var geometry = new PathGeometry(new[] { Clone() });
        PathGeometry flattened = geometry.GetFlattenedPathGeometry(tolerance, type);
        return flattened.Figures.Count == 0 ? new PathFigure() : flattened.Figures[0].Clone();
    }

    public new PathFigure Clone() => (PathFigure)base.Clone();
    public new PathFigure CloneCurrentValue() => (PathFigure)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new PathFigure();

    public override string ToString() => ToString(System.Globalization.CultureInfo.CurrentCulture);

    public string ToString(IFormatProvider? provider)
    {
        provider ??= System.Globalization.CultureInfo.CurrentCulture;
        var builder = new System.Text.StringBuilder();
        builder.Append('M').Append(' ').Append(FormatPoint(StartPoint, provider));
        foreach (PathSegment segment in Segments)
        {
            AppendSegment(builder, segment, provider);
        }
        if (IsClosed)
            builder.Append(" Z");
        return builder.ToString();
    }

    string IFormattable.ToString(string? format, IFormatProvider? provider) =>
        ToString(provider);

    internal static void AppendSegment(
        System.Text.StringBuilder builder,
        PathSegment segment,
        IFormatProvider provider)
    {
        switch (segment)
        {
            case LineSegment line:
                builder.Append(" L ").Append(FormatPoint(line.Point, provider));
                break;
            case PolyLineSegment polyLine:
                foreach (Point point in polyLine.Points)
                    builder.Append(" L ").Append(FormatPoint(point, provider));
                break;
            case BezierSegment bezier:
                builder.Append(" C ").Append(FormatPoint(bezier.Point1, provider)).Append(' ')
                    .Append(FormatPoint(bezier.Point2, provider)).Append(' ')
                    .Append(FormatPoint(bezier.Point3, provider));
                break;
            case PolyBezierSegment polyBezier:
                for (int i = 0; i + 2 < polyBezier.Points.Count; i += 3)
                    builder.Append(" C ").Append(FormatPoint(polyBezier.Points[i], provider)).Append(' ')
                        .Append(FormatPoint(polyBezier.Points[i + 1], provider)).Append(' ')
                        .Append(FormatPoint(polyBezier.Points[i + 2], provider));
                break;
            case QuadraticBezierSegment quadratic:
                builder.Append(" Q ").Append(FormatPoint(quadratic.Point1, provider)).Append(' ')
                    .Append(FormatPoint(quadratic.Point2, provider));
                break;
            case PolyQuadraticBezierSegment polyQuadratic:
                for (int i = 0; i + 1 < polyQuadratic.Points.Count; i += 2)
                    builder.Append(" Q ").Append(FormatPoint(polyQuadratic.Points[i], provider)).Append(' ')
                        .Append(FormatPoint(polyQuadratic.Points[i + 1], provider));
                break;
            case ArcSegment arc:
                builder.Append(" A ")
                    .Append(arc.Size.Width.ToString(null, provider)).Append(',')
                    .Append(arc.Size.Height.ToString(null, provider)).Append(' ')
                    .Append(arc.RotationAngle.ToString(null, provider)).Append(' ')
                    .Append(arc.IsLargeArc ? '1' : '0').Append(' ')
                    .Append(arc.SweepDirection == SweepDirection.Clockwise ? '1' : '0').Append(' ')
                    .Append(FormatPoint(arc.Point, provider));
                break;
        }
    }

    private static string FormatPoint(Point point, IFormatProvider provider) =>
        string.Format(provider, "{0},{1}", point.X, point.Y);
}

/// <summary>
/// Base class for path segments.
/// </summary>
public abstract class PathSegment : Animatable
{
    public static readonly DependencyProperty IsStrokedProperty =
        DependencyProperty.Register(nameof(IsStroked), typeof(bool), typeof(PathSegment),
            new PropertyMetadata(true));

    public static readonly DependencyProperty IsSmoothJoinProperty =
        DependencyProperty.Register(nameof(IsSmoothJoin), typeof(bool), typeof(PathSegment),
            new PropertyMetadata(false));

    /// <summary>
    /// Gets or sets whether the segment is stroked.
    /// </summary>
    public bool IsStroked
    {
        get => (bool)GetValue(IsStrokedProperty)!;
        set { SetValue(IsStrokedProperty, value); WritePostscript(); }
    }

    public bool IsSmoothJoin
    {
        get => (bool)GetValue(IsSmoothJoinProperty)!;
        set { SetValue(IsSmoothJoinProperty, value); WritePostscript(); }
    }

    public new PathSegment Clone() => (PathSegment)base.Clone();
    public new PathSegment CloneCurrentValue() => (PathSegment)base.CloneCurrentValue();

    /// <summary>
    /// Gets the points that define this segment.
    /// </summary>
    public abstract IEnumerable<Point> GetPoints();

    /// <summary>
    /// Gets the end point of this segment given a start point.
    /// </summary>
    /// <param name="startPoint">The start point of the segment.</param>
    /// <returns>The end point of the segment.</returns>
    public abstract Point GetEndPoint(Point startPoint);
}

/// <summary>
/// Represents a straight line segment.
/// </summary>
public sealed class LineSegment : PathSegment
{
    public static readonly DependencyProperty PointProperty =
        DependencyProperty.Register(nameof(Point), typeof(Point), typeof(LineSegment),
            new PropertyMetadata(new Point()));

    /// <summary>
    /// Gets or sets the end point of the line segment.
    /// </summary>
    public Point Point
    {
        get => (Point)GetValue(PointProperty)!;
        set { SetValue(PointProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LineSegment"/> class.
    /// </summary>
    public LineSegment()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LineSegment"/> class.
    /// </summary>
    /// <param name="point">The end point.</param>
    /// <param name="isStroked">Whether the segment is stroked.</param>
    public LineSegment(Point point, bool isStroked = true)
    {
        Point = point;
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints()
    {
        yield return Point;
    }

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Point;

    public new LineSegment Clone() => (LineSegment)base.Clone();
    public new LineSegment CloneCurrentValue() => (LineSegment)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new LineSegment();
}

/// <summary>
/// Represents a series of connected line segments.
/// </summary>
public sealed class PolyLineSegment : PathSegment
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(PointCollection), typeof(PolyLineSegment),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the collection of points defining the poly line.
    /// </summary>
    public PointCollection Points
    {
        get => (PointCollection)GetValue(PointsProperty)!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            PointCollection? old = (PointCollection?)GetValue(PointsProperty);
            OnFreezablePropertyChanged(old, value, PointsProperty);
            SetValue(PointsProperty, value);
            WritePostscript();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PolyLineSegment"/> class.
    /// </summary>
    public PolyLineSegment()
    {
        Points = new PointCollection();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PolyLineSegment"/> class.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <param name="isStroked">Whether the segment is stroked.</param>
    public PolyLineSegment(IEnumerable<Point> points, bool isStroked = true)
    {
        Points = new PointCollection(points);
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints() => Points;

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Points.Count > 0 ? Points[^1] : startPoint;

    public new PolyLineSegment Clone() => (PolyLineSegment)base.Clone();
    public new PolyLineSegment CloneCurrentValue() => (PolyLineSegment)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new PolyLineSegment();
}

/// <summary>
/// Represents an elliptical arc segment.
/// </summary>
public sealed class ArcSegment : PathSegment
{
    public static readonly DependencyProperty PointProperty =
        DependencyProperty.Register(nameof(Point), typeof(Point), typeof(ArcSegment),
            new PropertyMetadata(new Point()));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(Size), typeof(ArcSegment),
            new PropertyMetadata(new Size()));

    public static readonly DependencyProperty RotationAngleProperty =
        DependencyProperty.Register(nameof(RotationAngle), typeof(double), typeof(ArcSegment),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty IsLargeArcProperty =
        DependencyProperty.Register(nameof(IsLargeArc), typeof(bool), typeof(ArcSegment),
            new PropertyMetadata(false));

    public static readonly DependencyProperty SweepDirectionProperty =
        DependencyProperty.Register(nameof(SweepDirection), typeof(SweepDirection), typeof(ArcSegment),
            new PropertyMetadata(SweepDirection.Counterclockwise));

    /// <summary>
    /// Gets or sets the end point of the arc.
    /// </summary>
    public Point Point
    {
        get => (Point)GetValue(PointProperty)!;
        set { SetValue(PointProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets the size (radii) of the arc.
    /// </summary>
    public Size Size
    {
        get => (Size)GetValue(SizeProperty)!;
        set { SetValue(SizeProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets the rotation angle in degrees.
    /// </summary>
    public double RotationAngle
    {
        get => (double)GetValue(RotationAngleProperty)!;
        set { SetValue(RotationAngleProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets whether the arc spans more than 180 degrees.
    /// </summary>
    public bool IsLargeArc
    {
        get => (bool)GetValue(IsLargeArcProperty)!;
        set { SetValue(IsLargeArcProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets the sweep direction.
    /// </summary>
    public SweepDirection SweepDirection
    {
        get => (SweepDirection)GetValue(SweepDirectionProperty)!;
        set { SetValue(SweepDirectionProperty, value); WritePostscript(); }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArcSegment"/> class.
    /// </summary>
    public ArcSegment()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArcSegment"/> class.
    /// </summary>
    public ArcSegment(Point point, Size size, double rotationAngle, bool isLargeArc, SweepDirection sweepDirection, bool isStroked = true)
    {
        Point = point;
        Size = size;
        RotationAngle = rotationAngle;
        IsLargeArc = isLargeArc;
        SweepDirection = sweepDirection;
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints()
    {
        yield return Point;
    }

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Point;

    public new ArcSegment Clone() => (ArcSegment)base.Clone();
    public new ArcSegment CloneCurrentValue() => (ArcSegment)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new ArcSegment();
}

/// <summary>
/// Specifies the sweep direction of an arc.
/// </summary>
public enum SweepDirection
{
    /// <summary>
    /// Arcs are drawn counterclockwise.
    /// </summary>
    Counterclockwise,

    /// <summary>
    /// Arcs are drawn clockwise.
    /// </summary>
    Clockwise
}

/// <summary>
/// Represents a cubic Bezier curve segment.
/// </summary>
public sealed class BezierSegment : PathSegment
{
    public static readonly DependencyProperty Point1Property =
        DependencyProperty.Register(nameof(Point1), typeof(Point), typeof(BezierSegment), new PropertyMetadata(new Point()));
    public static readonly DependencyProperty Point2Property =
        DependencyProperty.Register(nameof(Point2), typeof(Point), typeof(BezierSegment), new PropertyMetadata(new Point()));
    public static readonly DependencyProperty Point3Property =
        DependencyProperty.Register(nameof(Point3), typeof(Point), typeof(BezierSegment), new PropertyMetadata(new Point()));

    /// <summary>
    /// Gets or sets the first control point.
    /// </summary>
    public Point Point1
    {
        get => (Point)GetValue(Point1Property)!;
        set { SetValue(Point1Property, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets the second control point.
    /// </summary>
    public Point Point2
    {
        get => (Point)GetValue(Point2Property)!;
        set { SetValue(Point2Property, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets the end point.
    /// </summary>
    public Point Point3
    {
        get => (Point)GetValue(Point3Property)!;
        set { SetValue(Point3Property, value); WritePostscript(); }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BezierSegment"/> class.
    /// </summary>
    public BezierSegment()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BezierSegment"/> class.
    /// </summary>
    public BezierSegment(Point point1, Point point2, Point point3, bool isStroked = true)
    {
        Point1 = point1;
        Point2 = point2;
        Point3 = point3;
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints()
    {
        yield return Point1;
        yield return Point2;
        yield return Point3;
    }

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Point3;

    public new BezierSegment Clone() => (BezierSegment)base.Clone();
    public new BezierSegment CloneCurrentValue() => (BezierSegment)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new BezierSegment();
}

/// <summary>
/// Represents a series of cubic Bezier curve segments.
/// </summary>
public sealed class PolyBezierSegment : PathSegment
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(PointCollection), typeof(PolyBezierSegment),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the collection of points (in groups of 3: control1, control2, end).
    /// </summary>
    public PointCollection Points
    {
        get => (PointCollection)GetValue(PointsProperty)!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            PointCollection? old = (PointCollection?)GetValue(PointsProperty);
            OnFreezablePropertyChanged(old, value, PointsProperty);
            SetValue(PointsProperty, value);
            WritePostscript();
        }
    }

    /// <summary>Initializes a new empty instance.</summary>
    public PolyBezierSegment()
    {
        Points = new PointCollection();
    }

    /// <summary>Initializes a new instance from points (in groups of 3) and a stroke flag.</summary>
    public PolyBezierSegment(IEnumerable<Point> points, bool isStroked = true)
    {
        Points = new PointCollection(points);
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints() => Points;

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Points.Count > 0 ? Points[^1] : startPoint;

    public new PolyBezierSegment Clone() => (PolyBezierSegment)base.Clone();
    public new PolyBezierSegment CloneCurrentValue() => (PolyBezierSegment)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new PolyBezierSegment();
}

/// <summary>
/// Represents a quadratic Bezier curve segment.
/// </summary>
public sealed class QuadraticBezierSegment : PathSegment
{
    public static readonly DependencyProperty Point1Property =
        DependencyProperty.Register(nameof(Point1), typeof(Point), typeof(QuadraticBezierSegment),
            new PropertyMetadata(new Point()));
    public static readonly DependencyProperty Point2Property =
        DependencyProperty.Register(nameof(Point2), typeof(Point), typeof(QuadraticBezierSegment),
            new PropertyMetadata(new Point()));

    /// <summary>
    /// Gets or sets the control point.
    /// </summary>
    public Point Point1
    {
        get => (Point)GetValue(Point1Property)!;
        set { SetValue(Point1Property, value); WritePostscript(); }
    }

    /// <summary>
    /// Gets or sets the end point.
    /// </summary>
    public Point Point2
    {
        get => (Point)GetValue(Point2Property)!;
        set { SetValue(Point2Property, value); WritePostscript(); }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QuadraticBezierSegment"/> class.
    /// </summary>
    public QuadraticBezierSegment()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QuadraticBezierSegment"/> class.
    /// </summary>
    public QuadraticBezierSegment(Point point1, Point point2, bool isStroked = true)
    {
        Point1 = point1;
        Point2 = point2;
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints()
    {
        yield return Point1;
        yield return Point2;
    }

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Point2;

    public new QuadraticBezierSegment Clone() => (QuadraticBezierSegment)base.Clone();
    public new QuadraticBezierSegment CloneCurrentValue() => (QuadraticBezierSegment)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new QuadraticBezierSegment();
}

/// <summary>
/// Represents a series of quadratic Bezier curve segments.
/// </summary>
public sealed class PolyQuadraticBezierSegment : PathSegment
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(PointCollection), typeof(PolyQuadraticBezierSegment),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the collection of points (in groups of 2: control, end).
    /// </summary>
    public PointCollection Points
    {
        get => (PointCollection)GetValue(PointsProperty)!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            PointCollection? old = (PointCollection?)GetValue(PointsProperty);
            OnFreezablePropertyChanged(old, value, PointsProperty);
            SetValue(PointsProperty, value);
            WritePostscript();
        }
    }

    /// <summary>Initializes a new empty instance.</summary>
    public PolyQuadraticBezierSegment()
    {
        Points = new PointCollection();
    }

    /// <summary>Initializes a new instance from points (in groups of 2) and a stroke flag.</summary>
    public PolyQuadraticBezierSegment(IEnumerable<Point> points, bool isStroked = true)
    {
        Points = new PointCollection(points);
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints() => Points;

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Points.Count > 0 ? Points[^1] : startPoint;

    public new PolyQuadraticBezierSegment Clone() => (PolyQuadraticBezierSegment)base.Clone();
    public new PolyQuadraticBezierSegment CloneCurrentValue() => (PolyQuadraticBezierSegment)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new PolyQuadraticBezierSegment();
}

// ImageSource is defined in ImageSource.cs
