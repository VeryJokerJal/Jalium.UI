#include "jalium_api.h"
#include "jalium_platform.h"
#include "jalium_media.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

#import <AppKit/AppKit.h>
#import <ImageIO/ImageIO.h>
#import <objc/runtime.h>

extern "C" void jalium_metal_init();
extern "C" void jalium_fill_path(JaliumRenderTarget*, float, float,
    const float*, uint32_t, JaliumBrush*, int32_t, int32_t);

static void Check(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

struct Surface {
    std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context;
    NSView* view;
    std::unique_ptr<JaliumRenderTarget, decltype(&jalium_render_target_destroy)> target;
    int width, height;

    Surface(int logicalWidth, int logicalHeight, float scale, uint32_t msaa)
        : context(jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy),
          view([[NSView alloc] initWithFrame:NSMakeRect(0, 0, logicalWidth, logicalHeight)]),
          target(nullptr, jalium_render_target_destroy),
          width(std::lround(logicalWidth * scale)), height(std::lround(logicalHeight * scale))
    {
        Check(context != nullptr, "Metal context creation failed");
        JaliumSurfaceDescriptor descriptor{};
        descriptor.platform = JALIUM_PLATFORM_MACOS;
        descriptor.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
        target.reset(jalium_render_target_create_for_surface(context.get(), &descriptor, width, height));
        Check(target != nullptr, "Metal target creation failed");
        jalium_render_target_set_dpi(target.get(), 96 * scale, 96 * scale);
        jalium_render_target_set_path_msaa(target.get(), msaa);
    }

    void Begin(bool readback = true)
    {
        if (readback) Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK,
            "GPU readback request failed");
        Check(jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "BeginDraw failed");
    }

    std::vector<uint8_t> End()
    {
        Check(jalium_render_target_end_draw(target.get()) == JALIUM_OK, "EndDraw failed");
        std::vector<uint8_t> pixels(width * height * 4);
        int32_t readWidth = 0, readHeight = 0;
        Check(jalium_render_target_fetch_readback(target.get(), pixels.data(), width * 4,
            &readWidth, &readHeight) == JALIUM_OK && readWidth == width && readHeight == height,
            "GPU readback dimensions are incorrect");
        return pixels;
    }
};

using Brush = std::unique_ptr<JaliumBrush, decltype(&jalium_brush_destroy)>;
static Brush Solid(Surface& surface, float r, float g, float b, float a = 1)
{
    Brush brush(jalium_brush_create_solid(surface.context.get(), r, g, b, a), jalium_brush_destroy);
    Check(brush != nullptr, "Solid brush creation failed");
    return brush;
}

static NSData* EncodePng(const uint8_t* premultipliedBgra, uint32_t width, uint32_t height)
{
    CGColorSpaceRef colorSpace = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGDataProviderRef provider = CGDataProviderCreateWithData(nullptr, premultipliedBgra, width * height * 4, nullptr);
    CGImageRef image = CGImageCreate(width, height, 8, 32, width * 4, colorSpace,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst) | kCGBitmapByteOrder32Little, provider, nullptr, false,
        kCGRenderingIntentDefault);
    NSMutableData* png = [NSMutableData data];
    CGImageDestinationRef destination = CGImageDestinationCreateWithData(
        (__bridge CFMutableDataRef)png, CFSTR("public.png"), 1, nullptr);
    Check(image && destination, "Asymmetric PNG fixture creation failed");
    CGImageDestinationAddImage(destination, image, nullptr);
    bool encoded = CGImageDestinationFinalize(destination);
    CFRelease(destination); CGImageRelease(image); CGDataProviderRelease(provider);
    CGColorSpaceRelease(colorSpace);
    Check(encoded, "PNG fixture encoding failed");
    return png;
}

