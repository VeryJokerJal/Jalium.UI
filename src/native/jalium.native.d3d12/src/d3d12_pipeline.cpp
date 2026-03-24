/// D3D12 GPU Shader Pipeline — full implementation of all P/Invoke functions
/// declared in Jalium.UI.Gpu (D3D12ShaderBackend + ShaderCompiler).
///
/// Provides buffer/texture management, PSO creation, root signatures,
/// descriptor heaps, command recording, shader compilation, and GPU sync
/// on top of the existing D3D12Backend device and command queue.

#include "d3d12_pipeline.h"
#include "jalium_internal.h"
#include "jalium_api.h"

#include <d3d12.h>
#include <dxgi1_6.h>
#include <d3dcompiler.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>

#pragma comment(lib, "d3dcompiler.lib")

using Microsoft::WRL::ComPtr;

// ============================================================================
// Anonymous namespace — internal helpers
// ============================================================================

namespace {

// Global pipeline context registry
std::mutex g_pipelineMutex;
std::unordered_map<void*, jalium::PipelineContext*> g_pipelines;

// Input layout definitions (D3D12_INPUT_ELEMENT_DESC arrays)
// Must match managed UIInputLayouts exactly.

static const D3D12_INPUT_ELEMENT_DESC kRectInstancedLayout[] = {
    // Slot 0: per-vertex (16B stride)
    { "POSITION",           0, DXGI_FORMAT_R32G32_FLOAT,       0,  0, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,   0 },
    { "TEXCOORD",           0, DXGI_FORMAT_R32G32_FLOAT,       0,  8, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,   0 },
    // Slot 1: per-instance (80B stride)
    { "INST_POSITION",      0, DXGI_FORMAT_R32G32_FLOAT,       1,  0, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "INST_SIZE",          0, DXGI_FORMAT_R32G32_FLOAT,       1,  8, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "INST_UV",            0, DXGI_FORMAT_R32G32B32A32_FLOAT, 1, 16, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "INST_COLOR",         0, DXGI_FORMAT_R32_UINT,           1, 32, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "INST_CORNER_RADIUS", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 1, 36, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "INST_BORDER_THICK",  0, DXGI_FORMAT_R32G32B32A32_FLOAT, 1, 52, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "INST_BORDER_COLOR",  0, DXGI_FORMAT_R32_UINT,           1, 68, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "INST_PADDING",       0, DXGI_FORMAT_R32G32_FLOAT,       1, 72, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
};

static const D3D12_INPUT_ELEMENT_DESC kTextInstancedLayout[] = {
    // Slot 0: per-vertex (16B)
    { "POSITION",    0, DXGI_FORMAT_R32G32_FLOAT,       0,  0, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,   0 },
    { "TEXCOORD",    0, DXGI_FORMAT_R32G32_FLOAT,       0,  8, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,   0 },
    // Slot 1: per-instance (36B)
    { "GLYPH_POS",   0, DXGI_FORMAT_R32G32_FLOAT,       1,  0, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "GLYPH_SIZE",  0, DXGI_FORMAT_R32G32_FLOAT,       1,  8, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "GLYPH_UV",    0, DXGI_FORMAT_R32G32B32A32_FLOAT, 1, 16, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "GLYPH_COLOR", 0, DXGI_FORMAT_R32_UINT,           1, 32, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
};

static const D3D12_INPUT_ELEMENT_DESC kImageInstancedLayout[] = {
    // Slot 0: per-vertex (16B)
    { "POSITION",       0, DXGI_FORMAT_R32G32_FLOAT,       0,  0, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,   0 },
    { "TEXCOORD",       0, DXGI_FORMAT_R32G32_FLOAT,       0,  8, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,   0 },
    // Slot 1: per-instance (52B)
    { "INST_POSITION",  0, DXGI_FORMAT_R32G32_FLOAT,       1,  0, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "INST_SIZE",      0, DXGI_FORMAT_R32G32_FLOAT,       1,  8, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "INST_UV",        0, DXGI_FORMAT_R32G32B32A32_FLOAT, 1, 16, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "INST_COLOR",     0, DXGI_FORMAT_R32_UINT,           1, 32, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
    { "INST_NINESLICE", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 1, 36, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
};

static const D3D12_INPUT_ELEMENT_DESC kPathDirectLayout[] = {
    { "POSITION", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 0, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 8, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 },
};

void GetInputLayout(int type,
                    const D3D12_INPUT_ELEMENT_DESC** ppElements,
                    UINT* pCount)
{
    switch (type) {
        case jalium::INPUT_RECT_INSTANCED:
            *ppElements = kRectInstancedLayout;
            *pCount = _countof(kRectInstancedLayout);
            break;
        case jalium::INPUT_TEXT_INSTANCED:
            *ppElements = kTextInstancedLayout;
            *pCount = _countof(kTextInstancedLayout);
            break;
        case jalium::INPUT_IMAGE_INSTANCED:
            *ppElements = kImageInstancedLayout;
            *pCount = _countof(kImageInstancedLayout);
            break;
        case jalium::INPUT_PATH_DIRECT:
            *ppElements = kPathDirectLayout;
            *pCount = _countof(kPathDirectLayout);
            break;
        default: // INPUT_NONE
            *ppElements = nullptr;
            *pCount = 0;
            break;
    }
}

// Build a D3D12 blend description from the managed BlendMode enum value.
D3D12_BLEND_DESC BuildBlendDesc(int blendMode)
{
    D3D12_BLEND_DESC desc = {};
    desc.AlphaToCoverageEnable = FALSE;
    desc.IndependentBlendEnable = FALSE;

    auto& rt = desc.RenderTarget[0];
    rt.RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;

    if (blendMode == jalium::BLEND_NORMAL) {
        // Standard premultiplied-alpha blending
        rt.BlendEnable = TRUE;
        rt.SrcBlend  = D3D12_BLEND_ONE;
        rt.DestBlend = D3D12_BLEND_INV_SRC_ALPHA;
        rt.BlendOp   = D3D12_BLEND_OP_ADD;
        rt.SrcBlendAlpha  = D3D12_BLEND_ONE;
        rt.DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
        rt.BlendOpAlpha   = D3D12_BLEND_OP_ADD;
    } else if (blendMode == jalium::BLEND_MULTIPLY) {
        rt.BlendEnable = TRUE;
        rt.SrcBlend  = D3D12_BLEND_ZERO;
        rt.DestBlend = D3D12_BLEND_SRC_COLOR;
        rt.BlendOp   = D3D12_BLEND_OP_ADD;
        rt.SrcBlendAlpha  = D3D12_BLEND_ONE;
        rt.DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
        rt.BlendOpAlpha   = D3D12_BLEND_OP_ADD;
    } else if (blendMode == jalium::BLEND_SCREEN) {
        rt.BlendEnable = TRUE;
        rt.SrcBlend  = D3D12_BLEND_ONE;
        rt.DestBlend = D3D12_BLEND_INV_SRC_COLOR;
        rt.BlendOp   = D3D12_BLEND_OP_ADD;
        rt.SrcBlendAlpha  = D3D12_BLEND_ONE;
        rt.DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
        rt.BlendOpAlpha   = D3D12_BLEND_OP_ADD;
    } else {
        // All other advanced blend modes: use normal alpha blend as fallback.
        // True Overlay/Darken/etc. require pixel shader logic.
        rt.BlendEnable = TRUE;
        rt.SrcBlend  = D3D12_BLEND_ONE;
        rt.DestBlend = D3D12_BLEND_INV_SRC_ALPHA;
        rt.BlendOp   = D3D12_BLEND_OP_ADD;
        rt.SrcBlendAlpha  = D3D12_BLEND_ONE;
        rt.DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
        rt.BlendOpAlpha   = D3D12_BLEND_OP_ADD;
    }

    return desc;
}

// Serialize and create a root signature on the device.
HRESULT CreateSerializedRootSignature(
    ID3D12Device* device,
    int rootSigType,
    ID3D12RootSignature** ppRootSig)
{
    // Static sampler shared by Standard, Path, Composite
    D3D12_STATIC_SAMPLER_DESC staticSampler = {};
    staticSampler.Filter           = D3D12_FILTER_MIN_MAG_MIP_LINEAR;
    staticSampler.AddressU         = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    staticSampler.AddressV         = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    staticSampler.AddressW         = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    staticSampler.MaxAnisotropy    = 1;
    staticSampler.ComparisonFunc   = D3D12_COMPARISON_FUNC_NEVER;
    staticSampler.MaxLOD           = D3D12_FLOAT32_MAX;
    staticSampler.ShaderRegister   = 0; // s0
    staticSampler.ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

    D3D12_ROOT_PARAMETER params[4] = {};
    UINT numParams = 0;
    UINT numStaticSamplers = 0;
    const D3D12_STATIC_SAMPLER_DESC* pStaticSamplers = nullptr;

    // Descriptor ranges (allocated on stack, need stable addresses)
    D3D12_DESCRIPTOR_RANGE srvRange = {};
    srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
    srvRange.BaseShaderRegister = 0;
    srvRange.RegisterSpace = 0;
    srvRange.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

    D3D12_DESCRIPTOR_RANGE uavRange = {};
    uavRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV;
    uavRange.NumDescriptors = 1;
    uavRange.BaseShaderRegister = 0;
    uavRange.RegisterSpace = 0;
    uavRange.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

    D3D12_ROOT_SIGNATURE_FLAGS flags =
        D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;

    switch (rootSigType) {
        case jalium::ROOT_SIG_STANDARD:
            // Param 0: Root CBV at b0 (frame constants)
            params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
            params[0].Descriptor.ShaderRegister = 0;
            params[0].Descriptor.RegisterSpace  = 0;
            params[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            // Param 1: Descriptor table — 3 SRVs (t0, t1, t2)
            srvRange.NumDescriptors = 3;
            params[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            params[1].DescriptorTable.NumDescriptorRanges = 1;
            params[1].DescriptorTable.pDescriptorRanges   = &srvRange;
            params[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            numParams = 2;
            numStaticSamplers = 1;
            pStaticSamplers = &staticSampler;
            break;

        case jalium::ROOT_SIG_PATH:
            // Param 0: Root CBV b0 (frame constants)
            params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
            params[0].Descriptor.ShaderRegister = 0;
            params[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            // Param 1: Root CBV b1 (path constants)
            params[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
            params[1].Descriptor.ShaderRegister = 1;
            params[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            // Param 2: Descriptor table — 1 SRV (t0)
            srvRange.NumDescriptors = 1;
            params[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            params[2].DescriptorTable.NumDescriptorRanges = 1;
            params[2].DescriptorTable.pDescriptorRanges   = &srvRange;
            params[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            numParams = 3;
            numStaticSamplers = 1;
            pStaticSamplers = &staticSampler;
            break;

        case jalium::ROOT_SIG_COMPUTE:
            flags = D3D12_ROOT_SIGNATURE_FLAG_NONE; // no IA

            // Param 0: Root CBV b0
            params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
            params[0].Descriptor.ShaderRegister = 0;
            params[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            // Param 1: SRV table (t0)
            srvRange.NumDescriptors = 1;
            params[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            params[1].DescriptorTable.NumDescriptorRanges = 1;
            params[1].DescriptorTable.pDescriptorRanges   = &srvRange;
            params[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            // Param 2: UAV table (u0)
            params[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            params[2].DescriptorTable.NumDescriptorRanges = 1;
            params[2].DescriptorTable.pDescriptorRanges   = &uavRange;
            params[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            numParams = 3;
            numStaticSamplers = 0;
            pStaticSamplers = nullptr;
            break;

        case jalium::ROOT_SIG_COMPOSITE:
            // Param 0: Root CBV b0
            params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
            params[0].Descriptor.ShaderRegister = 0;
            params[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            // Param 1: SRV table (t0)
            srvRange.NumDescriptors = 1;
            params[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            params[1].DescriptorTable.NumDescriptorRanges = 1;
            params[1].DescriptorTable.pDescriptorRanges   = &srvRange;
            params[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

            numParams = 2;
            numStaticSamplers = 1;
            pStaticSamplers = &staticSampler;
            break;

        default:
            return E_INVALIDARG;
    }

    D3D12_ROOT_SIGNATURE_DESC rsDesc = {};
    rsDesc.NumParameters     = numParams;
    rsDesc.pParameters       = params;
    rsDesc.NumStaticSamplers = numStaticSamplers;
    rsDesc.pStaticSamplers   = pStaticSamplers;
    rsDesc.Flags             = flags;

    ComPtr<ID3DBlob> sigBlob, errorBlob;
    HRESULT hr = D3D12SerializeRootSignature(
        &rsDesc, D3D_ROOT_SIGNATURE_VERSION_1,
        &sigBlob, &errorBlob);
    if (FAILED(hr)) return hr;

    hr = device->CreateRootSignature(
        0,
        sigBlob->GetBufferPointer(),
        sigBlob->GetBufferSize(),
        IID_PPV_ARGS(ppRootSig));
    return hr;
}

} // anonymous namespace

// ============================================================================
// jalium namespace — PipelineContext implementation
// ============================================================================

namespace jalium {

// --- Format helpers ---

DXGI_FORMAT TextureFormatToDxgi(int format) {
    switch (format) {
        case TEX_FMT_RGBA8: return DXGI_FORMAT_R8G8B8A8_UNORM;
        case TEX_FMT_BGRA8: return DXGI_FORMAT_B8G8R8A8_UNORM;
        case TEX_FMT_R8:    return DXGI_FORMAT_R8_UNORM;
        case TEX_FMT_BC1:   return DXGI_FORMAT_BC1_UNORM;
        case TEX_FMT_BC3:   return DXGI_FORMAT_BC3_UNORM;
        case TEX_FMT_BC7:   return DXGI_FORMAT_BC7_UNORM;
        default:             return DXGI_FORMAT_R8G8B8A8_UNORM;
    }
}

DXGI_FORMAT RtFormatToDxgi(int format) {
    switch (format) {
        case RT_FMT_R8G8B8A8_UNORM:     return DXGI_FORMAT_R8G8B8A8_UNORM;
        case RT_FMT_B8G8R8A8_UNORM:     return DXGI_FORMAT_B8G8R8A8_UNORM;
        case RT_FMT_R16G16B16A16_FLOAT: return DXGI_FORMAT_R16G16B16A16_FLOAT;
        case RT_FMT_R32G32B32A32_FLOAT: return DXGI_FORMAT_R32G32B32A32_FLOAT;
        case RT_FMT_R8_UNORM:           return DXGI_FORMAT_R8_UNORM;
        case RT_FMT_R32_FLOAT:          return DXGI_FORMAT_R32_FLOAT;
        default:                         return DXGI_FORMAT_R8G8B8A8_UNORM;
    }
}

D3D12_CULL_MODE CullModeToDx(int mode) {
    switch (mode) {
        case CULL_FRONT: return D3D12_CULL_MODE_FRONT;
        case CULL_BACK:  return D3D12_CULL_MODE_BACK;
        default:         return D3D12_CULL_MODE_NONE;
    }
}

UINT GetVertexStride(int /*layoutType*/) { return 16; } // always float2 pos + float2 uv

UINT GetInstanceStride(int layoutType) {
    switch (layoutType) {
        case INPUT_RECT_INSTANCED:  return 80;
        case INPUT_TEXT_INSTANCED:  return 36;
        case INPUT_IMAGE_INSTANCED: return 52;
        default: return 0;
    }
}

// --- Global registry ---

// Thread-local cache to avoid lock contention on repeated lookups for the same context.
// Uses a generation counter to detect cross-thread invalidation: when any pipeline
// is destroyed, the global generation increments, causing all threads' caches to miss.
static std::atomic<uint64_t> g_pipelineGeneration{0};
static thread_local void* g_cachedPipelineKey = nullptr;
static thread_local PipelineContext* g_cachedPipelineValue = nullptr;
static thread_local uint64_t g_cachedGeneration = 0;

PipelineContext* GetPipeline(void* ctx) {
    uint64_t currentGen = g_pipelineGeneration.load(std::memory_order_acquire);
    if (ctx == g_cachedPipelineKey && g_cachedPipelineValue && g_cachedGeneration == currentGen) {
        return g_cachedPipelineValue;
    }
    std::lock_guard lock(g_pipelineMutex);
    auto it = g_pipelines.find(ctx);
    auto* result = (it != g_pipelines.end()) ? it->second : nullptr;
    g_cachedPipelineKey = ctx;
    g_cachedPipelineValue = result;
    g_cachedGeneration = g_pipelineGeneration.load(std::memory_order_acquire);
    return result;
}

D3D12Backend* GetD3D12Backend(void* ctx) {
    if (!ctx) return nullptr;
    auto* context = reinterpret_cast<Context*>(ctx);
    return static_cast<D3D12Backend*>(context->GetBackendImpl());
}

// --- PipelineContext ---

PipelineContext::PipelineContext(D3D12Backend* backend)
    : backend_(backend)
    , device_(backend->GetDevice())
    , commandQueue_(backend->GetCommandQueue())
{
}

PipelineContext::~PipelineContext() {
    Shutdown();
}

bool PipelineContext::Initialize() {
    if (initialized_) return true;

    HRESULT hr;

    // Main command allocator + command list
    hr = device_->CreateCommandAllocator(
        D3D12_COMMAND_LIST_TYPE_DIRECT,
        IID_PPV_ARGS(&commandAllocator_));
    if (FAILED(hr)) return false;

    hr = device_->CreateCommandList(
        0, D3D12_COMMAND_LIST_TYPE_DIRECT,
        commandAllocator_.Get(), nullptr,
        IID_PPV_ARGS(&commandList_));
    if (FAILED(hr)) return false;
    commandList_->Close();
    commandListOpen_ = false;

    // Upload helper allocator + list
    hr = device_->CreateCommandAllocator(
        D3D12_COMMAND_LIST_TYPE_DIRECT,
        IID_PPV_ARGS(&uploadAllocator_));
    if (FAILED(hr)) return false;

    hr = device_->CreateCommandList(
        0, D3D12_COMMAND_LIST_TYPE_DIRECT,
        uploadAllocator_.Get(), nullptr,
        IID_PPV_ARGS(&uploadCommandList_));
    if (FAILED(hr)) return false;
    uploadCommandList_->Close();

    hr = device_->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&uploadFence_));
    if (FAILED(hr)) return false;
    uploadFenceEvent_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);

    // SRV/CBV/UAV descriptor heap (shader visible)
    D3D12_DESCRIPTOR_HEAP_DESC srvDesc = {};
    srvDesc.Type  = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
    srvDesc.NumDescriptors = kPipelineMaxSrvDescriptors;
    srvDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
    hr = device_->CreateDescriptorHeap(&srvDesc, IID_PPV_ARGS(&srvHeap_));
    if (FAILED(hr)) return false;
    srvDescriptorSize_ = device_->GetDescriptorHandleIncrementSize(
        D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

    // RTV descriptor heap (CPU only)
    D3D12_DESCRIPTOR_HEAP_DESC rtvDesc = {};
    rtvDesc.Type  = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
    rtvDesc.NumDescriptors = kPipelineMaxRtvDescriptors;
    rtvDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
    hr = device_->CreateDescriptorHeap(&rtvDesc, IID_PPV_ARGS(&rtvHeap_));
    if (FAILED(hr)) return false;
    rtvDescriptorSize_ = device_->GetDescriptorHandleIncrementSize(
        D3D12_DESCRIPTOR_HEAP_TYPE_RTV);

    // Fence for main submit
    hr = device_->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence_));
    if (FAILED(hr)) return false;
    fenceEvent_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);

    initialized_ = true;
    return true;
}

void PipelineContext::Shutdown() {
    if (!initialized_) return;

    // Drain GPU
    if (fence_ && commandQueue_) {
        fenceValue_++;
        commandQueue_->Signal(fence_.Get(), fenceValue_);
        if (fence_->GetCompletedValue() < fenceValue_) {
            fence_->SetEventOnCompletion(fenceValue_, fenceEvent_);
            WaitForSingleObject(fenceEvent_, INFINITE);
        }
    }

    if (fenceEvent_)       { CloseHandle(fenceEvent_);       fenceEvent_ = nullptr; }
    if (uploadFenceEvent_) { CloseHandle(uploadFenceEvent_); uploadFenceEvent_ = nullptr; }

    commandList_.Reset();
    commandAllocator_.Reset();
    uploadCommandList_.Reset();
    uploadAllocator_.Reset();
    uploadFence_.Reset();
    srvHeap_.Reset();
    rtvHeap_.Reset();
    fence_.Reset();

    initialized_ = false;
}

ID3D12GraphicsCommandList* PipelineContext::GetCommandList() {
    EnsureOpen();
    return commandList_.Get();
}

void PipelineContext::EnsureOpen() {
    if (commandListOpen_) return;
    commandAllocator_->Reset();
    commandList_->Reset(commandAllocator_.Get(), nullptr);

    // Bind the shader-visible descriptor heap
    ID3D12DescriptorHeap* heaps[] = { srvHeap_.Get() };
    commandList_->SetDescriptorHeaps(_countof(heaps), heaps);

    commandListOpen_ = true;
}

void PipelineContext::Submit() {
    if (!commandListOpen_) return;

    commandList_->Close();
    commandListOpen_ = false;

    ID3D12CommandList* lists[] = { commandList_.Get() };
    commandQueue_->ExecuteCommandLists(1, lists);

    // Synchronous wait — simple and correct for UI pipeline
    fenceValue_++;
    commandQueue_->Signal(fence_.Get(), fenceValue_);
    if (fence_->GetCompletedValue() < fenceValue_) {
        fence_->SetEventOnCompletion(fenceValue_, fenceEvent_);
        WaitForSingleObject(fenceEvent_, INFINITE);
    }
}

int PipelineContext::AllocateSrvIndex() {
    // Prefer recycled indices to avoid exhausting the descriptor heap
    if (!srvFreeList_.empty()) {
        int index = srvFreeList_.top();
        srvFreeList_.pop();
        return index;
    }
    if (nextSrvIndex_ >= static_cast<int>(kPipelineMaxSrvDescriptors))
        return -1;
    return nextSrvIndex_++;
}

void PipelineContext::FreeSrvIndex(int index) {
    if (index >= 0 && index < nextSrvIndex_) {
        srvFreeList_.push(index);
    }
}

D3D12_CPU_DESCRIPTOR_HANDLE PipelineContext::GetSrvCpuHandle(int index) const {
    auto base = srvHeap_->GetCPUDescriptorHandleForHeapStart();
    base.ptr += static_cast<SIZE_T>(index) * srvDescriptorSize_;
    return base;
}

D3D12_GPU_DESCRIPTOR_HANDLE PipelineContext::GetSrvGpuHandle(int index) const {
    auto base = srvHeap_->GetGPUDescriptorHandleForHeapStart();
    base.ptr += static_cast<UINT64>(index) * srvDescriptorSize_;
    return base;
}

void PipelineContext::SignalFence(uint64_t value) {
    commandQueue_->Signal(fence_.Get(), value);
}

void PipelineContext::WaitForFence(uint64_t value) {
    if (fence_->GetCompletedValue() < value) {
        fence_->SetEventOnCompletion(value, fenceEvent_);
        WaitForSingleObject(fenceEvent_, INFINITE);
    }
}

bool PipelineContext::UploadToDefaultBuffer(
    ID3D12Resource* dest,
    const void* data,
    size_t dataSize,
    D3D12_RESOURCE_STATES afterState)
{
    if (!data || dataSize == 0) return false;

    // Create staging buffer
    D3D12_HEAP_PROPERTIES uploadHeap = {};
    uploadHeap.Type = D3D12_HEAP_TYPE_UPLOAD;

    D3D12_RESOURCE_DESC bufDesc = {};
    bufDesc.Dimension        = D3D12_RESOURCE_DIMENSION_BUFFER;
    bufDesc.Width            = dataSize;
    bufDesc.Height           = 1;
    bufDesc.DepthOrArraySize = 1;
    bufDesc.MipLevels        = 1;
    bufDesc.SampleDesc.Count = 1;
    bufDesc.Layout           = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;

    ComPtr<ID3D12Resource> staging;
    HRESULT hr = device_->CreateCommittedResource(
        &uploadHeap, D3D12_HEAP_FLAG_NONE,
        &bufDesc, D3D12_RESOURCE_STATE_GENERIC_READ,
        nullptr, IID_PPV_ARGS(&staging));
    if (FAILED(hr)) return false;

    // Map and copy
    void* mapped = nullptr;
    D3D12_RANGE readRange = {};
    hr = staging->Map(0, &readRange, &mapped);
    if (FAILED(hr)) return false;
    memcpy(mapped, data, dataSize);
    staging->Unmap(0, nullptr);

    // Record copy on upload command list
    uploadAllocator_->Reset();
    uploadCommandList_->Reset(uploadAllocator_.Get(), nullptr);

    // Transition dest to COPY_DEST
    D3D12_RESOURCE_BARRIER barrier = {};
    barrier.Type  = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource   = dest;
    barrier.Transition.StateBefore = afterState;
    barrier.Transition.StateAfter  = D3D12_RESOURCE_STATE_COPY_DEST;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    if (afterState != D3D12_RESOURCE_STATE_COPY_DEST) {
        uploadCommandList_->ResourceBarrier(1, &barrier);
    }

    uploadCommandList_->CopyBufferRegion(dest, 0, staging.Get(), 0, dataSize);

    // Transition to afterState
    if (afterState != D3D12_RESOURCE_STATE_COPY_DEST) {
        std::swap(barrier.Transition.StateBefore, barrier.Transition.StateAfter);
        uploadCommandList_->ResourceBarrier(1, &barrier);
    }

    uploadCommandList_->Close();

    ID3D12CommandList* lists[] = { uploadCommandList_.Get() };
    commandQueue_->ExecuteCommandLists(1, lists);

    // Wait for upload completion
    uploadFenceValue_++;
    commandQueue_->Signal(uploadFence_.Get(), uploadFenceValue_);
    if (uploadFence_->GetCompletedValue() < uploadFenceValue_) {
        uploadFence_->SetEventOnCompletion(uploadFenceValue_, uploadFenceEvent_);
        WaitForSingleObject(uploadFenceEvent_, INFINITE);
    }

    return true;
}

void PipelineContext::SetCurrentPSO(PipelinePSO* pso) {
    if (pso) {
        currentIsCompute_   = pso->isCompute;
        currentInputLayout_ = pso->inputLayoutType;
    }
}

} // namespace jalium

// ============================================================================
// extern "C" — P/Invoke entry points
// ============================================================================

extern "C" {

// ===== Initialization =====

JALIUM_API int jalium_pipeline_init(void* context) {
    if (!context) return E_INVALIDARG;

    auto* backend = jalium::GetD3D12Backend(context);
    if (!backend) return E_FAIL;

    auto* pipeline = new jalium::PipelineContext(backend);
    if (!pipeline->Initialize()) {
        delete pipeline;
        return E_FAIL;
    }

    {
        std::lock_guard lock(g_pipelineMutex);
        g_pipelines[context] = pipeline;
    }

    return S_OK;
}

JALIUM_API void jalium_pipeline_shutdown(void* context) {
    jalium::PipelineContext* pipeline = nullptr;
    {
        std::lock_guard lock(g_pipelineMutex);
        auto it = g_pipelines.find(context);
        if (it != g_pipelines.end()) {
            pipeline = it->second;
            g_pipelines.erase(it);
        }
    }
    // Bump global generation to invalidate ALL threads' caches.
    // Thread-local caches check this generation before using cached values.
    jalium::g_pipelineGeneration.fetch_add(1, std::memory_order_release);
    delete pipeline;
}

// ===== Buffers =====

JALIUM_API void* jalium_buffer_create(void* context, const void* data, int size, int usage) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || size <= 0) return nullptr;

    // All buffers use upload heap for simplicity (CPU-writable, GPU-readable).
    // This is efficient for a UI framework with per-frame dynamic data.
    D3D12_HEAP_PROPERTIES heapProps = {};
    heapProps.Type = (usage == jalium::BUFFER_USAGE_READBACK)
        ? D3D12_HEAP_TYPE_READBACK
        : D3D12_HEAP_TYPE_UPLOAD;

    D3D12_RESOURCE_DESC bufDesc = {};
    bufDesc.Dimension        = D3D12_RESOURCE_DIMENSION_BUFFER;
    bufDesc.Width            = static_cast<UINT64>(size);
    bufDesc.Height           = 1;
    bufDesc.DepthOrArraySize = 1;
    bufDesc.MipLevels        = 1;
    bufDesc.SampleDesc.Count = 1;
    bufDesc.Layout           = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;

    D3D12_RESOURCE_STATES initialState =
        (usage == jalium::BUFFER_USAGE_READBACK)
            ? D3D12_RESOURCE_STATE_COPY_DEST
            : D3D12_RESOURCE_STATE_GENERIC_READ;

    auto* buf = new jalium::PipelineBuffer();
    buf->size  = size;
    buf->usage = usage;

    HRESULT hr = pipeline->GetDevice()->CreateCommittedResource(
        &heapProps, D3D12_HEAP_FLAG_NONE,
        &bufDesc, initialState,
        nullptr, IID_PPV_ARGS(&buf->resource));
    if (FAILED(hr)) { delete buf; return nullptr; }

    // Persistently map upload/readback buffers
    D3D12_RANGE readRange = {};
    hr = buf->resource->Map(0, &readRange, &buf->mappedData);
    if (FAILED(hr)) { delete buf; return nullptr; }

    // Copy initial data
    if (data && buf->mappedData) {
        memcpy(buf->mappedData, data, static_cast<size_t>(size));
    }

    return buf;
}

JALIUM_API void* jalium_buffer_create_empty(void* context, int size, int usage) {
    return jalium_buffer_create(context, nullptr, size, usage);
}

JALIUM_API void jalium_buffer_update(void* context, void* buffer, int offset, const void* data, int size) {
    (void)context;
    if (!buffer || !data || size <= 0) return;

    auto* buf = static_cast<jalium::PipelineBuffer*>(buffer);
    if (!buf->mappedData) return;
    if (offset + size > buf->size) return;

    memcpy(static_cast<uint8_t*>(buf->mappedData) + offset, data, static_cast<size_t>(size));
}

JALIUM_API void jalium_buffer_destroy(void* context, void* buffer) {
    (void)context;
    if (!buffer) return;

    auto* buf = static_cast<jalium::PipelineBuffer*>(buffer);
    if (buf->mappedData) {
        buf->resource->Unmap(0, nullptr);
        buf->mappedData = nullptr;
    }
    delete buf;
}

JALIUM_API void* jalium_buffer_get_mapped_ptr(void* context, void* buffer) {
    (void)context;
    if (!buffer) return nullptr;
    return static_cast<jalium::PipelineBuffer*>(buffer)->mappedData;
}

// ===== Textures =====

JALIUM_API void* jalium_texture_load(void* context, const char* path, int format) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !path) return nullptr;

    auto* backend = pipeline->GetBackend();
    IWICImagingFactory* wicFactory = backend->GetWICFactory();
    if (!wicFactory) return nullptr;

    // Convert UTF-8 path to wide string
    int wideLen = MultiByteToWideChar(CP_UTF8, 0, path, -1, nullptr, 0);
    if (wideLen <= 0) return nullptr;
    std::wstring widePath(static_cast<size_t>(wideLen), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, path, -1, widePath.data(), wideLen);

    // Decode image via WIC
    ComPtr<IWICBitmapDecoder> decoder;
    HRESULT hr = wicFactory->CreateDecoderFromFilename(
        widePath.c_str(), nullptr, GENERIC_READ,
        WICDecodeMetadataCacheOnDemand, &decoder);
    if (FAILED(hr)) return nullptr;

    ComPtr<IWICBitmapFrameDecode> frame;
    hr = decoder->GetFrame(0, &frame);
    if (FAILED(hr)) return nullptr;

    ComPtr<IWICFormatConverter> converter;
    hr = wicFactory->CreateFormatConverter(&converter);
    if (FAILED(hr)) return nullptr;

    hr = converter->Initialize(
        frame.Get(), GUID_WICPixelFormat32bppRGBA,
        WICBitmapDitherTypeNone, nullptr, 0.0f,
        WICBitmapPaletteTypeMedianCut);
    if (FAILED(hr)) return nullptr;

    UINT width, height;
    converter->GetSize(&width, &height);
    if (width == 0 || height == 0) return nullptr;

    std::vector<uint8_t> pixels(width * height * 4);
    hr = converter->CopyPixels(nullptr, width * 4,
        static_cast<UINT>(pixels.size()), pixels.data());
    if (FAILED(hr)) return nullptr;

    // Create D3D12 texture
    DXGI_FORMAT dxgiFormat = jalium::TextureFormatToDxgi(format);

    D3D12_HEAP_PROPERTIES heapProps = {};
    heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;

    D3D12_RESOURCE_DESC texDesc = {};
    texDesc.Dimension        = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    texDesc.Width            = width;
    texDesc.Height           = height;
    texDesc.DepthOrArraySize = 1;
    texDesc.MipLevels        = 1;
    texDesc.Format           = dxgiFormat;
    texDesc.SampleDesc.Count = 1;

    auto* tex = new jalium::PipelineTexture();
    tex->width  = static_cast<int>(width);
    tex->height = static_cast<int>(height);
    tex->format = dxgiFormat;

    hr = pipeline->GetDevice()->CreateCommittedResource(
        &heapProps, D3D12_HEAP_FLAG_NONE,
        &texDesc, D3D12_RESOURCE_STATE_COMMON,
        nullptr, IID_PPV_ARGS(&tex->resource));
    if (FAILED(hr)) { delete tex; return nullptr; }

    // Upload pixel data via staging buffer
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint = {};
    UINT numRows = 0;
    UINT64 rowSizeBytes = 0, totalBytes = 0;
    pipeline->GetDevice()->GetCopyableFootprints(
        &texDesc, 0, 1, 0, &footprint, &numRows, &rowSizeBytes, &totalBytes);

    // Create staging buffer
    D3D12_HEAP_PROPERTIES uploadHeap = {};
    uploadHeap.Type = D3D12_HEAP_TYPE_UPLOAD;

    D3D12_RESOURCE_DESC stagingDesc = {};
    stagingDesc.Dimension        = D3D12_RESOURCE_DIMENSION_BUFFER;
    stagingDesc.Width            = totalBytes;
    stagingDesc.Height           = 1;
    stagingDesc.DepthOrArraySize = 1;
    stagingDesc.MipLevels        = 1;
    stagingDesc.SampleDesc.Count = 1;
    stagingDesc.Layout           = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;

    ComPtr<ID3D12Resource> staging;
    hr = pipeline->GetDevice()->CreateCommittedResource(
        &uploadHeap, D3D12_HEAP_FLAG_NONE,
        &stagingDesc, D3D12_RESOURCE_STATE_GENERIC_READ,
        nullptr, IID_PPV_ARGS(&staging));
    if (FAILED(hr)) { delete tex; return nullptr; }

    // Copy pixel data to staging (respecting row pitch)
    uint8_t* mapped = nullptr;
    D3D12_RANGE readRange = {};
    hr = staging->Map(0, &readRange, reinterpret_cast<void**>(&mapped));
    if (FAILED(hr)) { delete tex; return nullptr; }

    const UINT srcRowPitch = width * 4;
    for (UINT row = 0; row < numRows; ++row) {
        memcpy(
            mapped + footprint.Offset + row * footprint.Footprint.RowPitch,
            pixels.data() + row * srcRowPitch,
            srcRowPitch);
    }
    staging->Unmap(0, nullptr);

    // Record texture copy on a one-shot command list
    pipeline->EnsureOpen();
    auto* cmdList = pipeline->GetCommandList();

    D3D12_RESOURCE_BARRIER barrier = {};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource   = tex->resource.Get();
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON;
    barrier.Transition.StateAfter  = D3D12_RESOURCE_STATE_COPY_DEST;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    cmdList->ResourceBarrier(1, &barrier);

    D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
    srcLoc.pResource       = staging.Get();
    srcLoc.Type            = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
    srcLoc.PlacedFootprint = footprint;

    D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
    dstLoc.pResource        = tex->resource.Get();
    dstLoc.Type             = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    dstLoc.SubresourceIndex = 0;

    cmdList->CopyTextureRegion(&dstLoc, 0, 0, 0, &srcLoc, nullptr);

    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
    barrier.Transition.StateAfter  = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    cmdList->ResourceBarrier(1, &barrier);

    pipeline->Submit();

    // Create SRV
    tex->srvIndex = pipeline->AllocateSrvIndex();
    if (tex->srvIndex >= 0) {
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Format                  = dxgiFormat;
        srvDesc.ViewDimension           = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Texture2D.MipLevels     = 1;

        pipeline->GetDevice()->CreateShaderResourceView(
            tex->resource.Get(), &srvDesc,
            pipeline->GetSrvCpuHandle(tex->srvIndex));
    }

    return tex;
}

JALIUM_API void* jalium_glyph_atlas_create(void* context, const char* fontId, float fontSize, int width, int height) {
    (void)fontId;
    (void)fontSize;

    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || width <= 0 || height <= 0) return nullptr;

    // Create an R8_UNORM texture for glyph atlas storage
    D3D12_HEAP_PROPERTIES heapProps = {};
    heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;

    D3D12_RESOURCE_DESC texDesc = {};
    texDesc.Dimension        = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    texDesc.Width            = static_cast<UINT64>(width);
    texDesc.Height           = static_cast<UINT>(height);
    texDesc.DepthOrArraySize = 1;
    texDesc.MipLevels        = 1;
    texDesc.Format           = DXGI_FORMAT_R8_UNORM;
    texDesc.SampleDesc.Count = 1;

    auto* tex = new jalium::PipelineTexture();
    tex->width  = width;
    tex->height = height;
    tex->format = DXGI_FORMAT_R8_UNORM;

    HRESULT hr = pipeline->GetDevice()->CreateCommittedResource(
        &heapProps, D3D12_HEAP_FLAG_NONE,
        &texDesc, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,
        nullptr, IID_PPV_ARGS(&tex->resource));
    if (FAILED(hr)) { delete tex; return nullptr; }

    // Create SRV
    tex->srvIndex = pipeline->AllocateSrvIndex();
    if (tex->srvIndex >= 0) {
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Format                  = DXGI_FORMAT_R8_UNORM;
        srvDesc.ViewDimension           = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Texture2D.MipLevels     = 1;

        pipeline->GetDevice()->CreateShaderResourceView(
            tex->resource.Get(), &srvDesc,
            pipeline->GetSrvCpuHandle(tex->srvIndex));
    }

    return tex;
}

JALIUM_API void* jalium_texture_create_rt(void* context, int width, int height, int format) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || width <= 0 || height <= 0) return nullptr;

    DXGI_FORMAT dxgiFormat = jalium::RtFormatToDxgi(format);

    D3D12_HEAP_PROPERTIES heapProps = {};
    heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;

    D3D12_RESOURCE_DESC texDesc = {};
    texDesc.Dimension        = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    texDesc.Width            = static_cast<UINT64>(width);
    texDesc.Height           = static_cast<UINT>(height);
    texDesc.DepthOrArraySize = 1;
    texDesc.MipLevels        = 1;
    texDesc.Format           = dxgiFormat;
    texDesc.SampleDesc.Count = 1;
    texDesc.Flags            = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;

    D3D12_CLEAR_VALUE clearValue = {};
    clearValue.Format = dxgiFormat;

    auto* tex = new jalium::PipelineTexture();
    tex->width  = width;
    tex->height = height;
    tex->format = dxgiFormat;

    HRESULT hr = pipeline->GetDevice()->CreateCommittedResource(
        &heapProps, D3D12_HEAP_FLAG_NONE,
        &texDesc, D3D12_RESOURCE_STATE_RENDER_TARGET,
        &clearValue, IID_PPV_ARGS(&tex->resource));
    if (FAILED(hr)) { delete tex; return nullptr; }

    // Create SRV
    tex->srvIndex = pipeline->AllocateSrvIndex();
    if (tex->srvIndex >= 0) {
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Format                  = dxgiFormat;
        srvDesc.ViewDimension           = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Texture2D.MipLevels     = 1;

        pipeline->GetDevice()->CreateShaderResourceView(
            tex->resource.Get(), &srvDesc,
            pipeline->GetSrvCpuHandle(tex->srvIndex));
    }

    return tex;
}

JALIUM_API void* jalium_texture_create_2d(void* context, int width, int height, int format, int usage) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || width <= 0 || height <= 0) return nullptr;

    DXGI_FORMAT dxgiFormat = jalium::TextureFormatToDxgi(format);

    D3D12_HEAP_PROPERTIES heapProps = {};
    heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;

    D3D12_RESOURCE_DESC texDesc = {};
    texDesc.Dimension        = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    texDesc.Width            = static_cast<UINT64>(width);
    texDesc.Height           = static_cast<UINT>(height);
    texDesc.DepthOrArraySize = 1;
    texDesc.MipLevels        = 1;
    texDesc.Format           = dxgiFormat;
    texDesc.SampleDesc.Count = 1;
    texDesc.Flags            = D3D12_RESOURCE_FLAG_NONE;

    D3D12_RESOURCE_STATES initialState = D3D12_RESOURCE_STATE_COMMON;

    if (usage & jalium::TEX_USAGE_RENDER_TARGET) {
        texDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
        initialState = D3D12_RESOURCE_STATE_RENDER_TARGET;
    }
    if (usage & jalium::TEX_USAGE_UNORDERED_ACCESS) {
        texDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        initialState = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
    }
    if (usage & jalium::TEX_USAGE_DEPTH_STENCIL) {
        texDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
        dxgiFormat = DXGI_FORMAT_D32_FLOAT;
        texDesc.Format = dxgiFormat;
        initialState = D3D12_RESOURCE_STATE_DEPTH_WRITE;
    }

    auto* tex = new jalium::PipelineTexture();
    tex->width  = width;
    tex->height = height;
    tex->format = dxgiFormat;

    HRESULT hr = pipeline->GetDevice()->CreateCommittedResource(
        &heapProps, D3D12_HEAP_FLAG_NONE,
        &texDesc, initialState,
        nullptr, IID_PPV_ARGS(&tex->resource));
    if (FAILED(hr)) { delete tex; return nullptr; }

    // Create SRV for shader resource usage
    if (usage & jalium::TEX_USAGE_SHADER_RESOURCE) {
        tex->srvIndex = pipeline->AllocateSrvIndex();
        if (tex->srvIndex >= 0) {
            D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
            srvDesc.Format = (usage & jalium::TEX_USAGE_DEPTH_STENCIL)
                ? DXGI_FORMAT_R32_FLOAT  // depth SRV uses float format
                : dxgiFormat;
            srvDesc.ViewDimension           = D3D12_SRV_DIMENSION_TEXTURE2D;
            srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            srvDesc.Texture2D.MipLevels     = 1;

            pipeline->GetDevice()->CreateShaderResourceView(
                tex->resource.Get(), &srvDesc,
                pipeline->GetSrvCpuHandle(tex->srvIndex));
        }
    }

    return tex;
}

JALIUM_API void jalium_texture_destroy(void* context, void* texture) {
    if (!texture) return;
    auto* tex = static_cast<jalium::PipelineTexture*>(texture);
    // Reclaim the SRV descriptor index so it can be reused
    if (tex->srvIndex >= 0) {
        auto* pipeline = jalium::GetPipeline(context);
        if (pipeline) {
            pipeline->FreeSrvIndex(tex->srvIndex);
        }
    }
    delete tex;
}

// ===== Binding =====

JALIUM_API void jalium_bind_vertex_buffer(void* context, void* buffer) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !buffer) return;

    auto* buf = static_cast<jalium::PipelineBuffer*>(buffer);
    auto* cmdList = pipeline->GetCommandList();

    D3D12_VERTEX_BUFFER_VIEW vbv = {};
    vbv.BufferLocation = buf->resource->GetGPUVirtualAddress();
    vbv.SizeInBytes    = static_cast<UINT>(buf->size);
    vbv.StrideInBytes  = jalium::GetVertexStride(pipeline->CurrentInputLayout());

    cmdList->IASetVertexBuffers(0, 1, &vbv);
}

JALIUM_API void jalium_bind_index_buffer(void* context, void* buffer) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !buffer) return;

    auto* buf = static_cast<jalium::PipelineBuffer*>(buffer);
    auto* cmdList = pipeline->GetCommandList();

    D3D12_INDEX_BUFFER_VIEW ibv = {};
    ibv.BufferLocation = buf->resource->GetGPUVirtualAddress();
    ibv.SizeInBytes    = static_cast<UINT>(buf->size);
    ibv.Format         = DXGI_FORMAT_R16_UINT; // managed uses ushort indices

    cmdList->IASetIndexBuffer(&ibv);
}

JALIUM_API void jalium_bind_instance_buffer(void* context, void* buffer) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !buffer) return;

    auto* buf = static_cast<jalium::PipelineBuffer*>(buffer);
    auto* cmdList = pipeline->GetCommandList();

    UINT stride = jalium::GetInstanceStride(pipeline->CurrentInputLayout());
    if (stride == 0) return; // no instance data for this layout

    D3D12_VERTEX_BUFFER_VIEW vbv = {};
    vbv.BufferLocation = buf->resource->GetGPUVirtualAddress();
    vbv.SizeInBytes    = static_cast<UINT>(buf->size);
    vbv.StrideInBytes  = stride;

    cmdList->IASetVertexBuffers(1, 1, &vbv); // slot 1 = instance data
}

JALIUM_API void jalium_bind_uniform_buffer(void* context, void* buffer) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !buffer) return;

    auto* buf = static_cast<jalium::PipelineBuffer*>(buffer);
    auto* cmdList = pipeline->GetCommandList();

    // Bind as root CBV at parameter index 0
    if (pipeline->IsCurrentCompute()) {
        cmdList->SetComputeRootConstantBufferView(
            0, buf->resource->GetGPUVirtualAddress());
    } else {
        cmdList->SetGraphicsRootConstantBufferView(
            0, buf->resource->GetGPUVirtualAddress());
    }
}

