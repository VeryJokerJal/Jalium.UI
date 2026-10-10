#include "jalium_api.h"
#include "jalium_internal.h"
#include "metal_backend.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <thread>
#include <vector>

#import <AppKit/AppKit.h>
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>

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

static void TestNativeGPU(uint32_t samples)
{
    id<MTLDevice> device = MTLCreateSystemDefaultDevice();
    Check(device != nil, "Direct Metal device creation");
    std::printf("Direct GPU: %s; unified=%d; samples=%u\n", device.name.UTF8String,
        device.hasUnifiedMemory, samples);
    Check([device supportsTextureSampleCount:samples], "Direct Metal sample-count support");
    id<MTLCommandQueue> queue = [device newCommandQueue];
    MTLCommandBufferDescriptor* descriptor = [MTLCommandBufferDescriptor new];
    descriptor.errorOptions = MTLCommandBufferErrorOptionEncoderExecutionStatus;
    id<MTLCommandBuffer> command = [queue commandBufferWithDescriptor:descriptor];
    MTLTextureDescriptor* textureDescriptor = [MTLTextureDescriptor
        texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm width:7 height:5 mipmapped:NO];
    textureDescriptor.storageMode = MTLStorageModePrivate;
    textureDescriptor.usage = MTLTextureUsageRenderTarget;
    id<MTLTexture> texture = [device newTextureWithDescriptor:textureDescriptor];
    id<MTLTexture> multisample = nil, stencil = nil;
    if (samples > 1) {
        MTLTextureDescriptor* attachment = [MTLTextureDescriptor new];
        attachment.textureType = MTLTextureType2DMultisample;
        attachment.pixelFormat = MTLPixelFormatBGRA8Unorm;
        attachment.width = 7; attachment.height = 5; attachment.sampleCount = samples;
        attachment.storageMode = MTLStorageModePrivate;
        attachment.usage = MTLTextureUsageRenderTarget;
        multisample = [device newTextureWithDescriptor:attachment];
        attachment.pixelFormat = MTLPixelFormatStencil8;
        stencil = [device newTextureWithDescriptor:attachment];
        Check(multisample && stencil, "Direct Metal MSAA and stencil creation");
    }
    id<MTLBuffer> pixels = [device newBufferWithLength:5 * 256 options:MTLResourceStorageModeShared];
    id<MTLSharedEvent> event = [device newSharedEvent];
    Check(command && texture && pixels && event, "Direct Metal resource creation");
    MTLRenderPassDescriptor* pass = [MTLRenderPassDescriptor renderPassDescriptor];
    pass.colorAttachments[0].texture = samples > 1 ? multisample : texture;
    pass.colorAttachments[0].resolveTexture = samples > 1 ? texture : nil;
    pass.colorAttachments[0].loadAction = MTLLoadActionClear;
    pass.colorAttachments[0].storeAction = samples > 1
        ? MTLStoreActionStoreAndMultisampleResolve : MTLStoreActionStore;
    pass.colorAttachments[0].clearColor = MTLClearColorMake(1, 0, 0, 1);
    if (stencil) {
        pass.stencilAttachment.texture = stencil;
        pass.stencilAttachment.loadAction = MTLLoadActionClear;
        pass.stencilAttachment.storeAction = MTLStoreActionDontCare;
        pass.stencilAttachment.clearStencil = 0;
    }
    id<MTLRenderCommandEncoder> clear = [command renderCommandEncoderWithDescriptor:pass];
    clear.label = @"Direct Metal clear";
    [clear endEncoding];
    id<MTLBlitCommandEncoder> copy = [command blitCommandEncoder];
    copy.label = @"Direct Metal readback";
    [copy copyFromTexture:texture sourceSlice:0 sourceLevel:0 sourceOrigin:MTLOriginMake(0, 0, 0)
        sourceSize:MTLSizeMake(7, 5, 1) toBuffer:pixels destinationOffset:0
        destinationBytesPerRow:256 destinationBytesPerImage:5 * 256];
    [copy endEncoding];
    [command encodeSignalEvent:event value:1];
    [command commit];
    [command waitUntilCompleted];
    if (command.status == MTLCommandBufferStatusError) {
        std::fprintf(stderr, "Direct Metal command failed: domain=%s code=%ld description=%s\n",
            command.error.domain.UTF8String, static_cast<long>(command.error.code),
            command.error.localizedDescription.UTF8String);
        for (id<MTLCommandBufferEncoderInfo> info in
             command.error.userInfo[MTLCommandBufferEncoderInfoErrorKey])
            std::fprintf(stderr, "Direct Metal encoder: label=%s state=%ld\n",
                info.label.UTF8String, static_cast<long>(info.errorState));
    }
    Check(command.status == MTLCommandBufferStatusCompleted, "Direct Metal GPU execution");
    Check(event.signaledValue == 1, "Direct Metal GPU shared-event signal");
    const uint8_t* bytes = static_cast<const uint8_t*>(pixels.contents);
    for (int y = 0; y < 5; ++y) for (int x = 0; x < 7; ++x) {
        const uint8_t* p = bytes + y * 256 + x * 4;
        Check(p[0] == 0 && p[1] == 0 && p[2] == 255 && p[3] == 255,
            "Direct Metal clear/copy BGRA pixels");
    }
    std::printf("PASS direct Metal clear, BGRA readback and shared-event signal; samples=%u\n", samples);
}

