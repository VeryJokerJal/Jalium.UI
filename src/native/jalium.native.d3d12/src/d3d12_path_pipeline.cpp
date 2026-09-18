// d3d12_path_pipeline.cpp
//
// Stencil-then-cover SVG path renderer for D3D12 with 8× MSAA scratch.
//
// Why MSAA on a scratch buffer rather than rendering into the back buffer
// directly? Three reasons:
//   1) The rest of the renderer (SDF rects, bitmap blits, glyph atlas) runs
//      at 1×. Bumping the back buffer to 8× MSAA inflates *every* draw —
//      DrawBitmap and DrawBackdropFilter especially blew the FPS budget on
//      that route the last time it was tried (see DirectRenderer "Phase 2
//      MSAA was rolled back" comment in BeginFrame).
//   2) Stencil-then-cover *requires* a depth-stencil view; the scratch RT
//      keeps that DSV isolated from any future change to back buffer state.
//   3) Path edge quality is the workload that benefits most from MSAA —
//      analytic AA elsewhere (SDF rects, glyph subpixel coverage) is
//      already sharp without it. Spending 8 samples on the 10% of frame
//      pixels that paths actually touch is a net win.
//
// Frame flow:
//   • First stencil-path batch in this frame:
//       – Lazy-create 8× MSAA color + 8× MSAA depth-stencil + 1× resolve
//         texture sized to the path-content extent. Grow-only until a viewport
//         resize establishes a new allocation boundary (high-water-mark):
//         a window-sized request reuses the buffers; an oversized element-effect
//         capture grows them once, then the common case keeps hitting the cache.
//       – Clear MSAA color (transparent) and stencil (0). RTV/DSV bound.
//   • Each stencil-path batch:
//       – Stencil pass (write stencil only) + cover pass (write color
//         with stencil-NotEqual-0, REPLACE→0 to reset stencil for next
//         path in the same frame).
//   • At the next non-path batch (or end of frame):
//       – ResolveSubresource MSAA color → 1× resolve texture.
//       – Switch root sig to resolveRootSig, descriptor heap on resolve
//         SRV, fullscreen triangle draws with premult-alpha blend onto
//         the back buffer.
//       – Restore main root sig + descriptors so the next batch picks up
//         where it left off.

#include "d3d12_direct_renderer.h"
#include "jalium_flatten.h"
#include "jalium_stencil_path.h"

#include <d3dcompiler.h>
#include <cstring>
#include <cstdlib>
#include <algorithm>

#pragma comment(lib, "d3dcompiler.lib")

namespace jalium {

// MSAA scratch formats. One definition, used by the PSOs, by the resources,
// and by the multisample-capability query — a sample count that is not
// supported for BOTH of these is not usable, and querying a different format
// than the one the resources use would resolve to a count that then fails
// CreateCommittedResource.
//   • R16G16B16A16_FLOAT keeps premultiplied intermediate values without
//     clamping; sRGB conversion happens at the resolve blit.
//   • D24_UNORM_S8_UINT — only the stencil half is used, depth is permanently
//     disabled.
static constexpr DXGI_FORMAT kPathMsaaColorFormat = DXGI_FORMAT_R16G16B16A16_FLOAT;
static constexpr DXGI_FORMAT kPathMsaaDsFormat    = DXGI_FORMAT_D24_UNORM_S8_UINT;

// Sample counts we are willing to run the path scratch at, highest first.
static constexpr UINT kPathMsaaLadder[] = { 16u, 8u, 4u, 2u, 1u };

namespace {
inline D3D12_RESOURCE_BARRIER MakeBarrier(ID3D12Resource* r,
    D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after) {
    D3D12_RESOURCE_BARRIER b = {};
    b.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    b.Transition.pResource = r;
    b.Transition.StateBefore = before;
    b.Transition.StateAfter  = after;
    b.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    return b;
}
}  // namespace

// ============================================================================
// Shaders
// ============================================================================
static const char* kStencilPathHLSL = R"(
cbuffer PathDraw : register(b1) {
    float4 transform0;     // m11 m12 m21 m22
    float4 transform1;     // dx  dy  _   _
    float4 drawColor;      // premultiplied RGBA
    float4 screenSize;     // w  h  invW invH
};

struct VSIn  { float2 pos : POSITION; };
struct VSOut { float4 svpos : SV_POSITION; };

VSOut VSMain(VSIn input) {
    float m11 = transform0.x;
    float m12 = transform0.y;
    float m21 = transform0.z;
    float m22 = transform0.w;
    float tdx = transform1.x;
    float tdy = transform1.y;

    float px = input.pos.x * m11 + input.pos.y * m21 + tdx;
    float py = input.pos.x * m12 + input.pos.y * m22 + tdy;

    // pixel → NDC ([-1,1], top-left origin → flip Y)
    float ndcX = px * (2.0 * screenSize.z) - 1.0;
    float ndcY = 1.0 - py * (2.0 * screenSize.w);

    VSOut o;
    o.svpos = float4(ndcX, ndcY, 0.0, 1.0);
    return o;
}

float4 PSMain() : SV_TARGET {
    return drawColor;
}
)";

