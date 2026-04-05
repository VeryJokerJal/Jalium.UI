#pragma once

#include "d3d12_backend.h"
#include <vector>
#include <cstdint>
#include <cmath>
#include <algorithm>
#include <mutex>

namespace jalium {

// Forward declarations for brush types
class Brush;

/// Shared shader blob cache — avoids recompiling Vello shaders per-window.
/// Owned by D3D12DirectRenderer, shared across D3D12VelloRenderer instances.
struct ShaderBlobCache {
    std::mutex mutex;
    bool velloCompiled = false;
    ComPtr<ID3DBlob> velloFineCS;
};

// ============================================================================
// Vello-style GPU compute path renderer
//
// Pipeline stages:
//   1. Path Encode  (CPU) — encode path segments into GPU buffers
//   2. Flatten      (CS)  — bezier curves → line segments (Wang's formula)
//   3. Bin + Alloc  (CS)  — assign segments to 16×16 tiles, allocate tile storage
//   4. Backdrop     (CS)  — prefix-sum winding number propagation across tile rows
//   5. Coarse       (CS)  — generate per-tile command lists (PTCL)
//   6. Fine         (CS)  — render final pixels with analytical AA coverage
// ============================================================================

// --- GPU-side data structures (must match HLSL) ---

static constexpr uint32_t kTileWidth  = 16;
static constexpr uint32_t kTileHeight = 16;

// Path segment tags (matches CPU-side encoding)
static constexpr uint32_t kSegTagLineTo  = 0;
static constexpr uint32_t kSegTagQuadTo  = 1;
static constexpr uint32_t kSegTagCubicTo = 2;

// Fill rules
static constexpr uint32_t kFillRuleEvenOdd = 0;
static constexpr uint32_t kFillRuleNonZero = 1;

// Brush types for PTCL commands
static constexpr uint32_t kBrushSolid          = 0;
static constexpr uint32_t kBrushLinearGradient  = 1;
static constexpr uint32_t kBrushRadialGradient  = 2;
static constexpr uint32_t kBrushSweepGradient   = 3;
static constexpr uint32_t kBrushImage           = 4;

// Gradient extend modes
static constexpr uint32_t kExtendPad     = 0;  // clamp to edge colors
static constexpr uint32_t kExtendRepeat  = 1;  // tile/repeat
static constexpr uint32_t kExtendReflect = 2;  // mirror/reflect

// Gradient ramp width (entries per gradient)
static constexpr uint32_t kGradientRampWidth = 256;
// Maximum gradients per frame
static constexpr uint32_t kMaxGradients = 64;

// A single path segment uploaded to GPU (48 bytes, matches HLSL)
struct PathSegment {
    float p0x, p0y;     // start point
    float p1x, p1y;     // control point 1 / end point (line)
    float p2x, p2y;     // control point 2 (cubic) / end point (quad)
    float p3x, p3y;     // end point (cubic)
    uint32_t tag;        // 0=line, 1=quad, 2=cubic
    uint32_t pathIndex;  // which path this segment belongs to
    uint32_t pad0, pad1;
};
static_assert(sizeof(PathSegment) == 48, "PathSegment must be 48 bytes");

// A flattened line segment produced by the flatten shader (24 bytes)
struct LineSeg {
    float p0x, p0y;
    float p1x, p1y;
    uint32_t pathIndex;
    uint32_t pad;
};
static_assert(sizeof(LineSeg) == 24, "LineSeg must be 24 bytes");

// Sorted segment for GPU fine shader (20 bytes, matches HLSL Segment struct)
struct VelloSortedSeg {
    float p0x, p0y;   // point0
    float p1x, p1y;   // point1
    float y_edge;      // y-edge for winding contribution (1e9 = no edge)
};
static_assert(sizeof(VelloSortedSeg) == 20, "VelloSortedSeg must be 20 bytes (matches HLSL Segment)");

// Draw tag types — encodes per-path draw order for GPU coarse shader
static constexpr uint32_t kDrawTagFill      = 0;  // regular fill path
static constexpr uint32_t kDrawTagBeginClip = 1;  // begin clip region
static constexpr uint32_t kDrawTagEndClip   = 2;  // end clip region
static constexpr uint32_t kDrawTagBlurRect  = 3;  // blur rect primitive

// Draw tag entry (16 bytes) — uploaded to GPU for coarse shader draw ordering
struct DrawTag {
    uint32_t tag;          // kDrawTagFill/BeginClip/EndClip/BlurRect
    uint32_t pathIdx;      // index into PathInfo/PathDraw arrays
    uint32_t blendMode;    // for EndClip: Porter-Duff blend mode
    float    alpha;        // for EndClip: clip alpha
};
static_assert(sizeof(DrawTag) == 16, "DrawTag must be 16 bytes");

// Per-path metadata (32 bytes)
struct PathInfo {
    uint32_t segOffset;    // offset into PathSegment buffer
    uint32_t segCount;     // number of segments in this path
    uint32_t fillRule;     // 0=EvenOdd, 1=NonZero
    uint32_t tileOffset;   // offset into per-path tile array
    uint32_t tileBboxX;    // tile-level bbox (in tiles, not pixels)
    uint32_t tileBboxY;
    uint32_t tileBboxW;    // width in tiles
    uint32_t tileBboxH;    // height in tiles
};
static_assert(sizeof(PathInfo) == 32, "PathInfo must be 32 bytes");

// Per-path draw info (64 bytes, supports solid color + gradients)
struct PathDraw {
    float colorR, colorG, colorB, colorA;              // premultiplied solid color
    float bboxMinX, bboxMinY, bboxMaxX, bboxMaxY;      // pixel-space bounds
    uint32_t brushType;      // 0=solid, 1=linear, 2=radial, 3=sweep
    uint32_t gradientIndex;  // index into gradient ramp array
    float gradParam0;        // linear: p0.x  | radial: center.x | sweep: center.x
    float gradParam1;        // linear: p0.y  | radial: center.y | sweep: center.y
    float gradParam2;        // linear: p1.x  | radial: radius.x | sweep: t0 (start angle normalized)
    float gradParam3;        // linear: p1.y  | radial: radius.y | sweep: t1 (end angle normalized)
    float gradParam4;        // radial: origin.x (also stores extendMode as float-encoded uint for all)
    float gradParam5;        // radial: origin.y
    // NOTE: extendMode is packed into gradParam4 for linear (since p4 was unused),
    //       and into gradParam5's upper bits for radial/sweep via SetExtendMode().
    //       In PTCL, extend mode is passed as a separate uint after gradient params.
};
static_assert(sizeof(PathDraw) == 64, "PathDraw must be 64 bytes");

// ============================================================================
// Vello GPU Pipeline — Data structures
// ============================================================================

// CPU pipeline fine shader constants (32 bytes, matches kVelloFineCS cbuffer)
// CPU pipeline constants — must match the first fields of cbuffer VelloConfig in fine shader
struct VelloConstants {
    uint32_t widthTiles;     // width_in_tiles
    uint32_t heightTiles;    // height_in_tiles
    uint32_t widthPixels;    // target_width
    uint32_t heightPixels;   // target_height
    uint32_t baseColor;      // base_color — 0 = transparent (premultiplied RGBA8)
    uint32_t numPaths;       // n_drawobj (not used by CPU fine, but keeps layout correct)
    uint32_t numSegments;    // n_path
    uint32_t pad0;           // n_clip
};
static_assert(sizeof(VelloConstants) == 32, "VelloConstants must be 32 bytes");

// GPU pipeline config (96 bytes, matches HLSL cbuffer VelloConfig)
struct VelloConfig {
    uint32_t width_in_tiles;
    uint32_t height_in_tiles;
    uint32_t target_width;
    uint32_t target_height;
    uint32_t base_color;       // packed RGBA8
    uint32_t n_drawobj;
    uint32_t n_path;
    uint32_t n_clip;
    uint32_t bin_data_start;
    uint32_t lines_size;
    uint32_t binning_size;
    uint32_t tiles_size;
    uint32_t seg_counts_size;
    uint32_t segments_size;
    uint32_t blend_size;
    uint32_t ptcl_size;
    uint32_t num_segments;     // total PathSegment count
    uint32_t pad_[7];
};
static_assert(sizeof(VelloConfig) == 96, "VelloConfig must be 96 bytes");

// BumpAllocators: GPU atomic counters for dynamic buffer allocation (32 bytes)
struct BumpAllocators {
    uint32_t failed;       // bitmask of failed stages
    uint32_t binning;      // bin_data write offset
    uint32_t ptcl;         // PTCL dynamic allocation offset
    uint32_t tile;         // tile allocation offset
    uint32_t seg_counts;   // SegmentCount allocation offset
    uint32_t segments;     // final Segment allocation offset
    uint32_t blend;        // blend stack spill offset
    uint32_t lines;        // LineSoup allocation offset
};
static_assert(sizeof(BumpAllocators) == 32, "BumpAllocators must be 32 bytes");

// VelloPath: per-draw-object path info for GPU pipeline (20 bytes)
struct VelloPath {
    uint32_t bbox[4];      // tile-space bounding box (x0, y0, x1, y1)
    uint32_t tiles;        // offset into VelloTile array
};
static_assert(sizeof(VelloPath) == 20, "VelloPath must be 20 bytes");

// VelloTile: per-tile info (8 bytes)
struct VelloTile {
    int32_t  backdrop;
    uint32_t segment_count_or_ix;
};
static_assert(sizeof(VelloTile) == 8, "VelloTile must be 8 bytes");

// LineSoup: flattened line segment for GPU pipeline (24 bytes)
struct LineSoup {
    uint32_t path_ix;
    float p0x, p0y;
    float p1x, p1y;
    uint32_t pad;
};
static_assert(sizeof(LineSoup) == 24, "LineSoup must be 24 bytes");

// VelloSegment: final tile-relative segment for fine shader (20 bytes)
struct VelloSegment {
    float point0x, point0y;
    float point1x, point1y;
    float y_edge;
};
static_assert(sizeof(VelloSegment) == 20, "VelloSegment must be 20 bytes");

// VelloSegmentCount: intermediate for path_count/path_tiling (8 bytes)
struct VelloSegmentCount {
    uint32_t line_ix;
    uint32_t counts;       // packed: low16=seg_in_line, hi16=seg_in_slice
};
static_assert(sizeof(VelloSegmentCount) == 8, "VelloSegmentCount must be 8 bytes");

// VelloBinHeader: per-bin metadata (8 bytes)
struct VelloBinHeader {
    uint32_t element_count;
    uint32_t chunk_offset;
};
static_assert(sizeof(VelloBinHeader) == 8, "VelloBinHeader must be 8 bytes");

// VelloPathBbox: atomic path bounding box from flatten (24 bytes)
struct VelloPathBbox {
    int32_t x0, y0, x1, y1;
    uint32_t draw_flags;
    uint32_t trans_ix;
};
static_assert(sizeof(VelloPathBbox) == 24, "VelloPathBbox must be 24 bytes");

// VelloDrawMonoid: per-draw-object prefix sum (16 bytes)
struct VelloDrawMonoid {
    uint32_t path_ix;
    uint32_t clip_ix;
    uint32_t scene_offset;
    uint32_t info_offset;
};
static_assert(sizeof(VelloDrawMonoid) == 16, "VelloDrawMonoid must be 16 bytes");

// Bump allocator failure stage flags
static constexpr uint32_t kStageBindng     = 0x1;
static constexpr uint32_t kStageTileAlloc  = 0x2;
static constexpr uint32_t kStageFlatten    = 0x4;
static constexpr uint32_t kStagePathCount  = 0x8;
static constexpr uint32_t kStageCoarse     = 0x10;

// PTCL constants
static constexpr uint32_t kPtclInitialAlloc = 64;
static constexpr uint32_t kPtclIncrement    = 256;

// PTCL command opcodes — must match CMD_* in kVelloFineCS (vello_fine.cs.hlsl)
static constexpr uint32_t kPtclEnd        = 0;   // CMD_END
static constexpr uint32_t kPtclFill       = 1;   // CMD_FILL:       size_and_rule, seg_base, backdrop
static constexpr uint32_t kPtclSolid      = 3;   // CMD_SOLID:      (no payload, sets area=1.0)
static constexpr uint32_t kPtclColor      = 5;   // CMD_COLOR:      packed_rgba
static constexpr uint32_t kPtclLinGrad    = 6;   // CMD_LIN_GRAD:   gradIdx, p0x, p0y, p1x, p1y, ext, pad, pad
static constexpr uint32_t kPtclRadGrad    = 7;   // CMD_RAD_GRAD:   gradIdx, cx, cy, rx, ry, ox, oy, ext
static constexpr uint32_t kPtclSweepGrad  = 8;   // CMD_SWEEP_GRAD: gradIdx, cx, cy, t0, t1, ext, pad, pad
static constexpr uint32_t kPtclImage      = 9;   // CMD_IMAGE:      atlasIdx, u0, v0, u1, v1, opacity
static constexpr uint32_t kPtclBeginClip  = 10;  // CMD_BEGIN_CLIP:  (no payload)
static constexpr uint32_t kPtclEndClip    = 11;  // CMD_END_CLIP:   blendMode, alpha
static constexpr uint32_t kPtclJump       = 12;  // CMD_JUMP:       target_cmd_ix
static constexpr uint32_t kPtclBlurRect   = 13;  // CMD_BLUR_RECT:  info_offset, packed_rgba
static constexpr uint32_t kPtclSaveBackdrop = 14; // (custom, no shader CMD) — save backdrop for clip
static constexpr uint32_t kPtclMaskClip   = 15;  // (custom, no shader CMD) — maskIdx, x, y, w, h

// Porter-Duff compose modes (matches Vello encoding)
static constexpr uint32_t kBlendSrcOver    = 0;   // default: Cs + Cb*(1-αs)
static constexpr uint32_t kBlendSrcIn      = 1;   // Cs * αb
static constexpr uint32_t kBlendSrcOut     = 2;   // Cs * (1-αb)
static constexpr uint32_t kBlendSrcAtop    = 3;   // Cs*αb + Cb*(1-αs)
static constexpr uint32_t kBlendDestOver   = 4;   // Cb + Cs*(1-αb)
static constexpr uint32_t kBlendDestIn     = 5;   // Cb * αs
static constexpr uint32_t kBlendDestOut    = 6;   // Cb * (1-αs)
static constexpr uint32_t kBlendDestAtop   = 7;   // Cb*αs + Cs*(1-αb)
static constexpr uint32_t kBlendXor        = 8;   // Cs*(1-αb) + Cb*(1-αs)
static constexpr uint32_t kBlendPlus       = 9;   // Cs + Cb (clamped)
static constexpr uint32_t kBlendCopy       = 10;  // Cs (replace)
static constexpr uint32_t kBlendNormal     = 11;  // same as SrcOver (default mix mode)
static constexpr uint32_t kBlendMultiply   = 12;  // Cs * Cb
static constexpr uint32_t kBlendScreen     = 13;  // Cs + Cb - Cs*Cb
static constexpr uint32_t kBlendOverlay    = 14;  // hard_light(Cb, Cs)
static constexpr uint32_t kBlendDarken     = 15;  // min(Cs, Cb)
static constexpr uint32_t kBlendLighten    = 16;  // max(Cs, Cb)
static constexpr uint32_t kBlendColorDodge = 17;  // Cb / (1-Cs)
static constexpr uint32_t kBlendColorBurn  = 18;  // 1 - (1-Cb)/Cs
static constexpr uint32_t kBlendHardLight  = 19;  // overlay with swapped args
static constexpr uint32_t kBlendSoftLight  = 20;  // W3C soft-light
static constexpr uint32_t kBlendDifference = 21;  // |Cs - Cb|
static constexpr uint32_t kBlendExclusion  = 22;  // Cs + Cb - 2*Cs*Cb
static constexpr uint32_t kBlendHue        = 23;  // Hue(Cs) + Sat(Cb) + Lum(Cb)
static constexpr uint32_t kBlendSaturation = 24;  // Sat(Cs) applied to Cb
static constexpr uint32_t kBlendColorHsl   = 25;  // HS from Cs, L from Cb
static constexpr uint32_t kBlendLuminosity  = 26;  // L from Cs, HS from Cb

// Max clip depth supported in fine shader
static constexpr uint32_t kMaxClipDepth = 16;

// ============================================================================
// Vello Filter Graph — multi-primitive filter pipeline (W3C filter effects)
// ============================================================================

/// Filter primitive types (matches Vello's FilterPrimitive enum)
enum class VelloFilterType : uint32_t {
    GaussianBlur,   // std_deviation, edgeMode
    DropShadow,     // dx, dy, std_deviation, color, edgeMode
    ColorMatrix,    // 4x5 matrix
    Offset,         // dx, dy
    Morphology,     // radiusX, radiusY, isDilate
    Flood,          // color (r,g,b,a)
    Brightness,     // amount
    Contrast,       // amount
    Grayscale,      // amount
    HueRotate,      // angle (degrees)
    Invert,         // amount
    Opacity,        // amount
    Saturate,       // amount
    Sepia,          // amount
};

/// Edge mode for filter boundary sampling
enum class VelloEdgeMode : uint32_t {
    None = 0,       // transparent black (default)
    Duplicate,      // clamp to edge
    Wrap,           // repeat/tile
    Mirror,         // mirror
};

/// A single filter primitive with parameters
struct VelloFilterPrimitive {
    VelloFilterType type;
    float params[24];   // up to 24 float parameters (varies by type)
    uint32_t paramCount;
};

/// A filter graph: ordered sequence of primitives forming a filter pipeline
struct VelloFilterGraph {
    std::vector<VelloFilterPrimitive> primitives;

