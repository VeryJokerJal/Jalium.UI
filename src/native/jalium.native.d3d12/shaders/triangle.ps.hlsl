#include "rounded_clip.hlsli"

struct PsInput
{
    float4 clipPos : SV_Position;
    float4 color   : COLOR0;
};

float4 main(PsInput input) : SV_Target
{
    float clipCoverage = RoundedClipCoverage(input.clipPos.xy);
    float4 color = input.color * clipCoverage;
    if (color.a < 1.0 / 255.0) discard;
    return color;
}
