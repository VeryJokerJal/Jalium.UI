using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

/// <summary>
/// Rendering backend types.
/// </summary>
public enum RenderBackend
{
    Auto = 0,
    D3D12 = 1,
    D3D11 = 2,
    Vulkan = 3,
    OpenGL = 4,
    Metal = 5,
    Software = 99
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
/// Native method imports for the Jalium rendering engine.
/// </summary>
internal static partial class NativeMethods
{
    private const string CoreLib = "jalium.native.core";
    private const string D3D12Lib = "jalium.native.d3d12";
    private const string VulkanLib = "jalium.native.vulkan";
    private const string MetalLib = "jalium.native.metal";

    /// <summary>
    /// Static constructor to register the D3D12 backend once the native library is available.
    /// </summary>
    static NativeMethods()
    {
        TryInitializeBackend(D3D12Init);
        TryInitializeBackend(VulkanInit);
        TryInitializeBackend(MetalInit);
    }

    [LibraryImport(D3D12Lib, EntryPoint = "jalium_d3d12_init")]
    private static partial void D3D12Init();

    [LibraryImport(VulkanLib, EntryPoint = "jalium_vulkan_init")]
    private static partial void VulkanInit();

    [LibraryImport(MetalLib, EntryPoint = "jalium_metal_init")]
    private static partial void MetalInit();

    private static void TryInitializeBackend(Action init)
    {
        try
        {
            init();
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (BadImageFormatException)
        {
        }
    }

    [LibraryImport(D3D12Lib, EntryPoint = "jalium_d3d12_init")]
    private static partial void D3D12Init();

    #region Context Management

    /// <summary>
    /// Creates a new Jalium rendering context.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_context_create")]
    internal static partial nint ContextCreate(RenderBackend backend);

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
    /// Draws a filled ellipse.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_fill_ellipse")]
    internal static partial void DrawFillEllipse(nint renderTarget, float centerX, float centerY, float radiusX, float radiusY, nint brush);

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
    internal static partial void DrawPolygon(nint renderTarget, float[] points, int pointCount, nint brush, float strokeWidth, int closed);

    /// <summary>
    /// Fills a path with lines and bezier curves.
    /// Commands: tag 0 = LineTo [0,x,y], tag 1 = BezierTo [1,cp1x,cp1y,cp2x,cp2y,ex,ey].
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_fill_path")]
    internal static partial void FillPath(nint renderTarget, float startX, float startY, float[] commands, int commandLength, nint brush, int fillRule);

    /// <summary>
    /// Strokes a path with lines and bezier curves.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_stroke_path")]
    internal static partial void StrokePath(nint renderTarget, float startX, float startY, float[] commands, int commandLength, nint brush, float strokeWidth, int closed);

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
        float x, float y, float w, float h, float progress, int mode);

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
        float x, float y, float w, float h, float radius);

    /// <summary>
    /// Applies a drop shadow effect to the captured element content and draws it.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_draw_drop_shadow_effect")]
    internal static partial void DrawDropShadowEffect(nint renderTarget,
        float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a);

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
        float[] stops, uint stopCount);

    /// <summary>
    /// Creates a radial gradient brush.
    /// </summary>
    [LibraryImport(CoreLib, EntryPoint = "jalium_brush_create_radial_gradient")]
    internal static partial nint BrushCreateRadialGradient(
        nint context,
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        float[] stops, uint stopCount);

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
    internal static partial int IsBackendAvailable(RenderBackend backend);

    #endregion
}
