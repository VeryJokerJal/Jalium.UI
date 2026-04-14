// Impeller solid fill fragment shader
// Used by ImpellerVulkanEngine for CPU-tessellated geometry rendering.
//
// Input: Interpolated color from vertex shader
// Output: Final pixel color (premultiplied alpha)
//
// Compile: dxc -spirv -T ps_6_0 -E main impeller_solid_fill.frag.hlsl -Fo impeller_solid_fill.frag.spv

struct PSInput {
    float4 position : SV_POSITION;
    [[vk::location(0)]] float4 color : COLOR;
};

float4 main(PSInput input) : SV_TARGET {
    return input.color;
}
