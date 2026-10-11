#include "jalium_backend.h"
#import <AppKit/AppKit.h>
#include <cmath>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
int assertions = 0;
void Check(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
    ++assertions;
}
const wchar_t* Managed(const char16_t* value) { return reinterpret_cast<const wchar_t*>(value); }
using Font = std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)>;
using Paragraph = std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)>;
Font Format(JaliumContext* context, const char16_t* family, int size = 20, int weight = 400, int style = 0)
{
    Font result(jalium_text_format_create(context, Managed(family), size, weight, style), jalium_text_format_destroy);
    Check(result != nullptr, "Cascade font creation failed");
    jalium_text_format_set_word_wrapping(result.get(), 1);
    return result;
}
Paragraph Layout(JaliumTextFormat* primary, const std::u16string& text, const std::vector<JaliumTextSpan>& spans)
{
    Paragraph result(jalium_text_paragraph_create_with_wrapping(primary, reinterpret_cast<const uint16_t*>(text.data()),
        text.size(), spans.data(), spans.size(), 100000, 48, 0, 0, 1), jalium_text_paragraph_destroy);
    Check(result != nullptr && jalium_text_paragraph_line_count(result.get()) == 1, "Cascade paragraph did not keep one line");
    return result;
}
JaliumTextMetrics Measure(JaliumTextFormat* font, const std::u16string& text)
{
    JaliumTextMetrics result{};
    Check(jalium_text_format_measure_text(font, Managed(text.c_str()), text.size(), 100000, 1000, &result) == JALIUM_OK,
        "Cascade measurement failed");
    return result;
}
NSFont* Reference(NSString* family, int size, int weight, int style)
{
    CGFloat value = weight == 600 ? NSFontWeightSemibold : weight == 900 ? NSFontWeightBlack : NSFontWeightRegular;
    NSFont* font = [NSFont fontWithDescriptor:[NSFontDescriptor fontDescriptorWithFontAttributes:@{
        NSFontFamilyAttribute:family, NSFontTraitsAttribute:@{NSFontWeightTrait:@(value)}}] size:size];
    return style == 0 ? font : [NSFontManager.sharedFontManager convertFont:font toHaveTrait:NSItalicFontMask];
}
CGFloat ReferenceWidth(NSFont* primary, NSFont* hebrew, NSString* text)
{
    NSMutableAttributedString* string = [[NSMutableAttributedString alloc] initWithString:text attributes:@{NSFontAttributeName:primary}];
    [string addAttribute:NSFontAttributeName value:hebrew range:NSMakeRange(4, 3)];
    CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)string);
    CGFloat width = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
    CFRelease(line); return width;
}
void Compare(JaliumTextFormat* format, JaliumTextParagraph* actual, JaliumTextParagraph* reference, const std::u16string& text)
{
    for (uint32_t position = 0; position <= text.size(); ++position) for (int backward : {0, 1}) {
        JaliumParagraphCaret got{}, wanted{};
        Check(jalium_text_paragraph_get_caret(actual, 0, position, backward, &got) == JALIUM_OK &&
            jalium_text_paragraph_get_caret(reference, 0, position, backward, &wanted) == JALIUM_OK &&
            std::abs(got.x - wanted.x) < .001 && got.textPosition == wanted.textPosition &&
            got.backwardAffinity == wanted.backwardAffinity, "Ordered cascade caret differs from explicit script fonts");
    }
    JaliumParagraphCaret wanted{};
    JaliumTextHitTestResult got{};
    Check(jalium_text_paragraph_get_caret(reference, 0, text.size(), 0, &wanted) == JALIUM_OK &&
        jalium_text_format_hit_test_text_position(format, Managed(text.c_str()), text.size(), 100000, 1000,
            text.size(), 0, &got) == JALIUM_OK && std::abs(got.caretX - wanted.x) < .001,
        "Ordered format and paragraph caret use different fonts");
    for (uint32_t start : {0u, 4u, 5u, 7u}) {
        JaliumTextRangeMetrics left[8]{}, right[8]{}; uint32_t leftCount{}, rightCount{};
        Check(jalium_text_paragraph_get_selection(actual, 0, start, text.size() - start, left, 8, &leftCount) == JALIUM_OK &&
            jalium_text_paragraph_get_selection(reference, 0, start, text.size() - start, right, 8, &rightCount) == JALIUM_OK &&
            leftCount == rightCount, "Ordered cascade selection count differs");
        for (uint32_t i = 0; i < leftCount; ++i)
            Check(std::abs(left[i].x - right[i].x) < .001 && std::abs(left[i].width - right[i].width) < .001,
                "Ordered cascade selection edges differ");
    }
}
struct Surface {
    NSView* view;
    int dpi;
    std::unique_ptr<JaliumRenderTarget, decltype(&jalium_render_target_destroy)> target;
    Surface(JaliumContext* context, int scale) : view([[NSView alloc] initWithFrame:NSMakeRect(0, 0, 360, 96)]), dpi(scale),
        target(nullptr, jalium_render_target_destroy)
    {
        JaliumSurfaceDescriptor descriptor{}; descriptor.platform = JALIUM_PLATFORM_MACOS;
        descriptor.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW; descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
        target.reset(jalium_render_target_create_for_surface(context, &descriptor, 360 * dpi, 96 * dpi));
        Check(target != nullptr, "Cascade GPU target unavailable"); jalium_render_target_set_dpi(target.get(), 96 * dpi, 96 * dpi);
    }
    std::vector<uint8_t> Capture(JaliumTextParagraph* paragraph)
    {
        Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK &&
            jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "Cascade GPU setup failed");
        jalium_render_target_clear(target.get(), 1, 1, 1, 1);
        Check(jalium_render_target_draw_paragraph_line(target.get(), paragraph, 0, 12, 12, 1) == JALIUM_OK &&
            jalium_render_target_end_draw(target.get()) == JALIUM_OK, "Cascade GPU drawing failed");
        std::vector<uint8_t> pixels(360 * dpi * 96 * dpi * 4); int width{}, height{};
        Check(jalium_render_target_fetch_readback(target.get(), pixels.data(), 360 * dpi * 4, &width, &height) == JALIUM_OK &&
            width == 360 * dpi && height == 96 * dpi, "Cascade GPU dimensions differ"); return pixels;
    }
};
void Save(const char* name, int dpi, const std::vector<uint8_t>& pixels)
{
    if (const char* directory = std::getenv("JALIUM_FONT_CAPTURE_DIR")) {
        NSString* folder = [NSString stringWithUTF8String:directory];
        [[NSFileManager defaultManager] createDirectoryAtPath:folder withIntermediateDirectories:YES attributes:nil error:nil];
        NSString* path = [folder stringByAppendingPathComponent:[NSString stringWithFormat:@"%s-%dx.bgra", name, dpi]];
        Check([[NSData dataWithBytes:pixels.data() length:pixels.size()] writeToFile:path atomically:YES], "Cascade GPU capture write failed");
    }
}
class Unsupported final : public jalium::TextFormat {
public:
    void SetAlignment(int32_t) override {} void SetParagraphAlignment(int32_t) override {}
    void SetTrimming(int32_t) override {} void SetWordWrapping(int32_t) override {}
    void SetLineSpacing(int32_t, float, float) override {} void SetMaxLines(uint32_t) override {}
    JaliumResult MeasureText(const wchar_t*, uint32_t, float, float, JaliumTextMetrics*) override { return JALIUM_ERROR_NOT_SUPPORTED; }
    JaliumResult GetFontMetrics(JaliumTextMetrics*) override { return JALIUM_ERROR_NOT_SUPPORTED; }
    JaliumResult HitTestPoint(const wchar_t*, uint32_t, float, float, float, float, JaliumTextHitTestResult*) override { return JALIUM_ERROR_NOT_SUPPORTED; }
    JaliumResult HitTestTextPosition(const wchar_t*, uint32_t, float, float, uint32_t, int32_t, JaliumTextHitTestResult*) override { return JALIUM_ERROR_NOT_SUPPORTED; }
};
void Invalid(JaliumContext* context)
{
    auto primary = Format(context, u"Avenir Next"); auto otherSize = Format(context, u"Arial Hebrew", 32);
    JaliumTextFormat* list[] = {primary.get(), nullptr};
    Check(jalium_text_format_set_font_fallbacks(nullptr, nullptr, 0) == JALIUM_ERROR_INVALID_ARGUMENT, "Null cascade target accepted");
    Check(jalium_text_format_set_font_fallbacks(primary.get(), nullptr, 1) == JALIUM_ERROR_INVALID_ARGUMENT, "Null cascade list accepted");
    Check(jalium_text_format_set_font_fallbacks(primary.get(), list, 4097) == JALIUM_ERROR_INVALID_ARGUMENT, "Oversized cascade accepted");
    Check(jalium_text_format_set_font_fallbacks(primary.get(), list, 2) == JALIUM_ERROR_INVALID_ARGUMENT, "Null cascade entry accepted");
    Check(jalium_text_format_set_font_fallbacks(primary.get(), list, 1) == JALIUM_ERROR_INVALID_ARGUMENT, "Self cascade accepted");
    list[0] = otherSize.get();
    Check(jalium_text_format_set_font_fallbacks(primary.get(), list, 1) == JALIUM_ERROR_INVALID_ARGUMENT, "Different font sizes accepted");
    Unsupported unsupported;
    Check(jalium_text_format_set_font_fallbacks(reinterpret_cast<JaliumTextFormat*>(&unsupported), nullptr, 0) ==
        JALIUM_ERROR_NOT_SUPPORTED, "Legacy format capability query changed its vtable");
}
void Installed(JaliumContext* context)
{
    const std::u16string text = u"abc אבג tail";
    NSString* sample = @"abc אבג tail";
    Surface one(context, 1), two(context, 2);
    for (int size : {12, 20, 32}) for (int weight : {400, 600, 900}) for (int style : {0, 1, 2}) for (int order : {0, 1}) {
        auto primary = Format(context, u"Avenir Next", size, weight, style);
        auto plain = Format(context, u"Avenir Next", size, weight, style);
        auto first = Format(context, order ? u"New Peninim MT" : u"Arial Hebrew", size, weight, style);
        auto second = Format(context, order ? u"Arial Hebrew" : u"New Peninim MT", size, weight, style);
        JaliumTextFormat* list[] = {first.get(), second.get()};
        Check(jalium_text_format_set_font_fallbacks(primary.get(), list, 2) == JALIUM_OK, "Ordered fallback capability failed");
        auto actual = Layout(primary.get(), text, {{primary.get(), 0, uint32_t(text.size()), 0, 0, 0, 1}});
        auto reference = Layout(plain.get(), text, {{plain.get(), 0, 4, 0, 0, 0, 1}, {first.get(), 4, 3, 0, 0, 0, 1},
            {plain.get(), 7, uint32_t(text.size() - 7), 0, 0, 0, 1}});
        auto measured = Measure(primary.get(), text);
        CGFloat wanted = ReferenceWidth(Reference(@"Avenir Next", size, weight, style),
            Reference(order ? @"New Peninim MT" : @"Arial Hebrew", size, weight, style), sample);
        Check(std::abs(measured.width - wanted) < .001, "Ordered font metrics differ from explicit AppKit script fonts");
        Compare(primary.get(), actual.get(), reference.get(), text);
        if (size == 20) for (auto surface : {&one, &two}) {
            auto pixels = surface->Capture(actual.get());
            Check(pixels == surface->Capture(reference.get()), "Ordered cascade GPU glyphs differ from explicit script fonts");
            if (weight == 400 && style == 0) Save(order ? "cascade-peninim" : "cascade-arial", surface->dpi, pixels);
        }
        Check(jalium_text_format_set_font_fallbacks(primary.get(), nullptr, 0) == JALIUM_OK &&
            Measure(primary.get(), text).width == Measure(plain.get(), text).width, "Clearing cascade did not restore original font");
    }
    std::printf("Ordered font cascades: 54 size/weight/style/order configurations, caret/selection and 1x/2x GPU comparisons passed\n");
}

