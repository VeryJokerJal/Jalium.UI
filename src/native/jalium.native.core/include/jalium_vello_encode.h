// jalium_vello_encode.h -- shared CPU scene encoder for the Vello GPU pipeline.
//
// C++ port of vello 0.10.0's `vello_encoding` crate (encoding.rs / path.rs /
// draw.rs / config.rs / resolve.rs / ramp_cache.rs), shared by the D3D12 and
// Vulkan backends. The scene is a single packed u32 stream (path tags, path
// data, draw tags, draw data, transforms, styles) with word-offset bases in
// VelloConfig; all monoid scans, curve flattening, stroke expansion and clip
// stack evaluation happen ON THE GPU (see vello_*.cs.hlsl).
//
// This header is a byte-exact contract with the compute shaders -- do NOT
// reorder fields or change sizes without updating vello_shared.hlsli and both
// backends' pipelines in lockstep.
//
// Divergences from upstream vello_encoding (all deliberate):
//   * Gradient ramps are resolved immediately at encode time (no Patch /
//     late-bound Resolver machinery) into a per-frame 512-wide premultiplied
//     RGBA8 ramp image, deduplicated by content hash.
//   * The i16 path-data encoding is never emitted (f32 only), matching what
//     upstream's PathEncoder emits in 0.10.0.
//   * Glyph runs are not supported (Jalium has its own text pipeline).
//   * D3D12/Vulkan select upstream's `pathtag_scan_small` permutation when
//     eligible, but the large-chain counts and reduced buffers remain
//     populated unconditionally for backends that have not adopted it yet.
//   * Elliptical radial gradients are expressed via a synthesized brush
//     transform (upstream leaves this to the caller).

#pragma once

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <vector>

#include "jalium_rendering_engine.h"
#include "jalium_triangulate.h"

namespace jalium {

// ============================================================================
// Geometry / pipeline constants (mirror vello_shared.hlsli)
// ============================================================================

constexpr uint32_t kVelloTileWidth = 16;
constexpr uint32_t kVelloTileHeight = 16;
constexpr uint32_t kVelloNTileX = 16;
constexpr uint32_t kVelloNTileY = 16;
constexpr uint32_t kVelloNTile = 256;
constexpr uint32_t kVelloPtclInitialAlloc = 64;
constexpr uint32_t kVelloRampWidth = 512;
constexpr uint32_t kVelloWorkgroupSize = 256;
// align_up(n_path_tags, 4 * WG) -- tag bytes per pathtag workgroup.
constexpr uint32_t kVelloPathTagAlign = 4 * kVelloWorkgroupSize;

// PathTag byte values (pathtag.wgsl / path.rs)
constexpr uint8_t kVelloPathTagLineToF32 = 0x9;
constexpr uint8_t kVelloPathTagQuadToF32 = 0xa;
constexpr uint8_t kVelloPathTagCubicToF32 = 0xb;
constexpr uint8_t kVelloPathTagPath = 0x10;
constexpr uint8_t kVelloPathTagTransform = 0x20;
constexpr uint8_t kVelloPathTagStyle = 0x40;
constexpr uint8_t kVelloPathTagSubpathEndBit = 0x4;

// DrawTag values (drawtag.wgsl / draw.rs). Bit-packed DrawMonoid:
//   path_ix = (tag != 0), clip_ix = tag & 1,
//   scene_offset (draw data u32s) = (tag >> 2) & 0x7,
//   info_offset (info u32s) = (tag >> 6) & 0xf.
constexpr uint32_t kVelloDrawTagNop = 0x000;
constexpr uint32_t kVelloDrawTagColor = 0x044;
constexpr uint32_t kVelloDrawTagLinearGradient = 0x114;
constexpr uint32_t kVelloDrawTagRadialGradient = 0x29c;
constexpr uint32_t kVelloDrawTagSweepGradient = 0x254;
constexpr uint32_t kVelloDrawTagImage = 0x28C;
constexpr uint32_t kVelloDrawTagBlurRect = 0x2d4;
constexpr uint32_t kVelloDrawTagBeginClip = 0x049;
constexpr uint32_t kVelloDrawTagEndClip = 0x021;

inline uint32_t VelloDrawTagSceneSize(uint32_t tag) { return (tag >> 2) & 0x7u; }
inline uint32_t VelloDrawTagInfoSize(uint32_t tag) { return (tag >> 6) & 0xfu; }

// Style flags (path.rs::Style)
constexpr uint32_t kVelloStyleFlagsStyleBit = 0x80000000u;  // 1 = stroke
constexpr uint32_t kVelloStyleFlagsFillBit = 0x40000000u;   // 1 = even-odd
constexpr uint32_t kVelloStyleJoinBevel = 0u;
constexpr uint32_t kVelloStyleJoinMiter = 0x10000000u;
constexpr uint32_t kVelloStyleJoinRound = 0x20000000u;
constexpr uint32_t kVelloStyleCapButt = 0u;
constexpr uint32_t kVelloStyleCapSquare = 0x01000000u;
constexpr uint32_t kVelloStyleCapRound = 0x02000000u;
// start cap bits = cap bits << 2, end cap bits = cap bits.

// Blend words: (mix << 8) | compose (peniko::BlendMode)
constexpr uint32_t kVelloBlendDefault = 3u;              // Normal + SrcOver
constexpr uint32_t kVelloBlendClip = 0x8003u;            // MIX_CLIP<<8 | SrcOver
constexpr uint32_t kVelloBlendLuminanceMask = 0x10000u;  // out-of-band

// Extend modes (peniko::Extend)
constexpr uint32_t kVelloExtendPad = 0;
constexpr uint32_t kVelloExtendRepeat = 1;
constexpr uint32_t kVelloExtendReflect = 2;

// Bump allocator failure bits (bump.wgsl)
constexpr uint32_t kVelloStageBinning = 0x1;
constexpr uint32_t kVelloStageTileAlloc = 0x2;
constexpr uint32_t kVelloStageFlatten = 0x4;
constexpr uint32_t kVelloStagePathCount = 0x8;
constexpr uint32_t kVelloStageCoarse = 0x10;

// ============================================================================
// GPU-mirror structs (element strides for buffer sizing; HLSL natural layout)
// ============================================================================

struct VelloTransform {
    float matrix[4] = {1.0f, 0.0f, 0.0f, 1.0f};  // column-major 2x2: (a,b,c,d)
    float translation[2] = {0.0f, 0.0f};

    static VelloTransform Identity() { return VelloTransform{}; }

    static VelloTransform FromEngine(const EngineTransform& t)
    {
        VelloTransform v;
        v.matrix[0] = t.m11;
        v.matrix[1] = t.m12;
        v.matrix[2] = t.m21;
        v.matrix[3] = t.m22;
        v.translation[0] = t.dx;
        v.translation[1] = t.dy;
        return v;
    }

    bool operator==(const VelloTransform& o) const
    {
        return std::memcmp(this, &o, sizeof(VelloTransform)) == 0;
    }

    // self * other: applies `other` first.
    VelloTransform Mul(const VelloTransform& o) const
    {
        VelloTransform r;
        r.matrix[0] = matrix[0] * o.matrix[0] + matrix[2] * o.matrix[1];
        r.matrix[1] = matrix[1] * o.matrix[0] + matrix[3] * o.matrix[1];
        r.matrix[2] = matrix[0] * o.matrix[2] + matrix[2] * o.matrix[3];
        r.matrix[3] = matrix[1] * o.matrix[2] + matrix[3] * o.matrix[3];
        r.translation[0] = matrix[0] * o.translation[0] + matrix[2] * o.translation[1] + translation[0];
        r.translation[1] = matrix[1] * o.translation[0] + matrix[3] * o.translation[1] + translation[1];
        return r;
    }

    void Apply(float x, float y, float& ox, float& oy) const
    {
        ox = matrix[0] * x + matrix[2] * y + translation[0];
        oy = matrix[1] * x + matrix[3] * y + translation[1];
    }
};
static_assert(sizeof(VelloTransform) == 24, "VelloTransform must be 24 bytes");

// IEEE-754 binary16 encode for the miter limit (Style low 16 bits).
inline uint16_t VelloF32ToF16(float value)
{
    uint32_t bits;
    std::memcpy(&bits, &value, 4);
    uint32_t sign = (bits >> 16) & 0x8000u;
    int32_t exponent = (int32_t)((bits >> 23) & 0xffu) - 127 + 15;
    uint32_t mantissa = bits & 0x7fffffu;
    if (exponent >= 0x1f) return (uint16_t)(sign | 0x7c00u);  // inf/overflow
    if (exponent <= 0) {
        if (exponent < -10) return (uint16_t)sign;
        mantissa |= 0x800000u;
        uint32_t shift = (uint32_t)(14 - exponent);
        return (uint16_t)(sign | (mantissa >> shift));
    }
    return (uint16_t)(sign | ((uint32_t)exponent << 10) | (mantissa >> 13));
}

struct VelloStyle {
    uint32_t flagsAndMiterLimit = 0;
    float lineWidth = 0.0f;

    static VelloStyle Fill(FillRule rule)
    {
        VelloStyle s;
        s.flagsAndMiterLimit = (rule == FillRule::EvenOdd) ? kVelloStyleFlagsFillBit : 0u;
        s.lineWidth = 0.0f;
        return s;
    }

    // lineJoin: 0=Miter, 1=Bevel, else Round (Jalium convention)
    // lineCap:  0=Butt/Flat, 1=Square, else Round
    static VelloStyle Stroke(float width, int32_t lineJoin, float miterLimit, int32_t lineCap)
    {
        uint32_t join = (lineJoin == 0)   ? kVelloStyleJoinMiter
                        : (lineJoin == 1) ? kVelloStyleJoinBevel
                                          : kVelloStyleJoinRound;
        uint32_t cap = (lineCap == 0)   ? kVelloStyleCapButt
                       : (lineCap == 1) ? kVelloStyleCapSquare
                                        : kVelloStyleCapRound;
        VelloStyle s;
        s.flagsAndMiterLimit = kVelloStyleFlagsStyleBit | join | (cap << 2) | cap |
                               (uint32_t)VelloF32ToF16(miterLimit);
        s.lineWidth = width;
        return s;
    }

