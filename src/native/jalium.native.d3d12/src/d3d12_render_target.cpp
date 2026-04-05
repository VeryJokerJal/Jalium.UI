#include "d3d12_render_target.h"
#include "d3d12_resources.h"
#include "d3d12_triangulate.h"
#include "d3d12_vello.h"
#include <cstring>
#include <cmath>
#include <algorithm>
#include <cstdio>

namespace jalium {

// ============================================================================
// Construction / Destruction
// ============================================================================

D3D12RenderTarget::D3D12RenderTarget(D3D12Backend* backend, void* hwnd, int32_t width, int32_t height, bool useComposition)
    : backend_(backend)
    , hwnd_(static_cast<HWND>(hwnd))
    , isComposition_(useComposition)
{
    width_ = width;
    height_ = height;
}

D3D12RenderTarget::~D3D12RenderTarget() {
    if (isDrawing_ && directRenderer_) {
        directRenderer_->AbortFrame();
        isDrawing_ = false;
    }
    WaitForAllFrames();
    directRenderer_.reset();
    if (fenceEvent_) { CloseHandle(fenceEvent_); fenceEvent_ = nullptr; }
}

// ============================================================================
// Initialization
// ============================================================================

bool D3D12RenderTarget::Initialize() {
    if (!CreateSwapChain()) return false;

    // Create DirectRenderer
    directRenderer_ = std::make_unique<D3D12DirectRenderer>(backend_);
    if (!directRenderer_->Initialize(swapChain_.Get(), FrameCount))
        return false;

    float dpiScale = dpiX_ / 96.0f;
    directRenderer_->SetDpiScale(dpiScale > 0 ? dpiScale : 1.0f);

    // Create fence for Resize/Shutdown synchronization
    auto device = backend_->GetDevice();
    if (FAILED(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence_))))
        return false;
    fenceEvent_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (!fenceEvent_) return false;

    frameIndex_ = swapChain_->GetCurrentBackBufferIndex();
    return true;
}

// ============================================================================
// Swap Chain Creation
// ============================================================================

bool D3D12RenderTarget::CreateSwapChain() {
    auto factory = backend_->GetDXGIFactory();
    auto commandQueue = backend_->GetCommandQueue();
    if (!factory || !commandQueue) return false;

    // Check tearing support
    BOOL allowTearing = FALSE;
    ComPtr<IDXGIFactory5> factory5;
    if (SUCCEEDED(factory->QueryInterface(IID_PPV_ARGS(&factory5)))) {
        factory5->CheckFeatureSupport(DXGI_FEATURE_PRESENT_ALLOW_TEARING, &allowTearing, sizeof(allowTearing));
    }
    tearingSupported_ = (allowTearing == TRUE);

    DXGI_SWAP_CHAIN_DESC1 desc = {};
    desc.Width = width_;
    desc.Height = height_;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = FrameCount;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;

    ComPtr<IDXGISwapChain1> swapChain1;
    HRESULT hr;

    if (isComposition_) {
        desc.Scaling = DXGI_SCALING_STRETCH;
        desc.AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED;
        desc.Flags = 0;

        hr = factory->CreateSwapChainForComposition(commandQueue, &desc, nullptr, &swapChain1);
        if (FAILED(hr)) return false;

        // Set up DirectComposition
        hr = DCompositionCreateDevice(nullptr, IID_PPV_ARGS(&dcompDevice_));
        if (FAILED(hr)) return false;
        hr = dcompDevice_->CreateTargetForHwnd(hwnd_, TRUE, &dcompTarget_);
        if (FAILED(hr)) return false;
        hr = dcompDevice_->CreateVisual(&dcompVisual_);
        if (FAILED(hr)) return false;
        hr = dcompDevice_->CreateVisual(&dcompSwapChainVisual_);
        if (FAILED(hr)) return false;
        hr = dcompSwapChainVisual_->SetContent(swapChain1.Get());
        if (FAILED(hr)) return false;
        hr = dcompVisual_->AddVisual(dcompSwapChainVisual_.Get(), FALSE, nullptr);
        if (FAILED(hr)) return false;
        hr = dcompTarget_->SetRoot(dcompVisual_.Get());
        if (FAILED(hr)) return false;
        hr = dcompDevice_->Commit();
        if (FAILED(hr)) return false;
    } else {
        desc.Scaling = DXGI_SCALING_NONE;
        desc.Flags = tearingSupported_ ? DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING : 0;

        hr = factory->CreateSwapChainForHwnd(commandQueue, hwnd_, &desc, nullptr, nullptr, &swapChain1);
        if (FAILED(hr) && desc.Flags != 0) {
            desc.Flags = 0;
            hr = factory->CreateSwapChainForHwnd(commandQueue, hwnd_, &desc, nullptr, nullptr, &swapChain1);
        }
        if (FAILED(hr) && desc.Scaling == DXGI_SCALING_NONE) {
            desc.Scaling = DXGI_SCALING_STRETCH;
            hr = factory->CreateSwapChainForHwnd(commandQueue, hwnd_, &desc, nullptr, nullptr, &swapChain1);
        }
        if (FAILED(hr)) return false;
        factory->MakeWindowAssociation(hwnd_, DXGI_MWA_NO_ALT_ENTER);
    }

    swapChainCreationFlags_ = desc.Flags;
    if (!(swapChainCreationFlags_ & DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING)) {
        tearingSupported_ = false;
    }

    // Set dark background color to prevent white flash during resize
    DXGI_RGBA bgColor = { 0.157f, 0.157f, 0.157f, 1.0f };
    swapChain1->SetBackgroundColor(&bgColor);

    hr = swapChain1.As(&swapChain_);
    return SUCCEEDED(hr);
}

// ============================================================================
// Synchronization
// ============================================================================

void D3D12RenderTarget::WaitForAllFrames() {
    if (!fence_ || !fenceEvent_) return;
    auto queue = backend_->GetCommandQueue();
    if (!queue) return;

    uint64_t maxFv = 0;
    for (uint32_t i = 0; i < FrameCount; ++i)
        if (fenceValues_[i] > maxFv) maxFv = fenceValues_[i];

    uint64_t fv = maxFv + 1;
    if (FAILED(queue->Signal(fence_.Get(), fv))) return;
    if (fence_->GetCompletedValue() < fv) {
        fence_->SetEventOnCompletion(fv, fenceEvent_);
        WaitForSingleObject(fenceEvent_, 5000);
    }
    for (uint32_t i = 0; i < FrameCount; ++i) fenceValues_[i] = fv;
}

// ============================================================================
// Resize
// ============================================================================