static void TestBitmap(float scale, uint32_t msaa)
{
    Surface surface(64, 32, scale, msaa);
    // Top row red/green; bottom row blue/white. Exercise both the rendering
    // decoder and Gallery's media decoder before their pixels reach the GPU.
    const uint8_t source[] = {0,0,255,255, 0,255,0,255, 255,0,0,255, 255,255,255,255};
    NSData* png = EncodePng(source, 2, 2);
    for (auto format : {JALIUM_PF_BGRA8, JALIUM_PF_RGBA8}) {
        jalium_image_t media{};
        Check(jalium_image_decode_memory(static_cast<const uint8_t*>(png.bytes), png.length,
            format, &media) == JALIUM_MEDIA_OK, "Apple media PNG decode failed");
        bool matches = media.width == 2 && media.height == 2 && media.stride_bytes == 8;
        for (int i = 0; matches && i < 16; ++i) {
            int expectedIndex = i;
            if (format == JALIUM_PF_RGBA8 && i % 4 != 1 && i % 4 != 3)
                expectedIndex = i % 4 == 0 ? i + 2 : i - 2;
            matches = media.pixels[i] == source[expectedIndex];
        }
        jalium_image_free(&media);
        Check(matches, "Apple media PNG pixels are vertically reversed or have incorrect channels");
    }
    using Bitmap = std::unique_ptr<JaliumImage, decltype(&jalium_bitmap_destroy)>;
    Bitmap raw(jalium_bitmap_create_from_pixels(surface.context.get(), source, 2, 2, 8), jalium_bitmap_destroy);
    Bitmap decoded(jalium_bitmap_create_from_memory(surface.context.get(),
        static_cast<const uint8_t*>(png.bytes), static_cast<uint32_t>(png.length)), jalium_bitmap_destroy);
    const uint8_t translucent[] = {0,0,255,128};
    Bitmap alpha(jalium_bitmap_create_from_pixels(surface.context.get(), translucent, 1, 1, 4), jalium_bitmap_destroy);
    const uint8_t premultiplied[] = {0,0,128,128};
    NSData* alphaPng = EncodePng(premultiplied, 1, 1);
    jalium_image_t media{};
    Check(jalium_image_decode_memory(static_cast<const uint8_t*>(alphaPng.bytes), alphaPng.length,
        JALIUM_PF_BGRA8, &media) == JALIUM_MEDIA_OK, "Translucent Apple media PNG decode failed");
    bool straightAlpha = media.width == 1 && media.height == 1 && media.pixels[2] >= 254 && media.pixels[3] == 128;
    Bitmap mediaAlpha(jalium_bitmap_create_from_pixels(surface.context.get(), media.pixels,
        media.width, media.height, media.stride_bytes), jalium_bitmap_destroy);
    jalium_image_free(&media);
    Check(straightAlpha, "Apple media must return straight-alpha pixels to the bitmap upload ABI");
    Check(raw && decoded && alpha, "Bitmap resource creation failed");
    surface.Begin();
    jalium_render_target_clear(surface.target.get(), 0, 0, 0, 1);
    jalium_draw_bitmap_ex(surface.target.get(), raw.get(), 0, 0, 16, 16, 1, 3);
    jalium_draw_bitmap_ex(surface.target.get(), decoded.get(), 20, 0, 16, 16, 1, 3);
    jalium_draw_bitmap_ex(surface.target.get(), alpha.get(), 40, 0, 16, 16, 1, 3);
    jalium_draw_bitmap_ex(surface.target.get(), mediaAlpha.get(), 40, 16, 16, 16, 1, 3);
    auto pixels = surface.End();
    auto pixel = [&](int x, int y) { return pixels.data() + (std::lround(y * scale) * surface.width +
        std::lround(x * scale)) * 4; };
    for (int y : {4, 12}) for (int x : {4, 12})
        Check(std::memcmp(pixel(x, y), pixel(x + 20, y), 4) == 0,
            "Decoded PNG is vertically reversed relative to the raw top-down image");
    Check(pixel(4,4)[2] > 250 && pixel(4,12)[0] > 250, "Raw bitmap orientation is incorrect");
    Check(std::abs(int(pixel(44,4)[2]) - 128) <= 1,
        "Straight-alpha bitmap upload was not premultiplied for blending");
    Check(std::abs(int(pixel(44,20)[2]) - 128) <= 1,
        "Apple media pixels were premultiplied twice before blending");
}

