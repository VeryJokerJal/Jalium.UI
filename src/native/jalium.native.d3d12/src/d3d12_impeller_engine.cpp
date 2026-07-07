#include "d3d12_impeller_engine.h"
#include "jalium_scanline_rasterizer.h"   // PixelRect / RasterizePathToRects
#include "jalium_api.h"                   // JALIUM_API export macro
#include "jalium_path_stats.h"            // unified path telemetry (core dll)
#include "jalium_flatten.h"               // MaxScaleFromTransform / ScaleBucketFromMaxScale
#include <atomic>
#include <cstring>
#include <cmath>
#include <algorithm>
#include <chrono>
#include <d3dcompiler.h>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace jalium {

// TrigCache and IsConvexPolygon now live in jalium_impeller_shapes.h
// (cross-backend); the D3D12 engine consumes them through that header.

// TessellateConvexFan moved to jalium_impeller_shapes.h.
// All filled-circle / filled-ellipse / filled-round-rect / stroked-circle /
// round-cap-line shape generators moved to jalium_impeller_shapes.h —
// EncodeFillEllipse below now invokes the cross-backend template directly.

// (Old GenerateFilledCircleStrip / EllipseStrip / RoundRectStrip /
//  StrokedCircleStrip / RoundCapLineStrip implementations removed —
//  the templated versions in jalium_impeller_shapes.h are now used.)
#if 0
bool ImpellerD3D12Engine::GenerateFilledCircleStrip_DEAD_DELETE_ME(
    float cx, float cy, float radius,
    float r, float g, float b, float a,
    const EngineTransform& transform)
{
    float maxScale = std::max(
        std::sqrt(transform.m11 * transform.m11 + transform.m12 * transform.m12),
        std::sqrt(transform.m21 * transform.m21 + transform.m22 * transform.m22));
    size_t divisions = TrigCache::ComputeDivisions(maxScale * radius);
    const auto& trigs = trigCache_.Get(divisions);

    ImpellerDrawBatch batch;
    // 4 vertices per trig entry (2 quadrants × 2 points each), as triangle strip
    batch.vertices.reserve(trigs.size() * 4);

    // Quadrant 1+4 (left side): bottom-left and top-left
    for (auto& t : trigs) {
        float ox = t.cos * radius, oy = t.sin * radius;
        float px1 = cx - ox, py1 = cy + oy;
        float px2 = cx - ox, py2 = cy - oy;
        TransformPoint(px1, py1, transform);
        TransformPoint(px2, py2, transform);
        batch.vertices.push_back({ px1, py1, r, g, b, a });
        batch.vertices.push_back({ px2, py2, r, g, b, a });
    }

    // Quadrant 2+3 (right side): swap cos/sin for symmetric traversal
    for (auto& t : trigs) {
        float ox = t.sin * radius, oy = t.cos * radius;
        float px1 = cx + ox, py1 = cy + oy;
        float px2 = cx + ox, py2 = cy - oy;
        TransformPoint(px1, py1, transform);
        TransformPoint(px2, py2, transform);
        batch.vertices.push_back({ px1, py1, r, g, b, a });
        batch.vertices.push_back({ px2, py2, r, g, b, a });
    }

    // Triangle strip indices: sequential
    uint32_t vc = (uint32_t)batch.vertices.size();
    batch.indices.reserve((vc - 2) * 3);
    for (uint32_t i = 0; i + 2 < vc; ++i) {
        if (i % 2 == 0) {
            batch.indices.push_back(i);
            batch.indices.push_back(i + 1);
            batch.indices.push_back(i + 2);
        } else {
            batch.indices.push_back(i + 1);
            batch.indices.push_back(i);
            batch.indices.push_back(i + 2);
        }
    }

    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    return true;
}

bool ImpellerD3D12Engine::GenerateFilledEllipseStrip(
    float cx, float cy, float rx, float ry,
    float r, float g, float b, float a,
    const EngineTransform& transform)
{
    if (std::abs(rx - ry) < 0.01f) {
        return GenerateFilledCircleStrip(cx, cy, rx, r, g, b, a, transform);
    }

    float maxScale = std::max(
        std::sqrt(transform.m11 * transform.m11 + transform.m12 * transform.m12),
        std::sqrt(transform.m21 * transform.m21 + transform.m22 * transform.m22));
    size_t divisions = TrigCache::ComputeDivisions(maxScale * std::max(rx, ry));
    const auto& trigs = trigCache_.Get(divisions);

    ImpellerDrawBatch batch;
    batch.vertices.reserve(trigs.size() * 4);

    // Quadrant 1+4 (left)
    for (auto& t : trigs) {
        float ox = t.cos * rx, oy = t.sin * ry;
        float px1 = cx - ox, py1 = cy + oy;
        float px2 = cx - ox, py2 = cy - oy;
        TransformPoint(px1, py1, transform);
        TransformPoint(px2, py2, transform);
        batch.vertices.push_back({ px1, py1, r, g, b, a });
        batch.vertices.push_back({ px2, py2, r, g, b, a });
    }

    // Quadrant 2+3 (right): swap sin/cos and radii
    for (auto& t : trigs) {
        float ox = t.sin * rx, oy = t.cos * ry;
        float px1 = cx + ox, py1 = cy + oy;
        float px2 = cx + ox, py2 = cy - oy;
        TransformPoint(px1, py1, transform);
        TransformPoint(px2, py2, transform);
        batch.vertices.push_back({ px1, py1, r, g, b, a });
        batch.vertices.push_back({ px2, py2, r, g, b, a });
    }

    uint32_t vc = (uint32_t)batch.vertices.size();
    batch.indices.reserve((vc - 2) * 3);
    for (uint32_t i = 0; i + 2 < vc; ++i) {
        if (i % 2 == 0) {
            batch.indices.push_back(i);
            batch.indices.push_back(i + 1);
            batch.indices.push_back(i + 2);
        } else {
            batch.indices.push_back(i + 1);
            batch.indices.push_back(i);
            batch.indices.push_back(i + 2);
        }
    }

    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    return true;
}

bool ImpellerD3D12Engine::GenerateFilledRoundRectStrip(
    float x, float y, float w, float h, float rx, float ry,
    float r, float g, float b, float a,
    const EngineTransform& transform)
{
    // If corner radii fill the entire rect, use ellipse
    if (rx * 2 >= w && ry * 2 >= h) {
        return GenerateFilledEllipseStrip(x + w * 0.5f, y + h * 0.5f,
                                          w * 0.5f, h * 0.5f, r, g, b, a, transform);
    }

    float maxScale = std::max(
        std::sqrt(transform.m11 * transform.m11 + transform.m12 * transform.m12),
        std::sqrt(transform.m21 * transform.m21 + transform.m22 * transform.m22));
    size_t divisions = TrigCache::ComputeDivisions(maxScale * std::max(rx, ry));
    const auto& trigs = trigCache_.Get(divisions);

    float left = x + rx;
    float top = y + ry;
    float right = x + w - rx;
    float bottom = y + h - ry;

    ImpellerDrawBatch batch;
    batch.vertices.reserve(trigs.size() * 4);

    // Quadrant 1+4: top-left and bottom-left corners
    for (auto& t : trigs) {
        float ox = t.cos * rx, oy = t.sin * ry;
        float px1 = left - ox, py1 = bottom + oy;
        float px2 = left - ox, py2 = top - oy;
        TransformPoint(px1, py1, transform);
        TransformPoint(px2, py2, transform);
        batch.vertices.push_back({ px1, py1, r, g, b, a });
        batch.vertices.push_back({ px2, py2, r, g, b, a });
    }

    // Quadrant 2+3: top-right and bottom-right corners
    for (auto& t : trigs) {
        float ox = t.sin * rx, oy = t.cos * ry;
        float px1 = right + ox, py1 = bottom + oy;
        float px2 = right + ox, py2 = top - oy;
        TransformPoint(px1, py1, transform);
        TransformPoint(px2, py2, transform);
        batch.vertices.push_back({ px1, py1, r, g, b, a });
        batch.vertices.push_back({ px2, py2, r, g, b, a });
    }

    uint32_t vc = (uint32_t)batch.vertices.size();
    batch.indices.reserve((vc - 2) * 3);
    for (uint32_t i = 0; i + 2 < vc; ++i) {
        if (i % 2 == 0) {
            batch.indices.push_back(i);
            batch.indices.push_back(i + 1);
            batch.indices.push_back(i + 2);
        } else {
            batch.indices.push_back(i + 1);
            batch.indices.push_back(i);
            batch.indices.push_back(i + 2);
        }
    }

    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    return true;
}

// ============================================================================
// Stroked Circle (Flutter Impeller: GenerateStrokedCircle)
// Inner+outer ring as triangle strip, zig-zag between radii.
// ============================================================================

bool ImpellerD3D12Engine::GenerateStrokedCircleStrip(
    float cx, float cy, float radius, float strokeWidth,
    float r, float g, float b, float a,
    const EngineTransform& transform)
{
    float halfW = strokeWidth * 0.5f;
    if (halfW <= 0 || halfW >= radius) {
        // Degenerate: fill instead
        return GenerateFilledCircleStrip(cx, cy, radius, r, g, b, a, transform);
    }

    float outerR = radius + halfW;
    float innerR = radius - halfW;
    float maxScale = std::max(
        std::sqrt(transform.m11 * transform.m11 + transform.m12 * transform.m12),
        std::sqrt(transform.m21 * transform.m21 + transform.m22 * transform.m22));
    size_t divisions = TrigCache::ComputeDivisions(maxScale * outerR);
    const auto& trigs = trigCache_.Get(divisions);

    ImpellerDrawBatch batch;
    // 8 vertices per trig: 4 quadrants × (outer + inner)
    batch.vertices.reserve(trigs.size() * 8);

    // Generate 4 quadrants, each with outer+inner zig-zag
    auto emitQuadrant = [&](auto transformOuter, auto transformInner) {
        for (auto& t : trigs) {
            float ox, oy, ix, iy;
            transformOuter(t, outerR, ox, oy);
            transformInner(t, innerR, ix, iy);
            float pox = cx + ox, poy = cy + oy;
            float pix = cx + ix, piy = cy + iy;
            TransformPoint(pox, poy, transform);
            TransformPoint(pix, piy, transform);
            batch.vertices.push_back({ pox, poy, r, g, b, a });
            batch.vertices.push_back({ pix, piy, r, g, b, a });
        }
    };

    // Q1: top-left
    emitQuadrant(
        [](const Trig& t, float R, float& x, float& y) { x = -t.cos * R; y = -t.sin * R; },
        [](const Trig& t, float R, float& x, float& y) { x = -t.cos * R; y = -t.sin * R; });
    // Actually use the Flutter pattern: outer_radius vs inner_radius
    // Simpler approach: full circle with outer/inner interleaving
    batch.vertices.clear();

    uint32_t totalPoints = (uint32_t)(trigs.size() * 4); // full circle
    batch.vertices.reserve(totalPoints * 2);

    for (uint32_t q = 0; q < 4; ++q) {
        for (size_t i = 0; i < trigs.size(); ++i) {
            float tc = trigs[i].cos, ts = trigs[i].sin;
            float ox, oy;
            switch (q) {
                case 0: ox = -tc; oy = -ts; break; // Q1
                case 1: ox = ts; oy = -tc; break;  // Q2 (swap sin/cos)
                case 2: ox = tc; oy = ts; break;   // Q3
                case 3: ox = -ts; oy = tc; break;  // Q4
            }
            float pox = cx + ox * outerR, poy = cy + oy * outerR;
            float pix = cx + ox * innerR, piy = cy + oy * innerR;
            TransformPoint(pox, poy, transform);
            TransformPoint(pix, piy, transform);
            batch.vertices.push_back({ pox, poy, r, g, b, a });
            batch.vertices.push_back({ pix, piy, r, g, b, a });
        }
    }

    uint32_t vc = (uint32_t)batch.vertices.size();
    batch.indices.reserve((vc - 2) * 3);
    for (uint32_t i = 0; i + 2 < vc; ++i) {
        if (i % 2 == 0) { batch.indices.push_back(i); batch.indices.push_back(i + 1); batch.indices.push_back(i + 2); }
        else { batch.indices.push_back(i + 1); batch.indices.push_back(i); batch.indices.push_back(i + 2); }
    }
    // Close the ring: connect last pair to first pair
    if (vc >= 4) {
        batch.indices.push_back(vc - 2); batch.indices.push_back(vc - 1); batch.indices.push_back(0);
        batch.indices.push_back(vc - 1); batch.indices.push_back(1); batch.indices.push_back(0);
    }

    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    return true;
}

// ============================================================================
// RoundCapLine (Flutter Impeller: GenerateRoundCapLine)
// Thick line with hemicircle caps at both endpoints.
// ============================================================================

bool ImpellerD3D12Engine::GenerateRoundCapLineStrip(
    float x0, float y0, float x1, float y1, float radius,
    float r, float g, float b, float a,
    const EngineTransform& transform)
{
    float dx = x1 - x0, dy = y1 - y0;
    float len = std::sqrt(dx * dx + dy * dy);
    if (len < 1e-6f) {
        return GenerateFilledCircleStrip((x0 + x1) * 0.5f, (y0 + y1) * 0.5f,
                                         radius, r, g, b, a, transform);
    }

    // Along and across vectors, scaled to radius
    float ax = dx / len * radius, ay = dy / len * radius;
    float px = -ay, py = ax; // perpendicular

    float maxScale = std::max(
        std::sqrt(transform.m11 * transform.m11 + transform.m12 * transform.m12),
        std::sqrt(transform.m21 * transform.m21 + transform.m22 * transform.m22));
    size_t divisions = TrigCache::ComputeDivisions(maxScale * radius);
    const auto& trigs = trigCache_.Get(divisions);

    ImpellerDrawBatch batch;
    batch.vertices.reserve(trigs.size() * 4);

    // First half: hemicircle at p0 (going backwards)
    for (auto& t : trigs) {
        float relAlongX = ax * t.cos, relAlongY = ay * t.cos;
        float relAcrossX = px * t.sin, relAcrossY = py * t.sin;
        float v1x = x0 - relAlongX + relAcrossX, v1y = y0 - relAlongY + relAcrossY;
        float v2x = x0 - relAlongX - relAcrossX, v2y = y0 - relAlongY - relAcrossY;
        TransformPoint(v1x, v1y, transform);
        TransformPoint(v2x, v2y, transform);
        batch.vertices.push_back({ v1x, v1y, r, g, b, a });
        batch.vertices.push_back({ v2x, v2y, r, g, b, a });
    }

    // Second half: hemicircle at p1 (going forwards, swap sin/cos)
    for (auto& t : trigs) {
        float relAlongX = ax * t.sin, relAlongY = ay * t.sin;
        float relAcrossX = px * t.cos, relAcrossY = py * t.cos;
        float v1x = x1 + relAlongX + relAcrossX, v1y = y1 + relAlongY + relAcrossY;
        float v2x = x1 + relAlongX - relAcrossX, v2y = y1 + relAlongY - relAcrossY;
        TransformPoint(v1x, v1y, transform);
        TransformPoint(v2x, v2y, transform);
        batch.vertices.push_back({ v1x, v1y, r, g, b, a });
        batch.vertices.push_back({ v2x, v2y, r, g, b, a });
    }

    uint32_t vc = (uint32_t)batch.vertices.size();
    batch.indices.reserve((vc - 2) * 3);
    for (uint32_t i = 0; i + 2 < vc; ++i) {
        if (i % 2 == 0) { batch.indices.push_back(i); batch.indices.push_back(i + 1); batch.indices.push_back(i + 2); }
        else { batch.indices.push_back(i + 1); batch.indices.push_back(i); batch.indices.push_back(i + 2); }
    }

    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    return true;
}
#endif

// ============================================================================
// Gradient Fill (Linear/Radial/Sweep via vertex color interpolation)
// ============================================================================

bool ImpellerD3D12Engine::EncodeGradientFillPath(
    const std::vector<Contour>& contours,
    const EngineBrushData& brush,
    const EngineTransform& transform)
{
    if (!brush.stops || brush.stopCount == 0) return false;

    int32_t fr = 0; // even-odd default
    std::vector<float> triVerts;
    {
        path_stats::ScopedTriangulateTimer triTimer;
        bool ok = TriangulateCompoundPath(contours, fr, triVerts) && triVerts.size() >= 6;
        if (ok) triTimer.MarkOk();
        if (!ok) return false;
    }

    uint32_t vertCount = (uint32_t)(triVerts.size() / 2);
    ImpellerDrawBatch batch;
    batch.vertices.reserve(vertCount);
    batch.indices.reserve(vertCount);

    std::vector<float> stopData;
    FlattenGradientStops(brush, stopData);

    for (uint32_t i = 0; i < vertCount; ++i) {
        float px = triVerts[i * 2], py = triVerts[i * 2 + 1];

        // Sample gradient color in PATH space (gradient brush coords are in
        // path space) then transform vertex into pixel space.
        GradientColor gc = SampleBrushGradient(brush, stopData.data(), px, py);

        float vx = px, vy = py;
        TransformPoint(vx, vy, transform);
        batch.vertices.push_back({ vx, vy, gc.r * gc.a, gc.g * gc.a, gc.b * gc.a, gc.a });
        batch.indices.push_back(i);
    }

    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    return true;
}

// ComputeStrokeAlphaCoverage moved to jalium_impeller_shapes.h.

// ============================================================================
// ImpellerD3D12Engine — Impeller-style tessellation pipeline on D3D12
// ============================================================================

// Embedded HLSL shaders for Impeller solid fill pipeline
static const char* kImpellerSolidFillVS = R"hlsl(
cbuffer FrameConstants : register(b0) {
    float4x4 mvp;
};

struct VSInput {
    float2 position : POSITION;
    float4 color    : COLOR;
};

struct VSOutput {
    float4 position : SV_POSITION;
    float4 color    : COLOR;
};

VSOutput main(VSInput input) {
    VSOutput output;
    output.position = mul(mvp, float4(input.position, 0.0, 1.0));
    output.color = input.color;
    return output;
}
)hlsl";

static const char* kImpellerSolidFillPS = R"hlsl(
struct PSInput {
    float4 position : SV_POSITION;
    float4 color    : COLOR;
};

float4 main(PSInput input) : SV_TARGET {
    return input.color;
}
)hlsl";

// ============================================================================
// Construction / Destruction
// ============================================================================

ImpellerD3D12Engine::ImpellerD3D12Engine(ID3D12Device* device, DXGI_FORMAT rtvFormat)
    : device_(device), rtvFormat_(rtvFormat)
{
    // Transform-independent geometry cache (flatten + triangulate result keyed
    // by path data + fill rule + scale octave, NOT by the full transform). A
    // moving / scaled / rotated path hits this and only pays an O(N) per-frame
    // vertex transform instead of re-rasterizing every frame. Same type and
    // capacity Vulkan uses (vulkan_render_target.cpp kMaxPathCacheEntries).
    pathGeometryCache_ = std::make_unique<PathGeometryCache>(512);
}

ImpellerD3D12Engine::~ImpellerD3D12Engine() = default;

// ============================================================================
// Initialization
// ============================================================================

bool ImpellerD3D12Engine::Initialize() {
    if (initialized_) return true;

    if (!CreateRootSignature()) {
        return false;
    }
    if (!CreatePipelines()) {
        return false;
    }

    // Create RTV heap for output texture
    D3D12_DESCRIPTOR_HEAP_DESC rtvDesc = {};
    rtvDesc.NumDescriptors = 1;
    rtvDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
    if (FAILED(device_->CreateDescriptorHeap(&rtvDesc, IID_PPV_ARGS(&rtvHeap_)))) {
        return false;
    }

    initialized_ = true;
    return true;
}

bool ImpellerD3D12Engine::CreateRootSignature() {
    // Root parameter: CBV at b0 (4x4 MVP matrix)
    D3D12_ROOT_PARAMETER rootParam = {};
    rootParam.ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
    rootParam.Constants.ShaderRegister = 0;
    rootParam.Constants.RegisterSpace = 0;
    rootParam.Constants.Num32BitValues = 16; // 4x4 float matrix
    rootParam.ShaderVisibility = D3D12_SHADER_VISIBILITY_VERTEX;

    D3D12_ROOT_SIGNATURE_DESC rsDesc = {};
    rsDesc.NumParameters = 1;
    rsDesc.pParameters = &rootParam;
    rsDesc.Flags = D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;

    ComPtr<ID3DBlob> signature, error;
    HRESULT hr = D3D12SerializeRootSignature(&rsDesc, D3D_ROOT_SIGNATURE_VERSION_1,
                                              &signature, &error);
    if (FAILED(hr)) return false;

    hr = device_->CreateRootSignature(0, signature->GetBufferPointer(),
                                       signature->GetBufferSize(),
                                       IID_PPV_ARGS(&rootSignature_));
    return SUCCEEDED(hr);
}

bool ImpellerD3D12Engine::CreatePipelines() {
    // Compile shaders
    ComPtr<ID3DBlob> vsBlob, psBlob, errors;

    HRESULT hr = D3DCompile(kImpellerSolidFillVS, strlen(kImpellerSolidFillVS),
                             "ImpellerSolidFillVS", nullptr, nullptr, "main", "vs_5_0",
                             D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &vsBlob, &errors);
    if (FAILED(hr)) return false;

    hr = D3DCompile(kImpellerSolidFillPS, strlen(kImpellerSolidFillPS),
                     "ImpellerSolidFillPS", nullptr, nullptr, "main", "ps_5_0",
                     D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &psBlob, &errors);
    if (FAILED(hr)) return false;

    // Input layout: POSITION (float2) + COLOR (float4)
    D3D12_INPUT_ELEMENT_DESC inputElements[] = {
        { "POSITION", 0, DXGI_FORMAT_R32G32_FLOAT,    0, 0,  D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 },
        { "COLOR",    0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 8, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 },
    };

    D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
    psoDesc.pRootSignature = rootSignature_.Get();
    psoDesc.VS = { vsBlob->GetBufferPointer(), vsBlob->GetBufferSize() };
    psoDesc.PS = { psBlob->GetBufferPointer(), psBlob->GetBufferSize() };
    psoDesc.InputLayout = { inputElements, _countof(inputElements) };
    psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
    psoDesc.NumRenderTargets = 1;
    psoDesc.RTVFormats[0] = rtvFormat_;
    psoDesc.SampleDesc.Count = 1;
    psoDesc.SampleMask = UINT_MAX;
    psoDesc.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
    psoDesc.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
    psoDesc.RasterizerState.DepthClipEnable = TRUE;

    // Alpha blending: SrcAlpha, InvSrcAlpha (premultiplied alpha)
    psoDesc.BlendState.RenderTarget[0].BlendEnable = TRUE;
    psoDesc.BlendState.RenderTarget[0].SrcBlend = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlend = D3D12_BLEND_INV_SRC_ALPHA;
    psoDesc.BlendState.RenderTarget[0].BlendOp = D3D12_BLEND_OP_ADD;
    psoDesc.BlendState.RenderTarget[0].SrcBlendAlpha = D3D12_BLEND_ONE;
    psoDesc.BlendState.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
    psoDesc.BlendState.RenderTarget[0].BlendOpAlpha = D3D12_BLEND_OP_ADD;
    psoDesc.BlendState.RenderTarget[0].RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;

    hr = device_->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&solidFillPSO_));
    return SUCCEEDED(hr);
}

