// Impeller solid fill vertex shader
// Used by ImpellerVulkanEngine for CPU-tessellated geometry rendering.
//
// Input: Position (float2) + Color (float4) per vertex
// Output: Clip-space position + interpolated color
//
// Compile: dxc -spirv -T vs_6_0 -E main impeller_solid_fill.vert.hlsl -Fo impeller_solid_fill.vert.spv

[[vk::push_constant]]
struct PushConstants {
    float4x4 mvp;
} pc;

struct VSInput {
    [[vk::location(0)]] float2 position : POSITION;
    [[vk::location(1)]] float4 color    : COLOR;
};

struct VSOutput {
    float4 position : SV_POSITION;
    [[vk::location(0)]] float4 color : COLOR;
};

VSOutput main(VSInput input) {
    VSOutput output;
    output.position = mul(pc.mvp, float4(input.position, 0.0, 1.0));
    output.color = input.color;
    return output;
}
