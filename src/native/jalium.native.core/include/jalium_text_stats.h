#pragma once

#include <cstdint>
#include "jalium_api.h"

// Unified text-rendering telemetry. Single source of truth for the three
// caches that sit on the DrawText hot path:
//
//   1) Layout cache  — D3D12TextFormat::CreateLayout reuses the IDWriteTextLayout
//                      across calls with the same text+maxLines, so width/height
//                      changes don't re-run DirectWrite shaping.
//   2) Instance cache — D3D12GlyphAtlas::GenerateGlyphs memoises the resolved
//                       per-glyph quads (positions + UVs + decorations) for a
//                       shaped run, skipping layout->Draw and the per-glyph
//                       atlas walk on a hit.
//   3) Glyph raster cache — per-(fontFace, glyphIndex, fontSize, subpixel,
//                           aaMode, hinting) rasterized atlas slot.
//
// Same pattern as jalium_path_stats.h / jalium_bitmap_stats.h — see those for
// the rationale (cross-dll global state must live in core, not per-backend).

#ifdef __cplusplus
extern "C" {
#endif

typedef struct JaliumTextStats {
    uint64_t version;
    uint64_t layoutHits;
    uint64_t layoutMisses;
    uint64_t layoutEvictions;
    uint64_t instanceHits;
    uint64_t instanceMisses;
    uint64_t instanceEvictions;
    uint64_t glyphRasterHits;
    uint64_t glyphRasterMisses;
    uint64_t atlasResets;
    uint64_t drawTextCalls;       // RenderText entry-point invocations
    uint64_t emittedGlyphs;       // total glyph quads emitted (hit + miss path)
    uint64_t emittedDecorations;  // total underline / strikethrough rects emitted
    uint64_t reserved[16];
} JaliumTextStats;

#define JALIUM_TEXT_STATS_VERSION 1u

JALIUM_API void jalium_query_text_stats(JaliumTextStats* out);
JALIUM_API void jalium_reset_text_stats(void);

#ifdef __cplusplus
}  // extern "C"

namespace jalium {
namespace text_stats {

JALIUM_API void AddLayoutHit() noexcept;
JALIUM_API void AddLayoutMiss() noexcept;
JALIUM_API void AddLayoutEviction(uint64_t count) noexcept;

JALIUM_API void AddInstanceHit() noexcept;
JALIUM_API void AddInstanceMiss() noexcept;
JALIUM_API void AddInstanceEviction(uint64_t count) noexcept;

JALIUM_API void AddGlyphRasterHit(uint64_t count) noexcept;
JALIUM_API void AddGlyphRasterMiss(uint64_t count) noexcept;

JALIUM_API void AddAtlasReset() noexcept;

JALIUM_API void AddDrawTextCall() noexcept;
JALIUM_API void AddEmittedGlyphs(uint64_t count) noexcept;
JALIUM_API void AddEmittedDecorations(uint64_t count) noexcept;

}  // namespace text_stats
}  // namespace jalium

#endif  // __cplusplus