JaliumResult D3D12RenderTarget::Resize(int32_t width, int32_t height) {
    if (width <= 0 || height <= 0) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (width == width_ && height == height_) return JALIUM_OK;

    if (isDrawing_) {
        if (directRenderer_) directRenderer_->AbortFrame();
        isDrawing_ = false;
    }

    // Single GPU wait via DirectRenderer (it owns all submitted GPU work)
    if (directRenderer_) {
        directRenderer_->ReleaseBackBufferReferences();
    } else {
        WaitForAllFrames();
    }

    HRESULT hr = swapChain_->ResizeBuffers(FrameCount, width, height,
        DXGI_FORMAT_R8G8B8A8_UNORM, swapChainCreationFlags_);
    if (FAILED(hr)) {
        auto* device = backend_ ? backend_->GetDevice() : nullptr;
        HRESULT removedReason = device ? device->GetDeviceRemovedReason() : E_FAIL;
        char buffer[256] = {};
        sprintf_s(buffer,
            "[D3D12RenderTarget] ResizeBuffers failed hr=0x%08X removedReason=0x%08X size=%dx%d\n",
            static_cast<unsigned int>(hr),
            static_cast<unsigned int>(removedReason),
            width,
            height);
        OutputDebugStringA(buffer);
        return FAILED(removedReason) ? JALIUM_ERROR_DEVICE_LOST : JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    width_ = width;
    height_ = height;
    frameIndex_ = swapChain_->GetCurrentBackBufferIndex();

    if (directRenderer_) {
        if (!directRenderer_->OnResize(static_cast<UINT>(width), static_cast<UINT>(height))) {
            return backend_ ? backend_->CheckDeviceStatus() : JALIUM_ERROR_RESOURCE_CREATION_FAILED;
        }
    }

    fullInvalidation_ = true;
    dirtyRects_.clear();
    return JALIUM_OK;
}

// ============================================================================
// BeginDraw / EndDraw
// ============================================================================

JaliumResult D3D12RenderTarget::BeginDraw() {
    static int debugRender = -1;
    if (debugRender < 0) {
        char* val = nullptr; size_t len = 0;
        debugRender = (_dupenv_s(&val, &len, "JALIUM_DEBUG_RENDER") == 0 && val && val[0] == '1') ? 1 : 0;
        free(val);
    }

    if (isDrawing_) {
        if (debugRender) OutputDebugStringA("[BeginDraw] FAIL: already drawing\n");
        return JALIUM_ERROR_INVALID_STATE;
    }
    if (!directRenderer_) {
        if (debugRender) OutputDebugStringA("[BeginDraw] FAIL: no directRenderer\n");
        return JALIUM_ERROR_INVALID_STATE;
    }

    if (backend_) {
        JaliumResult deviceStatus = backend_->CheckDeviceStatus();
        if (deviceStatus != JALIUM_OK) {
            if (debugRender) OutputDebugStringA("[BeginDraw] FAIL: device lost\n");
            return deviceStatus;
        }
    }

    float clearAlpha = isComposition_ ? 0.0f : clearA_;
    bool ok = directRenderer_->BeginFrame(
        frameIndex_,
        static_cast<UINT>(width_), static_cast<UINT>(height_),
        fullInvalidation_,
        clearR_, clearG_, clearB_, clearAlpha);
    if (!ok) {
        if (debugRender) {
            char buf[128];
            sprintf_s(buf, "[BeginDraw] FAIL: BeginFrame returned false (frame=%u, fence completed=%llu, expected=%llu)\n",
                frameIndex_,
                (unsigned long long)(directRenderer_->GetFenceCompletedValue()),
                (unsigned long long)(directRenderer_->GetFrameFenceValue(frameIndex_)));
            OutputDebugStringA(buf);
        }
        if (backend_) {
            JaliumResult deviceStatus = backend_->CheckDeviceStatus();
            if (deviceStatus != JALIUM_OK) {
                return deviceStatus;
            }
        }
        return JALIUM_ERROR_INVALID_STATE;
    }

    isDrawing_ = true;
    preGlassSnapshotCaptured_ = false;
    return JALIUM_OK;
}

JaliumResult D3D12RenderTarget::EndDraw() {
    if (!isDrawing_) return JALIUM_ERROR_INVALID_STATE;
    if (!directRenderer_) { isDrawing_ = false; return JALIUM_ERROR_INVALID_STATE; }

    // Flush any pending Vello paths before ending the frame
    if (directRenderer_->HasVelloPaths()) {
        directRenderer_->FlushVelloPaths();
    }

    UINT syncInterval = vsyncEnabled_ ? 1 : 0;
    UINT presentFlags = (!vsyncEnabled_ && tearingSupported_ && !isComposition_)
        ? DXGI_PRESENT_ALLOW_TEARING : 0;

    bool useDirty = !fullInvalidation_ && !dirtyRects_.empty();
    std::vector<D3D12_RECT> d3dDirtyRects;
    if (useDirty) {
        // Dirty rects are stored in DIPs but Present1 expects back-buffer pixel
        // coordinates.  Scale by DPI so the DWM updates the full rendered area;
        // without this, at DPI > 100% the runtime copies stale content over the
        // outer ring of freshly rendered pixels.
        float scale = directRenderer_->GetDpiScale();
        d3dDirtyRects.reserve(dirtyRects_.size());
        for (auto& dr : dirtyRects_) {
            D3D12_RECT r;
            r.left = (LONG)(dr.x * scale);
            r.top = (LONG)(dr.y * scale);
            r.right = (LONG)std::ceil((dr.x + dr.w) * scale);
            r.bottom = (LONG)std::ceil((dr.y + dr.h) * scale);
            d3dDirtyRects.push_back(r);
        }
    }
    JaliumResult endResult = directRenderer_->EndFrame(useDirty, d3dDirtyRects, syncInterval, presentFlags);

    if (endResult == JALIUM_OK && isComposition_ && dcompDevice_) {
        HRESULT hr = dcompDevice_->Commit();
        if (FAILED(hr)) { isDrawing_ = false; return JALIUM_ERROR_DEVICE_LOST; }
    }

    dirtyRects_.clear();
    fullInvalidation_ = false;
    frameIndex_ = swapChain_->GetCurrentBackBufferIndex();
    isDrawing_ = false;
    return endResult;
}

// ============================================================================
// Brush Helpers
// ============================================================================

bool D3D12RenderTarget::FillBrushToInstance(Brush* brush, SdfRectInstance& inst) {
    if (!brush) return false;
    auto type = brush->GetType();
    if (type == JALIUM_BRUSH_SOLID) {
        auto* sb = static_cast<D3D12SolidBrush*>(brush);
        inst.fillR = sb->r_; inst.fillG = sb->g_;
        inst.fillB = sb->b_; inst.fillA = sb->a_;
        inst.gradientType = 0;
        return true;
    }
    if (type == JALIUM_BRUSH_LINEAR_GRADIENT) {
        auto* lb = static_cast<D3D12LinearGradientBrush*>(brush);
        inst.gradientType = 1;
        inst.gradGeom0 = lb->startX_;
        inst.gradGeom1 = lb->startY_;
        inst.gradGeom2 = lb->endX_;
        inst.gradGeom3 = lb->endY_;
        inst.stopCount = (uint32_t)std::min<size_t>(lb->stops_.size(), 4);
        for (uint32_t i = 0; i < inst.stopCount; i++) {
            float* s = &inst.stop0Pos + i * 5;
            s[0] = lb->stops_[i].position;
            s[1] = lb->stops_[i].color.r;
            s[2] = lb->stops_[i].color.g;
            s[3] = lb->stops_[i].color.b;
            s[4] = lb->stops_[i].color.a;
        }
        return true;
    }
    if (type == JALIUM_BRUSH_RADIAL_GRADIENT) {
        auto* rb = static_cast<D3D12RadialGradientBrush*>(brush);
        inst.gradientType = 2;
        inst.gradGeom0 = rb->centerX_;
        inst.gradGeom1 = rb->centerY_;
        inst.gradGeom2 = rb->radiusX_;
        inst.gradGeom3 = rb->radiusY_;
        inst.stopCount = (uint32_t)std::min<size_t>(rb->stops_.size(), 4);
        for (uint32_t i = 0; i < inst.stopCount; i++) {
            float* s = &inst.stop0Pos + i * 5;
            s[0] = rb->stops_[i].position;
            s[1] = rb->stops_[i].color.r;
            s[2] = rb->stops_[i].color.g;
            s[3] = rb->stops_[i].color.b;
            s[4] = rb->stops_[i].color.a;
        }
        return true;
    }
    return false;
}

bool D3D12RenderTarget::ExtractBrushColor(Brush* brush, float& r, float& g, float& b, float& a) {
    if (!brush) return false;
    if (brush->GetType() != JALIUM_BRUSH_SOLID) return false;
    auto* sb = static_cast<D3D12SolidBrush*>(brush);
    r = sb->r_; g = sb->g_; b = sb->b_; a = sb->a_;
    return true;
}

// ============================================================================
// Drawing — Rectangles
// ============================================================================

void D3D12RenderTarget::Clear(float r, float g, float b, float a) {
    clearR_ = r; clearG_ = g; clearB_ = b; clearA_ = a;
    if (isDrawing_ && directRenderer_) {
        auto* cl = directRenderer_->GetCommandList();
        float clearColor[4] = { r, g, b, a };
        auto rtvHandle = directRenderer_->GetRtvHandle();
        cl->ClearRenderTargetView(rtvHandle, clearColor, 0, nullptr);
    }
}

void D3D12RenderTarget::FlushVelloIfNeeded() {
    // Mid-frame Vello flush is disabled: Dispatch may reallocate GPU buffers
    // (lineSegBuffer_, ptclBuffer_, etc.) which frees resources still referenced
    // by the current command list, causing OBJECT_DELETED_WHILE_STILL_IN_USE.
    // Vello paths are flushed once at EndDraw instead.
    // TODO: Implement deferred buffer management to support mid-frame flush
    // for correct Z-ordering of Vello paths with non-path draws.
}

void D3D12RenderTarget::FillRectangle(float x, float y, float w, float h, Brush* brush) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();
    if (directRenderer_->IsInOffscreenCapture()) {
        char buf[256];
        sprintf_s(buf, "[FillRect] OFFSCREEN CALL x=%.1f y=%.1f w=%.1f h=%.1f\n", x, y, w, h);
        OutputDebugStringA(buf);
    }
    SdfRectInstance inst = {};
    if (FillBrushToInstance(brush, inst)) {
        inst.posX = x; inst.posY = y; inst.sizeX = w; inst.sizeY = h;
        inst.opacity = directRenderer_->GetOpacity();
        directRenderer_->AddSdfRect(inst);
    }
}

