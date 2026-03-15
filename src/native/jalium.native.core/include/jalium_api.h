#pragma once

#include "jalium_types.h"

// Platform-specific export macros
#ifdef _WIN32
    #if defined(JALIUM_STATIC)
        #define JALIUM_API
    #elif defined(JALIUM_EXPORTS)
        #define JALIUM_API __declspec(dllexport)
    #else
        #define JALIUM_API __declspec(dllimport)
    #endif
#else
    #define JALIUM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// ============================================================================
// Context Management
// ============================================================================

/// Creates a new Jalium rendering context with the specified backend.
/// @param backend The rendering backend to use.
/// @return A handle to the created context, or nullptr on failure.
JALIUM_API JaliumContext* jalium_context_create(JaliumBackend backend);

/// Destroys a Jalium rendering context and releases all associated resources.
/// @param ctx The context to destroy.
JALIUM_API void jalium_context_destroy(JaliumContext* ctx);

/// Gets the backend type of a context.
/// @param ctx The context.
/// @return The backend type.
JALIUM_API JaliumBackend jalium_context_get_backend(JaliumContext* ctx);

/// Gets the last error code.
/// @param ctx The context.
/// @return The error code, or JALIUM_OK if no error.
JALIUM_API JaliumResult jalium_context_get_last_error(JaliumContext* ctx);

/// Gets the last error message.
/// @param ctx The context.
/// @return The error message, or nullptr if no error.
JALIUM_API const wchar_t* jalium_context_get_error_message(JaliumContext* ctx);

// ============================================================================
// Render Target Management
// ============================================================================

/// Creates a render target for a window handle.
/// @param ctx The rendering context.
/// @param hwnd The native window handle (HWND on Windows).
/// @param width The width in pixels.
/// @param height The height in pixels.
/// @return A handle to the created render target, or nullptr on failure.
JALIUM_API JaliumRenderTarget* jalium_render_target_create_for_hwnd(
    JaliumContext* ctx,
    void* hwnd,
    int32_t width,
    int32_t height
);

/// Creates a render target with composition swap chain for per-pixel alpha transparency.
/// Uses CreateSwapChainForComposition + DirectComposition (WinUI 3 / Avalonia style).
/// The window must have WS_EX_NOREDIRECTIONBITMAP extended style.
/// @param ctx The rendering context.
/// @param hwnd The native window handle (HWND on Windows).
/// @param width The width in pixels.
/// @param height The height in pixels.
/// @return A handle to the created render target, or nullptr on failure.
JALIUM_API JaliumRenderTarget* jalium_render_target_create_for_composition(
    JaliumContext* ctx,
    void* hwnd,
    int32_t width,
    int32_t height
);

/// Creates a render target from a platform-neutral surface descriptor.
/// @param ctx The rendering context.
/// @param surface The platform-native surface descriptor.
/// @param width The width in pixels.
/// @param height The height in pixels.
/// @return A handle to the created render target, or nullptr on failure.
JALIUM_API JaliumRenderTarget* jalium_render_target_create_for_surface(
    JaliumContext* ctx,
    const JaliumSurfaceDescriptor* surface,
    int32_t width,
    int32_t height
);

/// Creates a composition-capable render target from a platform-neutral surface descriptor.
JALIUM_API JaliumRenderTarget* jalium_render_target_create_for_composition_surface(
    JaliumContext* ctx,
    const JaliumSurfaceDescriptor* surface,
    int32_t width,
    int32_t height
);

/// Destroys a render target.
/// @param rt The render target to destroy.
JALIUM_API void jalium_render_target_destroy(JaliumRenderTarget* rt);

/// Resizes a render target.
/// @param rt The render target.
/// @param width The new width in pixels.
/// @param height The new height in pixels.
/// @return JALIUM_OK on success.
JALIUM_API JaliumResult jalium_render_target_resize(JaliumRenderTarget* rt, int32_t width, int32_t height);

/// Begins a drawing session on the render target.
/// @param rt The render target.
/// @return JALIUM_OK on success.
JALIUM_API JaliumResult jalium_render_target_begin_draw(JaliumRenderTarget* rt);