bool ImpellerD3D12Engine::EnsureOutputTexture(uint32_t w, uint32_t h) {
    if (outputTexture_ && outputW_ == w && outputH_ == h) return true;

    D3D12_RESOURCE_DESC texDesc = {};
    texDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    texDesc.Width = w;
    texDesc.Height = h;
    texDesc.DepthOrArraySize = 1;
    texDesc.MipLevels = 1;
    texDesc.Format = rtvFormat_;
    texDesc.SampleDesc.Count = 1;
    texDesc.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;

    D3D12_HEAP_PROPERTIES heapProps = {};
    heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;

    D3D12_CLEAR_VALUE clearVal = {};
    clearVal.Format = rtvFormat_;
    clearVal.Color[3] = 0.0f; // Transparent

    HRESULT hr = device_->CreateCommittedResource(
        &heapProps, D3D12_HEAP_FLAG_NONE, &texDesc,
        D3D12_RESOURCE_STATE_RENDER_TARGET, &clearVal,
        IID_PPV_ARGS(&outputTexture_));
    if (FAILED(hr)) return false;

    // Create RTV
    D3D12_RENDER_TARGET_VIEW_DESC rtvDesc = {};
    rtvDesc.Format = rtvFormat_;
    rtvDesc.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2D;
    device_->CreateRenderTargetView(outputTexture_.Get(), &rtvDesc,
                                     rtvHeap_->GetCPUDescriptorHandleForHeapStart());

    outputW_ = w;
    outputH_ = h;
    return true;
}

bool ImpellerD3D12Engine::EnsureVertexBuffer(size_t requiredBytes) {
    if (vertexBufferSize_ >= requiredBytes) return true;

    size_t newSize = std::max(requiredBytes, size_t(256 * 1024)); // Min 256KB

    // Upload buffer
    D3D12_HEAP_PROPERTIES uploadProps = {};
    uploadProps.Type = D3D12_HEAP_TYPE_UPLOAD;
    D3D12_RESOURCE_DESC bufDesc = {};
    bufDesc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    bufDesc.Width = newSize;
    bufDesc.Height = 1;
    bufDesc.DepthOrArraySize = 1;
    bufDesc.MipLevels = 1;
    bufDesc.SampleDesc.Count = 1;
    bufDesc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;

    HRESULT hr = device_->CreateCommittedResource(
        &uploadProps, D3D12_HEAP_FLAG_NONE, &bufDesc,
        D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
        IID_PPV_ARGS(&vertexUploadBuffer_));
    if (FAILED(hr)) return false;

    // GPU buffer
    D3D12_HEAP_PROPERTIES defaultProps = {};
    defaultProps.Type = D3D12_HEAP_TYPE_DEFAULT;
    hr = device_->CreateCommittedResource(
        &defaultProps, D3D12_HEAP_FLAG_NONE, &bufDesc,
        D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER, nullptr,
        IID_PPV_ARGS(&vertexBuffer_));
    if (FAILED(hr)) return false;

    vertexBufferSize_ = newSize;
    vertexUploadSize_ = newSize;
    return true;
}

bool ImpellerD3D12Engine::EnsureIndexBuffer(size_t requiredBytes) {
    if (indexBufferSize_ >= requiredBytes) return true;

    size_t newSize = std::max(requiredBytes, size_t(128 * 1024)); // Min 128KB

    D3D12_HEAP_PROPERTIES uploadProps = {};
    uploadProps.Type = D3D12_HEAP_TYPE_UPLOAD;
    D3D12_RESOURCE_DESC bufDesc = {};
    bufDesc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    bufDesc.Width = newSize;
    bufDesc.Height = 1;
    bufDesc.DepthOrArraySize = 1;
    bufDesc.MipLevels = 1;
    bufDesc.SampleDesc.Count = 1;
    bufDesc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;

    HRESULT hr = device_->CreateCommittedResource(
        &uploadProps, D3D12_HEAP_FLAG_NONE, &bufDesc,
        D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
        IID_PPV_ARGS(&indexUploadBuffer_));
    if (FAILED(hr)) return false;

    D3D12_HEAP_PROPERTIES defaultProps = {};
    defaultProps.Type = D3D12_HEAP_TYPE_DEFAULT;
    hr = device_->CreateCommittedResource(
        &defaultProps, D3D12_HEAP_FLAG_NONE, &bufDesc,
        D3D12_RESOURCE_STATE_INDEX_BUFFER, nullptr,
        IID_PPV_ARGS(&indexBuffer_));
    if (FAILED(hr)) return false;

    indexBufferSize_ = newSize;
    indexUploadSize_ = newSize;
    return true;
}

bool ImpellerD3D12Engine::EnsureStencilVertexBuffer(size_t requiredBytes) {
    if (stencilVertexUploadSize_ >= requiredBytes) return true;
    size_t newSize = std::max(requiredBytes, size_t(128 * 1024));
    D3D12_HEAP_PROPERTIES uploadProps = {};
    uploadProps.Type = D3D12_HEAP_TYPE_UPLOAD;
    D3D12_RESOURCE_DESC bufDesc = {};
    bufDesc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    bufDesc.Width = newSize;
    bufDesc.Height = 1;
    bufDesc.DepthOrArraySize = 1;
    bufDesc.MipLevels = 1;
    bufDesc.SampleDesc.Count = 1;
    bufDesc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    if (FAILED(device_->CreateCommittedResource(
            &uploadProps, D3D12_HEAP_FLAG_NONE, &bufDesc,
            D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
            IID_PPV_ARGS(&stencilVertexUploadBuffer_))))
        return false;
    stencilVertexUploadSize_ = newSize;
    return true;
}

bool ImpellerD3D12Engine::EnsureStencilIndexBuffer(size_t requiredBytes) {
    if (stencilIndexUploadSize_ >= requiredBytes) return true;
    size_t newSize = std::max(requiredBytes, size_t(64 * 1024));
    D3D12_HEAP_PROPERTIES uploadProps = {};
    uploadProps.Type = D3D12_HEAP_TYPE_UPLOAD;
    D3D12_RESOURCE_DESC bufDesc = {};
    bufDesc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    bufDesc.Width = newSize;
    bufDesc.Height = 1;
    bufDesc.DepthOrArraySize = 1;
    bufDesc.MipLevels = 1;
    bufDesc.SampleDesc.Count = 1;
    bufDesc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    if (FAILED(device_->CreateCommittedResource(
            &uploadProps, D3D12_HEAP_FLAG_NONE, &bufDesc,
            D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
            IID_PPV_ARGS(&stencilIndexUploadBuffer_))))
        return false;
    stencilIndexUploadSize_ = newSize;
    return true;
}

// ============================================================================
// Per-Frame Lifecycle
// ============================================================================

void ImpellerD3D12Engine::BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) {
    viewportW_ = viewportWidth;
    viewportH_ = viewportHeight;
    batches_.clear();
    encodedPathCount_ = 0;
    flatPoints_.clear();
}

void ImpellerD3D12Engine::SetScissorRect(float left, float top, float right, float bottom) {
    scissorLeft_ = left; scissorTop_ = top;
    scissorRight_ = right; scissorBottom_ = bottom;
    hasScissor_ = true;
}

void ImpellerD3D12Engine::ClearScissorRect() {
    hasScissor_ = false;
}

void ImpellerD3D12Engine::SetRoundedClip(const float rect[4], const float radii[4]) {
    roundedClipRect_[0] = rect[0]; roundedClipRect_[1] = rect[1];
    roundedClipRect_[2] = rect[2]; roundedClipRect_[3] = rect[3];
    roundedClipCornerRadii_[0] = radii[0]; roundedClipCornerRadii_[1] = radii[1];
    roundedClipCornerRadii_[2] = radii[2]; roundedClipCornerRadii_[3] = radii[3];
    hasRoundedClip_ = true;
}

void ImpellerD3D12Engine::ClearRoundedClip() {
    hasRoundedClip_ = false;
}

// ============================================================================
// Path Flattening (CPU) — Bezier → Line Segments
// ============================================================================

void ImpellerD3D12Engine::FlattenPath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineTransform& transform)
{
    flatPoints_.clear();

    float sx = startX, sy = startY;
    TransformPoint(sx, sy, transform);
    flatPoints_.push_back(sx);
    flatPoints_.push_back(sy);

    float curX = startX, curY = startY;
    uint32_t i = 0;

    while (i < commandLength) {
        float tag = commands[i];
        if (tag == 0.0f) {
            // LineTo: [0, x, y]
            if (i + 2 >= commandLength) break;
            float x = commands[i + 1], y = commands[i + 2];
            float tx = x, ty = y;
            TransformPoint(tx, ty, transform);
            flatPoints_.push_back(tx);
            flatPoints_.push_back(ty);
            curX = x; curY = y;
            i += 3;
        } else if (tag == 1.0f) {
            // BezierTo (cubic): [1, cp1x, cp1y, cp2x, cp2y, ex, ey]
            if (i + 6 >= commandLength) break;
            float cp1x = commands[i + 1], cp1y = commands[i + 2];
            float cp2x = commands[i + 3], cp2y = commands[i + 4];
            float ex = commands[i + 5], ey = commands[i + 6];

            // Transform all control points
            float tcx = curX, tcy = curY;
            TransformPoint(tcx, tcy, transform);
            float tcp1x = cp1x, tcp1y = cp1y;
            TransformPoint(tcp1x, tcp1y, transform);
            float tcp2x = cp2x, tcp2y = cp2y;
            TransformPoint(tcp2x, tcp2y, transform);
            float tex = ex, tey = ey;
            TransformPoint(tex, tey, transform);

            FlattenCubic(tcx, tcy, tcp1x, tcp1y, tcp2x, tcp2y, tex, tey, flattenTolerance_);

            curX = ex; curY = ey;
            i += 7;
        } else {
            // Unknown tag, skip
            i++;
        }
    }
}

void ImpellerD3D12Engine::FlattenCubic(
    float x0, float y0, float x1, float y1,
    float x2, float y2, float x3, float y3,
    float tolerance)
{
    // de Casteljau subdivision with Wang's formula for adaptive subdivision.
    // Wang's formula: N = ceil(sqrt(3/(4*tolerance) * max(|b2-2b1+b0|, |b3-2b2+b1|)))

    float dx1 = x2 - 2.0f * x1 + x0;
    float dy1 = y2 - 2.0f * y1 + y0;
    float dx2 = x3 - 2.0f * x2 + x1;
    float dy2 = y3 - 2.0f * y2 + y1;

    float mx = std::max(std::abs(dx1), std::abs(dx2));
    float my = std::max(std::abs(dy1), std::abs(dy2));
    float maxDev = std::sqrt(mx * mx + my * my);

    if (maxDev <= tolerance) {
        // Flat enough — just add the endpoint
        flatPoints_.push_back(x3);
        flatPoints_.push_back(y3);
        return;
    }

    // Wang's formula
    uint32_t n = (uint32_t)std::ceil(std::sqrt(3.0f / (4.0f * tolerance) * maxDev));
    n = std::min(n, 256u); // Safety cap

    float dt = 1.0f / (float)n;
    for (uint32_t i = 1; i <= n; ++i) {
        float t = dt * i;
        float t2 = t * t;
        float t3 = t2 * t;
        float mt = 1.0f - t;
        float mt2 = mt * mt;
        float mt3 = mt2 * mt;

        float px = mt3 * x0 + 3.0f * mt2 * t * x1 + 3.0f * mt * t2 * x2 + t3 * x3;
        float py = mt3 * y0 + 3.0f * mt2 * t * y1 + 3.0f * mt * t2 * y2 + t3 * y3;

        flatPoints_.push_back(px);
        flatPoints_.push_back(py);
    }
}

void ImpellerD3D12Engine::FlattenQuadratic(
    float x0, float y0, float x1, float y1,
    float x2, float y2, float tolerance)
{
    // Convert quadratic to cubic and flatten
    // Cubic cp1 = p0 + 2/3*(p1-p0), cp2 = p2 + 2/3*(p1-p2)
    float cp1x = x0 + 2.0f / 3.0f * (x1 - x0);
    float cp1y = y0 + 2.0f / 3.0f * (y1 - y0);
    float cp2x = x2 + 2.0f / 3.0f * (x1 - x2);
    float cp2y = y2 + 2.0f / 3.0f * (y1 - y2);

    FlattenCubic(x0, y0, cp1x, cp1y, cp2x, cp2y, x2, y2, tolerance);
}

// ============================================================================
// Tessellation (CPU) — Polygon → Triangles
// ============================================================================

bool ImpellerD3D12Engine::TessellateCurrentPath(const EngineBrushData& brush, FillRule fillRule) {
    uint32_t pointCount = (uint32_t)(flatPoints_.size() / 2);
    if (pointCount < 3) return false;

    std::vector<uint32_t> indices;
    {
        path_stats::ScopedTriangulateTimer triTimer;
        bool ok = TriangulatePolygon(flatPoints_.data(), pointCount, indices);
        if (ok) triTimer.MarkOk();
        if (!ok) return false;
    }

    if (indices.empty()) return false;

    // Premultiply alpha
    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    // Build vertex buffer
    ImpellerDrawBatch batch;
    batch.vertices.reserve(pointCount);
    for (uint32_t i = 0; i < pointCount; ++i) {
        ImpellerVertex v;
        v.x = flatPoints_[i * 2];
        v.y = flatPoints_[i * 2 + 1];
        v.r = r; v.g = g; v.b = b; v.a = a;
        batch.vertices.push_back(v);
    }
    batch.indices = std::move(indices);
    batch.pipelineType = 0; // solid fill

    PushBatch(std::move(batch));
    return true;
}

// ============================================================================
// Stroke Expansion (CPU, Impeller-style)
// ============================================================================

bool ImpellerD3D12Engine::ExpandStroke(
    const EngineBrushData& brush,
    float strokeWidth,
    ImpellerJoin join, float miterLimit,
    ImpellerCap cap, bool closed,
    std::vector<Contour>* collectContours)
{
    uint32_t pointCount = (uint32_t)(flatPoints_.size() / 2);
    if (pointCount < 2) return false;

    ImpellerDrawBatch batch;
    bool ok = jalium::ExpandStrokePath<ImpellerVertex>(
        batch.vertices, batch.indices,
        flatPoints_.data(), pointCount,
        strokeWidth, join, miterLimit, cap, closed,
        brush.r, brush.g, brush.b, brush.a,
        collectContours);
    if (!ok) return false;

    // Collect-mode wrote into collectContours, not batch — nothing more to push.
    if (collectContours) return true;

    if (batch.vertices.empty() || batch.indices.empty()) return true;
    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    return true;
}

#if 0
// Legacy inline ExpandStroke body — superseded by jalium::ExpandStrokePath in
// jalium_impeller_stroke.h. Retained under #if 0 so the diff trivially shows
// the original algorithm we forwarded to. Will be physically deleted in a
// follow-up cleanup commit; the templated header is the source of truth now.
bool ImpellerD3D12Engine::ExpandStroke_LEGACY(
    const EngineBrushData& brush,
    float strokeWidth,
    ImpellerJoin join, float miterLimit,
    ImpellerCap cap, bool closed,
    std::vector<Contour>* collectContours)
{
    uint32_t pointCount = (uint32_t)(flatPoints_.size() / 2);
    if (pointCount < 2) return false;

    float halfWidth = strokeWidth * 0.5f;

    // Premultiply alpha
    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    // Sub-pixel stroke handling:
    //   - Collect mode (going through the analytic AA rasterizer):
    //     keep the true geometric halfWidth so fractional coverage
    //     naturally tapers hairlines — no alpha hack needed.
    //   - Direct mode (legacy, binary GPU rasterization): clamp to
    //     0.5 and fade alpha by the lost coverage, otherwise very
    //     thin strokes pop in/out as the transform scales.
    if (!collectContours && halfWidth < 0.5f && halfWidth > 0.0f) {
        float fade = halfWidth / 0.5f;
        r *= fade;
        g *= fade;
        b *= fade;
        a *= fade;
        halfWidth = 0.5f;
    }

    ImpellerDrawBatch batch;
    auto& verts = batch.vertices;
    auto& indices = batch.indices;

    auto getX = [&](uint32_t i) { return flatPoints_[i * 2]; };
    auto getY = [&](uint32_t i) { return flatPoints_[i * 2 + 1]; };

    // Compute per-segment normals
    struct Segment { float nx, ny; };
    std::vector<Segment> segNormals;
    segNormals.reserve(pointCount - 1);
    for (uint32_t i = 0; i + 1 < pointCount; ++i) {
        float dx = getX(i + 1) - getX(i);
        float dy = getY(i + 1) - getY(i);
        float len = std::sqrt(dx * dx + dy * dy);
        if (len < 1e-6f) { segNormals.push_back({0, 0}); continue; }
        segNormals.push_back({ -dy / len, dx / len });
    }

    // ---- Build stroke geometry: one quad per segment ----
    //
    // The old code inserted a "sharp-angle bridging" fan at every
    // segment junction whose angle exceeded 10°, but the bridge's
    // trailing vertices were never connected to the NEXT segment's
    // quad start (it pushed fresh vertices), so at each bridge there
    // was a wedge-shaped gap relying on emitJoin to cover it. That
    // also double-covered most of the junction. With pixel-space
    // flattening (see EncodeStrokePath front matter) each curve is
    // now subdivided densely enough that every junction is below the
    // bridging threshold anyway, so dropping the bridge fan removes
    // a source of overdraw and gap artifacts. Corner coverage is now
    // fully delegated to emitJoin below (miter / bevel / round).
    for (uint32_t i = 0; i + 1 < pointCount; ++i) {
        float nx = segNormals[i].nx * halfWidth;
        float ny = segNormals[i].ny * halfWidth;
        float x0 = getX(i), y0 = getY(i);
        float x1 = getX(i + 1), y1 = getY(i + 1);

        uint32_t base = (uint32_t)verts.size();
        verts.push_back({ x0 + nx, y0 + ny, r, g, b, a });
        verts.push_back({ x0 - nx, y0 - ny, r, g, b, a });
        verts.push_back({ x1 + nx, y1 + ny, r, g, b, a });
        verts.push_back({ x1 - nx, y1 - ny, r, g, b, a });
        indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);
        indices.push_back(base + 1); indices.push_back(base + 3); indices.push_back(base + 2);
    }

    // ---- Joins between segments ----
    auto emitJoin = [&](float n0x, float n0y, float n1x, float n1y, float cx, float cy) {
        if (join == ImpellerJoin::Round) {
            GenerateRoundJoin(verts, indices, cx, cy, n0x, n0y, n1x, n1y, halfWidth, r, g, b, a);
        } else if (join == ImpellerJoin::Bevel) {
            // Outer bevel
            uint32_t base = (uint32_t)verts.size();
            verts.push_back({ cx, cy, r, g, b, a });
            verts.push_back({ cx + n0x * halfWidth, cy + n0y * halfWidth, r, g, b, a });
            verts.push_back({ cx + n1x * halfWidth, cy + n1y * halfWidth, r, g, b, a });
            indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);
            // Inner bevel
            base = (uint32_t)verts.size();
            verts.push_back({ cx, cy, r, g, b, a });
            verts.push_back({ cx - n0x * halfWidth, cy - n0y * halfWidth, r, g, b, a });
            verts.push_back({ cx - n1x * halfWidth, cy - n1y * halfWidth, r, g, b, a });
            indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);
        } else {
            // Miter join (with miter limit fallback to bevel)
            float dot = n0x * n1x + n0y * n1y;
            float alignment = (dot + 1.0f) * 0.5f;
            if (alignment > 0.999f) return; // Nearly straight, no join needed

            float cr = n0x * n1y - n0y * n1x;
            float dir = cr > 0 ? -1.0f : 1.0f;

            // Bevel base triangle
            uint32_t base = (uint32_t)verts.size();
            verts.push_back({ cx, cy, r, g, b, a });
            verts.push_back({ cx + n0x * halfWidth * dir, cy + n0y * halfWidth * dir, r, g, b, a });
            verts.push_back({ cx + n1x * halfWidth * dir, cy + n1y * halfWidth * dir, r, g, b, a });
            indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);

            // Miter extension (if within limit)
            if (alignment > 1e-6f) {
                float mx = (n0x + n1x) * 0.5f * halfWidth / alignment;
                float my = (n0y + n1y) * 0.5f * halfWidth / alignment;
                float miterDist2 = mx * mx + my * my;
                float miterLimitDist2 = miterLimit * miterLimit;
                if (miterDist2 <= miterLimitDist2) {
                    uint32_t mbase = (uint32_t)verts.size();
                    verts.push_back({ cx + mx * dir, cy + my * dir, r, g, b, a });
                    indices.push_back(base); indices.push_back(base + 2); indices.push_back(mbase);
                }
            }
        }
    };

    for (uint32_t i = 1; i + 1 < pointCount; ++i) {
        emitJoin(segNormals[i - 1].nx, segNormals[i - 1].ny,
                 segNormals[i].nx, segNormals[i].ny,
                 getX(i), getY(i));
    }

    // Closing join: for a closed contour, the start vertex (== end vertex)
    // sits between the last segment and the first segment, but the loop
    // above never visits it. Without this, the corner at the path's start
    // point shows a wedge-shaped gap — visible as the "notch" on the
    // top-left of stroked rectangles like the title-bar maximize icon.
    if (closed && pointCount >= 3 && segNormals.size() >= 2) {
        uint32_t lastSeg = (uint32_t)segNormals.size() - 1;
        emitJoin(segNormals[lastSeg].nx, segNormals[lastSeg].ny,
                 segNormals[0].nx, segNormals[0].ny,
                 getX(0), getY(0));
    }

    // ---- Caps ----
    if (!closed && pointCount >= 2) {
        // Start cap
        float nx = segNormals[0].nx, ny = segNormals[0].ny;
        float cx = getX(0), cy = getY(0);
        if (cap == ImpellerCap::Round) {
            GenerateRoundCap(verts, indices, cx, cy, nx, ny, halfWidth, r, g, b, a, true);
        } else if (cap == ImpellerCap::Square) {
            float dx = -segNormals[0].ny, dy = segNormals[0].nx;
            uint32_t base = (uint32_t)verts.size();
            verts.push_back({ cx + nx * halfWidth - dx * halfWidth, cy + ny * halfWidth - dy * halfWidth, r, g, b, a });
            verts.push_back({ cx - nx * halfWidth - dx * halfWidth, cy - ny * halfWidth - dy * halfWidth, r, g, b, a });
            verts.push_back({ cx + nx * halfWidth, cy + ny * halfWidth, r, g, b, a });
            verts.push_back({ cx - nx * halfWidth, cy - ny * halfWidth, r, g, b, a });
            indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);
            indices.push_back(base + 1); indices.push_back(base + 3); indices.push_back(base + 2);
        }

        // End cap
        uint32_t lastSeg = (uint32_t)segNormals.size() - 1;
        nx = segNormals[lastSeg].nx; ny = segNormals[lastSeg].ny;
        cx = getX(pointCount - 1); cy = getY(pointCount - 1);
        if (cap == ImpellerCap::Round) {
            GenerateRoundCap(verts, indices, cx, cy, nx, ny, halfWidth, r, g, b, a, false);
        } else if (cap == ImpellerCap::Square) {
            float dx = segNormals[lastSeg].ny, dy = -segNormals[lastSeg].nx;
            uint32_t base = (uint32_t)verts.size();
            verts.push_back({ cx + nx * halfWidth, cy + ny * halfWidth, r, g, b, a });
            verts.push_back({ cx - nx * halfWidth, cy - ny * halfWidth, r, g, b, a });
            verts.push_back({ cx + nx * halfWidth + dx * halfWidth, cy + ny * halfWidth + dy * halfWidth, r, g, b, a });
            verts.push_back({ cx - nx * halfWidth + dx * halfWidth, cy - ny * halfWidth + dy * halfWidth, r, g, b, a });
            indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);
            indices.push_back(base + 1); indices.push_back(base + 3); indices.push_back(base + 2);
        }
    }

    if (verts.empty() || indices.empty()) return true;

    if (collectContours) {
        // Convert every triangle in the stroke mesh into its own
        // 3-vertex contour. We force CCW winding (positive signed
        // area) so that when the whole set is fed to the NonZero
        // AA rasterizer every triangle contributes +1 inside its
        // interior — overlaps at joins / bridges simply sum to +2,
        // +3 ..., still "inside", which is exactly the union we
        // want for stroke-to-fill conversion. Without this winding
        // normalization a CW triangle at a join would subtract
        // coverage and carve a hole through the stroke.
        //
        // Color is discarded: the caller rasterizes these contours
        // with its own brush and uses the per-pixel analytic coverage
        // as the alpha, which is the whole point of routing strokes
        // through this path.
        for (size_t ti = 0; ti + 2 < indices.size(); ti += 3) {
            uint32_t i0 = indices[ti];
            uint32_t i1 = indices[ti + 1];
            uint32_t i2 = indices[ti + 2];
            if (i0 >= verts.size() || i1 >= verts.size() || i2 >= verts.size()) continue;
            float ax = verts[i0].x, ay = verts[i0].y;
            float bx = verts[i1].x, by = verts[i1].y;
            float cx = verts[i2].x, cy = verts[i2].y;
            float sa = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (std::abs(sa) < 1e-7f) continue; // degenerate
            Contour tri;
            tri.points.reserve(6);
            tri.points.push_back(ax); tri.points.push_back(ay);
            if (sa > 0.0f) {
                tri.points.push_back(bx); tri.points.push_back(by);
                tri.points.push_back(cx); tri.points.push_back(cy);
            } else {
                tri.points.push_back(cx); tri.points.push_back(cy);
                tri.points.push_back(bx); tri.points.push_back(by);
            }
            collectContours->push_back(std::move(tri));
        }
        return true;
    }

    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    return true;
}
#endif // legacy ExpandStroke

