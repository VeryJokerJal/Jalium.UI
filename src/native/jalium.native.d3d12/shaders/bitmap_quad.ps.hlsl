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
};

float4 main(PsInput input) : SV_Target
{
    float clipCoverage = RoundedClipCoverage(input.clipPos.xy);

    int idx = (int)input.samplerIdx;
    float4 color;
    if (idx == 1)
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
