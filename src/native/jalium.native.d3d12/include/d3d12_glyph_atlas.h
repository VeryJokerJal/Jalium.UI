#pragma once

#include "d3d12_backend.h"
#include <dwrite_3.h>
#include <unordered_map>
#include <vector>
#include <list>
#include <cstdint>

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
    // Color emoji glyphs (COLR/CPAL fonts like Segoe UI Emoji) are rasterized
    // with their authored colours baked into the atlas RGBA. The text shader
    // detects this flag via a negative-R sentinel on the emitted instance
    // and routes them through a SrcOver alpha path instead of the per-channel
    // ClearType dual-source blend, so the emoji renders in its own colours
    // rather than being tinted by the text Foreground.
    bool isColor = false;
};

// ============================================================================
// Key for glyph cache lookup
// ============================================================================

// Quantization granularity for the per-axis transform scale baked into GlyphKey
// (scaleXQ/scaleYQ = round(scale * kGlyphScaleQuant); the value kGlyphScaleQuant
// itself == 1.0x == identity). Finer = smoother thin strokes during an animated
// deform: the bitmap is rasterized at the bucket scale but DISPLAYED at the
// actual (continuous) scale, so the in-bucket residual (±0.5/bucket) point-scales
// the stem and makes it shimmer thicker/thinner as the drag drifts within a
// bucket. 1/16 halves that residual vs 1/8. Cost: more cached deformation buckets.
// uint8 cap => max scale kGlyphScaleQuant-relative is 255/16 ≈ 15.9x (ample).
static constexpr int kGlyphScaleQuant = 16;

struct GlyphKey {
    IDWriteFontFace* fontFace;
    uint16_t glyphIndex;
    uint16_t fontSize;      // BASE physical pixel size = round(fontSize*dpi), NO transform scale
    uint8_t  subpixelX;     // sub-pixel X offset quantized to 1/8 pixel (0..7)
    // Per-axis transform scale, quantized to 1/kGlyphScaleQuant steps (value =
    // round(scale*kGlyphScaleQuant); kGlyphScaleQuant == 1.0x == unscaled). The
    // glyph is rasterized at the FINAL deformed pixel size via a
    // DWRITE_MATRIX(scaleX,0,0,scaleY) so e.g. a liquid-glass squeeze
    // (scaleX≈0.63, scaleY≈2.17) produces an already-compressed crisp bitmap —
    // instead of point-minifying an isotropic atlas (which dropped thin stems:
    // d→c, r→l). Both == kGlyphScaleQuant means identity == normal text path.
    uint8_t  scaleXQ = (uint8_t)kGlyphScaleQuant;
    uint8_t  scaleYQ = (uint8_t)kGlyphScaleQuant;
    // Per-glyph rendering mode resolved from the source TextFormat's
    // TextRenderingMode / TextHintingMode. Cached separately for each combo
    // because the rasterized bitmap differs:
    //   - aaMode picks 1- vs 3-bytes-per-pixel coverage and which DWrite
    //     antialias mode runs (Aliased / Grayscale / ClearType — 0 stays
    //     reserved for "fall back to SyncAntialiasMode" only at the caller).
    //   - hintingMode toggles DWRITE_GRID_FIT_MODE_ENABLED/DISABLED so the
    //     same glyph rasterizes differently when WPF's TextOptions.TextHintingMode
    //     is Animated (no grid fit, smoother sub-pixel animation) vs Fixed.
    // Without these fields the cache would hand back the wrong bitmap on the
    // very next glyph after a per-format mode switch.
    uint8_t  aaMode;
    uint8_t  hintingMode;

    bool operator==(const GlyphKey& other) const {
        return fontFace == other.fontFace &&
               glyphIndex == other.glyphIndex &&
               fontSize == other.fontSize &&
               subpixelX == other.subpixelX &&
               aaMode == other.aaMode &&
               hintingMode == other.hintingMode &&
               scaleXQ == other.scaleXQ &&
               scaleYQ == other.scaleYQ;
    }
};

