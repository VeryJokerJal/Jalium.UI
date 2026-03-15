Texture2D frameTexture : register(t0);
SamplerState frameSampler : register(s1);

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

float4 main(PsInput input) : SV_Target
{
    return frameTexture.Sample(frameSampler, input.uv);
}