static void TestCaptionGlyphs(float scale, uint32_t msaa)
{
    // These are the filled outline paths from Themes/Controls/TitleBar.jalxaml.
    // The outer rectangles wind clockwise; their holes wind counterclockwise.
    const float maximize[] = {
        0,3,13, 0,13,13, 0,13,3, 0,3,3, 5,
        2,12,12, 0,4,12, 0,4,4, 0,12,4, 0,12,12, 5
    };
    const float restore[] = {
        0,3,14, 0,12,14, 0,12,5, 0,3,5, 5,
        2,11,13, 0,4,13, 0,4,6, 0,11,6, 0,11,13, 5,
        2,5,5, 0,6,5, 0,6,4, 0,13,4, 0,13,11,
        0,12,11, 0,12,12, 0,14,12, 0,14,3, 0,5,3, 0,5,5, 5
    };
    for (bool dark : {false, true}) for (bool restored : {false, true}) {
        Surface surface(24, 24, scale, msaa);
        auto ink = Solid(surface, dark ? 1 : 0, dark ? 1 : 0, dark ? 1 : 0);
        surface.Begin();
        const float background = dark ? 0 : 1;
        jalium_render_target_clear(surface.target.get(), background, background, background, 1);
        jalium_fill_path(surface.target.get(), 3, restored ? 5 : 3,
            restored ? restore : maximize,
            restored ? static_cast<uint32_t>(std::size(restore)) : static_cast<uint32_t>(std::size(maximize)),
            ink.get(), 0, -1);
        auto pixels = surface.End();
        auto isInk = [&](float x, float y) {
            auto* pixel = pixels.data() + (static_cast<int>(y * scale) * surface.width +
                static_cast<int>(x * scale)) * 4;
            return dark ? pixel[0] > 245 : pixel[0] < 10;
        };
        Check(!isInk(8.5f, 8.5f), restored ? "Restore glyph's center must remain hollow" :
            "Maximize glyph's center must remain hollow");
        Check(isInk(3.5f, 8.5f) && isInk(restored ? 11.5f : 12.5f, 8.5f) &&
            isInk(8.5f, restored ? 5.5f : 3.5f) && isInk(8.5f, restored ? 13.5f : 12.5f),
            "Caption glyph lost an outline edge");
        if (restored) Check(isInk(9.5f, 3.5f) && isInk(13.5f, 8.5f),
            "Restore glyph lost its rear window outline");
        if (const char* artifactDir = std::getenv("JALIUM_ICON_ARTIFACT_DIR");
            artifactDir && scale == 2 && msaa == 4) {
            NSString* path = [NSString stringWithFormat:@"%s/%s-%s.png", artifactDir,
                restored ? "restore" : "maximize", dark ? "dark" : "light"];
            Check([EncodePng(pixels.data(), surface.width, surface.height) writeToFile:path atomically:YES],
                "Caption glyph artifact write failed");
        }
    }
}

