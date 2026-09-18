#pragma once

#ifdef _WIN32
#include "jalium_types.h"
#include <dwrite.h>
#include <wrl/client.h>
#include <algorithm>
#include <new>
#include <vector>

namespace jalium::font_units {

class GlyphAdvanceRecorder final : public IDWriteTextRenderer {
    LONG references_ = 1;
public:
    float advance = 0;
    bool found = false;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid == __uuidof(IUnknown) || iid == __uuidof(IDWritePixelSnapping) || iid == __uuidof(IDWriteTextRenderer)) {
            *result = static_cast<IDWriteTextRenderer*>(this); AddRef(); return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&references_); }
    ULONG STDMETHODCALLTYPE Release() override {
        const auto count = InterlockedDecrement(&references_);
        if (!count) delete this;
        return count;
    }
    HRESULT STDMETHODCALLTYPE IsPixelSnappingDisabled(void*, BOOL* result) override { *result = TRUE; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetCurrentTransform(void*, DWRITE_MATRIX* result) override { *result = {1,0,0,1,0,0}; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetPixelsPerDip(void*, FLOAT* result) override { *result = 1; return S_OK; }
    HRESULT STDMETHODCALLTYPE DrawGlyphRun(void*, FLOAT, FLOAT, DWRITE_MEASURING_MODE,
        const DWRITE_GLYPH_RUN* run, const DWRITE_GLYPH_RUN_DESCRIPTION*, IUnknown*) override {
        if (run && run->glyphAdvances && run->glyphIndices)
            for (UINT32 i = 0; i < run->glyphCount; ++i)
                if (run->glyphIndices[i]) { advance += run->glyphAdvances[i]; found = true; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE DrawUnderline(void*, FLOAT, FLOAT, const DWRITE_UNDERLINE*, IUnknown*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE DrawStrikethrough(void*, FLOAT, FLOAT, const DWRITE_STRIKETHROUGH*, IUnknown*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE DrawInlineObject(void*, FLOAT, FLOAT, IDWriteInlineObject*, BOOL, BOOL, IUnknown*) override { return S_OK; }
};

inline JaliumResult Read(IDWriteFactory* factory, IDWriteTextFormat* format, float size, JaliumFontUnitMetrics* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;
    *result = {sizeof(JaliumFontUnitMetrics), size * .5f, size, size * .5f, size, size, size * 1.2f, 0};
    if (!factory || !format) return JALIUM_OK;
    using Microsoft::WRL::ComPtr;
    ComPtr<IDWriteFontCollection> collection;
    format->GetFontCollection(&collection);
    if (!collection) return JALIUM_OK;
    std::vector<wchar_t> familyName(format->GetFontFamilyNameLength() + 1);
    if (FAILED(format->GetFontFamilyName(familyName.data(), static_cast<UINT32>(familyName.size())))) return JALIUM_OK;
    UINT32 index = 0; BOOL exists = FALSE;
    collection->FindFamilyName(familyName.data(), &index, &exists);
    if (!exists) return JALIUM_OK;
    ComPtr<IDWriteFontFamily> family;
    ComPtr<IDWriteFont> font;
    if (FAILED(collection->GetFontFamily(index, &family)) || !family ||
        FAILED(family->GetFirstMatchingFont(format->GetFontWeight(), format->GetFontStretch(), format->GetFontStyle(), &font)) || !font)
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    DWRITE_FONT_METRICS metrics{};
    font->GetMetrics(&metrics);
    if (!metrics.designUnitsPerEm) return JALIUM_OK;
    const auto scale = size / metrics.designUnitsPerEm;
    result->ascent = metrics.ascent * scale;
    result->lineHeight = (metrics.ascent + metrics.descent + metrics.lineGap) * scale;
    result->capHeight = result->ascent;
    result->available = 16;
    ComPtr<IDWriteFontFace> face;
    font->CreateFontFace(&face);
    auto height = [&](UINT32 codepoint, UINT16 tableMetric) {
        if (tableMetric) return static_cast<float>(tableMetric);
        if (!face) return 0.f;
        UINT16 glyph = 0; DWRITE_GLYPH_METRICS gm{};
        if (FAILED(face->GetGlyphIndices(&codepoint, 1, &glyph)) || !glyph || FAILED(face->GetDesignGlyphMetrics(&glyph, 1, &gm))) return 0.f;
        const auto top = static_cast<float>(gm.verticalOriginY - gm.topSideBearing);
        const auto bottom = top - static_cast<float>(gm.advanceHeight) + gm.topSideBearing + gm.bottomSideBearing;
        return top + (std::min)(0.f, bottom);
    };
    const auto xHeight = height('x', metrics.xHeight);
    const auto capHeight = height('O', metrics.capHeight);
    if (xHeight > 0) { result->xHeight = xHeight * scale; result->available |= 1; }
    if (capHeight > 0) { result->capHeight = capHeight * scale; result->available |= 2; }
    // A one-character layout selects the same fallback face as actual drawing.
    // It has no adjacent characters to introduce kerning or ligatures.
    auto advance = [&](wchar_t character, float& value, uint32_t flag) {
        ComPtr<IDWriteTextLayout> layout;
        if (FAILED(factory->CreateTextLayout(&character, 1, format, size * 16, size * 16, &layout))) return;
        ComPtr<GlyphAdvanceRecorder> recorder;
        recorder.Attach(new(std::nothrow) GlyphAdvanceRecorder());
        if (recorder && SUCCEEDED(layout->Draw(nullptr, recorder.Get(), 0, 0)) && recorder->found) {
            value = recorder->advance; result->available |= flag;
        }
    };
    advance(L'0', result->zeroAdvance, 4);
    advance(L'\x6c34', result->ideographicAdvance, 8);
    return JALIUM_OK;
}
} // namespace jalium::font_units
#endif
