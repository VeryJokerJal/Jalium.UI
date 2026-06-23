#pragma once

#include "jalium_types.h"
#include "jalium_api.h"  // JALIUM_API export/import decorator for TextFormat helpers
#include "jalium_video_surface.h"  // JaliumVideoSurfaceKind / Descriptor / DirtyRect
#include <algorithm>
#include <memory>
#include <string>

namespace jalium {

// Forward declarations
class RenderTarget;
class Brush;
class TextFormat;
class Bitmap;
class VideoSurface;

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

    /// Fills <paramref name="out"/> with the currently selected GPU adapter's
    /// description (name, type, VRAM, vendor / device IDs). Used by host apps
    /// to show "current GPU" in status bars / Help/About dialogs and to
    /// diagnose hybrid-graphics misconfiguration (e.g. independent GPU
    /// disabled in Device Manager → DXGI falls back to WARP / Microsoft Basic
    /// Render Driver, which causes 30fps + input lag).
    ///
    /// Default implementation returns JALIUM_ERROR_NOT_SUPPORTED so legacy
    /// backends keep building; D3D12 overrides this with the actual
    /// `DXGI_ADAPTER_DESC1` captured at device creation.
    virtual JaliumResult GetAdapterInfo(JaliumAdapterInfo* out) const
    {
        if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
        *out = JaliumAdapterInfo{};
        return JALIUM_ERROR_NOT_SUPPORTED;
    }

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

    // ────────────────────────────────────────────────────────────────────
    //  Video surface (frame-rate updated BGRA8 / hardware-decoded GPU
    //  texture). See jalium_video_surface.h for the public ABI. Backends
    //  that haven't implemented video streaming yet inherit the default
    //  no-ops — the C-ABI dispatch in video_surface.cpp returns nullptr
    //  so the managed caller falls back to the legacy WriteableBitmap
    //  path.

    virtual VideoSurface* CreateVideoSurface(uint32_t width, uint32_t height,
                                             uint32_t formatHint)
    {
        (void)width; (void)height; (void)formatHint;
        return nullptr;
    }