static void TestCompoundPathWinding(float scale, uint32_t msaa)
{
    for (int rule : {0, 1}) for (bool sameDirection : {false, true})
        for (bool reversed : {false, true}) {
        // Outer, hole, island, and a disjoint shape with opposite winding.
        std::vector<std::vector<float>> contours = {
            {3,3, 3,21, 21,21, 21,3},
            {7,7, 17,7, 17,17, 7,17},
            {10,10, 10,14, 14,14, 14,10},
            {27,3, 35,3, 35,11, 27,11}
        };
        auto reverse = [](std::vector<float>& points) {
            for (size_t i = 0, j = points.size() - 2; i < j; i += 2, j -= 2) {
                std::swap(points[i], points[j]);
                std::swap(points[i + 1], points[j + 1]);
            }
        };
        if (sameDirection) reverse(contours[1]);
        if (reversed) for (auto& contour : contours) reverse(contour);
        // A hole may precede its parent in the command stream.
        std::rotate(contours.begin(), contours.begin() + 1, contours.end());
        std::vector<float> commands;
        for (size_t i = 0; i < contours.size(); ++i) {
            const auto& points = contours[i];
            if (i) commands.insert(commands.end(), {2, points[0], points[1]});
            for (size_t v = 2; v < points.size(); v += 2)
                commands.insert(commands.end(), {0, points[v], points[v + 1]});
            commands.push_back(5);
        }
        Surface surface(40, 24, scale, msaa);
        auto ink = Solid(surface, 0, 0, 0, 0.5f);
        surface.Begin();
        jalium_render_target_clear(surface.target.get(), 1, 1, 1, 1);
        jalium_fill_path(surface.target.get(), contours[0][0], contours[0][1],
            commands.data(), static_cast<uint32_t>(commands.size()), ink.get(), rule, -1);
        auto pixels = surface.End();
        auto checkPixel = [&](float x, float y, int expected, const char* message) {
            auto* pixel = pixels.data() + (static_cast<int>(y * scale) * surface.width +
                static_cast<int>(x * scale)) * 4;
            Check(std::abs(int(pixel[0]) - expected) <= 2, message);
        };
        checkPixel(3.5f, 12.5f, 128, "Compound outer outline is missing");
        checkPixel(8.5f, 8.5f, rule == 1 && sameDirection ? 128 : 255,
            "Compound interior does not respect its fill rule");
        checkPixel(11.5f, 11.5f, 128, "Nested island is missing or painted twice");
        checkPixel(28.5f, 4.5f, 128, "Disjoint contour was classified as a hole");
        checkPixel(25.5f, 4.5f, 255, "Compound fill leaked outside its contours");
    }
}

static void TestDirtyRendering(float scale, uint32_t msaa)
{
    Surface surface(128, 96, scale, msaa);
    auto background = Solid(surface, 1, 1, 1);
    auto foreground = Solid(surface, 0.05f, 0.2f, 0.8f, 0.65f);
    auto ink = Solid(surface, 0.1f, 0.1f, 0.1f);
    std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)> font(
        jalium_text_format_create(surface.context.get(), reinterpret_cast<const wchar_t*>(u"Helvetica"),
            15, 400, 0), jalium_text_format_destroy);
    Check(font != nullptr, "Text format creation failed");
    auto paint = [&] {
        auto* target = surface.target.get();
        jalium_draw_fill_rectangle(target, 0, 0, 128, 96, background.get());
        jalium_push_clip(target, 12.25f, 12.25f, 100, 72);
        jalium_draw_fill_rounded_rectangle(target, 16.25f, 18.25f, 80, 50, 12, 12, foreground.get());
        jalium_draw_text(target, reinterpret_cast<const wchar_t*>(u"Gallery 中文 😀"), 13,
            font.get(), 20, 25, 90, 32, ink.get());
        jalium_pop_clip(target);
        jalium_push_clip(target, 200, 200, 10, 10);
        jalium_draw_fill_rectangle(target, 0, 0, 128, 96, foreground.get());
        jalium_pop_clip(target);
        jalium_draw_fill_rectangle(target, 104.25f, 76.25f, 5, 5, foreground.get());
    };
    surface.Begin();
    jalium_render_target_clear(surface.target.get(), 1, 1, 1, 1);
    paint();
    auto reference = surface.End();
    JaliumGpuStats initial{};
    jalium_render_target_query_gpu_stats(surface.target.get(), &initial);
    for (int frame = 0; frame < 8; ++frame) {
        jalium_render_target_add_dirty_rect(surface.target.get(),
            10.25f + frame, 10.25f, 56.5f, 45.5f);
        surface.Begin();
        paint();
        auto incremental = surface.End();
        int differences = 0;
        for (size_t i = 0; i < reference.size(); ++i)
            if (std::abs(int(reference[i]) - incremental[i]) > 1) ++differences;
        if (differences) std::fprintf(stderr, "dirty rendering: scale=%.1f msaa=%u frame=%d differences=%d\n",
            scale, msaa, frame, differences);
        Check(differences == 0, "Partial redraw differs from an unchanged full redraw");
    }
    JaliumGpuStats final{};
    jalium_render_target_query_gpu_stats(surface.target.get(), &final);
    Check(initial.glyphSlotsUsed > 0 && final.glyphSlotsUsed == initial.glyphSlotsUsed,
        "Changing damage bounds rerasterized and cached identical text");
}

