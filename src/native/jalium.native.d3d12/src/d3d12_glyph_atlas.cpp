#include "d3d12_glyph_atlas.h"
#include <cstring>
#include <cmath>
#include <algorithm>

namespace jalium {

// ── Inline helpers replacing CD3DX12_* ──
static D3D12_HEAP_PROPERTIES MakeHeapProps(D3D12_HEAP_TYPE type) {
    D3D12_HEAP_PROPERTIES hp = {};
    hp.Type = type;
    return hp;
}

static D3D12_RESOURCE_DESC MakeTex2DDesc(DXGI_FORMAT fmt, UINT64 w, UINT h) {
    D3D12_RESOURCE_DESC rd = {};
    rd.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    rd.Width = w;
    rd.Height = h;
    rd.DepthOrArraySize = 1;
    rd.MipLevels = 1;
    rd.Format = fmt;
    rd.SampleDesc.Count = 1;
    rd.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    return rd;
}

static D3D12_RESOURCE_DESC MakeBufferDesc(UINT64 size) {
    D3D12_RESOURCE_DESC rd = {};
    rd.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    rd.Width = size;
    rd.Height = 1;
    rd.DepthOrArraySize = 1;
    rd.MipLevels = 1;
    rd.SampleDesc.Count = 1;
    rd.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    return rd;
}

static D3D12_RESOURCE_BARRIER MakeTransitionBarrier(ID3D12Resource* res, D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after) {
    D3D12_RESOURCE_BARRIER b = {};
    b.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    b.Transition.pResource = res;
    b.Transition.StateBefore = before;
    b.Transition.StateAfter = after;
    b.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    return b;
}

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
        ComPtr<IDWriteFontFace> fontFace;  // prevent dangling pointer via AddRef
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

