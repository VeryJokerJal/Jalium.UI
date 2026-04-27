#include "d3d12_brush_shader.h"

#include <d3dcompiler.h>
#include <string>
#include <sstream>

#pragma comment(lib, "d3dcompiler.lib")

namespace jalium {

namespace {

// -----------------------------------------------------------------------
// Shared fullscreen-triangle vertex shader.
//
// Emits a single triangle that covers all of NDC; pixel-space position
// of each fragment is passed through TEXCOORD0 so the brush PS can
// compute SDF to the stroke polyline in the same coordinate frame the
// managed side used to fill StrokePoints.
//
// ViewportSize (px) is read from BrushConstants (b0). Keeps the VS
// independent of the main-RT pipeline's frame constants — brush
// dispatches run against the ink-layer bitmap, which has its own size.
// -----------------------------------------------------------------------
constexpr const char* kBrushVs = R"__HLSL__(
cbuffer BrushConstants : register(b0)
{
    float4 StrokeColor;
    float  StrokeWidth;
    float  StrokeHeight;
    float  TimeSeconds;
    uint   RandomSeed;
    float2 BBoxMin;
    float2 BBoxMax;
    uint   PointCount;
    uint   TaperMode;
    uint   IgnorePressure;
    uint   FitToCurve;
    float2 ViewportSize;
    float2 Pad;
};

struct PsIn
{
    float4 svPos : SV_Position;
    float2 pxPos : TEXCOORD0;
};

PsIn main(uint vid : SV_VertexID)
{
    // Canonical fullscreen triangle:
    //   vid 0 -> (-1,-1), vid 1 -> (3,-1), vid 2 -> (-1,3)
    float2 ndc = float2(
        (vid == 1) ?  3.0f : -1.0f,
        (vid == 2) ?  3.0f : -1.0f);

    PsIn o;
    o.svPos = float4(ndc, 0.0f, 1.0f);
    // ndc.x in [-1,3] -> px.x in [0, 2*width]
    // ndc.y in [-1,3] -> px.y in [0, 2*height] (y is flipped)
    o.pxPos = float2((ndc.x + 1.0f) * 0.5f * ViewportSize.x,
                     (1.0f - ndc.y) * 0.5f * ViewportSize.y);
    return o;
}
)__HLSL__";

// -----------------------------------------------------------------------
// Shared brush-shader preamble.
//
// This is concatenated in front of every user-provided BrushMain HLSL
// body. Must stay byte-identical to the managed-side preamble
// (Jalium.UI.Controls/Ink/Shaders/BrushShaderPreamble.hlsl) so both
// reference docs describe the same ABI.
// -----------------------------------------------------------------------
constexpr const char* kBrushPsPreamble = R"__HLSL__(
cbuffer BrushConstants : register(b0)
{
    float4 StrokeColor;
    float  StrokeWidth;
    float  StrokeHeight;
    float  TimeSeconds;
    uint   RandomSeed;
    float2 BBoxMin;
    float2 BBoxMax;
    uint   PointCount;
    uint   TaperMode;
    uint   IgnorePressure;
    uint   FitToCurve;
    float2 ViewportSize;
    float2 Pad;
};

// Optional user cbuffer. A brush shader that needs custom parameters
// declares its own field layout here — e.g.
//     cbuffer UserParams : register(b1) { float4 P0; float4 P1; };
// The framework packs BrushShader.ExtraParameters in declaration order,
// padded to 16-byte (float4) slots, so the first parameter always lands
// at offset 0 regardless of its declared type.
// Callers that don't need extras simply omit the cbuffer and the
// framework passes 0 bytes for b1 (the PSO still declares the slot but
// no shader reads it).

struct StrokePoint
{
    float x;
    float y;
    float pressure;
    float pad;
};

StructuredBuffer<StrokePoint> StrokePoints : register(t0);

float Hash21(float2 p, uint extra)
{
    uint3 q = uint3(asuint(p.x), asuint(p.y), RandomSeed ^ extra);
    q = q * uint3(374761393u, 668265263u, 2246822519u);
    q = (q.x ^ q.y ^ q.z) * uint3(0x85ebca6bu, 0xc2b2ae35u, 0x27d4eb2fu);
    uint h = q.x ^ q.y ^ q.z;
    return (h & 0x00FFFFFFu) / float(0x01000000u);
}

float2 SdfSegment(float2 px, float2 a, float2 b)
{
    float2 pa = px - a;
    float2 ba = b - a;
    float lenSq = dot(ba, ba);
    float t = (lenSq > 1e-6) ? saturate(dot(pa, ba) / lenSq) : 0;
    return float2(length(pa - ba * t), t);
}

// Universal taper scale in [0, 1] at arc-param t.
//   TaperMode 0 (None)         → 1 (no change)
//   TaperMode 1 (TaperedStart) → 0 at t=0, 1 at t=1 (thin → thick)
//   TaperMode 2 (TaperedEnd)   → 1 at t=0, 0 at t=1 (thick → thin)
// Every brush shader that wants taper support multiplies its own half-
// width (or particle density, or color alpha, etc.) by this value. Keeps
// all taper logic in one place so the behaviour stays identical across
// brushes. Declared before SdfPolyline because the union-SDF path uses
// it to pick the dominating segment at self-intersections.
float TaperScale(float t)
{
    if (TaperMode == 1) return 1.0 - (1.0 - t) * (1.0 - t);
    if (TaperMode == 2) return 1.0 - t * t;
    return 1.0;
}

float2 SdfPolyline(float2 px)
{
    // Two-pass union-SDF. Self-intersecting strokes have the same pixel
    // close to multiple segments at once; picking the arc from the
    // globally-nearest segment makes arc (and therefore taper / pressure
    // half-width) flip between candidates on a per-pixel basis — the
    // stroke appears cut at every crossing.
    //
    // Instead, we score each segment by the coverage it *would* produce
    // at this pixel (halfWidth − distance + 0.5-px AA) and pick the
    // winning arc from the segment that paints the pixel most. The
    // returned bestDist is still the global min so the outer shader's
    // StrokeCoverage() formula keeps its AA falloff. In practice the
    // dominating segment's halfW minus the global-min dist over-estimates
    // coverage slightly at crossings — which reads as a small bulge at
    // the intersection instead of a visible break. Exactly what ink
    // strokes should do.

    // Pass 1: total arc length (needed to normalize each segment's arc).
    float totalLen = 0;
    [loop]
    for (uint i = 0; i + 1 < PointCount; ++i)
    {
        StrokePoint pa = StrokePoints[i];
        StrokePoint pb = StrokePoints[i + 1];
        totalLen += length(float2(pb.x - pa.x, pb.y - pa.y));
    }
    float invLen = (totalLen > 1e-6) ? (1.0 / totalLen) : 0.0;

    // Pass 2: score each segment; track global-min dist and coverage-
    // winning arc independently.
    float bestDist = 1e20;
    float bestArc  = 0;
    float bestCov  = -1;
    float accum    = 0;

    [loop]
    for (uint j = 0; j + 1 < PointCount; ++j)
    {
        StrokePoint pa = StrokePoints[j];
        StrokePoint pb = StrokePoints[j + 1];
        float2 a   = float2(pa.x, pa.y);
        float2 b   = float2(pb.x, pb.y);
        float  len = length(b - a);
        float2 r   = SdfSegment(px, a, b);
        float  arc = saturate((accum + r.y * len) * invLen);

        // Estimated coverage this segment produces. Mirrors HalfWidthAt
        // but inline — HalfWidthAt depends on arc, which is what we are
        // computing here. Pressure is sampled from the segment's two
        // endpoints (not the t-index lookup HalfWidthAt does) because
        // per-segment sampling is exact here.
        float halfWEst = StrokeWidth * 0.5;
        if (IgnorePressure == 0)
        {
            float p = lerp(pa.pressure, pb.pressure, r.y);
            halfWEst *= p;
        }
        halfWEst *= TaperScale(arc);
        float covEst = saturate(halfWEst - r.x + 0.5);

        if (covEst > bestCov)
        {
            bestCov = covEst;
            bestArc = arc;
        }
        bestDist = min(bestDist, r.x);
        accum   += len;
    }

    return float2(bestDist, bestArc);
}

float HalfWidthAt(float t)
{
    float radius = StrokeWidth * 0.5;
    if (IgnorePressure == 0 && PointCount >= 2)
    {
        float idxF = saturate(t) * (PointCount - 1);
        uint  idx0 = (uint)floor(idxF);
        uint  idx1 = min(idx0 + 1, PointCount - 1);
        float frac = idxF - idx0;
        float p    = lerp(StrokePoints[idx0].pressure, StrokePoints[idx1].pressure, frac);
        radius *= p;
    }
    // Taper scales radius fully to 0 at the tapered end. StrokeCoverage()
    // relies on a +0.5 AA term so a radius of 0 still resolves to a soft
    // 1-px fade — the taper is visible instead of getting clamped away.
    radius *= TaperScale(t);
    return max(radius, 0.0);
}

float StrokeCoverage(float sdf, float halfWidth)
{
    return saturate(halfWidth - sdf + 0.5);
}

struct PsIn
{
    float4 svPos : SV_Position;
    float2 pxPos : TEXCOORD0;
};
)__HLSL__";

