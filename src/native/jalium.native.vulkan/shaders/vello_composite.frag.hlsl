// Samples the premultiplied-alpha Vello output image and returns it unchanged;
// the pipeline's SrcOver premultiplied blend (srcColor ONE, dstColor
// ONE_MINUS_SRC_ALPHA) composites it over the swap-chain content. Explicit
// vk::binding so the descriptor set is {0: sampled image, 1: sampler} with no
// register-class shift needed.
[[vk::binding(0)]] Texture2D<float4> velloOutput;
[[vk::binding(1)]] SamplerState      velloSampler;

float4 main(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
    return velloOutput.Sample(velloSampler, uv);
}
