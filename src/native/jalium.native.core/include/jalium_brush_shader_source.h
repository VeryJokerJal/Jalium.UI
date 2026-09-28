#pragma once

namespace jalium {

// Canonical HLSL contract shared by D3D12, Vulkan and Metal Ink. Backends only
// provide their fullscreen vertex stage, resource remapping and blend state;
// every authored BrushMain is compiled against this byte-identical preamble.
inline constexpr const char* kSharedBrushPixelPreamble = R"JALBRUSH(
cbuffer BrushConstants : register(b0)
{
    float4 StrokeColor;
    float StrokeWidth;
    float StrokeHeight;
    float TimeSeconds;
    uint RandomSeed;
    float2 BBoxMin;
    float2 BBoxMax;
    uint PointCount;
    uint TaperMode;
    uint IgnorePressure;
    uint FitToCurve;
    float2 ViewportSize;
    float2 Pad;
};

struct StrokePoint
{
    float x;
    float y;
    float pressure;
    float pad;
};

StructuredBuffer<StrokePoint> StrokePoints : register(t0);

float Hash21(float2 p, uint extra)
{
    uint3 q = uint3(asuint(p.x), asuint(p.y), RandomSeed ^ extra);
    q = q * uint3(374761393u, 668265263u, 2246822519u);
    q = (q.x ^ q.y ^ q.z) * uint3(0x85ebca6bu, 0xc2b2ae35u, 0x27d4eb2fu);
    uint h = q.x ^ q.y ^ q.z;
    return (h & 0x00FFFFFFu) / float(0x01000000u);
}

float2 SdfSegment(float2 px, float2 a, float2 b)
{
    float2 pa = px - a;
    float2 ba = b - a;
    float lenSq = dot(ba, ba);
    float t = (lenSq > 1e-6) ? saturate(dot(pa, ba) / lenSq) : 0;
    return float2(length(pa - ba * t), t);
}

float TaperScale(float t)
{
    if (TaperMode == 1) return 1.0 - (1.0 - t) * (1.0 - t);
    if (TaperMode == 2) return 1.0 - t * t;
    return 1.0;
}

float2 SdfPolyline(float2 px)
{
    float totalLen = 0;
    [loop]
    for (uint i = 0; i + 1 < PointCount; ++i)
    {
        StrokePoint pa = StrokePoints[i];
        StrokePoint pb = StrokePoints[i + 1];
        totalLen += length(float2(pb.x - pa.x, pb.y - pa.y));
    }
    float invLen = (totalLen > 1e-6) ? (1.0 / totalLen) : 0.0;

    float bestDist = 1e20;
    float bestArc = 0;
    float bestCov = -1;
    float accum = 0;
    [loop]
    for (uint j = 0; j + 1 < PointCount; ++j)
    {
        StrokePoint pa = StrokePoints[j];
        StrokePoint pb = StrokePoints[j + 1];
        float2 a = float2(pa.x, pa.y);
        float2 b = float2(pb.x, pb.y);
        float len = length(b - a);
        float2 result = SdfSegment(px, a, b);
        float arc = saturate((accum + result.y * len) * invLen);
        float halfWidth = StrokeWidth * 0.5;
        if (IgnorePressure == 0)
            halfWidth *= lerp(pa.pressure, pb.pressure, result.y);
        halfWidth *= TaperScale(arc);
        float candidateCoverage = saturate(halfWidth - result.x + 0.5);
        if (candidateCoverage > bestCov)
        {
            bestCov = candidateCoverage;
            bestArc = arc;
        }
        bestDist = min(bestDist, result.x);
        accum += len;
    }
    return float2(bestDist, bestArc);
}

float HalfWidthAt(float t)
{
    float radius = StrokeWidth * 0.5;
    if (IgnorePressure == 0 && PointCount >= 2)
    {
        float idxF = saturate(t) * (PointCount - 1);
        uint idx0 = (uint)floor(idxF);
        uint idx1 = min(idx0 + 1, PointCount - 1);
        float fraction = idxF - idx0;
        radius *= lerp(StrokePoints[idx0].pressure,
                       StrokePoints[idx1].pressure, fraction);
    }
    return max(radius * TaperScale(t), 0.0);
}

float StrokeCoverage(float sdf, float halfWidth)
{
    return saturate(halfWidth - sdf + 0.5);
}

struct PsIn
{
    float4 svPos : SV_Position;
    float2 pxPos : TEXCOORD0;
};
)JALBRUSH";

inline constexpr const char* kSharedBrushPixelEntry = R"JALBRUSH(
float4 BrushPsMain(PsIn input) : SV_Target
{
    return BrushMain(input.pxPos);
}
)JALBRUSH";

} // namespace jalium