    virtual VideoSurface* WrapExternalVideoSurface(
        const JaliumVideoSurfaceDescriptor* descriptor)
    {
        (void)descriptor;
        return nullptr;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Ink-layer / brush-shader pipeline
    //
    //  Backend-agnostic interface for the InkCanvas's GPU-resident
    //  committed-ink layer and the pixel-shader brush pipeline that
    //  paints into it. D3D12 implements these today; other backends
    //  inherit the default no-ops (managed side detects the nullptr /
    //  negative return and falls back to the legacy CPU raster path).
    //
    //  All pointers are opaque handles (void*) at this layer — the core
    //  translation unit doesn't know the concrete D3D12 types.

    virtual void* CreateInkLayerBitmap(uint32_t width, uint32_t height)
    {
        (void)width; (void)height;
        return nullptr;
    }

    virtual void DestroyInkLayerBitmap(void* bitmap) { (void)bitmap; }

    virtual int32_t ResizeInkLayerBitmap(void* bitmap, uint32_t width, uint32_t height)
    {
        (void)bitmap; (void)width; (void)height;
        return -1;
    }

    virtual void ClearInkLayerBitmap(void* bitmap, float r, float g, float b, float a)
    {
        (void)bitmap; (void)r; (void)g; (void)b; (void)a;
    }

    /// Compile an HLSL brush pixel shader + create its PSO.
    /// blendMode: 0=SourceOver, 1=Additive, 2=Erase.
    virtual void* CreateBrushShader(const char* shaderKey, const char* brushMainHlsl, int32_t blendMode)
    {
        (void)shaderKey; (void)brushMainHlsl; (void)blendMode;
        return nullptr;
    }

    virtual void DestroyBrushShader(void* shader) { (void)shader; }

    /// Dispatch a brush shader over an ink-layer bitmap. strokePoints
    /// is `pointCount × 16 bytes` of StrokePoint{x,y,pressure,pad}.
    /// constants is the 80-byte managed BrushConstantsNative struct
    /// (the backend appends ViewportSize + pad to reach the 96-byte
    /// cbuffer the shader expects). extraParams / extraParamsSize is
    /// an optional user-defined cbuffer (b1) that custom brush shaders
    /// can read — pass nullptr / 0 when the shader only uses b0.
    virtual int32_t DispatchBrush(void* bitmap, void* shader,
                                   const void* strokePoints, uint32_t pointCount,
                                   const void* constants,
                                   const void* extraParams, uint32_t extraParamsSize)
    {
        (void)bitmap; (void)shader; (void)strokePoints; (void)pointCount;
        (void)constants; (void)extraParams; (void)extraParamsSize;
        return -1;
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

    /// Off-thread animation probe (Increment 1 hard gate). Creates a child
    /// composition visual with solid-color content and an autonomous offset
    /// animation that DWM drives at vblank with no app-side present. Only the
    /// composition (DComp) D3D12 backend implements this; everything else falls
    /// back here and the managed layer keeps the per-frame present path.
    virtual JaliumResult CreateAnimProbe(
        int32_t x, int32_t y, int32_t width, int32_t height,
        float travelPx, float periodSec, uint32_t colorArgb, int32_t vertical,
        void** visualOut)
    {
        (void)x; (void)y; (void)width; (void)height;
        (void)travelPx; (void)periodSec; (void)colorArgb; (void)vertical;
        if (visualOut) {
            *visualOut = nullptr;
        }
        return JALIUM_ERROR_NOT_SUPPORTED;
    }

    /// Destroys a probe visual previously created by CreateAnimProbe.
    virtual JaliumResult DestroyAnimProbe(void* visual)
    {
        (void)visual;
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
    /// @param edgeMode Anti-aliasing mode (-1 = inherit/backend default,
    ///                 1 = Aliased binary edges, 2 = Antialiased analytic coverage).
    virtual void FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule, int32_t edgeMode = -1) = 0;

    /// Strokes a path defined by a command buffer (lines + bezier curves).
    /// lineCap: 0 = Butt, 1 = Square, 2 = Round.
    /// @param edgeMode Anti-aliasing mode (-1 = inherit/backend default,
    ///                 1 = Aliased binary edges, 2 = Antialiased analytic coverage).
    virtual void StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin = 0, float miterLimit = 10.0f, int32_t lineCap = 0,
        const float* dashPattern = nullptr, uint32_t dashCount = 0, float dashOffset = 0.0f, int32_t edgeMode = -1) = 0;


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

    /// Composites an InkCanvas ink-layer bitmap onto this render target.
    /// The bitmap argument is the opaque handle returned by
    /// jalium_ink_layer_bitmap_create (cast to void* for API neutrality —
    /// the backend casts it back to its own D3D12InkLayerBitmap type).
    /// Default: no-op (backends without ink-layer support can ignore).
    virtual void BlitInkLayer(void* inkLayerBitmap,
                              float dstX, float dstY, float opacity)
    {
        (void)inkLayerBitmap; (void)dstX; (void)dstY; (void)opacity;
    }

    /// Retained GPU layers (damage-driven composited-animation fast path).
    /// A subtree's CONTENT is rendered ONCE into a persistent offscreen texture,
    /// then composited as a transformed/opacity quad on subsequent frames so a
    /// composited (Opacity/RenderTransform) animation on a content-clean subtree
    /// does not re-emit the whole subtree every frame. Default: unsupported, so
    /// callers fall back to full re-emission with no behavior change (backends
    /// without offscreen-RT capture, e.g. Vulkan today).
    ///
    /// Whether this render target can realize/composite retained layers now.
    virtual bool SupportsRetainedLayers() const { return false; }

    /// Begins capturing subsequent draws into a persistent layer texture covering
    /// (x,y,w,h) in DIP screen space. Reuses/resizes existingLayer when non-null.
    /// Returns the layer handle (opaque), or null if a layer could not be realized
    /// (caller must fall back and must NOT call RealizeLayerEnd).
    virtual void* RealizeLayerBegin(void* existingLayer, float x, float y, float w, float h)
    {
        (void)existingLayer; (void)x; (void)y; (void)w; (void)h;
        return nullptr;
    }

    /// Closes the capture scope opened by RealizeLayerBegin and finalizes the layer
    /// texture for sampling (restores the previous render target / viewport / clip).
    virtual void RealizeLayerEnd(void* layer) { (void)layer; }

    /// Composites a realized layer as a single quad at (x,y,w,h) in the current
    /// (offset/transform) space with the given opacity, honoring the live clip.
    virtual void CompositeLayer(void* layer, float x, float y, float w, float h, float opacity)
    {
        (void)layer; (void)x; (void)y; (void)w; (void)h; (void)opacity;
    }

    /// Destroys a retained layer, fence-gating its GPU texture release. Safe to
    /// call at any time (the texture is retired, not freed, while in flight).
    virtual void DestroyRetainedLayer(void* layer) { (void)layer; }

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

    /// Pushes a rounded-rect clip with independent radii for each corner.
    /// Default implementation collapses to the maximum radius and forwards to
    /// <see cref="PushRoundedRectClip"/>; backends that natively support
    /// asymmetric corner radii should override.
    virtual void PushPerCornerRoundedRectClip(float x, float y, float w, float h,
        float tl, float tr, float br, float bl)
    {
        float maxR = std::max(std::max(tl, tr), std::max(br, bl));
        PushRoundedRectClip(x, y, w, h, maxR, maxR);
    }

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

    /// Hands ownership of present pacing to the caller ("external pacing").
    /// When enabled, BeginDraw must not block on the swap-chain frame-latency
    /// waitable (the caller consumes its signals — e.g. via a thread-pool wait
    /// callback — and only schedules a frame once a present credit is
    /// available), and Present must not block on vsync (sync interval 0); the
    /// waitable alone provides the frame cadence. Backends without a
    /// frame-latency waitable ignore this and keep their internal pacing.
    virtual void SetExternalPresentPacing(bool /*enabled*/) {}

    /// Sets the path stencil-then-cover MSAA sample count (1/2/4/8). Applied at
    /// the next frame boundary. Backends without an MSAA path renderer ignore it.
    /// Lets callers trade path edge anti-aliasing quality for GPU time.
    virtual void SetPathMsaaSampleCount(uint32_t /*sampleCount*/) {}

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

    /// Draws a bitmap with an explicit scaling-mode hint. The default
    /// implementation falls back to the legacy <c>DrawBitmap</c> so backends
    /// that don't (yet) honour scaling mode keep working unchanged. Backends
    /// that select a sampler / mipmap chain based on this hint must override.
    /// scalingMode values match the JaliumBitmapScalingMode enum.
    virtual void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity, int /*scalingMode*/)
    {
        DrawBitmap(bitmap, x, y, w, h, opacity);
    }

