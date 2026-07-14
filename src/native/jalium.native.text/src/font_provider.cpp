#include "font_provider.h"
#include "font_face.h"

#include <cstring>
#include <cwchar>
#include <fstream>
#include <iterator>
#include <vector>

namespace jalium {

namespace {

std::unique_ptr<FontFace> LoadFace(const std::string& path, int faceIndex)
{
    std::ifstream file(path, std::ios::binary);
    if (!file) return nullptr;
    std::vector<uint8_t> bytes(
        (std::istreambuf_iterator<char>(file)),
         std::istreambuf_iterator<char>());
    if (bytes.empty()) return nullptr;
    return FontFace::Parse(std::move(bytes), faceIndex);
}

} // namespace

// ============================================================================
// FontProvider base class default implementation
//
// Discovery (FindFont) is platform-specific; face creation is shared: read the
// resolved font file's bytes and hand them to the self-hosted FontFace parser.
// ============================================================================

std::unique_ptr<FontFace> FontProvider::CreateFace(
    const wchar_t* familyName,
    int32_t weight,
    int32_t style)
{
    std::string path;
    int faceIndex = 0;

    if (!FindFont(familyName, weight, style, path, faceIndex))
    {
        // Try the platform default family as a fallback.
        const wchar_t* defaultFamily = GetDefaultFontFamily();
        if (defaultFamily && (!familyName || wcscmp(defaultFamily, familyName) != 0))
        {
            if (!FindFont(defaultFamily, weight, style, path, faceIndex))
                return nullptr;
        }
        else
        {
            return nullptr;
        }
    }

    return LoadFace(path, faceIndex);
}

std::unique_ptr<FontFace> FontProvider::CreateFace(const FontMatch& match)
{
    if (match.path.empty()) return nullptr;
    return LoadFace(match.path, match.faceIndex);
}

std::vector<FontProvider::FontMatch> FontProvider::FindFallbackFonts(
    const std::vector<uint32_t>&,
    const wchar_t*,
    int32_t weight,
    int32_t style)
{
    std::vector<FontMatch> result;
    const wchar_t* family = GetFallbackFontFamily();
    if (!family) return result;
    FontMatch match;
    if (FindFont(family, weight, style, match.path, match.faceIndex))
        result.push_back(std::move(match));
    return result;
}

} // namespace jalium