JALIUM_API void jalium_bind_texture(void* context, int slot, void* texture) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !texture) return;

    auto* tex = static_cast<jalium::PipelineTexture*>(texture);
    if (tex->srvIndex < 0) return;

    auto* cmdList = pipeline->GetCommandList();
    auto gpuHandle = pipeline->GetSrvGpuHandle(tex->srvIndex);

    // Bind descriptor table at parameter index 1 (SRV table)
    if (pipeline->IsCurrentCompute()) {
        cmdList->SetComputeRootDescriptorTable(1, gpuHandle);
    } else {
        cmdList->SetGraphicsRootDescriptorTable(1, gpuHandle);
    }
}

// ===== State =====

JALIUM_API void jalium_set_scissor(void* context, int x, int y, int width, int height) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return;

    D3D12_RECT rect = {
        static_cast<LONG>(x),
        static_cast<LONG>(y),
        static_cast<LONG>(x + width),
        static_cast<LONG>(y + height)
    };
    pipeline->GetCommandList()->RSSetScissorRects(1, &rect);
}

JALIUM_API void jalium_set_viewport(void* context, int x, int y, int width, int height) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return;

    D3D12_VIEWPORT vp = {};
    vp.TopLeftX = static_cast<FLOAT>(x);
    vp.TopLeftY = static_cast<FLOAT>(y);
    vp.Width    = static_cast<FLOAT>(width);
    vp.Height   = static_cast<FLOAT>(height);
    vp.MinDepth = 0.0f;
    vp.MaxDepth = 1.0f;

    pipeline->GetCommandList()->RSSetViewports(1, &vp);
}

