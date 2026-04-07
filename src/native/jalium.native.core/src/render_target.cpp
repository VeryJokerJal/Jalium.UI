#include "jalium_internal.h"
#include "jalium_string_util.h"
#ifdef _WIN32
#include <windows.h>
#endif

// ============================================================================
// C API Implementation
// ============================================================================

namespace {

bool IsValidSurfaceDescriptor(const JaliumSurfaceDescriptor* surface)
{
    return surface != nullptr && surface->handle0 != 0;
}

JaliumRenderTarget* CreateRenderTargetCore(
    JaliumContext* ctx,
    const JaliumSurfaceDescriptor* surface,
    int32_t width,
    int32_t height,
    bool useComposition)
{
    if (!ctx) {
        return nullptr;
    }

    auto* context = reinterpret_cast<jalium::Context*>(ctx);

    if (!IsValidSurfaceDescriptor(surface) || width <= 0 || height <= 0) {
        context->SetLastError(
            JALIUM_ERROR_INVALID_ARGUMENT,
            useComposition
                ? L"Invalid composition render target surface creation arguments."
                : L"Invalid render target surface creation arguments.");
        return nullptr;
    }

    auto backend = jalium::GetBackendFromContext(ctx);
    if (!backend) {
        context->SetLastError(JALIUM_ERROR_INVALID_STATE, L"Render backend is unavailable for this context.");
        return nullptr;
    }

    auto rt = useComposition
        ? backend->CreateRenderTargetForCompositionSurface(surface, width, height)
        : backend->CreateRenderTargetForSurface(surface, width, height);
    if (!rt) {
        context->SetLastError(
            JALIUM_ERROR_RESOURCE_CREATION_FAILED,
            useComposition
                ? L"Backend failed to create composition render target from surface descriptor."
                : L"Backend failed to create render target from surface descriptor.");
        return nullptr;
    }

    context->SetLastError(JALIUM_OK);
    return reinterpret_cast<JaliumRenderTarget*>(rt);
}

} // namespace

extern "C" {

JALIUM_API JaliumRenderTarget* jalium_render_target_create_for_hwnd(
    JaliumContext* ctx,
    void* hwnd,
    int32_t width,
    int32_t height)
{
    JaliumSurfaceDescriptor surface{};
    surface.platform = JALIUM_PLATFORM_WINDOWS;
    surface.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
    surface.handle0 = reinterpret_cast<intptr_t>(hwnd);
    return CreateRenderTargetCore(ctx, &surface, width, height, false);
}

JALIUM_API JaliumRenderTarget* jalium_render_target_create_for_composition(
    JaliumContext* ctx,
    void* hwnd,
    int32_t width,
    int32_t height)
{
    JaliumSurfaceDescriptor surface{};
    surface.platform = JALIUM_PLATFORM_WINDOWS;
    surface.kind = JALIUM_SURFACE_KIND_COMPOSITION_TARGET;
    surface.handle0 = reinterpret_cast<intptr_t>(hwnd);
    return CreateRenderTargetCore(ctx, &surface, width, height, true);
}

JALIUM_API JaliumRenderTarget* jalium_render_target_create_for_surface(
    JaliumContext* ctx,
    const JaliumSurfaceDescriptor* surface,
    int32_t width,
    int32_t height)
{
    return CreateRenderTargetCore(ctx, surface, width, height, false);
}

JALIUM_API JaliumRenderTarget* jalium_render_target_create_for_composition_surface(
    JaliumContext* ctx,
    const JaliumSurfaceDescriptor* surface,
    int32_t width,
    int32_t height)
{
    return CreateRenderTargetCore(ctx, surface, width, height, true);
}

JALIUM_API void jalium_render_target_destroy(JaliumRenderTarget* rt) {
    if (rt) {
        delete reinterpret_cast<jalium::RenderTarget*>(rt);
    }
}

JALIUM_API JaliumResult jalium_render_target_resize(JaliumRenderTarget* rt, int32_t width, int32_t height) {
    if (!rt || width <= 0 || height <= 0) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    return reinterpret_cast<jalium::RenderTarget*>(rt)->Resize(width, height);
}

JALIUM_API JaliumResult jalium_render_target_begin_draw(JaliumRenderTarget* rt) {
    if (!rt) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->BeginDraw();
}

JALIUM_API JaliumResult jalium_render_target_end_draw(JaliumRenderTarget* rt) {
    if (!rt) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->EndDraw();
}

JALIUM_API void jalium_render_target_clear(JaliumRenderTarget* rt, float r, float g, float b, float a) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->Clear(r, g, b, a);
    }
}

