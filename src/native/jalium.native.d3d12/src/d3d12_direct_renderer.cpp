#include "d3d12_direct_renderer.h"
#include "d3d12_vello.h"
#include "d3d12_shader_source.h"
#include "d3d12_shader_bytecode.h"
#include <d3dcompiler.h>
#include <cassert>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <cstdio>
#include <vector>
#pragma comment(lib, "d3dcompiler.lib")

namespace jalium {

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
    }

    // RTV heap
    {
        D3D12_DESCRIPTOR_HEAP_DESC desc = {};
        desc.NumDescriptors = frameCount_ + 2;  // +2 for offscreen RT slots
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
        frameConstantsBuffer_->Map(0, nullptr, &frameConstantsMapped_);
    }

    if (!CreateFrameResources()) return false;
    if (!CreateRootSignature()) return false;
    if (!CreatePSOs()) return false;
    if (!CreateBlurResources()) {
        OutputDebugStringA("[D3D12DirectRenderer] Blur resources init failed (non-fatal)\n");
        // Non-fatal: blur will fall back to semi-transparent overlay
    }

    // Initialize glyph atlas for text rendering
    auto dwriteFactory = backend_->GetDWriteFactory();
    if (dwriteFactory) {
        glyphAtlas_ = std::make_unique<D3D12GlyphAtlas>(device_, dwriteFactory);
        if (!glyphAtlas_->Initialize()) {
            OutputDebugStringA("[D3D12DirectRenderer] GlyphAtlas init failed\n");
            glyphAtlas_.reset();
        }
    }

    // Initialize Vello GPU path renderer
    velloRenderer_ = std::make_unique<D3D12VelloRenderer>(device_, nullptr);
    if (!velloRenderer_->Initialize()) {
        OutputDebugStringA("[D3D12DirectRenderer] Vello init failed (non-fatal, falling back to CPU triangulation)\n");
        velloRenderer_.reset();
    } else {
        velloRenderer_->SetGPUPipeline(true);
        // Vello GPU pipeline enabled
    }

    initialized_ = true;
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
            if (fence_->GetCompletedValue() < fv) {
                fence_->SetEventOnCompletion(fv, fenceEvent_);
                WaitForSingleObject(fenceEvent_, 5000);
            }
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
    // Wait for GPU idle before releasing
    if (fence_ && fenceEvent_) {
        auto queue = backend_->GetCommandQueue();
        if (queue) {
            uint64_t fv = nextFenceValue_++;
            queue->Signal(fence_.Get(), fv);
            if (fence_->GetCompletedValue() < fv) {
                fence_->SetEventOnCompletion(fv, fenceEvent_);
                WaitForSingleObject(fenceEvent_, 5000);
            }
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

    // Back buffer references should already be released via ReleaseBackBufferReferences().
    // Defensive: release again in case caller didn't call it.
    for (UINT i = 0; i < frameCount_; i++) {
        renderTargets_[i].Reset();
    }

    // Acquire new back buffer references from the (already resized) swap chain
    for (UINT i = 0; i < frameCount_; i++) {
        HRESULT hr = swapChain_->GetBuffer(i, IID_PPV_ARGS(&renderTargets_[i]));
        if (FAILED(hr)) return false;
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

        // Instance upload buffer (persistently mapped)
        auto heapProps = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
        auto bufDesc = MakeBufferDesc(kInstanceBufferSize);
        if (FAILED(device_->CreateCommittedResource(
                &heapProps, D3D12_HEAP_FLAG_NONE, &bufDesc,
                D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
                IID_PPV_ARGS(&fr.instanceUploadBuffer))))
            return false;
        fr.instanceUploadBuffer->Map(0, nullptr, &fr.instanceMappedPtr);

        // Per-frame constants ring buffer — each FlushGraphicsForCompute gets its own
        // 256-byte aligned slot, so offscreen and main-RT draws see correct constants.
        auto cbHeapProps = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
        auto cbBufDesc = MakeBufferDesc(kConstantsRingSize);
        if (FAILED(device_->CreateCommittedResource(
                &cbHeapProps, D3D12_HEAP_FLAG_NONE, &cbBufDesc,
                D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
                IID_PPV_ARGS(&fr.constantsBuffer))))
            return false;
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
    commandList_->Close();

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

    D3D12_ROOT_PARAMETER params[3] = {};
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

    // [2] Root 32-bit constant — instance base offset (b1)
    params[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
    params[2].Constants.ShaderRegister = 1;
    params[2].Constants.RegisterSpace = 0;
    params[2].Constants.Num32BitValues = 8;
    params[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_VERTEX;

    // Static samplers
    D3D12_STATIC_SAMPLER_DESC samplers[2] = {};

    // s0 — linear clamp (general texture sampling)
    samplers[0].Filter = D3D12_FILTER_MIN_MAG_MIP_LINEAR;
    samplers[0].AddressU = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[0].AddressV = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[0].AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[0].ShaderRegister = 0;
    samplers[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

    // s1 — point clamp (pixel-exact ClearType text sampling)
    samplers[1].Filter = D3D12_FILTER_MIN_MAG_MIP_POINT;
    samplers[1].AddressU = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[1].AddressV = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[1].AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplers[1].ShaderRegister = 1;
    samplers[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

    D3D12_ROOT_SIGNATURE_DESC rootSigDesc = {};
    rootSigDesc.NumParameters = 3;
    rootSigDesc.pParameters = params;
    rootSigDesc.NumStaticSamplers = 2;
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

    currentFrame_ = frameIndex;
    viewportWidth_ = width;
    viewportHeight_ = height;

    auto& fr = frames_[currentFrame_];

    // Wait for the GPU to finish this buffer's previous work.
    // Use a longer timeout to avoid busy-spinning on integrated GPUs where
    // frame times are inherently longer (~10-16ms).  The UI thread blocks
    // here, which is fine because there is no useful work to do until the
    // GPU frees this buffer.  A short timeout (1ms) caused excessive
    // TryBeginDraw failures on iGPUs, wasting CPU cycles on retry loops.
    //
    // IMPORTANT: ResetEvent before SetEventOnCompletion to clear any stale signal
    // from a previous timed-out wait.  Without this, a previous timeout leaves the
    // event signaled when the old fence eventually completes, causing the NEXT
    // WaitForSingleObject to return immediately even though the new fence value
    // hasn't been reached — leading to command allocator reuse while the GPU is
    // still executing, which corrupts rendering (flickering, stray lines).
    if (fr.fenceValue > 0 && fence_->GetCompletedValue() < fr.fenceValue) {
        ResetEvent(fenceEvent_);
        fence_->SetEventOnCompletion(fr.fenceValue, fenceEvent_);
        WaitForSingleObject(fenceEvent_, 50);
        if (fence_->GetCompletedValue() < fr.fenceValue) {
            return false;  // Still not ready after 50ms — something is very wrong
        }
    }

    // Reset allocator + command list
    fr.commandAllocator->Reset();
    commandList_->Reset(fr.commandAllocator.Get(), nullptr);

    // Transition back buffer: PRESENT → RENDER_TARGET
    auto barrier = MakeTransitionBarrier(
        renderTargets_[currentFrame_].Get(),
        D3D12_RESOURCE_STATE_PRESENT,
        D3D12_RESOURCE_STATE_RENDER_TARGET);
    commandList_->ResourceBarrier(1, &barrier);

    // Set render target
    auto rtvHandle = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
    rtvHandle.ptr += currentFrame_ * rtvDescriptorSize_;
    commandList_->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    // Clear if requested (full invalidation frames)
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

    // Reset glyph atlas if it overflowed in the previous frame
    if (glyphAtlas_ && glyphAtlas_->NeedsReset()) {
        glyphAtlas_->Reset();
        glyphAtlas_->ClearResetFlag();
    }

    // Reset scissor stack
    while (!scissorStack_.empty()) scissorStack_.pop();

    // Reset pre-glass snapshot flag for fused panels
    preGlassSnapshotCaptured_ = false;
    snapshotValid_ = false;
    snapshotUsedThisFrame_ = false;
    offscreenCaptureValid_[0] = false;
    offscreenCaptureValid_[1] = false;
    offscreenResourcesUsedThisFrame_ = false;
    blurTempsUsedThisFrame_ = false;
    inOffscreenCapture_ = false;
    fr.constantsRingOffset = 0;  // reset ring buffer for this frame

    // Reset transform stack with identity
    while (!transformStack_.empty()) transformStack_.pop();
    transformStack_.push(Transform2D::Identity());

    // Begin Vello frame
    if (velloRenderer_) {
        velloRenderer_->BeginFrame(width, height);
    }

    inFrame_ = true;
    return true;
}

void D3D12DirectRenderer::AbortFrame()
{
    if (!inFrame_) return;
    inFrame_ = false;

    // Close the command list without executing — discard all recorded commands.
    // The GPU will never see this frame's work. No barrier transitions needed
    // because the command list is never submitted to the queue.
    if (commandList_) {
        commandList_->Close();
    }

    // Clear instance collections so they don't leak into the next frame
    rectInstances_.clear();
    textInstances_.clear();
    bitmapInstances_.clear();
    triangleVertices_.clear();
    bitmapTextures_.clear();
    batches_.clear();

    // Reset snapshot validity — the snapshot may reference stale back buffer content
    snapshotValid_ = false;
    snapshotUsedThisFrame_ = false;
    offscreenCaptureValid_[0] = false;
    offscreenCaptureValid_[1] = false;
    offscreenResourcesUsedThisFrame_ = false;
    blurTempsUsedThisFrame_ = false;
    inOffscreenCapture_ = false;
}

JaliumResult D3D12DirectRenderer::EndFrame(bool useDirtyRects, const std::vector<D3D12_RECT>& dirtyRects,
                                    UINT syncInterval, UINT presentFlags)
{
    if (!inFrame_) return JALIUM_ERROR_INVALID_STATE;
    inFrame_ = false;

    // Flush Vello paths before recording graphics commands
    FlushVelloPaths();

    // Upload instance data + record draw commands
    UploadInstances();
    RecordDrawCommands();

    // Transition: RENDER_TARGET → PRESENT
    auto barrier = MakeTransitionBarrier(
        renderTargets_[currentFrame_].Get(),
        D3D12_RESOURCE_STATE_RENDER_TARGET,
        D3D12_RESOURCE_STATE_PRESENT);
    commandList_->ResourceBarrier(1, &barrier);

    // Close and execute
    HRESULT closeHr = commandList_->Close();
    if (FAILED(closeHr)) {
        // Command list recording had errors — submitting it would cause device removal.
        // Log the failure and skip this frame.
        LogDeviceRemovedReason("CommandList::Close", device_, closeHr);
        return JALIUM_ERROR_INVALID_STATE;
    }
    ID3D12CommandList* lists[] = { commandList_.Get() };
    backend_->GetCommandQueue()->ExecuteCommandLists(1, lists);

    // Present
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

    // Signal fence for this frame
    frames_[currentFrame_].fenceValue = nextFenceValue_++;
    backend_->GetCommandQueue()->Signal(fence_.Get(), frames_[currentFrame_].fenceValue);

    if (SUCCEEDED(hr) || hr == DXGI_STATUS_OCCLUDED) {
        return JALIUM_OK;
    }

    // True device loss — GPU removed, reset, or driver crash.
    if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET) {
        LogDeviceRemovedReason("Present", device_, hr);
        return JALIUM_ERROR_DEVICE_LOST;
    }

    // Transient Present failure (e.g. DXGI_ERROR_INVALID_CALL during resize,
    // mode change, etc.).  Treat as a dropped frame — the next frame will retry.
    return JALIUM_OK;
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

void D3D12DirectRenderer::AddSdfRect(const SdfRectInstance& inst)
{
    if (rectInstances_.size() >= kMaxInstancesPerFrame) {
        if (inOffscreenCapture_) {
            OutputDebugStringA("[AddSdfRect] AUTO-FLUSH during offscreen! CLEARING all instances\n");
        }
        // Auto-flush: upload and record the current batch, then continue
        // instead of dropping the draw call.
        FlushGraphicsForCompute();
    }

    DrawBatch batch;
    batch.type = DrawBatchType::SdfRect;
    batch.instanceOffset = (uint32_t)rectInstances_.size();
    batch.instanceCount = 1;
    batch.sortOrder = drawOrder_++;
    batch.hasScissor = !scissorStack_.empty();
    if (batch.hasScissor) {
        batch.scissor = scissorStack_.top();
    }
    batches_.push_back(batch);

    // Premultiply fill/border alpha (SDF rect shader expects premultiplied RGBA).
    SdfRectInstance adjusted = inst;
    adjusted.fillR *= adjusted.fillA;
    adjusted.fillG *= adjusted.fillA;
    adjusted.fillB *= adjusted.fillA;
    adjusted.borderR *= adjusted.borderA;
    adjusted.borderG *= adjusted.borderA;
    adjusted.borderB *= adjusted.borderA;

    // Apply current opacity
    adjusted.opacity *= currentOpacity_;

    // Apply current transform (CPU-side)
    const auto& t = transformStack_.top();
    float newX = adjusted.posX * t.m11 + adjusted.posY * t.m21 + t.dx;
    float newY = adjusted.posX * t.m12 + adjusted.posY * t.m22 + t.dy;
    adjusted.posX = newX;
    adjusted.posY = newY;

    // Scale the size by the transform's scale components
    float scaleX = std::sqrt(t.m11 * t.m11 + t.m12 * t.m12);
    float scaleY = std::sqrt(t.m21 * t.m21 + t.m22 * t.m22);
    adjusted.sizeX *= scaleX;
    adjusted.sizeY *= scaleY;

    // Scale corner radii and border width
    float avgScale = (scaleX + scaleY) * 0.5f;
    adjusted.cornerTL *= avgScale;
    adjusted.cornerTR *= avgScale;
    adjusted.cornerBR *= avgScale;
    adjusted.cornerBL *= avgScale;
    adjusted.borderWidth *= avgScale;

    // Apply current shape type (0 = RoundedRect, 1 = SuperEllipse)
    adjusted.shapeType = currentShapeType_;
    adjusted.shapeN = currentShapeN_;

    rectInstances_.push_back(adjusted);
    if (inOffscreenCapture_) {
        char buf[128];
        sprintf_s(buf, "[AddSdfRect OFFSCREEN] pos=(%.0f,%.0f) size=(%.0f,%.0f) rects=%zu\n",
            adjusted.posX, adjusted.posY, adjusted.sizeX, adjusted.sizeY, rectInstances_.size());
        OutputDebugStringA(buf);
    }
}

void D3D12DirectRenderer::AddText(IDWriteTextLayout* layout, float x, float y,
                                   float r, float g, float b, float a)
{
    if (!glyphAtlas_ || !layout) return;

    if (textInstances_.size() >= kMaxInstancesPerFrame) {
        if (inOffscreenCapture_) {
            OutputDebugStringA("[AddText] AUTO-FLUSH during offscreen! CLEARING all instances\n");
        }
        FlushGraphicsForCompute();
    }

    uint32_t startIdx = (uint32_t)textInstances_.size();

    // Apply current transform to the text origin
    const auto& t = transformStack_.top();
    float tx = x * t.m11 + y * t.m21 + t.dx;
    float ty = x * t.m12 + y * t.m22 + t.dy;

    // Apply current opacity
    float effectiveA = a * currentOpacity_;

    // Collect glyph instances and text decorations
    std::vector<D3D12GlyphAtlas::TextDecorationRect> decorations;
    uint32_t count = glyphAtlas_->GenerateGlyphs(layout, tx, ty, r, g, b, effectiveA,
                                                  textInstances_, &decorations);

    // Apply transform scaling to each glyph instance.
    // GenerateGlyphs places glyphs at absolute screen positions using the
    // transformed origin (tx,ty), but the glyph offsets within the layout
    // and glyph sizes are not scaled.  When a non-identity transform is
    // active (e.g. liquid glass ScaleTransform), scale each glyph's
    // position (relative to origin) and size so text visually deforms.
    float scaleX = std::sqrt(t.m11 * t.m11 + t.m12 * t.m12);
    float scaleY = std::sqrt(t.m21 * t.m21 + t.m22 * t.m22);
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
        DrawBatch batch;
        batch.type = DrawBatchType::Text;
        batch.instanceOffset = startIdx;
        batch.instanceCount = count;
        batch.sortOrder = drawOrder_++;
        batch.hasScissor = !scissorStack_.empty();
        if (batch.hasScissor) batch.scissor = scissorStack_.top();
        batches_.push_back(batch);
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
                                     float uvMaxX, float uvMaxY)
{
    if (!textureResource) return;
    if (bitmapInstances_.size() >= kMaxInstancesPerFrame) {
        if (inOffscreenCapture_) {
            OutputDebugStringA("[AddBitmap] AUTO-FLUSH during offscreen! CLEARING all instances\n");
        }
        // Auto-flush: upload and record the current batch, then continue
        FlushGraphicsForCompute();
    }

    BitmapQuadInstance inst = {};
    inst.posX = x; inst.posY = y;
    inst.sizeX = w; inst.sizeY = h;
    inst.uvMinX = 0.0f; inst.uvMinY = 0.0f;
    inst.uvMaxX = uvMaxX; inst.uvMaxY = uvMaxY;
    inst.opacity = opacity * currentOpacity_;

    // Apply current transform (CPU-side)
    const auto& t = transformStack_.top();
    float newX = inst.posX * t.m11 + inst.posY * t.m21 + t.dx;
    float newY = inst.posX * t.m12 + inst.posY * t.m22 + t.dy;
    inst.posX = newX;
    inst.posY = newY;
    float scaleX = std::sqrt(t.m11 * t.m11 + t.m12 * t.m12);
    float scaleY = std::sqrt(t.m21 * t.m21 + t.m22 * t.m22);
    inst.sizeX *= scaleX;
    inst.sizeY *= scaleY;

    DrawBatch batch;
    batch.type = DrawBatchType::Bitmap;
    batch.instanceOffset = (uint32_t)bitmapInstances_.size();
    batch.instanceCount = 1;
    batch.sortOrder = drawOrder_++;
    batch.hasScissor = !scissorStack_.empty();
    if (batch.hasScissor) batch.scissor = scissorStack_.top();

    BitmapBatchTexture tex;
    tex.batchIndex = (uint32_t)batches_.size();
    tex.textureResource = textureResource;
    tex.format = format;
    bitmapTextures_.push_back(tex);

    batches_.push_back(batch);
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
        FlushGraphicsForCompute();
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

    DrawBatch batch;
    batch.type = DrawBatchType::Triangle;
    batch.instanceOffset = startVertex;         // repurposed: vertex offset
    batch.instanceCount = vertexCount;           // repurposed: vertex count
    batch.sortOrder = drawOrder_++;
    batch.hasScissor = !scissorStack_.empty();
    if (batch.hasScissor) batch.scissor = scissorStack_.top();
    batches_.push_back(batch);
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

    D3D12_RECT rect;
    rect.left = (LONG)(minX * dpiScale_);
    rect.top = (LONG)(minY * dpiScale_);
    rect.right = (LONG)(maxX * dpiScale_);
    rect.bottom = (LONG)(maxY * dpiScale_);

    // Intersect with parent scissor if any
    if (!scissorStack_.empty()) {
        auto& parent = scissorStack_.top();
        rect.left = std::max(rect.left, parent.left);
        rect.top = std::max(rect.top, parent.top);
        rect.right = std::min(rect.right, parent.right);
        rect.bottom = std::min(rect.bottom, parent.bottom);
    }

    scissorStack_.push(rect);
    if (inOffscreenCapture_) {
        OutputDebugStringA("[PushScissor OFFSCREEN] called — triggers FlushGfx\n");
    }
}

void D3D12DirectRenderer::PopScissor()
{
    if (!scissorStack_.empty())
        scissorStack_.pop();
    if (inOffscreenCapture_) {
        OutputDebugStringA("[PopScissor OFFSCREEN] called\n");
    }
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

    FlushGraphicsForCompute();

    // Pass current scissor to Vello for tile culling
    if (!scissorStack_.empty()) {
        auto& s = scissorStack_.top();
        velloRenderer_->SetScissorRect(
            (float)s.left, (float)s.top,
            (float)s.right, (float)s.bottom);
    } else {
        velloRenderer_->ClearScissorRect();
    }

    if (velloRenderer_->Dispatch(commandList_.Get(), currentFrame_)) {
        ID3D12Resource* output = velloRenderer_->GetOutputTexture();
        if (output) {
            // Composite Vello output as a full-viewport bitmap
            float w = (float)viewportWidth_ / dpiScale_;
            float h = (float)viewportHeight_ / dpiScale_;
            AddBitmap(0, 0, w, h, 1.0f, output, DXGI_FORMAT_R8G8B8A8_UNORM, 1.0f, 1.0f);
        }
    }
}

// ============================================================================
// Upload + Record
// ============================================================================

void D3D12DirectRenderer::UploadInstances()
{
    auto& fr = frames_[currentFrame_];
    uint8_t* dst = (uint8_t*)fr.instanceMappedPtr;

    // Align start offset to SdfRectInstance stride for StructuredBuffer SRV compatibility
    size_t rectAlign = sizeof(SdfRectInstance);
    size_t offset = ((uploadBufferOffset_ + rectAlign - 1) / rectAlign) * rectAlign;
    size_t rectStartByteOffset = offset;  // save for SRV creation

    // Upload rect instances
    if (!rectInstances_.empty()) {
        size_t rectDataSize = rectInstances_.size() * sizeof(SdfRectInstance);
        if (offset + rectDataSize <= kInstanceBufferSize) {
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
        if (textBufferOffset + textDataSize <= kInstanceBufferSize) {
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
        if (bitmapBufferOffset + bitmapDataSize <= kInstanceBufferSize) {
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
        if (triBufferOffset + triDataSize <= kInstanceBufferSize) {
            memcpy(dst + triBufferOffset, triangleVertices_.data(), triDataSize);
            offset = triBufferOffset + triDataSize;
        } else {
            OutputDebugStringA("[D3D12DirectRenderer] WARNING: Triangle vertex buffer overflow — data dropped\n");
        }
    }

    // ── Descriptor ring buffer ──
    // Save upload buffer write position for next flush
    uploadBufferOffset_ = offset;
    // Each flush allocates descriptor slots from the SRV heap.
    // Base layout: [0-1] rect SRV + atlas, [4-5] text SRV + atlas
    // Per-bitmap/snapshot batch: 2 extra slots (instance SRV + texture)
    // This avoids overwriting descriptors that are still referenced by earlier draws
    // on the same command list (GPU hasn't executed them yet).
    UINT numBitmapSlots = (UINT)(bitmapTextures_.size() + CountSnapshotBlitBatches()) * 2;
    UINT kSlotsPerFlush = 8 + numBitmapSlots;
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

    // Default scissor
    D3D12_RECT fullScissor = { 0, 0, (LONG)viewportWidth_, (LONG)viewportHeight_ };
    D3D12_RECT currentScissor = fullScissor;

    // Build lookup: batchIndex → bitmapTextures_ index for bitmap batches
    size_t nextBitmapTexIdx = 0;

    // Draw in painter's order (batches are already in order)
    DrawBatchType currentPSO = DrawBatchType::SdfRect;
    commandList_->SetPipelineState(sdfRectPSO_.Get());

    for (size_t batchIdx = 0; batchIdx < batches_.size(); batchIdx++) {
        const auto& batch = batches_[batchIdx];


        // Switch PSO if needed
        if (batch.type != currentPSO) {
            currentPSO = batch.type;
            switch (batch.type) {
            case DrawBatchType::SdfRect:
            case DrawBatchType::Ellipse:
            case DrawBatchType::Line:
                // Ellipse and Line reuse SdfRect PSO
                commandList_->SetPipelineState(sdfRectPSO_.Get());
                commandList_->SetGraphicsRootDescriptorTable(1, srvGpuBase);
                break;
            case DrawBatchType::Text:
                commandList_->SetPipelineState(textPSO_.Get());
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

        // Apply per-batch scissor rect for clipping
        if (batch.hasScissor) {
            // Skip batches with empty scissor rects (fully clipped elements)
            if (batch.scissor.left >= batch.scissor.right || batch.scissor.top >= batch.scissor.bottom)
                continue;
            commandList_->RSSetScissorRects(1, &batch.scissor);
        } else {
            D3D12_RECT fullScissor = { 0, 0, (LONG)viewportWidth_, (LONG)viewportHeight_ };
            commandList_->RSSetScissorRects(1, &fullScissor);
        }

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

    if (fence_->GetCompletedValue() < fenceValue) {
        ResetEvent(fenceEvent_);
        if (SUCCEEDED(fence_->SetEventOnCompletion(fenceValue, fenceEvent_))) {
            WaitForSingleObject(fenceEvent_, 5000);
        }
    }
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

void D3D12DirectRenderer::FlushGraphicsForCompute()
{
    if (inOffscreenCapture_) {
        char buf[256];
        sprintf_s(buf, "[FlushGfx OFFSCREEN] rects=%zu text=%zu batches=%zu — CLEARING ALL\n",
            rectInstances_.size(), textInstances_.size(), batches_.size());
        OutputDebugStringA(buf);
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
    batches_.clear();
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
    FlushGraphicsForCompute();

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
    auto rtvHandle = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
    rtvHandle.ptr += currentFrame_ * rtvDescriptorSize_;
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
    FlushGraphicsForCompute();

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

    auto rtvHandle = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
    rtvHandle.ptr += currentFrame_ * rtvDescriptorSize_;

    float clearColor[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
    cl->ClearRenderTargetView(rtvHandle, clearColor, 1, &clearRect);
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

    if (!EnsureSnapshotTexture()) {
        snapshotValid_ = false;
        return false;
    }

    // Flush pending draws so the back buffer is up to date
    FlushGraphicsForCompute();

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
    auto rtvHandle = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
    rtvHandle.ptr += currentFrame_ * rtvDescriptorSize_;
    cl->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    D3D12_VIEWPORT vp = { 0, 0, (float)viewportWidth_, (float)viewportHeight_, 0, 1 };
    D3D12_RECT scissor = { 0, 0, (LONG)viewportWidth_, (LONG)viewportHeight_ };
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);

    snapshotValid_ = true;
    snapshotUsedThisFrame_ = true;
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

        // Upload buffer
        UINT64 rowPitch = ((UINT64)w * 4 + D3D12_TEXTURE_DATA_PITCH_ALIGNMENT - 1) & ~(D3D12_TEXTURE_DATA_PITCH_ALIGNMENT - 1);
        UINT64 uploadSize = rowPitch * h;
        auto uploadHeap = MakeHeapProps(D3D12_HEAP_TYPE_UPLOAD);
        auto bufDesc = MakeBufferDesc(uploadSize);
        if (FAILED(device_->CreateCommittedResource(&uploadHeap, D3D12_HEAP_FLAG_NONE, &bufDesc,
                D3D12_RESOURCE_STATE_GENERIC_READ, nullptr, IID_PPV_ARGS(&desktopUploadBuffer_))))
            return;

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
// DrawGlowingBorderHighlight — approximated with SDF rects
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

    // Step 2: Animated glow spindle around the border
    float perimeter = 2.0f * (w + h);
    float trailLengthPx = perimeter * trailLength;
    float headPos = animationPhase * perimeter;

    // Draw the spindle as a series of glowing segments along the perimeter
    const int numSegments = 32;
    float maxWidth = strokeWidth * 2.5f;

    for (int i = 0; i < numSegments; ++i) {
        float t = (float)i / numSegments;
        float pos = headPos - t * trailLengthPx;
        if (pos < 0) pos += perimeter;
        pos = fmodf(pos, perimeter);

        // Convert perimeter position to (px, py)
        float px, py;
        if (pos < w) {
            px = x + pos; py = y;
        } else if (pos < w + h) {
            px = x + w; py = y + (pos - w);
        } else if (pos < 2 * w + h) {
            px = x + w - (pos - w - h); py = y + h;
        } else {
            px = x; py = y + h - (pos - 2 * w - h);
        }

        // Spindle width: sine taper
        float spindleFactor = sinf(3.14159f * t);
        float segWidth = maxWidth * spindleFactor;
        if (segWidth < 1.0f) continue;

        // Opacity decreases towards tail
        float segOpacity = 0.9f * (1.0f - t);

        SdfRectInstance seg = {};
        seg.posX = px - segWidth * 0.5f;
        seg.posY = py - segWidth * 0.5f;
        seg.sizeX = segWidth;
        seg.sizeY = segWidth;
        seg.fillR = glowR * segOpacity;
        seg.fillG = glowG * segOpacity;
        seg.fillB = glowB * segOpacity;
        seg.fillA = segOpacity;
        float cr = segWidth * 0.5f;
        seg.cornerTL = cr; seg.cornerTR = cr; seg.cornerBR = cr; seg.cornerBL = cr;
        seg.opacity = 1.0f;
        AddSdfRect(seg);
    }

    // Step 3: Static border outline
    SdfRectInstance border = {};
    border.posX = x; border.posY = y;
    border.sizeX = w; border.sizeY = h;
    border.borderR = glowR * 0.3f; border.borderG = glowG * 0.3f;
    border.borderB = glowB * 0.3f; border.borderA = 0.3f;
    border.borderWidth = 1.0f;
    border.opacity = 1.0f;
    AddSdfRect(border);
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

    // Spindle between from/to centers
    float fromCX = fromX + fromW * 0.5f, fromCY = fromY + fromH * 0.5f;
    float toCX = toX + toW * 0.5f, toCY = toY + toH * 0.5f;
    float headX = lerp(fromCX, toCX, headProgress);
    float headY = lerp(fromCY, toCY, headProgress);
    float tailX = lerp(fromCX, toCX, tailProgress);
    float tailY = lerp(fromCY, toCY, tailProgress);

    const int numPoints = 16;
    float maxWidth = strokeWidth * 2.5f;
    for (int i = 0; i <= numPoints; ++i) {
        float t = (float)i / numPoints;
        float px = lerp(tailX, headX, t);
        float py = lerp(tailY, headY, t);
        float spindleFactor = sinf(3.14159f * t);
        float sw = maxWidth * spindleFactor;
        if (sw < 1.0f) continue;
        float opacity = 0.9f * spindleFactor;

        SdfRectInstance seg = {};
        seg.posX = px - sw * 0.5f; seg.posY = py - sw * 0.5f;
        seg.sizeX = sw; seg.sizeY = sw;
        seg.fillR = glowR * opacity; seg.fillG = glowG * opacity;
        seg.fillB = glowB * opacity; seg.fillA = opacity;
        float cr = sw * 0.5f;
        seg.cornerTL = cr; seg.cornerTR = cr; seg.cornerBR = cr; seg.cornerBL = cr;
        seg.opacity = 1.0f;
        AddSdfRect(seg);
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
        char buf[256];
        sprintf_s(buf, "[BeginOffscreenCapture] FAIL precondition: inFrame=%d slot=%d w=%.1f h=%.1f\n",
            (int)inFrame_, slot, w, h);
        OutputDebugStringA(buf);
        return false;
    }
    if (inOffscreenCapture_) {
        OutputDebugStringA("[BeginOffscreenCapture] FAIL: already in offscreen capture\n");
        return false;
    }

    // w, h are in DIP.  Allocate pixels at the current DPI scale so content
    // renders at full resolution.
    UINT pw = (UINT)std::ceil(w * dpiScale_);
    UINT ph = (UINT)std::ceil(h * dpiScale_);
    if (pw == 0) pw = 1;
    if (ph == 0) ph = 1;

    // Flush pending draws
    FlushGraphicsForCompute();

    if (!EnsureOffscreenTargets(pw, ph)) {
        char buf[256];
        sprintf_s(buf, "[BeginOffscreenCapture] FAIL EnsureOffscreenTargets: pw=%u ph=%u offscreenW=%u offscreenH=%u usedThisFrame=%d\n",
            pw, ph, offscreenW_, offscreenH_, (int)offscreenResourcesUsedThisFrame_);
        OutputDebugStringA(buf);
        offscreenCaptureValid_[slot] = false;
        return false;
    }

    offscreenCaptureX_[slot] = x;
    offscreenCaptureY_[slot] = y;
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

    inOffscreenCapture_ = true;
    {
        char buf[256];
        sprintf_s(buf, "[BeginOffscreenCapture] OK: rects=%zu batches=%zu after setup\n",
            rectInstances_.size(), batches_.size());
        OutputDebugStringA(buf);
    }
    return true;
}

void D3D12DirectRenderer::EndOffscreenCapture(int slot)
{
    if (!inFrame_ || slot < 0 || slot > 1 || !inOffscreenCapture_) return;

    {
        char buf[512];
        sprintf_s(buf, "[EndOffscreenCapture] slot=%d rects=%zu text=%zu bitmaps=%zu triangles=%zu batches=%zu screenW=%.1f screenH=%.1f\n",
            slot, rectInstances_.size(), textInstances_.size(), bitmapInstances_.size(),
            triangleVertices_.size(), batches_.size(),
            currentFrameConstants_.screenWidth, currentFrameConstants_.screenHeight);
        OutputDebugStringA(buf);
    }

    // Pop the offset transform
    PopTransform();

    // Flush pending draws to the offscreen RT
    FlushGraphicsForCompute();
    {
        char buf[128];
        sprintf_s(buf, "[EndOffscreenCapture] AFTER FLUSH: rects=%zu batches=%zu\n",
            rectInstances_.size(), batches_.size());
        OutputDebugStringA(buf);
    }

    auto* cl = commandList_.Get();

    // Transition offscreen RT to pixel shader resource for later texture read
    auto barrier = MakeTransitionBarrier(offscreenRT_[slot].Get(),
        D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
    cl->ResourceBarrier(1, &barrier);
    offscreenRTState_[slot] = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;

    // Restore main render target
    auto rtvHandle = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
    rtvHandle.ptr += currentFrame_ * rtvDescriptorSize_;
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

    // Restore the scissor stack saved during BeginOffscreenCapture
    scissorStack_ = std::move(savedScissorStack_);
    savedScissorStack_ = {};

    offscreenCaptureValid_[slot] = true;
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
    // Compute UV range so we only sample the valid portion.
    UINT pw = (UINT)std::ceil(w * dpiScale_);
    UINT ph = (UINT)std::ceil(h * dpiScale_);
    if (pw == 0) pw = 1;
    if (ph == 0) ph = 1;
    float uvMaxX = (offscreenW_ > 0) ? (float)pw / (float)offscreenW_ : 1.0f;
    float uvMaxY = (offscreenH_ > 0) ? (float)ph / (float)offscreenH_ : 1.0f;

    AddBitmap(x, y, w, h, opacity, offscreenRT_[slot].Get(), swapChainFormat_, uvMaxX, uvMaxY);

    // Flush immediately so the offscreen texture is sampled now, before a
    // subsequent BeginOffscreenCapture can clear and reuse the same slot.
    FlushGraphicsForCompute();
}

void D3D12DirectRenderer::DrawOffscreenBitmapCropped(int slot,
    float x, float y, float w, float h,
    float uvOffsetX, float uvOffsetY, float opacity)
{
    if (!inFrame_ || slot < 0 || slot > 1 || !offscreenRT_[slot] || !offscreenCaptureValid_[slot]) return;
    if (w <= 0 || h <= 0) return;

    // Compute UV sub-region within the offscreen texture.
    // uvOffset is in DIP from the capture origin; convert to pixels then to UV.
    float uvMinXPx = uvOffsetX * dpiScale_;
    float uvMinYPx = uvOffsetY * dpiScale_;
    float uvMaxXPx = uvMinXPx + std::ceil(w * dpiScale_);
    float uvMaxYPx = uvMinYPx + std::ceil(h * dpiScale_);
    float uvMinXf = (offscreenW_ > 0) ? uvMinXPx / (float)offscreenW_ : 0.0f;
    float uvMinYf = (offscreenH_ > 0) ? uvMinYPx / (float)offscreenH_ : 0.0f;
    float uvMaxXf = (offscreenW_ > 0) ? uvMaxXPx / (float)offscreenW_ : 1.0f;
    float uvMaxYf = (offscreenH_ > 0) ? uvMaxYPx / (float)offscreenH_ : 1.0f;

    BitmapQuadInstance inst = {};
    inst.posX = x; inst.posY = y;
    inst.sizeX = w; inst.sizeY = h;
    inst.uvMinX = uvMinXf; inst.uvMinY = uvMinYf;
    inst.uvMaxX = uvMaxXf; inst.uvMaxY = uvMaxYf;
    inst.opacity = opacity * currentOpacity_;

    // Apply current transform (same as AddBitmap)
    const auto& t = transformStack_.top();
    float newX = inst.posX * t.m11 + inst.posY * t.m21 + t.dx;
    float newY = inst.posX * t.m12 + inst.posY * t.m22 + t.dy;
    inst.posX = newX;
    inst.posY = newY;
    float scaleX = std::sqrt(t.m11 * t.m11 + t.m12 * t.m12);
    float scaleY = std::sqrt(t.m21 * t.m21 + t.m22 * t.m22);
    inst.sizeX *= scaleX;
    inst.sizeY *= scaleY;

    DrawBatch batch;
    batch.type = DrawBatchType::Bitmap;
    batch.instanceOffset = (uint32_t)bitmapInstances_.size();
    batch.instanceCount = 1;
    batch.sortOrder = drawOrder_++;
    batch.hasScissor = !scissorStack_.empty();
    if (batch.hasScissor) batch.scissor = scissorStack_.top();

    BitmapBatchTexture tex;
    tex.batchIndex = (uint32_t)batches_.size();
    tex.textureResource = offscreenRT_[slot].Get();
    tex.format = swapChainFormat_;
    bitmapTextures_.push_back(tex);

    batches_.push_back(batch);
    bitmapInstances_.push_back(inst);

    FlushGraphicsForCompute();
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

    FlushGraphicsForCompute();

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
    if (cbOffset + cbSize > kInstanceBufferSize) {
        DrawOffscreenBitmap(slot, x, y, w, h, 1.0f);
        return;
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

    auto rtvHandle = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
    rtvHandle.ptr += currentFrame_ * rtvDescriptorSize_;
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
        !offscreenRT_[slot] || !offscreenCaptureValid_[slot])
        return false;
    if (radius <= 0) return true; // nothing to blur

    // Scale DIP radius to physical pixels for the compute shader
    float pixelRadius = radius * dpiScale_;

    // Use the allocated offscreen texture dimensions (in physical pixels).
    UINT regionW = offscreenW_;
    UINT regionH = offscreenH_;
    if (regionW == 0 || regionH == 0) return false;

    DXGI_FORMAT fmt = swapChainFormat_;
    if (!EnsureBlurTemps(regionW, regionH)) return false;
    blurTempsUsedThisFrame_ = true;

    // Flush pending graphics before compute operations
    FlushGraphicsForCompute();

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
    auto rtvHandle = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
    rtvHandle.ptr += currentFrame_ * rtvDescriptorSize_;
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
    FlushGraphicsForCompute();

    // Reserve per-call constant buffer space before mutating blur temp states.
    auto& fr = frames_[currentFrame_];
    constexpr size_t cbAligned = 256; // D3D12 CBV alignment
    size_t cbOffset = ((uploadBufferOffset_ + cbAligned - 1) / cbAligned) * cbAligned;
    if (cbOffset + cbAligned > kInstanceBufferSize) {
        DrawSnapshotBlurred(x, y, w, h, blurRadius, tintR, tintG, tintB, tintOpacity, cornerRadius);
        return;
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

    auto rtvHandle = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
    rtvHandle.ptr += currentFrame_ * rtvDescriptorSize_;
    cl->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    // Bind frame constants (b0)
    cl->SetGraphicsRootConstantBufferView(0, fr.constantsBuffer->GetGPUVirtualAddress());

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
    cl->SetGraphicsRootConstantBufferView(0, fr.constantsBuffer->GetGPUVirtualAddress());
    cl->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);
    cl->RSSetViewports(1, &vp);
    cl->RSSetScissorRects(1, &scissor);
}

} // namespace jalium
