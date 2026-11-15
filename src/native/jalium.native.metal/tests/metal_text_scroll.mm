#include "jalium_api.h"
#import <AppKit/AppKit.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <stdexcept>
#include <string_view>
#include <thread>
#include <vector>

namespace {
constexpr int W = 384, H = 160;
constexpr char16_t Text[] = u"Scroll A\u0301 \u4e2d\u6587 \U0001f642";
constexpr uint32_t Length = sizeof(Text) / sizeof(char16_t) - 1;
void Check(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

struct Surface {
    JaliumContext* context = jalium_context_create(JALIUM_BACKEND_METAL);
    NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, W, H)];
    JaliumRenderTarget* target = nullptr;
    JaliumTextFormat* font = nullptr;
    JaliumBrush* brush = nullptr;
    JaliumTextParagraph* paragraph = nullptr;
    int scale;

    Surface(int dpi, JaliumRenderingEngine engine) : scale(dpi) {
        Check(context != nullptr, "Metal context");
        JaliumSurfaceDescriptor descriptor{};
        descriptor.platform = JALIUM_PLATFORM_MACOS;
        descriptor.kind = JALIUM_SURFACE_KIND_COMPOSITION_TARGET;
        descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
        target = jalium_render_target_create_for_composition_surface(context, &descriptor, W * scale, H * scale);
        Check(target != nullptr, "Text scroll surface");
        jalium_render_target_set_dpi(target, 96 * scale, 96 * scale);
        Check(jalium_render_target_set_engine(target, engine) == JALIUM_OK, "Text scroll engine");
        font = jalium_text_format_create(context, reinterpret_cast<const wchar_t*>(u"Georgia"), 22, 400, 0);
        brush = jalium_brush_create_solid(context, .15f, .35f, .8f, .8f);
        Check(font && brush, "Text resources");
        JaliumTextSpan span{font, 0, Length, .15f, .35f, .8f, .8f};
        paragraph = jalium_text_paragraph_create(font, reinterpret_cast<const uint16_t*>(Text), Length,
            &span, 1, 310, 0, 0, 0);
        Check(paragraph != nullptr, "Text paragraph");
    }
    ~Surface() {
        jalium_text_paragraph_destroy(paragraph);
        jalium_brush_destroy(brush);
        jalium_text_format_destroy(font);
        jalium_render_target_destroy(target);
        jalium_context_destroy(context);
    }
    int CacheEntries() {
        JaliumGpuStats stats{};
        Check(jalium_render_target_query_gpu_stats(target, &stats) == JALIUM_OK, "Text cache stats");
        return stats.glyphSlotsUsed;
    }
    std::vector<uint8_t> Capture(float x, float y, bool retained, bool cold = false,
        float m11 = 1, float m12 = 0, float m21 = 0, float m22 = 1) {
        if (cold) Check(jalium_render_target_reclaim_idle_resources(target) == JALIUM_OK, "Drop reference cache");
        Check(jalium_render_target_request_readback(target) == JALIUM_OK, "Request text readback");
        Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "Begin text frame");
        jalium_render_target_clear(target, 0, 0, 0, 0);
        const float matrix[]{m11, m12, m21, m22, x, y};
        jalium_push_transform(target, matrix);
        // Paragraph rendering requires an active encoder, established by Clear.
        if (retained) Check(jalium_render_target_draw_paragraph_line(target, paragraph, 0, 12, 18, .7f)
            == JALIUM_OK, "Draw paragraph");
        else jalium_draw_text(target, reinterpret_cast<const wchar_t*>(Text), Length, font, 12, 18, 310, 60, brush);
        jalium_pop_transform(target);
        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "End text frame");
        int32_t width = 0, height = 0;
        std::vector<uint8_t> pixels(W * H * scale * scale * 4);
        Check(jalium_render_target_fetch_readback(target, pixels.data(), W * scale * 4, &width, &height)
            == JALIUM_OK && width == W * scale && height == H * scale, "Text readback");
        return pixels;
    }
    double DrawScrollFrame(int frame) {
        auto acquired = jalium_render_target_begin_draw(target);
        for (int retry = 0; acquired == JALIUM_ERROR_INVALID_STATE && retry < 500; ++retry) {
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
            acquired = jalium_render_target_begin_draw(target);
        }
        Check(acquired == JALIUM_OK, "Acquire benchmark frame");
        const auto start = std::chrono::steady_clock::now();
        jalium_render_target_clear(target, 0, 0, 0, 0);
        for (int label = 0; label < 16; ++label) {
            const float matrix[]{1, 0, 0, 1, .375f + frame % 19, .625f + label * 5 + frame / 19};
            jalium_push_transform(target, matrix);
            jalium_draw_text(target, reinterpret_cast<const wchar_t*>(Text), Length, font, 12, 18, 310, 60, brush);
            jalium_pop_transform(target);
        }
        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "End benchmark frame");
        return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
    }
};