#if 0
// Legacy GenerateRoundCap / GenerateRoundJoin / ComputeQuadrantDivisions —
// all moved to jalium_impeller_stroke.h / jalium_impeller_shapes.h.
void ImpellerD3D12Engine::GenerateRoundCap_LEGACY(
    std::vector<ImpellerVertex>& verts,
    std::vector<uint32_t>& indices,
    float cx, float cy,
    float nx, float ny,
    float halfWidth,
    float r, float g, float b, float a,
    bool isStart)
{
    constexpr uint32_t kSegments = 8;
    constexpr float kPi = (float)M_PI;

    float angle0 = std::atan2(ny, nx);
    float startAngle = isStart ? (angle0 + kPi * 0.5f) : (angle0 - kPi * 0.5f);
    float sweep = isStart ? kPi : -kPi;

    uint32_t base = (uint32_t)verts.size();
    verts.push_back({ cx, cy, r, g, b, a }); // center

    for (uint32_t i = 0; i <= kSegments; ++i) {
        float t = (float)i / (float)kSegments;
        float angle = startAngle + sweep * t;
        verts.push_back({ cx + halfWidth * std::cos(angle),
                          cy + halfWidth * std::sin(angle), r, g, b, a });
    }

    for (uint32_t i = 0; i < kSegments; ++i) {
        indices.push_back(base);
        indices.push_back(base + 1 + i);
        indices.push_back(base + 2 + i);
    }
}

void ImpellerD3D12Engine::GenerateRoundJoin_LEGACY(
    std::vector<ImpellerVertex>& verts,
    std::vector<uint32_t>& indices,
    float cx, float cy,
    float n0x, float n0y,
    float n1x, float n1y,
    float halfWidth,
    float r, float g, float b, float a)
{
    // Round join fills only the OUTER side of a corner — the inner side
    // is already covered by the natural overlap of adjacent segment
    // quads, so drawing a full circle (as the old code did by emitting
    // BOTH an n0→n1 fan AND a −n0→−n1 fan) produces a visible bead at
    // every polyline vertex. Symptom: dense-flattened curves look like
    // a string of circles instead of a smooth stroke.
    //
    // Outer-side detection matches the miter-join convention in
    // emitJoin: cross(n0, n1) > 0 → outer is on the −n side; otherwise
    // outer is on the +n side. We rotate the arc to sweep across the
    // outer side and emit a single triangle fan from the corner point.
    float cr = n0x * n1y - n0y * n1x;
    // Nearly-collinear normals → no meaningful corner to fill.
    if (std::abs(cr) < 1e-5f) return;

    float sign = (cr > 0.0f) ? -1.0f : 1.0f;
    float a0x = n0x * sign, a0y = n0y * sign;
    float a1x = n1x * sign, a1y = n1y * sign;

    float angle0 = std::atan2(a0y, a0x);
    float angle1 = std::atan2(a1y, a1x);
    float diff = angle1 - angle0;
    while (diff >  (float)M_PI) diff -= 2.0f * (float)M_PI;
    while (diff < -(float)M_PI) diff += 2.0f * (float)M_PI;

    // Segment count proportional to angular span, roughly tracking the
    // round-cap tessellation density (8 segments per 180°).
    uint32_t segments = std::max(2u, (uint32_t)std::ceil(std::abs(diff) / (float)M_PI * 8.0f));

    uint32_t base = (uint32_t)verts.size();
    verts.push_back({ cx, cy, r, g, b, a });
    for (uint32_t i = 0; i <= segments; ++i) {
        float t = (float)i / (float)segments;
        float angle = angle0 + diff * t;
        verts.push_back({ cx + halfWidth * std::cos(angle),
                          cy + halfWidth * std::sin(angle), r, g, b, a });
    }
    for (uint32_t i = 0; i < segments; ++i) {
        indices.push_back(base);
        indices.push_back(base + 1 + i);
        indices.push_back(base + 2 + i);
    }
}

uint32_t ImpellerD3D12Engine::ComputeQuadrantDivisions_LEGACY(float pixelRadius) {
    constexpr float kCircleTolerance = 0.1f;
    if (pixelRadius <= 0.0f) return 1;
    float k = kCircleTolerance / pixelRadius;
    if (k >= 1.0f) return 1;
    float n = std::ceil((float)M_PI / 4.0f / std::acos(1.0f - k));
    return std::max(1u, std::min((uint32_t)n, 64u));
}
#endif // legacy GenerateRoundCap / GenerateRoundJoin / ComputeQuadrantDivisions

// PixelRect / RasterizePathToRects moved to jalium_scanline_rasterizer.h so
// the Vulkan Impeller engine shares the exact pixel output. The legacy
// in-place implementation is parked under `#if 0` below to keep the original
// algorithm visible in this commit's diff; it will be physically removed in
// a follow-up cleanup pass.
#if 0

namespace {

struct PixelRect_LEGACY { int x; int y; int w; int h; float alpha; };

// ----------------------------------------------------------------------------
// RasterizePathToRects — analytic anti-aliased scanline rasterizer
//
// Converts arbitrary contours (any number, any winding, any fill rule,
// concave / self-intersecting / with holes) into a list of axis-aligned
// rectangles whose per-rect alpha encodes the exact fractional coverage
// of the source path. This replaces the previous binary-coverage AET:
// the old output matched D3D's top-left rule pixel-for-pixel but was
// visibly jagged on curves and diagonal triangle edges (scrollbar
// arrows, tab corners, rounded icons).
//
// Algorithm: 4× vertical subpixel sampling with exact horizontal coverage.
//
//   1. All non-horizontal edges are collected into a flat list, each
//      storing its y range [yMin, yMax) and inverse slope (dxdy).
//   2. For each integer scanline row py ∈ [yStart, yEnd):
//        a. A coverage[] accumulator (one float per pixel column over
//           the path's x bbox) is zeroed.
//        b. Four sub-scanlines are evaluated at fy = py + 0.125, 0.375,
//           0.625, 0.875 — i.e. the center of each vertical quarter of
//           the pixel. Each sub-scanline:
//              - Finds every edge where fy ∈ [yMin, yMax) (half-open,
//                so a vertex touching the scanline is counted exactly
//                once, preventing parity flips).
//              - Computes the x crossing, sorts them, walks in/out
//                pairs under the selected fill rule, producing float
//                spans [fillFrom, fillTo) in pixel units.
//              - For each float span, distributes horizontal coverage
//                to every pixel column px the span touches:
//                    overlap = max(0, min(px+1, fillTo) - max(px, fillFrom))
//                    coverage[px - xOffset] += overlap * 0.25
//                The 0.25 factor is 1/kSub — each sub-scanline
//                represents a quarter of the pixel's vertical extent,
//                so four full-horizontal sub-scanlines sum to 1.0.
//        c. coverage[] now holds the exact fractional area the path
//           occupies in each pixel of this row (modulo the 4× vertical
//           quantization).
//        d. coverage[] is run-length encoded into (x, w, alpha) runs,
//           quantizing alpha to 8 bits so tiny float noise doesn't
//           split runs.
//   3. Consecutive scanlines whose RLE row layouts match (including
//      quantized alpha) are coalesced into a single taller rect. A
//      solid-interior rectangle thus becomes a handful of rects (top
//      and bottom edge rows plus one tall interior rect) instead of
//      one rect per scanline.
//
// Quality: 4× vertical × continuous horizontal gives roughly 32 unique
// coverage levels at edge pixels, which is visually indistinguishable
// from 8-bit AA on typical UI shapes. Straight horizontal/vertical
// edges remain perfectly sharp.
//
// Correctness notes:
//   - Half-open [yMin, yMax) on edges + half-open [fillFrom, fillTo)
//     on spans means a pixel center exactly on an edge is attributed
//     to exactly one side, never both (no double-cover seam darkening).
//   - The 4 sub-scanlines are sampled at (k+0.5)/4 offsets so the
//     overall coverage is symmetric: a horizontal edge landing on the
//     pixel's top or bottom boundary gives 0 or 1, not 0 or 1 modulo
//     bias.
//   - Path points exactly on an integer coordinate no longer drop
//     interior pixels on triangles (this was the "scrollbar arrow has
//     holes in the middle" bug under binary coverage — partial cover
//     at any nearby sub-row now carries the pixel).
// ----------------------------------------------------------------------------
struct RasterEdge {
    float yMin, yMax; // half-open [yMin, yMax)
    float xAtYMin;    // x coordinate at y = yMin
    float dxdy;       // dx per unit dy (inverse slope)
    int   dir;        // +1 if the edge goes down, -1 if it goes up
};

inline void RasterizePathToRects(
    const std::vector<Contour>& contours,
    FillRule rule,
    std::vector<PixelRect>& outRects)
{
    if (contours.empty()) return;

    std::vector<RasterEdge> edges;
    edges.reserve(64);

    float minY =  std::numeric_limits<float>::infinity();
    float maxY = -std::numeric_limits<float>::infinity();
    float minX =  std::numeric_limits<float>::infinity();
    float maxX = -std::numeric_limits<float>::infinity();

    auto addEdge = [&](float x0, float y0, float x1, float y1) {
        if (x0 < minX) minX = x0;
        if (x1 < minX) minX = x1;
        if (x0 > maxX) maxX = x0;
        if (x1 > maxX) maxX = x1;
        if (y0 < minY) minY = y0;
        if (y1 < minY) minY = y1;
        if (y0 > maxY) maxY = y0;
        if (y1 > maxY) maxY = y1;

        float dy = y1 - y0;
        if (std::abs(dy) < 1e-7f) return; // horizontal: no scanline crossings

        RasterEdge e;
        if (y0 < y1) {
            e.yMin = y0; e.yMax = y1;
            e.xAtYMin = x0;
            e.dxdy = (x1 - x0) / (y1 - y0);
            e.dir  = +1;
        } else {
            e.yMin = y1; e.yMax = y0;
            e.xAtYMin = x1;
            e.dxdy = (x0 - x1) / (y0 - y1);
            e.dir  = -1;
        }
        edges.push_back(e);
    };

    for (const auto& c : contours) {
        uint32_t n = c.VertexCount();
        if (n < 2) continue;
        for (uint32_t i = 0; i + 1 < n; ++i) {
            addEdge(c.X(i), c.Y(i), c.X(i + 1), c.Y(i + 1));
        }
        // Implicit close if the last vertex isn't already the first.
        if (n >= 3) {
            float fx0 = c.X(0),     fy0 = c.Y(0);
            float lx  = c.X(n - 1), ly  = c.Y(n - 1);
            if (std::abs(fx0 - lx) > 1e-6f || std::abs(fy0 - ly) > 1e-6f) {
                addEdge(lx, ly, fx0, fy0);
            }
        }
    }

    if (edges.empty()) return;

    // Pixel-column range for the coverage accumulator. One pixel of
    // padding on each side lets partial-coverage edges at the bounding
    // box extend into an adjacent column without special-casing.
    int pxX0 = (int)std::floor(minX) - 1;
    int pxX1 = (int)std::ceil (maxX) + 1;
    int pxWidth = pxX1 - pxX0;
    if (pxWidth <= 0) return;

    int yStart = (int)std::floor(minY);
    int yEnd   = (int)std::ceil (maxY);
    if (yEnd <= yStart) return;

    constexpr int   kSub     = 4;
    constexpr float kSubStep = 1.0f / (float)kSub;

    std::vector<float> coverage((size_t)pxWidth, 0.0f);
    std::vector<std::pair<float, int>> crossings;
    crossings.reserve(edges.size());

    // RLE row buffer and vertical coalescing state.
    struct RunSpan { int x; int w; uint8_t qAlpha; };
    std::vector<RunSpan> prevSpans;
    std::vector<RunSpan> curSpans;
    int  runStartY = 0;
    bool runOpen   = false;

    auto flushRun = [&](int yExclusive) {
        if (!runOpen) return;
        int h = yExclusive - runStartY;
        if (h > 0) {
            for (const auto& s : prevSpans) {
                outRects.push_back({
                    s.x, runStartY, s.w, h,
                    (float)s.qAlpha / 255.0f
                });
            }
        }
        runOpen = false;
        prevSpans.clear();
    };

    for (int py = yStart; py < yEnd; ++py) {
        std::fill(coverage.begin(), coverage.end(), 0.0f);

        for (int k = 0; k < kSub; ++k) {
            float fy = (float)py + ((float)k + 0.5f) * kSubStep;
            if (fy < minY || fy >= maxY) continue;

            crossings.clear();
            for (const auto& e : edges) {
                if (fy < e.yMin || fy >= e.yMax) continue;
                float x = e.xAtYMin + (fy - e.yMin) * e.dxdy;
                crossings.push_back({ x, e.dir });
            }
            if (crossings.empty()) continue;

            std::sort(crossings.begin(), crossings.end(),
                [](const std::pair<float,int>& a, const std::pair<float,int>& b) {
                    return a.first < b.first;
                });

            int   winding  = 0;
            bool  inside   = false;
            float fillFrom = 0.0f;
            for (const auto& cr : crossings) {
                bool was = inside;
                if (rule == FillRule::NonZero) {
                    winding += cr.second;
                    inside   = (winding != 0);
                } else {
                    winding ^= 1;
                    inside   = (winding != 0);
                }
                if (!was && inside) {
                    fillFrom = cr.first;
                } else if (was && !inside) {
                    float fillTo = cr.first;
                    if (fillTo <= fillFrom) continue;

                    int pxA = (int)std::floor(fillFrom) - pxX0;
                    int pxB = (int)std::ceil (fillTo)   - pxX0;
                    if (pxA < 0) pxA = 0;
                    if (pxB > pxWidth) pxB = pxWidth;

                    for (int px = pxA; px < pxB; ++px) {
                        float pxLeft  = (float)(px + pxX0);
                        float pxRight = pxLeft + 1.0f;
                        float l = pxLeft  > fillFrom ? pxLeft  : fillFrom;
                        float r = pxRight < fillTo   ? pxRight : fillTo;
                        if (r > l) {
                            coverage[(size_t)px] += (r - l) * kSubStep;
                        }
                    }
                }
            }
        }

        // RLE the coverage row into runs of identical quantized alpha.
        curSpans.clear();
        {
            int px = 0;
            while (px < pxWidth) {
                float c0 = coverage[(size_t)px];
                if (c0 > 1.0f) c0 = 1.0f;
                int q0 = (int)(c0 * 255.0f + 0.5f);
                if (q0 <= 0) { ++px; continue; }

                int runEnd = px + 1;
                while (runEnd < pxWidth) {
                    float c1 = coverage[(size_t)runEnd];
                    if (c1 > 1.0f) c1 = 1.0f;
                    int q1 = (int)(c1 * 255.0f + 0.5f);
                    if (q1 != q0) break;
                    ++runEnd;
                }

                curSpans.push_back({
                    px + pxX0,
                    runEnd - px,
                    (uint8_t)q0
                });
                px = runEnd;
            }
        }

        // Vertical coalescing vs the currently-open run.
        bool same =
            runOpen &&
            curSpans.size() == prevSpans.size() &&
            std::equal(curSpans.begin(), curSpans.end(), prevSpans.begin(),
                [](const RunSpan& a, const RunSpan& b) {
                    return a.x == b.x && a.w == b.w && a.qAlpha == b.qAlpha;
                });

        if (!same) {
            flushRun(py);
            if (!curSpans.empty()) {
                prevSpans = curSpans;
                runStartY = py;
                runOpen   = true;
            }
        }
    }

    flushRun(yEnd);
}

} // namespace
#endif // legacy in-place RasterizePathToRects (now in jalium_scanline_rasterizer.h)

// ============================================================================
// Path Encoding Entry Points
// ============================================================================

namespace {
// Self-intersection test for a closed contour. Ear-clip triangulation assumes a
// SIMPLE polygon and ignores the fill rule; a self-crossing outline (a pentagram
// or figure-eight authored as one figure) would fill wrong under EvenOdd/NonZero.
// EncodeFillPath uses this to keep such contours off the triangulation fast path
// and on the analytic scanline rasterizer instead. Only PROPER crossings count
// (strict opposite-sides on both segments), so shared vertices of adjacent edges
// never register — no false positives on simple polygons, hence no needless
// slow-path routing for ordinary icons. O(n²), computed once per unique geometry
// (the result is cached via CachedPathGeometry), on small icon-sized contours.
inline float SelfXCross(float ax, float ay, float bx, float by, float cx, float cy) {
    return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);  // (b-a) × (c-a)
}
inline bool SegmentsProperlyCross(float ax, float ay, float bx, float by,
                                  float cx, float cy, float dx, float dy) {
    float d1 = SelfXCross(ax, ay, bx, by, cx, cy);
    float d2 = SelfXCross(ax, ay, bx, by, dx, dy);
    float d3 = SelfXCross(cx, cy, dx, dy, ax, ay);
    float d4 = SelfXCross(cx, cy, dx, dy, bx, by);
    return ((d1 > 0.0f && d2 < 0.0f) || (d1 < 0.0f && d2 > 0.0f)) &&
           ((d3 > 0.0f && d4 < 0.0f) || (d3 < 0.0f && d4 > 0.0f));
}
inline bool ContourSelfIntersects(const Contour& c) {
    const uint32_t n = c.VertexCount();
    if (n < 4) return false;  // a triangle cannot self-intersect
    for (uint32_t i = 0; i < n; ++i) {
        const uint32_t i1 = (i + 1) % n;
        const float ax = c.X(i), ay = c.Y(i), bx = c.X(i1), by = c.Y(i1);
        for (uint32_t j = i + 2; j < n; ++j) {
            if (i == 0 && j == n - 1) continue;  // edges e0 and e(n-1) share vertex v0
            const uint32_t j1 = (j + 1) % n;
            if (SegmentsProperlyCross(ax, ay, bx, by,
                                      c.X(j), c.Y(j), c.X(j1), c.Y(j1)))
                return true;
        }
    }
    return false;
}
}  // namespace