JALIUM_API void jalium_render_target_set_vsync(JaliumRenderTarget* rt, int32_t enabled) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->SetVSyncEnabled(enabled != 0);
    }
}

JALIUM_API void jalium_render_target_set_dpi(JaliumRenderTarget* rt, float dpiX, float dpiY) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->SetDpi(dpiX, dpiY);
    }
}

JALIUM_API void jalium_render_target_add_dirty_rect(JaliumRenderTarget* rt, float x, float y, float width, float height) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->AddDirtyRect(x, y, width, height);
    }
}

JALIUM_API void jalium_render_target_set_full_invalidation(JaliumRenderTarget* rt) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->SetFullInvalidation();
    }
}

JALIUM_API int32_t jalium_render_target_supports_partial_presentation(JaliumRenderTarget* rt) {
    if (!rt) {
        return 0;
    }

    return reinterpret_cast<jalium::RenderTarget*>(rt)->SupportsPartialPresentation() ? 1 : 0;
}

JALIUM_API JaliumResult jalium_render_target_create_webview_visual(
    JaliumRenderTarget* rt,
    void** visual_out)
{
    if (!rt || !visual_out) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    *visual_out = nullptr;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->CreateWebViewVisual(visual_out);
}

JALIUM_API JaliumResult jalium_render_target_destroy_webview_visual(
    JaliumRenderTarget* rt,
    void* visual)
{
    if (!rt || !visual) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    return reinterpret_cast<jalium::RenderTarget*>(rt)->DestroyWebViewVisual(visual);
}

JALIUM_API JaliumResult jalium_render_target_set_webview_visual_placement(
    JaliumRenderTarget* rt,
    void* visual,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height,
    int32_t content_offset_x,
    int32_t content_offset_y)
{
    if (!rt || !visual || width < 0 || height < 0) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    return reinterpret_cast<jalium::RenderTarget*>(rt)->SetWebViewVisualPlacement(
        visual,
        x,
        y,
        width,
        height,
        content_offset_x,
        content_offset_y);
}

JALIUM_API void jalium_draw_fill_rectangle(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    JaliumBrush* brush)
{
    if (rt && brush) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->FillRectangle(
            x, y, width, height,
            reinterpret_cast<jalium::Brush*>(brush));
    }
}

JALIUM_API void jalium_draw_rectangle(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    JaliumBrush* brush,
    float strokeWidth)
{
    if (rt && brush) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawRectangle(
            x, y, width, height,
            reinterpret_cast<jalium::Brush*>(brush),
            strokeWidth);
    }
}

JALIUM_API void jalium_draw_fill_rounded_rectangle(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    float radiusX, float radiusY,
    JaliumBrush* brush)
{
    if (rt && brush) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->FillRoundedRectangle(
            x, y, width, height, radiusX, radiusY,
            reinterpret_cast<jalium::Brush*>(brush));
    }
}

JALIUM_API void jalium_draw_rounded_rectangle(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    float radiusX, float radiusY,
    JaliumBrush* brush,
    float strokeWidth)
{
    if (rt && brush) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawRoundedRectangle(
            x, y, width, height, radiusX, radiusY,
            reinterpret_cast<jalium::Brush*>(brush),
            strokeWidth);
    }
}

