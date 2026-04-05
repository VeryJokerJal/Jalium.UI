// Vello GPU Pipeline V2 — Shared Structures and Constants
// Matches the reference Vello (Rust/WGSL) pipeline data structures,
// adapted for D3D12/HLSL with Jalium.UI's CPU encoding format.

#ifndef VELLO_SHARED_HLSLI
#define VELLO_SHARED_HLSLI

// ============================================================================
// Tile geometry constants
// ============================================================================
#define TILE_WIDTH  16u
#define TILE_HEIGHT 16u
#define TILE_SCALE  0.0625  // 1.0 / TILE_WIDTH

// Bin = 16x16 tiles
#define N_TILE_X 16u
#define N_TILE_Y 16u
#define N_TILE   256u  // N_TILE_X * N_TILE_Y

// Workgroup sizes
#define WG_SIZE 256u

// ============================================================================
// PTCL constants
// ============================================================================
#define PTCL_INITIAL_ALLOC 64u
#define PTCL_INCREMENT     256u
#define PTCL_HEADROOM      2u

// PTCL command opcodes
#define CMD_END        0u
#define CMD_FILL       1u
#define CMD_STROKE     2u
#define CMD_SOLID      3u
#define CMD_COLOR      5u
#define CMD_LIN_GRAD   6u
#define CMD_RAD_GRAD   7u
#define CMD_SWEEP_GRAD 8u
#define CMD_IMAGE      9u
#define CMD_BEGIN_CLIP 10u
#define CMD_END_CLIP   11u
#define CMD_JUMP       12u
#define CMD_BLUR_RECT  13u

// ============================================================================
// Fill rules
// ============================================================================
#define FILL_EVEN_ODD 0u
#define FILL_NON_ZERO 1u

// ============================================================================
// Draw info flags
// ============================================================================
#define DRAW_INFO_FLAGS_FILL_RULE_BIT 1u

// ============================================================================
// Blend stack
// ============================================================================
#define BLEND_STACK_SPLIT 4u

// ============================================================================
// Gradient constants
// ============================================================================
#define GRAD_RAMP_WIDTH 256u

// Gradient extend modes
#define EXTEND_PAD     0u
#define EXTEND_REPEAT  1u
#define EXTEND_REFLECT 2u

// ============================================================================
// Brush types
// ============================================================================
#define BRUSH_SOLID   0u
#define BRUSH_LINEAR  1u
#define BRUSH_RADIAL  2u
#define BRUSH_SWEEP   3u
#define BRUSH_IMAGE   4u

// ============================================================================
// Bump allocator failure flags
// ============================================================================
#define STAGE_BINNING    0x1u
#define STAGE_TILE_ALLOC 0x2u
#define STAGE_FLATTEN    0x4u
#define STAGE_PATH_COUNT 0x8u
#define STAGE_COARSE     0x10u

// ============================================================================
// Numerical robustness constants
// ============================================================================
#define ONE_MINUS_ULP  0.99999994
#define ROBUST_EPSILON 2e-7

// ============================================================================
// GPU Data Structures
// ============================================================================

// BumpAllocators: 32 bytes — GPU atomic counters for dynamic buffer allocation.
// Stored in a RWByteAddressBuffer, accessed via InterlockedAdd.
// Offsets: failed=0, binning=4, ptcl=8, tile=12, seg_counts=16, segments=20, blend=24, lines=28
#define BUMP_FAILED      0u
#define BUMP_BINNING     4u
#define BUMP_PTCL        8u
#define BUMP_TILE       12u
#define BUMP_SEG_COUNTS 16u
#define BUMP_SEGMENTS   20u
#define BUMP_BLEND      24u
#define BUMP_LINES      28u

// VelloConfig: constant buffer matching the CPU-side VelloConfig struct.
// cbuffer VelloConfig : register(b0) { ... };

// PathSegment: input from CPU encoding (48 bytes) — unchanged from V1
struct PathSegment
{
    float2 p0;
    float2 p1;
    float2 p2;
    float2 p3;
    uint   tag;        // 0=line, 1=quad, 2=cubic
    uint   pathIndex;
    uint   pad0, pad1;
};

// LineSoup: flattened line segment (24 bytes) — output of flatten
struct LineSoup
{
    uint   path_ix;
    float2 p0;
    float2 p1;
    uint   pad;
};

// VelloPath: per-draw-object path info (20 bytes) — output of tile_alloc
struct VelloPath
{
    uint4 bbox;   // tile-space bounding box (x0, y0, x1, y1)
    uint  tiles;  // offset into VelloTile array
};

// VelloTile: per-tile info (8 bytes)
// Before coarse: segment_count_or_ix = count of segments
// After coarse: segment_count_or_ix = ~allocation_base (bitwise NOT for detection)
struct VelloTile
{
    int  backdrop;
    uint segment_count_or_ix;
};

// Segment: final tile-relative segment for fine shader (20 bytes)
struct Segment
{
    float2 point0;  // relative to tile origin
    float2 point1;
    float  y_edge;  // y coordinate for edge contribution at tile left
};

// SegmentCount: intermediate for path_count/path_tiling (8 bytes)
struct SegmentCount
{
    uint line_ix;  // index into LineSoup array
    uint counts;   // packed: low 16 = seg within line, high 16 = seg within slice
};

// BinHeader: per-bin metadata (8 bytes)
struct BinHeader
{
    uint element_count;
    uint chunk_offset;
};

// PathBbox: atomic path bounding box (24 bytes) — written by flatten via atomics
struct PathBbox
{
    int x0, y0, x1, y1;
    uint draw_flags;   // contains fill rule (bit 0) and other flags
    uint trans_ix;     // unused in our encoding (kept for compatibility)
};

// DrawMonoid: per-draw-object prefix sum (16 bytes)
struct DrawMonoid
{
    uint path_ix;
    uint clip_ix;
    uint scene_offset;
    uint info_offset;
};

// DrawTag: per-draw command from CPU encoding (16 bytes)
struct DrawTag
{
    uint tag;          // 0=Fill, 1=BeginClip, 2=EndClip, 3=BlurRect
    uint pathIdx;
    uint blendMode;
    float alpha;
};

// PathInfo: per-path metadata from CPU encoding (32 bytes) — unchanged from V1
struct PathInfo
{
    uint segOffset;
    uint segCount;
    uint fillRule;
    uint tileOffset;
    uint tileBboxX, tileBboxY;
    uint tileBboxW, tileBboxH;
};

// PathDraw: per-path draw info from CPU encoding (64 bytes) — unchanged from V1
struct PathDraw
{
    float4 color;
    float4 bbox;
    uint   brushType;
    uint   gradientIndex;
    float  gp0, gp1, gp2, gp3, gp4, gp5;
};

// Segment size in uint32s (for PTCL addressing)
#define SEG_SIZE 5u

// ============================================================================
// Helper functions
// ============================================================================

// Bounding box intersection
float4 bbox_intersect(float4 a, float4 b)
{
    return float4(max(a.xy, b.xy), min(a.zw, b.zw));
}

// Pack two 16-bit values into a uint32
uint pack_u16(uint lo, uint hi)
{
    return (lo & 0xFFFFu) | (hi << 16u);
}

// Unpack lower 16 bits
uint unpack_lo16(uint v)
{
    return v & 0xFFFFu;
}

// Unpack upper 16 bits
uint unpack_hi16(uint v)
{
    return v >> 16u;
}

#endif // VELLO_SHARED_HLSLI
