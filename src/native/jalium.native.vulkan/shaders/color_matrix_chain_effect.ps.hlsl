// Ordered CSS color-matrix filter chain over an isolated premultiplied capture.
// Each stage clamps straight RGBA before feeding the following matrix.
cbuffer EffectConstants : register(b0)
{
    float4 meta;       // stage count, unused
    float4 rows[320];  // 64 stages × (four coefficient rows + one offset row)
};
Texture2D content : register(t0);
SamplerState contentSampler : register(s0);
struct PsIn { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

float4 main(PsIn input) : SV_Target
{
    const float4 source = content.Sample(contentSampler, input.uv);
    float4 value = float4(source.a > 0.0001f ? source.rgb / source.a :
        float3(0, 0, 0), source.a);
    [loop]
    for (int stage = 0; stage < (int)meta.x; ++stage)
    {
        const int base = stage * 5;
        const float4 before = value;
        value = saturate(float4(
            dot(rows[base + 0], before) + rows[base + 4].x,
            dot(rows[base + 1], before) + rows[base + 4].y,
            dot(rows[base + 2], before) + rows[base + 4].z,
            dot(rows[base + 3], before) + rows[base + 4].w));
    }
    return float4(value.rgb * value.a, value.a);
}
