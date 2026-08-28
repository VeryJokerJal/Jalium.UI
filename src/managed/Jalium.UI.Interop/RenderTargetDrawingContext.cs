using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Rendering;

namespace Jalium.UI.Interop;

/// <summary>
/// A DrawingContext implementation that renders to a RenderTarget.
/// </summary>
public sealed class RenderTargetDrawingContext : DrawingContextAdapter, IOffsetDrawingContext, IClipBoundsDrawingContext, IOpacityDrawingContext, IEffectDrawingContext, ITransformDrawingContext, ICacheableDrawingContext, ILayerCompositingDrawingContext
{
    private static readonly ConditionalWeakTable<PathGeometry, FrozenPathGeometryInfo>
        s_frozenPathGeometryInfo = new();

    private sealed class FrozenPathGeometryInfo
    {
        public FrozenPathGeometryInfo(PathGeometry geometry)
        {
            Bounds = geometry.Bounds;
            FillFigures = geometry.Figures.Where(static figure => figure.IsFilled).ToArray();
            HasNestedFillFigures = FillFigures.Length > 1 && FiguresHaveNesting(FillFigures);
        }

        public Rect Bounds { get; }
        public PathFigure[] FillFigures { get; }
        public bool HasNestedFillFigures { get; }
    }

    private const int MaxBrushCacheSize = 256;
    private const int MaxTextFormatCacheSize = 64;
    // Entry count, not bytes, is what evicts in practice: a catalog page with a
    // hundred-odd thumbnails blew past 64 and thrashed the LRU while sitting far
    // under the byte budget below (114 cards x 312 KiB is only ~35 MB). Keep the
    // count high enough that the byte ceiling is the real limit.
    private const int MaxBitmapCacheSize = 192;
    // GPU texture-cache byte budget (hard ceiling). This is VRAM usage and must NOT
    // be throttled by the managed process WorkingSet: doing so collapsed the budget
    // to 32MB under any real IDE memory footprint, forcing currently-visible card
    // textures to be LRU-evicted and re-uploaded every single frame (~42MB/frame on
    // the New-Solution wizard). With adaptive downscaling the live working set is
    // tiny; this ceiling only backstops pages that draw many full-resolution images.
    private const long MaxBitmapCacheBytes = 256L * 1024 * 1024;

    private readonly RenderTarget _renderTarget;
    private readonly RenderContext _context;
    private readonly Dictionary<Brush, NativeBrush> _brushCache = new();
    private readonly Dictionary<TextFormatCacheKey, NativeTextFormat> _textFormatCache = new();
    private readonly Dictionary<ImageSource, BitmapCacheEntry> _bitmapCache = new();
    private readonly Stack<DrawingState> _stateStack = new();

    // Sub-pixel outward nudge applied ONLY to the managed cull clip (CurrentClipBounds),
    // never to the native GPU scissor. It converts Rect.IntersectsWith's strict-edge test into
    // a real overlap so an element flush against the pixel-snapped clip boundary is still
    // admitted for redraw, guaranteeing the cull clip is a SUPERSET of the cleared pixels on a
    // partial frame. Under-admit after a clear = 1-frame flicker; over-admit = harmless
    // overdraw (still scissored by the unchanged aliased clip).
    private const double ClipCullEpsilon = 0.25;

    private readonly Stack<Rect?> _clipBoundsStack = new();
    private readonly Stack<PushedEffect> _effectStack = new();
    private readonly HashSet<IEffect> _effectApplicationPath =
        new(ReferenceEqualityComparer.Instance);

    // Depth of non-translate (scale/rotate/skew/matrix) transforms currently active
    // on the transform stack. Translate transforms go through the managed Offset
    // fast-path and do NOT increment this. When > 0, the managed coordinate space
    // (design coord + accumulated translate) no longer matches the actual screen
    // space, so any clip rect pushed before these transforms cannot be used for
    // managed culling against childBounds — native D2D clipping continues to be
    // correct because D2D applies the matrix itself.
    private int _nativeTransformDepth;

    // Full current native transform matrix mirrored on the managed side. The
    // native renderer applies transforms on the CPU (see AddText/AddSdfRect),
    // which scales bitmap-backed content (text glyph atlases, bitmaps) and
    // causes blurring under ScaleTransform. Mirroring the matrix here lets
    // DrawText pre-rasterize glyphs at screen resolution by pushing an inverse
    // transform and a scaled-up font size — giving a crisp result identical
    // to what D2D/DirectWrite would produce with matrix-aware text rendering.
    // Elements are m11, m12, m21, m22, dx, dy (same layout as Transform2D).
    private readonly double[] _currentNativeMatrix = new double[6] { 1, 0, 0, 1, 0, 0 };
    private readonly Stack<double[]> _nativeMatrixStack = new();

    // ── Retained GPU layer state (damage-driven composited-animation fast path) ──
    // Cached native capability (queried once); env kill-switch.
    private bool? _supportsRetainedLayers;
    private static readonly bool s_retainedLayersDisabled =
        Environment.GetEnvironmentVariable("JALIUM_DISABLE_RETAINED_LAYERS") == "1";

    // Kill-switch for sub-pixel glyph positioning — both the Ideal-mode identity
    // path (exact inter-glyph spacing for static text) and the scale-compensated
    // DrawText branch (no per-glyph trembling under a live zoom).
    // JALIUM_TEXT_SUBPIXEL_POSITIONING=0 restores the whole-pixel pen snapping of
    // every glyph everywhere — useful to A/B "Desk to p" spacing / zoom tremble
    // against the fix.
    private static readonly bool s_subpixelPositioningDisabled =
        Environment.GetEnvironmentVariable("JALIUM_TEXT_SUBPIXEL_POSITIONING") == "0";

    // True between BeginLayerCapture / EndLayerCapture (the RT is redirected into a
    // layer texture). Prevents nested layer capture.
    private bool _inLayerCapture;
    // Count of ambient PushOpacity scopes currently active. A subtree is only
    // layer-eligible when this is 0, so the realize does not bake an ancestor's
    // opacity into the cached texture (the composite would re-apply it).
    private int _opacityDepth;
    // While capturing a layer, ShouldRenderChild must cull against the LAYER bounds
    // (so the whole subtree is captured), not the window dirty-region clip. When
    // set, CurrentClipBounds returns this verbatim.
    private Rect? _layerCaptureClipBounds;
    // Depth of open effect captures (BeginEffectCapture..EndEffectCapture).
    // While > 0, CurrentClipBounds returns the innermost entry of
    // _effectCaptureCullOverrideStack instead of the window dirty-region /
    // ancestor-viewport clip — see the stack and the getter for why.
    private int _effectCaptureCullSuspendDepth;
    // One SURFACE-space capture rect per open BeginEffectCapture scope, mapped
    // through the same TransformRectAabb path PushClipBounds applies, so the
    // CurrentClipBounds getter can map it back into the drawing space at ANY
    // transform depth reached inside the capture. While a capture is open this
    // REPLACES the dirty-region / viewport clip as the cull source: the capture
    // texture is rebuilt from scratch every frame (no "kept from last frame"
    // pixels), so culling against the damage rect would punch holes into the
    // content the effect samples — but content outside the capture rect can
    // never reach the offscreen texture at all, so culling against the rect
    // keeps the capture complete WITHOUT re-emitting an entire long subtree
    // under an effect on every small-dirty-rect animation frame (the managed
    // emit cost was O(whole subtree) per frame while culling was suspended
    // outright). Kept in lockstep with _effectCaptureCullSuspendDepth.
    private readonly Stack<Rect> _effectCaptureCullOverrideStack = new();

    // ── Effect capture under a live non-translate transform ────────────────
    // An element effect is captured while its ancestors' RenderTransform /
    // LayoutTransform / Viewbox zoom is live on the native matrix stack, so the
    // offscreen texture holds SCREEN-space pixels (the capture applies the
    // matrix once). The composite must therefore be a 1:1 screen-space blit
    // instead of "pre-transform rect × live matrix" — otherwise the content is
    // transformed a second time on the way back and cropped to the singly-
    // transformed rect (a scaled shadow layer showed a square dark slab behind
    // its pill). One frame per open BeginEffectCapture scope, popped by
    // EndEffectCapture into _lastEndedEffectCapture for the ApplyElementEffect
    // that follows: it carries the SCREEN-space capture origin the composite's
    // UV offset must be relative to. Cleared with the cull override on the
    // per-frame self-heal.
    private readonly Stack<EffectCaptureFrame> _effectCaptureFrameStack = new();
    private EffectCaptureFrame _lastEndedEffectCapture;
    private readonly float[] _effectInverseMatrixScratch = new float[6];

    /// <summary>
    /// Screen-space (surface DIP) origin of an effect capture. <see cref="Transformed"/>
    /// is true when a non-identity native matrix was live at BeginEffectCapture, i.e.
    /// when <see cref="ScreenX"/>/<see cref="ScreenY"/> differ from the untransformed
    /// rect the caller passed.
    /// </summary>
    internal readonly record struct EffectCaptureFrame(double ScreenX, double ScreenY, bool Transformed);

    // Scoped opt-out for effects that deliberately deform their content with
    // the active matrix. Liquid glass drag uses this so text follows the same
    // transform as child borders and bitmaps instead of becoming a font-size
    // substitution inside the panel.
    private int _nativeTextTransformDepth;
    private long _bitmapCacheBytes;
    private long _bitmapCacheSequence;
    // Monotonic per-frame id, advanced once per render at the end of
    // TrimBitmapCacheIfNeeded (which every drawing context reaches via
    // TrimCacheIfNeeded). Cache entries touched in the current frame are never evicted
    // — this is what stops the per-frame upload thrash of currently-visible textures.
    // volatile for cross-thread visibility on the opt-in render-thread path (exact on
    // the default inline path where draw + trim are sequential on the UI thread).
    private volatile int _currentFrameId;
    private long _brushCacheSequence;
    private long _textFormatCacheSequence;
    private bool _closed;

    private readonly record struct TextFormatCacheKey(
        string FontFamily,
        double FontSize,
        int FontWeight,
        int FontStyle,
        int TextRenderingMode,
        int TextFormattingMode,
        int TextHintingMode,
        bool SubpixelPositioning);

    private sealed class BitmapCacheEntry
    {
        public BitmapCacheEntry(NativeBitmap bitmap, long estimatedBytes, long lastAccessSequence, long contentGeneration = 0)
        {
            Bitmap = bitmap;
            EstimatedBytes = estimatedBytes;
            LastAccessSequence = lastAccessSequence;
            ContentGeneration = contentGeneration;
        }

        public NativeBitmap Bitmap { get; }
        public long EstimatedBytes { get; }
        public long LastAccessSequence { get; set; }
        /// <summary>
        /// <see cref="ImageSource.ContentGeneration"/> of the pixels this entry actually uploaded.
        /// A mismatch on lookup means the source replaced its raster under a reference-equal
        /// identity, so the cached texture holds the wrong bytes: the entry either updates pixels
        /// in-place (D3D12 / Vulkan, <see cref="WriteableBitmap"/> only) and re-stamps this, or is
        /// dropped and re-uploaded.
        /// </summary>
        /// <remarks>
        /// This deliberately covers every source kind rather than just <see cref="WriteableBitmap"/>.
        /// The old test was <c>imageSource is WriteableBitmap &amp;&amp; revision differs</c>, which
        /// made a <see cref="BitmapImage"/> that upgraded its display bucket permanently
        /// un-detectable: the cache kept serving the small first decode, so the upgrade paid a
        /// full native decode and changed nothing on screen — no crash, no log line.
        /// </remarks>
        public long ContentGeneration { get; set; }

        /// <summary>
        /// Per-frame id at which this entry was last drawn. When it equals the current
        /// frame id the texture is in active use this frame and must never be evicted —
        /// the render thread may still be sampling it, and evicting + re-uploading
        /// visible bitmaps every frame is exactly the thrash this prevents.
        /// </summary>
        public int LastFrameUsed { get; set; }
    }

    /// <summary>
    /// Snapshot of a <see cref="DrawingContext.PushEffect(IEffect,Rect)"/> call. We store the
    /// <b>full capture region</b> (element bounds inflated by the effect's
    /// padding), not just the element bounds — that way <see cref="DrawingContext.PopEffect"/>
    /// can draw the whole blurred/shadowed/shader'd extent back onto the main
    /// target, including the padding where blur soft edges live. Drawing only
    /// the element bounds would crop those soft edges off, leaving the central
    /// pixels dominant and the original silhouette visible through the "blur".
    /// </summary>
    private readonly struct PushedEffect
    {
        public PushedEffect(IEffect effect, float x, float y, float w, float h,
            float captureX, float captureY)
        {
            Effect = effect;
            X = x; Y = y; W = w; H = h;
            CaptureX = captureX; CaptureY = captureY;
        }

        public IEffect Effect { get; }
        public float X { get; }   // capture region top-left x, on main RT
        public float Y { get; }   // capture region top-left y
        public float W { get; }   // capture region width (element + horizontal padding)
        public float H { get; }   // capture region height (element + vertical padding)
        public float CaptureX { get; }  // offscreen texture origin (same as X in this design)
        public float CaptureY { get; }
    }

    // Ellipse batch buffer for particle brush optimization
    private float[]? _ellipseBatchBuffer;
    private int _ellipseBatchCount;
    private bool _isEllipseBatching;

    // ─── SVG / Vector Drawing Performance Diagnostics ───
    private Stopwatch? _svgDiagStopwatch;
    private int _svgDrawGeometryCount;
    private int _svgDrawPathNativeCount;
    private int _svgDrawPathPolygonCount;
    private int _svgDrawCompoundCount;
    private int _svgPushTransformCount;
    private int _svgPopCount;
    private long _svgGetBrushTicks;
    private long _svgPathBuildTicks;
    private long _svgNativeCallTicks;
    private long _svgBoundsCalcTicks;
    private bool _svgDiagActive;
    private static int s_svgFrameNumber;

    // ─── SVG Rasterization Cache ───
    // Caches the rasterized BitmapImage for vector drawings to avoid
    // re-tessellating hundreds of paths every frame.
    // Uses BitmapImage (not NativeBitmap directly) so that the existing
    // GetNativeBitmap / _bitmapCache pipeline handles D3D12 resource lifecycle.
    private sealed class VectorDrawingCacheEntry
    {
        public BitmapImage? RasterizedBitmap;
        public int PixelWidth;
        public int PixelHeight;

        /// <summary>
        /// The inner <see cref="ImageSource"/>s the rasterization read pixels from, each with the
        /// <see cref="ImageSource.ContentGeneration"/> that was baked into this raster.
        /// </summary>
        /// <remarks>
        /// <para>The entry is keyed on the OUTER vector source, but a <see cref="DrawingImage"/>
        /// wrapping an <see cref="ImageDrawing"/> — or a shape filled with an
        /// <see cref="ImageBrush"/> — bakes an inner bitmap's pixels into this raster. The first
        /// rasterization of a URI-backed inner source necessarily runs before its deferred decode
        /// has published anything, and produces a valid, entirely blank buffer. Without this list
        /// the publish raises <c>RasterChanged</c> for the INNER source, the eviction looks that
        /// inner source up under the outer key, misses, and the blank raster is served for the life
        /// of the window (the context outlives every frame) unless the draw rect happens to change
        /// size.</para>
        /// <para>Generations as well as identities, for the same division of labour the GPU bitmap
        /// cache uses: the compare is what guarantees CORRECTNESS on the next draw, and the event
        /// is what makes the release PROMPT. The compare is the half that still holds when no
        /// eviction is delivered at all — a host with no dispatcher pumping the decode notifier, a
        /// context whose drain has not run yet — and the event is the half that covers a publish
        /// landing DURING the rasterization, whose generation this list would otherwise record as
        /// already included.</para>
        /// </remarks>
        public List<(ImageSource Source, long Generation)>? TouchedSources;

