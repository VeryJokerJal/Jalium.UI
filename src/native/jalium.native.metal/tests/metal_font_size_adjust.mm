#include "jalium_api.h"
#include "../src/metal_text_paragraph.h"
#import <AppKit/AppKit.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
int checks = 0, pixelChecks = 0;
void Check(bool value, const char* message) { if (!value) { std::fprintf(stderr,"At assertion %d pixel %d\n", checks,pixelChecks); throw std::runtime_error(message); } ++checks; }
void Near(double actual, double expected, const char* message) {
    if (std::abs(actual - expected) > .003) std::fprintf(stderr, "%s: %g != %g\n", message, actual, expected);
    Check(std::abs(actual - expected) < .003, message);
}
const wchar_t* Managed(const char16_t* value) { return reinterpret_cast<const wchar_t*>(value); }
using Format = std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)>;
Format Font(JaliumContext* context, const char16_t* family) {
    Format font(jalium_text_format_create(context, Managed(family), 32, 400, 0), jalium_text_format_destroy);
    Check(font != nullptr, "Size adjustment format unavailable");
    jalium_text_format_set_word_wrapping(font.get(), 1);
    return font;
}
CGFontRef Graphics(NSString* path) {
    NSData* data = [NSData dataWithContentsOfFile:path]; Check(data.length > 12, "Fixture unavailable");
    auto provider = CGDataProviderCreateWithCFData((__bridge CFDataRef)data);
    auto font = CGFontCreateWithDataProvider(provider); CGDataProviderRelease(provider);
    Check(font != nullptr, "Independent graphics face unavailable"); return font;
}
CTFontRef Raw(CGFontRef graphics, double size) { return CTFontCreateWithGraphicsFont(graphics, size, nullptr, nullptr); }
NSAttributedString* Pair(CTFontRef first, CTFontRef second) {
    NSMutableAttributedString* text = [[NSMutableAttributedString alloc] initWithString:@"xA" attributes:@{NSFontAttributeName:(__bridge id)first, NSForegroundColorAttributeName:NSColor.blackColor}];
    [text addAttribute:NSFontAttributeName value:(__bridge id)second range:NSMakeRange(1, 1)]; return text;
}
void Compare(JaliumTextFormat* font, NSAttributedString* expected, const std::u16string& sample = u"xA") {
    CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)expected);
    JaliumTextMetrics metrics{};
    Check(jalium_text_format_measure_text(font, Managed(sample.c_str()), sample.size(), 10000, 1000, &metrics) == JALIUM_OK, "Size adjustment measurement failed");
    Near(metrics.width, CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr), "Per-face width differs");
    for (uint32_t index = 0; index <= sample.size(); ++index) {
        JaliumTextHitTestResult hit{};
        Check(jalium_text_format_hit_test_text_position(font, Managed(sample.c_str()), sample.size(), 10000, 1000, index, 0, &hit) == JALIUM_OK, "Size adjustment caret unavailable");
        Near(hit.caretX, CTLineGetOffsetForStringIndex(line, index, nullptr), "Source caret differs");
    }
    JaliumTextSpan span{font, 0, static_cast<uint32_t>(sample.size()), 0, 0, 0, 1};
    auto paragraph = jalium_text_paragraph_create_with_wrapping(font, reinterpret_cast<const uint16_t*>(sample.data()), sample.size(), &span, 1, 10000, 48, 0, 0, 1);
    Check(paragraph != nullptr, "Size adjustment paragraph unavailable");
    JaliumParagraphLineMetrics paragraphMetrics{};
    Check(jalium_text_paragraph_get_line(paragraph, 0, &paragraphMetrics) == JALIUM_OK, "Paragraph metrics unavailable");
    Near(paragraphMetrics.line.width, metrics.width, "Paragraph differs from format");
    auto* native = dynamic_cast<jalium::MetalTextParagraph*>(reinterpret_cast<jalium::TextParagraph*>(paragraph));
    Check(native != nullptr, "Native paragraph provider unavailable");
    for (int dpi : {1, 2}) for (bool transformed : {false, true}) {
        float matrix[] = {float(dpi), transformed ? .2f*dpi : 0, transformed ? -.15f*dpi : 0, float(dpi), 6, 9};
        std::vector<uint8_t> actual; uint32_t width{}, height{}; float left{}, top{};
        Check(native->RasterizeLine(0, 12, 12, 1, matrix, 720*dpi, 192*dpi, actual, width, height, left, top), "Adjusted rasterization failed");
        std::vector<uint8_t> pixels(width*height*4);
        auto space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        auto context = CGBitmapContextCreate(pixels.data(), width, height, 8, width*4, space, static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst)|static_cast<CGBitmapInfo>(kCGBitmapByteOrder32Little));
        Check(context != nullptr, "Reference bitmap context unavailable");
        CGContextSetAllowsFontSubpixelPositioning(context, true); CGContextSetShouldSubpixelPositionFonts(context, true);
        CGContextSetAllowsFontSubpixelQuantization(context, false); CGContextSetShouldSubpixelQuantizeFonts(context, false);
        double x = 12, y = 12 + paragraphMetrics.baseline;
        CGContextConcatCTM(context, CGAffineTransformMake(matrix[0], -matrix[1], -matrix[2], matrix[3], matrix[0]*x + matrix[2]*y + matrix[4] - left, height - (matrix[1]*x + matrix[3]*y + matrix[5] - top)));
        CGContextSetTextMatrix(context, CGAffineTransformIdentity); CTLineDraw(line, context);
        Check(actual == pixels && std::any_of(actual.begin(), actual.end(), [](uint8_t v){return v != 0;}), "Adjusted glyph pixels differ from independent CTLineDraw");
        ++pixelChecks; CGContextRelease(context); CGColorSpaceRelease(space);
    }
    jalium_text_paragraph_destroy(paragraph); CFRelease(line);
}
CTFontRef OpticalReference(CGFontRef graphics, double size, double optical) {
    NSArray* axes = CFBridgingRelease(CGFontCopyVariationAxes(graphics));
    NSData* table = CFBridgingRelease(CGFontCopyTableForTag(graphics, 0x66766172));
    const auto* bytes = static_cast<const uint8_t*>(table.bytes);
    auto u16 = [&](size_t at) { Check(at+2 <= table.length, "Optical table bounds"); return uint32_t(bytes[at])<<8 | bytes[at+1]; };
    auto u32 = [&](size_t at) { return u16(at)<<16 | u16(at+2); };
    auto base = Raw(graphics, 32); NSDictionary* defaults = CFBridgingRelease(CTFontCopyVariation(base));
    NSMutableDictionary* coordinates = [NSMutableDictionary new];
    for (NSUInteger i = 0; i < axes.count; ++i) {
        uint32_t tag = u32(u16(4)+i*u16(10)); NSDictionary* axis = axes[i];
        double value = tag == 0x6f70737a ? optical : defaults[@(tag)] ? [defaults[@(tag)] doubleValue] : [axis[(__bridge id)kCGFontVariationAxisDefaultValue] doubleValue];
        value = std::clamp(value, [axis[(__bridge id)kCGFontVariationAxisMinValue] doubleValue], [axis[(__bridge id)kCGFontVariationAxisMaxValue] doubleValue]);
        coordinates[axis[(__bridge id)kCGFontVariationAxisName]] = @(value);
        if (tag == 0x6f70737a) optical = value;
    }
    auto selected = CGFontCreateCopyWithVariations(graphics, (__bridge CFDictionaryRef)coordinates);
    auto descriptor = CTFontDescriptorCreateWithAttributes((__bridge CFDictionaryRef)@{(__bridge id)kCTFontOpticalSizeAttribute:@(optical)});
    auto result = CTFontCreateWithGraphicsFont(selected, size, nullptr, descriptor);
    CFRelease(base); CFRelease(descriptor); CGFontRelease(selected); return result;
}
void OpticalSizing(JaliumContext* context) {
    NSString* path = @"/System/Library/Fonts/SFNS.ttf"; NSData* data = [NSData dataWithContentsOfFile:path];
    auto resource = jalium_font_resource_register(Managed(u"SizeOptical"), static_cast<const uint8_t*>(data.bytes), data.length);
    Check(resource != nullptr, "Optical fixture registration failed"); auto graphics = Graphics(path);
    auto font = Font(context, u"SizeOptical"); NSArray* axes = CFBridgingRelease(CGFontCopyVariationAxes(graphics));
    double defaultOptical = 0; for (NSDictionary* axis in axes) if ([axis[(__bridge id)kCGFontVariationAxisName] isEqual:@"Optical Size"])
        defaultOptical = [axis[(__bridge id)kCGFontVariationAxisDefaultValue] doubleValue];
    // The raw axis name above is supplied by Core Graphics, not a localized UI label.
    Check(defaultOptical > 0, "Optical default unavailable");
    for (uint32_t mode : {1u, 2u}) for (double explicitOptical : {0., 6., 128.}) {
        JaliumFontVariation axis{0x6f70737a, explicitOptical}; uint32_t count = explicitOptical ? 1 : 0;
        Check(jalium_text_format_set_font_optical_variations(font.get(), &axis, count, mode) == JALIUM_OK, "Optical setup failed");
        auto before = OpticalReference(graphics, 32, explicitOptical ? explicitOptical : mode == 1 ? 32 : defaultOptical);
        double aspect = CTFontGetCapHeight(before)/32; float used{};
        Check(jalium_text_format_set_font_size_adjust(font.get(), 2, aspect*1.5, mode, &axis, count, &used) == JALIUM_OK, "Optical size adjustment failed"); Near(used, 48, "Optical used size differs");
        auto expected = OpticalReference(graphics, 48, explicitOptical ? explicitOptical : mode == 1 ? 48 : defaultOptical);
        Compare(font.get(), Pair(expected, expected)); CFRelease(before); CFRelease(expected);
    }
    CGFontRelease(graphics); jalium_font_resource_release(resource);
}
void Run(JaliumContext* context, NSString* root) {
    std::vector<JaliumFontResource*> resources;
    const char16_t* aliases[] = {u"SizePrimary", u"SizeSecondary", u"SizeMissing"};
    NSArray<NSString*>* names = @[@"Primary", @"Secondary", @"Missing"];
    for (NSUInteger i = 0; i < names.count; ++i) {
        NSData* data = [NSData dataWithContentsOfFile:[root stringByAppendingPathComponent:[names[i] stringByAppendingString:@".ttf"]]];
        auto resource = jalium_font_resource_register(Managed(aliases[i]), static_cast<const uint8_t*>(data.bytes), data.length);
        Check(resource != nullptr, "Size fixture registration failed"); resources.push_back(resource);
    }
    auto first = Font(context, aliases[0]), second = Font(context, aliases[1]), missing = Font(context, aliases[2]);
    JaliumTextFormat* fallback[] = {second.get()};
    Check(jalium_text_format_set_font_fallbacks(first.get(), fallback, 1) == JALIUM_OK, "Per-face cascade failed");
    auto primary = Graphics([root stringByAppendingPathComponent:@"Primary.ttf"]), secondary = Graphics([root stringByAppendingPathComponent:@"Secondary.ttf"]);
    const double ratios[] = {.5, .75, .6, 1, 1.2};
    for (uint32_t metric = 1; metric <= 5; ++metric) {
        double aspect{}; float used{};
        Check(jalium_text_format_get_font_aspect_metric(first.get(), metric, &aspect) == JALIUM_OK, "Aspect query failed"); Near(aspect, ratios[metric-1], "Primary aspect differs");
        Check(jalium_text_format_get_font_aspect_metric(missing.get(), metric, &aspect) == JALIUM_OK && aspect == 0, "Missing metric was synthesized");
        Check(jalium_text_format_set_font_size_adjust(missing.get(), metric, .9, 2, nullptr, 0, &used) == JALIUM_OK && used == 32, "Missing metric adjusted the font");
        for (int repeat = 0; repeat < 3; ++repeat) {
            Check(jalium_text_format_set_font_size_adjust(first.get(), metric, ratios[metric-1]*1.5, 2, nullptr, 0, &used) == JALIUM_OK && used == 48, "Primary used size differs");
            auto one = Raw(primary, 48), two = Raw(secondary, 96); Compare(first.get(), Pair(one, two)); CFRelease(one); CFRelease(two);
        }
        for (double invalid : {-1., std::numeric_limits<double>::infinity(), std::numeric_limits<double>::quiet_NaN()})
            Check(jalium_text_format_set_font_size_adjust(first.get(), metric, invalid, 2, nullptr, 0, &used) == JALIUM_ERROR_INVALID_ARGUMENT, "Invalid adjustment accepted");
        Check(jalium_text_format_set_font_size_adjust(first.get(), 6, .5, 2, nullptr, 0, &used) == JALIUM_ERROR_INVALID_ARGUMENT, "Invalid metric accepted");
        Check(jalium_text_format_set_font_size_adjust(first.get(), metric, .5, 3, nullptr, 0, &used) == JALIUM_ERROR_INVALID_ARGUMENT, "Invalid optical mode accepted");
        Check(jalium_text_format_set_font_size_adjust(first.get(), metric, .5, 2, nullptr, 1, &used) == JALIUM_ERROR_INVALID_ARGUMENT, "Null axes accepted");
        auto one = Raw(primary, 48), two = Raw(secondary, 96); Compare(first.get(), Pair(one, two)); CFRelease(one); CFRelease(two);
        Check(jalium_text_format_set_font_size_adjust(first.get(), metric, 0, 2, nullptr, 0, &used) == JALIUM_OK && used == 0, "Zero used size differs");
        JaliumTextMetrics zero{};
        Check(jalium_text_format_measure_text(first.get(), Managed(u"xA"), 2, 10000, 1000, &zero) == JALIUM_OK && std::abs(zero.width) < 1e-15, "Zero adjustment retained advance");
        Check(jalium_text_format_set_font_size_adjust(first.get(), metric, std::numeric_limits<double>::max(), 2, nullptr, 0, &used) == JALIUM_OK && used == 1048576, "Extreme used size was not bounded");
        Check(jalium_text_format_set_font_size_adjust(first.get(), 0, 0, 2, nullptr, 0, &used) == JALIUM_OK && used == 32, "Reset failed");
        one = Raw(primary, 32); two = Raw(secondary, 32); Compare(first.get(), Pair(one, two)); CFRelease(one); CFRelease(two);
    }
    // Lowercase x is present in Primary; only Secondary contains uppercase X.
    // Synthesis must size the face selected after casing, including position.
    float capsUsed{};
    Check(jalium_text_format_set_font_size_adjust(first.get(),1,.75,2,nullptr,0,&capsUsed)==JALIUM_OK,"Caps adjustment failed");
    Check(jalium_text_format_set_font_caps(first.get(),1,1,nullptr,0)==JALIUM_OK,"Caps setup failed");
    for(uint32_t position:{0u,1u,2u}) {
        Check(jalium_text_format_set_font_position(first.get(),position,1,0)==JALIUM_OK,"Position setup failed");
        double scale=position?.65:1;float sourceSize=static_cast<float>(48*scale);float capsSize=static_cast<float>(sourceSize*.8);
        auto x=Raw(secondary,capsSize*2),a=Raw(secondary,static_cast<float>(96*scale));
        NSMutableAttributedString* expected=[[NSMutableAttributedString alloc]initWithString:@"XA" attributes:@{NSFontAttributeName:(__bridge id)x,NSForegroundColorAttributeName:NSColor.blackColor}];
        [expected addAttribute:NSFontAttributeName value:(__bridge id)a range:NSMakeRange(1,1)];
        if(position)[expected addAttribute:(__bridge NSString*)kCTBaselineOffsetAttributeName value:@(position==1?-19.2:33.6) range:NSMakeRange(0,2)];
        Compare(first.get(),expected);CFRelease(x);CFRelease(a);
    }
    // Use the font selected by CoreText for an absent CJK character, then
    // calculate the reference scale directly from that face's zero glyph.
    auto implicit = Font(context, aliases[0]); float implicitUsed{};
    Check(jalium_text_format_set_font_size_adjust(implicit.get(), 3, .9, 2, nullptr, 0, &implicitUsed) == JALIUM_OK, "Implicit fallback sizing failed");
    auto base = Raw(primary, 32);
    NSAttributedString* original = [[NSAttributedString alloc] initWithString:@"x漢" attributes:@{NSFontAttributeName:(__bridge id)base, NSForegroundColorAttributeName:NSColor.blackColor}];
    auto shaped = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)original);
    NSMutableAttributedString* reference = [original mutableCopy]; auto selectedRuns = CTLineGetGlyphRuns(shaped); Check(CFArrayGetCount(selectedRuns) >= 2, "Implicit fallback was not exercised");
    for (CFIndex index = 0; index < CFArrayGetCount(selectedRuns); ++index) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(selectedRuns,index));
        auto selected = static_cast<CTFontRef>(CFDictionaryGetValue(CTRunGetAttributes(run),kCTFontAttributeName));
        UniChar zero = '0'; CGGlyph glyph{}; CGSize advance{};
        bool hasZero = CTFontGetGlyphsForCharacters(selected,&zero,&glyph,1) && glyph;
        if (hasZero) CTFontGetAdvancesForGlyphs(selected,kCTFontOrientationHorizontal,&glyph,&advance,1);
        double size = hasZero && advance.width > 0 ? 32*.9/(advance.width/CTFontGetSize(selected)) : 32;
        auto graphics = CTFontCopyGraphicsFont(selected,nullptr); auto descriptor = CTFontCopyFontDescriptor(selected); auto matrix = CTFontGetMatrix(selected);
        auto adjusted = CTFontCreateWithGraphicsFont(graphics,size,&matrix,descriptor); auto range = CTRunGetStringRange(run);
        [reference addAttribute:NSFontAttributeName value:(__bridge id)adjusted range:NSMakeRange(range.location,range.length)];
        CFRelease(adjusted); CFRelease(descriptor); CGFontRelease(graphics);
    }
    Compare(implicit.get(),reference,u"x漢"); CFRelease(shaped); CFRelease(base);
    CGFontRelease(primary); CGFontRelease(secondary);
    for (auto resource : resources) jalium_font_resource_release(resource);
}
}
int main(int argc, char** argv) {
    @autoreleasepool { try {
        Check(argc == 2, "Fixture directory required"); [NSApplication sharedApplication];
        NSString* root = [NSString stringWithUTF8String:argv[1]];
        for (JaliumBackend backend : {JALIUM_BACKEND_METAL, JALIUM_BACKEND_SOFTWARE}) {
            auto context = jalium_context_create(backend); Check(context != nullptr, "Size adjustment context unavailable");
            Run(context, root); OpticalSizing(context); jalium_context_destroy(context);
        }
        std::printf("Font size adjustment: %d checks; %d independent raster comparisons\n", checks, pixelChecks); return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "FAIL %s\n", error.what()); return 1; } }
}