static void TestChangingDamage(float scale, uint32_t msaa)
{
    Surface incremental(128, 96, scale, msaa);
    Surface full(128, 96, scale, msaa);
    auto background = Solid(incremental, 1, 1, 1);
    auto blue = Solid(incremental, 0, 0.2f, 0.9f, 0.7f);
    auto red = Solid(incremental, 0.9f, 0.1f, 0, 0.7f);
    auto paint = [&](Surface& surface, int frame) {
        auto* target = surface.target.get();
        jalium_draw_fill_rectangle(target, 0, 0, 128, 96, background.get());
        // Fractional right/bottom edges touch one more physical pixel than
        // ceil(width/height). Change both the color and a thin caret each time.
        jalium_draw_fill_rounded_rectangle(target, 16.25f, 18.25f, 80, 50, 12, 12,
            (frame % 2 ? red : blue).get());
        jalium_draw_fill_rectangle(target, 104.25f + frame % 3, 76.25f, 1, 5, blue.get());
    };
    for (int frame = 0; frame < 6; ++frame) {
        if (frame) {
            jalium_render_target_add_dirty_rect(incremental.target.get(), 15.75f, 17.75f, 81, 51);
            jalium_render_target_add_dirty_rect(incremental.target.get(), 103.75f, 75.75f, 4, 6);
        }
        incremental.Begin();
        if (!frame) jalium_render_target_clear(incremental.target.get(), 1, 1, 1, 1);
        paint(incremental, frame); auto actual = incremental.End();
        jalium_render_target_set_full_invalidation(full.target.get());
        full.Begin(); jalium_render_target_clear(full.target.get(), 1, 1, 1, 1);
        paint(full, frame); auto expected = full.End();
        size_t differences = 0;
        for (size_t i = 0; i < actual.size(); ++i)
            if (std::abs(int(actual[i]) - expected[i]) > 1) ++differences;
        if (differences) std::fprintf(stderr, "changing damage: scale=%.1f msaa=%u frame=%d differences=%zu\n",
            scale, msaa, frame, differences);
        Check(differences == 0, "Changed content left stale pixels at fractional damage boundaries");
    }
}

static void TestClipChanges(float scale, uint32_t msaa)
{
    Surface surface(128, 96, scale, msaa);
    auto brush = Solid(surface, 0.2f, 0.4f, 0.8f, 0.7f);
    auto paint = [&](int extraDepth) {
        jalium_render_target_set_full_invalidation(surface.target.get());
        surface.Begin();
        auto* target = surface.target.get();
        jalium_render_target_clear(target, 1, 1, 1, 1);
        jalium_push_clip_aliased(target, 8, 8, 104, 80);
        for (int i = 0; i < extraDepth; ++i) jalium_push_clip(target, 0, 0, 128, 96);
        jalium_push_rounded_rect_clip(target, 12, 12, 80, 60, 8, 8);
        jalium_push_rounded_rect_clip_exclude(target, 30, 28, 24, 20, 4, 4);
        jalium_draw_fill_rectangle(target, 0, 0, 128, 96, brush.get());
        jalium_pop_clip(target);
        jalium_pop_clip(target);
        for (int i = 0; i < extraDepth; ++i) jalium_pop_clip(target);
        jalium_pop_clip(target);
        // A disjoint empty clip must not reuse the preceding nonempty scissor.
        jalium_push_clip(target, 10, 10, 5, 5);
        jalium_push_clip(target, 30, 30, 5, 5);
        jalium_draw_fill_rectangle(target, 0, 0, 128, 96, brush.get());
        jalium_pop_clip(target); jalium_pop_clip(target);
        jalium_draw_fill_rectangle(target, 110, 82, 8, 8, brush.get());
        return surface.End();
    };
    auto simple = paint(0);
    auto deep = paint(20);
    for (size_t i = 0; i < simple.size(); ++i)
        Check(std::abs(int(simple[i]) - deep[i]) <= 1, "Changing clip depth altered rounded/excluded clip output");
    auto pixel = [&](int x, int y) { return simple.data() + (std::lround(y * scale) * surface.width +
        std::lround(x * scale)) * 4; };
    Check(pixel(40,38)[0] > 250 && pixel(20,38)[0] < 250 && pixel(116,86)[0] < 250,
        "Clip exclusion or restoring the scissor after PopClip failed");
}

