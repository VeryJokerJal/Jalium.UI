#include "d3d12_ink_layer.h"
#include "jalium_types.h"
#include <d3d12sdklayers.h>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
using namespace jalium;

namespace jalium {
struct D3D12InkLayerBitmapTestAccess
{
    static bool Flush(D3D12InkLayerBitmap& layer, bool wait = true)
        { return layer.FlushPending(wait); }
    static void CloseRecording(D3D12InkLayerBitmap& layer)
        { if (FAILED(layer.cmdList_->Close())) throw std::runtime_error("pre-close failed"); }
    static ComPtr<ID3D12CommandAllocator> SwapAllocator(
        D3D12InkLayerBitmap& layer, ComPtr<ID3D12CommandAllocator> replacement)
        { layer.cmdAlloc_.Swap(replacement); return replacement; }
    static size_t RetainedPsoCount(D3D12InkLayerBitmap& layer)
        { return layer.batchPsos_.size(); }
};
}

namespace {
void Require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}
void Check(HRESULT hr, const char* message) { Require(SUCCEEDED(hr), message); }

struct Constants
{
    float color[4] = {1, 0, 0, 1};
    float width = 2, height = 2, time = 0;
    uint32_t seed = 0;
    float min[2] = {0, 0}, max[2] = {32, 32};
    uint32_t count = 2, taper = 0, ignorePressure = 0, fit = 0;
    float viewport[2] = {}, pad[2] = {};
};
static_assert(sizeof(Constants) == 80);
struct Point { float x = 0, y = 0, pressure = 1, pad = 0; };

