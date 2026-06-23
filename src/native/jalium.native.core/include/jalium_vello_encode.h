#pragma once

// ============================================================================
// jalium_vello_encode.h
//
// Backend-agnostic, header-only CPU encoder that turns path/brush draw calls
// into the Vello GPU compute pipeline's on-wire scene buffers (PathSegment[],
// PathInfo[], PathDraw[], DrawTag[], VelloDrawMonoid[], VelloClipInp[],
// gradientRamps[]).
//
// This is a straight port of the D3D12 Vello CPU scene-encoder
// (src/native/jalium.native.d3d12/{include/d3d12_vello.h, src/d3d12_vello.cpp})
// with every backend dependency stripped out so the Vulkan Vello GPU pipeline
// can reuse the exact same encoding. The struct layouts below are the byte-exact
// contract with the SPIR-V / HLSL compute shaders
// (src/native/jalium.native.d3d12/shaders/vello_*.cs.hlsl + vello_shared.hlsli);
// do NOT reorder fields or change sizes.
//
// Scope (mirrors D3D12-current): solid + linear/radial/sweep gradient fills,
// stroke (CPU-widened to a NonZero fill), and scissor-as-clip (rectangular,
// emitted as a BeginClip/EndClip drawtag pair). Image brushes, blur-rect, and
// arbitrary clip masks are intentionally out of scope and stubbed/skipped.
// ============================================================================

#include <vector>
#include <cstdint>
#include <cmath>
#include <limits>
#include <algorithm>
#include <cstring>

#include "jalium_rendering_engine.h"  // EngineBrushData, EngineTransform, FillRule
#include "jalium_triangulate.h"       // FlattenCubicBezier/Quadratic/SvgArc, kTag*, DispatchPathCommand, Contour
#include "jalium_impeller_stroke.h"   // ExpandStrokePath (CPU stroke widening)

namespace jalium {

// ============================================================================
// GPU-side data structures — MUST match the HLSL/SPIR-V shaders byte-for-byte.
// Mirrored verbatim from d3d12_vello.h lines ~40-267. Same fields, same order,
// same static_assert sizes. These are the on-wire contract.
// ============================================================================

static constexpr uint32_t kVelloTileWidth  = 16;
static constexpr uint32_t kVelloTileHeight = 16;

// Path segment tags (matches CPU-side encoding consumed by vello_flatten).
static constexpr uint32_t kSegTagLineTo  = 0;
static constexpr uint32_t kSegTagQuadTo  = 1;
static constexpr uint32_t kSegTagCubicTo = 2;

// Fill rules.
static constexpr uint32_t kFillRuleEvenOdd = 0;
static constexpr uint32_t kFillRuleNonZero = 1;

// Brush types for PathDraw / PTCL commands.
static constexpr uint32_t kBrushSolid          = 0;
static constexpr uint32_t kBrushLinearGradient = 1;
static constexpr uint32_t kBrushRadialGradient = 2;
static constexpr uint32_t kBrushSweepGradient  = 3;
static constexpr uint32_t kBrushImage          = 4;

// Gradient extend modes.
static constexpr uint32_t kExtendPad     = 0;  // clamp to edge colors
static constexpr uint32_t kExtendRepeat  = 1;  // tile/repeat
static constexpr uint32_t kExtendReflect = 2;  // mirror/reflect

// Gradient ramp width (entries per gradient) and max gradients per frame.
static constexpr uint32_t kGradientRampWidth = 256;
static constexpr uint32_t kMaxGradients      = 64;

// Draw tag types — encodes per-path draw order for the coarse shader.
static constexpr uint32_t kDrawTagFill      = 0;  // regular fill path
static constexpr uint32_t kDrawTagBeginClip = 1;  // begin clip region
static constexpr uint32_t kDrawTagEndClip   = 2;  // end clip region
static constexpr uint32_t kDrawTagBlurRect  = 3;  // blur rect primitive

// A single path segment uploaded to GPU (48 bytes, matches HLSL).
struct PathSegment {
    float p0x, p0y;     // start point
    float p1x, p1y;     // control point 1 / end point (line)
    float p2x, p2y;     // control point 2 (cubic) / end point (quad)
    float p3x, p3y;     // end point (cubic)
    uint32_t tag;        // 0=line, 1=quad, 2=cubic
    uint32_t pathIndex;  // which path this segment belongs to
    uint32_t pad0, pad1;
};
static_assert(sizeof(PathSegment) == 48, "PathSegment must be 48 bytes");

// Draw tag entry (16 bytes) — uploaded to GPU for coarse shader draw ordering.
struct DrawTag {
    uint32_t tag;          // kDrawTagFill/BeginClip/EndClip/BlurRect
    uint32_t pathIdx;      // index into PathInfo/PathDraw arrays
    uint32_t blendMode;    // for EndClip: Porter-Duff blend mode
    float    alpha;        // for EndClip: clip alpha
};
static_assert(sizeof(DrawTag) == 16, "DrawTag must be 16 bytes");

// Per-path metadata (32 bytes).
struct PathInfo {
    uint32_t segOffset;    // offset into PathSegment buffer
    uint32_t segCount;     // number of segments in this path
    uint32_t fillRule;     // 0=EvenOdd, 1=NonZero
    uint32_t tileOffset;   // offset into per-path tile array
    uint32_t tileBboxX;    // tile-level bbox (in tiles, not pixels)
    uint32_t tileBboxY;
    uint32_t tileBboxW;    // width in tiles
    uint32_t tileBboxH;    // height in tiles
};
static_assert(sizeof(PathInfo) == 32, "PathInfo must be 32 bytes");

// Per-path draw info (64 bytes, supports solid color + gradients).
//
// Gradient parameter packing (ported EXACTLY from D3D12 EncodeFillPathBrush):
//   brushType == kBrushSolid:
//       colorR/G/B/A = premultiplied solid color.
//   brushType == kBrushLinearGradient:
//       gradParam0 = p0.x (transformed), gradParam1 = p0.y,
//       gradParam2 = p1.x (transformed), gradParam3 = p1.y,
//       gradParam4 = extendMode as float (D3D12 hardcodes kExtendPad for linear),
//       colorA     = opacity (fine shader modulates the ramp by this),
//       colorR/G/B = 0.
//   brushType == kBrushRadialGradient:
//       gradParam0 = center.x (transformed), gradParam1 = center.y,
//       gradParam2 = radius.x * scaleX,      gradParam3 = radius.y * scaleY,
//       gradParam4 = origin.x (transformed), gradParam5 = origin.y,
//       colorA     = opacity,
//       colorR     = extendMode bit-cast into a float (memcpy uint->float),
//       colorG/B   = 0.
//   brushType == kBrushSweepGradient:
//       gradParam0 = center.x (transformed), gradParam1 = center.y,
//       gradParam2 = t0 (start angle normalized), gradParam3 = t1 (end angle),
//       gradParam4 = extendMode as float,
//       colorA     = opacity, colorR/G/B = 0.
struct PathDraw {
    float colorR, colorG, colorB, colorA;          // premultiplied solid color
    float bboxMinX, bboxMinY, bboxMaxX, bboxMaxY;  // pixel-space bounds
    uint32_t brushType;      // 0=solid, 1=linear, 2=radial, 3=sweep
    uint32_t gradientIndex;  // index into gradient ramp array
    float gradParam0;
    float gradParam1;
    float gradParam2;
    float gradParam3;
    float gradParam4;
    float gradParam5;
};
static_assert(sizeof(PathDraw) == 64, "PathDraw must be 64 bytes");

// VelloDrawMonoid: per-draw-object prefix sum (16 bytes).
struct VelloDrawMonoid {
    uint32_t path_ix;
    uint32_t clip_ix;
    uint32_t scene_offset;
    uint32_t info_offset;
};
static_assert(sizeof(VelloDrawMonoid) == 16, "VelloDrawMonoid must be 16 bytes");

// VelloClipInp: input for GPU clip pipeline (8 bytes, matches HLSL ClipInp).
//   BeginClip: ix >= 0 (the draw object index)
//   EndClip:   ix < 0  (-(draw_object_index) - 1)
//   path_ix:   the path geometry index that defines the clip shape; EndClip
//              shares its matching BeginClip's path_ix.
struct VelloClipInp {
    int32_t ix;
    int32_t path_ix;
};
static_assert(sizeof(VelloClipInp) == 8, "VelloClipInp must be 8 bytes");

// ============================================================================
// Color-space helpers for gradient ramp interpolation.
// Ported verbatim from d3d12_vello.cpp (anon namespace, ~553-587).
// ============================================================================
namespace vello_detail {

inline float SrgbToLinear(float c) {
    return (c <= 0.04045f) ? c / 12.92f : std::pow((c + 0.055f) / 1.055f, 2.4f);
}
inline float LinearToSrgb(float c) {
    c = std::max(0.0f, std::min(1.0f, c));
    return (c <= 0.0031308f) ? c * 12.92f : 1.055f * std::pow(c, 1.0f / 2.4f) - 0.055f;
}

struct OKLab { float L, a, b; };

inline OKLab SrgbToOklab(float sr, float sg, float sb) {
    float r = SrgbToLinear(sr), g = SrgbToLinear(sg), b = SrgbToLinear(sb);
    float l = 0.4122214708f*r + 0.5363325363f*g + 0.0514459929f*b;
    float m = 0.2119034982f*r + 0.6806995451f*g + 0.1073969566f*b;
    float s = 0.0883024619f*r + 0.2817188376f*g + 0.6299787005f*b;
    float l_ = std::cbrt(l), m_ = std::cbrt(m), s_ = std::cbrt(s);
    return {
        0.2104542553f*l_ + 0.7936177850f*m_ - 0.0040720468f*s_,
        1.9779984951f*l_ - 2.4285922050f*m_ + 0.4505937099f*s_,
        0.0259040371f*l_ + 0.7827717662f*m_ - 0.8086757660f*s_
    };
}
inline void OklabToSrgb(float L, float a, float b, float& sr, float& sg, float& sb) {
    float l_ = L + 0.3963377774f*a + 0.2158037573f*b;
    float m_ = L - 0.1055613458f*a - 0.0638541728f*b;
    float s_ = L - 0.0894841775f*a - 1.2914855480f*b;
    float l = l_*l_*l_, m = m_*m_*m_, s = s_*s_*s_;
    float rl = +4.0767416621f*l - 3.3077115913f*m + 0.2309699292f*s;
    float gl = -1.2684380046f*l + 2.6097574011f*m - 0.3413193965f*s;
    float bl = -0.0041960863f*l - 0.7034186147f*m + 1.7076147010f*s;
    sr = LinearToSrgb(rl); sg = LinearToSrgb(gl); sb = LinearToSrgb(bl);
}

}  // namespace vello_detail

// ============================================================================
// VelloScene — the CPU-encoded scene buffers consumed by the GPU pipeline.
// ============================================================================
struct VelloScene {
    std::vector<PathSegment>     segments;
    std::vector<PathInfo>        pathInfos;
    std::vector<PathDraw>        pathDraws;
    std::vector<DrawTag>         drawTags;
    std::vector<VelloDrawMonoid> drawMonoids;    // filled by VelloSceneEncoder::Finalize()
    std::vector<VelloClipInp>    clipInps;        // filled by Finalize()
    std::vector<uint32_t>        gradientRamps;   // gradientCount * kGradientRampWidth entries
    uint32_t numPaths      = 0;   // paths carrying geometry (Fill/BeginClip/BlurRect)
    uint32_t numDrawObjs   = 0;   // == drawTags.size()
    uint32_t numClipOps    = 0;   // count of BeginClip + EndClip drawtags
    uint32_t gradientCount = 0;
    uint32_t viewportW     = 0;
    uint32_t viewportH     = 0;

