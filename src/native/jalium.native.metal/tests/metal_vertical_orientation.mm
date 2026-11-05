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
struct Example { NSString* text; std::vector<NSRange> upright; };
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


// Explicit source ranges come from CSS Writing Modes / UAX #50 examples.
// This reference does not call the production classifier or its generated data.
CTLineRef Reference(NSString* source, NSString* family, const std::vector<NSRange>& upright) {
    auto font = CTFontCreateWithName((__bridge CFStringRef)family, 32, nullptr);
    auto text = [[NSMutableAttributedString alloc] initWithString:source attributes:@{
        (__bridge NSString*)kCTFontAttributeName: (__bridge id)font,
        (__bridge NSString*)kCTVerticalFormsAttributeName: @NO,
        (__bridge NSString*)kCTBaselineClassAttributeName: (__bridge id)kCTBaselineClassIdeographicCentered,
        (__bridge NSString*)kCTBaselineReferenceInfoAttributeName: @{
            (__bridge NSString*)kCTBaselineClassIdeographicCentered: @0
        }
    }];
    for (const auto& range : upright)
        [text addAttribute:(__bridge NSString*)kCTVerticalFormsAttributeName value:@YES range:range];
    auto line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)text);
    CFRelease(font);
    return line;
}

std::vector<uint8_t> ReferencePixels(CTLineRef line, float baseline) {
    std::vector<uint8_t> pixels(Width * Height * 4);
    auto space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    auto context = CGBitmapContextCreate(pixels.data(), Width, Height, 8, Width * 4, space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst) | kCGBitmapByteOrder32Little);
    Check(context, "Reference bitmap context");
    CGContextSetAllowsFontSubpixelPositioning(context, true);
    CGContextSetShouldSubpixelPositionFonts(context, true);
    CGContextSetAllowsFontSubpixelQuantization(context, false);
    CGContextSetShouldSubpixelQuantizeFonts(context, false);
    CGContextSetRGBFillColor(context, 0, 0, 0, 1);
    CGContextConcatCTM(context, CGAffineTransformMake(0, -1, 1, 0, 80 - baseline, Height - 12));
    CGContextSetTextMatrix(context, CGAffineTransformIdentity);
    CGContextSetTextPosition(context, 0, 0);
    CTLineDraw(line, context);
    CGContextRelease(context); CGColorSpaceRelease(space);
    return pixels;
}

