#include "d3d12_ink_layer.h"
#include "d3d12_backend.h"
#include "jalium_internal.h"

#include <cstring>
#include <memory>
#include <unordered_map>
#include <mutex>
#include <algorithm>

namespace jalium {

namespace {

// Per-device singleton map for the shared brush-shader pipeline. Brush
// dispatches on the same device all share one root signature + VS.
std::mutex                                                              s_pipelineMutex;
std::unordered_map<ID3D12Device*, std::unique_ptr<D3D12BrushShaderPipeline>> s_pipelines;

// If teardown cannot establish/wait for a completion marker, ownership must
// outlive the bitmap. Retry retirement at later batch boundaries rather than
// blocking on an unsignalled value or releasing GPU dependencies prematurely.
struct DeferredInkBatch
{
    ComPtr<ID3D12Device> device;
    ComPtr<ID3D12CommandQueue> queue;
    ComPtr<ID3D12Fence> fence;
    uint64_t fenceValue = 0;
    bool needsSignal = false;
    std::vector<ComPtr<IUnknown>> dependencies;
};

std::mutex s_deferredInkMutex;
std::vector<DeferredInkBatch> s_deferredInkBatches;

} // namespace

// Declared in d3d12_ink_layer.h. File-local state above keeps the registry
// contained to this TU; the public entry point lets backend shutdown drain it.
void ReclaimDeferredInkBatches()
{
    std::lock_guard<std::mutex> lock(s_deferredInkMutex);
    for (auto it = s_deferredInkBatches.begin(); it != s_deferredInkBatches.end();)
    {
        auto& batch = *it;
        const bool deviceLost = FAILED(batch.device->GetDeviceRemovedReason());
        if (!deviceLost && batch.needsSignal)
        {
            if (SUCCEEDED(batch.queue->Signal(batch.fence.Get(), batch.fenceValue)))
                batch.needsSignal = false;
        }
        if (deviceLost || (!batch.needsSignal &&
                           batch.fence->GetCompletedValue() >= batch.fenceValue))
            it = s_deferredInkBatches.erase(it);
        else
            ++it;
    }
}

D3D12BrushShaderPipeline* D3D12InkLayerBitmap::SharedPipeline(ID3D12Device* device)
{
    if (!device) return nullptr;
    std::lock_guard<std::mutex> lock(s_pipelineMutex);
    auto it = s_pipelines.find(device);
    if (it != s_pipelines.end()) return it->second.get();

    auto pipe = std::make_unique<D3D12BrushShaderPipeline>(device);
    if (!pipe->Initialize()) return nullptr;
    auto* raw = pipe.get();
    s_pipelines[device] = std::move(pipe);
    return raw;
}

D3D12InkLayerBitmap::D3D12InkLayerBitmap(ID3D12Device* device, ID3D12CommandQueue* queue)
    : device_(device), queue_(queue)
{
    fenceEvent_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);
}

D3D12InkLayerBitmap::~D3D12InkLayerBitmap()
{
    // ReleaseResources flushes any open batch (blocking) before the texture
    // is freed — see its FlushPending(true).
    if (!ReleaseResources()) DeferInFlightResources();
    if (fenceEvent_)
    {
        CloseHandle(fenceEvent_);
        fenceEvent_ = nullptr;
    }
}

bool D3D12InkLayerBitmap::Initialize(uint32_t width, uint32_t height)
{
    if (!device_ || !queue_) return false;

    if (!CreateResources(width, height)) return false;
    if (!EnsureDispatchResources())      return false;
    Clear(0, 0, 0, 0);
    // The layer must actually be cleared before it's first used — commit the
    // one-off clear batch now (a single synchronous submit is acceptable here).
    return batchOpen_ && FlushPending(true);
}

bool D3D12InkLayerBitmap::Resize(uint32_t width, uint32_t height)
{
    if (width == width_ && height == height_) return true;
    // ReleaseResources flushes the open batch before dropping the old texture.
    if (!ReleaseResources()) return false;
    if (!CreateResources(width, height)) return false;
    Clear(0, 0, 0, 0);
    return batchOpen_ && FlushPending(true);
}