void D3D12RenderTarget::DrawRectangle(float x, float y, float w, float h, Brush* brush, float strokeWidth) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();
    float r, g, b, a;
    if (ExtractBrushColor(brush, r, g, b, a)) {
        SdfRectInstance inst = {};
        inst.posX = x; inst.posY = y; inst.sizeX = w; inst.sizeY = h;
        inst.borderR = r; inst.borderG = g; inst.borderB = b; inst.borderA = a;
        inst.borderWidth = strokeWidth;
        inst.opacity = directRenderer_->GetOpacity();
        directRenderer_->AddSdfRect(inst);
    }
}

void D3D12RenderTarget::FillRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();
    if (directRenderer_->IsInOffscreenCapture()) {
        char buf[256];
        sprintf_s(buf, "[FillRoundedRect] OFFSCREEN x=%.1f y=%.1f w=%.1f h=%.1f brushType=%d\n",
            x, y, w, h, (int)brush->GetType());
        OutputDebugStringA(buf);
    }
    SdfRectInstance inst = {};
    bool brushOk = FillBrushToInstance(brush, inst);
    if (directRenderer_->IsInOffscreenCapture()) {
        char buf[128];
        sprintf_s(buf, "[FillRoundedRect] brushOk=%d fillR=%.2f fillA=%.2f\n",
            (int)brushOk, inst.fillR, inst.fillA);
        OutputDebugStringA(buf);
    }
    if (brushOk) {
        inst.posX = x; inst.posY = y; inst.sizeX = w; inst.sizeY = h;
        inst.cornerTL = rx; inst.cornerTR = rx; inst.cornerBR = rx; inst.cornerBL = rx;
        inst.opacity = directRenderer_->GetOpacity();
        directRenderer_->AddSdfRect(inst);
    }
}

void D3D12RenderTarget::DrawRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush, float strokeWidth) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();

    float r, g, b, a;
    if (ExtractBrushColor(brush, r, g, b, a)) {
        SdfRectInstance inst = {};
        inst.posX = x; inst.posY = y; inst.sizeX = w; inst.sizeY = h;
        inst.cornerTL = rx; inst.cornerTR = rx; inst.cornerBR = rx; inst.cornerBL = rx;
        inst.borderR = r; inst.borderG = g; inst.borderB = b; inst.borderA = a;
        inst.borderWidth = strokeWidth;
        inst.opacity = directRenderer_->GetOpacity();
        directRenderer_->AddSdfRect(inst);
    }
}

void D3D12RenderTarget::FillPerCornerRoundedRectangle(float x, float y, float w, float h,
    float tl, float tr, float br, float bl, Brush* brush) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();

    SdfRectInstance inst = {};
    if (FillBrushToInstance(brush, inst)) {
        inst.posX = x; inst.posY = y; inst.sizeX = w; inst.sizeY = h;
        inst.cornerTL = tl; inst.cornerTR = tr; inst.cornerBR = br; inst.cornerBL = bl;
        inst.opacity = directRenderer_->GetOpacity();
        directRenderer_->AddSdfRect(inst);
    }
}

void D3D12RenderTarget::DrawPerCornerRoundedRectangle(float x, float y, float w, float h,
    float tl, float tr, float br, float bl, Brush* brush, float strokeWidth) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();

    float r, g, b, a;
    if (ExtractBrushColor(brush, r, g, b, a)) {
        SdfRectInstance inst = {};
        inst.posX = x; inst.posY = y; inst.sizeX = w; inst.sizeY = h;
        inst.cornerTL = tl; inst.cornerTR = tr; inst.cornerBR = br; inst.cornerBL = bl;
        inst.borderR = r; inst.borderG = g; inst.borderB = b; inst.borderA = a;
        inst.borderWidth = strokeWidth;
        inst.opacity = directRenderer_->GetOpacity();
        directRenderer_->AddSdfRect(inst);
    }
}

// ============================================================================
// Drawing — Ellipses (approximated with SuperEllipse SDF)
// ============================================================================

void D3D12RenderTarget::FillEllipse(float cx, float cy, float rx, float ry, Brush* brush) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();

    SdfRectInstance inst = {};
    if (FillBrushToInstance(brush, inst)) {
        inst.posX = cx - rx; inst.posY = cy - ry;
        inst.sizeX = rx * 2; inst.sizeY = ry * 2;
        inst.cornerTL = rx; inst.cornerTR = rx; inst.cornerBR = rx; inst.cornerBL = rx;
        inst.opacity = directRenderer_->GetOpacity();
        directRenderer_->AddSdfRect(inst);
    }
}

void D3D12RenderTarget::FillEllipseBatch(const float* data, uint32_t count) {
    if (!isDrawing_ || !directRenderer_ || !data || count == 0) return;
    FlushVelloIfNeeded();
    // Layout per element (stride = 5): cx, cy, rx, ry, packedRGBA
    // packedRGBA is a uint32 stored as float bits: R | (G<<8) | (B<<16) | (A<<24)
    constexpr uint32_t kStride = 5;
    for (uint32_t i = 0; i < count; i++) {
        uint32_t base = i * kStride;
        float cx = data[base + 0];
        float cy = data[base + 1];
        float rx = data[base + 2];
        float ry = data[base + 3];

        // Unpack RGBA from float bits
        uint32_t packed;
        memcpy(&packed, &data[base + 4], sizeof(uint32_t));
        float r = (packed & 0xFF) / 255.0f;
        float g = ((packed >> 8) & 0xFF) / 255.0f;
        float b = ((packed >> 16) & 0xFF) / 255.0f;
        float a = ((packed >> 24) & 0xFF) / 255.0f;

        SdfRectInstance inst = {};
        inst.posX = cx - rx; inst.posY = cy - ry;
        inst.sizeX = rx * 2; inst.sizeY = ry * 2;
        inst.cornerTL = rx; inst.cornerTR = rx; inst.cornerBR = rx; inst.cornerBL = rx;
        inst.fillR = r; inst.fillG = g; inst.fillB = b; inst.fillA = a;
        inst.opacity = directRenderer_->GetOpacity();
        directRenderer_->AddSdfRect(inst);
    }
}

