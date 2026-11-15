#include "jalium_api.h"
#include "apple_text_paragraph.h"
#import <AppKit/AppKit.h>
#import <ImageIO/ImageIO.h>
#import <QuartzCore/CAMetalLayer.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
int checks = 0;
bool metal = false;
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); ++checks; }
void Near(double actual, double expected, const char* message) {
    if (std::abs(actual - expected) > .01) {
        std::fprintf(stderr, "%s: %.6f != %.6f\n", message, actual, expected);
        throw std::runtime_error(message);
    }
    ++checks;
}
template<class T, void(*Destroy)(T*)> using TestOwner = std::unique_ptr<T, decltype(Destroy)>;
const wchar_t* Managed(const char16_t* value) { return reinterpret_cast<const wchar_t*>(value); }
constexpr int Width = 128, Height = 600;
struct Example { NSString* text; std::vector<NSRange> vertical; };
struct ValidationSurface {
    NSView* view;
    TestOwner<JaliumRenderTarget, jalium_render_target_destroy> target{nullptr, jalium_render_target_destroy};
    explicit ValidationSurface(JaliumContext* context) {
        view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, Width, Height)];
        view.wantsLayer = YES;
        view.layer = metal ? [CAMetalLayer layer] : [CALayer layer];
        JaliumSurfaceDescriptor descriptor{};
        descriptor.platform = JALIUM_PLATFORM_MACOS;
        descriptor.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
        target.reset(jalium_render_target_create_for_surface(context, &descriptor, Width, Height));
        Check(target != nullptr, "Actual native surface target");
        jalium_render_target_set_dpi(target.get(), 96, 96);
    }
    std::vector<uint8_t> Draw(JaliumTextParagraph* paragraph, const float* transform) {
        Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK, "Request actual backend readback");
        Check(jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "Begin actual backend frame");
        jalium_render_target_clear(target.get(), 0, 0, 0, 0);
        jalium_push_transform(target.get(), transform);
        Check(jalium_render_target_draw_paragraph_line(target.get(), paragraph, 0, 0, 0, 1) == JALIUM_OK, "Public backend paragraph draw");
        jalium_pop_transform(target.get());
        Check(jalium_render_target_end_draw(target.get()) == JALIUM_OK, "End actual backend frame");
        std::vector<uint8_t> pixels(Width * Height * 4); int32_t width = 0, height = 0;
        Check(jalium_render_target_fetch_readback(target.get(), pixels.data(), Width * 4, &width, &height) == JALIUM_OK &&
            width == Width && height == Height, "Read actual backend pixels");
        return pixels;
    }
};


// This reference places independently shaped graphemes on an explicit 32px
// grid. It does not use production delegates, vertical classification, metric
// synthesis, source maps, or spacing code. The marked CJK/emoji source ranges
// use CoreText's vertical forms; the remaining graphemes stay horizontal.
std::vector<uint8_t> ReferencePixels(const Example& example, NSString* family, float baseline, double spacing) {
    std::vector<uint8_t> pixels(Width * Height * 4);
    auto space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    auto context = CGBitmapContextCreate(pixels.data(), Width, Height, 8, Width * 4, space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst) | kCGBitmapByteOrder32Little);
    Check(context, "Independent reference bitmap");
    CGContextSetAllowsFontSubpixelPositioning(context, true);
    CGContextSetShouldSubpixelPositionFonts(context, true);
    CGContextSetAllowsFontSubpixelQuantization(context, false);
    CGContextSetShouldSubpixelQuantizeFonts(context, false);
    CGContextSetRGBFillColor(context, 0, 0, 0, 1);
    auto font = CTFontCreateWithName((__bridge CFStringRef)family, 32, nullptr);
    unsigned cell = 0;
    for (NSUInteger at = 0; at < example.text.length;) {
        auto range = [example.text rangeOfComposedCharacterSequenceAtIndex:at];
        at = NSMaxRange(range);
        NSString* piece = [example.text substringWithRange:range];
        if ([piece rangeOfCharacterFromSet:[[NSCharacterSet controlCharacterSet] invertedSet]].location == NSNotFound) continue;
        bool vertical = false;
        for (auto native : example.vertical) vertical |= NSLocationInRange(range.location, native);
        auto text = [[NSAttributedString alloc] initWithString:piece attributes:@{
            (__bridge NSString*)kCTFontAttributeName: (__bridge id)font,
            (__bridge NSString*)kCTVerticalFormsAttributeName: @(vertical),
            (__bridge NSString*)kCTBaselineClassAttributeName: (__bridge id)kCTBaselineClassIdeographicCentered,
            (__bridge NSString*)kCTBaselineReferenceInfoAttributeName: @{
                (__bridge NSString*)kCTBaselineClassIdeographicCentered: @0
            }
        }];
        auto line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)text);
        const double advance = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
        const double top = cell++ * (32 + spacing);
        CGContextSaveGState(context);
        if (vertical) CGContextConcatCTM(context, CGAffineTransformMake(0, -1, 1, 0, 80 - baseline, Height - 12 - top));
        else CGContextTranslateCTM(context, 80 - baseline - advance / 2, Height - 12 - top - 16);
        CGContextSetTextMatrix(context, CGAffineTransformIdentity);
        CGContextSetTextPosition(context, 0, 0); CTLineDraw(line, context);
        CGContextRestoreGState(context); CFRelease(line);
    }
    CFRelease(font); CGContextRelease(context); CGColorSpaceRelease(space);
    return pixels;
}