// Fullscreen-triangle resolve shader: samples the 1× resolve texture and
// blends onto the back buffer. No vertex buffer needed — uses SV_VertexID
// trick to emit three verts spanning [-1,-1]..[3,-1]..[-1,3].
static const char* kPathResolveHLSL = R"(
Texture2D<float4> srcTex : register(t0);
SamplerState srcSampler : register(s0);

// uvScale.xy maps the fullscreen-triangle's [0,1] uv onto only the
// [0,contentW]×[0,contentH] sub-region of the resolve texture. The MSAA scratch
// and the resolve texture are grow-only (sized to the high-water-mark content
// extent) and can therefore be LARGER than this path run's content when an
// earlier oversized element-effect capture grew them; the scale samples just the
// used top-left corner so the blit stays 1:1 in texels. It is (1,1) whenever the
// texture is exactly content-sized — the common case — reproducing the original
// full-texture blit bit-for-bit.
cbuffer ResolveParams : register(b0) {
    float4 uvScale;  // .xy = contentSize / textureSize, .zw unused
};

struct VSOut { float4 svpos : SV_POSITION; float2 uv : TEXCOORD0; };

VSOut VSMain(uint vid : SV_VertexID) {
    // 0 → (-1,-1) / uv (0,1)
    // 1 → ( 3,-1) / uv (2,1)
    // 2 → (-1, 3) / uv (0,-1)
    float2 ndc;
    ndc.x = (vid == 1) ? 3.0 : -1.0;
    ndc.y = (vid == 2) ? 3.0 : -1.0;
    VSOut o;
    o.svpos = float4(ndc, 0.0, 1.0);
    o.uv = float2((ndc.x + 1.0) * 0.5, (1.0 - ndc.y) * 0.5) * uvScale.xy;
    return o;
}

float4 PSMain(VSOut input) : SV_TARGET {
    // Sample is already premultiplied — pass through; the blend state does
    // ONE + INV_SRC_ALPHA, so transparent path pixels (alpha 0) don't darken
    // the destination.
    return srcTex.Sample(srcSampler, input.uv);
}
)";

// ============================================================================
// Resource creation
// ============================================================================

bool D3D12DirectRenderer::EnsureStencilPathResources()
{
    if (stencilPathReady_) return true;
    if (stencilPathInitAttempted_) return false;

    stencilPathInitAttempted_ = true;
    if (CreateStencilPathResources()) return true;

    // Creation publishes shaders, root signatures, PSOs and heaps in stages.
    // Release any partial optional pipeline so a failed fallback does not retain
    // memory for the rest of the render target's lifetime.
    stencilPathRootSig_.Reset();
    psoStencilFillNonZero_.Reset();
    psoStencilFillEvenOdd_.Reset();
    psoStencilCover_.Reset();
    stencilPathVS_.Reset();
    stencilPathPS_.Reset();
    pathResolveRootSig_.Reset();
    psoPathResolve_.Reset();
    pathResolveVS_.Reset();
    pathResolvePS_.Reset();
    pathMsaaRtvHeap_.Reset();
    stencilDsvHeap_.Reset();
    stencilPathReady_ = false;
    OutputDebugStringA(
        "[D3D12DirectRenderer] Lazy stencil path init failed; using analytic fallback\n");
    return false;
}

