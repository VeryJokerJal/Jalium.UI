#include "rounded_clip.hlsli"
#include "../../jalium.native.core/shaders/continuous_corner.hlsli"

struct PsInput
{
    float4 clipPos : SV_Position;
    float2 localPos : TEXCOORD0;
    nointerpolation float2 rectSize : TEXCOORD1;
    float4 fillColor : COLOR0;
    nointerpolation float3 shapeParams : TEXCOORD2;
};

float4 main(PsInput input) : SV_Target
{
    float clipCoverage = RoundedClipCoverage(input.clipPos.xy);
    float4 color = input.fillColor;
    [branch]
    if (input.shapeParams.z < 1.5)
    {
        float2 halfSize = input.rectSize * 0.5;
        float2 p = input.localPos - halfSize;
        float dist = JaliumSdFullSuperellipse(p, halfSize, input.shapeParams.x);
        float aa = JaliumFullSuperellipseAaWidth(p, halfSize, input.shapeParams.x);
        aa *= input.shapeParams.y;
        float fillAlpha = 1.0 - smoothstep(-aa * 0.5, aa * 0.5, dist);
        color *= fillAlpha;
    }
    color *= clipCoverage;
    if (color.a < 1.0 / 255.0) discard;
    return color;
}
