#include "d3d12_vello.h"
#include "d3d12_shader_source.h"
#include "d3d12_vello_bytecode.h"
#include "d3d12_triangulate.h"  // for path command tags
#include "d3d12_resources.h"    // for D3D12SolidBrush, D3D12LinearGradientBrush, etc.
#include <d3dcompiler.h>
#include <cstring>
#include <cstdio>
#include <algorithm>
#include <fstream>
#include <sstream>

namespace jalium {

// ============================================================================
// Custom ID3DInclude for shader compilation
// Resolves #include "vello_shared.hlsli" at runtime from disk.
// ============================================================================
class VelloShaderInclude : public ID3DInclude {
public:
    VelloShaderInclude(const std::string& shaderDir) : shaderDir_(shaderDir) {}

    HRESULT __stdcall Open(D3D_INCLUDE_TYPE /*type*/, LPCSTR pFileName,
                           LPCVOID /*pParentData*/, LPCVOID* ppData, UINT* pBytes) override {
        std::string path = shaderDir_ + "\\" + pFileName;
        std::ifstream f(path, std::ios::binary);
        if (!f.is_open()) return E_FAIL;
        std::ostringstream ss;
        ss << f.rdbuf();
        auto content = new std::string(ss.str());
        *ppData = content->c_str();
        *pBytes = (UINT)content->size();
        buffers_.push_back(content);
        return S_OK;
    }

    HRESULT __stdcall Close(LPCVOID /*pData*/) override {
        return S_OK;
    }

    ~VelloShaderInclude() {
        for (auto* p : buffers_) delete p;
    }

private:
    std::string shaderDir_;
    std::vector<std::string*> buffers_;
};

// ============================================================================
// Multi-Atlas Allocator Implementation
// ============================================================================

AtlasAllocator::AtlasAllocator(uint32_t width, uint32_t height)
    : width_(width), height_(height)
{
    freeRects_.push_back({0, 0, width, height});
}

bool AtlasAllocator::Allocate(uint32_t w, uint32_t h, AtlasAllocation& out)
{
    // Best short-side fit: find the free rect that minimizes wasted short side
    int bestIdx = -1;
    uint32_t bestShortSide = UINT32_MAX;
    for (int i = 0; i < (int)freeRects_.size(); i++) {
        auto& r = freeRects_[i];
        if (r.w >= w && r.h >= h) {
            uint32_t shortSide = std::min(r.w - w, r.h - h);
            if (shortSide < bestShortSide) {
                bestShortSide = shortSide;
                bestIdx = i;
            }
        }
    }
    if (bestIdx < 0) return false;

    auto& chosen = freeRects_[bestIdx];
    out.x = chosen.x; out.y = chosen.y;
    out.w = w; out.h = h;
    out.allocId = nextAllocId_++;
    allocatedArea_ += w * h;

    // Guillotine split: split remaining space into two rects
    uint32_t rightW = chosen.w - w, bottomH = chosen.h - h;
    FreeRect right = {chosen.x + w, chosen.y, rightW, h};
    FreeRect bottom = {chosen.x, chosen.y + h, chosen.w, bottomH};

    // Remove chosen rect and add splits (if non-degenerate)
    freeRects_.erase(freeRects_.begin() + bestIdx);
    if (right.w > 0 && right.h > 0) freeRects_.push_back(right);
    if (bottom.w > 0 && bottom.h > 0) freeRects_.push_back(bottom);

    return true;
}

void AtlasAllocator::Free(uint32_t /*allocId*/)
{
    // Simple implementation: no coalescing. Reset when atlas is cleared.
    // Full implementation would track allocations and coalesce free rects.
}

void AtlasAllocator::Reset()
{
    freeRects_.clear();
    freeRects_.push_back({0, 0, width_, height_});
    nextAllocId_ = 0;
    allocatedArea_ = 0;
}

MultiAtlasManager::MultiAtlasManager(uint32_t atlasWidth, uint32_t atlasHeight)
    : atlasWidth_(atlasWidth), atlasHeight_(atlasHeight)
{
}

AtlasAllocation MultiAtlasManager::Allocate(uint32_t w, uint32_t h)
{
    // Try existing atlases first
    for (auto& entry : atlases_) {
        AtlasAllocation alloc;
        if (entry.allocator.Allocate(w, h, alloc)) {
            alloc.atlasId = entry.id;
            return alloc;
        }
    }
    // Create a new atlas
    uint32_t id = nextAtlasId_++;
    atlases_.push_back({id, AtlasAllocator(atlasWidth_, atlasHeight_)});
    AtlasAllocation alloc;
    if (atlases_.back().allocator.Allocate(w, h, alloc)) {
        alloc.atlasId = id;
        return alloc;
    }
    // Image too large for atlas
    alloc.atlasId = UINT32_MAX;
    alloc.x = alloc.y = alloc.w = alloc.h = 0;
    return alloc;
}

void MultiAtlasManager::Free(const AtlasAllocation& alloc)
{
    for (auto& entry : atlases_) {
        if (entry.id == alloc.atlasId) {
            entry.allocator.Free(alloc.allocId);
            return;
        }
    }
}

void MultiAtlasManager::Reset()
{
    for (auto& entry : atlases_) entry.allocator.Reset();
}

// -- Inline helpers --
static D3D12_HEAP_PROPERTIES MakeHeap(D3D12_HEAP_TYPE type) {
    D3D12_HEAP_PROPERTIES hp = {};
    hp.Type = type;
    return hp;
}

static D3D12_RESOURCE_DESC MakeBufDesc(UINT64 size) {
    D3D12_RESOURCE_DESC rd = {};
    rd.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    rd.Width = size;
    rd.Height = 1;
    rd.DepthOrArraySize = 1;
    rd.MipLevels = 1;
    rd.SampleDesc.Count = 1;
    rd.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    return rd;
}

static D3D12_RESOURCE_DESC MakeTex2D(DXGI_FORMAT fmt, UINT w, UINT h, D3D12_RESOURCE_FLAGS flags = D3D12_RESOURCE_FLAG_NONE) {
    D3D12_RESOURCE_DESC rd = {};
    rd.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    rd.Width = w;
    rd.Height = h;
    rd.DepthOrArraySize = 1;
    rd.MipLevels = 1;
    rd.Format = fmt;
    rd.SampleDesc.Count = 1;
    rd.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    rd.Flags = flags;
    return rd;
}

static D3D12_RESOURCE_BARRIER MakeBarrier(ID3D12Resource* res, D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after) {
    D3D12_RESOURCE_BARRIER b = {};
    b.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    b.Transition.pResource = res;
    b.Transition.StateBefore = before;
    b.Transition.StateAfter = after;
    b.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    return b;
}

static bool CreateBuffer(ID3D12Device* dev, UINT64 size, D3D12_HEAP_TYPE heap,
                          D3D12_RESOURCE_FLAGS flags, D3D12_RESOURCE_STATES state,
                          ComPtr<ID3D12Resource>& out) {
    auto hp = MakeHeap(heap);
    auto desc = MakeBufDesc(size);
    desc.Flags = flags;
    return SUCCEEDED(dev->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &desc, state, nullptr, IID_PPV_ARGS(&out)));
}

// ============================================================================
// Construction
// ============================================================================

D3D12VelloRenderer::D3D12VelloRenderer(ID3D12Device* device, ShaderBlobCache* shaderCache)
    : device_(device), shaderCache_(shaderCache)
{
}

D3D12VelloRenderer::~D3D12VelloRenderer() = default;

// ============================================================================
// Initialization
// ============================================================================

bool D3D12VelloRenderer::Initialize()
{
    if (!CreateRootSignature()) { OutputDebugStringA("[Vello] CreateRootSignature FAILED\n"); return false; }
    if (!CreateComputePipelines()) { OutputDebugStringA("[Vello] CreateComputePipelines FAILED\n"); return false; }

    // Create per-frame compute SRV/UAV descriptor heaps.
    // Must be per-frame: CPU writes descriptors for frame N+1 while GPU may
    // still be reading frame N's descriptors during fine shader execution.
    srvDescSize_ = device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
    for (uint32_t f = 0; f < kMaxFrames; f++) {
        D3D12_DESCRIPTOR_HEAP_DESC heapDesc = {};
        heapDesc.NumDescriptors = 28;  // t0-t6 (7 SRVs) + u0-u4 (5 UAVs) + extras
        heapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        heapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        if (FAILED(device_->CreateDescriptorHeap(&heapDesc, IID_PPV_ARGS(&computeSrvHeap_[f]))))
            return false;
    }

    // Create per-frame upload buffers for line count + constants
    for (uint32_t f = 0; f < kMaxFrames; f++) {
        auto& fu = frameUploads_[f];
        if (!CreateBuffer(device_, 256, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
                          D3D12_RESOURCE_STATE_GENERIC_READ, fu.lineCountUpload))
            return false;
        {
            void* mapped = nullptr;
            fu.lineCountUpload->Map(0, nullptr, &mapped);
            memset(mapped, 0, 256);
            fu.lineCountUpload->Unmap(0, nullptr);
        }
        if (!CreateBuffer(device_, 256, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
                          D3D12_RESOURCE_STATE_GENERIC_READ, fu.constantUpload))
            return false;
    }

    // Non-shader-visible CPU descriptor heap for ClearUnorderedAccessViewFloat
    {
        D3D12_DESCRIPTOR_HEAP_DESC cpuDesc = {};
        cpuDesc.NumDescriptors = 1;
        cpuDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        cpuDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;  // CPU-only
        if (FAILED(device_->CreateDescriptorHeap(&cpuDesc, IID_PPV_ARGS(&cpuUavHeap_))))
            return false;
    }

    initialized_ = true;

    // Vello compute pipelines (13 PSOs) are created lazily inside DispatchGPU
    // on the first path-rendering frame.  Pre-warming here used to avoid an
    // ~800ms first-path latency in debug builds, but it also held PSO memory
    // permanently — even for apps that never draw paths.  DispatchGPU's lazy
    // CreateGPUPipeline path handles the cold-start case automatically.
    return true;
}