    void Clear() {
        segments.clear();
        pathInfos.clear();
        pathDraws.clear();
        drawTags.clear();
        drawMonoids.clear();
        clipInps.clear();
        gradientRamps.clear();
        numPaths      = 0;
        numDrawObjs   = 0;
        numClipOps    = 0;
        gradientCount = 0;
        // viewportW/H are reset by BeginFrame, left intact here so a bare Clear()
        // followed by direct buffer inspection still reports the last viewport.
    }

    bool Empty() const { return drawTags.empty(); }
};

// ============================================================================
// VelloSceneEncoder — walks draw calls into a VelloScene.
// ============================================================================
class VelloSceneEncoder {
public:
    static constexpr uint32_t kTileSize = 16;

    // ------------------------------------------------------------------------
    // Per-frame lifecycle
    // ------------------------------------------------------------------------

    // Clears the scene, sets the viewport, and recomputes the tile grid.
    // Mirrors D3D12VelloRenderer::BeginFrame.
    void BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) {
        scene_.Clear();
        scene_.viewportW = viewportWidth;
        scene_.viewportH = viewportHeight;
        tilesX_ = (viewportWidth + kTileSize - 1) / kTileSize;
        tilesY_ = (viewportHeight + kTileSize - 1) / kTileSize;
        totalPathTiles_ = 0;

        hasScissor_   = false;
        scissorActive_ = false;
        scissorL_ = scissorT_ = scissorR_ = scissorB_ = 0.0f;
        clipStack_.clear();
    }

    // Sets the scissor rect. Like D3D12, this both (a) clamps subsequent path
    // tile bboxes for culling, AND (b) is realized as a rectangular BeginClip
    // (emitted lazily on the next draw) so the GPU clip pipeline masks pixels
    // exactly to the scissor. Changing the scissor closes any previously open
    // scissor-clip and opens a new one on the next encoded draw.
    void SetScissor(float l, float t, float r, float b) {
        // No-op if identical to the currently requested scissor.
        if (hasScissor_ && l == scissorL_ && t == scissorT_ &&
            r == scissorR_ && b == scissorB_) {
            return;
        }
        CloseScissorClip();
        scissorL_ = l; scissorT_ = t; scissorR_ = r; scissorB_ = b;
        hasScissor_ = true;
    }

    // Clears the scissor and closes any open scissor-clip.
    void ClearScissor() {
        CloseScissorClip();
        hasScissor_ = false;
    }

    // ------------------------------------------------------------------------
    // Path encoding
    // ------------------------------------------------------------------------

    bool EncodeFillPath(float startX, float startY,
                        const float* commands, uint32_t commandLength,
                        const EngineBrushData& brush, FillRule fillRule,
                        const EngineTransform& transform) {
        EnsureScissorClip();

        uint32_t rule = (fillRule == FillRule::NonZero) ? kFillRuleNonZero : kFillRuleEvenOdd;
        uint32_t pathIdx = 0;
        if (!EncodePathGeometry(startX, startY, commands, commandLength, rule,
                                transform, pathIdx)) {
            return false;
        }

        PathDraw draw = {};
        draw.bboxMinX = bboxMinX_;
        draw.bboxMinY = bboxMinY_;
        draw.bboxMaxX = bboxMaxX_;
        draw.bboxMaxY = bboxMaxY_;
        PackBrush(brush, transform, /*opacity*/ 1.0f, draw);

        scene_.pathDraws.push_back(draw);
        scene_.drawTags.push_back({ kDrawTagFill, pathIdx, 0, 0 });
        return true;
    }

