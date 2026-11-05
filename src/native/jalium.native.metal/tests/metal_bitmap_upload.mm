#include "jalium_api.h"
#include "metal_backend.h"
#import <AppKit/AppKit.h>
#import <Metal/Metal.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <future>
#include <memory>
#include <stdexcept>
#include <thread>
#include <vector>

extern "C" void jalium_metal_init();

namespace {
using Clock = std::chrono::steady_clock;
using Bitmap = std::unique_ptr<JaliumImage, decltype(&jalium_bitmap_destroy)>;
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
double Milliseconds(Clock::time_point start) {
    return std::chrono::duration<double, std::milli>(Clock::now() - start).count();
}

struct Surface {
    NSView* view;
    std::unique_ptr<JaliumRenderTarget, decltype(&jalium_render_target_destroy)> target;
    std::vector<uint8_t> pixels = std::vector<uint8_t>(512 * 320 * 4);
    Surface(JaliumContext* context, JaliumRenderingEngine engine)
        : view([[NSView alloc] initWithFrame:NSMakeRect(0, 0, 256, 160)]),
          target(nullptr, jalium_render_target_destroy) {
        JaliumSurfaceDescriptor descriptor{};
        descriptor.platform = JALIUM_PLATFORM_MACOS;
        descriptor.kind = JALIUM_SURFACE_KIND_COMPOSITION_TARGET;
        descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
        target.reset(jalium_render_target_create_for_composition_surface(context, &descriptor, 512, 320));
        Check(target != nullptr, "Bitmap upload surface creation failed");
        jalium_render_target_set_dpi(target.get(), 192, 192);
        jalium_render_target_set_path_msaa(target.get(), 4);
        Check(jalium_render_target_set_engine(target.get(), engine) == JALIUM_OK, "Bitmap engine selection failed");
    }
    void Begin() {
        Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK, "Request readback failed");
        Check(jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "Begin failed");
        jalium_render_target_clear(target.get(), 0, 0, 0, 0);
    }
    void End() {
        Check(jalium_render_target_end_draw(target.get()) == JALIUM_OK, "End failed");
        int32_t width{}, height{};
        Check(jalium_render_target_fetch_readback(target.get(), pixels.data(), 512 * 4, &width, &height)
            == JALIUM_OK && width == 512 && height == 320, "Readback failed");
    }
    void Pixel(int x, int y, std::array<int, 4> expected) {
        const auto* actual = pixels.data() + (y * 2 * 512 + x * 2) * 4;
        for (int c = 0; c < 4; ++c) {
            if (std::abs(actual[c] - expected[c]) > 2) {
                std::fprintf(stderr, "Bitmap pixel %d,%d channel %d: %d expected %d\n",
                    x, y, c, actual[c], expected[c]);
                throw std::runtime_error("Bitmap mip/alpha pixel mismatch");
            }
        }
    }
};

void TestMipmaps(JaliumContext* context, JaliumRenderingEngine engine) {
    Surface first(context, engine), second(context, engine);
    auto brush = jalium_brush_create_solid(context, 1, 1, 1, 1);
    Check(brush != nullptr, "Brush creation failed");
    for (auto size : {std::array{1, 1}, std::array{1, 257}, std::array{257, 1},
                      std::array{63, 37}, std::array{512, 288}}) {
        const int width = size[0], height = size[1], stride = width * 4 + 12;
        std::vector<uint8_t> source(stride * height, 0xCD);
        for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x) {
            uint8_t* pixel = source.data() + y * stride + x * 4;
            pixel[0] = 64; pixel[1] = 128; pixel[2] = 240; pixel[3] = 128;
        }
        Bitmap image(jalium_bitmap_create_from_pixels(context, source.data(), width, height, stride),
            jalium_bitmap_destroy);
        Check(image != nullptr, "Padded bitmap creation failed");
        for (auto* surface : {&first, &second, &first}) {
            surface->Begin();
            // An encoder is already open when the first texture is uploaded.
            jalium_draw_fill_rectangle(surface->target.get(), 200, 100, 8, 8, brush);
            for (int mode : {0, 1, 2, 3})
                jalium_draw_bitmap_ex(surface->target.get(), image.get(), 8 + mode * 40, 8, 24, 24, 1, mode);
            surface->End();
            for (int mode : {0, 1, 2, 3}) surface->Pixel(20 + mode * 40, 20, {32, 64, 120, 128});
        }
    }
    jalium_brush_destroy(brush);

    // Independent expected average: half opaque red and half transparent cyan
    // must produce premultiplied half-red, with no cyan fringe at small sizes.
    std::vector<uint8_t> checker(256 * 256 * 4);
    for (int y = 0; y < 256; ++y) for (int x = 0; x < 256; ++x) {
        auto* pixel = checker.data() + (y * 256 + x) * 4;
        bool red = (x + y) % 2 == 0;
        pixel[0] = pixel[1] = red ? 0 : 255;
        pixel[2] = pixel[3] = red ? 255 : 0;
    }
    Bitmap image(jalium_bitmap_create_from_pixels(context, checker.data(), 256, 256, 256 * 4), jalium_bitmap_destroy);
    Check(image != nullptr, "Checker bitmap creation failed");
    first.Begin();
    jalium_draw_bitmap_ex(first.target.get(), image.get(), 8, 8, 8, 8, 1, 2);
    first.End();
    first.Pixel(12, 12, {0, 0, 128, 128});
}

