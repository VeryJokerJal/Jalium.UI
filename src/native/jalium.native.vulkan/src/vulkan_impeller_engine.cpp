#include "vulkan_impeller_engine.h"
#include "jalium_scanline_rasterizer.h"   // PixelRect / RasterizePathToRects
#include "jalium_triangulate.h"           // TriangulateCompoundPath / FlattenPathToContours
#include <cstring>
#include <cmath>
#include <algorithm>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace jalium {

// ============================================================================
// ImpellerVulkanEngine — CPU tessellation + scanline AA + cross-backend
// stroke / shape / gradient algorithms.
//
// The engine produces VkImpellerDrawBatch records and hands them to
// vulkan_render_target.cpp (via GetBatches()). It does NOT own a render
// pass / pipeline / framebuffer — that side lives in the render target so
// the existing GPU composite path can consume Impeller batches the same way
// it consumes its other GPU draw lists. This mirrors the D3D12 design
// where ImpellerD3D12Engine batches are fed into D3D12DirectRenderer.
// ============================================================================

ImpellerVulkanEngine::ImpellerVulkanEngine(VkDevice device, VkPhysicalDevice physicalDevice)
    : device_(device)
    , physicalDevice_(physicalDevice)
{
}

ImpellerVulkanEngine::~ImpellerVulkanEngine() = default;

bool ImpellerVulkanEngine::Initialize() {
    initialized_ = true;
    return true;
}

void ImpellerVulkanEngine::BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) {
    viewportW_ = viewportWidth;
    viewportH_ = viewportHeight;
    batches_.clear();
    encodedPathCount_ = 0;
    flatPoints_.clear();
}

void ImpellerVulkanEngine::SetScissorRect(float left, float top, float right, float bottom) {
    scissorLeft_ = left; scissorTop_ = top;
    scissorRight_ = right; scissorBottom_ = bottom;
    hasScissor_ = true;
}

void ImpellerVulkanEngine::ClearScissorRect() {
    hasScissor_ = false;
}

bool ImpellerVulkanEngine::Execute(void* /*commandList*/, void* /*renderTarget*/,
                                   uint32_t /*width*/, uint32_t /*height*/) {
    // Consumed externally via GetBatches() — see vulkan_render_target.cpp.
    return true;
}

bool ImpellerVulkanEngine::HasPendingWork() const {
    return !batches_.empty();
}

uint32_t ImpellerVulkanEngine::GetEncodedPathCount() const {
    return encodedPathCount_;
}

// ============================================================================
// ExpandStroke — wrapper around the cross-backend jalium::ExpandStrokePath
// ============================================================================

bool ImpellerVulkanEngine::ExpandStroke(
    const EngineBrushData& brush,
    float strokeWidth,
    ImpellerJoin join, float miterLimit,
    ImpellerCap cap, bool closed,
    std::vector<Contour>* collectContours)
{
    uint32_t pointCount = (uint32_t)(flatPoints_.size() / 2);
    if (pointCount < 2) return false;

    VkImpellerDrawBatch batch;
    bool ok = jalium::ExpandStrokePath<VkImpellerVertex>(
        batch.vertices, batch.indices,
        flatPoints_.data(), pointCount,
        strokeWidth, join, miterLimit, cap, closed,
        brush.r, brush.g, brush.b, brush.a,
        collectContours);
    if (!ok) return false;
    if (collectContours) return true;

    if (batch.vertices.empty() || batch.indices.empty()) return true;
    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    return true;
}

// ============================================================================
// Helpers — pixel-space command transform (same logic as D3D12 backend)
// ============================================================================