void D3D12RenderTarget::DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();
    float r, g, b, a;
    if (ExtractBrushColor(brush, r, g, b, a)) {
        SdfRectInstance inst = {};
        inst.posX = cx - rx; inst.posY = cy - ry;
        inst.sizeX = rx * 2; inst.sizeY = ry * 2;
        inst.cornerTL = rx; inst.cornerTR = rx; inst.cornerBR = rx; inst.cornerBL = rx;
        inst.borderR = r; inst.borderG = g; inst.borderB = b; inst.borderA = a;
        inst.borderWidth = strokeWidth;
        inst.opacity = directRenderer_->GetOpacity();
        directRenderer_->AddSdfRect(inst);
    }
}

// ============================================================================
// Drawing — Lines
// ============================================================================

void D3D12RenderTarget::DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();
    float r, g, b, a;
    if (!ExtractBrushColor(brush, r, g, b, a)) return;

    float dx = x2 - x1, dy = y2 - y1;
    float len = std::sqrt(dx * dx + dy * dy);
    if (len < 0.001f) return;

    // Build two triangles for the line
    float nx = -dy / len * strokeWidth * 0.5f;
    float ny =  dx / len * strokeWidth * 0.5f;
    float opacity = directRenderer_->GetOpacity();
    float pr = r * a * opacity, pg = g * a * opacity, pb = b * a * opacity, pa = a * opacity;

    TriangleVertex verts[6] = {
        { x1 + nx, y1 + ny, pr, pg, pb, pa },
        { x1 - nx, y1 - ny, pr, pg, pb, pa },
        { x2 + nx, y2 + ny, pr, pg, pb, pa },
        { x2 + nx, y2 + ny, pr, pg, pb, pa },
        { x1 - nx, y1 - ny, pr, pg, pb, pa },
        { x2 - nx, y2 - ny, pr, pg, pb, pa },
    };
    directRenderer_->AddTriangles(verts, 6);
}

// ============================================================================
// Drawing — Polygons & Paths (triangulated)
// ============================================================================

// Check if a polygon is convex by verifying all cross products have the same sign.
static bool IsConvexPolygon(const float* points, uint32_t count) {
    if (count < 3) return false;
    bool hasPositive = false, hasNegative = false;
    for (uint32_t i = 0; i < count; i++) {
        uint32_t j = (i + 1) % count;
        uint32_t k = (i + 2) % count;
        float ax = points[j * 2] - points[i * 2];
        float ay = points[j * 2 + 1] - points[i * 2 + 1];
        float bx = points[k * 2] - points[j * 2];
        float by = points[k * 2 + 1] - points[j * 2 + 1];
        float cross = ax * by - ay * bx;
        if (cross > 1e-6f) hasPositive = true;
        if (cross < -1e-6f) hasNegative = true;
        if (hasPositive && hasNegative) return false;
    }
    return true;
}

void D3D12RenderTarget::FillPolygon(const float* points, uint32_t pointCount, Brush* brush, int32_t fillRule) {
    if (!isDrawing_ || !brush || !directRenderer_ || pointCount < 3) return;

    // Route non-solid brushes (gradients) through Vello for GPU rendering
    if (brush->GetType() != JALIUM_BRUSH_SOLID) {
        auto* vello = directRenderer_->GetVelloRenderer();
        if (vello) {
            directRenderer_->ApplyScissorToVello();
            // Build LineTo command buffer from polygon points
            std::vector<float> cmds;
            cmds.reserve(pointCount * 3);
            for (uint32_t i = 1; i < pointCount; i++) {
                cmds.push_back(0); // LineTo tag
                cmds.push_back(points[i * 2]);
                cmds.push_back(points[i * 2 + 1]);
            }
            cmds.push_back(5); // ClosePath tag
            float opacity = directRenderer_->GetOpacity();
            auto t = directRenderer_->GetCurrentTransform();
            float dpiScale = directRenderer_->GetDpiScale();
            float vm11 = t.m11 * dpiScale, vm12 = t.m12 * dpiScale;
            float vm21 = t.m21 * dpiScale, vm22 = t.m22 * dpiScale;
            float vdx  = t.dx  * dpiScale, vdy  = t.dy  * dpiScale;
            if (vello->EncodeFillPathBrush(points[0], points[1], cmds.data(), (uint32_t)cmds.size(),
                    brush, (uint32_t)fillRule, opacity,
                    vm11, vm12, vm21, vm22, vdx, vdy))
                return;
        }
    }

    FlushVelloIfNeeded();
    float r, g, b, a;
    if (!ExtractBrushColor(brush, r, g, b, a)) return;

    float opacity = directRenderer_->GetOpacity();
    float pr = r * a * opacity, pg = g * a * opacity, pb = b * a * opacity, pa = a * opacity;

    // For convex polygons, fan triangulation is simple and always correct.
    // For concave polygons, use ear-clipping for proper triangulation.
    if (IsConvexPolygon(points, pointCount)) {
        std::vector<TriangleVertex> verts;
        verts.reserve((pointCount - 2) * 3);
        for (uint32_t i = 1; i + 1 < pointCount; i++) {
            verts.push_back({ points[0], points[1], pr, pg, pb, pa });
            verts.push_back({ points[i * 2], points[i * 2 + 1], pr, pg, pb, pa });
            verts.push_back({ points[(i + 1) * 2], points[(i + 1) * 2 + 1], pr, pg, pb, pa });
        }
        if (!verts.empty())
            directRenderer_->AddTriangles(verts.data(), (uint32_t)verts.size());
        return;
    }

    // Concave polygon: ear-clipping triangulation
    std::vector<uint32_t> indices;
    if (!TriangulatePolygon(points, pointCount, indices) || indices.size() < 3) {
        // Fallback to fan triangulation for degenerate cases
        std::vector<TriangleVertex> verts;
        verts.reserve((pointCount - 2) * 3);
        for (uint32_t i = 1; i + 1 < pointCount; i++) {
            verts.push_back({ points[0], points[1], pr, pg, pb, pa });
            verts.push_back({ points[i * 2], points[i * 2 + 1], pr, pg, pb, pa });
            verts.push_back({ points[(i + 1) * 2], points[(i + 1) * 2 + 1], pr, pg, pb, pa });
        }
        if (!verts.empty())
            directRenderer_->AddTriangles(verts.data(), (uint32_t)verts.size());
        return;
    }

    std::vector<TriangleVertex> verts;
    verts.reserve(indices.size());
    for (uint32_t idx : indices) {
        verts.push_back({ points[idx * 2], points[idx * 2 + 1], pr, pg, pb, pa });
    }
    if (!verts.empty())
        directRenderer_->AddTriangles(verts.data(), (uint32_t)verts.size());
}

