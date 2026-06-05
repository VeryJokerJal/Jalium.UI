#pragma once

#include "jalium_backend.h"
#include "d3d12_backend.h"
#include <vector>
#include <list>
#include <unordered_map>
#include <cstdint>

namespace jalium {

/// Simple color representation (no D2D dependency).
struct ColorF { float r, g, b, a; };

/// Gradient stop with position and color (no D2D dependency).
struct GradStop { float position; ColorF color; };

class D3D12SolidBrush : public Brush {
public:
    D3D12SolidBrush(float r, float g, float b, float a);
    ~D3D12SolidBrush() override = default;

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_SOLID; }

    float r_, g_, b_, a_;
};

class D3D12LinearGradientBrush : public Brush {
public:
    D3D12LinearGradientBrush(
        float startX, float startY, float endX, float endY,
        const JaliumGradientStop* stops, uint32_t stopCount);
    ~D3D12LinearGradientBrush() override = default;

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_LINEAR_GRADIENT; }

    float startX_, startY_, endX_, endY_;
    std::vector<GradStop> stops_;
    uint32_t spreadMethod_ = 0;  // 0=Pad, 1=Repeat, 2=Reflect
};

class D3D12RadialGradientBrush : public Brush {
public:
    D3D12RadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount);
    ~D3D12RadialGradientBrush() override = default;

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_RADIAL_GRADIENT; }

    float centerX_, centerY_, radiusX_, radiusY_, originX_, originY_;
    std::vector<GradStop> stops_;
    uint32_t spreadMethod_ = 0;  // 0=Pad, 1=Repeat, 2=Reflect
};

/// D3D12 bitmap wrapper.
class D3D12Bitmap : public Bitmap {
public:
    D3D12Bitmap(D3D12Backend* backend, uint32_t width, uint32_t height);
    ~D3D12Bitmap() override;

    uint32_t GetWidth() const override { return width_; }
    uint32_t GetHeight() const override { return height_; }

    /// Gets or creates a D3D12 texture resource for direct rendering.
    ID3D12Resource* GetOrCreateD3D12Texture(ID3D12Device* device, ID3D12GraphicsCommandList* cmdList);

    /// Sets the bitmap data from WIC.
    void SetBitmapData(const uint8_t* data, uint32_t dataSize);

    /// Updates packed BGRA8 pixels in place. Marks the bitmap as dynamic — subsequent
    /// GPU uploads will reuse the existing default-heap texture (no mip chain) instead
    /// of recreating a fresh committed resource per call. Returns true on success.
    /// Used by video frame / WriteableBitmap hot paths to avoid GPU memory thrashing.
    bool UpdatePackedPixels(const uint8_t* pixels, uint32_t width, uint32_t height, uint32_t stride) override;

    /// Releases old resources that are no longer needed by the GPU.
    /// Call after a fence wait confirms the GPU finished using old textures.
    void ReleasePendingResources();

    uint32_t width_, height_;
    std::vector<uint8_t> pixelData_;

    // Public so the renderer can park the current upload buffer in
    // bitmapTextures_ to keep it alive at least for the duration of the
    // command list that referenced it via CopyTextureRegion. Returns a
    // ref-counted handle; the renderer drops it on the next BeginFrame
    // after fence-wait, by which point the GPU has finished the copy.
    ComPtr<ID3D12Resource> GetCurrentUploadBuffer() const { return d3d12UploadBuffer_; }
    ComPtr<ID3D12Resource> GetCurrentTexture() const { return d3d12Texture_; }

private:
    // Backend owning this bitmap. Never owned. Used only at destruction time
    // to forward the underlying GPU resources to the fence-gated graveyard,
    // since the bitmap may be Disposed from any thread (worker, GC finalizer)
    // while the GPU is still consuming its texture/upload buffer.
    D3D12Backend* backend_ = nullptr;

    // D3D12 texture for direct renderer path
    ComPtr<ID3D12Resource> d3d12Texture_;
    ComPtr<ID3D12Resource> d3d12UploadBuffer_;
    bool d3d12TextureValid_ = false;
    // True for video / WriteableBitmap fast-update path: skip mip chain generation
    // and reuse the existing default-heap texture across uploads.
    bool isDynamic_ = false;
    // Deferred release: old resources kept alive until GPU finishes using them
    std::vector<ComPtr<ID3D12Resource>> pendingRelease_;
    // Tracks the bytes this bitmap has currently added to the global
    // bitmap_stats::gpuResidentBytes counter. Destructor subtracts the same
    // amount so reload / release accounting stays balanced.
    uint64_t pinnedGpuBytes_ = 0;

