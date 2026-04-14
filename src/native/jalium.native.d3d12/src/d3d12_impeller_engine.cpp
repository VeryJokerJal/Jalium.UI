#include "d3d12_impeller_engine.h"
#include <cstring>
#include <cmath>
#include <algorithm>
#include <chrono>
#include <d3dcompiler.h>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace jalium {

// ============================================================================
// TrigCache — Precomputed trig table (Flutter Impeller: Trigs class)
// ============================================================================

TrigCache::TrigCache() : cache_(kCachedCount + 1) {}

void TrigCache::Ensure(size_t divisions) const {
    if (divisions >= cache_.size()) cache_.resize(divisions + 1);
    if (!cache_[divisions].empty()) return;

    auto& v = cache_[divisions];
    v.reserve(divisions + 1);
    double angleScale = (M_PI / 2.0) / divisions;
    v.push_back({ 1.0f, 0.0f });
    for (size_t i = 1; i < divisions; ++i) {
        double a = i * angleScale;
        v.push_back({ (float)std::cos(a), (float)std::sin(a) });
    }
    v.push_back({ 0.0f, 1.0f });
}

const std::vector<Trig>& TrigCache::Get(size_t divisions) const {
    Ensure(divisions);
    return cache_[divisions];
}

size_t TrigCache::ComputeDivisions(float pixelRadius) {
    if (pixelRadius <= 0.0f) return 1;
    float k = kCircleTolerance / pixelRadius;
    if (k >= 1.0f) return 1;
    return (size_t)std::ceil((float)(M_PI / 4.0) / std::acos(1.0f - k));
}

// ============================================================================
// Convex Detection (Flutter Impeller: Path::IsConvex)
// ============================================================================

bool ImpellerD3D12Engine::IsConvexPolygon(const float* points, uint32_t pointCount) {
    if (pointCount < 3) return false;
    bool hasPositive = false, hasNegative = false;
    for (uint32_t i = 0; i < pointCount; ++i) {
        uint32_t j = (i + 1) % pointCount;
        uint32_t k = (i + 2) % pointCount;
        float ax = points[j * 2] - points[i * 2];
        float ay = points[j * 2 + 1] - points[i * 2 + 1];
        float bx = points[k * 2] - points[j * 2];
        float by = points[k * 2 + 1] - points[j * 2 + 1];
        float cross = ax * by - ay * bx;
        if (cross > 1e-6f) hasPositive = true;
        if (cross < -1e-6f) hasNegative = true;
        if (hasPositive && hasNegative) return false;
    }
    return true;
}

// ============================================================================
// Convex Triangle Fan (O(n) vs O(n²) ear-clipping)
// ============================================================================

bool ImpellerD3D12Engine::TessellateConvexFan(
    const float* points, uint32_t pointCount,
    float r, float g, float b, float a)
{
    if (pointCount < 3) return false;

    ImpellerDrawBatch batch;
    batch.vertices.reserve(pointCount);
    for (uint32_t i = 0; i < pointCount; ++i) {
        batch.vertices.push_back({ points[i * 2], points[i * 2 + 1], r, g, b, a });
    }

    // Triangle fan: vertex 0 is the hub, connect to each consecutive pair
    batch.indices.reserve((pointCount - 2) * 3);
    for (uint32_t i = 1; i + 1 < pointCount; ++i) {
        batch.indices.push_back(0);
        batch.indices.push_back(i);
        batch.indices.push_back(i + 1);
    }

    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    return true;
}

// ============================================================================
// Optimized Shape Generators (Flutter Impeller: EllipticalVertexGenerator)
// Uses triangle strip via zig-zag between quadrant pairs.
// ============================================================================

