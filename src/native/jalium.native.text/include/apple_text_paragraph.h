#pragma once
#include "jalium_backend.h"
#include "jalium_text_api.h"
#include "apple_font_visibility.h"
#include <vector>

namespace jalium {
class JALIUM_TEXT_API MetalTextParagraph final : public TextParagraph, public TextParagraphNavigationProvider,
    public TextParagraphSourceBoundsProvider, public TextParagraphCapsOperationsProvider, public TextParagraphPositionProvider {
public:
    MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
        const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
        float width, float minLineHeight, int32_t alignment, int32_t direction);
    MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
        const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
        float width, float minLineHeight, int32_t alignment, int32_t direction, int32_t wrapping);
    MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
        const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
        float width, float minLineHeight, int32_t alignment, int32_t direction, int32_t wrapping,
        const std::vector<void*>& visibility, const std::vector<std::vector<FontCascadeEntry>>& canonical = {}, bool contextPaint = false, const JaliumTextMetrics* defaultMetrics = nullptr);
    MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
        const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
        float width, float minLineHeight, int32_t alignment, int32_t direction, int32_t wrapping,
        const std::vector<void*>& visibility, const std::vector<std::vector<FontCascadeEntry>>& canonical,
        bool contextPaint, const JaliumTextMetrics* defaultMetrics, bool verticalForms);
    MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
        const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
        float width, float minLineHeight, int32_t alignment, int32_t direction, int32_t wrapping,
        const std::vector<void*>& visibility, const std::vector<std::vector<FontCascadeEntry>>& canonical,
        bool contextPaint, const JaliumTextMetrics* defaultMetrics, bool verticalForms, bool uprightForms);
    MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
        const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
        float width, float minLineHeight, int32_t alignment, int32_t direction, int32_t wrapping,
        const std::vector<void*>& visibility, const std::vector<std::vector<FontCascadeEntry>>& canonical,
        bool contextPaint, const JaliumTextMetrics* defaultMetrics, bool verticalForms, bool uprightForms,
        const JaliumTextCombineUnit* combined, uint32_t combinedCount);
    ~MetalTextParagraph() override;
    bool IsValid() const;
    bool UsesContextPaint() const;
    uint64_t CacheIdentity() const;
    // Factory-only operation, before the immutable object is exposed or retained.
    bool ApplySpacing(const JaliumTextSpacingUnit* units, uint32_t count);
    bool ApplyAdvances(const JaliumTextAdvanceUnit* units, uint32_t count);
    uint32_t LineCount() const override;
    JaliumResult GetPositionModes(uint8_t* modes, uint32_t length) const override;
    JaliumResult GetCapsOperations(uint8_t* operations, uint32_t length) const override;
    JaliumResult GetLine(uint32_t index, JaliumParagraphLineMetrics* result) const override;
    JaliumResult GetFragments(uint32_t line, JaliumTextFragmentMetrics* results,
        uint32_t capacity, uint32_t* count) const override;
    JaliumResult GetCaret(uint32_t line, uint32_t position, bool backward,
        JaliumParagraphCaret* result) const override;
    JaliumResult HitTest(uint32_t line, float x, JaliumParagraphCaret* result) const override;
    JaliumResult GetSelection(uint32_t line, uint32_t start, uint32_t length,
        JaliumTextRangeMetrics* results, uint32_t capacity, uint32_t* count) const override;
    JaliumResult GetSourceBounds(uint32_t line, uint32_t start, uint32_t length,
        JaliumTextRangeMetrics* result) const override;
    JaliumResult NavigateWord(TextParagraph* const* paragraphs, const uint32_t* offsets,
        uint32_t count, const uint16_t* text, uint32_t length, uint32_t position,
        uint32_t selectionStart, uint32_t selectionLength, int32_t direction,
        bool backwardAffinity, JaliumParagraphCaret* result) const override;
    bool RasterizeLine(uint32_t line, float x, float y, float opacity,
        const float* deviceTransform, float clipWidth, float clipHeight,
        std::vector<uint8_t>& pixels, uint32_t& pixelWidth, uint32_t& pixelHeight,
        float& deviceX, float& deviceY, const uint32_t* ranges = nullptr,
        uint32_t rangeCount = 0, int32_t visibility = 0, const float* paintColor = nullptr,
        uint32_t ownerStart = 0, uint32_t ownerLength = UINT32_MAX) const;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
using AppleTextParagraph = MetalTextParagraph;
}
