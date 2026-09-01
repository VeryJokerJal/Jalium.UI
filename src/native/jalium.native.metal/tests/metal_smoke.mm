#include "metal_backend.h"

#import <AppKit/AppKit.h>

#include <cstdint>
#include <cstdio>
#include <memory>
#include <vector>

int main()
{
    @autoreleasepool {
        [NSApplication sharedApplication];
        NSWindow* window = [[NSWindow alloc]
            initWithContentRect:NSMakeRect(0, 0, 128, 96)
            styleMask:NSWindowStyleMaskBorderless
            backing:NSBackingStoreBuffered defer:NO];
        [window orderFront:nil];

        std::unique_ptr<jalium::IRenderBackend> backend(jalium::CreateMetalBackend());
        if (!backend) return 10;
        std::unique_ptr<jalium::RenderTarget> target(
            backend->CreateRenderTarget((__bridge void*)window, 128, 96));
        std::unique_ptr<jalium::Brush> red(
            backend->CreateSolidBrush(1.0f, 0.0f, 0.0f, 1.0f));
        if (!target || !red || backend->CheckDeviceStatus() != JALIUM_OK) return 11;

        if (target->BeginDraw() != JALIUM_OK) return 12;
        target->Clear(0, 0, 0, 1);
        target->FillRectangle(24, 18, 80, 60, red.get());
        if (target->RequestReadback() != JALIUM_OK) return 13;
        if (target->EndDraw() != JALIUM_OK) return 14;

        int32_t width = 0, height = 0;
        if (target->FetchReadback(nullptr, 0, &width, &height) != JALIUM_OK ||
            width != 128 || height != 96) return 15;
        std::vector<uint8_t> pixels(static_cast<size_t>(width) * height * 4);
        if (target->FetchReadback(pixels.data(), width * 4, &width, &height) != JALIUM_OK)
            return 16;
        const size_t center = (static_cast<size_t>(48) * width + 64) * 4;
        const uint8_t b = pixels[center], g = pixels[center + 1];
        const uint8_t r = pixels[center + 2], a = pixels[center + 3];
        if (r < 245 || g > 8 || b > 8 || a < 245) {
            std::fprintf(stderr, "unexpected center BGRA=(%u,%u,%u,%u)\n", b,g,r,a);
            return 17;
        }
        [window orderOut:nil];
        return 0;
    }
}
