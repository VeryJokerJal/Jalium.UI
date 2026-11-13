#include "jalium_api.h"
#include "font_face.h"
#include "text_shaper.h"
#import <AppKit/AppKit.h>
#include <cmath>
#include <cstdio>
#include <memory>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
int checks = 0, changedReferences = 0;
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); ++checks; }
const wchar_t* Managed(const char16_t* text) { return reinterpret_cast<const wchar_t*>(text); }
uint32_t Tag(const char* text) { return uint32_t(text[0]) << 24 | uint32_t(text[1]) << 16 | uint32_t(text[2]) << 8 | uint32_t(text[3]); }
using Format = std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)>;
using Paragraph = std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)>;

CTFontRef Reference(NSString* name, const std::vector<JaliumFontFeature>& features)
{
    CTFontRef base = CTFontCreateWithName((__bridge CFStringRef)name, 32, nullptr);
    NSMutableArray* settings = [NSMutableArray new];
    for (auto feature : features) {
        char tag[] = {char(feature.tag >> 24), char(feature.tag >> 16), char(feature.tag >> 8), char(feature.tag), 0};
        [settings addObject:@{(__bridge id)kCTFontOpenTypeFeatureTag:[NSString stringWithUTF8String:tag],
            (__bridge id)kCTFontOpenTypeFeatureValue:@(feature.value)}];
    }
    CTFontDescriptorRef descriptor = CTFontDescriptorCreateWithAttributes((__bridge CFDictionaryRef)@{
        (__bridge id)kCTFontFeatureSettingsAttribute:settings});
    CTFontRef result = CTFontCreateCopyWithAttributes(base, 32, nullptr, descriptor);
    CFRelease(base); CFRelease(descriptor); return result;
}

double Compare(JaliumTextFormat* format, CTFontRef font)
{
    const std::u16string text = u"111 888 1234 1/2 3/4 0 1st";
    NSString* string = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(text.data()) length:text.size()];
    NSAttributedString* attributed = [[NSAttributedString alloc] initWithString:string attributes:@{NSFontAttributeName:(__bridge id)font}];
    CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)attributed);
    double expected = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
    JaliumTextMetrics metrics{};
    Check(jalium_text_format_measure_text(format, Managed(text.c_str()), text.size(), 100000, 1000, &metrics) == JALIUM_OK,
        "Feature measurement failed");
    if (std::abs(metrics.width - expected) >= .002) std::fprintf(stderr, "feature width actual=%g expected=%g\n", metrics.width, expected);
    Check(std::abs(metrics.width - expected) < .002, "Feature measurement differs from independent CoreText oracle");
    for (uint32_t position : {0u, 1u, 3u, 4u, 7u, 12u, uint32_t(text.size())}) {
        JaliumTextHitTestResult hit{};
        Check(jalium_text_format_hit_test_text_position(format, Managed(text.c_str()), text.size(), 100000, 1000,
            position, 0, &hit) == JALIUM_OK, "Feature caret query failed");
        Check(std::abs(hit.caretX - CTLineGetOffsetForStringIndex(line, position, nullptr)) < .002,
            "Feature caret differs from independently shaped source position");
    }
    CFRelease(line); return metrics.width;
}

bool ReferenceFeature(CTFontRef font, CTFontTableTag tableTag, uint32_t tag)
{
    CFDataRef table = CTFontCopyTable(font, tableTag, kCTFontTableOptionNoOptions);
    if (!table) return false;
    const auto* bytes = CFDataGetBytePtr(table); size_t size = CFDataGetLength(table); bool found = false;
    auto u16 = [&](size_t at) { return unsigned(bytes[at]) * 256 + bytes[at + 1]; };
    if (size >= 10) {
        size_t list = u16(6);
        if (list <= size - 2) {
            size_t count = u16(list);
            if (count <= (size - list - 2) / 6)
                for (size_t i = 0; i < count; ++i) {
                    size_t at = list + 2 + i * 6;
                    uint32_t current = uint32_t(u16(at)) << 16 | u16(at + 2);
                    found |= current == tag;
                }
        }
    }
    CFRelease(table); return found;
}

void FeatureQueries(JaliumTextFormat* format, CTFontRef reference)
{
    const uint32_t tags[] = {Tag("smcp"), Tag("c2sc"), Tag("pcap"), Tag("c2pc"), Tag("kern"), Tag("XXXX"), Tag("smcp")};
    uint8_t supported[7];
    for (uint32_t tables : {1u, 2u, 3u}) {
        Check(jalium_text_format_get_font_feature_support(format, tables, tags, 7, supported) == JALIUM_OK, "Feature availability query failed");
        for (unsigned i = 0; i < 7; ++i) {
            bool expected = ((tables & 1) && ReferenceFeature(reference, kCTFontTableGSUB, tags[i])) ||
                ((tables & 2) && ReferenceFeature(reference, kCTFontTableGPOS, tags[i]));
            Check(supported[i] == expected, "Feature availability differs from independently read CoreText table");
        }
    }
    Check(jalium_text_format_get_font_feature_support(format, 1, nullptr, 0, nullptr) == JALIUM_OK, "Empty feature query failed");
    Check(jalium_text_format_get_font_feature_support(nullptr, 1, tags, 7, supported) == JALIUM_ERROR_INVALID_ARGUMENT, "Null format accepted for feature query");
    Check(jalium_text_format_get_font_feature_support(format, 0, tags, 7, supported) == JALIUM_ERROR_INVALID_ARGUMENT, "Invalid feature table mask accepted");
    Check(jalium_text_format_get_font_feature_support(format, 4, tags, 7, supported) == JALIUM_ERROR_INVALID_ARGUMENT, "Unknown feature table accepted");
    Check(jalium_text_format_get_font_feature_support(format, 1, nullptr, 7, supported) == JALIUM_ERROR_INVALID_ARGUMENT, "Null feature query tags accepted");
    Check(jalium_text_format_get_font_feature_support(format, 1, tags, 7, nullptr) == JALIUM_ERROR_INVALID_ARGUMENT, "Null feature query output accepted");
    Check(jalium_text_format_get_font_feature_support(format, 1, tags, 16385, supported) == JALIUM_ERROR_INVALID_ARGUMENT, "Oversized feature query accepted");
    const uint32_t invalid = 0; supported[0] = 123;
    Check(jalium_text_format_get_font_feature_support(format, 1, &invalid, 1, supported) == JALIUM_ERROR_INVALID_ARGUMENT && supported[0] == 123,
        "Invalid query tag altered output");
}

