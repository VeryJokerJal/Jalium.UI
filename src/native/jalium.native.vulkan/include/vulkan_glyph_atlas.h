#pragma once

#ifdef _WIN32

// ============================================================================
// VulkanGlyphAtlas — CPU-only DirectWrite glyph atlas (Windows only)
//
// Ports the CPU half of D3D12GlyphAtlas (DirectWrite rasterization + all
// caching + the CPU RGBA atlas bitmap + glyph-instance generation). It contains
// ZERO Vulkan GPU code: no VkImage/VkBuffer/vk* calls. A later batch (B4) owns
// the VkImage and uploads this class's CPU bitmap via the render target's
// dynamically-loaded Vulkan function pointers (the Vulkan backend resolves every
// device function as a member function pointer through vkGetDeviceProcAddr in
// vulkan_render_target.cpp, so a standalone class cannot call vkCreateImage
// directly). The B4 uploader consumes the public GetAtlasBitmap / GetWidth /
// GetHeight / GetGeneration / TakeDirtyBand / ConsumeAtlasRecreated accessors.
//
// Manages an R8G8B8A8 atlas bitmap for ClearType sub-pixel text: R,G,B = per-
// channel sub-pixel coverage, A = max coverage. Starts at kInitialAtlasDim and
// grows ×2 up to kMaxAtlasDim on overflow (CPU resize only).
// ============================================================================

#include <dwrite_3.h>
#include <wrl/client.h>
#include <unordered_map>
#include <vector>
#include <list>
#include <cstdint>

namespace jalium {

// ============================================================================
// Glyph instance for text shader (64 bytes)
// ============================================================================

struct VkGlyphInstance {
    float posX, posY;       // screen position
    float sizeX, sizeY;     // basis diagonal: X-axis.x, Y-axis.y
    float skewX, skewY;     // basis off-diagonal: Y-axis.x, X-axis.y
    float uvMinX, uvMinY;   // atlas UV top-left
    float uvMaxX, uvMaxY;   // atlas UV bottom-right
    float padX, padY;       // align float4 color to SPIR-V/std430 offset 48
    float colorR, colorG, colorB, colorA; // premultiplied RGBA
};
static_assert(sizeof(VkGlyphInstance) == 64, "VkGlyphInstance must be 64 bytes");

// ============================================================================
// Glyph Atlas entry — cached position in the atlas bitmap
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
    // Upscale GenerateGlyphs applies to w/h/bearings at quad emission. 1.0 for
    // normal glyphs; > 1.0 when RasterizeGlyph had to shrink the em because the
    // ink box exceeded kMaxGlyphBitmapDim (huge-ink fonts, capped-ppem emoji) —
    // the glyph renders slightly soft instead of vanishing.
    float scale = 1.0f;
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

// Quantization granularity for the ROTATED / SKEWED 2x2 glyph transform baked
// into GlyphKey (xf**Q = round(m * kGlyphXformQuant)). At 1/256 a unit-scale
// rotation buckets every ~0.22 degrees: fine enough that a static rotation is
// visually exact, coarse enough that a slow rotate animation reuses a bucket
// for several frames instead of rasterizing a fresh strike every frame.
static constexpr int kGlyphXformQuant = 256;

// Upper bound on the FINAL vertical ppem a glyph is rasterized at. Deep-zoomed
// or print-preview text above this renders from a strike rasterized AT the cap
// and upscaled at quad emission — bounded atlas pressure instead of glyphs
// silently vanishing past the old hard 512px ink rejection. Every ppem above
// the cap shares ONE strike per glyph, so continuous zoom stops flooding the
// atlas with per-step size buckets. Mirrors D3D12GlyphAtlas.
static constexpr uint16_t kMaxGlyphRasterPpem = 512;

// Largest ink box RasterizeGlyph accepts into the atlas. Ink beyond it (fonts
// whose ink overshoots the em box, extreme aspect deforms) re-rasterizes at a
// proportionally smaller em, with GlyphEntry::scale carrying the upscale.
static constexpr int kMaxGlyphBitmapDim = 640;

// Above this ppem, sub-pixel phases are pinned to 0: the ≤1/8-px placement
// residual is an invisible fraction of such a glyph's advance, while caching
// up to 8 phase variants of big strikes multiplies atlas load by 8× — a
// zoomed CJK paragraph's variants alone can exceed the 4096² maximum, which
// degenerates into a reset-every-frame loop with per-frame missing glyphs.
static constexpr uint16_t kMaxSubpixelPhasePpem = 96;

// Colour-emoji scratch canvas stays capped at 512px, with the baseline parked
// at 75% from the top. Cap the emoji em low enough that Fluent overshoot ink
// (≈1.1–1.2 em above baseline) still fits: 0.75·512 / 1.2 ≈ 320.
static constexpr uint16_t kMaxColorGlyphPpem = 320;

struct GlyphKey {
    IDWriteFontFace* fontFace;
    uint16_t glyphIndex;
    uint16_t fontSize;      // FINAL vertical ppem (DPI and scaleY already folded in)
    uint8_t  subpixelX;     // sub-pixel X offset quantized to 1/8 pixel (0..7)
    // Quantized per-axis transform buckets. scaleY is folded into fontSize so
    // DirectWrite grid-fits at the final vertical ppem; RasterizeGlyph uses
    // scaleXQ/scaleYQ only as the remaining horizontal aspect ratio.
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
    // Quantized 2x2 glyph transform for a ROTATED / SKEWED run, expressed
    // relative to the vertical scale that is already folded into fontSize (so
    // an unrotated uniform scale would come out as the identity here). All four
    // stay 0 on an axis-aligned run — that path keeps using the scaleXQ:scaleYQ
    // aspect matrix — which makes every pre-existing key byte-identical to what
    // it was before this field existed.
    int16_t  xf11Q = 0;
    int16_t  xf12Q = 0;
    int16_t  xf21Q = 0;
    int16_t  xf22Q = 0;

