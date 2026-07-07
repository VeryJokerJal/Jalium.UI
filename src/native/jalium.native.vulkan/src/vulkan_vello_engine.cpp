#include "vulkan_vello_engine.h"
#include "jalium_scanline_rasterizer.h"
#include "jalium_triangulate.h"
#include "jalium_path_stats.h"            // stroke cache hit/miss telemetry
#include "jalium_flatten.h"               // ScaleBucketFromMaxScale
#include <cstring>
#include <cmath>
#include <algorithm>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace jalium {

// Forward-declare HashPathInput (jalium_path_cache.h:113) rather than include
// that header — it also defines the JALIUM_API PathGeometryCache class whose
// std::list/unordered_map members emit a benign C4251 across the DLL boundary,
// and the stroke cache key only needs this transform-independent hash.
JALIUM_API uint64_t HashPathInput(float startX, float startY,
                                  const float* commands, uint32_t commandLength,
                                  int32_t fillRule, uint32_t scaleBucket) noexcept;

// ============================================================================
// VelloVulkanEngine — implementation. Mirrors ImpellerVulkanEngine, only
// differs in GetType() so the user-visible engine toggle works.
// ============================================================================

VelloVulkanEngine::VelloVulkanEngine(
    VkDevice device, VkPhysicalDevice physicalDevice,
    VkQueue computeQueue, uint32_t computeQueueFamily)
    : device_(device)
    , physicalDevice_(physicalDevice)
    , computeQueue_(computeQueue)
    , computeQueueFamily_(computeQueueFamily)
{
}

VelloVulkanEngine::~VelloVulkanEngine() = default;

bool VelloVulkanEngine::Initialize() {
    initialized_ = true;
    return true;
}

void VelloVulkanEngine::BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) {
    viewportW_ = viewportWidth;
    viewportH_ = viewportHeight;
    batches_.clear();
    encodedPathCount_ = 0;
    flatPoints_.clear();
    // Rounded-clip mirror is sticky like the scissor — reset per frame.
    hasRoundedClip_ = false;
    sceneEncoder_.BeginFrame(viewportWidth, viewportHeight);
}

void VelloVulkanEngine::SetScissorRect(float left, float top, float right, float bottom) {
    scissorLeft_ = left; scissorTop_ = top;
    scissorRight_ = right; scissorBottom_ = bottom;
    hasScissor_ = true;
    sceneEncoder_.SetScissor(left, top, right, bottom);
}

void VelloVulkanEngine::ClearScissorRect() {
    hasScissor_ = false;
    sceneEncoder_.ClearScissor();
}

bool VelloVulkanEngine::Execute(void* /*commandList*/, void* /*renderTarget*/,
                                uint32_t /*width*/, uint32_t /*height*/) {
    return true;
}

bool VelloVulkanEngine::HasPendingWork() const {
    return computeMode_ ? sceneEncoder_.HasWork() : !batches_.empty();
}

uint32_t VelloVulkanEngine::GetEncodedPathCount() const {
    return computeMode_ ? sceneEncoder_.PathCount() : encodedPathCount_;
}