// ===== Draw =====

JALIUM_API void jalium_draw_indexed_instanced(
    void* context,
    uint32_t indexCount, uint32_t instanceCount,
    uint32_t firstIndex, int baseVertex, uint32_t firstInstance)
{
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return;

    auto* cmdList = pipeline->GetCommandList();
    cmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    cmdList->DrawIndexedInstanced(
        indexCount, instanceCount,
        firstIndex, baseVertex, firstInstance);
}

JALIUM_API void jalium_draw(
    void* context,
    uint32_t vertexCount, uint32_t instanceCount,
    uint32_t startVertex, uint32_t startInstance)
{
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return;

    auto* cmdList = pipeline->GetCommandList();
    cmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    cmdList->DrawInstanced(vertexCount, instanceCount, startVertex, startInstance);
}

JALIUM_API void jalium_draw_glyphs(void* context, uint32_t offset, uint32_t count) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || count == 0) return;

    auto* cmdList = pipeline->GetCommandList();
    cmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    // Glyphs use 6 vertices per glyph (two triangles forming a quad)
    cmdList->DrawInstanced(6, count, 0, offset);
}

JALIUM_API void jalium_dispatch(void* context, uint32_t x, uint32_t y, uint32_t z) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return;

    pipeline->GetCommandList()->Dispatch(x, y, z);
}

