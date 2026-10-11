#pragma once
#include "jalium_backend.h"
#include "metal_font_visibility.h"
#include <vector>

namespace jalium {
class MetalTextParagraph final : public TextParagraph, public TextParagraphNavigationProvider {
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
        const std::vector<void*>& visibility, const std::vector<std::vector<FontCascadeEntry>>& canonical = {});
    ~MetalTextParagraph() override;
    bool IsValid() const;
    uint64_t CacheIdentity() const;
    uint32_t LineCount() const override;
    JaliumResult GetLine(uint32_t index, JaliumParagraphLineMetrics* result) const override;
    JaliumResult GetFragments(uint32_t line, JaliumTextFragmentMetrics* results,
        uint32_t capacity, uint32_t* count) const override;
    JaliumResult GetCaret(uint32_t line, uint32_t position, bool backward,
        JaliumParagraphCaret* result) const override;
    JaliumResult HitTest(uint32_t line, float x, JaliumParagraphCaret* result) const override;
    JaliumResult GetSelection(uint32_t line, uint32_t start, uint32_t length,
        JaliumTextRangeMetrics* results, uint32_t capacity, uint32_t* count) const override;
    JaliumResult NavigateWord(TextParagraph* const* paragraphs, const uint32_t* offsets,
        uint32_t count, const uint16_t* text, uint32_t length, uint32_t position,
        uint32_t selectionStart, uint32_t selectionLength, int32_t direction,
        bool backwardAffinity, JaliumParagraphCaret* result) const override;
    bool RasterizeLine(uint32_t line, float x, float y, float opacity,
        const float* deviceTransform, float clipWidth, float clipHeight,
        std::vector<uint8_t>& pixels, uint32_t& pixelWidth, uint32_t& pixelHeight,
        float& deviceX, float& deviceY) const;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
