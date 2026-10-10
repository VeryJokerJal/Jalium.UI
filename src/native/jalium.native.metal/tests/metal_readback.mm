#include "jalium_api.h"
#include "jalium_internal.h"
#include "metal_backend.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <thread>
#include <vector>

#import <AppKit/AppKit.h>
#import <Metal/Metal.h>

extern "C" void jalium_metal_init();

static void Check(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

static void CheckResult(JaliumResult actual, JaliumResult expected, const char* operation)
{
    if (actual != expected) {
        std::fprintf(stderr, "%s: result=%d expected=%d\n", operation, actual, expected);
        throw std::runtime_error(operation);
    }
}

struct Surface {
    std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context{
        jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy};
    NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 7, 5)];
    std::unique_ptr<JaliumRenderTarget, decltype(&jalium_render_target_destroy)> target{
        nullptr, jalium_render_target_destroy};
    id<MTLCommandQueue> queue;
    id<MTLDevice> device;

    Surface()
    {
        Check(context != nullptr, "Metal context creation");
        JaliumSurfaceDescriptor descriptor{};
        descriptor.platform = JALIUM_PLATFORM_MACOS;
        descriptor.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
        target.reset(jalium_render_target_create_for_surface(context.get(), &descriptor, 7, 5));
        Check(target != nullptr, "Metal readback target creation");
        auto* backend = static_cast<jalium::MetalBackend*>(
            jalium::GetBackendFromContext(context.get()));
        device = (__bridge id<MTLDevice>)backend->DeviceHandle();
        queue = (__bridge id<MTLCommandQueue>)backend->CommandQueueHandle();
        Check(device && queue, "Metal device and queue initialization");
        std::printf("GPU: %s\n", device.name.UTF8String);
    }

    void Capture()
    {
        CheckResult(jalium_render_target_request_readback(target.get()), JALIUM_OK, "RequestReadback");
        CheckResult(jalium_render_target_request_readback(target.get()), JALIUM_ERROR_INVALID_STATE,
            "Duplicate request");
        CheckResult(jalium_render_target_begin_draw(target.get()), JALIUM_OK, "BeginDraw");
        jalium_render_target_clear(target.get(), 1, 0, 0, 1);
        CheckResult(jalium_render_target_end_draw(target.get()), JALIUM_OK, "EndDraw");
    }

    JaliumResult Fetch(uint8_t* bytes, uint32_t stride, int32_t& width, int32_t& height)
    {
        return jalium_render_target_fetch_readback(target.get(), bytes, stride, &width, &height);
    }
};

static void CheckPixels(const std::vector<uint8_t>& bytes, int stride)
{
    for (int y = 0; y < 5; ++y) {
        for (int x = 0; x < 7; ++x) {
            const uint8_t* p = bytes.data() + y * stride + x * 4;
            Check(p[0] == 0 && p[1] == 0 && p[2] == 255 && p[3] == 255,
                "Readback must copy actual top-down BGRA pixels");
        }
        for (int x = 28; x < stride; ++x)
            Check(bytes[y * stride + x] == 0xa5, "Readback overwrote destination row padding");
    }
}

static void TestLifecycle(Surface& surface)
{
    int32_t width = -1, height = -1;
    CheckResult(surface.Fetch(nullptr, 0, width, height), JALIUM_ERROR_INVALID_STATE, "Empty size query");
    Check(width == 0 && height == 0, "Empty query dimensions");
    surface.Capture();
    for (int query = 0; query < 2; ++query) {
        CheckResult(surface.Fetch(nullptr, 0, width, height), JALIUM_OK, "Pending size query");
        Check(width == 7 && height == 5, "Captured physical dimensions");
    }
    std::vector<uint8_t> pixels(5 * 40, 0xa5);
    CheckResult(surface.Fetch(pixels.data(), 27, width, height), JALIUM_ERROR_INVALID_ARGUMENT,
        "Short stride preserves capture");
    Check(std::all_of(pixels.begin(), pixels.end(), [](auto b) { return b == 0xa5; }),
        "Rejected fetch wrote destination bytes");
    CheckResult(jalium_render_target_resize(surface.target.get(), 9, 3), JALIUM_OK, "Resize before fetch");
    CheckResult(surface.Fetch(pixels.data(), 40, width, height), JALIUM_OK, "Fetch after resize");
    Check(width == 7 && height == 5, "Resize changed the pending capture dimensions");
    CheckPixels(pixels, 40);
    CheckResult(surface.Fetch(nullptr, 0, width, height), JALIUM_ERROR_INVALID_STATE, "Consumed size query");
    Check(width == 0 && height == 0, "Consumed query dimensions");
    CheckResult(jalium_render_target_resize(surface.target.get(), 7, 5), JALIUM_OK, "Restore target size");
    std::puts("PASS size queries, short stride, padded BGRA rows, resize, one-shot consumption");
}