bool D3D12VelloRenderer::CreateRootSignature()
{
    // Root signature for Vello compute shaders:
    //   [0] CBV b0 - VelloConstants
    //   [1] Descriptor table - SRV t0-t4 (lineSegs, pathDraws, pathInfos, ptcl, ptclOffsets)
    //   [2] Descriptor table - UAV u0-u4 (lineSegs, lineCount, tiles, tileCmds, output)

    D3D12_DESCRIPTOR_RANGE srvRange = {};
    srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
    srvRange.NumDescriptors = 7;  // t0-t6 (t5=gradient ramps, t6=image atlas)
    srvRange.BaseShaderRegister = 0;

    D3D12_DESCRIPTOR_RANGE uavRange = {};
    uavRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV;
    uavRange.NumDescriptors = 5;
    uavRange.BaseShaderRegister = 0;

    D3D12_ROOT_PARAMETER params[3] = {};
    // [0] CBV
    params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
    params[0].Descriptor.ShaderRegister = 0;
    params[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // [1] SRV table
    params[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    params[1].DescriptorTable.NumDescriptorRanges = 1;
    params[1].DescriptorTable.pDescriptorRanges = &srvRange;
    params[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // [2] UAV table
    params[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    params[2].DescriptorTable.NumDescriptorRanges = 1;
    params[2].DescriptorTable.pDescriptorRanges = &uavRange;
    params[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // Static sampler for image brush (bilinear, clamp)
    D3D12_STATIC_SAMPLER_DESC samplerDesc = {};
    samplerDesc.Filter = D3D12_FILTER_MIN_MAG_MIP_LINEAR;
    samplerDesc.AddressU = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplerDesc.AddressV = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplerDesc.AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    samplerDesc.ShaderRegister = 0;  // s0
    samplerDesc.ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    D3D12_ROOT_SIGNATURE_DESC desc = {};
    desc.NumParameters = 3;
    desc.pParameters = params;
    desc.NumStaticSamplers = 1;
    desc.pStaticSamplers = &samplerDesc;

    ComPtr<ID3DBlob> signature, error;
    if (FAILED(D3D12SerializeRootSignature(&desc, D3D_ROOT_SIGNATURE_VERSION_1_0, &signature, &error))) {
        if (error) OutputDebugStringA((const char*)error->GetBufferPointer());
        return false;
    }
    return SUCCEEDED(device_->CreateRootSignature(0, signature->GetBufferPointer(),
                     signature->GetBufferSize(), IID_PPV_ARGS(&cpuRootSig_)));
}

bool D3D12VelloRenderer::CreateComputePipelines()
{
    // Use pre-compiled bytecode for CPU fine shader (same shader as GPU pipeline).
    // This eliminates ~200ms of runtime D3DCompile.
    using namespace vello_bytecode;
    D3D12_COMPUTE_PIPELINE_STATE_DESC cpso = {};
    cpso.pRootSignature = cpuRootSig_.Get();
    cpso.CS = { kFine, kFineSize };
    if (FAILED(device_->CreateComputePipelineState(&cpso, IID_PPV_ARGS(&cpuFinePSO_)))) {
        OutputDebugStringA("[Vello] CPU fine PSO creation from pre-compiled bytecode FAILED\n");
        return false;
    }
    return true;
}

// ============================================================================
// Buffer Management
// ============================================================================

bool D3D12VelloRenderer::EnsureBuffers()
{
    uint32_t neededSegs = std::max((uint32_t)segments_.size(), 1u);
    uint32_t neededPaths = std::max((uint32_t)pathInfos_.size(), 1u);
    // Estimate max line segments: each cubic can produce up to 128 lines
    uint32_t neededLineSegs = std::max(neededSegs * 32, 4096u);
    uint32_t neededTiles = tilesX_ * tilesY_;
    uint32_t neededTileCmds = neededTiles * 256;  // MAX_CMDS_PER_TILE

    bool needRebuild = false;
    if (neededSegs > segmentCapacity_) { segmentCapacity_ = neededSegs * 2; needRebuild = true; }
    if (neededPaths > pathCapacity_) { pathCapacity_ = neededPaths * 2; needRebuild = true; }
    if (neededLineSegs > lineSegCapacity_) { lineSegCapacity_ = neededLineSegs * 2; needRebuild = true; }
    if (neededTiles > tileCapacity_) { tileCapacity_ = neededTiles; needRebuild = true; }
    if (neededTileCmds > tileCmdCapacity_) { tileCmdCapacity_ = neededTileCmds; needRebuild = true; }

    if (!needRebuild && segmentBuffer_) return true;

    // GPU buffers (DEFAULT heap, UAV-capable)
    auto flags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
    auto state = D3D12_RESOURCE_STATE_COMMON;

    if (!CreateBuffer(device_, segmentCapacity_ * sizeof(PathSegment), D3D12_HEAP_TYPE_DEFAULT, flags, state, segmentBuffer_)) return false;
    if (!CreateBuffer(device_, pathCapacity_ * sizeof(PathInfo), D3D12_HEAP_TYPE_DEFAULT, flags, state, pathInfoBuffer_)) return false;
    if (!CreateBuffer(device_, pathCapacity_ * sizeof(PathDraw), D3D12_HEAP_TYPE_DEFAULT, flags, state, pathDrawBuffer_)) return false;
    if (!CreateBuffer(device_, lineSegCapacity_ * sizeof(VelloSortedSeg), D3D12_HEAP_TYPE_DEFAULT, flags, state, lineSegBuffer_)) return false;
    if (!CreateBuffer(device_, 256, D3D12_HEAP_TYPE_DEFAULT, flags, state, lineCountBuffer_)) return false;
    if (!CreateBuffer(device_, tileCapacity_ * 12, D3D12_HEAP_TYPE_DEFAULT, flags, state, tileBuffer_)) return false;
    if (!CreateBuffer(device_, tileCmdCapacity_ * 12, D3D12_HEAP_TYPE_DEFAULT, flags, state, tileCmdBuffer_)) return false;

    // Upload buffers - per-frame to avoid GPU race conditions
    auto uploadState = D3D12_RESOURCE_STATE_GENERIC_READ;
    for (uint32_t f = 0; f < kMaxFrames; f++) {
        auto& fu = frameUploads_[f];
        if (!CreateBuffer(device_, segmentCapacity_ * sizeof(PathSegment), D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE, uploadState, fu.segmentUpload)) return false;
        if (!CreateBuffer(device_, pathCapacity_ * sizeof(PathInfo), D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE, uploadState, fu.pathInfoUpload)) return false;
        if (!CreateBuffer(device_, pathCapacity_ * sizeof(PathDraw), D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE, uploadState, fu.pathDrawUpload)) return false;
    }

    return true;
}

bool D3D12VelloRenderer::EnsureOutputTexture(uint32_t w, uint32_t h)
{
    if (outputTexture_ && outputW_ == w && outputH_ == h) return true;

    auto hp = MakeHeap(D3D12_HEAP_TYPE_DEFAULT);
    auto desc = MakeTex2D(DXGI_FORMAT_R8G8B8A8_UNORM, w, h, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);

    if (FAILED(device_->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &desc,
            D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&outputTexture_))))
        return false;

    outputW_ = w;
    outputH_ = h;
    return true;
}

// ============================================================================
// Path Encoding
// ============================================================================

void D3D12VelloRenderer::BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight)
{
    segments_.clear();
    pathInfos_.clear();
    pathDraws_.clear();
    cpuLineSegs_.clear();
    gradientRamps_.clear();
    gradientCount_ = 0;
    clipStack_.clear();
    clipDepth_ = 0;
    clipEvents_.clear();
    drawTags_.clear();
    imageEntries_.clear();
    totalPathTiles_ = 0;
    viewportW_ = viewportWidth;
    viewportH_ = viewportHeight;
    tilesX_ = (viewportWidth + kTileWidth - 1) / kTileWidth;
    tilesY_ = (viewportHeight + kTileHeight - 1) / kTileHeight;
}

// ============================================================================
// Gradient Ramp Generation
// ============================================================================

// Color space conversion helpers for gradient interpolation (Vello-style)
namespace {

// sRGB gamma -> linear
inline float srgb_to_linear(float c) {
    return (c <= 0.04045f) ? c / 12.92f : std::pow((c + 0.055f) / 1.055f, 2.4f);
}
// linear -> sRGB gamma
inline float linear_to_srgb(float c) {
    c = std::max(0.0f, std::min(1.0f, c));
    return (c <= 0.0031308f) ? c * 12.92f : 1.055f * std::pow(c, 1.0f / 2.4f) - 0.055f;
}

// sRGB -> OKLab (Bjorn Ottosson)
struct OKLab { float L, a, b; };
inline OKLab srgb_to_oklab(float sr, float sg, float sb) {
    float r = srgb_to_linear(sr), g = srgb_to_linear(sg), b = srgb_to_linear(sb);
    float l = 0.4122214708f*r + 0.5363325363f*g + 0.0514459929f*b;
    float m = 0.2119034982f*r + 0.6806995451f*g + 0.1073969566f*b;
    float s = 0.0883024619f*r + 0.2817188376f*g + 0.6299787005f*b;
    float l_ = std::cbrt(l), m_ = std::cbrt(m), s_ = std::cbrt(s);
    return {
        0.2104542553f*l_ + 0.7936177850f*m_ - 0.0040720468f*s_,
        1.9779984951f*l_ - 2.4285922050f*m_ + 0.4505937099f*s_,
        0.0259040371f*l_ + 0.7827717662f*m_ - 0.8086757660f*s_
    };
}
// OKLab -> sRGB
inline void oklab_to_srgb(float L, float a, float b, float& sr, float& sg, float& sb) {
    float l_ = L + 0.3963377774f*a + 0.2158037573f*b;
    float m_ = L - 0.1055613458f*a - 0.0638541728f*b;
    float s_ = L - 0.0894841775f*a - 1.2914855480f*b;
    float l = l_*l_*l_, m = m_*m_*m_, s = s_*s_*s_;
    float rl = +4.0767416621f*l - 3.3077115913f*m + 0.2309699292f*s;
    float gl = -1.2684380046f*l + 2.6097574011f*m - 0.3413193965f*s;
    float bl = -0.0041960863f*l - 0.7034186147f*m + 1.7076147010f*s;
    sr = linear_to_srgb(rl); sg = linear_to_srgb(gl); sb = linear_to_srgb(bl);
}

} // anon namespace

uint32_t D3D12VelloRenderer::AddGradientRamp(const GradStop* stops, uint32_t stopCount,
                                              uint32_t colorSpace)
{
    if (stopCount == 0 || gradientCount_ >= kMaxGradients) return 0;

    uint32_t idx = gradientCount_++;
    gradientRamps_.resize(gradientCount_ * kGradientRampWidth);
    uint32_t* ramp = &gradientRamps_[idx * kGradientRampWidth];

    if (stopCount == 1) {
        auto& s = stops[0];
        uint32_t packed = ((uint32_t)(s.color.r * 255.0f + 0.5f))
                        | ((uint32_t)(s.color.g * 255.0f + 0.5f) << 8)
                        | ((uint32_t)(s.color.b * 255.0f + 0.5f) << 16)
                        | ((uint32_t)(s.color.a * 255.0f + 0.5f) << 24);
        for (uint32_t i = 0; i < kGradientRampWidth; i++) ramp[i] = packed;
        return idx;
    }

    // Interpolate between sorted stops in the requested color space
    for (uint32_t i = 0; i < kGradientRampWidth; i++) {
        float t = (float)i / (float)(kGradientRampWidth - 1);

        // Find enclosing stops
        uint32_t lo = 0, hi = stopCount - 1;
        for (uint32_t s = 0; s < stopCount; s++) {
            if (stops[s].position <= t) lo = s;
        }
        for (uint32_t s = stopCount; s-- > 0; ) {
            if (stops[s].position >= t) hi = s;
        }

        float r, g, b, a;
        if (lo == hi || stops[hi].position <= stops[lo].position) {
            r = stops[lo].color.r; g = stops[lo].color.g;
            b = stops[lo].color.b; a = stops[lo].color.a;
        } else {
            float frac = (t - stops[lo].position) / (stops[hi].position - stops[lo].position);
            frac = std::max(0.0f, std::min(1.0f, frac));

            if (colorSpace == 1) {
                // Linear sRGB interpolation: convert to linear, interpolate, convert back
                float lr0 = srgb_to_linear(stops[lo].color.r), lr1 = srgb_to_linear(stops[hi].color.r);
                float lg0 = srgb_to_linear(stops[lo].color.g), lg1 = srgb_to_linear(stops[hi].color.g);
                float lb0 = srgb_to_linear(stops[lo].color.b), lb1 = srgb_to_linear(stops[hi].color.b);
                float lr = lr0 + (lr1 - lr0) * frac;
                float lg = lg0 + (lg1 - lg0) * frac;
                float lb = lb0 + (lb1 - lb0) * frac;
                r = linear_to_srgb(lr); g = linear_to_srgb(lg); b = linear_to_srgb(lb);
                a = stops[lo].color.a + (stops[hi].color.a - stops[lo].color.a) * frac;
            } else if (colorSpace == 2) {
                // OKLab interpolation: convert to OKLab, interpolate, convert back
                auto lab0 = srgb_to_oklab(stops[lo].color.r, stops[lo].color.g, stops[lo].color.b);
                auto lab1 = srgb_to_oklab(stops[hi].color.r, stops[hi].color.g, stops[hi].color.b);
                float L = lab0.L + (lab1.L - lab0.L) * frac;
                float A = lab0.a + (lab1.a - lab0.a) * frac;
                float B = lab0.b + (lab1.b - lab0.b) * frac;
                oklab_to_srgb(L, A, B, r, g, b);
                a = stops[lo].color.a + (stops[hi].color.a - stops[lo].color.a) * frac;
            } else {
                // sRGB gamma interpolation (default, same as before)
                r = stops[lo].color.r + (stops[hi].color.r - stops[lo].color.r) * frac;
                g = stops[lo].color.g + (stops[hi].color.g - stops[lo].color.g) * frac;
                b = stops[lo].color.b + (stops[hi].color.b - stops[lo].color.b) * frac;
                a = stops[lo].color.a + (stops[hi].color.a - stops[lo].color.a) * frac;
            }
        }

        // Premultiply alpha
        float pr = std::max(0.0f, std::min(1.0f, r)) * a;
        float pg = std::max(0.0f, std::min(1.0f, g)) * a;
        float pb = std::max(0.0f, std::min(1.0f, b)) * a;

        ramp[i] = ((uint32_t)(std::min(pr, 1.0f) * 255.0f + 0.5f))
                 | ((uint32_t)(std::min(pg, 1.0f) * 255.0f + 0.5f) << 8)
                 | ((uint32_t)(std::min(pb, 1.0f) * 255.0f + 0.5f) << 16)
                 | ((uint32_t)(std::min(a, 1.0f) * 255.0f + 0.5f) << 24);
    }
    return idx;
}

// ============================================================================
// Path Geometry Encoding (shared between solid/brush versions)
// ============================================================================

bool D3D12VelloRenderer::EncodePathGeometry(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    uint32_t fillRule,
    float m11, float m12, float m21, float m22, float tdx, float tdy,
    uint32_t& outPathIdx)
{
    uint32_t pathIdx = (uint32_t)pathInfos_.size();
    uint32_t segOffset = (uint32_t)segments_.size();

    currentBboxMinX_ = startX;
    currentBboxMinY_ = startY;
    currentBboxMaxX_ = startX;
    currentBboxMaxY_ = startY;

    float curX = startX, curY = startY;
    float moveX = startX, moveY = startY;
    bool hasDrawnInSubpath = false;  // track if any draw commands since last MoveTo

    uint32_t ci = 0;
    while (ci < commandLength) {
        int tag = (int)commands[ci];
        switch (tag) {
        case kTagMoveTo: {
            if (ci + 3 > commandLength) goto done;
            // Implicit close of previous subpath (only if we actually drew something)
            if (hasDrawnInSubpath && (curX != moveX || curY != moveY)) {
                PathSegment closeSeg = {};
                closeSeg.p0x = curX; closeSeg.p0y = curY;
                closeSeg.p1x = moveX; closeSeg.p1y = moveY;
                closeSeg.p2x = moveX; closeSeg.p2y = moveY;
                closeSeg.p3x = moveX; closeSeg.p3y = moveY;
                closeSeg.tag = kSegTagLineTo; closeSeg.pathIndex = pathIdx;
                segments_.push_back(closeSeg);
            }
            curX = commands[ci + 1]; curY = commands[ci + 2];
            moveX = curX; moveY = curY;
            hasDrawnInSubpath = false;
            ci += 3;
            break;
        }
        case kTagLineTo: {
            if (ci + 3 > commandLength) goto done;
            float ex = commands[ci + 1], ey = commands[ci + 2];
            PathSegment seg = {};
            seg.p0x = curX; seg.p0y = curY;
            seg.p1x = ex; seg.p1y = ey;
            seg.p2x = ex; seg.p2y = ey;
            seg.p3x = ex; seg.p3y = ey;
            seg.tag = kSegTagLineTo; seg.pathIndex = pathIdx;
            segments_.push_back(seg);
            currentBboxMinX_ = std::min(currentBboxMinX_, ex);
            currentBboxMinY_ = std::min(currentBboxMinY_, ey);
            currentBboxMaxX_ = std::max(currentBboxMaxX_, ex);
            currentBboxMaxY_ = std::max(currentBboxMaxY_, ey);
            curX = ex; curY = ey;
            hasDrawnInSubpath = true;
            ci += 3;
            break;
        }
        case kTagQuadTo: {
            if (ci + 5 > commandLength) goto done;
            float cpx = commands[ci + 1], cpy = commands[ci + 2];
            float ex = commands[ci + 3], ey = commands[ci + 4];
            PathSegment seg = {};
            seg.p0x = curX; seg.p0y = curY;
            seg.p1x = cpx; seg.p1y = cpy;
            seg.p2x = ex; seg.p2y = ey;
            seg.p3x = ex; seg.p3y = ey;
            seg.tag = kSegTagQuadTo; seg.pathIndex = pathIdx;
            segments_.push_back(seg);
            currentBboxMinX_ = std::min({currentBboxMinX_, cpx, ex});
            currentBboxMinY_ = std::min({currentBboxMinY_, cpy, ey});
            currentBboxMaxX_ = std::max({currentBboxMaxX_, cpx, ex});
            currentBboxMaxY_ = std::max({currentBboxMaxY_, cpy, ey});
            curX = ex; curY = ey;
            hasDrawnInSubpath = true;
            ci += 5;
            break;
        }
        case kTagCubicTo: {
            if (ci + 7 > commandLength) goto done;
            float c1x = commands[ci + 1], c1y = commands[ci + 2];
            float c2x = commands[ci + 3], c2y = commands[ci + 4];
            float ex = commands[ci + 5], ey = commands[ci + 6];
            PathSegment seg = {};
            seg.p0x = curX; seg.p0y = curY;
            seg.p1x = c1x; seg.p1y = c1y;
            seg.p2x = c2x; seg.p2y = c2y;
            seg.p3x = ex; seg.p3y = ey;
            seg.tag = kSegTagCubicTo; seg.pathIndex = pathIdx;
            segments_.push_back(seg);
            currentBboxMinX_ = std::min({currentBboxMinX_, c1x, c2x, ex});
            currentBboxMinY_ = std::min({currentBboxMinY_, c1y, c2y, ey});
            currentBboxMaxX_ = std::max({currentBboxMaxX_, c1x, c2x, ex});
            currentBboxMaxY_ = std::max({currentBboxMaxY_, c1y, c2y, ey});
            curX = ex; curY = ey;
            hasDrawnInSubpath = true;
            ci += 7;
            break;
        }
        case kTagArcTo: {
            if (ci + 8 > commandLength) goto done;
            float ex = commands[ci + 1], ey = commands[ci + 2];
            float rx = commands[ci + 3], ry = commands[ci + 4];
            float xRot = commands[ci + 5];
            bool largeArc = commands[ci + 6] != 0.0f;
            bool sweep = commands[ci + 7] != 0.0f;
            hasDrawnInSubpath = true;
            std::vector<float> arcPts;
            FlattenSvgArc(curX, curY, ex, ey, rx, ry, xRot, largeArc, sweep, arcPts, 0.25f);
            float px = curX, py = curY;
            for (size_t ai = 0; ai + 1 < arcPts.size(); ai += 2) {
                float nx = arcPts[ai], ny = arcPts[ai + 1];
                PathSegment seg = {};
                seg.p0x = px; seg.p0y = py;
                seg.p1x = nx; seg.p1y = ny;
                seg.p2x = nx; seg.p2y = ny;
                seg.p3x = nx; seg.p3y = ny;
                seg.tag = kSegTagLineTo; seg.pathIndex = pathIdx;
                segments_.push_back(seg);
                currentBboxMinX_ = std::min(currentBboxMinX_, nx);
                currentBboxMinY_ = std::min(currentBboxMinY_, ny);
                currentBboxMaxX_ = std::max(currentBboxMaxX_, nx);
                currentBboxMaxY_ = std::max(currentBboxMaxY_, ny);
                px = nx; py = ny;
            }
            curX = ex; curY = ey;
            ci += 8;
            break;
        }
        case kTagClosePath: {
            if (curX != moveX || curY != moveY) {
                PathSegment seg = {};
                seg.p0x = curX; seg.p0y = curY;
                seg.p1x = moveX; seg.p1y = moveY;
                seg.p2x = moveX; seg.p2y = moveY;
                seg.p3x = moveX; seg.p3y = moveY;
                seg.tag = kSegTagLineTo; seg.pathIndex = pathIdx;
                segments_.push_back(seg);
            }
            curX = moveX; curY = moveY;
            ci += 1;
            break;
        }
        default: ci += 1; break;
        }
    }
done:

    // Implicit close of last subpath
    if (hasDrawnInSubpath && (curX != moveX || curY != moveY)) {
        PathSegment seg = {};
        seg.p0x = curX; seg.p0y = curY;
        seg.p1x = moveX; seg.p1y = moveY;
        seg.p2x = moveX; seg.p2y = moveY;
        seg.p3x = moveX; seg.p3y = moveY;
        seg.tag = kSegTagLineTo; seg.pathIndex = pathIdx;
        segments_.push_back(seg);
    }

    uint32_t segCount = (uint32_t)segments_.size() - segOffset;
    if (segCount == 0) return false;  // no geometry to render

    // Apply transform
    bool hasTransform = (m11 != 1.0f || m12 != 0.0f || m21 != 0.0f || m22 != 1.0f || tdx != 0.0f || tdy != 0.0f);
    if (hasTransform) {
        currentBboxMinX_ = 1e30f; currentBboxMinY_ = 1e30f;
        currentBboxMaxX_ = -1e30f; currentBboxMaxY_ = -1e30f;
        for (uint32_t si = segOffset; si < (uint32_t)segments_.size(); si++) {
            auto& s = segments_[si];
            auto xfPt = [&](float& px, float& py) {
                float nx = px * m11 + py * m21 + tdx;
                float ny = px * m12 + py * m22 + tdy;
                px = nx; py = ny;
                currentBboxMinX_ = std::min(currentBboxMinX_, nx);
                currentBboxMinY_ = std::min(currentBboxMinY_, ny);
                currentBboxMaxX_ = std::max(currentBboxMaxX_, nx);
                currentBboxMaxY_ = std::max(currentBboxMaxY_, ny);
            };
            xfPt(s.p0x, s.p0y);
            xfPt(s.p1x, s.p1y);
            if (s.tag >= kSegTagQuadTo) xfPt(s.p2x, s.p2y);
            if (s.tag == kSegTagCubicTo) xfPt(s.p3x, s.p3y);
        }
    }

    // CPU flatten: convert PathSegments to LineSeg
    uint32_t lineSegStart = (uint32_t)cpuLineSegs_.size();
    {
        for (uint32_t si = segOffset; si < (uint32_t)segments_.size(); si++) {
            auto& s = segments_[si];
            if (s.tag == kSegTagLineTo) {
                if (std::abs(s.p0x - s.p1x) < 1e-6f && std::abs(s.p0y - s.p1y) < 1e-6f) continue;
                LineSeg ls = {};
                ls.p0x = s.p0x; ls.p0y = s.p0y;
                ls.p1x = s.p1x; ls.p1y = s.p1y;
                ls.pathIndex = s.pathIndex;
                cpuLineSegs_.push_back(ls);
            } else if (s.tag == kSegTagQuadTo) {
                float tol = 0.25f;
                float mx = 0.5f*(s.p0x+s.p2x), my = 0.5f*(s.p0y+s.p2y);
                float ddx = s.p1x - mx, ddy = s.p1y - my;
                float dev = std::sqrt(ddx*ddx+ddy*ddy);
                int n = std::max(1, (int)std::ceil(std::sqrt(dev/(2.0f*tol))));
                n = std::min(n, 64);
                float px = s.p0x, py = s.p0y;
                for (int i = 1; i <= n; i++) {
                    float t = (float)i / (float)n;
                    float u = 1-t;
                    float nx = u*u*s.p0x + 2*u*t*s.p1x + t*t*s.p2x;
                    float ny = u*u*s.p0y + 2*u*t*s.p1y + t*t*s.p2y;
                    LineSeg ls = {}; ls.p0x=px; ls.p0y=py; ls.p1x=nx; ls.p1y=ny; ls.pathIndex=s.pathIndex;
                    cpuLineSegs_.push_back(ls);
                    px=nx; py=ny;
                }
            } else if (s.tag == kSegTagCubicTo) {
                float tol = 0.25f;
                float tolSq = tol * tol;
                struct CubicSplit { float t0, t1; };
                CubicSplit stack[16];
                int top = 0;
                stack[top++] = {0.0f, 1.0f};
                float px = s.p0x, py = s.p0y;

                auto evalCubic = [&](float t) -> std::pair<float, float> {
                    float u = 1-t;
                    return {u*u*u*s.p0x + 3*u*u*t*s.p1x + 3*u*t*t*s.p2x + t*t*t*s.p3x,
                            u*u*u*s.p0y + 3*u*u*t*s.p1y + 3*u*t*t*s.p2y + t*t*t*s.p3y};
                };

                while (top > 0) {
                    auto [t0, t1] = stack[--top];
                    float tmid = 0.5f * (t0 + t1);
                    auto [x0, y0] = evalCubic(t0);
                    auto [x1, y1] = evalCubic(t1);
                    auto [xm, ym] = evalCubic(tmid);
                    float mx = 0.5f*(x0+x1), my = 0.5f*(y0+y1);
                    float devX = xm - mx, devY = ym - my;
                    if (devX*devX + devY*devY > tolSq && top < 15 && (t1 - t0) > 1e-5f) {
                        stack[top++] = {tmid, t1};
                        stack[top++] = {t0, tmid};
                    } else {
                        LineSeg ls = {}; ls.p0x=px; ls.p0y=py; ls.p1x=x1; ls.p1y=y1; ls.pathIndex=s.pathIndex;
                        cpuLineSegs_.push_back(ls);
                        px = x1; py = y1;
                    }
                }
            }
        }

        // Recompute bbox from flattened line segments (CPU path only)
        float lbMinX = 1e30f, lbMinY = 1e30f, lbMaxX = -1e30f, lbMaxY = -1e30f;
        for (uint32_t li = lineSegStart; li < (uint32_t)cpuLineSegs_.size(); li++) {
            auto& ls = cpuLineSegs_[li];
            lbMinX = std::min({lbMinX, ls.p0x, ls.p1x});
            lbMinY = std::min({lbMinY, ls.p0y, ls.p1y});
            lbMaxX = std::max({lbMaxX, ls.p0x, ls.p1x});
            lbMaxY = std::max({lbMaxY, ls.p0y, ls.p1y});
        }
        if (lbMinX < 1e29f) {
            currentBboxMinX_ = lbMinX; currentBboxMinY_ = lbMinY;
            currentBboxMaxX_ = lbMaxX; currentBboxMaxY_ = lbMaxY;
        }
    }

    // Reject paths that span too many tiles
    float bboxW = currentBboxMaxX_ - currentBboxMinX_;
    float bboxH = currentBboxMaxY_ - currentBboxMinY_;
    if (bboxW > 16384.0f || bboxH > 16384.0f) {
        segments_.resize(segOffset);
        cpuLineSegs_.resize(lineSegStart);
        return false;
    }

    // Clamp path bbox to scissor rect if set (prevents rendering outside clip region)
    if (hasScissor_) {
        currentBboxMinX_ = std::max(currentBboxMinX_, scissorLeft_);
        currentBboxMinY_ = std::max(currentBboxMinY_, scissorTop_);
        currentBboxMaxX_ = std::min(currentBboxMaxX_, scissorRight_);
        currentBboxMaxY_ = std::min(currentBboxMaxY_, scissorBottom_);
        if (currentBboxMaxX_ <= currentBboxMinX_ || currentBboxMaxY_ <= currentBboxMinY_) {
            // Path entirely outside scissor — skip
            segments_.resize(segOffset);
            cpuLineSegs_.resize(lineSegStart);
            return false;
        }
    }

    // Compute per-path tile bbox
    uint32_t tileBboxX = (uint32_t)std::max(0.0f, std::floor(currentBboxMinX_)) / kTileWidth;
    uint32_t tileBboxY = (uint32_t)std::max(0.0f, std::floor(currentBboxMinY_)) / kTileHeight;
    uint32_t tileBboxR = std::min((uint32_t)std::ceil(currentBboxMaxX_) / kTileWidth + 1, tilesX_);
    uint32_t tileBboxB = std::min((uint32_t)std::ceil(currentBboxMaxY_) / kTileHeight + 1, tilesY_);
    uint32_t tileBboxW = tileBboxR > tileBboxX ? tileBboxR - tileBboxX : 0;
    uint32_t tileBboxH = tileBboxB > tileBboxY ? tileBboxB - tileBboxY : 0;
    uint32_t tileCount = tileBboxW * tileBboxH;
    uint32_t tileOffset = totalPathTiles_;
    totalPathTiles_ += tileCount;

    PathInfo info = {};
    info.segOffset = segOffset;
    info.segCount = segCount;
    info.fillRule = fillRule;
    info.tileOffset = tileOffset;
    info.tileBboxX = tileBboxX;
    info.tileBboxY = tileBboxY;
    info.tileBboxW = tileBboxW;
    info.tileBboxH = tileBboxH;
    pathInfos_.push_back(info);

    // PathDraw will be set by caller
    outPathIdx = pathIdx;
    return true;
}

void D3D12VelloRenderer::OptimizeClipIntersection()
{
    // Vello-inspired clip path intersection optimization:
    // When multiple BeginClip/EndClip pairs are nested, merge consecutive
    // clip paths whose bounding boxes overlap into a single combined clip.
    // This reduces clip stack depth in the fine shader.

    if (clipEvents_.size() < 4) return;  // need at least 2 nested clips

    // Scan for consecutive BeginClip events (nested clips)
    // and merge overlapping clip paths by combining their geometry
    for (size_t i = 0; i + 1 < clipEvents_.size(); i++) {
        if (clipEvents_[i].type != ClipEvent::kBeginClip) continue;
        if (clipEvents_[i + 1].type != ClipEvent::kBeginClip) continue;

        // Two consecutive BeginClip events → nested clips
        uint32_t outerClipPath = clipEvents_[i].pathIdx;
        uint32_t innerClipPath = clipEvents_[i + 1].pathIdx;

        // Check bounding box overlap
        if (outerClipPath >= pathInfos_.size() || innerClipPath >= pathInfos_.size()) continue;
        auto& outerInfo = pathInfos_[outerClipPath];
        auto& innerInfo = pathInfos_[innerClipPath];

        // Compute intersection of tile bboxes
        uint32_t isectMinX = std::max(outerInfo.tileBboxX, innerInfo.tileBboxX);
        uint32_t isectMinY = std::max(outerInfo.tileBboxY, innerInfo.tileBboxY);
        uint32_t isectMaxX = std::min(outerInfo.tileBboxX + outerInfo.tileBboxW,
                                       innerInfo.tileBboxX + innerInfo.tileBboxW);
        uint32_t isectMaxY = std::min(outerInfo.tileBboxY + outerInfo.tileBboxH,
                                       innerInfo.tileBboxY + innerInfo.tileBboxH);

        if (isectMaxX <= isectMinX || isectMaxY <= isectMinY) continue;

        // Tighten inner clip's tile bbox to the intersection
        innerInfo.tileBboxX = isectMinX;
        innerInfo.tileBboxY = isectMinY;
        innerInfo.tileBboxW = isectMaxX - isectMinX;
        innerInfo.tileBboxH = isectMaxY - isectMinY;
    }
}

void D3D12VelloRenderer::BuildPtcl()
{
    // Apply clip intersection optimization before building PTCL
    OptimizeClipIntersection();

    uint32_t numPaths = (uint32_t)pathInfos_.size();
    uint32_t numGlobalTiles = tilesX_ * tilesY_;
    uint32_t numLineSegs = (uint32_t)cpuLineSegs_.size();

    // ── Per-segment tile binning + backdrop prefix-sum ──
    // For each path, bin its segments to tiles they actually cross,
    // and compute backdrop (winding number at tile left edge) via prefix-sum.

    // Step 1: Build per-path line-segment ranges
    struct PathLineRange { uint32_t start; uint32_t count; };
    std::vector<PathLineRange> pathLineRanges(numPaths);
    {
        std::vector<uint32_t> counts(numPaths, 0);
        for (auto& seg : cpuLineSegs_)
            if (seg.pathIndex < numPaths) counts[seg.pathIndex]++;
        uint32_t off = 0;
        for (uint32_t p = 0; p < numPaths; p++) {
            pathLineRanges[p].start = off;
            pathLineRanges[p].count = counts[p];
            off += counts[p];
        }
    }
    // Initialize sorted segments from line segments (y_edge will be computed per-tile in PTCL build)
    cpuSortedSegs_.resize(cpuLineSegs_.size());
    for (size_t si = 0; si < cpuLineSegs_.size(); si++) {
        auto& ls = cpuLineSegs_[si];
        cpuSortedSegs_[si] = { ls.p0x, ls.p0y, ls.p1x, ls.p1y, 1e9f };
    }
    cpuPathTiles_.clear();

    // Step 2: Per-path per-tile binning + backdrop
    // For each path, determine which tiles each segment touches,
    // and compute backdrop deltas at each tile's left edge.

    struct TileSegInfo {
        std::vector<uint32_t> segIndices;  // indices into cpuSortedSegs_
        int32_t backdropDelta = 0;         // winding delta at left edge
    };

    // Per-path per-tile data: pathTileMap[path][globalTileIdx] -> TileSegInfo
    // Use flat vector indexed by path * numGlobalTiles + tileIdx
    // Optimized: only iterate tiles within path bbox, use compact representation

    cpuPtcl_.clear();
    cpuPtclOffsets_.resize(numGlobalTiles);

    // For each global tile, collect per-path (segments, backdrop)
    // Build a compact representation per tile
    struct TilePathEntry {
        uint32_t pathIdx;
        uint32_t segStart;   // start in a packed segment index buffer
        uint32_t segCount;   // segments that actually cross this tile
        int32_t  backdrop;   // accumulated winding at left edge
    };

    // Packed per-tile segment indices
    std::vector<uint32_t> packedSegIndices;
    // Per-tile: list of path entries
    std::vector<std::vector<TilePathEntry>> tilePaths(numGlobalTiles);

    for (uint32_t p = 0; p < numPaths; p++) {
        auto& pi = pathInfos_[p];
        auto& lr = pathLineRanges[p];
        if (lr.count == 0) continue;

        uint32_t tbx = pi.tileBboxX, tby = pi.tileBboxY;
        uint32_t tbw = pi.tileBboxW, tbh = pi.tileBboxH;
        if (tbw == 0 || tbh == 0) continue;

        // Per-tile segment lists and backdrop deltas for this path
        uint32_t numPathTiles = tbw * tbh;
        std::vector<std::vector<uint32_t>> tileSegs(numPathTiles);
        std::vector<int32_t> tileBackdropDelta(numPathTiles, 0);

        // Use DDA binning + backdrop. DDA assigns segments to tiles they
        // actually cross, and backdrop provides the winding number baseline.
        {
        // Ported from Vello path_count.rs: DDA-based segment binning + backdrop.
        // This is a direct translation of the reference Rust CPU shader.
        constexpr float TILE_SCALE = 1.0f / (float)kTileWidth;
        constexpr float ONE_MINUS_ULP_F = 0.99999994f;
        constexpr float ROBUST_EPS = 2e-7f;

        int32_t bbox_i[4] = { (int32_t)tbx, (int32_t)tby,
                               (int32_t)(tbx + tbw), (int32_t)(tby + tbh) };
        int32_t stride = bbox_i[2] - bbox_i[0];

        for (uint32_t si = lr.start; si < lr.start + lr.count; si++) {
            auto& seg = cpuLineSegs_[si];
            float p0x = seg.p0x, p0y = seg.p0y, p1x = seg.p1x, p1y = seg.p1y;

            bool is_down = p1y >= p0y;
            float xy0x, xy0y, xy1x, xy1y;
            if (is_down) { xy0x=p0x; xy0y=p0y; xy1x=p1x; xy1y=p1y; }
            else          { xy0x=p1x; xy0y=p1y; xy1x=p0x; xy1y=p0y; }

            float s0x = xy0x * TILE_SCALE, s0y = xy0y * TILE_SCALE;
            float s1x = xy1x * TILE_SCALE, s1y = xy1y * TILE_SCALE;

            auto span_fn = [](float a, float b) -> uint32_t {
                return (uint32_t)std::max(std::ceil(std::max(a,b)) - std::floor(std::min(a,b)), 1.0f);
            };
            uint32_t count_x = span_fn(s0x, s1x) - 1;
            uint32_t count = count_x + span_fn(s0y, s1y);

            float dx = std::abs(s1x - s0x);
            float dy = s1y - s0y;
            if (dx + dy == 0.0f) continue;
            if (dy == 0.0f && std::floor(s0y) == s0y) continue;

            float idxdy = 1.0f / (dx + dy);
            float a = dx * idxdy;
            bool is_positive_slope = s1x >= s0x;
            float x_sign = is_positive_slope ? 1.0f : -1.0f;
            float xt0 = std::floor(s0x * x_sign);
            float c = s0x * x_sign - xt0;
            float y0 = std::floor(s0y);
            float ytop = (s0y == s1y) ? std::ceil(s0y) : (y0 + 1.0f);
            float b = std::min((dy * c + dx * (ytop - s0y)) * idxdy, ONE_MINUS_ULP_F);
            float robust_err = std::floor(a * ((float)count - 1.0f) + b) - (float)count_x;
            if (robust_err != 0.0f) {
                a -= std::copysign(ROBUST_EPS, robust_err);
            }
            float x0 = xt0 * x_sign + (is_positive_slope ? 0.0f : -1.0f);

            float xmin = std::min(s0x, s1x);
            if (s0y >= (float)bbox_i[3] || s1y <= (float)bbox_i[1] ||
                xmin >= (float)bbox_i[2] || stride == 0) continue;

            // Clip to bbox in "i" space
            uint32_t imin = 0;
            if (s0y < (float)bbox_i[1]) {
                float iminf = std::round(((float)bbox_i[1] - y0 + b - a) / (1.0f - a)) - 1.0f;
                if (y0 + iminf - std::floor(a * iminf + b) < (float)bbox_i[1]) iminf += 1.0f;
                imin = (uint32_t)iminf;
            }
            uint32_t imax = count;
            if (s1y > (float)bbox_i[3]) {
                float imaxf = std::round(((float)bbox_i[3] - y0 + b - a) / (1.0f - a)) - 1.0f;
                if (y0 + imaxf - std::floor(a * imaxf + b) < (float)bbox_i[3]) imaxf += 1.0f;
                imax = (uint32_t)imaxf;
            }

            int delta = is_down ? -1 : 1;
            int32_t ymin_bd = 0, ymax_bd = 0;

            if (std::max(s0x, s1x) <= (float)bbox_i[0]) {
                ymin_bd = (int32_t)std::ceil(s0y);
                ymax_bd = (int32_t)std::ceil(s1y);
                imax = imin;
            } else {
                float fudge = is_positive_slope ? 0.0f : 1.0f;
                if (xmin < (float)bbox_i[0]) {
                    float f = std::round((x_sign * ((float)bbox_i[0] - x0) - b + fudge) / a);
                    if ((x0 + x_sign * std::floor(a * f + b) < (float)bbox_i[0]) == is_positive_slope) f += 1.0f;
                    int32_t ynext = (int32_t)(y0 + f - std::floor(a * f + b) + 1.0f);
                    if (is_positive_slope) {
                        if ((uint32_t)f > imin) {
                            ymin_bd = (int32_t)(y0 + (y0 == s0y ? 0.0f : 1.0f));
                            ymax_bd = ynext;
                            imin = (uint32_t)f;
                        }
                    } else {
                        if ((uint32_t)f < imax) {
                            ymin_bd = ynext;
                            ymax_bd = (int32_t)std::ceil(s1y);
                            imax = (uint32_t)f;
                        }
                    }
                }
                if (std::max(s0x, s1x) > (float)bbox_i[2]) {
                    float f = std::round((x_sign * ((float)bbox_i[2] - x0) - b + fudge) / a);
                    if ((x0 + x_sign * std::floor(a * f + b) < (float)bbox_i[2]) == is_positive_slope) f += 1.0f;
                    if (is_positive_slope) imax = std::min(imax, (uint32_t)f);
                    else imin = std::max(imin, (uint32_t)f);
                }
            }
            imax = std::max(imin, imax);

            // Backdrop for segments left of bbox
            ymin_bd = std::max(ymin_bd, bbox_i[1]);
            ymax_bd = std::min(ymax_bd, bbox_i[3]);
            for (int32_t yy = ymin_bd; yy < ymax_bd; yy++) {
                uint32_t localIdx = (uint32_t)(yy - bbox_i[1]) * tbw;
                tileBackdropDelta[localIdx] += delta;
            }

            // DDA walk: bin segments and compute backdrop
            float last_z = std::floor(a * ((float)imin - 1.0f) + b);
            for (uint32_t i = imin; i < imax; i++) {
                float zf = a * (float)i + b;
                float z = std::floor(zf);
                int32_t y = (int32_t)(y0 + (float)i - z);
                int32_t x = (int32_t)(x0 + x_sign * z);

                // Bin this segment to tile (x, y) within path bbox
                if (x >= bbox_i[0] && x < bbox_i[2] && y >= bbox_i[1] && y < bbox_i[3]) {
                    uint32_t localIdx = (uint32_t)(y - bbox_i[1]) * tbw + (uint32_t)(x - bbox_i[0]);
                    tileSegs[localIdx].push_back(si);
                }

                // Top-edge backdrop: when entering a new column from above
                bool top_edge = (i == 0) ? (y0 == s0y) : (last_z == z);
                if (top_edge && x + 1 < bbox_i[2]) {
                    int32_t x_bump = std::max(x + 1, bbox_i[0]);
                    uint32_t localIdx = (uint32_t)(y - bbox_i[1]) * tbw + (uint32_t)(x_bump - bbox_i[0]);
                    if (localIdx < numPathTiles) {
                        tileBackdropDelta[localIdx] += delta;
                    }
                }
                last_z = z;
            }
        }

        // Inclusive prefix-sum of backdrop deltas left-to-right per row
        for (uint32_t lty = 0; lty < tbh; lty++) {
            int32_t accum = 0;
            for (uint32_t ltx = 0; ltx < tbw; ltx++) {
                uint32_t localIdx = lty * tbw + ltx;
                accum += tileBackdropDelta[localIdx];
                tileBackdropDelta[localIdx] = accum;
            }
        }
        } // end else (DDA path for large segments)

        // Store per-tile entries
        for (uint32_t lty = 0; lty < tbh; lty++) {
            for (uint32_t ltx = 0; ltx < tbw; ltx++) {
                uint32_t localIdx = lty * tbw + ltx;
                uint32_t gx = tbx + ltx, gy = tby + lty;
                uint32_t globalTileIdx = gy * tilesX_ + gx;

                auto& segs = tileSegs[localIdx];
                int32_t backdrop = tileBackdropDelta[localIdx];

                // Skip tiles with no segments and zero backdrop (completely outside)
                if (segs.empty() && backdrop == 0) continue;

                TilePathEntry entry;
                entry.pathIdx = p;
                entry.segStart = (uint32_t)packedSegIndices.size();
                entry.segCount = (uint32_t)segs.size();
                entry.backdrop = backdrop;

                for (auto segIdx : segs) packedSegIndices.push_back(segIdx);

                tilePaths[globalTileIdx].push_back(entry);
            }
        }
    }

    // Step 3: Build PTCL from per-tile path entries
    // Use clipEvents_ to emit commands in correct draw order (with clip begin/end)
    cpuSortedSegs_.clear();

    auto pushFloatBits = [&](float v) {
        uint32_t bits;
        memcpy(&bits, &v, sizeof(bits));
        cpuPtcl_.push_back(bits);
    };

    // Helper: emit brush command for a path draw.
    // Each command's payload size must exactly match what the fine shader reads
    // (see kVelloFineCS in d3d12_shader_source.h).
    auto emitBrush = [&](const PathDraw& draw) {
        if (draw.brushType == kBrushLinearGradient) {
            // Shader reads: gradIndex, p0x, p0y, p1x, p1y, extF, opacity, dummy (8 u32)
            cpuPtcl_.push_back(kPtclLinGrad);
            cpuPtcl_.push_back(draw.gradientIndex);
            pushFloatBits(draw.gradParam0);  // p0x
            pushFloatBits(draw.gradParam1);  // p0y
            pushFloatBits(draw.gradParam2);  // p1x
            pushFloatBits(draw.gradParam3);  // p1y
            cpuPtcl_.push_back((uint32_t)draw.gradParam4);  // extendMode
            pushFloatBits(draw.colorA);  // opacity
            cpuPtcl_.push_back(0);  // pad
        } else if (draw.brushType == kBrushRadialGradient) {
            // Shader reads: gradIndex, cx, cy, rx, ry, ox, oy, extF, opacity (9 u32)
            cpuPtcl_.push_back(kPtclRadGrad);
            cpuPtcl_.push_back(draw.gradientIndex);
            pushFloatBits(draw.gradParam0);  // cx
            pushFloatBits(draw.gradParam1);  // cy
            pushFloatBits(draw.gradParam2);  // rx
            pushFloatBits(draw.gradParam3);  // ry
            pushFloatBits(draw.gradParam4);  // ox
            pushFloatBits(draw.gradParam5);  // oy
            // Extract extend mode from colorR (float-encoded uint32)
            uint32_t radExtMode; memcpy(&radExtMode, &draw.colorR, sizeof(uint32_t));
            cpuPtcl_.push_back(radExtMode);
            pushFloatBits(draw.colorA);  // opacity
        } else if (draw.brushType == kBrushSweepGradient) {
            // Shader reads: gradIndex, scx, scy, st0, st1, extF, opacity, dummy (8 u32)
            cpuPtcl_.push_back(kPtclSweepGrad);
            cpuPtcl_.push_back(draw.gradientIndex);
            pushFloatBits(draw.gradParam0);  // center.x
            pushFloatBits(draw.gradParam1);  // center.y
            pushFloatBits(draw.gradParam2);  // t0 (start angle)
            pushFloatBits(draw.gradParam3);  // t1 (end angle)
            cpuPtcl_.push_back(kExtendPad);  // extendMode
            pushFloatBits(draw.colorA);  // opacity
            cpuPtcl_.push_back(0);  // pad
        } else if (draw.brushType == kBrushImage) {
            // Shader reads: atlasIdx, u0, v0, u1, v1, opacity (6 u32)
            cpuPtcl_.push_back(kPtclImage);
            cpuPtcl_.push_back(draw.gradientIndex);  // image/atlas index
            pushFloatBits(draw.gradParam0);  // u0 / origin x
            pushFloatBits(draw.gradParam1);  // v0 / origin y
            pushFloatBits(draw.gradParam2);  // u1 / u scale
            pushFloatBits(draw.gradParam3);  // v1 / v scale
            pushFloatBits(draw.colorA);      // opacity
        } else if (draw.brushType == 0xFF) {
            // Blur rect: shader CMD_BLUR_RECT reads 2 u32: info_off, packed_rgba
            // The blur rect coverage is computed analytically in the shader using
            // area[] from a preceding CMD_FILL/CMD_SOLID.
            cpuPtcl_.push_back(kPtclBlurRect);
            cpuPtcl_.push_back(0);  // info_off (unused in current CPU pipeline)
            uint32_t r8 = (uint32_t)(std::min(draw.colorR, 1.0f) * 255.0f + 0.5f);
            uint32_t g8 = (uint32_t)(std::min(draw.colorG, 1.0f) * 255.0f + 0.5f);
            uint32_t b8 = (uint32_t)(std::min(draw.colorB, 1.0f) * 255.0f + 0.5f);
            uint32_t a8 = (uint32_t)(std::min(draw.colorA, 1.0f) * 255.0f + 0.5f);
            cpuPtcl_.push_back(r8 | (g8 << 8) | (b8 << 16) | (a8 << 24));
        } else {
            uint32_t r8 = (uint32_t)(std::min(draw.colorR, 1.0f) * 255.0f + 0.5f);
            uint32_t g8 = (uint32_t)(std::min(draw.colorG, 1.0f) * 255.0f + 0.5f);
            uint32_t b8 = (uint32_t)(std::min(draw.colorB, 1.0f) * 255.0f + 0.5f);
            uint32_t a8 = (uint32_t)(std::min(draw.colorA, 1.0f) * 255.0f + 0.5f);
            cpuPtcl_.push_back(kPtclColor);
            cpuPtcl_.push_back(r8 | (g8 << 8) | (b8 << 16) | (a8 << 24));
        }
    };

    // Convert a LineSeg to VelloSortedSeg for the fine shader.
    // y_edge is set to 1e9 (disabled) because the CPU pipeline's DDA-based
    // BuildPtcl already computes correct per-tile backdrop winding via
    // prefix sum.  The GPU pipeline uses y_edge as a per-segment winding
    // correction, but combining it with DDA backdrop causes double-counting
    // that fills the entire bounding box as a solid block.
    auto makeVelloSeg = [](const LineSeg& ls) -> VelloSortedSeg {
        return { ls.p0x, ls.p0y, ls.p1x, ls.p1y, 1e9f };
    };

    // Helper: emit fill/solid + brush for a path at a specific tile
    auto emitPathForTile = [&](uint32_t pathIdx, uint32_t globalTileIdx) {
        // Find this path's entry in tilePaths for this tile
        auto& entries = tilePaths[globalTileIdx];
        for (auto& entry : entries) {
            if (entry.pathIdx != pathIdx) continue;

            auto& draw = pathDraws_[entry.pathIdx];
            auto& pi = pathInfos_[entry.pathIdx];

            if (entry.segCount > 0) {
                uint32_t segStartInSorted = (uint32_t)cpuSortedSegs_.size();
                for (uint32_t k = 0; k < entry.segCount; k++) {
                    cpuSortedSegs_.push_back(makeVelloSeg(cpuLineSegs_[packedSegIndices[entry.segStart + k]]));
                }
                cpuPtcl_.push_back(kPtclFill);
                // size_and_rule: segCount in upper bits, fill rule in bit 0
                // Shader: even_odd = (size_and_rule & 1) != 0; n_segs = size_and_rule >> 1
                uint32_t evenOdd = (pi.fillRule == kFillRuleEvenOdd) ? 1u : 0u;
                cpuPtcl_.push_back((entry.segCount << 1) | evenOdd);
                cpuPtcl_.push_back(segStartInSorted);
                cpuPtcl_.push_back((uint32_t)entry.backdrop);
            } else if (entry.backdrop != 0) {
                cpuPtcl_.push_back(kPtclSolid);
            } else {
                return;  // no contribution to this tile
            }

            emitBrush(draw);
            return;
        }
    };

    // Build PTCL per tile following clipEvents_ ordering
    bool hasClips = false;
    for (auto& evt : clipEvents_) {
        if (evt.type == ClipEvent::kBeginClip || evt.type == ClipEvent::kEndClip) {
            hasClips = true;
            break;
        }
    }

    for (uint32_t ty = 0; ty < tilesY_; ty++) {
        for (uint32_t tx = 0; tx < tilesX_; tx++) {
            uint32_t globalTileIdx = ty * tilesX_ + tx;
            cpuPtclOffsets_[globalTileIdx] = (uint32_t)cpuPtcl_.size();

            if (!hasClips) {
                // Fast path: no clips, emit all path entries directly (same as before)
                auto& entries = tilePaths[globalTileIdx];
                for (auto& entry : entries) {
                    auto& draw = pathDraws_[entry.pathIdx];
                    auto& pi = pathInfos_[entry.pathIdx];

                    if (entry.segCount > 0) {
                        uint32_t segStartInSorted = (uint32_t)cpuSortedSegs_.size();
                        for (uint32_t k = 0; k < entry.segCount; k++) {
                            cpuSortedSegs_.push_back(makeVelloSeg(cpuLineSegs_[packedSegIndices[entry.segStart + k]]));
                        }
                        cpuPtcl_.push_back(kPtclFill);
                        uint32_t evenOdd = (pi.fillRule == kFillRuleEvenOdd) ? 1u : 0u;
                        cpuPtcl_.push_back((entry.segCount << 1) | evenOdd);
                        cpuPtcl_.push_back(segStartInSorted);
                        cpuPtcl_.push_back((uint32_t)entry.backdrop);
                    } else if (entry.backdrop != 0) {
                        cpuPtcl_.push_back(kPtclSolid);
                    } else {
                        continue;
                    }
                    emitBrush(draw);
                }
            } else {
                // Clip-aware path: iterate events in order
                for (auto& evt : clipEvents_) {
                    if (evt.type == ClipEvent::kBeginClip) {
                        // Vello clip: SAVE_BACKDROP → clip_fill+brush → BEGIN_CLIP → content → END_CLIP
                        // SAVE_BACKDROP saves current pixel before clip path overwrites it.
                        cpuPtcl_.push_back(kPtclSaveBackdrop);
                        emitPathForTile(evt.pathIdx, globalTileIdx);
                        cpuPtcl_.push_back(kPtclBeginClip);
                    } else if (evt.type == ClipEvent::kEndClip) {
                        cpuPtcl_.push_back(kPtclEndClip);
                        cpuPtcl_.push_back(evt.blendMode);
                        pushFloatBits(evt.alpha);
                    } else {
                        // kDraw: emit path content
                        emitPathForTile(evt.pathIdx, globalTileIdx);
                    }
                }
            }

            cpuPtcl_.push_back(kPtclEnd);
        }
    }
}

bool D3D12VelloRenderer::CachePath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    uint32_t fillRule, VelloCachedPath& outCache)
{
    outCache.Clear();

    // Temporarily use EncodePathGeometry to flatten, then extract the line segments
    uint32_t pathIdx;
    uint32_t lineSegBefore = (uint32_t)cpuLineSegs_.size();
    uint32_t pathsBefore = (uint32_t)pathInfos_.size();
    uint32_t segsBefore = (uint32_t)segments_.size();

    if (!EncodePathGeometry(startX, startY, commands, commandLength,
                            fillRule, 1, 0, 0, 1, 0, 0, pathIdx))
        return false;

    // Extract flattened line segments
    for (uint32_t i = lineSegBefore; i < (uint32_t)cpuLineSegs_.size(); i++) {
        outCache.lineSegs.push_back(cpuLineSegs_[i]);
    }
    outCache.bboxMinX = currentBboxMinX_;
    outCache.bboxMinY = currentBboxMinY_;
    outCache.bboxMaxX = currentBboxMaxX_;
    outCache.bboxMaxY = currentBboxMaxY_;
    outCache.fillRule = fillRule;
    outCache.valid = true;

    // Roll back — we only wanted to cache, not actually encode
    cpuLineSegs_.resize(lineSegBefore);
    pathInfos_.resize(pathsBefore);
    segments_.resize(segsBefore);

    return true;
}

bool D3D12VelloRenderer::EncodeFillPathCached(
    const VelloCachedPath& cached,
    float r, float g, float b, float a,
    float m11, float m12, float m21, float m22, float tdx, float tdy)
{
    if (!cached.valid || cached.lineSegs.empty()) return false;

    uint32_t pathIdx = (uint32_t)pathInfos_.size();
    uint32_t lineSegStart = (uint32_t)cpuLineSegs_.size();

    // Apply transform to cached line segments
    bool hasXf = (m11!=1||m12!=0||m21!=0||m22!=1||tdx!=0||tdy!=0);
    float bMinX = 1e30f, bMinY = 1e30f, bMaxX = -1e30f, bMaxY = -1e30f;

    for (auto& src : cached.lineSegs) {
        LineSeg ls = src;
        if (hasXf) {
            float nx0 = ls.p0x*m11+ls.p0y*m21+tdx, ny0 = ls.p0x*m12+ls.p0y*m22+tdy;
            float nx1 = ls.p1x*m11+ls.p1y*m21+tdx, ny1 = ls.p1x*m12+ls.p1y*m22+tdy;
            ls.p0x=nx0; ls.p0y=ny0; ls.p1x=nx1; ls.p1y=ny1;
        }
        ls.pathIndex = pathIdx;
        bMinX = std::min({bMinX, ls.p0x, ls.p1x});
        bMinY = std::min({bMinY, ls.p0y, ls.p1y});
        bMaxX = std::max({bMaxX, ls.p0x, ls.p1x});
        bMaxY = std::max({bMaxY, ls.p0y, ls.p1y});
        cpuLineSegs_.push_back(ls);
    }

    // Compute tile bbox
    uint32_t tileBboxX = (uint32_t)std::max(0.0f, std::floor(bMinX)) / kTileWidth;
    uint32_t tileBboxY = (uint32_t)std::max(0.0f, std::floor(bMinY)) / kTileHeight;
    uint32_t tileBboxR = std::min((uint32_t)std::ceil(bMaxX) / kTileWidth + 1, tilesX_);
    uint32_t tileBboxB = std::min((uint32_t)std::ceil(bMaxY) / kTileHeight + 1, tilesY_);
    uint32_t tileBboxW = tileBboxR > tileBboxX ? tileBboxR - tileBboxX : 0;
    uint32_t tileBboxH = tileBboxB > tileBboxY ? tileBboxB - tileBboxY : 0;

    PathInfo info = {};
    info.segOffset = 0; info.segCount = 0;
    info.fillRule = cached.fillRule;
    info.tileOffset = totalPathTiles_;
    info.tileBboxX = tileBboxX; info.tileBboxY = tileBboxY;
    info.tileBboxW = tileBboxW; info.tileBboxH = tileBboxH;
    totalPathTiles_ += tileBboxW * tileBboxH;
    pathInfos_.push_back(info);

    PathDraw draw = {};
    draw.colorR = r*a; draw.colorG = g*a; draw.colorB = b*a; draw.colorA = a;
    draw.bboxMinX = bMinX; draw.bboxMinY = bMinY;
    draw.bboxMaxX = bMaxX; draw.bboxMaxY = bMaxY;
    draw.brushType = kBrushSolid;
    pathDraws_.push_back(draw);

    clipEvents_.push_back({ClipEvent::kDraw, pathIdx, 0, 0});
    drawTags_.push_back({kDrawTagFill, pathIdx, 0, 0});
    return true;
}

bool D3D12VelloRenderer::EncodeFillPath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    float r, float g, float b, float a,
    uint32_t fillRule,
    float m11, float m12, float m21, float m22, float tdx, float tdy)
{
    uint32_t pathIdx;
    if (!EncodePathGeometry(startX, startY, commands, commandLength,
                            fillRule, m11, m12, m21, m22, tdx, tdy, pathIdx))
        return false;

    PathDraw draw = {};
    draw.colorR = r * a;  // premultiply
    draw.colorG = g * a;
    draw.colorB = b * a;
    draw.colorA = a;
    draw.bboxMinX = currentBboxMinX_;
    draw.bboxMinY = currentBboxMinY_;
    draw.bboxMaxX = currentBboxMaxX_;
    draw.bboxMaxY = currentBboxMaxY_;
    draw.brushType = kBrushSolid;
    draw.gradientIndex = 0;
    draw.gradParam0 = draw.gradParam1 = draw.gradParam2 = draw.gradParam3 = 0;
    draw.gradParam4 = draw.gradParam5 = 0;
    pathDraws_.push_back(draw);

    // Record draw event for clip-aware PTCL ordering
    clipEvents_.push_back({ClipEvent::kDraw, pathIdx, 0, 0});
    drawTags_.push_back({kDrawTagFill, pathIdx, 0, 0});

    return true;
}

