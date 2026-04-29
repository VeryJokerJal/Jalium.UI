using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

/// <summary>
/// Rendering backend types.
/// </summary>
public enum RenderBackend
{
    Auto = 0,
    D3D12 = 1,
    Vulkan = 3,
    Metal = 5,
    Software = 7
}

/// <summary>
/// Rendering engine types. The engine determines how 2D vector graphics
/// are rasterized, orthogonal to the GPU backend (D3D12/Vulkan/Metal).
/// </summary>
public enum RenderingEngine
{
    /// <summary>Automatic: defaults to Impeller on all platforms.</summary>
    Auto = 0,
    /// <summary>Vello: GPU compute pipeline with prefix-sum tiling.</summary>
    Vello = 1,
    /// <summary>Impeller: tessellation-based pipeline (Flutter-derived).</summary>
    Impeller = 2
}

/// <summary>
/// Text metrics structure returned by native text measurement.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TextMetrics
{
    /// <summary>The width of the text layout area.</summary>
    public float Width;

    /// <summary>The height of the text layout area.</summary>
    public float Height;

    /// <summary>The natural line height (ascent + descent + lineGap).</summary>
    public float LineHeight;

    /// <summary>The baseline offset from the top.</summary>
    public float Baseline;

    /// <summary>The ascent of the font (above baseline).</summary>
    public float Ascent;

    /// <summary>The descent of the font (below baseline).</summary>
    public float Descent;

    /// <summary>The recommended line gap.</summary>
    public float LineGap;

    /// <summary>The number of lines in the layout.</summary>
    public uint LineCount;
}

/// <summary>
/// Text hit-test result returned by native hit-testing APIs.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TextHitTestResult
{
    /// <summary>Character index at the hit point.</summary>
    public uint TextPosition;

    /// <summary>Non-zero if hit is on the trailing edge of the character.</summary>
    public int IsTrailingHit;

    /// <summary>Non-zero if the point is inside the text layout.</summary>
    public int IsInside;

    /// <summary>X position of the caret at this text position.</summary>
    public float CaretX;

    /// <summary>Y position of the caret.</summary>
    public float CaretY;

    /// <summary>Height of the caret.</summary>
    public float CaretHeight;
}

/// <summary>
/// Native method imports for the Jalium rendering engine.
/// </summary>
internal static partial class NativeMethods
{
    private const string CoreLib = "jalium.native.core";
    private const string D3D12Lib = "jalium.native.d3d12";
    private const string VulkanLib = "jalium.native.vulkan";
    private const string MetalLib = "jalium.native.metal";
    private const string SoftwareLib = "jalium.native.software";
    private const string PlatformLib = "jalium.native.platform";

    // Per-backend "init attempted" flags. 0 = not yet, 1 = init() has been
    // invoked once (whether it succeeded or threw). Interlocked CAS so the
    // first-touch path through EnsureBackendInitialized is single-shot even
    // under racy access from background prewarm + UI thread.
    private static int s_d3d12InitTried;
    private static int s_vulkanInitTried;
    private static int s_metalInitTried;

    /// <summary>
    /// Static constructor: only registers the Software fallback backend.
    /// GPU backends (D3D12 / Vulkan / Metal) stay completely unloaded — their
    /// native DLLs (and transitive dependencies like vulkan-1.dll) are not
    /// brought into the process — until <see cref="EnsureBackendInitialized"/>
    /// is called for that specific backend, which happens lazily inside
    /// <see cref="RenderContext"/>'s constructor and inside
    /// <see cref="IsBackendAvailable"/>. This shaves the secondary backend's
    /// LoadLibrary chain (and its first-chance DllNotFoundException when its
    /// runtime probe fails) off the startup path on machines that only ever
    /// use the platform-default backend.
    /// </summary>
    static NativeMethods()
    {
        TryInitializeBackend(SoftwareInit);
    }

    /// <summary>
    /// Ensures the native init function for <paramref name="backend"/> has
    /// been invoked exactly once for the lifetime of the process. Calling this
    /// triggers LoadLibrary of the backend's native DLL via the first P/Invoke
    /// against it, which is what registers the backend's factory in the native
    /// registry. Repeated calls are a no-op. Safe to call from any thread.
    /// </summary>
    /// <remarks>
    /// <see cref="RenderBackend.Software"/> is already initialized in the static
    /// constructor and <see cref="RenderBackend.Auto"/> is a request marker, not
    /// a backend — both are no-ops here. Callers that need to materialize the
    /// platform default for an Auto request should resolve it through
    /// <see cref="RenderBackendSelector"/> first.
    /// </remarks>
    internal static void EnsureBackendInitialized(RenderBackend backend)
    {
        switch (backend)
        {
            case RenderBackend.D3D12:
                if (Interlocked.CompareExchange(ref s_d3d12InitTried, 1, 0) == 0)
                {
                    TryInitializeBackend(D3D12Init);
                }
                break;
            case RenderBackend.Vulkan:
                if (Interlocked.CompareExchange(ref s_vulkanInitTried, 1, 0) == 0)
                {
                    TryInitializeBackend(VulkanInit);
                }
                break;
            case RenderBackend.Metal:
                if (Interlocked.CompareExchange(ref s_metalInitTried, 1, 0) == 0)
                {
                    TryInitializeBackend(MetalInit);
                }
                break;
            case RenderBackend.Software:
            case RenderBackend.Auto:
            default:
                // Software handled by the static ctor; Auto is not a concrete
                // backend. Both are intentionally no-ops.
                break;
        }
    }

