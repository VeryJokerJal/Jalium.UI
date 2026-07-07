#include "font_provider.h"
#include "font_face.h"

#include <cstring>
#include <cwchar>
#include <fstream>
#include <iterator>
#include <vector>

namespace jalium {

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

    std::ifstream file(path, std::ios::binary);
    if (!file) return nullptr;
    std::vector<uint8_t> bytes(
        (std::istreambuf_iterator<char>(file)),
         std::istreambuf_iterator<char>());
    if (bytes.empty()) return nullptr;

    return FontFace::Parse(std::move(bytes), faceIndex);
}

} // namespace jalium