void CharacterRangesAndDisplay(JaliumContext* context)
{
    auto primary = Format(context, u"Avenir Next"); auto plain = Format(context, u"Avenir Next");
    auto arial = Format(context, u"Arial Hebrew"); auto peninim = Format(context, u"New Peninim MT");
    JaliumUnicodeRange latin{0, 127}, hebrew{0x590, 0x5ff}, all{0, 0x10ffff}, invalid{3, 2};
    uint32_t characters[] = {65, 0x5d0, 0x10ffff}; uint8_t supported[3]{};
    Check(jalium_text_format_get_character_coverage(arial.get(), characters, 3, supported) == JALIUM_OK && supported[1] && !supported[2],
        "Direct cmap coverage did not exclude a missing scalar");
    Check(jalium_text_format_set_unicode_ranges(arial.get(), &latin, 1, 1) == JALIUM_OK &&
        jalium_text_format_get_character_coverage(arial.get(), characters, 3, supported) == JALIUM_OK && !supported[1],
        "unicode-range did not intersect the direct cmap");
    JaliumTextFormat* fallbacks[] = {arial.get(), peninim.get()};
    Check(jalium_text_format_set_font_fallbacks(primary.get(), fallbacks, 2) == JALIUM_OK &&
        jalium_text_format_get_character_coverage(primary.get(), characters, 3, supported) == JALIUM_OK && !supported[1],
        "Coverage lookup incorrectly counted fallback glyphs");
    const std::u16string text = u"abc אבג tail";
    auto limited = Layout(primary.get(), text, {{primary.get(), 0, uint32_t(text.size()), 0, 0, 0, 1}});
    auto expected = Layout(plain.get(), text, {{plain.get(), 0, 4, 0, 0, 0, 1}, {peninim.get(), 4, 3, 0, 0, 0, 1},
        {plain.get(), 7, uint32_t(text.size()-7), 0, 0, 0, 1}});
    Compare(primary.get(), limited.get(), expected.get(), text);
    JaliumFontDisplayEntry display[] = {{plain.get(), &all, 1, 0}, {nullptr, &hebrew, 1, 3}, {peninim.get(), &all, 1, 0}};
    Check(jalium_text_format_set_font_display(primary.get(), display, 3) == JALIUM_OK, "Partial font-display mask failed");
    auto blocked = Layout(primary.get(), text, {{primary.get(), 0, uint32_t(text.size()), 0, 0, 0, 1}});
    Compare(primary.get(), blocked.get(), expected.get(), text);
    auto ink = Layout(plain.get(), text, {{plain.get(), 0, 4, 0, 0, 0, 1}, {peninim.get(), 4, 3, 0, 0, 0, 0},
        {plain.get(), 7, uint32_t(text.size()-7), 0, 0, 0, 1}});
    for (int dpi : {1, 2}) {
        Surface surface(context, dpi); auto pixels = surface.Capture(blocked.get());
        Check(pixels == surface.Capture(ink.get()) && pixels != surface.Capture(expected.get()), "font-display hid visible Latin or painted blocked Hebrew");
        Save("css-partial-block", dpi, pixels);
        Check(surface.Capture(limited.get()) == surface.Capture(expected.get()), "unicode-range fallback ink differs from explicit fonts");
        Save("css-unicode-range", dpi, surface.Capture(limited.get()));
    }
    Check(jalium_text_format_set_font_display(primary.get(), nullptr, 0) == JALIUM_OK, "font-display reset failed");
    auto restored = Layout(primary.get(), text, {{primary.get(), 0, uint32_t(text.size()), 0, 0, 0, 1}});
    Surface surface(context, 1);
    Check(surface.Capture(restored.get()) == surface.Capture(expected.get()), "Reset font-display did not restore glyph ink");
    display[1].flags = 1;
    JaliumFontDisplayEntry overlap[] = {display[0], display[1], {nullptr, &hebrew, 1, 3}, display[2]};
    Check(jalium_text_format_set_font_display(primary.get(), overlap, 4) == JALIUM_OK, "Overlapping display periods failed");
    auto visible = Layout(primary.get(), text, {{primary.get(), 0, uint32_t(text.size()), 0, 0, 0, 1}});
    Check(surface.Capture(visible.get()) == surface.Capture(expected.get()), "Earlier swap face inherited a later block period");
    Check(jalium_text_format_set_unicode_ranges(arial.get(), nullptr, 0, 0) == JALIUM_OK &&
        jalium_text_format_get_character_coverage(arial.get(), characters, 3, supported) == JALIUM_OK && supported[1], "Range reset did not restore cmap");
    Check(jalium_text_format_set_unicode_ranges(primary.get(), &invalid, 1, 1) == JALIUM_ERROR_INVALID_ARGUMENT &&
        jalium_text_format_set_unicode_ranges(primary.get(), &all, 65537, 1) == JALIUM_ERROR_INVALID_ARGUMENT &&
        jalium_text_format_set_unicode_ranges(primary.get(), nullptr, 1, 1) == JALIUM_ERROR_INVALID_ARGUMENT &&
        jalium_text_format_set_unicode_ranges(primary.get(), nullptr, 0, 2) == JALIUM_ERROR_INVALID_ARGUMENT, "Invalid range arguments accepted");
    uint32_t surrogate = 0xd800;
    Check(jalium_text_format_get_character_coverage(primary.get(), &surrogate, 1, supported) == JALIUM_ERROR_INVALID_ARGUMENT &&
        jalium_text_format_get_character_coverage(primary.get(), characters, 65537, supported) == JALIUM_ERROR_INVALID_ARGUMENT &&
        jalium_text_format_get_character_coverage(primary.get(), nullptr, 1, supported) == JALIUM_ERROR_INVALID_ARGUMENT, "Invalid cmap arguments accepted");
    JaliumFontDisplayEntry bad{nullptr, &all, 1, 2};
    Check(jalium_text_format_set_font_display(primary.get(), &bad, 1) == JALIUM_ERROR_INVALID_ARGUMENT &&
        jalium_text_format_set_font_display(primary.get(), nullptr, 1) == JALIUM_ERROR_INVALID_ARGUMENT &&
        jalium_text_format_set_font_display(primary.get(), &bad, 4097) == JALIUM_ERROR_INVALID_ARGUMENT, "Invalid display arguments accepted");
    Unsupported unsupported; auto old = reinterpret_cast<JaliumTextFormat*>(&unsupported);
    Check(jalium_text_format_set_unicode_ranges(old, nullptr, 0, 0) == JALIUM_ERROR_NOT_SUPPORTED &&
        jalium_text_format_get_character_coverage(old, nullptr, 0, nullptr) == JALIUM_ERROR_NOT_SUPPORTED &&
        jalium_text_format_set_font_display(old, nullptr, 0) == JALIUM_ERROR_NOT_SUPPORTED, "Old TextFormat vtable changed");
    auto canonical = Format(context, u"Avenir Next"); auto mono = Format(context, u"Andale Mono"); auto monoPlain = Format(context, u"Andale Mono");
    JaliumUnicodeRange accent{0xc1, 0xc1}; JaliumTextFormat* canonicalList[] = {mono.get()};
    Check(jalium_text_format_set_unicode_ranges(mono.get(), &accent, 1, 1) == JALIUM_OK &&
        jalium_text_format_set_unicode_ranges(canonical.get(), nullptr, 0, 1) == JALIUM_OK &&
        jalium_text_format_set_font_fallbacks(canonical.get(), canonicalList, 1) == JALIUM_OK, "Canonical range setup failed");
    const std::u16string decomposed = u"A\u0301";
    Check(Measure(canonical.get(), decomposed).width == Measure(monoPlain.get(), decomposed).width, "Canonical glyph selected the wrong format font");
    auto canonicalParagraph = Layout(canonical.get(), decomposed, {{canonical.get(), 0, 2, 0, 0, 0, 1}});
    auto canonicalReference = Layout(monoPlain.get(), decomposed, {{monoPlain.get(), 0, 2, 0, 0, 0, 1}});
    JaliumParagraphCaret actualCaret{}, expectedCaret{};
    Check(jalium_text_paragraph_get_caret(canonicalParagraph.get(), 0, 2, 0, &actualCaret) == JALIUM_OK &&
        jalium_text_paragraph_get_caret(canonicalReference.get(), 0, 2, 0, &expectedCaret) == JALIUM_OK && actualCaret.x == expectedCaret.x,
        "Canonical cluster lost its original UTF-16 endpoint");
    JaliumFontDisplayEntry canonicalDisplay[] = {{nullptr, &accent, 1, 3}, {mono.get(), &accent, 1, 0}};
    Check(jalium_text_format_set_font_display(canonical.get(), canonicalDisplay, 2) == JALIUM_OK, "Canonical display mask failed");
    auto canonicalBlocked = Layout(canonical.get(), decomposed, {{canonical.get(), 0, 2, 0, 0, 0, 1}});
    for (int dpi : {1, 2}) {
        Surface output(context, dpi); auto pixels = output.Capture(canonicalBlocked.get());
        Check(std::all_of(pixels.begin(), pixels.end(), [](uint8_t value) { return value == 255; }), "Canonical block painted decomposed fallback ink");
        Save("css-canonical-block", dpi, pixels);
        auto loadedPixels = output.Capture(canonicalParagraph.get());
        Check(loadedPixels == output.Capture(canonicalReference.get()), "Canonical range glyph ink differs from its original face");
        Save("css-canonical-loaded", dpi, loadedPixels);
    }
    std::printf("CSS character ranges and partial display: cmap intersection, layout retention, overlap, resets, ABI guards and 1x/2x GPU passed\n");
}