static void TestRetainedDamage(float scale, uint32_t msaa)
{
    Surface surface(128, 96, scale, msaa);
    auto red = Solid(surface, 1, 0, 0);
    surface.Begin();
    jalium_render_target_clear(surface.target.get(), 0, 0, 0, 1);
    surface.End();
    jalium_render_target_add_dirty_rect(surface.target.get(), 0, 0, 10, 10);
    surface.Begin();
    jalium_push_clip_aliased(surface.target.get(), 0, 0, 10, 10);
    void* layer = jalium_render_target_realize_layer_begin(surface.target.get(), nullptr, 16, 16, 64, 48);
    Check(layer != nullptr, "Retained layer creation failed");
    jalium_draw_fill_rectangle(surface.target.get(), 16, 16, 64, 48, red.get());
    jalium_render_target_realize_layer_end(surface.target.get(), layer);
    jalium_pop_clip(surface.target.get());
    surface.End();
    jalium_render_target_set_full_invalidation(surface.target.get());
    surface.Begin();
    jalium_render_target_clear(surface.target.get(), 0, 0, 0, 1);
    jalium_render_target_composite_layer(surface.target.get(), layer, 16, 16, 64, 48, 1);
    auto pixels = surface.End();
    jalium_render_target_destroy_retained_layer(surface.target.get(), layer);
    auto pixel = [&](int x, int y) { return pixels.data() + (std::lround(y * scale) * surface.width +
        std::lround(x * scale)) * 4; };
    Check(pixel(20,20)[2] > 250 && pixel(70,58)[2] > 250,
        "A retained layer was captured through the transient frame damage clip");
}

static int hideCalls = 0, unhideCalls = 0;
static void CountHide(id, SEL) { ++hideCalls; }
static void CountUnhide(id, SEL) { ++unhideCalls; }

static void TestCursor()
{
    Check(jalium_platform_init() == JALIUM_OK, "AppKit initialization failed");
    JaliumWindowParams params{};
    params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Cursor regression");
    params.x = params.y = JALIUM_DEFAULT_POS;
    params.width = 320; params.height = 200;
    params.style = JALIUM_WINDOW_STYLE_BORDERLESS;
    // Count hide/unhide calls without changing the user's system cursor.
    struct CursorMethods {
        Method hide = class_getClassMethod(NSCursor.class, @selector(hide));
        Method unhide = class_getClassMethod(NSCursor.class, @selector(unhide));
        IMP originalHide = method_setImplementation(hide, reinterpret_cast<IMP>(CountHide));
        IMP originalUnhide = method_setImplementation(unhide, reinterpret_cast<IMP>(CountUnhide));
        ~CursorMethods() {
            method_setImplementation(hide, originalHide);
            method_setImplementation(unhide, originalUnhide);
        }
    } methods;
    std::unique_ptr<JaliumPlatformWindow, decltype(&jalium_window_destroy)> window(
        jalium_window_create(&params), jalium_window_destroy);
    Check(window != nullptr, "Cursor test window creation failed");
    hideCalls = unhideCalls = 0;
    for (int i = 0; i < 5; ++i) jalium_window_set_cursor(window.get(), JALIUM_CURSOR_HIDDEN);
    jalium_window_set_cursor(window.get(), JALIUM_CURSOR_ARROW);
    Check(hideCalls == 1 && unhideCalls == 1, "Repeated hidden cursor requests leaked AppKit hide counts");
    jalium_window_set_cursor(window.get(), JALIUM_CURSOR_HAND);
    Check(NSCursor.currentCursor == NSCursor.pointingHandCursor, "Hand cursor was not applied");
    jalium_window_set_cursor(window.get(), JALIUM_CURSOR_IBEAM);
    Check(NSCursor.currentCursor == NSCursor.IBeamCursor, "Text cursor was not applied");
    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(window.get());
    NSEvent* event = [NSEvent otherEventWithType:NSEventTypeApplicationDefined
        location:NSZeroPoint modifierFlags:0 timestamp:0 windowNumber:view.window.windowNumber
        context:nil subtype:0 data1:0 data2:0];
    jalium_window_set_cursor(window.get(), JALIUM_CURSOR_HIDDEN);
    [view mouseExited:event];
    Check(hideCalls == unhideCalls, "Leaving a window kept its cursor hidden");
    jalium_window_set_cursor(window.get(), JALIUM_CURSOR_HIDDEN);
    window.reset();
    Check(hideCalls == unhideCalls, "Destroying a window kept its cursor hidden");
    jalium_platform_shutdown();
}