    [LibraryImport(D3D12Lib, EntryPoint = "jalium_d3d12_init")]
    private static partial void D3D12Init();

    [LibraryImport(VulkanLib, EntryPoint = "jalium_vulkan_init")]
    private static partial void VulkanInit();

    [LibraryImport(MetalLib, EntryPoint = "jalium_metal_init")]
    private static partial void MetalInit();

    [LibraryImport(SoftwareLib, EntryPoint = "jalium_software_init")]
    private static partial void SoftwareInit();

    private static void TryInitializeBackend(Action init)
    {
        try
        {
            init();
        }
        catch (Exception ex) when (
            ex is DllNotFoundException or
            EntryPointNotFoundException or
            BadImageFormatException or
            TypeLoadException or
            MarshalDirectiveException or
            InvalidOperationException)
        {
            // Backend library not available on this platform — safe to ignore.
        }
        catch (Exception)
        {
            // Any other load failure is also non-fatal; the backend simply won't register.
        }
    }

    #region Context Management

    /// <summary>
    /// Creates a new Jalium rendering context.
    /// </summary>
    [DllImport(CoreLib, EntryPoint = "jalium_context_create", ExactSpelling = true)]
    internal static extern nint ContextCreate(RenderBackend backend);

    /// <summary>
    /// Destroys a Jalium rendering context.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_context_destroy")]
    internal static partial void ContextDestroy(nint context);

    /// <summary>
    /// Gets the backend type of a context.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_context_get_backend")]
    internal static partial RenderBackend ContextGetBackend(nint context);

    /// <summary>
    /// Gets the last error code.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_context_get_last_error")]
    internal static partial int ContextGetLastError(nint context);

    /// <summary>
    /// Checks if the GPU device is still operational.
    /// Returns 0 if OK, non-zero if device lost.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_context_check_device_status")]
    internal static partial int ContextCheckDeviceStatus(nint context);

    /// <summary>
    /// Sets the default rendering engine for new render targets on this context.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_context_set_default_engine")]
    internal static partial int ContextSetDefaultEngine(nint context, RenderingEngine engine);

    /// <summary>
    /// Gets the default rendering engine for a context.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_context_get_default_engine")]
    internal static partial RenderingEngine ContextGetDefaultEngine(nint context);

    #endregion

    #region Rendering Engine (Hot-Switch)

    /// <summary>
    /// Gets the active rendering engine for a render target.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_get_engine")]
    internal static partial RenderingEngine RenderTargetGetEngine(nint renderTarget);

    /// <summary>
    /// Sets the rendering engine for a render target (hot-switch).
    /// Takes effect at the next BeginDraw().
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_set_engine")]
    internal static partial int RenderTargetSetEngine(nint renderTarget, RenderingEngine engine);

    /// <summary>
    /// Mirrors native <c>JaliumGpuStats</c> — sequential layout matches the C struct.
    /// Used by the DevTools Perf tab to surface GPU resource usage.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuStatsNative
    {
        public int GlyphSlotsUsed;
        public int GlyphSlotsTotal;
        public long GlyphBytes;
        public int PathEntries;
        public long PathBytes;
        public int TextureCount;
        public long TextureBytes;
    }

    /// <summary>
    /// Snapshots GPU resource usage for a render target. Returns JALIUM_OK (0) on
    /// success, JALIUM_ERROR_NOT_SUPPORTED (3) if the backend hasn't implemented
    /// the query.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_query_gpu_stats")]
    internal static partial int RenderTargetQueryGpuStats(nint renderTarget, out GpuStatsNative stats);

    /// <summary>
    /// Asks the backend to drop any reusable GPU / CPU caches it has accumulated
    /// (path tessellation results, rasterized text bitmaps, glyph atlas pages,
    /// gradient stops, etc). Invoked by the managed-side idle reclaimer once the
    /// app has been quiet long enough that holding the caches is no longer worth
    /// the memory; backends rebuild on demand on the next frame that needs them.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_reclaim_idle_resources")]
    internal static partial int RenderTargetReclaimIdleResources(nint renderTarget);

    #endregion

    #region Render Target Management

    /// <summary>
    /// Creates a render target for a window handle.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_create_for_hwnd")]
    internal static partial nint RenderTargetCreateForHwnd(nint context, nint hwnd, int width, int height);

    /// <summary>
    /// Creates a render target with composition swap chain for per-pixel alpha transparency.
    /// Uses CreateSwapChainForComposition + DirectComposition.
    /// The window must have WS_EX_NOREDIRECTIONBITMAP extended style.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_create_for_composition")]
    internal static partial nint RenderTargetCreateForComposition(nint context, nint hwnd, int width, int height);

    /// <summary>
    /// Creates a render target from a platform-neutral surface descriptor.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_create_for_surface")]
    internal static partial nint RenderTargetCreateForSurface(nint context, in NativeSurfaceDescriptor surface, int width, int height);