void D3D12RenderTarget::DrawPolygon(const float* points, uint32_t pointCount, Brush* brush, float strokeWidth, bool closed,
    int32_t /*lineJoin*/, float /*miterLimit*/) {
    if (!isDrawing_ || !brush || !directRenderer_ || pointCount < 2) return;
    FlushVelloIfNeeded();
    float r, g, b, a;
    if (!ExtractBrushColor(brush, r, g, b, a)) return;

    float opacity = directRenderer_->GetOpacity();
    float pr = r * a * opacity, pg = g * a * opacity, pb = b * a * opacity, pa = a * opacity;
    float hw = strokeWidth * 0.5f;

    uint32_t segCount = closed ? pointCount : pointCount - 1;
    std::vector<TriangleVertex> verts;
    verts.reserve(segCount * 6);

    for (uint32_t i = 0; i < segCount; i++) {
        uint32_t j = (i + 1) % pointCount;
        float x1 = points[i * 2], y1 = points[i * 2 + 1];
        float x2 = points[j * 2], y2 = points[j * 2 + 1];
        float dx = x2 - x1, dy = y2 - y1;
        float len = std::sqrt(dx * dx + dy * dy);
        if (len < 0.001f) continue;
        float nx = -dy / len * hw, ny = dx / len * hw;
        verts.push_back({ x1 + nx, y1 + ny, pr, pg, pb, pa });
        verts.push_back({ x1 - nx, y1 - ny, pr, pg, pb, pa });
        verts.push_back({ x2 + nx, y2 + ny, pr, pg, pb, pa });
        verts.push_back({ x2 + nx, y2 + ny, pr, pg, pb, pa });
        verts.push_back({ x1 - nx, y1 - ny, pr, pg, pb, pa });
        verts.push_back({ x2 - nx, y2 - ny, pr, pg, pb, pa });
    }
    if (!verts.empty())
        directRenderer_->AddTriangles(verts.data(), (uint32_t)verts.size());
}

void D3D12RenderTarget::FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule) {
    if (!isDrawing_ || !brush || !directRenderer_) return;

    // Route through Vello GPU path renderer when available
    auto* vello = directRenderer_->GetVelloRenderer();
    if (vello) {
        // Pass current scissor to Vello for per-path bbox clamping
        directRenderer_->ApplyScissorToVello();
        float opacity = directRenderer_->GetOpacity();
        auto t = directRenderer_->GetCurrentTransform();
        float dpiScale = directRenderer_->GetDpiScale();
        // Scale DIP coordinates to physical pixels for Vello's pixel-space rendering
        float vm11 = t.m11 * dpiScale, vm12 = t.m12 * dpiScale;
        float vm21 = t.m21 * dpiScale, vm22 = t.m22 * dpiScale;
        float vdx  = t.dx  * dpiScale, vdy  = t.dy  * dpiScale;
        if (vello->EncodeFillPathBrush(startX, startY, commands, commandLength,
                brush, (uint32_t)fillRule, opacity,
                vm11, vm12, vm21, vm22, vdx, vdy))
            return;
        // Vello encoding failed (unsupported brush, degenerate path, etc.) — fall through to CPU
    }

    // CPU triangulation fallback
    float r, g, b, a;
    if (!ExtractBrushColor(brush, r, g, b, a)) return;

    float opacity = directRenderer_->GetOpacity();
    float pr = r * a * opacity, pg = g * a * opacity, pb = b * a * opacity, pa = a * opacity;

    // Flatten path commands into contours (one per sub-path)
    std::vector<Contour> contours;
    contours.push_back(Contour{});
    contours.back().points.push_back(startX);
    contours.back().points.push_back(startY);

    uint32_t i = 0;
    while (i < commandLength) {
        int cmd = (int)commands[i++];
        auto& cur = contours.back().points;
        if (cmd == 0 && i + 1 < commandLength) { // LineTo
            cur.push_back(commands[i++]);
            cur.push_back(commands[i++]);
        } else if (cmd == 1 && i + 5 < commandLength) { // BezierTo
            float cx1 = commands[i++], cy1 = commands[i++];
            float cx2 = commands[i++], cy2 = commands[i++];
            float ex  = commands[i++], ey  = commands[i++];
            float sx = cur[cur.size() - 2], sy = cur[cur.size() - 1];
            const int N = 12;
            for (int s = 1; s <= N; s++) {
                float t = (float)s / N;
                float u = 1 - t;
                cur.push_back(u*u*u*sx + 3*u*u*t*cx1 + 3*u*t*t*cx2 + t*t*t*ex);
                cur.push_back(u*u*u*sy + 3*u*u*t*cy1 + 3*u*t*t*cy2 + t*t*t*ey);
            }
        } else if (cmd == 2 && i + 1 < commandLength) { // MoveTo — new contour
            contours.push_back(Contour{});
            contours.back().points.push_back(commands[i++]);
            contours.back().points.push_back(commands[i++]);
        } else if (cmd == 3 && i + 3 < commandLength) { // QuadTo
            float cx = commands[i++], cy = commands[i++];
            float ex = commands[i++], ey = commands[i++];
            float sx = cur[cur.size() - 2], sy = cur[cur.size() - 1];
            const int N = 8;
            for (int s = 1; s <= N; s++) {
                float t = (float)s / N;
                float u = 1 - t;
                cur.push_back(u*u*sx + 2*u*t*cx + t*t*ex);
                cur.push_back(u*u*sy + 2*u*t*cy + t*t*ey);
            }
        } else if (cmd == 5) { // ClosePath — no-op for fill
            // no-op
        } else {
            break;
        }
    }

    // Remove empty contours
    contours.erase(
        std::remove_if(contours.begin(), contours.end(),
            [](const Contour& c) { return c.VertexCount() < 3; }),
        contours.end());

    if (contours.empty()) return;

    // Use compound path triangulation with fill rule support
    std::vector<float> triVerts;
    if (TriangulateCompoundPath(contours, fillRule, triVerts) && triVerts.size() >= 6) {
        std::vector<TriangleVertex> verts;
        verts.reserve(triVerts.size() / 2);
        for (uint32_t v = 0; v + 1 < (uint32_t)triVerts.size(); v += 2) {
            verts.push_back({ triVerts[v], triVerts[v + 1], pr, pg, pb, pa });
        }
        directRenderer_->AddTriangles(verts.data(), (uint32_t)verts.size());
    } else if (contours.size() == 1) {
        // Fallback: simple polygon fill for single contour
        FillPolygon(contours[0].points.data(), contours[0].VertexCount(), brush, fillRule);
    }
}