class FailingAllocator final : public ID3D12CommandAllocator
{
    std::atomic<ULONG> refs_{1};
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override
    {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (iid == __uuidof(IUnknown) || iid == __uuidof(ID3D12Object) ||
            iid == __uuidof(ID3D12DeviceChild) || iid == __uuidof(ID3D12Pageable) ||
            iid == __uuidof(ID3D12CommandAllocator))
        { *out = static_cast<ID3D12CommandAllocator*>(this); AddRef(); return S_OK; }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs_; }
    ULONG STDMETHODCALLTYPE Release() override
        { auto n = --refs_; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE GetPrivateData(REFGUID, UINT*, void*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE SetPrivateData(REFGUID, UINT, const void*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE SetPrivateDataInterface(REFGUID, const IUnknown*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE SetName(LPCWSTR) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE GetDevice(REFIID, void**) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Reset() override { return E_OUTOFMEMORY; }
};

// This proxy forwards real work to WARP but can fail queue Signal before it
// is queued. The layer never passes the proxy back into a D3D12 runtime API.
class FaultQueue final : public ID3D12CommandQueue
{
    std::atomic<ULONG> refs_{1};
    ComPtr<ID3D12CommandQueue> real_;
public:
    unsigned signalFailures = 0;
    explicit FaultQueue(ID3D12CommandQueue* real) : real_(real) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override
    {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (iid == __uuidof(IUnknown) || iid == __uuidof(ID3D12Object) ||
            iid == __uuidof(ID3D12DeviceChild) || iid == __uuidof(ID3D12Pageable) ||
            iid == __uuidof(ID3D12CommandQueue))
        { *out = static_cast<ID3D12CommandQueue*>(this); AddRef(); return S_OK; }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs_; }
    ULONG STDMETHODCALLTYPE Release() override
        { auto n = --refs_; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE GetPrivateData(REFGUID guid, UINT* size, void* data) override
        { return real_->GetPrivateData(guid, size, data); }
    HRESULT STDMETHODCALLTYPE SetPrivateData(REFGUID guid, UINT size, const void* data) override
        { return real_->SetPrivateData(guid, size, data); }
    HRESULT STDMETHODCALLTYPE SetPrivateDataInterface(REFGUID guid, const IUnknown* data) override
        { return real_->SetPrivateDataInterface(guid, data); }
    HRESULT STDMETHODCALLTYPE SetName(LPCWSTR name) override { return real_->SetName(name); }
    HRESULT STDMETHODCALLTYPE GetDevice(REFIID iid, void** device) override
        { return real_->GetDevice(iid, device); }
    void STDMETHODCALLTYPE UpdateTileMappings(ID3D12Resource* resource, UINT regions,
        const D3D12_TILED_RESOURCE_COORDINATE* starts, const D3D12_TILE_REGION_SIZE* sizes,
        ID3D12Heap* heap, UINT ranges, const D3D12_TILE_RANGE_FLAGS* flags,
        const UINT* offsets, const UINT* counts, D3D12_TILE_MAPPING_FLAGS mappingFlags) override
        { real_->UpdateTileMappings(resource, regions, starts, sizes, heap, ranges, flags, offsets, counts, mappingFlags); }
    void STDMETHODCALLTYPE CopyTileMappings(ID3D12Resource* dst,
        const D3D12_TILED_RESOURCE_COORDINATE* dstStart, ID3D12Resource* src,
        const D3D12_TILED_RESOURCE_COORDINATE* srcStart, const D3D12_TILE_REGION_SIZE* size,
        D3D12_TILE_MAPPING_FLAGS flags) override
        { real_->CopyTileMappings(dst, dstStart, src, srcStart, size, flags); }
    void STDMETHODCALLTYPE ExecuteCommandLists(UINT count, ID3D12CommandList* const* lists) override
        { real_->ExecuteCommandLists(count, lists); }
    void STDMETHODCALLTYPE SetMarker(UINT metadata, const void* data, UINT size) override
        { real_->SetMarker(metadata, data, size); }
    void STDMETHODCALLTYPE BeginEvent(UINT metadata, const void* data, UINT size) override
        { real_->BeginEvent(metadata, data, size); }
    void STDMETHODCALLTYPE EndEvent() override { real_->EndEvent(); }
    HRESULT STDMETHODCALLTYPE Signal(ID3D12Fence* fence, UINT64 value) override
        { if (signalFailures) { --signalFailures; return E_OUTOFMEMORY; } return real_->Signal(fence, value); }
    HRESULT STDMETHODCALLTYPE Wait(ID3D12Fence* fence, UINT64 value) override
        { return real_->Wait(fence, value); }
    HRESULT STDMETHODCALLTYPE GetTimestampFrequency(UINT64* value) override
        { return real_->GetTimestampFrequency(value); }
    HRESULT STDMETHODCALLTYPE GetClockCalibration(UINT64* gpu, UINT64* cpu) override
        { return real_->GetClockCalibration(gpu, cpu); }
    D3D12_COMMAND_QUEUE_DESC STDMETHODCALLTYPE GetDesc() override { return real_->GetDesc(); }
};

struct Fixture
{
    ComPtr<ID3D12Device> device;
    ComPtr<ID3D12CommandQueue> queue;
    ComPtr<ID3D12InfoQueue> info;
    ComPtr<FaultQueue> faultQueue;

    Fixture()
    {
        ComPtr<ID3D12Debug> debug;
        if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debug))))
            debug->EnableDebugLayer();
        ComPtr<IDXGIFactory4> factory;
        Check(CreateDXGIFactory2(0, IID_PPV_ARGS(&factory)), "DXGI factory");
        ComPtr<IDXGIAdapter> warp;
        Check(factory->EnumWarpAdapter(IID_PPV_ARGS(&warp)), "WARP adapter");
        Check(D3D12CreateDevice(warp.Get(), D3D_FEATURE_LEVEL_11_0,
            IID_PPV_ARGS(&device)), "D3D12 WARP device");
        D3D12_COMMAND_QUEUE_DESC desc{};
        Check(device->CreateCommandQueue(&desc, IID_PPV_ARGS(&queue)), "command queue");
        device.As(&info);
        faultQueue.Attach(new FaultQueue(queue.Get()));
        std::printf("D3D12 WARP; debug layer: %s\n", info ? "enabled" : "unavailable");
    }

    std::unique_ptr<D3D12InkLayerBitmap> Layer(bool faults = false)
    {
        auto layer = std::make_unique<D3D12InkLayerBitmap>(device.Get(),
            faults ? faultQueue.Get() : queue.Get());
        Require(layer->Initialize(32, 32), "initialize ink layer");
        return layer;
    }

    std::unique_ptr<D3D12BrushShader> Shader(BrushBlendMode blend = BrushBlendMode::SourceOver)
    {
        auto shader = D3D12InkLayerBitmap::SharedPipeline(device.Get())->CreateBrushShader(
            "ink-batch-test", "float4 BrushMain(float2 p) { return StrokeColor; }", blend);
        Require(shader != nullptr, "compile brush");
        return shader;
    }

    int Draw(D3D12InkLayerBitmap& layer, D3D12BrushShader& shader,
        const Constants& constants = Constants{}, size_t count = 2)
    {
        std::vector<Point> points(count);
        return layer.DispatchBrush(&shader, points.data(), static_cast<uint32_t>(count),
            &constants, sizeof(constants), nullptr, 0);
    }

    void WaitQueue()
    {
        ComPtr<ID3D12Fence> fence;
        Check(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence)), "test fence");
        Check(queue->Signal(fence.Get(), 1), "test signal");
        HANDLE event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        Require(event != nullptr, "test event");
        HRESULT hr = fence->SetEventOnCompletion(1, event);
        DWORD result = SUCCEEDED(hr) ? WaitForSingleObject(event, 10000) : WAIT_FAILED;
        CloseHandle(event);
        Require(result == WAIT_OBJECT_0, "test queue completion timed out");
    }

    void Pixel(D3D12InkLayerBitmap& layer, std::array<int, 4> expected)
    {
        Require(D3D12InkLayerBitmapTestAccess::Flush(layer), "flush ink for readback");
        auto desc = layer.Texture()->GetDesc();
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint{};
        UINT64 bytes = 0;
        device->GetCopyableFootprints(&desc, 0, 1, 0, &footprint, nullptr, nullptr, &bytes);
        D3D12_HEAP_PROPERTIES heap{};
        heap.Type = D3D12_HEAP_TYPE_READBACK;
        D3D12_RESOURCE_DESC buffer{};
        buffer.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
        buffer.Width = bytes;
        buffer.Height = buffer.DepthOrArraySize = buffer.MipLevels = 1;
        buffer.SampleDesc.Count = 1;
        buffer.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
        ComPtr<ID3D12Resource> readback;
        Check(device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &buffer,
            D3D12_RESOURCE_STATE_COPY_DEST, nullptr, IID_PPV_ARGS(&readback)), "readback buffer");
        ComPtr<ID3D12CommandAllocator> allocator;
        Check(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,
            IID_PPV_ARGS(&allocator)), "readback allocator");
        ComPtr<ID3D12GraphicsCommandList> list;
        Check(device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator.Get(),
            nullptr, IID_PPV_ARGS(&list)), "readback list");
        D3D12_RESOURCE_BARRIER barrier{};
        barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barrier.Transition.pResource = layer.Texture();
        barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
        barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_SOURCE;
        list->ResourceBarrier(1, &barrier);
        D3D12_TEXTURE_COPY_LOCATION dst{}, src{};
        dst.pResource = readback.Get();
        dst.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        dst.PlacedFootprint = footprint;
        src.pResource = layer.Texture();
        src.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        list->CopyTextureRegion(&dst, 0, 0, 0, &src, nullptr);
        std::swap(barrier.Transition.StateBefore, barrier.Transition.StateAfter);
        list->ResourceBarrier(1, &barrier);
        Check(list->Close(), "close readback");
        ID3D12CommandList* lists[] = {list.Get()};
        queue->ExecuteCommandLists(1, lists);
        WaitQueue();
        void* mapped = nullptr;
        Check(readback->Map(0, nullptr, &mapped), "map readback");
        auto* pixel = static_cast<uint8_t*>(mapped) + footprint.Offset +
            16 * footprint.Footprint.RowPitch + 16 * 4;
        bool matches = true;
        for (size_t i = 0; i < 4; ++i) matches &= std::abs(int(pixel[i]) - expected[i]) <= 1;
        std::printf("pixel: %u,%u,%u,%u\n", pixel[0], pixel[1], pixel[2], pixel[3]);
        readback->Unmap(0, nullptr);
        Require(matches, "ink pixel mismatch");
    }

    void NoErrors()
    {
        WaitQueue();
        Check(device->GetDeviceRemovedReason(), "device removed");
        if (!info) return;
        for (UINT64 i = 0; i < info->GetNumStoredMessages(); ++i)
        {
            SIZE_T size = 0;
            info->GetMessage(i, nullptr, &size);
            std::vector<uint8_t> storage(size);
            auto* message = reinterpret_cast<D3D12_MESSAGE*>(storage.data());
            Check(info->GetMessage(i, message, &size), "debug message");
            if (message->Severity <= D3D12_MESSAGE_SEVERITY_ERROR)
            { std::fprintf(stderr, "%s\n", message->pDescription); Require(false, "D3D12 validation error"); }
        }
    }

    void FailClose(D3D12InkLayerBitmap& layer)
    {
        D3D12InkLayerBitmapTestAccess::CloseRecording(layer);
        Require(!D3D12InkLayerBitmapTestAccess::Flush(layer, false), "injected Close must fail");
        // The deliberate second Close is the only expected validation error.
        if (info) info->ClearStoredMessages();
    }
};

