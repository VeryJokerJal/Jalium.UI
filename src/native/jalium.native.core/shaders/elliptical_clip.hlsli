#ifndef JALIUM_ELLIPTICAL_CLIP_HLSLI
#define JALIUM_ELLIPTICAL_CLIP_HLSLI

struct JaliumEllipticalClip {
    float4 localX;
    float4 localY;
    float4 box; // width, height, exclude, singular
    float4 radiusX;
    float4 radiusY;
};
#ifdef JALIUM_VULKAN_CLIP
[[vk::binding(0, 1)]] ByteAddressBuffer jaliumEllipticalClipBytes;
JaliumEllipticalClip JaliumLoadEllipticalClip(uint index)
{
    uint offset = 16u + index * 80u;
    JaliumEllipticalClip shape;
    shape.localX = asfloat(jaliumEllipticalClipBytes.Load4(offset));
    shape.localY = asfloat(jaliumEllipticalClipBytes.Load4(offset + 16u));
    shape.box = asfloat(jaliumEllipticalClipBytes.Load4(offset + 32u));
    shape.radiusX = asfloat(jaliumEllipticalClipBytes.Load4(offset + 48u));
    shape.radiusY = asfloat(jaliumEllipticalClipBytes.Load4(offset + 64u));
    return shape;
}
#else
#ifdef JALIUM_BACKDROP_ELLIPTICAL_CLIP
StructuredBuffer<JaliumEllipticalClip> jaliumEllipticalClips : register(t1);
#else
StructuredBuffer<JaliumEllipticalClip> jaliumEllipticalClips : register(t3);
#endif
JaliumEllipticalClip JaliumLoadEllipticalClip(uint index) { return jaliumEllipticalClips[index]; }
#endif

float JaliumEllipticalClipDistance(JaliumEllipticalClip shape, float2 pixel)
{
    if (shape.box.w != 0) return 1.0e10;
    float2 p = float2(dot(shape.localX.xyz, float3(pixel, 1)), dot(shape.localY.xyz, float3(pixel, 1)));
    uint edges = (uint)shape.localX.w;
    float d = -1.0e10;
    if (edges & 1u) d = max(d, -p.x);
    if (edges & 2u) d = max(d, -p.y);
    if (edges & 4u) d = max(d, p.x - shape.box.x);
    if (edges & 8u) d = max(d, p.y - shape.box.y);
    float4 dx = float4(p.x, shape.box.x - p.x, shape.box.x - p.x, p.x);
    float4 dy = float4(p.y, p.y, shape.box.y - p.y, shape.box.y - p.y);
    uint4 adjoining = uint4(3u, 6u, 12u, 9u);
    [unroll] for (uint corner = 0; corner < 4; ++corner) {
        float2 r = float2(shape.radiusX[corner], shape.radiusY[corner]);
        if ((edges & adjoining[corner]) != adjoining[corner] || any(r <= 0) || dx[corner] >= r.x || dy[corner] >= r.y) continue;
        float2 q = (float2(dx[corner], dy[corner]) - r) / r;
        float len = length(q);
        float gradient = length(q / r);
        if (gradient > 0) d = max(d, len * (len - 1.0) / gradient);
    }
    return d;
}

void JaliumPathClipEdge(float4 edge, float2 pixel,
    inout int winding, inout float minimumDistanceSquared)
{
    float2 a = edge.xy;
    float2 b = edge.zw;
    float2 ab = b - a;
    float along = saturate(dot(pixel - a, ab) / max(dot(ab, ab), 1.0e-12));
    float2 delta = pixel - (a + along * ab);
    minimumDistanceSquared = min(minimumDistanceSquared, dot(delta, delta));

    bool upward = a.y <= pixel.y && b.y > pixel.y;
    bool downward = b.y <= pixel.y && a.y > pixel.y;
    if (upward || downward) {
        float crossing = a.x + (pixel.y - a.y) * ab.x / ab.y;
        if (crossing > pixel.x) winding += upward ? 1 : -1;
    }
}

float JaliumPathClipCoverage(uint headerIndex, JaliumEllipticalClip header, float2 pixel)
{
    uint edgeCount = (uint)header.box.x;
    uint recordCount = (uint)header.box.z;
    if (edgeCount == 0u) return 0.0;
    if (pixel.x < header.localX.x - 0.5 || pixel.y < header.localX.y - 0.5 ||
        pixel.x > header.localX.z + 0.5 || pixel.y > header.localX.w + 0.5) return 0.0;
    int winding = 0;
    float minimumDistanceSquared = 1.0e20;
    [loop] for (uint recordIndex = 0; recordIndex < recordCount; ++recordIndex) {
        JaliumEllipticalClip record = JaliumLoadEllipticalClip(headerIndex + 1u + recordIndex);
        uint edgeIndex = recordIndex * 5u;
        if (edgeIndex + 0u < edgeCount) JaliumPathClipEdge(record.localX, pixel, winding, minimumDistanceSquared);
        if (edgeIndex + 1u < edgeCount) JaliumPathClipEdge(record.localY, pixel, winding, minimumDistanceSquared);
        if (edgeIndex + 2u < edgeCount) JaliumPathClipEdge(record.box, pixel, winding, minimumDistanceSquared);
        if (edgeIndex + 3u < edgeCount) JaliumPathClipEdge(record.radiusX, pixel, winding, minimumDistanceSquared);
        if (edgeIndex + 4u < edgeCount) JaliumPathClipEdge(record.radiusY, pixel, winding, minimumDistanceSquared);
    }
    bool inside = (uint)header.box.y == 0u ? (winding & 1) != 0 : winding != 0;
    float distance = sqrt(minimumDistanceSquared);
    return saturate(0.5 - (inside ? -distance : distance));
}

float JaliumEllipticalClipCoverage(uint count, float2 pixel)
{
    float coverage = 1.0;
    // The buffer length follows the live stack; nesting has no shader limit.
    [loop] for (uint i = 0; i < count; ++i) {
        JaliumEllipticalClip shape = JaliumLoadEllipticalClip(i);
        if (shape.box.w == 2.0) {
            coverage = min(coverage, JaliumPathClipCoverage(i, shape, pixel));
            i += (uint)shape.box.z;
            continue;
        }
        float d = JaliumEllipticalClipDistance(shape, pixel);
        float aa = max(fwidth(d), .0001);
        float c = 1.0 - smoothstep(-.5 * aa, .5 * aa, d);
        if (shape.box.z != 0) c = 1.0 - c;
        coverage = min(coverage, c);
    }
    return coverage;
}
#ifdef JALIUM_VULKAN_CLIP
float JaliumVulkanClipCoverage(float2 pixel)
{
    return JaliumEllipticalClipCoverage(jaliumEllipticalClipBytes.Load(0), pixel);
}
#endif
#endif