@interface ReadbackMetalLayer : CAMetalLayer
@property(nonatomic) NSUInteger drawableRequests;
@property(nonatomic) BOOL suppressDrawables;
@end

@implementation ReadbackMetalLayer
- (id<CAMetalDrawable>)nextDrawable
{
    self.drawableRequests++;
    return self.suppressDrawables ? nil : [super nextDrawable];
}
@end

struct Surface {
    std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context{
        jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy};
    NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 7, 5)];
    ReadbackMetalLayer* layer = [ReadbackMetalLayer layer];
    std::unique_ptr<JaliumRenderTarget, decltype(&jalium_render_target_destroy)> target{
        nullptr, jalium_render_target_destroy};
    id<MTLCommandQueue> queue;
    id<MTLDevice> device;

    Surface()
    {
        Check(context != nullptr, "Metal context creation");
        view.wantsLayer = YES;
        view.layer = layer;
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
    Check(surface.layer.drawableRequests == 0,
        "A detached view must not acquire an onscreen drawable for readback");
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

static void TestWindowAttachment(Surface& surface)
{
    NSWindow* window = [[NSWindow alloc] initWithContentRect:NSMakeRect(0, 0, 7, 5)
        styleMask:NSWindowStyleMaskBorderless backing:NSBackingStoreBuffered defer:NO];
    window.releasedWhenClosed = NO;
    // This test observes drawable acquisition without depending on WindowServer
    // presentation. Actual attached-window rendering is checked by the UI host.
    surface.layer.suppressDrawables = YES;
    NSUInteger expectedRequests = 0;
    for (int iteration = 0; iteration < 3; ++iteration) {
        window.contentView = surface.view;
        Check(surface.view.window == window, "Render view attachment");
        surface.Capture();
        Check(surface.layer.drawableRequests == ++expectedRequests,
            "An attached view must keep the presentation path");
        std::vector<uint8_t> attachedPixels(5 * 40, 0xa5);
        int32_t width = -1, height = -1;
        CheckResult(surface.Fetch(attachedPixels.data(), 40, width, height), JALIUM_OK,
            "Attached view readback when no drawable is available");
        Check(width == 7 && height == 5, "Attached capture dimensions");
        CheckPixels(attachedPixels, 40);

        window.contentView = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 7, 5)];
        Check(surface.view.window == nil, "Render view detachment");
        surface.Capture();
        Check(surface.layer.drawableRequests == expectedRequests,
            "A detached view must stop requesting onscreen drawables");
        std::vector<uint8_t> detachedPixels(5 * 40, 0xa5);
        CheckResult(surface.Fetch(detachedPixels.data(), 40, width, height), JALIUM_OK,
            "Readback after view detachment");
        Check(width == 7 && height == 5, "Detached capture dimensions");
        CheckPixels(detachedPixels, 40);
    }
    [window close];
    surface.layer.suppressDrawables = NO;
    std::puts("PASS repeated view attachment/detachment preserves presentation selection and BGRA readback");
}

int main(int argc, char** argv)
{
    @autoreleasepool {
        try {
            if (argc == 2 && std::strcmp(argv[1], "--gpu-baseline") == 0) {
                TestNativeGPU(1);
                return 0;
            }
            if (argc == 2 && std::strcmp(argv[1], "--gpu-msaa-baseline") == 0) {
                TestNativeGPU(4);
                return 0;
            }
            Check(argc == 1, "Unknown readback test option");
            [NSApplication sharedApplication];
            jalium_metal_init();
            Surface surface;
            TestLifecycle(surface);
            TestConcurrentFetch(surface);
            TestWindowAttachment(surface);
            return 0;
        } catch (const std::exception& error) {
            std::fprintf(stderr, "FAIL Metal readback: %s\n", error.what());
            return 1;
        }
    }
}
