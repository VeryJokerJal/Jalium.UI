#include "jalium_api.h"
#include "jalium_platform.h"

#include <cmath>
#include <cstdio>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

#import <AppKit/AppKit.h>

extern "C" void jalium_metal_init();
void TestWordNavigation();

// Supply isolated scroll packets directly to the view without injecting input
// into the user's desktop.
@interface JaliumSmokeScrollEvent : NSEvent
@property(nonatomic) CGFloat scrollX;
@property(nonatomic) CGFloat scrollY;
@property(nonatomic) BOOL precise;
@end
@implementation JaliumSmokeScrollEvent
- (NSEventType)type { return NSEventTypeScrollWheel; }
- (CGFloat)scrollingDeltaX { return self.scrollX; }
- (CGFloat)scrollingDeltaY { return self.scrollY; }
- (BOOL)hasPreciseScrollingDeltas { return self.precise; }
- (NSEventPhase)phase { return NSEventPhaseNone; }
- (NSEventPhase)momentumPhase { return NSEventPhaseNone; }
- (NSPoint)locationInWindow { return NSZeroPoint; }
- (NSEventModifierFlags)modifierFlags { return 0; }
@end

static void Check(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

static void TestPixels(float dpiScale)
{
    jalium_metal_init();
    std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context(
        jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy);
    Check(context != nullptr, "Metal context creation failed");
    NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 128, 96)];
    JaliumSurfaceDescriptor surface{};
    surface.platform = JALIUM_PLATFORM_MACOS;
    surface.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
    surface.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
    const int physicalWidth = std::lround(128 * dpiScale);
    const int physicalHeight = std::lround(96 * dpiScale);
    std::unique_ptr<JaliumRenderTarget, decltype(&jalium_render_target_destroy)> target(
        jalium_render_target_create_for_surface(context.get(), &surface, physicalWidth, physicalHeight),
        jalium_render_target_destroy);
    Check(target != nullptr, "Metal render target creation failed");
    jalium_render_target_set_dpi(target.get(), 96 * dpiScale, 96 * dpiScale);
    std::unique_ptr<JaliumBrush, decltype(&jalium_brush_destroy)> brush(
        jalium_brush_create_solid(context.get(), 1, 0, 0, 1), jalium_brush_destroy);
    std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)> font(
        jalium_text_format_create(context.get(), reinterpret_cast<const wchar_t*>(u"Helvetica"),
            18, 400, 0), jalium_text_format_destroy);
    Check(brush && font, "Metal drawing resource creation failed");
    static_assert(sizeof(JaliumTextRangeMetrics) == 24);
    const char16_t ranged[] = u"a\U0001f600b\r\nc";
    JaliumTextRangeMetrics range{};
    Check(jalium_text_format_hit_test_text_range(font.get(), reinterpret_cast<const wchar_t*>(ranged),
        7, 120, 80, 2, 1, &range) == JALIUM_OK && range.textPosition == 1 && range.length == 2 && range.width > 2,
        "Range ABI must expand a partial surrogate pair and return UTF-16 offsets");
    Check(jalium_text_format_hit_test_text_range(font.get(), reinterpret_cast<const wchar_t*>(ranged),
        7, 120, 80, 0, 7, &range) == JALIUM_OK && range.length == 6,
        "Range ABI must stop at the first visual row and include CRLF");
    Check(jalium_text_format_hit_test_text_range(font.get(), reinterpret_cast<const wchar_t*>(ranged),
        7, 120, 80, 6, 0, &range) == JALIUM_OK && range.textPosition == 6 && range.length == 0 && range.width == 0,
        "Zero-length range must return an insertion caret");
    Check(jalium_text_format_hit_test_text_range(font.get(), reinterpret_cast<const wchar_t*>(ranged),
        7, 120, 80, 6, UINT32_MAX, &range) == JALIUM_ERROR_INVALID_ARGUMENT && range.height == 0,
        "Invalid range must be rejected and clear the output");
    const char16_t text[] = u"Hello \u4e2d\u6587 \U0001f600";
    const uint32_t textLength = sizeof(text) / sizeof(char16_t) - 1;
    JaliumTextMetrics metrics{};
    Check(jalium_text_format_measure_text(font.get(), reinterpret_cast<const wchar_t*>(text),
        textLength, 120, 50, &metrics) == JALIUM_OK && metrics.width > 20 && metrics.height > 10,
        "CoreText UTF-16 measurement failed");
    JaliumTextHitTestResult hit{};
    Check(jalium_text_format_hit_test_text_position(font.get(),
        reinterpret_cast<const wchar_t*>(text), textLength, 120, 50, 2, 0, &hit) == JALIUM_OK &&
        hit.caretX > 0 && hit.caretHeight > 10 && std::isfinite(hit.caretY),
        "CoreText caret ABI failed");

    Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK, "Readback request failed");
    Check(jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "BeginDraw failed");
    jalium_render_target_clear(target.get(), 1, 1, 1, 1);
    jalium_draw_fill_rectangle(target.get(), 8, 8, 32, 24, brush.get());
    jalium_draw_text(target.get(), reinterpret_cast<const wchar_t*>(text), textLength,
        font.get(), 4, 40, 120, 50, brush.get());
    const char16_t tightText[] = u"Gallery";
    JaliumTextMetrics tightMetrics{};
    Check(jalium_text_format_measure_text(font.get(), reinterpret_cast<const wchar_t*>(tightText),
        7, 120, 50, &tightMetrics) == JALIUM_OK, "Tight text measurement failed");
    jalium_draw_text(target.get(), reinterpret_cast<const wchar_t*>(tightText), 7,
        font.get(), 44, 4, 80, tightMetrics.lineHeight, brush.get());
    jalium_draw_text(target.get(), reinterpret_cast<const wchar_t*>(u"F"), 1,
        font.get(), 100, 65, 24, 25, brush.get());
    Check(jalium_render_target_end_draw(target.get()) == JALIUM_OK, "EndDraw failed");
    std::vector<uint8_t> pixels(physicalWidth * physicalHeight * 4);
    int32_t width = 0, height = 0;
    Check(jalium_render_target_fetch_readback(target.get(), pixels.data(), physicalWidth * 4,
        &width, &height) == JALIUM_OK && width == physicalWidth && height == physicalHeight,
        "GPU readback must preserve physical dimensions at Retina DPI");
    auto pixel = [&](int x, int y) { return pixels.data() + (y * width + x) * 4; };
    const auto red = pixel(20 * dpiScale, 20 * dpiScale);
    Check(red[0] < 5 && red[1] < 5 && red[2] > 250 && red[3] > 250,
        "Filled rectangle has incorrect BGRA pixels");
    const auto white = pixel(100 * dpiScale, 32 * dpiScale);
    Check(white[0] > 250 && white[1] > 250 && white[2] > 250, "Clear color is incorrect");
    int textPixels = 0;
    for (int y = 40 * dpiScale; y < height; ++y)
        for (int x = 0; x < width; ++x)
            if (pixel(x, y)[1] < 200) ++textPixels;
    Check(textPixels > 30, "CoreText glyphs were not painted");
    int tightPixels = 0;
    for (int y = 4 * dpiScale; y < 30 * dpiScale; ++y)
        for (int x = 44 * dpiScale; x < width; ++x)
            if (pixel(x, y)[1] < 200) ++tightPixels;
    if (tightPixels <= 30) std::fprintf(stderr, "tight text: height=%f lineHeight=%f ascent=%f descent=%f gap=%f pixels=%d\n",
        tightMetrics.height, tightMetrics.lineHeight, tightMetrics.ascent, tightMetrics.descent,
        tightMetrics.lineGap, tightPixels);
    Check(tightPixels > 30, "CoreText did not paint text inside its measured height");
    int topRightInk = 0, bottomRightInk = 0;
    for (int y = 65 * dpiScale; y < 86 * dpiScale; ++y)
        for (int x = 106 * dpiScale; x < 120 * dpiScale; ++x)
            if (pixel(x, y)[1] < 200) {
                if (y < 75 * dpiScale) ++topRightInk;
                else ++bottomRightInk;
            }
    Check(topRightInk > bottomRightInk, "CoreText glyphs were painted upside down");
}

