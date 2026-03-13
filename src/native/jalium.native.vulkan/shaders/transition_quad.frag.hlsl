Texture2D fromTexture : register(t0);
Texture2D toTexture : register(t1);
SamplerState transitionSampler : register(s2);

struct PushConstants
{
    float4 rect;
    float4 progressOpacity;
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    float2 clipFlags;
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

float HashTransition(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float NoiseTransition(float2 st)
{
    float2 i = floor(st);
    float2 f = frac(st);
    float a = HashTransition(i);
    float b = HashTransition(i + float2(1.0, 0.0));
    float c = HashTransition(i + float2(0.0, 1.0));
    float d = HashTransition(i + float2(1.0, 1.0));
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

float4 main(PsInput input) : SV_Target
{
    if (gPushConstants.clipFlags.x > 0.5f && !IsInsideRoundRect(input.position.xy, gPushConstants.roundedClipRect, gPushConstants.roundedClipRadius)) {
        discard;
    }

    const float progress = saturate(gPushConstants.progressOpacity.x);
    const int mode = (int)round(gPushConstants.progressOpacity.z);
    const float2 resolution = max(gPushConstants.screenSize, float2(1.0f, 1.0f));

    float4 oldColor = fromTexture.Sample(transitionSampler, input.uv);
    float4 newColor = toTexture.Sample(transitionSampler, input.uv);
    float4 color = 0.0f;

    if (mode == 0) {
        float n = NoiseTransition(input.uv * 40.0);
        float threshold = progress * 1.2 - 0.1;
        float edge = smoothstep(threshold - 0.05, threshold + 0.05, n);
        float edgeMask = smoothstep(threshold - 0.08, threshold - 0.03, n) *
                         (1.0 - smoothstep(threshold - 0.03, threshold + 0.02, n));
        float3 edgeColor = float3(1.0, 0.5, 0.1) * edgeMask * 2.0;
        color = lerp(newColor, oldColor, edge);
        color.rgb += edgeColor;
    } else if (mode == 1) {
        float maxBlock = 40.0;
        float blockSize = max(1.0, maxBlock * sin(progress * 3.14159));
        float2 blockUV = floor(input.uv * resolution / blockSize) * blockSize / resolution;
        color = progress < 0.5
            ? fromTexture.Sample(transitionSampler, blockUV)
            : toTexture.Sample(transitionSampler, blockUV);
    } else if (mode == 2) {
        float intensity = sin(progress * 3.14159) * 0.8 + 0.2;
        float lineNoise = HashTransition(float2(floor(input.uv.y * 30.0), floor(progress * 20.0)));
        float displacement = (lineNoise - 0.5) * 0.15 * intensity;
        float shift = displacement * intensity;
        float2 uvR = input.uv + float2(shift, 0);
        float2 uvB = input.uv - float2(shift, 0);
        float blockSwitch = HashTransition(float2(floor(input.uv.x * 8.0), floor(input.uv.y * 12.0 + progress * 5.0)));
        bool useNew = blockSwitch < progress;
        float r = useNew ? toTexture.Sample(transitionSampler, uvR).r : fromTexture.Sample(transitionSampler, uvR).r;
        float g = useNew ? toTexture.Sample(transitionSampler, input.uv).g : fromTexture.Sample(transitionSampler, input.uv).g;
        float b = useNew ? toTexture.Sample(transitionSampler, uvB).b : fromTexture.Sample(transitionSampler, uvB).b;
        float scanline = sin(input.uv.y * resolution.y * 2.0) * 0.03 * intensity;
        color = float4(r + scanline, g + scanline, b + scanline, 1.0);
    } else if (mode == 3) {
        float spread = (1.0 - progress) * 0.08;
        float oldR = fromTexture.Sample(transitionSampler, input.uv + float2(spread, spread * 0.5)).r;
        float oldG = fromTexture.Sample(transitionSampler, input.uv).g;
        float oldB = fromTexture.Sample(transitionSampler, input.uv - float2(spread, spread * 0.5)).b;
        float newSpread = progress * 0.08;
        float newR = toTexture.Sample(transitionSampler, input.uv + float2(newSpread, newSpread * 0.5)).r;
        float newG = toTexture.Sample(transitionSampler, input.uv).g;
        float newB = toTexture.Sample(transitionSampler, input.uv - float2(newSpread, newSpread * 0.5)).b;
        color = lerp(float4(oldR, oldG, oldB, 1.0), float4(newR, newG, newB, 1.0), progress);
    } else if (mode == 4) {
        float time = progress * 6.28318;
        float strength = sin(progress * 3.14159) * 0.12;
        float2 distortion = float2(
            sin(input.uv.y * 15.0 + time) * strength,
            cos(input.uv.x * 15.0 + time * 1.3) * strength);
        distortion += float2(
            sin(input.uv.y * 8.0 - time * 0.7) * strength * 0.5,
            cos(input.uv.x * 8.0 - time * 0.5) * strength * 0.5);
        color = lerp(
            fromTexture.Sample(transitionSampler, input.uv + distortion),
            toTexture.Sample(transitionSampler, input.uv - distortion * 0.5),
            smoothstep(0.2, 0.8, progress));
    } else if (mode == 5) {
        float amplitude = sin(progress * 3.14159) * 0.15;
        float frequency = 8.0;
        float speed = progress * 12.56636;
        float wave = sin(input.uv.y * frequency + speed) * amplitude;
        color = lerp(
            fromTexture.Sample(transitionSampler, input.uv + float2(wave, wave * 0.3)),
            toTexture.Sample(transitionSampler, input.uv - float2(wave * 0.5, wave * 0.2)),
            progress);
    } else if (mode == 6) {
        float columnNoise = HashTransition(float2(floor(input.uv.x * 30.0), 0));
        float rowNoise = HashTransition(float2(0, floor(input.uv.y * 50.0)));
        float threshold = progress * 1.5 - columnNoise * 0.3 - rowNoise * 0.2;
        if (threshold > 0.5) {
            color = newColor;
        } else {
            float displ = max(0, threshold) * 0.5;
            float2 blownUV = clamp(input.uv + float2(displ * (1.0 + columnNoise), displ * 0.3 * sin(input.uv.y * 20.0)), 0.0, 1.0);
            float4 blown = fromTexture.Sample(transitionSampler, blownUV);
            blown.a *= 1.0 - displ * 2.0;
            color = lerp(newColor, blown, blown.a);
        }
    } else if (mode == 7) {
        float2 center = float2(0.5, 0.5);
        float dist = length(input.uv - center);
        float maxDist = length(float2(0.5, 0.5));
        float rippleRadius = progress * maxDist * 1.3;
        float rippleWidth = 0.08;
        float mask = smoothstep(rippleRadius, rippleRadius - rippleWidth, dist);
        float rippleDist = abs(dist - rippleRadius);
        float waveStrength = (1.0 - smoothstep(0.0, rippleWidth * 2.0, rippleDist))
                           * sin(rippleDist * 60.0) * 0.015
                           * (1.0 - progress);
        float2 waveOffset = normalize(input.uv - center + 0.001) * waveStrength;
        color = lerp(fromTexture.Sample(transitionSampler, input.uv + waveOffset), newColor, mask);
    } else if (mode == 8) {
        float2 center = float2(0.5, 0.5);
        float2 dir = input.uv - center;
        float angle = atan2(dir.x, -dir.y);
        float normalizedAngle = (angle + 3.14159) / 6.28318;
        color = normalizedAngle < progress ? newColor : oldColor;
    } else if (mode == 9) {
        float lum = dot(oldColor.rgb, float3(0.299, 0.587, 0.114));
        float3 thermal = 0.0f;
        if (lum < 0.2) thermal = lerp(float3(0, 0, 0.5), float3(0, 0, 1), lum / 0.2);
        else if (lum < 0.4) thermal = lerp(float3(0, 0, 1), float3(0, 1, 0), (lum - 0.2) / 0.2);
        else if (lum < 0.6) thermal = lerp(float3(0, 1, 0), float3(1, 1, 0), (lum - 0.4) / 0.2);
        else if (lum < 0.8) thermal = lerp(float3(1, 1, 0), float3(1, 0, 0), (lum - 0.6) / 0.2);
        else thermal = lerp(float3(1, 0, 0), float3(1, 1, 1), (lum - 0.8) / 0.2);

        if (progress < 0.4) {
            color = lerp(oldColor, float4(thermal, 1.0), progress / 0.4);
        } else if (progress < 0.6) {
            float glow = ((progress - 0.4) / 0.2) * 0.3;
            color = float4(thermal + glow, 1.0);
        } else {
            float t = (progress - 0.6) / 0.4;
            float newLum = dot(newColor.rgb, float3(0.299, 0.587, 0.114));
            float3 newThermal = float3(1, max(0.5, newLum), newLum * 0.5);
            color = lerp(float4(thermal, 1.0), lerp(float4(newThermal, 1.0), newColor, t), t);
        }
    } else {
        color = lerp(oldColor, newColor, progress);
    }

    color.a *= saturate(gPushConstants.progressOpacity.y);
    return color;
}