/// Ends a drawing session and presents the result.
/// @param rt The render target.
/// @return JALIUM_OK on success.
JALIUM_API JaliumResult jalium_render_target_end_draw(JaliumRenderTarget* rt);

/// Clears the render target with a color.
/// @param rt The render target.
/// @param r Red component (0.0 - 1.0).
/// @param g Green component (0.0 - 1.0).
/// @param b Blue component (0.0 - 1.0).
/// @param a Alpha component (0.0 - 1.0).
JALIUM_API void jalium_render_target_clear(JaliumRenderTarget* rt, float r, float g, float b, float a);

/// Sets whether VSync is enabled for the render target.
/// When disabled, Present returns immediately for faster frame updates during resize.
/// @param rt The render target.
/// @param enabled 1 to enable VSync, 0 to disable.
JALIUM_API void jalium_render_target_set_vsync(JaliumRenderTarget* rt, int32_t enabled);

/// Sets the DPI for the render target.
/// Updates D2D context so DIP coordinates are correctly mapped to physical pixels.
/// @param rt The render target.
/// @param dpiX Horizontal DPI (96 = 100% scaling).
/// @param dpiY Vertical DPI (96 = 100% scaling).
JALIUM_API void jalium_render_target_set_dpi(JaliumRenderTarget* rt, float dpiX, float dpiY);

// ============================================================================
// Dirty Rect Management
// ============================================================================

/// Adds a dirty rectangle for partial rendering optimization.
/// @param rt The render target.
/// @param x X coordinate.
/// @param y Y coordinate.
/// @param width Width.
/// @param height Height.
JALIUM_API void jalium_render_target_add_dirty_rect(JaliumRenderTarget* rt, float x, float y, float width, float height);

/// Marks the entire render target as needing full redraw.
/// @param rt The render target.
JALIUM_API void jalium_render_target_set_full_invalidation(JaliumRenderTarget* rt);

/// Creates a composition visual node for embedding external content (e.g. WebView).
/// On Windows this returns an IUnknown* pointer in visual_out.
/// Caller must eventually destroy it via jalium_render_target_destroy_webview_visual.
/// @param rt The render target.
/// @param visual_out Output visual pointer.
/// @return JALIUM_OK on success.
JALIUM_API JaliumResult jalium_render_target_create_webview_visual(
    JaliumRenderTarget* rt,
    void** visual_out);

/// Destroys a composition visual previously created by jalium_render_target_create_webview_visual.
/// @param rt The render target.
/// @param visual The visual pointer to destroy.
/// @return JALIUM_OK on success.
JALIUM_API JaliumResult jalium_render_target_destroy_webview_visual(
    JaliumRenderTarget* rt,
    void* visual);

/// Updates the placement and clip rectangle of a composition visual previously created
/// by jalium_render_target_create_webview_visual.
/// @param rt The render target.
/// @param visual The visual pointer to update.
/// @param x Left coordinate in composition space.
/// @param y Top coordinate in composition space.
/// @param width Visible width.
/// @param height Visible height.
/// @param content_offset_x Content X offset inside the clipped host region.
/// @param content_offset_y Content Y offset inside the clipped host region.
/// @return JALIUM_OK on success.
JALIUM_API JaliumResult jalium_render_target_set_webview_visual_placement(
    JaliumRenderTarget* rt,
    void* visual,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height,
    int32_t content_offset_x,
    int32_t content_offset_y);

// ============================================================================
// Drawing Commands
// ============================================================================

/// Draws a filled rectangle.
/// @param rt The render target.
/// @param x The x coordinate.
/// @param y The y coordinate.
/// @param width The width.
/// @param height The height.
/// @param brush The brush to fill with.
JALIUM_API void jalium_draw_fill_rectangle(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    JaliumBrush* brush
);

/// Draws a rectangle outline.
/// @param rt The render target.
/// @param x The x coordinate.
/// @param y The y coordinate.
/// @param width The width.
/// @param height The height.
/// @param brush The brush for the outline.
/// @param strokeWidth The stroke width.
JALIUM_API void jalium_draw_rectangle(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    JaliumBrush* brush,
    float strokeWidth
);

