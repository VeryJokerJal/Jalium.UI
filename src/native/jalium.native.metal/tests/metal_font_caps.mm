#include "jalium_api.h"
#include "metal_font_caps.h"
#import <AppKit/AppKit.h>
#include <cmath>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <string>

namespace {
int checks = 0;
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); ++checks; }
void Near(double actual, double expected, const char* message) {
    if (std::abs(actual - expected) > .003) { std::fprintf(stderr, "%s: actual %.6f expected %.6f\n", message, actual, expected); throw std::runtime_error(message); }
    ++checks;
}
const wchar_t* Managed(const char16_t* text) { return reinterpret_cast<const wchar_t*>(text); }
using Format = std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)>;
using Paragraph = std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)>;

double Width(NSString* text, double size = 25.6) {
    auto font = CTFontCreateWithName(CFSTR("Arial"), size, nullptr);
    auto value = [[NSAttributedString alloc] initWithString:text attributes:@{NSFontAttributeName:(__bridge id)font}];
    auto line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)value);
    double width = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr); CFRelease(line); CFRelease(font); return width;
}
void TransformMap() {
    auto font = CTFontCreateWithName(CFSTR("Arial"), 32, nullptr);
    for (auto item : {std::pair{u"aßi", u"ASSI"}, std::pair{u"ﬃ", u"FFI"}, std::pair{u"é", u"É"}, std::pair{u"𐐨", u"𐐀"},
        std::pair{u"ⅰⓐ", u"ⅠⒶ"}, std::pair{u"\u0345", u"Ι"}, std::pair{u"Α\u0345", u"ΑΙ"}}) {
        std::u16string source(item.first), expected(item.second);
        auto string = CFStringCreateWithCharacters(kCFAllocatorDefault, reinterpret_cast<const UniChar*>(source.data()), source.size());
        auto text = CFAttributedStringCreateMutable(kCFAllocatorDefault, 0); CFAttributedStringReplaceString(text, CFRangeMake(0, 0), string);
        CFAttributedStringSetAttribute(text, CFRangeMake(0, source.size()), kCTFontAttributeName, font);
        jalium::SetAppleCapsAttributes(text, CFRangeMake(0, source.size()), 1, true, CFSTR("en-US"));
        jalium::AppleCapsMap mapping; auto painted = jalium::CreateAppleCapsText(text, mapping);
        Check(painted && mapping.Active(), "Caps transform/map missing");
        auto wanted = CFStringCreateWithCharacters(kCFAllocatorDefault, reinterpret_cast<const UniChar*>(expected.data()), expected.size());
        Check(CFEqual(CFAttributedStringGetString(painted), wanted), "Full Unicode uppercase differs from specified mapping");
        for (const auto& part : mapping.parts) {
            Check(mapping.ToPaint(part.source.location) == part.paint.location, "Source start does not map to paint start");
            Check(mapping.ToPaint(part.source.location + part.source.length) == part.paint.location + part.paint.length, "Source end does not map to paint end");
            for (CFIndex i = 1; i < part.paint.length; ++i) Check(!mapping.Boundary(part.paint.location + i), "Expansion exposed an interior source boundary");
        }
        CFRelease(wanted); CFRelease(painted); CFRelease(text); CFRelease(string);
    }
    CFRelease(font);
}

