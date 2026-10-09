#include "jalium_api.h"
#include "../src/metal_text_paragraph.h"
#import <AppKit/AppKit.h>
#include <cmath>
#include <algorithm>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>
#include <set>
#include <vector>

namespace {
int checks = 0;
float requestedWidth = 100; int requestedWeight = 400, requestedStyle = 0;
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); ++checks; }
const wchar_t* Managed(const char16_t* text) { return reinterpret_cast<const wchar_t*>(text); }
using Format = std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)>;
Format Font(JaliumContext* context, const char16_t* family, float width, int weight = 400, int style = 0, float size = 20)
{
    Format result(jalium_text_format_create_with_width(context, Managed(family), size, weight, style, width), jalium_text_format_destroy);
    Check(result != nullptr, "Width-aware format creation failed");
    requestedWidth = width; requestedWeight = weight; requestedStyle = style;
    jalium_text_format_set_word_wrapping(result.get(), 1); return result;
}
CGFloat Weight(int weight)
{
    constexpr CGFloat values[] = {-1, -.8, -.6, -.4, 0, .23, .3, .4, .56, .62, 1};
    int lower = std::min(weight / 100, 9); return values[lower] + (values[lower + 1] - values[lower]) * (weight - lower * 100) / 100;
}
CTFontRef Varied(CTFontRef base, float width, NSNumber* weight = nil)
{
    NSMutableDictionary* variation = [CFBridgingRelease(CTFontCopyVariation(base)) mutableCopy] ?: [NSMutableDictionary new];
    variation[@(0x77647468)] = @(std::clamp(width, 30.f, 150.f));
    if (weight) variation[@(0x77676874)] = weight;
    CTFontDescriptorRef descriptor = CTFontDescriptorCreateWithAttributes((__bridge CFDictionaryRef)@{(__bridge id)kCTFontVariationAttribute:variation});
    CTFontRef result = CTFontCreateCopyWithAttributes(base, CTFontGetSize(base), nullptr, descriptor); CFRelease(descriptor); return result;
}
void Compare(JaliumTextFormat* actual, CTFontRef expected)
{
    const std::u16string sample = u"MMMM iii1 abc אבג xyz tail";
    NSString* text = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(sample.data()) length:sample.size()];
    NSAttributedString* attributed = [[NSAttributedString alloc] initWithString:text attributes:@{NSFontAttributeName:(__bridge id)expected}];
    CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)attributed);
    double width = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
    JaliumTextMetrics metrics{};
    const auto measured = jalium_text_format_measure_text(actual, Managed(sample.c_str()), sample.size(), 100000, 1000, &metrics);
    if (measured != JALIUM_OK || std::abs(metrics.width - width) >= .001 || std::abs(metrics.ascent - CTFontGetAscent(expected)) >= .001)
        std::fprintf(stderr, "Width reference failure: request=%g weight=%d style=%d expected=%s %.6f/%.6f actual=%.6f/%.6f status=%d\n",
            requestedWidth, requestedWeight, requestedStyle, [CFBridgingRelease(CTFontCopyPostScriptName(expected)) UTF8String],
            width, CTFontGetAscent(expected), metrics.width, metrics.ascent, measured);
    Check(measured == JALIUM_OK &&
        std::abs(metrics.width - width) < .001 && std::abs(metrics.ascent - CTFontGetAscent(expected)) < .001, "Font width measurement differs from CoreText reference");
    JaliumFontUnitMetrics units{sizeof(JaliumFontUnitMetrics)};
    Check(jalium_text_format_get_font_unit_metrics(actual, &units) == JALIUM_OK && (units.available & 127) == 127 &&
        std::abs(units.xHeight - CTFontGetXHeight(expected)) < .001 &&
        std::abs(units.capHeight - CTFontGetCapHeight(expected)) < .001 &&
        std::abs(units.underlinePosition + CTFontGetUnderlinePosition(expected)) < .001 &&
        std::abs(units.underlineThickness - CTFontGetUnderlineThickness(expected)) < .001, "Font unit metrics differ from CoreText");
    for (NSString* character in @[@"0", @"水"]) {
        NSAttributedString* single = [[NSAttributedString alloc] initWithString:character attributes:@{NSFontAttributeName:(__bridge id)expected}];
        CTLineRef unit = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)single);
        double reference = CTLineGetTypographicBounds(unit, nullptr, nullptr, nullptr);
        Check(std::abs((character.length == 1 && [character characterAtIndex:0] == '0' ? units.zeroAdvance : units.ideographicAdvance) - reference) < .001,
            "Font-relative advance differs from same-fallback CoreText reference"); CFRelease(unit);
    }
    JaliumTextSpan span{actual, 0, static_cast<uint32_t>(sample.size()), 0, 0, 0, 1};
    auto paragraph = jalium_text_paragraph_create_with_wrapping(actual, reinterpret_cast<const uint16_t*>(sample.data()),
        sample.size(), &span, 1, 100000, 48, 0, 0, 1);
    Check(paragraph != nullptr, "Width paragraph creation failed");
    for (uint32_t position = 0; position <= sample.size(); ++position) {
        CGFloat secondary = 0; CGFloat primary = CTLineGetOffsetForStringIndex(line, position, &secondary);
        JaliumParagraphCaret caret{};
        Check(jalium_text_paragraph_get_caret(paragraph, 0, position, 0, &caret) == JALIUM_OK &&
            (std::abs(caret.x - primary) < .001 || std::abs(caret.x - secondary) < .001), "Width paragraph caret differs from CoreText");
    }
    jalium_text_paragraph_destroy(paragraph); CFRelease(line);
}

