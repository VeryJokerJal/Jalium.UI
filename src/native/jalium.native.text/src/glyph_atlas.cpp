#include "glyph_atlas.h"

#include <cstring>
#include <algorithm>

namespace jalium {

const AtlasGlyphEntry GlyphAtlas::kInvalidEntry = {};

GlyphAtlas::GlyphAtlas()
{
    atlasPixels_.resize(
        static_cast<size_t>(kAtlasWidth) * kAtlasHeight * kAtlasBytesPerPixel, 0);
}

GlyphAtlas::~GlyphAtlas() = default;

const AtlasGlyphEntry& GlyphAtlas::GetOrInsert(
    GlyphRasterizer& rasterizer,
    FT_Face face,
    uint64_t fontId,
    uint16_t glyphIndex,
    uint16_t fontSizePx,
    uint8_t subpixelX)
{
    std::lock_guard<std::mutex> lock(mutex_);

    // Check cache
    AtlasGlyphKey key{fontId, glyphIndex, fontSizePx, subpixelX};
    auto it = cache_.find(key);
    if (it != cache_.end())
        return it->second;

    // Rasterize the glyph
    RasterizedGlyph rasterized = rasterizer.Rasterize(
        face, glyphIndex, static_cast<float>(fontSizePx), subpixelX);

    AtlasGlyphEntry entry{};
    entry.bearingX = static_cast<int16_t>(rasterized.bearingX);
    entry.bearingY = static_cast<int16_t>(rasterized.bearingY);

    if (rasterized.width > 0 && rasterized.height > 0)
    {
        uint32_t outX, outY;
        if (!PackGlyph(rasterized.width, rasterized.height, outX, outY))
        {
            // Atlas is full — return invalid
            return kInvalidEntry;
        }

        entry.x = static_cast<uint16_t>(outX);
        entry.y = static_cast<uint16_t>(outY);
        entry.w = static_cast<uint16_t>(rasterized.width);
        entry.h = static_cast<uint16_t>(rasterized.height);
        entry.valid = true;

        // Blit glyph pixels to atlas
        BlitToAtlas(outX, outY, rasterized.width, rasterized.height,
                    rasterized.pixels.data());
    }
    else
    {
        // Empty glyph (e.g., space)
        entry.w = 0;
        entry.h = 0;
        entry.valid = true; // Valid but no pixels
    }

    auto [insertIt, _] = cache_.emplace(key, entry);
    return insertIt->second;
}

std::vector<AtlasDirtyRect> GlyphAtlas::TakeDirtyRects()
{
    std::lock_guard<std::mutex> lock(mutex_);
    std::vector<AtlasDirtyRect> rects;
    rects.swap(dirtyRects_);
    return rects;
}

void GlyphAtlas::Clear()
{
    std::lock_guard<std::mutex> lock(mutex_);
    cache_.clear();
    dirtyRects_.clear();
    packX_ = 0;
    packY_ = 0;
    rowHeight_ = 0;
    std::memset(atlasPixels_.data(), 0, atlasPixels_.size());
}

bool GlyphAtlas::PackGlyph(uint32_t w, uint32_t h, uint32_t& outX, uint32_t& outY)
{
    // 1-pixel padding to prevent texture bleeding
    uint32_t pw = w + 1;
    uint32_t ph = h + 1;

    // Try to fit in current row
    if (packX_ + pw <= kAtlasWidth)
    {
        outX = packX_;
        outY = packY_;
        packX_ += pw;
        rowHeight_ = std::max(rowHeight_, ph);
        return true;
    }

    // Move to next row
    packX_ = 0;
    packY_ += rowHeight_;
    rowHeight_ = 0;

    if (packY_ + ph > kAtlasHeight)
    {
        // Atlas is full
        return false;
    }

    outX = packX_;
    outY = packY_;
    packX_ += pw;
    rowHeight_ = ph;
    return true;
}

void GlyphAtlas::BlitToAtlas(uint32_t x, uint32_t y, uint32_t w, uint32_t h,
                              const uint8_t* rgba)
{
    for (uint32_t row = 0; row < h; row++)
    {
        uint32_t dstOffset = ((y + row) * kAtlasWidth + x) * kAtlasBytesPerPixel;
        uint32_t srcOffset = row * w * kAtlasBytesPerPixel;
        std::memcpy(atlasPixels_.data() + dstOffset, rgba + srcOffset,
                    w * kAtlasBytesPerPixel);
    }

    // Track dirty rect
    dirtyRects_.push_back({x, y, w, h});
}

} // namespace jalium