void ContextualTransform() {
    struct Case { CFStringRef source, expected, language; uint32_t variant; bool split; };
    for (const auto& item : {
        Case{CFSTR("ΟΣ"), CFSTR("ος"), CFSTR("el"), 5, false},
        Case{CFSTR("ΟΣΑ"), CFSTR("οσα"), CFSTR("el"), 5, false},
        Case{CFSTR("Ο'Σ"), CFSTR("ο'ς"), CFSTR("el"), 5, false},
        Case{CFSTR("ΟΣ'Α"), CFSTR("οσ'α"), CFSTR("el"), 5, false},
        Case{CFSTR("ΟΣ"), CFSTR("Ος"), CFSTR("el"), 5, true},
        Case{CFSTR("I\u0307"), CFSTR("i"), CFSTR("tr"), 5, false},
        Case{CFSTR("I\u0323\u0307"), CFSTR("i\u0323"), CFSTR("az"), 5, false},
    }) {
        auto font = CTFontCreateWithName(CFSTR("Seravek"), 32, nullptr);
        auto text = CFAttributedStringCreateMutable(kCFAllocatorDefault, 0);
        CFAttributedStringReplaceString(text, CFRangeMake(0, 0), item.source);
        auto range = CFRangeMake(0, CFStringGetLength(item.source));
        CFAttributedStringSetAttribute(text, range, kCTFontAttributeName, font);
        if (item.split) { range.location = 1; --range.length; }
        jalium::SetAppleCapsAttributes(text, range, item.variant, true, item.language, 0);
        jalium::AppleCapsMap mapping; auto painted = jalium::CreateAppleCapsText(text, mapping);
        Check(painted && CFEqual(CFAttributedStringGetString(painted), item.expected), "Caps lost paragraph context across scalar/style boundaries");
        Check(mapping.SourceLength(0) == CFStringGetLength(item.source), "Contextual caps changed source ownership");
        if (CFEqual(item.source, CFSTR("I\u0307"))) {
            auto base = mapping.PaintOwnedRange(CFRangeMake(0, 1));
            auto mark = mapping.PaintOwnedRange(CFRangeMake(1, 1));
            Check(base.location == 0 && base.length == 0 && mark.location == 0 && mark.length == 1,
                "Cross-source grapheme paint ownership was duplicated or lost");
        }
        CFRelease(painted); CFRelease(text); CFRelease(font);
    }
    // A cased combining character has its own case policy; a caseless accent
    // follows its base while retaining the accent's authored paint attributes.
    auto font = CTFontCreateWithName(CFSTR("Arial"), 32, nullptr);
    auto text = CFAttributedStringCreateMutable(kCFAllocatorDefault, 0);
    CFAttributedStringReplaceString(text, CFRangeMake(0, 0), CFSTR("Α\u0345 a\u0301 i\u0307"));
    auto whole = CFRangeMake(0, CFAttributedStringGetLength(text));
    CFAttributedStringSetAttribute(text, whole, kCTFontAttributeName, font);
    CFAttributedStringSetAttribute(text, CFRangeMake(4, 1), CFSTR("AuthoredAccent"), CFSTR("preserved"));
    jalium::SetAppleCapsAttributes(text, whole, 1, true, CFSTR("lt"));
    jalium::AppleCapsMap mapping; auto painted = jalium::CreateAppleCapsText(text, mapping);
    Check(painted && CFEqual(CFAttributedStringGetString(painted), CFSTR("ΑΙ A\u0301 I")), "Mixed cased/uncased grapheme conversion differs");
    auto firstFont = static_cast<CTFontRef>(CFAttributedStringGetAttribute(painted, 0, kCTFontAttributeName, nullptr));
    auto secondFont = static_cast<CTFontRef>(CFAttributedStringGetAttribute(painted, 1, kCTFontAttributeName, nullptr));
    Near(CTFontGetSize(firstFont), 32, "Small-caps shrank the original uppercase base");
    Near(CTFontGetSize(secondFont), 25.6, "Small-caps failed to scale the lowercase combining character");
    auto accentFont = static_cast<CTFontRef>(CFAttributedStringGetAttribute(painted, 4, kCTFontAttributeName, nullptr));
    Near(CTFontGetSize(accentFont), 25.6, "Caseless accent did not follow its synthesized base");
    Check(CFEqual(CFAttributedStringGetAttribute(painted, 4, CFSTR("AuthoredAccent"), nullptr), CFSTR("preserved")), "Caps replaced the accent's paint attributes");
    Check(mapping.parts.size() == 5 && mapping.parts.front().source.length == 2, "Caps exposed an interior source-grapheme boundary");
    CFRelease(painted); CFRelease(text); CFRelease(font);
}