static void TestRoundedTextLineHeight(float scale, uint32_t msaa)
{
    Surface surface(320, 64, scale, msaa);
    auto ink = Solid(surface, 0, 0, 0);
    const char16_t* text = u"编辑内容🙂 + text";
    for (const char16_t* family : {u"PingFang SC", u"Helvetica"})
    for (float size : {16.0f, 18.0f, 20.0f}) {
        std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)> font(
            jalium_text_format_create(surface.context.get(), reinterpret_cast<const wchar_t*>(family),
                size, 400, 0), jalium_text_format_destroy);
        Check(font != nullptr, "Rounded line text format creation failed");
        JaliumTextMetrics metrics{};
        Check(jalium_text_format_get_font_metrics(font.get(), &metrics) == JALIUM_OK,
            "Rounded line font metrics unavailable");
        jalium_text_format_set_word_wrapping(font.get(), 1);
        const float lineHeight = std::round(metrics.lineHeight);
        auto paint = [&](float layoutHeight) {
            surface.Begin();
            jalium_render_target_clear(surface.target.get(), 1, 1, 1, 1);
            jalium_push_clip(surface.target.get(), 4, 4, 300, lineHeight);
            jalium_draw_text(surface.target.get(), reinterpret_cast<const wchar_t*>(text),
                static_cast<uint32_t>(std::char_traits<char16_t>::length(text)), font.get(),
                4, 4, 10000, layoutHeight, ink.get());
            jalium_pop_clip(surface.target.get());
            return surface.End();
        };
        const auto pixels = paint(lineHeight);
        const auto reference = paint(std::ceil(metrics.lineHeight));
        size_t visible = 0;
        for (size_t offset = 0; offset < pixels.size(); offset += 4)
            visible += std::min({pixels[offset], pixels[offset + 1], pixels[offset + 2]}) < 200;
        if (visible < 32) {
            std::fprintf(stderr, "Rounded text size=%g natural_height=%g frame_height=%g ink_pixels=%zu\n",
                size, metrics.lineHeight, lineHeight, visible);
            throw std::runtime_error("Rounding the measured editor line height removed all text");
        }
        for (size_t offset = 0; offset < pixels.size(); ++offset)
            Check(std::abs(int(pixels[offset]) - reference[offset]) <= 1,
                "Fitting a rounded editor line shifted its baseline or escaped its clip");
    }
}

