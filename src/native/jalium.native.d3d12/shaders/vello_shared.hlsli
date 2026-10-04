// Vello GPU Pipeline V3 — Shared Structures and Constants
// Faithful port of vello 0.10.0 (2026-08-14) shader/shared/*.wgsl to HLSL,
// compiled for D3D12 with fxc (cs_5_1) and for Vulkan with dxc (-spirv, dx-layout).
//
// Sources mirrored here (upstream vello_shaders/shader/shared/):
//   config.wgsl pathtag.wgsl drawtag.wgsl ptcl.wgsl tile.wgsl segment.wgsl
//   bump.wgsl clip.wgsl bbox.wgsl transform.wgsl util.wgsl
// blend.wgsl is ported separately in vello_blend.hlsli (only fine uses it).
//
// The scene is a single u32 stream (see VelloConfig bases), matching
// vello_encoding's packed layout produced by the C++ encoder in
// jalium_vello_encode.h. Keep every struct/constant in lockstep with both.

#ifndef VELLO_SHARED_HLSLI
#define VELLO_SHARED_HLSLI

// ============================================================================
// config.wgsl — Config uniform + tile geometry
// ============================================================================

// Must match VelloConfig in jalium_vello_encode.h (and upstream ConfigUniform).
cbuffer VelloConfig : register(b0)
{
    uint width_in_tiles;
    uint height_in_tiles;

    uint target_width;
    uint target_height;

    // The initial color applied to the pixels in a tile during the fine stage.
    // The format is packed RGBA8 in MSB order.
    uint base_color;

    uint n_drawobj;
    uint n_path;
    uint n_clip;

    // To reduce the number of bindings, info and bin data are combined
    // into one buffer.
    uint bin_data_start;

    // Offsets within the scene buffer (in u32 units)
    uint pathtag_base;
    uint pathdata_base;

    uint drawtag_base;
    uint drawdata_base;

    uint transform_base;
    uint style_base;

    // Sizes of bump allocated buffers (in element size units)
    uint lines_size;
    uint binning_size;
    uint tiles_size;
    uint seg_counts_size;
    uint segments_size;
    uint blend_size;
    uint ptcl_size;
};

#define TILE_WIDTH  16u
#define TILE_HEIGHT 16u
// Number of tiles per bin
#define N_TILE_X 16u
#define N_TILE_Y 16u
#define N_TILE   256u  // N_TILE_X * N_TILE_Y

// Not currently supporting non-square tiles
#define TILE_SCALE 0.0625

// The "split" point between using local memory in fine for the blend stack and
// spilling to the blend_spill buffer.
#define BLEND_STACK_SPLIT 4u

// Radial gradient kinds (computed in draw_leaf, consumed by fine)
#define RAD_GRAD_KIND_CIRCULAR        1u
#define RAD_GRAD_KIND_STRIP           2u
#define RAD_GRAD_KIND_FOCAL_ON_CIRCLE 3u
#define RAD_GRAD_KIND_CONE            4u

// Radial gradient flags
#define RAD_GRAD_SWAPPED 1u

// ============================================================================
// pathtag.wgsl — path tag stream decoding
// ============================================================================

struct TagMonoid
{
    uint trans_ix;
    uint pathseg_ix;
    uint pathseg_offset;
    uint style_ix;
    uint path_ix;
};

#define PATH_TAG_SEG_TYPE    3u
#define PATH_TAG_LINETO      1u
#define PATH_TAG_QUADTO      2u
#define PATH_TAG_CUBICTO     3u
#define PATH_TAG_F32         8u
#define PATH_TAG_TRANSFORM   0x20u
#define PATH_TAG_PATH        0x10u
#define PATH_TAG_STYLE       0x40u
#define PATH_TAG_SUBPATH_END 4u

// Size of the `Style` data structure in words
#define STYLE_SIZE_IN_WORDS 2u

#define STYLE_FLAGS_STYLE      0x80000000u
#define STYLE_FLAGS_FILL       0x40000000u
#define STYLE_MITER_LIMIT_MASK 0xFFFFu

#define STYLE_FLAGS_START_CAP_MASK 0x0C000000u
#define STYLE_FLAGS_END_CAP_MASK   0x03000000u

#define STYLE_FLAGS_CAP_BUTT   0u
#define STYLE_FLAGS_CAP_SQUARE 0x01000000u
#define STYLE_FLAGS_CAP_ROUND  0x02000000u

#define STYLE_FLAGS_JOIN_MASK  0x30000000u
#define STYLE_FLAGS_JOIN_BEVEL 0u
#define STYLE_FLAGS_JOIN_MITER 0x10000000u
#define STYLE_FLAGS_JOIN_ROUND 0x20000000u

TagMonoid tag_monoid_identity()
{
    TagMonoid m = (TagMonoid)0;
    return m;
}