// ============================================================================
// EncodeFillPath — transform-independent local-space geometry cache.
//
// THE fix for "Geometry drawing is laggy under animation/scroll/zoom". The
// legacy pipeline (now EncodeFillPathScanline, kept verbatim as the fallback)
// transforms commands to PIXEL space, flattens + scanline-rasterizes, and
// caches the resulting PixelRect list keyed by the FULL transform matrix. Any
// scale/rotation change ⇒ cache miss ⇒ the whole O(W·H·edges) rasterizer
// re-runs every frame for every visible path. WPF/WinUI3 (Direct2D) instead
// tessellate ONCE in geometry-local space and let the GPU apply the transform.
//
// This mirrors VulkanRenderTarget::FillPath (vulkan_render_target.cpp:8059):
// flatten + triangulate once in LOCAL space, cache keyed by (startX, startY,
// commands, fillRule, scaleBucket) — translation & rotation are NOT in the
// key — then each frame only transform the cached vertices (O(N)). Edge AA on
// our non-MSAA solid-fill target is a constant-width feather ring built per
// frame from the cached boundary contours (the same vertex-feather technique
// the binary-mesh stroke path documents).
//
// Self-intersecting / multi-subpath outlines that TriangulateCompoundPath
// can't handle fall through to EncodeFillPathScanline unchanged — those are
// rare and usually static, so correctness wins there over transform-free
// caching (exactly Vulkan's triangulationSucceeded ? fast : fallback split).
// ============================================================================
bool ImpellerD3D12Engine::EncodeFillPath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transformIn,
    int32_t edgeMode)
{
    // Gradient brushes keep the legacy source-space sampler path (the gradient
    // is sampled in path-local coords before the pixel transform). Solid-fill
    // colour only here; everything else defers to the scanline implementation.
    if (brush.type == 1 || brush.type == 2 || !pathGeometryCache_ ||
        !commands || commandLength == 0) {
        return EncodeFillPathScanline(startX, startY, commands, commandLength,
                                      brush, fillRule, transformIn, edgeMode);
    }

    // ── Compound-path correctness gate ──────────────────────────────────
    // A fill with more than one sub-path (any explicit MoveTo separator — the
    // first sub-path uses startX/startY, so tag 2 appears only for the 2nd+
    // contour) may describe a hole or nested contour. The transform-independent
    // triangulation fast path below classifies holes by winding SIGN
    // (jalium_triangulate.h::TriangulateCompoundPath): correct for NonZero
    // (it re-verifies each triangle's winding number) but WRONG for EvenOdd,
    // which has no such verification — two nested SAME-winding contours (a
    // ring, a letter 'O', a gear) both classify as "outer" and the hole fills
    // solid. EvenOdd is the default fill rule for XAML path markup /
    // StreamGeometry / PathGeometry, so this is the "many icons render
    // incorrectly under Analytic" bug.
    //
    // Route every compound fill to the analytic scanline rasterizer
    // (EncodeFillPathScanline → RasterizePathToRects), which resolves arbitrary
    // winding + fill rule + holes + self-intersection exactly and emits true
    // per-pixel coverage AA — the WPF/Skia-style analytic AA this mode is
    // documented to provide. A single sub-path continues below to the cached
    // fast path IF it is simple: ear-clip is exact for a non-self-intersecting
    // contour, where both fill rules yield its interior (a self-intersecting
    // single figure is caught by the ContourSelfIntersects guard below and also
    // routed to the scanline path). (The stencil-then-cover MSAA route is never
    // reached here, so this gate governs precisely the PathAntiAliasing.Analytic
    // solid-fill path.)
    for (uint32_t ci = 0; ci < commandLength; ) {
        int tag = (int)commands[ci];
        if (tag == 2) {  // MoveTo → a second (or later) sub-path exists
            return EncodeFillPathScanline(startX, startY, commands, commandLength,
                                          brush, fillRule, transformIn, edgeMode);
        }
        switch (tag) {
            case 0: ci += 3; break;  // LineTo  [tag, x, y]
            case 1: ci += 7; break;  // CubicTo [tag, c1x, c1y, c2x, c2y, ex, ey]
            case 3: ci += 5; break;  // QuadTo  [tag, cx, cy, ex, ey]
            case 5: ci += 1; break;  // ClosePath [tag]
            default: ci = commandLength; break;  // unknown tag → stop scanning
        }
    }

    const float maxScale     = MaxScaleFromTransform(transformIn);
    const uint32_t scaleBkt  = ScaleBucketFromMaxScale(maxScale);
    const uint64_t key = HashPathInput(startX, startY, commands, commandLength,
                                       (int32_t)fillRule, scaleBkt);

    std::shared_ptr<const CachedPathGeometry> geom;
    if (auto hit = pathGeometryCache_->FindAndTouch(key)) {
        geom = std::move(hit->entry);
        path_stats::AddGeometryHit();
    } else {
        auto fresh = std::make_shared<CachedPathGeometry>();
        // Local-space flatten. Source-space tolerance = pixel tolerance /
        // maxScale so the on-screen flattening error stays ≈flattenTolerance_
        // px at this scale bucket (same contract the gradient branch in the
        // scanline path relies on; scaleBucket gives each octave its own
        // entry so density tracks on-screen size).
        const float srcTol = (maxScale > 0.001f)
            ? flattenTolerance_ / maxScale : flattenTolerance_;
        {
            path_stats::ScopedFlattenTimer flattenTimer(commandLength);
            fresh->contours = FlattenPathToContours(
                startX, startY, commands, commandLength, srcTol);
            uint64_t ov = 0;
            for (const auto& c : fresh->contours) ov += c.VertexCount();
            flattenTimer.RecordOutputVerts(ov);
        }
        fresh->contours.erase(
            std::remove_if(fresh->contours.begin(), fresh->contours.end(),
                [](const Contour& c) { return c.VertexCount() < 3; }),
            fresh->contours.end());
        // The triangulation fast path is only correct for a single SIMPLE
        // contour: TriangulateCompoundPath's single-contour branch ear-clips
        // the raw polygon and ignores the fill rule, so a self-intersecting
        // outline (a pentagram / figure-eight authored as ONE figure) would be
        // filled wrong under EvenOdd (its inner region should be a hole). Leave
        // triangulationSucceeded=false for a self-intersecting contour so the
        // block below routes it to EncodeFillPathScanline → RasterizePathToRects,
        // which resolves winding + fill rule exactly. (The MoveTo gate above
        // already sent multi-sub-path/compound fills there; a post-flatten
        // count>1 — only reachable via a mid-buffer ClosePath that managed does
        // not emit — is handled the same way for safety.) This guard can only
        // move geometry from the fast path to the correct scanline path, never
        // the reverse, so it cannot regress; simple single contours (the
        // overwhelming majority of icons) keep the transform-independent cache.
        const bool fastPathSafe =
            fresh->contours.size() == 1 && !ContourSelfIntersects(fresh->contours[0]);
        if (fastPathSafe) {
            const int32_t fr = (fillRule == FillRule::NonZero) ? 1 : 0;
            std::vector<float> tri;
            {
                path_stats::ScopedTriangulateTimer triTimer;
                bool ok = TriangulateCompoundPath(fresh->contours, fr, tri)
                          && tri.size() >= 6;
                if (ok) {
                    triTimer.MarkOk();
                    fresh->localTriangles = std::move(tri);
                    fresh->triangulationSucceeded = true;
                }
            }
        }
        pathGeometryCache_->Insert(key, fresh);
        geom = std::move(fresh);
        path_stats::AddGeometryMiss();
    }

    if (!geom->triangulationSucceeded || geom->localTriangles.empty()) {
        // Not triangulable here — preserve the proven analytic-AA slow path.
        return EncodeFillPathScanline(startX, startY, commands, commandLength,
                                      brush, fillRule, transformIn, edgeMode);
    }

    const float r = brush.r * brush.a;
    const float g = brush.g * brush.a;
    const float b = brush.b * brush.a;
    const float a = brush.a;
    if (a <= 0.0f) return true;

    // Interior: transform the cached local-space triangle soup to pixel space.
    // This O(N) loop is the ONLY per-frame CPU cost for an animated fill now
    // (was: full bezier flatten + AET scanline rasterization every frame).
    // bbox is folded into the same loop and we use PushBatchWithCoverage so
    // back-to-back fills (typical UI: multiple shapes in a row) collapse into
    // one D3D12 DrawIndexedInstanced and avoid the second vertex walk inside
    // ComputeBatchCoverage.
    {
        const auto& lt = geom->localTriangles;       // x,y pairs, 3 per tri
        const uint32_t vc = (uint32_t)(lt.size() / 2);
        ImpellerDrawBatch batch;
        batch.vertices.resize(vc);
        batch.indices.resize(vc);
        const float* pp = lt.data();
        ImpellerVertex* vp = batch.vertices.data();
        uint32_t* ip = batch.indices.data();
        const float tm11 = transformIn.m11, tm21 = transformIn.m21, tdx = transformIn.dx;
        const float tm12 = transformIn.m12, tm22 = transformIn.m22, tdy = transformIn.dy;
        float minX =  std::numeric_limits<float>::infinity();
        float minY =  std::numeric_limits<float>::infinity();
        float maxX = -std::numeric_limits<float>::infinity();
        float maxY = -std::numeric_limits<float>::infinity();
        for (uint32_t i = 0; i < vc; ++i) {
            float lx = pp[i * 2], ly = pp[i * 2 + 1];
            float x = tm11 * lx + tm21 * ly + tdx;
            float y = tm12 * lx + tm22 * ly + tdy;
            vp[i].x = x; vp[i].y = y;
            vp[i].r = r; vp[i].g = g; vp[i].b = b; vp[i].a = a;
            ip[i] = i;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        batch.pipelineType = 0;
        PushBatchWithCoverage(std::move(batch), minX, minY, maxX, maxY);
    }

    // Edge AA: constant-width feather ring around every boundary contour,
    // built in pixel space from the cached local contours so the soft edge
    // stays ~0.6 px on screen at any transform.
    EmitContourFeather(geom->contours, transformIn, r, g, b, a);

    encodedPathCount_++;
    return true;
}

// ----------------------------------------------------------------------------
// EmitContourFeather — 1 px-ish alpha-fade ring along each boundary contour.
//
// Our solid-fill PSO renders to a single-sample target (no MSAA), so raw
// triangle edges would stair-step. For every contour we emit a centred ring:
// each boundary vertex contributes an inner vertex (full fill alpha, ~0.3 px
// inside) and an outer vertex (alpha 0, ~0.3 px outside) along the averaged
// edge normal; consecutive pairs form a triangle strip. GPU bilinear blend of
// the per-vertex alpha gives a clean ~0.6 px feather. The ring is centred so
// it always overlaps the interior mesh — no seam, and the inner-side normal
// sign is irrelevant to quality.
// ----------------------------------------------------------------------------
void ImpellerD3D12Engine::EmitContourFeather(
    const std::vector<Contour>& contours,
    const EngineTransform& transform,
    float r, float g, float b, float a)
{
    if (a <= 0.0f) return;
    constexpr float kHalfFeatherPx = 0.3f;  // ⇒ ~0.6 px total soft edge

    ImpellerDrawBatch batch;
    batch.pipelineType = 0;

    // Track screen-space bbox inline so PushBatchWithCoverage can both
    // coalesce this batch with the interior fill emitted just before us AND
    // skip its second vertex walk inside ComputeBatchCoverage.
    float minX =  std::numeric_limits<float>::infinity();
    float minY =  std::numeric_limits<float>::infinity();
    float maxX = -std::numeric_limits<float>::infinity();
    float maxY = -std::numeric_limits<float>::infinity();
    const float tm11 = transform.m11, tm21 = transform.m21, tdx = transform.dx;
    const float tm12 = transform.m12, tm22 = transform.m22, tdy = transform.dy;

    for (const auto& c : contours) {
        const uint32_t n = c.VertexCount();
        if (n < 3) continue;

        // Transform this contour's points to pixel space once.
        std::vector<float> p(n * 2);
        for (uint32_t i = 0; i < n; ++i) {
            float lx = c.X(i), ly = c.Y(i);
            p[i * 2]     = tm11 * lx + tm21 * ly + tdx;
            p[i * 2 + 1] = tm12 * lx + tm22 * ly + tdy;
        }

        const uint32_t base = (uint32_t)batch.vertices.size();
        batch.vertices.reserve(batch.vertices.size() + (size_t)n * 2 + 2);
        batch.indices.reserve(batch.indices.size() + (size_t)n * 6 + 6);

        for (uint32_t i = 0; i < n; ++i) {
            const uint32_t prev = (i + n - 1) % n;
            const uint32_t next = (i + 1) % n;
            // Averaged adjacent-edge direction → outward normal (perp).
            float dx = p[next * 2]     - p[prev * 2];
            float dy = p[next * 2 + 1] - p[prev * 2 + 1];
            float len = std::sqrt(dx * dx + dy * dy);
            float nx, ny;
            if (len > 1e-6f) { nx = -dy / len; ny = dx / len; }
            else             { nx = 0.0f;      ny = 0.0f; }
            const float px = p[i * 2], py = p[i * 2 + 1];
            const float ix = px - nx * kHalfFeatherPx;
            const float iy = py - ny * kHalfFeatherPx;
            const float ox = px + nx * kHalfFeatherPx;
            const float oy = py + ny * kHalfFeatherPx;
            // Inner (solid) then outer (transparent).
            batch.vertices.push_back({ ix, iy, r, g, b, a });
            batch.vertices.push_back({ ox, oy, 0, 0, 0, 0 });
            // Independent min/max checks on every vertex — the inner/outer
            // pair is centred on the boundary, so neither dominates.
            if (ix < minX) minX = ix;
            if (iy < minY) minY = iy;
            if (ix > maxX) maxX = ix;
            if (iy > maxY) maxY = iy;
            if (ox < minX) minX = ox;
            if (oy < minY) minY = oy;
            if (ox > maxX) maxX = ox;
            if (oy > maxY) maxY = oy;
        }

        // Strip around the closed loop: (in_i,out_i,in_i+1)+(out_i,out_i+1,in_i+1)
        for (uint32_t i = 0; i < n; ++i) {
            const uint32_t j = (i + 1) % n;
            const uint32_t in_i  = base + i * 2;
            const uint32_t out_i = in_i + 1;
            const uint32_t in_j  = base + j * 2;
            const uint32_t out_j = in_j + 1;
            batch.indices.push_back(in_i);
            batch.indices.push_back(out_i);
            batch.indices.push_back(in_j);
            batch.indices.push_back(out_i);
            batch.indices.push_back(out_j);
            batch.indices.push_back(in_j);
        }
    }

    if (!batch.vertices.empty())
        PushBatchWithCoverage(std::move(batch), minX, minY, maxX, maxY);
}

// ============================================================================
// EncodeFillPathScanline — legacy pixel-space scanline fill (UNCHANGED).
//
// Preserved verbatim as the fallback for paths EncodeFillPath's triangulator
// can't handle (self-intersecting / multi-subpath glyph outlines) and for
// gradient brushes. Its transform-coupled PixelRect cache is fine here: the
// inputs that reach it are rare and typically static.
// ============================================================================
bool ImpellerD3D12Engine::EncodeFillPathScanline(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transformIn,
    int32_t edgeMode)
{
    (void)edgeMode;  // D3D12 fill already runs analytic AA via RasterizePathToRects;
                     // Aliased fallback is reserved for the binary-mesh fast path.
    auto fillPathEntryTime = std::chrono::high_resolution_clock::now();

    // ------------------------------------------------------------------
    // Gradient brushes still use the source-space flatten path because
    // EncodeGradientFillPath samples the gradient at each contour vertex
    // in PATH-LOCAL coordinates (gradient brush.startX/Y/endX/Y are also
    // in path space) and only transforms to pixels after sampling.
    // Touching that contract would require rewriting the gradient
    // sampler — out of scope for this fix, so we keep the legacy path.
    //
    // Gradient path also bypasses the rasterized-fill cache (key would
    // need brush stops), which is fine because gradients are a small
    // fraction of total fills in typical UI workloads.
    // ------------------------------------------------------------------
    if (brush.type == 1 || brush.type == 2) {
        float gradMaxScale = std::max(
            std::sqrt(transformIn.m11 * transformIn.m11 + transformIn.m12 * transformIn.m12),
            std::sqrt(transformIn.m21 * transformIn.m21 + transformIn.m22 * transformIn.m22));
        float gradTolerance = (gradMaxScale > 0.001f)
            ? flattenTolerance_ / gradMaxScale
            : flattenTolerance_;

        std::vector<Contour> gradContours;
        {
            path_stats::ScopedFlattenTimer flattenTimer(commandLength);
            gradContours = FlattenPathToContours(
                startX, startY, commands, commandLength, gradTolerance);
            uint64_t outputVerts = 0;
            for (const auto& c : gradContours) outputVerts += c.VertexCount();
            flattenTimer.RecordOutputVerts(outputVerts);
        }
        if (gradContours.empty()) return false;

        bool gradOk = EncodeGradientFillPath(gradContours, brush, transformIn);
        if (gradOk) encodedPathCount_++;
        return gradOk;
    }

    // ───────────────────────────────────────────────────────────────────
    // Solid fill: same 1/8-px-quantized dx/dy stripping as EncodeStrokePath.
    // Repeated controls at any DPI-snapped position share a cache entry.
    // ───────────────────────────────────────────────────────────────────
    int intDx, intDy;
    EngineTransform transform = transformIn;
    {
        constexpr float kFracQuant = 8.0f;
        constexpr float kInvFracQuant = 1.0f / 8.0f;
        int qDx = (int)std::lround(transformIn.dx * kFracQuant);
        int qDy = (int)std::lround(transformIn.dy * kFracQuant);
        int fracDxBucket = ((qDx % 8) + 8) % 8;
        int fracDyBucket = ((qDy % 8) + 8) % 8;
        intDx = (qDx - fracDxBucket) / 8;
        intDy = (qDy - fracDyBucket) / 8;
        transform.dx = fracDxBucket * kInvFracQuant;
        transform.dy = fracDyBucket * kInvFracQuant;
    }

    // Local helper — same in-place coalescing + resize+index-write hot loop
    // as EncodeStrokePath's emitter (zero temp-batch allocation).
    auto emitFillRectsAsBatch = [this, &brush](const std::vector<PixelRect>& rects, int sx, int sy) {
        size_t rectCount = rects.size();
        if (rectCount == 0) return;

        float br = brush.r * brush.a;
        float bg = brush.g * brush.a;
        float bb = brush.b * brush.a;
        float ba = brush.a;
        float fsx = (float)sx, fsy = (float)sy;

        ImpellerDrawBatch* target = nullptr;
        if (!batches_.empty()) {
            auto& last = batches_.back();
            if (last.pipelineType == 0 && last.stencilContours.empty() &&
                last.hasScissor == hasScissor_ &&
                (!hasScissor_ ||
                 (last.scissorL == scissorLeft_ && last.scissorT == scissorTop_ &&
                  last.scissorR == scissorRight_ && last.scissorB == scissorBottom_)) &&
                last.hasRoundedClip == hasRoundedClip_ &&
                (!hasRoundedClip_ ||
                 (last.roundedClipRect[0] == roundedClipRect_[0] && last.roundedClipRect[1] == roundedClipRect_[1] &&
                  last.roundedClipRect[2] == roundedClipRect_[2] && last.roundedClipRect[3] == roundedClipRect_[3] &&
                  last.roundedClipCornerRadii[0] == roundedClipCornerRadii_[0] && last.roundedClipCornerRadii[1] == roundedClipCornerRadii_[1] &&
                  last.roundedClipCornerRadii[2] == roundedClipCornerRadii_[2] && last.roundedClipCornerRadii[3] == roundedClipCornerRadii_[3])))
            {
                target = &last;
            }
        }
        if (target == nullptr) {
            batches_.emplace_back();
            target = &batches_.back();
            target->pipelineType = 0;
            target->hasScissor = hasScissor_;
            if (hasScissor_) {
                target->scissorL = scissorLeft_;
                target->scissorT = scissorTop_;
                target->scissorR = scissorRight_;
                target->scissorB = scissorBottom_;
            }
            target->hasRoundedClip = hasRoundedClip_;
            if (hasRoundedClip_) {
                target->roundedClipRect[0] = roundedClipRect_[0]; target->roundedClipRect[1] = roundedClipRect_[1];
                target->roundedClipRect[2] = roundedClipRect_[2]; target->roundedClipRect[3] = roundedClipRect_[3];
                target->roundedClipCornerRadii[0] = roundedClipCornerRadii_[0]; target->roundedClipCornerRadii[1] = roundedClipCornerRadii_[1];
                target->roundedClipCornerRadii[2] = roundedClipCornerRadii_[2]; target->roundedClipCornerRadii[3] = roundedClipCornerRadii_[3];
            }
        }

        size_t oldV = target->vertices.size();
        size_t oldI = target->indices.size();
        target->vertices.resize(oldV + rectCount * 4);
        target->indices.resize(oldI + rectCount * 6);

        auto* vp = target->vertices.data() + oldV;
        auto* ip = target->indices.data() + oldI;
        const auto* rp = rects.data();
        uint32_t baseVertex = (uint32_t)oldV;

        float minX = std::numeric_limits<float>::infinity();
        float minY = std::numeric_limits<float>::infinity();
        float maxX = -std::numeric_limits<float>::infinity();
        float maxY = -std::numeric_limits<float>::infinity();

        for (size_t i = 0; i < rectCount; i++) {
            const auto& rect = rp[i];
            float x0 = (float)rect.x + fsx;
            float y0 = (float)rect.y + fsy;
            float x1 = x0 + (float)rect.w;
            float y1 = y0 + (float)rect.h;
            float cov = rect.alpha;
            float ra = br * cov;
            float ga = bg * cov;
            float bbA = bb * cov;
            float aa = ba * cov;

            size_t v = i * 4;
            vp[v + 0] = { x0, y0, ra, ga, bbA, aa };
            vp[v + 1] = { x1, y0, ra, ga, bbA, aa };
            vp[v + 2] = { x1, y1, ra, ga, bbA, aa };
            vp[v + 3] = { x0, y1, ra, ga, bbA, aa };

            uint32_t b = baseVertex + (uint32_t)v;
            size_t k = i * 6;
            ip[k + 0] = b;
            ip[k + 1] = b + 1;
            ip[k + 2] = b + 2;
            ip[k + 3] = b;
            ip[k + 4] = b + 2;
            ip[k + 5] = b + 3;

            if (x0 < minX) minX = x0;
            if (y0 < minY) minY = y0;
            if (x1 > maxX) maxX = x1;
            if (y1 > maxY) maxY = y1;
        }

        if (target->hasCoverage) {
            if (minX < target->coverageL) target->coverageL = minX;
            if (minY < target->coverageT) target->coverageT = minY;
            if (maxX > target->coverageR) target->coverageR = maxX;
            if (maxY > target->coverageB) target->coverageB = maxY;
        } else {
            target->hasCoverage = true;
            target->coverageL = minX;
            target->coverageT = minY;
            target->coverageR = maxX;
            target->coverageB = maxY;
        }
        encodedPathCount_++;
    };

    uint64_t fillCacheKey = HashFillInputs(
        startX, startY, commands, commandLength,
        (int32_t)fillRule, transform);

    if (auto cached = FillCacheFind(fillCacheKey)) {
        path_stats::AddFillHit(cached->rects.size());
        if (cached->rects.empty()) {
            // Empty result was previously seen — fall through to
            // triangulation fallback (rasterizer-empty doesn't mean
            // fully empty; sub-pixel paths still render via triangulator).
        } else {
            emitFillRectsAsBatch(cached->rects, intDx, intDy);
            return true;
        }
    } else {
        path_stats::AddFillMiss();
    }

    // ------------------------------------------------------------------
    // Solid fill: transform commands → pixel space, then flatten with a
    // fixed pixel-space tolerance.
    //
    // The previous approach scaled flattenTolerance_ by 1/maxScale to
    // approximate constant screen-space error while flattening in source
    // space. That breaks for shapes where Stretch="Uniform" downscales a
    // ~1000-unit source path into ~8 pixels: source-space tolerance
    // balloons to ~35 units, Wang's formula then produces only ~2 segments
    // per arc, and ear-clipping the resulting near-degenerate concave
    // polygon at 8-pixel scale leaks pixels at the rasterized edges (the
    // "rounded play arrow with missing chunks" symptom).
    //
    // Doing it in pixel space gives every Bézier exactly the right segment
    // count for the actual on-screen size: small icons get few segments
    // (no waste), huge SVGs get many (no aliasing). The contours that come
    // out of FlattenPathToContours are already in pixel coordinates, so we
    // also skip the post-flatten transform pass below.
    // ------------------------------------------------------------------
    float maxScale = std::max(
        std::sqrt(transform.m11 * transform.m11 + transform.m12 * transform.m12),
        std::sqrt(transform.m21 * transform.m21 + transform.m22 * transform.m22));

    float pxStartX = startX, pxStartY = startY;
    TransformPoint(pxStartX, pxStartY, transform);

    std::vector<float> pxCommands;
    pxCommands.reserve(commandLength);
    {
        uint32_t i = 0;
        while (i < commandLength) {
            int tag = (int)commands[i];
            switch (tag) {
                case 0: { // LineTo: [0, ex, ey]
                    if (i + 2 >= commandLength) { i = commandLength; break; }
                    float x = commands[i + 1], y = commands[i + 2];
                    TransformPoint(x, y, transform);
                    pxCommands.push_back(0.0f);
                    pxCommands.push_back(x);
                    pxCommands.push_back(y);
                    i += 3;
                    break;
                }
                case 1: { // CubicTo: [1, c1x, c1y, c2x, c2y, ex, ey]
                    if (i + 6 >= commandLength) { i = commandLength; break; }
                    float c1x = commands[i + 1], c1y = commands[i + 2];
                    float c2x = commands[i + 3], c2y = commands[i + 4];
                    float ex  = commands[i + 5], ey  = commands[i + 6];
                    TransformPoint(c1x, c1y, transform);
                    TransformPoint(c2x, c2y, transform);
                    TransformPoint(ex,  ey,  transform);
                    pxCommands.push_back(1.0f);
                    pxCommands.push_back(c1x); pxCommands.push_back(c1y);
                    pxCommands.push_back(c2x); pxCommands.push_back(c2y);
                    pxCommands.push_back(ex);  pxCommands.push_back(ey);
                    i += 7;
                    break;
                }
                case 2: { // MoveTo: [2, x, y]
                    if (i + 2 >= commandLength) { i = commandLength; break; }
                    float x = commands[i + 1], y = commands[i + 2];
                    TransformPoint(x, y, transform);
                    pxCommands.push_back(2.0f);
                    pxCommands.push_back(x);
                    pxCommands.push_back(y);
                    i += 3;
                    break;
                }
                case 3: { // QuadTo: [3, cx, cy, ex, ey]
                    if (i + 4 >= commandLength) { i = commandLength; break; }
                    float cx = commands[i + 1], cy = commands[i + 2];
                    float ex = commands[i + 3], ey = commands[i + 4];
                    TransformPoint(cx, cy, transform);
                    TransformPoint(ex, ey, transform);
                    pxCommands.push_back(3.0f);
                    pxCommands.push_back(cx); pxCommands.push_back(cy);
                    pxCommands.push_back(ex); pxCommands.push_back(ey);
                    i += 5;
                    break;
                }
                case 5: { // ClosePath: [5]
                    pxCommands.push_back(5.0f);
                    i += 1;
                    break;
                }
                default:
                    // Tag 4 (ArcTo) is never emitted by managed (arcs are
                    // pre-converted to cubics); unknown tag → bail out of
                    // the loop so we still flatten what we have.
                    i = commandLength;
                    break;
            }
        }
    }

    // Fixed pixel-space tolerance — independent of source scale.
    float adaptiveTolerance = flattenTolerance_;

    std::vector<Contour> contours;
    {
        path_stats::ScopedFlattenTimer flattenTimer(commandLength);
        contours = FlattenPathToContours(
            pxStartX, pxStartY, pxCommands.data(), (uint32_t)pxCommands.size(),
            adaptiveTolerance);
        uint64_t outputVerts = 0;
        for (const auto& c : contours) outputVerts += c.VertexCount();
        flattenTimer.RecordOutputVerts(outputVerts);
    }

    if (contours.empty()) {
        return false;
    }

    // Contours are already in pixel space (transformed pre-flatten above).
    // Gradients took the early-return source-space path, so anything that
    // reaches here is a solid fill.

    // Remove degenerate contours
    contours.erase(
        std::remove_if(contours.begin(), contours.end(),
            [](const Contour& c) { return c.VertexCount() < 3; }),
        contours.end());
    if (contours.empty()) return false;

    // Premultiply alpha
    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    // ------------------------------------------------------------------
    // Scanline rasterization — primary path for every solid fill.
    //
    // RasterizePathToRects runs the full AET scanline algorithm against
    // the contours (any size, any complexity, any fill rule) and returns
    // a list of axis-aligned rectangles that exactly tile the filled
    // pixels under D3D's top-left rule. Vertical run-length coalescing
    // collapses repeated span layouts, so even a full-window fill
    // produces a handful of rects instead of thousands.
    //
    // This replaces triangulation entirely for correctness-critical
    // cases: ear-clipping and its fallbacks used to crack concave /
    // self-intersecting / hole-bearing paths at small sizes (scrollbar
    // arrows, glyph-style icons) and drop interior pixels. The scanline
    // path has no such failure modes — it handles arbitrary contours
    // directly from edge crossings, not tessellation.
    //
    // Triangulation is retained below only as a last-resort fallback
    // for the pathological case where scanlining produces zero rects
    // (e.g. entirely sub-pixel geometry that nothing should render).
    // ------------------------------------------------------------------
    {
        std::vector<PixelRect> rects;
        rects.reserve(64);
        RasterizePathToRects(contours, fillRule, rects);

        if (!rects.empty()) {
            // Cache rects origin-relative; emit lambda applies (intDx, intDy).
            auto entry = std::make_shared<CachedFillRects>();
            entry->rects = rects;
            FillCacheInsert(fillCacheKey, std::move(entry));

            emitFillRectsAsBatch(rects, intDx, intDy);
            return true;
        }
        // Empty rect list — sub-pixel or degenerate. Fall through to
        // triangulation as a last resort so something still renders.
    }

    // ------------------------------------------------------------------
    // CPU triangulation routing (fallback for large paths).
    //
    // TriangulateCompoundPath is designed for multi-contour paths with holes
    // and arbitrary fill rules. For SINGLE-contour concave shapes the plain
    // ear-clipping (TriangulatePolygon) handles them robustly. Route:
    //   • 1 contour  → TriangulatePolygon (ear-clip)
    //   • >1 contour → TriangulateCompoundPath (handles holes + winding)
    //
    // Failure of either path falls through to per-contour ear-clip as a
    // best-effort recovery — better to render *something* than nothing.
    // ------------------------------------------------------------------
    int32_t fr = (fillRule == FillRule::NonZero) ? 1 : 0;

    // Triangulation paths emit pixel-space vertices directly; because the
    // pipeline above ran with dx/dy zeroed (cache requires origin-relative
    // output), we must add (intDx, intDy) here to land at the correct
    // screen position. fdx/fdy are the per-vertex offsets.
    float fdx = (float)intDx;
    float fdy = (float)intDy;

    if (contours.size() == 1) {
        const auto& c = contours[0];
        std::vector<uint32_t> indices;
        bool triOk;
        {
            path_stats::ScopedTriangulateTimer triTimer;
            triOk = TriangulatePolygon(c.points.data(), c.VertexCount(), indices)
                    && indices.size() >= 3;
            if (triOk) triTimer.MarkOk();
        }
        if (triOk)
        {
            ImpellerDrawBatch batch;
            batch.vertices.reserve(c.VertexCount());
            for (uint32_t i = 0; i < c.VertexCount(); ++i) {
                batch.vertices.push_back({ c.X(i) + fdx, c.Y(i) + fdy, r, g, b, a });
            }
            batch.indices = std::move(indices);
            batch.pipelineType = 0;
            PushBatch(std::move(batch));
            encodedPathCount_++;
            return true;
        }
    } else {
        std::vector<float> triVerts;
        bool triOk;
        {
            path_stats::ScopedTriangulateTimer triTimer;
            triOk = TriangulateCompoundPath(contours, fr, triVerts) && triVerts.size() >= 6;
            if (triOk) triTimer.MarkOk();
        }
        if (triOk) {
            ImpellerDrawBatch batch;
            uint32_t vertCount = (uint32_t)(triVerts.size() / 2);
            batch.vertices.reserve(vertCount);
            batch.indices.reserve(vertCount);
            for (uint32_t i = 0; i < vertCount; ++i) {
                batch.vertices.push_back({ triVerts[i * 2] + fdx, triVerts[i * 2 + 1] + fdy, r, g, b, a });
                batch.indices.push_back(i);
            }
            batch.pipelineType = 0;
            PushBatch(std::move(batch));
            encodedPathCount_++;
            return true;
        }
    }

    // Best-effort fallback: triangulate each contour independently. This
    // loses inter-contour winding (holes) but renders something visible for
    // shapes the primary triangulator rejects.
    {
        bool anyEmitted = false;
        for (auto& c : contours) {
            uint32_t vc = c.VertexCount();
            if (vc < 3) continue;
            std::vector<uint32_t> indices;
            bool triOk;
            {
                path_stats::ScopedTriangulateTimer triTimer;
                triOk = TriangulatePolygon(c.points.data(), vc, indices) && indices.size() >= 3;
                if (triOk) triTimer.MarkOk();
            }
            if (triOk) {
                ImpellerDrawBatch batch;
                batch.vertices.reserve(indices.size());
                batch.indices.reserve(indices.size());
                for (uint32_t idx = 0; idx < (uint32_t)indices.size(); ++idx) {
                    uint32_t vi = indices[idx];
                    batch.vertices.push_back({ c.X(vi) + fdx, c.Y(vi) + fdy, r, g, b, a });
                    batch.indices.push_back(idx);
                }
                batch.pipelineType = 0;
                PushBatch(std::move(batch));
                anyEmitted = true;
            }
        }
        if (anyEmitted) encodedPathCount_++;
        return anyEmitted;
    }
}

// ============================================================================
// Stroke rasterization cache helpers
//
// EncodeStrokePath's CPU pipeline (transform commands → flatten → optional dash
// → ExpandStroke mesh → RasterizePathToRects) dominates StreamGeometry /
// DrawGeometry profiles when many static paths are redrawn each frame. The
// cache stores the final PixelRect list so hits skip the entire pipeline and
// only run the per-frame batch build. See d3d12_impeller_engine.h for the full
// rationale and key design.
// ============================================================================

namespace {
inline void FnvMix64(uint64_t& h, const void* data, size_t size) noexcept {
    auto* p = static_cast<const uint8_t*>(data);
    for (size_t i = 0; i < size; i++) {
        h ^= p[i];
        h *= 0x100000001B3ull;
    }
}
}  // namespace

uint64_t ImpellerD3D12Engine::HashStrokeInputs(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    float strokeWidth, bool closed,
    int32_t lineJoin, float miterLimit, int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    const EngineTransform& transform,
    int32_t edgeMode) noexcept
{
    uint64_t h = 0xCBF29CE484222325ull;  // FNV-1a 64-bit offset basis
    FnvMix64(h, &startX, sizeof(startX));
    FnvMix64(h, &startY, sizeof(startY));
    FnvMix64(h, &commandLength, sizeof(commandLength));
    if (commands && commandLength > 0)
        FnvMix64(h, commands, commandLength * sizeof(float));
    FnvMix64(h, &strokeWidth, sizeof(strokeWidth));
    uint8_t closedByte = closed ? 1 : 0;
    FnvMix64(h, &closedByte, sizeof(closedByte));
    FnvMix64(h, &lineJoin, sizeof(lineJoin));
    FnvMix64(h, &miterLimit, sizeof(miterLimit));
    FnvMix64(h, &lineCap, sizeof(lineCap));
    FnvMix64(h, &dashCount, sizeof(dashCount));
    if (dashPattern && dashCount > 0)
        FnvMix64(h, dashPattern, dashCount * sizeof(float));
    FnvMix64(h, &dashOffset, sizeof(dashOffset));
    // Transform must be in the key — the entire pipeline (including command
    // pre-transform) runs in pixel space, so different transforms produce
    // different rects. Static UI keeps transform stable across frames.
    FnvMix64(h, &transform.m11, sizeof(float));
    FnvMix64(h, &transform.m12, sizeof(float));
    FnvMix64(h, &transform.m21, sizeof(float));
    FnvMix64(h, &transform.m22, sizeof(float));
    FnvMix64(h, &transform.dx,  sizeof(float));
    FnvMix64(h, &transform.dy,  sizeof(float));
    // edgeMode partitions Antialiased (analytic) vs Aliased (binary) entries
    // so the two pipelines don't poison each other's cache.
    uint8_t edgeByte = (uint8_t)(edgeMode & 0xFF);
    FnvMix64(h, &edgeByte, sizeof(edgeByte));
    return h;
}

std::shared_ptr<const ImpellerD3D12Engine::CachedStrokeRects>
ImpellerD3D12Engine::StrokeCacheFind(uint64_t key)
{
    auto it = strokeCacheMap_.find(key);
    if (it == strokeCacheMap_.end()) return nullptr;
    // Promote to head (most-recently-used).
    strokeCacheList_.splice(strokeCacheList_.begin(), strokeCacheList_, it->second);
    return it->second->entry;
}

void ImpellerD3D12Engine::StrokeCacheInsert(
    uint64_t key, std::shared_ptr<const CachedStrokeRects> entry)
{
    auto existing = strokeCacheMap_.find(key);
    if (existing != strokeCacheMap_.end()) {
        existing->second->entry = std::move(entry);
        strokeCacheList_.splice(strokeCacheList_.begin(), strokeCacheList_, existing->second);
        return;
    }
    if (strokeCacheList_.size() >= kStrokeCacheCapacity) {
        auto& lru = strokeCacheList_.back();
        strokeCacheMap_.erase(lru.key);
        strokeCacheList_.pop_back();
    }
    strokeCacheList_.push_front({key, std::move(entry)});
    strokeCacheMap_[key] = strokeCacheList_.begin();
}

std::shared_ptr<const ImpellerD3D12Engine::CachedStrokeAnalyticRects>
ImpellerD3D12Engine::StrokeAnalyticCacheFind(uint64_t key)
{
    auto it = strokeAnalyticCacheMap_.find(key);
    if (it == strokeAnalyticCacheMap_.end()) return nullptr;
    strokeAnalyticCacheList_.splice(strokeAnalyticCacheList_.begin(), strokeAnalyticCacheList_, it->second);
    return it->second->entry;
}

void ImpellerD3D12Engine::StrokeAnalyticCacheInsert(
    uint64_t key, std::shared_ptr<const CachedStrokeAnalyticRects> entry)
{
    auto existing = strokeAnalyticCacheMap_.find(key);
    if (existing != strokeAnalyticCacheMap_.end()) {
        existing->second->entry = std::move(entry);
        strokeAnalyticCacheList_.splice(strokeAnalyticCacheList_.begin(), strokeAnalyticCacheList_, existing->second);
        return;
    }
    if (strokeAnalyticCacheList_.size() >= kStrokeAnalyticCacheCapacity) {
        auto& lru = strokeAnalyticCacheList_.back();
        strokeAnalyticCacheMap_.erase(lru.key);
        strokeAnalyticCacheList_.pop_back();
    }
    strokeAnalyticCacheList_.push_front({key, std::move(entry)});
    strokeAnalyticCacheMap_[key] = strokeAnalyticCacheList_.begin();
}

uint64_t ImpellerD3D12Engine::HashFillInputs(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    int32_t fillRule,
    const EngineTransform& transform) noexcept
{
    uint64_t h = 0xCBF29CE484222325ull;
    FnvMix64(h, &startX, sizeof(startX));
    FnvMix64(h, &startY, sizeof(startY));
    FnvMix64(h, &commandLength, sizeof(commandLength));
    if (commands && commandLength > 0)
        FnvMix64(h, commands, commandLength * sizeof(float));
    FnvMix64(h, &fillRule, sizeof(fillRule));
    FnvMix64(h, &transform.m11, sizeof(float));
    FnvMix64(h, &transform.m12, sizeof(float));
    FnvMix64(h, &transform.m21, sizeof(float));
    FnvMix64(h, &transform.m22, sizeof(float));
    FnvMix64(h, &transform.dx,  sizeof(float));
    FnvMix64(h, &transform.dy,  sizeof(float));
    return h;
}

std::shared_ptr<const ImpellerD3D12Engine::CachedFillRects>
ImpellerD3D12Engine::FillCacheFind(uint64_t key)
{
    auto it = fillCacheMap_.find(key);
    if (it == fillCacheMap_.end()) return nullptr;
    fillCacheList_.splice(fillCacheList_.begin(), fillCacheList_, it->second);
    return it->second->entry;
}

void ImpellerD3D12Engine::FillCacheInsert(
    uint64_t key, std::shared_ptr<const CachedFillRects> entry)
{
    auto existing = fillCacheMap_.find(key);
    if (existing != fillCacheMap_.end()) {
        existing->second->entry = std::move(entry);
        fillCacheList_.splice(fillCacheList_.begin(), fillCacheList_, existing->second);
        return;
    }
    if (fillCacheList_.size() >= kFillCacheCapacity) {
        auto& lru = fillCacheList_.back();
        fillCacheMap_.erase(lru.key);
        fillCacheList_.pop_back();
    }
    fillCacheList_.push_front({key, std::move(entry)});
    fillCacheMap_[key] = fillCacheList_.begin();
}

// ============================================================================
// EncodeStrokePath — transform-independent local-space cache for the common
// case (solid, non-dashed, binary-mesh+feather — i.e. animated spinners /
// progress rings / stroked Paths under a RenderTransform). Same root-cause fix
// as EncodeFillPath/EncodeFillPolygon: the legacy body (now
// EncodeStrokePathPixelCached) keys its cache on the FULL transform and runs
// the whole flatten → ExpandStroke → (analytic) rasterize pipeline in pixel
// space, so any scale/rotation/animation misses every frame.
//
// Here we flatten + expand ONCE in source space (source-unit strokeWidth →
// thickness scales with the transform, exactly WPF Pen semantics), cache the
// local-space feathered triangle mesh keyed by path + stroke params +
// scaleBucket (NOT transform), then each frame only transform the cached
// vertices (O(N)). Dashed strokes, explicit Antialiased (analytic, the
// static-icon quality mode), gradient brushes and the no-command case defer
// to EncodeStrokePathPixelCached unchanged.
// ============================================================================
bool ImpellerD3D12Engine::EncodeStrokePath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    float strokeWidth, bool closed,
    int32_t lineJoin, float miterLimit,
    int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    const EngineTransform& transformIn,
    int32_t edgeMode)
{
    int em = edgeMode;
    if (em < 0) em = 1;                       // default = binary mesh + feather
    const bool analytic = (em == 2);          // explicit Antialiased (static)

    // Anything outside the cacheable common case keeps the proven legacy path.
    // Gradient brushes (type 1/2) now take the cached local-space path too: the
    // emit loop samples the gradient per vertex in source space, producing a TRUE
    // per-pixel gradient stroke that keeps the solid path's feather AA. Only
    // dashed / explicit-analytic gradients fall back to the (flat) pixel-cached
    // path.
    if (analytic || dashCount > 0 || dashPattern ||
        !commands || commandLength == 0 || strokeWidth <= 0.0f) {
        return EncodeStrokePathPixelCached(
            startX, startY, commands, commandLength, brush,
            strokeWidth, closed, lineJoin, miterLimit, lineCap,
            dashPattern, dashCount, dashOffset, transformIn, edgeMode);
    }

    const float maxScale    = MaxScaleFromTransform(transformIn);
    const uint32_t scaleBkt = ScaleBucketFromMaxScale(maxScale);

    // Key: geometry + scaleBucket (HashPathInput, same as fill) then the
    // stroke-shape parameters mixed in. Transform is NOT in the key — it is
    // applied per frame at emit. StrokeCache is a distinct map from the fill
    // cache so there is no cross-pollution.
    uint64_t key = HashPathInput(startX, startY, commands, commandLength,
                                 /*fillRule*/ 0, scaleBkt);
    FnvMix64(key, &strokeWidth, sizeof(strokeWidth));
    uint8_t closedByte = closed ? 1 : 0;
    FnvMix64(key, &closedByte, sizeof(closedByte));
    FnvMix64(key, &lineJoin, sizeof(lineJoin));
    FnvMix64(key, &miterLimit, sizeof(miterLimit));
    FnvMix64(key, &lineCap, sizeof(lineCap));

    const float br = brush.r * brush.a;
    const float bg = brush.g * brush.a;
    const float bb = brush.b * brush.a;
    const float ba = brush.a;

    // Gradient strokes sample the gradient per vertex (in source/path space) at
    // emit time. The cached mesh stores only positions + feather coverage, so it
    // is brush-agnostic; the color is applied fresh here, giving a TRUE per-pixel
    // gradient stroke that keeps the solid path's feather AA.
    const bool isGradientStroke = (brush.type == 1 || brush.type == 2);
    std::vector<float> gradStopData;
    if (isGradientStroke) FlattenGradientStops(brush, gradStopData);

    // Emit a cached local-space mesh: transform every vertex by the current
    // transform (O(N)) and reapply the per-vertex feather coverage. This is
    // the ONLY per-frame CPU cost now for an animated stroke (was: full
    // flatten + ExpandStroke + scanline rasterize every frame).
    //
    // We compute the screen-space coverage bbox inside the same loop and emit
    // via PushBatchWithCoverage so:
    //   1) consecutive cached strokes (typical UI: icon row, ScrollBar arrows,
    //      Checkbox glyphs) collapse into ONE D3D12 DrawIndexedInstanced;
    //   2) PushBatch's ComputeBatchCoverage second walk over vertices is
    //      skipped — saves ~N float compares per call.
    // Coverage vector is always populated by StrokeCacheInsert (one byte per
    // vertex), so the hot loop reads it unconditionally — no branch.
    auto emitLocalMesh = [&](const CachedStrokeRects& m) {
        const size_t vc = m.positions.size() / 2;
        if (vc == 0 || m.indices.empty()) return;
        ImpellerDrawBatch batch;
        batch.vertices.resize(vc);
        batch.indices = m.indices;
        const float kInv255 = 1.0f / 255.0f;
        const float* pp = m.positions.data();
        const uint8_t* cp = m.coverage.data();
        ImpellerVertex* vp = batch.vertices.data();
        const float tm11 = transformIn.m11, tm21 = transformIn.m21, tdx = transformIn.dx;
        const float tm12 = transformIn.m12, tm22 = transformIn.m22, tdy = transformIn.dy;
        float minX =  std::numeric_limits<float>::infinity();
        float minY =  std::numeric_limits<float>::infinity();
        float maxX = -std::numeric_limits<float>::infinity();
        float maxY = -std::numeric_limits<float>::infinity();
        for (size_t i = 0; i < vc; ++i) {
            float lx = pp[i * 2], ly = pp[i * 2 + 1];
            float x = tm11 * lx + tm21 * ly + tdx;
            float y = tm12 * lx + tm22 * ly + tdy;
            float cov = (float)cp[i] * kInv255;
            vp[i].x = x; vp[i].y = y;
            if (isGradientStroke) {
                // Sample in source space (the brush gradient geometry is authored
                // in path space, same as the cached mesh positions).
                GradientColor gc = SampleBrushGradient(brush, gradStopData.data(), lx, ly);
                float a = gc.a * cov;                 // premultiplied vertex color
                vp[i].r = gc.r * a; vp[i].g = gc.g * a;
                vp[i].b = gc.b * a; vp[i].a = a;
            } else {
                vp[i].r = br * cov; vp[i].g = bg * cov;
                vp[i].b = bb * cov; vp[i].a = ba * cov;
            }
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        batch.pipelineType = 0;
        PushBatchWithCoverage(std::move(batch), minX, minY, maxX, maxY);
        encodedPathCount_++;
    };

    if (auto cached = StrokeCacheFind(key)) {
        path_stats::AddStrokeHit(cached->positions.size() / 2);
        if (cached->positions.empty()) return false;
        emitLocalMesh(*cached);
        return true;
    }
    path_stats::AddStrokeMiss();

    // Miss: flatten the raw commands in SOURCE space (tolerance scaled by
    // 1/maxScale so on-screen smoothness matches this scale bucket — same
    // contract EncodeFillPath uses) and expand the stroke at source-unit
    // width into a binary feathered mesh.
    const float srcTol = (maxScale > 0.001f)
        ? flattenTolerance_ / maxScale : flattenTolerance_;
    std::vector<Contour> contours;
    {
        path_stats::ScopedFlattenTimer flattenTimer(commandLength);
        contours = FlattenPathToContours(startX, startY, commands,
                                         commandLength, srcTol);
        uint64_t ov = 0;
        for (const auto& c : contours) ov += c.VertexCount();
        flattenTimer.RecordOutputVerts(ov);
    }

    auto join = static_cast<ImpellerJoin>(lineJoin);
    auto cap  = static_cast<ImpellerCap>(lineCap);
    std::vector<ImpellerVertex> meshVerts;
    std::vector<uint32_t>       meshIndices;
    meshVerts.reserve(contours.size() * 64);
    meshIndices.reserve(contours.size() * 96);
    // The cached mesh is emitted in SOURCE space and transformed to pixels at
    // PushBatch time, so a "1 pixel" feather skirt must be sized in source
    // units as 1/maxScale. Without this, the feather would be 1 source-unit
    // wide → after a 2× transform it becomes a 2-px AA ring, fattening the
    // stroke to ~strokeWidth+3px on screen.
    const float featherSrcUnit = (maxScale > 1e-4f) ? (1.0f / maxScale) : 1.0f;
    for (auto& c : contours) {
        if (c.VertexCount() < 2) continue;
        jalium::ExpandStrokePath<ImpellerVertex>(
            meshVerts, meshIndices,
            c.points.data(), c.VertexCount(),
            strokeWidth, join, miterLimit, cap, closed,
            // Build the cached mesh with an OPAQUE reference color so the stored
            // coverage is the pure feather geometry, independent of brush alpha.
            // The real color (solid or per-vertex gradient) is applied at emit.
            // Without this, a transparent-first-stop gradient (or a stroke drawn
            // under opacity 0) would bake alpha 0 into the mesh and — because the
            // stroke cache key excludes the brush — poison the geometry-keyed
            // entry, making every later stroke of the same shape vanish too.
            1.0f, 1.0f, 1.0f, 1.0f,
            /*collectContours*/ nullptr,
            featherSrcUnit);
    }

    auto entry = std::make_shared<CachedStrokeRects>();
    if (meshVerts.empty() || meshIndices.empty()) {
        StrokeCacheInsert(key, entry);   // negative cache: empty result
        return false;
    }
    entry->positions.resize(meshVerts.size() * 2);
    entry->coverage.resize(meshVerts.size());
    float minX =  std::numeric_limits<float>::infinity();
    float minY =  std::numeric_limits<float>::infinity();
    float maxX = -std::numeric_limits<float>::infinity();
    float maxY = -std::numeric_limits<float>::infinity();
    const float invBrushA = 1.0f;   // mesh built with reference alpha 1 → coverage is the raw feather
    for (size_t i = 0; i < meshVerts.size(); ++i) {
        const auto& v = meshVerts[i];
        entry->positions[i * 2]     = v.x;
        entry->positions[i * 2 + 1] = v.y;
        float cov = v.a * invBrushA;
        if (cov < 0.0f) cov = 0.0f; else if (cov > 1.0f) cov = 1.0f;
        entry->coverage[i] = (uint8_t)std::lround(cov * 255.0f);
        if (v.x < minX) minX = v.x;
        if (v.y < minY) minY = v.y;
        if (v.x > maxX) maxX = v.x;
        if (v.y > maxY) maxY = v.y;
    }
    entry->indices = std::move(meshIndices);
    entry->bboxL = minX; entry->bboxT = minY;
    entry->bboxR = maxX; entry->bboxB = maxY;
    StrokeCacheInsert(key, entry);
    emitLocalMesh(*entry);
    return true;
}

bool ImpellerD3D12Engine::EncodeStrokePathPixelCached(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    float strokeWidth, bool closed,
    int32_t lineJoin, float miterLimit,
    int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    const EngineTransform& transformIn,
    int32_t edgeMode)
{
    // Resolve edge mode. D3D12 stroke default = binary mesh + vertex
    // feather AA (vertex-stage analytic coverage via outer-skirt verts at
    // alpha=0 — GPU's bilinear-on-color does the per-pixel coverage
    // interpolation for free). ~300 verts per stroke vs ~6000 for the
    // full RasterizePathToRects path; ≥4× lower CPU + steady GPU cost.
    // edgeMode == 2 (explicit Antialiased) opts into the full analytic
    // RasterizePathToRects rect-list for the highest quality (preferred
    // for non-animated icons).
    if (edgeMode < 0) edgeMode = 1 /* Aliased = binary mesh + feather */;

    // ───────────────────────────────────────────────────────────────────
    // Quantize transform.dx/dy to 1/8 pixel to maximize cache sharing.
    //
    // Earlier integer-only stripping failed on DPI=1.5 (and 1.25 / 1.75)
    // setups: an integer-DIP ox like 10 becomes pixel et.dx=15 (integer)
    // but ox=11 becomes 16.5 (fractional), so half the controls hit the
    // slow non-integer fall-through and miss cache. Hit rate stayed low,
    // and the extra PushTransform/PopTransform cost made things WORSE.
    //
    // Fix: ALWAYS strip dx/dy. Quantize to 1/8 px buckets — fractional
    // bucket goes into the cache key, integer-pixel part is the per-call
    // emit offset. AA correctness within 1/8 px (imperceptible). 100
    // ListBoxItems at any layout-snapped position now share one entry
    // regardless of DPI, while sub-pixel animation still rasterizes
    // correctly per fractional bucket.
    // ───────────────────────────────────────────────────────────────────
    int intDx, intDy;
    EngineTransform transform = transformIn;
    {
        constexpr float kFracQuant = 8.0f;
        constexpr float kInvFracQuant = 1.0f / 8.0f;
        int qDx = (int)std::lround(transformIn.dx * kFracQuant);
        int qDy = (int)std::lround(transformIn.dy * kFracQuant);
        // Floor-mod into [0, 7] so negative qDx is handled too.
        int fracDxBucket = ((qDx % 8) + 8) % 8;
        int fracDyBucket = ((qDy % 8) + 8) % 8;
        intDx = (qDx - fracDxBucket) / 8;
        intDy = (qDy - fracDyBucket) / 8;
        transform.dx = fracDxBucket * kInvFracQuant;
        transform.dy = fracDyBucket * kInvFracQuant;
    }

    // Emits a cached triangle mesh (origin-relative positions + indices) into
    // the last solid-fill batch on batches_ when state matches (in-place
    // coalescing — zero temp batch alloc), or into a freshly emplaced batch
    // otherwise. Per-vertex work is just (read 8 B position, +offset, write
    // 24 B vertex with brush color) — vastly cheaper than the previous
    // PixelRect → 4-vertex expansion. ~200 verts per long bezier stroke vs
    // ~6000 verts under the scanline path → ≥ 30× fewer per-stroke writes.
    auto emitCachedMesh = [this, &brush](const CachedStrokeRects& cached, int sx, int sy) {
        size_t vertexCount = cached.positions.size() / 2;
        size_t indexCount  = cached.indices.size();
        if (vertexCount == 0 || indexCount == 0) return;

        float br = brush.r * brush.a;
        float bg = brush.g * brush.a;
        float bb = brush.b * brush.a;
        float ba = brush.a;
        float fsx = (float)sx, fsy = (float)sy;

        ImpellerDrawBatch* target = nullptr;
        if (!batches_.empty()) {
            auto& last = batches_.back();
            if (last.pipelineType == 0 && last.stencilContours.empty() &&
                last.hasScissor == hasScissor_ &&
                (!hasScissor_ ||
                 (last.scissorL == scissorLeft_ && last.scissorT == scissorTop_ &&
                  last.scissorR == scissorRight_ && last.scissorB == scissorBottom_)) &&
                last.hasRoundedClip == hasRoundedClip_ &&
                (!hasRoundedClip_ ||
                 (last.roundedClipRect[0] == roundedClipRect_[0] && last.roundedClipRect[1] == roundedClipRect_[1] &&
                  last.roundedClipRect[2] == roundedClipRect_[2] && last.roundedClipRect[3] == roundedClipRect_[3] &&
                  last.roundedClipCornerRadii[0] == roundedClipCornerRadii_[0] && last.roundedClipCornerRadii[1] == roundedClipCornerRadii_[1] &&
                  last.roundedClipCornerRadii[2] == roundedClipCornerRadii_[2] && last.roundedClipCornerRadii[3] == roundedClipCornerRadii_[3])))
            {
                target = &last;
            }
        }
        if (target == nullptr) {
            batches_.emplace_back();
            target = &batches_.back();
            target->pipelineType = 0;
            target->hasScissor = hasScissor_;
            if (hasScissor_) {
                target->scissorL = scissorLeft_;
                target->scissorT = scissorTop_;
                target->scissorR = scissorRight_;
                target->scissorB = scissorBottom_;
            }
            target->hasRoundedClip = hasRoundedClip_;
            if (hasRoundedClip_) {
                target->roundedClipRect[0] = roundedClipRect_[0]; target->roundedClipRect[1] = roundedClipRect_[1];
                target->roundedClipRect[2] = roundedClipRect_[2]; target->roundedClipRect[3] = roundedClipRect_[3];
                target->roundedClipCornerRadii[0] = roundedClipCornerRadii_[0]; target->roundedClipCornerRadii[1] = roundedClipCornerRadii_[1];
                target->roundedClipCornerRadii[2] = roundedClipCornerRadii_[2]; target->roundedClipCornerRadii[3] = roundedClipCornerRadii_[3];
            }
        }

        size_t oldV = target->vertices.size();
        size_t oldI = target->indices.size();
        target->vertices.resize(oldV + vertexCount);
        target->indices.resize(oldI + indexCount);

        auto* vp = target->vertices.data() + oldV;
        const auto* pp = cached.positions.data();
        const auto* cp = cached.coverage.empty() ? nullptr : cached.coverage.data();
        const float kCovScale = 1.0f / 255.0f;
        for (size_t i = 0; i < vertexCount; i++) {
            float x = pp[i * 2]     + fsx;
            float y = pp[i * 2 + 1] + fsy;
            // Per-vertex coverage carries the vertex-feather AA mask
            // (outer feather verts = 0, inner solid = 255). Multiply both
            // color and alpha channels because the engine vertex format is
            // premultiplied alpha — covering a 0-alpha edge means both
            // visible color and opacity drop to 0.
            float cov = cp ? (float)cp[i] * kCovScale : 1.0f;
            vp[i].x = x;
            vp[i].y = y;
            vp[i].r = br * cov;
            vp[i].g = bg * cov;
            vp[i].b = bb * cov;
            vp[i].a = ba * cov;
        }

        auto* ip = target->indices.data() + oldI;
        const auto* sip = cached.indices.data();
        uint32_t base = (uint32_t)oldV;
        for (size_t i = 0; i < indexCount; i++) {
            ip[i] = sip[i] + base;
        }

        // Union the cached origin-relative bbox into target's coverage,
        // shifted by the per-call offset.
        float bL = cached.bboxL + fsx;
        float bT = cached.bboxT + fsy;
        float bR = cached.bboxR + fsx;
        float bB = cached.bboxB + fsy;
        if (target->hasCoverage) {
            if (bL < target->coverageL) target->coverageL = bL;
            if (bT < target->coverageT) target->coverageT = bT;
            if (bR > target->coverageR) target->coverageR = bR;
            if (bB > target->coverageB) target->coverageB = bB;
        } else {
            target->hasCoverage = true;
            target->coverageL = bL;
            target->coverageT = bT;
            target->coverageR = bR;
            target->coverageB = bB;
        }
        encodedPathCount_++;
    };

    // EdgeMode dispatch: Antialiased (default) routes through analytic
    // coverage scanline — same algorithm as fill, matches Vulkan stroke;
    // Aliased keeps the binary triangle-mesh fast path for pixel-art icons
    // and one-pixel hairline rulings.
    const bool useAnalytic = (edgeMode != 1 /* Aliased */);

    // Analytic-mode emitter — mirror of EncodeFillPath::emitFillRectsAsBatch.
    // Takes a PixelRect list and emits one 4-vertex quad per rect into the
    // current batch (in-place coalescing into the last solid-fill batch when
    // state matches; new batch otherwise). Per-rect alpha is multiplied into
    // the brush color, producing the analytic coverage edge.
    auto emitStrokeRectsAsBatch = [this, &brush](const std::vector<PixelRect>& rects, int sx, int sy) {
        size_t rectCount = rects.size();
        if (rectCount == 0) return;

        float br = brush.r * brush.a;
        float bg = brush.g * brush.a;
        float bb = brush.b * brush.a;
        float ba = brush.a;
        float fsx = (float)sx, fsy = (float)sy;

        ImpellerDrawBatch* target = nullptr;
        if (!batches_.empty()) {
            auto& last = batches_.back();
            if (last.pipelineType == 0 && last.stencilContours.empty() &&
                last.hasScissor == hasScissor_ &&
                (!hasScissor_ ||
                 (last.scissorL == scissorLeft_ && last.scissorT == scissorTop_ &&
                  last.scissorR == scissorRight_ && last.scissorB == scissorBottom_)) &&
                last.hasRoundedClip == hasRoundedClip_ &&
                (!hasRoundedClip_ ||
                 (last.roundedClipRect[0] == roundedClipRect_[0] && last.roundedClipRect[1] == roundedClipRect_[1] &&
                  last.roundedClipRect[2] == roundedClipRect_[2] && last.roundedClipRect[3] == roundedClipRect_[3] &&
                  last.roundedClipCornerRadii[0] == roundedClipCornerRadii_[0] && last.roundedClipCornerRadii[1] == roundedClipCornerRadii_[1] &&
                  last.roundedClipCornerRadii[2] == roundedClipCornerRadii_[2] && last.roundedClipCornerRadii[3] == roundedClipCornerRadii_[3])))
            {
                target = &last;
            }
        }
        if (target == nullptr) {
            batches_.emplace_back();
            target = &batches_.back();
            target->pipelineType = 0;
            target->hasScissor = hasScissor_;
            if (hasScissor_) {
                target->scissorL = scissorLeft_;
                target->scissorT = scissorTop_;
                target->scissorR = scissorRight_;
                target->scissorB = scissorBottom_;
            }
            target->hasRoundedClip = hasRoundedClip_;
            if (hasRoundedClip_) {
                target->roundedClipRect[0] = roundedClipRect_[0]; target->roundedClipRect[1] = roundedClipRect_[1];
                target->roundedClipRect[2] = roundedClipRect_[2]; target->roundedClipRect[3] = roundedClipRect_[3];
                target->roundedClipCornerRadii[0] = roundedClipCornerRadii_[0]; target->roundedClipCornerRadii[1] = roundedClipCornerRadii_[1];
                target->roundedClipCornerRadii[2] = roundedClipCornerRadii_[2]; target->roundedClipCornerRadii[3] = roundedClipCornerRadii_[3];
            }
        }

        size_t oldV = target->vertices.size();
        size_t oldI = target->indices.size();
        target->vertices.resize(oldV + rectCount * 4);
        target->indices.resize(oldI + rectCount * 6);

        auto* vp = target->vertices.data() + oldV;
        auto* ip = target->indices.data() + oldI;
        const auto* rp = rects.data();
        uint32_t baseVertex = (uint32_t)oldV;

        float minX = std::numeric_limits<float>::infinity();
        float minY = std::numeric_limits<float>::infinity();
        float maxX = -std::numeric_limits<float>::infinity();
        float maxY = -std::numeric_limits<float>::infinity();

        for (size_t i = 0; i < rectCount; i++) {
            const auto& rect = rp[i];
            float x0 = (float)rect.x + fsx;
            float y0 = (float)rect.y + fsy;
            float x1 = x0 + (float)rect.w;
            float y1 = y0 + (float)rect.h;
            float cov = rect.alpha;
            float ra = br * cov;
            float ga = bg * cov;
            float bbA = bb * cov;
            float aa = ba * cov;

            size_t v = i * 4;
            vp[v + 0] = { x0, y0, ra, ga, bbA, aa };
            vp[v + 1] = { x1, y0, ra, ga, bbA, aa };
            vp[v + 2] = { x1, y1, ra, ga, bbA, aa };
            vp[v + 3] = { x0, y1, ra, ga, bbA, aa };

            uint32_t bIdx = baseVertex + (uint32_t)v;
            size_t k = i * 6;
            ip[k + 0] = bIdx;
            ip[k + 1] = bIdx + 1;
            ip[k + 2] = bIdx + 2;
            ip[k + 3] = bIdx;
            ip[k + 4] = bIdx + 2;
            ip[k + 5] = bIdx + 3;

            if (x0 < minX) minX = x0;
            if (y0 < minY) minY = y0;
            if (x1 > maxX) maxX = x1;
            if (y1 > maxY) maxY = y1;
        }

        if (target->hasCoverage) {
            if (minX < target->coverageL) target->coverageL = minX;
            if (minY < target->coverageT) target->coverageT = minY;
            if (maxX > target->coverageR) target->coverageR = maxX;
            if (maxY > target->coverageB) target->coverageB = maxY;
        } else {
            target->hasCoverage = true;
            target->coverageL = minX;
            target->coverageT = minY;
            target->coverageR = maxX;
            target->coverageB = maxY;
        }
        encodedPathCount_++;
    };

    // Cache lookup — same inputs (commands, stroke params, transform with
    // 1/8-px-quantized fractional dx/dy) always produce the same origin-
    // relative geometry. The edgeMode byte in the hash partitions
    // Antialiased (PixelRect list) and Aliased (triangle mesh) entries
    // so they don't poison each other.
    uint64_t cacheKey = HashStrokeInputs(
        startX, startY, commands, commandLength,
        strokeWidth, closed, lineJoin, miterLimit, lineCap,
        dashPattern, dashCount, dashOffset, transform, edgeMode);
    if (useAnalytic) {
        if (auto cached = StrokeAnalyticCacheFind(cacheKey)) {
            path_stats::AddStrokeHit(cached->rects.size());
            if (cached->rects.empty()) return false;
            emitStrokeRectsAsBatch(cached->rects, intDx, intDy);
            return true;
        }
    } else {
        if (auto cached = StrokeCacheFind(cacheKey)) {
            path_stats::AddStrokeHit(cached->positions.size() / 2);
            if (cached->positions.empty()) return false;
            emitCachedMesh(*cached, intDx, intDy);
            return true;
        }
    }
    path_stats::AddStrokeMiss();


    // ------------------------------------------------------------------
    // Pixel-space flattening — mirrors the fix EncodeFillPath already
    // applied (see L1640-1658 for the full rationale). Transforming the
    // raw commands into pixel space BEFORE Wang's-formula subdivision
    // means every Bezier gets exactly the right segment count for its
    // on-screen size. The previous source-space flatten with tolerance
    // scaled by 1/maxScale produced only ~2 segments per arc on
    // Stretch="Uniform" icons (ScrollBar arrows, tab corners, rounded
    // play triangle) — that's what made stroked curves look faceted /
    // "stretched" at small sizes.
    //
    // Because the flattener emits contours directly in pixel space,
    // the per-contour TransformPoint loop that used to follow is gone:
    // flatPoints_ becomes a straight copy of contour points.
    // ------------------------------------------------------------------
    float maxScale = std::max(
        std::sqrt(transform.m11 * transform.m11 + transform.m12 * transform.m12),
        std::sqrt(transform.m21 * transform.m21 + transform.m22 * transform.m22));

    // strokeWidth and dashPattern come in as source-space lengths (e.g.
    // pen.Thickness in managed units), the same space the raw commands
    // live in. Since we now pre-transform commands into pixel space,
    // stroke width and dash segment lengths must be scaled too or the
    // stroked outline will have the right shape but wrong thickness.
    float pxStrokeWidth = strokeWidth * maxScale;
    float pxDashOffset  = dashOffset  * maxScale;
    std::vector<float> pxDashPattern;
    if (dashPattern && dashCount > 0) {
        pxDashPattern.resize(dashCount);
        for (uint32_t d = 0; d < dashCount; ++d) {
            pxDashPattern[d] = dashPattern[d] * maxScale;
        }
    }

    float pxStartX = startX, pxStartY = startY;
    TransformPoint(pxStartX, pxStartY, transform);

    std::vector<float> pxCommands;
    pxCommands.reserve(commandLength);
    {
        uint32_t i = 0;
        while (i < commandLength) {
            int tag = (int)commands[i];
            switch (tag) {
                case 0: { // LineTo: [0, ex, ey]
                    if (i + 2 >= commandLength) { i = commandLength; break; }
                    float x = commands[i + 1], y = commands[i + 2];
                    TransformPoint(x, y, transform);
                    pxCommands.push_back(0.0f);
                    pxCommands.push_back(x);
                    pxCommands.push_back(y);
                    i += 3;
                    break;
                }
                case 1: { // CubicTo: [1, c1x, c1y, c2x, c2y, ex, ey]
                    if (i + 6 >= commandLength) { i = commandLength; break; }
                    float c1x = commands[i + 1], c1y = commands[i + 2];
                    float c2x = commands[i + 3], c2y = commands[i + 4];
                    float ex  = commands[i + 5], ey  = commands[i + 6];
                    TransformPoint(c1x, c1y, transform);
                    TransformPoint(c2x, c2y, transform);
                    TransformPoint(ex,  ey,  transform);
                    pxCommands.push_back(1.0f);
                    pxCommands.push_back(c1x); pxCommands.push_back(c1y);
                    pxCommands.push_back(c2x); pxCommands.push_back(c2y);
                    pxCommands.push_back(ex);  pxCommands.push_back(ey);
                    i += 7;
                    break;
                }
                case 2: { // MoveTo: [2, x, y]
                    if (i + 2 >= commandLength) { i = commandLength; break; }
                    float x = commands[i + 1], y = commands[i + 2];
                    TransformPoint(x, y, transform);
                    pxCommands.push_back(2.0f);
                    pxCommands.push_back(x);
                    pxCommands.push_back(y);
                    i += 3;
                    break;
                }
                case 3: { // QuadTo: [3, cx, cy, ex, ey]
                    if (i + 4 >= commandLength) { i = commandLength; break; }
                    float cx = commands[i + 1], cy = commands[i + 2];
                    float ex = commands[i + 3], ey = commands[i + 4];
                    TransformPoint(cx, cy, transform);
                    TransformPoint(ex, ey, transform);
                    pxCommands.push_back(3.0f);
                    pxCommands.push_back(cx); pxCommands.push_back(cy);
                    pxCommands.push_back(ex); pxCommands.push_back(ey);
                    i += 5;
                    break;
                }
                case 5: { // ClosePath: [5]
                    pxCommands.push_back(5.0f);
                    i += 1;
                    break;
                }
                default:
                    // Tag 4 (ArcTo) is never emitted by managed; unknown
                    // tag → bail out but keep what we've parsed so far.
                    i = commandLength;
                    break;
            }
        }
    }

    float adaptiveTolerance = flattenTolerance_;

    std::vector<Contour> contours;
    {
        path_stats::ScopedFlattenTimer flattenTimer(commandLength);
        contours = FlattenPathToContours(
            pxStartX, pxStartY, pxCommands.data(), (uint32_t)pxCommands.size(),
            adaptiveTolerance);
        uint64_t outputVerts = 0;
        for (const auto& c : contours) outputVerts += c.VertexCount();
        flattenTimer.RecordOutputVerts(outputVerts);
    }

    if (contours.empty()) return false;

    auto join = static_cast<ImpellerJoin>(lineJoin);
    auto cap = static_cast<ImpellerCap>(lineCap);

    // -------------------------------------------------------------
    // Stroke widening with two output modes selected by useAnalytic:
    //
    //   Antialiased (useAnalytic = true, the default):
    //     ExpandStrokePath collects expanded contours into strokeContours.
    //     A subsequent RasterizePathToRects pass produces a PixelRect list
    //     with per-rect alpha coverage — same algorithm and shape as fill.
    //     Smooth edges, identical to Vulkan stroke output.
    //
    //   Aliased (useAnalytic = false):
    //     ExpandStrokePath emits a tessellated triangle mesh directly
    //     (per-segment quads + miter/round joins + caps). ~200 verts for
    //     a long bezier wave; GPU rasterizer fills triangles with a
    //     constant brush color. Sharp binary edges — pixel-art look.
    //
    // Dash patterns accumulate sub-stroke output into the same buffer
    // regardless of mode.
    // -------------------------------------------------------------
    std::vector<Contour> strokeContours;
    std::vector<ImpellerVertex> meshVerts;
    std::vector<uint32_t>       meshIndices;
    if (useAnalytic) {
        strokeContours.reserve(contours.size() * 8);
    } else {
        meshVerts.reserve(contours.size() * 64);
        meshIndices.reserve(contours.size() * 96);
    }

    auto expandSubStroke = [&](uint32_t pointCount, bool subClosed) {
        jalium::ExpandStrokePath<ImpellerVertex>(
            meshVerts, meshIndices,
            flatPoints_.data(), pointCount,
            pxStrokeWidth, join, miterLimit, cap, subClosed,
            brush.r, brush.g, brush.b, brush.a,
            /* collectContours */ useAnalytic ? &strokeContours : nullptr);
    };

    for (auto& c : contours) {
        if (c.VertexCount() < 2) continue;

        flatPoints_ = c.points;

        if (!pxDashPattern.empty()) {
            uint32_t pointCount = (uint32_t)(flatPoints_.size() / 2);
            if (pointCount < 2) continue;

            float totalDashLen = 0;
            for (uint32_t d = 0; d < dashCount; ++d) totalDashLen += pxDashPattern[d];
            if (totalDashLen <= 0) totalDashLen = 1.0f;

            float accum = -pxDashOffset;
            while (accum < 0) accum += totalDashLen;

            uint32_t dashIdx = 0;
            float dashRemain = pxDashPattern[0];
            float temp = accum;
            while (temp > 0 && dashCount > 0) {
                if (temp <= dashRemain) { dashRemain -= temp; temp = 0; }
                else { temp -= dashRemain; dashIdx = (dashIdx + 1) % dashCount; dashRemain = pxDashPattern[dashIdx]; }
            }

            bool isDraw = (dashIdx % 2) == 0;
            std::vector<float> currentSegment;
            std::vector<float> savedFlat = flatPoints_;

            for (uint32_t i = 0; i + 1 < pointCount; ++i) {
                float x0 = savedFlat[i * 2], y0 = savedFlat[i * 2 + 1];
                float x1 = savedFlat[(i + 1) * 2], y1 = savedFlat[(i + 1) * 2 + 1];
                float dx = x1 - x0, dy = y1 - y0;
                float segLen = std::sqrt(dx * dx + dy * dy);
                if (segLen < 1e-6f) continue;

                float consumed = 0;
                while (consumed < segLen) {
                    float canConsume = std::min(dashRemain, segLen - consumed);
                    float t0 = consumed / segLen, t1 = (consumed + canConsume) / segLen;
                    if (isDraw) {
                        if (currentSegment.empty()) { currentSegment.push_back(x0 + dx * t0); currentSegment.push_back(y0 + dy * t0); }
                        currentSegment.push_back(x0 + dx * t1); currentSegment.push_back(y0 + dy * t1);
                    }
                    consumed += canConsume; dashRemain -= canConsume;
                    if (dashRemain <= 1e-6f) {
                        if (isDraw && currentSegment.size() >= 4) {
                            flatPoints_ = std::move(currentSegment);
                            expandSubStroke((uint32_t)(flatPoints_.size() / 2), false);
                        }
                        currentSegment.clear();
                        dashIdx = (dashIdx + 1) % dashCount; dashRemain = pxDashPattern[dashIdx]; isDraw = !isDraw;
                    }
                }
            }
            if (isDraw && currentSegment.size() >= 4) {
                flatPoints_ = std::move(currentSegment);
                expandSubStroke((uint32_t)(flatPoints_.size() / 2), false);
            }
        } else {
            expandSubStroke((uint32_t)(flatPoints_.size() / 2), closed);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Analytic branch: feed strokeContours into RasterizePathToRects
    // (the same analytic-AA scanline rasterizer fill already uses).
    // Cache the resulting PixelRect list so subsequent frames bypass
    // both stroke expansion and rasterization.
    // ──────────────────────────────────────────────────────────────
    if (useAnalytic) {
        if (strokeContours.empty()) {
            StrokeAnalyticCacheInsert(cacheKey, std::make_shared<CachedStrokeAnalyticRects>());
            return false;
        }
        auto cached = std::make_shared<CachedStrokeAnalyticRects>();
        cached->rects.reserve(256);
        // Stroke widening always produces NonZero-fill polygons (cap/join
        // tessellations are convex and don't cross themselves with
        // alternating winding), so we hard-code FillRule::NonZero here.
        RasterizePathToRects(strokeContours, FillRule::NonZero, cached->rects);
        if (cached->rects.empty()) {
            StrokeAnalyticCacheInsert(cacheKey, std::move(cached));
            return false;
        }
        emitStrokeRectsAsBatch(cached->rects, intDx, intDy);
        StrokeAnalyticCacheInsert(cacheKey, std::move(cached));
        return true;
    }

    // ──────────────────────────────────────────────────────────────
    // Aliased branch: cache the raw triangle mesh and emit it as-is.
    // ──────────────────────────────────────────────────────────────
    if (meshVerts.empty() || meshIndices.empty()) {
        StrokeCacheInsert(cacheKey, std::make_shared<CachedStrokeRects>());
        return false;
    }

    auto cached = std::make_shared<CachedStrokeRects>();
    cached->positions.resize(meshVerts.size() * 2);
    cached->coverage.resize(meshVerts.size());
    float minX =  std::numeric_limits<float>::infinity();
    float minY =  std::numeric_limits<float>::infinity();
    float maxX = -std::numeric_limits<float>::infinity();
    float maxY = -std::numeric_limits<float>::infinity();
    const float invBrushA = (brush.a > 0.0f) ? (1.0f / brush.a) : 0.0f;
    for (size_t i = 0; i < meshVerts.size(); i++) {
        const auto& v = meshVerts[i];
        cached->positions[i * 2]     = v.x;
        cached->positions[i * 2 + 1] = v.y;
        // Normalize the vertex alpha back to 0..1 coverage so the cache
        // entry is brush-independent: ExpandStrokePath emitted alpha as
        // `brushA × coverage`, divide to recover the geometry-only mask.
        // Outer feather verts come out as coverage=0, inner solid =1.
        float cov = v.a * invBrushA;
        if (cov < 0.0f) cov = 0.0f; else if (cov > 1.0f) cov = 1.0f;
        cached->coverage[i] = (uint8_t)std::lround(cov * 255.0f);
        if (v.x < minX) minX = v.x;
        if (v.y < minY) minY = v.y;
        if (v.x > maxX) maxX = v.x;
        if (v.y > maxY) maxY = v.y;
    }
    cached->indices = std::move(meshIndices);
    cached->bboxL = minX; cached->bboxT = minY;
    cached->bboxR = maxX; cached->bboxB = maxY;

    StrokeCacheInsert(cacheKey, cached);
    emitCachedMesh(*cached, intDx, intDy);
    return true;
}

// ============================================================================
// EncodeFillPolygon — transform-independent local-space cache (same fix as
// EncodeFillPath). This is THE hot path for straight-line filled figures
// (Path/Shape Data without curves, ScrollBar/RepeatButton glyphs): managed
// DrawPathFigurePolygon → RenderTarget.FillPolygon → here. The legacy body
// (now EncodeFillPolygonScanline) re-ran the full AET scanline rasterizer for
// EVERY polygon EVERY frame with NO cache at all — profiled at ~3.3 ms × 38
// polygons = 127 ms/frame, the dominant "Geometry drawing is laggy" cost.
//
// The incoming points carry the element's stable layout Offset (baked managed-
// side) but NOT scroll/animation — those live in `transform`, applied per
// frame at emit. So hashing the raw points + fillRule + scaleBucket gives a
// frame-stable key: triangulate once, then only an O(N) vertex transform per
// frame. Mirrors VulkanRenderTarget::FillPath / our EncodeFillPath exactly.
// ============================================================================
bool ImpellerD3D12Engine::EncodeFillPolygon(
    const float* points, uint32_t pointCount,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform)
{
    if (pointCount < 3 || !points) return false;

    // Gradient brushes (sampled in path-local space) and the no-cache safety
    // case defer to the legacy per-call scanline rasterizer unchanged.
    if (brush.type == 1 || brush.type == 2 || !pathGeometryCache_) {
        return EncodeFillPolygonScanline(points, pointCount, brush, fillRule,
                                         transform);
    }

    const float maxScale    = MaxScaleFromTransform(transform);
    const uint32_t scaleBkt = ScaleBucketFromMaxScale(maxScale);
    // Hash the raw (pre-transform) point array as the geometry payload.
    const uint64_t key = HashPathInput(points[0], points[1],
                                       points, pointCount * 2u,
                                       (int32_t)fillRule, scaleBkt);

    std::shared_ptr<const CachedPathGeometry> geom;
    if (auto hit = pathGeometryCache_->FindAndTouch(key)) {
        geom = std::move(hit->entry);
        path_stats::AddGeometryHit();
    } else {
        auto fresh = std::make_shared<CachedPathGeometry>();
        fresh->contours.resize(1);
        fresh->contours[0].points.assign(points, points + (size_t)pointCount * 2);
        const int32_t fr = (fillRule == FillRule::NonZero) ? 1 : 0;
        std::vector<float> tri;
        {
            path_stats::ScopedTriangulateTimer triTimer;
            bool ok = TriangulateCompoundPath(fresh->contours, fr, tri)
                      && tri.size() >= 6;
            if (ok) {
                triTimer.MarkOk();
                fresh->localTriangles = std::move(tri);
                fresh->triangulationSucceeded = true;
            }
        }
        pathGeometryCache_->Insert(key, fresh);
        geom = std::move(fresh);
        path_stats::AddGeometryMiss();
    }

    if (!geom->triangulationSucceeded || geom->localTriangles.empty()) {
        // Near-degenerate / self-intersecting: preserve the analytic slow path.
        return EncodeFillPolygonScanline(points, pointCount, brush, fillRule,
                                         transform);
    }

    const float r = brush.r * brush.a;
    const float g = brush.g * brush.a;
    const float b = brush.b * brush.a;
    const float a = brush.a;
    if (a <= 0.0f) return true;

    {
        const auto& lt = geom->localTriangles;       // x,y pairs, 3 per tri
        const uint32_t vc = (uint32_t)(lt.size() / 2);
        ImpellerDrawBatch batch;
        batch.vertices.resize(vc);
        batch.indices.resize(vc);
        const float* pp = lt.data();
        ImpellerVertex* vp = batch.vertices.data();
        uint32_t* ip = batch.indices.data();
        const float tm11 = transform.m11, tm21 = transform.m21, tdx = transform.dx;
        const float tm12 = transform.m12, tm22 = transform.m22, tdy = transform.dy;
        float minX =  std::numeric_limits<float>::infinity();
        float minY =  std::numeric_limits<float>::infinity();
        float maxX = -std::numeric_limits<float>::infinity();
        float maxY = -std::numeric_limits<float>::infinity();
        for (uint32_t i = 0; i < vc; ++i) {
            float lx = pp[i * 2], ly = pp[i * 2 + 1];
            float x = tm11 * lx + tm21 * ly + tdx;
            float y = tm12 * lx + tm22 * ly + tdy;
            vp[i].x = x; vp[i].y = y;
            vp[i].r = r; vp[i].g = g; vp[i].b = b; vp[i].a = a;
            ip[i] = i;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        batch.pipelineType = 0;
        PushBatchWithCoverage(std::move(batch), minX, minY, maxX, maxY);
    }
    EmitContourFeather(geom->contours, transform, r, g, b, a);

    encodedPathCount_++;
    return true;
}

// ============================================================================
// EncodeFillPolygonScanline — legacy per-call pixel-space scanline fill
// (UNCHANGED). Fallback for gradient brushes and polygons EncodeFillPolygon's
// triangulator rejects.
// ============================================================================
bool ImpellerD3D12Engine::EncodeFillPolygonScanline(
    const float* points, uint32_t pointCount,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform)
{
    if (pointCount < 3) return false;

    // Premultiply alpha
    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    // Transform points into pixel space and build a single Contour so we
    // can feed the same AET scanline rasterizer EncodeFillPath uses. This
    // is the entry point ScrollBar / RepeatButton / Path elements with
    // straight-line geometry hit, and the former ear-clipping code here
    // was the actual source of the "triangle corners render but interior
    // has gaps" bug — small integer-aligned triangles cracked at the tip
    // because TriangulatePolygon produced near-degenerate ears that the
    // GPU rasterizer then dropped under the top-left rule.
    std::vector<Contour> contours(1);
    Contour& c = contours[0];
    c.points.reserve(pointCount * 2);
    for (uint32_t i = 0; i < pointCount; ++i) {
        float x = points[i * 2], y = points[i * 2 + 1];
        TransformPoint(x, y, transform);
        c.points.push_back(x);
        c.points.push_back(y);
    }

    std::vector<PixelRect> rects;
    rects.reserve(32);
    RasterizePathToRects(contours, fillRule, rects);

    if (!rects.empty()) {
        ImpellerDrawBatch batch;
        batch.vertices.reserve(rects.size() * 4);
        batch.indices.reserve(rects.size() * 6);
        for (const auto& rect : rects) {
            float x0 = (float)rect.x;
            float y0 = (float)rect.y;
            float x1 = (float)(rect.x + rect.w);
            float y1 = (float)(rect.y + rect.h);
            // Apply analytic coverage to premult-alpha brush color.
            float ra = r * rect.alpha;
            float ga = g * rect.alpha;
            float ba = b * rect.alpha;
            float aa = a * rect.alpha;
            uint32_t base = (uint32_t)batch.vertices.size();
            batch.vertices.push_back({ x0, y0, ra, ga, ba, aa });
            batch.vertices.push_back({ x1, y0, ra, ga, ba, aa });
            batch.vertices.push_back({ x1, y1, ra, ga, ba, aa });
            batch.vertices.push_back({ x0, y1, ra, ga, ba, aa });
            batch.indices.push_back(base);
            batch.indices.push_back(base + 1);
            batch.indices.push_back(base + 2);
            batch.indices.push_back(base);
            batch.indices.push_back(base + 2);
            batch.indices.push_back(base + 3);
        }
        batch.pipelineType = 0;
        PushBatch(std::move(batch));
        encodedPathCount_++;
        return true;
    }

    // Degenerate / sub-pixel polygon — nothing to draw.
    return false;
}

bool ImpellerD3D12Engine::EncodeFillEllipse(
    float cx, float cy, float rx, float ry,
    const EngineBrushData& brush,
    const EngineTransform& transform)
{
    // Premultiply alpha for the solid-fill PSO's premult-alpha blend mode.
    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    // Cross-backend triangle-strip generator (TrigCache-backed quadrant pairs).
    ImpellerDrawBatch batch;
    if (!jalium::GenerateFilledEllipseStrip<ImpellerVertex>(
            batch.vertices, batch.indices,
            cx, cy, rx, ry, r, g, b, a,
            trigCache_, transform)) {
        return false;
    }
    batch.pipelineType = 0;
    // Conservative screen-space AABB from the 4 transformed corners of the
    // ellipse's local bbox. This is at most a √2 over-approximation for
    // rotated ellipses but lets PushBatchWithCoverage skip its own vertex
    // walk and coalesce consecutive FillEllipse calls (46 calls/frame in the
    // gallery sample → 1 D3D12 draw).
    float minX, minY, maxX, maxY;
    {
        const float lx[4] = { cx - rx, cx + rx, cx + rx, cx - rx };
        const float ly[4] = { cy - ry, cy - ry, cy + ry, cy + ry };
        minX =  std::numeric_limits<float>::infinity();
        minY =  std::numeric_limits<float>::infinity();
        maxX = -std::numeric_limits<float>::infinity();
        maxY = -std::numeric_limits<float>::infinity();
        for (int i = 0; i < 4; ++i) {
            float x = transform.m11 * lx[i] + transform.m21 * ly[i] + transform.dx;
            float y = transform.m12 * lx[i] + transform.m22 * ly[i] + transform.dy;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
    }
    PushBatchWithCoverage(std::move(batch), minX, minY, maxX, maxY);
    encodedPathCount_++;
    return true;
}

// ============================================================================
// Stencil-then-Cover (non-convex path fill via GPU stencil buffer)
//
// Flutter Impeller: GeometryResult::Mode::kNonZero / kEvenOdd
// Pass 1: Triangle fan from an arbitrary point through all path edges,
//          incrementing/decrementing stencil (NonZero) or toggling (EvenOdd).
// Pass 2: Draw bounding box quad, discarding pixels where stencil == 0.
// ============================================================================

bool ImpellerD3D12Engine::EnsureStencilResources(uint32_t w, uint32_t h) {
    if (depthStencilBuffer_ && dsvW_ == w && dsvH_ == h) return true;

    // Create depth-stencil buffer
    D3D12_RESOURCE_DESC dsDesc = {};
    dsDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    dsDesc.Width = w;
    dsDesc.Height = h;
    dsDesc.DepthOrArraySize = 1;
    dsDesc.MipLevels = 1;
    dsDesc.Format = DXGI_FORMAT_D24_UNORM_S8_UINT;
    dsDesc.SampleDesc.Count = 1;
    dsDesc.Flags = D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;

    D3D12_HEAP_PROPERTIES heapProps = {};
    heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;

    D3D12_CLEAR_VALUE clearVal = {};
    clearVal.Format = DXGI_FORMAT_D24_UNORM_S8_UINT;
    clearVal.DepthStencil.Depth = 1.0f;
    clearVal.DepthStencil.Stencil = 0;

    if (FAILED(device_->CreateCommittedResource(
            &heapProps, D3D12_HEAP_FLAG_NONE, &dsDesc,
            D3D12_RESOURCE_STATE_DEPTH_WRITE, &clearVal,
            IID_PPV_ARGS(&depthStencilBuffer_))))
        return false;

    // Create DSV heap
    if (!dsvHeap_) {
        D3D12_DESCRIPTOR_HEAP_DESC dsvDesc = {};
        dsvDesc.NumDescriptors = 1;
        dsvDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_DSV;
        if (FAILED(device_->CreateDescriptorHeap(&dsvDesc, IID_PPV_ARGS(&dsvHeap_))))
            return false;
    }

    D3D12_DEPTH_STENCIL_VIEW_DESC dsvViewDesc = {};
    dsvViewDesc.Format = DXGI_FORMAT_D24_UNORM_S8_UINT;
    dsvViewDesc.ViewDimension = D3D12_DSV_DIMENSION_TEXTURE2D;
    device_->CreateDepthStencilView(depthStencilBuffer_.Get(), &dsvViewDesc,
                                     dsvHeap_->GetCPUDescriptorHandleForHeapStart());

    dsvW_ = w;
    dsvH_ = h;

    // Create stencil PSOs if not yet created
    if (!stencilWritePSO_) {
        // Stencil write PSO: no color output, write stencil only
        // For NonZero: front face increments, back face decrements
        D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
        psoDesc.pRootSignature = rootSignature_.Get();

        // Reuse solid fill shaders (we need VS to transform vertices, PS is ignored)
        ComPtr<ID3DBlob> vsBlob, psBlob, errors;
        D3DCompile(
            "cbuffer C:register(b0){float4x4 mvp;};"
            "float4 main(float2 p:POSITION,float4 c:COLOR):SV_POSITION{return mul(mvp,float4(p,0,1));}",
            0, nullptr, nullptr, nullptr, "main", "vs_5_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &vsBlob, &errors);
        D3DCompile("void main(){}", 0, nullptr, nullptr, nullptr, "main", "ps_5_0", 0, 0, &psBlob, &errors);

        if (!vsBlob || !psBlob) return false;

        D3D12_INPUT_ELEMENT_DESC inputElements[] = {
            { "POSITION", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 0, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 },
            { "COLOR", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 8, D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 },
        };

        psoDesc.VS = { vsBlob->GetBufferPointer(), vsBlob->GetBufferSize() };
        psoDesc.PS = { psBlob->GetBufferPointer(), psBlob->GetBufferSize() };
        psoDesc.InputLayout = { inputElements, _countof(inputElements) };
        psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
        psoDesc.NumRenderTargets = 0; // No color output
        psoDesc.DSVFormat = DXGI_FORMAT_D24_UNORM_S8_UINT;
        psoDesc.SampleDesc.Count = 1;
        psoDesc.SampleMask = UINT_MAX;
        psoDesc.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
        psoDesc.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
        psoDesc.RasterizerState.DepthClipEnable = FALSE;

        // Stencil: always pass, increment on front, decrement on back.
        // NonZero winding accumulation MUST wrap (mod-256), never saturate:
        // fan triangles regularly decrement FIRST (contour direction decides
        // which faces rasterize back-facing). With _SAT a decrement at 0 sticks
        // to 0 and the winding count is destroyed — star wedges over-fill,
        // blobs grow straight-edge tails outside the hull (parity harness).
        // INCR/DECR keep the count exact mod 256 (order- and sign-independent;
        // cover tests stencil != 0), matching
        // docs/reference/pure_d3d12_path_renderer.h and the Vulkan side
        // ("_AND_WRAP keeps the count exact" in EnsureStencilCoverResources).
        psoDesc.DepthStencilState.DepthEnable = FALSE;
        psoDesc.DepthStencilState.StencilEnable = TRUE;
        psoDesc.DepthStencilState.StencilReadMask = 0xFF;
        psoDesc.DepthStencilState.StencilWriteMask = 0xFF;
        psoDesc.DepthStencilState.FrontFace.StencilFunc = D3D12_COMPARISON_FUNC_ALWAYS;
        psoDesc.DepthStencilState.FrontFace.StencilPassOp = D3D12_STENCIL_OP_INCR;
        psoDesc.DepthStencilState.FrontFace.StencilFailOp = D3D12_STENCIL_OP_KEEP;
        psoDesc.DepthStencilState.FrontFace.StencilDepthFailOp = D3D12_STENCIL_OP_KEEP;
        psoDesc.DepthStencilState.BackFace.StencilFunc = D3D12_COMPARISON_FUNC_ALWAYS;
        psoDesc.DepthStencilState.BackFace.StencilPassOp = D3D12_STENCIL_OP_DECR;
        psoDesc.DepthStencilState.BackFace.StencilFailOp = D3D12_STENCIL_OP_KEEP;
        psoDesc.DepthStencilState.BackFace.StencilDepthFailOp = D3D12_STENCIL_OP_KEEP;

        // Disable color write
        psoDesc.BlendState.RenderTarget[0].RenderTargetWriteMask = 0;

        if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&stencilWritePSO_))))
            return false;

        // Cover PSO (NonZero): stencil != 0, write color, clear stencil to 0
        psoDesc.NumRenderTargets = 1;
        psoDesc.RTVFormats[0] = rtvFormat_;
        psoDesc.VS = { solidFillPSO_ ? vsBlob->GetBufferPointer() : nullptr,
                       solidFillPSO_ ? vsBlob->GetBufferSize() : 0 };
        // Recompile with color output
        ComPtr<ID3DBlob> psBlobColor;
        D3DCompile(
            "struct I{float4 p:SV_POSITION;float4 c:COLOR;};float4 main(I i):SV_TARGET{return i.c;}",
            0, nullptr, nullptr, nullptr, "main", "ps_5_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &psBlobColor, &errors);
        if (!psBlobColor) return false;
        psoDesc.PS = { psBlobColor->GetBufferPointer(), psBlobColor->GetBufferSize() };

        psoDesc.DepthStencilState.FrontFace.StencilFunc = D3D12_COMPARISON_FUNC_NOT_EQUAL;
        psoDesc.DepthStencilState.FrontFace.StencilPassOp = D3D12_STENCIL_OP_ZERO; // Clear stencil
        psoDesc.DepthStencilState.BackFace = psoDesc.DepthStencilState.FrontFace;

        // Enable color write + blending
        psoDesc.BlendState.RenderTarget[0].BlendEnable = TRUE;
        psoDesc.BlendState.RenderTarget[0].SrcBlend = D3D12_BLEND_ONE;
        psoDesc.BlendState.RenderTarget[0].DestBlend = D3D12_BLEND_INV_SRC_ALPHA;
        psoDesc.BlendState.RenderTarget[0].BlendOp = D3D12_BLEND_OP_ADD;
        psoDesc.BlendState.RenderTarget[0].SrcBlendAlpha = D3D12_BLEND_ONE;
        psoDesc.BlendState.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
        psoDesc.BlendState.RenderTarget[0].BlendOpAlpha = D3D12_BLEND_OP_ADD;
        psoDesc.BlendState.RenderTarget[0].RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;

        if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&stencilCoverNonZeroPSO_))))
            return false;

        // Cover PSO (EvenOdd): stencil bit 0 == 1
        psoDesc.DepthStencilState.StencilReadMask = 0x01;
        psoDesc.DepthStencilState.FrontFace.StencilFunc = D3D12_COMPARISON_FUNC_NOT_EQUAL;
        psoDesc.DepthStencilState.FrontFace.StencilPassOp = D3D12_STENCIL_OP_ZERO;
        psoDesc.DepthStencilState.BackFace = psoDesc.DepthStencilState.FrontFace;

        if (FAILED(device_->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&stencilCoverEvenOddPSO_))))
            return false;
    }

    return true;
}

