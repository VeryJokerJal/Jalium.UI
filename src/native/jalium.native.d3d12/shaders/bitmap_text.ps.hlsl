Texture2D<float> glyphAtlas : register(t1);
SamplerState glyphSampler : register(s0);

struct PsInput
{
    float4 clipPos : SV_Position;
    float2 uv      : TEXCOORD0;
    float4 color   : COLOR0;
};

float4 main(PsInput input) : SV_Target
{
    float alpha = glyphAtlas.Sample(glyphSampler, input.uv);
    float contrast = saturate(alpha * 1.2 - 0.1);
    alpha = lerp(alpha, contrast, 0.3);

    float4 color = input.color;
    color.a *= alpha;
    color.rgb *= color.a;

    if (color.a < 1.0 / 255.0) discard;
    return color;
}