void ComparePixels(JaliumTextFormat* format, CTFontRef reference, int dpi, bool transformed)
{
    const std::u16string sample = u"MMMM iii1 abc אבג xyz tail";
    NSString* text = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(sample.data()) length:sample.size()];
    CGColorRef color = CGColorCreateGenericRGB(0, 0, 0, 1);
    NSAttributedString* attributed = [[NSAttributedString alloc] initWithString:text attributes:@{
        (__bridge id)kCTFontAttributeName:(__bridge id)reference,
        (__bridge id)kCTForegroundColorAttributeName:(__bridge id)color}];
    CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)attributed);
    JaliumTextSpan span{format, 0, static_cast<uint32_t>(sample.size()), 0, 0, 0, 1};
    auto paragraph = jalium_text_paragraph_create_with_wrapping(format, reinterpret_cast<const uint16_t*>(sample.data()),
        sample.size(), &span, 1, 100000, 48, 0, 0, 1);
    Check(paragraph != nullptr, "Width pixel paragraph creation failed");
    auto* native = dynamic_cast<jalium::MetalTextParagraph*>(reinterpret_cast<jalium::TextParagraph*>(paragraph));
    Check(native != nullptr, "Width pixel paragraph provider unavailable");
    float matrix[] = {float(dpi), transformed ? .2f * dpi : 0, transformed ? -.15f * dpi : 0, float(dpi), 6, 9};
    std::vector<uint8_t> actual; uint32_t width{}, height{}; float left{}, top{};
    Check(native->RasterizeLine(0, 12, 12, 1, matrix, 720 * dpi, 192 * dpi, actual, width, height, left, top),
        "Width pixel rasterization failed");
    JaliumParagraphLineMetrics metrics{};
    Check(jalium_text_paragraph_get_line(paragraph, 0, &metrics) == JALIUM_OK, "Width pixel baseline unavailable");
    std::vector<uint8_t> expected(width * height * 4);
    CGColorSpaceRef space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGContextRef context = CGBitmapContextCreate(expected.data(), width, height, 8, width * 4, space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst) | static_cast<CGBitmapInfo>(kCGBitmapByteOrder32Little));
    Check(context != nullptr, "Width reference pixel context unavailable");
    const float baselineX = 12, baselineY = 12 + metrics.baseline;
    CGAffineTransform transform = CGAffineTransformMake(matrix[0], -matrix[1], -matrix[2], matrix[3],
        matrix[0] * baselineX + matrix[2] * baselineY + matrix[4] - left,
        height - (matrix[1] * baselineX + matrix[3] * baselineY + matrix[5] - top));
    CGContextConcatCTM(context, transform); CGContextSetTextMatrix(context, CGAffineTransformIdentity);
    CGContextSetTextPosition(context, 0, 0); CTLineDraw(line, context);
    Check(actual == expected && std::count_if(actual.begin(), actual.end(), [](uint8_t value) { return value != 0; }) > 100,
        "Width paragraph glyph pixels differ from independent CTLineDraw");
    CGContextRelease(context); CGColorSpaceRelease(space); CFRelease(line); CGColorRelease(color);
    jalium_text_paragraph_destroy(paragraph);
}
}