    void Clear() { primitives.clear(); }
    bool IsEmpty() const { return primitives.empty(); }

    void AddBlur(float stdDev, VelloEdgeMode edge = VelloEdgeMode::None) {
        VelloFilterPrimitive p; p.type = VelloFilterType::GaussianBlur;
        p.params[0] = stdDev; p.params[1] = (float)(uint32_t)edge; p.paramCount = 2;
        primitives.push_back(p);
    }
    void AddDropShadow(float dx, float dy, float stdDev, float r, float g, float b, float a,
                       VelloEdgeMode edge = VelloEdgeMode::None) {
        VelloFilterPrimitive p; p.type = VelloFilterType::DropShadow;
        p.params[0]=dx; p.params[1]=dy; p.params[2]=stdDev;
        p.params[3]=r; p.params[4]=g; p.params[5]=b; p.params[6]=a;
        p.params[7]=(float)(uint32_t)edge; p.paramCount = 8;
        primitives.push_back(p);
    }
    void AddColorMatrix(const float* matrix20) {
        VelloFilterPrimitive p; p.type = VelloFilterType::ColorMatrix;
        memcpy(p.params, matrix20, 20 * sizeof(float)); p.paramCount = 20;
        primitives.push_back(p);
    }
    void AddOffset(float dx, float dy) {
        VelloFilterPrimitive p; p.type = VelloFilterType::Offset;
        p.params[0]=dx; p.params[1]=dy; p.paramCount = 2;
        primitives.push_back(p);
    }
    void AddMorphology(float rx, float ry, bool isDilate) {
        VelloFilterPrimitive p; p.type = VelloFilterType::Morphology;
        p.params[0]=rx; p.params[1]=ry; p.params[2]=isDilate?1.0f:0.0f; p.paramCount = 3;
        primitives.push_back(p);
    }
    void AddBrightness(float amount) {
        VelloFilterPrimitive p; p.type = VelloFilterType::Brightness;
        p.params[0]=amount; p.paramCount = 1; primitives.push_back(p);
    }
    void AddContrast(float amount) {
        VelloFilterPrimitive p; p.type = VelloFilterType::Contrast;
        p.params[0]=amount; p.paramCount = 1; primitives.push_back(p);
    }
    void AddGrayscale(float amount) {
        VelloFilterPrimitive p; p.type = VelloFilterType::Grayscale;
        p.params[0]=amount; p.paramCount = 1; primitives.push_back(p);
    }
    void AddHueRotate(float degrees) {
        VelloFilterPrimitive p; p.type = VelloFilterType::HueRotate;
        p.params[0]=degrees; p.paramCount = 1; primitives.push_back(p);
    }
    void AddInvert(float amount) {
        VelloFilterPrimitive p; p.type = VelloFilterType::Invert;
        p.params[0]=amount; p.paramCount = 1; primitives.push_back(p);
    }
    void AddOpacity(float amount) {
        VelloFilterPrimitive p; p.type = VelloFilterType::Opacity;
        p.params[0]=amount; p.paramCount = 1; primitives.push_back(p);
    }
    void AddSaturate(float amount) {
        VelloFilterPrimitive p; p.type = VelloFilterType::Saturate;
        p.params[0]=amount; p.paramCount = 1; primitives.push_back(p);
    }
    void AddSepia(float amount) {
        VelloFilterPrimitive p; p.type = VelloFilterType::Sepia;
        p.params[0]=amount; p.paramCount = 1; primitives.push_back(p);
    }
};

/// Render graph node types (matches Vello's RenderNodeKind)
enum class VelloRenderNodeKind : uint32_t {
    RootLayer,      // base compositing target
    FilterLayer,    // layer with filter effects
};

/// A render graph node
struct VelloRenderNode {
    uint32_t id;
    VelloRenderNodeKind kind;
    uint32_t layerId;
    VelloFilterGraph filterGraph;  // only for FilterLayer
};

/// Render graph edge (dependency)
struct VelloRenderEdge {
    uint32_t from, to;
    uint32_t layerId;   // connecting layer
};

/// Render graph for multi-pass filter rendering (Vello-style DAG)
struct VelloRenderGraph {
    std::vector<VelloRenderNode> nodes;
    std::vector<VelloRenderEdge> edges;
    std::vector<uint32_t> executionOrder;  // pre-computed topological order
    uint32_t nextNodeId = 0;
    uint32_t rootNodeId = UINT32_MAX;
    bool hasFilters = false;

