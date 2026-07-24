#include "d3d12_resources.h"
#include "jalium_text_stats.h"

namespace jalium {

D3D12TextFormat::D3D12TextFormat(
    IDWriteFactory* factory,
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
    : factory_(factory), fontSize_(fontSize)
{
    DWRITE_FONT_WEIGHT weight = static_cast<DWRITE_FONT_WEIGHT>(fontWeight);
    DWRITE_FONT_STYLE style = static_cast<DWRITE_FONT_STYLE>(fontStyle);

    factory->CreateTextFormat(
        fontFamily,
        nullptr,  // Font collection (nullptr = system collection)
        weight,
        style,
        DWRITE_FONT_STRETCH_NORMAL,
        fontSize,
        L"",  // Locale
        &format_);
}

void D3D12TextFormat::SetAlignment(int32_t alignment) {
    if (!format_) return;

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

    format_->SetTextAlignment(textAlignment);
    InvalidateLayoutCache();
}

void D3D12TextFormat::SetParagraphAlignment(int32_t alignment) {
    if (!format_) return;

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

    format_->SetParagraphAlignment(paragraphAlignment);
    InvalidateLayoutCache();
}

void D3D12TextFormat::SetTrimming(int32_t trimming) {
    if (!format_ || !factory_) return;

    DWRITE_TRIMMING trimmingOptions = {};
    ComPtr<IDWriteInlineObject> ellipsis;

    switch (trimming) {
        case JALIUM_TEXT_TRIMMING_NONE:
            trimmingOptions.granularity = DWRITE_TRIMMING_GRANULARITY_NONE;
            break;
        case JALIUM_TEXT_TRIMMING_CHARACTER_ELLIPSIS:
            trimmingOptions.granularity = DWRITE_TRIMMING_GRANULARITY_CHARACTER;
            factory_->CreateEllipsisTrimmingSign(format_.Get(), &ellipsis);
            break;
        case JALIUM_TEXT_TRIMMING_WORD_ELLIPSIS:
            trimmingOptions.granularity = DWRITE_TRIMMING_GRANULARITY_WORD;
            factory_->CreateEllipsisTrimmingSign(format_.Get(), &ellipsis);
            break;
        default:
            trimmingOptions.granularity = DWRITE_TRIMMING_GRANULARITY_NONE;
            break;
    }

    format_->SetTrimming(&trimmingOptions, ellipsis.Get());
    InvalidateLayoutCache();
}

void D3D12TextFormat::SetWordWrapping(int32_t wrapping) {
    if (!format_) return;

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

    format_->SetWordWrapping(wordWrapping);
    InvalidateLayoutCache();
}

void D3D12TextFormat::SetLineSpacing(int32_t method, float spacing, float baseline) {
    if (!format_) return;

    DWRITE_LINE_SPACING_METHOD dwMethod;
    switch (method) {
        case 0: dwMethod = DWRITE_LINE_SPACING_METHOD_DEFAULT; break;
        case 1: dwMethod = DWRITE_LINE_SPACING_METHOD_UNIFORM; break;
        case 2: dwMethod = DWRITE_LINE_SPACING_METHOD_PROPORTIONAL; break;
        default: dwMethod = DWRITE_LINE_SPACING_METHOD_DEFAULT; break;
    }

    format_->SetLineSpacing(dwMethod, spacing, baseline);
    InvalidateLayoutCache();
}

void D3D12TextFormat::SetMaxLines(uint32_t maxLines) {
    if (maxLines_ == maxLines) return;
    maxLines_ = maxLines;
    InvalidateLayoutCache();
}

uint64_t D3D12TextFormat::HashLayoutKey(
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

void D3D12TextFormat::InvalidateLayoutCache() noexcept
{
    layoutMap_.clear();
    layoutLru_.clear();
}

void D3D12TextFormat::ApplyMaxLinesClamp(IDWriteTextLayout* layout) const noexcept
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

// Cache hit/miss/eviction telemetry has moved to the unified core atomics
// (jalium::text_stats — see jalium_text_stats.h). The legacy
// QueryLayoutCache* statics now read directly from there so any caller that
// still pokes them keeps working; new code should query the unified C ABI
// jalium_query_text_stats which also exposes instance / glyph-raster caches.
uint64_t D3D12TextFormat::QueryLayoutCacheHits() noexcept {
    JaliumTextStats s{};
    jalium_query_text_stats(&s);
    return s.layoutHits;
}
uint64_t D3D12TextFormat::QueryLayoutCacheMisses() noexcept {
    JaliumTextStats s{};
    jalium_query_text_stats(&s);
    return s.layoutMisses;
}
uint64_t D3D12TextFormat::QueryLayoutCacheEvictions() noexcept {
    JaliumTextStats s{};
    jalium_query_text_stats(&s);
    return s.layoutEvictions;
}

HRESULT D3D12TextFormat::CreateLayout(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    IDWriteTextLayout** layout,
    uint64_t* outKey)
{
    if (outKey) *outKey = 0;  // 0 = uncacheable; set on every success path below
    if (!layout) return E_POINTER;
    *layout = nullptr;
    if (!factory_ || !format_) return E_FAIL;

    // Two-tier key design:
    //   - `key` (text + maxLines only) drives the physical layoutMap_ cache.
    //     SetMaxWidth/SetMaxHeight can replay the cached shaped layout against
    //     any new dimensions without re-running DirectWrite's heavy text
    //     analysis + glyph shaping, so width/height fluctuations no longer
    //     thrash the layout cache.
    //   - `outKey` (text + maxLines + width + height + format identity) drives
    //     the glyph-INSTANCE cache one tier above, where positioned glyph
    //     quads do depend on the layout dimensions — different maxWidth can
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

    HRESULT hr = factory_->CreateTextLayout(
        text, textLength, format_.Get(), maxWidth, maxHeight, layout);

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
        layoutLru_.push_front({ key, ComPtr<IDWriteTextLayout>(*layout) });
        layoutMap_[key] = layoutLru_.begin();
    }

    return hr;
}

const D3D12TextFormat::FontFaceMetrics& D3D12TextFormat::EnsureFontFaceMetrics()
{
    if (fontFaceMetrics_.attempted) {
        return fontFaceMetrics_;
    }
    fontFaceMetrics_.attempted = true;

    if (!format_) {
        return fontFaceMetrics_;  // resolved stays false -> callers use the line-metric fallback
    }

    ComPtr<IDWriteFontCollection> fontCollection;
    format_->GetFontCollection(&fontCollection);
    if (!fontCollection) {
        return fontFaceMetrics_;
    }

    UINT32 familyNameLen = format_->GetFontFamilyNameLength() + 1;
    std::vector<WCHAR> familyNameBuf(familyNameLen);
    format_->GetFontFamilyName(familyNameBuf.data(), familyNameLen);

    uint32_t familyIndex = 0;
    BOOL exists = FALSE;
    fontCollection->FindFamilyName(familyNameBuf.data(), &familyIndex, &exists);
    if (!exists) {
        return fontFaceMetrics_;
    }

    ComPtr<IDWriteFontFamily> fontFamily;
    fontCollection->GetFontFamily(familyIndex, &fontFamily);
    if (!fontFamily) {
        return fontFaceMetrics_;
    }

    ComPtr<IDWriteFont> font;
    fontFamily->GetFirstMatchingFont(
        format_->GetFontWeight(),
        format_->GetFontStretch(),
        format_->GetFontStyle(),
        &font);
    if (!font) {
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

JaliumResult D3D12TextFormat::MeasureText(
    const wchar_t* text,
    uint32_t textLength,
    float maxWidth,
    float maxHeight,
    JaliumTextMetrics* metrics)
{
    if (!format_ || !factory_ || !text || !metrics) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    ComPtr<IDWriteTextLayout> layout;
    HRESULT hr = CreateLayout(text, textLength, maxWidth, maxHeight, &layout);
    if (FAILED(hr) || !layout) {
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    // Get text metrics from the layout
    DWRITE_TEXT_METRICS textMetrics;
    hr = layout->GetMetrics(&textMetrics);
    if (FAILED(hr)) {
        return JALIUM_ERROR_UNKNOWN;
    }

    // Keep WPF's two width concepts distinct: layout uses the trimmed width,
    // while caret and selection calculations retain trailing-space advances.
    metrics->width = textMetrics.width;
    metrics->widthIncludingTrailingWhitespace = textMetrics.widthIncludingTrailingWhitespace;
    metrics->height = textMetrics.height;
    metrics->lineCount = textMetrics.lineCount;

    // Get line metrics for the first line to extract font metrics
    DWRITE_LINE_METRICS lineMetrics;
    uint32_t actualLineCount = 0;
    hr = layout->GetLineMetrics(&lineMetrics, 1, &actualLineCount);
    if (SUCCEEDED(hr) && actualLineCount > 0) {
        metrics->baseline = lineMetrics.baseline;
        metrics->lineHeight = lineMetrics.height;

        // Calculate ascent, descent from baseline and line height
        // In DirectWrite: baseline = ascent (from top of line to baseline)
        // height = ascent + descent + lineGap
        metrics->ascent = lineMetrics.baseline;
        metrics->descent = lineMetrics.height - lineMetrics.baseline;

        // Per-format font-face metrics (ascent / descent / lineGap and the
        // WPF-style lineHeight) are constant for this format's immutable font
        // identity, so resolve them once and reuse. The old code re-ran the full
        // GetFontCollection -> FindFamilyName -> GetFirstMatchingFont ->
        // GetMetrics chain on EVERY MeasureText (even when the shaped layout was
        // a cache hit), which dominated the managed measure pass while scrolling
        // recycled rows. When the face does not resolve, ascent/descent/lineGap/
        // lineHeight keep the line-metric values computed just above — exactly as
        // the old `if (font)`-guarded block left them.
        const FontFaceMetrics& faceMetrics = EnsureFontFaceMetrics();
        if (faceMetrics.resolved) {
            metrics->ascent = faceMetrics.ascent;
            metrics->descent = faceMetrics.descent;
            metrics->lineGap = faceMetrics.lineGap;
            // WPF-style line height: ascent + descent + lineGap
            metrics->lineHeight = faceMetrics.lineHeight;
        }
    } else {
        // Fallback: use approximate values
        metrics->lineHeight = fontSize_ * 1.2f;
        metrics->baseline = fontSize_;
        metrics->ascent = fontSize_;
        metrics->descent = fontSize_ * 0.2f;
        metrics->lineGap = 0.0f;
    }

    return JALIUM_OK;
}

JaliumResult D3D12TextFormat::GetFontMetrics(JaliumTextMetrics* metrics)
{
    if (!format_ || !factory_ || !metrics) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    // Initialize output
    memset(metrics, 0, sizeof(JaliumTextMetrics));

    // Get font collection
    ComPtr<IDWriteFontCollection> fontCollection;
    format_->GetFontCollection(&fontCollection);
    if (!fontCollection) {
        // Use fallback values
        metrics->ascent = fontSize_;
        metrics->descent = fontSize_ * 0.2f;
        metrics->lineGap = 0.0f;
        metrics->lineHeight = fontSize_ * 1.2f;
        metrics->baseline = fontSize_;
        return JALIUM_OK;
    }

    // Find the font family
    UINT32 familyNameLen = format_->GetFontFamilyNameLength() + 1;
    std::vector<WCHAR> familyNameBuf(familyNameLen);
    format_->GetFontFamilyName(familyNameBuf.data(), familyNameLen);

    uint32_t familyIndex = 0;
    BOOL exists = FALSE;
    fontCollection->FindFamilyName(familyNameBuf.data(), &familyIndex, &exists);

    if (!exists) {
        // Font not found, use fallback
        metrics->ascent = fontSize_;
        metrics->descent = fontSize_ * 0.2f;
        metrics->lineGap = 0.0f;
        metrics->lineHeight = fontSize_ * 1.2f;
        metrics->baseline = fontSize_;
        return JALIUM_OK;
    }

    // Get the font family
    ComPtr<IDWriteFontFamily> fontFamily;
    fontCollection->GetFontFamily(familyIndex, &fontFamily);
    if (!fontFamily) {
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    // Get matching font
    ComPtr<IDWriteFont> font;
    fontFamily->GetFirstMatchingFont(
        format_->GetFontWeight(),
        format_->GetFontStretch(),
        format_->GetFontStyle(),
        &font);

    if (!font) {
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    // Get font metrics
    DWRITE_FONT_METRICS fontMetrics;
    font->GetMetrics(&fontMetrics);

    // Convert design units to DIPs
    // The scale factor is fontSize / designUnitsPerEm
    float scale = fontSize_ / static_cast<float>(fontMetrics.designUnitsPerEm);

    metrics->ascent = fontMetrics.ascent * scale;
    metrics->descent = fontMetrics.descent * scale;
    metrics->lineGap = fontMetrics.lineGap * scale;

    // WPF-style natural line height: ascent + descent + lineGap
    metrics->lineHeight = metrics->ascent + metrics->descent + metrics->lineGap;
    metrics->baseline = metrics->ascent;

    return JALIUM_OK;
}

JaliumResult D3D12TextFormat::HitTestPoint(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    float pointX, float pointY,
    JaliumTextHitTestResult* result)
{
    if (!format_ || !factory_ || !text || !result) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    memset(result, 0, sizeof(JaliumTextHitTestResult));

    ComPtr<IDWriteTextLayout> layout;
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

JaliumResult D3D12TextFormat::HitTestTextPosition(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    uint32_t textPosition, int32_t isTrailingHit,
    JaliumTextHitTestResult* result)
{
    if (!format_ || !factory_ || !text || !result) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    memset(result, 0, sizeof(JaliumTextHitTestResult));

    ComPtr<IDWriteTextLayout> layout;
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

} // namespace jalium