void TestAllAlphaPairs(JaliumContext* context, JaliumRenderingEngine engine) {
    Surface surface(context, engine);
    constexpr int width = 257, height = 256, stride = width * 4 + 7;
    std::vector<uint8_t> source(stride * height, 0xCD);
    for (int alpha = 0; alpha < height; ++alpha) for (int x = 0; x < width; ++x) {
        auto* pixel = source.data() + alpha * stride + x * 4;
        pixel[0] = static_cast<uint8_t>(x);
        pixel[1] = static_cast<uint8_t>(255 - x);
        pixel[2] = static_cast<uint8_t>(x * 73);
        pixel[3] = static_cast<uint8_t>(alpha);
    }
    Bitmap image(jalium_bitmap_create_from_pixels(context, source.data(), width, height, stride), jalium_bitmap_destroy);
    Check(image != nullptr, "Alpha matrix bitmap creation failed");
    surface.Begin();
    // One source pixel per device pixel, nearest sampling; the final column
    // exercises the scalar tail and each padded row changes vector alignment.
    jalium_draw_bitmap_ex(surface.target.get(), image.get(), 0, 0, width / 2.f, height / 2.f, 1, 3);
    surface.End();
    for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x) {
        const auto* input = source.data() + y * stride + x * 4;
        const auto* actual = surface.pixels.data() + (y * 512 + x) * 4;
        for (int channel = 0; channel < 4; ++channel) {
            const int expected = channel == 3 ? y : (input[channel] * y + 127) / 255;
            Check(std::abs(actual[channel] - expected) <= 1, "Exhaustive alpha matrix mismatch");
        }
    }
}

void TestBusyFrameAcquisition() {
    jalium::MetalBackend backend;
    Check(backend.Initialize(), "Busy-frame backend initialization failed");
    NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 64, 64)];
    JaliumSurfaceDescriptor descriptor{};
    descriptor.platform = JALIUM_PLATFORM_MACOS;
    descriptor.kind = JALIUM_SURFACE_KIND_COMPOSITION_TARGET;
    descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
    std::unique_ptr<jalium::RenderTarget> target(backend.CreateRenderTargetForCompositionSurface(&descriptor, 64, 64));
    Check(target != nullptr, "Busy-frame target creation failed");
    auto device = (__bridge id<MTLDevice>)backend.DeviceHandle();
    auto queue = (__bridge id<MTLCommandQueue>)backend.CommandQueueHandle();
    id<MTLSharedEvent> event = [device newSharedEvent];
    id<MTLCommandBuffer> blocker = [queue commandBuffer];
    [blocker encodeWaitForEvent:event value:1];
    [blocker commit];
    // The future also releases the queue on assertion failure, before target
    // destruction waits for outstanding buffers. No test can strand the GPU.
    auto release = std::async(std::launch::async, [event] {
        std::this_thread::sleep_for(std::chrono::milliseconds(400));
        event.signaledValue = 1;
    });
    for (int frame = 0; frame < 3; ++frame) {
        Check(target->BeginDraw() == JALIUM_OK, "Initial frame credit missing");
        target->Clear(0, 0, 0, 0);
        Check(target->EndDraw() == JALIUM_OK, "Queued frame submission failed");
    }
    auto start = Clock::now();
    auto busy = target->BeginDraw();
    double elapsed = Milliseconds(start);
    if (busy == JALIUM_OK) {
        target->Clear(0, 0, 0, 0);
        (void)target->EndDraw();
    }
    release.get();
    std::printf("FRAME_ACQUIRE busy_ms=%.3f result=%d\n", elapsed, static_cast<int>(busy));
    Check(busy == JALIUM_ERROR_INVALID_STATE && elapsed < 100,
        "Busy frame acquisition blocked instead of yielding to input");
    JaliumResult recovered = JALIUM_ERROR_INVALID_STATE;
    for (int retry = 0; retry < 200 && recovered == JALIUM_ERROR_INVALID_STATE; ++retry) {
        recovered = target->BeginDraw();
        if (recovered != JALIUM_OK) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    Check(recovered == JALIUM_OK, "Frame credit did not recover after GPU progress");
    target->Clear(0, 0, 0, 0);
    Check(target->EndDraw() == JALIUM_OK, "Recovered frame submission failed");
    std::puts("FRAME_ACQUIRE recovered=true");
}

