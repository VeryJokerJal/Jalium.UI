#include "d3d12_render_target.h"
#include "d3d12_resources.h"
#include "d3d12_retained_layer.h"
#include "d3d12_triangulate.h"
#include "d3d12_vello.h"
#include "jalium_text_stats.h"
#include <d3dcompiler.h>   // D3DCompile — runtime HLSL→DXBC for DrawShaderEffectFromSource
#pragma comment(lib, "d3dcompiler.lib")
#include <cstring>
#include <cmath>
#include <algorithm>
#include <cstdio>
#include <cstdlib>   // _wdupenv_s / _wtoi / free —— JALIUM_SWAPCHAIN_BUFFERS 解析
#include <limits>

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
    // Default engine: Auto → Vello on D3D12 (highest performance)
    activeEngine_ = ResolveRenderingEngine(JALIUM_ENGINE_AUTO, JALIUM_BACKEND_D3D12);
    pendingEngine_ = activeEngine_;
}

D3D12RenderTarget::~D3D12RenderTarget() {
    // Abort on the real list state too: a frame whose list leaked open (e.g. an
    // EndDraw that unwound via SEH) must still be closed at teardown.
    if (directRenderer_ && (isDrawing_ || directRenderer_->IsCommandListRecording())) {
        directRenderer_->AbortFrame();
        isDrawing_ = false;
    }
    WaitForAllFrames();
    directRenderer_.reset();
    if (fenceEvent_) { CloseHandle(fenceEvent_); fenceEvent_ = nullptr; }
    if (frameLatencyWaitable_) { CloseHandle(frameLatencyWaitable_); frameLatencyWaitable_ = nullptr; }
}

// ============================================================================
// Initialization
// ============================================================================

bool D3D12RenderTarget::Initialize() {
    if (!CreateSwapChain()) return false;

    // Create DirectRenderer
    directRenderer_ = std::make_unique<D3D12DirectRenderer>(backend_);
    // swapBufferCount_ 已在上面的 CreateSwapChain() 内解析定值。
    if (!directRenderer_->Initialize(swapChain_.Get(), swapBufferCount_))
        return false;

    float dpiScale = dpiX_ / 96.0f;
    directRenderer_->SetDpiScale(dpiScale > 0 ? dpiScale : 1.0f);

    // Only one path engine runs — disable the other
    directRenderer_->SetVelloEnabled(!IsImpellerActive());

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
// Rendering Engine Hot-Switch
// ============================================================================

JaliumResult D3D12RenderTarget::SetRenderingEngine(JaliumRenderingEngine engine) {
    // Resolve Auto to concrete engine for D3D12
    JaliumRenderingEngine resolved = ResolveRenderingEngine(engine, JALIUM_BACKEND_D3D12);
    pendingEngine_ = resolved;
    // If not currently drawing, apply immediately (e.g. during creation)
    if (!isDrawing_) {
        activeEngine_ = resolved;
    }
    // Only one path engine runs at a time
    if (directRenderer_) {
        directRenderer_->SetVelloEnabled(resolved == JALIUM_ENGINE_VELLO);
    }
    return JALIUM_OK;
}

// ============================================================================
// GPU Diagnostics (Perf tab)
// ============================================================================

JaliumResult D3D12RenderTarget::GetPresentInfo(JaliumPresentInfo* out) const {
    if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
    *out = JaliumPresentInfo{};
    if (!swapChain_) return JALIUM_ERROR_NOT_SUPPORTED;

    // SwapEffect: 当前框架强制 FLIP_SEQUENTIAL（3）；这里**实际查询** swap chain 而非
    // 用编译期常量 —— 让宿主能感知未来路径变更 / 驱动 fallback。
    DXGI_SWAP_CHAIN_DESC1 desc{};
    if (SUCCEEDED(swapChain_->GetDesc1(&desc))) {
        out->swapEffect  = static_cast<int32_t>(desc.SwapEffect);
        out->bufferCount = static_cast<int32_t>(desc.BufferCount);
        out->tearingEnabled  = (desc.Flags & DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING) ? 1 : 0;
        out->waitableEnabled = (desc.Flags & DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT) ? 1 : 0;
    } else {
        // 兜底：用 CreateSwapChain 时记的 flags（已 fallback-stripped）和 swapBufferCount_。
        out->swapEffect  = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
        out->bufferCount = static_cast<int32_t>(swapBufferCount_);
        out->tearingEnabled  = (swapChainCreationFlags_ & DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING) ? 1 : 0;
        out->waitableEnabled = (swapChainCreationFlags_ & DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT) ? 1 : 0;
    }

    // Max frame latency 只有 IDXGISwapChain2 才能读回；waitable 路径下我们 SetMaximumFrameLatency(1)，
    // 这里再 GetMaximumFrameLatency 确认一次驱动是否真的接受了。
    out->maxFrameLatency = 0;
    if (out->waitableEnabled) {
        ComPtr<IDXGISwapChain2> swapChain2;
        if (SUCCEEDED(const_cast<IDXGISwapChain3*>(swapChain_.Get())->QueryInterface(IID_PPV_ARGS(&swapChain2)))
            && swapChain2) {
            UINT latency = 0;
            if (SUCCEEDED(swapChain2->GetMaximumFrameLatency(&latency))) {
                out->maxFrameLatency = static_cast<int32_t>(latency);
            }
        }
    }

    out->composition = isComposition_ ? 1 : 0;
    return JALIUM_OK;
}

JaliumResult D3D12RenderTarget::QueryGpuStats(JaliumGpuStats* out) const {
    if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
    *out = JaliumGpuStats{};

    // Glyph atlas is the only persistent GPU cache here — pull slot usage + bytes.
    if (directRenderer_) {
        if (auto* atlas = directRenderer_->GetGlyphAtlas()) {
            out->glyphSlotsUsed = atlas->GetCacheEntryCount();
            out->glyphSlotsTotal = atlas->GetEstimatedCapacity();
            out->glyphBytes = atlas->GetPackedBytes();
        }
        // Bitmap textures are per-frame but the count is indicative of load.
        out->textureCount = directRenderer_->GetBitmapBatchTextureCount();
        out->textureBytes = directRenderer_->GetBitmapBatchTextureBytes();
        // Add the glyph atlas texture itself to the texture pool.
        if (auto* atlas = directRenderer_->GetGlyphAtlas()) {
            out->textureCount += 1;
            out->textureBytes += atlas->GetTotalBytes();
        }
    }

    // Path-cache-ish metric: current in-flight Impeller batches (tessellated paths).
    // Vello uses compute, no per-frame CPU-side count beyond what's encoded.
    if (impellerEngine_) {
        out->pathEntries = static_cast<int32_t>(impellerEngine_->GetEncodedPathCount());
    }

    // Frame-pacing diagnostics — answer "why is BeginDraw slow?"
    // swapBufferCount_ is fixed at swap-chain creation (CreateSwapChain) and
    // bounds the number of frames the CPU can queue ahead of the GPU; the
    // wait/work numbers below explain how close we are to that ceiling.
    out->swapBufferCount = static_cast<int32_t>(swapBufferCount_);
    if (directRenderer_) {
        out->frameGpuWaitNs            = static_cast<int64_t>(directRenderer_->GetLastFrameGpuWaitNs());
        out->lastFramePresentToReadyNs = static_cast<int64_t>(directRenderer_->GetLastFramePresentToReadyNs());
        out->presentBlockNs            = static_cast<int64_t>(directRenderer_->GetLastFramePresentBlockNs());
    }
    out->frameWaitableWaitNs = static_cast<int64_t>(lastFrameWaitableWaitNs_);

    return JALIUM_OK;
}

JaliumResult D3D12RenderTarget::QueryGpuTiming(JaliumGpuTimingStats* out) const
{
    if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
    *out = JaliumGpuTimingStats{};
    if (!directRenderer_) return JALIUM_ERROR_NOT_SUPPORTED;

    auto snap = directRenderer_->GetGpuTimingSnapshot();
    if (!snap.valid) {
        // First frame after init, or backend can't initialise the query heap.
        // Leave the struct zeroed and return success — the caller treats this
        // as "no breakdown yet" rather than a hard error.
        out->timingValid = 0;
        return JALIUM_OK;
    }

    using Cat = D3D12DirectRenderer::GpuTimingCategory;
    out->totalGpuNs     = static_cast<int64_t>(snap.totalNs);
    out->sdfRectNs      = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::SdfRect)]);
    out->textNs         = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::Text)]);
    out->bitmapNs       = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::Bitmap)]);
    out->pathNs         = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::Path)]);
    out->backdropNs     = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::Backdrop)]);
    out->liquidGlassNs  = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::LiquidGlass)]);
    out->otherNs        = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::Other)]);
    out->batchCount     = static_cast<int32_t>(snap.batchCount);
    out->timingValid    = 1;
    return JALIUM_OK;
}

JaliumResult D3D12RenderTarget::ReclaimIdleResources() {
    // Glyph atlas: the only persistent GPU cache the D3D12 backend keeps
    // across frames. Mid-frame Reset() would shift the UV coordinates of
    // every glyph already emitted earlier in the current frame
    // (see project_d3d12_glyph_atlas_no_midframe_reset memory entry), so we
    // cannot just call atlas->Reset() here. Instead we set the atlas's
    // pending-reset flag — D3D12DirectRenderer::BeginFrame already invokes
    // ApplyPendingGrowthOrReset() once the previous frame's fence has fired,
    // which honors the flag and recreates the atlas exactly once on the
    // safe boundary. Lazily rebuilt as text re-renders.
    //
    // Other D3D12 caches (bitmap-batch textures, instance buffers, blur
    // temps, snapshot RTs) either reset every frame (cleared in BeginFrame
    // around line 792) or are ComPtr-managed scratch resources that don't
    // benefit from explicit eviction. The custom-shader PSO cache is
    // negligibly sized for typical apps and not worth touching here.
    if (directRenderer_) {
        if (auto* atlas = directRenderer_->GetGlyphAtlas()) {
            atlas->RequestResetAtFrameBoundary();
        }

        // Custom-shader PSO cache: tiny by entry count but each entry holds
        // an ID3D12PipelineState (driver-side pipeline + bytecode). ComPtrs
        // auto-release on clear(); any in-flight draw using these PSOs
        // keeps its own implicit ref through the open command list until
        // the next fence completes — frame-safe.
        directRenderer_->ClearCustomShaderCache();
    }
    return JALIUM_OK;
}

bool D3D12RenderTarget::EnsureImpellerEngine() {
    if (impellerEngine_) return true;
    if (!backend_ || !backend_->GetDevice()) return false;

    DXGI_FORMAT fmt = directRenderer_ ? directRenderer_->GetSwapChainFormat() : DXGI_FORMAT_R8G8B8A8_UNORM;
    impellerEngine_ = std::make_unique<ImpellerD3D12Engine>(backend_->GetDevice(), fmt);
    return impellerEngine_->Initialize();
}

void D3D12RenderTarget::SyncScissorToImpeller() {
    if (!impellerEngine_) return;
    if (directRenderer_ && directRenderer_->HasScissor()) {
        auto s = directRenderer_->GetCurrentScissor();
        impellerEngine_->SetScissorRect(
            (float)s.left, (float)s.top, (float)s.right, (float)s.bottom);
    } else {
        impellerEngine_->ClearScissorRect();
    }

    // Mirror the rounded clip too, so each Impeller batch snapshots the clip it
    // was recorded under.  This is what lets us drop the flush-at-clip-boundary
    // barriers: batches at different rounded-clip states simply refuse to merge
    // (per-batch payload differs) instead of forcing a GPU flush, and on replay
    // FlushImpellerBatches feeds each batch's snapshot back as a forced override.
    float rcRect[4], rcRadii[4];
    if (directRenderer_ && directRenderer_->ResolveCurrentRoundedClip(rcRect, rcRadii)) {
        impellerEngine_->SetRoundedClip(rcRect, rcRadii);
    } else {
        impellerEngine_->ClearRoundedClip();
    }
}

// ============================================================================
// Swap Chain Creation
// ============================================================================

