#include "rounded_clip.hlsli"

Texture2D<float4> bitmapTexture : register(t1);

// s0 — bilinear clamp (LowQuality / Linear)
SamplerState linearSampler : register(s0);
// s1 — point clamp (NearestNeighbor — also used by ClearType text path)
SamplerState pointSampler  : register(s1);
// s2 — anisotropic clamp + trilinear mipmap (HighQuality / Fant / Unspecified default)
SamplerState anisoSampler  : register(s2);

struct PsInput
{
    float4 clipPos     : SV_Position;
    float2 uv          : TEXCOORD0;
    float  opacity     : TEXCOORD1;
    nointerpolation float samplerIdx : TEXCOORD2;
    nointerpolation float2 uvMax : TEXCOORD3;
};

float4 SamplePixelated(float2 uv, float2 uvMax)
{
    uint texWidth, texHeight;
    bitmapTexture.GetDimensions(texWidth, texHeight);
    float2 sourceSize = float2(texWidth, texHeight);
    float2 texelPerPixel = float2(
        length(float2(ddx(uv.x), ddy(uv.x))) * sourceSize.x,
        length(float2(ddx(uv.y), ddy(uv.y))) * sourceSize.y);
    float2 step = max(1.0f, floor(1.0f / max(texelPerPixel, 0.000001f) + 0.5f));
    float2 grid = uv * sourceSize * step - 0.5f;
    float2 lower = floor(grid);
    float2 blend = frac(grid);
    int2 lastPixel = max(int2(0, 0), int2(floor(uvMax * sourceSize - 0.0001f)));
    int2 p00 = clamp(int2(floor(lower / step)), int2(0, 0), lastPixel);
    int2 p10 = clamp(int2(floor((lower + float2(1, 0)) / step)), int2(0, 0), lastPixel);
    int2 p01 = clamp(int2(floor((lower + float2(0, 1)) / step)), int2(0, 0), lastPixel);
    int2 p11 = clamp(int2(floor((lower + 1.0f) / step)), int2(0, 0), lastPixel);
    float4 c00 = bitmapTexture.Load(int3(p00, 0));
    float4 c10 = bitmapTexture.Load(int3(p10, 0));
    float4 c01 = bitmapTexture.Load(int3(p01, 0));
    float4 c11 = bitmapTexture.Load(int3(p11, 0));
    return lerp(lerp(c00, c10, blend.x), lerp(c01, c11, blend.x), blend.y);
}

float4 main(PsInput input) : SV_Target
{
    float clipCoverage = RoundedClipCoverage(input.clipPos.xy);

    int idx = (int)input.samplerIdx;
    float4 color;
    if (idx == 3)
    {
        color = SamplePixelated(input.uv, input.uvMax);
    }
    else if (idx == 1)
    {
        // NearestNeighbor — pixel-art / 1:1 UI sprites
        color = bitmapTexture.Sample(pointSampler, input.uv);
    }
    else if (idx == 2)
    {
        // HighQuality — anisotropic + mipmap. Falls back to bilinear when the
        // texture has no mipchain (mipLevels == 1), which still beats Linear
        // because the driver picks an anisotropy taps schedule appropriate
        // for the screen-space derivatives.
        color = bitmapTexture.Sample(anisoSampler, input.uv);
    }
    else
    {
        // 0 (Linear / LowQuality) — bilinear, no mipmap
        color = bitmapTexture.Sample(linearSampler, input.uv);
    }
    color *= input.opacity;
    color *= clipCoverage;
    if (color.a < 1.0 / 255.0) discard;
    return color;
}
