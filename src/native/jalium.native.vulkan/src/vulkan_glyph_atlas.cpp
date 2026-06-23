#include "vulkan_glyph_atlas.h"

#ifdef _WIN32

#include "jalium_text_options.h"
#include "jalium_text_stats.h"
#include <cstring>
#include <cmath>
#include <algorithm>

namespace jalium {

// Copies a top-down 32bpp BGRA region out of a GDI memory DC (used by the GDI
// bitmap-render-target fallback path in RasterizeGlyph). Returns false on any
// GDI failure — the caller then drops back to leaving the glyph entry invalid.
static bool CopyDcRegionToBgraPixels(HDC sourceDc, int width, int height, std::vector<uint8_t>& pixels) {
    if (!sourceDc || width <= 0 || height <= 0) {
        return false;
    }

    BITMAPINFO bmi = {};
    bmi.bmiHeader.biSize = sizeof(bmi.bmiHeader);
    bmi.bmiHeader.biWidth = width;
    bmi.bmiHeader.biHeight = -height; // top-down
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    void* bits = nullptr;
    HDC scratchDc = CreateCompatibleDC(sourceDc);
    if (!scratchDc) {
        return false;
    }

    HBITMAP scratchBitmap = CreateDIBSection(sourceDc, &bmi, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (!scratchBitmap || !bits) {
        if (scratchBitmap) {
            DeleteObject(scratchBitmap);
        }
        DeleteDC(scratchDc);
        return false;
    }

    HGDIOBJ oldBitmap = SelectObject(scratchDc, scratchBitmap);
    bool copied = oldBitmap != nullptr && oldBitmap != HGDI_ERROR &&
                  BitBlt(scratchDc, 0, 0, width, height, sourceDc, 0, 0, SRCCOPY) != FALSE;

    if (copied) {
        GdiFlush();
        pixels.resize((size_t)width * height * 4);
        memcpy(pixels.data(), bits, pixels.size());
    }

    if (oldBitmap && oldBitmap != HGDI_ERROR) {
        SelectObject(scratchDc, oldBitmap);
    }
    DeleteObject(scratchBitmap);
    DeleteDC(scratchDc);
    return copied;
}

// ============================================================================
// Custom IDWriteTextRenderer for extracting glyph runs
// ============================================================================

class GlyphRunCollector : public IDWriteTextRenderer {
public:
    struct GlyphRun {
        Microsoft::WRL::ComPtr<IDWriteFontFace> fontFace;  // prevent dangling pointer via AddRef
        float fontSize;
        float baselineX, baselineY;
        std::vector<uint16_t> glyphIndices;
        std::vector<float> glyphAdvances;
        std::vector<DWRITE_GLYPH_OFFSET> glyphOffsets;
    };

    // Text decoration (underline / strikethrough)
    struct TextDecoration {
        float x, y;      // top-left of the decoration line
        float width;      // horizontal extent
        float thickness;  // line thickness
        bool isStrikethrough; // false = underline, true = strikethrough
    };

    std::vector<GlyphRun> runs;
    std::vector<TextDecoration> decorations;

    float dpiScale = 1.0f;  // currently unused — ppd fixed at 1.0

    // IUnknown — stack-allocated, ref counting is a no-op.
    // DirectWrite's Draw() is synchronous and does not retain the renderer.
    ULONG STDMETHODCALLTYPE AddRef() override { return 1; }
    ULONG STDMETHODCALLTYPE Release() override { return 1; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** obj) override {
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IDWriteTextRenderer) || riid == __uuidof(IDWritePixelSnapping)) {
            *obj = this;
            return S_OK;
        }
        *obj = nullptr;
        return E_NOINTERFACE;
    }