bool ImpellerD3D12Engine::StencilThenCoverFill(
    const std::vector<Contour>& contours,
    FillRule fillRule,
    float r, float g, float b, float a,
    ID3D12GraphicsCommandList* cmdList,
    D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle,
    uint32_t viewportW, uint32_t viewportH)
{
    if (!EnsureStencilResources(viewportW, viewportH)) return false;

    // Build triangle fan from centroid through all contour edges
    // This is the stencil-fill geometry
    std::vector<ImpellerVertex> stencilVerts;
    std::vector<uint32_t> stencilIndices;

    for (auto& c : contours) {
        uint32_t pc = c.VertexCount();
        if (pc < 3) continue;

        // Use first vertex as fan hub
        uint32_t hubIdx = (uint32_t)stencilVerts.size();
        for (uint32_t i = 0; i < pc; ++i) {
            stencilVerts.push_back({ c.X(i), c.Y(i), 0, 0, 0, 0 }); // color doesn't matter for stencil
        }
        for (uint32_t i = 1; i + 1 < pc; ++i) {
            stencilIndices.push_back(hubIdx);
            stencilIndices.push_back(hubIdx + i);
            stencilIndices.push_back(hubIdx + i + 1);
        }
    }

    if (stencilIndices.empty()) return false;

    // Compute bounding box for cover quad
    float minX = 1e9f, minY = 1e9f, maxX = -1e9f, maxY = -1e9f;
    for (auto& v : stencilVerts) {
        minX = std::min(minX, v.x); minY = std::min(minY, v.y);
        maxX = std::max(maxX, v.x); maxY = std::max(maxY, v.y);
    }

    // Upload stencil vertices to dedicated stencil upload buffer
    // (avoids overwriting solid batch data in the main upload buffers)
    size_t stencilVBBytes = stencilVerts.size() * sizeof(ImpellerVertex);
    size_t stencilIBBytes = stencilIndices.size() * sizeof(uint32_t);
    size_t coverVBBytes = 6 * sizeof(ImpellerVertex);
    if (!EnsureStencilVertexBuffer(stencilVBBytes + coverVBBytes)) return false;
    if (!EnsureStencilIndexBuffer(stencilIBBytes + 6 * sizeof(uint32_t))) return false;

    // Map and upload
    {
        void* mapped = nullptr;
        D3D12_RANGE readRange = { 0, 0 };
        stencilVertexUploadBuffer_->Map(0, &readRange, &mapped);
        memcpy(mapped, stencilVerts.data(), stencilVBBytes);
        ImpellerVertex coverVerts[6] = {
            { minX, minY, r, g, b, a }, { maxX, minY, r, g, b, a }, { maxX, maxY, r, g, b, a },
            { minX, minY, r, g, b, a }, { maxX, maxY, r, g, b, a }, { minX, maxY, r, g, b, a },
        };
        memcpy((uint8_t*)mapped + stencilVBBytes, coverVerts, coverVBBytes);
        stencilVertexUploadBuffer_->Unmap(0, nullptr);
    }
    {
        void* mapped = nullptr;
        D3D12_RANGE readRange = { 0, 0 };
        stencilIndexUploadBuffer_->Map(0, &readRange, &mapped);
        memcpy(mapped, stencilIndices.data(), stencilIBBytes);
        uint32_t coverBase = (uint32_t)stencilVerts.size();
        uint32_t coverIdx[6] = { coverBase, coverBase + 1, coverBase + 2,
                                  coverBase + 3, coverBase + 4, coverBase + 5 };
        memcpy((uint8_t*)mapped + stencilIBBytes, coverIdx, sizeof(coverIdx));
        stencilIndexUploadBuffer_->Unmap(0, nullptr);
    }

    D3D12_CPU_DESCRIPTOR_HANDLE dsvHandle = dsvHeap_->GetCPUDescriptorHandleForHeapStart();

    // Clear stencil to 0
    cmdList->ClearDepthStencilView(dsvHandle, D3D12_CLEAR_FLAG_STENCIL, 1.0f, 0, 0, nullptr);

    D3D12_VIEWPORT viewport = {};
    viewport.Width = (float)viewportW;
    viewport.Height = (float)viewportH;
    viewport.MaxDepth = 1.0f;
    cmdList->RSSetViewports(1, &viewport);

    D3D12_RECT scissor = { 0, 0, (LONG)viewportW, (LONG)viewportH };
    cmdList->RSSetScissorRects(1, &scissor);

    float mvp[16] = {
        2.0f / viewportW, 0, 0, 0,
        0, -2.0f / viewportH, 0, 0,
        0, 0, 1, 0,
        -1.0f, 1.0f, 0, 1
    };

    // ---- Pass 1: Write stencil (no color) ----
    cmdList->OMSetRenderTargets(0, nullptr, FALSE, &dsvHandle);
    cmdList->SetGraphicsRootSignature(rootSignature_.Get());
    cmdList->SetPipelineState(stencilWritePSO_.Get());
    cmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    cmdList->SetGraphicsRoot32BitConstants(0, 16, mvp, 0);
    cmdList->OMSetStencilRef(0);

    D3D12_VERTEX_BUFFER_VIEW vbv = {};
    vbv.BufferLocation = stencilVertexUploadBuffer_->GetGPUVirtualAddress();
    vbv.SizeInBytes = (UINT)stencilVBBytes;
    vbv.StrideInBytes = sizeof(ImpellerVertex);
    cmdList->IASetVertexBuffers(0, 1, &vbv);

    D3D12_INDEX_BUFFER_VIEW ibv = {};
    ibv.BufferLocation = stencilIndexUploadBuffer_->GetGPUVirtualAddress();
    ibv.SizeInBytes = (UINT)stencilIBBytes;
    ibv.Format = DXGI_FORMAT_R32_UINT;
    cmdList->IASetIndexBuffer(&ibv);

    cmdList->DrawIndexedInstanced((UINT)stencilIndices.size(), 1, 0, 0, 0);

    // ---- Pass 2: Cover bounding box, reading stencil ----
    cmdList->OMSetRenderTargets(1, &rtvHandle, FALSE, &dsvHandle);
    cmdList->SetPipelineState(fillRule == FillRule::NonZero
                              ? stencilCoverNonZeroPSO_.Get()
                              : stencilCoverEvenOddPSO_.Get());
    cmdList->SetGraphicsRoot32BitConstants(0, 16, mvp, 0);
    cmdList->OMSetStencilRef(0);

    // Cover quad VB/IB
    D3D12_VERTEX_BUFFER_VIEW cvbv = {};
    cvbv.BufferLocation = stencilVertexUploadBuffer_->GetGPUVirtualAddress() + stencilVBBytes;
    cvbv.SizeInBytes = (UINT)coverVBBytes;
    cvbv.StrideInBytes = sizeof(ImpellerVertex);
    cmdList->IASetVertexBuffers(0, 1, &cvbv);

    D3D12_INDEX_BUFFER_VIEW civbv = {};
    civbv.BufferLocation = stencilIndexUploadBuffer_->GetGPUVirtualAddress() + stencilIBBytes;
    civbv.SizeInBytes = 6 * sizeof(uint32_t);
    civbv.Format = DXGI_FORMAT_R32_UINT;
    cmdList->IASetIndexBuffer(&civbv);

    cmdList->DrawIndexedInstanced(6, 1, 0, 0, 0);

    // Unbind DSV so subsequent draws don't use stencil
    cmdList->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    return true;
}

