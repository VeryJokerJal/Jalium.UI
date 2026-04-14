#pragma once

#include "jalium_types.h"
#include <algorithm>
#include <memory>
#include <string>

namespace jalium {

// Forward declarations
class RenderTarget;
class Brush;
class TextFormat;
class Bitmap;

/// Abstract interface for rendering backends.
/// Each rendering backend (D3D12, Vulkan, etc.) implements this interface.
class IRenderBackend {
public:
    virtual ~IRenderBackend() = default;

    /// Gets the backend type.
    virtual JaliumBackend GetType() const = 0;

    /// Gets the backend name for debugging.
    virtual const wchar_t* GetName() const = 0;

    /// Checks if the GPU device is still operational.
    /// Returns JALIUM_OK if the device is healthy, non-zero if device is lost.
    virtual JaliumResult CheckDeviceStatus() { return JALIUM_OK; }

    /// Creates a render target for a window handle.
    virtual RenderTarget* CreateRenderTarget(void* hwnd, int32_t width, int32_t height) = 0;

    /// Creates a render target with composition swap chain for per-pixel alpha transparency.
    virtual RenderTarget* CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height) = 0;

    /// Creates a render target from a platform-neutral surface descriptor.
    /// Default implementation preserves the legacy HWND-style path by forwarding
    /// handle0 as the native window handle.
    virtual RenderTarget* CreateRenderTargetForSurface(
        const JaliumSurfaceDescriptor* surface,
        int32_t width,
        int32_t height)
    {
        if (!surface || surface->handle0 == 0) {
            return nullptr;
        }

        return CreateRenderTarget(reinterpret_cast<void*>(surface->handle0), width, height);
    }

    /// Creates a composition-capable render target from a platform-neutral surface descriptor.
    virtual RenderTarget* CreateRenderTargetForCompositionSurface(
        const JaliumSurfaceDescriptor* surface,
        int32_t width,
        int32_t height)
    {
        if (!surface || surface->handle0 == 0) {
            return nullptr;
        }

        return CreateRenderTargetForComposition(reinterpret_cast<void*>(surface->handle0), width, height);
    }

    /// Creates a solid color brush.
    virtual Brush* CreateSolidBrush(float r, float g, float b, float a) = 0;

    /// Creates a linear gradient brush.
    /// spreadMethod: 0=Pad (clamp), 1=Repeat (tile), 2=Reflect (mirror)
    virtual Brush* CreateLinearGradientBrush(
        float startX, float startY, float endX, float endY,
        const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0) = 0;

    /// Creates a radial gradient brush.
    /// spreadMethod: 0=Pad (clamp), 1=Repeat (tile), 2=Reflect (mirror)
    virtual Brush* CreateRadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0) = 0;

    /// Creates a text format.
    virtual TextFormat* CreateTextFormat(
        const wchar_t* fontFamily,
        float fontSize,
        int32_t fontWeight,
        int32_t fontStyle) = 0;

    /// Creates a bitmap from encoded image data (PNG, JPEG, etc.).
    virtual Bitmap* CreateBitmapFromMemory(const uint8_t* data, uint32_t dataSize) = 0;

    /// Creates a bitmap from raw BGRA8 pixel data.
    virtual Bitmap* CreateBitmapFromPixels(
        const uint8_t* pixels,
        uint32_t width,
        uint32_t height,
        uint32_t stride)
    {
        (void)pixels;
        (void)width;
        (void)height;
        (void)stride;
        return nullptr;
    }

};

/// Abstract base class for render targets.
class RenderTarget {
public:
    virtual ~RenderTarget() = default;

    /// Creates a composition visual node that can host embedded content (e.g. WebView).
    /// The returned pointer is backend-specific (IUnknown* on Windows) and reference-counted.
    virtual JaliumResult CreateWebViewVisual(void** visualOut)
    {
        if (visualOut) {
            *visualOut = nullptr;
        }
        return JALIUM_ERROR_NOT_SUPPORTED;
    }

