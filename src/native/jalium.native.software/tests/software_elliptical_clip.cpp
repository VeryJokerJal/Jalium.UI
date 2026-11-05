#include "jalium_api.h"
#include <array>
#include <cmath>
#include <functional>
#include <iostream>
#include <limits>
#include <vector>

extern "C" void jalium_software_init();

namespace {
constexpr int width = 128, height = 96;
int failures = 0;
void Check(bool value, const char* message)
{
    if (!value) { ++failures; std::cerr << "FAIL: " << message << '\n'; }
}

JaliumEllipticalRectClip Ellipse(float x, float y, float w, float h)
{
    JaliumEllipticalRectClip clip {};
    clip.structSize = sizeof(clip); clip.edges = 15;
    clip.x = x; clip.y = y; clip.width = w; clip.height = h;
    for (int i = 0; i < 4; ++i) { clip.radiusX[i] = w / 2; clip.radiusY[i] = h / 2; }
    return clip;
}

bool InEllipse(double px, double py, double cx, double cy, double rx, double ry)
{
    // Native raster spans use a half-open bounding rectangle, including when
    // a transformed pixel centre lands exactly on the ellipse's bottom tip.
    if (px < cx - rx || py < cy - ry || px >= cx + rx || py >= cy + ry) return false;
    const auto x = (px - cx) / rx, y = (py - cy) / ry;
    return x * x + y * y <= 1;
}

void Scene(JaliumRenderTarget* target, JaliumBrush* brush, const char* name,
    const std::function<void()>& push, int pops, const std::function<bool(double, double)>& expected)
{
    Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "BeginDraw");
    jalium_render_target_clear(target, 0, 0, 0, 1);
    push();
    jalium_draw_fill_rectangle(target, 0, 0, width, height, brush);
    for (int i = 0; i < pops; ++i) jalium_pop_clip(target);
    Check(jalium_render_target_request_readback(target) == JALIUM_OK, "readback request");
    Check(jalium_render_target_end_draw(target) == JALIUM_OK, "EndDraw");
    std::vector<uint8_t> pixels(width * height * 4);
    int32_t w = 0, h = 0;
    Check(jalium_render_target_fetch_readback(target, pixels.data(), width * 4, &w, &h) == JALIUM_OK, "readback");
    Check(w == width && h == height, "readback dimensions");
    int differences = 0;
    for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x) {
        const auto offset = (y * width + x) * 4;
        const int green = expected(x + .5, y + .5) ? 255 : 0;
        if (pixels[offset] != 0 || pixels[offset + 1] != green || pixels[offset + 2] != 0 || pixels[offset + 3] != 255) {
            if (differences < 3) std::cerr << name << " at " << x << ',' << y << ": " << int(pixels[offset + 1]) << " vs " << green << '\n';
            ++differences;
        }
    }
    if (differences) { ++failures; std::cerr << "FAIL: " << name << ": " << differences << " differing pixels\n"; }
}
}