bool ImpellerD3D12Engine::GenerateFilledCircleStrip(
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
    if (!TriangulateCompoundPath(contours, fr, triVerts) || triVerts.size() < 6)
        return false;

    uint32_t vertCount = (uint32_t)(triVerts.size() / 2);
    ImpellerDrawBatch batch;
    batch.vertices.reserve(vertCount);
    batch.indices.reserve(vertCount);

    // Build gradient stop array for SampleLinearGradient
    std::vector<float> stopData;
    stopData.reserve(brush.stopCount * 5);
    for (uint32_t i = 0; i < brush.stopCount; ++i) {
        stopData.push_back(brush.stops[i].position);
        stopData.push_back(brush.stops[i].r);
        stopData.push_back(brush.stops[i].g);
        stopData.push_back(brush.stops[i].b);
        stopData.push_back(brush.stops[i].a);
    }

    for (uint32_t i = 0; i < vertCount; ++i) {
        float px = triVerts[i * 2], py = triVerts[i * 2 + 1];

        // Sample gradient color at this vertex position (in path space)
        GradientColor gc;
        if (brush.type == 1) {
            // Linear gradient
            gc = SampleLinearGradient(px, py,
                brush.startX, brush.startY, brush.endX, brush.endY,
                stopData.data(), brush.stopCount);
        } else if (brush.type == 2) {
            // Radial gradient: distance from center → t
            float dx = px - brush.centerX, dy = py - brush.centerY;
            float dist = std::sqrt(dx * dx + dy * dy);
            float maxR = std::max(brush.radiusX, brush.radiusY);
            float t = (maxR > 1e-6f) ? dist / maxR : 0.0f;
            t = std::max(0.0f, std::min(1.0f, t));
            float projX = brush.startX + t * (brush.endX - brush.startX);
            gc = SampleLinearGradient(projX, 0,
                brush.startX, 0, brush.endX, 0,
                stopData.data(), brush.stopCount);
        } else if (brush.type == 3) {
            // Sweep gradient: angle from center → t
            float dx = px - brush.centerX, dy = py - brush.centerY;
            float angle = std::atan2(dy, dx); // [-pi, pi]
            float t = (angle + (float)M_PI) / (2.0f * (float)M_PI); // [0, 1]
            // Apply start/end angle if specified (startX/endX repurposed as angles in radians)
            if (std::abs(brush.endX - brush.startX) > 1e-6f) {
                float startA = brush.startX, endA = brush.endX;
                float range = endA - startA;
                t = (angle - startA) / range;
                t = t - std::floor(t); // wrap to [0,1]
            }
            t = std::max(0.0f, std::min(1.0f, t));
            float projX = stopData[0] + t * (stopData[(brush.stopCount - 1) * 5] - stopData[0]);
            gc = SampleLinearGradient(projX, 0,
                stopData[0], 0, stopData[(brush.stopCount - 1) * 5], 0,
                stopData.data(), brush.stopCount);
        } else {
            gc = { brush.r * brush.a, brush.g * brush.a, brush.b * brush.a, brush.a };
        }

        // Premultiply and transform
        float vx = px, vy = py;
        TransformPoint(vx, vy, transform);
        batch.vertices.push_back({ vx, vy, gc.r * gc.a, gc.g * gc.a, gc.b * gc.a, gc.a });
        batch.indices.push_back(i);
    }

    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    return true;
}

// ============================================================================
// Alpha Coverage (Flutter Impeller: ComputeStrokeAlphaCoverage)
// ============================================================================

float ImpellerD3D12Engine::ComputeStrokeAlphaCoverage(float strokeWidth, float transformScale) {
    float pixelWidth = strokeWidth * transformScale;
    if (pixelWidth >= 1.0f) return 1.0f;
    // Subpixel stroke: fade alpha proportionally
    return std::max(pixelWidth, 0.05f);
}

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
}

ImpellerD3D12Engine::~ImpellerD3D12Engine() = default;

// ============================================================================
// Initialization
// ============================================================================