    /// <summary>
    /// Creates a composition-capable render target from a platform-neutral surface descriptor.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_create_for_composition_surface")]
    internal static partial nint RenderTargetCreateForCompositionSurface(nint context, in NativeSurfaceDescriptor surface, int width, int height);

    /// <summary>
    /// Destroys a render target.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_destroy")]
    internal static partial void RenderTargetDestroy(nint renderTarget);

    /// <summary>
    /// Resizes a render target.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_resize")]
    internal static partial int RenderTargetResize(nint renderTarget, int width, int height);

    /// <summary>
    /// Begins a drawing session.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_begin_draw")]
    internal static partial int RenderTargetBeginDraw(nint renderTarget);

    /// <summary>
    /// Ends a drawing session.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_end_draw")]
    internal static partial int RenderTargetEndDraw(nint renderTarget);

    /// <summary>
    /// Clears the render target.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_clear")]
    internal static partial void RenderTargetClear(nint renderTarget, float r, float g, float b, float a);

    /// <summary>
    /// Sets whether VSync is enabled.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_set_vsync")]
    internal static partial void RenderTargetSetVSync(nint renderTarget, int enabled);

    /// <summary>
    /// Sets the DPI for the render target so D2D maps DIP coordinates to physical pixels.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_set_dpi")]
    internal static partial void RenderTargetSetDpi(nint renderTarget, float dpiX, float dpiY);

    /// <summary>
    /// Adds a dirty rectangle for partial rendering optimization.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_add_dirty_rect")]
    internal static partial void RenderTargetAddDirtyRect(nint renderTarget, float x, float y, float width, float height);

    /// <summary>
    /// Marks the entire render target as needing full redraw.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_set_full_invalidation")]
    internal static partial void RenderTargetSetFullInvalidation(nint renderTarget);

    /// <summary>
    /// Returns whether the render target supports partial redraw + dirty-rect presentation.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_supports_partial_presentation")]
    internal static partial int RenderTargetSupportsPartialPresentation(nint renderTarget);

    /// <summary>
    /// Creates a composition visual node for hosting external content (e.g. WebView).
    /// Returns a backend-specific COM pointer (IUnknown* on Windows).
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_create_webview_visual")]
    internal static partial int RenderTargetCreateWebViewVisual(nint renderTarget, out nint visualTarget);

    /// <summary>
    /// Destroys a composition visual previously created by RenderTargetCreateWebViewVisual.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_destroy_webview_visual")]
    internal static partial int RenderTargetDestroyWebViewVisual(nint renderTarget, nint visualTarget);

    /// <summary>
    /// Updates the placement and clip rectangle of a composition visual created for WebView hosting.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_set_webview_visual_placement")]
    internal static partial int RenderTargetSetWebViewVisualPlacement(
        nint renderTarget,
        nint visualTarget,
        int x,
        int y,
        int width,
        int height,
        int contentOffsetX,
        int contentOffsetY);

    #endregion

    #region Drawing Commands

    /// <summary>
    /// Draws a filled rectangle.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_fill_rectangle")]
    internal static partial void DrawFillRectangle(nint renderTarget, float x, float y, float width, float height, nint brush);

    /// <summary>
    /// Draws a rectangle outline.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_rectangle")]
    internal static partial void DrawRectangle(nint renderTarget, float x, float y, float width, float height, nint brush, float strokeWidth);

    /// <summary>
    /// Draws a filled rounded rectangle.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_fill_rounded_rectangle")]
    internal static partial void DrawFillRoundedRectangle(nint renderTarget, float x, float y, float width, float height, float radiusX, float radiusY, nint brush);

    /// <summary>
    /// Draws a rounded rectangle outline.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_rounded_rectangle")]
    internal static partial void DrawRoundedRectangle(nint renderTarget, float x, float y, float width, float height, float radiusX, float radiusY, nint brush, float strokeWidth);

    /// <summary>
    /// Draws a filled rounded rectangle with per-corner radii.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_fill_per_corner_rounded_rectangle")]
    internal static partial void FillPerCornerRoundedRectangle(nint renderTarget, float x, float y, float width, float height, float tl, float tr, float br, float bl, nint brush);

    /// <summary>
    /// Draws a rounded rectangle outline with per-corner radii.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_per_corner_rounded_rectangle")]
    internal static partial void DrawPerCornerRoundedRectangle(nint renderTarget, float x, float y, float width, float height, float tl, float tr, float br, float bl, nint brush, float strokeWidth);

    /// <summary>
    /// Draws a filled ellipse.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_fill_ellipse")]
    internal static partial void DrawFillEllipse(nint renderTarget, float centerX, float centerY, float radiusX, float radiusY, nint brush);

    /// <summary>
    /// Draws a batch of filled ellipses with per-ellipse color (5 floats each: cx, cy, rx, ry, packedRGBA).
    /// Single P/Invoke call for thousands of ellipses.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_fill_ellipse_batch")]
    internal static partial void FillEllipseBatch(nint renderTarget, float[] data, uint count);

    /// <summary>
    /// Draws an ellipse outline.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_ellipse")]
    internal static partial void DrawEllipse(nint renderTarget, float centerX, float centerY, float radiusX, float radiusY, nint brush, float strokeWidth);

