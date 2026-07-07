// Impeller solid fill fragment shader
// Used by ImpellerVulkanEngine / VelloVulkanEngine (CPU-tessellated geometry)
// and the stencil-then-cover PSOs — all five pipelines share this FS and the
// EngineBatchPushConstants layout.
//
// Input: Interpolated color from vertex shader (premultiplied alpha)
// Output: Final pixel color, masked by the per-batch rounded-clip channel.
//
// Rounded-clip channel: each engine batch snapshots the innermost live
// rounded clip (resolved to physical px by SyncClipToEngine, mirroring
// ImpellerD3D12Engine::SetRoundedClip). clipFlags.x > 0.5 arms the mask;
// the SDF uses CIRCULAR per-corner radii (TL, TR, BR, BL) with a 1-px
// fwidth/smoothstep AA ramp — the same rounded-clip model as the D3D12
// rounded_clip.hlsli helper. This closes the "engine batch shader has no
// rounded-clip channel" gap: Path / Polygon / stroke content inside a
// rounded Border no longer leaks through the corners (previously only the
// rectangular AABB scissor applied).
//
// Push-constant block BYTE-MATCHES the C++ EngineBatchPushConstants (112 B).
//
// Compile: dxc -spirv -T ps_6_0 -E main impeller_solid_fill.frag.hlsl -Fo impeller_solid_fill.frag.spv

[[vk::push_constant]]
struct PushConstants {
    float4x4 mvp;             // vertex stage only — declared for layout parity
    float4 roundedClipRect;   // L, T, R, B (physical px)
    float4 roundedClipRadii;  // TL, TR, BR, BL circular radii (px)
    float4 clipFlags;         // .x > 0.5 → rounded clip enabled
} pc;

struct PSInput {
    float4 position : SV_POSITION;
    [[vk::location(0)]] float4 color : COLOR;
};

// Anti-aliased coverage of the per-corner rounded rect at this fragment.
// Quadrant-selects the corner's own circular radius (each quadrant only
// depends on its own corner), then evaluates the centred rounded-box SDF and
// converts to a ~1-px smoothstep coverage ramp via fwidth.
float RoundedClipCoverage(float2 p, float4 rect, float4 radii)
{
    const float midX = (rect.x + rect.z) * 0.5f;
    const float midY = (rect.y + rect.w) * 0.5f;
    float r;
    if (p.y < midY) {
        r = (p.x < midX) ? radii.x : radii.y;   // TL : TR
    } else {
        r = (p.x >= midX) ? radii.z : radii.w;  // BR : BL
    }
    const float2 center   = (rect.xy + rect.zw) * 0.5f;
    const float2 halfSize = max((rect.zw - rect.xy) * 0.5f, float2(0.0001f, 0.0001f));
    r = clamp(r, 0.0f, min(halfSize.x, halfSize.y));
    const float2 q = abs(p - center) - halfSize + r;
    const float d = min(max(q.x, q.y), 0.0f) + length(max(q, 0.0f)) - r;
    const float aa = max(fwidth(d), 0.0001f);
    return 1.0f - smoothstep(-aa * 0.5f, aa * 0.5f, d);
}

float4 main(PSInput input) : SV_TARGET {
    float4 color = input.color;
    if (pc.clipFlags.x > 0.5f) {
        // fwidth runs under uniform control flow (push-constant branch) and
        // before the discard, so helper-lane derivatives stay defined. The
        // stencil FILL passes bind this FS with color writes disabled — a
        // coverage discard there merely drops stencil toggles outside the
        // clip, which the cover pass would have masked anyway.
        const float cov = RoundedClipCoverage(input.position.xy, pc.roundedClipRect, pc.roundedClipRadii);
        if (cov <= 0.0f) {
            discard;
        }
        color *= cov;   // premultiplied alpha: scale ALL channels by coverage
    }
    return color;
}