    /// Draws a video surface. Backends that don't implement video surfaces
    /// yet keep the default no-op; the managed caller will have already
    /// fallen back to a WriteableBitmap via DrawBitmap before reaching here.
    virtual void DrawVideoSurface(VideoSurface* /*surface*/,
                                  float /*x*/, float /*y*/, float /*w*/, float /*h*/,
                                  float /*opacity*/, int /*scalingMode*/) {}

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
        float uvOffsetX, float uvOffsetY,
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

    /// Applies a custom pixel shader supplied as SM6 HLSL *source* (compiled at
    /// runtime by the backend) instead of precompiled DXBC bytecode. This is the
    /// cross-backend custom-shader path: D3D12 compiles the source with
    /// D3DCompile, Vulkan with DXC→SPIR-V, so one authored shader works on both.
    /// The captured content is exposed as Texture2D@t0 + SamplerState@s0 and the
    /// constants buffer is bound to cbuffer@b0; the framework vertex shader
    /// supplies a fullscreen quad with float2 uv : TEXCOORD0.
    /// Default: draw the captured content unmodified (matches the DXBC path's
    /// compile-failure fallback) so backends that don't implement it degrade
    /// exactly like a no-op shader.
    virtual void DrawShaderEffectFromSource(float x, float y, float w, float h,
        const char* hlslSource, const float* constants, uint32_t constantFloatCount)
    {
        (void)x; (void)y; (void)w; (void)h;
        (void)hlslSource; (void)constants; (void)constantFloatCount;
    }

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
    // GPU Resource Diagnostics
    // ========================================================================

    /// Fills <paramref name="out"/> with the backend's current GPU resource usage.
    /// Called once per frame by DevTools' Perf tab; must be safe to call from the
    /// UI / render thread and cheap enough to not disturb frame pacing.
    /// Default implementation zero-fills and returns JALIUM_ERROR_NOT_SUPPORTED so
    /// the managed side can distinguish "backend not wired up" from "zero usage".
    virtual JaliumResult QueryGpuStats(JaliumGpuStats* out) const
    {
        if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
        *out = JaliumGpuStats{};
        return JALIUM_ERROR_NOT_SUPPORTED;
    }

