#include "font_provider.h"
#include "font_face.h"
#include "glyph_atlas.h"
#include "glyph_rasterizer.h"
#include "text_engine.h"
#include "text_layout.h"
#include "jalium_text_api.h"
#include "jalium_text_options.h"

#include <cstdlib>
#include <fstream>
#include <iostream>
#include <iterator>
#include <set>
#include <string>
#include <vector>

namespace {

int failures = 0;

void Check(bool condition, const char* message)
{
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        ++failures;
    }
}

std::unique_ptr<jalium::FontFace> LoadFace(const char* path)
{
    if (!path || !*path) return nullptr;
    std::ifstream input(path, std::ios::binary);
    if (!input) return nullptr;
    std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(input)), {});
    return jalium::FontFace::Parse(std::move(bytes), 0);
}

void TestAntialiasPixels()
{
    jalium::FontProviderFontconfig provider;
    auto face = provider.CreateFace(L"DejaVu Sans", 400, 0);
    Check(face != nullptr, "fontconfig resolves a real outline font");
    if (!face) return;
    const uint16_t glyph = face->GetGlyphIndex(U'A');
    Check(glyph != 0, "test face covers Latin A");

    jalium::GlyphRasterizer rasterizer;
    const auto aliased = rasterizer.Rasterize(face.get(), glyph, 19.0f, 3,
                                               jalium::GlyphAntialiasMode::Aliased);
    const auto grayscale = rasterizer.Rasterize(face.get(), glyph, 19.0f, 3,
                                                 jalium::GlyphAntialiasMode::Grayscale);
    const auto lcd = rasterizer.Rasterize(face.get(), glyph, 19.0f, 3,
                                           jalium::GlyphAntialiasMode::HorizontalLcd);
    Check(!aliased.pixels.empty() && !grayscale.pixels.empty() && !lcd.pixels.empty(),
          "all three AA modes rasterize pixels");
    bool aliasedOnlyBinary = true;
    bool grayscaleHasPartial = false;
    bool lcdHasIndependentChannels = false;
    for (size_t i = 0; i + 3 < aliased.pixels.size(); i += 4) {
        const auto value = aliased.pixels[i + 3];
        aliasedOnlyBinary &= value == 0 || value == 255;
        aliasedOnlyBinary &= aliased.pixels[i] == value && aliased.pixels[i + 1] == value && aliased.pixels[i + 2] == value;
    }
    for (size_t i = 0; i + 3 < grayscale.pixels.size(); i += 4) {
        const auto value = grayscale.pixels[i + 3];
        grayscaleHasPartial |= value > 0 && value < 255;
        Check(grayscale.pixels[i] == value && grayscale.pixels[i + 1] == value && grayscale.pixels[i + 2] == value,
              "grayscale mask channels remain equal");
    }
    for (size_t i = 0; i + 3 < lcd.pixels.size(); i += 4)
        lcdHasIndependentChannels |= lcd.pixels[i] != lcd.pixels[i + 1] || lcd.pixels[i + 1] != lcd.pixels[i + 2];
    Check(aliasedOnlyBinary, "aliased mask is strictly bilevel");
    Check(grayscaleHasPartial, "grayscale mask contains analytic partial coverage");
    Check(lcd.hasSubpixel && lcdHasIndependentChannels, "LCD mask contains independent RGB coverage");

    jalium::GlyphAtlas atlas;
    const auto& a = atlas.GetOrInsert(rasterizer, face.get(), 42, glyph, 19, 3,
                                      jalium::GlyphAntialiasMode::Aliased);
    const auto& g = atlas.GetOrInsert(rasterizer, face.get(), 42, glyph, 19, 3,
                                      jalium::GlyphAntialiasMode::Grayscale);
    const auto& l = atlas.GetOrInsert(rasterizer, face.get(), 42, glyph, 19, 3,
                                      jalium::GlyphAntialiasMode::HorizontalLcd);
    Check(a.valid && g.valid && l.valid, "AA atlas entries are valid");
    Check(atlas.GetCacheEntryCount() == 3, "AA mode participates in the atlas cache key");
    Check((l.flags & jalium::ATLAS_GLYPH_LCD) != 0, "LCD atlas entry is tagged");
}

