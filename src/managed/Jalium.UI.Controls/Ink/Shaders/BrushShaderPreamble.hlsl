// =============================================================================
// Jalium.UI — Brush shader shared preamble
//
// Every brush shader is a pixel shader that writes to the InkCanvas offscreen
// RGBA8 bitmap. The framework provides a fullscreen triangle VS that emits
// NDC + a pixel-space position passed through to PS. The user-authored
// "BrushMain(float2 px)" function returns premultiplied RGBA for the pixel.
//
// Inputs available:
//   cbuffer BrushConstants (b0)  — stroke metadata (see layout below).
//   StructuredBuffer<float2> StrokePoints (t0) — polyline vertices in
//     InkCanvas-local pixel coordinates, 0..StrokePointCount-1.
//
// Coordinate system: px is in InkCanvas-local pixels (same space as the
// polyline). Origin top-left, +y down.
//
// Blend: framework sets the pipeline blend state per BrushShader.BlendMode.
//   For premultiplied source-over, return premul RGBA — do NOT multiply the
//   user color by alpha inside BrushMain.
// =============================================================================

cbuffer BrushConstants : register(b0)
{
    // Primary stroke appearance
    float4 StrokeColor;        // RGBA, premul
    float  StrokeWidth;        // px
    float  StrokeHeight;       // px (for elliptical tips)
    float  TimeSeconds;        // accumulates across frames for animation
    uint   RandomSeed;         // stable per-stroke hash seed
    // Stroke extent (pixel-space bbox inflated by stroke half-width)
    float2 BBoxMin;
    float2 BBoxMax;
    uint   PointCount;
    uint   TaperMode;          // 0 = None, 1 = TaperedStart, 2 = TaperedEnd
    uint   IgnorePressure;     // 0 = use per-point pressure, 1 = ignore
    uint   FitToCurve;         // 0 = polyline, 1 = Catmull-Rom smoothed (PS samples)
};

struct StrokePoint
{
    float x;
    float y;
    float pressure;  // 0..1
    float pad;
};

StructuredBuffer<StrokePoint> StrokePoints : register(t0);

// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------

// Deterministic pseudo-random in [0,1) from 2D integer coord + stroke seed.
// Cheap enough to call per-pixel; stable across frames as long as seed is.
float Hash21(float2 p, uint extra)
{
    uint3 q = uint3(asuint(p.x), asuint(p.y), RandomSeed ^ extra);
    q = q * uint3(374761393u, 668265263u, 2246822519u);
    q = (q.x ^ q.y ^ q.z) * uint3(0x85ebca6bu, 0xc2b2ae35u, 0x27d4eb2fu);
    uint h = q.x ^ q.y ^ q.z;
    return (h & 0x00FFFFFFu) / float(0x01000000u);
}

// Distance from pixel `px` to segment (a,b). Returns distance + closest-t in [0,1].
float2 SdfSegment(float2 px, float2 a, float2 b)
{
    float2 pa = px - a;
    float2 ba = b - a;
    float lenSq = dot(ba, ba);
    float t = (lenSq > 1e-6) ? saturate(dot(pa, ba) / lenSq) : 0;
    return float2(length(pa - ba * t), t);
}

// Universal taper scale in [0, 1] at arc-param t. Declared before
// SdfPolyline because the union-SDF path uses it to pick the dominating
// segment at self-intersections.
//   TaperMode 0 (None)         → 1 (no change)
//   TaperMode 1 (TaperedStart) → 0 at t=0, 1 at t=1 (thin → thick)
//   TaperMode 2 (TaperedEnd)   → 1 at t=0, 0 at t=1 (thick → thin)
float TaperScale(float t)
{
    if (TaperMode == 1) return 1.0 - (1.0 - t) * (1.0 - t);
    if (TaperMode == 2) return 1.0 - t * t;
    return 1.0;
}

// Distance from pixel to the full polyline + normalized arc-length position
// of the **coverage-dominant** segment (the segment that paints this pixel
// most). Return.x = global min distance (px); .y = arc ∈ [0,1].
//
// Why not "arc of the nearest segment"? Self-intersecting strokes have the
// same pixel close to two (or more) segments; picking the globally-nearest
// one makes the arc flip between candidates per-pixel, and anything that
// reads halfW(arc) — taper, pressure — flips with it. The stroke appears
// cut at every crossing.
//
// Score each segment by the coverage it *would* produce (halfW − dist +
// 0.5-px AA); keep the arc from the winner. Pairing that arc with the
// global-min distance slightly over-estimates coverage at crossings,
// which reads as a subtle bulge at the intersection rather than a break —
// the correct behaviour for overlapping ink.
float2 SdfPolyline(float2 px)
{
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

    // Pass 2: per-segment coverage scoring.
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

        // Estimate coverage this segment produces. Inline because
        // HalfWidthAt would recurse into an arc that is what we are
        // computing. Per-segment pressure is sampled from the two
        // segment endpoints directly, which is more accurate here
        // than HalfWidthAt's global t-index lookup.
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

// Local stroke half-width at normalized arc position t, combining taper +
// (optionally) interpolated pressure. Pressure sampling is nearest-point
// in the polyline; adequate for 2–5 px strokes and cheaper than arc-length
// re-scan on every pixel.
float HalfWidthAt(float t)
{
    float radius = StrokeWidth * 0.5;

    // Pressure modulation
    if (IgnorePressure == 0 && PointCount >= 2)
    {
        float idxF = saturate(t) * (PointCount - 1);
        uint idx0 = (uint)floor(idxF);
        uint idx1 = min(idx0 + 1, PointCount - 1);
        float frac = idxF - idx0;
        float p = lerp(StrokePoints[idx0].pressure, StrokePoints[idx1].pressure, frac);
        radius *= p;
    }

    // Taper runs fully to 0 at the tapered end; StrokeCoverage()'s +0.5 AA
    // term keeps the very tip drawn as a soft 1-px fade instead of a
    // clamp-induced hard edge.
    radius *= TaperScale(t);

    return max(radius, 0.0);
}

// Smooth coverage from signed distance — 1 px antialias falloff.
float StrokeCoverage(float sdf, float halfWidth)
{
    return saturate(halfWidth - sdf + 0.5);
}

// ---------------------------------------------------------------------------
// VS output shared with fullscreen triangle
// ---------------------------------------------------------------------------
struct PsIn
{
    float4 svPos : SV_Position;  // clip space
    float2 pxPos : TEXCOORD0;    // InkCanvas-local pixel coord
};