    void Clear() {
        nodes.clear(); edges.clear(); executionOrder.clear();
        nextNodeId = 0; rootNodeId = UINT32_MAX; hasFilters = false;
    }

    uint32_t AddRootLayer(uint32_t layerId) {
        uint32_t id = nextNodeId++;
        rootNodeId = id;
        VelloRenderNode node; node.id = id; node.kind = VelloRenderNodeKind::RootLayer;
        node.layerId = layerId;
        nodes.push_back(std::move(node));
        return id;
    }

    uint32_t AddFilterLayer(uint32_t layerId, const VelloFilterGraph& filterGraph) {
        uint32_t id = nextNodeId++;
        hasFilters = true;
        VelloRenderNode node; node.id = id; node.kind = VelloRenderNodeKind::FilterLayer;
        node.layerId = layerId; node.filterGraph = filterGraph;
        nodes.push_back(std::move(node));
        return id;
    }

    void AddEdge(uint32_t from, uint32_t to, uint32_t layerId) {
        edges.push_back({from, to, layerId});
    }

    void RecordNodeForExecution(uint32_t nodeId) {
        executionOrder.push_back(nodeId);
    }
};

// ============================================================================
// Vello GPU Path Renderer
// ============================================================================

// ============================================================================
// Multi-Atlas Image Manager (Vello-style guillotine allocator)
// ============================================================================

/// An allocation within an atlas.
struct AtlasAllocation {
    uint32_t atlasId;
    uint32_t x, y, w, h;
    uint32_t allocId;  // internal handle for deallocation
};

/// Simple guillotine rectangle allocator for a single atlas texture.
class AtlasAllocator {
public:
    AtlasAllocator(uint32_t width, uint32_t height);