    bool operator==(const VelloStyle& o) const
    {
        return flagsAndMiterLimit == o.flagsAndMiterLimit && lineWidth == o.lineWidth;
    }
};
static_assert(sizeof(VelloStyle) == 8, "VelloStyle must be 8 bytes");

// ConfigUniform (config.wgsl / config.rs) -- 22 u32 = 88 bytes packed.
struct VelloConfig {
    uint32_t width_in_tiles = 0;
    uint32_t height_in_tiles = 0;
    uint32_t target_width = 0;
    uint32_t target_height = 0;
    uint32_t base_color = 0;  // premultiplied RGBA8, r in the low byte
    // Layout (resolve.rs), inlined:
    uint32_t n_drawobj = 0;
    uint32_t n_path = 0;
    uint32_t n_clip = 0;
    uint32_t bin_data_start = 0;
    uint32_t pathtag_base = 0;
    uint32_t pathdata_base = 0;
    uint32_t drawtag_base = 0;
    uint32_t drawdata_base = 0;
    uint32_t transform_base = 0;
    uint32_t style_base = 0;
    // Bump-allocated buffer sizes (element units):
    uint32_t lines_size = 0;
    uint32_t binning_size = 0;
    uint32_t tiles_size = 0;
    uint32_t seg_counts_size = 0;
    uint32_t segments_size = 0;
    uint32_t blend_size = 0;
    uint32_t ptcl_size = 0;
};
static_assert(sizeof(VelloConfig) == 88, "VelloConfig must be 88 bytes");

// BumpAllocators (bump.wgsl) -- zeroed before flatten each dispatch.
struct VelloBumpAllocators {
    uint32_t failed;
    uint32_t binning;
    uint32_t ptcl;
    uint32_t tile;
    uint32_t seg_counts;
    uint32_t segments;
    uint32_t blend;
    uint32_t lines;
};
static_assert(sizeof(VelloBumpAllocators) == 32, "VelloBumpAllocators must be 32 bytes");

// Element strides of the GPU-internal buffers (HLSL StructuredBuffer layouts).
constexpr uint32_t kVelloStrideTagMonoid = 20;
constexpr uint32_t kVelloStridePathBbox = 24;
constexpr uint32_t kVelloStrideDrawMonoid = 16;
constexpr uint32_t kVelloStrideClipInp = 8;
constexpr uint32_t kVelloStrideClipBic = 8;
constexpr uint32_t kVelloStrideClipEl = 20;
constexpr uint32_t kVelloStrideClipBbox = 16;
constexpr uint32_t kVelloStrideDrawBbox = 16;
constexpr uint32_t kVelloStrideBinHeader = 8;
constexpr uint32_t kVelloStridePath = 20;
constexpr uint32_t kVelloStrideTile = 8;
constexpr uint32_t kVelloStrideLineSoup = 24;
constexpr uint32_t kVelloStrideSegCount = 8;
constexpr uint32_t kVelloStrideSegment = 24;

// ============================================================================
// Scene container
// ============================================================================

struct VelloScene {
    // Encoded streams (the six substreams of the packed scene buffer).
    std::vector<uint8_t> pathTags;
    std::vector<uint32_t> pathData;  // f32 bit patterns
    std::vector<uint32_t> drawTags;
    std::vector<uint32_t> drawData;
    std::vector<VelloTransform> transforms;
    std::vector<VelloStyle> styles;

    uint32_t numPaths = 0;
    uint32_t numPathSegments = 0;
    uint32_t numClips = 0;
    uint32_t numOpenClips = 0;
    uint32_t numInfoWords = 0;  // sum of info_size over drawTags (= bin_data_start)

    // Gradient ramp image: kVelloRampWidth premultiplied RGBA8 texels per ramp.
    std::vector<uint32_t> rampData;
    uint32_t rampCount = 0;

    uint32_t viewportW = 0;
    uint32_t viewportH = 0;

    void Clear()
    {
        pathTags.clear();
        pathData.clear();
        drawTags.clear();
        drawData.clear();
        transforms.clear();
        styles.clear();
        numPaths = 0;
        numPathSegments = 0;
        numClips = 0;
        numOpenClips = 0;
        numInfoWords = 0;
        rampData.clear();
        rampCount = 0;
        // viewport intentionally retained
    }

    bool Empty() const { return drawTags.empty(); }
};

// The tile-aligned device-space rectangle a sub-scene actually covers.
//
// A frame is cut into many sub-scenes (painter-order interleaving with rects/
// text/bitmaps), and a typical sub-scene is a single small icon. Rendering
// each one at full viewport size would allocate a viewport-sized output
// texture and run `fine` over every tile on screen, so instead the encoder
// reports the union bbox of everything it encoded and the backend renders
// ONLY that region: the packed transform stream is rebased by -origin, so the
// GPU pipeline still sees a self-contained scene whose target happens to be
// the region. The backend then composites the result at `origin`.
struct VelloRenderRegion {
    uint32_t originX = 0;
    uint32_t originY = 0;
    uint32_t width = 0;
    uint32_t height = 0;

    bool Empty() const { return width == 0 || height == 0; }
};

// Packed scene + layout (resolve.rs)
struct VelloPackedScene {
    std::vector<uint32_t> data;
    uint32_t pathTagBase = 0;
    uint32_t pathDataBase = 0;
    uint32_t drawTagBase = 0;
    uint32_t drawDataBase = 0;
    uint32_t transformBase = 0;
    uint32_t styleBase = 0;
    uint32_t numPathTagBytes = 0;  // unpadded tag count (drives flatten dispatch)
    VelloRenderRegion region;      // transforms in `data` are rebased by -region.origin
};

// Per-dispatch derived sizes/counts (config.rs::WorkgroupCounts/BufferSizes).
struct VelloRenderInfo {
    VelloConfig config;

    // Dispatch grids (x sizes; y=z=1 unless noted)
    // Upstream uses the two extra reduce2/scan1 passes only when the first
    // reduction produces more than one workgroup's worth of monoids. D3D12
    // and Vulkan use this flag to select the small scan permutation; the
    // legacy counts/buffer sizes below remain populated so other backends can
    // keep using the always-large graph until they adopt that permutation.
    bool useLargePathScan = false;
    uint32_t pathtagReduceWgs = 0;
    uint32_t pathtagReduce2Wgs = 0;   // = pathtagScan1Wgs; used by large scan only
    uint32_t pathtagScan1Wgs = 0;
    uint32_t pathtagScanWgs = 0;      // = pathtagReduceWgs
    uint32_t bboxClearWgs = 0;
    uint32_t flattenWgs = 0;
    uint32_t drawReduceWgs = 0;       // = drawLeafWgs
    uint32_t clipReduceWgs = 0;       // 0 => skip
    uint32_t clipLeafWgs = 0;         // 0 => skip
    uint32_t binningWgs = 0;
    uint32_t tileAllocWgs = 0;
    uint32_t backdropWgs = 0;
    uint32_t widthInBins = 0;         // coarse grid x
    uint32_t heightInBins = 0;        // coarse grid y
    // fine grid = (width_in_tiles, height_in_tiles)

    // Element counts for GPU-internal buffers
    uint32_t reducedSize = 0;         // TagMonoid
    uint32_t reduced2Size = 0;        // TagMonoid (256)
    uint32_t reducedScanSize = 0;     // TagMonoid
    uint32_t tagMonoidsSize = 0;      // TagMonoid
    uint32_t pathBboxSize = 0;        // PathBbox
    uint32_t drawReducedSize = 0;     // DrawMonoid
    uint32_t drawMonoidSize = 0;      // DrawMonoid
    uint32_t clipInpSize = 0;         // ClipInp
    uint32_t clipBicSize = 0;         // Bic
    uint32_t clipElSize = 0;          // ClipEl
    uint32_t clipBboxSize = 0;        // float4
    uint32_t drawBboxSize = 0;        // float4
    uint32_t binHeaderSize = 0;       // BinHeader
    uint32_t pathSize = 0;            // Path (aligned up to 256)
    uint32_t infoBinDataSize = 0;     // u32 (info + bin data combined)
    uint32_t tileSize = 0;            // Tile
    uint32_t lineSoupSize = 0;        // LineSoup
    uint32_t segCountSize = 0;        // SegmentCount
    uint32_t segmentSize = 0;         // Segment
    uint32_t blendSpillSize = 0;      // u32
    uint32_t ptclSize = 0;            // u32
};

// One packed sub-scene cut out of the encoder mid-frame (painter-order
// interleaving for the GPU compute path: every span of consecutive path
// encodes becomes its own dispatch graph + composite, exactly like the D3D12
// FlushVelloPaths sub-scenes). Self-contained: the compute pipeline consumes
// it without touching the encoder again.
struct VelloSubScene {
    VelloPackedScene packed;
    VelloRenderInfo ri;
    std::vector<uint32_t> rampData;
    uint32_t rampCount = 0;
    uint32_t viewportW = 0;
    uint32_t viewportH = 0;
};

inline uint32_t VelloAlignUp(uint32_t len, uint32_t alignment)
{
    return (len + alignment - 1u) & ~(alignment - 1u);
}

inline uint32_t VelloDivCeil(uint32_t n, uint32_t d)
{
    return (n + d - 1u) / d;
}

// ============================================================================
// Path encoder (path.rs::PathEncoder) -- f32 variant only
// ============================================================================

class VelloPathEncoder {
public:
    VelloPathEncoder(std::vector<uint8_t>& tags, std::vector<uint32_t>& data,
                     uint32_t& numSegments, uint32_t& numPaths, bool isFill)
        : tags_(tags), data_(data), numSegments_(numSegments), numPaths_(numPaths),
          isFill_(isFill)
    {
    }

    void MoveTo(float x, float y)
    {
        if (isFill_) Close();
        float buf[2] = {x, y};
        if (state_ == State::MoveTo) {
            data_.resize(data_.size() - 2);  // drop dangling move
        } else if (state_ == State::NonemptySubpath) {
            if (!isFill_) InsertStrokeCapMarkerSegment(false);
            if (!tags_.empty()) tags_.back() |= kVelloPathTagSubpathEndBit;
        }
        firstPoint_[0] = x;
        firstPoint_[1] = y;
        PushData(buf, 2);
        state_ = State::MoveTo;
    }

    void LineTo(float x, float y)
    {
        if (state_ == State::Start) {
            if (numEncodedSegments_ == 0) return;  // implicit move promotion needs prior point
            MoveToLastPoint();
        }
        if (!isFill_ && state_ == State::MoveTo) {
            float p0[2] = {LastX(), LastY()};
            if (!SetStartTangentLine(p0[0], p0[1], x, y)) return;
        }
        float buf[2] = {x, y};
        if (IsZeroLengthSegment(buf, 1)) return;
        PushData(buf, 2);
        tags_.push_back(kVelloPathTagLineToF32);
        state_ = State::NonemptySubpath;
        numEncodedSegments_++;
    }

