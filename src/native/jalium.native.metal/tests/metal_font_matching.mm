#include "jalium_api.h"
#import <AppKit/AppKit.h>
#include <cmath>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <string>

namespace {
int checks = 0;
const wchar_t* Managed(const char16_t* text) { return reinterpret_cast<const wchar_t*>(text); }
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); ++checks; }
using Format = std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)>;
const std::u16string sample = u"MMMM iii1 abc אבג xyz tail";

void Compare(JaliumContext* context, const char16_t* family, int weight, int style, float width, NSString* face)
{
    Format actual(jalium_text_format_create_with_width(context, Managed(family), 20, weight, style, width), jalium_text_format_destroy);
    Check(actual != nullptr, "Fully matched font creation failed");
    jalium_text_format_set_word_wrapping(actual.get(), 1);
    NSFont* font = [NSFont fontWithName:face size:20];
    CTFontRef reference = nullptr;
    if (style && [face hasPrefix:@"HelveticaNeue-Condensed"]) {
        // The installed condensed faces have no italic sibling. Keep their
        // width and use the same default geometric slope, independently of
        // the native selector, rather than letting AppKit change widths.
        CGAffineTransform slope = CGAffineTransformMake(1, 0, std::tan(12.0 * M_PI / 180), 1, 0, 0);
        reference = CTFontCreateCopyWithAttributes((__bridge CTFontRef)font, 20, &slope, nullptr);
    } else {
        if (style) font = [NSFontManager.sharedFontManager convertFont:font toHaveTrait:NSItalicFontMask];
        reference = static_cast<CTFontRef>(CFRetain((__bridge CTFontRef)font));
    }
    NSString* text = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(sample.data()) length:sample.size()];
    NSAttributedString* attributed = [[NSAttributedString alloc] initWithString:text attributes:@{NSFontAttributeName:(__bridge id)reference}];
    CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)attributed);
    double expected = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
    JaliumTextMetrics metrics{};
    auto result = jalium_text_format_measure_text(actual.get(), Managed(sample.c_str()), sample.size(), 100000, 1000, &metrics);
    if (result != JALIUM_OK || std::abs(metrics.width - expected) >= .001)
        std::fprintf(stderr, "Matched face failure: family=%s weight=%d style=%d width=%g expected=%s %.6f actual=%.6f\n",
            family == u"Helvetica Neue" ? "Helvetica Neue" : "Avenir/private", weight, style, width, face.UTF8String, expected, metrics.width);
    Check(result == JALIUM_OK && std::abs(metrics.width - expected) < .001 && std::abs(metrics.ascent - CTFontGetAscent(reference)) < .001,
        "Width/style/weight matching differs from explicit reference face");
    JaliumTextSpan span{actual.get(), 0, static_cast<uint32_t>(sample.size()), 0, 0, 0, 1};
    auto paragraph = jalium_text_paragraph_create_with_wrapping(actual.get(), reinterpret_cast<const uint16_t*>(sample.data()),
        sample.size(), &span, 1, 100000, 48, 0, 0, 1);
    Check(paragraph != nullptr, "Matched font paragraph creation failed");
    for (uint32_t position = 0; position <= sample.size(); ++position) {
        CGFloat secondary = 0, primary = CTLineGetOffsetForStringIndex(line, position, &secondary);
        JaliumParagraphCaret caret{};
        Check(jalium_text_paragraph_get_caret(paragraph, 0, position, 0, &caret) == JALIUM_OK &&
            (std::abs(caret.x - primary) < .001 || std::abs(caret.x - secondary) < .001), "Matched font caret differs from reference face");
    }
    JaliumFontUnitMetrics units{sizeof(JaliumFontUnitMetrics)};
    NSAttributedString* zero = [[NSAttributedString alloc] initWithString:@"0" attributes:@{NSFontAttributeName:(__bridge id)reference}];
    CTLineRef unit = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)zero);
    Check(jalium_text_format_get_font_unit_metrics(actual.get(), &units) == JALIUM_OK &&
        std::abs(units.zeroAdvance - CTLineGetTypographicBounds(unit, nullptr, nullptr, nullptr)) < .001,
        "Matched font ch ruler differs from reference face");
    CFRelease(unit); CFRelease(line); CFRelease(reference); jalium_text_paragraph_destroy(paragraph);
}

// Explicit expected faces at CSS search boundaries. The reference never asks
// CoreText to select a family by weight, so it cannot repeat the old selector.
struct WeightCase { int weight; NSString* helvetica; NSString* avenir; };
}