    /// <summary>
    /// Draws a line.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_line")]
    internal static partial void DrawLine(nint renderTarget, float x1, float y1, float x2, float y2, nint brush, float strokeWidth);

    /// <summary>
    /// Fills a polygon defined by an array of points.
    /// </summary>
    /// <param name="renderTarget">The render target.</param>
    /// <param name="points">Array of point coordinates (x0, y0, x1, y1, ...).</param>
    /// <param name="pointCount">Number of points.</param>
    /// <param name="brush">Brush to fill with.</param>
    /// <param name="fillRule">0 = EvenOdd, 1 = NonZero.</param>
    [LibraryImport(CoreLib, EntryPoint = "jalium_fill_polygon")]
    internal static partial void FillPolygon(nint renderTarget, float[] points, int pointCount, nint brush, int fillRule);

    /// <summary>
    /// Draws a polygon outline.
    /// </summary>
    /// <param name="renderTarget">The render target.</param>
    /// <param name="points">Array of point coordinates (x0, y0, x1, y1, ...).</param>
    /// <param name="pointCount">Number of points.</param>
    /// <param name="brush">Brush for stroke.</param>
    /// <param name="strokeWidth">Width of stroke.</param>
    /// <param name="closed">Whether to close the polygon (1 = closed, 0 = open).</param>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_polygon")]
    internal static partial void DrawPolygon(nint renderTarget, float[] points, int pointCount, nint brush, float strokeWidth, int closed, int lineJoin, float miterLimit);

    /// <summary>
    /// Fills a path with lines and bezier curves.
    /// Commands: tag 0 = LineTo [0,x,y], tag 1 = CubicBezierTo [1,cp1x,cp1y,cp2x,cp2y,ex,ey],
    ///           tag 2 = MoveTo [2,x,y], tag 3 = QuadBezierTo [3,cpx,cpy,ex,ey],
    ///           tag 4 = ArcTo [4,ex,ey,rx,ry,xRotDeg,largeArc,sweep], tag 5 = ClosePath [5].
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_fill_path")]
    internal static partial void FillPath(nint renderTarget, float startX, float startY, float[] commands, int commandLength, nint brush, int fillRule);

    /// <summary>
    /// Strokes a path with lines and bezier curves.
    /// lineCap: 0 = Butt, 1 = Square, 2 = Round.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_stroke_path")]
    internal static partial void StrokePath(nint renderTarget, float startX, float startY, float[] commands, int commandLength, nint brush, float strokeWidth, int closed, int lineJoin, float miterLimit, int lineCap,
        float[]? dashPattern, int dashCount, float dashOffset);


    // ═══════════════════════════════════════════════════════════════════════
    //  Ink layer bitmap + brush-shader pipeline
    // ═══════════════════════════════════════════════════════════════════════
    //
    //  The InkCanvas stores committed strokes in a persistent RGBA8 GPU
    //  bitmap. Brush shaders (user- or built-in-authored HLSL pixel
    //  shaders) run once per committed stroke and write pixels directly
    //  into that bitmap; per-frame cost collapses to a single blit into
    //  the main render target.
    //
    //  All five functions below are implemented on the active backend's
    //  context (D3D12 today; Vulkan can no-op or return failure while it
    //  catches up).

    /// <summary>
    /// Allocates a persistent RGBA8 offscreen render target bitmap owned
    /// by <paramref name="contextHandle"/>. Returns 0 on failure.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_ink_layer_bitmap_create")]
    internal static partial nint InkLayerBitmapCreate(nint contextHandle, int width, int height);

    /// <summary>
    /// Releases the bitmap and its GPU texture.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_ink_layer_bitmap_destroy")]
    internal static partial void InkLayerBitmapDestroy(nint bitmap);

    /// <summary>
    /// Reallocates the backing texture. Contents reset to transparent.
    /// Returns 0 on success.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_ink_layer_bitmap_resize")]
    internal static partial int InkLayerBitmapResize(nint bitmap, int width, int height);

    /// <summary>
    /// Clears the bitmap to a premultiplied RGBA color.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_ink_layer_bitmap_clear")]
    internal static partial void InkLayerBitmapClear(nint bitmap, float r, float g, float b, float a);

    /// <summary>
    /// Compiles an HLSL pixel shader for the brush pipeline. The framework
    /// concatenates the shared preamble with <paramref name="brushMainHlsl"/>
    /// before feeding to D3DCompile. <paramref name="blendMode"/> selects
    /// the PSO blend state (0=SourceOver, 1=Additive, 2=Erase).
    /// Returns 0 on failure (compilation error / out of memory).
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_brush_shader_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint BrushShaderCreate(
        nint contextHandle,
        string shaderKey,
        string brushMainHlsl,
        int blendMode);

    /// <summary>
    /// Releases a brush shader and its cached PSO.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_brush_shader_destroy")]
    internal static partial void BrushShaderDestroy(nint shader);