bool ImpellerD3D12Engine::Initialize() {
    if (initialized_) return true;

    if (!CreateRootSignature()) {
        OutputDebugStringA("[Impeller] Initialize: CreateRootSignature FAILED\n");
        return false;
    }
    if (!CreatePipelines()) {
        OutputDebugStringA("[Impeller] Initialize: CreatePipelines FAILED\n");
        return false;
    }

    // Create RTV heap for output texture
    D3D12_DESCRIPTOR_HEAP_DESC rtvDesc = {};
    rtvDesc.NumDescriptors = 1;
    rtvDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
    if (FAILED(device_->CreateDescriptorHeap(&rtvDesc, IID_PPV_ARGS(&rtvHeap_)))) {
        OutputDebugStringA("[Impeller] Initialize: CreateDescriptorHeap FAILED\n");
        return false;
    }

    {
        char buf[128];
        sprintf_s(buf, "[Impeller] Initialize: SUCCESS — PSO ready, RTVFormat=%u\n", (unsigned)rtvFormat_);
        OutputDebugStringA(buf);
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
    if (!TriangulatePolygon(flatPoints_.data(), pointCount, indices)) {
        return false;
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

void ImpellerD3D12Engine::GenerateRoundCap(
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

void ImpellerD3D12Engine::GenerateRoundJoin(
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

// ============================================================================
// Ellipse Tessellation (Impeller-style)
// ============================================================================

uint32_t ImpellerD3D12Engine::ComputeQuadrantDivisions(float pixelRadius) {
    // Impeller formula: N = ceil(pi/4 / acos(1 - tolerance/radius))
    constexpr float kCircleTolerance = 0.1f;
    if (pixelRadius <= 0.0f) return 1;

    float k = kCircleTolerance / pixelRadius;
    if (k >= 1.0f) return 1;

    float n = std::ceil((float)M_PI / 4.0f / std::acos(1.0f - k));
    return std::max(1u, std::min((uint32_t)n, 64u));
}

// ============================================================================
// Scanline rasterizer (Vello-inspired analytic coverage, no triangulation)
// ============================================================================
//
// For paths small enough that ear-clipping triangulation produces visible
// pixel cracks (icon glyphs, scrollbar arrows, the rounded play arrow at
// 8×8), we evaluate fill coverage exactly per pixel using horizontal
// scanline + edge-crossing winding count. This mirrors the math Vello
// uses on the GPU but on the CPU, so the resulting batch is just a list
// of 1-pixel-tall solid rectangles that go through Impeller's existing
// solid-fill PSO — meaning per-batch GPU scissor (UI clipping) still
// applies. Vello as an engine renders correctly but ignores the UI clip
// stack; this approach gets Vello's pixel correctness AND Impeller's
// clipping in one go.
//
// Coverage is binary (no analytic AA) — matches what the triangulation
// path already produces for tiny shapes, which look fine. Adding analytic
// AA would multiply each span by a per-pixel alpha mask and double the
// vertex count; revisit once the correctness baseline is solid.
// ============================================================================

namespace {

// Analytic AA output unit: an axis-aligned rectangle with a per-rect
// alpha in [0, 1]. The alpha is applied to the already-premultiplied
// brush color at emit time (color * alpha, alpha * alpha), keeping the
// solid-fill PSO's premult-alpha blending correct.
struct PixelRect { int x; int y; int w; int h; float alpha; };

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

// ============================================================================
// Path Encoding Entry Points
// ============================================================================

bool ImpellerD3D12Engine::EncodeFillPath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform)
{
    auto fillPathEntryTime = std::chrono::high_resolution_clock::now();

    // ------------------------------------------------------------------
    // Gradient brushes still use the source-space flatten path because
    // EncodeGradientFillPath samples the gradient at each contour vertex
    // in PATH-LOCAL coordinates (gradient brush.startX/Y/endX/Y are also
    // in path space) and only transforms to pixels after sampling.
    // Touching that contract would require rewriting the gradient
    // sampler — out of scope for this fix, so we keep the legacy path.
    // ------------------------------------------------------------------
    if (brush.type == 1 || brush.type == 2) {
        float gradMaxScale = std::max(
            std::sqrt(transform.m11 * transform.m11 + transform.m12 * transform.m12),
            std::sqrt(transform.m21 * transform.m21 + transform.m22 * transform.m22));
        float gradTolerance = (gradMaxScale > 0.001f)
            ? flattenTolerance_ / gradMaxScale
            : flattenTolerance_;

        std::vector<Contour> gradContours = FlattenPathToContours(
            startX, startY, commands, commandLength, gradTolerance);
        if (gradContours.empty()) return false;

        bool gradOk = EncodeGradientFillPath(gradContours, brush, transform);
        if (gradOk) encodedPathCount_++;
        return gradOk;
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

    auto flattenStart = std::chrono::high_resolution_clock::now();

    std::vector<Contour> contours = FlattenPathToContours(
        pxStartX, pxStartY, pxCommands.data(), (uint32_t)pxCommands.size(),
        adaptiveTolerance);

    auto flattenEnd = std::chrono::high_resolution_clock::now();

    if (contours.empty()) {
        static int emptyCount = 0;
        if (emptyCount++ < 5) {
            char buf[128];
            sprintf_s(buf, "[Impeller] EncodeFillPath: FlattenPathToContours returned empty, cmdLen=%u\n", commandLength);
            OutputDebugStringA(buf);
        }
        return false;
    }

    {
        // ── SVG Perf: log every call for first batch, then summary ──
        static int encodeCount = 0;
        static double totalFlattenUs = 0;
        static double totalTessUs = 0;
        static double totalCallUs = 0;
        static uint32_t totalPathPts = 0;
        static int batchCallCount = 0;

        uint32_t totalPts = 0;
        for (auto& c : contours) totalPts += c.VertexCount();

        double flattenUs = std::chrono::duration<double, std::micro>(flattenEnd - flattenStart).count();
        totalFlattenUs += flattenUs;
        totalPathPts += totalPts;
        batchCallCount++;

        if (encodeCount < 20) {
            char buf[320];
            sprintf_s(buf, "[Impeller FillPath #%d] flatten=%.1fus, %zu contours, %u pts, "
                "cmdLen=%u, tol=%.3f, scale=%.2f\n",
                encodeCount, flattenUs, contours.size(), totalPts,
                commandLength, adaptiveTolerance, maxScale);
            OutputDebugStringA(buf);
        }
        // Summary every 700 calls (approx one SVG tiger frame)
        if (batchCallCount >= 700 || (encodeCount > 0 && encodeCount % 700 == 0)) {
            char buf[320];
            sprintf_s(buf, "[Impeller FillPath SUMMARY] %d calls, flatten=%.1fms, "
                "tess=%.1fms, total=%.1fms, totalPts=%u\n",
                batchCallCount,
                totalFlattenUs / 1000.0,
                totalTessUs / 1000.0,
                totalCallUs / 1000.0,
                totalPathPts);
            OutputDebugStringA(buf);
            totalFlattenUs = 0;
            totalTessUs = 0;
            totalCallUs = 0;
            totalPathPts = 0;
            batchCallCount = 0;
        }
        encodeCount++;
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
            ImpellerDrawBatch batch;
            batch.vertices.reserve(rects.size() * 4);
            batch.indices.reserve(rects.size() * 6);
            for (const auto& rect : rects) {
                float x0 = (float)rect.x;
                float y0 = (float)rect.y;
                float x1 = (float)(rect.x + rect.w);
                float y1 = (float)(rect.y + rect.h);
                // Apply per-rect analytic coverage to the already
                // premultiplied brush color. Because r,g,b were
                // multiplied by a at the top of the function, scaling
                // all four channels by rect.alpha produces valid
                // premult-alpha values the blend mode consumes as
                // (src * 1 + dst * (1 - src.a)).
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

    if (contours.size() == 1) {
        const auto& c = contours[0];
        std::vector<uint32_t> indices;
        if (TriangulatePolygon(c.points.data(), c.VertexCount(), indices)
            && indices.size() >= 3)
        {
            ImpellerDrawBatch batch;
            batch.vertices.reserve(c.VertexCount());
            for (uint32_t i = 0; i < c.VertexCount(); ++i) {
                batch.vertices.push_back({ c.X(i), c.Y(i), r, g, b, a });
            }
            batch.indices = std::move(indices);
            batch.pipelineType = 0;
            PushBatch(std::move(batch));
            encodedPathCount_++;
            return true;
        }
    } else {
        std::vector<float> triVerts;
        if (TriangulateCompoundPath(contours, fr, triVerts) && triVerts.size() >= 6) {
            ImpellerDrawBatch batch;
            uint32_t vertCount = (uint32_t)(triVerts.size() / 2);
            batch.vertices.reserve(vertCount);
            batch.indices.reserve(vertCount);
            for (uint32_t i = 0; i < vertCount; ++i) {
                batch.vertices.push_back({ triVerts[i * 2], triVerts[i * 2 + 1], r, g, b, a });
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
        static int warnCount = 0;
        if (warnCount++ < 20) {
            uint32_t totalPts = 0;
            for (auto& c : contours) totalPts += c.VertexCount();
            char buf[256];
            sprintf_s(buf, "[Impeller] primary triangulation failed, per-contour fallback: "
                "%zu contours, %u pts\n", contours.size(), totalPts);
            OutputDebugStringA(buf);
        }

        bool anyEmitted = false;
        for (auto& c : contours) {
            uint32_t vc = c.VertexCount();
            if (vc < 3) continue;
            std::vector<uint32_t> indices;
            if (TriangulatePolygon(c.points.data(), vc, indices) && indices.size() >= 3) {
                ImpellerDrawBatch batch;
                batch.vertices.reserve(indices.size());
                batch.indices.reserve(indices.size());
                for (uint32_t idx = 0; idx < (uint32_t)indices.size(); ++idx) {
                    uint32_t vi = indices[idx];
                    batch.vertices.push_back({ c.X(vi), c.Y(vi), r, g, b, a });
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

bool ImpellerD3D12Engine::EncodeStrokePath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    float strokeWidth, bool closed,
    int32_t lineJoin, float miterLimit,
    int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    const EngineTransform& transform)
{
    auto strokeEntryTime = std::chrono::high_resolution_clock::now();

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

    std::vector<Contour> contours = FlattenPathToContours(
        pxStartX, pxStartY, pxCommands.data(), (uint32_t)pxCommands.size(),
        adaptiveTolerance);

    {
        static int s_strokeCount = 0;
        static double s_totalStrokeUs = 0;
        auto strokeFlattenEnd = std::chrono::high_resolution_clock::now();
        double us = std::chrono::duration<double, std::micro>(strokeFlattenEnd - strokeEntryTime).count();
        s_totalStrokeUs += us;
        s_strokeCount++;
        if (s_strokeCount <= 20 || s_strokeCount % 700 == 0) {
            char buf[256];
            sprintf_s(buf, "[Impeller StrokePath #%d] flatten=%.1fus, %zu contours, cmdLen=%u, strokeW=%.1f\n",
                s_strokeCount, us, contours.size(), commandLength, strokeWidth);
            OutputDebugStringA(buf);
        }
        if (s_strokeCount % 700 == 0) {
            char buf[192];
            sprintf_s(buf, "[Impeller StrokePath SUMMARY] %d calls, total=%.1fms\n",
                s_strokeCount, s_totalStrokeUs / 1000.0);
            OutputDebugStringA(buf);
            s_totalStrokeUs = 0;
            s_strokeCount = 0;
        }
    }

    if (contours.empty()) return false;

    auto join = static_cast<ImpellerJoin>(lineJoin);
    auto cap = static_cast<ImpellerCap>(lineCap);

    // -------------------------------------------------------------
    // Route the whole stroke through the analytic-AA rasterizer.
    //
    // ExpandStroke normally builds a triangle mesh and pushes it as
    // an ImpellerDrawBatch — the GPU then rasterizes it with binary
    // SampleDesc.Count=1 coverage, giving visibly aliased edges on
    // everything that isn't axis-aligned (the symptom the user saw
    // on DockLayout tab outlines).
    //
    // In "collect" mode it instead appends one CCW-normalized
    // triangle contour per mesh triangle to strokeContours. We then
    // feed all of those contours as a single NonZero compound path
    // to RasterizePathToRects, which produces alpha-weighted rects
    // — the same path fills already take, so strokes now share the
    // same 4× vertical / analytic-horizontal AA quality.
    //
    // Dash expansion still produces multiple logical sub-polylines
    // per source contour, but all of them dump their triangles into
    // the same strokeContours vector, so there's exactly one final
    // rasterize + emit pass regardless of dash count.
    // -------------------------------------------------------------
    std::vector<Contour> strokeContours;
    strokeContours.reserve(contours.size() * 8);

    // Stroke each contour separately. Contours are already in pixel
    // space (pxCommands were transformed before flattening above), so
    // we just copy the points verbatim — no per-vertex transform.
    for (auto& c : contours) {
        if (c.VertexCount() < 2) continue;

        flatPoints_ = c.points;

        // Apply dash pattern if specified (pixel-space lengths).
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
                            ExpandStroke(brush, pxStrokeWidth, join, miterLimit, cap, false, &strokeContours);
                        }
                        currentSegment.clear();
                        dashIdx = (dashIdx + 1) % dashCount; dashRemain = pxDashPattern[dashIdx]; isDraw = !isDraw;
                    }
                }
            }
            if (isDraw && currentSegment.size() >= 4) {
                flatPoints_ = std::move(currentSegment);
                ExpandStroke(brush, pxStrokeWidth, join, miterLimit, cap, false, &strokeContours);
            }
        } else {
            ExpandStroke(brush, pxStrokeWidth, join, miterLimit, cap, closed, &strokeContours);
        }
    }

    if (strokeContours.empty()) return false;

    // Rasterize the full stroke mesh as a compound NonZero path and
    // emit alpha-weighted rects through the solid-fill PSO.
    std::vector<PixelRect> rects;
    rects.reserve(strokeContours.size() * 2);
    RasterizePathToRects(strokeContours, FillRule::NonZero, rects);

    if (rects.empty()) return false;

    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    ImpellerDrawBatch batch;
    batch.vertices.reserve(rects.size() * 4);
    batch.indices.reserve(rects.size() * 6);
    for (const auto& rect : rects) {
        float x0 = (float)rect.x;
        float y0 = (float)rect.y;
        float x1 = (float)(rect.x + rect.w);
        float y1 = (float)(rect.y + rect.h);
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

bool ImpellerD3D12Engine::EncodeFillPolygon(
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
    // Premultiply alpha
    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    // Use optimized triangle strip generator (Flutter Impeller-style)
    bool ok = GenerateFilledEllipseStrip(cx, cy, rx, ry, r, g, b, a, transform);
    if (ok) encodedPathCount_++;
    return ok;
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

        // Stencil: always pass, increment on front, decrement on back
        psoDesc.DepthStencilState.DepthEnable = FALSE;
        psoDesc.DepthStencilState.StencilEnable = TRUE;
        psoDesc.DepthStencilState.StencilReadMask = 0xFF;
        psoDesc.DepthStencilState.StencilWriteMask = 0xFF;
        psoDesc.DepthStencilState.FrontFace.StencilFunc = D3D12_COMPARISON_FUNC_ALWAYS;
        psoDesc.DepthStencilState.FrontFace.StencilPassOp = D3D12_STENCIL_OP_INCR_SAT;
        psoDesc.DepthStencilState.FrontFace.StencilFailOp = D3D12_STENCIL_OP_KEEP;
        psoDesc.DepthStencilState.FrontFace.StencilDepthFailOp = D3D12_STENCIL_OP_KEEP;
        psoDesc.DepthStencilState.BackFace.StencilFunc = D3D12_COMPARISON_FUNC_ALWAYS;
        psoDesc.DepthStencilState.BackFace.StencilPassOp = D3D12_STENCIL_OP_DECR_SAT;
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

    {
        static int execCount = 0;
        if (execCount++ < 10) {
            uint32_t solidCount = 0, stencilCount = 0;
            for (auto& b : batches_) {
                if (b.pipelineType == 1) stencilCount++;
                else solidCount++;
            }
            char buf[256];
            sprintf_s(buf, "[Impeller] Execute: vp=%ux%u batches=%zu (solid=%u stencil=%u)\n",
                viewportW, viewportH, batches_.size(), solidCount, stencilCount);
            OutputDebugStringA(buf);
        }
    }

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

    // Tile coverage culling stats (Flutter Impeller-style)
    uint32_t culledByCoverage = 0;

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
            culledByCoverage++;
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

    {
        static int cullLog = 0;
        if (culledByCoverage > 0 && cullLog++ < 10) {
            char buf[160];
            sprintf_s(buf, "[Impeller] tile-coverage culled %u/%zu batches\n",
                      culledByCoverage, batches_.size());
            OutputDebugStringA(buf);
        }
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