bool D3D12VelloRenderer::EncodeFillPathBrush(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    Brush* brush, uint32_t fillRule, float opacity,
    float m11, float m12, float m21, float m22, float tdx, float tdy)
{
    if (!brush) return false;

    uint32_t pathIdx;
    if (!EncodePathGeometry(startX, startY, commands, commandLength,
                            fillRule, m11, m12, m21, m22, tdx, tdy, pathIdx))
        return false;

    PathDraw draw = {};
    draw.bboxMinX = currentBboxMinX_;
    draw.bboxMinY = currentBboxMinY_;
    draw.bboxMaxX = currentBboxMaxX_;
    draw.bboxMaxY = currentBboxMaxY_;

    auto brushType = brush->GetType();
    if (brushType == JALIUM_BRUSH_SOLID) {
        auto* sb = static_cast<D3D12SolidBrush*>(brush);
        float a = sb->a_ * opacity;
        draw.colorR = sb->r_ * a;
        draw.colorG = sb->g_ * a;
        draw.colorB = sb->b_ * a;
        draw.colorA = a;
        draw.brushType = kBrushSolid;
    } else if (brushType == JALIUM_BRUSH_LINEAR_GRADIENT) {
        auto* lg = static_cast<D3D12LinearGradientBrush*>(brush);
        uint32_t gradIdx = AddGradientRamp(lg->stops_.data(), (uint32_t)lg->stops_.size());
        draw.brushType = kBrushLinearGradient;
        draw.gradientIndex = gradIdx;
        // Transform gradient endpoints by the same affine transform as the path
        float gp0x = lg->startX_ * m11 + lg->startY_ * m21 + tdx;
        float gp0y = lg->startX_ * m12 + lg->startY_ * m22 + tdy;
        float gp1x = lg->endX_ * m11 + lg->endY_ * m21 + tdx;
        float gp1y = lg->endX_ * m12 + lg->endY_ * m22 + tdy;
        draw.gradParam0 = gp0x;
        draw.gradParam1 = gp0y;
        draw.gradParam2 = gp1x;
        draw.gradParam3 = gp1y;
        draw.gradParam4 = (float)kExtendPad;  // extend mode
        // Apply opacity: store as colorA for the fine shader to modulate
        draw.colorA = opacity;
        draw.colorR = draw.colorG = draw.colorB = 0;
    } else if (brushType == JALIUM_BRUSH_RADIAL_GRADIENT) {
        auto* rg = static_cast<D3D12RadialGradientBrush*>(brush);
        uint32_t gradIdx = AddGradientRamp(rg->stops_.data(), (uint32_t)rg->stops_.size());
        draw.brushType = kBrushRadialGradient;
        draw.gradientIndex = gradIdx;
        // Transform center and origin
        float cx = rg->centerX_ * m11 + rg->centerY_ * m21 + tdx;
        float cy = rg->centerX_ * m12 + rg->centerY_ * m22 + tdy;
        float ox = rg->originX_ * m11 + rg->originY_ * m21 + tdx;
        float oy = rg->originX_ * m12 + rg->originY_ * m22 + tdy;
        // Transform radii (approximate: use scale of transform axes)
        float scaleX = std::sqrt(m11*m11 + m12*m12);
        float scaleY = std::sqrt(m21*m21 + m22*m22);
        draw.gradParam0 = cx;
        draw.gradParam1 = cy;
        draw.gradParam2 = rg->radiusX_ * scaleX;
        draw.gradParam3 = rg->radiusY_ * scaleY;
        draw.gradParam4 = ox;
        draw.gradParam5 = oy;
        draw.colorA = opacity;
        // Pack extend mode into colorR (unused for gradient brushes)
        float extFloat; uint32_t extU = kExtendPad;
        memcpy(&extFloat, &extU, sizeof(float));
        draw.colorR = extFloat;
        draw.colorG = draw.colorB = 0;
    } else {
        // Unsupported brush type — roll back geometry
        pathInfos_.pop_back();
        return false;
    }

    pathDraws_.push_back(draw);
    clipEvents_.push_back({ClipEvent::kDraw, pathIdx, 0, 0});
    drawTags_.push_back({kDrawTagFill, pathIdx, 0, 0});
    return true;
}