        /// <summary>
        /// Whether every source this raster was built from still carries the pixels it was built
        /// from.
        /// </summary>
        public bool MatchesTouchedSourceGenerations()
        {
            if (TouchedSources is null)
            {
                return true;
            }

            for (var i = 0; i < TouchedSources.Count; i++)
            {
                var (source, generation) = TouchedSources[i];
                if (source.ContentGeneration != generation)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether this raster was built from <paramref name="source"/>'s pixels.</summary>
        public bool DependsOn(ImageSource source)
        {
            if (TouchedSources is null)
            {
                return false;
            }

            for (var i = 0; i < TouchedSources.Count; i++)
            {
                if (ReferenceEquals(TouchedSources[i].Source, source))
                {
                    return true;
                }
            }

            return false;
        }
    }
    private readonly Dictionary<ImageSource, VectorDrawingCacheEntry> _vectorDrawingCache = new();

    /// <summary>
    /// Gets the underlying render target.
    /// </summary>
    public RenderTarget RenderTarget => _renderTarget;

    /// <summary>Gets the render context this drawing session is bound to.</summary>
    public RenderContext Context => _context;

    /// <summary>
    /// Composites an <see cref="InkLayerBitmap"/> onto the render target.
    /// Used by InkCanvas to flush its GPU-side committed-ink layer each
    /// frame after per-stroke brush shader dispatches.
    /// </summary>
    /// <remarks>
    /// <paramref name="dstX"/> / <paramref name="dstY"/> are in the
    /// caller's local coordinate space. Translate-only transforms go
    /// through the managed <see cref="Offset"/> fast-path (not the native
    /// transform stack), so every other draw method in this class adds
    /// the offset before forwarding to native — we do the same here or
    /// the bitmap lands at screen (0,0) regardless of the owning visual's
    /// position.
    /// </remarks>
    public void BlitInkLayer(InkLayerBitmap bitmap, float dstX, float dstY, float opacity = 1.0f)
    {
        if (_closed || bitmap is null || !bitmap.IsValid) return;
        if (_renderTarget is null || _renderTarget.Handle == nint.Zero) return;
        NativeMethods.RenderTargetBlitInkLayer(
            _renderTarget.Handle, bitmap.Handle,
            dstX + (float)Offset.X,
            dstY + (float)Offset.Y,
            opacity);
    }

    /// <summary>
    /// Gets or sets the current transform offset for child rendering.
    /// </summary>
    public Point Offset { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// The clip-bounds stack is maintained in SURFACE space (post-transform) by
    /// <see cref="PushClipBounds"/>: a clip pushed under a non-translate transform is
    /// mapped through the accumulated native matrix before it is intersected and
    /// stored, so every entry is in one consistent absolute space regardless of the
    /// transform depth at which it was pushed. This getter maps the top entry BACK
    /// into the current drawing (offset/screen) space via the inverse of the
    /// accumulated native matrix, so the value stays directly comparable to a child's
    /// offset-space bounds in <see cref="Visual.ShouldRenderChild"/> at ANY transform
    /// depth — restoring viewport culling for scaled/rotated subtrees (a composited
    /// RenderTransform animation no longer forces the whole subtree to re-emit every
    /// frame).
    ///
    /// This value is ONLY a managed culling hint — the native renderer (D2D/D3D12)
    /// owns the real per-transform clip — so the result is deliberately CONSERVATIVE:
    /// AABBs of transformed corners can only grow the rectangle (less culling, never
    /// wrong pixels), and a singular/non-invertible matrix falls back to <c>null</c>
    /// (no culling), so a visible child can never be discarded.
    /// </remarks>
    public Rect? CurrentClipBounds
    {
        get
        {
            // While realizing a retained layer, cull only against the layer's own
            // bounds so the whole subtree is captured (the window dirty-region clip
            // would wrongly drop in-layer content outside the current dirty rect).
            if (_layerCaptureClipBounds.HasValue)
                return _layerCaptureClipBounds;

            // While capturing an element for an effect (BeginEffectCapture..
            // EndEffectCapture), cull against the CAPTURE RECT instead of the
            // window dirty-region / ancestor-viewport clip: the capture
            // texture is rebuilt from scratch every frame with no "pixels
            // outside the dirty rect kept from last frame" fallback, so
            // dirty-region culling (ShouldRenderChild / DrawingReplayer's
            // AABB short-cut) would punch real holes into the content the
            // effect samples — a glow then renders a truncated silhouette
            // with a hard seam at the damage-rect edge. Content outside the
            // capture rect is different: it can never land in the offscreen
            // texture (the rect IS the texture's bounds), so culling it is
            // free — and keeps a long non-virtualized subtree under an
            // effect from re-emitting in full on every small-dirty-rect
            // frame. The override entry was stored in SURFACE space by
            // BeginEffectCapture (the exact PushClipBounds mapping), so map
            // it back through the current inverse matrix exactly like the
            // regular stack path below. (Same reasoning as
            // _layerCaptureClipBounds above; the surface-space round-trip is
            // what makes the rect comparable across nested transform depths.)
            if (_effectCaptureCullSuspendDepth > 0)
            {
                // Defensive only — Begin pushes an entry whenever it bumps the
                // depth. No entry → keep the legacy "no cull" (never wrong).
                if (_effectCaptureCullOverrideStack.Count == 0)
                    return null;

                var capture = _effectCaptureCullOverrideStack.Peek();
                if (_nativeTransformDepth <= 0)
                    return capture;

                var cm = new Jalium.UI.Media.Matrix(
                    _currentNativeMatrix[0], _currentNativeMatrix[1],
                    _currentNativeMatrix[2], _currentNativeMatrix[3],
                    _currentNativeMatrix[4], _currentNativeMatrix[5]);
                if (!cm.TryInvert(out var captureInv))
                    return null; // non-invertible → no cull, never under-cull

                return TransformRectAabb(capture,
                    captureInv.M11, captureInv.M12, captureInv.M21, captureInv.M22,
                    captureInv.OffsetX, captureInv.OffsetY);
            }

            if (_clipBoundsStack.Count == 0)
                return null;

            var top = _clipBoundsStack.Peek();

            // Depth 0: the native matrix is identity, surface space == screen space,
            // so the stored value is already in the comparison space — return as-is
            // (bit-for-bit identical to the historical fast path).
            if (_nativeTransformDepth <= 0 || !top.HasValue)
                return top;

            var m = new Jalium.UI.Media.Matrix(
                _currentNativeMatrix[0], _currentNativeMatrix[1],
                _currentNativeMatrix[2], _currentNativeMatrix[3],
                _currentNativeMatrix[4], _currentNativeMatrix[5]);
            if (!m.TryInvert(out var inv))
                return null; // non-invertible (e.g. zero scale) → no cull, never under-cull

            return TransformRectAabb(top.Value,
                inv.M11, inv.M12, inv.M21, inv.M22, inv.OffsetX, inv.OffsetY);
        }
    }

    /// <summary>
    /// Intersects an offset/screen-space clip rectangle with the current clip-bounds
    /// stack and pushes the result, keeping every entry in one consistent SURFACE
    /// space. Under a non-translate transform the incoming rect is first mapped
    /// through the accumulated native matrix (so it is comparable to entries pushed
    /// at other transform depths); at depth 0 the matrix is identity and this is a
    /// no-op. See <see cref="CurrentClipBounds"/> for why an over-large result is
    /// always safe (the value is only a managed culling hint).
    /// </summary>
    private void PushClipBounds(Rect offsetScreenClip)
    {
        Rect surfaceClip = _nativeTransformDepth > 0
            ? TransformRectAabb(offsetScreenClip,
                _currentNativeMatrix[0], _currentNativeMatrix[1],
                _currentNativeMatrix[2], _currentNativeMatrix[3],
                _currentNativeMatrix[4], _currentNativeMatrix[5])
            : offsetScreenClip;

        Rect? effectiveClip = surfaceClip;
        if (_clipBoundsStack.Count > 0)
        {
            var parentClip = _clipBoundsStack.Peek();
            effectiveClip = parentClip.HasValue ? Rect.Intersect(parentClip.Value, surfaceClip) : surfaceClip;
        }
        _clipBoundsStack.Push(effectiveClip);
    }

    /// <summary>
    /// Returns the axis-aligned bounding box of <paramref name="r"/> transformed by
    /// the affine matrix [m11 m12 / m21 m22 / dx dy] (WPF row-vector convention:
    /// x' = x·m11 + y·m21 + dx, y' = x·m12 + y·m22 + dy). Used to map clip bounds
    /// in/out of surface space conservatively (the AABB never shrinks the region).
    /// </summary>
    private static Rect TransformRectAabb(Rect r,
        double m11, double m12, double m21, double m22, double dx, double dy)
    {
        double x0 = r.X, y0 = r.Y, x1 = r.X + r.Width, y1 = r.Y + r.Height;

        double ax = x0 * m11 + y0 * m21 + dx, ay = x0 * m12 + y0 * m22 + dy;
        double bx = x1 * m11 + y0 * m21 + dx, by = x1 * m12 + y0 * m22 + dy;
        double cx = x1 * m11 + y1 * m21 + dx, cy = x1 * m12 + y1 * m22 + dy;
        double ex = x0 * m11 + y1 * m21 + dx, ey = x0 * m12 + y1 * m22 + dy;

        double minX = Math.Min(Math.Min(ax, bx), Math.Min(cx, ex));
        double minY = Math.Min(Math.Min(ay, by), Math.Min(cy, ey));
        double maxX = Math.Max(Math.Max(ax, bx), Math.Max(cx, ex));
        double maxY = Math.Max(Math.Max(ay, by), Math.Max(cy, ey));

        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    /// <summary>
    /// Resolves omitted layout-clip edges against the currently visible drawing
    /// extent. Keeping the resulting rectangle near the render target avoids the
    /// float cancellation caused by encoding a half-plane as a billion-pixel rect.
    /// </summary>
    internal static Rect ResolveBoundsClip(Rect bounds, ClipEdges edges, Rect limit)
    {
        if (bounds.IsEmpty || edges == ClipEdges.All || limit.IsEmpty)
        {
            return bounds;
        }

        var left = (edges & ClipEdges.Left) != 0
            ? bounds.Left
            : Math.Min(bounds.Left, limit.Left);
        var top = (edges & ClipEdges.Top) != 0
            ? bounds.Top
            : Math.Min(bounds.Top, limit.Top);
        var right = (edges & ClipEdges.Right) != 0
            ? bounds.Right
            : Math.Max(bounds.Right, limit.Right);
        var bottom = (edges & ClipEdges.Bottom) != 0
            ? bounds.Bottom
            : Math.Max(bounds.Bottom, limit.Bottom);

        return new Rect(left, top, right - left, bottom - top);
    }

    private Rect GetBoundsClipLimit()
    {
        var current = CurrentClipBounds;
        if (current is { IsEmpty: false } currentBounds)
        {
            return currentBounds;
        }

        var viewport = new Rect(
            0,
            0,
            _renderTarget.Width / _renderTarget.DpiScaleX,
            _renderTarget.Height / _renderTarget.DpiScaleY);
        if (_nativeTransformDepth <= 0)
        {
            return viewport;
        }

        var matrix = new Jalium.UI.Media.Matrix(
            _currentNativeMatrix[0], _currentNativeMatrix[1],
            _currentNativeMatrix[2], _currentNativeMatrix[3],
            _currentNativeMatrix[4], _currentNativeMatrix[5]);
        return matrix.TryInvert(out var inverse)
            ? TransformRectAabb(
                viewport,
                inverse.M11, inverse.M12, inverse.M21, inverse.M22,
                inverse.OffsetX, inverse.OffsetY)
            : viewport;
    }

    // ── ILayerCompositingDrawingContext (retained GPU layer fast path) ──

    /// <inheritdoc />
    public bool SupportsRetainedLayers
    {
        get
        {
            if (s_retainedLayersDisabled) return false;
            if (_supportsRetainedLayers is bool cached) return cached;
            bool ok = _renderTarget != null && _renderTarget.Handle != nint.Zero
                && NativeMethods.RenderTargetSupportsRetainedLayers(_renderTarget.Handle) != 0;
            _supportsRetainedLayers = ok;
            return ok;
        }
    }

    /// <inheritdoc />
    public nint BeginLayerCapture(nint existingLayer, Rect worldBounds)
    {
        if (_closed || _inLayerCapture) return 0;
        if (!SupportsRetainedLayers) return 0;
        // An ancestor non-translate transform or opacity would bake into the
        // captured content (the composite re-applies them) — fall back instead.
        if (_nativeTransformDepth > 0 || _opacityDepth > 0) return 0;
        if (worldBounds.Width <= 0 || worldBounds.Height <= 0) return 0;
        if (_renderTarget == null || _renderTarget.Handle == nint.Zero) return 0;

        nint layer = NativeMethods.RenderTargetRealizeLayerBegin(
            _renderTarget.Handle, existingLayer,
            (float)worldBounds.X, (float)worldBounds.Y,
            (float)worldBounds.Width, (float)worldBounds.Height);
        if (layer == 0) return 0;

        _inLayerCapture = true;
        _layerCaptureClipBounds = worldBounds;
        return layer;
    }

    /// <inheritdoc />
    public void EndLayerCapture(nint layer)
    {
        if (_closed || !_inLayerCapture) return;
        if (_renderTarget != null && _renderTarget.Handle != nint.Zero)
            NativeMethods.RenderTargetRealizeLayerEnd(_renderTarget.Handle, layer);
        _inLayerCapture = false;
        _layerCaptureClipBounds = null;
    }

    /// <inheritdoc />
    public void CompositeLayer(nint layer, Rect worldBounds, double opacity,
        Transform? transform, double originX, double originY)
    {
        if (_closed || layer == 0) return;
        if (_renderTarget == null || _renderTarget.Handle == nint.Zero) return;
        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.DRAW_COMPOSITE);

        // Apply the live RenderTransform exactly as the normal child path would
        // (the caller has already set Offset = childOffset). Opacity is passed to
        // the composite directly (AddBitmap folds in ambient opacity), so it is
        // NOT pushed here.
        //
        // A pure TranslateTransform (the RenderTransformOrigin-free case, i.e. the
        // overwhelmingly common slide / reorder / follow-the-pointer animation) is NOT
        // pushed onto the native matrix stack: PushTransform(Transform) folds it into the
        // managed Offset fast path, which every regular draw op honours by adding Offset to
        // its coordinates before crossing into native. worldBounds, however, is already an
        // absolute (childOffset-based) rectangle and was handed straight to native, so the
        // translation silently vanished: the cached layer was composited at the element's
        // UN-translated slot while dirty tracking (GetDirtyRenderBounds) and child culling
        // (ShouldRenderChild) both followed the translated one. Symptom: a TranslateTransform-
        // animated container does not move at all, and neighbours it passes over get
        // partially erased (they are culled against the translated bounds yet drawn — if at
        // all — at the stale position). Fold the Offset delta produced by the push into the
        // composite rectangle so the quad lands where the element's ink is accounted for;
        // non-translate transforms still travel through the native matrix and see no delta.
        var offsetBeforePush = Offset;
        bool pushed = false;
        if (transform != null)
        {
            ((ITransformDrawingContext)this).PushTransform(transform, originX, originY);
            pushed = true;
        }
        var translateX = Offset.X - offsetBeforePush.X;
        var translateY = Offset.Y - offsetBeforePush.Y;

        NativeMethods.RenderTargetCompositeLayer(
            _renderTarget.Handle, layer,
            (float)(worldBounds.X + translateX), (float)(worldBounds.Y + translateY),
            (float)worldBounds.Width, (float)worldBounds.Height,
            (float)opacity);
        LastCompositedLayerRectForTests = new Rect(
            worldBounds.X + translateX, worldBounds.Y + translateY, worldBounds.Width, worldBounds.Height);
        CompositedLayerCountForTests++;

        if (pushed)
            ((ITransformDrawingContext)this).PopTransform();
    }

    /// <summary>Screen-space rectangle handed to native by the most recent <see cref="CompositeLayer"/> (tests).</summary>
    internal Rect LastCompositedLayerRectForTests { get; private set; }

    /// <summary>Number of <see cref="CompositeLayer"/> calls that reached native on this context (tests).</summary>
    internal int CompositedLayerCountForTests { get; private set; }

    /// <summary>
    /// The owning thread's frame prologue. Destroys retained layers queued by
    /// <see cref="Visual"/> (idle-eviction / detach happen without a render
    /// context) so the GPU textures are released promptly through the
    /// fence-gated native graveyard, applies the bitmap-cache evictions other
    /// threads queued, and re-arms the per-frame effect-capture self-heal.
    /// </summary>
    /// <remarks>
    /// Every host that owns a context calls this once per frame BEFORE it draws
    /// (<c>Window</c> on both the inline and render-thread paths,
    /// <c>PopupWindow</c>, <c>DockIndicatorWindow</c>), which is what makes it
    /// the one place where "this must happen on the thread that owns the caches,
    /// before anything reads them" can be honoured for all of them at once.
    /// </remarks>
    internal void DrainPendingRetainedLayers()
    {
        // First, because it is the only step here that must precede every cache READ this frame,
        // and because it is the only one that does not need a live render target: a context whose
        // RT handle is momentarily zero still has to retire the uploads other threads asked it to
        // drop, or the request is silently lost until the next raise.
        DrainPendingCacheEvictions();

        // Per-frame self-heal for the effect-capture cull override: if a render
        // exception unwound past an open BeginEffectCapture (Visual.RenderDirect
        // has no try/finally and the window's catch-all keeps the process alive),
        // the counter would stay pinned > 0 on this POOLED context and clamp
        // clip-bounds culling to a stale capture rect for every subsequent frame
        // — an invisible, permanent correctness/performance cliff. A frame can
        // never legitimately start with an open capture, so zero both here (the
        // native side has the symmetric guard in ResetGpuReplay).
        _effectCaptureCullSuspendDepth = 0;
        _effectCaptureCullOverrideStack.Clear();
        _effectCaptureFrameStack.Clear();
        _lastEndedEffectCapture = default;
        _suppressedEffectCaptureDepth = 0;
        if (_renderTarget == null || _renderTarget.Handle == nint.Zero) return;
        while (Visual.TryDequeuePendingLayerDestroy(out nint h))
        {
            if (h != 0) _renderTarget.DestroyRetainedLayer(h);
        }
    }

    /// <summary>
    /// Downgrade ONLY the snapshot/background-sampling effects (backdrop filter +
    /// LiquidGlass refraction) to their cheap flat fallbacks. Set true during live
    /// resize (_isSizing): the background snapshot lags the in-flight resized buffer,
    /// so the real refraction samples the wrong screen region and the glass content
    /// appears displaced ("outside the panel"). Glow/shadow effects are element-capture
    /// based (not snapshot based) and are unaffected — they keep rendering fully.
    /// </summary>
    internal bool SimplifyBackdropEffects { get; set; }

    /// <summary>
    /// Draw element content directly instead of redirecting every shadow, glow,
    /// blur, or custom effect through the renderer-wide offscreen surface. Live
    /// window resize enables this policy because those shared surfaces otherwise
    /// serialize effect-heavy frames behind the previous GPU fence. The window
    /// schedules a normal full frame when resizing ends, so the visual downgrade
    /// exists only while the pointer is moving.
    /// </summary>
    internal bool SimplifyElementEffects { get; set; }

    bool IEffectDrawingContext.IsElementEffectCaptureEnabled =>
        !SimplifyElementEffects;

    // DrawingRecorder replay can contain explicit Begin/End effect commands
    // even when Visual itself observes the policy above. Track those suppressed
    // scopes so their content stays on the main target and the replay remains
    // balanced without entering the native capture pipeline.
    private int _suppressedEffectCaptureDepth;

    /// <summary>
    /// Begins batching ellipse draw calls. While batching is active, DrawEllipse calls
    /// with solid color brushes are accumulated and flushed as a single native call.
    /// </summary>
    public void BeginEllipseBatch(int estimatedCount = 256)
    {
        if (_isEllipseBatching) return;
        _isEllipseBatching = true;
        _ellipseBatchCount = 0;
        var bufferSize = estimatedCount * 5;
        if (_ellipseBatchBuffer == null || _ellipseBatchBuffer.Length < bufferSize)
            _ellipseBatchBuffer = new float[bufferSize];
    }

    /// <summary>
    /// Flushes all accumulated ellipses as a single native batch call.
    /// </summary>
    public void EndEllipseBatch()
    {
        if (!_isEllipseBatching) return;
        _isEllipseBatching = false;

        if (_ellipseBatchCount > 0 && _ellipseBatchBuffer != null)
        {
            _renderTarget.FillEllipseBatch(_ellipseBatchBuffer, (uint)_ellipseBatchCount);
            _ellipseBatchCount = 0;
        }
    }

    /// <summary>
    /// Installs <c>MediaRenderCacheHost</c> into <c>Visual.RenderCacheHost</c>
    /// on first use of this type. Kept on the drawing-context class — which
    /// is guaranteed to be loaded before any render happens — so the
    /// retained-mode cache is live for every frame without requiring a
    /// dedicated startup hook in each app entry point.
    /// </summary>
    static RenderTargetDrawingContext()
    {
        Jalium.UI.Media.Rendering.MediaRenderCacheHost.Bootstrap();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderTargetDrawingContext"/> class.
    /// </summary>
    /// <param name="renderTarget">The render target to draw on.</param>
    /// <param name="context">The render context for creating resources.</param>
    public RenderTargetDrawingContext(RenderTarget renderTarget, RenderContext context)
    {
        _renderTarget = renderTarget ?? throw new ArgumentNullException(nameof(renderTarget));
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // Subscribe to GPU bitmap eviction requests so the idle-resource
        // reclaimer can free this context's NativeBitmap upload of a source
        // when the owning element has been off-screen long enough.
        _gpuEvictionHandler = OnGpuCacheEvictionRequested;
        ImageSource.GpuCacheEvictionRequested += _gpuEvictionHandler;

        // And to raster replacement, which is a different statement: eviction says "release this
        // memory, the bytes are still correct", RasterChanged says "these bytes are WRONG".
        // A deferred bitmap publishing a bigger bucket keeps its reference identity, and this
        // cache is keyed on exactly that identity, so without this leg the upgrade decode runs in
        // full and the screen never changes.
        //
        // Neither handler touches the caches inline — both only QUEUE the source. See
        // _pendingCacheEvictions for the ownership argument.
        _rasterChangedHandler = OnImageRasterChanged;
        ImageSource.RasterChanged += _rasterChangedHandler;
    }

    private readonly Action<ImageSource> _gpuEvictionHandler;
    private readonly Action<ImageSource> _rasterChangedHandler;

    // Sources whose cached upload is to be dropped: enqueued by whichever thread RAISED the
    // eviction, applied by the thread that OWNS this context.
    //
    // Both events are bare synchronous invokes on the raising thread, and their raisers are the
    // decode publish, the animated-substitute swap and every source assignment — delivered on the
    // main dispatcher. But the main dispatcher does not own this context: whenever the render
    // thread is running (the default on Windows/D3D12) it exclusively owns creation, drawing and
    // the cache trim, and it is inside GetNativeBitmap — TryGetValue, in-place entry mutation,
    // indexer insert, a non-atomic _bitmapCacheBytes read-modify-write and an ENUMERATING trim
    // loop — over these very dictionaries at exactly the moment a publish lands. Evicting inline
    // from the raising thread is therefore a second writer to a plain Dictionary plus a Dispose of
    // a native texture the render thread is about to sample: the "native brush use-after-free +
    // Dictionary corruption" pair that Window's render-thread ownership rule exists to prevent.
    // Neither leg is observable when it happens — the notifier catches per item and the render
    // loop discards the frame — so it presents as an image that is wrong on some machines with
    // nothing logged, which is the whole failure class this pipeline work exists to remove.
    //
    // Deferring by one frame is sound because the event is not the correctness mechanism: the
    // ContentGeneration compare in GetNativeBitmap already re-uploads a replaced raster on the very
    // next draw with no event at all. What the event buys is PROMPT release of the superseded
    // texture, and prompt is not weakened here — the same notifier drain that raises it also raises
    // ContentChangedBatchCompleted, which is what schedules the frame that applies it. Only the
    // idle reclaimer, which asks for no repaint of its own, now waits for whatever frame comes
    // next; it is asking to give memory back, so a late release costs nothing.
    private readonly object _pendingCacheEvictionLock = new();
    // A set, not a list: the bucket ladder publishes several times per image, and each publish
    // raises once. Collapsing them keeps the drain proportional to distinct sources.
    private readonly HashSet<ImageSource> _pendingCacheEvictions = new();
    // Fast-path flag so an idle frame does not take the lock. Only ever written under the lock;
    // a read that races a raise costs one frame of delay and can never lose an eviction, because
    // the flag stays set until a drain clears it under that same lock.
    private volatile bool _hasPendingCacheEvictions;

    /// <summary>
    /// Queues the reclaimer's request to release this context's upload of <paramref name="source"/>.
    /// </summary>
    private void OnGpuCacheEvictionRequested(ImageSource source) => QueueCacheEviction(source);

    /// <summary>
    /// Queues the drop of this context's upload of a source whose pixels were replaced under a
    /// reference-equal identity, so the next draw re-uploads from the new raster instead of
    /// re-serving the superseded one.
    /// </summary>
    /// <remarks>
    /// Belt and braces with the generation compare in <see cref="GetNativeBitmap"/>: that compare
    /// is what guarantees CORRECTNESS on the very next draw even if this event is never delivered,
    /// while this handler is what releases the superseded GPU texture promptly rather than leaving
    /// it resident until the trim pass notices. That is precisely why it is safe to defer the drop
    /// to the owning thread's next frame instead of doing it here, on the raiser's thread.
    /// </remarks>
    private void OnImageRasterChanged(ImageSource source) => QueueCacheEviction(source);

    /// <summary>
    /// Records that <paramref name="source"/>'s cached upload must be dropped. Callable from ANY
    /// thread; touches nothing but the pending set.
    /// </summary>
    private void QueueCacheEviction(ImageSource source)
    {
        if (source is null) return;

        lock (_pendingCacheEvictionLock)
        {
            // Racy read of _closed, deliberately: losing the race merely leaves one reference in a
            // set that Close() has already abandoned, and the drain that would act on it no longer
            // runs. Reading it inside the lock keeps it ordered against Close()'s own Clear.
            if (_closed) return;

            _pendingCacheEvictions.Add(source);
            _hasPendingCacheEvictions = true;
        }
    }

    /// <summary>
    /// Applies the evictions queued by other threads. Must run on the thread that owns this
    /// context, and does so as the first step of that thread's frame — see
    /// <see cref="DrainPendingRetainedLayers"/>.
    /// </summary>
    private void DrainPendingCacheEvictions()
    {
        if (!_hasPendingCacheEvictions) return;

        ImageSource[] evictions;
        lock (_pendingCacheEvictionLock)
        {
            var count = _pendingCacheEvictions.Count;
            _hasPendingCacheEvictions = false;
            if (count == 0) return;

            evictions = new ImageSource[count];
            _pendingCacheEvictions.CopyTo(evictions);
            _pendingCacheEvictions.Clear();
        }

        foreach (var source in evictions)
        {
            try
            {
                DropCachedUploadOf(source);
            }
            catch (Exception ex)
            {
                // One bad source must not cost the others their eviction, and must NOT escape into
                // the frame: this runs at the top of Replay, where a throw is swallowed by the
                // render loop's catch-all and would discard every frame for as long as the entry
                // kept failing — a permanently black window from a texture-release fault. Reported
                // through the Release-live channel rather than swallowed; the entry is already out
                // of the dictionary by the time Dispose can throw, so the cache stays coherent and
                // only the native handle leaks.
                ImageDiagnostics.DecodeFailed(
                    DescribeImageSource(source), "gpu cache eviction", ex);
            }
        }
    }

    /// <summary>
    /// Drops this context's upload of <paramref name="source"/> and any vector raster derived from
    /// it. Owning thread only — reached solely from <see cref="DrainPendingCacheEvictions"/>.
    /// </summary>
    private void DropCachedUploadOf(ImageSource source)
    {
        // The bitmap cache is keyed by ImageSource reference identity, so a
        // direct lookup is enough — no scan needed. RemoveBitmapCacheEntry
        // disposes the NativeBitmap (which calls jalium_bitmap_destroy on the
        // GPU texture) and accounts for the freed bytes.
        if (_bitmapCache.TryGetValue(source, out var entry))
        {
            RemoveBitmapCacheEntry(source, entry);
        }

        // Also drop any rasterized vector-drawing slot keyed on this source —
        // it was uploaded as a NativeBitmap via the same pipeline.
        if (_vectorDrawingCache.TryGetValue(source, out var vector))
        {
            DropVectorRaster(source, vector);
        }

        // ...and every slot whose raster was rasterized FROM this source. A DrawingImage keeps its
        // own identity while the bitmap inside it publishes under a different one, so the key
        // lookup above cannot see that relationship; the dependency set recorded at rasterization
        // time is what does. A linear scan is right here: the dictionary holds one entry per vector
        // source drawn into this window (single digits), a drop runs once per publish rather than
        // per frame, and a reverse index would have to be unwound on every eviction path to avoid
        // rooting dead sources.
        if (_vectorDrawingCache.Count != 0)
        {
            List<ImageSource>? dependents = null;
            foreach (var kvp in _vectorDrawingCache)
            {
                if (kvp.Value.DependsOn(source))
                {
                    (dependents ??= []).Add(kvp.Key);
                }
            }

            if (dependents is not null)
            {
                foreach (var key in dependents)
                {
                    if (_vectorDrawingCache.TryGetValue(key, out var dependent))
                    {
                        DropVectorRaster(key, dependent);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Removes one vector-raster slot and the GPU upload of the bitmap it produced.
    /// </summary>
    private void DropVectorRaster(ImageSource key, VectorDrawingCacheEntry entry)
    {
        if (entry.RasterizedBitmap != null &&
            _bitmapCache.TryGetValue(entry.RasterizedBitmap, out var rasterEntry))
        {
            RemoveBitmapCacheEntry(entry.RasterizedBitmap, rasterEntry);
        }

        _vectorDrawingCache.Remove(key);
    }

    private static (float RadiusX, float RadiusY) NormalizeRoundedRectRadii(float width, float height, double radiusX, double radiusY)
    {
        static double Sanitize(double radius) =>
            double.IsFinite(radius) && radius > 0 ? radius : 0;

        var halfWidth = Math.Max(0f, width) / 2f;
        var halfHeight = Math.Max(0f, height) / 2f;
        var normalizedRadiusX = (float)Math.Min(Sanitize(radiusX), halfWidth);
        var normalizedRadiusY = (float)Math.Min(Sanitize(radiusY), halfHeight);
        return (normalizedRadiusX, normalizedRadiusY);
    }

    private static (float TL, float TR, float BR, float BL) NormalizePerCornerRadii(
        float width, float height, double tl, double tr, double br, double bl)
    {
        static double Sanitize(double radius) =>
            double.IsFinite(radius) && radius > 0 ? radius : 0;

        var halfMin = Math.Max(0f, Math.Min(width, height)) / 2f;
        return (
            (float)Math.Min(Sanitize(tl), halfMin),
            (float)Math.Min(Sanitize(tr), halfMin),
            (float)Math.Min(Sanitize(br), halfMin),
            (float)Math.Min(Sanitize(bl), halfMin));
    }

    private void FillTransientOverlay(float x, float y, float width, float height, float radiusX, float radiusY,
        float r, float g, float b, float a)
    {
        if (width <= 0 || height <= 0 || a <= 0) return;

        using var brush = _context.CreateSolidBrush(r, g, b, a);
        if (radiusX > 0 || radiusY > 0)
        {
            _renderTarget.FillRoundedRectangle(x, y, width, height, radiusX, radiusY, brush);
        }
        else
        {
            _renderTarget.FillRectangle(x, y, width, height, brush);
        }
    }

    private void DrawSimplifiedBackdropEffect(float x, float y, float width, float height,
        CornerRadius cornerRadius, IBackdropEffect effect)
    {
        var normalizedCornerRadius = cornerRadius.Normalize(width, height);
        float radiusX = (float)Math.Max(normalizedCornerRadius.TopLeft, normalizedCornerRadius.TopRight);
        float radiusY = (float)Math.Max(normalizedCornerRadius.TopLeft, normalizedCornerRadius.BottomLeft);

        uint tintColorArgb = effect.TintColorArgb;
        float overlayAlpha = Math.Clamp(effect.TintOpacity > 0 ? effect.TintOpacity : 0.14f, 0.08f, 0.45f);
        float r = 0.12f;
        float g = 0.12f;
        float b = 0.12f;

        if (tintColorArgb != 0)
        {
            r = ((tintColorArgb >> 16) & 0xFF) / 255f;
            g = ((tintColorArgb >> 8) & 0xFF) / 255f;
            b = (tintColorArgb & 0xFF) / 255f;
        }

        FillTransientOverlay(x, y, width, height, radiusX, radiusY, r, g, b, overlayAlpha);
    }

    private static float SnapCoordinate(double value)
    {
        // Pixel snapping is disabled: render coordinates pass through unchanged.
        // The native renderer does sub-pixel AA, so fractional positions render
        // cleanly. Previously this locked values already sitting on an integer or
        // half-pixel boundary to a hard device-pixel edge; with snapping off they
        // render at their exact position (mild AA at rest, smooth sub-pixel motion
        // when animated).
        return double.IsFinite(value) ? (float)value : 0f;
    }

    /// <inheritdoc />
    public override void DrawLine(Pen pen, Point point0, Point point1)
    {
        if (_closed || pen?.Brush == null) return;

        var brush = GetNativeBrush(pen.Brush);
        if (brush == null) return;

        var x0 = SnapCoordinate(point0.X + Offset.X);
        var y0 = SnapCoordinate(point0.Y + Offset.Y);
        var x1 = SnapCoordinate(point1.X + Offset.X);
        var y1 = SnapCoordinate(point1.Y + Offset.Y);
        var thickness = (float)pen.Thickness;

        // Dashed line: split into segments
        if (pen.DashStyle is { Dashes.Count: > 0 })
        {
            DrawDashedLine(x0, y0, x1, y1, brush, thickness, pen.DashStyle, pen.Thickness);
            return;
        }

        _renderTarget.DrawLine(x0, y0, x1, y1, brush, thickness);
    }

    private void DrawDashedLine(float x0, float y0, float x1, float y1,
        NativeBrush nativeBrush, float thickness, DashStyle dashStyle, double penThickness)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var lineLength = Math.Sqrt(dx * dx + dy * dy);
        if (lineLength < 0.5) return;

        var dashes = dashStyle.Dashes;
        var offset = dashStyle.Offset * penThickness;
        var ux = (float)(dx / lineLength);
        var uy = (float)(dy / lineLength);

        double pos = -offset;
        int dashIndex = 0;
        while (pos < lineLength)
        {
            var dashLen = dashes[dashIndex % dashes.Count] * penThickness;
            var gapLen = dashes[(dashIndex + 1) % dashes.Count] * penThickness;

            var start = Math.Max(0, pos);
            var end = Math.Min(lineLength, pos + dashLen);

            if (end > start)
            {
                _renderTarget.DrawLine(
                    x0 + ux * (float)start, y0 + uy * (float)start,
                    x0 + ux * (float)end, y0 + uy * (float)end,
                    nativeBrush, thickness);
            }

            pos += dashLen + gapLen;
            dashIndex += 2;
        }
    }

    /// <inheritdoc />
    public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
    {
        if (_closed) return;
        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.DRAW_RECT2);

        // Preserve intentional half-pixel alignment for odd-width strokes.
        var x = SnapCoordinate(rectangle.X + Offset.X);
        var y = SnapCoordinate(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;

        // Fill
        if (brush != null && !TryFillRectangleAsImageBrush(brush, x, y, width, height))
        {
            var nativeBrush = GetNativeBrush(brush, x, y, width, height);
            if (nativeBrush != null)
            {
                _renderTarget.FillRectangle(x, y, width, height, nativeBrush);
            }
        }

        // Stroke – snap all four edges so the stroke is uniform on every side.
        // The fill keeps the original width/height to avoid shrinking backgrounds.
        if (pen?.Brush != null)
        {
            var strokeRight = SnapCoordinate(rectangle.X + rectangle.Width + Offset.X);
            var strokeBottom = SnapCoordinate(rectangle.Y + rectangle.Height + Offset.Y);
            var strokeW = strokeRight - x;
            var strokeH = strokeBottom - y;
            var strokeBrush = GetNativeBrush(pen.Brush, x, y, strokeW, strokeH);
            if (strokeBrush != null)
            {
                _renderTarget.DrawRectangle(x, y, strokeW, strokeH, strokeBrush, (float)pen.Thickness);
            }
        }
    }

    /// <inheritdoc />
    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        if (_closed) return;

        // Preserve intentional half-pixel alignment for odd-width strokes.
        var x = SnapCoordinate(rectangle.X + Offset.X);
        var y = SnapCoordinate(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;
        var (rx, ry) = NormalizeRoundedRectRadii(width, height, radiusX, radiusY);

        // Fill
        if (brush != null && !TryFillRoundedRectangleAsImageBrush(brush, x, y, width, height, rx, ry))
        {
            var nativeBrush = GetNativeBrush(brush, x, y, width, height);
            if (nativeBrush != null)
            {
                _renderTarget.FillRoundedRectangle(x, y, width, height, rx, ry, nativeBrush);
            }
        }

        // Stroke – snap all four edges so the stroke is uniform on every side.
        // The fill keeps the original width/height to avoid shrinking backgrounds.
        if (pen?.Brush != null)
        {
            var strokeRight = SnapCoordinate(rectangle.X + rectangle.Width + Offset.X);
            var strokeBottom = SnapCoordinate(rectangle.Y + rectangle.Height + Offset.Y);
            var strokeW = strokeRight - x;
            var strokeH = strokeBottom - y;
            var (strokeRx, strokeRy) = NormalizeRoundedRectRadii(strokeW, strokeH, radiusX, radiusY);
            var strokeBrush = GetNativeBrush(pen.Brush, x, y, strokeW, strokeH);
            if (strokeBrush != null)
            {
                _renderTarget.DrawRoundedRectangle(x, y, strokeW, strokeH, strokeRx, strokeRy, strokeBrush, (float)pen.Thickness);
            }
        }
    }

    /// <summary>
    /// Draws a rounded rectangle with per-corner radii using native SDF rendering.
    /// </summary>
    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius cornerRadius)
    {
        if (_closed) return;

        var x = SnapCoordinate(rectangle.X + Offset.X);
        var y = SnapCoordinate(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;
        var maxR = Math.Min(width, height) / 2f;
        var tl = (float)Math.Min(cornerRadius.TopLeft, maxR);
        var tr = (float)Math.Min(cornerRadius.TopRight, maxR);
        var br = (float)Math.Min(cornerRadius.BottomRight, maxR);
        var bl = (float)Math.Min(cornerRadius.BottomLeft, maxR);

        if (brush != null && !TryFillPerCornerRoundedAsImageBrush(brush, x, y, width, height, tl, tr, br, bl))
        {
            var nativeBrush = GetNativeBrush(brush, x, y, width, height);
            if (nativeBrush != null)
            {
                _renderTarget.FillPerCornerRoundedRectangle(x, y, width, height, tl, tr, br, bl, nativeBrush);
            }
        }

        if (pen?.Brush != null)
        {
            var strokeRight = SnapCoordinate(rectangle.X + rectangle.Width + Offset.X);
            var strokeBottom = SnapCoordinate(rectangle.Y + rectangle.Height + Offset.Y);
            var strokeW = strokeRight - x;
            var strokeH = strokeBottom - y;
            var strokeBrush = GetNativeBrush(pen.Brush, x, y, strokeW, strokeH);
            if (strokeBrush != null)
            {
                _renderTarget.DrawPerCornerRoundedRectangle(x, y, strokeW, strokeH, tl, tr, br, bl, strokeBrush, (float)pen.Thickness);
            }
        }
    }

    /// <inheritdoc />
    public override void DrawContentBorder(Brush? fillBrush, Pen? strokePen, Rect rectangle,
        double bottomLeftRadius, double bottomRightRadius)
    {
        if (_closed) return;

        // Always use managed BezierSegment path (native D2D arc direction is inverted)
        base.DrawContentBorder(fillBrush, strokePen, rectangle, bottomLeftRadius, bottomRightRadius);
    }

    /// <inheritdoc />
    public override void DrawLines(Pen pen, ReadOnlySpan<Point> endpoints)
    {
        if (_closed || pen?.Brush is null || endpoints.Length < 2)
        {
            return;
        }

        // One brush-cache lookup for the whole batch, then a tight loop of
        // native DrawLine calls. Dashed pens fall through to per-segment
        // dashing via DrawDashedLine, which cannot be amortised across
        // segments without compromising the dash phase alignment — the
        // loop still saves N-1 GetNativeBrush hash lookups either way.
        var nativeBrush = GetNativeBrush(pen.Brush);
        if (nativeBrush is null)
        {
            return;
        }

        var thickness = (float)pen.Thickness;
        var dashed = pen.DashStyle is { Dashes.Count: > 0 };
        var pairs = endpoints.Length / 2;

        for (int i = 0; i < pairs; i++)
        {
            var p0 = endpoints[2 * i];
            var p1 = endpoints[2 * i + 1];
            var x0 = SnapCoordinate(p0.X + Offset.X);
            var y0 = SnapCoordinate(p0.Y + Offset.Y);
            var x1 = SnapCoordinate(p1.X + Offset.X);
            var y1 = SnapCoordinate(p1.Y + Offset.Y);

            if (dashed)
            {
                DrawDashedLine(x0, y0, x1, y1, nativeBrush, thickness, pen.DashStyle!, pen.Thickness);
            }
            else
            {
                _renderTarget.DrawLine(x0, y0, x1, y1, nativeBrush, thickness);
            }
        }
    }

    /// <inheritdoc />
    public override void DrawPoints(Brush? brush, ReadOnlySpan<Point> centers, double radius)
    {
        if (_closed || brush is null || centers.IsEmpty || !(radius > 0))
        {
            return;
        }

        // Fast path: the native ellipse batch API already exists and packs
        // N solid-color circles into a single FillEllipseBatch call. Wrap
        // the per-point DrawEllipse loop in a Begin/End scope so each
        // DrawEllipse takes the existing batch-buffer fast path at line 498.
        //
        // The only contract the batch buffer requires is `brush is
        // SolidColorBrush && pen == null` — DrawPoints enforces both by
        // construction. Non-solid brushes fall through the base loop,
        // which still works (one native call per point) but misses the
        // packed path; documented as a caller responsibility.
        if (brush is SolidColorBrush)
        {
            var wasBatching = _isEllipseBatching;
            if (!wasBatching)
            {
                BeginEllipseBatch(centers.Length);
            }
            try
            {
                for (int i = 0; i < centers.Length; i++)
                {
                    DrawEllipse(brush, pen: null, centers[i], radius, radius);
                }
            }
            finally
            {
                if (!wasBatching)
                {
                    EndEllipseBatch();
                }
            }
            return;
        }

        // Non-solid fill: fall back to the default loop. Still correct,
        // just no batch packing.
        base.DrawPoints(brush, centers, radius);
    }

    /// <inheritdoc />
    public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
    {
        if (_closed) return;

        // Pass center through SnapCoordinate so it follows the same rule as all
        // other shapes: snap only when the value already sits on a device-pixel
        // boundary, otherwise let the fractional float through for AA. The old
        // unconditional Math.Round here claimed to "prevent sub-pixel jittering"
        // but actually CAUSED it — spring animations sweeping continuous values
        // through .5 got collapsed to neighbouring integers each frame.
        var cx = SnapCoordinate(center.X + Offset.X);
        var cy = SnapCoordinate(center.Y + Offset.Y);
        var rx = (float)radiusX;
        var ry = (float)radiusY;

        // Fast path: batch filled ellipses with solid color brushes (particle brushes)
        if (_isEllipseBatching && brush is SolidColorBrush solidBrush && pen == null)
        {
            EnsureBatchCapacity();
            var offset = _ellipseBatchCount * 5;
            _ellipseBatchBuffer![offset] = cx;
            _ellipseBatchBuffer[offset + 1] = cy;
            _ellipseBatchBuffer[offset + 2] = rx;
            _ellipseBatchBuffer[offset + 3] = ry;
            // Pack color as RGBA uint32 stored in float bits
            var c = solidBrush.Color;
            uint packed = (uint)c.R | ((uint)c.G << 8) | ((uint)c.B << 16) | ((uint)c.A << 24);
            _ellipseBatchBuffer[offset + 4] = BitConverter.Int32BitsToSingle((int)packed);
            _ellipseBatchCount++;
            return;
        }

        // Bounding box for gradient brush coordinate conversion
        float bx = cx - rx, by = cy - ry, bw = rx * 2, bh = ry * 2;

        // Fill
        if (brush != null && !TryFillEllipseAsImageBrush(brush, cx, cy, rx, ry))
        {
            var nativeBrush = GetNativeBrush(brush, bx, by, bw, bh);
            if (nativeBrush != null)
            {
                _renderTarget.FillEllipse(cx, cy, rx, ry, nativeBrush);
            }
        }

        // Stroke
        if (pen?.Brush != null)
        {
            var strokeBrush = GetNativeBrush(pen.Brush, bx, by, bw, bh);
            if (strokeBrush != null)
            {
                _renderTarget.DrawEllipse(cx, cy, rx, ry, strokeBrush, (float)pen.Thickness);
            }
        }
    }

    private void EnsureBatchCapacity()
    {
        var needed = (_ellipseBatchCount + 1) * 5;
        if (_ellipseBatchBuffer == null || _ellipseBatchBuffer.Length < needed)
        {
            var newSize = Math.Max(needed, (_ellipseBatchBuffer?.Length ?? 256) * 2);
            var newBuffer = new float[newSize];
            if (_ellipseBatchBuffer != null)
                Array.Copy(_ellipseBatchBuffer, newBuffer, _ellipseBatchCount * 5);
            _ellipseBatchBuffer = newBuffer;
        }
    }

    /// <inheritdoc />
    public override void DrawText(FormattedText formattedText, Point origin)
    {
        if (_closed || formattedText == null || string.IsNullOrEmpty(formattedText.Text)) return;
        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.DRAW_TEXT2);

        var mx = origin.X + Offset.X;
        var my = origin.Y + Offset.Y;

        // Gradient foregrounds are mapped onto the text's own box. The old call passed
        // no bounds at all (GetNativeBrush(brush) ⇒ a 0×0 rect), which degenerates a
        // RelativeToBoundingBox gradient — every stop collapses onto the same point, so
        // even a backend that CAN render gradient text has nothing meaningful to sample.
        // Measured extents, not MaxTextWidth/Height: those are the layout CONSTRAINT and
        // are routinely unbounded (the 10000 fallback below), which would stretch the
        // gradient across a box the text occupies a sliver of.
        var brushW = (float)formattedText.Width;
        var brushH = (float)formattedText.Height;
        if (brushW <= 0 || float.IsInfinity(brushW) || float.IsNaN(brushW)) brushW = 0;
        if (brushH <= 0 || float.IsInfinity(brushH) || float.IsNaN(brushH)) brushH = 0;

        // The brush is created per branch below, NOT here: the two call paths hand
        // native DIFFERENT coordinate spaces, and a RelativeToBoundingBox gradient is
        // resolved to absolute coordinates at creation time against the bounds given
        // here. Binding it once up front pins the gradient to the pre-transform box
        // while the scale-compensated branch reports glyph positions in screen space —
        // every glyph then samples the gradient at the wrong place (on a 150% display
        // the sampling coordinate is 1.5× the mapped one, so most glyphs clamp to the
        // last stop and the gradient looks frozen and truncated).
        // Solid brushes ignore the bounds entirely, so this costs the common path nothing.
        if (formattedText.Foreground == null) return;

        var width = (float)formattedText.MaxTextWidth;
        var height = (float)formattedText.MaxTextHeight;
        if (width <= 0 || float.IsInfinity(width) || float.IsNaN(width)) width = 10000;
        if (height <= 0 || float.IsInfinity(height) || float.IsNaN(height)) height = 10000;

        // When a non-translate transform is active, the native renderer applies
        // the transform on the CPU by (a) translating the text origin and
        // (b) scaling each glyph quad's size by the matrix's scale factor. Step
        // (b) stretches an atlas that was rasterized at the original font size,
        // producing a blurry result for any scale != 1. To fix this, we pre-
        // rasterize the glyph atlas at the screen-effective font size, push an
        // inverse matrix so the native matrix cancels out, and hand native the
        // screen-space coordinates — leaving it with an identity transform and
        // glyphs already at their final resolution.
        var nm11 = _currentNativeMatrix[0];
        var nm12 = _currentNativeMatrix[1];
        var nm21 = _currentNativeMatrix[2];
        var nm22 = _currentNativeMatrix[3];
        var ndx = _currentNativeMatrix[4];
        var ndy = _currentNativeMatrix[5];

        bool isIdentity = _nativeTransformDepth <= 0 ||
            (Math.Abs(nm11 - 1.0) < 1e-6 && Math.Abs(nm12) < 1e-6 &&
             Math.Abs(nm21) < 1e-6 && Math.Abs(nm22 - 1.0) < 1e-6 &&
             Math.Abs(ndx) < 1e-6 && Math.Abs(ndy) < 1e-6);

        // Pixel-snap the effective font size (mirrors WPF TextFormattingMode.Display) and
        // degrade heavy weights at sizes where CJK strokes collide (WinUI's gasp-table
        // hinting does the same implicitly). These passes apply to both identity-matrix
        // and scale-compensated paths: under identity the caller's font size is usually
        // already an integer so snapping is a no-op, but the weight degradation matters
        // for small-size bold that's blurry regardless of scale.
        var fontScale = 1.0;
        var preserveNativeScaleDeformation = _nativeTextTransformDepth > 0;
        if (!isIdentity)
        {
            var scaleX = Math.Sqrt(nm11 * nm11 + nm12 * nm12);
            var scaleY = Math.Sqrt(nm21 * nm21 + nm22 * nm22);
            if (scaleX <= 1e-6 || scaleY <= 1e-6) return; // degenerate

            // The screen-resolution compensation below intentionally flattens
            // the active matrix into one font size. That is right for a regular
            // uniform zoom, but it erases axis-aligned non-uniform deformation:
            // liquid-glass drag stretches the border on one axis and compresses
            // it on the other, so text must keep the native matrix just like the
            // other child content does.
            preserveNativeScaleDeformation |= ShouldPreserveNativeTextScaleDeformation(
                nm11, nm12, nm21, nm22, scaleX, scaleY);
            if (!preserveNativeScaleDeformation)
            {
                fontScale = Math.Max(scaleX, scaleY);
            }
        }

        var rawScaledFontSize = formattedText.FontSize * fontScale;
        // Pixel snapping is disabled: the em size is NOT rounded to a whole pixel,
        // so font-size animations scale smoothly. (Cost: fractional sizes create
        // more glyph-atlas entries; the native atlas absorbs this.)
        var effectiveFontSize = Math.Max(1.0, rawScaledFontSize);

        // Weight degradation threshold: 13px is the knee where YaHei Bold strokes
        // start merging on a 1:1 pixel grid. Below that, fall back to Medium/Regular
        // so small CJK labels stay readable instead of turning into dark blobs.
        var effectiveWeight = formattedText.FontWeight;
        if (effectiveFontSize < 13.0 && effectiveWeight >= 500)
        {
            effectiveWeight = 400;
        }

        if (isIdentity || preserveNativeScaleDeformation)
        {
            // Glyph placement policy follows TextOptions.TextFormattingMode, the
            // same split WPF makes:
            //   Ideal   (default) → sub-pixel positioning. The layout's advances are
            //             natural (fractional) DirectWrite metrics, so every glyph
            //             pen lands on a fraction; the native atlas keeps 1/8-px
            //             phases measured from the final screen pen and the run is
            //             drawn with the spacing the font designer intended.
            //   Display           → whole-pixel pen snapping (each glyph pen rounded
            //             to the nearest physical pixel, byte-identical bitmap for
            //             every instance of a character).
            // Display used to be the only behaviour. With natural advances it rounds
            // each pen INDEPENDENTLY, so two neighbouring gaps can be off by up to a
            // whole pixel in opposite directions: in a 14px bold label "Desktop" read
            // as "Desk to p" because the k→t gap rounded wide and s→k / t→o rounded
            // tight. A pixel is a large fraction of a bold letter gap, so the
            // inconsistency is obvious at small UI sizes — the exact case the
            // snapping was supposed to make look crisp. Sub-pixel placement keeps the
            // bitmaps point-sampled (no resampling blur); only where a glyph lands
            // changes, by at most 1/8 px from its true position.
            // JALIUM_TEXT_SUBPIXEL_POSITIONING=0 restores the snapped path for A/B.
            var identitySubpixel = !s_subpixelPositioningDisabled &&
                formattedText.TextFormattingMode == (int)TextFormattingMode.Ideal;
            var format = GetTextFormat(
                formattedText.FontFamily,
                effectiveFontSize,
                effectiveWeight,
                formattedText.FontStyle,
                formattedText.TextRenderingMode,
                formattedText.TextFormattingMode,
                formattedText.TextHintingMode,
                subpixelPositioning: identitySubpixel);
            if (format == null) return;

            var x = (float)mx;
            var y = (float)my; // pixel snapping disabled: the line-box top Y passes through unrounded
            // Native receives the untransformed origin here, so the gradient maps onto
            // the text box in that same space.
            var identityBrush = GetNativeBrush(formattedText.Foreground, x, y, brushW, brushH);
            if (identityBrush == null) return;
            _renderTarget.DrawText(formattedText.Text, format, x, y, width, height, identityBrush);
            return;
        }

        // Under a live scale the run's pens move continuously — every frame of a
        // 1.00→1.03 hover zoom re-lays the text out at a slightly different em
        // size, so each glyph's screen X drifts by a fraction of a pixel. With
        // whole-pixel pen snapping every glyph crosses its own pixel boundary at
        // a different instant and the characters visibly tremble left/right
        // against the smoothly scaling box. Sub-pixel positioning keeps 1/8-px
        // phases measured from the final screen pen instead, so the spacing
        // stays exact while the glyph bitmaps remain crisp. Unlike the identity
        // branch it is requested regardless of TextFormattingMode: a Display-mode
        // label that zooms still has to stay still while it zooms.
        var scaledFormat = GetTextFormat(
            formattedText.FontFamily,
            effectiveFontSize,
            effectiveWeight,
            formattedText.FontStyle,
            formattedText.TextRenderingMode,
            formattedText.TextFormattingMode,
            formattedText.TextHintingMode,
            subpixelPositioning: !s_subpixelPositioningDisabled);
        if (scaledFormat == null) return;

        // Screen-space origin = current matrix applied to (mx, my). The origin
        // is the text layout box's top-left (native backends place the first
        // baseline at y + ascent). Pixel snapping is disabled: Y is NOT rounded
        // so vertical text animation under a scale transform stays smooth.
        var screenX = (float)(nm11 * mx + nm21 * my + ndx);
        var screenY = (float)(nm12 * mx + nm22 * my + ndy);

        // Layout bounding box scales with the actual em size used, so wrap positions
        // stay consistent with the effective glyph metrics.
        var effectiveScale = effectiveFontSize / formattedText.FontSize;
        var scaledWidth = (float)(width * effectiveScale);
        var scaledHeight = (float)(height * effectiveScale);

        // Inverse of the current 2x2 linear part; affine inverse translation is
        // -A^{-1} * t. For a pure scale (m12=m21=0) this is simply the diagonal reciprocal.
        var det = nm11 * nm22 - nm12 * nm21;
        if (Math.Abs(det) < 1e-12) return;
        var invA11 = nm22 / det;
        var invA12 = -nm12 / det;
        var invA21 = -nm21 / det;
        var invA22 = nm11 / det;
        var invDx = -(invA11 * ndx + invA21 * ndy);
        var invDy = -(invA12 * ndx + invA22 * ndy);

        // Native's compose is new_top = old_top * incoming. We pick incoming so that
        // old_top * incoming = Identity, i.e. incoming = old_top^{-1}. After this
        // transient push, the native CPU-side transform does nothing, so the
        // screen-space coords below go directly onto the glyph atlas.
        //
        // The push + DrawText + pop is bundled into a single P/Invoke through
        // DrawTextWithInverseTransform so this fast-path costs one boundary
        // crossing instead of three — material at 800+ DrawText calls/frame.
        Span<float> inverse = stackalloc float[6]
        {
            (float)invA11, (float)invA12,
            (float)invA21, (float)invA22,
            (float)invDx,  (float)invDy
        };
        // Glyph positions below are SCREEN space (the inverse push cancels native's
        // transform), and the box is scaled by the effective em size — so the gradient
        // must be mapped onto that same screen-space box, not the pre-transform one.
        var screenBrush = GetNativeBrush(
            formattedText.Foreground,
            screenX, screenY,
            (float)(brushW * effectiveScale), (float)(brushH * effectiveScale));
        if (screenBrush == null) return;

        _renderTarget.DrawTextWithInverseTransform(
            formattedText.Text, scaledFormat,
            screenX, screenY, scaledWidth, scaledHeight, screenBrush,
            inverse);
    }

    private static bool ShouldPreserveNativeTextScaleDeformation(
        double m11,
        double m12,
        double m21,
        double m22,
        double scaleX,
        double scaleY)
    {
        const double axisAlignmentEpsilon = 1e-6;
        const double scaleDifferenceEpsilon = 0.001;

        return Math.Abs(m12) <= axisAlignmentEpsilon &&
               Math.Abs(m21) <= axisAlignmentEpsilon &&
               Math.Abs(scaleX - scaleY) > scaleDifferenceEpsilon;
    }

    internal void PushNativeTextTransform()
    {
        _nativeTextTransformDepth++;
    }

    internal void PopNativeTextTransform()
    {
        if (_nativeTextTransformDepth > 0)
        {
            _nativeTextTransformDepth--;
        }
    }

    /// <inheritdoc />
    public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
    {
        if (_closed || geometry == null) return;
        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.DRAW_GEO);

        if (_svgDiagActive)
            _svgDrawGeometryCount++;

        // Apply Geometry.Transform if set
        var geometryTransform = geometry.Transform;
        bool pushedTransform = false;
        if (geometryTransform != null && !geometryTransform.Value.IsIdentity)
        {
            PushTransform(geometryTransform);
            pushedTransform = true;
        }

        try
        {
            DrawGeometryCore(brush, pen, geometry);
        }
        finally
        {
            if (pushedTransform)
                Pop();
        }
    }

    private void DrawGeometryCore(Brush? brush, Pen? pen, Geometry geometry)
    {
        // Handle geometry types
        if (geometry is RectangleGeometry rectGeom)
        {
            if (rectGeom.RadiusX > 0 || rectGeom.RadiusY > 0)
            {
                DrawRoundedRectangle(brush, pen, rectGeom.Rect, rectGeom.RadiusX, rectGeom.RadiusY);
            }
            else
            {
                DrawRectangle(brush, pen, rectGeom.Rect);
            }
        }
        else if (geometry is EllipseGeometry ellipseGeom)
        {
            DrawEllipse(brush, pen, ellipseGeom.Center, ellipseGeom.RadiusX, ellipseGeom.RadiusY);
        }
        else if (geometry is LineGeometry lineGeom)
        {
            if (pen != null)
            {
                DrawLine(pen, lineGeom.StartPoint, lineGeom.EndPoint);
            }
        }
        else if (geometry is GeometryGroup group)
        {
            foreach (var child in group.Children)
            {
                DrawGeometry(brush, pen, child);
            }
        }
        else if (geometry is CombinedGeometry combined)
        {
            // Route through the real boolean combiner (Geometry.Combine via
            // GetFlattenedPathGeometry) so the GPU path matches the software backend
            // instead of the old per-mode bounding-box approximations. The flattened
            // result carries FillRule.Nonzero and is filled by the standard path pipeline.
            var flat = combined.GetFlattenedPathGeometry();
            if (!flat.IsEmpty())
                DrawPathGeometry(brush, pen, flat);
        }
        else if (geometry is StreamGeometry streamGeom)
        {
            var inner = streamGeom.GetPathGeometry();
            if (inner != null)
                DrawPathGeometry(brush, pen, inner);
        }
        else if (geometry is PathGeometry pathGeom)
        {
            DrawPathGeometry(brush, pen, pathGeom);
        }
    }

    private static bool FigureHasCurves(PathFigure figure)
    {
        foreach (var segment in figure.Segments)
        {
            if (segment is BezierSegment or QuadraticBezierSegment
                or PolyBezierSegment or PolyQuadraticBezierSegment
                or ArcSegment)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns true if any fill figure's bounding box is contained within
    /// another figure's bounding box — the necessary condition for one figure
    /// to cut a hole in another. When no figure nests inside another, the
    /// figures are disjoint or overlapping siblings with no hole relationship
    /// and must each be filled independently (their union); routing them
    /// through the compound-path triangulator would let its winding-direction
    /// hole heuristic corrupt the fill. Bounding-box containment is a
    /// conservative test: a genuine hole always has its bbox nested, so a real
    /// hole is never mis-routed to separate fills.
    /// </summary>
    private static bool FiguresHaveNesting(IReadOnlyList<PathFigure> figures)
    {
        int n = figures.Count;
        if (n < 2) return false;

        Span<Rect> bounds = n <= 32
            ? stackalloc Rect[n]
            : new Rect[n];
        for (int i = 0; i < n; i++)
            bounds[i] = PathGeometry.GetFigureBounds(figures[i]);

        for (int i = 0; i < n; i++)
        {
            if (bounds[i].IsEmpty) continue;
            for (int j = 0; j < n; j++)
            {
                if (i != j && bounds[j].Contains(bounds[i]))
                    return true;
            }
        }
        return false;
    }

    private void DrawPathGeometry(Brush? brush, Pen? pen, PathGeometry pathGeom)
    {
        // Check if we need managed dashed stroke rendering
        bool hasDash = pen?.DashStyle?.Dashes is { Count: > 0 };
        // Use managed widening only for non-Flat line caps (which the native
        // DrawPolygon cannot render).  LineJoin differences (Miter vs Bevel)
        // are handled natively — the managed widening + FillPolygon path
        // cannot correctly render closed stroke outlines on D3D12 (triangle
        // fan doesn't support concave/ring polygons).
        bool hasNonFlatCaps = pen != null && (
            pen.StartLineCap != PenLineCap.Flat ||
            pen.EndLineCap != PenLineCap.Flat);

        // Compute geometry bounds once for gradient brush coordinate mapping.
        long boundsTickStart = _svgDiagActive ? Stopwatch.GetTimestamp() : 0;
        var frozenInfo = pathGeom.IsFrozen
            ? s_frozenPathGeometryInfo.GetValue(
                pathGeom,
                static geometry => new FrozenPathGeometryInfo(geometry))
            : null;
        var geoBounds = frozenInfo?.Bounds ?? pathGeom.Bounds;
        if (_svgDiagActive)
            _svgBoundsCalcTicks += Stopwatch.GetTimestamp() - boundsTickStart;

        // For fill: use compound path rendering when there are multiple figures
        // (enables proper hole/fill rule handling in the native triangulator)
        IReadOnlyList<PathFigure>? fillFigures = null;
        if (brush != null)
        {
            if (frozenInfo is not null)
            {
                fillFigures = frozenInfo.FillFigures;
            }
            else
            {
                _fillFigureBuffer ??= new List<PathFigure>();
                _fillFigureBuffer.Clear();
                foreach (var figure in pathGeom.Figures)
                {
                    if (figure.IsFilled)
                        _fillFigureBuffer.Add(figure);
                }

                fillFigures = _fillFigureBuffer;
            }
        }

        if (fillFigures != null && fillFigures.Count > 1)
        {
            // The native compound-path triangulator classifies each contour as
            // outer-vs-hole by its winding DIRECTION. That assumption only holds
            // for the "outer CCW + holes CW" convention; two independent solid
            // shapes that happen to wind the same way get one mis-classified as
            // a hole of the other and bridge-subtracted — a corrupted fill.
            //
            // A hole can only exist when one figure NESTS inside another. When
            // no figure nests, the figures are disjoint / overlapping siblings
            // whose correct fill is simply their union — render each one as an
            // independent single-figure fill, which has no cross-figure
            // winding interaction.  Compound fill is reserved for genuinely
            // nested figures (real holes), where it is needed and correct.
            if (frozenInfo?.HasNestedFillFigures ?? FiguresHaveNesting(fillFigures))
            {
                // Send all figures as a single compound path with MoveTo separators
                DrawCompoundPathFill(brush!, fillFigures, pathGeom.FillRule, geoBounds);
            }
            else
            {
                foreach (var figure in fillFigures)
                {
                    if (FigureHasCurves(figure))
                        DrawPathFigureNative(brush, null, figure, pathGeom.FillRule, geoBounds);
                    else
                        DrawPathFigurePolygon(brush, null, figure, pathGeom.FillRule, geoBounds);
                }
            }
        }
        else if (fillFigures != null && fillFigures.Count == 1)
        {
            var figure = fillFigures[0];
            if (FigureHasCurves(figure))
                DrawPathFigureNative(brush, null, figure, pathGeom.FillRule, geoBounds);
            else
                DrawPathFigurePolygon(brush, null, figure, pathGeom.FillRule, geoBounds);
        }

        // Stroke rendering: each figure stroked individually.
        if (pen?.Brush != null)
        {
            foreach (var figure in pathGeom.Figures)
            {
                // Whether the route taken below already renders the pen's line caps.
                // Native StrokePath does (ExpandStrokePath emits round/square caps from
                // the lineCap argument); native DrawPolygon hard-codes butt caps, and the
                // managed dash expansion strokes bare segments — those two need the
                // endpoint cap circles appended manually below. Appending them after
                // StrokePath too would double-paint the cap, which darkens visibly the
                // moment the stroke brush is translucent.
                bool routeRendersCaps;
                if (hasDash && FigureHasCurves(figure))
                {
                    // Route dashed curved paths through native StrokePath (Vello handles dash expansion)
                    DrawPathFigureNative(null, pen, figure, pathGeom.FillRule, geoBounds);
                    routeRendersCaps = true;
                }
                else if (hasDash)
                {
                    // Straight-line dashed paths: managed dash expansion (avoids Vello overhead)
                    DrawDashedPathFigure(pen, figure);
                    routeRendersCaps = false;
                }
                else if (FigureHasCurves(figure))
                {
                    DrawPathFigureNative(null, pen, figure, pathGeom.FillRule, geoBounds);
                    routeRendersCaps = true;
                }
                else
                {
                    DrawPathFigurePolygon(null, pen, figure, pathGeom.FillRule, geoBounds);
                    routeRendersCaps = false;
                }

                // Endpoint cap circles for the routes that cannot render caps natively.
                if (!routeRendersCaps && hasNonFlatCaps && !figure.IsClosed && pen.Brush != null)
                {
                    var capRadius = pen.Thickness / 2;
                    var startPt = figure.StartPoint;
                    var endPt = startPt;
                    // Find the last point in the figure
                    foreach (var seg in figure.Segments)
                    {
                        if (seg is LineSegment ls) endPt = ls.Point;
                        else if (seg is PolyLineSegment pls && pls.Points.Count > 0) endPt = pls.Points[^1];
                        else if (seg is BezierSegment bs) endPt = bs.Point3;
                        else if (seg is PolyBezierSegment pbs && pbs.Points.Count > 0) endPt = pbs.Points[^1];
                        else if (seg is QuadraticBezierSegment qs) endPt = qs.Point2;
                        else if (seg is ArcSegment arcs) endPt = arcs.Point;
                    }
                    if (pen.StartLineCap == PenLineCap.Round)
                        DrawEllipse(pen.Brush, null, startPt, capRadius, capRadius);
                    if (pen.EndLineCap == PenLineCap.Round)
                        DrawEllipse(pen.Brush, null, endPt, capRadius, capRadius);
                }
            }
        }
    }

    /// <summary>
    /// Sends multiple path figures as a single compound path to native FillPath,
    /// using tag 2 (MoveTo) to separate contours.  This enables the native
    /// triangulator to handle holes and fill rules correctly.
    /// </summary>
    private List<PathFigure>? _fillFigureBuffer;

    private void DrawCompoundPathFill(
        Brush brush,
        IReadOnlyList<PathFigure> figures,
        FillRule fillRule,
        Rect geoBounds)
    {
        if (_svgDiagActive)
            _svgDrawCompoundCount++;

        _pathCommandBuffer ??= new List<float>(256);
        _pathCommandBuffer.Clear();
        var cmds = _pathCommandBuffer;
        var ox = Offset.X;
        var oy = Offset.Y;

        // First figure: use the normal startX/startY
        var firstFigure = figures[0];
        float startX = (float)(firstFigure.StartPoint.X + ox);
        float startY = (float)(firstFigure.StartPoint.Y + oy);

        AppendFigureSegments(cmds, firstFigure, firstFigure.StartPoint, ox, oy);
        if (firstFigure.IsClosed) cmds.Add(5f); // ClosePath tag

        // Subsequent figures: use MoveTo (tag 2) to start new contours
        for (int f = 1; f < figures.Count; f++)
        {
            var figure = figures[f];
            cmds.Add(2f); // MoveTo tag
            cmds.Add((float)(figure.StartPoint.X + ox));
            cmds.Add((float)(figure.StartPoint.Y + oy));

            AppendFigureSegments(cmds, figure, figure.StartPoint, ox, oy);
            if (figure.IsClosed) cmds.Add(5f); // ClosePath tag
        }

        if (cmds.Count == 0) return;

        var screenBounds = new Rect(geoBounds.X + ox, geoBounds.Y + oy, geoBounds.Width, geoBounds.Height);
        if (TryFillPathAsImageBrush(brush, screenBounds))
        {
            return;
        }

        var nativeBrush = GetNativeBrush(brush,
            (float)screenBounds.X, (float)screenBounds.Y,
            (float)screenBounds.Width, (float)screenBounds.Height);
        if (nativeBrush != null)
        {
            int rule = fillRule == FillRule.Nonzero ? 1 : 0;
            var commandArray = CopyPathCommands(cmds);
            _renderTarget.FillPath(startX, startY, commandArray, cmds.Count, nativeBrush, rule);
        }
    }

    private void DrawWidenedStroke(Pen pen, PathFigure figure, FillRule fillRule)
    {
        // Build a single-figure PathGeometry, widen it, then fill the result
        var singleGeom = new PathGeometry { FillRule = fillRule };
        singleGeom.Figures.Add(figure);

        var widened = singleGeom.GetWidenedPathGeometry(pen);
        if (widened.Figures.Count == 0) return;

        var wBounds = widened.Bounds;
        var strokeBrush = pen.Brush;
        foreach (var wFigure in widened.Figures)
        {
            DrawPathFigurePolygon(strokeBrush, null, wFigure, FillRule.Nonzero, wBounds);
        }
    }

    private void DrawDashedPathFigure(Pen pen, PathFigure figure)
    {
        // Flatten the figure to get all points
        var points = new List<Point> { figure.StartPoint };
        var currentPoint = figure.StartPoint;
        foreach (var segment in figure.Segments)
        {
            switch (segment)
            {
                case LineSegment ls:
                    points.Add(ls.Point);
                    currentPoint = ls.Point;
                    break;
                case PolyLineSegment pls:
                    points.AddRange(pls.Points);
                    if (pls.Points.Count > 0) currentPoint = pls.Points[^1];
                    break;
                case BezierSegment bez:
                    points.AddRange(GetBezierPoints(currentPoint, bez.Point1, bez.Point2, bez.Point3));
                    currentPoint = bez.Point3;
                    break;
                case PolyBezierSegment pbez:
                    var bpts = pbez.Points;
                    for (int i = 0; i + 2 < bpts.Count; i += 3)
                    {
                        points.AddRange(GetBezierPoints(currentPoint, bpts[i], bpts[i + 1], bpts[i + 2]));
                        currentPoint = bpts[i + 2];
                    }
                    break;
                case QuadraticBezierSegment q:
                    points.AddRange(GetQuadBezierPoints(currentPoint, q.Point1, q.Point2));
                    currentPoint = q.Point2;
                    break;
                case PolyQuadraticBezierSegment pq:
                    var qpts = pq.Points;
                    for (int i = 0; i + 1 < qpts.Count; i += 2)
                    {
                        points.AddRange(GetQuadBezierPoints(currentPoint, qpts[i], qpts[i + 1]));
                        currentPoint = qpts[i + 1];
                    }
                    break;
                case ArcSegment arc:
                    points.AddRange(GetArcPoints(currentPoint, arc));
                    currentPoint = arc.Point;
                    break;
            }
        }

        if (figure.IsClosed && points.Count > 1)
        {
            var first = points[0];
            var last = points[^1];
            if (Math.Abs(first.X - last.X) > 1e-10 || Math.Abs(first.Y - last.Y) > 1e-10)
                points.Add(first);
        }

        if (points.Count < 2) return;

        // Compute cumulative distances
        var dashes = pen.DashStyle!.Dashes;
        var dashOffset = pen.DashStyle.Offset * pen.Thickness;
        if (dashes.Count == 0) return;

        // Build the dash pattern in absolute units
        var pattern = new double[dashes.Count];
        double patternLength = 0;
        for (int i = 0; i < dashes.Count; i++)
        {
            pattern[i] = dashes[i] * pen.Thickness;
            patternLength += pattern[i];
        }
        if (patternLength <= 0) return;

        // Walk along the polyline, emitting dashed sub-segments
        if (pen.Brush == null) return;
        var strokeBrush = GetNativeBrush(pen.Brush);
        if (strokeBrush == null) return;

        int dashIndex = 0;
        bool drawing = true; // true = dash (visible), false = gap
        double remaining = pattern[0];

        // Apply dash offset
        double offset = dashOffset % patternLength;
        if (offset < 0) offset += patternLength;
        while (offset > 0)
        {
            if (offset >= remaining)
            {
                offset -= remaining;
                dashIndex = (dashIndex + 1) % pattern.Length;
                drawing = !drawing;
                remaining = pattern[dashIndex];
            }
            else
            {
                remaining -= offset;
                offset = 0;
            }
        }

        var dashStart = points[0];
        int ptIndex = 0;

        while (ptIndex < points.Count - 1)
        {
            var segStart = points[ptIndex];
            var segEnd = points[ptIndex + 1];
            var segDx = segEnd.X - segStart.X;
            var segDy = segEnd.Y - segStart.Y;
            var segLen = Math.Sqrt(segDx * segDx + segDy * segDy);

            if (segLen < 1e-10)
            {
                ptIndex++;
                continue;
            }

            double consumed = 0;
            while (consumed < segLen - 1e-10)
            {
                var available = segLen - consumed;
                if (remaining <= available)
                {
                    // Finish this dash/gap segment
                    var t = (consumed + remaining) / segLen;
                    var endPt = new Point(
                        segStart.X + segDx * t,
                        segStart.Y + segDy * t);

                    if (drawing)
                    {
                        // Emit stroke from dashStart to endPt
                        EmitStrokeLine(dashStart, endPt, strokeBrush, (float)pen.Thickness);
                    }

                    consumed += remaining;
                    dashStart = endPt;
                    dashIndex = (dashIndex + 1) % pattern.Length;
                    drawing = !drawing;
                    remaining = pattern[dashIndex];
                }
                else
                {
                    // This segment ends before the current dash/gap completes
                    remaining -= available;
                    if (drawing)
                    {
                        // dashStart to segEnd is part of a visible dash; don't emit yet
                    }
                    consumed = segLen;
                }
            }

            ptIndex++;
            if (ptIndex < points.Count && !drawing)
            {
                // In a gap, update dashStart to next point
            }
            else if (ptIndex < points.Count && drawing)
            {
                // Continuing a dash into the next segment, dashStart stays
            }
        }

        // Emit final dash segment if we're still drawing
        if (drawing && ptIndex > 0)
        {
            var lastPt = points[^1];
            if (Math.Abs(dashStart.X - lastPt.X) > 1e-10 || Math.Abs(dashStart.Y - lastPt.Y) > 1e-10)
            {
                EmitStrokeLine(dashStart, lastPt, strokeBrush, (float)pen.Thickness);
            }
        }
    }

    private void EmitStrokeLine(Point from, Point to, NativeBrush brush, float strokeWidth)
    {
        var ox = Offset.X;
        var oy = Offset.Y;
        _renderTarget.DrawLine(
            (float)(from.X + ox), (float)(from.Y + oy),
            (float)(to.X + ox), (float)(to.Y + oy),
            brush, strokeWidth);
    }

    /// <summary>
    /// Promotes a quadratic bezier to cubic and appends the cubic command.
    /// cp1 = start + 2/3*(ctrl - start), cp2 = end + 2/3*(ctrl - end)
    /// </summary>
    private static void AppendQuadAsCubic(List<float> cmds, Point start, Point ctrl, Point end, double ox, double oy)
    {
        var cp1X = start.X + 2.0 / 3.0 * (ctrl.X - start.X);
        var cp1Y = start.Y + 2.0 / 3.0 * (ctrl.Y - start.Y);
        var cp2X = end.X + 2.0 / 3.0 * (ctrl.X - end.X);
        var cp2Y = end.Y + 2.0 / 3.0 * (ctrl.Y - end.Y);

        cmds.Add(1f);
        cmds.Add((float)(cp1X + ox));
        cmds.Add((float)(cp1Y + oy));
        cmds.Add((float)(cp2X + ox));
        cmds.Add((float)(cp2Y + oy));
        cmds.Add((float)(end.X + ox));
        cmds.Add((float)(end.Y + oy));
    }

    /// <summary>
    /// Converts an SVG-style arc to cubic bezier curves appended to the command buffer.
    /// Uses the standard endpoint-to-center parameterization, then approximates each
    /// arc segment (≤ π/2) with a single cubic bezier.
    /// </summary>
    private static void AppendArcAsCubicBeziers(List<float> cmds, Point start, ArcSegment arc, double ox, double oy)
    {
        var end = arc.Point;
        var rx = arc.Size.Width;
        var ry = arc.Size.Height;

        // Handle degenerate cases
        if (rx == 0 || ry == 0 || (start.X == end.X && start.Y == end.Y))
        {
            cmds.Add(0f);
            cmds.Add((float)(end.X + ox));
            cmds.Add((float)(end.Y + oy));
            return;
        }

        // Convert endpoint parameterization to center parameterization (SVG spec F.6.5-F.6.6)
        var rotAngle = arc.RotationAngle * Math.PI / 180.0;
        var cosA = Math.Cos(rotAngle);
        var sinA = Math.Sin(rotAngle);

        var dx2 = (start.X - end.X) / 2.0;
        var dy2 = (start.Y - end.Y) / 2.0;
        var x1p = cosA * dx2 + sinA * dy2;
        var y1p = -sinA * dx2 + cosA * dy2;

        // Ensure radii are large enough
        var x1pSq = x1p * x1p;
        var y1pSq = y1p * y1p;
        var rxSq = rx * rx;
        var rySq = ry * ry;
        var lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1)
        {
            var sqrtLam = Math.Sqrt(lambda);
            rx *= sqrtLam;
            ry *= sqrtLam;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        // Calculate center point
        var sign = (arc.IsLargeArc != (arc.SweepDirection == SweepDirection.Clockwise)) ? 1.0 : -1.0;
        var sq = Math.Max(0, (rxSq * rySq - rxSq * y1pSq - rySq * x1pSq) / (rxSq * y1pSq + rySq * x1pSq));
        var coef = sign * Math.Sqrt(sq);
        var cxp = coef * rx * y1p / ry;
        var cyp = -coef * ry * x1p / rx;
        var cx = cosA * cxp - sinA * cyp + (start.X + end.X) / 2.0;
        var cy = sinA * cxp + cosA * cyp + (start.Y + end.Y) / 2.0;

        // Calculate start and sweep angles
        var startAngle = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var endAngle = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
        var deltaAngle = endAngle - startAngle;

        if (arc.SweepDirection == SweepDirection.Clockwise && deltaAngle < 0)
            deltaAngle += 2 * Math.PI;
        else if (arc.SweepDirection == SweepDirection.Counterclockwise && deltaAngle > 0)
            deltaAngle -= 2 * Math.PI;

        // Split into segments of at most π/2 and approximate each with a cubic bezier
        int segCount = (int)Math.Ceiling(Math.Abs(deltaAngle) / (Math.PI / 2.0));
        segCount = Math.Max(1, segCount);
        var segAngle = deltaAngle / segCount;

        for (int i = 0; i < segCount; i++)
        {
            var a1 = startAngle + segAngle * i;
            var a2 = a1 + segAngle;

            // Cubic bezier approximation of a unit circle arc from a1 to a2:
            // alpha = sin(da) * (sqrt(4 + 3*tan(da/2)^2) - 1) / 3
            var da = a2 - a1;
            var halfTan = Math.Tan(da / 2.0);
            var alpha = Math.Sin(da) * (Math.Sqrt(4 + 3 * halfTan * halfTan) - 1) / 3.0;

            var cos1 = Math.Cos(a1);
            var sin1 = Math.Sin(a1);
            var cos2 = Math.Cos(a2);
            var sin2 = Math.Sin(a2);

            // Points on the unit ellipse (before rotation/translation)
            var ep1x = rx * cos1;
            var ep1y = ry * sin1;
            var ep2x = rx * cos2;
            var ep2y = ry * sin2;

            // Control point tangent directions
            var d1x = -rx * sin1;
            var d1y = ry * cos1;
            var d2x = -rx * sin2;
            var d2y = ry * cos2;

            var cp1x = ep1x + alpha * d1x;
            var cp1y = ep1y + alpha * d1y;
            var cp2x = ep2x - alpha * d2x;
            var cp2y = ep2y - alpha * d2y;

            // Apply rotation and translation
            var fcp1x = cosA * cp1x - sinA * cp1y + cx;
            var fcp1y = sinA * cp1x + cosA * cp1y + cy;
            var fcp2x = cosA * cp2x - sinA * cp2y + cx;
            var fcp2y = sinA * cp2x + cosA * cp2y + cy;
            var fep2x = cosA * ep2x - sinA * ep2y + cx;
            var fep2y = sinA * ep2x + cosA * ep2y + cy;

            cmds.Add(1f); // BezierTo
            cmds.Add((float)(fcp1x + ox));
            cmds.Add((float)(fcp1y + oy));
            cmds.Add((float)(fcp2x + ox));
            cmds.Add((float)(fcp2y + oy));
            cmds.Add((float)(fep2x + ox));
            cmds.Add((float)(fep2y + oy));
        }
    }

    /// <summary>
    /// Appends all segments of a PathFigure to the command buffer.
    /// Used by both single-figure and compound-path rendering.
    /// </summary>
    private static Point AppendFigureSegments(List<float> cmds, PathFigure figure, Point currentPoint, double ox, double oy)
    {
        foreach (var segment in figure.Segments)
        {
            if (segment is LineSegment lineSeg)
            {
                cmds.Add(0f);
                cmds.Add((float)(lineSeg.Point.X + ox));
                cmds.Add((float)(lineSeg.Point.Y + oy));
                currentPoint = lineSeg.Point;
            }
            else if (segment is PolyLineSegment polyLine)
            {
                foreach (var pt in polyLine.Points)
                {
                    cmds.Add(0f);
                    cmds.Add((float)(pt.X + ox));
                    cmds.Add((float)(pt.Y + oy));
                    currentPoint = pt;
                }
            }
            else if (segment is BezierSegment bezier)
            {
                cmds.Add(1f);
                cmds.Add((float)(bezier.Point1.X + ox));
                cmds.Add((float)(bezier.Point1.Y + oy));
                cmds.Add((float)(bezier.Point2.X + ox));
                cmds.Add((float)(bezier.Point2.Y + oy));
                cmds.Add((float)(bezier.Point3.X + ox));
                cmds.Add((float)(bezier.Point3.Y + oy));
                currentPoint = bezier.Point3;
            }
            else if (segment is PolyBezierSegment polyBezier)
            {
                var pts = polyBezier.Points;
                for (int i = 0; i + 2 < pts.Count; i += 3)
                {
                    cmds.Add(1f);
                    cmds.Add((float)(pts[i].X + ox));
                    cmds.Add((float)(pts[i].Y + oy));
                    cmds.Add((float)(pts[i + 1].X + ox));
                    cmds.Add((float)(pts[i + 1].Y + oy));
                    cmds.Add((float)(pts[i + 2].X + ox));
                    cmds.Add((float)(pts[i + 2].Y + oy));
                    currentPoint = pts[i + 2];
                }
            }
            else if (segment is QuadraticBezierSegment quad)
            {
                // Native QuadTo tag 3: [3, cpx, cpy, ex, ey]
                cmds.Add(3f);
                cmds.Add((float)(quad.Point1.X + ox));
                cmds.Add((float)(quad.Point1.Y + oy));
                cmds.Add((float)(quad.Point2.X + ox));
                cmds.Add((float)(quad.Point2.Y + oy));
                currentPoint = quad.Point2;
            }
            else if (segment is PolyQuadraticBezierSegment polyQuad)
            {
                var pts = polyQuad.Points;
                for (int i = 0; i + 1 < pts.Count; i += 2)
                {
                    // Native QuadTo tag 3: [3, cpx, cpy, ex, ey]
                    cmds.Add(3f);
                    cmds.Add((float)(pts[i].X + ox));
                    cmds.Add((float)(pts[i].Y + oy));
                    cmds.Add((float)(pts[i + 1].X + ox));
                    cmds.Add((float)(pts[i + 1].Y + oy));
                    currentPoint = pts[i + 1];
                }
            }
            else if (segment is ArcSegment arc)
            {
                // Convert arc to cubic bezier curves that native can render (tag 1).
                // Native backends don't support raw arc commands.
                AppendArcAsCubicBeziers(cmds, currentPoint, arc, ox, oy);
                currentPoint = arc.Point;
            }
        }
        return currentPoint;
    }

    /// <summary>
    /// Renders a path figure using the native FillPath/StrokePath API with real bezier curves.
    /// </summary>
    // Reusable command buffer for path rendering to reduce GC pressure.
    private List<float>? _pathCommandBuffer;
    private float[]? _pathCommandArray;

    private float[] CopyPathCommands(List<float> commands)
    {
        var required = commands.Count;
        if (_pathCommandArray is null || _pathCommandArray.Length < required)
        {
            var capacity = 128;
            while (capacity < required)
            {
                capacity = checked(capacity * 2);
            }

            _pathCommandArray = new float[capacity];
        }

        commands.CopyTo(_pathCommandArray, 0);
        return _pathCommandArray;
    }

    private void DrawPathFigureNative(Brush? brush, Pen? pen, PathFigure figure, FillRule fillRule, Rect geoBounds)
    {
        if (_svgDiagActive)
            _svgDrawPathNativeCount++;

        // Build command buffer: tag 0 = LineTo [0,x,y], tag 1 = BezierTo [1,cp1x,cp1y,cp2x,cp2y,ex,ey]
        long pathBuildStart = _svgDiagActive ? Stopwatch.GetTimestamp() : 0;

        _pathCommandBuffer ??= new List<float>(128);
        _pathCommandBuffer.Clear();
        var cmds = _pathCommandBuffer;
        var ox = Offset.X;
        var oy = Offset.Y;

        AppendFigureSegments(cmds, figure, figure.StartPoint, ox, oy);

        if (cmds.Count == 0) return;

        // A closed figure's final edge (last point back to StartPoint) exists
        // only as the ClosePath tag — AppendFigureSegments emits segments, not
        // the wrap. Without it the native flattener produced an open contour
        // and the stroker drew every side of the outline except the closing
        // one (the "warehouse icon has no right wall" bug). The multi-figure
        // fill path already emits this tag; the single-figure path did not.
        if (figure.IsClosed) cmds.Add(5f); // ClosePath tag

        float startX = (float)(figure.StartPoint.X + ox);
        float startY = (float)(figure.StartPoint.Y + oy);
        float bx = (float)(geoBounds.X + ox), by = (float)(geoBounds.Y + oy);
        float bw = (float)geoBounds.Width, bh = (float)geoBounds.Height;
        var cmdArray = CopyPathCommands(cmds);
        var commandCount = cmds.Count;

        if (_svgDiagActive)
            _svgPathBuildTicks += Stopwatch.GetTimestamp() - pathBuildStart;

        if (brush != null && figure.IsFilled)
        {
            if (brush is ImageBrush)
            {
                TryFillPathAsImageBrush(brush, new Rect(bx, by, bw, bh));
            }
            else
            {
                long brushStart = _svgDiagActive ? Stopwatch.GetTimestamp() : 0;
                var nativeBrush = GetNativeBrush(brush, bx, by, bw, bh);
                if (_svgDiagActive)
                    _svgGetBrushTicks += Stopwatch.GetTimestamp() - brushStart;

                if (nativeBrush != null)
                {
                    int rule = fillRule == FillRule.Nonzero ? 1 : 0;
                    long nativeStart = _svgDiagActive ? Stopwatch.GetTimestamp() : 0;
                    _renderTarget.FillPath(startX, startY, cmdArray, commandCount, nativeBrush, rule);
                    if (_svgDiagActive)
                        _svgNativeCallTicks += Stopwatch.GetTimestamp() - nativeStart;
                }
            }
        }

        if (pen?.Brush != null)
        {
            long brushStart = _svgDiagActive ? Stopwatch.GetTimestamp() : 0;
            var strokeBrush = GetNativeBrush(pen.Brush, bx, by, bw, bh);
            if (_svgDiagActive)
                _svgGetBrushTicks += Stopwatch.GetTimestamp() - brushStart;

            if (strokeBrush != null)
            {
                int nativeLineCap = pen.StartLineCap switch
                {
                    PenLineCap.Round => 2,    // kLineCapRound
                    PenLineCap.Square => 1,   // kLineCapSquare
                    _ => 0                    // kLineCapButt (Flat, Triangle)
                };
                // Marshal dash pattern if present
                float[]? dashArray = null;
                float dashOff = 0f;
                if (pen.DashStyle?.Dashes is { Count: > 0 } dashes)
                {
                    dashArray = new float[dashes.Count];
                    for (int di = 0; di < dashes.Count; di++)
                        dashArray[di] = (float)(dashes[di] * pen.Thickness);
                    dashOff = (float)(pen.DashStyle.Offset * pen.Thickness);
                }
                long nativeStart = _svgDiagActive ? Stopwatch.GetTimestamp() : 0;
                _renderTarget.StrokePath(startX, startY, cmdArray, commandCount, strokeBrush, (float)pen.Thickness, figure.IsClosed, (int)pen.LineJoin, (float)pen.MiterLimit, nativeLineCap, dashArray, dashOff);
                if (_svgDiagActive)
                    _svgNativeCallTicks += Stopwatch.GetTimestamp() - nativeStart;
            }
        }
    }

    /// <summary>
    /// Renders a path figure as a polygon (all segments flattened to line points).
    /// </summary>
    // Reusable point buffer for polygon flattening to reduce GC pressure.
    private List<Point>? _polygonPointBuffer;

    private void DrawPathFigurePolygon(Brush? brush, Pen? pen, PathFigure figure, FillRule fillRule, Rect geoBounds)
    {
        if (_svgDiagActive)
            _svgDrawPathPolygonCount++;

        _polygonPointBuffer ??= new List<Point>(64);
        _polygonPointBuffer.Clear();
        var points = _polygonPointBuffer;
        points.Add(figure.StartPoint);
        var currentPoint = figure.StartPoint;
        bool hasCurvedSegments = false;

        foreach (var segment in figure.Segments)
        {
            if (segment is LineSegment lineSeg)
            {
                points.Add(lineSeg.Point);
                currentPoint = lineSeg.Point;
            }
            else if (segment is PolyLineSegment polyLine)
            {
                foreach (var point in polyLine.Points)
                {
                    points.Add(point);
                    currentPoint = point;
                }
            }
            else if (segment is ArcSegment arc)
            {
                hasCurvedSegments = true;
                var arcPoints = GetArcPoints(currentPoint, arc);
                points.AddRange(arcPoints);
                currentPoint = arc.Point;
            }
            else if (segment is BezierSegment bezier)
            {
                hasCurvedSegments = true;
                var bezierPoints = GetBezierPoints(currentPoint, bezier.Point1, bezier.Point2, bezier.Point3);
                points.AddRange(bezierPoints);
                currentPoint = bezier.Point3;
            }
            else if (segment is PolyBezierSegment polyBezier)
            {
                hasCurvedSegments = true;
                var pts = polyBezier.Points;
                for (int i = 0; i + 2 < pts.Count; i += 3)
                {
                    var bezierPoints = GetBezierPoints(currentPoint, pts[i], pts[i + 1], pts[i + 2]);
                    points.AddRange(bezierPoints);
                    currentPoint = pts[i + 2];
                }
            }
            else if (segment is QuadraticBezierSegment quad)
            {
                hasCurvedSegments = true;
                var quadPoints = GetQuadBezierPoints(currentPoint, quad.Point1, quad.Point2);
                points.AddRange(quadPoints);
                currentPoint = quad.Point2;
            }
            else if (segment is PolyQuadraticBezierSegment polyQuad)
            {
                hasCurvedSegments = true;
                var pts = polyQuad.Points;
                for (int i = 0; i + 1 < pts.Count; i += 2)
                {
                    var quadPoints = GetQuadBezierPoints(currentPoint, pts[i], pts[i + 1]);
                    points.AddRange(quadPoints);
                    currentPoint = pts[i + 1];
                }
            }
        }

        // The native DrawPolygon already adds a 0.5 offset for odd-pixel strokes
        // to align to pixel centers.  The managed side must therefore snap to the
        // nearest *integer* so the combined result lands on half-pixel → crisp 1px.
        // Using SnapCoordinate (which preserves half-pixel values) would cause a
        // double offset: 0.5 (snap) + 0.5 (native) = 1.0 → integer position →
        // the stroke spans two pixel rows and appears ~2px thick.
        //
        // For paths that contain diagonal segments we skip snapping entirely so
        // that the native 0.5 shift is a uniform translation (no visual impact on
        // thickness) and anti-aliased diagonals render at their natural weight.
        bool isAxisAligned = !hasCurvedSegments && IsAxisAlignedPath(points);

        var pointArray = new float[points.Count * 2];
        if (isAxisAligned && points.Count > 0)
        {
            // Snap the first point to the nearest integer, then apply the
            // same fractional offset to all subsequent points.  This preserves
            // relative distances (lengths) between points while still aligning
            // the path to the pixel grid for crisp rendering.
            var baseX = points[0].X + Offset.X;
            var baseY = points[0].Y + Offset.Y;
            var snapDx = Math.Round(baseX) - baseX;
            var snapDy = Math.Round(baseY) - baseY;

            for (int i = 0; i < points.Count; i++)
            {
                pointArray[i * 2] = (float)(points[i].X + Offset.X + snapDx);
                pointArray[i * 2 + 1] = (float)(points[i].Y + Offset.Y + snapDy);
            }
        }
        else
        {
            for (int i = 0; i < points.Count; i++)
            {
                pointArray[i * 2] = (float)(points[i].X + Offset.X);
                pointArray[i * 2 + 1] = (float)(points[i].Y + Offset.Y);
            }
        }

        var ox = Offset.X;
        var oy = Offset.Y;
        float bx = (float)(geoBounds.X + ox), by = (float)(geoBounds.Y + oy);
        float bw = (float)geoBounds.Width, bh = (float)geoBounds.Height;

        if (brush != null && figure.IsFilled && points.Count >= 3)
        {
            if (brush is ImageBrush)
            {
                TryFillPathAsImageBrush(brush, new Rect(bx, by, bw, bh));
            }
            else
            {
                int rule = fillRule == FillRule.Nonzero ? 1 : 0;
                var nativeBrush = GetNativeBrush(brush, bx, by, bw, bh);
                if (nativeBrush != null)
                {
                    _renderTarget.FillPolygon(pointArray, nativeBrush, rule);
                }
            }
        }

        if (pen?.Brush != null && points.Count >= 2)
        {
            var strokeBrush = GetNativeBrush(pen.Brush, bx, by, bw, bh);
            if (strokeBrush != null)
            {
                _renderTarget.DrawPolygon(pointArray, strokeBrush, (float)pen.Thickness, figure.IsClosed, (int)pen.LineJoin, (float)pen.MiterLimit);
            }
        }
    }

    /// <summary>
    /// Checks whether all consecutive point pairs form axis-aligned (horizontal or vertical) segments.
    /// </summary>
    private static bool IsAxisAlignedPath(List<Point> points)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            var p1 = points[i];
            var p2 = points[i + 1];
            // Segment is axis-aligned if either X or Y is the same
            if (Math.Abs(p1.X - p2.X) > 0.001 && Math.Abs(p1.Y - p2.Y) > 0.001)
                return false;
        }
        return true;
    }

    private const double FlatteningTolerance = 0.25;

    // Bitmap alpha contract: the framework hands STRAIGHT (non-premultiplied)
    // BGRA8 to the native bitmap-upload ABI on every backend, and each backend
    // premultiplies internally where its blend requires (D3D12 on upload, Vulkan
    // while packing replay staging, software blends straight). There is
    // deliberately no managed per-backend premultiply predicate here — that
    // would make the upper layer aware of which backend is active, the exact
    // coupling this contract removes.

    private List<Point> GetBezierPoints(Point p0, Point p1, Point p2, Point p3)
    {
        var points = new List<Point>();
        FlattenCubicBezier(points, p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y, 0);
        return points;
    }

    private static void FlattenCubicBezier(List<Point> points,
        double x0, double y0, double x1, double y1,
        double x2, double y2, double x3, double y3, int depth)
    {
        if (depth > 10)
        {
            points.Add(new Point(x3, y3));
            return;
        }

        // Flatness test: distance of control points from the chord
        double dx = x3 - x0, dy = y3 - y0;
        double len2 = dx * dx + dy * dy;
        double d1, d2;
        if (len2 < 1e-10)
        {
            d1 = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            d2 = Math.Sqrt((x2 - x0) * (x2 - x0) + (y2 - y0) * (y2 - y0));
        }
        else
        {
            double invLen = 1.0 / Math.Sqrt(len2);
            double nx = -dy * invLen, ny = dx * invLen;
            d1 = Math.Abs(nx * (x1 - x0) + ny * (y1 - y0));
            d2 = Math.Abs(nx * (x2 - x0) + ny * (y2 - y0));
        }

        if (d1 + d2 <= FlatteningTolerance)
        {
            points.Add(new Point(x3, y3));
            return;
        }

        // De Casteljau subdivision at t=0.5
        double m01x = (x0 + x1) * 0.5, m01y = (y0 + y1) * 0.5;
        double m12x = (x1 + x2) * 0.5, m12y = (y1 + y2) * 0.5;
        double m23x = (x2 + x3) * 0.5, m23y = (y2 + y3) * 0.5;
        double m012x = (m01x + m12x) * 0.5, m012y = (m01y + m12y) * 0.5;
        double m123x = (m12x + m23x) * 0.5, m123y = (m12y + m23y) * 0.5;
        double mx = (m012x + m123x) * 0.5, my = (m012y + m123y) * 0.5;

        FlattenCubicBezier(points, x0, y0, m01x, m01y, m012x, m012y, mx, my, depth + 1);
        FlattenCubicBezier(points, mx, my, m123x, m123y, m23x, m23y, x3, y3, depth + 1);
    }

    private List<Point> GetQuadBezierPoints(Point p0, Point p1, Point p2)
    {
        // Promote to cubic: cp1 = p0 + 2/3*(p1-p0), cp2 = p2 + 2/3*(p1-p2)
        var cp1x = p0.X + 2.0 / 3.0 * (p1.X - p0.X);
        var cp1y = p0.Y + 2.0 / 3.0 * (p1.Y - p0.Y);
        var cp2x = p2.X + 2.0 / 3.0 * (p1.X - p2.X);
        var cp2y = p2.Y + 2.0 / 3.0 * (p1.Y - p2.Y);
        return GetBezierPoints(p0, new Point(cp1x, cp1y), new Point(cp2x, cp2y), p2);
    }

    private List<Point> GetArcPoints(Point start, ArcSegment arc)
    {
        var points = new List<Point>();
        var end = arc.Point;
        var rx = arc.Size.Width;
        var ry = arc.Size.Height;

        // Handle degenerate cases
        if (rx == 0 || ry == 0 || (start.X == end.X && start.Y == end.Y))
        {
            points.Add(end);
            return points;
        }

        // Convert endpoint parameterization to center parameterization
        // Based on SVG arc implementation algorithm
        var dx = (start.X - end.X) / 2;
        var dy = (start.Y - end.Y) / 2;

        var rotationAngle = arc.RotationAngle * Math.PI / 180;
        var cosAngle = Math.Cos(rotationAngle);
        var sinAngle = Math.Sin(rotationAngle);

        var x1p = cosAngle * dx + sinAngle * dy;
        var y1p = -sinAngle * dx + cosAngle * dy;

        // Ensure radii are large enough
        var x1pSq = x1p * x1p;
        var y1pSq = y1p * y1p;
        var rxSq = rx * rx;
        var rySq = ry * ry;

        var lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1)
        {
            var sqrtLambda = Math.Sqrt(lambda);
            rx *= sqrtLambda;
            ry *= sqrtLambda;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        // Calculate center point
        // Per SVG spec: sign is positive when fA != fS
        var sign = (arc.IsLargeArc != (arc.SweepDirection == SweepDirection.Clockwise)) ? 1 : -1;
        var sq = Math.Max(0, (rxSq * rySq - rxSq * y1pSq - rySq * x1pSq) / (rxSq * y1pSq + rySq * x1pSq));
        var coef = sign * Math.Sqrt(sq);

        var cxp = coef * rx * y1p / ry;
        var cyp = -coef * ry * x1p / rx;

        var cx = cosAngle * cxp - sinAngle * cyp + (start.X + end.X) / 2;
        var cy = sinAngle * cxp + cosAngle * cyp + (start.Y + end.Y) / 2;

        // Calculate start and end angles
        var startAngle = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var endAngle = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);

        var deltaAngle = endAngle - startAngle;

        // Adjust delta angle based on sweep direction
        if (arc.SweepDirection == SweepDirection.Clockwise && deltaAngle < 0)
            deltaAngle += 2 * Math.PI;
        else if (arc.SweepDirection == SweepDirection.Counterclockwise && deltaAngle > 0)
            deltaAngle -= 2 * Math.PI;

        // Adaptive segment count based on arc size and sweep angle
        var circumference = Math.Abs(deltaAngle) * Math.Max(rx, ry);
        var segments = Math.Clamp((int)(circumference / FlatteningTolerance), 4, 256);
        for (int i = 1; i <= segments; i++)
        {
            var t = i / (double)segments;
            var angle = startAngle + deltaAngle * t;

            var px = rx * Math.Cos(angle);
            var py = ry * Math.Sin(angle);

            var x = cosAngle * px - sinAngle * py + cx;
            var y = sinAngle * px + cosAngle * py + cy;

            points.Add(new Point(x, y));
        }

        return points;
    }

    /// <inheritdoc />
    public override void DrawImage(ImageSource imageSource, Rect rectangle)
        => DrawImage(imageSource, rectangle, BitmapScalingMode.Unspecified);

    /// <inheritdoc />
    public override void DrawImage(ImageSource imageSource, Rect rectangle, BitmapScalingMode scalingMode)
    {
        if (_closed || imageSource == null) return;

        // Stamped for the source the CALLER named, before any of the substitutions below can
        // replace it: the raster branch may hand GetNativeBitmap a downscaled thumbnail and the
        // animated branch a frame, and neither of those is the instance an idle element is about to
        // reclaim. The stamp is what tells that element's reclaim "somebody is still drawing this",
        // so it has to name the shared source, not the renderer's private stand-in for it.
        imageSource.MarkDrawn();

        // Video surface fast path: when the source is a D3DImage backed by a
        // NativeVideoSurface, skip the bitmap-cache machinery entirely and
        // dispatch straight to jalium_render_target_draw_video_surface so the
        // GPU samples the staged texture in-place. Stage 1 wires this; stage 2
        // backends (Software done, D3D12 / Vulkan pending) make it visible.
        if (imageSource is Jalium.UI.Interop.D3DImage d3dImage)
        {
            if (!d3dImage.IsFrontBufferAvailable || d3dImage.NativeHandle == nint.Zero) return;
            if (d3dImage.ResourceType == Jalium.UI.Interop.D3DResourceType.NativeVideoSurface)
            {
                var rx = (float)(rectangle.X + Offset.X);
                var ry = (float)(rectangle.Y + Offset.Y);
                _renderTarget.DrawVideoSurface(d3dImage.NativeHandle, rx, ry,
                    (float)rectangle.Width, (float)rectangle.Height, 1.0f, scalingMode);
                return;
            }
            // Other D3DResourceType kinds (IDirect3DSurface9 legacy / D3D11 / VkImage /
            // AHardwareBuffer) are not yet implemented — silently no-op until the
            // corresponding stages land. Callers should prefer NativeVideoSurface.
            return;
        }

        // Handle vector image sources by rendering the Drawing tree directly.
        // Source viewport is used instead of geometry bounds — for SVG, the
        // viewport is the (0, 0, width, height) rect that determines the
        // intended whitespace around content. Using geometry bounds would
        // stretch the visible geometry to fill the target rect, breaking
        // centering (e.g. settings cog) and clipping content positioned at
        // the viewport edges (e.g. notification bell decorations).
        Drawing? vectorDrawing = null;
        Rect vectorViewport = Rect.Empty;
        switch (imageSource)
        {
            case SvgImage svg when svg.Drawing != null:
                vectorDrawing = svg.Drawing;
                vectorViewport = (svg.Width > 0 && svg.Height > 0)
                    ? new Rect(0, 0, svg.Width, svg.Height)
                    : svg.Drawing.Bounds;
                break;
            case DrawingImage di when di.Drawing != null:
                vectorDrawing = di.Drawing;
                vectorViewport = di.Drawing.Bounds;
                break;
        }
        if (vectorDrawing != null)
        {
            var drawing = vectorDrawing;
            if (vectorViewport.IsEmpty || vectorViewport.Width <= 0 || vectorViewport.Height <= 0) return;

            // Rasterize at device-pixel resolution so the software anti-aliasing isn't
            // softened by a subsequent GPU upscale. An ancestor scale/rotate transform is
            // mirrored in _currentNativeMatrix, so fold its effective per-axis scale into
            // the raster size exactly as the bitmap-downscale branch below does. With no
            // in-tree transform sx=sy=1 and this matches a bare ceil(); the raster is then
            // drawn back at the logical rectangle size, so it is sampled 1:1 or minified,
            // never magnified. Clamped to bound memory under extreme zoom.
            // (Pure window DPI > 100% is applied by the native layer below the bitmap
            // upload, identical to the BitmapImage path, so it is intentionally not folded
            // here — matching the rest of the image pipeline rather than over-sampling.)
            double svgSx = Math.Sqrt(_currentNativeMatrix[0] * _currentNativeMatrix[0]
                                   + _currentNativeMatrix[1] * _currentNativeMatrix[1]);
            double svgSy = Math.Sqrt(_currentNativeMatrix[2] * _currentNativeMatrix[2]
                                   + _currentNativeMatrix[3] * _currentNativeMatrix[3]);
            if (svgSx <= 0 || double.IsNaN(svgSx)) svgSx = 1;
            if (svgSy <= 0 || double.IsNaN(svgSy)) svgSy = 1;
            svgSx = Math.Min(svgSx, 8.0);
            svgSy = Math.Min(svgSy, 8.0);
            var targetW = (int)Math.Ceiling(rectangle.Width * svgSx);
            var targetH = (int)Math.Ceiling(rectangle.Height * svgSy);
            if (targetW <= 0 || targetH <= 0) return;
            // Cap the raster buffer so a near-viewport SVG at extreme zoom can't OOM
            // (≤4096 per edge ≈ 64MB BGRA worst case); it degrades to slightly soft, not crash.
            const int MaxSvgRasterEdge = 4096;
            if (targetW > MaxSvgRasterEdge) targetW = MaxSvgRasterEdge;
            if (targetH > MaxSvgRasterEdge) targetH = MaxSvgRasterEdge;

            // ── Check cache: reuse rasterized BitmapImage if size matches ──
            // ...and if every bitmap baked into it still holds the pixels that were baked in. The
            // size compare alone served a DrawingImage's PRE-DECODE rasterization — an entirely
            // transparent buffer — for as long as the element kept its size, which for an icon in a
            // fixed cell is forever.
            if (_vectorDrawingCache.TryGetValue(imageSource, out var cached) &&
                cached.RasterizedBitmap != null &&
                cached.PixelWidth == targetW && cached.PixelHeight == targetH &&
                cached.MatchesTouchedSourceGenerations())
            {
                // Cache hit — draw via the standard bitmap pipeline (< 0.1ms)
                var cachedNative = GetNativeBitmap(cached.RasterizedBitmap);
                if (cachedNative != null)
                {
                    var cx = (float)(rectangle.X + Offset.X);
                    var cy = (float)(rectangle.Y + Offset.Y);
                    _renderTarget.DrawBitmap(cachedNative, cx, cy, (float)rectangle.Width, (float)rectangle.Height, 1.0f, scalingMode);

                    var frameNum2 = System.Threading.Interlocked.Increment(ref s_svgFrameNumber);
                    if (frameNum2 <= 5 || frameNum2 % 300 == 0)
                        System.Diagnostics.Debug.WriteLine($"[SVG Perf] Frame #{frameNum2} | CACHE HIT | {targetW}x{targetH}");
                    return;
                }
            }

            // ── Cache miss: rasterize SVG to BGRA pixel buffer ──
            _svgDiagStopwatch ??= new Stopwatch();
            _svgDiagStopwatch.Restart();
            _svgDiagActive = true;
            _svgDrawGeometryCount = 0;
            _svgDrawPathNativeCount = 0;
            _svgDrawPathPolygonCount = 0;
            _svgDrawCompoundCount = 0;
            _svgPushTransformCount = 0;
            _svgPopCount = 0;
            _svgGetBrushTicks = 0;
            _svgPathBuildTicks = 0;
            _svgNativeCallTicks = 0;
            _svgBoundsCalcTicks = 0;

            // Rasterize via CPU software renderer into a BGRA pixel buffer.
            //
            // The rasterizer records which inner bitmaps it sampled. That set is the entry's
            // staleness signal: this raster is a flat snapshot of sources that publish
            // asynchronously, and the very first rasterization of a URI-backed inner bitmap
            // necessarily happens before its decode has produced anything.
            var touchedSources = new HashSet<ImageSource>();
            var pixels = SoftwareVectorRasterizer.Rasterize(
                drawing, targetW, targetH, vectorViewport, touchedSources);
            BitmapImage? rasterized = null;
            if (pixels != null)
            {
                rasterized = BitmapImage.FromPixels(pixels, targetW, targetH, targetW * 4);
            }

            if (rasterized != null)
            {
                List<(ImageSource Source, long Generation)>? dependencies = null;
                if (touchedSources.Count > 0)
                {
                    dependencies = new List<(ImageSource, long)>(touchedSources.Count);
                    foreach (var touched in touchedSources)
                    {
                        dependencies.Add((touched, touched.ContentGeneration));
                    }
                }

                // Cache the BitmapImage — D3D12 resource lifecycle is managed by
                // the existing GetNativeBitmap / _bitmapCache pipeline.
                _vectorDrawingCache[imageSource] = new VectorDrawingCacheEntry
                {
                    RasterizedBitmap = rasterized,
                    PixelWidth = targetW,
                    PixelHeight = targetH,
                    TouchedSources = dependencies,
                };

                // Draw via standard bitmap pipeline
                var nativeBmp = GetNativeBitmap(rasterized);
                if (nativeBmp != null)
                {
                    var cx = (float)(rectangle.X + Offset.X);
                    var cy = (float)(rectangle.Y + Offset.Y);
                    _renderTarget.DrawBitmap(nativeBmp, cx, cy, (float)rectangle.Width, (float)rectangle.Height, 1.0f, scalingMode);
                }
            }
            else
            {
                // Fallback: direct rendering (slow path)
                var scaleX = rectangle.Width / vectorViewport.Width;
                var scaleY = rectangle.Height / vectorViewport.Height;

                var transform = new TransformGroup();
                transform.Add(new TranslateTransform { X = -vectorViewport.X, Y = -vectorViewport.Y });
                transform.Add(new ScaleTransform { ScaleX = scaleX, ScaleY = scaleY });
                transform.Add(new TranslateTransform { X = rectangle.X, Y = rectangle.Y });

                PushTransform(transform);
                drawing.RenderTo(this);
                Pop();
            }

            _svgDiagStopwatch.Stop();
            _svgDiagActive = false;
            var totalMs = _svgDiagStopwatch.Elapsed.TotalMilliseconds;

            var frameNum = System.Threading.Interlocked.Increment(ref s_svgFrameNumber);
            if (frameNum <= 10 || frameNum % 60 == 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SVG Perf] Frame #{frameNum} | RASTERIZE | Total: {totalMs:F2}ms | " +
                    $"Size: {targetW}x{targetH} | " +
                    $"Cached: {(rasterized != null ? "yes" : "fallback")}");
            }
            return;
        }

        // ── Adaptive bitmap downscaling ──────────────────────────────────────
        // A large BitmapImage drawn into a small target (e.g. a 1855×848 PNG shown
        // as a 168×72 card thumbnail) would otherwise upload the full-resolution
        // texture and re-sample it on the GPU. Route static BitmapImages through the
        // downscale cache so the realized GPU texture matches the display size;
        // misses fall back to the full-res source for this frame and the thumbnail
        // is synthesized asynchronously for the next one. Drawn-larger / at-size /
        // small sources return false (no downscale → full-res), giving the adaptive
        // "display-size by default, full-res on demand" behaviour. WriteableBitmap
        // (video), AnimatedBitmap and vector sources are excluded by the is-check and
        // the earlier branches, so their fast paths are untouched.
        // Transform-space size, in the same units the downscale cache has always used: an
        // ancestor scale/rotate transform is mirrored in _currentNativeMatrix, so fold its
        // effective scale in. With no in-tree transform sx=sy=1 and this is identical to a bare
        // ceil(); a genuine minify (sx<1) is honoured as-is to avoid over-large buckets.
        GetTransformScale(out var sx, out var sy);
        int bucketTargetW = (int)Math.Ceiling(rectangle.Width * sx);
        int bucketTargetH = (int)Math.Ceiling(rectangle.Height * sy);

        ImageSource drawSource = imageSource;
        if (imageSource is BitmapImage downscaleCandidate &&
            downscaleCandidate.TryGetPixelSnapshot(out var candidateSnapshot) &&
            candidateSnapshot is not null)
        {
            if (bucketTargetW > 0 && bucketTargetH > 0 &&
                Jalium.UI.Media.Imaging.BitmapDownscaleCache.TryGetOrCreate(
                    downscaleCandidate, candidateSnapshot, bucketTargetW, bucketTargetH, out var thumb))
            {
                drawSource = thumb;
            }
        }

        // The decode hint is the TRUE device rect, so it additionally folds in this render
        // target's own DPI — which _currentNativeMatrix does not carry, because the native side
        // applies the DPI scale itself. Per render target rather than per process, so a window on
        // a 200% secondary monitor asks for 200% pixels while one on the 100% primary does not.
        var (deviceW, deviceH) = ToDeviceHint(bucketTargetW, bucketTargetH);

        var bitmap = GetNativeBitmap(drawSource, deviceW, deviceH);
        if (bitmap == null) return;

        // Pixel snapping disabled: pass the origin through so animated images move
        // smoothly at sub-pixel precision instead of stepping by whole pixels.
        var x = (float)(rectangle.X + Offset.X);
        var y = (float)(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;

        _renderTarget.DrawBitmap(bitmap, x, y, width, height, 1.0f, scalingMode);
    }

    /// <inheritdoc />
    public override void DrawBackdropEffect(
        Rect rectangle,
        IBackdropEffect effect,
        CornerRadius cornerRadius)
    {
        if (_closed) return;

        // Check if there's any effect to apply
        if (effect == null || !effect.HasEffect) return;

        // Pixel snapping disabled: pass the backdrop origin through unchanged.
        var x = (float)(rectangle.X + Offset.X);
        var y = (float)(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;
        var normalizedCornerRadius = cornerRadius.Normalize(rectangle.Width, rectangle.Height);

        if (SimplifyBackdropEffects)
        {
            DrawSimplifiedBackdropEffect(x, y, width, height, cornerRadius, effect);
            return;
        }

        // Every IBackdropEffect parameter travels as one struct: blur kernel
        // family + sigma, the colour pipeline (brightness/contrast/saturation/
        // hue/grayscale/sepia/invert), tint with alpha, grain, opacity and the
        // per-corner rounding. The old string-based entry points could only
        // carry blur/tint/noise/saturation/luminosity and silently dropped the
        // rest. Native converts DIPs to physical pixels itself.
        var material = BackdropMaterialDesc.FromEffect(
            effect, x, y, width, height, in normalizedCornerRadius);
        _renderTarget.DrawBackdropMaterial(in material);
    }

    /// <summary>
    /// Begins capturing content into an offscreen bitmap for transition shader effects.
    /// Converts local bounds to screen coordinates using the current Offset.
    /// </summary>
    /// <param name="slot">0 = old content, 1 = new content.</param>
    /// <param name="localBounds">The transition area in local coordinates.</param>
    public void BeginTransitionCapture(int slot, Rect localBounds)
    {
        if (_closed) return;
        var x = (float)(localBounds.X + Offset.X);
        var y = (float)(localBounds.Y + Offset.Y);
        _renderTarget.BeginTransitionCapture(slot, x, y,
            (float)localBounds.Width, (float)localBounds.Height);
    }

    /// <summary>
    /// Ends capturing content for a transition slot and restores the main render target.
    /// </summary>
    /// <param name="slot">0 = old content, 1 = new content.</param>
    public void EndTransitionCapture(int slot)
    {
        if (_closed) return;
        _renderTarget.EndTransitionCapture(slot);
    }

    /// <summary>
    /// Draws the transition shader effect blending old and new content bitmaps.
    /// </summary>
    /// <param name="localBounds">The transition area in local coordinates.</param>
    /// <param name="progress">Transition progress (0.0 - 1.0).</param>
    /// <param name="mode">Shader mode index (0-9).</param>
    public void DrawTransitionShader(Rect localBounds, float progress, int mode, float cornerRadius = 0f)
    {
        if (_closed) return;
        var x = (float)(localBounds.X + Offset.X);
        var y = (float)(localBounds.Y + Offset.Y);
        _renderTarget.DrawTransitionShader(x, y,
            (float)localBounds.Width, (float)localBounds.Height, progress, mode, cornerRadius);
    }

    /// <summary>
    /// Draws a previously captured transition bitmap.
    /// </summary>
    public void DrawCapturedTransition(int slot, Rect localBounds, float opacity)
    {
        if (_closed) return;
        var x = (float)(localBounds.X + Offset.X);
        var y = (float)(localBounds.Y + Offset.Y);
        _renderTarget.DrawCapturedTransition(slot, x, y,
            (float)localBounds.Width, (float)localBounds.Height, opacity);
    }

    /// <summary>
    /// Draws a liquid glass effect. Overrides the base <see cref="DrawingContext"/>
    /// entry point so the call survives the retained-mode recorder round-trip
    /// (the recorder captures the parameter object; on replay, the cached
    /// <see cref="DrawingReplayer"/> dispatches back here on the live target).
    /// </summary>
    public override void DrawLiquidGlass(LiquidGlassParameters parameters)
    {
        if (_closed || parameters is null) return;

        var rectangle = parameters.Rectangle;
        var x = (float)(rectangle.X + Offset.X);
        var y = (float)(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;

        if (SimplifyBackdropEffects)
        {
            float overlayAlpha = Math.Clamp(
                parameters.TintOpacity > 0 ? parameters.TintOpacity : 0.22f,
                0.14f, 0.42f);
            FillTransientOverlay(
                x, y, width, height,
                parameters.CornerRadius, parameters.CornerRadius,
                parameters.TintR, parameters.TintG, parameters.TintB,
                overlayAlpha);
            return;
        }

        var neighborData = parameters.NeighborData is { } buffer
            ? new ReadOnlySpan<float>(buffer, 0, Math.Min(buffer.Length, parameters.NeighborCount * 5))
            : ReadOnlySpan<float>.Empty;

        _renderTarget.DrawLiquidGlass(
            x, y, width, height,
            parameters.CornerRadius, parameters.BlurRadius,
            parameters.RefractionAmount, parameters.ChromaticAberration,
            parameters.TintR, parameters.TintG, parameters.TintB, parameters.TintOpacity,
            parameters.LightX, parameters.LightY, parameters.HighlightBoost,
            parameters.ShapeType, parameters.ShapeExponent,
            parameters.NeighborCount, parameters.FusionRadius, neighborData);
    }

    /// <inheritdoc />
    public override void PushTransform(Transform transform)
    {
        if (_closed) return;

        if (_svgDiagActive)
            _svgPushTransformCount++;

        if (transform is TranslateTransform translate)
        {
            // Translation: handled via managed Offset (existing fast path)
            _stateStack.Push(new DrawingState(DrawingStateType.Transform, Offset));
            Offset = new Point(Offset.X + translate.X, Offset.Y + translate.Y);
        }
        else
        {
            // Non-translate transform: push native D2D1 matrix.
            // Drawing operations add managed Offset to coordinates before native,
            // so we compose: T(-offset) * transform * T(+offset) to apply
            // the transform in local space while coordinates are in screen space.
            var m = transform.Value;
            var ox = Offset.X;
            var oy = Offset.Y;

            // step 1: T(-offset) * transform
            // M' = T(-ox,-oy) * M
            var m11 = m.M11;
            var m12 = m.M12;
            var m21 = m.M21;
            var m22 = m.M22;
            var dx = -ox * m11 + -oy * m21 + m.OffsetX;
            var dy = -ox * m12 + -oy * m22 + m.OffsetY;

            // step 2: result * T(+offset)
            var finalDx = dx + ox;
            var finalDy = dy + oy;

            // --- Nested-transform fix --------------------------------------------
            // Native PushTransform performs RIGHT-multiply: native_new = native_old * incoming.
            // For nested visuals (outer pushed first, inner pushed second), the
            // expected row-vector apply order is P * inner * outer (inner applied
            // first, then outer). That requires LEFT-multiply on the stack:
            //   stack_new = incoming * stack_old
            // The native renderer can't be changed cheaply (4 C++ backends), so we
            // pass it a conjugated matrix that, after native's RIGHT-multiply,
            // produces the same final state as LEFT-multiply:
            //   conjugate = current^-1 * incoming * current
            //   native_new = current * conjugate = incoming * current  ✓
            // Falls back to plain `incoming` when current is singular (e.g. zero
            // scale) — that's harmless because a singular accumulated transform
            // already collapses subsequent drawing anyway.
            var incoming = new Jalium.UI.Media.Matrix(m11, m12, m21, m22, finalDx, finalDy);
            var current = new Jalium.UI.Media.Matrix(
                _currentNativeMatrix[0], _currentNativeMatrix[1],
                _currentNativeMatrix[2], _currentNativeMatrix[3],
                _currentNativeMatrix[4], _currentNativeMatrix[5]);
            Jalium.UI.Media.Matrix toNative;
            if (current.TryInvert(out var currentInv))
            {
                toNative = currentInv * incoming * current;
            }
            else
            {
                toNative = incoming;
            }

            _renderTarget.PushTransform(new float[]
            {
                (float)toNative.M11, (float)toNative.M12,
                (float)toNative.M21, (float)toNative.M22,
                (float)toNative.OffsetX, (float)toNative.OffsetY
            });
            _nativeTransformDepth++;

            // Save the previous matrix so Pop can restore it.
            _nativeMatrixStack.Push((double[])_currentNativeMatrix.Clone());

            // managed mirror tracks the LEFT-multiplied state (matches what
            // native ends up with after the conjugate is RIGHT-multiplied).
            var newState = incoming * current;
            _currentNativeMatrix[0] = newState.M11;
            _currentNativeMatrix[1] = newState.M12;
            _currentNativeMatrix[2] = newState.M21;
            _currentNativeMatrix[3] = newState.M22;
            _currentNativeMatrix[4] = newState.OffsetX;
            _currentNativeMatrix[5] = newState.OffsetY;

            _stateStack.Push(new DrawingState(DrawingStateType.NativeTransform, Point.Zero));
        }
    }

    /// <summary>
    /// Explicit implementation of ITransformDrawingContext.PushTransform.
    /// Composes the transform with the requested origin offset and forwards to
    /// the typed PushTransform.
    /// </summary>
    void ITransformDrawingContext.PushTransform(Transform transform, double originX, double originY)
    {
        if (originX != 0 || originY != 0)
        {
            // Compose: T(-origin) * transform * T(+origin)
            var m = transform.Value;
            var pre = new Matrix(1, 0, 0, 1, -originX, -originY);
            var post = new Matrix(1, 0, 0, 1, originX, originY);
            var combined = Matrix.Multiply(Matrix.Multiply(pre, m), post);
            PushTransform(new MatrixTransform(combined));
        }
        else
        {
            PushTransform(transform);
        }
    }

    /// <summary>
    /// Explicit implementation of ITransformDrawingContext.PopTransform.
    /// </summary>
    void ITransformDrawingContext.PopTransform()
    {
        Pop();
    }

    /// <inheritdoc />
    public override void PushClip(Geometry clipGeometry)
    {
        if (_closed || clipGeometry == null) return;

        var rectangleGeometry = clipGeometry as RectangleGeometry;
        var bounds = rectangleGeometry is
            {
                BoundsClipEdges: not ClipEdges.All,
                BoundsClipRect.IsEmpty: false
            }
            ? rectangleGeometry.BoundsClipRect
            : clipGeometry.Bounds;
        var offsetBounds = new Rect(
            bounds.X + Offset.X,
            bounds.Y + Offset.Y,
            bounds.Width,
            bounds.Height);
        if (rectangleGeometry is { BoundsClipEdges: not ClipEdges.All })
        {
            offsetBounds = ResolveBoundsClip(
                offsetBounds,
                rectangleGeometry.BoundsClipEdges,
                GetBoundsClipLimit());
        }

        var exactLeft = offsetBounds.Left;
        var exactTop = offsetBounds.Top;
        var exactRight = offsetBounds.Right;
        var exactBottom = offsetBounds.Bottom;

        var (x, y, w, h) = SnapHardClipRect(exactLeft, exactTop, exactRight, exactBottom);

        // Rounded clips get the EXACT rect instead. Their backend counterpart is an
        // antialiased SDF coverage mask evaluated per fragment (rounded_clip.hlsli,
        // sampled by every batched pixel shader), and that mask must sit on the true
        // geometric boundary. Handing it the outward-expanded rect — which this
        // method used to do for both paths — made the antialiased clip up to 1px
        // LOOSER than the real one, so clipped content survived a row past where it
        // should have stopped, at full coverage.
        //
        // That produced a bright 1px seam wherever a ClipToBounds Border held an
        // Image under a gradient scrim: the scrim is an ordinary fill and stopped on
        // its own exact AA edge, while the Image rode the loosened mask one row
        // further and showed through unscrimmed. The backend still expands the
        // scissor itself (D3D12RenderTarget::EmitRoundedClipPair), so the hard cull
        // stays conservative while the mask stays exact.
        //
        // Snapping this one INWARD as well was tried for the pale rim along the corner arcs
        // and is NOT the answer: the rim survived unchanged (it is not clipped content
        // reaching past the fill) and the inward snap cost a row of the image at the top
        // edge, trading a bright seam for a dark one. Leave it exact.
        var ex = (float)exactLeft;
        var ey = (float)exactTop;
        var ew = (float)Math.Max(0, exactRight - exactLeft);
        var eh = (float)Math.Max(0, exactBottom - exactTop);

        var clipRect = new Rect(exactLeft, exactTop, Math.Max(0, exactRight - exactLeft), Math.Max(0, exactBottom - exactTop));
        PushClipBounds(clipRect);

        if (rectangleGeometry is { } rectGeom)
        {
            if (rectGeom.HasPerCornerRadii)
            {
                var (tl, tr, br, bl) = NormalizePerCornerRadii(ew, eh,
                    rectGeom.CornerRadius.TopLeft,
                    rectGeom.CornerRadius.TopRight,
                    rectGeom.CornerRadius.BottomRight,
                    rectGeom.CornerRadius.BottomLeft);
                _renderTarget.PushPerCornerRoundedRectClip(ex, ey, ew, eh, tl, tr, br, bl);
            }
            else if (rectGeom.RadiusX > 0 || rectGeom.RadiusY > 0)
            {
                var (rx, ry) = NormalizeRoundedRectRadii(ew, eh, rectGeom.RadiusX, rectGeom.RadiusY);
                _renderTarget.PushRoundedRectClip(ex, ey, ew, eh, rx, ry);
            }
            else
            {
                _renderTarget.PushClip(x, y, w, h);
            }
        }
        else
        {
            _renderTarget.PushClip(x, y, w, h);
        }

        _stateStack.Push(new DrawingState(DrawingStateType.Clip, Point.Zero));
    }

    /// <summary>
    /// Snaps a hard (scissor-only) clip rectangle to whole pixels.
    ///
    /// Aliased clips have NO antialiased mask behind them — the scissor rectangle is
    /// the visual boundary itself. An integer scissor cannot represent a fractional
    /// clip edge, so some rounding is unavoidable, and both fixed directions have
    /// shipped as visible defects:
    ///
    ///  - OUTWARD (Floor start / Ceiling end) let un-antialiased content — bitmap
    ///    quads sample pixel centers at full coverage — survive a whole row past the
    ///    clip. That was the bright 1px seam an Image under a gradient scrim showed
    ///    at the bottom of a ClipToBounds host, pulsing while an animation swept the
    ///    fraction.
    ///  - INWARD (Ceiling start / Floor end) shaved the first row/column of content
    ///    sitting flush against the clip origin. A 1px chip border inside a
    ///    ScrollViewer whose layout landed on a fractional Y lost its entire top
    ///    edge: the stroke's only pixel row fell wholly outside the scissor while
    ///    the bottom/right edges survived, so every sub-pixel layout rendered a
    ///    border with a missing side.
    ///
    /// Round-to-NEAREST bounds both failure modes at half a pixel and matches the
    /// straight-edge policy the rounded-clip path already settled on (native
    /// EmitRoundedClipPair): hard clip edges land on the nearest pixel boundary, so
    /// flush content keeps its first row whenever the majority of that row is inside
    /// the clip, and leakage past the true edge can never reach a full-coverage row.
    ///
    /// A clip thinner than one pixel must not collapse to nothing: rounding both
    /// edges to the same boundary would erase a 0.4px-tall region entirely, and a
    /// caller that asked to clip to a hairline still expects to see the hairline.
    /// Such a rect widens to the one whole pixel a scissor can express.
    /// </summary>
    internal static (float X, float Y, float W, float H) SnapHardClipRect(
        double exactLeft, double exactTop, double exactRight, double exactBottom)
    {
        var x = (float)Math.Round(exactLeft);
        var y = (float)Math.Round(exactTop);
        var w = (float)Math.Round(exactRight) - x;
        var h = (float)Math.Round(exactBottom) - y;

        if (w <= 0 && exactRight > exactLeft)
        {
            w = 1f;
        }
        if (h <= 0 && exactBottom > exactTop)
        {
            h = 1f;
        }

        return (x, y, w, h);
    }

    /// <summary>
    /// Pushes a rounded-rect clip using element bounds and corner radius.
    /// </summary>
    public void PushRoundedRectClip(Rect bounds, CornerRadius cornerRadius)
    {
        if (_closed) return;

        var x = (float)(bounds.X + Offset.X);
        var y = (float)(bounds.Y + Offset.Y);
        var w = (float)bounds.Width;
        var h = (float)bounds.Height;

        var clipRect = new Rect(x, y, w, h);
        PushClipBounds(clipRect);

        bool perCorner =
            cornerRadius.TopLeft != cornerRadius.TopRight ||
            cornerRadius.TopRight != cornerRadius.BottomRight ||
            cornerRadius.BottomRight != cornerRadius.BottomLeft;
        if (perCorner)
        {
            var (tl, tr, br, bl) = NormalizePerCornerRadii(w, h,
                cornerRadius.TopLeft, cornerRadius.TopRight,
                cornerRadius.BottomRight, cornerRadius.BottomLeft);
            _renderTarget.PushPerCornerRoundedRectClip(x, y, w, h, tl, tr, br, bl);
        }
        else
        {
            var r = (float)cornerRadius.TopLeft;
            _renderTarget.PushRoundedRectClip(x, y, w, h, r, r);
        }
        _stateStack.Push(new DrawingState(DrawingStateType.Clip, Point.Zero));
    }

    /// <inheritdoc />
    public void PushPerCornerRoundedRectClip(Rect bounds, CornerRadius cornerRadius)
    {
        if (_closed) return;

        var x = (float)(bounds.X + Offset.X);
        var y = (float)(bounds.Y + Offset.Y);
        var w = (float)bounds.Width;
        var h = (float)bounds.Height;

        var clipRect = new Rect(x, y, w, h);
        PushClipBounds(clipRect);

        var (tl, tr, br, bl) = NormalizePerCornerRadii(w, h,
            cornerRadius.TopLeft, cornerRadius.TopRight,
            cornerRadius.BottomRight, cornerRadius.BottomLeft);
        _renderTarget.PushPerCornerRoundedRectClip(x, y, w, h, tl, tr, br, bl);
        _stateStack.Push(new DrawingState(DrawingStateType.Clip, Point.Zero));
    }

    /// <inheritdoc />
    public override void PushOpacity(double opacity)
    {
        if (_closed) return;

        _renderTarget.PushOpacity((float)opacity);
        _stateStack.Push(new DrawingState(DrawingStateType.Opacity, Point.Zero));
        _opacityDepth++;
    }

    /// <summary>
    /// Sets the current shape type for subsequent SDF rect draw calls.
    /// Call with (0, 0) to reset to default rounded rectangle mode.
    /// </summary>
    /// <param name="type">0 = RoundedRect, 1 = SuperEllipse.</param>
    /// <param name="n">SuperEllipse exponent (e.g. 4.0 for squircle).</param>
    public override void SetShapeType(int type, float n)
    {
        if (_closed) return;
        _renderTarget.SetShapeType(type, n);
    }

    /// <summary>
    /// Punches a transparent rectangular hole using the current offset and clip stack.
    /// </summary>
    public void PunchTransparentRect(Rect rectangle)
    {
        if (_closed) return;

        var x = (float)Math.Round(rectangle.X + Offset.X);
        var y = (float)Math.Round(rectangle.Y + Offset.Y);
        var width = (float)Math.Round(rectangle.Width);
        var height = (float)Math.Round(rectangle.Height);

        if (width <= 0 || height <= 0)
            return;

        _renderTarget.PunchTransparentRect(x, y, width, height);
    }

    /// <summary>
    /// Pops the most recent opacity from the opacity stack.
    /// </summary>
    public void PopOpacity()
    {
        if (_closed) return;

        // Pop from our state stack if the top is opacity
        if (_stateStack.Count > 0 && _stateStack.Peek().Type == DrawingStateType.Opacity)
        {
            _stateStack.Pop();
            if (_opacityDepth > 0) _opacityDepth--;
        }
        _renderTarget.PopOpacity();
    }

    /// <inheritdoc />
    public override void Pop()
    {
        if (_closed || _stateStack.Count == 0) return;

        if (_svgDiagActive)
            _svgPopCount++;

        var state = _stateStack.Pop();
        switch (state.Type)
        {
            case DrawingStateType.Transform:
                Offset = state.SavedOffset;
                break;
            case DrawingStateType.NativeTransform:
                _nativeTransformDepth--;
                if (_nativeMatrixStack.Count > 0)
                {
                    var prev = _nativeMatrixStack.Pop();
                    Array.Copy(prev, _currentNativeMatrix, 6);
                }
                _renderTarget.PopTransform();
                break;
            case DrawingStateType.Clip:
                if (_clipBoundsStack.Count > 0)
                {
                    _clipBoundsStack.Pop();
                }
                _renderTarget.PopClip();
                break;
            case DrawingStateType.Opacity:
                if (_opacityDepth > 0) _opacityDepth--;
                _renderTarget.PopOpacity();
                break;
            case DrawingStateType.ViewportOnly:
                if (_clipBoundsStack.Count > 0)
                {
                    _clipBoundsStack.Pop();
                }
                // No native PopClip — ViewportOnly only affects managed culling
                break;
        }
    }

    /// <summary>
    /// Pushes a dirty region clip that restricts D2D rendering AND managed viewport
    /// culling to the specified rectangle. Uses the native PushClip for GPU-side
    /// clipping and updates <see cref="CurrentClipBounds"/> for
    /// <see cref="Visual.ShouldRenderChild"/> viewport culling.
    /// </summary>
    internal void PushDirtyRegionClip(Rect dirtyRegion)
    {
        if (_closed) return;

        // Pixel-snap OUTWARD (floor/ceil) — exactly the box the ALIASED GPU scissor below
        // covers, hence (clamped by that scissor) exactly the box ClearBackground clears on a
        // partial frame. Computed once so the SAME snapped box feeds both the managed cull clip
        // and the native scissor: if they diverge a cleared edge pixel can be culled-but-not-
        // repainted, which blinks grazing Path icons every frame an animation drives the clip.
        double snapX = Math.Floor(dirtyRegion.X);
        double snapY = Math.Floor(dirtyRegion.Y);
        double snapW = Math.Ceiling(dirtyRegion.X + dirtyRegion.Width) - snapX;
        double snapH = Math.Ceiling(dirtyRegion.Y + dirtyRegion.Height) - snapY;

        // CULL clip (the managed culling hint read by Visual.ShouldRenderChild AND
        // DrawingReplayer) = the snapped box grown by a sub-pixel epsilon on every side, so an
        // element flush against the snapped integer boundary still STRICTLY overlaps under
        // Rect.IntersectsWith. This makes the cull clip a guaranteed SUPERSET of the cleared
        // pixels; the native renderer owns the REAL clip via PushClipAliased below, so an
        // over-large managed cull hint is always safe (over-cull = harmless overdraw).
        PushClipBounds(new Rect(
            snapX - ClipCullEpsilon,
            snapY - ClipCullEpsilon,
            snapW + 2 * ClipCullEpsilon,
            snapH + 2 * ClipCullEpsilon));

        // GPU scissor — TIGHT and unchanged: the same floor/ceil-snapped integer box, NO
        // epsilon. ALIASED mode = hard pixel boundary, no semi-transparent edge seams.
        _renderTarget.PushClipAliased((float)snapX, (float)snapY, (float)snapW, (float)snapH);
        _stateStack.Push(new DrawingState(DrawingStateType.Clip, Point.Zero));
    }

    /// <summary>
    /// Pops a dirty region clip previously pushed by <see cref="PushDirtyRegionClip"/>.
    /// </summary>
    internal void PopDirtyRegionClip()
    {
        if (_closed) return;
        Pop();
    }

    // ========================================================================
    // Per-draw PushEffect / PopEffect — nestable capture-and-shader scopes.
    // Distinct from the element-level capture that Visual.Render orchestrates
    // around a UIElement.Effect: this one is caller-driven, so per-glyph
    // animation or selective effect regions can opt in explicitly.
    // ========================================================================

    /// <inheritdoc />
    public override void PushEffect(IEffect effect, Rect captureBounds)
    {
        if (_closed || effect == null || !effect.HasEffect || SimplifyElementEffects) return;

        var padding = effect.EffectPadding;

        // Capture region = element bounds inflated by effect padding (shadows,
        // glows etc. draw outside the element). Apply current Offset so the
        // capture sits at the right screen position.
        var left = captureBounds.X + Offset.X - padding.Left;
        var top = captureBounds.Y + Offset.Y - padding.Top;
        var right = captureBounds.X + Offset.X + captureBounds.Width + padding.Right;
        var bottom = captureBounds.Y + Offset.Y + captureBounds.Height + padding.Bottom;

        // Pixel-snap the capture bounds, same as Visual.Render does, so changing
        // a continuous parameter (e.g. animated blur radius) doesn't re-sample
        // sub-pixel edges every frame.
        var snappedLeft = (float)Math.Floor(left);
        var snappedTop = (float)Math.Floor(top);
        var snappedRight = (float)Math.Ceiling(right);
        var snappedBottom = (float)Math.Ceiling(bottom);
        var captureW = Math.Max(0f, snappedRight - snappedLeft);
        var captureH = Math.Max(0f, snappedBottom - snappedTop);

        if (captureW <= 0 || captureH <= 0) return;

        // Store the FULL capture region — see PushedEffect's xml doc for why.
        // (X, Y) is the screen-space top-left of the capture region (with
        // padding). (W, H) is the full capture size. CaptureX/CaptureY match
        // X/Y, giving uvOffset = 0 so ApplyElementEffect samples the offscreen
        // texture starting from its top-left and covers the entire blurred area.
        _effectStack.Push(new PushedEffect(effect,
            snappedLeft, snappedTop, captureW, captureH,
            snappedLeft, snappedTop));

        BeginEffectCapture(snappedLeft, snappedTop, captureW, captureH);
    }

    /// <inheritdoc />
    public override void PopEffect()
    {
        if (_closed || _effectStack.Count == 0) return;

        var entry = _effectStack.Pop();
        EndEffectCapture();
        ApplyElementEffect(entry.Effect,
            entry.X, entry.Y, entry.W, entry.H,
            entry.CaptureX, entry.CaptureY);
    }

    // ========================================================================
    // Element Effect Capture & Rendering
    // ========================================================================

    /// <summary>
    /// Begins capturing element content into an offscreen bitmap for effect processing.
    /// </summary>
    /// <remarks>
    /// <paramref name="x"/>/<paramref name="y"/>/<paramref name="w"/>/<paramref name="h"/> are
    /// in the current managed drawing space (Offset-included, BEFORE the live native
    /// matrix), like every other draw call. Under a non-identity native matrix the
    /// capture must still cover the element's whole SCREEN footprint (a scaled-up
    /// element overflows its untransformed rect), so the screen-space AABB is
    /// computed here and recorded for <see cref="ApplyElementEffect"/>; whether the
    /// AABB or the untransformed rect is handed to native depends on which space
    /// the backend expects — see <see cref="NativeEffectCaptureRectIsPostTransform"/>.
    /// </remarks>
    public void BeginEffectCapture(float x, float y, float w, float h)
    {
        if (_closed) return;
        if (SimplifyElementEffects)
        {
            _suppressedEffectCaptureDepth++;
            return;
        }
        // Swap the cull source from the dirty-region/viewport clip to the
        // capture rect for the capture's duration — see CurrentClipBounds.
        // Stored in SURFACE space (the exact PushClipBounds mapping) so it
        // survives transform pushes inside the capture; grown by the same
        // sub-pixel epsilon as the dirty-region cull clip so float round-trip
        // error can never cull content flush against the capture edge.
        // Balanced in EndEffectCapture.
        var captureRect = new Rect(
            x - ClipCullEpsilon,
            y - ClipCullEpsilon,
            w + 2 * ClipCullEpsilon,
            h + 2 * ClipCullEpsilon);
        _effectCaptureCullOverrideStack.Push(_nativeTransformDepth > 0
            ? TransformRectAabb(captureRect,
                _currentNativeMatrix[0], _currentNativeMatrix[1],
                _currentNativeMatrix[2], _currentNativeMatrix[3],
                _currentNativeMatrix[4], _currentNativeMatrix[5])
            : captureRect);
        _effectCaptureCullSuspendDepth++;

        float nativeX = x, nativeY = y, nativeW = w, nativeH = h;
        var frame = new EffectCaptureFrame(x, y, Transformed: false);
        if (TryGetActiveNativeTransform(out var liveMatrix))
        {
            var screenRect = ComputeScreenEffectCaptureRect(
                new Rect(x, y, w, h), liveMatrix,
                _renderTarget.DpiScaleX, _renderTarget.DpiScaleY);
            frame = new EffectCaptureFrame(screenRect.X, screenRect.Y, Transformed: true);
            if (NativeEffectCaptureRectIsPostTransform)
            {
                nativeX = (float)screenRect.X;
                nativeY = (float)screenRect.Y;
                nativeW = (float)screenRect.Width;
                nativeH = (float)screenRect.Height;
            }
        }
        _effectCaptureFrameStack.Push(frame);
        _renderTarget.BeginEffectCapture(nativeX, nativeY, nativeW, nativeH);
    }

    /// <summary>
    /// Ends capturing element content and restores the main render target.
    /// </summary>
    public void EndEffectCapture()
    {
        if (_closed) return;
        if (_suppressedEffectCaptureDepth > 0)
        {
            _suppressedEffectCaptureDepth--;
            return;
        }
        if (_effectCaptureCullSuspendDepth > 0)
            _effectCaptureCullSuspendDepth--;
        if (_effectCaptureCullOverrideStack.Count > 0)
            _effectCaptureCullOverrideStack.Pop();
        _lastEndedEffectCapture = _effectCaptureFrameStack.Count > 0
            ? _effectCaptureFrameStack.Pop()
            : default;
        _renderTarget.EndEffectCapture();
    }

    /// <summary>
    /// True when the native backend treats the rect handed to its effect capture as
    /// POST-transform (screen) space: D3D12's <c>BeginOffscreenCapture</c> composes
    /// <c>T(-x,-y)</c> on top of the live transform stack and sizes the texture by
    /// <c>(w,h)</c> directly, so under a live matrix it needs the screen-space AABB
    /// or the transformed content overflows / lands outside the texture. Vulkan
    /// (OffscreenBegin marker) and the software rasterizer map the incoming rect
    /// through the live transform themselves and must keep receiving the
    /// untransformed rect — feeding them the AABB would transform it twice.
    /// </summary>
    private bool NativeEffectCaptureRectIsPostTransform =>
        _renderTarget.Backend == RenderBackend.D3D12;

    /// <summary>
    /// Returns the live native matrix when a non-identity non-translate transform is
    /// active (surface = drawing × matrix), false at identity so the untransformed
    /// fast paths stay bit-identical to their historical behaviour.
    /// </summary>
    private bool TryGetActiveNativeTransform(out Jalium.UI.Media.Matrix matrix)
    {
        matrix = Jalium.UI.Media.Matrix.Identity;
        if (_nativeTransformDepth <= 0)
            return false;

        var m11 = _currentNativeMatrix[0];
        var m12 = _currentNativeMatrix[1];
        var m21 = _currentNativeMatrix[2];
        var m22 = _currentNativeMatrix[3];
        var dx = _currentNativeMatrix[4];
        var dy = _currentNativeMatrix[5];
        const double eps = 1e-6;
        if (Math.Abs(m11 - 1.0) < eps && Math.Abs(m12) < eps &&
            Math.Abs(m21) < eps && Math.Abs(m22 - 1.0) < eps &&
            Math.Abs(dx) < eps && Math.Abs(dy) < eps)
        {
            return false;
        }

        matrix = new Jalium.UI.Media.Matrix(m11, m12, m21, m22, dx, dy);
        return true;
    }

    /// <summary>
    /// Screen-space capture rect for an untransformed capture rect under
    /// <paramref name="matrix"/>: the transformed AABB, snapped OUTWARD to the
    /// physical pixel grid. The snap is what keeps the composite crisp — the
    /// composite samples the capture texture at
    /// <c>texel = (screenPixelCentre − captureOrigin) × dpi</c>, so an origin on the
    /// pixel grid puts every sample on a texel centre (a 1:1 copy) instead of a
    /// bilinear blend of neighbours that softens text and vector edges.
    /// </summary>
    internal static Rect ComputeScreenEffectCaptureRect(
        Rect captureRect, Jalium.UI.Media.Matrix matrix, double dpiScaleX, double dpiScaleY)
    {
        var aabb = TransformRectAabb(captureRect,
            matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.OffsetX, matrix.OffsetY);
        double sx = dpiScaleX > 0 ? dpiScaleX : 1.0;
        double sy = dpiScaleY > 0 ? dpiScaleY : 1.0;
        double left = Math.Floor(aabb.X * sx) / sx;
        double top = Math.Floor(aabb.Y * sy) / sy;
        double right = Math.Ceiling((aabb.X + aabb.Width) * sx) / sx;
        double bottom = Math.Ceiling((aabb.Y + aabb.Height) * sy) / sy;
        return new Rect(left, top, Math.Max(0.0, right - left), Math.Max(0.0, bottom - top));
    }

    /// <summary>
    /// Per-axis scale magnitudes of an affine matrix and the average used to scale
    /// isotropic effect quantities (blur radius, corner radii). Matches the native
    /// backends' own convention (Vulkan scales its shadow radius/sigma by the same
    /// average of the column norms).
    /// </summary>
    internal static void GetTransformScales(Jalium.UI.Media.Matrix matrix,
        out double scaleX, out double scaleY, out double average)
    {
        scaleX = Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
        scaleY = Math.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22);
        average = 0.5 * (scaleX + scaleY);
    }

    /// <summary>
    /// Test seam: the frame recorded by the most recent EndEffectCapture.
    /// </summary>
    internal EffectCaptureFrame LastEndedEffectCaptureForTests => _lastEndedEffectCapture;

    /// <summary>
    /// Applies the given element effect to the captured content and draws the result.
    /// Dispatches to the appropriate native rendering method based on concrete effect type.
    /// </summary>
    /// <remarks>
    /// Under a live non-identity native matrix the capture texture already holds the
    /// element transformed once (screen space). Compositing it through the live matrix
    /// again would transform it twice, so this neutralizes the matrix for the duration
    /// of the native call (pushes its inverse — the same trick DrawText uses to
    /// rasterize glyphs at screen resolution) and hands native SCREEN-space geometry:
    /// the AABB of the transformed element rect, the shadow/glow offsets mapped through
    /// the linear part, isotropic radii scaled by the average axis scale, and a UV
    /// offset relative to the screen-space capture origin recorded at
    /// <see cref="BeginEffectCapture"/>. Rotation/skew degrade to the AABB for the
    /// analytic shadow shape (the content composite stays exact) — the same
    /// approximation the Vulkan backend applies natively.
    /// </remarks>
    public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
        float captureOriginX = 0, float captureOriginY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
    {
        if (_closed || effect == null || SimplifyElementEffects) return;

        if (!TryGetActiveNativeTransform(out var liveMatrix) ||
            !liveMatrix.TryInvert(out var inverse))
        {
            // Identity (or singular — nothing sensible to draw): the historical path,
            // untouched. UV offset = element position relative to the capture origin.
            ApplyElementEffectCore(effect, x, y, w, h,
                x - captureOriginX, y - captureOriginY,
                Jalium.UI.Media.Matrix.Identity, 1.0,
                cornerTL, cornerTR, cornerBR, cornerBL);
            return;
        }

        var elementRect = TransformRectAabb(new Rect(x, y, w, h),
            liveMatrix.M11, liveMatrix.M12, liveMatrix.M21, liveMatrix.M22,
            liveMatrix.OffsetX, liveMatrix.OffsetY);
        GetTransformScales(liveMatrix, out _, out _, out var scale);

        // Screen-space capture origin: what BeginEffectCapture recorded for this scope.
        // A frame that was NOT recorded under a transform (unbalanced push between Begin
        // and Apply — never the case for Visual/PushEffect) degrades to mapping the
        // untransformed origin through the same snap, so the composite still lands on
        // the element instead of drawing from a stale field.
        var frame = _lastEndedEffectCapture;
        double captureScreenX, captureScreenY;
        if (frame.Transformed)
        {
            captureScreenX = frame.ScreenX;
            captureScreenY = frame.ScreenY;
        }
        else
        {
            var mappedOrigin = ComputeScreenEffectCaptureRect(
                new Rect(captureOriginX, captureOriginY, 0, 0), liveMatrix,
                _renderTarget.DpiScaleX, _renderTarget.DpiScaleY);
            captureScreenX = mappedOrigin.X;
            captureScreenY = mappedOrigin.Y;
        }

        var inv = _effectInverseMatrixScratch;
        inv[0] = (float)inverse.M11; inv[1] = (float)inverse.M12;
        inv[2] = (float)inverse.M21; inv[3] = (float)inverse.M22;
        inv[4] = (float)inverse.OffsetX; inv[5] = (float)inverse.OffsetY;

        // Native's compose is new_top = old_top * incoming, so pushing the inverse of
        // the mirrored matrix leaves native at identity (D3D12) / the DPI root only
        // (Vulkan): the screen-space geometry below goes straight onto the surface.
        _renderTarget.PushTransform(inv);
        try
        {
            ApplyElementEffectCore(effect,
                (float)elementRect.X, (float)elementRect.Y,
                (float)elementRect.Width, (float)elementRect.Height,
                (float)(elementRect.X - captureScreenX), (float)(elementRect.Y - captureScreenY),
                liveMatrix, scale,
                (float)(cornerTL * scale), (float)(cornerTR * scale),
                (float)(cornerBR * scale), (float)(cornerBL * scale));
        }
        finally
        {
            _renderTarget.PopTransform();
        }
    }

    /// <summary>
    /// Effect dispatch in the space native will draw in. <paramref name="space"/> is the
    /// live matrix whose linear part maps effect OFFSET vectors (shadow/inner-shadow
    /// offsets, emboss light direction) into that space; <paramref name="scale"/> scales
    /// isotropic pixel quantities (blur radii, glow size, emboss relief). Identity /
    /// 1.0 at identity, where the arguments are the caller's untransformed values.
    /// EffectGroup recursion stays inside this method so the compensation above is
    /// applied exactly once per capture.
    /// </summary>
    private void ApplyElementEffectCore(IEffect effect, float x, float y, float w, float h,
        float uvOffX, float uvOffY,
        Jalium.UI.Media.Matrix space, double scale,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL)
    {
        if (!_effectApplicationPath.Add(effect)) return;

        try
        {

        if (effect is Media.Effects.BlurEffect blur)
        {
            if (blur.Radius > 0)
            {
                // Blur content should be clipped to element's rounded corners.
                // x,y already contain the element's screen position (= Offset).
                bool hasCorners = cornerTL > 0 || cornerTR > 0 || cornerBR > 0 || cornerBL > 0;
                if (hasCorners)
                {
                    float maxR = Math.Max(Math.Max(cornerTL, cornerTR), Math.Max(cornerBR, cornerBL));
                    _renderTarget.PushRoundedRectClip(x, y, w, h, maxR, maxR);
                }
                _renderTarget.DrawBlurEffect(x, y, w, h, (float)(blur.Radius * scale), uvOffX, uvOffY);
                if (hasCorners)
                {
                    _renderTarget.PopClip();
                }
            }
        }
        else if (effect is Media.Effects.ElementBlurEffect elementBlur)
        {
            if (elementBlur.Radius > 0)
                _renderTarget.DrawBlurEffect(x, y, w, h, (float)(elementBlur.Radius * scale), uvOffX, uvOffY);
        }
        else if (effect is Media.Effects.DropShadowEffect shadow)
        {
            var color = shadow.Color;
            var effectiveAlpha = (color.A / 255f) * (float)shadow.Opacity;
            MapEffectVector(space, shadow.OffsetX, shadow.OffsetY, out var offsetX, out var offsetY);
            _renderTarget.DrawDropShadowEffect(x, y, w, h,
                (float)(shadow.BlurRadius * scale),
                (float)offsetX,
                (float)offsetY,
                color.R / 255f, color.G / 255f, color.B / 255f,
                effectiveAlpha,
                uvOffX, uvOffY,
                cornerTL, cornerTR, cornerBR, cornerBL);
        }
        else if (effect is Media.Effects.OuterGlowEffect glow)
        {
            var color = glow.GlowColor;
            var effectiveAlpha = (color.A / 255f) * (float)glow.Opacity;
            _renderTarget.DrawOuterGlowEffect(x, y, w, h,
                (float)(glow.EffectiveBlurRadius * scale),
                color.R / 255f, color.G / 255f, color.B / 255f,
                effectiveAlpha, (float)glow.Intensity,
                uvOffX, uvOffY,
                cornerTL, cornerTR, cornerBR, cornerBL);
        }
        else if (effect is Media.Effects.InnerShadowEffect innerShadow)
        {
            var color = innerShadow.Color;
            var effectiveAlpha = (color.A / 255f) * (float)innerShadow.Opacity;
            MapEffectVector(space, innerShadow.OffsetX, innerShadow.OffsetY, out var offsetX, out var offsetY);
            _renderTarget.DrawInnerShadowEffect(x, y, w, h,
                (float)(innerShadow.BlurRadius * scale),
                (float)offsetX,
                (float)offsetY,
                color.R / 255f, color.G / 255f, color.B / 255f,
                effectiveAlpha,
                uvOffX, uvOffY,
                cornerTL, cornerTR, cornerBR, cornerBL);
        }
        else if (effect is Media.Effects.EmbossEffect emboss)
        {
            MapEffectVector(space, emboss.LightDirectionX, emboss.LightDirectionY, out var lightX, out var lightY);
            _renderTarget.DrawEmbossEffect(x, y, w, h,
                (float)emboss.Amount,
                (float)lightX,
                (float)lightY,
                (float)(emboss.Relief * scale));
        }
        else if (effect is Media.Effects.ColorMatrixEffect colorMatrix)
        {
            var m = colorMatrix.Matrix;
            Span<float> matrixData = stackalloc float[20];
            matrixData[0] = m.M11; matrixData[1] = m.M12; matrixData[2] = m.M13; matrixData[3] = m.M14; matrixData[4] = m.M15;
            matrixData[5] = m.M21; matrixData[6] = m.M22; matrixData[7] = m.M23; matrixData[8] = m.M24; matrixData[9] = m.M25;
            matrixData[10] = m.M31; matrixData[11] = m.M32; matrixData[12] = m.M33; matrixData[13] = m.M34; matrixData[14] = m.M35;
            matrixData[15] = m.M41; matrixData[16] = m.M42; matrixData[17] = m.M43; matrixData[18] = m.M44; matrixData[19] = m.M45;
            _renderTarget.DrawColorMatrixEffect(x, y, w, h, matrixData);
        }
        else if (effect is Media.Effects.ShaderEffect shaderEffect)
        {
            var pixelShader = shaderEffect.PixelShaderForRendering;
            var sourceHlsl = pixelShader?.SourceHlsl;
            if (!string.IsNullOrEmpty(sourceHlsl))
            {
                // Cross-backend path: HLSL source compiled at runtime. Required
                // for custom shaders to run on the Vulkan backend (it can't use
                // the DXBC bytecode path); D3D12 also honours it via D3DCompile.
                _renderTarget.DrawShaderEffectFromSource(x, y, w, h,
                    sourceHlsl,
                    shaderEffect.BuildConstantBuffer());
            }
            else
            {
                var shaderBytecode = pixelShader?.ShaderBytecode;
                if (shaderBytecode is { Length: > 0 })
                {
                    _renderTarget.DrawShaderEffect(x, y, w, h,
                        shaderBytecode,
                        shaderEffect.BuildConstantBuffer());
                }
                else
                {
                    _renderTarget.DrawBlurEffect(x, y, w, h, 0f, uvOffX, uvOffY);
                }
            }
        }
        else if (effect is Media.Effects.EffectGroup group)
        {
            // A group must never silently discard all but its first child. Each
            // supported child reads the same isolated capture and is dispatched in
            // declaration order. Preserve the original capture origin and corner
            // radii; resetting them to zero shifts sampling for padded effects and
            // was enough to make grouped shadows/glows disappear. The arguments are
            // already in native's drawing space — recurse into the core, not the
            // public entry, so the transform compensation is not applied twice.
            var activeEffects = group.ActiveEffects;
            for (int i = 0; i < activeEffects.Count; i++)
            {
                var child = activeEffects[i];
                ApplyElementEffectCore(child, x, y, w, h,
                    uvOffX, uvOffY,
                    space, scale,
                    cornerTL, cornerTR, cornerBR, cornerBL);
            }
        }
        else
        {
            // Unknown/custom Effect subclasses must degrade to an unmodified
            // composite. The element has already been redirected offscreen; doing
            // nothing here would make otherwise valid custom effects erase it.
            _renderTarget.DrawBlurEffect(x, y, w, h, 0f, uvOffX, uvOffY);
        }
        }
        finally
        {
            _effectApplicationPath.Remove(effect);
        }
    }

    /// <summary>
    /// Maps an effect offset VECTOR (no translation) through the linear part of
    /// <paramref name="space"/> — WPF row-vector convention, the same one
    /// <see cref="TransformRectAabb"/> uses.
    /// </summary>
    private static void MapEffectVector(Jalium.UI.Media.Matrix space, double vx, double vy,
        out double outX, out double outY)
    {
        outX = vx * space.M11 + vy * space.M21;
        outY = vx * space.M12 + vy * space.M22;
    }

    /// <inheritdoc />
    public override void Close()
    {
        if (_closed) return;
        _closed = true;
        ImageSource.GpuCacheEvictionRequested -= _gpuEvictionHandler;
        ImageSource.RasterChanged -= _rasterChangedHandler;

        // Nothing will drain the queue once this context stops getting frames, and every entry in
        // it is a strong reference to an application bitmap, so release them here. A raise that was
        // already in flight when the delegates came off can still add one afterwards; harmless, and
        // it is why QueueCacheEviction re-checks _closed under the same lock.
        lock (_pendingCacheEvictionLock)
        {
            _pendingCacheEvictions.Clear();
            _hasPendingCacheEvictions = false;
        }
        // Note: Don't dispose cached resources here - they may be reused
    }

    /// <summary>
    /// Number of GPU textures this context currently holds. Test seam for the eviction-threading
    /// contract: whether a queued eviction has been applied yet is not otherwise observable from
    /// outside, and that timing is the whole property under test.
    /// </summary>
    internal int CachedBitmapCount => _bitmapCache.Count;

    /// <summary>
    /// Vector rasterizations this context currently holds. Test seam for the same reason
    /// <see cref="CachedBitmapCount"/> is one: whether a cache-clearing call reached this
    /// dictionary is not observable from outside, and it did not for either of them.
    /// </summary>
    internal int CachedVectorDrawingCount => _vectorDrawingCache.Count;

    /// <summary>
    /// Clears all cached resources.
    /// </summary>
    public void ClearCache()
    {
        foreach (var brush in _brushCache.Values)
        {
            brush.Dispose();
        }
        _brushCache.Clear();

        foreach (var format in _textFormatCache.Values)
        {
            format.Dispose();
        }
        _textFormatCache.Clear();

        foreach (var entry in _bitmapCache.Values)
        {
            entry.Bitmap.Dispose();
        }
        _bitmapCache.Clear();
        _bitmapCacheBytes = 0;
        ClearVectorDrawingCache();
    }

    /// <summary>
    /// Clears only cached bitmaps. Useful during window teardown to quickly release
    /// large image resources while avoiding text/brush teardown order issues.
    /// </summary>
    public void ClearBitmapCache()
    {
        foreach (var entry in _bitmapCache.Values)
        {
            entry.Bitmap.Dispose();
        }

        _bitmapCache.Clear();
        _bitmapCacheBytes = 0;
        ClearVectorDrawingCache();
    }

    /// <summary>
    /// Drops every cached vector rasterization.
    /// </summary>
    /// <remarks>
    /// Belongs with the bitmap cache in both callers above and was missing from both. Each entry
    /// holds a full-size <see cref="BitmapImage"/> raster — the thing a teardown or a low-memory
    /// purge is trying to release — and, once the uploads it depended on have been disposed, an
    /// entry that survives is a strong reference to pixels nothing is drawing. Correctness rests on
    /// the same argument as the eviction path: the raster is reproducible, so dropping it can only
    /// cost one re-rasterization on the next frame that needs it. Neither caller runs per frame
    /// (window/popup teardown, render-thread handover, and the low-memory notification), so that
    /// cost is not on any hot path.
    /// </remarks>
    private void ClearVectorDrawingCache() => _vectorDrawingCache.Clear();

    /// <summary>
    /// Trims caches if they exceed their maximum size.
    /// Call this after each frame to prevent memory from growing unbounded.
    /// </summary>
    public void TrimCacheIfNeeded()
    {
        if (_brushCache.Count > MaxBrushCacheSize)
        {
            // LRU eviction: remove the least recently used half
            var toRemove = _brushCache
                .OrderBy(static kvp => kvp.Value.LastAccessSequence)
                .Take(_brushCache.Count / 2)
                .ToList();
            foreach (var kvp in toRemove)
            {
                kvp.Value.Dispose();
                _brushCache.Remove(kvp.Key);
            }
        }

        if (_textFormatCache.Count > MaxTextFormatCacheSize)
        {
            // LRU eviction: remove least recently used half
            var toRemove = _textFormatCache
                .OrderBy(static kvp => kvp.Value.LastAccessSequence)
                .Take(_textFormatCache.Count / 2)
                .ToList();
            foreach (var kvp in toRemove)
            {
                kvp.Value.Dispose();
                _textFormatCache.Remove(kvp.Key);
            }
        }

        TrimBitmapCacheIfNeeded();
    }

    private NativeBrush? GetNativeBrush(Brush brush)
        => GetNativeBrush(brush, 0, 0, 0, 0);

    private NativeBrush? GetNativeBrush(Brush brush, float bx, float by, float bw, float bh)
    {
        if (brush == null) return null;

        if (brush is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            double opacity = Math.Clamp(solidBrush.Opacity, 0.0, 1.0);
            // Cache based on (brush reference, current color) to invalidate
            // when the same brush object's color or opacity changes.
            if (_brushCache.TryGetValue(brush, out var cached))
            {
                if (cached.CachedColor == color &&
                    cached.CachedOpacity == opacity)
                {
                    cached.LastAccessSequence = ++_brushCacheSequence;
                    return cached;
                }
                // Color changed — dispose old native brush and recreate
                cached.Dispose();
                _brushCache.Remove(brush);
            }

            // Pass sRGB values to native: D2D expects sRGB, and the direct D3D12
            // path converts to linear internally (SRGB RTV handles gamma).
            var nb = _context.CreateSolidBrush(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f,
                EffectiveBrushAlpha(color.A, opacity));
            nb.CachedColor = color;
            nb.CachedOpacity = opacity;
            nb.LastAccessSequence = ++_brushCacheSequence;
            _brushCache[brush] = nb;
            return nb;
        }

        if (brush is LinearGradientBrush linear)
        {
            long contentHash = linear.ComputeContentHash();
            long boundsKey =
                linear.MappingMode == BrushMappingMode.RelativeToBoundingBox
                    ? ComputeGradientBoundsKey(bx, by, bw, bh)
                    : 0;
            if (_brushCache.TryGetValue(brush, out var cachedLinear) &&
                cachedLinear.CachedGradientContentHash == contentHash &&
                cachedLinear.CachedBoundsKey == boundsKey)
            {
                cachedLinear.LastAccessSequence = ++_brushCacheSequence;
                return cachedLinear;
            }
            return CreateNativeLinearGradient(linear, bx, by, bw, bh);
        }

        if (brush is RadialGradientBrush radial)
        {
            long contentHash = radial.ComputeContentHash();
            long boundsKey =
                radial.MappingMode == BrushMappingMode.RelativeToBoundingBox
                    ? ComputeGradientBoundsKey(bx, by, bw, bh)
                    : 0;
            if (_brushCache.TryGetValue(brush, out var cachedRadial) &&
                cachedRadial.CachedGradientContentHash == contentHash &&
                cachedRadial.CachedBoundsKey == boundsKey)
            {
                cachedRadial.LastAccessSequence = ++_brushCacheSequence;
                return cachedRadial;
            }
            return CreateNativeRadialGradient(radial, bx, by, bw, bh);
        }

        if (brush is ImageBrush imageBrush)
        {
            // Stroke fallback: degrade an ImageBrush stroke to a SolidColorBrush
            // approximating the average pixel of the image. The fill path uses
            // the dedicated TryFill*AsImageBrush helpers, which clip + tile the
            // bitmap directly and never reach this method for the fill brush.
            return GetImageBrushStrokeFallback(imageBrush);
        }

        return null;
    }

    /// <summary>
    /// Returns a <see cref="NativeBrush"/> approximating the average color of
    /// <paramref name="imageBrush"/>'s source. Used as a graceful degradation
    /// for code paths that paint a stroke or compound shape with an ImageBrush —
    /// real bitmap-tiled strokes need a path-clipped opacity-mask backend that
    /// the framework does not expose yet, so a flat-color stand-in keeps the
    /// silhouette visible instead of dropping the stroke entirely.
    /// </summary>
    /// <remarks>
    /// Cached on the brush instance and invalidated when the underlying
    /// <see cref="ImageSource"/> reference changes — a single ImageBrush whose
    /// source is reassigned (e.g. for a sprite swap) re-samples on the next
    /// stroke, while a shared brush keeps reusing the cached native solid.
    /// </remarks>
    private NativeBrush? GetImageBrushStrokeFallback(ImageBrush imageBrush)
    {
        if (_brushCache.TryGetValue(imageBrush, out var cached))
        {
            // CachedSourceRef tracks the ImageSource the brush was sampled from.
            // When the brush points at a different source, drop the stale entry
            // and re-sample below; otherwise reuse the existing native solid.
            if (ReferenceEquals(cached.CachedImageSource, imageBrush.ImageSource))
            {
                cached.LastAccessSequence = ++_brushCacheSequence;
                return cached;
            }
            cached.Dispose();
            _brushCache.Remove(imageBrush);
        }

        var color = SampleAverageColor(imageBrush.ImageSource) ?? Color.FromArgb(0, 0, 0, 0);
        if (color.A == 0)
        {
            return null;
        }

        var alpha = color.A / 255f * (float)imageBrush.Opacity;
        var nb = _context.CreateSolidBrush(color.R / 255f, color.G / 255f, color.B / 255f, alpha);
        nb.CachedColor = Color.FromArgb((byte)(alpha * 255f), color.R, color.G, color.B);
        nb.CachedImageSource = imageBrush.ImageSource;
        nb.LastAccessSequence = ++_brushCacheSequence;
        _brushCache[imageBrush] = nb;
        return nb;
    }

    private static Color? SampleAverageColor(ImageSource? source)
    {
        // Through the snapshot so the buffer cannot be replaced by a decode worker between the
        // length check and the sampling loop. No decode is requested: this is the stroke-fallback
        // colour for a brush whose source has already been resolved elsewhere.
        if (source is BitmapImage bitmap &&
            bitmap.TryGetPixelSnapshot(out var snapshot) &&
            snapshot is { Pixels.Length: >= 4 })
        {
            var pixels = snapshot.Pixels;

            // Pixels are BGRA8 stored as Pbgra32. Sample on a coarse grid to
            // keep this O(1) — full-image averaging on a 4K texture is wasted
            // work for a fallback color.
            const int MaxSamples = 64;
            int totalPixels = pixels.Length / 4;
            int step = Math.Max(1, totalPixels / MaxSamples);

            long sumB = 0, sumG = 0, sumR = 0, sumA = 0;
            int count = 0;
            for (int i = 0; i < totalPixels; i += step)
            {
                int off = i * 4;
                sumB += pixels[off];
                sumG += pixels[off + 1];
                sumR += pixels[off + 2];
                sumA += pixels[off + 3];
                count++;
            }

            if (count == 0) return null;
            return Color.FromArgb(
                (byte)(sumA / count),
                (byte)(sumR / count),
                (byte)(sumG / count),
                (byte)(sumB / count));
        }

        return null;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  ImageBrush fill: clip the shape, tile the bitmap.
    //
    //  The native brush API only knows solid + gradient brushes, so an
    //  ImageBrush fill cannot be passed through GetNativeBrush — we reach
    //  for the bitmap pipeline instead. The shape's geometry becomes a
    //  clip layer; TileBrushHelper lays out the bitmap tiles inside that
    //  clip according to the brush's Viewport / Viewbox / Stretch / Tile
    //  / Alignment properties; each tile is one DrawBitmap call (with an
    //  optional flip transform around the tile center for FlipX / FlipY /
    //  FlipXY tile modes).
    //
    //  Each TryFill*AsImageBrush helper returns true if it claims the brush —
    //  i.e. the call site must NOT fall through to GetNativeBrush. The
    //  return value does NOT signal that pixels were actually emitted: a
    //  null ImageSource or a degenerate viewport is still a successful
    //  "claim" because the alternative (a black SolidColorBrush stand-in)
    //  is worse than nothing.

    /// <summary>
    /// Encodes which native clip primitive bounds an ImageBrush fill. Avoids
    /// allocating a per-call closure on the rendering hot path by passing the
    /// shape parameters by-value.
    /// </summary>
    private enum ImageBrushClipKind
    {
        /// <summary>Axis-aligned rectangular clip via <c>PushClip</c>.</summary>
        Rect,
        /// <summary>Rounded-rectangle clip via <c>PushRoundedRectClip</c>.</summary>
        RoundedRect,
        /// <summary>Ellipse clip — degenerate rounded-rect with rx = w/2, ry = h/2.</summary>
        Ellipse
    }

    private bool TryFillRectangleAsImageBrush(Brush? brush, float x, float y, float w, float h)
    {
        if (brush is not ImageBrush imageBrush) return false;
        FillImageBrushTiles(imageBrush, new Rect(x, y, w, h),
            ImageBrushClipKind.Rect, x, y, w, h, 0, 0);
        return true;
    }

    private bool TryFillRoundedRectangleAsImageBrush(Brush? brush,
        float x, float y, float w, float h, float rx, float ry)
    {
        if (brush is not ImageBrush imageBrush) return false;
        var kind = (rx > 0 || ry > 0) ? ImageBrushClipKind.RoundedRect : ImageBrushClipKind.Rect;
        FillImageBrushTiles(imageBrush, new Rect(x, y, w, h),
            kind, x, y, w, h, rx, ry);
        return true;
    }

    private bool TryFillPerCornerRoundedAsImageBrush(Brush? brush,
        float x, float y, float w, float h,
        float tl, float tr, float br, float bl)
    {
        if (brush is not ImageBrush imageBrush) return false;
        // Native side has no 4-corner clip primitive — degrade to a uniform
        // clip using the largest corner radius. For the common case where
        // all four corners are equal this is exact; otherwise the clip is
        // looser than the rendered border but never tighter (image never
        // bleeds past the visible border edge).
        var maxR = Math.Max(Math.Max(tl, tr), Math.Max(br, bl));
        var kind = maxR > 0 ? ImageBrushClipKind.RoundedRect : ImageBrushClipKind.Rect;
        FillImageBrushTiles(imageBrush, new Rect(x, y, w, h),
            kind, x, y, w, h, maxR, maxR);
        return true;
    }

    private bool TryFillEllipseAsImageBrush(Brush? brush,
        float cx, float cy, float radiusX, float radiusY)
    {
        if (brush is not ImageBrush imageBrush) return false;
        var x = cx - radiusX;
        var y = cy - radiusY;
        var w = radiusX * 2;
        var h = radiusY * 2;
        // PushRoundedRectClip with rx=radiusX, ry=radiusY on a (2r × 2r) box
        // is exactly an ellipse — the native rounded-rect clip degenerates
        // to a pure ellipse path when corner radius equals half-extent.
        FillImageBrushTiles(imageBrush, new Rect(x, y, w, h),
            ImageBrushClipKind.Ellipse, x, y, w, h, radiusX, radiusY);
        return true;
    }

    private bool TryFillPathAsImageBrush(Brush? brush, Rect geoBoundsScreen)
    {
        if (brush is not ImageBrush imageBrush) return false;
        if (geoBoundsScreen.Width <= 0 || geoBoundsScreen.Height <= 0) return true;

        // Arbitrary path geometry has no clip primitive on the native side —
        // bound the image to the path's bounding box. The result fills more
        // pixels than the path geometry for non-rectangular paths, but keeps
        // the brush visible (and is correct for the common rectangle/rounded
        // /ellipse paths that already route through their dedicated helpers).
        FillImageBrushTiles(imageBrush, geoBoundsScreen,
            ImageBrushClipKind.Rect,
            (float)geoBoundsScreen.X, (float)geoBoundsScreen.Y,
            (float)geoBoundsScreen.Width, (float)geoBoundsScreen.Height,
            0, 0);
        return true;
    }

    /// <summary>
    /// Common ImageBrush fill driver: resolves the source bitmap, computes the
    /// tile placements, pushes the shape clip described by
    /// (<paramref name="clipKind"/>, <paramref name="clipX"/>, <paramref name="clipY"/>,
    /// <paramref name="clipW"/>, <paramref name="clipH"/>, <paramref name="clipRx"/>,
    /// <paramref name="clipRy"/>), and emits one DrawBitmap call per tile (with a flip
    /// transform when the tile mode requires it). Bails out cleanly if the source is
    /// missing or the layout produces no tiles — the shape clip is pushed only after
    /// we know there is something to draw, so callers always get matched push/pop pairs.
    /// </summary>
    private void FillImageBrushTiles(ImageBrush imageBrush, Rect shapeBounds,
        ImageBrushClipKind clipKind,
        float clipX, float clipY, float clipW, float clipH,
        float clipRx, float clipRy)
    {
        if (imageBrush.ImageSource is null) return;

        // shapeBounds is already in this context's drawing space (callers add Offset before
        // handing it over), so it converts to device pixels exactly like a DrawImage rect. Cover
        // mode matters to the bucket resolver: UniformToFill has to satisfy the LARGER axis ratio,
        // and asking for a contain-sized bucket there produces a visibly soft fill.
        GetTransformScale(out var sx, out var sy);
        var (hintW, hintH) = ToDeviceHint(
            (int)Math.Ceiling(shapeBounds.Width * sx),
            (int)Math.Ceiling(shapeBounds.Height * sy));

        var nativeBitmap = GetNativeBitmap(
            imageBrush.ImageSource,
            hintW,
            hintH,
            hintCover: imageBrush.Stretch == Stretch.UniformToFill);
        if (nativeBitmap is null) return;

        var imgW = (double)nativeBitmap.Width;
        var imgH = (double)nativeBitmap.Height;
        if (imgW <= 0 || imgH <= 0) return;

        var placements = TileBrushHelper.ComputeTilePlacements(imageBrush, shapeBounds, imgW, imgH);
        if (placements.Count == 0) return;

        var opacity = (float)Math.Clamp(imageBrush.Opacity, 0.0, 1.0);
        if (opacity <= 0) return;

        var scalingMode = BitmapScalingMode.Unspecified;

        // For the common default case (Viewport == shape, TileMode.None,
        // Stretch.Fill, no Viewbox crop) the single placement's ClipRect
        // equals shapeBounds — the per-tile clip would be redundant. Detect
        // and skip it to halve native PushClip / PopClip pressure on the
        // hot path (Border.Background = ImageBrush is by far the most
        // common scenario).
        bool perTileClipNeeded = placements.Count > 1 ||
            !RectsApproximatelyEqual(placements[0].ClipRect, shapeBounds);

        PushImageBrushShapeClip(clipKind, clipX, clipY, clipW, clipH, clipRx, clipRy);
        try
        {
            for (int i = 0; i < placements.Count; i++)
            {
                DrawImageBrushTile(nativeBitmap, placements[i], opacity, scalingMode, perTileClipNeeded);
            }
        }
        finally
        {
            _renderTarget.PopClip();
        }
    }

    private void PushImageBrushShapeClip(ImageBrushClipKind kind,
        float x, float y, float w, float h, float rx, float ry)
    {
        switch (kind)
        {
            case ImageBrushClipKind.RoundedRect:
            case ImageBrushClipKind.Ellipse:
                _renderTarget.PushRoundedRectClip(x, y, w, h, rx, ry);
                break;
            case ImageBrushClipKind.Rect:
            default:
                _renderTarget.PushClip(x, y, w, h);
                break;
        }
    }

    // Reusable matrix buffer for per-tile flip transforms — keeps the inner
    // tile loop allocation-free. Row layout matches PushTransform's contract:
    // [m11, m12, m21, m22, dx, dy].
    private readonly float[] _imageBrushFlipMatrix = new float[6];

    private void DrawImageBrushTile(NativeBitmap bitmap, TileBrushHelper.TilePlacement placement,
        float opacity, BitmapScalingMode scalingMode, bool pushTileClip)
    {
        var clip = placement.ClipRect;
        var dst = placement.ImageDestRect;

        // Tile out of the screen entirely — skip the native call.
        if (clip.Width <= 0 || clip.Height <= 0) return;
        if (dst.Width <= 0 || dst.Height <= 0) return;

        if (pushTileClip)
        {
            _renderTarget.PushClip((float)clip.X, (float)clip.Y, (float)clip.Width, (float)clip.Height);
        }

        bool needsFlip = placement.FlipX || placement.FlipY;
        if (needsFlip)
        {
            // Flip about the tile's center so the bitmap stays inside its
            // clip rectangle. The native compose rule is `new = old * incoming`,
            // so this matrix is applied to the bitmap-local coords first and
            // composes correctly with any caller-pushed transform on top.
            var cx = (float)(clip.X + clip.Width * 0.5);
            var cy = (float)(clip.Y + clip.Height * 0.5);
            var sx = placement.FlipX ? -1f : 1f;
            var sy = placement.FlipY ? -1f : 1f;
            var dx = placement.FlipX ? 2f * cx : 0f;
            var dy = placement.FlipY ? 2f * cy : 0f;
            var m = _imageBrushFlipMatrix;
            m[0] = sx; m[1] = 0f;
            m[2] = 0f; m[3] = sy;
            m[4] = dx; m[5] = dy;
            _renderTarget.PushTransform(m);
        }

        try
        {
            _renderTarget.DrawBitmap(bitmap,
                (float)dst.X, (float)dst.Y,
                (float)dst.Width, (float)dst.Height,
                opacity,
                scalingMode);
        }
        finally
        {
            if (needsFlip) _renderTarget.PopTransform();
            if (pushTileClip) _renderTarget.PopClip();
        }
    }

    private static bool RectsApproximatelyEqual(Rect a, Rect b)
    {
        const double Eps = 0.5; // half-pixel tolerance — clip rects are pixel-snapped on the native side
        return Math.Abs(a.X - b.X) < Eps &&
               Math.Abs(a.Y - b.Y) < Eps &&
               Math.Abs(a.Width - b.Width) < Eps &&
               Math.Abs(a.Height - b.Height) < Eps;
    }

    internal static float EffectiveBrushAlpha(byte alpha, double opacity)
    {
        return alpha / 255f * (float)Math.Clamp(opacity, 0.0, 1.0);
    }

    internal static float[] MarshalGradientStops(
        IList<GradientStop> stops,
        double opacity)
    {
        var arr = new float[stops.Count * 5];
        for (int i = 0; i < stops.Count; i++)
        {
            var s = stops[i];
            int off = i * 5;
            arr[off] = (float)s.Offset;
            // Pass sRGB values: D2D D2D1_GAMMA_2_2 expects sRGB, and the
            // software backend's InterpolateGradientStops handles sRGB↔linear.
            arr[off + 1] = s.Color.R / 255f;
            arr[off + 2] = s.Color.G / 255f;
            arr[off + 3] = s.Color.B / 255f;
            arr[off + 4] = EffectiveBrushAlpha(s.Color.A, opacity);
        }
        return arr;
    }

    private static long ComputeGradientBoundsKey(
        float bx,
        float by,
        float bw,
        float bh)
    {
        const long FnvOffsetBasis =
            unchecked((long)0xcbf29ce484222325UL);
        const long FnvPrime =
            unchecked((long)0x100000001b3UL);

        long hash = FnvOffsetBasis;
        hash = unchecked(
            (hash ^ BitConverter.SingleToInt32Bits(bx)) * FnvPrime);
        hash = unchecked(
            (hash ^ BitConverter.SingleToInt32Bits(by)) * FnvPrime);
        hash = unchecked(
            (hash ^ BitConverter.SingleToInt32Bits(bw)) * FnvPrime);
        hash = unchecked(
            (hash ^ BitConverter.SingleToInt32Bits(bh)) * FnvPrime);
        return hash == 0 ? 1 : hash;
    }

    private NativeBrush? CreateNativeLinearGradient(LinearGradientBrush brush,
        float bx, float by, float bw, float bh)
    {
        if (brush.GradientStops.Count == 0)
            return null;

        float sx, sy, ex, ey;
        if (brush.MappingMode == BrushMappingMode.RelativeToBoundingBox)
        {
            sx = bx + (float)brush.StartPoint.X * bw;
            sy = by + (float)brush.StartPoint.Y * bh;
            ex = bx + (float)brush.EndPoint.X * bw;
            ey = by + (float)brush.EndPoint.Y * bh;
        }
        else
        {
            sx = (float)brush.StartPoint.X;
            sy = (float)brush.StartPoint.Y;
            ex = (float)brush.EndPoint.X;
            ey = (float)brush.EndPoint.Y;
        }

        // Guard against degenerate gradient line (start == end).
        if (sx == ex && sy == ey)
            return null;

        var stops = MarshalGradientStops(
            brush.GradientStops,
            brush.Opacity);
        var nb = _context.CreateLinearGradientBrush(sx, sy, ex, ey, stops, (uint)brush.GradientStops.Count, (uint)brush.SpreadMethod);
        if (!nb.IsValid)
        {
            nb.Dispose();
            return null;
        }

        nb.LastAccessSequence = ++_brushCacheSequence;
        nb.CachedGradientContentHash = brush.ComputeContentHash();
        nb.CachedBoundsKey =
            brush.MappingMode == BrushMappingMode.RelativeToBoundingBox
                ? ComputeGradientBoundsKey(bx, by, bw, bh)
                : 0;

        // Replace previous cached entry if any
        if (_brushCache.TryGetValue(brush, out var old))
            old.Dispose();
        _brushCache[brush] = nb;
        return nb;
    }

    private NativeBrush? CreateNativeRadialGradient(RadialGradientBrush brush,
        float bx, float by, float bw, float bh)
    {
        if (brush.GradientStops.Count == 0)
            return null;

        float cx, cy, rx, ry, ox, oy;
        if (brush.MappingMode == BrushMappingMode.RelativeToBoundingBox)
        {
            cx = bx + (float)brush.Center.X * bw;
            cy = by + (float)brush.Center.Y * bh;
            rx = (float)brush.RadiusX * bw;
            ry = (float)brush.RadiusY * bh;
            ox = bx + (float)brush.GradientOrigin.X * bw;
            oy = by + (float)brush.GradientOrigin.Y * bh;
        }
        else
        {
            cx = (float)brush.Center.X;
            cy = (float)brush.Center.Y;
            rx = (float)brush.RadiusX;
            ry = (float)brush.RadiusY;
            ox = (float)brush.GradientOrigin.X;
            oy = (float)brush.GradientOrigin.Y;
        }

        var stops = MarshalGradientStops(
            brush.GradientStops,
            brush.Opacity);
        var nb = _context.CreateRadialGradientBrush(cx, cy, rx, ry, ox, oy, stops, (uint)brush.GradientStops.Count, (uint)brush.SpreadMethod);
        if (!nb.IsValid)
        {
            nb.Dispose();
            return null;
        }

        nb.LastAccessSequence = ++_brushCacheSequence;
        nb.CachedGradientContentHash = brush.ComputeContentHash();
        nb.CachedBoundsKey =
            brush.MappingMode == BrushMappingMode.RelativeToBoundingBox
                ? ComputeGradientBoundsKey(bx, by, bw, bh)
                : 0;

        // Replace previous cached entry if any
        if (_brushCache.TryGetValue(brush, out var old))
            old.Dispose();
        _brushCache[brush] = nb;
        return nb;
    }

    private NativeTextFormat? GetTextFormat(
        string fontFamily,
        double fontSize,
        int fontWeight,
        int fontStyle,
        int textRenderingMode,
        int textFormattingMode,
        int textHintingMode,
        bool subpixelPositioning = false)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            fontFamily = FrameworkElement.DefaultFontFamilyName;
        }

        if (double.IsNaN(fontSize) || double.IsInfinity(fontSize) || fontSize <= 0)
        {
            fontSize = 12;
        }

        // TextOptions modes are part of the cache key — the native format
        // stores them, and two elements asking for the same family/size/weight
        // /style but different rendering/hinting modes (e.g. an authoring
        // canvas next to a Grayscale text panel inside a ClearType chrome)
        // must NOT share one cached handle: the second caller would otherwise
        // silently get the first caller's mode.
        var key = new TextFormatCacheKey(
            fontFamily,
            fontSize,
            fontWeight,
            fontStyle,
            textRenderingMode,
            textFormattingMode,
            textHintingMode,
            subpixelPositioning);

        if (_textFormatCache.TryGetValue(key, out var cached) && cached.IsValid)
        {
            cached.LastAccessSequence = ++_textFormatCacheSequence;
            return cached;
        }

        // Resolve the family STACK the same way measurement does. Both paths must land on the
        // same typeface or the layout is computed for one font and the glyphs drawn in another:
        // DirectWrite does not reject an unknown family here, it quietly substitutes a default,
        // so the mismatch is silent and shows up only as text overflowing its own measured box
        // (a trailing glyph clipped by any container sized to that box).
        var format = TextMeasurement.CreateTextFormatFromFamilyList(
            _context, fontFamily, (float)fontSize, fontWeight, fontStyle);
        if (format != null)
        {
            // Push the resolved TextOptions modes into the freshly created
            // native format. These are per-format on the native side, stored
            // on the TextFormat base class; the backend reads them on every
            // DrawText (D3D12 glyph atlas keys off them; Vulkan maps to
            // LOGFONT.lfQuality). Calling the setter on Auto / Ideal / Auto
            // is harmless because the native side just stores the value.
            format.SetTextRenderingMode(textRenderingMode);
            format.SetTextFormattingMode(textFormattingMode);
            format.SetTextHintingMode(textHintingMode);
            // Sub-pixel positioning is a placement policy derived by DrawText from
            // TextFormattingMode (Ideal → on, Display → off) plus the live-scale
            // branch, which always asks for it. It is part of the cache key above,
            // so a Display-mode label and an Ideal-mode label with the same font
            // never share a native format.
            if (subpixelPositioning)
            {
                format.SetSubpixelPositioning(true);
            }
            format.LastAccessSequence = ++_textFormatCacheSequence;
            _textFormatCache[key] = format;
        }

        return format;
    }

    /// <summary>
    /// Effective per-axis scale of the transform currently pushed on this context.
    /// </summary>
    /// <remarks>
    /// <c>_currentNativeMatrix</c> is [m11, m12, m21, m22, dx, dy] and mirrors exactly the
    /// in-tree PushTransform stack — RenderTransform, LayoutTransform, ScrollViewer zoom. It does
    /// NOT include the render target's DPI scale, which the native side applies on its own; see
    /// <see cref="ToDeviceHint"/> for the conversion that does.
    /// </remarks>
    private void GetTransformScale(out double scaleX, out double scaleY)
    {
        scaleX = Math.Sqrt(_currentNativeMatrix[0] * _currentNativeMatrix[0]
                         + _currentNativeMatrix[1] * _currentNativeMatrix[1]);
        scaleY = Math.Sqrt(_currentNativeMatrix[2] * _currentNativeMatrix[2]
                         + _currentNativeMatrix[3] * _currentNativeMatrix[3]);
        if (!(scaleX > 0) || !double.IsFinite(scaleX)) scaleX = 1;
        if (!(scaleY > 0) || !double.IsFinite(scaleY)) scaleY = 1;
    }

    /// <summary>
    /// Converts a transform-space size into the device-pixel decode hint <see cref="GetNativeBitmap"/>
    /// takes, by folding in this render target's DPI scale.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the hint does not come from <c>FrameworkElement.LayoutDpiScale</c>:
    /// that is a process-global static assigned by whichever window last handled a DPI change, so
    /// on a mixed-DPI multi-monitor desktop every window but one asks for the wrong number of
    /// pixels. The render target owns the DPI of the monitor it is actually presenting to.
    /// </remarks>
    private (int Width, int Height) ToDeviceHint(int transformSpaceWidth, int transformSpaceHeight)
    {
        if (transformSpaceWidth <= 0 || transformSpaceHeight <= 0)
        {
            return (0, 0);
        }

        var dpiX = _renderTarget.DpiScaleX;
        var dpiY = _renderTarget.DpiScaleY;
        if (!(dpiX > 0) || !double.IsFinite(dpiX)) dpiX = 1;
        if (!(dpiY > 0) || !double.IsFinite(dpiY)) dpiY = 1;

        // Clamped to the same ceiling RequestDecode clamps to, so an absurd transform cannot ask
        // the decoder for a buffer nothing could allocate.
        return (
            (int)Math.Clamp(Math.Ceiling(transformSpaceWidth * dpiX), 1d, 16384d),
            (int)Math.Clamp(Math.Ceiling(transformSpaceHeight * dpiY), 1d, 16384d));
    }

    /// <summary>
    /// Stable identity for an image source in diagnostics records, without forcing every source
    /// type to grow a diagnostics API.
    /// </summary>
    private static string DescribeImageSource(ImageSource imageSource) =>
        imageSource switch
        {
            BitmapImage bitmap => bitmap.DiagnosticSourceName,
            _ => imageSource.GetType().Name,
        };

    /// <summary>
    /// Resolves the GPU texture for an image source — and, for a deferred source that has not
    /// decoded yet, REQUESTS that decode on the way through.
    /// </summary>
    /// <param name="imageSource">The source to realize.</param>
    /// <param name="hintPixelWidth">
    /// Device-pixel width the caller is about to draw into, or 0 when unknown. Advisory: it feeds
    /// the display-bucket ladder, which only ever grows, so an under-estimate costs sharpness on
    /// one frame and an over-estimate costs resident bytes.
    /// </param>
    /// <param name="hintPixelHeight">Device-pixel height, same contract as
    /// <paramref name="hintPixelWidth"/>.</param>
    /// <param name="hintCover">
    /// True when the caller will scale the image to COVER its rect (<c>Stretch.UniformToFill</c>),
    /// so the bucket must satisfy the larger axis ratio rather than the smaller one.
    /// </param>
    /// <remarks>
    /// <para>This method is the single choke point every bitmap consumer funnels through —
    /// <c>DrawImage</c>, the <c>ImageBrush</c> tile filler, the vector raster cache, animated
    /// frames — which is exactly why the decode request belongs here and nowhere else. It used to
    /// live in <c>Image.OnRender</c>, so an <c>ImageBrush</c>, an <c>ImageDrawing</c> or a
    /// <c>Shape.Fill</c> backed by a URI bitmap had NOTHING in the process that would ever ask for
    /// its pixels: this method saw a pending deferred source, returned null, and did so again on
    /// every subsequent frame forever. Permanently blank, with no error anywhere.</para>
    /// <para>The size hints come from the caller's true device rect (its draw rect folded through
    /// the live transform stack and the render target's own DPI), never from a process-global DPI
    /// scale. A process-global is wrong under a RenderTransform, wrong under ScrollViewer zoom,
    /// and wrong for every window that is not on the primary monitor.</para>
    /// </remarks>
    private NativeBitmap? GetNativeBitmap(
        ImageSource imageSource,
        int hintPixelWidth = 0,
        int hintPixelHeight = 0,
        bool hintCover = false)
    {
        if (imageSource == null) return null;

        // The choke point every bitmap consumer funnels through, which is why the "still in use"
        // stamp belongs here as well as in DrawImage: an ImageBrush tile fill, an ImageDrawing or a
        // Shape.Fill backed by the same source reaches this method and nothing else, so stamping
        // only at DrawImage would leave a brush-painted image unprotected from an idle element that
        // shares its source.
        imageSource.MarkDrawn();

        // Animated bitmaps are just a sequence of BitmapImage frames + a timer.
        // Forward to whichever BitmapImage frame is currently displayed so each
        // frame gets its own cache entry (and gets evicted naturally as the
        // animation rotates through frames). The frame timer drives
        // FrameChanged → InvalidateVisual on the host control, which re-enters
        // this path with the new CurrentFrame on the next render pass.
        if (imageSource is Jalium.UI.Media.AnimatedBitmap animated)
        {
            var current = animated.CurrentFrame;
            return current != null
                ? GetNativeBitmap(current, hintPixelWidth, hintPixelHeight, hintCover)
                : null;
        }

        // A deferred BitmapImage whose encoded bytes turned out to hold several frames carries an
        // internal AnimatedBitmap substitute, discovered by the metadata probe. Forwarding to it
        // here — through the same path a top-level AnimatedBitmap takes — is what makes
        // <Image Source="cat.gif"/> animate for EVERY consumer, without the framework ever writing
        // to the application's Source property. ImageSourceLoader could only make that swap when
        // the encoded bytes were already materialised, which a deferred source's never are, so the
        // XAML type converter's own path always produced a static first frame.
        //
        // No recursion risk: the substitute's frames are eager BitmapImages built from decoded
        // pixels, so they carry no substitute of their own.
        if (imageSource is BitmapImage { AnimatedSubstitute: { } animatedSubstitute })
        {
            return GetNativeBitmap(animatedSubstitute, hintPixelWidth, hintPixelHeight, hintCover);
        }

        // Reference identity is not enough for ANY source whose pixels can be replaced in place:
        // a rewritten WriteableBitmap and a BitmapImage that upgraded its display bucket both keep
        // the same instance. ContentGeneration is the one stamp that covers both (and reads 0 for
        // genuinely immutable sources, which compares equal forever exactly as before).
        var currentGeneration = imageSource.ContentGeneration;

        if (_bitmapCache.TryGetValue(imageSource, out var cached))
        {
            bool stale = cached.ContentGeneration != currentGeneration;

            if (!stale && cached.Bitmap.IsValid)
            {
                cached.LastAccessSequence = ++_bitmapCacheSequence;
                cached.LastFrameUsed = _currentFrameId;
                return cached.Bitmap;
            }

            // Hot path: WriteableBitmap content changed but the native bitmap is still
            // valid AND has the same dimensions → update pixels in place. D3D12 reuses
            // the default-heap texture (skipping CreateCommittedResource per frame),
            // Vulkan just rewrites the staging pixel buffer. This is what keeps the
            // swap chain stable when video streams 1080p frames at 30+fps.
            if (cached.Bitmap.IsValid &&
                imageSource is WriteableBitmap writeableUpdate &&
                cached.Bitmap.Width == (uint)writeableUpdate.PixelWidth &&
                cached.Bitmap.Height == (uint)writeableUpdate.PixelHeight &&
                cached.Bitmap.TryUpdatePixels(
                    writeableUpdate.BackBufferArray,
                    writeableUpdate.PixelWidth,
                    writeableUpdate.PixelHeight,
                    writeableUpdate.BackBufferStride))
            {
                cached.ContentGeneration = currentGeneration;
                cached.LastAccessSequence = ++_bitmapCacheSequence;
                cached.LastFrameUsed = _currentFrameId;
                return cached.Bitmap;
            }

            RemoveBitmapCacheEntry(imageSource, cached);
        }

        NativeBitmap? nativeBitmap = null;

        // Stamp the entry with the generation of the pixels ACTUALLY uploaded, not with the value
        // read at method entry. A publish that lands between the two makes the two differ, and
        // stamping the newer one would mark the older texture current — a permanently stale
        // upload. Stamping the older one costs one extra frame, and the compare above corrects it.
        var uploadedGeneration = currentGeneration;

        if (imageSource is BitmapImage bitmapImage)
        {
            BitmapPixelSnapshot? snapshot = null;
            try
            {
                // THE decode driver. Every bitmap consumer in the framework reaches this line, so
                // this is where "somebody wants these pixels, at about this size" is recorded —
                // not in Image.OnRender, which only exists for one of those consumers.
                //
                // Idempotent and cheap: a non-deferred source returns immediately, and a deferred
                // one only enqueues work when the request can reach a strictly larger display
                // bucket than the one already published. Calling it once per draw is therefore a
                // lock acquisition, not a decode.
                //
                // Gated on a known axis, and that gate is load-bearing. An all-zero request is not
                // "no request" to the deferred decoder — it means "the caller does not know the
                // size, so it may need every pixel", and it resolves to the source's NATURAL size.
                // Since a display bucket may only ever grow, forwarding a degenerate frame's (0,0)
                // once would pin the full-resolution raster resident for the life of the source. A
                // renderer always knows its own rect, so (0,0) here is never an honest request for
                // full resolution; the only caller entitled to make that request is layout, which
                // does so explicitly through Image.RequestBitmapDecode.
                if (hintPixelWidth > 0 || hintPixelHeight > 0)
                {
                    bitmapImage.RequestDecode(hintPixelWidth, hintPixelHeight, hintCover);
                }

                if (!bitmapImage.TryGetPixelSnapshot(out snapshot) || snapshot is null)
                {
                    // No pixels yet. For a deferred source that is simply "the decode is still in
                    // flight" and the request above is what finishes it. For a NON-deferred source
                    // whose buffer the idle reclaimer dropped, this rebuilds it from the encoded
                    // bytes rather than letting every GPU cache miss pay a full native decode.
                    // Both states look identical from here, which is why the restore must be tried
                    // for both rather than being gated behind a pending check — gating it is what
                    // made a reclaimed deferred source unreachable to the restore path entirely.
                    bitmapImage.TryRestorePixelData();
                    bitmapImage.TryGetPixelSnapshot(out snapshot);
                }

                if (snapshot is not null)
                {
                    // One consistent tuple: buffer, dimensions, stride and channel order were
                    // produced together and published with a single reference write. Reading them
                    // as four independent properties is what let the render thread pair a 64x64
                    // buffer with 512x512 dimensions mid-publish and hand an out-of-range upload
                    // to the backend — a black rectangle, or worse.
                    uploadedGeneration = snapshot.Generation;
                    nativeBitmap = UploadSnapshot(snapshot);
                }
                else if (!bitmapImage.IsDeferredDecodePending &&
                         bitmapImage.ImageData is { Length: > 0 } encodedBytes)
                {
                    // Encoded-bytes fallback for a source that has no pixel buffer and no deferred
                    // decoder behind it. Deliberately NOT taken for a pending deferred source:
                    // that would run a full synchronous native decode on the render thread, which
                    // is precisely what the deferred scheduler exists to avoid.
                    nativeBitmap = _context.CreateBitmap(encodedBytes);
                }
            }
            catch (Exception ex)
            {
                // NOT Debug.WriteLine. That is [Conditional("DEBUG")] and therefore absent from
                // the builds users run, which is the entire reason a GPU upload failure — VRAM
                // exhaustion, a dimension over the adapter's D3D12_REQ_TEXTURE2D limit on a
                // FL9_3/FL10_x WARP or RDP adapter, a device-removed frame — presented as a black
                // rectangle with no evidence anywhere. ImageDiagnostics is live in Release.
                ImageDiagnostics.UploadFailed(
                    DescribeImageSource(imageSource),
                    "BitmapImage upload",
                    snapshot?.Width ?? bitmapImage.PixelWidth,
                    snapshot?.Height ?? bitmapImage.PixelHeight,
                    ex);

                // Diagnostics alone is a support channel, not an application one: an app that
                // handles Image.ImageFailed to swap in a fallback got nothing at all, because
                // nothing routed an upload failure into the source's LoadFailed chain. Posted
                // rather than raised, because this method runs on the render thread whenever
                // JALIUM_RENDER_THREAD is on (the default on Windows) and that chain reaches a
                // routed-event raise and a dependency-property write. The notifier deduplicates
                // against the source's latched failure, so a retry-every-frame upload path costs
                // one ImageFailed per failure episode rather than one per frame.
                BitmapDecodeNotifier.PostSourceFailure(imageSource, ex);
            }
        }
        else if (imageSource is WriteableBitmap writeable &&
                 writeable.PixelWidth > 0 && writeable.PixelHeight > 0)
        {
            // WriteableBitmap defaults to straight (non-premultiplied) Bgra32, and
            // neither SetPixel nor WritePixels premultiply, so its buffer is straight
            // in the common case — which is exactly what the bitmap-upload ABI expects.
            // The straight pixels are uploaded verbatim on every backend; the native
            // side premultiplies internally where its blend requires it (D3D12 in
            // UpdatePackedPixels / CreateBitmapFromPixels, Vulkan while packing its
            // replay staging, software blends straight). No managed per-backend
            // compensation is needed. (Genuinely premultiplied Pbgra32 source content
            // remains a separate WriteableBitmap alpha-mode concern.)
            try
            {
                nativeBitmap = _context.CreateBitmapFromPixels(
                    writeable.BackBufferArray,
                    writeable.PixelWidth,
                    writeable.PixelHeight,
                    writeable.BackBufferStride);
            }
            catch (Exception ex)
            {
                ImageDiagnostics.UploadFailed(
                    DescribeImageSource(imageSource),
                    "WriteableBitmap upload",
                    writeable.PixelWidth,
                    writeable.PixelHeight,
                    ex);

                // Same contract as the BitmapImage path above: the application learns about the
                // failure through Image.ImageFailed, on the UI thread, once per failure episode.
                BitmapDecodeNotifier.PostSourceFailure(imageSource, ex);
            }
        }

        if (nativeBitmap != null)
        {
            // The device took these pixels, so whatever refused them earlier has cleared. Releasing
            // the latch here is the other half of the "an upload failure never stops the draw"
            // contract: the render gate ignores an upload-class failure so the retry can happen at
            // all, and this is what makes the retry's success visible — the once-per-episode
            // ImageFailed report re-arms, and ImageSource.LoadFailure stops describing a failure
            // that no longer exists. A decode-class failure is deliberately left alone; see
            // ImageSource.ClearUploadFailure.
            imageSource.ClearUploadFailure();

            var estimatedBytes = EstimateBitmapBytes(nativeBitmap);
            _bitmapCache[imageSource] = new BitmapCacheEntry(
                nativeBitmap,
                estimatedBytes,
                ++_bitmapCacheSequence,
                uploadedGeneration)
            {
                LastFrameUsed = _currentFrameId,
            };
            _bitmapCacheBytes += estimatedBytes;
        }

        return nativeBitmap;
    }

    /// <summary>
    /// Uploads one immutable pixel publication to the GPU, normalizing its channel order first
    /// when the decoder produced something other than BGRA8.
    /// </summary>
    /// <remarks>
    /// <para>The pixels are straight (non-premultiplied) alpha: WIC decodes to 32bppBGRA and
    /// IconHelper's GetDIBits is straight too. The bitmap-upload ABI takes straight alpha
    /// regardless of backend — each native backend premultiplies internally as its blend requires
    /// (D3D12 premultiplies on upload in CreateBitmapFromPixels; Vulkan premultiplies while packing
    /// its replay staging buffer; software blends straight). The managed layer stays
    /// backend-agnostic and hands the raw pixels over as-is.</para>
    /// <para>What it does NOT do is assume the channel order. <c>DecodedImage.Format</c> used to be
    /// discarded at publish time, so an Android/Mali decoder returning RGBA8 was uploaded as if it
    /// were BGRA8 — every image rendered with red and blue swapped, with nothing anywhere saying
    /// so. The format now travels with the pixels and is honoured here.</para>
    /// </remarks>
    private NativeBitmap UploadSnapshot(BitmapPixelSnapshot snapshot)
    {
        if (s_uploadFaultInjector?.Invoke(snapshot) is { } injected)
        {
            throw injected;
        }

        return _context.CreateBitmapFromPixels(
            snapshot.Pixels,
            snapshot.Width,
            snapshot.Height,
            snapshot.Stride,
            snapshot.Format);
    }

    private static Func<BitmapPixelSnapshot, Exception?>? s_uploadFaultInjector;

    /// <summary>
    /// Test seam: makes the native upload of a publication fail, the way a real adapter does.
    /// </summary>
    /// <remarks>
    /// <para>The upload catch is where a black image finally became REPORTABLE — it is the site
    /// that used to be a compiled-out <c>Debug.WriteLine</c> — and it was also the one site nothing
    /// could reach from a test: the fault it handles is VRAM exhaustion, a device-removed frame or
    /// an over-limit texture dimension on a WARP/RDP adapter, none of which a test can provoke on
    /// demand. Reverting both reporting calls in that catch to <c>Debug.WriteLine</c> therefore left
    /// the whole suite green, i.e. the fix's headline site was unpinned. This seam is what a test
    /// stands in for the adapter with.</para>
    /// <para>Deliberately a nullable static consulted only inside <see cref="UploadSnapshot"/>: one
    /// null read per texture upload (not per draw — a cache hit never reaches here), no allocation,
    /// and no way for it to change behaviour unless a test has explicitly installed a fault. It is
    /// never set by framework code.</para>
    /// </remarks>
    internal static Func<BitmapPixelSnapshot, Exception?>? UploadFaultInjector
    {
        get => Volatile.Read(ref s_uploadFaultInjector);
        set => Volatile.Write(ref s_uploadFaultInjector, value);
    }

    private void TrimBitmapCacheIfNeeded()
    {
        // This runs once per render for EVERY drawing context (main window, popups,
        // dock indicators, …) via TrimCacheIfNeeded(), so it is the natural per-frame
        // boundary for the bitmap cache. Entries drawn this frame were stamped with
        // the current _currentFrameId and are protected from eviction below; the id
        // is advanced AFTER trimming so they survive this frame and become evictable
        // next frame. Driving the advance here — rather than from a per-context
        // BeginFrame() call — is what keeps the protection correct for every context
        // without one being forgotten: a context that never advanced the id would
        // match every entry below and silently disable the cache budget forever.
        if (_bitmapCache.Count != 0)
        {
            var bitmapCacheByteBudget = GetBitmapCacheByteBudget();
            while (_bitmapCache.Count > MaxBitmapCacheSize || _bitmapCacheBytes > bitmapCacheByteBudget)
            {
                KeyValuePair<ImageSource, BitmapCacheEntry>? oldest = null;
                foreach (var kvp in _bitmapCache)
                {
                    // Never evict a texture used in the current frame: the render
                    // thread may still be sampling it, and evicting + re-uploading
                    // currently visible bitmaps every frame is exactly the thrash
                    // this guards against.
                    if (kvp.Value.LastFrameUsed == _currentFrameId)
                    {
                        continue;
                    }

                    if (oldest == null || kvp.Value.LastAccessSequence < oldest.Value.Value.LastAccessSequence)
                    {
                        oldest = kvp;
                    }
                }

                if (oldest == null)
                {
                    break;
                }

                RemoveBitmapCacheEntry(oldest.Value.Key, oldest.Value.Value);
            }
        }

        // Advance the per-frame id for the next render. unchecked so the ~2^31-frame
        // wrap is a defined wraparound (worst case: one bitmap redundantly re-uploaded
        // on the wrap frame — no correctness impact).
        unchecked { _currentFrameId++; }
    }

    // Single hard ceiling. The previous WorkingSet-pressure tiers throttled a GPU
    // (VRAM) cache by managed process memory — a category error that collapsed the
    // budget to 32MB and drove per-frame texture re-upload. Evicting idle entries
    // alone keeps this bounded; current-frame entries are protected in the trim loop.
    private static long GetBitmapCacheByteBudget() => MaxBitmapCacheBytes;

    private void RemoveBitmapCacheEntry(ImageSource key, BitmapCacheEntry entry)
    {
        if (_bitmapCache.Remove(key))
        {
            _bitmapCacheBytes = Math.Max(0, _bitmapCacheBytes - entry.EstimatedBytes);
            entry.Bitmap.Dispose();
        }
    }

    private static long EstimateBitmapBytes(NativeBitmap bitmap)
    {
        // Native bitmaps are stored as RGBA8 textures (4 bytes per pixel).
        return (long)bitmap.Width * bitmap.Height * 4;
    }

    private enum DrawingStateType
    {
        Transform,
        NativeTransform,
        Clip,
        Opacity,
        ViewportOnly
    }

    private readonly struct DrawingState
    {
        public DrawingStateType Type { get; }
        public Point SavedOffset { get; }

        public DrawingState(DrawingStateType type, Point savedOffset)
        {
            Type = type;
            SavedOffset = savedOffset;
        }
    }
}