/// Draws a filled ellipse.
/// @param rt The render target.
/// @param centerX The center x coordinate.
/// @param centerY The center y coordinate.
/// @param radiusX The x radius.
/// @param radiusY The y radius.
/// @param brush The brush to fill with.
JALIUM_API void jalium_draw_fill_ellipse(
    JaliumRenderTarget* rt,
    float centerX, float centerY, float radiusX, float radiusY,
    JaliumBrush* brush
);

/// Draws an ellipse outline.
/// @param rt The render target.
/// @param centerX The center x coordinate.
/// @param centerY The center y coordinate.
/// @param radiusX The x radius.
/// @param radiusY The y radius.
/// @param brush The brush for the outline.
/// @param strokeWidth The stroke width.
JALIUM_API void jalium_draw_ellipse(
    JaliumRenderTarget* rt,
    float centerX, float centerY, float radiusX, float radiusY,
    JaliumBrush* brush,
    float strokeWidth
);

/// Draws a line.
/// @param rt The render target.
/// @param x1 The start x coordinate.
/// @param y1 The start y coordinate.
/// @param x2 The end x coordinate.
/// @param y2 The end y coordinate.
/// @param brush The brush for the line.
/// @param strokeWidth The stroke width.
JALIUM_API void jalium_draw_line(
    JaliumRenderTarget* rt,
    float x1, float y1, float x2, float y2,
    JaliumBrush* brush,
    float strokeWidth
);

/// Draws text.
/// @param rt The render target.
/// @param text The text to draw.
/// @param textLength The length of the text (number of characters).
/// @param format The text format.
/// @param x The x coordinate of the layout box.
/// @param y The y coordinate of the layout box.
/// @param width The width of the layout box.
/// @param height The height of the layout box.
/// @param brush The brush for the text.
JALIUM_API void jalium_draw_text(
    JaliumRenderTarget* rt,
    const wchar_t* text,
    uint32_t textLength,
    JaliumTextFormat* format,
    float x, float y, float width, float height,
    JaliumBrush* brush
);

/// Draws a filled rounded rectangle.
/// @param rt The render target.
/// @param x The x coordinate.
/// @param y The y coordinate.
/// @param width The width.
/// @param height The height.
/// @param radiusX The x radius of the corners.
/// @param radiusY The y radius of the corners.
/// @param brush The brush to fill with.
JALIUM_API void jalium_draw_fill_rounded_rectangle(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    float radiusX, float radiusY,
    JaliumBrush* brush
);

/// Draws a rounded rectangle outline.
/// @param rt The render target.
/// @param x The x coordinate.
/// @param y The y coordinate.
/// @param width The width.
/// @param height The height.
/// @param radiusX The x radius of the corners.
/// @param radiusY The y radius of the corners.
/// @param brush The brush for the outline.
/// @param strokeWidth The stroke width.
JALIUM_API void jalium_draw_rounded_rectangle(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    float radiusX, float radiusY,
    JaliumBrush* brush,
    float strokeWidth
);

// ============================================================================
// Transform Stack
// ============================================================================

/// Pushes a transform matrix onto the stack.
/// @param rt The render target.
/// @param matrix The 3x2 transform matrix (column-major, 6 floats).
JALIUM_API void jalium_push_transform(JaliumRenderTarget* rt, const float* matrix);

/// Pops the top transform from the stack.
/// @param rt The render target.
JALIUM_API void jalium_pop_transform(JaliumRenderTarget* rt);

/// Pushes a clip rectangle onto the stack.
/// @param rt The render target.
/// @param x The x coordinate.
/// @param y The y coordinate.
/// @param width The width.
/// @param height The height.
JALIUM_API void jalium_push_clip(JaliumRenderTarget* rt, float x, float y, float width, float height);

/// Pushes a clip rectangle with ALIASED anti-aliasing (hard pixel boundary).
/// Used for dirty region clips where semi-transparent edges cause artifacts.
/// @param rt The render target.
/// @param x The x coordinate.
/// @param y The y coordinate.
/// @param width The width.
/// @param height The height.
JALIUM_API void jalium_push_clip_aliased(JaliumRenderTarget* rt, float x, float y, float width, float height);