static void TestStyledParagraph(float dpi)
{
    std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context(
        jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy);
    Check(context != nullptr, "Paragraph Metal context failed");
    NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 300, 300)];
    JaliumSurfaceDescriptor surface{};
    surface.platform = JALIUM_PLATFORM_MACOS; surface.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
    surface.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
    int size = std::lround(300 * dpi);
    std::unique_ptr<JaliumRenderTarget, decltype(&jalium_render_target_destroy)> target(
        jalium_render_target_create_for_surface(context.get(), &surface, size, size), jalium_render_target_destroy);
    Check(target != nullptr, "Paragraph render target failed");
    jalium_render_target_set_dpi(target.get(), 96 * dpi, 96 * dpi);
    const char16_t* text = u"first styled row with words second styled row \u4e2d\u6587 \U0001f600 third styled row";
    uint32_t length = static_cast<uint32_t>(std::char_traits<char16_t>::length(text));
    auto family = reinterpret_cast<const wchar_t*>(u"Helvetica");
    std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)> normal(
        jalium_text_format_create(context.get(), family, 18, 400, 0), jalium_text_format_destroy);
    std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)> large(
        jalium_text_format_create(context.get(), family, 26, 700, 0), jalium_text_format_destroy);
    JaliumTextSpan spans[] = {{normal.get(), 0, 29, 1, 0, 0, 1},
        {large.get(), 29, length - 29, 0, 0, 1, 1}};
    auto create = [&](float width) { return jalium_text_paragraph_create(normal.get(),
        reinterpret_cast<const uint16_t*>(text), length, spans, 2, width, 27, 0, 0); };
    Check(create(NAN) == nullptr, "Paragraph rejects non-finite width");
    spans[1].textPosition = 30;
    Check(create(180) == nullptr, "Paragraph rejects gaps between style ranges");
    spans[1].textPosition = 29;
    std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)> paragraph(create(180), jalium_text_paragraph_destroy);
    Check(paragraph != nullptr, "Attributed paragraph creation failed");
    uint32_t count = jalium_text_paragraph_line_count(paragraph.get());
    Check(count >= 4, "Styled paragraph did not wrap inside runs");
    std::vector<JaliumParagraphLineMetrics> lines(count);
    uint32_t end = 0;
    for (uint32_t i = 0; i < count; ++i) {
        auto& row = lines[i];
        Check(jalium_text_paragraph_get_line(paragraph.get(), i, &row) == JALIUM_OK,
            "Paragraph row query failed");
        Check(row.line.textPosition == end && row.line.length && row.line.height >= 27,
            "Paragraph rows lost logical offsets or mixed-font height");
        end += row.line.length;
        uint32_t fragments = 0;
        Check(jalium_text_paragraph_get_fragments(paragraph.get(), i, nullptr, 0, &fragments) == JALIUM_OK && fragments,
            "Paragraph has no style fragments");
        JaliumTextFragmentMetrics dummy{};
        Check(jalium_text_paragraph_get_fragments(paragraph.get(), i, &dummy, 0, &fragments) == JALIUM_ERROR_INVALID_ARGUMENT,
            "Paragraph rejects undersized fragment output");
        JaliumParagraphCaret edge{}, hit{};
        Check(jalium_text_paragraph_get_caret(paragraph.get(), i, row.line.rightCaretPosition,
            row.line.rightBackwardAffinity, &edge) == JALIUM_OK, "Paragraph physical edge query failed");
        Check(jalium_text_paragraph_hit_test(paragraph.get(), i, edge.x, &hit) == JALIUM_OK &&
            hit.textPosition == edge.textPosition, "Paragraph physical caret did not hit test consistently");
    }
    Check(end == length, "Paragraph dropped the end of the text");
    spans[0].a = spans[1].a = 0.25f;
    std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)> faded(create(180), jalium_text_paragraph_destroy);
    Check(faded != nullptr, "Transparent styled paragraph creation failed");
    spans[0].a = spans[1].a = 1;
    // CTLine must keep all style fonts alive after the input formats are gone.
    normal.reset(); large.reset();
    auto draw = [&](bool clipped, bool rotated, JaliumTextParagraph* layout = nullptr) {
        Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK, "Paragraph readback request failed");
        Check(jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "Paragraph BeginDraw failed");
        jalium_render_target_clear(target.get(), 1, 1, 1, 1);
        float matrix[] = {0, 1, -1, 0, 270, 0};
        if (rotated) jalium_push_transform(target.get(), matrix);
        if (clipped) jalium_push_clip(target.get(), 12, 20, 90, 240);
        for (uint32_t i = 0; i < count; ++i)
            Check(jalium_render_target_draw_paragraph_line(target.get(), layout ? layout : paragraph.get(), i,
                12, 20 + lines[i].line.y, 1) == JALIUM_OK, "Paragraph line drawing capability failed");
        if (clipped) jalium_pop_clip(target.get());
        if (rotated) jalium_pop_transform(target.get());
        Check(jalium_render_target_end_draw(target.get()) == JALIUM_OK, "Paragraph EndDraw failed");
        std::vector<uint8_t> pixels(static_cast<size_t>(size) * size * 4);
        int32_t w = 0, h = 0;
        Check(jalium_render_target_fetch_readback(target.get(), pixels.data(), size * 4, &w, &h) == JALIUM_OK && w == size && h == size,
            "Paragraph readback dimensions changed");
        return pixels;
    };
    auto pixels = draw(false, false);
    auto fadedPixels = draw(false, false, faded.get());
    for (size_t i = 0; i < fadedPixels.size(); i += 4)
        Check(fadedPixels[i+1] >= 180, "Run opacity must also fade color-font glyphs");
    if (const char* directory = std::getenv("JALIUM_PARAGRAPH_CAPTURE_DIR")) {
        auto capture = [&](const std::vector<uint8_t>& bytes, const char* suffix) {
            CGColorSpaceRef space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
            CGDataProviderRef provider = CGDataProviderCreateWithData(nullptr, bytes.data(), bytes.size(), nullptr);
            CGImageRef image = CGImageCreate(size, size, 8, 32, size * 4, space,
                static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little),
                provider, nullptr, false, kCGRenderingIntentDefault);
            NSBitmapImageRep* bitmap = [[NSBitmapImageRep alloc] initWithCGImage:image];
            NSData* png = [bitmap representationUsingType:NSBitmapImageFileTypePNG properties:@{}];
            NSString* filename = [NSString stringWithFormat:@"%s/paragraph-%.0fx-%s.png", directory, dpi, suffix];
            Check(png && [png writeToFile:filename atomically:YES], "Cannot save paragraph GPU capture");
            CGImageRelease(image); CGDataProviderRelease(provider); CGColorSpaceRelease(space);
        };
        capture(pixels, "styled"); capture(draw(false, true), "rotated"); capture(draw(true, false), "clipped");
        capture(fadedPixels, "opacity");
    }
    Check(pixels == draw(false, false), "Paragraph cache replay changed pixels");
    int red = 0, blue = 0;
    for (size_t i = 0; i < pixels.size(); i += 4) {
        if (pixels[i+2] > 100 && pixels[i] < 100 && pixels[i+1] < 100) ++red;
        if (pixels[i] > 100 && pixels[i+2] < 100 && pixels[i+1] < 100) ++blue;
    }
    Check(red > 60 * dpi && blue > 60 * dpi, "Styled paragraph lost run foreground colors");
    for (const auto& row : lines) {
        int ink = 0;
        int top = std::lround((20 + row.line.y) * dpi);
        int bottom = std::min(size, static_cast<int>((20 + row.line.y + row.line.height) * dpi));
        for (int y = top; y < bottom; ++y) for (int x = 12 * dpi; x < 205 * dpi; ++x) {
            auto pixel = pixels.data() + (y * size + x) * 4;
            if (pixel[1] < 170) ++ink;
        }
        Check(ink > 10, "A wrapped paragraph row was not painted in its line box");
    }
    auto clipped = draw(true, false);
    int inside = 0;
    for (int y = 0; y < size; ++y) for (int x = 0; x < size; ++x) {
        auto pixel = clipped.data() + (y * size + x) * 4;
        bool ink = pixel[1] < 170;
        if (ink && x >= 12 * dpi && x < 102 * dpi) ++inside;
        if (x >= 103 * dpi) Check(!ink, "Paragraph escaped the native clip stack");
    }
    Check(inside > 80, "Clip removed the entire paragraph");
    auto rotated = draw(false, true);
    int rotatedInk = 0;
    for (size_t i = 0; i < rotated.size(); i += 4) if (rotated[i+1] < 170) ++rotatedInk;
    Check(rotatedInk > 100 && rotated != pixels, "Paragraph ignored the full affine transform");
    std::printf("Attributed paragraph GPU checks passed at %.0fx DPI (%u rows)\n", dpi, count);
}