    /// Destroys a previously created composition visual node.
    virtual JaliumResult DestroyWebViewVisual(void* visual)
    {
        (void)visual;
        return JALIUM_ERROR_NOT_SUPPORTED;
    }

    /// Updates the placement of a previously created composition visual node.
    /// x/y/width/height describe the visible host region, and contentOffsetX/Y shift
    /// the content inside that clipped region when the control is partially occluded.
    virtual JaliumResult SetWebViewVisualPlacement(
        void* visual,
        int32_t x,
        int32_t y,
        int32_t width,
        int32_t height,
        int32_t contentOffsetX,
        int32_t contentOffsetY)
    {
        (void)visual;
        (void)x;
        (void)y;
        (void)width;
        (void)height;
        (void)contentOffsetX;
        (void)contentOffsetY;
        return JALIUM_ERROR_NOT_SUPPORTED;
    }

    /// Resizes the render target.
    virtual JaliumResult Resize(int32_t width, int32_t height) = 0;

    /// Begins a drawing session.
    virtual JaliumResult BeginDraw() = 0;

    /// Ends a drawing session and presents.
    virtual JaliumResult EndDraw() = 0;

    /// Clears with a color.
    virtual void Clear(float r, float g, float b, float a) = 0;

    /// Draws a filled rectangle.
    virtual void FillRectangle(float x, float y, float w, float h, Brush* brush) = 0;

    /// Draws a rectangle outline.
    virtual void DrawRectangle(float x, float y, float w, float h, Brush* brush, float strokeWidth) = 0;

