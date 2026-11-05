#pragma once
#include "jalium_backend.h"
#include "jalium_text_api.h"
#include <memory>
#include <vector>
namespace jalium {
class JALIUM_TEXT_API MetalTextFormat final : public TextFormat, public FontMathConstantsProvider, public TextRangeMetricsProvider, public TextLineMetricsProvider, public TextParagraphProvider, public TextParagraphWrappingProvider, public TextParagraphVerticalProvider, public TextParagraphVerticalAdvanceProvider, public TextFontFallbackProvider, public TextFontCharacterProvider, public FontUnitMetricsProvider, public TextFontFeatureProvider, public TextFontFeatureQueryProvider, public TextParagraphSpacingProvider, public TextParagraphContextProvider, public TextParagraphAdvanceProvider, public TextFontCapsProvider, public TextFontCapsContextProvider, public TextFontCapsContinuationProvider, public TextFontPositionProvider, public TextFontEmojiProvider, public TextFontVariationProvider, public TextFontOpticalVariationProvider, public TextFontSizeAdjustProvider, public TextFontFaceMetricsProvider, public TextParagraphUprightProvider, public TextParagraphCombineProvider, public TextOrientationProvider {
public:
    JaliumResult SetTextOrientation(int32_t orientation, int32_t baseline) override {
        if (orientation < 0 || orientation > 2 || baseline < 0 || baseline > 1) return JALIUM_ERROR_INVALID_ARGUMENT;
        orientation_ = orientation;
        orientationBaseline_ = baseline;
        return JALIUM_OK;
    }
    int32_t TextOrientation() const { return orientation_; }
    bool CentralOrientationBaseline() const { return orientationBaseline_ == 0; }
    MetalTextFormat(const wchar_t* family, float size, int32_t weight, int32_t style);
    MetalTextFormat(const wchar_t* family, float size, int32_t weight, int32_t style, float widthPercentage);
    ~MetalTextFormat() override;

    JaliumResult SetFontSizeAdjust(uint32_t metric, double aspect, uint32_t opticalMode, const JaliumFontVariation* axes, uint32_t count, float* usedSize) override;
    JaliumResult GetFontInkBounds(const wchar_t* text,uint32_t length,JaliumTextRangeMetrics* bounds) override;
    JaliumResult SetFontFaceMetrics(const JaliumFontFaceMetrics* metrics, float* usedSize) override;
    JaliumResult GetFontAspectMetric(uint32_t metric, double* aspect) override;
    void ApplySizeAdjustAttributes(void* attributed, uint32_t start, uint32_t length) const;
    bool IsValid() const;
    bool IsKerningDisabled() const;
    JaliumResult SetFontOpticalVariations(const JaliumFontVariation* axes, uint32_t count, uint32_t mode) override;
    JaliumResult SetFontVariations(const JaliumFontVariation* axes, uint32_t count) override;
    JaliumResult SetFontFeatures(const JaliumFontFeature* features, uint32_t count) override;
    JaliumResult GetFontFeatureSupport(uint32_t tables, const uint32_t* tags, uint32_t count, uint8_t* supported) const override;
    JaliumResult SetFontCaps(uint32_t variant, uint32_t flags, const char* language, uint32_t languageLength) override;
    JaliumResult SetFontCapsContext(uint32_t boundaries) override;
    JaliumResult SetFontCapsContinuation(uint32_t operation) override;
    JaliumResult SetFontEmoji(uint32_t variant, uint32_t after) override;
    void ApplyEmojiAttributes(void* attributed, uint32_t start, uint32_t length) const;
    void ApplyVerticalFeatureAttributes(void* attributed, uint32_t start, uint32_t length) const;
    JaliumResult SetFontPosition(uint32_t variant, uint32_t flags, uint32_t runMode) override;
    void ApplyPositionAttributes(void* attributed, uint32_t start, uint32_t length) const;
    void ApplyCapsAttributes(void* attributed, uint32_t start, uint32_t length) const;
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
    TextParagraph* CreateSpacedParagraph(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount,
        const JaliumTextSpacingUnit* units, uint32_t unitCount,
        float width, float minLineHeight, int32_t direction) override;
    TextParagraph* CreateContextParagraph(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount,
        const JaliumTextSpacingUnit* units, uint32_t unitCount,
        float width, float minLineHeight, int32_t direction) override;
    TextParagraph* CreateParagraphWithWrapping(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount, float width, float minLineHeight,
        int32_t alignment, int32_t direction, int32_t wrapping) override;
    TextParagraph* CreateVerticalParagraph(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount, float inlineSize, float minColumnWidth,
        int32_t alignment, int32_t direction, int32_t wrapping) override;
    TextParagraph* CreateAdvancedVerticalParagraph(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount,
        const JaliumTextAdvanceUnit* units, uint32_t unitCount,
        float inlineSize, float minColumnWidth, int32_t direction, bool contextPaint) override;
    TextParagraph* CreateAdvancedParagraph(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount,
        const JaliumTextAdvanceUnit* units, uint32_t unitCount,
        float width, float minLineHeight, int32_t direction, bool contextPaint) override;
    TextParagraph* CreateUprightParagraph(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount, float inlineSize, float minColumnWidth,
        int32_t alignment, int32_t direction, int32_t wrapping) override;
    TextParagraph* CreateAdvancedUprightParagraph(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount,
        const JaliumTextAdvanceUnit* units, uint32_t unitCount,
        float inlineSize, float minColumnWidth, int32_t direction, bool contextPaint) override;

    TextParagraph* CreateCombinedParagraph(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount,
        const JaliumTextCombineUnit* combined, uint32_t combinedCount,
        const JaliumTextAdvanceUnit* advances, uint32_t advanceCount,
        float inlineSize, float minColumnWidth, int32_t alignment, int32_t direction,
        int32_t wrapping, int32_t orientation, bool contextPaint) override;

private:
    TextParagraph* CreateParagraphCore(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount, float width, float minLineHeight,
        int32_t alignment, int32_t direction, int32_t wrapping, bool contextPaint, bool verticalForms = false);
    TextParagraph* CreateParagraphCore(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount, float width, float minLineHeight,
        int32_t alignment, int32_t direction, int32_t wrapping, bool contextPaint, bool verticalForms, bool uprightForms);
    TextParagraph* CreateParagraphCore(const uint16_t* text, uint32_t length,
        const JaliumTextSpan* spans, uint32_t spanCount, float width, float minLineHeight,
        int32_t alignment, int32_t direction, int32_t wrapping, bool contextPaint, bool verticalForms, bool uprightForms,
        const JaliumTextCombineUnit* combined, uint32_t combinedCount);
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
    int32_t orientation_ = -1;
    int32_t orientationBaseline_ = 0;
    std::unique_ptr<Impl> impl_;
    friend class MetalRenderTarget;
};

using AppleTextFormat = MetalTextFormat;
}
