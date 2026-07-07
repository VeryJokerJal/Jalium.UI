#include "d3d12_vello_engine.h"
#include <cmath>

namespace jalium {

// ============================================================================
// VelloD3D12Engine — adapter from IRenderingEngine to D3D12VelloRenderer
// ============================================================================

VelloD3D12Engine::VelloD3D12Engine(ID3D12Device* device, ShaderBlobCache* shaderCache)
    : vello_(device, shaderCache)
{
}

VelloD3D12Engine::~VelloD3D12Engine() = default;

bool VelloD3D12Engine::Initialize() {
    return vello_.Initialize();
}

void VelloD3D12Engine::BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) {
    vello_.BeginFrame(viewportWidth, viewportHeight);
}

void VelloD3D12Engine::SetScissorRect(float left, float top, float right, float bottom) {
    vello_.SetScissorRect(left, top, right, bottom);
}

void VelloD3D12Engine::ClearScissorRect() {
    vello_.ClearScissorRect();
}

bool VelloD3D12Engine::EncodeFillPath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform,
    int32_t edgeMode)
{
    (void)edgeMode;  // Vello path currently has analytic AA only; aliased fallback not implemented yet.
    uint32_t rule = (fillRule == FillRule::NonZero) ? kFillRuleNonZero : kFillRuleEvenOdd;

    // Full brush packing (solid / linear / radial / SWEEP) — mirrors the core
    // encoder's EncodeFillPath + PackBrush, which VelloVulkanEngine consumes.
    // Previously this adapter flattened EVERY brush to the flat fallback color
    // (brush.r/g/b/a), which is exactly where a sweep gradient degraded to a
    // flat fill on D3D12 (D5a). opacity = 1.0 matches the core call site.
    return vello_.EncodeFillPathEngineBrush(
        startX, startY, commands, commandLength,
        brush, rule, /*opacity*/ 1.0f,
        transform.m11, transform.m12,
        transform.m21, transform.m22,
        transform.dx, transform.dy);
}

bool VelloD3D12Engine::EncodeStrokePath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    float strokeWidth, bool closed,
    int32_t lineJoin, float miterLimit,
    int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    const EngineTransform& transform,
    int32_t edgeMode)
{
    (void)edgeMode;  // Vello path currently has analytic AA only.
    // Gradient-capable stroke (incl. sweep): solid-color geometry pass, then
    // the emitted PathDraw is patched with the engine brush packing.
    return vello_.EncodeStrokePathEngineBrush(
        startX, startY, commands, commandLength,
        brush,
        strokeWidth, closed,
        lineJoin, miterLimit, /*opacity*/ 1.0f, lineCap,
        dashPattern, dashCount, dashOffset,
        transform.m11, transform.m12,
        transform.m21, transform.m22,
        transform.dx, transform.dy);
}

bool VelloD3D12Engine::EncodeFillPolygon(
    const float* points, uint32_t pointCount,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform)
{
    if (pointCount < 3) return false;

    // Convert polygon points to a path command buffer:
    //   tag 0 = LineTo [0, x, y]
    // First point is the start, rest are LineTo commands.
    std::vector<float> commands;
    commands.reserve((pointCount - 1) * 3);
    for (uint32_t i = 1; i < pointCount; ++i) {
        commands.push_back(0.0f);  // LineTo tag
        commands.push_back(points[i * 2]);
        commands.push_back(points[i * 2 + 1]);
    }
    // Close the polygon
    commands.push_back(0.0f);
    commands.push_back(points[0]);
    commands.push_back(points[1]);

    return EncodeFillPath(
        points[0], points[1],
        commands.data(), (uint32_t)commands.size(),
        brush, fillRule, transform);
}

bool VelloD3D12Engine::EncodeFillEllipse(
    float cx, float cy, float rx, float ry,
    const EngineBrushData& brush,
    const EngineTransform& transform)
{
    // Approximate ellipse with 4 cubic bezier curves (standard technique).
    // Magic number for cubic bezier circle approximation: kappa = 4*(sqrt(2)-1)/3
    constexpr float kappa = 0.5522847498f;
    float kx = rx * kappa;
    float ky = ry * kappa;

    // Start at top of ellipse
    float startX = cx;
    float startY = cy - ry;

    // 4 cubic bezier curves (tag 1 = BezierTo [1, cp1x, cp1y, cp2x, cp2y, ex, ey])
    float commands[] = {
        // Top-right quadrant: (cx, cy-ry) → (cx+rx, cy)
        1, cx + kx, cy - ry,  cx + rx, cy - ky,  cx + rx, cy,
        // Bottom-right quadrant: (cx+rx, cy) → (cx, cy+ry)
        1, cx + rx, cy + ky,  cx + kx, cy + ry,  cx, cy + ry,
        // Bottom-left quadrant: (cx, cy+ry) → (cx-rx, cy)
        1, cx - kx, cy + ry,  cx - rx, cy + ky,  cx - rx, cy,
        // Top-left quadrant: (cx-rx, cy) → (cx, cy-ry)
        1, cx - rx, cy - ky,  cx - kx, cy - ry,  cx, cy - ry,
    };

    return EncodeFillPath(
        startX, startY,
        commands, sizeof(commands) / sizeof(float),
        brush, FillRule::NonZero, transform);
}

bool VelloD3D12Engine::Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) {
    auto* cmdList = static_cast<ID3D12GraphicsCommandList*>(commandList);
    // renderTarget is unused for Vello — it renders to its own output texture
    // which is later composited by the DirectRenderer.
    return vello_.Dispatch(cmdList, 0);
}

bool VelloD3D12Engine::HasPendingWork() const {
    return vello_.HasWork();
}

uint32_t VelloD3D12Engine::GetEncodedPathCount() const {
    return vello_.GetPathCount();
}

} // namespace jalium
