// Dedicated INK-LAYER composite fragment shader (premultiplied source).
//
// This is a near-exact mirror of bitmap_quad.frag.hlsl. The ONLY semantic
// difference is the opacity line in main():
//   bitmap_quad (STRAIGHT source):  color.a *= opacity;   // scale alpha only
//   ink_composite (PREMUL source):  color    *= opacity;  // scale ALL channels
//
// Why a separate shader instead of reusing bitmap_quad? The resident ink
// layer image is PREMULTIPLIED (the brush shaders emit premultiplied RGBA and
// the ink brush pipeline blends with srcColorBlendFactor = ONE; see
// Ink/Shaders/BrushShaderPreamble.hlsl + vulkan_ink_layer.cpp ConfigureBlend).
// The generic bitmap pipeline blends STRAIGHT alpha (SRC_ALPHA /
// ONE_MINUS_SRC_ALPHA) and its shader scales only alpha by opacity — correct
// for STRAIGHT producers (polygon/stroke rasterizers, WIC/stb images) but it
// double-multiplies a premultiplied source, darkening AA edges and translucent
// strokes. This shader is paired with a dedicated PREMULTIPLIED-SrcOver
// pipeline (inkCompositePipeline, srcColorBlendFactor = ONE) so the shared
// straight-alpha bitmapPipeline and its producers are left completely
// untouched. Scaling all four channels by the layer opacity here keeps the
// source premultiplied, so out = src*1 + dst*(1 - src.a) is correct for any
// opacity in [0,1].
//
// The register bindings (t0 texture, s1 sampler) and push-constant layout are
// identical to bitmap_quad so this shader can ride the same bitmapPipelineLayout
// + frameDescriptorSetLayout and the shared bitmap_quad.vert.hlsl vertex shader.

Texture2D bitmapTexture : register(t0);
SamplerState bitmapSampler : register(s1);

struct PushConstants
{
    float4 rect;
    float4 uvOpacity;
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    float2 clipFlags;
    float4 innerRoundedClipRect;
    float2 innerRoundedClipRadius;
    float2 padding2;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

bool IsInsideRoundRect(float2 pixel, float4 rect, float2 radius)
{
    const float left = rect.x;
    const float top = rect.y;
    const float right = rect.z;
    const float bottom = rect.w;
    if (pixel.x < left || pixel.y < top || pixel.x > right || pixel.y > bottom) {
        return false;
    }

    const float rx = max(radius.x, 0.0f);
    const float ry = max(radius.y, 0.0f);
    if (rx <= 0.0f || ry <= 0.0f) {
        return true;
    }

    if (pixel.x < left + rx && pixel.y < top + ry) {
        const float2 delta = (pixel - float2(left + rx, top + ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }
    if (pixel.x > right - rx && pixel.y < top + ry) {
        const float2 delta = (pixel - float2(right - rx, top + ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }
    if (pixel.x < left + rx && pixel.y > bottom - ry) {
        const float2 delta = (pixel - float2(left + rx, bottom - ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }
    if (pixel.x > right - rx && pixel.y > bottom - ry) {
        const float2 delta = (pixel - float2(right - rx, bottom - ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }

    return true;
}

float4 main(PsInput input) : SV_Target
{
    if (gPushConstants.clipFlags.x > 0.5f && !IsInsideRoundRect(input.position.xy, gPushConstants.roundedClipRect, gPushConstants.roundedClipRadius)) {
        discard;
    }
    if (gPushConstants.clipFlags.y > 0.5f && IsInsideRoundRect(input.position.xy, gPushConstants.innerRoundedClipRect, gPushConstants.innerRoundedClipRadius)) {
        discard;
    }
    float4 color = bitmapTexture.Sample(bitmapSampler, input.uv);
    // PREMULTIPLIED source: scale every channel by the layer opacity (NOT just
    // alpha) so the texel stays premultiplied for the premultiplied-SrcOver
    // blend (srcColorBlendFactor = ONE). This is the one line that differs from
    // bitmap_quad.frag.hlsl.
    color *= saturate(gPushConstants.uvOpacity.z);
    return color;
}