    // CPU-widens the stroke into fill geometry (quads + joins + caps), then
    // encodes the result as a single NonZero fill path — matching the D3D12
    // visual result. Uses jalium::ExpandStrokePath to widen the flattened
    // polyline into per-triangle contours (each winding-normalized to CCW so
    // overlapping triangles union under NonZero), which are emitted as
    // PathSegment line loops. Dash patterns are expanded by splitting the
    // polyline into "on" sub-strokes and widening each independently.
    bool EncodeStrokePath(float startX, float startY,
                          const float* commands, uint32_t commandLength,
                          const EngineBrushData& brush, float strokeWidth, bool closed,
                          int32_t lineJoin, float miterLimit, int32_t lineCap,
                          const float* dashPattern, uint32_t dashCount, float dashOffset,
                          const EngineTransform& transform) {
        // Flatten to a polyline (tol 0.25, same as D3D12 EncodeStrokePath).
        std::vector<float> pts =
            FlattenPathCommands(startX, startY, commands, commandLength, 0.25f);

        // A ClosePath command forces closed regardless of the caller's flag.
        for (uint32_t ci = 0; ci < commandLength; ) {
            int tag = (int)commands[ci];
            if (tag == kTagClosePath) { closed = true; break; }
            else if (tag == kTagLineTo)  ci += 3;
            else if (tag == kTagCubicTo) ci += 7;
            else if (tag == kTagMoveTo)  ci += 3;
            else if (tag == kTagQuadTo)  ci += 5;
            else if (tag == kTagArcTo)   ci += 8;
            else ci += 1;
        }

        uint32_t pointCount = (uint32_t)(pts.size() / 2);
        if (pointCount < 2) return false;

        // Collect widened stroke geometry as per-triangle CCW contours.
        std::vector<Contour> contours;

        if (dashPattern && dashCount > 0) {
            // Walk the dash pattern, widening each "on" sub-stroke separately.
            ForEachDashSubStroke(
                pts.data(), pointCount, dashPattern, dashCount, dashOffset,
                [&](const float* subPts, uint32_t subCount) {
                    if (subCount < 2) return;
                    WidenIntoContours(subPts, subCount, strokeWidth,
                                      lineJoin, miterLimit, lineCap,
                                      /*closed*/ false, brush, contours);
                });
        } else {
            WidenIntoContours(pts.data(), pointCount, strokeWidth,
                              lineJoin, miterLimit, lineCap,
                              closed, brush, contours);
        }

        if (contours.empty()) return false;

        // The stroke was flattened and widened in LOCAL (untransformed) space —
        // exactly like D3D12, which widens in local space then transforms every
        // segment point. Apply the affine to all widened contour points now so
        // the encoded geometry lands in device space. Gradient endpoints are
        // still local-space, so the real transform is passed to PackBrush below.
        const float m11 = transform.m11, m12 = transform.m12;
        const float m21 = transform.m21, m22 = transform.m22;
        const float tdx = transform.dx,  tdy = transform.dy;
        bool hasTransform = (m11 != 1.0f || m12 != 0.0f || m21 != 0.0f ||
                             m22 != 1.0f || tdx != 0.0f || tdy != 0.0f);
        if (hasTransform) {
            for (auto& c : contours) {
                for (size_t k = 0; k + 1 < c.points.size(); k += 2) {
                    float px = c.points[k], py = c.points[k + 1];
                    c.points[k]     = px * m11 + py * m21 + tdx;
                    c.points[k + 1] = px * m12 + py * m22 + tdy;
                }
            }
        }

        EnsureScissorClip();

        // Encode all stroke contours (now device-space) as one NonZero fill path.
        uint32_t pathIdx = 0;
        if (!EncodeContoursGeometry(contours, kFillRuleNonZero, pathIdx)) {
            return false;
        }

        // Brush packing. PackBrush handles solid (premultiplied) and gradient
        // brushes; gradient endpoints are local-space and get transformed here.
        PathDraw draw = {};
        draw.bboxMinX = bboxMinX_;
        draw.bboxMinY = bboxMinY_;
        draw.bboxMaxX = bboxMaxX_;
        draw.bboxMaxY = bboxMaxY_;
        PackBrush(brush, transform, /*opacity*/ 1.0f, draw);

        scene_.pathDraws.push_back(draw);
        scene_.drawTags.push_back({ kDrawTagFill, pathIdx, 0, 0 });
        return true;
    }

    // Builds a LineTo command stream (closing back to point[0]) and forwards to
    // EncodeFillPath. Mirrors d3d12_vello_engine.cpp EncodeFillPolygon.
    bool EncodeFillPolygon(const float* points, uint32_t pointCount,
                           const EngineBrushData& brush, FillRule fillRule,
                           const EngineTransform& transform) {
        if (pointCount < 3 || points == nullptr) return false;

        std::vector<float> commands;
        commands.reserve((size_t)pointCount * 3);
        for (uint32_t i = 1; i < pointCount; ++i) {
            commands.push_back((float)kTagLineTo);
            commands.push_back(points[i * 2]);
            commands.push_back(points[i * 2 + 1]);
        }
        // Close the polygon back to the first point.
        commands.push_back((float)kTagLineTo);
        commands.push_back(points[0]);
        commands.push_back(points[1]);

        return EncodeFillPath(points[0], points[1],
                              commands.data(), (uint32_t)commands.size(),
                              brush, fillRule, transform);
    }

    // Approximates the ellipse with 4 cubic beziers (kappa = 4*(sqrt(2)-1)/3)
    // and forwards to EncodeFillPath. Mirrors d3d12_vello_engine.cpp.
    bool EncodeFillEllipse(float cx, float cy, float rx, float ry,
                           const EngineBrushData& brush, const EngineTransform& transform) {
        constexpr float kappa = 0.5522847498f;
        float kx = rx * kappa;
        float ky = ry * kappa;

        float startX = cx;
        float startY = cy - ry;

        // tag 1 = CubicBezierTo [1, cp1x, cp1y, cp2x, cp2y, ex, ey]
        const float commands[] = {
            1, cx + kx, cy - ry,  cx + rx, cy - ky,  cx + rx, cy,
            1, cx + rx, cy + ky,  cx + kx, cy + ry,  cx,      cy + ry,
            1, cx - kx, cy + ry,  cx - rx, cy + ky,  cx - rx, cy,
            1, cx - rx, cy - ky,  cx - kx, cy - ry,  cx,      cy - ry,
        };

        return EncodeFillPath(startX, startY,
                              commands, sizeof(commands) / sizeof(float),
                              brush, FillRule::NonZero, transform);
    }