    /// Try to allocate a rectangle. Returns false if no space.
    bool Allocate(uint32_t w, uint32_t h, AtlasAllocation& out);
    /// Free a previously allocated rectangle.
    void Free(uint32_t allocId);
    /// Reset all allocations.
    void Reset();

    uint32_t GetWidth() const { return width_; }
    uint32_t GetHeight() const { return height_; }
    uint32_t GetAllocatedArea() const { return allocatedArea_; }

private:
    struct FreeRect { uint32_t x, y, w, h; };
    uint32_t width_, height_;
    std::vector<FreeRect> freeRects_;
    uint32_t nextAllocId_ = 0;
    uint32_t allocatedArea_ = 0;
};

/// Multi-atlas manager: manages multiple atlas textures for overflow.
class MultiAtlasManager {
public:
    MultiAtlasManager(uint32_t atlasWidth = 4096, uint32_t atlasHeight = 4096);

    /// Allocate space for an image. Creates new atlas if needed.
    AtlasAllocation Allocate(uint32_t w, uint32_t h);
    /// Free an allocation.
    void Free(const AtlasAllocation& alloc);
    /// Reset all atlases.
    void Reset();

    uint32_t GetAtlasCount() const { return (uint32_t)atlases_.size(); }

private:
    uint32_t atlasWidth_, atlasHeight_;
    struct AtlasEntry {
        uint32_t id;
        AtlasAllocator allocator;
    };
    std::vector<AtlasEntry> atlases_;
    uint32_t nextAtlasId_ = 0;
};

/// Cached geometry data for a path — avoids re-flattening on repeated draws.
/// Stores pre-computed line segments and path metadata.
struct VelloCachedPath {
    std::vector<LineSeg> lineSegs;       // pre-flattened line segments
    float bboxMinX, bboxMinY, bboxMaxX, bboxMaxY;
    uint32_t fillRule;
    bool valid = false;

    void Clear() { lineSegs.clear(); valid = false; }
};

class D3D12VelloRenderer {
public:
    explicit D3D12VelloRenderer(ID3D12Device* device, ShaderBlobCache* shaderCache = nullptr);
    ~D3D12VelloRenderer();

    bool Initialize();

    // --- Path encoding (CPU side) ---

    /// Begin a new frame of path encoding.  Clears all buffers.
    void BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight);

    /// Set scissor rect to clamp Vello path tile bboxes. Paths outside scissor are culled.
    void SetScissorRect(float left, float top, float right, float bottom) {
        scissorLeft_ = left; scissorTop_ = top;
        scissorRight_ = right; scissorBottom_ = bottom;
        hasScissor_ = true;
    }
    void ClearScissorRect() { hasScissor_ = false; }

    /// Encode a fill path with solid color.
    /// Returns false if path is too large for GPU (caller should fall back to CPU).
    bool EncodeFillPath(float startX, float startY,
                        const float* commands, uint32_t commandLength,
                        float r, float g, float b, float a,
                        uint32_t fillRule,
                        float m11 = 1, float m12 = 0, float m21 = 0,
                        float m22 = 1, float dx = 0, float dy = 0);