void NativeFeatures(JaliumContext* context)
{
    const std::vector<std::vector<JaliumFontFeature>> cases = {
        {{Tag("lnum"), 1}}, {{Tag("onum"), 1}}, {{Tag("pnum"), 1}}, {{Tag("tnum"), 1}},
        {{Tag("frac"), 1}}, {{Tag("afrc"), 1}}, {{Tag("ordn"), 1}}, {{Tag("zero"), 1}},
        {{Tag("onum"), 1}, {Tag("tnum"), 1}, {Tag("frac"), 1}, {Tag("ordn"), 1}, {Tag("zero"), 1}},
        {{Tag("tnum"), 0}}, {{Tag("XXXX"), 1}}, {},
    };
    for (auto family : {u"Avenir Next", u"Seravek", u"Trattatello"}) {
        NSString* name = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(family) length:std::char_traits<char16_t>::length(family)];
        Format format(jalium_text_format_create(context, Managed(family), 32, 400, 0), jalium_text_format_destroy);
        Check(format != nullptr, "Feature font creation failed"); jalium_text_format_set_word_wrapping(format.get(), 1);
        CTFontRef regular = Reference(name, {}); double normal = Compare(format.get(), regular);
        FeatureQueries(format.get(), regular);
        for (const auto& features : cases) {
            Check(jalium_text_format_set_font_features(format.get(), features.data(), features.size()) == JALIUM_OK, "Feature selection failed");
            FeatureQueries(format.get(), regular);
            CTFontRef expected = Reference(name, features); double width = Compare(format.get(), expected); CFRelease(expected);
            changedReferences += std::abs(width - normal) > .01;
            Check(jalium_text_format_set_font_features(format.get(), nullptr, 0) == JALIUM_OK, "Feature reset failed");
            Check(std::abs(Compare(format.get(), regular) - normal) < .002, "Normal reset retained old feature settings");
        }
        CFRelease(regular);
        if (std::u16string(family) == u"Seravek") {
            const std::u16string digits = u"111111";
            const JaliumFontFeature tabular{Tag("tnum"), 1}, proportional{Tag("pnum"), 1};
            Check(jalium_text_format_set_font_features(format.get(), &tabular, 1) == JALIUM_OK, "Retained paragraph feature selection failed");
            JaliumTextSpan span{format.get(), 0, static_cast<uint32_t>(digits.size()), 0, 0, 0, 1};
            auto make = [&] { return Paragraph(jalium_text_paragraph_create_with_wrapping(format.get(),
                reinterpret_cast<const uint16_t*>(digits.data()), digits.size(), &span, 1, 10000, 44, 0, 0, 1), jalium_text_paragraph_destroy); };
            auto retained = make(); Check(retained != nullptr, "Retained paragraph could not create");
            JaliumParagraphCaret before{}, after{}, freshCaret{};
            Check(jalium_text_paragraph_get_caret(retained.get(), 0, digits.size(), 0, &before) == JALIUM_OK, "Retained paragraph caret unavailable");
            Check(jalium_text_format_set_font_features(format.get(), &proportional, 1) == JALIUM_OK, "Paragraph source feature update failed");
            Check(jalium_text_paragraph_get_caret(retained.get(), 0, digits.size(), 0, &after) == JALIUM_OK && before.x == after.x,
                "Existing immutable paragraph changed after its format feature mutation");
            auto fresh = make(); Check(fresh != nullptr, "Fresh feature paragraph could not create");
            Check(jalium_text_paragraph_get_caret(fresh.get(), 0, digits.size(), 0, &freshCaret) == JALIUM_OK && std::abs(before.x - freshCaret.x) > 1,
                "Fresh paragraph retained the old numeric feature");
        }
        const JaliumFontFeature good{Tag("tnum"), 1}, invalid{0, 1};
        Check(jalium_text_format_set_font_features(nullptr, &good, 1) == JALIUM_ERROR_INVALID_ARGUMENT, "Null format accepted");
        Check(jalium_text_format_set_font_features(format.get(), nullptr, 1) == JALIUM_ERROR_INVALID_ARGUMENT, "Null feature array accepted");
        Check(jalium_text_format_set_font_features(format.get(), &good, 16385) == JALIUM_ERROR_INVALID_ARGUMENT, "Oversized array accepted");
        Check(jalium_text_format_set_font_features(format.get(), &invalid, 1) == JALIUM_ERROR_INVALID_ARGUMENT, "Invalid tag accepted");
    }
    Check(changedReferences >= 9, "Font fixtures did not actually change numeric glyphs or spacing");
    Format kerning(jalium_text_format_create(context, Managed(u"Seravek"), 32, 400, 0), jalium_text_format_destroy);
    Check(kerning != nullptr, "Kerning fixture unavailable");
    const std::u16string text = u"AVATAR To WA";
    CTFontRef regular = Reference(@"Seravek", {});
    NSString* source = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(text.data()) length:text.size()];
    NSAttributedString* attributed = [[NSAttributedString alloc] initWithString:source attributes:@{
        NSFontAttributeName:(__bridge id)regular, NSKernAttributeName:@0}];
    CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)attributed);
    const JaliumFontFeature kernOff{Tag("kern"), 0};
    Check(jalium_text_format_set_font_features(kerning.get(), &kernOff, 1) == JALIUM_OK, "Kerning disable failed");
    JaliumTextMetrics metrics{};
    Check(jalium_text_format_measure_text(kerning.get(), Managed(text.c_str()), text.size(), 100000, 1000, &metrics) == JALIUM_OK,
        "Kerning measurement failed");
    Check(std::abs(metrics.width - CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr)) < .002,
        "Kerning off differs from an independent CoreText kern attribute");
    for (uint32_t position = 0; position <= text.size(); ++position) {
        JaliumTextHitTestResult hit{};
        Check(jalium_text_format_hit_test_text_position(kerning.get(), Managed(text.c_str()), text.size(), 100000, 1000,
            position, 0, &hit) == JALIUM_OK, "Kerning caret query failed");
        Check(std::abs(hit.caretX - CTLineGetOffsetForStringIndex(line, position, nullptr)) < .002,
            "Kerning caret differs from the independent source geometry");
    }
    CFRelease(line); CFRelease(regular);
}

