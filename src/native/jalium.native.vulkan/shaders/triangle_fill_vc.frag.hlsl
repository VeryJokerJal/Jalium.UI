struct PushConstants
{
    float4 color;
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    float2 clipFlags;
    float4 perCornerRadiusX;
    float4 perCornerRadiusY;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct PsInput
{
    float4 position : SV_Position;
    float4 color    : COLOR;
};

// Analytic rounded-clip coverage shared by uniform and per-corner clips.
// The quad may contribute helper lanes outside the clip, so evaluate fwidth
// before discarding and apply the result to premultiplied vertex colour.
float CoverageRoundRect(float2 pixel, float4 rect, float2 radius)
{
    const float2 halfSize = max((rect.zw - rect.xy) * 0.5f, 0.0f);
    const float2 center = (rect.xy + rect.zw) * 0.5f;
    const float rx = min(max(radius.x, 0.0f), halfSize.x);
    const float ry = min(max(radius.y, 0.0f), halfSize.y);

    float pixelDist;
    if (rx <= 0.0f || ry <= 0.0f) {
        const float2 q = abs(pixel - center) - halfSize;
        pixelDist = length(max(q, 0.0f)) + min(max(q.x, q.y), 0.0f);
    } else {
        float anchorX;
        float anchorY;
        if (pixel.x < rect.x + rx) anchorX = rect.x + rx;
        else if (pixel.x > rect.z - rx) anchorX = rect.z - rx;
        else anchorX = pixel.x;

        if (pixel.y < rect.y + ry) anchorY = rect.y + ry;
        else if (pixel.y > rect.w - ry) anchorY = rect.w - ry;
        else anchorY = pixel.y;

        const float2 delta =
            (pixel - float2(anchorX, anchorY)) / float2(rx, ry);
        pixelDist = (length(delta) - 1.0f) * min(rx, ry);
    }

    const float aa = max(fwidth(pixelDist), 0.0001f);
    return 1.0f - smoothstep(-aa * 0.5f, aa * 0.5f, pixelDist);
}

float CoveragePerCornerRoundRect(
    float2 pixel, float4 rect, float4 rxs, float4 rys)
{
    const float midX = (rect.x + rect.z) * 0.5f;
    const float midY = (rect.y + rect.w) * 0.5f;
    float rx;
    float ry;
    if (pixel.y < midY) {
        if (pixel.x < midX) { rx = rxs.x; ry = rys.x; }
        else                { rx = rxs.y; ry = rys.y; }
    } else {
        if (pixel.x >= midX) { rx = rxs.z; ry = rys.z; }
        else                  { rx = rxs.w; ry = rys.w; }
    }
    return CoverageRoundRect(pixel, rect, float2(rx, ry));
}

float RoundedClipCoverage(float2 pixel)
{
    const float perCornerSum =
        dot(gPushConstants.perCornerRadiusX, float4(1.0f, 1.0f, 1.0f, 1.0f)) +
        dot(gPushConstants.perCornerRadiusY, float4(1.0f, 1.0f, 1.0f, 1.0f));
    float coverage = perCornerSum > 0.001f
        ? CoveragePerCornerRoundRect(
            pixel,
            gPushConstants.roundedClipRect,
            gPushConstants.perCornerRadiusX,
            gPushConstants.perCornerRadiusY)
        : CoverageRoundRect(
            pixel,
            gPushConstants.roundedClipRect,
            gPushConstants.roundedClipRadius);
    if (gPushConstants.clipFlags.x > 1.5f) {
        coverage = 1.0f - coverage;
    }
    return coverage;
}

float4 main(PsInput input) : SV_Target
{
    float4 color = input.color * gPushConstants.color;
    if (gPushConstants.clipFlags.x > 0.5f) {
        const float coverage = RoundedClipCoverage(input.position.xy);
        if (coverage <= 0.0f) {
            discard;
        }
        color *= coverage;
    }

    // The push-constant color acts as a tint applied on top of the vertex
    // color. For solid fills the vertex colors are set to (1,1,1,1) and the
    // push-constant carries the real color. For per-vertex gradient fills the
    // push-constant is set to (1,1,1,1) and the vertex colors carry the
    // sampled per-vertex colors.
    return color;
}