bool D3D12RenderTarget::CreateSwapChain() {
    auto factory = backend_->GetDXGIFactory();
    auto commandQueue = backend_->GetCommandQueue();
    if (!factory || !commandQueue) return false;

    // 解析后台缓冲数：默认 kDefaultSwapBufferCount(2)，JALIUM_SWAPCHAIN_BUFFERS
    // 可覆盖并钳到 [2, FrameCount]。只在创建期定一次，后续 Resize 沿用。
    bool bufferCountFromEnv = false;
    {
        swapBufferCount_ = kDefaultSwapBufferCount;
        wchar_t* val = nullptr; size_t len = 0;
        if (_wdupenv_s(&val, &len, L"JALIUM_SWAPCHAIN_BUFFERS") == 0 && val && *val) {
            uint32_t n = (uint32_t)_wtoi(val);
            if (n < 2) n = 2;
            if (n > FrameCount) n = FrameCount;
            swapBufferCount_ = n;
            bufferCountFromEnv = true;
        }
        if (val) free(val);
    }

    // 探测核显仅用于诊断/标签；**不再**据此加深流水线。实测 4 配置 A/B：在这块 UMA 核显
    // 上 present→ready(DWM flip 释放)≈110ms 是固有成本，改 MaxFrameLatency / buffer 数 /
    // vsync / waitable-vs-fence 都不改变它，只把等待换地方。之前给核显 MFL=2+3buffer 只是
    // 多攒一帧输入延迟、毫无收益，故撤回：核显与独显一样用最低延迟 MFL=1 + 2 buffer。
    // 需要更深队列可用 JALIUM_SWAPCHAIN_BUFFERS 显式覆盖。
    {
        JaliumAdapterInfo ai = {};
        isIntegratedAdapter_ = backend_ && backend_->GetAdapterInfo(&ai) == JALIUM_OK
                             && ai.adapterType == JALIUM_GPU_ADAPTER_TYPE_INTEGRATED;
        // 默认 1（最低排队深度）。【撤回记录 2026-06-10】曾改默认=bufferCount(3)
        // 当"令牌桶深度"：合成探针实验 hover 158→95ms，但真实应用 idle 攒满
        // credit 后突发连发多帧，FLIP_SEQUENTIAL 按序展示在慢合成(9fps)下让
        // 交互帧排队尾 +300ms——比单名额更糟。深桶只该在确证 DWM 走 discard
        // 式消费的场景由 JALIUM_MAX_FRAME_LATENCY 显式开启（钳到
        // [1, swapBufferCount_]）。
        maxFrameLatency_ = 1u;
        {
            wchar_t* val = nullptr; size_t len = 0;
            if (_wdupenv_s(&val, &len, L"JALIUM_MAX_FRAME_LATENCY") == 0 && val && *val) {
                uint32_t n = (uint32_t)_wtoi(val);
                if (n < 1) n = 1;
                if (n > swapBufferCount_) n = swapBufferCount_;
                maxFrameLatency_ = n;
            }
            if (val) free(val);
        }
        (void)bufferCountFromEnv;
    }

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
    desc.BufferCount = swapBufferCount_;
    // FLIP_SEQUENTIAL is REQUIRED — Jalium's retained-mode renderer uses
    // partial dirty-rect Present, which depends on the back buffer keeping
    // its previous-frame contents outside the dirty region. FLIP_DISCARD
    // makes those regions undefined and produces a white / garbage window.
    // The latency win we want here comes from FRAME_LATENCY_WAITABLE_OBJECT
    // + SetMaximumFrameLatency(1) below, which is fully compatible with
    // FLIP_SEQUENTIAL on both HWND and Composition paths.
    // D3D12 forbids the legacy BITBLT model entirely: CreateSwapChainForHwnd with
    // DXGI_SWAP_EFFECT_SEQUENTIAL/DISCARD returns INVALID_CALL on a D3D12 command
    // queue (verified experimentally 2026-05-30 — the swap chain failed to create
    // and the RT looped). So flip model is the ONLY option; the zero-latency
    // software path works only because it uses GDI, not D3D12.
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;

    ComPtr<IDXGISwapChain1> swapChain1;
    HRESULT hr;

    if (isComposition_) {
        desc.Scaling = DXGI_SCALING_STRETCH;
        desc.AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED;
        // DComp path also benefits from the frame-latency waitable: it lets
        // CPU sync with the DComp present queue instead of busy-polling fences.
        desc.Flags = DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;

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
        // FRAME_LATENCY_WAITABLE_OBJECT lets the CPU block in lockstep with
        // swap-chain buffer availability so CPU work happens right before
        // display, not 2-3 frames ahead. Combined with ALLOW_TEARING (which
        // is legal on FLIP_SEQUENTIAL too — the model has been supported
        // since Windows 10 1809), this is the lowest-latency configuration
        // compatible with our partial dirty-rect Present scheme.
        desc.Flags = DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT
                   | (tearingSupported_ ? DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING : 0);

        hr = factory->CreateSwapChainForHwnd(commandQueue, hwnd_, &desc, nullptr, nullptr, &swapChain1);
        if (FAILED(hr) && (desc.Flags & DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING)) {
            // Some adapters reject ALLOW_TEARING — strip and retry but keep
            // the waitable, which is the more important latency fix.
            desc.Flags &= ~DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING;
            hr = factory->CreateSwapChainForHwnd(commandQueue, hwnd_, &desc, nullptr, nullptr, &swapChain1);
        }
        if (FAILED(hr) && (desc.Flags & DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT)) {
            // Older runtimes may also reject the waitable flag; final fall-
            // back is a plain flip-discard swap chain with no extra flags.
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
    if (FAILED(hr)) return false;

    // If the swap chain was created with the waitable flag, take ownership
    // of the latency-control HANDLE and cap maximum frame latency to 1.
    // SetMaximumFrameLatency(1) overrides DXGI's default of 3, which is the
    // single biggest contributor to back-pressure on iGPU: the runtime
    // queues up to 3 GPU frames ahead, the OS won't release the buffer
    // until the queue drains. The HANDLE remains owned by us — close it in
    // the destructor (the swap chain itself only signals it).
    if (swapChainCreationFlags_ & DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT) {
        ComPtr<IDXGISwapChain2> swapChain2;
        if (SUCCEEDED(swapChain_.As(&swapChain2)) && swapChain2) {
            // Cap frame latency first so the waitable's initial signal
            // reflects the new cap rather than the DXGI default of 3.
            // 核显独显一律 1（最低延迟）——核显放宽到 2 的旧方案已实测撤回，
            // 见 CreateSwapChain 顶部说明。
            swapChain2->SetMaximumFrameLatency(maxFrameLatency_);
            HANDLE waitable = swapChain2->GetFrameLatencyWaitableObject();
            if (waitable) {
                // Replace any previous handle (Resize path) to avoid leaks.
                if (frameLatencyWaitable_) CloseHandle(frameLatencyWaitable_);
                frameLatencyWaitable_ = waitable;
            }
        }
    }

    return true;
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

    // The command list's REAL open state (cmdListRecording_), not isDrawing_/
    // inFrame_, decides resize safety: BeginFrame opens the list before isDrawing_/
    // inFrame_ are set, and EndFrame clears them before Close, so a resize can land
    // while the list is open yet those flags read false — the #921
    // OBJECT_DELETED_WHILE_STILL_IN_USE root cause (freeing a back buffer the still-
    // open list references).
    const bool listOpen = directRenderer_ && directRenderer_->IsCommandListRecording();
    if (listOpen && directRenderer_->CommandListOwnerThread() != GetCurrentThreadId()) {
        // Cross-thread: the render thread owns an open list. Closing it or freeing
        // the resources it references from this (UI) thread is a data race. Refuse;
        // the managed layer re-stashes and retries after the render thread is parked
        // / between frames. (RequestRenderThreadIdle normally drains the render
        // thread before Resize, so this is a belt-and-suspenders backstop.)
        char buf[256];
        sprintf_s(buf, "[JALIUM-921] Resize deferred (BUSY): command list open on another thread curTid=%lu listOwnerTid=%lu\n",
            GetCurrentThreadId(), directRenderer_->CommandListOwnerThread());
        OutputDebugStringA(buf);
        return JALIUM_ERROR_BUSY;
    }

    // Same thread (or no open list): abort the in-flight / leaked frame so the open
    // list is Closed BEFORE we free the back buffers it references. Keyed on
    // listOpen OR isDrawing_ so a list left open with isDrawing_=0 (the BeginFrame
    // open-gap, or a prior EndDraw whose SEH unwound past the native isDrawing_
    // reset) is still aborted.
    if (listOpen || isDrawing_) {
        if (directRenderer_) directRenderer_->AbortFrame();
        isDrawing_ = false;
    }

    // Single GPU wait via DirectRenderer (it owns all submitted GPU work)
    if (directRenderer_) {
        directRenderer_->ReleaseBackBufferReferences();
    } else {
        WaitForAllFrames();
    }

    HRESULT hr = swapChain_->ResizeBuffers(swapBufferCount_, width, height,
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

    // Re-apply maximum frame latency after ResizeBuffers — the HANDLE itself
    // stays valid across resize (DXGI keeps GetFrameLatencyWaitableObject
    // stable for the lifetime of the swap chain), but the cap occasionally
    // needs reasserting on certain drivers/runtimes. Cheap and defensive.
    if (frameLatencyWaitable_) {
        ComPtr<IDXGISwapChain2> swapChain2;
        if (SUCCEEDED(swapChain_.As(&swapChain2)) && swapChain2) {
            swapChain2->SetMaximumFrameLatency(maxFrameLatency_);
        }
    }

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

    // Stale clip frames from a previous (potentially aborted) frame would
    // poison the rounded-clip pop logic below.  DirectRenderer resets its own
    // scissor / rounded-clip stacks in BeginFrame; mirror that here.
    clipFrameIsRounded_.clear();

    if (backend_) {
        JaliumResult deviceStatus = backend_->CheckDeviceStatus();
        if (deviceStatus != JALIUM_OK) {
            if (debugRender) OutputDebugStringA("[BeginDraw] FAIL: device lost\n");
            return deviceStatus;
        }
    }

    // Block on the swap-chain frame-latency waitable (16 ms timeout). On a
    // healthy swap chain it signals within ~one vsync; the managed Window retry
    // path pumps the dispatcher between attempts so input/animation get a turn.
    // (Earlier this was SKIPPED on iGPU+vsync to "move the wait into Present" —
    // measured to be pointless: the per-frame DWM flip-release cost is fixed and
    // just reappears in the fence wait instead. Reverted to the uniform path.)
    //
    // The auto-reset waitable means we are the SOLE consumer here. A
    // hypothetical background "pacer" thread that also waits on this
    // HANDLE would race us for each signal and the loser would stall until
    // the next vsync timeout — see project memory v5 retrospective.
    //
    // External pacing: when the managed scheduler owns the waitable (it
    // consumes signals via a thread-pool wait callback and only schedules a
    // frame once a present credit arrived), waiting here would deadlock —
    // the signal was already consumed. Skip the wait entirely; the caller
    // IS the sole consumer now. Present below uses sync interval 0 in this
    // mode so a mis-scheduled extra frame degrades to DXGI's own queueing
    // rather than a 16 ms timeout spin.
    if (frameLatencyWaitable_ && !externalPresentPacing_) {
        LARGE_INTEGER waitStart;
        QueryPerformanceCounter(&waitStart);
        DWORD waitResult = WaitForSingleObjectEx(frameLatencyWaitable_, 16, FALSE);
        LARGE_INTEGER waitEnd;
        QueryPerformanceCounter(&waitEnd);
        LARGE_INTEGER freq;
        QueryPerformanceFrequency(&freq);
        if (freq.QuadPart > 0) {
            int64_t delta = waitEnd.QuadPart - waitStart.QuadPart;
            if (delta > 0) {
                int64_t whole = (delta / freq.QuadPart) * 1'000'000'000LL;
                int64_t frac  = ((delta % freq.QuadPart) * 1'000'000'000LL) / freq.QuadPart;
                accumulatingWaitableWaitNs_ += static_cast<uint64_t>(whole + frac);
            }
        }
        if (waitResult == WAIT_TIMEOUT) {
            lastFrameWaitableWaitNs_ = accumulatingWaitableWaitNs_;
            if (debugRender) OutputDebugStringA("[BeginDraw] FAIL: swap-chain waitable timed out\n");
            return JALIUM_ERROR_INVALID_STATE;
        }
        lastFrameWaitableWaitNs_ = accumulatingWaitableWaitNs_;
        accumulatingWaitableWaitNs_ = 0;
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

    // Apply pending engine switch at frame boundary
    if (pendingEngine_ != activeEngine_) {
        activeEngine_ = pendingEngine_;
    }

    isDrawing_ = true;
    preGlassSnapshotCaptured_ = false;

    // Defensive: any deferred Push* recorded across a failed frame would
    // otherwise leak into this one. BeginDraw always starts with a clean
    // pending list, matching the documented contract that callers issue
    // a balanced push/pop sequence within each Begin/EndDraw scope.
    pendingStateOps_.clear();

    // Initialize only the active path engine for this frame
    if (IsImpellerActive()) {
        if (EnsureImpellerEngine()) {
            impellerEngine_->BeginFrame(static_cast<uint32_t>(width_), static_cast<uint32_t>(height_));
        }
    }
    // Vello BeginFrame is handled inside DirectRenderer::BeginFrame
    // (skipped when velloEnabled_==false)

    return JALIUM_OK;
}

JaliumResult D3D12RenderTarget::EndDraw() {
    if (!isDrawing_) return JALIUM_ERROR_INVALID_STATE;
    if (!directRenderer_) { isDrawing_ = false; return JALIUM_ERROR_INVALID_STATE; }

    // Device gate mirroring BeginDraw: a GPU switch (graphics-settings change,
    // driver restart, adapter detach) removes the device MID-FRAME, after
    // BeginDraw's check passed. Flushing/recording below would push commands
    // into a torn-down user-mode driver — NVIDIA's nvwgf2umx AVs (uncatchable
    // in .NET) instead of failing cleanly. Abort the recorded frame and report
    // DEVICE_LOST so the managed recovery chain rebuilds the context. Skipping
    // Present means no waitable signal; managed already returns the consumed
    // present credit on every non-OK EndDraw result, so pacing stays balanced.
    if (backend_) {
        JaliumResult deviceStatus = backend_->CheckDeviceStatus();
        if (deviceStatus != JALIUM_OK) {
            directRenderer_->AbortFrame();
            dirtyRects_.clear();
            isDrawing_ = false;
            return deviceStatus;
        }
    }

    // Flush the active path engine — only one runs at a time
    if (IsImpellerActive()) {
        FlushImpellerBatches();
    } else if (directRenderer_->HasVelloPaths()) {
        directRenderer_->FlushVelloPaths();
    }

    // External pacing forces sync interval 0: the frame-latency waitable is
    // the cadence source (its signal rate IS the compositor's consumption
    // rate), so a vsync-aligned Present would only add a second blocking
    // point. Under a slow compositor (occlusion throttling, remote/virtual
    // displays — measured 460 ms DWM buffer retire on this class of setup)
    // Present(1) stalls the calling thread for the whole retire interval;
    // Present(0) returns immediately and the waitable absorbs the pacing.
    bool effectiveVsync = vsyncEnabled_ && !externalPresentPacing_;
    UINT syncInterval = effectiveVsync ? 1 : 0;
    // ALLOW_TEARING stays strictly opt-in via vsync-off. External pacing must
    // NOT widen it to vsync-ON users: windowed DWM composition ignores the
    // flag, but in direct-scanout scenarios (fullscreen MPO promotion) it
    // produces real tearing they never opted into. A windowed flip-model
    // Present(0) without the flag still never blocks the calling thread under
    // credit scheduling — with MFL=1 the queue is provably empty at submit.
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
    // Under external pacing a transient Present failure must come back as
    // PRESENT_FAILED (never OK): the managed scheduler consumed a credit for
    // this frame and only its unified non-OK EndDraw path returns it.
    JaliumResult endResult = directRenderer_->EndFrame(useDirty, d3dDirtyRects, syncInterval, presentFlags,
                                                       /*reportTransientPresentFailure=*/externalPresentPacing_);

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

bool D3D12RenderTarget::BrushToEngineBrush(Brush* brush, float opacity,
                                           EngineBrushData& bd,
                                           std::vector<EngineBrushData::GradientStop>& stopStore) {
    if (!brush) return false;
    auto type = brush->GetType();
    if (type == JALIUM_BRUSH_SOLID) {
        auto* sb = static_cast<D3D12SolidBrush*>(brush);
        bd.type = 0;
        bd.r = sb->r_; bd.g = sb->g_; bd.b = sb->b_;
        bd.a = sb->a_ * opacity;            // matches the existing inline solid path
        return true;
    }
    if (type == JALIUM_BRUSH_LINEAR_GRADIENT) {
        auto* lb = static_cast<D3D12LinearGradientBrush*>(brush);
        if (lb->stops_.empty()) return false;
        bd.type = 1;
        bd.startX = lb->startX_; bd.startY = lb->startY_;
        bd.endX   = lb->endX_;   bd.endY   = lb->endY_;
        bd.spreadMethod = lb->spreadMethod_;
        stopStore.clear();
        stopStore.reserve(lb->stops_.size());
        for (const auto& s : lb->stops_) {
            // Straight (non-premultiplied) stop colors; opacity folded into the
            // stop alpha — the gradient sampler ignores bd.a and premultiplies by
            // the sampled alpha itself (EncodeGradientFillPath).
            stopStore.push_back({ s.position, s.color.r, s.color.g, s.color.b, s.color.a * opacity });
        }
        bd.stops = stopStore.data();
        bd.stopCount = (uint32_t)stopStore.size();
        // Flat fallback (first stop) for the engine routes with no gradient
        // sampler — strokes (EncodeStrokePathPixelCached) and polygon fills
        // (EncodeFillPolygonScanline) paint a solid of bd.r/g/b/a.
        bd.r = stopStore[0].r; bd.g = stopStore[0].g; bd.b = stopStore[0].b; bd.a = stopStore[0].a;
        return true;
    }
    if (type == JALIUM_BRUSH_RADIAL_GRADIENT) {
        auto* rb = static_cast<D3D12RadialGradientBrush*>(brush);
        if (rb->stops_.empty()) return false;
        bd.type = 2;
        bd.centerX = rb->centerX_; bd.centerY = rb->centerY_;
        bd.radiusX = rb->radiusX_; bd.radiusY = rb->radiusY_;
        bd.originX = rb->originX_; bd.originY = rb->originY_;
        bd.spreadMethod = rb->spreadMethod_;
        stopStore.clear();
        stopStore.reserve(rb->stops_.size());
        for (const auto& s : rb->stops_) {
            stopStore.push_back({ s.position, s.color.r, s.color.g, s.color.b, s.color.a * opacity });
        }
        bd.stops = stopStore.data();
        bd.stopCount = (uint32_t)stopStore.size();
        bd.r = stopStore[0].r; bd.g = stopStore[0].g; bd.b = stopStore[0].b; bd.a = stopStore[0].a;
        return true;
    }
    return false;   // image / unsupported → caller falls back
}

bool D3D12RenderTarget::ExtractStrokeColor(Brush* brush, float& r, float& g, float& b, float& a) {
    if (ExtractBrushColor(brush, r, g, b, a)) return true;   // solid fast path
    if (!brush) return false;
    auto type = brush->GetType();
    if (type == JALIUM_BRUSH_LINEAR_GRADIENT) {
        auto* lb = static_cast<D3D12LinearGradientBrush*>(brush);
        if (lb->stops_.empty()) return false;
        const auto& c = lb->stops_[0].color;   // representative solid (first stop)
        r = c.r; g = c.g; b = c.b; a = c.a;
        return true;
    }
    if (type == JALIUM_BRUSH_RADIAL_GRADIENT) {
        auto* rb = static_cast<D3D12RadialGradientBrush*>(brush);
        if (rb->stops_.empty()) return false;
        const auto& c = rb->stops_[0].color;
        r = c.r; g = c.g; b = c.b; a = c.a;
        return true;
    }
    return false;   // image / unsupported
}

bool D3D12RenderTarget::TryStrokeGradientPath(Brush* brush, float strokeWidth,
                                              float startX, float startY,
                                              const std::vector<float>& cmds, bool closed,
                                              int32_t lineJoin, float miterLimit, int32_t lineCap) {
    if (!IsImpellerActive() || !brush || cmds.empty() || strokeWidth <= 0.0f) return false;
    auto type = brush->GetType();
    if (type != JALIUM_BRUSH_LINEAR_GRADIENT && type != JALIUM_BRUSH_RADIAL_GRADIENT) return false;
    if (!EnsureImpellerEngine()) return false;

    // The engine emit reads transform / scissor / opacity directly off
    // DirectRenderer, so materialize any deferred Push* first (same as
    // StrokePath / FillPolygon).
    CommitDeferredState();

    auto t = directRenderer_->GetCurrentTransform();
    float dpiScale = directRenderer_->GetDpiScale();
    float opacity = directRenderer_->GetOpacity();
    EngineBrushData bd;
    std::vector<EngineBrushData::GradientStop> stopStore;
    if (!BrushToEngineBrush(brush, opacity, bd, stopStore)) return false;

    EngineTransform et;
    et.m11 = t.m11 * dpiScale; et.m12 = t.m12 * dpiScale;
    et.m21 = t.m21 * dpiScale; et.m22 = t.m22 * dpiScale;
    et.dx = t.dx * dpiScale; et.dy = t.dy * dpiScale;

    return impellerEngine_->EncodeStrokePath(startX, startY, cmds.data(), (uint32_t)cmds.size(),
        bd, strokeWidth, closed, lineJoin, miterLimit, lineCap, nullptr, 0, 0.0f, et);
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
    // First commit any deferred Push* — every real draw method calls this
    // hook, so it is the single right place to materialise queued state
    // before the draw observes it. Skipping when there is nothing pending
    // keeps the no-state-change hot path branch-only.
    if (!pendingStateOps_.empty()) {
        CommitDeferredState();
    }

    // When Impeller is active, drain any path geometry the engine has
    // accumulated since the last non-path draw. Encode{Fill,Stroke}Path use
    // PushBatchWithCoverage to coalesce N back-to-back path emits into a
    // single GPU batch — but that coalescing only works as long as we do NOT
    // call FlushImpellerBatches between them. Doing it here (lazily, right
    // before the next non-path draw observes the result) preserves
    // painter-order correctness while letting 87 consecutive StrokePath
    // calls collapse to a single AddTrianglesPreTransformed → 1
    // DrawIndexedInstanced at record time.
    if (IsImpellerActive()) {
        if (impellerEngine_ && impellerEngine_->HasPendingWork()) {
            FlushImpellerBatches();
        }
        return;
    }
    if (!directRenderer_ || !directRenderer_->HasVelloPaths()) return;

    // Vello accumulates all path encodes (FillPath / StrokePath) across the
    // whole frame and produces a single offscreen RT in Dispatch. Compositing
    // that RT once at EndDraw means every Vello path lands ON TOP of every
    // rect / bitmap / text drawn afterwards — opaque cards stop covering the
    // SaaSBackground waves drawn before them, so the cards LOOK translucent
    // even though their Background is solid white. The Impeller pipeline
    // doesn't have this problem because each path encode is immediately
    // turned into triangles and sorted with the rest of the batch list.
    //
    // To restore the painter's-algorithm semantics for Vello as well, drain
    // the accumulated Vello scene WHENEVER a non-Vello draw is about to
    // record. FlushVelloPaths does the Dispatch + AddBitmap (capturing the
    // current drawOrder), then ForceNewOutputTexture + BeginFrame so any
    // subsequent paths in this frame land in a fresh subscene that gets its
    // OWN Dispatch + composite at the next flush boundary or at EndDraw.
    //
    // Mid-frame multi-Dispatch is now safe because:
    //   - EnsureBuffers / EnsureGPUBuffers retire-on-grow (old default-heap
    //     buffer ComPtrs land on pendingRetiredResources_ before reassign).
    //   - Each Dispatch allocates a FRESH shader-visible descriptor heap
    //     (the long-lived gpuSrvHeap_[fi] would race because GPU reads
    //     descriptors at execution time, not at record time).
    //   - Each Dispatch retires its per-frame upload buffers + configUpload
    //     so the next CPU write doesn't race the prior Dispatch's queued
    //     CopyBufferRegion / CBV reads.
    //   - ForceNewOutputTexture / EnsureOutputTexture retire the previous
    //     outputTexture_ onto pendingRetiredResources_ (via RetireOutputTexture)
    //     instead of Reset()/overwrite. The BitmapBatchTexture ComPtr keep-alive
    //     alone is NOT enough: the very next mid-frame FlushGraphicsForCompute
    //     clears bitmapTextures_ before this commandList is Closed, which would
    //     otherwise free the composited 'JaliumVelloOutput' mid-list (#921).
    //   - DrainRetired moves all those ComPtrs onto FrameResources's
    //     retiredInstanceBuffers / retiredDescriptorHeaps, whose lifetime is
    //     gated by this slot's fence.
    directRenderer_->FlushVelloPaths();
}

void D3D12RenderTarget::FlushImpellerBatches() {
    if (!impellerEngine_ || !impellerEngine_->HasPendingWork() || !directRenderer_) return;

    // Convert Impeller batches to DirectRenderer triangles.
    // Impeller vertices are in pixel-space; DirectRenderer shader expects DIP-space.
    float invDpi = 1.0f / directRenderer_->GetDpiScale();

    // Reused across the loop AND across frames so the index→flat-triangle
    // expansion never allocates after the first big-batch frame warmed it.
    // This is the spike-killer for "next non-path draw triggers a flush of
    // N accumulated path batches": the per-batch vector previously did a
    // fresh malloc/free pair, which dominated the per-call latency on the
    // first non-path draw of a path-heavy frame.
    thread_local std::vector<TriangleVertex> s_flushExpand;

    for (auto& batch : impellerEngine_->GetBatches()) {
        if (batch.indices.empty() || batch.vertices.empty()) continue;

        // Apply the batch's SNAPSHOTTED scissor: temporarily push to the
        // DirectRenderer's scissor stack so AddTrianglesPreTransformed
        // (candidate.hasScissor = !scissorStack_.empty(); scissor = top()) captures
        // the clip this batch was RECORDED under — not whatever clip is live now.
        // Both branches push so the snapshot is authoritative (mirrors the rounded
        // clip forced-override below). The lazy flush fires while a LATER, clipped
        // element is mid-draw, so the live stack top is that element's clip; a
        // no-clip batch (batch.hasScissor == false) must NOT inherit it or it gets
        // wrongly clipped away. Concrete failure this fixes: a status-bar stroke
        // icon (no clip of its own) inherited the adjacent label's scissor and
        // vanished whenever the flush was lazy — i.e. every animation frame.
        bool pushedScissor = true;
        if (batch.hasScissor) {
            // Push raw pixel-space scissor rect directly
            // (DirectRenderer stores pixel-space rects in its scissor stack)
            D3D12_RECT sr;
            sr.left = (LONG)batch.scissorL;
            sr.top = (LONG)batch.scissorT;
            sr.right = (LONG)batch.scissorR;
            sr.bottom = (LONG)batch.scissorB;
            directRenderer_->PushScissorRaw(sr);
        } else {
            // No recorded clip → draw unclipped (full viewport), exactly like a
            // no-scissor DIRECT batch (DrawBatches' else-branch uses the full
            // content extent). This overrides the stale live stack top.
            D3D12_RECT full = { 0, 0,
                (LONG)directRenderer_->GetViewportWidth(),
                (LONG)directRenderer_->GetViewportHeight() };
            directRenderer_->PushScissorRaw(full);
        }

        // Feed the batch's snapshotted rounded clip back as a forced override so
        // ResolveRoundedClipForBatch (inside AddTrianglesPreTransformed) masks these
        // triangles with the clip they were RECORDED under — not whatever rounded
        // clip is live now (batches accumulate across clip boundaries instead of
        // flushing, so the live stack has typically moved on).
        if (batch.hasRoundedClip) {
            directRenderer_->SetForcedRoundedClip(batch.roundedClipRect, batch.roundedClipCornerRadii);
        } else {
            directRenderer_->SetForcedRoundedClipNone();
        }

        // Expand indexed vertices to flat triangle list, converting pixel→DIP.
        // resize(N) + indexed write is faster than reserve + N push_backs:
        // push_back has an inline capacity check at each call, resize sets
        // capacity once. For a 32K-vertex batch this is the dominant
        // expand-phase win on top of the cross-frame reuse.
        s_flushExpand.resize(batch.indices.size());
        size_t outIdx = 0;
        const auto& verts = batch.vertices;
        const size_t vN = verts.size();
        for (auto idx : batch.indices) {
            if (idx < vN) {
                const auto& v = verts[idx];
                s_flushExpand[outIdx++] = { v.x * invDpi, v.y * invDpi, v.r, v.g, v.b, v.a };
            }
        }
        if (outIdx > 0) {
            directRenderer_->AddTrianglesPreTransformed(s_flushExpand.data(), (uint32_t)outIdx);
        }

        if (pushedScissor) {
            directRenderer_->PopScissor();
        }
    }
    // Restore normal live-stack rounded-clip resolution for subsequent
    // non-Impeller (direct) draws in this frame.
    directRenderer_->ClearForcedRoundedClip();
    impellerEngine_->ClearBatches();
}

void D3D12RenderTarget::FillRectangle(float x, float y, float w, float h, Brush* brush) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();
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
    // Gradient outline → TRUE per-pixel gradient stroke via the engine (the SDF
    // border below paints a solid color). Solids fall straight through.
    if (IsImpellerActive() &&
        (brush->GetType() == JALIUM_BRUSH_LINEAR_GRADIENT || brush->GetType() == JALIUM_BRUSH_RADIAL_GRADIENT)) {
        std::vector<float> cmds = {
            0.0f, x + w, y,
            0.0f, x + w, y + h,
            0.0f, x,     y + h,
            5.0f
        };
        if (TryStrokeGradientPath(brush, strokeWidth, x, y, cmds, true, 0, 4.0f, 0)) return;
    }
    float r, g, b, a;
    if (ExtractStrokeColor(brush, r, g, b, a)) {
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
    SdfRectInstance inst = {};
    bool brushOk = FillBrushToInstance(brush, inst);
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

    // Gradient outline → TRUE per-pixel gradient stroke via the engine.
    if (IsImpellerActive() &&
        (brush->GetType() == JALIUM_BRUSH_LINEAR_GRADIENT || brush->GetType() == JALIUM_BRUSH_RADIAL_GRADIENT)) {
        const float k = 0.5522847498f;
        float crx = std::min(std::max(rx, 0.0f), w * 0.5f);
        float cry = std::min(std::max(ry, 0.0f), h * 0.5f);
        float kx = crx * k, ky = cry * k;
        std::vector<float> cmds = {
            0.0f, x + w - crx, y,
            1.0f, x + w - crx + kx, y, x + w, y + cry - ky, x + w, y + cry,
            0.0f, x + w, y + h - cry,
            1.0f, x + w, y + h - cry + ky, x + w - crx + kx, y + h, x + w - crx, y + h,
            0.0f, x + crx, y + h,
            1.0f, x + crx - kx, y + h, x, y + h - cry + ky, x, y + h - cry,
            0.0f, x, y + cry,
            1.0f, x, y + cry - ky, x + crx - kx, y, x + crx, y,
            5.0f
        };
        if (TryStrokeGradientPath(brush, strokeWidth, x + crx, y, cmds, true, 0, 4.0f, 0)) return;
    }

    float r, g, b, a;
    if (ExtractStrokeColor(brush, r, g, b, a)) {
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

    // Gradient outline → TRUE per-pixel gradient stroke via the engine.
    if (IsImpellerActive() &&
        (brush->GetType() == JALIUM_BRUSH_LINEAR_GRADIENT || brush->GetType() == JALIUM_BRUSH_RADIAL_GRADIENT)) {
        const float k = 0.5522847498f;
        const float maxR = std::min(w, h) * 0.5f;
        float ctl = std::min(std::max(tl, 0.0f), maxR);
        float ctr = std::min(std::max(tr, 0.0f), maxR);
        float cbr = std::min(std::max(br, 0.0f), maxR);
        float cbl = std::min(std::max(bl, 0.0f), maxR);
        std::vector<float> cmds = {
            0.0f, x + w - ctr, y,
            1.0f, x + w - ctr + ctr * k, y, x + w, y + ctr - ctr * k, x + w, y + ctr,
            0.0f, x + w, y + h - cbr,
            1.0f, x + w, y + h - cbr + cbr * k, x + w - cbr + cbr * k, y + h, x + w - cbr, y + h,
            0.0f, x + cbl, y + h,
            1.0f, x + cbl - cbl * k, y + h, x, y + h - cbl + cbl * k, x, y + h - cbl,
            0.0f, x, y + ctl,
            1.0f, x, y + ctl - ctl * k, x + ctl - ctl * k, y, x + ctl, y,
            5.0f
        };
        if (TryStrokeGradientPath(brush, strokeWidth, x + ctl, y, cmds, true, 0, 4.0f, 0)) return;
    }

    float r, g, b, a;
    if (ExtractStrokeColor(brush, r, g, b, a)) {
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
// Drawing — Ellipses (true ellipse SDF with analytical AA)
//
// Both filled and stroked ellipses go through the SdfRect PSO with
// shapeType=1 (SuperEllipse) and shapeN=2 (real ellipse equation
// |x/a|² + |y/b|² = 1). The pixel shader applies smoothstep(fwidth(dist))
// analytical anti-aliasing, so edges stay smooth at any rx/ry ratio without
// MSAA. This beats Impeller's triangle-strip tessellation (which has no AA
// at all on the per-vertex-color PSO and visibly polygonalises flat ellipses).
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
        inst.shapeType = 1.0f; // SuperEllipse
        inst.shapeN = 2.0f;    // real ellipse
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
        inst.shapeType = 1.0f;
        inst.shapeN = 2.0f;
        directRenderer_->AddSdfRect(inst);
    }
}

void D3D12RenderTarget::DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();
    // Gradient ring → TRUE per-pixel gradient stroke via the engine (4 cubic
    // beziers approximating the ellipse). Solids fall through to the SDF ring.
    if (IsImpellerActive() && rx > 0.0f && ry > 0.0f &&
        (brush->GetType() == JALIUM_BRUSH_LINEAR_GRADIENT || brush->GetType() == JALIUM_BRUSH_RADIAL_GRADIENT)) {
        const float k = 0.5522847498f;
        float kx = rx * k, ky = ry * k;
        std::vector<float> cmds = {
            1.0f, cx + rx, cy + ky, cx + kx, cy + ry, cx,      cy + ry,
            1.0f, cx - kx, cy + ry, cx - rx, cy + ky, cx - rx, cy,
            1.0f, cx - rx, cy - ky, cx - kx, cy - ry, cx,      cy - ry,
            1.0f, cx + kx, cy - ry, cx + rx, cy - ky, cx + rx, cy,
            5.0f
        };
        if (TryStrokeGradientPath(brush, strokeWidth, cx + rx, cy, cmds, true, 1, 4.0f, 0)) return;
    }
    float r, g, b, a;
    if (ExtractStrokeColor(brush, r, g, b, a)) {
        SdfRectInstance inst = {};
        inst.posX = cx - rx; inst.posY = cy - ry;
        inst.sizeX = rx * 2; inst.sizeY = ry * 2;
        inst.cornerTL = rx; inst.cornerTR = rx; inst.cornerBR = rx; inst.cornerBL = rx;
        inst.borderR = r; inst.borderG = g; inst.borderB = b; inst.borderA = a;
        inst.borderWidth = strokeWidth;
        inst.opacity = directRenderer_->GetOpacity();
        inst.shapeType = 1.0f;
        inst.shapeN = 2.0f;
        directRenderer_->AddSdfRect(inst);
    }
}

// ============================================================================
// Drawing — Lines
// ============================================================================

void D3D12RenderTarget::DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth) {
    if (!isDrawing_ || !brush || !directRenderer_) return;
    FlushVelloIfNeeded();
    // Gradient line → TRUE per-pixel gradient stroke via the engine (the 3-strip
    // AA path below is solid-only). Solids fall straight through; if the gradient
    // encode fails, ExtractStrokeColor degrades it to a representative solid.
    if (IsImpellerActive() &&
        (brush->GetType() == JALIUM_BRUSH_LINEAR_GRADIENT || brush->GetType() == JALIUM_BRUSH_RADIAL_GRADIENT)) {
        std::vector<float> cmds = { 0.0f, x2, y2 };
        if (TryStrokeGradientPath(brush, strokeWidth, x1, y1, cmds, false, 0, 4.0f, 0)) return;
    }
    float r, g, b, a;
    if (!ExtractStrokeColor(brush, r, g, b, a)) return;

    float dx = x2 - x1, dy = y2 - y1;
    float len = std::sqrt(dx * dx + dy * dy);
    if (len < 0.001f) return;

    // Unit perpendicular to line direction.
    float invLen = 1.0f / len;
    float nx = -dy * invLen;
    float ny =  dx * invLen;

    float opacity = directRenderer_->GetOpacity();
    float baseA = a * opacity;
    float pr = r * baseA, pg = g * baseA, pb = b * baseA;

    // Manual analytical AA: render the line as three side-by-side strips —
    // a solid core flanked by a 1-pixel alpha-ramp on each side. This is the
    // same trick Direct2D uses for thin strokes and keeps oblique lines from
    // looking like staircases on the per-vertex-color triangle PSO (which
    // has no MSAA and no in-shader AA).
    //
    //   Stroke ≥ 1px: core half-width = (stroke - 1) * 0.5, then 1px feather
    //                  on each side ramping alpha 1 → 0.
    //   Stroke < 1px: no core, the whole stroke becomes the feather and we
    //                  fade alpha by the stroke's pixel coverage so very thin
    //                  lines don't pop.
    constexpr float kFeather = 1.0f;
    float halfStroke = strokeWidth * 0.5f;

    float coreHalf;     // distance from line center to inner edge of feather
    float outerHalf;    // distance from line center to outer (zero-alpha) edge
    float coverage;     // alpha scale for sub-pixel widths

    if (strokeWidth >= kFeather) {
        coreHalf  = halfStroke - kFeather * 0.5f;
        outerHalf = halfStroke + kFeather * 0.5f;
        coverage  = 1.0f;
    } else {
        coreHalf  = 0.0f;
        outerHalf = kFeather * 0.5f;            // 0.5px on each side
        coverage  = strokeWidth / kFeather;     // fade thin lines proportionally
    }

    float caPr = pr * coverage, caPg = pg * coverage, caPb = pb * coverage, caPa = baseA * coverage;

    // Endpoints (positive/negative perpendicular for inner core, outer for feather).
    float p1cx = x1 + nx * coreHalf,  p1cy = y1 + ny * coreHalf;
    float p1cnx = x1 - nx * coreHalf, p1cny = y1 - ny * coreHalf;
    float p2cx = x2 + nx * coreHalf,  p2cy = y2 + ny * coreHalf;
    float p2cnx = x2 - nx * coreHalf, p2cny = y2 - ny * coreHalf;

    float p1ox = x1 + nx * outerHalf,  p1oy = y1 + ny * outerHalf;
    float p1onx = x1 - nx * outerHalf, p1ony = y1 - ny * outerHalf;
    float p2ox = x2 + nx * outerHalf,  p2oy = y2 + ny * outerHalf;
    float p2onx = x2 - nx * outerHalf, p2ony = y2 - ny * outerHalf;

    TriangleVertex verts[18];
    uint32_t vi = 0;

    // Core strip (solid alpha). Skipped for sub-pixel lines (coreHalf == 0).
    if (coreHalf > 0.0f) {
        verts[vi++] = { p1cx,  p1cy,  caPr, caPg, caPb, caPa };
        verts[vi++] = { p1cnx, p1cny, caPr, caPg, caPb, caPa };
        verts[vi++] = { p2cx,  p2cy,  caPr, caPg, caPb, caPa };
        verts[vi++] = { p2cx,  p2cy,  caPr, caPg, caPb, caPa };
        verts[vi++] = { p1cnx, p1cny, caPr, caPg, caPb, caPa };
        verts[vi++] = { p2cnx, p2cny, caPr, caPg, caPb, caPa };
    }

    // Outer feather (alpha-ramped on the outside edge).
    // Positive-perpendicular side: solid edge → zero-alpha outer edge.
    verts[vi++] = { p1cx, p1cy, caPr, caPg, caPb, caPa };
    verts[vi++] = { p1ox, p1oy, 0.0f, 0.0f, 0.0f, 0.0f };
    verts[vi++] = { p2cx, p2cy, caPr, caPg, caPb, caPa };
    verts[vi++] = { p2cx, p2cy, caPr, caPg, caPb, caPa };
    verts[vi++] = { p1ox, p1oy, 0.0f, 0.0f, 0.0f, 0.0f };
    verts[vi++] = { p2ox, p2oy, 0.0f, 0.0f, 0.0f, 0.0f };

    // Negative-perpendicular side.
    verts[vi++] = { p1cnx, p1cny, caPr, caPg, caPb, caPa };
    verts[vi++] = { p2cnx, p2cny, caPr, caPg, caPb, caPa };
    verts[vi++] = { p1onx, p1ony, 0.0f, 0.0f, 0.0f, 0.0f };
    verts[vi++] = { p1onx, p1ony, 0.0f, 0.0f, 0.0f, 0.0f };
    verts[vi++] = { p2cnx, p2cny, caPr, caPg, caPb, caPa };
    verts[vi++] = { p2onx, p2ony, 0.0f, 0.0f, 0.0f, 0.0f };

    directRenderer_->AddTriangles(verts, vi);
}

// ============================================================================
// Drawing — Polygons & Paths (triangulated)
// ============================================================================

// IsConvexPolygon now lives in jalium_impeller_shapes.h (cross-backend).
// d3d12_impeller_engine.h transitively includes it, so it is visible here
// via the namespace-jalium ADL chain that d3d12_render_target.cpp pulls in.

void D3D12RenderTarget::FillPolygon(const float* points, uint32_t pointCount, Brush* brush, int32_t fillRule) {
    if (!isDrawing_ || !brush || !directRenderer_ || pointCount < 3) return;

    // Path emit reads transform / scissor / opacity directly off DirectRenderer
    // instead of going through FlushVelloIfNeeded, so we must materialize any
    // deferred Push* here too.
    CommitDeferredState();

    // ── Thin-fill stencil-then-cover fast path (solid color brushes only).
    //
    // The Impeller / fan-triangulation paths below rasterize through the
    // single-sampled triangle pipeline (trianglePSO is SampleDesc.Count = 1, no
    // MSAA). A thin axis-aligned fill — e.g. the 1px-tall title-bar *minimize*
    // glyph — then loses coverage on sub-pixel rows and renders blank or faint,
    // while the *maximize* glyph (authored as a nested compound path, so it
    // reaches FillPath) renders crisply via the 8x MSAA stencil-then-cover path.
    //
    // Give thin solid fills that same robust MSAA coverage WITHOUT disturbing the
    // well-tuned, coalesced Impeller route for normal-sized polygons: route ONLY
    // near-degenerate thin polygons (device-space minor extent of a few pixels)
    // through stencil-then-cover. Normal shapes are untouched, so there is no
    // coverage/perf regression for them.
    //
    // GUARD: stencil-path batches honor only the rectangular scissor — the cover
    // PS has no rounded-clip SDF (the Impeller triangle path snapshots the
    // rounded clip per batch instead). So skip this fast path whenever a rounded
    // clip is active, otherwise a thin polygon inside a rounded-clipped region
    // would escape the rounding.
    if (!directRenderer_->HasRoundedClip() && brush->GetType() == JALIUM_BRUSH_SOLID) {
        float minX = points[0], minY = points[1], maxX = points[0], maxY = points[1];
        for (uint32_t i = 1; i < pointCount; i++) {
            float x = points[i * 2], y = points[i * 2 + 1];
            if (x < minX) minX = x; else if (x > maxX) maxX = x;
            if (y < minY) minY = y; else if (y > maxY) maxY = y;
        }
        auto t = directRenderer_->GetCurrentTransform();
        float sx = std::sqrt(t.m11 * t.m11 + t.m12 * t.m12);
        float sy = std::sqrt(t.m21 * t.m21 + t.m22 * t.m22);
        float scale = std::max(sx, sy) * directRenderer_->GetDpiScale();
        float devMinExtent = std::min(maxX - minX, maxY - minY) * scale;

        if (devMinExtent < 4.0f) {
            float r, g, b, a;
            if (ExtractBrushColor(brush, r, g, b, a)) {
                // points → path commands: implicit MoveTo(start) + LineTo(rest) +
                // ClosePath. Tags match jalium_triangulate.h (LineTo = 0, Close = 5).
                std::vector<float> cmds;
                cmds.reserve(static_cast<size_t>(pointCount) * 3 + 1);
                for (uint32_t i = 1; i < pointCount; i++) {
                    cmds.push_back(0.0f);                 // LineTo
                    cmds.push_back(points[i * 2]);
                    cmds.push_back(points[i * 2 + 1]);
                }
                cmds.push_back(5.0f);                      // ClosePath
                auto geom = directRenderer_->GetOrBuildStencilPathGeometry(
                    points[0], points[1], cmds.data(), (uint32_t)cmds.size());
                if (directRenderer_->AddStencilPath(geom, r, g, b, a, fillRule)) {
                    return;
                }
            }
        }
    }

    // Impeller engine path
    if (IsImpellerActive() && EnsureImpellerEngine()) {
        auto t = directRenderer_->GetCurrentTransform();
        float dpiScale = directRenderer_->GetDpiScale();
        float opacity = directRenderer_->GetOpacity();

        EngineBrushData bd;
        std::vector<EngineBrushData::GradientStop> stopStore;
        if (BrushToEngineBrush(brush, opacity, bd, stopStore)) {
            EngineTransform et;
            et.m11 = t.m11 * dpiScale; et.m12 = t.m12 * dpiScale;
            et.m21 = t.m21 * dpiScale; et.m22 = t.m22 * dpiScale;
            et.dx = t.dx * dpiScale; et.dy = t.dy * dpiScale;

            FillRule fr = (fillRule == 1) ? FillRule::NonZero : FillRule::EvenOdd;
            if (bd.type == 0) {
                if (impellerEngine_->EncodeFillPolygon(points, pointCount, bd, fr, et)) {
                    // Lazy flush: the next non-path draw (or EndDraw) drains
                    // the engine into DirectRenderer, letting N consecutive
                    // path emits coalesce into one GPU batch.
                    return;
                }
            } else {
                // Gradient: EncodeFillPolygon paints a flat color (no gradient
                // sampler), so synthesize LineTo/ClosePath commands and route
                // through EncodeFillPath, which samples the gradient per-vertex.
                std::vector<float> cmds;
                cmds.reserve(static_cast<size_t>(pointCount) * 3 + 1);
                for (uint32_t i = 1; i < pointCount; i++) {
                    cmds.push_back(0.0f);                 // LineTo
                    cmds.push_back(points[i * 2]);
                    cmds.push_back(points[i * 2 + 1]);
                }
                cmds.push_back(5.0f);                      // ClosePath
                if (impellerEngine_->EncodeFillPath(points[0], points[1], cmds.data(), (uint32_t)cmds.size(), bd, fr, et, -1)) {
                    return;
                }
            }
        }
    }

    // Route non-solid brushes (gradients) through Vello for GPU rendering
    if (!IsImpellerActive() && brush->GetType() != JALIUM_BRUSH_SOLID) {
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

    // Always use full robust triangulation — no convex fan shortcut.
    // Fan triangulation can produce pixel gaps on small shapes (scrollbar arrows,
    // window button icons) when IsConvexPolygon has false positives.
    std::vector<uint32_t> indices;
    if (!TriangulatePolygonRobust(points, pointCount, indices) || indices.size() < 3) {
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
    int32_t lineJoin, float miterLimit) {
    if (!isDrawing_ || !brush || !directRenderer_ || pointCount < 2) return;

    CommitDeferredState();

    // Impeller engine path: convert polygon to LineTo commands and stroke via Impeller
    if (IsImpellerActive() && EnsureImpellerEngine()) {
        auto t = directRenderer_->GetCurrentTransform();
        float dpiScale = directRenderer_->GetDpiScale();
        float opacity = directRenderer_->GetOpacity();

        // Solid or gradient. A linear/radial gradient polyline now renders as a
        // TRUE per-vertex gradient via EncodeStrokePath (see StrokePath); only
        // dashed/analytic gradients degrade to the flat first-stop solid.
        EngineBrushData bd;
        std::vector<EngineBrushData::GradientStop> stopStore;
        if (BrushToEngineBrush(brush, opacity, bd, stopStore)) {
            EngineTransform et;
            et.m11 = t.m11 * dpiScale; et.m12 = t.m12 * dpiScale;
            et.m21 = t.m21 * dpiScale; et.m22 = t.m22 * dpiScale;
            et.dx = t.dx * dpiScale; et.dy = t.dy * dpiScale;

            // Build LineTo command buffer from polygon points (skip first = start point)
            std::vector<float> cmds;
            cmds.reserve(pointCount * 3 + 1);
            for (uint32_t i = 1; i < pointCount; i++) {
                cmds.push_back(0); // LineTo tag
                cmds.push_back(points[i * 2]);
                cmds.push_back(points[i * 2 + 1]);
            }
            if (closed) {
                cmds.push_back(5); // ClosePath tag
            }

            if (impellerEngine_->EncodeStrokePath(
                    points[0], points[1], cmds.data(), (uint32_t)cmds.size(),
                    bd, strokeWidth, closed, lineJoin, miterLimit, 0, nullptr, 0, 0.0f, et)) {
                // Lazy flush: see FillPolygon for the coalescing rationale.
                return;
            }
        }
    }

    FlushVelloIfNeeded();
    float r, g, b, a;
    if (!ExtractBrushColor(brush, r, g, b, a)) return;

    float opacity = directRenderer_->GetOpacity();
    float pr = r * a * opacity, pg = g * a * opacity, pb = b * a * opacity, pa = a * opacity;
    float hw = strokeWidth * 0.5f;

    uint32_t segCount = closed ? pointCount : pointCount - 1;

    // Pre-compute per-segment normals (perpendicular, scaled by half-width)
    struct Vec2 { float x, y; };
    std::vector<Vec2> normals(segCount);
    for (uint32_t i = 0; i < segCount; i++) {
        uint32_t j = (i + 1) % pointCount;
        float dx = points[j * 2] - points[i * 2];
        float dy = points[j * 2 + 1] - points[i * 2 + 1];
        float len = std::sqrt(dx * dx + dy * dy);
        if (len < 0.001f) len = 0.001f;
        normals[i] = { -dy / len * hw, dx / len * hw };
    }

    // Compute miter offset at each vertex (shared by adjacent segments).
    // For vertex i, the miter is the average of normals from the incoming and
    // outgoing segments, scaled so that the perpendicular distance from the
    // stroke center-line equals hw.
    uint32_t jointCount = closed ? pointCount : pointCount;
    struct MiterPt { float lx, ly, rx, ry; }; // left (+normal) and right (-normal) miter offsets
    std::vector<MiterPt> miters(pointCount);

    for (uint32_t i = 0; i < pointCount; i++) {
        float px = points[i * 2], py = points[i * 2 + 1];

        bool isStart = (i == 0 && !closed);
        bool isEnd   = (i == pointCount - 1 && !closed);

        if (isStart) {
            miters[i] = { px + normals[0].x, py + normals[0].y,
                          px - normals[0].x, py - normals[0].y };
        } else if (isEnd) {
            uint32_t lastSeg = segCount - 1;
            miters[i] = { px + normals[lastSeg].x, py + normals[lastSeg].y,
                          px - normals[lastSeg].x, py - normals[lastSeg].y };
        } else {
            // Joint between incoming segment (prevSeg) and outgoing segment (nextSeg)
            uint32_t prevSeg = (i == 0) ? segCount - 1 : i - 1;
            uint32_t nextSeg = i % segCount;

            float n0x = normals[prevSeg].x, n0y = normals[prevSeg].y;
            float n1x = normals[nextSeg].x, n1y = normals[nextSeg].y;

            float avgNx = n0x + n1x, avgNy = n0y + n1y;
            float avgLen = std::sqrt(avgNx * avgNx + avgNy * avgNy);

            if (avgLen < 1e-6f) {
                // Nearly 180-degree turn: use either normal
                miters[i] = { px + n0x, py + n0y, px - n0x, py - n0y };
            } else {
                avgNx /= avgLen;
                avgNy /= avgLen;
                float dot = (n0x * n1x + n0y * n1y) / (hw * hw);
                float miterLen = hw / std::max(0.1f, std::sqrt(0.5f * (1.0f + dot)));
                // Clamp miter to 4× half-width to prevent spikes on very sharp angles
                miterLen = std::min(miterLen, hw * 4.0f);
                miters[i] = { px + avgNx * miterLen, py + avgNy * miterLen,
                              px - avgNx * miterLen, py - avgNy * miterLen };
            }
        }
    }

    // Build quads using miter points at each vertex for seamless joins
    std::vector<TriangleVertex> verts;
    verts.reserve(segCount * 6);

    for (uint32_t i = 0; i < segCount; i++) {
        uint32_t j = (i + 1) % pointCount;
        // Quad from vertex i miters to vertex j miters
        verts.push_back({ miters[i].lx, miters[i].ly, pr, pg, pb, pa });
        verts.push_back({ miters[i].rx, miters[i].ry, pr, pg, pb, pa });
        verts.push_back({ miters[j].lx, miters[j].ly, pr, pg, pb, pa });
        verts.push_back({ miters[j].lx, miters[j].ly, pr, pg, pb, pa });
        verts.push_back({ miters[i].rx, miters[i].ry, pr, pg, pb, pa });
        verts.push_back({ miters[j].rx, miters[j].ry, pr, pg, pb, pa });
    }
    if (!verts.empty())
        directRenderer_->AddTriangles(verts.data(), (uint32_t)verts.size());
}

void D3D12RenderTarget::FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule, int32_t edgeMode) {
    if (!isDrawing_ || !brush || !directRenderer_) return;

    CommitDeferredState();

    // ── Stencil-then-cover fast path (solid color brushes only).
    //
    // Mirrors docs/reference/pure_d3d12_path_renderer.h:
    //  • CPU once: flatten + fan triangulate, cached per (commands, scaleBucket)
    //    so the SAME upload bytes cover every frame of a smooth scale animation.
    //  • Per frame: two DrawInstanced calls (stencil pass + cover pass) per
    //    path. No CPU rasterization, no ear-clipping, no pixel-rect emit. The
    //    transform is applied in the vertex shader from a root constant.
    //
    // Gradient / image brushes don't fit the stencil model (gradient sampling
    // happens at fragment time and needs barycentric vertex colors), so they
    // fall through to the Impeller path below — which is unchanged.
    {
        float r, g, b, a;
        if (ExtractBrushColor(brush, r, g, b, a)) {
            auto geom = directRenderer_->GetOrBuildStencilPathGeometry(
                startX, startY, commands, commandLength);
            if (directRenderer_->AddStencilPath(geom, r, g, b, a, fillRule)) {
                return;
            }
        }
        // Brush wasn't a solid color, or the renderer wasn't ready: drop through
        // to the engine routing below.
    }

    // Route based on active rendering engine
    if (IsImpellerActive()) {
        // Impeller engine: CPU tessellation + D3D12 rasterization
        if (EnsureImpellerEngine()) {
            auto t = directRenderer_->GetCurrentTransform();
            float dpiScale = directRenderer_->GetDpiScale();
            float opacity = directRenderer_->GetOpacity();

            // Solid OR gradient: EncodeFillPath samples linear/radial gradients
            // per-vertex (EncodeGradientFillPath), so path fills get TRUE gradients.
            EngineBrushData bd;
            std::vector<EngineBrushData::GradientStop> stopStore;
            if (BrushToEngineBrush(brush, opacity, bd, stopStore)) {
                EngineTransform et;
                et.m11 = t.m11 * dpiScale; et.m12 = t.m12 * dpiScale;
                et.m21 = t.m21 * dpiScale; et.m22 = t.m22 * dpiScale;
                et.dx = t.dx * dpiScale; et.dy = t.dy * dpiScale;

                FillRule fr = (fillRule == 1) ? FillRule::NonZero : FillRule::EvenOdd;
                if (impellerEngine_->EncodeFillPath(startX, startY, commands, commandLength, bd, fr, et, edgeMode)) {
                    // Lazy flush: see FillPolygon for the coalescing rationale.
                    return;
                }
            }
        }
        // Impeller encoding failed — fall through to CPU fallback
    } else {
        // Vello engine (default): GPU compute path renderer
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
    }

    // CPU triangulation fallback
    float r, g, b, a;
    if (!ExtractBrushColor(brush, r, g, b, a)) return;

    float opacity = directRenderer_->GetOpacity();
    float pr = r * a * opacity, pg = g * a * opacity, pb = b * a * opacity, pa = a * opacity;

    // Use FlattenPathToContours for proper command parsing:
    // - Handles ClosePath (closes contour back to sub-path start)
    // - Handles ArcTo (tag 4) with adaptive arc flattening
    // - Uses adaptive Bézier subdivision (De Casteljau) instead of fixed N=12
    // - Correctly splits compound paths at MoveTo boundaries
    std::vector<Contour> contours = FlattenPathToContours(startX, startY, commands, commandLength, 0.5f);

    // Remove degenerate contours
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
    const float* dashPattern, uint32_t dashCount, float dashOffset, int32_t edgeMode) {
    if (!isDrawing_ || !brush || !directRenderer_) return;

    CommitDeferredState();

    // Route based on active rendering engine
    if (IsImpellerActive()) {
        // Impeller engine: CPU stroke expansion + D3D12 rasterization
        if (EnsureImpellerEngine()) {
            auto t = directRenderer_->GetCurrentTransform();
            float dpiScale = directRenderer_->GetDpiScale();
            float opacity = directRenderer_->GetOpacity();

            // Solid strokes encode their color directly; linear/radial gradient
            // strokes now render as TRUE per-vertex gradients (EncodeStrokePath
            // samples the gradient along the stroke mesh). Only dashed / explicit-
            // analytic gradients degrade to the flat first-stop fallback in
            // bd.r/g/b/a. Either way the stroke is visible, not dropped.
            EngineBrushData bd;
            std::vector<EngineBrushData::GradientStop> stopStore;
            if (BrushToEngineBrush(brush, opacity, bd, stopStore)) {
                EngineTransform et;
                et.m11 = t.m11 * dpiScale; et.m12 = t.m12 * dpiScale;
                et.m21 = t.m21 * dpiScale; et.m22 = t.m22 * dpiScale;
                et.dx = t.dx * dpiScale; et.dy = t.dy * dpiScale;

                if (impellerEngine_->EncodeStrokePath(startX, startY, commands, commandLength,
                        bd, strokeWidth, closed, lineJoin, miterLimit, lineCap,
                        dashPattern, dashCount, dashOffset, et, edgeMode)) {
                    // Lazy flush: see FillPolygon for the coalescing rationale.
                    return;
                }
            }
        }
        // Impeller encoding failed — fall through to CPU
    } else {
        // Vello engine (default)
        auto* vello = directRenderer_->GetVelloRenderer();
        if (vello) {
            directRenderer_->ApplyScissorToVello();
            float opacity = directRenderer_->GetOpacity();
            auto t = directRenderer_->GetCurrentTransform();
            float dpiScale = directRenderer_->GetDpiScale();
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
    }

    // CPU polyline fallback: use FlattenPathCommands for proper command parsing
    // (handles ClosePath, ArcTo, adaptive Bézier subdivision)
    std::vector<float> pts = FlattenPathCommands(startX, startY, commands, commandLength, 0.5f);
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
        if (ExtractStrokeColor(strokeBrush, r, g, b, a)) {
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
    jalium::text_stats::AddDrawTextCall();

    auto* tf = static_cast<D3D12TextFormat*>(format);
    float r = 1, g = 1, b = 1, a = 1;
    ExtractBrushColor(brush, r, g, b, a);

    ComPtr<IDWriteTextLayout> layout;
    uint64_t layoutKey = 0;
    if (FAILED(tf->CreateLayout(text, textLength, w, h, &layout, &layoutKey))) return;
    // Resolve per-element TextOptions against the process-wide fallback chain
    // here at the boundary so AddText / GenerateGlyphs / RasterizeGlyph only
    // see concrete modes; the glyph atlas keys off the resolved AA mode so
    // ClearType and Grayscale runs cache independently within the same frame.
    const int32_t aaMode = tf->ResolveEffectiveTextRenderingMode();
    const int32_t hintingMode = tf->GetTextHintingMode();
    directRenderer_->AddText(layout.Get(), x, y, r, g, b, a, layoutKey, aaMode, hintingMode);
}

// ============================================================================
// Drawing — Bitmaps
// ============================================================================

void D3D12RenderTarget::DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity) {
    DrawBitmap(bitmap, x, y, w, h, opacity, 0 /* JALIUM_BITMAP_SCALING_UNSPECIFIED */);
}

void D3D12RenderTarget::DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity, int scalingMode) {
    if (!isDrawing_ || !directRenderer_ || !bitmap) return;
    FlushVelloIfNeeded();

    auto* d3d12Bmp = static_cast<D3D12Bitmap*>(bitmap);
    auto* cl = directRenderer_->GetCommandList();
    auto* tex = d3d12Bmp->GetOrCreateD3D12Texture(backend_->GetDevice(), cl);
    if (!tex) return;
    // Query the actual texture format — WIC bitmaps are typically B8G8R8A8, not R8G8B8A8.
    // Using the wrong format family for the SRV causes D3D12 validation failure → device lost.
    auto texDesc = tex->GetDesc();
    // Pass the upload buffer so AddBitmap keeps a ref alive in bitmapTextures_
    // until the next BeginFrame fence-wait drains it. Without this the upload
    // buffer's only owner is the D3D12Bitmap; if the bitmap is Disposed
    // mid-frame (worker-thread LRU eviction, finalizer), the upload buffer
    // ref hits zero while the just-recorded CopyTextureRegion is still
    // pending GPU execution — D3D12 ERROR #921.
    auto uploadBuffer = d3d12Bmp->GetCurrentUploadBuffer();
    directRenderer_->AddBitmap(x, y, w, h, opacity * directRenderer_->GetOpacity(),
                                tex, texDesc.Format, 1.0f, 1.0f, scalingMode,
                                uploadBuffer.Get());
}

void D3D12RenderTarget::BlitInkLayer(D3D12InkLayerBitmap* inkBitmap,
                                      float dstX, float dstY, float opacity)
{
    if (!isDrawing_ || !directRenderer_ || !inkBitmap) return;
    if (!inkBitmap->Texture()) return;
    FlushVelloIfNeeded();

    // DispatchBrush / Clear leave the ink texture in PIXEL_SHADER_RESOURCE,
    // which is exactly the state AddBitmap expects. No barrier needed here.
    directRenderer_->AddBitmap(
        dstX, dstY,
        (float)inkBitmap->Width(),
        (float)inkBitmap->Height(),
        opacity * directRenderer_->GetOpacity(),
        inkBitmap->Texture(),
        inkBitmap->Format(),
        1.0f, 1.0f, 0 /* unspecified scaling */);
}

// ─── Retained GPU layers ────────────────────────────────────────────────────
// Forward to the direct renderer, flushing any deferred Vello/state first so the
// RT redirect (realize) and the composite quad see committed state — mirrors the
// FlushVelloIfNeeded() guard in BlitInkLayer above.
bool D3D12RenderTarget::SupportsRetainedLayers() const
{
    return directRenderer_ && directRenderer_->SupportsRetainedLayers();
}

void* D3D12RenderTarget::RealizeLayerBegin(void* existingLayer, float x, float y, float w, float h)
{
    if (!isDrawing_ || !directRenderer_) return nullptr;
    FlushVelloIfNeeded();
    return directRenderer_->BeginRetainedLayerCapture(
        reinterpret_cast<D3D12RetainedLayer*>(existingLayer), x, y, w, h);
}

void D3D12RenderTarget::RealizeLayerEnd(void* layer)
{
    if (!isDrawing_ || !directRenderer_) return;
    FlushVelloIfNeeded();
    directRenderer_->EndRetainedLayerCapture(reinterpret_cast<D3D12RetainedLayer*>(layer));
}

void D3D12RenderTarget::CompositeLayer(void* layer, float x, float y, float w, float h, float opacity)
{
    if (!isDrawing_ || !directRenderer_ || !layer) return;
    FlushVelloIfNeeded();
    directRenderer_->CompositeRetainedLayer(
        reinterpret_cast<D3D12RetainedLayer*>(layer), x, y, w, h, opacity);
}

void D3D12RenderTarget::DestroyRetainedLayer(void* layer)
{
    if (!layer) return;
    if (directRenderer_) {
        directRenderer_->DestroyRetainedLayer(reinterpret_cast<D3D12RetainedLayer*>(layer));
    } else {
        // Renderer already torn down — the layer's destructor still retires its
        // texture through the backend graveyard it captured at creation. If the
        // CREATING device was removed (GPU switch), that graveyard may itself be
        // gone — orphan the COM refs first (nothing is in flight on a removed
        // device), same rule as DirectRenderer::DestroyRetainedLayer.
        auto* l = reinterpret_cast<D3D12RetainedLayer*>(layer);
        if (l->CreatingDeviceRemoved()) {
            l->OrphanGpuResources();
            g_retainedLayerOrphanedCount.fetch_add(1, std::memory_order_relaxed);
        } else {
            g_retainedLayerGraveyardCount.fetch_add(1, std::memory_order_relaxed);
        }
        delete l;
    }
}

// ============================================================================
// State — Transform, Clip, Opacity (deferred / peephole-collapsing)
// ============================================================================
//
// Each Push* records its op into pendingStateOps_ and returns immediately.
// CommitDeferredState() is called at the entry of every real draw method
// (via FlushVelloIfNeeded for non-path draws, explicitly inside the path
// emit methods) and walks the pending list in order, replaying each op
// onto DirectRenderer's state stacks.
//
// A Pop* that finds a matching push at the tail of pendingStateOps_ — i.e.
// no draw has fired since that push — simply pops the pending entry. Both
// the directRenderer state mutation and the SyncScissorToImpeller copy are
// skipped entirely, which is the dominant case for tree-traversal templates
// that visit empty branches.

bool D3D12RenderTarget::IdentityMatrixSkip(const float* m)
{
    constexpr float kEpsilon = 1e-7f;
    return std::abs(m[0] - 1.0f) < kEpsilon &&
           std::abs(m[1])        < kEpsilon &&
           std::abs(m[2])        < kEpsilon &&
           std::abs(m[3] - 1.0f) < kEpsilon &&
           std::abs(m[4])        < kEpsilon &&
           std::abs(m[5])        < kEpsilon;
}

void D3D12RenderTarget::EmitDeferredOp(const DeferredStateOp& op)
{
    if (!directRenderer_) return;
    switch (op.kind) {
        case DeferredOpKind::Transform:
            directRenderer_->PushTransform(op.data[0], op.data[1], op.data[2],
                                           op.data[3], op.data[4], op.data[5]);
            break;
        case DeferredOpKind::ClipAxisAligned:
        case DeferredOpKind::ClipAxisAlignedAliased:
            directRenderer_->PushScissor(op.data[0], op.data[1], op.data[2], op.data[3]);
            clipFrameIsRounded_.push_back(false);
            SyncScissorToImpeller();
            break;
        case DeferredOpKind::ClipRoundedRect:
            // No flush at the clip boundary: Impeller batches snapshot the rounded
            // clip per batch (SyncScissorToImpeller mirrors it into the engine), so
            // pending batches keep their draw-time clip even after the live stack
            // moves on. This is what lets path batches coalesce across rounded-clip
            // pushes instead of forcing a GPU flush at every Border/titlebar.
            directRenderer_->PushScissor(op.data[0], op.data[1], op.data[2], op.data[3]);
            directRenderer_->PushRoundedClip(op.data[0], op.data[1], op.data[2], op.data[3],
                                              op.data[4], op.data[5]);
            clipFrameIsRounded_.push_back(true);
            SyncScissorToImpeller();
            break;
        case DeferredOpKind::ClipPerCornerRounded:
            // See ClipRoundedRect: per-batch rounded-clip snapshot, no flush.
            directRenderer_->PushScissor(op.data[0], op.data[1], op.data[2], op.data[3]);
            directRenderer_->PushPerCornerRoundedClip(op.data[0], op.data[1], op.data[2], op.data[3],
                                                       op.data[4], op.data[5], op.data[6], op.data[7]);
            clipFrameIsRounded_.push_back(true);
            SyncScissorToImpeller();
            break;
        case DeferredOpKind::Opacity:
            opacityStack_.push(directRenderer_->GetOpacity());
            directRenderer_->SetOpacity(directRenderer_->GetOpacity() * op.data[0]);
            break;
    }
}

void D3D12RenderTarget::CommitDeferredState()
{
    if (pendingStateOps_.empty()) return;
    // Replay in insertion order — LIFO is preserved because EmitDeferredOp
    // mirrors each Push* exactly onto DirectRenderer's matching stack.
    for (auto& op : pendingStateOps_) {
        EmitDeferredOp(op);
    }
    pendingStateOps_.clear();
}

void D3D12RenderTarget::PushTransform(const float* matrix) {
    if (!directRenderer_ || !matrix) return;
    // Identity transform is a true no-op. The matching PopTransform below
    // can still pop the implicit identity entry from pendingStateOps_ even
    // when none was pushed because we encode an Identity-Transform op
    // anyway — that keeps the LIFO invariant trivially correct for nested
    // pushes that interleave identities with real transforms.
    DeferredStateOp op{ DeferredOpKind::Transform, {} };
    op.data[0] = matrix[0]; op.data[1] = matrix[1];
    op.data[2] = matrix[2]; op.data[3] = matrix[3];
    op.data[4] = matrix[4]; op.data[5] = matrix[5];
    pendingStateOps_.push_back(op);
}

void D3D12RenderTarget::PopTransform() {
    if (!directRenderer_) return;
    if (!pendingStateOps_.empty() && pendingStateOps_.back().kind == DeferredOpKind::Transform) {
        // Peephole: the matching push never reached DirectRenderer — drop it.
        pendingStateOps_.pop_back();
        return;
    }
    directRenderer_->PopTransform();
}

void D3D12RenderTarget::PushClip(float x, float y, float w, float h) {
    if (!directRenderer_) return;
    DeferredStateOp op{ DeferredOpKind::ClipAxisAligned, {} };
    op.data[0] = x; op.data[1] = y; op.data[2] = w; op.data[3] = h;
    pendingStateOps_.push_back(op);
}

void D3D12RenderTarget::PushClipAliased(float x, float y, float w, float h) {
    if (!directRenderer_) return;
    DeferredStateOp op{ DeferredOpKind::ClipAxisAlignedAliased, {} };
    op.data[0] = x; op.data[1] = y; op.data[2] = w; op.data[3] = h;
    pendingStateOps_.push_back(op);
}

void D3D12RenderTarget::PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry) {
    if (!directRenderer_) return;
    DeferredStateOp op{ DeferredOpKind::ClipRoundedRect, {} };
    op.data[0] = x;  op.data[1] = y; op.data[2] = w; op.data[3] = h;
    op.data[4] = rx; op.data[5] = ry;
    pendingStateOps_.push_back(op);
}

void D3D12RenderTarget::PushPerCornerRoundedRectClip(float x, float y, float w, float h,
    float tl, float tr, float br, float bl)
{
    if (!directRenderer_) return;
    DeferredStateOp op{ DeferredOpKind::ClipPerCornerRounded, {} };
    op.data[0] = x;  op.data[1] = y;  op.data[2] = w;  op.data[3] = h;
    op.data[4] = tl; op.data[5] = tr; op.data[6] = br; op.data[7] = bl;
    pendingStateOps_.push_back(op);
}

void D3D12RenderTarget::PopClip() {
    if (!directRenderer_) return;
    if (!pendingStateOps_.empty()) {
        auto kind = pendingStateOps_.back().kind;
        if (kind == DeferredOpKind::ClipAxisAligned ||
            kind == DeferredOpKind::ClipAxisAlignedAliased ||
            kind == DeferredOpKind::ClipRoundedRect ||
            kind == DeferredOpKind::ClipPerCornerRounded) {
            pendingStateOps_.pop_back();
            return;
        }
    }
    bool wasRounded = false;
    if (!clipFrameIsRounded_.empty()) {
        wasRounded = clipFrameIsRounded_.back();
        clipFrameIsRounded_.pop_back();
    }
    if (wasRounded) {
        // No flush on pop: pending path batches already snapshotted this rounded
        // clip (per-batch payload), so popping it here doesn't disturb them — on
        // replay FlushImpellerBatches feeds each batch's snapshot back as a forced
        // override. (See EmitDeferredOp::ClipRoundedRect.)
        directRenderer_->PopRoundedClip();
    }
    directRenderer_->PopScissor();
    SyncScissorToImpeller();
}

void D3D12RenderTarget::PunchTransparentRect(float x, float y, float w, float h) {
    if (!isDrawing_ || !directRenderer_) return;
    CommitDeferredState();
    directRenderer_->PunchTransparentRect(x, y, w, h);
}

void D3D12RenderTarget::PushOpacity(float opacity) {
    if (!directRenderer_) return;
    DeferredStateOp op{ DeferredOpKind::Opacity, {} };
    op.data[0] = opacity;
    pendingStateOps_.push_back(op);
}

void D3D12RenderTarget::PopOpacity() {
    if (!directRenderer_) return;
    if (!pendingStateOps_.empty() && pendingStateOps_.back().kind == DeferredOpKind::Opacity) {
        pendingStateOps_.pop_back();
        return;
    }
    if (opacityStack_.empty()) return;
    directRenderer_->SetOpacity(opacityStack_.top());
    opacityStack_.pop();
}

void D3D12RenderTarget::SetShapeType(int type, float n) {
    if (directRenderer_) directRenderer_->SetShapeType((float)type, n);
}

void D3D12RenderTarget::SetVSyncEnabled(bool enabled) { vsyncEnabled_ = enabled; }

void D3D12RenderTarget::SetExternalPresentPacing(bool enabled) {
    // Only meaningful when the swap chain actually has a frame-latency
    // waitable for the caller to consume; without one BeginDraw never waited
    // in the first place, so external pacing would silently remove the only
    // back-pressure (DXGI's own Present blocking). Composition (DComp) swap
    // chains ALSO carry a waitable, but their Present + dcomp Commit cadence
    // must stay internally paced — reject them too. Callers check
    // GetFrameLatencyWaitable() != 0 and non-composition before enabling;
    // this is defence in depth for direct C-ABI hosts.
    externalPresentPacing_ = enabled && frameLatencyWaitable_ != nullptr && !isComposition_;
    if (externalPresentPacing_) {
        // BeginDraw stops writing the waitable-wait stats in this mode; clear
        // them so a stale value from before the switch (e.g. the render
        // thread's last frame ahead of a schema-gap latch handover) isn't
        // republished every frame by QueryGpuStats / the frametime log / the
        // "BeginDraw (wait)" DevTools split.
        lastFrameWaitableWaitNs_ = 0;
        accumulatingWaitableWaitNs_ = 0;
    }
}

void D3D12RenderTarget::SetPathMsaaSampleCount(uint32_t sampleCount) {
    if (directRenderer_) directRenderer_->SetPathMsaaSampleCount(sampleCount);
}

bool D3D12RenderTarget::DebugRemoveDevice() {
    // Official debug trigger for DEVICE_REMOVED: the device immediately enters
    // the removed state and GetDeviceRemovedReason reports
    // DXGI_ERROR_DEVICE_REMOVED — exactly what a GPU switch / driver restart
    // produces, but at a point the harness chooses (e.g. mid-frame between
    // BeginDraw and EndDraw). Affects every render target sharing this
    // backend's device, which mirrors the real event.
    if (!backend_) return false;
    ID3D12Device* device = backend_->GetDevice();
    if (!device) return false;
    Microsoft::WRL::ComPtr<ID3D12Device5> device5;
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&device5)))) return false;
    device5->RemoveDevice();
    return true;
}

bool D3D12RenderTarget::DebugGetRetainedDestroyCounts(uint64_t* orphaned, uint64_t* graveyard) {
    if (!orphaned || !graveyard) return false; // self-defending: the C-ABI wrapper null-checks, but this virtual is public
    *orphaned  = g_retainedLayerOrphanedCount.load(std::memory_order_relaxed);
    *graveyard = g_retainedLayerGraveyardCount.load(std::memory_order_relaxed);
    return true;
}

uint64_t D3D12RenderTarget::DebugDevicePointer() {
    return backend_ ? reinterpret_cast<uint64_t>(backend_->GetDevice()) : 0;
}

bool D3D12RenderTarget::DebugInOffscreenCapture() {
    return directRenderer_ && directRenderer_->IsInOffscreenCapture();
}

// TEST-ONLY (#921 regression self-check). Reproduces the same-thread "leaked-open
// command list" race and drives a resize through it, so the production guard in
// Resize (the `listOpen || isDrawing_` branch that must AbortFrame the leaked
// frame BEFORE the back buffers are freed) is verified end to end on a real GPU.
//
// The race staged here is the BeginFrame open-gap: BeginDraw opens the command
// list via directRenderer_->BeginFrame() and only sets isDrawing_=true afterwards,
// so there is a window where the command list is recording (and has already
// recorded a barrier referencing the back buffer) while isDrawing_ still reads
// false. We reproduce that window deterministically by opening the list the same
// way and NOT setting isDrawing_, then calling Resize on the SAME thread.
int32_t D3D12RenderTarget::DebugForceLeakedCommandListResize(int32_t newWidth, int32_t newHeight, int32_t* outListClosed) {
    if (outListClosed) *outListClosed = 0;
    if (!directRenderer_) return -1;            // no renderer to open a list on
    if (isDrawing_) return -2;                  // must start from a clean (idle) target
    // The resize has to actually change the size, or Resize short-circuits to
    // JALIUM_OK without ever reaching the guard (leaving the list open).
    if (newWidth == width_ && newHeight == height_) return -3;

    // Open the command list exactly as BeginDraw does, but DO NOT set isDrawing_:
    // this is the #921 open-gap (cmdListRecording_==true while isDrawing_==false).
    const float clearAlpha = isComposition_ ? 0.0f : clearA_;
    const bool opened = directRenderer_->BeginFrame(
        frameIndex_,
        static_cast<UINT>(width_), static_cast<UINT>(height_),
        fullInvalidation_,
        clearR_, clearG_, clearB_, clearAlpha);
    if (!opened) return -4;                     // device/fence gate — could not stage

    // Confirm we are genuinely in the leaked-open state on THIS thread before
    // exercising the guard (a different owner thread would take the BUSY branch).
    if (!directRenderer_->IsCommandListRecording()
        || directRenderer_->CommandListOwnerThread() != GetCurrentThreadId()
        || isDrawing_) {
        directRenderer_->AbortFrame();          // never leave a frame open
        return -5;
    }

    // The operation under test. On the same thread the guard must AbortFrame()
    // (Close the leaked list) BEFORE ReleaseBackBufferReferences()/ResizeBuffers()
    // free the back buffers it references. A regression that drops the AbortFrame
    // leaves the list open here and (under the debug layer) trips D3D12 #921
    // OBJECT_DELETED_WHILE_STILL_IN_USE.
    const JaliumResult resizeResult = Resize(newWidth, newHeight);

    // (b) The leaked command list must have been Closed by the guard.
    const bool listClosed = !directRenderer_->IsCommandListRecording();
    if (outListClosed) *outListClosed = listClosed ? 1 : 0;

    // Belt-and-suspenders: if a regression left the list open, Close it now so the
    // harness can tear the window down cleanly. The reported *outListClosed (0)
    // already records the failure for the test to assert on.
    if (directRenderer_->IsCommandListRecording()) {
        directRenderer_->AbortFrame();
    }

    return static_cast<int32_t>(resizeResult);
}

// TEST-ONLY (#921 Vello-output regression self-check). Stages a genuinely-open
// command list the same way DebugForceLeakedCommandListResize does (the BeginFrame
// open-gap: cmdListRecording_==true while isDrawing_==false), forces the Vello path
// on, then drives the 'JaliumVelloOutput' orphan sequence (see
// D3D12DirectRenderer::DebugForceVelloOutputOrphan) and reports whether the output
// texture survived the mid-frame bitmapTextures_.clear(). Pre-fix the texture's sole
// keep-alive was that bitmap entry, so the clear freed it while the open list still
// referenced it -> #921; post-fix RetireOutputTexture parks it on the fence-gated
// retired list so it survives. *outAlive: 1 = survived (fix held), 0 = freed in use.
int32_t D3D12RenderTarget::DebugForceVelloOutputOrphan(int32_t* outAlive) {
    if (outAlive) *outAlive = 0;
    if (!directRenderer_) return -1;            // no renderer to open a list on
    if (isDrawing_) return -2;                  // must start from a clean (idle) target

    // The D3D12 default engine is Impeller, so GetVelloRenderer() would be null. Force
    // the directRenderer's Vello path on for the staged frame only; toggle
    // SetVelloEnabled directly (NOT SetRenderingEngine) so the target's activeEngine_
    // is left untouched, and restore it from activeEngine_ on every exit below.
    directRenderer_->SetVelloEnabled(true);

    // Open the command list exactly as BeginDraw does, but DO NOT set isDrawing_ —
    // the same #921 open-gap so the orphan sequence records into a genuinely open list.
    const float clearAlpha = isComposition_ ? 0.0f : clearA_;
    const bool opened = directRenderer_->BeginFrame(
        frameIndex_,
        static_cast<UINT>(width_), static_cast<UINT>(height_),
        fullInvalidation_,
        clearR_, clearG_, clearB_, clearAlpha);
    if (!opened) {
        directRenderer_->SetVelloEnabled(!IsImpellerActive());
        return -4;                              // device/fence gate — could not stage (harness retries)
    }

    if (!directRenderer_->IsCommandListRecording()
        || directRenderer_->CommandListOwnerThread() != GetCurrentThreadId()
        || isDrawing_) {
        directRenderer_->AbortFrame();          // never leave a frame open
        directRenderer_->SetVelloEnabled(!IsImpellerActive());
        return -5;
    }

    // The operation under test: dispatch + composite + ForceNewOutputTexture + the
    // mid-frame FlushGraphicsForCompute clear, with detection of survival.
    const int32_t rc = directRenderer_->DebugForceVelloOutputOrphan(outAlive);

    // Close the (still-open) list cleanly so the harness can keep rendering, and
    // restore the engine flag to the target's real activeEngine_ (Impeller default).
    if (directRenderer_->IsCommandListRecording()) {
        directRenderer_->AbortFrame();
    }
    directRenderer_->SetVelloEnabled(!IsImpellerActive());
    return rc;
}

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

// ── Dirty-rect aggregation helpers ───────────────────────────────────────────
namespace {

inline bool RectContains(const D3D12RenderTarget::DirtyRect& outer,
                         const D3D12RenderTarget::DirtyRect& inner) {
    return outer.x <= inner.x
        && outer.y <= inner.y
        && outer.x + outer.w >= inner.x + inner.w
        && outer.y + outer.h >= inner.y + inner.h;
}

inline bool RectsIntersect(const D3D12RenderTarget::DirtyRect& a,
                           const D3D12RenderTarget::DirtyRect& b) {
    return a.x < b.x + b.w
        && b.x < a.x + a.w
        && a.y < b.y + b.h
        && b.y < a.y + a.h;
}

inline D3D12RenderTarget::DirtyRect RectUnion(
    const D3D12RenderTarget::DirtyRect& a,
    const D3D12RenderTarget::DirtyRect& b) {
    float x0 = (std::min)(a.x, b.x);
    float y0 = (std::min)(a.y, b.y);
    float x1 = (std::max)(a.x + a.w, b.x + b.w);
    float y1 = (std::max)(a.y + a.h, b.y + b.h);
    return { x0, y0, x1 - x0, y1 - y0 };
}

inline bool ShouldMergeRects(
    const D3D12RenderTarget::DirtyRect& a,
    const D3D12RenderTarget::DirtyRect& b,
    float adjacencyEpsilon, float wasteRatio) {
    if (RectsIntersect(a, b)) return true;

    bool xClose = a.x + a.w + adjacencyEpsilon >= b.x
        && b.x + b.w + adjacencyEpsilon >= a.x;
    bool yClose = a.y + a.h + adjacencyEpsilon >= b.y
        && b.y + b.h + adjacencyEpsilon >= a.y;
    if (xClose && yClose) return true;

    float aArea = a.w * a.h;
    float bArea = b.w * b.h;
    auto u = RectUnion(a, b);
    float uArea = u.w * u.h;
    float waste = uArea - (aArea + bArea);
    float larger = (std::max)(aArea, bArea);
    if (larger <= 0.0f) return false;
    return waste / larger <= wasteRatio;
}

} // namespace

void D3D12RenderTarget::AddDirtyRect(float x, float y, float w, float h) {
    if (fullInvalidation_) return;

    // Inflate by the fixed margin.  The C# caller now also applies a DPI-aware
    // margin, but we still add a small constant here so that external callers
    // (DevTools overlays, tests) don't have to know about AA fringes.
    float margin = DirtyRectMargin;
    DirtyRect r{
        (std::max)(x - margin, 0.0f),
        (std::max)(y - margin, 0.0f),
        w + margin * 2.0f,
        h + margin * 2.0f
    };
    if (r.w <= 0.0f || r.h <= 0.0f) return;

    // 1. Absorption — new rect already contained in an existing one.
    for (const auto& existing : dirtyRects_) {
        if (RectContains(existing, r)) return;
    }

    // 2. Replacement — new rect swallows existing ones; drop them.
    for (size_t i = dirtyRects_.size(); i-- > 0; ) {
        if (RectContains(r, dirtyRects_[i])) {
            dirtyRects_.erase(dirtyRects_.begin() + i);
        }
    }

    // 3. Beneficial merge — overlap / near-adjacency. Iterate to a fixed point
    //    because merging two rects may make the result eligible to merge with
    //    yet another.
    bool changed = true;
    while (changed) {
        changed = false;
        for (size_t i = 0; i < dirtyRects_.size(); i++) {
            if (ShouldMergeRects(dirtyRects_[i], r,
                                 DirtyRectAdjacencyEpsilon,
                                 DirtyRectMergeWasteRatio)) {
                r = RectUnion(dirtyRects_[i], r);
                dirtyRects_.erase(dirtyRects_.begin() + i);
                changed = true;
                break;
            }
        }
    }

    dirtyRects_.push_back(r);

    // 4. Capacity — if we've overflown, perform repeated minimum-waste merges
    //    of the closest pair. This bounds memory and Present1-array size
    //    without the "give up → full redraw" fallback the old code used.
    while (dirtyRects_.size() > MaxDirtyRects) {
        size_t bestI = 0, bestJ = 1;
        float bestExtra = std::numeric_limits<float>::max();
        for (size_t i = 0; i < dirtyRects_.size(); i++) {
            float ai = dirtyRects_[i].w * dirtyRects_[i].h;
            for (size_t j = i + 1; j < dirtyRects_.size(); j++) {
                auto u = RectUnion(dirtyRects_[i], dirtyRects_[j]);
                float extra = u.w * u.h - ai - dirtyRects_[j].w * dirtyRects_[j].h;
                if (extra < bestExtra) {
                    bestExtra = extra;
                    bestI = i;
                    bestJ = j;
                }
            }
        }
        auto merged = RectUnion(dirtyRects_[bestI], dirtyRects_[bestJ]);
        dirtyRects_.erase(dirtyRects_.begin() + bestJ);
        dirtyRects_.erase(dirtyRects_.begin() + bestI);
        dirtyRects_.push_back(merged);
    }
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
    CommitDeferredState();
    if (!directRenderer_->FlushGraphicsForCompute()) return;  // device lost — frame will abort
    // Tag everything from here through DrawSnapshotBlurred as the Backdrop
    // GPU category. FlushGraphicsForCompute() above drained pending batches
    // (those already have their own per-batch category marks inside
    // RecordDrawCommands), so the next span is genuinely backdrop work.
    directRenderer_->MarkGpuTimingPoint(D3D12DirectRenderer::GpuTimingCategory::Backdrop);
    if (!directRenderer_->CaptureSnapshot()) {
        // Failure path: hand the span back to Other so we don't leave the
        // next batch (whose PSO switch will re-tag anyway) attributed to Backdrop.
        directRenderer_->MarkGpuTimingPoint(D3D12DirectRenderer::GpuTimingCategory::Other);
        return;
    }
    float avgRadius = cornerRadiusTL;
    directRenderer_->DrawSnapshotBlurred(x, y, w, h, blurRadius, 0, 0, 0, tintOpacity, avgRadius);
    directRenderer_->MarkGpuTimingPoint(D3D12DirectRenderer::GpuTimingCategory::Other);
}

void D3D12RenderTarget::DrawGlowingBorderHighlight(
    float x, float y, float w, float h,
    float animationPhase, float r, float g, float b,
    float strokeWidth, float trailLength, float dimOpacity,
    float screenWidth, float screenHeight)
{
    if (!isDrawing_ || !directRenderer_) return;
    CommitDeferredState();
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
    CommitDeferredState();
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
    CommitDeferredState();
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
    CommitDeferredState();
    if (!directRenderer_->FlushGraphicsForCompute()) return;  // device lost — frame will abort
    directRenderer_->MarkGpuTimingPoint(D3D12DirectRenderer::GpuTimingCategory::LiquidGlass);
    if (neighborCount > 0 && preGlassSnapshotCaptured_) {
        // Reuse existing pre-glass snapshot for fused panels
    } else {
        if (!directRenderer_->CaptureSnapshot()) {
            directRenderer_->MarkGpuTimingPoint(D3D12DirectRenderer::GpuTimingCategory::Other);
            return;
        }
        if (neighborCount > 0) preGlassSnapshotCaptured_ = true;
    }
    directRenderer_->DrawLiquidGlass(x, y, w, h, cornerRadius, blurRadius,
        refractionAmount, chromaticAberration,
        tintR, tintG, tintB, tintOpacity,
        lightX, lightY, highlightBoost,
        shapeType, shapeExponent,
        neighborCount, fusionRadius, neighborData);
    directRenderer_->MarkGpuTimingPoint(D3D12DirectRenderer::GpuTimingCategory::Other);
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
    CommitDeferredState();
    directRenderer_->DrawDesktopBackdrop(x, y, w, h, blurRadius, tintR, tintG, tintB, tintOpacity);
}

// ============================================================================
// Transition Capture
// ============================================================================

void D3D12RenderTarget::BeginTransitionCapture(int slot, float x, float y, float w, float h) {
    if (!isDrawing_ || !directRenderer_ || slot < 0 || slot > 1) return;
    CommitDeferredState();
    // Drain pending Impeller batches to the back buffer before redirecting the
    // render target to the offscreen slot (see BeginEffectCapture for the full
    // rationale — otherwise pre-capture strokes leak into the captured texture).
    FlushImpellerBatches();
    directRenderer_->BeginOffscreenCapture(slot, x, y, w, h);
}

void D3D12RenderTarget::EndTransitionCapture(int slot) {
    if (!isDrawing_ || !directRenderer_ || slot < 0 || slot > 1) return;
    CommitDeferredState();
    directRenderer_->EndOffscreenCapture(slot);
}

void D3D12RenderTarget::DrawTransitionShader(float x, float y, float w, float h, float progress, int mode) {
    // TODO: implement transition shader effect in DirectRenderer
    (void)x; (void)y; (void)w; (void)h; (void)progress; (void)mode;
}

void D3D12RenderTarget::DrawCapturedTransition(int slot, float x, float y, float w, float h, float opacity) {
    if (!isDrawing_ || !directRenderer_ || slot < 0 || slot > 1) return;
    CommitDeferredState();
    directRenderer_->DrawOffscreenBitmap(slot, x, y, w, h, opacity);
}

// ============================================================================
// Effect Capture
// ============================================================================

void D3D12RenderTarget::BeginEffectCapture(float x, float y, float w, float h) {
    if (!isDrawing_ || !directRenderer_) { lastEffectCaptureOk_ = false; return; }
    CommitDeferredState();
    // Drain any pending Impeller stroke/fill batches to the DirectRenderer's
    // triangle list BEFORE we redirect the render target to the offscreen
    // capture. Without this, an element rendered just before an effect element
    // (e.g. a card's arrow glyph immediately preceding the NEXT card's drop
    // shadow) is still queued in the Impeller engine; it would otherwise be
    // drained mid-capture and drawn into the effect's offscreen texture instead
    // of the back buffer, so every such element except the last (which has no
    // following capture) silently vanished. FlushGraphicsForCompute then paints
    // the now-materialised triangles to the current (back-buffer) target.
    FlushImpellerBatches();
    if (!directRenderer_->FlushGraphicsForCompute()) {
        // Device lost — frame will abort; EndEffectCapture sees the flag and
        // skips its EndOffscreenCapture.
        lastEffectCaptureOk_ = false;
        return;
    }
    lastEffectCaptureOk_ = directRenderer_->BeginOffscreenCapture(0, x, y, w, h);
}

void D3D12RenderTarget::EndEffectCapture() {
    if (!isDrawing_ || !directRenderer_) return;
    CommitDeferredState();
    if (lastEffectCaptureOk_) {
        directRenderer_->EndOffscreenCapture(0);
    }
}

void D3D12RenderTarget::DrawBlurEffect(float x, float y, float w, float h, float radius,
    float uvOffsetX, float uvOffsetY)
{
    if (!isDrawing_ || !directRenderer_) return;
    CommitDeferredState();
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
    CommitDeferredState();
    if (!lastEffectCaptureOk_) return;

    // Soft drop shadow via layered SDF rounded-rects drawn DIRECTLY on the main RT.
    //
    // 2026-06-02 refactor: the previous implementation rendered the shadow into offscreen
    // slot 1 and ran a compute-shader gaussian blur (BlurOffscreenSlot). That element-level
    // path was fragile and had never been exercised by real XAML:
    //   * BlurOffscreenSlot's return value was ignored, so whenever the blur was skipped or
    //     failed (e.g. blur resources not ready, or the per-frame offscreen-temp guard tripped
    //     for the 2nd+ shadowed element in a frame) an UNBLURRED hard rounded-rect was still
    //     composited — the "ghost rectangle" artifact;
    //   * the compute-blur / offscreen round-trip could leave the command list / pipeline in a
    //     state that dropped subsequent draws in the frame (observed: the title bar and other
    //     chrome vanished when a page used many element shadows).
    // Approximating the blur with N concentric, expanding, equal-alpha rounded rects drawn
    // straight to the main RT removes the offscreen + compute round-trip entirely: no ghost
    // rectangles and no pipeline corruption. Intensity stays faithful to the requested alpha
    // (cumulative centre alpha ≈ a, since over-blending N layers of a/N gives 1-(1-a/N)^N ≈ a),
    // so callers still tune the shadow purely from DropShadowEffect.Opacity — no native rebuild.
    float baseOpacity = directRenderer_->GetOpacity();
    if (a > 0.0f && baseOpacity > 0.0f) {
        const int LAYERS = 7;
        float perLayerA = a / static_cast<float>(LAYERS);
        float maxSpread = (blurRadius > 0.0f) ? blurRadius : 0.0f;
        // Outermost (largest) first so closer-to-element layers paint last on top.
        for (int i = LAYERS; i >= 1; --i) {
            float spread = maxSpread * (static_cast<float>(i) / static_cast<float>(LAYERS));
            SdfRectInstance inst = {};
            inst.posX = x + offsetX - spread;
            inst.posY = y + offsetY - spread;
            inst.sizeX = w + 2.0f * spread;
            inst.sizeY = h + 2.0f * spread;
            inst.cornerTL = cornerTL + spread; inst.cornerTR = cornerTR + spread;
            inst.cornerBR = cornerBR + spread; inst.cornerBL = cornerBL + spread;
            // Straight (non-premultiplied) color: the SdfRect PS does fill.rgb * coverage
            // and the normal fill path passes straight color (inst.fillR = r). Premultiplying
            // here by perLayerA dimmed the RGB an extra factor relative to the alpha, washing
            // a colored glow/shadow (e.g. GlowEmerald #34D399) out to a neutral gray halo.
            inst.fillR = r; inst.fillG = g;
            inst.fillB = b; inst.fillA = perLayerA;
            inst.opacity = baseOpacity;
            directRenderer_->AddSdfRect(inst);
        }
    }

    // Composite original element content on top of shadow
    directRenderer_->DrawOffscreenBitmapCropped(0, x, y, w, h,
        uvOffsetX, uvOffsetY, 1.0f);
}

// Pixel shader for the alpha-based outer glow. Runtime-compiled + cached by source
// hash via DrawShaderEffectFromSource. It multi-tap gaussian-blurs the captured
// element's ALPHA (offscreen slot 0 holds the silhouette on a transparent {0,0,0,0}
// background) and tints it, so the halo hugs glyph/shape contours instead of the old
// rectangular SDF approximation.
//
// b0 constant buffer carries 10 floats (uploaded by DrawCustomShaderEffect):
//   [0..3] tint.rgb + global glow alpha (opacity*intensity, clamped on CPU)
//   [4..5] texel   = 1/offscreenW, 1/offscreenH   (one source pixel, UV units)
//   [6..7] uvScale = capturePx/offscreen          (maps quad uv[0,1] -> capture sub-rect)
//   [8]    radiusTaps   [9] sigma
// The offscreen atlas is larger than the capture, so uv is scaled by uvScale and taps
// are clamped to the cleared capture sub-rect to avoid sampling stale atlas texels.
static const char kOuterGlowPS[] = R"HLSL(
Texture2D<float4> srcTex : register(t1);
SamplerState linearSampler : register(s0);

struct GlowCB {
    float4 tint;
    float2 step;       // per-tap UV step = texel * (radiusPx / K)
    float2 uvScale;
    float  kTaps;      // taps each direction; the 2D grid is (2K+1)x(2K+1)
    float  sigma;      // gaussian sigma in tap units
};
ConstantBuffer<GlowCB> gGlow : register(b0);

struct PsInput { float4 clipPos : SV_Position; float2 uv : TEXCOORD0; };

float4 main(PsInput input) : SV_Target
{
    int K = (int)gGlow.kTaps;
    if (K < 1) K = 1;
    if (K > 12) K = 12;                 // (2K+1)^2 grid — hard cap on the inner loops
    float sigma = max(gGlow.sigma, 0.5);
    float twoSigma2 = 2.0 * sigma * sigma;

    // Map the quad's [0,1] uv onto the capture sub-rect of the shared offscreen atlas.
    float2 baseUv = input.uv * gGlow.uvScale;
    float2 lo = float2(0.0, 0.0);
    float2 hi = gGlow.uvScale;   // never sample past the cleared capture region

    // True 2D RADIAL gaussian over a (2K+1)x(2K+1) grid. Summing one horizontal 1D
    // pass and one vertical 1D pass (the previous approach) is NOT a 2D gaussian — it
    // is a PLUS-shaped kernel that shows up as horizontal/vertical streaks, not a round
    // halo. A correct separable blur would need two convolution passes (H then V) with
    // an intermediate texture; a single-pass 2D grid is the correct alternative. The
    // per-tap step spans the glow radius in K steps, so the grid covers the whole halo.
    float accumA = 0.0;
    float accumW = 0.0;
    [loop]
    for (int dy = -K; dy <= K; ++dy)
    {
        [loop]
        for (int dx = -K; dx <= K; ++dx)
        {
            float wgt = exp(-(float(dx * dx + dy * dy)) / twoSigma2);
            float2 uv = clamp(baseUv + float2(float(dx), float(dy)) * gGlow.step, lo, hi);
            accumA += srcTex.SampleLevel(linearSampler, uv, 0).a * wgt;
            accumW += wgt;
        }
    }

    // Knockout the element's own silhouette so the glow lives ONLY outside it.
    // Without this the blurred alpha is ~1 under the glyph and bleeds through the
    // glyph's antialiased (semi-transparent) edges when the crisp text is composited
    // on top — fattening every stroke and filling the gaps between strokes with
    // orange, which reads as a blurry / out-of-focus "double vision" text. Sampling
    // the center alpha and suppressing glow where the element is opaque keeps the
    // text razor-sharp with the halo strictly around the contour.
    float centerA = srcTex.SampleLevel(linearSampler, clamp(baseUv, lo, hi), 0).a;
    float glowAlpha = (accumW > 0.0) ? (accumA / accumW) : 0.0;
    glowAlpha *= saturate(1.0 - centerA);
    glowAlpha *= gGlow.tint.a;

    // Premultiplied output for the SrcBlend=ONE / DestBlend=INV_SRC_ALPHA PSO.
    float3 rgb = gGlow.tint.rgb * glowAlpha;
    float4 outc = float4(rgb, glowAlpha);
    if (outc.a < (1.0 / 255.0)) discard;
    return outc;
}
)HLSL";

