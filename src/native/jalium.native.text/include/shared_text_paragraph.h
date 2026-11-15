#pragma once
#include "text_layout.h"
#include "jalium_text_glyph_ranges.h"

namespace jalium {
// Immutable shaped paragraph. Format/font snapshots outlive all creating
// formats; query indexes never call the shaper or touch the glyph atlas.
class JALIUM_TEXT_API SharedTextParagraph final : public TextParagraph {
public:
    static SharedTextParagraph* Create(const JaliumTextFormat& defaults,
        const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
        float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping);
    ~SharedTextParagraph() override;
    // Construction-only. Preserve shaping and paragraph bidi resolution while
    // translating whole glyph clusters and rebuilding each row's source cells.
    bool ApplyAdvances(const JaliumTextAdvanceUnit* units, uint32_t count);
    uint32_t LineCount() const override;
    JaliumResult GetLine(uint32_t index, JaliumParagraphLineMetrics* result) const override;
    JaliumResult GetFragments(uint32_t line, JaliumTextFragmentMetrics* results, uint32_t capacity, uint32_t* count) const override;
    JaliumResult GetCaret(uint32_t line, uint32_t position, bool backward, JaliumParagraphCaret* result) const override;
    JaliumResult HitTest(uint32_t line, float x, JaliumParagraphCaret* result) const override;
    JaliumResult GetSelection(uint32_t line, uint32_t start, uint32_t length,
        JaliumTextRangeMetrics* results, uint32_t capacity, uint32_t* count) const override;
    struct PaintRun {
        std::vector<uint32_t> ranges;
        float r, g, b, a;
    };
    std::vector<PaintRun> PaintRuns(uint32_t line, uint32_t start, uint32_t length,
        const TextGlyphRanges& filter, const float* overrideRgba = nullptr) const;
    const wchar_t* Text() const;
    uint32_t TextLength() const;
    JaliumTextFormat* DrawingFormat() const;
    float Width() const;
    float Height() const;
private:
    SharedTextParagraph();
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
} // namespace jalium
