// D3D12VelloRenderer -- Vello 0.10.0 GPU compute pipeline (D3D12 backend).
// See d3d12_vello.h for the architecture overview.

#include "d3d12_vello.h"

#include <cassert>
#include <cstring>

#include "d3d12_resources.h"
#include "d3d12_vello_bytecode.h"

#include <chrono>
#include <cstdio>
#include <cstdlib>

namespace jalium {

using namespace vello_bytecode;

// ---------------------------------------------------------------------------
// Opt-in dispatch profiler: set JALIUM_VELLO_PERF=1 to get a per-second stderr
// line with the per-frame sub-scene dispatch count, CPU record time and scene
// sizes. Zero cost when the env var is absent (one cached bool test).
// ---------------------------------------------------------------------------
namespace {

bool VelloPerfEnabled()
{
    static const bool enabled = [] {
        char* buf = nullptr;
        size_t len = 0;
        if (_dupenv_s(&buf, &len, "JALIUM_VELLO_PERF") != 0 || !buf) return false;
        bool on = buf[0] != '\0' && buf[0] != '0';
        free(buf);
        return on;
    }();
    return enabled;
}

bool VelloSmallScanEnabled()
{
    static const bool enabled = [] {
        char buf[16];
        size_t n = 0;
        if (getenv_s(&n, buf, sizeof(buf), "JALIUM_VELLO_SMALL_SCAN") != 0 || n == 0) {
            return true;
        }
        return buf[0] != '0';
    }();
    return enabled;
}

bool VelloBatchBarriersEnabled()
{
    static const bool enabled = [] {
        char buf[16];
        size_t n = 0;
        if (getenv_s(&n, buf, sizeof(buf), "JALIUM_VELLO_BATCH_BARRIERS") != 0 || n == 0) {
            return true;
        }
        return buf[0] != '0';
    }();
    return enabled;
}

bool VelloStagePerfRequested()
{
    static const bool enabled = [] {
        char buf[16];
        size_t n = 0;
        if (getenv_s(&n, buf, sizeof(buf), "JALIUM_VELLO_STAGE_PERF") != 0 || n == 0) {
            return false;
        }
        return buf[0] != '0';
    }();
    return enabled;
}

bool VelloCpuTagScanEnabled()
{
    static const bool enabled = [] {
        char buf[16];
        size_t n = 0;
        if (getenv_s(&n, buf, sizeof(buf), "JALIUM_VELLO_CPU_TAG_SCAN") != 0 || n == 0) {
            return false;
        }
        return buf[0] != '0';
    }();
    return enabled;
}

struct VelloPerfStats {
    uint64_t frames = 0;
    uint64_t dispatches = 0;
    double cpuMicros = 0;
    double encodeMicros = 0;   // Finalize + Pack + BuildRenderInfo
    double bufferMicros = 0;   // EnsureGpuBuffers + EnsureFrameUploads + memcpy
    double descMicros = 0;     // heap creation + descriptor writes
    double recordMicros = 0;   // barriers + SetPipelineState/Dispatch
    uint64_t pathTagBytes = 0;
    uint64_t drawObjs = 0;
    uint64_t sceneWords = 0;
    uint64_t fineWorkgroups = 0;
    uint64_t cpuScans = 0;
    uint64_t smallScans = 0;
    uint64_t largeScans = 0;
    uint64_t outputCreates = 0;
    uint64_t outputReuses = 0;
    std::chrono::steady_clock::time_point lastDump = std::chrono::steady_clock::now();
};

VelloPerfStats g_perf;

}  // namespace

// Flush-gate telemetry (written by D3D12RenderTarget::FlushVelloIfNeeded).
uint64_t g_velloGateSkip = 0;      // bounded call, disjoint -> no flush
uint64_t g_velloGateHit = 0;       // bounded call, overlap -> flushed
uint64_t g_velloGateUnbounded = 0; // paramless call with pending work -> flushed
uint64_t g_velloGateHitSite[8] = {};  // site tag of bounded hits
uint64_t g_velloGateDumpBudget = 0;   // level>=2: per-second [GateHit] lines
uint64_t g_velloGateReroute = 0;      // overlapping rect/poly encoded into the sub-scene
uint64_t g_vdBudget = 0;              // level>=2: per-second [VD] dispatch lines

int VelloPerfLevel()
{
    static int level = []() {
        char buf[16]; size_t n = 0;
        if (getenv_s(&n, buf, sizeof(buf), "JALIUM_VELLO_PERF") != 0 || n == 0) return 0;
        int v = atoi(buf);
        return v > 0 ? v : (buf[0] != '0' ? 1 : 0);
    }();
    return level;
}

void VelloPerfBeginFrame()
{
    if (!VelloPerfEnabled()) return;
    g_perf.frames++;
    auto now = std::chrono::steady_clock::now();
    double elapsed = std::chrono::duration<double>(now - g_perf.lastDump).count();
    if (elapsed >= 1.0 && g_perf.frames > 0) {
        double nd = g_perf.dispatches ? (double)g_perf.dispatches : 1.0;
        std::fprintf(stderr,
                     "[VelloPerf] %.1f fps | dispatch/frame=%.1f | cpu/frame=%.2fms "
                     "(%.0fus/dispatch: encode=%.0f buffers=%.0f desc=%.0f record=%.0f) | "
                     "tags/frame=%llu drawobj/frame=%llu sceneKB/frame=%.1f fineWG/frame=%llu "
                     "scan/frame=%.1fC/%.1fS/%.1fL output/frame=%.1fnew/%.1freused\n",
                     g_perf.frames / elapsed,
                     (double)g_perf.dispatches / (double)g_perf.frames,
                     g_perf.cpuMicros / 1000.0 / (double)g_perf.frames,
                     g_perf.cpuMicros / nd,
                     g_perf.encodeMicros / nd, g_perf.bufferMicros / nd,
                     g_perf.descMicros / nd, g_perf.recordMicros / nd,
                     (unsigned long long)(g_perf.pathTagBytes / g_perf.frames),
                     (unsigned long long)(g_perf.drawObjs / g_perf.frames),
                     (double)g_perf.sceneWords * 4.0 / 1024.0 / (double)g_perf.frames,
                     (unsigned long long)(g_perf.fineWorkgroups / g_perf.frames),
                     (double)g_perf.cpuScans / (double)g_perf.frames,
                     (double)g_perf.smallScans / (double)g_perf.frames,
                     (double)g_perf.largeScans / (double)g_perf.frames,
                     (double)g_perf.outputCreates / (double)g_perf.frames,
                     (double)g_perf.outputReuses / (double)g_perf.frames);
        std::fprintf(stderr,
                     "[VelloGate] skip/frame=%.1f hitB/frame=%.1f unb/frame=%.1f reroute/frame=%.1f "
                     "hitSites[rect=%llu text=%llu poly=%llu ellipse=%llu line=%llu bitmap=%llu other=%llu]\n",
                     (double)g_velloGateSkip / (double)g_perf.frames,
                     (double)g_velloGateHit / (double)g_perf.frames,
                     (double)g_velloGateUnbounded / (double)g_perf.frames,
                     (double)g_velloGateReroute / (double)g_perf.frames,
                     (unsigned long long)g_velloGateHitSite[0],
                     (unsigned long long)g_velloGateHitSite[1],
                     (unsigned long long)g_velloGateHitSite[2],
                     (unsigned long long)g_velloGateHitSite[3],
                     (unsigned long long)g_velloGateHitSite[4],
                     (unsigned long long)g_velloGateHitSite[5],
                     (unsigned long long)g_velloGateHitSite[6]);
        std::fflush(stderr);
        g_velloGateSkip = g_velloGateHit = g_velloGateUnbounded = g_velloGateReroute = 0;
        g_velloGateDumpBudget = 60;
        g_vdBudget = 40;
        for (auto& v : g_velloGateHitSite) v = 0;
        g_perf = VelloPerfStats{};
        g_perf.lastDump = now;
    }
}

namespace {

constexpr uint32_t kSlotsPerStage = 16;  // 5 SRV + 4 UAV used; padded stride
constexpr uint32_t kSrvSlots = 5;
constexpr uint32_t kUavSlots = 4;

struct StageShader {
    const unsigned char* bytecode;
    unsigned int size;
};

}  // namespace

// ============================================================================
// Lifetime
// ============================================================================

D3D12VelloRenderer::D3D12VelloRenderer(ID3D12Device* device,
                                       ShaderBlobCache* /*shaderCache*/,
                                       uint64_t timestampFrequency)
    : device_(device), timestampFrequency_(timestampFrequency)
{
}

D3D12VelloRenderer::~D3D12VelloRenderer() = default;

bool D3D12VelloRenderer::Initialize()
{
    if (!device_) return false;
    descriptorSize_ =
        device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

    stageProfilerEnabled_ = InitializeStageProfiler();

    initialized_ = true;
    // PSO creation is deferred to the first Dispatch (keeps startup fast when
    // the Vello engine is enabled but never used).
    return true;
}

void D3D12VelloRenderer::BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight)
{
    encoder_.BeginFrame(viewportWidth, viewportHeight);
}

bool D3D12VelloRenderer::InitializeStageProfiler()
{
    if (!VelloStagePerfRequested() || timestampFrequency_ == 0) return false;

    D3D12_QUERY_HEAP_DESC queryDesc = {};
    queryDesc.Type = D3D12_QUERY_HEAP_TYPE_TIMESTAMP;
    queryDesc.Count = kMaxFrames * kMaxStageProfileQueriesPerFrame;
    if (FAILED(device_->CreateQueryHeap(&queryDesc, IID_PPV_ARGS(&stageProfileQueryHeap_)))) {
        return false;
    }

    D3D12_HEAP_PROPERTIES heap = {};
    heap.Type = D3D12_HEAP_TYPE_READBACK;
    D3D12_RESOURCE_DESC buffer = {};
    buffer.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    buffer.Width = sizeof(uint64_t) * kMaxStageProfileQueriesPerFrame;
    buffer.Height = 1;
    buffer.DepthOrArraySize = 1;
    buffer.MipLevels = 1;
    buffer.SampleDesc.Count = 1;
    buffer.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;

    for (uint32_t frame = 0; frame < kMaxFrames; frame++) {
        if (FAILED(device_->CreateCommittedResource(
                &heap, D3D12_HEAP_FLAG_NONE, &buffer, D3D12_RESOURCE_STATE_COPY_DEST,
                nullptr, IID_PPV_ARGS(&stageProfileReadback_[frame])))) {
            stageProfileQueryHeap_.Reset();
            for (auto& readback : stageProfileReadback_) readback.Reset();
            return false;
        }
        stageProfileReadback_[frame]->SetName(L"JaliumVelloStageTimingReadback");
        stageProfileRecords_[frame].reserve(kMaxStageProfileDispatches);
    }
    stageProfileLastDumpMs_ = GetTickCount64();
    return true;
}