    /// Encode a fill path with any brush type (solid, linear gradient, radial gradient).
    /// Returns false if path is too large for GPU or brush type is unsupported.
    bool EncodeFillPathBrush(float startX, float startY,
                             const float* commands, uint32_t commandLength,
                             Brush* brush, uint32_t fillRule, float opacity,
                             float m11 = 1, float m12 = 0, float m21 = 0,
                             float m22 = 1, float dx = 0, float dy = 0);

    /// Encode a stroke path (expanded to fill on CPU for now).
    /// Returns false if path is too large for GPU.
    /// lineJoin: 0=Miter, 1=Bevel, 2=Round
    /// lineCap: 0=Flat, 1=Square, 2=Round
    /// dashPattern/dashCount: optional dash array (alternating on/off lengths)
    /// dashOffset: offset into the dash pattern
    bool EncodeStrokePath(float startX, float startY,
                              const float* commands, uint32_t commandLength,
                              float r, float g, float b, float a,
                              float strokeWidth, bool closed,
                              int32_t lineJoin, float miterLimit,
                              int32_t lineCap = 2,
                              const float* dashPattern = nullptr,
                              uint32_t dashCount = 0,
                              float dashOffset = 0,
                              float m11 = 1, float m12 = 0, float m21 = 0,
                              float m22 = 1, float dx = 0, float dy = 0);

    /// Encode a stroke path with any brush type.
    bool EncodeStrokePathBrush(float startX, float startY,
                               const float* commands, uint32_t commandLength,
                               Brush* brush, float strokeWidth, bool closed,
                               int32_t lineJoin, float miterLimit, float opacity,
                               int32_t lineCap = 2,
                               const float* dashPattern = nullptr,
                               uint32_t dashCount = 0,
                               float dashOffset = 0,
                               float m11 = 1, float m12 = 0, float m21 = 0,
                               float m22 = 1, float dx = 0, float dy = 0);

    // --- Clip operations ---

    /// Begin a clip region defined by a path. Content drawn between
    /// BeginClip and EndClip is masked by this path's fill area.
    /// Returns false if clip path is too large for GPU.
    bool EncodeBeginClip(float startX, float startY,
                         const float* commands, uint32_t commandLength,
                         uint32_t fillRule,
                         float m11 = 1, float m12 = 0, float m21 = 0,
                         float m22 = 1, float dx = 0, float dy = 0);

    /// Begin a rectangular clip region (optimized, no path encoding needed).
    void EncodeBeginClipRect(float x, float y, float w, float h);

    /// End the current clip region. Composites clipped content using blendMode.
    void EncodeEndClip(uint32_t blendMode = kBlendSrcOver, float alpha = 1.0f);

    /// Register an alpha mask texture for use with clip/layer compositing.
    /// Returns a mask index for use with EncodeBeginClipMask.
    uint32_t RegisterMask(ID3D12Resource* texture, uint32_t width, uint32_t height, bool isLuminance = false);

    /// Begin a clip region using a pre-rendered mask texture.
    /// maskIndex: returned by RegisterMask.
    void EncodeBeginClipMask(uint32_t maskIndex, float x, float y, float w, float h);

    /// Encode a fill path from a cached (pre-flattened) path — avoids re-flattening.
    /// The cached path stores pre-computed line segments from a previous flatten.
    bool EncodeFillPathCached(const VelloCachedPath& cached,
                              float r, float g, float b, float a,
                              float m11 = 1, float m12 = 0, float m21 = 0,
                              float m22 = 1, float dx = 0, float dy = 0);

    /// Pre-flatten a path and cache the result for repeated use.
    /// Returns false if the path is too large.
    bool CachePath(float startX, float startY,
                   const float* commands, uint32_t commandLength,
                   uint32_t fillRule, VelloCachedPath& outCache);

    /// Encode a blurred rounded rectangle (optimized GPU primitive for shadows).
    /// Draws a filled rounded rect with Gaussian blur applied analytically.
    void EncodeBlurRect(float x, float y, float w, float h,
                        float cornerRadius, float blurSigma,
                        float r, float g, float b, float a,
                        float m11 = 1, float m12 = 0, float m21 = 0,
                        float m22 = 1, float dx = 0, float dy = 0);

    /// Current clip depth.
    uint32_t GetClipDepth() const { return clipDepth_; }

    // --- Layer filter effects (Vello-style) ---

    /// Apply a Gaussian blur to the output texture (separable, multi-scale decimation).
    /// Uses Vello's decimated blur algorithm: downsample, blur at reduced size, upsample.
    /// Must be called after Dispatch() and before compositing.
    bool ApplyGaussianBlur(ID3D12GraphicsCommandList* cmdList,
                           float stdDeviation, uint32_t frameIndex = 0);

    /// Apply a drop shadow effect: offset + blur + color replacement.
    bool ApplyDropShadow(ID3D12GraphicsCommandList* cmdList,
                         float dx, float dy, float stdDeviation,
                         float r, float g, float b, float a,
                         uint32_t frameIndex = 0);

    /// Apply a 4x5 color matrix filter to the output texture.
    /// matrix: 20 floats in row-major order (R,G,B,A rows × R,G,B,A,offset cols).
    /// CSS filter functions map to specific matrices:
    ///   Brightness(v): diagonal(v,v,v,1) + offset(0,0,0,0)
    ///   Contrast(v):   diagonal(v,v,v,1) + offset((1-v)/2, ...)
    ///   Grayscale(v):  luminance matrix blend
    ///   HueRotate(deg): rotation in RGB space
    ///   Invert(v):     diagonal(1-2v,1-2v,1-2v,1) + offset(v,v,v,0)
    ///   Opacity(v):    diagonal(1,1,1,v)
    ///   Saturate(v):   saturation matrix
    ///   Sepia(v):      sepia tone matrix
    bool ApplyColorMatrix(ID3D12GraphicsCommandList* cmdList,
                          const float* matrix, uint32_t frameIndex = 0);

    /// Apply a pixel offset (shift) to the output texture.
    bool ApplyOffset(ID3D12GraphicsCommandList* cmdList,
                     float dx, float dy, uint32_t frameIndex = 0);

    /// Apply morphological operation (dilate or erode) to the output texture.
    /// radiusX/Y: kernel radius in pixels. isDilate: true=dilate, false=erode.
    bool ApplyMorphology(ID3D12GraphicsCommandList* cmdList,
                         float radiusX, float radiusY, bool isDilate,
                         uint32_t frameIndex = 0);

    // --- CSS Filter convenience methods ---
    bool ApplyBrightness(ID3D12GraphicsCommandList* cl, float amount, uint32_t fi = 0);
    bool ApplyContrast(ID3D12GraphicsCommandList* cl, float amount, uint32_t fi = 0);
    bool ApplyGrayscale(ID3D12GraphicsCommandList* cl, float amount, uint32_t fi = 0);
    bool ApplyHueRotate(ID3D12GraphicsCommandList* cl, float degrees, uint32_t fi = 0);
    bool ApplyInvert(ID3D12GraphicsCommandList* cl, float amount, uint32_t fi = 0);
    bool ApplyOpacityFilter(ID3D12GraphicsCommandList* cl, float amount, uint32_t fi = 0);
    bool ApplySaturate(ID3D12GraphicsCommandList* cl, float amount, uint32_t fi = 0);
    bool ApplySepia(ID3D12GraphicsCommandList* cl, float amount, uint32_t fi = 0);