static void TestWindow()
{
    Check(jalium_platform_init() == JALIUM_OK, "AppKit initialization failed");
    float scale = jalium_platform_get_system_dpi_scale();
    JaliumWindowParams params{};
    params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Jalium macOS smoke");
    params.x = params.y = JALIUM_DEFAULT_POS;
    params.width = std::lround(320 * scale);
    params.height = std::lround(200 * scale);
    params.style = JALIUM_WINDOW_STYLE_BORDERLESS | JALIUM_WINDOW_STYLE_RESIZABLE;
    std::unique_ptr<JaliumPlatformWindow, decltype(&jalium_window_destroy)> window(
        jalium_window_create(&params), jalium_window_destroy);
    Check(window != nullptr, "AppKit window creation failed");
    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(window.get());
    Check(std::abs(view.bounds.size.width - 320) < 1 &&
        std::abs(view.bounds.size.height - 200) < 1, "Retina window dimensions were not converted to points");
    Check(view.window.canBecomeKeyWindow && view.window.canBecomeMainWindow,
        "Custom chrome window cannot accept keyboard focus");
    Check((view.window.styleMask & NSWindowStyleMaskResizable) != 0,
        "Borderless window lost its resizable style");
    jalium_window_move(window.get(), 64, 96);
    int32_t x = 0, y = 0;
    jalium_window_get_position(window.get(), &x, &y);
    Check(std::abs(x - 64) <= 1 && std::abs(y - 96) <= 1, "Window move did not round-trip physical coordinates");

    struct InputEvents {
        int starts = 0, updates = 0, ends = 0, characters = 0, backspaces = 0, pastes = 0;
        float wheelX = 0, wheelY = 0;
        std::string committed;
    } input;
    jalium_window_set_event_callback(window.get(), [](const JaliumPlatformEvent* event, void* data) {
        auto& input = *static_cast<InputEvents*>(data);
        switch (event->type) {
            case JALIUM_EVENT_COMPOSITION_START: ++input.starts; break;
            case JALIUM_EVENT_COMPOSITION_UPDATE: ++input.updates; break;
            case JALIUM_EVENT_COMPOSITION_END:
                ++input.ends; input.committed = event->composition.utf8Text; break;
            case JALIUM_EVENT_CHAR_INPUT: ++input.characters; break;
            case JALIUM_EVENT_MOUSE_WHEEL:
                input.wheelX = event->wheel.deltaX; input.wheelY = event->wheel.deltaY; break;
            case JALIUM_EVENT_KEY_DOWN:
                if (event->key.keyCode == 0x08) ++input.backspaces;
                if (event->key.keyCode == 'V' && event->key.modifiers == JALIUM_MOD_META) ++input.pastes;
                break;
            default: break;
        }
    }, &input);
    id<NSTextInputClient> textClient = (id<NSTextInputClient>)view;
    NSEvent* backspace = [NSEvent keyEventWithType:NSEventTypeKeyDown
        location:NSZeroPoint modifierFlags:0 timestamp:0 windowNumber:view.window.windowNumber
        context:nil characters:@"\x7f" charactersIgnoringModifiers:@"\x7f" isARepeat:NO keyCode:51];
    [textClient setMarkedText:@"han" selectedRange:NSMakeRange(3, 0)
        replacementRange:NSMakeRange(NSNotFound, 0)];
    [view keyDown:backspace];
    Check(input.backspaces == 0, "IME Backspace must not delete committed managed text");
    [textClient insertText:@"汉" replacementRange:NSMakeRange(NSNotFound, 0)];
    Check(input.starts == 1 && input.updates == 1 && input.ends == 1 &&
        input.characters == 0 && input.committed == "汉" && ![textClient hasMarkedText],
        "IME commit must end composition once without duplicate character input");
    [view keyDown:backspace];
    Check(input.backspaces == 1, "Backspace was dispatched more than once");
    Check([view tryToPerform:@selector(paste:) with:nil] && input.pastes == 1,
        "AppKit Paste responder action did not dispatch the managed editing command");
    JaliumSmokeScrollEvent* scroll = [JaliumSmokeScrollEvent new];
    scroll.precise = YES;
    scroll.scrollX = 24; scroll.scrollY = -48;
    [view scrollWheel:scroll];
    Check(std::abs(input.wheelX + 0.5f) < 0.001f && std::abs(input.wheelY + 1) < 0.001f,
        "Precise AppKit scroll points were interpreted as wheel notches");
    scroll.precise = NO;
    scroll.scrollX = 1; scroll.scrollY = -3;
    [view scrollWheel:scroll];
    Check(std::abs(input.wheelX + 1.0f / 3) < 0.001f && std::abs(input.wheelY + 1) < 0.001f,
        "AppKit mouse-wheel lines were interpreted as wheel notches");
    jalium_window_set_event_callback(window.get(), nullptr, nullptr);

    int wakes = 0;
    JaliumDispatcher* dispatcher = nullptr;
    Check(jalium_dispatcher_create(&dispatcher) == JALIUM_OK, "Dispatcher creation failed");
    jalium_dispatcher_set_callback(dispatcher, [](void* data) { ++*static_cast<int*>(data); }, &wakes);
    jalium_dispatcher_wake(dispatcher);
    for (int i = 0; i < 20 && wakes == 0; ++i)
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.01, true);
    jalium_dispatcher_destroy(dispatcher);
    Check(wakes == 1, "AppKit dispatcher wake failed");
    window.reset();
    jalium_platform_shutdown();
}

