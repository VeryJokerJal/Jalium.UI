#pragma once

#include "glyph_rasterizer.h"
#include "jalium_text_api.h"

#include <cstdint>
#include <unordered_map>
#include <vector>
#include <mutex>

namespace jalium {

enum AtlasGlyphFlags : uint8_t {
    ATLAS_GLYPH_NONE = 0,
    ATLAS_GLYPH_LCD = 1u << 0,
    ATLAS_GLYPH_COLOR = 1u << 1,
};

class FontFace;

// ============================================================================
// GlyphAtlas: CPU-side glyph atlas management
//
// Manages a 4096x4096 R8G8B8A8 texture atlas for text rendering.
// Row-based packing with dirty region tracking.
// Mirrors the D3D12GlyphAtlas design for consistency.
// ============================================================================

static constexpr uint32_t kAtlasWidth = 4096;
static constexpr uint32_t kAtlasHeight = 4096;
static constexpr uint32_t kAtlasBytesPerPixel = 4; // R8G8B8A8

/// Atlas entry: cached position of a rasterized glyph in the atlas.
struct AtlasGlyphEntry {
    uint16_t x, y;          ///< Position in atlas (pixels)
    uint16_t w, h;          ///< Glyph bitmap size (pixels)
    int16_t  bearingX;       ///< Horizontal bearing
    int16_t  bearingY;       ///< Vertical bearing (positive = up)
    uint8_t  flags = ATLAS_GLYPH_NONE;
    bool     valid = false;
};

/// Key for glyph cache lookup (platform-neutral).
struct AtlasGlyphKey {
    uint64_t fontId;         ///< Hash of font family + weight + style
    uint16_t glyphIndex;     ///< Shaped glyph index
    uint16_t fontSize;       ///< Physical pixel size
    uint8_t  subpixelX;     ///< 1/8 pixel quantization (0..7)
    uint8_t  antialiasMode; ///< GlyphAntialiasMode; part of cache identity

    bool operator==(const AtlasGlyphKey& other) const {
        return fontId == other.fontId &&
               glyphIndex == other.glyphIndex &&
               fontSize == other.fontSize &&
               subpixelX == other.subpixelX &&
               antialiasMode == other.antialiasMode;
    }
};

struct AtlasGlyphKeyHash {
    size_t operator()(const AtlasGlyphKey& k) const {
        size_t h = std::hash<uint64_t>{}(k.fontId);
        // Pack non-overlapping in a uint64: subpixelX needs 3 bits now (0..7,
        // 1/8-pixel buckets), so widen past the old uint32 layout where it would
        // collide with fontSize. [0..2] subpixelX | [3..4] AA mode |
        // [5..20] fontSize | [21..36] glyphIndex.
        uint64_t packed = ((uint64_t)(k.subpixelX & 0x7))
                        | ((uint64_t)(k.antialiasMode & 0x3) << 3)
                        | ((uint64_t)k.fontSize   << 5)
                        | ((uint64_t)k.glyphIndex << 21);
        h ^= std::hash<uint64_t>{}(packed) + 0x9e3779b9 + (h << 6) + (h >> 2);
        return h;
    }
};

/// Glyph quad instance for the text shader (48 bytes, matches D3D12).
struct TextGlyphQuad {
    float posX, posY;           ///< Screen position
    float sizeX, sizeY;        ///< Quad size
    float uvMinX, uvMinY;      ///< Atlas UV top-left
    float uvMaxX, uvMaxY;      ///< Atlas UV bottom-right
    float colorR, colorG, colorB;
    // The original 48-byte C++ ABI ended with colorA. Self-hosted consumers
    // never sampled per-quad color (they already own the run brush), so reuse
    // those four bytes as metadata without changing vector stride across .so
    // rebuild boundaries. Legacy consumers see a meaningless colorA but keep
    // correct geometry/UVs; current consumers read AtlasGlyphFlags.
    union {
        float colorA;
        uint32_t flags;
    };
};
static_assert(sizeof(TextGlyphQuad) == 48, "TextGlyphQuad must preserve its backend ABI");

/// Dirty rectangle for tracking atlas regions that need GPU upload.
struct AtlasDirtyRect {
    uint32_t x, y, width, height;
};

class JALIUM_TEXT_API GlyphAtlas {
public:
    GlyphAtlas();
    ~GlyphAtlas();

    /// Looks up or rasterizes a glyph, inserting it into the atlas.
    /// @param rasterizer The glyph rasterizer to use if not cached.
    /// @param face Self-hosted font face.
    /// @param fontId Unique identifier for the font (family+weight+style hash).
    /// @param glyphIndex Glyph index from shaping.
    /// @param fontSizePx Font size in pixels.
    /// @param subpixelX Sub-pixel X quantization (0..7).
    /// @return Atlas entry, or invalid entry if atlas is full.
    const AtlasGlyphEntry& GetOrInsert(
        GlyphRasterizer& rasterizer,
        FontFace* face,
        uint64_t fontId,
        uint16_t glyphIndex,
        uint16_t fontSizePx,
        uint8_t subpixelX,
        GlyphAntialiasMode antialiasMode);

    /// Gets the raw atlas pixel data (RGBA8, 4096x4096).
    const uint8_t* GetPixelData() const { return atlasPixels_.data(); }

    /// Gets the atlas width.
    uint32_t GetWidth() const { return kAtlasWidth; }

    /// Gets the atlas height.
    uint32_t GetHeight() const { return kAtlasHeight; }

    /// Gets and clears the list of dirty rectangles since last call.
    std::vector<AtlasDirtyRect> TakeDirtyRects();

    /// Returns true if there are pending dirty rects.
    bool IsDirty() const { return !dirtyRects_.empty(); }

    /// Clears the entire atlas cache (e.g. on device lost).
    void Clear();

    // ── Diagnostics accessors (DevTools Perf tab) ──
    // Lock around mutex_ since cache_ may be written from a rasterization
    // thread while the UI thread is snapshotting.

    int32_t GetCacheEntryCount() {
        std::lock_guard<std::mutex> lock(mutex_);
        return static_cast<int32_t>(cache_.size());
    }

    int32_t GetEstimatedCapacity() const {
        return (kAtlasWidth * kAtlasHeight) / (16 * 16);
    }

    int64_t GetPackedBytes() {
        std::lock_guard<std::mutex> lock(mutex_);
        int64_t wholeRows = static_cast<int64_t>(packY_) * kAtlasWidth;
        int64_t currentRowPartial = static_cast<int64_t>(packX_) * rowHeight_;
        return (wholeRows + currentRowPartial) * 4;
    }

    int64_t GetTotalBytes() const {
        return static_cast<int64_t>(kAtlasWidth) * kAtlasHeight * 4;
    }

private:
    std::vector<uint8_t> atlasPixels_;

    // Row-based packer state
    uint32_t packX_ = 0;
    uint32_t packY_ = 0;
    uint32_t rowHeight_ = 0;

    // Glyph cache
    std::unordered_map<AtlasGlyphKey, AtlasGlyphEntry, AtlasGlyphKeyHash> cache_;

    // Dirty tracking
    std::vector<AtlasDirtyRect> dirtyRects_;

    // Thread safety
    std::mutex mutex_;

    // Invalid entry sentinel
    static const AtlasGlyphEntry kInvalidEntry;

    bool PackGlyph(uint32_t w, uint32_t h, uint32_t& outX, uint32_t& outY);
    void BlitToAtlas(uint32_t x, uint32_t y, uint32_t w, uint32_t h, const uint8_t* rgba);
};

} // namespace jalium