void FragmentContext(JaliumContext* context) {
    Format format(jalium_text_format_create(context, Managed(u"Seravek"), 32, 400, 0), jalium_text_format_destroy);
    Format reference(jalium_text_format_create(context, Managed(u"Seravek"), 32, 400, 0), jalium_text_format_destroy);
    JaliumFontFeature off{0x736d6370, 0};
    Check(format && reference && jalium_text_format_set_font_caps(format.get(), 5, 3, "el", 2) == 0 &&
        jalium_text_format_set_font_features(format.get(), &off, 1) == 0, "Fragment format unavailable");
    for (auto flags : {0u, 1u, 3u, 2u, 1u, 0u}) {
        Check(jalium_text_format_set_font_caps_context(format.get(), flags) == 0, "Fragment context rejected");
        JaliumTextMetrics actual{}, expected{};
        Check(jalium_text_format_measure_text(format.get(), Managed(u"Σ"), 1, 10000, 1000, &actual) == 0 &&
            jalium_text_format_measure_text(reference.get(), Managed(flags == 1 ? u"ς" : u"σ"), 1, 10000, 1000, &expected) == 0,
            "Fragment measurement failed");
        Near(actual.width, expected.width, "Fragment used the wrong final-sigma form");
        JaliumTextHitTestResult hit{};
        Check(jalium_text_format_hit_test_text_position(format.get(), Managed(u"Σ"), 1, 10000, 1000, 1, 0, &hit) == 0 &&
            hit.textPosition == 1, "Fragment caret changed source ownership");
        Near(hit.caretX, expected.width, "Fragment caret ignored contextual width");
    }
    Check(jalium_text_format_set_font_caps_context(format.get(), 1) == 0 &&
        jalium_text_format_set_font_caps(format.get(), 5, 3, "el", 2) == 0, "Caps reset rejected");
    JaliumTextMetrics reset{}, expected{};
    Check(jalium_text_format_measure_text(format.get(), Managed(u"Σ"), 1, 10000, 1000, &reset) == 0 &&
        jalium_text_format_measure_text(reference.get(), Managed(u"σ"), 1, 10000, 1000, &expected) == 0, "Reset measurement failed");
    Near(reset.width, expected.width, "Caps reset retained stale boundary context");
    for (auto flags : {64u, 0xffffffffu})
        Check(jalium_text_format_set_font_caps_context(format.get(), flags) == JALIUM_ERROR_INVALID_ARGUMENT, "Unknown boundary bits accepted");
    Check(jalium_text_format_set_font_caps_context(nullptr, 0) == JALIUM_ERROR_INVALID_ARGUMENT, "Null fragment format accepted");
}