    /// Reads the previous frame's GPU-side timing breakdown. The data
    /// is populated asynchronously: the backend issues timestamp queries
    /// during the frame, resolves them at EndFrame, and the readback
    /// becomes valid once the frame's fence is observed complete by the
    /// next BeginFrame. Default implementation returns NOT_SUPPORTED;
    /// D3D12 overrides this with real measurements.
    virtual JaliumResult QueryGpuTiming(JaliumGpuTimingStats* out) const
    {
        if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
        *out = JaliumGpuTimingStats{};
        return JALIUM_ERROR_NOT_SUPPORTED;
    }

    /// Returns the OS HANDLE (cast to intptr_t for portability) that the
    /// backend uses as its frame-latency waitable, or 0 when no such
    /// object exists (older platforms, non-D3D12 backends). Callers use
    /// this for vsync-aligned scheduling — block on the handle in a
    /// background thread, marshal a "ready" signal to the UI thread so
    /// the next RenderFrame happens right after the swap chain releases
    /// a back buffer. The HANDLE remains owned by the render target;
    /// callers must NOT CloseHandle it.
    virtual intptr_t GetFrameLatencyWaitable() const { return 0; }

    /// Fills <paramref name="out"/> with the swap chain / present configuration
    /// currently in use. Lets host apps display "FLIP / tearing on / 1 frame
    /// latency" in status bars and confirm the low-latency path is actually
    /// enabled (driver / OS can silently strip flags during swap chain creation).
    /// Default implementation returns NOT_SUPPORTED so non-D3D12 backends
    /// keep building.
    virtual JaliumResult GetPresentInfo(JaliumPresentInfo* out) const
    {
        if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
        *out = JaliumPresentInfo{};
        return JALIUM_ERROR_NOT_SUPPORTED;
    }

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

    // ========================================================================
    // Idle-Resource Reclamation
    // ========================================================================

    /// Drops any reusable GPU / CPU caches the backend has accumulated during
    /// rendering. Invoked by the managed-side idle reclaimer
    /// (`app.UseIdleResourceReclamation()`) on a periodic tick — typically once
    /// per second — so backends can shrink their path-geometry, text-bitmap,
    /// glyph-atlas, and gradient caches when the application has been quiet.
    ///
    /// Contract:
    /// * MUST be safe to call from the UI / render thread between frames. Implementations
    ///   that touch GPU resources still in use by an in-flight frame are responsible for
    ///   deferring the destroy through the backend's own frame-fence machinery
    ///   (e.g. Vulkan's PerFrameState ring, D3D12's BeginFrame boundary).
    /// * MUST be idempotent — repeated calls with no fresh activity in between should
    ///   be cheap no-ops, not progressively destroy more.
    /// * MUST NOT throw or invalidate any JaliumNativeBitmap / JaliumGeometry handles
    ///   that managed code still holds; only internal lookup caches go.
    ///
    /// Default implementation returns JALIUM_OK with no work done so backends that
    /// have nothing to reclaim do not need to override.
    virtual JaliumResult ReclaimIdleResources() { return JALIUM_OK; }

    // ========================================================================
    // TEST-ONLY device-removal injection (GPU-switch hardening verification)
    // ========================================================================
    // Appended at the END of the vtable on purpose: inserting virtuals in the
    // middle shifts every later slot, so a backend DLL built against an older
    // header would mis-dispatch across the C-ABI boundary. Keep new test hooks
    // here, and rebuild ALL backends (d3d12/vulkan/software/metal) when adding
    // any RenderTarget virtual.

    /// Forcibly removes this target's GPU device using the backend's official
    /// debug trigger (D3D12: ID3D12Device5::RemoveDevice), so the device-lost
    /// hardening (mid-frame DEVICE_REMOVED latch, retained-layer cross-
    /// generation guards, managed recovery chain) can be exercised by an
    /// automated harness instead of a physical GPU switch. The C ABI gates this
    /// behind the JALIUM_DEBUG_DEVICE_REMOVE environment variable; backends
    /// without a debug removal mechanism return false.
    virtual bool DebugRemoveDevice() { return false; }