void TestFontconfigClusterFallback()
{
    jalium::FontProviderFontconfig provider;
    constexpr const wchar_t* primaryFamily = L"DejaVu Sans";
    auto primary = provider.CreateFace(primaryFamily, 400, 0);
    if (!primary) {
        std::cout << "SKIP: DejaVu Sans test face is not installed\n";
        return;
    }
    const std::vector<uint32_t> probes = {
        0x4E2D, 0x65E5, 0xD55C, 0x0E01
    };
    uint32_t selected = 0;
    std::vector<jalium::FontProvider::FontMatch> selectedMatches;
    for (uint32_t cp : probes) {
        if (primary->HasGlyph(cp)) continue;
        auto matches = provider.FindFallbackFonts({cp}, primaryFamily, 400, 0);
        for (const auto& match : matches) {
            auto fallback = provider.CreateFace(match);
            if (fallback && fallback->HasGlyph(cp)) {
                selected = cp;
                selectedMatches = std::move(matches);
                break;
            }
        }
        if (selected) break;
    }
    Check(selected != 0, "FcFontSort finds a real face for a codepoint absent from the primary");
    if (!selected) return;
    const auto cached = provider.FindFallbackFonts({selected}, primaryFamily, 400, 0);
    Check(!cached.empty() && cached.front().path == selectedMatches.front().path,
          "fontconfig fallback query is stable through its cache");

    jalium::TextEngine engine;
    Check(engine.Initialize() == JALIUM_OK, "text engine initializes");
    auto* baseFormat = engine.CreateTextFormat(primaryFamily, 24.0f, 400, 0);
    auto* format = dynamic_cast<jalium::JaliumTextFormat*>(baseFormat);
    Check(format != nullptr, "self-hosted text format created");
    if (format) {
        const wchar_t text[] = {static_cast<wchar_t>(selected), 0};
        std::vector<jalium::TextGlyphQuad> quads;
        format->GenerateGlyphQuads(text, 1, 200, 80, 0, 0, 0, 1, 0, 0, quads);
        Check(!quads.empty(), "layout renders a missing-primary glyph through a fontconfig fallback face");
    }
    delete baseFormat;
}

void TestSystemFontFamilyEnumeration()
{
    const int32_t count = jalium_text_get_system_font_family_count();
    Check(count > 0, "Fontconfig exposes at least one real system font family");
    if (count <= 0) return;

    std::set<std::string> names;
    for (int32_t index = 0; index < count; ++index)
    {
        const int32_t required = jalium_text_copy_system_font_family(index, nullptr, 0);
        Check(required > 1, "font family size query includes UTF-8 text and NUL");
        if (required <= 1) continue;
        std::vector<char> buffer(static_cast<size_t>(required));
        Check(jalium_text_copy_system_font_family(index, buffer.data(), required) == required,
              "font family copy returns the queried size");
        Check(buffer.back() == '\0', "font family copy is NUL terminated");
        names.emplace(buffer.data());
    }
    Check(static_cast<int32_t>(names.size()) == count,
          "Fontconfig system family enumeration is deduplicated");
    Check(jalium_text_copy_system_font_family(-1, nullptr, 0) == 0 &&
          jalium_text_copy_system_font_family(count, nullptr, 0) == 0,
          "font family ABI rejects out-of-range indices");
}

void TestFormatModePropagation()
{
    jalium::TextEngine engine;
    Check(engine.Initialize() == JALIUM_OK, "mode propagation text engine initializes");
    std::unique_ptr<jalium::TextFormat> base(engine.CreateTextFormat(L"DejaVu Sans", 22.0f, 400, 0));
    auto* format = dynamic_cast<jalium::JaliumTextFormat*>(base.get());
    Check(format != nullptr, "mode propagation format created");
    if (!format) return;
    format->SetTextRenderingMode(JALIUM_TEXT_AA_CLEARTYPE);
    std::vector<jalium::TextGlyphQuad> clearType;
    format->GenerateGlyphQuads(L"A", 1, 100, 50, 0, 0, 0, 1, 0, 0, clearType);
    Check(!clearType.empty() && (clearType.front().flags & jalium::ATLAS_GLYPH_LCD) != 0,
          "per-format ClearType reaches the self-hosted rasterizer");

    std::vector<jalium::TextGlyphQuad> degraded;
    format->GenerateGlyphQuads(L"A", 1, 100, 50, 0, 0, 0, 1, 0, 0, degraded,
                               1.0f, JALIUM_TEXT_AA_GRAYSCALE);
    Check(!degraded.empty() && (degraded.front().flags & jalium::ATLAS_GLYPH_LCD) == 0,
          "single-alpha Vulkan staging can explicitly degrade ClearType to grayscale");
}

