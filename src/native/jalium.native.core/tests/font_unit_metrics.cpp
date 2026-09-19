#include "jalium_api.h"
#include "jalium_backend.h"
#include "jalium_dwrite_font_units.h"
#include <cmath>
#include <iostream>

extern "C" void jalium_software_init();

class LegacyTextFormat final : public jalium::TextFormat {
public:
    void SetAlignment(int32_t) override {}
    void SetParagraphAlignment(int32_t) override {}
    void SetTrimming(int32_t) override {}
    void SetWordWrapping(int32_t) override {}
    void SetLineSpacing(int32_t, float, float) override {}
    void SetMaxLines(uint32_t) override {}
    JaliumResult MeasureText(const wchar_t*, uint32_t, float, float, JaliumTextMetrics*) override { return JALIUM_ERROR_NOT_SUPPORTED; }
    JaliumResult GetFontMetrics(JaliumTextMetrics*) override { return JALIUM_ERROR_NOT_SUPPORTED; }
    JaliumResult HitTestPoint(const wchar_t*, uint32_t, float, float, float, float, JaliumTextHitTestResult*) override { return JALIUM_ERROR_NOT_SUPPORTED; }
    JaliumResult HitTestTextPosition(const wchar_t*, uint32_t, float, float, uint32_t, int32_t, JaliumTextHitTestResult*) override { return JALIUM_ERROR_NOT_SUPPORTED; }
};

int main()
{
    int failures = 0;
    auto check = [&](bool condition, const char* message) {
        if (!condition) { std::cerr << "FAIL: " << message << '\n'; ++failures; }
    };
    LegacyTextFormat legacy;
    JaliumFontUnitMetrics metrics{};
    check(jalium_text_format_get_font_unit_metrics(reinterpret_cast<JaliumTextFormat*>(&legacy), &metrics) == JALIUM_ERROR_INVALID_ARGUMENT,
        "the C API rejects an undersized output structure");
    metrics.structSize = sizeof(metrics);
    check(jalium_text_format_get_font_unit_metrics(reinterpret_cast<JaliumTextFormat*>(&legacy), &metrics) == JALIUM_ERROR_NOT_SUPPORTED,
        "legacy text formats retain their original vtable and safely report missing metrics");

    jalium_software_init();
    auto* context = jalium_context_create(JALIUM_BACKEND_SOFTWARE);
    check(context != nullptr, "software context is available without a window");
    if (context) {
#ifdef _WIN32
        const auto* family = reinterpret_cast<const wchar_t*>(u"Arial");
#else
        const auto* family = reinterpret_cast<const wchar_t*>(u"DejaVu Sans");
#endif
        for (float size : {20.f, 40.f}) {
            auto* format = jalium_text_format_create(context, family, size, 400, 0);
            check(format != nullptr, "native font format was created");
            if (!format) continue;
            metrics = {}; metrics.structSize = sizeof(metrics);
            check(jalium_text_format_get_font_unit_metrics(format, &metrics) == JALIUM_OK,
                "font-unit metrics cross the native C API");
            check((metrics.available & 16) != 0 && (metrics.available & 4) != 0,
                "font and zero-glyph metrics come from the selected native face");
            check(metrics.xHeight > 0 && metrics.xHeight < size * 2 && metrics.capHeight > 0 && metrics.capHeight < size * 2,
                "font heights are in DIPs, not design units");
            check(metrics.zeroAdvance > 0 && metrics.ideographicAdvance > 0 && metrics.lineHeight >= metrics.ascent,
                "native rulers are finite positive lengths");
            JaliumTextMetrics measured{};
            // The public C ABI always carries UTF-16, including on wchar_t=32 platforms.
            jalium_text_format_measure_text(format, reinterpret_cast<const wchar_t*>(u"000"), 3, 1000, 1000, &measured);
            check(std::abs(measured.widthIncludingTrailingWhitespace - metrics.zeroAdvance * 3) < .05f,
                "the zero ruler agrees with independently laid-out zero glyphs");
            const auto naturalLine = metrics.lineHeight;
            jalium_text_format_set_line_spacing(format, 1, size * 4, size * 2);
            metrics.structSize = sizeof(metrics);
            jalium_text_format_get_font_unit_metrics(format, &metrics);
            check(metrics.lineHeight == naturalLine, "normal line ruler ignores explicit layout line spacing");
            jalium_text_format_destroy(format);
        }
        jalium_context_destroy(context);
    }
#ifdef _WIN32
    Microsoft::WRL::ComPtr<IDWriteFactory> factory;
    DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(factory.GetAddressOf()));
    check(factory != nullptr, "DirectWrite factory is available without a GPU");
    if (factory) {
        Microsoft::WRL::ComPtr<IDWriteTextFormat> format;
        factory->CreateTextFormat(L"Arial", nullptr, DWRITE_FONT_WEIGHT_NORMAL, DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH_NORMAL, 20, L"en-us", &format);
        metrics.structSize = sizeof(metrics);
        check(jalium::font_units::Read(factory.Get(), format.Get(), 20, &metrics) == JALIUM_OK &&
            (metrics.available & 23) == 23, "D3D12/Vulkan DirectWrite rulers contain native x/cap/zero metrics");
        check(metrics.xHeight > 5 && metrics.xHeight < 20 && metrics.capHeight > metrics.xHeight && metrics.capHeight < 20,
            "DirectWrite x/cap metrics use the correct font-to-DIP scale");
    }
#endif
    if (failures) return 1;
    std::cout << "Native font unit metrics passed\n";
    return 0;
}
