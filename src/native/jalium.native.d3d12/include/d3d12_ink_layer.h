#pragma once

#include "d3d12_backend.h"
#include "d3d12_brush_shader.h"
#include <d3d12.h>
#include <wrl/client.h>
#include <cstdint>
#include <vector>

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
    // Returns a JaliumInkDispatchResult (jalium_types.h). D3D12 never
    // reports STALE_CONTEXT — upload/pipeline failures here are momentary
    // (TRANSIENT: retry the same handles next frame) and true device
    // removal is recovered out-of-band by full RenderContext replacement.
    //
    // Clear()/DispatchBrush() now only RECORD into a deferred batch; the
    // batch is submitted by FlushPending() (called from the render target's
    // BlitInkLayer at the frame boundary), so a device-removal failure is
    // observed one call later than it would have been by the previous
    // synchronous implementation. Transient batch errors are reported once;
    // the same handles can be retried on the following call.
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
    // D3D12RenderTarget::BlitInkLayer (d3d12_render_target.cpp) must flush
    // the accumulated ink batch before it records this texture as a sampled
    // bitmap on the main render list. FlushPending stays private otherwise.
    friend class D3D12RenderTarget;
    friend struct D3D12InkLayerBitmapTestAccess;

    bool CreateResources(uint32_t width, uint32_t height);
    bool ReleaseResources();
    bool EnsureDispatchResources();
    bool BeginBatchIfNeeded();
    bool FlushPending(bool wait);
    bool SignalPendingBatch();
    int TakeBatchError();
    void DeferInFlightResources();
    bool WaitForFence(uint64_t value);
    bool ArenaAlloc(uint64_t bytes, void** cpuOut, D3D12_GPU_VIRTUAL_ADDRESS* gpuOut);

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

    // Command allocator, list and fence. A single list is reused every
    // batch; the allocator can only be Reset once the previous batch's
    // fence has completed (BeginBatchIfNeeded guards that).
    ComPtr<ID3D12CommandAllocator> cmdAlloc_;
    ComPtr<ID3D12GraphicsCommandList> cmdList_;
    ComPtr<ID3D12Fence>            fence_;
    uint64_t                       fenceValue_ = 0;
    HANDLE                         fenceEvent_ = nullptr;

    // ── Deferred multi-dispatch batching ────────────────────────────────
    // Clear()/DispatchBrush() accumulate into one command list and one
    // per-batch upload arena instead of doing one blocking ExecuteAndWait
    // per call; FlushPending() submits at the frame boundary.
    bool                 batchOpen_ = false;
    bool                 batchNeedsSignal_ = false;
    uint64_t             lastSubmittedFenceValue_ = 0;
    // Errors discovered by Clear()/FlushPending() are consumed by the next
    // DispatchBrush(). They must not permanently disable otherwise healthy
    // handles. A failed Close discards the list and restores the state from
    // before that unsubmitted recording.
    int                  batchError_ = 0;
    D3D12_RESOURCE_STATES batchInitialTextureState_ = D3D12_RESOURCE_STATE_COMMON;
    ID3D12PipelineState* currentPso_ = nullptr;
    // Command lists do not retain PSOs. Keep every bound PSO alive through
    // submission and fence completion, even if its public brush is disposed.
    std::vector<ComPtr<ID3D12PipelineState>> batchPsos_;

    // Per-batch upload arena: one large UPLOAD buffer with a bump allocator.
    // Each dispatch bumps a private 256-byte-aligned slot so concurrent
    // dispatches never overwrite each other's still-in-flight data. On
    // overflow the active buffer is retired (kept alive until the batch's
    // fence completes) and a larger one takes over.
    ComPtr<ID3D12Resource>              arena_;
    uint8_t*                            arenaCpu_ = nullptr;
    uint64_t                            arenaCapacity_ = 0;
    uint64_t                            arenaOffset_ = 0;
    std::vector<ComPtr<ID3D12Resource>> retiredArena_;
};

} // namespace jalium
