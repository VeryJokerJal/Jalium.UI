#include "d3d12_glyph_atlas.h"
#include "jalium_text_options.h"
#include "jalium_text_stats.h"
#include <wincodec.h>
#include <mutex>
#include <cstring>
#include <cmath>
#include <algorithm>

namespace jalium {

// Shared WIC factory used by RasterizeColorGlyph to decode PNG/JPEG/TIFF
// strikes embedded in colour-emoji fonts (Windows 11 Fluent Segoe UI Emoji).
// The atlas does not own the factory's lifetime — it lives in a function-local
// static so the first call lazily creates it and process teardown releases it
// after every D3D12GlyphAtlas instance has gone away.
static IWICImagingFactory* EnsureWicFactory()
{
    static std::mutex s_mutex;
    static ComPtr<IWICImagingFactory> s_factory;
    std::lock_guard<std::mutex> lock(s_mutex);
    if (!s_factory) {
        // CoInitializeEx is the host's responsibility (D3D12 backend Initialize
        // already runs MTA Init); CoCreateInstance on the apartment thread is
        // safe regardless of which thread first ends up here.
        ComPtr<IWICImagingFactory> factory;
        HRESULT hr = CoCreateInstance(
            CLSID_WICImagingFactory,
            nullptr,
            CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(factory.GetAddressOf()));
        if (FAILED(hr)) return nullptr;
        s_factory = std::move(factory);
    }
    return s_factory.Get();
}

// Decodes an in-memory PNG / JPEG / TIFF stream into 32bpp BGRA (premultiplied
// alpha so atlas blit is a straight memcpy that the colour-emoji shader path
// reads as authored). Returns false on any WIC failure — the caller falls
// through to leaving that layer empty.
static bool DecodeEmojiImageBytes(const void* bytes, uint32_t size,
                                  uint32_t& outW, uint32_t& outH,
                                  std::vector<uint8_t>& outPixels)
{
    if (!bytes || size == 0) return false;
    IWICImagingFactory* factory = EnsureWicFactory();
    if (!factory) return false;

    ComPtr<IWICStream> stream;
    if (FAILED(factory->CreateStream(stream.GetAddressOf()))) return false;
    if (FAILED(stream->InitializeFromMemory(
            const_cast<BYTE*>(static_cast<const BYTE*>(bytes)), size))) return false;

    ComPtr<IWICBitmapDecoder> decoder;
    if (FAILED(factory->CreateDecoderFromStream(stream.Get(), nullptr,
            WICDecodeMetadataCacheOnDemand, decoder.GetAddressOf()))) return false;

    ComPtr<IWICBitmapFrameDecode> frame;
    if (FAILED(decoder->GetFrame(0, frame.GetAddressOf()))) return false;

    UINT w = 0, h = 0;
    if (FAILED(frame->GetSize(&w, &h)) || w == 0 || h == 0) return false;

    ComPtr<IWICFormatConverter> conv;
    if (FAILED(factory->CreateFormatConverter(conv.GetAddressOf()))) return false;
    // 32bppPBGRA = pre-multiplied BGRA, lays out byte-for-byte the same as the
    // atlas (which is RGBA8_UNORM after a B↔R swap). We honour PNG's straight
    // alpha by asking WIC for premultiplied, which matches the SrcOver-style
    // shader path the colour-emoji branch uses.
    if (FAILED(conv->Initialize(frame.Get(),
            GUID_WICPixelFormat32bppPBGRA,
            WICBitmapDitherTypeNone, nullptr, 0.0,
            WICBitmapPaletteTypeCustom))) return false;

    const size_t stride = (size_t)w * 4;
    outPixels.assign(stride * h, 0);
    if (FAILED(conv->CopyPixels(nullptr, (UINT)stride, (UINT)outPixels.size(),
            outPixels.data()))) return false;

    outW = w;
    outH = h;
    return true;
}

// Box-filter downscale (or nearest-neighbour upscale) of a BGRA / RGBA buffer.
// Used to fit a colour-emoji strike rendered at, say, 64 ppem into the actual
// font size requested (commonly 14-20 px). A real bicubic would be sharper but
// box averaging is already much better than nearest, fast, and dependency-free.
static void ResampleBgraNearestBox(const uint8_t* src, uint32_t srcW, uint32_t srcH,
                                   std::vector<uint8_t>& dst, uint32_t dstW, uint32_t dstH)
{
    dst.assign((size_t)dstW * dstH * 4, 0);
    if (srcW == 0 || srcH == 0 || dstW == 0 || dstH == 0) return;

    const float xRatio = (float)srcW / (float)dstW;
    const float yRatio = (float)srcH / (float)dstH;

    if (dstW >= srcW && dstH >= srcH) {
        // Upscale: nearest neighbour (avoids haloing artefacts on emoji edges).
        for (uint32_t y = 0; y < dstH; ++y) {
            uint32_t sy = (uint32_t)std::min<uint32_t>((uint32_t)(y * yRatio), srcH - 1);
            for (uint32_t x = 0; x < dstW; ++x) {
                uint32_t sx = (uint32_t)std::min<uint32_t>((uint32_t)(x * xRatio), srcW - 1);
                const uint8_t* s = src + ((size_t)sy * srcW + sx) * 4;
                uint8_t* d = dst.data() + ((size_t)y * dstW + x) * 4;
                d[0] = s[0]; d[1] = s[1]; d[2] = s[2]; d[3] = s[3];
            }
        }
        return;
    }

    // Downscale: average over the src footprint of each dst pixel.
    for (uint32_t y = 0; y < dstH; ++y) {
        uint32_t sy0 = (uint32_t)std::floor(y * yRatio);
        uint32_t sy1 = (uint32_t)std::min<uint32_t>(srcH, (uint32_t)std::ceil((y + 1) * yRatio));
        if (sy1 <= sy0) sy1 = sy0 + 1;
        for (uint32_t x = 0; x < dstW; ++x) {
            uint32_t sx0 = (uint32_t)std::floor(x * xRatio);
            uint32_t sx1 = (uint32_t)std::min<uint32_t>(srcW, (uint32_t)std::ceil((x + 1) * xRatio));
            if (sx1 <= sx0) sx1 = sx0 + 1;
            uint32_t accB = 0, accG = 0, accR = 0, accA = 0, count = 0;
            for (uint32_t yy = sy0; yy < sy1; ++yy) {
                for (uint32_t xx = sx0; xx < sx1; ++xx) {
                    const uint8_t* s = src + ((size_t)yy * srcW + xx) * 4;
                    accB += s[0]; accG += s[1]; accR += s[2]; accA += s[3];
                    ++count;
                }
            }
            uint8_t* d = dst.data() + ((size_t)y * dstW + x) * 4;
            d[0] = (uint8_t)(accB / count);
            d[1] = (uint8_t)(accG / count);
            d[2] = (uint8_t)(accR / count);
            d[3] = (uint8_t)(accA / count);
        }
    }
}

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

D3D12GlyphAtlas::D3D12GlyphAtlas(ID3D12Device* device, IDWriteFactory* dwriteFactory, D3D12Backend* backend)
    : device_(device), dwriteFactory_(dwriteFactory), backend_(backend)
{
    // Atlas bitmap is sized in Initialize() at kInitialAtlasDim and grown by
    // GrowAtlas() — no 64 MB up-front allocation.
}

D3D12GlyphAtlas::~D3D12GlyphAtlas() = default;

void D3D12GlyphAtlas::Reset()
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
    atlasTexture_->SetName(L"JaliumGlyphAtlas");  // [JALIUM-921 diag]

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

