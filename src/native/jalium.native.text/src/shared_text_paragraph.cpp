#include "shared_text_paragraph.h"
#include "text_bidi.h"
#include "text_grapheme.h"
#include <algorithm>
#include <cmath>
#include <limits>
#include <unordered_map>

namespace jalium {
namespace {
template<class T> JaliumResult Copy(const std::vector<T>& source, T* output, uint32_t capacity, uint32_t* count)
{
    if (!count || (capacity && !output)) return JALIUM_ERROR_INVALID_ARGUMENT;
    *count = static_cast<uint32_t>(source.size());
    if (!output && !capacity) return JALIUM_OK;
    if (capacity < source.size()) return JALIUM_ERROR_INVALID_ARGUMENT;
    std::copy(source.begin(), source.end(), output); return JALIUM_OK;
}
struct Cell {
    uint32_t first, end;
    float leading = 0, trailing = 0;
    bool assigned = false, visible = true;
};
struct Row {
    JaliumParagraphLineMetrics metrics{};
    std::vector<Cell> cells;
    std::vector<uint32_t> visualCells;
    std::vector<JaliumParagraphCaret> visualEdges;
    std::vector<JaliumTextFragmentMetrics> fragments;
};
}
struct SharedTextParagraph::Impl {
    std::wstring text;
    std::vector<uint32_t> sourceOffsets;
    uint32_t sourceLength = 0;
    float width = 0, height = 0, drawingWidth = 0, drawingHeight = 0;
    std::vector<std::unique_ptr<JaliumTextFormat>> formats;
    struct Style { uint32_t first, end, scalarFirst, scalarEnd, format; float r, g, b, a; };
    std::vector<Style> styles;
    std::vector<Row> rows;
    uint32_t Scalar(uint32_t source) const {
        auto it = std::lower_bound(sourceOffsets.begin(), sourceOffsets.end(), source);
        return it != sourceOffsets.end() && *it == source ? static_cast<uint32_t>(it - sourceOffsets.begin()) : UINT32_MAX;
    }
};
SharedTextParagraph::SharedTextParagraph() : impl_(std::make_unique<Impl>()) {}
SharedTextParagraph::~SharedTextParagraph() = default;

SharedTextParagraph* SharedTextParagraph::Create(const JaliumTextFormat& defaults,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping)
{
    if (!text || !defaults.face_ || !std::isfinite(width) || width <= 0 ||
        !std::isfinite(minHeight) || minHeight < 0 || direction < -1 || direction > 1 ||
        alignment < 0 || alignment > 3 || wrapping < 0 || wrapping > 2 || (count && !spans)) return nullptr;
    auto owner = std::unique_ptr<SharedTextParagraph>(new SharedTextParagraph());
    auto& data = *owner->impl_; data.sourceLength = length; data.width = width;
    for (uint32_t source = 0; source < length;) {
        uint32_t first = source, cp = text[source++];
        if (cp >= 0xD800 && cp <= 0xDBFF && source < length && text[source] >= 0xDC00 && text[source] <= 0xDFFF)
            cp = 0x10000 + ((cp - 0xD800) << 10) + text[source++] - 0xDC00;
        else if (cp >= 0xD800 && cp <= 0xDFFF) cp = 0xFFFD;
        data.sourceOffsets.push_back(first);
        if constexpr (sizeof(wchar_t) == 2) {
            if (cp > 0xFFFF) {
                data.text.push_back(wchar_t(0xD800 + ((cp - 0x10000) >> 10)));
                data.sourceOffsets.push_back(first + 1);
                data.text.push_back(wchar_t(0xDC00 + ((cp - 0x10000) & 1023))); continue;
            }
        }
        data.text.push_back(wchar_t(cp));
    }
    data.sourceOffsets.push_back(length);
    data.formats.emplace_back(new JaliumTextFormat(defaults, JaliumTextFormat::SnapshotTag{}));
    std::unordered_map<const JaliumTextFormat*, uint32_t> captured{{&defaults, 0}};
    uint32_t end = 0;
    for (uint32_t i = 0; i < count; ++i) {
        const auto& input = spans[i];
        auto* format = dynamic_cast<const JaliumTextFormat*>(reinterpret_cast<TextFormat*>(input.format));
        if (!format || !format->face_ || input.textPosition != end || !input.length || input.length > length - end ||
            !std::isfinite(input.r) || !std::isfinite(input.g) || !std::isfinite(input.b) || !std::isfinite(input.a)) return nullptr;
        uint32_t first = data.Scalar(end), last = data.Scalar(end + input.length);
        if (first == UINT32_MAX || last == UINT32_MAX) return nullptr;
        auto [it, added] = captured.try_emplace(format, static_cast<uint32_t>(data.formats.size()));
        if (added) data.formats.emplace_back(new JaliumTextFormat(*format, JaliumTextFormat::SnapshotTag{}));
        data.styles.push_back({end, end + input.length, first, last, it->second, input.r, input.g, input.b, input.a});
        end += input.length;
    }
    if (end != length) return nullptr;
    auto& drawing = *data.formats.front();
    drawing.alignment_ = JALIUM_TEXT_ALIGN_LEADING;
    drawing.paragraphAlignment_ = JALIUM_PARAGRAPH_ALIGN_NEAR;
    auto layout = std::make_shared<JaliumTextFormat::LayoutResult>();
    TextBidi bidi(data.text.data(), static_cast<uint32_t>(data.text.size()), direction);
    std::vector<ShapedGlyph> logical;
    for (size_t styleIndex = 0; styleIndex < data.styles.size();) {
        const auto& style = data.styles[styleIndex];
        size_t limit = styleIndex + 1;
        while (limit < data.styles.size()) {
            const auto& next = data.styles[limit];
            if (next.format != style.format) break;
            ++limit;
        }
        auto& format = *data.formats[style.format];
        const auto last = data.styles[limit - 1].scalarEnd;
        auto shaped = format.ShapeWithFallback(bidi.MirroredText() + style.scalarFirst,
            last - style.scalarFirst, bidi, style.scalarFirst);
        for (auto glyph : shaped.glyphs) {
            glyph.cluster += style.scalarFirst; glyph.clusterEnd += style.scalarFirst;
            glyph.fontSize = format.fontSizePx_;
            logical.push_back(glyph);
        }
        styleIndex = limit;
    }
    const auto boundaries = grapheme::Boundaries(data.text);
    auto clusterStart = [&](uint32_t position) {
        auto it = std::upper_bound(boundaries.begin(), boundaries.end(), position);
        return it == boundaries.begin() ? 0 : *(it - 1);
    };
    size_t firstGlyph = 0, cursor = 0, lastBreak = 0;
    uint32_t firstCharacter = 0;
    float pen = 0, top = 0;
    auto emit = [&](size_t first, size_t last, uint32_t firstSource, uint32_t lastSource, bool soft) {
        JaliumTextFormat::LayoutLine line{}; line.startIndex = firstSource; line.endIndex = lastSource;
        line.glyphs.assign(logical.begin() + first, logical.begin() + last);
        float ascent = defaults.ascent_, descent = defaults.descent_, leading = defaults.lineGap_;
        auto style = std::upper_bound(data.styles.begin(), data.styles.end(), firstSource,
            [](uint32_t value, const auto& style) { return value < style.scalarEnd; });
        for (; style != data.styles.end() && style->scalarFirst < lastSource; ++style) {
            const auto& format = *data.formats[style->format];
            ascent = std::max(ascent, format.ascent_); descent = std::max(descent, format.descent_); leading = std::max(leading, format.lineGap_);
        }
        line.height = std::max(minHeight, ascent + descent + leading);
        line.ascent = ascent + std::max(0.f, line.height - ascent - descent - leading) * .5f;
        line.baselineY = top + line.ascent; top += line.height;
        for (const auto& glyph : line.glyphs) line.width += glyph.advanceX;
        float trimmed = line.width;
        for (auto it = line.glyphs.rbegin(); it != line.glyphs.rend(); ++it) {
            if (it->cluster >= data.text.size() || (data.text[it->cluster] != L' ' && data.text[it->cluster] != L'\t')) break;
            trimmed -= it->advanceX;
        }
        if (alignment == 3 && soft && width > trimmed) {
            size_t opportunities = 0;
            for (const auto& glyph : line.glyphs)
                if (glyph.cluster < data.text.size() && data.text[glyph.cluster] == L' ' && glyph.clusterEnd < lastSource) ++opportunities;
            if (opportunities) {
                const float extra = (width - line.width) / opportunities;
                for (auto& glyph : line.glyphs)
                    if (glyph.cluster < data.text.size() && data.text[glyph.cluster] == L' ' && glyph.clusterEnd < lastSource) glyph.advanceX += extra;
                line.width = width; trimmed = width;
            }
        }
        layout->totalWidth = std::max(layout->totalWidth, std::max(0.f, trimmed));
        layout->totalWidthIncludingTrailingWhitespace = std::max(layout->totalWidthIncludingTrailingWhitespace, line.width);
        bidi.Reorder(line.glyphs, firstSource, lastSource);
        layout->lines.push_back(std::move(line));
    };
    while (cursor < logical.size()) {
        const auto& glyph = logical[cursor]; uint32_t cp = uint32_t(data.text[glyph.cluster]);
        if (TextBidi::IsLineBreak(cp)) {
            emit(firstGlyph, cursor, firstCharacter, glyph.cluster, false);
            uint32_t next = glyph.clusterEnd; ++cursor;
            if (cp == '\r' && next < data.text.size() && data.text[next] == L'\n') {
                ++next; if (cursor < logical.size() && logical[cursor].cluster < next) ++cursor;
            }
            firstGlyph = cursor; firstCharacter = next; lastBreak = cursor; pen = 0; continue;
        }
        if (wrapping != JALIUM_WORD_WRAP_NONE && width > 0 && pen > 0 && pen + glyph.advanceX > width) {
            size_t at = wrapping == JALIUM_WORD_WRAP_CHARACTER ? cursor : lastBreak;
            const uint32_t cluster = clusterStart(glyph.cluster);
            if (wrapping == JALIUM_WORD_WRAP_CHARACTER)
                while (at > firstGlyph && logical[at - 1].cluster >= cluster) --at;
            if (at > firstGlyph) {
                uint32_t next = logical[at].cluster;
                emit(firstGlyph, at, firstCharacter, next, true);
                firstGlyph = at; firstCharacter = next; cursor = at; lastBreak = at; pen = 0; continue;
            }
        }
        pen += glyph.advanceX;
        if (cp == ' ' || cp == '\t') lastBreak = cursor + 1;
        ++cursor;
    }
    if (firstGlyph < logical.size() || firstCharacter == data.text.size() || layout->lines.empty())
        emit(firstGlyph, logical.size(), firstCharacter, static_cast<uint32_t>(data.text.size()), false);
    layout->totalHeight = data.height = top;
    float maximumSize = defaults.fontSizePx_;
    for (const auto& format : data.formats) maximumSize = std::max(maximumSize, format->fontSizePx_);
    // The width selects wrapping/alignment; it is not a glyph clipping box.
    // A single-line input can scroll to any part of an over-wide paragraph.
    data.drawingWidth = std::max(width, layout->totalWidthIncludingTrailingWhitespace) + 2 * maximumSize;
    data.drawingHeight = top + 2 * maximumSize;
    drawing.frozenLayout_ = layout;
    for (uint32_t index = 0; index < layout->lines.size(); ++index) {
        auto& line = layout->lines[index]; Row row;
        auto& metrics = row.metrics; auto& m = metrics.line;
        const uint32_t nextSource = index + 1 < layout->lines.size() ? layout->lines[index + 1].startIndex : line.endIndex;
        m.textPosition = data.sourceOffsets[line.startIndex];
        m.length = data.sourceOffsets[nextSource] - m.textPosition;
        m.y = line.baselineY - line.ascent; m.height = line.height; m.width = line.width;
        m.x = alignment == 1 ? width - line.width : alignment == 2 ? (width - line.width) * .5f : 0;
        metrics.baseline = line.ascent;
        auto first = std::lower_bound(boundaries.begin(), boundaries.end(), line.startIndex);
        auto last = std::lower_bound(boundaries.begin(), boundaries.end(), line.endIndex);
        const auto cellsEnd = std::lower_bound(boundaries.begin(), boundaries.end(), nextSource);
        for (auto it = first; it < cellsEnd; ++it) {
            const uint32_t a = *it, b = *(it + 1);
            row.cells.push_back({data.sourceOffsets[a], data.sourceOffsets[b], 0, 0, false,
                !TextBidi::IsFormattingControl(uint32_t(data.text[a])) && !TextBidi::IsLineBreak(uint32_t(data.text[a]))});
        }
        float x = m.x;
        for (const auto& glyph : line.glyphs) {
            auto begin = std::upper_bound(first, last + (last != boundaries.end()), glyph.cluster);
            if (begin != first) --begin;
            auto finish = std::lower_bound(begin, boundaries.end(), glyph.clusterEnd);
            const size_t parts = std::max<size_t>(1, finish - begin);
            for (auto it = begin; it < finish && it < last; ++it) {
                const size_t cellIndex = it - first;
                if (cellIndex >= row.cells.size()) break;
                auto& cell = row.cells[cellIndex];
                const float local = float(it - begin) / parts, next = float(it - begin + 1) / parts;
                float a = x + glyph.advanceX * (glyph.bidiLevel & 1 ? 1 - local : local);
                float b = x + glyph.advanceX * (glyph.bidiLevel & 1 ? 1 - next : next);
                if (!cell.assigned) { cell.leading = a; cell.trailing = b; cell.assigned = true; }
                else {
                    float left = std::min({cell.leading, cell.trailing, a, b}), right = std::max({cell.leading, cell.trailing, a, b});
                    cell.leading = glyph.bidiLevel & 1 ? right : left; cell.trailing = glyph.bidiLevel & 1 ? left : right;
                }
            }
            x += glyph.advanceX;
        }
        for (size_t i = 0; i < row.cells.size(); ++i) {
            auto& cell = row.cells[i];
            if (!cell.assigned) {
                float edge = i ? row.cells[i - 1].trailing : m.x;
                if (!i) for (const auto& following : row.cells) if (following.assigned) { edge = following.leading; break; }
                cell.leading = cell.trailing = edge;
            }
            if (!cell.visible || std::abs(cell.leading - cell.trailing) < .00001f) continue;
            row.visualCells.push_back(static_cast<uint32_t>(i));
            row.visualEdges.push_back({cell.first, 0, cell.leading}); row.visualEdges.push_back({cell.end, 1, cell.trailing});
        }
        std::sort(row.visualCells.begin(), row.visualCells.end(), [&](auto a, auto b) {
            return std::min(row.cells[a].leading, row.cells[a].trailing) < std::min(row.cells[b].leading, row.cells[b].trailing); });
        std::stable_sort(row.visualEdges.begin(), row.visualEdges.end(), [](const auto& a, const auto& b) { return a.x < b.x; });
        if (row.visualEdges.empty()) row.visualEdges.push_back({m.textPosition, 0, m.x});
        m.leftCaretPosition = row.visualEdges.front().textPosition; m.leftBackwardAffinity = row.visualEdges.front().backwardAffinity;
        m.rightCaretPosition = row.visualEdges.back().textPosition; m.rightBackwardAffinity = row.visualEdges.back().backwardAffinity;
        std::vector<JaliumTextFragmentMetrics> pieces;
        for (auto cellIndex : row.visualCells) {
            const auto& cell = row.cells[cellIndex];
            auto style = std::upper_bound(data.styles.begin(), data.styles.end(), cell.first,
                [](uint32_t value, const auto& style) { return value < style.end; });
            for (; style != data.styles.end() && style->first < cell.end; ++style) {
                const float left = std::min(cell.leading, cell.trailing), right = std::max(cell.leading, cell.trailing);
                uint32_t firstSource = std::max(cell.first, style->first), endSource = std::min(cell.end, style->end);
                pieces.push_back({firstSource, endSource-firstSource, static_cast<uint32_t>(style-data.styles.begin()), left, right-left});
            }
        }
        std::sort(pieces.begin(), pieces.end(), [](const auto& a, const auto& b) {
            return a.spanIndex < b.spanIndex || (a.spanIndex == b.spanIndex && a.x < b.x); });
        for (const auto& piece : pieces) {
            if (!row.fragments.empty() && row.fragments.back().spanIndex == piece.spanIndex &&
                std::abs(row.fragments.back().x+row.fragments.back().width-piece.x)<.0001f) {
                auto& fragment=row.fragments.back();fragment.width=piece.x+piece.width-fragment.x;
                uint32_t end=std::max(fragment.textPosition+fragment.length,piece.textPosition+piece.length);
                fragment.textPosition=std::min(fragment.textPosition,piece.textPosition);fragment.length=end-fragment.textPosition;
            } else row.fragments.push_back(piece);
        }
        data.rows.push_back(std::move(row));
    }
    return owner.release();
}

bool SharedTextParagraph::ApplyAdvances(const JaliumTextAdvanceUnit* input, uint32_t count)
{
    auto& data = *impl_;
    if (!input || !count || !data.formats.front()->frozenLayout_) return false;
    uint32_t end = 0;
    for (uint32_t i = 0; i < count; ++i) {
        const auto& unit = input[i];
        if (unit.textPosition != end || !unit.length || unit.length > data.sourceLength - end ||
            data.Scalar(end) == UINT32_MAX || data.Scalar(end + unit.length) == UINT32_MAX ||
            !std::isfinite(unit.spacing) || !std::isfinite(unit.leading) || !std::isfinite(unit.trailing) ||
            std::abs(unit.spacing) > 1000000 || std::abs(unit.leading) > 1000000 ||
            std::abs(unit.trailing) > 1000000 || (unit.flags & ~7u)) return false;
        end += unit.length;
    }
    if (end != data.sourceLength) return false;
    auto layout = std::make_shared<JaliumTextFormat::LayoutResult>(*data.formats.front()->frozenLayout_);
    layout->totalWidth = layout->totalWidthIncludingTrailingWhitespace = 0;
    struct Unit {
        JaliumTextAdvanceUnit source;
        double start, end, left, right, ownLeft = 0, ownRight = 0;
        double leftPad = 0, rightPad = 0, leftShift = 0, rightShift = 0, glyphShift = 0;
    };
    for (uint32_t index = 0; index < data.rows.size(); ++index) {
        auto& row = data.rows[index]; auto& metrics = row.metrics.line;
        const float originalOrigin = metrics.x;
        const uint32_t rowEnd = metrics.textPosition + metrics.length;
        std::vector<Unit> units; std::vector<JaliumTextAdvanceUnit> ignored;
        auto first = std::lower_bound(input, input + count, metrics.textPosition,
            [](const auto& unit, uint32_t position) { return unit.textPosition < position; });
        for (auto unit = first; unit != input + count && unit->textPosition < rowEnd; ++unit) {
            if (unit->length > rowEnd - unit->textPosition) return false;
            if (unit->flags & 1) { ignored.push_back(*unit); continue; }
            JaliumParagraphCaret a{}, b{};
            if (GetCaret(index, unit->textPosition, false, &a) != JALIUM_OK ||
                GetCaret(index, unit->textPosition + unit->length, true, &b) != JALIUM_OK) return false;
            units.push_back({*unit, a.x, b.x, std::min(a.x, b.x), std::max(a.x, b.x)});
            auto& cell = units.back(); const double natural = cell.right - cell.left;
            if (!(unit->flags & 4) || natural > .000001) {
                cell.ownLeft = b.x < a.x ? unit->trailing : unit->leading;
                cell.ownRight = b.x < a.x ? unit->leading : unit->trailing;
                const double requested = cell.ownLeft + cell.ownRight;
                if (requested < -natural) {
                    const double scale = -natural / requested;
                    cell.ownLeft *= scale; cell.ownRight *= scale;
                }
            }
        }
        std::stable_sort(units.begin(), units.end(), [](const auto& a, const auto& b) { return a.left < b.left; });
        for (size_t i = 1; i < units.size(); ++i) {
            auto& before = units[i - 1]; auto& after = units[i];
            if ((before.source.flags & after.source.flags & 2) != 0) continue;
            const double a = before.source.spacing / 2.0, b = after.source.spacing / 2.0;
            const double requested = a + b;
            const double limit = std::min(before.right - before.left + before.ownLeft + before.ownRight,
                after.right - after.left + after.ownLeft + after.ownRight);
            const double actual = std::max(-limit, requested);
            const double scale = requested < 0 && actual != requested ? actual / requested : 1;
            before.rightPad = a * scale; after.leftPad = b * scale;
        }
        double extra = 0;
        for (auto& unit : units) {
            unit.leftShift = extra; unit.glyphShift = extra + unit.ownLeft + unit.leftPad;
            extra += unit.ownLeft + unit.ownRight + unit.leftPad + unit.rightPad;
            unit.rightShift = extra;
            if (unit.right + unit.rightShift < unit.left + unit.leftShift - .001) return false;
        }
        std::vector<size_t> logical(units.size());
        for (size_t i = 0; i < logical.size(); ++i) logical[i] = i;
        std::sort(logical.begin(), logical.end(), [&](size_t a, size_t b) { return units[a].source.textPosition < units[b].source.textPosition; });
        auto firstAt = [&](uint32_t position) {
            return std::lower_bound(logical.begin(), logical.end(), position, [&](size_t i, uint32_t p) {
                return units[i].source.textPosition + units[i].source.length <= p;
            });
        };
        for (auto& glyph : layout->lines[index].glyphs) {
            const uint32_t start = data.sourceOffsets[glyph.cluster], last = data.sourceOffsets[glyph.clusterEnd];
            double shift = 0; uint32_t members = 0;
            for (auto member = firstAt(start); member != logical.end(); ++member) {
                const auto& unit = units[*member]; if (unit.source.textPosition >= last) break;
                shift += unit.glyphShift; ++members;
            }
            if (members) glyph.offsetX += static_cast<float>(shift / members);
        }
        std::vector<Cell> cells;
        for (const auto& unit : units) {
            const bool rtl = unit.end < unit.start;
            cells.push_back({unit.source.textPosition, unit.source.textPosition + unit.source.length,
                static_cast<float>(unit.start + (rtl ? unit.rightShift : unit.leftShift)),
                static_cast<float>(unit.end + (rtl ? unit.leftShift : unit.rightShift)), true, true});
        }
        for (const auto& unit : ignored) {
            JaliumParagraphCaret old{};
            if (GetCaret(index, unit.textPosition, false, &old) != JALIUM_OK) return false;
            double mapped = 0;
            if (!units.empty()) {
                auto distance = [&](const auto& cell) { return std::min(std::abs(old.x-cell.left), std::abs(old.x-cell.right)); };
                auto nearest = std::lower_bound(units.begin(), units.end(), old.x,
                    [](const auto& cell, double x) { return cell.left < x; });
                if (nearest == units.end()) --nearest;
                if (nearest != units.begin() && distance(*(nearest-1)) <= distance(*nearest)) --nearest;
                mapped = std::abs(old.x-nearest->left) <= std::abs(old.x-nearest->right)
                    ? nearest->left + nearest->leftShift : nearest->right + nearest->rightShift;
            }
            cells.push_back({unit.textPosition, unit.textPosition + unit.length, static_cast<float>(mapped), static_cast<float>(mapped), true, false});
        }
        std::sort(cells.begin(), cells.end(), [](const auto& a, const auto& b) { return a.first < b.first; });
        row.cells = std::move(cells); row.visualCells.clear(); row.visualEdges.clear(); row.fragments.clear();
        for (uint32_t i = 0; i < row.cells.size(); ++i) {
            const auto& cell = row.cells[i];
            if (!cell.visible || std::abs(cell.leading-cell.trailing) < .00001f) continue;
            row.visualCells.push_back(i);
            row.visualEdges.push_back({cell.first, 0, cell.leading}); row.visualEdges.push_back({cell.end, 1, cell.trailing});
        }
        std::stable_sort(row.visualCells.begin(), row.visualCells.end(), [&](auto a, auto b) {
            return std::min(row.cells[a].leading,row.cells[a].trailing) < std::min(row.cells[b].leading,row.cells[b].trailing); });
        std::stable_sort(row.visualEdges.begin(), row.visualEdges.end(), [](const auto& a, const auto& b) { return a.x < b.x; });
        if (row.visualEdges.empty()) row.visualEdges.push_back({metrics.textPosition, 0, 0});
        metrics.x = row.visualEdges.front().x; metrics.width = row.visualEdges.back().x - metrics.x;
        // Normalize the pen before raster snapping. RenderParagraph supplies
        // the used-row origin, which can move after hidden RTL whitespace.
        layout->lines[index].originX = originalOrigin-metrics.x;
        metrics.leftCaretPosition = row.visualEdges.front().textPosition; metrics.leftBackwardAffinity = row.visualEdges.front().backwardAffinity;
        metrics.rightCaretPosition = row.visualEdges.back().textPosition; metrics.rightBackwardAffinity = row.visualEdges.back().backwardAffinity;
        std::vector<JaliumTextFragmentMetrics> pieces;
        for (auto i : row.visualCells) {
            const auto& cell = row.cells[i];
            auto style = std::upper_bound(data.styles.begin(), data.styles.end(), cell.first,
                [](uint32_t value, const auto& item) { return value < item.end; });
            for (; style != data.styles.end() && style->first < cell.end; ++style) {
                const auto start = std::max(cell.first,style->first), last = std::min(cell.end,style->end);
                pieces.push_back({start,last-start,static_cast<uint32_t>(style-data.styles.begin()),
                    std::min(cell.leading,cell.trailing),std::abs(cell.leading-cell.trailing)});
            }
        }
        std::sort(pieces.begin(),pieces.end(),[](const auto& a,const auto& b) { return a.spanIndex<b.spanIndex || (a.spanIndex==b.spanIndex&&a.x<b.x); });
        for (const auto& piece : pieces) {
            if (!row.fragments.empty() && row.fragments.back().spanIndex == piece.spanIndex &&
                std::abs(row.fragments.back().x+row.fragments.back().width-piece.x) < .0001f) {
                auto& fragment = row.fragments.back(); fragment.width = piece.x+piece.width-fragment.x;
                const auto last = std::max(fragment.textPosition+fragment.length,piece.textPosition+piece.length);
                fragment.textPosition = std::min(fragment.textPosition,piece.textPosition); fragment.length = last-fragment.textPosition;
            } else row.fragments.push_back(piece);
        }
        auto& line = layout->lines[index]; line.width = metrics.width;
        layout->totalWidth = std::max(layout->totalWidth,line.width);
        layout->totalWidthIncludingTrailingWhitespace = std::max(layout->totalWidthIncludingTrailingWhitespace,line.width);
    }
    float maximumSize = 0;
    for (const auto& format : data.formats) maximumSize = std::max(maximumSize,format->fontSizePx_);
    data.drawingWidth = std::max(data.drawingWidth,layout->totalWidthIncludingTrailingWhitespace + 2*maximumSize);
    data.formats.front()->frozenLayout_ = std::move(layout);
    return true;
}

uint32_t SharedTextParagraph::LineCount() const { return static_cast<uint32_t>(impl_->rows.size()); }
JaliumResult SharedTextParagraph::GetLine(uint32_t line, JaliumParagraphLineMetrics* result) const
{
    if (!result || line >= LineCount()) return JALIUM_ERROR_INVALID_ARGUMENT;
    *result = impl_->rows[line].metrics; return JALIUM_OK;
}
JaliumResult SharedTextParagraph::GetFragments(uint32_t line, JaliumTextFragmentMetrics* results, uint32_t capacity, uint32_t* count) const
{
    if (line >= LineCount()) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Copy(impl_->rows[line].fragments, results, capacity, count);
}
JaliumResult SharedTextParagraph::GetCaret(uint32_t line, uint32_t position, bool backward, JaliumParagraphCaret* result) const
{
    if (!result || line >= LineCount()) return JALIUM_ERROR_INVALID_ARGUMENT;
    const auto& row = impl_->rows[line]; const auto& m = row.metrics.line;
    if (position < m.textPosition || position - m.textPosition > m.length) return JALIUM_ERROR_INVALID_ARGUMENT;
    const auto& cells = row.cells;
    auto it = std::upper_bound(cells.begin(), cells.end(), position, [](uint32_t value, const Cell& cell) { return value < cell.first; });
    if (it != cells.begin()) --it;
    if (it != cells.end() && position > it->first && position < it->end) position = it->first;
    if (backward) {
        it = std::lower_bound(cells.begin(), cells.end(), position, [](const Cell& cell, uint32_t value) { return cell.end < value; });
        if (it != cells.end() && it->end == position) { *result = {position, 1, it->trailing}; return JALIUM_OK; }
    }
    it = std::lower_bound(cells.begin(), cells.end(), position, [](const Cell& cell, uint32_t value) { return cell.first < value; });
    float x = it != cells.end() ? it->leading : cells.empty() ? m.x : cells.back().trailing;
    *result = {position, backward ? 1 : 0, x}; return JALIUM_OK;
}
JaliumResult SharedTextParagraph::HitTest(uint32_t line, float x, JaliumParagraphCaret* result) const
{
    if (!result || line >= LineCount() || !std::isfinite(x)) return JALIUM_ERROR_INVALID_ARGUMENT;
    const auto& row = impl_->rows[line];
    auto cell = std::upper_bound(row.visualCells.begin(), row.visualCells.end(), x, [&](float value, uint32_t index) {
        return value < std::min(row.cells[index].leading, row.cells[index].trailing); });
    if (cell != row.visualCells.begin()) {
        const auto& hit = row.cells[*(cell - 1)];
        if (x < std::max(hit.leading, hit.trailing)) {
            *result = std::abs(x - hit.leading) < std::abs(x - hit.trailing)
                ? JaliumParagraphCaret{hit.first, 0, hit.leading} : JaliumParagraphCaret{hit.end, 1, hit.trailing};
            return JALIUM_OK;
        }
    }
    const auto& edges = row.visualEdges;
    auto edge = std::lower_bound(edges.begin(), edges.end(), x, [](const auto& value, float point) { return value.x < point; });
    if (edge == edges.end()) --edge;
    else if (edge != edges.begin() && x - (edge - 1)->x <= edge->x - x) --edge;
    *result = *edge; return JALIUM_OK;
}
JaliumResult SharedTextParagraph::GetSelection(uint32_t line, uint32_t start, uint32_t length,
    JaliumTextRangeMetrics* results, uint32_t capacity, uint32_t* count) const
{
    if (line >= LineCount() || start > impl_->sourceLength || length > impl_->sourceLength - start) return JALIUM_ERROR_INVALID_ARGUMENT;
    const auto& row = impl_->rows[line]; std::vector<JaliumTextRangeMetrics> rectangles;
    if (length) {
        auto cell = std::upper_bound(row.cells.begin(), row.cells.end(), start, [](uint32_t value, const Cell& cell) { return value < cell.end; });
        for (; cell != row.cells.end() && cell->first < start + length; ++cell) {
            if (!cell->visible) continue;
            const float left = std::min(cell->leading, cell->trailing), right = std::max(cell->leading, cell->trailing);
            if (right > left) rectangles.push_back({cell->first, cell->end - cell->first, left, row.metrics.line.y, right - left, row.metrics.line.height});
        }
    }
    std::sort(rectangles.begin(), rectangles.end(), [](const auto& a, const auto& b) { return a.x < b.x; });
    std::vector<JaliumTextRangeMetrics> merged;
    for (const auto& rectangle : rectangles) {
        if (!merged.empty() && rectangle.x <= merged.back().x + merged.back().width + .0001f) {
            auto& last = merged.back(); last.width = std::max(last.x + last.width, rectangle.x + rectangle.width) - last.x;
            const uint32_t end = std::max(last.textPosition + last.length, rectangle.textPosition + rectangle.length);
            last.textPosition = std::min(last.textPosition, rectangle.textPosition); last.length = end - last.textPosition;
        } else merged.push_back(rectangle);
    }
    return Copy(merged, results, capacity, count);
}
std::vector<SharedTextParagraph::PaintRun> SharedTextParagraph::PaintRuns(uint32_t line, uint32_t start, uint32_t length,
    const TextGlyphRanges& filter, const float* rgba) const
{
    std::vector<PaintRun> result;
    if (line >= LineCount() || start > impl_->sourceLength || length > impl_->sourceLength - start) return result;
    const auto& m = impl_->rows[line].metrics.line;
    const uint32_t first = std::max(start, m.textPosition), end = std::min(start + length, m.textPosition + m.length);
    for (const auto& style : impl_->styles) {
        uint32_t a = std::max(first, style.first), b = std::min(end, style.end); if (b <= a) continue;
        PaintRun run{{}, rgba ? rgba[0] : style.r, rgba ? rgba[1] : style.g, rgba ? rgba[2] : style.b, rgba ? rgba[3] : style.a};
        std::vector<std::pair<uint32_t,uint32_t>> intervals;
        for (const auto& glyph : impl_->formats.front()->frozenLayout_->lines[line].glyphs) {
            const uint32_t firstSource = impl_->sourceOffsets[glyph.cluster];
            const uint32_t endSource = impl_->sourceOffsets[glyph.clusterEnd];
            // A cluster crossing a paint boundary belongs to its final source
            // character. This matches the CoreText slice policy and paints a
            // ligature once, including when the two colors are translucent.
            // Visibility still selects complete glyphs by source overlap.
            if (endSource > a && endSource <= b && endSource > firstSource &&
                TextGlyphVisible(firstSource, endSource, filter.Data(), filter.Count(), filter.Mode()))
                intervals.emplace_back(firstSource, endSource);
        }
        std::sort(intervals.begin(), intervals.end());
        for (const auto& part : intervals) {
            if (!run.ranges.empty() && part.first <= run.ranges[run.ranges.size()-2] + run.ranges.back())
                run.ranges.back() = std::max(run.ranges.back(), part.second - run.ranges[run.ranges.size()-2]);
            else { run.ranges.push_back(part.first); run.ranges.push_back(part.second-part.first); }
        }
        if (!run.ranges.empty()) result.push_back(std::move(run));
    }
    return result;
}
const wchar_t* SharedTextParagraph::Text() const { return impl_->text.c_str(); }
uint32_t SharedTextParagraph::TextLength() const { return static_cast<uint32_t>(impl_->text.size()); }
JaliumTextFormat* SharedTextParagraph::DrawingFormat() const { return impl_->formats.front().get(); }
float SharedTextParagraph::Width() const { return impl_->drawingWidth; }
float SharedTextParagraph::Height() const { return impl_->drawingHeight; }
} // namespace jalium