    // ------------------------------------------------------------------------
    // Finalize — close any open scissor-clip, then compute draw monoids and the
    // clip-input array (with the begin/end path_ix fixup), exactly mirroring the
    // D3D12 ComputeDrawMonoids + Dispatch clipInp construction.
    // ------------------------------------------------------------------------
    void Finalize() {
        CloseScissorClip();

        const uint32_t numDrawObjs = (uint32_t)scene_.drawTags.size();
        scene_.numDrawObjs = numDrawObjs;

        // Count clip ops (BeginClip + EndClip drawtags) and geometry-carrying
        // paths (Fill/BeginClip/BlurRect).
        uint32_t numClipOps = 0;
        for (const auto& dt : scene_.drawTags) {
            if (dt.tag == kDrawTagBeginClip || dt.tag == kDrawTagEndClip) numClipOps++;
        }
        scene_.numClipOps = numClipOps;
        scene_.numPaths   = (uint32_t)scene_.pathInfos.size();
        scene_.gradientCount = scene_.gradientRamps.empty()
            ? 0u
            : (uint32_t)(scene_.gradientRamps.size() / kGradientRampWidth);

        // ── Draw monoids (CPU prefix sum) ──
        // path_ix increments on Fill/BeginClip/BlurRect.
        // clip_ix increments on BOTH BeginClip and EndClip.
        // scene_offset = info_offset = draw index.
        // (The GPU clip_leaf stage overwrites monoid.path_ix later for clip
        //  draws, so the split is preserved exactly as D3D12 does it.)
        auto& monoids = scene_.drawMonoids;
        monoids.resize(numDrawObjs);
        {
            uint32_t path_ix = 0, clip_ix = 0;
            for (uint32_t i = 0; i < numDrawObjs; ++i) {
                monoids[i].path_ix      = path_ix;
                monoids[i].clip_ix      = clip_ix;
                monoids[i].scene_offset = i;
                monoids[i].info_offset  = i;

                const auto& dt = scene_.drawTags[i];
                if (dt.tag == kDrawTagFill || dt.tag == kDrawTagBeginClip ||
                    dt.tag == kDrawTagBlurRect) {
                    path_ix++;
                }
                if (dt.tag == kDrawTagBeginClip || dt.tag == kDrawTagEndClip) {
                    clip_ix++;
                }
            }
        }

        // ── ClipInp array + draw-monoid fixup for matched begin/end clips ──
        // BeginClip → ix = draw_obj_index (positive).
        // EndClip   → ix = -(draw_obj_index) - 1 (negative); shares the
        //             matching BeginClip's path_ix/scene_offset/info_offset.
        auto& clipInps = scene_.clipInps;
        clipInps.clear();
        if (numClipOps > 0) {
            clipInps.resize(numClipOps);
            uint32_t clipIdx = 0;
            std::vector<uint32_t> clipBeginStack;
            for (uint32_t i = 0; i < numDrawObjs; ++i) {
                const auto& dt = scene_.drawTags[i];
                if (dt.tag == kDrawTagBeginClip) {
                    VelloClipInp ci;
                    ci.ix = (int32_t)i;                       // positive → BeginClip
                    ci.path_ix = (int32_t)monoids[i].path_ix;
                    clipInps[clipIdx++] = ci;
                    clipBeginStack.push_back(i);
                } else if (dt.tag == kDrawTagEndClip) {
                    VelloClipInp ci;
                    ci.ix = -(int32_t)i - 1;                  // negative → EndClip
                    if (!clipBeginStack.empty()) {
                        uint32_t beginIdx = clipBeginStack.back();
                        clipBeginStack.pop_back();
                        ci.path_ix = (int32_t)monoids[beginIdx].path_ix;
                        monoids[i].path_ix      = monoids[beginIdx].path_ix;
                        monoids[i].scene_offset = monoids[beginIdx].scene_offset;
                        monoids[i].info_offset  = monoids[beginIdx].info_offset;
                    } else {
                        ci.path_ix = 0;
                    }
                    clipInps[clipIdx++] = ci;
                }
            }
        }
    }

