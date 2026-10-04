// CSS filter drop-shadow() over an isolated premultiplied capture.
// The pixel shader blurs and offsets the captured alpha mask, tints it, then
// composites the untouched input image over the shadow in one GPU pass.
// Constants [4..7] are patched during replay with texel size and UV scale.
cbuffer EffectConstants : register(b0)
{
    float4 p0;  // tint RGB, shadow opacity
    float4 p1;  // texel U/V, capture UV scale X/Y (replay-patched)
    float4 p2;  // K taps, sigma in taps, stepPx, offsetPxX
    float4 p3;  // offsetPxY, shadow-only flag
};
Texture2D content : register(t0);
SamplerState contentSampler : register(s0);
struct PsIn { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

float4 main(PsIn i) : SV_Target
{
    const int K = clamp((int)p2.x, 1, 12);
    const float sigma = max(p2.y, 0.5f);
    const float2 texel = p1.xy;
    const float2 uvScale = p1.zw;
    const float2 lo = 0.5f * texel;
    const float2 hi = max(lo, uvScale - lo);
    const float2 baseUv = i.uv;
    const float4 image = content.Sample(contentSampler, clamp(baseUv, lo, hi));
    if (p0.a <= 0.0f)
        return p3.y > 0.5f ? float4(0, 0, 0, 0) : image;
    const float2 sourceUv = baseUv - float2(p2.w, p3.x) * texel;
    const float2 step = texel * p2.z;
    float alpha = 0.0f;
    float weights = 0.0f;
    [loop]
    for (int dy = -K; dy <= K; ++dy)
    {
        [loop]
        for (int dx = -K; dx <= K; ++dx)
        {
            const float weight = exp(-(float)(dx * dx + dy * dy) /
                                     (2.0f * sigma * sigma));
            const float2 uv = clamp(sourceUv + float2((float)dx, (float)dy) * step,
                                    lo, hi);
            alpha += content.Sample(contentSampler, uv).a * weight;
            weights += weight;
        }
    }
    const float shadowAlpha = saturate(alpha / max(weights, 0.0001f)) * p0.a;
    if (p3.y > 0.5f)
        return float4(p0.rgb * shadowAlpha, shadowAlpha);
    const float oneMinusImage = 1.0f - image.a;
    return float4(image.rgb + p0.rgb * shadowAlpha * oneMinusImage,
                  image.a + shadowAlpha * oneMinusImage);
}
