// Vello GPU Pipeline V3 — bbox_clear
// Port of vello 0.10.0 shader/bbox_clear.wgsl.
// Initializes every path bounding box to the empty (inverted) extent.
//
// Bindings: b0 config | u0 path_bboxes
// Dispatch: (ceil(n_drawobj / 256), 1, 1)

#include "vello_shared.hlsli"

RWStructuredBuffer<PathBbox> path_bboxes : register(u0);

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID)
{
    uint ix = global_id.x;
    if (ix < n_path) {
        path_bboxes[ix].x0 = 0x7fffffff;
        path_bboxes[ix].y0 = 0x7fffffff;
        path_bboxes[ix].x1 = int(0x80000000);
        path_bboxes[ix].y1 = int(0x80000000);
    }
}
