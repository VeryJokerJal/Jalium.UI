#pragma once

#include "jalium_rendering_engine.h"
#include "d3d12_backend.h"
#include "d3d12_triangulate.h"
#include <vector>
#include <cstdint>
#include <cmath>
#include <array>
#include <limits>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace jalium {

// ============================================================================
// ImpellerD3D12Engine — Flutter Impeller-style tessellation engine on D3D12
//
// Architecture (matches Flutter Impeller):
//   1. Shape detection → optimized parametric generators (circle, ellipse, rrect)
//   2. Convex path detection → triangle fan (O(n), no ear-clipping)
//   3. Complex path → FlattenPathToContours + TriangulateCompoundPath
//   4. Stroke expansion with prevent-overdraw + sharp angle bridging
//   5. Gradient support (linear/radial) via vertex color interpolation
//   6. GPU rasterization via own PSO on back buffer
// ============================================================================

/// Vertex layout for Impeller solid fill pipeline.
struct ImpellerVertex {
    float x, y;         // Position
    float r, g, b, a;   // Color (premultiplied alpha)
};
static_assert(sizeof(ImpellerVertex) == 24, "ImpellerVertex must be 24 bytes");

/// A batch of triangles to draw with a single PSO.
/// pipelineType: 0=solid fill (CPU-tessellated), 1=stencil-then-cover (GPU, deferred)
struct ImpellerDrawBatch {
    std::vector<ImpellerVertex> vertices;
    std::vector<uint32_t> indices;
    uint32_t pipelineType = 0;

    // --- Per-batch scissor (snapshot at encode time) ---
    bool hasScissor = false;
    float scissorL = 0, scissorT = 0, scissorR = 0, scissorB = 0;

    // --- Tile coverage (Flutter Impeller: Entity::GetCoverage) ---
    // Screen-space AABB of this batch. Used to cull batches outside the
    // viewport/scissor and to tighten the rasterizer scissor on submission,
    // mirroring Impeller's per-entity coverage tracking.
    bool hasCoverage = false;
    float coverageL = 0, coverageT = 0, coverageR = 0, coverageB = 0;

    // --- Stencil-then-cover data (pipelineType == 1) ---
    std::vector<Contour> stencilContours;   // original contours for stencil pass
    FillRule stencilFillRule = FillRule::EvenOdd;
    float stencilR = 0, stencilG = 0, stencilB = 0, stencilA = 0;
};

/// Stroke cap style.
enum class ImpellerCap : int32_t { Butt = 0, Square = 1, Round = 2 };

/// Stroke join style.
enum class ImpellerJoin : int32_t { Miter = 0, Bevel = 1, Round = 2 };

// ============================================================================
// Precomputed trig table (Flutter Impeller: Trigs class)
// ============================================================================

struct Trig {
    float cos, sin;
    Trig() : cos(1.0f), sin(0.0f) {}
    Trig(float c, float s) : cos(c), sin(s) {}
};

/// Precomputed quadrant division trig tables, cached per division count.
/// kCircleTolerance = 0.1 pixel max deviation from true circle arc.
class TrigCache {
public:
    static constexpr float kCircleTolerance = 0.1f;
    static constexpr size_t kCachedCount = 300;

    TrigCache();

    /// Get trig values for the given number of quadrant divisions.
    const std::vector<Trig>& Get(size_t divisions) const;

    /// Compute quadrant divisions for a given pixel radius.
    static size_t ComputeDivisions(float pixelRadius);

private:
    mutable std::vector<std::vector<Trig>> cache_;
    void Ensure(size_t divisions) const;
};

// ============================================================================
// ImpellerD3D12Engine
// ============================================================================

class ImpellerD3D12Engine : public IRenderingEngine {
public:
    explicit ImpellerD3D12Engine(ID3D12Device* device, DXGI_FORMAT rtvFormat = DXGI_FORMAT_R8G8B8A8_UNORM);
    ~ImpellerD3D12Engine() override;

    JaliumRenderingEngine GetType() const override { return JALIUM_ENGINE_IMPELLER; }
    bool Initialize() override;

