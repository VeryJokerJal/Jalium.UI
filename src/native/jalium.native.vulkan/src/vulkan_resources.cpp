#include "vulkan_resources.h"
#include "jalium_bitmap_stats.h"

#ifndef _WIN32
#include "text_engine.h"
#include "text_layout.h"
#endif

#ifdef _WIN32
#include <Windows.h>
#include "jalium_text_stats.h"
#endif

#include <algorithm>
#include <cmath>
#include <cstring>

namespace jalium {

VulkanSolidBrush::VulkanSolidBrush(float r, float g, float b, float a)
    : r_(r), g_(g), b_(b), a_(a)
{
}

VulkanLinearGradientBrush::VulkanLinearGradientBrush(
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t spreadMethod)
    : startX_(startX), startY_(startY), endX_(endX), endY_(endY)
    , spreadMethod_(spreadMethod)
{
    if (stops && stopCount > 0) {
        stops_.assign(stops, stops + stopCount);
    }
}

VulkanRadialGradientBrush::VulkanRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t spreadMethod)
    : centerX_(centerX)
    , centerY_(centerY)
    , radiusX_(radiusX)
    , radiusY_(radiusY)
    , originX_(originX)
    , originY_(originY)
    , spreadMethod_(spreadMethod)
{
    if (stops && stopCount > 0) {
        stops_.assign(stops, stops + stopCount);
    }
}

VulkanBitmap::VulkanBitmap(uint32_t width, uint32_t height, std::vector<uint8_t> pixelData)
    : width_(width > 0 ? width : 1)
    , height_(height > 0 ? height : 1)
    , pixelData_(std::make_shared<std::vector<uint8_t>>(std::move(pixelData)))
{
    // Ensure pixel data matches expected size (4 bytes per RGBA pixel)
    size_t expected = static_cast<size_t>(width_) * height_ * 4;
    if (pixelData_->size() < expected) {
        pixelData_->resize(expected, 0);
    }
}

bool VulkanBitmap::UpdatePackedPixels(const uint8_t* pixels, uint32_t width, uint32_t height, uint32_t stride)
{
    if (!pixels || width == 0 || height == 0 || stride < width * 4u) {
        return false;
    }
    if (width != width_ || height != height_) {
        return false;  // Size change requires recreation.
    }

    const size_t rowBytes = static_cast<size_t>(width) * 4u;
    const size_t requiredSize = rowBytes * height;
    auto& current = *pixelData_;

    // Memcmp short-circuit — same pattern as D3D12Bitmap. Video frames /
    // WriteableBitmap hot paths commonly push the same pixels every frame
    // when the underlying content is paused or unchanged; this lets us bail
    // before the memcpy + GPU upload pipeline. memcmp returns at the first
    // differing byte so genuinely-changed bitmaps pay only the diverging
    // prefix.
    if (current.size() == requiredSize) {
        if (stride == rowBytes) {
            if (std::memcmp(current.data(), pixels, requiredSize) == 0) {
                bitmap_stats::AddMemcmpShortCircuit();
                return true;
            }
        } else {
            bool same = true;
            for (uint32_t row = 0; row < height; ++row) {
                if (std::memcmp(current.data() + row * rowBytes,
                                pixels + static_cast<size_t>(row) * stride,
                                rowBytes) != 0)
                {
                    same = false;
                    break;
                }
            }
            if (same) {
                bitmap_stats::AddMemcmpShortCircuit();
                return true;
            }
        }
    }

    // Allocate a fresh shared buffer and copy into it. This is the
    // copy-on-write step: any GpuReplayCommand still referring to the
    // previous frame's shared_ptr keeps that buffer alive on its own, so
    // in-flight GPU work continues to see consistent pixels even though we
    // immediately publish the new content for subsequent draws.
    auto next = std::make_shared<std::vector<uint8_t>>(requiredSize);
    if (stride == rowBytes) {
        std::memcpy(next->data(), pixels, requiredSize);
    } else {
        for (uint32_t row = 0; row < height; ++row) {
            std::memcpy(next->data() + row * rowBytes,
                        pixels + static_cast<size_t>(row) * stride,
                        rowBytes);
        }
    }
    pixelData_ = std::move(next);

    bitmap_stats::AddUpload(requiredSize);
    // Telemetry parity with D3D12's dynamic-path AddDynamicReuse: a dynamic
    // bitmap (video / WriteableBitmap) updated its pixels WITHOUT recreating
    // any GPU resource. On Vulkan there is no per-bitmap texture at all — the
    // pixels ride through the shared per-frame staging buffer + upload image
    // at replay time — so every non-short-circuited in-place update is by
    // construction a "dynamic reuse" (the D3D12 equivalent of overwriting the
    // existing default-heap texture instead of CreateCommittedResource).
    bitmap_stats::AddDynamicReuse();
    return true;
}