struct GlyphKeyHash {
    size_t operator()(const GlyphKey& k) const {
        size_t h = std::hash<void*>{}(k.fontFace);
        // Pack every per-glyph field into a 40-bit slot in one uint64 so each
        // tuple gets a distinct hash input. Layout (LSB → MSB):
        //   [ 0.. 2] subpixelX  (3 bits — 8 sub-pixel buckets, 1/8 px each)
        //   [ 3.. 5] aaMode     (3 bits — 0..3 today)
        //   [ 6.. 8] hintingMode(3 bits — 0..2 today)
        //   [ 9..24] fontSize   (16 bits — full uint16 range)
        //   [25..40] glyphIndex (16 bits — full uint16 range)
        // No fields overlap, so two different keys hash through different
        // packed words (the secondary collision risk is the std::hash
        // distribution itself, which is independent of our packing).
        uint64_t packed = ((uint64_t)(k.subpixelX & 0x7))
                        | ((uint64_t)(k.aaMode    & 0x7) << 3)
                        | ((uint64_t)(k.hintingMode & 0x7) << 6)
                        | ((uint64_t)k.fontSize   << 9)
                        | ((uint64_t)k.glyphIndex << 25)
                        | ((uint64_t)k.scaleXQ    << 41)   // per-axis transform scale
                        | ((uint64_t)k.scaleYQ    << 49);
        h ^= std::hash<uint64_t>{}(packed) + 0x9e3779b9 + (h << 6) + (h >> 2);
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
    explicit D3D12GlyphAtlas(ID3D12Device* device, IDWriteFactory* dwriteFactory, D3D12Backend* backend = nullptr);
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
    /// `layoutKey` (0 = uncacheable) is a stable content hash of the source
    /// text + format + constraints (from D3D12TextFormat::CreateLayout). When
    /// non-zero the resolved glyph quads + decorations are memoized per
    /// (layoutKey, dpiScale, aaMode, hintingMode) so repeat frames skip
    /// layout->Draw + the per-glyph atlas walk — the dominant DrawText cost
    /// once the layout itself is cached. Entries are tagged with the atlas
    /// generation and ignored after any Reset()/GrowAtlas() so stale atlas
    /// UVs are never served.
    ///
    /// `aaMode` (JALIUM_TEXT_AA_*) and `hintingMode` (0=Auto, 1=Fixed,
    /// 2=Animated) come from the source TextFormat's per-element TextOptions
    /// values, already resolved against the process-wide override by
    /// TextFormat::ResolveEffectiveTextRenderingMode. Pass 0 / 0 to take
    /// the historical "process-wide only" behaviour, which still re-resolves
    /// against the global setting on every call (since aaMode=0 / Auto is the
    /// signal that nobody asked for an explicit override).
    ///
    /// `rasterScale` (>= 1.0, default 1.0) is the effective magnification the
    /// caller's transform will apply to this text (max of the transform's two
    /// axis scales, quantized + capped by the caller). The glyph is rasterized
    /// at fontSize * dpiScale * rasterScale so a magnified glyph samples a
    /// high-resolution atlas bitmap instead of a stretched base-size one (which
    /// is what made scaled text mosaic). The emitted quad geometry is divided
    /// back down to BASE-DIP space, so the caller's existing post-transform
    /// quad scaling reproduces the correct on-screen size. rasterScale is part
    /// of the per-layout memo key (it changes the atlas slots / UVs).
    uint32_t GenerateGlyphs(
        IDWriteTextLayout* layout,
        float originX, float originY,
        float colorR, float colorG, float colorB, float colorA,
        std::vector<GlyphQuadInstance>& outInstances,
        std::vector<TextDecorationRect>* outDecorations = nullptr,
        uint64_t layoutKey = 0,
        int32_t aaMode = 0,
        int32_t hintingMode = 0,
        float scaleX = 1.0f,
        float scaleY = 1.0f,
        // Axis-aligned scale (no rotation/skew) requests the CRISP path: the pen
        // is floored to the integer pixel grid (baking the sub-pixel phase into
        // the bitmap the same way identity text does) so the caller can integer-
        // snap the post-scaled quad and sample it with the POINT PSO for pixel-
        // exact stems. Rotated/skewed text leaves this false and keeps the
        // continuous pen + bilinear PSO so it animates without per-glyph jitter.
        bool crispAxisAligned = false);

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
    D3D12Backend* backend_;  // optional: when set, GrowAtlas retires old atlas/upload buffers through the backend's fence-tracked graveyard instead of dropping them via ComPtr operator= (which triggers D3D12 ERROR #921).

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

    // Atlas generation: bumped on every Reset()/GrowAtlas() (anything that
    // moves slots or changes atlas dimensions, invalidating cached UVs).
    // The resolved-glyph cache tags entries with the generation they were
    // built under and treats a mismatch as a miss — content-addressed key
    // (no raw pointers) + generation guard makes stale-UV garbled text
    // impossible by construction.
    uint32_t atlasGeneration_ = 0;

    // Resolved-glyph memo: colour-neutral, origin-relative quads + decorations
    // for a shaped run keyed by layoutKey alone (no origin, no colour). emit
    // applies the caller's premultiplied colour AND translates each quad by
    // the screen-space origin, so the same shaped run hits across every
    // origin it appears at (scrolling, drag, animation) and across every
    // foreground colour.
    struct CachedGlyphRun {
        std::vector<GlyphQuadInstance> instances;  // posX/posY = layout-local; colour unset
        std::vector<TextDecorationRect> decos;     // x/y = layout-local; colour unset
        uint32_t gen = 0;
    };
    struct InstNode { uint64_t key; CachedGlyphRun run; };
    std::list<InstNode> instLru_;
    std::unordered_map<uint64_t, std::list<InstNode>::iterator> instMap_;
    static constexpr size_t kInstCacheCap = 4096;

    static uint64_t HashInstanceKey(uint64_t layoutKey,
                                    float dpiScale,
                                    int32_t aaMode,
                                    int32_t hintingMode,
                                    float scaleX, float scaleY,
                                    bool crispAxisAligned) noexcept;

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
    ComPtr<IDWriteFactory4> dwriteFactory4_;  // cached QI for TranslateColorGlyphRun (color emoji)
    ComPtr<IDWriteBitmapRenderTarget> bitmapRenderTarget_;  // fallback rasterizer
    ComPtr<IDWriteRenderingParams> renderingParams_;

    // Rasterizes a single colour-emoji glyph (COLR / CPAL font) by walking
    // every layer that TranslateColorGlyphRun reports for the run, taking the
    // grayscale alpha mask of each layer and accumulating
    // <c>colour × coverage</c> (pre-multiplied) into the atlas RGBA cell.
    // Returns true when the entry was filled, false to fall back to the
    // mono-coverage path. Sets <c>entry.isColor = true</c> on success.
    bool RasterizeColorGlyph(const GlyphKey& key, GlyphEntry& entry);

    bool initialized_ = false;
    bool needsReset_ = false;  // Atlas at max dim and overflowed — reset next frame
    bool needsGrow_ = false;   // Atlas can still grow — recreate resources next frame
    uint32_t pendingGrowW_ = 0;  // largest reqW seen this frame (px)
    uint32_t pendingGrowH_ = 0;  // largest reqH seen this frame (px)
    float dpiScale_ = 1.0f;

    // Text antialias mode (cached snapshot of jalium_text_get_global_antialias_mode).
    // Bumped to mark the cached glyph bitmaps invalid when the application
    // switches between ClearType and Grayscale at runtime — we re-rasterize on
    // demand rather than rasterizing both up front.
    uint64_t lastAntialiasGen_ = 0;
    int32_t  currentAntialiasMode_ = 3;  // resolved (Auto → CT on Windows)

    // Reads jalium_text_get_global_antialias_mode and resets the cache if the
    // mode changed since the previous call. Returns the resolved (concrete)
    // mode to use for this rasterization pass.
    int32_t SyncAntialiasMode();
};

} // namespace jalium