// Wrapper appended after the user's BrushMain body — gives D3DCompile
// a real SV_Target entry point.
constexpr const char* kBrushPsEntry = R"__HLSL__(
float4 BrushPsMain(PsIn input) : SV_Target
{
    return BrushMain(input.pxPos);
}
)__HLSL__";

bool CompileHlsl(const char* source, size_t length, const char* entry,
                 const char* target, const char* debugName,
                 ComPtr<ID3DBlob>& outBlob)
{
    UINT flags = 0;
#ifdef _DEBUG
    flags = D3DCOMPILE_DEBUG | D3DCOMPILE_SKIP_OPTIMIZATION;
#else
    flags = D3DCOMPILE_OPTIMIZATION_LEVEL3;
#endif

    ComPtr<ID3DBlob> errors;
    HRESULT hr = D3DCompile(source, length, debugName,
                            nullptr, nullptr,
                            entry, target,
                            flags, 0,
                            outBlob.GetAddressOf(),
                            errors.GetAddressOf());
    if (FAILED(hr))
    {
        if (errors)
        {
            OutputDebugStringA("[Jalium brush shader] compile error: ");
            OutputDebugStringA((const char*)errors->GetBufferPointer());
            OutputDebugStringA("\n");
        }
        return false;
    }
    return true;
}