    /// Execute a complete filter graph (chain of filter primitives) on the output texture.
    /// This is the main entry point for multi-primitive filter pipeline execution.
    bool ExecuteFilterGraph(ID3D12GraphicsCommandList* cmdList,
                            const VelloFilterGraph& graph, uint32_t frameIndex = 0);

    // --- COLR Color Font Rendering (Vello-style) ---

    /// Render a COLR color glyph using the Vello path rendering pipeline.
    /// Uses IDWriteFactory4::TranslateColorGlyphRun to enumerate color layers,
    /// then renders each layer via Vello's fill/clip/blend operations.
    /// Returns true if the glyph was a color glyph and was rendered.
    /// Returns false if the glyph is not a color glyph (caller should fall back to grayscale).
    bool RenderColorGlyph(IDWriteFontFace* fontFace, uint16_t glyphId,
                          float fontSize, float x, float y,
                          float r, float g, float b, float a,
                          float m11 = 1, float m12 = 0, float m21 = 0,
                          float m22 = 1, float dx = 0, float dy = 0);

    /// Execute a render graph: process all nodes in dependency order.
    /// Each FilterLayer node applies its filter graph to the output.
    bool ExecuteRenderGraph(ID3D12GraphicsCommandList* cmdList,
                            const VelloRenderGraph& graph, uint32_t frameIndex = 0);

    // --- Image brush ---

    /// Register a bitmap for use as image brush in path fills.
    /// Returns an atlas index for use with EncodeFillPathImage.
    /// The texture must remain valid until Dispatch completes.
    uint32_t RegisterImage(ID3D12Resource* texture, uint32_t width, uint32_t height);

    /// Encode a fill path with an image brush.
    bool EncodeFillPathImage(float startX, float startY,
                             const float* commands, uint32_t commandLength,
                             uint32_t imageIndex, uint32_t fillRule, float opacity,
                             float m11 = 1, float m12 = 0, float m21 = 0,
                             float m22 = 1, float dx = 0, float dy = 0);

    /// Upload encoded data to GPU and dispatch all compute stages.
    /// The output texture contains the rendered path pixels.
    /// frameIndex selects per-frame upload buffers to avoid GPU race conditions.
    /// Returns true if rendering succeeded (output texture has valid content).
    /// Uses GPU pipeline when gpuPipeline=true (Flatten→Bin→Backdrop→Coarse→Fine).
    /// Falls back to CPU pipeline when false (CPU flatten/bin/PTCL + GPU Fine).
    bool Dispatch(ID3D12GraphicsCommandList* cmdList, uint32_t frameIndex = 0);

    /// Enable/disable Vello GPU pipeline (full Vello architecture).
    void SetGPUPipeline(bool enable) { useGpuPipeline_ = enable; }
    bool IsGPUPipeline() const { return useGpuPipeline_; }

    /// Full Vello GPU pipeline dispatch: all stages run as GPU compute shaders.
    bool DispatchGPU(ID3D12GraphicsCommandList* cmdList, uint32_t frameIndex = 0);

    /// Get the output texture (RGBA8, viewport-sized) for compositing.
    ID3D12Resource* GetOutputTexture() const { return outputTexture_.Get(); }
    uint32_t GetOutputW() const { return viewportW_; }
    uint32_t GetOutputH() const { return viewportH_; }

    /// Force next Dispatch to create a new output texture (for z-group splitting).
    /// The old texture stays alive via ComPtr in BitmapBatchTexture.
    void ForceNewOutputTexture() { outputTexture_.Reset(); outputW_ = 0; outputH_ = 0; }

    /// Returns true if any paths were encoded this frame.
    bool HasWork() const { return !pathInfos_.empty(); }
    uint32_t GetPathCount() const { return (uint32_t)pathInfos_.size(); }

private:
    // --- Path encoding helpers ---
    void FlattenBezierToSegments(float p0x, float p0y, float p1x, float p1y,
                                 float p2x, float p2y, float p3x, float p3y,
                                 float tolerance);
    void FlattenQuadToSegments(float p0x, float p0y, float p1x, float p1y,
                               float p2x, float p2y, float tolerance);

    // --- GPU resource management ---
    bool CreateComputePipelines();
    bool CreateRootSignature();
    bool EnsureBuffers();
    bool EnsureOutputTexture(uint32_t w, uint32_t h);

    ID3D12Device* device_;
    bool initialized_ = false;
    bool useGpuPipeline_ = false;  // false = CPU flatten/bin/PTCL + GPU Fine (stable)
                                   // true = Vello GPU pipeline (full compute)

    // Viewport
    uint32_t viewportW_ = 0, viewportH_ = 0;
    uint32_t tilesX_ = 0, tilesY_ = 0;

    // Scissor rect (pixel coords, clamped to viewport)
    float scissorLeft_ = 0, scissorTop_ = 0, scissorRight_ = 0, scissorBottom_ = 0;
    bool hasScissor_ = false;

    // CPU-side staging buffers (populated by Encode*, uploaded in Dispatch)
    std::vector<PathSegment> segments_;
    std::vector<PathInfo>    pathInfos_;
    std::vector<PathDraw>    pathDraws_;
    std::vector<DrawTag>     drawTags_;     // per-draw-command ordering for GPU coarse

    // Gradient ramps: each gradient is a 256-entry RGBA8 color lookup table
    std::vector<uint32_t> gradientRamps_;  // N * kGradientRampWidth entries
    uint32_t gradientCount_ = 0;

    // Helper: build a gradient ramp from stops and return the gradient index.
    // colorSpace: 0=sRGB, 1=LinearSRGB, 2=OKLab
    uint32_t AddGradientRamp(const struct GradStop* stops, uint32_t stopCount,
                             uint32_t colorSpace = 0);

    // Helper: encode path geometry (shared between solid and brush versions)
    bool EncodePathGeometry(float startX, float startY,
                            const float* commands, uint32_t commandLength,
                            uint32_t fillRule,
                            float m11, float m12, float m21, float m22, float dx, float dy,
                            uint32_t& outPathIdx);

    // Image atlas: registered image textures for image brush fills
    struct ImageEntry {
        ID3D12Resource* texture;
        uint32_t width, height;
    };
    std::vector<ImageEntry> imageEntries_;
    static constexpr uint32_t kMaxImages = 16;