/// Pushes a rounded rectangle clip onto the stack using a geometry mask layer.
/// @param rt The render target.
/// @param x The x coordinate.
/// @param y The y coordinate.
/// @param width The width.
/// @param height The height.
/// @param rx The x radius of the corners.
/// @param ry The y radius of the corners.
JALIUM_API void jalium_push_rounded_rect_clip(JaliumRenderTarget* rt, float x, float y, float width, float height, float rx, float ry);

/// Pops the top clip from the stack.
/// @param rt The render target.
JALIUM_API void jalium_pop_clip(JaliumRenderTarget* rt);

/// Punches a transparent rectangular hole in the current render target.
/// @param rt The render target.
/// @param x The x coordinate.
/// @param y The y coordinate.
/// @param width The width.
/// @param height The height.
JALIUM_API void jalium_punch_transparent_rect(JaliumRenderTarget* rt, float x, float y, float width, float height);

/// Pushes an opacity value onto the stack.
/// @param rt The render target.
/// @param opacity The opacity (0.0 - 1.0).
JALIUM_API void jalium_push_opacity(JaliumRenderTarget* rt, float opacity);

/// Pops the top opacity from the stack.
/// @param rt The render target.
JALIUM_API void jalium_pop_opacity(JaliumRenderTarget* rt);

// ============================================================================
// Brush Management
// ============================================================================

/// Creates a solid color brush.
/// @param ctx The rendering context.
/// @param r Red component (0.0 - 1.0).
/// @param g Green component (0.0 - 1.0).
/// @param b Blue component (0.0 - 1.0).
/// @param a Alpha component (0.0 - 1.0).
/// @return A handle to the created brush, or nullptr on failure.
JALIUM_API JaliumBrush* jalium_brush_create_solid(JaliumContext* ctx, float r, float g, float b, float a);

/// Creates a linear gradient brush.
/// @param ctx The rendering context.
/// @param startX The start x coordinate.
/// @param startY The start y coordinate.
/// @param endX The end x coordinate.
/// @param endY The end y coordinate.
/// @param stops Array of gradient stops (position, r, g, b, a for each stop).
/// @param stopCount Number of gradient stops.
/// @return A handle to the created brush, or nullptr on failure.
JALIUM_API JaliumBrush* jalium_brush_create_linear_gradient(
    JaliumContext* ctx,
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops,
    uint32_t stopCount
);

/// Creates a radial gradient brush.
/// @param ctx The rendering context.
/// @param centerX The center x coordinate.
/// @param centerY The center y coordinate.
/// @param radiusX The x radius.
/// @param radiusY The y radius.
/// @param originX The gradient origin x coordinate.
/// @param originY The gradient origin y coordinate.
/// @param stops Array of gradient stops.
/// @param stopCount Number of gradient stops.
/// @return A handle to the created brush, or nullptr on failure.
JALIUM_API JaliumBrush* jalium_brush_create_radial_gradient(
    JaliumContext* ctx,
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops,
    uint32_t stopCount
);

/// Destroys a brush.
/// @param brush The brush to destroy.
JALIUM_API void jalium_brush_destroy(JaliumBrush* brush);

// ============================================================================
// Text Format Management
// ============================================================================

/// Creates a text format.
/// @param ctx The rendering context.
/// @param fontFamily The font family name.
/// @param fontSize The font size in DIPs.
/// @param fontWeight The font weight (100-900, 400 = normal, 700 = bold).
/// @param fontStyle The font style (0 = normal, 1 = italic, 2 = oblique).
/// @return A handle to the created text format, or nullptr on failure.
JALIUM_API JaliumTextFormat* jalium_text_format_create(
    JaliumContext* ctx,
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle
);

/// Destroys a text format.
/// @param format The text format to destroy.
JALIUM_API void jalium_text_format_destroy(JaliumTextFormat* format);

/// Sets the text alignment.
/// @param format The text format.
/// @param alignment The alignment (0 = leading, 1 = trailing, 2 = center, 3 = justified).
JALIUM_API void jalium_text_format_set_alignment(JaliumTextFormat* format, int32_t alignment);

/// Sets the paragraph alignment.
/// @param format The text format.
/// @param alignment The alignment (0 = near/top, 1 = far/bottom, 2 = center).
JALIUM_API void jalium_text_format_set_paragraph_alignment(JaliumTextFormat* format, int32_t alignment);