void PsoBeforeSubmit(Fixture& f)
{
    auto layer = f.Layer();
    auto red = f.Shader(), green = f.Shader(BrushBlendMode::Additive), erase = f.Shader(BrushBlendMode::Erase);
    Constants color;
    Require(f.Draw(*layer, *red, color, 32768) == 0, "red dispatch");
    color.color[0] = 0; color.color[1] = 1; color.color[3] = 0;
    Require(f.Draw(*layer, *green, color, 32768) == 0, "additive dispatch");
    color.color[3] = 0.5f;
    Require(f.Draw(*layer, *erase, color, 32768) == 0, "erase dispatch");
    red.reset(); green.reset(); erase.reset();
    f.Pixel(*layer, {128, 128, 0, 128});
    layer->Clear(0, 0, 1, 1);
    f.Pixel(*layer, {0, 0, 255, 255});
}

void PsoInFlight(Fixture& f)
{
    auto layer = f.Layer();
    auto shader = f.Shader();
    ComPtr<ID3D12Fence> gate;
    Check(f.device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&gate)), "gate fence");
    struct Unblock { ID3D12Fence* fence; ~Unblock() { fence->Signal(1); } } unblock{gate.Get()};
    Check(f.queue->Wait(gate.Get(), 1), "gate queue");
    Require(f.Draw(*layer, *shader) == 0, "dispatch behind gate");
    Require(D3D12InkLayerBitmapTestAccess::Flush(*layer, false), "async ink submit");
    shader.reset();
    Check(gate->Signal(1), "unblock queue");
    f.Pixel(*layer, {255, 0, 0, 255});
}

