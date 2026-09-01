// D3D12VelloRenderer -- Vello 0.10.0 GPU compute pipeline, D3D12 backend.
//
// Full rewrite (2026-08-30): the CPU-side scene encoding is the shared
// VelloSceneEncoder (jalium_vello_encode.h, a port of vello_encoding); this
// class owns only the D3D12 resources and the 19-stage dispatch graph
// (pathtag_reduce/reduce2/scan1/scan -> bbox_clear -> flatten -> draw_reduce/
// draw_leaf -> clip_reduce/clip_leaf -> binning -> tile_alloc ->
// path_count_setup/path_count(indirect) -> backdrop -> coarse ->
// path_tiling_setup/path_tiling(indirect) -> fine).
//
// Curve flattening, GPU stroke expansion (Euler spirals), monoid scans and
// the real clip stack all run on the GPU now; gradients support pad/repeat/
// reflect extend modes, two-point conical radials and sweeps via a 512-wide
// premultiplied ramp texture.
//
// #921 lifetime contract (unchanged from the previous implementation):
// every GPU resource that a still-open command list references must be parked
// on pendingRetiredResources_/pendingRetiredHeaps_ instead of being released;
// the direct renderer drains those into its fence-gated frame lists via
// DrainRetired/DrainRetiredHeaps after each dispatch (see FlushVelloPaths).

#pragma once

#include <d3d12.h>
#include <wrl/client.h>

#include <cstdint>
#include <vector>

#include "jalium_vello_encode.h"

namespace jalium {

class Brush;

using Microsoft::WRL::ComPtr;

// Legacy compile-compat stub (the pre-bytecode shader cache; never used).
struct ShaderBlobCache {
    bool velloCompiled = false;
};

// Per-frame tick for the opt-in JALIUM_VELLO_PERF dispatch profiler (see
// d3d12_vello.cpp). No-op unless the env var is set.
void VelloPerfBeginFrame();
int VelloPerfLevel();

class D3D12VelloRenderer {
public:
    static constexpr uint32_t kMaxFrames = 3;

    explicit D3D12VelloRenderer(ID3D12Device* device, ShaderBlobCache* shaderCache = nullptr);
    ~D3D12VelloRenderer();

    bool Initialize();

    // ------------------------------------------------------------------
    // Frame / scissor
    // ------------------------------------------------------------------

    void BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight);

    void SetScissorRect(float left, float top, float right, float bottom)
    {
        encoder_.SetScissor(left, top, right, bottom);
    }

    void ClearScissorRect() { encoder_.ClearScissor(); }

    // ------------------------------------------------------------------
    // Encoding (paths arrive in DIP/local space; the affine carries DPI)
    // ------------------------------------------------------------------

    bool EncodeFillPath(float startX, float startY, const float* commands,
                        uint32_t commandLength, float r, float g, float b, float a,
                        uint32_t fillRule, float m11 = 1, float m12 = 0, float m21 = 0,
                        float m22 = 1, float dx = 0, float dy = 0);

    bool EncodeFillPathBrush(float startX, float startY, const float* commands,
                             uint32_t commandLength, Brush* brush, uint32_t fillRule,
                             float opacity, float m11 = 1, float m12 = 0, float m21 = 0,
                             float m22 = 1, float dx = 0, float dy = 0);

    bool EncodeStrokePathBrush(float startX, float startY, const float* commands,
                               uint32_t commandLength, Brush* brush, float strokeWidth,
                               bool closed, int32_t lineJoin, float miterLimit, float opacity,
                               int32_t lineCap = 2, const float* dashPattern = nullptr,
                               uint32_t dashCount = 0, float dashOffset = 0, float m11 = 1,
                               float m12 = 0, float m21 = 0, float m22 = 1, float dx = 0,
                               float dy = 0);

    bool EncodeFillPathEngineBrush(float startX, float startY, const float* commands,
                                   uint32_t commandLength, const EngineBrushData& brush,
                                   uint32_t fillRule, float opacity, float m11 = 1,
                                   float m12 = 0, float m21 = 0, float m22 = 1, float dx = 0,
                                   float dy = 0);