    void BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) override;
    void SetScissorRect(float left, float top, float right, float bottom) override;
    void ClearScissorRect() override;

    bool EncodeFillPath(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const EngineBrushData& brush,
        FillRule fillRule,
        const EngineTransform& transform) override;

    bool EncodeStrokePath(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const EngineBrushData& brush,
        float strokeWidth, bool closed,
        int32_t lineJoin, float miterLimit,
        int32_t lineCap,
        const float* dashPattern, uint32_t dashCount, float dashOffset,
        const EngineTransform& transform) override;

    bool EncodeFillPolygon(
        const float* points, uint32_t pointCount,
        const EngineBrushData& brush,
        FillRule fillRule,
        const EngineTransform& transform) override;

    bool EncodeFillEllipse(
        float cx, float cy, float rx, float ry,
        const EngineBrushData& brush,
        const EngineTransform& transform) override;

    bool Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) override;
    bool HasPendingWork() const override;
    uint32_t GetEncodedPathCount() const override;

    ID3D12Resource* GetOutputTexture() const { return outputTexture_.Get(); }
    const std::vector<ImpellerDrawBatch>& GetBatches() const { return batches_; }
    void ClearBatches() { batches_.clear(); }

    /// Push a batch with current scissor state automatically captured.
    /// Also computes the batch's screen-space tile coverage AABB from its
    /// vertices (or stencil contours), used by ExecuteOnCommandList to cull
    /// off-screen batches and tighten per-batch scissor rects.
    void PushBatch(ImpellerDrawBatch&& batch) {
        batch.hasScissor = hasScissor_;
        if (hasScissor_) {
            batch.scissorL = scissorLeft_;
            batch.scissorT = scissorTop_;
            batch.scissorR = scissorRight_;
            batch.scissorB = scissorBottom_;
        }
        ComputeBatchCoverage(batch);
        batches_.push_back(std::move(batch));
    }

    /// Compute screen-space AABB for a batch from its vertices or stencil
    /// contours. Sets hasCoverage = false when no geometry is available.
    static void ComputeBatchCoverage(ImpellerDrawBatch& batch) {
        float minX =  std::numeric_limits<float>::infinity();
        float minY =  std::numeric_limits<float>::infinity();
        float maxX = -std::numeric_limits<float>::infinity();
        float maxY = -std::numeric_limits<float>::infinity();
        bool any = false;

        for (const auto& v : batch.vertices) {
            if (v.x < minX) minX = v.x;
            if (v.y < minY) minY = v.y;
            if (v.x > maxX) maxX = v.x;
            if (v.y > maxY) maxY = v.y;
            any = true;
        }
        if (batch.pipelineType == 1) {
            for (const auto& c : batch.stencilContours) {
                uint32_t n = c.VertexCount();
                for (uint32_t i = 0; i < n; ++i) {
                    float px = c.X(i);
                    float py = c.Y(i);
                    if (px < minX) minX = px;
                    if (py < minY) minY = py;
                    if (px > maxX) maxX = px;
                    if (py > maxY) maxY = py;
                    any = true;
                }
            }
        }
        if (!any || !(maxX >= minX) || !(maxY >= minY)) {
            batch.hasCoverage = false;
            return;
        }
        batch.hasCoverage = true;
        batch.coverageL = minX;
        batch.coverageT = minY;
        batch.coverageR = maxX;
        batch.coverageB = maxY;
    }

    bool ExecuteOnCommandList(
        ID3D12GraphicsCommandList* cmdList,
        D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle,
        D3D12_RECT scissor,
        uint32_t viewportW, uint32_t viewportH);