// ===== Effects =====

JALIUM_API void jalium_apply_effect(
    void* context, int effectType,
    uint32_t srcTex, uint32_t dstTex,
    const void* parameters, int paramSize)
{
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return;

    // Effect dispatch: the managed side sets up the compute PSO, root sig, and
    // binds resources before calling this. We just dispatch the compute shader.
    // Thread group size: 8x8 is standard for image processing.
    // The actual dispatch dimensions are encoded in the effect parameters.
    // For now, dispatch based on parameter data if available.
    (void)effectType;
    (void)srcTex;
    (void)dstTex;
    (void)parameters;
    (void)paramSize;

    // The managed side handles effect setup and calls Dispatch directly
    // via ExecuteCommandBuffer. This function serves as an extension point
    // for future native-side effect orchestration.
}

JALIUM_API void jalium_capture_backdrop(
    void* context, float x, float y, float w, float h,
    uint32_t targetTexIndex)
{
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return;

    (void)x; (void)y; (void)w; (void)h; (void)targetTexIndex;
    // Backdrop capture requires copying the current render target region
    // to the target texture. This is typically done by the managed render
    // graph which sets up copy commands via ExecuteCommandBuffer.
}

JALIUM_API void jalium_apply_backdrop_filter(
    void* context, uint32_t srcTexIndex, uint32_t dstTexIndex)
{
    (void)context; (void)srcTexIndex; (void)dstTexIndex;
    // Backdrop filter is dispatched via compute commands from managed side.
}