    /// Snapshots the process-wide retained-layer destroy counters — how many
    /// layers were orphaned (creating device removed; fence graveyard bypassed)
    /// vs. retired through a fence-gated graveyard. The branch choice is
    /// otherwise unobservable from managed code. Returns false when the backend
    /// does not track these counters.
    virtual bool DebugGetRetainedDestroyCounts(uint64_t* orphaned, uint64_t* graveyard)
    {
        (void)orphaned; (void)graveyard;
        return false;
    }

    /// The GPU device pointer backing this target, so a multi-window harness can
    /// assert two windows really landed on DIFFERENT devices after a staggered
    /// recovery (the precondition for the cross-device orphan-vs-graveyard
    /// discrimination to mean anything). 0 when unavailable.
    virtual uint64_t DebugDevicePointer() { return 0; }

    /// True while an offscreen effect capture is open, so the capture harness can
    /// confirm the device was removed INSIDE a live capture scope (the behavior
    /// it claims to verify) rather than merely while an effect was on screen.
    virtual bool DebugInOffscreenCapture() { return false; }

    /// TEST-ONLY (#921 regression self-check). Stages the "same-thread leaked-open
    /// command list" race and then resizes, so the same-thread branch of the
    /// backend's resize guard — which must Close the leaked command list BEFORE
    /// the back buffers it still references are freed — is exercised on a real
    /// device. The backend opens its command list exactly as a normal frame does
    /// but WITHOUT marking the target as drawing (the BeginFrame open-gap: the
    /// command list is recording while the draw flag still reads false), then
    /// performs a same-thread resize to (width,height). Writes 1 to *outListClosed
    /// when the command list was Closed afterwards (the #921 fix held) and 0 when
    /// it was left open (the OBJECT_DELETED_WHILE_STILL_IN_USE regression).
    /// Returns the resize JaliumResult code (0 == JALIUM_OK) on success, or a
    /// negative value when the scenario could not be staged. Backends without this
    /// debug capability return -1.
    virtual int32_t DebugForceLeakedCommandListResize(int32_t width, int32_t height, int32_t* outListClosed)
    {
        (void)width; (void)height;
        if (outListClosed) *outListClosed = 0;
        return -1;
    }

