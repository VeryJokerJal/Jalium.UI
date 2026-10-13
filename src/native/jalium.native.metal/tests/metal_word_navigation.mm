#include "jalium_api.h"
#import <AppKit/AppKit.h>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <vector>
#include <thread>
#include <atomic>

static void TestNoWrapParagraphs(JaliumTextFormat* format)
{
    NSUInteger checks = 0;
    for (NSString* text in @[
        [[@"MMMM " stringByPaddingToLength:15000 withString:@"MMMM " startingAtIndex:0] stringByAppendingString:@"abc אבגדה 123 xyz tail"],
        [[@"שלום " stringByPaddingToLength:15000 withString:@"שלום " startingAtIndex:0] stringByAppendingString:@"abc 👨‍👩‍👧‍👦 e\u0301 tail"]]) {
        std::vector<uint16_t> utf(text.length); [text getCharacters:utf.data() range:NSMakeRange(0, text.length)];
        JaliumTextSpan span{format, 0, static_cast<uint32_t>(utf.size()), 1, 1, 1, 1};
        auto paragraph = jalium_text_paragraph_create_with_wrapping(format, utf.data(), utf.size(), &span, 1, 130, 24, 0, -1, 1);
        if (!paragraph) throw std::runtime_error("no-wrap paragraph creation failed");
        std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)> owned(paragraph, jalium_text_paragraph_destroy);
        JaliumParagraphLineMetrics row{};
        if (jalium_text_paragraph_line_count(paragraph) != 1 || jalium_text_paragraph_get_line(paragraph, 0, &row) != JALIUM_OK ||
            row.line.length != utf.size() || row.line.width < 100000 || row.line.y != 0)
            throw std::runtime_error("long no-wrap paragraph silently wrapped or lost text");
        NSMutableParagraphStyle* style = [NSMutableParagraphStyle new]; style.lineBreakMode = NSLineBreakByClipping;
        NSTextView* view = [[NSTextView alloc] initWithFrame:NSMakeRect(0, 0, 130, 80)];
        view.textContainerInset = NSZeroSize; view.textContainer.lineFragmentPadding = 0;
        view.horizontallyResizable = YES; view.textContainer.widthTracksTextView = NO;
        view.textContainer.size = NSMakeSize(CGFLOAT_MAX, CGFLOAT_MAX);
        [view.textStorage setAttributedString:[[NSAttributedString alloc] initWithString:text
            attributes:@{NSFontAttributeName:[NSFont fontWithName:@"Helvetica" size:20], NSParagraphStyleAttributeName:style}]];
        uint32_t offset = 0;
        for (NSUInteger index : {static_cast<NSUInteger>(0), static_cast<NSUInteger>(6925), static_cast<NSUInteger>(10000),
            static_cast<NSUInteger>(14999), static_cast<NSUInteger>(15000), text.length})
        for (int affinity = 0; affinity < 2; ++affinity) for (int movement = 0; movement < 4; ++movement) {
            [view setSelectedRange:NSMakeRange(index, 0) affinity:static_cast<NSSelectionAffinity>(affinity) stillSelecting:NO];
            bool backward = view.selectionAffinity == NSSelectionAffinityUpstream;
            if (movement == 0) [view moveWordForward:nil]; else if (movement == 1) [view moveWordBackward:nil];
            else if (movement == 2) [view moveWordRight:nil]; else [view moveWordLeft:nil];
            JaliumParagraphCaret result{};
            if (jalium_text_paragraph_navigate_word(&paragraph, &offset, 1, utf.data(), utf.size(), index,
                0, 0, movement, backward, &result) != JALIUM_OK || result.textPosition != view.selectedRange.location ||
                (result.backwardAffinity ? 0 : 1) != view.selectionAffinity)
                throw std::runtime_error("long no-wrap word navigation differs from NSTextView");
            ++checks;
        }
        if (jalium_text_paragraph_create_with_wrapping(format, utf.data(), utf.size(), &span, 1, 130, 24, 0, -1, 3))
            throw std::runtime_error("invalid paragraph wrapping accepted");
    }
    std::printf("CoreText no-wrap: %lu long-line NSTextView reference checks passed\n", checks);
}