    // ------------------------------------------------------------------------
    // Accessors
    // ------------------------------------------------------------------------
    const VelloScene& scene() const { return scene_; }
    bool HasWork() const { return !scene_.drawTags.empty(); }
    uint32_t PathCount() const { return scene_.numPaths; }
    uint32_t TilesX() const { return (scene_.viewportW + kTileSize - 1) / kTileSize; }
    uint32_t TilesY() const { return (scene_.viewportH + kTileSize - 1) / kTileSize; }

private:
    // ========================================================================
    // Core geometry encoder — ports D3D12VelloRenderer::EncodePathGeometry.
    //
    // Walks the float command stream into PathSegment[] with implicit subpath
    // closes, applies the affine transform per point, CPU-flattens beziers via
    // the shared flatten helpers (tol 0.25) ONLY to compute the path bbox,
    // recomputes the bbox from the flattened lines, rejects paths whose bbox
    // exceeds 16384px (returns false), clamps to the scissor, computes the
    // per-path TILE bbox using the exact exclusive-ceil formula, and appends a
    // PathInfo. On success, bboxMin/Max_ hold the final pixel-space bounds and
    // outPathIdx is the new path index.
    // ========================================================================
    bool EncodePathGeometry(float startX, float startY,
                            const float* commands, uint32_t commandLength,
                            uint32_t fillRule, const EngineTransform& t,
                            uint32_t& outPathIdx) {
        const float m11 = t.m11, m12 = t.m12, m21 = t.m21, m22 = t.m22, tdx = t.dx, tdy = t.dy;

        uint32_t pathIdx = (uint32_t)scene_.pathInfos.size();
        uint32_t segOffset = (uint32_t)scene_.segments.size();

        bboxMinX_ = startX; bboxMinY_ = startY;
        bboxMaxX_ = startX; bboxMaxY_ = startY;

        float curX = startX, curY = startY;
        float moveX = startX, moveY = startY;
        bool hasDrawnInSubpath = false;

        auto pushLine = [&](float x0, float y0, float x1, float y1) {
            PathSegment seg = {};
            seg.p0x = x0; seg.p0y = y0;
            seg.p1x = x1; seg.p1y = y1;
            seg.p2x = x1; seg.p2y = y1;
            seg.p3x = x1; seg.p3y = y1;
            seg.tag = kSegTagLineTo; seg.pathIndex = pathIdx;
            scene_.segments.push_back(seg);
        };

        uint32_t ci = 0;
        bool done = false;
        while (ci < commandLength && !done) {
            int tag = (int)commands[ci];
            switch (tag) {
            case kTagMoveTo: {
                if (ci + 3 > commandLength) { done = true; break; }
                if (hasDrawnInSubpath && (curX != moveX || curY != moveY)) {
                    pushLine(curX, curY, moveX, moveY);
                }
                curX = commands[ci + 1]; curY = commands[ci + 2];
                moveX = curX; moveY = curY;
                hasDrawnInSubpath = false;
                ci += 3;
                break;
            }
            case kTagLineTo: {
                if (ci + 3 > commandLength) { done = true; break; }
                float ex = commands[ci + 1], ey = commands[ci + 2];
                pushLine(curX, curY, ex, ey);
                bboxMinX_ = std::min(bboxMinX_, ex);
                bboxMinY_ = std::min(bboxMinY_, ey);
                bboxMaxX_ = std::max(bboxMaxX_, ex);
                bboxMaxY_ = std::max(bboxMaxY_, ey);
                curX = ex; curY = ey;
                hasDrawnInSubpath = true;
                ci += 3;
                break;
            }
            case kTagQuadTo: {
                if (ci + 5 > commandLength) { done = true; break; }
                float cpx = commands[ci + 1], cpy = commands[ci + 2];
                float ex = commands[ci + 3], ey = commands[ci + 4];
                PathSegment seg = {};
                seg.p0x = curX; seg.p0y = curY;
                seg.p1x = cpx;  seg.p1y = cpy;
                seg.p2x = ex;   seg.p2y = ey;
                seg.p3x = ex;   seg.p3y = ey;
                seg.tag = kSegTagQuadTo; seg.pathIndex = pathIdx;
                scene_.segments.push_back(seg);
                bboxMinX_ = std::min({bboxMinX_, cpx, ex});
                bboxMinY_ = std::min({bboxMinY_, cpy, ey});
                bboxMaxX_ = std::max({bboxMaxX_, cpx, ex});
                bboxMaxY_ = std::max({bboxMaxY_, cpy, ey});
                curX = ex; curY = ey;
                hasDrawnInSubpath = true;
                ci += 5;
                break;
            }
            case kTagCubicTo: {
                if (ci + 7 > commandLength) { done = true; break; }
                float c1x = commands[ci + 1], c1y = commands[ci + 2];
                float c2x = commands[ci + 3], c2y = commands[ci + 4];
                float ex = commands[ci + 5], ey = commands[ci + 6];
                PathSegment seg = {};
                seg.p0x = curX; seg.p0y = curY;
                seg.p1x = c1x;  seg.p1y = c1y;
                seg.p2x = c2x;  seg.p2y = c2y;
                seg.p3x = ex;   seg.p3y = ey;
                seg.tag = kSegTagCubicTo; seg.pathIndex = pathIdx;
                scene_.segments.push_back(seg);
                bboxMinX_ = std::min({bboxMinX_, c1x, c2x, ex});
                bboxMinY_ = std::min({bboxMinY_, c1y, c2y, ey});
                bboxMaxX_ = std::max({bboxMaxX_, c1x, c2x, ex});
                bboxMaxY_ = std::max({bboxMaxY_, c1y, c2y, ey});
                curX = ex; curY = ey;
                hasDrawnInSubpath = true;
                ci += 7;
                break;
            }
            case kTagArcTo: {
                if (ci + 8 > commandLength) { done = true; break; }
                float ex = commands[ci + 1], ey = commands[ci + 2];
                float rx = commands[ci + 3], ry = commands[ci + 4];
                float xRot = commands[ci + 5];
                bool largeArc = commands[ci + 6] != 0.0f;
                bool sweep = commands[ci + 7] != 0.0f;
                hasDrawnInSubpath = true;
                std::vector<float> arcPts;
                FlattenSvgArc(curX, curY, ex, ey, rx, ry, xRot, largeArc, sweep, arcPts, 0.25f);
                float px = curX, py = curY;
                for (size_t ai = 0; ai + 1 < arcPts.size(); ai += 2) {
                    float nx = arcPts[ai], ny = arcPts[ai + 1];
                    pushLine(px, py, nx, ny);
                    bboxMinX_ = std::min(bboxMinX_, nx);
                    bboxMinY_ = std::min(bboxMinY_, ny);
                    bboxMaxX_ = std::max(bboxMaxX_, nx);
                    bboxMaxY_ = std::max(bboxMaxY_, ny);
                    px = nx; py = ny;
                }
                curX = ex; curY = ey;
                ci += 8;
                break;
            }
            case kTagClosePath: {
                if (curX != moveX || curY != moveY) {
                    pushLine(curX, curY, moveX, moveY);
                }
                curX = moveX; curY = moveY;
                ci += 1;
                break;
            }
            default:
                ci += 1;
                break;
            }
        }

        // Implicit close of the last subpath.
        if (hasDrawnInSubpath && (curX != moveX || curY != moveY)) {
            pushLine(curX, curY, moveX, moveY);
        }

        uint32_t segCount = (uint32_t)scene_.segments.size() - segOffset;
        if (segCount == 0) return false;

        // Apply transform per point (and recompute the bbox from transformed pts).
        bool hasTransform = (m11 != 1.0f || m12 != 0.0f || m21 != 0.0f ||
                             m22 != 1.0f || tdx != 0.0f || tdy != 0.0f);
        if (hasTransform) {
            bboxMinX_ = 1e30f; bboxMinY_ = 1e30f;
            bboxMaxX_ = -1e30f; bboxMaxY_ = -1e30f;
            for (uint32_t si = segOffset; si < (uint32_t)scene_.segments.size(); ++si) {
                auto& s = scene_.segments[si];
                auto xf = [&](float& px, float& py) {
                    float nx = px * m11 + py * m21 + tdx;
                    float ny = px * m12 + py * m22 + tdy;
                    px = nx; py = ny;
                    bboxMinX_ = std::min(bboxMinX_, nx);
                    bboxMinY_ = std::min(bboxMinY_, ny);
                    bboxMaxX_ = std::max(bboxMaxX_, nx);
                    bboxMaxY_ = std::max(bboxMaxY_, ny);
                };
                xf(s.p0x, s.p0y);
                xf(s.p1x, s.p1y);
                if (s.tag >= kSegTagQuadTo)  xf(s.p2x, s.p2y);
                if (s.tag == kSegTagCubicTo) xf(s.p3x, s.p3y);
            }
        }

        // CPU-flatten the segments to lines ONLY to recompute a tight bbox.
        // (The flattened lines are not uploaded — the GPU flatten stage
        //  re-flattens the PathSegments. We discard them after the bbox pass.)
        {
            constexpr float kFlattenTolerance = 0.25f;  // device-space pixels
            std::vector<float> curvePts;
            curvePts.reserve(64);

            float lbMinX = 1e30f, lbMinY = 1e30f, lbMaxX = -1e30f, lbMaxY = -1e30f;
            auto accum = [&](float x, float y) {
                lbMinX = std::min(lbMinX, x); lbMinY = std::min(lbMinY, y);
                lbMaxX = std::max(lbMaxX, x); lbMaxY = std::max(lbMaxY, y);
            };

            for (uint32_t si = segOffset; si < (uint32_t)scene_.segments.size(); ++si) {
                auto& s = scene_.segments[si];
                if (s.tag == kSegTagLineTo) {
                    if (std::abs(s.p0x - s.p1x) < 1e-6f && std::abs(s.p0y - s.p1y) < 1e-6f) continue;
                    accum(s.p0x, s.p0y); accum(s.p1x, s.p1y);
                } else if (s.tag == kSegTagQuadTo) {
                    curvePts.clear();
                    FlattenQuadraticBezier(s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y,
                                           curvePts, kFlattenTolerance);
                    float px = s.p0x, py = s.p0y;
                    accum(px, py);
                    for (size_t pi = 0; pi + 1 < curvePts.size(); pi += 2) {
                        float nx = curvePts[pi], ny = curvePts[pi + 1];
                        accum(nx, ny); px = nx; py = ny;
                    }
                } else if (s.tag == kSegTagCubicTo) {
                    curvePts.clear();
                    FlattenCubicBezier(s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y, s.p3x, s.p3y,
                                       curvePts, kFlattenTolerance);
                    float px = s.p0x, py = s.p0y;
                    accum(px, py);
                    for (size_t pi = 0; pi + 1 < curvePts.size(); pi += 2) {
                        float nx = curvePts[pi], ny = curvePts[pi + 1];
                        accum(nx, ny); px = nx; py = ny;
                    }
                }
            }
            if (lbMinX < 1e29f) {
                bboxMinX_ = lbMinX; bboxMinY_ = lbMinY;
                bboxMaxX_ = lbMaxX; bboxMaxY_ = lbMaxY;
            }
        }

        return FinishPathInfo(segOffset, segCount, fillRule, outPathIdx);
    }

    // Encodes a set of pre-widened device-space contours (used by strokes) as a
    // single path's PathSegments + PathInfo. Equivalent to the stroke tail of
    // D3D12 EncodeStrokePath: each contour becomes a closed line loop; the bbox
    // and tile bbox use the same exclusive-ceil formula. Contours are already in
    // device space, so no transform is applied here.
    bool EncodeContoursGeometry(const std::vector<Contour>& contours,
                                uint32_t fillRule, uint32_t& outPathIdx) {
        uint32_t pathIdx = (uint32_t)scene_.pathInfos.size();
        uint32_t segOffset = (uint32_t)scene_.segments.size();

        bboxMinX_ = 1e30f; bboxMinY_ = 1e30f;
        bboxMaxX_ = -1e30f; bboxMaxY_ = -1e30f;

        auto emitLine = [&](float x0, float y0, float x1, float y1) {
            if (std::abs(x1 - x0) < 1e-6f && std::abs(y1 - y0) < 1e-6f) return;
            PathSegment s = {};
            s.p0x = x0; s.p0y = y0;
            s.p1x = x1; s.p1y = y1;
            s.p2x = x1; s.p2y = y1;
            s.p3x = x1; s.p3y = y1;
            s.tag = kSegTagLineTo; s.pathIndex = pathIdx;
            scene_.segments.push_back(s);
            bboxMinX_ = std::min({bboxMinX_, x0, x1});
            bboxMinY_ = std::min({bboxMinY_, y0, y1});
            bboxMaxX_ = std::max({bboxMaxX_, x0, x1});
            bboxMaxY_ = std::max({bboxMaxY_, y0, y1});
        };

        for (const auto& c : contours) {
            uint32_t n = c.VertexCount();
            if (n < 3) continue;
            for (uint32_t i = 0; i < n; ++i) {
                uint32_t j = (i + 1) % n;
                emitLine(c.X(i), c.Y(i), c.X(j), c.Y(j));
            }
        }

        uint32_t segCount = (uint32_t)scene_.segments.size() - segOffset;
        if (segCount == 0) return false;

        return FinishPathInfo(segOffset, segCount, fillRule, outPathIdx);
    }