void ControlledParagraphs(JaliumContext* context)
{
    Format format(jalium_text_format_create(context, Managed(u"Seravek"), 32, 400, 0), jalium_text_format_destroy);
    Check(format != nullptr, "Bidi control fixture unavailable");
    const std::u16string text = u"中\u202eA \u202cB";
    JaliumTextSpan span{format.get(), 0, static_cast<uint32_t>(text.size()), 0, 0, 0, 1};
    std::vector<JaliumTextAdvanceUnit> units;
    for (uint32_t i = 0; i < text.size(); ++i) units.push_back({i, 1, 0, 0, 0, text[i] == 0x202e || text[i] == 0x202c ? 1u : 0u});
    Paragraph plain(jalium_text_paragraph_create_with_wrapping(format.get(), reinterpret_cast<const uint16_t*>(text.data()),
        text.size(), &span, 1, 100000, 44, 0, 0, 1), jalium_text_paragraph_destroy);
    Paragraph advanced(jalium_text_paragraph_create_context_advanced(format.get(), reinterpret_cast<const uint16_t*>(text.data()),
        text.size(), &span, 1, units.data(), units.size(), 100000, 44, 0), jalium_text_paragraph_destroy);
    JaliumTextMetrics chinese{}, latin{}, space{};
    Check(jalium_text_format_measure_text(format.get(), Managed(u"中"), 1, 100000, 44, &chinese) == JALIUM_OK &&
        jalium_text_format_measure_text(format.get(), Managed(u"A"), 1, 100000, 44, &latin) == JALIUM_OK &&
        jalium_text_format_measure_text(format.get(), Managed(u" "), 1, 100000, 44, &space) == JALIUM_OK, "Bidi reference unavailable");
    for (auto paragraph : {plain.get(), advanced.get()}) {
        Check(paragraph != nullptr, "Controlled paragraph unavailable");
        JaliumTextRangeMetrics ink{};
        Check(jalium_text_paragraph_get_source_bounds(paragraph, 0, 0, 1, &ink) == JALIUM_OK && ink.width > 0,
            "Trailing bidi control stole the preceding glyph's source ownership");
        for (auto expectation : {std::pair<uint32_t, double>{1, chinese.width}, {4, chinese.width},
            {3, chinese.width + space.widthIncludingTrailingWhitespace}, {6, chinese.width + space.widthIncludingTrailingWhitespace + latin.width}}) {
            JaliumParagraphCaret caret{};
            // Positions 1/4 are the trailing Chinese/space edges; position 3
            // is the trailing RTL A; position 6 here uses the leading B edge.
            uint32_t position = expectation.first == 6 ? 5 : expectation.first;
            Check(jalium_text_paragraph_get_caret(paragraph, 0, position, expectation.first == 6 ? 0 : 1, &caret) == JALIUM_OK &&
                std::abs(caret.x - expectation.second) < .002, "Bidi control exposed the opposite run's caret");
        }
    }
}