JALIUM_API void jalium_composite_layer(
    void* context, uint32_t srcTexIndex,
    float x, float y, float w, float h,
    int blendMode, uint8_t opacity)
{
    (void)context; (void)srcTexIndex;
    (void)x; (void)y; (void)w; (void)h;
    (void)blendMode; (void)opacity;
    // Compositing is driven by the managed render graph using draw commands.
}

JALIUM_API void jalium_submit(void* context) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return;

    pipeline->Submit();
}

// ===== Shader Compilation =====

JALIUM_API int jalium_shader_compile(
    const void* sourceData, int sourceSize,
    const char* entryPoint, const char* target, int flags,
    void** bytecodePtr, int* bytecodeSize,
    void** errorPtr, int* errorSize)
{
    // Initialize outputs
    if (bytecodePtr)  *bytecodePtr  = nullptr;
    if (bytecodeSize) *bytecodeSize = 0;
    if (errorPtr)     *errorPtr     = nullptr;
    if (errorSize)    *errorSize    = 0;

    if (!sourceData || sourceSize <= 0 || !entryPoint || !target)
        return E_INVALIDARG;

    ComPtr<ID3DBlob> codeBlob, errorBlob;
    HRESULT hr = D3DCompile(
        sourceData,
        static_cast<SIZE_T>(sourceSize),
        nullptr,   // source name (debug)
        nullptr,   // defines
        D3D_COMPILE_STANDARD_FILE_INCLUDE,
        entryPoint,
        target,
        static_cast<UINT>(flags),
        0,         // effect flags
        &codeBlob,
        &errorBlob);

    // Copy error message if any
    if (errorBlob && errorBlob->GetBufferSize() > 0) {
        auto errSize = static_cast<int>(errorBlob->GetBufferSize());
        void* errCopy = malloc(static_cast<size_t>(errSize));
        if (errCopy) {
            memcpy(errCopy, errorBlob->GetBufferPointer(), static_cast<size_t>(errSize));
            if (errorPtr)  *errorPtr  = errCopy;
            if (errorSize) *errorSize = errSize;
        }
    }

    if (FAILED(hr)) return hr;

    // Copy bytecode
    if (codeBlob && codeBlob->GetBufferSize() > 0) {
        auto codeSize = static_cast<int>(codeBlob->GetBufferSize());
        void* codeCopy = malloc(static_cast<size_t>(codeSize));
        if (codeCopy) {
            memcpy(codeCopy, codeBlob->GetBufferPointer(), static_cast<size_t>(codeSize));
            if (bytecodePtr)  *bytecodePtr  = codeCopy;
            if (bytecodeSize) *bytecodeSize = codeSize;
        }
    }

    return S_OK;
}