JALIUM_API void jalium_fill_per_corner_rounded_rectangle(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    float tl, float tr, float br, float bl,
    JaliumBrush* brush)
{
    if (rt && brush) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->FillPerCornerRoundedRectangle(
            x, y, width, height, tl, tr, br, bl,
            reinterpret_cast<jalium::Brush*>(brush));
    }
}

JALIUM_API void jalium_draw_per_corner_rounded_rectangle(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    float tl, float tr, float br, float bl,
    JaliumBrush* brush,
    float strokeWidth)
{
    if (rt && brush) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawPerCornerRoundedRectangle(
            x, y, width, height, tl, tr, br, bl,
            reinterpret_cast<jalium::Brush*>(brush),
            strokeWidth);
    }
}

JALIUM_API void jalium_draw_fill_ellipse(
    JaliumRenderTarget* rt,
    float centerX, float centerY, float radiusX, float radiusY,
    JaliumBrush* brush)
{
    if (rt && brush) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->FillEllipse(
            centerX, centerY, radiusX, radiusY,
            reinterpret_cast<jalium::Brush*>(brush));
    }
}

JALIUM_API void jalium_fill_ellipse_batch(
    JaliumRenderTarget* rt,
    const float* data,
    uint32_t count)
{
    if (rt && data && count > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->FillEllipseBatch(data, count);
    }
}

JALIUM_API void jalium_draw_ellipse(
    JaliumRenderTarget* rt,
    float centerX, float centerY, float radiusX, float radiusY,
    JaliumBrush* brush,
    float strokeWidth)
{
    if (rt && brush) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawEllipse(
            centerX, centerY, radiusX, radiusY,
            reinterpret_cast<jalium::Brush*>(brush),
            strokeWidth);
    }
}

JALIUM_API void jalium_draw_line(
    JaliumRenderTarget* rt,
    float x1, float y1, float x2, float y2,
    JaliumBrush* brush,
    float strokeWidth)
{
    if (rt && brush) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawLine(
            x1, y1, x2, y2,
            reinterpret_cast<jalium::Brush*>(brush),
            strokeWidth);
    }
}

JALIUM_API void jalium_fill_polygon(
    JaliumRenderTarget* rt,
    const float* points,
    uint32_t pointCount,
    JaliumBrush* brush,
    int32_t fillRule)
{
    if (rt && brush && points && pointCount >= 3) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->FillPolygon(
            points, pointCount,
            reinterpret_cast<jalium::Brush*>(brush),
            fillRule);
    }
}

JALIUM_API void jalium_draw_polygon(
    JaliumRenderTarget* rt,
    const float* points,
    uint32_t pointCount,
    JaliumBrush* brush,
    float strokeWidth,
    int32_t closed,
    int32_t lineJoin,
    float miterLimit)
{
    if (rt && brush && points && pointCount >= 2) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawPolygon(
            points, pointCount,
            reinterpret_cast<jalium::Brush*>(brush),
            strokeWidth,
            closed != 0, lineJoin, miterLimit);
    }
}

JALIUM_API void jalium_fill_path(
    JaliumRenderTarget* rt,
    float startX, float startY,
    const float* commands,
    uint32_t commandLength,
    JaliumBrush* brush,
    int32_t fillRule)
{
    if (rt && brush && commands && commandLength > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->FillPath(
            startX, startY, commands, commandLength,
            reinterpret_cast<jalium::Brush*>(brush),
            fillRule);
    }
}

JALIUM_API void jalium_stroke_path(
    JaliumRenderTarget* rt,
    float startX, float startY,
    const float* commands,
    uint32_t commandLength,
    JaliumBrush* brush,
    float strokeWidth,
    int32_t closed,
    int32_t lineJoin,
    float miterLimit,
    int32_t lineCap,
    const float* dashPattern,
    uint32_t dashCount,
    float dashOffset)
{
    if (rt && brush && commands && commandLength > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->StrokePath(
            startX, startY, commands, commandLength,
            reinterpret_cast<jalium::Brush*>(brush),
            strokeWidth, closed != 0, lineJoin, miterLimit, lineCap,
            dashPattern, dashCount, dashOffset);
    }
}

