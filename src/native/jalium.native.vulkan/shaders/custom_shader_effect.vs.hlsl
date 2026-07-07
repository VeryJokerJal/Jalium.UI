// Shared custom-shader-effect vertex shader (Stage 4 / S1 GPU-RT path).
//
// Pre-compiled to embedded SPIR-V (gen_effect_builtin_spv.ps1 ->
// vulkan_effect_builtin_shaders.h). Emitting the VS as embedded SPIR-V lets the
// custom-shader pipeline BASE (VS + descriptor/pipeline layouts) come up with NO
// runtime DXC, which is what makes the built-in ColorMatrix / Emboss effects
// work when dxcompiler.dll is absent. User DrawShaderEffectFromSource pixel
// shaders still compile through the runtime DXC, but they reuse this same
// embedded VS module.
//
// Byte-for-byte the source that used to live in kCustomShaderVsHlsl inside
// vulkan_render_target.cpp. It draws the element quad (rect in physical px),
// converts to NDC, and bakes uvScale into the uv so the pixel shaders sample the
// capture sub-rect of the shared (larger) upload image directly.
//
// Y ORIENTATION: the GPU replay loop binds a NEGATIVE-height (Y-flipped)
// viewport (RenderVulkanBatches: viewport.y=extent.height, height=-extent.height,
// "flips Y axis to match OpenGL/D3D conventions"). Every other replay VS
// (bitmap_quad, solid_rect, ...) therefore emits ndc.y = 1 - 2*(py/H) so that a
// SMALL py (top of the element) lands at the TOP of the framebuffer under that
// flip. This VS previously emitted ndc.y = 2*(py/H) - 1 (a "no flip" convention
// that does NOT hold here), which rendered every built-in / user effect quad
// VERTICALLY MIRRORED about the framebuffer centre — invisible for the
// vertically-near-symmetric ColorMatrix scene but a gross upside-down layout for
// the asymmetric Emboss scene (parity: full-image vertical flip, 44% -> WARN).
// Matching bitmap_quad's ndc.y (uv unchanged — the upload image is blitted
// top-down exactly as the bitmap path stores it) fixes ALL custom-shader effects
// (ColorMatrix / Emboss / user DrawShaderEffectFromSource) at once.
//
// Push-constant only; no descriptor bindings, so no register shift applies to
// the VS itself. The gen script still compiles it with the same shift flags for
// consistency (they are inert here).

struct Geom { float4 rect; float2 screenSize; float2 uvScale; };
[[vk::push_constant]] Geom geom;

struct VsOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

VsOut main(uint vid : SV_VertexID)
{
    float2 corners[6] = {
        float2(0,0), float2(1,0), float2(1,1),
        float2(0,0), float2(1,1), float2(0,1)
    };
    float2 c  = corners[vid];
    float2 px = geom.rect.xy + c * geom.rect.zw;
    // The replay loop binds a Y-flipped (negative-height) viewport, so match the
    // bitmap_quad / solid_rect convention: X maps 2*(px/W)-1, Y maps
    // 1 - 2*(py/H). Emitting the "no flip" 2*(py/H)-1 here rendered every effect
    // quad upside-down. uv is left top-down (the upload image is blitted top-down).
    float ndcX = (px.x / geom.screenSize.x) * 2.0f - 1.0f;
    float ndcY = 1.0f - (px.y / geom.screenSize.y) * 2.0f;
    VsOut o;
    o.pos = float4(ndcX, ndcY, 0.0f, 1.0f);
    o.uv  = c * geom.uvScale;
    return o;
}