    // IDWritePixelSnapping — enable snapping at 96 DPI (ppd = 1.0) so glyph
    // advances are rounded to integer DIP boundaries.  This produces crisp,
    // consistent character spacing because every glyph lands on the same
    // sub-pixel grid.  Sub-pixel ClearType rendering is then handled by our
    // own pipeline (CreateGlyphRunAnalysis with quantized offsets).
    HRESULT STDMETHODCALLTYPE IsPixelSnappingDisabled(void*, BOOL* disabled) override { *disabled = FALSE; return S_OK; }
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

D3D12GlyphAtlas::D3D12GlyphAtlas(ID3D12Device* device, IDWriteFactory* dwriteFactory)
    : device_(device), dwriteFactory_(dwriteFactory)
{
    // Atlas bitmap is sized in Initialize() at kInitialAtlasDim and grown by
    // GrowAtlas() — no 64 MB up-front allocation.
}

D3D12GlyphAtlas::~D3D12GlyphAtlas() = default;

void D3D12GlyphAtlas::Reset()
{
    cache_.clear();
    std::fill(atlasBitmap_.begin(), atlasBitmap_.end(), (uint8_t)0);
    packX_ = 0;
    packY_ = 0;
    rowHeight_ = 0;
    dirty_ = true;
    dirtyMinY_ = 0;
    dirtyMaxY_ = static_cast<uint16_t>(atlasH_);
}

bool D3D12GlyphAtlas::Initialize()
{
    atlasW_ = kInitialAtlasDim;
    atlasH_ = kInitialAtlasDim;
    atlasBitmap_.assign((size_t)atlasW_ * atlasH_ * 4, 0);  // RGBA = 4 bytes per pixel

    // Create atlas texture (R8G8B8A8_UNORM for ClearType sub-pixel coverage, GPU default heap)
    auto texDesc = MakeTex2DDesc(DXGI_FORMAT_R8G8B8A8_UNORM, atlasW_, atlasH_);
    auto defaultHeap = MakeHeapProps(D3D12_HEAP_TYPE_DEFAULT);

    if (FAILED(device_->CreateCommittedResource(
            &defaultHeap, D3D12_HEAP_FLAG_NONE, &texDesc,
            D3D12_RESOURCE_STATE_COMMON, nullptr,
            IID_PPV_ARGS(&atlasTexture_))))
        return false;

    // Upload buffer for atlas updates — size = width * height * 4 (RGBA = 4 bytes per pixel)
    // Add row alignment padding (D3D12 requires 256-byte row pitch alignment)
    UINT rowPitch = (atlasW_ * 4 + 255) & ~255u;
    UINT64 uploadSize = (UINT64)rowPitch * atlasH_;
    auto uploadHeap = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
    auto uploadDesc = MakeBufferDesc(uploadSize);

    if (FAILED(device_->CreateCommittedResource(
            &uploadHeap, D3D12_HEAP_FLAG_NONE, &uploadDesc,
            D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
            IID_PPV_ARGS(&uploadBuffer_))))
        return false;

    // Create GDI-compatible bitmap render target for glyph rasterization
    ComPtr<IDWriteGdiInterop> gdiInterop;
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
        ComPtr<IDWriteRenderingParams3> params3;
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

bool D3D12GlyphAtlas::AllocateAtlasRect(uint16_t w, uint16_t h, uint16_t& outX, uint16_t& outY)
{
    // 1px padding to avoid sampling neighbors
    uint16_t pw = w + 2;
    uint16_t ph = h + 2;

    if (packX_ + pw > atlasW_) {
        // Move to next row
        packX_ = 0;
        packY_ += rowHeight_;
        rowHeight_ = 0;
    }

    if (packY_ + ph > atlasH_) {
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

bool D3D12GlyphAtlas::GrowAtlas(uint32_t reqW, uint32_t reqH)
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

    // Reallocate the CPU shadow, copying preserved rows pixel-aligned.
    std::vector<uint8_t> newBitmap((size_t)newW * newH * 4, 0);
    const size_t oldRowBytes = (size_t)atlasW_ * 4;
    const size_t newRowBytes = (size_t)newW * 4;
    for (uint32_t y = 0; y < atlasH_; ++y) {
        memcpy(newBitmap.data() + (size_t)y * newRowBytes,
               atlasBitmap_.data() + (size_t)y * oldRowBytes,
               oldRowBytes);
    }

    // Recreate the GPU resources at the new size.  Caller is responsible for
    // the safety contract (no command list outstanding that references the
    // old atlasTexture_); see GrowAtlas comment in the header.
    auto texDesc = MakeTex2DDesc(DXGI_FORMAT_R8G8B8A8_UNORM, newW, newH);
    auto defaultHeap = MakeHeapProps(D3D12_HEAP_TYPE_DEFAULT);
    ComPtr<ID3D12Resource> newTex;
    if (FAILED(device_->CreateCommittedResource(
            &defaultHeap, D3D12_HEAP_FLAG_NONE, &texDesc,
            D3D12_RESOURCE_STATE_COMMON, nullptr,
            IID_PPV_ARGS(&newTex)))) {
        return false;
    }

    UINT rowPitch = (newW * 4 + 255) & ~255u;
    UINT64 uploadSize = (UINT64)rowPitch * newH;
    auto uploadHeap = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
    auto uploadDesc = MakeBufferDesc(uploadSize);
    ComPtr<ID3D12Resource> newUpload;
    if (FAILED(device_->CreateCommittedResource(
            &uploadHeap, D3D12_HEAP_FLAG_NONE, &uploadDesc,
            D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
            IID_PPV_ARGS(&newUpload)))) {
        return false;
    }

    atlasTexture_ = newTex;
    uploadBuffer_ = newUpload;
    atlasBitmap_ = std::move(newBitmap);
    atlasW_ = newW;
    atlasH_ = newH;
    atlasState_ = D3D12_RESOURCE_STATE_COMMON;

    // The full atlas needs reupload — old rows preserved data is on the CPU
    // shadow, but the new GPU texture is freshly created and empty.
    dirty_ = true;
    dirtyMinY_ = 0;
    dirtyMaxY_ = static_cast<uint16_t>((std::min<uint32_t>)(atlasH_, UINT16_MAX));
    return true;
}

void D3D12GlyphAtlas::ApplyPendingGrowthOrReset()
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
        pendingGrowW_ = 0;
        pendingGrowH_ = 0;
    } else if (needsReset_) {
        Reset();
        needsReset_ = false;
    }
}

// ============================================================================
// Glyph Rasterization
// ============================================================================

bool D3D12GlyphAtlas::RasterizeGlyph(const GlyphKey& key, GlyphEntry& entry)
{
    DWRITE_GLYPH_RUN glyphRun = {};
    glyphRun.fontFace = key.fontFace;
    glyphRun.fontEmSize = (float)key.fontSize;
    glyphRun.glyphCount = 1;
    glyphRun.glyphIndices = &key.glyphIndex;

    // ── Primary path: IDWriteGlyphRunAnalysis for accurate ClearType bounds ──
    if (dwriteFactory3_) {
        // Sub-pixel X offset: the glyph's appearance depends on where it falls
        // relative to the physical pixel grid.  We rasterize at the quantized
        // sub-pixel offset so each cached variant matches a specific position.
        float subpixelOffset = key.subpixelX / 4.0f;

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

        ComPtr<IDWriteFontFace3> fontFace3;
        if (SUCCEEDED(key.fontFace->QueryInterface(IID_PPV_ARGS(&fontFace3))) && fontFace3) {
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
                    gridFitMode = recGridFit;
                }
            }
        }

