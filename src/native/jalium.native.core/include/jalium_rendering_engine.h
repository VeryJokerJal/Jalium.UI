#pragma once

#include "jalium_types.h"
#include <cstdint>
#include <vector>

namespace jalium {

/// Resolves JALIUM_ENGINE_AUTO to the concrete engine for the given backend.
/// When Auto, defaults to Impeller on all platforms.
/// The user selects the engine via RenderContext.DefaultRenderingEngine.
inline JaliumRenderingEngine ResolveRenderingEngine(
    JaliumRenderingEngine engine, JaliumBackend /*backend*/)
{
    if (engine != JALIUM_ENGINE_AUTO) return engine;
    return JALIUM_ENGINE_IMPELLER;
}

class Brush;

// ============================================================================
// IRenderingEngine — abstract interface for 2D vector graphics engines
//
// Two implementations:
//   VelloEngine   — GPU compute pipeline (prefix-sum tiling, analytical AA)
//   ImpellerEngine — CPU tessellation + GPU rasterization (stencil-then-cover)
//
// The engine handles complex path rendering.  Simple primitives (rects, text,
// bitmaps) are still handled by the backend's direct renderer.
// ============================================================================

/// Fill rule for path rendering.
enum class FillRule : uint32_t {
    EvenOdd  = 0,
    NonZero  = 1
};

/// Brush data passed to the engine for path fills.
struct EngineBrushData {
    uint32_t type = 0;          // 0=solid, 1=linearGrad, 2=radialGrad, 3=sweepGrad, 4=image
    float r = 0, g = 0, b = 0, a = 1;  // solid color

    // Gradient data
    float startX = 0, startY = 0;
    float endX = 0, endY = 0;
    float centerX = 0, centerY = 0;
    float radiusX = 0, radiusY = 0;
    float originX = 0, originY = 0;
    uint32_t spreadMethod = 0;  // 0=Pad, 1=Repeat, 2=Reflect

    struct GradientStop {
        float position;
        float r, g, b, a;
    };
    const GradientStop* stops = nullptr;
    uint32_t stopCount = 0;
};

/// Transform matrix (3x2 column-major, same as JaliumMatrix).
struct EngineTransform {
    float m11 = 1, m12 = 0;
    float m21 = 0, m22 = 1;
    float dx = 0, dy = 0;
};

/// Abstract rendering engine interface.
/// Implementations are per-backend (D3D12/Vulkan) × per-engine (Vello/Impeller).
class IRenderingEngine {
public:
    virtual ~IRenderingEngine() = default;

    /// Gets the engine type.
    virtual JaliumRenderingEngine GetType() const = 0;

    /// Initializes the engine.  Called once after construction.
    virtual bool Initialize() = 0;

    // ========================================================================
    // Per-Frame Lifecycle
    // ========================================================================

    /// Begin a new frame.  Clears internal buffers.
    virtual void BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) = 0;

    /// Set scissor rect for culling.
    virtual void SetScissorRect(float left, float top, float right, float bottom) = 0;

    /// Clear scissor rect.
    virtual void ClearScissorRect() = 0;

    // ========================================================================
    // Path Encoding
    // ========================================================================

    /// Encode a filled path.
    /// @param startX, startY  Starting point of the path.
    /// @param commands        Command buffer (tag 0=LineTo [0,x,y], tag 1=BezierTo [1,cp1x,cp1y,cp2x,cp2y,ex,ey]).
    /// @param commandLength   Number of floats in the command buffer.
    /// @param brush           Brush data for the fill.
    /// @param fillRule        Fill rule (EvenOdd or NonZero).
    /// @param transform       Current transform matrix.
    /// @return true on success, false if path is too complex (caller should fallback).
    virtual bool EncodeFillPath(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const EngineBrushData& brush,
        FillRule fillRule,
        const EngineTransform& transform) = 0;

    /// Encode a stroked path.
    virtual bool EncodeStrokePath(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const EngineBrushData& brush,
        float strokeWidth,
        bool closed,
        int32_t lineJoin,
        float miterLimit,
        int32_t lineCap,
        const float* dashPattern,
        uint32_t dashCount,
        float dashOffset,
        const EngineTransform& transform) = 0;

    /// Encode a filled polygon (triangulated internally by the engine).
    virtual bool EncodeFillPolygon(
        const float* points, uint32_t pointCount,
        const EngineBrushData& brush,
        FillRule fillRule,
        const EngineTransform& transform) = 0;

    /// Encode a filled ellipse.
    virtual bool EncodeFillEllipse(
        float cx, float cy, float rx, float ry,
        const EngineBrushData& brush,
        const EngineTransform& transform) = 0;

    // ========================================================================
    // GPU Execution
    // ========================================================================

    /// Execute the GPU pipeline and render all encoded paths to the target texture.
    /// @param commandList  Backend-specific command list (ID3D12GraphicsCommandList* or VkCommandBuffer).
    /// @param renderTarget Backend-specific render target texture.
    /// @param width, height Viewport dimensions.
    /// @return true on success.
    virtual bool Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) = 0;

    /// Returns true if there are encoded paths to render.
    virtual bool HasPendingWork() const = 0;

    /// Returns the number of paths encoded in the current frame.
    virtual uint32_t GetEncodedPathCount() const = 0;
};

} // namespace jalium