JALIUM_API void jalium_shader_free_blob(void* blob) {
    free(blob);
}

// ===== Pipeline State Objects =====

JALIUM_API void* jalium_pso_create_graphics(
    void* context,
    const void* vsBytecode, int vsSize,
    const void* psBytecode, int psSize,
    int inputLayout, int blendMode, int cullMode,
    int depthEnable, int rtFormat, int sampleCount,
    int rootSigType)
{
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return nullptr;
    if (!vsBytecode || vsSize <= 0 || !psBytecode || psSize <= 0)
        return nullptr;

    // First create the root signature for this PSO
    ComPtr<ID3D12RootSignature> rootSig;
    HRESULT hr = CreateSerializedRootSignature(
        pipeline->GetDevice(), rootSigType, &rootSig);
    if (FAILED(hr)) return nullptr;

    // Get input layout
    const D3D12_INPUT_ELEMENT_DESC* inputElements = nullptr;
    UINT inputElementCount = 0;
    GetInputLayout(inputLayout, &inputElements, &inputElementCount);

    // Build PSO description
    D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
    psoDesc.pRootSignature = rootSig.Get();
    psoDesc.VS = { vsBytecode, static_cast<SIZE_T>(vsSize) };
    psoDesc.PS = { psBytecode, static_cast<SIZE_T>(psSize) };
    psoDesc.BlendState = BuildBlendDesc(blendMode);
    psoDesc.SampleMask = UINT_MAX;

    // Rasterizer
    psoDesc.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
    psoDesc.RasterizerState.CullMode = jalium::CullModeToDx(cullMode);
    psoDesc.RasterizerState.FrontCounterClockwise = FALSE;
    psoDesc.RasterizerState.DepthClipEnable = TRUE;
    psoDesc.RasterizerState.MultisampleEnable = (sampleCount > 1) ? TRUE : FALSE;
    psoDesc.RasterizerState.AntialiasedLineEnable = FALSE;

    // Depth/stencil
    psoDesc.DepthStencilState.DepthEnable    = depthEnable ? TRUE : FALSE;
    psoDesc.DepthStencilState.DepthWriteMask = depthEnable
        ? D3D12_DEPTH_WRITE_MASK_ALL
        : D3D12_DEPTH_WRITE_MASK_ZERO;
    psoDesc.DepthStencilState.DepthFunc      = D3D12_COMPARISON_FUNC_LESS_EQUAL;
    psoDesc.DepthStencilState.StencilEnable  = FALSE;

    // Input layout
    psoDesc.InputLayout = { inputElements, inputElementCount };
    psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;

    // Render target
    psoDesc.NumRenderTargets = 1;
    psoDesc.RTVFormats[0]    = jalium::RtFormatToDxgi(rtFormat);
    psoDesc.DSVFormat        = depthEnable
        ? DXGI_FORMAT_D32_FLOAT
        : DXGI_FORMAT_UNKNOWN;
    psoDesc.SampleDesc.Count = static_cast<UINT>(sampleCount > 0 ? sampleCount : 1);

    auto* pso = new jalium::PipelinePSO();
    pso->inputLayoutType = inputLayout;
    pso->isCompute = false;

    hr = pipeline->GetDevice()->CreateGraphicsPipelineState(
        &psoDesc, IID_PPV_ARGS(&pso->pso));
    if (FAILED(hr)) { delete pso; return nullptr; }

    return pso;
}