D3D12VelloRenderer::StageProfileRecord* D3D12VelloRenderer::BeginStageProfile(
    ID3D12GraphicsCommandList* cmdList, uint32_t frameIndex)
{
    if (!stageProfilerEnabled_ || !cmdList || !stageProfileQueryHeap_) return nullptr;
    const uint32_t frame = frameIndex % kMaxFrames;
    uint32_t& cursor = stageProfileQueryCursor_[frame];
    if (cursor + kStageCount + 1 > kMaxStageProfileQueriesPerFrame) return nullptr;

    auto& records = stageProfileRecords_[frame];
    if (records.size() >= kMaxStageProfileDispatches) return nullptr;
    records.emplace_back();
    StageProfileRecord& record = records.back();
    record.firstQuery = cursor;
    record.stages.reserve(kStageCount);

    const uint32_t query = frame * kMaxStageProfileQueriesPerFrame + cursor++;
    cmdList->EndQuery(stageProfileQueryHeap_.Get(), D3D12_QUERY_TYPE_TIMESTAMP, query);
    return &record;
}

void D3D12VelloRenderer::MarkStageProfile(ID3D12GraphicsCommandList* cmdList,
                                          uint32_t frameIndex,
                                          StageProfileRecord* record,
                                          Stage stage)
{
    if (!record) return;
    const uint32_t frame = frameIndex % kMaxFrames;
    uint32_t& cursor = stageProfileQueryCursor_[frame];
    if (cursor >= kMaxStageProfileQueriesPerFrame) return;
    const uint32_t query = frame * kMaxStageProfileQueriesPerFrame + cursor++;
    cmdList->EndQuery(stageProfileQueryHeap_.Get(), D3D12_QUERY_TYPE_TIMESTAMP, query);
    record->stages.push_back(stage);
}

void D3D12VelloRenderer::ResolveStageProfile(ID3D12GraphicsCommandList* cmdList,
                                             uint32_t frameIndex,
                                             StageProfileRecord* record)
{
    if (!record || record->stages.empty()) return;
    const uint32_t frame = frameIndex % kMaxFrames;
    const uint32_t queryCount = (uint32_t)record->stages.size() + 1;
    const uint32_t firstQuery = frame * kMaxStageProfileQueriesPerFrame + record->firstQuery;
    cmdList->ResolveQueryData(
        stageProfileQueryHeap_.Get(), D3D12_QUERY_TYPE_TIMESTAMP, firstQuery, queryCount,
        stageProfileReadback_[frame].Get(), (uint64_t)record->firstQuery * sizeof(uint64_t));
}

void D3D12VelloRenderer::DecodeStageProfile(uint32_t frameIndex)
{
    if (!stageProfilerEnabled_) return;
    const uint32_t frame = frameIndex % kMaxFrames;
    auto& records = stageProfileRecords_[frame];
    uint32_t& cursor = stageProfileQueryCursor_[frame];
    if (records.empty() || cursor == 0) {
        records.clear();
        cursor = 0;
        return;
    }

    void* mapped = nullptr;
    D3D12_RANGE readRange = {0, (SIZE_T)cursor * sizeof(uint64_t)};
    if (FAILED(stageProfileReadback_[frame]->Map(0, &readRange, &mapped)) || !mapped) {
        records.clear();
        cursor = 0;
        return;
    }

    const auto* timestamps = static_cast<const uint64_t*>(mapped);
    for (const StageProfileRecord& record : records) {
        for (size_t i = 0; i < record.stages.size(); i++) {
            uint64_t begin = timestamps[record.firstQuery + i];
            uint64_t end = timestamps[record.firstQuery + i + 1];
            if (end <= begin) continue;
            const uint32_t stage = (uint32_t)record.stages[i];
            stageProfileTicks_[stage] += end - begin;
            stageProfileSamples_[stage]++;
        }
    }
    stageProfileReadback_[frame]->Unmap(0, nullptr);
    stageProfileFrames_++;
    stageProfileDispatches_ += records.size();
    records.clear();
    cursor = 0;

    const uint64_t nowMs = GetTickCount64();
    if (nowMs - stageProfileLastDumpMs_ < 1000 || stageProfileFrames_ == 0) return;

    uint64_t totalTicks = 0;
    for (uint32_t stage = 0; stage < kStageCount; stage++) {
        totalTicks += stageProfileTicks_[stage];
    }
    auto stageUs = [&](Stage stage) {
        const uint32_t ix = (uint32_t)stage;
        if (stageProfileSamples_[ix] == 0) return 0.0;
        return (double)stageProfileTicks_[ix] * 1'000'000.0 /
               (double)timestampFrequency_ / (double)stageProfileSamples_[ix];
    };
    const double gpuMsPerFrame = (double)totalTicks * 1000.0 /
                                 (double)timestampFrequency_ /
                                 (double)stageProfileFrames_;
    std::fprintf(stderr,
                 "[VelloStagePerf] frames=%llu dispatch/frame=%.1f stage-gpu/frame=%.3fms\n",
                 (unsigned long long)stageProfileFrames_,
                 (double)stageProfileDispatches_ / (double)stageProfileFrames_,
                 gpuMsPerFrame);
    std::fprintf(stderr,
                 "[VelloStagePerf] us/dispatch reduce=%.1f reduce2=%.1f scan1=%.1f "
                 "scanL=%.1f scanS=%.1f bbox=%.1f flatten=%.1f drawR=%.1f drawL=%.1f "
                 "clipR=%.1f clipL=%.1f bin=%.1f tile=%.1f countSetup=%.1f count=%.1f "
                 "backdrop=%.1f coarse=%.1f tilingSetup=%.1f tiling=%.1f fine=%.1f\n",
                 stageUs(kStagePathtagReduce), stageUs(kStagePathtagReduce2),
                 stageUs(kStagePathtagScan1), stageUs(kStagePathtagScan),
                 stageUs(kStagePathtagScanSmall), stageUs(kStageBboxClear),
                 stageUs(kStageFlatten), stageUs(kStageDrawReduce),
                 stageUs(kStageDrawLeaf), stageUs(kStageClipReduce),
                 stageUs(kStageClipLeaf), stageUs(kStageBinning),
                 stageUs(kStageTileAlloc), stageUs(kStagePathCountSetup),
                 stageUs(kStagePathCount), stageUs(kStageBackdrop),
                 stageUs(kStageCoarse), stageUs(kStagePathTilingSetup),
                 stageUs(kStagePathTiling), stageUs(kStageFine));
    std::fflush(stderr);
    stageProfileFrames_ = 0;
    stageProfileDispatches_ = 0;
    std::memset(stageProfileTicks_, 0, sizeof(stageProfileTicks_));
    std::memset(stageProfileSamples_, 0, sizeof(stageProfileSamples_));
    stageProfileLastDumpMs_ = nowMs;
}

// ============================================================================
// Pipelines
// ============================================================================