    bool EncodeStrokePathEngineBrush(float startX, float startY, const float* commands,
                                     uint32_t commandLength, const EngineBrushData& brush,
                                     float strokeWidth, bool closed, int32_t lineJoin,
                                     float miterLimit, float opacity, int32_t lineCap = 2,
                                     const float* dashPattern = nullptr, uint32_t dashCount = 0,
                                     float dashOffset = 0, float m11 = 1, float m12 = 0,
                                     float m21 = 0, float m22 = 1, float dx = 0, float dy = 0);

    bool EncodeBeginClip(float startX, float startY, const float* commands,
                         uint32_t commandLength, uint32_t fillRule, float m11 = 1,
                         float m12 = 0, float m21 = 0, float m22 = 1, float dx = 0,
                         float dy = 0);
    void EncodeBeginClipRect(float x, float y, float w, float h);
    void EncodeEndClip(uint32_t blendMode = kVelloBlendDefault, float alpha = 1.0f);

    bool EncodeBlurRect(float x, float y, float w, float h, float cornerRadius,
                        float blurSigma, float r, float g, float b, float a, float m11 = 1,
                        float m12 = 0, float m21 = 0, float m22 = 1, float dx = 0,
                        float dy = 0);

    // ------------------------------------------------------------------
    // Dispatch / output
    // ------------------------------------------------------------------

    // Records the whole compute graph into cmdList. Returns false when there
    // is nothing to draw or resources could not be created.
    bool Dispatch(ID3D12GraphicsCommandList* cmdList, uint32_t frameIndex = 0);

    // Retained for API compatibility -- the pipeline is GPU-only now.
    void SetGPUPipeline(bool) {}
    bool IsGPUPipeline() const { return true; }

    ID3D12Resource* GetOutputTexture() const { return outputTexture_.Get(); }
    uint32_t GetOutputW() const { return outputW_; }
    uint32_t GetOutputH() const { return outputH_; }

    // Device-space rectangle the last successful Dispatch rendered. A
    // sub-scene only covers its own content, so the composite must place the
    // output texture at this origin rather than over the whole viewport.
    const VelloRenderRegion& LastRegion() const { return lastRegion_; }

    // Hands the just-composited output texture to `frameIndex`'s in-flight
    // list so the next Dispatch gets a different one, then reuses a recycled
    // texture instead of allocating (the composite's AddBitmap must remain a
    // valid owner until the command list executes -- D3D12 #921).
    void ForceNewOutputTexture(uint32_t frameIndex);

    // Called once per frame after `frameIndex`'s fence has been observed:
    // returns that slot's in-flight output textures to the free pool and
    // resets the per-frame upload arena. Recycling is what keeps a
    // many-sub-scene frame from allocating hundreds of textures.
    void RecycleFrameResources(uint32_t frameIndex);

    // TEST SEAM (#921): true while `tex` is still owned by this renderer -- i.e.
    // parked in a frame slot's in-flight list, sitting in the recycle pool, or
    // on the pending-retire list. Anything owned here outlives the frame's
    // command list, which is exactly the keep-alive the composite requires.
    bool OwnsOutputTexture(ID3D12Resource* tex) const
    {
        if (!tex) return false;
        for (uint32_t f = 0; f < kMaxFrames; f++) {
            for (const auto& p : outputInFlight_[f]) {
                if (p.tex.Get() == tex) return true;
            }
        }
        for (const auto& p : outputFreeList_) {
            if (p.tex.Get() == tex) return true;
        }
        for (const auto& r : pendingRetiredResources_) {
            if (r.Get() == tex) return true;
        }
        return false;
    }

    bool HasWork() const { return encoder_.HasWork(); }

    bool PendingDeviceBounds(float& x0, float& y0, float& x1, float& y1) const
    {
        return encoder_.PendingDeviceBounds(x0, y0, x1, y1);
    }

    bool PendingHitsDeviceRect(float x0, float y0, float x1, float y1) const
    {
        return encoder_.PendingHitsDeviceRect(x0, y0, x1, y1);
    }
    uint32_t GetPathCount() const { return encoder_.PathCount(); }

    // ------------------------------------------------------------------
    // Retire plumbing (see FlushVelloPaths in d3d12_direct_renderer.cpp)
    // ------------------------------------------------------------------