    /// Draws a filled rounded rectangle.
    virtual void FillRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush) = 0;

    /// Draws a rounded rectangle outline.
    virtual void DrawRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush, float strokeWidth) = 0;

    /// Draws a filled rounded rectangle with per-corner radii.
    virtual void FillPerCornerRoundedRectangle(float x, float y, float w, float h,
        float tl, float tr, float br, float bl, Brush* brush)
    {
        float maxR = std::max(std::max(tl, tr), std::max(br, bl));
        FillRoundedRectangle(x, y, w, h, maxR, maxR, brush);
    }

    /// Draws a rounded rectangle outline with per-corner radii.
    virtual void DrawPerCornerRoundedRectangle(float x, float y, float w, float h,
        float tl, float tr, float br, float bl, Brush* brush, float strokeWidth)
    {
        float maxR = std::max(std::max(tl, tr), std::max(br, bl));
        DrawRoundedRectangle(x, y, w, h, maxR, maxR, brush, strokeWidth);
    }

    /// Draws a filled ellipse.
    virtual void FillEllipse(float cx, float cy, float rx, float ry, Brush* brush) = 0;

    /// Draws a batch of filled ellipses with per-ellipse color.
    /// data layout per ellipse: [cx, cy, rx, ry, colorRGBA_packed_as_float] × count.
    /// Default implementation is a no-op; backends should override for efficient batch rendering.
    virtual void FillEllipseBatch(const float* data, uint32_t count) { (void)data; (void)count; }

    /// Draws an ellipse outline.
    virtual void DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth) = 0;

    /// Draws a line.
    virtual void DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth) = 0;

    /// Fills a polygon defined by an array of points.
    /// @param points Array of point coordinates (x0, y0, x1, y1, ...).
    /// @param pointCount Number of points (length of array / 2).
    /// @param brush Brush to fill with.
    /// @param fillRule 0 = EvenOdd, 1 = NonZero (Winding).
    virtual void FillPolygon(const float* points, uint32_t pointCount, Brush* brush, int32_t fillRule) = 0;

    /// Draws a polygon outline.
    /// @param points Array of point coordinates (x0, y0, x1, y1, ...).
    /// @param pointCount Number of points (length of array / 2).
    /// @param brush Brush for stroke.
    /// @param strokeWidth Width of stroke.
    /// @param closed Whether to close the polygon.
    virtual void DrawPolygon(const float* points, uint32_t pointCount, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin = 0, float miterLimit = 10.0f) = 0;

    /// Fills a path defined by a command buffer (lines + bezier curves).
    /// Command encoding: tag 0 = LineTo [0,x,y], tag 1 = BezierTo [1,cp1x,cp1y,cp2x,cp2y,ex,ey].
    virtual void FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule) = 0;

    /// Strokes a path defined by a command buffer (lines + bezier curves).
    /// lineCap: 0 = Butt, 1 = Square, 2 = Round.
    virtual void StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin = 0, float miterLimit = 10.0f, int32_t lineCap = 0,
        const float* dashPattern = nullptr, uint32_t dashCount = 0, float dashOffset = 0.0f) = 0;

    /// Draws a content area border: fills a rect with bottom-only rounded corners,
    /// then strokes a U-shape (left + bottom + right, no top) with the same radii.
    /// @param x,y,w,h The content area rectangle.
    /// @param blRadius Bottom-left corner radius.
    /// @param brRadius Bottom-right corner radius.
    /// @param fillBrush Brush for background fill (may be null).
    /// @param strokeBrush Brush for border stroke (may be null).
    /// @param strokeWidth Border line width.
    virtual void DrawContentBorder(float x, float y, float w, float h,
        float blRadius, float brRadius,
        Brush* fillBrush, Brush* strokeBrush, float strokeWidth) = 0;

    /// Draws text.
    virtual void RenderText(
        const wchar_t* text, uint32_t textLength,
        TextFormat* format,
        float x, float y, float w, float h,
        Brush* brush) = 0;

    /// Pushes a transform.
    virtual void PushTransform(const float* matrix) = 0;

    /// Pops a transform.
    virtual void PopTransform() = 0;

    /// Pushes a clip rectangle (PER_PRIMITIVE anti-aliasing — smooth edges).
    virtual void PushClip(float x, float y, float w, float h) = 0;

    /// Pushes a clip rectangle with ALIASED anti-aliasing (hard pixel boundary).
    /// Used for dirty region clips where semi-transparent edges would cause artifacts.
    virtual void PushClipAliased(float x, float y, float w, float h) { PushClip(x, y, w, h); }

    /// Pushes a rounded rectangle clip using a geometry mask layer.
    virtual void PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry) = 0;

    /// Pops a clip.
    virtual void PopClip() = 0;

    /// Punches a transparent rectangular hole in the current render target.
    virtual void PunchTransparentRect(float x, float y, float w, float h) = 0;

    /// Pushes an opacity.
    virtual void PushOpacity(float opacity) = 0;

    /// Pops an opacity.
    virtual void PopOpacity() = 0;

    /// Sets the current shape type for SDF rect rendering.
    /// type: 0 = RoundedRect, 1 = SuperEllipse.  n: exponent (e.g. 4 for squircle).
    virtual void SetShapeType(int type, float n) = 0;

    /// Sets whether VSync is enabled.
    /// When disabled, Present returns immediately for faster frame updates during resize.
    virtual void SetVSyncEnabled(bool enabled) = 0;

    /// Sets the DPI for the render target.
    /// Updates D2D context DPI so DIP coordinates are correctly mapped to physical pixels.
    /// @param dpiX Horizontal DPI (96 = 100% scaling).
    /// @param dpiY Vertical DPI (96 = 100% scaling).
    virtual void SetDpi(float dpiX, float dpiY) = 0;

    /// Adds a dirty rectangle to the current frame's dirty list.
    /// The rectangle will be used for partial rendering optimization.
    /// @param x X coordinate.
    /// @param y Y coordinate.
    /// @param w Width.
    /// @param h Height.
    virtual void AddDirtyRect(float x, float y, float w, float h) = 0;

    /// Marks the entire render target as dirty, forcing a full redraw.
    virtual void SetFullInvalidation() = 0;

    /// Returns whether this render target preserves back-buffer contents across presents,
    /// allowing partial redraw + dirty-rect presentation.
    virtual bool SupportsPartialPresentation() const { return true; }

    /// Draws a bitmap.
    virtual void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity) = 0;

    /// Draws a backdrop filter effect.
    /// @param x X position.
    /// @param y Y position.
    /// @param w Width.
    /// @param h Height.
    /// @param backdropFilter CSS-style backdrop filter string (e.g., "blur(20px)").
    /// @param material Material type (e.g., "acrylic", "mica").
    /// @param materialTint Tint color in hex format.
    /// @param tintOpacity Tint opacity (0-1).
    /// @param blurRadius Blur radius in pixels.
    /// @param cornerRadiusTL Top-left corner radius.
    /// @param cornerRadiusTR Top-right corner radius.
    /// @param cornerRadiusBR Bottom-right corner radius.
    /// @param cornerRadiusBL Bottom-left corner radius.
    virtual void DrawBackdropFilter(
        float x, float y, float w, float h,
        const char* backdropFilter,
        const char* material,
        const char* materialTint,
        float tintOpacity,
        float blurRadius,
        float cornerRadiusTL, float cornerRadiusTR,
        float cornerRadiusBR, float cornerRadiusBL) = 0;

    /// Draws a glowing border highlight effect for DevTools element inspection.
    /// Creates an animated glowing line that follows the element border with:
    /// - Gradient trail (thick in middle, thin at ends)
    /// - Non-linear rotation animation
    /// - Dimmed overlay outside the highlighted area
    /// @param x X position of the element.
    /// @param y Y position of the element.
    /// @param w Width of the element.
    /// @param h Height of the element.
    /// @param animationPhase Animation phase (0.0 - 1.0, cycles continuously).
    /// @param glowColorR Glow color red component (0-1).
    /// @param glowColorG Glow color green component (0-1).
    /// @param glowColorB Glow color blue component (0-1).
    /// @param strokeWidth Width of the glowing stroke.
    /// @param trailLength Length of the trailing glow (0.0 - 1.0 of perimeter).
    /// @param dimOpacity Opacity of the dimmed area outside (0-1).
    /// @param screenWidth Total screen/window width for dimming.
    /// @param screenHeight Total screen/window height for dimming.
    virtual void DrawGlowingBorderHighlight(
        float x, float y, float w, float h,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float trailLength,
        float dimOpacity,
        float screenWidth, float screenHeight) = 0;

    /// Draws a glowing border transition effect between two elements.
    virtual void DrawGlowingBorderTransition(
        float fromX, float fromY, float fromW, float fromH,
        float toX, float toY, float toW, float toH,
        float headProgress, float tailProgress,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float trailLength,
        float dimOpacity,
        float screenWidth, float screenHeight) = 0;

    /// Draws a ripple effect expanding from element border.
    /// Used after transition animation completes, before rotation starts.
    /// @param x X position of the element.
    /// @param y Y position of the element.
    /// @param w Width of the element.
    /// @param h Height of the element.
    /// @param rippleProgress Ripple expansion progress (0.0 - 1.0).
    /// @param glowColorR Glow color red component (0-1).
    /// @param glowColorG Glow color green component (0-1).
    /// @param glowColorB Glow color blue component (0-1).
    /// @param strokeWidth Base stroke width.
    /// @param dimOpacity Opacity of the dimmed area outside (0-1).
    /// @param screenWidth Total screen/window width for dimming.
    /// @param screenHeight Total screen/window height for dimming.
    virtual void DrawRippleEffect(
        float x, float y, float w, float h,
        float rippleProgress,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float dimOpacity,
        float screenWidth, float screenHeight) = 0;

    /// Captures the desktop area at the specified screen coordinates.
    /// Uses BitBlt from the screen DC to capture what's visible at that position.
    /// The captured content is cached internally and used by DrawDesktopBackdrop.
    /// @param screenX Screen X coordinate.
    /// @param screenY Screen Y coordinate.
    /// @param width Width to capture.
    /// @param height Height to capture.
    virtual void CaptureDesktopArea(int32_t screenX, int32_t screenY, int32_t width, int32_t height) {}

    /// Draws the cached desktop capture with Gaussian blur and tint overlay.
    /// Must call CaptureDesktopArea first to populate the cached capture.
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
    virtual void DrawDesktopBackdrop(
        float x, float y, float w, float h,
        float blurRadius,
        float tintR, float tintG, float tintB, float tintOpacity,
        float noiseIntensity, float saturation) {}

    /// Begins capturing content into an offscreen bitmap for transition shader effects.
    /// @param slot 0 = old content, 1 = new content.
    /// @param x X position of the transition area (in DIPs).
    /// @param y Y position of the transition area (in DIPs).
    /// @param w Width of the transition area (in DIPs).
    /// @param h Height of the transition area (in DIPs).
    virtual void BeginTransitionCapture(int slot, float x, float y, float w, float h) {}

    /// Ends capturing content for a transition slot and restores the main render target.
    /// @param slot 0 = old content, 1 = new content.
    virtual void EndTransitionCapture(int slot) {}

    /// Draws the transition shader effect blending old and new content bitmaps.
    /// @param x X position of the transition area (in DIPs).
    /// @param y Y position of the transition area (in DIPs).
    /// @param w Width of the transition area (in DIPs).
    /// @param h Height of the transition area (in DIPs).
    /// @param progress Transition progress (0.0 - 1.0).
    /// @param mode Shader mode index (0-9).
    virtual void DrawTransitionShader(float x, float y, float w, float h, float progress, int mode) {}

    /// Draws a previously captured transition bitmap to the current render target.
    /// @param slot Transition slot to draw from (0 or 1).
    /// @param x Destination X position (in DIPs).
    /// @param y Destination Y position (in DIPs).
    /// @param w Destination width (in DIPs).
    /// @param h Destination height (in DIPs).
    /// @param opacity Opacity to apply (0.0 - 1.0).
    virtual void DrawCapturedTransition(int slot, float x, float y, float w, float h, float opacity) {}

    // ========================================================================
    // Element Effect Capture & Rendering
    // ========================================================================

    /// Begins capturing element content into an offscreen bitmap for effect processing.
    /// @param x X position of the capture area (in DIPs).
    /// @param y Y position of the capture area (in DIPs).
    /// @param w Width of the capture area (in DIPs).
    /// @param h Height of the capture area (in DIPs).
    virtual void BeginEffectCapture(float x, float y, float w, float h) {}

    /// Ends capturing element content and restores the main render target.
    virtual void EndEffectCapture() {}

    /// Applies a Gaussian blur effect to the captured element content and draws it.
    /// @param x X position to draw at (in DIPs).
    /// @param y Y position to draw at (in DIPs).
    /// @param w Width of the draw area (in DIPs).
    /// @param h Height of the draw area (in DIPs).
    /// @param radius Blur radius (in DIPs).
    virtual void DrawBlurEffect(float x, float y, float w, float h, float radius,
        float uvOffsetX = 0, float uvOffsetY = 0) {}

    /// Applies a drop shadow effect to the captured element content and draws it.
    /// Draws the shadow first (offset + blurred alpha), then the original content on top.
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
    virtual void DrawDropShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float uvOffsetX = 0, float uvOffsetY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0) {}

    /// Applies an outer glow effect around the element.
    /// @param glowSize Size of the glow spread.
    /// @param r,g,b,a Glow color (premultiplied alpha).
    /// @param intensity Glow brightness multiplier.
    virtual void DrawOuterGlowEffect(float x, float y, float w, float h,
        float glowSize, float r, float g, float b, float a, float intensity,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL) {}

    /// Applies an inner shadow effect inside the element bounds.
    virtual void DrawInnerShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL) {}

    /// Applies a 5x4 color matrix transformation to the element content.
    /// @param matrix 20 floats in row-major order (5x4 matrix).
    virtual void DrawColorMatrixEffect(float x, float y, float w, float h,
        const float* matrix) {}

    /// Applies an emboss effect to the element content.
    /// @param amount Emboss strength.
    /// @param lightDirX,lightDirY Light direction.
    /// @param relief Depth of the emboss.
    virtual void DrawEmbossEffect(float x, float y, float w, float h,
        float amount, float lightDirX, float lightDirY, float relief) {}

    /// Applies a custom pixel shader effect to the captured element content.
    /// The captured content is exposed to the shader as t0/s0 and the constants
    /// buffer is bound to b0.
    virtual void DrawShaderEffect(float x, float y, float w, float h,
        const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
        const float* constants, uint32_t constantFloatCount) {}

    /// Draws a liquid glass effect with SDF-based refraction, highlight, and inner shadow.
    /// Captures current render target content, applies blur, then renders the full
    /// liquid glass pipeline in a single custom D2D1 effect pass.
    virtual void DrawLiquidGlass(
        float x, float y, float w, float h,
        float cornerRadius,
        float blurRadius,
        float refractionAmount,
        float chromaticAberration,
        float tintR, float tintG, float tintB, float tintOpacity,
        float lightX, float lightY,
        float highlightBoost = 0.0f,
        int shapeType = 0,
        float shapeExponent = 4.0f,
        int neighborCount = 0,
        float fusionRadius = 30.0f,
        const float* neighborData = nullptr) {}

    // ========================================================================
    // Rendering Engine Selection (Hot-Switch)
    // ========================================================================

    /// Gets the active rendering engine for this render target.
    virtual JaliumRenderingEngine GetRenderingEngine() const { return activeEngine_; }

    /// Sets the rendering engine (hot-switch).  Takes effect at the next BeginDraw().
    /// Returns JALIUM_OK on success, JALIUM_ERROR_NOT_SUPPORTED if the engine is
    /// not available for this backend.
    virtual JaliumResult SetRenderingEngine(JaliumRenderingEngine engine) {
        pendingEngine_ = engine;
        // Subclasses override to resolve Auto and apply immediately when not drawing.
        // Base implementation always applies immediately (no isDrawing_ tracking here).
        activeEngine_ = engine;
        return JALIUM_OK;
    }

