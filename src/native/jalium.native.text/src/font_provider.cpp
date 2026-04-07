#include "font_provider.h"

#include <ft2build.h>
#include FT_FREETYPE_H

#include <cstring>
#include <cwchar>

namespace jalium {

// ============================================================================
// FontProvider base class default implementation
// ============================================================================

FT_Face FontProvider::CreateFace(
    FT_Library ftLib,
    const wchar_t* familyName,
    int32_t weight,
    int32_t style)
{
    std::string path;
    int faceIndex = 0;

    if (!FindFont(familyName, weight, style, path, faceIndex))
    {
        // Try default font family as fallback
        const wchar_t* defaultFamily = GetDefaultFontFamily();
        if (defaultFamily && wcscmp(defaultFamily, familyName) != 0)
        {
            if (!FindFont(defaultFamily, weight, style, path, faceIndex))
                return nullptr;
        }
        else
        {
            return nullptr;
        }
    }

    FT_Face face = nullptr;
    FT_Error err = FT_New_Face(ftLib, path.c_str(), faceIndex, &face);
    if (err != 0)
        return nullptr;

    return face;
}

} // namespace jalium