void ConfigureBlendState(D3D12_BLEND_DESC& blend, BrushBlendMode mode)
{
    // Base: default off.
    blend.AlphaToCoverageEnable  = FALSE;
    blend.IndependentBlendEnable = FALSE;
    auto& rt = blend.RenderTarget[0];
    rt = {};
    rt.BlendEnable           = TRUE;
    rt.LogicOpEnable         = FALSE;
    rt.RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;
    rt.LogicOp               = D3D12_LOGIC_OP_NOOP;

    switch (mode)
    {
    case BrushBlendMode::SourceOver:
        // Premultiplied source-over:
        //   C' = Csrc + Cdst * (1 - Asrc)
        //   A' = Asrc + Adst * (1 - Asrc)
        rt.SrcBlend        = D3D12_BLEND_ONE;
        rt.DestBlend       = D3D12_BLEND_INV_SRC_ALPHA;
        rt.BlendOp         = D3D12_BLEND_OP_ADD;
        rt.SrcBlendAlpha   = D3D12_BLEND_ONE;
        rt.DestBlendAlpha  = D3D12_BLEND_INV_SRC_ALPHA;
        rt.BlendOpAlpha    = D3D12_BLEND_OP_ADD;
        break;

    case BrushBlendMode::Additive:
        // Saturating additive — repeated passes deepen without overshooting
        // beyond 1.0 (the D3D12 pipeline clamps implicitly on UNORM targets).
        //   C' = Csrc + Cdst
        //   A' = Asrc + Adst
        rt.SrcBlend        = D3D12_BLEND_ONE;
        rt.DestBlend       = D3D12_BLEND_ONE;
        rt.BlendOp         = D3D12_BLEND_OP_ADD;
        rt.SrcBlendAlpha   = D3D12_BLEND_ONE;
        rt.DestBlendAlpha  = D3D12_BLEND_ONE;
        rt.BlendOpAlpha    = D3D12_BLEND_OP_ADD;
        break;

    case BrushBlendMode::Erase:
        // Eraser: subtract source alpha from destination alpha,
        // destination color scaled by (1 - src_alpha).
        //   C' = Cdst * (1 - Asrc)
        //   A' = Adst * (1 - Asrc)
        rt.SrcBlend        = D3D12_BLEND_ZERO;
        rt.DestBlend       = D3D12_BLEND_INV_SRC_ALPHA;
        rt.BlendOp         = D3D12_BLEND_OP_ADD;
        rt.SrcBlendAlpha   = D3D12_BLEND_ZERO;
        rt.DestBlendAlpha  = D3D12_BLEND_INV_SRC_ALPHA;
        rt.BlendOpAlpha    = D3D12_BLEND_OP_ADD;
        break;
    }
}

} // namespace