void AdvancedParagraphs(JaliumContext* context)
{
    Check(sizeof(JaliumTextAdvanceUnit) == 24 && sizeof(JaliumTextSpacingUnit) == 16, "Advance ABI layout changed");
    Format format(jalium_text_format_create(context, Managed(u"Seravek"), 32, 400, 0), jalium_text_format_destroy);
    const std::vector<JaliumFontFeature> features{{Tag("kern"), 1}, {Tag("liga"), 1}};
    Check(format && jalium_text_format_set_font_features(format.get(), features.data(), features.size()) == JALIUM_OK,
        "Advance fixture unavailable");
    CTFontRef font = Reference(@"Seravek", features);
    for (const std::u16string text : {u"AV ffi AV", u" AV ", u"AV\u00a0ffi", u"\u05d0\u05d1 AV"}) {
        NSString* source = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(text.data()) length:text.size()];
        NSAttributedString* attributed = [[NSAttributedString alloc] initWithString:source attributes:@{NSFontAttributeName:(__bridge id)font}];
        CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)attributed);
        const double natural = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
        for (float word : {0.f, 6.f, -3.f, -100.f}) for (float tracking : {0.f, 2.f}) {
            if (word < -50 && tracking != 0) continue;
            // Bidi tracking is covered by the existing visual-order fixture.
            if (text[0] == 0x05d0 && tracking != 0) continue;
            std::vector<JaliumTextAdvanceUnit> units;
            double extra = 0;
            for (uint32_t i = 0; i < text.size(); ++i) {
                const bool space = text[i] == ' ' || text[i] == 0xa0;
                units.push_back({i, 1, tracking, space ? word / 2 : 0, space ? word / 2 : 0, space ? 4u : 0u});
                if (space) {
                    const double width = std::abs(CTLineGetOffsetForStringIndex(line, i + 1, nullptr) - CTLineGetOffsetForStringIndex(line, i, nullptr));
                    // The mixed-direction space's two affinities are distinct;
                    // positive word spacing has the same total in either order.
                    extra += word < -50 ? -width : word;
                }
            }
            if (text[0] == 0x05d0 && word < -50) continue;
            JaliumTextSpan span{format.get(), 0, static_cast<uint32_t>(text.size()), 0, 0, 0, 1};
            for (auto create : {jalium_text_paragraph_create_advanced, jalium_text_paragraph_create_context_advanced}) {
                Paragraph paragraph(create(format.get(), reinterpret_cast<const uint16_t*>(text.data()), text.size(), &span, 1,
                    units.data(), units.size(), 100000, 44, 0), jalium_text_paragraph_destroy);
                Check(paragraph != nullptr, "Advanced paragraph creation failed");
                JaliumParagraphLineMetrics metrics{};
                Check(jalium_text_paragraph_get_line(paragraph.get(), 0, &metrics) == JALIUM_OK, "Advanced line unavailable");
                Check(std::abs(metrics.line.width - natural - extra - (text.size() - 1) * tracking) < .002,
                    "Word spacing lost whole CoreText shaping or used the wrong separator width");
                for (uint32_t i = 0; i <= text.size(); ++i) for (int backward : {0, 1}) {
                    JaliumParagraphCaret caret{}, hit{}, roundtrip{};
                    Check(jalium_text_paragraph_get_caret(paragraph.get(), 0, i, backward, &caret) == JALIUM_OK, "Advanced caret unavailable");
                    Check(jalium_text_paragraph_hit_test(paragraph.get(), 0, caret.x, &hit) == JALIUM_OK &&
                        jalium_text_paragraph_get_caret(paragraph.get(), 0, hit.textPosition, hit.backwardAffinity, &roundtrip) == JALIUM_OK &&
                        std::abs(caret.x - roundtrip.x) < .002, "Advanced hit and caret disagree");
                }
                JaliumTextRangeMetrics selection{}; uint32_t count = 0;
                Check(jalium_text_paragraph_get_selection(paragraph.get(), 0, 0, text.size(), &selection, 1, &count) == JALIUM_OK && count == 1 &&
                    std::abs(selection.width - metrics.line.width) < .002, "Advanced selection disagrees with its line");
            }
        }
        CFRelease(line);
    }
    CFRelease(font);
    const uint16_t text[]{'A', ' '}; JaliumTextSpan span{format.get(), 0, 2, 0, 0, 0, 1};
    JaliumTextAdvanceUnit units[]{{0, 1, 2, 0, 0, 0}, {1, 1, 2, 3, 3, 4}};
    for (auto create : {jalium_text_paragraph_create_advanced, jalium_text_paragraph_create_context_advanced}) {
        auto invalid = [&](const JaliumTextAdvanceUnit* values, uint32_t count, float width = 1000, int direction = 0) {
            Paragraph rejected(create(format.get(), text, 2, &span, 1, values, count, width, 44, direction), jalium_text_paragraph_destroy);
            Check(!rejected, "Malformed advance input was accepted");
        };
        invalid(nullptr, 2); invalid(units, 0); invalid(units, 1); invalid(units, 3); invalid(units, 2, 0); invalid(units, 2, 1000, 2);
        for (auto broken : {JaliumTextAdvanceUnit{1, 1, 2, 0, 0, 0}, {0, 0, 2, 0, 0, 0}, {0, 3, 2, 0, 0, 0},
            {0, 1, 2, std::numeric_limits<float>::quiet_NaN(), 0, 0}, {0, 1, 2, 0, std::numeric_limits<float>::infinity(), 0},
            {0, 1, 2, 1000001, 0, 0}, {0, 1, 2, 0, 0, 8}}) {
            auto saved = units[0]; units[0] = broken; invalid(units, 2); units[0] = saved;
        }
        const uint16_t invisible[]{0x200b}; JaliumTextSpan hiddenSpan{format.get(), 0, 1, 0, 0, 0, 1};
        JaliumTextAdvanceUnit hidden{0, 1, 0, 5, 5, 4};
        Paragraph paragraph(create(format.get(), invisible, 1, &hiddenSpan, 1, &hidden, 1, 1000, 44, 0), jalium_text_paragraph_destroy);
        JaliumParagraphLineMetrics metrics{};
        Check(paragraph && jalium_text_paragraph_get_line(paragraph.get(), 0, &metrics) == JALIUM_OK && metrics.line.width == 0,
            "A zero-width separator acquired word spacing");
    }
}

