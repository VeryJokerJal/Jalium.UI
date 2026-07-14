#include "jalium_api.h"
#include "jalium_platform.h"

#include <X11/Xlib.h>
#include <X11/Xutil.h>

#include <chrono>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <string>
#include <thread>

using namespace std::chrono_literals;

extern "C" void jalium_software_init();

namespace {

int g_failures = 0;

void Check(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << "FAILED: " << message << '\n';
        ++g_failures;
    }
}

int CountAttachedSysVSegments()
{
    std::ifstream maps("/proc/self/maps");
    int count = 0;
    std::string line;
    while (std::getline(maps, line))
        if (line.find("/SYSV") != std::string::npos) ++count;
    return count;
}

bool WaitUntilViewable(Display* display, ::Window window)
{
    for (int attempt = 0; attempt < 200; ++attempt)
    {
        (void)jalium_platform_poll_events();
        XWindowAttributes attributes{};
        if (XGetWindowAttributes(display, window, &attributes) &&
            attributes.map_state == IsViewable)
            return true;
        std::this_thread::sleep_for(2ms);
    }
    return false;
}

bool WaitForSize(Display* display, ::Window window, int width, int height)
{
    for (int attempt = 0; attempt < 200; ++attempt)
    {
        (void)jalium_platform_poll_events();
        XWindowAttributes attributes{};
        if (XGetWindowAttributes(display, window, &attributes) &&
            attributes.width == width && attributes.height == height)
            return true;
        std::this_thread::sleep_for(2ms);
    }
    return false;
}

struct PixelSample
{
    unsigned long pixel = 0;
    unsigned long redMask = 0;
    unsigned long greenMask = 0;
    unsigned long blueMask = 0;
    int depth = 0;
    bool valid = false;
};

PixelSample ReadPixel(Display* display, ::Window window, int x, int y)
{
    PixelSample sample;
    XWindowAttributes attributes{};
    if (!XGetWindowAttributes(display, window, &attributes)) return sample;
    XImage* image = XGetImage(display, window, x, y, 1, 1, AllPlanes, ZPixmap);
    if (!image) return sample;
    sample.pixel = XGetPixel(image, 0, 0);
    sample.redMask = image->red_mask;
    sample.greenMask = image->green_mask;
    sample.blueMask = image->blue_mask;
    sample.depth = attributes.depth;
    sample.valid = true;
    XDestroyImage(image);
    return sample;
}

uint8_t ExtractChannel(unsigned long pixel, unsigned long mask)
{
    if (!mask) return 0;
    unsigned int shift = 0;
    while (((mask >> shift) & 1ul) == 0ul) ++shift;
    const unsigned long maximum = mask >> shift;
    const unsigned long value = (pixel & mask) >> shift;
    return static_cast<uint8_t>((value * 255ul + maximum / 2ul) / maximum);
}

bool Near(uint8_t actual, uint8_t expected, uint8_t tolerance = 3)
{
    return std::abs(static_cast<int>(actual) - static_cast<int>(expected)) <= tolerance;
}

bool IsRgb(const PixelSample& sample, uint8_t red, uint8_t green, uint8_t blue)
{
    return sample.valid &&
        Near(ExtractChannel(sample.pixel, sample.redMask), red) &&
        Near(ExtractChannel(sample.pixel, sample.greenMask), green) &&
        Near(ExtractChannel(sample.pixel, sample.blueMask), blue);
}

bool PresentClear(JaliumRenderTarget* target,
                  float red, float green, float blue, float alpha)
{
    if (jalium_render_target_begin_draw(target) != JALIUM_OK) return false;
    jalium_render_target_clear(target, red, green, blue, alpha);
    return jalium_render_target_end_draw(target) == JALIUM_OK;
}

JaliumPlatformWindow* CreateWindow(uint32_t style, int width, int height)
{
    const char16_t title[] = u"Jalium X11 software present smoke";
    JaliumWindowParams parameters{};
    parameters.title = reinterpret_cast<const JaliumUtf16Char*>(title);
    parameters.width = width;
    parameters.height = height;
    parameters.style = style;
    JaliumPlatformWindow* window = jalium_window_create(&parameters);
    if (window) jalium_window_show(window);
    return window;
}

