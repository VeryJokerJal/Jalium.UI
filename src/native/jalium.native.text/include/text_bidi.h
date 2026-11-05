#pragma once

#include "text_shaper.h"
#include <SheenBidi/SheenBidi.h>
#include <cstdint>
#include <string>
#include <vector>

namespace jalium {

// Paragraph analysis retains logical wchar_t offsets. Only completed lines are
// reordered, after wrapping, so measuring, painting and caret hits share UAX #9.
class TextBidi {
public:
    TextBidi(const wchar_t* text, uint32_t length, int direction = -1);
    ~TextBidi();
    TextBidi(const TextBidi&) = delete;
    TextBidi& operator=(const TextBidi&) = delete;
    const wchar_t* MirroredText() const { return mirrored_.c_str(); }
    uint8_t Level(uint32_t position) const { return position < levels_.size() ? levels_[position] : 0; }
    bool Uniform() const { return uniform_; }
    void Reorder(std::vector<ShapedGlyph>& glyphs, uint32_t start, uint32_t end) const;
    static bool IsFormattingControl(uint32_t codepoint);
    static bool IsLineBreak(uint32_t codepoint);

private:
    std::wstring text_;
    std::wstring mirrored_;
    std::vector<uint8_t> levels_;
    std::vector<SBParagraphRef> paragraphs_;
    SBAlgorithmRef algorithm_ = nullptr;
    bool uniform_ = true;
};

} // namespace jalium