void FormattingControlsPreserveFallbackGlyphs(JaliumContext* context)
{
    Format format(jalium_text_format_create(context, Managed(u"Seravek"), 22, 400, 0), jalium_text_format_destroy);
    Check(format != nullptr, "Fallback control fixture unavailable");
    const uint16_t reference[]{0x4e59};
    JaliumTextSpan referenceSpan{format.get(), 0, 1, 0, 0, 0, 1};
    JaliumTextAdvanceUnit visible{0, 1, 0, 0, 0, 0};
    for (int direction : {0, 1}) for (auto create : {jalium_text_paragraph_create_advanced, jalium_text_paragraph_create_context_advanced}) {
        Paragraph expected(create(format.get(), reference, 1, &referenceSpan, 1, &visible, 1, 100000, 44, direction), jalium_text_paragraph_destroy);
        JaliumParagraphLineMetrics referenceMetrics{};
        Check(expected && jalium_text_paragraph_get_line(expected.get(), 0, &referenceMetrics) == JALIUM_OK && referenceMetrics.line.width > 1,
            "Reference fallback glyph unavailable");
        for (uint16_t control : {0x200b, 0x2060, 0xfeff}) {
            const uint16_t text[]{reference[0], control}; JaliumTextSpan span{format.get(), 0, 2, 0, 0, 0, 1};
            const JaliumTextAdvanceUnit units[]{{0, 1, 0, 0, 0, 0}, {1, 1, 0, 0, 0, 1}};
            Paragraph paragraph(create(format.get(), text, 2, &span, 1, units, 2, 100000, 44, direction), jalium_text_paragraph_destroy);
            JaliumParagraphLineMetrics metrics{}; JaliumTextRangeMetrics selected{}; uint32_t count = 0;
            Check(paragraph && jalium_text_paragraph_get_line(paragraph.get(), 0, &metrics) == JALIUM_OK &&
                std::abs(metrics.line.width - referenceMetrics.line.width) < .002, "Formatting control collapsed the preceding fallback glyph");
            Check(jalium_text_paragraph_get_selection(paragraph.get(), 0, 0, 1, &selected, 1, &count) == JALIUM_OK && count == 1 &&
                std::abs(selected.width - referenceMetrics.line.width) < .002, "Fallback source selection lost its glyph width");
        }
    }
}

