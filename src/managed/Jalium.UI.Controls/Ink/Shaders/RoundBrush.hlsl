// Round brush — uniform-width capsule along the polyline with soft 1-px AA.
// The simplest brush; a baseline reference for other shaders.
//
// The shared preamble (BrushShaderPreamble.hlsl) is prepended at compile
// time, so SdfPolyline / HalfWidthAt / StrokeColor etc. are in scope.

float4 BrushMain(float2 px)
{
    float2 r = SdfPolyline(px);         // .x = distance, .y = arc 0..1
    float halfW = HalfWidthAt(r.y);
    float cov = StrokeCoverage(r.x, halfW);
    if (cov <= 0) discard;

    // StrokeColor is premultiplied — scale by coverage to get premul output.
    return StrokeColor * cov;
}