    /// True when this key carries a real rotation / skew. RasterizeGlyph keeps
    /// the strike upright; GenerateGlyphs uses this 2x2 for the oriented basis.
    bool HasGlyphRotation() const {
        return (xf11Q | xf12Q | xf21Q | xf22Q) != 0;
    }

    bool operator==(const GlyphKey& other) const {
        return fontFace == other.fontFace &&
               glyphIndex == other.glyphIndex &&
               fontSize == other.fontSize &&
               subpixelX == other.subpixelX &&
               aaMode == other.aaMode &&
               hintingMode == other.hintingMode &&
               scaleXQ == other.scaleXQ &&
               scaleYQ == other.scaleYQ &&
               xf11Q == other.xf11Q &&
               xf12Q == other.xf12Q &&
               xf21Q == other.xf21Q &&
               xf22Q == other.xf22Q;
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
        // Rotated / skewed runs pack their quantized 2x2 into a second word.
        // Mixed only when present, so an axis-aligned key hashes exactly where
        // it did before rotation support existed.
        if (k.HasGlyphRotation()) {
            const uint64_t xf = ((uint64_t)(uint16_t)k.xf11Q)
                              | ((uint64_t)(uint16_t)k.xf12Q << 16)
                              | ((uint64_t)(uint16_t)k.xf21Q << 32)
                              | ((uint64_t)(uint16_t)k.xf22Q << 48);
            h ^= std::hash<uint64_t>{}(xf) + 0x9e3779b9 + (h << 6) + (h >> 2);
        }
        return h;
    }
};

/// Cache value: glyph atlas entry + ref-counted font face.
/// Holding a ComPtr keeps the IDWriteFontFace alive so that the raw pointer
/// in GlyphKey remains valid and cannot be recycled for a different font.
struct GlyphCacheValue {
    GlyphEntry entry;
    Microsoft::WRL::ComPtr<IDWriteFontFace> fontFaceRef;  // prevents dangling GlyphKey::fontFace
    // Frame counter of the last GenerateGlyphs that referenced this strike.
    // CompactAtlas keeps only recently-used entries, so the stale size buckets
    // a zoom gesture leaves behind get reclaimed WITHOUT wiping the glyphs the
    // current frame is drawing — which is exactly what a full Reset does, and
    // why zooming used to make text flicker line by line. Mirrors D3D12.
    uint32_t lastUsedFrame = 0;
};

// ============================================================================
// Vulkan Glyph Atlas (CPU-only)
//
// Maintains an R8G8B8A8 CPU atlas bitmap for ClearType sub-pixel text rendering.
// Uses DirectWrite for text layout and glyph rasterization (CPU). It does NOT
// own any GPU resource — the Vulkan VkImage upload is the B4 render-target
// batch's responsibility, which reads the CPU bitmap through the public
// accessors below.
// ============================================================================

class VulkanGlyphAtlas {
public:
    explicit VulkanGlyphAtlas(IDWriteFactory* dwriteFactory);
    ~VulkanGlyphAtlas();

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
    /// text + format + constraints (from VulkanTextFormat::CreateLayout). When
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
    /// `scaleX`/`scaleY` is the per-axis magnification the caller's transform
    /// applies to this text. The final vertical ppem is used as DirectWrite's
    /// em size, while only scaleX/scaleY remains in its glyph transform. This
    /// keeps hinting tied to the final pixel grid and avoids magnifying
    /// per-glyph ink-box differences. Emitted geometry is divided back to
    /// base-DIP space before the caller reapplies the actual transform.
    uint32_t GenerateGlyphs(
        IDWriteTextLayout* layout,
        float originX, float originY,
        float colorR, float colorG, float colorB, float colorA,
        std::vector<VkGlyphInstance>& outInstances,
        std::vector<TextDecorationRect>* outDecorations = nullptr,
        uint64_t layoutKey = 0,
        int32_t aaMode = 0,
        int32_t hintingMode = 0,
        float scaleX = 1.0f,
        float scaleY = 1.0f,
        // Axis-aligned display text floors the physical pen before the caller
        // snaps the final quad and samples it with POINT filtering.  Animated,
        // rotated, or skewed text leaves this false to retain continuous motion.
        bool crispAxisAligned = false,
        // Sub-pixel positioning (TextFormat::GetSubpixelPositioning): keep the
        // 1/8-pixel phases for EVERY run (grid-fitted sizes included) and
        // measure them from the final screen pen — the run origin's own
        // fraction is folded into the pen before the phase is taken, so the
        // emitted quad + the caller's whole-pixel snap land each glyph within
        // 1/8 px of its true place. Without it a run that slides or scales
        // sub-pixel has every glyph cross its own pixel boundary at a
        // different instant. Ignored for deformed (non-unit scale-bucket)
        // runs. Origin-dependent: the instance memo is keyed by the origin
        // phase as well. Mirrors D3D12GlyphAtlas::GenerateGlyphs.
        bool subpixelPositioning = false,
        // Full 2x2 linear part of the caller's transform, row-major
        // (m11, m12, m21, m22). Pass nullptr — or an axis-aligned matrix — to
        // keep the historical behaviour exactly: the run is placed with
        // scaleX/scaleY alone and the glyphs are rasterized upright.
        //
        // When it carries a real rotation / skew the run switches to the
        // ROTATED path: atlas strikes remain upright, while the pen and both
        // glyph-quad basis vectors are mapped through the matrix. The oriented
        // quads are already final — the caller must NOT re-magnify them by
        // scaleX/scaleY (see VulkanRenderTarget::RenderText).
        const float* linear2x2 = nullptr,
        // Optional {x, y} origin for the emitted TextDecorationRects. Glyph
        // quads carry their oriented basis, so they need the POST-transform
        // origin; decorations are drawn as plain rects that run through the
        // ambient transform again, so they need the PRE-transform one — which
        // is also what makes an underline rotate along with its text. Pass
        // nullptr to keep the historical behaviour of sharing the glyph origin.
        const float* decorationOrigin = nullptr,
        // Set when a small symbol-font run uses a higher-resolution strike.
        // The render target must select its smooth sampler to resolve that
        // coverage into the final pixel grid.
        bool* outRequiresSmoothSampling = nullptr);