JALIUM_API void jalium_draw_content_border(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    float blRadius, float brRadius,
    JaliumBrush* fillBrush, JaliumBrush* strokeBrush,
    float strokeWidth)
{
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawContentBorder(
            x, y, width, height, blRadius, brRadius,
            fillBrush ? reinterpret_cast<jalium::Brush*>(fillBrush) : nullptr,
            strokeBrush ? reinterpret_cast<jalium::Brush*>(strokeBrush) : nullptr,
            strokeWidth);
    }
}

JALIUM_API void jalium_draw_text(
    JaliumRenderTarget* rt,
    const wchar_t* text,
    uint32_t textLength,
    JaliumTextFormat* format,
    float x, float y, float width, float height,
    JaliumBrush* brush)
{
    if (rt && text && format && brush) {
#if defined(_WIN32)
        reinterpret_cast<jalium::RenderTarget*>(rt)->RenderText(
            text, textLength,
            reinterpret_cast<jalium::TextFormat*>(format),
            x, y, width, height,
            reinterpret_cast<jalium::Brush*>(brush));
#else
        // Managed code sends UTF-16 data but wchar_t is 4 bytes on Linux/Android.
        auto wstr = jalium::ManagedToWString(text, textLength);
        reinterpret_cast<jalium::RenderTarget*>(rt)->RenderText(
            wstr.c_str(), static_cast<uint32_t>(wstr.size()),
            reinterpret_cast<jalium::TextFormat*>(format),
            x, y, width, height,
            reinterpret_cast<jalium::Brush*>(brush));
#endif
    }
}

JALIUM_API void jalium_push_transform(JaliumRenderTarget* rt, const float* matrix) {
    if (rt && matrix) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->PushTransform(matrix);
    }
}

JALIUM_API void jalium_pop_transform(JaliumRenderTarget* rt) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->PopTransform();
    }
}

JALIUM_API void jalium_push_clip(JaliumRenderTarget* rt, float x, float y, float width, float height) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->PushClip(x, y, width, height);
    }
}

JALIUM_API void jalium_push_clip_aliased(JaliumRenderTarget* rt, float x, float y, float width, float height) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->PushClipAliased(x, y, width, height);
    }
}

JALIUM_API void jalium_push_rounded_rect_clip(JaliumRenderTarget* rt, float x, float y, float width, float height, float rx, float ry) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->PushRoundedRectClip(x, y, width, height, rx, ry);
    }
}

JALIUM_API void jalium_pop_clip(JaliumRenderTarget* rt) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->PopClip();
    }
}

JALIUM_API void jalium_punch_transparent_rect(JaliumRenderTarget* rt, float x, float y, float width, float height) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->PunchTransparentRect(x, y, width, height);
    }
}

JALIUM_API void jalium_push_opacity(JaliumRenderTarget* rt, float opacity) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->PushOpacity(opacity);
    }
}

JALIUM_API void jalium_pop_opacity(JaliumRenderTarget* rt) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->PopOpacity();
    }
}

JALIUM_API void jalium_set_shape_type(JaliumRenderTarget* rt, int type, float n) {
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->SetShapeType(type, n);
    }
}

JALIUM_API void jalium_draw_bitmap(
    JaliumRenderTarget* rt,
    JaliumImage* bitmap,
    float x, float y, float width, float height,
    float opacity)
{
    if (rt && bitmap) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawBitmap(
            reinterpret_cast<jalium::Bitmap*>(bitmap),
            x, y, width, height, opacity);
    }
}

