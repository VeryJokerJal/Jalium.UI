Texture2D blurTexture : register(t0);
SamplerState blurSampler : register(s1);

struct PushConstants
{
    float4 rect;
    float4 blurInfo1;
    float4 blurInfo2;
    float4 blurTint;
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    float2 clipFlags;
    float4 innerRoundedClipRect;
    float2 innerRoundedClipRadius;
    float2 padding3;
    float4 quadPoint01;
    float4 quadPoint23;
    float2 geometryFlags;
    float2 padding2;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

bool IsInsideRoundRect(float2 pixel, float4 rect, float2 radius)
{
    const float left = rect.x;
    const float top = rect.y;
    const float right = rect.z;
    const float bottom = rect.w;
    if (pixel.x < left || pixel.y < top || pixel.x > right || pixel.y > bottom) {
        return false;
    }

    const float rx = max(radius.x, 0.0f);
    const float ry = max(radius.y, 0.0f);
    if (rx <= 0.0f || ry <= 0.0f) {
        return true;
    }

    if (pixel.x < left + rx && pixel.y < top + ry) {
        const float2 delta = (pixel - float2(left + rx, top + ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }
    if (pixel.x > right - rx && pixel.y < top + ry) {
        const float2 delta = (pixel - float2(right - rx, top + ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }
    if (pixel.x < left + rx && pixel.y > bottom - ry) {
        const float2 delta = (pixel - float2(left + rx, bottom - ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }
    if (pixel.x > right - rx && pixel.y > bottom - ry) {
        const float2 delta = (pixel - float2(right - rx, bottom - ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }

    return true;
}

// sRGB ↔ linear conversion so blurring averages in linear light space.
float SrgbToLinearCh(float s)
{
    return (s <= 0.04045) ? s / 12.92 : pow((s + 0.055) / 1.055, 2.4);
}
float3 SrgbToLinear(float3 s)
{
    return float3(SrgbToLinearCh(s.x), SrgbToLinearCh(s.y), SrgbToLinearCh(s.z));
}
float LinearToSrgbCh(float l)
{
    return (l <= 0.0031308) ? l * 12.92 : 1.055 * pow(l, 1.0 / 2.4) - 0.055;
}
float3 LinearToSrgb(float3 l)
{
    return float3(LinearToSrgbCh(l.x), LinearToSrgbCh(l.y), LinearToSrgbCh(l.z));
}

float4 main(PsInput input) : SV_Target
{
    if (gPushConstants.clipFlags.x > 0.5f && !IsInsideRoundRect(input.position.xy, gPushConstants.roundedClipRect, gPushConstants.roundedClipRadius)) {
        discard;
    }
    if (gPushConstants.clipFlags.y > 0.5f && IsInsideRoundRect(input.position.xy, gPushConstants.innerRoundedClipRect, gPushConstants.innerRoundedClipRadius)) {
        discard;
    }

    const int radius = min(12, max(0, (int)round(gPushConstants.blurInfo2.x)));
    const float2 texelStep = float2(
        gPushConstants.blurInfo1.x * gPushConstants.blurInfo1.z,
        gPushConstants.blurInfo1.y * gPushConstants.blurInfo1.w);

    // Accumulate in linear space to avoid perceptual darkening at blur edges.
    float4 sum = 0.0f;
    int count = 0;
    for (int dy = -radius; dy <= radius; ++dy) {
        for (int dx = -radius; dx <= radius; ++dx) {
            float4 s = blurTexture.Sample(blurSampler, input.uv + float2(dx, dy) * texelStep);
            s.rgb = SrgbToLinear(s.rgb);
            sum += s;
            ++count;
        }
    }

    float4 color = count > 0 ? sum / count : blurTexture.Sample(blurSampler, input.uv);
    color.rgb = LinearToSrgb(color.rgb);

    if (gPushConstants.blurInfo2.z > 0.5f) {
        color = float4(gPushConstants.blurTint.rgb, color.a * gPushConstants.blurTint.a);
    }
    color.a *= saturate(gPushConstants.blurInfo2.y);
    return color;
}
