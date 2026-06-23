#include "d3d12_direct_renderer.h"
#include "d3d12_retained_layer.h"
#include "d3d12_vello.h"
#include "d3d12_shader_source.h"
#include "d3d12_shader_bytecode.h"
#include <d3dcompiler.h>
#include <cassert>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <vector>
#pragma comment(lib, "d3dcompiler.lib")

namespace jalium {

// ── QPC helpers for frame-pacing instrumentation ──────────────────────────
// Cached on first use. QueryPerformanceFrequency is documented as fixed for
// the lifetime of the system, so a single read is sufficient.
namespace {
inline LARGE_INTEGER QpcNow() {
    LARGE_INTEGER t;
    QueryPerformanceCounter(&t);
    return t;
}
inline int64_t QpcFrequencyHz() {
    static int64_t s_freq = []() {
        LARGE_INTEGER f;
        QueryPerformanceFrequency(&f);
        return f.QuadPart;
    }();
    return s_freq;
}
inline uint64_t QpcDiffNs(const LARGE_INTEGER& t0, const LARGE_INTEGER& t1) {
    int64_t delta = t1.QuadPart - t0.QuadPart;
    if (delta <= 0) return 0;
    // (delta * 1e9) / freq  — split to avoid overflow on long sessions.
    int64_t freq = QpcFrequencyHz();
    if (freq <= 0) return 0;
    int64_t whole = (delta / freq) * 1'000'000'000LL;
    int64_t frac  = ((delta % freq) * 1'000'000'000LL) / freq;
    return static_cast<uint64_t>(whole + frac);
}
} // namespace

// ── Inline helpers replacing CD3DX12_* (d3dx12.h is a minimal subset) ──

static D3D12_HEAP_PROPERTIES MakeHeapProps(D3D12_HEAP_TYPE type) {
    D3D12_HEAP_PROPERTIES hp = {};
    hp.Type = type;
    return hp;
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

static void LogDeviceRemovedReason(const char* stage, ID3D12Device* device, HRESULT hr)
{
    char buffer[256] = {};
    HRESULT removedReason = device ? device->GetDeviceRemovedReason() : E_FAIL;
    sprintf_s(buffer,
        "[D3D12DirectRenderer] %s failed hr=0x%08X removedReason=0x%08X\n",
        stage ? stage : "D3D12",
        static_cast<unsigned int>(hr),
        static_cast<unsigned int>(removedReason));
    OutputDebugStringA(buffer);

    if (!device) {
        return;
    }

    ComPtr<ID3D12DeviceRemovedExtendedData> dred;
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&dred)))) {
        return;
    }

    D3D12_DRED_AUTO_BREADCRUMBS_OUTPUT breadcrumbs = {};
    if (SUCCEEDED(dred->GetAutoBreadcrumbsOutput(&breadcrumbs))) {
        const char* opNames[] = {
            "SetMarker","BeginEvent","EndEvent","DrawInstanced","DrawIndexedInstanced",
            "ExecuteIndirect","Dispatch","CopyBufferRegion","CopyTextureRegion","CopyResource",
            "CopyTiles","ResolveSubresource","ClearRenderTargetView","ClearUnorderedAccessView",
            "ClearDepthStencilView","ResourceBarrier","ExecuteBundle","Present","ResolveQueryData",
            "BeginSubmission","EndSubmission","DecodeFrame","ProcessFrames","AtomicCopyBuffer",
            "ResolveSubresourceRegion","WriteBufferImmediate","DecodeFrame1","SetProtectedResourceSession",
            "DecodeFrame2","ProcessFrames1","BuildRaytracingAccelerationStructure",
            "EmitRaytracingAccelerationStructurePostbuildInfo","CopyRaytracingAccelerationStructure",
            "DispatchRays","InitializeMetaCommand","ExecuteMetaCommand","EstimateMotion",
            "ResolveMotionVectorHeap","SetPipelineState1","InitializeExtensionCommand","ExecuteExtensionCommand"
        };
        auto* node = breadcrumbs.pHeadAutoBreadcrumbNode;
        while (node) {
            sprintf_s(buffer,
                "[DRED] CL=%p queue=%p count=%u last=%u cmd=\"%S\"\n",
                node->pCommandListDebugNameW ? (void*)1 : nullptr,
                node->pCommandQueueDebugNameW ? (void*)1 : nullptr,
                node->BreadcrumbCount,
                node->pLastBreadcrumbValue ? *node->pLastBreadcrumbValue : 0,
                node->pCommandListDebugNameW ? node->pCommandListDebugNameW : L"(anon)");
            OutputDebugStringA(buffer);
            // Print the last few breadcrumb operations
            if (node->pCommandHistory && node->BreadcrumbCount > 0) {
                uint32_t lastCompleted = node->pLastBreadcrumbValue ? *node->pLastBreadcrumbValue : 0;
                uint32_t start = lastCompleted > 3 ? lastCompleted - 3 : 0;
                uint32_t end = lastCompleted + 4 < node->BreadcrumbCount ? lastCompleted + 4 : node->BreadcrumbCount;
                for (uint32_t j = start; j < end; j++) {
                    uint32_t opIdx = (uint32_t)node->pCommandHistory[j];
                    const char* opName = (opIdx < _countof(opNames)) ? opNames[opIdx] : "Unknown";
                    sprintf_s(buffer, "[DRED]   [%u] %s%s\n", j, opName,
                              (j == lastCompleted) ? " <-- LAST COMPLETED" : (j == lastCompleted + 1) ? " <-- CRASHED HERE?" : "");
                    OutputDebugStringA(buffer);
                }
            }
            node = node->pNext;
        }
    }

    D3D12_DRED_PAGE_FAULT_OUTPUT pageFault = {};
    if (SUCCEEDED(dred->GetPageFaultAllocationOutput(&pageFault))) {
        sprintf_s(buffer,
            "[D3D12DirectRenderer] DRED pageFaultVA=0x%llX existing=%p recentFreed=%p\n",
            static_cast<unsigned long long>(pageFault.PageFaultVA),
            pageFault.pHeadExistingAllocationNode,
            pageFault.pHeadRecentFreedAllocationNode);
        OutputDebugStringA(buffer);
    }

    ComPtr<ID3D12InfoQueue> infoQueue;
    if (SUCCEEDED(device->QueryInterface(IID_PPV_ARGS(&infoQueue)))) {
        UINT64 messageCount = infoQueue->GetNumStoredMessagesAllowedByRetrievalFilter();
        UINT64 start = messageCount > 16 ? messageCount - 16 : 0;
        for (UINT64 i = start; i < messageCount; ++i) {
            SIZE_T messageLength = 0;
            if (FAILED(infoQueue->GetMessage(i, nullptr, &messageLength)) || messageLength == 0) {
                continue;
            }

            std::vector<uint8_t> messageBytes(messageLength);
            auto* message = reinterpret_cast<D3D12_MESSAGE*>(messageBytes.data());
            if (FAILED(infoQueue->GetMessage(i, message, &messageLength))) {
                continue;
            }

            sprintf_s(buffer,
                "[D3D12InfoQueue] severity=%d id=%d: ",
                static_cast<int>(message->Severity),
                static_cast<int>(message->ID));
            OutputDebugStringA(buffer);
            OutputDebugStringA(message->pDescription ? message->pDescription : "(null)");
            OutputDebugStringA("\n");
        }
    }
}

static uint64_t HashShaderBytecode(const uint8_t* data, uint32_t size)
{
    uint64_t hash = 1469598103934665603ull;
    for (uint32_t i = 0; i < size; ++i) {
        hash ^= data[i];
        hash *= 1099511628211ull;
    }
    return hash;
}

// ============================================================================
// Construction / Destruction
// ============================================================================

D3D12DirectRenderer::D3D12DirectRenderer(D3D12Backend* backend)
    : backend_(backend)
    , device_(backend ? backend->GetDevice() : nullptr)
{
}

D3D12DirectRenderer::~D3D12DirectRenderer()
{
    Shutdown();
}

// ============================================================================
// Initialization
// ============================================================================

bool D3D12DirectRenderer::Initialize(IDXGISwapChain3* swapChain, UINT frameCount)
{
    if (!device_ || !backend_ || !swapChain || frameCount == 0 || frameCount > kMaxFrames)
        return false;

    swapChain_ = swapChain;
    frameCount_ = frameCount;

    // Query actual swap chain format so PSOs match exactly
    DXGI_SWAP_CHAIN_DESC scDesc = {};
    if (SUCCEEDED(swapChain_->GetDesc(&scDesc))) {
        swapChainFormat_ = scDesc.BufferDesc.Format;
    } else {
        swapChainFormat_ = DXGI_FORMAT_R8G8B8A8_UNORM; // safe default
    }

    // Get back buffer references
    for (UINT i = 0; i < frameCount_; i++) {
        HRESULT hr = swapChain_->GetBuffer(i, IID_PPV_ARGS(&renderTargets_[i]));
        if (FAILED(hr)) return false;
        wchar_t dbgNm[40]; swprintf_s(dbgNm, L"JaliumBackBuffer%u", i); renderTargets_[i]->SetName(dbgNm);  // [JALIUM-921 diag] name so #921 points at the culprit
    }

    // RTV heap
    // Layout: [0..frameCount_-1] back buffers, [frameCount_..frameCount_+1] offscreen, [frameCount_+2] MSAA color
    {
        D3D12_DESCRIPTOR_HEAP_DESC desc = {};
        desc.NumDescriptors = frameCount_ + 3;
        desc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        if (FAILED(device_->CreateDescriptorHeap(&desc, IID_PPV_ARGS(&rtvHeap_))))
            return false;
        rtvDescriptorSize_ = device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);

        // Create RTVs matching the swap chain format (sRGB passthrough — no gamma conversion).
        D3D12_RENDER_TARGET_VIEW_DESC rtvDesc = {};
        rtvDesc.Format = swapChainFormat_;
        rtvDesc.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2D;
        auto handle = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
        for (UINT i = 0; i < frameCount_; i++) {
            device_->CreateRenderTargetView(renderTargets_[i].Get(), &rtvDesc, handle);
            handle.ptr += rtvDescriptorSize_;
        }
    }

    // SRV heap (shader-visible, for StructuredBuffer binding)
    {
        D3D12_DESCRIPTOR_HEAP_DESC desc = {};
        desc.NumDescriptors = kMaxSrvDescriptors;
        desc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        desc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        if (FAILED(device_->CreateDescriptorHeap(&desc, IID_PPV_ARGS(&srvHeap_))))
            return false;
        srvDescriptorSize_ = device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
    }

    // Fence
    {
        if (FAILED(device_->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence_))))
            return false;
        fenceEvent_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!fenceEvent_) return false;
    }

    // Frame constants upload buffer (persistently mapped)
    {
        auto heapProps = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
        auto bufDesc = MakeBufferDesc(256); // min CBV size
        if (FAILED(device_->CreateCommittedResource(
                &heapProps, D3D12_HEAP_FLAG_NONE, &bufDesc,
                D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
                IID_PPV_ARGS(&frameConstantsBuffer_))))
            return false;
        frameConstantsBuffer_->SetName(L"JaliumFrameConstants");  // [JALIUM-921 diag]
        frameConstantsBuffer_->Map(0, nullptr, &frameConstantsMapped_);
    }

    if (!CreateFrameResources()) return false;
    if (!CreateRootSignature()) return false;
    if (!CreatePSOs()) return false;
    if (!CreateBlurResources()) {
        OutputDebugStringA("[D3D12DirectRenderer] Blur resources init failed (non-fatal)\n");
        // Non-fatal: blur will fall back to semi-transparent overlay
    }
    if (!CreateStencilPathResources()) {
        OutputDebugStringA("[D3D12DirectRenderer] Stencil path init failed (non-fatal, falls back to Impeller scanline)\n");
        // Non-fatal: AddStencilPath returns false → D3D12RenderTarget routes to ImpellerEngine.
    }

    // Initialize glyph atlas for text rendering
    auto dwriteFactory = backend_->GetDWriteFactory();
    if (dwriteFactory) {
        glyphAtlas_ = std::make_unique<D3D12GlyphAtlas>(device_, dwriteFactory, backend_);
        if (!glyphAtlas_->Initialize()) {
            OutputDebugStringA("[D3D12DirectRenderer] GlyphAtlas init failed\n");
            glyphAtlas_.reset();
        }
    }

    // Vello GPU 路径渲染器改为懒创建（见 EnsureVelloRenderer）。这里不再
    // eager 构造：默认引擎是 Impeller(velloEnabled_=false),整条 Vello 子系统
    // (~7 计算 PSO + 每帧描述符堆 + 缓冲 + 驱动 compute 上下文)本就用不到,
    // eager 创建纯属浪费 GPU/驱动内存与启动期 PSO 编译。active engine 真是
    // Vello 时,首帧 BeginFrame 会按需 EnsureVelloRenderer()。

    // ── GPU timestamp infrastructure ────────────────────────────────────
    // One TIMESTAMP query heap across all frames (frameCount × slots-per-
    // frame) plus per-frame readback buffer (uint64_t × slots-per-frame).
    // Failure here is non-fatal: timingSupported_ stays false and the
    // breakdown remains zero-valued; the renderer continues working.
    if (auto* cmdQueue = backend_->GetCommandQueue()) {
        if (SUCCEEDED(cmdQueue->GetTimestampFrequency(&timestampFrequency_)) && timestampFrequency_ > 0) {
            D3D12_QUERY_HEAP_DESC qhDesc = {};
            qhDesc.Type = D3D12_QUERY_HEAP_TYPE_TIMESTAMP;
            qhDesc.Count = kMaxTimingSlotsPerFrame * frameCount_;
            HRESULT hr = device_->CreateQueryHeap(&qhDesc, IID_PPV_ARGS(&timingQueryHeap_));
            if (SUCCEEDED(hr)) {
                bool allReadbackOk = true;
                for (UINT i = 0; i < frameCount_; ++i) {
                    auto rbHeapProps = MakeHeapProps(D3D12_HEAP_TYPE_READBACK);
                    auto rbBufDesc = MakeBufferDesc(sizeof(uint64_t) * kMaxTimingSlotsPerFrame);
                    if (FAILED(device_->CreateCommittedResource(
                            &rbHeapProps, D3D12_HEAP_FLAG_NONE, &rbBufDesc,
                            D3D12_RESOURCE_STATE_COPY_DEST, nullptr,
                            IID_PPV_ARGS(&timing_[i].readback)))) {
                        allReadbackOk = false;
                        break;
                    }
                    timing_[i].readback->SetName(L"JaliumTimingReadback");  // [JALIUM-921 diag]
                    timing_[i].spanCategories.reserve(kMaxTimingSlotsPerFrame);
                }
                timingSupported_ = allReadbackOk;
            }
        }
    }

    initialized_ = true;
    return true;
}

bool D3D12DirectRenderer::EnsureVelloRenderer() {
    if (velloRenderer_) return true;          // 已创建
    if (!velloEnabled_) return false;          // active engine 非 Vello → 不创建
    if (!device_) return false;

    auto vr = std::make_unique<D3D12VelloRenderer>(device_, nullptr);
    if (!vr->Initialize()) {
        OutputDebugStringA("[D3D12DirectRenderer] Vello lazy init failed (non-fatal, falling back to CPU triangulation)\n");
        return false;                          // 保持 velloRenderer_ 为空,后续不再重试本帧
    }
    vr->SetGPUPipeline(true);
    velloRenderer_ = std::move(vr);
    return true;
}

void D3D12DirectRenderer::Shutdown()
{
    if (!initialized_) return;

    // Wait for GPU idle
    if (fence_ && fenceEvent_) {
        auto queue = backend_->GetCommandQueue();
        if (queue) {
            uint64_t fv = nextFenceValue_++;
            queue->Signal(fence_.Get(), fv);
            backend_->NoteSubmittedFenceValue(fv);
            if (fence_->GetCompletedValue() < fv) {
                fence_->SetEventOnCompletion(fv, fenceEvent_);
                WaitForSingleObject(fenceEvent_, 5000);
            }
            // GPU is now idle — flush anything else parked in the graveyard.
            backend_->ReclaimRetiredGpuResources(fence_->GetCompletedValue());
        }
    }

    if (frameConstantsMapped_) {
        frameConstantsBuffer_->Unmap(0, nullptr);
        frameConstantsMapped_ = nullptr;
    }

    for (UINT i = 0; i < frameCount_; i++) {
        if (frames_[i].instanceMappedPtr) {
            frames_[i].instanceUploadBuffer->Unmap(0, nullptr);
            frames_[i].instanceMappedPtr = nullptr;
        }
        if (frames_[i].constantsMappedPtr) {
            frames_[i].constantsBuffer->Unmap(0, nullptr);
            frames_[i].constantsMappedPtr = nullptr;
        }
        // GPU is idle (waited above) — buffers retired by mid-frame growth in the
        // last submitted frame are no longer referenced and can be released now.
        frames_[i].retiredInstanceBuffers.clear();
        frames_[i].retiredDescriptorHeaps.clear();
    }

    if (fenceEvent_) {
        CloseHandle(fenceEvent_);
        fenceEvent_ = nullptr;
    }

    velloRenderer_.reset();

    initialized_ = false;
}

// ============================================================================
// Resize — update back buffer references and RTVs after swap chain resize
// ============================================================================

void D3D12DirectRenderer::ReleaseBackBufferReferences()
{
    // [JALIUM-921 diagnostic] If the list is open here, releasing the back-buffer
    // references it still holds is part of the #921 setup.
    if (cmdListRecording_.load(std::memory_order_acquire)) {
        char buf[256];
        sprintf_s(buf, "[JALIUM-921] *** ReleaseBackBufferReferences while commandList OPEN! curTid=%lu listOwnerTid=%lu inFrame_=%d ***\n",
            GetCurrentThreadId(),
            cmdListOwnerThread_.load(std::memory_order_acquire),
            inFrame_ ? 1 : 0);
        OutputDebugStringA(buf);
    }

    // Wait for GPU idle before releasing
    if (fence_ && fenceEvent_) {
        auto queue = backend_->GetCommandQueue();
        if (queue) {
            uint64_t fv = nextFenceValue_++;
            queue->Signal(fence_.Get(), fv);
            backend_->NoteSubmittedFenceValue(fv);
            if (fence_->GetCompletedValue() < fv) {
                fence_->SetEventOnCompletion(fv, fenceEvent_);
                WaitForSingleObject(fenceEvent_, 5000);
            }
            backend_->ReclaimRetiredGpuResources(fence_->GetCompletedValue());
        }
    }

    for (UINT i = 0; i < frameCount_; i++) {
        renderTargets_[i].Reset();
    }
}

bool D3D12DirectRenderer::OnResize(UINT newWidth, UINT newHeight)
{
    if (!initialized_ || !device_ || !swapChain_)
        return false;

    // [JALIUM-921 diagnostic] SMOKING GUN: we are about to Reset the back buffers /
    // effect textures the open command list may still reference. If the list is open
    // here, the next Close() will fire #921 OBJECT_DELETED_WHILE_STILL_IN_USE.
    if (cmdListRecording_.load(std::memory_order_acquire)) {
        char buf[256];
        sprintf_s(buf, "[JALIUM-921] *** OnResize Reset while commandList OPEN! curTid=%lu listOwnerTid=%lu inFrame_=%d newSize=%ux%u ***\n",
            GetCurrentThreadId(),
            cmdListOwnerThread_.load(std::memory_order_acquire),
            inFrame_ ? 1 : 0, newWidth, newHeight);
        OutputDebugStringA(buf);
    }

    // Back buffer references should already be released via ReleaseBackBufferReferences().
    // Defensive: release again in case caller didn't call it.
    for (UINT i = 0; i < frameCount_; i++) {
        renderTargets_[i].Reset();
    }

    // Acquire new back buffer references from the (already resized) swap chain
    for (UINT i = 0; i < frameCount_; i++) {
        HRESULT hr = swapChain_->GetBuffer(i, IID_PPV_ARGS(&renderTargets_[i]));
        if (FAILED(hr)) return false;
        wchar_t dbgNm[40]; swprintf_s(dbgNm, L"JaliumBackBuffer%u", i); renderTargets_[i]->SetName(dbgNm);  // [JALIUM-921 diag]
    }

    // Recreate RTVs for the new back buffers
    {
        D3D12_RENDER_TARGET_VIEW_DESC rtvDesc = {};
        rtvDesc.Format = swapChainFormat_;
        rtvDesc.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2D;

        auto handle = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
        for (UINT i = 0; i < frameCount_; i++) {
            device_->CreateRenderTargetView(renderTargets_[i].Get(), &rtvDesc, handle);
            handle.ptr += rtvDescriptorSize_;
        }
    }

    // Invalidate cached blur temp textures — they were sized for the old dimensions
    blurTempA_.Reset();
    blurTempB_.Reset();
    blurTempW_ = 0;
    blurTempH_ = 0;
    blurTempAState_ = D3D12_RESOURCE_STATE_COMMON;
    blurTempBState_ = D3D12_RESOURCE_STATE_COMMON;

    // Invalidate snapshot and offscreen resources
    snapshotTexture_.Reset();
    snapshotW_ = 0;
    snapshotH_ = 0;
    snapshotValid_ = false;
    snapshotUsedThisFrame_ = false;
    snapshotState_ = D3D12_RESOURCE_STATE_COMMON;

    offscreenRT_[0].Reset();
    offscreenRT_[1].Reset();
    offscreenRTState_[0] = D3D12_RESOURCE_STATE_COMMON;
    offscreenRTState_[1] = D3D12_RESOURCE_STATE_COMMON;
    offscreenW_ = 0;
    offscreenH_ = 0;
    offscreenCaptureValid_[0] = false;
    offscreenCaptureValid_[1] = false;
    offscreenResourcesUsedThisFrame_ = false;

    blurTempsUsedThisFrame_ = false;

    // Drop the MSAA color buffer; BeginFrame will allocate a new one at the
    // new size on the next call.
    msaaColorBuffer_.Reset();
    msaaWidth_ = 0;
    msaaHeight_ = 0;

    // Update viewport dimensions (will be applied in next BeginFrame)
    viewportWidth_ = newWidth;
    viewportHeight_ = newHeight;

    return true;
}

// ============================================================================
// Resource Creation
// ============================================================================

bool D3D12DirectRenderer::CreateFrameResources()
{
    for (UINT i = 0; i < frameCount_; i++) {
        auto& fr = frames_[i];

        // Command allocator per frame
        if (FAILED(device_->CreateCommandAllocator(
                D3D12_COMMAND_LIST_TYPE_DIRECT,
                IID_PPV_ARGS(&fr.commandAllocator))))
            return false;

        // Instance upload buffer is allocated lazily on first use and grows as
        // needed — see EnsureFrameInstanceCapacity.  A bare AOT window draws a
        // few KB of instances; preallocating the historical 48 MB up front
        // wasted ~144 MB across the three in-flight frames.
        fr.instanceCapacity = 0;

        // Per-frame constants ring buffer — each FlushGraphicsForCompute gets its own
        // 256-byte aligned slot, so offscreen and main-RT draws see correct constants.
        auto cbHeapProps = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
        auto cbBufDesc = MakeBufferDesc(kConstantsRingSize);
        if (FAILED(device_->CreateCommittedResource(
                &cbHeapProps, D3D12_HEAP_FLAG_NONE, &cbBufDesc,
                D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
                IID_PPV_ARGS(&fr.constantsBuffer))))
            return false;
        fr.constantsBuffer->SetName(L"JaliumFrameConstantsRing");  // [JALIUM-921 diag]
        fr.constantsBuffer->Map(0, nullptr, &fr.constantsMappedPtr);
    }

    // Partition SRV heap into per-frame regions to prevent cross-frame descriptor races.
    // Reserve 16 slots at the end for blur descriptors.
    static constexpr UINT kBlurReservedSlots = 16;
    frameSrvRegionSize_ = (kMaxSrvDescriptors - kBlurReservedSlots) / frameCount_;

    // Shared command list (created closed, reset per frame)
    if (FAILED(device_->CreateCommandList(
            0, D3D12_COMMAND_LIST_TYPE_DIRECT,
            frames_[0].commandAllocator.Get(), nullptr,
            IID_PPV_ARGS(&commandList_))))
        return false;
    commandList_->SetName(L"JaliumMainCmdList");  // [JALIUM-921 diag]
    commandList_->Close();

    return true;
}

bool D3D12DirectRenderer::EnsureFrameInstanceCapacity(FrameResources& fr, size_t requiredBytes)
{
    if (fr.instanceUploadBuffer && fr.instanceMappedPtr && fr.instanceCapacity >= requiredBytes) {
        return true;
    }

    // Mid-frame growth is supported.  Earlier draws on the open command list may
    // hold descriptor-table or VBV references to the existing instanceUploadBuffer
    // resource; releasing it now would trigger
    // D3D12 ERROR #921 OBJECT_DELETED_WHILE_STILL_IN_USE on commandList_->Close().
    // Instead, the old buffer is parked on fr.retiredInstanceBuffers, which keeps
    // its refcount alive until BeginFrame clears the list after this slot's fence
    // completes.  uploadBufferOffset_ is intentionally not reset: data the GPU
    // still needs from the old buffer remains there (the parked ComPtr keeps the
    // resource resident), while new writes from the rest of this frame land in the
    // new buffer at offset uploadBufferOffset_+ — and the SRVs created for those
    // new writes correctly point to the new resource.
    size_t newCap = std::max<size_t>(fr.instanceCapacity, kInitialInstanceCapacityBytes);
    while (newCap < requiredBytes) {
        newCap *= 2;
    }

    if (fr.instanceUploadBuffer) {
        if (fr.instanceMappedPtr) {
            fr.instanceUploadBuffer->Unmap(0, nullptr);
            fr.instanceMappedPtr = nullptr;
        }
        fr.retiredInstanceBuffers.push_back(std::move(fr.instanceUploadBuffer));
    }

    auto heapProps = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
    auto bufDesc = MakeBufferDesc(newCap);
    if (FAILED(device_->CreateCommittedResource(
            &heapProps, D3D12_HEAP_FLAG_NONE, &bufDesc,
            D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
            IID_PPV_ARGS(&fr.instanceUploadBuffer)))) {
        fr.instanceCapacity = 0;
        return false;
    }
    fr.instanceUploadBuffer->SetName(L"JaliumInstanceUpload");  // [JALIUM-921 diag]
    fr.instanceUploadBuffer->Map(0, nullptr, &fr.instanceMappedPtr);
    if (!fr.instanceMappedPtr) {
        // Map fails on a removed device; a null mapped pointer would turn the
        // memcpys in UploadInstances into an AV. Treat as growth failure.
        fr.instanceUploadBuffer.Reset();
        fr.instanceCapacity = 0;
        return false;
    }
    fr.instanceCapacity = newCap;
    return true;
}

