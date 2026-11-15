#include "jalium_api.h"

#import <AppKit/AppKit.h>
#import <QuartzCore/CAMetalLayer.h>

#include <cmath>
#include <cstdio>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <vector>

extern "C" void jalium_metal_init();

@interface JaliumResizeTestView : NSView
@end
@implementation JaliumResizeTestView
- (BOOL)isFlipped { return YES; }
@end

@interface JaliumResizeTestDelegate : NSObject <NSWindowDelegate>
@end
@implementation JaliumResizeTestDelegate
- (void)windowWillClose:(NSNotification*)notification { [NSApp stop:nil]; }
@end

static void Check(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

struct Surface {
    std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context{
        jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy};
    std::unique_ptr<JaliumRenderTarget, decltype(&jalium_render_target_destroy)> target{
        nullptr, jalium_render_target_destroy};
    NSView* view;
    explicit Surface(NSView* host, float scale) : view(host)
    {
        Check(context != nullptr, "Metal context creation failed");
        JaliumSurfaceDescriptor descriptor{};
        descriptor.platform = JALIUM_PLATFORM_MACOS;
        descriptor.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
        target.reset(jalium_render_target_create_for_surface(context.get(), &descriptor,
            std::lround(view.bounds.size.width * scale), std::lround(view.bounds.size.height * scale)));
        Check(target != nullptr, "Metal target creation failed");
        jalium_render_target_set_dpi(target.get(), 96 * scale, 96 * scale);
    }

    void Draw()
    {
        std::unique_ptr<JaliumBrush, decltype(&jalium_brush_destroy)> blue{
            jalium_brush_create_solid(context.get(), 0.05f, 0.35f, 0.85f, 1), jalium_brush_destroy};
        Check(blue != nullptr, "Resize marker brush creation failed");
        Check(jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "Begin resize frame failed");
        jalium_render_target_clear(target.get(), 0.97f, 0.98f, 1, 1);
        jalium_draw_fill_rectangle(target.get(), 32, 32, 96, 96, blue.get());
        jalium_draw_fill_rectangle(target.get(), 32, 160, 256, 4, blue.get());
        Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK, "Request resize readback failed");
        Check(jalium_render_target_end_draw(target.get()) == JALIUM_OK, "End resize frame failed");
    }

    void CheckMarker(int width, int height, float scale)
    {
        std::vector<uint8_t> pixels(static_cast<size_t>(width) * height * 4);
        int actualWidth = 0, actualHeight = 0;
        Check(jalium_render_target_fetch_readback(target.get(), pixels.data(), width * 4,
            &actualWidth, &actualHeight) == JALIUM_OK, "Fetch resize readback failed");
        Check(actualWidth == width && actualHeight == height, "Resized framebuffer has stale dimensions");
        for (int y = 0; y < height; ++y) {
            for (int x = 0; x < width; ++x) {
                const auto* pixel = pixels.data() + (static_cast<size_t>(y) * width + x) * 4;
                const bool blue = pixel[0] > 180 && pixel[2] < 150;
                const bool square = x >= 32 * scale && x < 128 * scale &&
                    y >= 32 * scale && y < 128 * scale;
                const bool ruler = x >= 32 * scale && x < 288 * scale &&
                    y >= 160 * scale && y < 164 * scale;
                if (blue != (square || ruler)) {
                    std::fprintf(stderr, "Marker mismatch: scale=%.0f size=%dx%d at=%d,%d BGRA=%u,%u,%u,%u expected=%d\n",
                        scale, width, height, x, y, pixel[0], pixel[1], pixel[2], pixel[3], square || ruler);
                    throw std::runtime_error("Resize changed the marker's scale or position");
                }
            }
        }
    }
};

static void CheckPendingFrame(NSView* view, float scale)
{
    CAMetalLayer* layer = (CAMetalLayer*)view.layer;
    Check([layer.contentsGravity isEqualToString:view.isFlipped ? kCAGravityBottomLeft : kCAGravityTopLeft],
        "Pending Metal frame is stretched or moved when the host view resizes");
    Check(view.layerContentsPlacement == NSViewLayerContentsPlacementTopLeft,
        "AppKit can restore scaled contents placement on resize");
    Check(std::abs(layer.contentsScale - scale) < 0.001,
        "Pending Metal frame uses the wrong backing pixel scale");
}

static void TestResize(float scale, bool flipped)
{
    NSView* view = flipped
        ? [[JaliumResizeTestView alloc] initWithFrame:NSMakeRect(0, 0, 640, 420)]
        : [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 640, 420)];
    Surface surface(view, scale);
    CheckPendingFrame(view, scale);
    surface.Draw();
    surface.CheckMarker(std::lround(640 * scale), std::lround(420 * scale), scale);
    for (NSValue* value in @[[NSValue valueWithSize:NSMakeSize(960, 540)],
                            [NSValue valueWithSize:NSMakeSize(420, 640)],
                            [NSValue valueWithSize:NSMakeSize(640, 420)]]) {
        NSSize size = value.sizeValue;
        // Leave the old drawable presented while AppKit changes the viewport.
        // This interval must not scale or recenter its text/controls.
        view.frame = NSMakeRect(0, 0, size.width, size.height);
        [view layoutSubtreeIfNeeded];
        CheckPendingFrame(view, scale);
        const int width = std::lround(size.width * scale), height = std::lround(size.height * scale);
        Check(jalium_render_target_resize(surface.target.get(), width, height) == JALIUM_OK,
            "Resize failed");
        CheckPendingFrame(view, scale);
        surface.Draw();
        surface.CheckMarker(width, height, scale);
    }
    std::printf("Resize presentation and marker pixels passed at %.0fx (flipped=%d)\n", scale, flipped);
}

int main(int argc, char** argv)
{
    @autoreleasepool {
        try {
            [NSApplication sharedApplication];
            jalium_metal_init();
            if (argc == 2 && std::strcmp(argv[1], "--preview") == 0) {
                [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];
                NSWindow* window = [[NSWindow alloc] initWithContentRect:NSMakeRect(180, 180, 640, 420)
                    styleMask:NSWindowStyleMaskTitled | NSWindowStyleMaskClosable | NSWindowStyleMaskResizable
                    backing:NSBackingStoreBuffered defer:NO];
                window.releasedWhenClosed = NO;
                window.title = @"Metal resize: fixed 96 × 96 square";
                JaliumResizeTestDelegate* delegate = [JaliumResizeTestDelegate new];
                window.delegate = delegate;
                window.contentView = [[JaliumResizeTestView alloc] initWithFrame:NSMakeRect(0, 0, 640, 420)];
                Surface surface(window.contentView, window.backingScaleFactor);
                [window makeKeyAndOrderFront:nil];
                surface.Draw();
                // No frames follow: resize the window to inspect the compositor's
                // treatment of a pending old frame, independently of layout speed.
                [NSApp run];
                return 0;
            }
            [NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited];
            TestResize(1, false);
            TestResize(1, true);
            TestResize(2, false);
            TestResize(2, true);
            return 0;
        } catch (const std::exception& error) {
            std::fprintf(stderr, "%s\n", error.what());
            return 1;
        }
    }
}