    // D3D12VideoSurface flips isDynamic_ / d3d12TextureValid_ after Lock/Unlock
    // writes a fresh BGRA8 frame into pixelData_ — kept as a friend grant
    // rather than exposing the two flags publicly.
    friend class D3D12VideoSurface;
    // ImportedD3D12VideoSurface bypasses the upload path entirely: it sets
    // d3d12Texture_ directly to a texture imported via OpenSharedHandle and
    // marks d3d12TextureValid_ so GetOrCreateD3D12Texture's fast path returns
    // it immediately without trying to upload pixelData_.
    friend class ImportedD3D12VideoSurface;
};

/// Stage 3b.2: a video surface wrapping a D3D12 texture imported from a
/// D3D11 NT shared handle (typically the latest MF DXVA decode output mirrored
/// into a SHARED_NTHANDLE texture, see win_mf_video_decoder's stage 3b.1).
///
/// Embeds a D3D12Bitmap so DrawVideoSurface can route through the existing
/// DrawBitmap path. The bitmap's d3d12Texture_ slot is set directly to the
/// imported resource and d3d12TextureValid_ is true so GetOrCreateD3D12Texture
/// hits its fast path and never tries to upload from pixelData_ (which stays
/// empty — there is no CPU staging on this path).
///
/// Cross-device synchronization (keyed mutex acquire/release between D3D12
/// reader and D3D11 MF writer) is left to a later iteration; the current
/// implementation relies on the MF side's Flush + ReleaseSync to ensure the
/// shared texture's pixels are coherent at the moment Wrap returns.
class ImportedD3D12VideoSurface : public VideoSurface {
public:
    ImportedD3D12VideoSurface(D3D12Backend* backend,
                              ComPtr<ID3D12Resource> importedTexture,
                              uint32_t width, uint32_t height);
    ~ImportedD3D12VideoSurface() override;

    uint32_t GetWidth()  const override { return bitmap.width_;  }
    uint32_t GetHeight() const override { return bitmap.height_; }
    JaliumVideoSurfaceKind GetKind() const override { return JALIUM_VS_KIND_D3D11_SHARED; }

    bool Lock(uint8_t** outPtr, uint32_t* outStride) override;
    bool Unlock(const JaliumVideoSurfaceDirtyRect* dirty) override;

    /// Stage 3b.3 reader-side sync. Called by D3D12RenderTarget::DrawVideoSurface
    /// before/after sampling. Acquires reader key (1), then releases writer key
    /// (0) to let MF write the next frame. When the imported resource doesn't
    /// surface IDXGIKeyedMutex (driver / SHARED_KEYEDMUTEX flag absent), both
    /// calls are no-ops and we fall back to the in-order-execution assumption
    /// stage 3b.2 relied on.
    void AcquireReaderLock();
    void ReleaseReaderLock();

    ComPtr<ID3D12Resource>  importedTexture;
    ComPtr<IDXGIKeyedMutex> keyedMutex;     // null if driver doesn't expose it
    D3D12Bitmap             bitmap;
    bool                    holdsReaderLock = false;
};

/// Video surface implementation: a thin wrapper around a D3D12Bitmap that
/// exposes the bitmap's CPU staging vector to managed callers via Lock/Unlock.
/// The decoder pump writes BGRA8 frames straight into pixelData_;
/// Unlock invalidates d3d12TextureValid_ so the next render pass copies the
/// fresh CPU buffer into the default-heap texture in one shot. Reuses the
/// existing D3D12Bitmap retire / SRV / draw path, so the only thing this
/// class adds is the writable-vector + dirty-flag plumbing.
class D3D12VideoSurface : public VideoSurface {
public:
    D3D12VideoSurface(D3D12Backend* backend, uint32_t width, uint32_t height);
    ~D3D12VideoSurface() override = default;

    uint32_t GetWidth()  const override { return bitmap.width_;  }
    uint32_t GetHeight() const override { return bitmap.height_; }
    JaliumVideoSurfaceKind GetKind() const override { return JALIUM_VS_KIND_BGRA8_CPU; }

    bool Lock(uint8_t** outPtr, uint32_t* outStride) override;
    bool Unlock(const JaliumVideoSurfaceDirtyRect* dirty) override;

    /// Composable target the D3D12 render path already knows how to draw.
    /// D3D12RenderTarget::DrawVideoSurface routes through this.
    D3D12Bitmap bitmap;
};

/// DirectWrite text format wrapper.
class D3D12TextFormat : public TextFormat {
public:
    D3D12TextFormat(IDWriteFactory* factory,
                    const wchar_t* fontFamily,
                    float fontSize,
                    int32_t fontWeight,
                    int32_t fontStyle);
    ~D3D12TextFormat() override = default;

