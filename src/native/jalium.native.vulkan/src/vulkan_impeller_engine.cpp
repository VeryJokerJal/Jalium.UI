#include "vulkan_impeller_engine.h"
#include "jalium_scanline_rasterizer.h"   // PixelRect / RasterizePathToRects
#include "jalium_triangulate.h"           // TriangulateCompoundPath / FlattenPathToContours
#include "jalium_path_stats.h"            // unified path telemetry
#include "jalium_flatten.h"               // ScaleBucketFromMaxScale
#include <cstring>
#include <cmath>
#include <algorithm>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace jalium {

// HashPathInput lives in jalium_path_cache.h, but that header also defines the
// JALIUM_API PathGeometryCache class whose std::list/unordered_map members emit
// a (benign) C4251 across the DLL boundary. We only need the transform-
// independent path hash for the stroke cache key, so forward-declare it rather
// than pull the whole header into this TU (signature mirrors jalium_path_cache.h:113).
JALIUM_API uint64_t HashPathInput(float startX, float startY,
                                  const float* commands, uint32_t commandLength,
                                  int32_t fillRule, uint32_t scaleBucket) noexcept;

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

// FNV-1a 64-bit mix (file-local; mirrors the D3D12 engine TU — its FnvMix64 is
// anonymous-namespace and not exported). Folds the stroke-shape params into the
// transform-independent HashPathInput geometry hash for the stroke mesh cache.
inline void FnvMix64(uint64_t& h, const void* data, size_t size) noexcept {
    auto* p = static_cast<const uint8_t*>(data);
    for (size_t i = 0; i < size; i++) {
        h ^= p[i];
        h *= 0x100000001B3ull;
    }
}

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
    const EngineTransform& transform,
    int32_t edgeMode)
{
    (void)edgeMode;  // Vulkan fill already runs analytic AA via RasterizePathToRects.
    // Gradient brushes flatten in source space because the gradient sampler
    // takes path-local coords; transform happens in the per-vertex bake step.
    if (brush.type == 1 || brush.type == 2 || brush.type == 3) {
        float gradMaxScale = MaxScale(transform);
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

        if (!brush.stops || brush.stopCount == 0) return false;

        int32_t fr = 0;
        std::vector<float> triVerts;
        {
            path_stats::ScopedTriangulateTimer triTimer;
            bool ok = TriangulateCompoundPath(gradContours, fr, triVerts) && triVerts.size() >= 6;
            if (ok) triTimer.MarkOk();
            if (!ok) return false;
        }

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

    std::vector<Contour> contours;
    {
        path_stats::ScopedFlattenTimer flattenTimer(commandLength);
        contours = FlattenPathToContours(
            pxStartX, pxStartY, pxCommands.data(), (uint32_t)pxCommands.size(),
            flattenTolerance_);
        uint64_t outputVerts = 0;
        for (const auto& c : contours) outputVerts += c.VertexCount();
        flattenTimer.RecordOutputVerts(outputVerts);
    }

    contours.erase(
        std::remove_if(contours.begin(), contours.end(),
            [](const Contour& c) { return c.VertexCount() < 3; }),
        contours.end());
    if (contours.empty()) return false;

    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    // GPU stencil-then-cover: hand the pixel-space contours to the consumer as a
    // stencil batch instead of CPU-scanline-rasterizing them into pixel quads.
    if (VkStencilPathEnabled()) {
        VkImpellerDrawBatch batch;
        if (!BuildVkStencilBatch(std::move(contours), fillRule, r, g, b, a, batch))
            return false;
        PushBatch(std::move(batch));
        encodedPathCount_++;
        return true;
    }

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
        bool triOk;
        {
            path_stats::ScopedTriangulateTimer triTimer;
            triOk = TriangulatePolygon(c.points.data(), vc, indices) && indices.size() >= 3;
            if (triOk) triTimer.MarkOk();
        }
        if (triOk) {
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
// Transform-independent stroke mesh cache (LRU) — mirrors ImpellerD3D12Engine.
// ============================================================================

std::shared_ptr<const ImpellerVulkanEngine::CachedStrokeRects>
ImpellerVulkanEngine::StrokeCacheFind(uint64_t key)
{
    auto it = strokeCacheMap_.find(key);
    if (it == strokeCacheMap_.end()) return nullptr;
    // Promote to head (most-recently-used).
    strokeCacheList_.splice(strokeCacheList_.begin(), strokeCacheList_, it->second);
    return it->second->entry;
}

void ImpellerVulkanEngine::StrokeCacheInsert(
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
    const EngineTransform& transform,
    int32_t edgeMode)
{
    // Resolve edge mode (D3D12 parity): em<0 → 1 (binary feather mesh, the
    // default + cacheable); em==2 → explicit Antialiased (keeps the legacy
    // analytic scanline/stencil body below). Today the rest of the Vulkan body
    // ignores edgeMode (it runs analytic AA unconditionally) — only the new
    // cached fast-path honors it, exactly like the D3D12 gate at
    // d3d12_impeller_engine.cpp:2513.
    int em = edgeMode;
    if (em < 0) em = 1;
    const bool analytic = (em == 2);

    // ── Transform-independent local-space stroke MESH cache (fast path) ───────
    // The legacy body below re-flattens + re-expands + (scanline | stencil-fan-
    // triangulates) EVERY frame because nothing here was memoized; for the
    // ~35 strokes/frame in DevTools that is the dominant CPU cost (StrokePath
    // 16.3ms). Here — mirroring ImpellerD3D12Engine::EncodeStrokePath — we
    // flatten + ExpandStrokePath ONCE in SOURCE space (stroke width in source
    // units → thickness scales with the transform, WPF Pen semantics), cache
    // the feathered triangle mesh keyed on geometry + stroke params +
    // scaleBucket (transform EXCLUDED), then each frame only transform the
    // cached vertices (O(N)) and recolor. Emits pipelineType=0 — bypassing BOTH
    // the CPU scanline AND the GPU stencil-then-cover, exactly like the gradient
    // branch below already does, just memoized. Solid AND linear/radial/sweep
    // gradient strokes take this path; dashed / explicit-analytic / no-command /
    // zero-width defer to the proven legacy body.
    const bool gradientStroke =
        (brush.type == 1 || brush.type == 2 || brush.type == 3) &&
        brush.stops && brush.stopCount > 0;
    if (StrokeCacheEnabled() && !analytic &&
        (!dashPattern || dashCount == 0) &&
        commands && commandLength > 0 && strokeWidth > 0.0f) {

        const float maxScale = MaxScale(transform);
        const uint32_t scaleBkt = ScaleBucketFromMaxScale(maxScale);

        // Key: geometry + scaleBucket (HashPathInput, transform-INDEPENDENT)
        // then the stroke-shape params. Brush is NOT in the key — the cached
        // mesh stores opaque coverage and is recolored at emit, so a solid and a
        // gradient stroke of the same shape share one entry.
        uint64_t key = HashPathInput(startX, startY, commands, commandLength,
                                     /*fillRule*/ 0, scaleBkt);
        FnvMix64(key, &strokeWidth, sizeof(strokeWidth));
        uint8_t closedByte = closed ? 1 : 0;
        FnvMix64(key, &closedByte, sizeof(closedByte));
        FnvMix64(key, &lineJoin, sizeof(lineJoin));
        FnvMix64(key, &miterLimit, sizeof(miterLimit));
        FnvMix64(key, &lineCap, sizeof(lineCap));

        // Gradient stops resolved once per call; sampled per cached vertex at
        // emit (in source space, where the cached positions live).
        std::vector<float> gradStopData;
        if (gradientStroke) FlattenGradientStops(brush, gradStopData);

        const float br = brush.r * brush.a;   // premultiplied solid color
        const float bg = brush.g * brush.a;
        const float bb = brush.b * brush.a;
        const float ba = brush.a;

        // Emit a cached source-space mesh: transform each vertex by the current
        // transform and reapply per-vertex feather coverage + brush/gradient
        // color. This is the ONLY per-frame CPU cost for a cache hit.
        auto emitLocalMesh = [&](const CachedStrokeRects& m) {
            const size_t vc = m.positions.size() / 2;
            if (vc == 0 || m.indices.empty()) return;
            VkImpellerDrawBatch batch;
            batch.vertices.resize(vc);
            batch.indices = m.indices;
            const float kInv255 = 1.0f / 255.0f;
            for (size_t i = 0; i < vc; ++i) {
                float lx = m.positions[i * 2], ly = m.positions[i * 2 + 1];
                float cov = (float)m.coverage[i] * kInv255;
                float vx = lx, vy = ly;                  // source space
                TransformPoint(vx, vy, transform);        // → pixel space
                if (gradientStroke) {
                    GradientColor gc = SampleBrushGradient(brush, gradStopData.data(), lx, ly);
                    float a = gc.a * cov;                 // premultiplied
                    batch.vertices[i] = VkImpellerVertex{ vx, vy, gc.r * a, gc.g * a, gc.b * a, a };
                } else {
                    batch.vertices[i] = VkImpellerVertex{ vx, vy, br * cov, bg * cov, bb * cov, ba * cov };
                }
            }
            batch.pipelineType = 0;
            PushBatch(std::move(batch));
            encodedPathCount_++;
        };

        if (auto cached = StrokeCacheFind(key)) {
            path_stats::AddStrokeHit(cached->positions.size() / 2);
            if (cached->positions.empty()) return false;   // negative cache hit
            emitLocalMesh(*cached);
            return true;
        }
        path_stats::AddStrokeMiss();

        // Miss: flatten in SOURCE space (tolerance scaled by 1/maxScale so the
        // on-screen smoothness matches this scale bucket) and expand the stroke
        // at source-unit width into a feathered binary mesh.
        const float srcTol = (maxScale > 0.001f)
            ? flattenTolerance_ / maxScale : flattenTolerance_;
        std::vector<Contour> srcContours;
        {
            path_stats::ScopedFlattenTimer flattenTimer(commandLength);
            srcContours = FlattenPathToContours(startX, startY, commands,
                                                commandLength, srcTol);
            uint64_t ov = 0;
            for (const auto& c : srcContours) ov += c.VertexCount();
            flattenTimer.RecordOutputVerts(ov);
        }

        auto join = static_cast<ImpellerJoin>(lineJoin);
        auto cap  = static_cast<ImpellerCap>(lineCap);
        std::vector<VkImpellerVertex> meshVerts;
        std::vector<uint32_t>         meshIndices;
        meshVerts.reserve(srcContours.size() * 64);
        meshIndices.reserve(srcContours.size() * 96);
        // Mesh built in SOURCE space → feather skirt sized 1/maxScale source
        // units so it stays 1 screen-pixel after the emit transform (without
        // this a stroke cached at 2× would render a 2px feather, ~2× too thick).
        const float featherSrcUnit = (maxScale > 1e-4f) ? (1.0f / maxScale) : 1.0f;
        for (auto& c : srcContours) {
            if (c.VertexCount() < 2) continue;
            jalium::ExpandStrokePath<VkImpellerVertex>(
                meshVerts, meshIndices,
                c.points.data(), c.VertexCount(),
                strokeWidth, join, miterLimit, cap, closed,
                // OPAQUE reference color → stored vertex .a is the pure feather
                // coverage, independent of the brush (applied fresh at emit).
                // Baking brush alpha would poison the geometry-keyed entry and
                // make a transparent/opacity-0 stroke vanish for all later
                // same-shape strokes (see d3d12_impeller_engine.cpp:2648).
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
        for (size_t i = 0; i < meshVerts.size(); ++i) {
            const auto& v = meshVerts[i];
            entry->positions[i * 2]     = v.x;
            entry->positions[i * 2 + 1] = v.y;
            float cov = v.a;                       // ref alpha 1 → .a is pure coverage
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

    // ── Legacy fallback (analytic / dashed / no-command / zero-width) ─────────

    // TRUE gradient strokes: build the stroke MESH in SOURCE space with a
    // reference opaque color (so each vertex .a is the pure feather coverage),
    // then recolor every vertex by sampling the gradient at its source position
    // and transforming the position to pixels. Mirrors the D3D12 engine and the
    // EncodeFillPath gradient branch. Dashed gradients fall through to the flat
    // first-stop path below.
    if ((brush.type == 1 || brush.type == 2 || brush.type == 3) &&
        brush.stops && brush.stopCount > 0 &&
        (!dashPattern || dashCount == 0) &&
        commands && commandLength > 0 && strokeWidth > 0.0f) {
        float gMaxScale = MaxScale(transform);
        float gTol = (gMaxScale > 0.001f) ? flattenTolerance_ / gMaxScale : flattenTolerance_;
        float featherSrc = (gMaxScale > 1e-4f) ? (1.0f / gMaxScale) : 1.0f;
        std::vector<Contour> srcContours =
            FlattenPathToContours(startX, startY, commands, commandLength, gTol);
        if (!srcContours.empty()) {
            auto gJoin = static_cast<ImpellerJoin>(lineJoin);
            auto gCap  = static_cast<ImpellerCap>(lineCap);
            std::vector<VkImpellerVertex> meshVerts;
            std::vector<uint32_t>         meshIndices;
            for (auto& c : srcContours) {
                if (c.VertexCount() < 2) continue;
                jalium::ExpandStrokePath<VkImpellerVertex>(
                    meshVerts, meshIndices,
                    c.points.data(), c.VertexCount(),
                    strokeWidth, gJoin, miterLimit, gCap, closed,
                    1.0f, 1.0f, 1.0f, 1.0f,   // reference color — recolored per vertex below
                    /*collectContours*/ nullptr, featherSrc);
            }
            if (!meshVerts.empty() && !meshIndices.empty()) {
                std::vector<float> stopData;
                FlattenGradientStops(brush, stopData);
                VkImpellerDrawBatch batch;
                batch.vertices.resize(meshVerts.size());
                batch.indices = meshIndices;
                for (size_t i = 0; i < meshVerts.size(); ++i) {
                    float lx = meshVerts[i].x, ly = meshVerts[i].y;   // source space
                    float cov = meshVerts[i].a;                        // feather coverage
                    GradientColor gc = SampleBrushGradient(brush, stopData.data(), lx, ly);
                    float vx = lx, vy = ly;
                    TransformPoint(vx, vy, transform);
                    float a = gc.a * cov;                              // premultiplied
                    batch.vertices[i] = VkImpellerVertex{ vx, vy, gc.r * a, gc.g * a, gc.b * a, a };
                }
                batch.pipelineType = 0;
                PushBatch(std::move(batch));
                encodedPathCount_++;
                return true;
            }
        }
        // Gradient stroke encode failed → fall through to the flat first-stop path.
    }

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

    std::vector<Contour> contours;
    {
        path_stats::ScopedFlattenTimer flattenTimer(commandLength);
        contours = FlattenPathToContours(
            pxStartX, pxStartY, pxCommands.data(), (uint32_t)pxCommands.size(),
            flattenTolerance_);
        uint64_t outputVerts = 0;
        for (const auto& c : contours) outputVerts += c.VertexCount();
        flattenTimer.RecordOutputVerts(outputVerts);
    }

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

    // GPU stencil-then-cover: a stroke renders as a filled outline (the expanded
    // stroke contours). Always NonZero — ExpandStroke emits overlapping CCW
    // triangle soup, which EvenOdd would punch holes in.
    if (VkStencilPathEnabled()) {
        VkImpellerDrawBatch batch;
        if (!BuildVkStencilBatch(std::move(strokeContours), FillRule::NonZero,
                                 brush.r * brush.a, brush.g * brush.a,
                                 brush.b * brush.a, brush.a, batch))
            return false;
        PushBatch(std::move(batch));
        encodedPathCount_++;
        return true;
    }

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

    // Gradient fill: triangulate the polygon and bake a true per-vertex gradient,
    // exactly like EncodeFillPath's gradient branch. The solid path below only
    // reads brush.r/g/b/a, which BuildEngineBrush leaves unset for gradient
    // brushes — so a gradient MUST take this branch or it renders garbage.
    if (brush.type == 1 || brush.type == 2 || brush.type == 3) {
        if (!brush.stops || brush.stopCount == 0) return false;

        std::vector<uint32_t> gradIndices;
        if (!TriangulatePolygon(points, pointCount, gradIndices) || gradIndices.size() < 3) {
            return false;
        }

        std::vector<float> stopData;
        FlattenGradientStops(brush, stopData);

        VkImpellerDrawBatch batch;
        batch.vertices.reserve(gradIndices.size());
        batch.indices.reserve(gradIndices.size());
        for (uint32_t idx = 0; idx < (uint32_t)gradIndices.size(); ++idx) {
            uint32_t vi = gradIndices[idx];
            float px = points[vi * 2], py = points[vi * 2 + 1];
            GradientColor gc = SampleBrushGradient(brush, stopData.data(), px, py);
            float vx = px, vy = py;
            TransformPoint(vx, vy, transform);
            batch.vertices.push_back({ vx, vy, gc.r * gc.a, gc.g * gc.a, gc.b * gc.a, gc.a });
            batch.indices.push_back(idx);
        }
        batch.pipelineType = 0;
        PushBatch(std::move(batch));
        encodedPathCount_++;
        return true;
    }

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

    // GPU stencil-then-cover: emit the (transformed, pixel-space) polygon as a
    // stencil batch instead of CPU-scanline-rasterizing it into pixel quads.
    if (VkStencilPathEnabled()) {
        VkImpellerDrawBatch batch;
        if (!BuildVkStencilBatch(std::move(contours), fillRule, r, g, b, a, batch))
            return false;
        PushBatch(std::move(batch));
        encodedPathCount_++;
        return true;
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