        ComPtr<IDWriteGlyphRunAnalysis> analysis;
        HRESULT hr = useGdiFallback ? E_FAIL : dwriteFactory3_->CreateGlyphRunAnalysis(
            &glyphRun,
            nullptr,  // no transform
            renderingMode,
            DWRITE_MEASURING_MODE_NATURAL,
            gridFitMode,
            DWRITE_TEXT_ANTIALIAS_MODE_CLEARTYPE,
            subpixelOffset, 0.0f,  // sub-pixel X offset baked into bounds
            &analysis);

        if (SUCCEEDED(hr) && analysis) {
            // Get exact pixel bounds including ClearType sub-pixel fringe
            RECT bounds = {};
            hr = analysis->GetAlphaTextureBounds(DWRITE_TEXTURE_CLEARTYPE_3x1, &bounds);
            if (FAILED(hr)) { entry.valid = false; return true; }

            int glyphW = bounds.right - bounds.left;
            int glyphH = bounds.bottom - bounds.top;

            if (glyphW <= 0 || glyphH <= 0) { entry.valid = false; return true; }
            if (glyphW > 512 || glyphH > 512) { entry.valid = false; return true; }

            // Get ClearType alpha texture (3 bytes per pixel: R, G, B coverage)
            UINT32 bufferSize = (UINT32)((size_t)glyphW * glyphH * 3);
            std::vector<uint8_t> alphaValues(bufferSize);
            hr = analysis->CreateAlphaTexture(DWRITE_TEXTURE_CLEARTYPE_3x1,
                &bounds, alphaValues.data(), bufferSize);
            if (FAILED(hr)) { entry.valid = false; return false; }

            uint16_t atlasX, atlasY;
            if (!AllocateAtlasRect((uint16_t)glyphW, (uint16_t)glyphH, atlasX, atlasY)) {
                needsReset_ = true;
                entry.valid = false;
                return true;
            }

            // Copy 3-byte-per-pixel ClearType data to RGBA atlas
            for (int y = 0; y < glyphH; y++) {
                if ((uint32_t)(atlasY + y) >= atlasH_) break;
                for (int x = 0; x < glyphW; x++) {
                    if ((uint32_t)(atlasX + x) >= atlasW_) break;
                    const uint8_t* src = alphaValues.data() + ((size_t)y * glyphW + x) * 3;
                    size_t atlasOffset = ((size_t)(atlasY + y) * atlasW_ + (atlasX + x)) * 4;
                    atlasBitmap_[atlasOffset + 0] = src[0]; // R sub-pixel coverage
                    atlasBitmap_[atlasOffset + 1] = src[1]; // G sub-pixel coverage
                    atlasBitmap_[atlasOffset + 2] = src[2]; // B sub-pixel coverage
                    atlasBitmap_[atlasOffset + 3] = std::max(std::max(src[0], src[1]), src[2]);
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

    float subpixelOffset = key.subpixelX / 4.0f;
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
// Generate Glyph Instances
// ============================================================================

uint32_t D3D12GlyphAtlas::GenerateGlyphs(
    IDWriteTextLayout* layout,
    float originX, float originY,
    float colorR, float colorG, float colorB, float colorA,
    std::vector<GlyphQuadInstance>& outInstances,
    std::vector<TextDecorationRect>* outDecorations)
{
    if (!layout || !initialized_) return 0;

    // Atlas overflow recycling happens at the real frame boundary inside
    // D3D12DirectRenderer::BeginFrame (see NeedsReset/ClearResetFlag pair).
    // Never reset here — AllocateAtlasRect's contract is that this flag is
    // consumed between frames.  Resetting mid-frame would invalidate the
    // UV coordinates of every GlyphQuadInstance already emitted earlier in
    // this same frame, so those glyphs would sample atlas slots that have
    // since been rewritten by later glyphs in this call — the visible
    // symptom is characters displaying as random other characters or
    // overlapping ghosts wherever text appears.

    // Extract glyph runs from the text layout.
    // Pixel snapping is disabled in the collector — DirectWrite reports exact
    // layout positions, and we handle sub-pixel alignment ourselves.
    GlyphRunCollector collector;
    layout->Draw(nullptr, &collector, originX, originY);

    uint32_t count = 0;
    float invW = 1.0f / static_cast<float>(atlasW_);
    float invH = 1.0f / static_cast<float>(atlasH_);

    for (auto& run : collector.runs) {
        float penX = run.baselineX;
        // Apply DPI scale to get physical pixel size for rasterization.
        // Do NOT quantize fontSize — it must match the size DirectWrite used
        // to compute glyph advances, otherwise spacing errors accumulate.
        float scaledSize = run.fontSize * dpiScale_;
        if (scaledSize <= 0) continue;
        uint16_t fontSize = (uint16_t)std::round(scaledSize);
        if (fontSize < 1) fontSize = 1;

        float invDpi = 1.0f / dpiScale_;

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
            // Quantize to 1/4 pixel (4 cached variants per glyph)
            uint8_t subpixelQuant = (uint8_t)std::min((int)(subpixelF * 4.0f), 3);

            GlyphKey key;
            key.fontFace = run.fontFace.Get();
            key.glyphIndex = run.glyphIndices[i];
            key.fontSize = fontSize;
            key.subpixelX = subpixelQuant;

            auto it = cache_.find(key);
            if (it == cache_.end()) {
                GlyphCacheValue val = {};
                if (!RasterizeGlyph(key, val.entry)) continue;
                val.fontFaceRef = run.fontFace;
                it = cache_.emplace(key, std::move(val)).first;
            }

            auto& entry = it->second.entry;
            if (entry.valid && entry.w > 0 && entry.h > 0) {
                // Position the glyph quad at the integer-pixel base + bearing.
                // bearingX (= bounds.left from ClearType analysis) includes the
                // sub-pixel offset baked in during rasterization.
                float glyphX = std::floor(penXPhysical) * invDpi + entry.bearingX * invDpi;
                float glyphY = run.baselineY - offsetY - entry.bearingY * invDpi;

                GlyphQuadInstance inst;
                inst.posX = glyphX;
                inst.posY = glyphY;
                inst.sizeX = (float)entry.w * invDpi;
                inst.sizeY = (float)entry.h * invDpi;
                inst.uvMinX = entry.x * invW;
                inst.uvMinY = entry.y * invH;
                inst.uvMaxX = (entry.x + entry.w) * invW;
                inst.uvMaxY = (entry.y + entry.h) * invH;
                inst.colorR = colorR * colorA; // premultiply
                inst.colorG = colorG * colorA;
                inst.colorB = colorB * colorA;
                inst.colorA = colorA;
                outInstances.push_back(inst);
                count++;
            }

            if (i < run.glyphAdvances.size())
                penX += run.glyphAdvances[i];
        }
    }

    // Output text decorations (underline/strikethrough) as rect primitives
    if (outDecorations && !collector.decorations.empty()) {
        for (auto& dec : collector.decorations) {
            TextDecorationRect rect;
            rect.x = dec.x;
            rect.y = dec.y;
            rect.width = dec.width;
            rect.thickness = std::max(dec.thickness, 1.0f);
            // Premultiply color
            rect.colorR = colorR * colorA;
            rect.colorG = colorG * colorA;
            rect.colorB = colorB * colorA;
            rect.colorA = colorA;
            outDecorations->push_back(rect);
        }
    }

    return count;
}

// ============================================================================
// GPU Upload
// ============================================================================

void D3D12GlyphAtlas::FlushToGpu(ID3D12GraphicsCommandList* cmdList)
{
    if (!dirty_ || !cmdList) return;

    // Transition atlas to copy dest
    if (atlasState_ != D3D12_RESOURCE_STATE_COPY_DEST) {
        auto barrier = MakeTransitionBarrier(atlasTexture_.Get(),
            atlasState_, D3D12_RESOURCE_STATE_COPY_DEST);
        cmdList->ResourceBarrier(1, &barrier);
        atlasState_ = D3D12_RESOURCE_STATE_COPY_DEST;
    }

    // Only upload the dirty region (dirtyMinY_ to dirtyMaxY_) instead of the full atlas.
    // This reduces upload bandwidth from ~16MB to just the modified rows.
    UINT uploadMinY = dirtyMinY_;
    UINT uploadMaxY = (std::min)((UINT)dirtyMaxY_, (UINT)atlasH_);
    if (uploadMinY >= uploadMaxY) {
        // Full atlas was dirtied (e.g. after Reset / GrowAtlas)
        uploadMinY = 0;
        uploadMaxY = atlasH_;
    }
    UINT uploadHeight = uploadMaxY - uploadMinY;

    UINT rowPitch = (atlasW_ * 4 + 255) & ~255u;  // RGBA = 4 bytes per pixel
    void* mapped = nullptr;
    HRESULT hr = uploadBuffer_->Map(0, nullptr, &mapped);
    if (FAILED(hr) || !mapped) {
        // Map failed — keep dirty flag so we retry next frame
        return;
    }
    // Only copy the dirty rows to the upload buffer
    UINT64 uploadRowOffset = (UINT64)uploadMinY * rowPitch;
    for (UINT y = 0; y < uploadHeight; y++) {
        memcpy((uint8_t*)mapped + uploadRowOffset + y * rowPitch,
               atlasBitmap_.data() + ((size_t)uploadMinY + y) * atlasW_ * 4,
               atlasW_ * 4);
    }
    uploadBuffer_->Unmap(0, nullptr);

    // Copy only the dirty region from upload buffer to texture
    D3D12_TEXTURE_COPY_LOCATION dst = {};
    dst.pResource = atlasTexture_.Get();
    dst.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    dst.SubresourceIndex = 0;

    D3D12_TEXTURE_COPY_LOCATION src = {};
    src.pResource = uploadBuffer_.Get();
    src.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
    src.PlacedFootprint.Offset = 0;
    src.PlacedFootprint.Footprint.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    src.PlacedFootprint.Footprint.Width = atlasW_;
    src.PlacedFootprint.Footprint.Height = atlasH_;
    src.PlacedFootprint.Footprint.Depth = 1;
    src.PlacedFootprint.Footprint.RowPitch = rowPitch;

    D3D12_BOX srcBox = {};
    srcBox.left = 0;
    srcBox.top = uploadMinY;
    srcBox.right = atlasW_;
    srcBox.bottom = uploadMaxY;
    srcBox.front = 0;
    srcBox.back = 1;

    cmdList->CopyTextureRegion(&dst, 0, uploadMinY, 0, &src, &srcBox);

    // Transition back to shader resource
    {
        auto barrier = MakeTransitionBarrier(atlasTexture_.Get(),
            D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        cmdList->ResourceBarrier(1, &barrier);
        atlasState_ = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    }

    dirty_ = false;
    dirtyMinY_ = UINT16_MAX;
    dirtyMaxY_ = 0;
}

} // namespace jalium
