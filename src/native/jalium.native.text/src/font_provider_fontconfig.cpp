#if defined(__linux__) && !defined(__ANDROID__)

#include "font_provider.h"

#include <fontconfig/fontconfig.h>
#include <cstring>
#include <cwchar>

namespace jalium {

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
    char utf8Name[512];
    size_t out = 0;
    for (const wchar_t* p = familyName; *p != L'\0' && out + 4 < sizeof(utf8Name); ++p)
    {
        const uint32_t cp = static_cast<uint32_t>(*p);
        if (cp < 0x80)
        {
            utf8Name[out++] = static_cast<char>(cp);
        }
        else if (cp < 0x800)
        {
            utf8Name[out++] = static_cast<char>(0xC0 | (cp >> 6));
            utf8Name[out++] = static_cast<char>(0x80 | (cp & 0x3F));
        }
        else if (cp < 0x10000)
        {
            utf8Name[out++] = static_cast<char>(0xE0 | (cp >> 12));
            utf8Name[out++] = static_cast<char>(0x80 | ((cp >> 6) & 0x3F));
            utf8Name[out++] = static_cast<char>(0x80 | (cp & 0x3F));
        }
        else if (cp <= 0x10FFFF)
        {
            utf8Name[out++] = static_cast<char>(0xF0 | (cp >> 18));
            utf8Name[out++] = static_cast<char>(0x80 | ((cp >> 12) & 0x3F));
            utf8Name[out++] = static_cast<char>(0x80 | ((cp >> 6) & 0x3F));
            utf8Name[out++] = static_cast<char>(0x80 | (cp & 0x3F));
        }
    }
    utf8Name[out] = '\0';

    // Create fontconfig pattern
    FcPattern* pattern = FcPatternCreate();
    FcPatternAddString(pattern, FC_FAMILY, reinterpret_cast<const FcChar8*>(utf8Name));

    // Map weight: Jalium (100-900) -> Fontconfig (0-210+)
    // Fontconfig FC_WEIGHT values: 0=thin, 40=extralight, 50=light, 80=regular,
    // 100=medium, 180=bold, 200=extrabold, 210=black
    int fcWeight = FC_WEIGHT_REGULAR;
    if (weight <= 100)      fcWeight = FC_WEIGHT_THIN;
    else if (weight <= 200) fcWeight = FC_WEIGHT_EXTRALIGHT;
    else if (weight <= 300) fcWeight = FC_WEIGHT_LIGHT;
    else if (weight <= 400) fcWeight = FC_WEIGHT_REGULAR;
    else if (weight <= 500) fcWeight = FC_WEIGHT_MEDIUM;
    else if (weight <= 600) fcWeight = FC_WEIGHT_SEMIBOLD;
    else if (weight <= 700) fcWeight = FC_WEIGHT_BOLD;
    else if (weight <= 800) fcWeight = FC_WEIGHT_EXTRABOLD;
    else                    fcWeight = FC_WEIGHT_BLACK;
    FcPatternAddInteger(pattern, FC_WEIGHT, fcWeight);

    // Map style
    int fcSlant = FC_SLANT_ROMAN;
    if (style == 1) fcSlant = FC_SLANT_ITALIC;
    else if (style == 2) fcSlant = FC_SLANT_OBLIQUE;
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

} // namespace jalium

#endif // __linux__ && !__ANDROID__