void SpacedParagraphs(JaliumContext* context)
{
    Check(sizeof(JaliumTextSpacingUnit) == 16, "Spacing ABI layout changed");
    Format format(jalium_text_format_create(context, Managed(u"Seravek"), 32, 400, 0), jalium_text_format_destroy);
    const std::vector<JaliumFontFeature> features{{Tag("kern"), 1}, {Tag("liga"), 0}, {Tag("clig"), 0}};
    Check(format && jalium_text_format_set_font_features(format.get(), features.data(), features.size()) == JALIUM_OK,
        "Spacing fixture unavailable");
    CTFontRef font = Reference(@"Seravek", features);
    for (const std::u16string text : {u"AV", u"To WA", u"AVATAR"}) {
        NSString* source = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(text.data()) length:text.size()];
        NSAttributedString* attributed = [[NSAttributedString alloc] initWithString:source attributes:@{NSFontAttributeName:(__bridge id)font}];
        CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)attributed);
        double width = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
        for (float spacing : {0.f, 2.f, -1.f, .0002f}) {
            JaliumTextSpan span{format.get(), 0, static_cast<uint32_t>(text.size()), 0, 0, 0, 1};
            std::vector<JaliumTextSpacingUnit> units;
            for (uint32_t i = 0; i < text.size(); ++i) units.push_back({i, 1, spacing, 0});
            Paragraph paragraph(jalium_text_paragraph_create_spaced(format.get(), reinterpret_cast<const uint16_t*>(text.data()),
                text.size(), &span, 1, units.data(), units.size(), 100000, 44, 0), jalium_text_paragraph_destroy);
            Check(paragraph && jalium_text_paragraph_line_count(paragraph.get()) == 1, "Spaced paragraph creation failed");
            JaliumParagraphLineMetrics metrics{};
            Check(jalium_text_paragraph_get_line(paragraph.get(), 0, &metrics) == JALIUM_OK, "Spaced line unavailable");
            Check(std::abs(metrics.line.width - width - (text.size() - 1) * spacing) < .002,
                "Tracking lost whole-line CoreText kerning or added a line-edge gap");
            for (uint32_t i = 0; i <= text.size(); ++i) {
                JaliumParagraphCaret caret{}, hit{};
                Check(jalium_text_paragraph_get_caret(paragraph.get(), 0, i, 0, &caret) == JALIUM_OK, "Spaced caret unavailable");
                double extra = i == 0 ? 0 : i == text.size() ? (i - 1) * spacing : (i - .5) * spacing;
                Check(std::abs(caret.x - CTLineGetOffsetForStringIndex(line, i, nullptr) - extra) < .002,
                    "Tracking caret disagrees with independent CoreText geometry");
                Check(jalium_text_paragraph_hit_test(paragraph.get(), 0, caret.x, &hit) == JALIUM_OK && hit.textPosition == i,
                    "Tracking hit test disagrees with its caret");
            }
            JaliumTextRangeMetrics selection{}; uint32_t count = 0;
            Check(jalium_text_paragraph_get_selection(paragraph.get(), 0, 0, text.size(), &selection, 1, &count) == JALIUM_OK && count == 1,
                "Spaced selection failed");
            Check(std::abs(selection.width - metrics.line.width) < .002, "Spaced selection disagrees with line geometry");
        }
        CFRelease(line);
    }
    CFRelease(font);
    const uint16_t text[]{'A', 'V'}; JaliumTextSpan span{format.get(), 0, 2, 0, 0, 0, 1};
    JaliumTextSpacingUnit units[]{{0, 1, 2, 0}, {1, 1, 2, 0}};
    auto invalid = [&](const uint16_t* source, uint32_t length, const JaliumTextSpan* spans, uint32_t spanCount,
        const JaliumTextSpacingUnit* values, uint32_t count, float width, float height, int32_t direction) {
        for (auto create : {jalium_text_paragraph_create_spaced, jalium_text_paragraph_create_context_spaced}) {
            Paragraph rejected(create(format.get(), source, length, spans, spanCount,
                values, count, width, height, direction), jalium_text_paragraph_destroy);
            Check(!rejected, "Malformed spaced paragraph input was accepted");
        }
    };
    invalid(nullptr, 2, &span, 1, units, 2, 1000, 44, 0);
    invalid(text, 2, nullptr, 1, units, 2, 1000, 44, 0);
    invalid(text, 2, &span, 1, nullptr, 2, 1000, 44, 0);
    invalid(text, 2, &span, 1, units, 3, 1000, 44, 0);
    invalid(text, 2, &span, 1, units, 1, 1000, 44, 0);
    invalid(text, 2, &span, 1, units, 2, 0, 44, 0);
    invalid(text, 2, &span, 1, units, 2, 1000, -1, 0);
    invalid(text, 2, &span, 1, units, 2, 1000, 44, 2);
    for (auto broken : {JaliumTextSpacingUnit{1, 1, 2, 0}, {0, 0, 2, 0}, {0, 3, 2, 0},
        {0, 1, std::numeric_limits<float>::quiet_NaN(), 0}, {0, 1, 1000001, 0}, {0, 1, 2, 4}}) {
        auto saved = units[0]; units[0] = broken;
        invalid(text, 2, &span, 1, units, 2, 1000, 44, 0); units[0] = saved;
    }
    const uint16_t invisible[]{0x200b, 0x2060}; units[0].flags = units[1].flags = 1;
    Paragraph ignored(jalium_text_paragraph_create_spaced(format.get(), invisible, 2, &span, 1,
        units, 2, 1000, 44, 0), jalium_text_paragraph_destroy);
    Check(ignored != nullptr, "Formatting-only paragraph failed");
    JaliumParagraphLineMetrics metrics{};
    Check(jalium_text_paragraph_get_line(ignored.get(), 0, &metrics) == JALIUM_OK && metrics.line.width == 0,
        "Invisible formatting acquired tracking width");
    for (uint32_t i = 0; i <= 2; ++i) {
        JaliumParagraphCaret caret{};
        Check(jalium_text_paragraph_get_caret(ignored.get(), 0, i, 0, &caret) == JALIUM_OK && caret.x == 0,
            "Formatting-only paragraph lost a source position");
    }
}