// ─── D3D12BrushShader ──────────────────────────────────────────────────

D3D12BrushShader::D3D12BrushShader(
    ComPtr<ID3DBlob> psBlob,
    ComPtr<ID3D12PipelineState> pso,
    BrushBlendMode blendMode,
    std::string shaderKey)
    : psBlob_(std::move(psBlob))
    , pso_(std::move(pso))
    , blendMode_(blendMode)
    , shaderKey_(std::move(shaderKey))
{
}

// ─── D3D12BrushShaderPipeline ──────────────────────────────────────────

D3D12BrushShaderPipeline::D3D12BrushShaderPipeline(ID3D12Device* device)
    : device_(device)
{
}

bool D3D12BrushShaderPipeline::Initialize()
{
    if (initialized_) return true;
    if (!device_) return false;

    // ─── Compile shared VS ──────────────────────────────────────────
    if (!CompileHlsl(kBrushVs, strlen(kBrushVs), "main", "vs_5_1",
                     "jalium_brush.vs.hlsl", vsBlob_))
    {
        return false;
    }

    // ─── Root signature ─────────────────────────────────────────────
    // [0] Root CBV b0 — BrushConstants (framework-filled, 96 bytes, VS+PS)
    // [1] Root CBV b1 — UserParams (optional, per-shader layout, PS only)
    // [2] Root SRV t0 — StrokePoints buffer (PS only)
    D3D12_ROOT_PARAMETER params[3] = {};
    params[0].ParameterType             = D3D12_ROOT_PARAMETER_TYPE_CBV;
    params[0].Descriptor.ShaderRegister = 0;
    params[0].Descriptor.RegisterSpace  = 0;
    params[0].ShaderVisibility          = D3D12_SHADER_VISIBILITY_ALL;

    params[1].ParameterType             = D3D12_ROOT_PARAMETER_TYPE_CBV;
    params[1].Descriptor.ShaderRegister = 1;
    params[1].Descriptor.RegisterSpace  = 0;
    params[1].ShaderVisibility          = D3D12_SHADER_VISIBILITY_PIXEL;

    params[2].ParameterType             = D3D12_ROOT_PARAMETER_TYPE_SRV;
    params[2].Descriptor.ShaderRegister = 0;
    params[2].Descriptor.RegisterSpace  = 0;
    params[2].ShaderVisibility          = D3D12_SHADER_VISIBILITY_PIXEL;

    D3D12_ROOT_SIGNATURE_DESC rsDesc = {};
    rsDesc.NumParameters = 3;
    rsDesc.pParameters   = params;
    rsDesc.Flags         = D3D12_ROOT_SIGNATURE_FLAG_NONE;

    ComPtr<ID3DBlob> sig, errors;
    HRESULT hr = D3D12SerializeRootSignature(
        &rsDesc, D3D_ROOT_SIGNATURE_VERSION_1_0,
        sig.GetAddressOf(), errors.GetAddressOf());
    if (FAILED(hr))
    {
        if (errors)
        {
            OutputDebugStringA("[Jalium brush shader] root sig error: ");
            OutputDebugStringA((const char*)errors->GetBufferPointer());
        }
        return false;
    }
    hr = device_->CreateRootSignature(
        0, sig->GetBufferPointer(), sig->GetBufferSize(),
        IID_PPV_ARGS(rootSig_.GetAddressOf()));
    if (FAILED(hr)) return false;

    initialized_ = true;
    return true;
}