#ifdef _WIN32

// Process-shared DirectWrite factory. Thread-safe function-local-static Meyers
// singleton: DWriteCreateFactory(SHARED) then QueryInterface to IDWriteFactory5.
// Mirrors the single-factory funnel the D3D12 backend uses. Returns a raw
// IDWriteFactory5* owned by the static ComPtr — never destroyed before exit.
// May be nullptr if creation or the QI fails; callers must null-check.
IDWriteFactory5* GetSharedDWriteFactory()
{
    static Microsoft::WRL::ComPtr<IDWriteFactory5> factory5 = [] {
        Microsoft::WRL::ComPtr<IDWriteFactory> factory;
        HRESULT hr = DWriteCreateFactory(
            DWRITE_FACTORY_TYPE_SHARED,
            __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(factory.GetAddressOf()));
        Microsoft::WRL::ComPtr<IDWriteFactory5> result;
        if (SUCCEEDED(hr) && factory) {
            factory.As(&result);  // QueryInterface to IDWriteFactory5 (null on failure)
        }
        return result;
    }();
    return factory5.Get();
}

VulkanTextFormat::VulkanTextFormat(
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
    : fontFamily_(fontFamily ? fontFamily : L"Segoe UI")
    , fontSize_(fontSize)
    , fontWeight_(fontWeight)
    , fontStyle_(fontStyle)
{
    // Create the DirectWrite format object (mirrors D3D12TextFormat ctor).
    // Font weight maps directly (JaliumFontWeight values are the DWRITE_FONT_WEIGHT
    // numeric values); font style maps directly (0=normal/1=italic/2=oblique are
    // the DWRITE_FONT_STYLE values). Same empty locale as D3D12. If the shared
    // factory or CreateTextFormat fails, dwFormat_ stays null and the
    // measurement methods fall back to approximate metrics.
    IDWriteFactory5* factory = GetSharedDWriteFactory();
    if (factory) {
        DWRITE_FONT_WEIGHT weight = static_cast<DWRITE_FONT_WEIGHT>(fontWeight);
        DWRITE_FONT_STYLE style = static_cast<DWRITE_FONT_STYLE>(fontStyle);

        factory->CreateTextFormat(
            fontFamily_.c_str(),
            nullptr,  // Font collection (nullptr = system collection)
            weight,
            style,
            DWRITE_FONT_STRETCH_NORMAL,
            fontSize,
            L"",  // Locale
            &dwFormat_);
    }
}
#else
VulkanTextFormat::VulkanTextFormat(
    TextEngine* textEngine,
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
    : fontFamily_(fontFamily ? fontFamily : L"Sans")
    , fontSize_(fontSize)
    , fontWeight_(fontWeight)
    , fontStyle_(fontStyle)
{
    // Create a JaliumTextFormat if text engine is available
    if (textEngine) {
        auto* ftFormat = textEngine->CreateTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
        if (ftFormat) {
            ftTextFormat_.reset(static_cast<JaliumTextFormat*>(ftFormat));
        }
    }
}

#endif

VulkanTextFormat::~VulkanTextFormat() = default;

void VulkanTextFormat::SetAlignment(int32_t alignment)
{
    alignment_ = alignment;
#ifdef _WIN32
    if (dwFormat_) {
        DWRITE_TEXT_ALIGNMENT textAlignment;
        switch (alignment) {
            case JALIUM_TEXT_ALIGN_LEADING:
                textAlignment = DWRITE_TEXT_ALIGNMENT_LEADING;
                break;
            case JALIUM_TEXT_ALIGN_TRAILING:
                textAlignment = DWRITE_TEXT_ALIGNMENT_TRAILING;
                break;
            case JALIUM_TEXT_ALIGN_CENTER:
                textAlignment = DWRITE_TEXT_ALIGNMENT_CENTER;
                break;
            case JALIUM_TEXT_ALIGN_JUSTIFIED:
                textAlignment = DWRITE_TEXT_ALIGNMENT_JUSTIFIED;
                break;
            default:
                textAlignment = DWRITE_TEXT_ALIGNMENT_LEADING;
                break;
        }
        dwFormat_->SetTextAlignment(textAlignment);
        InvalidateLayoutCache();
    }
#else
    if (ftTextFormat_) ftTextFormat_->SetAlignment(alignment);
#endif
}

void VulkanTextFormat::SetParagraphAlignment(int32_t alignment)
{
    paragraphAlignment_ = alignment;
#ifdef _WIN32
    if (dwFormat_) {
        DWRITE_PARAGRAPH_ALIGNMENT paragraphAlignment;
        switch (alignment) {
            case JALIUM_PARAGRAPH_ALIGN_NEAR:
                paragraphAlignment = DWRITE_PARAGRAPH_ALIGNMENT_NEAR;
                break;
            case JALIUM_PARAGRAPH_ALIGN_FAR:
                paragraphAlignment = DWRITE_PARAGRAPH_ALIGNMENT_FAR;
                break;
            case JALIUM_PARAGRAPH_ALIGN_CENTER:
                paragraphAlignment = DWRITE_PARAGRAPH_ALIGNMENT_CENTER;
                break;
            default:
                paragraphAlignment = DWRITE_PARAGRAPH_ALIGNMENT_NEAR;
                break;
        }
        dwFormat_->SetParagraphAlignment(paragraphAlignment);
        InvalidateLayoutCache();
    }
#else
    if (ftTextFormat_) ftTextFormat_->SetParagraphAlignment(alignment);
#endif
}

void VulkanTextFormat::SetTrimming(int32_t trimming)
{
    trimming_ = trimming;
#ifdef _WIN32
    IDWriteFactory5* factory = GetSharedDWriteFactory();
    if (dwFormat_ && factory) {
        DWRITE_TRIMMING trimmingOptions = {};
        Microsoft::WRL::ComPtr<IDWriteInlineObject> ellipsis;

        switch (trimming) {
            case JALIUM_TEXT_TRIMMING_NONE:
                trimmingOptions.granularity = DWRITE_TRIMMING_GRANULARITY_NONE;
                break;
            case JALIUM_TEXT_TRIMMING_CHARACTER_ELLIPSIS:
                trimmingOptions.granularity = DWRITE_TRIMMING_GRANULARITY_CHARACTER;
                factory->CreateEllipsisTrimmingSign(dwFormat_.Get(), &ellipsis);
                break;
            case JALIUM_TEXT_TRIMMING_WORD_ELLIPSIS:
                trimmingOptions.granularity = DWRITE_TRIMMING_GRANULARITY_WORD;
                factory->CreateEllipsisTrimmingSign(dwFormat_.Get(), &ellipsis);
                break;
            default:
                trimmingOptions.granularity = DWRITE_TRIMMING_GRANULARITY_NONE;
                break;
        }

        dwFormat_->SetTrimming(&trimmingOptions, ellipsis.Get());
        InvalidateLayoutCache();
    }
#else
    if (ftTextFormat_) ftTextFormat_->SetTrimming(trimming);
#endif
}

void VulkanTextFormat::SetWordWrapping(int32_t wrapping)
{
    wordWrapping_ = wrapping;
#ifdef _WIN32
    if (dwFormat_) {
        DWRITE_WORD_WRAPPING wordWrapping;
        switch (wrapping) {
            case JALIUM_WORD_WRAP:
                wordWrapping = DWRITE_WORD_WRAPPING_WRAP;
                break;
            case JALIUM_WORD_WRAP_NONE:
                wordWrapping = DWRITE_WORD_WRAPPING_NO_WRAP;
                break;
            case JALIUM_WORD_WRAP_CHARACTER:
                wordWrapping = DWRITE_WORD_WRAPPING_CHARACTER;
                break;
            case JALIUM_WORD_WRAP_EMERGENCY:
                wordWrapping = DWRITE_WORD_WRAPPING_EMERGENCY_BREAK;
                break;
            default:
                wordWrapping = DWRITE_WORD_WRAPPING_WRAP;
                break;
        }
        dwFormat_->SetWordWrapping(wordWrapping);
        InvalidateLayoutCache();
    }
#else
    if (ftTextFormat_) ftTextFormat_->SetWordWrapping(wrapping);
#endif
}

void VulkanTextFormat::SetLineSpacing(int32_t method, float spacing, float baseline)
{
    lineSpacingMethod_ = method;
    lineSpacingMultiplier_ = spacing;
    lineSpacingBaseline_ = baseline;
#ifdef _WIN32
    if (dwFormat_) {
        DWRITE_LINE_SPACING_METHOD dwMethod;
        switch (method) {
            case 0: dwMethod = DWRITE_LINE_SPACING_METHOD_DEFAULT; break;
            case 1: dwMethod = DWRITE_LINE_SPACING_METHOD_UNIFORM; break;
            case 2: dwMethod = DWRITE_LINE_SPACING_METHOD_PROPORTIONAL; break;
            default: dwMethod = DWRITE_LINE_SPACING_METHOD_DEFAULT; break;
        }
        dwFormat_->SetLineSpacing(dwMethod, spacing, baseline);
        InvalidateLayoutCache();
    }
#else
    if (ftTextFormat_) ftTextFormat_->SetLineSpacing(method, spacing, baseline);
#endif
}

void VulkanTextFormat::SetMaxLines(uint32_t maxLines) {
#ifdef _WIN32
    if (maxLines_ == maxLines) return;
    maxLines_ = maxLines;
    InvalidateLayoutCache();
#else
    maxLines_ = maxLines;
    if (ftTextFormat_) ftTextFormat_->SetMaxLines(maxLines);
#endif
}

#ifdef _WIN32
uint64_t VulkanTextFormat::HashLayoutKey(
    const wchar_t* text, uint32_t textLength) const noexcept
{
    uint64_t h = 0xCBF29CE484222325ull;  // FNV-1a 64-bit
    auto mix = [&h](const void* p, size_t n) {
        const uint8_t* b = static_cast<const uint8_t*>(p);
        for (size_t i = 0; i < n; ++i) { h ^= b[i]; h *= 0x100000001B3ull; }
    };
    mix(&textLength, sizeof(textLength));
    if (text && textLength)
        mix(text, static_cast<size_t>(textLength) * sizeof(wchar_t));
    mix(&maxLines_, sizeof(maxLines_));
    return h;
}

void VulkanTextFormat::InvalidateLayoutCache() noexcept
{
    layoutMap_.clear();
    layoutLru_.clear();
}

void VulkanTextFormat::ApplyMaxLinesClamp(IDWriteTextLayout* layout) const noexcept
{
    if (!layout || maxLines_ == 0) return;
    // DirectWrite doesn't have a SetMaxLines API. Approximate by constraining
    // max height to lineHeight * maxLines, so the layout clips at that boundary.
    DWRITE_TEXT_METRICS tm = {};
    if (SUCCEEDED(layout->GetMetrics(&tm)) && tm.lineCount > maxLines_) {
        std::vector<DWRITE_LINE_METRICS> lineMetrics(tm.lineCount);
        uint32_t actualLines = 0;
        if (SUCCEEDED(layout->GetLineMetrics(lineMetrics.data(), tm.lineCount, &actualLines))) {
            float totalH = 0;
            for (uint32_t i = 0; i < maxLines_ && i < actualLines; ++i) {
                totalH += lineMetrics[i].height;
            }
            // Clamp the height in place rather than recreating the layout —
            // SetMaxHeight only re-runs layout pass 2 (line breaking),
            // skipping the heavy text-analysis / shaping work.
            layout->SetMaxHeight(totalH);
        }
    }
}

HRESULT VulkanTextFormat::CreateLayout(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    IDWriteTextLayout** layout,
    uint64_t* outKey)
{
    if (outKey) *outKey = 0;  // 0 = uncacheable; set on every success path below
    if (!layout) return E_POINTER;
    *layout = nullptr;
    IDWriteFactory5* factory = GetSharedDWriteFactory();
    if (!factory || !dwFormat_) return E_FAIL;

    // Two-tier key design (mirrors D3D12TextFormat::CreateLayout):
    //   - `key` (text + maxLines only) drives the physical layoutMap_ cache.
    //     SetMaxWidth/SetMaxHeight replay the cached shaped layout against new
    //     dimensions without re-running DirectWrite's heavy text analysis +
    //     glyph shaping, so width/height fluctuations don't thrash the cache.
    //   - `outKey` (text + maxLines + width + height + format identity) drives
    //     the glyph-INSTANCE cache one tier above, where positioned glyph
    //     quads do depend on the layout dimensions — a different maxWidth can
    //     produce different wrap points, so that cache MUST miss on width
    //     changes even though the layout cache hit.
    const uint64_t key = HashLayoutKey(text, textLength);
    if (outKey) {
        uint64_t gk = key;
        gk ^= (uint64_t)reinterpret_cast<uintptr_t>(this)
              + 0x9E3779B97F4A7C15ull + (gk << 6) + (gk >> 2);
        auto mixGk = [&gk](const void* p, size_t n) {
            const uint8_t* b = static_cast<const uint8_t*>(p);
            for (size_t i = 0; i < n; ++i) { gk ^= b[i]; gk *= 0x100000001B3ull; }
        };
        mixGk(&maxWidth,  sizeof(maxWidth));
        mixGk(&maxHeight, sizeof(maxHeight));
        *outKey = gk;
    }
    if (auto it = layoutMap_.find(key); it != layoutMap_.end()) {
        // Hit. Promote to MRU and re-apply the caller's dimensions to the
        // cached layout (cheap: just invalidates the layout's internal layout
        // calc — the heavy text-analysis / shaping work is preserved).
        layoutLru_.splice(layoutLru_.begin(), layoutLru_, it->second);
        IDWriteTextLayout* cached = it->second->layout.Get();
        if (cached) {
            cached->SetMaxWidth(maxWidth);
            cached->SetMaxHeight(maxHeight);
            // Re-apply the maxLines clamp: SetMaxHeight(maxHeight) above just
            // overwrote any clamp baked in on a prior call, and the cache key
            // excludes maxWidth so a width change still lands here as a hit.
            ApplyMaxLinesClamp(cached);
            cached->AddRef();
            *layout = cached;
            jalium::text_stats::AddLayoutHit();
            return S_OK;
        }
        // Defensive: empty slot — drop and fall through to recreate.
        layoutMap_.erase(it);
    }

    jalium::text_stats::AddLayoutMiss();

    HRESULT hr = factory->CreateTextLayout(
        text, textLength, dwFormat_.Get(), maxWidth, maxHeight, layout);

    if (SUCCEEDED(hr) && *layout) {
        ApplyMaxLinesClamp(*layout);
    }

    if (SUCCEEDED(hr) && *layout) {
        // Insert final (post-maxLines) layout. ComPtr's raw-pointer ctor
        // AddRefs, so the cache owns its own ref while *layout keeps the
        // caller's +1 from CreateTextLayout. Evict LRU tail at capacity.
        if (layoutLru_.size() >= kLayoutCacheCap) {
            layoutMap_.erase(layoutLru_.back().key);
            layoutLru_.pop_back();
            jalium::text_stats::AddLayoutEviction(1);
        }
        layoutLru_.push_front({ key, Microsoft::WRL::ComPtr<IDWriteTextLayout>(*layout) });
        layoutMap_[key] = layoutLru_.begin();
    }

    return hr;
}

// Resolve-once font-face metrics (mirrors D3D12TextFormat::EnsureFontFaceMetrics
// in d3d12_text.cpp). See the FontFaceMetrics declaration in vulkan_resources.h
// for why this must not run per MeasureText call: the resolution chain below is
// pure DWrite font-collection lookup whose inputs (family / size / weight /
// style) are fixed at construction, yet the old inline copies re-ran it on
// every MeasureText / GetFontMetrics — the virtualization scroll hot path.
const VulkanTextFormat::FontFaceMetrics& VulkanTextFormat::EnsureFontFaceMetrics()
{
    if (fontFaceMetrics_.attempted) {
        return fontFaceMetrics_;
    }
    fontFaceMetrics_.attempted = true;

    if (!dwFormat_) {
        return fontFaceMetrics_;  // resolved stays false → callers use their fallbacks
    }

    Microsoft::WRL::ComPtr<IDWriteFontCollection> fontCollection;
    dwFormat_->GetFontCollection(&fontCollection);
    if (!fontCollection) {
        return fontFaceMetrics_;
    }

    UINT32 familyNameLen = dwFormat_->GetFontFamilyNameLength() + 1;
    std::vector<WCHAR> familyNameBuf(familyNameLen);
    dwFormat_->GetFontFamilyName(familyNameBuf.data(), familyNameLen);

    uint32_t familyIndex = 0;
    BOOL exists = FALSE;
    fontCollection->FindFamilyName(familyNameBuf.data(), &familyIndex, &exists);
    if (!exists) {
        return fontFaceMetrics_;
    }

    Microsoft::WRL::ComPtr<IDWriteFontFamily> fontFamily;
    fontCollection->GetFontFamily(familyIndex, &fontFamily);
    if (!fontFamily) {
        fontFaceMetrics_.hardFailure = true;
        return fontFaceMetrics_;
    }

    Microsoft::WRL::ComPtr<IDWriteFont> font;
    fontFamily->GetFirstMatchingFont(
        dwFormat_->GetFontWeight(),
        dwFormat_->GetFontStretch(),
        dwFormat_->GetFontStyle(),
        &font);
    if (!font) {
        fontFaceMetrics_.hardFailure = true;
        return fontFaceMetrics_;
    }

    DWRITE_FONT_METRICS fontMetrics;
    font->GetMetrics(&fontMetrics);

    // Convert design units to DIPs (designUnitsPerEm is the scale factor).
    const float scale = fontSize_ / static_cast<float>(fontMetrics.designUnitsPerEm);
    fontFaceMetrics_.ascent  = fontMetrics.ascent * scale;
    fontFaceMetrics_.descent = fontMetrics.descent * scale;
    fontFaceMetrics_.lineGap = fontMetrics.lineGap * scale;
    // WPF-style natural line height: ascent + descent + lineGap.
    fontFaceMetrics_.lineHeight =
        fontFaceMetrics_.ascent + fontFaceMetrics_.descent + fontFaceMetrics_.lineGap;
    fontFaceMetrics_.resolved = true;
    return fontFaceMetrics_;
}
#endif  // _WIN32

JaliumResult VulkanTextFormat::HitTestPoint(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    float pointX, float pointY,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;
    memset(result, 0, sizeof(*result));
#ifdef _WIN32
    // DirectWrite path (mirrors D3D12TextFormat::HitTestPoint). Falls through to
    // the zeroed JALIUM_OK result below if the shared factory / format is null.
    if (dwFormat_ && text) {
        Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
        HRESULT hr = CreateLayout(text, textLength, maxWidth, maxHeight, &layout);
        if (FAILED(hr) || !layout) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;

        BOOL isTrailingHit = FALSE;
        BOOL isInside = FALSE;
        DWRITE_HIT_TEST_METRICS hitMetrics = {};

        hr = layout->HitTestPoint(pointX, pointY, &isTrailingHit, &isInside, &hitMetrics);
        if (FAILED(hr)) return JALIUM_ERROR_UNKNOWN;

        result->textPosition = hitMetrics.textPosition;
        result->isTrailingHit = isTrailingHit ? 1 : 0;
        result->isInside = isInside ? 1 : 0;

        // Get caret position for this text position
        float caretX = 0, caretY = 0;
        DWRITE_HIT_TEST_METRICS caretMetrics = {};
        hr = layout->HitTestTextPosition(hitMetrics.textPosition, isTrailingHit, &caretX, &caretY, &caretMetrics);
        if (SUCCEEDED(hr)) {
            result->caretX = caretX;
            result->caretY = caretY;
            result->caretHeight = caretMetrics.height;
        }
        return JALIUM_OK;
    }
#else
    if (ftTextFormat_)
        return ftTextFormat_->HitTestPoint(text, textLength, maxWidth, 0, pointX, pointY, result);
#endif
    return JALIUM_OK;
}

JaliumResult VulkanTextFormat::HitTestTextPosition(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    uint32_t textPosition, int32_t isTrailingHit,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;
    memset(result, 0, sizeof(*result));
#ifdef _WIN32
    // DirectWrite path (mirrors D3D12TextFormat::HitTestTextPosition). Falls
    // through to the zeroed JALIUM_OK result below if factory / format is null.
    if (dwFormat_ && text) {
        Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
        HRESULT hr = CreateLayout(text, textLength, maxWidth, maxHeight, &layout);
        if (FAILED(hr) || !layout) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;

        float caretX = 0, caretY = 0;
        DWRITE_HIT_TEST_METRICS hitMetrics = {};

        hr = layout->HitTestTextPosition(textPosition, isTrailingHit ? TRUE : FALSE,
                                          &caretX, &caretY, &hitMetrics);
        if (FAILED(hr)) return JALIUM_ERROR_UNKNOWN;

        result->textPosition = textPosition;
        result->isTrailingHit = isTrailingHit;
        result->isInside = 1;
        result->caretX = caretX;
        result->caretY = caretY;
        result->caretHeight = hitMetrics.height;
        return JALIUM_OK;
    }
#else
    if (ftTextFormat_)
        return ftTextFormat_->HitTestTextPosition(text, textLength, maxWidth, 0, textPosition, isTrailingHit, result);
#endif
    return JALIUM_OK;
}

JaliumResult VulkanTextFormat::MeasureText(
    const wchar_t* text,
    uint32_t textLength,
    float maxWidth,
    float maxHeight,
    JaliumTextMetrics* metrics)
{
    if (!text || !metrics) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    std::memset(metrics, 0, sizeof(JaliumTextMetrics));

#ifdef _WIN32
    // DirectWrite path (mirrors D3D12TextFormat::MeasureText). When the shared
    // factory / format failed to create, dwFormat_ is null and we fall through
    // to the approximate-metrics fallback below.
    if (dwFormat_) {
        Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
        HRESULT hr = CreateLayout(text, textLength, maxWidth, maxHeight, &layout);
        if (FAILED(hr) || !layout) {
            return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
        }

        DWRITE_TEXT_METRICS textMetrics;
        hr = layout->GetMetrics(&textMetrics);
        if (FAILED(hr)) {
            return JALIUM_ERROR_UNKNOWN;
        }

        // Layout excludes trailing spaces; caret and selection still need them.
        metrics->width = textMetrics.width;
        metrics->widthIncludingTrailingWhitespace = textMetrics.widthIncludingTrailingWhitespace;
        metrics->height = textMetrics.height;
        metrics->lineCount = textMetrics.lineCount;

        // First-line metrics give baseline / line height / ascent / descent.
        DWRITE_LINE_METRICS lineMetrics;
        uint32_t actualLineCount = 0;
        hr = layout->GetLineMetrics(&lineMetrics, 1, &actualLineCount);
        if (SUCCEEDED(hr) && actualLineCount > 0) {
            metrics->baseline = lineMetrics.baseline;
            metrics->lineHeight = lineMetrics.height;
            // DirectWrite: baseline = ascent; height = ascent + descent + lineGap.
            metrics->ascent = lineMetrics.baseline;
            metrics->descent = lineMetrics.height - lineMetrics.baseline;

            // Refine ascent/descent/lineGap/lineHeight from the resolved font
            // face. Resolved ONCE per format via EnsureFontFaceMetrics (mirrors
            // D3D12) — the old inline chain re-ran the full GetFontCollection →
            // FindFamilyName → GetFirstMatchingFont → GetMetrics lookup on
            // EVERY MeasureText (even when the shaped layout was a cache hit),
            // which dominated the managed measure pass while scrolling
            // recycled virtualized rows. When the face does not resolve, the
            // line-metric values just computed are left in place — exactly as
            // the old `if (font)`-guarded block's failure path left them.
            const FontFaceMetrics& faceMetrics = EnsureFontFaceMetrics();
            if (faceMetrics.resolved) {
                metrics->ascent = faceMetrics.ascent;
                metrics->descent = faceMetrics.descent;
                metrics->lineGap = faceMetrics.lineGap;
                // WPF-style natural line height: ascent + descent + lineGap.
                metrics->lineHeight = faceMetrics.lineHeight;
            }
        } else {
            // Fallback: use approximate values (mirrors D3D12).
            metrics->lineHeight = fontSize_ * 1.2f;
            metrics->baseline = fontSize_;
            metrics->ascent = fontSize_;
            metrics->descent = fontSize_ * 0.2f;
            metrics->lineGap = 0.0f;
        }

        return JALIUM_OK;
    }
#else
    // Cross-platform: delegate to FreeType text engine
    if (ftTextFormat_) {
        return ftTextFormat_->MeasureText(text, textLength, maxWidth, maxHeight, metrics);
    }
#endif

    // Fallback: approximate metrics if no text engine available
    if (metrics->width == 0.0f && textLength > 0) {
        const float approxCharWidth = fontSize_ * 0.55f;
        const float approximateWidth = std::min(maxWidth, approxCharWidth * static_cast<float>(textLength));
        metrics->width = approximateWidth;
        metrics->widthIncludingTrailingWhitespace = approximateWidth;
        metrics->height = std::min(maxHeight, fontSize_ * 1.2f);
        metrics->lineCount = 1;
    }

    if (metrics->ascent == 0.0f) {
        metrics->ascent = fontSize_ * 0.8f;
        metrics->descent = fontSize_ * 0.2f;
        metrics->lineGap = fontSize_ * 0.2f;
        metrics->lineHeight = metrics->ascent + metrics->descent + metrics->lineGap;
        metrics->baseline = metrics->ascent;
        metrics->height = std::max(metrics->height, metrics->lineHeight);
    }
    return JALIUM_OK;
}

JaliumResult VulkanTextFormat::GetFontMetrics(JaliumTextMetrics* metrics)
{
    if (!metrics) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    std::memset(metrics, 0, sizeof(JaliumTextMetrics));

#ifdef _WIN32
    // DirectWrite path (mirrors D3D12TextFormat::GetFontMetrics). dwFormat_ is
    // null only when the shared factory / CreateTextFormat failed; in that case
    // we fall through to the approximate fallback below.
    if (dwFormat_) {
        // Font-face metrics are resolved once per format and cached (see
        // EnsureFontFaceMetrics) — this method used to re-run the full
        // GetFontCollection → FindFamilyName → GetFirstMatchingFont →
        // GetMetrics chain on every call. The three pre-cache outcomes are
        // preserved exactly:
        //   resolved    → face metrics (success path),
        //   hardFailure → RESOURCE_CREATION_FAILED (family / font object
        //                 came back null after the name lookup succeeded),
        //   neither     → fontSize_-derived fallback + OK (no collection, or
        //                 family name not found — mirrors D3D12).
        const FontFaceMetrics& faceMetrics = EnsureFontFaceMetrics();
        if (faceMetrics.resolved) {
            metrics->ascent = faceMetrics.ascent;
            metrics->descent = faceMetrics.descent;
            metrics->lineGap = faceMetrics.lineGap;
            // WPF-style natural line height: ascent + descent + lineGap.
            metrics->lineHeight = faceMetrics.lineHeight;
            metrics->baseline = faceMetrics.ascent;
            return JALIUM_OK;
        }
        if (faceMetrics.hardFailure) {
            return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
        }
        // Use fallback values (mirrors D3D12).
        metrics->ascent = fontSize_;
        metrics->descent = fontSize_ * 0.2f;
        metrics->lineGap = 0.0f;
        metrics->lineHeight = fontSize_ * 1.2f;
        metrics->baseline = fontSize_;
        return JALIUM_OK;
    }
#else
    if (ftTextFormat_) {
        return ftTextFormat_->GetFontMetrics(metrics);
    }
#endif

    // Fallback
    metrics->ascent = fontSize_ * 0.8f;
    metrics->descent = fontSize_ * 0.2f;
    metrics->lineGap = fontSize_ * 0.2f;
    metrics->lineHeight = metrics->ascent + metrics->descent + metrics->lineGap;
    metrics->baseline = metrics->ascent;
    return JALIUM_OK;
}

} // namespace jalium
