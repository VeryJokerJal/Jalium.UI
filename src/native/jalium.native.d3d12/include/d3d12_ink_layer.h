#pragma once

#include "d3d12_backend.h"
#include "d3d12_brush_shader.h"
#include <d3d12.h>
#include <wrl/client.h>
#include <cstdint>

namespace jalium {

using Microsoft::WRL::ComPtr;

// Persistent GPU-side RGBA8 bitmap that InkCanvas uses as its committed-ink
// layer. Strokes are painted into this bitmap by brush pixel shaders, and
// the main render target blits it every frame. Owns its own RTV + SRV
// descriptor heaps so it's self-contained — no sharing with the window
// render target's ring buffers.
class D3D12InkLayerBitmap
{
public:
    D3D12InkLayerBitmap(ID3D12Device* device, ID3D12CommandQueue* queue);
    ~D3D12InkLayerBitmap();

    D3D12InkLayerBitmap(const D3D12InkLayerBitmap&)            = delete;
    D3D12InkLayerBitmap& operator=(const D3D12InkLayerBitmap&) = delete;

    // Allocate or re-allocate the backing texture. Contents reset to
    // transparent on any size change. Returns false on GPU OOM.
    bool Initialize(uint32_t width, uint32_t height);
    bool Resize(uint32_t width, uint32_t height);

    // Clear the bitmap to a premultiplied RGBA color.
    void Clear(float r, float g, float b, float a);

    // Dispatch a brush pixel shader over the bitmap's full extent. The
    // shader is expected to discard pixels outside its stroke bbox — we
    // apply the stroke-bbox scissor for a fast-reject path but still let
    // the shader do per-pixel culling via SDF.
    //
    // strokePoints must point to `pointCount × 16` bytes of BrushStrokePoint
    // data (x, y, pressure, pad). constants is a 96-byte BrushConstants
    // cbuffer (80 bytes from managed side + 16 bytes framework-filled
    // ViewportSize+pad, which this function populates from the bitmap's
    // current dimensions).
    //
    // extraParams is optional — when non-null and extraParamsSize > 0,
    // the bytes are uploaded into cbuffer b1 (rounded up to the 256-byte
    // CBV alignment). Pass nullptr / 0 to leave b1 unbound.
    //
    // Returns 0 on success, non-zero on upload / dispatch failure.
    int DispatchBrush(
        D3D12BrushShader*               shader,
        const void*                     strokePoints,
        uint32_t                        pointCount,
        const void*                     managedConstants,  // 80 bytes
        uint32_t                        managedConstantsSize,
        const void*                     extraParams,        // may be nullptr
        uint32_t                        extraParamsSize);

    uint32_t            Width()   const { return width_;  }
    uint32_t            Height()  const { return height_; }
    ID3D12Resource*     Texture() const { return texture_.Get(); }
    D3D12_GPU_DESCRIPTOR_HANDLE SrvGpuHandle() const { return srvGpu_; }
    DXGI_FORMAT         Format()  const { return DXGI_FORMAT_R8G8B8A8_UNORM; }

    // Shared pipeline reference (lazily populated on first dispatch).
    // Not owning — the backend's singleton manages its lifetime.
    static D3D12BrushShaderPipeline* SharedPipeline(ID3D12Device* device);

private:
    bool CreateResources(uint32_t width, uint32_t height);
    void ReleaseResources();
    bool EnsureDispatchResources();
    void ExecuteAndWait(ID3D12GraphicsCommandList* cmdList);

    ID3D12Device*                 device_ = nullptr;
    ID3D12CommandQueue*           queue_  = nullptr;

    // Backing texture
    ComPtr<ID3D12Resource>        texture_;
    D3D12_RESOURCE_STATES         textureState_ = D3D12_RESOURCE_STATE_COMMON;
    uint32_t                      width_  = 0;
    uint32_t                      height_ = 0;

    // RTV (single entry CPU heap)
    ComPtr<ID3D12DescriptorHeap>  rtvHeap_;
    D3D12_CPU_DESCRIPTOR_HANDLE   rtvCpu_ = {};

    // SRV (GPU-visible heap — used by BlitInkLayer on the main RT)
    ComPtr<ID3D12DescriptorHeap>  srvHeap_;
    D3D12_CPU_DESCRIPTOR_HANDLE   srvCpu_ = {};
    D3D12_GPU_DESCRIPTOR_HANDLE   srvGpu_ = {};

    // Per-dispatch scratch: command allocator, list, fence. Synchronous
    // for simplicity — brush dispatch latency is already dominated by
    // shader compile the first time a shader is used.
    ComPtr<ID3D12CommandAllocator> cmdAlloc_;
    ComPtr<ID3D12GraphicsCommandList> cmdList_;
    ComPtr<ID3D12Fence>            fence_;
    uint64_t                       fenceValue_ = 0;
    HANDLE                         fenceEvent_ = nullptr;

    // Upload buffers (reused across dispatches)
    ComPtr<ID3D12Resource>        cbUpload_;       // 96 bytes (b0)
    ComPtr<ID3D12Resource>        userCbUpload_;   // user params (b1); grows as needed
    uint32_t                      userCbUploadCapacity_ = 0;
    ComPtr<ID3D12Resource>        sbUpload_;       // points × 16; grows as needed
    uint32_t                      sbUploadCapacity_ = 0;
};

} // namespace jalium