static void TestConcurrentFetch(Surface& surface)
{
    for (int iteration = 0; iteration < 8; ++iteration) {
        id<MTLSharedEvent> event = [surface.device newSharedEvent];
        id<MTLCommandBuffer> gate = [surface.queue commandBuffer];
        Check(event && gate, "GPU synchronization gate creation");
        [gate encodeWaitForEvent:event value:1];
        [gate commit];
        surface.Capture();

        std::array<std::vector<uint8_t>, 2> pixels{
            std::vector<uint8_t>(5 * 40, 0xa5), std::vector<uint8_t>(5 * 40, 0xa5)};
        std::array<JaliumResult, 2> results{};
        std::array<int32_t, 2> widths{-1, -1}, heights{-1, -1};
        std::atomic<int> ready{0};
        std::atomic<bool> start{false};
        std::array<std::thread, 2> readers;
        for (int i = 0; i < 2; ++i) {
            readers[i] = std::thread([&, i] {
                @autoreleasepool {
                    ready.fetch_add(1, std::memory_order_release);
                    while (!start.load(std::memory_order_acquire)) std::this_thread::yield();
                    results[i] = surface.Fetch(pixels[i].data(), 40, widths[i], heights[i]);
                }
            });
        }
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
        while (ready.load(std::memory_order_acquire) != 2 &&
               std::chrono::steady_clock::now() < deadline) std::this_thread::yield();
        const bool bothReady = ready.load(std::memory_order_acquire) == 2;
        start.store(true, std::memory_order_release);
        // Keep the real GPU copy pending while both CPU callers enter the fence wait.
        std::this_thread::sleep_for(std::chrono::milliseconds(75));
        event.signaledValue = 1;
        for (auto& reader : readers) reader.join();
        Check(bothReady, "Readback threads failed to start");
        std::printf("Concurrent capture %d: results=%d/%d sizes=%dx%d/%dx%d\n",
            iteration, results[0], results[1], widths[0], heights[0], widths[1], heights[1]);
        const int winner = results[0] == JALIUM_OK ? 0 : 1;
        const int loser = 1 - winner;
        CheckResult(results[winner], JALIUM_OK, "Concurrent winning fetch");
        CheckResult(results[loser], JALIUM_ERROR_INVALID_STATE, "Concurrent consumed fetch");
        Check(widths[winner] == 7 && heights[winner] == 5, "Winning fetch dimensions");
        Check(widths[loser] == 0 && heights[loser] == 0, "Consumed concurrent fetch dimensions");
        CheckPixels(pixels[winner], 40);
        Check(std::all_of(pixels[loser].begin(), pixels[loser].end(),
            [](auto b) { return b == 0xa5; }), "Consumed concurrent fetch wrote destination");
    }
    std::puts("PASS two concurrent callers consume each GPU capture once; subsequent captures work");
}

int main()
{
    @autoreleasepool {
        try {
            [NSApplication sharedApplication];
            jalium_metal_init();
            Surface surface;
            TestLifecycle(surface);
            TestConcurrentFetch(surface);
            return 0;
        } catch (const std::exception& error) {
            std::fprintf(stderr, "FAIL Metal readback: %s\n", error.what());
            return 1;
        }
    }
}