bool D3D12InkLayerBitmap::CreateResources(uint32_t width, uint32_t height)
{
    if (!device_ || width == 0 || height == 0) return false;

    // Texture
    D3D12_HEAP_PROPERTIES heapProps = {};
    heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;

    D3D12_RESOURCE_DESC desc = {};
    desc.Dimension          = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    desc.Alignment          = 0;
    desc.Width              = width;
    desc.Height             = height;
    desc.DepthOrArraySize   = 1;
    desc.MipLevels          = 1;
    desc.Format             = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count   = 1;
    desc.SampleDesc.Quality = 0;
    desc.Layout             = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    desc.Flags              = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;

    D3D12_CLEAR_VALUE clearVal = {};
    clearVal.Format   = DXGI_FORMAT_R8G8B8A8_UNORM;
    clearVal.Color[0] = 0; clearVal.Color[1] = 0; clearVal.Color[2] = 0; clearVal.Color[3] = 0;

    HRESULT hr = device_->CreateCommittedResource(
        &heapProps, D3D12_HEAP_FLAG_NONE,
        &desc, D3D12_RESOURCE_STATE_RENDER_TARGET, &clearVal,
        IID_PPV_ARGS(texture_.GetAddressOf()));
    if (FAILED(hr)) return false;

    textureState_ = D3D12_RESOURCE_STATE_RENDER_TARGET;
    width_        = width;
    height_       = height;

    // RTV heap (1 entry)
    D3D12_DESCRIPTOR_HEAP_DESC rtvDesc = {};
    rtvDesc.Type          = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
    rtvDesc.NumDescriptors = 1;
    rtvDesc.Flags         = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
    hr = device_->CreateDescriptorHeap(&rtvDesc, IID_PPV_ARGS(rtvHeap_.GetAddressOf()));
    if (FAILED(hr)) return false;
    rtvCpu_ = rtvHeap_->GetCPUDescriptorHandleForHeapStart();

    D3D12_RENDER_TARGET_VIEW_DESC rtvViewDesc = {};
    rtvViewDesc.Format        = DXGI_FORMAT_R8G8B8A8_UNORM;
    rtvViewDesc.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2D;
    device_->CreateRenderTargetView(texture_.Get(), &rtvViewDesc, rtvCpu_);

    // SRV heap — GPU visible so the main RT's blit PSO can sample it
    D3D12_DESCRIPTOR_HEAP_DESC srvDesc = {};
    srvDesc.Type          = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
    srvDesc.NumDescriptors = 1;
    srvDesc.Flags         = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
    hr = device_->CreateDescriptorHeap(&srvDesc, IID_PPV_ARGS(srvHeap_.GetAddressOf()));
    if (FAILED(hr)) return false;
    srvCpu_ = srvHeap_->GetCPUDescriptorHandleForHeapStart();
    srvGpu_ = srvHeap_->GetGPUDescriptorHandleForHeapStart();

    D3D12_SHADER_RESOURCE_VIEW_DESC srvViewDesc = {};
    srvViewDesc.Format                  = DXGI_FORMAT_R8G8B8A8_UNORM;
    srvViewDesc.ViewDimension           = D3D12_SRV_DIMENSION_TEXTURE2D;
    srvViewDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
    srvViewDesc.Texture2D.MipLevels     = 1;
    device_->CreateShaderResourceView(texture_.Get(), &srvViewDesc, srvCpu_);

    return true;
}

bool D3D12InkLayerBitmap::ReleaseResources()
{
    // Commit + drain any batch (open or already submitted) that still
    // references the backing texture before dropping it — otherwise the GPU
    // could be reading a freed texture (D3D12 error → device removal).
    if (!FlushPending(true) && device_ && SUCCEEDED(device_->GetDeviceRemovedReason()))
        return false;
    retiredArena_.clear();   // fence complete → retired arenas are idle
    batchPsos_.clear();

    texture_.Reset();
    rtvHeap_.Reset();
    srvHeap_.Reset();
    width_ = height_ = 0;
    textureState_ = D3D12_RESOURCE_STATE_COMMON;
    return true;
}