JALIUM_API void jalium_draw_backdrop_filter(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    const char* backdropFilter,
    const char* material,
    const char* materialTint,
    float tintOpacity,
    float blurRadius,
    float cornerRadiusTL, float cornerRadiusTR,
    float cornerRadiusBR, float cornerRadiusBL)
{
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawBackdropFilter(
            x, y, width, height,
            backdropFilter ? backdropFilter : "",
            material ? material : "",
            materialTint ? materialTint : "",
            tintOpacity,
            blurRadius,
            cornerRadiusTL, cornerRadiusTR,
            cornerRadiusBR, cornerRadiusBL);
    }
}

JALIUM_API void jalium_draw_glowing_border_highlight(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    float animationPhase,
    float glowColorR, float glowColorG, float glowColorB,
    float strokeWidth,
    float trailLength,
    float dimOpacity,
    float screenWidth, float screenHeight)
{
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawGlowingBorderHighlight(
            x, y, width, height,
            animationPhase,
            glowColorR, glowColorG, glowColorB,
            strokeWidth,
            trailLength,
            dimOpacity,
            screenWidth, screenHeight);
    }
}

JALIUM_API void jalium_draw_glowing_border_transition(
    JaliumRenderTarget* rt,
    float fromX, float fromY, float fromWidth, float fromHeight,
    float toX, float toY, float toWidth, float toHeight,
    float headProgress, float tailProgress,
    float animationPhase,
    float glowColorR, float glowColorG, float glowColorB,
    float strokeWidth,
    float trailLength,
    float dimOpacity,
    float screenWidth, float screenHeight)
{
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawGlowingBorderTransition(
            fromX, fromY, fromWidth, fromHeight,
            toX, toY, toWidth, toHeight,
            headProgress, tailProgress,
            animationPhase,
            glowColorR, glowColorG, glowColorB,
            strokeWidth,
            trailLength,
            dimOpacity,
            screenWidth, screenHeight);
    }
}

JALIUM_API void jalium_draw_ripple_effect(
    JaliumRenderTarget* rt,
    float x, float y, float width, float height,
    float rippleProgress,
    float glowColorR, float glowColorG, float glowColorB,
    float strokeWidth,
    float dimOpacity,
    float screenWidth, float screenHeight)
{
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawRippleEffect(
            x, y, width, height,
            rippleProgress,
            glowColorR, glowColorG, glowColorB,
            strokeWidth,
            dimOpacity,
            screenWidth, screenHeight);
    }
}

JALIUM_API void jalium_capture_desktop_area(
    JaliumRenderTarget* rt,
    int32_t screenX, int32_t screenY,
    int32_t width, int32_t height)
{
    if (rt && width > 0 && height > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->CaptureDesktopArea(screenX, screenY, width, height);
    }
}

JALIUM_API void jalium_draw_desktop_backdrop(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h,
    float blurRadius,
    float tintR, float tintG, float tintB, float tintOpacity,
    float noiseIntensity, float saturation)
{
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawDesktopBackdrop(
            x, y, w, h, blurRadius, tintR, tintG, tintB, tintOpacity,
            noiseIntensity, saturation);
    }
}

JALIUM_API void jalium_transition_begin_capture(
    JaliumRenderTarget* rt, int32_t slot,
    float x, float y, float w, float h)
{
    if (rt && (slot == 0 || slot == 1) && w > 0 && h > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->BeginTransitionCapture(slot, x, y, w, h);
    }
}

JALIUM_API void jalium_transition_end_capture(JaliumRenderTarget* rt, int32_t slot)
{
    if (rt && (slot == 0 || slot == 1)) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->EndTransitionCapture(slot);
    }
}

JALIUM_API void jalium_draw_transition_shader(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h,
    float progress, int32_t mode)
{
    if (rt && w > 0 && h > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawTransitionShader(x, y, w, h, progress, mode);
    }
}