void D3D12RenderTarget::StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed,
    int32_t lineJoin, float miterLimit, int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset) {
    if (!isDrawing_ || !brush || !directRenderer_) return;

    // Route through Vello GPU path renderer when available
    auto* vello = directRenderer_->GetVelloRenderer();
    if (vello) {
        directRenderer_->ApplyScissorToVello();
        float opacity = directRenderer_->GetOpacity();
        auto t = directRenderer_->GetCurrentTransform();
        float dpiScale = directRenderer_->GetDpiScale();
        // Scale DIP coordinates to physical pixels for Vello's pixel-space rendering
        float vm11 = t.m11 * dpiScale, vm12 = t.m12 * dpiScale;
        float vm21 = t.m21 * dpiScale, vm22 = t.m22 * dpiScale;
        float vdx  = t.dx  * dpiScale, vdy  = t.dy  * dpiScale;
        if (vello->EncodeStrokePathBrush(startX, startY, commands, commandLength,
                brush, strokeWidth, closed, lineJoin, miterLimit, opacity,
                lineCap, dashPattern, dashCount, dashOffset,
                vm11, vm12, vm21, vm22, vdx, vdy))
            return;
        // Vello encoding failed — fall through to CPU
    }

    // CPU polyline fallback: flatten to polyline, then stroke as polygon outline
    std::vector<float> pts;
    pts.push_back(startX);
    pts.push_back(startY);
    uint32_t i = 0;
    while (i < commandLength) {
        int cmd = (int)commands[i++];
        if (cmd == 0 && i + 1 < commandLength) { // LineTo [x, y]
            pts.push_back(commands[i++]);
            pts.push_back(commands[i++]);
        } else if (cmd == 1 && i + 5 < commandLength) { // BezierTo [cx1, cy1, cx2, cy2, ex, ey]
            float cx1 = commands[i++], cy1 = commands[i++];
            float cx2 = commands[i++], cy2 = commands[i++];
            float ex  = commands[i++], ey  = commands[i++];
            float sx = pts[pts.size() - 2], sy = pts[pts.size() - 1];
            const int N = 12;
            for (int s = 1; s <= N; s++) {
                float t = (float)s / N;
                float u = 1 - t;
                pts.push_back(u*u*u*sx + 3*u*u*t*cx1 + 3*u*t*t*cx2 + t*t*t*ex);
                pts.push_back(u*u*u*sy + 3*u*u*t*cy1 + 3*u*t*t*cy2 + t*t*t*ey);
            }
        } else if (cmd == 2 && i + 1 < commandLength) { // MoveTo [x, y]
            pts.push_back(commands[i++]);
            pts.push_back(commands[i++]);
        } else if (cmd == 3 && i + 3 < commandLength) { // QuadTo [cx, cy, ex, ey]
            float cx = commands[i++], cy = commands[i++];
            float ex = commands[i++], ey = commands[i++];
            float sx = pts[pts.size() - 2], sy = pts[pts.size() - 1];
            const int N = 8;
            for (int s = 1; s <= N; s++) {
                float t = (float)s / N;
                float u = 1 - t;
                pts.push_back(u*u*sx + 2*u*t*cx + t*t*ex);
                pts.push_back(u*u*sy + 2*u*t*cy + t*t*ey);
            }
        } else if (cmd == 5) { // ClosePath
            closed = true;
        } else {
            break;
        }
    }
    DrawPolygon(pts.data(), (uint32_t)(pts.size() / 2), brush, strokeWidth, closed, lineJoin, miterLimit);
}

void D3D12RenderTarget::DrawContentBorder(float x, float y, float w, float h,
    float blRadius, float brRadius,
    Brush* fillBrush, Brush* strokeBrush, float strokeWidth)
{
    if (!isDrawing_ || !directRenderer_) return;
    FlushVelloIfNeeded();
    // Fill with bottom corner radii
    if (fillBrush) {
        SdfRectInstance inst = {};
        if (FillBrushToInstance(fillBrush, inst)) {
            inst.posX = x; inst.posY = y; inst.sizeX = w; inst.sizeY = h;
            inst.cornerBL = blRadius; inst.cornerBR = brRadius;
            inst.opacity = directRenderer_->GetOpacity();
            directRenderer_->AddSdfRect(inst);
        }
    }
    // Stroke: 3-sided U shape (left, bottom, right)
    if (strokeBrush && strokeWidth > 0) {
        float r, g, b, a;
        if (ExtractBrushColor(strokeBrush, r, g, b, a)) {
            SdfRectInstance inst = {};
            inst.posX = x; inst.posY = y; inst.sizeX = w; inst.sizeY = h;
            inst.cornerBL = blRadius; inst.cornerBR = brRadius;
            inst.borderR = r; inst.borderG = g; inst.borderB = b; inst.borderA = a;
            inst.borderWidth = strokeWidth;
            inst.opacity = directRenderer_->GetOpacity();
            directRenderer_->AddSdfRect(inst);
        }
    }
}

// ============================================================================
// Drawing — Text
// ============================================================================

void D3D12RenderTarget::RenderText(
    const wchar_t* text, uint32_t textLength,
    TextFormat* format,
    float x, float y, float w, float h,
    Brush* brush)
{
    if (!isDrawing_ || !directRenderer_ || !format || !text || textLength == 0) return;
    FlushVelloIfNeeded();

    auto* tf = static_cast<D3D12TextFormat*>(format);
    float r = 1, g = 1, b = 1, a = 1;
    ExtractBrushColor(brush, r, g, b, a);

    ComPtr<IDWriteTextLayout> layout;
    if (FAILED(tf->CreateLayout(text, textLength, w, h, &layout))) return;
    directRenderer_->AddText(layout.Get(), x, y, r, g, b, a);
}

// ============================================================================
// Drawing — Bitmaps
// ============================================================================

void D3D12RenderTarget::DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity) {
    if (!isDrawing_ || !directRenderer_ || !bitmap) return;
    FlushVelloIfNeeded();

    auto* d3d12Bmp = static_cast<D3D12Bitmap*>(bitmap);
    auto* cl = directRenderer_->GetCommandList();
    auto* tex = d3d12Bmp->GetOrCreateD3D12Texture(backend_->GetDevice(), cl);
    if (!tex) return;
    // Query the actual texture format — WIC bitmaps are typically B8G8R8A8, not R8G8B8A8.
    // Using the wrong format family for the SRV causes D3D12 validation failure → device lost.
    auto texDesc = tex->GetDesc();
    directRenderer_->AddBitmap(x, y, w, h, opacity * directRenderer_->GetOpacity(),
                                tex, texDesc.Format);
}

// ============================================================================
// State — Transform, Clip, Opacity
// ============================================================================

void D3D12RenderTarget::PushTransform(const float* matrix) {
    if (!directRenderer_) return;
    directRenderer_->PushTransform(matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5]);
}

void D3D12RenderTarget::PopTransform() {
    if (directRenderer_) directRenderer_->PopTransform();
}

void D3D12RenderTarget::PushClip(float x, float y, float w, float h) {
    if (directRenderer_ && directRenderer_->IsInOffscreenCapture()) {
        OutputDebugStringA("[PushClipRect OFFSCREEN] called\n");
    }
    if (directRenderer_) directRenderer_->PushScissor(x, y, w, h);
}

void D3D12RenderTarget::PushClipAliased(float x, float y, float w, float h) {
    if (directRenderer_) directRenderer_->PushScissor(x, y, w, h);
}

void D3D12RenderTarget::PushRoundedRectClip(float x, float y, float w, float h, float /*rx*/, float /*ry*/) {
    // Approximate with axis-aligned scissor (no stencil support yet)
    if (directRenderer_) directRenderer_->PushScissor(x, y, w, h);
}

void D3D12RenderTarget::PopClip() {
    if (directRenderer_ && directRenderer_->IsInOffscreenCapture()) {
        OutputDebugStringA("[PopClipRect OFFSCREEN] called\n");
    }
    if (directRenderer_) directRenderer_->PopScissor();
}

void D3D12RenderTarget::PunchTransparentRect(float x, float y, float w, float h) {
    if (!isDrawing_ || !directRenderer_) return;
    directRenderer_->PunchTransparentRect(x, y, w, h);
}

void D3D12RenderTarget::PushOpacity(float opacity) {
    if (!directRenderer_) return;
    opacityStack_.push(directRenderer_->GetOpacity());
    directRenderer_->SetOpacity(directRenderer_->GetOpacity() * opacity);
}

void D3D12RenderTarget::PopOpacity() {
    if (!directRenderer_ || opacityStack_.empty()) return;
    directRenderer_->SetOpacity(opacityStack_.top());
    opacityStack_.pop();
}

void D3D12RenderTarget::SetShapeType(int type, float n) {
    if (directRenderer_) directRenderer_->SetShapeType((float)type, n);
}