void D3D12InkLayerBitmap::DeferInFlightResources()
{
    DeferredInkBatch batch;
    batch.device = device_;
    batch.queue = queue_;
    batch.fence = fence_;
    batch.needsSignal = batchNeedsSignal_;
    batch.fenceValue = batchNeedsSignal_ ? fenceValue_ + 1 : lastSubmittedFenceValue_;
    auto keep = [&](auto& resource) {
        if (resource) batch.dependencies.emplace_back(resource.Get());
    };
    keep(texture_);
    keep(rtvHeap_);
    keep(srvHeap_);
    keep(cmdAlloc_);
    keep(cmdList_);
    keep(arena_);
    for (auto& arena : retiredArena_) keep(arena);
    for (auto& pso : batchPsos_) keep(pso);
    std::lock_guard<std::mutex> lock(s_deferredInkMutex);
    s_deferredInkBatches.push_back(std::move(batch));
}

bool D3D12InkLayerBitmap::EnsureDispatchResources()
{
    if (!fenceEvent_) return false;

    HRESULT hr;
    if (!cmdAlloc_)
    {
        hr = device_->CreateCommandAllocator(
            D3D12_COMMAND_LIST_TYPE_DIRECT,
            IID_PPV_ARGS(cmdAlloc_.GetAddressOf()));
        if (FAILED(hr)) return false;
    }

    if (!cmdList_)
    {
        hr = device_->CreateCommandList(
            0, D3D12_COMMAND_LIST_TYPE_DIRECT,
            cmdAlloc_.Get(), nullptr,
            IID_PPV_ARGS(cmdList_.GetAddressOf()));
        if (FAILED(hr)) return false;
        cmdList_->SetName(L"JaliumInkCmdList");  // [JALIUM-921 diag]
        if (FAILED(cmdList_->Close()))
        {
            cmdList_.Reset();
            return false;
        }
    }

    if (!fence_)
    {
        hr = device_->CreateFence(0, D3D12_FENCE_FLAG_NONE,
                                  IID_PPV_ARGS(fence_.GetAddressOf()));
        if (FAILED(hr)) return false;
    }

    // Upload storage (b0 cbuffer / b1 user params / t0 stroke points) is no
    // longer per-object; each dispatch allocates a private slot out of the
    // per-batch arena (see ArenaAlloc).
    return true;
}

int D3D12InkLayerBitmap::TakeBatchError()
{
    const int error = batchError_;
    batchError_ = 0;
    return error;
}

bool D3D12InkLayerBitmap::WaitForFence(uint64_t value)
{
    if (!fence_ || value == 0) return true;
    if (fence_->GetCompletedValue() >= value) return true;
    if (FAILED(fence_->SetEventOnCompletion(value, fenceEvent_))) return false;
    return WaitForSingleObject(fenceEvent_, INFINITE) == WAIT_OBJECT_0;
}

bool D3D12InkLayerBitmap::BeginBatchIfNeeded()
{
    if (batchOpen_) return true;
    ReclaimDeferredInkBatches();
    if (!device_ || FAILED(device_->GetDeviceRemovedReason()) ||
        !EnsureDispatchResources() || !SignalPendingBatch())
    {
        batchError_ = JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
        return false;
    }

    // Only one batch is ever in flight: the command allocator can't be Reset
    // until the previously-submitted batch is GPU-complete. In steady state
    // the previous frame's batch has long finished, so this is a no-op.
    if (!WaitForFence(lastSubmittedFenceValue_))
    {
        batchError_ = JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
        return false;
    }

    // Previous batch complete → any arena buffer retired during it is idle.
    retiredArena_.clear();
    batchPsos_.clear();

    HRESULT hr = cmdAlloc_->Reset();
    if (FAILED(hr))
    {
        batchError_ = JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
        return false;
    }
    hr = cmdList_->Reset(cmdAlloc_.Get(), nullptr);
    if (FAILED(hr))
    {
        batchError_ = JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
        cmdList_.Reset();
        return false;
    }

    arenaOffset_ = 0;
    currentPso_  = nullptr;  // list was Reset without an initial PSO
    batchInitialTextureState_ = textureState_;
    batchOpen_   = true;
    return true;
}

