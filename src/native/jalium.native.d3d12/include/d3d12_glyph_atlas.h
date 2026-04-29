#pragma once

#include "d3d12_backend.h"
#include <dwrite_3.h>
#include <unordered_map>
#include <vector>

namespace jalium {

// ============================================================================
// Glyph instance for text shader (48 bytes)
// ============================================================================

struct GlyphQuadInstance {
    float posX, posY;       // screen position
    float sizeX, sizeY;     // quad size
    float uvMinX, uvMinY;   // atlas UV top-left
    float uvMaxX, uvMaxY;   // atlas UV bottom-right
    float colorR, colorG, colorB, colorA; // premultiplied RGBA
};
static_assert(sizeof(GlyphQuadInstance) == 48, "GlyphQuadInstance must be 48 bytes");

// ============================================================================
// Glyph Atlas entry — cached position in the atlas texture
// ============================================================================

struct GlyphEntry {
    uint16_t x, y;      // position in atlas (pixels)
    uint16_t w, h;      // glyph size (pixels)
    int16_t  bearingX;   // horizontal offset from pen position
    int16_t  bearingY;   // vertical offset from baseline
    bool valid;
};

// ============================================================================
// Key for glyph cache lookup
// ============================================================================

struct GlyphKey {
    IDWriteFontFace* fontFace;
    uint16_t glyphIndex;
    uint16_t fontSize;      // physical pixel size (rounded, no further quantization)
    uint8_t  subpixelX;     // sub-pixel X offset quantized to 1/4 pixel (0..3)

    bool operator==(const GlyphKey& other) const {
        return fontFace == other.fontFace &&
               glyphIndex == other.glyphIndex &&
               fontSize == other.fontSize &&
               subpixelX == other.subpixelX;
    }
};

struct GlyphKeyHash {
    size_t operator()(const GlyphKey& k) const {
        size_t h = std::hash<void*>{}(k.fontFace);
        h ^= std::hash<uint32_t>{}(((uint32_t)k.glyphIndex << 16) | ((uint32_t)k.fontSize << 2) | k.subpixelX) + 0x9e3779b9 + (h << 6) + (h >> 2);
        return h;
    }
};

/// Cache value: glyph atlas entry + ref-counted font face.
/// Holding a ComPtr keeps the IDWriteFontFace alive so that the raw pointer
/// in GlyphKey remains valid and cannot be recycled for a different font.
struct GlyphCacheValue {
    GlyphEntry entry;
    ComPtr<IDWriteFontFace> fontFaceRef;  // prevents dangling GlyphKey::fontFace
};

// ============================================================================
// D3D12 Glyph Atlas
//
// Manages a 4096x4096 R8G8B8A8 texture atlas for ClearType sub-pixel text rendering.
// Uses DirectWrite for text layout and glyph rasterization (CPU),
// and D3D12 dual-source blending for per-channel alpha compositing (GPU).
// ============================================================================

class D3D12GlyphAtlas {
public:
    explicit D3D12GlyphAtlas(ID3D12Device* device, IDWriteFactory* dwriteFactory);
    ~D3D12GlyphAtlas();

    bool Initialize();

    /// Text decoration (underline / strikethrough) — rendered as SDF rects
    struct TextDecorationRect {
        float x, y, width, thickness;
        float colorR, colorG, colorB, colorA;
    };

    /// Generates glyph instances for a text layout.
    /// Returns the number of glyph instances added to `outInstances`.
    /// Optionally outputs text decoration rects (underline/strikethrough).
    uint32_t GenerateGlyphs(
        IDWriteTextLayout* layout,
        float originX, float originY,
        float colorR, float colorG, float colorB, float colorA,
        std::vector<GlyphQuadInstance>& outInstances,
        std::vector<TextDecorationRect>* outDecorations = nullptr);

    /// Uploads any pending glyph data to the GPU atlas texture.
    /// Must be called before rendering text in a frame.
    void FlushToGpu(ID3D12GraphicsCommandList* cmdList);

    /// Gets the atlas SRV resource for binding.
    ID3D12Resource* GetAtlasResource() const { return atlasTexture_.Get(); }

    /// Gets atlas dimensions.  These reflect the CURRENT atlas size, which
    /// starts at kInitialAtlasDim and grows ×2 as glyphs spill over the bottom.
    uint32_t GetWidth() const { return atlasW_; }
    uint32_t GetHeight() const { return atlasH_; }

    /// Sets the DPI scale factor for glyph rasterization.
    /// Default is 1.0 (96 DPI). Set to dpi/96.0 for high-DPI displays.
    void SetDpiScale(float dpiScale) { dpiScale_ = dpiScale > 0 ? dpiScale : 1.0f; }
    float GetDpiScale() const { return dpiScale_; }

    /// Resets the atlas cache (e.g., when DPI changes or atlas is full).
    void Reset();

    /// Returns true if the atlas overflowed and needs a reset at frame boundary.
    bool NeedsReset() const { return needsReset_; }
    void ClearResetFlag() { needsReset_ = false; }