std::unique_ptr<D3D12BrushShader> D3D12BrushShaderPipeline::CreateBrushShader(
    const char* shaderKey,
    const char* brushMainHlsl,
    BrushBlendMode blendMode)
{
    if (!Initialize()) return nullptr;
    if (!brushMainHlsl) return nullptr;

    // Assemble the final PS source: preamble + user BrushMain + entry.
    std::string psSource;
    psSource.reserve(strlen(kBrushPsPreamble) + strlen(brushMainHlsl) + strlen(kBrushPsEntry) + 4);
    psSource.append(kBrushPsPreamble);
    psSource.append("\n");
    psSource.append(brushMainHlsl);
    psSource.append("\n");
    psSource.append(kBrushPsEntry);

    ComPtr<ID3DBlob> psBlob;
    std::string debugName = "jalium_brush_";
    debugName += (shaderKey ? shaderKey : "anon");
    debugName += ".ps.hlsl";
    if (!CompileHlsl(psSource.c_str(), psSource.size(), "BrushPsMain", "ps_5_1",
                     debugName.c_str(), psBlob))
    {
        return nullptr;
    }

    // ─── PSO ────────────────────────────────────────────────────────
    D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
    psoDesc.pRootSignature = rootSig_.Get();
    psoDesc.VS = { vsBlob_->GetBufferPointer(), vsBlob_->GetBufferSize() };
    psoDesc.PS = { psBlob->GetBufferPointer(), psBlob->GetBufferSize() };

    // No vertex input — we read SV_VertexID only.
    psoDesc.InputLayout.NumElements        = 0;
    psoDesc.InputLayout.pInputElementDescs = nullptr;

    // Rasterizer: solid, no cull (triangle covers screen regardless of winding).
    D3D12_RASTERIZER_DESC rast = {};
    rast.FillMode              = D3D12_FILL_MODE_SOLID;
    rast.CullMode              = D3D12_CULL_MODE_NONE;
    rast.FrontCounterClockwise = FALSE;
    rast.DepthClipEnable       = TRUE;
    psoDesc.RasterizerState    = rast;

    // Depth / stencil disabled — ink-layer target has no depth attachment.
    D3D12_DEPTH_STENCIL_DESC ds = {};
    ds.DepthEnable   = FALSE;
    ds.StencilEnable = FALSE;
    psoDesc.DepthStencilState = ds;

    ConfigureBlendState(psoDesc.BlendState, blendMode);

    psoDesc.SampleMask            = UINT_MAX;
    psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
    psoDesc.NumRenderTargets      = 1;
    psoDesc.RTVFormats[0]         = DXGI_FORMAT_R8G8B8A8_UNORM;
    psoDesc.DSVFormat             = DXGI_FORMAT_UNKNOWN;
    psoDesc.SampleDesc.Count      = 1;
    psoDesc.SampleDesc.Quality    = 0;

    ComPtr<ID3D12PipelineState> pso;
    HRESULT hr = device_->CreateGraphicsPipelineState(
        &psoDesc, IID_PPV_ARGS(pso.GetAddressOf()));
    if (FAILED(hr))
    {
        OutputDebugStringA("[Jalium brush shader] CreateGraphicsPipelineState failed\n");
        return nullptr;
    }

    return std::make_unique<D3D12BrushShader>(
        std::move(psBlob),
        std::move(pso),
        blendMode,
        shaderKey ? shaderKey : "");
}

} // namespace jalium