void CombiningContinuation(JaliumContext* context) {
    for (bool native : {false, true}) {
        auto family = native ? u"Seravek" : u"Arial";
        auto language = native ? "tr" : "lt"; uint32_t mode = native ? 5 : 1;
        std::u16string source = native ? u"I\u0307" : u"i\u0307";
        Format format(jalium_text_format_create(context, Managed(family), 32, 400, 0), jalium_text_format_destroy);
        Check(format && jalium_text_format_set_font_caps(format.get(), mode, 3, language, 2) == 0, "Continuation format unavailable");
        JaliumFontFeature off{0x736d6370, 0}; Check(jalium_text_format_set_font_features(format.get(), &off, 1) == 0, "Continuation feature override failed");
        JaliumTextSpan span{format.get(), 0, 2, 0, 0, 0, 1};
        Paragraph whole(jalium_text_paragraph_create(format.get(), reinterpret_cast<const uint16_t*>(source.data()), 2,
            &span, 1, 10000, 44, 0, 0), jalium_text_paragraph_destroy);
        uint8_t operations[2]{99,99};
        Check(whole && jalium_text_paragraph_get_caps_operations(whole.get(), operations, 2) == 0, "Cannot query resolved caps operations");
        Check(operations[0] == (native ? 2 : 1) && operations[1] == operations[0], "Combining mark lost its preceding scalar operation");
        uint8_t sentinel = 99;
        Check(jalium_text_paragraph_get_caps_operations(whole.get(), &sentinel, 1) == JALIUM_ERROR_INVALID_ARGUMENT && sentinel == 99,
            "Invalid caps operation count changed output");
        Check(jalium_text_format_set_font_caps_context(format.get(), native ? 4 : 8) == 0 &&
            jalium_text_format_set_font_caps_continuation(format.get(), operations[1]) == 0, "Cannot configure leading operation");
        uint16_t mark = 0x0307; span.length = 1;
        JaliumTextMetrics direct{};
        Check(jalium_text_format_measure_text(format.get(), Managed(u"\u0307"), 1, 10000, 1000, &direct) == 0 &&
            direct.width == 0 && direct.height > 0, "Deleted mark lost its direct source line height");
        for (uint32_t position : {0u,1u}) {
            JaliumTextHitTestResult hit{};
            Check(jalium_text_format_hit_test_text_position(format.get(), Managed(u"\u0307"), 1, 10000, 1000, position, 0, &hit) == 0 &&
                hit.textPosition == position && hit.caretX == 0 && hit.caretHeight > 0, "Deleted mark lost a direct source caret");
        }
        JaliumTextRangeMetrics range{};
        Check(jalium_text_format_hit_test_text_range(format.get(), Managed(u"\u0307"), 1, 10000, 1000, 0, 1, &range) == 0 &&
            range.textPosition == 0 && range.length == 1 && range.width == 0 && range.height > 0, "Deleted mark lost its direct source range");
        Paragraph empty(jalium_text_paragraph_create(format.get(), &mark, 1, &span, 1, 10000, 44, 0, 0), jalium_text_paragraph_destroy);
        JaliumParagraphLineMetrics line{};
        Check(empty && jalium_text_paragraph_line_count(empty.get()) == 1 && jalium_text_paragraph_get_line(empty.get(), 0, &line) == 0,
            "Deleted leading mark lost its source line");
        Check(line.line.textPosition == 0 && line.line.length == 1, "Deleted mark changed source line indices");
        Near(line.line.width, 0, "Deleted leading mark retained advance");
        for (uint32_t position : {0u,1u}) {
            JaliumParagraphCaret caret{};
            Check(jalium_text_paragraph_get_caret(empty.get(), 0, position, 0, &caret) == 0 && caret.textPosition == position,
                "Deleted mark lost a source caret"); Near(caret.x, 0, "Deleted mark moved its source caret");
        }
        JaliumTextRangeMetrics selection{}; uint32_t count = 0;
        Check(jalium_text_paragraph_get_selection(empty.get(), 0, 0, 1, &selection, 1, &count) == 0 && count == 1 &&
            selection.textPosition == 0 && selection.length == 1, "Deleted mark lost its source selection");
        Near(selection.width, 0, "Deleted mark selection retained paint width");
        Check(jalium_text_format_set_font_caps(format.get(), mode, 3, language, 2) == 0, "Continuation reset failed");
        Paragraph reset(jalium_text_paragraph_create(format.get(), &mark, 1, &span, 1, 10000, 44, 0, 0), jalium_text_paragraph_destroy);
        uint8_t resetOp = 99;
        Check(reset && jalium_text_paragraph_get_caps_operations(reset.get(), &resetOp, 1) == 0 && resetOp == 0,
            "Caps reset retained leading operation");
        Check(jalium_text_format_set_font_caps_continuation(format.get(), 3) == JALIUM_ERROR_INVALID_ARGUMENT,
            "Unknown continuation operation accepted");
    }
    Check(jalium_text_format_set_font_caps_continuation(nullptr, 0) == JALIUM_ERROR_INVALID_ARGUMENT, "Null continuation format accepted");
    Check(jalium_text_paragraph_get_caps_operations(nullptr, nullptr, 0) == JALIUM_ERROR_INVALID_ARGUMENT, "Null operation paragraph accepted");
}