void D3D12RenderTarget::SetVSyncEnabled(bool enabled) { vsyncEnabled_ = enabled; }

void D3D12RenderTarget::SetDpi(float dpiX, float dpiY) {
    dpiX_ = dpiX;
    dpiY_ = dpiY;
    if (directRenderer_) {
        float scale = dpiX / 96.0f;
        directRenderer_->SetDpiScale(scale > 0 ? scale : 1.0f);
    }
}

// ============================================================================
// Dirty Rect Tracking
// ============================================================================

void D3D12RenderTarget::AddDirtyRect(float x, float y, float w, float h) {
    if (fullInvalidation_) return;
    float margin = DirtyRectMargin;
    float nx = (std::max)(x - margin, 0.0f);
    float ny = (std::max)(y - margin, 0.0f);
    float nw = w + margin * 2;
    float nh = h + margin * 2;

    // Merge into existing rects or add new
    if (dirtyRects_.size() >= MaxDirtyRects) {
        fullInvalidation_ = true;
        dirtyRects_.clear();
        return;
    }
    dirtyRects_.push_back({ nx, ny, nw, nh });
}

void D3D12RenderTarget::SetFullInvalidation() {
    fullInvalidation_ = true;
    dirtyRects_.clear();
}

// ============================================================================
// Effects — Backdrop Filter, Liquid Glass, Glow, etc.
// ============================================================================

void D3D12RenderTarget::DrawBackdropFilter(
    float x, float y, float w, float h,
    const char*, const char*, const char*,
    float tintOpacity, float blurRadius,
    float cornerRadiusTL, float, float, float)
{
    if (!isDrawing_ || !directRenderer_) return;
    directRenderer_->FlushGraphicsForCompute();
    if (!directRenderer_->CaptureSnapshot()) return;
    float avgRadius = cornerRadiusTL;
    directRenderer_->DrawSnapshotBlurred(x, y, w, h, blurRadius, 0, 0, 0, tintOpacity, avgRadius);
}

void D3D12RenderTarget::DrawGlowingBorderHighlight(
    float x, float y, float w, float h,
    float animationPhase, float r, float g, float b,
    float strokeWidth, float trailLength, float dimOpacity,
    float screenWidth, float screenHeight)
{
    if (!isDrawing_ || !directRenderer_) return;
    directRenderer_->DrawGlowingBorderHighlight(x, y, w, h, animationPhase, r, g, b,
        strokeWidth, trailLength, dimOpacity, screenWidth, screenHeight);
}

void D3D12RenderTarget::DrawGlowingBorderTransition(
    float fromX, float fromY, float fromW, float fromH,
    float toX, float toY, float toW, float toH,
    float headProgress, float tailProgress,
    float animationPhase, float r, float g, float b,
    float strokeWidth, float trailLength, float dimOpacity,
    float screenWidth, float screenHeight)
{
    if (!isDrawing_ || !directRenderer_) return;
    directRenderer_->DrawGlowingBorderTransition(
        fromX, fromY, fromW, fromH, toX, toY, toW, toH,
        headProgress, tailProgress, animationPhase, r, g, b,
        strokeWidth, trailLength, dimOpacity, screenWidth, screenHeight);
}

void D3D12RenderTarget::DrawRippleEffect(
    float x, float y, float w, float h,
    float rippleProgress, float r, float g, float b,
    float strokeWidth, float dimOpacity,
    float screenWidth, float screenHeight)
{
    if (!isDrawing_ || !directRenderer_) return;
    directRenderer_->DrawRippleEffect(x, y, w, h, rippleProgress, r, g, b,
        strokeWidth, dimOpacity, screenWidth, screenHeight);
}

void D3D12RenderTarget::DrawLiquidGlass(
    float x, float y, float w, float h,
    float cornerRadius, float blurRadius,
    float refractionAmount, float chromaticAberration,
    float tintR, float tintG, float tintB, float tintOpacity,
    float lightX, float lightY, float highlightBoost,
    int shapeType, float shapeExponent,
    int neighborCount, float fusionRadius, const float* neighborData)
{
    if (!isDrawing_ || !directRenderer_) return;
    directRenderer_->FlushGraphicsForCompute();
    if (neighborCount > 0 && preGlassSnapshotCaptured_) {
        // Reuse existing pre-glass snapshot for fused panels
    } else {
        if (!directRenderer_->CaptureSnapshot()) return;
        if (neighborCount > 0) preGlassSnapshotCaptured_ = true;
    }
    directRenderer_->DrawLiquidGlass(x, y, w, h, cornerRadius, blurRadius,
        refractionAmount, chromaticAberration,
        tintR, tintG, tintB, tintOpacity,
        lightX, lightY, highlightBoost,
        shapeType, shapeExponent,
        neighborCount, fusionRadius, neighborData);
}

void D3D12RenderTarget::CaptureDesktopArea(int32_t screenX, int32_t screenY, int32_t width, int32_t height) {
    if (directRenderer_) directRenderer_->CaptureDesktopArea(screenX, screenY, width, height);
}

void D3D12RenderTarget::DrawDesktopBackdrop(
    float x, float y, float w, float h,
    float blurRadius, float tintR, float tintG, float tintB, float tintOpacity,
    float /*noiseIntensity*/, float /*saturation*/)
{
    if (!isDrawing_ || !directRenderer_) return;
    directRenderer_->DrawDesktopBackdrop(x, y, w, h, blurRadius, tintR, tintG, tintB, tintOpacity);
}

// ============================================================================
// Transition Capture
// ============================================================================

void D3D12RenderTarget::BeginTransitionCapture(int slot, float x, float y, float w, float h) {
    if (!isDrawing_ || !directRenderer_ || slot < 0 || slot > 1) return;
    directRenderer_->BeginOffscreenCapture(slot, x, y, w, h);
}

void D3D12RenderTarget::EndTransitionCapture(int slot) {
    if (!isDrawing_ || !directRenderer_ || slot < 0 || slot > 1) return;
    directRenderer_->EndOffscreenCapture(slot);
}

void D3D12RenderTarget::DrawTransitionShader(float x, float y, float w, float h, float progress, int mode) {
    // TODO: implement transition shader effect in DirectRenderer
    (void)x; (void)y; (void)w; (void)h; (void)progress; (void)mode;
}

void D3D12RenderTarget::DrawCapturedTransition(int slot, float x, float y, float w, float h, float opacity) {
    if (!isDrawing_ || !directRenderer_ || slot < 0 || slot > 1) return;
    directRenderer_->DrawOffscreenBitmap(slot, x, y, w, h, opacity);
}

// ============================================================================
// Effect Capture
// ============================================================================

void D3D12RenderTarget::BeginEffectCapture(float x, float y, float w, float h) {
    if (!isDrawing_ || !directRenderer_) { lastEffectCaptureOk_ = false; return; }
    directRenderer_->FlushGraphicsForCompute();
    lastEffectCaptureOk_ = directRenderer_->BeginOffscreenCapture(0, x, y, w, h);
    if (!lastEffectCaptureOk_) {
        char buf[256];
        sprintf_s(buf, "[BeginEffectCapture] FAILED: x=%.1f y=%.1f w=%.1f h=%.1f isDrawing=%d inOffscreen=%d\n",
            x, y, w, h, (int)isDrawing_, (int)directRenderer_->IsInOffscreenCapture());
        OutputDebugStringA(buf);
    }
}

void D3D12RenderTarget::EndEffectCapture() {
    if (!isDrawing_ || !directRenderer_) return;
    if (lastEffectCaptureOk_) {
        directRenderer_->EndOffscreenCapture(0);
    }
}