    void QuadTo(float x1, float y1, float x2, float y2)
    {
        if (state_ == State::Start) {
            if (numEncodedSegments_ == 0) return;
            MoveToLastPoint();
        }
        if (!isFill_ && state_ == State::MoveTo) {
            float p0[2] = {LastX(), LastY()};
            if (!SetStartTangentQuad(p0[0], p0[1], x1, y1, x2, y2)) return;
        }
        float buf[4] = {x1, y1, x2, y2};
        if (IsZeroLengthSegment(buf, 2)) return;
        PushData(buf, 4);
        tags_.push_back(kVelloPathTagQuadToF32);
        state_ = State::NonemptySubpath;
        numEncodedSegments_++;
    }

    void CubicTo(float x1, float y1, float x2, float y2, float x3, float y3)
    {
        if (state_ == State::Start) {
            if (numEncodedSegments_ == 0) return;
            MoveToLastPoint();
        }
        if (!isFill_ && state_ == State::MoveTo) {
            float p0[2] = {LastX(), LastY()};
            if (!SetStartTangentCubic(p0[0], p0[1], x1, y1, x2, y2, x3, y3)) return;
        }
        float buf[6] = {x1, y1, x2, y2, x3, y3};
        if (IsZeroLengthSegment(buf, 3)) return;
        PushData(buf, 6);
        tags_.push_back(kVelloPathTagCubicToF32);
        state_ = State::NonemptySubpath;
        numEncodedSegments_++;
    }

    void Close()
    {
        if (state_ != State::NonemptySubpath) {
            if (state_ == State::MoveTo) {
                data_.resize(data_.size() - 2);
                state_ = State::Start;
            }
            return;
        }
        if (data_.size() < 2) {
            state_ = State::Start;
            return;
        }
        float lx = LastX(), ly = LastY();
        if (lx != firstPoint_[0] || ly != firstPoint_[1]) {
            float buf[2] = {firstPoint_[0], firstPoint_[1]};
            PushData(buf, 2);
            tags_.push_back(kVelloPathTagLineToF32);
            numEncodedSegments_++;
        }
        if (!isFill_) InsertStrokeCapMarkerSegment(true);
        if (!tags_.empty()) tags_.back() |= kVelloPathTagSubpathEndBit;
        state_ = State::Start;
    }

    // Writes an empty path (used to keep the clip stack balanced for
    // degenerate clip shapes).
    void EmptyPath()
    {
        float buf[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        PushData(buf, 4);
        tags_.push_back(kVelloPathTagLineToF32);
        numEncodedSegments_++;
        state_ = State::NonemptySubpath;
    }

    // Returns the number of encoded segments. Emits the PATH marker (and bumps
    // n_paths) only when at least one segment was encoded.
    uint32_t Finish(bool insertPathMarker)
    {
        if (isFill_) Close();
        if (state_ == State::MoveTo) {
            data_.resize(data_.size() - 2);
        }
        if (numEncodedSegments_ != 0) {
            if (!isFill_ && state_ == State::NonemptySubpath) {
                InsertStrokeCapMarkerSegment(false);
            }
            if (!tags_.empty()) tags_.back() |= kVelloPathTagSubpathEndBit;
            numSegments_ += numEncodedSegments_;
            if (insertPathMarker) {
                tags_.push_back(kVelloPathTagPath);
                numPaths_ += 1;
            }
        }
        return numEncodedSegments_;
    }

private:
    enum class State { Start, MoveTo, NonemptySubpath };

    void PushData(const float* vals, size_t n)
    {
        for (size_t i = 0; i < n; i++) {
            uint32_t bits;
            std::memcpy(&bits, &vals[i], 4);
            data_.push_back(bits);
        }
    }

    float LastX() const
    {
        float v;
        std::memcpy(&v, &data_[data_.size() - 2], 4);
        return v;
    }

    float LastY() const
    {
        float v;
        std::memcpy(&v, &data_[data_.size() - 1], 4);
        return v;
    }

    void MoveToLastPoint()
    {
        // Promote a leading segment with no prior move to a move of the last
        // encoded point (kurbo behavior).
        float x = LastX(), y = LastY();
        MoveTo(x, y);
    }

    // Zero-length filter (path.rs::is_zero_length_segment, EPSILON = 1e-12):
    // bbox of the new control points together with the current last point.
    bool IsZeroLengthSegment(const float* pts, size_t nPoints) const
    {
        constexpr float kEps = 1e-12f;
        float x0 = LastX(), y0 = LastY();
        float minX = x0, maxX = x0, minY = y0, maxY = y0;
        for (size_t i = 0; i < nPoints; i++) {
            float x = pts[i * 2], y = pts[i * 2 + 1];
            minX = std::fmin(minX, x);
            maxX = std::fmax(maxX, x);
            minY = std::fmin(minY, y);
            maxY = std::fmax(maxY, y);
        }
        return !(maxX - minX > kEps || maxY - minY > kEps);
    }

    // Start tangent selection (path.rs::start_tangent_for_*): pick the first
    // control point sufficiently far from the start; None => drop the segment.
    static bool TangentValid(float px, float py, float qx, float qy)
    {
        constexpr float kEps = 1e-12f;
        float dx = qx - px, dy = qy - py;
        return dx * dx + dy * dy > kEps;
    }

    bool SetStartTangentLine(float p0x, float p0y, float p1x, float p1y)
    {
        if (!TangentValid(p0x, p0y, p1x, p1y)) return false;
        firstStartTangentEnd_[0] = p0x + (p1x - p0x) / 3.0f;
        firstStartTangentEnd_[1] = p0y + (p1y - p0y) / 3.0f;
        return true;
    }

    bool SetStartTangentQuad(float p0x, float p0y, float p1x, float p1y, float p2x, float p2y)
    {
        if (TangentValid(p0x, p0y, p1x, p1y)) {
            firstStartTangentEnd_[0] = p1x + (p0x - p1x) / 3.0f;
            firstStartTangentEnd_[1] = p1y + (p0y - p1y) / 3.0f;
        } else if (TangentValid(p0x, p0y, p2x, p2y)) {
            firstStartTangentEnd_[0] = p1x + (p2x - p1x) / 3.0f;
            firstStartTangentEnd_[1] = p1y + (p2y - p1y) / 3.0f;
        } else {
            return false;
        }
        return true;
    }

    bool SetStartTangentCubic(float p0x, float p0y, float p1x, float p1y, float p2x, float p2y,
                              float p3x, float p3y)
    {
        if (TangentValid(p0x, p0y, p1x, p1y)) {
            firstStartTangentEnd_[0] = p1x;
            firstStartTangentEnd_[1] = p1y;
        } else if (TangentValid(p0x, p0y, p2x, p2y)) {
            firstStartTangentEnd_[0] = p2x;
            firstStartTangentEnd_[1] = p2y;
        } else if (TangentValid(p0x, p0y, p3x, p3y)) {
            firstStartTangentEnd_[0] = p3x;
            firstStartTangentEnd_[1] = p3y;
        } else {
            return false;
        }
        return true;
    }

    // Stroke cap marker (path.rs): open subpath => quad-to (p1 = subpath first
    // point, p2 = start tangent end); closed subpath => line-to the start
    // tangent end. This is the only segment carrying SUBPATH_END, and the GPU
    // flattener derives caps/joins from it.
    void InsertStrokeCapMarkerSegment(bool isClosed)
    {
        if (isClosed) {
            LineTo(firstStartTangentEnd_[0], firstStartTangentEnd_[1]);
        } else {
            QuadTo(firstPoint_[0], firstPoint_[1], firstStartTangentEnd_[0],
                   firstStartTangentEnd_[1]);
        }
    }

    std::vector<uint8_t>& tags_;
    std::vector<uint32_t>& data_;
    uint32_t& numSegments_;
    uint32_t& numPaths_;
    bool isFill_;
    State state_ = State::Start;
    uint32_t numEncodedSegments_ = 0;
    float firstPoint_[2] = {0, 0};
    float firstStartTangentEnd_[2] = {0, 0};
};

// ============================================================================
// Color helpers
// ============================================================================

// Premultiplied RGBA8, little-endian, r in the low byte (draw.rs::DrawColor).
inline uint32_t VelloPackPremulColor(float r, float g, float b, float a)
{
    a = std::fmin(std::fmax(a, 0.0f), 1.0f);
    auto quant = [](float v) -> uint32_t {
        v = std::fmin(std::fmax(v, 0.0f), 1.0f);
        return (uint32_t)(v * 255.0f + 0.5f);
    };
    return quant(r * a) | (quant(g * a) << 8) | (quant(b * a) << 16) | (quant(a) << 24);
}

// ============================================================================
// Scene encoder
// ============================================================================

class VelloSceneEncoder {
public:
    static constexpr uint32_t kTileSize = kVelloTileWidth;
    // Cap on gradient ramps per frame (each is kVelloRampWidth texels).
    static constexpr uint32_t kMaxRamps = 512;

    // ------------------------------------------------------------------
    // Frame lifecycle
    // ------------------------------------------------------------------

    void BeginFrame(uint32_t viewportW, uint32_t viewportH)
    {
        scene_.Clear();
        scene_.viewportW = viewportW;
        scene_.viewportH = viewportH;
        tilesX_ = VelloDivCeil(viewportW, kVelloTileWidth);
        tilesY_ = VelloDivCeil(viewportH, kVelloTileHeight);
        hasScissor_ = false;
        scissorClipActive_ = false;
        rampHashes_.clear();
        packedDirty_ = true;
        ResetBbox();
    }

    void SetScissor(float l, float t, float r, float b)
    {
        if (hasScissor_ && scissor_[0] == l && scissor_[1] == t && scissor_[2] == r &&
            scissor_[3] == b) {
            return;
        }
        CloseScissorClip();
        scissor_[0] = l;
        scissor_[1] = t;
        scissor_[2] = r;
        scissor_[3] = b;
        hasScissor_ = true;
    }

    void ClearScissor()
    {
        CloseScissorClip();
        hasScissor_ = false;
    }

    // ------------------------------------------------------------------
    // Fills / strokes (Jalium command-stream entry points)
    // ------------------------------------------------------------------