bool D3D12DirectRenderer::CreateRootSignature()
{
    // Root signature layout (version 1.0 for compatibility):
    //   [0] Root CBV b0 — FrameConstants (screenSize, invScreenSize)
    //   [1] Descriptor Table — SRV t0-t1 (instances + glyph atlas)
    //   Static sampler s0 — linear clamp (general)
    //   Static sampler s1 — point clamp (ClearType text)

    D3D12_DESCRIPTOR_RANGE srvRange = {};
    srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
    srvRange.NumDescriptors = 2; // t0 (instances) + t1 (glyph atlas)
    srvRange.BaseShaderRegister = 0;
    srvRange.RegisterSpace = 0;
    srvRange.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

    D3D12_ROOT_PARAMETER params[4] = {};
    // [0] Root CBV for frame constants (b0)
    params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
    params[0].Descriptor.ShaderRegister = 0;
    params[0].Descriptor.RegisterSpace = 0;
    params[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // [1] Descriptor table (SRV t0-t1)
    params[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    params[1].DescriptorTable.NumDescriptorRanges = 1;
    params[1].DescriptorTable.pDescriptorRanges = &srvRange;
    params[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // [2] Root 32-bit constant — instance base offset (b1).
    //     Custom-effect VS / liquid-glass also reuse b1 with their own cbuffer
    //     layouts; the 8-dword window is large enough for the largest of those
    //     (geom = float4 + viewport float2 + pad float2).
    params[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
    params[2].Constants.ShaderRegister = 1;
    params[2].Constants.RegisterSpace = 0;
    params[2].Constants.Num32BitValues = 8;
    params[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // [3] Root 32-bit constant — rounded-rect clip data (b2).
    //     Layout (12 dwords / 48 bytes):
    //         uint  hasRoundedClip
    //         uint3 _pad
    //         float4 roundedClipRect    // (left, top, right, bottom) in physical pixels
    //         float4 roundedClipRadius  // (rx, ry, _, _) in physical pixels
    //     Read only by the four batched pixel shaders (sdf_rect / bitmap_quad /
    //     bitmap_text / triangle); custom-effect / liquid-glass shaders simply
    //     don't declare the cbuffer and the data sits unused.
    params[3].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
    params[3].Constants.ShaderRegister = 2;
    params[3].Constants.RegisterSpace = 0;
    params[3].Constants.Num32BitValues = 12;
    params[3].ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

    // Static samplers
    // s0 — bilinear clamp (Linear / LowQuality bitmap path; general texture sampling)
    // s1 — point clamp (NearestNeighbor bitmap path AND pixel-exact ClearType text)
    // s2 — anisotropic clamp + trilinear mipmap (HighQuality / Fant / Unspecified default)
    D3D12_STATIC_SAMPLER_DESC samplers[3] = {};

    samplers[0].Filter = D3D12_FILTER_MIN_MAG_MIP_LINEAR;
    samplers[0].AddressU = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[0].AddressV = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[0].AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[0].ShaderRegister = 0;
    samplers[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

    samplers[1].Filter = D3D12_FILTER_MIN_MAG_MIP_POINT;
    samplers[1].AddressU = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[1].AddressV = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[1].AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[1].ShaderRegister = 1;
    samplers[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

    samplers[2].Filter = D3D12_FILTER_ANISOTROPIC;
    samplers[2].AddressU = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[2].AddressV = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[2].AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[2].MaxAnisotropy = 16;
    samplers[2].MinLOD = 0.0f;
    samplers[2].MaxLOD = D3D12_FLOAT32_MAX;
    samplers[2].ShaderRegister = 2;
    samplers[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

    D3D12_ROOT_SIGNATURE_DESC rootSigDesc = {};
    rootSigDesc.NumParameters = 4;
    rootSigDesc.pParameters = params;
    rootSigDesc.NumStaticSamplers = 3;
    rootSigDesc.pStaticSamplers = samplers;
    // ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT is required for PSOs that use vertex input
    // layouts (e.g. triangle PSO). PSOs without input layouts are unaffected.
    rootSigDesc.Flags = D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;

    ComPtr<ID3DBlob> signature, error;
    if (FAILED(D3D12SerializeRootSignature(&rootSigDesc, D3D_ROOT_SIGNATURE_VERSION_1_0, &signature, &error))) {
        if (error) OutputDebugStringA((const char*)error->GetBufferPointer());
        return false;
    }

    return SUCCEEDED(device_->CreateRootSignature(
        0, signature->GetBufferPointer(), signature->GetBufferSize(),
        IID_PPV_ARGS(&rootSignature_)));
}

bool D3D12DirectRenderer::CreatePSOs()
{
    // Use pre-compiled bytecode from d3d12_shader_bytecode.h (fxc /T *s_5_1 /O3).
    // This eliminates ~500ms+ of runtime D3DCompile on every window creation.
    using namespace shader_bytecode;

    // Wrap pre-compiled bytecode into ID3DBlob for downstream code that references blob pointers.
    auto wrapBytecode = [](const unsigned char* data, unsigned int size, ID3DBlob** blob) -> bool {
        HRESULT hr = D3DCreateBlob(size, blob);
        if (FAILED(hr)) return false;
        memcpy((*blob)->GetBufferPointer(), data, size);
        return true;
    };

    if (!wrapBytecode(ksdf_rect_vs, ksdf_rect_vsSize, &sdfRectVS_)) return false;
    if (!wrapBytecode(ksdf_rect_ps, ksdf_rect_psSize, &sdfRectPS_)) return false;
    if (!wrapBytecode(kbitmap_text_vs, kbitmap_text_vsSize, &textVS_)) return false;
    if (!wrapBytecode(kbitmap_text_ps, kbitmap_text_psSize, &textPS_)) return false;
    if (!wrapBytecode(kbitmap_text_smooth_ps, kbitmap_text_smooth_psSize, &textSmoothPS_)) return false;
    if (!wrapBytecode(kbitmap_quad_vs, kbitmap_quad_vsSize, &bitmapVS_)) return false;
    if (!wrapBytecode(kbitmap_quad_ps, kbitmap_quad_psSize, &bitmapPS_)) return false;
    if (!wrapBytecode(kcustom_effect_vs, kcustom_effect_vsSize, &customEffectVS_)) return false;
    if (!wrapBytecode(ktriangle_vs, ktriangle_vsSize, &triangleVS_)) return false;
    if (!wrapBytecode(ktriangle_ps, ktriangle_psSize, &trianglePS_)) return false;

    // SDF Rect PSO — no input layout (vertices from SV_VertexID, instances from StructuredBuffer)
    D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
    psoDesc.pRootSignature = rootSignature_.Get();
    psoDesc.VS = { sdfRectVS_->GetBufferPointer(), sdfRectVS_->GetBufferSize() };
    psoDesc.PS = { sdfRectPS_->GetBufferPointer(), sdfRectPS_->GetBufferSize() };

    // Premultiplied alpha blending
    psoDesc.BlendState.RenderTarget[0].BlendEnable = TRUE;
    psoDesc.BlendState.RenderTarget[0].SrcBlend = D3D12_BLEND_ONE;           // src already premultiplied
    psoDesc.BlendState.RenderTarget[0].DestBlend = D3D12_BLEND_INV_SRC_ALPHA;
    psoDesc.BlendState.RenderTarget[0].BlendOp = D3D12_BLEND_OP_ADD;
    psoDesc.BlendState.RenderTarget[0].SrcBlendAlpha = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
    psoDesc.BlendState.RenderTarget[0].BlendOpAlpha = D3D12_BLEND_OP_ADD;
    psoDesc.BlendState.RenderTarget[0].RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;

    psoDesc.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
    psoDesc.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
    psoDesc.RasterizerState.DepthClipEnable = FALSE;

    psoDesc.DepthStencilState.DepthEnable = FALSE;
    psoDesc.SampleMask = UINT_MAX;
    psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
    psoDesc.NumRenderTargets = 1;
    // Use SRGB format to match the SRGB RTV — GPU auto-converts linear->sRGB on write
    psoDesc.RTVFormats[0] = swapChainFormat_;
    // PSOs that target the swap-chain RT must match the MSAA sample count
    // because we render into a 4× MSAA color buffer and resolve at end of frame.
    psoDesc.SampleDesc.Count = 1;

    if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&sdfRectPSO_))))
        return false;

    // Text PSO — ClearType dual-source blending for per-channel sub-pixel alpha
    psoDesc.VS = { textVS_->GetBufferPointer(), textVS_->GetBufferSize() };
    psoDesc.PS = { textPS_->GetBufferPointer(), textPS_->GetBufferSize() };
    psoDesc.BlendState.RenderTarget[0].BlendEnable = TRUE;
    psoDesc.BlendState.RenderTarget[0].SrcBlend = D3D12_BLEND_ONE;              // premultiplied color * coverage already in shader
    psoDesc.BlendState.RenderTarget[0].DestBlend = D3D12_BLEND_INV_SRC1_COLOR;  // per-channel (1 - coverage) from SV_Target1
    psoDesc.BlendState.RenderTarget[0].BlendOp = D3D12_BLEND_OP_ADD;
    psoDesc.BlendState.RenderTarget[0].SrcBlendAlpha = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC1_ALPHA;
    psoDesc.BlendState.RenderTarget[0].BlendOpAlpha = D3D12_BLEND_OP_ADD;
    psoDesc.BlendState.RenderTarget[0].RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;
    if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&textPSO_))))
        return false;

    // Deformed-text PSO — identical dual-source ClearType blend, but the bilinear
    // text PS (smooth sub-pixel sampling) so transform-scaled / animated text moves
    // without per-glyph integer-snap jitter and thin strokes don't shimmer.
    psoDesc.PS = { textSmoothPS_->GetBufferPointer(), textSmoothPS_->GetBufferSize() };
    if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&textSmoothPSO_))))
        return false;

    // Restore standard premultiplied alpha blend for subsequent PSOs
    psoDesc.BlendState.RenderTarget[0].SrcBlend = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlend = D3D12_BLEND_INV_SRC_ALPHA;
    psoDesc.BlendState.RenderTarget[0].SrcBlendAlpha = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;

    // Bitmap PSO — same blend state, bitmap quad shaders
    psoDesc.VS = { bitmapVS_->GetBufferPointer(), bitmapVS_->GetBufferSize() };
    psoDesc.PS = { bitmapPS_->GetBufferPointer(), bitmapPS_->GetBufferSize() };
    if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&bitmapPSO_))))
        return false;

    // Copy-blend PSO — SDF rect shaders but with SRC=ONE, DEST=ZERO (overwrite)
    psoDesc.VS = { sdfRectVS_->GetBufferPointer(), sdfRectVS_->GetBufferSize() };
    psoDesc.PS = { sdfRectPS_->GetBufferPointer(), sdfRectPS_->GetBufferSize() };
    psoDesc.BlendState.RenderTarget[0].BlendEnable = TRUE;
    psoDesc.BlendState.RenderTarget[0].SrcBlend = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlend = D3D12_BLEND_ZERO;
    psoDesc.BlendState.RenderTarget[0].BlendOp = D3D12_BLEND_OP_ADD;
    psoDesc.BlendState.RenderTarget[0].SrcBlendAlpha = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_ZERO;
    psoDesc.BlendState.RenderTarget[0].BlendOpAlpha = D3D12_BLEND_OP_ADD;
    if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&copyBlendPSO_))))
        return false;

    // Triangle PSO — fresh desc to avoid accumulated state from previous PSOs
    {
        D3D12_INPUT_ELEMENT_DESC triInputLayout[] = {
            { "POSITION", 0, DXGI_FORMAT_R32G32_FLOAT,       0, 0,  D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 },
            { "COLOR",    0, DXGI_FORMAT_R32G32B32A32_FLOAT,  0, 8,  D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 },
        };

        D3D12_GRAPHICS_PIPELINE_STATE_DESC triDesc = {};
        triDesc.pRootSignature = rootSignature_.Get();
        triDesc.VS = { triangleVS_->GetBufferPointer(), triangleVS_->GetBufferSize() };
        triDesc.PS = { trianglePS_->GetBufferPointer(), trianglePS_->GetBufferSize() };
        triDesc.InputLayout = { triInputLayout, _countof(triInputLayout) };

        triDesc.BlendState.RenderTarget[0].BlendEnable = TRUE;
        triDesc.BlendState.RenderTarget[0].SrcBlend = D3D12_BLEND_ONE;
        triDesc.BlendState.RenderTarget[0].DestBlend = D3D12_BLEND_INV_SRC_ALPHA;
        triDesc.BlendState.RenderTarget[0].BlendOp = D3D12_BLEND_OP_ADD;
        triDesc.BlendState.RenderTarget[0].SrcBlendAlpha = D3D12_BLEND_ONE;
        triDesc.BlendState.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
        triDesc.BlendState.RenderTarget[0].BlendOpAlpha = D3D12_BLEND_OP_ADD;
        triDesc.BlendState.RenderTarget[0].RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;

        triDesc.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
        triDesc.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
        triDesc.RasterizerState.DepthClipEnable = FALSE;  // 2D UI, no depth buffer

        triDesc.DepthStencilState.DepthEnable = FALSE;
        triDesc.SampleMask = UINT_MAX;
        triDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
        triDesc.NumRenderTargets = 1;
        triDesc.RTVFormats[0] = swapChainFormat_;
        triDesc.SampleDesc.Count = 1;

        if (FAILED(device_->CreateGraphicsPipelineState(&triDesc, IID_PPV_ARGS(&trianglePSO_))))
            return false;
    }
    return true;
}

// ============================================================================
// Per-Frame Lifecycle
// ============================================================================

bool D3D12DirectRenderer::BeginFrame(UINT frameIndex, UINT width, UINT height,
                                      bool clear, float clearR, float clearG, float clearB, float clearA)
{
    if (!initialized_ || inFrame_) return false;
    if (frameIndex >= frameCount_) return false;

    // Device gate: never open a frame on a removed device (GPU switch, driver
    // restart, TDR). The fence below would read UINT64_MAX and wave the wait
    // through, and the allocator/list Reset would silently fail — recording
    // into a non-recording list inside a torn-down driver. BeginDraw re-checks
    // the backend on a false return and surfaces DEVICE_LOST to managed.
    if (device_ && FAILED(device_->GetDeviceRemovedReason())) return false;
    frameDeviceLost_ = false;

    currentFrame_ = frameIndex;
    viewportWidth_ = width;
    viewportHeight_ = height;

    auto& fr = frames_[currentFrame_];

    // Wait for the GPU to finish this buffer's previous work.
    // Short 50 ms timeout is deliberate: when the wait exceeds it, the
    // managed Window's retry loop releases the UI thread back to the
    // dispatcher so input / animation / WM_PAINT can still get a turn
    // between BeginDraw attempts. A long single wait here (e.g. 1000 ms)
    // would freeze the UI for the full Present→ready cycle, which on
    // iGPU is 100-130 ms and feels far worse than the FPS counter would
    // suggest. The retry path's per-attempt cost is accumulated below so
    // DevTools still reports the total wait honestly.
    //
    // IMPORTANT: ResetEvent before SetEventOnCompletion to clear any stale
    // signal from a previous timed-out wait. Without this, a previous
    // timeout leaves the event signaled when the old fence eventually
    // completes, causing the NEXT WaitForSingleObject to return immediately
    // even though the new fence value hasn't been reached — leading to
    // command allocator reuse while the GPU is still executing, which
    // corrupts rendering (flickering, stray lines).
    //
    // Frame-pacing instrumentation: every wait (success or timeout)
    // accumulates into accumulatingFrameWaitNs_, so a managed BeginDraw
    // retry loop on iGPU shows the full pile-up.
    if (fr.fenceValue > 0 && fence_->GetCompletedValue() < fr.fenceValue) {
        ResetEvent(fenceEvent_);
        fence_->SetEventOnCompletion(fr.fenceValue, fenceEvent_);
        LARGE_INTEGER waitStart = QpcNow();
        WaitForSingleObject(fenceEvent_, 50);
        LARGE_INTEGER waitEnd = QpcNow();
        accumulatingFrameWaitNs_ += QpcDiffNs(waitStart, waitEnd);
        if (fence_->GetCompletedValue() < fr.fenceValue) {
            // Timed-out path: surface partial wait + return false to let
            // the managed Window's retry mechanism release the dispatcher
            // queue for other work before trying again. accumulator stays
            // nonzero so the next attempt keeps appending.
            lastFrameGpuWaitNs_ = accumulatingFrameWaitNs_;
            return false;
        }
    }

    // The path stencil/MSAA scratch set is renderer-wide, not per-frame. The
    // regular wait above protects only this swap-chain slot; without this
    // additional dependency frame N+1 can clear and resolve the same scratch
    // textures while frame N is still executing on the GPU. During continuous
    // animation that presents as one stale/partially blended Path result per
    // in-flight back buffer (typically three flashes on a triple-buffer chain).
    if (pathScratchFenceValue_ > 0 &&
        fence_->GetCompletedValue() < pathScratchFenceValue_) {
        ResetEvent(fenceEvent_);
        fence_->SetEventOnCompletion(pathScratchFenceValue_, fenceEvent_);
        LARGE_INTEGER waitStart = QpcNow();
        WaitForSingleObject(fenceEvent_, 50);
        LARGE_INTEGER waitEnd = QpcNow();
        accumulatingFrameWaitNs_ += QpcDiffNs(waitStart, waitEnd);
        if (fence_->GetCompletedValue() < pathScratchFenceValue_) {
            lastFrameGpuWaitNs_ = accumulatingFrameWaitNs_;
            return false;
        }
    }

    // BeginFrame proceeded past the wait: flush the accumulator and stamp
    // the present-to-ready wall clock for the previously presented frame.
    lastFrameGpuWaitNs_ = accumulatingFrameWaitNs_;
    accumulatingFrameWaitNs_ = 0;
    if (lastPresentSignalQpc_ != 0) {
        LARGE_INTEGER readyTick = QpcNow();
        LARGE_INTEGER prev; prev.QuadPart = static_cast<LONGLONG>(lastPresentSignalQpc_);
        lastFramePresentToReadyNs_ = QpcDiffNs(prev, readyTick);
    }

    // The previous use of this slot is GPU-complete: any instance upload buffers
    // that mid-frame growth retired during that previous frame are no longer
    // referenced by the GPU and can now be released.  Clearing the ComPtr vector
    // drops the last refcount on each retired resource. Same lifetime applies to
    // the per-Dispatch Vello descriptor heaps drained onto retiredDescriptorHeaps.
    if (!fr.retiredInstanceBuffers.empty()) {
        fr.retiredInstanceBuffers.clear();
    }
    if (!fr.retiredDescriptorHeaps.empty()) {
        fr.retiredDescriptorHeaps.clear();
    }

    // Drain the backend's GPU-resource graveyard for everything whose
    // fence value is now ≤ the completed value. This is where bitmap
    // textures/upload buffers retired by D3D12Bitmap destruction (worker-
    // thread LRU eviction, GC finalizer) actually get freed — the
    // destructor only hands them to the graveyard; freeing happens here
    // after we've confirmed the GPU is done with them.
    if (backend_) {
        backend_->ReclaimRetiredGpuResources(fence_->GetCompletedValue());
    }

    // Reset allocator + command list. Both fail on a removed device (and the
    // gate above can race a removal happening right now); recording into a
    // list that never re-opened is driver UB, so bail and let BeginDraw's
    // device re-check classify the failure.
    HRESULT resetHr = fr.commandAllocator->Reset();
    if (SUCCEEDED(resetHr)) {
        resetHr = commandList_->Reset(fr.commandAllocator.Get(), nullptr);
    }
    if (FAILED(resetHr)) {
        return false;
    }

    // [JALIUM-921 diagnostic] The command list is now OPEN; the lines below record
    // a barrier referencing renderTargets_[currentFrame_]. Mark recording HERE — not
    // at inFrame_=true (end of this function) — so the open window is observable to a
    // resize racing on another thread.
    cmdListRecording_.store(true, std::memory_order_release);
    cmdListOwnerThread_.store(GetCurrentThreadId(), std::memory_order_release);

    // ── 4× MSAA color target setup ─────────────────────────────────────
    // We render into a 4-sample MSAA texture and resolve to the swap-chain
    // back buffer in EndFrame. This restores edge AA after Phase 1 of the
    // stroke pipeline switched from CPU scanline rasterization to direct
    // GPU triangle tessellation.
    //
    // D3D12 implicit-decay semantics: after ExecuteCommandLists finishes, a
    // RENDER_TARGET texture without explicit barriers decays back to COMMON.
    // So even though EndFrame ends the previous frame with MSAA in
    // RENDER_TARGET state, the GPU sees it as COMMON when we start this
    // frame's recording. Explicitly promote it back to RENDER_TARGET. Same
    // applies to the back-buffer transition (PRESENT == COMMON, both 0x0).
    // Phase 2 MSAA was rolled back: per the DevTools Perf trace, sampling
    // many bitmaps + DrawBackdropFilter + DrawSnapshotBlurred into a 4× MSAA
    // color buffer dropped the demo from 137 FPS (Phase 1, no MSAA, aliased
    // strokes) to 18 FPS — DrawBitmap alone went from sub-ms to ~5 ms/call
    // when its PSO changed to SampleDesc=4. The MSAA infrastructure stays
    // in place behind EnsureMsaaColorBuffer / GetMsaaRtvHandle so we can
    // re-enable it later (e.g. behind a per-element toggle, or once stroke
    // edges get analytic-AA in the fragment shader instead). For now we
    // bind the swap-chain back buffer directly so the rest of the pipeline
    // (effects, snapshot, etc.) keeps its sample-count = 1 fast paths.
    auto barrier = MakeTransitionBarrier(
        renderTargets_[currentFrame_].Get(),
        D3D12_RESOURCE_STATE_PRESENT,
        D3D12_RESOURCE_STATE_RENDER_TARGET);
    commandList_->ResourceBarrier(1, &barrier);
    auto rtvHandle = GetSwapChainRtvHandle();
    commandList_->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    if (clear) {
        float clearColor[4] = { clearR, clearG, clearB, clearA };
        commandList_->ClearRenderTargetView(rtvHandle, clearColor, 0, nullptr);
    }


    // Set viewport + default scissor
    D3D12_VIEWPORT vp = { 0, 0, (float)width, (float)height, 0, 1 };
    D3D12_RECT scissor = { 0, 0, (LONG)width, (LONG)height };
    commandList_->RSSetViewports(1, &vp);
    commandList_->RSSetScissorRects(1, &scissor);

    // Update frame constants — use DIP dimensions so that DIP coordinates from
    // managed layout map correctly to NDC.  The viewport stays in physical pixels
    // for full-resolution rendering; the shader's invScreenSize converts DIP → NDC.
    float dipWidth = (float)width / dpiScale_;
    float dipHeight = (float)height / dpiScale_;
    currentFrameConstants_.screenWidth = dipWidth;
    currentFrameConstants_.screenHeight = dipHeight;
    currentFrameConstants_.invScreenWidth = 1.0f / dipWidth;
    currentFrameConstants_.invScreenHeight = 1.0f / dipHeight;

    // Clear instance collections
    rectInstances_.clear();
    textInstances_.clear();
    bitmapInstances_.clear();
    triangleVertices_.clear();
    bitmapTextures_.clear();
    stencilPathDraws_.clear();
    batches_.clear();
    drawOrder_ = 0.0f;
    currentOpacity_ = 1.0f;
    currentShapeType_ = 0.0f;
    currentShapeN_ = 4.0f;

    // Reset ring buffer offsets for new frame — each frame uses its own SRV region
    // to prevent cross-frame descriptor races (GPU may still be reading the other
    // frame's descriptors when we start writing ours).
    uploadBufferOffset_ = 0;
    srvAllocOffset_ = currentFrame_ * frameSrvRegionSize_;

    // If the atlas overflowed last frame, grow it (or reset if already at max).
    // This is a frame boundary — BeginFrame has already waited this frame's
    // previous fence, so recreating the atlas resources is safe.  Mid-frame
    // would be unsafe because it changes atlas dimensions and invalidates the
    // UV coordinates of every glyph instance already emitted this frame.
    if (glyphAtlas_) {
        glyphAtlas_->ApplyPendingGrowthOrReset();
    }

    // Apply a pending path-MSAA sample-count change at the same frame boundary.
    // BeginFrame has already waited this slot's previous fence, so the old
    // stencil/cover PSOs and the MSAA scratch RT/depth are idle and safe to
    // rebuild. ApplyPendingPathMsaaSampleCount also zeroes pathMsaaWidth_/Height_
    // so the scratch resources recreate at the new sample count on this frame's
    // first path batch.
    if (pendingPathMsaaSampleCount_ != pathMsaaSampleCount_) {
        ApplyPendingPathMsaaSampleCount();
    }

    // Reset scissor stack
    while (!scissorStack_.empty()) scissorStack_.pop();
    roundedClipStack_.clear();

    // Reset pre-glass snapshot flag for fused panels
    preGlassSnapshotCaptured_ = false;
    snapshotValid_ = false;
    snapshotUsedThisFrame_ = false;
    // Sentinel so the first CaptureSnapshot in this frame can never short-
    // circuit against a stale watermark left from the previous frame.
    snapshotCaptureDrawOrder_ = -1.0f;
    offscreenCaptureValid_[0] = false;
    offscreenCaptureValid_[1] = false;
    offscreenResourcesUsedThisFrame_ = false;
    blurTempsUsedThisFrame_ = false;
    pathMsaaUsedThisFrame_ = false;
    inOffscreenCapture_ = false;
    inRetainedCapture_ = false;  // 防御：与 inOffscreenCapture_ 对称复位，兜底 retained-capture 标志在 resize/abort 时泄漏成 true（详见 EndRetainedLayerCapture），否则 effect capture 永久失败
    fr.constantsRingOffset = 0;  // reset ring buffer for this frame

    // Reset transform stack with identity
    while (!transformStack_.empty()) transformStack_.pop();
    transformStack_.push(Transform2D::Identity());

    // Begin Vello frame (skipped when Impeller is active). velloEnabled_ 为真
    // (active engine==Vello)时按需懒创建 Vello 子系统;Impeller 下整条跳过,
    // velloRenderer_ 永远为空,零开销。
    if (velloEnabled_ && EnsureVelloRenderer()) {
        velloRenderer_->BeginFrame(width, height);
    }

    // ── GPU timing: decode previous frame, start this frame ─────────────
    // Decode runs *after* fence wait above guaranteed the GPU finished
    // resolving this slot's previous queries → the readback buffer holds
    // valid data. We then reset the per-frame timing state and emit the
    // initial timestamp tagged with "Other" so any work before the first
    // explicit MarkGpuTimingPoint gets a category.
    if (timingSupported_) {
        DecodeGpuTimingForCompletedFrame(currentFrame_);
        auto& tf = timing_[currentFrame_];
        tf.nextSlot = 0;
        tf.spanCategories.clear();
        tf.batchCountAtFinalize = 0;
        tf.hasResolvedData = false;
        // First timestamp opens an "Other" span; subsequent marks override.
        MarkGpuTimingPoint(GpuTimingCategory::Other);
    }

    inFrame_ = true;
    return true;
}

bool D3D12DirectRenderer::CloseCommandListIfOpen(HRESULT* outCloseHr)
{
    if (outCloseHr) *outCloseHr = S_OK;
    // Atomically claim the close: only the caller that flips recording true→false
    // performs the Close, so the shared commandList_ is never Closed twice (an
    // invalid op the D3D12 debug layer flags as an error).
    if (!cmdListRecording_.exchange(false, std::memory_order_acq_rel)) {
        return false;
    }
    cmdListOwnerThread_.store(0, std::memory_order_release);
    if (commandList_) {
        if (CheckFrameDeviceAlive()) {
            HRESULT hr = commandList_->Close();
            if (outCloseHr) *outCloseHr = hr;
        } else if (outCloseHr) {
            // Removed device: intentionally skip the physical Close (recording API
            // into a torn-down driver is exactly what the abort avoids); leaving
            // the list open is safe — the next BeginFrame is stopped at its own
            // device gate before reusing it. Surface the removal in outCloseHr so
            // EndFrame's FAILED(closeHr) branch returns DEVICE_LOST and skips
            // ExecuteCommandLists/Present of the never-closed list — matching the
            // pre-fix behavior where Close() itself failed on a removed device.
            // (AbortFrame callers pass nullptr and are unaffected.)
            HRESULT removed = device_ ? device_->GetDeviceRemovedReason() : DXGI_ERROR_DEVICE_REMOVED;
            *outCloseHr = FAILED(removed) ? removed : DXGI_ERROR_DEVICE_REMOVED;
        }
    }
    return true;
}

void D3D12DirectRenderer::AbortFrame()
{
    // Key on the REAL list state (cmdListRecording_), not inFrame_. The list is
    // open from BeginFrame's commandList_->Reset (which precedes inFrame_=true)
    // through EndFrame's Close (which follows inFrame_=false), so gating the abort
    // on inFrame_ made it a no-op in those two windows — and a resize landing
    // there freed the back buffer the still-open list referenced (#921). Closing
    // is keyed on cmdListRecording_, which is true across both windows.
    if (!cmdListRecording_.load(std::memory_order_acquire) && !inFrame_) return;
    inFrame_ = false;

    // Discard the recorded commands without executing — the GPU never sees this
    // frame. Idempotent (atomic claim) and device-removed-safe (skips the Close).
    CloseCommandListIfOpen();

    // Clear instance collections so they don't leak into the next frame
    rectInstances_.clear();
    textInstances_.clear();
    bitmapInstances_.clear();
    triangleVertices_.clear();
    bitmapTextures_.clear();
    stencilPathDraws_.clear();
    batches_.clear();

    // Reset snapshot validity — the snapshot may reference stale back buffer content
    snapshotValid_ = false;
    snapshotUsedThisFrame_ = false;
    offscreenCaptureValid_[0] = false;
    offscreenCaptureValid_[1] = false;
    offscreenResourcesUsedThisFrame_ = false;
    blurTempsUsedThisFrame_ = false;
    pathMsaaUsedThisFrame_ = false;
    inOffscreenCapture_ = false;
    inRetainedCapture_ = false;  // 防御：与 inOffscreenCapture_ 对称复位，兜底 retained-capture 标志在 resize/abort 时泄漏成 true（详见 EndRetainedLayerCapture），否则 effect capture 永久失败
}

bool D3D12DirectRenderer::CheckFrameDeviceAlive()
{
    if (frameDeviceLost_) return false;
    if (device_ && FAILED(device_->GetDeviceRemovedReason())) {
        frameDeviceLost_ = true;
        return false;
    }
    return true;
}

JaliumResult D3D12DirectRenderer::EndFrame(bool useDirtyRects, const std::vector<D3D12_RECT>& dirtyRects,
                                    UINT syncInterval, UINT presentFlags, bool reportTransientPresentFailure)
{
    if (!inFrame_) return JALIUM_ERROR_INVALID_STATE;

    // Mid-frame device removal (GPU switch, driver restart, TDR): every call
    // below — SRV creation in RecordDrawCommands, Close, ExecuteCommandLists —
    // reaches a user-mode driver whose internals are already torn down.
    // NVIDIA's nvwgf2umx is known to AV (uncatchable in .NET) on that path
    // rather than fail. Abandon the recorded frame and surface DEVICE_LOST so
    // the managed recovery chain (dispose + rebuild context) takes over.
    if (!CheckFrameDeviceAlive()) {
        AbortFrame();
        return JALIUM_ERROR_DEVICE_LOST;
    }

    // Flush Vello paths before recording graphics commands
    FlushVelloPaths();

    // Upload instance data + record draw commands
    UploadInstances();
    RecordDrawCommands();

    // Re-check: the calls above can latch device loss themselves (Vello flush
    // gate, mid-frame buffer growth failure). Abort before recording the
    // barrier / GPU-timing / Close calls below into the dead driver. inFrame_
    // intentionally stays true until this point so AbortFrame's guard passes;
    // none of the three calls above read it.
    if (frameDeviceLost_) {
        AbortFrame();
        return JALIUM_ERROR_DEVICE_LOST;
    }
    inFrame_ = false;

    // Transition back buffer RENDER_TARGET → PRESENT (matching the original
    // pre-MSAA flow now that we render directly into the swap chain).
    auto barrier = MakeTransitionBarrier(
        renderTargets_[currentFrame_].Get(),
        D3D12_RESOURCE_STATE_RENDER_TARGET,
        D3D12_RESOURCE_STATE_PRESENT);
    commandList_->ResourceBarrier(1, &barrier);

    // GPU timing: emit the terminal timestamp + resolve the entire frame's
    // queries into the readback buffer. ResolveQueryData has to happen on
    // the same command list and *before* Close(); the result is GPU-side
    // available only after the frame's fence completes, which is exactly
    // what the next BeginFrame waits on before calling
    // DecodeGpuTimingForCompletedFrame.
    if (timingSupported_) {
        MarkGpuTimingPoint(GpuTimingCategory::kFrameEnd);
        auto& tf = timing_[currentFrame_];
        if (tf.nextSlot > 0 && timingQueryHeap_ && tf.readback) {
            UINT base = currentFrame_ * kMaxTimingSlotsPerFrame;
            commandList_->ResolveQueryData(
                timingQueryHeap_.Get(),
                D3D12_QUERY_TYPE_TIMESTAMP,
                base, tf.nextSlot,
                tf.readback.Get(),
                0);
            tf.batchCountAtFinalize = static_cast<uint32_t>(batches_.size());
            tf.hasResolvedData = true;
        }
    }

    // Close and execute. Route through CloseCommandListIfOpen so the close is
    // claimed atomically (no double Close vs a same-thread resize-initiated abort)
    // and cmdListRecording_/cmdListOwnerThread_ clear in lockstep. If the list was
    // already closed under us (a resize abort claimed it), this frame is abandoned:
    // do NOT Execute/Present a torn-down list — clear residual state and bail,
    // mirroring the device-lost abort branches above.
    HRESULT closeHr = S_OK;
    if (!CloseCommandListIfOpen(&closeHr)) {
        AbortFrame();
        return JALIUM_ERROR_INVALID_STATE;
    }
    if (FAILED(closeHr)) {
        // Command list recording had errors — submitting it would cause device removal.
        // Log the failure and skip this frame.
        LogDeviceRemovedReason("CommandList::Close", device_, closeHr);
        // Distinguish "recording error" from "device died under us": reporting
        // DEVICE_LOST here lets the managed recovery rebuild the context on its
        // FIRST attempt (forceNewContext) instead of burning a retry round on
        // INVALID_STATE before the next BeginDraw re-classifies it.
        if (device_ && FAILED(device_->GetDeviceRemovedReason())) {
            return JALIUM_ERROR_DEVICE_LOST;
        }
        return JALIUM_ERROR_INVALID_STATE;
    }
    ID3D12CommandList* lists[] = { commandList_.Get() };
    backend_->GetCommandQueue()->ExecuteCommandLists(1, lists);

    // Present — timed separately (presentStartQpc → presentBlockNs) because
    // under a slow compositor (occlusion throttling, remote/virtual displays)
    // a vsync-aligned Present blocks the calling thread for the whole DWM
    // buffer-retire interval (measured 130-460 ms). DevTools peels this out
    // of the "EndDraw" API row so the stall can't masquerade as CPU encode
    // work — same treatment the BeginDraw waitable wait already gets.
    LARGE_INTEGER presentStartQpc = QpcNow();
    HRESULT hr;
    if (useDirtyRects && !dirtyRects.empty()) {
        LONG bbW = (LONG)viewportWidth_;
        LONG bbH = (LONG)viewportHeight_;
        std::vector<RECT> presentRects;
        presentRects.reserve(dirtyRects.size());
        for (size_t i = 0; i < dirtyRects.size(); i++) {
            RECT r;
            r.left   = std::max((LONG)dirtyRects[i].left,   (LONG)0);
            r.top    = std::max((LONG)dirtyRects[i].top,    (LONG)0);
            r.right  = std::min((LONG)dirtyRects[i].right,  bbW);
            r.bottom = std::min((LONG)dirtyRects[i].bottom, bbH);
            if (r.right > r.left && r.bottom > r.top) {
                presentRects.push_back(r);
            }
        }
        if (!presentRects.empty()) {
            DXGI_PRESENT_PARAMETERS pp = {};
            pp.DirtyRectsCount = (UINT)presentRects.size();
            pp.pDirtyRects = presentRects.data();

            ComPtr<IDXGISwapChain1> sc1;
            if (SUCCEEDED(swapChain_->QueryInterface(IID_PPV_ARGS(&sc1)))) {
                hr = sc1->Present1(syncInterval, presentFlags, &pp);
            } else {
                hr = swapChain_->Present(syncInterval, presentFlags);
            }
        } else {
            // All dirty rects were clipped away — present without dirty rects
            hr = swapChain_->Present(syncInterval, presentFlags);
        }
    } else {
        DXGI_PRESENT_PARAMETERS pp = {};
        ComPtr<IDXGISwapChain1> sc1;
        if (SUCCEEDED(swapChain_->QueryInterface(IID_PPV_ARGS(&sc1)))) {
            hr = sc1->Present1(syncInterval, presentFlags, &pp);
        } else {
            hr = swapChain_->Present(syncInterval, presentFlags);
        }
    }

    lastFramePresentBlockNs_ = QpcDiffNs(presentStartQpc, QpcNow());

    // Signal fence for this frame
    frames_[currentFrame_].fenceValue = nextFenceValue_++;
    backend_->GetCommandQueue()->Signal(fence_.Get(), frames_[currentFrame_].fenceValue);
    if (pathMsaaUsedThisFrame_) {
        pathScratchFenceValue_ = frames_[currentFrame_].fenceValue;
    }

    // Frame-pacing: stamp the QPC tick at the moment Signal is issued. The
    // next BeginFrame's "wait completed" tick minus this gives ≈ GPU work
    // time for the frame we are presenting now.
    lastPresentSignalQpc_ = static_cast<uint64_t>(QpcNow().QuadPart);

    // Tell the backend graveyard that anything retired from now on must
    // outlive at least this fence value. Resources retired *before* this
    // call are tagged with an older value and may already be reclaimable;
    // resources retired *after* this call are tagged with the new value
    // and will be reclaimed when this frame's Signal completes.
    backend_->NoteSubmittedFenceValue(frames_[currentFrame_].fenceValue);

    if (SUCCEEDED(hr) || hr == DXGI_STATUS_OCCLUDED) {
        return JALIUM_OK;
    }

    // True device loss — GPU removed, reset, or driver crash.
    if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET) {
        LogDeviceRemovedReason("Present", device_, hr);
        return JALIUM_ERROR_DEVICE_LOST;
    }

    // Transient Present failure (e.g. DXGI_ERROR_INVALID_CALL during resize,
    // mode change, etc.) on a healthy device — the frame's GPU work was
    // submitted, only the present was dropped.
    //
    // Under external present pacing this MUST surface to managed code: the
    // scheduler consumed a present credit at BeginDraw, and a FAILED Present
    // never signals the frame-latency waitable. Swallowing the error here
    // (returning OK) strands that credit — the event-driven scheduler then
    // starves until the 500 ms heartbeat. Credit-conservation iron rule: a
    // credit returns to the pool ONLY via a successful present's waitable
    // signal or an explicit managed-side return; native must never eat a
    // failed present. PRESENT_FAILED (not INVALID_STATE) keeps the managed
    // handler on the repaint path instead of a needless RT rebuild.
    if (reportTransientPresentFailure) {
        return JALIUM_ERROR_PRESENT_FAILED;
    }

    // Legacy contract (no external pacing): treat as a dropped frame — the
    // next frame will retry.
    return JALIUM_OK;
}

