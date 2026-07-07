// Dedicated text-glyph pipeline — vertex shader.
//
// Draws one quad (6 corners via SV_VertexID) per glyph instance (SV_InstanceID),
// reading the glyph's screen rect + atlas UV sub-rect + premultiplied colour from
// a per-frame StructuredBuffer. This is what the generic bitmap_quad pipeline
// cannot do: each glyph samples its OWN atlas sub-rect (uvMin..uvMax), not the
// whole image from (0,0). The NDC mapping is copied verbatim from
// bitmap_quad.vert.hlsl so the negative-height viewport + the 1 - y/h flip stay
// consistent with every other Vulkan primitive (do not add/remove a flip here).

struct GlyphInstance
{
    float2 pos;     // screen-space top-left, physical px (matches VkGlyphInstance posX/posY)
    float2 size;    // quad size, physical px (sizeX/sizeY)
    float2 uvMin;   // atlas UV top-left, normalized (uvMinX/uvMinY)
    float2 uvMax;   // atlas UV bottom-right, normalized (uvMaxX/uvMaxY)
    float4 color;   // premultiplied RGBA; colorR < 0 = colour-emoji sentinel
};

// set 0, binding 2 — read in the vertex stage (see textDescriptorSetLayout).
[[vk::binding(2)]] StructuredBuffer<GlyphInstance> gGlyphs;

struct TextPushConstants
{
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    float2 clipFlags;
    float4 innerRoundedClipRect;
    float2 innerRoundedClipRadius;
    float2 padding2;
    // Per-corner OUTER-clip radii (TL, TR, BR, BL). Fragment-shader-only;
    // declared here solely so the push-constant block layout matches the
    // 112-byte C++ TextPushConstants across stages.
    float4 perCornerRadiusX;
    float4 perCornerRadiusY;
};

[[vk::push_constant]]
TextPushConstants gPushConstants;

struct VsOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float4 color : TEXCOORD1;
};

VsOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    const float2 corners[6] = {
        float2(0.0f, 0.0f),
        float2(1.0f, 0.0f),
        float2(1.0f, 1.0f),
        float2(0.0f, 0.0f),
        float2(1.0f, 1.0f),
        float2(0.0f, 1.0f)
    };

    const GlyphInstance g = gGlyphs[instanceId];
    const float2 corner = corners[vertexId];

    const float2 pixelPosition = g.pos + corner * g.size;
    const float2 screenSize = max(gPushConstants.screenSize, float2(1.0f, 1.0f));

    VsOutput output;
    output.position = float4(
        pixelPosition.x / screenSize.x * 2.0f - 1.0f,
        1.0f - pixelPosition.y / screenSize.y * 2.0f,
        0.0f,
        1.0f);
    output.uv = lerp(g.uvMin, g.uvMax, corner);
    output.color = g.color;
    return output;
}
