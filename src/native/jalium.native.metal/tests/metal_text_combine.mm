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
bool metal = false; int dpi = 1;
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
constexpr int Width = 128, Height = 128;
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
        target.reset(jalium_render_target_create_for_surface(context, &descriptor, Width*dpi, Height*dpi));
        Check(target != nullptr, "Actual native surface target");
        jalium_render_target_set_dpi(target.get(), 96*dpi, 96*dpi);
    }
    std::vector<uint8_t> Draw(JaliumTextParagraph* paragraph, const float* transform) {
        Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK, "Request actual backend readback");
        Check(jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "Begin actual backend frame");
        jalium_render_target_clear(target.get(), 0, 0, 0, 0);
        jalium_push_transform(target.get(), transform);
        Check(jalium_render_target_draw_paragraph_line(target.get(), paragraph, 0, 0, 0, 1) == JALIUM_OK, "Public backend paragraph draw");
        jalium_pop_transform(target.get());
        Check(jalium_render_target_end_draw(target.get()) == JALIUM_OK, "End actual backend frame");
        std::vector<uint8_t> pixels(Width * Height * dpi * dpi * 4); int32_t width = 0, height = 0;
        Check(jalium_render_target_fetch_readback(target.get(), pixels.data(), Width * dpi * 4, &width, &height) == JALIUM_OK &&
            width == Width*dpi && height == Height*dpi, "Read actual backend pixels");
        return pixels;
    }
};


void Save(const std::vector<uint8_t>& pixels, NSString* name) {
    const char* folder = std::getenv("JALIUM_COMBINE_SCREENSHOT_DIR");
    if (!folder) return;
    NSString* root = [[NSString stringWithUTF8String:folder] stringByAppendingPathComponent:metal ? @"Metal" : @"Software"];
    [[NSFileManager defaultManager] createDirectoryAtPath:root withIntermediateDirectories:YES attributes:nil error:nil];
    auto space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    auto context = CGBitmapContextCreate(const_cast<uint8_t*>(pixels.data()), Width*dpi, Height*dpi, 8, Width*dpi*4, space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst) | kCGBitmapByteOrder32Little);
    auto image = CGBitmapContextCreateImage(context);
    auto destination = CGImageDestinationCreateWithURL((__bridge CFURLRef)[NSURL fileURLWithPath:[root stringByAppendingPathComponent:name]], CFSTR("public.png"), 1, nullptr);
    Check(destination, "Screenshot destination");
    CGImageDestinationAddImage(destination, image, nullptr);
    Check(CGImageDestinationFinalize(destination), "Write screenshot");
    CFRelease(destination); CGImageRelease(image); CGContextRelease(context); CGColorSpaceRelease(space);
}