void EqualPixels(const std::vector<uint8_t>& actual, const std::vector<uint8_t>& expected) {
    Check(actual.size() == expected.size(), "Text comparison size");
    int maximum = 0;
    for (size_t i = 0; i < actual.size(); ++i)
        maximum = std::max(maximum, std::abs(int(actual[i]) - expected[i]));
    if (maximum > 1) std::fprintf(stderr, "Scrolled text pixel delta: %d\n", maximum);
    Check(maximum <= 1, "Scrolled text differs from cold rasterization");
}

void TestScroll(int dpi, JaliumRenderingEngine engine, bool retained) {
    Surface cached(dpi, engine), reference(dpi, engine);
    cached.Capture(.375f, .625f, retained);
    const int initial = cached.CacheEntries();
    Check(initial == 1, "Initial text cache entry");
    for (int i = 1; i <= 20; ++i) {
        const float x = .375f + i, y = .625f + i * 2;
        EqualPixels(cached.Capture(x, y, retained), reference.Capture(x, y, retained, true));
    }
    std::printf("text_scroll engine=%d dpi=%d paragraph=%d integer_entries=%d\n",
        engine, dpi, retained, cached.CacheEntries());
    Check(cached.CacheEntries() == initial, "Integer scrolling must reuse the text raster");
    EqualPixels(cached.Capture(5.625f, 8.375f, retained), reference.Capture(5.625f, 8.375f, retained, true));
    Check(cached.CacheEntries() == initial + 1, "A different pixel phase needs a separate raster");
    // Reusing a whole raster outside the viewport must preserve exact clipping.
    for (auto position : {std::array<float, 2>{-41.625f, -24.375f}, {280.375f, 124.625f}, {3.375f, 7.625f}})
        EqualPixels(cached.Capture(position[0], position[1], retained),
            reference.Capture(position[0], position[1], retained, true));

    // A clipped first draw must never become the canonical, incomplete raster.
    Surface clipped(dpi, engine);
    clipped.Capture(-41.625f, -24.375f, retained);
    EqualPixels(clipped.Capture(.375f, .625f, retained), reference.Capture(.375f, .625f, retained, true));
    EqualPixels(clipped.Capture(9.375f, 11.625f, retained), reference.Capture(9.375f, 11.625f, retained, true));
    Check(clipped.CacheEntries() == 1, "Replace clipped raster without leaking cache accounting");

    // Rotation/shear and their fractional device origin remain part of the key.
    EqualPixels(cached.Capture(30.375f, 6.625f, retained, false, .94f, .12f, -.08f, 1.02f),
        reference.Capture(30.375f, 6.625f, retained, true, .94f, .12f, -.08f, 1.02f));
    EqualPixels(cached.Capture(43.375f, 14.625f, retained, false, .94f, .12f, -.08f, 1.02f),
        reference.Capture(43.375f, 14.625f, retained, true, .94f, .12f, -.08f, 1.02f));
}
}

int main(int argc, char** argv) { @autoreleasepool {
    [NSApplication sharedApplication];
    try {
        if (argc == 2 && std::string_view(argv[1]) == "--benchmark") {
            Surface surface(1, JALIUM_ENGINE_IMPELLER);
            std::vector<double> times;
            for (int frame = 0; frame < 105; ++frame) {
                const double time = surface.DrawScrollFrame(frame);
                if (frame >= 5) times.push_back(time);
            }
            std::sort(times.begin(), times.end());
            std::printf("text_scroll_benchmark frames=%zu labels_per_frame=16 p50_ms=%.3f p95_ms=%.3f max_ms=%.3f entries=%d\n",
                times.size(), times[times.size()/2], times[size_t(std::ceil(times.size()*.95))-1], times.back(), surface.CacheEntries());
            return 0;
        }
        for (auto engine : {JALIUM_ENGINE_IMPELLER, JALIUM_ENGINE_VELLO})
            for (int dpi : {1, 2}) for (bool paragraph : {false, true}) TestScroll(dpi, engine, paragraph);
        std::puts("PASS: scroll cache reuse and cold-raster pixel parity");
        return 0;
    } catch (const std::exception& error) {
        std::fprintf(stderr, "FAIL: %s\n", error.what());
        return 1;
    }
} }