JALIUM_API void jalium_draw_captured_transition(
    JaliumRenderTarget* rt,
    int32_t slot, float x, float y, float w, float h, float opacity)
{
    if (rt && (slot == 0 || slot == 1) && w > 0 && h > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawCapturedTransition(slot, x, y, w, h, opacity);
    }
}

// ============================================================================
// Element Effect Capture & Rendering
// ============================================================================

JALIUM_API void jalium_effect_begin_capture(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h)
{
    #ifdef _WIN32
    OutputDebugStringA("[C API] jalium_effect_begin_capture CALLED\n");
    #endif
    if (rt && w > 0 && h > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->BeginEffectCapture(x, y, w, h);
    }
}

JALIUM_API void jalium_effect_end_capture(JaliumRenderTarget* rt)
{
    #ifdef _WIN32
    OutputDebugStringA("[C API] jalium_effect_end_capture CALLED\n");
    #endif
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->EndEffectCapture();
    }
}

JALIUM_API void jalium_draw_blur_effect(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h, float radius,
    float uvOffsetX, float uvOffsetY)
{
    if (rt && w > 0 && h > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawBlurEffect(
            x, y, w, h, radius, uvOffsetX, uvOffsetY);
    }
}

JALIUM_API void jalium_draw_drop_shadow_effect(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h,
    float blurRadius, float offsetX, float offsetY,
    float r, float g, float b, float a,
    float uvOffsetX, float uvOffsetY,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    if (rt && w > 0 && h > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawDropShadowEffect(
            x, y, w, h, blurRadius, offsetX, offsetY, r, g, b, a,
            uvOffsetX, uvOffsetY,
            cornerTL, cornerTR, cornerBR, cornerBL);
    }
}

JALIUM_API void jalium_draw_outer_glow_effect(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h,
    float glowSize, float r, float g, float b, float a, float intensity,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    if (rt && w > 0 && h > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawOuterGlowEffect(
            x, y, w, h, glowSize, r, g, b, a, intensity,
            cornerTL, cornerTR, cornerBR, cornerBL);
    }
}

JALIUM_API void jalium_draw_inner_shadow_effect(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h,
    float blurRadius, float offsetX, float offsetY,
    float r, float g, float b, float a,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    if (rt && w > 0 && h > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawInnerShadowEffect(
            x, y, w, h, blurRadius, offsetX, offsetY, r, g, b, a,
            cornerTL, cornerTR, cornerBR, cornerBL);
    }
}

JALIUM_API void jalium_draw_color_matrix_effect(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h,
    const float* matrix)
{
    if (rt && w > 0 && h > 0 && matrix) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawColorMatrixEffect(
            x, y, w, h, matrix);
    }
}

JALIUM_API void jalium_draw_emboss_effect(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h,
    float amount, float lightDirX, float lightDirY, float relief)
{
    if (rt && w > 0 && h > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawEmbossEffect(
            x, y, w, h, amount, lightDirX, lightDirY, relief);
    }
}

JALIUM_API void jalium_draw_shader_effect(
    JaliumRenderTarget* rt,
    float x, float y, float w, float h,
    const uint8_t* shaderBytecode,
    uint32_t shaderBytecodeSize,
    const float* constants,
    uint32_t constantFloatCount)
{
    if (rt && w > 0 && h > 0 && shaderBytecode && shaderBytecodeSize > 0) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawShaderEffect(
            x, y, w, h, shaderBytecode, shaderBytecodeSize, constants, constantFloatCount);
    }
}

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
    const float* neighborData)
{
    if (rt) {
        reinterpret_cast<jalium::RenderTarget*>(rt)->DrawLiquidGlass(
            x, y, w, h, cornerRadius, blurRadius,
            refractionAmount, chromaticAberration,
            tintR, tintG, tintB, tintOpacity,
            lightX, lightY, highlightBoost,
            shapeType, shapeExponent,
            neighborCount, fusionRadius, neighborData);
    }
}

} // extern "C"