int TestFontWidths(JaliumContext* context)
{
    for (float invalid : {-1.f, std::numeric_limits<float>::infinity(), std::numeric_limits<float>::quiet_NaN()})
        Check(jalium_text_format_create_with_width(context, Managed(u"Helvetica Neue"), 20, 400, 0, invalid) == nullptr, "Invalid width was accepted");
    Check(jalium_text_format_create_with_width(nullptr, Managed(u"SF Pro"), 20, 400, 0, 75) == nullptr &&
        jalium_text_format_create_with_width(context, nullptr, 20, 400, 0, 75) == nullptr, "Null width creation arguments accepted");
    for (float width : {0.f, 30.f, 50.f, 62.5f, 75.f, 87.5f, 90.25f, 99.f, 100.f, 101.f, 112.5f, 125.f, 150.f, 175.f, 200.f, 5000.f})
        for (int weight : {100, 400, 650, 700, 900}) for (int style : {0, 1}) {
            NSFont* system = [NSFont systemFontOfSize:20 weight:Weight(weight)];
            if (style && width == 100) system = [NSFontManager.sharedFontManager convertFont:system toHaveTrait:NSItalicFontMask];
            CTFontRef reference = width == 100 ? static_cast<CTFontRef>(CFRetain((__bridge CTFontRef)system)) : Varied((__bridge CTFontRef)system, width);
            if (style && width != 100) {
                CGAffineTransform slant = CGAffineTransformMake(1, 0, std::tan(12.0 * M_PI / 180), 1, 0, 0);
                CTFontRef italic = CTFontCreateCopyWithAttributes(reference, 20, &slant, nullptr); CFRelease(reference); reference = italic;
            }
            auto actual = Font(context, u"SF Pro", width, weight, style); Compare(actual.get(), reference); CFRelease(reference);
        }
    for (float width : {75.f, 125.f}) for (int weight : {400, 650}) for (int style : {0, 1}) {
        NSFont* system = [NSFont systemFontOfSize:20 weight:Weight(weight)];
        CTFontRef reference = Varied((__bridge CTFontRef)system, width);
        if (style) {
            CGAffineTransform slant = CGAffineTransformMake(1, 0, std::tan(12.0 * M_PI / 180), 1, 0, 0);
            CTFontRef italic = CTFontCreateCopyWithAttributes(reference, 20, &slant, nullptr); CFRelease(reference); reference = italic;
        }
        auto actual = Font(context, u"SF Pro", width, weight, style);
        for (int dpi : {1, 2}) for (bool transformed : {false, true}) ComparePixels(actual.get(), reference, dpi, transformed);
        CFRelease(reference);
    }
    std::printf("Width glyph reference: 32 paragraph pixel comparisons with independent CTLineDraw at 1x/2x, including affine transforms\n");
    for (float width : {50.f, 75.f, 87.5f, 99.f}) for (int weight : {400, 700, 900}) {
        auto actual = Font(context, u"Helvetica Neue", width, weight);
        NSFont* reference = [NSFont fontWithName:weight == 900 ? @"HelveticaNeue-CondensedBlack" : @"HelveticaNeue-CondensedBold" size:20];
        Compare(actual.get(), (__bridge CTFontRef)reference);
    }
    // A family without width variants retains its actual face; no bitmap scaling.
    for (float width : {0.f, 75.f, 125.f, 5000.f}) {
        auto actual = Font(context, u"Menlo", width); Compare(actual.get(), (__bridge CTFontRef)[NSFont fontWithName:@"Menlo" size:20]);
    }
    NSFont* system = [NSFont systemFontOfSize:20];
    NSURL* url = CFBridgingRelease(CTFontCopyAttribute((__bridge CTFontRef)system, kCTFontURLAttribute));
    NSData* data = [NSData dataWithContentsOfURL:url];
    auto resource = jalium_font_resource_register(Managed(u"PrivateWidth44"), static_cast<const uint8_t*>(data.bytes), data.length);
    Check(resource != nullptr, "Private width fixture failed");
    CGDataProviderRef provider = CGDataProviderCreateWithCFData((__bridge CFDataRef)data);
    CGFontRef graphics = CGFontCreateWithDataProvider(provider);
    CTFontRef base = CTFontCreateWithGraphicsFont(graphics, 20, nullptr, nullptr);
    for (float width : {50.f, 75.f, 90.25f, 112.5f, 150.f, 200.f}) {
        auto actual = Font(context, u"PrivateWidth44", width, 650);
        CTFontRef reference = Varied(base, width, @650); Compare(actual.get(), reference); CFRelease(reference);
    }
    auto retained = Font(context, u"PrivateWidth44", 75, 650);
    jalium_font_resource_release(resource);
    CTFontRef reference = Varied(base, 75, @650); Compare(retained.get(), reference); CFRelease(reference);
    CFRelease(base); CFRelease(graphics); CFRelease(provider);
    NSData* collection = [NSData dataWithContentsOfFile:@"/System/Library/Fonts/HelveticaNeue.ttc"];
    auto collectionResource = jalium_font_resource_register(Managed(u"PrivateWidthCollection44"),
        static_cast<const uint8_t*>(collection.bytes), collection.length);
    Check(collectionResource != nullptr, "Private width collection unavailable");
    for (float width : {50.f, 75.f, 87.5f}) for (int weight : {400, 700, 900}) {
        auto actual = Font(context, u"PrivateWidthCollection44", width, weight);
        NSFont* expected = [NSFont fontWithName:weight == 900 ? @"HelveticaNeue-CondensedBlack" : @"HelveticaNeue-CondensedBold" size:20];
        Compare(actual.get(), (__bridge CTFontRef)expected);
    }
    jalium_font_resource_release(collectionResource);
    // Changing all collection advances proves that width selection stays in
    // these private bytes instead of finding an installed face of the same name.
    NSMutableData* modified = [collection mutableCopy]; auto* bytes = static_cast<uint8_t*>(modified.mutableBytes);
    auto read16 = [&](size_t offset) { if (offset + 2 > modified.length) throw std::runtime_error("TTC uint16 outside fixture"); return (bytes[offset] << 8) | bytes[offset + 1]; };
    auto read32 = [&](size_t offset) { if (offset + 4 > modified.length) throw std::runtime_error("TTC uint32 outside fixture"); return
        (uint32_t(bytes[offset]) << 24) | (uint32_t(bytes[offset + 1]) << 16) | (uint32_t(bytes[offset + 2]) << 8) | bytes[offset + 3]; };
    std::set<uint32_t> altered;
    uint32_t faces = read32(8);
    for (uint32_t face = 0; face < faces; ++face) {
        uint32_t offset = read32(12 + face * 4), hhea = 0, hmtx = 0;
        int tables = read16(offset + 4);
        for (int table = 0; table < tables; ++table) {
            uint32_t record = offset + 12 + table * 16, tag = read32(record), location = read32(record + 8);
            if (tag == 0x68686561) hhea = location;
            if (tag == 0x686d7478) hmtx = location;
        }
        Check(hhea && hmtx, "Private collection has no advance table");
        if (!altered.insert(hmtx).second) continue;
        int glyphs = read16(hhea + 34);
        for (int glyph = 0; glyph < glyphs; ++glyph) {
            uint32_t position = hmtx + glyph * 4; int value = read16(position) + 32;
            if (value > 65535) throw std::runtime_error("Modified advance exceeds uint16"); bytes[position] = value >> 8; bytes[position + 1] = value;
        }
    }
    auto changedResource = jalium_font_resource_register(Managed(u"ModifiedWidthCollection44"), bytes, modified.length);
    Check(changedResource != nullptr, "Modified width collection rejected");
    auto changed = Font(context, u"ModifiedWidthCollection44", 75, 700), installed = Font(context, u"Helvetica Neue", 75, 700);
    jalium_font_resource_release(changedResource);
    const char16_t* latin = u"MMMM iii1 abc xyz tail"; JaliumTextMetrics changedMetrics{}, installedMetrics{};
    Check(jalium_text_format_measure_text(changed.get(), Managed(latin), 21, 100000, 1000, &changedMetrics) == JALIUM_OK &&
        jalium_text_format_measure_text(installed.get(), Managed(latin), 21, 100000, 1000, &installedMetrics) == JALIUM_OK &&
        changedMetrics.width > installedMetrics.width, "Private width selection substituted installed collection bytes");
    std::printf("macOS font width checks: %d assertions; 160 system width/weight/style configurations\n", checks);
    return checks;
}
