#if defined(__linux__) && !defined(__ANDROID__)

#include "font_provider.h"
#include "jalium_text_api.h"

#include <fontconfig/fontconfig.h>
#include <cstring>
#include <cwchar>
#include <algorithm>
#include <limits>
#include <sstream>
#include <string>
#include <vector>

namespace {

const std::vector<std::string>& GetSystemFontFamilies()
{
    static const std::vector<std::string> families = [] {
        std::vector<std::string> result;
        if (!FcInit()) return result;

        FcConfig* config = FcConfigGetCurrent();
        FcPattern* pattern = FcPatternCreate();
        FcObjectSet* objects = FcObjectSetCreate();
        if (objects && !FcObjectSetAdd(objects, FC_FAMILY))
        {
            FcObjectSetDestroy(objects);
            objects = nullptr;
        }
        if (!config || !pattern || !objects)
        {
            if (objects) FcObjectSetDestroy(objects);
            if (pattern) FcPatternDestroy(pattern);
            return result;
        }

        FcFontSet* fonts = FcFontList(config, pattern, objects);
        if (fonts)
        {
            for (int fontIndex = 0; fontIndex < fonts->nfont; ++fontIndex)
            {
                for (int familyIndex = 0;; ++familyIndex)
                {
                    FcChar8* family = nullptr;
                    if (FcPatternGetString(
                            fonts->fonts[fontIndex], FC_FAMILY, familyIndex, &family) != FcResultMatch ||
                        !family || !*family)
                    {
                        break;
                    }
                    result.emplace_back(reinterpret_cast<const char*>(family));
                }
            }
            FcFontSetDestroy(fonts);
        }
        FcObjectSetDestroy(objects);
        FcPatternDestroy(pattern);

        const auto lessIgnoreCase = [](const std::string& left, const std::string& right) {
            const int comparison = FcStrCmpIgnoreCase(
                reinterpret_cast<const FcChar8*>(left.c_str()),
                reinterpret_cast<const FcChar8*>(right.c_str()));
            return comparison != 0 ? comparison < 0 : left < right;
        };
        const auto equalIgnoreCase = [](const std::string& left, const std::string& right) {
            return FcStrCmpIgnoreCase(
                       reinterpret_cast<const FcChar8*>(left.c_str()),
                       reinterpret_cast<const FcChar8*>(right.c_str())) == 0;
        };
        std::sort(result.begin(), result.end(), lessIgnoreCase);
        result.erase(std::unique(result.begin(), result.end(), equalIgnoreCase), result.end());
        return result;
    }();
    return families;
}

} // namespace