    // Shared tail for EncodePathGeometry / EncodeContoursGeometry: bbox reject,
    // scissor clamp, exclusive-ceil tile bbox, append PathInfo.
    bool FinishPathInfo(uint32_t segOffset, uint32_t segCount,
                        uint32_t fillRule, uint32_t& outPathIdx) {
        // Reject paths that span too many pixels.
        float bboxW = bboxMaxX_ - bboxMinX_;
        float bboxH = bboxMaxY_ - bboxMinY_;
        if (bboxW > 16384.0f || bboxH > 16384.0f) {
            scene_.segments.resize(segOffset);
            return false;
        }

        // Clamp path bbox to the scissor rect (tile culling).
        if (hasScissor_) {
            bboxMinX_ = std::max(bboxMinX_, scissorL_);
            bboxMinY_ = std::max(bboxMinY_, scissorT_);
            bboxMaxX_ = std::min(bboxMaxX_, scissorR_);
            bboxMaxY_ = std::min(bboxMaxY_, scissorB_);
            if (bboxMaxX_ <= bboxMinX_ || bboxMaxY_ <= bboxMinY_) {
                scene_.segments.resize(segOffset);
                return false;
            }
        }

        // Per-path tile bbox. Right/bottom are EXCLUSIVE via ceil(max/tile) —
        // the ceil form fixes the phantom-tile bug (max=31.5 → right tile = 2,
        // not 3). Preserve precisely.
        uint32_t tileBboxX = (uint32_t)std::max(0.0f, std::floor(bboxMinX_)) / kTileSize;
        uint32_t tileBboxY = (uint32_t)std::max(0.0f, std::floor(bboxMinY_)) / kTileSize;
        uint32_t tileBboxR = std::min(
            (uint32_t)std::max(0.0f, std::ceil(bboxMaxX_ / (float)kTileSize)), tilesX_);
        uint32_t tileBboxB = std::min(
            (uint32_t)std::max(0.0f, std::ceil(bboxMaxY_ / (float)kTileSize)), tilesY_);
        uint32_t tileBboxW = tileBboxR > tileBboxX ? tileBboxR - tileBboxX : 0;
        uint32_t tileBboxH = tileBboxB > tileBboxY ? tileBboxB - tileBboxY : 0;
        uint32_t tileCount = tileBboxW * tileBboxH;
        uint32_t tileOffset = totalPathTiles_;
        totalPathTiles_ += tileCount;

        PathInfo info = {};
        info.segOffset = segOffset;
        info.segCount  = segCount;
        info.fillRule  = fillRule;
        info.tileOffset = tileOffset;
        info.tileBboxX = tileBboxX;
        info.tileBboxY = tileBboxY;
        info.tileBboxW = tileBboxW;
        info.tileBboxH = tileBboxH;
        scene_.pathInfos.push_back(info);

        outPathIdx = (uint32_t)scene_.pathInfos.size() - 1;
        return true;
    }

    // ========================================================================
    // Brush packing — ports D3D12 EncodeFillPath (solid) + EncodeFillPathBrush
    // (linear/radial) plus the sweep case. bboxMin/Max_ must already be set.
    // ========================================================================
    void PackBrush(const EngineBrushData& brush, const EngineTransform& t,
                   float opacity, PathDraw& draw) {
        const float m11 = t.m11, m12 = t.m12, m21 = t.m21, m22 = t.m22, tdx = t.dx, tdy = t.dy;

        if (brush.type == kBrushLinearGradient && brush.stopCount > 0) {
            uint32_t gradIdx = AddGradientRamp(brush.stops, brush.stopCount, /*colorSpace*/ 0);
            draw.brushType = kBrushLinearGradient;
            draw.gradientIndex = gradIdx;
            float gp0x = brush.startX * m11 + brush.startY * m21 + tdx;
            float gp0y = brush.startX * m12 + brush.startY * m22 + tdy;
            float gp1x = brush.endX   * m11 + brush.endY   * m21 + tdx;
            float gp1y = brush.endX   * m12 + brush.endY   * m22 + tdy;
            draw.gradParam0 = gp0x;
            draw.gradParam1 = gp0y;
            draw.gradParam2 = gp1x;
            draw.gradParam3 = gp1y;
            // D3D12 hardcodes kExtendPad for linear (stored as a plain float).
            draw.gradParam4 = (float)kExtendPad;
            draw.colorA = opacity;     // fine shader modulates ramp by colorA
            draw.colorR = draw.colorG = draw.colorB = 0;
            return;
        }

        if (brush.type == kBrushRadialGradient && brush.stopCount > 0) {
            uint32_t gradIdx = AddGradientRamp(brush.stops, brush.stopCount, /*colorSpace*/ 0);
            draw.brushType = kBrushRadialGradient;
            draw.gradientIndex = gradIdx;
            float cx = brush.centerX * m11 + brush.centerY * m21 + tdx;
            float cy = brush.centerX * m12 + brush.centerY * m22 + tdy;
            float ox = brush.originX * m11 + brush.originY * m21 + tdx;
            float oy = brush.originX * m12 + brush.originY * m22 + tdy;
            float scaleX = std::sqrt(m11 * m11 + m12 * m12);
            float scaleY = std::sqrt(m21 * m21 + m22 * m22);
            draw.gradParam0 = cx;
            draw.gradParam1 = cy;
            draw.gradParam2 = brush.radiusX * scaleX;
            draw.gradParam3 = brush.radiusY * scaleY;
            draw.gradParam4 = ox;
            draw.gradParam5 = oy;
            draw.colorA = opacity;
            // D3D12 packs the extend mode bit-cast into colorR (unused for
            // gradients). Match exactly: memcpy uint -> float, hardcode pad.
            float extFloat; uint32_t extU = kExtendPad;
            std::memcpy(&extFloat, &extU, sizeof(float));
            draw.colorR = extFloat;
            draw.colorG = draw.colorB = 0;
            return;
        }

        if (brush.type == kBrushSweepGradient && brush.stopCount > 0) {
            // Sweep is not present in the current D3D12 EncodeFillPathBrush
            // switch (it only handles solid/linear/radial); we extend it here
            // following the PathDraw struct comments and the fine shader's
            // CMD_SWEEP_GRAD layout (gradIdx, cx, cy, t0, t1, ext).
            uint32_t gradIdx = AddGradientRamp(brush.stops, brush.stopCount, /*colorSpace*/ 0);
            draw.brushType = kBrushSweepGradient;
            draw.gradientIndex = gradIdx;
            float cx = brush.centerX * m11 + brush.centerY * m21 + tdx;
            float cy = brush.centerX * m12 + brush.centerY * m22 + tdy;
            draw.gradParam0 = cx;
            draw.gradParam1 = cy;
            // startX/endX carry the normalized start/end angle (t0/t1) for sweep.
            draw.gradParam2 = brush.startX;  // t0
            draw.gradParam3 = brush.endX;    // t1
            draw.gradParam4 = (float)kExtendPad;
            draw.colorA = opacity;
            draw.colorR = draw.colorG = draw.colorB = 0;
            return;
        }

        // Solid (default / fallback for unsupported brush types incl. image).
        float a = brush.a * opacity;
        draw.colorR = brush.r * a;   // premultiply
        draw.colorG = brush.g * a;
        draw.colorB = brush.b * a;
        draw.colorA = a;
        draw.brushType = kBrushSolid;
        draw.gradientIndex = 0;
        draw.gradParam0 = draw.gradParam1 = draw.gradParam2 = draw.gradParam3 = 0;
        draw.gradParam4 = draw.gradParam5 = 0;
    }