    /// <summary>
    /// Runs <paramref name="shader"/> over the bbox region of
    /// <paramref name="bitmap"/>, reading stroke polyline points from
    /// <paramref name="strokePoints"/> (array of <see cref="BrushStrokePoint"/>,
    /// total byte length = <paramref name="pointCount"/> × 16). The
    /// <paramref name="constants"/> pointer must reference a filled
    /// <see cref="BrushConstantsNative"/> (80 bytes).
    /// <paramref name="extraParams"/> is an optional user-defined cbuffer
    /// (bound as <c>b1</c>) that custom brush shaders can read. Pass
    /// <c>null</c> / 0 when the shader uses only the standard b0 cbuffer.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_ink_layer_bitmap_dispatch_brush")]
    internal static unsafe partial int InkLayerBitmapDispatchBrush(
        nint bitmap,
        nint shader,
        BrushStrokePoint* strokePoints,
        int pointCount,
        BrushConstantsNative* constants,
        void* extraParams,
        int extraParamsSize);

    /// <summary>
    /// Composites <paramref name="bitmap"/> onto the current render
    /// target at <paramref name="dstX"/>, <paramref name="dstY"/>. Uses
    /// premultiplied source-over. Must be called between
    /// BeginDraw/EndDraw on the destination render target.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_render_target_blit_ink_layer")]
    internal static partial void RenderTargetBlitInkLayer(
        nint renderTarget,
        nint bitmap,
        float dstX, float dstY,
        float opacity);

    /// <summary>
    /// Draws a content area border: fills with bottom-only rounded corners, strokes U-shape (no top).
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_content_border")]
    internal static partial void DrawContentBorder(nint renderTarget, float x, float y, float width, float height,
        float blRadius, float brRadius, nint fillBrush, nint strokeBrush, float strokeWidth);

    /// <summary>
    /// Draws text.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_text")]
    internal static unsafe partial void DrawTextRaw(nint renderTarget, char* text, int textLength, nint textFormat, float x, float y, float width, float height, nint brush);