bool D3D12InkLayerBitmap::SignalPendingBatch()
{
    if (!batchNeedsSignal_) return true;
    const uint64_t value = fenceValue_ + 1;
    if (FAILED(queue_->Signal(fence_.Get(), value)))
    {
        batchError_ = JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
        return false;
    }
    fenceValue_ = lastSubmittedFenceValue_ = value;
    batchNeedsSignal_ = false;
    return true;
}

bool D3D12InkLayerBitmap::FlushPending(bool wait)
{
    if (batchOpen_)
    {
        const HRESULT hr = cmdList_->Close();
        batchOpen_ = false;
        if (FAILED(hr))
        {
            // A command list whose Close failed cannot be Reset. None of its
            // barriers executed, so discard it and roll back CPU state too.
            cmdList_.Reset();
            textureState_ = batchInitialTextureState_;
            batchError_ = JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
            return false;
        }
        ID3D12CommandList* lists[] = { cmdList_.Get() };
        queue_->ExecuteCommandLists(1, lists);
        batchNeedsSignal_ = true;
    }

    // Publish only a successfully queued fence value. If Signal failed, the
    // next call retries the marker before any allocator/upload/PSO is reused.
    if (!SignalPendingBatch() ||
        (device_ && FAILED(device_->GetDeviceRemovedReason())) ||
        (wait && !WaitForFence(lastSubmittedFenceValue_)))
    {
        batchError_ = JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
        return false;
    }
    return true;
}

bool D3D12InkLayerBitmap::ArenaAlloc(uint64_t bytes, void** cpuOut,
                                     D3D12_GPU_VIRTUAL_ADDRESS* gpuOut)
{
    // 256-byte alignment satisfies the root-CBV requirement and the 16-byte
    // StructuredBuffer element stride for the root-SRV.
    const uint64_t align   = 256;
    const uint64_t aligned = (bytes + align - 1) & ~(align - 1);

    if (!arena_ || arenaOffset_ + aligned > arenaCapacity_)
    {
        uint64_t newCap = arenaCapacity_ ? arenaCapacity_ : (256ull * 1024ull);
        while (newCap < arenaOffset_ + aligned) newCap *= 2;

        D3D12_HEAP_PROPERTIES upProps = {};
        upProps.Type = D3D12_HEAP_TYPE_UPLOAD;
        D3D12_RESOURCE_DESC desc = {};
        desc.Dimension        = D3D12_RESOURCE_DIMENSION_BUFFER;
        desc.Width            = newCap;
        desc.Height           = 1;
        desc.DepthOrArraySize = 1;
        desc.MipLevels        = 1;
        desc.Format           = DXGI_FORMAT_UNKNOWN;
        desc.SampleDesc.Count = 1;
        desc.Layout           = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;

        ComPtr<ID3D12Resource> fresh;
        HRESULT hr = device_->CreateCommittedResource(
            &upProps, D3D12_HEAP_FLAG_NONE,
            &desc, D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
            IID_PPV_ARGS(fresh.GetAddressOf()));
        if (FAILED(hr)) return false;

        void* mapped = nullptr;
        D3D12_RANGE nil = { 0, 0 };
        hr = fresh->Map(0, &nil, &mapped);
        if (FAILED(hr)) return false;

        // If earlier dispatches in this open batch already recorded GPU
        // references into the current arena, keep it alive until the batch's
        // fence completes; otherwise it is safe to release immediately.
        if (arena_ && arenaOffset_ > 0)
            retiredArena_.push_back(arena_);

        arena_         = fresh;
        arenaCpu_      = static_cast<uint8_t*>(mapped);
        arenaCapacity_ = newCap;
        arenaOffset_   = 0;
    }

    const uint64_t off = arenaOffset_;
    arenaOffset_ += aligned;
    *cpuOut = arenaCpu_ + off;
    *gpuOut = arena_->GetGPUVirtualAddress() + off;
    return true;
}