static void TestClipboard()
{
    // Restore every materialized representation after this isolated test.
    struct SavedPasteboard {
        NSMutableArray<NSPasteboardItem*>* items = [NSMutableArray array];
        SavedPasteboard() {
            for (NSPasteboardItem* original in NSPasteboard.generalPasteboard.pasteboardItems) {
                NSPasteboardItem* saved = [NSPasteboardItem new];
                for (NSPasteboardType type in original.types) {
                    NSData* data = [original dataForType:type];
                    if (data) [saved setData:data forType:type];
                }
                [items addObject:saved];
            }
        }
        ~SavedPasteboard() {
            [NSPasteboard.generalPasteboard clearContents];
            if (items.count) [NSPasteboard.generalPasteboard writeObjects:items];
        }
    } saved;
    const char* text = "macOS 中文 😀";
    const char* html = "<b>macOS</b>";
    const JaliumClipboardDataItem items[] = {
        {"text/plain;charset=utf-8", reinterpret_cast<const uint8_t*>(text), static_cast<uint32_t>(std::strlen(text))},
        {"text/html", reinterpret_cast<const uint8_t*>(html), static_cast<uint32_t>(std::strlen(html))},
    };
    Check(jalium_clipboard_set_data(items, 2) == JALIUM_OK, "AppKit clipboard publication failed");
    JaliumUtf16Char* rawText = nullptr;
    Check(jalium_clipboard_get_text(&rawText) == JALIUM_OK && rawText,
        "MIME text was not published as AppKit plain text");
    std::unique_ptr<JaliumUtf16Char, decltype(&jalium_platform_free)> ownedText(rawText, jalium_platform_free);
    Check(std::memcmp(rawText, u"macOS 中文 😀", sizeof(u"macOS 中文 😀")) == 0,
        "AppKit clipboard corrupted UTF-16 text");
    char* rawFormats = nullptr;
    Check(jalium_clipboard_get_formats(&rawFormats) == JALIUM_OK && rawFormats,
        "AppKit clipboard format enumeration failed");
    std::unique_ptr<char, decltype(&jalium_platform_free)> ownedFormats(rawFormats, jalium_platform_free);
    Check(std::strstr(rawFormats, "text/plain;charset=utf-8") && std::strstr(rawFormats, "text/html"),
        "AppKit clipboard formats did not map back to MIME names");
    uint8_t* rawData = nullptr;
    uint32_t size = 0;
    Check(jalium_clipboard_get_data("text/html", &rawData, &size) == JALIUM_OK && rawData,
        "AppKit clipboard MIME data read failed");
    std::unique_ptr<uint8_t, decltype(&jalium_platform_free)> ownedData(rawData, jalium_platform_free);
    Check(size == std::strlen(html) && std::memcmp(rawData, html, size) == 0,
        "AppKit clipboard HTML did not round-trip");
}

int main()
{
    @autoreleasepool {
        try {
            [NSApplication sharedApplication];
            [NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited];
            TestPixels(1);
            TestPixels(2);
            TestStyledParagraph(1);
            TestStyledParagraph(2);
            TestWordNavigation();
            TestWindow();
            TestClipboard();
            std::puts("Metal pixels, CoreText, Retina window, input, clipboard and dispatcher passed");
            return 0;
        } catch (const std::exception& error) {
            std::fprintf(stderr, "%s\n", error.what());
            return 1;
        }
    }
}