/// Sets the text trimming mode.
/// @param format The text format.
/// @param trimming The trimming mode (0 = none, 1 = character ellipsis, 2 = word ellipsis).
JALIUM_API void jalium_text_format_set_trimming(JaliumTextFormat* format, int32_t trimming);

/// Measures text and returns metrics.
/// Uses DirectWrite's IDWriteTextLayout for accurate measurement.
/// @param format The text format.
/// @param text The text to measure.
/// @param textLength The length of the text (number of characters).
/// @param maxWidth The maximum width constraint (use a large value for no constraint).
/// @param maxHeight The maximum height constraint (use a large value for no constraint).
/// @param metrics Output structure to receive the text metrics.
/// @return JALIUM_OK on success.
JALIUM_API JaliumResult jalium_text_format_measure_text(
    JaliumTextFormat* format,
    const wchar_t* text,
    uint32_t textLength,
    float maxWidth,
    float maxHeight,
    JaliumTextMetrics* metrics
);

/// Gets font metrics for a text format.
/// Returns the font's ascent, descent, and line gap without needing text.
/// @param format The text format.
/// @param metrics Output structure to receive the metrics.
/// @return JALIUM_OK on success.
JALIUM_API JaliumResult jalium_text_format_get_font_metrics(
    JaliumTextFormat* format,
    JaliumTextMetrics* metrics
);

// ============================================================================
// Bitmap Management
// ============================================================================

/// Creates a bitmap from encoded image data (PNG, JPEG, etc.).
/// @param ctx The rendering context.
/// @param data The encoded image data.
/// @param dataSize The size of the data in bytes.
/// @return A handle to the created bitmap, or nullptr on failure.
JALIUM_API JaliumImage* jalium_bitmap_create_from_memory(
    JaliumContext* ctx,
    const uint8_t* data,
    uint32_t dataSize
);

/// Creates a bitmap from raw BGRA8 pixel data.
/// @param ctx The rendering context.
/// @param pixels Raw BGRA8 pixel buffer.
/// @param width The bitmap width in pixels.
/// @param height The bitmap height in pixels.
/// @param stride The number of bytes between two adjacent rows.
/// @return A handle to the created bitmap, or nullptr on failure.
JALIUM_API JaliumImage* jalium_bitmap_create_from_pixels(
    JaliumContext* ctx,
    const uint8_t* pixels,
    uint32_t width,
    uint32_t height,
    uint32_t stride
);

/// Gets the width of a bitmap.
/// @param bitmap The bitmap.
/// @return The width in pixels.
JALIUM_API uint32_t jalium_bitmap_get_width(JaliumImage* bitmap);

/// Gets the height of a bitmap.
/// @param bitmap The bitmap.
/// @return The height in pixels.
JALIUM_API uint32_t jalium_bitmap_get_height(JaliumImage* bitmap);

/// Destroys a bitmap.
/// @param bitmap The bitmap to destroy.
JALIUM_API void jalium_bitmap_destroy(JaliumImage* bitmap);

/// Draws a bitmap.
/// @param rt The render target.
/// @param bitmap The bitmap to draw.
/// @param x The destination x coordinate.
/// @param y The destination y coordinate.
/// @param width The destination width.
/// @param height The destination height.
/// @param opacity The opacity (0.0 - 1.0).
JALIUM_API void jalium_draw_bitmap(
    JaliumRenderTarget* rt,
    JaliumImage* bitmap,
    float x, float y, float width, float height,
    float opacity
);

// ============================================================================
// Backend Registration
// ============================================================================

/// Registers a rendering backend.
/// @param backend The backend type.
/// @param factory The factory function to create the backend.
/// @return JALIUM_OK on success.
JALIUM_API JaliumResult jalium_register_backend(JaliumBackend backend, JaliumBackendFactory factory);

/// Registers a rendering backend with an optional availability probe.
/// @param backend The backend type.
/// @param factory The factory function to create the backend.
/// @param availability Optional callback that returns 1 when the backend can run
/// on the current machine. Null means "registered implies available".
/// @return JALIUM_OK on success.
JALIUM_API JaliumResult jalium_register_backend_ex(
    JaliumBackend backend,
    JaliumBackendFactory factory,
    JaliumBackendAvailabilityCallback availability);