bool D3D12DirectRenderer::CreateStencilPathResources()
{
    if (!device_) return false;

    // Optional startup override of the path MSAA quality, e.g.
    // JALIUM_PATH_MSAA=4 to halve path GPU cost on low-end adapters, or =16 for
    // maximum smoothness where the adapter offers it.
    // Runtime changes go through SetPathMsaaSampleCount; this only seeds the
    // initial value so the first PSO/RT build uses it.
    // _dupenv_s is the CRT-safe form (plain getenv trips C4996-as-error here).
    {
        char* env = nullptr;
        size_t envLen = 0;
        if (_dupenv_s(&env, &envLen, "JALIUM_PATH_MSAA") == 0 && env) {
            int requested = std::atoi(env);
            SetPathMsaaSampleCount(requested > 0 ? (uint32_t)requested : 8u);
            pathMsaaSampleCountRequest_ = pendingPathMsaaSampleCount_;
            free(env);
        }
    }

    // Resolve the request (default or env-seeded) against what this device
    // actually supports, so the first PSO/RT build already uses a valid count.
    pathMsaaSampleCount_ =
        ResolveSupportedPathMsaaSampleCount(pathMsaaSampleCountRequest_);

    if (!stencilPathCache_) {
        stencilPathCache_ = std::make_unique<StencilPathCache>(kStencilPathCacheCapacity);
    }

    UINT compileFlags = 0;
#if defined(_DEBUG)
    compileFlags |= D3DCOMPILE_DEBUG | D3DCOMPILE_SKIP_OPTIMIZATION;
#endif

    // ── Path stencil/cover shaders.
    {
        ComPtr<ID3DBlob> err;
        if (FAILED(D3DCompile(kStencilPathHLSL, std::strlen(kStencilPathHLSL),
                "stencil_path", nullptr, nullptr, "VSMain", "vs_5_0",
                compileFlags, 0, &stencilPathVS_, &err))) {
            if (err) OutputDebugStringA((const char*)err->GetBufferPointer());
            return false;
        }
        err.Reset();
        if (FAILED(D3DCompile(kStencilPathHLSL, std::strlen(kStencilPathHLSL),
                "stencil_path", nullptr, nullptr, "PSMain", "ps_5_0",
                compileFlags, 0, &stencilPathPS_, &err))) {
            if (err) OutputDebugStringA((const char*)err->GetBufferPointer());
            return false;
        }
    }

    // ── Resolve shaders.
    {
        ComPtr<ID3DBlob> err;
        if (FAILED(D3DCompile(kPathResolveHLSL, std::strlen(kPathResolveHLSL),
                "path_resolve", nullptr, nullptr, "VSMain", "vs_5_0",
                compileFlags, 0, &pathResolveVS_, &err))) {
            if (err) OutputDebugStringA((const char*)err->GetBufferPointer());
            return false;
        }
        err.Reset();
        if (FAILED(D3DCompile(kPathResolveHLSL, std::strlen(kPathResolveHLSL),
                "path_resolve", nullptr, nullptr, "PSMain", "ps_5_0",
                compileFlags, 0, &pathResolvePS_, &err))) {
            if (err) OutputDebugStringA((const char*)err->GetBufferPointer());
            return false;
        }
    }

    // ── Root signature for stencil/cover (b1 root constants only).
    {
        D3D12_ROOT_PARAMETER param = {};
        param.ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
        param.Constants.ShaderRegister = 1;
        param.Constants.RegisterSpace = 0;
        param.Constants.Num32BitValues = 16;
        param.ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

        D3D12_ROOT_SIGNATURE_DESC rsDesc = {};
        rsDesc.NumParameters = 1;
        rsDesc.pParameters = &param;
        rsDesc.Flags = D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;

        ComPtr<ID3DBlob> rsBlob, rsErr;
        if (FAILED(D3D12SerializeRootSignature(&rsDesc, D3D_ROOT_SIGNATURE_VERSION_1_0,
                                               &rsBlob, &rsErr))) {
            if (rsErr) OutputDebugStringA((const char*)rsErr->GetBufferPointer());
            return false;
        }
        if (FAILED(device_->CreateRootSignature(0,
                rsBlob->GetBufferPointer(), rsBlob->GetBufferSize(),
                IID_PPV_ARGS(&stencilPathRootSig_)))) {
            return false;
        }
    }

    // ── Root signature for resolve: SRV descriptor table (t0, pixel) + uv-scale
    //    root constants (b0, vertex) + static sampler (s0). Param order is kept
    //    table-first so the existing SetGraphicsRootDescriptorTable(0,…) call in
    //    exitPathMode is unchanged; the uv-scale (param 1) lets the resolve sample
    //    only the used sub-region of a grow-only resolve texture (see
    //    kPathResolveHLSL / EnsureStencilDepthBuffer).
    {
        D3D12_DESCRIPTOR_RANGE srvRange = {};
        srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
        srvRange.NumDescriptors = 1;
        srvRange.BaseShaderRegister = 0;
        srvRange.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

        D3D12_ROOT_PARAMETER params[2] = {};
        // param 0: SRV descriptor table (t0) — pixel shader samples the resolve.
        params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
        params[0].DescriptorTable.NumDescriptorRanges = 1;
        params[0].DescriptorTable.pDescriptorRanges = &srvRange;
        params[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;
        // param 1: uv-scale root constants (b0) — vertex shader scales the uv.
        params[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
        params[1].Constants.ShaderRegister = 0;
        params[1].Constants.RegisterSpace = 0;
        params[1].Constants.Num32BitValues = 4;  // float4 uvScale
        params[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_VERTEX;

        D3D12_STATIC_SAMPLER_DESC sampler = {};
        sampler.Filter = D3D12_FILTER_MIN_MAG_MIP_LINEAR;
        sampler.AddressU = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
        sampler.AddressV = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
        sampler.AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
        sampler.ShaderRegister = 0;
        sampler.ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;

        D3D12_ROOT_SIGNATURE_DESC rsDesc = {};
        rsDesc.NumParameters = 2;
        rsDesc.pParameters = params;
        rsDesc.NumStaticSamplers = 1;
        rsDesc.pStaticSamplers = &sampler;
        rsDesc.Flags = D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;

        ComPtr<ID3DBlob> rsBlob, rsErr;
        if (FAILED(D3D12SerializeRootSignature(&rsDesc, D3D_ROOT_SIGNATURE_VERSION_1_0,
                                               &rsBlob, &rsErr))) {
            if (rsErr) OutputDebugStringA((const char*)rsErr->GetBufferPointer());
            return false;
        }
        if (FAILED(device_->CreateRootSignature(0,
                rsBlob->GetBufferPointer(), rsBlob->GetBufferSize(),
                IID_PPV_ARGS(&pathResolveRootSig_)))) {
            return false;
        }
    }

    // ── Stencil/Cover PSOs — depend on the path MSAA sample count, so they
    //    live in a helper that can be re-run when the count changes at runtime.
    if (!RebuildStencilCoverPSOs()) return false;

    // Resolve PSO — 1× target = back buffer, no MSAA, premult alpha blend.
    {
        D3D12_GRAPHICS_PIPELINE_STATE_DESC rd = {};
        rd.pRootSignature = pathResolveRootSig_.Get();
        rd.VS = { pathResolveVS_->GetBufferPointer(), pathResolveVS_->GetBufferSize() };
        rd.PS = { pathResolvePS_->GetBufferPointer(), pathResolvePS_->GetBufferSize() };
        rd.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
        rd.SampleMask = UINT_MAX;
        rd.NumRenderTargets = 1;
        rd.RTVFormats[0] = swapChainFormat_;
        rd.SampleDesc.Count = 1;
        rd.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
        rd.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
        rd.RasterizerState.DepthClipEnable = FALSE;
        rd.DepthStencilState.DepthEnable = FALSE;
        rd.DepthStencilState.StencilEnable = FALSE;

        D3D12_BLEND_DESC bd = {};
        bd.RenderTarget[0].BlendEnable = TRUE;
        bd.RenderTarget[0].SrcBlend       = D3D12_BLEND_ONE;
        bd.RenderTarget[0].DestBlend      = D3D12_BLEND_INV_SRC_ALPHA;
        bd.RenderTarget[0].BlendOp        = D3D12_BLEND_OP_ADD;
        bd.RenderTarget[0].SrcBlendAlpha  = D3D12_BLEND_ONE;
        bd.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
        bd.RenderTarget[0].BlendOpAlpha   = D3D12_BLEND_OP_ADD;
        bd.RenderTarget[0].RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;
        rd.BlendState = bd;

        if (FAILED(device_->CreateGraphicsPipelineState(&rd,
                IID_PPV_ARGS(&psoPathResolve_)))) return false;
    }

    // RTV/DSV heaps for the MSAA scratch. The resolve SRV lives in srvHeap_
    // ring (allocated lazily in RecordDrawCommands).
    {
        D3D12_DESCRIPTOR_HEAP_DESC rh = {};
        rh.NumDescriptors = 1;
        rh.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        if (FAILED(device_->CreateDescriptorHeap(&rh, IID_PPV_ARGS(&pathMsaaRtvHeap_)))) return false;

        D3D12_DESCRIPTOR_HEAP_DESC dh = {};
        dh.NumDescriptors = 1;
        dh.Type = D3D12_DESCRIPTOR_HEAP_TYPE_DSV;
        if (FAILED(device_->CreateDescriptorHeap(&dh, IID_PPV_ARGS(&stencilDsvHeap_)))) return false;
    }

    stencilPathReady_ = true;
    return true;
}

bool D3D12DirectRenderer::RebuildStencilCoverPSOs()
{
    if (!device_ || !stencilPathRootSig_ || !stencilPathVS_ || !stencilPathPS_) {
        return false;
    }

    D3D12_INPUT_ELEMENT_DESC inputLayout[] = {
        { "POSITION", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 0,
          D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 },
    };

    D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
    psoDesc.pRootSignature = stencilPathRootSig_.Get();
    psoDesc.VS = { stencilPathVS_->GetBufferPointer(), stencilPathVS_->GetBufferSize() };
    psoDesc.PS = { stencilPathPS_->GetBufferPointer(), stencilPathPS_->GetBufferSize() };
    psoDesc.InputLayout = { inputLayout, _countof(inputLayout) };
    psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
    psoDesc.SampleMask = UINT_MAX;
    psoDesc.NumRenderTargets = 1;
    psoDesc.RTVFormats[0] = kPathMsaaColorFormat;
    psoDesc.SampleDesc.Count = pathMsaaSampleCount_;
    psoDesc.DSVFormat = kPathMsaaDsFormat;

    psoDesc.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
    psoDesc.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
    psoDesc.RasterizerState.DepthClipEnable = FALSE;
    psoDesc.RasterizerState.MultisampleEnable = TRUE;  // MSAA needs this on

    // Stencil PSOs (write stencil only).
    {
        D3D12_BLEND_DESC noColor = {};
        noColor.RenderTarget[0].RenderTargetWriteMask = 0;
        psoDesc.BlendState = noColor;

        D3D12_DEPTH_STENCIL_DESC dss = {};
        dss.DepthEnable = FALSE;
        dss.DepthWriteMask = D3D12_DEPTH_WRITE_MASK_ZERO;
        dss.StencilEnable = TRUE;
        dss.StencilReadMask = 0xFF;
        dss.StencilWriteMask = 0xFF;
        dss.FrontFace.StencilFailOp = D3D12_STENCIL_OP_KEEP;
        dss.FrontFace.StencilDepthFailOp = D3D12_STENCIL_OP_KEEP;
        dss.FrontFace.StencilFunc = D3D12_COMPARISON_FUNC_ALWAYS;

        dss.FrontFace.StencilPassOp = D3D12_STENCIL_OP_INVERT;
        dss.BackFace = dss.FrontFace;
        psoDesc.DepthStencilState = dss;
        if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc,
                IID_PPV_ARGS(&psoStencilFillEvenOdd_)))) return false;

        // NonZero winding accumulation MUST wrap (mod-256), never saturate:
        // fan triangles regularly decrement FIRST (contour direction decides
        // which faces rasterize back-facing). With _SAT a decrement at 0 sticks
        // to 0 and the winding count is destroyed — star wedges over-fill and
        // painter-order waves collapse to diagonals (parity harness). INCR/DECR
        // keep the count exact mod 256 (order- and sign-independent; the cover
        // pass tests stencil != 0 with ref 0 / mask 0xFF, so wrapped negative
        // counts still read "inside"), matching
        // docs/reference/pure_d3d12_path_renderer.h:1426 and the Vulkan side
        // ("_AND_WRAP keeps the count exact" in EnsureStencilCoverResources).
        dss.FrontFace.StencilPassOp = D3D12_STENCIL_OP_INCR;
        dss.BackFace = dss.FrontFace;
        dss.BackFace.StencilPassOp = D3D12_STENCIL_OP_DECR;
        psoDesc.DepthStencilState = dss;
        if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc,
                IID_PPV_ARGS(&psoStencilFillNonZero_)))) return false;
    }

    // Cover PSO (stencil != 0 → write color, REPLACE 0 to clear).
    {
        D3D12_BLEND_DESC blend = {};
        blend.RenderTarget[0].BlendEnable = TRUE;
        blend.RenderTarget[0].SrcBlend       = D3D12_BLEND_ONE;
        blend.RenderTarget[0].DestBlend      = D3D12_BLEND_INV_SRC_ALPHA;
        blend.RenderTarget[0].BlendOp        = D3D12_BLEND_OP_ADD;
        blend.RenderTarget[0].SrcBlendAlpha  = D3D12_BLEND_ONE;
        blend.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
        blend.RenderTarget[0].BlendOpAlpha   = D3D12_BLEND_OP_ADD;
        blend.RenderTarget[0].RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;
        psoDesc.BlendState = blend;

        D3D12_DEPTH_STENCIL_DESC dss = {};
        dss.DepthEnable = FALSE;
        dss.DepthWriteMask = D3D12_DEPTH_WRITE_MASK_ZERO;
        dss.StencilEnable = TRUE;
        dss.StencilReadMask = 0xFF;
        dss.StencilWriteMask = 0xFF;
        dss.FrontFace.StencilFailOp      = D3D12_STENCIL_OP_KEEP;
        dss.FrontFace.StencilDepthFailOp = D3D12_STENCIL_OP_KEEP;
        dss.FrontFace.StencilPassOp      = D3D12_STENCIL_OP_REPLACE;
        dss.FrontFace.StencilFunc        = D3D12_COMPARISON_FUNC_NOT_EQUAL;
        dss.BackFace = dss.FrontFace;
        psoDesc.DepthStencilState = dss;

        if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc,
                IID_PPV_ARGS(&psoStencilCover_)))) return false;
    }

    return true;
}

