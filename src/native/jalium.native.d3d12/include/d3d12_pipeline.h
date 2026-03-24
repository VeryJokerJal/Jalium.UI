#pragma once

#include "d3d12_backend.h"
#include <d3dcompiler.h>
#include <unordered_map>
#include <vector>
#include <stack>
#include <mutex>

#pragma comment(lib, "d3dcompiler.lib")

namespace jalium {

// ============================================================================
// Constants
// ============================================================================

constexpr UINT kPipelineMaxSrvDescriptors = 2048;
constexpr UINT kPipelineMaxRtvDescriptors = 32;

// ============================================================================
// Enums matching managed side
// ============================================================================

// BufferUsage (managed: Jalium.UI.Gpu.Resources.BufferUsage)
enum PipelineBufferUsage : int {
    BUFFER_USAGE_VERTEX   = 0,
    BUFFER_USAGE_INDEX    = 1,
    BUFFER_USAGE_INSTANCE = 2,
    BUFFER_USAGE_UNIFORM  = 3,
    BUFFER_USAGE_STORAGE  = 4,
    BUFFER_USAGE_UPLOAD   = 5,
    BUFFER_USAGE_READBACK = 6,
    BUFFER_USAGE_INDIRECT = 7
};

// TextureFormat (managed: Jalium.UI.Gpu.TextureFormat)
enum PipelineTextureFormat : int {
    TEX_FMT_RGBA8 = 0,
    TEX_FMT_BGRA8 = 1,
    TEX_FMT_R8    = 2,
    TEX_FMT_BC1   = 3,
    TEX_FMT_BC3   = 4,
    TEX_FMT_BC7   = 5,
    TEX_FMT_ASTC  = 6
};

// TextureUsage flags (managed: Jalium.UI.Gpu.Resources.TextureUsage)
enum PipelineTextureUsage : int {
    TEX_USAGE_SHADER_RESOURCE = 1,
    TEX_USAGE_RENDER_TARGET   = 2,
    TEX_USAGE_UNORDERED_ACCESS = 4,
    TEX_USAGE_DEPTH_STENCIL   = 8
};

// RenderTargetFormat (managed: Jalium.UI.Gpu.Pipeline.RenderTargetFormat)
enum PipelineRtFormat : int {
    RT_FMT_R8G8B8A8_UNORM     = 0,
    RT_FMT_B8G8R8A8_UNORM     = 1,
    RT_FMT_R16G16B16A16_FLOAT = 2,
    RT_FMT_R32G32B32A32_FLOAT = 3,
    RT_FMT_R8_UNORM           = 4,
    RT_FMT_R32_FLOAT          = 5
};

// InputLayoutType (managed: Jalium.UI.Gpu.Pipeline.InputLayoutType)
enum PipelineInputLayout : int {
    INPUT_RECT_INSTANCED  = 0,
    INPUT_TEXT_INSTANCED  = 1,
    INPUT_IMAGE_INSTANCED = 2,
    INPUT_PATH_DIRECT     = 3,
    INPUT_NONE            = 4
};

// RootSignatureType (managed: Jalium.UI.Gpu.Pipeline.RootSignatureType)
enum PipelineRootSigType : int {
    ROOT_SIG_STANDARD  = 0,
    ROOT_SIG_PATH      = 1,
    ROOT_SIG_COMPUTE   = 2,
    ROOT_SIG_COMPOSITE = 3
};

// BlendMode (managed: Jalium.UI.Gpu.BlendMode)
enum PipelineBlendMode : int {
    BLEND_NORMAL     = 0,
    BLEND_MULTIPLY   = 1,
    BLEND_SCREEN     = 2,
    BLEND_OVERLAY    = 3,
    BLEND_DARKEN     = 4,
    BLEND_LIGHTEN    = 5,
    BLEND_COLORDODGE = 6,
    BLEND_COLORBURN  = 7,
    BLEND_SOFTLIGHT  = 8,
    BLEND_HARDLIGHT  = 9,
    BLEND_DIFFERENCE = 10,
    BLEND_EXCLUSION  = 11
};

// CullMode (managed: Jalium.UI.Gpu.Pipeline.CullMode)
enum PipelineCullMode : int {
    CULL_NONE  = 0,
    CULL_FRONT = 1,
    CULL_BACK  = 2
};

// ============================================================================
// Resource wrapper types
// ============================================================================

/// Type tag for discriminating PipelineBuffer vs PipelineTexture handles.
/// The managed side passes opaque nint handles returned from buffer/texture
/// creation, and descriptor/barrier functions must extract the underlying
/// ID3D12Resource*. The tag MUST be the first member of both structs.
enum PipelineResourceTag : uint32_t {
    RESOURCE_TAG_BUFFER  = 0x4A42'5546,  // "JBUF"
    RESOURCE_TAG_TEXTURE = 0x4A54'4558,  // "JTEX"
};

struct PipelineBuffer {
    PipelineResourceTag tag = RESOURCE_TAG_BUFFER;
    ComPtr<ID3D12Resource> resource;
    int size = 0;
    int usage = 0;
    void* mappedData = nullptr;  // persistently mapped for upload heaps
};

struct PipelineTexture {
    PipelineResourceTag tag = RESOURCE_TAG_TEXTURE;
    ComPtr<ID3D12Resource> resource;
    int width = 0;
    int height = 0;
    DXGI_FORMAT format = DXGI_FORMAT_UNKNOWN;
    int srvIndex = -1;
};

struct PipelinePSO {
    ComPtr<ID3D12PipelineState> pso;
    int inputLayoutType = INPUT_NONE;
    bool isCompute = false;
};

/// Extract the underlying ID3D12Resource* from a handle that may be
/// a PipelineBuffer*, PipelineTexture*, or raw ID3D12Resource*.
/// Uses a tagged-pointer scheme: PipelineBuffer/PipelineTexture have a known
/// 32-bit tag as their first member. Raw COM pointers (from jalium_get_device
/// interop) have vtable pointers that won't match these specific tags.
/// Tag values are chosen to avoid collision with typical vtable addresses.
inline ID3D12Resource* ExtractD3D12Resource(void* handle) {
    if (!handle) return nullptr;
    auto tag = *static_cast<const uint32_t*>(handle);
    if (tag == RESOURCE_TAG_BUFFER) {
        auto* buf = static_cast<PipelineBuffer*>(handle);
        return buf->resource.Get();
    }
    if (tag == RESOURCE_TAG_TEXTURE) {
        auto* tex = static_cast<PipelineTexture*>(handle);
        return tex->resource.Get();
    }
    // Assume raw ID3D12Resource* (e.g. from jalium_get_device interop).
    // The tag values (0x4A42'5546, 0x4A54'4558) are ASCII strings unlikely to
    // collide with vtable pointers (which are high addresses in 64-bit mode).
    return static_cast<ID3D12Resource*>(handle);
}

// ============================================================================
// Pipeline Context
// ============================================================================

class PipelineContext {
public:
    explicit PipelineContext(D3D12Backend* backend);
    ~PipelineContext();