void D3D12InkLayerBitmap::Clear(float r, float g, float b, float a)
{
    if (!texture_ || !BeginBatchIfNeeded()) return;

    if (textureState_ != D3D12_RESOURCE_STATE_RENDER_TARGET)
    {
        D3D12_RESOURCE_BARRIER barrier = {};
        barrier.Type                   = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barrier.Transition.pResource   = texture_.Get();
        barrier.Transition.StateBefore = textureState_;
        barrier.Transition.StateAfter  = D3D12_RESOURCE_STATE_RENDER_TARGET;
        barrier.Transition.Subresource = 0;
        cmdList_->ResourceBarrier(1, &barrier);
        textureState_ = D3D12_RESOURCE_STATE_RENDER_TARGET;
    }

    const float color[4] = { r, g, b, a };
    cmdList_->ClearRenderTargetView(rtvCpu_, color, 0, nullptr);

    // Leave texture in PSR so BlitInkLayer on the window RT can sample it
    // without an explicit barrier. Next dispatch/clear restores RT state.
    D3D12_RESOURCE_BARRIER toPsr = {};
    toPsr.Type                   = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    toPsr.Transition.pResource   = texture_.Get();
    toPsr.Transition.StateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET;
    toPsr.Transition.StateAfter  = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    toPsr.Transition.Subresource = 0;
    cmdList_->ResourceBarrier(1, &toPsr);
    textureState_ = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;

    // Recorded into the current batch only — no submit, no CPU wait here.
}