void TestBatchedRetainedRetirement() {
    jalium::MetalBackend backend;
    Check(backend.Initialize(), "Retirement backend initialization failed");
    NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 64, 64)];
    JaliumSurfaceDescriptor descriptor{};
    descriptor.platform = JALIUM_PLATFORM_MACOS;
    descriptor.kind = JALIUM_SURFACE_KIND_COMPOSITION_TARGET;
    descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
    std::unique_ptr<jalium::RenderTarget> target(backend.CreateRenderTargetForCompositionSurface(&descriptor, 64, 64));
    Check(target != nullptr, "Retirement target creation failed");
    Check(target->BeginDraw() == JALIUM_OK, "Begin retained creation");
    std::vector<void*> layers;
    for (int i = 0; i < 256; ++i) {
        void* layer = target->RealizeLayerBegin(nullptr, 0, 0, 64, 64);
        Check(layer != nullptr, "Create retained layer");
        target->Clear(1, 0, 0, 1);
        target->RealizeLayerEnd(layer);
        layers.push_back(layer);
    }
    Check(target->EndDraw() == JALIUM_OK && target->WaitForCompletion() == JALIUM_OK, "Finish retained creation");
    auto device = (__bridge id<MTLDevice>)backend.DeviceHandle();
    auto queue = (__bridge id<MTLCommandQueue>)backend.CommandQueueHandle();
    id<MTLSharedEvent> event = [device newSharedEvent];
    id<MTLCommandBuffer> blocker = [queue commandBuffer];
    [blocker encodeWaitForEvent:event value:1];
    [blocker commit];
    auto release = std::async(std::launch::async, [event] {
        std::this_thread::sleep_for(std::chrono::milliseconds(400));
        event.signaledValue = 1;
    });
    auto start = Clock::now();
    for (void* layer : layers) target->DestroyRetainedLayer(layer);
    double elapsed = Milliseconds(start);
    uint64_t orphaned = 0, graveyard = 0;
    Check(target->DebugGetRetainedDestroyCounts(&orphaned, &graveyard), "Retained retirement counters");
    release.get();
    std::printf("LAYER_RETIRE count=%zu enqueue_ms=%.3f before_fence=%llu\n", layers.size(), elapsed,
        static_cast<unsigned long long>(graveyard));
    Check(elapsed < 100, "Retired layers saturated the command queue and blocked input");
    Check(orphaned == 0 && graveyard == 0, "Retired layers were deleted before their frame fence");
    Check(target->BeginDraw() == JALIUM_OK, "Begin batched retirement frame");
    target->Clear(0, 0, 0, 0);
    Check(target->EndDraw() == JALIUM_OK && target->WaitForCompletion() == JALIUM_OK, "Complete retirement fence");
    Check(target->DebugGetRetainedDestroyCounts(&orphaned, &graveyard) && orphaned == 0 && graveyard == layers.size(),
        "Batched retirement did not release each layer exactly once");
    // A diagnostic/reclaim barrier must also drain a deletion without a new frame.
    Check(target->BeginDraw() == JALIUM_OK, "Begin idle layer creation");
    void* idleLayer = target->RealizeLayerBegin(nullptr, 0, 0, 64, 64);
    Check(idleLayer != nullptr, "Create idle layer");
    target->RealizeLayerEnd(idleLayer);
    Check(target->EndDraw() == JALIUM_OK && target->WaitForCompletion() == JALIUM_OK, "Finish idle layer");
    target->DestroyRetainedLayer(idleLayer);
    Check(target->ReclaimIdleResources() == JALIUM_OK, "Idle retirement barrier");
    Check(target->DebugGetRetainedDestroyCounts(&orphaned, &graveyard) && orphaned == 0 && graveyard == layers.size() + 1,
        "Idle barrier left a retired layer behind");
    Check(target->BeginDraw() == JALIUM_OK, "Begin current-frame retirement");
    void* currentLayer = target->RealizeLayerBegin(nullptr, 0, 0, 64, 64);
    Check(currentLayer != nullptr, "Create current-frame layer");
    target->Clear(0, 1, 0, 1);
    target->RealizeLayerEnd(currentLayer);
    target->CompositeLayer(currentLayer, 0, 0, 64, 64, 1);
    target->DestroyRetainedLayer(currentLayer);
    Check(target->DebugGetRetainedDestroyCounts(&orphaned, &graveyard) && graveyard == layers.size() + 1,
        "An unsubmitted current-frame layer was released early");
    Check(target->EndDraw() == JALIUM_OK && target->WaitForCompletion() == JALIUM_OK, "Fence current-frame layer");
    Check(target->DebugGetRetainedDestroyCounts(&orphaned, &graveyard) && orphaned == 0 && graveyard == layers.size() + 2,
        "Current-frame retirement did not complete");
    std::puts("LAYER_RETIRE fenced=true idle_drained=true current_frame_drained=true");
}

