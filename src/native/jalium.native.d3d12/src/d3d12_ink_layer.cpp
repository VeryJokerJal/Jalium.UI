#include "d3d12_ink_layer.h"
#include "d3d12_backend.h"
#include "jalium_internal.h"

#include <cstring>
#include <memory>
#include <unordered_map>
#include <mutex>

namespace jalium {

namespace {

// Per-device singleton map for the shared brush-shader pipeline. Brush
// dispatches on the same device all share one root signature + VS.
std::mutex                                                              s_pipelineMutex;
std::unordered_map<ID3D12Device*, std::unique_ptr<D3D12BrushShaderPipeline>> s_pipelines;

} // namespace

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
    ReleaseResources();
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
    return true;
}

bool D3D12InkLayerBitmap::Resize(uint32_t width, uint32_t height)
{
    if (width == width_ && height == height_) return true;
    ReleaseResources();
    if (!CreateResources(width, height)) return false;
    Clear(0, 0, 0, 0);
    return true;
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

void D3D12InkLayerBitmap::ReleaseResources()
{
    texture_.Reset();
    rtvHeap_.Reset();
    srvHeap_.Reset();
    width_ = height_ = 0;
    textureState_ = D3D12_RESOURCE_STATE_COMMON;
}

bool D3D12InkLayerBitmap::EnsureDispatchResources()
{
    if (cmdList_) return true;

    HRESULT hr = device_->CreateCommandAllocator(
        D3D12_COMMAND_LIST_TYPE_DIRECT,
        IID_PPV_ARGS(cmdAlloc_.GetAddressOf()));
    if (FAILED(hr)) return false;

    hr = device_->CreateCommandList(
        0, D3D12_COMMAND_LIST_TYPE_DIRECT,
        cmdAlloc_.Get(), nullptr,
        IID_PPV_ARGS(cmdList_.GetAddressOf()));
    if (FAILED(hr)) return false;
    cmdList_->Close();

    hr = device_->CreateFence(0, D3D12_FENCE_FLAG_NONE,
                              IID_PPV_ARGS(fence_.GetAddressOf()));
    if (FAILED(hr)) return false;

    // Constants upload buffer (96 bytes = 1 CBV aligned to 256)
    D3D12_HEAP_PROPERTIES uploadProps = {};
    uploadProps.Type = D3D12_HEAP_TYPE_UPLOAD;

    D3D12_RESOURCE_DESC cbDesc = {};
    cbDesc.Dimension        = D3D12_RESOURCE_DIMENSION_BUFFER;
    cbDesc.Width            = 256;
    cbDesc.Height           = 1;
    cbDesc.DepthOrArraySize = 1;
    cbDesc.MipLevels        = 1;
    cbDesc.Format           = DXGI_FORMAT_UNKNOWN;
    cbDesc.SampleDesc.Count = 1;
    cbDesc.Layout           = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;

    hr = device_->CreateCommittedResource(
        &uploadProps, D3D12_HEAP_FLAG_NONE,
        &cbDesc, D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
        IID_PPV_ARGS(cbUpload_.GetAddressOf()));
    if (FAILED(hr)) return false;

    return true;
}

void D3D12InkLayerBitmap::ExecuteAndWait(ID3D12GraphicsCommandList* cmdList)
{
    cmdList->Close();
    ID3D12CommandList* lists[] = { cmdList };
    queue_->ExecuteCommandLists(1, lists);

    ++fenceValue_;
    queue_->Signal(fence_.Get(), fenceValue_);
    if (fence_->GetCompletedValue() < fenceValue_)
    {
        fence_->SetEventOnCompletion(fenceValue_, fenceEvent_);
        WaitForSingleObject(fenceEvent_, INFINITE);
    }
}

void D3D12InkLayerBitmap::Clear(float r, float g, float b, float a)
{
    if (!texture_ || !cmdList_) return;

    cmdAlloc_->Reset();
    cmdList_->Reset(cmdAlloc_.Get(), nullptr);

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

    ExecuteAndWait(cmdList_.Get());
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
    if (!shader || !shader->Pso())           return -1;
    if (!texture_ || !cmdList_)              return -2;
    if (pointCount < 2)                      return -3;
    if (managedConstantsSize == 0 || managedConstantsSize > 80)
        return -4;

    auto* pipeline = SharedPipeline(device_);
    if (!pipeline) return -5;

    // Grow stroke-points upload buffer if needed
    const uint32_t requiredSb = pointCount * 16u;
    if (sbUploadCapacity_ < requiredSb || !sbUpload_)
    {
        sbUpload_.Reset();
        uint32_t cap = 1;
        while (cap < requiredSb) cap *= 2;
        if (cap < 256) cap = 256;

        D3D12_HEAP_PROPERTIES upProps = {};
        upProps.Type = D3D12_HEAP_TYPE_UPLOAD;
        D3D12_RESOURCE_DESC sbDesc = {};
        sbDesc.Dimension        = D3D12_RESOURCE_DIMENSION_BUFFER;
        sbDesc.Width            = cap;
        sbDesc.Height           = 1;
        sbDesc.DepthOrArraySize = 1;
        sbDesc.MipLevels        = 1;
        sbDesc.Format           = DXGI_FORMAT_UNKNOWN;
        sbDesc.SampleDesc.Count = 1;
        sbDesc.Layout           = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
        HRESULT hr = device_->CreateCommittedResource(
            &upProps, D3D12_HEAP_FLAG_NONE,
            &sbDesc, D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
            IID_PPV_ARGS(sbUpload_.GetAddressOf()));
        if (FAILED(hr)) return -6;
        sbUploadCapacity_ = cap;
    }

    // Upload stroke points
    {
        void* mapped = nullptr;
        D3D12_RANGE nil = { 0, 0 };
        sbUpload_->Map(0, &nil, &mapped);
        std::memcpy(mapped, strokePoints, requiredSb);
        sbUpload_->Unmap(0, nullptr);
    }

    // Upload cbuffer: full 80-byte managed struct (matches the 80-byte
    // HLSL cbuffer byte-for-byte). ViewportSize lives at offset 64 in
    // the cbuffer and is native-authoritative — whatever the managed
    // struct carries in those bytes is ignored and overwritten with the
    // ink-layer bitmap's pixel dimensions. Pad fields (offset 72..79)
    // are cleared so no managed leakage reaches the shader.
    {
        void* mapped = nullptr;
        D3D12_RANGE nil = { 0, 0 };
        cbUpload_->Map(0, &nil, &mapped);
        std::memset(mapped, 0, 256);
        std::memcpy(mapped, managedConstants, managedConstantsSize);
        // Overwrite ViewportSize + Pad at offset 64 (in floats: index 16..19).
        float* vp = (float*)mapped + 16;
        vp[0] = (float)width_;
        vp[1] = (float)height_;
        vp[2] = 0.0f;
        vp[3] = 0.0f;
        cbUpload_->Unmap(0, nullptr);
    }

    // Pixel-aligned scissor from BBoxMin/BBoxMax in the cbuffer (offsets
    // 20..27 in the 80-byte managed struct). Evaluate BEFORE touching the
    // command list — if the bbox is empty there's nothing to draw, and we
    // must not leave a half-recorded cmdList with barriers that never
    // execute (that would desync CPU-tracked textureState_ from the
    // actual GPU state and cause D3D12 #538 on the next Clear/Dispatch).
    const float* ubo = (const float*)managedConstants;
    const int minX = std::max(0, (int)std::floor(ubo[8]));
    const int minY = std::max(0, (int)std::floor(ubo[9]));
    const int maxX = std::min((int)width_,  (int)std::ceil(ubo[10]));
    const int maxY = std::min((int)height_, (int)std::ceil(ubo[11]));
    D3D12_RECT scissor = { minX, minY, maxX, maxY };
    if (scissor.right <= scissor.left || scissor.bottom <= scissor.top)
    {
        // Empty bbox — nothing to draw, nothing recorded, state unchanged.
        return 0;
    }

    // Upload optional user params (b1). CBV size must be 256-byte aligned;
    // the upload buffer grows on demand so a big custom shader cbuffer
    // doesn't re-alloc every frame.
    bool hasUserParams = (extraParams != nullptr && extraParamsSize > 0);
    if (hasUserParams)
    {
        const uint32_t alignedSize = (extraParamsSize + 255u) & ~255u;
        if (userCbUploadCapacity_ < alignedSize || !userCbUpload_)
        {
            userCbUpload_.Reset();
            D3D12_HEAP_PROPERTIES upProps = {};
            upProps.Type = D3D12_HEAP_TYPE_UPLOAD;
            D3D12_RESOURCE_DESC ucbDesc = {};
            ucbDesc.Dimension        = D3D12_RESOURCE_DIMENSION_BUFFER;
            ucbDesc.Width            = alignedSize;
            ucbDesc.Height           = 1;
            ucbDesc.DepthOrArraySize = 1;
            ucbDesc.MipLevels        = 1;
            ucbDesc.Format           = DXGI_FORMAT_UNKNOWN;
            ucbDesc.SampleDesc.Count = 1;
            ucbDesc.Layout           = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
            HRESULT hr = device_->CreateCommittedResource(
                &upProps, D3D12_HEAP_FLAG_NONE,
                &ucbDesc, D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
                IID_PPV_ARGS(userCbUpload_.GetAddressOf()));
            if (FAILED(hr)) return -7;
            userCbUploadCapacity_ = alignedSize;
        }
        void* mapped = nullptr;
        D3D12_RANGE nil = { 0, 0 };
        userCbUpload_->Map(0, &nil, &mapped);
        std::memset(mapped, 0, alignedSize);
        std::memcpy(mapped, extraParams, extraParamsSize);
        userCbUpload_->Unmap(0, nullptr);
    }

    // Record dispatch. From this point on, ANY exit path must Execute
    // the command list (so barriers actually apply on the GPU) — early
    // returns between Reset() and ExecuteAndWait() would desync state.
    cmdAlloc_->Reset();
    cmdList_->Reset(cmdAlloc_.Get(), shader->Pso());

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

    D3D12_VIEWPORT vp = {};
    vp.TopLeftX = 0; vp.TopLeftY = 0;
    vp.Width    = (float)width_;
    vp.Height   = (float)height_;
    vp.MinDepth = 0.0f;
    vp.MaxDepth = 1.0f;
    cmdList_->RSSetViewports(1, &vp);
    cmdList_->RSSetScissorRects(1, &scissor);

    cmdList_->SetGraphicsRootSignature(pipeline->RootSignature());
    cmdList_->SetGraphicsRootConstantBufferView(0, cbUpload_->GetGPUVirtualAddress());
    // b1: user params when present. Binding cbUpload_ again as a harmless
    // placeholder when absent — the shader won't read it unless it declares
    // the cbuffer, so the bytes don't matter; the CBV just has to be valid.
    cmdList_->SetGraphicsRootConstantBufferView(
        1,
        (hasUserParams ? userCbUpload_ : cbUpload_)->GetGPUVirtualAddress());
    cmdList_->SetGraphicsRootShaderResourceView(2, sbUpload_->GetGPUVirtualAddress());

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

    ExecuteAndWait(cmdList_.Get());
    return 0;
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
        return -1;
    return reinterpret_cast<D3D12InkLayerBitmap*>(bitmap)->DispatchBrush(
        reinterpret_cast<D3D12BrushShader*>(shader),
        strokePoints, pointCount, constants, 80,
        extraParams, extraParamsSize);
}

} // namespace jalium