void TestWordNavigation()
{
    std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context(
        jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy);
    if (!context) throw std::runtime_error("word navigation Metal context failed");
    auto format = jalium_text_format_create(context.get(), reinterpret_cast<const wchar_t*>(u"Helvetica"), 20, 400, 0);
    auto large = jalium_text_format_create(context.get(), reinterpret_cast<const wchar_t*>(u"Helvetica-Bold"), 40, 400, 0);
    if (!format || !large) throw std::runtime_error("word navigation fonts failed");
    std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)> ownFormat(format, jalium_text_format_destroy);
    std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)> ownLarge(large, jalium_text_format_destroy);
    NSUInteger checks = 0;
    for (NSString* text in @[@"abc אבגדה 123 xyz العربية 中文 words tail", @"שלום עולם abc def ghi שלום עולם abc",
        @"abc lmnop אבג xyz אבגדה word end", @"中文输入 👨‍👩‍👧‍👦 e\u0301cole 日本語の編集 ภาษาไทยยินดีต้อนรับ"])
    for (NSNumber* width in @[@80, @130, @240, @1000000])
    for (NSNumber* direction in @[@(-1), @0, @1]) for (NSNumber* mixed in @[@NO, @YES]) {
        NSMutableParagraphStyle* style = [NSMutableParagraphStyle new];
        style.baseWritingDirection = static_cast<NSWritingDirection>(direction.intValue);
        NSMutableAttributedString* attributed = [[NSMutableAttributedString alloc] initWithString:text
            attributes:@{NSFontAttributeName:[NSFont fontWithName:@"Helvetica" size:20], NSParagraphStyleAttributeName:style}];
        if (mixed.boolValue) [attributed addAttribute:NSFontAttributeName value:[NSFont fontWithName:@"Helvetica-Bold" size:40]
            range:NSMakeRange(2, 12)];
        NSTextView* view = [[NSTextView alloc] initWithFrame:NSMakeRect(0, 0, width.doubleValue, 400)];
        view.textContainerInset = NSZeroSize; view.textContainer.lineFragmentPadding = 0;
        view.textContainer.size = NSMakeSize(width.doubleValue, 1000000);
        [view.textStorage setAttributedString:attributed];
        std::vector<uint16_t> buffer(text.length);
        [text getCharacters:buffer.data() range:NSMakeRange(0, text.length)];
        std::vector<JaliumTextSpan> spans{{format, 0, static_cast<uint32_t>(text.length), 1, 1, 1, 1}};
        if (mixed.boolValue) spans = {{format, 0, 2, 1, 1, 1, 1}, {large, 2, 12, 1, 1, 1, 1},
            {format, 14, static_cast<uint32_t>(text.length - 14), 1, 1, 1, 1}};
        auto paragraph = jalium_text_paragraph_create(format, buffer.data(), buffer.size(), spans.data(), spans.size(),
            width.floatValue, 24, 0, direction.intValue);
        if (!paragraph) throw std::runtime_error("word navigation paragraph failed");
        std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)> owned(paragraph, jalium_text_paragraph_destroy);
        uint32_t offset = 0;
        for (NSUInteger index = 0; index <= text.length; ++index) {
            if (index < text.length && [text rangeOfComposedCharacterSequenceAtIndex:index].location != index) continue;
            for (int affinity = 0; affinity < 2; ++affinity) for (int movement = 0; movement < 4; ++movement) {
                [view setSelectedRange:NSMakeRange(index, 0) affinity:static_cast<NSSelectionAffinity>(affinity) stillSelecting:NO];
                bool initialBackward = view.selectionAffinity == NSSelectionAffinityUpstream;
                switch (movement) {
                    case 0: [view moveWordForward:nil]; break;
                    case 1: [view moveWordBackward:nil]; break;
                    case 2: [view moveWordRight:nil]; break;
                    case 3: [view moveWordLeft:nil]; break;
                }
                JaliumParagraphCaret result{};
                auto status = jalium_text_paragraph_navigate_word(&paragraph, &offset, 1, buffer.data(), buffer.size(),
                    index, 0, 0, movement, initialBackward, &result);
                if (status != JALIUM_OK || result.textPosition != view.selectedRange.location ||
                    (result.backwardAffinity ? 0 : 1) != view.selectionAffinity) {
                    std::fprintf(stderr, "word reference mismatch width=%g direction=%d mixed=%d index=%lu move=%d affinity=%d expected=%lu/%ld got=%u/%d status=%d text=%s\n",
                        width.doubleValue, direction.intValue, mixed.boolValue, index, movement, affinity,
                        view.selectedRange.location, view.selectionAffinity, result.textPosition,
                        result.backwardAffinity ? 0 : 1, status, text.UTF8String);
                    throw std::runtime_error("CoreText word navigation differs from same-font NSTextView");
                }
                ++checks;
                if (movement >= 2) {
                    [view setSelectedRange:NSMakeRange(index, 0) affinity:static_cast<NSSelectionAffinity>(affinity) stillSelecting:NO];
                    initialBackward = view.selectionAffinity == NSSelectionAffinityUpstream;
                    if (movement == 2) [view moveWordRightAndModifySelection:nil];
                    else [view moveWordLeftAndModifySelection:nil];
                    NSRange selected = view.selectedRange;
                    NSUInteger caret = selected.location == index ? NSMaxRange(selected) : selected.location;
                    if (jalium_text_paragraph_navigate_word(&paragraph, &offset, 1, buffer.data(), buffer.size(),
                        index, 0, 0, movement, initialBackward, &result) != JALIUM_OK || result.textPosition != caret) {
                        std::fprintf(stderr, "word extension width=%g direction=%d index=%lu move=%d expected=%lu actual=%u\n",
                            width.doubleValue, direction.intValue, index, movement, caret, result.textPosition);
                        throw std::runtime_error("CoreText word selection extension differs from NSTextView");
                    }
                    ++checks;
                }
            }
        }
        // Both ends of an ordered selection participate in physical movement.
        for (int movement = 2; movement <= 3; ++movement) {
            [view setSelectedRange:NSMakeRange(1, 4)];
            if (movement == 2) [view moveWordRight:nil]; else [view moveWordLeft:nil];
            JaliumParagraphCaret result{};
            if (jalium_text_paragraph_navigate_word(&paragraph, &offset, 1, buffer.data(), buffer.size(),
                5, 1, 4, movement, 0, &result) != JALIUM_OK || result.textPosition != view.selectedRange.location)
                throw std::runtime_error("CoreText word navigation from an ordered selection failed");
            ++checks;
        }
    }
    // A document can contain distinct paragraph directions, empty paragraphs,
    // CRLF gaps and a final paragraph separator.
    NSString* text = @"alpha beta\r\n\r\nשלום עולם\r\n";
    std::vector<uint16_t> buffer(text.length); [text getCharacters:buffer.data() range:NSMakeRange(0, text.length)];
    uint32_t offsets[] = {0, 12, 14}; uint32_t lengths[] = {10, 0, 9};
    JaliumTextParagraph* paragraphs[3]{};
    NSMutableAttributedString* attributed = [[NSMutableAttributedString alloc] initWithString:text
        attributes:@{NSFontAttributeName:[NSFont fontWithName:@"Helvetica" size:20]}];
    for (int i = 0; i < 3; ++i) {
        JaliumTextSpan span{format, 0, lengths[i], 1, 1, 1, 1};
        paragraphs[i] = jalium_text_paragraph_create(format, buffer.data() + offsets[i], lengths[i],
            lengths[i] ? &span : nullptr, lengths[i] ? 1 : 0, 80, 24, 0, i == 2 ? 1 : 0);
        if (!paragraphs[i]) throw std::runtime_error("multi-paragraph word layout failed");
        NSMutableParagraphStyle* style = [NSMutableParagraphStyle new];
        style.baseWritingDirection = i == 2 ? NSWritingDirectionRightToLeft : NSWritingDirectionLeftToRight;
        [attributed addAttribute:NSParagraphStyleAttributeName value:style range:NSMakeRange(offsets[i], lengths[i] + 2)];
    }
    NSTextView* view = [[NSTextView alloc] initWithFrame:NSMakeRect(0, 0, 80, 300)];
    view.textContainerInset = NSZeroSize; view.textContainer.lineFragmentPadding = 0;
    view.textContainer.size = NSMakeSize(80, 1000000); [view.textStorage setAttributedString:attributed];
    for (NSUInteger index = 0; index <= text.length; ++index) for (int movement = 2; movement <= 3; ++movement) {
        [view setSelectedRange:NSMakeRange(index, 0)];
        if (movement == 2) [view moveWordRight:nil]; else [view moveWordLeft:nil];
        JaliumParagraphCaret result{};
        if (jalium_text_paragraph_navigate_word(paragraphs, offsets, 3, buffer.data(), buffer.size(), index,
            0, 0, movement, 0, &result) != JALIUM_OK || result.textPosition != view.selectedRange.location) {
            std::fprintf(stderr, "multi paragraph index=%lu movement=%d expected=%lu actual=%u\n", index, movement,
                view.selectedRange.location, result.textPosition);
            throw std::runtime_error("word navigation across paragraph separators failed");
        }
        ++checks;
    }
    JaliumParagraphCaret result{1, 1, 1};
    std::atomic<int> workerFailures{0};
    auto worker = [&] {
        @autoreleasepool {
            for (int i = 0; i < 200; ++i) {
                JaliumParagraphCaret destination{};
                uint32_t start = i % 2 ? 14 : 0;
                uint32_t expected = i % 2 ? 18 : 5;
                if (jalium_text_paragraph_navigate_word(paragraphs, offsets, 3, buffer.data(), buffer.size(), start,
                    0, 0, 0, 0, &destination) != JALIUM_OK || destination.textPosition != expected) ++workerFailures;
            }
        }
    };
    std::thread one(worker), two(worker); one.join(); two.join();
    if (workerFailures != 0) throw std::runtime_error("word navigation context crossed threads");
    if (jalium_text_paragraph_navigate_word(nullptr, offsets, 3, buffer.data(), buffer.size(), 0,
        0, 0, 2, 0, &result) != JALIUM_ERROR_INVALID_ARGUMENT || result.textPosition != 0)
        throw std::runtime_error("word navigation invalid-argument guard failed");
    buffer[0] = 'z';
    if (jalium_text_paragraph_navigate_word(paragraphs, offsets, 3, buffer.data(), buffer.size(), 0,
        0, 0, 2, 0, &result) != JALIUM_ERROR_INVALID_ARGUMENT)
        throw std::runtime_error("word navigation accepted stale paragraph contents");
    for (auto paragraph : paragraphs) jalium_text_paragraph_destroy(paragraph);
    TestNoWrapParagraphs(format);
    std::printf("CoreText word navigation: %lu NSTextView reference checks plus 400 worker queries passed\n", checks);
}