    // ── B4 GPU-uploader accessors ──
    //
    // The B4 render-target batch owns the VkImage and uploads this class's CPU
    // bitmap using the render target's dynamically-loaded Vulkan function
    // pointers. These accessors hand it the bitmap, dims, generation, dirty band
    // and recreation signal WITHOUT this class touching any Vulkan API.

    /// Raw pointer to the CPU atlas bitmap (RGBA, GetWidth()×GetHeight() pixels).
    const uint8_t* GetAtlasBitmap() const { return atlasBitmap_.data(); }

    /// Current atlas dimensions. These reflect the CURRENT atlas size, which
    /// starts at kInitialAtlasDim and grows ×2 as glyphs spill over the bottom.
    uint32_t GetWidth() const { return atlasW_; }
    uint32_t GetHeight() const { return atlasH_; }

    /// Atlas generation: bumped on every Reset()/GrowAtlas(). B4 can tag its
    /// uploaded VkImage state with this and re-upload the whole atlas on change.
    uint32_t GetGeneration() const { return atlasGeneration_; }

    /// If the atlas bitmap is dirty, returns true plus the dirty [outMinY,
    /// outMaxY) row band and clears the dirty state (mirrors D3D12 FlushToGpu's
    /// dirty-region consume, but WITHOUT uploading — just hands the band to the
    /// B4 caller). A full-atlas dirty (Reset/GrowAtlas) returns [0, height).
    bool TakeDirtyBand(uint16_t& outMinY, uint16_t& outMaxY);

