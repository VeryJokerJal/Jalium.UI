Texture2D<float4> bitmapTexture : register(t1);
SamplerState bitmapSampler : register(s0);

struct PsInput
{
    float4 clipPos : SV_Position;
    float2 uv      : TEXCOORD0;
    float  opacity : TEXCOORD1;
};

float4 main(PsInput input) : SV_Target
{
    float4 color = bitmapTexture.Sample(bitmapSampler, input.uv);
    color *= input.opacity;
    if (color.a < 1.0 / 255.0) discard;
    return color;
}
