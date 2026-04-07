#pragma once

#include "glyph_rasterizer.h"

#include <cstdint>
#include <unordered_map>
#include <vector>
#include <mutex>

typedef struct FT_FaceRec_* FT_Face;

namespace jalium {

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
    bool     valid = false;
};

/// Key for glyph cache lookup (FreeType-based, platform-neutral).
struct AtlasGlyphKey {
    uint64_t fontId;         ///< Hash of font family + weight + style
    uint16_t glyphIndex;     ///< HarfBuzz output glyph index
    uint16_t fontSize;       ///< Physical pixel size
    uint8_t  subpixelX;     ///< 1/4 pixel quantization (0..3)

    bool operator==(const AtlasGlyphKey& other) const {
        return fontId == other.fontId &&
               glyphIndex == other.glyphIndex &&
               fontSize == other.fontSize &&
               subpixelX == other.subpixelX;
    }
};

struct AtlasGlyphKeyHash {
    size_t operator()(const AtlasGlyphKey& k) const {
        size_t h = std::hash<uint64_t>{}(k.fontId);
        h ^= std::hash<uint32_t>{}(
            ((uint32_t)k.glyphIndex << 16) |
            ((uint32_t)k.fontSize << 2) |
            k.subpixelX
        ) + 0x9e3779b9 + (h << 6) + (h >> 2);
        return h;
    }
};

/// Glyph quad instance for the text shader (48 bytes, matches D3D12).
struct TextGlyphQuad {
    float posX, posY;           ///< Screen position
    float sizeX, sizeY;        ///< Quad size
    float uvMinX, uvMinY;      ///< Atlas UV top-left
    float uvMaxX, uvMaxY;      ///< Atlas UV bottom-right
    float colorR, colorG, colorB, colorA; ///< Premultiplied RGBA
};
static_assert(sizeof(TextGlyphQuad) == 48, "TextGlyphQuad must be 48 bytes");

/// Dirty rectangle for tracking atlas regions that need GPU upload.
struct AtlasDirtyRect {
    uint32_t x, y, width, height;
};

class GlyphAtlas {
public:
    GlyphAtlas();
    ~GlyphAtlas();

    /// Looks up or rasterizes a glyph, inserting it into the atlas.
    /// @param rasterizer The glyph rasterizer to use if not cached.
    /// @param face FreeType face handle.
    /// @param fontId Unique identifier for the font (family+weight+style hash).
    /// @param glyphIndex Glyph index from HarfBuzz.
    /// @param fontSizePx Font size in pixels.
    /// @param subpixelX Sub-pixel X quantization (0..3).
    /// @return Atlas entry, or invalid entry if atlas is full.
    const AtlasGlyphEntry& GetOrInsert(
        GlyphRasterizer& rasterizer,
        FT_Face face,
        uint64_t fontId,
        uint16_t glyphIndex,
        uint16_t fontSizePx,
        uint8_t subpixelX);

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