    void SetAlignment(int32_t alignment) override;
    void SetParagraphAlignment(int32_t alignment) override;
    void SetTrimming(int32_t trimming) override;
    void SetWordWrapping(int32_t wrapping) override;
    void SetLineSpacing(int32_t method, float spacing, float baseline) override;
    void SetMaxLines(uint32_t maxLines) override;

    JaliumResult MeasureText(
        const wchar_t* text,
        uint32_t textLength,
        float maxWidth,
        float maxHeight,
        JaliumTextMetrics* metrics) override;

    JaliumResult GetFontMetrics(JaliumTextMetrics* metrics) override;

    JaliumResult HitTestPoint(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        float pointX, float pointY,
        JaliumTextHitTestResult* result) override;

    JaliumResult HitTestTextPosition(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        uint32_t textPosition, int32_t isTrailingHit,
        JaliumTextHitTestResult* result) override;

    IDWriteTextFormat* GetFormat() const { return format_.Get(); }
    IDWriteFactory* GetFactory() const { return factory_; }

    /// Creates an IDWriteTextLayout with the current format settings (including
    /// maxLines). Cached: DirectWrite shaping/itemization/line-breaking is
    /// ~10-15µs and a data-heavy frame issues hundreds of identical calls
    /// (profiled: DrawText 344 calls / 5.46 ms). The shaped layout depends
    /// only on (text, maxWidth, maxHeight, maxLines) for THIS format object —
    /// the format (font/size/weight/style/alignment/wrap/trim/spacing) is the
    /// natural cache partition, so a format mutation clears the whole cache.
    /// AddText consumes the layout read-only, so sharing one instance across
    /// draws and frames is safe.
    /// `outKey` (optional) receives a globally-unique content hash of this
    /// shaped layout (text + this format object + constraints). The glyph
    /// atlas uses it to memoize resolved glyph quads across frames.
    HRESULT CreateLayout(const wchar_t* text, uint32_t textLength,
                         float maxWidth, float maxHeight,
                         IDWriteTextLayout** layout,
                         uint64_t* outKey = nullptr);

    /// Reports cache hit / miss / eviction counters since last reset.
    /// Used by DevTools to verify CreateLayout cache effectiveness.
    static uint64_t QueryLayoutCacheHits() noexcept;
    static uint64_t QueryLayoutCacheMisses() noexcept;
    static uint64_t QueryLayoutCacheEvictions() noexcept;

private:
    /// Cache key intentionally excludes maxWidth / maxHeight: those dimensions
    /// are re-applied to the cached layout via SetMaxWidth/SetMaxHeight on hit
    /// (cheap — DirectWrite just invalidates internal layout calc), while the
    /// expensive CreateTextLayout (text parsing + script analysis + font
    /// fallback + glyph shaping) runs only when the text content or the
    /// maxLines clamp actually changes. This is what turns animated TextBlock
    /// content with a fluctuating element width — previously a guaranteed
    /// miss every frame — into a 95%+ hit-rate scenario.
    uint64_t HashLayoutKey(const wchar_t* text, uint32_t textLength) const noexcept;
    /// Drop all cached layouts. Called by every setter that mutates a
    /// layout-affecting format property so stale layouts are never served.
    void InvalidateLayoutCache() noexcept;
    /// Apply the maxLines_ height clamp to a layout. DirectWrite has no
    /// SetMaxLines API, so we constrain max height to the summed height of the
    /// first maxLines_ lines. Must run on BOTH the cache-miss and cache-hit
    /// paths: the cache key excludes maxWidth, so a width change yields a hit
    /// whose re-applied SetMaxHeight(maxHeight) would otherwise wipe the clamp
    /// and re-wrap text past the line limit.
    void ApplyMaxLinesClamp(IDWriteTextLayout* layout) const noexcept;

    ComPtr<IDWriteTextFormat> format_;
    IDWriteFactory* factory_ = nullptr;
    float fontSize_ = 0.0f;
    uint32_t maxLines_ = 0;  // 0 = unlimited

    // Bounded LRU of shaped layouts. Cap covers a data-heavy frame's full
    // visible text set (hundreds of unique strings) so it reuses across
    // frames instead of thrashing. Layouts are small (DirectWrite internal
    // buffers, a few KB each).
    struct LayoutCacheEntry { uint64_t key; ComPtr<IDWriteTextLayout> layout; };
    std::list<LayoutCacheEntry> layoutLru_;
    std::unordered_map<uint64_t, std::list<LayoutCacheEntry>::iterator> layoutMap_;
    static constexpr size_t kLayoutCacheCap = 2048;
};

} // namespace jalium