    /// TEST-ONLY (#921 Vello-output regression self-check). Drives the D3D12 Vello
    /// 'JaliumVelloOutput' orphan sequence in-process: dispatches a Vello path into
    /// an output texture, composites it into the bitmap keep-alive list, calls
    /// ForceNewOutputTexture() to drop Vello's own reference, then runs the mid-frame
    /// FlushGraphicsForCompute() that clears that keep-alive — all while the command
    /// list is open. Writes 1 to *outAlive when the texture survived (the fix parked
    /// it on the fence-gated retire list) and 0 when it was freed while still
    /// referenced (the OBJECT_DELETED_WHILE_STILL_IN_USE regression). Returns 0
    /// (JALIUM_OK) when staged, or a negative value when it could not be staged.
    /// Backends without this debug capability return -1.
    virtual int32_t DebugForceVelloOutputOrphan(int32_t* outAlive)
    {
        if (outAlive) *outAlive = 0;
        return -1;
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

    // ------------------------------------------------------------------
    // Per-format text rendering options (WPF System.Windows.Media.TextOptions
    // attached properties, plumbed per-call instead of process-wide so each
    // text element can independently choose ClearType/Grayscale, Ideal/Display
    // glyph metrics, and the hinting mode). Defaults mirror managed
    // Jalium.UI.Media.TextOptions defaults — Auto / Ideal / Auto.
    //
    // The backend reads these on every DrawText call (or when it builds the
    // glyph atlas / DWrite layout). Auto means "fall back to the process-wide
    // jalium_text_get_global_antialias_mode value, then to the platform
    // default" — backends call ResolveTextRenderingMode() to do that.
    //
    // Setters are non-virtual and live entirely on the base class because the
    // values are pure data the backend reads back; no derived behaviour needed.
    // ------------------------------------------------------------------

    /// Sets the per-format text rendering (anti-alias) mode.
    /// 0=Auto, 1=Aliased, 2=Grayscale, 3=ClearType. Auto delegates to
    /// the process-wide value.
    void SetTextRenderingMode(int32_t mode) noexcept { text_rendering_mode_ = mode; }

    /// Sets the per-format text formatting mode.
    /// 0=Ideal (resolution-independent glyph metrics, WPF default),
    /// 1=Display (pixel-snapped metrics — sharp at small sizes, less
    /// uniform when DPI-scaled).
    void SetTextFormattingMode(int32_t mode) noexcept { text_formatting_mode_ = mode; }

    /// Sets the per-format text hinting mode.
    /// 0=Auto, 1=Fixed (full hinting — sharper static text),
    /// 2=Animated (hinting suppressed — smoother subpixel animation).
    void SetTextHintingMode(int32_t mode) noexcept { text_hinting_mode_ = mode; }

    int32_t GetTextRenderingMode() const noexcept { return text_rendering_mode_; }
    int32_t GetTextFormattingMode() const noexcept { return text_formatting_mode_; }
    int32_t GetTextHintingMode() const noexcept { return text_hinting_mode_; }

    /// Resolves the per-format TextRenderingMode against the process-wide
    /// fallback chain and returns a concrete JALIUM_TEXT_AA_* value (never
    /// Auto). Backends call this in their glyph-rasterization path so a
    /// single helper owns the policy:
    ///   - per-format mode wins when explicit (Aliased / Grayscale / ClearType)
    ///   - Auto → fall back to process-wide jalium_text_set_global_antialias_mode
    ///   - process-wide Auto → Grayscale on every platform (was ClearType on
    ///     Windows before 2026-05-24; see jalium::text_options::ResolveMode
    ///     in jalium_text_options.cpp for the rationale)
    /// JALIUM_API-decorated so backend DLLs (jalium.native.d3d12.dll,
    /// jalium.native.vulkan.dll, …) can call it across the core DLL boundary.
    JALIUM_API int32_t ResolveEffectiveTextRenderingMode() const noexcept;

protected:
    // 0 (Auto) / 0 (Ideal) / 0 (Auto) match the managed-side defaults so an
    // un-set format renders exactly like the process-wide default. Backends
    // can also read the fields directly for cache keys without going through
    // the public getters.
    int32_t text_rendering_mode_  = 0;  // JALIUM_TEXT_AA_AUTO
    int32_t text_formatting_mode_ = 0;  // Ideal
    int32_t text_hinting_mode_    = 0;  // Auto
};

/// Abstract base class for bitmaps.
class Bitmap {
public:
    virtual ~Bitmap() = default;
    virtual uint32_t GetWidth() const = 0;
    virtual uint32_t GetHeight() const = 0;

    /// Hot-update packed BGRA8 pixels in place. Default implementation refuses;
    /// D3D12 / Vulkan override to skip per-frame texture / VkImage recreation.
    /// Returns true on success. Stride is the source row pitch in bytes.
    virtual bool UpdatePackedPixels(const uint8_t* /*pixels*/, uint32_t /*width*/,
                                    uint32_t /*height*/, uint32_t /*stride*/) {
        return false;
    }
};

/// Abstract base class for video surfaces. See jalium_video_surface.h for
/// the public-facing C ABI and design notes; this is the per-backend
/// contract.
///
/// Lifecycle is single-threaded (owned by one decoder pump thread); concrete
/// backends decide whether their staging upload happens on the same thread
/// (Software) or gets shipped off to a dedicated copy queue (D3D12 /
/// Vulkan).
class VideoSurface {
public:
    virtual ~VideoSurface() = default;
    virtual uint32_t GetWidth()  const = 0;
    virtual uint32_t GetHeight() const = 0;
    virtual JaliumVideoSurfaceKind GetKind() const = 0;

    /// Maps the staging buffer for CPU write. Only the BGRA8 CPU path needs
    /// to support this; external-wrap surfaces return false.
    virtual bool Lock(uint8_t** outPtr, uint32_t* outStride) = 0;

    /// Signals the staging-buffer write is complete; backends issue the
    /// copy-to-device-texture step (D3D12 / Vulkan) or just bump a content
    /// revision (Software). dirty may be nullptr to invalidate everything.
    virtual bool Unlock(const JaliumVideoSurfaceDirtyRect* dirty) = 0;
};

} // namespace jalium