    /// <summary>
    /// Draws a backdrop filter effect.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_backdrop_filter", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void DrawBackdropFilter(
        nint renderTarget,
        float x, float y, float width, float height,
        string backdropFilter,
        string material,
        string materialTint,
        float materialTintOpacity,
        float materialBlurRadius,
        float cornerRadiusTL,
        float cornerRadiusTR,
        float cornerRadiusBR,
        float cornerRadiusBL);

    /// <summary>
    /// Draws a glowing border highlight effect for DevTools.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_glowing_border_highlight")]
    internal static partial void DrawGlowingBorderHighlight(
        nint renderTarget,
        float x, float y, float width, float height,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float trailLength,
        float dimOpacity,
        float screenWidth, float screenHeight);

    /// <summary>
    /// Draws a glowing border transition effect between two elements.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_glowing_border_transition")]
    internal static partial void DrawGlowingBorderTransition(
        nint renderTarget,
        float fromX, float fromY, float fromWidth, float fromHeight,
        float toX, float toY, float toWidth, float toHeight,
        float headProgress, float tailProgress,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float trailLength,
        float dimOpacity,
        float screenWidth, float screenHeight);

    /// <summary>
    /// Draws a ripple effect expanding from element border.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_ripple_effect")]
    internal static partial void DrawRippleEffect(
        nint renderTarget,
        float x, float y, float width, float height,
        float rippleProgress,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float dimOpacity,
        float screenWidth, float screenHeight);

    /// <summary>
    /// Begins capturing content into an offscreen bitmap for transition shader effects.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_transition_begin_capture")]
    internal static partial void TransitionBeginCapture(nint renderTarget, int slot,
        float x, float y, float w, float h);

    /// <summary>
    /// Ends capturing content for a transition slot and restores the main render target.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_transition_end_capture")]
    internal static partial void TransitionEndCapture(nint renderTarget, int slot);

    /// <summary>
    /// Draws the transition shader effect blending old and new content bitmaps.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_transition_shader")]
    internal static partial void DrawTransitionShader(nint renderTarget,
        float x, float y, float w, float h, float progress, int mode, float cornerRadius);

    /// <summary>
    /// Draws a previously captured transition bitmap to the current render target.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_captured_transition")]
    internal static partial void DrawCapturedTransition(nint renderTarget,
        int slot, float x, float y, float w, float h, float opacity);

    // ========================================================================
    // Element Effect Capture & Rendering
    // ========================================================================

    /// <summary>
    /// Begins capturing element content into an offscreen bitmap for effect processing.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_effect_begin_capture")]
    internal static partial void EffectBeginCapture(nint renderTarget,
        float x, float y, float w, float h);

    /// <summary>
    /// Ends capturing element content and restores the main render target.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_effect_end_capture")]
    internal static partial void EffectEndCapture(nint renderTarget);

    /// <summary>
    /// Applies a Gaussian blur effect to the captured element content and draws it.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_blur_effect")]
    internal static partial void DrawBlurEffect(nint renderTarget,
        float x, float y, float w, float h, float radius,
        float uvOffsetX, float uvOffsetY);

    /// <summary>
    /// Applies a drop shadow effect to the captured element content and draws it.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_drop_shadow_effect")]
    internal static partial void DrawDropShadowEffect(nint renderTarget,
        float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float uvOffsetX, float uvOffsetY,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL);

    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_outer_glow_effect")]
    internal static partial void DrawOuterGlowEffect(nint renderTarget,
        float x, float y, float w, float h,
        float glowSize, float r, float g, float b, float a, float intensity,
        float uvOffsetX, float uvOffsetY,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL);

    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_inner_shadow_effect")]
    internal static partial void DrawInnerShadowEffect(nint renderTarget,
        float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float uvOffsetX, float uvOffsetY,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL);

    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_color_matrix_effect")]
    internal static partial void DrawColorMatrixEffect(nint renderTarget,
        float x, float y, float w, float h,
        ReadOnlySpan<float> matrix);

    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_emboss_effect")]
    internal static partial void DrawEmbossEffect(nint renderTarget,
        float x, float y, float w, float h,
        float amount, float lightDirX, float lightDirY, float relief);

    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_shader_effect")]
    internal static partial void DrawShaderEffect(nint renderTarget,
        float x, float y, float w, float h,
        [In] byte[] shaderBytecode, uint shaderBytecodeSize,
        [In] float[] constants, uint constantFloatCount);

    /// <summary>
    /// Draws a liquid glass effect with SDF-based refraction, highlight, and inner shadow.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_liquid_glass")]
    internal static partial void DrawLiquidGlass(
        nint renderTarget,
        float x, float y, float width, float height,
        float cornerRadius,
        float blurRadius,
        float refractionAmount,
        float chromaticAberration,
        float tintR, float tintG, float tintB, float tintOpacity,
        float lightX, float lightY,
        float highlightBoost,
        int shapeType,
        float shapeExponent,
        int neighborCount,
        float fusionRadius,
        nint neighborData);

    #endregion

    #region Transform and Clip

    /// <summary>
    /// Pushes a transform matrix.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_push_transform")]
    internal static partial void PushTransform(nint renderTarget, [In] float[] matrix);

    /// <summary>
    /// Pops the current transform.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_pop_transform")]
    internal static partial void PopTransform(nint renderTarget);

    /// <summary>
    /// Pushes a clip rectangle.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_push_clip")]
    internal static partial void PushClip(nint renderTarget, float x, float y, float width, float height);

    /// <summary>
    /// Pushes a clip rectangle with ALIASED anti-aliasing (hard pixel boundary).
    /// Used for dirty region clips where semi-transparent edges cause artifacts.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_push_clip_aliased")]
    internal static partial void PushClipAliased(nint renderTarget, float x, float y, float width, float height);

    /// <summary>
    /// Pushes a rounded rectangle clip using a geometry mask layer.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_push_rounded_rect_clip")]
    internal static partial void PushRoundedRectClip(nint renderTarget, float x, float y, float width, float height, float rx, float ry);

    /// <summary>
    /// Pops the current clip.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_pop_clip")]
    internal static partial void PopClip(nint renderTarget);

    /// <summary>
    /// Punches a transparent rectangular hole in the current render target.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_punch_transparent_rect")]
    internal static partial void PunchTransparentRect(nint renderTarget, float x, float y, float width, float height);

    /// <summary>
    /// Pushes an opacity value.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_push_opacity")]
    internal static partial void PushOpacity(nint renderTarget, float opacity);

    /// <summary>
    /// Pops the current opacity.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_pop_opacity")]
    internal static partial void PopOpacity(nint renderTarget);

    /// <summary>
    /// Sets the current shape type for SDF rect rendering.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_set_shape_type")]
    internal static partial void SetShapeType(nint renderTarget, int type, float n);

    #endregion

    #region Brush Management

    /// <summary>
    /// Creates a solid color brush.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_brush_create_solid")]
    internal static partial nint BrushCreateSolid(nint context, float r, float g, float b, float a);

    /// <summary>
    /// Creates a linear gradient brush.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_brush_create_linear_gradient")]
    internal static partial nint BrushCreateLinearGradient(
        nint context,
        float startX, float startY, float endX, float endY,
        float[] stops, uint stopCount,
        uint extendMode);

    /// <summary>
    /// Creates a radial gradient brush.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_brush_create_radial_gradient")]
    internal static partial nint BrushCreateRadialGradient(
        nint context,
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        float[] stops, uint stopCount,
        uint extendMode);

    /// <summary>
    /// Destroys a brush.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_brush_destroy")]
    internal static partial void BrushDestroy(nint brush);

    #endregion

    #region Text Format Management

    /// <summary>
    /// Creates a text format.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_create", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint TextFormatCreate(nint context, string fontFamily, float fontSize, int fontWeight, int fontStyle);

    /// <summary>
    /// Destroys a text format.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_destroy")]
    internal static partial void TextFormatDestroy(nint textFormat);

    /// <summary>
    /// Sets text alignment.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_set_alignment")]
    internal static partial void TextFormatSetAlignment(nint textFormat, int alignment);

    /// <summary>
    /// Sets paragraph alignment.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_set_paragraph_alignment")]
    internal static partial void TextFormatSetParagraphAlignment(nint textFormat, int alignment);

    /// <summary>
    /// Sets text trimming mode.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_set_trimming")]
    internal static partial void TextFormatSetTrimming(nint textFormat, int trimming);

    /// <summary>
    /// Sets word wrapping mode (0=wrap, 1=no_wrap, 2=character, 3=emergency_break).
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_set_word_wrapping")]
    internal static partial void TextFormatSetWordWrapping(nint textFormat, int wrapping);

    /// <summary>
    /// Sets line spacing (method: 0=default, 1=uniform, 2=proportional).
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_set_line_spacing")]
    internal static partial void TextFormatSetLineSpacing(nint textFormat, int method, float spacing, float baseline);

    /// <summary>
    /// Sets maximum number of lines (0 = unlimited).
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_set_max_lines")]
    internal static partial void TextFormatSetMaxLines(nint textFormat, uint maxLines);

    /// <summary>
    /// Hit-tests a point against a text layout.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_hit_test_point", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int TextFormatHitTestPoint(nint textFormat, string text, int textLength,
        float maxWidth, float maxHeight, float pointX, float pointY, out TextHitTestResult result);

    /// <summary>
    /// Gets caret position for a given text index.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_hit_test_text_position", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int TextFormatHitTestTextPosition(nint textFormat, string text, int textLength,
        float maxWidth, float maxHeight, uint textPosition, int isTrailingHit, out TextHitTestResult result);

    /// <summary>
    /// Measures text and returns metrics.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_measure_text", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int TextFormatMeasureText(nint textFormat, string text, int textLength, float maxWidth, float maxHeight, out TextMetrics metrics);

    /// <summary>
    /// Gets font metrics without measuring text.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_text_format_get_font_metrics")]
    internal static partial int TextFormatGetFontMetrics(nint textFormat, out TextMetrics metrics);

    #endregion

    #region Bitmap Management

    /// <summary>
    /// Creates a bitmap from encoded image data.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_bitmap_create_from_memory")]
    internal static partial nint BitmapCreateFromMemory(nint context, [In] byte[] data, uint dataSize);

    /// <summary>
    /// Creates a bitmap from raw BGRA8 pixel data.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_bitmap_create_from_pixels")]
    internal static partial nint BitmapCreateFromPixels(nint context, [In] byte[] pixels, uint width, uint height, uint stride);

    /// <summary>
    /// Gets the width of a bitmap.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_bitmap_get_width")]
    internal static partial uint BitmapGetWidth(nint bitmap);

    /// <summary>
    /// Gets the height of a bitmap.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_bitmap_get_height")]
    internal static partial uint BitmapGetHeight(nint bitmap);

    /// <summary>
    /// Updates an existing bitmap's pixels in place. Avoids the per-frame texture
    /// recreation that thrashes the swap chain when a video / WriteableBitmap streams
    /// frames at 30+fps. Returns 1 on success, 0 on failure (size mismatch / unsupported backend).
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_bitmap_update_pixels")]
    internal static partial int BitmapUpdatePixels(nint bitmap, [In] byte[] pixels, uint width, uint height, uint stride);

    /// <summary>
    /// Destroys a bitmap.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_bitmap_destroy")]
    internal static partial void BitmapDestroy(nint bitmap);

    /// <summary>
    /// Draws a bitmap.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_bitmap")]
    internal static partial void DrawBitmap(nint renderTarget, nint bitmap, float x, float y, float width, float height, float opacity);

    /// <summary>
    /// Draws a bitmap with explicit scaling-mode selection.
    /// scalingMode values mirror <see cref="Jalium.UI.Media.BitmapScalingMode"/> integer ordinals
    /// (Unspecified=0, LowQuality=1, HighQuality=2, NearestNeighbor=3, Linear=4, Fant=5).
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_bitmap_ex")]
    internal static partial void DrawBitmapEx(nint renderTarget, nint bitmap, float x, float y, float width, float height, float opacity, int scalingMode);

    /// <summary>
    /// Captures the desktop area at the specified screen coordinates.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_capture_desktop_area")]
    internal static partial void CaptureDesktopArea(nint renderTarget, int screenX, int screenY, int width, int height);

    /// <summary>
    /// Draws the cached desktop capture with blur and tint overlay.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_desktop_backdrop")]
    internal static partial void DrawDesktopBackdrop(
        nint renderTarget,
        float x, float y, float w, float h,
        float blurRadius,
        float tintR, float tintG, float tintB, float tintOpacity,
        float noiseIntensity, float saturation);

    #endregion

    #region Backend Registration

    /// <summary>
    /// Checks if a backend is available.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_is_backend_available")]
    private static partial int IsBackendAvailableNative(RenderBackend backend);

    /// <summary>
    /// Public availability probe. Performs the lazy backend init for the
    /// requested backend before delegating to the native registry, so a
    /// caller asking "is Vulkan available?" actually loads jalium.native.vulkan
    /// (and its transitive vulkan-1.dll) at this point — and only at this
    /// point — instead of paying that cost up front in the static ctor.
    /// </summary>
    internal static int IsBackendAvailable(RenderBackend backend)
    {
        EnsureBackendInitialized(backend);
        return IsBackendAvailableNative(backend);
    }

    #endregion

    #region Platform Library (jalium.native.platform)

    // --- Platform Initialization ---

    [LibraryImport(PlatformLib, EntryPoint = "jalium_platform_init")]
    internal static partial int PlatformInit();

    [LibraryImport(PlatformLib, EntryPoint = "jalium_platform_shutdown")]
    internal static partial void PlatformShutdown();

    [LibraryImport(PlatformLib, EntryPoint = "jalium_platform_get_current")]
    internal static partial int PlatformGetCurrent();

    // --- Window Management ---

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_create")]
    internal static partial nint WindowCreate(ref NativePlatformWindowParams windowParams);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_destroy")]
    internal static partial void WindowDestroy(nint window);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_show")]
    internal static partial void WindowShow(nint window);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_hide")]
    internal static partial void WindowHide(nint window);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_set_title", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial void WindowSetTitle(nint window, string title);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_resize")]
    internal static partial void WindowResize(nint window, int width, int height);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_move")]
    internal static partial void WindowMove(nint window, int x, int y);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_set_state")]
    internal static partial void WindowSetState(nint window, int state);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_get_state")]
    internal static partial int WindowGetState(nint window);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_get_native_handle")]
    internal static partial nint WindowGetNativeHandle(nint window);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_get_surface")]
    internal static partial NativeSurfaceDescriptor WindowGetSurface(nint window);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_invalidate")]
    internal static partial void WindowInvalidate(nint window);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_set_cursor")]
    internal static partial void WindowSetCursor(nint window, int cursorShape);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_get_client_size")]
    internal static partial void WindowGetClientSize(nint window, out int width, out int height);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_get_position")]
    internal static partial void WindowGetPosition(nint window, out int x, out int y);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_get_dpi_scale")]
    internal static partial float WindowGetDpiScale(nint window);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_window_get_monitor_refresh_rate")]
    internal static partial int WindowGetMonitorRefreshRate(nint window);

    // --- Event Loop ---

    [LibraryImport(PlatformLib, EntryPoint = "jalium_platform_run_message_loop")]
    internal static partial int PlatformRunMessageLoop();

    [LibraryImport(PlatformLib, EntryPoint = "jalium_platform_poll_events")]
    internal static partial int PlatformPollEvents();

    [LibraryImport(PlatformLib, EntryPoint = "jalium_platform_quit")]
    internal static partial void PlatformQuit(int exitCode);

    // --- Dispatcher ---

    [LibraryImport(PlatformLib, EntryPoint = "jalium_dispatcher_create")]
    internal static partial int DispatcherCreate(out nint dispatcher);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_dispatcher_destroy")]
    internal static partial void DispatcherDestroy(nint dispatcher);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_dispatcher_wake")]
    internal static partial void DispatcherWake(nint dispatcher);

    // --- Timer ---

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_create")]
    internal static partial int TimerCreate(out nint timer);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_destroy")]
    internal static partial void TimerDestroy(nint timer);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_arm")]
    internal static partial void TimerArm(nint timer, long intervalMicroseconds);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_arm_repeating")]
    internal static partial void TimerArmRepeating(nint timer, long intervalMicroseconds);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_disarm")]
    internal static partial void TimerDisarm(nint timer);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_timer_wait")]
    internal static partial int TimerWait(nint timer, uint timeoutMs);

    // --- DPI ---

    [LibraryImport(PlatformLib, EntryPoint = "jalium_platform_get_system_dpi_scale")]
    internal static partial float PlatformGetSystemDpiScale();

    // --- Input ---

    [LibraryImport(PlatformLib, EntryPoint = "jalium_input_get_key_state")]
    internal static partial short InputGetKeyState(int virtualKey);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_input_get_cursor_pos")]
    internal static partial void InputGetCursorPos(out float x, out float y);

    // --- Clipboard ---

    [LibraryImport(PlatformLib, EntryPoint = "jalium_clipboard_get_text")]
    internal static partial int ClipboardGetText(out nint text);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_clipboard_set_text", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int ClipboardSetText(string text);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_platform_free")]
    internal static partial void PlatformFree(nint ptr);

    // --- Android-specific ---

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_set_native_window")]
    internal static partial void AndroidSetNativeWindow(nint nativeWindow);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_set_density")]
    internal static partial void AndroidSetDensity(float density);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_set_refresh_rate")]
    internal static partial void AndroidSetRefreshRate(int refreshRate);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_on_pause")]
    internal static partial void AndroidOnPause();

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_on_resume")]
    internal static partial void AndroidOnResume();

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_on_destroy")]
    internal static partial void AndroidOnDestroy();

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_on_low_memory")]
    internal static partial void AndroidOnLowMemory();

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_set_jni_env")]
    internal static partial void AndroidSetJniEnv(nint javaVM, nint activity);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_set_safe_area_insets")]
    internal static partial void AndroidSetSafeAreaInsets(float top, float bottom, float left, float right);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_set_keyboard_visible")]
    internal static partial void AndroidSetKeyboardVisible(int visible, int heightPx);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_set_orientation")]
    internal static partial void AndroidSetOrientation(int orientation);

    // Input injection (called from managed Activity touch/key overrides)
    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_inject_touch")]
    internal static partial void AndroidInjectTouch(
        int pointerId, float x, float y, float pressure,
        int action, int pointerType, int modifiers);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_inject_key")]
    internal static partial void AndroidInjectKey(
        int androidKeyCode, int scanCode,
        int action, int metaState, int repeatCount);

    [LibraryImport(PlatformLib, EntryPoint = "jalium_android_inject_char")]
    internal static partial void AndroidInjectChar(uint codepoint);

    #endregion
}

/// <summary>
/// Window creation parameters for the platform library.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativePlatformWindowParams
{
    public nint Title;    // wchar_t*
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public uint Style;
    public nint ParentHandle;
}