void ContextParagraphs(JaliumContext* context)
{
    Format format(jalium_text_format_create(context, Managed(u"Seravek"), 32, 400, 0), jalium_text_format_destroy);
    const std::vector<JaliumFontFeature> features{{Tag("kern"), 1}, {Tag("liga"), 1}, {Tag("clig"), 1}};
    Check(format && jalium_text_format_set_font_features(format.get(), features.data(), features.size()) == JALIUM_OK,
        "Context-paint fixture unavailable");
    CTFontRef font = Reference(@"Seravek", features);
    for (const std::u16string text : {u"AV", u"ffi", u"a\u0301b", u"\U0001f469\u200d\U0001f4bb\U0001f642", u"\u0645\u0631\u062d\u0628\u0627"}) {
        NSString* source = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(text.data()) length:text.size()];
        NSAttributedString* attributed = [[NSAttributedString alloc] initWithString:source attributes:@{NSFontAttributeName:(__bridge id)font}];
        CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)attributed);
        const double natural = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
        for (uint32_t seam = 1; seam < text.size(); ++seam) for (float spacing : {0.f, 2.f, -1.f}) {
            // Paint is supplied to the renderer after shaping. Even unequal
            // RGB/alpha and a seam inside a cluster must leave shaping intact.
            const JaliumTextSpan spans[]{{format.get(), 0, seam, 1, 0, 0, .3f},
                {format.get(), seam, uint32_t(text.size()) - seam, 0, 1, 0, .8f}};
            std::vector<JaliumTextSpacingUnit> units;
            for (NSUInteger position = 0; position < source.length;) {
                NSRange cluster = [source rangeOfComposedCharacterSequenceAtIndex:position];
                units.push_back({uint32_t(cluster.location), uint32_t(cluster.length), spacing, text[0] == 0x645 ? 2u : 0u});
                position = NSMaxRange(cluster);
            }
            Paragraph paragraph(jalium_text_paragraph_create_context_spaced(format.get(), reinterpret_cast<const uint16_t*>(text.data()),
                text.size(), spans, 2, units.data(), units.size(), 100000, 44, 0), jalium_text_paragraph_destroy);
            Check(paragraph != nullptr, "Context-paint paragraph failed");
            JaliumParagraphLineMetrics metrics{};
            Check(jalium_text_paragraph_get_line(paragraph.get(), 0, &metrics) == JALIUM_OK,
                "Context-paint line unavailable");
            const double extra = text[0] == 0x645 ? 0 : (units.size() - 1) * spacing;
            if (std::abs(metrics.line.width - natural - extra) >= .002)
                std::fprintf(stderr, "Context shape first=%x seam=%u spacing=%g actual=%g natural=%g extra=%g units=%zu\n", unsigned(text[0]), seam, spacing, metrics.line.width, natural, extra, units.size());
            Check(std::abs(metrics.line.width - natural - extra) < .002,
                "A paint/source seam changed whole-line shaping");
            uint32_t count = 0;
            Check(jalium_text_paragraph_get_fragments(paragraph.get(), 0, nullptr, 0, &count) == JALIUM_OK && count > 0,
                "Context-paint paragraph lost visual runs");
            JaliumTextRangeMetrics full{}, left{}, right{}, empty{};
            Check(jalium_text_paragraph_get_source_bounds(paragraph.get(), 0, 0, text.size(), &full) == JALIUM_OK && full.width > 0,
                "Shared glyph ink bounds unavailable");
            Check(jalium_text_paragraph_get_source_bounds(paragraph.get(), 0, 0, seam, &left) == JALIUM_OK &&
                jalium_text_paragraph_get_source_bounds(paragraph.get(), 0, seam, text.size() - seam, &right) == JALIUM_OK,
                "Source-owned glyph bounds unavailable");
            const double minimum = left.width > 0 ? right.width > 0 ? std::min(left.x, right.x) : left.x : right.x;
            const double maximum = std::max(left.width > 0 ? left.x + left.width : -1e20f, right.width > 0 ? right.x + right.width : -1e20f);
            Check(std::abs(minimum - full.x) < .002 && std::abs(maximum - full.x - full.width) < .002,
                "Source-owned ink does not partition the whole glyph bounds");
            Check(jalium_text_paragraph_get_source_bounds(paragraph.get(), 0, seam, 0, &empty) == JALIUM_OK && empty.width == 0 &&
                jalium_text_paragraph_get_source_bounds(paragraph.get(), 0, text.size(), 1, &empty) == JALIUM_ERROR_INVALID_ARGUMENT,
                "Empty/invalid source-owned ink query failed");
        }
        CFRelease(line);
    }
    CFRelease(font);
}

