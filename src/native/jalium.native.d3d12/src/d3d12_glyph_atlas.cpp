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

    /// DPI scale factor for pixel snapping.  Set to dpiScale_ (e.g. 1.5 for
    /// 144 DPI) so DirectWrite snaps glyph advances to the physical pixel grid
    /// instead of the coarse 96-DPI grid.  Without this, rounding errors
    /// accumulate and create visible gaps ("b utt on" instead of "button").
    float dpiScale = 1.0f;

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

    // IDWritePixelSnapping — report actual display DPI so glyph positions
    // snap to the physical pixel grid, not the 96-DPI logical grid.
    HRESULT STDMETHODCALLTYPE IsPixelSnappingDisabled(void*, BOOL* disabled) override { *disabled = FALSE; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetCurrentTransform(void*, DWRITE_MATRIX* transform) override {
        *transform = { 1, 0, 0, 1, 0, 0 };
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetPixelsPerDip(void*, FLOAT* ppd) override { *ppd = dpiScale; return S_OK; }

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
    atlasBitmap_.resize((size_t)kAtlasWidth * kAtlasHeight * 4, 0);  // RGBA = 4 bytes per pixel
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
    dirtyMaxY_ = kAtlasHeight;
}

bool D3D12GlyphAtlas::Initialize()
{
    // Create atlas texture (R8G8B8A8_UNORM for ClearType sub-pixel coverage, GPU default heap)
    auto texDesc = MakeTex2DDesc(DXGI_FORMAT_R8G8B8A8_UNORM, kAtlasWidth, kAtlasHeight);
    auto defaultHeap = MakeHeapProps(D3D12_HEAP_TYPE_DEFAULT);

    if (FAILED(device_->CreateCommittedResource(
            &defaultHeap, D3D12_HEAP_FLAG_NONE, &texDesc,
            D3D12_RESOURCE_STATE_COMMON, nullptr,
            IID_PPV_ARGS(&atlasTexture_))))
        return false;

    // Upload buffer for atlas updates — size = width * height * 4 (RGBA = 4 bytes per pixel)
    // Add row alignment padding (D3D12 requires 256-byte row pitch alignment)
    UINT rowPitch = (kAtlasWidth * 4 + 255) & ~255u;
    UINT64 uploadSize = (UINT64)rowPitch * kAtlasHeight;
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
            DWRITE_GRID_FIT_MODE_DISABLED,
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

    if (packX_ + pw > kAtlasWidth) {
        // Move to next row
        packX_ = 0;
        packY_ += rowHeight_;
        rowHeight_ = 0;
    }

    if (packY_ + ph > kAtlasHeight) {
        // Atlas full — signal reset for the NEXT frame.
        // We cannot reset mid-frame because already-generated glyph instances
        // reference current atlas UV coords. Resetting would invalidate them,
        // causing garbage rendering for the remainder of this frame.
        needsReset_ = true;
        return false;  // skip this glyph for now
    }

    outX = packX_ + 1; // 1px padding
    outY = packY_ + 1;
    packX_ += pw;
    rowHeight_ = std::max(rowHeight_, ph);
    return true;
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

        ComPtr<IDWriteGlyphRunAnalysis> analysis;
        HRESULT hr = dwriteFactory3_->CreateGlyphRunAnalysis(
            &glyphRun,
            nullptr,  // no transform
            DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC,
            DWRITE_MEASURING_MODE_NATURAL,
            DWRITE_GRID_FIT_MODE_DISABLED,
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
                if ((uint32_t)(atlasY + y) >= kAtlasHeight) break;
                for (int x = 0; x < glyphW; x++) {
                    if ((uint32_t)(atlasX + x) >= kAtlasWidth) break;
                    const uint8_t* src = alphaValues.data() + ((size_t)y * glyphW + x) * 3;
                    size_t atlasOffset = ((size_t)(atlasY + y) * kAtlasWidth + (atlasX + x)) * 4;
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
        if ((uint32_t)(atlasY + y) >= kAtlasHeight) break;
        for (int x = 0; x < glyphW; x++) {
            if ((uint32_t)(atlasX + x) >= kAtlasWidth) break;
            const uint8_t* pixel = glyphPixels.data() + (((size_t)y * glyphW) + x) * 4;
            size_t atlasOffset = ((size_t)(atlasY + y) * kAtlasWidth + (atlasX + x)) * 4;
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

    // Extract glyph runs from the text layout.
    // Pass dpiScale_ so DirectWrite snaps to the physical pixel grid.
    GlyphRunCollector collector;
    collector.dpiScale = dpiScale_;
    layout->Draw(nullptr, &collector, originX, originY);

    uint32_t count = 0;
    float invW = 1.0f / kAtlasWidth;
    float invH = 1.0f / kAtlasHeight;

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

            // Compute pen position in physical pixels to determine sub-pixel offset.
            // ClearType rendering is sensitive to horizontal sub-pixel position —
            // a glyph at x=10.0 looks different from one at x=10.33.
            float penXPhysical = (penX + offsetX) * dpiScale_;
            float subpixelF = penXPhysical - std::floor(penXPhysical);
            // Quantize to 1/4 pixel (4 cached variants per glyph — balances quality vs cache size)
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
                // The glyph was rasterized with sub-pixel offset baked in via
                // CreateGlyphRunAnalysis(baselineOriginX = subpixelQuant/4.0).
                // bounds.left already includes that offset, so position the quad
                // at the integer pixel boundary + bounds offset.
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
    UINT uploadMaxY = (std::min)((UINT)dirtyMaxY_, (UINT)kAtlasHeight);
    if (uploadMinY >= uploadMaxY) {
        // Full atlas was dirtied (e.g. after Reset)
        uploadMinY = 0;
        uploadMaxY = kAtlasHeight;
    }
    UINT uploadHeight = uploadMaxY - uploadMinY;

    UINT rowPitch = (kAtlasWidth * 4 + 255) & ~255u;  // RGBA = 4 bytes per pixel
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
               atlasBitmap_.data() + ((size_t)uploadMinY + y) * kAtlasWidth * 4,
               kAtlasWidth * 4);
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
    src.PlacedFootprint.Footprint.Width = kAtlasWidth;
    src.PlacedFootprint.Footprint.Height = kAtlasHeight;
    src.PlacedFootprint.Footprint.Depth = 1;
    src.PlacedFootprint.Footprint.RowPitch = rowPitch;

    D3D12_BOX srcBox = {};
    srcBox.left = 0;
    srcBox.top = uploadMinY;
    srcBox.right = kAtlasWidth;
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