static void Benchmark()
{
    Surface surface(800, 500, 2, 4);
    auto background = Solid(surface, 0.15f, 0.15f, 0.15f);
    auto ink = Solid(surface, 0.9f, 0.9f, 0.9f);
    std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)> font(
        jalium_text_format_create(surface.context.get(), reinterpret_cast<const wchar_t*>(u"Helvetica"),
            14, 400, 0), jalium_text_format_destroy);
    constexpr int frames = 24;
    auto paint = [&](int frame, bool readback) {
        if (frame) jalium_render_target_add_dirty_rect(surface.target.get(), 0, 0, 800 - frame, 500);
        surface.Begin(readback);
        jalium_render_target_clear(surface.target.get(), 0.15f, 0.15f, 0.15f, 1);
        for (int row = 0; row < 24; ++row) {
            jalium_push_clip(surface.target.get(), 0, row * 20, 790, 20);
            jalium_draw_fill_rectangle(surface.target.get(), 0, row * 20, 790, 20, background.get());
            jalium_draw_text(surface.target.get(), reinterpret_cast<const wchar_t*>(u"Cacheable Gallery label"),
                23, font.get(), 8, row * 20, 780, 20, ink.get());
            jalium_pop_clip(surface.target.get());
        }
        if (readback) surface.End();
        else Check(jalium_render_target_end_draw(surface.target.get()) == JALIUM_OK, "Benchmark EndDraw failed");
    };
    paint(0, true);
    auto start = std::chrono::steady_clock::now();
    for (int frame = 1; frame <= frames; ++frame) paint(frame, frame == frames);
    double elapsed = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
    JaliumGpuStats stats{};
    jalium_render_target_query_gpu_stats(surface.target.get(), &stats);
    JaliumGpuTimingStats timing{};
    jalium_render_target_query_gpu_timing(surface.target.get(), &timing);
    std::printf("Retina 4x MSAA: frames=%d elapsed_ms=%.3f ms_per_frame=%.3f last_gpu_ms=%.3f text_entries=%d text_bytes=%lld\n",
        frames, elapsed, elapsed / frames, timing.totalGpuNs / 1e6, stats.glyphSlotsUsed,
        static_cast<long long>(stats.glyphBytes));
}

int main(int argc, char** argv)
{
    @autoreleasepool {
        [NSApplication sharedApplication];
        jalium_metal_init();
        if (argc > 1 && std::strcmp(argv[1], "--text-line-height") == 0) {
            try {
                for (float scale : {1.0f, 2.0f}) for (uint32_t msaa : {1u, 4u})
                    TestRoundedTextLineHeight(scale, msaa);
                std::printf("PASS rounded editor text line heights at 1x/2x and 1x/4x MSAA\n");
                return 0;
            }
            catch (const std::exception& error) { std::fprintf(stderr, "%s\n", error.what()); return 1; }
        }
        if (argc > 1 && std::strcmp(argv[1], "--benchmark") == 0) {
            try { Benchmark(); return 0; }
            catch (const std::exception& error) { std::fprintf(stderr, "%s\n", error.what()); return 1; }
        }
        int failures = 0;
        auto run = [&](const char* name, auto test) {
            try { test(); std::printf("PASS %s\n", name); }
            catch (const std::exception& error) { ++failures; std::fprintf(stderr, "FAIL %s: %s\n", name, error.what()); }
        };
        for (float scale : {1.0f, 2.0f}) for (uint32_t msaa : {1u, 4u}) {
            run("bitmap orientation/alpha", [&] { TestBitmap(scale, msaa); });
            run("hollow maximize/restore glyphs", [&] { TestCaptionGlyphs(scale, msaa); });
            run("compound fill rules/winding/islands", [&] { TestCompoundPathWinding(scale, msaa); });
            run("dirty redraw/text cache", [&] { TestDirtyRendering(scale, msaa); });
            run("rounded editor text line heights", [&] { TestRoundedTextLineHeight(scale, msaa); });
            run("changing fractional damage", [&] { TestChangingDamage(scale, msaa); });
            run("rounded/excluded/deep/empty clips", [&] { TestClipChanges(scale, msaa); });
            run("retained layer damage", [&] { TestRetainedDamage(scale, msaa); });
        }
        run("AppKit cursor balance", TestCursor);
        return failures ? 1 : 0;
    }
}