protected:
    int32_t width_ = 0;
    int32_t height_ = 0;
    bool vsyncEnabled_ = true;
    JaliumRenderingEngine activeEngine_ = JALIUM_ENGINE_AUTO;
    JaliumRenderingEngine pendingEngine_ = JALIUM_ENGINE_AUTO;
};

/// Abstract base class for brushes.
class Brush {
public:
    virtual ~Brush() = default;
    virtual JaliumBrushType GetType() const = 0;
};

/// Abstract base class for text formats.
class TextFormat {
public:
    virtual ~TextFormat() = default;
    virtual void SetAlignment(int32_t alignment) = 0;
    virtual void SetParagraphAlignment(int32_t alignment) = 0;
    virtual void SetTrimming(int32_t trimming) = 0;
    virtual void SetWordWrapping(int32_t wrapping) = 0;
    virtual void SetLineSpacing(int32_t method, float spacing, float baseline) = 0;
    virtual void SetMaxLines(uint32_t maxLines) = 0;

    virtual JaliumResult MeasureText(
        const wchar_t* text,
        uint32_t textLength,
        float maxWidth,
        float maxHeight,
        JaliumTextMetrics* metrics) = 0;

    virtual JaliumResult GetFontMetrics(JaliumTextMetrics* metrics) = 0;

    /// Hit-tests a point against the text layout to find the character position.
    virtual JaliumResult HitTestPoint(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        float pointX, float pointY,
        JaliumTextHitTestResult* result) = 0;

    /// Gets the caret position and bounding rect for a given text position.
    virtual JaliumResult HitTestTextPosition(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        uint32_t textPosition, int32_t isTrailingHit,
        JaliumTextHitTestResult* result) = 0;
};

/// Abstract base class for bitmaps.
class Bitmap {
public:
    virtual ~Bitmap() = default;
    virtual uint32_t GetWidth() const = 0;
    virtual uint32_t GetHeight() const = 0;
};

} // namespace jalium