    // Cache IDWriteFactory4 for TranslateColorGlyphRun (colour emoji path).
    // Available on Windows 10 1607+ — when absent, RasterizeColorGlyph reports
    // false and the caller falls through to the monochrome glyph path so
    // older systems still render emoji (as outlines), just without colour.
    dwriteFactory_->QueryInterface(IID_PPV_ARGS(&dwriteFactory4_));

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
    newTex->SetName(L"JaliumGlyphAtlas");  // [JALIUM-921 diag]

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

    // Hand the old atlas / upload buffers to the backend's fence-tracked
    // graveyard before swapping in the new ones. The previous-frame GPU
    // commands (running on the OTHER frame slot) still reference the old
    // atlas via SRV bindings — letting ComPtr::operator= drop the last ref
    // here triggers D3D12 ERROR #921 OBJECT_DELETED_WHILE_STILL_IN_USE on
    // the next command queue execute. The graveyard pins the resource at
    // refcount ≥ 1 and frees it only after its fence is GPU-complete.
    //
    // BeginFrame waits only the CURRENT slot's fence before calling
    // ApplyPendingGrowthOrReset; the OTHER slot's commands may still be in
    // flight, which is exactly the window where this race fires.
    if (backend_) {
        if (atlasTexture_) backend_->RetireGpuResource(std::move(atlasTexture_));
        if (uploadBuffer_) backend_->RetireGpuResource(std::move(uploadBuffer_));
    }