    bool EncodeFillPath(float startX, float startY, const float* commands, uint32_t commandLength,
                        const EngineBrushData& brush, FillRule fillRule,
                        const EngineTransform& transform, float opacity = 1.0f)
    {
        EnsureScissorClip();
        packedDirty_ = true;
        VelloTransform t = VelloTransform::FromEngine(transform);
        EncodeTransform(t);
        EncodeStyle(VelloStyle::Fill(fillRule));
        uint32_t nSegs = EncodePathCommands(startX, startY, commands, commandLength, true);
        if (nSegs == 0) return true;  // nothing to draw; not an error
        float bx0, by0, bx1, by1;
        CommandHullBounds(startX, startY, commands, commandLength, bx0, by0, bx1, by1);
        AccumulateLocalBox(bx0, by0, bx1, by1, t, 1.0f);
        EncodeBrush(brush, opacity, t);
        return true;
    }

    bool EncodeStrokePath(float startX, float startY, const float* commands,
                          uint32_t commandLength, const EngineBrushData& brush, float strokeWidth,
                          bool /*closed*/, int32_t lineJoin, float miterLimit, int32_t lineCap,
                          const float* dashPattern, uint32_t dashCount, float dashOffset,
                          const EngineTransform& transform, float opacity = 1.0f)
    {
        if (!(strokeWidth > 0.0f)) return true;  // zero-width stroke draws nothing
        EnsureScissorClip();
        packedDirty_ = true;
        VelloTransform t = VelloTransform::FromEngine(transform);

        if (dashPattern != nullptr && dashCount > 0) {
            return EncodeDashedStroke(startX, startY, commands, commandLength, brush, strokeWidth,
                                      lineJoin, miterLimit, lineCap, dashPattern, dashCount,
                                      dashOffset, t, opacity);
        }

        EncodeTransform(t);
        EncodeStyle(VelloStyle::Stroke(strokeWidth, lineJoin, miterLimit, lineCap));
        uint32_t nSegs = EncodePathCommands(startX, startY, commands, commandLength, false);
        if (nSegs == 0) return true;
        AccumulateStrokeBounds(startX, startY, commands, commandLength, strokeWidth,
                               miterLimit, t);
        EncodeBrush(brush, opacity, t);
        return true;
    }

    bool EncodeFillPolygon(const float* points, uint32_t pointCount, const EngineBrushData& brush,
                           FillRule fillRule, const EngineTransform& transform,
                           float opacity = 1.0f)
    {
        if (pointCount < 3) return true;
        EnsureScissorClip();
        packedDirty_ = true;
        VelloTransform t = VelloTransform::FromEngine(transform);
        EncodeTransform(t);
        EncodeStyle(VelloStyle::Fill(fillRule));
        VelloPathEncoder enc(scene_.pathTags, scene_.pathData, scene_.numPathSegments,
                             scene_.numPaths, true);
        enc.MoveTo(points[0], points[1]);
        for (uint32_t i = 1; i < pointCount; i++) {
            enc.LineTo(points[i * 2], points[i * 2 + 1]);
        }
        enc.Close();
        if (enc.Finish(true) == 0) return true;
        for (uint32_t i = 0; i < pointCount; i++) {
            float dx, dy;
            t.Apply(points[i * 2], points[i * 2 + 1], dx, dy);
            AccumulateDevicePoint(dx - 1.0f, dy - 1.0f);
            AccumulateDevicePoint(dx + 1.0f, dy + 1.0f);
        }
        EncodeBrush(brush, opacity, t);
        return true;
    }

    bool EncodeFillEllipse(float cx, float cy, float rx, float ry, const EngineBrushData& brush,
                           const EngineTransform& transform, float opacity = 1.0f)
    {
        EnsureScissorClip();
        packedDirty_ = true;
        VelloTransform t = VelloTransform::FromEngine(transform);
        EncodeTransform(t);
        EncodeStyle(VelloStyle::Fill(FillRule::NonZero));
        constexpr float k = 0.5522847498f;
        VelloPathEncoder enc(scene_.pathTags, scene_.pathData, scene_.numPathSegments,
                             scene_.numPaths, true);
        enc.MoveTo(cx + rx, cy);
        enc.CubicTo(cx + rx, cy + k * ry, cx + k * rx, cy + ry, cx, cy + ry);
        enc.CubicTo(cx - k * rx, cy + ry, cx - rx, cy + k * ry, cx - rx, cy);
        enc.CubicTo(cx - rx, cy - k * ry, cx - k * rx, cy - ry, cx, cy - ry);
        enc.CubicTo(cx + k * rx, cy - ry, cx + rx, cy - k * ry, cx + rx, cy);
        enc.Close();
        if (enc.Finish(true) == 0) return true;
        AccumulateLocalBox(cx - rx, cy - ry, cx + rx, cy + ry, t, 1.0f);
        EncodeBrush(brush, opacity, t);
        return true;
    }

    // ------------------------------------------------------------------
    // Layers / clips / blur
    // ------------------------------------------------------------------

    // Pushes a clip/blend layer whose shape is the given path.
    bool EncodeBeginClipPath(float startX, float startY, const float* commands,
                             uint32_t commandLength, FillRule fillRule,
                             const EngineTransform& transform,
                             uint32_t blend = kVelloBlendClip, float alpha = 1.0f)
    {
        packedDirty_ = true;
        VelloTransform t = VelloTransform::FromEngine(transform);
        EncodeTransform(t);
        EncodeStyle(VelloStyle::Fill(fillRule));
        uint32_t nSegs = EncodePathCommands(startX, startY, commands, commandLength, true);
        if (nSegs == 0) EncodeEmptyShape();
        // NOTE: a clip only ever shrinks what is visible, so it deliberately
        // does not grow the sub-scene region.
        scene_.drawTags.push_back(kVelloDrawTagBeginClip);
        scene_.numInfoWords += VelloDrawTagInfoSize(kVelloDrawTagBeginClip);
        scene_.drawData.push_back(blend);
        uint32_t alphaBits;
        float clamped = std::fmin(std::fmax(alpha, 0.0f), 1.0f);
        std::memcpy(&alphaBits, &clamped, 4);
        scene_.drawData.push_back(alphaBits);
        scene_.numClips++;
        scene_.numOpenClips++;
        return true;
    }

    void EncodeBeginClipRect(float x, float y, float w, float h,
                             uint32_t blend = kVelloBlendClip, float alpha = 1.0f)
    {
        float commands[9];
        commands[0] = (float)kTagLineTo;
        commands[1] = x + w;
        commands[2] = y;
        commands[3] = (float)kTagLineTo;
        commands[4] = x + w;
        commands[5] = y + h;
        commands[6] = (float)kTagLineTo;
        commands[7] = x;
        commands[8] = y + h;
        EngineTransform identity;
        EncodeBeginClipPath(x, y, commands, 9, FillRule::NonZero, identity, blend, alpha);
    }

    void EncodeEndClip()
    {
        if (scene_.numOpenClips == 0) return;
        packedDirty_ = true;
        scene_.drawTags.push_back(kVelloDrawTagEndClip);
        // Dummy path for the end-clip draw object.
        scene_.pathTags.push_back(kVelloPathTagPath);
        scene_.numPaths++;
        scene_.numClips++;
        scene_.numOpenClips--;
    }

    // Blurred rounded rectangle (upstream draw_blurred_rounded_rect).
    bool EncodeBlurredRoundedRect(float x, float y, float w, float h, float cornerRadius,
                                  float stdDev, float r, float g, float b, float a,
                                  const EngineTransform& transform)
    {
        EnsureScissorClip();
        packedDirty_ = true;
        VelloTransform t = VelloTransform::FromEngine(transform);
        EncodeTransform(t);
        EncodeStyle(VelloStyle::Fill(FillRule::NonZero));
        // Cover the blur kernel extent.
        float k = 2.5f * stdDev;
        VelloPathEncoder enc(scene_.pathTags, scene_.pathData, scene_.numPathSegments,
                             scene_.numPaths, true);
        enc.MoveTo(x - k, y - k);
        enc.LineTo(x + w + k, y - k);
        enc.LineTo(x + w + k, y + h + k);
        enc.LineTo(x - k, y + h + k);
        enc.Close();
        if (enc.Finish(true) == 0) return true;
        AccumulateLocalBox(x - k, y - k, x + w + k, y + h + k, t, 1.0f);
        // The fine shader evaluates the rect centered at the local origin, so
        // the brush transform recenters it.
        VelloTransform center = VelloTransform::Identity();
        center.translation[0] = x + w * 0.5f;
        center.translation[1] = y + h * 0.5f;
        if (EncodeTransform(t.Mul(center))) SwapLastPathTags();
        scene_.drawTags.push_back(kVelloDrawTagBlurRect);
        scene_.numInfoWords += VelloDrawTagInfoSize(kVelloDrawTagBlurRect);
        scene_.drawData.push_back(VelloPackPremulColor(r, g, b, a));
        PushDrawDataF32(w);
        PushDrawDataF32(h);
        PushDrawDataF32(cornerRadius);
        PushDrawDataF32(stdDev);
        return true;
    }

    // ------------------------------------------------------------------
    // Finalize / queries
    // ------------------------------------------------------------------

    void Finalize()
    {
        CloseScissorClip();
        // Force-close any unbalanced clip layers so the GPU clip stack stays
        // consistent (upstream appends synthetic END_CLIPs at resolve time).
        while (scene_.numOpenClips > 0) {
            EncodeEndClip();
        }
    }

    const VelloScene& scene() const { return scene_; }
    bool HasWork() const { return !scene_.drawTags.empty(); }
    uint32_t PathCount() const { return scene_.numPaths; }
    uint32_t TilesX() const { return tilesX_; }
    uint32_t TilesY() const { return tilesY_; }

    // ------------------------------------------------------------------
    // Resolve: pack the six streams into the single scene u32 buffer
    // (resolve.rs layout order) and derive config + dispatch sizes.
    // ------------------------------------------------------------------