    // ========================================================================
    // AddGradientRamp — ports D3D12VelloRenderer::AddGradientRamp.
    // Builds a 256-entry premultiplied RGBA8 LUT per gradient (max kMaxGradients),
    // interpolating stops in sRGB(0)/LinearSRGB(1)/OKLab(2). Returns the gradient
    // index; appends kGradientRampWidth uint32 entries to scene_.gradientRamps.
    // ========================================================================
    uint32_t AddGradientRamp(const EngineBrushData::GradientStop* stops,
                             uint32_t stopCount, uint32_t colorSpace) {
        using namespace vello_detail;
        uint32_t curCount = (uint32_t)(scene_.gradientRamps.size() / kGradientRampWidth);
        if (stopCount == 0 || curCount >= kMaxGradients) return 0;

        uint32_t idx = curCount;
        scene_.gradientRamps.resize((size_t)(idx + 1) * kGradientRampWidth);
        uint32_t* ramp = &scene_.gradientRamps[(size_t)idx * kGradientRampWidth];

        if (stopCount == 1) {
            const auto& s = stops[0];
            uint32_t packed = ((uint32_t)(s.r * 255.0f + 0.5f))
                            | ((uint32_t)(s.g * 255.0f + 0.5f) << 8)
                            | ((uint32_t)(s.b * 255.0f + 0.5f) << 16)
                            | ((uint32_t)(s.a * 255.0f + 0.5f) << 24);
            for (uint32_t i = 0; i < kGradientRampWidth; ++i) ramp[i] = packed;
            return idx;
        }

        for (uint32_t i = 0; i < kGradientRampWidth; ++i) {
            float tt = (float)i / (float)(kGradientRampWidth - 1);

            uint32_t lo = 0, hi = stopCount - 1;
            for (uint32_t s = 0; s < stopCount; ++s) {
                if (stops[s].position <= tt) lo = s;
            }
            for (uint32_t s = stopCount; s-- > 0; ) {
                if (stops[s].position >= tt) hi = s;
            }

            float r, g, b, a;
            if (lo == hi || stops[hi].position <= stops[lo].position) {
                r = stops[lo].r; g = stops[lo].g; b = stops[lo].b; a = stops[lo].a;
            } else {
                float frac = (tt - stops[lo].position) / (stops[hi].position - stops[lo].position);
                frac = std::max(0.0f, std::min(1.0f, frac));

                if (colorSpace == 1) {
                    float lr0 = SrgbToLinear(stops[lo].r), lr1 = SrgbToLinear(stops[hi].r);
                    float lg0 = SrgbToLinear(stops[lo].g), lg1 = SrgbToLinear(stops[hi].g);
                    float lb0 = SrgbToLinear(stops[lo].b), lb1 = SrgbToLinear(stops[hi].b);
                    float lr = lr0 + (lr1 - lr0) * frac;
                    float lg = lg0 + (lg1 - lg0) * frac;
                    float lb = lb0 + (lb1 - lb0) * frac;
                    r = LinearToSrgb(lr); g = LinearToSrgb(lg); b = LinearToSrgb(lb);
                    a = stops[lo].a + (stops[hi].a - stops[lo].a) * frac;
                } else if (colorSpace == 2) {
                    OKLab lab0 = SrgbToOklab(stops[lo].r, stops[lo].g, stops[lo].b);
                    OKLab lab1 = SrgbToOklab(stops[hi].r, stops[hi].g, stops[hi].b);
                    float L = lab0.L + (lab1.L - lab0.L) * frac;
                    float A = lab0.a + (lab1.a - lab0.a) * frac;
                    float B = lab0.b + (lab1.b - lab0.b) * frac;
                    OklabToSrgb(L, A, B, r, g, b);
                    a = stops[lo].a + (stops[hi].a - stops[lo].a) * frac;
                } else {
                    r = stops[lo].r + (stops[hi].r - stops[lo].r) * frac;
                    g = stops[lo].g + (stops[hi].g - stops[lo].g) * frac;
                    b = stops[lo].b + (stops[hi].b - stops[lo].b) * frac;
                    a = stops[lo].a + (stops[hi].a - stops[lo].a) * frac;
                }
            }

            float pr = std::max(0.0f, std::min(1.0f, r)) * a;
            float pg = std::max(0.0f, std::min(1.0f, g)) * a;
            float pb = std::max(0.0f, std::min(1.0f, b)) * a;

            ramp[i] = ((uint32_t)(std::min(pr, 1.0f) * 255.0f + 0.5f))
                     | ((uint32_t)(std::min(pg, 1.0f) * 255.0f + 0.5f) << 8)
                     | ((uint32_t)(std::min(pb, 1.0f) * 255.0f + 0.5f) << 16)
                     | ((uint32_t)(std::min(a, 1.0f) * 255.0f + 0.5f) << 24);
        }
        return idx;
    }

    // ========================================================================
    // Scissor-as-clip — ports the intent of D3D12 ApplyScissorToVello +
    // EncodeBeginClipRect/EncodeEndClip. The scissor is realized as a
    // rectangular BeginClip (a NonZero rect fill path with its own
    // PathInfo/segments + a white opaque PathDraw) emitted lazily before the
    // first draw under that scissor, and closed by an EndClip on scissor change
    // / ClearScissor / Finalize.
    // ========================================================================

    // Emits the BeginClip for the current scissor if one is requested but not
    // yet open. Called at the top of every Encode* draw.
    void EnsureScissorClip() {
        if (!hasScissor_ || scissorActive_) return;
        // Guard against degenerate scissor (nothing visible).
        if (scissorR_ <= scissorL_ || scissorB_ <= scissorT_) return;
        EncodeBeginClipRect(scissorL_, scissorT_, scissorR_ - scissorL_, scissorB_ - scissorT_);
        scissorActive_ = true;
    }

    // Closes the open scissor-clip, if any.
    void CloseScissorClip() {
        if (!scissorActive_) return;
        EncodeEndClip();
        scissorActive_ = false;
    }

    // Encodes a rectangular BeginClip: a NonZero rect fill path (MoveTo implicit
    // via startX/Y + 3 LineTo + ClosePath) with a white opaque PathDraw, plus
    // the kDrawTagBeginClip drawtag. The clip rect is already in device space
    // (the scissor is set in device space), so an identity transform is used.
    void EncodeBeginClipRect(float x, float y, float w, float h) {
        float commands[8];
        uint32_t ci = 0;
        commands[ci++] = (float)kTagLineTo; commands[ci++] = x + w; commands[ci++] = y;
        commands[ci++] = (float)kTagLineTo; commands[ci++] = x + w; commands[ci++] = y + h;
        commands[ci++] = (float)kTagLineTo; commands[ci++] = x;     commands[ci++] = y + h;
        // NOTE: D3D12 also pushes a ClosePath tag here; EncodePathGeometry's
        // implicit-close-of-last-subpath produces the same closing segment, and
        // we keep the buffer to the 3 LineTo (the close is implicit). For exact
        // parity the closing edge (x+ , y+h) -> (x, y) is added by the implicit
        // close below.

        // We must NOT clamp the clip path's own bbox to the scissor it defines.
        bool savedHasScissor = hasScissor_;
        hasScissor_ = false;

        uint32_t pathIdx = 0;
        bool ok = EncodePathGeometry(x, y, commands, ci, kFillRuleNonZero,
                                     EngineTransform{}, pathIdx);

        hasScissor_ = savedHasScissor;

        if (!ok) {
            // Degenerate clip rect — treat as no clip to keep begin/end balanced.
            return;
        }

        PathDraw draw = {};
        draw.bboxMinX = bboxMinX_;
        draw.bboxMinY = bboxMinY_;
        draw.bboxMaxX = bboxMaxX_;
        draw.bboxMaxY = bboxMaxY_;
        draw.brushType = kBrushSolid;
        draw.colorR = 1.0f;  // premultiplied white — full clip coverage
        draw.colorG = 1.0f;
        draw.colorB = 1.0f;
        draw.colorA = 1.0f;
        scene_.pathDraws.push_back(draw);

        scene_.drawTags.push_back({ kDrawTagBeginClip, pathIdx, 0, 0 });
        clipStack_.push_back(pathIdx);
    }