// ============================================================================
// GPU Execution
// ============================================================================

bool ImpellerD3D12Engine::Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) {
    if (batches_.empty()) return true;

    auto* cmdList = static_cast<ID3D12GraphicsCommandList*>(commandList);

    if (!EnsureOutputTexture(width, height)) return false;

    // Calculate total vertex and index data sizes (solid batches only)
    size_t totalVertexBytes = 0;
    size_t totalIndexBytes = 0;
    for (auto& batch : batches_) {
        if (batch.pipelineType == 1) continue;
        totalVertexBytes += batch.vertices.size() * sizeof(ImpellerVertex);
        totalIndexBytes += batch.indices.size() * sizeof(uint32_t);
    }

    if (totalVertexBytes > 0 && totalIndexBytes > 0) {
        if (!EnsureVertexBuffer(totalVertexBytes)) return false;
        if (!EnsureIndexBuffer(totalIndexBytes)) return false;

        // Upload vertex data
        void* mappedVB = nullptr;
        D3D12_RANGE readRange = { 0, 0 };
        vertexUploadBuffer_->Map(0, &readRange, &mappedVB);
        size_t vbOffset = 0;
        for (auto& batch : batches_) {
            if (batch.pipelineType == 1) continue;
            size_t bytes = batch.vertices.size() * sizeof(ImpellerVertex);
            memcpy((uint8_t*)mappedVB + vbOffset, batch.vertices.data(), bytes);
            vbOffset += bytes;
        }
        vertexUploadBuffer_->Unmap(0, nullptr);

        // Upload index data
        void* mappedIB = nullptr;
        indexUploadBuffer_->Map(0, &readRange, &mappedIB);
        size_t ibOffset = 0;
        for (auto& batch : batches_) {
            if (batch.pipelineType == 1) continue;
            size_t bytes = batch.indices.size() * sizeof(uint32_t);
            memcpy((uint8_t*)mappedIB + ibOffset, batch.indices.data(), bytes);
            ibOffset += bytes;
        }
        indexUploadBuffer_->Unmap(0, nullptr);

        // Copy upload → GPU buffers
        D3D12_RESOURCE_BARRIER barriers[2] = {};
        barriers[0].Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barriers[0].Transition.pResource = vertexBuffer_.Get();
        barriers[0].Transition.StateBefore = D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER;
        barriers[0].Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
        barriers[0].Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;

        barriers[1].Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barriers[1].Transition.pResource = indexBuffer_.Get();
        barriers[1].Transition.StateBefore = D3D12_RESOURCE_STATE_INDEX_BUFFER;
        barriers[1].Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
        barriers[1].Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;

        cmdList->ResourceBarrier(2, barriers);

        cmdList->CopyBufferRegion(vertexBuffer_.Get(), 0, vertexUploadBuffer_.Get(), 0, totalVertexBytes);
        cmdList->CopyBufferRegion(indexBuffer_.Get(), 0, indexUploadBuffer_.Get(), 0, totalIndexBytes);

        barriers[0].Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
        barriers[0].Transition.StateAfter = D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER;
        barriers[1].Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
        barriers[1].Transition.StateAfter = D3D12_RESOURCE_STATE_INDEX_BUFFER;

        cmdList->ResourceBarrier(2, barriers);
    }

    // Set render target
    D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle = rtvHeap_->GetCPUDescriptorHandleForHeapStart();

    // Clear output texture
    float clearColor[4] = { 0, 0, 0, 0 };
    cmdList->ClearRenderTargetView(rtvHandle, clearColor, 0, nullptr);
    cmdList->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    // Set viewport and scissor
    D3D12_VIEWPORT viewport = {};
    viewport.Width = (float)width;
    viewport.Height = (float)height;
    viewport.MaxDepth = 1.0f;
    cmdList->RSSetViewports(1, &viewport);

    D3D12_RECT scissorRect = {};
    if (hasScissor_) {
        scissorRect.left = (LONG)scissorLeft_;
        scissorRect.top = (LONG)scissorTop_;
        scissorRect.right = (LONG)scissorRight_;
        scissorRect.bottom = (LONG)scissorBottom_;
    } else {
        scissorRect.right = (LONG)width;
        scissorRect.bottom = (LONG)height;
    }
    cmdList->RSSetScissorRects(1, &scissorRect);

    // Set pipeline and root signature
    cmdList->SetGraphicsRootSignature(rootSignature_.Get());
    cmdList->SetPipelineState(solidFillPSO_.Get());
    cmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

    // Set orthographic projection matrix as root constants
    float mvp[16] = {
        2.0f / width,  0,               0, 0,
        0,            -2.0f / height,    0, 0,
        0,             0,               1, 0,
        -1.0f,         1.0f,            0, 1
    };
    cmdList->SetGraphicsRoot32BitConstants(0, 16, mvp, 0);

    // Draw all batches
    size_t vbDrawOffset = 0;
    size_t ibDrawOffset = 0;

    for (auto& batch : batches_) {
        // Stencil-then-cover batch
        if (batch.pipelineType == 1) {
            if (!batch.stencilContours.empty()) {
                StencilThenCoverFill(
                    batch.stencilContours,
                    batch.stencilFillRule,
                    batch.stencilR, batch.stencilG, batch.stencilB, batch.stencilA,
                    cmdList, rtvHandle, width, height);

                // Restore solid fill pipeline state
                cmdList->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);
                cmdList->RSSetViewports(1, &viewport);
                cmdList->RSSetScissorRects(1, &scissorRect);
                cmdList->SetGraphicsRootSignature(rootSignature_.Get());
                cmdList->SetPipelineState(solidFillPSO_.Get());
                cmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
                cmdList->SetGraphicsRoot32BitConstants(0, 16, mvp, 0);
            }
            continue;
        }

        D3D12_VERTEX_BUFFER_VIEW vbv = {};
        vbv.BufferLocation = vertexBuffer_->GetGPUVirtualAddress() + vbDrawOffset;
        vbv.SizeInBytes = (UINT)(batch.vertices.size() * sizeof(ImpellerVertex));
        vbv.StrideInBytes = sizeof(ImpellerVertex);
        cmdList->IASetVertexBuffers(0, 1, &vbv);

        D3D12_INDEX_BUFFER_VIEW ibv = {};
        ibv.BufferLocation = indexBuffer_->GetGPUVirtualAddress() + ibDrawOffset;
        ibv.SizeInBytes = (UINT)(batch.indices.size() * sizeof(uint32_t));
        ibv.Format = DXGI_FORMAT_R32_UINT;
        cmdList->IASetIndexBuffer(&ibv);

        cmdList->DrawIndexedInstanced((UINT)batch.indices.size(), 1, 0, 0, 0);

        vbDrawOffset += batch.vertices.size() * sizeof(ImpellerVertex);
        ibDrawOffset += batch.indices.size() * sizeof(uint32_t);
    }

    return true;
}

