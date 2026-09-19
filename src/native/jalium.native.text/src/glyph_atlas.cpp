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
    FontFace* face,
    uint64_t fontId,
    uint16_t glyphIndex,
    uint16_t fontSizePx,
    uint8_t subpixelX,
    GlyphAntialiasMode antialiasMode)
{
    std::lock_guard<std::mutex> lock(mutex_);

    // Check cache
    AtlasGlyphKey key{fontId, glyphIndex, fontSizePx, subpixelX,
                      static_cast<uint8_t>(antialiasMode)};
    auto it = cache_.find(key);
    if (it != cache_.end())
        return it->second;

    // Rasterize the glyph
    RasterizedGlyph rasterized = rasterizer.Rasterize(
        face, glyphIndex, static_cast<float>(fontSizePx), subpixelX, antialiasMode);

    AtlasGlyphEntry entry{};
    entry.bearingX = static_cast<int16_t>(rasterized.bearingX);
    entry.bearingY = static_cast<int16_t>(rasterized.bearingY);
    if (rasterized.hasSubpixel) entry.flags |= ATLAS_GLYPH_LCD;
    if (rasterized.isColor) entry.flags |= ATLAS_GLYPH_COLOR;

    if (rasterized.width > 0 && rasterized.height > 0)
    {
        // A glyph that cannot fit even into an EMPTY atlas must never trigger
        // the reset-and-retry below: the reset would evict every cached glyph,
        // the retry would still fail, and the uncached miss would repeat that
        // on every call — one oversized glyph corrupting/starving all other
        // text every frame. Cache it as invalid instead so it costs one
        // rasterization per generation and leaves the atlas untouched.
        if (rasterized.width + 1 > kAtlasWidth || rasterized.height + 1 > kAtlasHeight)
        {
            auto [oversizedIt, _] = cache_.emplace(key, entry);
            return oversizedIt->second;
        }

        uint32_t outX, outY;
        if (!PackGlyph(rasterized.width, rasterized.height, outX, outY))
        {
            // Atlas is full. The old behaviour returned kInvalidEntry WITHOUT
            // caching, so every later call re-rasterized the same glyph and
            // still drew nothing — a long session that had touched enough
            // distinct glyphs (CJK text, many sizes, 8 sub-pixel buckets)
            // degraded into permanent per-call rasterization churn with
            // missing glyphs. Reset the atlas (new generation) and retry once;
            // consumers re-generate any run that straddled the reset.
            ClearLocked();
            if (!PackGlyph(rasterized.width, rasterized.height, outX, outY))
            {
                // A single glyph larger than the whole atlas — genuinely
                // unrepresentable.
                return kInvalidEntry;
            }
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
    ClearLocked();
}

void GlyphAtlas::ClearLocked()
{
    cache_.clear();
    dirtyRects_.clear();
    packX_ = 0;
    packY_ = 0;
    rowHeight_ = 0;
    std::memset(atlasPixels_.data(), 0, atlasPixels_.size());
    generation_.fetch_add(1, std::memory_order_acq_rel);
}

bool GlyphAtlas::PackGlyph(uint32_t w, uint32_t h, uint32_t& outX, uint32_t& outY)
{
    // 1-pixel padding to prevent texture bleeding
    uint32_t pw = w + 1;
    uint32_t ph = h + 1;

    // Can never fit, not even in an empty atlas. Without this, a glyph wider
    // than the atlas passed the next-row branch (which only checks height)
    // and BlitToAtlas wrote past the row end — wrapping into other glyphs'
    // rows and, on the last rows, past the buffer itself.
    if (pw > kAtlasWidth || ph > kAtlasHeight)
        return false;

    // Try to fit in current row. The height check was missing here: a tall
    // glyph placed near the bottom blitted out of the atlas buffer.
    if (packX_ + pw <= kAtlasWidth && packY_ + ph <= kAtlasHeight)
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
    // Defensive clamp: PackGlyph guarantees in-bounds placement, but an
    // out-of-range rect must never scribble over other rows / past the buffer.
    if (x >= kAtlasWidth || y >= kAtlasHeight)
        return;
    const uint32_t copyW = std::min(w, kAtlasWidth - x);
    const uint32_t copyH = std::min(h, kAtlasHeight - y);

    for (uint32_t row = 0; row < copyH; row++)
    {
        size_t dstOffset = (static_cast<size_t>(y + row) * kAtlasWidth + x) * kAtlasBytesPerPixel;
        size_t srcOffset = static_cast<size_t>(row) * w * kAtlasBytesPerPixel;
        std::memcpy(atlasPixels_.data() + dstOffset, rgba + srcOffset,
                    copyW * kAtlasBytesPerPixel);
    }

    // Track dirty rect
    dirtyRects_.push_back({x, y, copyW, copyH});
}

} // namespace jalium