UINT D3D12DirectRenderer::ResolveSupportedPathMsaaSampleCount(UINT requested) const
{
    if (!device_ || requested <= 1u) return 1u;

    // D3D12 guarantees nothing above 1× for a given format: 16× in particular
    // is common on desktop NVIDIA but absent on plenty of integrated parts, and
    // an unsupported count does not fail loudly — CreateGraphicsPipelineState
    // and CreateCommittedResource just return E_INVALIDARG and the whole
    // stencil path silently disables itself. So ask, and walk down.
    for (UINT candidate : kPathMsaaLadder) {
        if (candidate > requested) continue;
        if (candidate == 1u) return 1u;

        bool ok = true;
        for (DXGI_FORMAT fmt : { kPathMsaaColorFormat, kPathMsaaDsFormat }) {
            D3D12_FEATURE_DATA_MULTISAMPLE_QUALITY_LEVELS q = {};
            q.Format = fmt;
            q.SampleCount = candidate;
            q.Flags = D3D12_MULTISAMPLE_QUALITY_LEVELS_FLAG_NONE;
            if (FAILED(device_->CheckFeatureSupport(
                    D3D12_FEATURE_MULTISAMPLE_QUALITY_LEVELS, &q, sizeof(q)))
                || q.NumQualityLevels == 0) {
                ok = false;
                break;
            }
        }
        if (ok) return candidate;
    }
    return 1u;
}