    atlasTexture_ = newTex;
    uploadBuffer_ = newUpload;
    atlasBitmap_ = std::move(newBitmap);
    atlasW_ = newW;
    atlasH_ = newH;
    atlasState_ = D3D12_RESOURCE_STATE_COMMON;
    // Atlas dimensions changed → every cached UV (entry.x*invW etc.) is now
    // wrong. Bump generation so the resolved-glyph memo rebuilds.
    ++atlasGeneration_;

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

int32_t D3D12GlyphAtlas::SyncAntialiasMode()
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

bool D3D12GlyphAtlas::RasterizeGlyph(const GlyphKey& key, GlyphEntry& entry)
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

    // Colour-emoji fast path: Segoe UI Emoji and other COLR/CPAL fonts publish
    // multiple layers per glyph that have to be composited in their authored
    // colours. The mono ClearType / Grayscale path below would only see the
    // outline-layer alpha and render a black mask — which is the original
    // issue. Detecting IsColorFont up front lets us dispatch to the multi-
    // layer rasterizer; on factory-4-less systems (Windows 10 < 1607) we
    // fall through and emoji at least appears as a black outline rather
    // than missing entirely.
    bool isColorFont = false;
    if (dwriteFactory4_) {
        ComPtr<IDWriteFontFace2> fontFace2;
        if (SUCCEEDED(key.fontFace->QueryInterface(IID_PPV_ARGS(&fontFace2))) &&
            fontFace2 && fontFace2->IsColorFont())
        {
            isColorFont = true;
            if (RasterizeColorGlyph(key, entry)) {
                return true;
            }
            // Colour path declined this specific glyph. For codepoints that
            // exist in BOTH the COLR set and as a plain outline glyph in the
            // same font (rare but legal — e.g. CombiningGraphemeJoiner inside
            // Segoe UI Emoji), the outline mono path below produces a useful
            // dingbat. For codepoints that exist ONLY as colour-bitmap layers
            // we failed to decode (SVG / COLR v1 paint-tree on older code
            // paths), the mono path renders an empty box — visible to the
            // user as a "blank, looks like it was clipped" glyph, which is
            // worse than skipping the glyph entirely because cache stores
            // it permanently. We mark the entry invalid + valid-cache-bit
            // false so GenerateGlyphs skips emission this frame and the
            // cache is repopulated next frame, giving DirectWrite another
            // chance to publish a usable layer (e.g. after a font update or
            // a strike load).
            DWRITE_GLYPH_METRICS gm{};
            if (SUCCEEDED(key.fontFace->GetDesignGlyphMetrics(&key.glyphIndex, 1, &gm, FALSE))) {
                DWRITE_FONT_METRICS fm{};
                key.fontFace->GetMetrics(&fm);
                const float fmScale = (float)key.fontSize / (float)fm.designUnitsPerEm;
                const int placeholderW = std::max(4, (int)std::round(gm.advanceWidth * fmScale));
                const int placeholderH = std::max(4, (int)std::round((fm.ascent + fm.descent) * fmScale * 0.7f));
                // Render a faint rounded grey square as a "this emoji could
                // not be rasterised" hint — better signal than an invisible
                // gap or a stray black mask. Pre-multiplied 30 % grey, full A.
                uint16_t ax, ay;
                if (AllocateAtlasRect((uint16_t)placeholderW, (uint16_t)placeholderH, ax, ay)) {
                    for (int y = 0; y < placeholderH; ++y) {
                        if ((uint32_t)(ay + y) >= atlasH_) break;
                        uint8_t* row = atlasBitmap_.data() + ((size_t)(ay + y) * atlasW_ + ax) * 4;
                        for (int x = 0; x < placeholderW; ++x) {
                            const bool border = (x == 0 || x == placeholderW - 1 ||
                                                 y == 0 || y == placeholderH - 1);
                            const uint8_t v = border ? 90 : 40;
                            row[x * 4 + 0] = v;
                            row[x * 4 + 1] = v;
                            row[x * 4 + 2] = v;
                            row[x * 4 + 3] = border ? 160 : 80;
                        }
                    }
                    entry.x = ax;
                    entry.y = ay;
                    entry.w = (uint16_t)placeholderW;
                    entry.h = (uint16_t)placeholderH;
                    entry.bearingX = 0;
                    entry.bearingY = (int16_t)placeholderH;
                    entry.valid = true;
                    entry.isColor = true;  // route through SrcOver, not ClearType
                    dirty_ = true;
                    dirtyMinY_ = std::min(dirtyMinY_, ay);
                    dirtyMaxY_ = std::max(dirtyMaxY_, (uint16_t)(ay + placeholderH));
                    return true;
                }
            }
            // Atlas allocation failed too — let the caller skip this glyph;
            // it gets retried on the next frame after the pending reset.
            entry.valid = false;
            return false;
        }
    }
    (void)isColorFont;

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