int main()
{
    static_assert(sizeof(JaliumEllipticalRectClip) == 56);
    jalium_software_init();
    auto* context = jalium_context_create(JALIUM_BACKEND_SOFTWARE);
    Check(context != nullptr, "software context");
    if (!context) return 1;
    JaliumSurfaceDescriptor surface {};
    surface.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW; surface.handle0 = 1;
    auto* target = jalium_render_target_create_for_surface(context, &surface, width, height);
    auto* brush = jalium_brush_create_solid(context, 0, 1, 0, 1);
    Check(target && brush, "headless target and brush");
    if (!target || !brush) return 1;

    auto oval = Ellipse(8, 12, 100, 60);
    Scene(target, brush, "two-axis radii", [&] {
        Check(jalium_push_elliptical_rect_clip(target, &oval) == JALIUM_OK, "ellipse push");
    }, 1, [](double x, double y) { return InEllipse(x, y, 58, 42, 50, 30); });

    auto inner = Ellipse(24, 4, 80, 72);
    Scene(target, brush, "nested intersection and plain child clip", [&] {
        Check(jalium_push_elliptical_rect_clip(target, &oval) == JALIUM_OK, "outer push");
        Check(jalium_push_elliptical_rect_clip(target, &inner) == JALIUM_OK, "inner push");
        jalium_push_clip(target, 0, 0, 128, 96);
    }, 3, [](double x, double y) { return InEllipse(x, y, 58, 42, 50, 30) && InEllipse(x, y, 64, 40, 40, 36); });

    Scene(target, brush, "pop restores ancestor", [&] {
        Check(jalium_push_elliptical_rect_clip(target, &oval) == JALIUM_OK, "outer push");
        Check(jalium_push_elliptical_rect_clip(target, &inner) == JALIUM_OK, "inner push");
        jalium_pop_clip(target);
    }, 1, [](double x, double y) { return InEllipse(x, y, 58, 42, 50, 30); });

    Scene(target, brush, "captured nonuniform transform", [&] {
        const float transform[6] { .75f, .25f, -.125f, .5f, 20, 4 };
        jalium_push_transform(target, transform);
        Check(jalium_push_elliptical_rect_clip(target, &oval) == JALIUM_OK, "transformed push");
        jalium_pop_transform(target);
    }, 1, [](double x, double y) {
        x -= 20; y -= 4;
        return InEllipse((.5 * x + .125 * y) / .40625, (-.25 * x + .75 * y) / .40625, 58, 42, 50, 30);
    });

    auto corner = Ellipse(20, 20, 60, 40);
    corner.edges = 3; corner.radiusX[0] = 25; corner.radiusY[0] = 12;
    Scene(target, brush, "open right and bottom edges", [&] {
        Check(jalium_push_elliptical_rect_clip(target, &corner) == JALIUM_OK, "open push");
    }, 1, [](double x, double y) {
        if (x < 20 || y < 20) return false;
        return x >= 45 || y >= 32 || InEllipse(x, y, 45, 32, 25, 12);
    });

    auto thick = Ellipse(30, 30, 40, 40);
    for (int i = 0; i < 4; ++i) thick.radiusX[i] = thick.radiusY[i] = 0;
    thick.radiusX[1] = 70; thick.radiusY[1] = 70;
    Scene(target, brush, "inner curve crosses opposite borders", [&] {
        Check(jalium_push_elliptical_rect_clip(target, &thick) == JALIUM_OK, "thick border push");
    }, 1, [](double x, double y) { return x >= 30 && x < 70 && y >= 30 && y < 70 && InEllipse(x, y, 0, 100, 70, 70); });

    Scene(target, brush, "invalid requests leave existing clip intact", [&] {
        jalium_push_clip(target, 12, 14, 40, 30);
        auto invalid = oval; invalid.structSize = 0;
        Check(jalium_push_elliptical_rect_clip(target, &invalid) == JALIUM_ERROR_INVALID_ARGUMENT, "invalid size");
        invalid = oval; invalid.radiusY[2] = std::numeric_limits<float>::quiet_NaN();
        Check(jalium_push_elliptical_rect_clip(target, &invalid) == JALIUM_ERROR_INVALID_ARGUMENT, "NaN radius");
        invalid = oval; invalid.edges = 16;
        Check(jalium_push_elliptical_rect_clip(target, &invalid) == JALIUM_ERROR_INVALID_ARGUMENT, "invalid edges");
        Check(jalium_push_elliptical_rect_clip(target, nullptr) == JALIUM_ERROR_INVALID_ARGUMENT, "null descriptor");
    }, 1, [](double x, double y) { return x >= 12 && x < 52 && y >= 14 && y < 44; });

    Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "shadow BeginDraw");
    jalium_render_target_clear(target, 0, 0, 0, 1);
    jalium_effect_begin_capture(target, 0, 0, width, height);
    Check(jalium_push_elliptical_rect_clip(target, &oval) == JALIUM_OK, "shadow ellipse push");
    jalium_draw_fill_rectangle(target, 0, 0, width, height, brush);
    jalium_pop_clip(target);
    jalium_effect_end_capture(target);
    Check(jalium_draw_drop_shadow_effect_elliptical(target,
        0, 0, width, height, 2, 4, 3, 1, 0, 0, 1, 0, 0, &oval) == JALIUM_OK,
        "elliptical shadow side interface");
    auto invalidShadow = oval; invalidShadow.structSize = 0;
    Check(jalium_draw_drop_shadow_effect_elliptical(target,
        0, 0, width, height, 2, 4, 3, 1, 0, 0, 1, 0, 0, &invalidShadow) ==
        JALIUM_ERROR_INVALID_ARGUMENT, "invalid shadow descriptor");
    Check(jalium_render_target_request_readback(target) == JALIUM_OK, "shadow readback request");
    Check(jalium_render_target_end_draw(target) == JALIUM_OK, "shadow EndDraw");
    std::vector<uint8_t> shadowPixels(width * height * 4);
    int32_t shadowW = 0, shadowH = 0;
    Check(jalium_render_target_fetch_readback(target, shadowPixels.data(), width * 4,
        &shadowW, &shadowH) == JALIUM_OK, "shadow readback");
    int redPixels = 0, greenPixels = 0;
    for (size_t i = 0; i + 3 < shadowPixels.size(); i += 4) {
        if (shadowPixels[i + 2] > 8) ++redPixels;
        if (shadowPixels[i + 1] > 128) ++greenPixels;
    }
    Check(redPixels > 0, "elliptical shadow emitted tinted pixels");
    Check(greenPixels > 0, "elliptical shadow retained captured content");

    auto inset = Ellipse(20, 20, 88, 48);
    for (int i = 0; i < 4; ++i) {
        inset.radiusX[i] = 32;
        inset.radiusY[i] = 8;
    }
    auto RenderInset = [&](float spread, bool paintContent) {
        Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "inset BeginDraw");
        jalium_render_target_clear(target, 0, 0, 0, 1);
        jalium_effect_begin_capture(target, 0, 0, width, height);
        if (paintContent)
            jalium_draw_fill_rectangle(target, 20, 20, 88, 48, brush);
        jalium_effect_end_capture(target);
        Check(jalium_draw_inner_shadow_effect_elliptical(target,
            20, 20, 88, 48, 8, 0, 0, spread,
            1, 0, 0, 1, 0, 0, &inset) == JALIUM_OK,
            "elliptical inset side interface");
        Check(jalium_render_target_request_readback(target) == JALIUM_OK,
            "inset readback request");
        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "inset EndDraw");
        std::vector<uint8_t> pixels(width * height * 4);
        int32_t actualWidth = 0, actualHeight = 0;
        Check(jalium_render_target_fetch_readback(target, pixels.data(), width * 4,
            &actualWidth, &actualHeight) == JALIUM_OK, "inset readback");
        Check(actualWidth == width && actualHeight == height, "inset dimensions");
        return pixels;
    };
    auto Red = [](const std::vector<uint8_t>& pixels, int x, int y) {
        return pixels[(y * width + x) * 4 + 2];
    };
    const auto insetBase = RenderInset(0, true);
    const auto insetPositive = RenderInset(6, true);
    const auto insetNegative = RenderInset(-6, true);
    const auto insetTransparent = RenderInset(4, false);
    Check(Red(insetBase, 64, 22) > 25, "zero-offset inset has a visible inner edge");
    Check(Red(insetBase, 64, 44) < 8, "zero-offset inset leaves the center clear");
    Check(Red(insetBase, 64, 18) == 0, "inset stays inside its contour");
    Check(Red(insetPositive, 64, 27) > Red(insetBase, 64, 27) + 35,
        "positive inset spread widens the band");
    Check(Red(insetNegative, 64, 22) + 25 < Red(insetBase, 64, 22),
        "negative inset spread narrows the band");
    Check(Red(insetTransparent, 64, 22) > 40,
        "inset shadow does not depend on captured background alpha");

    Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "invalid inset BeginDraw");
    auto invalidInset = inset;
    invalidInset.radiusY[2] = std::numeric_limits<float>::quiet_NaN();
    Check(jalium_draw_inner_shadow_effect_elliptical(target,
        20, 20, 88, 48, 8, 0, 0, 1,
        1, 0, 0, 1, 0, 0, &invalidInset) == JALIUM_ERROR_INVALID_ARGUMENT,
        "invalid inset descriptor is rejected");
    Check(jalium_render_target_end_draw(target) == JALIUM_OK, "invalid inset EndDraw");

    Scene(target, brush, "all clips popped", [] {}, 0, [](double, double) { return true; });
    jalium_brush_destroy(brush); jalium_render_target_destroy(target); jalium_context_destroy(context);
    if (failures) return 1;
    std::cout << "Native elliptical clip ABI and pixel tests passed\n";
    return 0;
}