    /// Returns true exactly once if the atlas bitmap was reallocated (grow or
    /// reset) since the last call, then clears the flag. Signals B4 to recreate
    /// its VkImage at the new GetWidth()/GetHeight() size before the next upload.
    bool ConsumeAtlasRecreated();

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
    /// data is uploaded for the new frame. Used by the idle-resource reclaimer
    /// when the application has been quiet long enough that the cache should be
    /// dropped to release memory. Safe to call from any thread mid-frame — the
    /// actual work happens later, inside ApplyPendingGrowthOrReset.
    /// HARD reset request: the caller wants the memory back (or the cached
    /// pixels are the wrong format after an antialias-mode switch), so it is
    /// never downgraded to a compaction the way an atlas-full condition is.
    void RequestResetAtFrameBoundary() { needsHardReset_ = true; }

    /// Returns true if AllocateAtlasRect ran out of space *and* the atlas can
    /// still grow before hitting kMaxAtlasDim. Caller (B4 render-target
    /// BeginFrame) should call ApplyPendingGrowthOrReset at the frame boundary.
    bool NeedsGrow() const { return needsGrow_; }

    /// Frame-boundary entry point: if the previous frame requested growth, do it
    /// now (preserving cached glyph pixels); if the previous frame requested a
    /// reset because growth wasn't possible, perform the reset. CPU-only — safe
    /// to call before the new frame's glyph upload.
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

    /// Bytes of atlas memory currently packed (approximate: rows packed
    /// + current row partial column).
    int64_t GetPackedBytes() const {
        int64_t wholeRows = static_cast<int64_t>(packY_) * atlasW_;
        int64_t currentRowPartial = static_cast<int64_t>(packX_) * rowHeight_;
        return (wholeRows + currentRowPartial) * 4;  // RGBA8
    }

    /// Total bytes the CPU atlas bitmap occupies at its CURRENT size.
    int64_t GetTotalBytes() const {
        return static_cast<int64_t>(atlasW_) * atlasH_ * 4;
    }

private:
    /// Rasterization policy the font's `gasp` table asks for at a given ppem,
    /// as reported by IDWriteFontFace3::GetRecommendedRenderingMode.
    /// Mirrors D3D12GlyphAtlas::GaspPolicy.
    struct GaspPolicy {
        DWRITE_RENDERING_MODE1 mode = DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC;
        DWRITE_GRID_FIT_MODE   gridFit = DWRITE_GRID_FIT_MODE_DEFAULT;
        /// The recommendation is one CreateGlyphRunAnalysis cannot serve
        /// (OUTLINE / ALIASED / DEFAULT) — use the GDI bitmap render target.
        bool useGdiFallback = false;
        /// True when the mode grid-fits outlines onto the pixel grid, i.e. the
        /// font asked for hinting at this size. Those sizes render a visibly
        /// different bitmap per sub-pixel phase, so GenerateGlyphs pins phase 0
        /// there and trades sub-pixel positioning for shape consistency.
        bool gridAligned = false;
    };

    /// Memoised GetRecommendedRenderingMode. Both the per-run path
    /// (GenerateGlyphs) and the per-cache-miss path (RasterizeGlyph) resolve
    /// through here so they can never disagree about how a glyph is rasterized.
    GaspPolicy ResolveGaspPolicy(IDWriteFontFace* fontFace,
                                 uint16_t ppem,
                                 const DWRITE_MATRIX* transform,
                                 uint8_t hintingMode);

    /// Keyed by (face, ppem, aspect bucket, hinting mode). Cleared with cache_
    /// in Reset(), since cache_ is what keeps those faces alive.
    std::unordered_map<uint64_t, GaspPolicy> gaspPolicyCache_;

    bool RasterizeGlyph(const GlyphKey& key, GlyphEntry& entry);
    bool AllocateAtlasRect(uint16_t w, uint16_t h, uint16_t& outX, uint16_t& outY);
    // Grow atlasBitmap_ to at least (reqW, reqH), capped at kMaxAtlasDim.
    // Preserves all already-packed glyph pixels, bumps atlasGeneration_, sets
    // atlasRecreated_ and marks the whole new bitmap dirty. CPU-only — no GPU
    // calls; B4 recreates its VkImage when ConsumeAtlasRecreated() reports true.
    bool GrowAtlas(uint32_t reqW, uint32_t reqH);

    // Re-packs the strikes used within the last kCompactKeepFrames frames into
    // the front of the atlas (tallest first, which is what makes shelf packing
    // efficient) and drops everything older. Pixels are memcpy'd from their old
    // slot, so nothing is re-rasterized. Bumps the generation like Reset does —
    // the caller must be at a frame boundary. Returns false when there is
    // nothing stale worth reclaiming. Mirrors D3D12GlyphAtlas.
    bool CompactAtlas();