// ============================================================================
// GPU timing
// ============================================================================

void D3D12DirectRenderer::MarkGpuTimingPoint(GpuTimingCategory category)
{
    if (!timingSupported_ || !timingQueryHeap_) return;
    auto& tf = timing_[currentFrame_];
    if (tf.nextSlot >= kMaxTimingSlotsPerFrame) return;  // budget exhausted, drop
    UINT slot = currentFrame_ * kMaxTimingSlotsPerFrame + tf.nextSlot;
    commandList_->EndQuery(timingQueryHeap_.Get(), D3D12_QUERY_TYPE_TIMESTAMP, slot);
    tf.spanCategories.push_back(category);
    tf.nextSlot += 1;
}

void D3D12DirectRenderer::DecodeGpuTimingForCompletedFrame(UINT frameIndex)
{
    if (!timingSupported_ || frameIndex >= frameCount_) return;
    auto& tf = timing_[frameIndex];
    if (!tf.hasResolvedData || tf.nextSlot < 2 || !tf.readback || timestampFrequency_ == 0) {
        // No fully-formed frame to decode (e.g. very first frame after init).
        return;
    }

    // Map readback. The CPU read-only range is sized to the slots we actually
    // resolved; passing nullptr to Unmap is the documented "no-write" path.
    void* mapped = nullptr;
    D3D12_RANGE readRange = { 0, sizeof(uint64_t) * tf.nextSlot };
    if (FAILED(tf.readback->Map(0, &readRange, &mapped)) || !mapped) {
        tf.hasResolvedData = false;
        return;
    }
    const uint64_t* timestamps = static_cast<const uint64_t*>(mapped);

    GpuTimingSnapshot snap;
    snap.batchCount = tf.batchCountAtFinalize;
    // Each consecutive pair (i, i+1) is a span; the span's category is the
    // one we tagged at the timestamp that *opened* the span (slot i).
    for (UINT i = 0; i + 1 < tf.nextSlot; ++i) {
        GpuTimingCategory cat = tf.spanCategories[i];
        if (cat == GpuTimingCategory::kFrameEnd) continue;  // safety
        uint64_t a = timestamps[i];
        uint64_t b = timestamps[i + 1];
        if (b <= a) continue;  // ignore clock anomalies / wrap
        uint64_t deltaTicks = b - a;
        // Convert ticks → ns using the queue's reported frequency.
        uint64_t ns = (deltaTicks / timestampFrequency_) * 1'000'000'000ull
                    + ((deltaTicks % timestampFrequency_) * 1'000'000'000ull) / timestampFrequency_;
        size_t catIdx = static_cast<size_t>(cat);
        if (catIdx < static_cast<size_t>(GpuTimingCategory::kCount)) {
            snap.categoryNs[catIdx] += ns;
        }
    }
    // Total span = first→last timestamp difference, computed independently
    // so it includes intervals where the span category was unclassified.
    if (tf.nextSlot >= 2) {
        uint64_t total = timestamps[tf.nextSlot - 1] - timestamps[0];
        snap.totalNs = (total / timestampFrequency_) * 1'000'000'000ull
                     + ((total % timestampFrequency_) * 1'000'000'000ull) / timestampFrequency_;
    }
    snap.valid = true;

    tf.readback->Unmap(0, nullptr);
    tf.hasResolvedData = false;  // consume — next BeginFrame won't re-decode same data
    lastGpuTimingSnapshot_ = snap;
}

// ============================================================================
// DPI
// ============================================================================

void D3D12DirectRenderer::SetDpiScale(float dpiScale)
{
    if (dpiScale > 0) {
        dpiScale_ = dpiScale;
    }
    if (glyphAtlas_ && dpiScale > 0) {
        float oldScale = glyphAtlas_->GetDpiScale();
        if (std::abs(oldScale - dpiScale) > 0.01f) {
            // DPI changed — reset atlas to re-rasterize at new scale
            glyphAtlas_->Reset();
            glyphAtlas_->SetDpiScale(dpiScale);
        }
    }
}

// ============================================================================
// Draw Commands
// ============================================================================

// Batch state compatibility check used by all per-instance emit paths
// (AddSdfRect / AddText / AddTriangles*).  When the previous batch is the
// same type and shares scissor + rounded-clip state with the candidate,
// the new instance can be appended to that batch instead of producing a
// fresh DrawBatch + extra DrawIndexedInstanced call at record time.
//
// Texture identity (bitmap path) is checked at the call site since it
// requires looking at bitmapTextures_ alongside the batch.
static inline bool BatchStateCompatibleForMerge(const DrawBatch& prev, const DrawBatch& cand)
{
    if (prev.type != cand.type) return false;
    if (prev.smoothText != cand.smoothText) return false;  // different text PSO (point vs bilinear)
    if (prev.hasScissor != cand.hasScissor) return false;
    if (cand.hasScissor) {
        if (prev.scissor.left   != cand.scissor.left  ||
            prev.scissor.top    != cand.scissor.top   ||
            prev.scissor.right  != cand.scissor.right ||
            prev.scissor.bottom != cand.scissor.bottom)
            return false;
    }
    if (prev.hasRoundedClip != cand.hasRoundedClip) return false;
    if (cand.hasRoundedClip) {
        if (prev.roundedClipInverse != cand.roundedClipInverse) return false;
        if (memcmp(prev.roundedClipRect,        cand.roundedClipRect,        sizeof(float) * 4) != 0) return false;
        if (memcmp(prev.roundedClipCornerRadii, cand.roundedClipCornerRadii, sizeof(float) * 4) != 0) return false;
    }
    return true;
}

void D3D12DirectRenderer::AddSdfRect(const SdfRectInstance& inst)
{
    if (rectInstances_.size() >= kMaxInstancesPerFrame) {
        // Auto-flush: upload and record the current batch, then continue
        // instead of dropping the draw call.
        if (!FlushGraphicsForCompute()) return;  // device lost — frame will abort
    }

    // Build the fully-resolved instance first.  Transform/opacity/shape are
    // baked into SdfRectInstance per-vertex, so they do NOT affect batch
    // coalescing — only scissor + rounded clip + batch type do.
    SdfRectInstance adjusted = inst;
    adjusted.fillR *= adjusted.fillA;
    adjusted.fillG *= adjusted.fillA;
    adjusted.fillB *= adjusted.fillA;
    adjusted.borderR *= adjusted.borderA;
    adjusted.borderG *= adjusted.borderA;
    adjusted.borderB *= adjusted.borderA;

    adjusted.opacity *= currentOpacity_;

    // Carry the full current transform into the instance; the vertex shader
    // applies it to the quad corners (oriented-quad path). Position, size, corner
    // radii, border width and gradient geometry stay in the caller's pre-transform
    // space, so rotation / negative-diagonal (180 deg) / skew are no longer
    // collapsed into a mispositioned axis-aligned quad by a sign-stripped sqrt()
    // scale. For an un-rotated element this matrix is identity and the emitted
    // quad + SDF inputs are bit-identical to the legacy CPU-pre-transform path.
    const auto& t = transformStack_.top();
    adjusted.xfM11 = t.m11; adjusted.xfM12 = t.m12;
    adjusted.xfM21 = t.m21; adjusted.xfM22 = t.m22;
    adjusted.xfDx  = t.dx;  adjusted.xfDy  = t.dy;

    if (adjusted.shapeType <= 0.0f) {
        adjusted.shapeType = currentShapeType_;
        adjusted.shapeN = currentShapeN_;
    }

    // Prepare the candidate batch state (scissor + rounded clip).
    DrawBatch candidate;
    candidate.type = DrawBatchType::SdfRect;
    candidate.instanceOffset = (uint32_t)rectInstances_.size();
    candidate.instanceCount = 1;
    candidate.hasScissor = !scissorStack_.empty();
    if (candidate.hasScissor) candidate.scissor = scissorStack_.top();
    ResolveRoundedClipForBatch(candidate);

    // Coalesce with the previous batch when state matches and the new
    // instance lands directly after it in rectInstances_ — N back-to-back
    // FillRectangle / FillRoundedRectangle / FillPerCornerRoundedRectangle
    // calls collapse into 1 DrawIndexedInstanced(N) at record time.
    if (!batches_.empty()) {
        DrawBatch& prev = batches_.back();
        if (BatchStateCompatibleForMerge(prev, candidate) &&
            prev.instanceOffset + prev.instanceCount == (uint32_t)rectInstances_.size()) {
            prev.instanceCount++;
            rectInstances_.push_back(adjusted);
            return;
        }
    }

    candidate.sortOrder = drawOrder_++;
    batches_.push_back(candidate);
    rectInstances_.push_back(adjusted);
}

void D3D12DirectRenderer::AddText(IDWriteTextLayout* layout, float x, float y,
                                   float r, float g, float b, float a,
                                   uint64_t layoutKey,
                                   int32_t aaMode,
                                   int32_t hintingMode)
{
    if (!glyphAtlas_ || !layout) return;

    if (textInstances_.size() >= kMaxInstancesPerFrame) {
        if (!FlushGraphicsForCompute()) return;  // device lost — frame will abort
    }

    uint32_t startIdx = (uint32_t)textInstances_.size();

    // Apply current transform to the text origin
    const auto& t = transformStack_.top();
    float tx = x * t.m11 + y * t.m21 + t.dx;
    float ty = x * t.m12 + y * t.m22 + t.dy;

    // Apply current opacity
    float effectiveA = a * currentOpacity_;

    // Per-axis transform scale this text will undergo on screen. We hand these to
    // GenerateGlyphs so each glyph is rasterized ALREADY-deformed (scaleX wide,
    // scaleY tall) via a DWRITE_MATRIX — so a liquid-glass squeeze stays crisp
    // (thin stems survive: no d->c / r->l) instead of point-minifying an isotropic
    // atlas. GenerateGlyphs quantizes these internally for the cache; the quads
    // come back BASE-DIP and the post-transform scaling below restores on-screen
    // size 1:1 against the deformed bitmap.
    float scaleX = std::sqrt(t.m11 * t.m11 + t.m12 * t.m12);
    float scaleY = std::sqrt(t.m21 * t.m21 + t.m22 * t.m22);

    // Collect glyph instances and text decorations
    std::vector<D3D12GlyphAtlas::TextDecorationRect> decorations;
    uint32_t count = glyphAtlas_->GenerateGlyphs(layout, tx, ty, r, g, b, effectiveA,
                                                  textInstances_, &decorations,
                                                  layoutKey,
                                                  aaMode, hintingMode,
                                                  scaleX, scaleY);

    // Apply transform scaling to each glyph instance.
    // GenerateGlyphs emits BASE-DIP quads (any rasterScale magnification was
    // divided back out) positioned relative to the transformed origin (tx,ty).
    // Scaling position + size by the transform's axis scales here magnifies the
    // base-DIP quad to its on-screen size; because the atlas bitmap was already
    // rasterized at rasterScale, the magnified quad stays crisp instead of
    // mosaicking (rasterScale rounds the magnitude up, so the bitmap is never
    // upscaled past 1:1 — at most a slightly hi-res atlas is minified).
    if (count > 0 && (std::abs(scaleX - 1.0f) > 0.001f || std::abs(scaleY - 1.0f) > 0.001f)) {
        for (uint32_t i = startIdx; i < startIdx + count; i++) {
            auto& g = textInstances_[i];
            // Scale position relative to the transformed text origin
            g.posX = tx + (g.posX - tx) * scaleX;
            g.posY = ty + (g.posY - ty) * scaleY;
            // Scale glyph quad size
            g.sizeX *= scaleX;
            g.sizeY *= scaleY;
        }
    }

    if (count > 0) {
        DrawBatch candidate;
        candidate.type = DrawBatchType::Text;
        // Deformed text (any transform scale) draws with the bilinear text PSO so
        // it animates smoothly (no per-glyph integer-snap jitter / thin-stroke
        // shimmer); 1:1 text stays on the crisp point PSO.
        candidate.smoothText = (std::abs(scaleX - 1.0f) > 0.001f ||
                                std::abs(scaleY - 1.0f) > 0.001f);
        candidate.instanceOffset = startIdx;
        candidate.instanceCount = count;
        candidate.hasScissor = !scissorStack_.empty();
        if (candidate.hasScissor) candidate.scissor = scissorStack_.top();
        ResolveRoundedClipForBatch(candidate);

        // Coalesce consecutive DrawText calls when state matches and the
        // new glyph run lands directly after the previous batch in
        // textInstances_ — 800+ per-control labels collapse to a handful
        // of Text DrawIndexedInstanced calls.
        bool merged = false;
        if (!batches_.empty()) {
            DrawBatch& prev = batches_.back();
            if (BatchStateCompatibleForMerge(prev, candidate) &&
                prev.instanceOffset + prev.instanceCount == startIdx) {
                prev.instanceCount += count;
                merged = true;
            }
        }
        if (!merged) {
            candidate.sortOrder = drawOrder_++;
            batches_.push_back(candidate);
        }
    }

    // Render text decorations (underline/strikethrough) as SDF rect instances
    for (auto& dec : decorations) {
        SdfRectInstance inst = {};
        inst.posX = dec.x;
        inst.posY = dec.y;
        inst.sizeX = dec.width;
        inst.sizeY = dec.thickness;
        inst.fillR = dec.colorR;
        inst.fillG = dec.colorG;
        inst.fillB = dec.colorB;
        inst.fillA = dec.colorA;
        inst.opacity = 1.0f;
        AddSdfRect(inst);
    }
}

void D3D12DirectRenderer::AddBitmap(float x, float y, float w, float h, float opacity,
                                     ID3D12Resource* textureResource, DXGI_FORMAT format,
                                     float uvMaxX, float uvMaxY,
                                     int scalingMode,
                                     ID3D12Resource* uploadBuffer)
{
    if (!textureResource) return;
    if (bitmapInstances_.size() >= kMaxInstancesPerFrame) {
        // Auto-flush: upload and record the current batch, then continue
        if (!FlushGraphicsForCompute()) return;  // device lost — frame will abort
    }

    // Map JaliumBitmapScalingMode → shader sampler slot (s0=linear, s1=point, s2=aniso).
    // Unspecified, HighQuality, Fant default to anisotropic — UI icons get sharp scaling
    // out of the box, matching the user's "default high quality" expectation.
    float samplerIdx;
    switch (scalingMode) {
        case 1:  samplerIdx = 0.0f; break;  // LowQuality → linear
        case 3:  samplerIdx = 1.0f; break;  // NearestNeighbor → point
        case 4:  samplerIdx = 0.0f; break;  // Linear → linear
        case 0:                              // Unspecified → high quality
        case 2:                              // HighQuality
        case 5:                              // Fant
        default: samplerIdx = 2.0f; break;  // anisotropic
    }

    BitmapQuadInstance inst = {};
    inst.posX = x; inst.posY = y;
    inst.sizeX = w; inst.sizeY = h;
    inst.uvMinX = 0.0f; inst.uvMinY = 0.0f;
    inst.uvMaxX = uvMaxX; inst.uvMaxY = uvMaxY;
    inst.opacity = opacity * currentOpacity_;
    inst.samplerIdx = samplerIdx;

    // Carry the full current transform into the instance; the vertex shader
    // applies it to the quad corners so rotated / 180-deg-flipped / skewed
    // bitmaps (incl. retained-layer composites and offscreen blits) render
    // correctly instead of being mispositioned by the old sign-stripped scale.
    // pos/size stay in pre-transform space. Identity => bit-identical output.
    const auto& t = transformStack_.top();
    inst.xfM11 = t.m11; inst.xfM12 = t.m12;
    inst.xfM21 = t.m21; inst.xfM22 = t.m22;
    inst.xfDx  = t.dx;  inst.xfDy  = t.dy;

    DrawBatch candidate;
    candidate.type = DrawBatchType::Bitmap;
    candidate.instanceOffset = (uint32_t)bitmapInstances_.size();
    candidate.instanceCount = 1;
    candidate.hasScissor = !scissorStack_.empty();
    if (candidate.hasScissor) candidate.scissor = scissorStack_.top();
    ResolveRoundedClipForBatch(candidate);

    // Coalesce with previous Bitmap batch when state matches (scissor +
    // rounded clip via BatchStateCompatibleForMerge), the texture identity
    // matches (textureResource + format), and the new instance lands
    // directly after the previous batch's slice in bitmapInstances_.
    // N back-to-back DrawBitmap calls (ImageBrush.TileMode, sprite sheets,
    // glyph runs via bitmap path) collapse to one SRV-descriptor-pair +
    // one DrawIndexedInstanced(N) at record time.
    //
    // samplerIdx / opacity / transform are baked into BitmapQuadInstance,
    // so they do NOT block coalescing.
    if (!batches_.empty() && !bitmapTextures_.empty()) {
        DrawBatch& prev = batches_.back();
        const BitmapBatchTexture& prevTex = bitmapTextures_.back();
        const bool sameTex = prevTex.batchIndex == (uint32_t)(batches_.size() - 1) &&
                             prevTex.textureResource.Get() == textureResource &&
                             prevTex.format == format;
        if (sameTex &&
            BatchStateCompatibleForMerge(prev, candidate) &&
            prev.instanceOffset + prev.instanceCount == bitmapInstances_.size()) {
            prev.instanceCount++;
            bitmapInstances_.push_back(inst);
            return;
        }
    }

    BitmapBatchTexture tex;
    tex.batchIndex = (uint32_t)batches_.size();
    tex.textureResource = textureResource;
    tex.uploadBuffer = uploadBuffer;  // may be nullptr (offscreen RT / desktop dup sources)
    tex.format = format;
    bitmapTextures_.push_back(tex);

    candidate.sortOrder = drawOrder_++;
    batches_.push_back(candidate);
    bitmapInstances_.push_back(inst);
}

// ============================================================================
// Triangle Fill (for path/polygon)
// ============================================================================

void D3D12DirectRenderer::AddTriangles(const TriangleVertex* vertices, uint32_t vertexCount)
{
    if (!inFrame_ || !vertices || vertexCount < 3) return;

    if (triangleVertices_.size() + vertexCount > kMaxInstancesPerFrame * 16) {
        // Auto-flush to avoid buffer overflow
        if (!FlushGraphicsForCompute()) return;  // device lost — frame will abort
    }

    // Apply current transform and opacity to all vertices
    const auto& t = transformStack_.top();
    float opacity = currentOpacity_;
    uint32_t startVertex = (uint32_t)triangleVertices_.size();

    for (uint32_t i = 0; i < vertexCount; i++) {
        TriangleVertex v = vertices[i];
        float newX = v.x * t.m11 + v.y * t.m21 + t.dx;
        float newY = v.x * t.m12 + v.y * t.m22 + t.dy;
        v.x = newX;
        v.y = newY;

        // Apply currentOpacity_ by scaling premultiplied RGBA
        if (opacity < 1.0f - (1.0f / 255.0f)) {
            v.r *= opacity;
            v.g *= opacity;
            v.b *= opacity;
            v.a *= opacity;
        }

        triangleVertices_.push_back(v);
    }

    DrawBatch candidate;
    candidate.type = DrawBatchType::Triangle;
    candidate.instanceOffset = startVertex;         // repurposed: vertex offset
    candidate.instanceCount = vertexCount;          // repurposed: vertex count
    candidate.hasScissor = !scissorStack_.empty();
    if (candidate.hasScissor) candidate.scissor = scissorStack_.top();
    ResolveRoundedClipForBatch(candidate);

    if (!batches_.empty()) {
        DrawBatch& prev = batches_.back();
        if (BatchStateCompatibleForMerge(prev, candidate) &&
            prev.instanceOffset + prev.instanceCount == startVertex) {
            prev.instanceCount += vertexCount;
            return;
        }
    }

    candidate.sortOrder = drawOrder_++;
    batches_.push_back(candidate);
}

void D3D12DirectRenderer::AddTrianglesPreTransformed(const TriangleVertex* vertices, uint32_t vertexCount)
{
    if (!inFrame_ || !vertices || vertexCount < 3) return;

    if (triangleVertices_.size() + vertexCount > kMaxInstancesPerFrame * 16) {
        if (!FlushGraphicsForCompute()) return;  // device lost — frame will abort
    }

    // Vertices are already in pixel-space with opacity applied — add directly
    uint32_t startVertex = (uint32_t)triangleVertices_.size();
    triangleVertices_.insert(triangleVertices_.end(), vertices, vertices + vertexCount);

    DrawBatch candidate;
    candidate.type = DrawBatchType::Triangle;
    candidate.instanceOffset = startVertex;
    candidate.instanceCount = vertexCount;
    candidate.hasScissor = !scissorStack_.empty();
    if (candidate.hasScissor) candidate.scissor = scissorStack_.top();
    ResolveRoundedClipForBatch(candidate);

    if (!batches_.empty()) {
        DrawBatch& prev = batches_.back();
        if (BatchStateCompatibleForMerge(prev, candidate) &&
            prev.instanceOffset + prev.instanceCount == startVertex) {
            prev.instanceCount += vertexCount;
            return;
        }
    }

    candidate.sortOrder = drawOrder_++;
    batches_.push_back(candidate);
}

// ============================================================================
// State Stacks
// ============================================================================

void D3D12DirectRenderer::PushTransform(float m11, float m12, float m21, float m22, float dx, float dy)
{
    Transform2D incoming = { m11, m12, m21, m22, dx, dy };
    Transform2D combined = transformStack_.empty()
        ? incoming
        : transformStack_.top() * incoming;
    transformStack_.push(combined);
}

void D3D12DirectRenderer::PopTransform()
{
    if (transformStack_.size() > 1)
        transformStack_.pop();
}

Transform2D D3D12DirectRenderer::GetCurrentTransform() const
{
    return transformStack_.empty() ? Transform2D::Identity() : transformStack_.top();
}

void D3D12DirectRenderer::PushScissor(float x, float y, float w, float h)
{
    // Draw primitives apply the current transform to coordinates CPU-side,
    // so the scissor must be transformed to match.  Compute the axis-aligned
    // bounding box of the transformed clip rectangle.
    const auto& t = transformStack_.top();
    float x0 = x * t.m11 + y * t.m21 + t.dx;
    float y0 = x * t.m12 + y * t.m22 + t.dy;
    float x1 = (x+w) * t.m11 + y * t.m21 + t.dx;
    float y1 = (x+w) * t.m12 + y * t.m22 + t.dy;
    float x2 = x * t.m11 + (y+h) * t.m21 + t.dx;
    float y2 = x * t.m12 + (y+h) * t.m22 + t.dy;
    float x3 = (x+w) * t.m11 + (y+h) * t.m21 + t.dx;
    float y3 = (x+w) * t.m12 + (y+h) * t.m22 + t.dy;

    float minX = (std::min)({x0, x1, x2, x3});
    float minY = (std::min)({y0, y1, y2, y3});
    float maxX = (std::max)({x0, x1, x2, x3});
    float maxY = (std::max)({y0, y1, y2, y3});

    // Scissor must include every pixel whose center lies inside the clip rect
    // in physical-pixel space. (LONG) truncates toward 0 — equivalent to floor
    // for positives — which silently shrinks right/bottom by up to 1 px when
    // the clip rect has any sub-pixel fraction (DPI scaling 150 % / 200 %,
    // or layout snapping that lands on .5 boundaries). The pixel rows lost
    // along the bottom and right edges are exactly the rows where the SDF
    // rounded-rect Background fills its AA gradient, so the smoothed corner
    // gets hard-cut into a stair-step. Expand outward: floor on min, ceil on
    // max — D3D12 scissor is [left, right) / [top, bottom) inclusive-exclusive,
    // so ceil on the maxima still excludes the next-out-of-bounds pixel.
    D3D12_RECT rect;
    rect.left   = (LONG)std::floor(minX * dpiScale_);
    rect.top    = (LONG)std::floor(minY * dpiScale_);
    rect.right  = (LONG)std::ceil (maxX * dpiScale_);
    rect.bottom = (LONG)std::ceil (maxY * dpiScale_);

    // Intersect with parent scissor if any
    if (!scissorStack_.empty()) {
        auto& parent = scissorStack_.top();
        rect.left = std::max(rect.left, parent.left);
        rect.top = std::max(rect.top, parent.top);
        rect.right = std::min(rect.right, parent.right);
        rect.bottom = std::min(rect.bottom, parent.bottom);
    }

    scissorStack_.push(rect);
}

void D3D12DirectRenderer::PopScissor()
{
    if (!scissorStack_.empty())
        scissorStack_.pop();
}

void D3D12DirectRenderer::PushRoundedClip(float x, float y, float w, float h, float rx, float ry)
{
    // Symmetric variant: all four corners get the same radius.  Use min(rx, ry)
    // so the SDF stays valid (sd_round_box assumes a single radius per corner).
    float r = std::min(rx, ry);
    PushPerCornerRoundedClip(x, y, w, h, r, r, r, r);
}

void D3D12DirectRenderer::PushPerCornerRoundedClip(float x, float y, float w, float h,
    float tl, float tr, float br, float bl)
{
    RoundedClipState s {};
    s.x = x; s.y = y; s.w = w; s.h = h;
    s.radiusTL = tl;
    s.radiusTR = tr;
    s.radiusBR = br;
    s.radiusBL = bl;
    s.transform = transformStack_.empty() ? Transform2D::Identity() : transformStack_.top();
    roundedClipStack_.push_back(s);
}

void D3D12DirectRenderer::PushRoundedClipExclude(float x, float y, float w, float h, float rx, float ry)
{
    // Symmetric rounded clip, but flagged inverse so the pixel-shader SDF mask
    // keeps the area OUTSIDE the rect (masks the interior). Deliberately pushes
    // NO scissor: an inverse clip must remain free to paint just beyond the
    // bounds, whereas a scissor would re-confine drawing back inside the rect.
    float r = std::min(rx, ry);
    RoundedClipState s {};
    s.x = x; s.y = y; s.w = w; s.h = h;
    s.radiusTL = r; s.radiusTR = r; s.radiusBR = r; s.radiusBL = r;
    s.inverse = true;
    s.transform = transformStack_.empty() ? Transform2D::Identity() : transformStack_.top();
    roundedClipStack_.push_back(s);
}

void D3D12DirectRenderer::PopRoundedClip()
{
    if (!roundedClipStack_.empty())
        roundedClipStack_.pop_back();
}

bool D3D12DirectRenderer::ResolveRoundedClipForBatch(DrawBatch& batch) const
{
    // Forced override: when replaying snapshotted Impeller batches, each batch
    // already carries the rounded-clip state it was captured under (already in
    // physical px, identical units to what this function would compute).  Use
    // that instead of the live stack, whose top may have advanced since the
    // batch was recorded — we no longer flush at every clip boundary.
    if (forcedRoundedClipActive_) {
        if (!forcedRoundedClipPresent_) {
            batch.hasRoundedClip = false;
            return false;
        }
        batch.hasRoundedClip = true;
        batch.roundedClipInverse = false; // Impeller-replay forced clips are never inverse
        batch.roundedClipRect[0] = forcedRoundedClipRect_[0];
        batch.roundedClipRect[1] = forcedRoundedClipRect_[1];
        batch.roundedClipRect[2] = forcedRoundedClipRect_[2];
        batch.roundedClipRect[3] = forcedRoundedClipRect_[3];
        batch.roundedClipCornerRadii[0] = forcedRoundedClipRadii_[0];
        batch.roundedClipCornerRadii[1] = forcedRoundedClipRadii_[1];
        batch.roundedClipCornerRadii[2] = forcedRoundedClipRadii_[2];
        batch.roundedClipCornerRadii[3] = forcedRoundedClipRadii_[3];
        return true;
    }

    float rect[4], radii[4];
    bool inverse = false;
    if (!ResolveLiveRoundedClip(rect, radii, &inverse)) {
        batch.hasRoundedClip = false;
        return false;
    }
    batch.hasRoundedClip = true;
    batch.roundedClipInverse = inverse;
    batch.roundedClipRect[0] = rect[0];
    batch.roundedClipRect[1] = rect[1];
    batch.roundedClipRect[2] = rect[2];
    batch.roundedClipRect[3] = rect[3];
    batch.roundedClipCornerRadii[0] = radii[0];
    batch.roundedClipCornerRadii[1] = radii[1];
    batch.roundedClipCornerRadii[2] = radii[2];
    batch.roundedClipCornerRadii[3] = radii[3];
    return true;
}

// Resolves the innermost live rounded clip into physical-pixel rect/radii.
// Shared by ResolveRoundedClipForBatch (DirectRenderer's own batches) and
// ResolveCurrentRoundedClip (mirrored into the Impeller engine so its batches
// snapshot the same clip).  Returns false when the stack is empty.
bool D3D12DirectRenderer::ResolveLiveRoundedClip(float outRect[4], float outRadii[4], bool* outInverse) const
{
    if (roundedClipStack_.empty())
        return false;

    // Pick the innermost rounded clip — it dominates because the matching
    // axis-aligned scissor (set via PushScissor) already bounds the visible
    // region to the intersection.  The pixel-shader SDF only needs to mask
    // the corners of that innermost rounded rect.
    const auto& clip = roundedClipStack_.back();
    if (outInverse) *outInverse = clip.inverse;

    // Project the DIP-space rect through the captured transform, then through
    // dpiScale_ so the coordinates match SV_Position (physical pixels).
    const auto& t = clip.transform;
    auto applyT = [&](float x, float y, float& outX, float& outY) {
        outX = x * t.m11 + y * t.m21 + t.dx;
        outY = x * t.m12 + y * t.m22 + t.dy;
    };

    float x0, y0, x1, y1, x2, y2, x3, y3;
    applyT(clip.x,           clip.y,           x0, y0);
    applyT(clip.x + clip.w,  clip.y,           x1, y1);
    applyT(clip.x + clip.w,  clip.y + clip.h,  x2, y2);
    applyT(clip.x,           clip.y + clip.h,  x3, y3);

    float minX = (std::min)({ x0, x1, x2, x3 });
    float minY = (std::min)({ y0, y1, y2, y3 });
    float maxX = (std::max)({ x0, x1, x2, x3 });
    float maxY = (std::max)({ y0, y1, y2, y3 });

    // Approximate uniform scale from the transform's diagonal.  Border
    // CornerRadius is expressed in DIP-space and is symmetric: a single
    // scale factor (the smaller of X/Y) keeps the SDF circular even when
    // the transform contains slight non-uniform stretch, which avoids
    // ellipse-clip artifacts at corners that should remain circular.
    float scaleX = std::sqrt(t.m11 * t.m11 + t.m12 * t.m12);
    float scaleY = std::sqrt(t.m21 * t.m21 + t.m22 * t.m22);
    if (scaleX <= 0.0f) scaleX = 1.0f;
    if (scaleY <= 0.0f) scaleY = 1.0f;
    float scale = std::min(scaleX, scaleY);

    outRect[0] = minX * dpiScale_;
    outRect[1] = minY * dpiScale_;
    outRect[2] = maxX * dpiScale_;
    outRect[3] = maxY * dpiScale_;
    outRadii[0] = clip.radiusTL * scale * dpiScale_;
    outRadii[1] = clip.radiusTR * scale * dpiScale_;
    outRadii[2] = clip.radiusBR * scale * dpiScale_;
    outRadii[3] = clip.radiusBL * scale * dpiScale_;
    return true;
}

bool D3D12DirectRenderer::ResolveCurrentRoundedClip(float outRect[4], float outRadii[4]) const
{
    return ResolveLiveRoundedClip(outRect, outRadii);
}

void D3D12DirectRenderer::ApplyScissorToVello()
{
    if (!velloRenderer_) return;
    if (!scissorStack_.empty()) {
        auto& s = scissorStack_.top();
        velloRenderer_->SetScissorRect(
            (float)s.left, (float)s.top,
            (float)s.right, (float)s.bottom);
    } else {
        velloRenderer_->ClearScissorRect();
    }
}

bool D3D12DirectRenderer::HasVelloPaths() const
{
    return velloRenderer_ && velloRenderer_->HasWork();
}

void D3D12DirectRenderer::FlushVelloPaths()
{
    if (!velloRenderer_ || !velloRenderer_->HasWork() || !inFrame_) return;

    if (!FlushGraphicsForCompute()) return;  // device lost — frame will abort

    // Pass current scissor to Vello for tile culling
    if (!scissorStack_.empty()) {
        auto& s = scissorStack_.top();
        velloRenderer_->SetScissorRect(
            (float)s.left, (float)s.top,
            (float)s.right, (float)s.bottom);
    } else {
        velloRenderer_->ClearScissorRect();
    }

    bool dispatched = velloRenderer_->Dispatch(commandList_.Get(), currentFrame_);
    if (dispatched) {
        ID3D12Resource* output = velloRenderer_->GetOutputTexture();
        if (output) {
            // Composite Vello output as a full-viewport bitmap with the CURRENT
            // drawOrder. When called mid-frame from FlushVelloIfNeeded, this
            // composite slots between the rect/text/bitmap batches drawn before
            // the flush and those drawn afterwards — opaque content drawn after
            // (e.g. card backgrounds) correctly covers the wave/dot pixels in
            // the Vello bitmap.
            float w = (float)viewportWidth_ / dpiScale_;
            float h = (float)viewportHeight_ / dpiScale_;
            AddBitmap(0, 0, w, h, 1.0f, output, DXGI_FORMAT_R8G8B8A8_UNORM, 1.0f, 1.0f);
        }
        // Force the next Dispatch in this frame to allocate a fresh output
        // texture; the one we just composited is held alive by the
        // BitmapBatchTexture entry AddBitmap pushed above.
        velloRenderer_->ForceNewOutputTexture();
        // Reset Vello's CPU-side scene encoding so subsequent paths in this
        // frame accumulate into a fresh subscene rather than re-rendering the
        // content we just flushed.
        velloRenderer_->BeginFrame(viewportWidth_, viewportHeight_);
    }

    // Drain the resources Dispatch retired (per-frame upload buffers,
    // descriptor heap, configUpload CBV, any default-heap buffers grown
    // mid-frame) onto this slot's frame retired list — even on Dispatch
    // failure paths, because partial command queueing may already have
    // happened before the failure point. Their lifetime is gated by the
    // slot's fence — BeginFrame for this slot won't recycle the command
    // allocator until the GPU has executed the commandList that consumed
    // them, so the ComPtrs are released only when it's verifiably safe.
    auto& fr = frames_[currentFrame_];
    velloRenderer_->DrainRetired(fr.retiredInstanceBuffers);
    velloRenderer_->DrainRetiredHeaps(fr.retiredDescriptorHeaps);
}

// TEST-ONLY (#921 Vello-output regression self-check). Must be called with the
// command list already open (D3D12RenderTarget::DebugForceVelloOutputOrphan stages
// that the same way BeginDraw does). Reproduces the exact 'JaliumVelloOutput' orphan
// the fix addresses, then reports — without needing the D3D12 debug layer — whether
// the output texture survived. Steps mirror FlushVelloPaths' core (above):
//   A. encode a Vello path so Dispatch produces an output texture;
//   B. Dispatch -> capture the live output pointer -> AddBitmap composites it into
//      the bitmap keep-alive list -> ForceNewOutputTexture() drops Vello's own ref
//      (post-fix: parks it on pendingRetiredResources_ -> DrainRetired moves it onto
//      frames_[currentFrame_].retiredInstanceBuffers; pre-fix: bare Reset, so the
//      bitmap entry is the SOLE owner);
//   C. the mid-frame FlushGraphicsForCompute() clears the bitmap keep-alive while the
//      command list still references the texture (the #921 drop site).
// Detection: the captured pointer is still present on retiredInstanceBuffers iff the
// fix parked it. The pointer is only COMPARED, never dereferenced, so the scan is
// safe even when a regression has already freed the texture.
int32_t D3D12DirectRenderer::DebugForceVelloOutputOrphan(int32_t* outAlive) {
    if (outAlive) *outAlive = 0;
    if (!IsCommandListRecording()) return -4;        // caller must have opened the list
    if (!velloEnabled_ || !EnsureVelloRenderer() || !velloRenderer_) return -5;

    // Step A — give Vello real path work (a small triangle), encoded directly on the
    // renderer to bypass FillPath's solid-color stencil fast-path. Tags: 0=LineTo,
    // 5=ClosePath (jalium_triangulate.h); startX/startY is the implicit MoveTo.
    const float cmds[] = { 0.0f, 120.0f, 40.0f, 0.0f, 80.0f, 110.0f, 5.0f };
    velloRenderer_->EncodeFillPath(40.0f, 40.0f, cmds, 7u,
                                   1.0f, 0.0f, 0.0f, 1.0f, /*fillRule NonZero*/ 1u);
    if (!velloRenderer_->HasWork()) return -6;        // path rejected — cannot stage

    // Step B — dispatch (creates outputTexture_), capture it, composite + retire.
    if (!velloRenderer_->Dispatch(commandList_.Get(), currentFrame_)) return -7;
    ID3D12Resource* parked = velloRenderer_->GetOutputTexture();
    if (!parked) return -8;
    const float w = (float)viewportWidth_ / dpiScale_;
    const float h = (float)viewportHeight_ / dpiScale_;
    AddBitmap(0.0f, 0.0f, w, h, 1.0f, parked, DXGI_FORMAT_R8G8B8A8_UNORM, 1.0f, 1.0f);
    velloRenderer_->ForceNewOutputTexture();          // fix parks `parked`; regression bare-Resets it
    velloRenderer_->BeginFrame(viewportWidth_, viewportHeight_);
    auto& fr = frames_[currentFrame_];
    velloRenderer_->DrainRetired(fr.retiredInstanceBuffers);
    velloRenderer_->DrainRetiredHeaps(fr.retiredDescriptorHeaps);

    // Step C — the mid-frame clear that used to free the sole keep-alive while the
    // open command list still referenced the texture.
    if (!FlushGraphicsForCompute()) return -9;        // device lost mid-stage

    // Detection (deterministic, debug-layer-independent): is `parked` still pinned on
    // the fence-gated retired list?
    for (const auto& r : fr.retiredInstanceBuffers) {
        if (r.Get() == parked) { if (outAlive) *outAlive = 1; break; }
    }
    return 0;
}

// ============================================================================
// Upload + Record
// ============================================================================

void D3D12DirectRenderer::UploadInstances()
{
    auto& fr = frames_[currentFrame_];

    // Compute the exact instance footprint (with the same per-stage alignment
    // RecordDrawCommands depends on) and grow the per-frame UPLOAD heap before
    // we touch the mapped pointer.  Padding by kMidFrameReserveBytes lets
    // FlushGraphicsForCompute / DrawCustomShaderEffect / DrawLiquidGlass append
    // their constant buffers in the same buffer without triggering a fallback.
    {
        const size_t rectAlignProbe = sizeof(SdfRectInstance);
        size_t probe = ((uploadBufferOffset_ + rectAlignProbe - 1) / rectAlignProbe) * rectAlignProbe;
        if (!rectInstances_.empty()) {
            probe += rectInstances_.size() * sizeof(SdfRectInstance);
        }
        const size_t textAlignProbe = sizeof(GlyphQuadInstance);
        probe = ((probe + textAlignProbe - 1) / textAlignProbe) * textAlignProbe;
        if (!textInstances_.empty()) {
            probe += textInstances_.size() * sizeof(GlyphQuadInstance);
        }
        const size_t bitmapAlignProbe = sizeof(BitmapQuadInstance);
        probe = ((probe + bitmapAlignProbe - 1) / bitmapAlignProbe) * bitmapAlignProbe;
        if (!bitmapInstances_.empty()) {
            probe += bitmapInstances_.size() * sizeof(BitmapQuadInstance);
        }
        probe = ((probe + 3) / 4) * 4;
        if (!triangleVertices_.empty()) {
            probe += triangleVertices_.size() * sizeof(TriangleVertex);
        }
        // Stencil-path verts are 2×float = 8 bytes/vertex, 8-byte aligned.
        probe = ((probe + 7) / 8) * 8;
        for (const auto& d : stencilPathDraws_) {
            if (d.geom) {
                probe += d.geom->fillTriangles.size()  * sizeof(StencilPathVertex);
                probe += d.geom->coverTriangles.size() * sizeof(StencilPathVertex);
            }
        }
        const size_t requiredBytes = probe + kMidFrameReserveBytes;
        if (!EnsureFrameInstanceCapacity(fr, requiredBytes)) {
            // Growth failure is either OOM or — far more likely mid-frame — a
            // removed device: CreateCommittedResource fails AFTER the old
            // buffer was parked, leaving fr.instanceUploadBuffer null. Latch
            // device loss so EndFrame aborts instead of recording, and drop
            // this flush's batches so RecordDrawCommands (batches_ empty) can
            // never bind a null instance buffer.
            OutputDebugStringA("[D3D12DirectRenderer] FATAL: failed to grow instance upload buffer — dropping frame content\n");
            if (device_ && FAILED(device_->GetDeviceRemovedReason())) {
                frameDeviceLost_ = true;
            }
            rectInstances_.clear();
            textInstances_.clear();
            bitmapInstances_.clear();
            triangleVertices_.clear();
            bitmapTextures_.clear();
            stencilPathDraws_.clear();
            batches_.clear();
            return;
        }
    }

    uint8_t* dst = (uint8_t*)fr.instanceMappedPtr;
    const size_t cap = fr.instanceCapacity;

    // Align start offset to SdfRectInstance stride for StructuredBuffer SRV compatibility
    size_t rectAlign = sizeof(SdfRectInstance);
    size_t offset = ((uploadBufferOffset_ + rectAlign - 1) / rectAlign) * rectAlign;
    size_t rectStartByteOffset = offset;  // save for SRV creation

    // Upload rect instances
    if (!rectInstances_.empty()) {
        size_t rectDataSize = rectInstances_.size() * sizeof(SdfRectInstance);
        if (offset + rectDataSize <= cap) {
            memcpy(dst + offset, rectInstances_.data(), rectDataSize);
            offset += rectDataSize;
        } else {
            OutputDebugStringA("[D3D12DirectRenderer] WARNING: Rect instance buffer overflow — data dropped\n");
        }
    }

    // Upload text instances (after rects, aligned to GlyphQuadInstance stride)
    // NOTE: GlyphQuadInstance is 48 bytes (not power-of-2), use division for alignment
    size_t textAlign = sizeof(GlyphQuadInstance);
    size_t textBufferOffset = ((offset + textAlign - 1) / textAlign) * textAlign;
    textBufferByteOffset_ = textBufferOffset;
    if (!textInstances_.empty()) {
        size_t textDataSize = textInstances_.size() * sizeof(GlyphQuadInstance);
        if (textBufferOffset + textDataSize <= cap) {
            memcpy(dst + textBufferOffset, textInstances_.data(), textDataSize);
            offset = textBufferOffset + textDataSize;
        } else {
            OutputDebugStringA("[D3D12DirectRenderer] WARNING: Text instance buffer overflow — data dropped\n");
        }
    }

    // Upload bitmap instances (after text in same buffer, aligned to struct stride)
    size_t bitmapAlign = sizeof(BitmapQuadInstance);
    size_t bitmapBufferOffset = ((offset + bitmapAlign - 1) / bitmapAlign) * bitmapAlign;
    bitmapBufferByteOffset_ = bitmapBufferOffset;
    if (!bitmapInstances_.empty()) {
        size_t bitmapDataSize = bitmapInstances_.size() * sizeof(BitmapQuadInstance);
        if (bitmapBufferOffset + bitmapDataSize <= cap) {
            memcpy(dst + bitmapBufferOffset, bitmapInstances_.data(), bitmapDataSize);
            offset = bitmapBufferOffset + bitmapDataSize;
        } else {
            OutputDebugStringA("[D3D12DirectRenderer] WARNING: Bitmap instance buffer overflow — data dropped\n");
        }
    }

    // Upload triangle vertices (after bitmaps, 4-byte aligned)
    size_t triBufferOffset = ((offset + 3) / 4) * 4;
    triBufferByteOffset_ = triBufferOffset;
    if (!triangleVertices_.empty()) {
        size_t triDataSize = triangleVertices_.size() * sizeof(TriangleVertex);
        if (triBufferOffset + triDataSize <= cap) {
            memcpy(dst + triBufferOffset, triangleVertices_.data(), triDataSize);
            offset = triBufferOffset + triDataSize;
        } else {
            OutputDebugStringA("[D3D12DirectRenderer] WARNING: Triangle vertex buffer overflow — data dropped\n");
        }
    }

    // ── Stencil-path local-space vertices (8-byte stride float2 POSITION).
    // Each StencilPathDraw owns its own contiguous slice of fillTriangles +
    // coverTriangles, recorded with two DrawInstanced calls in
    // RecordDrawCommands. The vertex shader applies the per-draw transform
    // from b1 root constants, so vertex data is identical across frames
    // even when the path animates — that's the whole point of the cache.
    size_t pathBufferOffset = ((offset + 7) / 8) * 8;
    stencilPathBufferByteOffset_ = pathBufferOffset;
    if (!stencilPathDraws_.empty()) {
        size_t stride = sizeof(StencilPathVertex);
        size_t cursor = pathBufferOffset;
        bool overflow = false;
        for (auto& draw : stencilPathDraws_) {
            const auto& geom = draw.geom;
            if (!geom) continue;
            // fill vertex slice
            size_t fillBytes  = geom->fillTriangles.size()  * stride;
            size_t coverBytes = geom->coverTriangles.size() * stride;
            if (cursor + fillBytes + coverBytes > cap) {
                overflow = true;
                break;
            }
            if (fillBytes) {
                memcpy(dst + cursor, geom->fillTriangles.data(), fillBytes);
                draw.fillVbOffsetBytes = cursor;
                draw.fillVertexCount   = (UINT)geom->fillTriangles.size();
                cursor += fillBytes;
            }
            if (coverBytes) {
                memcpy(dst + cursor, geom->coverTriangles.data(), coverBytes);
                draw.coverVbOffsetBytes = cursor;
                draw.coverVertexCount   = (UINT)geom->coverTriangles.size();
                cursor += coverBytes;
            }
        }
        if (overflow) {
            OutputDebugStringA("[D3D12DirectRenderer] WARNING: Stencil path vertex buffer overflow — paths truncated\n");
        }
        offset = cursor;
    }

    // ── Descriptor ring buffer ──
    // Save upload buffer write position for next flush
    uploadBufferOffset_ = offset;
    // Each flush allocates descriptor slots from the SRV heap.
    // Base layout: [0-1] rect SRV + atlas, [4-5] text SRV + atlas
    // Per-bitmap/snapshot batch: 2 extra slots (instance SRV + texture)
    // Per-stencil-path batch: at most one extra resolve SRV slot. Path slots
    //   live AFTER the bitmap range so the bitmap descriptor pairs stay
    //   stride-2 aligned no matter how many resolves run between them.
    // This avoids overwriting descriptors that are still referenced by earlier draws
    // on the same command list (GPU hasn't executed them yet).
    UINT numBitmapSlots = (UINT)(bitmapTextures_.size() + CountSnapshotBlitBatches()) * 2;
    UINT numPathResolveSlots = (UINT)stencilPathDraws_.size();  // upper bound: 1 per path
    UINT kSlotsPerFlush = 8 + numBitmapSlots + numPathResolveSlots;
    // Stash path-resolve region size so RecordDrawCommands can place its
    // ring counter past the bitmap region.
    lastFlushPathResolveBase_ = 8 + numBitmapSlots;
    lastFlushPathResolveCount_ = numPathResolveSlots;
    // Check for overflow BEFORE allocation to avoid writing past frame region boundary
    UINT frameSrvBase = currentFrame_ * frameSrvRegionSize_;
    UINT frameSrvEnd = frameSrvBase + frameSrvRegionSize_;
    if (srvAllocOffset_ + kSlotsPerFlush > frameSrvEnd) {
        // Out of descriptor space.  Wrapping would overwrite descriptors that are
        // still referenced by earlier draw commands in this command list (not yet
        // executed), causing the GPU to read invalid data → device lost.
        // Truncate: drop the draws that don't fit.  This may cause visual glitches
        // for the remainder of this frame, but avoids device removal.
        OutputDebugStringA("[D3D12DirectRenderer] SRV descriptor ring overflow — truncating draws for this frame\n");
        rectInstances_.clear();
        textInstances_.clear();
        bitmapInstances_.clear();
        triangleVertices_.clear();
        bitmapTextures_.clear();
        stencilPathDraws_.clear();
        batches_.clear();
        return;
    }
    UINT baseSlot = srvAllocOffset_;
    srvAllocOffset_ += kSlotsPerFlush;
    lastFlushSrvBase_ = baseSlot;
    lastFlushSlotsPerFlush_ = kSlotsPerFlush;

    auto srvCpuBase = srvHeap_->GetCPUDescriptorHandleForHeapStart();
    srvCpuBase.ptr += baseSlot * srvDescriptorSize_;

    // Slot base+0: rect instances (at the aligned start of this flush's data)
    size_t rectBufferByteOffset = rectStartByteOffset;
    if (!rectInstances_.empty()) {
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Buffer.FirstElement = rectBufferByteOffset / sizeof(SdfRectInstance);
        srvDesc.Buffer.NumElements = (UINT)rectInstances_.size();
        srvDesc.Buffer.StructureByteStride = sizeof(SdfRectInstance);
        srvDesc.Format = DXGI_FORMAT_UNKNOWN;
        auto handle = srvCpuBase;
        device_->CreateShaderResourceView(fr.instanceUploadBuffer.Get(), &srvDesc, handle);
    }

    // Slot base+1: glyph atlas texture
    {
        auto handle = srvCpuBase;
        handle.ptr += srvDescriptorSize_;
        if (glyphAtlas_) {
            glyphAtlas_->FlushToGpu(commandList_.Get());
            D3D12_SHADER_RESOURCE_VIEW_DESC atlasSrv = {};
            atlasSrv.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
            atlasSrv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            atlasSrv.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            atlasSrv.Texture2D.MipLevels = 1;
            device_->CreateShaderResourceView(glyphAtlas_->GetAtlasResource(), &atlasSrv, handle);
        }
    }

    // Slot base+4: text instances
    if (!textInstances_.empty()) {
        auto handle = srvCpuBase;
        handle.ptr += 4 * srvDescriptorSize_;
        D3D12_SHADER_RESOURCE_VIEW_DESC textSrv = {};
        textSrv.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        textSrv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        textSrv.Buffer.FirstElement = textBufferByteOffset_ / sizeof(GlyphQuadInstance);
        textSrv.Buffer.NumElements = (UINT)textInstances_.size();
        textSrv.Buffer.StructureByteStride = sizeof(GlyphQuadInstance);
        textSrv.Format = DXGI_FORMAT_UNKNOWN;
        device_->CreateShaderResourceView(fr.instanceUploadBuffer.Get(), &textSrv, handle);

        // Slot base+5: glyph atlas (for text descriptor table)
        auto atlasHandle = handle;
        atlasHandle.ptr += srvDescriptorSize_;
        if (glyphAtlas_) {
            D3D12_SHADER_RESOURCE_VIEW_DESC atlasSrv2 = {};
            atlasSrv2.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
            atlasSrv2.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            atlasSrv2.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            atlasSrv2.Texture2D.MipLevels = 1;
            device_->CreateShaderResourceView(glyphAtlas_->GetAtlasResource(), &atlasSrv2, atlasHandle);
        }
    }
}

void D3D12DirectRenderer::RecordDrawCommands()
{
    if (batches_.empty()) return;
    // Latched device loss (an earlier flush this frame detected removal):
    // recording SRVs/draws below would reach the torn-down UMD.
    if (frameDeviceLost_) return;
    // Defense-in-depth: a flush whose instance buffer died (removed device
    // during mid-frame growth) drops its batches above, so this cannot trip —
    // but binding a null instance buffer is the exact signature of the
    // GPU-switch AV inside the UMD, so refuse outright.
    if (!frames_[currentFrame_].instanceUploadBuffer) return;

    // Set root signature + descriptor heap
    commandList_->SetGraphicsRootSignature(rootSignature_.Get());
    ID3D12DescriptorHeap* heaps[] = { srvHeap_.Get() };
    commandList_->SetDescriptorHeaps(1, heaps);

    // Write current frame constants to a fresh 256-byte slot in the ring buffer.
    // Each flush gets its own slot so offscreen and main-RT draws see correct constants
    // (avoids the race where a single CBV upload buffer gets overwritten mid-frame).
    auto& fr = frames_[currentFrame_];
    UINT cbOffset = fr.constantsRingOffset;
    if (cbOffset + 256 > kConstantsRingSize) cbOffset = 0;  // wrap
    memcpy((uint8_t*)fr.constantsMappedPtr + cbOffset, &currentFrameConstants_, sizeof(DirectFrameConstants));
    commandList_->SetGraphicsRootConstantBufferView(0,
        fr.constantsBuffer->GetGPUVirtualAddress() + cbOffset);
    fr.constantsRingOffset = cbOffset + 256;

    // Bind instance SRV (descriptor table) — use current flush's descriptor region
    UINT descBase = lastFlushSrvBase_;
    auto srvGpuBase = srvHeap_->GetGPUDescriptorHandleForHeapStart();
    srvGpuBase.ptr += descBase * srvDescriptorSize_;
    commandList_->SetGraphicsRootDescriptorTable(1, srvGpuBase);

    // Bitmap/snapshot batches each get their own unique descriptor pair starting at slot 8+
    UINT nextBitmapDescSlot = descBase + 8;  // first bitmap-specific slot

    // Set topology
    commandList_->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

    // Build lookup: batchIndex → bitmapTextures_ index for bitmap batches
    size_t nextBitmapTexIdx = 0;

    // Draw in painter's order (batches are already in order)
    DrawBatchType currentPSO = DrawBatchType::SdfRect;
    commandList_->SetPipelineState(sdfRectPSO_.Get());

    // ── Path-pipeline state machine ────────────────────────────────────
    // Stencil-then-cover paths target an 8× MSAA scratch RT, not the back
    // buffer. We enter "path mode" lazily on the first path batch (clear
    // MSAA color + stencil, bind scratch RT/DSV, switch root signature),
    // stay there for runs of consecutive path batches, and flush back to
    // the swap-chain RT (resolve → blit) the moment a non-path batch
    // arrives or the loop ends. This keeps the rest of the renderer
    // unchanged: bitmap / text / rect still draw 1× directly into the
    // swap chain.
    bool pathActiveOnMsaa = false;          // currently rendering paths into MSAA scratch?
    bool pathScratchClearedThisFrame = false;  // ClearRTV/CDS done this frame on scratch?

    // We collect SRV slots from the per-flush ring for each resolve blit
    // we have to do. Path resolves live in their own region (placed past
    // the bitmap descriptors by UploadInstances) so bitmap descriptor
    // pairs stay stride-2 aligned even when paths interleave.
    UINT pathResolveCursor = descBase + lastFlushPathResolveBase_;
    UINT pathResolveBudget = lastFlushPathResolveCount_;
    auto allocResolveSrvSlot = [&]() -> UINT {
        if (pathResolveBudget == 0) {
            // Budget exhausted (UploadInstances under-counted, or extra path
            // batches were appended after — defensive guard so we don't
            // collide with a bitmap descriptor). Skip the blit; visual is
            // wrong only for that path, no descriptor corruption.
            return UINT_MAX;
        }
        UINT slot = pathResolveCursor;
        pathResolveCursor += 1;
        pathResolveBudget -= 1;
        return slot;
    };

    // Path render extent — the viewport the stencil-path arm rasterizes and the
    // resolve blits through, and the divisor the path VS turns positions into NDC
    // with. Inside an element-effect capture the path must register with the rest
    // of the captured subtree, which Begin*Capture draws at the pw×ph capture
    // viewport — so the path uses the capture extent, NOT the window viewport.
    // This is what makes an OVERSIZED capture (pw > viewportWidth_ or
    // ph > viewportHeight_ — a maximized/full-window element plus effect padding,
    // or a retained-animation container larger than the window) render the path
    // UNTRUNCATED and aligned with its non-path siblings instead of clipping it at
    // the window edge. The MSAA scratch + resolve grow to cover this extent
    // (grow-only; EnsureStencilDepthBuffer) and the blit samples only the used
    // sub-region via a uv-scale, so the common window-sized case never churns
    // resources and renders bit-identically (uv-scale 1.0). This extent is ALSO
    // the default (no-clip) scissor for every batch in this flush — see the two
    // RSSetScissorRects else-branches below. Begin*Capture clears the scissor
    // stack, so an un-clipped batch inside a capture (path OR non-path) carries
    // hasScissor=false and falls into that default; it must be allowed to fill the
    // whole pw×ph capture target, not just the window, or OVERSIZED non-path
    // content would truncate at the window edge and de-register from the
    // now-untruncated path. In non-capture rendering this is exactly the window
    // viewport, so those scissors are byte-identical to before. The capture state
    // is constant for this whole flush, so compute the extent once. The >0 guard
    // falls back to the window viewport if a capture is somehow active with a
    // degenerate zero extent.
    const bool pathInCapture = (inOffscreenCapture_ || inRetainedCapture_);
    const UINT pathContentW =
        (pathInCapture && captureViewportW_ > 0) ? captureViewportW_ : viewportWidth_;
    const UINT pathContentH =
        (pathInCapture && captureViewportH_ > 0) ? captureViewportH_ : viewportHeight_;

    auto enterPathMode = [&]() {
        if (pathActiveOnMsaa) return;
        if (!EnsureStencilDepthBuffer(pathContentW, pathContentH)) return;
        // [#921] The pathMsaa* scratch is about to be bound into the open command
        // list; block any further mid-frame regrow of it (see
        // EnsureStencilDepthBuffer's pathMsaaUsedThisFrame_ guard).
        pathMsaaUsedThisFrame_ = true;

        // Stencil-then-cover paths run in their own MSAA scratch RT and don't
        // go through the PSO-switch arm below, so tag the timing category
        // here on the path-mode entry.
        MarkGpuTimingPoint(GpuTimingCategory::Path);

        // Transition MSAA color to RENDER_TARGET if it isn't already.
        if (pathMsaaColorState_ != D3D12_RESOURCE_STATE_RENDER_TARGET) {
            auto b = MakeTransitionBarrier(pathMsaaColor_.Get(),
                pathMsaaColorState_, D3D12_RESOURCE_STATE_RENDER_TARGET);
            commandList_->ResourceBarrier(1, &b);
            pathMsaaColorState_ = D3D12_RESOURCE_STATE_RENDER_TARGET;
        }

        auto rtvHandle = pathMsaaRtvHeap_->GetCPUDescriptorHandleForHeapStart();
        auto dsvHandle = stencilDsvHeap_->GetCPUDescriptorHandleForHeapStart();
        commandList_->OMSetRenderTargets(1, &rtvHandle, FALSE, &dsvHandle);

        // The stencil-path VS computes NDC from the path-content root constants
        // (rootConsts[12..15] = pathContentW/pathContentH, below) and rasterizes
        // through a viewport of the same extent, so a path vertex at content-local
        // pixel (px,py) lands at scratch texel (px,py). The ambient viewport here
        // is whatever the last batch left bound (the pw×ph capture rect during a
        // capture); force the path-content viewport so the path always lands at
        // content-local pixels [0,W)×[0,H) — the texels the resolve blit copies
        // into the target. In normal rendering pathContentW/H is the window
        // viewport (this is a no-op then); inside an element-effect capture it is
        // the capture extent, so an OVERSIZED capture rasterizes untruncated — the
        // scratch was grown to cover it (EnsureStencilDepthBuffer above) and the
        // resolve later samples just this [0,W)×[0,H) sub-region.
        D3D12_VIEWPORT pathViewport = { 0, 0, (float)pathContentW, (float)pathContentH, 0, 1 };
        commandList_->RSSetViewports(1, &pathViewport);

        // On the first path batch of the frame, clear MSAA color to
        // transparent and stencil to 0 so old contents don't leak.
        if (!pathScratchClearedThisFrame) {
            float clearColor[4] = { 0, 0, 0, 0 };
            commandList_->ClearRenderTargetView(rtvHandle, clearColor, 0, nullptr);
            commandList_->ClearDepthStencilView(dsvHandle,
                D3D12_CLEAR_FLAG_STENCIL, 1.0f, 0, 0, nullptr);
            pathScratchClearedThisFrame = true;
        }

        commandList_->SetGraphicsRootSignature(stencilPathRootSig_.Get());
        commandList_->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        pathActiveOnMsaa = true;
        currentPSO = DrawBatchType::StencilPath;
    };

    auto exitPathMode = [&]() {
        if (!pathActiveOnMsaa) return;

        // Resolve 8× MSAA color → 1× resolve texture.
        // MSAA color is currently RENDER_TARGET; resolve source must be
        // RESOLVE_SOURCE. Resolve dest must be RESOLVE_DEST.
        D3D12_RESOURCE_BARRIER preBarriers[2];
        preBarriers[0] = MakeTransitionBarrier(pathMsaaColor_.Get(),
            D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_RESOLVE_SOURCE);
        preBarriers[1] = MakeTransitionBarrier(pathResolveTexture_.Get(),
            pathResolveTexState_, D3D12_RESOURCE_STATE_RESOLVE_DEST);
        commandList_->ResourceBarrier(2, preBarriers);

        commandList_->ResolveSubresource(
            pathResolveTexture_.Get(), 0,
            pathMsaaColor_.Get(), 0,
            DXGI_FORMAT_R16G16B16A16_FLOAT);

        D3D12_RESOURCE_BARRIER postBarriers[2];
        postBarriers[0] = MakeTransitionBarrier(pathMsaaColor_.Get(),
            D3D12_RESOURCE_STATE_RESOLVE_SOURCE, D3D12_RESOURCE_STATE_RENDER_TARGET);
        postBarriers[1] = MakeTransitionBarrier(pathResolveTexture_.Get(),
            D3D12_RESOURCE_STATE_RESOLVE_DEST, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        commandList_->ResourceBarrier(2, postBarriers);
        pathMsaaColorState_  = D3D12_RESOURCE_STATE_RENDER_TARGET;
        pathResolveTexState_ = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;

        // Capture-aware blit destination. During an element-effect offscreen
        // capture (or a retained-layer realize), the active render target is the
        // capture texture, NOT the swap-chain back buffer — its RTV and capture
        // viewport were stashed by Begin*Capture. Resolving the path scratch to
        // the back buffer here would both (a) drop the path from the captured
        // content and (b) leave a displaced stray copy on the main window. Pick
        // the capture RTV whenever a capture is in flight so the path composites
        // into the same texture as the rest of the captured subtree. (Guarding
        // here — inside exitPathMode rather than in End*Capture — makes the path
        // RESOLVE follow the capture no matter which FlushGraphicsForCompute caller
        // triggers it mid-capture. Note: nested compute effects (BlurRegion /
        // LiquidGlass / snapshot) that themselves rebind the back buffer mid-capture
        // are a separate, pre-existing gap not addressed here.)
        const bool inCapture = (inOffscreenCapture_ || inRetainedCapture_);
        const D3D12_CPU_DESCRIPTOR_HANDLE blitRtv =
            inCapture ? captureRtv_ : GetSwapChainRtvHandle();

        // Allocate one SRV slot for the resolve texture and write the SRV.
        UINT srvSlot = allocResolveSrvSlot();
        if (srvSlot != UINT_MAX) {
            auto srvCpu = srvHeap_->GetCPUDescriptorHandleForHeapStart();
            srvCpu.ptr += srvSlot * srvDescriptorSize_;
            D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
            srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
            srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            srvDesc.Format = DXGI_FORMAT_R16G16B16A16_FLOAT;
            srvDesc.Texture2D.MipLevels = 1;
            device_->CreateShaderResourceView(pathResolveTexture_.Get(), &srvDesc, srvCpu);
            auto srvGpu = srvHeap_->GetGPUDescriptorHandleForHeapStart();
            srvGpu.ptr += srvSlot * srvDescriptorSize_;

            // Bind the blit target (no DSV) and run the fullscreen-triangle blit
            // at the path-content viewport. enterPathMode rasterized the path
            // through that same extent, so it sits at content-local texels
            // [0,W)×[0,H) of the resolve texture; a content-sized blit lands it at
            // the matching [0,W)×[0,H) texels of the target. The resolve texture is
            // grow-only and may be LARGER than the content (an earlier oversized
            // capture grew it), so the uv-scale below samples only its used corner
            // — without it a bigger-than-content texture would squish across the
            // viewport. In capture mode W×H is the pw×ph capture viewport, so the
            // blit fills the offscreen RT / retained layer exactly (no longer
            // relying on RT-bounds clipping); outside capture it is the window.
            commandList_->OMSetRenderTargets(1, &blitRtv, FALSE, nullptr);
            D3D12_VIEWPORT blitViewport = { 0, 0, (float)pathContentW, (float)pathContentH, 0, 1 };
            D3D12_RECT blitScissor = { 0, 0, (LONG)pathContentW, (LONG)pathContentH };
            commandList_->RSSetViewports(1, &blitViewport);
            commandList_->RSSetScissorRects(1, &blitScissor);

            commandList_->SetGraphicsRootSignature(pathResolveRootSig_.Get());
            ID3D12DescriptorHeap* heaps[] = { srvHeap_.Get() };
            commandList_->SetDescriptorHeaps(1, heaps);
            commandList_->SetGraphicsRootDescriptorTable(0, srvGpu);
            // uv-scale = content / allocated texture: samples only the used
            // top-left sub-region of the grow-only resolve texture. Exactly (1,1)
            // when the texture is content-sized (the common, never-grown case),
            // reproducing the original 1:1 blit. pathMsaaWidth_/Height_ are the
            // allocated texels (≥ content) set by EnsureStencilDepthBuffer.
            const float resolveUvScale[4] = {
                (pathMsaaWidth_  > 0) ? (float)pathContentW / (float)pathMsaaWidth_  : 1.0f,
                (pathMsaaHeight_ > 0) ? (float)pathContentH / (float)pathMsaaHeight_ : 1.0f,
                0.0f, 0.0f,
            };
            commandList_->SetGraphicsRoot32BitConstants(1, 4, resolveUvScale, 0);
            commandList_->SetPipelineState(psoPathResolve_.Get());
            commandList_->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            commandList_->IASetVertexBuffers(0, 0, nullptr);
            commandList_->DrawInstanced(3, 1, 0, 0);
        }
        // else: no SRV budget — drop this path run's blit, but STILL fall through
        // to the restore tail below. Returning early here is the old latent bug:
        // it left the stencil-path root signature + 8× MSAA scratch RT (with DSV)
        // bound under the 1× sdfRect PSO, a root-sig/PSO/sample-count mismatch that
        // corrupts or device-removes the next batch.

        // ── Restore tail (runs whether or not the blit happened) ──
        // Re-emit the main root signature + this flush's CBV + instance descriptor
        // table; root constants and descriptor-table bindings don't survive a root
        // signature switch, so the next non-path batch needs them re-bound. These
        // are correct in capture mode too: cbOffset/srvGpuBase reference the capture
        // flush's own sub-region constants and instances.
        commandList_->SetGraphicsRootSignature(rootSignature_.Get());
        commandList_->SetGraphicsRootConstantBufferView(0,
            fr.constantsBuffer->GetGPUVirtualAddress() + cbOffset);
        commandList_->SetGraphicsRootDescriptorTable(1, srvGpuBase);

        // Re-bind the render target + viewport for the subsequent non-path batches.
        // The batch loop re-sets scissor per batch but inherits RT + viewport, so
        // both must be correct here. In capture mode: the capture RT at the pw×ph
        // capture viewport. Otherwise: the back buffer at the window viewport.
        // When the blit ran it already left exactly this RT + content viewport
        // bound (pathContentW/H == captureViewportW_/H_ in capture, == the window
        // viewport otherwise), so this is a redundant re-bind there — but it is
        // REQUIRED for the dropped-blit branch above, which falls through with the
        // MSAA scratch RT + DSV and the path viewport still bound.
        if (inCapture) {
            commandList_->OMSetRenderTargets(1, &captureRtv_, FALSE, nullptr);
            D3D12_VIEWPORT capViewport = { 0, 0, (float)captureViewportW_, (float)captureViewportH_, 0, 1 };
            commandList_->RSSetViewports(1, &capViewport);
        } else {
            auto bbRtv = GetSwapChainRtvHandle();
            commandList_->OMSetRenderTargets(1, &bbRtv, FALSE, nullptr);
            D3D12_VIEWPORT fullViewport = { 0, 0, (float)viewportWidth_, (float)viewportHeight_, 0, 1 };
            commandList_->RSSetViewports(1, &fullViewport);
        }

        // Reset state-machine knobs so the next path batch in this frame re-binds
        // MSAA and re-clears the scratch (otherwise earlier paths, already resolved
        // and blitted, would re-resolve and over-blend their alpha), and force a PSO
        // re-select so the next non-path batch re-emits its own PSO + table.
        pathScratchClearedThisFrame = false;
        pathActiveOnMsaa = false;
        currentPSO = DrawBatchType::SdfRect;
        commandList_->SetPipelineState(sdfRectPSO_.Get());
    };

    for (size_t batchIdx = 0; batchIdx < batches_.size(); batchIdx++) {
        const auto& batch = batches_[batchIdx];

        if (batch.type == DrawBatchType::StencilPath) {
            if (!stencilPathReady_) continue;
            enterPathMode();
            if (!pathActiveOnMsaa) continue;  // EnsureStencilDepthBuffer failed

            // Apply scissor for this path. The default (no per-batch clip) must
            // span the path-content extent, NOT the window — otherwise an oversized
            // capture's path is re-truncated at the window edge by the scissor test
            // even though the viewport (line above) and MSAA scratch cover the full
            // pathContentW×pathContentH. In non-capture rendering pathContentW/H IS
            // the window viewport, so this is unchanged there.
            if (batch.hasScissor) {
                if (batch.scissor.left >= batch.scissor.right ||
                    batch.scissor.top >= batch.scissor.bottom)
                    continue;
                commandList_->RSSetScissorRects(1, &batch.scissor);
            } else {
                D3D12_RECT fullScissor = { 0, 0, (LONG)pathContentW, (LONG)pathContentH };
                commandList_->RSSetScissorRects(1, &fullScissor);
            }

            uint32_t drawIdx = batch.instanceOffset;
            if (drawIdx >= stencilPathDraws_.size()) continue;
            const auto& draw = stencilPathDraws_[drawIdx];
            if (draw.fillVertexCount == 0 || draw.coverVertexCount == 0) continue;

            // 16 dwords of root constants (transform/color/screen).
            float rootConsts[16] = {};
            rootConsts[0] = draw.m11; rootConsts[1] = draw.m12;
            rootConsts[2] = draw.m21; rootConsts[3] = draw.m22;
            rootConsts[4] = draw.dx;  rootConsts[5] = draw.dy;
            rootConsts[8]  = draw.r;
            rootConsts[9]  = draw.g;
            rootConsts[10] = draw.b;
            rootConsts[11] = draw.a;
            rootConsts[12] = (float)pathContentW;
            rootConsts[13] = (float)pathContentH;
            rootConsts[14] = (pathContentW > 0) ? 1.0f / (float)pathContentW : 0.0f;
            rootConsts[15] = (pathContentH > 0) ? 1.0f / (float)pathContentH : 0.0f;
            commandList_->SetGraphicsRoot32BitConstants(0, 16, rootConsts, 0);

            // Pass 1 — stencil fill.
            commandList_->OMSetStencilRef(0);
            commandList_->SetPipelineState(
                draw.fillRule == 1 ? psoStencilFillNonZero_.Get()
                                   : psoStencilFillEvenOdd_.Get());
            D3D12_VERTEX_BUFFER_VIEW fillVbv = {};
            fillVbv.BufferLocation = fr.instanceUploadBuffer->GetGPUVirtualAddress() + draw.fillVbOffsetBytes;
            fillVbv.SizeInBytes    = draw.fillVertexCount * (UINT)sizeof(StencilPathVertex);
            fillVbv.StrideInBytes  = sizeof(StencilPathVertex);
            commandList_->IASetVertexBuffers(0, 1, &fillVbv);
            commandList_->DrawInstanced(draw.fillVertexCount, 1, 0, 0);

            // Pass 2 — cover.
            commandList_->SetPipelineState(psoStencilCover_.Get());
            D3D12_VERTEX_BUFFER_VIEW coverVbv = {};
            coverVbv.BufferLocation = fr.instanceUploadBuffer->GetGPUVirtualAddress() + draw.coverVbOffsetBytes;
            coverVbv.SizeInBytes    = draw.coverVertexCount * (UINT)sizeof(StencilPathVertex);
            coverVbv.StrideInBytes  = sizeof(StencilPathVertex);
            commandList_->IASetVertexBuffers(0, 1, &coverVbv);
            commandList_->DrawInstanced(draw.coverVertexCount, 1, 0, 0);
            continue;
        }

        // Non-stencil-path batch: if we were rendering paths, resolve + blit
        // them onto the back buffer first so painter's order is preserved.
        if (pathActiveOnMsaa) {
            exitPathMode();
        }

        // Switch PSO if needed
        if (batch.type != currentPSO) {
            currentPSO = batch.type;
            // Tag the GPU work that's about to be issued so DevTools can
            // attribute time to the right category. Map DrawBatchType → the
            // coarser timing category bucket.
            GpuTimingCategory cat;
            switch (batch.type) {
            case DrawBatchType::SdfRect:
            case DrawBatchType::Ellipse:
            case DrawBatchType::Line:
            case DrawBatchType::PunchRect:
                cat = GpuTimingCategory::SdfRect;
                break;
            case DrawBatchType::Text:        cat = GpuTimingCategory::Text;    break;
            case DrawBatchType::Bitmap:
            case DrawBatchType::SnapshotBlit:
                cat = GpuTimingCategory::Bitmap;
                break;
            case DrawBatchType::Triangle:
            case DrawBatchType::StencilPath:
                cat = GpuTimingCategory::Path;
                break;
            case DrawBatchType::LiquidGlass: cat = GpuTimingCategory::LiquidGlass; break;
            default:                          cat = GpuTimingCategory::Other;       break;
            }
            MarkGpuTimingPoint(cat);

            switch (batch.type) {
            case DrawBatchType::SdfRect:
            case DrawBatchType::Ellipse:
            case DrawBatchType::Line:
                // Ellipse and Line reuse SdfRect PSO
                commandList_->SetPipelineState(sdfRectPSO_.Get());
                commandList_->SetGraphicsRootDescriptorTable(1, srvGpuBase);
                break;
            case DrawBatchType::Text:
                // Deformed (transform-scaled) text uses the bilinear text PSO for
                // smooth animated sub-pixel positioning; 1:1 text uses the crisp
                // point PSO. (Batches with different smoothText never coalesce.)
                commandList_->SetPipelineState(
                    batch.smoothText ? textSmoothPSO_.Get() : textPSO_.Get());
                {
                    auto textSrvGpu = srvGpuBase;
                    textSrvGpu.ptr += 4 * srvDescriptorSize_;
                    commandList_->SetGraphicsRootDescriptorTable(1, textSrvGpu);
                }
                break;
            case DrawBatchType::Bitmap:
                commandList_->SetPipelineState(bitmapPSO_.Get());
                break;
            case DrawBatchType::PunchRect:
                commandList_->SetPipelineState(copyBlendPSO_.Get());
                commandList_->SetGraphicsRootDescriptorTable(1, srvGpuBase);
                break;
            case DrawBatchType::SnapshotBlit:
                commandList_->SetPipelineState(bitmapPSO_.Get());
                break;
            }
        }

        // For snapshot blit batches, use the snapshot texture as bitmap source.
        // Each batch gets its own unique descriptor pair to avoid overwrite races.
        if (batch.type == DrawBatchType::SnapshotBlit && snapshotTexture_) {
            auto& fr = frames_[currentFrame_];
            UINT bmpSlot = nextBitmapDescSlot;
            nextBitmapDescSlot += 2;

            auto bmpSrvCpuH = srvHeap_->GetCPUDescriptorHandleForHeapStart();
            bmpSrvCpuH.ptr += bmpSlot * srvDescriptorSize_;

            D3D12_SHADER_RESOURCE_VIEW_DESC instSrv = {};
            instSrv.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
            instSrv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            instSrv.Buffer.FirstElement = bitmapBufferByteOffset_ / sizeof(BitmapQuadInstance);
            instSrv.Buffer.NumElements = (UINT)bitmapInstances_.size();
            instSrv.Buffer.StructureByteStride = sizeof(BitmapQuadInstance);
            instSrv.Format = DXGI_FORMAT_UNKNOWN;
            device_->CreateShaderResourceView(fr.instanceUploadBuffer.Get(), &instSrv, bmpSrvCpuH);

            auto texSrvCpu = bmpSrvCpuH;
            texSrvCpu.ptr += srvDescriptorSize_;
            D3D12_SHADER_RESOURCE_VIEW_DESC texSrv = {};
            texSrv.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
            texSrv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            texSrv.Format = swapChainFormat_;
            texSrv.Texture2D.MipLevels = 1;
            device_->CreateShaderResourceView(snapshotTexture_.Get(), &texSrv, texSrvCpu);

            auto bmpSrvGpuH = srvHeap_->GetGPUDescriptorHandleForHeapStart();
            bmpSrvGpuH.ptr += bmpSlot * srvDescriptorSize_;
            commandList_->SetGraphicsRootDescriptorTable(1, bmpSrvGpuH);
        }

        // For bitmap batches, create unique SRVs per batch for the bitmap instance buffer + texture.
        // Each batch gets its own descriptor pair to avoid overwrite races.
        if (batch.type == DrawBatchType::Bitmap && nextBitmapTexIdx < bitmapTextures_.size()) {
            const auto& texInfo = bitmapTextures_[nextBitmapTexIdx++];
            auto& fr = frames_[currentFrame_];
            UINT bmpSlot = nextBitmapDescSlot;
            nextBitmapDescSlot += 2;

            auto bmpSrvCpu = srvHeap_->GetCPUDescriptorHandleForHeapStart();
            bmpSrvCpu.ptr += bmpSlot * srvDescriptorSize_;

            // t0: bitmap instances StructuredBuffer (offset into the shared upload buffer)
            D3D12_SHADER_RESOURCE_VIEW_DESC instSrv = {};
            instSrv.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
            instSrv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            instSrv.Buffer.FirstElement = bitmapBufferByteOffset_ / sizeof(BitmapQuadInstance);
            instSrv.Buffer.NumElements = (UINT)bitmapInstances_.size();
            instSrv.Buffer.StructureByteStride = sizeof(BitmapQuadInstance);
            instSrv.Format = DXGI_FORMAT_UNKNOWN;
            device_->CreateShaderResourceView(fr.instanceUploadBuffer.Get(), &instSrv, bmpSrvCpu);

            // t1: bitmap texture
            auto texSrvCpu = bmpSrvCpu;
            texSrvCpu.ptr += srvDescriptorSize_;
            D3D12_SHADER_RESOURCE_VIEW_DESC texSrv = {};
            texSrv.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
            texSrv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            texSrv.Format = texInfo.format;
            texSrv.Texture2D.MipLevels = 1;
            device_->CreateShaderResourceView(texInfo.textureResource.Get(), &texSrv, texSrvCpu);

            // Bind the bitmap descriptor table (unique per batch)
            auto bmpSrvGpuB = srvHeap_->GetGPUDescriptorHandleForHeapStart();
            bmpSrvGpuB.ptr += bmpSlot * srvDescriptorSize_;
            commandList_->SetGraphicsRootDescriptorTable(1, bmpSrvGpuB);
        }

        // Apply per-batch scissor rect for clipping. The default (no per-batch
        // clip) spans the content extent, not the window: inside an oversized
        // capture non-path content must reach the full pw×ph capture target so it
        // registers with the (now-untruncated) path content; clipping it at the
        // window edge would leave path-vs-non-path mismatched in [viewport, pw).
        // pathContentW/H == the window viewport outside a capture (unchanged there).
        if (batch.hasScissor) {
            // Skip batches with empty scissor rects (fully clipped elements)
            if (batch.scissor.left >= batch.scissor.right || batch.scissor.top >= batch.scissor.bottom)
                continue;
            commandList_->RSSetScissorRects(1, &batch.scissor);
        } else {
            D3D12_RECT fullScissor = { 0, 0, (LONG)pathContentW, (LONG)pathContentH };
            commandList_->RSSetScissorRects(1, &fullScissor);
        }

        // Per-batch rounded-rect clip — written to b2 (root parameter 3).
        // The four batched pixel shaders read the cbuffer and discard fragments
        // outside the SDF.  Layout matches RoundedClipConstants in the shaders.
        // Per-corner radii are TL, TR, BR, BL in physical pixels.
        struct RoundedClipB2 {
            uint32_t hasRoundedClip;
            uint32_t inverse;
            uint32_t _pad[2];
            float    rect[4];
            float    cornerRadii[4]; // TL, TR, BR, BL
        } clipB2 = {};
        clipB2.hasRoundedClip = batch.hasRoundedClip ? 1u : 0u;
        clipB2.inverse = batch.roundedClipInverse ? 1u : 0u;
        if (batch.hasRoundedClip) {
            clipB2.rect[0] = batch.roundedClipRect[0];
            clipB2.rect[1] = batch.roundedClipRect[1];
            clipB2.rect[2] = batch.roundedClipRect[2];
            clipB2.rect[3] = batch.roundedClipRect[3];
            clipB2.cornerRadii[0] = batch.roundedClipCornerRadii[0];
            clipB2.cornerRadii[1] = batch.roundedClipCornerRadii[1];
            clipB2.cornerRadii[2] = batch.roundedClipCornerRadii[2];
            clipB2.cornerRadii[3] = batch.roundedClipCornerRadii[3];
        }
        commandList_->SetGraphicsRoot32BitConstants(3, 12, &clipB2, 0);

        // Triangle batches use vertex buffer directly (not StructuredBuffer)
        if (batch.type == DrawBatchType::Triangle) {
            commandList_->SetPipelineState(trianglePSO_.Get());
            auto& fr = frames_[currentFrame_];
            D3D12_VERTEX_BUFFER_VIEW vbv = {};
            vbv.BufferLocation = fr.instanceUploadBuffer->GetGPUVirtualAddress() + triBufferByteOffset_;
            vbv.SizeInBytes = (UINT)(triangleVertices_.size() * sizeof(TriangleVertex));
            vbv.StrideInBytes = sizeof(TriangleVertex);
            commandList_->IASetVertexBuffers(0, 1, &vbv);
            commandList_->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            // batch.instanceOffset = start vertex, batch.instanceCount = vertex count
            commandList_->DrawInstanced(batch.instanceCount, 1, batch.instanceOffset, 0);
            continue;
        }

        // Set instance base offset via root constant (SV_InstanceID doesn't include StartInstanceLocation!)
        commandList_->SetGraphicsRoot32BitConstant(2, batch.instanceOffset, 0);

        // Draw: 6 vertices per instance (2 triangles)
        commandList_->DrawInstanced(6, batch.instanceCount, 0, 0);
    }

    // End-of-flush flush: if the last batch was a path, resolve+blit now
    // so the back buffer reflects everything we recorded.
    if (pathActiveOnMsaa) {
        exitPathMode();
    }
}

// ============================================================================
// Gaussian Blur — Compute Shader Resources
// ============================================================================

bool D3D12DirectRenderer::CreateBlurResources()
{
    if (!device_) return false;

    // Use pre-compiled bytecode for Gaussian blur compute shader.
    {
        using namespace shader_bytecode;
        HRESULT hr = D3DCreateBlob(kgaussian_blur_csSize, &blurCS_);
        if (FAILED(hr)) return false;
        memcpy(blurCS_->GetBufferPointer(), kgaussian_blur_cs, kgaussian_blur_csSize);
    }

    // --- Root signature for blur compute ---
    // [0] Root 32-bit constants (4 x uint32 = BlurConstants)
    // [1] Descriptor table: SRV t0
    // [2] Descriptor table: UAV u0
    D3D12_DESCRIPTOR_RANGE srvRange = {};
    srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
    srvRange.NumDescriptors = 1;
    srvRange.BaseShaderRegister = 0;

    D3D12_DESCRIPTOR_RANGE uavRange = {};
    uavRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV;
    uavRange.NumDescriptors = 1;
    uavRange.BaseShaderRegister = 0;

    D3D12_ROOT_PARAMETER params[3] = {};
    // [0] Root constants
    params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
    params[0].Constants.ShaderRegister = 0;
    params[0].Constants.RegisterSpace = 0;
    params[0].Constants.Num32BitValues = sizeof(BlurConstants) / 4;
    params[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // [1] SRV descriptor table
    params[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    params[1].DescriptorTable.NumDescriptorRanges = 1;
    params[1].DescriptorTable.pDescriptorRanges = &srvRange;
    params[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // [2] UAV descriptor table
    params[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    params[2].DescriptorTable.NumDescriptorRanges = 1;
    params[2].DescriptorTable.pDescriptorRanges = &uavRange;
    params[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    D3D12_ROOT_SIGNATURE_DESC rootSigDesc = {};
    rootSigDesc.NumParameters = 3;
    rootSigDesc.pParameters = params;
    rootSigDesc.Flags = D3D12_ROOT_SIGNATURE_FLAG_NONE;

    ComPtr<ID3DBlob> signature, error;
    if (FAILED(D3D12SerializeRootSignature(&rootSigDesc, D3D_ROOT_SIGNATURE_VERSION_1_0, &signature, &error))) {
        if (error) OutputDebugStringA((const char*)error->GetBufferPointer());
        return false;
    }
    if (FAILED(device_->CreateRootSignature(0, signature->GetBufferPointer(), signature->GetBufferSize(),
            IID_PPV_ARGS(&blurRootSignature_))))
        return false;

    // --- Compute PSO ---
    D3D12_COMPUTE_PIPELINE_STATE_DESC cpsoDesc = {};
    cpsoDesc.pRootSignature = blurRootSignature_.Get();
    cpsoDesc.CS = { blurCS_->GetBufferPointer(), blurCS_->GetBufferSize() };
    if (FAILED(device_->CreateComputePipelineState(&cpsoDesc, IID_PPV_ARGS(&blurPSO_))))
        return false;

    // --- CPU-side descriptor heap for blur SRV/UAV creation (4 descriptors) ---
    D3D12_DESCRIPTOR_HEAP_DESC cpuHeapDesc = {};
    cpuHeapDesc.NumDescriptors = 4;
    cpuHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
    cpuHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE; // not shader-visible
    if (FAILED(device_->CreateDescriptorHeap(&cpuHeapDesc, IID_PPV_ARGS(&blurCpuHeap_))))
        return false;

    blurResourcesReady_ = true;
    return true;
}

// ============================================================================
// Ensure temporary blur textures are large enough for the given region
// ============================================================================

static D3D12_RESOURCE_DESC MakeTexture2DDesc(UINT width, UINT height, DXGI_FORMAT format, D3D12_RESOURCE_FLAGS flags)
{
    D3D12_RESOURCE_DESC desc = {};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    desc.Width = width;
    desc.Height = height;
    desc.DepthOrArraySize = 1;
    desc.MipLevels = 1;
    desc.Format = format;
    desc.SampleDesc.Count = 1;
    desc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    desc.Flags = flags;
    return desc;
}

// ────────────────────────────────────────────────────────────────────────
// EnsureMsaaColorBuffer
//
// Lazily creates / re-creates the 4× MSAA color buffer that the frame
// renders into. The buffer is sized to the current viewport (matching
// the swap-chain back buffer); on resize OnResize() drops the old one
// so the next BeginFrame allocates a fresh size-matched buffer. RTV is
// (re)written into the dedicated MSAA slot in rtvHeap_.
// ────────────────────────────────────────────────────────────────────────
bool D3D12DirectRenderer::EnsureMsaaColorBuffer(uint32_t width, uint32_t height)
{
    if (width == 0 || height == 0) return false;
    if (msaaColorBuffer_ && width == msaaWidth_ && height == msaaHeight_) return true;

    msaaColorBuffer_.Reset();
    msaaWidth_ = 0;
    msaaHeight_ = 0;

    D3D12_RESOURCE_DESC desc = {};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    desc.Width = width;
    desc.Height = height;
    desc.DepthOrArraySize = 1;
    desc.MipLevels = 1;
    desc.Format = swapChainFormat_;
    desc.SampleDesc.Count = kMsaaSampleCount;
    desc.SampleDesc.Quality = 0;
    desc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    desc.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;

    auto heapProps = MakeHeapProps(D3D12_HEAP_TYPE_DEFAULT);

    // Pass nullptr for the optimized clear value: the MSAA buffer is cleared
    // to several different colors over its lifetime (BeginFrame's user color,
    // partial-redraw's zero, mid-frame D3D12RenderTarget::Clear), so any
    // single optimized value would mismatch most of them and emit a perf
    // warning every clear (D3D12 #820 CLEARRENDERTARGETVIEW_MISMATCHINGCLEARVALUE).
    // The frame-start MSAA clear runs once per frame so giving up the fast-
    // clear optimization is a fixed sub-microsecond cost.
    //
    // Start in COMMON so the per-frame BeginFrame barrier (COMMON →
    // RENDER_TARGET) is consistent whether this is the first frame or a
    // post-decay subsequent frame.
    HRESULT hr = device_->CreateCommittedResource(
        &heapProps, D3D12_HEAP_FLAG_NONE, &desc,
        D3D12_RESOURCE_STATE_COMMON, nullptr,
        IID_PPV_ARGS(&msaaColorBuffer_));
    if (FAILED(hr)) return false;
    msaaColorBuffer_->SetName(L"JaliumMsaaColor");  // [JALIUM-921 diag]

    msaaWidth_ = width;
    msaaHeight_ = height;

    D3D12_RENDER_TARGET_VIEW_DESC rtvDesc = {};
    rtvDesc.Format = swapChainFormat_;
    rtvDesc.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2DMS;
    device_->CreateRenderTargetView(msaaColorBuffer_.Get(), &rtvDesc, GetMsaaRtvHandle());

    return true;
}

void D3D12DirectRenderer::WaitForGpuIdle()
{
    if (!backend_ || !fence_ || !fenceEvent_) {
        return;
    }

    auto* queue = backend_->GetCommandQueue();
    if (!queue) {
        return;
    }

    const uint64_t fenceValue = nextFenceValue_++;
    if (FAILED(queue->Signal(fence_.Get(), fenceValue))) {
        return;
    }
    backend_->NoteSubmittedFenceValue(fenceValue);

    if (fence_->GetCompletedValue() < fenceValue) {
        ResetEvent(fenceEvent_);
        if (SUCCEEDED(fence_->SetEventOnCompletion(fenceValue, fenceEvent_))) {
            WaitForSingleObject(fenceEvent_, 5000);
        }
    }
    backend_->ReclaimRetiredGpuResources(fence_->GetCompletedValue());
}

bool D3D12DirectRenderer::EnsureSnapshotTexture()
{
    if (!device_ || viewportWidth_ == 0 || viewportHeight_ == 0) {
        snapshotValid_ = false;
        snapshotState_ = D3D12_RESOURCE_STATE_COMMON;
        return false;
    }

    if (snapshotTexture_ && snapshotW_ == viewportWidth_ && snapshotH_ == viewportHeight_) {
        return true;
    }

    if (snapshotUsedThisFrame_) {
        snapshotValid_ = false;
        return false;
    }

    WaitForGpuIdle();

    snapshotTexture_.Reset();
    auto heapProps = MakeHeapProps(D3D12_HEAP_TYPE_DEFAULT);
    auto desc = MakeTexture2DDesc(viewportWidth_, viewportHeight_, swapChainFormat_, D3D12_RESOURCE_FLAG_NONE);
    if (FAILED(device_->CreateCommittedResource(
            &heapProps,
            D3D12_HEAP_FLAG_NONE,
            &desc,
            D3D12_RESOURCE_STATE_COMMON,
            nullptr,
            IID_PPV_ARGS(&snapshotTexture_)))) {
        snapshotW_ = 0;
        snapshotH_ = 0;
        snapshotValid_ = false;
        snapshotState_ = D3D12_RESOURCE_STATE_COMMON;
        return false;
    }
    snapshotTexture_->SetName(L"JaliumSnapshotTexture");  // [JALIUM-921 diag]

    snapshotW_ = viewportWidth_;
    snapshotH_ = viewportHeight_;
    snapshotValid_ = false;
    snapshotState_ = D3D12_RESOURCE_STATE_COMMON;
    return true;
}

bool D3D12DirectRenderer::EnsureBlurTemps(UINT requiredWidth, UINT requiredHeight)
{
    if (!device_ || requiredWidth == 0 || requiredHeight == 0) {
        return false;
    }

    if (blurTempA_ && blurTempB_ && requiredWidth <= blurTempW_ && requiredHeight <= blurTempH_) {
        return true;
    }

    if (blurTempsUsedThisFrame_) {
        return false;
    }

    WaitForGpuIdle();

    blurTempA_.Reset();
    blurTempB_.Reset();
    blurTempAState_ = D3D12_RESOURCE_STATE_COMMON;
    blurTempBState_ = D3D12_RESOURCE_STATE_COMMON;

    UINT allocW = (std::max)(requiredWidth, viewportWidth_);
    UINT allocH = (std::max)(requiredHeight, viewportHeight_);
    allocW = (allocW + 63) & ~63u;
    allocH = (allocH + 63) & ~63u;

    auto heapProps = MakeHeapProps(D3D12_HEAP_TYPE_DEFAULT);
    auto descA = MakeTexture2DDesc(allocW, allocH, swapChainFormat_, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
    auto descB = MakeTexture2DDesc(allocW, allocH, swapChainFormat_, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);

    if (FAILED(device_->CreateCommittedResource(
            &heapProps,
            D3D12_HEAP_FLAG_NONE,
            &descA,
            D3D12_RESOURCE_STATE_COMMON,
            nullptr,
            IID_PPV_ARGS(&blurTempA_)))) {
        blurTempW_ = 0;
        blurTempH_ = 0;
        blurTempAState_ = D3D12_RESOURCE_STATE_COMMON;
        blurTempBState_ = D3D12_RESOURCE_STATE_COMMON;
        return false;
    }

    if (FAILED(device_->CreateCommittedResource(
            &heapProps,
            D3D12_HEAP_FLAG_NONE,
            &descB,
            D3D12_RESOURCE_STATE_COMMON,
            nullptr,
            IID_PPV_ARGS(&blurTempB_)))) {
        blurTempA_.Reset();
        blurTempW_ = 0;
        blurTempH_ = 0;
        blurTempAState_ = D3D12_RESOURCE_STATE_COMMON;
        blurTempBState_ = D3D12_RESOURCE_STATE_COMMON;
        return false;
    }
    blurTempA_->SetName(L"JaliumBlurTempA");  // [JALIUM-921 diag]
    blurTempB_->SetName(L"JaliumBlurTempB");

    blurTempW_ = allocW;
    blurTempH_ = allocH;
    blurTempAState_ = D3D12_RESOURCE_STATE_COMMON;
    blurTempBState_ = D3D12_RESOURCE_STATE_COMMON;
    return true;
}

bool D3D12DirectRenderer::EnsureOffscreenTargets(UINT requiredWidth, UINT requiredHeight)
{
    if (!device_ || requiredWidth == 0 || requiredHeight == 0) {
        offscreenCaptureValid_[0] = false;
        offscreenCaptureValid_[1] = false;
        return false;
    }

    if (offscreenRT_[0] && offscreenRT_[1] && requiredWidth <= offscreenW_ && requiredHeight <= offscreenH_) {
        return true;
    }

    if (offscreenResourcesUsedThisFrame_) {
        offscreenCaptureValid_[0] = false;
        offscreenCaptureValid_[1] = false;
        return false;
    }

    WaitForGpuIdle();

    // Allocate at least viewport-sized so any visible element's effect can use the
    // offscreen without needing a mid-frame resize (which is blocked by usedThisFrame).
    UINT allocW = (std::max)({requiredWidth, offscreenW_, viewportWidth_});
    UINT allocH = (std::max)({requiredHeight, offscreenH_, viewportHeight_});
    allocW = (allocW + 63) & ~63u;
    allocH = (allocH + 63) & ~63u;

    for (int i = 0; i < 2; ++i) {
        offscreenRT_[i].Reset();

        auto heapProps = MakeHeapProps(D3D12_HEAP_TYPE_DEFAULT);
        auto desc = MakeTexture2DDesc(allocW, allocH, swapChainFormat_, D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET);
        float clearColor[4] = { 0, 0, 0, 0 };
        D3D12_CLEAR_VALUE clearVal = {};
        clearVal.Format = swapChainFormat_;
        memcpy(clearVal.Color, clearColor, sizeof(clearColor));

        if (FAILED(device_->CreateCommittedResource(
                &heapProps,
                D3D12_HEAP_FLAG_NONE,
                &desc,
                D3D12_RESOURCE_STATE_COMMON,
                &clearVal,
                IID_PPV_ARGS(&offscreenRT_[i])))) {
            offscreenRT_[0].Reset();
            offscreenRT_[1].Reset();
            offscreenW_ = 0;
            offscreenH_ = 0;
            offscreenCaptureValid_[0] = false;
            offscreenCaptureValid_[1] = false;
            return false;
        }
        wchar_t onm[40]; swprintf_s(onm, L"JaliumOffscreenRT%d", i); offscreenRT_[i]->SetName(onm);  // [JALIUM-921 diag]
    }

    offscreenW_ = allocW;
    offscreenH_ = allocH;
    offscreenRTState_[0] = D3D12_RESOURCE_STATE_COMMON;
    offscreenRTState_[1] = D3D12_RESOURCE_STATE_COMMON;
    offscreenCaptureValid_[0] = false;
    offscreenCaptureValid_[1] = false;
    return true;
}

// ============================================================================
// FlushGraphicsForCompute
// ============================================================================

bool D3D12DirectRenderer::FlushGraphicsForCompute()
{
    // Device gate for mid-frame flushes (effect captures, retained-layer
    // realize and offscreen blits trigger several per frame): once the device
    // is removed, recording through the UMD can AV rather than fail. Drop the
    // pending batches; frameDeviceLost_ is now latched so EndFrame aborts.
    // Returns false so callers skip their own GPU recording that would follow
    // this flush (barriers, dispatches, SRV creation — same AV class).
    if (!CheckFrameDeviceAlive()) {
        rectInstances_.clear();
        textInstances_.clear();
        bitmapInstances_.clear();
        triangleVertices_.clear();
        bitmapTextures_.clear();
        stencilPathDraws_.clear();
        batches_.clear();
        return false;
    }

    // Record any pending graphics draw commands before switching to compute.
    UploadInstances();
    RecordDrawCommands();

    // Clear the batch/instance lists so EndFrame won't re-record them
    rectInstances_.clear();
    textInstances_.clear();
    bitmapInstances_.clear();
    triangleVertices_.clear();
    bitmapTextures_.clear();
    stencilPathDraws_.clear();
    batches_.clear();

    // UploadInstances can latch device loss itself (mid-frame growth failure
    // on a removed device) — report it so callers stop recording too.
    return !frameDeviceLost_;
}

// ============================================================================
// BlurRegion — two-pass separable Gaussian blur via compute shader
// ============================================================================

void D3D12DirectRenderer::BlurRegion(float x, float y, float w, float h, float radius)
{
    if (!inFrame_ || !blurResourcesReady_ || radius <= 0 || w <= 0 || h <= 0) {
        // Fallback: draw semi-transparent overlay to approximate blur
        if (inFrame_ && w > 0 && h > 0) {
            SdfRectInstance overlay = {};
            overlay.posX = x;
            overlay.posY = y;
            overlay.sizeX = w;
            overlay.sizeY = h;
            // Semi-transparent white overlay as a placeholder
            overlay.fillR = 0.5f * 0.3f;
            overlay.fillG = 0.5f * 0.3f;
            overlay.fillB = 0.5f * 0.3f;
            overlay.fillA = 0.3f;
            overlay.opacity = 1.0f;
            AddSdfRect(overlay);
        }
        return;
    }

    // Convert DIP coordinates to physical pixels for texture operations
    float px = x * dpiScale_;
    float py = y * dpiScale_;
    float pw = w * dpiScale_;
    float ph = h * dpiScale_;

    // Clamp region to viewport (physical pixels)
    float rx = std::max(0.0f, px);
    float ry = std::max(0.0f, py);
    float rr = std::min(px + pw, (float)viewportWidth_);
    float rb = std::min(py + ph, (float)viewportHeight_);
    if (rr <= rx || rb <= ry) return;

    UINT regionW = (UINT)(rr - rx);
    UINT regionH = (UINT)(rb - ry);
    if (regionW == 0 || regionH == 0) return;

    // Flush pending graphics work so render target contents are up to date
    if (!FlushGraphicsForCompute()) return;  // device lost — frame will abort

    // --- Ensure temp textures are large enough ---
    DXGI_FORMAT fmt = swapChainFormat_;
    if (!EnsureBlurTemps(regionW, regionH)) {
        SdfRectInstance overlay = {};
        overlay.posX = x;
        overlay.posY = y;
        overlay.sizeX = w;
        overlay.sizeY = h;
        overlay.fillR = 0.5f * 0.3f;
        overlay.fillG = 0.5f * 0.3f;
        overlay.fillB = 0.5f * 0.3f;
        overlay.fillA = 0.3f;
        overlay.opacity = 1.0f;
        AddSdfRect(overlay);
        return;
    }
    blurTempsUsedThisFrame_ = true;

    auto* cl = commandList_.Get();
    auto* backBuffer = renderTargets_[currentFrame_].Get();

    // --- Step 1: Copy region from back buffer into blurTempA ---
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(backBuffer, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_COPY_SOURCE);
        barriers[1] = MakeTransitionBarrier(blurTempA_.Get(), blurTempAState_, D3D12_RESOURCE_STATE_COPY_DEST);
        cl->ResourceBarrier(2, barriers);
        blurTempAState_ = D3D12_RESOURCE_STATE_COPY_DEST;
    }

    {
        D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
        srcLoc.pResource = backBuffer;
        srcLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        srcLoc.SubresourceIndex = 0;

        D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
        dstLoc.pResource = blurTempA_.Get();
        dstLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        dstLoc.SubresourceIndex = 0;

        D3D12_BOX srcBox = {};
        srcBox.left = (UINT)rx;
        srcBox.top = (UINT)ry;
        srcBox.right = (UINT)rx + regionW;
        srcBox.bottom = (UINT)ry + regionH;
        srcBox.front = 0;
        srcBox.back = 1;

        cl->CopyTextureRegion(&dstLoc, 0, 0, 0, &srcLoc, &srcBox);
    }

    // --- Step 2: Horizontal blur  TempA -> TempB ---
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(blurTempA_.Get(), blurTempAState_, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        barriers[1] = MakeTransitionBarrier(blurTempB_.Get(), blurTempBState_, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        cl->ResourceBarrier(2, barriers);
        blurTempAState_ = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
        blurTempBState_ = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
    }

    // Use descriptor slots at the END of the heap for blur (past ring buffer region)
    const UINT blurSrvSlot = kMaxSrvDescriptors - 8;
    const UINT blurUavSlot = kMaxSrvDescriptors - 7;

    auto srvCpuBase = srvHeap_->GetCPUDescriptorHandleForHeapStart();
    auto srvGpuBase = srvHeap_->GetGPUDescriptorHandleForHeapStart();

    // Create SRV for TempA (input to horizontal pass)
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Format = fmt;
        srvDesc.Texture2D.MipLevels = 1;

        auto handle = srvCpuBase;
        handle.ptr += blurSrvSlot * srvDescriptorSize_;
        device_->CreateShaderResourceView(blurTempA_.Get(), &srvDesc, handle);
    }

    // Create UAV for TempB (output of horizontal pass)
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        uavDesc.Format = fmt;

        auto handle = srvCpuBase;
        handle.ptr += blurUavSlot * srvDescriptorSize_;
        device_->CreateUnorderedAccessView(blurTempB_.Get(), nullptr, &uavDesc, handle);
    }

    // Set compute root signature and PSO
    cl->SetComputeRootSignature(blurRootSignature_.Get());
    ID3D12DescriptorHeap* heaps[] = { srvHeap_.Get() };
    cl->SetDescriptorHeaps(1, heaps);
    cl->SetPipelineState(blurPSO_.Get());

    // Horizontal pass constants
    BlurConstants hConstants;
    hConstants.direction = 0; // horizontal
    hConstants.radius = radius;
    hConstants.texWidth = regionW;
    hConstants.texHeight = regionH;
    cl->SetComputeRoot32BitConstants(0, sizeof(BlurConstants) / 4, &hConstants, 0);

    // Bind SRV and UAV
    {
        auto srvGpu = srvGpuBase;
        srvGpu.ptr += blurSrvSlot * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(1, srvGpu);

        auto uavGpu = srvGpuBase;
        uavGpu.ptr += blurUavSlot * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(2, uavGpu);
    }

    // Dispatch horizontal pass: ceil(width/256) groups x height groups
    UINT groupsX = (regionW + 255) / 256;
    cl->Dispatch(groupsX, regionH, 1);

    // --- Step 3: Vertical blur  TempB -> TempA ---
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(blurTempB_.Get(), blurTempBState_, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        barriers[1] = MakeTransitionBarrier(blurTempA_.Get(), blurTempAState_, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        cl->ResourceBarrier(2, barriers);
        blurTempBState_ = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
        blurTempAState_ = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
    }

    // Create SRV for TempB (input to vertical pass)
    const UINT blurSrvSlot2 = kMaxSrvDescriptors - 6;
    const UINT blurUavSlot2 = kMaxSrvDescriptors - 5;
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Format = fmt;
        srvDesc.Texture2D.MipLevels = 1;

        auto handle = srvCpuBase;
        handle.ptr += blurSrvSlot2 * srvDescriptorSize_;
        device_->CreateShaderResourceView(blurTempB_.Get(), &srvDesc, handle);
    }
    // Create UAV for TempA (output of vertical pass) at slot 11
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        uavDesc.Format = fmt;

        auto handle = srvCpuBase;
        handle.ptr += blurUavSlot2 * srvDescriptorSize_;
        device_->CreateUnorderedAccessView(blurTempA_.Get(), nullptr, &uavDesc, handle);
    }

    // Vertical pass constants
    BlurConstants vConstants;
    vConstants.direction = 1; // vertical
    vConstants.radius = radius;
    vConstants.texWidth = regionW;
    vConstants.texHeight = regionH;
    cl->SetComputeRoot32BitConstants(0, sizeof(BlurConstants) / 4, &vConstants, 0);

    {
        auto srvGpu = srvGpuBase;
        srvGpu.ptr += blurSrvSlot2 * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(1, srvGpu);

        auto uavGpu = srvGpuBase;
        uavGpu.ptr += blurUavSlot2 * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(2, uavGpu);
    }

    // Dispatch vertical pass: ceil(height/256) groups x width groups
    UINT groupsY = (regionH + 255) / 256;
    cl->Dispatch(groupsY, regionW, 1);

    // --- Step 4: Copy result back from TempA to back buffer ---
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(blurTempA_.Get(), blurTempAState_, D3D12_RESOURCE_STATE_COPY_SOURCE);
        barriers[1] = MakeTransitionBarrier(backBuffer, D3D12_RESOURCE_STATE_COPY_SOURCE, D3D12_RESOURCE_STATE_COPY_DEST);
        cl->ResourceBarrier(2, barriers);
        blurTempAState_ = D3D12_RESOURCE_STATE_COPY_SOURCE;
    }

    {
        D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
        srcLoc.pResource = blurTempA_.Get();
        srcLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        srcLoc.SubresourceIndex = 0;

        D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
        dstLoc.pResource = backBuffer;
        dstLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        dstLoc.SubresourceIndex = 0;

        D3D12_BOX srcBox = {};
        srcBox.left = 0;
        srcBox.top = 0;
        srcBox.right = regionW;
        srcBox.bottom = regionH;
        srcBox.front = 0;
        srcBox.back = 1;

        cl->CopyTextureRegion(&dstLoc, (UINT)rx, (UINT)ry, 0, &srcLoc, &srcBox);
    }

    // Transition back buffer back to RENDER_TARGET for subsequent draws
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(backBuffer, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_RENDER_TARGET);
        barriers[1] = MakeTransitionBarrier(blurTempA_.Get(), blurTempAState_, D3D12_RESOURCE_STATE_COMMON);
        cl->ResourceBarrier(2, barriers);
        blurTempAState_ = D3D12_RESOURCE_STATE_COMMON;
    }

    // Transition TempB back to COMMON for next use
    {
        auto barrier = MakeTransitionBarrier(blurTempB_.Get(), blurTempBState_, D3D12_RESOURCE_STATE_COMMON);
        cl->ResourceBarrier(1, &barrier);
        blurTempBState_ = D3D12_RESOURCE_STATE_COMMON;
    }

    // Re-bind render target and viewport for subsequent graphics draws
    auto rtvHandle = GetSwapChainRtvHandle();
    cl->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    D3D12_VIEWPORT vp = { 0, 0, (float)viewportWidth_, (float)viewportHeight_, 0, 1 };
    D3D12_RECT scissor = { 0, 0, (LONG)viewportWidth_, (LONG)viewportHeight_ };
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);
}

// ============================================================================
// PunchTransparentRect — write (0,0,0,0) using copy blend
// ============================================================================

void D3D12DirectRenderer::PunchTransparentRect(float x, float y, float w, float h)
{
    if (!inFrame_ || w <= 0 || h <= 0) return;

    // Apply transform to get screen-space coordinates
    const auto& t = transformStack_.top();
    float newX = x * t.m11 + y * t.m21 + t.dx;
    float newY = x * t.m12 + y * t.m22 + t.dy;
    float scaleX = std::sqrt(t.m11 * t.m11 + t.m12 * t.m12);
    float scaleY = std::sqrt(t.m21 * t.m21 + t.m22 * t.m22);
    float sw = w * scaleX;
    float sh = h * scaleY;

    // Flush pending draws — they must be recorded before ClearRTV.
    if (!FlushGraphicsForCompute()) return;  // device lost — frame will abort

    // Use ClearRenderTargetView with a scissor rect to punch a transparent hole.
    // This directly writes (0,0,0,0) to the region without going through the SDF shader
    // (which would discard alpha=0 pixels).
    auto* cl = commandList_.Get();

    // Coordinates are in DIPs; ClearRenderTargetView needs physical pixels.
    D3D12_RECT clearRect;
    clearRect.left = (LONG)(newX * dpiScale_);
    clearRect.top = (LONG)(newY * dpiScale_);
    clearRect.right = (LONG)((newX + sw) * dpiScale_);
    clearRect.bottom = (LONG)((newY + sh) * dpiScale_);

    auto rtvHandle = GetSwapChainRtvHandle();

    float clearColor[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
    cl->ClearRenderTargetView(rtvHandle, clearColor, 1, &clearRect);

    // This writes the swap-chain back buffer directly (bypassing AddXxx), so it
    // must advance drawOrder_ to keep CaptureSnapshot's fast-skip invariant
    // intact: equality of drawOrder_ means "back buffer unchanged since". A
    // following backdrop/glass snapshot would otherwise reuse the stale
    // pre-punch copy and miss this transparent hole.
    drawOrder_++;
}

// ============================================================================
// CaptureSnapshot — copy back buffer to snapshot texture
// ============================================================================

bool D3D12DirectRenderer::CaptureSnapshot()
{
    if (!inFrame_) return false;

    UINT w = viewportWidth_;
    UINT h = viewportHeight_;
    if (w == 0 || h == 0) return false;

    // Fast skip: a previous CaptureSnapshot in this frame already produced an
    // intact copy AND nothing has been emitted since. Two sibling
    // DrawBackdropFilter / DrawLiquidGlass calls in the same frame use this
    // path to share one full-screen CopyResource instead of paying for one
    // per call (~2.8 ms saved per duplicate at 1080p, scales with viewport).
    // drawOrder_ is monotonically increasing for every AddSdfRect / AddText
    // / AddBitmap / AddTriangles*, so equality means "no draw fired since".
    if (snapshotValid_ && snapshotCaptureDrawOrder_ == drawOrder_) {
        snapshotUsedThisFrame_ = true;
        return true;
    }

    if (!EnsureSnapshotTexture()) {
        snapshotValid_ = false;
        return false;
    }

    // Flush pending draws so the back buffer is up to date
    if (!FlushGraphicsForCompute()) return false;  // device lost — frame will abort

    auto* cl = commandList_.Get();
    auto* backBuffer = renderTargets_[currentFrame_].Get();

    // Transition for copy
    D3D12_RESOURCE_BARRIER barriers[2];
    barriers[0] = MakeTransitionBarrier(backBuffer, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_COPY_SOURCE);
    barriers[1] = MakeTransitionBarrier(snapshotTexture_.Get(), snapshotState_, D3D12_RESOURCE_STATE_COPY_DEST);
    cl->ResourceBarrier(2, barriers);
    snapshotState_ = D3D12_RESOURCE_STATE_COPY_DEST;

    // Copy entire back buffer to snapshot
    cl->CopyResource(snapshotTexture_.Get(), backBuffer);

    // Transition back
    barriers[0] = MakeTransitionBarrier(backBuffer, D3D12_RESOURCE_STATE_COPY_SOURCE, D3D12_RESOURCE_STATE_RENDER_TARGET);
    barriers[1] = MakeTransitionBarrier(snapshotTexture_.Get(), snapshotState_, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
    cl->ResourceBarrier(2, barriers);
    snapshotState_ = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;

    // Re-bind render target
    auto rtvHandle = GetSwapChainRtvHandle();
    cl->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    D3D12_VIEWPORT vp = { 0, 0, (float)viewportWidth_, (float)viewportHeight_, 0, 1 };
    D3D12_RECT scissor = { 0, 0, (LONG)viewportWidth_, (LONG)viewportHeight_ };
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);

    snapshotValid_ = true;
    snapshotUsedThisFrame_ = true;
    // Watermark for the reuse fast-path: any subsequent AddXxx will bump
    // drawOrder_, making the equality check above fail and forcing a
    // genuine re-capture for the next backdrop user.
    snapshotCaptureDrawOrder_ = drawOrder_;
    return true;
}

// ============================================================================
// DrawSnapshotBlurred — blur a region from the snapshot and draw it back
// ============================================================================

void D3D12DirectRenderer::DrawSnapshotBlurred(float x, float y, float w, float h,
                                               float blurRadius,
                                               float tintR, float tintG, float tintB, float tintOpacity,
                                               float cornerRadius)
{
    if (!inFrame_ || w <= 0 || h <= 0) return;

    // Draw the snapshot region to the back buffer, optionally with blur
    if (snapshotValid_ && snapshotTexture_) {
        // Add the snapshot region as a bitmap blit
        if (bitmapInstances_.size() < kMaxInstancesPerFrame) {
            BitmapQuadInstance inst = {};
            inst.posX = x; inst.posY = y;
            inst.sizeX = w; inst.sizeY = h;
            // UV coords map DIP position to snapshot texture [0,1].
            // Snapshot is captured at physical pixel size, so scale DIPs to physical.
            float invSnapW = dpiScale_ / (float)snapshotW_;
            float invSnapH = dpiScale_ / (float)snapshotH_;
            inst.uvMinX = x * invSnapW;
            inst.uvMinY = y * invSnapH;
            inst.uvMaxX = (x + w) * invSnapW;
            inst.uvMaxY = (y + h) * invSnapH;
            inst.opacity = 1.0f;

            DrawBatch batch;
            batch.type = DrawBatchType::SnapshotBlit;
            batch.instanceOffset = (uint32_t)bitmapInstances_.size();
            batch.instanceCount = 1;
            batch.sortOrder = drawOrder_++;
            batch.hasScissor = !scissorStack_.empty();
            if (batch.hasScissor) batch.scissor = scissorStack_.top();
            ResolveRoundedClipForBatch(batch);
            batches_.push_back(batch);
            bitmapInstances_.push_back(inst);
        }

        // Blur the region in-place on the back buffer (if requested)
        if (blurRadius > 0.5f && blurResourcesReady_) {
            BlurRegion(x, y, w, h, blurRadius);
        }
    }

    // Draw tint overlay if needed
    if (tintOpacity > 0.01f) {
        SdfRectInstance tint = {};
        tint.posX = x; tint.posY = y;
        tint.sizeX = w; tint.sizeY = h;
        tint.fillR = tintR * tintOpacity;
        tint.fillG = tintG * tintOpacity;
        tint.fillB = tintB * tintOpacity;
        tint.fillA = tintOpacity;
        tint.cornerTL = cornerRadius;
        tint.cornerTR = cornerRadius;
        tint.cornerBR = cornerRadius;
        tint.cornerBL = cornerRadius;
        tint.opacity = 1.0f;
        AddSdfRect(tint);
    }
}

// ============================================================================
// CaptureDesktopArea — GDI capture + D3D12 upload
// ============================================================================

void D3D12DirectRenderer::CaptureDesktopArea(int32_t screenX, int32_t screenY, int32_t width, int32_t height)
{
    if (width <= 0 || height <= 0 || !device_) return;

    // Capture from screen DC using BitBlt
    HDC desktopDC = GetDC(NULL);
    if (!desktopDC) return;

    HDC memDC = CreateCompatibleDC(desktopDC);
    if (!memDC) { ReleaseDC(NULL, desktopDC); return; }

    HBITMAP hBitmap = CreateCompatibleBitmap(desktopDC, width, height);
    if (!hBitmap) { DeleteDC(memDC); ReleaseDC(NULL, desktopDC); return; }

    HGDIOBJ oldBitmap = SelectObject(memDC, hBitmap);
    BitBlt(memDC, 0, 0, width, height, desktopDC, screenX, screenY, SRCCOPY);
    SelectObject(memDC, oldBitmap);

    // Get pixel data
    BITMAPINFOHEADER bi = {};
    bi.biSize = sizeof(bi);
    bi.biWidth = width;
    bi.biHeight = -height;  // top-down
    bi.biPlanes = 1;
    bi.biBitCount = 32;
    bi.biCompression = BI_RGB;

    std::vector<uint8_t> pixels(width * height * 4);
    GetDIBits(memDC, hBitmap, 0, height, pixels.data(),
        reinterpret_cast<BITMAPINFO*>(&bi), DIB_RGB_COLORS);

    // Fix alpha (GDI returns alpha=0)
    for (int32_t i = 0; i < width * height; ++i) {
        pixels[i * 4 + 3] = 255;
    }

    DeleteObject(hBitmap);
    DeleteDC(memDC);
    ReleaseDC(NULL, desktopDC);

    UINT w = (UINT)width;
    UINT h = (UINT)height;

    // Create or resize desktop texture
    if (!desktopTexture_ || desktopCaptureW_ != w || desktopCaptureH_ != h) {
        desktopTexture_.Reset();
        desktopUploadBuffer_.Reset();

        auto heapProps = MakeHeapProps(D3D12_HEAP_TYPE_DEFAULT);
        auto desc = MakeTexture2DDesc(w, h, DXGI_FORMAT_B8G8R8A8_UNORM, D3D12_RESOURCE_FLAG_NONE);
        if (FAILED(device_->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &desc,
                D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&desktopTexture_))))
            return;
        desktopTexture_->SetName(L"JaliumDesktopCapture");  // [JALIUM-921 diag]

        // Upload buffer
        UINT64 rowPitch = ((UINT64)w * 4 + D3D12_TEXTURE_DATA_PITCH_ALIGNMENT - 1) & ~(D3D12_TEXTURE_DATA_PITCH_ALIGNMENT - 1);
        UINT64 uploadSize = rowPitch * h;
        auto uploadHeap = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
        auto bufDesc = MakeBufferDesc(uploadSize);
        if (FAILED(device_->CreateCommittedResource(&uploadHeap, D3D12_HEAP_FLAG_NONE, &bufDesc,
                D3D12_RESOURCE_STATE_GENERIC_READ, nullptr, IID_PPV_ARGS(&desktopUploadBuffer_))))
            return;
        desktopUploadBuffer_->SetName(L"JaliumDesktopUpload");  // [JALIUM-921 diag]

        desktopCaptureW_ = w;
        desktopCaptureH_ = h;
        desktopTextureState_ = D3D12_RESOURCE_STATE_COMMON;
    }

    // Upload pixel data to upload buffer (with proper row pitch alignment)
    UINT64 rowPitch = ((UINT64)w * 4 + D3D12_TEXTURE_DATA_PITCH_ALIGNMENT - 1) & ~(D3D12_TEXTURE_DATA_PITCH_ALIGNMENT - 1);
    void* mapped = nullptr;
    if (SUCCEEDED(desktopUploadBuffer_->Map(0, nullptr, &mapped))) {
        uint8_t* dst = (uint8_t*)mapped;
        for (UINT row = 0; row < h; ++row) {
            memcpy(dst + row * rowPitch, pixels.data() + row * w * 4, w * 4);
        }
        desktopUploadBuffer_->Unmap(0, nullptr);
    }

    // Copy upload buffer to texture (deferred — will execute when command list is submitted)
    if (inFrame_) {
        auto* cl = commandList_.Get();
        // Track the actual resource state — on second+ call it's PIXEL_SHADER_RESOURCE, not COMMON
        D3D12_RESOURCE_STATES currentState = desktopTextureState_;
        auto barrier = MakeTransitionBarrier(desktopTexture_.Get(), currentState, D3D12_RESOURCE_STATE_COPY_DEST);
        cl->ResourceBarrier(1, &barrier);

        D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
        srcLoc.pResource = desktopUploadBuffer_.Get();
        srcLoc.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        srcLoc.PlacedFootprint.Offset = 0;
        srcLoc.PlacedFootprint.Footprint.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        srcLoc.PlacedFootprint.Footprint.Width = w;
        srcLoc.PlacedFootprint.Footprint.Height = h;
        srcLoc.PlacedFootprint.Footprint.Depth = 1;
        srcLoc.PlacedFootprint.Footprint.RowPitch = (UINT)rowPitch;

        D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
        dstLoc.pResource = desktopTexture_.Get();
        dstLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        dstLoc.SubresourceIndex = 0;

        cl->CopyTextureRegion(&dstLoc, 0, 0, 0, &srcLoc, nullptr);

        barrier = MakeTransitionBarrier(desktopTexture_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        cl->ResourceBarrier(1, &barrier);
        desktopTextureState_ = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
        desktopCaptureValid_ = true;
    }
    // Don't set desktopCaptureValid_ if not in frame — GPU copy wasn't issued
}

// ============================================================================
// DrawDesktopBackdrop — draw captured desktop with blur and tint
// ============================================================================

void D3D12DirectRenderer::DrawDesktopBackdrop(float x, float y, float w, float h,
                                               float blurRadius,
                                               float tintR, float tintG, float tintB, float tintOpacity)
{
    if (!inFrame_ || !desktopCaptureValid_ || !desktopTexture_) return;
    if (w <= 0 || h <= 0) return;

    // Draw the desktop texture as a bitmap
    AddBitmap(x, y, w, h, 1.0f, desktopTexture_.Get(), DXGI_FORMAT_B8G8R8A8_UNORM);

    // Blur the region if requested
    if (blurRadius > 0.5f) {
        BlurRegion(x, y, w, h, blurRadius);
    }

    // Tint overlay
    if (tintOpacity > 0.001f) {
        SdfRectInstance tint = {};
        tint.posX = x; tint.posY = y;
        tint.sizeX = w; tint.sizeY = h;
        tint.fillR = tintR * tintOpacity;
        tint.fillG = tintG * tintOpacity;
        tint.fillB = tintB * tintOpacity;
        tint.fillA = tintOpacity;
        tint.opacity = 1.0f;
        AddSdfRect(tint);
    }
}

// ============================================================================
// EmitSoftGlowDot — one radial soft-glow stamp (bright centre → clear edge)
// ============================================================================

void D3D12DirectRenderer::EmitSoftGlowDot(
    float cx, float cy, float diameter, float r, float g, float b, float peakAlpha)
{
    if (diameter < 1.0f || peakAlpha <= 0.003f) return;

    float rad = diameter * 0.5f;

    SdfRectInstance dot = {};
    dot.posX = cx - rad;
    dot.posY = cy - rad;
    dot.sizeX = diameter;
    dot.sizeY = diameter;
    // Full corner radius → the SDF shape is a circle with a soft AA edge.
    dot.cornerTL = rad; dot.cornerTR = rad; dot.cornerBR = rad; dot.cornerBL = rad;

    // Radial gradient: the centre is supplied in absolute pixels (the vertex
    // shader subtracts the instance position to localise it); radius is absolute.
    dot.gradientType = 2;
    dot.stopCount = 3;
    dot.gradGeom0 = cx;
    dot.gradGeom1 = cy;
    dot.gradGeom2 = rad;
    dot.gradGeom3 = rad;

    // Straight-alpha stops (the pixel shader premultiplies). A solid core, a
    // fast shoulder, then a transparent rim → a soft, glow-like falloff.
    dot.stop0Pos = 0.0f; dot.stop0R = r; dot.stop0G = g; dot.stop0B = b; dot.stop0A = peakAlpha;
    dot.stop1Pos = 0.5f; dot.stop1R = r; dot.stop1G = g; dot.stop1B = b; dot.stop1A = peakAlpha * 0.32f;
    dot.stop2Pos = 1.0f; dot.stop2R = r; dot.stop2G = g; dot.stop2B = b; dot.stop2A = 0.0f;

    dot.opacity = 1.0f;
    AddSdfRect(dot);
}

// ============================================================================
// DrawGlowingBorderHighlight — continuous tapered soft-glow ribbon
// ============================================================================

void D3D12DirectRenderer::DrawGlowingBorderHighlight(
    float x, float y, float w, float h,
    float animationPhase, float glowR, float glowG, float glowB,
    float strokeWidth, float trailLength, float dimOpacity,
    float screenWidth, float screenHeight)
{
    if (!inFrame_ || w <= 0 || h <= 0) return;

    // Step 1: Dim overlay (excluding the highlighted element area)
    if (dimOpacity > 0.01f) {
        float expand = strokeWidth * 10.0f;
        // Top
        if (y - expand > 0) {
            SdfRectInstance top = {};
            top.posX = 0; top.posY = 0;
            top.sizeX = screenWidth; top.sizeY = std::max(0.0f, y - expand);
            top.fillR = 0; top.fillG = 0; top.fillB = 0; top.fillA = dimOpacity;
            top.opacity = 1.0f;
            AddSdfRect(top);
        }
        // Bottom
        if (y + h + expand < screenHeight) {
            SdfRectInstance bot = {};
            bot.posX = 0; bot.posY = y + h + expand;
            bot.sizeX = screenWidth; bot.sizeY = screenHeight - (y + h + expand);
            bot.fillR = 0; bot.fillG = 0; bot.fillB = 0; bot.fillA = dimOpacity;
            bot.opacity = 1.0f;
            AddSdfRect(bot);
        }
        // Left
        {
            SdfRectInstance left = {};
            left.posX = 0; left.posY = std::max(0.0f, y - expand);
            left.sizeX = std::max(0.0f, x - expand);
            left.sizeY = std::min(screenHeight, y + h + expand) - left.posY;
            left.fillR = 0; left.fillG = 0; left.fillB = 0; left.fillA = dimOpacity;
            left.opacity = 1.0f;
            if (left.sizeX > 0 && left.sizeY > 0) AddSdfRect(left);
        }
        // Right
        {
            SdfRectInstance right = {};
            right.posX = x + w + expand; right.posY = std::max(0.0f, y - expand);
            right.sizeX = screenWidth - right.posX;
            right.sizeY = std::min(screenHeight, y + h + expand) - right.posY;
            right.fillR = 0; right.fillG = 0; right.fillB = 0; right.fillA = dimOpacity;
            right.opacity = 1.0f;
            if (right.sizeX > 0 && right.sizeY > 0) AddSdfRect(right);
        }
    }

    // Step 2: A continuous, tapered ribbon of soft glow whose dots are anchored
    // directly ON the boundary line. The inverse rounded clip pushed below masks
    // away the inner half of every dot, so the light reads as an outer glow that
    // hugs the edge — bright at the line, fading outward, with nothing spilling
    // into the element interior. Thin at both ends, thickest in the middle.
    float perimeter = 2.0f * (w + h);
    if (perimeter < 2.0f) return;

    // Keep the glow strictly OUTSIDE the element. No scissor — an inverse clip
    // must stay free to paint in the thin band just beyond the bounds.
    PushRoundedClipExclude(x, y, w, h, 0.0f, 0.0f);

    float trail = std::max(0.05f, std::min(1.0f, trailLength));
    float trailPx = perimeter * trail;
    float headPos = animationPhase * perimeter;

    // One dot every ~2px so neighbouring dots fuse into a continuous ribbon.
    int numSamples = std::max(48, std::min(900, (int)(trailPx * 0.5f)));

    float coreMax = std::max(strokeWidth * 2.0f, 2.5f);   // bright core on the line
    float haloMax = std::max(strokeWidth * 5.0f, 9.0f);   // tight outer glow, kept close

    for (int i = 0; i <= numSamples; ++i) {
        float u = (float)i / (float)numSamples;   // 0 = head, 1 = tail
        // Symmetric spindle taper: thin at both ends, fattest in the middle.
        float taper = sinf(3.14159265f * u);
        if (taper <= 0.02f) continue;

        float pos = headPos - u * trailPx;
        pos = fmodf(pos, perimeter);
        if (pos < 0.0f) pos += perimeter;

        // Anchor each dot ON the perimeter (TL → TR → BR → BL). The inverse clip
        // trims off whichever half falls inside the element.
        float px, py;
        if (pos < w) {
            px = x + pos;                  py = y;
        } else if (pos < w + h) {
            px = x + w;                    py = y + (pos - w);
        } else if (pos < 2.0f * w + h) {
            px = x + w - (pos - w - h);    py = y + h;
        } else {
            px = x;                        py = y + h - (pos - 2.0f * w - h);
        }

        EmitSoftGlowDot(px, py, haloMax * taper, glowR, glowG, glowB, 0.30f * taper);
        EmitSoftGlowDot(px, py, coreMax * taper, glowR, glowG, glowB, 0.95f * taper);
    }

    // Step 3: a faint static outer halo so the whole edge keeps a soft glow while
    // the bright ribbon sweeps. Each layer is the border of an outward-grown
    // rect, so it sits in the band [bounds edge, bounds + ext] — outside the
    // element. The inverse clip leaves it untouched.
    {
        const float exts[3]   = { strokeWidth * 1.0f, strokeWidth * 2.5f, strokeWidth * 4.0f };
        const float alphas[3] = { 0.26f, 0.14f, 0.06f };
        for (int k = 0; k < 3; ++k) {
            float e = std::max(exts[k], 1.0f);
            SdfRectInstance ring = {};
            ring.posX = x - e; ring.posY = y - e;
            ring.sizeX = w + e * 2.0f; ring.sizeY = h + e * 2.0f;
            ring.borderR = glowR; ring.borderG = glowG; ring.borderB = glowB; ring.borderA = alphas[k];
            ring.borderWidth = e;
            ring.cornerTL = e; ring.cornerTR = e; ring.cornerBR = e; ring.cornerBL = e;
            ring.opacity = 1.0f;
            AddSdfRect(ring);
        }
    }

    PopRoundedClip();
}

// ============================================================================
// DrawGlowingBorderTransition — transition glow between two rects
// ============================================================================

void D3D12DirectRenderer::DrawGlowingBorderTransition(
    float fromX, float fromY, float fromW, float fromH,
    float toX, float toY, float toW, float toH,
    float headProgress, float tailProgress,
    float animationPhase, float glowR, float glowG, float glowB,
    float strokeWidth, float trailLength, float dimOpacity,
    float screenWidth, float screenHeight)
{
    if (!inFrame_) return;

    auto lerp = [](float a, float b, float t) { return a + (b - a) * t; };

    // Draw dim overlay around interpolated highlight area
    if (dimOpacity > 0.01f) {
        float hx = lerp(fromX, toX, headProgress);
        float hy = lerp(fromY, toY, headProgress);
        float hw = lerp(fromW, toW, headProgress);
        float hh = lerp(fromH, toH, headProgress);
        float expand = strokeWidth * 10.0f;

        if (hy - expand > 0) {
            SdfRectInstance r = {};
            r.posX = 0; r.posY = 0; r.sizeX = screenWidth; r.sizeY = hy - expand;
            r.fillR = 0; r.fillG = 0; r.fillB = 0; r.fillA = dimOpacity;
            r.opacity = 1.0f;
            AddSdfRect(r);
        }
        if (hy + hh + expand < screenHeight) {
            SdfRectInstance r = {};
            r.posX = 0; r.posY = hy + hh + expand; r.sizeX = screenWidth;
            r.sizeY = screenHeight - r.posY;
            r.fillR = 0; r.fillG = 0; r.fillB = 0; r.fillA = dimOpacity;
            r.opacity = 1.0f;
            AddSdfRect(r);
        }
    }

    // Soft glowing comet between the from/to centres, using the same tapered
    // soft-glow dots as the highlight ribbon so the two effects match visually.
    float fromCX = fromX + fromW * 0.5f, fromCY = fromY + fromH * 0.5f;
    float toCX = toX + toW * 0.5f, toCY = toY + toH * 0.5f;
    float headX = lerp(fromCX, toCX, headProgress);
    float headY = lerp(fromCY, toCY, headProgress);
    float tailX = lerp(fromCX, toCX, tailProgress);
    float tailY = lerp(fromCY, toCY, tailProgress);

    const int numPoints = 48;
    float coreMax = std::max(strokeWidth * 2.2f, 2.5f);
    float haloMax = std::max(strokeWidth * 11.0f, 16.0f);
    for (int i = 0; i <= numPoints; ++i) {
        float t = (float)i / (float)numPoints;     // 0 = tail, 1 = head
        float taper = sinf(3.14159265f * t);
        if (taper <= 0.02f) continue;
        float px = lerp(tailX, headX, t);
        float py = lerp(tailY, headY, t);
        EmitSoftGlowDot(px, py, haloMax * taper, glowR, glowG, glowB, 0.20f * taper);
        EmitSoftGlowDot(px, py, coreMax * taper, glowR, glowG, glowB, 0.95f * taper);
    }

    // Target border (fades in)
    SdfRectInstance border = {};
    border.posX = toX; border.posY = toY;
    border.sizeX = toW; border.sizeY = toH;
    float bo = 0.3f * headProgress;
    border.borderR = glowR * bo; border.borderG = glowG * bo;
    border.borderB = glowB * bo; border.borderA = bo;
    border.borderWidth = 1.0f;
    border.opacity = 1.0f;
    AddSdfRect(border);
}

// ============================================================================
// DrawRippleEffect — ripple animation
// ============================================================================

void D3D12DirectRenderer::DrawRippleEffect(
    float x, float y, float w, float h,
    float rippleProgress, float glowR, float glowG, float glowB,
    float strokeWidth, float dimOpacity,
    float screenWidth, float screenHeight)
{
    if (!inFrame_ || w <= 0 || h <= 0) return;

    // Dim overlay
    if (dimOpacity > 0.01f) {
        float expand = strokeWidth * 10.0f;
        if (y - expand > 0) {
            SdfRectInstance r = {};
            r.posX = 0; r.posY = 0; r.sizeX = screenWidth; r.sizeY = y - expand;
            r.fillR = 0; r.fillG = 0; r.fillB = 0; r.fillA = dimOpacity;
            r.opacity = 1.0f;
            AddSdfRect(r);
        }
        if (y + h + expand < screenHeight) {
            SdfRectInstance r = {};
            r.posX = 0; r.posY = y + h + expand; r.sizeX = screenWidth;
            r.sizeY = screenHeight - r.posY;
            r.fillR = 0; r.fillG = 0; r.fillB = 0; r.fillA = dimOpacity;
            r.opacity = 1.0f;
            AddSdfRect(r);
        }
    }

    float centerX = x + w * 0.5f;
    float centerY = y + h * 0.5f;

    // Multiple ripple rings
    const int numRipples = 3;
    for (int i = 0; i < numRipples; ++i) {
        float rippleOffset = (float)i / numRipples;
        float adjustedProgress = rippleProgress - rippleOffset * 0.3f;
        if (adjustedProgress < 0.0f) continue;
        adjustedProgress = std::min(1.0f, adjustedProgress / (1.0f - rippleOffset * 0.3f));

        float currentW = adjustedProgress * w;
        float currentH = adjustedProgress * h;
        float opacity = (1.0f - powf(adjustedProgress, 1.0f + i * 0.5f)) * 0.9f;
        if (opacity < 0.01f) continue;

        float rippleX = centerX - currentW * 0.5f;
        float rippleY = centerY - currentH * 0.5f;
        float cornerRadius = std::min(currentW, currentH) * 0.05f;

        // Main ripple ring (border only)
        SdfRectInstance ring = {};
        ring.posX = rippleX; ring.posY = rippleY;
        ring.sizeX = currentW; ring.sizeY = currentH;
        ring.borderR = glowR * opacity; ring.borderG = glowG * opacity;
        ring.borderB = glowB * opacity; ring.borderA = opacity;
        ring.cornerTL = cornerRadius; ring.cornerTR = cornerRadius;
        ring.cornerBR = cornerRadius; ring.cornerBL = cornerRadius;
        float sw = strokeWidth * 1.5f * (1.0f - adjustedProgress * 0.5f);
        ring.borderWidth = std::max(1.0f, sw);
        ring.opacity = 1.0f;
        AddSdfRect(ring);
    }

    // Element border (fades in)
    float borderOpacity = 0.6f + 0.4f * rippleProgress;
    SdfRectInstance border = {};
    border.posX = x; border.posY = y;
    border.sizeX = w; border.sizeY = h;
    border.borderR = glowR * borderOpacity; border.borderG = glowG * borderOpacity;
    border.borderB = glowB * borderOpacity; border.borderA = borderOpacity;
    border.borderWidth = 1.0f;
    border.opacity = 1.0f;
    AddSdfRect(border);
}

// ============================================================================
// Offscreen Render Target — for transition capture
// ============================================================================

bool D3D12DirectRenderer::BeginOffscreenCapture(int slot, float x, float y, float w, float h)
{
    if (!inFrame_ || slot < 0 || slot > 1 || w <= 0 || h <= 0) {
        return false;
    }
    if (inOffscreenCapture_ || inRetainedCapture_) {
        return false;
    }

    // w, h are in DIP.  Allocate pixels at the current DPI scale so content
    // renders at full resolution.
    UINT pw = (UINT)std::ceil(w * dpiScale_);
    UINT ph = (UINT)std::ceil(h * dpiScale_);
    if (pw == 0) pw = 1;
    if (ph == 0) ph = 1;

    // Flush pending draws
    if (!FlushGraphicsForCompute()) {
        // Device lost — frame will abort; never open the capture.
        offscreenCaptureValid_[slot] = false;
        return false;
    }

    bool eotOk = EnsureOffscreenTargets(pw, ph);
    if (!eotOk) {
        offscreenCaptureValid_[slot] = false;
        return false;
    }

    offscreenCaptureX_[slot] = x;
    offscreenCaptureY_[slot] = y;
    offscreenCaptureW_[slot] = (float)pw;
    offscreenCaptureH_[slot] = (float)ph;
    offscreenCaptureValid_[slot] = false;
    offscreenResourcesUsedThisFrame_ = true;

    auto* cl = commandList_.Get();

    // Transition offscreen RT to render target.
    D3D12_RESOURCE_STATES offscreenState = offscreenRTState_[slot];
    auto barrier = MakeTransitionBarrier(offscreenRT_[slot].Get(),
        offscreenState, D3D12_RESOURCE_STATE_RENDER_TARGET);
    cl->ResourceBarrier(1, &barrier);

    // Create a temporary RTV for the offscreen RT.
    // RTV heap has frameCount_ + 2 descriptors; slots [frameCount_] and [frameCount_+1]
    // are reserved for offscreen RT slot 0 and slot 1 respectively.
    D3D12_CPU_DESCRIPTOR_HANDLE offRtv = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
    offRtv.ptr += (frameCount_ + (UINT)slot) * rtvDescriptorSize_;
    device_->CreateRenderTargetView(offscreenRT_[slot].Get(), nullptr, offRtv);

    cl->OMSetRenderTargets(1, &offRtv, FALSE, nullptr);

    // Clear
    float clearColor[4] = { 0, 0, 0, 0 };
    cl->ClearRenderTargetView(offRtv, clearColor, 0, nullptr);

    // Set viewport to physical pixel size
    D3D12_VIEWPORT vp = { 0, 0, (float)pw, (float)ph, 0, 1 };
    D3D12_RECT scissor = { 0, 0, (LONG)pw, (LONG)ph };
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);

    // Save and clear the scissor stack.  Parent clip rects are in screen-space
    // physical pixels, which would clip away offscreen content that starts at (0,0).
    savedScissorStack_ = std::move(scissorStack_);
    scissorStack_ = {};

    // Push a transform to shift from screen coords to offscreen-local coords
    PushTransform(1, 0, 0, 1, -x, -y);

    // Frame constants: screenWidth/Height are in DIP — the shader divides DIP positions
    // by these to get NDC, and the viewport maps NDC to physical pixels.
    // Use the original DIP dimensions so the full capture area maps to [-1,+1] NDC.
    currentFrameConstants_.screenWidth = w;
    currentFrameConstants_.screenHeight = h;
    currentFrameConstants_.invScreenWidth = 1.0f / w;
    currentFrameConstants_.invScreenHeight = 1.0f / h;

    // Stash the active capture target for the stencil-path arm (exitPathMode):
    // path content must resolve into this offscreen RT, not the back buffer.
    captureRtv_ = offRtv;
    captureViewportW_ = pw;
    captureViewportH_ = ph;

    inOffscreenCapture_ = true;
    return true;
}

void D3D12DirectRenderer::EndOffscreenCapture(int slot)
{
    if (!inFrame_ || slot < 0 || slot > 1 || !inOffscreenCapture_) return;

    // Pop the offset transform
    PopTransform();

    // Flush pending draws to the offscreen RT
    if (!FlushGraphicsForCompute()) {
        // Device lost mid-capture: skip every GPU restore call below (they
        // record into a dead driver) but COMPLETE the CPU state restore — a
        // stuck inOffscreenCapture_ would permanently fail every later effect
        // capture (same trap EndRetainedLayerCapture documents for resize).
        inOffscreenCapture_ = false;
        captureRtv_ = {};
        captureViewportW_ = 0;
        captureViewportH_ = 0;
        scissorStack_ = std::move(savedScissorStack_);
        savedScissorStack_ = {};
        float dipWl = (float)viewportWidth_ / dpiScale_;
        float dipHl = (float)viewportHeight_ / dpiScale_;
        currentFrameConstants_.screenWidth = dipWl;
        currentFrameConstants_.screenHeight = dipHl;
        currentFrameConstants_.invScreenWidth = 1.0f / dipWl;
        currentFrameConstants_.invScreenHeight = 1.0f / dipHl;
        offscreenCaptureValid_[slot] = false;
        return;
    }

    auto* cl = commandList_.Get();

    // Transition offscreen RT to pixel shader resource for later texture read
    auto barrier = MakeTransitionBarrier(offscreenRT_[slot].Get(),
        D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
    cl->ResourceBarrier(1, &barrier);
    offscreenRTState_[slot] = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;

    // Restore main render target
    auto rtvHandle = GetSwapChainRtvHandle();
    cl->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    // Restore viewport
    D3D12_VIEWPORT vp = { 0, 0, (float)viewportWidth_, (float)viewportHeight_, 0, 1 };
    D3D12_RECT scissor = { 0, 0, (LONG)viewportWidth_, (LONG)viewportHeight_ };
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);

    // Restore frame constants (DIP dimensions, matching BeginFrame)
    float dipW = (float)viewportWidth_ / dpiScale_;
    float dipH = (float)viewportHeight_ / dpiScale_;
    currentFrameConstants_.screenWidth = dipW;
    currentFrameConstants_.screenHeight = dipH;
    currentFrameConstants_.invScreenWidth = 1.0f / dipW;
    currentFrameConstants_.invScreenHeight = 1.0f / dipH;

    inOffscreenCapture_ = false;
    captureRtv_ = {};
    captureViewportW_ = 0;
    captureViewportH_ = 0;

    // Restore the scissor stack saved during BeginOffscreenCapture
    scissorStack_ = std::move(savedScissorStack_);
    savedScissorStack_ = {};

    offscreenCaptureValid_[slot] = true;
}

// ─── Retained GPU layers ────────────────────────────────────────────────────
// Mirror the BeginOffscreenCapture / EndOffscreenCapture RT-redirect dance but
// target a PERSISTENT per-layer texture+RTV, and realize on the MAIN command
// list (no separate synchronous submit). Translate/scale/opacity composite
// reuses AddBitmap (which bakes scale+translate from the live transform stack);
// rotation/skew is a documented follow-on (AddBitmap drops it).
D3D12RetainedLayer* D3D12DirectRenderer::BeginRetainedLayerCapture(
    D3D12RetainedLayer* layer, float x, float y, float w, float h)
{
    if (!inFrame_ || inOffscreenCapture_ || inRetainedCapture_ || w <= 0 || h <= 0) {
        return nullptr;
    }

    // Allocate physical pixels at the current DPI so the layer renders sharp.
    UINT pw = (UINT)std::ceil(w * dpiScale_);
    UINT ph = (UINT)std::ceil(h * dpiScale_);
    if (pw == 0) pw = 1;
    if (ph == 0) ph = 1;

    // Stale-generation guard (device-lost recovery): a layer created on a
    // previous, now-removed device must never reach EnsureSize. A same-size
    // hit would feed the dead device's texture into THIS device's command
    // list (ResourceBarrier / OMSetRenderTargets on a foreign resource → UMD
    // AV, the GPU-switch crash signature), and a resize would Release()
    // through a backend pointer that may already be destroyed. Refuse; the
    // caller falls back to direct rendering and managed recovery re-realizes.
    if (layer && layer->Device() != device_) {
        return nullptr;
    }

    const bool created = (layer == nullptr);
    if (created) {
        layer = new (std::nothrow) D3D12RetainedLayer(device_, backend_);
        if (!layer) return nullptr;
    }
    if (!layer->EnsureSize(pw, ph, swapChainFormat_)) {
        if (created) delete layer;
        return nullptr;
    }

    if (!FlushGraphicsForCompute()) {
        // Device lost — frame will abort; never open the capture. A layer we
        // just allocated dies with the device; retire it through the (still
        // live) graveyard rather than leaking the wrapper.
        if (created) delete layer;
        return nullptr;
    }
    auto* cl = commandList_.Get();

    // Transition the layer texture to RENDER_TARGET.
    auto barrier = MakeTransitionBarrier(layer->Texture(),
        layer->State(), D3D12_RESOURCE_STATE_RENDER_TARGET);
    cl->ResourceBarrier(1, &barrier);
    layer->SetState(D3D12_RESOURCE_STATE_RENDER_TARGET);

    D3D12_CPU_DESCRIPTOR_HANDLE rtv = layer->Rtv();
    cl->OMSetRenderTargets(1, &rtv, FALSE, nullptr);

    float clearColor[4] = { 0, 0, 0, 0 };
    cl->ClearRenderTargetView(rtv, clearColor, 0, nullptr);

    D3D12_VIEWPORT vp = { 0, 0, (float)pw, (float)ph, 0, 1 };
    D3D12_RECT scissor = { 0, 0, (LONG)pw, (LONG)ph };
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);

    // Parent screen-space scissor rects would clip away layer-local content that
    // starts at (0,0); save and clear, exactly like BeginOffscreenCapture.
    savedScissorStack_ = std::move(scissorStack_);
    scissorStack_ = {};

    // Shift screen coords → layer-local coords (content drawn at world (x,y)
    // lands at the texture origin).
    PushTransform(1, 0, 0, 1, -x, -y);

    // Frame constants map the DIP capture area to [-1,+1] NDC (viewport → pixels).
    currentFrameConstants_.screenWidth = w;
    currentFrameConstants_.screenHeight = h;
    currentFrameConstants_.invScreenWidth = 1.0f / w;
    currentFrameConstants_.invScreenHeight = 1.0f / h;

    // Stash the active capture target for the stencil-path arm (exitPathMode):
    // path content must resolve into this layer texture, not the back buffer.
    captureRtv_ = rtv;
    captureViewportW_ = pw;
    captureViewportH_ = ph;

    inRetainedCapture_ = true;
    activeRealizeLayer_ = layer;
    return layer;
}

void D3D12DirectRenderer::EndRetainedLayerCapture(D3D12RetainedLayer* layer)
{
    if (!inRetainedCapture_) return;
    // 根因修复：frame 已结束/abort（典型：retained-layer capture 期间发生 resize，
    // ResizeBuffers/AbortFrame 把 inFrame_ 置回 false 并抽走 back buffer）——此时绝不能再做
    // GPU 操作（RT 切换 / barrier / 提交命令列表），但**必须**复位 inRetainedCapture_，否则它
    // 卡死 true，之后每帧所有 effect 的 BeginOffscreenCapture(:4019 `if(inOffscreenCapture_||
    // inRetainedCapture_) return false`) 全部失败 → glow/阴影等 effect 永久失效
    // （[BeginEffectCapture] FAILED）。原代码 `if(!inFrame_||!inRetainedCapture_) return` 在
    // !inFrame_ 时直接退出、漏掉了下面 :4242 的复位，这就是 bug。
    if (!inFrame_) { inRetainedCapture_ = false; activeRealizeLayer_ = nullptr; return; }

    // Pop the layer-local offset transform.
    PopTransform();

    // Flush pending draws into the layer.
    if (!FlushGraphicsForCompute()) {
        // Device lost mid-realize: skip the GPU restore below but COMPLETE the
        // CPU state restore (a stuck inRetainedCapture_ kills all later effect
        // captures — same trap the !inFrame_ branch above documents). The layer
        // stays in RENDER_TARGET state, so CompositeRetainedLayer refuses to
        // sample the unrealized texture.
        inRetainedCapture_ = false;
        activeRealizeLayer_ = nullptr;
        captureRtv_ = {};
        captureViewportW_ = 0;
        captureViewportH_ = 0;
        scissorStack_ = std::move(savedScissorStack_);
        savedScissorStack_ = {};
        float dipWl = (float)viewportWidth_ / dpiScale_;
        float dipHl = (float)viewportHeight_ / dpiScale_;
        currentFrameConstants_.screenWidth = dipWl;
        currentFrameConstants_.screenHeight = dipHl;
        currentFrameConstants_.invScreenWidth = 1.0f / dipWl;
        currentFrameConstants_.invScreenHeight = 1.0f / dipHl;
        return;
    }

    auto* cl = commandList_.Get();

    if (layer && layer->Texture() && layer->State() == D3D12_RESOURCE_STATE_RENDER_TARGET) {
        auto barrier = MakeTransitionBarrier(layer->Texture(),
            D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        cl->ResourceBarrier(1, &barrier);
        layer->SetState(D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
    }

    // Restore the main render target + viewport.
    auto rtvHandle = GetSwapChainRtvHandle();
    cl->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    D3D12_VIEWPORT vp = { 0, 0, (float)viewportWidth_, (float)viewportHeight_, 0, 1 };
    D3D12_RECT scissor = { 0, 0, (LONG)viewportWidth_, (LONG)viewportHeight_ };
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);

    // Restore frame constants (DIP dimensions, matching BeginFrame).
    float dipW = (float)viewportWidth_ / dpiScale_;
    float dipH = (float)viewportHeight_ / dpiScale_;
    currentFrameConstants_.screenWidth = dipW;
    currentFrameConstants_.screenHeight = dipH;
    currentFrameConstants_.invScreenWidth = 1.0f / dipW;
    currentFrameConstants_.invScreenHeight = 1.0f / dipH;

    inRetainedCapture_ = false;
    activeRealizeLayer_ = nullptr;
    captureRtv_ = {};
    captureViewportW_ = 0;
    captureViewportH_ = 0;

    // Restore the scissor stack saved during BeginRetainedLayerCapture.
    scissorStack_ = std::move(savedScissorStack_);
    savedScissorStack_ = {};
}

void D3D12DirectRenderer::CompositeRetainedLayer(
    D3D12RetainedLayer* layer, float x, float y, float w, float h, float opacity)
{
    if (!inFrame_ || !layer || !layer->Texture() || w <= 0 || h <= 0) return;
    // Stale-generation guard: never sample a texture created on a previous
    // (removed) device — AddBitmap would hand it to this device's
    // CreateShaderResourceView, reproducing the GPU-switch AV one frame after
    // recovery. Skipping leaves the element blank for one frame; managed
    // recovery already marked the layer dirty so it re-realizes.
    if (layer->Device() != device_) return;
    // Only composite a realized (sampleable) layer.
    if (layer->State() != D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE) return;

    // The texture was sized to ceil(w*dpi) × ceil(h*dpi) at realize; if the
    // composite size matches, uvMax ≈ 1. Clamp so we never sample past the edge.
    UINT pw = (UINT)std::ceil(w * dpiScale_);
    UINT ph = (UINT)std::ceil(h * dpiScale_);
    if (pw == 0) pw = 1;
    if (ph == 0) ph = 1;
    float uvMaxX = (layer->PixelWidth()  > 0) ? std::min(1.0f, (float)pw / (float)layer->PixelWidth())  : 1.0f;
    float uvMaxY = (layer->PixelHeight() > 0) ? std::min(1.0f, (float)ph / (float)layer->PixelHeight()) : 1.0f;

    // AddBitmap applies the live transform stack (scale+translate) and ambient
    // opacity; the layer texture carries no upload buffer (RT-sourced).
    AddBitmap(x, y, w, h, opacity, layer->Texture(), swapChainFormat_, uvMaxX, uvMaxY);
}

void D3D12DirectRenderer::DestroyRetainedLayer(D3D12RetainedLayer* layer)
{
    if (!layer) return;
    if (layer->Device() != device_ && layer->CreatingDeviceRemoved()) {
        // Created on a previous device that is actually REMOVED: its backend
        // graveyard may already be destroyed, so Release()'s RetireGpuResource
        // would be a use-after-free; and nothing can be in flight on a removed
        // device, so dropping the COM references directly is safe.
        //
        // A different-but-HEALTHY creating device (staggered multi-window
        // recovery, Create-stage context swap: the handle crossed contexts via
        // the process-wide pending-destroy queue) falls through to the normal
        // delete — its own backend_ is alive (a healthy context survives while
        // any window still holds its RT) and the fence-gated graveyard must
        // keep protecting frames that may still sample the texture.
        layer->OrphanGpuResources();
        g_retainedLayerOrphanedCount.fetch_add(1, std::memory_order_relaxed);
    } else {
        g_retainedLayerGraveyardCount.fetch_add(1, std::memory_order_relaxed);
    }
    // Texture retire is fence-gated inside Release(); deleting the object is then
    // safe even if a composite quad referencing the texture is still in flight.
    delete layer;
}

void D3D12DirectRenderer::DrawOffscreenBitmap(int slot, float x, float y, float w, float h, float opacity)
{
    if (!inFrame_ || slot < 0 || slot > 1 || !offscreenRT_[slot] || !offscreenCaptureValid_[slot]) return;
    if (w <= 0 || h <= 0) return;

    // The offscreen RT is already in PIXEL_SHADER_RESOURCE state after
    // EndOffscreenCapture / BlurOffscreenSlot.  Draw it as an alpha-blended
    // textured quad so transparent regions composite correctly over the
    // existing back buffer content (the old CopyTextureRegion path overwrote
    // transparent areas with black).

    // The offscreen texture may be larger than the captured region.
    // Compute UV range so we only sample the valid portion. The divisor is the
    // recorded per-slot capture extent (physical px), the sole UV authority —
    // numerically identical to ceil(w*dpiScale_) on every path (closes F1).
    float capW = offscreenCaptureW_[slot];
    float capH = offscreenCaptureH_[slot];
    float uvMaxX = (offscreenW_ > 0 && capW > 0) ? capW / (float)offscreenW_ : 1.0f;
    float uvMaxY = (offscreenH_ > 0 && capH > 0) ? capH / (float)offscreenH_ : 1.0f;

    AddBitmap(x, y, w, h, opacity, offscreenRT_[slot].Get(), swapChainFormat_, uvMaxX, uvMaxY);

    // Flush immediately so the offscreen texture is sampled now, before a
    // subsequent BeginOffscreenCapture can clear and reuse the same slot.
    // Device-lost result needs no handling: nothing follows, EndFrame aborts.
    (void)FlushGraphicsForCompute();
}

void D3D12DirectRenderer::DrawOffscreenBitmapCropped(int slot,
    float x, float y, float w, float h,
    float uvOffsetX, float uvOffsetY, float opacity)
{
    if (!inFrame_ || slot < 0 || slot > 1 || !offscreenRT_[slot] || !offscreenCaptureValid_[slot]) return;
    if (w <= 0 || h <= 0) return;

    // Compute UV sub-region for the ELEMENT (the captured content), which occupies
    // offscreen [uvOff, uvOff+size]; the capture has extra padding beyond that for the
    // glow halo. uvMin = uvOff (asymmetry-correct); uvMax = uvOff + ELEMENT size.
    // NOT the full capture extent capW=(size+2*uvOff)*dpi — using capW oversamples by
    // uvOff, compressing/shifting the composited element up-left while the glow stays at
    // the true position, so the glow's exposed lower edge reads as a downward ghost copy.
    float uvMinXf = (offscreenW_ > 0) ? (uvOffsetX * dpiScale_) / (float)offscreenW_ : 0.0f;
    float uvMinYf = (offscreenH_ > 0) ? (uvOffsetY * dpiScale_) / (float)offscreenH_ : 0.0f;
    float uvMaxXf = (offscreenW_ > 0) ? ((uvOffsetX + w) * dpiScale_) / (float)offscreenW_ : 1.0f;
    float uvMaxYf = (offscreenH_ > 0) ? ((uvOffsetY + h) * dpiScale_) / (float)offscreenH_ : 1.0f;

    BitmapQuadInstance inst = {};
    inst.posX = x; inst.posY = y;
    inst.sizeX = w; inst.sizeY = h;
    inst.uvMinX = uvMinXf; inst.uvMinY = uvMinYf;
    inst.uvMaxX = uvMaxXf; inst.uvMaxY = uvMaxYf;
    inst.opacity = opacity * currentOpacity_;

    // Carry the full current transform into the instance (same contract as
    // AddBitmap — the VS applies it to the quad corners). pos/size stay in
    // pre-transform space; identity => bit-identical to the old inline path.
    const auto& t = transformStack_.top();
    inst.xfM11 = t.m11; inst.xfM12 = t.m12;
    inst.xfM21 = t.m21; inst.xfM22 = t.m22;
    inst.xfDx  = t.dx;  inst.xfDy  = t.dy;

    DrawBatch batch;
    batch.type = DrawBatchType::Bitmap;
    batch.instanceOffset = (uint32_t)bitmapInstances_.size();
    batch.instanceCount = 1;
    batch.sortOrder = drawOrder_++;
    batch.hasScissor = !scissorStack_.empty();
    if (batch.hasScissor) batch.scissor = scissorStack_.top();
    ResolveRoundedClipForBatch(batch);

    BitmapBatchTexture tex;
    tex.batchIndex = (uint32_t)batches_.size();
    tex.textureResource = offscreenRT_[slot].Get();
    tex.format = swapChainFormat_;
    bitmapTextures_.push_back(tex);

    batches_.push_back(batch);
    bitmapInstances_.push_back(inst);

    // Device-lost result needs no handling: nothing follows, EndFrame aborts.
    (void)FlushGraphicsForCompute();
}

// ============================================================================
// BlurOffscreenSlot — blur an offscreen capture texture in-place
// ============================================================================

ID3D12PipelineState* D3D12DirectRenderer::GetOrCreateCustomShaderPSO(const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize)
{
    if (!device_ || !rootSignature_ || !customEffectVS_ || !shaderBytecode || shaderBytecodeSize == 0) {
        return nullptr;
    }

    const uint64_t hash = HashShaderBytecode(shaderBytecode, shaderBytecodeSize);
    for (auto& entry : customShaderCache_) {
        if (entry.hash == hash &&
            entry.bytecode.size() == shaderBytecodeSize &&
            std::memcmp(entry.bytecode.data(), shaderBytecode, shaderBytecodeSize) == 0) {
            return entry.pso.Get();
        }
    }

    D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
    psoDesc.pRootSignature = rootSignature_.Get();
    psoDesc.VS = { customEffectVS_->GetBufferPointer(), customEffectVS_->GetBufferSize() };
    psoDesc.PS = { shaderBytecode, shaderBytecodeSize };

    psoDesc.BlendState.RenderTarget[0].BlendEnable = TRUE;
    psoDesc.BlendState.RenderTarget[0].SrcBlend = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlend = D3D12_BLEND_INV_SRC_ALPHA;
    psoDesc.BlendState.RenderTarget[0].BlendOp = D3D12_BLEND_OP_ADD;
    psoDesc.BlendState.RenderTarget[0].SrcBlendAlpha = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
    psoDesc.BlendState.RenderTarget[0].BlendOpAlpha = D3D12_BLEND_OP_ADD;
    psoDesc.BlendState.RenderTarget[0].RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;

    psoDesc.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
    psoDesc.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
    psoDesc.RasterizerState.DepthClipEnable = FALSE;
    psoDesc.DepthStencilState.DepthEnable = FALSE;
    psoDesc.DepthStencilState.StencilEnable = FALSE;
    psoDesc.SampleMask = UINT_MAX;
    psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
    psoDesc.NumRenderTargets = 1;
    psoDesc.RTVFormats[0] = swapChainFormat_;
    psoDesc.SampleDesc.Count = 1;

    ComPtr<ID3D12PipelineState> pso;
    HRESULT hr = device_->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&pso));
    if (FAILED(hr)) {
        LogDeviceRemovedReason("CreateCustomShaderPSO", device_, hr);
        return nullptr;
    }

    CustomShaderCacheEntry entry;
    entry.hash = hash;
    entry.bytecode.assign(shaderBytecode, shaderBytecode + shaderBytecodeSize);
    entry.pso = pso;
    customShaderCache_.push_back(std::move(entry));
    return customShaderCache_.back().pso.Get();
}