    const VelloPackedScene& Pack()
    {
        if (!packedDirty_) return packed_;
        packedDirty_ = false;
        VelloPackedScene& p = packed_;
        p.data.clear();

        const VelloScene& s = scene_;
        p.region = ComputeRegion();
        uint32_t nPathTagBytes = (uint32_t)s.pathTags.size();
        uint32_t pathTagPaddedBytes = VelloAlignUp(std::max(nPathTagBytes, 1u), kVelloPathTagAlign);

        p.pathTagBase = 0;
        p.pathDataBase = pathTagPaddedBytes / 4;
        p.drawTagBase = p.pathDataBase + (uint32_t)s.pathData.size();
        p.drawDataBase = p.drawTagBase + (uint32_t)s.drawTags.size();
        p.transformBase = p.drawDataBase + (uint32_t)s.drawData.size();
        p.styleBase = p.transformBase + (uint32_t)s.transforms.size() * 6;
        p.numPathTagBytes = nPathTagBytes;

        uint32_t total = p.styleBase + (uint32_t)s.styles.size() * 2;
        p.data.assign(total, 0u);

        // path tags (zero-padded; tag 0 is a no-op)
        if (!s.pathTags.empty()) {
            std::memcpy(p.data.data(), s.pathTags.data(), s.pathTags.size());
        }
        if (!s.pathData.empty()) {
            std::memcpy(p.data.data() + p.pathDataBase, s.pathData.data(),
                        s.pathData.size() * 4);
        }
        if (!s.drawTags.empty()) {
            std::memcpy(p.data.data() + p.drawTagBase, s.drawTags.data(), s.drawTags.size() * 4);
        }
        if (!s.drawData.empty()) {
            std::memcpy(p.data.data() + p.drawDataBase, s.drawData.data(), s.drawData.size() * 4);
        }
        if (!s.transforms.empty()) {
            std::memcpy(p.data.data() + p.transformBase, s.transforms.data(),
                        s.transforms.size() * sizeof(VelloTransform));
            // Rebase every transform so the sub-scene renders into a
            // region-sized target whose top-left is region.origin. Path data
            // is local-space, so only the translations move.
            if (p.region.originX != 0 || p.region.originY != 0) {
                float ox = (float)p.region.originX;
                float oy = (float)p.region.originY;
                auto* xf = reinterpret_cast<VelloTransform*>(p.data.data() + p.transformBase);
                for (size_t i = 0; i < s.transforms.size(); i++) {
                    xf[i].translation[0] -= ox;
                    xf[i].translation[1] -= oy;
                }
            }
        }
        if (!s.styles.empty()) {
            std::memcpy(p.data.data() + p.styleBase, s.styles.data(),
                        s.styles.size() * sizeof(VelloStyle));
        }
        return p;
    }

    // Computes the per-dispatch configuration. Call after Finalize()+Pack().
    VelloRenderInfo BuildRenderInfo() const
    {
        const VelloScene& s = scene_;
        const VelloPackedScene& p = packed_;
        VelloRenderInfo ri;

        // Render only the region this sub-scene covers (transforms in the
        // packed stream are already rebased by -region.origin).
        uint32_t targetW = p.region.Empty() ? scene_.viewportW : p.region.width;
        uint32_t targetH = p.region.Empty() ? scene_.viewportH : p.region.height;
        uint32_t widthInTiles = VelloDivCeil(targetW, kVelloTileWidth);
        uint32_t heightInTiles = VelloDivCeil(targetH, kVelloTileHeight);
        uint32_t nPaths = s.numPaths;
        uint32_t nDrawObj = (uint32_t)s.drawTags.size();
        uint32_t nClip = s.numClips;

        uint32_t pathTagPadded = VelloAlignUp(std::max(p.numPathTagBytes, 1u), kVelloPathTagAlign);
        uint32_t pathTagWgs = pathTagPadded / kVelloPathTagAlign;
        uint32_t reducedSize = VelloAlignUp(std::max(pathTagWgs, 1u), kVelloWorkgroupSize);

        ri.useLargePathScan = pathTagWgs > kVelloWorkgroupSize;
        ri.pathtagReduceWgs = pathTagWgs;
        ri.pathtagScan1Wgs = reducedSize / kVelloWorkgroupSize;
        ri.pathtagReduce2Wgs = ri.pathtagScan1Wgs;
        ri.pathtagScanWgs = pathTagWgs;
        ri.bboxClearWgs = VelloDivCeil(std::max(nDrawObj, 1u), kVelloWorkgroupSize);
        ri.flattenWgs = VelloDivCeil(std::max(p.numPathTagBytes, 1u), kVelloWorkgroupSize);
        ri.drawReduceWgs = std::min(VelloDivCeil(std::max(nDrawObj, 1u), kVelloWorkgroupSize),
                                    kVelloWorkgroupSize);
        ri.clipReduceWgs = (nClip > 0) ? (nClip - 1) / kVelloWorkgroupSize : 0;
        ri.clipLeafWgs = VelloDivCeil(nClip, kVelloWorkgroupSize);
        ri.binningWgs = VelloDivCeil(std::max(nDrawObj, 1u), kVelloWorkgroupSize);
        ri.tileAllocWgs = VelloDivCeil(std::max(nPaths, 1u), kVelloWorkgroupSize);
        ri.backdropWgs = VelloDivCeil(std::max(nPaths, 1u), kVelloWorkgroupSize);
        ri.widthInBins = VelloDivCeil(widthInTiles, kVelloNTileX);
        ri.heightInBins = VelloDivCeil(heightInTiles, kVelloNTileY);

        // Buffer sizes (config.rs::BufferSizes, min 1 element).
        auto atLeast1 = [](uint32_t v) { return std::max(v, 1u); };
        ri.reducedSize = reducedSize;
        ri.reduced2Size = kVelloWorkgroupSize;
        ri.reducedScanSize = reducedSize;
        ri.tagMonoidsSize = atLeast1(pathTagWgs * kVelloWorkgroupSize);
        ri.pathBboxSize = atLeast1(nPaths);
        ri.drawReducedSize = atLeast1(ri.drawReduceWgs);
        ri.drawMonoidSize = atLeast1(nDrawObj);
        ri.clipInpSize = atLeast1(nClip);
        ri.clipBicSize = atLeast1(nClip / kVelloWorkgroupSize);
        ri.clipElSize = atLeast1(nClip);
        ri.clipBboxSize = atLeast1(nClip);
        ri.drawBboxSize = atLeast1(nDrawObj);
        uint32_t nBins = ri.widthInBins * ri.heightInBins;
        uint32_t alignedNBins = VelloAlignUp(std::max(nBins, 1u), kVelloNTile);
        ri.binHeaderSize = atLeast1(ri.binningWgs * alignedNBins);
        ri.pathSize = VelloAlignUp(atLeast1(nPaths), kVelloWorkgroupSize);

        // Bump-allocated buffers -- upstream's hand-picked defaults, with the
        // combined info+bin_data buffer grown when the info stream is large.
        constexpr uint32_t kBinDataDefault = 1u << 18;
        constexpr uint32_t kTilesDefault = 1u << 21;
        constexpr uint32_t kLinesDefault = 1u << 21;
        constexpr uint32_t kSegCountsDefault = 1u << 21;
        constexpr uint32_t kSegmentsDefault = 1u << 21;
        constexpr uint32_t kBlendDefault = 1u << 20;
        constexpr uint32_t kPtclDefault = 1u << 23;

        uint32_t binDataStart = s.numInfoWords;
        uint32_t infoBinData = kBinDataDefault;
        if (binDataStart > kBinDataDefault / 2) {
            infoBinData = VelloAlignUp(binDataStart * 2, kVelloNTile);
        }
        ri.infoBinDataSize = infoBinData;
        ri.tileSize = kTilesDefault;
        ri.lineSoupSize = kLinesDefault;
        ri.segCountSize = kSegCountsDefault;
        ri.segmentSize = kSegmentsDefault;
        ri.blendSpillSize = kBlendDefault;
        ri.ptclSize = std::max(kPtclDefault,
                               widthInTiles * heightInTiles * kVelloPtclInitialAlloc +
                                   (1u << 16));

        VelloConfig& c = ri.config;
        c.width_in_tiles = widthInTiles;
        c.height_in_tiles = heightInTiles;
        c.target_width = targetW;
        c.target_height = targetH;
        c.base_color = 0;  // transparent
        c.n_drawobj = nDrawObj;
        c.n_path = nPaths;
        c.n_clip = nClip;
        c.bin_data_start = binDataStart;
        c.pathtag_base = p.pathTagBase;
        c.pathdata_base = p.pathDataBase;
        c.drawtag_base = p.drawTagBase;
        c.drawdata_base = p.drawDataBase;
        c.transform_base = p.transformBase;
        c.style_base = p.styleBase;
        c.lines_size = ri.lineSoupSize;
        c.binning_size = ri.infoBinDataSize - binDataStart;
        c.tiles_size = ri.tileSize;
        c.seg_counts_size = ri.segCountSize;
        c.segments_size = ri.segmentSize;
        c.blend_size = ri.blendSpillSize;
        c.ptcl_size = ri.ptclSize;
        return ri;
    }

    // ------------------------------------------------------------------
    // Mid-frame sub-scene cut (painter-order interleaving)
    // ------------------------------------------------------------------
    // Seals everything encoded so far into `out` (finalize -> pack -> render
    // info, then MOVES the packed stream and ramp texels out) and re-opens the
    // encoder for the rest of the frame at the same viewport. Returns false
    // (and leaves the encoder untouched) when nothing was encoded. The sticky
    // scissor mirror lives in the ENGINE, not here — the caller must re-apply
    // its scissor/clip state after a successful cut, because the finalize
    // closes the scissor clip layer and BeginFrame clears hasScissor_.
    bool CutSubScene(VelloSubScene& out)
    {
        if (scene_.drawTags.empty()) return false;
        Finalize();
        Pack();
        out.ri = BuildRenderInfo();
        out.packed = std::move(packed_);
        packed_ = VelloPackedScene {};
        out.rampData = std::move(scene_.rampData);
        scene_.rampData.clear();
        out.rampCount = scene_.rampCount;
        out.viewportW = scene_.viewportW;
        out.viewportH = scene_.viewportH;
        BeginFrame(scene_.viewportW, scene_.viewportH);
        return true;
    }

    // Per-primitive device-space boxes: every backend Encode* wrapper brackets
    // one primitive with Begin/EndPrimitiveBox, giving the overlap gate ITEM
    // granularity. Icon rows interleave path icons with rects/text; against the
    // single merged bbox everything on the row "overlaps", against the item
    // boxes almost nothing does. Overflow (>kMaxPrimBoxes) degrades to the
    // merged-bbox answer, which is conservative (more flushes, never wrong).
    void BeginPrimitiveBox()
    {
        primMinX_ = 1e30f; primMinY_ = 1e30f;
        primMaxX_ = -1e30f; primMaxY_ = -1e30f;
    }