    // Mask textures: alpha or luminance masks for clip/layer compositing
    struct MaskEntry {
        ID3D12Resource* texture;
        uint32_t width, height;
        bool isLuminance;
    };
    std::vector<MaskEntry> maskEntries_;
    static constexpr uint32_t kMaxMasks = 8;

    // CPU-side flattened line segments (bypass GPU flatten)
    std::vector<LineSeg> cpuLineSegs_;
    uint32_t totalPathTiles_ = 0;  // total per-path tiles allocated this frame

    // CPU-side per-path tile data (backdrop + segment count)
    std::vector<VelloTile> cpuPathTiles_;

    // CPU-side sorted segments (per path per tile, matches HLSL Segment struct)
    std::vector<VelloSortedSeg> cpuSortedSegs_;

    // CPU-side PTCL (Per-Tile Command List) — built on CPU, executed by fine shader
    std::vector<uint32_t> cpuPtcl_;
    std::vector<uint32_t> cpuPtclOffsets_;  // per global tile: offset into cpuPtcl_

    void BuildPtcl();  // CPU-side PTCL construction

    /// Pre-compute clip path intersection for nested clips.
    /// Merges multiple clip alpha masks into a single combined mask
    /// to reduce fine shader clip stack depth.
    void OptimizeClipIntersection();

    // Clip stack: tracks BeginClip/EndClip pairs for PTCL generation
    // Each entry records the path index of the clip path
    struct ClipEntry {
        uint32_t pathIdx;     // path index for clip geometry (or UINT32_MAX for rect clip)
        uint32_t blendMode;   // blend mode for EndClip
        float    alpha;       // alpha for EndClip
        // For rect clips:
        float rx, ry, rw, rh;
    };
    std::vector<ClipEntry> clipStack_;
    uint32_t clipDepth_ = 0;

    // Clip events: ordered list of clip begin/end interleaved with path draws
    struct ClipEvent {
        enum Type { kBeginClip, kEndClip, kDraw };
        Type type;
        uint32_t pathIdx;     // for kDraw: draw path index; for kBeginClip: clip path index
        uint32_t blendMode;   // for kEndClip
        float    alpha;       // for kEndClip
    };
    std::vector<ClipEvent> clipEvents_;

    // Temporary line seg buffer tracking
    float currentBboxMinX_, currentBboxMinY_;
    float currentBboxMaxX_, currentBboxMaxY_;
    uint32_t currentSegOffset_;

    ShaderBlobCache* shaderCache_ = nullptr;

    // --- GPU resources ---

    // Root signature + PSO for CPU pipeline compute stages
    ComPtr<ID3D12RootSignature> cpuRootSig_;
    ComPtr<ID3D12PipelineState> cpuFinePSO_;

    // Compiled shader blob for CPU pipeline fine stage
    ComPtr<ID3DBlob> fineCS_;
    ComPtr<ID3DBlob> blurHorizCS_;
    ComPtr<ID3DBlob> blurVertCS_;
    ComPtr<ID3DBlob> downsampleCS_;
    ComPtr<ID3DBlob> upsampleCS_;

    // Blur pipeline state objects
    ComPtr<ID3D12PipelineState> blurHorizPSO_;
    ComPtr<ID3D12PipelineState> blurVertPSO_;
    ComPtr<ID3D12PipelineState> downsamplePSO_;
    ComPtr<ID3D12PipelineState> upsamplePSO_;
    ComPtr<ID3D12RootSignature> blurRootSig_;

    // Intermediate textures for blur (lazily created)
    ComPtr<ID3D12Resource> blurTempTexture_;    // same size as output, for separable passes
    uint32_t blurTempW_ = 0, blurTempH_ = 0;
    // Decimation textures (for multi-scale blur)
    static constexpr uint32_t kMaxDecimations = 8;
    ComPtr<ID3D12Resource> decimTextures_[kMaxDecimations]; // half-res chain
    uint32_t decimW_[kMaxDecimations] = {};
    uint32_t decimH_[kMaxDecimations] = {};

    bool CreateBlurPipelines();

    // Filter effect PSOs (lazy-created)
    ComPtr<ID3DBlob> colorMatrixCS_;
    ComPtr<ID3DBlob> offsetCS_;
    ComPtr<ID3DBlob> morphologyCS_;
    ComPtr<ID3D12PipelineState> colorMatrixPSO_;
    ComPtr<ID3D12PipelineState> offsetPSO_;
    ComPtr<ID3D12PipelineState> morphologyPSO_;
    bool filterPSOsCreated_ = false;
    bool CreateFilterPipelines();

    // GPU buffers
    ComPtr<ID3D12Resource> segmentBuffer_;       // PathSegment[]
    ComPtr<ID3D12Resource> pathInfoBuffer_;       // PathInfo[]
    ComPtr<ID3D12Resource> pathDrawBuffer_;       // PathDraw[]
    ComPtr<ID3D12Resource> drawTagBuffer_;        // DrawTag[] per draw command
    uint32_t drawTagCapacity_ = 0;
    ComPtr<ID3D12Resource> lineSegBuffer_;        // LineSeg[] (flatten output)
    ComPtr<ID3D12Resource> lineCountBuffer_;      // uint32_t atomic counter
    ComPtr<ID3D12Resource> tileBuffer_;           // global tile: cmdCount + cmd offset
    ComPtr<ID3D12Resource> tileCmdBuffer_;        // PTCL command stream (legacy)
    ComPtr<ID3D12Resource> pathTileBuffer_;       // VelloTile[] per-path per-tile
    ComPtr<ID3D12Resource> ptclBuffer_;           // PTCL (Per-Tile Command List)
    ComPtr<ID3D12Resource> constantBuffer_;       // VelloConfig
    ComPtr<ID3D12Resource> gradientRampBuffer_;   // Gradient ramp data (SRV t5)
    uint32_t gradientRampCapacity_ = 0;           // capacity in gradient count

    // Upload buffers (CPU → GPU), per-frame to avoid race conditions.
    // Frame N-1 may still be in flight when frame N writes new data.
    static constexpr uint32_t kMaxFrames = 3;