void Save(const std::vector<uint8_t>& pixels, NSString* name) {
    const char* folder = std::getenv("JALIUM_UPRIGHT_SCREENSHOT_DIR");
    if (!folder) return;
    NSString* root = [[NSString stringWithUTF8String:folder] stringByAppendingPathComponent:metal ? @"Metal" : @"Software"];
    [[NSFileManager defaultManager] createDirectoryAtPath:root withIntermediateDirectories:YES attributes:nil error:nil];
    auto space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    auto context = CGBitmapContextCreate(const_cast<uint8_t*>(pixels.data()), Width, Height, 8, Width * 4, space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst) | kCGBitmapByteOrder32Little);
    auto image = CGBitmapContextCreateImage(context);
    auto destination = CGImageDestinationCreateWithURL((__bridge CFURLRef)[NSURL fileURLWithPath:[root stringByAppendingPathComponent:name]], CFSTR("public.png"), 1, nullptr);
    Check(destination, "Screenshot destination");
    CGImageDestinationAddImage(destination, image, nullptr);
    Check(CGImageDestinationFinalize(destination), "Write screenshot");
    CFRelease(destination); CGImageRelease(image); CGContextRelease(context); CGColorSpaceRelease(space);
}

void Verify(JaliumContext* context, ValidationSurface& surface, NSString* family, const Example& example,
    int index, bool split, bool spaced, int direction) {
    std::u16string source(example.text.length, 0), name(family.length, 0);
    [example.text getCharacters:reinterpret_cast<unichar*>(source.data()) range:NSMakeRange(0, source.size())];
    [family getCharacters:reinterpret_cast<unichar*>(name.data()) range:NSMakeRange(0, name.size())];
    TestOwner<JaliumTextFormat, jalium_text_format_destroy> font(
        jalium_text_format_create(context, Managed(name.c_str()), 32, 400, 0), jalium_text_format_destroy);
    Check(font != nullptr, "Public text format");
    std::vector<JaliumTextSpan> spans;
    if (split) {
        for (uint32_t at = 0; at < source.size(); ++at) spans.push_back({font.get(), at, 1, 0, 0, 0, 1});
    } else spans.push_back({font.get(), 0, static_cast<uint32_t>(source.size()), 0, 0, 0, 1});
    std::vector<JaliumTextAdvanceUnit> units;
    unsigned cells = 0;
    for (NSUInteger at = 0; at < example.text.length;) {
        auto range = [example.text rangeOfComposedCharacterSequenceAtIndex:at];
        auto piece = [example.text substringWithRange:range];
        bool control = [piece rangeOfCharacterFromSet:[[NSCharacterSet controlCharacterSet] invertedSet]].location == NSNotFound;
        units.push_back({static_cast<uint32_t>(at), static_cast<uint32_t>(range.length), spaced ? 6.f : 0.f, 0, 0, control ? 1u : 0u});
        if (!control) ++cells;
        at = NSMaxRange(range);
    }
    TestOwner<JaliumTextParagraph, jalium_text_paragraph_destroy> paragraph(
        split || spaced ? jalium_text_paragraph_create_upright_advanced(font.get(), reinterpret_cast<const uint16_t*>(source.data()),
            source.size(), spans.data(), spans.size(), units.data(), units.size(), 10000, 40, direction, 1)
        : jalium_text_paragraph_create_upright(font.get(), reinterpret_cast<const uint16_t*>(source.data()),
            source.size(), spans.data(), spans.size(), 10000, 40, 0, direction, 1), jalium_text_paragraph_destroy);
    Check(paragraph && jalium_text_paragraph_line_count(paragraph.get()) == 1, "Public upright paragraph");
    JaliumParagraphLineMetrics metrics{};
    Check(jalium_text_paragraph_get_line(paragraph.get(), 0, &metrics) == JALIUM_OK, "Public upright metrics");
    if (std::abs(metrics.line.width - (cells * 32 + (cells - 1) * (spaced ? 6 : 0))) > .01)
        std::fprintf(stderr, "%s case %d split %d spaced %d direction %d\n", family.UTF8String, index, split, spaced, direction);
    Near(metrics.line.width, cells * 32 + (cells - 1) * (spaced ? 6 : 0), "One vertical cell per grapheme and spacing only between cells");
    auto native = dynamic_cast<jalium::MetalTextParagraph*>(reinterpret_cast<jalium::TextParagraph*>(paragraph.get()));
    Check(native, "Apple paragraph rasterizer");
    const float transform[] = {0, 1, -1, 0, 80, 12};
    std::vector<uint8_t> cropped; uint32_t width = 0, height = 0; float x = 0, y = 0;
    Check(native->RasterizeLine(0, 0, 0, 1, transform, Width, Height, cropped, width, height, x, y), "Upright rasterization");
    std::vector<uint8_t> actual(Width * Height * 4);
    for (uint32_t row = 0; row < height; ++row)
        std::copy_n(cropped.data() + row * width * 4, width * 4,
            actual.data() + (static_cast<size_t>(y + row) * Width + static_cast<size_t>(x)) * 4);
    const auto expected = ReferencePixels(example, family, metrics.baseline, spaced ? 6 : 0);
    NSString* prefix = [NSString stringWithFormat:@"%@-%d-%d-%d-%d", family, index, split, spaced, direction];
    Save(actual, [prefix stringByAppendingString:@"-actual.png"]);
    Save(expected, [prefix stringByAppendingString:@"-reference.png"]);
    Check(std::any_of(actual.begin(), actual.end(), [](uint8_t value) { return value != 0; }), "Real upright glyph ink");
    size_t different = 0;
    for (size_t at = 0; at < actual.size(); ++at) different += actual[at] != expected[at];
    if (different) std::fprintf(stderr, "%s: %zu different native bytes\n", prefix.UTF8String, different);
    Check(different == 0, "Upright native pixels match independently positioned graphemes");
    auto backend = surface.Draw(paragraph.get(), transform);
    auto backendExpected = expected;
    if (!metal) for (size_t at = 0; at < backendExpected.size(); at += 4) {
        const unsigned alpha = backendExpected[at + 3];
        if (alpha) for (int channel = 0; channel < 3; ++channel)
            backendExpected[at + channel] = std::min(255u, (unsigned(backendExpected[at + channel]) * 255 + alpha / 2) / alpha);
    }
    different = 0;
    for (size_t at = 0; at < actual.size(); ++at) different += backend[at] != backendExpected[at];
    if (different) std::fprintf(stderr, "%s: %zu different backend bytes\n", prefix.UTF8String, different);
    Check(different == 0, "Public backend upright frame matches the independent reference");
}