TagMonoid combine_tag_monoid(TagMonoid a, TagMonoid b)
{
    TagMonoid c;
    c.trans_ix = a.trans_ix + b.trans_ix;
    c.pathseg_ix = a.pathseg_ix + b.pathseg_ix;
    c.pathseg_offset = a.pathseg_offset + b.pathseg_offset;
    c.style_ix = a.style_ix + b.style_ix;
    c.path_ix = a.path_ix + b.path_ix;
    return c;
}

TagMonoid reduce_tag(uint tag_word)
{
    TagMonoid c;
    uint point_count = tag_word & 0x3030303u;
    c.pathseg_ix = countbits((point_count * 7u) & 0x4040404u);
    c.trans_ix = countbits(tag_word & (PATH_TAG_TRANSFORM * 0x1010101u));
    uint n_points = point_count + ((tag_word >> 2u) & 0x1010101u);
    uint a = n_points + (n_points & (((tag_word >> 3u) & 0x1010101u) * 15u));
    a += a >> 8u;
    a += a >> 16u;
    c.pathseg_offset = a & 0xffu;
    c.path_ix = countbits(tag_word & (PATH_TAG_PATH * 0x1010101u));
    c.style_ix = countbits(tag_word & (PATH_TAG_STYLE * 0x1010101u)) * STYLE_SIZE_IN_WORDS;
    return c;
}

// ============================================================================
// drawtag.wgsl — draw object stream decoding
// ============================================================================

struct DrawMonoid
{
    // The number of paths preceding this draw object.
    uint path_ix;
    // The number of clip operations preceding this draw object.
    uint clip_ix;
    // The offset of the encoded draw object in the scene (u32s).
    uint scene_offset;
    // The offset of the associated info.
    uint info_offset;
};

#define DRAWTAG_NOP                  0u
#define DRAWTAG_FILL_COLOR           0x44u
#define DRAWTAG_FILL_LIN_GRADIENT    0x114u
#define DRAWTAG_FILL_RAD_GRADIENT    0x29cu
#define DRAWTAG_FILL_SWEEP_GRADIENT  0x254u
#define DRAWTAG_FILL_IMAGE           0x28Cu
#define DRAWTAG_BLURRED_ROUNDED_RECT 0x2d4u
#define DRAWTAG_BEGIN_CLIP           0x49u
#define DRAWTAG_END_CLIP             0x21u

// First word of each draw info stream entry contains the flags.
// 0 represents a non-zero fill. 1 represents an even-odd fill.
#define DRAW_INFO_FLAGS_FILL_RULE_BIT 1u

DrawMonoid draw_monoid_identity()
{
    DrawMonoid m = (DrawMonoid)0;
    return m;
}

DrawMonoid combine_draw_monoid(DrawMonoid a, DrawMonoid b)
{
    DrawMonoid c;
    c.path_ix = a.path_ix + b.path_ix;
    c.clip_ix = a.clip_ix + b.clip_ix;
    c.scene_offset = a.scene_offset + b.scene_offset;
    c.info_offset = a.info_offset + b.info_offset;
    return c;
}

DrawMonoid map_draw_tag(uint tag_word)
{
    DrawMonoid c;
    c.path_ix = (tag_word != DRAWTAG_NOP) ? 1u : 0u;
    c.clip_ix = tag_word & 1u;
    c.scene_offset = (tag_word >> 2u) & 0x07u;
    c.info_offset = (tag_word >> 6u) & 0x0fu;
    return c;
}

// ============================================================================
// ptcl.wgsl — per-tile command list layout
// ============================================================================

// Initial allocation, in u32's.
#define PTCL_INITIAL_ALLOC 64u
#define PTCL_INCREMENT     256u

// Amount of space taken by jump
#define PTCL_HEADROOM 2u

// Tags for PTCL commands
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

// PTCL command payload word counts (excluding the opcode word), for reference:
//   CMD_FILL       3  (size_and_rule, seg_data, backdrop)
//   CMD_SOLID      0
//   CMD_COLOR      1  (rgba_color)
//   CMD_LIN_GRAD   5  (index, extend_mode, line_x, line_y, line_c)
//   CMD_RAD_GRAD  11  (index, extend_mode, matrx x4, xlat x2, focal_x, radius, kind, flags)
//   CMD_SWEEP_GRAD 9  (index, extend_mode, matrx x4, xlat x2, t0, t1)
//   CMD_IMAGE     15  (matrx x4, xlat x2, atlas_offset x2, extents x2, format,
//                      x_extend_mode, y_extend_mode, quality, alpha, alpha_type — packed as 15 words)
//   CMD_BEGIN_CLIP 0
//   CMD_END_CLIP   2  (blend, alpha)
//   CMD_JUMP       1  (new_ix)
//   CMD_BLUR_RECT 11  (rgba_color, matrx x4, xlat x2, width, height, radius, std_dev)

// ============================================================================
// tile.wgsl — path/tile intermediate info
// ============================================================================

struct VelloPath
{
    // bounding box in tiles
    uint4 bbox;
    // offset (in u32's) to tile rectangle
    uint tiles;
};

struct VelloTile
{
    int backdrop;
    // Count of segments in the tile up to coarse rasterization, and the
    // (inverted) segment index afterwards.
    uint segment_count_or_ix;
};

