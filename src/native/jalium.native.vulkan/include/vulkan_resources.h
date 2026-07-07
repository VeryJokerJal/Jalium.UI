#pragma once

#include "jalium_backend.h"

#ifndef _WIN32
// Forward declarations for cross-platform text engine
namespace jalium { class TextEngine; class JaliumTextFormat; }
#endif

#include <string>
#include <vector>
#include <memory>
#include <cstdint>
#include <list>
#include <unordered_map>

#ifdef _WIN32
// DirectWrite text path (mirrors the D3D12 backend's DWrite usage so Windows
// Vulkan text measurement matches the DirectWrite rendering later batches add).
// dwrite_3.h is required for IDWriteFactory5; wrl/client.h supplies ComPtr.
#include <dwrite_3.h>
#include <wrl/client.h>
#endif

namespace jalium {

#ifdef _WIN32
// Process-shared DirectWrite factory accessor (Windows only). Mirrors the way
// the D3D12 backend funnels every IDWriteTextFormat / IDWriteTextLayout through
// a single factory. Returned as a raw pointer — the factory is owned by an
// internal function-local static and outlives every caller. MAY be nullptr if
// DWriteCreateFactory / QueryInterface fails, so every caller MUST null-check.
// Both this batch (VulkanTextFormat) and a later glyph-atlas batch use it.
IDWriteFactory5* GetSharedDWriteFactory();
#endif

class VulkanSolidBrush : public Brush {
public:
    VulkanSolidBrush(float r, float g, float b, float a);

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_SOLID; }

    float r_ = 0.0f;
    float g_ = 0.0f;
    float b_ = 0.0f;
    float a_ = 1.0f;
};

class VulkanLinearGradientBrush : public Brush {
public:
    VulkanLinearGradientBrush(
        float startX, float startY, float endX, float endY,
        const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0);

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_LINEAR_GRADIENT; }

    float startX_ = 0.0f;
    float startY_ = 0.0f;
    float endX_ = 0.0f;
    float endY_ = 0.0f;
    std::vector<JaliumGradientStop> stops_;
    uint32_t spreadMethod_ = 0;  // 0=Pad, 1=Repeat, 2=Reflect (mirrors D3D12LinearGradientBrush)
};

class VulkanRadialGradientBrush : public Brush {
public:
    VulkanRadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0);

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_RADIAL_GRADIENT; }

    float centerX_ = 0.0f;
    float centerY_ = 0.0f;
    float radiusX_ = 0.0f;
    float radiusY_ = 0.0f;
    float originX_ = 0.0f;
    float originY_ = 0.0f;
    std::vector<JaliumGradientStop> stops_;
    uint32_t spreadMethod_ = 0;  // 0=Pad, 1=Repeat, 2=Reflect (mirrors D3D12RadialGradientBrush)
};

class VulkanBitmap : public Bitmap {
public:
    VulkanBitmap(uint32_t width, uint32_t height, std::vector<uint8_t> pixelData);

    uint32_t GetWidth() const override { return width_; }
    uint32_t GetHeight() const override { return height_; }

    // Pixel data is held behind a shared_ptr so per-frame GpuReplayCommand
    // entries can keep a reference instead of vector-copying ~5 MB/RGBA bitmap
    // (Jalium.One CreateSolutionView's 31 × 1.3 MB PNGs were doing exactly
    // that). UpdatePackedPixels swaps in a fresh shared_ptr instead of
    // mutating the existing one — that way any GpuReplayCommand still
    // referencing the previous frame's pixels keeps its data alive and replays
    // correctly even after the source bitmap mutates.
    const std::vector<uint8_t>& GetPixels() const { return *pixelData_; }
    std::shared_ptr<const std::vector<uint8_t>> GetSharedPixels() const { return pixelData_; }

    /// Replaces the packed BGRA8 pixels in-place. Used by video / WriteableBitmap
    /// hot paths to avoid per-frame VulkanBitmap reconstruction. Returns true on success.
    bool UpdatePackedPixels(const uint8_t* pixels, uint32_t width, uint32_t height, uint32_t stride) override;

private:
    uint32_t width_ = 0;
    uint32_t height_ = 0;
    std::shared_ptr<std::vector<uint8_t>> pixelData_;
};

