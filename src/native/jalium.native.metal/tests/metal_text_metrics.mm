#include "jalium_api.h"
#import <CoreText/CoreText.h>
#include <cmath>
#include <cstdio>
#include <memory>
#include <string>
#include <stdexcept>
#include <vector>

namespace {
int checks = 0;
void Check(bool value, const char* message)
{
    ++checks;
    if (!value) throw std::runtime_error(message);
}
const wchar_t* Managed(const char16_t* text) { return reinterpret_cast<const wchar_t*>(text); }
using Format = std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)>;
void TestLine(JaliumContext* context, const char16_t* family, float size, const std::u16string& text)
{
    Format format(jalium_text_format_create(context, Managed(family), size, 400, 0), jalium_text_format_destroy);
    Check(format != nullptr, "Text format creation failed");
    jalium_text_format_set_word_wrapping(format.get(), 1);
    CFStringRef name = CFStringCreateWithCharacters(nullptr, reinterpret_cast<const UniChar*>(family), std::char_traits<char16_t>::length(family));
    CTFontRef font = CTFontCreateWithName(name, size, nullptr);
    CFStringRef string = CFStringCreateWithCharacters(nullptr, reinterpret_cast<const UniChar*>(text.data()), text.size());
    const void* keys[] = {kCTFontAttributeName}; const void* values[] = {font};
    CFDictionaryRef attributes = CFDictionaryCreate(nullptr, keys, values, 1, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CFAttributedStringRef attributed = CFAttributedStringCreate(nullptr, string, attributes);
    CTLineRef line = CTLineCreateWithAttributedString(attributed);
    double including = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
    double width = including - CTLineGetTrailingWhitespaceWidth(line);
    JaliumTextMetrics metrics{};
    Check(jalium_text_format_measure_text(format.get(), Managed(text.c_str()), text.size(), 100000, 1000, &metrics) == JALIUM_OK,
        "Native text measurement failed");
    JaliumTextHitTestResult caret{};
    Check(jalium_text_format_hit_test_text_position(format.get(), Managed(text.c_str()), text.size(), 100000, 1000,
        text.size(), 0, &caret) == JALIUM_OK, "Native end caret failed");
    std::printf("size=%g length=%zu: measured=%.6f inclusive=%.6f reference=%.6f/%.6f caret=%.6f\n",
        size, text.size(), metrics.width, metrics.widthIncludingTrailingWhitespace, width, including, caret.caretX);
    CFRelease(line); CFRelease(attributed); CFRelease(attributes); CFRelease(string); CFRelease(font); CFRelease(name);
    Check(std::abs(metrics.width - width) < .001 && std::abs(metrics.widthIncludingTrailingWhitespace - including) < .001,
        "Text advances were rounded or trailing whitespace was lost");
    Check(std::abs(caret.caretX - including) < .001, "Measured advance and end caret disagree");
    Check(metrics.lineCount == 1, "A single line was counted from rounded height");
    JaliumTextSpan span{format.get(), 0, static_cast<uint32_t>(text.size()), 1, 1, 1, 1};
    for (float constraint : {1.f, 100000.f}) {
        std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)> paragraph(
            jalium_text_paragraph_create_with_wrapping(format.get(), reinterpret_cast<const uint16_t*>(text.data()),
                text.size(), &span, 1, constraint, 1, 0, 0, 1), jalium_text_paragraph_destroy);
        Check(paragraph != nullptr, "Single line paragraph failed");
        JaliumParagraphLineMetrics row{};
        Check(jalium_text_paragraph_get_line(paragraph.get(), 0, &row) == JALIUM_OK, "Paragraph metrics failed");
        JaliumParagraphCaret edge{};
        Check(jalium_text_paragraph_get_caret(paragraph.get(), 0, text.size(), 0, &edge) == JALIUM_OK, "Paragraph caret failed");
        std::printf("  paragraph constraint=%g x=%.6f width=%.6f end=%.6f expected=%.6f\n",
            constraint, row.line.x, row.line.width, edge.x, including);
        Check(std::abs(row.line.width - including) < .001 && std::abs(edge.x - including) < .001,
            "Single line paragraph and drawn text advances disagree");
    }
}
void TestRows(JaliumContext* context)
{
    Format format(jalium_text_format_create(context, Managed(u"Menlo"), 15, 400, 0), jalium_text_format_destroy);
    Check(format != nullptr, "Multiline format failed");
    for (const std::u16string text : {u"ab\ncd", u"ab\r\ncd", u"ab\n", u"\n", u"\r\n", u"ab\n\n"}) {
        JaliumTextMetrics metrics{};
        Check(jalium_text_format_measure_text(format.get(), Managed(text.c_str()), text.size(), 100000, 1000, &metrics) == JALIUM_OK,
            "Multiline measurement failed");
        uint32_t expected = text == u"ab\n\n" ? 3 : 2;
        Check(metrics.lineCount == expected, "Explicit/terminal newline row count was wrong");
        Check(metrics.height > 0 && metrics.height <= metrics.lineHeight * expected + .01f,
            "Row height disagrees with line count");
    }
}
void TestScopedTabCarets(JaliumContext* context)
{
    Format format(jalium_text_format_create(context, Managed(u"Menlo"), 18, 400, 0), jalium_text_format_destroy);
    Check(format != nullptr, "Scoped tab format failed");
    for (const std::u16string text : {u"\u2067אב A\t\u2069", u"\u202eאב A\t\u202c",
        u"\u2067אב A\t\u2069BC", u"\u202eאב A\t\u202cBC"}) {
        const auto tab = static_cast<uint32_t>(text.find(u'\t'));
        for (int direction : {0, 1}) {
            JaliumTextSpan span{format.get(), 0, static_cast<uint32_t>(text.size()), 1, 1, 1, 1};
            std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)> paragraph(
                jalium_text_paragraph_create_with_wrapping(format.get(), reinterpret_cast<const uint16_t*>(text.data()),
                    text.size(), &span, 1, 100000, 1, 0, direction, 1), jalium_text_paragraph_destroy);
            Check(paragraph != nullptr, "Scoped tab paragraph failed");
            CTFontRef font = CTFontCreateWithName(CFSTR("Menlo"), 18, nullptr);
            CFStringRef string = CFStringCreateWithCharacters(nullptr, reinterpret_cast<const UniChar*>(text.data()), text.size());
            CTWritingDirection dir = direction ? kCTWritingDirectionRightToLeft : kCTWritingDirectionLeftToRight;
            CTParagraphStyleSetting setting{kCTParagraphStyleSpecifierBaseWritingDirection, sizeof(dir), &dir};
            CTParagraphStyleRef style = CTParagraphStyleCreate(&setting, 1);
            const void* keys[] = {kCTFontAttributeName, kCTParagraphStyleAttributeName};
            const void* values[] = {font, style};
            CFDictionaryRef attributes = CFDictionaryCreate(nullptr, keys, values, 2, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
            CFAttributedStringRef attributed = CFAttributedStringCreate(nullptr, string, attributes);
            CTLineRef reference = CTLineCreateWithAttributedString(attributed);
            bool found = false;
            CFArrayRef runs = CTLineGetGlyphRuns(reference);
            for (CFIndex runIndex = 0; runIndex < CFArrayGetCount(runs); ++runIndex) {
                auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, runIndex));
                CFIndex count = CTRunGetGlyphCount(run);
                std::vector<CFIndex> indices(count); std::vector<CGPoint> positions(count); std::vector<CGSize> advances(count);
                CTRunGetStringIndices(run, CFRangeMake(0, 0), indices.data());
                CTRunGetPositions(run, CFRangeMake(0, 0), positions.data());
                CTRunGetAdvances(run, CFRangeMake(0, 0), advances.data());
                for (CFIndex glyph = 0; glyph < count; ++glyph) {
                    if (indices[glyph] != tab) continue;
                    found = true;
                    bool rtl = (CTRunGetStatus(run) & kCTRunStatusRightToLeft) != 0;
                    double left = positions[glyph].x, right = left + advances[glyph].width;
                    JaliumParagraphCaret first{}, last{};
                    Check(jalium_text_paragraph_get_caret(paragraph.get(), 0, tab, 0, &first) == JALIUM_OK &&
                        jalium_text_paragraph_get_caret(paragraph.get(), 0, tab + 1, 1, &last) == JALIUM_OK,
                        "Closing bidi control removed a tab source caret");
                    Check(std::abs(first.x - (rtl ? right : left)) < .001 && std::abs(last.x - (rtl ? left : right)) < .001,
                        "Scoped tab carets differ from independent CoreText glyph advances");
                }
            }
            Check(found, "Independent scoped tab reference has no tab glyph");
            CFRelease(reference); CFRelease(attributed); CFRelease(attributes); CFRelease(style); CFRelease(string); CFRelease(font);
        }
    }
}
}
int main()
{
    @autoreleasepool {
        try {
            std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context(
                jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy);
            Check(context != nullptr, "Metal context failed");
            for (float size : {12.f, 13.f, 14.f, 15.f, 18.f, 21.5f})
                for (const std::u16string text : {u"M", u"MMMMMMMMMM", u" ", u"  ", u"a  ", u"中文 🚀 👨‍👩‍👧 é tail", u"AV fi office", u"AV fi office 中文 👩‍💻"})
                    for (auto family : {u"Menlo", u"Helvetica"}) TestLine(context.get(), family, size, text);
            TestRows(context.get());
            TestScopedTabCarets(context.get());
            std::printf("CoreText text metrics: %d assertions passed\n", checks);
            return 0;
        } catch (const std::exception& error) { std::fprintf(stderr, "Text metrics check failed: %s\n", error.what()); return 1; }
    }
}
