#include "d3d12_glyph_atlas.h"
#include "jalium_text_options.h"
#include "jalium_text_stats.h"
#include <wincodec.h>
#include <mutex>
#include <cstring>
#include <cmath>
#include <algorithm>
#include <cstdio>
#include <share.h>

namespace jalium {

// JALIUM_ATLAS_TRACE=1 → append atlas packing / growth / reset events to
// %TEMP%\jalium_atlas_trace.txt. Off by default (one env lookup per process).
static FILE* AtlasTraceFile() {
    static FILE* f = nullptr;
    static bool tried = false;
    if (!tried) {
        tried = true;
        char buf[8];
        DWORD n = GetEnvironmentVariableA("JALIUM_ATLAS_TRACE", buf, sizeof(buf));
        if (n > 0 && buf[0] != '0') {
            char dir[MAX_PATH] = {};
            DWORD m = GetEnvironmentVariableA("TEMP", dir, sizeof(dir));
            char path[MAX_PATH + 40];
            if (m > 0 && m < sizeof(dir)) {
                _snprintf_s(path, sizeof(path), _TRUNCATE, "%s\\jalium_atlas_trace.txt", dir);
            } else {
                _snprintf_s(path, sizeof(path), _TRUNCATE, "jalium_atlas_trace.txt");
            }
            f = _fsopen(path, "w", _SH_DENYWR);
            if (f) { fprintf(f, "[atlas] trace start (d3d12)\n"); fflush(f); }
        }
    }
    return f;
}
static bool AtlasTraceEnabled() { return AtlasTraceFile() != nullptr; }
static void AtlasTraceLog(const char* fmt, ...) {
    FILE* f = AtlasTraceFile();
    if (!f) return;
    va_list args;
    va_start(args, fmt);
    vfprintf(f, fmt, args);
    va_end(args);
    fflush(f);
}

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

// DirectWrite can grid-fit a heavily squeezed small glyph to a single opaque
// column. Enhanced contrast cannot make that column look wider because there
// is no neighbouring coverage to enhance. Preserve a fractional amount of
// adjacent coverage inside the glyph's existing bounds so paired stems (U/H,
// CJK verticals) do not collapse, without changing advances or atlas geometry.
static void PreserveSqueezedStemCoverage(
    std::vector<uint8_t>& alpha,
    int width,
    int height,
    size_t channels,
    float horizontalPpem,
    float horizontalAspect)
{
    if (width < 3 || height <= 0 || channels == 0 ||
        horizontalAspect >= 1.0f || horizontalPpem >= 16.0f) {
        return;
    }

    const float compression = std::clamp(1.0f - horizontalAspect, 0.0f, 1.0f);
    const float smallText = std::clamp(
        (16.0f - horizontalPpem) / 8.0f, 0.0f, 1.0f);
    const float spread = std::min(0.35f, compression * smallText);
    if (spread < 0.04f) {
        return;
    }

    const std::vector<uint8_t> source = alpha;
    for (int y = 0; y < height; ++y) {
        for (int x = 0; x < width; ++x) {
            for (size_t channel = 0; channel < channels; ++channel) {
                const size_t index =
                    ((size_t)y * (size_t)width + (size_t)x) * channels + channel;
                uint8_t neighbour = 0;
                if (x > 0) {
                    neighbour = source[index - channels];
                }
                if (x + 1 < width) {
                    neighbour = std::max(
                        neighbour, source[index + channels]);
                }
                const uint8_t preserved = (uint8_t)std::lround(
                    (float)neighbour * spread);
                alpha[index] = std::max(source[index], preserved);
            }
        }
    }
}

// ============================================================================
// gasp-table rasterization policy
// ============================================================================
//
// DirectWrite exposes two families of antialiasing for outline glyphs:
//
//   * NATURAL / GDI_*        — the outline is grid-fit VERTICALLY, so
//                              horizontal strokes and the baseline land on
//                              whole pixel rows and read as solid ink.
//   * NATURAL_SYMMETRIC      — symmetric antialiasing in both axes; strokes
//                              are free to straddle two rows at 50 % each.
//
// Which one is right is a per-font, per-ppem decision that lives in the font's
// `gasp` table, and GetRecommendedRenderingMode is the API that reads it.
// Symmetric rendering only becomes the better choice once a stroke is thick
// enough to span more than one pixel — which is why the gasp table for the
// stock UI fonts asks for NATURAL below roughly 20 ppem and only switches to
// NATURAL_SYMMETRIC above it.
//
// Caching: the answer depends only on (face, ppem, transform, hint), so it is
// memoised. The lookup is on the per-run path in GenerateGlyphs as well as the
// per-glyph miss path in RasterizeGlyph, and the COM call is not free.
D3D12GlyphAtlas::GaspPolicy D3D12GlyphAtlas::ResolveGaspPolicy(
    IDWriteFontFace* fontFace,
    uint16_t ppem,
    const DWRITE_MATRIX* transform,
    uint8_t hintingMode)
{
    // Symmetric antialiasing only becomes the better trade once a stroke spans
    // more than one pixel; below this em size it just costs stroke contrast.
    // 20 is where the stock UI fonts' own gasp tables switch over, so fonts
    // that already behave see no change at all.
    static constexpr uint16_t kSymmetricDowngradeMaxPpem = 20;

    GaspPolicy policy;
    if (!fontFace) {
        policy.useGdiFallback = true;
        return policy;
    }

    // The transform only ever carries the X:Y aspect ratio here (scaleY is
    // folded into ppem), so a single bucket byte identifies it for the key.
    uint8_t aspectBucket = 0;
    if (transform) {
        long q = std::lround(transform->m11 * 32.0f);
        aspectBucket = (uint8_t)std::clamp(q, 1L, 255L);
    }
    const uint64_t cacheKey =
        (uint64_t)(uintptr_t)fontFace ^
        (((uint64_t)ppem << 1) | ((uint64_t)hintingMode << 18) |
         ((uint64_t)aspectBucket << 22)) * 0x9E3779B97F4A7C15ull;

    if (auto it = gaspPolicyCache_.find(cacheKey); it != gaspPolicyCache_.end()) {
        return it->second;
    }

    ComPtr<IDWriteFontFace3> fontFace3;
    if (FAILED(fontFace->QueryInterface(IID_PPV_ARGS(&fontFace3))) || !fontFace3) {
        // Pre-Win10 face: keep the historical behaviour (symmetric + whatever
        // grid fit the caller pinned) rather than guessing.
        policy.mode = DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC;
        policy.gridFit = DWRITE_GRID_FIT_MODE_DEFAULT;
        gaspPolicyCache_.emplace(cacheKey, policy);
        return policy;
    }

    DWRITE_RENDERING_MODE1 recMode = DWRITE_RENDERING_MODE1_DEFAULT;
    DWRITE_GRID_FIT_MODE recGridFit = DWRITE_GRID_FIT_MODE_DEFAULT;
    HRESULT hr = fontFace3->GetRecommendedRenderingMode(
        (float)ppem,
        96.0f, 96.0f,                         // pixelsPerDip = 1.0 (ppem is final)
        transform,
        FALSE,                                // not sideways
        DWRITE_OUTLINE_THRESHOLD_ANTIALIASED,
        DWRITE_MEASURING_MODE_NATURAL,
        renderingParams_.Get(),
        &recMode,
        &recGridFit);

    if (FAILED(hr)) {
        policy.mode = DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC;
        policy.gridFit = DWRITE_GRID_FIT_MODE_DEFAULT;
        gaspPolicyCache_.emplace(cacheKey, policy);
        return policy;
    }

    switch (recMode) {
        // CreateGlyphRunAnalysis cannot consume these — the GDI bitmap render
        // target path owns them.
        case DWRITE_RENDERING_MODE1_OUTLINE:
        case DWRITE_RENDERING_MODE1_ALIASED:
        case DWRITE_RENDERING_MODE1_DEFAULT:
            policy.useGdiFallback = true;
            break;

        // GDI-compatible modes rasterize against GDI's integer-advance metrics.
        // Our layout is measured with DWRITE_MEASURING_MODE_NATURAL, so feeding
        // a GDI mode into CreateGlyphRunAnalysis would pair hinted-to-integer
        // ink with fractional natural advances and scatter the glyphs. Take the
        // vertical grid fitting they were recommended FOR — that is the part
        // that matters at these sizes — via plain NATURAL, which is the
        // natural-metrics member of the same family.
        case DWRITE_RENDERING_MODE1_GDI_CLASSIC:
        case DWRITE_RENDERING_MODE1_GDI_NATURAL:
            policy.mode = DWRITE_RENDERING_MODE1_NATURAL;
            policy.gridFit = recGridFit;
            policy.gridAligned = true;
            break;

        case DWRITE_RENDERING_MODE1_NATURAL:
            policy.mode = recMode;
            policy.gridFit = recGridFit;
            policy.gridAligned = true;
            break;

        // NATURAL_SYMMETRIC and NATURAL_SYMMETRIC_DOWNSAMPLED.
        //
        // Symmetric antialiasing spreads a stroke's coverage across the pixel
        // grid in BOTH axes. That is the right trade once a stroke is thicker
        // than a pixel, but at UI sizes it costs contrast for nothing here:
        // measured on Microsoft YaHei UI at 12 ppem, symmetric and NATURAL
        // produce the SAME ink box (12x11 at bearing (0,-10)) yet every
        // horizontal stroke peaks at 191/255 under symmetric versus a solid
        // 255 under NATURAL. The glyph lands on the right rows either way and
        // simply never reaches full ink, which reads as "small text is grey /
        // out of focus".
        //
        // This is not a hypothetical: the recommendation is per-font, and the
        // families a modern Windows UI actually defaults to are exactly the
        // ones that ask for symmetric all the way down. Measured against
        // GetRecommendedRenderingMode at 11/12/13/14 ppem:
        //
        //     Microsoft YaHei UI, Segoe UI, Consolas  -> NATURAL
        //     Segoe UI Variable Display / Text        -> NATURAL_SYMMETRIC
        //     Cascadia Code                           -> NATURAL_SYMMETRIC
        //
        // So a window mixing a Segoe UI Variable label with a YaHei UI one
        // renders the first soft and the second crisp, at the same size, with
        // no way to tell from the markup — the original "why is only THIS list
        // blurry" report.
        //
        // Nothing downstream compensates: the quad is snapped to whole physical
        // pixels and sampled with a POINT sampler precisely so the atlas bitmap
        // reaches the screen untouched, so whatever contrast the rasterizer
        // gives up is gone for good.
        //
        // Below the size where symmetric starts paying for itself, take NATURAL
        // instead and keep the recommended grid-fit mode. A font that
        // explicitly asks for grid fitting to be OFF is left alone: there
        // symmetric is the honest choice and NATURAL would only fight it.
        default:
            if (ppem < kSymmetricDowngradeMaxPpem &&
                recGridFit != DWRITE_GRID_FIT_MODE_DISABLED) {
                policy.mode = DWRITE_RENDERING_MODE1_NATURAL;
                policy.gridFit = recGridFit;
                policy.gridAligned = true;
            } else {
                policy.mode = recMode;
                policy.gridFit = recGridFit;
                policy.gridAligned = false;
            }
            break;
    }

    gaspPolicyCache_.emplace(cacheKey, policy);
    return policy;
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
        BOOL isSideways;
        UINT32 bidiLevel;
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
        run.isSideways = glyphRun->isSideways;
        run.bidiLevel = glyphRun->bidiLevel;
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
    AtlasTraceLog("[atlas] Reset at=%ux%u gen=%u\n", atlasW_, atlasH_, atlasGeneration_ + 1);
    // Every cached glyph slot is about to be invalidated — bump the
    // generation so the resolved-glyph memo treats all its entries (which
    // hold now-stale atlas UVs) as misses.
    ++atlasGeneration_;
    jalium::text_stats::AddAtlasReset();
    cache_.clear();
    // cache_ is what keeps the IDWriteFontFace objects behind the raw pointers
    // in both key sets alive, so the gasp memo has to be dropped with it —
    // otherwise a recycled face address could serve another font's policy.
    gaspPolicyCache_.clear();
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
    uploadBuffer_->SetName(L"JaliumGlyphAtlasUpload");

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
    //
    // CRITICAL: renderingMode and gridFitMode must stay DEFAULT here.
    // GetRecommendedRenderingMode short-circuits and returns whatever mode a
    // params object names, so pinning NATURAL_SYMMETRIC/ENABLED (as this used
    // to) silently bypassed the font's gasp table for EVERY size. Measured on
    // Microsoft YaHei UI: at 9-20 ppem the gasp table asks for NATURAL, which
    // grid-fits horizontal strokes onto whole pixel rows; NATURAL_SYMMETRIC
    // antialiases vertically instead, so at the 12 px default UI size every
    // CJK stroke landed as two rows of half-grey — the "small text is blurry"
    // report. Only at >= 24 ppem does the gasp table itself switch to
    // NATURAL_SYMMETRIC, which is exactly why large text looked fine.
    //
    // Gamma / contrast come from the SYSTEM params rather than hard-coded
    // values: they feed GetAlphaBlendParams and the GDI fallback's
    // DrawGlyphRun, and gamma 1.0 + grayscaleEnhancedContrast 0.0 (the old
    // literals) are both far from what Windows itself uses (1.8 / 1.0).
    ComPtr<IDWriteRenderingParams> systemParams;
    dwriteFactory_->CreateRenderingParams(&systemParams);

    if (dwriteFactory3_) {
        float gamma = systemParams ? systemParams->GetGamma() : 1.8f;
        float enhancedContrast = systemParams ? systemParams->GetEnhancedContrast() : 0.5f;
        float clearTypeLevel = systemParams ? systemParams->GetClearTypeLevel() : 1.0f;
        DWRITE_PIXEL_GEOMETRY pixelGeometry = systemParams
            ? systemParams->GetPixelGeometry()
            : DWRITE_PIXEL_GEOMETRY_RGB;
        float grayscaleEnhancedContrast = 1.0f;
        if (systemParams) {
            ComPtr<IDWriteRenderingParams3> sys3;
            if (SUCCEEDED(systemParams.As(&sys3)) && sys3) {
                grayscaleEnhancedContrast = sys3->GetGrayscaleEnhancedContrast();
            }
        }

        ComPtr<IDWriteRenderingParams3> params3;
        dwriteFactory3_->CreateCustomRenderingParams(
            gamma,
            enhancedContrast,
            grayscaleEnhancedContrast,
            clearTypeLevel,
            pixelGeometry,
            DWRITE_RENDERING_MODE1_DEFAULT,     // let the gasp table decide
            DWRITE_GRID_FIT_MODE_DEFAULT,       // ditto
            &params3);
        if (params3) {
            params3->QueryInterface(IID_PPV_ARGS(&renderingParams_));
        }
    }
    if (!renderingParams_) {
        renderingParams_ = systemParams;
    }
    if (!renderingParams_) {
        if (FAILED(dwriteFactory_->CreateRenderingParams(&renderingParams_)))
            return false;
    }

    initialized_ = true;
    return true;
}

bool D3D12GlyphAtlas::EnsureUploadBuffer()
{
    if (uploadBuffer_) return true;

    const UINT rowPitch = (atlasW_ * 4 + 255) & ~255u;
    const UINT64 uploadSize = static_cast<UINT64>(rowPitch) * atlasH_;
    auto uploadHeap = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
    auto uploadDesc = MakeBufferDesc(uploadSize);
    if (FAILED(device_->CreateCommittedResource(
            &uploadHeap, D3D12_HEAP_FLAG_NONE, &uploadDesc,
            D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
            IID_PPV_ARGS(&uploadBuffer_)))) {
        return false;
    }

    uploadBuffer_->SetName(L"JaliumGlyphAtlasUpload");
    return true;
}

void D3D12GlyphAtlas::QueueUploadBufferKeepaliveAfterCopy()
{
    if (!uploadBuffer_) return;

    // The current main command list is still OPEN. D3D12Backend's graveyard is
    // keyed from lastSubmittedFenceValue+1, but a later effect-resource grow can
    // issue an auxiliary Signal/Reclaim before this list is submitted. Parking
    // here lets DirectRenderer transfer the resource to the exact frame slot
    // whose real submission fence covers the CopyTextureRegion.
    pendingRetiredUploadBuffers_.push_back(std::move(uploadBuffer_));
}

void D3D12GlyphAtlas::DrainRetiredUploadBuffers(
    std::vector<ComPtr<ID3D12Resource>>& destination)
{
    if (pendingRetiredUploadBuffers_.empty()) return;

    destination.reserve(destination.size() + pendingRetiredUploadBuffers_.size());
    for (auto& resource : pendingRetiredUploadBuffers_) {
        destination.push_back(std::move(resource));
    }
    pendingRetiredUploadBuffers_.clear();
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
        if (AtlasTraceEnabled()) {
            AtlasTraceLog("[atlas] alloc FAIL %ux%u atlas=%ux%u packY=%u grow=%d reset=%d\n",
                    w, h, atlasW_, atlasH_, packY_, needsGrow_ ? 1 : 0, needsReset_ ? 1 : 0);
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
    AtlasTraceLog("[atlas] GrowAtlas enter req=%ux%u at=%ux%u\n", reqW, reqH, atlasW_, atlasH_);
    uint32_t newW = atlasW_;
    uint32_t newH = atlasH_;
    while (newW < reqW && newW < kMaxAtlasDim) newW *= 2;
    while (newH < reqH && newH < kMaxAtlasDim) newH *= 2;
    if (newW > kMaxAtlasDim) newW = kMaxAtlasDim;
    if (newH > kMaxAtlasDim) newH = kMaxAtlasDim;
    if (newW == atlasW_ && newH == atlasH_) {
        AtlasTraceLog("[atlas] grow noop req=%ux%u at=%ux%u\n", reqW, reqH, atlasW_, atlasH_);
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
    HRESULT growTexHr = device_->CreateCommittedResource(
            &defaultHeap, D3D12_HEAP_FLAG_NONE, &texDesc,
            D3D12_RESOURCE_STATE_COMMON, nullptr,
            IID_PPV_ARGS(&newTex));
    if (FAILED(growTexHr)) {
        AtlasTraceLog("[atlas] grow tex FAIL hr=0x%08lX %ux%u\n",
                      (unsigned long)growTexHr, newW, newH);
        return false;
    }
    newTex->SetName(L"JaliumGlyphAtlas");  // [JALIUM-921 diag]

    UINT rowPitch = (newW * 4 + 255) & ~255u;
    UINT64 uploadSize = (UINT64)rowPitch * newH;
    auto uploadHeap = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
    auto uploadDesc = MakeBufferDesc(uploadSize);
    ComPtr<ID3D12Resource> newUpload;
    HRESULT growUpHr = device_->CreateCommittedResource(
            &uploadHeap, D3D12_HEAP_FLAG_NONE, &uploadDesc,
            D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
            IID_PPV_ARGS(&newUpload));
    if (FAILED(growUpHr)) {
        AtlasTraceLog("[atlas] grow upload FAIL hr=0x%08lX %ux%u\n",
                      (unsigned long)growUpHr, newW, newH);
        return false;
    }
    newUpload->SetName(L"JaliumGlyphAtlasUpload");

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

void D3D12GlyphAtlas::MeasurePackedArea(uint64_t& liveArea, uint64_t& deadArea) const
{
    const uint32_t oldestKept =
        frameCounter_ > kCompactKeepFrames ? frameCounter_ - kCompactKeepFrames : 0;
    liveArea = 0;
    deadArea = 0;
    for (const auto& kv : cache_) {
        const auto& v = kv.second;
        if (!v.entry.valid || v.entry.w == 0 || v.entry.h == 0) continue;
        const uint64_t area = (uint64_t)(v.entry.w + 2) * (uint64_t)(v.entry.h + 2);
        if (v.lastUsedFrame >= oldestKept) liveArea += area;
        else deadArea += area;
    }
}

bool D3D12GlyphAtlas::CompactAtlas()
{
    const uint32_t oldestKept =
        frameCounter_ > kCompactKeepFrames ? frameCounter_ - kCompactKeepFrames : 0;

    // Collect the survivors. Tallest first: the packer is a shelf packer, so a
    // row's height is set by its tallest member — feeding it mixed sizes in
    // text order is what wasted most of the atlas (one 400 px title glyph in a
    // row of 120 px body glyphs burns 400 px of shelf for all of them).
    struct Live {
        GlyphKey key;
        uint16_t x, y, w, h;
    };
    std::vector<Live> live;
    live.reserve(cache_.size());
    uint64_t liveArea = 0;
    for (const auto& kv : cache_) {
        const auto& v = kv.second;
        if (v.lastUsedFrame < oldestKept) continue;
        if (!v.entry.valid || v.entry.w == 0 || v.entry.h == 0) continue;
        live.push_back({kv.first, v.entry.x, v.entry.y, v.entry.w, v.entry.h});
        liveArea += (uint64_t)(v.entry.w + 2) * (uint64_t)(v.entry.h + 2);
    }

    // Nothing reclaimable (everything is live) — the caller must grow or reset.
    if (live.size() == cache_.size()) {
        return false;
    }
    // Survivors alone would refill the atlas; a compaction now would just be
    // repeated next frame. Let the caller reset instead.
    if (liveArea * 10 > (uint64_t)atlasW_ * atlasH_ * 9) {
        return false;
    }

    std::sort(live.begin(), live.end(),
              [](const Live& a, const Live& b) { return a.h > b.h; });

    // AllocateAtlasRect raises needsGrow_/needsReset_ when it runs out. Those
    // flags describe the PREVIOUS frame's demand and are being consumed by the
    // caller right now; a re-pack must not resurrect them.
    const bool savedNeedsGrow = needsGrow_;
    const bool savedNeedsReset = needsReset_;

    // Re-pack into a fresh bitmap. Pixels move by memcpy — no glyph is
    // re-rasterized, so this costs no DirectWrite work.
    std::vector<uint8_t> newBitmap((size_t)atlasW_ * atlasH_ * 4, 0);
    const std::vector<uint8_t> oldBitmap = std::move(atlasBitmap_);
    atlasBitmap_ = std::move(newBitmap);
    packX_ = 0;
    packY_ = 0;
    rowHeight_ = 0;

    const size_t rowBytes = (size_t)atlasW_ * 4;
    size_t kept = 0;
    std::unordered_map<GlyphKey, GlyphCacheValue, GlyphKeyHash> compacted;
    compacted.reserve(live.size() * 2);

    for (const auto& l : live) {
        uint16_t nx, ny;
        if (!AllocateAtlasRect(l.w, l.h, nx, ny)) {
            // Ran out mid-repack (shouldn't happen given the area guard above);
            // the rest are simply dropped and re-rasterized on demand.
            break;
        }
        for (uint16_t row = 0; row < l.h; ++row) {
            memcpy(atlasBitmap_.data() + ((size_t)(ny + row) * atlasW_ + nx) * 4,
                   oldBitmap.data() + ((size_t)(l.y + row) * atlasW_ + l.x) * 4,
                   (size_t)l.w * 4);
        }
        auto it = cache_.find(l.key);
        if (it == cache_.end()) continue;
        GlyphCacheValue moved = it->second;
        moved.entry.x = nx;
        moved.entry.y = ny;
        compacted.emplace(l.key, std::move(moved));
        ++kept;
    }
    (void)rowBytes;

    needsGrow_ = savedNeedsGrow;
    needsReset_ = savedNeedsReset;

    const size_t dropped = cache_.size() - kept;
    cache_ = std::move(compacted);
    // Keyed by raw face pointers kept alive by cache_ entries; the dropped ones
    // may have released the last reference, so the memo has to go with them.
    gaspPolicyCache_.clear();

    // A run served from the resolved-glyph memo (instLru_) returns before the
    // per-glyph loop, so its strikes are never stamped and can be dropped here
    // even while on screen. That is a cost, not a correctness problem: the
    // generation bump below invalidates every memo entry, so those runs are
    // rebuilt — and re-rasterized into the space this pass just freed — during
    // the very same frame. No glyph goes missing, and because everything
    // rebuilt is stamped with the current frame, the next boundary sees ~0 %
    // dead and does not compact again.

    // Every UV moved.
    ++atlasGeneration_;
    jalium::text_stats::AddAtlasReset();
    dirty_ = true;
    dirtyMinY_ = 0;
    dirtyMaxY_ = (uint16_t)(std::min<uint32_t>)(packY_ + rowHeight_ + 1, atlasH_);

    AtlasTraceLog("[atlas] compact kept=%zu dropped=%zu usedRows=%u gen=%u\n",
                  kept, dropped, (unsigned)(packY_ + rowHeight_), atlasGeneration_);
    return true;
}

void D3D12GlyphAtlas::ApplyPendingGrowthOrReset()
{
    ++frameCounter_;

    // A hard reset (idle reclaim, antialias-mode switch) is unconditional: the
    // cached pixels either have to be freed or are the wrong format.
    if (needsHardReset_) {
        Reset();
        needsHardReset_ = false;
        needsReset_ = false;
        needsGrow_ = false;
        pendingGrowW_ = 0;
        pendingGrowH_ = 0;
        return;
    }

    const uint32_t usedRows = packY_ + rowHeight_;
    const bool overflowed = needsGrow_ || needsReset_;
    // Act BEFORE the packer runs dry: a mid-frame allocation failure drops that
    // glyph for the frame being presented, which is what makes zoomed text
    // flicker. 75 % used is the trigger to make room ahead of the demand.
    const bool nearlyFull = usedRows * 4 >= atlasH_ * 3;
    if (!overflowed && !nearlyFull) {
        return;
    }

    if (AtlasTraceEnabled() && overflowed) {
        AtlasTraceLog("[atlas] boundary grow=%d reset=%d pending=%ux%u atlas=%ux%u\n",
                needsGrow_ ? 1 : 0, needsReset_ ? 1 : 0,
                pendingGrowW_, pendingGrowH_, atlasW_, atlasH_);
    }

    uint64_t liveArea = 0, deadArea = 0;
    MeasurePackedArea(liveArea, deadArea);
    const uint64_t packedArea = liveArea + deadArea;

    // 1) Grow first while there is headroom to take. Growth preserves every
    //    strike and needs no re-pack, and — measured on a 0.5→8× zoom over the
    //    certificate template — it is what actually stops glyphs being dropped:
    //    keeping the atlas small and compacting instead just makes it overflow
    //    again a frame later (1287 dropped glyphs vs 0).
    //
    //    Size for the DEMAND in one step rather than doubling once per
    //    boundary: each catch-up frame at the wrong size drops glyphs, and a
    //    dropped glyph is a hole in the frame being presented.
    bool resolved = false;
    if (atlasW_ < kMaxAtlasDim || atlasH_ < kMaxAtlasDim) {
        uint32_t target = (std::max)(atlasW_, atlasH_);
        while (target < kMaxAtlasDim &&
               (uint64_t)target * target * 3 < packedArea * 5) {
            target *= 2;
        }
        if (target == (std::max)(atlasW_, atlasH_)) {
            target *= 2;  // pressure without measurable demand — take one step
        }
        target = (std::min)(target, kMaxAtlasDim);
        resolved = GrowAtlas((std::max)(pendingGrowW_, target),
                             (std::max)(pendingGrowH_, target));
    }

    // 2) At kMaxAtlasDim: reclaim the stale size buckets a zoom gesture leaves
    //    behind. Reset() reclaims them too — by throwing away the LIVE set as
    //    well, so the frames right after draw with glyphs missing, which is the
    //    flicker. Compaction reclaims exactly the dead ones.
    if (!resolved && packedArea > 0 && deadArea * 10 >= packedArea * 3) {
        resolved = CompactAtlas();
    }

    // 3) Nothing stale to reclaim and no room to grow: the live set genuinely
    //    does not fit, so a wipe is all that is left. (Rare — it takes a single
    //    frame whose distinct glyphs exceed 4096².)
    if (!resolved && overflowed) {
        Reset();
    }

    needsGrow_ = false;
    needsReset_ = false;
    pendingGrowW_ = 0;
    pendingGrowH_ = 0;
    if (AtlasTraceEnabled()) {
        static uint32_t lastW = 0, lastH = 0;
        if (lastW != atlasW_ || lastH != atlasH_) {
            AtlasTraceLog("[atlas] size now %ux%u gen=%u\n", atlasW_, atlasH_, atlasGeneration_);
            lastW = atlasW_; lastH = atlasH_;
        }
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
        // HARD: compaction would carry the wrong-format pixels forward.
        needsHardReset_ = true;
    }
    return currentAntialiasMode_;
}

// -- JALIUM_TEXT_TRACE (rasterizer side) -----------------------------------
// One line per DISTINCT (fontFace, ppem, aa, hint, sub-pixel phase, scale) the
// atlas ever rasterizes: which DirectWrite rendering mode + grid-fit mode
// actually reached CreateGlyphRunAnalysis, plus the ink box and the per-row
// peak coverage of that first glyph. A grid-fit raster puts a horizontal
// stroke on ONE row at full coverage; an unhinted one splits it over two rows
// at partial coverage -- which is exactly what "small text looks soft" means,
// so the row profile settles the question without guessing. A few dozen lines
// per session. Shares the JALIUM_TEXT_TRACE switch with the transform-side
// trace in d3d12_direct_renderer.cpp.
namespace {
bool GlyphTraceEnabled() {
    static int state = -1;
    if (state < 0) {
        char buf[8];
        DWORD n = GetEnvironmentVariableA("JALIUM_TEXT_TRACE", buf, sizeof(buf));
        state = (n > 0 && buf[0] != '0') ? 1 : 0;
    }
    return state == 1;
}
FILE* GlyphTraceFile() {
    static FILE* f = nullptr;
    static bool tried = false;
    if (!tried) {
        tried = true;
        char dir[MAX_PATH] = {};
        DWORD n = GetEnvironmentVariableA("TEMP", dir, sizeof(dir));
        char path[MAX_PATH + 40];
        if (n > 0 && n < sizeof(dir)) {
            _snprintf_s(path, sizeof(path), _TRUNCATE, "%s\\jalium_glyph_trace.txt", dir);
        } else {
            _snprintf_s(path, sizeof(path), _TRUNCATE, "jalium_glyph_trace.txt");
        }
        f = _fsopen(path, "w", _SH_DENYWR);
    }
    return f;
}
}  // namespace

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

    // key.fontSize is the FINAL vertical ppem.  Keep Y at identity and let
    // the matrix carry only the X:Y aspect ratio.  Hinting at the final ppem
    // is important: hinting a 14px outline and then stretching it to 30px
    // magnifies one-pixel CJK ink-box differences and leaves compressed Latin
    // stems unnaturally thin.
    const float glyphAspectX = key.scaleXQ /
        (float)std::max<uint8_t>(key.scaleYQ, 1);
    // Rotation/skew is carried by the oriented GPU quad. Keep the cached strike
    // upright here (at the final X:Y resolution) so a shallow angle cannot be
    // quantized independently inside every tiny glyph bitmap.
    const bool keyRotated = key.HasGlyphRotation();
    const DWRITE_MATRIX glyphXform =
        { glyphAspectX, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f };
    const bool hasGlyphXform = key.scaleXQ != key.scaleYQ;

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
        bool gaspAlignedForTrace = false;  // JALIUM_TEXT_TRACE only

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
        //
        // A ROTATED run never takes the GDI route: the bitmap render target's
        // ink-box estimate further down is derived from the axis-aligned
        // aspect ratio and would clip rotated ink. Grayscale/ClearType through
        // the analysis path is the right trade there — a bilevel glyph being
        // rotated has no pixel grid left to snap to anyway.
        if (aaMode == JALIUM_TEXT_AA_ALIASED && !keyRotated) {
            useGdiFallback = true;
        }

        if (keyRotated) {
            // Rotated / skewed: grid fitting has nothing left to snap to once
            // the stems stop lining up with the pixel grid, and a hinted strike
            // would jump arbitrarily between neighbouring angle buckets as the
            // rotation animates. Pin symmetric antialiasing with grid fitting
            // off so the glyph SHAPE stays stable across angles.
            renderingMode = DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC;
            gridFitMode = DWRITE_GRID_FIT_MODE_DISABLED;
        } else if (!useGdiFallback) {
            const GaspPolicy policy = ResolveGaspPolicy(
                key.fontFace, key.fontSize,
                hasGlyphXform ? &glyphXform : nullptr, hint);
            if (policy.useGdiFallback) {
                useGdiFallback = true;
            } else {
                renderingMode = policy.mode;
                gaspAlignedForTrace = policy.gridAligned;
                // Only adopt the recommended grid-fit policy when the
                // caller didn't pin an explicit Fixed / Animated mode —
                // otherwise WPF's TextOptions.TextHintingMode override
                // would silently lose to the font's gasp-table hint.
                if (hint == 0) {
                    gridFitMode = policy.gridFit;
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

            // Oversized ink: instead of dropping the glyph (the old hard 512px
            // rejection — huge zoomed/print text simply vanished), re-analyze
            // at a proportionally smaller em and let GenerateGlyphs upscale
            // the quad by entry.scale. Ink is ~linear in em, so one retry
            // normally lands; the 3% margin absorbs AA padding rounding.
            float rasterEm = (float)key.fontSize;
            for (int attempt = 0;
                 glyphW > kMaxGlyphBitmapDim || glyphH > kMaxGlyphBitmapDim;
                 ++attempt) {
                if (attempt >= 2) { entry.valid = false; return true; }
                rasterEm *= (float)kMaxGlyphBitmapDim /
                            (float)std::max(glyphW, glyphH) * 0.97f;
                if (rasterEm < 1.0f) { entry.valid = false; return true; }
                DWRITE_GLYPH_RUN shrunkRun = glyphRun;
                shrunkRun.fontEmSize = rasterEm;
                ComPtr<IDWriteGlyphRunAnalysis> shrunkAnalysis;
                if (FAILED(dwriteFactory3_->CreateGlyphRunAnalysis(
                        &shrunkRun,
                        hasGlyphXform ? &glyphXform : nullptr,
                        renderingMode,
                        DWRITE_MEASURING_MODE_NATURAL,
                        gridFitMode,
                        dwriteAaMode,
                        subpixelOffset, 0.0f,
                        &shrunkAnalysis)) || !shrunkAnalysis) {
                    entry.valid = false;
                    return true;
                }
                analysis = shrunkAnalysis;
                bounds = {};
                if (FAILED(analysis->GetAlphaTextureBounds(textureType, &bounds))) {
                    entry.valid = false;
                    return true;
                }
                glyphW = bounds.right - bounds.left;
                glyphH = bounds.bottom - bounds.top;
                if (glyphW <= 0 || glyphH <= 0) { entry.valid = false; return true; }
            }
            entry.scale = (float)key.fontSize / rasterEm;

            // Grayscale path: 1 byte/pixel of coverage; ClearType: 3 bytes/pixel.
            UINT32 bufferSize = (UINT32)((size_t)glyphW * glyphH * bytesPerPixel);
            std::vector<uint8_t> alphaValues(bufferSize);
            hr = analysis->CreateAlphaTexture(textureType,
                &bounds, alphaValues.data(), bufferSize);
            if (FAILED(hr)) { entry.valid = false; return false; }

            PreserveSqueezedStemCoverage(
                alphaValues,
                glyphW,
                glyphH,
                bytesPerPixel,
                rasterEm * glyphAspectX,
                glyphAspectX);

            // Diagnostic only -- see GlyphTraceEnabled() above.
            if (GlyphTraceEnabled()) {
                static std::unordered_map<uint64_t, uint32_t> seenSigs;
                const uint64_t sig =
                    ((uint64_t)(uintptr_t)key.fontFace) ^
                    (((uint64_t)key.fontSize << 1) | ((uint64_t)key.aaMode << 20) |
                     ((uint64_t)key.hintingMode << 24) | ((uint64_t)key.subpixelX << 28) |
                     ((uint64_t)key.scaleXQ << 34) | ((uint64_t)key.scaleYQ << 42));
                if (seenSigs.size() < 400 && seenSigs.find(sig) == seenSigs.end()) {
                    seenSigs.emplace(sig, 1u);
                    if (FILE* gf = GlyphTraceFile()) {
                        fprintf(gf,
                                "face=%p ppem=%u aa=%u hint=%u sub=%u sxq=%u syq=%u "
                                "mode=%d gridFit=%d gdiFallback=%d gaspAligned=%d "
                                "box=%dx%d bearing=(%ld,%ld) rowPeak=[",
                                (void*)key.fontFace, (unsigned)key.fontSize,
                                (unsigned)key.aaMode, (unsigned)key.hintingMode,
                                (unsigned)key.subpixelX, (unsigned)key.scaleXQ,
                                (unsigned)key.scaleYQ,
                                (int)renderingMode, (int)gridFitMode,
                                useGdiFallback ? 1 : 0, gaspAlignedForTrace ? 1 : 0,
                                glyphW, glyphH, bounds.left, bounds.top);
                        for (int ty = 0; ty < glyphH && ty < 24; ++ty) {
                            uint8_t peak = 0;
                            for (int tx = 0; tx < glyphW; ++tx) {
                                const uint8_t* sp = alphaValues.data() +
                                    ((size_t)ty * glyphW + tx) * bytesPerPixel;
                                for (size_t c = 0; c < bytesPerPixel; ++c)
                                    peak = std::max(peak, sp[c]);
                            }
                            fprintf(gf, "%s%u", ty ? " " : "", (unsigned)peak);
                        }
                        fprintf(gf, "]\n");
                        fflush(gf);
                    }
                }
            }

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

    float rasterEm = (float)key.fontSize;
    float scale = rasterEm / fontMetrics.designUnitsPerEm;
    const float inkUnitsW =
        (float)(metrics.advanceWidth - metrics.leftSideBearing - metrics.rightSideBearing);
    const float inkUnitsH =
        (float)(metrics.advanceHeight - metrics.topSideBearing - metrics.bottomSideBearing);
    int glyphW = (int)std::ceil(inkUnitsW * scale * glyphAspectX) + 4;
    int glyphH = (int)std::ceil(inkUnitsH * scale) + 2;

    if (AtlasTraceEnabled()) {
        AtlasTraceLog("[atlas] gdi glyph=%u ppem=%u box=%dx%d\n",
                key.glyphIndex, key.fontSize, glyphW, glyphH);
    }

    if (glyphW <= 0 || glyphH <= 0) {
        entry.valid = false;
        return true;
    }

    // Oversized ink: shrink the em instead of dropping the glyph (mirrors the
    // analysis path); GenerateGlyphs upscales the quad by entry.scale.
    while (glyphW > kMaxGlyphBitmapDim || glyphH > kMaxGlyphBitmapDim) {
        rasterEm *= (float)kMaxGlyphBitmapDim /
                    (float)std::max(glyphW, glyphH);
        if (rasterEm < 1.0f) { entry.valid = false; return true; }
        scale = rasterEm / fontMetrics.designUnitsPerEm;
        glyphW = (int)std::ceil(inkUnitsW * scale * glyphAspectX) + 4;
        glyphH = (int)std::ceil(inkUnitsH * scale) + 2;
        if (glyphW <= 0 || glyphH <= 0) { entry.valid = false; return true; }
    }
    entry.scale = (float)key.fontSize / rasterEm;
    glyphRun.fontEmSize = rasterEm;

    SIZE curSize = {};
    bitmapRenderTarget_->GetSize(&curSize);
    if (curSize.cx < glyphW || curSize.cy < glyphH) {
        bitmapRenderTarget_->Resize(std::max((UINT32)glyphW, (UINT32)curSize.cx),
                                     std::max((UINT32)glyphH, (UINT32)curSize.cy));
    }

    HDC hdc = bitmapRenderTarget_->GetMemoryDC();
    RECT clearRect = { 0, 0, glyphW, glyphH };
    FillRect(hdc, &clearRect, (HBRUSH)GetStockObject(BLACK_BRUSH));

    // IDWriteBitmapRenderTarget is also used for explicit Aliased text. Apply
    // the same aspect-only transform as the analysis path so its geometry and
    // bearings use the same cache-key semantics.
    bitmapRenderTarget_->SetCurrentTransform(hasGlyphXform ? &glyphXform : nullptr);

    float subpixelOffset = key.subpixelX / 8.0f;  // 1/8-pixel buckets (match primary path)
    float originX = -(metrics.leftSideBearing * scale) +
                    (2.0f + subpixelOffset) / glyphAspectX;
    float originY = (metrics.verticalOriginY - metrics.topSideBearing) * scale + 1;

    bitmapRenderTarget_->DrawGlyphRun(originX, originY, DWRITE_MEASURING_MODE_NATURAL,
        &glyphRun, renderingParams_.Get(), RGB(255, 255, 255), nullptr);

    GdiFlush();

    std::vector<uint8_t> glyphPixels;
    if (!CopyDcRegionToBgraPixels(hdc, glyphW, glyphH, glyphPixels)) {
        if (AtlasTraceEnabled()) {
            AtlasTraceLog("[atlas] gdi COPY FAIL glyph=%u %dx%d\n", key.glyphIndex, glyphW, glyphH);
        }
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
    entry.bearingX = (int16_t)std::round(
        metrics.leftSideBearing * scale * glyphAspectX - 2);
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

    // Huge emoji requests rasterize at the capped em (the scratch canvas below
    // cannot grow past 512) and the emitter upscales via entry.scale — a
    // deep-zoomed emoji renders soft instead of clipped or as the grey box.
    const float emojiEm = (float)(std::min<uint32_t>)(key.fontSize, kMaxColorGlyphPpem);

    DWRITE_GLYPH_RUN glyphRun = {};
    glyphRun.fontFace   = key.fontFace;
    glyphRun.fontEmSize = emojiEm;
    glyphRun.glyphCount = 1;
    glyphRun.glyphIndices = &key.glyphIndex;

    // 1/8-pixel buckets: the denominator must stay consistent with the
    // subpixelQuant *8 (clamp 7) store site in GenerateGlyphs and the two mono
    // bake sites' /8.0f. This offset is baked into baselineOrigin below, flows
    // through the layer union bounds into entry.bearingX, and ultimately into
    // the emitted glyphX — using /4.0f here shifts colour/emoji glyphs in
    // buckets 4..7 a full pixel right of the surrounding monochrome text.
    const float subpixelOffset = key.subpixelX / 8.0f;
    const float glyphAspectX = key.scaleXQ /
        (float)std::max<uint8_t>(key.scaleYQ, 1);
    // Match the mono path: colour layers stay upright in the atlas and the GPU
    // instance basis applies rotation/skew to the composed emoji as one quad.
    const bool keyRotated = key.HasGlyphRotation();
    const DWRITE_MATRIX glyphXform =
        { glyphAspectX, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f };
    const bool hasGlyphXform = key.scaleXQ != key.scaleYQ;

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
    const float emScale = emojiEm / (float)fontMetrics.designUnitsPerEm;
    const int   designAscent  = (int)std::ceil(fontMetrics.ascent  * emScale);
    const int   designDescent = (int)std::ceil(fontMetrics.descent * emScale);
    int canvasSize = std::max(4 * (int)emojiEm,
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
                    hasGlyphXform ? &glyphXform : nullptr,
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
            uint32_t dstW = std::max<uint32_t>(
                1, (uint32_t)std::round(srcW * scale * glyphAspectX));
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
            const float baseOrigX = (float)originX_px +
                                    layer->baselineOriginX * glyphAspectX;
            const float baseOrigY = (float)originY_px + layer->baselineOriginY;
            const int leftX = (int)std::round(
                baseOrigX + imgData.horizontalLeftOrigin.x * scale * glyphAspectX);
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
    entry.scale    = (float)key.fontSize / emojiEm;

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
                                          float scaleX, float scaleY,
                                          bool crispAxisAligned,
                                          uint8_t originPhaseX,
                                          bool subpixelPositioning,
                                          const int16_t* xformQ) noexcept
{
    uint64_t h = 0xCBF29CE484222325ull;  // FNV-1a 64-bit
    auto mix = [&h](const void* p, size_t n) {
        const uint8_t* b = static_cast<const uint8_t*>(p);
        for (size_t i = 0; i < n; ++i) { h ^= b[i]; h *= 0x100000001B3ull; }
    };
    mix(&layoutKey, sizeof(layoutKey));
    mix(&dpiScale, sizeof(dpiScale));
    // The crisp path floors the pen (integer phase) while the rotated/skewed
    // path keeps it continuous, so the SAME layout at the SAME scale bucket
    // produces different cached quad X positions depending on this flag — it
    // must key the memo or a rotated run could be served crisp-floored (or vice
    // versa) and land glyphs a fraction of a pixel off.
    uint8_t crispByte = crispAxisAligned ? 1 : 0;
    mix(&crispByte, sizeof(crispByte));
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
    // Sub-pixel positioning folds the origin's 1/8-px phase into every pen
    // before flooring, so the cached layout-local positions (and the phase
    // bitmaps they reference) are only valid for runs whose origin shares that
    // phase. Keying on the phase keeps the memo origin-independent in spirit —
    // at most 8 variants per shaped run — while the snapped path stays at one.
    const uint8_t posByte = subpixelPositioning ? (uint8_t)(0x10 | (originPhaseX & 0x7)) : (uint8_t)0;
    mix(&posByte, sizeof(posByte));
    // A rotated run's cached quads are already mapped through the 2x2, so two
    // angles of the SAME layout are different runs. Mixed only when present, so
    // every axis-aligned hash keeps the exact value it had before rotation
    // support existed (and no cached run is invalidated by this change).
    if (xformQ) {
        mix(xformQ, sizeof(int16_t) * 4);
    }
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
    float scaleY,
    bool crispAxisAligned,
    bool subpixelPositioning,
    const float* linear2x2,
    const float* decorationOrigin,
    bool* outRequiresSmoothSampling)
{
    if (outRequiresSmoothSampling) *outRequiresSmoothSampling = false;
    if (!layout || !initialized_) return 0;

    // Decorations are emitted as ordinary rects by the caller, so they want the
    // PRE-transform origin — the ambient transform is applied to them a second
    // time downstream, which is also what lets an underline rotate with its
    // text. Callers that don't distinguish keep the historical behaviour of
    // sharing the (already transformed) glyph origin.
    const float decorationOriginX = decorationOrigin ? decorationOrigin[0] : originX;
    const float decorationOriginY = decorationOrigin ? decorationOrigin[1] : originY;

    // Quantize the final per-axis transform to 1/kGlyphScaleQuant steps. The Y
    // bucket is folded into fontEmSize below, so DirectWrite selects and hints
    // at the FINAL screen ppem. The glyph matrix then carries only X:Y, keeping
    // anisotropic deformation without stretching small-size hinting decisions.
    // The two original buckets stay in GlyphKey so their aspect ratio is exact.
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
    const bool    deformed = (scaleXQ != (uint8_t)kGlyphScaleQuant ||
                              scaleYQ != (uint8_t)kGlyphScaleQuant);

    // ── Rotated / skewed runs ────────────────────────────────────────────────
    // The axis-aligned path folds vertical scale into ppem and lets the caller
    // reapply scaleX/scaleY. A rotation cannot be represented by position+size
    // alone, so the rotated path keeps an upright final-resolution strike and
    // maps the pen plus BOTH quad basis vectors through the 2x2. The emitted
    // oriented quads sit in FINAL screen DIPs and the caller must not scale them
    // again (D3D12DirectRenderer::AddText honours that).
    float lin11 = 1.0f, lin12 = 0.0f, lin21 = 0.0f, lin22 = 1.0f;
    if (linear2x2) {
        lin11 = linear2x2[0]; lin12 = linear2x2[1];
        lin21 = linear2x2[2]; lin22 = linear2x2[3];
    }
    // Same epsilon rule AddText uses to pick its axis-aligned fast path, so the
    // two never disagree about which class a transform belongs to.
    const float linScaleRef = std::max({ scaleX, scaleY, 1.0f });
    const bool rotated = linear2x2 &&
                         (std::abs(lin12) > 1e-3f * linScaleRef ||
                          std::abs(lin21) > 1e-3f * linScaleRef);

    // Quantized glyph matrix = M / syR, so the vertical scale already folded
    // into the ppem is divided back out (mirrors what the aspect matrix does
    // on the axis-aligned path).
    int16_t xformQ[4] = { 0, 0, 0, 0 };
    float   xfR[4] = { 1.0f, 0.0f, 0.0f, 1.0f };   // dequantized, matches the key
    if (rotated) {
        const float invSy = 1.0f / std::max(syR, 1e-4f);
        auto quantXf = [](float v) -> int16_t {
            long q = std::lround(v * (float)kGlyphXformQuant);
            return (int16_t)std::clamp(q, -32768L, 32767L);
        };
        xformQ[0] = quantXf(lin11 * invSy);
        xformQ[1] = quantXf(lin12 * invSy);
        xformQ[2] = quantXf(lin21 * invSy);
        xformQ[3] = quantXf(lin22 * invSy);
        // A quantized matrix that landed on all-zeros would read back as "not
        // rotated" in GlyphKey::HasGlyphRotation; nudge the dominant term so a
        // degenerate transform still takes a distinct, non-crashing bucket.
        if ((xformQ[0] | xformQ[1] | xformQ[2] | xformQ[3]) == 0) xformQ[0] = 1;
        for (int i = 0; i < 4; ++i) xfR[i] = xformQ[i] / (float)kGlyphXformQuant;
    }
    // A rotated run shares the deformed run's phase policy: its pen is
    // continuous, and once the ink is off the pixel grid the eight sub-pixel
    // strikes cost 8x the atlas for nothing visible.
    const bool deformedOrRotated = deformed || rotated;

    // Sub-pixel positioning: phases are measured from the FINAL screen pen,
    // i.e. origin + layout-local pen. The origin is a DIP value here (the
    // D3D12 pipeline applies dpiScale_ when it maps to NDC), so take its
    // fraction in PHYSICAL pixels and quantize to the same 1/8 buckets the
    // glyph phases use. Deformed runs keep their single-phase policy (a
    // moving deform would otherwise flood the atlas — see the EXCEPTION note
    // in the glyph loop), so the mode is only honoured at unit scale buckets.
    bool subpixelRun = subpixelPositioning && !deformedOrRotated;
    uint8_t originPhaseX = 0;
    float originPhaseOffset = 0.0f;   // physical px, == originPhaseX / 8
    if (subpixelRun) {
        const float originPhysX = originX * dpiScale_;
        const float originFrac = originPhysX - std::floor(originPhysX);
        originPhaseX = (uint8_t)std::clamp((int)(originFrac * 8.0f), 0, 7);
        originPhaseOffset = originPhaseX / 8.0f;
    }

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

    // Ideal grayscale text uses the same layout-relative coverage at rest,
    // during scrolling, and during uniform zoom. Switching to per-glyph
    // phases/rounding at unit scale makes individual characters jump as the
    // origin crosses pixel boundaries. Explicit Fixed/Display/Aliased modes
    // retain their pixel-grid contract.
    const bool continuousRun = !rotated && subpixelPositioning &&
        effectiveHintingMode != 1 && effectiveAaMode == JALIUM_TEXT_AA_GRAYSCALE &&
        std::abs(scaleX - scaleY) <= 1e-6f * linScaleRef;
    if (continuousRun) {
        // The run is moved as a unit at replay, so a screen-X phase must not
        // choose a different strike or cache entry. Colour/fallback glyphs
        // below also use a fixed strike with a continuous pen position.
        subpixelRun = false;
        originPhaseX = 0;
        originPhaseOffset = 0.0f;
    }
    // Keep one high-resolution strike across small zoom changes. Rebuilding
    // every glyph's coverage at each animation tick makes different stems
    // change phase on different frames. The exact live scale is still applied
    // to the quads by AddText; only raster resolution uses a stable level.
    const float continuousRasterScale = continuousRun
        ? std::exp2(std::floor(std::log2(scaleX) + 0.5f)) : 1.0f;
    const float cacheScaleX = continuousRun ? continuousRasterScale : sxR;
    const float cacheScaleY = continuousRun ? continuousRasterScale : syR;

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
        if (outRequiresSmoothSampling) {
            *outRequiresSmoothSampling = run.requiresSmoothSampling;
        }
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
                dr.x += decorationOriginX;
                dr.y += decorationOriginY;
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
                                            cacheScaleX, cacheScaleY, crispAxisAligned,
                                            originPhaseX, subpixelRun,
                                            rotated ? xformQ : nullptr);
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
    built.requiresSmoothSampling = continuousRun;
    float invW = 1.0f / static_cast<float>(atlasW_);
    float invH = 1.0f / static_cast<float>(atlasH_);

    uint64_t glyphRasterHitsThisRun = 0;
    uint64_t glyphRasterMissesThisRun = 0;

    // A transformed DirectWrite analysis over the WHOLE fallback run keeps
    // glyph advances, CJK fallback baselines and ink bounds in the exact same
    // coordinate system as layout measurement. Per-glyph bearing/ppem rounding
    // is deliberately bypassed for rotation/skew and continuous text motion.
    // Track fallback runs independently: an emoji must not force the Latin,
    // CJK, or Arabic runs next to it back onto per-character positioning.
    std::vector<bool> wholeRuns(collector.runs.size(), false);
    std::vector<std::pair<size_t, size_t>> runSpans(collector.runs.size());
    if ((rotated || continuousRun) && dwriteFactory3_ &&
        effectiveAaMode != JALIUM_TEXT_AA_ALIASED) {
        auto appendWholeRun = [&](const auto& run) -> bool {
            if (!run.fontFace || run.glyphIndices.empty()) return true;
            ComPtr<IDWriteFontFace2> face2;
            if (SUCCEEDED(run.fontFace.As(&face2)) && face2 && face2->IsColorFont()) {
                return false; // retain authored colour layers in the glyph path
            }

            const float finalPpem = run.fontSize * dpiScale_ *
                (continuousRun ? continuousRasterScale : std::max(scaleX, scaleY));
            const float rasterFactor =
                finalPpem <= 32.0f && effectiveAaMode != JALIUM_TEXT_AA_ALIASED
                ? 2.0f : 1.0f;
            const float rasterRatio = continuousRun ? continuousRasterScale / scaleX : 1.0f;
            const float matrixScale = dpiScale_ * rasterFactor * rasterRatio;
            const DWRITE_MATRIX runTransform {
                lin11 * matrixScale, lin12 * matrixScale,
                lin21 * matrixScale, lin22 * matrixScale,
                0.0f, 0.0f };
            DWRITE_GLYPH_RUN glyphRun {};
            glyphRun.fontFace = run.fontFace.Get();
            glyphRun.fontEmSize = run.fontSize;
            glyphRun.glyphCount = static_cast<UINT32>(run.glyphIndices.size());
            glyphRun.glyphIndices = run.glyphIndices.data();
            glyphRun.glyphAdvances = run.glyphAdvances.empty()
                ? nullptr : run.glyphAdvances.data();
            glyphRun.glyphOffsets = run.glyphOffsets.empty()
                ? nullptr : run.glyphOffsets.data();
            glyphRun.isSideways = run.isSideways;
            glyphRun.bidiLevel = run.bidiLevel;

            ComPtr<IDWriteGlyphRunAnalysis> analysis;
            HRESULT hr = dwriteFactory3_->CreateGlyphRunAnalysis(
                &glyphRun, &runTransform,
                DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC,
                DWRITE_MEASURING_MODE_NATURAL,
                DWRITE_GRID_FIT_MODE_DISABLED,
                DWRITE_TEXT_ANTIALIAS_MODE_GRAYSCALE,
                run.baselineX, run.baselineY, &analysis);
            RECT bounds {};
            if (FAILED(hr) || !analysis ||
                FAILED(analysis->GetAlphaTextureBounds(
                    DWRITE_TEXTURE_ALIASED_1x1, &bounds))) {
                return false;
            }
            const int runW = bounds.right - bounds.left;
            const int runH = bounds.bottom - bounds.top;
            if (runW <= 0 || runH <= 0) return true;
            if (runW > 65535 || runH > 65535) {
                return false;
            }

            // Tile long runs instead of switching back to hinted glyphs
            // when they exceed the atlas width. Each tile stores a 1-texel
            // neighbour gutter for bilinear sampling, while its quad covers
            // only the interior, so adjacent tiles neither gap nor overlap.
            const int tileW = static_cast<int>(atlasW_) - 4;
            const int tileH = static_cast<int>(atlasH_) - 4;
            if (tileW <= 0 || tileH <= 0) return false;
            const float divisor = dpiScale_ * rasterFactor;
            const float divisorX = divisor * (continuousRun ? continuousRasterScale : 1.0f);
            const float divisorY = divisor * (continuousRun ? continuousRasterScale : 1.0f);
            for (int top = bounds.top; top < bounds.bottom; top += tileH) {
                for (int left = bounds.left; left < bounds.right; left += tileW) {
                    const int right = std::min(left + tileW, static_cast<int>(bounds.right));
                    const int bottom = std::min(top + tileH, static_cast<int>(bounds.bottom));
                    const RECT textureBounds {
                        std::max(left - 1, static_cast<int>(bounds.left)),
                        std::max(top - 1, static_cast<int>(bounds.top)),
                        std::min(right + 1, static_cast<int>(bounds.right)),
                        std::min(bottom + 1, static_cast<int>(bounds.bottom)) };
                    const int textureW = textureBounds.right - textureBounds.left;
                    const int textureH = textureBounds.bottom - textureBounds.top;
                    std::vector<uint8_t> alpha(static_cast<size_t>(textureW) * textureH);
                    if (FAILED(analysis->CreateAlphaTexture(
                            DWRITE_TEXTURE_ALIASED_1x1, &textureBounds,
                            alpha.data(), static_cast<UINT32>(alpha.size())))) return false;
                    uint16_t atlasX = 0, atlasY = 0;
                    if (!AllocateAtlasRect(static_cast<uint16_t>(textureW),
                            static_cast<uint16_t>(textureH), atlasX, atlasY)) return false;
                    for (int row = 0; row < textureH; ++row) {
                        for (int col = 0; col < textureW; ++col) {
                            const uint8_t coverage = alpha[static_cast<size_t>(row) * textureW + col];
                            const size_t dst = (static_cast<size_t>(atlasY + row) * atlasW_ + atlasX + col) * 4u;
                            atlasBitmap_[dst + 0] = coverage;
                            atlasBitmap_[dst + 1] = coverage;
                            atlasBitmap_[dst + 2] = coverage;
                            atlasBitmap_[dst + 3] = coverage;
                        }
                    }
                    dirty_ = true;
                    dirtyMinY_ = std::min(dirtyMinY_, atlasY);
                    dirtyMaxY_ = std::max(dirtyMaxY_, static_cast<uint16_t>(atlasY + textureH));
                    // Bilinear coverage extends beyond the ink bitmap.
                    // Include the transparent atlas padding at the run's
                    // outer edges; otherwise the quad itself cuts off that
                    // coverage each time its origin crosses a half pixel.
                    const int quadLeft = left == bounds.left ? left - 1 : left;
                    const int quadTop = top == bounds.top ? top - 1 : top;
                    const int quadRight = right == bounds.right ? right + 1 : right;
                    const int quadBottom = bottom == bounds.bottom ? bottom + 1 : bottom;
                    const float texelX = atlasX + quadLeft - textureBounds.left;
                    const float texelY = atlasY + quadTop - textureBounds.top;
                    GlyphQuadInstance inst {};
                    inst.posX = quadLeft / divisorX;
                    inst.posY = quadTop / divisorY;
                    inst.sizeX = (quadRight - quadLeft) / divisorX;
                    inst.sizeY = (quadBottom - quadTop) / divisorY;
                    inst.uvMinX = texelX * invW;
                    inst.uvMinY = texelY * invH;
                    inst.uvMaxX = (texelX + quadRight - quadLeft) * invW;
                    inst.uvMaxY = (texelY + quadBottom - quadTop) * invH;
                    built.instances.push_back(inst);
                }
            }
            built.requiresSmoothSampling |= continuousRun || rasterFactor > 1.0f;
            glyphRasterMissesThisRun += run.glyphIndices.size();
            return true;
        };
        for (size_t runIndex = 0; runIndex < collector.runs.size(); ++runIndex) {
            const size_t start = built.instances.size();
            wholeRuns[runIndex] = appendWholeRun(collector.runs[runIndex]);
            if (!wholeRuns[runIndex]) built.instances.resize(start);
            else runSpans[runIndex] = { start, built.instances.size() - start };
        }
    }

    for (size_t runIndex = 0; runIndex < collector.runs.size(); ++runIndex) {
        if (wholeRuns[runIndex]) continue;
        auto& run = collector.runs[runIndex];
        const size_t glyphRunStart = built.instances.size();
        float penX = run.baselineX;
        // Quantize the ordinary DPI-scaled ppem exactly as the identity path
        // has always done, then fold the vertical deformation into the em.
        // RasterizeGlyph uses scaleXQ/scaleYQ only as a horizontal aspect
        // matrix, so grid fitting sees the final vertical pixel size.
        float scaledSize = run.fontSize * dpiScale_;
        if (scaledSize <= 0) continue;
        long basePpemLong = std::lround(scaledSize);
        basePpemLong = std::clamp(basePpemLong, 1L, 65535L);
        const uint16_t basePpem = (uint16_t)basePpemLong;

        const float glyphRasterY = continuousRun ? continuousRasterScale : syR;
        const uint8_t glyphScaleXQ = continuousRun ? quantScale(continuousRasterScale) : scaleXQ;
        const uint8_t glyphScaleYQ = continuousRun ? quantScale(continuousRasterScale) : scaleYQ;
        long finalPpemLong = std::lround(basePpem * glyphRasterY);
        finalPpemLong = std::clamp(finalPpemLong, 1L, 65535L);
        const uint16_t fontSize = (uint16_t)finalPpemLong;

        // Symbol fonts are vector icon libraries, not running text. At small
        // display sizes their outlines often contain multiple sub-pixel stems
        // inside one glyph (Segoe MDL2 U+EB41's three tiny "w" shapes are a
        // representative case). Rasterizing directly at 15 ppem collapses
        // those stems into one hard row before the GPU ever sees them. Use a
        // 2x grayscale strike and resolve it with bilinear sampling instead:
        // one destination sample averages the corresponding 2x2 coverage
        // footprint, preserving the authored shape and smoothing the ring.
        // Explicit Aliased text remains exactly aliased by contract.
        constexpr uint16_t kSymbolSupersampleMaxPpem = 32;
        constexpr uint32_t kSymbolSupersampleFactor = 2;
        const bool symbolSupersampled =
            run.fontFace.Get() != nullptr && run.fontFace->IsSymbolFont() &&
            fontSize <= kSymbolSupersampleMaxPpem &&
            effectiveAaMode != JALIUM_TEXT_AA_ALIASED;

        // At small UI sizes a shallow rotation moves the top and bottom of one
        // glyph by less than one final pixel. Rasterizing that glyph directly at
        // 12-14 ppem quantizes most stems back to an upright-looking mask even
        // though the pen walk is correctly rotated. Build a 2x transformed
        // strike and resolve it once through the smooth sampler: the glyph
        // outline itself retains the angle without the 1x bilinear softening.
        constexpr uint16_t kRotatedSupersampleMaxPpem = 32;
        constexpr uint32_t kRotatedSupersampleFactor = 2;
        const bool rotatedSupersampled =
            rotated && fontSize <= kRotatedSupersampleMaxPpem &&
            effectiveAaMode != JALIUM_TEXT_AA_ALIASED;

        // Strike-size policy: small symbol fonts rasterize above final size and
        // shrink; oversized text rasterizes at most kMaxGlyphRasterPpem and
        // grows. strikeToFinalScale handles both directions. Above the cap
        // every ppem shares one strike per glyph, so deep zoom neither floods
        // the atlas with size buckets nor hits the oversized-ink rejection
        // that used to make big text vanish. Pen walk/layout stay at fontSize.
        const uint32_t supersampleFactor = symbolSupersampled
            ? kSymbolSupersampleFactor
            : (rotatedSupersampled ? kRotatedSupersampleFactor : 1u);
        const uint32_t requestedRasterPpem =
            (uint32_t)fontSize * supersampleFactor;
        const uint16_t rasterPpem = (uint16_t)(std::min<uint32_t>)(
            requestedRasterPpem, kMaxGlyphRasterPpem);
        const float strikeToFinalScale = fontSize / (float)rasterPpem;
        const bool ppemCapped = rasterPpem < fontSize;
        built.requiresSmoothSampling |= symbolSupersampled || rotatedSupersampled;
        if (AtlasTraceEnabled()) {
            AtlasTraceLog("[atlas] run ppem=%u raster=%u glyphs=%zu\n",
                    fontSize, rasterPpem, run.glyphIndices.size());
        }
        // An upscaled strike magnifies ClearType's 1px RGB fringes into a
        // visible colour halo — capped runs drop to grayscale coverage.
        const uint8_t runAaMode = ((ppemCapped || symbolSupersampled || rotatedSupersampled) &&
                                   effectiveAaMode == JALIUM_TEXT_AA_CLEARTYPE)
            ? (uint8_t)JALIUM_TEXT_AA_GRAYSCALE
            : (uint8_t)effectiveAaMode;

        // Rounding final ppem slightly changes the effective Y scale. Derive
        // both divisors from the exact cached raster so clean transforms map
        // atlas texels to screen pixels without an avoidable resample.
        const float rasterScaleY = fontSize / (float)basePpem;
        const float glyphAspectX = glyphScaleXQ / (float)glyphScaleYQ;
        const float rasterScaleX = rasterScaleY * glyphAspectX;

        float invDpi = 1.0f / dpiScale_;
        float invRasterX = 1.0f / rasterScaleX;
        float invRasterY = 1.0f / rasterScaleY;

        // Rotated path: the effective linear map DirectWrite actually
        // rasterized through is (final ppem / base ppem) * the quantized glyph
        // matrix. Walking the pen through THAT — rather than through the
        // caller's exact matrix — keeps inter-glyph spacing consistent with the
        // bitmaps' own scale, so no glyph drifts off its neighbours because the
        // ppem rounded or the matrix quantized.
        const float eff11 = rasterScaleY * xfR[0];
        const float eff12 = rasterScaleY * xfR[1];
        const float eff21 = rasterScaleY * xfR[2];
        const float eff22 = rasterScaleY * xfR[3];

        // Sub-pixel positioning policy for this run, resolved once from the
        // font's gasp table (the same call RasterizeGlyph makes per cache miss,
        // memoised).
        //
        // Grid-fit sizes rasterize the outline against the pixel grid, so the
        // SAME character comes out with measurably different ink depending on
        // which of the 8 phases it lands in — measured on Microsoft YaHei UI at
        // 12 ppem, one Latin glyph spans 4 px in some phases and 5 px in others,
        // with average coverage varying by ~25 %. In running text that reads as
        // "some characters are bigger/bolder than others". Pinning phase 0 makes
        // every instance of a character byte-identical, which is the same trade
        // GDI and WPF's Display mode make at these sizes: shape consistency
        // bought with up to a pixel of positional error. That is the
        // Display-mode (pen-snapped) policy. It is NOT a free trade: the
        // layout's advances are natural (fractional), so snapping each pen
        // independently lets two neighbouring gaps round in opposite
        // directions — a 14px bold "Desktop" rendered as "Desk to p". Ideal
        // mode (the managed default) therefore asks for sub-pixel positioning
        // (subpixelRun below) and keeps the phases even at grid-fit sizes.
        // Above the grid-fit range symmetric antialiasing renders phases
        // consistently, so there the phases stay in every mode.
        const DWRITE_MATRIX runGlyphXform {
            scaleXQ / (float)std::max<uint8_t>(scaleYQ, 1), 0.0f, 0.0f, 1.0f, 0.0f, 0.0f
        };
        const GaspPolicy runPolicy = ResolveGaspPolicy(
            run.fontFace.Get(), rasterPpem,
            (scaleXQ != scaleYQ) ? &runGlyphXform : nullptr,
            static_cast<uint8_t>(effectiveHintingMode));
        // Animated hinting (2) explicitly asks for grid fitting to be off so
        // text slides smoothly; honour that by keeping the sub-pixel phases.
        // Sub-pixel positioning (Ideal-mode text, and any text under a live
        // scale on the managed side) asks for the same thing for a different
        // reason: the pens are fractional — static text because the advances
        // are natural metrics, zooming text because they move continuously —
        // and a grid-fitted run pinned to phase 0 would place each glyph up to
        // half a pixel off, each in its own direction. Phases bound the
        // placement error to 1/8 px per glyph, which is what keeps the
        // spacing visually constant.
        // Above kMaxSubpixelPhasePpem the phases are pinned regardless: the
        // 1/8-px residual is an invisible fraction of such a glyph's advance,
        // while 8 cached variants of BIG strikes are what blow the atlas —
        // a zoomed CJK paragraph at ~140px × 8 phases × dozens of glyphs
        // exceeds even the 4096² maximum, and once the working set itself
        // cannot fit, every frame resets the atlas and drops a different
        // subset of glyphs: text flickers line by line.
        const bool useSubpixelPhases =
            !symbolSupersampled &&
            (subpixelRun || (effectiveHintingMode == 2) || !runPolicy.gridAligned) &&
            rasterPpem <= kMaxSubpixelPhasePpem;

        for (uint32_t i = 0; i < run.glyphIndices.size(); i++) {
            // Apply DirectWrite glyph offsets (kerning adjustments, mark positioning, etc.)
            float offsetX = 0, offsetY = 0;
            if (i < run.glyphOffsets.size()) {
                offsetX = run.glyphOffsets[i].advanceOffset;
                offsetY = run.glyphOffsets[i].ascenderOffset;
            }

            // Compute pen position in physical pixels for sub-pixel ClearType.
            // Sub-pixel positioning measures it from the final screen pen: the
            // origin's quantized fraction is added here (and removed again
            // below) so the phase — and the floor it is taken against — see
            // origin + pen rather than the layout-local pen alone.
            float penXPhysical = (penX + offsetX) * dpiScale_ + originPhaseOffset;
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
            // A capped run also pins phase 0: sub-pixel phases are invisible
            // once the strike is upscaled, and 8 variants of the biggest
            // glyphs would multiply the very atlas load the cap bounds.
            uint8_t subpixelQuant = (continuousRun || deformedOrRotated || !useSubpixelPhases ||
                                     ppemCapped || symbolSupersampled)
                ? (uint8_t)0
                : (uint8_t)std::min((int)(subpixelF * 8.0f), 7);

            GlyphKey key;
            key.fontFace = run.fontFace.Get();
            key.glyphIndex = run.glyphIndices[i];
            key.fontSize = rasterPpem;
            key.subpixelX = subpixelQuant;
            // Y is folded into fontSize; RasterizeGlyph treats the two scale
            // buckets as an X:Y aspect ratio.
            key.scaleXQ = glyphScaleXQ;
            key.scaleYQ = glyphScaleYQ;
            // The effective AA + hinting modes are baked into the key so the
            // same glyph rasterized in ClearType for one element doesn't get
            // re-emitted for a different element that asked for Grayscale or
            // Animated hinting — RasterizeGlyph reads them straight off the
            // key and skips the SyncAntialiasMode() fallback when set.
            key.aaMode = runAaMode;
            key.hintingMode = static_cast<uint8_t>(effectiveHintingMode);
            // Rotated / skewed: retain the quantized 2x2 for the oriented
            // instance basis and cache key. RasterizeGlyph intentionally keeps
            // the atlas strike upright.
            if (rotated) {
                key.xf11Q = xformQ[0]; key.xf12Q = xformQ[1];
                key.xf21Q = xformQ[2]; key.xf22Q = xformQ[3];
            }

            auto it = cache_.find(key);
            if (it == cache_.end()) {
                GlyphCacheValue val = {};
                if (!RasterizeGlyph(key, val.entry)) {
                    ++glyphRasterMissesThisRun;
                    continue;
                }
                // A full atlas reports the glyph as temporarily invalid while
                // scheduling a grow/reset. Never memoize that empty result:
                // GrowAtlas preserves cache_ entries, so an invalid entry here
                // would survive the generation bump and remain a permanent
                // missing character after the atlas has gained capacity.
                if (!val.entry.valid && (needsGrow_ || needsReset_)) {
                    ++glyphRasterMissesThisRun;
                    continue;
                }
                val.fontFaceRef = run.fontFace;
                val.lastUsedFrame = frameCounter_;
                it = cache_.emplace(key, std::move(val)).first;
                ++glyphRasterMissesThisRun;
            } else {
                ++glyphRasterHitsThisRun;
            }
            // Touch on every reference (hit or miss) — CompactAtlas keeps the
            // strikes stamped with a recent frame and reclaims the rest.
            it->second.lastUsedFrame = frameCounter_;

            auto& entry = it->second.entry;
            if (entry.valid && entry.w > 0 && entry.h > 0) {
                // Position the glyph quad at the integer-pixel pen + bearing.
                // The pen term is base-DIP (penX was shaped at run.fontSize);
                // entry.* are the final-ppem bitmap's physical pixels. Divide
                // them by the exact effective raster scales derived above to
                // return to base-DIP; AddText reapplies the actual transform.
                // 1:1 text snaps the pen to the integer pixel grid (the sub-pixel
                // phase is baked into the bitmap via subpixelX) for crisp stable
                // text. AXIS-ALIGNED scaled text (crispAxisAligned) does the SAME
                // floor: the bitmap is rasterized at the exact per-axis scale, so
                // once AddText integer-snaps the post-scaled physical quad the
                // POINT PSO samples it pixel-exact (this is what removes the soft
                // grey fringe on scaled/animated labels). ROTATED/SKEWED deform
                // keeps the pen CONTINUOUS (no floor): each glyph would otherwise
                // cross integer boundaries at a different sub-pixel instant as the
                // deform animates, so adjacent glyphs jump at different times (the
                // "left s still, right s moved" jitter); the bilinear PSO samples
                // that continuous position smoothly.
                // Without a sub-pixel phase there is no fractional offset baked
                // into the bitmap, so floor() would bias every glyph up to a
                // full pixel to the left and let that bias accumulate visibly
                // across a line. Round instead: the residual becomes +/-0.5 px
                // and averages out.
                float penXForPos;
                if (continuousRun || (deformedOrRotated && !crispAxisAligned)) {
                    penXForPos = penXPhysical;
                } else if (!deformedOrRotated && !useSubpixelPhases) {
                    penXForPos = std::round(penXPhysical);
                } else {
                    penXForPos = std::floor(penXPhysical);
                }
                // Sub-pixel positioning: back to layout-local space. The cached
                // quad then sits at floor(origin + pen) - originPhase, emitRun
                // adds the full origin, and AddText's whole-pixel snap sees
                // floor(origin + pen) + (originFrac - originPhase) with the
                // residual < 1/8 px — it resolves to exactly floor(origin + pen).
                penXForPos -= originPhaseOffset;

                // entry.* live in the RASTERIZED strike's pixels. upscale maps
                // them back to final-ppem pixels: the run-level ppem cap plus
                // the per-glyph oversized-ink shrink (entry.scale).
                const float upscale = strikeToFinalScale * entry.scale;
                const float padding = (continuousRun || effectiveHintingMode == 2 ||
                    symbolSupersampled || rotatedSupersampled ||
                    (rotated && runAaMode != JALIUM_TEXT_AA_ALIASED)) ? 1.0f : 0.0f;
                float glyphX, glyphY, quadW, quadH;
                float skewX = 0.0f, skewY = 0.0f;
                if (rotated) {
                    // The atlas strike is upright. Build its local top-left and
                    // dimensions exactly like the axis-aligned path, then map
                    // BOTH quad basis vectors through the effective 2x2. The VS
                    // therefore rotates/shears the character outline itself,
                    // not merely the glyph pen positions.
                    const float localX = penX + offsetX +
                        (entry.bearingX - padding) * upscale * invDpi * invRasterX;
                    const float localY = run.baselineY - offsetY -
                        (entry.bearingY + padding) * upscale * invDpi * invRasterY;
                    const float localW = (entry.w + 2.0f * padding) * upscale * invDpi * invRasterX;
                    const float localH = (entry.h + 2.0f * padding) * upscale * invDpi * invRasterY;
                    glyphX = localX * eff11 + localY * eff21;
                    glyphY = localX * eff12 + localY * eff22;
                    quadW = localW * eff11;
                    skewY = localW * eff12;
                    skewX = localH * eff21;
                    quadH = localH * eff22;
                } else {
                    glyphX = penXForPos * invDpi + (entry.bearingX - padding) * upscale * invDpi * invRasterX;
                    glyphY = run.baselineY - offsetY - (entry.bearingY + padding) * upscale * invDpi * invRasterY;
                    quadW = (entry.w + 2.0f * padding) * upscale * invDpi * invRasterX;
                    quadH = (entry.h + 2.0f * padding) * upscale * invDpi * invRasterY;
                }

                GlyphQuadInstance inst;
                inst.posX = glyphX;
                inst.posY = glyphY;
                inst.sizeX = quadW;
                inst.sizeY = quadH;
                inst.skewX = skewX;
                inst.skewY = skewY;
                inst.uvMinX = (entry.x - padding) * invW;
                inst.uvMinY = (entry.y - padding) * invH;
                inst.uvMaxX = (entry.x + entry.w + padding) * invW;
                inst.uvMaxY = (entry.y + entry.h + padding) * invH;
                inst.padX = inst.padY = 0.0f;
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
        runSpans[runIndex] = { glyphRunStart, built.instances.size() - glyphRunStart };
    }

    // Whole-run masks and colour glyphs are built in separate passes. Restore
    // DirectWrite's painter order when both occur in one layout, including
    // overlapping combining marks and colour-font fallback runs.
    if (std::any_of(wholeRuns.begin(), wholeRuns.end(), [](bool v) { return v; }) &&
        !std::all_of(wholeRuns.begin(), wholeRuns.end(), [](bool v) { return v; })) {
        std::vector<GlyphQuadInstance> ordered;
        ordered.reserve(built.instances.size());
        for (const auto& span : runSpans) {
            ordered.insert(ordered.end(), built.instances.begin() + span.first,
                built.instances.begin() + span.first + span.second);
        }
        built.instances = std::move(ordered);
    }

    // Build decorations unconditionally so the memo stays correct even if a
    // later same-key call passes outDecorations; emit gates on the pointer.
    for (auto& dec : collector.decorations) {
        TextDecorationRect rect;
        rect.x = dec.x;
        rect.y = dec.y;
        rect.width = dec.width;
        rect.thickness = std::max(dec.thickness, 1.0f);
        // Decorations stay in PRE-transform layout space on purpose: the
        // caller draws them as ordinary rects, which already run through the
        // ambient transform, so an underline rotates with its text for free.
        // (Glyph quads cannot do that — their ink is baked into the atlas —
        // which is why they carry the post-transform origin instead.)
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
                                            cacheScaleX, cacheScaleY, crispAxisAligned,
                                            originPhaseX, subpixelRun,
                                            rotated ? xformQ : nullptr);
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

bool D3D12GlyphAtlas::FlushToGpu(ID3D12GraphicsCommandList* cmdList)
{
    if (!dirty_ || !cmdList) return false;
    if (!atlasTexture_ || !EnsureUploadBuffer()) return false;

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
        return false;
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

    // The command list now owns a GPU read of this exact staging allocation.
    // Never Map it again: park it for the caller's current-frame fence
    // keepalive before any later FlushToGpu uploads newly-rasterized glyphs.
    QueueUploadBufferKeepaliveAfterCopy();

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
    return true;
}

} // namespace jalium