void D3D12RenderTarget::DrawOuterGlowEffect(float x, float y, float w, float h,
    float glowSize, float r, float g, float b, float a, float intensity,
    float uvOffsetX, float uvOffsetY,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    if (!isDrawing_ || !directRenderer_) return;
    CommitDeferredState();
    if (!lastEffectCaptureOk_) return;
    (void)cornerTL; (void)cornerTR; (void)cornerBR; (void)cornerBL;  // glow follows alpha, not rounded-rect corners

    // Alpha-based, silhouette-following outer glow.
    //
    // The element was captured into offscreen slot 0 with EffectPadding =
    // GlowSize+EffectiveBlurRadius of transparent margin on every side
    // (Visual.cs / OuterGlowEffect.EffectPadding), cleared to {0,0,0,0}. So slot 0
    // holds the element's ALPHA silhouette on transparent, with room to bleed out by
    // ~glowSize. kOuterGlowPS multi-tap gaussian-blurs that ALPHA and tints it,
    // producing a halo that hugs glyph/shape edges instead of the old 7-layer
    // rectangular SDF approximation (which lit the whole bounds rectangle).
    //
    // Routed through DrawShaderEffectFromSource -> DrawCustomShaderEffect, the
    // already-proven path that (a) FlushGraphicsForCompute()s first, (b) barriers
    // slot 0 -> PIXEL_SHADER_RESOURCE, (c) allocs a per-frame SRV, and (d) restores
    // RTV+viewport+scissor at its tail. No compute, no blur-temp budget — so the
    // prior compute-blur regression ("blur skipped -> hard halo / vanishing chrome")
    // cannot recur. The element content is then composited on top to stay crisp.
    float ga = a * intensity;
    if (ga > 1.0f) ga = 1.0f;
    float baseOpacity = directRenderer_->GetOpacity();

    if (ga > 0.0f && glowSize > 0.0f && baseOpacity > 0.0f) {
        // Reconstruct the (symmetric) capture rect from the UV offset managed passed
        // (uvOffset = element_origin - capture_origin, equal on all sides because
        // EffectPadding is a symmetric Thickness).
        float capX = x - uvOffsetX;
        float capY = y - uvOffsetY;
        float capW = w + 2.0f * uvOffsetX;
        float capH = h + 2.0f * uvOffsetY;
        if (uvOffsetX <= 0.0f || uvOffsetY <= 0.0f) {
            // Capture wasn't padded — degrade to the element rect (glow won't extend
            // past the edges, but no out-of-bounds sampling).
            capX = x; capY = y; capW = w; capH = h;
        }

        float dpi = directRenderer_->GetDpiScale();
        float offW = static_cast<float>(directRenderer_->GetOffscreenWidth());
        float offH = static_cast<float>(directRenderer_->GetOffscreenHeight());
        // Source the capture extent from the recorded slot-0 value (sole UV
        // authority, closes F1); offW/offH stay the divisor so uvScale/texel are
        // numerically identical to the old capW*dpi form.
        float capPxW = std::max(1.0f, (float)directRenderer_->GetOffscreenCaptureW(0));
        float capPxH = std::max(1.0f, (float)directRenderer_->GetOffscreenCaptureH(0));
        // Shared offscreen atlas is larger than the capture; map quad uv[0,1] onto the
        // capture sub-rect (uvScale) and step taps by one source pixel (texel).
        float texelU = (offW > 0.0f) ? (1.0f / offW) : (1.0f / capPxW);
        float texelV = (offH > 0.0f) ? (1.0f / offH) : (1.0f / capPxH);
        float uvScaleX = (offW > 0.0f) ? (capPxW / offW) : 1.0f;
        float uvScaleY = (offH > 0.0f) ? (capPxH / offH) : 1.0f;

        // 2D radial gaussian: K taps each direction; the per-tap step covers the glow
        // radius in K steps so the (2K+1)^2 grid spans the whole halo. sigma is in tap
        // units (K/3 => grid edge sits at ~3 sigma, a smooth radial falloff).
        float radiusPx = glowSize * dpi;
        float kf = std::ceil(radiusPx / 3.0f);     // ~one tap per 3 source px
        if (kf < 4.0f) kf = 4.0f;
        if (kf > 12.0f) kf = 12.0f;                 // cap (2K+1)^2 <= 625 taps
        float stepPx = radiusPx / kf;
        float stepU = texelU * stepPx;
        float stepV = texelV * stepPx;
        float sigma = std::max(0.5f, kf / 3.0f);

        float constants[10] = {
            r, g, b, ga,
            stepU, stepV,
            uvScaleX, uvScaleY,
            kf, sigma
        };

        // Silhouette-following glow underneath, drawn straight to the back buffer.
        DrawShaderEffectFromSource(capX, capY, capW, capH, kOuterGlowPS, constants, 10);
    }

    // Composite the original element content on top of the glow so it stays crisp.
    directRenderer_->DrawOffscreenBitmapCropped(0, x, y, w, h,
        uvOffsetX, uvOffsetY, 1.0f);
}