namespace {

inline float MaxScale(const EngineTransform& t) {
    return std::max(
        std::sqrt(t.m11 * t.m11 + t.m12 * t.m12),
        std::sqrt(t.m21 * t.m21 + t.m22 * t.m22));
}

// Build a pixel-space command stream by walking the source command tape and
// pre-applying the transform. Output retains the same tag layout
// (LineTo/CubicTo/MoveTo/QuadTo/ClosePath) that FlattenPathToContours
// expects.
inline void TransformCommandsToPixelSpace(
    const float* commands, uint32_t commandLength,
    const EngineTransform& transform,
    std::vector<float>& outCommands)
{
    auto apply = [&](float& x, float& y) {
        float tx = transform.m11 * x + transform.m21 * y + transform.dx;
        float ty = transform.m12 * x + transform.m22 * y + transform.dy;
        x = tx;
        y = ty;
    };

    outCommands.clear();
    outCommands.reserve(commandLength);
    uint32_t i = 0;
    while (i < commandLength) {
        int tag = (int)commands[i];
        switch (tag) {
            case 0: { // LineTo: [0, ex, ey]
                if (i + 2 >= commandLength) { i = commandLength; break; }
                float x = commands[i + 1], y = commands[i + 2];
                apply(x, y);
                outCommands.push_back(0.0f);
                outCommands.push_back(x);
                outCommands.push_back(y);
                i += 3;
                break;
            }
            case 1: { // CubicTo: [1, c1x, c1y, c2x, c2y, ex, ey]
                if (i + 6 >= commandLength) { i = commandLength; break; }
                float c1x = commands[i + 1], c1y = commands[i + 2];
                float c2x = commands[i + 3], c2y = commands[i + 4];
                float ex  = commands[i + 5], ey  = commands[i + 6];
                apply(c1x, c1y);
                apply(c2x, c2y);
                apply(ex,  ey);
                outCommands.push_back(1.0f);
                outCommands.push_back(c1x); outCommands.push_back(c1y);
                outCommands.push_back(c2x); outCommands.push_back(c2y);
                outCommands.push_back(ex);  outCommands.push_back(ey);
                i += 7;
                break;
            }
            case 2: { // MoveTo: [2, x, y]
                if (i + 2 >= commandLength) { i = commandLength; break; }
                float x = commands[i + 1], y = commands[i + 2];
                apply(x, y);
                outCommands.push_back(2.0f);
                outCommands.push_back(x);
                outCommands.push_back(y);
                i += 3;
                break;
            }
            case 3: { // QuadTo: [3, cx, cy, ex, ey]
                if (i + 4 >= commandLength) { i = commandLength; break; }
                float cx = commands[i + 1], cy = commands[i + 2];
                float ex = commands[i + 3], ey = commands[i + 4];
                apply(cx, cy);
                apply(ex, ey);
                outCommands.push_back(3.0f);
                outCommands.push_back(cx); outCommands.push_back(cy);
                outCommands.push_back(ex); outCommands.push_back(ey);
                i += 5;
                break;
            }
            case 5: { // ClosePath
                outCommands.push_back(5.0f);
                i += 1;
                break;
            }
            default:
                i = commandLength;
                break;
        }
    }
}

inline void EmitRectsAsBatch(
    const std::vector<PixelRect>& rects,
    float r, float g, float b, float a,
    VkImpellerDrawBatch& batch)
{
    batch.vertices.reserve(batch.vertices.size() + rects.size() * 4);
    batch.indices.reserve(batch.indices.size() + rects.size() * 6);
    for (const auto& rect : rects) {
        float x0 = (float)rect.x;
        float y0 = (float)rect.y;
        float x1 = (float)(rect.x + rect.w);
        float y1 = (float)(rect.y + rect.h);
        // Apply per-rect analytic coverage to premultiplied brush color.
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
}

} // namespace

// ============================================================================
// EncodeFillPath
// ============================================================================

bool ImpellerVulkanEngine::EncodeFillPath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform)
{
    // Gradient brushes flatten in source space because the gradient sampler
    // takes path-local coords; transform happens in the per-vertex bake step.
    if (brush.type == 1 || brush.type == 2 || brush.type == 3) {
        float gradMaxScale = MaxScale(transform);
        float gradTolerance = (gradMaxScale > 0.001f)
            ? flattenTolerance_ / gradMaxScale
            : flattenTolerance_;

        std::vector<Contour> gradContours = FlattenPathToContours(
            startX, startY, commands, commandLength, gradTolerance);
        if (gradContours.empty()) return false;

        if (!brush.stops || brush.stopCount == 0) return false;

        int32_t fr = 0;
        std::vector<float> triVerts;
        if (!TriangulateCompoundPath(gradContours, fr, triVerts) || triVerts.size() < 6)
            return false;

        std::vector<float> stopData;
        FlattenGradientStops(brush, stopData);

        VkImpellerDrawBatch batch;
        uint32_t vertCount = (uint32_t)(triVerts.size() / 2);
        batch.vertices.reserve(vertCount);
        batch.indices.reserve(vertCount);

        for (uint32_t i = 0; i < vertCount; ++i) {
            float px = triVerts[i * 2], py = triVerts[i * 2 + 1];
            GradientColor gc = SampleBrushGradient(brush, stopData.data(), px, py);
            float vx = px, vy = py;
            TransformPoint(vx, vy, transform);
            batch.vertices.push_back({ vx, vy, gc.r * gc.a, gc.g * gc.a, gc.b * gc.a, gc.a });
            batch.indices.push_back(i);
        }

        batch.pipelineType = 0;
        PushBatch(std::move(batch));
        encodedPathCount_++;
        return true;
    }

    // Solid fill: pixel-space flatten → analytic-AA scanline rasterize.
    float pxStartX = startX, pxStartY = startY;
    TransformPoint(pxStartX, pxStartY, transform);

    std::vector<float> pxCommands;
    TransformCommandsToPixelSpace(commands, commandLength, transform, pxCommands);

    std::vector<Contour> contours = FlattenPathToContours(
        pxStartX, pxStartY, pxCommands.data(), (uint32_t)pxCommands.size(),
        flattenTolerance_);

    contours.erase(
        std::remove_if(contours.begin(), contours.end(),
            [](const Contour& c) { return c.VertexCount() < 3; }),
        contours.end());
    if (contours.empty()) return false;

    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    std::vector<PixelRect> rects;
    rects.reserve(64);
    RasterizePathToRects(contours, fillRule, rects);

    if (!rects.empty()) {
        VkImpellerDrawBatch batch;
        EmitRectsAsBatch(rects, r, g, b, a, batch);
        batch.pipelineType = 0;
        PushBatch(std::move(batch));
        encodedPathCount_++;
        return true;
    }

    // Sub-pixel / degenerate fallback: per-contour ear-clip so something
    // still renders for tiny shapes RasterizePathToRects rejected.
    bool anyEmitted = false;
    for (auto& c : contours) {
        uint32_t vc = c.VertexCount();
        if (vc < 3) continue;
        std::vector<uint32_t> indices;
        if (TriangulatePolygon(c.points.data(), vc, indices) && indices.size() >= 3) {
            VkImpellerDrawBatch batch;
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

// ============================================================================
// EncodeStrokePath
// ============================================================================

bool ImpellerVulkanEngine::EncodeStrokePath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    float strokeWidth, bool closed,
    int32_t lineJoin, float miterLimit,
    int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    const EngineTransform& transform)
{
    float maxScale = MaxScale(transform);
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
    TransformCommandsToPixelSpace(commands, commandLength, transform, pxCommands);

    std::vector<Contour> contours = FlattenPathToContours(
        pxStartX, pxStartY, pxCommands.data(), (uint32_t)pxCommands.size(),
        flattenTolerance_);

    if (contours.empty()) return false;

    auto join = static_cast<ImpellerJoin>(lineJoin);
    auto cap  = static_cast<ImpellerCap>(lineCap);

    std::vector<Contour> strokeContours;
    strokeContours.reserve(contours.size() * 8);

    for (auto& c : contours) {
        if (c.VertexCount() < 2) continue;

        flatPoints_ = c.points;

        if (!pxDashPattern.empty()) {
            // Inline dash walker — same algorithm as the D3D12 EncodeStrokePath
            // so visual output is identical. Each on-segment becomes its own
            // sub-polyline that goes through ExpandStroke (collect mode).
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
                else {
                    temp -= dashRemain;
                    dashIdx = (dashIdx + 1) % dashCount;
                    dashRemain = pxDashPattern[dashIdx];
                }
            }

            bool isDraw = (dashIdx % 2) == 0;
            std::vector<float> currentSegment;
            std::vector<float> savedFlat = flatPoints_;

            for (uint32_t i = 0; i + 1 < pointCount; ++i) {
                float x0 = savedFlat[i * 2],     y0 = savedFlat[i * 2 + 1];
                float x1 = savedFlat[(i + 1) * 2], y1 = savedFlat[(i + 1) * 2 + 1];
                float dx = x1 - x0, dy = y1 - y0;
                float segLen = std::sqrt(dx * dx + dy * dy);
                if (segLen < 1e-6f) continue;

                float consumed = 0;
                while (consumed < segLen) {
                    float canConsume = std::min(dashRemain, segLen - consumed);
                    float t0 = consumed / segLen, t1 = (consumed + canConsume) / segLen;
                    if (isDraw) {
                        if (currentSegment.empty()) {
                            currentSegment.push_back(x0 + dx * t0);
                            currentSegment.push_back(y0 + dy * t0);
                        }
                        currentSegment.push_back(x0 + dx * t1);
                        currentSegment.push_back(y0 + dy * t1);
                    }
                    consumed += canConsume;
                    dashRemain -= canConsume;
                    if (dashRemain <= 1e-6f) {
                        if (isDraw && currentSegment.size() >= 4) {
                            flatPoints_ = std::move(currentSegment);
                            ExpandStroke(brush, pxStrokeWidth, join, miterLimit, cap, false, &strokeContours);
                        }
                        currentSegment.clear();
                        dashIdx = (dashIdx + 1) % dashCount;
                        dashRemain = pxDashPattern[dashIdx];
                        isDraw = !isDraw;
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

    std::vector<PixelRect> rects;
    rects.reserve(strokeContours.size() * 2);
    RasterizePathToRects(strokeContours, FillRule::NonZero, rects);

    if (rects.empty()) return false;

    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    VkImpellerDrawBatch batch;
    EmitRectsAsBatch(rects, r, g, b, a, batch);
    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    encodedPathCount_++;
    return true;
}

// ============================================================================
// EncodeFillPolygon
// ============================================================================

bool ImpellerVulkanEngine::EncodeFillPolygon(
    const float* points, uint32_t pointCount,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform)
{
    if (pointCount < 3) return false;

    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

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

    if (rects.empty()) return false;

    VkImpellerDrawBatch batch;
    EmitRectsAsBatch(rects, r, g, b, a, batch);
    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    encodedPathCount_++;
    return true;
}

// ============================================================================
// EncodeFillEllipse
// ============================================================================

bool ImpellerVulkanEngine::EncodeFillEllipse(
    float cx, float cy, float rx, float ry,
    const EngineBrushData& brush,
    const EngineTransform& transform)
{
    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    VkImpellerDrawBatch batch;
    if (!jalium::GenerateFilledEllipseStrip<VkImpellerVertex>(
            batch.vertices, batch.indices,
            cx, cy, rx, ry, r, g, b, a,
            trigCache_, transform)) {
        return false;
    }
    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    encodedPathCount_++;
    return true;
}

} // namespace jalium