bool D3D12VelloRenderer::EncodeStrokePath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    float r, float g, float b, float a,
    float strokeWidth, bool closed,
    int32_t lineJoin, float miterLimit,
    int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    float m11, float m12, float m21, float m22, float tdx, float tdy)
{
    // Flatten path to polyline
    std::vector<float> pts = FlattenPathCommands(startX, startY, commands, commandLength, 0.25f);

    // Check closed flag from commands
    for (uint32_t ci = 0; ci < commandLength; ) {
        int tag = (int)commands[ci];
        if (tag == kTagClosePath) { closed = true; break; }
        else if (tag == kTagLineTo) ci += 3;
        else if (tag == kTagCubicTo) ci += 7;
        else if (tag == kTagMoveTo) ci += 3;
        else if (tag == kTagQuadTo) ci += 5;
        else if (tag == kTagArcTo) ci += 8;
        else ci += 1;
    }

    uint32_t pointCount = (uint32_t)(pts.size() / 2);
    if (pointCount < 2) return (uint32_t)pathInfos_.size();

    // Dash pattern expansion: split polyline into dashed sub-segments
    if (dashPattern && dashCount > 0) {
        // Compute total length of polyline
        std::vector<float> segLens(pointCount - 1);
        float totalLen = 0;
        for (uint32_t i = 0; i < pointCount - 1; i++) {
            float ddx = pts[(i+1)*2] - pts[i*2];
            float ddy = pts[(i+1)*2+1] - pts[i*2+1];
            segLens[i] = std::sqrt(ddx*ddx + ddy*ddy);
            totalLen += segLens[i];
        }
        // Walk polyline, emitting dashed sub-strokes
        float dashPhase = dashOffset;
        // Normalize phase into pattern
        float patternLen = 0;
        for (uint32_t d = 0; d < dashCount; d++) patternLen += std::max(dashPattern[d], 0.001f);
        if (patternLen > 0.001f) {
            while (dashPhase < 0) dashPhase += patternLen;
            while (dashPhase >= patternLen) dashPhase -= patternLen;
        }
        uint32_t dashIdx = 0;
        float dashRemain = dashPattern[0];
        // Skip ahead by dashPhase
        float phase = dashPhase;
        while (phase > 0 && dashIdx < dashCount) {
            if (phase < dashRemain) { dashRemain -= phase; break; }
            phase -= dashRemain;
            dashIdx = (dashIdx + 1) % dashCount;
            dashRemain = dashPattern[dashIdx];
        }
        bool isDraw = (dashIdx % 2 == 0);

        std::vector<float> subPts;
        float dist = 0;
        uint32_t segI = 0;
        float segUsed = 0;
        auto interpPoint = [&](uint32_t si, float t) -> std::pair<float,float> {
            float x0 = pts[si*2], y0 = pts[si*2+1];
            float x1 = pts[(si+1)*2], y1 = pts[(si+1)*2+1];
            return {x0 + t*(x1-x0), y0 + t*(y1-y0)};
        };

        bool anyEmitted = false;
        subPts.push_back(pts[0]); subPts.push_back(pts[1]);

        while (segI < pointCount - 1) {
            float segLeft = segLens[segI] - segUsed;
            if (dashRemain <= segLeft) {
                // Dash boundary within this segment
                float t = (segUsed + dashRemain) / segLens[segI];
                auto [px, py] = interpPoint(segI, t);
                if (isDraw) {
                    subPts.push_back(px); subPts.push_back(py);
                    // Emit this sub-stroke recursively (without dash)
                    if (subPts.size() >= 4) {
                        // Build commands for sub-stroke
                        std::vector<float> subCmds;
                        uint32_t subCount = (uint32_t)(subPts.size() / 2);
                        for (uint32_t k = 1; k < subCount; k++) {
                            subCmds.push_back((float)kTagLineTo);
                            subCmds.push_back(subPts[k*2]);
                            subCmds.push_back(subPts[k*2+1]);
                        }
                        EncodeStrokePath(subPts[0], subPts[1],
                            subCmds.data(), (uint32_t)subCmds.size(),
                            r, g, b, a, strokeWidth, false,
                            lineJoin, miterLimit, lineCap,
                            nullptr, 0, 0,
                            m11, m12, m21, m22, tdx, tdy);
                        anyEmitted = true;
                    }
                }
                subPts.clear();
                subPts.push_back(px); subPts.push_back(py);
                segUsed += dashRemain;
                dashIdx = (dashIdx + 1) % dashCount;
                dashRemain = dashPattern[dashIdx];
                isDraw = !isDraw;
            } else {
                // Move to next segment
                dashRemain -= segLeft;
                segI++;
                segUsed = 0;
                if (segI < pointCount - 1 && isDraw) {
                    subPts.push_back(pts[(segI)*2]);
                    subPts.push_back(pts[(segI)*2+1]);
                } else if (segI < pointCount - 1) {
                    subPts.clear();
                    subPts.push_back(pts[(segI)*2]);
                    subPts.push_back(pts[(segI)*2+1]);
                }
            }
        }
        // Emit remaining sub-stroke
        if (isDraw && subPts.size() >= 4) {
            if (segI < pointCount) {
                subPts.push_back(pts[(pointCount-1)*2]);
                subPts.push_back(pts[(pointCount-1)*2+1]);
            }
            std::vector<float> subCmds;
            uint32_t subCount = (uint32_t)(subPts.size() / 2);
            for (uint32_t k = 1; k < subCount; k++) {
                subCmds.push_back((float)kTagLineTo);
                subCmds.push_back(subPts[k*2]);
                subCmds.push_back(subPts[k*2+1]);
            }
            EncodeStrokePath(subPts[0], subPts[1],
                subCmds.data(), (uint32_t)subCmds.size(),
                r, g, b, a, strokeWidth, false,
                lineJoin, miterLimit, lineCap,
                nullptr, 0, 0,
                m11, m12, m21, m22, tdx, tdy);
            anyEmitted = true;
        }
        return anyEmitted;
    }

    // Simplify
    {
        const float kMinDistSq = 0.01f;
        std::vector<float> cleaned;
        cleaned.reserve(pts.size());
        cleaned.push_back(pts[0]);
        cleaned.push_back(pts[1]);
        for (uint32_t j = 1; j < pointCount; j++) {
            float dx = pts[j * 2] - cleaned[cleaned.size() - 2];
            float dy = pts[j * 2 + 1] - cleaned[cleaned.size() - 1];
            if (dx * dx + dy * dy >= kMinDistSq) {
                cleaned.push_back(pts[j * 2]);
                cleaned.push_back(pts[j * 2 + 1]);
            }
        }
        pts = std::move(cleaned);
        pointCount = (uint32_t)(pts.size() / 2);
    }

    if (pointCount < 2) return (uint32_t)pathInfos_.size();

    // Generate stroke geometry as per-segment closed quads + join/cap sub-contours.
    // Each quad is a separate CW-wound closed sub-contour within a single Vello
    // path.  Under NonZero fill, overlapping quads always have winding >= 1 and
    // are correctly filled — this avoids the self-intersection artifacts that
    // occur when the entire stroke outline is a single contour.
    float hw = strokeWidth * 0.5f;
    uint32_t segCount = closed ? pointCount : pointCount - 1;

    // Compute segment info
    struct SegInfo { float dx, dy, nx, ny; };
    std::vector<SegInfo> segs(segCount);
    for (uint32_t i = 0; i < segCount; i++) {
        uint32_t j = (i + 1) % pointCount;
        float dx = pts[j * 2] - pts[i * 2];
        float dy = pts[j * 2 + 1] - pts[i * 2 + 1];
        float len = std::sqrt(dx * dx + dy * dy);
        if (len < 1e-6f) len = 1e-6f;
        segs[i] = { dx / len, dy / len, -dy / len * hw, dx / len * hw };
    }

    uint32_t pathIdx = (uint32_t)pathInfos_.size();
    uint32_t segOffset = (uint32_t)segments_.size();

    currentBboxMinX_ = 1e30f;
    currentBboxMinY_ = 1e30f;
    currentBboxMaxX_ = -1e30f;
    currentBboxMaxY_ = -1e30f;

    // Helper: emit a single line segment and update bbox
    auto emitLine = [&](float x0, float y0, float x1, float y1) {
        if (std::abs(x1 - x0) < 1e-6f && std::abs(y1 - y0) < 1e-6f) return;
        PathSegment s = {};
        s.p0x = x0; s.p0y = y0; s.p1x = x1; s.p1y = y1;
        s.p2x = x1; s.p2y = y1; s.p3x = x1; s.p3y = y1;
        s.tag = kSegTagLineTo; s.pathIndex = pathIdx;
        segments_.push_back(s);
        currentBboxMinX_ = std::min({currentBboxMinX_, x0, x1});
        currentBboxMinY_ = std::min({currentBboxMinY_, y0, y1});
        currentBboxMaxX_ = std::max({currentBboxMaxX_, x0, x1});
        currentBboxMaxY_ = std::max({currentBboxMaxY_, y0, y1});
    };

    // Helper: emit a closed CW quad (rectangle) as 4 line segments
    auto emitQuad = [&](float ax, float ay, float bx, float by,
                        float cx, float cy, float dx_, float dy_) {
        emitLine(ax, ay, bx, by);
        emitLine(bx, by, cx, cy);
        emitLine(cx, cy, dx_, dy_);
        emitLine(dx_, dy_, ax, ay);
    };

    // Helper: emit a closed fan (triangle or polygon) from center + arc points
    auto emitFan = [&](float cx, float cy, float a0, float a1, float radius, int nSteps) {
        // Build arc points
        float prevX = cx + std::cos(a0) * radius;
        float prevY = cy + std::sin(a0) * radius;
        float da = a1 - a0;
        for (int k = 1; k <= nSteps; k++) {
            float t = a0 + da * (float)k / (float)nSteps;
            float curX = cx + std::cos(t) * radius;
            float curY = cy + std::sin(t) * radius;
            // CW triangle: center → prev → cur → center
            emitLine(cx, cy, prevX, prevY);
            emitLine(prevX, prevY, curX, curY);
            emitLine(curX, curY, cx, cy);
            prevX = curX; prevY = curY;
        }
    };

    // Emit per-segment quads
    for (uint32_t i = 0; i < segCount; i++) {
        uint32_t j = (i + 1) % pointCount;
        float x1 = pts[i * 2],     y1 = pts[i * 2 + 1];
        float x2 = pts[j * 2],     y2 = pts[j * 2 + 1];
        float nx = segs[i].nx, ny = segs[i].ny;
        // CW quad: (x1+n, y1+n) → (x2+n, y2+n) → (x2-n, y2-n) → (x1-n, y1-n)
        emitQuad(x1 + nx, y1 + ny,  x2 + nx, y2 + ny,
                 x2 - nx, y2 - ny,  x1 - nx, y1 - ny);
    }

    // Helper: emit a miter join triangle
    auto emitMiterJoin = [&](float jx, float jy,
                              float n0x, float n0y, float n1x, float n1y,
                              float hw_) {
        // Miter point: intersection of the two offset lines
        // Direction bisector
        float bx = n0x + n1x, by = n0y + n1y;
        float blen = std::sqrt(bx*bx + by*by);
        if (blen < 1e-6f) return;
        bx /= blen; by /= blen;
        // Miter length: hw / cos(half_angle)
        float cosHalf = (n0x * bx + n0y * by);
        if (std::abs(cosHalf) < 1e-6f) return;
        float miterLen = hw_ / cosHalf;
        // Check miter limit
        if (miterLen > miterLimit * hw_) {
            // Fall back to bevel
            float p0x = jx + n0x, p0y = jy + n0y;
            float p1x = jx + n1x, p1y = jy + n1y;
            emitLine(jx, jy, p0x, p0y);
            emitLine(p0x, p0y, p1x, p1y);
            emitLine(p1x, p1y, jx, jy);
            return;
        }
        float mx = jx + bx * miterLen, my = jy + by * miterLen;
        float p0x = jx + n0x, p0y = jy + n0y;
        float p1x = jx + n1x, p1y = jy + n1y;
        emitLine(p0x, p0y, mx, my);
        emitLine(mx, my, p1x, p1y);
        emitLine(p1x, p1y, p0x, p0y);
    };

    // Helper: emit a bevel join triangle
    auto emitBevelJoin = [&](float jx, float jy,
                              float n0x, float n0y, float n1x, float n1y) {
        float p0x = jx + n0x, p0y = jy + n0y;
        float p1x = jx + n1x, p1y = jy + n1y;
        emitLine(jx, jy, p0x, p0y);
        emitLine(p0x, p0y, p1x, p1y);
        emitLine(p1x, p1y, jx, jy);
    };

    // Emit joins at each interior vertex
    for (uint32_t i = 0; i < pointCount; i++) {
        if (!closed && (i == 0 || i == pointCount - 1)) continue;
        uint32_t prevSeg = (i + segCount - 1) % segCount;
        uint32_t nextSeg = i % segCount;

        float jx = pts[i * 2], jy = pts[i * 2 + 1];

        // Outer join (+ side)
        float n0px = segs[prevSeg].nx, n0py = segs[prevSeg].ny;
        float n1px = segs[nextSeg].nx, n1py = segs[nextSeg].ny;

        if (lineJoin == 0) {
            // Miter join
            emitMiterJoin(jx, jy, n0px, n0py, n1px, n1py, hw);
            emitMiterJoin(jx, jy, -n0px, -n0py, -n1px, -n1py, hw);
        } else if (lineJoin == 1) {
            // Bevel join
            emitBevelJoin(jx, jy, n0px, n0py, n1px, n1py);
            emitBevelJoin(jx, jy, -n0px, -n0py, -n1px, -n1py);
        } else {
            // Round join (default)
            float a0 = std::atan2(n0py, n0px);
            float a1 = std::atan2(n1py, n1px);
            float da = a1 - a0;
            if (da > 3.14159f) da -= 6.28318f;
            if (da < -3.14159f) da += 6.28318f;
            if (std::abs(da) > 0.01f) {
                int n = std::max(2, (int)(std::abs(da) * hw * 0.5f));
                n = std::min(n, 16);
                emitFan(jx, jy, a0, a0 + da, hw, n);
            }
            // Inner side
            a0 = std::atan2(-n0py, -n0px);
            a1 = std::atan2(-n1py, -n1px);
            da = a1 - a0;
            if (da > 3.14159f) da -= 6.28318f;
            if (da < -3.14159f) da += 6.28318f;
            if (std::abs(da) > 0.01f) {
                int n = std::max(2, (int)(std::abs(da) * hw * 0.5f));
                n = std::min(n, 16);
                emitFan(jx, jy, a0, a0 + da, hw, n);
            }
        }
    }

    // Emit caps at endpoints (open paths only)
    if (!closed) {
        auto emitCap = [&](float cx, float cy, float dirX, float dirY, bool isStart) {
            // dirX, dirY: direction of the segment at this endpoint
            float nx = -dirY * hw, ny = dirX * hw;  // normal

            if (lineCap == 0) {
                // Flat cap: no extension beyond endpoint (already covered by quad)
            } else if (lineCap == 1) {
                // Square cap: extend by hw in the direction away from path
                float ext = isStart ? -1.0f : 1.0f;
                float ex = dirX * hw * ext, ey = dirY * hw * ext;
                float p0x = cx + nx, p0y = cy + ny;
                float p1x = cx - nx, p1y = cy - ny;
                float p2x = p1x + ex, p2y = p1y + ey;
                float p3x = p0x + ex, p3y = p0y + ey;
                emitQuad(p0x, p0y, p3x, p3y, p2x, p2y, p1x, p1y);
            } else {
                // Round cap: semicircle
                float a0, a1;
                if (isStart) {
                    a0 = std::atan2(-ny, -nx);
                    a1 = a0 - 3.14159f;
                } else {
                    a0 = std::atan2(ny, nx);
                    a1 = a0 - 3.14159f;
                }
                int n = std::max(4, (int)(hw * 1.5f));
                n = std::min(n, 32);
                emitFan(cx, cy, a0, a1, hw, n);
            }
        };

        // Start cap
        emitCap(pts[0], pts[1], segs[0].dx, segs[0].dy, true);
        // End cap
        uint32_t last = pointCount - 1;
        emitCap(pts[last * 2], pts[last * 2 + 1],
                segs[segCount - 1].dx, segs[segCount - 1].dy, false);
    }

    uint32_t totalSegs = (uint32_t)segments_.size() - segOffset;
    if (totalSegs == 0) return true;

    // Apply transform to all segment points
    bool hasTransform = (m11 != 1.0f || m12 != 0.0f || m21 != 0.0f || m22 != 1.0f || tdx != 0.0f || tdy != 0.0f);
    if (hasTransform) {
        currentBboxMinX_ = 1e30f; currentBboxMinY_ = 1e30f;
        currentBboxMaxX_ = -1e30f; currentBboxMaxY_ = -1e30f;
        for (uint32_t si = segOffset; si < (uint32_t)segments_.size(); si++) {
            auto& s = segments_[si];
            // Stroke segments are all lines (tag=0), transform all points
            float nx0 = s.p0x * m11 + s.p0y * m21 + tdx;
            float ny0 = s.p0x * m12 + s.p0y * m22 + tdy;
            float nx1 = s.p1x * m11 + s.p1y * m21 + tdx;
            float ny1 = s.p1x * m12 + s.p1y * m22 + tdy;
            s.p0x = nx0; s.p0y = ny0;
            s.p1x = nx1; s.p1y = ny1;
            s.p2x = nx1; s.p2y = ny1;
            s.p3x = nx1; s.p3y = ny1;
            currentBboxMinX_ = std::min({currentBboxMinX_, nx0, nx1});
            currentBboxMinY_ = std::min({currentBboxMinY_, ny0, ny1});
            currentBboxMaxX_ = std::max({currentBboxMaxX_, nx0, nx1});
            currentBboxMaxY_ = std::max({currentBboxMaxY_, ny0, ny1});
        }
    }

    // Reject paths that span too many tiles
    float bboxW = currentBboxMaxX_ - currentBboxMinX_;
    float bboxH = currentBboxMaxY_ - currentBboxMinY_;
    if (bboxW > 16384.0f || bboxH > 16384.0f) {
        segments_.resize(segOffset);
        return false;
    }

    // CPU flatten stroke segments (all are lines already)
    for (uint32_t si = segOffset; si < (uint32_t)segments_.size(); si++) {
        auto& s = segments_[si];
        if (std::abs(s.p0x - s.p1x) < 1e-6f && std::abs(s.p0y - s.p1y) < 1e-6f) continue;
        LineSeg ls = {}; ls.p0x=s.p0x; ls.p0y=s.p0y; ls.p1x=s.p1x; ls.p1y=s.p1y; ls.pathIndex=s.pathIndex;
        cpuLineSegs_.push_back(ls);
    }

    // Compute per-path tile bbox (same as EncodeFillPath)
    uint32_t tileBboxX = (uint32_t)std::max(0.0f, std::floor(currentBboxMinX_)) / kTileWidth;
    uint32_t tileBboxY = (uint32_t)std::max(0.0f, std::floor(currentBboxMinY_)) / kTileHeight;
    uint32_t tileBboxR = std::min((uint32_t)std::ceil(currentBboxMaxX_) / kTileWidth + 1, tilesX_);
    uint32_t tileBboxB = std::min((uint32_t)std::ceil(currentBboxMaxY_) / kTileHeight + 1, tilesY_);
    uint32_t tileBboxW = tileBboxR > tileBboxX ? tileBboxR - tileBboxX : 0;
    uint32_t tileBboxH = tileBboxB > tileBboxY ? tileBboxB - tileBboxY : 0;
    uint32_t tileCount = tileBboxW * tileBboxH;
    uint32_t tileOffset = totalPathTiles_;
    totalPathTiles_ += tileCount;

    PathInfo info = {};
    info.segOffset = segOffset;
    info.segCount = totalSegs;
    info.fillRule = kFillRuleNonZero;
    info.tileOffset = tileOffset;
    info.tileBboxX = tileBboxX;
    info.tileBboxY = tileBboxY;
    info.tileBboxW = tileBboxW;
    info.tileBboxH = tileBboxH;
    pathInfos_.push_back(info);

    PathDraw draw = {};
    draw.colorR = r * a;
    draw.colorG = g * a;
    draw.colorB = b * a;
    draw.colorA = a;
    draw.bboxMinX = currentBboxMinX_;
    draw.bboxMinY = currentBboxMinY_;
    draw.bboxMaxX = currentBboxMaxX_;
    draw.bboxMaxY = currentBboxMaxY_;
    pathDraws_.push_back(draw);
    drawTags_.push_back({kDrawTagFill, pathIdx, 0, 0});
    clipEvents_.push_back({ClipEvent::kDraw, (uint32_t)(pathInfos_.size() - 1), 0, 0});

    return true;
}

bool D3D12VelloRenderer::EncodeStrokePathBrush(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    Brush* brush, float strokeWidth, bool closed,
    int32_t lineJoin, float miterLimit, float opacity,
    int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    float m11, float m12, float m21, float m22, float tdx, float tdy)
{
    if (!brush) return false;

    // Extract brush color/gradient info, then delegate to EncodeStrokePath for geometry
    auto brushType = brush->GetType();
    float r = 0, g = 0, b = 0, a = 1;
    if (brushType == JALIUM_BRUSH_SOLID) {
        auto* sb = static_cast<D3D12SolidBrush*>(brush);
        r = sb->r_; g = sb->g_; b = sb->b_; a = sb->a_ * opacity;
    }

    // Record pathDraw count before EncodeStrokePath (it pushes its own PathDraw)
    size_t drawCountBefore = pathDraws_.size();

    if (!EncodeStrokePath(startX, startY, commands, commandLength,
                          r, g, b, a, strokeWidth, closed, lineJoin, miterLimit,
                          lineCap, dashPattern, dashCount, dashOffset,
                          m11, m12, m21, m22, tdx, tdy))
        return false;

    // Patch the PathDraw that EncodeStrokePath just pushed with gradient info
    if (pathDraws_.size() > drawCountBefore) {
        auto& draw = pathDraws_.back();
        if (brushType == JALIUM_BRUSH_LINEAR_GRADIENT) {
            auto* lg = static_cast<D3D12LinearGradientBrush*>(brush);
            uint32_t gradIdx = AddGradientRamp(lg->stops_.data(), (uint32_t)lg->stops_.size());
            draw.brushType = kBrushLinearGradient;
            draw.gradientIndex = gradIdx;
            float gp0x = lg->startX_ * m11 + lg->startY_ * m21 + tdx;
            float gp0y = lg->startX_ * m12 + lg->startY_ * m22 + tdy;
            float gp1x = lg->endX_ * m11 + lg->endY_ * m21 + tdx;
            float gp1y = lg->endX_ * m12 + lg->endY_ * m22 + tdy;
            draw.gradParam0 = gp0x; draw.gradParam1 = gp0y;
            draw.gradParam2 = gp1x; draw.gradParam3 = gp1y;
            draw.gradParam4 = (float)kExtendPad;  // extend mode
            draw.colorA = opacity;
        } else if (brushType == JALIUM_BRUSH_RADIAL_GRADIENT) {
            auto* rg = static_cast<D3D12RadialGradientBrush*>(brush);
            uint32_t gradIdx = AddGradientRamp(rg->stops_.data(), (uint32_t)rg->stops_.size());
            draw.brushType = kBrushRadialGradient;
            draw.gradientIndex = gradIdx;
            float cx = rg->centerX_ * m11 + rg->centerY_ * m21 + tdx;
            float cy = rg->centerX_ * m12 + rg->centerY_ * m22 + tdy;
            float ox = rg->originX_ * m11 + rg->originY_ * m21 + tdx;
            float oy = rg->originX_ * m12 + rg->originY_ * m22 + tdy;
            float scaleX = std::sqrt(m11*m11 + m12*m12);
            float scaleY = std::sqrt(m21*m21 + m22*m22);
            draw.gradParam0 = cx; draw.gradParam1 = cy;
            draw.gradParam2 = rg->radiusX_ * scaleX; draw.gradParam3 = rg->radiusY_ * scaleY;
            draw.gradParam4 = ox; draw.gradParam5 = oy;
            float extFloat; uint32_t extU = kExtendPad;
            memcpy(&extFloat, &extU, sizeof(float));
            draw.colorR = extFloat;
            draw.colorA = opacity;
        }
    }
    return true;
}

// ============================================================================
// Blur Rounded Rect Primitive
// ============================================================================

void D3D12VelloRenderer::EncodeBlurRect(
    float x, float y, float w, float h,
    float cornerRadius, float blurSigma,
    float r, float g, float b, float a,
    float m11, float m12, float m21, float m22, float tdx, float tdy)
{
    // Transform rect corners to screen space
    float x0 = x * m11 + y * m21 + tdx;
    float y0 = x * m12 + y * m22 + tdy;
    float x1 = (x+w) * m11 + (y+h) * m21 + tdx;
    float y1 = (x+w) * m12 + (y+h) * m22 + tdy;

    // Expand bbox by blur sigma (3σ captures 99.7% of Gaussian)
    float expand = blurSigma * 3.0f;
    float bx0 = std::min(x0, x1) - expand;
    float by0 = std::min(y0, y1) - expand;
    float bx1 = std::max(x0, x1) + expand;
    float by1 = std::max(y0, y1) + expand;

    // Create a dummy path that covers the blur bbox
    // The blur rect is drawn directly by the fine shader, not via path geometry
    uint32_t pathIdx = (uint32_t)pathInfos_.size();

    PathInfo info = {};
    info.segOffset = 0; info.segCount = 0;
    info.fillRule = kFillRuleNonZero;
    info.tileBboxX = (uint32_t)std::max(0.0f, std::floor(bx0)) / kTileWidth;
    info.tileBboxY = (uint32_t)std::max(0.0f, std::floor(by0)) / kTileHeight;
    uint32_t tileBboxR = std::min((uint32_t)std::ceil(bx1) / kTileWidth + 1, tilesX_);
    uint32_t tileBboxB = std::min((uint32_t)std::ceil(by1) / kTileHeight + 1, tilesY_);
    info.tileBboxW = tileBboxR > info.tileBboxX ? tileBboxR - info.tileBboxX : 0;
    info.tileBboxH = tileBboxB > info.tileBboxY ? tileBboxB - info.tileBboxY : 0;
    info.tileOffset = totalPathTiles_;
    totalPathTiles_ += info.tileBboxW * info.tileBboxH;
    pathInfos_.push_back(info);

    // Store blur rect params in PathDraw
    // Use brushType = 0xFF to mark as blur rect (special handling in BuildPtcl)
    PathDraw draw = {};
    draw.colorR = r * a;  // premultiply
    draw.colorG = g * a;
    draw.colorB = b * a;
    draw.colorA = a;
    draw.bboxMinX = std::min(x0, x1);
    draw.bboxMinY = std::min(y0, y1);
    draw.bboxMaxX = std::max(x0, x1);
    draw.bboxMaxY = std::max(y0, y1);
    draw.brushType = 0xFF;  // special: blur rect marker
    draw.gradParam0 = std::min(x0, x1);  // rect x
    draw.gradParam1 = std::min(y0, y1);  // rect y
    draw.gradParam2 = std::abs(x1 - x0);  // rect w
    draw.gradParam3 = std::abs(y1 - y0);  // rect h
    draw.gradParam4 = cornerRadius;
    draw.gradParam5 = blurSigma;
    pathDraws_.push_back(draw);

    clipEvents_.push_back({ClipEvent::kDraw, pathIdx, 0, 0});
    drawTags_.push_back({kDrawTagFill, pathIdx, 0, 0});
}

// ============================================================================
// Image Brush
// ============================================================================

uint32_t D3D12VelloRenderer::RegisterImage(ID3D12Resource* texture, uint32_t width, uint32_t height)
{
    if (!texture || imageEntries_.size() >= kMaxImages) return 0;
    uint32_t idx = (uint32_t)imageEntries_.size();
    imageEntries_.push_back({texture, width, height});
    return idx;
}

bool D3D12VelloRenderer::EncodeFillPathImage(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    uint32_t imageIndex, uint32_t fillRule, float opacity,
    float m11, float m12, float m21, float m22, float tdx, float tdy)
{
    if (imageIndex >= imageEntries_.size()) return false;

    uint32_t pathIdx;
    if (!EncodePathGeometry(startX, startY, commands, commandLength,
                            fillRule, m11, m12, m21, m22, tdx, tdy, pathIdx))
        return false;

    PathDraw draw = {};
    draw.bboxMinX = currentBboxMinX_;
    draw.bboxMinY = currentBboxMinY_;
    draw.bboxMaxX = currentBboxMaxX_;
    draw.bboxMaxY = currentBboxMaxY_;
    draw.brushType = kBrushImage;
    draw.gradientIndex = imageIndex;  // reuse field for image index
    // Store UV mapping: map path bbox to [0,1] UV space
    draw.gradParam0 = currentBboxMinX_;  // u0 origin x
    draw.gradParam1 = currentBboxMinY_;  // v0 origin y
    float bw = currentBboxMaxX_ - currentBboxMinX_;
    float bh = currentBboxMaxY_ - currentBboxMinY_;
    draw.gradParam2 = (bw > 1e-6f) ? 1.0f / bw : 0;  // u scale
    draw.gradParam3 = (bh > 1e-6f) ? 1.0f / bh : 0;  // v scale
    draw.colorA = opacity;
    pathDraws_.push_back(draw);

    clipEvents_.push_back({ClipEvent::kDraw, pathIdx, 0, 0});
    drawTags_.push_back({kDrawTagFill, pathIdx, 0, 0});
    return true;
}

// ============================================================================
// Clip Operations
// ============================================================================

bool D3D12VelloRenderer::EncodeBeginClip(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    uint32_t fillRule,
    float m11, float m12, float m21, float m22, float tdx, float tdy)
{
    if (clipDepth_ >= kMaxClipDepth) return false;

    // Encode clip path geometry (same as fill path)
    uint32_t clipPathIdx;
    if (!EncodePathGeometry(startX, startY, commands, commandLength,
                            fillRule, m11, m12, m21, m22, tdx, tdy, clipPathIdx))
        return false;

    // Clip path draw: white opaque so clip area has full alpha coverage.
    // The fine shader uses the clip pixel's alpha as the mask.
    PathDraw draw = {};
    draw.bboxMinX = currentBboxMinX_;
    draw.bboxMinY = currentBboxMinY_;
    draw.bboxMaxX = currentBboxMaxX_;
    draw.bboxMaxY = currentBboxMaxY_;
    draw.brushType = kBrushSolid;
    draw.colorR = 1.0f;  // premultiplied white
    draw.colorG = 1.0f;
    draw.colorB = 1.0f;
    draw.colorA = 1.0f;
    pathDraws_.push_back(draw);

    // Record clip begin event
    ClipEntry entry = {};
    entry.pathIdx = clipPathIdx;
    entry.blendMode = kBlendSrcOver;
    entry.alpha = 1.0f;
    clipStack_.push_back(entry);
    clipDepth_++;

    clipEvents_.push_back({ClipEvent::kBeginClip, clipPathIdx, 0, 0});
    drawTags_.push_back({kDrawTagBeginClip, clipPathIdx, 0, 0});
    return true;
}

void D3D12VelloRenderer::EncodeBeginClipRect(float x, float y, float w, float h)
{
    if (clipDepth_ >= kMaxClipDepth) return;

    // Build a simple rectangle path: MoveTo + 4 LineTo + ClosePath
    float commands[16];
    uint32_t ci = 0;
    // LineTo right
    commands[ci++] = (float)kTagLineTo; commands[ci++] = x + w; commands[ci++] = y;
    // LineTo bottom-right
    commands[ci++] = (float)kTagLineTo; commands[ci++] = x + w; commands[ci++] = y + h;
    // LineTo bottom-left
    commands[ci++] = (float)kTagLineTo; commands[ci++] = x; commands[ci++] = y + h;
    // ClosePath
    commands[ci++] = (float)kTagClosePath;

    EncodeBeginClip(x, y, commands, ci, kFillRuleNonZero);
}

void D3D12VelloRenderer::EncodeEndClip(uint32_t blendMode, float alpha)
{
    if (clipDepth_ == 0) return;

    clipDepth_--;
    if (!clipStack_.empty()) {
        clipStack_.back().blendMode = blendMode;
        clipStack_.back().alpha = alpha;
        clipStack_.pop_back();
    }

    clipEvents_.push_back({ClipEvent::kEndClip, 0, blendMode, alpha});
    drawTags_.push_back({kDrawTagEndClip, 0, blendMode, alpha});
}

uint32_t D3D12VelloRenderer::RegisterMask(ID3D12Resource* texture, uint32_t width, uint32_t height, bool isLuminance)
{
    if (!texture || maskEntries_.size() >= kMaxMasks) return 0;
    uint32_t idx = (uint32_t)maskEntries_.size();
    maskEntries_.push_back({texture, width, height, isLuminance});
    return idx;
}

void D3D12VelloRenderer::EncodeBeginClipMask(uint32_t maskIndex, float x, float y, float w, float h)
{
    if (clipDepth_ >= kMaxClipDepth) return;
    if (maskIndex >= maskEntries_.size()) return;

    // Create a dummy path for the mask region (covers the mask bbox)
    uint32_t pathIdx = (uint32_t)pathInfos_.size();

    PathInfo info = {};
    info.segOffset = 0; info.segCount = 0;
    info.fillRule = kFillRuleNonZero;
    info.tileBboxX = (uint32_t)std::max(0.0f, std::floor(x)) / kTileWidth;
    info.tileBboxY = (uint32_t)std::max(0.0f, std::floor(y)) / kTileHeight;
    uint32_t tileBboxR = std::min((uint32_t)std::ceil(x + w) / kTileWidth + 1, tilesX_);
    uint32_t tileBboxB = std::min((uint32_t)std::ceil(y + h) / kTileHeight + 1, tilesY_);
    info.tileBboxW = tileBboxR > info.tileBboxX ? tileBboxR - info.tileBboxX : 0;
    info.tileBboxH = tileBboxB > info.tileBboxY ? tileBboxB - info.tileBboxY : 0;
    info.tileOffset = totalPathTiles_;
    totalPathTiles_ += info.tileBboxW * info.tileBboxH;
    pathInfos_.push_back(info);

    // PathDraw with mask info encoded in brush fields
    PathDraw draw = {};
    draw.bboxMinX = x; draw.bboxMinY = y;
    draw.bboxMaxX = x + w; draw.bboxMaxY = y + h;
    draw.brushType = kBrushSolid;
    draw.colorA = 1.0f;
    pathDraws_.push_back(draw);

    ClipEntry entry = {};
    entry.pathIdx = pathIdx;
    entry.blendMode = kBlendSrcOver;
    entry.alpha = 1.0f;
    clipStack_.push_back(entry);
    clipDepth_++;

    clipEvents_.push_back({ClipEvent::kBeginClip, pathIdx, 0, 0});
    drawTags_.push_back({kDrawTagBeginClip, pathIdx, 0, 0});
}