bool ImpellerD3D12Engine::ExecuteOnCommandList(
    ID3D12GraphicsCommandList* cmdList,
    D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle,
    D3D12_RECT scissor,
    uint32_t viewportW, uint32_t viewportH)
{
    if (batches_.empty()) return true;

    // Separate solid batches from stencil batches
    bool hasSolidBatches = false;
    bool hasStencilBatches = false;
    for (auto& batch : batches_) {
        if (batch.pipelineType == 1) hasStencilBatches = true;
        else hasSolidBatches = true;
    }

    // Calculate total data sizes for solid batches only
    size_t totalVertexBytes = 0;
    size_t totalIndexBytes = 0;
    for (auto& batch : batches_) {
        if (batch.pipelineType == 1) continue; // stencil batches have no CPU vertices
        totalVertexBytes += batch.vertices.size() * sizeof(ImpellerVertex);
        totalIndexBytes += batch.indices.size() * sizeof(uint32_t);
    }

    if (hasSolidBatches && totalVertexBytes > 0 && totalIndexBytes > 0) {
        if (!EnsureVertexBuffer(totalVertexBytes)) return false;
        if (!EnsureIndexBuffer(totalIndexBytes)) return false;

        // Upload vertex data directly to upload heap
        {
            void* mapped = nullptr;
            D3D12_RANGE readRange = { 0, 0 };
            if (FAILED(vertexUploadBuffer_->Map(0, &readRange, &mapped))) return false;
            size_t offset = 0;
            for (auto& batch : batches_) {
                if (batch.pipelineType == 1) continue;
                size_t bytes = batch.vertices.size() * sizeof(ImpellerVertex);
                memcpy((uint8_t*)mapped + offset, batch.vertices.data(), bytes);
                offset += bytes;
            }
            vertexUploadBuffer_->Unmap(0, nullptr);
        }

        // Upload index data
        {
            void* mapped = nullptr;
            D3D12_RANGE readRange = { 0, 0 };
            if (FAILED(indexUploadBuffer_->Map(0, &readRange, &mapped))) return false;
            size_t offset = 0;
            for (auto& batch : batches_) {
                if (batch.pipelineType == 1) continue;
                size_t bytes = batch.indices.size() * sizeof(uint32_t);
                memcpy((uint8_t*)mapped + offset, batch.indices.data(), bytes);
                offset += bytes;
            }
            indexUploadBuffer_->Unmap(0, nullptr);
        }
    }

    // Bind Impeller PSO + root signature directly on the caller's command list
    cmdList->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);

    D3D12_VIEWPORT viewport = {};
    viewport.Width = (float)viewportW;
    viewport.Height = (float)viewportH;
    viewport.MaxDepth = 1.0f;
    cmdList->RSSetViewports(1, &viewport);
    cmdList->RSSetScissorRects(1, &scissor);

    cmdList->SetGraphicsRootSignature(rootSignature_.Get());
    cmdList->SetPipelineState(solidFillPSO_.Get());
    cmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

    // Orthographic projection: pixel space → clip space
    float w = (float)viewportW, h = (float)viewportH;
    float mvp[16] = {
        2.0f / w,  0,          0, 0,
        0,        -2.0f / h,   0, 0,
        0,         0,          1, 0,
        -1.0f,     1.0f,       0, 1
    };
    cmdList->SetGraphicsRoot32BitConstants(0, 16, mvp, 0);

    // Default scissor (full viewport)
    D3D12_RECT defaultScissor = scissor;

    // Draw each batch
    size_t vbDrawOffset = 0;
    size_t ibDrawOffset = 0;

    for (auto& batch : batches_) {
        // Compute the effective scissor for this batch:
        //   effective = viewport ∩ user_scissor ∩ tile_coverage
        // Coverage is the screen-space AABB of the batch's geometry, captured
        // at PushBatch time. This mirrors Flutter Impeller's per-entity coverage
        // which lets the rasterizer skip pixels the draw cannot possibly touch.
        D3D12_RECT effective = defaultScissor;
        if (batch.hasScissor) {
            effective.left   = std::max(effective.left,   (LONG)batch.scissorL);
            effective.top    = std::max(effective.top,    (LONG)batch.scissorT);
            effective.right  = std::min(effective.right,  (LONG)batch.scissorR);
            effective.bottom = std::min(effective.bottom, (LONG)batch.scissorB);
        }
        if (batch.hasCoverage) {
            // Floor/ceil to integer pixels and pad by 1px to absorb any
            // rasterization fill-rule rounding at the edges.
            LONG cl = (LONG)std::floor(batch.coverageL) - 1;
            LONG ct = (LONG)std::floor(batch.coverageT) - 1;
            LONG cr = (LONG)std::ceil (batch.coverageR) + 1;
            LONG cb = (LONG)std::ceil (batch.coverageB) + 1;
            effective.left   = std::max(effective.left,   cl);
            effective.top    = std::max(effective.top,    ct);
            effective.right  = std::min(effective.right,  cr);
            effective.bottom = std::min(effective.bottom, cb);
        }

        // Cull empty intersection — batch contributes no pixels.
        if (effective.right <= effective.left || effective.bottom <= effective.top) {
            if (batch.pipelineType != 1) {
                vbDrawOffset += batch.vertices.size() * sizeof(ImpellerVertex);
                ibDrawOffset += batch.indices.size() * sizeof(uint32_t);
            }
            continue;
        }

        cmdList->RSSetScissorRects(1, &effective);

        // Stencil-then-cover batch: delegate to GPU stencil path
        if (batch.pipelineType == 1) {
            if (!batch.stencilContours.empty()) {
                StencilThenCoverFill(
                    batch.stencilContours,
                    batch.stencilFillRule,
                    batch.stencilR, batch.stencilG, batch.stencilB, batch.stencilA,
                    cmdList, rtvHandle, viewportW, viewportH);

                // Restore solid fill pipeline state after stencil pass
                cmdList->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);
                cmdList->RSSetViewports(1, &viewport);
                cmdList->RSSetScissorRects(1, &defaultScissor);
                cmdList->SetGraphicsRootSignature(rootSignature_.Get());
                cmdList->SetPipelineState(solidFillPSO_.Get());
                cmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
                cmdList->SetGraphicsRoot32BitConstants(0, 16, mvp, 0);
            }
            continue;
        }

        if (batch.indices.empty()) {
            vbDrawOffset += batch.vertices.size() * sizeof(ImpellerVertex);
            continue;
        }

        D3D12_VERTEX_BUFFER_VIEW vbv = {};
        vbv.BufferLocation = vertexUploadBuffer_->GetGPUVirtualAddress() + vbDrawOffset;
        vbv.SizeInBytes = (UINT)(batch.vertices.size() * sizeof(ImpellerVertex));
        vbv.StrideInBytes = sizeof(ImpellerVertex);
        cmdList->IASetVertexBuffers(0, 1, &vbv);

        D3D12_INDEX_BUFFER_VIEW ibv = {};
        ibv.BufferLocation = indexUploadBuffer_->GetGPUVirtualAddress() + ibDrawOffset;
        ibv.SizeInBytes = (UINT)(batch.indices.size() * sizeof(uint32_t));
        ibv.Format = DXGI_FORMAT_R32_UINT;
        cmdList->IASetIndexBuffer(&ibv);

        cmdList->DrawIndexedInstanced((UINT)batch.indices.size(), 1, 0, 0, 0);

        vbDrawOffset += batch.vertices.size() * sizeof(ImpellerVertex);
        ibDrawOffset += batch.indices.size() * sizeof(uint32_t);
    }

    batches_.clear();
    return true;
}

bool ImpellerD3D12Engine::HasPendingWork() const {
    return !batches_.empty();
}

uint32_t ImpellerD3D12Engine::GetEncodedPathCount() const {
    return encodedPathCount_;
}

} // namespace jalium