int TestFontMatching(JaliumContext* context)
{
    const WeightCase cases[] = {
        {1,@"HelveticaNeue-UltraLight",@"AvenirNext-UltraLight"}, {50,@"HelveticaNeue-UltraLight",@"AvenirNext-UltraLight"},
        {100,@"HelveticaNeue-UltraLight",@"AvenirNext-UltraLight"}, {125,@"HelveticaNeue-UltraLight",@"AvenirNext-UltraLight"},
        {199,@"HelveticaNeue-UltraLight",@"AvenirNext-UltraLight"}, {200,@"HelveticaNeue-Thin",@"AvenirNext-UltraLight"},
        {275,@"HelveticaNeue-Thin",@"AvenirNext-UltraLight"}, {300,@"HelveticaNeue-Light",@"AvenirNext-UltraLight"},
        {350,@"HelveticaNeue-Light",@"AvenirNext-UltraLight"}, {399,@"HelveticaNeue-Light",@"AvenirNext-UltraLight"},
        {400,@"HelveticaNeue",@"AvenirNext-Regular"}, {401,@"HelveticaNeue-Medium",@"AvenirNext-Medium"},
        {450,@"HelveticaNeue-Medium",@"AvenirNext-Medium"}, {499,@"HelveticaNeue-Medium",@"AvenirNext-Medium"},
        {500,@"HelveticaNeue-Medium",@"AvenirNext-Medium"}, {501,@"HelveticaNeue-Bold",@"AvenirNext-DemiBold"},
        {550,@"HelveticaNeue-Bold",@"AvenirNext-DemiBold"}, {650,@"HelveticaNeue-Bold",@"AvenirNext-Bold"},
        {699,@"HelveticaNeue-Bold",@"AvenirNext-Bold"}, {700,@"HelveticaNeue-Bold",@"AvenirNext-Bold"},
        {701,@"HelveticaNeue-Bold",@"AvenirNext-Heavy"}, {750,@"HelveticaNeue-Bold",@"AvenirNext-Heavy"},
        {800,@"HelveticaNeue-Bold",@"AvenirNext-Heavy"}, {900,@"HelveticaNeue-Bold",@"AvenirNext-Heavy"},
        {999,@"HelveticaNeue-Bold",@"AvenirNext-Heavy"}, {1000,@"HelveticaNeue-Bold",@"AvenirNext-Heavy"}
    };
    for (const auto& item : cases) for (float width : {75.f,100.f,125.f}) for (int style : {0,1,2}) {
        NSString* helvetica = width == 75 ? (item.weight > 700 ? @"HelveticaNeue-CondensedBlack" : @"HelveticaNeue-CondensedBold") : item.helvetica;
        Compare(context, u"Helvetica Neue", item.weight, style, width, helvetica);
        Compare(context, u"Avenir Next", item.weight, style, width, item.avenir);
    }
    NSData* data = [NSData dataWithContentsOfFile:@"/System/Library/Fonts/HelveticaNeue.ttc"];
    auto resource = jalium_font_resource_register(Managed(u"PrivateMatch46"), static_cast<const uint8_t*>(data.bytes), data.length);
    Check(resource != nullptr, "Private match collection unavailable");
    for (int weight : {450,501,750,900}) for (int style : {0,1})
        Compare(context, u"PrivateMatch46", weight, style, 100, weight == 450 ? @"HelveticaNeue-Medium" : @"HelveticaNeue-Bold");
    Format retained(jalium_text_format_create_with_width(context, Managed(u"PrivateMatch46"), 20, 900, 0, 100), jalium_text_format_destroy);
    jalium_font_resource_release(resource); Check(retained != nullptr, "Matched private format did not retain collection");
    Format expected(jalium_text_format_create(context, Managed(u"HelveticaNeue-Bold"),20,400,0),jalium_text_format_destroy);
    JaliumTextMetrics actualMetrics{}, expectedMetrics{};
    Check(jalium_text_format_measure_text(retained.get(), Managed(sample.c_str()), sample.size(),100000,1000,&actualMetrics)==JALIUM_OK &&
        jalium_text_format_measure_text(expected.get(), Managed(sample.c_str()), sample.size(),100000,1000,&expectedMetrics)==JALIUM_OK &&
        actualMetrics.width==expectedMetrics.width, "Matched private normal-width face failed after registration release");
    std::printf("Full font matching: %d assertions; 468 installed and 8 private width/style/weight boundary configurations\n", checks);
    return checks;
}