    void EndPrimitiveBox(bool encoded)
    {
        if (!encoded) return;
        if (!(primMinX_ <= primMaxX_ && primMinY_ <= primMaxY_)) return;
        if (primBoxCount_ < kMaxPrimBoxes) {
            primBoxes_[primBoxCount_][0] = primMinX_;
            primBoxes_[primBoxCount_][1] = primMinY_;
            primBoxes_[primBoxCount_][2] = primMaxX_;
            primBoxes_[primBoxCount_][3] = primMaxY_;
            primBoxCount_++;
        } else {
            primBoxOverflow_ = true;
        }
    }

    // Does the pending content actually touch the device rect? Tests the
    // per-primitive boxes when available; falls back to the merged bbox on
    // overflow or when a primitive was encoded outside a Begin/End bracket.
    bool PendingHitsDeviceRect(float x0, float y0, float x1, float y1) const
    {
        if (!(bboxMinX_ <= bboxMaxX_ && bboxMinY_ <= bboxMaxY_)) return true;
        if (x0 >= bboxMaxX_ || x1 <= bboxMinX_ ||
            y0 >= bboxMaxY_ || y1 <= bboxMinY_) {
            return false;
        }
        if (primBoxOverflow_ || primBoxCount_ == 0) return true;
        for (uint32_t i = 0; i < primBoxCount_; i++) {
            if (x0 < primBoxes_[i][2] && x1 > primBoxes_[i][0] &&
                y0 < primBoxes_[i][3] && y1 > primBoxes_[i][1]) {
                return true;
            }
        }
        return false;
    }

    // Raw device-space bbox of everything encoded so far (pre tile alignment).
    // False when nothing bbox-accumulating was encoded yet. Callers use it to
    // decide whether an incoming NON-path draw actually overlaps the pending
    // sub-scene (disjoint -> painter order between them is irrelevant -> no
    // flush needed).
    bool PendingDeviceBounds(float& x0, float& y0, float& x1, float& y1) const
    {
        if (!(bboxMinX_ <= bboxMaxX_ && bboxMinY_ <= bboxMaxY_)) return false;
        x0 = bboxMinX_; y0 = bboxMinY_; x1 = bboxMaxX_; y1 = bboxMaxY_;
        return true;
    }

    // Tile-aligned device-space region this sub-scene covers, clamped to the
    // viewport. Empty when nothing was encoded.
    VelloRenderRegion ComputeRegion() const
    {
        VelloRenderRegion r;
        if (!(bboxMinX_ <= bboxMaxX_ && bboxMinY_ <= bboxMaxY_)) return r;

        float vw = (float)scene_.viewportW;
        float vh = (float)scene_.viewportH;
        float minX = std::fmax(0.0f, std::fmin(bboxMinX_, vw));
        float minY = std::fmax(0.0f, std::fmin(bboxMinY_, vh));
        float maxX = std::fmax(0.0f, std::fmin(bboxMaxX_, vw));
        float maxY = std::fmax(0.0f, std::fmin(bboxMaxY_, vh));
        if (!(maxX > minX && maxY > minY)) return r;

        uint32_t x0 = ((uint32_t)minX / kVelloTileWidth) * kVelloTileWidth;
        uint32_t y0 = ((uint32_t)minY / kVelloTileHeight) * kVelloTileHeight;
        uint32_t x1 = VelloAlignUp((uint32_t)std::ceil(maxX), kVelloTileWidth);
        uint32_t y1 = VelloAlignUp((uint32_t)std::ceil(maxY), kVelloTileHeight);
        x1 = std::min(x1, VelloAlignUp(scene_.viewportW, kVelloTileWidth));
        y1 = std::min(y1, VelloAlignUp(scene_.viewportH, kVelloTileHeight));
        if (x1 <= x0 || y1 <= y0) return r;

        r.originX = x0;
        r.originY = y0;
        r.width = x1 - x0;
        r.height = y1 - y0;
        return r;
    }

private:
    // ------------------------------------------------------------------
    // Sub-scene device-space bounding box
    // ------------------------------------------------------------------

    void ResetBbox()
    {
        bboxMinX_ = 1e30f;
        bboxMinY_ = 1e30f;
        bboxMaxX_ = -1e30f;
        bboxMaxY_ = -1e30f;
        primBoxCount_ = 0;
        primBoxOverflow_ = false;
        primMinX_ = 1e30f; primMinY_ = 1e30f;
        primMaxX_ = -1e30f; primMaxY_ = -1e30f;
    }

    void AccumulateDevicePoint(float x, float y)
    {
        bboxMinX_ = std::fmin(bboxMinX_, x);
        bboxMinY_ = std::fmin(bboxMinY_, y);
        bboxMaxX_ = std::fmax(bboxMaxX_, x);
        bboxMaxY_ = std::fmax(bboxMaxY_, y);
        primMinX_ = std::fmin(primMinX_, x);
        primMinY_ = std::fmin(primMinY_, y);
        primMaxX_ = std::fmax(primMaxX_, x);
        primMaxY_ = std::fmax(primMaxY_, y);
    }

    // Unions the device-space bounds of a local-space axis-aligned box by
    // transforming its four corners. `pad` widens the result (stroke half
    // width, blur kernel) in device space.
    void AccumulateLocalBox(float lx0, float ly0, float lx1, float ly1,
                            const VelloTransform& t, float pad)
    {
        const float xs[4] = {lx0, lx1, lx0, lx1};
        const float ys[4] = {ly0, ly0, ly1, ly1};
        for (int i = 0; i < 4; i++) {
            float dx, dy;
            t.Apply(xs[i], ys[i], dx, dy);
            AccumulateDevicePoint(dx - pad, dy - pad);
            AccumulateDevicePoint(dx + pad, dy + pad);
        }
    }

    // Conservative local-space bounds of a Jalium command stream: the control
    // point hull (which contains the curves) plus the implicit start point.
    static void CommandHullBounds(float startX, float startY, const float* commands,
                                  uint32_t commandLength, float& x0, float& y0, float& x1,
                                  float& y1)
    {
        x0 = x1 = startX;
        y0 = y1 = startY;
        auto add = [&](float x, float y) {
            x0 = std::fmin(x0, x);
            y0 = std::fmin(y0, y);
            x1 = std::fmax(x1, x);
            y1 = std::fmax(y1, y);
        };
        uint32_t i = 0;
        while (i < commandLength) {
            int tag = (int)commands[i];
            switch (tag) {
                case kTagMoveTo:
                case kTagLineTo:
                    if (i + 2 >= commandLength) return;
                    add(commands[i + 1], commands[i + 2]);
                    i += 3;
                    break;
                case kTagQuadTo:
                    if (i + 4 >= commandLength) return;
                    add(commands[i + 1], commands[i + 2]);
                    add(commands[i + 3], commands[i + 4]);
                    i += 5;
                    break;
                case kTagCubicTo:
                    if (i + 6 >= commandLength) return;
                    add(commands[i + 1], commands[i + 2]);
                    add(commands[i + 3], commands[i + 4]);
                    add(commands[i + 5], commands[i + 6]);
                    i += 7;
                    break;
                case kTagArcTo: {
                    if (i + 7 >= commandLength) return;
                    // Conservative: the arc stays within |r| of its endpoints.
                    float ex = commands[i + 1], ey = commands[i + 2];
                    float rx = std::fabs(commands[i + 3]), ry = std::fabs(commands[i + 4]);
                    float rr = std::fmax(rx, ry) * 2.0f;
                    add(ex - rr, ey - rr);
                    add(ex + rr, ey + rr);
                    i += 8;
                    break;
                }
                case kTagClosePath:
                    i += 1;
                    break;
                default:
                    return;
            }
        }
    }

    // Stroke bounds: control hull plus half the stroke width, widened for
    // miter spikes and round joins/caps, all in device space.
    void AccumulateStrokeBounds(float startX, float startY, const float* commands,
                                uint32_t commandLength, float strokeWidth, float miterLimit,
                                const VelloTransform& t)
    {
        float bx0, by0, bx1, by1;
        CommandHullBounds(startX, startY, commands, commandLength, bx0, by0, bx1, by1);
        float half = 0.5f * strokeWidth * TransformScale(t);
        float pad = half * std::fmax(1.0f, std::fmin(miterLimit, 10.0f)) + 1.0f;
        AccumulateLocalBox(bx0, by0, bx1, by1, t, pad);
    }

    // Device-space scale factor used to widen local-space stroke padding.
    static float TransformScale(const VelloTransform& t)
    {
        float sx = std::sqrt(t.matrix[0] * t.matrix[0] + t.matrix[1] * t.matrix[1]);
        float sy = std::sqrt(t.matrix[2] * t.matrix[2] + t.matrix[3] * t.matrix[3]);
        return std::fmax(sx, sy);
    }

    // ------------------------------------------------------------------
    // Stream helpers
    // ------------------------------------------------------------------

    void PushDrawDataF32(float v)
    {
        uint32_t bits;
        std::memcpy(&bits, &v, 4);
        scene_.drawData.push_back(bits);
    }

    // Dedup against the last encoded value (encoding.rs::encode_transform).
    // Returns true if a TRANSFORM tag was pushed.
    bool EncodeTransform(const VelloTransform& t)
    {
        if (scene_.transforms.empty() || !(scene_.transforms.back() == t)) {
            scene_.pathTags.push_back(kVelloPathTagTransform);
            scene_.transforms.push_back(t);
            return true;
        }
        return false;
    }

    bool EncodeStyle(const VelloStyle& s)
    {
        if (scene_.styles.empty() || !(scene_.styles.back() == s)) {
            scene_.pathTags.push_back(kVelloPathTagStyle);
            scene_.styles.push_back(s);
            return true;
        }
        return false;
    }

    // Swap the trailing [PATH, TRANSFORM] tags to [TRANSFORM, PATH] so the
    // brush transform (not the geometry transform) binds to the paint
    // (encoding.rs::swap_last_path_tags; load-bearing for gradients/blur).
    void SwapLastPathTags()
    {
        size_t n = scene_.pathTags.size();
        if (n >= 2) std::swap(scene_.pathTags[n - 1], scene_.pathTags[n - 2]);
    }

    void EncodeEmptyShape()
    {
        VelloPathEncoder enc(scene_.pathTags, scene_.pathData, scene_.numPathSegments,
                             scene_.numPaths, true);
        enc.EmptyPath();
        enc.Finish(true);
    }

