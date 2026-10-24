#pragma once
#include "jalium_backend.h"
#include "jalium_text_api.h"
#include <memory>
#include <vector>
namespace jalium {
class JALIUM_TEXT_API MetalTextFormat final : public TextFormat, public FontMathConstantsProvider, public TextRangeMetricsProvider, public TextLineMetricsProvider, public TextParagraphProvider, public TextParagraphWrappingProvider, public TextFontFallbackProvider, public TextFontCharacterProvider, public FontUnitMetricsProvider {
public:
    MetalTextFormat(const wchar_t* family, float size, int32_t weight, int32_t style);
    MetalTextFormat(const wchar_t* family, float size, int32_t weight, int32_t style, float widthPercentage);
    ~MetalTextFormat() override;

    bool IsValid() const;
    JaliumResult SetFontFallbacks(TextFormat* const* formats, uint32_t count) override;
    JaliumResult SetUnicodeRanges(const JaliumUnicodeRange* ranges, uint32_t count, bool restricted) override;
    JaliumResult GetCharacterCoverage(const uint32_t* characters, uint32_t count, uint8_t* supported) const override;
    JaliumResult SetFontDisplay(const JaliumFontDisplayEntry* entries, uint32_t count) override;
    void SetAlignment(int32_t alignment) override;
    void SetParagraphAlignment(int32_t alignment) override;
    void SetTrimming(int32_t trimming) override;
    void SetWordWrapping(int32_t wrapping) override;
    void SetLineSpacing(int32_t method, float spacing, float baseline) override;
    void SetMaxLines(uint32_t maxLines) override;
    JaliumResult MeasureText(const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight, JaliumTextMetrics* metrics) override;
    JaliumResult GetFontMetrics(JaliumTextMetrics* metrics) override;
    JaliumResult GetFontUnitMetrics(JaliumFontUnitMetrics* metrics) override;
    JaliumResult GetFontMathConstants(JaliumFontMathConstants* constants) override;
    JaliumResult HitTestPoint(const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight, float pointX, float pointY,
        JaliumTextHitTestResult* result) override;
    JaliumResult HitTestTextPosition(const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight, uint32_t textPosition,
        int32_t isTrailingHit, JaliumTextHitTestResult* result) override;
    JaliumResult HitTestTextRange(const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight, uint32_t textPosition, uint32_t length,
        JaliumTextRangeMetrics* result) override;
    JaliumResult GetLineMetrics(const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight, uint32_t textPosition, int32_t backwardAffinity,
        JaliumTextLineMetrics* result) override;
    TextParagraph* CreateParagraph(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount, float width, float minLineHeight,
        int32_t alignment, int32_t direction) override;
    TextParagraph* CreateParagraphWithWrapping(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount, float width, float minLineHeight,
        int32_t alignment, int32_t direction, int32_t wrapping) override;

private:
    MetalTextFormat(const wchar_t* family, float size, int32_t weight, int32_t style,
        float widthPercentage, bool matchWidth);
public:
    bool Rasterize(const wchar_t* text, uint32_t textLength,
        float x, float y, float width, float height,
        const float* deviceTransform,
        float clipX, float clipY, float clipWidth, float clipHeight,
        float r, float g, float b, float a,
        std::vector<uint8_t>& pixels, uint32_t& pixelWidth,
        uint32_t& pixelHeight, float& deviceX, float& deviceY,
        const uint32_t* ranges, uint32_t rangeCount, int32_t visibility) const;
    uint64_t CacheIdentity() const;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
    friend class MetalRenderTarget;
};

using AppleTextFormat = MetalTextFormat;
}