namespace jalium {

namespace {

std::string WideToUtf8(const wchar_t* value)
{
    std::string utf8;
    if (!value) return utf8;
    for (const wchar_t* p = value; *p; ++p)
    {
        const uint32_t cp = static_cast<uint32_t>(*p);
        if (cp < 0x80) utf8.push_back(static_cast<char>(cp));
        else if (cp < 0x800) {
            utf8.push_back(static_cast<char>(0xC0 | (cp >> 6)));
            utf8.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else if (cp < 0x10000) {
            utf8.push_back(static_cast<char>(0xE0 | (cp >> 12)));
            utf8.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            utf8.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else if (cp <= 0x10FFFF) {
            utf8.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            utf8.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            utf8.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            utf8.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
    }
    return utf8;
}

int ToFcWeight(int32_t weight)
{
    if (weight <= 100) return FC_WEIGHT_THIN;
    if (weight <= 200) return FC_WEIGHT_EXTRALIGHT;
    if (weight <= 300) return FC_WEIGHT_LIGHT;
    if (weight <= 400) return FC_WEIGHT_REGULAR;
    if (weight <= 500) return FC_WEIGHT_MEDIUM;
    if (weight <= 600) return FC_WEIGHT_SEMIBOLD;
    if (weight <= 700) return FC_WEIGHT_BOLD;
    if (weight <= 800) return FC_WEIGHT_EXTRABOLD;
    return FC_WEIGHT_BLACK;
}

int ToFcSlant(int32_t style)
{
    return style == 1 ? FC_SLANT_ITALIC : (style == 2 ? FC_SLANT_OBLIQUE : FC_SLANT_ROMAN);
}

} // namespace

FontProviderFontconfig::FontProviderFontconfig()
{
    FcInit();
    fcConfig_ = FcConfigGetCurrent();
}

FontProviderFontconfig::~FontProviderFontconfig()
{
    // FcConfig is shared, don't destroy
    fcConfig_ = nullptr;
}

bool FontProviderFontconfig::FindFont(
    const wchar_t* familyName,
    int32_t weight,
    int32_t style,
    std::string& outPath,
    int& outFaceIndex)
{
    if (!familyName || !fcConfig_)
        return false;

    // Explicit UTF-32 → UTF-8 conversion. wcstombs converts through the
    // process locale: under "C"/POSIX (containers, minimal sessions) it fails
    // on any non-ASCII family name — and its return value was discarded, so
    // fontconfig received uninitialized stack bytes for names like "思源黑体".
    const std::string utf8Name = WideToUtf8(familyName);

    // Create fontconfig pattern
    FcPattern* pattern = FcPatternCreate();
    FcPatternAddString(pattern, FC_FAMILY, reinterpret_cast<const FcChar8*>(utf8Name.c_str()));

    // Map weight: Jalium (100-900) -> Fontconfig (0-210+)
    // Fontconfig FC_WEIGHT values: 0=thin, 40=extralight, 50=light, 80=regular,
    // 100=medium, 180=bold, 200=extrabold, 210=black
    int fcWeight = ToFcWeight(weight);
    FcPatternAddInteger(pattern, FC_WEIGHT, fcWeight);

    // Map style
    int fcSlant = ToFcSlant(style);
    FcPatternAddInteger(pattern, FC_SLANT, fcSlant);

    // Perform substitution and matching
    FcConfigSubstitute(static_cast<FcConfig*>(fcConfig_), pattern, FcMatchPattern);
    FcDefaultSubstitute(pattern);

    FcResult result;
    FcPattern* match = FcFontMatch(static_cast<FcConfig*>(fcConfig_), pattern, &result);

    bool found = false;
    if (match)
    {
        FcChar8* filePath = nullptr;
        if (FcPatternGetString(match, FC_FILE, 0, &filePath) == FcResultMatch && filePath)
        {
            outPath = reinterpret_cast<const char*>(filePath);
            found = true;
        }

        int index = 0;
        FcPatternGetInteger(match, FC_INDEX, 0, &index);
        outFaceIndex = index;

        FcPatternDestroy(match);
    }

    FcPatternDestroy(pattern);
    return found;
}

const wchar_t* FontProviderFontconfig::GetDefaultFontFamily() const
{
    return L"sans-serif";
}

const wchar_t* FontProviderFontconfig::GetFallbackFontFamily() const
{
    // FindFont routes family names through FcFontMatch, so this resolves to
    // whatever CJK Noto (or closest) is installed; if none is present fontconfig
    // returns its default sans, which is no worse than dropping the glyph.
    return L"Noto Sans CJK SC";
}

std::vector<FontProvider::FontMatch> FontProviderFontconfig::FindFallbackFonts(
    const std::vector<uint32_t>& codepoints,
    const wchar_t* preferredFamily,
    int32_t weight,
    int32_t style)
{
    if (!fcConfig_ || codepoints.empty()) return {};

    std::ostringstream keyBuilder;
    keyBuilder << WideToUtf8(preferredFamily) << '|' << weight << '|' << style;
    for (uint32_t cp : codepoints) keyBuilder << '|' << std::hex << cp;
    const std::string key = keyBuilder.str();
    {
        std::lock_guard<std::mutex> lock(fallbackCacheMutex_);
        auto it = fallbackCache_.find(key);
        if (it != fallbackCache_.end()) return it->second;
    }

    FcPattern* pattern = FcPatternCreate();
    if (!pattern) return {};
    const std::string family = WideToUtf8(preferredFamily);
    if (!family.empty())
        FcPatternAddString(pattern, FC_FAMILY, reinterpret_cast<const FcChar8*>(family.c_str()));
    FcPatternAddInteger(pattern, FC_WEIGHT, ToFcWeight(weight));
    FcPatternAddInteger(pattern, FC_SLANT, ToFcSlant(style));

    FcCharSet* charset = FcCharSetCreate();
    if (!charset) { FcPatternDestroy(pattern); return {}; }
    for (uint32_t cp : codepoints)
        if (cp <= 0x10FFFFu) FcCharSetAddChar(charset, static_cast<FcChar32>(cp));
    FcPatternAddCharSet(pattern, FC_CHARSET, charset);
    FcCharSetDestroy(charset); // FcPatternAddCharSet retains its own reference.

    FcConfigSubstitute(static_cast<FcConfig*>(fcConfig_), pattern, FcMatchPattern);
    FcDefaultSubstitute(pattern);
    FcResult fcResult = FcResultNoMatch;
    // trim=false is intentional. trim=true removes fonts after their newly
    // contributed charset is exhausted and can hide a later single face that
    // covers an entire emoji/combining cluster.
    FcFontSet* set = FcFontSort(static_cast<FcConfig*>(fcConfig_), pattern, FcFalse, nullptr, &fcResult);
    std::vector<FontMatch> result;
    if (set)
    {
        result.reserve(std::min(set->nfont, 64));
        for (int i = 0; i < set->nfont && result.size() < 64; ++i)
        {
            FcChar8* file = nullptr;
            if (FcPatternGetString(set->fonts[i], FC_FILE, 0, &file) != FcResultMatch || !file)
                continue;
            FontMatch match;
            match.path = reinterpret_cast<const char*>(file);
            FcPatternGetInteger(set->fonts[i], FC_INDEX, 0, &match.faceIndex);
            FcChar8* matchedFamily = nullptr;
            if (FcPatternGetString(set->fonts[i], FC_FAMILY, 0, &matchedFamily) == FcResultMatch && matchedFamily)
                match.family = reinterpret_cast<const char*>(matchedFamily);
            const bool duplicate = std::any_of(result.begin(), result.end(), [&](const FontMatch& old) {
                return old.path == match.path && old.faceIndex == match.faceIndex;
            });
            if (!duplicate) result.push_back(std::move(match));
        }
        FcFontSetDestroy(set);
    }
    FcPatternDestroy(pattern);

    {
        std::lock_guard<std::mutex> lock(fallbackCacheMutex_);
        fallbackCache_[key] = result;
    }
    return result;
}

} // namespace jalium

extern "C" JALIUM_TEXT_API int32_t jalium_text_get_system_font_family_count(void)
{
    const size_t count = GetSystemFontFamilies().size();
    return count <= static_cast<size_t>(std::numeric_limits<int32_t>::max())
        ? static_cast<int32_t>(count)
        : 0;
}

extern "C" JALIUM_TEXT_API int32_t jalium_text_copy_system_font_family(
    int32_t index,
    char* buffer,
    int32_t buffer_size)
{
    const auto& families = GetSystemFontFamilies();
    if (index < 0 || static_cast<size_t>(index) >= families.size()) return 0;

    const std::string& family = families[static_cast<size_t>(index)];
    if (family.size() >= static_cast<size_t>(std::numeric_limits<int32_t>::max())) return 0;
    const int32_t required = static_cast<int32_t>(family.size() + 1);
    if (!buffer || buffer_size < required) return required;

    std::memcpy(buffer, family.c_str(), static_cast<size_t>(required));
    return required;
}

#endif // __linux__ && !__ANDROID__