void VerticalMetrics(JaliumContext* context, NSString* folder) {
    // Deterministic geometric fonts declare vertical advances of 1200 and
    // 600 units at 1000 units/em, unlike both horizontal widths and 1em.
    for (NSString* family in @[@"Primary", @"Secondary"]) {
        auto path = [folder stringByAppendingPathComponent:[family stringByAppendingString:@".ttf"]];
        auto data = [NSData dataWithContentsOfFile:path];
        Check(data.length > 12, "Vertical metric font fixture");
        std::u16string name(family.length, 0);
        [family getCharacters:reinterpret_cast<unichar*>(name.data()) range:NSMakeRange(0, name.size())];
        TestOwner<JaliumFontResource, jalium_font_resource_release> resource(
            jalium_font_resource_register(Managed(name.c_str()), static_cast<const uint8_t*>(data.bytes), data.length), jalium_font_resource_release);
        Check(resource != nullptr, "Register geometric vertical font");
        TestOwner<JaliumTextFormat, jalium_text_format_destroy> font(
            jalium_text_format_create(context, Managed(name.c_str()), 32, 400, 0), jalium_text_format_destroy);
        Check(font != nullptr, "Custom vertical metric format");
        const std::u16string source = u"H0x";
        JaliumTextSpan span{font.get(), 0, 3, 0, 0, 0, 1};
        TestOwner<JaliumTextParagraph, jalium_text_paragraph_destroy> paragraph(
            jalium_text_paragraph_create_upright(font.get(), reinterpret_cast<const uint16_t*>(source.data()),
                source.size(), &span, 1, 10000, 40, 0, 1, 1), jalium_text_paragraph_destroy);
        Check(paragraph != nullptr, "Custom font upright paragraph");
        const double cell = [family isEqual:@"Primary"] ? 38.4 : 19.2;
        JaliumParagraphLineMetrics metrics{};
        Check(jalium_text_paragraph_get_line(paragraph.get(), 0, &metrics) == JALIUM_OK, "Custom font upright line");
        Near(metrics.line.width, cell * 3, "Upright uses real vertical metrics instead of horizontal widths or 1em");
        for (uint32_t at = 0; at <= 3; ++at) {
            JaliumParagraphCaret caret{};
            Check(jalium_text_paragraph_get_caret(paragraph.get(), 0, at, 0, &caret) == JALIUM_OK, "Custom font source caret");
            Near(caret.x, at * cell, "Source cell follows vertical metric");
        }
    }
}