    // Parse the Jalium float command stream into the path encoder.
    // Returns the number of encoded segments.
    uint32_t EncodePathCommands(float startX, float startY, const float* commands,
                                uint32_t commandLength, bool isFill)
    {
        VelloPathEncoder enc(scene_.pathTags, scene_.pathData, scene_.numPathSegments,
                             scene_.numPaths, isFill);
        enc.MoveTo(startX, startY);
        float curX = startX, curY = startY;
        uint32_t i = 0;
        while (i < commandLength) {
            int tag = (int)commands[i];
            switch (tag) {
                case kTagMoveTo:
                    if (i + 2 >= commandLength) return enc.Finish(true);
                    curX = commands[i + 1];
                    curY = commands[i + 2];
                    enc.MoveTo(curX, curY);
                    i += 3;
                    break;
                case kTagLineTo:
                    if (i + 2 >= commandLength) return enc.Finish(true);
                    curX = commands[i + 1];
                    curY = commands[i + 2];
                    enc.LineTo(curX, curY);
                    i += 3;
                    break;
                case kTagQuadTo:
                    if (i + 4 >= commandLength) return enc.Finish(true);
                    enc.QuadTo(commands[i + 1], commands[i + 2], commands[i + 3],
                               commands[i + 4]);
                    curX = commands[i + 3];
                    curY = commands[i + 4];
                    i += 5;
                    break;
                case kTagCubicTo:
                    if (i + 6 >= commandLength) return enc.Finish(true);
                    enc.CubicTo(commands[i + 1], commands[i + 2], commands[i + 3],
                                commands[i + 4], commands[i + 5], commands[i + 6]);
                    curX = commands[i + 5];
                    curY = commands[i + 6];
                    i += 7;
                    break;
                case kTagArcTo: {
                    if (i + 7 >= commandLength) return enc.Finish(true);
                    float ex = commands[i + 1], ey = commands[i + 2];
                    float rx = commands[i + 3], ry = commands[i + 4];
                    float rotDeg = commands[i + 5];
                    bool largeArc = commands[i + 6] != 0.0f;
                    bool sweep = commands[i + 7] != 0.0f;
                    EncodeSvgArcAsCubics(enc, curX, curY, ex, ey, rx, ry, rotDeg, largeArc,
                                         sweep);
                    curX = ex;
                    curY = ey;
                    i += 8;
                    break;
                }
                case kTagClosePath:
                    enc.Close();
                    i += 1;
                    break;
                default:
                    return enc.Finish(true);
            }
        }
        return enc.Finish(true);
    }

    // SVG arc -> cubic Beziers (endpoint to center parameterization, split at
    // <= 90 degrees per segment, (4/3)tan(theta/4) control distance). The GPU
    // flattens the cubics adaptively.
    static void EncodeSvgArcAsCubics(VelloPathEncoder& enc, float startX, float startY,
                                     float endX, float endY, float rx, float ry,
                                     float xAxisRotationDegrees, bool largeArc, bool sweep)
    {
        constexpr float kPi = 3.14159265358979323846f;
        constexpr float kEps = 1e-5f;
        if ((std::fabs(startX - endX) < kEps && std::fabs(startY - endY) < kEps) || rx <= kEps ||
            ry <= kEps) {
            enc.LineTo(endX, endY);
            return;
        }
        rx = std::fabs(rx);
        ry = std::fabs(ry);
        float phi = xAxisRotationDegrees * kPi / 180.0f;
        float cosPhi = std::cos(phi), sinPhi = std::sin(phi);
        float dx = (startX - endX) * 0.5f, dy = (startY - endY) * 0.5f;
        float x1p = cosPhi * dx + sinPhi * dy;
        float y1p = -sinPhi * dx + cosPhi * dy;
        float rxSq = rx * rx, rySq = ry * ry;
        float x1pSq = x1p * x1p, y1pSq = y1p * y1p;
        float radiiCheck = x1pSq / rxSq + y1pSq / rySq;
        if (radiiCheck > 1.0f) {
            float scale = std::sqrt(radiiCheck);
            rx *= scale;
            ry *= scale;
            rxSq = rx * rx;
            rySq = ry * ry;
        }
        float num = rxSq * rySq - rxSq * y1pSq - rySq * x1pSq;
        float den = rxSq * y1pSq + rySq * x1pSq;
        float sign = (largeArc == sweep) ? -1.0f : 1.0f;
        float factor = (den <= kEps) ? 0.0f : sign * std::sqrt(std::fmax(0.0f, num / den));
        float cxp = factor * (rx * y1p / ry);
        float cyp = factor * (-ry * x1p / rx);
        float cx = cosPhi * cxp - sinPhi * cyp + (startX + endX) * 0.5f;
        float cy = sinPhi * cxp + cosPhi * cyp + (startY + endY) * 0.5f;
        auto vecAngle = [](float ux, float uy, float vx, float vy) {
            float dotv = ux * vx + uy * vy;
            float lenp = std::sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            if (lenp <= 0.0f) return 0.0f;
            float c = std::fmin(std::fmax(dotv / lenp, -1.0f), 1.0f);
            float ang = std::acos(c);
            return (ux * vy - uy * vx < 0.0f) ? -ang : ang;
        };
        float v1x = (x1p - cxp) / rx, v1y = (y1p - cyp) / ry;
        float v2x = (-x1p - cxp) / rx, v2y = (-y1p - cyp) / ry;
        float startAngle = vecAngle(1.0f, 0.0f, v1x, v1y);
        float deltaAngle = vecAngle(v1x, v1y, v2x, v2y);
        if (!sweep && deltaAngle > 0.0f) deltaAngle -= 2.0f * kPi;
        else if (sweep && deltaAngle < 0.0f) deltaAngle += 2.0f * kPi;

        int nSegs = (int)std::ceil(std::fabs(deltaAngle) / (kPi * 0.5f));
        nSegs = std::max(nSegs, 1);
        float segAngle = deltaAngle / (float)nSegs;
        float kappa = (4.0f / 3.0f) * std::tan(segAngle * 0.25f);
        float angle = startAngle;
        for (int s = 0; s < nSegs; s++) {
            float a0 = angle;
            float a1 = angle + segAngle;
            float c0 = std::cos(a0), s0 = std::sin(a0);
            float c1 = std::cos(a1), s1 = std::sin(a1);
            // Points/derivatives on the unit circle, mapped through the
            // ellipse radii and rotation.
            auto mapPoint = [&](float ux, float uy, float& ox, float& oy) {
                float ex2 = rx * ux, ey2 = ry * uy;
                ox = cosPhi * ex2 - sinPhi * ey2 + cx;
                oy = sinPhi * ex2 + cosPhi * ey2 + cy;
            };
            auto mapDeriv = [&](float ux, float uy, float& ox, float& oy) {
                float ex2 = rx * ux, ey2 = ry * uy;
                ox = cosPhi * ex2 - sinPhi * ey2;
                oy = sinPhi * ex2 + cosPhi * ey2;
            };
            float p0x, p0y, p3x, p3y, d0x, d0y, d1x, d1y;
            mapPoint(c0, s0, p0x, p0y);
            mapPoint(c1, s1, p3x, p3y);
            mapDeriv(-s0, c0, d0x, d0y);
            mapDeriv(-s1, c1, d1x, d1y);
            float p1x = p0x + kappa * d0x, p1y = p0y + kappa * d0y;
            float p2x = p3x - kappa * d1x, p2y = p3y - kappa * d1y;
            if (s == nSegs - 1) {
                p3x = endX;
                p3y = endY;
            }
            enc.CubicTo(p1x, p1y, p2x, p2y, p3x, p3y);
            angle = a1;
        }
    }

    // ------------------------------------------------------------------
    // Brush encoding (encoding.rs::encode_brush + ramp resolution)
    // ------------------------------------------------------------------

    void EncodeSolid(float r, float g, float b, float a)
    {
        scene_.drawTags.push_back(kVelloDrawTagColor);
        scene_.numInfoWords += VelloDrawTagInfoSize(kVelloDrawTagColor);
        scene_.drawData.push_back(VelloPackPremulColor(r, g, b, a));
    }

    void EncodeBrush(const EngineBrushData& brush, float opacity, const VelloTransform& pathT)
    {
        switch (brush.type) {
            case 1:
                EncodeLinearGradient(brush, opacity);
                return;
            case 2:
                EncodeRadialGradient(brush, opacity, pathT);
                return;
            case 3:
                EncodeSweepGradient(brush, opacity);
                return;
            default:
                EncodeSolid(brush.r, brush.g, brush.b, brush.a * opacity);
                return;
        }
    }

    void EncodeLinearGradient(const EngineBrushData& brush, float opacity)
    {
        uint32_t rampId;
        if (!ResolveGradientStops(brush, opacity, rampId)) return;  // degenerate handled
        scene_.drawTags.push_back(kVelloDrawTagLinearGradient);
        scene_.numInfoWords += VelloDrawTagInfoSize(kVelloDrawTagLinearGradient);
        scene_.drawData.push_back((rampId << 2) | ExtendBits(brush));
        PushDrawDataF32(brush.startX);
        PushDrawDataF32(brush.startY);
        PushDrawDataF32(brush.endX);
        PushDrawDataF32(brush.endY);
    }

    void EncodeRadialGradient(const EngineBrushData& brush, float opacity,
                              const VelloTransform& pathT)
    {
        constexpr float kGradientEps = 1.0f / (1 << 12);
        float rx = brush.radiusX, ry = brush.radiusY;
        float cxv = brush.centerX, cyv = brush.centerY;
        float ox = brush.originX, oy = brush.originY;
        bool hasFocal = (std::fabs(ox - cxv) > 1e-6f || std::fabs(oy - cyv) > 1e-6f);
        float p0x = hasFocal ? ox : cxv;
        float p0y = hasFocal ? oy : cyv;
        float r0 = 0.0f;
        float r1 = rx;
        // Degenerate: same center/radii => transparent (upstream behavior).
        if (!hasFocal && std::fabs(r0 - r1) < kGradientEps) {
            EncodeSolid(0, 0, 0, 0);
            return;
        }
        uint32_t rampId;
        if (!ResolveGradientStops(brush, opacity, rampId)) return;

        // Elliptical radii: synthesize a brush transform scaling y around the
        // center so the GPU sees a circular gradient of radius rx.
        bool elliptical = std::fabs(rx - ry) > 1e-6f && rx > 1e-6f && ry > 1e-6f;
        if (elliptical) {
            VelloTransform bt = VelloTransform::Identity();
            float sy = ry / rx;
            // translate(c) * scale(1, sy) * translate(-c)
            bt.matrix[3] = sy;
            bt.translation[1] = cyv - sy * cyv;
            if (EncodeTransform(pathT.Mul(bt))) SwapLastPathTags();
            // Focal point in brush space: invert the y scaling.
            p0y = cyv + (p0y - cyv) / sy;
        }

        scene_.drawTags.push_back(kVelloDrawTagRadialGradient);
        scene_.numInfoWords += VelloDrawTagInfoSize(kVelloDrawTagRadialGradient);
        scene_.drawData.push_back((rampId << 2) | ExtendBits(brush));
        PushDrawDataF32(p0x);
        PushDrawDataF32(p0y);
        PushDrawDataF32(cxv);
        PushDrawDataF32(cyv);
        PushDrawDataF32(r0);
        PushDrawDataF32(r1);
    }