        ComPtr<IDWriteFontFace3> fontFace3;
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

        ComPtr<IDWriteGlyphRunAnalysis> analysis;
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
// TranslateColorGlyphRun decomposes a colour-font glyph run into a sequence of
// monochrome sub-runs, each tagged with the palette colour it should be drawn
// in. We rasterize every sub-run as a grayscale alpha mask (one channel of
// coverage) and accumulate `colour × coverage` into a shared RGBA scratch
// buffer using straight SrcOver, then copy the result into the atlas as
// premultiplied RGBA. The atlas entry is flagged isColor so the text VS/PS
// can sentinel-route it through the alpha-only blend path.
//
// We deliberately limit the desired image formats to COLR/CFF/TRUETYPE — adding
// bitmap formats (PNG/JPEG/TIFF) would mean copying decoded WIC bitmaps into
// the atlas as a separate code path, which is left as a follow-up for fonts
// that ship CBDT/sbix bitmap strikes (Segoe UI Emoji on Win10 is COLR, so the
// common case is covered).

// Debug trace controlled by the JALIUM_EMOJI_TRACE environment variable —
// dump glyph index, image format the layer enumerator reported, and whether
// each layer succeeded. Enables remote-diagnosing "this one emoji renders
// blank" cases without modifying source. Stays cheap (one env lookup per
// process) when off.
static bool EmojiTraceEnabled() {
    static int state = -1;
    if (state < 0) {
        char buf[8];
        DWORD n = GetEnvironmentVariableA("JALIUM_EMOJI_TRACE", buf, sizeof(buf));
        state = (n > 0 && buf[0] != '0') ? 1 : 0;
    }
    return state == 1;
}

bool D3D12GlyphAtlas::RasterizeColorGlyph(const GlyphKey& key, GlyphEntry& entry)
{
    if (!dwriteFactory4_) return false;

    DWRITE_GLYPH_RUN glyphRun = {};
    glyphRun.fontFace   = key.fontFace;
    glyphRun.fontEmSize = (float)key.fontSize;
    glyphRun.glyphCount = 1;
    glyphRun.glyphIndices = &key.glyphIndex;

    // 1/8-pixel buckets: the denominator must stay consistent with the
    // subpixelQuant *8 (clamp 7) store site in GenerateGlyphs and the two mono
    // bake sites' /8.0f. This offset is baked into baselineOrigin below, flows
    // through the layer union bounds into entry.bearingX, and ultimately into
    // the emitted glyphX — using /4.0f here shifts colour/emoji glyphs in
    // buckets 4..7 a full pixel right of the surrounding monochrome text.
    const float subpixelOffset = key.subpixelX / 8.0f;

    // Translate into per-layer sub-runs. DWRITE_E_NOCOLOR signals "this
    // specific glyph has no colour layers" — common when a colour font holds
    // both colour and outline glyphs; the caller will retry the mono path.
    //
    // Windows 11's Fluent Segoe UI Emoji is a hybrid font: classic Unicode
    // pictographs stay in COLR/CPAL, but the modern 3D emoji set is shipped
    // as PNG strikes (DWRITE_GLYPH_IMAGE_FORMATS_PNG / JPEG / TIFF / raw
    // PREMULTIPLIED_B8G8R8A8). If we omit those formats from the desired
    // mask, TranslateColorGlyphRun returns DWRITE_E_NOCOLOR for the
    // bitmap-only glyphs and we drop back to the monochrome mask — which is
    // exactly the "🍌 colour, 😄 colour, 🥹 colour, ⬛ black" pattern this
    // issue reported. Asking for every format DirectWrite can return lets
    // it dispatch each glyph to whichever storage the font author used.
    ComPtr<IDWriteColorGlyphRunEnumerator1> enumerator;
    const DWRITE_GLYPH_IMAGE_FORMATS desiredFormats =
        DWRITE_GLYPH_IMAGE_FORMATS_TRUETYPE |
        DWRITE_GLYPH_IMAGE_FORMATS_CFF |
        DWRITE_GLYPH_IMAGE_FORMATS_COLR |
        DWRITE_GLYPH_IMAGE_FORMATS_PNG |
        DWRITE_GLYPH_IMAGE_FORMATS_JPEG |
        DWRITE_GLYPH_IMAGE_FORMATS_TIFF |
        DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8;

    // D2D1_POINT_2F is the dcommon.h struct { x, y } — explicit aggregate-init
    // keeps us from having to drag d2d1helper.h into this TU just for Point2F.
    D2D1_POINT_2F baselineOrigin{};
    baselineOrigin.x = subpixelOffset;
    baselineOrigin.y = 0.0f;
    HRESULT hr = dwriteFactory4_->TranslateColorGlyphRun(
        baselineOrigin,
        &glyphRun,
        nullptr,
        desiredFormats,
        DWRITE_MEASURING_MODE_NATURAL,
        nullptr,
        /*colorPaletteIndex*/ 0,
        &enumerator);
    if (hr == DWRITE_E_NOCOLOR || FAILED(hr) || !enumerator) {
        if (EmojiTraceEnabled()) {
            char buf[160];
            sprintf_s(buf, "[jalium emoji] glyph=%u ppem=%u TranslateColorGlyphRun hr=0x%08lX %s\n",
                      key.glyphIndex, key.fontSize, hr,
                      hr == DWRITE_E_NOCOLOR ? "NOCOLOR" : "FAIL");
            OutputDebugStringA(buf);
        }
        return false;
    }

    // Build a scratch canvas large enough to hold every layer regardless of
    // how far above/below the baseline the font author placed it. Fluent
    // emoji often draw decorative elements (eyes, glints, drop shadows) well
    // past the design ascent / descent box — sizing canvas as `2 × fontSize`
    // and parking the baseline at canvas/2 + designAscent/2 (the previous
    // attempt) clipped those elements at small sizes, which is the visible
    // "rendered but blank" pattern this issue is about.
    //
    // Instead use 4 × fontSize (capped at 512) and put the baseline a quarter
    // from the bottom: emoji can extend 3·fontSize above and 1·fontSize below
    // the baseline before clipping, comfortably exceeding what any strike or
    // COLR layer actually authors.
    DWRITE_FONT_METRICS fontMetrics{};
    key.fontFace->GetMetrics(&fontMetrics);
    const float emScale = (float)key.fontSize / (float)fontMetrics.designUnitsPerEm;
    const int   designAscent  = (int)std::ceil(fontMetrics.ascent  * emScale);
    const int   designDescent = (int)std::ceil(fontMetrics.descent * emScale);
    int canvasSize = std::max(4 * (int)key.fontSize,
                              (designAscent + designDescent) * 2 + 16);
    if (canvasSize > 512) canvasSize = 512;
    if (canvasSize <= 0) return false;
    const int   originX_px = canvasSize / 2;
    const int   originY_px = canvasSize - canvasSize / 4;  // baseline at 75% from top

    std::vector<uint8_t> scratch((size_t)canvasSize * canvasSize * 4, 0);

    int unionLeft   = canvasSize;
    int unionTop    = canvasSize;
    int unionRight  = 0;
    int unionBottom = 0;

    // Walk every layer. MoveNext() advances; HasRun signals end of stream.
    for (;;) {
        BOOL hasRun = FALSE;
        if (FAILED(enumerator->MoveNext(&hasRun)) || !hasRun) {
            break;
        }

        DWRITE_COLOR_GLYPH_RUN1 const* layer = nullptr;
        if (FAILED(enumerator->GetCurrentRun(&layer)) || !layer) {
            continue;
        }

        // Use the per-layer colour the font authored. paletteIndex == 0xFFFF
        // means "use the run's foreground colour"; for our offline rasterization
        // we treat that as opaque white and let the shader apply Foreground.
        // But we want emoji to ignore Foreground entirely (the whole point of
        // colour emoji is that they keep their authored colours), so we still
        // bake white here — the glyph entry's isColor flag will tell the PS
        // to skip Foreground tinting and use the atlas RGB directly.
        float lr, lg, lb, la;
        if (layer->paletteIndex == 0xFFFF) {
            lr = lg = lb = la = 1.0f;
        } else {
            lr = layer->runColor.r;
            lg = layer->runColor.g;
            lb = layer->runColor.b;
            la = layer->runColor.a;
        }
        // Premultiply once up front.
        const float pr = lr * la, pg = lg * la, pb = lb * la;

        // Track unioned bounds so we trim the empty padding before
        // pushing into the atlas. Updated by both vector and bitmap paths.
        auto extendUnion = [&](int dx, int dy) {
            if (dx < unionLeft)        unionLeft = dx;
            if (dy < unionTop)         unionTop = dy;
            if (dx + 1 > unionRight)   unionRight = dx + 1;
            if (dy + 1 > unionBottom)  unionBottom = dy + 1;
        };

        const DWRITE_GLYPH_IMAGE_FORMATS imgFmt = layer->glyphImageFormat;
        const DWRITE_GLYPH_IMAGE_FORMATS kVectorMask =
            DWRITE_GLYPH_IMAGE_FORMATS_NONE |
            DWRITE_GLYPH_IMAGE_FORMATS_TRUETYPE |
            DWRITE_GLYPH_IMAGE_FORMATS_CFF |
            DWRITE_GLYPH_IMAGE_FORMATS_COLR;
        const DWRITE_GLYPH_IMAGE_FORMATS kBitmapMask =
            DWRITE_GLYPH_IMAGE_FORMATS_PNG |
            DWRITE_GLYPH_IMAGE_FORMATS_JPEG |
            DWRITE_GLYPH_IMAGE_FORMATS_TIFF |
            DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8;

        const bool isVector = (imgFmt == DWRITE_GLYPH_IMAGE_FORMATS_NONE) ||
                              ((imgFmt & kVectorMask) != 0 && (imgFmt & kBitmapMask) == 0);

        if (EmojiTraceEnabled()) {
            char buf[160];
            sprintf_s(buf, "[jalium emoji] glyph=%u layer fmt=0x%04X color=(%.2f,%.2f,%.2f,%.2f)\n",
                      key.glyphIndex, (unsigned)imgFmt, lr, lg, lb, la);
            OutputDebugStringA(buf);
        }
        const bool isBitmap = (imgFmt & kBitmapMask) != 0;

        if (isVector) {
            // ── Vector layer (COLR sub-glyph, outline) ──
            // Grayscale alpha-mask the layer's outline and SrcOver-composite
            // it in the layer colour onto the scratch canvas.
            ComPtr<IDWriteGlyphRunAnalysis> analysis;
            if (FAILED(dwriteFactory4_->CreateGlyphRunAnalysis(
                    &layer->glyphRun,
                    nullptr,
                    DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC,
                    DWRITE_MEASURING_MODE_NATURAL,
                    DWRITE_GRID_FIT_MODE_DEFAULT,
                    DWRITE_TEXT_ANTIALIAS_MODE_GRAYSCALE,
                    layer->baselineOriginX,
                    layer->baselineOriginY,
                    &analysis)) || !analysis) {
                continue;
            }

            RECT bounds = {};
            if (FAILED(analysis->GetAlphaTextureBounds(DWRITE_TEXTURE_ALIASED_1x1, &bounds))) {
                continue;
            }
            int lW = bounds.right - bounds.left;
            int lH = bounds.bottom - bounds.top;
            if (lW <= 0 || lH <= 0) continue;
            if (lW > canvasSize || lH > canvasSize) continue;

            std::vector<uint8_t> alpha((size_t)lW * lH, 0);
            if (FAILED(analysis->CreateAlphaTexture(DWRITE_TEXTURE_ALIASED_1x1,
                                                    &bounds, alpha.data(), (UINT32)alpha.size()))) {
                continue;
            }

            for (int yy = 0; yy < lH; ++yy) {
                int dy = originY_px + bounds.top + yy;
                if (dy < 0 || dy >= canvasSize) continue;
                for (int xx = 0; xx < lW; ++xx) {
                    int dx = originX_px + bounds.left + xx;
                    if (dx < 0 || dx >= canvasSize) continue;

                    const uint8_t cov = alpha[(size_t)yy * lW + xx];
                    if (cov == 0) continue;
                    const float a = (float)cov / 255.0f;

                    uint8_t* dst = scratch.data() + ((size_t)dy * canvasSize + dx) * 4;
                    const float invA = 1.0f - a * la;
                    auto blend = [&](int idx, float srcVal) {
                        float d = (float)dst[idx] / 255.0f;
                        float o = srcVal * a + d * invA;
                        if (o < 0) o = 0; if (o > 1) o = 1;
                        dst[idx] = (uint8_t)std::round(o * 255.0f);
                    };
                    blend(0, pr);
                    blend(1, pg);
                    blend(2, pb);
                    blend(3, la);
                    extendUnion(dx, dy);
                }
            }
            continue;
        }

        if (isBitmap) {
            // ── Bitmap-strike layer (Win11 Fluent emoji is PNG strikes) ──
            // Pull the encoded image bytes via IDWriteFontFace4::GetGlyphImageData,
            // decode through WIC into pre-multiplied BGRA, scale to the
            // requested em size, then SrcOver-blit into the scratch canvas.
            ComPtr<IDWriteFontFace4> ff4;
            if (FAILED(layer->glyphRun.fontFace->QueryInterface(IID_PPV_ARGS(&ff4))) || !ff4) {
                continue;
            }

            // GetGlyphImageData picks the strike whose pixelsPerEm is closest
            // to the requested ppem. fontSize here is already DPI-scaled.
            const uint16_t glyphId = layer->glyphRun.glyphIndices
                ? layer->glyphRun.glyphIndices[0]
                : key.glyphIndex;

            // The format we ask for must be exactly one bit of the mask
            // GetGlyphImageData expects a singular format; use whichever the
            // layer reports first that we know how to decode.
            DWRITE_GLYPH_IMAGE_FORMATS reqFmt = DWRITE_GLYPH_IMAGE_FORMATS_NONE;
            if      (imgFmt & DWRITE_GLYPH_IMAGE_FORMATS_PNG)  reqFmt = DWRITE_GLYPH_IMAGE_FORMATS_PNG;
            else if (imgFmt & DWRITE_GLYPH_IMAGE_FORMATS_JPEG) reqFmt = DWRITE_GLYPH_IMAGE_FORMATS_JPEG;
            else if (imgFmt & DWRITE_GLYPH_IMAGE_FORMATS_TIFF) reqFmt = DWRITE_GLYPH_IMAGE_FORMATS_TIFF;
            else if (imgFmt & DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8)
                reqFmt = DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8;
            if (reqFmt == DWRITE_GLYPH_IMAGE_FORMATS_NONE) continue;

            DWRITE_GLYPH_IMAGE_DATA imgData = {};
            void* dataCtx = nullptr;
            if (FAILED(ff4->GetGlyphImageData(
                    glyphId, (uint32_t)key.fontSize, reqFmt,
                    &imgData, &dataCtx)) || !dataCtx) {
                continue;
            }

            if (imgData.pixelSize.width == 0 || imgData.pixelSize.height == 0 ||
                imgData.pixelsPerEm == 0) {
                ff4->ReleaseGlyphImageData(dataCtx);
                continue;
            }

            // Decode (or directly take) BGRA pixels.
            uint32_t srcW = 0, srcH = 0;
            std::vector<uint8_t> bgra;
            if (reqFmt == DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8) {
                srcW = imgData.pixelSize.width;
                srcH = imgData.pixelSize.height;
                const size_t bytes = (size_t)srcW * srcH * 4;
                if (imgData.imageDataSize < bytes) {
                    ff4->ReleaseGlyphImageData(dataCtx);
                    continue;
                }
                bgra.assign((const uint8_t*)imgData.imageData,
                            (const uint8_t*)imgData.imageData + bytes);
            } else {
                if (!DecodeEmojiImageBytes(imgData.imageData, imgData.imageDataSize,
                                           srcW, srcH, bgra)) {
                    ff4->ReleaseGlyphImageData(dataCtx);
                    continue;
                }
            }

            // Scale to the requested em (the strike's ppem rarely matches the
            // current font size exactly). Aspect-preserving by definition.
            const float scale = (float)key.fontSize / (float)imgData.pixelsPerEm;
            uint32_t dstW = std::max<uint32_t>(1, (uint32_t)std::round(srcW * scale));
            uint32_t dstH = std::max<uint32_t>(1, (uint32_t)std::round(srcH * scale));
            if ((int)dstW > canvasSize) dstW = (uint32_t)canvasSize;
            if ((int)dstH > canvasSize) dstH = (uint32_t)canvasSize;

            std::vector<uint8_t> scaled;
            const uint8_t* srcPx;
            if (srcW != dstW || srcH != dstH) {
                ResampleBgraNearestBox(bgra.data(), srcW, srcH, scaled, dstW, dstH);
                srcPx = scaled.data();
            } else {
                srcPx = bgra.data();
            }

            // Position the scaled bitmap. horizontalLeftOrigin is in strike
            // pixel coordinates from the baseline; scale it down to the dst
            // em so the bitmap sits the same way at any font size.
            const float baseOrigX = (float)originX_px + layer->baselineOriginX;
            const float baseOrigY = (float)originY_px + layer->baselineOriginY;
            const int leftX = (int)std::round(baseOrigX + imgData.horizontalLeftOrigin.x * scale);
            const int topY  = (int)std::round(baseOrigY + imgData.horizontalLeftOrigin.y * scale);

            for (uint32_t yy = 0; yy < dstH; ++yy) {
                int dy = topY + (int)yy;
                if (dy < 0 || dy >= canvasSize) continue;
                for (uint32_t xx = 0; xx < dstW; ++xx) {
                    int dx = leftX + (int)xx;
                    if (dx < 0 || dx >= canvasSize) continue;

                    const uint8_t* sp = srcPx + ((size_t)yy * dstW + xx) * 4;
                    if (sp[3] == 0) continue;

                    // sp is pre-multiplied BGRA. Atlas is RGBA — swap B↔R.
                    const float srcA = sp[3] / 255.0f;
                    const float invA = 1.0f - srcA;
                    uint8_t* dst = scratch.data() + ((size_t)dy * canvasSize + dx) * 4;
                    auto blend = [&](int dstIdx, uint8_t srcByte) {
                        float s = srcByte / 255.0f;
                        float d = dst[dstIdx] / 255.0f;
                        float o = s + d * invA;
                        if (o < 0) o = 0; if (o > 1) o = 1;
                        dst[dstIdx] = (uint8_t)std::round(o * 255.0f);
                    };
                    blend(0, sp[2]);  // R ← BGRA.R
                    blend(1, sp[1]);  // G
                    blend(2, sp[0]);  // B ← BGRA.B
                    blend(3, sp[3]);  // A
                    extendUnion(dx, dy);
                }
            }

            ff4->ReleaseGlyphImageData(dataCtx);
            continue;
        }

        // Any other format (SVG, COLR_PAINT_TREE for COLR v1 gradients, ...)
        // is left to a future commit. Drop these layers silently rather than
        // letting the whole glyph fall back to the black-mask path.
    }

    if (unionRight <= unionLeft || unionBottom <= unionTop) {
        if (EmojiTraceEnabled()) {
            char buf[160];
            sprintf_s(buf, "[jalium emoji] glyph=%u ppem=%u union empty — every layer skipped or unsupported\n",
                      key.glyphIndex, key.fontSize);
            OutputDebugStringA(buf);
        }
        return false;  // every layer was empty
    }

    const int glyphW = unionRight - unionLeft;
    const int glyphH = unionBottom - unionTop;
    if (glyphW > 512 || glyphH > 512) return false;

    uint16_t atlasX, atlasY;
    if (!AllocateAtlasRect((uint16_t)glyphW, (uint16_t)glyphH, atlasX, atlasY)) {
        needsReset_ = true;
        entry.valid = false;
        return true;  // reset will happen next frame; suppress fallback for this attempt
    }

    for (int y = 0; y < glyphH; ++y) {
        if ((uint32_t)(atlasY + y) >= atlasH_) break;
        const uint8_t* src = scratch.data() + ((size_t)(unionTop + y) * canvasSize + unionLeft) * 4;
        uint8_t* dst = atlasBitmap_.data() + ((size_t)(atlasY + y) * atlasW_ + atlasX) * 4;
        memcpy(dst, src, (size_t)glyphW * 4);
    }

    entry.x = atlasX;
    entry.y = atlasY;
    entry.w = (uint16_t)glyphW;
    entry.h = (uint16_t)glyphH;
    // Bearings: convert from scratch-canvas coordinates back to baseline-relative.
    entry.bearingX = (int16_t)(unionLeft   - originX_px);
    entry.bearingY = (int16_t)(originY_px - unionTop);
    entry.valid    = true;
    entry.isColor  = true;

    dirty_ = true;
    dirtyMinY_ = std::min(dirtyMinY_, atlasY);
    dirtyMaxY_ = std::max(dirtyMaxY_, (uint16_t)(atlasY + glyphH));
    return true;
}

// ============================================================================
// Generate Glyph Instances
// ============================================================================

uint64_t D3D12GlyphAtlas::HashInstanceKey(uint64_t layoutKey,
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

uint32_t D3D12GlyphAtlas::GenerateGlyphs(
    IDWriteTextLayout* layout,
    float originX, float originY,
    float colorR, float colorG, float colorB, float colorA,
    std::vector<GlyphQuadInstance>& outInstances,
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
        // pass over a contiguous slice. GlyphQuadInstance is trivially
        // copyable (POD floats), enforced by the static_assert on its
        // layout, so memcpy is well-defined.
        const size_t glyphN = run.instances.size();
        if (glyphN > 0) {
            const size_t base = outInstances.size();
            outInstances.resize(base + glyphN);
            std::memcpy(outInstances.data() + base,
                        run.instances.data(),
                        glyphN * sizeof(GlyphQuadInstance));
            GlyphQuadInstance* dst = outInstances.data() + base;
            for (size_t i = 0; i < glyphN; ++i) {
                GlyphQuadInstance& gi = dst[i];
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

                GlyphQuadInstance inst;
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