// ============================================================================
// segment.wgsl — line soup and tile segments
// ============================================================================

// Segments laid out for contiguous storage
struct Segment
{
    // Points are relative to tile origin
    float2 point0;
    float2 point1;
    float y_edge;
    float pad;
};

// A line segment produced by flattening and ready for rasterization.
struct LineSoup
{
    uint path_ix;
    float2 p0;
    float2 p1;
    uint pad;
};

// An intermediate data structure for sorting tile segments.
struct SegmentCount
{
    // Reference to element of LineSoup array
    uint line_ix;
    // Two count values packed into a single u32
    // Lower 16 bits: index of segment within line
    // Upper 16 bits: index of segment within segment slice
    uint counts;
};

// ============================================================================
// bump.wgsl — bump allocators
// ============================================================================

// Bitflags for each stage that can fail allocation.
#define STAGE_BINNING    0x1u
#define STAGE_TILE_ALLOC 0x2u
#define STAGE_FLATTEN    0x4u
#define STAGE_PATH_COUNT 0x8u
#define STAGE_COARSE     0x10u

// BumpAllocators layout, accessed as a RWByteAddressBuffer with these offsets.
// Must be kept in sync with VelloBumpAllocators in jalium_vello_encode.h.
#define BUMP_FAILED      0u
#define BUMP_BINNING     4u
#define BUMP_PTCL        8u
#define BUMP_TILE       12u
#define BUMP_SEG_COUNTS 16u
#define BUMP_SEGMENTS   20u
#define BUMP_BLEND      24u
#define BUMP_LINES      28u
#define BUMP_SIZE       32u

// IndirectCount: {count_x, count_y, count_z} at offsets 0/4/8.

// ============================================================================
// clip.wgsl — clip stack structures
// ============================================================================

struct Bic
{
    uint a;
    uint b;
};

Bic bic_combine(Bic x, Bic y)
{
    uint m = min(x.b, y.a);
    Bic r;
    r.a = x.a + y.a - m;
    r.b = x.b + y.b - m;
    return r;
}

struct ClipInp
{
    // Index of the draw object.
    uint ix;
    // Packed encoding of an enum with the sign bit as the tag. If positive,
    // this entry is a BeginClip and contains the associated path index. If
    // negative, it is an EndClip and contains the bitwise-not of the EndClip
    // draw object index.
    int path_ix;
};

struct ClipEl
{
    uint parent_ix;
    float4 bbox;
};

// ============================================================================
// bbox.wgsl — annotated path bounding box
// ============================================================================

struct PathBbox
{
    int x0;
    int y0;
    int x1;
    int y1;
    uint draw_flags;
    uint trans_ix;
};

float4 bbox_intersect(float4 a, float4 b)
{
    return float4(max(a.xy, b.xy), min(a.zw, b.zw));
}

// ============================================================================
// transform.wgsl — affine transform helpers
// ============================================================================

struct Transform
{
    float4 matrx;
    float2 translate;
};

float2 transform_apply(Transform transform, float2 p)
{
    return transform.matrx.xy * p.x + transform.matrx.zw * p.y + transform.translate;
}

Transform transform_inverse(Transform transform)
{
    float inv_det = 1.0 / (transform.matrx.x * transform.matrx.w - transform.matrx.y * transform.matrx.z);
    float4 inv_mat = inv_det * float4(transform.matrx.w, -transform.matrx.y, -transform.matrx.z, transform.matrx.x);
    // Column-vector convention, matching transform_apply.
    float2 inv_tr = inv_mat.xy * -transform.translate.x + inv_mat.zw * -transform.translate.y;
    Transform r;
    r.matrx = inv_mat;
    r.translate = inv_tr;
    return r;
}

Transform transform_mul(Transform a, Transform b)
{
    Transform r;
    r.matrx = a.matrx.xyxy * b.matrx.xxzz + a.matrx.zwzw * b.matrx.yyww;
    r.translate = a.matrx.xy * b.translate.x + a.matrx.zw * b.translate.y + a.translate;
    return r;
}

// ============================================================================
// Extend modes (peniko::Extend) — consumed by fine for gradients and images
// ============================================================================

#define EXTEND_PAD     0u
#define EXTEND_REPEAT  1u
#define EXTEND_REFLECT 2u

// Gradient ramp texture width (must match jalium_vello_encode.h kVelloRampWidth)
#define GRAD_RAMP_WIDTH 512u

// ============================================================================
// Numerical robustness constants (path_count / path_tiling)
// ============================================================================

#define ONE_MINUS_ULP  0.99999994
#define ROBUST_EPSILON 2e-7

// ============================================================================
// Helpers
// ============================================================================

uint pack_u16(uint lo, uint hi)
{
    return (lo & 0xFFFFu) | (hi << 16u);
}

uint unpack_lo16(uint v)
{
    return v & 0xFFFFu;
}

uint unpack_hi16(uint v)
{
    return v >> 16u;
}

#endif // VELLO_SHARED_HLSLI
