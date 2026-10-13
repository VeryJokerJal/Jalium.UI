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
#include <utility>
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

enum class GPUProfile {
    Plain, SceneUsage, ClipUsage, SampledClip, AppKit, Layer, Resize,
    ClipOnly, ScissorOnly, DepthOnly, NilDepthRepro
};

static const char* ProfileName(GPUProfile profile)
{
    switch (profile) {
    case GPUProfile::SceneUsage: return "scene-usage";
    case GPUProfile::ClipUsage: return "clip-usage";
    case GPUProfile::SampledClip: return "sampled-clip";
    case GPUProfile::AppKit: return "appkit";
    case GPUProfile::Layer: return "windowless-layer";
    case GPUProfile::Resize: return "pending-resize";
    case GPUProfile::ClipOnly: return "clip-binding-only";
    case GPUProfile::ScissorOnly: return "scissor-only";
    case GPUProfile::DepthOnly: return "depth-state-only";
    case GPUProfile::NilDepthRepro: return "nil-depth-repro";
    default: return "plain";
    }
}

static void TestNativeGPU(uint32_t samples, GPUProfile profile = GPUProfile::Plain)
{
    if (profile == GPUProfile::AppKit || profile == GPUProfile::Layer)
        [NSApplication sharedApplication];
    id<MTLDevice> device = MTLCreateSystemDefaultDevice();
    Check(device != nil, "Direct Metal device creation");
    std::printf("Direct GPU: %s; unified=%d; samples=%u; profile=%s\n", device.name.UTF8String,
        device.hasUnifiedMemory, samples, ProfileName(profile));
    Check([device supportsTextureSampleCount:samples], "Direct Metal sample-count support");
    id<MTLCommandQueue> queue = [device newCommandQueue];
    NSView* view = nil;
    CAMetalLayer* layer = nil;
    if (profile == GPUProfile::Layer) {
        view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 7, 5)];
        view.wantsLayer = YES;
        layer = [CAMetalLayer layer];
        view.layer = layer;
        layer.device = device;
        layer.pixelFormat = MTLPixelFormatBGRA8Unorm;
        layer.framebufferOnly = YES;
        layer.opaque = YES;
        layer.maximumDrawableCount = 3;
        layer.allowsNextDrawableTimeout = YES;
        layer.presentsWithTransaction = NO;
        layer.displaySyncEnabled = YES;
        CGColorSpaceRef srgb = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        layer.colorspace = srgb;
        CGColorSpaceRelease(srgb);
        layer.drawableSize = CGSizeMake(7, 5);
        Check(view.window == nil, "Direct Metal layer must stay windowless");
    }
    MTLCommandBufferDescriptor* descriptor = [MTLCommandBufferDescriptor new];
    descriptor.errorOptions = MTLCommandBufferErrorOptionEncoderExecutionStatus;
    id<MTLCommandBuffer> command = [queue commandBufferWithDescriptor:descriptor];
    id<MTLSharedEvent> resizeGate = nil;
    if (profile == GPUProfile::Resize) {
        resizeGate = [device newSharedEvent];
        Check(resizeGate != nil, "Direct Metal resize gate creation");
        [command encodeWaitForEvent:resizeGate value:1];
    }
    MTLTextureDescriptor* textureDescriptor = [MTLTextureDescriptor
        texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm width:7 height:5 mipmapped:NO];
    textureDescriptor.storageMode = MTLStorageModePrivate;
    textureDescriptor.usage = MTLTextureUsageRenderTarget;
    const bool sceneUsage = profile == GPUProfile::SceneUsage ||
        profile == GPUProfile::ClipUsage || profile == GPUProfile::SampledClip ||
        profile == GPUProfile::Resize || profile == GPUProfile::ClipOnly ||
        profile == GPUProfile::ScissorOnly || profile == GPUProfile::DepthOnly ||
        profile == GPUProfile::NilDepthRepro;
    if (sceneUsage) textureDescriptor.usage |= MTLTextureUsageShaderRead | MTLTextureUsageShaderWrite;
    id<MTLTexture> texture = [device newTextureWithDescriptor:textureDescriptor];
    id<MTLTexture> clip = nil;
    if (profile == GPUProfile::ClipUsage || profile == GPUProfile::SampledClip ||
        profile == GPUProfile::Resize || profile == GPUProfile::ClipOnly) {
        MTLTextureDescriptor* mask = [MTLTextureDescriptor
            texture2DDescriptorWithPixelFormat:MTLPixelFormatR8Unorm width:1 height:1 mipmapped:NO];
        mask.storageMode = MTLStorageModeShared;
        mask.usage = profile == GPUProfile::SampledClip ? MTLTextureUsageShaderRead :
            MTLTextureUsageRenderTarget | MTLTextureUsageShaderRead | MTLTextureUsageShaderWrite;
        clip = [device newTextureWithDescriptor:mask];
        Check(clip != nil, "Direct Metal shared R8 clip creation");
        const uint8_t white = 255;
        [clip replaceRegion:MTLRegionMake2D(0, 0, 1, 1) mipmapLevel:0 withBytes:&white bytesPerRow:1];
    }
    std::printf("Direct resources: colorUsage=%lu; clipUsage=%lu\n",
        static_cast<unsigned long>(texture.usage), static_cast<unsigned long>(clip.usage));
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
    if (clip) {
        // Isolate the fragment binding used by the framework's clear encoder.
        // No shader or draw call participates in this clear/copy control.
        [clear setFragmentTexture:clip atIndex:30];
    }
    const bool combinedState = clip && profile != GPUProfile::ClipOnly;
    if (combinedState || profile == GPUProfile::ScissorOnly)
        [clear setScissorRect:MTLScissorRect{0, 0, 7, 5}];
    if (combinedState || profile == GPUProfile::DepthOnly) {
        id<MTLDepthStencilState> state = [device newDepthStencilStateWithDescriptor:
            [MTLDepthStencilDescriptor new]];
        Check(state != nil, "Direct Metal explicit default depth/stencil state");
        [clear setDepthStencilState:state];
    }
    // Opt-in reproducer for the previous implementation, kept out of the
    // baseline tests which exercise the explicit default used by the renderer.
    if (profile == GPUProfile::NilDepthRepro)
        [clear setDepthStencilState:nil];
    [clear endEncoding];
    id<MTLBlitCommandEncoder> copy = [command blitCommandEncoder];
    copy.label = @"Direct Metal readback";
    [copy copyFromTexture:texture sourceSlice:0 sourceLevel:0 sourceOrigin:MTLOriginMake(0, 0, 0)
        sourceSize:MTLSizeMake(7, 5, 1) toBuffer:pixels destinationOffset:0
        destinationBytesPerRow:256 destinationBytesPerImage:5 * 256];
    [copy endEncoding];
    [command encodeSignalEvent:event value:1];
    [command commit];
    if (resizeGate) {
        // Keep the submitted copy pending while replacing the surface's texture
        // references, as Resize does before FetchReadback waits on its capture.
        pass = nil; clear = nil; copy = nil;
        textureDescriptor.width = 9; textureDescriptor.height = 3;
        texture = [device newTextureWithDescriptor:textureDescriptor];
        MTLTextureDescriptor* attachment = [MTLTextureDescriptor new];
        attachment.textureType = MTLTextureType2DMultisample;
        attachment.width = 9; attachment.height = 3; attachment.sampleCount = samples;
        attachment.storageMode = MTLStorageModePrivate;
        attachment.usage = MTLTextureUsageRenderTarget;
        attachment.pixelFormat = MTLPixelFormatBGRA8Unorm;
        multisample = [device newTextureWithDescriptor:attachment];
        attachment.pixelFormat = MTLPixelFormatStencil8;
        stencil = [device newTextureWithDescriptor:attachment];
        const bool replaced = texture && multisample && stencil;
        resizeGate.signaledValue = 1;
        Check(replaced, "Direct Metal replacement textures");
    }
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
    std::printf("PASS direct Metal clear, BGRA readback and shared-event signal; samples=%u; profile=%s\n",
        samples, ProfileName(profile));
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