int D3D12InkLayerBitmap::DispatchBrush(
    D3D12BrushShader*  shader,
    const void*        strokePoints,
    uint32_t           pointCount,
    const void*        managedConstants,
    uint32_t           managedConstantsSize,
    const void*        extraParams,
    uint32_t           extraParamsSize)
{
    // Report an asynchronous failure once; a healthy batch may retry using
    // these same handles on the next call.
    if (batchError_)                         return TakeBatchError();
    if (!shader || !shader->Pso())           return JALIUM_INK_DISPATCH_ERROR_INVALID_ARG;
    if (!texture_)                          return JALIUM_INK_DISPATCH_ERROR_INVALID_STATE;
    if (pointCount < 2)                      return JALIUM_INK_DISPATCH_ERROR_INVALID_ARG;
    if (managedConstantsSize == 0 || managedConstantsSize > 80)
        return JALIUM_INK_DISPATCH_ERROR_INVALID_ARG;

    // The shared pipeline is built lazily per device and NOT cached on
    // failure — the next dispatch retries construction, so a failed build
    // (driver hiccup, momentary OOM) is transient, not a generation loss.
    // A genuinely removed device is handled out-of-band by the window's
    // device-lost recovery, which replaces the whole RenderContext.
    auto* pipeline = SharedPipeline(device_);
    if (!pipeline) return JALIUM_INK_DISPATCH_ERROR_TRANSIENT;

    // Pixel-aligned scissor from BBoxMin/BBoxMax in the cbuffer (offsets
    // 20..27 in the 80-byte managed struct). Computed from the CPU-side
    // managed constants BEFORE the batch is opened — an empty bbox records
    // nothing and leaves CPU-tracked textureState_ untouched.
    const float* ubo = (const float*)managedConstants;
    const int minX = std::max(0, (int)std::floor(ubo[8]));
    const int minY = std::max(0, (int)std::floor(ubo[9]));
    const int maxX = std::min((int)width_,  (int)std::ceil(ubo[10]));
    const int maxY = std::min((int)height_, (int)std::ceil(ubo[11]));
    D3D12_RECT scissor = { minX, minY, maxX, maxY };
    if (scissor.right <= scissor.left || scissor.bottom <= scissor.top)
    {
        // Empty bbox — nothing to draw, nothing recorded, state unchanged.
        return JALIUM_INK_DISPATCH_OK;
    }

    const bool     hasUserParams = (extraParams != nullptr && extraParamsSize > 0);
    const uint64_t requiredSb    = (uint64_t)pointCount * 16u;
    const uint64_t alignedUser   = hasUserParams
        ? (((uint64_t)extraParamsSize + 255u) & ~(uint64_t)255u) : 0u;

    // Accumulate into this frame's batch (opened lazily on first call).
    if (!BeginBatchIfNeeded()) return TakeBatchError();

    // Per-dispatch upload slots out of the per-batch arena. Each dispatch
    // gets private, non-overlapping storage so a later dispatch can never
    // clobber an earlier one the GPU has not yet consumed.
    void* cbCpu = nullptr;
    D3D12_GPU_VIRTUAL_ADDRESS cbGpu = 0;
    if (!ArenaAlloc(256, &cbCpu, &cbGpu)) return JALIUM_INK_DISPATCH_ERROR_TRANSIENT;

    // Full 80-byte managed struct (matches the 80-byte HLSL cbuffer
    // byte-for-byte). ViewportSize lives at offset 64 and is
    // native-authoritative — the managed bytes there are overwritten with the
    // ink-layer bitmap's pixel dimensions; pad fields (offset 72..79) are
    // cleared so no managed leakage reaches the shader.
    std::memset(cbCpu, 0, 256);
    std::memcpy(cbCpu, managedConstants, managedConstantsSize);
    float* vp = (float*)cbCpu + 16;
    vp[0] = (float)width_;
    vp[1] = (float)height_;
    vp[2] = 0.0f;
    vp[3] = 0.0f;

    // Optional user params (b1), 256-byte aligned for the root CBV. Binding
    // the b0 slot again as a harmless placeholder when absent — the shader
    // won't read it unless it declares the cbuffer, so the CBV just has to
    // be valid.
    D3D12_GPU_VIRTUAL_ADDRESS userGpu = cbGpu;
    if (hasUserParams)
    {
        void* userCpu = nullptr;
        D3D12_GPU_VIRTUAL_ADDRESS userAllocGpu = 0;
        if (!ArenaAlloc(alignedUser, &userCpu, &userAllocGpu))
            return JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
        std::memset(userCpu, 0, (size_t)alignedUser);
        std::memcpy(userCpu, extraParams, extraParamsSize);
        userGpu = userAllocGpu;
    }

    void* sbCpu = nullptr;
    D3D12_GPU_VIRTUAL_ADDRESS sbGpu = 0;
    if (!ArenaAlloc(requiredSb, &sbCpu, &sbGpu))
        return JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
    std::memcpy(sbCpu, strokePoints, (size_t)requiredSb);

    // Record into the open batch. No submit / no CPU wait here — the batch is
    // submitted once at the frame boundary by FlushPending().
    if (textureState_ != D3D12_RESOURCE_STATE_RENDER_TARGET)
    {
        D3D12_RESOURCE_BARRIER barrier = {};
        barrier.Type                   = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barrier.Transition.pResource   = texture_.Get();
        barrier.Transition.StateBefore = textureState_;
        barrier.Transition.StateAfter  = D3D12_RESOURCE_STATE_RENDER_TARGET;
        barrier.Transition.Subresource = 0;
        cmdList_->ResourceBarrier(1, &barrier);
        textureState_ = D3D12_RESOURCE_STATE_RENDER_TARGET;
    }

    cmdList_->OMSetRenderTargets(1, &rtvCpu_, FALSE, nullptr);

    D3D12_VIEWPORT vp2 = {};
    vp2.TopLeftX = 0; vp2.TopLeftY = 0;
    vp2.Width    = (float)width_;
    vp2.Height   = (float)height_;
    vp2.MinDepth = 0.0f;
    vp2.MaxDepth = 1.0f;
    cmdList_->RSSetViewports(1, &vp2);
    cmdList_->RSSetScissorRects(1, &scissor);

    cmdList_->SetGraphicsRootSignature(pipeline->RootSignature());
    cmdList_->SetGraphicsRootConstantBufferView(0, cbGpu);
    cmdList_->SetGraphicsRootConstantBufferView(1, userGpu);
    cmdList_->SetGraphicsRootShaderResourceView(2, sbGpu);

    // The list was Reset without an initial PSO (Clear may run first in the
    // batch), so the PSO is always set explicitly — deduped against the
    // batch's current one. Retention is deduped too: alternating brushes must
    // not accumulate one AddRef per switch inside a single batch.
    if (currentPso_ != shader->Pso())
    {
        const bool retained = std::any_of(
            batchPsos_.begin(), batchPsos_.end(),
            [&](const ComPtr<ID3D12PipelineState>& p) { return p.Get() == shader->Pso(); });
        if (!retained)
            batchPsos_.emplace_back(shader->Pso());
        cmdList_->SetPipelineState(shader->Pso());
        currentPso_ = shader->Pso();
    }

    cmdList_->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    cmdList_->DrawInstanced(3, 1, 0, 0);

    // Leave texture in PSR so the main render target can sample it on blit
    // without needing its own barrier. Restored on next dispatch/clear.
    D3D12_RESOURCE_BARRIER toPsr = {};
    toPsr.Type                   = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    toPsr.Transition.pResource   = texture_.Get();
    toPsr.Transition.StateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET;
    toPsr.Transition.StateAfter  = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    toPsr.Transition.Subresource = 0;
    cmdList_->ResourceBarrier(1, &toPsr);
    textureState_ = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;

    return JALIUM_INK_DISPATCH_OK;
}