void D3D12DirectRenderer::ApplyPendingPathMsaaSampleCount()
{
    if (pendingPathMsaaSampleCount_ == pathMsaaSampleCountRequest_) return;

    const UINT resolved =
        ResolveSupportedPathMsaaSampleCount(pendingPathMsaaSampleCount_);
    // Record the request FIRST and unconditionally: it is what makes this
    // change "applied", including when the device resolves two different
    // requests (16 and 8 on 8×-only hardware) to the same effective count.
    pathMsaaSampleCountRequest_ = pendingPathMsaaSampleCount_;

    if (!stencilPathReady_) {
        // Pipeline not initialised yet — just latch the value so the first
        // CreateStencilPathResources picks it up.
        pathMsaaSampleCount_ = resolved;
        return;
    }
    if (resolved == pathMsaaSampleCount_) return;   // nothing to rebuild

    pathMsaaSampleCount_ = resolved;

    // Caller (BeginFrame) has already waited the per-frame fence, so the old
    // MSAA PSOs and scratch RT are guaranteed idle on the GPU.
    if (!RebuildStencilCoverPSOs()) {
        // PSO rebuild failed (extremely unlikely) — fall back to disabling the
        // stencil fast path so we don't bind a mismatched PSO to the new RT.
        stencilPathReady_ = false;
        return;
    }

    // Force EnsureStencilDepthBuffer to recreate the scratch color + depth at
    // the new sample count on the next path batch this frame.
    pathMsaaWidth_  = 0;
    pathMsaaHeight_ = 0;
}