    void DrainRetired(std::vector<ComPtr<ID3D12Resource>>& outRetired);
    void DrainRetiredHeaps(std::vector<ComPtr<ID3D12DescriptorHeap>>& outRetired);

private:
    // Stage indices into psos_/rootSigs_ (dispatch order).
    enum Stage : uint32_t {
        kStagePathtagReduce = 0,
        kStagePathtagReduce2,
        kStagePathtagScan1,
        kStagePathtagScan,
        kStageBboxClear,
        kStageFlatten,
        kStageDrawReduce,
        kStageDrawLeaf,
        kStageClipReduce,
        kStageClipLeaf,
        kStageBinning,
        kStageTileAlloc,
        kStagePathCountSetup,
        kStagePathCount,
        kStageBackdrop,
        kStageCoarse,
        kStagePathTilingSetup,
        kStagePathTiling,
        kStageFine,
        kStageCount
    };

    // Per-frame-slot linear upload arena. Every Dispatch bump-allocates its
    // scene / config / ramp / bump-zero bytes out of one persistent buffer
    // instead of creating committed resources per sub-scene; the offset is
    // reset once per frame in RecycleFrameResources.
    struct FrameUploads {
        ComPtr<ID3D12Resource> arena;
        uint64_t capacity = 0;
        uint64_t offset = 0;
        uint8_t* mapped = nullptr;
    };

    // A sub-allocation inside the arena.
    struct UploadSlice {
        ID3D12Resource* resource = nullptr;
        uint64_t offset = 0;
        uint8_t* cpu = nullptr;
        bool Valid() const { return resource != nullptr; }
    };

    bool CreatePipelines();
    bool EnsureOutputTexture(uint32_t w, uint32_t h);
    bool EnsureGpuBuffers(const VelloRenderInfo& ri, uint32_t sceneWords);
    bool EnsureFrameArena(uint32_t frameIndex, uint64_t neededBytes);
    UploadSlice ArenaAlloc(uint32_t frameIndex, uint64_t bytes, uint64_t alignment = 256);

    // Bump-allocates `count` descriptors out of this frame's shader-visible
    // heap. Returns false when the heap could not be created; on overflow the
    // heap is grown (old one retired) and the cache invalidated.
    bool AllocDescriptors(uint32_t frameIndex, uint32_t count, uint32_t& outBase);

    void RetireOutputTexture()
    {
        if (outputTexture_) {
            pendingRetiredResources_.push_back(std::move(outputTexture_));
            outputTexture_.Reset();
        }
        outputW_ = 0;
        outputH_ = 0;
    }

    // Creates (or regrows, retiring the old resource) a DEFAULT-heap buffer
    // with UAV support. Byte size is rounded up with 2x headroom.
    bool EnsureBuffer(ComPtr<ID3D12Resource>& buf, uint64_t& capacity, uint64_t neededBytes,
                      const wchar_t* name);

    VelloSceneEncoder encoder_;

    ComPtr<ID3D12Device> device_;
    bool initialized_ = false;
    bool pipelinesReady_ = false;

    ComPtr<ID3D12RootSignature> rootSig_;
    ComPtr<ID3D12PipelineState> psos_[kStageCount];
    ComPtr<ID3D12CommandSignature> dispatchIndirectSig_;