void D3D12DirectRenderer::DrawCustomShaderEffect(int slot,
    float x, float y, float w, float h,
    const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
    const float* constants, uint32_t constantFloatCount)
{
    if (!inFrame_ || slot < 0 || slot > 1 || !offscreenRT_[slot] || !offscreenCaptureValid_[slot]) {
        return;
    }
    if (w <= 0 || h <= 0) {
        return;
    }
    if (!shaderBytecode || shaderBytecodeSize == 0) {
        DrawOffscreenBitmap(slot, x, y, w, h, 1.0f);
        return;
    }

    auto* pso = GetOrCreateCustomShaderPSO(shaderBytecode, shaderBytecodeSize);
    if (!pso) {
        DrawOffscreenBitmap(slot, x, y, w, h, 1.0f);
        return;
    }

    if (!FlushGraphicsForCompute()) return;  // device lost — frame will abort

    auto* cl = commandList_.Get();
    if (offscreenRTState_[slot] != D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE) {
        auto barrier = MakeTransitionBarrier(offscreenRT_[slot].Get(),
            offscreenRTState_[slot], D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        cl->ResourceBarrier(1, &barrier);
        offscreenRTState_[slot] = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    }

    auto& fr = frames_[currentFrame_];
    const uint32_t effectiveFloatCount = constantFloatCount > 0 ? constantFloatCount : 4;
    const size_t constantBytes = static_cast<size_t>(effectiveFloatCount) * sizeof(float);
    const size_t cbSize = ((std::max<size_t>(constantBytes, 16) + 255) / 256) * 256;
    size_t cbOffset = ((uploadBufferOffset_ + 255) / 256) * 256;
    if (cbOffset + cbSize > fr.instanceCapacity) {
        // Mid-frame growth is safe — EnsureFrameInstanceCapacity parks the old
        // buffer on fr.retiredInstanceBuffers so any earlier draw's descriptors
        // remain valid until BeginFrame's fence wait clears them.
        if (!EnsureFrameInstanceCapacity(fr, cbOffset + cbSize)) {
            DrawOffscreenBitmap(slot, x, y, w, h, 1.0f);
            return;
        }
    }

    auto* cbPtr = static_cast<uint8_t*>(fr.instanceMappedPtr) + cbOffset;
    std::memset(cbPtr, 0, cbSize);
    if (constants && constantFloatCount > 0) {
        std::memcpy(cbPtr, constants, constantBytes);
    }
    uploadBufferOffset_ = cbOffset + cbSize;
    D3D12_GPU_VIRTUAL_ADDRESS cbGpuAddr = fr.instanceUploadBuffer->GetGPUVirtualAddress() + cbOffset;

    UINT frameSrvBase = currentFrame_ * frameSrvRegionSize_;
    UINT frameSrvEnd = frameSrvBase + frameSrvRegionSize_;
    UINT srvOffset = srvAllocOffset_;
    if (srvOffset < frameSrvBase || srvOffset + 2 > frameSrvEnd) {
        srvOffset = frameSrvBase;
    }
    srvAllocOffset_ = srvOffset + 2;

    auto srvCpu = srvHeap_->GetCPUDescriptorHandleForHeapStart();
    srvCpu.ptr += srvOffset * srvDescriptorSize_;

    D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
    srvDesc.Format = swapChainFormat_;
    srvDesc.Texture2D.MipLevels = 1;
    device_->CreateShaderResourceView(offscreenRT_[slot].Get(), &srvDesc, srvCpu);

    auto srvCpu2 = srvCpu;
    srvCpu2.ptr += srvDescriptorSize_;
    device_->CreateShaderResourceView(offscreenRT_[slot].Get(), &srvDesc, srvCpu2);

    cl->SetGraphicsRootSignature(rootSignature_.Get());
    ID3D12DescriptorHeap* heaps[] = { srvHeap_.Get() };
    cl->SetDescriptorHeaps(1, heaps);
    cl->SetPipelineState(pso);
    cl->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

    D3D12_VIEWPORT vp = { 0, 0, (float)viewportWidth_, (float)viewportHeight_, 0, 1 };
    D3D12_RECT scissor = { 0, 0, (LONG)viewportWidth_, (LONG)viewportHeight_ };
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);

    auto rtvHandle = GetSwapChainRtvHandle();
    cl->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    cl->SetGraphicsRootConstantBufferView(0, cbGpuAddr);

    float geomConstants[8] = {
        x, y, w, h,
        (float)viewportWidth_ / dpiScale_,
        (float)viewportHeight_ / dpiScale_,
        0.0f, 0.0f
    };
    cl->SetGraphicsRoot32BitConstants(2, 8, geomConstants, 0);

    auto srvGpu = srvHeap_->GetGPUDescriptorHandleForHeapStart();
    srvGpu.ptr += srvOffset * srvDescriptorSize_;
    cl->SetGraphicsRootDescriptorTable(1, srvGpu);

    cl->DrawInstanced(6, 1, 0, 0);
}