bool D3D12VelloRenderer::CreatePipelines()
{
    if (pipelinesReady_) return true;

    // Shared compute root signature:
    //   0: root CBV b0 (VelloConfig)
    //   1: descriptor table SRV t0..t4
    //   2: descriptor table UAV u0..u3
    D3D12_DESCRIPTOR_RANGE srvRange = {};
    srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
    srvRange.NumDescriptors = kSrvSlots;
    srvRange.BaseShaderRegister = 0;
    srvRange.OffsetInDescriptorsFromTableStart = 0;

    D3D12_DESCRIPTOR_RANGE uavRange = {};
    uavRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV;
    uavRange.NumDescriptors = kUavSlots;
    uavRange.BaseShaderRegister = 0;
    uavRange.OffsetInDescriptorsFromTableStart = 0;

    D3D12_ROOT_PARAMETER params[3] = {};
    params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
    params[0].Descriptor.ShaderRegister = 0;
    params[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;
    params[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    params[1].DescriptorTable.NumDescriptorRanges = 1;
    params[1].DescriptorTable.pDescriptorRanges = &srvRange;
    params[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;
    params[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    params[2].DescriptorTable.NumDescriptorRanges = 1;
    params[2].DescriptorTable.pDescriptorRanges = &uavRange;
    params[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    D3D12_ROOT_SIGNATURE_DESC rsDesc = {};
    rsDesc.NumParameters = 3;
    rsDesc.pParameters = params;

    ComPtr<ID3DBlob> blob, error;
    if (FAILED(D3D12SerializeRootSignature(&rsDesc, D3D_ROOT_SIGNATURE_VERSION_1, &blob,
                                           &error))) {
        return false;
    }
    if (FAILED(device_->CreateRootSignature(0, blob->GetBufferPointer(), blob->GetBufferSize(),
                                            IID_PPV_ARGS(&rootSig_)))) {
        return false;
    }

    const StageShader shaders[kStageCount] = {
        {kPathtagReduce, kPathtagReduceSize},    // kStagePathtagReduce
        {kPathtagReduce2, kPathtagReduce2Size},  // kStagePathtagReduce2
        {kPathtagScan1, kPathtagScan1Size},      // kStagePathtagScan1
        {kPathtagScan, kPathtagScanSize},        // kStagePathtagScan
        {kPathtagScanSmall, kPathtagScanSmallSize}, // kStagePathtagScanSmall
        {kBboxClear, kBboxClearSize},            // kStageBboxClear
        {kFlatten, kFlattenSize},                // kStageFlatten
        {kDrawReduce, kDrawReduceSize},          // kStageDrawReduce
        {kDrawLeaf, kDrawLeafSize},              // kStageDrawLeaf
        {kVelloClipReduce, kVelloClipReduceSize},// kStageClipReduce
        {kVelloClipLeaf, kVelloClipLeafSize},    // kStageClipLeaf
        {kBinning, kBinningSize},                // kStageBinning
        {kTileAlloc, kTileAllocSize},            // kStageTileAlloc
        {kPathCountSetup, kPathCountSetupSize},  // kStagePathCountSetup
        {kPathCount, kPathCountSize},            // kStagePathCount
        {kBackdrop, kBackdropSize},              // kStageBackdrop
        {kCoarse, kCoarseSize},                  // kStageCoarse
        {kPathTilingSetup, kPathTilingSetupSize},// kStagePathTilingSetup
        {kPathTiling, kPathTilingSize},          // kStagePathTiling
        {kFine, kFineSize},                      // kStageFine
    };

    for (uint32_t i = 0; i < kStageCount; i++) {
        D3D12_COMPUTE_PIPELINE_STATE_DESC desc = {};
        desc.pRootSignature = rootSig_.Get();
        desc.CS.pShaderBytecode = shaders[i].bytecode;
        desc.CS.BytecodeLength = shaders[i].size;
        if (FAILED(device_->CreateComputePipelineState(&desc, IID_PPV_ARGS(&psos_[i])))) {
            return false;
        }
    }

    D3D12_INDIRECT_ARGUMENT_DESC arg = {};
    arg.Type = D3D12_INDIRECT_ARGUMENT_TYPE_DISPATCH;
    D3D12_COMMAND_SIGNATURE_DESC sigDesc = {};
    sigDesc.ByteStride = sizeof(uint32_t) * 3;
    sigDesc.NumArgumentDescs = 1;
    sigDesc.pArgumentDescs = &arg;
    if (FAILED(device_->CreateCommandSignature(&sigDesc, nullptr,
                                               IID_PPV_ARGS(&dispatchIndirectSig_)))) {
        return false;
    }

    pipelinesReady_ = true;
    return true;
}

// ============================================================================
// Encode entry points
// ============================================================================

static EngineTransform MakeTransform(float m11, float m12, float m21, float m22, float dx,
                                     float dy)
{
    EngineTransform t;
    t.m11 = m11;
    t.m12 = m12;
    t.m21 = m21;
    t.m22 = m22;
    t.dx = dx;
    t.dy = dy;
    return t;
}

// Converts a backend Brush into the engine-neutral brush description. Returns
// false for brush types Vello does not route (image brushes fall back to the
// CPU triangulation path which handles them).
static bool ConvertBrush(Brush* brush, EngineBrushData& out)
{
    if (!brush) return false;
    switch (brush->GetType()) {
        case JALIUM_BRUSH_SOLID: {
            auto* sb = static_cast<D3D12SolidBrush*>(brush);
            out.type = 0;
            out.r = sb->r_;
            out.g = sb->g_;
            out.b = sb->b_;
            out.a = sb->a_;
            return true;
        }
        case JALIUM_BRUSH_LINEAR_GRADIENT: {
            auto* lg = static_cast<D3D12LinearGradientBrush*>(brush);
            out.type = 1;
            out.startX = lg->startX_;
            out.startY = lg->startY_;
            out.endX = lg->endX_;
            out.endY = lg->endY_;
            out.spreadMethod = lg->spreadMethod_;
            out.stops = reinterpret_cast<const EngineBrushData::GradientStop*>(
                lg->stops_.data());
            out.stopCount = (uint32_t)lg->stops_.size();
            static_assert(sizeof(GradStop) == sizeof(EngineBrushData::GradientStop),
                          "GradStop layout must match EngineBrushData::GradientStop");
            return true;
        }
        case JALIUM_BRUSH_RADIAL_GRADIENT: {
            auto* rg = static_cast<D3D12RadialGradientBrush*>(brush);
            out.type = 2;
            out.centerX = rg->centerX_;
            out.centerY = rg->centerY_;
            out.radiusX = rg->radiusX_;
            out.radiusY = rg->radiusY_;
            out.originX = rg->originX_;
            out.originY = rg->originY_;
            out.spreadMethod = rg->spreadMethod_;
            out.stops = reinterpret_cast<const EngineBrushData::GradientStop*>(
                rg->stops_.data());
            out.stopCount = (uint32_t)rg->stops_.size();
            return true;
        }
        default:
            return false;  // image / bitmap brushes: CPU fallback
    }
}

bool D3D12VelloRenderer::EncodeFillPath(float startX, float startY, const float* commands,
                                        uint32_t commandLength, float r, float g, float b,
                                        float a, uint32_t fillRule, float m11, float m12,
                                        float m21, float m22, float dx, float dy)
{
    encoder_.BeginPrimitiveBox();
    EngineBrushData brush;
    brush.type = 0;
    brush.r = r;
    brush.g = g;
    brush.b = b;
    brush.a = a;
    { bool ok_ = encoder_.EncodeFillPath(startX, startY, commands, commandLength, brush,
                                   fillRule == 0 ? FillRule::EvenOdd : FillRule::NonZero,
                                   MakeTransform(m11, m12, m21, m22, dx, dy)); encoder_.EndPrimitiveBox(ok_); return ok_; }
}

bool D3D12VelloRenderer::EncodeFillPathBrush(float startX, float startY, const float* commands,
                                             uint32_t commandLength, Brush* brush,
                                             uint32_t fillRule, float opacity, float m11,
                                             float m12, float m21, float m22, float dx,
                                             float dy)
{
    encoder_.BeginPrimitiveBox();
    EngineBrushData eb;
    if (!ConvertBrush(brush, eb)) { encoder_.EndPrimitiveBox(false); return false; }
    { bool ok_ = encoder_.EncodeFillPath(startX, startY, commands, commandLength, eb,
                                   fillRule == 0 ? FillRule::EvenOdd : FillRule::NonZero,
                                   MakeTransform(m11, m12, m21, m22, dx, dy), opacity); encoder_.EndPrimitiveBox(ok_); return ok_; }
}

bool D3D12VelloRenderer::EncodeStrokePathBrush(
    float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush,
    float strokeWidth, bool closed, int32_t lineJoin, float miterLimit, float opacity,
    int32_t lineCap, const float* dashPattern, uint32_t dashCount, float dashOffset, float m11,
    float m12, float m21, float m22, float dx, float dy)
{
    encoder_.BeginPrimitiveBox();
    EngineBrushData eb;
    if (!ConvertBrush(brush, eb)) { encoder_.EndPrimitiveBox(false); return false; }
    { bool ok_ = encoder_.EncodeStrokePath(startX, startY, commands, commandLength, eb, strokeWidth,
                                     closed, lineJoin, miterLimit, lineCap, dashPattern,
                                     dashCount, dashOffset,
                                     MakeTransform(m11, m12, m21, m22, dx, dy), opacity); encoder_.EndPrimitiveBox(ok_); return ok_; }
}

bool D3D12VelloRenderer::EncodeFillPathEngineBrush(float startX, float startY,
                                                   const float* commands,
                                                   uint32_t commandLength,
                                                   const EngineBrushData& brush,
                                                   uint32_t fillRule, float opacity, float m11,
                                                   float m12, float m21, float m22, float dx,
                                                   float dy)
{
    encoder_.BeginPrimitiveBox();
    { bool ok_ = encoder_.EncodeFillPath(startX, startY, commands, commandLength, brush,
                                   fillRule == 0 ? FillRule::EvenOdd : FillRule::NonZero,
                                   MakeTransform(m11, m12, m21, m22, dx, dy), opacity); encoder_.EndPrimitiveBox(ok_); return ok_; }
}

bool D3D12VelloRenderer::EncodeStrokePathEngineBrush(
    float startX, float startY, const float* commands, uint32_t commandLength,
    const EngineBrushData& brush, float strokeWidth, bool closed, int32_t lineJoin,
    float miterLimit, float opacity, int32_t lineCap, const float* dashPattern,
    uint32_t dashCount, float dashOffset, float m11, float m12, float m21, float m22, float dx,
    float dy)
{
    encoder_.BeginPrimitiveBox();
    { bool ok_ = encoder_.EncodeStrokePath(startX, startY, commands, commandLength, brush,
                                     strokeWidth, closed, lineJoin, miterLimit, lineCap,
                                     dashPattern, dashCount, dashOffset,
                                     MakeTransform(m11, m12, m21, m22, dx, dy), opacity); encoder_.EndPrimitiveBox(ok_); return ok_; }
}

bool D3D12VelloRenderer::EncodeBeginClip(float startX, float startY, const float* commands,
                                         uint32_t commandLength, uint32_t fillRule, float m11,
                                         float m12, float m21, float m22, float dx, float dy)
{
    return encoder_.EncodeBeginClipPath(startX, startY, commands, commandLength,
                                        fillRule == 0 ? FillRule::EvenOdd : FillRule::NonZero,
                                        MakeTransform(m11, m12, m21, m22, dx, dy));
}

void D3D12VelloRenderer::EncodeBeginClipRect(float x, float y, float w, float h)
{
    encoder_.EncodeBeginClipRect(x, y, w, h);
}

void D3D12VelloRenderer::EncodeEndClip(uint32_t blendMode, float alpha)
{
    (void)blendMode;
    (void)alpha;
    encoder_.EncodeEndClip();
}

bool D3D12VelloRenderer::EncodeBlurRect(float x, float y, float w, float h, float cornerRadius,
                                        float blurSigma, float r, float g, float b, float a,
                                        float m11, float m12, float m21, float m22, float dx,
                                        float dy)
{
    encoder_.BeginPrimitiveBox();
    { bool ok_ = encoder_.EncodeBlurredRoundedRect(x, y, w, h, cornerRadius, blurSigma, r, g, b, a,
                                             MakeTransform(m11, m12, m21, m22, dx, dy)); encoder_.EndPrimitiveBox(ok_); return ok_; }
}

// ============================================================================
// Resource management
// ============================================================================

bool D3D12VelloRenderer::EnsureBuffer(ComPtr<ID3D12Resource>& buf, uint64_t& capacity,
                                      uint64_t neededBytes, const wchar_t* name)
{
    if (buf && capacity >= neededBytes) return true;

    uint64_t newCap;
    if (neededBytes < (8ull << 20)) {
        newCap = std::max<uint64_t>(neededBytes * 2, 4096);
    } else {
        newCap = neededBytes;
    }

    // Retire-on-grow: the old buffer may still be referenced by the open
    // command list (D3D12 #921).
    if (buf) {
        pendingRetiredResources_.push_back(std::move(buf));
        buf.Reset();
    }

    D3D12_HEAP_PROPERTIES heap = {};
    heap.Type = D3D12_HEAP_TYPE_DEFAULT;
    D3D12_RESOURCE_DESC desc = {};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    desc.Width = newCap;
    desc.Height = 1;
    desc.DepthOrArraySize = 1;
    desc.MipLevels = 1;
    desc.SampleDesc.Count = 1;
    desc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    desc.Flags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;

    if (FAILED(device_->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc,
                                                D3D12_RESOURCE_STATE_COMMON, nullptr,
                                                IID_PPV_ARGS(&buf)))) {
        capacity = 0;
        return false;
    }
    buf->SetName(name);
    capacity = newCap;
    resourceGeneration_++;  // descriptors referencing the old buffer are stale
    return true;
}

bool D3D12VelloRenderer::EnsureOutputTexture(uint32_t w, uint32_t h)
{
    if (outputTexture_ && outputW_ == w && outputH_ == h) return true;
    RetireOutputTexture();

    // Reuse a recycled texture of the same size before allocating. Sub-scene
    // regions repeat heavily across frames (the same icons redraw), so the
    // pool converges after a frame or two and steady-state allocation is nil.
    for (size_t i = 0; i < outputFreeList_.size(); i++) {
        if (outputFreeList_[i].w == w && outputFreeList_[i].h == h) {
            outputTexture_ = std::move(outputFreeList_[i].tex);
            outputAllocationBytes_ = outputFreeList_[i].allocationBytes;
            outputFreeBytes_ -= outputAllocationBytes_;
            outputFreeList_[i] = std::move(outputFreeList_.back());
            outputFreeList_.pop_back();
            outputW_ = w;
            outputH_ = h;
            if (VelloPerfEnabled()) g_perf.outputReuses++;
            return true;
        }
    }

    D3D12_HEAP_PROPERTIES heap = {};
    heap.Type = D3D12_HEAP_TYPE_DEFAULT;
    D3D12_RESOURCE_DESC desc = {};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    desc.Width = w;
    desc.Height = h;
    desc.DepthOrArraySize = 1;
    desc.MipLevels = 1;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Flags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;

    if (FAILED(device_->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc,
                                                D3D12_RESOURCE_STATE_COMMON, nullptr,
                                                IID_PPV_ARGS(&outputTexture_)))) {
        return false;
    }
    outputTexture_->SetName(L"JaliumVelloOutput");
    outputW_ = w;
    outputH_ = h;
    outputAllocationBytes_ = device_->GetResourceAllocationInfo(0, 1, &desc).SizeInBytes;
    if (VelloPerfEnabled()) g_perf.outputCreates++;
    return true;
}

bool D3D12VelloRenderer::EnsureGpuBuffers(const VelloRenderInfo& ri, uint32_t sceneWords)
{
    bool ok = true;
    ok &= EnsureBuffer(sceneBuffer_, sceneCapacity_, (uint64_t)sceneWords * 4,
                       L"JaliumVelloScene");
    ok &= EnsureBuffer(reducedBuffer_, reducedCapacity_,
                       (uint64_t)ri.reducedSize * kVelloStrideTagMonoid, L"JaliumVelloReduced");
    ok &= EnsureBuffer(reduced2Buffer_, reduced2Capacity_,
                       (uint64_t)ri.reduced2Size * kVelloStrideTagMonoid,
                       L"JaliumVelloReduced2");
    ok &= EnsureBuffer(reducedScanBuffer_, reducedScanCapacity_,
                       (uint64_t)ri.reducedScanSize * kVelloStrideTagMonoid,
                       L"JaliumVelloReducedScan");
    ok &= EnsureBuffer(tagMonoidBuffer_, tagMonoidCapacity_,
                       (uint64_t)ri.tagMonoidsSize * kVelloStrideTagMonoid,
                       L"JaliumVelloTagMonoids");
    ok &= EnsureBuffer(pathBboxBuffer_, pathBboxCapacity_,
                       (uint64_t)ri.pathBboxSize * kVelloStridePathBbox,
                       L"JaliumVelloPathBbox");
    ok &= EnsureBuffer(lineSoupBuffer_, lineSoupCapacity_,
                       (uint64_t)ri.lineSoupSize * kVelloStrideLineSoup, L"JaliumVelloLines");
    ok &= EnsureBuffer(drawReducedBuffer_, drawReducedCapacity_,
                       (uint64_t)ri.drawReducedSize * kVelloStrideDrawMonoid,
                       L"JaliumVelloDrawReduced");
    ok &= EnsureBuffer(drawMonoidBuffer_, drawMonoidCapacity_,
                       (uint64_t)ri.drawMonoidSize * kVelloStrideDrawMonoid,
                       L"JaliumVelloDrawMonoids");
    ok &= EnsureBuffer(infoBinDataBuffer_, infoBinDataCapacity_,
                       (uint64_t)ri.infoBinDataSize * 4, L"JaliumVelloInfoBinData");
    ok &= EnsureBuffer(clipInpBuffer_, clipInpCapacity_,
                       (uint64_t)ri.clipInpSize * kVelloStrideClipInp, L"JaliumVelloClipInp");
    ok &= EnsureBuffer(clipBicBuffer_, clipBicCapacity_,
                       (uint64_t)ri.clipBicSize * kVelloStrideClipBic, L"JaliumVelloClipBic");
    ok &= EnsureBuffer(clipElBuffer_, clipElCapacity_,
                       (uint64_t)ri.clipElSize * kVelloStrideClipEl, L"JaliumVelloClipEl");
    ok &= EnsureBuffer(clipBboxBuffer_, clipBboxCapacity_,
                       (uint64_t)ri.clipBboxSize * kVelloStrideClipBbox,
                       L"JaliumVelloClipBbox");
    ok &= EnsureBuffer(drawBboxBuffer_, drawBboxCapacity_,
                       (uint64_t)ri.drawBboxSize * kVelloStrideDrawBbox,
                       L"JaliumVelloDrawBbox");
    ok &= EnsureBuffer(binHeaderBuffer_, binHeaderCapacity_,
                       (uint64_t)ri.binHeaderSize * kVelloStrideBinHeader,
                       L"JaliumVelloBinHeaders");
    ok &= EnsureBuffer(pathBuffer_, pathCapacity_, (uint64_t)ri.pathSize * kVelloStridePath,
                       L"JaliumVelloPaths");
    ok &= EnsureBuffer(tileBuffer_, tileCapacity_, (uint64_t)ri.tileSize * kVelloStrideTile,
                       L"JaliumVelloTiles");
    ok &= EnsureBuffer(segCountBuffer_, segCountCapacity_,
                       (uint64_t)ri.segCountSize * kVelloStrideSegCount,
                       L"JaliumVelloSegCounts");
    ok &= EnsureBuffer(segmentBuffer_, segmentCapacity_,
                       (uint64_t)ri.segmentSize * kVelloStrideSegment, L"JaliumVelloSegments");
    ok &= EnsureBuffer(ptclBuffer_, ptclCapacity_, (uint64_t)ri.ptclSize * 4,
                       L"JaliumVelloPtcl");
    ok &= EnsureBuffer(blendSpillBuffer_, blendSpillCapacity_, (uint64_t)ri.blendSpillSize * 4,
                       L"JaliumVelloBlendSpill");
    if (!bumpBuffer_) {
        uint64_t cap = 0;
        ok &= EnsureBuffer(bumpBuffer_, cap, 256, L"JaliumVelloBump");
    }
    if (!indirectBuffer_) {
        uint64_t cap = 0;
        ok &= EnsureBuffer(indirectBuffer_, cap, 256, L"JaliumVelloIndirect");
    }

    // Gradient ramp texture (512 x rows, RGBA8).
    uint32_t neededRows = std::max(encoder_.scene().rampCount, 1u);
    if (!rampTexture_ || rampRows_ < neededRows) {
        if (rampTexture_) {
            pendingRetiredResources_.push_back(std::move(rampTexture_));
            rampTexture_.Reset();
        }
        uint32_t rows = std::max(neededRows, std::max(rampRows_ * 2, 4u));
        D3D12_HEAP_PROPERTIES heap = {};
        heap.Type = D3D12_HEAP_TYPE_DEFAULT;
        D3D12_RESOURCE_DESC desc = {};
        desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        desc.Width = kVelloRampWidth;
        desc.Height = rows;
        desc.DepthOrArraySize = 1;
        desc.MipLevels = 1;
        desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.SampleDesc.Count = 1;
        if (FAILED(device_->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc,
                                                    D3D12_RESOURCE_STATE_COMMON, nullptr,
                                                    IID_PPV_ARGS(&rampTexture_)))) {
            rampRows_ = 0;
            return false;
        }
        rampTexture_->SetName(L"JaliumVelloRamps");
        rampRows_ = rows;
        resourceGeneration_++;
    }

    // 1x1 dummy image atlas (image brushes are not routed through Vello yet).
    if (!dummyAtlasTexture_) {
        D3D12_HEAP_PROPERTIES heap = {};
        heap.Type = D3D12_HEAP_TYPE_DEFAULT;
        D3D12_RESOURCE_DESC desc = {};
        desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        desc.Width = 1;
        desc.Height = 1;
        desc.DepthOrArraySize = 1;
        desc.MipLevels = 1;
        desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.SampleDesc.Count = 1;
        if (FAILED(device_->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc,
                                                    D3D12_RESOURCE_STATE_COMMON, nullptr,
                                                    IID_PPV_ARGS(&dummyAtlasTexture_)))) {
            return false;
        }
        dummyAtlasTexture_->SetName(L"JaliumVelloDummyAtlas");
        resourceGeneration_++;
    }

    return ok;
}

static bool CreateUploadBuffer(ID3D12Device* device, uint64_t bytes,
                               ComPtr<ID3D12Resource>& out, const wchar_t* name)
{
    D3D12_HEAP_PROPERTIES heap = {};
    heap.Type = D3D12_HEAP_TYPE_UPLOAD;
    D3D12_RESOURCE_DESC desc = {};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    desc.Width = bytes;
    desc.Height = 1;
    desc.DepthOrArraySize = 1;
    desc.MipLevels = 1;
    desc.SampleDesc.Count = 1;
    desc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    if (FAILED(device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc,
                                               D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
                                               IID_PPV_ARGS(&out)))) {
        return false;
    }
    out->SetName(name);
    return true;
}

