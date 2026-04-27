// Vertex shader for filled-polygon GPU command.
//
// Vertex input is in *local* (pre-transform) path-space — same coordinates as
// the cached PathGeometryCache entry. The 2x3 affine transform from the CPU
// transform stack is supplied via a push constant and applied here, in the
// vertex shader, rather than per-vertex on the CPU. That removes ~1ms/frame of
// CPU work per 100 paths and is the single biggest reason Vulkan can match the
// D3D12 backend's "GPU-batched" path geometry path.
//
// Layout: push-constant struct must match TriangleFillPushConstants in
// vulkan_render_target.cpp (alignment-padded). The transform is stored as two
// float4 rows so std430 alignment matches the CPU layout one-for-one.

struct PushConstants
{
    float4 color;
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    float2 clipFlags;
    // Affine 2x3 transform: world.x = m11*x + m12*y + dx, world.y = m21*x + m22*y + dy.
    // Packed as two float4: row0 = (m11, m12, dx, _), row1 = (m21, m22, dy, _).
    float4 transformRow0;
    float4 transformRow1;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct VsInput
{
    float2 position : POSITION;
};

struct VsOutput
{
    float4 position : SV_Position;
};

VsOutput main(VsInput input)
{
    const float2 screenSize = max(gPushConstants.screenSize, float2(1.0f, 1.0f));

    // Apply the 2x3 affine transform from local space to world (pixel) space.
    const float2 worldPos = float2(
        gPushConstants.transformRow0.x * input.position.x + gPushConstants.transformRow0.y * input.position.y + gPushConstants.transformRow0.z,
        gPushConstants.transformRow1.x * input.position.x + gPushConstants.transformRow1.y * input.position.y + gPushConstants.transformRow1.z);

    VsOutput output;
    output.position = float4(
        worldPos.x / screenSize.x * 2.0f - 1.0f,
        1.0f - worldPos.y / screenSize.y * 2.0f,
        0.0f,
        1.0f);
    return output;
}