/// Checks if a backend is available.
/// @param backend The backend type.
/// @return 1 if available, 0 if not.
JALIUM_API int32_t jalium_is_backend_available(JaliumBackend backend);

// ============================================================================
// Desktop Backdrop
// ============================================================================

/// Captures the desktop area at the specified screen coordinates.
/// Uses BitBlt from the screen DC to capture what's visible on screen.
/// The captured content is cached internally for use by jalium_draw_desktop_backdrop.
/// @param rt The render target.
/// @param screenX Screen X coordinate.
/// @param screenY Screen Y coordinate.
/// @param width Width to capture.
/// @param height Height to capture.
JALIUM_API void jalium_capture_desktop_area(
    JaliumRenderTarget* rt,
    int32_t screenX, int32_t screenY,
    int32_t width, int32_t height
);

/// Draws the cached desktop capture with Gaussian blur and tint overlay.
/// Must call jalium_capture_desktop_area first to populate the cached capture.
/// @param rt The render target.
/// @param x Destination X in render target coordinates.
/// @param y Destination Y in render target coordinates.
/// @param w Destination width.
/// @param h Destination height.
/// @param blurRadius Blur radius in pixels.
/// @param tintR Tint color red component (0-1).
/// @param tintG Tint color green component (0-1).
/// @param tintB Tint color blue component (0-1).
/// @param tintOpacity Tint overlay opacity (0-1).
/// @param noiseIntensity Noise overlay intensity (0-1).
/// @param saturation Saturation adjustment (1.0 = no change).
JALIUM_API void jalium_draw_desktop_backdrop(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h,
    float blurRadius,
    float tintR, float tintG, float tintB, float tintOpacity,
    float noiseIntensity, float saturation
);

// ============================================================================
// Content Transition Shader
// ============================================================================

/// Begins capturing content into an offscreen bitmap for transition shader effects.
/// @param rt The render target.
/// @param slot 0 = old content, 1 = new content.
/// @param x X position of the transition area (in DIPs).
/// @param y Y position of the transition area (in DIPs).
/// @param w Width of the transition area (in DIPs).
/// @param h Height of the transition area (in DIPs).
JALIUM_API void jalium_transition_begin_capture(JaliumRenderTarget* rt, int32_t slot,
    float x, float y, float w, float h);

/// Ends capturing content for a transition slot and restores the main render target.
/// @param rt The render target.
/// @param slot 0 = old content, 1 = new content.
JALIUM_API void jalium_transition_end_capture(JaliumRenderTarget* rt, int32_t slot);

/// Draws the transition shader effect blending old and new content bitmaps.
/// @param rt The render target.
/// @param x X position of the transition area (in DIPs).
/// @param y Y position of the transition area (in DIPs).
/// @param w Width of the transition area (in DIPs).
/// @param h Height of the transition area (in DIPs).
/// @param progress Transition progress (0.0 - 1.0).
/// @param mode Shader mode index (0-9).
JALIUM_API void jalium_draw_transition_shader(JaliumRenderTarget* rt,
    float x, float y, float w, float h, float progress, int32_t mode);

/// Draws a previously captured transition bitmap to the current render target.
/// @param rt Render target handle.
/// @param slot Transition slot to draw from (0 or 1).
/// @param x Destination X position (in DIPs).
/// @param y Destination Y position (in DIPs).
/// @param w Destination width (in DIPs).
/// @param h Destination height (in DIPs).
/// @param opacity Opacity to apply (0.0 - 1.0).
JALIUM_API void jalium_draw_captured_transition(JaliumRenderTarget* rt,
    int32_t slot, float x, float y, float w, float h, float opacity);

// ============================================================================
// Liquid Glass Effect
// ============================================================================