bool D3D12VelloRenderer::EnsureFrameArena(uint32_t frameIndex, uint64_t neededBytes)
{
    FrameUploads& fu = frameUploads_[frameIndex % kMaxFrames];
    if (fu.arena && fu.capacity >= neededBytes) return true;

    // Growing the arena mid-frame would orphan slices already handed out, so
    // the old buffer is retired (fence-gated) rather than released.
    if (fu.arena) {
        if (fu.mapped) {
            fu.arena->Unmap(0, nullptr);
            fu.mapped = nullptr;
        }
        pendingRetiredResources_.push_back(std::move(fu.arena));
        fu.arena.Reset();
    }
    uint64_t cap = std::max<uint64_t>(neededBytes * 2, 1u << 20);
    if (!CreateUploadBuffer(device_.Get(), cap, fu.arena, L"JaliumVelloUploadArena")) {
        fu.capacity = 0;
        fu.offset = 0;
        return false;
    }
    D3D12_RANGE noRead = {0, 0};
    void* mapped = nullptr;
    if (FAILED(fu.arena->Map(0, &noRead, &mapped)) || !mapped) {
        pendingRetiredResources_.push_back(std::move(fu.arena));
        fu.arena.Reset();
        fu.capacity = 0;
        fu.offset = 0;
        return false;
    }
    fu.mapped = static_cast<uint8_t*>(mapped);
    fu.capacity = cap;
    fu.offset = 0;
    return true;
}

D3D12VelloRenderer::UploadSlice D3D12VelloRenderer::ArenaAlloc(uint32_t frameIndex,
                                                               uint64_t bytes,
                                                               uint64_t alignment)
{
    FrameUploads& fu = frameUploads_[frameIndex % kMaxFrames];
    UploadSlice slice;
    if (!fu.arena || !fu.mapped) return slice;
    uint64_t start = (fu.offset + alignment - 1) & ~(alignment - 1);
    if (start + bytes > fu.capacity) return slice;
    fu.offset = start + bytes;
    slice.resource = fu.arena.Get();
    slice.offset = start;
    slice.cpu = fu.mapped + start;
    return slice;
}