void D3D12RenderTarget::DrawBlurEffect(float x, float y, float w, float h, float radius,
    float uvOffsetX, float uvOffsetY)
{
    if (!isDrawing_ || !directRenderer_) return;
    if (!lastEffectCaptureOk_) return;

    // (x,y,w,h) = element's actual screen bounds (stable position).
    // (uvOffsetX, uvOffsetY) = element position within the offscreen texture.
    if (radius > 0) {
        (void)directRenderer_->BlurOffscreenSlot(0, radius);
    }

    directRenderer_->DrawOffscreenBitmapCropped(0, x, y, w, h,
        uvOffsetX, uvOffsetY, 1.0f);
}

void D3D12RenderTarget::DrawDropShadowEffect(float x, float y, float w, float h,
    float blurRadius, float offsetX, float offsetY,
    float r, float g, float b, float a,
    float uvOffsetX, float uvOffsetY,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    if (!isDrawing_ || !directRenderer_) return;
    if (!lastEffectCaptureOk_) return;

    // (x,y,w,h) = element's actual screen bounds.
    // Shadow area = element bounds shifted by offset, expanded by blurRadius.
    float shadowCX = x + offsetX - blurRadius;
    float shadowCY = y + offsetY - blurRadius;
    float shadowCW = w + 2 * blurRadius;
    float shadowCH = h + 2 * blurRadius;

    // Render the shadow into offscreen slot 1 so we can blur it without
    // corrupting the main render target's existing content.
    if (directRenderer_->BeginOffscreenCapture(1, shadowCX, shadowCY, shadowCW, shadowCH)) {
        // Draw shadow fill rect into slot 1 (position is in screen DIP coords;
        // the offscreen transform shifts it to offscreen-local automatically).
        SdfRectInstance inst = {};
        inst.posX = x + offsetX; inst.posY = y + offsetY;
        inst.sizeX = w; inst.sizeY = h;
        inst.cornerTL = cornerTL; inst.cornerTR = cornerTR;
        inst.cornerBR = cornerBR; inst.cornerBL = cornerBL;
        inst.fillR = r * a; inst.fillG = g * a; inst.fillB = b * a; inst.fillA = a;
        inst.opacity = directRenderer_->GetOpacity();
        directRenderer_->AddSdfRect(inst);

        directRenderer_->EndOffscreenCapture(1);

        // Blur the shadow in the offscreen texture
        if (blurRadius > 0) {
            directRenderer_->BlurOffscreenSlot(1, blurRadius);
        }

        // Composite the blurred shadow onto the main RT
        directRenderer_->DrawOffscreenBitmap(1, shadowCX, shadowCY, shadowCW, shadowCH, 1.0f);
    }

    // Composite original element content on top of shadow
    directRenderer_->DrawOffscreenBitmapCropped(0, x, y, w, h,
        uvOffsetX, uvOffsetY, 1.0f);
}

void D3D12RenderTarget::DrawColorMatrixEffect(float x, float y, float w, float h, const float* /*matrix*/) {
    if (!isDrawing_ || !directRenderer_) return;
    // Fallback: just draw the captured content without transformation
    directRenderer_->DrawOffscreenBitmap(0, x, y, w, h, 1.0f);
}

void D3D12RenderTarget::DrawEmbossEffect(float x, float y, float w, float h,
    float /*amount*/, float /*lightDirX*/, float /*lightDirY*/, float /*relief*/)
{
    if (!isDrawing_ || !directRenderer_) return;
    directRenderer_->DrawOffscreenBitmap(0, x, y, w, h, 1.0f);
}

void D3D12RenderTarget::DrawShaderEffect(float x, float y, float w, float h,
    const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
    const float* constants, uint32_t constantFloatCount)
{
    if (!isDrawing_ || !directRenderer_ || !lastEffectCaptureOk_) return;

    directRenderer_->DrawCustomShaderEffect(
        0,
        x, y, w, h,
        shaderBytecode, shaderBytecodeSize,
        constants, constantFloatCount);
}

// ============================================================================
// WebView Visual (DirectComposition)
// ============================================================================

JaliumResult D3D12RenderTarget::CreateWebViewVisual(void** visualOut) {
    if (!isComposition_ || !dcompDevice_ || !visualOut) return JALIUM_ERROR_INVALID_STATE;
    *visualOut = nullptr;

    ComPtr<IDCompositionVisual> containerVisual;
    HRESULT hr = dcompDevice_->CreateVisual(&containerVisual);
    if (FAILED(hr)) return JALIUM_ERROR_INVALID_STATE;

    ComPtr<IDCompositionVisual> targetVisual;
    hr = dcompDevice_->CreateVisual(&targetVisual);
    if (FAILED(hr)) return JALIUM_ERROR_INVALID_STATE;

    hr = containerVisual->AddVisual(targetVisual.Get(), FALSE, nullptr);
    if (FAILED(hr)) return JALIUM_ERROR_INVALID_STATE;

    hr = dcompVisual_->AddVisual(containerVisual.Get(), TRUE, dcompSwapChainVisual_.Get());
    if (FAILED(hr)) return JALIUM_ERROR_INVALID_STATE;

    hr = dcompDevice_->Commit();
    if (FAILED(hr)) return JALIUM_ERROR_INVALID_STATE;

    WebViewVisualEntry entry;
    entry.containerVisual = containerVisual;
    entry.targetVisual = targetVisual;
    IDCompositionVisual* rawTarget = targetVisual.Get();
    webViewVisuals_[containerVisual.Get()] = std::move(entry);
    *visualOut = rawTarget;
    return JALIUM_OK;
}

JaliumResult D3D12RenderTarget::DestroyWebViewVisual(void* visual) {
    if (!isComposition_ || !dcompDevice_ || !visual) return JALIUM_ERROR_INVALID_STATE;
    auto* targetVis = static_cast<IDCompositionVisual*>(visual);
    for (auto it = webViewVisuals_.begin(); it != webViewVisuals_.end(); ++it) {
        if (it->second.targetVisual.Get() == targetVis) {
            dcompVisual_->RemoveVisual(it->second.containerVisual.Get());
            webViewVisuals_.erase(it);
            dcompDevice_->Commit();
            return JALIUM_OK;
        }
    }
    return JALIUM_ERROR_INVALID_STATE;
}

JaliumResult D3D12RenderTarget::SetWebViewVisualPlacement(
    void* visual, int32_t x, int32_t y, int32_t width, int32_t height,
    int32_t contentOffsetX, int32_t contentOffsetY)
{
    if (!isComposition_ || !dcompDevice_ || !visual) return JALIUM_ERROR_INVALID_STATE;
    auto* targetVis = static_cast<IDCompositionVisual*>(visual);
    IDCompositionVisual* containerVisual = nullptr;

    for (auto& [key, entry] : webViewVisuals_) {
        if (entry.targetVisual.Get() == targetVis) {
            containerVisual = entry.containerVisual.Get();
            break;
        }
    }
    if (!containerVisual) return JALIUM_ERROR_INVALID_STATE;

    containerVisual->SetOffsetX(static_cast<float>(x));
    containerVisual->SetOffsetY(static_cast<float>(y));
    const D2D_RECT_F clipRect = { 0.0f, 0.0f, static_cast<float>(width), static_cast<float>(height) };
    containerVisual->SetClip(clipRect);
    targetVis->SetOffsetX(static_cast<float>(contentOffsetX));
    targetVis->SetOffsetY(static_cast<float>(contentOffsetY));
    dcompDevice_->Commit();
    return JALIUM_OK;
}

} // namespace jalium