bool D3D12DirectRenderer::BlurOffscreenSlot(int slot, float radius)
{
    if (!inFrame_ || !blurResourcesReady_ || slot < 0 || slot > 1 ||
        !offscreenRT_[slot] || !offscreenCaptureValid_[slot]) {
        return false;
    }
    if (radius <= 0) return true; // nothing to blur

    // Scale DIP radius to physical pixels for the compute shader
    float pixelRadius = radius * dpiScale_;

    // Use the allocated offscreen texture dimensions (in physical pixels).
    UINT regionW = offscreenW_;
    UINT regionH = offscreenH_;
    if (regionW == 0 || regionH == 0) return false;

    DXGI_FORMAT fmt = swapChainFormat_;
    if (!EnsureBlurTemps(regionW, regionH)) {
        return false;
    }
    blurTempsUsedThisFrame_ = true;

    // Flush pending graphics before compute operations
    if (!FlushGraphicsForCompute()) return false;  // device lost — frame will abort

    auto* cl = commandList_.Get();

    // --- Step 1: Copy offscreenRT[slot] → blurTempA ---
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(offscreenRT_[slot].Get(),
            offscreenRTState_[slot], D3D12_RESOURCE_STATE_COPY_SOURCE);
        barriers[1] = MakeTransitionBarrier(blurTempA_.Get(),
            blurTempAState_, D3D12_RESOURCE_STATE_COPY_DEST);
        cl->ResourceBarrier(2, barriers);
        offscreenRTState_[slot] = D3D12_RESOURCE_STATE_COPY_SOURCE;
        blurTempAState_ = D3D12_RESOURCE_STATE_COPY_DEST;
    }
    {
        D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
        srcLoc.pResource = offscreenRT_[slot].Get();
        srcLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
        dstLoc.pResource = blurTempA_.Get();
        dstLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        D3D12_BOX srcBox = { 0, 0, 0, regionW, regionH, 1 };
        cl->CopyTextureRegion(&dstLoc, 0, 0, 0, &srcLoc, &srcBox);
    }

    // --- Step 2: Horizontal blur  TempA → TempB ---
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(blurTempA_.Get(),
            blurTempAState_, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        barriers[1] = MakeTransitionBarrier(blurTempB_.Get(),
            blurTempBState_, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        cl->ResourceBarrier(2, barriers);
        blurTempAState_ = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
        blurTempBState_ = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
    }

    const UINT blurSrvSlot = kMaxSrvDescriptors - 8;
    const UINT blurUavSlot = kMaxSrvDescriptors - 7;

    auto srvCpuBase = srvHeap_->GetCPUDescriptorHandleForHeapStart();
    auto srvGpuBase = srvHeap_->GetGPUDescriptorHandleForHeapStart();

    // SRV for TempA
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Format = fmt;
        srvDesc.Texture2D.MipLevels = 1;
        auto handle = srvCpuBase;
        handle.ptr += blurSrvSlot * srvDescriptorSize_;
        device_->CreateShaderResourceView(blurTempA_.Get(), &srvDesc, handle);
    }
    // UAV for TempB
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        uavDesc.Format = fmt;
        auto handle = srvCpuBase;
        handle.ptr += blurUavSlot * srvDescriptorSize_;
        device_->CreateUnorderedAccessView(blurTempB_.Get(), nullptr, &uavDesc, handle);
    }

    cl->SetComputeRootSignature(blurRootSignature_.Get());
    ID3D12DescriptorHeap* heaps[] = { srvHeap_.Get() };
    cl->SetDescriptorHeaps(1, heaps);
    cl->SetPipelineState(blurPSO_.Get());

    BlurConstants hConstants;
    hConstants.direction = 0;
    hConstants.radius = pixelRadius;
    hConstants.texWidth = regionW;
    hConstants.texHeight = regionH;
    cl->SetComputeRoot32BitConstants(0, sizeof(BlurConstants) / 4, &hConstants, 0);
    {
        auto srvGpu = srvGpuBase;
        srvGpu.ptr += blurSrvSlot * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(1, srvGpu);
        auto uavGpu = srvGpuBase;
        uavGpu.ptr += blurUavSlot * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(2, uavGpu);
    }
    cl->Dispatch((regionW + 255) / 256, regionH, 1);

    // --- Step 3: Vertical blur  TempB → TempA ---
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(blurTempB_.Get(),
            blurTempBState_, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        barriers[1] = MakeTransitionBarrier(blurTempA_.Get(),
            blurTempAState_, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        cl->ResourceBarrier(2, barriers);
        blurTempBState_ = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
        blurTempAState_ = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
    }

    const UINT blurSrvSlot2 = kMaxSrvDescriptors - 6;
    const UINT blurUavSlot2 = kMaxSrvDescriptors - 5;
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Format = fmt;
        srvDesc.Texture2D.MipLevels = 1;
        auto handle = srvCpuBase;
        handle.ptr += blurSrvSlot2 * srvDescriptorSize_;
        device_->CreateShaderResourceView(blurTempB_.Get(), &srvDesc, handle);
    }
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        uavDesc.Format = fmt;
        auto handle = srvCpuBase;
        handle.ptr += blurUavSlot2 * srvDescriptorSize_;
        device_->CreateUnorderedAccessView(blurTempA_.Get(), nullptr, &uavDesc, handle);
    }

    BlurConstants vConstants;
    vConstants.direction = 1;
    vConstants.radius = pixelRadius;
    vConstants.texWidth = regionW;
    vConstants.texHeight = regionH;
    cl->SetComputeRoot32BitConstants(0, sizeof(BlurConstants) / 4, &vConstants, 0);
    {
        auto srvGpu = srvGpuBase;
        srvGpu.ptr += blurSrvSlot2 * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(1, srvGpu);
        auto uavGpu = srvGpuBase;
        uavGpu.ptr += blurUavSlot2 * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(2, uavGpu);
    }
    cl->Dispatch((regionH + 255) / 256, regionW, 1);

    // --- Step 4: Copy blurred result back to offscreenRT[slot] ---
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(blurTempA_.Get(),
            blurTempAState_, D3D12_RESOURCE_STATE_COPY_SOURCE);
        barriers[1] = MakeTransitionBarrier(offscreenRT_[slot].Get(),
            offscreenRTState_[slot], D3D12_RESOURCE_STATE_COPY_DEST);
        cl->ResourceBarrier(2, barriers);
        blurTempAState_ = D3D12_RESOURCE_STATE_COPY_SOURCE;
        offscreenRTState_[slot] = D3D12_RESOURCE_STATE_COPY_DEST;
    }
    {
        D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
        srcLoc.pResource = blurTempA_.Get();
        srcLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
        dstLoc.pResource = offscreenRT_[slot].Get();
        dstLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        D3D12_BOX srcBox = { 0, 0, 0, regionW, regionH, 1 };
        cl->CopyTextureRegion(&dstLoc, 0, 0, 0, &srcLoc, &srcBox);
    }

    // Transition offscreenRT back to PIXEL_SHADER_RESOURCE for DrawOffscreenBitmap
    {
        auto barrier = MakeTransitionBarrier(offscreenRT_[slot].Get(),
            offscreenRTState_[slot], D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        cl->ResourceBarrier(1, &barrier);
        offscreenRTState_[slot] = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    }

    // Clean up blur temp states
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(blurTempA_.Get(),
            blurTempAState_, D3D12_RESOURCE_STATE_COMMON);
        barriers[1] = MakeTransitionBarrier(blurTempB_.Get(),
            blurTempBState_, D3D12_RESOURCE_STATE_COMMON);
        cl->ResourceBarrier(2, barriers);
        blurTempAState_ = D3D12_RESOURCE_STATE_COMMON;
        blurTempBState_ = D3D12_RESOURCE_STATE_COMMON;
    }

    // Re-bind render target and viewport for subsequent graphics draws
    // (FlushGraphicsForCompute resets command list state)
    auto rtvHandle = GetSwapChainRtvHandle();
    cl->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    D3D12_VIEWPORT vp = { 0, 0, (float)viewportWidth_, (float)viewportHeight_, 0, 1 };
    D3D12_RECT scissor = { 0, 0, (LONG)viewportWidth_, (LONG)viewportHeight_ };
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);

    return true;
}