void Save(const std::vector<uint8_t>& pixels, NSString* name) {
    const char* folder = std::getenv("JALIUM_VERTICAL_ORIENTATION_SCREENSHOT_DIR");
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

void Verify(JaliumContext* context, ValidationSurface& surface, NSString* family, const Example& example, int index, bool split) {
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
    for (NSUInteger at = 0; at < example.text.length;) {
        auto cluster = [example.text rangeOfComposedCharacterSequenceAtIndex:at];
        units.push_back({static_cast<uint32_t>(at), static_cast<uint32_t>(cluster.length), 0, 0, 0, 0});
        at = NSMaxRange(cluster);
    }
    TestOwner<JaliumTextParagraph, jalium_text_paragraph_destroy> paragraph(
        split ? jalium_text_paragraph_create_vertical_advanced(font.get(), reinterpret_cast<const uint16_t*>(source.data()),
            source.size(), spans.data(), spans.size(), units.data(), units.size(), 10000, 40, 0, 1)
        : jalium_text_paragraph_create_vertical(font.get(), reinterpret_cast<const uint16_t*>(source.data()),
            source.size(), spans.data(), spans.size(), 10000, 40, 0, 0, 1),
        jalium_text_paragraph_destroy);
    Check(paragraph && jalium_text_paragraph_line_count(paragraph.get()) == 1, "Public vertical paragraph");
    JaliumParagraphLineMetrics metrics{};
    Check(jalium_text_paragraph_get_line(paragraph.get(), 0, &metrics) == JALIUM_OK, "Public line metrics");
    auto reference = Reference(example.text, family, example.upright);
    Near(metrics.line.width, CTLineGetTypographicBounds(reference, nullptr, nullptr, nullptr), "Mixed advance matches independent CoreText");
    auto native = dynamic_cast<jalium::MetalTextParagraph*>(reinterpret_cast<jalium::TextParagraph*>(paragraph.get()));
    Check(native, "Apple paragraph rasterizer");
    const float transform[] = {0, 1, -1, 0, 80, 12};
    std::vector<uint8_t> cropped; uint32_t width = 0, height = 0; float x = 0, y = 0;
    Check(native->RasterizeLine(0, 0, 0, 1, transform, Width, Height, cropped, width, height, x, y), "Actual paragraph rasterization");
    std::vector<uint8_t> actual(Width * Height * 4);
    for (uint32_t row = 0; row < height; ++row)
        std::copy_n(cropped.data() + row * width * 4, width * 4,
            actual.data() + (static_cast<size_t>(y + row) * Width + static_cast<size_t>(x)) * 4);
    auto expected = ReferencePixels(reference, metrics.baseline);
    Save(actual, [NSString stringWithFormat:@"%@-%d-%d-actual.png", family, index, split]);
    Save(expected, [NSString stringWithFormat:@"%@-%d-%d-reference.png", family, index, split]);
    Check(std::any_of(actual.begin(), actual.end(), [](uint8_t value) { return value != 0; }), "Real glyph ink");
    size_t different = 0;
    for (size_t at = 0; at < actual.size(); ++at) different += actual[at] != expected[at];
    if (different) std::fprintf(stderr, "%s case %d split %d: %zu different bytes\n", family.UTF8String, index, split, different);
    Check(different == 0, "Actual glyph pixels match the independently shaped orientation and baseline");
    auto backendPixels = surface.Draw(paragraph.get(), transform);
    auto backendImage = backendPixels;
    if (!metal) for (size_t at = 0; at < backendImage.size(); at += 4)
        for (int channel = 0; channel < 3; ++channel)
            backendImage[at + channel] = (unsigned(backendImage[at + channel]) * backendImage[at + 3] + 127) / 255;
    Save(backendImage, [NSString stringWithFormat:@"%@-%d-%d-backend.png", family, index, split]);
    // Software readback stores straight BGRA; CoreText and Metal return
    // premultiplied BGRA. Compare the actual public representations correctly.
    auto backendExpected = expected;
    if (!metal) for (size_t at = 0; at < backendExpected.size(); at += 4) {
        const unsigned alpha = backendExpected[at + 3];
        if (alpha) for (int channel = 0; channel < 3; ++channel)
            backendExpected[at + channel] = std::min(255u, (unsigned(backendExpected[at + channel]) * 255 + alpha / 2) / alpha);
    }
    different = 0;
    for (size_t at = 0; at < actual.size(); ++at) different += backendPixels[at] != backendExpected[at];
    if (different) std::fprintf(stderr, "%s case %d split %d: %zu different backend bytes\n", family.UTF8String, index, split, different);
    Check(different == 0, "Public backend frame matches the independent CoreText pixels");

    uint32_t count = 0;
    Check(jalium_text_paragraph_get_fragments(paragraph.get(), 0, nullptr, 0, &count) == JALIUM_OK && count, "Public fragments");
    std::vector<JaliumTextFragmentMetrics> fragments(count);
    Check(jalium_text_paragraph_get_fragments(paragraph.get(), 0, fragments.data(), count, &count) == JALIUM_OK, "Copy fragments");
    double last = 0;
    for (const auto& fragment : fragments) {
        Check(fragment.x >= -.01 && fragment.width >= 0 && fragment.x + fragment.width <= metrics.line.width + .01,
            "Fragment remains in logical inline coordinates");
        last = std::max(last, static_cast<double>(fragment.x + fragment.width));
    }
    Near(last, metrics.line.width, "Fragments reach the final inline edge");
    CFRelease(reference);
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
            {@"ABfi e\u0301中（12）", {{7, 2}, {11, 1}}},
            {@"天地玄黄", {{0, 4}}},
            {@"fi e\u0301אבب", {}},
            {@"1️⃣👩‍💻中", {{0, 9}}},
            {@"A\u2067אב\u2069漢", {{5, 1}}},
        };
        for (NSString* family in @[@"Hiragino Sans", @"Arial", @"Times New Roman", @"Georgia"])
            for (int index = 0; index < 5; ++index)
                for (bool split : {false, true}) Verify(context.get(), surface, family, examples[index], index, split);
        std::printf("PASS mixed vertical orientation: %d checks; 40 independent CoreText and public-backend pixel pairs, intact/split UTF-16 spans\n", checks);
        return 0;
    } catch (const std::exception& error) {
        std::fprintf(stderr, "FAIL mixed vertical orientation: %s\n", error.what()); return 1;
    }
} }
