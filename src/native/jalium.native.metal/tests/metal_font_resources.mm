#include "jalium_api.h"
#include <cmath>
#include <cstdio>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>
#import <AppKit/AppKit.h>

int TestFontCascades(JaliumContext* context);
int TestFontWidths(JaliumContext* context);
int TestFontMatching(JaliumContext* context);

namespace {
int checks = 0;
int referenceWeight = 0, referenceStyle = 0, referenceSize = 0;
void Check(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
    ++checks;
}
const wchar_t* Managed(const char16_t* text) { return reinterpret_cast<const wchar_t*>(text); }
using Font = std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)>;
using Paragraph = std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)>;
Font CreateFont(JaliumContext* context, const char16_t* family)
{
    Font result(jalium_text_format_create(context, Managed(family), 20, 400, 0), jalium_text_format_destroy);
    Check(result != nullptr, "Font creation failed");
    jalium_text_format_set_word_wrapping(result.get(), 1);
    return result;
}
Font WeightedFont(JaliumContext* context, const char16_t* family, float size, int weight, int style)
{
    Font result(jalium_text_format_create(context, Managed(family), size, weight, style), jalium_text_format_destroy);
    Check(result != nullptr, "Weighted font creation failed");
    jalium_text_format_set_word_wrapping(result.get(), 1);
    return result;
}
JaliumTextMetrics Measure(JaliumTextFormat* font, const std::u16string& text)
{
    JaliumTextMetrics result{};
    Check(jalium_text_format_measure_text(font, Managed(text.c_str()), text.size(), 100000, 1000, &result) == JALIUM_OK,
        "Font measurement failed");
    return result;
}
Paragraph CreateParagraph(JaliumTextFormat* font, const std::u16string& text)
{
    JaliumTextSpan span{font, 0, static_cast<uint32_t>(text.size()), 0, 0, 0, 1};
    Paragraph result(jalium_text_paragraph_create_with_wrapping(font,
        reinterpret_cast<const uint16_t*>(text.c_str()), text.size(), &span, 1, 100000, 28, 0, 0, 1),
        jalium_text_paragraph_destroy);
    Check(result != nullptr, "Private font paragraph creation failed");
    return result;
}
void TestCatalog()
{
    for (auto name : {u"Menlo", u"menlo", u"Menlo-Regular", u"Helvetica", u"Andale Mono",
        u"SF Pro", u"sF pRo", u"system-ui", u"MONOSPACE"})
        Check(jalium_font_family_is_available(Managed(name)) == 1, "Installed family, face or platform alias was rejected");
    for (auto name : {u"", u"__JaliumMissingFont40__", u"__Missing40__, Menlo"})
        Check(jalium_font_family_is_available(Managed(name)) == 0, "Missing family was accepted through substitution");
    Check(jalium_font_family_is_available(nullptr) == 0, "Null family was accepted");
    int count = jalium_font_get_system_family_count();
    Check(count > 20 && count < 100000, "CoreText catalog was empty");
    bool menlo = false, helvetica = false;
    for (int i = 0; i < count; ++i) {
        int required = jalium_font_copy_system_family(i, nullptr, 0);
        Check(required > 1 && required <= 65536, "UTF-8 family length query failed");
        std::vector<char> buffer(required + 2, '!');
        Check(jalium_font_copy_system_family(i, buffer.data(), required) == required && buffer[required - 1] == 0 &&
            buffer[required] == '!' && buffer[required + 1] == '!', "Catalog copied beyond its UTF-8 buffer");
        menlo |= std::strcmp(buffer.data(), "Menlo") == 0;
        helvetica |= std::strcmp(buffer.data(), "Helvetica") == 0;
        char small[] = {'x', '!'};
        Check(jalium_font_copy_system_family(i, small, 1) == 0 && small[0] == 0 && small[1] == '!',
            "Short catalog buffer was not rejected safely");
    }
    Check(menlo && helvetica, "CoreText installed families were missing from catalog");
    Check(jalium_font_copy_system_family(-1, nullptr, 0) == 0 && jalium_font_copy_system_family(count, nullptr, 0) == 0 &&
        jalium_font_copy_system_family(0, nullptr, 1) == 0 && jalium_font_copy_system_family(0, nullptr, -1) == 0,
        "Invalid catalog query was accepted");
    bool worker = false;
    std::thread thread([&] { worker = jalium_font_copy_system_family(0, nullptr, 0) > 1 &&
        jalium_font_get_system_family_count() == count; });
    thread.join(); Check(worker, "Calling-thread catalog snapshot failed");
}
void TestSystemFont(JaliumContext* context)
{
    const std::u16string text = u"MMMM iii1 abc אבג xyz tail";
    NSString* sample = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(text.data()) length:text.size()];
    CTFontRef system = CTFontCreateUIFontForLanguage(kCTFontUIFontSystem, 20, nullptr);
    Check(system != nullptr, "CoreText system font reference was unavailable");
    NSAttributedString* attributed = [[NSAttributedString alloc] initWithString:sample
        attributes:@{NSFontAttributeName:(__bridge id)system}];
    CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)attributed);
    CGFloat width = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
    for (auto name : {u"SF Pro", u".AppleSystemUIFont", u"system-ui", u"sans-serif", u"sF pRo"}) {
        auto font = CreateFont(context, name); auto measured = Measure(font.get(), text);
        Check(std::abs(measured.width - width) < .001 && std::abs(measured.ascent - CTFontGetAscent(system)) < .001,
            "Framework default did not use the CoreText system font");
    }
    CFRelease(line); CFRelease(system);
    auto mono = CreateFont(context, u"Menlo");
    for (auto alias : {u"monospace", u"ui-monospace"}) {
        auto font = CreateFont(context, alias);
        Check(Measure(font.get(), text).width == Measure(mono.get(), text).width, "Monospace generic used a proportional fallback");
    }
}
void TestPrivateFont(JaliumContext* context)
{
    NSFont* reference = [NSFont fontWithName:@"Andale Mono" size:20];
    NSURL* url = CFBridgingRelease(CTFontCopyAttribute((__bridge CTFontRef)reference, kCTFontURLAttribute));
    NSData* bytes = [NSData dataWithContentsOfURL:url];
    Check(bytes.length > 12, "Owned font fixture was unavailable");
    constexpr auto alias = u"JaliumWindowFont40";
    const std::u16string text = u"MMMM iii1 abc אבג xyz tail";
    JaliumFontResource* resource = jalium_font_resource_register(Managed(alias), static_cast<const uint8_t*>(bytes.bytes), bytes.length);
    Check(resource != nullptr && jalium_font_family_is_available(Managed(alias)) == 1, "Private font registration failed");
    Check(jalium_font_resource_register(Managed(alias), static_cast<const uint8_t*>(bytes.bytes), bytes.length) == nullptr,
        "Duplicate live alias replaced its font");
    auto custom = CreateFont(context, alias); auto installed = CreateFont(context, u"Andale Mono");
    Check(Measure(custom.get(), text).width == Measure(installed.get(), text).width, "Private data was ignored during measurement");
    auto paragraph = CreateParagraph(custom.get(), text); auto expected = CreateParagraph(installed.get(), text);
    for (uint32_t i = 0; i <= text.size(); ++i) {
        JaliumParagraphCaret actual{}, wanted{};
        Check(jalium_text_paragraph_get_caret(paragraph.get(), 0, i, 0, &actual) == JALIUM_OK &&
            jalium_text_paragraph_get_caret(expected.get(), 0, i, 0, &wanted) == JALIUM_OK && actual.x == wanted.x,
            "Private paragraph caret used a substituted font");
    }
    jalium_font_resource_release(resource);
    Check(jalium_font_family_is_available(Managed(alias)) == 1, "A live text format did not retain its private resource");
    uint8_t garbage[32]{};
    Check(jalium_font_resource_register(Managed(u"InvalidWindowFont40"), garbage, sizeof garbage) == nullptr,
        "Invalid font bytes registered successfully");
    for (int dpi : {1, 2}) {
        NSView* view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 360, 96)];
        JaliumSurfaceDescriptor surface{}; surface.platform = JALIUM_PLATFORM_MACOS;
        surface.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW; surface.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
        std::unique_ptr<JaliumRenderTarget, decltype(&jalium_render_target_destroy)> target(
            jalium_render_target_create_for_surface(context, &surface, 360 * dpi, 96 * dpi), jalium_render_target_destroy);
        Check(target != nullptr, "Private font Metal surface failed"); jalium_render_target_set_dpi(target.get(), 96 * dpi, 96 * dpi);
        std::unique_ptr<JaliumBrush, decltype(&jalium_brush_destroy)> brush(
            jalium_brush_create_solid(context, 0, 0, 0, 1), jalium_brush_destroy);
        auto capture = [&](JaliumTextFormat* font, JaliumTextParagraph* row) {
            Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK &&
                jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "Private font readback setup failed");
            jalium_render_target_clear(target.get(), 1, 1, 1, 1);
            if (row) Check(jalium_render_target_draw_paragraph_line(target.get(), row, 0, 12, 12, 1) == JALIUM_OK,
                "Private paragraph drawing failed");
            else jalium_draw_text(target.get(), Managed(text.c_str()), text.size(), font, 12, 12, 340, 48, brush.get());
            Check(jalium_render_target_end_draw(target.get()) == JALIUM_OK, "Private font draw failed");
            std::vector<uint8_t> result(360 * dpi * 96 * dpi * 4); int32_t width{}, height{};
            Check(jalium_render_target_fetch_readback(target.get(), result.data(), 360 * dpi * 4, &width, &height) == JALIUM_OK &&
                width == 360 * dpi && height == 96 * dpi, "Private font physical pixel dimensions differ");
            return result;
        };
        auto pixels = capture(custom.get(), nullptr);
        Check(pixels == capture(installed.get(), nullptr), "Private-font glyph pixels differ from the same installed font");
        auto paragraphPixels = capture(nullptr, paragraph.get());
        Check(paragraphPixels == capture(nullptr, expected.get()), "Private-font paragraph pixels differ from the same installed font");
        if (const char* directory = std::getenv("JALIUM_FONT_CAPTURE_DIR")) {
            NSString* folder = [NSString stringWithUTF8String:directory];
            [[NSFileManager defaultManager] createDirectoryAtPath:folder withIntermediateDirectories:YES attributes:nil error:nil];
            for (const auto& pair : {std::make_pair("private-font", &pixels), std::make_pair("private-paragraph", &paragraphPixels)}) {
                NSString* path = [folder stringByAppendingPathComponent:[NSString stringWithFormat:@"%s-%dx.bgra", pair.first, dpi]];
                [[NSData dataWithBytes:pair.second->data() length:pair.second->size()] writeToFile:path atomically:YES];
            }
        }
    }
    custom.reset();
    Check(jalium_font_family_is_available(Managed(alias)) == 0, "Released alias remained in the registry");
    JaliumParagraphCaret caret{};
    Check(jalium_text_paragraph_get_caret(paragraph.get(), 0, text.size(), 0, &caret) == JALIUM_OK && caret.x > 300,
        "Paragraph failed after both private resource and temporary text format were released");
    resource = jalium_font_resource_register(Managed(alias), static_cast<const uint8_t*>(bytes.bytes), bytes.length);
    Check(resource != nullptr, "An expired alias could not be reused"); jalium_font_resource_release(resource);
}

