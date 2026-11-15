#pragma once

#ifdef _WIN32
#include "jalium_types.h"
#include "jalium_font_features.h"
#include <dwrite.h>
#include <wrl/client.h>

namespace jalium::font_features {

inline JaliumResult Read(IDWriteTextFormat* format, uint32_t tables, const uint32_t* tags,
    uint32_t count, uint8_t* supported)
{
    if (!format) return JALIUM_ERROR_NOT_SUPPORTED;
    using Microsoft::WRL::ComPtr;
    ComPtr<IDWriteFontCollection> collection;
    format->GetFontCollection(&collection);
    if (!collection) return JALIUM_ERROR_NOT_SUPPORTED;
    std::vector<wchar_t> name(format->GetFontFamilyNameLength() + 1);
    if (FAILED(format->GetFontFamilyName(name.data(), static_cast<UINT32>(name.size()))))
        return JALIUM_ERROR_NOT_SUPPORTED;
    UINT32 index = 0; BOOL exists = FALSE;
    if (FAILED(collection->FindFamilyName(name.data(), &index, &exists)) || !exists)
        return JALIUM_ERROR_NOT_SUPPORTED;
    ComPtr<IDWriteFontFamily> family;
    ComPtr<IDWriteFont> font;
    ComPtr<IDWriteFontFace> face;
    if (FAILED(collection->GetFontFamily(index, &family)) || !family ||
        FAILED(family->GetFirstMatchingFont(format->GetFontWeight(), format->GetFontStretch(), format->GetFontStyle(), &font)) || !font ||
        FAILED(font->CreateFontFace(&face)) || !face) return JALIUM_ERROR_NOT_SUPPORTED;
    std::vector<uint32_t> available;
    const UINT32 tableTags[] = {DWRITE_MAKE_OPENTYPE_TAG('G','S','U','B'), DWRITE_MAKE_OPENTYPE_TAG('G','P','O','S')};
    for (unsigned i = 0; i < 2; ++i) {
        if (!(tables & (1u << i))) continue;
        const void* table = nullptr; UINT32 size = 0; void* context = nullptr; exists = FALSE;
        HRESULT result = face->TryGetFontTable(tableTags[i], &table, &size, &context, &exists);
        struct TableUse {
            IDWriteFontFace* face; void* context; bool held;
            ~TableUse() { if (held) face->ReleaseFontTable(context); }
        } use{face.Get(), context, SUCCEEDED(result) && exists != FALSE};
        if (use.held) Collect(static_cast<const uint8_t*>(table), size, available);
    }
    Query(available, tags, count, supported);
    return JALIUM_OK;
}

} // namespace jalium::font_features
#endif
