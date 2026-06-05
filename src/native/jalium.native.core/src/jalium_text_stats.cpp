#include "jalium_text_stats.h"

#include <atomic>
#include <cstring>

namespace jalium {
namespace text_stats {
namespace {

struct TextStatsState {
    std::atomic<uint64_t> layoutHits{0};
    std::atomic<uint64_t> layoutMisses{0};
    std::atomic<uint64_t> layoutEvictions{0};
    std::atomic<uint64_t> instanceHits{0};
    std::atomic<uint64_t> instanceMisses{0};
    std::atomic<uint64_t> instanceEvictions{0};
    std::atomic<uint64_t> glyphRasterHits{0};
    std::atomic<uint64_t> glyphRasterMisses{0};
    std::atomic<uint64_t> atlasResets{0};
    std::atomic<uint64_t> drawTextCalls{0};
    std::atomic<uint64_t> emittedGlyphs{0};
    std::atomic<uint64_t> emittedDecorations{0};
};

TextStatsState& State() noexcept {
    static TextStatsState s;
    return s;
}

}  // namespace

void AddLayoutHit() noexcept {
    State().layoutHits.fetch_add(1, std::memory_order_relaxed);
}
void AddLayoutMiss() noexcept {
    State().layoutMisses.fetch_add(1, std::memory_order_relaxed);
}
void AddLayoutEviction(uint64_t count) noexcept {
    if (count) State().layoutEvictions.fetch_add(count, std::memory_order_relaxed);
}

void AddInstanceHit() noexcept {
    State().instanceHits.fetch_add(1, std::memory_order_relaxed);
}
void AddInstanceMiss() noexcept {
    State().instanceMisses.fetch_add(1, std::memory_order_relaxed);
}
void AddInstanceEviction(uint64_t count) noexcept {
    if (count) State().instanceEvictions.fetch_add(count, std::memory_order_relaxed);
}

void AddGlyphRasterHit(uint64_t count) noexcept {
    if (count) State().glyphRasterHits.fetch_add(count, std::memory_order_relaxed);
}
void AddGlyphRasterMiss(uint64_t count) noexcept {
    if (count) State().glyphRasterMisses.fetch_add(count, std::memory_order_relaxed);
}

void AddAtlasReset() noexcept {
    State().atlasResets.fetch_add(1, std::memory_order_relaxed);
}

void AddDrawTextCall() noexcept {
    State().drawTextCalls.fetch_add(1, std::memory_order_relaxed);
}
void AddEmittedGlyphs(uint64_t count) noexcept {
    if (count) State().emittedGlyphs.fetch_add(count, std::memory_order_relaxed);
}
void AddEmittedDecorations(uint64_t count) noexcept {
    if (count) State().emittedDecorations.fetch_add(count, std::memory_order_relaxed);
}

}  // namespace text_stats
}  // namespace jalium

extern "C" {

JALIUM_API void jalium_query_text_stats(JaliumTextStats* out) {
    if (!out) return;
    auto& s = jalium::text_stats::State();
    std::memset(out, 0, sizeof(*out));
    out->version            = JALIUM_TEXT_STATS_VERSION;
    out->layoutHits         = s.layoutHits.load(std::memory_order_relaxed);
    out->layoutMisses       = s.layoutMisses.load(std::memory_order_relaxed);
    out->layoutEvictions    = s.layoutEvictions.load(std::memory_order_relaxed);
    out->instanceHits       = s.instanceHits.load(std::memory_order_relaxed);
    out->instanceMisses     = s.instanceMisses.load(std::memory_order_relaxed);
    out->instanceEvictions  = s.instanceEvictions.load(std::memory_order_relaxed);
    out->glyphRasterHits    = s.glyphRasterHits.load(std::memory_order_relaxed);
    out->glyphRasterMisses  = s.glyphRasterMisses.load(std::memory_order_relaxed);
    out->atlasResets        = s.atlasResets.load(std::memory_order_relaxed);
    out->drawTextCalls      = s.drawTextCalls.load(std::memory_order_relaxed);
    out->emittedGlyphs      = s.emittedGlyphs.load(std::memory_order_relaxed);
    out->emittedDecorations = s.emittedDecorations.load(std::memory_order_relaxed);
}

JALIUM_API void jalium_reset_text_stats(void) {
    auto& s = jalium::text_stats::State();
    s.layoutHits.store(0, std::memory_order_relaxed);
    s.layoutMisses.store(0, std::memory_order_relaxed);
    s.layoutEvictions.store(0, std::memory_order_relaxed);
    s.instanceHits.store(0, std::memory_order_relaxed);
    s.instanceMisses.store(0, std::memory_order_relaxed);
    s.instanceEvictions.store(0, std::memory_order_relaxed);
    s.glyphRasterHits.store(0, std::memory_order_relaxed);
    s.glyphRasterMisses.store(0, std::memory_order_relaxed);
    s.atlasResets.store(0, std::memory_order_relaxed);
    s.drawTextCalls.store(0, std::memory_order_relaxed);
    s.emittedGlyphs.store(0, std::memory_order_relaxed);
    s.emittedDecorations.store(0, std::memory_order_relaxed);
}

}  // extern "C"