void D3D12RenderTarget::DrawColorMatrixEffect(float x, float y, float w, float h, const float* /*matrix*/) {
    if (!isDrawing_ || !directRenderer_) return;
    CommitDeferredState();
    // Fallback: just draw the captured content without transformation
    directRenderer_->DrawOffscreenBitmap(0, x, y, w, h, 1.0f);
}

void D3D12RenderTarget::DrawEmbossEffect(float x, float y, float w, float h,
    float /*amount*/, float /*lightDirX*/, float /*lightDirY*/, float /*relief*/)
{
    if (!isDrawing_ || !directRenderer_) return;
    CommitDeferredState();
    directRenderer_->DrawOffscreenBitmap(0, x, y, w, h, 1.0f);
}

void D3D12RenderTarget::DrawShaderEffect(float x, float y, float w, float h,
    const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
    const float* constants, uint32_t constantFloatCount)
{
    if (!isDrawing_ || !directRenderer_ || !lastEffectCaptureOk_) return;

    CommitDeferredState();
    directRenderer_->DrawCustomShaderEffect(
        0,
        x, y, w, h,
        shaderBytecode, shaderBytecodeSize,
        constants, constantFloatCount);
}

void D3D12RenderTarget::DrawShaderEffectFromSource(float x, float y, float w, float h,
    const char* hlslSource, const float* constants, uint32_t constantFloatCount)
{
    if (!isDrawing_ || !directRenderer_ || !lastEffectCaptureOk_ || !hlslSource) return;

    // FNV-1a hash of the source keys the compiled-bytecode cache.
    uint64_t hash = 1469598103934665603ull;
    for (const char* p = hlslSource; *p; ++p) {
        hash ^= static_cast<uint8_t>(*p);
        hash *= 1099511628211ull;
    }

    ComPtr<ID3DBlob> psBlob;
    auto it = customShaderHlslCache_.find(hash);
    if (it != customShaderHlslCache_.end()) {
        psBlob = it->second;
    } else {
        UINT flags = 0;
#ifdef _DEBUG
        flags = D3DCOMPILE_DEBUG | D3DCOMPILE_SKIP_OPTIMIZATION;
#else
        flags = D3DCOMPILE_OPTIMIZATION_LEVEL3;
#endif
        ComPtr<ID3DBlob> errors;
        HRESULT hr = D3DCompile(hlslSource, std::strlen(hlslSource),
                                "jalium_shader_effect.ps.hlsl", nullptr, nullptr,
                                "main", "ps_5_1", flags, 0,
                                psBlob.GetAddressOf(), errors.GetAddressOf());
        if (FAILED(hr)) {
            if (errors) {
                OutputDebugStringA("[Jalium shader effect] HLSL compile error: ");
                OutputDebugStringA(static_cast<const char*>(errors->GetBufferPointer()));
                OutputDebugStringA("\n");
            }
            // Fallback: draw the captured content unmodified (matches the DXBC
            // path's compile-failure behaviour).
            CommitDeferredState();
            directRenderer_->DrawCustomShaderEffect(0, x, y, w, h, nullptr, 0, constants, constantFloatCount);
            return;
        }
        customShaderHlslCache_.emplace(hash, psBlob);
    }

    CommitDeferredState();
    directRenderer_->DrawCustomShaderEffect(
        0, x, y, w, h,
        static_cast<const uint8_t*>(psBlob->GetBufferPointer()),
        static_cast<uint32_t>(psBlob->GetBufferSize()),
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

// ============================================================================
// Off-thread animation probe (Increment 1 — architecture hard gate)
// ============================================================================
//
// Proves that an IDCompositionAnimation bound to a child visual's offset is
// driven autonomously by DWM at vblank with ZERO app-side Present/Commit — the
// WPF independent-animation model. Content is a one-time-cleared mini composition
// swap chain; only the visual's offset animates. Once Commit() returns, the app
// can go fully idle and the block keeps sliding (verify with PresentMon: app
// present rate ≈ 0). If the block does NOT keep moving while the app is idle on
// the target iGPU, the whole off-thread direction is void — that is exactly what
// this probe exists to measure cheaply.

JaliumResult D3D12RenderTarget::CreateAnimProbe(
    int32_t x, int32_t y, int32_t width, int32_t height,
    float travelPx, float periodSec, uint32_t colorArgb, int32_t vertical,
    void** visualOut)
{
    if (!isComposition_ || !dcompDevice_ || !visualOut) return JALIUM_ERROR_INVALID_STATE;
    *visualOut = nullptr;
    if (width <= 0 || height <= 0) return JALIUM_ERROR_INVALID_ARGUMENT;

    auto* device = backend_->GetDevice();
    auto* queue  = backend_->GetCommandQueue();
    auto* factory = backend_->GetDXGIFactory();
    if (!device || !queue || !factory) return JALIUM_ERROR_INVALID_STATE;

    AnimProbeEntry entry;

    // ── 1. Mini composition swap chain holding the static solid-color content ──
    DXGI_SWAP_CHAIN_DESC1 desc = {};
    desc.Width  = static_cast<UINT>(width);
    desc.Height = static_cast<UINT>(height);
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;                                  // FLIP_SEQUENTIAL requires >= 2
    desc.SwapEffect  = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    desc.Scaling     = DXGI_SCALING_STRETCH;
    desc.AlphaMode   = DXGI_ALPHA_MODE_PREMULTIPLIED;      // DComp composites premultiplied
    desc.Flags       = 0;                                  // static content → no waitable

    HRESULT hr = factory->CreateSwapChainForComposition(queue, &desc, nullptr, &entry.contentSwapChain);
    if (FAILED(hr)) return JALIUM_ERROR_INVALID_STATE;

    // ── 2. Clear backbuffer 0 to the (premultiplied) probe color, once ──
    ComPtr<ID3D12Resource> backBuffer;
    hr = entry.contentSwapChain->GetBuffer(0, IID_PPV_ARGS(&backBuffer));
    if (FAILED(hr)) return JALIUM_ERROR_INVALID_STATE;

    D3D12_DESCRIPTOR_HEAP_DESC rtvHeapDesc = {};
    rtvHeapDesc.NumDescriptors = 1;
    rtvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
    ComPtr<ID3D12DescriptorHeap> rtvHeap;
    if (FAILED(device->CreateDescriptorHeap(&rtvHeapDesc, IID_PPV_ARGS(&rtvHeap))))
        return JALIUM_ERROR_INVALID_STATE;
    D3D12_CPU_DESCRIPTOR_HANDLE rtv = rtvHeap->GetCPUDescriptorHandleForHeapStart();
    device->CreateRenderTargetView(backBuffer.Get(), nullptr, rtv);

    ComPtr<ID3D12CommandAllocator> alloc;
    if (FAILED(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&alloc))))
        return JALIUM_ERROR_INVALID_STATE;
    ComPtr<ID3D12GraphicsCommandList> cmd;
    if (FAILED(device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, alloc.Get(), nullptr, IID_PPV_ARGS(&cmd))))
        return JALIUM_ERROR_INVALID_STATE;

    auto transition = [&](D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after) {
        D3D12_RESOURCE_BARRIER barrier = {};
        barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barrier.Transition.pResource   = backBuffer.Get();
        barrier.Transition.Subresource = 0;
        barrier.Transition.StateBefore = before;
        barrier.Transition.StateAfter  = after;
        cmd->ResourceBarrier(1, &barrier);
    };
    transition(D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);

    const float a = ((colorArgb >> 24) & 0xFF) / 255.0f;
    const float r = ((colorArgb >> 16) & 0xFF) / 255.0f;
    const float g = ((colorArgb >>  8) & 0xFF) / 255.0f;
    const float b = ((colorArgb >>  0) & 0xFF) / 255.0f;
    const float clear[4] = { r * a, g * a, b * a, a };    // premultiply for PREMULTIPLIED alpha mode
    cmd->ClearRenderTargetView(rtv, clear, 0, nullptr);

    transition(D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
    cmd->Close();
    ID3D12CommandList* lists[] = { cmd.Get() };
    queue->ExecuteCommandLists(1, lists);

    // Synchronous one-shot fence: this runs at setup time (not per frame), so a
    // blocking wait here is fine and keeps the probe self-contained / decoupled
    // from the render target's per-frame fence.
    ComPtr<ID3D12Fence> fence;
    if (FAILED(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence))))
        return JALIUM_ERROR_INVALID_STATE;
    HANDLE ev = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    queue->Signal(fence.Get(), 1);
    if (fence->GetCompletedValue() < 1 && ev) {
        fence->SetEventOnCompletion(1, ev);
        WaitForSingleObject(ev, 2000);
    }
    if (ev) CloseHandle(ev);

    entry.contentSwapChain->Present(0, 0);                // single present; content is static thereafter

    // ── 3. Child visual hosting the content, inserted ABOVE the main swap chain ──
    if (FAILED(dcompDevice_->CreateVisual(&entry.visual))) return JALIUM_ERROR_INVALID_STATE;
    if (FAILED(entry.visual->SetContent(entry.contentSwapChain.Get()))) return JALIUM_ERROR_INVALID_STATE;

    // ── 4. Autonomous offset animation. AddSinusoidal loops forever with no End/
    //      Repeat bookkeeping, so it is robust even if the exact phase/frequency
    //      units differ from expectation — the block still visibly oscillates,
    //      which is all the gate needs to prove. Increment 3 will replace this with
    //      a piecewise-linear curve matching the bar's exact motion. ──
    if (FAILED(dcompDevice_->CreateAnimation(&entry.anim))) return JALIUM_ERROR_INVALID_STATE;
    const float center    = (vertical ? static_cast<float>(y) : static_cast<float>(x)) + travelPx * 0.5f;
    const float amplitude = travelPx * 0.5f;
    const float frequency = (periodSec > 0.0f) ? static_cast<float>(1.0 / (2.0 * periodSec)) : 0.5f; // there-and-back per 2*period
    entry.anim->AddSinusoidal(0.0, /*bias*/ center, /*amplitude*/ amplitude, /*frequency*/ frequency, /*phase*/ 0.0f);

    if (vertical) {
        entry.visual->SetOffsetX(static_cast<float>(x));
        entry.visual->SetOffsetY(entry.anim.Get());
    } else {
        entry.visual->SetOffsetY(static_cast<float>(y));
        entry.visual->SetOffsetX(entry.anim.Get());
    }

    if (FAILED(dcompVisual_->AddVisual(entry.visual.Get(), TRUE, dcompSwapChainVisual_.Get())))
        return JALIUM_ERROR_INVALID_STATE;

    // ── 5. Single Commit. DWM owns the animation from here; the app need never
    //      present or commit again for the block to keep moving. ──
    if (FAILED(dcompDevice_->Commit())) return JALIUM_ERROR_INVALID_STATE;

    IDCompositionVisual* raw = entry.visual.Get();
    animProbes_[raw] = std::move(entry);
    *visualOut = raw;
    return JALIUM_OK;
}

JaliumResult D3D12RenderTarget::DestroyAnimProbe(void* visual) {
    if (!isComposition_ || !dcompDevice_ || !visual) return JALIUM_ERROR_INVALID_STATE;
    auto* v = static_cast<IDCompositionVisual*>(visual);
    auto it = animProbes_.find(v);
    if (it == animProbes_.end()) return JALIUM_ERROR_INVALID_STATE;

    dcompVisual_->RemoveVisual(v);
    animProbes_.erase(it);                                 // ComPtr members release here
    dcompDevice_->Commit();
    return JALIUM_OK;
}

} // namespace jalium