void PsoDedupe(Fixture& f)
{
    auto layer = f.Layer();
    auto a = f.Shader();
    auto b = f.Shader();
    for (int i = 0; i < 8; ++i)
    {
        Require(f.Draw(*layer, *a) == 0, "dispatch brush A");
        Require(f.Draw(*layer, *b) == 0, "dispatch brush B");
    }
    Require(D3D12InkLayerBitmapTestAccess::Flush(*layer), "submit batch");
    // Alternating brushes must retain one reference per distinct PSO, not one
    // AddRef per brush switch inside a single batch.
    Require(D3D12InkLayerBitmapTestAccess::RetainedPsoCount(*layer) == 2,
            "one retained reference per distinct PSO");
    f.Pixel(*layer, {255, 0, 0, 255});
}

void UnloadOpenBatch(Fixture& f)
{
    auto layer = f.Layer();
    auto shader = f.Shader();
    Require(f.Draw(*layer, *shader) == 0, "dispatch before unload");
    shader.reset();
    layer.reset(); // InkCanvas destroys its shaders before the bitmap.
}

void CloseRetry(Fixture& f)
{
    auto layer = f.Layer();
    auto shader = f.Shader();
    Require(f.Draw(*layer, *shader) == 0, "initial dispatch");
    f.FailClose(*layer);
    Require(f.Draw(*layer, *shader) == JALIUM_INK_DISPATCH_ERROR_TRANSIENT, "report async failure once");
    Require(f.Draw(*layer, *shader) == 0, "retry same layer and shader");
    f.Pixel(*layer, {255, 0, 0, 255});
}