/// Vulkan video surface. Wraps a VulkanBitmap + an owned staging vector.
/// Lock returns a writable pointer into the staging vector; Unlock hands
/// staging off to the bitmap via UpdatePackedPixels so the existing COW path
/// stays safe for in-flight GpuReplayCommand replays — the bitmap allocates
/// a fresh shared_ptr<vector> rather than mutating the previous frame's
/// buffer.
///
/// Slightly more memcpy than the D3D12 path (staging → pixelData_ extra hop)
/// because we don't break the COW invariant, but still 1 copy fewer than
/// the legacy WriteableBitmap fallback.
class VulkanVideoSurface : public VideoSurface {
public:
    VulkanVideoSurface(uint32_t width, uint32_t height);

    uint32_t GetWidth()  const override { return bitmap.GetWidth();  }
    uint32_t GetHeight() const override { return bitmap.GetHeight(); }
    JaliumVideoSurfaceKind GetKind() const override { return JALIUM_VS_KIND_BGRA8_CPU; }

    bool Lock(uint8_t** outPtr, uint32_t* outStride) override;
    bool Unlock(const JaliumVideoSurfaceDirtyRect* dirty) override;

    /// The composable target the Vulkan render path already knows how to draw.
    VulkanBitmap         bitmap;
    std::vector<uint8_t> staging;
};

class VulkanTextFormat : public TextFormat {
public:
#ifdef _WIN32
    VulkanTextFormat(
        const wchar_t* fontFamily,
        float fontSize,
        int32_t fontWeight,
        int32_t fontStyle);
#else
    VulkanTextFormat(
        TextEngine* textEngine,
        const wchar_t* fontFamily,
        float fontSize,
        int32_t fontWeight,
        int32_t fontStyle);
#endif

    ~VulkanTextFormat() override;

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

#ifdef _WIN32
    /// Creates an IDWriteTextLayout with the current format settings (including
    /// maxLines). Cached two-tier exactly like D3D12TextFormat::CreateLayout —
    /// DirectWrite shaping/itemization/line-breaking is expensive and a
    /// data-heavy frame issues hundreds of identical calls. The shaped layout
    /// depends only on (text, maxWidth, maxHeight, maxLines) for THIS format
    /// object, so the physical cache keys on (text + maxLines) and re-applies
    /// width/height on hit. A later render-target batch calls this to render.
    /// `outKey` (optional) receives a globally-unique content hash of the
    /// shaped layout (text + this format object + constraints) for the glyph
    /// instance cache one tier above.
    HRESULT CreateLayout(const wchar_t* text, uint32_t textLength,
                         float maxWidth, float maxHeight,
                         IDWriteTextLayout** layout,
                         uint64_t* outKey = nullptr);

    IDWriteTextFormat* GetDWriteFormat() const { return dwFormat_.Get(); }
#endif

    const std::wstring& GetFontFamily() const { return fontFamily_; }
    float GetFontSize() const { return fontSize_; }
    int32_t GetAlignment() const { return alignment_; }
    int32_t GetParagraphAlignment() const { return paragraphAlignment_; }
    int32_t GetFontWeight() const { return fontWeight_; }
    int32_t GetFontStyle() const { return fontStyle_; }
    int32_t GetTrimming() const { return trimming_; }
    int32_t GetWordWrapping() const { return wordWrapping_; }
    uint32_t GetMaxLines() const { return maxLines_; }

#ifndef _WIN32
    JaliumTextFormat* GetTextFormat() const { return ftTextFormat_.get(); }
#endif

private:
#ifdef _WIN32
    /// Cache key intentionally excludes maxWidth / maxHeight (mirrors D3D12):
    /// those dimensions are re-applied to the cached layout via
    /// SetMaxWidth/SetMaxHeight on hit, while the expensive CreateTextLayout
    /// (text parsing + script analysis + font fallback + glyph shaping) runs
    /// only when the text content or the maxLines clamp actually changes.
    uint64_t HashLayoutKey(const wchar_t* text, uint32_t textLength) const noexcept;
    /// Drop all cached layouts. Called by every setter that mutates a
    /// layout-affecting format property so stale layouts are never served.
    void InvalidateLayoutCache() noexcept;
    /// Apply the maxLines_ height clamp to a layout. DirectWrite has no
    /// SetMaxLines API, so max height is constrained to the summed height of the
    /// first maxLines_ lines. Runs on BOTH the cache-miss and cache-hit paths.
    void ApplyMaxLinesClamp(IDWriteTextLayout* layout) const noexcept;
#endif