    // Emits the matching kDrawTagEndClip (SrcOver, alpha 1).
    void EncodeEndClip() {
        if (clipStack_.empty()) return;
        clipStack_.pop_back();
        // blendMode = kBlendSrcOver (0), alpha = 1.0.
        scene_.drawTags.push_back({ kDrawTagEndClip, 0, 0, 1.0f });
    }

    // ========================================================================
    // Stroke helpers
    // ========================================================================

    // Widens a flattened polyline into per-triangle CCW contours using
    // ExpandStrokePath (collectContours mode). Vertex type is irrelevant in
    // collect mode (the function fills contours, not outVerts), so we use a
    // minimal POD vertex.
    void WidenIntoContours(const float* flatPoints, uint32_t pointCount,
                           float strokeWidth, int32_t lineJoin, float miterLimit,
                           int32_t lineCap, bool closed, const EngineBrushData& brush,
                           std::vector<Contour>& outContours) {
        struct StrokeVert { float x, y, r, g, b, a; };
        std::vector<StrokeVert> scratchVerts;
        std::vector<uint32_t>   scratchIndices;

        ImpellerJoin join = MapJoin(lineJoin);
        ImpellerCap  cap  = MapCap(lineCap);

        ExpandStrokePath<StrokeVert>(
            scratchVerts, scratchIndices,
            flatPoints, pointCount,
            strokeWidth, join, miterLimit, cap, closed,
            brush.r, brush.g, brush.b, brush.a,
            &outContours,          // collectContours: emit per-triangle CCW contours
            /*featherScaleInSrc*/ 1.0f);
    }

    static ImpellerJoin MapJoin(int32_t lineJoin) {
        // Jalium/WPF: 0=Miter, 1=Bevel, 2=Round.
        switch (lineJoin) {
        case 0: return ImpellerJoin::Miter;
        case 1: return ImpellerJoin::Bevel;
        default: return ImpellerJoin::Round;
        }
    }
    static ImpellerCap MapCap(int32_t lineCap) {
        // Jalium/WPF: 0=Flat/Butt, 1=Square, 2=Round.
        switch (lineCap) {
        case 0: return ImpellerCap::Butt;
        case 1: return ImpellerCap::Square;
        default: return ImpellerCap::Round;
        }
    }

    // Splits a flattened polyline into "on" sub-strokes per a dash pattern and
    // invokes `fn(subPoints, subPointCount)` for each. Ports the dash-walking
    // logic of D3D12 EncodeStrokePath (phase normalization + per-segment dash
    // boundary stepping).
    template <typename Fn>
    static void ForEachDashSubStroke(const float* pts, uint32_t pointCount,
                                     const float* dashPattern, uint32_t dashCount,
                                     float dashOffset, Fn&& fn) {
        if (pointCount < 2 || !dashPattern || dashCount == 0) return;

        std::vector<float> segLens(pointCount - 1);
        for (uint32_t i = 0; i < pointCount - 1; ++i) {
            float ddx = pts[(i + 1) * 2]     - pts[i * 2];
            float ddy = pts[(i + 1) * 2 + 1] - pts[i * 2 + 1];
            segLens[i] = std::sqrt(ddx * ddx + ddy * ddy);
        }

        float patternLen = 0;
        for (uint32_t d = 0; d < dashCount; ++d) patternLen += std::max(dashPattern[d], 0.001f);

        float dashPhase = dashOffset;
        if (patternLen > 0.001f) {
            while (dashPhase < 0) dashPhase += patternLen;
            while (dashPhase >= patternLen) dashPhase -= patternLen;
        }

        uint32_t dashIdx = 0;
        float dashRemain = dashPattern[0];
        float phase = dashPhase;
        while (phase > 0 && dashIdx < dashCount) {
            if (phase < dashRemain) { dashRemain -= phase; break; }
            phase -= dashRemain;
            dashIdx = (dashIdx + 1) % dashCount;
            dashRemain = dashPattern[dashIdx];
        }
        bool isDraw = (dashIdx % 2 == 0);

        auto interp = [&](uint32_t si, float tt) -> std::pair<float, float> {
            float x0 = pts[si * 2], y0 = pts[si * 2 + 1];
            float x1 = pts[(si + 1) * 2], y1 = pts[(si + 1) * 2 + 1];
            return { x0 + tt * (x1 - x0), y0 + tt * (y1 - y0) };
        };

        std::vector<float> subPts;
        subPts.push_back(pts[0]); subPts.push_back(pts[1]);

        uint32_t segI = 0;
        float segUsed = 0;
        while (segI < pointCount - 1) {
            float segLeft = segLens[segI] - segUsed;
            if (dashRemain <= segLeft) {
                float tt = (segLens[segI] > 1e-6f) ? (segUsed + dashRemain) / segLens[segI] : 0.0f;
                auto [px, py] = interp(segI, tt);
                if (isDraw) {
                    subPts.push_back(px); subPts.push_back(py);
                    if (subPts.size() >= 4) {
                        fn(subPts.data(), (uint32_t)(subPts.size() / 2));
                    }
                }
                subPts.clear();
                subPts.push_back(px); subPts.push_back(py);
                segUsed += dashRemain;
                dashIdx = (dashIdx + 1) % dashCount;
                dashRemain = dashPattern[dashIdx];
                isDraw = !isDraw;
            } else {
                dashRemain -= segLeft;
                segI++;
                segUsed = 0;
                if (segI < pointCount - 1 && isDraw) {
                    subPts.push_back(pts[segI * 2]);
                    subPts.push_back(pts[segI * 2 + 1]);
                } else if (segI < pointCount - 1) {
                    subPts.clear();
                    subPts.push_back(pts[segI * 2]);
                    subPts.push_back(pts[segI * 2 + 1]);
                }
            }
        }
        // Emit the trailing "on" sub-stroke.
        if (isDraw && subPts.size() >= 2) {
            subPts.push_back(pts[(pointCount - 1) * 2]);
            subPts.push_back(pts[(pointCount - 1) * 2 + 1]);
            if (subPts.size() >= 4) {
                fn(subPts.data(), (uint32_t)(subPts.size() / 2));
            }
        }
    }

    // ========================================================================
    // State
    // ========================================================================
    VelloScene scene_;

    uint32_t tilesX_ = 0, tilesY_ = 0;
    uint32_t totalPathTiles_ = 0;

    // Working bbox for the path currently being encoded (pixel/device space).
    float bboxMinX_ = 0, bboxMinY_ = 0, bboxMaxX_ = 0, bboxMaxY_ = 0;

    // Scissor state.
    bool  hasScissor_   = false;  // a scissor rect has been requested
    bool  scissorActive_ = false; // the BeginClip for it has been emitted
    float scissorL_ = 0, scissorT_ = 0, scissorR_ = 0, scissorB_ = 0;

    // Open-clip stack (path indices of BeginClips awaiting EndClip).
    std::vector<uint32_t> clipStack_;
};

}  // namespace jalium