// ============================================================================
// BlurSnapshotForGlass — blur full snapshot into blurTempA_ for liquid glass
// ============================================================================

bool D3D12DirectRenderer::BlurSnapshotForGlass(float blurRadius)
{
    if (!snapshotValid_ || !snapshotTexture_ || !blurResourcesReady_) return false;
    if (blurRadius <= 0) return false;

    UINT w = snapshotW_;
    UINT h = snapshotH_;

    DXGI_FORMAT fmt = swapChainFormat_;
    if (!EnsureBlurTemps(w, h)) {
        return false;
    }
    blurTempsUsedThisFrame_ = true;

    auto* cl = commandList_.Get();

    // Step 1: Transition snapshot to COPY_SOURCE, blurTempA to COPY_DEST
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(snapshotTexture_.Get(),
            snapshotState_, D3D12_RESOURCE_STATE_COPY_SOURCE);
        barriers[1] = MakeTransitionBarrier(blurTempA_.Get(),
            blurTempAState_, D3D12_RESOURCE_STATE_COPY_DEST);
        cl->ResourceBarrier(2, barriers);
        snapshotState_ = D3D12_RESOURCE_STATE_COPY_SOURCE;
        blurTempAState_ = D3D12_RESOURCE_STATE_COPY_DEST;
    }

    // Copy snapshot -> blurTempA (full texture)
    {
        D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
        srcLoc.pResource = snapshotTexture_.Get();
        srcLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
        dstLoc.pResource = blurTempA_.Get();
        dstLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        D3D12_BOX srcBox = { 0, 0, 0, w, h, 1 };
        cl->CopyTextureRegion(&dstLoc, 0, 0, 0, &srcLoc, &srcBox);
    }

    // Step 2: Horizontal blur  blurTempA -> blurTempB
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(blurTempA_.Get(),
            blurTempAState_, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        barriers[1] = MakeTransitionBarrier(blurTempB_.Get(),
            blurTempBState_, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        cl->ResourceBarrier(2, barriers);
        blurTempAState_ = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
        blurTempBState_ = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
    }

    const UINT blurSrvSlot = kMaxSrvDescriptors - 8;
    const UINT blurUavSlot = kMaxSrvDescriptors - 7;
    auto srvCpuBase = srvHeap_->GetCPUDescriptorHandleForHeapStart();
    auto srvGpuBase = srvHeap_->GetGPUDescriptorHandleForHeapStart();

    // SRV for blurTempA
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Format = fmt;
        srvDesc.Texture2D.MipLevels = 1;
        auto handle = srvCpuBase;
        handle.ptr += blurSrvSlot * srvDescriptorSize_;
        device_->CreateShaderResourceView(blurTempA_.Get(), &srvDesc, handle);
    }
    // UAV for blurTempB
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        uavDesc.Format = fmt;
        auto handle = srvCpuBase;
        handle.ptr += blurUavSlot * srvDescriptorSize_;
        device_->CreateUnorderedAccessView(blurTempB_.Get(), nullptr, &uavDesc, handle);
    }

    cl->SetComputeRootSignature(blurRootSignature_.Get());
    ID3D12DescriptorHeap* heaps[] = { srvHeap_.Get() };
    cl->SetDescriptorHeaps(1, heaps);
    cl->SetPipelineState(blurPSO_.Get());

    BlurConstants hConstants;
    hConstants.direction = 0;
    hConstants.radius = blurRadius;
    hConstants.texWidth = w;
    hConstants.texHeight = h;
    cl->SetComputeRoot32BitConstants(0, sizeof(BlurConstants) / 4, &hConstants, 0);

    {
        auto srvGpu = srvGpuBase;
        srvGpu.ptr += blurSrvSlot * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(1, srvGpu);
        auto uavGpu = srvGpuBase;
        uavGpu.ptr += blurUavSlot * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(2, uavGpu);
    }

    UINT groupsX = (w + 255) / 256;
    cl->Dispatch(groupsX, h, 1);

    // Step 3: Vertical blur  blurTempB -> blurTempA
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(blurTempB_.Get(),
            blurTempBState_, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        barriers[1] = MakeTransitionBarrier(blurTempA_.Get(),
            blurTempAState_, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        cl->ResourceBarrier(2, barriers);
        blurTempBState_ = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
        blurTempAState_ = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
    }

    const UINT blurSrvSlot2 = kMaxSrvDescriptors - 6;
    const UINT blurUavSlot2 = kMaxSrvDescriptors - 5;
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Format = fmt;
        srvDesc.Texture2D.MipLevels = 1;
        auto handle = srvCpuBase;
        handle.ptr += blurSrvSlot2 * srvDescriptorSize_;
        device_->CreateShaderResourceView(blurTempB_.Get(), &srvDesc, handle);
    }
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        uavDesc.Format = fmt;
        auto handle = srvCpuBase;
        handle.ptr += blurUavSlot2 * srvDescriptorSize_;
        device_->CreateUnorderedAccessView(blurTempA_.Get(), nullptr, &uavDesc, handle);
    }

    BlurConstants vConstants;
    vConstants.direction = 1;
    vConstants.radius = blurRadius;
    vConstants.texWidth = w;
    vConstants.texHeight = h;
    cl->SetComputeRoot32BitConstants(0, sizeof(BlurConstants) / 4, &vConstants, 0);

    {
        auto srvGpu = srvGpuBase;
        srvGpu.ptr += blurSrvSlot2 * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(1, srvGpu);
        auto uavGpu = srvGpuBase;
        uavGpu.ptr += blurUavSlot2 * srvDescriptorSize_;
        cl->SetComputeRootDescriptorTable(2, uavGpu);
    }

    UINT groupsY = (h + 255) / 256;
    cl->Dispatch(groupsY, w, 1);

    // Step 4: Transition blurTempA to PIXEL_SHADER_RESOURCE, blurTempB to COMMON
    {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = MakeTransitionBarrier(blurTempA_.Get(),
            blurTempAState_, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        barriers[1] = MakeTransitionBarrier(blurTempB_.Get(),
            blurTempBState_, D3D12_RESOURCE_STATE_COMMON);
        cl->ResourceBarrier(2, barriers);
        blurTempAState_ = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
        blurTempBState_ = D3D12_RESOURCE_STATE_COMMON;
    }

    // Restore snapshot to PIXEL_SHADER_RESOURCE
    {
        auto barrier = MakeTransitionBarrier(snapshotTexture_.Get(),
            snapshotState_, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        cl->ResourceBarrier(1, &barrier);
        snapshotState_ = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    }

    return true;
}

// ============================================================================
// Liquid Glass — Full Effect Rendering
// ============================================================================

bool D3D12DirectRenderer::CreateLiquidGlassResources()
{
    if (!device_ || lgResourcesReady_) return lgResourcesReady_;

    // --- Compile shaders ---
    UINT compileFlags = 0;
#ifdef _DEBUG
    compileFlags = D3DCOMPILE_DEBUG | D3DCOMPILE_SKIP_OPTIMIZATION;
#else
    compileFlags = D3DCOMPILE_OPTIMIZATION_LEVEL3;
#endif

    auto compileShader = [&](const char* source, size_t sourceLen, const char* debugName,
                             const char* target, ID3DBlob** blob) -> bool {
        ComPtr<ID3DBlob> errors;
        HRESULT hr = D3DCompile(source, sourceLen, debugName,
                                nullptr, nullptr, "main", target,
                                compileFlags, 0, blob, &errors);
        if (FAILED(hr) && errors) {
            OutputDebugStringA("LiquidGlass shader error: ");
            OutputDebugStringA((const char*)errors->GetBufferPointer());
        }
        return SUCCEEDED(hr);
    };

    using namespace shader_source;

    if (!compileShader(kLiquidGlassVS, sizeof(kLiquidGlassVS) - 1, "liquid_glass.vs.hlsl", "vs_5_1", &lgVS_))
        return false;
    if (!compileShader(kLiquidGlassPS, sizeof(kLiquidGlassPS) - 1, "liquid_glass.ps.hlsl", "ps_5_1", &lgPS_))
        return false;

    // --- Root signature for liquid glass ---
    // [0] Root CBV b0 — FrameConstants (screenSize, invScreenSize)
    // [1] Root CBV b1 — LiquidGlassParams (192 bytes)
    // [2] Root CBV b2 — LiquidGlassGeom (16 bytes, for VS)
    // [3] Descriptor table — SRV t1 (snapshot texture)
    // Static sampler s0 — linear clamp

    D3D12_DESCRIPTOR_RANGE srvRange = {};
    srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
    srvRange.NumDescriptors = 1;
    srvRange.BaseShaderRegister = 1;  // t1
    srvRange.RegisterSpace = 0;
    srvRange.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

    D3D12_ROOT_PARAMETER params[4] = {};
    // [0] Root CBV b0 — FrameConstants
    params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
    params[0].Descriptor.ShaderRegister = 0;
    params[0].Descriptor.RegisterSpace = 0;
    params[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // [1] Root CBV b1 — LiquidGlassParams
    params[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
    params[1].Descriptor.ShaderRegister = 1;
    params[1].Descriptor.RegisterSpace = 0;
    params[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

    // [2] Root 32-bit constants b2 — LiquidGlassGeom (4 floats)
    params[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
    params[2].Constants.ShaderRegister = 2;
    params[2].Constants.RegisterSpace = 0;
    params[2].Constants.Num32BitValues = 4;
    params[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_VERTEX;

    // [3] Descriptor table — SRV t1 (snapshot)
    params[3].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    params[3].DescriptorTable.NumDescriptorRanges = 1;
    params[3].DescriptorTable.pDescriptorRanges = &srvRange;
    params[3].ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

    D3D12_STATIC_SAMPLER_DESC sampler = {};
    sampler.Filter = D3D12_FILTER_MIN_MAG_MIP_LINEAR;
    sampler.AddressU = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    sampler.AddressV = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    sampler.AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    sampler.ShaderRegister = 0;
    sampler.ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

    D3D12_ROOT_SIGNATURE_DESC rootSigDesc = {};
    rootSigDesc.NumParameters = 4;
    rootSigDesc.pParameters = params;
    rootSigDesc.NumStaticSamplers = 1;
    rootSigDesc.pStaticSamplers = &sampler;
    rootSigDesc.Flags = D3D12_ROOT_SIGNATURE_FLAG_NONE;

    ComPtr<ID3DBlob> signature, error;
    if (FAILED(D3D12SerializeRootSignature(&rootSigDesc, D3D_ROOT_SIGNATURE_VERSION_1_0, &signature, &error))) {
        if (error) OutputDebugStringA((const char*)error->GetBufferPointer());
        return false;
    }
    if (FAILED(device_->CreateRootSignature(0, signature->GetBufferPointer(), signature->GetBufferSize(),
                                            IID_PPV_ARGS(&lgRootSignature_))))
        return false;

    // --- PSO ---
    D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
    psoDesc.pRootSignature = lgRootSignature_.Get();
    psoDesc.VS = { lgVS_->GetBufferPointer(), lgVS_->GetBufferSize() };
    psoDesc.PS = { lgPS_->GetBufferPointer(), lgPS_->GetBufferSize() };

    // Premultiplied alpha blending
    psoDesc.BlendState.RenderTarget[0].BlendEnable = TRUE;
    psoDesc.BlendState.RenderTarget[0].SrcBlend = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlend = D3D12_BLEND_INV_SRC_ALPHA;
    psoDesc.BlendState.RenderTarget[0].BlendOp = D3D12_BLEND_OP_ADD;
    psoDesc.BlendState.RenderTarget[0].SrcBlendAlpha = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
    psoDesc.BlendState.RenderTarget[0].BlendOpAlpha = D3D12_BLEND_OP_ADD;
    psoDesc.BlendState.RenderTarget[0].RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;

    psoDesc.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
    psoDesc.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
    psoDesc.RasterizerState.DepthClipEnable = FALSE;
    psoDesc.DepthStencilState.DepthEnable = FALSE;
    psoDesc.SampleMask = UINT_MAX;
    psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
    psoDesc.NumRenderTargets = 1;
    psoDesc.RTVFormats[0] = swapChainFormat_;
    psoDesc.SampleDesc.Count = 1;

    if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&lgPSO_))))
        return false;

    // --- Constants upload buffer (persistently mapped) ---
    {
        auto heapProps = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
        // 256-byte aligned for CBV
        auto bufDesc = MakeBufferDesc(256);
        if (FAILED(device_->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &bufDesc,
                D3D12_RESOURCE_STATE_GENERIC_READ, nullptr, IID_PPV_ARGS(&lgConstantsBuffer_))))
            return false;
        lgConstantsBuffer_->SetName(L"JaliumLiquidGlassConstants");  // [JALIUM-921 diag]
        lgConstantsBuffer_->Map(0, nullptr, &lgConstantsMapped_);
    }

    lgResourcesReady_ = true;
    return true;
}

void D3D12DirectRenderer::DrawLiquidGlass(
    float x, float y, float w, float h,
    float cornerRadius, float blurRadius,
    float refractionAmount, float chromaticAberration,
    float tintR, float tintG, float tintB, float tintOpacity,
    float lightX, float lightY, float highlightBoost,
    int shapeType, float shapeExponent,
    int neighborCount, float fusionRadius,
    const float* neighborData)
{
    if (!inFrame_ || w <= 0 || h <= 0) return;
    if (!snapshotValid_ || !snapshotTexture_) return;

    // Ensure liquid glass resources are created
    if (!lgResourcesReady_ && !CreateLiquidGlassResources()) {
        // Fallback to simple blur + tint
        DrawSnapshotBlurred(x, y, w, h, blurRadius, tintR, tintG, tintB, tintOpacity, cornerRadius);
        return;
    }

    // Flush pending batched draws before compute blur + liquid glass pipeline
    if (!FlushGraphicsForCompute()) return;  // device lost — frame will abort

    // Reserve per-call constant buffer space before mutating blur temp states.
    auto& fr = frames_[currentFrame_];
    constexpr size_t cbAligned = 256; // D3D12 CBV alignment
    size_t cbOffset = ((uploadBufferOffset_ + cbAligned - 1) / cbAligned) * cbAligned;
    if (cbOffset + cbAligned > fr.instanceCapacity) {
        // Mid-frame growth is safe — the old buffer is parked on
        // fr.retiredInstanceBuffers and freed once BeginFrame's fence wait
        // confirms the GPU is done with it.  Fall back only if allocation fails.
        if (!EnsureFrameInstanceCapacity(fr, cbOffset + cbAligned)) {
            DrawSnapshotBlurred(x, y, w, h, blurRadius, tintR, tintG, tintB, tintOpacity, cornerRadius);
            return;
        }
    }

    // Blur the snapshot for refraction sampling
    bool hasBlurred = BlurSnapshotForGlass(blurRadius);
    if (!hasBlurred) {
        // Fallback: draw blur+tint directly
        DrawSnapshotBlurred(x, y, w, h, blurRadius, tintR, tintG, tintB, tintOpacity, cornerRadius);
        return;
    }

    // --- Fill constants (matching original D2D1 implementation) ---
    LiquidGlassConstants cb = {};
    cb.glassX = x; cb.glassY = y; cb.glassW = w; cb.glassH = h;
    cb.cornerRadius = cornerRadius;

    // Refraction height: match original formula
    float refrH = (std::min)(refractionAmount * 0.667f, 40.0f);
    cb.refractionHeight = refrH;
    cb.refractionAmount = refractionAmount;
    cb.chromaticAberration = chromaticAberration;

    cb.vibrancy = 1.5f;
    cb.tintR = tintR; cb.tintG = tintG; cb.tintB = tintB;
    cb.tintOpacity = tintOpacity;
    cb.highlightOpacity = 0.55f + highlightBoost * 0.3f;

    // Pass light position directly (shader does per-pixel calculation)
    cb.lightPosX = lightX;
    cb.lightPosY = lightY;

    cb.shadowOffset = 3.0f;
    cb.shadowRadius = 8.0f;
    cb.shadowOpacity = 0.12f;

    // Blur texture dimensions for UV mapping (DIP-equivalent so DIP coords produce correct UVs)
    cb.blurTexW = (float)blurTempW_ / dpiScale_;
    cb.blurTexH = (float)blurTempH_ / dpiScale_;

    cb.scrW = (float)viewportWidth_ / dpiScale_;
    cb.scrH = (float)viewportHeight_ / dpiScale_;
    cb.shapeType = (float)shapeType;
    cb.shapeN = shapeExponent;

    int nc = (std::min)(neighborCount, 4);
    cb.neighborCount = (float)nc;
    cb.fusionRadius = fusionRadius;

    // Fill neighbor data (each neighbor: x, y, w, h, cornerRadius)
    if (neighborData && nc > 0) {
        if (nc > 0) { cb.n0x = neighborData[0]; cb.n0y = neighborData[1]; cb.n0w = neighborData[2]; cb.n0h = neighborData[3]; }
        if (nc > 1) { cb.n1x = neighborData[5]; cb.n1y = neighborData[6]; cb.n1w = neighborData[7]; cb.n1h = neighborData[8]; }
        if (nc > 2) { cb.n2x = neighborData[10]; cb.n2y = neighborData[11]; cb.n2w = neighborData[12]; cb.n2h = neighborData[13]; }
        if (nc > 3) { cb.n3x = neighborData[15]; cb.n3y = neighborData[16]; cb.n3w = neighborData[17]; cb.n3h = neighborData[18]; }
        float radii[4] = { cornerRadius, cornerRadius, cornerRadius, cornerRadius };
        if (nc > 0) radii[0] = neighborData[4];
        if (nc > 1) radii[1] = neighborData[9];
        if (nc > 2) radii[2] = neighborData[14];
        if (nc > 3) radii[3] = neighborData[19];
        cb.n0r = radii[0]; cb.n1r = radii[1]; cb.n2r = radii[2]; cb.n3r = radii[3];
    }

    // Upload constants to per-call region of the frame upload buffer.
    // Each DrawLiquidGlass call needs its own 256-byte aligned region
    // (the GPU hasn't executed earlier calls yet, so they must not share memory).
    memcpy((uint8_t*)fr.instanceMappedPtr + cbOffset, &cb, sizeof(cb));
    uploadBufferOffset_ = cbOffset + cbAligned;
    D3D12_GPU_VIRTUAL_ADDRESS cbGpuAddr = fr.instanceUploadBuffer->GetGPUVirtualAddress() + cbOffset;

    auto* cl = commandList_.Get();

    // --- Switch to liquid glass pipeline ---
    cl->SetGraphicsRootSignature(lgRootSignature_.Get());
    cl->SetPipelineState(lgPSO_.Get());
    cl->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

    D3D12_VIEWPORT vp = { 0, 0, (float)viewportWidth_, (float)viewportHeight_, 0, 1 };
    D3D12_RECT scissor = { 0, 0, (LONG)viewportWidth_, (LONG)viewportHeight_ };
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);

    auto rtvHandle = GetSwapChainRtvHandle();
    cl->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    // Bind frame constants (b0) — re-assert FULL VIEWPORT size first.
    // A mid-frame offscreen/retained capture (DrawOuterGlowEffect etc.) overwrites
    // currentFrameConstants_ with a sub-region size, and the old code bound the bare
    // ring-buffer base (offset 0) — undefined content (stale capture size or a previous
    // frame's residue). The glass VS reads invScreenSize from b0 to map DIP→NDC, so a
    // wrong screen size placed the quad off-panel (a green block over the sidebar,
    // during AND after resize). Restore viewport size, commit to a FRESH ring slot,
    // and bind that slot — never offset 0.
    currentFrameConstants_.screenWidth = (float)viewportWidth_ / dpiScale_;
    currentFrameConstants_.screenHeight = (float)viewportHeight_ / dpiScale_;
    currentFrameConstants_.invScreenWidth = dpiScale_ / (float)viewportWidth_;
    currentFrameConstants_.invScreenHeight = dpiScale_ / (float)viewportHeight_;
    UINT lgFcOffset = fr.constantsRingOffset;
    if (lgFcOffset + 256 > kConstantsRingSize) lgFcOffset = 0;
    memcpy((uint8_t*)fr.constantsMappedPtr + lgFcOffset, &currentFrameConstants_, sizeof(DirectFrameConstants));
    fr.constantsRingOffset = lgFcOffset + 256;
    D3D12_GPU_VIRTUAL_ADDRESS lgFcAddr = fr.constantsBuffer->GetGPUVirtualAddress() + lgFcOffset;
    cl->SetGraphicsRootConstantBufferView(0, lgFcAddr);

    // Bind liquid glass params (b1) — unique per call
    cl->SetGraphicsRootConstantBufferView(1, cbGpuAddr);

    // Bind geometry constants (b2) — glass rect for VS
    LiquidGlassGeom geom = { x, y, w, h };
    cl->SetGraphicsRoot32BitConstants(2, 4, &geom, 0);

    // Bind blurred snapshot texture as SRV t1
    ID3D12DescriptorHeap* heaps[] = { srvHeap_.Get() };
    cl->SetDescriptorHeaps(1, heaps);

    // Allocate SRV slot within the current frame's region to avoid cross-frame descriptor races
    UINT frameSrvBase = currentFrame_ * frameSrvRegionSize_;
    UINT frameSrvEnd = frameSrvBase + frameSrvRegionSize_;
    UINT lgSrvOffset = srvAllocOffset_;
    if (lgSrvOffset + 1 > frameSrvEnd) lgSrvOffset = frameSrvBase;
    srvAllocOffset_ = lgSrvOffset + 1;

    auto srvCpu = srvHeap_->GetCPUDescriptorHandleForHeapStart();
    srvCpu.ptr += lgSrvOffset * srvDescriptorSize_;

    D3D12_SHADER_RESOURCE_VIEW_DESC texSrv = {};
    texSrv.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
    texSrv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
    texSrv.Format = swapChainFormat_;
    texSrv.Texture2D.MipLevels = 1;
    // Bind blurTempA_ (blurred snapshot) instead of raw snapshot
    device_->CreateShaderResourceView(blurTempA_.Get(), &texSrv, srvCpu);

    auto srvGpu = srvHeap_->GetGPUDescriptorHandleForHeapStart();
    srvGpu.ptr += lgSrvOffset * srvDescriptorSize_;
    cl->SetGraphicsRootDescriptorTable(3, srvGpu);

    // Draw 6 vertices (2 triangles forming a quad)
    cl->DrawInstanced(6, 1, 0, 0);

    // This renders straight to the swap-chain back buffer (bypassing AddXxx),
    // so it must advance drawOrder_ to keep CaptureSnapshot's fast-skip
    // invariant intact. Without it, a second non-fused glass/backdrop in the
    // same frame would reuse the snapshot captured before this panel was drawn,
    // rendering its refraction as if this panel never existed.
    drawOrder_++;

    // Transition blurTempA_ back to COMMON for future reuse
    {
        auto barrier = MakeTransitionBarrier(blurTempA_.Get(),
            blurTempAState_, D3D12_RESOURCE_STATE_COMMON);
        cl->ResourceBarrier(1, &barrier);
        blurTempAState_ = D3D12_RESOURCE_STATE_COMMON;
    }

    // --- Restore previous pipeline state ---
    cl->SetGraphicsRootSignature(rootSignature_.Get());
    cl->SetDescriptorHeaps(1, heaps);
    cl->SetGraphicsRootConstantBufferView(0, lgFcAddr);
    cl->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);
}

} // namespace jalium