bool D3D12VelloRenderer::AllocDescriptors(uint32_t frameIndex, uint32_t count,
                                          uint32_t& outBase)
{
    uint32_t slot = frameIndex % kMaxFrames;
    // Default capacity covers a heavily sub-divided frame; grow on demand.
    uint32_t needed = descCursor_[slot] + count;
    if (!descHeap_[slot] || descCapacity_[slot] < needed) {
        uint32_t cap = std::max<uint32_t>(needed * 2, 4096);
        ComPtr<ID3D12DescriptorHeap> heap;
        D3D12_DESCRIPTOR_HEAP_DESC hd = {};
        hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        hd.NumDescriptors = cap;
        hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        if (FAILED(device_->CreateDescriptorHeap(&hd, IID_PPV_ARGS(&heap)))) return false;
        // The old heap may still be referenced by recorded dispatches.
        if (descHeap_[slot]) pendingRetiredHeaps_.push_back(std::move(descHeap_[slot]));
        descHeap_[slot] = std::move(heap);
        descCapacity_[slot] = cap;
        descCursor_[slot] = 0;
        cachedSharedFrame_ = UINT32_MAX;  // cached block lived in the old heap
        needed = count;
    }
    outBase = descCursor_[slot];
    descCursor_[slot] += count;
    return true;
}

void D3D12VelloRenderer::ForceNewOutputTexture(uint32_t frameIndex)
{
    // The composite (AddBitmap) still needs these pixels when the command list
    // executes, so the texture stays owned until this frame slot's fence is
    // observed -- RecycleFrameResources then returns it to the pool.
    if (outputTexture_) {
        PooledTexture pooled;
        pooled.tex = std::move(outputTexture_);
        pooled.w = outputW_;
        pooled.h = outputH_;
        pooled.allocationBytes = outputAllocationBytes_;
        outputInFlight_[frameIndex % kMaxFrames].push_back(std::move(pooled));
        outputTexture_.Reset();
    }
    outputW_ = 0;
    outputH_ = 0;
    outputAllocationBytes_ = 0;
}

void D3D12VelloRenderer::RecycleFrameResources(uint32_t frameIndex)
{
    uint32_t slot = frameIndex % kMaxFrames;
    if (stageProfilerEnabled_) DecodeStageProfile(slot);
    // Icon/text interleaving regularly exceeds 64 sub-scenes. A count-only
    // limit of 64 forced committed allocations every frame even when all
    // outputs together occupied only a few MiB. Bound actual GPU allocation
    // bytes (including placement alignment), with a secondary object limit.
    constexpr uint64_t kMaxPooledBytes = 64ull * 1024 * 1024;
    constexpr size_t kMaxPooled = 512;
    for (auto& pooled : outputInFlight_[slot]) {
        if (pooled.allocationBytes > kMaxPooledBytes) continue;
        // Make room for returning sizes after resize/scene changes instead
        // of keeping an unused, full pool that prevents new sizes converging.
        while (!outputFreeList_.empty() &&
               (outputFreeList_.size() >= kMaxPooled ||
                outputFreeBytes_ + pooled.allocationBytes > kMaxPooledBytes)) {
            outputFreeBytes_ -= outputFreeList_.front().allocationBytes;
            outputFreeList_.erase(outputFreeList_.begin());
        }
        outputFreeBytes_ += pooled.allocationBytes;
        outputFreeList_.push_back(std::move(pooled));
    }
    outputInFlight_[slot].clear();
    frameUploads_[slot].offset = 0;
    descCursor_[slot] = 0;
    if (cachedSharedFrame_ == slot) cachedSharedFrame_ = UINT32_MAX;
}

void D3D12VelloRenderer::DrainRetired(std::vector<ComPtr<ID3D12Resource>>& outRetired)
{
    for (auto& r : pendingRetiredResources_) {
        outRetired.push_back(std::move(r));
    }
    pendingRetiredResources_.clear();
}

void D3D12VelloRenderer::DrainRetiredHeaps(std::vector<ComPtr<ID3D12DescriptorHeap>>& outRetired)
{
    for (auto& h : pendingRetiredHeaps_) {
        outRetired.push_back(std::move(h));
    }
    pendingRetiredHeaps_.clear();
}

// ============================================================================
// Dispatch
// ============================================================================

namespace {

struct DescWriter {
    ID3D12Device* device;
    D3D12_CPU_DESCRIPTOR_HANDLE cpuBase;
    D3D12_GPU_DESCRIPTOR_HANDLE gpuBase;
    uint32_t descriptorSize;

    D3D12_CPU_DESCRIPTOR_HANDLE Cpu(uint32_t slot) const
    {
        return {cpuBase.ptr + (uint64_t)slot * descriptorSize};
    }

    D3D12_GPU_DESCRIPTOR_HANDLE Gpu(uint32_t slot) const
    {
        return {gpuBase.ptr + (uint64_t)slot * descriptorSize};
    }

    void SrvStructured(uint32_t slot, ID3D12Resource* res, uint32_t stride, uint32_t elements)
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC d = {};
        d.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        d.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        d.Format = DXGI_FORMAT_UNKNOWN;
        d.Buffer.NumElements = elements;
        d.Buffer.StructureByteStride = stride;
        device->CreateShaderResourceView(res, &d, Cpu(slot));
    }

    void SrvTexture(uint32_t slot, ID3D12Resource* res)
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC d = {};
        d.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
        d.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        d.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        d.Texture2D.MipLevels = 1;
        device->CreateShaderResourceView(res, &d, Cpu(slot));
    }

    void SrvNull(uint32_t slot)
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC d = {};
        d.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        d.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        d.Format = DXGI_FORMAT_R32_UINT;
        d.Buffer.NumElements = 1;
        device->CreateShaderResourceView(nullptr, &d, Cpu(slot));
    }

    void UavStructured(uint32_t slot, ID3D12Resource* res, uint32_t stride, uint32_t elements)
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC d = {};
        d.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
        d.Format = DXGI_FORMAT_UNKNOWN;
        d.Buffer.NumElements = elements;
        d.Buffer.StructureByteStride = stride;
        device->CreateUnorderedAccessView(res, nullptr, &d, Cpu(slot));
    }

    void UavRaw(uint32_t slot, ID3D12Resource* res, uint32_t words)
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC d = {};
        d.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
        d.Format = DXGI_FORMAT_R32_TYPELESS;
        d.Buffer.NumElements = words;
        d.Buffer.Flags = D3D12_BUFFER_UAV_FLAG_RAW;
        device->CreateUnorderedAccessView(res, nullptr, &d, Cpu(slot));
    }

    void UavTexture(uint32_t slot, ID3D12Resource* res)
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC d = {};
        d.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        d.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        device->CreateUnorderedAccessView(res, nullptr, &d, Cpu(slot));
    }

    void UavNull(uint32_t slot)
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC d = {};
        d.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
        d.Format = DXGI_FORMAT_R32_UINT;
        d.Buffer.NumElements = 1;
        device->CreateUnorderedAccessView(nullptr, nullptr, &d, Cpu(slot));
    }
};

void Transition(ID3D12GraphicsCommandList* cl, ID3D12Resource* res,
                D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after)
{
    D3D12_RESOURCE_BARRIER b = {};
    b.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    b.Transition.pResource = res;
    b.Transition.StateBefore = before;
    b.Transition.StateAfter = after;
    b.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    cl->ResourceBarrier(1, &b);
}

template <size_t N>
void TransitionMany(ID3D12GraphicsCommandList* cl, ID3D12Resource* const (&resources)[N],
                    D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after,
                    ID3D12Resource* alternateResource = nullptr,
                    D3D12_RESOURCE_STATES alternateBefore = D3D12_RESOURCE_STATE_COMMON)
{
    D3D12_RESOURCE_BARRIER barriers[N] = {};
    for (size_t i = 0; i < N; i++) {
        barriers[i].Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barriers[i].Transition.pResource = resources[i];
        barriers[i].Transition.StateBefore = resources[i] == alternateResource
                                                 ? alternateBefore
                                                 : before;
        barriers[i].Transition.StateAfter = after;
        barriers[i].Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    }
    cl->ResourceBarrier((UINT)N, barriers);
}

void UavBarrier(ID3D12GraphicsCommandList* cl)
{
    D3D12_RESOURCE_BARRIER b = {};
    b.Type = D3D12_RESOURCE_BARRIER_TYPE_UAV;
    b.UAV.pResource = nullptr;
    cl->ResourceBarrier(1, &b);
}

}  // namespace