void Native(JaliumContext* context) {
    const std::u16string source = u"aßi";
    Format format(jalium_text_format_create(context, Managed(u"Arial"), 32, 400, 0), jalium_text_format_destroy);
    Check(format != nullptr, "Caps format missing");
    Check(jalium_text_format_set_font_caps(format.get(), 1, 1, "en-US", 5) == JALIUM_OK, "Caps request failed");
    jalium_text_format_set_word_wrapping(format.get(), 1);
    JaliumTextMetrics metrics{}; Check(jalium_text_format_measure_text(format.get(), Managed(source.c_str()), source.size(), 10000, 1000, &metrics) == 0, "Caps measure failed");
    Near(metrics.width, Width(@"ASSI"), "Caps measurement differs from independent scaled-font reference");
    const double offsets[] = {0, Width(@"A"), Width(@"ASS"), Width(@"ASSI")};
    for (uint32_t i = 0; i <= source.size(); ++i) {
        JaliumTextHitTestResult hit{};
        Check(jalium_text_format_hit_test_text_position(format.get(), Managed(source.c_str()), source.size(), 10000, 1000, i, 0, &hit) == 0, "Caps caret query failed");
        Check(hit.textPosition == i, "Caps direct caret changed source index"); Near(hit.caretX, offsets[i], "Caps direct caret differs from source boundary");
        JaliumTextHitTestResult nearest{};
        Check(jalium_text_format_hit_test_point(format.get(), Managed(source.c_str()), source.size(), 10000, 1000, hit.caretX, 10, &nearest) == 0, "Caps point query failed");
        Check(nearest.textPosition == i, "Caps hit exposed a paint index");
    }
    JaliumTextHitTestResult trailing{};
    Check(jalium_text_format_hit_test_text_position(format.get(), Managed(source.c_str()), source.size(), 10000, 1000, 1, 1, &trailing) == 0,
        "Expanded character trailing hit failed");
    Near(trailing.caretX, offsets[2], "Trailing hit stopped inside the synthesized expansion");
    JaliumTextRangeMetrics directSelection{};
    Check(jalium_text_format_hit_test_text_range(format.get(), Managed(source.c_str()), source.size(), 10000, 1000, 1, 1, &directSelection) == 0,
        "Expanded direct range failed");
    Check(directSelection.textPosition == 1 && directSelection.length == 1, "Expanded direct range changed source indices");
    Near(directSelection.width, Width(@"SS"), "Expanded direct range did not contain both letters");
    JaliumTextSpan span{format.get(), 0, static_cast<uint32_t>(source.size()), 0, 0, 0, 1};
    Paragraph paragraph(jalium_text_paragraph_create(format.get(), reinterpret_cast<const uint16_t*>(source.data()), source.size(), &span, 1, 10000, metrics.lineHeight, 0, 0), jalium_text_paragraph_destroy);
    Check(paragraph != nullptr, "Caps paragraph missing");
    JaliumParagraphLineMetrics line{}; Check(jalium_text_paragraph_get_line(paragraph.get(), 0, &line) == 0, "Caps line missing");
    Check(line.line.textPosition == 0 && line.line.length == source.size(), "Caps line uses paint length"); Near(line.line.width, metrics.width, "Caps paragraph/direct advances disagree");
    for (uint32_t i = 0; i <= source.size(); ++i) {
        JaliumParagraphCaret caret{}; Check(jalium_text_paragraph_get_caret(paragraph.get(), 0, i, 0, &caret) == 0, "Caps paragraph caret missing");
        Check(caret.textPosition == i, "Caps paragraph caret uses paint index"); Near(caret.x, offsets[i], "Caps paragraph caret differs from source boundary");
    }
    uint32_t count = 0;
    Check(jalium_text_paragraph_get_selection(paragraph.get(), 0, 1, 1, nullptr, 0, &count) == 0 && count == 1, "Expanded character selection missing");
    JaliumTextRangeMetrics selection{}; Check(jalium_text_paragraph_get_selection(paragraph.get(), 0, 1, 1, &selection, 1, &count) == 0, "Expanded selection retrieval failed");
    Check(selection.textPosition == 1 && selection.length == 1, "Expanded selection changed source ownership");
    Near(selection.width, Width(@"SS"), "Expanded character selection lost one glyph");
    Check(jalium_text_format_set_font_caps(format.get(), 1, 0, "en-US", 5) == 0, "Disabling synthesis failed");
    Check(jalium_text_format_measure_text(format.get(), Managed(source.c_str()), source.size(), 10000, 1000, &metrics) == 0, "Disabled synthesis measure failed");
    Near(metrics.width, Width(@"aßi", 32), "Synthesis none still transformed text");
    Check(jalium_text_format_set_font_caps(format.get(), 1, 1, "tr", 2) == 0, "Turkish caps request failed");
    Check(jalium_text_format_measure_text(format.get(), Managed(u"iı"), 2, 10000, 1000, &metrics) == 0, "Turkish caps measure failed");
    Near(metrics.width, Width(@"İI"), "Caps ignored language-sensitive uppercasing");
    // Capital sharp-s is absent from Arial on this host. Keep the face chosen
    // for its source character instead of comparing its SS with Arial's SS.
    auto primary = CTFontCreateWithName(CFSTR("Arial"), 32, nullptr);
    auto fallback = CTFontCreateForString(primary, CFSTR("ẞ"), CFRangeMake(0, 1));
    auto smallFallback = CTFontCreateCopyWithAttributes(fallback, 25.6, nullptr, nullptr);
    auto smallPrimary = CTFontCreateCopyWithAttributes(primary, 25.6, nullptr, nullptr);
    auto reference = [[NSMutableAttributedString alloc] initWithString:@"ABSS" attributes:@{NSFontAttributeName:(__bridge id)smallPrimary}];
    [reference addAttribute:NSFontAttributeName value:(__bridge id)smallFallback range:NSMakeRange(2, 2)];
    auto referenceLine = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)reference);
    double fallbackWidth = CTLineGetTypographicBounds(referenceLine, nullptr, nullptr, nullptr);
    Check(jalium_text_format_set_font_caps(format.get(), 5, 1, "en-US", 5) == 0, "Unicase request failed");
    Check(jalium_text_format_measure_text(format.get(), Managed(u"ABẞ"), 3, 10000, 1000, &metrics) == 0, "Unicase expansion measure failed");
    Near(metrics.width, fallbackWidth, "Unicase expansion replaced the selected fallback face");
    CFRelease(referenceLine); CFRelease(smallPrimary); CFRelease(smallFallback); CFRelease(fallback); CFRelease(primary);

    Check(jalium_text_format_set_font_caps(format.get(), 1, 1, "en-US", 5) == 0, "Narrow caps request failed");
    const uint16_t sharp[] = {0x00df}; JaliumTextSpan sharpSpan{format.get(), 0, 1, 0, 0, 0, 1};
    Paragraph narrow(jalium_text_paragraph_create_with_wrapping(format.get(), sharp, 1, &sharpSpan, 1,
        Width(@"S") + .1, 40, 0, 0, 2), jalium_text_paragraph_destroy);
    Check(narrow != nullptr, "Narrow expanded paragraph missing");
    Check(jalium_text_paragraph_line_count(narrow.get()) == 1, "Wrapping split a synthesized source grapheme");
    JaliumParagraphCaret last{};
    Check(jalium_text_paragraph_get_caret(narrow.get(), 0, 1, 1, &last) == 0 && last.textPosition == 1, "Narrow expansion lost its trailing source caret");
    Format deletion(jalium_text_format_create(context, Managed(u"Seravek"), 32, 400, 0), jalium_text_format_destroy);
    JaliumFontFeature noSmall{0x736d6370, 0};
    Check(deletion && jalium_text_format_set_font_caps(deletion.get(), 5, 3, "tr", 2) == 0 &&
        jalium_text_format_set_font_features(deletion.get(), &noSmall, 1) == 0, "Contextual deletion format missing");
    const uint16_t dotted[] = {'I', 0x0307}; JaliumTextSpan deletionSpan{deletion.get(), 0, 2, 0, 0, 0, 1};
    Paragraph deleted(jalium_text_paragraph_create(deletion.get(), dotted, 2, &deletionSpan, 1, 10000, 44, 0, 0), jalium_text_paragraph_destroy);
    Check(deleted != nullptr, "Contextual deletion paragraph missing");
    JaliumParagraphLineMetrics deletedLine{};
    Check(jalium_text_paragraph_get_line(deleted.get(), 0, &deletedLine) == 0 && deletedLine.line.length == 2,
        "Deleting a paint scalar changed the line's source length");
    JaliumParagraphCaret deletedEnd{};
    Check(jalium_text_paragraph_get_caret(deleted.get(), 0, 2, 0, &deletedEnd) == 0 && deletedEnd.textPosition == 2,
        "Contextual deletion exposed a paint caret index");
    Near(deletedEnd.x, deletedLine.line.width, "Contextual deletion lost the final source caret");
    JaliumTextRangeMetrics deletedSelection{}; count = 0;
    Check(jalium_text_paragraph_get_selection(deleted.get(), 0, 0, 2, &deletedSelection, 1, &count) == 0 &&
        count == 1 && deletedSelection.length == 2, "Contextual deletion changed selection ownership");
    Near(deletedSelection.width, deletedLine.line.width, "Contextual deletion selected a paint-only interval");
    Check(jalium_text_format_set_font_caps(format.get(), 7, 1, nullptr, 0) == JALIUM_ERROR_INVALID_ARGUMENT, "Unknown caps variant accepted");
    Check(jalium_text_format_set_font_caps(format.get(), 1, 8, nullptr, 0) == JALIUM_ERROR_INVALID_ARGUMENT, "Invalid synthesis flag accepted");
    Check(jalium_text_format_set_font_caps(format.get(), 1, 1, "en_US", 5) == JALIUM_ERROR_INVALID_ARGUMENT, "Invalid caps language accepted");
    Check(jalium_text_format_set_font_caps(format.get(), 1, 1, nullptr, 1) == JALIUM_ERROR_INVALID_ARGUMENT, "Null language accepted with nonzero length");
    Check(jalium_text_format_set_font_caps(format.get(), 1, 1, "en", 129) == JALIUM_ERROR_INVALID_ARGUMENT, "Oversized language buffer accepted");
}
}

int main() {
    @autoreleasepool {
        [NSApplication.sharedApplication setActivationPolicy:NSApplicationActivationPolicyProhibited];
        try {
            TransformMap(); ContextualTransform();
            for (auto backend : {JALIUM_BACKEND_METAL, JALIUM_BACKEND_SOFTWARE}) {
                std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context(jalium_context_create(backend), jalium_context_destroy);
                Check(context != nullptr, "Caps backend unavailable"); Native(context.get()); FragmentContext(context.get()); CombiningContinuation(context.get());
            }
            std::printf("Caps synthesis: %d checks passed\n", checks); return 0;
        } catch (const std::exception& error) { std::fprintf(stderr, "Caps synthesis failed: %s\n", error.what()); return 1; }
    }
}
