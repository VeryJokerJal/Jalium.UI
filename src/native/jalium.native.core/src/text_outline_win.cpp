#include "jalium_api.h"
#include "jalium_font_resource.h"

#ifdef _WIN32
#include <Windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <iomanip>
#include <locale>
#include <new>
#include <sstream>
#include <string>

namespace {

class OutlineSink final : public ID2D1SimplifiedGeometrySink {
    LONG references_ = 1;
public:
    std::ostringstream path;
    float x = 0, y = 0;

    OutlineSink() { path.imbue(std::locale::classic()); path << std::setprecision(9); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid == __uuidof(IUnknown) || iid == __uuidof(ID2D1SimplifiedGeometrySink)) {
            *result = static_cast<ID2D1SimplifiedGeometrySink*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&references_); }
    ULONG STDMETHODCALLTYPE Release() override {
        const auto count = InterlockedDecrement(&references_);
        if (!count) delete this;
        return count;
    }
    void STDMETHODCALLTYPE SetFillMode(D2D1_FILL_MODE) override {}
    void STDMETHODCALLTYPE SetSegmentFlags(D2D1_PATH_SEGMENT) override {}
    void STDMETHODCALLTYPE BeginFigure(D2D1_POINT_2F start, D2D1_FIGURE_BEGIN) override {
        path << 'M' << start.x + x << ' ' << start.y + y << ' ';
    }
    void STDMETHODCALLTYPE AddLines(const D2D1_POINT_2F* points, UINT32 count) override {
        for (UINT32 i = 0; i < count; ++i)
            path << 'L' << points[i].x + x << ' ' << points[i].y + y << ' ';
    }
    void STDMETHODCALLTYPE AddBeziers(const D2D1_BEZIER_SEGMENT* segments, UINT32 count) override {
        for (UINT32 i = 0; i < count; ++i) {
            const auto& p = segments[i];
            path << 'C' << p.point1.x + x << ' ' << p.point1.y + y << ' '
                 << p.point2.x + x << ' ' << p.point2.y + y << ' '
                 << p.point3.x + x << ' ' << p.point3.y + y << ' ';
        }
    }
    void STDMETHODCALLTYPE EndFigure(D2D1_FIGURE_END) override { path << "Z "; }
    HRESULT STDMETHODCALLTYPE Close() override { return S_OK; }
};

class OutlineRenderer final : public IDWriteTextRenderer {
    LONG references_ = 1;
public:
    Microsoft::WRL::ComPtr<OutlineSink> sink;
    float baseline = 0;
    bool hasBaseline = false;

    OutlineRenderer() { sink.Attach(new(std::nothrow) OutlineSink()); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid == __uuidof(IUnknown) || iid == __uuidof(IDWritePixelSnapping) ||
            iid == __uuidof(IDWriteTextRenderer)) {
            *result = static_cast<IDWriteTextRenderer*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&references_); }
    ULONG STDMETHODCALLTYPE Release() override {
        const auto count = InterlockedDecrement(&references_);
        if (!count) delete this;
        return count;
    }
    HRESULT STDMETHODCALLTYPE IsPixelSnappingDisabled(void*, BOOL* disabled) override {
        if (!disabled) return E_POINTER;
        *disabled = TRUE;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetCurrentTransform(void*, DWRITE_MATRIX* matrix) override {
        if (!matrix) return E_POINTER;
        *matrix = {1, 0, 0, 1, 0, 0};
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetPixelsPerDip(void*, FLOAT* pixels) override {
        if (!pixels) return E_POINTER;
        *pixels = 1;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE DrawGlyphRun(void*, FLOAT originX, FLOAT originY,
        DWRITE_MEASURING_MODE, const DWRITE_GLYPH_RUN* run,
        const DWRITE_GLYPH_RUN_DESCRIPTION*, IUnknown*) override {
        if (!run || !run->fontFace || !sink) return E_INVALIDARG;
        if (!hasBaseline) { baseline = originY; hasBaseline = true; }
        sink->x = originX;
        sink->y = originY;
        return run->fontFace->GetGlyphRunOutline(run->fontEmSize, run->glyphIndices,
            run->glyphAdvances, run->glyphOffsets, run->glyphCount,
            run->isSideways, run->bidiLevel & 1, sink.Get());
    }
    HRESULT STDMETHODCALLTYPE DrawUnderline(void*, FLOAT, FLOAT,
        const DWRITE_UNDERLINE*, IUnknown*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE DrawStrikethrough(void*, FLOAT, FLOAT,
        const DWRITE_STRIKETHROUGH*, IUnknown*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE DrawInlineObject(void*, FLOAT, FLOAT,
        IDWriteInlineObject*, BOOL, BOOL, IUnknown*) override { return S_OK; }
};

} // namespace

extern "C" JALIUM_API int32_t jalium_text_copy_outline_path(
    const uint16_t* text, uint32_t textLength,
    const uint16_t* family, uint32_t familyLength,
    float fontSize, int32_t fontWeight, int32_t fontStyle,
    float* width, float* baseline, char* buffer, int32_t bufferSize)
{
    if (width) *width = 0;
    if (baseline) *baseline = 0;
    if (!text || !family || !width || !baseline || textLength > 4096 ||
        familyLength == 0 || familyLength > 256 ||
        !std::isfinite(fontSize) || fontSize <= 0 || fontSize > 35791 ||
        bufferSize < 0) return 0;
    try {
        const std::wstring familyName(reinterpret_cast<const wchar_t*>(family), familyLength);
        Microsoft::WRL::ComPtr<IDWriteFactory> factory;
        if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
                reinterpret_cast<IUnknown**>(factory.GetAddressOf())))) return 0;
        auto resource = jalium::AcquireFontResource(familyName.c_str());
        auto* collection = static_cast<IDWriteFontCollection*>(
            jalium_font_resource_get_collection(resource.get()));
        Microsoft::WRL::ComPtr<IDWriteTextFormat> format;
        if (FAILED(factory->CreateTextFormat(familyName.c_str(), collection,
                static_cast<DWRITE_FONT_WEIGHT>(std::clamp(fontWeight, 1, 999)),
                fontStyle == 1 ? DWRITE_FONT_STYLE_ITALIC :
                fontStyle == 2 ? DWRITE_FONT_STYLE_OBLIQUE : DWRITE_FONT_STYLE_NORMAL,
                DWRITE_FONT_STRETCH_NORMAL, fontSize, L"", &format))) return 0;
        format->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
        Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
        if (FAILED(factory->CreateTextLayout(reinterpret_cast<const wchar_t*>(text),
                textLength, format.Get(), 10000000.f, 10000000.f, &layout))) return 0;
        Microsoft::WRL::ComPtr<OutlineRenderer> renderer;
        renderer.Attach(new(std::nothrow) OutlineRenderer());
        if (!renderer || !renderer->sink || FAILED(layout->Draw(nullptr, renderer.Get(), 0, 0)))
            return 0;
        DWRITE_TEXT_METRICS metrics{};
        if (FAILED(layout->GetMetrics(&metrics))) return 0;
        const std::string path = renderer->sink->path.str();
        if (path.size() >= 8u * 1024u * 1024u) return 0;
        *width = metrics.widthIncludingTrailingWhitespace;
        *baseline = renderer->hasBaseline ? renderer->baseline : fontSize * .8f;
        const auto required = static_cast<int32_t>(path.size() + 1);
        if (buffer && bufferSize >= required) std::memcpy(buffer, path.c_str(), required);
        return required;
    } catch (...) { return 0; }
}
#endif