JALIUM_API void* jalium_pso_create_compute(
    void* context,
    const void* csBytecode, int csSize,
    int rootSigType)
{
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !csBytecode || csSize <= 0) return nullptr;

    ComPtr<ID3D12RootSignature> rootSig;
    HRESULT hr = CreateSerializedRootSignature(
        pipeline->GetDevice(), rootSigType, &rootSig);
    if (FAILED(hr)) return nullptr;

    D3D12_COMPUTE_PIPELINE_STATE_DESC psoDesc = {};
    psoDesc.pRootSignature = rootSig.Get();
    psoDesc.CS = { csBytecode, static_cast<SIZE_T>(csSize) };

    auto* pso = new jalium::PipelinePSO();
    pso->isCompute = true;
    pso->inputLayoutType = jalium::INPUT_NONE;

    hr = pipeline->GetDevice()->CreateComputePipelineState(
        &psoDesc, IID_PPV_ARGS(&pso->pso));
    if (FAILED(hr)) { delete pso; return nullptr; }

    return pso;
}

JALIUM_API void jalium_pso_destroy(void* context, void* pso) {
    (void)context;
    if (!pso) return;
    delete static_cast<jalium::PipelinePSO*>(pso);
}

// ===== Root Signatures =====

JALIUM_API void* jalium_root_signature_create(void* context, int type) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return nullptr;

    ID3D12RootSignature* rootSig = nullptr;
    HRESULT hr = CreateSerializedRootSignature(
        pipeline->GetDevice(), type, &rootSig);
    if (FAILED(hr)) return nullptr;

    return rootSig;
}