bool VelloVulkanEngine::ExpandStroke(
    const EngineBrushData& brush,
    float strokeWidth,
    ImpellerJoin join, float miterLimit,
    ImpellerCap cap, bool closed,
    std::vector<Contour>* collectContours)
{
    uint32_t pointCount = (uint32_t)(flatPoints_.size() / 2);
    if (pointCount < 2) return false;

    VkVelloDrawBatch batch;
    bool ok = jalium::ExpandStrokePath<VkVelloVertex>(
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

namespace {

// FNV-1a 64-bit mix (file-local) — folds stroke params into the
// transform-independent HashPathInput key for the stroke mesh cache.
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
            case 0: {
                if (i + 2 >= commandLength) { i = commandLength; break; }
                float x = commands[i + 1], y = commands[i + 2];
                apply(x, y);
                outCommands.push_back(0.0f);
                outCommands.push_back(x);
                outCommands.push_back(y);
                i += 3;
                break;
            }
            case 1: {
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
            case 2: {
                if (i + 2 >= commandLength) { i = commandLength; break; }
                float x = commands[i + 1], y = commands[i + 2];
                apply(x, y);
                outCommands.push_back(2.0f);
                outCommands.push_back(x);
                outCommands.push_back(y);
                i += 3;
                break;
            }
            case 3: {
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
            case 5:
                outCommands.push_back(5.0f);
                i += 1;
                break;
            default:
                i = commandLength;
                break;
        }
    }
}

inline void EmitRectsAsBatch(
    const std::vector<PixelRect>& rects,
    float r, float g, float b, float a,
    VkVelloDrawBatch& batch)
{
    batch.vertices.reserve(batch.vertices.size() + rects.size() * 4);
    batch.indices.reserve(batch.indices.size() + rects.size() * 6);
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
}

} // namespace

bool VelloVulkanEngine::EncodeFillPath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform,
    int32_t edgeMode)
{
    (void)edgeMode;  // Vello path currently runs analytic AA only.
    if (computeMode_) {
        return sceneEncoder_.EncodeFillPath(startX, startY, commands, commandLength,
                                            brush, fillRule, transform);
    }
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

        VkVelloDrawBatch batch;
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

    // GPU stencil-then-cover: hand the pixel-space contours to the consumer as a
    // stencil batch instead of CPU-scanline-rasterizing them into pixel quads.
    // (E4: analytic-only mode routes to the scanline path below instead.)
    if (UseStencilPath()) {
        VkVelloDrawBatch batch;
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
        VkVelloDrawBatch batch;
        EmitRectsAsBatch(rects, r, g, b, a, batch);
        batch.pipelineType = 0;
        PushBatch(std::move(batch));
        encodedPathCount_++;
        return true;
    }

    bool anyEmitted = false;
    for (auto& c : contours) {
        uint32_t vc = c.VertexCount();
        if (vc < 3) continue;
        std::vector<uint32_t> indices;
        if (TriangulatePolygon(c.points.data(), vc, indices) && indices.size() >= 3) {
            VkVelloDrawBatch batch;
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
// Transform-independent stroke mesh cache (LRU) — mirrors ImpellerVulkanEngine.
// ============================================================================

std::shared_ptr<const VelloVulkanEngine::CachedStrokeRects>
VelloVulkanEngine::StrokeCacheFind(uint64_t key)
{
    auto it = strokeCacheMap_.find(key);
    if (it == strokeCacheMap_.end()) return nullptr;
    strokeCacheList_.splice(strokeCacheList_.begin(), strokeCacheList_, it->second);
    return it->second->entry;
}

void VelloVulkanEngine::StrokeCacheInsert(
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

bool VelloVulkanEngine::EncodeStrokePath(
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
    if (computeMode_) {
        return sceneEncoder_.EncodeStrokePath(startX, startY, commands, commandLength, brush,
                                              strokeWidth, closed, lineJoin, miterLimit, lineCap,
                                              dashPattern, dashCount, dashOffset, transform);
    }

    // Resolve edge mode (D3D12 parity): em<0 → 1 (cacheable feather mesh);
    // em==2 → explicit Antialiased (keep the legacy scanline/stencil body).
    int em = edgeMode;
    if (em < 0) em = 1;
    const bool analytic = (em == 2);

    // ── Transform-independent local-space stroke MESH cache (fast path) ───────
    // Same fix as ImpellerVulkanEngine::EncodeStrokePath (solid-only here): the
    // legacy body re-flattens + re-expands + (scanline | stencil) EVERY frame
    // with no memoization. Cache the source-space feather mesh keyed on
    // geometry + stroke params + scaleBucket (transform EXCLUDED) and per frame
    // only transform the cached vertices (O(N)) + recolor, emitting
    // pipelineType=0. Dashed / explicit-analytic / no-command / zero-width
    // defer to the legacy body below.
    if (StrokeCacheEnabled() && !analytic &&
        (!dashPattern || dashCount == 0) &&
        commands && commandLength > 0 && strokeWidth > 0.0f) {

        const float cMaxScale = MaxScale(transform);
        const uint32_t scaleBkt = ScaleBucketFromMaxScale(cMaxScale);

        uint64_t key = HashPathInput(startX, startY, commands, commandLength,
                                     /*fillRule*/ 0, scaleBkt);
        FnvMix64(key, &strokeWidth, sizeof(strokeWidth));
        uint8_t closedByte = closed ? 1 : 0;
        FnvMix64(key, &closedByte, sizeof(closedByte));
        FnvMix64(key, &lineJoin, sizeof(lineJoin));
        FnvMix64(key, &miterLimit, sizeof(miterLimit));
        FnvMix64(key, &lineCap, sizeof(lineCap));

        const float br = brush.r * brush.a;   // premultiplied solid color
        const float bg = brush.g * brush.a;
        const float bb = brush.b * brush.a;
        const float ba = brush.a;

        auto emitLocalMesh = [&](const CachedStrokeRects& m) {
            const size_t vc = m.positions.size() / 2;
            if (vc == 0 || m.indices.empty()) return;
            VkVelloDrawBatch batch;
            batch.vertices.resize(vc);
            batch.indices = m.indices;
            const float kInv255 = 1.0f / 255.0f;
            for (size_t i = 0; i < vc; ++i) {
                float lx = m.positions[i * 2], ly = m.positions[i * 2 + 1];
                float cov = (float)m.coverage[i] * kInv255;
                float vx = lx, vy = ly;
                TransformPoint(vx, vy, transform);
                batch.vertices[i] = VkVelloVertex{ vx, vy, br * cov, bg * cov, bb * cov, ba * cov };
            }
            batch.pipelineType = 0;
            PushBatch(std::move(batch));
            encodedPathCount_++;
        };

        if (auto cached = StrokeCacheFind(key)) {
            path_stats::AddStrokeHit(cached->positions.size() / 2);
            if (cached->positions.empty()) return false;
            emitLocalMesh(*cached);
            return true;
        }
        path_stats::AddStrokeMiss();

        const float srcTol = (cMaxScale > 0.001f)
            ? flattenTolerance_ / cMaxScale : flattenTolerance_;
        std::vector<Contour> srcContours = FlattenPathToContours(
            startX, startY, commands, commandLength, srcTol);

        auto cjoin = static_cast<ImpellerJoin>(lineJoin);
        auto ccap  = static_cast<ImpellerCap>(lineCap);
        std::vector<VkVelloVertex> meshVerts;
        std::vector<uint32_t>      meshIndices;
        meshVerts.reserve(srcContours.size() * 64);
        meshIndices.reserve(srcContours.size() * 96);
        const float featherSrcUnit = (cMaxScale > 1e-4f) ? (1.0f / cMaxScale) : 1.0f;
        for (auto& c : srcContours) {
            if (c.VertexCount() < 2) continue;
            jalium::ExpandStrokePath<VkVelloVertex>(
                meshVerts, meshIndices,
                c.points.data(), c.VertexCount(),
                strokeWidth, cjoin, miterLimit, ccap, closed,
                1.0f, 1.0f, 1.0f, 1.0f,         // opaque reference → .a = pure coverage
                /*collectContours*/ nullptr,
                featherSrcUnit);
        }

        auto entry = std::make_shared<CachedStrokeRects>();
        if (meshVerts.empty() || meshIndices.empty()) {
            StrokeCacheInsert(key, entry);   // negative cache
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
            float cov = v.a;
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
    // (E4: analytic-only mode falls through to the scanline path below.)
    if (UseStencilPath()) {
        VkVelloDrawBatch batch;
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

    VkVelloDrawBatch batch;
    EmitRectsAsBatch(rects, r, g, b, a, batch);
    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    encodedPathCount_++;
    return true;
}

bool VelloVulkanEngine::EncodeFillPolygon(
    const float* points, uint32_t pointCount,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform)
{
    if (computeMode_) {
        return sceneEncoder_.EncodeFillPolygon(points, pointCount, brush, fillRule, transform);
    }
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

    // GPU stencil-then-cover: emit the (transformed, pixel-space) polygon as a
    // stencil batch instead of CPU-scanline-rasterizing it into pixel quads.
    // (E4: analytic-only mode routes to the scanline path below instead.)
    if (UseStencilPath()) {
        VkVelloDrawBatch batch;
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

    VkVelloDrawBatch batch;
    EmitRectsAsBatch(rects, r, g, b, a, batch);
    batch.pipelineType = 0;
    PushBatch(std::move(batch));
    encodedPathCount_++;
    return true;
}

bool VelloVulkanEngine::EncodeFillEllipse(
    float cx, float cy, float rx, float ry,
    const EngineBrushData& brush,
    const EngineTransform& transform)
{
    if (computeMode_) {
        return sceneEncoder_.EncodeFillEllipse(cx, cy, rx, ry, brush, transform);
    }
    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    VkVelloDrawBatch batch;
    if (!jalium::GenerateFilledEllipseStrip<VkVelloVertex>(
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