// Literal glyph IDs, advances and baseline metrics come from our original
// geometric font generator. This reference uses no production text-combine
// selection, run delegate, source map, width-feature or compression helper.
std::vector<uint8_t> Reference(NSData* data, NSString* name, NSString* source, float baseline, int orientation) {
    auto provider = CGDataProviderCreateWithCFData((__bridge CFDataRef)data);
    auto graphics = CGFontCreateWithDataProvider(provider); CGDataProviderRelease(provider);
    auto font = CTFontCreateWithGraphicsFont(graphics, 32, nullptr, nullptr); CGFontRelease(graphics);
    Check(font != nullptr, "Independent original reference face");
    NSString* text = [[source stringByReplacingOccurrencesOfString:@"\r" withString:@""] stringByReplacingOccurrencesOfString:@"\n" withString:@""];
    std::vector<CGGlyph> glyphs;
    for (NSUInteger at = 0; at < text.length; ++at) {
        unichar c = [text characterAtIndex:at];
        if (c == 0xfeff || c == 0x00ad || c == 0x200b) continue;
        if (text.length > 1 && c >= 0xff01 && c <= 0xff5e) c -= 0xfee0;
        glyphs.push_back(c == ' ' ? 6 : c == '1' || c == 'A' || c == 0xff11 ? 1 : 2);
    }
    const auto n = glyphs.size();
    bool partial = [name isEqual:@"CombinePartial"];
    bool allCovered = std::all_of(glyphs.begin(), glyphs.end(), [](auto gid) { return gid == 1 || gid == 2; }) &&
        (!partial || std::all_of(glyphs.begin(), glyphs.end(), [](auto gid) { return gid == 1; }));
    int selected = 0;
    if (n > 1 && allCovered) {
        if ([name isEqual:@"CombineWidths"] || partial) selected = n == 2 ? 3 : n == 3 ? 4 : 5;
        else if ([name isEqual:@"CombineHalf"]) selected = 3;
    }
    const bool onlySpaces = std::all_of(glyphs.begin(), glyphs.end(), [](auto gid) { return gid == 6; });
    const double step = onlySpaces ? 8 : selected == 3 ? 16 : selected == 4 ? 10.656 : selected == 5 ? 8 : [name isEqual:@"CombineNarrow"] ? 12.8 : 32;
    if (selected) std::fill(glyphs.begin(), glyphs.end(), static_cast<CGGlyph>(selected));
    double advance = n * step, scale = advance > 32 ? 32 / advance : 1;
    std::vector<CGPoint> positions;
    for (size_t i = 0; i < n; ++i) positions.push_back(CGPointMake(i * step, 0));
    std::vector<uint8_t> pixels(Width * Height * dpi * dpi * 4);
    auto space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    auto context = CGBitmapContextCreate(pixels.data(), Width*dpi, Height*dpi, 8, Width*dpi*4, space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst) | kCGBitmapByteOrder32Little);
    CGColorSpaceRelease(space); Check(context != nullptr, "Independent reference bitmap");
    CGContextSetAllowsFontSubpixelPositioning(context, true); CGContextSetShouldSubpixelPositionFonts(context, true);
    CGContextSetAllowsFontSubpixelQuantization(context, false); CGContextSetShouldSubpixelQuantizeFonts(context, false);
    CGContextSetRGBFillColor(context, 0, 0, 0, 1);
    CGContextScaleCTM(context, dpi, dpi);
    CGContextTranslateCTM(context, 80 - baseline + (orientation == 2 ? 8 : 0) - advance * scale / 2, Height - 12 - 24);
    CGContextScaleCTM(context, scale, 1);
    CTFontDrawGlyphs(font, glyphs.data(), positions.data(), glyphs.size(), context);
    CGContextRelease(context); CFRelease(font); return pixels;
}