void SharedShaper(const char* featureFixture)
{
    NSData* data = [NSData dataWithContentsOfFile:@"/System/Library/Fonts/SFNS.ttf"];
    Check(data.length > 0, "Shared shaper fixture missing");
    const auto* start = static_cast<const uint8_t*>(data.bytes);
    auto face = jalium::FontFace::Parse(std::vector<uint8_t>(start, start + data.length), 0);
    Check(face && face->IsValid(), "Shared shaper fixture could not parse");
    jalium::TextShaper shaper;
    auto width = [&](const wchar_t* text, uint32_t tag) {
        auto run = shaper.Shape(face.get(), 1, text, 6, 32, false, {{tag, 1}});
        Check(run.glyphs.size() == 6, "Numeric feature changed source cluster count");
        double result = 0; for (size_t i = 0; i < run.glyphs.size(); ++i) { Check(run.glyphs[i].cluster == i, "Numeric feature lost source clusters"); result += run.glyphs[i].advanceX; }
        return result;
    };
    const auto tabularOne = width(L"111111", Tag("tnum")), tabularEight = width(L"888888", Tag("tnum"));
    const auto proportionalOne = width(L"111111", Tag("pnum")), proportionalEight = width(L"888888", Tag("pnum"));
    Check(std::abs(tabularOne - tabularEight) < .002, "Shared shaper did not apply tabular numeric substitutions");
    Check(std::abs(proportionalOne - proportionalEight) > 1, "Shared shaper did not apply proportional numeric substitutions");
    NSData* fixture = [NSData dataWithContentsOfFile:[NSString stringWithUTF8String:featureFixture]];
    Check(fixture.length > 0, "WPT numeric font fixture missing");
    const auto* bytes = static_cast<const uint8_t*>(fixture.bytes);
    auto fancy = jalium::FontFace::Parse(std::vector<uint8_t>(bytes, bytes + fixture.length), 0);
    Check(fancy && fancy->IsValid(), "Shared shaper could not parse WPT numeric font");
    const char* tags[] = {"lnum", "onum", "pnum", "tnum", "frac", "afrc", "ordn", "zero"};
    auto sameShape = [&](uint16_t actual, char reference) {
        jalium::GlyphOutline left, right;
        if (!fancy->GetGlyphContours(actual, .25f, left) || !fancy->GetGlyphContours(fancy->GetGlyphIndex(reference), .25f, right) ||
            !left.hasInk || !right.hasInk || left.contours.size() != right.contours.size()) return false;
        for (size_t c = 0; c < left.contours.size(); ++c) {
            auto& one = left.contours[c].points; auto& two = right.contours[c].points;
            if (one.size() != two.size()) return false;
            for (size_t p = 0; p < one.size(); ++p) if (std::abs(one[p] - two[p]) > .001) return false;
        }
        return true;
    };
    for (int i = 0; i < 8; ++i) {
        wchar_t input = L'P' + i;
        auto normal = shaper.Shape(fancy.get(), 2, &input, 1, 32);
        auto changed = shaper.Shape(fancy.get(), 2, &input, 1, 32, false, {{Tag(tags[i]), 1}});
        auto disabled = shaper.Shape(fancy.get(), 2, &input, 1, 32, false, {{Tag(tags[i]), 0}});
        Check(normal.glyphs.size() == 1 && sameShape(normal.glyphs[0].glyphIndex, 'B'), "WPT default glyph was not the disabled cross");
        Check(changed.glyphs.size() == 1 && sameShape(changed.glyphs[0].glyphIndex, 'A'), "Shared numeric feature did not produce its WPT reference glyph");
        Check(disabled.glyphs.size() == 1 && disabled.glyphs[0].glyphIndex == normal.glyphs[0].glyphIndex, "Disabled feature did not restore the WPT cross");
        Check(changed.glyphs[0].cluster == 0, "WPT feature substitution changed its source cluster");
    }
    // WPT's independent check/cross glyphs also exercise the engine defaults.
    // Contextual/common ligatures and alternates are enabled by default;
    // discretionary and historical ligatures are opt-in.
    const char* ligatureTags[] = {"liga", "clig", "dlig", "hlig", "calt"};
    for (int i = 0; i < 5; ++i) {
        wchar_t input = L'C' + i;
        const bool defaultEnabled = i == 0 || i == 1 || i == 4;
        auto ordinary = shaper.Shape(fancy.get(), 4, &input, 1, 32);
        auto enabled = shaper.Shape(fancy.get(), 4, &input, 1, 32, false, {{Tag(ligatureTags[i]), 1}});
        auto disabled = shaper.Shape(fancy.get(), 4, &input, 1, 32, false, {{Tag(ligatureTags[i]), 0}});
        Check(ordinary.glyphs.size() == 1 && sameShape(ordinary.glyphs[0].glyphIndex, defaultEnabled ? 'A' : 'B'), "Shared default ligature/alternate feature differs from WPT");
        Check(enabled.glyphs.size() == 1 && sameShape(enabled.glyphs[0].glyphIndex, 'A'), "Enabled ligature/alternate did not produce the WPT check");
        Check(disabled.glyphs.size() == 1 && sameShape(disabled.glyphs[0].glyphIndex, 'B'), "Disabled ligature/alternate did not produce the WPT cross");
        Check(enabled.glyphs[0].cluster == 0 && disabled.glyphs[0].cluster == 0, "Ligature setting changed source cluster");
    }
    for (uint32_t index = 1; index <= 3; ++index) {
        wchar_t input = L'n' + index - 1;
        auto enabled = shaper.Shape(fancy.get(), 3, &input, 1, 32, false, {{Tag("salt"), index}});
        Check(enabled.glyphs.size() == 1 && sameShape(enabled.glyphs[0].glyphIndex, 'A'), "Alternate index did not select its independent WPT reference");
        Check(enabled.glyphs[0].cluster == 0, "Alternate substitution changed its source cluster");
        for (uint32_t other : {0u, 4u, 2147483647u}) {
            auto ordinary = shaper.Shape(fancy.get(), 3, &input, 1, 32, false, {{Tag("salt"), other}});
            Check(ordinary.glyphs.size() == 1 && sameShape(ordinary.glyphs[0].glyphIndex, 'B'), "Disabled or unavailable alternate did not preserve the default glyph");
        }
        auto lastOff = shaper.Shape(fancy.get(), 3, &input, 1, 32, false, {{Tag("salt"), index}, {Tag("salt"), 0}});
        Check(lastOff.glyphs.size() == 1 && sameShape(lastOff.glyphs[0].glyphIndex, 'B'), "Last duplicate feature did not disable an alternate");
    }
}
}

int main(int argc, char** argv)
{
    @autoreleasepool {
        [NSApplication.sharedApplication setActivationPolicy:NSApplicationActivationPolicyProhibited];
        try {
            std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context(jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy);
            Check(argc == 2, "WPT numeric fixture path required");
            Check(context != nullptr, "Feature context unavailable"); NativeFeatures(context.get()); SpacedParagraphs(context.get()); AdvancedParagraphs(context.get()); FormattingControlsPreserveFallbackGlyphs(context.get()); ControlledParagraphs(context.get()); ContextParagraphs(context.get()); SharedShaper(argv[1]);
            std::printf("Numeric feature checks: %d passed; changed native references: %d\n", checks, changedReferences); return 0;
        } catch (const std::exception& error) { std::fprintf(stderr, "Numeric feature check failed: %s\n", error.what()); return 1; }
    }
}