    // IDWritePixelSnapping — pixel snapping is DISABLED so glyph advances stay
    // fractional (continuous), keeping horizontal text positioning and scale
    // animation smooth instead of quantizing inter-glyph spacing to integer DIP
    // boundaries.  Sub-pixel ClearType sharpness is unaffected: it is produced by
    // a separate path (CreateGlyphRunAnalysis with quantized offsets) that is
    // retained.
    HRESULT STDMETHODCALLTYPE IsPixelSnappingDisabled(void*, BOOL* disabled) override { *disabled = TRUE; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetCurrentTransform(void*, DWRITE_MATRIX* transform) override {
        *transform = { 1, 0, 0, 1, 0, 0 };
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetPixelsPerDip(void*, FLOAT* ppd) override { *ppd = 1.0f; return S_OK; }

    // IDWriteTextRenderer
    HRESULT STDMETHODCALLTYPE DrawGlyphRun(void*, FLOAT baselineOriginX, FLOAT baselineOriginY,
        DWRITE_MEASURING_MODE, const DWRITE_GLYPH_RUN* glyphRun,
        const DWRITE_GLYPH_RUN_DESCRIPTION*, IUnknown*) override
    {
        GlyphRun run;
        run.fontFace = glyphRun->fontFace;  // ComPtr AddRef's automatically
        run.fontSize = glyphRun->fontEmSize;
        run.baselineX = baselineOriginX;
        run.baselineY = baselineOriginY;
        run.glyphIndices.assign(glyphRun->glyphIndices, glyphRun->glyphIndices + glyphRun->glyphCount);
        if (glyphRun->glyphAdvances)
            run.glyphAdvances.assign(glyphRun->glyphAdvances, glyphRun->glyphAdvances + glyphRun->glyphCount);
        if (glyphRun->glyphOffsets)
            run.glyphOffsets.assign(glyphRun->glyphOffsets, glyphRun->glyphOffsets + glyphRun->glyphCount);
        runs.push_back(std::move(run));
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE DrawUnderline(void*, FLOAT baselineOriginX, FLOAT baselineOriginY,
        const DWRITE_UNDERLINE* underline, IUnknown*) override
    {
        if (underline) {
            TextDecoration dec;
            dec.x = baselineOriginX;
            dec.y = baselineOriginY + underline->offset;
            dec.width = underline->width;
            dec.thickness = underline->thickness;
            dec.isStrikethrough = false;
            decorations.push_back(dec);
        }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE DrawStrikethrough(void*, FLOAT baselineOriginX, FLOAT baselineOriginY,
        const DWRITE_STRIKETHROUGH* strikethrough, IUnknown*) override
    {
        if (strikethrough) {
            TextDecoration dec;
            dec.x = baselineOriginX;
            dec.y = baselineOriginY + strikethrough->offset;
            dec.width = strikethrough->width;
            dec.thickness = strikethrough->thickness;
            dec.isStrikethrough = true;
            decorations.push_back(dec);
        }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE DrawInlineObject(void*, FLOAT, FLOAT, IDWriteInlineObject*, BOOL, BOOL, IUnknown*) override { return S_OK; }
};

// ============================================================================
// Construction / Initialization
// ============================================================================

VulkanGlyphAtlas::VulkanGlyphAtlas(IDWriteFactory* dwriteFactory)
    : dwriteFactory_(dwriteFactory)
{
    // Atlas bitmap is sized in Initialize() at kInitialAtlasDim and grown by
    // GrowAtlas() — no 64 MB up-front allocation.
}

VulkanGlyphAtlas::~VulkanGlyphAtlas() = default;

void VulkanGlyphAtlas::Reset()
{
    // Every cached glyph slot is about to be invalidated — bump the
    // generation so the resolved-glyph memo treats all its entries (which
    // hold now-stale atlas UVs) as misses.
    ++atlasGeneration_;
    jalium::text_stats::AddAtlasReset();
    cache_.clear();
    std::fill(atlasBitmap_.begin(), atlasBitmap_.end(), (uint8_t)0);
    packX_ = 0;
    packY_ = 0;
    rowHeight_ = 0;
    // The atlas bitmap contents (and possibly its identity, after a failed-grow
    // → Reset fallthrough) changed — tell the B4 uploader to re-create + re-fill
    // its VkImage, and mark the whole bitmap dirty.
    atlasRecreated_ = true;
    dirty_ = true;
    dirtyMinY_ = 0;
    dirtyMaxY_ = static_cast<uint16_t>(atlasH_);
}

bool VulkanGlyphAtlas::Initialize()
{
    atlasW_ = kInitialAtlasDim;
    atlasH_ = kInitialAtlasDim;
    atlasBitmap_.assign((size_t)atlasW_ * atlasH_ * 4, 0);  // RGBA = 4 bytes per pixel

    // Reserve cache buckets so a busy first frame doesn't rehash repeatedly.
    cache_.reserve(1024);
    instMap_.reserve(kInstCacheCap);

    if (!dwriteFactory_) return false;

    // Create GDI-compatible bitmap render target for glyph rasterization
    Microsoft::WRL::ComPtr<IDWriteGdiInterop> gdiInterop;
    if (FAILED(dwriteFactory_->GetGdiInterop(&gdiInterop)))
        return false;

    if (FAILED(gdiInterop->CreateBitmapRenderTarget(nullptr, 128, 128, &bitmapRenderTarget_)))
        return false;

    // Force 1:1 pixel mapping — we already scale fontSize by dpiScale_ ourselves,
    // so the render target must not apply an additional DPI scaling factor.
    bitmapRenderTarget_->SetPixelsPerDip(1.0f);

    // Cache IDWriteFactory3 for CreateGlyphRunAnalysis (ClearType path)
    dwriteFactory_->QueryInterface(IID_PPV_ARGS(&dwriteFactory3_));

    // Create custom rendering params for ClearType sub-pixel rendering.
    // clearTypeLevel = 1.0 enables full ClearType; RGBA atlas stores per-channel coverage.
    if (dwriteFactory3_) {
        Microsoft::WRL::ComPtr<IDWriteRenderingParams3> params3;
        dwriteFactory3_->CreateCustomRenderingParams(
            1.0f,              // gamma
            0.5f,              // enhancedContrast — slight boost for ClearType sharpness
            0.0f,              // grayscaleEnhancedContrast
            1.0f,              // clearTypeLevel = 1.0 enables full ClearType sub-pixel rendering
            DWRITE_PIXEL_GEOMETRY_RGB,
            DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC,
            DWRITE_GRID_FIT_MODE_ENABLED,
            &params3);
        if (params3) {
            params3->QueryInterface(IID_PPV_ARGS(&renderingParams_));
        }
    }
    if (!renderingParams_) {
        if (FAILED(dwriteFactory_->CreateRenderingParams(&renderingParams_)))
            return false;
    }

    initialized_ = true;
    return true;
}

// ============================================================================
// Atlas Packing (simple row-based)
// ============================================================================

bool VulkanGlyphAtlas::AllocateAtlasRect(uint16_t w, uint16_t h, uint16_t& outX, uint16_t& outY)
{
    // 1px padding to avoid sampling neighbors
    uint16_t pw = w + 2;
    uint16_t ph = h + 2;

    if (static_cast<uint32_t>(packX_) + pw > atlasW_) {
        // Move to next row
        packX_ = 0;
        packY_ += rowHeight_;
        rowHeight_ = 0;
    }

    if (static_cast<uint32_t>(packY_) + ph > atlasH_) {
        // Atlas is full at the current size.  We can't grow mid-batch because
        // that would change atlasW_/atlasH_ and make every UV the GenerateGlyphs
        // caller has already pushed point at the wrong coordinate.  Defer the
        // grow (or, if we're already at kMaxAtlasDim, the reset) to the next
        // BeginFrame, where ApplyPendingGrowthOrReset runs at the frame
        // boundary and the next frame's GenerateGlyphs sees consistent dims.
        if (atlasW_ < kMaxAtlasDim || atlasH_ < kMaxAtlasDim) {
            needsGrow_ = true;
            pendingGrowW_ = (std::max<uint32_t>)(pendingGrowW_, (uint32_t)(packX_ + pw));
            pendingGrowH_ = (std::max<uint32_t>)(pendingGrowH_, (uint32_t)(packY_ + ph));
        } else {
            needsReset_ = true;
        }
        return false;  // skip this glyph for now — it will be re-rasterized next frame
    }

    outX = packX_ + 1; // 1px padding
    outY = packY_ + 1;
    packX_ += pw;
    rowHeight_ = std::max(rowHeight_, ph);
    return true;
}

bool VulkanGlyphAtlas::GrowAtlas(uint32_t reqW, uint32_t reqH)
{
    uint32_t newW = atlasW_;
    uint32_t newH = atlasH_;
    while (newW < reqW && newW < kMaxAtlasDim) newW *= 2;
    while (newH < reqH && newH < kMaxAtlasDim) newH *= 2;
    if (newW > kMaxAtlasDim) newW = kMaxAtlasDim;
    if (newH > kMaxAtlasDim) newH = kMaxAtlasDim;
    if (newW == atlasW_ && newH == atlasH_) {
        return false;  // already at max — caller must fall back
    }

    // Reallocate the CPU bitmap, copying preserved rows pixel-aligned.
    std::vector<uint8_t> newBitmap((size_t)newW * newH * 4, 0);
    const size_t oldRowBytes = (size_t)atlasW_ * 4;
    const size_t newRowBytes = (size_t)newW * 4;
    for (uint32_t y = 0; y < atlasH_; ++y) {
        memcpy(newBitmap.data() + (size_t)y * newRowBytes,
               atlasBitmap_.data() + (size_t)y * oldRowBytes,
               oldRowBytes);
    }

    atlasBitmap_ = std::move(newBitmap);
    atlasW_ = newW;
    atlasH_ = newH;
    // Atlas dimensions changed → every cached UV (entry.x*invW etc.) is now
    // wrong. Bump generation so the resolved-glyph memo rebuilds.
    ++atlasGeneration_;
    // The CPU bitmap was reallocated at a new size — the B4 uploader must
    // recreate its VkImage and re-upload everything.
    atlasRecreated_ = true;

    // The full atlas needs reupload — old rows preserved data is on the CPU
    // bitmap, but the new (B4-owned) GPU image will be freshly created and empty.
    dirty_ = true;
    dirtyMinY_ = 0;
    dirtyMaxY_ = static_cast<uint16_t>((std::min<uint32_t>)(atlasH_, UINT16_MAX));
    return true;
}

void VulkanGlyphAtlas::ApplyPendingGrowthOrReset()
{
    if (needsGrow_) {
        uint32_t reqW = pendingGrowW_ ? pendingGrowW_ : atlasW_ * 2;
        uint32_t reqH = pendingGrowH_ ? pendingGrowH_ : atlasH_ * 2;
        if (!GrowAtlas(reqW, reqH)) {
            // GrowAtlas failed (allocation failure or already at max).  If we
            // never got off kMaxAtlasDim, fall through to a full reset so this
            // frame at least has a usable empty atlas.
            Reset();
        }
        needsGrow_ = false;
        // A successful grow supersedes any pending reset that RasterizeGlyph also
        // flagged while the atlas was full (it sets needsReset_ on every dropped
        // glyph). Without clearing it, the frame AFTER growth converges would see
        // needsReset_ still true and wastefully Reset()+refill the just-grown
        // atlas. Growing already reclaimed the space, so drop the stale reset.
        needsReset_ = false;
        pendingGrowW_ = 0;
        pendingGrowH_ = 0;
    } else if (needsReset_) {
        Reset();
        needsReset_ = false;
    }
}

// ============================================================================
// B4 GPU-uploader consume points (CPU-only — no Vulkan API here)
// ============================================================================

bool VulkanGlyphAtlas::TakeDirtyBand(uint16_t& outMinY, uint16_t& outMaxY)
{
    if (!dirty_) return false;

    // Clamp / normalize the band exactly like D3D12 FlushToGpu did before its
    // upload: an inverted or empty band means "the whole atlas was dirtied"
    // (e.g. after Reset / GrowAtlas), so hand back the full height.
    uint16_t minY = dirtyMinY_;
    uint16_t maxY = (uint16_t)(std::min<uint32_t>)((uint32_t)dirtyMaxY_, atlasH_);
    if (minY >= maxY) {
        minY = 0;
        maxY = (uint16_t)(std::min<uint32_t>)(atlasH_, UINT16_MAX);
    }
    outMinY = minY;
    outMaxY = maxY;

    dirty_ = false;
    dirtyMinY_ = UINT16_MAX;
    dirtyMaxY_ = 0;
    return true;
}

bool VulkanGlyphAtlas::ConsumeAtlasRecreated()
{
    if (!atlasRecreated_) return false;
    atlasRecreated_ = false;
    return true;
}

// ============================================================================
// Glyph Rasterization
// ============================================================================

int32_t VulkanGlyphAtlas::SyncAntialiasMode()
{
    uint64_t gen = jalium_text_get_antialias_generation();
    if (gen != lastAntialiasGen_) {
        lastAntialiasGen_ = gen;
        currentAntialiasMode_ = jalium::text_options::ResolveMode(
            jalium_text_get_global_antialias_mode());
        // Mode changed: existing atlas entries were rasterized with the old
        // rendering mode (e.g. ClearType R/G/B fringes) and cannot be reused
        // by the new mode (Grayscale needs R=G=B coverage). Reset on the next
        // frame boundary so the swap is one-shot rather than mixing fragments.
        needsReset_ = true;
    }
    return currentAntialiasMode_;
}

bool VulkanGlyphAtlas::RasterizeGlyph(const GlyphKey& key, GlyphEntry& entry)
{
    // Prefer the per-key mode captured at GenerateGlyphs time. Auto (0) is the
    // legacy "follow the process-wide setting" fallback for callers that
    // didn't fill the key (e.g. internal codepaths that build a GlyphKey
    // ad-hoc rather than going through GenerateGlyphs).
    int32_t aaMode = key.aaMode;
    if (aaMode == JALIUM_TEXT_AA_AUTO ||
        aaMode < JALIUM_TEXT_AA_AUTO || aaMode > JALIUM_TEXT_AA_CLEARTYPE) {
        aaMode = SyncAntialiasMode();
    }

    // Colour-emoji fast path: COLR/CPAL fonts (Segoe UI Emoji) publish multiple
    // layers per glyph that have to be composited in their authored colours.
    // RasterizeColorGlyph is STUBBED in B3 (returns false) so colour glyphs
    // simply fall through to the mono ClearType / Grayscale coverage path and
    // render as a black outline mask. B5a ports the colour path.
    if (RasterizeColorGlyph(key, entry)) {
        return true;
    }

    DWRITE_GLYPH_RUN glyphRun = {};
    glyphRun.fontFace = key.fontFace;
    glyphRun.fontEmSize = (float)key.fontSize;
    glyphRun.glyphCount = 1;
    glyphRun.glyphIndices = &key.glyphIndex;

    // ── Primary path: IDWriteGlyphRunAnalysis ──
    // Use CLEARTYPE_3x1 alpha texture for ClearType mode, ALIASED_1x1 for
    // Grayscale / Aliased. The alpha texture format dictates whether DirectWrite
    // emits per-channel sub-pixel fringes or a single grayscale coverage value.
    if (dwriteFactory3_) {
        // Sub-pixel X offset: the glyph's appearance depends on where it falls
        // relative to the physical pixel grid.  We rasterize at the quantized
        // sub-pixel offset so each cached variant matches a specific position.
        // 1/8-pixel buckets (must match the subpixelQuant denominator above).
        float subpixelOffset = key.subpixelX / 8.0f;

        // Ask the font face which rendering mode and grid-fit policy is right
        // for this em size.  Hard-coding GRID_FIT_MODE_ENABLED forces every
        // horizontal stem onto the pixel grid; in Bold weights at typical UI
        // sizes that snaps the middle bar of an 'e' onto the same row as the
        // top/bottom curves, so the bar disappears and the glyph reads as a
        // 'c'.  Solid block characters lose their interior fill the same way.
        // The font's gasp table knows when grid fitting is safe per size.
        DWRITE_RENDERING_MODE1 renderingMode = DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC;
        DWRITE_GRID_FIT_MODE gridFitMode = DWRITE_GRID_FIT_MODE_DEFAULT;
        bool useGdiFallback = false;

        // TextHintingMode override (WPF TextOptions.TextHintingMode):
        //   1=Fixed    → force grid fit on so the glyph is crisp every frame
        //   2=Animated → force grid fit off so the glyph stays smooth as it
        //                slides sub-pixel through a storyboard (otherwise the
        //                hinted stems pop every time penX crosses a pixel)
        //   0=Auto     → let GetRecommendedRenderingMode below pick (which
        //                consults the font's gasp table for the right answer
        //                at this em size)
        const uint8_t hint = key.hintingMode;
        if (hint == 1) {
            gridFitMode = DWRITE_GRID_FIT_MODE_ENABLED;
        } else if (hint == 2) {
            gridFitMode = DWRITE_GRID_FIT_MODE_DISABLED;
        }

        // For pure Aliased (bilevel) we route through GDI: CreateGlyphRunAnalysis
        // can't produce an ALIASED texture without a matching rendering mode and
        // the legacy GDI path is the established source of bilevel glyphs.
        if (aaMode == JALIUM_TEXT_AA_ALIASED) {
            useGdiFallback = true;
        }

        Microsoft::WRL::ComPtr<IDWriteFontFace3> fontFace3;
        if (!useGdiFallback &&
            SUCCEEDED(key.fontFace->QueryInterface(IID_PPV_ARGS(&fontFace3))) && fontFace3) {
            DWRITE_RENDERING_MODE1 recMode = DWRITE_RENDERING_MODE1_DEFAULT;
            DWRITE_GRID_FIT_MODE recGridFit = DWRITE_GRID_FIT_MODE_DEFAULT;
            HRESULT recHr = fontFace3->GetRecommendedRenderingMode(
                (float)key.fontSize,
                96.0f, 96.0f,                         // pixelsPerDip = 1.0
                nullptr,                              // no transform
                FALSE,                                // not sideways
                DWRITE_OUTLINE_THRESHOLD_ANTIALIASED,
                DWRITE_MEASURING_MODE_NATURAL,
                renderingParams_.Get(),
                &recMode,
                &recGridFit);
            if (SUCCEEDED(recHr)) {
                // CreateGlyphRunAnalysis cannot consume DEFAULT, ALIASED, or
                // OUTLINE — those have to be drawn through the GDI path.
                if (recMode == DWRITE_RENDERING_MODE1_OUTLINE ||
                    recMode == DWRITE_RENDERING_MODE1_ALIASED ||
                    recMode == DWRITE_RENDERING_MODE1_DEFAULT) {
                    useGdiFallback = true;
                } else {
                    renderingMode = recMode;
                    // Only adopt the recommended grid-fit policy when the
                    // caller didn't pin an explicit Fixed / Animated mode —
                    // otherwise WPF's TextOptions.TextHintingMode override
                    // would silently lose to the font's gasp-table hint.
                    if (hint == 0) {
                        gridFitMode = recGridFit;
                    }
                }
            }
        }

        const bool grayscale = (aaMode == JALIUM_TEXT_AA_GRAYSCALE);
        const DWRITE_TEXT_ANTIALIAS_MODE dwriteAaMode = grayscale
            ? DWRITE_TEXT_ANTIALIAS_MODE_GRAYSCALE
            : DWRITE_TEXT_ANTIALIAS_MODE_CLEARTYPE;
        const DWRITE_TEXTURE_TYPE textureType = grayscale
            ? DWRITE_TEXTURE_ALIASED_1x1
            : DWRITE_TEXTURE_CLEARTYPE_3x1;
        const size_t bytesPerPixel = grayscale ? 1u : 3u;

        Microsoft::WRL::ComPtr<IDWriteGlyphRunAnalysis> analysis;
        // Per-axis transform (liquid-glass deform etc.): rasterize the glyph
        // ALREADY-deformed (scaleX wide, scaleY tall) so it stays crisp displayed
        // 1:1 — no point-minification of an isotropic atlas erasing thin stems
        // (d->c, r->l). 8/8 == 1.0x -> identity -> nullptr (normal text path).
        const DWRITE_MATRIX glyphXform {
            key.scaleXQ / (float)kGlyphScaleQuant, 0.0f, 0.0f, key.scaleYQ / (float)kGlyphScaleQuant, 0.0f, 0.0f
        };
        const bool hasGlyphXform = (key.scaleXQ != (uint8_t)kGlyphScaleQuant ||
                                    key.scaleYQ != (uint8_t)kGlyphScaleQuant);
        HRESULT hr = useGdiFallback ? E_FAIL : dwriteFactory3_->CreateGlyphRunAnalysis(
            &glyphRun,
            hasGlyphXform ? &glyphXform : nullptr,
            renderingMode,
            DWRITE_MEASURING_MODE_NATURAL,
            gridFitMode,
            dwriteAaMode,
            subpixelOffset, 0.0f,  // sub-pixel X offset baked into bounds
            &analysis);

        if (SUCCEEDED(hr) && analysis) {
            // Get exact pixel bounds for the chosen texture type.
            RECT bounds = {};
            hr = analysis->GetAlphaTextureBounds(textureType, &bounds);
            if (FAILED(hr)) { entry.valid = false; return true; }

            int glyphW = bounds.right - bounds.left;
            int glyphH = bounds.bottom - bounds.top;

            if (glyphW <= 0 || glyphH <= 0) { entry.valid = false; return true; }
            if (glyphW > 512 || glyphH > 512) { entry.valid = false; return true; }

            // Grayscale path: 1 byte/pixel of coverage; ClearType: 3 bytes/pixel.
            UINT32 bufferSize = (UINT32)((size_t)glyphW * glyphH * bytesPerPixel);
            std::vector<uint8_t> alphaValues(bufferSize);
            hr = analysis->CreateAlphaTexture(textureType,
                &bounds, alphaValues.data(), bufferSize);
            if (FAILED(hr)) { entry.valid = false; return false; }

            uint16_t atlasX, atlasY;
            if (!AllocateAtlasRect((uint16_t)glyphW, (uint16_t)glyphH, atlasX, atlasY)) {
                needsReset_ = true;
                entry.valid = false;
                return true;
            }

            // Copy alpha-coverage data into the shared RGBA atlas. For Grayscale
            // the single coverage byte is replicated across R/G/B so the existing
            // shader (which multiplies atlas.rgb by the source colour and adds the
            // alpha channel) renders crisp grayscale text with no further branch.
            for (int y = 0; y < glyphH; y++) {
                if ((uint32_t)(atlasY + y) >= atlasH_) break;
                for (int x = 0; x < glyphW; x++) {
                    if ((uint32_t)(atlasX + x) >= atlasW_) break;
                    const uint8_t* src = alphaValues.data() + ((size_t)y * glyphW + x) * bytesPerPixel;
                    size_t atlasOffset = ((size_t)(atlasY + y) * atlasW_ + (atlasX + x)) * 4;
                    if (grayscale) {
                        uint8_t cov = src[0];
                        atlasBitmap_[atlasOffset + 0] = cov;
                        atlasBitmap_[atlasOffset + 1] = cov;
                        atlasBitmap_[atlasOffset + 2] = cov;
                        atlasBitmap_[atlasOffset + 3] = cov;
                    } else {
                        atlasBitmap_[atlasOffset + 0] = src[0]; // R sub-pixel coverage
                        atlasBitmap_[atlasOffset + 1] = src[1]; // G sub-pixel coverage
                        atlasBitmap_[atlasOffset + 2] = src[2]; // B sub-pixel coverage
                        atlasBitmap_[atlasOffset + 3] = std::max(std::max(src[0], src[1]), src[2]);
                    }
                }
            }

            entry.x = atlasX;
            entry.y = atlasY;
            entry.w = (uint16_t)glyphW;
            entry.h = (uint16_t)glyphH;
            // bounds.left/top are pixel offsets from baseline origin (0,0)
            entry.bearingX = (int16_t)bounds.left;
            entry.bearingY = (int16_t)(-bounds.top);  // top is negative (above baseline)
            entry.valid = true;

            dirty_ = true;
            dirtyMinY_ = std::min(dirtyMinY_, atlasY);
            dirtyMaxY_ = std::max(dirtyMaxY_, (uint16_t)(atlasY + glyphH));
            return true;
        }
    }

    // ── Fallback: GDI bitmap render target ──
    DWRITE_GLYPH_METRICS metrics;
    if (FAILED(key.fontFace->GetDesignGlyphMetrics(&key.glyphIndex, 1, &metrics, FALSE)))
        return false;

    DWRITE_FONT_METRICS fontMetrics;
    key.fontFace->GetMetrics(&fontMetrics);

    float scale = (float)key.fontSize / fontMetrics.designUnitsPerEm;
    int glyphW = (int)std::ceil((metrics.advanceWidth - metrics.leftSideBearing - metrics.rightSideBearing) * scale) + 4;
    int glyphH = (int)std::ceil((metrics.advanceHeight - metrics.topSideBearing - metrics.bottomSideBearing) * scale) + 2;

    if (glyphW <= 0 || glyphH <= 0 || glyphW > 512 || glyphH > 512) {
        entry.valid = false;
        return true;
    }

    SIZE curSize = {};
    bitmapRenderTarget_->GetSize(&curSize);
    if (curSize.cx < glyphW || curSize.cy < glyphH) {
        bitmapRenderTarget_->Resize(std::max((UINT32)glyphW, (UINT32)curSize.cx),
                                     std::max((UINT32)glyphH, (UINT32)curSize.cy));
    }

    HDC hdc = bitmapRenderTarget_->GetMemoryDC();
    RECT clearRect = { 0, 0, glyphW, glyphH };
    FillRect(hdc, &clearRect, (HBRUSH)GetStockObject(BLACK_BRUSH));

    float subpixelOffset = key.subpixelX / 8.0f;  // 1/8-pixel buckets (match primary path)
    float originX = -(metrics.leftSideBearing * scale) + 2 + subpixelOffset;
    float originY = (metrics.verticalOriginY - metrics.topSideBearing) * scale + 1;

    bitmapRenderTarget_->DrawGlyphRun(originX, originY, DWRITE_MEASURING_MODE_NATURAL,
        &glyphRun, renderingParams_.Get(), RGB(255, 255, 255), nullptr);

    GdiFlush();

    std::vector<uint8_t> glyphPixels;
    if (!CopyDcRegionToBgraPixels(hdc, glyphW, glyphH, glyphPixels)) {
        entry.valid = false;
        return false;
    }

    uint16_t atlasX, atlasY;
    if (!AllocateAtlasRect((uint16_t)glyphW, (uint16_t)glyphH, atlasX, atlasY)) {
        needsReset_ = true;
        entry.valid = false;
        return true;
    }

    for (int y = 0; y < glyphH; y++) {
        if ((uint32_t)(atlasY + y) >= atlasH_) break;
        for (int x = 0; x < glyphW; x++) {
            if ((uint32_t)(atlasX + x) >= atlasW_) break;
            const uint8_t* pixel = glyphPixels.data() + (((size_t)y * glyphW) + x) * 4;
            size_t atlasOffset = ((size_t)(atlasY + y) * atlasW_ + (atlasX + x)) * 4;
            atlasBitmap_[atlasOffset + 0] = pixel[2]; // R (BGRA→RGBA)
            atlasBitmap_[atlasOffset + 1] = pixel[1]; // G
            atlasBitmap_[atlasOffset + 2] = pixel[0]; // B
            atlasBitmap_[atlasOffset + 3] = std::max(std::max(pixel[0], pixel[1]), pixel[2]);
        }
    }

    entry.x = atlasX;
    entry.y = atlasY;
    entry.w = (uint16_t)glyphW;
    entry.h = (uint16_t)glyphH;
    entry.bearingX = (int16_t)std::round(metrics.leftSideBearing * scale - 2);
    entry.bearingY = (int16_t)std::round((metrics.verticalOriginY - metrics.topSideBearing) * scale + 1);
    entry.valid = true;

    dirty_ = true;
    dirtyMinY_ = std::min(dirtyMinY_, atlasY);
    dirtyMaxY_ = std::max(dirtyMaxY_, (uint16_t)(atlasY + glyphH));

    return true;
}

// ============================================================================
// Colour-Emoji Rasterization (COLR / CPAL)
// ============================================================================
//
// STUB for B3: always declines so colour glyphs fall through to the mono
// coverage path in RasterizeGlyph (rendering a black outline mask). The full
// D3D12 implementation walks TranslateColorGlyphRun layers and decodes PNG /
// JPEG / TIFF strikes through WIC, which would pull WIC (wincodec.h) and the
// IDWriteFactory4 / colour-glyph COM surface into this translation unit.
//
// TODO(B5a): port color-glyph (COLR/CPAL + WIC) — deferred.
bool VulkanGlyphAtlas::RasterizeColorGlyph(const GlyphKey& key, GlyphEntry& entry)
{
    (void)key;
    (void)entry;
    return false;
}

// ============================================================================
// Generate Glyph Instances
// ============================================================================

uint64_t VulkanGlyphAtlas::HashInstanceKey(uint64_t layoutKey,
                                           float dpiScale,
                                           int32_t aaMode,
                                           int32_t hintingMode,
                                           float scaleX, float scaleY) noexcept
{
    uint64_t h = 0xCBF29CE484222325ull;  // FNV-1a 64-bit
    auto mix = [&h](const void* p, size_t n) {
        const uint8_t* b = static_cast<const uint8_t*>(p);
        for (size_t i = 0; i < n; ++i) { h ^= b[i]; h *= 0x100000001B3ull; }
    };
    mix(&layoutKey, sizeof(layoutKey));
    mix(&dpiScale, sizeof(dpiScale));
    // Per-axis transform scale changes the rasterized (deformed) bitmap + atlas
    // slots/UVs for the SAME layout, so it must key the memo: a run cached at one
    // deformation must not be served at another (it would be the wrong stretch).
    mix(&scaleX, sizeof(scaleX));
    mix(&scaleY, sizeof(scaleY));
    // Mode bits must enter the key — two text elements with the same
    // layoutKey but different TextRenderingMode/TextHintingMode would
    // otherwise hand back the cached run rasterized in the other element's
    // mode (e.g. ClearType glyphs served to a Grayscale element).
    //
    // Origin (originX/Y) is intentionally NOT in the key: cached instances
    // store quad positions in layout-local coords (Draw was issued with
    // origin (0, 0)), and emitRun adds the caller's origin when serving the
    // hit. That lets a scrolling / dragged text run reuse its shaped layout
    // across every screen position it visits within the cache lifetime.
    mix(&aaMode, sizeof(aaMode));
    mix(&hintingMode, sizeof(hintingMode));
    return h;
}

uint32_t VulkanGlyphAtlas::GenerateGlyphs(
    IDWriteTextLayout* layout,
    float originX, float originY,
    float colorR, float colorG, float colorB, float colorA,
    std::vector<VkGlyphInstance>& outInstances,
    std::vector<TextDecorationRect>* outDecorations,
    uint64_t layoutKey,
    int32_t aaMode,
    int32_t hintingMode,
    float scaleX,
    float scaleY)
{
    if (!layout || !initialized_) return 0;

    // Quantize per-axis transform scale to 1/kGlyphScaleQuant steps (value ==
    // kGlyphScaleQuant means 1.0x == identity raster == normal-text path). The
    // glyph is rasterized at the FINAL deformed pixel size via a DWRITE_MATRIX
    // (e.g. a liquid-glass squeeze scaleX≈0.63 -> a correctly-thin CRISP stem;
    // stretch scaleY≈2.17 -> tall crisp glyph), instead of point-minifying an
    // isotropic atlas (which dropped thin stems: d->c, r->l). Quantization bounds
    // the cache to a few buckets during a smooth drag; AddText post-scales the
    // quad by the ACTUAL scale, so the in-bucket residual is a small magnification
    // (finer quant -> smaller residual -> less thin-stroke shimmer during motion),
    // never an erased stroke. Near-1.0 scales fold to identity -> normal path.
    if (!(scaleX > 0.0f)) scaleX = 1.0f;   // NaN / degenerate guard
    if (!(scaleY > 0.0f)) scaleY = 1.0f;
    auto quantScale = [](float s) -> uint8_t {
        long q = std::lround(s * (float)kGlyphScaleQuant);
        if (q < 1)   q = 1;      // floor
        if (q > 255) q = 255;    // uint8 ceil; the 512px glyph cap guards bigger
        return (uint8_t)q;
    };
    const uint8_t scaleXQ = quantScale(scaleX);
    const uint8_t scaleYQ = quantScale(scaleY);
    const float   sxR = scaleXQ / (float)kGlyphScaleQuant;   // dequantized raster scale (matches the key)
    const float   syR = scaleYQ / (float)kGlyphScaleQuant;

    // Detect a runtime process-wide ClearType↔Grayscale swap. SyncAntialiasMode()
    // bumps needsReset_ when it spots a mode change so cached glyph pixels
    // from the old mode don't survive into the new mode's frame. With per-format
    // mode plumbing the atlas can now also hold (ClearType, Grayscale) entries
    // for the same glyph simultaneously — those are distinguished by GlyphKey,
    // so the reset is only needed for the global-mode swap path.
    int32_t globalMode = SyncAntialiasMode();

    // Resolve the effective rendering mode for THIS call. aaMode == 0 (Auto)
    // means the caller didn't override at the format level, so fall through to
    // the process-wide value. Anything explicit wins so an authoring-tool
    // panel can render its Grayscale text next to a ClearType chrome border in
    // the same frame.
    int32_t effectiveAaMode = (aaMode != 0) ? aaMode : globalMode;
    // Clamp to valid JALIUM_TEXT_AA_* range; out-of-band values would index
    // off the end of the rasterizer mode table downstream.
    if (effectiveAaMode < JALIUM_TEXT_AA_AUTO || effectiveAaMode > JALIUM_TEXT_AA_CLEARTYPE) {
        effectiveAaMode = globalMode;
    }
    // hintingMode is consumed as-is by RasterizeGlyph (0..2). Anything else is
    // also clamped to Auto so the GlyphKey field stays small and predictable.
    int32_t effectiveHintingMode = hintingMode;
    if (effectiveHintingMode < 0 || effectiveHintingMode > 2) {
        effectiveHintingMode = 0;
    }

    // Apply this call's premultiplied colour + screen-origin translation to a
    // colour-neutral, origin-relative run (cached or freshly built) and
    // append it to the caller's buffers. Decorations are always built so the
    // memo is correct even if outDecorations varies between calls; they're
    // only emitted when the caller asked for them.
    //
    // Origin handling: cached instances store quad positions in the layout's
    // own coordinate space (origin = (0, 0)) so the cache key can be
    // origin-independent — scrolling / dragging text doesn't invalidate the
    // memo just because its position moved. Emit translates each quad by
    // (originX, originY) here, so cache reuses work across every screen
    // location the same shaped run ever appears at.
    //
    // Colour-emoji entries are flagged in the cached run by colorR == -1 (a
    // value the regular premultiplied path could never produce — premultiplied
    // RGB is non-negative). We preserve the sentinel through emit and only
    // forward the foreground alpha so the shader can do a SrcOver pass on the
    // atlas's authored RGBA without tinting the emoji with Foreground.
    auto emitRun = [&](const CachedGlyphRun& run) -> uint32_t {
        const float pr = colorR * colorA, pg = colorG * colorA,
                    pb = colorB * colorA, pa = colorA;

        // Bulk-copy the colour-neutral, origin-relative instances into the
        // caller's buffer in one memcpy, then sweep the freshly appended
        // slice once to apply (origin translate + premultiplied colour). The
        // hit path used to do one push_back per glyph with a per-element
        // branch on the colour-emoji sentinel; resize+memcpy avoids the
        // hidden capacity / size bookkeeping per element, and the single
        // sweep folds the translate and colour writes into one cache-friendly
        // pass over a contiguous slice. VkGlyphInstance is trivially
        // copyable (POD floats), enforced by the static_assert on its
        // layout, so memcpy is well-defined.
        const size_t glyphN = run.instances.size();
        if (glyphN > 0) {
            const size_t base = outInstances.size();
            outInstances.resize(base + glyphN);
            std::memcpy(outInstances.data() + base,
                        run.instances.data(),
                        glyphN * sizeof(VkGlyphInstance));
            VkGlyphInstance* dst = outInstances.data() + base;
            for (size_t i = 0; i < glyphN; ++i) {
                VkGlyphInstance& gi = dst[i];
                gi.posX += originX;
                gi.posY += originY;
                if (gi.colorR < 0.0f) {
                    // Colour emoji: cached R/G/B are already (-1, 0, 0)
                    // sentinel — only alpha needs the per-call value so
                    // Foreground opacity still applies.
                    gi.colorA = pa;
                } else {
                    gi.colorR = pr; gi.colorG = pg;
                    gi.colorB = pb; gi.colorA = pa;
                }
            }
        }

        const size_t decoN = run.decos.size();
        if (outDecorations && decoN > 0) {
            const size_t dbase = outDecorations->size();
            outDecorations->resize(dbase + decoN);
            std::memcpy(outDecorations->data() + dbase,
                        run.decos.data(),
                        decoN * sizeof(TextDecorationRect));
            TextDecorationRect* ddst = outDecorations->data() + dbase;
            for (size_t i = 0; i < decoN; ++i) {
                TextDecorationRect& dr = ddst[i];
                dr.x += originX;
                dr.y += originY;
                dr.colorR = pr; dr.colorG = pg;
                dr.colorB = pb; dr.colorA = pa;
            }
            jalium::text_stats::AddEmittedDecorations(decoN);
        }
        jalium::text_stats::AddEmittedGlyphs(glyphN);
        return (uint32_t)glyphN;
    };

    // Cache hit: skip layout->Draw + the entire per-glyph atlas walk. The
    // generation guard rejects any entry built before a Reset()/GrowAtlas()
    // (its cached UVs would now point at the wrong atlas slots) so stale-UV
    // garbled text is impossible. Key is origin-independent — same shaped
    // run hits every frame regardless of where on screen it lands.
    if (layoutKey != 0) {
        const uint64_t ck = HashInstanceKey(layoutKey, dpiScale_,
                                            effectiveAaMode, effectiveHintingMode,
                                            sxR, syR);
        auto mit = instMap_.find(ck);
        if (mit != instMap_.end()) {
            if (mit->second->run.gen == atlasGeneration_) {
                instLru_.splice(instLru_.begin(), instLru_, mit->second);
                jalium::text_stats::AddInstanceHit();
                return emitRun(mit->second->run);
            }
            instLru_.erase(mit->second);   // stale generation → rebuild
            instMap_.erase(mit);
        }
        jalium::text_stats::AddInstanceMiss();
    }

    // Atlas overflow recycling happens at the real frame boundary inside the B4
    // render-target BeginFrame (see NeedsReset/ClearResetFlag pair).
    // Never reset here — AllocateAtlasRect's contract is that this flag is
    // consumed between frames.  Resetting mid-frame would invalidate the
    // UV coordinates of every VkGlyphInstance already emitted earlier in
    // this same frame, so those glyphs would sample atlas slots that have
    // since been rewritten by later glyphs in this call — the visible
    // symptom is characters displaying as random other characters or
    // overlapping ghosts wherever text appears.

    // Extract glyph runs from the text layout.
    // Pixel snapping is disabled in the collector — DirectWrite reports exact
    // layout positions, and we handle sub-pixel alignment ourselves.
    //
    // Draw origin is (0, 0): collector baselines come back in the layout's
    // own coordinate space. emitRun adds the caller's (originX, originY) so
    // a single shaped run can serve every screen position the same text
    // appears at, which is the whole point of dropping origin from the
    // cache key above. Sub-pixel ClearType still works — subpixelQuant is
    // computed from the layout-internal penX, baked into the GlyphKey, and
    // RasterizeGlyph emits one atlas slot per (glyph, quant) variant.
    GlyphRunCollector collector;
    layout->Draw(nullptr, &collector, 0.0f, 0.0f);

    CachedGlyphRun built;
    float invW = 1.0f / static_cast<float>(atlasW_);
    float invH = 1.0f / static_cast<float>(atlasH_);

    uint64_t glyphRasterHitsThisRun = 0;
    uint64_t glyphRasterMissesThisRun = 0;

    for (auto& run : collector.runs) {
        float penX = run.baselineX;
        // Rasterize at the BASE physical em = round(fontSize * dpiScale). The
        // transform scale is NOT folded into the em — instead the per-axis
        // deformation (sxR, syR) is applied as a DWRITE_MATRIX inside
        // RasterizeGlyph, so the cached bitmap is already the FINAL deformed shape
        // and stays crisp when displayed 1:1 (point sampling is exact at 1:1).
        // This is what fixes the anisotropic squeeze dropping thin stems (d->c):
        // a 0.63x-wide glyph is rasterized correctly thin, not minified away.
        // Advances/penX stay in base layout units; the quad geometry is divided by
        // the per-axis raster scale and AddText re-applies the actual transform.
        float scaledSize = run.fontSize * dpiScale_;
        if (scaledSize <= 0) continue;
        uint16_t fontSize = (uint16_t)std::round(scaledSize);
        if (fontSize < 1) fontSize = 1;

        float invDpi = 1.0f / dpiScale_;
        float invRasterX = 1.0f / sxR;   // map deformed-X atlas geometry back to base-DIP
        float invRasterY = 1.0f / syR;   // (AddText post-scales by the actual transform)
        const bool deformed = (scaleXQ != (uint8_t)kGlyphScaleQuant ||
                               scaleYQ != (uint8_t)kGlyphScaleQuant);

        for (uint32_t i = 0; i < run.glyphIndices.size(); i++) {
            // Apply DirectWrite glyph offsets (kerning adjustments, mark positioning, etc.)
            float offsetX = 0, offsetY = 0;
            if (i < run.glyphOffsets.size()) {
                offsetX = run.glyphOffsets[i].advanceOffset;
                offsetY = run.glyphOffsets[i].ascenderOffset;
            }

            // Compute pen position in physical pixels for sub-pixel ClearType.
            float penXPhysical = (penX + offsetX) * dpiScale_;
            float subpixelF = penXPhysical - std::floor(penXPhysical);
            // Quantize to 1/8 pixel (8 cached variants per glyph). At 1/4 pixel
            // the optical residual is up to 0.25px; being size-invariant it is a
            // visible fraction of a small advance (~10% of an 8px-font advance)
            // and reads as phantom inter-glyph spacing. 1/8 pixel halves it to
            // <=0.125px. Truncation is kept (rounding wraps at frac→1, which the
            // integer floor(penX) origin below cannot express).
            //
            // EXCEPTION: deformed text (any transform scale, e.g. liquid-glass
            // drag) uses a single phase (0). A moving deform changes penX every
            // frame, so per-phase variants would flood the atlas -> GrowAtlas churn
            // -> cached runs invalidated by the generation guard -> glyphs blink
            // in/out (the thin 'l' flickering). Sub-pixel phase is invisible under
            // deformation anyway, so one phase bounds the atlas to one entry/glyph.
            uint8_t subpixelQuant = deformed
                ? (uint8_t)0
                : (uint8_t)std::min((int)(subpixelF * 8.0f), 7);

            GlyphKey key;
            key.fontFace = run.fontFace.Get();
            key.glyphIndex = run.glyphIndices[i];
            key.fontSize = fontSize;
            key.subpixelX = subpixelQuant;
            key.scaleXQ = scaleXQ;   // per-axis deformation -> RasterizeGlyph DWRITE_MATRIX
            key.scaleYQ = scaleYQ;
            // The effective AA + hinting modes are baked into the key so the
            // same glyph rasterized in ClearType for one element doesn't get
            // re-emitted for a different element that asked for Grayscale or
            // Animated hinting — RasterizeGlyph reads them straight off the
            // key and skips the SyncAntialiasMode() fallback when set.
            key.aaMode = static_cast<uint8_t>(effectiveAaMode);
            key.hintingMode = static_cast<uint8_t>(effectiveHintingMode);

            auto it = cache_.find(key);
            if (it == cache_.end()) {
                GlyphCacheValue val = {};
                if (!RasterizeGlyph(key, val.entry)) {
                    ++glyphRasterMissesThisRun;
                    continue;
                }
                val.fontFaceRef = run.fontFace;
                it = cache_.emplace(key, std::move(val)).first;
                ++glyphRasterMissesThisRun;
            } else {
                ++glyphRasterHitsThisRun;
            }

            auto& entry = it->second.entry;
            if (entry.valid && entry.w > 0 && entry.h > 0) {
                // Position the glyph quad at the integer-pixel pen + bearing.
                // The pen term is base-DIP (penX was shaped at run.fontSize);
                // entry.* are the DEFORMED bitmap's physical px (rasterized via the
                // per-axis DWRITE_MATRIX sxR/syR), so divide X by sxR and Y by syR
                // to land the quad in base-DIP. AddText then post-scales the quad
                // by the ACTUAL transform, reproducing the on-screen deformed size
                // 1:1 against the already-deformed crisp bitmap.
                // 1:1 text snaps the pen to the integer pixel grid (the sub-pixel
                // phase is baked into the bitmap via subpixelX) for crisp stable
                // text. DEFORMED text keeps the pen CONTINUOUS (no floor): each
                // glyph would otherwise cross integer boundaries at a different
                // sub-pixel instant as the deform animates, so adjacent glyphs jump
                // at different times (the "left s still, right s moved" jitter).
                // The bilinear text PSO samples this continuous position smoothly.
                float penXForPos = deformed ? penXPhysical : std::floor(penXPhysical);
                float glyphX = penXForPos * invDpi + entry.bearingX * invDpi * invRasterX;
                float glyphY = run.baselineY - offsetY - entry.bearingY * invDpi * invRasterY;

                VkGlyphInstance inst;
                inst.posX = glyphX;
                inst.posY = glyphY;
                inst.sizeX = (float)entry.w * invDpi * invRasterX;
                inst.sizeY = (float)entry.h * invDpi * invRasterY;
                inst.uvMinX = entry.x * invW;
                inst.uvMinY = entry.y * invH;
                inst.uvMaxX = (entry.x + entry.w) * invW;
                inst.uvMaxY = (entry.y + entry.h) * invH;
                // Colour applied at emit so one cached run serves any colour.
                // Colour-emoji glyphs get a -1 sentinel in R so emit() and the
                // pixel shader can keep them out of the per-channel ClearType
                // dual-source path (which would tint the authored emoji palette
                // with the text Foreground).
                if (entry.isColor) {
                    inst.colorR = -1.0f;
                    inst.colorG = 0.0f;
                    inst.colorB = 0.0f;
                    inst.colorA = 0.0f;
                } else {
                    inst.colorR = inst.colorG = inst.colorB = inst.colorA = 0.0f;
                }
                built.instances.push_back(inst);
            }

            if (i < run.glyphAdvances.size())
                penX += run.glyphAdvances[i];
        }
    }

    // Build decorations unconditionally so the memo stays correct even if a
    // later same-key call passes outDecorations; emit gates on the pointer.
    for (auto& dec : collector.decorations) {
        TextDecorationRect rect;
        rect.x = dec.x;
        rect.y = dec.y;
        rect.width = dec.width;
        rect.thickness = std::max(dec.thickness, 1.0f);
        rect.colorR = rect.colorG = rect.colorB = rect.colorA = 0.0f;
        built.decos.push_back(rect);
    }

    jalium::text_stats::AddGlyphRasterHit(glyphRasterHitsThisRun);
    jalium::text_stats::AddGlyphRasterMiss(glyphRasterMissesThisRun);

    const uint32_t count = emitRun(built);

    if (layoutKey != 0) {
        built.gen = atlasGeneration_;
        const uint64_t ck = HashInstanceKey(layoutKey, dpiScale_,
                                            effectiveAaMode, effectiveHintingMode,
                                            sxR, syR);
        if (auto ex = instMap_.find(ck); ex != instMap_.end()) {
            instLru_.erase(ex->second);
            instMap_.erase(ex);
        }
        if (instLru_.size() >= kInstCacheCap) {
            instMap_.erase(instLru_.back().key);
            instLru_.pop_back();
            jalium::text_stats::AddInstanceEviction(1);
        }
        instLru_.push_front(InstNode{ ck, std::move(built) });
        instMap_[ck] = instLru_.begin();
    }
    return count;
}

} // namespace jalium

#endif // _WIN32