// ─── D3D12Backend glue ─────────────────────────────────────────────────
//
// Route IRenderBackend's backend-agnostic brush / ink-layer virtuals to
// the D3D12InkLayerBitmap + D3D12BrushShader types defined above. The
// core C API forwarders (render_target.cpp) call these via the backend
// pointer obtained from JaliumContext.

void* D3D12Backend::CreateInkLayerBitmap(uint32_t width, uint32_t height)
{
    if (!device_ || !commandQueue_) return nullptr;
    auto* bitmap = new D3D12InkLayerBitmap(device_.Get(), commandQueue_.Get());
    if (!bitmap->Initialize(width, height))
    {
        delete bitmap;
        return nullptr;
    }
    return bitmap;
}

void D3D12Backend::DestroyInkLayerBitmap(void* bitmap)
{
    delete reinterpret_cast<D3D12InkLayerBitmap*>(bitmap);
}

int32_t D3D12Backend::ResizeInkLayerBitmap(void* bitmap, uint32_t width, uint32_t height)
{
    if (!bitmap) return -1;
    return reinterpret_cast<D3D12InkLayerBitmap*>(bitmap)->Resize(width, height) ? 0 : -2;
}

void D3D12Backend::ClearInkLayerBitmap(void* bitmap, float r, float g, float b, float a)
{
    if (!bitmap) return;
    reinterpret_cast<D3D12InkLayerBitmap*>(bitmap)->Clear(r, g, b, a);
}

void* D3D12Backend::CreateBrushShader(const char* shaderKey, const char* brushMainHlsl, int32_t blendMode)
{
    if (!device_ || !brushMainHlsl) return nullptr;
    auto* pipeline = D3D12InkLayerBitmap::SharedPipeline(device_.Get());
    if (!pipeline) return nullptr;
    auto shader = pipeline->CreateBrushShader(
        shaderKey, brushMainHlsl, (BrushBlendMode)blendMode);
    return shader ? shader.release() : nullptr;
}

void D3D12Backend::DestroyBrushShader(void* shader)
{
    delete reinterpret_cast<D3D12BrushShader*>(shader);
}

int32_t D3D12Backend::DispatchBrush(void* bitmap, void* shader,
                                     const void* strokePoints, uint32_t pointCount,
                                     const void* constants,
                                     const void* extraParams, uint32_t extraParamsSize)
{
    if (!bitmap || !shader || !strokePoints || !constants || pointCount < 2)
        return JALIUM_INK_DISPATCH_ERROR_INVALID_ARG;
    return reinterpret_cast<D3D12InkLayerBitmap*>(bitmap)->DispatchBrush(
        reinterpret_cast<D3D12BrushShader*>(shader),
        strokePoints, pointCount, constants, 80,
        extraParams, extraParamsSize);
}

} // namespace jalium