    struct FrameUploadBuffers {
        ComPtr<ID3D12Resource> segmentUpload;
        ComPtr<ID3D12Resource> pathInfoUpload;
        ComPtr<ID3D12Resource> pathDrawUpload;
        ComPtr<ID3D12Resource> constantUpload;
        ComPtr<ID3D12Resource> lineCountUpload;
        ComPtr<ID3D12Resource> lineSegUpload;
        uint32_t lineSegUploadCapacity = 0;
        ComPtr<ID3D12Resource> ptclUpload;
        ComPtr<ID3D12Resource> ptclOffsetsUpload;
        uint32_t ptclUploadCapacity = 0;
        uint32_t ptclOffsetsUploadCapacity = 0;
        ComPtr<ID3D12Resource> gradientRampUpload;
        uint32_t gradientRampUploadCapacity = 0;  // in gradient count
        // GPU pipeline temporary uploads — must survive until GPU finishes the frame
        ComPtr<ID3D12Resource> drawTagUpload;
        uint32_t drawTagUploadCapacity = 0;
        ComPtr<ID3D12Resource> drawMonoidUpload;
        uint32_t drawMonoidUploadCapacity = 0;
        ComPtr<ID3D12Resource> bumpZeroUpload;
        ComPtr<ID3D12Resource> clipBboxUpload;
        uint32_t clipBboxUploadCapacity = 0;
    };
    FrameUploadBuffers frameUploads_[kMaxFrames];
    ComPtr<ID3D12Resource> tileZeroUpload_;       // for zeroing the tile buffer
    uint32_t tileZeroUploadSize_ = 0;
    uint32_t ptclCapacity_ = 0;
    uint32_t ptclOffsetsCapacity_ = 0;

    // Output render target texture (RGBA8)
    ComPtr<ID3D12Resource> outputTexture_;
    uint32_t outputW_ = 0, outputH_ = 0;

    // Per-frame descriptor heaps for compute SRV/UAV (shader-visible).
    // Must be per-frame: CPU writes descriptors while GPU may still read
    // the previous frame's descriptors from the same heap.
    ComPtr<ID3D12DescriptorHeap> computeSrvHeap_[kMaxFrames];
    // Non-shader-visible CPU heap for ClearUnorderedAccessView
    ComPtr<ID3D12DescriptorHeap> cpuUavHeap_;
    UINT srvDescSize_ = 0;

    // Buffer capacities
    uint32_t segmentCapacity_  = 0;
    uint32_t pathCapacity_     = 0;
    uint32_t lineSegCapacity_  = 0;
    uint32_t tileCapacity_     = 0;
    uint32_t tileCmdCapacity_  = 0;

    // ============================================================================
    // Vello GPU Pipeline — Resources
    // ============================================================================

    bool gpuPipelineCreated_ = false;
    bool CreateGPUPipeline();

    // Compute shader blobs
    ComPtr<ID3DBlob> bboxClearCS_;
    ComPtr<ID3DBlob> velloFlattenCS_;
    ComPtr<ID3DBlob> binningCS_;
    ComPtr<ID3DBlob> tileAllocCS_;
    ComPtr<ID3DBlob> pathCountSetupCS_;
    ComPtr<ID3DBlob> pathCountCS_;
    ComPtr<ID3DBlob> backdropCS_;
    ComPtr<ID3DBlob> velloCoarseCS_;
    ComPtr<ID3DBlob> pathTilingSetupCS_;
    ComPtr<ID3DBlob> pathTilingCS_;
    ComPtr<ID3DBlob> velloFineCS_;

    // Pipeline State Objects
    ComPtr<ID3D12PipelineState> bboxClearPSO_;
    ComPtr<ID3D12PipelineState> velloFlattenPSO_;
    ComPtr<ID3D12PipelineState> binningPSO_;
    ComPtr<ID3D12PipelineState> tileAllocPSO_;
    ComPtr<ID3D12PipelineState> pathCountSetupPSO_;
    ComPtr<ID3D12PipelineState> pathCountPSO_;
    ComPtr<ID3D12PipelineState> backdropPSO_;
    ComPtr<ID3D12PipelineState> velloCoarsePSO_;
    ComPtr<ID3D12PipelineState> pathTilingSetupPSO_;
    ComPtr<ID3D12PipelineState> pathTilingPSO_;
    ComPtr<ID3D12PipelineState> velloFinePSO_;

    // Root signatures
    ComPtr<ID3D12RootSignature> gpuRootSig_;           // general purpose root sig for GPU stages
    ComPtr<ID3D12CommandSignature> indirectCmdSig_;     // for ExecuteIndirect dispatch

    // GPU buffers
    ComPtr<ID3D12Resource> bumpBuffer_;                 // BumpAllocators (32 bytes)
    ComPtr<ID3D12Resource> pathBboxBuffer_;              // VelloPathBbox[] per path (atomic bboxes)
    ComPtr<ID3D12Resource> lineSoupBuffer_;              // LineSoup[] (flatten output)
    ComPtr<ID3D12Resource> drawMonoidBuffer_;            // VelloDrawMonoid[] (CPU-computed prefix sum)
    ComPtr<ID3D12Resource> intersectedBboxBuffer_;       // float4[] per draw (clipped bboxes)
    ComPtr<ID3D12Resource> clipBboxBuffer_;              // float4[] per clip op (CPU-computed clip bboxes)
    ComPtr<ID3D12Resource> binDataBuffer_;               // uint32[] bin element indices
    ComPtr<ID3D12Resource> binHeaderBuffer_;             // VelloBinHeader[] per bin
    ComPtr<ID3D12Resource> velloPathBuffer_;             // VelloPath[] per draw
    ComPtr<ID3D12Resource> velloTileBuffer_;             // VelloTile[] (allocated by tile_alloc)
    ComPtr<ID3D12Resource> segCountBuffer_;              // VelloSegmentCount[] (path_count output)
    ComPtr<ID3D12Resource> velloSegmentBuffer_;            // VelloSegment[] (path_tiling output)
    ComPtr<ID3D12Resource> velloPtclBuffer_;               // uint32[] PTCL commands
    ComPtr<ID3D12Resource> blendSpillBuffer_;            // uint32[] blend stack spill
    ComPtr<ID3D12Resource> indirectBuffer1_;             // IndirectCount for path_count
    ComPtr<ID3D12Resource> indirectBuffer2_;             // IndirectCount for path_tiling
    ComPtr<ID3D12Resource> configUpload_;                // VelloConfig upload buffer

    // Buffer capacities
    uint32_t pathBboxCapacity_ = 0;
    uint32_t lineSoupCapacity_ = 0;
    uint32_t drawMonoidCapacity_ = 0;
    uint32_t intersectedBboxCapacity_ = 0;
    uint32_t binDataCapacity_ = 0;
    uint32_t binHeaderCapacity_ = 0;
    uint32_t velloPathCapacity_ = 0;
    uint32_t velloTileCapacity_ = 0;
    uint32_t segCountCapacity_ = 0;
    uint32_t velloSegmentCapacity_ = 0;
    uint32_t velloPtclCapacity_ = 0;
    uint32_t blendSpillCapacity_ = 0;

    // GPU descriptor heap
    ComPtr<ID3D12DescriptorHeap> gpuSrvHeap_[kMaxFrames];

    // Helper: compute DrawMonoid prefix sum on CPU
    void ComputeDrawMonoids(std::vector<VelloDrawMonoid>& outMonoids);

    // Helper: ensure all GPU buffers are allocated with sufficient capacity
    bool EnsureGPUBuffers(uint32_t numPaths, uint32_t numSegs, uint32_t numDrawObjs);

};

} // namespace jalium