void D3D12DirectRenderer::SetPathMsaaSampleCount(uint32_t sampleCount)
{
    // 0 = analytic-only: solid FillPath skips the MSAA stencil path entirely and
    // uses the active engine's analytic-coverage rasterizer (WPF/Skia-style AA),
    // which is much cheaper than 8× stencil-then-cover on weak GPUs. The MSAA
    // sample count is left untouched (still used by any non-solid path that falls
    // back to the stencil scratch), so toggling analytic off restores the old cost.
    pathAnalyticOnly_ = (sampleCount == 0);
    if (pathAnalyticOnly_) return;

    // Normalize to a power of two we build PSOs for. The device may not support
    // the normalized value — that is resolved (downward) against real caps in
    // ApplyPendingPathMsaaSampleCount, which owns the frame boundary where the
    // MSAA resources can safely be rebuilt. Normalizing here and resolving
    // there keeps this setter callable from any thread at any time.
    uint32_t normalized = (sampleCount >= 16) ? 16u
                        : (sampleCount >= 8)  ?  8u
                        : (sampleCount >= 4)  ?  4u
                        : (sampleCount >= 2)  ?  2u
                                              :  1u;
    pendingPathMsaaSampleCount_ = normalized;
}

bool D3D12DirectRenderer::EnsureStencilDepthBuffer(UINT width, UINT height)
{
    if (!stencilPathReady_) return false;

    // Grow-only between render-target resizes: the MSAA scratch + 1× resolve
    // are sized to the high-water-mark of every content extent requested since
    // the last viewport resize (normally the window viewport;
    // larger when a stencil path is rendered inside an oversized element-effect
    // capture — pw>viewportWidth_ or ph>viewportHeight_). A request that already
    // fits the current allocation reuses it, so a one-off oversized capture grows
    // the textures ONCE and the common window-sized path rendering keeps hitting
    // this cache with no per-frame churn. The resolve blit samples only the
    // [0,width]×[0,height] sub-region via a uv-scale (exitPathMode /
    // kPathResolveHLSL), so a texture larger than the content renders identically.
    if (pathMsaaColor_ && pathMsaaDepth_ && pathResolveTexture_
        && width <= pathMsaaWidth_ && height <= pathMsaaHeight_) {
        return true;
    }

    // [#921] Mid-frame growth is unsafe. FlushGraphicsForCompute records pending
    // draws into the STILL-OPEN command list without closing it, so an earlier
    // path batch this frame may already have bound the current pathMsaa* scratch
    // (a smaller, e.g. viewport-sized capture) into that open list. The Reset()
    // below would then free a resource the list still references → D3D12 #921
    // OBJECT_DELETED_WHILE_STILL_IN_USE at the eventual Close. Mirror the
    // offscreen/blur usedThisFrame_ guard: refuse to regrow once the scratch has
    // been used this frame; the caller renders into the existing scratch and the
    // resolve blit samples only the [0,w]×[0,h] sub-region, so an oversized path
    // is at worst clipped for one frame. The regrow then happens cleanly at the
    // next frame's first path (before any record), and the viewport-inclusive
    // base allocation below means the common ≤viewport case never needs a
    // mid-frame regrow at all.
    if (pathMsaaUsedThisFrame_) {
        return pathMsaaColor_ && pathMsaaDepth_ && pathResolveTexture_;
    }

    // Never shrink a dimension: allocate the max of the request and the current
    // size (a width-grows / height-shrinks request must not drop the height below
    // an earlier high-water-mark, or the two would ping-pong recreate). ALSO cover
    // the viewport so a small first path still allocates a viewport-sized scratch
    // and later viewport-extent path batches this frame reuse it without a
    // mid-frame regrow (which the guard above would otherwise have to refuse).
    const UINT allocW = (std::max)((std::max)(width,  pathMsaaWidth_),  viewportWidth_);
    const UINT allocH = (std::max)((std::max)(height, pathMsaaHeight_), viewportHeight_);

    // First use this frame (guard above) — the open list does not yet reference
    // these, and BeginFrame has already waited on the per-frame fence so any
    // previous frame's GPU work that referenced them has drained.
    pathMsaaColor_.Reset();
    pathMsaaDepth_.Reset();
    pathResolveTexture_.Reset();

    D3D12_HEAP_PROPERTIES hp = {};
    hp.Type = D3D12_HEAP_TYPE_DEFAULT;

    // Same two formats the PSOs and the multisample-capability query use.
    constexpr DXGI_FORMAT kColorFmt = kPathMsaaColorFormat;
    constexpr DXGI_FORMAT kDsFmt    = kPathMsaaDsFormat;

    // MSAA color (sample count is runtime-configurable).
    {
        D3D12_RESOURCE_DESC rd = {};
        rd.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        rd.Width  = allocW;
        rd.Height = allocH;
        rd.DepthOrArraySize = 1;
        rd.MipLevels = 1;
        rd.Format = kColorFmt;
        rd.SampleDesc.Count = pathMsaaSampleCount_;
        rd.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
        rd.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;

        D3D12_CLEAR_VALUE cv = {};
        cv.Format = kColorFmt;
        cv.Color[0] = 0.0f; cv.Color[1] = 0.0f; cv.Color[2] = 0.0f; cv.Color[3] = 0.0f;

        if (FAILED(device_->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE,
                &rd, D3D12_RESOURCE_STATE_RENDER_TARGET, &cv,
                IID_PPV_ARGS(&pathMsaaColor_)))) {
            return false;
        }
        pathMsaaColor_->SetName(L"JaliumPathMsaaColor");  // [JALIUM-921 diag]
        pathMsaaColorState_ = D3D12_RESOURCE_STATE_RENDER_TARGET;

        D3D12_RENDER_TARGET_VIEW_DESC rtvDesc = {};
        rtvDesc.Format = kColorFmt;
        rtvDesc.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2DMS;
        device_->CreateRenderTargetView(pathMsaaColor_.Get(), &rtvDesc,
            pathMsaaRtvHeap_->GetCPUDescriptorHandleForHeapStart());
    }

    // MSAA depth-stencil (we only use stencil — depth is permanently disabled).
    {
        D3D12_RESOURCE_DESC rd = {};
        rd.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        rd.Width  = allocW;
        rd.Height = allocH;
        rd.DepthOrArraySize = 1;
        rd.MipLevels = 1;
        rd.Format = kDsFmt;
        rd.SampleDesc.Count = pathMsaaSampleCount_;
        rd.Flags = D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
        rd.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;

        D3D12_CLEAR_VALUE cv = {};
        cv.Format = kDsFmt;
        cv.DepthStencil.Depth = 1.0f;
        cv.DepthStencil.Stencil = 0;

        if (FAILED(device_->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE,
                &rd, D3D12_RESOURCE_STATE_DEPTH_WRITE, &cv,
                IID_PPV_ARGS(&pathMsaaDepth_)))) {
            return false;
        }
        pathMsaaDepth_->SetName(L"JaliumPathMsaaDepth");  // [JALIUM-921 diag]

        D3D12_DEPTH_STENCIL_VIEW_DESC dsvDesc = {};
        dsvDesc.Format = kDsFmt;
        dsvDesc.ViewDimension = D3D12_DSV_DIMENSION_TEXTURE2DMS;
        device_->CreateDepthStencilView(pathMsaaDepth_.Get(), &dsvDesc,
            stencilDsvHeap_->GetCPUDescriptorHandleForHeapStart());
    }

    // 1× resolve target.
    {
        D3D12_RESOURCE_DESC rd = {};
        rd.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        rd.Width  = allocW;
        rd.Height = allocH;
        rd.DepthOrArraySize = 1;
        rd.MipLevels = 1;
        rd.Format = kColorFmt;
        rd.SampleDesc.Count = 1;
        rd.Flags = D3D12_RESOURCE_FLAG_NONE;
        rd.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;

        if (FAILED(device_->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE,
                &rd, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE, nullptr,
                IID_PPV_ARGS(&pathResolveTexture_)))) {
            return false;
        }
        pathResolveTexture_->SetName(L"JaliumPathResolve");  // [JALIUM-921 diag]
        pathResolveTexState_ = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    }

    pathMsaaWidth_  = allocW;
    pathMsaaHeight_ = allocH;
    return true;
}