void Benchmark(JaliumContext* context) {
    Surface surface(context, JALIUM_ENGINE_IMPELLER);
    std::vector<uint8_t> source(512 * 288 * 4, 180);
    for (size_t i = 3; i < source.size(); i += 4) source[i] = 255;
    auto create = [&] {
        Bitmap bitmap(jalium_bitmap_create_from_pixels(context, source.data(), 512, 288, 512 * 4), jalium_bitmap_destroy);
        Check(bitmap != nullptr, "Benchmark bitmap creation failed");
        return bitmap;
    };
    for (bool cold : {false, true}) {
        std::vector<Bitmap> cached;
        for (int i = 0; i < 3; ++i) cached.push_back(create());
        std::vector<double> cpu, frame;
        for (int n = 0; n < 28; ++n) {
            const auto start = Clock::now();
            std::vector<Bitmap> fresh;
            if (cold) for (int i = 0; i < 3; ++i) fresh.push_back(create());
            auto& images = cold ? fresh : cached;
            surface.Begin();
            for (int i = 0; i < 3; ++i)
                jalium_draw_bitmap_ex(surface.target.get(), images[i].get(), 8 + i * 80, 8, 72, 72, 1, 2);
            const auto encodeMs = Milliseconds(start);
            surface.End();
            if (n >= 4) { cpu.push_back(encodeMs); frame.push_back(Milliseconds(start)); }
        }
        auto report = [](std::vector<double> values, double percentile) {
            std::sort(values.begin(), values.end());
            return values[static_cast<size_t>(std::ceil(values.size() * percentile)) - 1];
        };
        std::printf("BITMAP_UPLOAD mode=%s images_per_frame=3 source=512x288 frames=%zu cpu_p50_ms=%.3f cpu_p95_ms=%.3f frame_p50_ms=%.3f frame_p95_ms=%.3f\n",
            cold ? "cold" : "cached", cpu.size(), report(cpu, 0.5), report(cpu, 0.95), report(frame, 0.5), report(frame, 0.95));
    }
}
}

int main() {
    @autoreleasepool {
        try {
            [NSApplication sharedApplication];
            jalium_metal_init();
            std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context(
                jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy);
            Check(context != nullptr, "Metal context creation failed");
            for (auto engine : {JALIUM_ENGINE_IMPELLER, JALIUM_ENGINE_VELLO}) {
                TestMipmaps(context.get(), engine);
                TestAllAlphaPairs(context.get(), engine);
            }
            Benchmark(context.get());
            TestBusyFrameAcquisition();
            TestBatchedRetainedRetirement();
            std::puts("PASS bitmap upload: mipmaps, alpha, padded rows, narrow/odd sizes, open encoder, repeated and shared-target draws");
            return 0;
        } catch (const std::exception& error) {
            std::fprintf(stderr, "FAIL bitmap upload: %s\n", error.what()); return 1;
        }
    }
}