uint16_t U16(const uint8_t* p) { return p[0] << 8 | p[1]; }
uint32_t U32(const uint8_t* p) { return uint32_t(p[0]) << 24 | uint32_t(p[1]) << 16 | p[2] << 8 | p[3]; }
uint32_t Table(const uint8_t* p, uint32_t base, const char* name)
{
    uint32_t tag = U32(reinterpret_cast<const uint8_t*>(name));
    for (uint16_t i = 0; i < U16(p + base + 4); ++i) {
        const uint8_t* table = p + base + 12 + i * 16;
        if (U32(table) == tag) return U32(table + 8);
    }
    return 0;
}
void PrivateData(JaliumContext* context)
{
    NSMutableData* data = [[NSData dataWithContentsOfFile:@"/System/Library/Fonts/ArialHB.ttc"] mutableCopy];
    Check(data.length > 12, "Private Hebrew cascade fixture unavailable");
    auto bytes = static_cast<uint8_t*>(data.mutableBytes);
    uint32_t base = U32(bytes + 12), hhea = Table(bytes, base, "hhea"), hmtx = Table(bytes, base, "hmtx");
    Check(hhea && hmtx, "Private cascade fixture has no advances");
    for (uint16_t i = 0; i < U16(bytes + hhea + 34); ++i) {
        uint32_t at = hmtx + i * 4; uint16_t width = U16(bytes + at) + 256;
        bytes[at] = width >> 8; bytes[at + 1] = width & 255;
    }
    constexpr auto alias = u"WindowPrivateCascade42";
    auto resource = jalium_font_resource_register(Managed(alias), bytes, data.length);
    Check(resource != nullptr, "Private cascade registration rejected valid TTC data");
    auto original = Format(context, u"Arial Hebrew"); auto privateFont = Format(context, alias);
    Check(Measure(original.get(), u"אבג").width != Measure(privateFont.get(), u"אבג").width,
        "Private collection substituted its installed PostScript name");
    auto primary = Format(context, u"Avenir Next"), plain = Format(context, u"Avenir Next");
    const std::u16string text = u"abc אבג tail";
    JaliumTextFormat* list[] = {privateFont.get()};
    Check(jalium_text_format_set_font_fallbacks(primary.get(), list, 1) == JALIUM_OK, "Private cascade setup failed");
    auto actual = Layout(primary.get(), text, {{primary.get(), 0, uint32_t(text.size()), 0, 0, 0, 1}});
    auto reference = Layout(plain.get(), text, {{plain.get(), 0, 4, 0, 0, 0, 1}, {privateFont.get(), 4, 3, 0, 0, 0, 1},
        {plain.get(), 7, uint32_t(text.size() - 7), 0, 0, 0, 1}});
    jalium_font_resource_release(resource); privateFont.reset();
    Check(jalium_font_family_is_available(Managed(alias)) == 1, "Fallback format did not retain the private registry lease");
    Compare(primary.get(), actual.get(), reference.get(), text);
    Check(jalium_text_format_set_font_fallbacks(primary.get(), nullptr, 0) == JALIUM_OK,
        "Private cascade could not be cleared");
    Check(jalium_font_family_is_available(Managed(alias)) == 0, "Cleared cascade left a private registry lease behind");
    for (int dpi : {1, 2}) {
        Surface surface(context, dpi); auto pixels = surface.Capture(actual.get());
        Check(pixels == surface.Capture(reference.get()), "Released private fallback bytes changed paragraph glyph pixels");
        Save("cascade-private-bytes", dpi, pixels);
    }
    JaliumParagraphCaret left{}, right{};
    Check(jalium_text_paragraph_get_caret(actual.get(), 0, text.size(), 0, &left) == JALIUM_OK &&
        jalium_text_paragraph_get_caret(reference.get(), 0, text.size(), 0, &right) == JALIUM_OK && left.x == right.x,
        "Private paragraph lost its data after registry release");
    std::printf("Private modified-TTC fallback: registry release, retained paragraph bytes and 1x/2x GPU comparisons passed\n");
}
}

int TestFontCascades(JaliumContext* context)
{
    Invalid(context); Installed(context); PrivateData(context); CharacterRangesAndDisplay(context);
    return assertions;
}