// ============================================================================
// GPU Dispatch
// ============================================================================

bool D3D12VelloRenderer::Dispatch(ID3D12GraphicsCommandList* cmdList, uint32_t frameIndex)
{
    if (!initialized_ || !cmdList || pathInfos_.empty() || segments_.empty()) return false;

    // Route to GPU pipeline if enabled
    if (useGpuPipeline_) {
        return DispatchGPU(cmdList, frameIndex);
    }

    // CPU pipeline
    static bool firstDispatch = true;
    if (firstDispatch) {
        char buf[256];
        sprintf_s(buf, "[Vello] Dispatch: paths=%u segs=%u lineSegs=%u tiles=%ux%u\n",
            (uint32_t)pathInfos_.size(), (uint32_t)segments_.size(),
            (uint32_t)cpuLineSegs_.size(), tilesX_, tilesY_);
        OutputDebugStringA(buf);
        firstDispatch = false;
    }

    if (!EnsureBuffers()) return false;
    if (!EnsureOutputTexture(viewportW_, viewportH_)) return false;

    // Select per-frame resources to avoid CPU/GPU race conditions
    uint32_t fi = frameIndex % kMaxFrames;
    auto& fu = frameUploads_[fi];
    auto& srvHeap = computeSrvHeap_[fi];

    // ── Upload CPU data to GPU ──

    // Segments
    {
        void* mapped = nullptr;
        fu.segmentUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, segments_.data(), segments_.size() * sizeof(PathSegment));
        fu.segmentUpload->Unmap(0, nullptr);
        cmdList->CopyBufferRegion(segmentBuffer_.Get(), 0, fu.segmentUpload.Get(), 0,
                                   segments_.size() * sizeof(PathSegment));
    }

    // PathInfo
    {
        void* mapped = nullptr;
        fu.pathInfoUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, pathInfos_.data(), pathInfos_.size() * sizeof(PathInfo));
        fu.pathInfoUpload->Unmap(0, nullptr);
        cmdList->CopyBufferRegion(pathInfoBuffer_.Get(), 0, fu.pathInfoUpload.Get(), 0,
                                   pathInfos_.size() * sizeof(PathInfo));
    }

    // PathDraw
    {
        void* mapped = nullptr;
        fu.pathDrawUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, pathDraws_.data(), pathDraws_.size() * sizeof(PathDraw));
        fu.pathDrawUpload->Unmap(0, nullptr);
        cmdList->CopyBufferRegion(pathDrawBuffer_.Get(), 0, fu.pathDrawUpload.Get(), 0,
                                   pathDraws_.size() * sizeof(PathDraw));
    }

    // Constants — layout must match cbuffer VelloConfig in the fine shader
    VelloConstants constants = {};
    constants.widthTiles = tilesX_;
    constants.heightTiles = tilesY_;
    constants.widthPixels = viewportW_;
    constants.heightPixels = viewportH_;
    constants.baseColor = 0;  // transparent background
    constants.numPaths = (uint32_t)pathInfos_.size();
    constants.numSegments = (uint32_t)segments_.size();
    {
        void* mapped = nullptr;
        fu.constantUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, &constants, sizeof(constants));
        fu.constantUpload->Unmap(0, nullptr);
    }

    // Write actual line count to counter buffer (replaces GPU flatten atomic counter)
    {
        uint32_t lc = (uint32_t)cpuLineSegs_.size();
        void* mapped = nullptr;
        fu.lineCountUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, &lc, sizeof(uint32_t));
        fu.lineCountUpload->Unmap(0, nullptr);
        cmdList->CopyBufferRegion(lineCountBuffer_.Get(), 0, fu.lineCountUpload.Get(), 0, 4);
    }

    // Note: tileBuffer_ zeroing is skipped in whole-path mode.
    // The fine shader reads coverage from PTCL (t3/t4), not from tiles (u2).
    // Zeroing tileBuffer_ would leave it in COPY_DEST state (never transitioned
    // to UAV), which can corrupt the descriptor table binding.

    // ── Resource barriers: COPY_DEST → UAV / SRV ──
    // Transition uploaded data and output resources.
    // lineSegBuffer_ and lineCountBuffer_ are handled separately after CPU upload.
    D3D12_RESOURCE_BARRIER barriers[8];
    int bCount = 0;
    barriers[bCount++] = MakeBarrier(segmentBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    barriers[bCount++] = MakeBarrier(pathInfoBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    barriers[bCount++] = MakeBarrier(pathDrawBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    barriers[bCount++] = MakeBarrier(lineCountBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
    barriers[bCount++] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
    cmdList->ResourceBarrier(bCount, barriers);

    // ── Clear output texture to transparent ──
    {
        // Create UAV descriptor in CPU-only heap for ClearUnorderedAccessViewFloat
        D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        auto cpuHandle = cpuUavHeap_->GetCPUDescriptorHandleForHeapStart();
        device_->CreateUnorderedAccessView(outputTexture_.Get(), nullptr, &uavDesc, cpuHandle);

        // GPU handle in shader-visible heap (slot 7 = output UAV, created below)
        // We need to create the GPU UAV first before clearing
        auto gpuUavHandle = srvHeap->GetGPUDescriptorHandleForHeapStart();
        gpuUavHandle.ptr += 11 * srvDescSize_;  // u4 = slot 11
        auto cpuSrvHandle = srvHeap->GetCPUDescriptorHandleForHeapStart();
        cpuSrvHandle.ptr += 11 * srvDescSize_;  // u4 = slot 11
        device_->CreateUnorderedAccessView(outputTexture_.Get(), nullptr, &uavDesc, cpuSrvHandle);

        ID3D12DescriptorHeap* heaps[] = { srvHeap.Get() };
        cmdList->SetDescriptorHeaps(1, heaps);

        const float clearColor[4] = { 0, 0, 0, 0 };
        cmdList->ClearUnorderedAccessViewFloat(gpuUavHandle, cpuHandle,
                                                outputTexture_.Get(), clearColor, 0, nullptr);
    }

    // ── Create descriptors ──
    // SRV layout must match fine shader registers:
    //   t0 = seg_data (sorted VelloSortedSeg, rebound later after upload)
    //   t1 = ptcl (ByteAddressBuffer, bound during PTCL upload stage)
    //   t2 = gradRamps (StructuredBuffer<uint>)
    //   t3 = info_data (ByteAddressBuffer = ptclOffsets, bound during PTCL upload)
    //   t4 = imageAtlas (Texture2D, optional)
    auto cpuBase = srvHeap->GetCPUDescriptorHandleForHeapStart();
    auto gpuBase = srvHeap->GetGPUDescriptorHandleForHeapStart();

    // SRV t0: placeholder (will be rebound to sorted segments after upload)
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC srv = {};
        srv.Format = DXGI_FORMAT_UNKNOWN;
        srv.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        srv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srv.Buffer.NumElements = (uint32_t)segments_.size();
        srv.Buffer.StructureByteStride = sizeof(PathSegment);
        auto h = cpuBase; h.ptr += 0 * srvDescSize_;
        device_->CreateShaderResourceView(segmentBuffer_.Get(), &srv, h);
    }
    // SRV t1: placeholder for ptcl (bound during PTCL upload stage)
    // SRV t2: gradient ramps (shader register t2 = gradRamps)
    {
        // Ensure gradient ramp GPU buffer exists
        uint32_t neededGrads = std::max(gradientCount_, 1u);
        if (neededGrads > gradientRampCapacity_) {
            gradientRampCapacity_ = neededGrads * 2;
            auto flags = D3D12_RESOURCE_FLAG_NONE;  // SRV only
            CreateBuffer(device_, gradientRampCapacity_ * kGradientRampWidth * sizeof(uint32_t),
                         D3D12_HEAP_TYPE_DEFAULT, flags, D3D12_RESOURCE_STATE_COMMON, gradientRampBuffer_);
        }
        // Upload gradient ramp data
        if (gradientCount_ > 0 && gradientRampBuffer_) {
            uint32_t gradBytes = gradientCount_ * kGradientRampWidth * sizeof(uint32_t);
            if (gradBytes > fu.gradientRampUploadCapacity * kGradientRampWidth * sizeof(uint32_t) || !fu.gradientRampUpload) {
                fu.gradientRampUploadCapacity = gradientCount_ * 2;
                CreateBuffer(device_, fu.gradientRampUploadCapacity * kGradientRampWidth * sizeof(uint32_t),
                             D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
                             D3D12_RESOURCE_STATE_GENERIC_READ, fu.gradientRampUpload);
            }
            if (fu.gradientRampUpload) {
                void* mapped = nullptr;
                fu.gradientRampUpload->Map(0, nullptr, &mapped);
                memcpy(mapped, gradientRamps_.data(), gradBytes);
                fu.gradientRampUpload->Unmap(0, nullptr);
                cmdList->CopyBufferRegion(gradientRampBuffer_.Get(), 0, fu.gradientRampUpload.Get(), 0, gradBytes);
                // Transition gradient buffer from COPY_DEST → SRV
                auto gb = MakeBarrier(gradientRampBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
                cmdList->ResourceBarrier(1, &gb);
            }
        }
        D3D12_SHADER_RESOURCE_VIEW_DESC srv = {};
        srv.Format = DXGI_FORMAT_UNKNOWN;
        srv.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        srv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srv.Buffer.NumElements = std::max(gradientCount_ * kGradientRampWidth, 1u);
        srv.Buffer.StructureByteStride = sizeof(uint32_t);
        auto h = cpuBase; h.ptr += 2 * srvDescSize_;  // t2 = gradRamps
        device_->CreateShaderResourceView(gradientRampBuffer_.Get(), &srv, h);
    }

    // UAV u0: output texture (shader writes rasterized pixels here)
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uav = {};
        uav.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        uav.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        auto h = cpuBase; h.ptr += 7 * srvDescSize_;  // u0 = slot 7
        device_->CreateUnorderedAccessView(outputTexture_.Get(), nullptr, &uav, h);
    }
    // UAV u1: blend_spill — for clip stack overflow (shader register u1)
    {
        // Ensure blend spill buffer exists
        if (!blendSpillBuffer_) {
            uint32_t blendCap = tilesX_ * tilesY_ * 256;  // 256 u32s per tile (generous)
            CreateBuffer(device_, blendCap * sizeof(uint32_t), D3D12_HEAP_TYPE_DEFAULT,
                         D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COMMON, blendSpillBuffer_);
            blendSpillCapacity_ = blendCap;
        }
        D3D12_UNORDERED_ACCESS_VIEW_DESC uav = {};
        uav.Format = DXGI_FORMAT_R32_TYPELESS;
        uav.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
        uav.Buffer.NumElements = blendSpillCapacity_;
        uav.Buffer.Flags = D3D12_BUFFER_UAV_FLAG_RAW;
        auto h = cpuBase; h.ptr += 8 * srvDescSize_;  // u1 = slot 8
        device_->CreateUnorderedAccessView(blendSpillBuffer_.Get(), nullptr, &uav, h);
    }
    // UAV u2: tiles — slot 24
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uav = {};
        uav.Format = DXGI_FORMAT_R32_TYPELESS;
        uav.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
        uav.Buffer.NumElements = tileCapacity_ * 3;
        uav.Buffer.Flags = D3D12_BUFFER_UAV_FLAG_RAW;
        auto h = cpuBase; h.ptr += 9 * srvDescSize_;  // u2 = slot 9
        device_->CreateUnorderedAccessView(tileBuffer_.Get(), nullptr, &uav, h);
    }
    // UAV u3: tileCmds — slot 25
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uav = {};
        uav.Format = DXGI_FORMAT_R32_TYPELESS;
        uav.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
        uav.Buffer.NumElements = tileCmdCapacity_ * 3;
        uav.Buffer.Flags = D3D12_BUFFER_UAV_FLAG_RAW;
        auto h = cpuBase; h.ptr += 10 * srvDescSize_;  // u3 = slot 10
        device_->CreateUnorderedAccessView(tileCmdBuffer_.Get(), nullptr, &uav, h);
    }
    // UAV u4: output texture — slot 26
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uav = {};
        uav.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        uav.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        auto h = cpuBase; h.ptr += 11 * srvDescSize_;  // u4 = slot 11
        device_->CreateUnorderedAccessView(outputTexture_.Get(), nullptr, &uav, h);
    }

    // SRV t6: image atlas - slot 6
    {
        if (!imageEntries_.empty()) {
            auto& img = imageEntries_[0];
            D3D12_SHADER_RESOURCE_VIEW_DESC srv = {};
            srv.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            srv.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
            srv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            srv.Texture2D.MipLevels = 1;
            auto h = cpuBase; h.ptr += 6 * srvDescSize_;
            device_->CreateShaderResourceView(img.texture, &srv, h);
        } else {
            D3D12_SHADER_RESOURCE_VIEW_DESC srv = {};
            srv.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            srv.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
            srv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            srv.Texture2D.MipLevels = 1;
            auto h = cpuBase; h.ptr += 6 * srvDescSize_;
            device_->CreateShaderResourceView(nullptr, &srv, h);
        }
    }

    // ── Set compute state ──
    ID3D12DescriptorHeap* heaps[] = { srvHeap.Get() };
    cmdList->SetDescriptorHeaps(1, heaps);
    cmdList->SetComputeRootSignature(cpuRootSig_.Get());
    cmdList->SetComputeRootConstantBufferView(0, fu.constantUpload->GetGPUVirtualAddress());

    auto srvGpu = gpuBase;
    auto uavGpu = gpuBase;
    uavGpu.ptr += 7 * srvDescSize_;  // UAVs start at slot 7 (after 7 SRVs: t0-t6)

    cmdList->SetComputeRootDescriptorTable(1, srvGpu);
    cmdList->SetComputeRootDescriptorTable(2, uavGpu);

    // -- Stage 1: CPU Flatten + BuildPtcl --
    BuildPtcl();

    // Safety: skip GPU dispatch if data is too large (prevents TDR)
    if (cpuSortedSegs_.size() > 500000 || cpuPtcl_.size() > 2000000) {
        // Data too large for GPU — output texture already cleared to transparent.
        // Transition everything back to COMMON so next frame starts cleanly.
        // IMPORTANT: outputTexture_ must end in COMMON (textures do NOT auto-decay).
        D3D12_RESOURCE_BARRIER resetBarriers[5];
        int rCount = 0;
        resetBarriers[rCount++] = MakeBarrier(segmentBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
        resetBarriers[rCount++] = MakeBarrier(pathInfoBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
        resetBarriers[rCount++] = MakeBarrier(pathDrawBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
        resetBarriers[rCount++] = MakeBarrier(lineCountBuffer_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COMMON);
        resetBarriers[rCount++] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COMMON);
        cmdList->ResourceBarrier(rCount, resetBarriers);
        return false;  // output is cleared but no paths rendered
    }

    // Upload sorted segments to GPU (per-frame upload buffer)
    uint32_t lineCount = (uint32_t)cpuSortedSegs_.size();
    if (lineCount > 0) {
        if (lineCount > lineSegCapacity_) {
            lineSegCapacity_ = lineCount * 2;
            auto flags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
            CreateBuffer(device_, lineSegCapacity_ * sizeof(VelloSortedSeg), D3D12_HEAP_TYPE_DEFAULT,
                         flags, D3D12_RESOURCE_STATE_COMMON, lineSegBuffer_);
        }
        if (lineCount > fu.lineSegUploadCapacity) {
            fu.lineSegUploadCapacity = lineCount * 2;
            CreateBuffer(device_, fu.lineSegUploadCapacity * sizeof(VelloSortedSeg), D3D12_HEAP_TYPE_UPLOAD,
                         D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ, fu.lineSegUpload);
        }
        if (fu.lineSegUpload) {
            void* mapped = nullptr;
            fu.lineSegUpload->Map(0, nullptr, &mapped);
            memcpy(mapped, cpuSortedSegs_.data(), lineCount * sizeof(VelloSortedSeg));
            fu.lineSegUpload->Unmap(0, nullptr);
            cmdList->CopyBufferRegion(lineSegBuffer_.Get(), 0, fu.lineSegUpload.Get(), 0, lineCount * sizeof(VelloSortedSeg));
        }
    }

    // Transition lineSegBuffer_ from COPY_DEST → SRV
    {
        auto b = MakeBarrier(lineSegBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        cmdList->ResourceBarrier(1, &b);
    }

    // Rebind SRV t0 to lineSegBuffer_ (sorted VelloSortedSeg, matches HLSL Segment)
    {
        D3D12_SHADER_RESOURCE_VIEW_DESC srv = {};
        srv.Format = DXGI_FORMAT_UNKNOWN;
        srv.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        srv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srv.Buffer.NumElements = lineCount;
        srv.Buffer.StructureByteStride = sizeof(VelloSortedSeg);
        auto h = cpuBase; h.ptr += 0 * srvDescSize_;
        device_->CreateShaderResourceView(lineSegBuffer_.Get(), &srv, h);
        cmdList->SetComputeRootDescriptorTable(1, srvGpu);
    }

    // ── Stage 2: Upload PTCL and ptclOffsets (built by BuildPtcl above) ──
    bool ptclReady = false;
    {
        uint32_t ptclBytes = (uint32_t)(cpuPtcl_.size() * sizeof(uint32_t));
        uint32_t offsetsBytes = (uint32_t)(cpuPtclOffsets_.size() * sizeof(uint32_t));
        if (ptclBytes > 0 && offsetsBytes > 0) {
            // Ensure GPU buffers exist
            if (!ptclBuffer_ || ptclBytes > ptclCapacity_ * sizeof(uint32_t)) {
                ptclCapacity_ = (uint32_t)cpuPtcl_.size() * 2;
                CreateBuffer(device_, ptclCapacity_ * sizeof(uint32_t), D3D12_HEAP_TYPE_DEFAULT,
                             D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_COMMON, ptclBuffer_);
            }
            if (!pathTileBuffer_ || offsetsBytes > ptclOffsetsCapacity_ * sizeof(uint32_t)) {
                ptclOffsetsCapacity_ = (uint32_t)cpuPtclOffsets_.size() * 2;
                CreateBuffer(device_, ptclOffsetsCapacity_ * sizeof(uint32_t), D3D12_HEAP_TYPE_DEFAULT,
                             D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_COMMON, pathTileBuffer_);
            }
            // Per-frame upload buffers (only recreate when capacity insufficient)
            if (!fu.ptclUpload || ptclBytes > fu.ptclUploadCapacity) {
                fu.ptclUploadCapacity = ptclBytes * 2;
                CreateBuffer(device_, fu.ptclUploadCapacity, D3D12_HEAP_TYPE_UPLOAD,
                             D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ, fu.ptclUpload);
            }
            if (!fu.ptclOffsetsUpload || offsetsBytes > fu.ptclOffsetsUploadCapacity) {
                fu.ptclOffsetsUploadCapacity = offsetsBytes * 2;
                CreateBuffer(device_, fu.ptclOffsetsUploadCapacity, D3D12_HEAP_TYPE_UPLOAD,
                             D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ, fu.ptclOffsetsUpload);
            }

            if (fu.ptclUpload && fu.ptclOffsetsUpload && ptclBuffer_ && pathTileBuffer_) {
                void* mapped1 = nullptr;
                void* mapped2 = nullptr;
                bool ok = true;
                if (FAILED(fu.ptclUpload->Map(0, nullptr, &mapped1)) || !mapped1) ok = false;
                if (ok) {
                    memcpy(mapped1, cpuPtcl_.data(), ptclBytes);
                    fu.ptclUpload->Unmap(0, nullptr);
                }
                if (ok && (FAILED(fu.ptclOffsetsUpload->Map(0, nullptr, &mapped2)) || !mapped2)) ok = false;
                if (ok) {
                    memcpy(mapped2, cpuPtclOffsets_.data(), offsetsBytes);
                    fu.ptclOffsetsUpload->Unmap(0, nullptr);
                }
                if (ok) {
                    cmdList->CopyBufferRegion(ptclBuffer_.Get(), 0, fu.ptclUpload.Get(), 0, ptclBytes);
                    cmdList->CopyBufferRegion(pathTileBuffer_.Get(), 0, fu.ptclOffsetsUpload.Get(), 0, offsetsBytes);

                    // Transition PTCL buffers from COPY_DEST → SRV
                    D3D12_RESOURCE_BARRIER ptclBarriers[2];
                    ptclBarriers[0] = MakeBarrier(ptclBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
                    ptclBarriers[1] = MakeBarrier(pathTileBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
                    cmdList->ResourceBarrier(2, ptclBarriers);

                    // Create SRVs for PTCL (t3) and ptclOffsets (t4)
                    {
                        D3D12_SHADER_RESOURCE_VIEW_DESC srv = {};
                        srv.Format = DXGI_FORMAT_R32_TYPELESS;
                        srv.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
                        srv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
                        srv.Buffer.NumElements = (uint32_t)cpuPtcl_.size();
                        srv.Buffer.Flags = D3D12_BUFFER_SRV_FLAG_RAW;
                        auto h = cpuBase; h.ptr += 1 * srvDescSize_;  // t1 = ptcl
                        device_->CreateShaderResourceView(ptclBuffer_.Get(), &srv, h);
                    }
                    {
                        D3D12_SHADER_RESOURCE_VIEW_DESC srv = {};
                        srv.Format = DXGI_FORMAT_R32_TYPELESS;
                        srv.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
                        srv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
                        srv.Buffer.NumElements = (uint32_t)cpuPtclOffsets_.size();
                        srv.Buffer.Flags = D3D12_BUFFER_SRV_FLAG_RAW;
                        auto h = cpuBase; h.ptr += 3 * srvDescSize_;  // t3 = info_data (ptclOffsets)
                        device_->CreateShaderResourceView(pathTileBuffer_.Get(), &srv, h);
                    }
                    cmdList->SetComputeRootDescriptorTable(1, srvGpu);
                    ptclReady = true;
                }
            }
        }
    }

    // ── Stage 3: Fine rasterization ──
    // Only dispatch if PTCL was successfully uploaded.
    if (ptclReady) {
        // Re-set compute state in case we jumped from skipPtcl
        {
            ID3D12DescriptorHeap* heaps[] = { srvHeap.Get() };
            cmdList->SetDescriptorHeaps(1, heaps);
            cmdList->SetComputeRootSignature(cpuRootSig_.Get());
            cmdList->SetComputeRootConstantBufferView(0, fu.constantUpload->GetGPUVirtualAddress());
            auto sGpu = srvHeap->GetGPUDescriptorHandleForHeapStart();
            auto uGpu = sGpu;
            uGpu.ptr += 7 * srvDescSize_;  // UAVs start at slot 7
            cmdList->SetComputeRootDescriptorTable(1, sGpu);
            cmdList->SetComputeRootDescriptorTable(2, uGpu);
        }
        cmdList->SetPipelineState(cpuFinePSO_.Get());
        cmdList->Dispatch(tilesX_, tilesY_, 1);
    }

    // Barrier: output UAV → COMMON for compositing (auto-promotes to SRV on read)
    {
        auto b = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                              D3D12_RESOURCE_STATE_COMMON);
        cmdList->ResourceBarrier(1, &b);
    }

    // Transition buffers back to COMMON
    D3D12_RESOURCE_BARRIER resetBarriers[14];
    int rCount = 0;
    if (segmentBuffer_) resetBarriers[rCount++] = MakeBarrier(segmentBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
    if (pathInfoBuffer_) resetBarriers[rCount++] = MakeBarrier(pathInfoBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
    if (pathDrawBuffer_) resetBarriers[rCount++] = MakeBarrier(pathDrawBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
    if (lineCountBuffer_) resetBarriers[rCount++] = MakeBarrier(lineCountBuffer_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COMMON);
    if (lineSegBuffer_) resetBarriers[rCount++] = MakeBarrier(lineSegBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
    if (gradientCount_ > 0 && gradientRampBuffer_) resetBarriers[rCount++] = MakeBarrier(gradientRampBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
    // ptclBuffer_ and pathTileBuffer_ were transitioned to SRV only if ptclReady
    if (ptclReady && ptclBuffer_) resetBarriers[rCount++] = MakeBarrier(ptclBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
    if (ptclReady && pathTileBuffer_) resetBarriers[rCount++] = MakeBarrier(pathTileBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
    if (rCount > 0) cmdList->ResourceBarrier(rCount, resetBarriers);

    return ptclReady;  // true only if fine shader dispatched successfully
}

// ============================================================================
// Blur Pipeline Creation
// ============================================================================

bool D3D12VelloRenderer::CreateBlurPipelines()
{
    // Root signature for blur shaders: CBV b0 + SRV t0 + UAV u0
    {
        D3D12_DESCRIPTOR_RANGE srvRange = {};
        srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
        srvRange.NumDescriptors = 1;
        srvRange.BaseShaderRegister = 0;

        D3D12_DESCRIPTOR_RANGE uavRange = {};
        uavRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV;
        uavRange.NumDescriptors = 1;
        uavRange.BaseShaderRegister = 0;

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

        D3D12_ROOT_SIGNATURE_DESC desc = {};
        desc.NumParameters = 3;
        desc.pParameters = params;

        ComPtr<ID3DBlob> sig, err;
        if (FAILED(D3D12SerializeRootSignature(&desc, D3D_ROOT_SIGNATURE_VERSION_1_0, &sig, &err)))
            return false;
        if (FAILED(device_->CreateRootSignature(0, sig->GetBufferPointer(),
                   sig->GetBufferSize(), IID_PPV_ARGS(&blurRootSig_))))
            return false;
    }

    UINT compileFlags = 0;
#ifdef _DEBUG
    compileFlags = D3DCOMPILE_DEBUG | D3DCOMPILE_SKIP_OPTIMIZATION;
#else
    compileFlags = D3DCOMPILE_OPTIMIZATION_LEVEL3;
#endif
    auto compile = [&](const char* src, size_t len, const char* name, ComPtr<ID3DBlob>& blob) -> bool {
        ComPtr<ID3DBlob> errors;
        HRESULT hr = D3DCompile(src, len, name, nullptr, nullptr, "main", "cs_5_1", compileFlags, 0, &blob, &errors);
        if (FAILED(hr) && errors) OutputDebugStringA((const char*)errors->GetBufferPointer());
        return SUCCEEDED(hr);
    };

    using namespace shader_source;
    if (!compile(kVelloBlurHorizCS, sizeof(kVelloBlurHorizCS) - 1, "blur_horiz", blurHorizCS_)) return false;
    if (!compile(kVelloBlurVertCS, sizeof(kVelloBlurVertCS) - 1, "blur_vert", blurVertCS_)) return false;
    if (!compile(kVelloDownsampleCS, sizeof(kVelloDownsampleCS) - 1, "downsample", downsampleCS_)) return false;
    if (!compile(kVelloUpsampleCS, sizeof(kVelloUpsampleCS) - 1, "upsample", upsampleCS_)) return false;

    auto makePSO = [&](ID3DBlob* cs, ComPtr<ID3D12PipelineState>& pso) -> bool {
        D3D12_COMPUTE_PIPELINE_STATE_DESC d = {};
        d.pRootSignature = blurRootSig_.Get();
        d.CS = { cs->GetBufferPointer(), cs->GetBufferSize() };
        return SUCCEEDED(device_->CreateComputePipelineState(&d, IID_PPV_ARGS(&pso)));
    };

    if (!makePSO(blurHorizCS_.Get(), blurHorizPSO_)) return false;
    if (!makePSO(blurVertCS_.Get(), blurVertPSO_)) return false;
    if (!makePSO(downsampleCS_.Get(), downsamplePSO_)) return false;
    if (!makePSO(upsampleCS_.Get(), upsamplePSO_)) return false;

    return true;
}

// ============================================================================
// Gaussian Blur (Vello decimated algorithm)
// ============================================================================

bool D3D12VelloRenderer::ApplyGaussianBlur(
    ID3D12GraphicsCommandList* cmdList, float stdDeviation, uint32_t frameIndex)
{
    if (!outputTexture_ || stdDeviation <= 0.01f) return false;

    // Lazy-create blur pipelines
    if (!blurRootSig_) {
        if (!CreateBlurPipelines()) return false;
    }

    uint32_t fi = frameIndex % kMaxFrames;
    auto& srvHeap = computeSrvHeap_[fi];

    // Vello decimated blur plan: compute number of 2x downsample levels
    float variance = stdDeviation * stdDeviation;
    uint32_t nDecimations = 0;
    float remainingVariance = variance;
    while (remainingVariance > 4.0f && nDecimations < kMaxDecimations) {
        remainingVariance = (remainingVariance - 3.0f) * 0.25f;
        nDecimations++;
    }
    float remainingSigma = std::sqrt(std::max(remainingVariance, 0.0f));

    // Compute Gaussian kernel for the reduced blur
    static constexpr uint32_t kMaxKernelSize = 13;
    float kernel[16] = {};  // padded to 16 for CB alignment
    uint32_t kernelSize = 1;
    if (remainingSigma > 0.01f) {
        uint32_t radius = (uint32_t)std::ceil(3.0f * remainingSigma);
        kernelSize = std::min(1 + radius * 2, kMaxKernelSize);
        float denom = 2.0f * remainingSigma * remainingSigma;
        float sum = 0;
        float center = (float)(kernelSize / 2);
        for (uint32_t i = 0; i < kernelSize; i++) {
            float x = (float)i - center;
            kernel[i] = std::exp(-x * x / denom);
            sum += kernel[i];
        }
        float scale = 1.0f / sum;
        for (uint32_t i = 0; i < kernelSize; i++) kernel[i] *= scale;
    } else {
        kernel[0] = 1.0f;
        kernelSize = 1;
    }

    // Ensure temp texture
    if (!blurTempTexture_ || blurTempW_ != outputW_ || blurTempH_ != outputH_) {
        auto hp = MakeHeap(D3D12_HEAP_TYPE_DEFAULT);
        auto desc = MakeTex2D(DXGI_FORMAT_R8G8B8A8_UNORM, outputW_, outputH_,
                              D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
        if (FAILED(device_->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &desc,
                D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&blurTempTexture_))))
            return false;
        blurTempW_ = outputW_;
        blurTempH_ = outputH_;
    }

    // Ensure decimation textures
    {
        uint32_t w = outputW_, h = outputH_;
        for (uint32_t d = 0; d < nDecimations; d++) {
            w = (w + 1) / 2;
            h = (h + 1) / 2;
            if (!decimTextures_[d] || decimW_[d] != w || decimH_[d] != h) {
                auto hp = MakeHeap(D3D12_HEAP_TYPE_DEFAULT);
                auto desc = MakeTex2D(DXGI_FORMAT_R8G8B8A8_UNORM, w, h,
                                      D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
                if (FAILED(device_->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &desc,
                        D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&decimTextures_[d]))))
                    return false;
                decimW_[d] = w;
                decimH_[d] = h;
            }
        }
    }

    // Blur constants upload buffer (reuse frame upload pattern)
    struct BlurConstants {
        uint32_t texWidth, texHeight, kernelSizeOrSrcW, pad0;
        float kernel[16];
    };
    ComPtr<ID3D12Resource> blurCBUpload;
    if (!CreateBuffer(device_, 256, D3D12_HEAP_TYPE_UPLOAD,
                      D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ, blurCBUpload))
        return false;

    // Helper: create a descriptor heap with 1 SRV + 1 UAV for blur passes
    auto makeBlurHeap = [&]() -> ComPtr<ID3D12DescriptorHeap> {
        D3D12_DESCRIPTOR_HEAP_DESC hd = {};
        hd.NumDescriptors = 2;
        hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        ComPtr<ID3D12DescriptorHeap> heap;
        device_->CreateDescriptorHeap(&hd, IID_PPV_ARGS(&heap));
        return heap;
    };

    // Helper: dispatch a blur/downsample/upsample pass
    auto dispatchPass = [&](ID3D12Resource* srcTex, uint32_t srcW, uint32_t srcH,
                            ID3D12Resource* dstTex, uint32_t dstW, uint32_t dstH,
                            ID3D12PipelineState* pso,
                            uint32_t cbW, uint32_t cbH, uint32_t cbKernelOrSrcW, uint32_t cbPad,
                            const float* cbKernel) {
        // Upload constants
        BlurConstants bc = {};
        bc.texWidth = cbW; bc.texHeight = cbH;
        bc.kernelSizeOrSrcW = cbKernelOrSrcW; bc.pad0 = cbPad;
        if (cbKernel) memcpy(bc.kernel, cbKernel, 16 * sizeof(float));
        {
            void* mapped = nullptr;
            blurCBUpload->Map(0, nullptr, &mapped);
            memcpy(mapped, &bc, sizeof(bc));
            blurCBUpload->Unmap(0, nullptr);
        }

        auto heap = makeBlurHeap();
        if (!heap) return;
        auto cpuBase = heap->GetCPUDescriptorHandleForHeapStart();

        // SRV t0
        {
            D3D12_SHADER_RESOURCE_VIEW_DESC srv = {};
            srv.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            srv.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
            srv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            srv.Texture2D.MipLevels = 1;
            device_->CreateShaderResourceView(srcTex, &srv, cpuBase);
        }
        // UAV u0
        {
            D3D12_UNORDERED_ACCESS_VIEW_DESC uav = {};
            uav.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            uav.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
            auto h = cpuBase; h.ptr += srvDescSize_;
            device_->CreateUnorderedAccessView(dstTex, nullptr, &uav, h);
        }

        // Barriers: src → SRV, dst → UAV
        D3D12_RESOURCE_BARRIER barriers[2];
        int bc2 = 0;
        barriers[bc2++] = MakeBarrier(srcTex, D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        barriers[bc2++] = MakeBarrier(dstTex, D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        cmdList->ResourceBarrier(bc2, barriers);

        ID3D12DescriptorHeap* heaps[] = { heap.Get() };
        cmdList->SetDescriptorHeaps(1, heaps);
        cmdList->SetComputeRootSignature(blurRootSig_.Get());
        cmdList->SetComputeRootConstantBufferView(0, blurCBUpload->GetGPUVirtualAddress());
        auto gpuBase = heap->GetGPUDescriptorHandleForHeapStart();
        cmdList->SetComputeRootDescriptorTable(1, gpuBase);
        auto uavGpu = gpuBase; uavGpu.ptr += srvDescSize_;
        cmdList->SetComputeRootDescriptorTable(2, uavGpu);
        cmdList->SetPipelineState(pso);
        cmdList->Dispatch((dstW + 15) / 16, (dstH + 15) / 16, 1);

        // UAV barrier + transition back to COMMON
        D3D12_RESOURCE_BARRIER uavBarrier = {};
        uavBarrier.Type = D3D12_RESOURCE_BARRIER_TYPE_UAV;
        uavBarrier.UAV.pResource = dstTex;
        cmdList->ResourceBarrier(1, &uavBarrier);

        D3D12_RESOURCE_BARRIER resetB[2];
        int rc = 0;
        resetB[rc++] = MakeBarrier(srcTex, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
        resetB[rc++] = MakeBarrier(dstTex, D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COMMON);
        cmdList->ResourceBarrier(rc, resetB);
    };

    // Step 1: Downsample chain (output → decim[0] → decim[1] → ...)
    ID3D12Resource* currentSrc = outputTexture_.Get();
    uint32_t curW = outputW_, curH = outputH_;
    for (uint32_t d = 0; d < nDecimations; d++) {
        dispatchPass(currentSrc, curW, curH,
                     decimTextures_[d].Get(), decimW_[d], decimH_[d],
                     downsamplePSO_.Get(),
                     decimW_[d], decimH_[d], curW, curH, nullptr);
        currentSrc = decimTextures_[d].Get();
        curW = decimW_[d]; curH = decimH_[d];
    }

    // Step 2: Separable Gaussian blur at decimated resolution
    // Horizontal pass: currentSrc → blurTemp (resized if needed)
    if (kernelSize > 1) {
        // For decimated resolution, we need appropriately sized temp
        // Use blurTempTexture_ if same size, otherwise create inline
        ComPtr<ID3D12Resource> hBlurTemp;
        if (curW == blurTempW_ && curH == blurTempH_) {
            hBlurTemp = blurTempTexture_;
        } else {
            auto hp = MakeHeap(D3D12_HEAP_TYPE_DEFAULT);
            auto desc = MakeTex2D(DXGI_FORMAT_R8G8B8A8_UNORM, curW, curH,
                                  D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
            device_->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &desc,
                D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&hBlurTemp));
        }

        if (hBlurTemp) {
            // Horizontal pass
            dispatchPass(currentSrc, curW, curH,
                         hBlurTemp.Get(), curW, curH,
                         blurHorizPSO_.Get(),
                         curW, curH, kernelSize, 0, kernel);

            // Vertical pass: hBlurTemp → currentSrc (write back)
            dispatchPass(hBlurTemp.Get(), curW, curH,
                         currentSrc, curW, curH,
                         blurVertPSO_.Get(),
                         curW, curH, kernelSize, 0, kernel);
        }
    }

    // Step 3: Upsample chain (decim[n-1] → ... → decim[0] → output)
    for (int32_t d = (int32_t)nDecimations - 1; d >= 0; d--) {
        ID3D12Resource* dst;
        uint32_t dstW, dstH;
        if (d == 0) {
            dst = outputTexture_.Get();
            dstW = outputW_; dstH = outputH_;
        } else {
            dst = decimTextures_[d - 1].Get();
            dstW = decimW_[d - 1]; dstH = decimH_[d - 1];
        }
        dispatchPass(decimTextures_[d].Get(), decimW_[d], decimH_[d],
                     dst, dstW, dstH,
                     upsamplePSO_.Get(),
                     dstW, dstH, decimW_[d], decimH_[d], nullptr);
    }

    return true;
}

// ============================================================================
// Drop Shadow (offset + blur + color tint)
// ============================================================================

bool D3D12VelloRenderer::ApplyDropShadow(
    ID3D12GraphicsCommandList* cmdList,
    float dx, float dy, float stdDeviation,
    float r, float g, float b, float a,
    uint32_t frameIndex)
{
    if (!outputTexture_) return false;

    // Step 1: Create a copy of the output texture with offset applied
    // For now, we render the shadow as a separate Vello pass with offset transform.
    // The shadow uses the same geometry but with:
    //   - Color replaced by shadow color
    //   - Transform offset by (dx, dy)
    //   - Gaussian blur applied

    // Simple approach: apply blur to the existing output, then the caller
    // composites shadow under original content.
    // This requires the caller to manage the compositing order.

    // Apply Gaussian blur if sigma > 0
    if (stdDeviation > 0.01f) {
        if (!ApplyGaussianBlur(cmdList, stdDeviation, frameIndex))
            return false;
    }

    return true;
}

// ============================================================================
// Filter Pipeline Creation (ColorMatrix, Offset, Morphology)
// ============================================================================

bool D3D12VelloRenderer::CreateFilterPipelines()
{
    if (filterPSOsCreated_) return true;
    // Reuse blur root signature (CBV + SRV t0 + UAV u0)
    if (!blurRootSig_ && !CreateBlurPipelines()) return false;

    UINT flags = 0;
#ifdef _DEBUG
    flags = D3DCOMPILE_DEBUG | D3DCOMPILE_SKIP_OPTIMIZATION;
#else
    flags = D3DCOMPILE_OPTIMIZATION_LEVEL3;
#endif
    auto compile = [&](const char* src, size_t len, const char* name, ComPtr<ID3DBlob>& blob) -> bool {
        ComPtr<ID3DBlob> errors;
        HRESULT hr = D3DCompile(src, len, name, nullptr, nullptr, "main", "cs_5_1", flags, 0, &blob, &errors);
        if (FAILED(hr) && errors) OutputDebugStringA((const char*)errors->GetBufferPointer());
        return SUCCEEDED(hr);
    };
    using namespace shader_source;
    if (!compile(kVelloColorMatrixCS, sizeof(kVelloColorMatrixCS)-1, "colormatrix", colorMatrixCS_)) return false;
    if (!compile(kVelloOffsetCS, sizeof(kVelloOffsetCS)-1, "offset", offsetCS_)) return false;
    if (!compile(kVelloMorphologyCS, sizeof(kVelloMorphologyCS)-1, "morphology", morphologyCS_)) return false;

    auto makePSO = [&](ID3DBlob* cs, ComPtr<ID3D12PipelineState>& pso) -> bool {
        D3D12_COMPUTE_PIPELINE_STATE_DESC d = {};
        d.pRootSignature = blurRootSig_.Get();
        d.CS = { cs->GetBufferPointer(), cs->GetBufferSize() };
        return SUCCEEDED(device_->CreateComputePipelineState(&d, IID_PPV_ARGS(&pso)));
    };
    if (!makePSO(colorMatrixCS_.Get(), colorMatrixPSO_)) return false;
    if (!makePSO(offsetCS_.Get(), offsetPSO_)) return false;
    if (!makePSO(morphologyCS_.Get(), morphologyPSO_)) return false;

    filterPSOsCreated_ = true;
    return true;
}

// ============================================================================
// ColorMatrix Filter
// ============================================================================

bool D3D12VelloRenderer::ApplyColorMatrix(
    ID3D12GraphicsCommandList* cmdList, const float* matrix, uint32_t frameIndex)
{
    if (!outputTexture_ || !matrix) return false;
    if (!CreateFilterPipelines()) return false;

    if (!blurTempTexture_ || blurTempW_ != outputW_ || blurTempH_ != outputH_) {
        auto hp = MakeHeap(D3D12_HEAP_TYPE_DEFAULT);
        auto desc = MakeTex2D(DXGI_FORMAT_R8G8B8A8_UNORM, outputW_, outputH_,
                              D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
        device_->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &desc,
            D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&blurTempTexture_));
        blurTempW_ = outputW_; blurTempH_ = outputH_;
    }

    // Upload constants
    struct { uint32_t w, h, p0, p1; float rows[20]; } cb;
    cb.w = outputW_; cb.h = outputH_; cb.p0 = cb.p1 = 0;
    memcpy(cb.rows, matrix, 20 * sizeof(float));

    ComPtr<ID3D12Resource> cbUpload;
    CreateBuffer(device_, 256, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
                 D3D12_RESOURCE_STATE_GENERIC_READ, cbUpload);
    { void* m = nullptr; cbUpload->Map(0,nullptr,&m); memcpy(m,&cb,sizeof(cb)); cbUpload->Unmap(0,nullptr); }

    auto heap = [&]() -> ComPtr<ID3D12DescriptorHeap> {
        D3D12_DESCRIPTOR_HEAP_DESC hd = {}; hd.NumDescriptors = 2;
        hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        ComPtr<ID3D12DescriptorHeap> h; device_->CreateDescriptorHeap(&hd, IID_PPV_ARGS(&h)); return h;
    }();

    auto cpuB = heap->GetCPUDescriptorHandleForHeapStart();
    { D3D12_SHADER_RESOURCE_VIEW_DESC s = {}; s.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
      s.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D; s.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
      s.Texture2D.MipLevels = 1; device_->CreateShaderResourceView(outputTexture_.Get(), &s, cpuB); }
    { D3D12_UNORDERED_ACCESS_VIEW_DESC u = {}; u.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
      u.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
      auto h2 = cpuB; h2.ptr += srvDescSize_;
      device_->CreateUnorderedAccessView(blurTempTexture_.Get(), nullptr, &u, h2); }

    D3D12_RESOURCE_BARRIER b[2];
    b[0] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    b[1] = MakeBarrier(blurTempTexture_.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
    cmdList->ResourceBarrier(2, b);

    ID3D12DescriptorHeap* heaps[] = { heap.Get() };
    cmdList->SetDescriptorHeaps(1, heaps);
    cmdList->SetComputeRootSignature(blurRootSig_.Get());
    cmdList->SetComputeRootConstantBufferView(0, cbUpload->GetGPUVirtualAddress());
    auto gpuB = heap->GetGPUDescriptorHandleForHeapStart();
    cmdList->SetComputeRootDescriptorTable(1, gpuB);
    auto uavG = gpuB; uavG.ptr += srvDescSize_;
    cmdList->SetComputeRootDescriptorTable(2, uavG);
    cmdList->SetPipelineState(colorMatrixPSO_.Get());
    cmdList->Dispatch((outputW_+15)/16, (outputH_+15)/16, 1);

    D3D12_RESOURCE_BARRIER uavB = {}; uavB.Type = D3D12_RESOURCE_BARRIER_TYPE_UAV;
    uavB.UAV.pResource = blurTempTexture_.Get(); cmdList->ResourceBarrier(1, &uavB);

    // Copy result back to output
    b[0] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COPY_DEST);
    b[1] = MakeBarrier(blurTempTexture_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COPY_SOURCE);
    cmdList->ResourceBarrier(2, b);
    cmdList->CopyResource(outputTexture_.Get(), blurTempTexture_.Get());
    b[0] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON);
    b[1] = MakeBarrier(blurTempTexture_.Get(), D3D12_RESOURCE_STATE_COPY_SOURCE, D3D12_RESOURCE_STATE_COMMON);
    cmdList->ResourceBarrier(2, b);

    return true;
}

// ============================================================================
// CSS Filter Functions → ColorMatrix
// ============================================================================

bool D3D12VelloRenderer::ApplyBrightness(ID3D12GraphicsCommandList* cl, float v, uint32_t fi) {
    float m[20] = { v,0,0,0, 0,v,0,0, 0,0,v,0, 0,0,0,1, 0,0,0,0 };
    return ApplyColorMatrix(cl, m, fi);
}

bool D3D12VelloRenderer::ApplyContrast(ID3D12GraphicsCommandList* cl, float v, uint32_t fi) {
    float off = (1.0f - v) * 0.5f;
    float m[20] = { v,0,0,0, 0,v,0,0, 0,0,v,0, 0,0,0,1, off,off,off,0 };
    return ApplyColorMatrix(cl, m, fi);
}

bool D3D12VelloRenderer::ApplyGrayscale(ID3D12GraphicsCommandList* cl, float v, uint32_t fi) {
    float t = 1.0f - v;
    float m[20] = {
        0.2126f+0.7874f*t, 0.7152f-0.7152f*t, 0.0722f-0.0722f*t, 0,
        0.2126f-0.2126f*t, 0.7152f+0.2848f*t, 0.0722f-0.0722f*t, 0,
        0.2126f-0.2126f*t, 0.7152f-0.7152f*t, 0.0722f+0.9278f*t, 0,
        0,0,0,1, 0,0,0,0
    };
    return ApplyColorMatrix(cl, m, fi);
}

bool D3D12VelloRenderer::ApplyHueRotate(ID3D12GraphicsCommandList* cl, float deg, uint32_t fi) {
    float rad = deg * 3.14159265f / 180.0f;
    float c = std::cos(rad), s = std::sin(rad);
    float m[20] = {
        0.213f+0.787f*c-0.213f*s, 0.715f-0.715f*c-0.715f*s, 0.072f-0.072f*c+0.928f*s, 0,
        0.213f-0.213f*c+0.143f*s, 0.715f+0.285f*c+0.140f*s, 0.072f-0.072f*c-0.283f*s, 0,
        0.213f-0.213f*c-0.787f*s, 0.715f-0.715f*c+0.715f*s, 0.072f+0.928f*c+0.072f*s, 0,
        0,0,0,1, 0,0,0,0
    };
    return ApplyColorMatrix(cl, m, fi);
}

bool D3D12VelloRenderer::ApplyInvert(ID3D12GraphicsCommandList* cl, float v, uint32_t fi) {
    float d = 1.0f - 2.0f * v;
    float m[20] = { d,0,0,0, 0,d,0,0, 0,0,d,0, 0,0,0,1, v,v,v,0 };
    return ApplyColorMatrix(cl, m, fi);
}

bool D3D12VelloRenderer::ApplyOpacityFilter(ID3D12GraphicsCommandList* cl, float v, uint32_t fi) {
    float m[20] = { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,v, 0,0,0,0 };
    return ApplyColorMatrix(cl, m, fi);
}

bool D3D12VelloRenderer::ApplySaturate(ID3D12GraphicsCommandList* cl, float v, uint32_t fi) {
    float m[20] = {
        0.2126f+0.7874f*v, 0.7152f-0.7152f*v, 0.0722f-0.0722f*v, 0,
        0.2126f-0.2126f*v, 0.7152f+0.2848f*v, 0.0722f-0.0722f*v, 0,
        0.2126f-0.2126f*v, 0.7152f-0.7152f*v, 0.0722f+0.9278f*v, 0,
        0,0,0,1, 0,0,0,0
    };
    return ApplyColorMatrix(cl, m, fi);
}

bool D3D12VelloRenderer::ApplySepia(ID3D12GraphicsCommandList* cl, float v, uint32_t fi) {
    float t = 1.0f - v;
    float m[20] = {
        0.393f+0.607f*t, 0.769f-0.769f*t, 0.189f-0.189f*t, 0,
        0.349f-0.349f*t, 0.686f+0.314f*t, 0.168f-0.168f*t, 0,
        0.272f-0.272f*t, 0.534f-0.534f*t, 0.131f+0.869f*t, 0,
        0,0,0,1, 0,0,0,0
    };
    return ApplyColorMatrix(cl, m, fi);
}

// ============================================================================
// Offset Filter
// ============================================================================

bool D3D12VelloRenderer::ApplyOffset(
    ID3D12GraphicsCommandList* cmdList, float dx, float dy, uint32_t frameIndex)
{
    if (!outputTexture_) return false;
    if (!CreateFilterPipelines()) return false;
    if (std::abs(dx) < 0.5f && std::abs(dy) < 0.5f) return true;  // no-op

    if (!blurTempTexture_ || blurTempW_ != outputW_ || blurTempH_ != outputH_) {
        auto hp = MakeHeap(D3D12_HEAP_TYPE_DEFAULT);
        auto desc = MakeTex2D(DXGI_FORMAT_R8G8B8A8_UNORM, outputW_, outputH_,
                              D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
        device_->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &desc,
            D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&blurTempTexture_));
        blurTempW_ = outputW_; blurTempH_ = outputH_;
    }

    struct { uint32_t w, h; int32_t ox, oy; } cb;
    cb.w = outputW_; cb.h = outputH_;
    cb.ox = (int32_t)std::round(dx); cb.oy = (int32_t)std::round(dy);

    ComPtr<ID3D12Resource> cbUp;
    CreateBuffer(device_, 256, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
                 D3D12_RESOURCE_STATE_GENERIC_READ, cbUp);
    { void* m = nullptr; cbUp->Map(0,nullptr,&m); memcpy(m,&cb,sizeof(cb)); cbUp->Unmap(0,nullptr); }

    auto heap = [&]() -> ComPtr<ID3D12DescriptorHeap> {
        D3D12_DESCRIPTOR_HEAP_DESC hd = {}; hd.NumDescriptors = 2;
        hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        ComPtr<ID3D12DescriptorHeap> h; device_->CreateDescriptorHeap(&hd, IID_PPV_ARGS(&h)); return h;
    }();

    auto cpuB = heap->GetCPUDescriptorHandleForHeapStart();
    { D3D12_SHADER_RESOURCE_VIEW_DESC s = {}; s.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
      s.ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D; s.Shader4ComponentMapping=D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
      s.Texture2D.MipLevels=1; device_->CreateShaderResourceView(outputTexture_.Get(),&s,cpuB); }
    { D3D12_UNORDERED_ACCESS_VIEW_DESC u={}; u.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
      u.ViewDimension=D3D12_UAV_DIMENSION_TEXTURE2D;
      auto h2=cpuB; h2.ptr+=srvDescSize_;
      device_->CreateUnorderedAccessView(blurTempTexture_.Get(),nullptr,&u,h2); }

    D3D12_RESOURCE_BARRIER b[2];
    b[0] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    b[1] = MakeBarrier(blurTempTexture_.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
    cmdList->ResourceBarrier(2, b);

    ID3D12DescriptorHeap* heaps[] = { heap.Get() };
    cmdList->SetDescriptorHeaps(1, heaps);
    cmdList->SetComputeRootSignature(blurRootSig_.Get());
    cmdList->SetComputeRootConstantBufferView(0, cbUp->GetGPUVirtualAddress());
    auto gpuB = heap->GetGPUDescriptorHandleForHeapStart();
    cmdList->SetComputeRootDescriptorTable(1, gpuB);
    auto uG = gpuB; uG.ptr+=srvDescSize_;
    cmdList->SetComputeRootDescriptorTable(2, uG);
    cmdList->SetPipelineState(offsetPSO_.Get());
    cmdList->Dispatch((outputW_+15)/16, (outputH_+15)/16, 1);

    D3D12_RESOURCE_BARRIER uavB={}; uavB.Type=D3D12_RESOURCE_BARRIER_TYPE_UAV;
    uavB.UAV.pResource=blurTempTexture_.Get(); cmdList->ResourceBarrier(1,&uavB);

    b[0] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COPY_DEST);
    b[1] = MakeBarrier(blurTempTexture_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COPY_SOURCE);
    cmdList->ResourceBarrier(2, b);
    cmdList->CopyResource(outputTexture_.Get(), blurTempTexture_.Get());
    b[0] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON);
    b[1] = MakeBarrier(blurTempTexture_.Get(), D3D12_RESOURCE_STATE_COPY_SOURCE, D3D12_RESOURCE_STATE_COMMON);
    cmdList->ResourceBarrier(2, b);

    return true;
}

// ============================================================================
// Morphology Filter (Dilate/Erode)
// ============================================================================

bool D3D12VelloRenderer::ApplyMorphology(
    ID3D12GraphicsCommandList* cmdList, float radiusX, float radiusY, bool isDilate,
    uint32_t frameIndex)
{
    if (!outputTexture_) return false;
    if (!CreateFilterPipelines()) return false;
    int rx = (int)std::ceil(radiusX), ry = (int)std::ceil(radiusY);
    if (rx <= 0 && ry <= 0) return true;

    if (!blurTempTexture_ || blurTempW_ != outputW_ || blurTempH_ != outputH_) {
        auto hp = MakeHeap(D3D12_HEAP_TYPE_DEFAULT);
        auto desc = MakeTex2D(DXGI_FORMAT_R8G8B8A8_UNORM, outputW_, outputH_,
                              D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
        device_->CreateCommittedResource(&hp, D3D12_HEAP_FLAG_NONE, &desc,
            D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&blurTempTexture_));
        blurTempW_ = outputW_; blurTempH_ = outputH_;
    }

    struct { uint32_t w, h; int32_t rx_, ry_; uint32_t isDilate_, p0, p1, p2; } cb;
    cb.w = outputW_; cb.h = outputH_;
    cb.rx_ = rx; cb.ry_ = ry; cb.isDilate_ = isDilate ? 1 : 0;
    cb.p0 = cb.p1 = cb.p2 = 0;

    ComPtr<ID3D12Resource> cbUp;
    CreateBuffer(device_, 256, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
                 D3D12_RESOURCE_STATE_GENERIC_READ, cbUp);
    { void* m = nullptr; cbUp->Map(0,nullptr,&m); memcpy(m,&cb,sizeof(cb)); cbUp->Unmap(0,nullptr); }

    auto heap = [&]() -> ComPtr<ID3D12DescriptorHeap> {
        D3D12_DESCRIPTOR_HEAP_DESC hd = {}; hd.NumDescriptors = 2;
        hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        ComPtr<ID3D12DescriptorHeap> h; device_->CreateDescriptorHeap(&hd, IID_PPV_ARGS(&h)); return h;
    }();

    auto cpuB = heap->GetCPUDescriptorHandleForHeapStart();
    { D3D12_SHADER_RESOURCE_VIEW_DESC s={}; s.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
      s.ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D; s.Shader4ComponentMapping=D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
      s.Texture2D.MipLevels=1; device_->CreateShaderResourceView(outputTexture_.Get(),&s,cpuB); }
    { D3D12_UNORDERED_ACCESS_VIEW_DESC u={}; u.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
      u.ViewDimension=D3D12_UAV_DIMENSION_TEXTURE2D;
      auto h2=cpuB; h2.ptr+=srvDescSize_;
      device_->CreateUnorderedAccessView(blurTempTexture_.Get(),nullptr,&u,h2); }

    D3D12_RESOURCE_BARRIER b[2];
    b[0] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    b[1] = MakeBarrier(blurTempTexture_.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
    cmdList->ResourceBarrier(2, b);

    ID3D12DescriptorHeap* heaps[] = { heap.Get() };
    cmdList->SetDescriptorHeaps(1, heaps);
    cmdList->SetComputeRootSignature(blurRootSig_.Get());
    cmdList->SetComputeRootConstantBufferView(0, cbUp->GetGPUVirtualAddress());
    auto gpuB = heap->GetGPUDescriptorHandleForHeapStart();
    cmdList->SetComputeRootDescriptorTable(1, gpuB);
    auto uG = gpuB; uG.ptr+=srvDescSize_;
    cmdList->SetComputeRootDescriptorTable(2, uG);
    cmdList->SetPipelineState(morphologyPSO_.Get());
    cmdList->Dispatch((outputW_+15)/16, (outputH_+15)/16, 1);

    D3D12_RESOURCE_BARRIER uavB={}; uavB.Type=D3D12_RESOURCE_BARRIER_TYPE_UAV;
    uavB.UAV.pResource=blurTempTexture_.Get(); cmdList->ResourceBarrier(1,&uavB);

    b[0] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COPY_DEST);
    b[1] = MakeBarrier(blurTempTexture_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COPY_SOURCE);
    cmdList->ResourceBarrier(2, b);
    cmdList->CopyResource(outputTexture_.Get(), blurTempTexture_.Get());
    b[0] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON);
    b[1] = MakeBarrier(blurTempTexture_.Get(), D3D12_RESOURCE_STATE_COPY_SOURCE, D3D12_RESOURCE_STATE_COMMON);
    cmdList->ResourceBarrier(2, b);

    return true;
}

// ============================================================================
// FilterGraph Execution — chain of filter primitives
// ============================================================================

bool D3D12VelloRenderer::ExecuteFilterGraph(
    ID3D12GraphicsCommandList* cmdList, const VelloFilterGraph& graph, uint32_t frameIndex)
{
    if (!outputTexture_ || graph.IsEmpty()) return false;

    for (auto& prim : graph.primitives) {
        bool ok = false;
        switch (prim.type) {
        case VelloFilterType::GaussianBlur:
            ok = ApplyGaussianBlur(cmdList, prim.params[0], frameIndex);
            break;
        case VelloFilterType::DropShadow:
            ok = ApplyDropShadow(cmdList,
                prim.params[0], prim.params[1], prim.params[2],
                prim.params[3], prim.params[4], prim.params[5], prim.params[6],
                frameIndex);
            break;
        case VelloFilterType::ColorMatrix:
            ok = ApplyColorMatrix(cmdList, prim.params, frameIndex);
            break;
        case VelloFilterType::Offset:
            ok = ApplyOffset(cmdList, prim.params[0], prim.params[1], frameIndex);
            break;
        case VelloFilterType::Morphology:
            ok = ApplyMorphology(cmdList, prim.params[0], prim.params[1],
                                 prim.params[2] > 0.5f, frameIndex);
            break;
        case VelloFilterType::Brightness:
            ok = ApplyBrightness(cmdList, prim.params[0], frameIndex);
            break;
        case VelloFilterType::Contrast:
            ok = ApplyContrast(cmdList, prim.params[0], frameIndex);
            break;
        case VelloFilterType::Grayscale:
            ok = ApplyGrayscale(cmdList, prim.params[0], frameIndex);
            break;
        case VelloFilterType::HueRotate:
            ok = ApplyHueRotate(cmdList, prim.params[0], frameIndex);
            break;
        case VelloFilterType::Invert:
            ok = ApplyInvert(cmdList, prim.params[0], frameIndex);
            break;
        case VelloFilterType::Opacity:
            ok = ApplyOpacityFilter(cmdList, prim.params[0], frameIndex);
            break;
        case VelloFilterType::Saturate:
            ok = ApplySaturate(cmdList, prim.params[0], frameIndex);
            break;
        case VelloFilterType::Sepia:
            ok = ApplySepia(cmdList, prim.params[0], frameIndex);
            break;
        case VelloFilterType::Flood:
            // Flood fills entire output with a color — implemented as ColorMatrix
            // with zero matrix + offset color
            {
                float m[20] = {0,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,0,
                               prim.params[0], prim.params[1], prim.params[2], prim.params[3]};
                ok = ApplyColorMatrix(cmdList, m, frameIndex);
            }
            break;
        }
        if (!ok) return false;
    }
    return true;
}

// ============================================================================
// RenderGraph Execution — process nodes in dependency order
// ============================================================================

bool D3D12VelloRenderer::ExecuteRenderGraph(
    ID3D12GraphicsCommandList* cmdList, const VelloRenderGraph& graph, uint32_t frameIndex)
{
    if (!graph.hasFilters) return true;  // no filter work to do

    // Execute nodes in pre-computed order (children before parents)
    for (uint32_t nodeId : graph.executionOrder) {
        if (nodeId >= (uint32_t)graph.nodes.size()) continue;
        auto& node = graph.nodes[nodeId];

        if (node.kind == VelloRenderNodeKind::FilterLayer) {
            if (!ExecuteFilterGraph(cmdList, node.filterGraph, frameIndex))
                return false;
        }
        // RootLayer nodes don't need filter processing
    }

    // Process root node last (if it has filters, which is unusual)
    if (graph.rootNodeId < (uint32_t)graph.nodes.size()) {
        auto& root = graph.nodes[graph.rootNodeId];
        if (root.kind == VelloRenderNodeKind::FilterLayer) {
            if (!ExecuteFilterGraph(cmdList, root.filterGraph, frameIndex))
                return false;
        }
    }

    return true;
}

// ============================================================================
// COLR Color Font Rendering
// ============================================================================

bool D3D12VelloRenderer::RenderColorGlyph(
    IDWriteFontFace* fontFace, uint16_t glyphId,
    float fontSize, float x, float y,
    float r, float g, float b, float a,
    float m11, float m12, float m21, float m22, float dx, float dy)
{
    if (!fontFace || !initialized_) return false;

    // Query IDWriteFontFace4 for COLR support
    ComPtr<IDWriteFontFace4> fontFace4;
    if (FAILED(fontFace->QueryInterface(IID_PPV_ARGS(&fontFace4)))) return false;

    // Check if this glyph has color layers (COLR table)
    DWRITE_GLYPH_IMAGE_FORMATS formats = DWRITE_GLYPH_IMAGE_FORMATS_NONE;
    if (FAILED(fontFace4->GetGlyphImageFormats(glyphId, 0, UINT32_MAX, &formats)))
        return false;
    if (!(formats & DWRITE_GLYPH_IMAGE_FORMATS_COLR)) return false;

    // Get the color glyph layers via IDWriteColorGlyphRunEnumerator1
    // Need IDWriteFactory4 — obtain from the font face's factory
    ComPtr<IDWriteFactory> factory;
    // DirectWrite doesn't provide a direct way to get factory from fontFace.
    // We'll use the global factory created during backend initialization.
    // For now, implement basic COLR v0 layer rendering by iterating COLR layers directly.

    // Use IDWriteFontFace4::GetGlyphImageData for COLR
    // Alternative: decompose glyph outlines per layer using GetColorPaletteCount + GetPaletteEntries

    uint32_t paletteCount = fontFace4->GetColorPaletteCount();
    if (paletteCount == 0) return false;

    uint32_t entryCount = fontFace4->GetPaletteEntryCount();
    if (entryCount == 0) return false;

    std::vector<DWRITE_COLOR_F> palette(entryCount);
    if (FAILED(fontFace4->GetPaletteEntries(0, 0, entryCount, palette.data()))) return false;

    // Get glyph outline for each color layer
    // COLR v0: each layer is a glyph ID + palette index pair
    // Use TranslateColorGlyphRun to enumerate layers

    DWRITE_GLYPH_RUN glyphRun = {};
    glyphRun.fontFace = fontFace;
    glyphRun.fontEmSize = fontSize;
    glyphRun.glyphCount = 1;
    glyphRun.glyphIndices = &glyphId;
    float advance = 0;
    glyphRun.glyphAdvances = &advance;
    DWRITE_GLYPH_OFFSET offset = {};
    glyphRun.glyphOffsets = &offset;

    // Get glyph outline as path geometry via IDWriteFontFace::GetGlyphRunOutline
    // For each COLR layer, get the outline and encode it as a Vello fill path

    // Basic implementation: render the glyph outline in the text color
    // Full COLR implementation would iterate color layers

    // For now, indicate this is a color glyph but return false to trigger fallback.
    // Full COLR implementation requires:
    // 1. IDWriteFactory4::TranslateColorGlyphRun to enumerate color layers
    // 2. Custom IDWriteGeometrySink to convert DWrite paths to Vello path commands
    // 3. Per-layer Vello fill with the palette color
    // This infrastructure is in place; actual layer rendering will be added
    // when a custom DWrite-to-Vello geometry sink is implemented.

    return false;  // Fallback to grayscale glyph rendering
    // TODO: Full COLR layer enumeration + per-layer Vello rendering
}

// ============================================================================
// Vello GPU Pipeline — Full implementation
// ============================================================================

void D3D12VelloRenderer::ComputeDrawMonoids(std::vector<VelloDrawMonoid>& out)
{
    out.resize(drawTags_.size());
    uint32_t path_ix = 0, clip_ix = 0;
    for (size_t i = 0; i < drawTags_.size(); i++) {
        out[i].path_ix = path_ix;
        out[i].clip_ix = clip_ix;
        out[i].scene_offset = (uint32_t)i;
        out[i].info_offset = (uint32_t)i;

        auto& dt = drawTags_[i];
        if (dt.tag == kDrawTagFill || dt.tag == kDrawTagBeginClip || dt.tag == kDrawTagBlurRect) {
            path_ix++;
        }
        // Vello: clip_ix increments for BOTH BeginClip and EndClip.
        // Each clip operation (begin or end) gets its own clip_bbox entry.
        // Draw objects between BeginClip[N] and EndClip[N] have clip_ix = N+1,
        // so binning reads clip_bboxes[clip_ix - 1] = the BeginClip's bbox.
        if (dt.tag == kDrawTagBeginClip || dt.tag == kDrawTagEndClip) {
            clip_ix++;
        }
    }
}

bool D3D12VelloRenderer::EnsureGPUBuffers(uint32_t numPaths, uint32_t numSegs, uint32_t numDrawObjs)
{
    auto uavFlags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
    auto state = D3D12_RESOURCE_STATE_COMMON;

    auto ensure = [&](ComPtr<ID3D12Resource>& buf, uint32_t& cap, uint32_t needed, uint32_t elemSize) {
        if (needed > cap || !buf) {
            cap = std::max(needed * 2, 256u);
            return CreateBuffer(device_, (UINT64)cap * elemSize, D3D12_HEAP_TYPE_DEFAULT, uavFlags, state, buf);
        }
        return true;
    };

    // BumpAllocators: 32 bytes fixed
    if (!bumpBuffer_)
        if (!CreateBuffer(device_, 256, D3D12_HEAP_TYPE_DEFAULT, uavFlags, state, bumpBuffer_)) return false;

    if (!ensure(pathBboxBuffer_, pathBboxCapacity_, numPaths, sizeof(VelloPathBbox))) return false;
    if (!ensure(lineSoupBuffer_, lineSoupCapacity_, numSegs * 64, sizeof(LineSoup))) return false;
    if (!ensure(drawMonoidBuffer_, drawMonoidCapacity_, numDrawObjs, sizeof(VelloDrawMonoid))) return false;
    if (!ensure(intersectedBboxBuffer_, intersectedBboxCapacity_, numDrawObjs, 16)) return false;

    // Clip pipeline buffers (GPU clip_reduce/clip_leaf)
    uint32_t totalClips = 0;
    for (auto& dt : drawTags_) {
        if (dt.tag == kDrawTagBeginClip || dt.tag == kDrawTagEndClip) totalClips++;
    }
    if (totalClips > 0) {
        if (!ensure(clipInpBuffer_, clipInpCapacity_, totalClips, sizeof(VelloClipInp))) return false;
        uint32_t clipWgs = (totalClips + 255) / 256;
        if (!ensure(clipBicBuffer_, clipBicCapacity_, std::max(clipWgs, 1u), sizeof(VelloClipBic))) return false;
        if (!ensure(clipElBuffer_, clipElCapacity_, totalClips, sizeof(VelloClipEl))) return false;
    }
    // clipBboxBuffer_ always needed (binning reads it even if 0 clips, via dummy)
    uint32_t clipBboxNeeded = std::max(totalClips, 1u);
    if (clipBboxNeeded > (uint32_t)(clipBboxBuffer_ ? clipBboxBuffer_->GetDesc().Width / 16 : 0) || !clipBboxBuffer_) {
        uint32_t cap = clipBboxNeeded * 2;
        if (!CreateBuffer(device_, cap * 16, D3D12_HEAP_TYPE_DEFAULT, uavFlags, state, clipBboxBuffer_)) return false;
    }

    uint32_t numBins = ((tilesX_ + 15) / 16) * ((tilesY_ + 15) / 16);
    if (!ensure(binHeaderBuffer_, binHeaderCapacity_, std::max(numBins * 256, 256u), sizeof(VelloBinHeader))) return false;
    if (!ensure(binDataBuffer_, binDataCapacity_, 1u << 18, 4)) return false;
    if (!ensure(velloPathBuffer_, velloPathCapacity_, numDrawObjs, sizeof(VelloPath))) return false;
    if (!ensure(velloTileBuffer_, velloTileCapacity_, 1u << 21, sizeof(VelloTile))) return false;
    if (!ensure(segCountBuffer_, segCountCapacity_, 1u << 21, sizeof(VelloSegmentCount))) return false;
    if (!ensure(velloSegmentBuffer_, velloSegmentCapacity_, 1u << 21, sizeof(VelloSegment))) return false;
    if (!ensure(velloPtclBuffer_, velloPtclCapacity_, 1u << 23, 4)) return false;
    if (!ensure(blendSpillBuffer_, blendSpillCapacity_, 1u << 20, 4)) return false;

    // Indirect dispatch buffers (12 bytes each)
    if (!indirectBuffer1_)
        if (!CreateBuffer(device_, 256, D3D12_HEAP_TYPE_DEFAULT, uavFlags, state, indirectBuffer1_)) return false;
    if (!indirectBuffer2_)
        if (!CreateBuffer(device_, 256, D3D12_HEAP_TYPE_DEFAULT, uavFlags, state, indirectBuffer2_)) return false;

    // Config upload buffer
    if (!configUpload_)
        if (!CreateBuffer(device_, 256, D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
                          D3D12_RESOURCE_STATE_GENERIC_READ, configUpload_)) return false;

    return true;
}

bool D3D12VelloRenderer::CreateGPUPipeline()
{
    if (gpuPipelineCreated_) return true;

    // Pipeline created from pre-compiled bytecode embedded in d3d12_vello_bytecode.h

    // Root signature: [0] CBV b0, [1] SRV table (t0-t7), [2] UAV table (u0-u7)
    {
        D3D12_DESCRIPTOR_RANGE srvRange = {};
        srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
        srvRange.NumDescriptors = 8;
        srvRange.BaseShaderRegister = 0;

        D3D12_DESCRIPTOR_RANGE uavRange = {};
        uavRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV;
        uavRange.NumDescriptors = 8;
        uavRange.BaseShaderRegister = 0;

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

        // Add static sampler for image atlas
        D3D12_STATIC_SAMPLER_DESC sampler = {};
        sampler.Filter = D3D12_FILTER_MIN_MAG_MIP_LINEAR;
        sampler.AddressU = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
        sampler.AddressV = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
        sampler.AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
        sampler.ShaderRegister = 0;
        sampler.ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

        D3D12_ROOT_SIGNATURE_DESC rsDesc = {};
        rsDesc.NumParameters = 3;
        rsDesc.pParameters = params;
        rsDesc.NumStaticSamplers = 1;
        rsDesc.pStaticSamplers = &sampler;

        ComPtr<ID3DBlob> sig, err;
        if (FAILED(D3D12SerializeRootSignature(&rsDesc, D3D_ROOT_SIGNATURE_VERSION_1_0, &sig, &err))) {
            OutputDebugStringA("[Vello GPU] Root signature serialization failed\n");
            return false;
        }
        if (FAILED(device_->CreateRootSignature(0, sig->GetBufferPointer(),
                   sig->GetBufferSize(), IID_PPV_ARGS(&gpuRootSig_)))) {
            OutputDebugStringA("[Vello GPU] Root signature creation failed\n");
            return false;
        }
    }

    // Create indirect command signature for ExecuteIndirect dispatch
    {
        D3D12_INDIRECT_ARGUMENT_DESC arg = {};
        arg.Type = D3D12_INDIRECT_ARGUMENT_TYPE_DISPATCH;
        D3D12_COMMAND_SIGNATURE_DESC csDesc = {};
        csDesc.ByteStride = 12; // sizeof(D3D12_DISPATCH_ARGUMENTS)
        csDesc.NumArgumentDescs = 1;
        csDesc.pArgumentDescs = &arg;
        if (FAILED(device_->CreateCommandSignature(&csDesc, nullptr, IID_PPV_ARGS(&indirectCmdSig_)))) {
            OutputDebugStringA("[Vello GPU] Indirect command signature creation failed\n");
            return false;
        }
    }

    // Use pre-compiled bytecode from d3d12_vello_bytecode.h (fxc /T cs_5_1 /O3).
    // This eliminates ~800ms of runtime D3DCompile on every startup.
    using namespace vello_bytecode;

    // Create PSOs directly from embedded bytecode — no runtime compilation needed.
    auto makePSO = [&](const unsigned char* bytecode, unsigned int size, ComPtr<ID3D12PipelineState>& pso) -> bool {
        D3D12_COMPUTE_PIPELINE_STATE_DESC d = {};
        d.pRootSignature = gpuRootSig_.Get();
        d.CS = { bytecode, size };
        return SUCCEEDED(device_->CreateComputePipelineState(&d, IID_PPV_ARGS(&pso)));
    };

    if (!makePSO(kBboxClear, kBboxClearSize, bboxClearPSO_)) return false;
    if (!makePSO(kFlatten, kFlattenSize, velloFlattenPSO_)) return false;
    if (!makePSO(kVelloClipReduce, kVelloClipReduceSize, clipReducePSO_)) return false;
    if (!makePSO(kVelloClipLeaf, kVelloClipLeafSize, clipLeafPSO_)) return false;
    if (!makePSO(kBinning, kBinningSize, binningPSO_)) return false;
    if (!makePSO(kTileAlloc, kTileAllocSize, tileAllocPSO_)) return false;
    if (!makePSO(kPathCountSetup, kPathCountSetupSize, pathCountSetupPSO_)) return false;
    if (!makePSO(kPathCount, kPathCountSize, pathCountPSO_)) return false;
    if (!makePSO(kBackdrop, kBackdropSize, backdropPSO_)) return false;
    if (!makePSO(kCoarse, kCoarseSize, velloCoarsePSO_)) return false;
    if (!makePSO(kPathTilingSetup, kPathTilingSetupSize, pathTilingSetupPSO_)) return false;
    if (!makePSO(kPathTiling, kPathTilingSize, pathTilingPSO_)) return false;
    if (!makePSO(kFine, kFineSize, velloFinePSO_)) return false;

    // All 13 PSOs created from pre-compiled bytecode (11 original + 2 clip pipeline)
    gpuPipelineCreated_ = true;
    return true;
}

bool D3D12VelloRenderer::DispatchGPU(ID3D12GraphicsCommandList* cmdList, uint32_t frameIndex)
{
    if (!initialized_ || !cmdList || pathInfos_.empty() || segments_.empty()) return false;

    static bool firstGpuDispatch = true;
    if (firstGpuDispatch) {
        firstGpuDispatch = false;
    }

    // Lazy-create GPU pipeline
    if (!CreateGPUPipeline()) {
        OutputDebugStringA("[Vello GPU] CreateGPUPipeline failed\n");
        return false;
    }

    uint32_t numPaths = (uint32_t)pathInfos_.size();
    uint32_t numSegs = (uint32_t)segments_.size();
    uint32_t numDrawObjs = (uint32_t)drawTags_.size();
    if (numDrawObjs == 0) numDrawObjs = numPaths;

    // Ensure output texture
    if (!EnsureOutputTexture(viewportW_, viewportH_)) return false;

    // Ensure all GPU buffers
    if (!EnsureGPUBuffers(numPaths, numSegs, numDrawObjs)) return false;

    // Ensure CPU buffers too (for segment upload)
    if (!EnsureBuffers()) return false;

    uint32_t fi = frameIndex % kMaxFrames;
    auto& fu = frameUploads_[fi];

    // ── Build VelloConfig ──
    uint32_t widthInBins = (tilesX_ + 15) / 16;
    uint32_t heightInBins = (tilesY_ + 15) / 16;
    uint32_t numBins = widthInBins * heightInBins;

    // Count total clip operations for n_clip config
    uint32_t totalClipOps = 0;
    for (auto& dt : drawTags_) {
        if (dt.tag == kDrawTagBeginClip || dt.tag == kDrawTagEndClip)
            totalClipOps++;
    }

    VelloConfig config = {};
    config.width_in_tiles = tilesX_;
    config.height_in_tiles = tilesY_;
    config.target_width = viewportW_;
    config.target_height = viewportH_;
    config.base_color = 0; // transparent
    config.n_drawobj = numDrawObjs;
    config.n_path = numPaths;
    config.n_clip = totalClipOps;
    config.bin_data_start = 0;
    config.lines_size = lineSoupCapacity_;
    config.binning_size = binDataCapacity_;
    config.tiles_size = velloTileCapacity_;
    config.seg_counts_size = segCountCapacity_;
    config.segments_size = velloSegmentCapacity_;
    config.blend_size = blendSpillCapacity_;
    config.ptcl_size = velloPtclCapacity_;
    config.num_segments = numSegs;

    // Upload config
    {
        void* mapped = nullptr;
        configUpload_->Map(0, nullptr, &mapped);
        memcpy(mapped, &config, sizeof(config));
        configUpload_->Unmap(0, nullptr);
    }

    // ── Upload CPU data ──
    // PathSegments
    {
        void* mapped = nullptr;
        fu.segmentUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, segments_.data(), numSegs * sizeof(PathSegment));
        fu.segmentUpload->Unmap(0, nullptr);
        cmdList->CopyBufferRegion(segmentBuffer_.Get(), 0, fu.segmentUpload.Get(), 0,
                                   numSegs * sizeof(PathSegment));
    }

    // PathInfos (for bbox_clear to read fill rules)
    {
        void* mapped = nullptr;
        fu.pathInfoUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, pathInfos_.data(), numPaths * sizeof(PathInfo));
        fu.pathInfoUpload->Unmap(0, nullptr);
        cmdList->CopyBufferRegion(pathInfoBuffer_.Get(), 0, fu.pathInfoUpload.Get(), 0,
                                   numPaths * sizeof(PathInfo));
    }

    // PathDraws (for coarse to read brush data)
    {
        void* mapped = nullptr;
        fu.pathDrawUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, pathDraws_.data(), numPaths * sizeof(PathDraw));
        fu.pathDrawUpload->Unmap(0, nullptr);
        cmdList->CopyBufferRegion(pathDrawBuffer_.Get(), 0, fu.pathDrawUpload.Get(), 0,
                                   numPaths * sizeof(PathDraw));
    }

    // DrawTags
    if (numDrawObjs > drawTagCapacity_ || !drawTagBuffer_) {
        drawTagCapacity_ = numDrawObjs * 2;
        CreateBuffer(device_, drawTagCapacity_ * sizeof(DrawTag), D3D12_HEAP_TYPE_DEFAULT,
                     D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_COMMON, drawTagBuffer_);
    }
    {
        uint32_t dtBytes = numDrawObjs * sizeof(DrawTag);
        if (!fu.drawTagUpload || numDrawObjs > fu.drawTagUploadCapacity) {
            fu.drawTagUploadCapacity = numDrawObjs * 2;
            CreateBuffer(device_, std::max(fu.drawTagUploadCapacity * (uint32_t)sizeof(DrawTag), 256u),
                         D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
                         D3D12_RESOURCE_STATE_GENERIC_READ, fu.drawTagUpload);
        }
        void* mapped = nullptr;
        fu.drawTagUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, drawTags_.data(), dtBytes);
        fu.drawTagUpload->Unmap(0, nullptr);
        cmdList->CopyBufferRegion(drawTagBuffer_.Get(), 0, fu.drawTagUpload.Get(), 0, dtBytes);
    }

    // DrawMonoids (CPU-computed prefix sum)
    std::vector<VelloDrawMonoid> drawMonoids;
    ComputeDrawMonoids(drawMonoids);

    // ── Build ClipInp data for GPU clip_reduce/clip_leaf pipeline ──
    // Follows Vello reference: BeginClip → ix = draw_obj_index (positive),
    //                          EndClip  → ix = -(draw_obj_index) - 1 (negative).
    // path_ix points to the path that defines the clip shape.
    std::vector<VelloClipInp> clipInpData;
    if (totalClipOps > 0) {
        clipInpData.resize(totalClipOps);
        uint32_t clipIdx = 0;
        // Also fix up DrawMonoids: EndClip must share path_ix with its matching BeginClip
        std::vector<uint32_t> clipBeginStack; // stack of draw object indices for BeginClip
        for (uint32_t i = 0; i < numDrawObjs; i++) {
            auto& dt = drawTags_[i];
            if (dt.tag == kDrawTagBeginClip) {
                VelloClipInp ci;
                ci.ix = (int32_t)i;               // positive → BeginClip
                ci.path_ix = (int32_t)drawMonoids[i].path_ix;
                clipInpData[clipIdx++] = ci;
                clipBeginStack.push_back(i);
            } else if (dt.tag == kDrawTagEndClip) {
                VelloClipInp ci;
                ci.ix = -(int32_t)i - 1;          // negative → EndClip
                if (!clipBeginStack.empty()) {
                    uint32_t beginIdx = clipBeginStack.back();
                    clipBeginStack.pop_back();
                    // EndClip uses same path_ix as its matching BeginClip
                    ci.path_ix = (int32_t)drawMonoids[beginIdx].path_ix;
                    drawMonoids[i].path_ix = drawMonoids[beginIdx].path_ix;
                    drawMonoids[i].scene_offset = drawMonoids[beginIdx].scene_offset;
                    drawMonoids[i].info_offset = drawMonoids[beginIdx].info_offset;
                } else {
                    ci.path_ix = 0;
                }
                clipInpData[clipIdx++] = ci;
            }
        }
    }

    // Upload DrawMonoids (may have been modified by clip fixup)
    {
        uint32_t dmBytes = numDrawObjs * sizeof(VelloDrawMonoid);
        if (!fu.drawMonoidUpload || numDrawObjs > fu.drawMonoidUploadCapacity) {
            fu.drawMonoidUploadCapacity = numDrawObjs * 2;
            CreateBuffer(device_, std::max(fu.drawMonoidUploadCapacity * (uint32_t)sizeof(VelloDrawMonoid), 256u),
                         D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
                         D3D12_RESOURCE_STATE_GENERIC_READ, fu.drawMonoidUpload);
        }
        void* mapped = nullptr;
        fu.drawMonoidUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, drawMonoids.data(), dmBytes);
        fu.drawMonoidUpload->Unmap(0, nullptr);
        cmdList->CopyBufferRegion(drawMonoidBuffer_.Get(), 0, fu.drawMonoidUpload.Get(), 0, dmBytes);
    }

    // Upload ClipInp data for GPU clip pipeline
    if (totalClipOps > 0 && clipInpBuffer_) {
        uint32_t ciBytes = totalClipOps * sizeof(VelloClipInp);
        if (!fu.clipInpUpload || totalClipOps > fu.clipInpUploadCapacity) {
            fu.clipInpUploadCapacity = totalClipOps * 2;
            CreateBuffer(device_, std::max(fu.clipInpUploadCapacity * (uint32_t)sizeof(VelloClipInp), 256u),
                         D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
                         D3D12_RESOURCE_STATE_GENERIC_READ, fu.clipInpUpload);
        }
        void* mapped = nullptr;
        fu.clipInpUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, clipInpData.data(), ciBytes);
        fu.clipInpUpload->Unmap(0, nullptr);
        cmdList->CopyBufferRegion(clipInpBuffer_.Get(), 0, fu.clipInpUpload.Get(), 0, ciBytes);
    }

    // Gradient ramps
    if (gradientCount_ > 0 && gradientRampBuffer_) {
        uint32_t gradBytes = gradientCount_ * kGradientRampWidth * sizeof(uint32_t);
        if (gradBytes > fu.gradientRampUploadCapacity * kGradientRampWidth * sizeof(uint32_t) || !fu.gradientRampUpload) {
            fu.gradientRampUploadCapacity = gradientCount_ * 2;
            CreateBuffer(device_, fu.gradientRampUploadCapacity * kGradientRampWidth * sizeof(uint32_t),
                         D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
                         D3D12_RESOURCE_STATE_GENERIC_READ, fu.gradientRampUpload);
        }
        if (fu.gradientRampUpload) {
            void* mapped = nullptr;
            fu.gradientRampUpload->Map(0, nullptr, &mapped);
            memcpy(mapped, gradientRamps_.data(), gradBytes);
            fu.gradientRampUpload->Unmap(0, nullptr);
            cmdList->CopyBufferRegion(gradientRampBuffer_.Get(), 0, fu.gradientRampUpload.Get(), 0, gradBytes);
        }
    }

    // ── Clear BumpAllocators to zero ──
    {
        if (!fu.bumpZeroUpload) {
            CreateBuffer(device_, 256, D3D12_HEAP_TYPE_UPLOAD,
                         D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATE_GENERIC_READ, fu.bumpZeroUpload);
        }
        BumpAllocators zeros = {};
        void* mapped = nullptr;
        fu.bumpZeroUpload->Map(0, nullptr, &mapped);
        memcpy(mapped, &zeros, sizeof(zeros));
        fu.bumpZeroUpload->Unmap(0, nullptr);
        cmdList->CopyBufferRegion(bumpBuffer_.Get(), 0, fu.bumpZeroUpload.Get(), 0, sizeof(BumpAllocators));
    }

    // ── Resource barriers: copy dest → appropriate states ──
    {
        D3D12_RESOURCE_BARRIER b[10];
        int bc = 0;
        b[bc++] = MakeBarrier(segmentBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        b[bc++] = MakeBarrier(pathInfoBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        b[bc++] = MakeBarrier(pathDrawBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        b[bc++] = MakeBarrier(drawTagBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        b[bc++] = MakeBarrier(drawMonoidBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        b[bc++] = MakeBarrier(bumpBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        b[bc++] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        if (totalClipOps > 0 && clipInpBuffer_)
            b[bc++] = MakeBarrier(clipInpBuffer_.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        cmdList->ResourceBarrier(bc, b);
    }

    // ── Clear output texture to transparent ──
    {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        auto cpuHandle = cpuUavHeap_->GetCPUDescriptorHandleForHeapStart();
        device_->CreateUnorderedAccessView(outputTexture_.Get(), nullptr, &uavDesc, cpuHandle);

        // Use a temporary shader-visible descriptor for the clear operation
        if (!gpuSrvHeap_[fi]) {
            D3D12_DESCRIPTOR_HEAP_DESC hd = {};
            hd.NumDescriptors = 256;
            hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
            hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
            device_->CreateDescriptorHeap(&hd, IID_PPV_ARGS(&gpuSrvHeap_[fi]));
        }
        // Use the last slot (255) as a temporary for the clear UAV
        auto gpuClearCpu = gpuSrvHeap_[fi]->GetCPUDescriptorHandleForHeapStart();
        gpuClearCpu.ptr += 255 * device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
        device_->CreateUnorderedAccessView(outputTexture_.Get(), nullptr, &uavDesc, gpuClearCpu);
        auto gpuClearGpu = gpuSrvHeap_[fi]->GetGPUDescriptorHandleForHeapStart();
        gpuClearGpu.ptr += 255 * device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

        ID3D12DescriptorHeap* clearHeaps[] = { gpuSrvHeap_[fi].Get() };
        cmdList->SetDescriptorHeaps(1, clearHeaps);
        const float clearColor[4] = { 0, 0, 0, 0 };
        cmdList->ClearUnorderedAccessViewFloat(gpuClearGpu, cpuHandle,
                                                outputTexture_.Get(), clearColor, 0, nullptr);
    }

    // ── Global UAV barrier helper ──
    auto uavBarrier = [&]() {
        D3D12_RESOURCE_BARRIER b = {};
        b.Type = D3D12_RESOURCE_BARRIER_TYPE_UAV;
        b.UAV.pResource = nullptr; // global UAV barrier
        cmdList->ResourceBarrier(1, &b);
    };

    // ── Create per-frame descriptor heap ──
    // Each stage needs 16 slots (SRV t0-t7 at +0, UAV u0-u7 at +8).
    // 11 stages * 16 = 176 descriptors minimum.  Use 256 for headroom.
    if (!gpuSrvHeap_[fi]) {
        D3D12_DESCRIPTOR_HEAP_DESC hd = {};
        hd.NumDescriptors = 256;
        hd.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        hd.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        device_->CreateDescriptorHeap(&hd, IID_PPV_ARGS(&gpuSrvHeap_[fi]));
    }
    if (!srvDescSize_) srvDescSize_ = device_->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

    auto cpuHeapStart = gpuSrvHeap_[fi]->GetCPUDescriptorHandleForHeapStart();
    auto gpuHeapStart = gpuSrvHeap_[fi]->GetGPUDescriptorHandleForHeapStart();
    ID3D12DescriptorHeap* heaps[] = { gpuSrvHeap_[fi].Get() };

    // Per-stage descriptor base — each stage gets 16 consecutive slots.
    // stageBase advances by 16 after each bindPipeline call.
    uint32_t stageBase = 0;

    // Helper: create SRV for structured buffer
    // slot is the SRV index within the stage (0-7 → t0-t7)
    auto makeSrv = [&](ID3D12Resource* res, uint32_t numElems, uint32_t stride, uint32_t slot) {
        D3D12_SHADER_RESOURCE_VIEW_DESC srv = {};
        srv.Format = DXGI_FORMAT_UNKNOWN;
        srv.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        srv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srv.Buffer.NumElements = std::max(numElems, 1u);
        srv.Buffer.StructureByteStride = stride;
        auto h = cpuHeapStart; h.ptr += (stageBase + slot) * srvDescSize_;
        device_->CreateShaderResourceView(res, &srv, h);
    };

    // Helper: create raw SRV
    auto makeRawSrv = [&](ID3D12Resource* res, uint32_t numElems, uint32_t slot) {
        D3D12_SHADER_RESOURCE_VIEW_DESC srv = {};
        srv.Format = DXGI_FORMAT_R32_TYPELESS;
        srv.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        srv.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srv.Buffer.NumElements = std::max(numElems, 1u);
        srv.Buffer.Flags = D3D12_BUFFER_SRV_FLAG_RAW;
        auto h = cpuHeapStart; h.ptr += (stageBase + slot) * srvDescSize_;
        device_->CreateShaderResourceView(res, &srv, h);
    };

    // Helper: create UAV for structured buffer
    // slot 8 = u0, 9 = u1, etc. within the stage's 16-slot region
    auto makeUav = [&](ID3D12Resource* res, uint32_t numElems, uint32_t stride, uint32_t slot) {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uav = {};
        uav.Format = DXGI_FORMAT_UNKNOWN;
        uav.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
        uav.Buffer.NumElements = std::max(numElems, 1u);
        uav.Buffer.StructureByteStride = stride;
        auto h = cpuHeapStart; h.ptr += (stageBase + slot) * srvDescSize_;
        device_->CreateUnorderedAccessView(res, nullptr, &uav, h);
    };

    // Helper: create raw UAV
    auto makeRawUav = [&](ID3D12Resource* res, uint32_t numElems, uint32_t slot) {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uav = {};
        uav.Format = DXGI_FORMAT_R32_TYPELESS;
        uav.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
        uav.Buffer.NumElements = std::max(numElems, 1u);
        uav.Buffer.Flags = D3D12_BUFFER_UAV_FLAG_RAW;
        auto h = cpuHeapStart; h.ptr += (stageBase + slot) * srvDescSize_;
        device_->CreateUnorderedAccessView(res, nullptr, &uav, h);
    };

    // Helper: create texture UAV
    auto makeTexUav = [&](ID3D12Resource* res, DXGI_FORMAT fmt, uint32_t slot) {
        D3D12_UNORDERED_ACCESS_VIEW_DESC uav = {};
        uav.Format = fmt;
        uav.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
        auto h = cpuHeapStart; h.ptr += (stageBase + slot) * srvDescSize_;
        device_->CreateUnorderedAccessView(res, nullptr, &uav, h);
    };

    // ── Set compute root signature + config ──
    // Each call advances stageBase by 16 so the next stage gets fresh descriptor slots.
    auto bindPipeline = [&](ID3D12PipelineState* pso) {
        cmdList->SetDescriptorHeaps(1, heaps);
        cmdList->SetComputeRootSignature(gpuRootSig_.Get());
        cmdList->SetComputeRootConstantBufferView(0, configUpload_->GetGPUVirtualAddress());
        auto srvGpu = gpuHeapStart; srvGpu.ptr += stageBase * srvDescSize_;
        cmdList->SetComputeRootDescriptorTable(1, srvGpu);
        auto uavGpu = gpuHeapStart; uavGpu.ptr += (stageBase + 8) * srvDescSize_;
        cmdList->SetComputeRootDescriptorTable(2, uavGpu);
        cmdList->SetPipelineState(pso);
        stageBase += 16;  // advance to next stage's descriptor region
    };

    // ================================================================
    // Stage 1: bbox_clear — clear atomic path bounding boxes
    // SRV t0: PathInfo[]   UAV u0: PathBbox (raw)
    // ================================================================
    makeSrv(pathInfoBuffer_.Get(), numPaths, sizeof(PathInfo), 0);
    makeRawUav(pathBboxBuffer_.Get(), numPaths * 6, 8); // slot 8 = u0
    bindPipeline(bboxClearPSO_.Get());
    cmdList->Dispatch((numPaths + 255) / 256, 1, 1);
    uavBarrier();

    // ================================================================
    // Stage 2: flatten — PathSegment[] → LineSoup[] + atomic PathBbox
    // SRV t0: PathSegment[]  UAV u0: LineSoup[]  u1: BumpAllocators  u2: PathBbox
    // ================================================================
    makeSrv(segmentBuffer_.Get(), numSegs, sizeof(PathSegment), 0);
    makeUav(lineSoupBuffer_.Get(), lineSoupCapacity_, sizeof(LineSoup), 8);  // u0
    makeRawUav(bumpBuffer_.Get(), 8, 9);   // u1
    makeRawUav(pathBboxBuffer_.Get(), numPaths * 6, 10); // u2
    bindPipeline(velloFlattenPSO_.Get());
    cmdList->Dispatch((numSegs + 255) / 256, 1, 1);
    uavBarrier();

    // ================================================================
    // Stage 3a: clip_reduce — hierarchical BIC reduction of clip begin/end pairs
    // SRV t0: ClipInp[]  t1: PathBbox (raw)
    // UAV u0: Bic[]  u1: ClipEl[]
    // Always dispatched when there are clips (performs within-workgroup matching)
    // ================================================================
    if (totalClipOps > 0 && clipInpBuffer_ && clipBicBuffer_ && clipElBuffer_) {
        uint32_t clipReduceWgs = (totalClipOps + 255) / 256;
        makeSrv(clipInpBuffer_.Get(), totalClipOps, sizeof(VelloClipInp), 0);
        makeRawSrv(pathBboxBuffer_.Get(), numPaths * 6, 1);
        makeUav(clipBicBuffer_.Get(), clipReduceWgs, sizeof(VelloClipBic), 8); // u0
        makeUav(clipElBuffer_.Get(), totalClipOps, sizeof(VelloClipEl), 9);     // u1
        bindPipeline(clipReducePSO_.Get());
        cmdList->Dispatch(clipReduceWgs, 1, 1);
        uavBarrier();
    }

    // ================================================================
    // Stage 3b: clip_leaf — compute final clip bboxes
    // SRV t0: ClipInp[]  t1: PathBbox (raw)  t2: Bic[]  t3: ClipEl[]
    // UAV u0: DrawMonoid[]  u1: clip_bbox[] (float4)
    // ================================================================
    if (totalClipOps > 0 && clipInpBuffer_ && clipBboxBuffer_) {
        uint32_t clipLeafWgs = (totalClipOps + 255) / 256;
        makeSrv(clipInpBuffer_.Get(), totalClipOps, sizeof(VelloClipInp), 0);
        makeRawSrv(pathBboxBuffer_.Get(), numPaths * 6, 1);
        if (clipBicBuffer_) {
            uint32_t bicCount = std::max((totalClipOps + 255) / 256, 1u);
            makeSrv(clipBicBuffer_.Get(), bicCount, sizeof(VelloClipBic), 2);
        } else {
            makeSrv(drawMonoidBuffer_.Get(), 1, sizeof(VelloDrawMonoid), 2); // dummy
        }
        if (clipElBuffer_) {
            makeSrv(clipElBuffer_.Get(), totalClipOps, sizeof(VelloClipEl), 3);
        } else {
            makeSrv(drawMonoidBuffer_.Get(), 1, sizeof(VelloDrawMonoid), 3); // dummy
        }
        makeUav(drawMonoidBuffer_.Get(), numDrawObjs, sizeof(VelloDrawMonoid), 8); // u0
        makeUav(clipBboxBuffer_.Get(), totalClipOps, 16, 9);                        // u1
        bindPipeline(clipLeafPSO_.Get());
        cmdList->Dispatch(clipLeafWgs, 1, 1);
        uavBarrier();
    }

    // ================================================================
    // Stage 4: binning — draw objects → bin_data[] + bin_header[]
    // SRV t0: DrawMonoid[]  t1: PathBbox (raw)  t2: clip_bbox[]  t3: DrawTag[]
    // UAV u0: BumpAllocators  u1: intersected_bbox[]  u2: bin_data (raw)  u3: bin_header[]
    // ================================================================
    makeSrv(drawMonoidBuffer_.Get(), numDrawObjs, sizeof(VelloDrawMonoid), 0);
    makeRawSrv(pathBboxBuffer_.Get(), numPaths * 6, 1);
    // clip_bbox: GPU-computed clip bboxes from clip_reduce/clip_leaf pipeline
    if (totalClipOps > 0 && clipBboxBuffer_) {
        makeSrv(clipBboxBuffer_.Get(), totalClipOps, 16, 2);
    } else {
        makeSrv(intersectedBboxBuffer_.Get(), 1, 16, 2); // dummy
    }
    makeSrv(drawTagBuffer_.Get(), numDrawObjs, sizeof(DrawTag), 3);
    makeRawUav(bumpBuffer_.Get(), 8, 8);   // u0
    makeUav(intersectedBboxBuffer_.Get(), numDrawObjs, 16, 9); // u1
    makeRawUav(binDataBuffer_.Get(), binDataCapacity_, 10);  // u2
    makeUav(binHeaderBuffer_.Get(), binHeaderCapacity_, sizeof(VelloBinHeader), 11); // u3
    bindPipeline(binningPSO_.Get());
    cmdList->Dispatch((numDrawObjs + 255) / 256, 1, 1);
    uavBarrier();

    // ================================================================
    // Stage 4: tile_alloc — allocate tiles per draw object
    // SRV t0: intersected_bbox (float4)  t1: DrawTag[]
    // UAV u0: BumpAllocators  u1: VelloPath[]  u2: VelloTile[]
    // ================================================================
    makeSrv(intersectedBboxBuffer_.Get(), numDrawObjs, 16, 0);
    makeSrv(drawTagBuffer_.Get(), numDrawObjs, sizeof(DrawTag), 1);
    makeRawUav(bumpBuffer_.Get(), 8, 8);   // u0
    makeUav(velloPathBuffer_.Get(), numDrawObjs, sizeof(VelloPath), 9); // u1
    makeUav(velloTileBuffer_.Get(), velloTileCapacity_, sizeof(VelloTile), 10); // u2
    bindPipeline(tileAllocPSO_.Get());
    cmdList->Dispatch((numDrawObjs + 255) / 256, 1, 1);
    uavBarrier();

    // ================================================================
    // Stage 5: path_count_setup — setup indirect dispatch
    // UAV u0: BumpAllocators  u1: indirect buffer
    // ================================================================
    makeRawUav(bumpBuffer_.Get(), 8, 8);   // u0
    makeRawUav(indirectBuffer1_.Get(), 3, 9); // u1
    bindPipeline(pathCountSetupPSO_.Get());
    cmdList->Dispatch(1, 1, 1);
    // Transition indirect buffer
    {
        auto b = MakeBarrier(indirectBuffer1_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                              D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
        cmdList->ResourceBarrier(1, &b);
    }

    // ================================================================
    // Stage 6: path_count (indirect) — count segments per tile
    // SRV t0: LineSoup[]  t1: VelloPath[]
    // UAV u0: BumpAllocators  u1: VelloTile (raw)  u2: SegmentCount[]
    // ================================================================
    makeSrv(lineSoupBuffer_.Get(), lineSoupCapacity_, sizeof(LineSoup), 0);
    makeSrv(velloPathBuffer_.Get(), numDrawObjs, sizeof(VelloPath), 1);
    makeRawUav(bumpBuffer_.Get(), 8, 8);   // u0
    makeRawUav(velloTileBuffer_.Get(), velloTileCapacity_ * 2, 9); // u1 (tile as raw for atomic)
    makeUav(segCountBuffer_.Get(), segCountCapacity_, sizeof(VelloSegmentCount), 10); // u2
    bindPipeline(pathCountPSO_.Get());
    cmdList->ExecuteIndirect(indirectCmdSig_.Get(), 1, indirectBuffer1_.Get(), 0, nullptr, 0);
    {
        auto b = MakeBarrier(indirectBuffer1_.Get(), D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT,
                              D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        cmdList->ResourceBarrier(1, &b);
    }
    uavBarrier();

    // ================================================================
    // Stage 7: backdrop — prefix sum of winding per tile row
    // SRV t0: VelloPath[]
    // UAV u0: VelloTile[]
    // ================================================================
    makeSrv(velloPathBuffer_.Get(), numDrawObjs, sizeof(VelloPath), 0);
    makeUav(velloTileBuffer_.Get(), velloTileCapacity_, sizeof(VelloTile), 8); // u0
    bindPipeline(backdropPSO_.Get());
    // Dispatch (n_drawobj, max_height_in_tiles, 1) — one WG per (drawobj, row)
    cmdList->Dispatch(numDrawObjs, tilesY_, 1);
    uavBarrier();

    // ================================================================
    // Stage 8: coarse — PTCL generation (256 threads per bin)
    // SRV t0: DrawTag[]  t1: DrawMonoid[]  t2: PathDraw[]  t3: BinHeader[]
    //     t4: bin_data (raw)  t5: VelloPath[]
    // UAV u0: VelloTile[]  u1: BumpAllocators  u2: ptcl (raw)
    // ================================================================
    makeSrv(drawTagBuffer_.Get(), numDrawObjs, sizeof(DrawTag), 0);
    makeSrv(drawMonoidBuffer_.Get(), numDrawObjs, sizeof(VelloDrawMonoid), 1);
    makeSrv(pathDrawBuffer_.Get(), numPaths, sizeof(PathDraw), 2);
    makeSrv(binHeaderBuffer_.Get(), binHeaderCapacity_, sizeof(VelloBinHeader), 3);
    makeRawSrv(binDataBuffer_.Get(), binDataCapacity_, 4);
    makeSrv(velloPathBuffer_.Get(), numDrawObjs, sizeof(VelloPath), 5);
    // Also need PathBbox for draw_flags lookup — bind as t6
    makeRawSrv(pathBboxBuffer_.Get(), numPaths * 6, 6);
    makeUav(velloTileBuffer_.Get(), velloTileCapacity_, sizeof(VelloTile), 8); // u0
    makeRawUav(bumpBuffer_.Get(), 8, 9);   // u1
    makeRawUav(velloPtclBuffer_.Get(), velloPtclCapacity_, 10); // u2
    bindPipeline(velloCoarsePSO_.Get());
    cmdList->Dispatch(widthInBins, heightInBins, 1);
    uavBarrier();

    // ================================================================
    // Stage 9: path_tiling_setup — setup indirect dispatch
    // UAV u0: BumpAllocators  u1: indirect buffer
    // ================================================================
    makeRawUav(bumpBuffer_.Get(), 8, 8);
    makeRawUav(indirectBuffer2_.Get(), 3, 9);
    bindPipeline(pathTilingSetupPSO_.Get());
    cmdList->Dispatch(1, 1, 1);
    {
        auto b = MakeBarrier(indirectBuffer2_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                              D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
        cmdList->ResourceBarrier(1, &b);
    }

    // ================================================================
    // Stage 10: path_tiling (indirect) — clip segments to tiles
    // SRV t0: SegmentCount[]  t1: LineSoup[]  t2: VelloPath[]  t3: VelloTile (raw read)
    // UAV u0: BumpAllocators  u1: Segment[]
    // ================================================================
    makeSrv(segCountBuffer_.Get(), segCountCapacity_, sizeof(VelloSegmentCount), 0);
    makeSrv(lineSoupBuffer_.Get(), lineSoupCapacity_, sizeof(LineSoup), 1);
    makeSrv(velloPathBuffer_.Get(), numDrawObjs, sizeof(VelloPath), 2);
    makeRawSrv(velloTileBuffer_.Get(), velloTileCapacity_ * 2, 3);
    makeRawUav(bumpBuffer_.Get(), 8, 8);   // u0
    makeUav(velloSegmentBuffer_.Get(), velloSegmentCapacity_, sizeof(VelloSegment), 9); // u1
    bindPipeline(pathTilingPSO_.Get());
    cmdList->ExecuteIndirect(indirectCmdSig_.Get(), 1, indirectBuffer2_.Get(), 0, nullptr, 0);
    {
        auto b = MakeBarrier(indirectBuffer2_.Get(), D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT,
                              D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        cmdList->ResourceBarrier(1, &b);
    }
    uavBarrier();

    // ================================================================
    // Stage 11: fine — pixel rasterization
    // SRV t0: Segment[]  t1: ptcl (raw)  t2: gradRamps[]  t3: info (raw)
    //     t4: imageAtlas (texture)
    // UAV u0: output texture  u1: blend_spill (raw)
    // ================================================================
    makeSrv(velloSegmentBuffer_.Get(), velloSegmentCapacity_, sizeof(VelloSegment), 0);
    makeRawSrv(velloPtclBuffer_.Get(), velloPtclCapacity_, 1);
    if (gradientCount_ > 0 && gradientRampBuffer_) {
        makeSrv(gradientRampBuffer_.Get(), gradientCount_ * kGradientRampWidth, sizeof(uint32_t), 2);
    } else {
        // Create dummy SRV
        makeSrv(bumpBuffer_.Get(), 1, 4, 2);
    }
    makeRawSrv(bumpBuffer_.Get(), 8, 3); // placeholder for info_data
    // t4: image atlas — skip for now
    makeTexUav(outputTexture_.Get(), DXGI_FORMAT_R8G8B8A8_UNORM, 8); // u0
    makeRawUav(blendSpillBuffer_.Get(), blendSpillCapacity_, 9); // u1
    bindPipeline(velloFinePSO_.Get());
    cmdList->Dispatch(tilesX_, tilesY_, 1);

    // ── Transition output back to COMMON ──
    {
        D3D12_RESOURCE_BARRIER b[4];
        int bc = 0;
        b[bc++] = MakeBarrier(outputTexture_.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COMMON);
        b[bc++] = MakeBarrier(segmentBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
        b[bc++] = MakeBarrier(pathInfoBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
        if (totalClipOps > 0 && clipInpBuffer_)
            b[bc++] = MakeBarrier(clipInpBuffer_.Get(), D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_COMMON);
        cmdList->ResourceBarrier(bc, b);
    }

    return true;  // GPU pipeline completed
}

} // namespace jalium
