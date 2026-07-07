// Impeller solid fill vertex shader
// Used by ImpellerVulkanEngine / VelloVulkanEngine (CPU-tessellated geometry)
// and the stencil-then-cover PSOs — all five pipelines share this VS and the
// EngineBatchPushConstants layout.
//
// Input: Position (float2) + Color (float4) per vertex
// Output: Clip-space position + interpolated color
//
// Push-constant block BYTE-MATCHES the C++ EngineBatchPushConstants in
// vulkan_render_target.cpp (112 bytes): the VS consumes only mvp; the
// rounded-clip channel (fragment stage) is declared here solely so the block
// layout is identical across stages.
//
// Compile: dxc -spirv -T vs_6_0 -E main impeller_solid_fill.vert.hlsl -Fo impeller_solid_fill.vert.spv

[[vk::push_constant]]
struct PushConstants {
    float4x4 mvp;
    float4 roundedClipRect;   // L, T, R, B (physical px) — fragment stage only
    float4 roundedClipRadii;  // TL, TR, BR, BL circular radii (px) — fragment stage only
    float4 clipFlags;         // .x > 0.5 → rounded clip enabled — fragment stage only
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