    /// Marks the atlas to be reset on the next BeginFrame, before any glyph
    /// SRV is bound to the new frame's command list. Used by the idle-resource
    /// reclaimer when the application has been quiet long enough that the
    /// cache should be dropped to release GPU memory. Safe to call from any
    /// thread that doesn't hold the render-target's command list, including
    /// mid-frame — the actual GPU work happens later, inside
    /// ApplyPendingGrowthOrReset on the next BeginFrame.
    void RequestResetAtFrameBoundary() { needsReset_ = true; }

    /// Returns true if AllocateAtlasRect ran out of space *and* the atlas can
    /// still grow before hitting kMaxAtlasDim.  Caller (D3D12DirectRenderer
    /// BeginFrame) should call ApplyPendingGrowthOrReset at the frame
    /// boundary, which performs the actual GPU resource recreation.
    bool NeedsGrow() const { return needsGrow_; }

    /// Frame-boundary entry point: if the previous frame requested growth, do it
    /// now (preserving cached glyph pixels); if the previous frame requested a
    /// reset because growth wasn't possible, perform the reset.  Safe to call
    /// before any glyph SRV is bound onto the new frame's command list.
    void ApplyPendingGrowthOrReset();

    // ── Diagnostics accessors (used by DevTools Perf tab via RenderTarget::QueryGpuStats) ──

    /// Number of glyph entries currently resident in the cache.
    int32_t GetCacheEntryCount() const {
        return static_cast<int32_t>(cache_.size());
    }

    /// Approximate slot capacity at average glyph size (16×16). Purely display-
    /// side — the packer itself has no slot grid.
    int32_t GetEstimatedCapacity() const {
        return (atlasW_ * atlasH_) / (16 * 16);
    }

    /// Bytes of atlas texture memory currently packed (approximate: rows packed
    /// + current row partial column).
    int64_t GetPackedBytes() const {
        int64_t wholeRows = static_cast<int64_t>(packY_) * atlasW_;
        int64_t currentRowPartial = static_cast<int64_t>(packX_) * rowHeight_;
        return (wholeRows + currentRowPartial) * 4;  // RGBA8
    }

    /// Total GPU bytes reserved for the atlas texture at its CURRENT size.
    int64_t GetTotalBytes() const {
        return static_cast<int64_t>(atlasW_) * atlasH_ * 4;
    }

private:
    bool RasterizeGlyph(const GlyphKey& key, GlyphEntry& entry);
    bool AllocateAtlasRect(uint16_t w, uint16_t h, uint16_t& outX, uint16_t& outY);
    // Grow atlasTexture_/uploadBuffer_/atlasBitmap_ to at least (reqW, reqH),
    // capped at kMaxAtlasDim.  Preserves all already-packed glyph pixels.
    // Safe to call only between frames or during batch-collect (before any
    // SRV referencing atlasTexture_ has been recorded onto the open command
    // list), so AllocateAtlasRect is the natural caller.
    bool GrowAtlas(uint32_t reqW, uint32_t reqH);

    ID3D12Device* device_;
    IDWriteFactory* dwriteFactory_;

    // Atlas texture — starts at kInitialAtlasDim and grows ×2 up to kMaxAtlasDim
    // on overflow.  Sized lazily so an idle UI keeps a 1 MB atlas instead of 64 MB.
    static constexpr uint32_t kInitialAtlasDim = 512;
    static constexpr uint32_t kMaxAtlasDim = 4096;
    uint32_t atlasW_ = kInitialAtlasDim;
    uint32_t atlasH_ = kInitialAtlasDim;
    ComPtr<ID3D12Resource> atlasTexture_;
    ComPtr<ID3D12Resource> uploadBuffer_;

    // CPU-side atlas bitmap (RGBA — R,G,B = sub-pixel coverage, A = max coverage)
    std::vector<uint8_t> atlasBitmap_;

    // Glyph cache (GlyphCacheValue holds a ComPtr to prevent dangling fontFace pointers)
    std::unordered_map<GlyphKey, GlyphCacheValue, GlyphKeyHash> cache_;

    // Simple row-based atlas packer
    uint16_t packX_ = 0;
    uint16_t packY_ = 0;
    uint16_t rowHeight_ = 0;

    // Dirty tracking for upload
    bool dirty_ = false;
    uint16_t dirtyMinY_ = UINT16_MAX;
    uint16_t dirtyMaxY_ = 0;

    // Current resource state for barrier tracking
    D3D12_RESOURCE_STATES atlasState_ = D3D12_RESOURCE_STATE_COMMON;

    // DirectWrite rasterization
    ComPtr<IDWriteFactory3> dwriteFactory3_;  // cached QI for CreateGlyphRunAnalysis
    ComPtr<IDWriteBitmapRenderTarget> bitmapRenderTarget_;  // fallback rasterizer
    ComPtr<IDWriteRenderingParams> renderingParams_;

    bool initialized_ = false;
    bool needsReset_ = false;  // Atlas at max dim and overflowed — reset next frame
    bool needsGrow_ = false;   // Atlas can still grow — recreate resources next frame
    uint32_t pendingGrowW_ = 0;  // largest reqW seen this frame (px)
    uint32_t pendingGrowH_ = 0;  // largest reqH seen this frame (px)
    float dpiScale_ = 1.0f;
};

} // namespace jalium