    // Sums the packed area of strikes used within kCompactKeepFrames (live) and
    // of everything older (dead). Drives both the growth target and the
    // decision to compact.
    void MeasurePackedArea(uint64_t& liveArea, uint64_t& deadArea) const;

    // Frames whose strikes survive a compaction.
    static constexpr uint32_t kCompactKeepFrames = 2;

    // Incremented once per frame boundary (ApplyPendingGrowthOrReset), stamped
    // into GlyphCacheValue::lastUsedFrame on every cache touch.
    uint32_t frameCounter_ = 0;

    IDWriteFactory* dwriteFactory_;

    // Atlas bitmap — starts at kInitialAtlasDim and grows ×2 up to kMaxAtlasDim
    // on overflow. Sized lazily so an idle UI keeps a 1 MB atlas instead of 64 MB.
    static constexpr uint32_t kInitialAtlasDim = 512;
    static constexpr uint32_t kMaxAtlasDim = 4096;
    uint32_t atlasW_ = kInitialAtlasDim;
    uint32_t atlasH_ = kInitialAtlasDim;

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

    // Set true by GrowAtlas()/Reset() when atlasBitmap_ is reallocated. Consumed
    // (and cleared) once by ConsumeAtlasRecreated() so the B4 GPU uploader knows
    // to recreate its VkImage at the new atlas size before the next upload.
    bool atlasRecreated_ = false;

    // Resolved-glyph memo: colour-neutral, origin-relative quads + decorations
    // for a shaped run keyed by layoutKey alone (no origin, no colour). emit
    // applies the caller's premultiplied colour AND translates each quad by
    // the screen-space origin, so the same shaped run hits across every
    // origin it appears at (scrolling, drag, animation) and across every
    // foreground colour.
    struct CachedGlyphRun {
        std::vector<VkGlyphInstance> instances;    // posX/posY = layout-local; colour unset
        std::vector<TextDecorationRect> decos;     // x/y = layout-local; colour unset
        bool requiresSmoothSampling = false;
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
                                    bool crispAxisAligned,
                                    uint8_t originPhaseX = 0,
                                    bool subpixelPositioning = false,
                                    // Four quantized 2x2 components (GlyphKey
                                    // xf11Q..xf22Q) for a rotated run, or
                                    // nullptr on the axis-aligned path — which
                                    // leaves the hash exactly as it was.
                                    const int16_t* xformQ = nullptr) noexcept;

    // Simple row-based atlas packer
    uint16_t packX_ = 0;
    uint16_t packY_ = 0;
    uint16_t rowHeight_ = 0;

    // Dirty tracking for upload (consumed by TakeDirtyBand)
    bool dirty_ = false;
    uint16_t dirtyMinY_ = UINT16_MAX;
    uint16_t dirtyMaxY_ = 0;

    // DirectWrite rasterization
    Microsoft::WRL::ComPtr<IDWriteFactory3> dwriteFactory3_;  // cached QI for CreateGlyphRunAnalysis
    Microsoft::WRL::ComPtr<IDWriteFactory4> dwriteFactory4_;  // cached QI for TranslateColorGlyphRun (color emoji)
    Microsoft::WRL::ComPtr<IDWriteBitmapRenderTarget> bitmapRenderTarget_;  // GDI fallback rasterizer
    Microsoft::WRL::ComPtr<IDWriteRenderingParams> renderingParams_;

    // Rasterizes a single colour-emoji glyph (COLR / CPAL / bitmap-strike font)
    // by walking IDWriteFactory4::TranslateColorGlyphRun layers and compositing
    // them (palette-coloured vector masks + WIC-decoded PNG/JPEG/TIFF strikes)
    // into a premultiplied-RGBA atlas slot flagged isColor. Returns false when
    // this glyph has no decodable colour representation (DWRITE_E_NOCOLOR,
    // factory-4-less OS, decode failure) — the caller then falls back. Ported
    // from D3D12GlyphAtlas::RasterizeColorGlyph (B5a).
    bool RasterizeColorGlyph(const GlyphKey& key, GlyphEntry& entry);

    bool initialized_ = false;
    bool needsReset_ = false;  // Atlas overflowed — grow, compact or reset next frame
    // Unconditional wipe requested: the idle reclaimer wants the memory back, or
    // an antialias-mode switch made every cached strike the wrong format. Never
    // downgraded to a compaction, which would keep those pixels.
    bool needsHardReset_ = false;
    bool needsGrow_ = false;   // Atlas can still grow — grow bitmap next frame
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

#endif // _WIN32