static void TestCaptureWithoutResize(Surface& surface)
{
    surface.Capture();
    std::vector<uint8_t> pixels(5 * 40, 0xa5);
    int32_t width = -1, height = -1;
    CheckResult(surface.Fetch(pixels.data(), 40, width, height), JALIUM_OK,
        "Fetch without resize");
    Check(width == 7 && height == 5, "Capture dimensions without resize");
    CheckPixels(pixels, 40);
    std::puts("PASS framework clear and BGRA readback without resizing the target");
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
            if (argc == 2) {
                for (const auto& option : std::array<std::pair<const char*, GPUProfile>, 10>{{
                    {"--gpu-scene-usage-baseline", GPUProfile::SceneUsage},
                    {"--gpu-clip-baseline", GPUProfile::ClipUsage},
                    {"--gpu-sampled-clip-baseline", GPUProfile::SampledClip},
                    {"--gpu-appkit-baseline", GPUProfile::AppKit},
                    {"--gpu-layer-baseline", GPUProfile::Layer},
                    {"--gpu-resize-baseline", GPUProfile::Resize},
                    {"--gpu-clip-only-baseline", GPUProfile::ClipOnly},
                    {"--gpu-scissor-only-baseline", GPUProfile::ScissorOnly},
                    {"--gpu-depth-only-baseline", GPUProfile::DepthOnly},
                    {"--gpu-nil-depth-repro", GPUProfile::NilDepthRepro}}}) {
                    if (std::strcmp(argv[1], option.first) == 0) {
                        TestNativeGPU(4, option.second);
                        return 0;
                    }
                }
            }
            if (argc == 2 && std::strcmp(argv[1], "--gpu-baseline") == 0) {
                TestNativeGPU(1);
                return 0;
            }
            if (argc == 2 && std::strcmp(argv[1], "--gpu-msaa-baseline") == 0) {
                TestNativeGPU(4);
                return 0;
            }
            const bool noResize = argc == 2 && std::strcmp(argv[1], "--framework-no-resize") == 0;
            Check(argc == 1 || noResize, "Unknown readback test option");
            [NSApplication sharedApplication];
            jalium_metal_init();
            Surface surface;
            if (noResize) {
                TestCaptureWithoutResize(surface);
                return 0;
            }
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
