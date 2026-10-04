#pragma once

#ifdef _WIN32
#include "jalium_font_math.h"

#include <dwrite.h>
#include <wrl/client.h>
#include <vector>

namespace jalium::font_math {

inline JaliumResult Read(IDWriteTextFormat* format, JaliumFontMathConstants* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;
    *result = {sizeof(JaliumFontMathConstants), 0, 0, 0};
    if (!format) return JALIUM_OK;

    using Microsoft::WRL::ComPtr;
    ComPtr<IDWriteFontCollection> collection;
    format->GetFontCollection(&collection);
    if (!collection) return JALIUM_OK;
    std::vector<wchar_t> familyName(format->GetFontFamilyNameLength() + 1);
    if (FAILED(format->GetFontFamilyName(familyName.data(), static_cast<UINT32>(familyName.size()))))
        return JALIUM_OK;
    UINT32 index = 0;
    BOOL exists = FALSE;
    collection->FindFamilyName(familyName.data(), &index, &exists);
    if (!exists) return JALIUM_OK;
    ComPtr<IDWriteFontFamily> family;
    ComPtr<IDWriteFont> font;
    if (FAILED(collection->GetFontFamily(index, &family)) || !family ||
        FAILED(family->GetFirstMatchingFont(format->GetFontWeight(), format->GetFontStretch(),
            format->GetFontStyle(), &font)) || !font)
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    ComPtr<IDWriteFontFace> face;
    if (FAILED(font->CreateFontFace(&face)) || !face) return JALIUM_OK;

    const void* table = nullptr;
    UINT32 length = 0;
    void* tableContext = nullptr;
    exists = FALSE;
    if (SUCCEEDED(face->TryGetFontTable(DWRITE_MAKE_OPENTYPE_TAG('M', 'A', 'T', 'H'),
            &table, &length, &tableContext, &exists)) && exists) {
        font_math::Read(table, length, result);
        face->ReleaseFontTable(tableContext);
    }
    return JALIUM_OK;
}

} // namespace jalium::font_math
#endif