void TestOpaqueAndIncrementalPresent(JaliumContext* context)
{
    JaliumPlatformWindow* window = CreateWindow(
        JALIUM_WINDOW_STYLE_RESIZABLE, 160, 120);
    Check(window != nullptr, "create opaque X11 window");
    if (!window) return;

    const JaliumSurfaceDescriptor surface = jalium_window_get_surface(window);
    auto* display = reinterpret_cast<Display*>(surface.handle0);
    const ::Window xwindow = static_cast<::Window>(surface.handle1);
    Check(surface.platform == JALIUM_PLATFORM_LINUX_X11 && display && xwindow,
          "opaque window exposes X11 display/window handles");
    Check(WaitUntilViewable(display, xwindow), "opaque X11 window becomes viewable");

    XWindowAttributes attributes{};
    Check(XGetWindowAttributes(display, xwindow, &attributes) != 0 &&
              attributes.visual != nullptr && attributes.depth > 0,
          "opaque window has a real Visual and depth");

    JaliumRenderTarget* target = jalium_render_target_create_for_surface(
        context, &surface, 160, 120);
    Check(target != nullptr, "create opaque software render target");
    if (!target)
    {
        jalium_window_destroy(window);
        return;
    }

    jalium_render_target_set_full_invalidation(target);
    Check(PresentClear(target, 1.0f, 0.0f, 0.0f, 1.0f),
          "present initial full red frame");
    Check(IsRgb(ReadPixel(display, xwindow, 5, 5), 255, 0, 0) &&
              IsRgb(ReadPixel(display, xwindow, 100, 80), 255, 0, 0),
          "XGetImage observes initial full-frame pixels");

    // Two separate dirty rectangles are merged into one clipped bounding box.
    // Clearing the CPU framebuffer to green makes the uploaded extent directly
    // observable: the gap inside the merged box changes, pixels outside do not.
    jalium_render_target_add_dirty_rect(target, 10, 10, 10, 10);
    jalium_render_target_add_dirty_rect(target, 40, 40, 10, 10);
    Check(PresentClear(target, 0.0f, 1.0f, 0.0f, 1.0f),
          "present merged incremental green region");
    Check(IsRgb(ReadPixel(display, xwindow, 12, 12), 0, 255, 0) &&
              IsRgb(ReadPixel(display, xwindow, 30, 30), 0, 255, 0) &&
              IsRgb(ReadPixel(display, xwindow, 48, 48), 0, 255, 0) &&
              IsRgb(ReadPixel(display, xwindow, 5, 5), 255, 0, 0) &&
              IsRgb(ReadPixel(display, xwindow, 80, 80), 255, 0, 0),
          "incremental upload changes only the merged dirty bounds");

    jalium_render_target_add_dirty_rect(target, -20, -20, 30, 30);
    Check(PresentClear(target, 0.0f, 0.0f, 1.0f, 1.0f),
          "present clipped negative-origin dirty rectangle");
    Check(IsRgb(ReadPixel(display, xwindow, 5, 5), 0, 0, 255) &&
              IsRgb(ReadPixel(display, xwindow, 12, 12), 0, 255, 0),
          "dirty rectangle is clipped to the framebuffer bounds");

    jalium_render_target_set_full_invalidation(target);
    Check(PresentClear(target, 0.0f, 0.0f, 1.0f, 1.0f) &&
              IsRgb(ReadPixel(display, xwindow, 120, 90), 0, 0, 255),
          "SetFullInvalidation restores a full upload");

    Check(PresentClear(target, 1.0f, 0.0f, 1.0f, 1.0f) &&
              IsRgb(ReadPixel(display, xwindow, 120, 90), 0, 0, 255),
          "a frame with no invalidation does not upload CPU framebuffer changes");

    jalium_render_target_set_dpi(target, 192.0f, 192.0f);
    jalium_render_target_add_dirty_rect(target, 10, 10, 10, 10);
    Check(PresentClear(target, 1.0f, 0.0f, 1.0f, 1.0f),
          "present a DPI-scaled dirty rectangle");
    Check(IsRgb(ReadPixel(display, xwindow, 25, 25), 255, 0, 255) &&
              IsRgb(ReadPixel(display, xwindow, 15, 15), 0, 0, 255) &&
              IsRgb(ReadPixel(display, xwindow, 45, 45), 0, 0, 255),
          "dirty DIPs are converted to clipped physical-pixel bounds");
    jalium_render_target_set_dpi(target, 96.0f, 96.0f);

    jalium_window_resize(window, 220, 140);
    const bool windowResized = WaitForSize(display, xwindow, 220, 140);
    Check(windowResized, "X11 window reaches its requested resize before present");
    Check(windowResized &&
              jalium_render_target_resize(target, 220, 140) == JALIUM_OK &&
              PresentClear(target, 1.0f, 1.0f, 0.0f, 1.0f),
          "resize recreates correctly-strided presentation storage");
    Check(windowResized &&
              IsRgb(ReadPixel(display, xwindow, 200, 130), 255, 255, 0),
          "resized edge pixels are presented from the new stride");

    jalium_render_target_destroy(target);
    jalium_window_destroy(window);
}