bool D3D12VelloRenderer::Dispatch(ID3D12GraphicsCommandList* cmdList, uint32_t frameIndex)
{
    const bool perf = VelloPerfEnabled();
    const auto perfT0 = perf ? std::chrono::steady_clock::now()
                             : std::chrono::steady_clock::time_point{};

    encoder_.Finalize();
    if (!initialized_ || !cmdList || !encoder_.HasWork()) return false;
    if (!CreatePipelines()) return false;

    const VelloScene& scene = encoder_.scene();
    uint32_t vpW = scene.viewportW;
    uint32_t vpH = scene.viewportH;
    if (vpW == 0 || vpH == 0) return false;

    const VelloPackedScene& packed = encoder_.Pack();
    VelloRenderInfo ri = encoder_.BuildRenderInfo();
    const bool useSmallPathScan = !ri.useLargePathScan && VelloSmallScanEnabled();
    const bool batchBarriers = VelloBatchBarriersEnabled();
    constexpr uint32_t kCpuTagScanMaxWgs = 4;
    const bool useCpuTagScan = useSmallPathScan &&
                               ri.pathtagReduceWgs <= kCpuTagScanMaxWgs &&
                               VelloCpuTagScanEnabled();

    // For tiny scenes, mirror vello_encoding::PathMonoid::new/combine on the
    // CPU and upload the exclusive prefix directly. At <=4 workgroups this is
    // at most 1,024 tag words / 20 KiB of output and costs less than recording
    // two dependent GPU dispatches. Larger scenes retain the GPU scan paths.
    if (useCpuTagScan) {
        cpuTagMonoids_.resize(ri.tagMonoidsSize);
        CpuTagMonoid prefix = {};
        constexpr uint32_t kRepeatedByteBits = 0x01010101u;
        constexpr uint32_t kStyleSizeWords = sizeof(VelloStyle) / sizeof(uint32_t);
        auto popcount32 = [](uint32_t value) {
            value -= (value >> 1u) & 0x55555555u;
            value = (value & 0x33333333u) + ((value >> 2u) & 0x33333333u);
            value = (value + (value >> 4u)) & 0x0f0f0f0fu;
            return (value * 0x01010101u) >> 24u;
        };
        for (uint32_t i = 0; i < ri.tagMonoidsSize; i++) {
            cpuTagMonoids_[i] = prefix;
            const uint32_t tagWord = packed.data[packed.pathTagBase + i];
            const uint32_t pointCount = tagWord & 0x03030303u;
            CpuTagMonoid item = {};
            item.pathsegIx = popcount32((pointCount * 7u) & 0x04040404u);
            item.transIx = popcount32(
                tagWord & ((uint32_t)kVelloPathTagTransform * kRepeatedByteBits));
            const uint32_t nPoints = pointCount + ((tagWord >> 2u) & kRepeatedByteBits);
            uint32_t dataWords = nPoints +
                                 (nPoints & (((tagWord >> 3u) & kRepeatedByteBits) * 15u));
            dataWords += dataWords >> 8u;
            dataWords += dataWords >> 16u;
            item.pathsegOffset = dataWords & 0xffu;
            item.pathIx = popcount32(
                tagWord & ((uint32_t)kVelloPathTagPath * kRepeatedByteBits));
            item.styleIx = popcount32(
                tagWord & ((uint32_t)kVelloPathTagStyle * kRepeatedByteBits)) * kStyleSizeWords;
            prefix.transIx += item.transIx;
            prefix.pathsegIx += item.pathsegIx;
            prefix.pathsegOffset += item.pathsegOffset;
            prefix.styleIx += item.styleIx;
            prefix.pathIx += item.pathIx;
        }
    }
    const auto perfT1 = perf ? std::chrono::steady_clock::now() : perfT0;

    // Render only the region this sub-scene covers. An empty region means the
    // encoder saw nothing with device-space extent -- nothing to draw.
    VelloRenderRegion region = packed.region;
    if (VelloPerfLevel() >= 2 && g_vdBudget > 0) {
        g_vdBudget--;
        std::fprintf(stderr, "[VD] region=(%u,%u %ux%u) paths=%u drawobjs=%u%c",
                     region.originX, region.originY, region.width, region.height,
                     ri.config.n_path, ri.config.n_drawobj, (char)10);
    }
    if (region.Empty()) return false;
    lastRegion_ = region;

    if (!EnsureOutputTexture(region.width, region.height)) return false;
    if (!EnsureGpuBuffers(ri, (uint32_t)packed.data.size())) return false;

    uint64_t sceneBytes = (uint64_t)packed.data.size() * 4;
    uint64_t rampBytes = (uint64_t)scene.rampData.size() * 4;
    uint64_t cpuTagBytes = useCpuTagScan
                               ? (uint64_t)cpuTagMonoids_.size() * sizeof(CpuTagMonoid)
                               : 0;

    // One linear arena allocation per sub-scene instead of committed
    // resources: scene | config (256-aligned for the root CBV) | ramps |
    // bump-zero.
    uint64_t arenaNeed = ((sceneBytes + 255) & ~255ull) + 256 +
                         ((rampBytes + 511) & ~511ull) +
                         ((cpuTagBytes + 255) & ~255ull) + 256;
    FrameUploads& fuSlot = frameUploads_[frameIndex % kMaxFrames];
    if (!EnsureFrameArena(frameIndex, fuSlot.offset + arenaNeed)) return false;

    UploadSlice sceneSlice = ArenaAlloc(frameIndex, sceneBytes, 256);
    UploadSlice configSlice = ArenaAlloc(frameIndex, 256, 256);
    UploadSlice rampSlice = rampBytes > 0
                                ? ArenaAlloc(frameIndex, rampBytes, 512)
                                : UploadSlice{};
    UploadSlice cpuTagSlice = useCpuTagScan
                                  ? ArenaAlloc(frameIndex, cpuTagBytes, 256)
                                  : UploadSlice{};
    UploadSlice bumpZeroSlice = ArenaAlloc(frameIndex, sizeof(VelloBumpAllocators), 256);
    if (!sceneSlice.Valid() || !configSlice.Valid() || !bumpZeroSlice.Valid() ||
        (useCpuTagScan && !cpuTagSlice.Valid()) ||
        (rampBytes > 0 && !rampSlice.Valid())) {
        return false;
    }

    std::memcpy(sceneSlice.cpu, packed.data.data(), (size_t)sceneBytes);
    std::memset(configSlice.cpu, 0, 256);
    std::memcpy(configSlice.cpu, &ri.config, sizeof(VelloConfig));
    if (rampBytes > 0) std::memcpy(rampSlice.cpu, scene.rampData.data(), (size_t)rampBytes);
    if (useCpuTagScan) {
        std::memcpy(cpuTagSlice.cpu, cpuTagMonoids_.data(), (size_t)cpuTagBytes);
    }
    std::memset(bumpZeroSlice.cpu, 0, sizeof(VelloBumpAllocators));
    const auto perfT2 = perf ? std::chrono::steady_clock::now() : perfT0;

    // ------------------------------------------------------------------
    // Upload copies
    // ------------------------------------------------------------------
    Transition(cmdList, sceneBuffer_.Get(), D3D12_RESOURCE_STATE_COMMON,
               D3D12_RESOURCE_STATE_COPY_DEST);
    Transition(cmdList, bumpBuffer_.Get(), D3D12_RESOURCE_STATE_COMMON,
               D3D12_RESOURCE_STATE_COPY_DEST);
    cmdList->CopyBufferRegion(sceneBuffer_.Get(), 0, sceneSlice.resource, sceneSlice.offset,
                              sceneBytes);
    cmdList->CopyBufferRegion(bumpBuffer_.Get(), 0, bumpZeroSlice.resource,
                              bumpZeroSlice.offset, sizeof(VelloBumpAllocators));
    if (useCpuTagScan) {
        Transition(cmdList, tagMonoidBuffer_.Get(), D3D12_RESOURCE_STATE_COMMON,
                   D3D12_RESOURCE_STATE_COPY_DEST);
        cmdList->CopyBufferRegion(tagMonoidBuffer_.Get(), 0, cpuTagSlice.resource,
                                  cpuTagSlice.offset, cpuTagBytes);
    }

    bool uploadRamps = rampBytes > 0;
    if (uploadRamps) {
        Transition(cmdList, rampTexture_.Get(), D3D12_RESOURCE_STATE_COMMON,
                   D3D12_RESOURCE_STATE_COPY_DEST);
        D3D12_TEXTURE_COPY_LOCATION dst = {};
        dst.pResource = rampTexture_.Get();
        dst.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        dst.SubresourceIndex = 0;
        D3D12_TEXTURE_COPY_LOCATION src = {};
        src.pResource = rampSlice.resource;
        src.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        src.PlacedFootprint.Offset = rampSlice.offset;
        src.PlacedFootprint.Footprint.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        src.PlacedFootprint.Footprint.Width = kVelloRampWidth;
        src.PlacedFootprint.Footprint.Height = scene.rampCount;
        src.PlacedFootprint.Footprint.Depth = 1;
        src.PlacedFootprint.Footprint.RowPitch = kVelloRampWidth * 4;  // 2048, 256-aligned
        cmdList->CopyTextureRegion(&dst, 0, 0, 0, &src, nullptr);
        Transition(cmdList, rampTexture_.Get(), D3D12_RESOURCE_STATE_COPY_DEST,
                   D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    } else {
        Transition(cmdList, rampTexture_.Get(), D3D12_RESOURCE_STATE_COMMON,
                   D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    }
    Transition(cmdList, dummyAtlasTexture_.Get(), D3D12_RESOURCE_STATE_COMMON,
               D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);

    Transition(cmdList, sceneBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST,
               D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    Transition(cmdList, bumpBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST,
               D3D12_RESOURCE_STATE_UNORDERED_ACCESS);

    // Every internal storage buffer: COMMON -> UAV for the dispatch graph.
    ID3D12Resource* uavResources[] = {
        reducedBuffer_.Get(),   reduced2Buffer_.Get(),  reducedScanBuffer_.Get(),
        tagMonoidBuffer_.Get(), pathBboxBuffer_.Get(),  lineSoupBuffer_.Get(),
        drawReducedBuffer_.Get(), drawMonoidBuffer_.Get(), infoBinDataBuffer_.Get(),
        clipInpBuffer_.Get(),   clipBicBuffer_.Get(),   clipElBuffer_.Get(),
        clipBboxBuffer_.Get(),  drawBboxBuffer_.Get(),  binHeaderBuffer_.Get(),
        pathBuffer_.Get(),      tileBuffer_.Get(),      segCountBuffer_.Get(),
        segmentBuffer_.Get(),   ptclBuffer_.Get(),      blendSpillBuffer_.Get(),
        indirectBuffer_.Get(),
    };
    if (batchBarriers) {
        TransitionMany(cmdList, uavResources, D3D12_RESOURCE_STATE_COMMON,
                       D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                       useCpuTagScan ? tagMonoidBuffer_.Get() : nullptr,
                       D3D12_RESOURCE_STATE_COPY_DEST);
    } else {
        for (ID3D12Resource* r : uavResources) {
            Transition(cmdList, r,
                       useCpuTagScan && r == tagMonoidBuffer_.Get()
                           ? D3D12_RESOURCE_STATE_COPY_DEST
                           : D3D12_RESOURCE_STATE_COMMON,
                       D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        }
    }
    Transition(cmdList, outputTexture_.Get(), D3D12_RESOURCE_STATE_COMMON,
               D3D12_RESOURCE_STATE_UNORDERED_ACCESS);

    // ------------------------------------------------------------------
    // Descriptor heap for this dispatch (parked immediately: #921)
    // ------------------------------------------------------------------
    // Stages 0..kStageFine-1 bind identical resources for every sub-scene in a
    // frame, so their descriptor block is written once and reused; `fine` binds
    // the per-sub-scene output texture and therefore always needs fresh slots
    // (a recorded dispatch's descriptors must never be mutated).
    const uint32_t kSharedDescCount = kStageFine * kSlotsPerStage;
    const uint32_t kFineDescCount = kSlotsPerStage;
    const uint32_t frameSlot = frameIndex % kMaxFrames;

    bool sharedValid = (cachedSharedFrame_ == frameSlot) &&
                       (cachedSharedGen_ == resourceGeneration_);
    uint32_t sharedBase = cachedSharedBase_;
    uint32_t fineBase = 0;
    if (sharedValid) {
        if (!AllocDescriptors(frameIndex, kFineDescCount, fineBase)) return false;
        // AllocDescriptors signals a heap replacement by clearing the cache;
        // the cached block lived in the old heap, so it must be rewritten.
        if (cachedSharedFrame_ == UINT32_MAX) sharedValid = false;
    }
    if (!sharedValid) {
        // Take both blocks in ONE allocation so an intervening heap growth
        // cannot leave sharedBase pointing into the previous heap.
        uint32_t base = 0;
        if (!AllocDescriptors(frameIndex, kSharedDescCount + kFineDescCount, base)) return false;
        sharedBase = base;
        fineBase = base + kSharedDescCount;
    }

    ID3D12DescriptorHeap* heapPtr = descHeap_[frameSlot].Get();
    DescWriter w{device_.Get(), heapPtr->GetCPUDescriptorHandleForHeapStart(),
                 heapPtr->GetGPUDescriptorHandleForHeapStart(), descriptorSize_};

    const VelloConfig& cfg = ri.config;
    uint32_t sceneWords = (uint32_t)packed.data.size();

    // Per-stage descriptor tables. Layout per stage: [0..4] = t0..t4,
    // [5..8] = u0..u3.
    // Views describe each buffer's FULL capacity so a descriptor depends only on
    // the resource identity (that is what makes the shared block cacheable
    // across sub-scenes); the shaders bound-check against the config counts,
    // never the view extent.
    auto elems = [](uint64_t capacityBytes, uint32_t stride) -> uint32_t {
        return stride ? (uint32_t)(capacityBytes / stride) : 0u;
    };
    const uint32_t nScene = elems(sceneCapacity_, 4);
    const uint32_t nReduced = elems(reducedCapacity_, kVelloStrideTagMonoid);
    const uint32_t nReduced2 = elems(reduced2Capacity_, kVelloStrideTagMonoid);
    const uint32_t nReducedScan = elems(reducedScanCapacity_, kVelloStrideTagMonoid);
    const uint32_t nTagMonoids = elems(tagMonoidCapacity_, kVelloStrideTagMonoid);
    const uint32_t nPathBbox = elems(pathBboxCapacity_, kVelloStridePathBbox);
    const uint32_t nLineSoup = elems(lineSoupCapacity_, kVelloStrideLineSoup);
    const uint32_t nDrawReduced = elems(drawReducedCapacity_, kVelloStrideDrawMonoid);
    const uint32_t nDrawMonoid = elems(drawMonoidCapacity_, kVelloStrideDrawMonoid);
    const uint32_t nInfoBin = elems(infoBinDataCapacity_, 4);
    const uint32_t nClipInp = elems(clipInpCapacity_, kVelloStrideClipInp);
    const uint32_t nClipBic = elems(clipBicCapacity_, kVelloStrideClipBic);
    const uint32_t nClipEl = elems(clipElCapacity_, kVelloStrideClipEl);
    const uint32_t nClipBbox = elems(clipBboxCapacity_, kVelloStrideClipBbox);
    const uint32_t nDrawBbox = elems(drawBboxCapacity_, kVelloStrideDrawBbox);
    const uint32_t nBinHeader = elems(binHeaderCapacity_, kVelloStrideBinHeader);
    const uint32_t nPath = elems(pathCapacity_, kVelloStridePath);
    const uint32_t nTile = elems(tileCapacity_, kVelloStrideTile);
    const uint32_t nSegCount = elems(segCountCapacity_, kVelloStrideSegCount);
    const uint32_t nSegment = elems(segmentCapacity_, kVelloStrideSegment);
    const uint32_t nPtcl = elems(ptclCapacity_, 4);
    const uint32_t nBlend = elems(blendSpillCapacity_, 4);
    const uint32_t nBumpWords = sizeof(VelloBumpAllocators) / 4;

    auto stageBase = [&](Stage st) {
        return st == kStageFine ? fineBase : (sharedBase + (uint32_t)st * kSlotsPerStage);
    };
    auto fillNulls = [&](Stage st) {
        uint32_t base = stageBase(st);
        for (uint32_t i = 0; i < kSrvSlots; i++) w.SrvNull(base + i);
        for (uint32_t i = 0; i < kUavSlots; i++) w.UavNull(base + kSrvSlots + i);
    };

    if (!sharedValid) {
        for (uint32_t st = 0; st < kStageFine; st++) fillNulls((Stage)st);
    }
    fillNulls(kStageFine);

    if (!sharedValid) {
        {
            uint32_t b = stageBase(kStagePathtagReduce);
            w.SrvStructured(b + 0, sceneBuffer_.Get(), 4, nScene);
            w.UavStructured(b + 5, reducedBuffer_.Get(), kVelloStrideTagMonoid, nReduced);
        }
        {
            uint32_t b = stageBase(kStagePathtagReduce2);
            w.SrvStructured(b + 0, reducedBuffer_.Get(), kVelloStrideTagMonoid, nReduced);
            w.UavStructured(b + 5, reduced2Buffer_.Get(), kVelloStrideTagMonoid, nReduced2);
        }
        {
            uint32_t b = stageBase(kStagePathtagScan1);
            w.SrvStructured(b + 0, reducedBuffer_.Get(), kVelloStrideTagMonoid, nReduced);
            w.SrvStructured(b + 1, reduced2Buffer_.Get(), kVelloStrideTagMonoid, nReduced2);
            w.UavStructured(b + 5, reducedScanBuffer_.Get(), kVelloStrideTagMonoid,
                            nReducedScan);
        }
        {
            uint32_t b = stageBase(kStagePathtagScan);
            w.SrvStructured(b + 0, sceneBuffer_.Get(), 4, nScene);
            w.SrvStructured(b + 1, reducedScanBuffer_.Get(), kVelloStrideTagMonoid,
                            nReducedScan);
            w.UavStructured(b + 5, tagMonoidBuffer_.Get(), kVelloStrideTagMonoid,
                            nTagMonoids);
        }
        {
            uint32_t b = stageBase(kStagePathtagScanSmall);
            w.SrvStructured(b + 0, sceneBuffer_.Get(), 4, nScene);
            w.SrvStructured(b + 1, reducedBuffer_.Get(), kVelloStrideTagMonoid,
                            nReduced);
            w.UavStructured(b + 5, tagMonoidBuffer_.Get(), kVelloStrideTagMonoid,
                            nTagMonoids);
        }
        {
            uint32_t b = stageBase(kStageBboxClear);
            w.UavStructured(b + 5, pathBboxBuffer_.Get(), kVelloStridePathBbox, nPathBbox);
        }
        {
            uint32_t b = stageBase(kStageFlatten);
            w.SrvStructured(b + 0, sceneBuffer_.Get(), 4, nScene);
            w.SrvStructured(b + 1, tagMonoidBuffer_.Get(), kVelloStrideTagMonoid,
                            nTagMonoids);
            w.UavRaw(b + 5, pathBboxBuffer_.Get(), nPathBbox * kVelloStridePathBbox / 4);
            w.UavRaw(b + 6, bumpBuffer_.Get(), nBumpWords);
            w.UavStructured(b + 7, lineSoupBuffer_.Get(), kVelloStrideLineSoup, nLineSoup);
        }
        {
            uint32_t b = stageBase(kStageDrawReduce);
            w.SrvStructured(b + 0, sceneBuffer_.Get(), 4, nScene);
            w.UavStructured(b + 5, drawReducedBuffer_.Get(), kVelloStrideDrawMonoid,
                            nDrawReduced);
        }
        {
            uint32_t b = stageBase(kStageDrawLeaf);
            w.SrvStructured(b + 0, sceneBuffer_.Get(), 4, nScene);
            w.SrvStructured(b + 1, drawReducedBuffer_.Get(), kVelloStrideDrawMonoid,
                            nDrawReduced);
            w.SrvStructured(b + 2, pathBboxBuffer_.Get(), kVelloStridePathBbox, nPathBbox);
            w.UavStructured(b + 5, drawMonoidBuffer_.Get(), kVelloStrideDrawMonoid,
                            nDrawMonoid);
            w.UavStructured(b + 6, infoBinDataBuffer_.Get(), 4, nInfoBin);
            w.UavStructured(b + 7, clipInpBuffer_.Get(), kVelloStrideClipInp, nClipInp);
        }
        {
            uint32_t b = stageBase(kStageClipReduce);
            w.SrvStructured(b + 0, clipInpBuffer_.Get(), kVelloStrideClipInp, nClipInp);
            w.SrvStructured(b + 1, pathBboxBuffer_.Get(), kVelloStridePathBbox, nPathBbox);
            w.UavStructured(b + 5, clipBicBuffer_.Get(), kVelloStrideClipBic, nClipBic);
            w.UavStructured(b + 6, clipElBuffer_.Get(), kVelloStrideClipEl, nClipEl);
        }
        {
            uint32_t b = stageBase(kStageClipLeaf);
            w.SrvStructured(b + 0, clipInpBuffer_.Get(), kVelloStrideClipInp, nClipInp);
            w.SrvStructured(b + 1, pathBboxBuffer_.Get(), kVelloStridePathBbox, nPathBbox);
            w.SrvStructured(b + 2, clipBicBuffer_.Get(), kVelloStrideClipBic, nClipBic);
            w.SrvStructured(b + 3, clipElBuffer_.Get(), kVelloStrideClipEl, nClipEl);
            w.UavStructured(b + 5, drawMonoidBuffer_.Get(), kVelloStrideDrawMonoid,
                            nDrawMonoid);
            w.UavStructured(b + 6, clipBboxBuffer_.Get(), kVelloStrideClipBbox, nClipBbox);
        }
        {
            uint32_t b = stageBase(kStageBinning);
            w.SrvStructured(b + 0, drawMonoidBuffer_.Get(), kVelloStrideDrawMonoid,
                            nDrawMonoid);
            w.SrvStructured(b + 1, pathBboxBuffer_.Get(), kVelloStridePathBbox, nPathBbox);
            w.SrvStructured(b + 2, clipBboxBuffer_.Get(), kVelloStrideClipBbox, nClipBbox);
            w.UavStructured(b + 5, drawBboxBuffer_.Get(), kVelloStrideDrawBbox, nDrawBbox);
            w.UavRaw(b + 6, bumpBuffer_.Get(), nBumpWords);
            w.UavStructured(b + 7, infoBinDataBuffer_.Get(), 4, nInfoBin);
            w.UavStructured(b + 8, binHeaderBuffer_.Get(), kVelloStrideBinHeader,
                            nBinHeader);
        }
        {
            uint32_t b = stageBase(kStageTileAlloc);
            w.SrvStructured(b + 0, sceneBuffer_.Get(), 4, nScene);
            w.SrvStructured(b + 1, drawBboxBuffer_.Get(), kVelloStrideDrawBbox, nDrawBbox);
            w.UavRaw(b + 5, bumpBuffer_.Get(), nBumpWords);
            w.UavStructured(b + 6, pathBuffer_.Get(), kVelloStridePath, nPath);
            w.UavStructured(b + 7, tileBuffer_.Get(), kVelloStrideTile, nTile);
        }
        {
            uint32_t b = stageBase(kStagePathCountSetup);
            w.UavRaw(b + 5, bumpBuffer_.Get(), nBumpWords);
            w.UavRaw(b + 6, indirectBuffer_.Get(), 3);
        }
        {
            uint32_t b = stageBase(kStagePathCount);
            w.SrvStructured(b + 0, lineSoupBuffer_.Get(), kVelloStrideLineSoup, nLineSoup);
            w.SrvStructured(b + 1, pathBuffer_.Get(), kVelloStridePath, nPath);
            w.UavRaw(b + 5, bumpBuffer_.Get(), nBumpWords);
            w.UavRaw(b + 6, tileBuffer_.Get(), nTile * kVelloStrideTile / 4);
            w.UavStructured(b + 7, segCountBuffer_.Get(), kVelloStrideSegCount, nSegCount);
        }
        {
            uint32_t b = stageBase(kStageBackdrop);
            w.SrvStructured(b + 0, pathBuffer_.Get(), kVelloStridePath, nPath);
            w.UavRaw(b + 5, bumpBuffer_.Get(), nBumpWords);
            w.UavStructured(b + 6, tileBuffer_.Get(), kVelloStrideTile, nTile);
        }
        {
            uint32_t b = stageBase(kStageCoarse);
            w.SrvStructured(b + 0, sceneBuffer_.Get(), 4, nScene);
            w.SrvStructured(b + 1, drawMonoidBuffer_.Get(), kVelloStrideDrawMonoid,
                            nDrawMonoid);
            w.SrvStructured(b + 2, binHeaderBuffer_.Get(), kVelloStrideBinHeader,
                            nBinHeader);
            w.SrvStructured(b + 3, infoBinDataBuffer_.Get(), 4, nInfoBin);
            w.SrvStructured(b + 4, pathBuffer_.Get(), kVelloStridePath, nPath);
            w.UavStructured(b + 5, tileBuffer_.Get(), kVelloStrideTile, nTile);
            w.UavRaw(b + 6, bumpBuffer_.Get(), nBumpWords);
            w.UavStructured(b + 7, ptclBuffer_.Get(), 4, nPtcl);
        }
        {
            uint32_t b = stageBase(kStagePathTilingSetup);
            w.UavRaw(b + 5, bumpBuffer_.Get(), nBumpWords);
            w.UavRaw(b + 6, indirectBuffer_.Get(), 3);
            w.UavStructured(b + 7, ptclBuffer_.Get(), 4, nPtcl);
        }
        {
            uint32_t b = stageBase(kStagePathTiling);
            w.SrvStructured(b + 0, segCountBuffer_.Get(), kVelloStrideSegCount, nSegCount);
            w.SrvStructured(b + 1, lineSoupBuffer_.Get(), kVelloStrideLineSoup, nLineSoup);
            w.SrvStructured(b + 2, pathBuffer_.Get(), kVelloStridePath, nPath);
            w.SrvStructured(b + 3, tileBuffer_.Get(), kVelloStrideTile, nTile);
            w.UavRaw(b + 5, bumpBuffer_.Get(), nBumpWords);
            w.UavStructured(b + 6, segmentBuffer_.Get(), kVelloStrideSegment, nSegment);
        }
    }

    {
        uint32_t b = stageBase(kStageFine);
        w.SrvStructured(b + 0, segmentBuffer_.Get(), kVelloStrideSegment, nSegment);
        w.SrvStructured(b + 1, ptclBuffer_.Get(), 4, nPtcl);
        w.SrvStructured(b + 2, infoBinDataBuffer_.Get(), 4, nInfoBin);
        w.SrvTexture(b + 3, rampTexture_.Get());
        w.SrvTexture(b + 4, dummyAtlasTexture_.Get());
        w.UavStructured(b + 5, blendSpillBuffer_.Get(), 4, nBlend);
        w.UavTexture(b + 6, outputTexture_.Get());
    }

    if (!sharedValid) {
        cachedSharedFrame_ = frameSlot;
        cachedSharedGen_ = resourceGeneration_;
        cachedSharedBase_ = sharedBase;
    }

    const auto perfT3 = perf ? std::chrono::steady_clock::now() : perfT0;

    cmdList->SetDescriptorHeaps(1, &heapPtr);

    // Fine writes every pixel in the region, including empty tiles and the
    // allocator-failure path. Recycled output needs no separate UAV clear.
    StageProfileRecord* stageProfile = BeginStageProfile(cmdList, frameIndex);

    // ------------------------------------------------------------------
    // The dispatch graph
    // ------------------------------------------------------------------
    cmdList->SetComputeRootSignature(rootSig_.Get());
    cmdList->SetComputeRootConstantBufferView(
        0, configSlice.resource->GetGPUVirtualAddress() + configSlice.offset);

    auto runStage = [&](Stage s, uint32_t x, uint32_t y, uint32_t z) {
        cmdList->SetPipelineState(psos_[s].Get());
        cmdList->SetComputeRootDescriptorTable(1, w.Gpu(stageBase(s)));
        cmdList->SetComputeRootDescriptorTable(2, w.Gpu(stageBase(s) + kSrvSlots));
        cmdList->Dispatch(x, y, z);
        UavBarrier(cmdList);
        if (stageProfile) MarkStageProfile(cmdList, frameIndex, stageProfile, s);
    };

    if (!useCpuTagScan) {
        // Small scan's group zero uses the identity parent and does not read
        // reduced[]. The large permutation still needs the complete chain.
        if (!useSmallPathScan || ri.pathtagReduceWgs > 1) {
            runStage(kStagePathtagReduce, ri.pathtagReduceWgs, 1, 1);
        }
        if (!useSmallPathScan) {
            runStage(kStagePathtagReduce2, ri.pathtagReduce2Wgs, 1, 1);
            runStage(kStagePathtagScan1, ri.pathtagScan1Wgs, 1, 1);
            runStage(kStagePathtagScan, ri.pathtagScanWgs, 1, 1);
        } else {
            runStage(kStagePathtagScanSmall, ri.pathtagScanWgs, 1, 1);
        }
    }
    runStage(kStageBboxClear, ri.bboxClearWgs, 1, 1);
    runStage(kStageFlatten, ri.flattenWgs, 1, 1);
    // draw_leaf also reads reduced[] only for preceding workgroups.
    if (ri.drawReduceWgs > 1) {
        runStage(kStageDrawReduce, ri.drawReduceWgs, 1, 1);
    }
    runStage(kStageDrawLeaf, ri.drawReduceWgs, 1, 1);
    if (ri.clipReduceWgs > 0) {
        runStage(kStageClipReduce, ri.clipReduceWgs, 1, 1);
    }
    if (ri.clipLeafWgs > 0) {
        runStage(kStageClipLeaf, ri.clipLeafWgs, 1, 1);
    }
    runStage(kStageBinning, ri.binningWgs, 1, 1);
    runStage(kStageTileAlloc, ri.tileAllocWgs, 1, 1);

    // path_count_setup -> indirect path_count
    runStage(kStagePathCountSetup, 1, 1, 1);
    Transition(cmdList, indirectBuffer_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
               D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
    cmdList->SetPipelineState(psos_[kStagePathCount].Get());
    cmdList->SetComputeRootDescriptorTable(1, w.Gpu(stageBase(kStagePathCount)));
    cmdList->SetComputeRootDescriptorTable(2, w.Gpu(stageBase(kStagePathCount) + kSrvSlots));
    cmdList->ExecuteIndirect(dispatchIndirectSig_.Get(), 1, indirectBuffer_.Get(), 0, nullptr,
                             0);
    UavBarrier(cmdList);
    Transition(cmdList, indirectBuffer_.Get(), D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT,
               D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
    if (stageProfile) {
        MarkStageProfile(cmdList, frameIndex, stageProfile, kStagePathCount);
    }

    runStage(kStageBackdrop, ri.backdropWgs, 1, 1);
    runStage(kStageCoarse, ri.widthInBins, ri.heightInBins, 1);

    // path_tiling_setup -> indirect path_tiling
    runStage(kStagePathTilingSetup, 1, 1, 1);
    Transition(cmdList, indirectBuffer_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
               D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
    cmdList->SetPipelineState(psos_[kStagePathTiling].Get());
    cmdList->SetComputeRootDescriptorTable(1, w.Gpu(stageBase(kStagePathTiling)));
    cmdList->SetComputeRootDescriptorTable(2, w.Gpu(stageBase(kStagePathTiling) + kSrvSlots));
    cmdList->ExecuteIndirect(dispatchIndirectSig_.Get(), 1, indirectBuffer_.Get(), 0, nullptr,
                             0);
    UavBarrier(cmdList);
    Transition(cmdList, indirectBuffer_.Get(), D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT,
               D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
    if (stageProfile) {
        MarkStageProfile(cmdList, frameIndex, stageProfile, kStagePathTiling);
    }

    runStage(kStageFine, cfg.width_in_tiles, cfg.height_in_tiles, 1);
    if (stageProfile) ResolveStageProfile(cmdList, frameIndex, stageProfile);

    // ------------------------------------------------------------------
    // Return everything to COMMON (mid-frame dispatches share one
    // ExecuteCommandLists, so no implicit state decay happens in between).
    // ------------------------------------------------------------------
    if (batchBarriers) {
        TransitionMany(cmdList, uavResources, D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                       D3D12_RESOURCE_STATE_COMMON);
    } else {
        for (ID3D12Resource* r : uavResources) {
            Transition(cmdList, r, D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                       D3D12_RESOURCE_STATE_COMMON);
        }
    }
    Transition(cmdList, bumpBuffer_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
               D3D12_RESOURCE_STATE_COMMON);
    Transition(cmdList, sceneBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,
               D3D12_RESOURCE_STATE_COMMON);
    Transition(cmdList, rampTexture_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,
               D3D12_RESOURCE_STATE_COMMON);
    Transition(cmdList, dummyAtlasTexture_.Get(),
               D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
    Transition(cmdList, outputTexture_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
               D3D12_RESOURCE_STATE_COMMON);

    // The arena keeps every slice handed out this frame alive until the slot's
    // fence is observed (RecycleFrameResources resets the offset), so nothing
    // is retired per dispatch any more.

    if (perf) {
        g_perf.dispatches++;
        g_perf.cpuMicros +=
            std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now() - perfT0)
                .count();
        g_perf.pathTagBytes += packed.numPathTagBytes;
        g_perf.drawObjs += cfg.n_drawobj;
        g_perf.sceneWords += (uint64_t)packed.data.size();
        g_perf.fineWorkgroups += (uint64_t)cfg.width_in_tiles * cfg.height_in_tiles;
        if (useCpuTagScan) g_perf.cpuScans++;
        else if (useSmallPathScan) g_perf.smallScans++;
        else g_perf.largeScans++;
        auto us = [](auto a2, auto b2) {
            return std::chrono::duration<double, std::micro>(b2 - a2).count();
        };
        g_perf.encodeMicros += us(perfT0, perfT1);
        g_perf.bufferMicros += us(perfT1, perfT2);
        g_perf.descMicros += us(perfT2, perfT3);
        g_perf.recordMicros += us(perfT3, std::chrono::steady_clock::now());
    }
    return true;
}

}  // namespace jalium
