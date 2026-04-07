#if defined(__ANDROID__)

#include "font_provider.h"

#include <dirent.h>
#include <sys/stat.h>
#include <cstring>
#include <cwchar>
#include <algorithm>
#include <fstream>
#include <sstream>

namespace jalium {

// ============================================================================
// Android font discovery
//
// Android system fonts are located at /system/fonts/.
// The primary configuration file is /system/etc/fonts.xml.
// As a fallback, we enumerate /system/fonts/ and use filename heuristics.
// ============================================================================

static const char* kSystemFontsDir = "/system/fonts/";
static const char* kFontsXmlPath = "/system/etc/fonts.xml";

FontProviderAndroid::FontProviderAndroid()
{
}

FontProviderAndroid::~FontProviderAndroid() = default;

void FontProviderAndroid::ParseFontsXml()
{
    if (parsed_) return;
    parsed_ = true;

    // Simplified fonts.xml parsing — extract family name and file associations.
    // Full XML parsing would use expat/libxml2; this is a lightweight approach
    // for the most common font families.

    // Hardcode the most common Android system fonts as fallback
    auto addFont = [this](const char* name, const char* file, int weight, int style) {
        std::string path = std::string(kSystemFontsDir) + file;
        struct stat st;
        if (stat(path.c_str(), &st) == 0)
        {
            // Find or create family
            FontFamily* family = nullptr;
            for (auto& f : families_)
            {
                if (f.name == name) { family = &f; break; }
            }
            if (!family)
            {
                families_.push_back({name, {}});
                family = &families_.back();
            }
            family->fonts.push_back({path, weight, style, 0});
        }
    };

    // Roboto (default sans-serif)
    addFont("Roboto", "Roboto-Thin.ttf", 100, 0);
    addFont("Roboto", "Roboto-ThinItalic.ttf", 100, 1);
    addFont("Roboto", "Roboto-Light.ttf", 300, 0);
    addFont("Roboto", "Roboto-LightItalic.ttf", 300, 1);
    addFont("Roboto", "Roboto-Regular.ttf", 400, 0);
    addFont("Roboto", "Roboto-Italic.ttf", 400, 1);
    addFont("Roboto", "Roboto-Medium.ttf", 500, 0);
    addFont("Roboto", "Roboto-MediumItalic.ttf", 500, 1);
    addFont("Roboto", "Roboto-Bold.ttf", 700, 0);
    addFont("Roboto", "Roboto-BoldItalic.ttf", 700, 1);
    addFont("Roboto", "Roboto-Black.ttf", 900, 0);
    addFont("Roboto", "Roboto-BlackItalic.ttf", 900, 1);

    // sans-serif alias -> Roboto
    for (auto& f : families_)
    {
        if (f.name == "Roboto")
        {
            families_.push_back({"sans-serif", f.fonts});
            break;
        }
    }

    // Noto Sans CJK (Chinese/Japanese/Korean)
    addFont("Noto Sans CJK", "NotoSansCJK-Regular.ttc", 400, 0);
    addFont("Noto Sans CJK", "NotoSansCJK-Bold.ttc", 700, 0);

    // Noto Serif
    addFont("Noto Serif", "NotoSerif-Regular.ttf", 400, 0);
    addFont("Noto Serif", "NotoSerif-Bold.ttf", 700, 0);
    addFont("Noto Serif", "NotoSerif-Italic.ttf", 400, 1);
    addFont("Noto Serif", "NotoSerif-BoldItalic.ttf", 700, 1);

    // serif alias
    for (auto& f : families_)
    {
        if (f.name == "Noto Serif")
        {
            families_.push_back({"serif", f.fonts});
            break;
        }
    }

    // Droid Sans Mono / monospace
    addFont("Droid Sans Mono", "DroidSansMono.ttf", 400, 0);
    for (auto& f : families_)
    {
        if (f.name == "Droid Sans Mono")
        {
            families_.push_back({"monospace", f.fonts});
            break;
        }
    }
}

bool FontProviderAndroid::FindFont(
    const wchar_t* familyName,
    int32_t weight,
    int32_t style,
    std::string& outPath,
    int& outFaceIndex)
{
    ParseFontsXml();

    if (!familyName) return false;

    // Convert wchar_t to UTF-8 for comparison
    size_t wlen = wcslen(familyName);
    char utf8Name[256];
    wcstombs(utf8Name, familyName, sizeof(utf8Name));
    utf8Name[sizeof(utf8Name) - 1] = '\0';

    // Case-insensitive family search
    std::string searchName(utf8Name);
    std::transform(searchName.begin(), searchName.end(), searchName.begin(), ::tolower);

    // Find family
    const FontFamily* found = nullptr;
    for (const auto& family : families_)
    {
        std::string lowerName = family.name;
        std::transform(lowerName.begin(), lowerName.end(), lowerName.begin(), ::tolower);
        if (lowerName == searchName)
        {
            found = &family;
            break;
        }
    }

    if (!found || found->fonts.empty())
    {
        // Fallback to Roboto
        for (const auto& family : families_)
        {
            if (family.name == "Roboto")
            {
                found = &family;
                break;
            }
        }
    }

    if (!found || found->fonts.empty())
    {
        // Last resort: try /system/fonts/Roboto-Regular.ttf directly
        outPath = "/system/fonts/Roboto-Regular.ttf";
        outFaceIndex = 0;
        struct stat st;
        return stat(outPath.c_str(), &st) == 0;
    }

    // Find best weight/style match
    int jStyle = (style == 1 || style == 2) ? 1 : 0; // Italic or Oblique -> italic

    const FontEntry* bestMatch = nullptr;
    int bestScore = INT32_MAX;

    for (const auto& entry : found->fonts)
    {
        int weightDiff = abs(entry.weight - weight);
        int styleDiff = (entry.style != jStyle) ? 1000 : 0;
        int score = weightDiff + styleDiff;

        if (score < bestScore)
        {
            bestScore = score;
            bestMatch = &entry;
        }
    }

    if (bestMatch)
    {
        outPath = bestMatch->path;
        outFaceIndex = bestMatch->faceIndex;
        return true;
    }

    return false;
}

const wchar_t* FontProviderAndroid::GetDefaultFontFamily() const
{
    return L"Roboto";
}

} // namespace jalium

#endif // __ANDROID__