void TestArgbVisual(JaliumContext* context)
{
    JaliumPlatformWindow* window = CreateWindow(
        JALIUM_WINDOW_STYLE_BORDERLESS | JALIUM_WINDOW_STYLE_TRANSPARENT,
        96, 72);
    Check(window != nullptr, "create transparent X11 window");
    if (!window) return;

    const JaliumSurfaceDescriptor surface = jalium_window_get_surface(window);
    auto* display = reinterpret_cast<Display*>(surface.handle0);
    const ::Window xwindow = static_cast<::Window>(surface.handle1);
    Check(WaitUntilViewable(display, xwindow), "transparent X11 window becomes viewable");
    XWindowAttributes attributes{};
    Check(XGetWindowAttributes(display, xwindow, &attributes) != 0 &&
              attributes.visual != nullptr && attributes.depth == 32,
          "transparent window selects its real 32-bit ARGB Visual");

    JaliumRenderTarget* target = jalium_render_target_create_for_surface(
        context, &surface, 96, 72);
    Check(target != nullptr, "create ARGB software render target");
    if (target)
    {
        jalium_render_target_set_full_invalidation(target);
        Check(PresentClear(target, 0.25f, 0.5f, 0.75f, 0.5f),
              "present translucent ARGB frame");
        const PixelSample sample = ReadPixel(display, xwindow, 20, 20);
        const unsigned long storageMask = sample.depth == 32
            ? 0xfffffffful
            : ((1ul << sample.depth) - 1ul);
        const unsigned long alphaMask = storageMask &
            ~(sample.redMask | sample.greenMask | sample.blueMask);
        Check(sample.valid && alphaMask != 0 &&
                  Near(ExtractChannel(sample.pixel, alphaMask), 128) &&
                  Near(ExtractChannel(sample.pixel, sample.redMask), 32) &&
                  Near(ExtractChannel(sample.pixel, sample.greenMask), 64) &&
                  Near(ExtractChannel(sample.pixel, sample.blueMask), 96),
              "ARGB pixels preserve alpha and premultiply RGB channels");
        jalium_render_target_destroy(target);
    }
    jalium_window_destroy(window);
}

} // namespace

int main()
{
    jalium_software_init();
    if (jalium_platform_init() != JALIUM_OK ||
        jalium_platform_get_current() != JALIUM_PLATFORM_LINUX_X11)
    {
        std::cerr << "FAILED: initialize X11 platform\n";
        return 1;
    }

    const int initialSysVSegments = CountAttachedSysVSegments();
    JaliumContext* context = jalium_context_create(JALIUM_BACKEND_SOFTWARE);
    Check(context != nullptr, "create software context");
    if (context)
    {
        TestOpaqueAndIncrementalPresent(context);
        TestArgbVisual(context);
        jalium_context_destroy(context);
    }
    Check(CountAttachedSysVSegments() == initialSysVSegments,
          "render-target destruction releases every attached SysV segment");
    jalium_platform_shutdown();

    if (g_failures == 0)
        std::cout << "X11 software present pixel smoke passed.\n";
    return g_failures == 0 ? 0 : 1;
}
