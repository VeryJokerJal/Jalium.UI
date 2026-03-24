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
    uint16_t fontSize;   // quantized (rounded to nearest 2px for cache efficiency)

    bool operator==(const GlyphKey& other) const {
        return fontFace == other.fontFace &&
               glyphIndex == other.glyphIndex &&
               fontSize == other.fontSize;
    }
};

struct GlyphKeyHash {
    size_t operator()(const GlyphKey& k) const {
        size_t h = std::hash<void*>{}(k.fontFace);
        h ^= std::hash<uint32_t>{}((uint32_t)k.glyphIndex << 16 | k.fontSize) + 0x9e3779b9 + (h << 6) + (h >> 2);
        return h;
    }
};

// ============================================================================
// D3D12 Glyph Atlas
//
// Manages a 4096x4096 R8 texture atlas for glyph rendering.
// Uses DirectWrite for text layout and glyph rasterization (CPU),
// and D3D12 for rendering glyph quads (GPU).
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

    /// Gets atlas dimensions.
    uint32_t GetWidth() const { return kAtlasWidth; }
    uint32_t GetHeight() const { return kAtlasHeight; }

    /// Sets the DPI scale factor for glyph rasterization.
    /// Default is 1.0 (96 DPI). Set to dpi/96.0 for high-DPI displays.
    void SetDpiScale(float dpiScale) { dpiScale_ = dpiScale > 0 ? dpiScale : 1.0f; }
    float GetDpiScale() const { return dpiScale_; }

    /// Resets the atlas cache (e.g., when DPI changes or atlas is full).
    void Reset();

    /// Returns true if the atlas overflowed and needs a reset at frame boundary.
    bool NeedsReset() const { return needsReset_; }
    void ClearResetFlag() { needsReset_ = false; }

private:
    bool RasterizeGlyph(const GlyphKey& key, GlyphEntry& entry);
    bool AllocateAtlasRect(uint16_t w, uint16_t h, uint16_t& outX, uint16_t& outY);

    ID3D12Device* device_;
    IDWriteFactory* dwriteFactory_;

    // Atlas texture
    static constexpr uint32_t kAtlasWidth = 4096;
    static constexpr uint32_t kAtlasHeight = 4096;
    ComPtr<ID3D12Resource> atlasTexture_;
    ComPtr<ID3D12Resource> uploadBuffer_;

    // CPU-side atlas bitmap (R8)
    std::vector<uint8_t> atlasBitmap_;

    // Glyph cache
    std::unordered_map<GlyphKey, GlyphEntry, GlyphKeyHash> cache_;

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

    // DirectWrite bitmap render target (for CPU rasterization)
    ComPtr<IDWriteBitmapRenderTarget> bitmapRenderTarget_;
    ComPtr<IDWriteRenderingParams> renderingParams_;

    bool initialized_ = false;
    bool needsReset_ = false;  // Atlas overflow — reset at next frame start
    float dpiScale_ = 1.0f;
};

} // namespace jalium