private:
    // --- Convex Detection & Fast Path ---

    /// Check if a polygon (flat point array) is convex.
    static bool IsConvexPolygon(const float* points, uint32_t pointCount);

    /// Tessellate a convex polygon as a triangle fan (O(n) vertices).
    /// Much faster than ear-clipping for convex shapes.
    bool TessellateConvexFan(const float* points, uint32_t pointCount,
                             float r, float g, float b, float a);

    // --- Optimized Shape Generators (Flutter Impeller-style) ---

    /// Generate a filled circle as triangle strip using precomputed trigs.
    bool GenerateFilledCircleStrip(float cx, float cy, float radius,
                                   float r, float g, float b, float a,
                                   const EngineTransform& transform);

    /// Generate a filled ellipse as triangle strip.
    bool GenerateFilledEllipseStrip(float cx, float cy, float rx, float ry,
                                    float r, float g, float b, float a,
                                    const EngineTransform& transform);

    /// Generate a filled rounded rect as triangle strip.
    bool GenerateFilledRoundRectStrip(float x, float y, float w, float h,
                                      float rx, float ry,
                                      float r, float g, float b, float a,
                                      const EngineTransform& transform);

    /// Generate a stroked circle as triangle strip (inner+outer ring).
    bool GenerateStrokedCircleStrip(float cx, float cy, float radius,
                                    float strokeWidth,
                                    float r, float g, float b, float a,
                                    const EngineTransform& transform);

    /// Generate a round-cap line as triangle strip (two hemicircles + rect).
    bool GenerateRoundCapLineStrip(float x0, float y0, float x1, float y1,
                                   float radius,
                                   float r, float g, float b, float a,
                                   const EngineTransform& transform);

    // --- Gradient Fill ---

    /// Encode a gradient-filled path (linear or radial).
    bool EncodeGradientFillPath(
        const std::vector<Contour>& contours,
        const EngineBrushData& brush,
        const EngineTransform& transform);

    // --- Legacy helpers ---

    void TransformPoint(float& x, float& y, const EngineTransform& t) const {
        float tx = t.m11 * x + t.m21 * y + t.dx;
        float ty = t.m12 * x + t.m22 * y + t.dy;
        x = tx;
        y = ty;
    }

    void FlattenPath(float startX, float startY,
                     const float* commands, uint32_t commandLength,
                     const EngineTransform& transform);

    void FlattenCubic(float x0, float y0, float x1, float y1,
                      float x2, float y2, float x3, float y3,
                      float tolerance);

    void FlattenQuadratic(float x0, float y0, float x1, float y1,
                          float x2, float y2, float tolerance);

    bool TessellateCurrentPath(const EngineBrushData& brush, FillRule fillRule);

    bool ExpandStroke(const EngineBrushData& brush,
                      float strokeWidth,
                      ImpellerJoin join, float miterLimit,
                      ImpellerCap cap, bool closed,
                      std::vector<Contour>* collectContours = nullptr);

    void GenerateRoundCap(std::vector<ImpellerVertex>& verts,
                          std::vector<uint32_t>& indices,
                          float cx, float cy,
                          float nx, float ny,
                          float halfWidth,
                          float r, float g, float b, float a,
                          bool isStart);

    void GenerateRoundJoin(std::vector<ImpellerVertex>& verts,
                           std::vector<uint32_t>& indices,
                           float cx, float cy,
                           float n0x, float n0y,
                           float n1x, float n1y,
                           float halfWidth,
                           float r, float g, float b, float a);

    static uint32_t ComputeQuadrantDivisions(float pixelRadius);

    // --- Alpha Coverage (Flutter Impeller-style) ---

    /// Compute stroke alpha coverage for subpixel strokes.
    static float ComputeStrokeAlphaCoverage(float strokeWidth, float transformScale);

    // --- Stencil-then-Cover (non-convex path fill) ---

    /// Fill a non-convex path using stencil buffer:
    ///  Pass 1: render all triangles (fan from origin) writing to stencil
    ///  Pass 2: cover bounding box, reading stencil (NonZero or EvenOdd)
    bool StencilThenCoverFill(
        const std::vector<Contour>& contours,
        FillRule fillRule,
        float r, float g, float b, float a,
        ID3D12GraphicsCommandList* cmdList,
        D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle,
        uint32_t viewportW, uint32_t viewportH);

    bool EnsureStencilResources(uint32_t w, uint32_t h);

    // --- GPU Resources ---

    bool CreatePipelines();
    bool CreateRootSignature();
    bool EnsureOutputTexture(uint32_t w, uint32_t h);
    bool EnsureVertexBuffer(size_t requiredBytes);
    bool EnsureIndexBuffer(size_t requiredBytes);

    ID3D12Device* device_;
    DXGI_FORMAT rtvFormat_;
    bool initialized_ = false;

    uint32_t viewportW_ = 0, viewportH_ = 0;

    float scissorLeft_ = 0, scissorTop_ = 0, scissorRight_ = 0, scissorBottom_ = 0;
    bool hasScissor_ = false;

    std::vector<float> flatPoints_;

    std::vector<ImpellerDrawBatch> batches_;
    uint32_t encodedPathCount_ = 0;

    float flattenTolerance_ = 0.25f;

    // Precomputed trig cache (shared across frames)
    TrigCache trigCache_;

    // --- D3D12 Resources ---

    ComPtr<ID3D12RootSignature> rootSignature_;
    ComPtr<ID3D12PipelineState> solidFillPSO_;

    ComPtr<ID3D12Resource> outputTexture_;
    uint32_t outputW_ = 0, outputH_ = 0;

    ComPtr<ID3D12Resource> vertexUploadBuffer_;
    ComPtr<ID3D12Resource> indexUploadBuffer_;
    size_t vertexUploadSize_ = 0;
    size_t indexUploadSize_ = 0;

    ComPtr<ID3D12Resource> vertexBuffer_;
    ComPtr<ID3D12Resource> indexBuffer_;
    size_t vertexBufferSize_ = 0;
    size_t indexBufferSize_ = 0;

    ComPtr<ID3D12DescriptorHeap> rtvHeap_;

    // Stencil-then-cover resources
    ComPtr<ID3D12PipelineState> stencilWritePSO_;   // writes to stencil, no color
    ComPtr<ID3D12PipelineState> stencilCoverNonZeroPSO_; // reads stencil != 0
    ComPtr<ID3D12PipelineState> stencilCoverEvenOddPSO_; // reads stencil bit 0
    ComPtr<ID3D12Resource> depthStencilBuffer_;
    ComPtr<ID3D12DescriptorHeap> dsvHeap_;
    uint32_t dsvW_ = 0, dsvH_ = 0;

    // Dedicated upload buffers for stencil pass (avoids overwriting solid batch data)
    ComPtr<ID3D12Resource> stencilVertexUploadBuffer_;
    ComPtr<ID3D12Resource> stencilIndexUploadBuffer_;
    size_t stencilVertexUploadSize_ = 0;
    size_t stencilIndexUploadSize_ = 0;

    bool EnsureStencilVertexBuffer(size_t requiredBytes);
    bool EnsureStencilIndexBuffer(size_t requiredBytes);
};

} // namespace jalium