void ClearRetry(Fixture& f)
{
    auto layer = f.Layer();
    layer->Clear(1, 0, 0, 1);
    f.FailClose(*layer);
    layer->Clear(0, 0, 1, 1); // Clear-only clients must recover too.
    f.Pixel(*layer, {0, 0, 255, 255});
}

void AllocatorRetry(Fixture& f)
{
    auto layer = f.Layer();
    auto shader = f.Shader();
    ComPtr<ID3D12CommandAllocator> failing;
    failing.Attach(new FailingAllocator());
    auto real = D3D12InkLayerBitmapTestAccess::SwapAllocator(*layer, failing);
    Require(f.Draw(*layer, *shader) == JALIUM_INK_DISPATCH_ERROR_TRANSIENT, "injected allocator OOM");
    D3D12InkLayerBitmapTestAccess::SwapAllocator(*layer, real);
    Require(f.Draw(*layer, *shader) == 0, "allocator retry without handle reconstruction");
    f.Pixel(*layer, {255, 0, 0, 255});
}

void SignalRetry(Fixture& f)
{
    auto layer = f.Layer(true);
    auto shader = f.Shader();
    Require(f.Draw(*layer, *shader) == 0, "dispatch before failed signal");
    f.faultQueue->signalFailures = 2;
    Require(!D3D12InkLayerBitmapTestAccess::Flush(*layer, false), "first Signal must fail");
    Require(!D3D12InkLayerBitmapTestAccess::Flush(*layer), "must not wait for failed Signal");
    shader.reset();
    f.Pixel(*layer, {255, 0, 0, 255}); // retry queues the missing marker.
    shader = f.Shader();
    Require(f.Draw(*layer, *shader) == JALIUM_INK_DISPATCH_ERROR_TRANSIENT, "report Signal failure once");
    Require(f.Draw(*layer, *shader) == 0, "retry after Signal recovery");
    f.Pixel(*layer, {255, 0, 0, 255});
}

void SignalTeardown(Fixture& f)
{
    auto layer = f.Layer(true);
    auto shader = f.Shader();
    Require(f.Draw(*layer, *shader) == 0, "dispatch before failing teardown");
    shader.reset();
    f.faultQueue->signalFailures = 10;
    layer.reset(); // Must defer dependencies, without an unreachable wait.
    f.faultQueue->signalFailures = 0;
    auto next = f.Layer();
    next->Clear(0, 1, 0, 1); // Later boundaries reclaim the retired batch.
    f.Pixel(*next, {0, 255, 0, 255});
    next->Clear(0, 0, 0, 0);
}
}

int main(int argc, char** argv)
{
    try
    {
        Require(argc == 2, "one test case is required");
        Fixture fixture;
        const std::string name = argv[1];
        if (name == "pso-before-submit") PsoBeforeSubmit(fixture);
        else if (name == "pso-in-flight") PsoInFlight(fixture);
        else if (name == "pso-dedupe") PsoDedupe(fixture);
        else if (name == "unload-open-batch") UnloadOpenBatch(fixture);
        else if (name == "close-retry") CloseRetry(fixture);
        else if (name == "clear-retry") ClearRetry(fixture);
        else if (name == "allocator-retry") AllocatorRetry(fixture);
        else if (name == "signal-retry") SignalRetry(fixture);
        else if (name == "signal-teardown") SignalTeardown(fixture);
        else throw std::runtime_error("unknown case: " + name);
        fixture.NoErrors();
        std::printf("PASS: %s\n", name.c_str());
        return 0;
    }
    catch (const std::exception& error)
    {
        std::fprintf(stderr, "FAIL: %s\n", error.what());
        return 1;
    }
}
