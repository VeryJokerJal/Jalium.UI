#pragma once
#include "jalium_backend.h"
#include <vector>

namespace jalium {
struct MetalWordNavigationRow {
    uint32_t start, end;
    float y, height;
    int32_t direction;
    std::vector<JaliumParagraphCaret> carets;
};
JaliumResult NavigateMetalWords(const uint16_t* text, uint32_t length,
    const std::vector<uint64_t>& identities, std::vector<MetalWordNavigationRow> rows,
    uint32_t position, uint32_t start, uint32_t selectionLength,
    int32_t direction, bool backwardAffinity, JaliumParagraphCaret* result);
}