/// Draws a liquid glass effect with SDF-based refraction, highlight, and inner shadow.
/// Captures current render target content, applies Gaussian blur, then renders
/// the full liquid glass pipeline (refraction + chromatic aberration + edge highlight
/// + inner shadow + composite) in a single pass.
/// @param rt The render target.
/// @param x X position of the glass panel.
/// @param y Y position of the glass panel.
/// @param w Width of the glass panel.
/// @param h Height of the glass panel.
/// @param cornerRadius Border radius in pixels.
/// @param blurRadius Gaussian blur radius in pixels (default 8).
/// @param refractionAmount UV displacement strength in pixels (default 60).
/// @param chromaticAberration Color dispersion 0-1 (0=off, 1=full).
/// @param tintR Tint color red component (0-1).
/// @param tintG Tint color green component (0-1).
/// @param tintB Tint color blue component (0-1).
/// @param tintOpacity Tint overlay opacity (0-1).
// ============================================================================
// Element Effect Capture & Rendering
// ============================================================================

/// Begins capturing element content into an offscreen bitmap for effect processing.
/// All subsequent drawing calls will be redirected to the offscreen bitmap.
/// @param rt The render target.
/// @param x X position of the capture area (in DIPs).
/// @param y Y position of the capture area (in DIPs).
/// @param w Width of the capture area (in DIPs).
/// @param h Height of the capture area (in DIPs).
JALIUM_API void jalium_effect_begin_capture(JaliumRenderTarget* rt,
    float x, float y, float w, float h);

/// Ends capturing element content and restores the main render target.
/// @param rt The render target.
JALIUM_API void jalium_effect_end_capture(JaliumRenderTarget* rt);

/// Applies a Gaussian blur effect to the captured element content and draws it.
/// Must be called after jalium_effect_begin_capture / jalium_effect_end_capture.
/// @param rt The render target.
/// @param x X position to draw at (in DIPs).
/// @param y Y position to draw at (in DIPs).
/// @param w Width of the draw area (in DIPs).
/// @param h Height of the draw area (in DIPs).
/// @param radius Blur radius (in DIPs).
JALIUM_API void jalium_draw_blur_effect(JaliumRenderTarget* rt,
    float x, float y, float w, float h, float radius);

/// Applies a drop shadow effect to the captured element content and draws it.
/// Draws the shadow (offset + blurred alpha) behind the original content.
/// Must be called after jalium_effect_begin_capture / jalium_effect_end_capture.
/// @param rt The render target.
/// @param x X position to draw at (in DIPs).
/// @param y Y position to draw at (in DIPs).
/// @param w Width of the draw area (in DIPs).
/// @param h Height of the draw area (in DIPs).
/// @param blurRadius Shadow blur radius (in DIPs).
/// @param offsetX Shadow X offset (in DIPs).
/// @param offsetY Shadow Y offset (in DIPs).
/// @param r Shadow color red (0-1).
/// @param g Shadow color green (0-1).
/// @param b Shadow color blue (0-1).
/// @param a Shadow opacity (0-1).
JALIUM_API void jalium_draw_drop_shadow_effect(JaliumRenderTarget* rt,
    float x, float y, float w, float h,
    float blurRadius, float offsetX, float offsetY,
    float r, float g, float b, float a);

// ============================================================================
// Liquid Glass Effect
// ============================================================================

// ============================================================================
// Content Border (U-shape, no top edge)
// ============================================================================

/// Draws a content border with rounded bottom corners (U-shape, no top edge).
/// The fill covers the full rectangle with bottom rounded corners.
/// The stroke draws only left + bottom + right edges (open top).
/// @param rt The render target.
/// @param x The x coordinate.
/// @param y The y coordinate.
/// @param width The width.
/// @param height The height.
/// @param blRadius Bottom-left corner radius.
/// @param brRadius Bottom-right corner radius.
/// @param fillBrush The brush for the fill (nullable).
/// @param strokeBrush The brush for the stroke (nullable).
/// @param strokeWidth The stroke width.
JALIUM_API void jalium_draw_content_border(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    float blRadius, float brRadius,
    JaliumBrush* fillBrush, JaliumBrush* strokeBrush,
    float strokeWidth
);

JALIUM_API void jalium_draw_liquid_glass(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h,
    float cornerRadius,
    float blurRadius,
    float refractionAmount,
    float chromaticAberration,
    float tintR, float tintG, float tintB, float tintOpacity,
    float lightX, float lightY,
    float highlightBoost,
    int32_t shapeType,
    float shapeExponent,
    int32_t neighborCount,
    float fusionRadius,
    const float* neighborData
);

#ifdef __cplusplus
}
#endif
