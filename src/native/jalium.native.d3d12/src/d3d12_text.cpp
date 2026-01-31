#include "d3d12_resources.h"

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

    // Create a text layout for accurate measurement
    ComPtr<IDWriteTextLayout> layout;
    HRESULT hr = factory_->CreateTextLayout(
        text,
        textLength,
        format_.Get(),
        maxWidth,
        maxHeight,
        &layout);

    if (FAILED(hr) || !layout) {
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    // Get text metrics from the layout
    DWRITE_TEXT_METRICS textMetrics;
    hr = layout->GetMetrics(&textMetrics);
    if (FAILED(hr)) {
        return JALIUM_ERROR_UNKNOWN;
    }

    // Fill out the output metrics
    // Use widthIncludingTrailingWhitespace to include trailing spaces in measurement
    // This is important for caret positioning and selection highlighting of spaces
    metrics->width = textMetrics.widthIncludingTrailingWhitespace;
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

        // Try to get more accurate font metrics from the font face
        ComPtr<IDWriteFontCollection> fontCollection;
        format_->GetFontCollection(&fontCollection);
        if (fontCollection) {
            uint32_t familyIndex = 0;
            BOOL exists = FALSE;
            WCHAR familyName[256];
            format_->GetFontFamilyName(familyName, 256);
            fontCollection->FindFamilyName(familyName, &familyIndex, &exists);

            if (exists) {
                ComPtr<IDWriteFontFamily> fontFamily;
                fontCollection->GetFontFamily(familyIndex, &fontFamily);
                if (fontFamily) {
                    ComPtr<IDWriteFont> font;
                    fontFamily->GetFirstMatchingFont(
                        format_->GetFontWeight(),
                        format_->GetFontStretch(),
                        format_->GetFontStyle(),
                        &font);

                    if (font) {
                        DWRITE_FONT_METRICS fontMetrics;
                        font->GetMetrics(&fontMetrics);

                        // Convert design units to DIPs
                        // designUnitsPerEm is the scale factor
                        float scale = fontSize_ / static_cast<float>(fontMetrics.designUnitsPerEm);
                        metrics->ascent = fontMetrics.ascent * scale;
                        metrics->descent = fontMetrics.descent * scale;
                        metrics->lineGap = fontMetrics.lineGap * scale;
                        // WPF-style line height: ascent + descent + lineGap
                        metrics->lineHeight = metrics->ascent + metrics->descent + metrics->lineGap;
                    }
                }
            }
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
    WCHAR familyName[256];
    format_->GetFontFamilyName(familyName, 256);

    uint32_t familyIndex = 0;
    BOOL exists = FALSE;
    fontCollection->FindFamilyName(familyName, &familyIndex, &exists);

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

} // namespace jalium
