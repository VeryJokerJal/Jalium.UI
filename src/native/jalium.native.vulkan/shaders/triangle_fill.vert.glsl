#version 450
//
// Vertex shader for filled-polygon GPU command. Vertex input is in *local*
// (pre-transform) path-space — same coordinates as the cached
// PathGeometryCache entry. The 2x3 affine transform from the CPU transform
// stack is supplied via a push constant and applied here in the vertex shader,
// rather than per-vertex on the CPU. That removes ~1ms/frame of CPU work per
// 100 paths, which is the single biggest reason Vulkan can match the D3D12
// backend's "GPU-batched" path geometry path.
//
// Push-constant layout MUST match TriangleFillPushConstants in
// vulkan_render_target.cpp (std430 padding rules), in particular:
//   color           : float4
//   screenSize      : float2
//   padding         : float2
//   roundedClipRect : float4
//   roundedClipRadius : float2
//   clipFlags         : float2
//   transformRow0     : float4   // (m11, m12, dx, _)
//   transformRow1     : float4   // (m21, m22, dy, _)

layout(push_constant) uniform PushConstants {
    vec4 color;
    vec2 screenSize;
    vec2 padding;
    vec4 roundedClipRect;
    vec2 roundedClipRadius;
    vec2 clipFlags;
    vec4 transformRow0;
    vec4 transformRow1;
} gPush;

layout(location = 0) in vec2 inPosition;

void main() {
    vec2 screen = max(gPush.screenSize, vec2(1.0, 1.0));

    // Affine 2x3 transform from local → world (pixel) space.
    vec2 worldPos = vec2(
        gPush.transformRow0.x * inPosition.x + gPush.transformRow0.y * inPosition.y + gPush.transformRow0.z,
        gPush.transformRow1.x * inPosition.x + gPush.transformRow1.y * inPosition.y + gPush.transformRow1.z);

    gl_Position = vec4(
        worldPos.x / screen.x * 2.0 - 1.0,
        1.0 - worldPos.y / screen.y * 2.0,
        0.0,
        1.0);
}
