// Fullscreen-triangle vertex shader for compositing the Vello GPU-compute
// output image onto the swap chain. Emits 3 vertices covering the viewport with
// uv 0..1 (top-left origin), matching the Vello fine stage's pixel layout 1:1.
struct VSOut {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VSOut main(uint vertexId : SV_VertexID) {
    VSOut o;
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2); // (0,0) (2,0) (0,2)
    o.uv = uv;
    o.position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
    return o;
}