void Verify(JaliumContext* context, ValidationSurface& surface, NSString* folder, NSString* name,
    NSString* sourceText, int orientation, int direction, bool split) {
    auto data = [NSData dataWithContentsOfFile:[folder stringByAppendingPathComponent:[name stringByAppendingString:@".ttf"]]];
    Check(data.length > 12, "Original text-combine fixture");
    std::u16string family(name.length, 0), source(sourceText.length, 0);
    [name getCharacters:reinterpret_cast<unichar*>(family.data()) range:NSMakeRange(0, family.size())];
    [sourceText getCharacters:reinterpret_cast<unichar*>(source.data()) range:NSMakeRange(0, source.size())];
    TestOwner<JaliumFontResource, jalium_font_resource_release> resource(jalium_font_resource_register(
        Managed(family.c_str()), static_cast<const uint8_t*>(data.bytes), data.length), jalium_font_resource_release);
    Check(resource != nullptr, "Register text-combine font");
    TestOwner<JaliumTextFormat, jalium_text_format_destroy> font(jalium_text_format_create(context, Managed(family.c_str()), 32, 400, 0), jalium_text_format_destroy);
    Check(font != nullptr, "Text-combine source format");
    std::vector<JaliumTextSpan> spans;
    if (split) for (uint32_t at = 0; at < source.size(); ++at) spans.push_back({font.get(), at, 1, 0, 0, 0, 1});
    else spans.push_back({font.get(), 0, static_cast<uint32_t>(source.size()), 0, 0, 0, 1});
    JaliumTextCombineUnit unit{0, static_cast<uint32_t>(source.size()), 32, direction};
    TestOwner<JaliumTextParagraph, jalium_text_paragraph_destroy> paragraph(jalium_text_paragraph_create_combined(font.get(),
        reinterpret_cast<const uint16_t*>(source.data()), source.size(), spans.data(), spans.size(), &unit, 1,
        nullptr, 0, 1000, 40, 0, direction, 1, orientation, 0), jalium_text_paragraph_destroy);
    Check(paragraph != nullptr, "Public combined paragraph"); Check(jalium_text_paragraph_line_count(paragraph.get()) == 1, "Forced breaks stay inside composition");
    JaliumParagraphLineMetrics metrics{};
    Check(jalium_text_paragraph_get_line(paragraph.get(), 0, &metrics) == JALIUM_OK, "Combined line metrics");
    Near(metrics.line.width, 32, "Composition always measures one em, including a 1.2em vertical font");
    Check(metrics.line.textPosition == 0 && metrics.line.length == source.size(), "Line retains complete original source");
    for (uint32_t at = 0; at <= source.size(); ++at) {
        JaliumParagraphCaret caret{};
        Check(jalium_text_paragraph_get_caret(paragraph.get(), 0, at, 0, &caret) == JALIUM_OK, "Original source caret");
        Check(caret.textPosition == 0 || caret.textPosition == source.size(), "No interior atomic caret");
        Near(caret.x, at == source.size() ? 32 : 0, "Caret snaps to original composition edges");
    }
    const float transform[] = {0, 1, -1, 0, 80, 12};
    auto expected = Reference(data, name, sourceText, metrics.baseline, orientation);
    auto native = reinterpret_cast<jalium::MetalTextParagraph*>(paragraph.get());
    std::vector<uint8_t> tile; uint32_t w = 0, h = 0; float x = 0, y = 0;
    float scaled[6]; for (int i = 0; i < 6; ++i) scaled[i] = transform[i] * dpi;
    bool hasInk = false; for (size_t i = 3; i < expected.size(); i += 4) hasInk |= expected[i] != 0;
    const bool rasterized = native->RasterizeLine(0, 0, 0, 1, scaled, Width*dpi, Height*dpi, tile, w, h, x, y);
    Check(rasterized || !hasInk, "Rasterize combined immutable line, allowing an empty whitespace composition");
    std::vector<uint8_t> actual(Width * Height * dpi * dpi * 4);
    for (uint32_t row = 0; row < h; ++row) std::copy_n(tile.data() + row*w*4, w*4, actual.data() + ((static_cast<unsigned>(y)+row)*Width*dpi+static_cast<unsigned>(x))*4);
    NSString* prefix = [NSString stringWithFormat:@"%@-%lu-%d-%d-%d-%d", name, static_cast<unsigned long>(std::hash<std::u16string>{}(source)), orientation, direction, split, dpi];
    Save(actual, [prefix stringByAppendingString:@"-actual.png"]); Save(expected, [prefix stringByAppendingString:@"-reference.png"]);
    auto different = size_t(0); for (size_t i = 0; i < actual.size(); ++i) different += actual[i] != expected[i];
    if (different) std::fprintf(stderr, "%s: %zu different reference bytes\n", prefix.UTF8String, different);
    Check(different == 0, "Combined ink matches literal original glyph reference");
    auto backend = surface.Draw(paragraph.get(), transform);
    different = 0; for (size_t i = 0; i < backend.size(); ++i) different += backend[i] != expected[i];
    if (different) std::fprintf(stderr, "%s: %zu different backend bytes\n", prefix.UTF8String, different);
    Check(different == 0, "Public native backend matches literal composition reference");
}