    void EncodeSweepGradient(const EngineBrushData& brush, float opacity)
    {
        constexpr float kSweepEps = 1.0f / (1 << 15);
        float t0 = brush.startX;
        float t1 = brush.endX;
        if (std::fabs(t0 - t1) < kSweepEps) {
            EncodeSolid(0, 0, 0, 0);
            return;
        }
        uint32_t rampId;
        if (!ResolveGradientStops(brush, opacity, rampId)) return;
        scene_.drawTags.push_back(kVelloDrawTagSweepGradient);
        scene_.numInfoWords += VelloDrawTagInfoSize(kVelloDrawTagSweepGradient);
        scene_.drawData.push_back((rampId << 2) | ExtendBits(brush));
        PushDrawDataF32(brush.centerX);
        PushDrawDataF32(brush.centerY);
        PushDrawDataF32(t0);
        PushDrawDataF32(t1);
    }

    static uint32_t ExtendBits(const EngineBrushData& brush)
    {
        return brush.spreadMethod <= 2 ? brush.spreadMethod : kVelloExtendPad;
    }

    // Builds (or reuses) the 512-texel premultiplied ramp for the brush.
    // Returns false when the gradient degenerates to a solid/transparent fill
    // (already encoded here).
    bool ResolveGradientStops(const EngineBrushData& brush, float opacity, uint32_t& rampId)
    {
        if (brush.stopCount == 0 || brush.stops == nullptr) {
            EncodeSolid(0, 0, 0, 0);
            return false;
        }
        if (brush.stopCount == 1) {
            const auto& s0 = brush.stops[0];
            EncodeSolid(s0.r, s0.g, s0.b, s0.a * opacity);
            return false;
        }
        // Content hash for per-frame dedup.
        uint64_t h = 1469598103934665603ull;
        auto mix = [&h](uint32_t v) {
            h ^= v;
            h *= 1099511628211ull;
        };
        for (uint32_t i = 0; i < brush.stopCount; i++) {
            const auto& st = brush.stops[i];
            uint32_t b[5];
            std::memcpy(&b[0], &st.position, 4);
            std::memcpy(&b[1], &st.r, 4);
            std::memcpy(&b[2], &st.g, 4);
            std::memcpy(&b[3], &st.b, 4);
            std::memcpy(&b[4], &st.a, 4);
            for (uint32_t w : b) mix(w);
        }
        uint32_t opBits;
        std::memcpy(&opBits, &opacity, 4);
        mix(opBits);

        for (const auto& e : rampHashes_) {
            if (e.hash == h) {
                rampId = e.id;
                return true;
            }
        }
        if (scene_.rampCount >= kMaxRamps) {
            // Ramp budget exhausted: approximate with the first stop.
            const auto& s0 = brush.stops[0];
            EncodeSolid(s0.r, s0.g, s0.b, s0.a * opacity);
            return false;
        }

        rampId = scene_.rampCount++;
        size_t base = scene_.rampData.size();
        scene_.rampData.resize(base + kVelloRampWidth);
        uint32_t stopIx = 0;
        for (uint32_t i = 0; i < kVelloRampWidth; i++) {
            float u = (float)i / (float)(kVelloRampWidth - 1);
            while (stopIx + 1 < brush.stopCount && u > brush.stops[stopIx + 1].position) {
                stopIx++;
            }
            const auto& a = brush.stops[stopIx];
            const auto& b =
                brush.stops[std::min(stopIx + 1, brush.stopCount - 1)];
            float t;
            float du = b.position - a.position;
            if (du < 1e-9f) {
                t = (u <= a.position) ? 0.0f : 1.0f;
            } else {
                t = std::fmin(std::fmax((u - a.position) / du, 0.0f), 1.0f);
            }
            float cr = a.r + (b.r - a.r) * t;
            float cg = a.g + (b.g - a.g) * t;
            float cb = a.b + (b.b - a.b) * t;
            float ca = (a.a + (b.a - a.a) * t) * opacity;
            scene_.rampData[base + i] = VelloPackPremulColor(cr, cg, cb, ca);
        }
        rampHashes_.push_back({h, rampId});
        return true;
    }

    // ------------------------------------------------------------------
    // Dashing (CPU-side, like upstream's kurbo::dash) -- flattens the path,
    // splits it into "on" runs and encodes each as an open polyline stroke.
    // ------------------------------------------------------------------

    bool EncodeDashedStroke(float startX, float startY, const float* commands,
                            uint32_t commandLength, const EngineBrushData& brush,
                            float strokeWidth, int32_t lineJoin, float miterLimit,
                            int32_t lineCap, const float* dashPattern, uint32_t dashCount,
                            float dashOffset, const VelloTransform& t, float opacity)
    {
        std::vector<float> pts =
            FlattenPathCommands(startX, startY, commands, commandLength, 0.25f);
        size_t nPts = pts.size() / 2;
        if (nPts < 2) return true;
        AccumulateStrokeBounds(startX, startY, commands, commandLength, strokeWidth,
                               miterLimit, t);

        float patternLen = 0.0f;
        for (uint32_t i = 0; i < dashCount; i++) patternLen += std::fmax(dashPattern[i], 0.0f);
        if (patternLen <= 1e-6f) {
            // Degenerate pattern: draw solid.
            EncodeTransform(t);
            EncodeStyle(VelloStyle::Stroke(strokeWidth, lineJoin, miterLimit, lineCap));
            uint32_t nSegs = EncodePathCommands(startX, startY, commands, commandLength, false);
            if (nSegs > 0) EncodeBrush(brush, opacity, t);
            return true;
        }

        // Normalize the phase into the pattern.
        float phase = std::fmod(dashOffset, patternLen);
        if (phase < 0.0f) phase += patternLen;
        uint32_t dashIx = 0;
        bool on = true;
        float remaining = 0.0f;
        {
            float p = phase;
            while (p >= dashPattern[dashIx] && dashPattern[dashIx] > 0.0f) {
                p -= dashPattern[dashIx];
                dashIx = (dashIx + 1) % dashCount;
                on = !on;
            }
            remaining = dashPattern[dashIx] - p;
        }

        EncodeTransform(t);
        EncodeStyle(VelloStyle::Stroke(strokeWidth, lineJoin, miterLimit, lineCap));

        VelloPathEncoder enc(scene_.pathTags, scene_.pathData, scene_.numPathSegments,
                             scene_.numPaths, false);
        float prevX = pts[0], prevY = pts[1];
        if (on) {
            enc.MoveTo(prevX, prevY);
        }
        for (size_t i = 1; i < nPts; i++) {
            float x = pts[i * 2], y = pts[i * 2 + 1];
            float segLen = std::hypot(x - prevX, y - prevY);
            float consumed = 0.0f;
            while (segLen - consumed > 1e-6f) {
                float step = std::fmin(remaining, segLen - consumed);
                consumed += step;
                remaining -= step;
                float tt = consumed / segLen;
                float px = prevX + (x - prevX) * tt;
                float py = prevY + (y - prevY) * tt;
                if (remaining <= 1e-6f) {
                    // Dash boundary reached.
                    if (on) {
                        enc.LineTo(px, py);
                    } else {
                        enc.MoveTo(px, py);
                    }
                    dashIx = (dashIx + 1) % dashCount;
                    remaining = dashPattern[dashIx];
                    on = !on;
                } else if (consumed >= segLen - 1e-6f) {
                    if (on) enc.LineTo(px, py);
                }
            }
            prevX = x;
            prevY = y;
        }
        if (enc.Finish(true) > 0) {
            EncodeBrush(brush, opacity, t);
        }
        return true;
    }

    // ------------------------------------------------------------------
    // Scissor-as-clip
    // ------------------------------------------------------------------

    void EnsureScissorClip()
    {
        if (!hasScissor_ || scissorClipActive_) return;
        scissorClipActive_ = true;  // set first: EncodeBeginClipRect re-enters encode paths
        float l = scissor_[0], tp = scissor_[1];
        float w = scissor_[2] - scissor_[0];
        float h = scissor_[3] - scissor_[1];
        if (w <= 0.0f || h <= 0.0f) {
            w = 0.0f;
            h = 0.0f;
        }
        EncodeBeginClipRect(l, tp, w, h);
    }

    void CloseScissorClip()
    {
        if (!scissorClipActive_) return;
        scissorClipActive_ = false;
        EncodeEndClip();
    }

    // ------------------------------------------------------------------

    VelloScene scene_;
    VelloPackedScene packed_;
    bool packedDirty_ = true;

    uint32_t tilesX_ = 0;
    uint32_t tilesY_ = 0;

    bool hasScissor_ = false;
    bool scissorClipActive_ = false;
    float scissor_[4] = {0, 0, 0, 0};

    // Union of every encoded draw's device-space bounds for this sub-scene.
    static constexpr uint32_t kMaxPrimBoxes = 24;
    float primBoxes_[kMaxPrimBoxes][4] = {};
    uint32_t primBoxCount_ = 0;
    bool primBoxOverflow_ = false;
    float primMinX_ = 1e30f, primMinY_ = 1e30f;
    float primMaxX_ = -1e30f, primMaxY_ = -1e30f;
    float bboxMinX_ = 1e30f;
    float bboxMinY_ = 1e30f;
    float bboxMaxX_ = -1e30f;
    float bboxMaxY_ = -1e30f;

    struct RampHashEntry {
        uint64_t hash;
        uint32_t id;
    };
    std::vector<RampHashEntry> rampHashes_;
};

}  // namespace jalium