    // GPU buffers (DEFAULT heap, UAV-capable; COMMON at rest).
    ComPtr<ID3D12Resource> sceneBuffer_;
    uint64_t sceneCapacity_ = 0;
    ComPtr<ID3D12Resource> reducedBuffer_;
    uint64_t reducedCapacity_ = 0;
    ComPtr<ID3D12Resource> reduced2Buffer_;
    uint64_t reduced2Capacity_ = 0;
    ComPtr<ID3D12Resource> reducedScanBuffer_;
    uint64_t reducedScanCapacity_ = 0;
    ComPtr<ID3D12Resource> tagMonoidBuffer_;
    uint64_t tagMonoidCapacity_ = 0;
    ComPtr<ID3D12Resource> pathBboxBuffer_;
    uint64_t pathBboxCapacity_ = 0;
    ComPtr<ID3D12Resource> lineSoupBuffer_;
    uint64_t lineSoupCapacity_ = 0;
    ComPtr<ID3D12Resource> drawReducedBuffer_;
    uint64_t drawReducedCapacity_ = 0;
    ComPtr<ID3D12Resource> drawMonoidBuffer_;
    uint64_t drawMonoidCapacity_ = 0;
    ComPtr<ID3D12Resource> infoBinDataBuffer_;
    uint64_t infoBinDataCapacity_ = 0;
    ComPtr<ID3D12Resource> clipInpBuffer_;
    uint64_t clipInpCapacity_ = 0;
    ComPtr<ID3D12Resource> clipBicBuffer_;
    uint64_t clipBicCapacity_ = 0;
    ComPtr<ID3D12Resource> clipElBuffer_;
    uint64_t clipElCapacity_ = 0;
    ComPtr<ID3D12Resource> clipBboxBuffer_;
    uint64_t clipBboxCapacity_ = 0;
    ComPtr<ID3D12Resource> drawBboxBuffer_;
    uint64_t drawBboxCapacity_ = 0;
    ComPtr<ID3D12Resource> binHeaderBuffer_;
    uint64_t binHeaderCapacity_ = 0;
    ComPtr<ID3D12Resource> pathBuffer_;
    uint64_t pathCapacity_ = 0;
    ComPtr<ID3D12Resource> tileBuffer_;
    uint64_t tileCapacity_ = 0;
    ComPtr<ID3D12Resource> segCountBuffer_;
    uint64_t segCountCapacity_ = 0;
    ComPtr<ID3D12Resource> segmentBuffer_;
    uint64_t segmentCapacity_ = 0;
    ComPtr<ID3D12Resource> ptclBuffer_;
    uint64_t ptclCapacity_ = 0;
    ComPtr<ID3D12Resource> blendSpillBuffer_;
    uint64_t blendSpillCapacity_ = 0;
    ComPtr<ID3D12Resource> bumpBuffer_;
    ComPtr<ID3D12Resource> indirectBuffer_;

    // Gradient ramp texture (512 x rampRows_, RGBA8) and the 1x1 dummy image
    // atlas (image brushes are not routed through Vello yet).
    ComPtr<ID3D12Resource> rampTexture_;
    uint32_t rampRows_ = 0;
    ComPtr<ID3D12Resource> dummyAtlasTexture_;

    ComPtr<ID3D12Resource> outputTexture_;
    uint32_t outputW_ = 0;
    uint32_t outputH_ = 0;
    VelloRenderRegion lastRegion_;

    // Output-texture recycling. Textures are pooled per size bucket; a frame
    // with N sub-scenes reuses the same handful across frames instead of
    // allocating N viewport-sized textures every frame.
    struct PooledTexture {
        ComPtr<ID3D12Resource> tex;
        uint32_t w = 0;
        uint32_t h = 0;
    };
    std::vector<PooledTexture> outputFreeList_;
    std::vector<PooledTexture> outputInFlight_[kMaxFrames];

    // Non-shader-visible UAV heap slot for ClearUnorderedAccessViewFloat.
    ComPtr<ID3D12DescriptorHeap> cpuUavHeap_;

    // One shader-visible descriptor heap per frame slot, bump-allocated across
    // every sub-scene dispatch in that frame (creating a heap per dispatch cost
    // ~0.3ms each, which dominated a many-sub-scene frame).
    ComPtr<ID3D12DescriptorHeap> descHeap_[kMaxFrames];
    uint32_t descCapacity_[kMaxFrames] = {};
    uint32_t descCursor_[kMaxFrames] = {};

    // Every stage except `fine` binds the same resources for every sub-scene in
    // a frame, so their descriptor block is written once and reused until a
    // resource is recreated (resourceGeneration_) or the frame slot changes.
    // Descriptors already referenced by recorded dispatches must never be
    // mutated, so `fine` (which binds the per-sub-scene output texture) always
    // gets a freshly allocated block.
    uint32_t resourceGeneration_ = 1;
    uint32_t cachedSharedGen_ = 0;
    uint32_t cachedSharedFrame_ = UINT32_MAX;
    uint32_t cachedSharedBase_ = 0;

    FrameUploads frameUploads_[kMaxFrames];

    uint32_t descriptorSize_ = 0;

    std::vector<ComPtr<ID3D12Resource>> pendingRetiredResources_;
    std::vector<ComPtr<ID3D12DescriptorHeap>> pendingRetiredHeaps_;
};

}  // namespace jalium