void WrapAndValidate(JaliumContext* context) {
    TestOwner<JaliumTextFormat, jalium_text_format_destroy> font(jalium_text_format_create(context, Managed(u"Arial"), 32, 400, 0), jalium_text_format_destroy);
    std::u16string source = u"A1234B";
    JaliumTextSpan span{font.get(), 0, static_cast<uint32_t>(source.size()), 0, 0, 0, 1};
    JaliumTextCombineUnit unit{1, 4, 32, 0};
    for (int orientation : {0, 1, 2}) for (float width : {1.f, 31.f, 32.f, 40.f, 64.f}) {
        TestOwner<JaliumTextParagraph, jalium_text_paragraph_destroy> paragraph(jalium_text_paragraph_create_combined(font.get(),
            reinterpret_cast<const uint16_t*>(source.data()), source.size(), &span, 1, &unit, 1, nullptr, 0, width, 40, 0, 0, 2, orientation, 0), jalium_text_paragraph_destroy);
        Check(paragraph != nullptr, "Combined character-wrapping paragraph");
        uint32_t seen = 0;
        for (uint32_t line = 0; line < jalium_text_paragraph_line_count(paragraph.get()); ++line) {
            JaliumParagraphLineMetrics metrics{}; Check(jalium_text_paragraph_get_line(paragraph.get(), line, &metrics) == JALIUM_OK, "Combined wrapped metrics");
            Check(metrics.line.textPosition == seen && metrics.line.length > 0, "Contiguous original wrapped rows");
            seen += metrics.line.length; Check(seen <= 1 || seen >= 5, "Never split a combined run");
        }
        Check(seen == source.size(), "Wrapped source coverage");
    }
    for (JaliumTextCombineUnit bad : {JaliumTextCombineUnit{7, 1, 32, 0}, {0, 0, 32, 0}, {0, 1, -1, 0}, {0, 1, 32, 4}})
        Check(jalium_text_paragraph_create_combined(font.get(), reinterpret_cast<const uint16_t*>(source.data()), source.size(), &span, 1,
            &bad, 1, nullptr, 0, 100, 40, 0, 0, 1, 0, 0) == nullptr, "Invalid combined input rejected");
}
}

int main(int argc, const char** argv) { @autoreleasepool {
    metal = argc > 1 && std::string(argv[1]) == "--metal";
    try {
        [NSApplication sharedApplication]; Check(argc > 2, "Text-combine fixture folder");
        TestOwner<JaliumContext, jalium_context_destroy> context(jalium_context_create(metal ? JALIUM_BACKEND_METAL : JALIUM_BACKEND_SOFTWARE), jalium_context_destroy);
        Check(context != nullptr, "Text-combine native context");
        unsigned pairs = 0;
        for (int scale : {1, 2}) { dpi = scale; ValidationSurface surface(context.get());
        for (NSString* name in @[@"CombineWidths", @"CombineHalf", @"CombinePartial", @"CombineNone", @"CombineEmpty", @"CombineNarrow"])
        for (NSString* source in @[@"1", @"12", @"11", @"123", @"1234", @"12345", @"AB", @"A\r\nB", @"１２", @"\ufeff12\ufeff", @"1\u00ad2", @"1\u200b2", @" ", @"   "])
        for (int orientation : {0, 1, 2}) for (int direction : {0, 1}) for (bool split : {false, true}) {
            Verify(context.get(), surface, [NSString stringWithUTF8String:argv[2]], name, source, orientation, direction, split); ++pairs;
        }
        }
        WrapAndValidate(context.get());
        std::printf("PASS text-combine: %d checks, %u independent literal glyph and actual-backend pixel pairs\n", checks, pairs);
        return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "FAIL text-combine: %s\n", error.what()); return 1; }
} }

