#include "jalium_api.h"
#import <AppKit/AppKit.h>
#import <CoreGraphics/CoreGraphics.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <stdexcept>
#include <vector>

namespace {
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
int scenes = 0, sampled = 0, failures = 0;
int Encode(double linear) {
    // Native gradient brushes currently interpolate linear light, then encode
    // sRGB. Keep this existing colour convention separate from coordinate tests.
    return static_cast<int>(std::lround(255 * (linear <= .0031308 ? 12.92 * linear : 1.055 * std::pow(linear, 1 / 2.4) - .055)));
}
void Scene(JaliumRenderTarget* target, JaliumBrush* brush, const std::array<float,6>& matrix,
           int dpi, int shape, bool radial) {
    ++scenes;
    Check(jalium_render_target_request_readback(target) == JALIUM_OK, "Request readback");
    Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "Begin frame");
    jalium_render_target_clear(target, 0, 0, 0, 0);
    jalium_push_transform(target, matrix.data());
    if (shape == 0) jalium_draw_fill_rectangle(target, 0, 0, 48, 40, brush);
    else if (shape == 1) jalium_draw_fill_rounded_rectangle(target, 0, 0, 48, 40, 6, 6, brush);
    else if (shape == 2) jalium_draw_fill_ellipse(target, 24, 20, 24, 20, brush);
    else if (shape == 3) {
        const float commands[] = {0,48,0, 0,48,40, 0,0,40, 5};
        jalium_fill_path(target, 0, 0, commands, 10, brush, 1, 2);
    }
    else if (shape == 4) jalium_draw_line(target, 0, 20, 48, 20, brush, 20);
    else jalium_draw_ellipse(target, 24,20,24,20,brush,8);
    jalium_pop_transform(target);
    jalium_draw_fill_rectangle(target, 0, 96, 48, 20, brush);
    Check(jalium_render_target_end_draw(target) == JALIUM_OK, "End frame");
    int size = 128 * dpi; int32_t width, height;
    std::vector<uint8_t> pixels(size * size * 4);
    Check(jalium_render_target_fetch_readback(target, pixels.data(), size * 4, &width, &height) == JALIUM_OK && width == size && height == size, "Fetch BGRA");
    const double determinant = matrix[0] * matrix[3] - matrix[1] * matrix[2];
    auto ellipse = CGPathCreateWithEllipseInRect(CGRectMake(0,0,48,40), nullptr);
    auto stroke = CGPathCreateCopyByStrokingPath(ellipse,nullptr,8,kCGLineCapButt,kCGLineJoinMiter,4);
    int checked = 0, bad = 0;
    for (int y = 0; y < size; ++y) for (int x = 0; x < size; ++x) {
        // Independent inverse and analytic red-blue ramps at pixel centres.
        const double px = (x + .5) / dpi - matrix[4];
        const double py = (y + .5) / dpi - matrix[5];
        const double lx = (matrix[3] * px - matrix[2] * py) / determinant;
        const double ly = (-matrix[1] * px + matrix[0] * py) / determinant;
        const auto* pixel = &pixels[(y * size + x) * 4];
        if (shape == 5) {
            int hits = 0;
            for (double dx : {-1.2,0.,1.2}) for (double dy : {-1.2,0.,1.2})
                hits += CGPathContainsPoint(stroke,nullptr,CGPointMake(lx+dx,ly+dy),false);
            if (hits == 0 && y < 90*dpi) { bad += pixel[3] > 2; continue; }
            if (hits != 9) continue;
        }
        else {
            if (shape == 2 && std::hypot((lx-24)/24,(ly-20)/20) > 1.2 && y < 90*dpi) { bad += pixel[3] > 2; continue; }
            if (lx < 4 || lx > 44 || ly < 4 || ly > 36) continue;
        }
        if (shape == 4 && (ly < 14 || ly > 26)) continue;
        if (shape == 2 && std::pow((lx - 24) / 24, 2) + std::pow((ly - 20) / 20, 2) > .8) continue;
        const double t = std::clamp(radial ? std::hypot((lx - 24) / 24, (ly - 20) / 20) : lx / 48, 0., 1.);
        const bool mismatch = pixel[3] < 253 || std::abs(int(pixel[0]) - Encode(t)) > 3 ||
               pixel[1] > 1 || std::abs(int(pixel[2]) - Encode(1 - t)) > 3;
        if (mismatch && bad == 0) std::fprintf(stderr, "first pixel=%d,%d local=%.4f,%.4f actualBGRA=%d,%d,%d,%d expectedB,R=%d,%d\n", x,y,lx,ly,pixel[0],pixel[1],pixel[2],pixel[3],Encode(t),Encode(1-t));
        bad += mismatch;
        ++checked;
    }
    Check(checked > 100, "Stable interior samples"); sampled += checked;
    CGPathRelease(stroke); CGPathRelease(ellipse);
    const auto* restored = &pixels[(100 * dpi * size + 12 * dpi) * 4];
    const double t = radial ? 1 : (12 + .5 / dpi) / 48;
    bad += restored[3] < 253 || std::abs(int(restored[0]) - Encode(t)) > 3;
    if (bad) { ++failures; std::fprintf(stderr, "gradient scene=%d dpi=%d shape=%d radial=%d bad=%d/%d\n", scenes, dpi, shape, radial, bad, checked); }
}
}
int main() { @autoreleasepool { try {
    [NSApplication sharedApplication];
    auto* context = jalium_context_create(JALIUM_BACKEND_SOFTWARE); Check(context != nullptr, "Software context");
    NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0,0,128,128)];
    JaliumSurfaceDescriptor descriptor{}; descriptor.platform = JALIUM_PLATFORM_MACOS;
    descriptor.kind = JALIUM_SURFACE_KIND_COMPOSITION_TARGET;
    descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
    const JaliumGradientStop stops[] = {{0,1,0,0,1},{1,0,0,1,1}};
    auto* linear = jalium_brush_create_linear_gradient(context, 0,0,48,0, stops,2,0);
    auto* radial = jalium_brush_create_radial_gradient(context, 24,20,24,20,24,20, stops,2,0);
    Check(linear && radial, "Gradient brushes");
    // First and last have the same device rectangle and opposite gradient
    // direction, exercising reuse of the same brush's bounded raster cache.
    const std::array<std::array<float,6>,6> transforms = {{{1,0,0,1,32,24}, {1.5f,0,0,.75f,24,30}, {0,1,-1,0,100,20}, {1,.2f,.3f,1,20,20}, {-1,0,0,1,100,24}, {-1,0,0,1,80,24}}};
    for (int dpi : {1,2}) {
        auto* target = jalium_render_target_create_for_composition_surface(context, &descriptor, 128*dpi,128*dpi);
        Check(target != nullptr, "Software composition target"); jalium_render_target_set_dpi(target,96*dpi,96*dpi);
        for (const auto& transform : transforms) for (int shape=0; shape<6; ++shape) {
            Scene(target,linear,transform,dpi,shape,false); Scene(target,radial,transform,dpi,shape,true);
        }
        jalium_render_target_destroy(target);
    }
    jalium_brush_destroy(linear); jalium_brush_destroy(radial); jalium_context_destroy(context);
    std::printf("%s Software gradients: %d scenes, %d analytic pixel samples, %d failed scenes\n", failures ? "FAIL" : "PASS", scenes,sampled,failures);
    return failures ? 1 : 0;
} catch (const std::exception& error) { std::fprintf(stderr,"FAIL %s\n",error.what()); return 1; } } }