void Wrapping(JaliumContext* context) {
    TestOwner<JaliumTextFormat, jalium_text_format_destroy> font(
        jalium_text_format_create(context, Managed(u"Arial"), 32, 400, 0), jalium_text_format_destroy);
    const std::u16string source = u"Ae\u0301B";
    JaliumTextSpan span{font.get(), 0, static_cast<uint32_t>(source.size()), 0, 0, 0, 1};
    for (int wrapping : {0, 2}) for (float width : {48.f, 64.f, 65.f}) {
        TestOwner<JaliumTextParagraph, jalium_text_paragraph_destroy> paragraph(
            jalium_text_paragraph_create_upright(font.get(), reinterpret_cast<const uint16_t*>(source.data()),
                source.size(), &span, 1, width, 40, 0, 1, wrapping), jalium_text_paragraph_destroy);
        Check(paragraph != nullptr, "Wrapped upright paragraph");
        Check(jalium_text_paragraph_line_count(paragraph.get()) == (width < 64 ? 3u : 2u), "Wrapping counts complete cells");
        uint32_t seen = 0;
        for (uint32_t row = 0; row < jalium_text_paragraph_line_count(paragraph.get()); ++row) {
            JaliumParagraphLineMetrics metrics{};
            Check(jalium_text_paragraph_get_line(paragraph.get(), row, &metrics) == JALIUM_OK, "Wrapped metrics");
            Check(metrics.line.textPosition == seen && metrics.line.length > 0, "Contiguous original source rows");
            seen += metrics.line.length;
            Check(seen != 2 && metrics.line.textPosition != 2, "Never split a combining sequence");
            Check(metrics.line.width <= width + .01, "Wrapped cell fits inline size");
        }
        Check(seen == source.size(), "Wrapped rows cover the complete original source");
    }
}
}

int main(int argc, const char** argv) { @autoreleasepool {
    metal = argc > 1 && std::string(argv[1]) == "--metal";
    try {
        [NSApplication sharedApplication];
        TestOwner<JaliumContext, jalium_context_destroy> context(jalium_context_create(metal ? JALIUM_BACKEND_METAL : JALIUM_BACKEND_SOFTWARE), jalium_context_destroy);
        Check(context != nullptr, "Apple native context");
        ValidationSurface surface(context.get());
        const Example examples[] = {
            {@"Ae\u0301fiאבب👩‍💻中", {{8, 6}}},
            {@"b\u0308\u0323Ａaאבب", {{3, 1}}},
            {@"ffi123", {}},
            {@"1️⃣👩‍💻中", {{0, 9}}},
            {@"A\u2067אב\u2069Z", {}},
        };
        for (NSString* family in @[@"Hiragino Sans", @"Arial", @"Times New Roman", @"Georgia"])
            for (int index = 0; index < 5; ++index)
                for (bool split : {false, true}) for (bool spaced : {false, true}) for (int direction : {0, 1})
                    Verify(context.get(), surface, family, examples[index], index, split, spaced, direction);
        Wrapping(context.get());
        Check(argc > 2, "Vertical metric fixture directory argument");
        VerticalMetrics(context.get(), [NSString stringWithUTF8String:argv[2]]);
        std::printf("PASS upright: %d checks; 160 independent CoreText/native/public-backend pixel pairs, wrapping and intact/split UTF-16 spans\n", checks);
        return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "FAIL upright: %s\n", error.what()); return 1; }
} }