// ============================================================================
// Public API: cache lookup + record path
// ============================================================================

std::shared_ptr<const StencilPathGeometry>
D3D12DirectRenderer::GetOrBuildStencilPathGeometry(
    float startX, float startY,
    const float* commands, uint32_t commandLength)
{
    if (!stencilPathCache_) {
        stencilPathCache_ = std::make_unique<StencilPathCache>(kStencilPathCacheCapacity);
    }

    Transform2D xform = GetCurrentTransform();
    float maxScale = MaxScaleFromMatrix(xform.m11, xform.m12, xform.m21, xform.m22);
    maxScale *= dpiScale_;
    uint32_t scaleBucket = ScaleBucketFromMaxScale(maxScale);

    uint64_t key = HashStencilPathInput(startX, startY,
        commands, commandLength, scaleBucket);

    if (auto hit = stencilPathCache_->FindAndTouch(key)) {
        return hit;
    }

    float tol = ComputePixelTolerance(maxScale);
    if (maxScale > 1e-3f) tol /= maxScale;

    auto built = BuildStencilPathGeometry(startX, startY,
        commands, commandLength, tol);
    stencilPathCache_->Insert(key, built);
    return built;
}

bool D3D12DirectRenderer::AddStencilPath(
    std::shared_ptr<const StencilPathGeometry> geom,
    float r, float g, float b, float a,
    int32_t fillRule)
{
    if (!inFrame_ || !EnsureStencilPathResources()) return false;
    if (!geom || geom->fillTriangles.empty() || geom->coverTriangles.empty()) {
        return true;
    }

    Transform2D xform = GetCurrentTransform();
    float opacity = currentOpacity_;
    float pr = r * a * opacity;
    float pg = g * a * opacity;
    float pb = b * a * opacity;
    float pa = a * opacity;
    if (pa <= 0.0f) return true;

    StencilPathDraw draw;
    draw.geom = std::move(geom);
    draw.m11 = xform.m11 * dpiScale_;
    draw.m12 = xform.m12 * dpiScale_;
    draw.m21 = xform.m21 * dpiScale_;
    draw.m22 = xform.m22 * dpiScale_;
    draw.dx  = xform.dx  * dpiScale_;
    draw.dy  = xform.dy  * dpiScale_;
    draw.r = pr; draw.g = pg; draw.b = pb; draw.a = pa;
    draw.fillRule = fillRule;

    uint32_t drawIndex = (uint32_t)stencilPathDraws_.size();
    stencilPathDraws_.push_back(std::move(draw));

    DrawBatch batch;
    batch.type = DrawBatchType::StencilPath;
    batch.instanceOffset = drawIndex;
    batch.instanceCount  = 1;
    batch.sortOrder = drawOrder_++;
    batch.hasScissor = !scissorStack_.empty();
    if (batch.hasScissor) batch.scissor = scissorStack_.top();
    ResolveRoundedClipForBatch(batch);
    batches_.push_back(batch);
    return true;
}

} // namespace jalium