JALIUM_API void jalium_root_signature_destroy(void* context, void* rootSig) {
    (void)context;
    if (!rootSig) return;
    static_cast<ID3D12RootSignature*>(rootSig)->Release();
}

// ===== Descriptors =====

JALIUM_API int jalium_descriptor_create_srv(void* context, void* resource) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !resource) return -1;

    auto* res = jalium::ExtractD3D12Resource(resource);

    int index = pipeline->AllocateSrvIndex();
    if (index < 0) return -1;

    auto resDesc = res->GetDesc();

    D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;

    if (resDesc.Dimension == D3D12_RESOURCE_DIMENSION_TEXTURE2D) {
        srvDesc.Format                  = resDesc.Format;
        srvDesc.ViewDimension           = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Texture2D.MipLevels     = resDesc.MipLevels;
    } else if (resDesc.Dimension == D3D12_RESOURCE_DIMENSION_BUFFER) {
        srvDesc.Format                        = DXGI_FORMAT_R32_TYPELESS;
        srvDesc.ViewDimension                 = D3D12_SRV_DIMENSION_BUFFER;
        srvDesc.Buffer.NumElements            = static_cast<UINT>(resDesc.Width / 4);
        srvDesc.Buffer.Flags                  = D3D12_BUFFER_SRV_FLAG_RAW;
    }

    pipeline->GetDevice()->CreateShaderResourceView(
        res, &srvDesc, pipeline->GetSrvCpuHandle(index));

    return index;
}

JALIUM_API int jalium_descriptor_create_cbv(void* context, void* buffer, int offset, int size) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !buffer || size <= 0) return -1;

    auto* res = jalium::ExtractD3D12Resource(buffer);

    int index = pipeline->AllocateSrvIndex();
    if (index < 0) return -1;

    D3D12_CONSTANT_BUFFER_VIEW_DESC cbvDesc = {};
    cbvDesc.BufferLocation = res->GetGPUVirtualAddress() + static_cast<UINT64>(offset);
    cbvDesc.SizeInBytes    = (static_cast<UINT>(size) + 255) & ~255u; // 256-byte aligned

    pipeline->GetDevice()->CreateConstantBufferView(
        &cbvDesc, pipeline->GetSrvCpuHandle(index));

    return index;
}

JALIUM_API int jalium_descriptor_create_uav(void* context, void* resource) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !resource) return -1;

    auto* res = jalium::ExtractD3D12Resource(resource);

    int index = pipeline->AllocateSrvIndex();
    if (index < 0) return -1;

    auto resDesc = res->GetDesc();

    D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};

    if (resDesc.Dimension == D3D12_RESOURCE_DIMENSION_TEXTURE2D) {
        uavDesc.Format        = resDesc.Format;
        uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
    } else if (resDesc.Dimension == D3D12_RESOURCE_DIMENSION_BUFFER) {
        uavDesc.Format                      = DXGI_FORMAT_R32_TYPELESS;
        uavDesc.ViewDimension               = D3D12_UAV_DIMENSION_BUFFER;
        uavDesc.Buffer.NumElements          = static_cast<UINT>(resDesc.Width / 4);
        uavDesc.Buffer.Flags                = D3D12_BUFFER_UAV_FLAG_RAW;
    }

    pipeline->GetDevice()->CreateUnorderedAccessView(
        res, nullptr, &uavDesc, pipeline->GetSrvCpuHandle(index));

    return index;
}

JALIUM_API void jalium_descriptor_free(void* context, int index) {
    auto* pipeline = jalium::GetPipeline(context);
    if (pipeline) {
        pipeline->FreeSrvIndex(index);
    }
}

// ===== Commands =====

JALIUM_API void jalium_cmd_set_pso(void* context, void* pso) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !pso) return;

    auto* p = static_cast<jalium::PipelinePSO*>(pso);
    pipeline->SetCurrentPSO(p);
    pipeline->GetCommandList()->SetPipelineState(p->pso.Get());
}

JALIUM_API void jalium_cmd_set_root_signature(void* context, void* rootSig) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !rootSig) return;

    auto* rs = static_cast<ID3D12RootSignature*>(rootSig);
    auto* cmdList = pipeline->GetCommandList();

    if (pipeline->IsCurrentCompute()) {
        cmdList->SetComputeRootSignature(rs);
    } else {
        cmdList->SetGraphicsRootSignature(rs);
    }

    // Re-bind descriptor heaps after root signature change
    ID3D12DescriptorHeap* heaps[] = { pipeline->GetSrvHeap() };
    cmdList->SetDescriptorHeaps(_countof(heaps), heaps);
}

JALIUM_API void jalium_cmd_resource_barrier(
    void* context, void* resource,
    int stateBefore, int stateAfter)
{
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline || !resource) return;

    D3D12_RESOURCE_BARRIER barrier = {};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource   = jalium::ExtractD3D12Resource(resource);
    barrier.Transition.StateBefore = static_cast<D3D12_RESOURCE_STATES>(stateBefore);
    barrier.Transition.StateAfter  = static_cast<D3D12_RESOURCE_STATES>(stateAfter);
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;

    pipeline->GetCommandList()->ResourceBarrier(1, &barrier);
}

JALIUM_API void jalium_cmd_clear_rt(
    void* context, uint32_t rtId,
    float r, float g, float b, float a)
{
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return;

    // rtId is an opaque render target identifier from the managed render graph.
    // The managed side is responsible for setting up the RTV before issuing this
    // command. For now, we treat rtId as a handle and create an on-the-fly RTV.
    // In practice this is driven by the managed render graph's RT management.
    (void)rtId;
    float clearColor[4] = { r, g, b, a };
    (void)clearColor;

    // TODO: Full implementation requires the managed side to pass the
    // PipelineTexture* handle or an RTV heap index for the target RT.
    // The current render graph drives this via SetRenderTarget + ClearRenderTarget
    // commands which will be extended as the managed pipeline matures.
}

// ===== Sync =====

JALIUM_API void jalium_fence_signal(void* context, uint64_t fenceValue) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return;

    pipeline->SignalFence(fenceValue);
}

JALIUM_API void jalium_fence_wait(void* context, uint64_t fenceValue) {
    auto* pipeline = jalium::GetPipeline(context);
    if (!pipeline) return;

    pipeline->WaitForFence(fenceValue);
}

// ===== Device Info =====

JALIUM_API void* jalium_get_device(void* context) {
    auto* backend = jalium::GetD3D12Backend(context);
    if (!backend) return nullptr;
    return backend->GetDevice();
}

JALIUM_API void* jalium_get_command_queue(void* context) {
    auto* backend = jalium::GetD3D12Backend(context);
    if (!backend) return nullptr;
    return backend->GetCommandQueue();
}

} // extern "C"