void TestLongWordWrapMakesForwardProgress()
{
    jalium::TextEngine engine;
    Check(engine.Initialize() == JALIUM_OK, "long-word wrap text engine initializes");
    std::unique_ptr<jalium::TextFormat> format(
        engine.CreateTextFormat(L"DejaVu Sans", 22.0f, 400, 0));
    Check(format != nullptr, "long-word wrap format created");
    if (!format) return;

    const std::wstring text = L"prefix " + std::wstring(80, L'W');
    JaliumTextMetrics metrics{};
    format->SetWordWrapping(JALIUM_WORD_WRAP);
    // Keep a future regression bounded: the old no-progress loop reaches this
    // cap and fails the exact line-count assertion instead of exhausting RAM.
    format->SetMaxLines(4);
    Check(format->MeasureText(
              text.c_str(), static_cast<uint32_t>(text.size()), 80.0f, 1000.0f, &metrics) == JALIUM_OK,
          "word wrapping a long token completes");
    Check(metrics.lineCount == 2,
          "word wrapping emits the prefix and overflowing token exactly once");
    Check(metrics.width > 80.0f,
          "word wrapping preserves overflow semantics for an unbreakable token");

    metrics = {};
    format->SetWordWrapping(JALIUM_WORD_WRAP_EMERGENCY);
    format->SetMaxLines(0);
    Check(format->MeasureText(
              text.c_str(), static_cast<uint32_t>(text.size()), 80.0f, 1000.0f, &metrics) == JALIUM_OK,
          "emergency wrapping a long token completes");
    Check(metrics.lineCount > 2,
          "emergency wrapping breaks an unbreakable token across lines");
}

bool TestColorFont(const char* environmentName, const char* label)
{
    const char* path = std::getenv(environmentName);
    if (!path || !*path) {
        std::cout << "SKIP: " << environmentName << " is not set\n";
        return false;
    }
    auto face = LoadFace(path);
    Check(face != nullptr, label);
    if (!face) return true;
    Check(face->HasColorTables(), "color test font exposes COLR/CPAL or CBDT/CBLC");
    jalium::GlyphRasterizer rasterizer;
    const uint32_t probes[] = {0x1F600, 0x1F642, 0x2764, 0x1F680, 0x1F44D};
    jalium::RasterizedGlyph color;
    for (uint32_t cp : probes) {
        const uint16_t glyph = face->GetGlyphIndex(cp);
        if (!glyph) continue;
        color = rasterizer.Rasterize(face.get(), glyph, 64.0f, 0,
                                     jalium::GlyphAntialiasMode::Grayscale);
        if (color.isColor) break;
    }
    Check(color.isColor && !color.pixels.empty(), "authored color glyph rasterizes to RGBA");
    std::set<uint32_t> colors;
    bool nonGray = false;
    for (size_t i = 0; i + 3 < color.pixels.size(); i += 4) {
        if (color.pixels[i + 3] < 16) continue;
        nonGray |= color.pixels[i] != color.pixels[i + 1] || color.pixels[i + 1] != color.pixels[i + 2];
        colors.insert((static_cast<uint32_t>(color.pixels[i]) << 16) |
                      (static_cast<uint32_t>(color.pixels[i + 1]) << 8) |
                       color.pixels[i + 2]);
    }
    Check(nonGray && colors.size() > 2, "color glyph contains multiple non-gray authored colors");
    return true;
}

} // namespace

int main()
{
    TestSystemFontFamilyEnumeration();
    TestAntialiasPixels();
    TestFontconfigClusterFallback();
    TestFormatModePropagation();
    TestLongWordWrapMakesForwardProgress();
    const bool testedBitmap = TestColorFont("JALIUM_COLOR_EMOJI_FONT", "CBDT/CBLC font loads");
    const bool testedColr = TestColorFont("JALIUM_COLR_FONT", "COLR/CPAL font loads");
    if (failures != 0) return 1;
    if (!testedBitmap && !testedColr)
        std::cout << "PASS: fallback/AA checks completed; set color-font env vars for color pixels\n";
    std::cout << "PASS: Linux text fallback, AA, cache, and color glyph pixels\n";
    return 0;
}