    std::wstring fontFamily_;
    float fontSize_ = 12.0f;
    int32_t fontWeight_ = 400;
    int32_t fontStyle_ = 0;
    int32_t alignment_ = JALIUM_TEXT_ALIGN_LEADING;
    int32_t paragraphAlignment_ = JALIUM_PARAGRAPH_ALIGN_NEAR;
    int32_t trimming_ = JALIUM_TEXT_TRIMMING_NONE;
    int32_t wordWrapping_ = 0;
    int32_t lineSpacingMethod_ = 0;
    float lineSpacingMultiplier_ = 0.0f;
    float lineSpacingBaseline_ = 0.0f;
    uint32_t maxLines_ = 0;

#ifdef _WIN32
    // DirectWrite format object for this font identity. Created from the
    // process-shared IDWriteFactory5 at construction; may stay null if the
    // factory or CreateTextFormat fails, in which case the measurement methods
    // fall back to the approximate-metrics path.
    Microsoft::WRL::ComPtr<IDWriteTextFormat> dwFormat_;

    // Per-format font-face metrics (ascent / descent / lineGap and the derived
    // WPF-style lineHeight) depend ONLY on this format's immutable font identity
    // (family / size / weight / style), all fixed at construction — none of the
    // public setters touch them. Resolving them costs a GetFontCollection ->
    // FindFamilyName -> GetFirstMatchingFont -> GetMetrics chain, which the old
    // MeasureText / GetFontMetrics re-ran on EVERY call (even on a layout-cache
    // hit) purely to fill ascent/descent/lineGap — on the D3D12 backend that
    // chain dominated the managed measure pass while scrolling recycled rows,
    // and this DWrite path had inherited the same shape. Resolve once and
    // reuse (structure mirrors D3D12TextFormat::FontFaceMetrics /
    // EnsureFontFaceMetrics). `resolved` records whether a matching font face
    // was found so the no-face fallback paths are reproduced exactly;
    // `hardFailure` additionally distinguishes GetFontMetrics' error-return
    // steps (family lookup succeeded but the family / font object came back
    // null → RESOURCE_CREATION_FAILED) from its silent-fallback steps (no
    // collection, or family name not found → approximate metrics + OK),
    // preserving the pre-cache return values.
    struct FontFaceMetrics {
        bool  attempted   = false;  // have we tried to resolve yet?
        bool  resolved    = false;  // was a matching font face found?
        bool  hardFailure = false;  // GetFontFamily / GetFirstMatchingFont returned null
        float ascent      = 0.0f;
        float descent     = 0.0f;
        float lineGap     = 0.0f;
        float lineHeight  = 0.0f;
    };
    const FontFaceMetrics& EnsureFontFaceMetrics();
    FontFaceMetrics fontFaceMetrics_;

    // Bounded LRU of shaped layouts (same structure + cap as D3D12). Cap covers
    // a data-heavy frame's full visible text set so it reuses across frames
    // instead of thrashing. Layouts are small (a few KB of DWrite buffers).
    struct LayoutCacheEntry { uint64_t key; Microsoft::WRL::ComPtr<IDWriteTextLayout> layout; };
    std::list<LayoutCacheEntry> layoutLru_;
    std::unordered_map<uint64_t, std::list<LayoutCacheEntry>::iterator> layoutMap_;
    static constexpr size_t kLayoutCacheCap = 2048;
#endif

#ifndef _WIN32
    // Self-hosted text engine (non-Windows)
    std::unique_ptr<JaliumTextFormat> ftTextFormat_;
#endif
};

} // namespace jalium