    bool Initialize();
    void Shutdown();

    // Device references (not owned)
    ID3D12Device* GetDevice() const { return device_; }
    ID3D12CommandQueue* GetCommandQueue() const { return commandQueue_; }
    D3D12Backend* GetBackend() const { return backend_; }

    // Command list management
    ID3D12GraphicsCommandList* GetCommandList();
    void EnsureOpen();
    void Submit();

    // Descriptor allocation
    int AllocateSrvIndex();
    void FreeSrvIndex(int index);
    D3D12_CPU_DESCRIPTOR_HANDLE GetSrvCpuHandle(int index) const;
    D3D12_GPU_DESCRIPTOR_HANDLE GetSrvGpuHandle(int index) const;

    // Fence
    void SignalFence(uint64_t value);
    void WaitForFence(uint64_t value);

    // Upload helper: records a copy on a one-shot command list, executes & waits
    bool UploadToDefaultBuffer(
        ID3D12Resource* dest,
        const void* data,
        size_t dataSize,
        D3D12_RESOURCE_STATES afterState);

    // Binding state
    void SetCurrentPSO(PipelinePSO* pso);
    bool IsCurrentCompute() const { return currentIsCompute_; }
    int  CurrentInputLayout() const { return currentInputLayout_; }

    ID3D12DescriptorHeap* GetSrvHeap() const { return srvHeap_.Get(); }

private:
    D3D12Backend* backend_;
    ID3D12Device* device_;
    ID3D12CommandQueue* commandQueue_;

    // Main command recording
    ComPtr<ID3D12CommandAllocator>    commandAllocator_;
    ComPtr<ID3D12GraphicsCommandList> commandList_;
    bool commandListOpen_ = false;

    // Upload helper (separate allocator to not conflict with main list)
    ComPtr<ID3D12CommandAllocator>    uploadAllocator_;
    ComPtr<ID3D12GraphicsCommandList> uploadCommandList_;
    ComPtr<ID3D12Fence>              uploadFence_;
    HANDLE uploadFenceEvent_ = nullptr;
    uint64_t uploadFenceValue_ = 0;

    // Shader-visible SRV/CBV/UAV descriptor heap
    ComPtr<ID3D12DescriptorHeap> srvHeap_;
    UINT srvDescriptorSize_ = 0;
    int  nextSrvIndex_ = 0;
    std::stack<int> srvFreeList_;  // recycled SRV descriptor indices

    // RTV descriptor heap (CPU-only)
    ComPtr<ID3D12DescriptorHeap> rtvHeap_;
    UINT rtvDescriptorSize_ = 0;
    int  nextRtvIndex_ = 0;

    // Fence for submit synchronization
    ComPtr<ID3D12Fence> fence_;
    HANDLE fenceEvent_ = nullptr;
    uint64_t fenceValue_ = 0;

    // Current PSO state
    bool currentIsCompute_ = false;
    int  currentInputLayout_ = INPUT_NONE;

    bool initialized_ = false;
};

// ============================================================================
// Format conversion helpers
// ============================================================================

DXGI_FORMAT TextureFormatToDxgi(int format);
DXGI_FORMAT RtFormatToDxgi(int format);
D3D12_CULL_MODE CullModeToDx(int mode);

UINT GetVertexStride(int layoutType);
UINT GetInstanceStride(int layoutType);

// ============================================================================
// Global pipeline context registry
// ============================================================================

PipelineContext* GetPipeline(void* ctx);
D3D12Backend*    GetD3D12Backend(void* ctx);

} // namespace jalium