CGFloat AppKitWeight(int weight)
{
    const CGFloat anchors[] = {-1, NSFontWeightUltraLight, NSFontWeightThin, NSFontWeightLight, NSFontWeightRegular,
        NSFontWeightMedium, NSFontWeightSemibold, NSFontWeightBold, NSFontWeightHeavy, NSFontWeightBlack, 1};
    const int index = std::min(weight / 100, 9);
    return anchors[index] + (anchors[index + 1] - anchors[index]) * (weight - index * 100) / 100;
}
void CheckReference(JaliumTextFormat* actual, CTFontRef font, const std::u16string& text)
{
    NSString* sample = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(text.data()) length:text.size()];
    NSAttributedString* reference = [[NSAttributedString alloc] initWithString:sample
        attributes:@{NSFontAttributeName:(__bridge id)font}];
    CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)reference);
    CGFloat width = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
    auto measured = Measure(actual, text);
    if (std::abs(measured.width - width) >= .001 || std::abs(measured.ascent - CTFontGetAscent(font)) >= .001) {
        NSString* name = CFBridgingRelease(CTFontCopyPostScriptName(font));
        std::fprintf(stderr, "Weight reference mismatch: %s weight=%d style=%d size=%d actual=%g/%g reference=%g/%g\n",
            name.UTF8String, referenceWeight, referenceStyle, referenceSize, measured.width, measured.ascent, width, CTFontGetAscent(font));
    }
    Check(std::abs(measured.width - width) < .001 && std::abs(measured.ascent - CTFontGetAscent(font)) < .001,
        "Numeric font weight did not match the AppKit/CoreText reference metrics");
    auto paragraph = CreateParagraph(actual, text); JaliumParagraphCaret caret{};
    Check(jalium_text_paragraph_get_caret(paragraph.get(), 0, text.size(), 0, &caret) == JALIUM_OK &&
        std::abs(caret.x - CTLineGetOffsetForStringIndex(line, text.size(), nullptr)) < .001,
        "Weighted paragraph end caret used a different font");
    CFRelease(line);
}
void TestNumericWeights(JaliumContext* context)
{
    const std::u16string text = u"MMMM iii1 abc אבג 123 xyz tail";
    const int weights[] = {1, 50, 100, 200, 250, 300, 350, 400, 450, 500, 550, 600, 650, 700, 750, 800, 850, 900, 950, 999, 1000};
    for (int size : {12, 20, 32}) for (int weight : weights) for (int style : {0, 1, 2}) {
        referenceWeight = weight; referenceStyle = style; referenceSize = size;
        NSFont* system = [NSFont systemFontOfSize:size weight:AppKitWeight(weight)];
        NSFont* expected = style == 0 ? system : [NSFontManager.sharedFontManager convertFont:system toHaveTrait:NSItalicFontMask];
        for (auto alias : {u"SF Pro", u"system-ui", u".AppleSystemUIFont"}) {
            auto actual = WeightedFont(context, alias, size, weight, style);
            CheckReference(actual.get(), (__bridge CTFontRef)expected, text);
        }
    }
    for (NSString* family : @[@"Helvetica", @"Helvetica Neue", @"Avenir Next", @"Menlo", @"PingFang SC"]) {
        std::u16string name(family.length, 0); [family getCharacters:reinterpret_cast<unichar*>(name.data()) range:NSMakeRange(0, family.length)];
        for (int weight : weights) for (int style : {0, 1, 2}) {
            referenceWeight = weight; referenceStyle = style; referenceSize = 20;
            NSFontDescriptor* descriptor = [NSFontDescriptor fontDescriptorWithFontAttributes:@{
                NSFontFamilyAttribute:family, NSFontTraitsAttribute:@{NSFontWeightTrait:@(AppKitWeight(weight))}}];
            NSFont* reference = [NSFont fontWithDescriptor:descriptor size:20];
            if (style != 0) reference = [NSFontManager.sharedFontManager convertFont:reference toHaveTrait:NSItalicFontMask];
            auto actual = WeightedFont(context, name.c_str(), 20, weight, style);
            CheckReference(actual.get(), (__bridge CTFontRef)reference, text);
        }
    }
    const char16_t* faces[] = {u"AvenirNext-UltraLight",u"AvenirNext-UltraLight",u"AvenirNext-UltraLight",u"AvenirNext-Regular",
        u"AvenirNext-Medium",u"AvenirNext-DemiBold",u"AvenirNext-Bold",u"AvenirNext-Heavy",u"AvenirNext-Heavy"};
    NSData* collection = [NSData dataWithContentsOfFile:@"/System/Library/Fonts/Avenir Next.ttc"];
    const char16_t* collectionAlias = u"WindowWeightCollection41";
    JaliumFontResource* resource = jalium_font_resource_register(Managed(collectionAlias),
        static_cast<const uint8_t*>(collection.bytes), collection.length);
    Check(resource != nullptr, "Private font collection registration failed");
    for (int weight = 100; weight <= 900; weight += 100) for (int style : {0, 1, 2}) {
        referenceWeight = weight; referenceStyle = style; referenceSize = 20;
        NSString* name = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(faces[weight / 100 - 1])
            length:std::char_traits<char16_t>::length(faces[weight / 100 - 1])];
        NSFont* reference = [NSFont fontWithName:name size:20];
        if (style != 0) reference = [NSFontManager.sharedFontManager convertFont:reference toHaveTrait:NSItalicFontMask];
        auto actual = WeightedFont(context, collectionAlias, 20, weight, style);
        CheckReference(actual.get(), (__bridge CTFontRef)reference, text);
    }
    jalium_font_resource_release(resource);
    for (NSString* path : @[@"/System/Library/Fonts/SFNS.ttf", @"/System/Library/Fonts/SFNSItalic.ttf"]) {
        NSData* data = [NSData dataWithContentsOfFile:path]; Check(data.length > 12, "Variable font fixture was unavailable");
        const char16_t* alias = u"WindowWeightVariable41";
        resource = jalium_font_resource_register(Managed(alias), static_cast<const uint8_t*>(data.bytes), data.length);
        Check(resource != nullptr, "Private variable font registration failed");
        CGDataProviderRef provider = CGDataProviderCreateWithCFData((__bridge CFDataRef)data);
        CGFontRef graphics = CGFontCreateWithDataProvider(provider);
        // CGFont's wght coordinate is the OpenType weight in the downloaded
        // bytes, independent of Apple's special system-font mapping.
        for (int weight : weights) {
            referenceWeight = weight; referenceStyle = 0; referenceSize = 20;
            // CGFont uses axis names, not OpenType tags, as dictionary keys.
            CGFontRef varied = CGFontCreateCopyWithVariations(graphics, (__bridge CFDictionaryRef)@{@"Weight":@(weight)});
            CTFontRef reference = CTFontCreateWithGraphicsFont(varied, 20, nullptr, nullptr);
            auto actual = WeightedFont(context, alias, 20, weight, 0); CheckReference(actual.get(), reference, text);
            CFRelease(reference); CGFontRelease(varied);
        }
        CGFontRelease(graphics); CGDataProviderRelease(provider); jalium_font_resource_release(resource);
    }
    std::printf("Numeric font references: 567 system, 315 named, 27 private collection, 42 private variable configurations passed\n");
}
}
int main(int argc, char** argv)
{
    @autoreleasepool {
        [NSApplication.sharedApplication setActivationPolicy:NSApplicationActivationPolicyProhibited];
        try {
            const bool weightsOnly = argc == 2 && std::strcmp(argv[1], "--weights-only") == 0;
            if (!weightsOnly) TestCatalog();
            std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context(
                jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy);
            Check(context != nullptr, "Metal font test context failed");
            if (!weightsOnly) { TestSystemFont(context.get()); TestPrivateFont(context.get()); }
            TestNumericWeights(context.get());
            if (!weightsOnly) checks += TestFontCascades(context.get());
            if (!weightsOnly) checks += TestFontWidths(context.get());
            if (!weightsOnly) checks += TestFontMatching(context.get());
            std::printf("CoreText font catalog, system aliases and private font checks: %d passed; same-font GPU pixels equal at 1x and 2x\n", checks);
            return 0;
        } catch (const std::exception& error) { std::fprintf(stderr, "Font check failed: %s\n", error.what()); return 1; }
    }
}
