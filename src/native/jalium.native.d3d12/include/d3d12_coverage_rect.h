#pragma once

#include "jalium_scanline_rasterizer.h"
#include <algorithm>
#include <cstdint>
#include <limits>
#include <memory>
#include <vector>

namespace jalium {

// Analytic rasterizer output with its final premultiplied color. Coordinates
// are physical pixels; neither the current transform nor opacity applies again.
struct D3D12CoverageRect {
    float left, top, right, bottom;
    float r, g, b, a;
};
static_assert(sizeof(D3D12CoverageRect) == 32);

// Origin-relative analytic coverage shared by the stroke cache and pending
// batches. A pending draw keeps the raster data alive even after LRU eviction.
struct D3D12CoverageMask {
    std::vector<PixelRect> rects;
    int64_t left = 0, top = 0, right = 0, bottom = 0;
    bool exactBounds = false;

    void Reset() { rects.clear(); exactBounds = false; }

    void UpdateBounds() {
        exactBounds = !rects.empty();
        left = top = (std::numeric_limits<int64_t>::max)();
        right = bottom = (std::numeric_limits<int64_t>::min)();
        constexpr int64_t limit = int64_t{1} << 24;
        for (const auto& rect : rects) {
            const int64_t x1 = int64_t(rect.x) + rect.w;
            const int64_t y1 = int64_t(rect.y) + rect.h;
            left = (std::min)(left, int64_t(rect.x));
            top = (std::min)(top, int64_t(rect.y));
            right = (std::max)(right, x1);
            bottom = (std::max)(bottom, y1);
            exactBounds &= rect.x >= -limit && rect.x <= limit && rect.y >= -limit && rect.y <= limit
                && rect.w >= 0 && rect.w <= limit && rect.h >= 0 && rect.h <= limit
                && x1 >= -limit && x1 <= limit && y1 >= -limit && y1 <= limit;
        }
    }

    void TranslatedBounds(int sx, int sy, float& minX, float& minY, float& maxX, float& maxY) const {
        constexpr int64_t limit = int64_t{1} << 24;
        if (exactBounds && sx >= -limit && sx <= limit && sy >= -limit && sy <= limit
            && left + sx >= -limit && right + sx <= limit && top + sy >= -limit && bottom + sy <= limit) {
            minX = float(left) + float(sx); minY = float(top) + float(sy);
            maxX = float(right) + float(sx); maxY = float(bottom) + float(sy);
            return;
        }
        // Preserve the old per-rectangle operation order when float additions
        // need rounding; (x + offset) + width is not generally (x + width) + offset.
        minX = minY = std::numeric_limits<float>::infinity();
        maxX = maxY = -std::numeric_limits<float>::infinity();
        for (const auto& rect : rects) {
            const float x0 = float(rect.x) + float(sx), y0 = float(rect.y) + float(sy);
            const float x1 = x0 + float(rect.w), y1 = y0 + float(rect.h);
            if (x0 < minX) minX = x0;
            if (y0 < minY) minY = y0;
            if (x1 > maxX) maxX = x1;
            if (y1 > maxY) maxY = y1;
        }
    }
};

struct D3D12CoverageMaskRun {
    std::shared_ptr<const D3D12CoverageMask> mask;
    float offsetX, offsetY;
    float r, g, b, a; // premultiplied brush, before per-rectangle coverage
};

// Solid ellipse payload. Analytic coverage boxes need only the first 32 bytes.
struct CoverageRectInstance {
    float posX, posY, sizeX, sizeY;
    float fillR, fillG, fillB, fillA;
    float xfM11, xfM12, xfM21, xfM22;
    float xfDx, xfDy, exponent, opacity;
};
static_assert(sizeof(CoverageRectInstance) == 64, "CoverageRectInstance must be 64 bytes");

// Variable-sized records preserve painter order without uploading unused
// transforms for analytic coverage boxes. Bit 0 marks a 64-byte ellipse record.
class CoverageInstanceStream {
public:
    size_t size() const { return offsets_.size(); }
    bool empty() const { return offsets_.empty(); }
    size_t data_size_bytes() const { return blocks_.size() * sizeof(Block); }
    const void* data() const { return blocks_.data(); }
    const uint32_t* offsets() const { return offsets_.data(); }
    void clear() { blocks_.clear(); offsets_.clear(); }
    void Release() {
        std::vector<Block>().swap(blocks_);
        std::vector<uint32_t>().swap(offsets_);
    }
    void AppendRects(const D3D12CoverageRect* rects, uint32_t count, float scale) {
        Reserve((size_t)count * 2, count);
        for (uint32_t i = 0; i < count; ++i) {
            const auto& rect = rects[i];
            offsets_.push_back((uint32_t)data_size_bytes());
            blocks_.push_back({ rect.left * scale, rect.top * scale,
                (rect.right - rect.left) * scale, (rect.bottom - rect.top) * scale });
            blocks_.push_back({ rect.r, rect.g, rect.b, rect.a });
        }
    }
    void AppendMaskRects(const D3D12CoverageMaskRun& run, uint32_t first, uint32_t count, float scale) {
        Reserve((size_t)count * 2, count);
        const auto* rects = run.mask->rects.data() + first;
        for (uint32_t i = 0; i < count; ++i) {
            const auto& rect = rects[i];
            const float x0 = float(rect.x) + run.offsetX, y0 = float(rect.y) + run.offsetY;
            const float x1 = x0 + float(rect.w), y1 = y0 + float(rect.h);
            offsets_.push_back((uint32_t)data_size_bytes());
            blocks_.push_back({ x0 * scale, y0 * scale, (x1 - x0) * scale, (y1 - y0) * scale });
            blocks_.push_back({ run.r * rect.alpha, run.g * rect.alpha, run.b * rect.alpha, run.a * rect.alpha });
        }
    }
    void AppendEllipse(const CoverageRectInstance& ellipse) {
        Reserve(4, 1);
        offsets_.push_back((uint32_t)data_size_bytes() | 1u);
        blocks_.push_back({ ellipse.posX, ellipse.posY, ellipse.sizeX, ellipse.sizeY });
        blocks_.push_back({ ellipse.fillR, ellipse.fillG, ellipse.fillB, ellipse.fillA });
        blocks_.push_back({ ellipse.xfM11, ellipse.xfM12, ellipse.xfM21, ellipse.xfM22 });
        blocks_.push_back({ ellipse.xfDx, ellipse.xfDy, ellipse.exponent, ellipse.opacity });
    }
private:
    struct Block { float x, y, z, w; };
    static_assert(sizeof(Block) == 16);
    void Reserve(size_t blocks, size_t indices) {
        const size_t requiredBlocks = blocks_.size() + blocks;
        const size_t requiredIndices = offsets_.size() + indices;
        if (requiredBlocks > blocks_.capacity())
            blocks_.reserve((std::max)(requiredBlocks, blocks_.capacity() * 2));
        if (requiredIndices > offsets_.capacity())
            offsets_.reserve((std::max)(requiredIndices, offsets_.capacity() * 2));
    }
    std::vector<Block> blocks_;
    std::vector<uint32_t> offsets_;
};

} // namespace jalium
