#include "d3d12_backend.h"
#include "d3d12_render_target.h"
#include "d3d12_resources.h"
#include <algorithm>
#include <cstdlib>
#include <cstdio>
#include <cwchar>
#include <vector>

#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "dwrite.lib")
#pragma comment(lib, "windowscodecs.lib")

namespace jalium {

namespace {
bool IsWarpForced()
{
    wchar_t* value = nullptr;
    size_t valueLength = 0;
    if (_wdupenv_s(&value, &valueLength, L"JALIUM_D3D12_FORCE_WARP") != 0 || !value || *value == L'\0') {
        if (value) {
            free(value);
        }
        return false;
    }

    bool enabled = _wcsicmp(value, L"1") == 0
        || _wcsicmp(value, L"true") == 0
        || _wcsicmp(value, L"yes") == 0
        || _wcsicmp(value, L"on") == 0;

    free(value);
    return enabled;
}
}

#if defined(_DEBUG)
namespace {
bool IsGpuDebugEnabled() {
    wchar_t* value = nullptr;
    size_t valueLength = 0;
    if (_wdupenv_s(&value, &valueLength, L"JALIUM_ENABLE_GPU_DEBUG") != 0 || !value || *value == L'\0') {
        if (value) {
            free(value);
        }
        return false;
    }

    bool enabled = _wcsicmp(value, L"1") == 0
        || _wcsicmp(value, L"true") == 0
        || _wcsicmp(value, L"yes") == 0
        || _wcsicmp(value, L"on") == 0;

    free(value);
    return enabled;
}

bool IsGpuValidationEnabled()
{
    wchar_t* value = nullptr;
    size_t valueLength = 0;
    if (_wdupenv_s(&value, &valueLength, L"JALIUM_ENABLE_GPU_VALIDATION") != 0 || !value || *value == L'\0') {
        if (value) {
            free(value);
        }
        return false;
    }

    bool enabled = _wcsicmp(value, L"1") == 0
        || _wcsicmp(value, L"true") == 0
        || _wcsicmp(value, L"yes") == 0
        || _wcsicmp(value, L"on") == 0;

    free(value);
    return enabled;
}
}
#endif

namespace {

bool AdapterDrivesMonitor(IDXGIAdapter1* adapter, HMONITOR monitor)
{
    if (!adapter || !monitor) {
        return false;
    }

    for (UINT outputIndex = 0;; ++outputIndex) {
        ComPtr<IDXGIOutput> output;
        if (adapter->EnumOutputs(outputIndex, &output) == DXGI_ERROR_NOT_FOUND) {
            break;
        }

        DXGI_OUTPUT_DESC outputDesc{};
        if (FAILED(output->GetDesc(&outputDesc))) {
            continue;
        }

        if (outputDesc.Monitor == monitor) {
            return true;
        }
    }

    return false;
}

} // namespace

D3D12Backend::D3D12Backend() = default;

D3D12Backend::~D3D12Backend() {
    // Graveyard ComPtrs hold the last refs to retired GPU resources. By the
    // time the backend itself is being destroyed the command queue is also
    // about to die, so any GPU work on these is necessarily complete; drop
    // them before tearing down the queue/device.
    {
        std::lock_guard<std::mutex> lock(graveyardMutex_);
        graveyard_.clear();
    }

    // Release in reverse order of creation
    wicFactory_.Reset();
    dwriteFactory_.Reset();
    commandQueue_.Reset();
    device_.Reset();
    dxgiFactory_.Reset();
}

void D3D12Backend::RetireGpuResource(ComPtr<ID3D12Resource>&& resource) {
    if (!resource) return;
    std::lock_guard<std::mutex> lock(graveyardMutex_);
    // Pin to (lastSubmittedFenceValue_ + 1): the value that the NEXT Signal will
    // emit. Any open command list currently being recorded (which may already
    // reference this resource via DrawBitmap / CopyTextureRegion) will Signal
    // a value ≥ lastSubmittedFenceValue_ + 1 when submitted.
    //
    // Using lastSubmittedFenceValue_ directly is INCORRECT and triggers
    // D3D12 ERROR #921 OBJECT_DELETED_WHILE_STILL_IN_USE:
    //   T0: frame N submitted, Signal(V)         → lastSubmitted = V
    //   T1: frame N+1 BeginFrame, Reclaim(V) ✓
    //   T2: frame N+1 starts recording; DrawBitmap(X) records X.texture into open cmd list
    //   T3: worker thread evicts X from LRU → ~D3D12Bitmap → RetireGpuResource(X.tex, fence=V)
    //   T4: frame N+1 submitted, Signal(V+1)
    //   T5: frame N+2 BeginFrame, Reclaim(V+1) → X.tex released (fence V ≤ V+1)
    //   T6: GPU executes frame N+1's commands → references released X.tex → CRASH
    //
    // With (lastSubmitted + 1) we guarantee X.tex stays alive until the next
    // Signal completes, by which time any cmd list that referenced it is done.
    graveyard_.push_back({ std::move(resource), lastSubmittedFenceValue_ + 1 });
}

void D3D12Backend::NoteSubmittedFenceValue(uint64_t fenceValue) {
    std::lock_guard<std::mutex> lock(graveyardMutex_);
    if (fenceValue > lastSubmittedFenceValue_) {
        lastSubmittedFenceValue_ = fenceValue;
    }
}

void D3D12Backend::ReclaimRetiredGpuResources(uint64_t completedFenceValue) {
    std::lock_guard<std::mutex> lock(graveyardMutex_);
    if (graveyard_.empty()) return;
    auto newEnd = std::remove_if(
        graveyard_.begin(), graveyard_.end(),
        [completedFenceValue](const RetiredResource& r) {
            return r.fenceValue <= completedFenceValue;
        });
    graveyard_.erase(newEnd, graveyard_.end());
}

void D3D12Backend::ReleasePartialInit() {
    wicFactory_.Reset();
    dwriteFactory_.Reset();
    commandQueue_.Reset();
    device_.Reset();
    dxgiFactory_.Reset();
}

JaliumResult D3D12Backend::GetAdapterInfo(JaliumAdapterInfo* out) const {
    if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
    *out = JaliumAdapterInfo{};
    if (!adapterDescValid_) return JALIUM_ERROR_NOT_SUPPORTED;

    // 名字：DXGI_ADAPTER_DESC1::Description 是 128 wide-char 定长缓冲；
    // JaliumAdapterInfo::name 也是 wchar_t[128]，直接 wcsncpy_s 即可。
    wcsncpy_s(out->name, _countof(out->name),
              adapterDesc_.Description, _TRUNCATE);

    // 类型分类：
    //   - SOFTWARE flag → Software（WARP）
    //   - Microsoft 厂商 + 零 VRAM → Software（Basic Render Driver 无 SOFTWARE flag 时的兜底）
    //   - UMA（统一内存架构）→ Integrated（核显 / APU 与 CPU 共享系统内存）
    //   - 否则 → Discrete
    // 注意：不能用 DedicatedVideoMemory==0 判核显。AMD / Intel APU 的 BIOS 常划一块
    // UMA carveout 当"专用显存"（512MB~2GB），DedicatedVideoMemory 非零，旧逻辑会把它
    // 误判成 Discrete（实测 "AMD Radeon(TM) Graphics" APU 被标成 Discrete）。可靠信号是
    // D3D12 的 D3D12_FEATURE_ARCHITECTURE1.UMA flag。
    bool isSoftware = (adapterDesc_.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0
                    || (adapterDesc_.VendorId == 0x1414 && adapterDesc_.DedicatedVideoMemory == 0);
    if (isSoftware) {
        out->adapterType = JALIUM_GPU_ADAPTER_TYPE_SOFTWARE;
    } else {
        // 先用 UMA flag 判定；CheckFeatureSupport 失败才回退到 DedicatedVideoMemory 启发式。
        bool isIntegrated = (adapterDesc_.DedicatedVideoMemory == 0);
        if (device_) {
            D3D12_FEATURE_DATA_ARCHITECTURE1 arch = {};
            arch.NodeIndex = 0;
            if (SUCCEEDED(device_->CheckFeatureSupport(
                    D3D12_FEATURE_ARCHITECTURE1, &arch, sizeof(arch)))) {
                isIntegrated = (arch.UMA != FALSE);
            }
        }
        out->adapterType = isIntegrated
            ? JALIUM_GPU_ADAPTER_TYPE_INTEGRATED
            : JALIUM_GPU_ADAPTER_TYPE_DISCRETE;
    }

    out->dedicatedVideoMemory = adapterDesc_.DedicatedVideoMemory;
    out->sharedSystemMemory   = adapterDesc_.SharedSystemMemory;
    out->vendorId             = adapterDesc_.VendorId;
    out->deviceId             = adapterDesc_.DeviceId;
    return JALIUM_OK;
}

JaliumResult D3D12Backend::CheckDeviceStatus() {
    if (!device_) return JALIUM_ERROR_DEVICE_LOST;

    HRESULT hr = device_->GetDeviceRemovedReason();
    if (SUCCEEDED(hr)) return JALIUM_OK;

    return JALIUM_ERROR_DEVICE_LOST;
}

bool D3D12Backend::Initialize(void* preferredWindow) {
    if (initialized_) return true;

    // Read GPU preference from environment variable (zero ABI change approach)
    gpuPrefFromEnv_ = JALIUM_GPU_PREFERENCE_AUTO;
    {
        wchar_t* val = nullptr;
        size_t len = 0;
        if (_wdupenv_s(&val, &len, L"JALIUM_GPU_PREFERENCE") == 0 && val && *val != L'\0') {
            if (_wcsicmp(val, L"integrated") == 0 || _wcsicmp(val, L"igpu") == 0
                || _wcsicmp(val, L"low") == 0 || _wcsicmp(val, L"minimum_power") == 0) {
                gpuPrefFromEnv_ = JALIUM_GPU_PREFERENCE_MINIMUM_POWER;
            } else if (_wcsicmp(val, L"discrete") == 0 || _wcsicmp(val, L"high") == 0
                || _wcsicmp(val, L"high_performance") == 0) {
                gpuPrefFromEnv_ = JALIUM_GPU_PREFERENCE_HIGH_PERFORMANCE;
            }
        }
        if (val) free(val);
    }

    if (!CreateD3D12Device(preferredWindow)) {
        return false;
    }

    if (!CreateDWriteFactory()) {
        ReleasePartialInit();
        return false;
    }

    if (!CreateWICFactory()) {
        ReleasePartialInit();
        return false;
    }

    initialized_ = true;
    return true;
}

bool D3D12Backend::CreateD3D12Device(void* preferredWindow) {
    UINT dxgiFactoryFlags = 0;

    // DRED (Device Removed Extended Data) for diagnosing device-lost.
    // Only in debug builds: D3D12GetDebugInterface loads d3d12SDKLayers.dll
    // and auto-breadcrumbs add per-command-list overhead.
#if defined(_DEBUG)
    {
        ComPtr<ID3D12DeviceRemovedExtendedDataSettings> dredSettings;
        if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&dredSettings)))) {
            dredSettings->SetAutoBreadcrumbsEnablement(D3D12_DRED_ENABLEMENT_FORCED_ON);
            dredSettings->SetPageFaultEnablement(D3D12_DRED_ENABLEMENT_FORCED_ON);
        }
    }
#endif

#if defined(_DEBUG)
    if (IsGpuDebugEnabled()) {
        ComPtr<ID3D12Debug> debugController;
        if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debugController)))) {
            debugController->EnableDebugLayer();
            dxgiFactoryFlags |= DXGI_CREATE_FACTORY_DEBUG;

            ComPtr<ID3D12Debug1> debugController1;
            if (IsGpuValidationEnabled() &&
                SUCCEEDED(debugController.As(&debugController1))) {
                debugController1->SetEnableGPUBasedValidation(TRUE);
                OutputDebugStringA("[D3D12Backend] GPU-based validation enabled.\n");
            }
        }
    }
#endif

    // Create DXGI factory
    HRESULT hr = CreateDXGIFactory2(dxgiFactoryFlags, IID_PPV_ARGS(&dxgiFactory_));
    if (FAILED(hr)) {
        return false;
    }

    auto tryCreateWarpDevice = [this]() -> bool {
        ComPtr<IDXGIAdapter> warpAdapter;
        if (FAILED(dxgiFactory_->EnumWarpAdapter(IID_PPV_ARGS(&warpAdapter)))) {
            return false;
        }

        if (!SUCCEEDED(D3D12CreateDevice(
            warpAdapter.Get(),
            D3D_FEATURE_LEVEL_11_0,
            IID_PPV_ARGS(&device_)))) {
            return false;
        }

        // WARP adapter exposes itself as IDXGIAdapter1 too — capture its desc so
        // GetAdapterInfo can report "Microsoft Basic Render Driver / Software"
        // (helps users notice they fell into WARP because driver / config is broken).
        ComPtr<IDXGIAdapter1> warpAdapter1;
        if (SUCCEEDED(warpAdapter.As(&warpAdapter1))) {
            DXGI_ADAPTER_DESC1 desc{};
            if (SUCCEEDED(warpAdapter1->GetDesc1(&desc))) {
                adapterDesc_ = desc;
                adapterDescValid_ = true;
            }
        }
        return true;
    };

    auto tryCreateDeviceForAdapter = [this](IDXGIAdapter1* adapter) -> bool {
        if (!adapter) {
            return false;
        }

        DXGI_ADAPTER_DESC1 desc{};
        if (FAILED(adapter->GetDesc1(&desc))) {
            return false;
        }

        // Skip software adapters (WARP fallback can be added later if needed).
        // Check both the explicit flag AND the Microsoft vendor + zero VRAM heuristic,
        // because some GPU-switching configurations expose WARP without the SOFTWARE flag.
        bool isSoftwareAdapter = (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0;
        if (!isSoftwareAdapter && desc.VendorId == 0x1414 && desc.DedicatedVideoMemory == 0) {
            isSoftwareAdapter = true; // Microsoft Basic Render Driver without SOFTWARE flag
        }
        if (isSoftwareAdapter) {
            return false;
        }

        HRESULT hrCreate = D3D12CreateDevice(
                adapter,
                D3D_FEATURE_LEVEL_11_0,
                IID_PPV_ARGS(&device_));
        if (!SUCCEEDED(hrCreate)) {
            return false;
        }

        // Cache the adapter description for later GetAdapterInfo() queries —
        // users see this in the IDE status bar, and it lets us prove which GPU
        // actually got picked (vs. hoping DXGI HighPerformance preference did
        // the right thing under hybrid graphics).
        adapterDesc_ = desc;
        adapterDescValid_ = true;
        return true;
    };

    // Map JaliumGpuPreference to DXGI_GPU_PREFERENCE for adapter enumeration.
    DXGI_GPU_PREFERENCE dxgiPref = DXGI_GPU_PREFERENCE_UNSPECIFIED;
    bool hasExplicitPreference = false;
    switch (gpuPrefFromEnv_) {
    case JALIUM_GPU_PREFERENCE_HIGH_PERFORMANCE:
        dxgiPref = DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE;
        hasExplicitPreference = true;
        break;
    case JALIUM_GPU_PREFERENCE_MINIMUM_POWER:
        dxgiPref = DXGI_GPU_PREFERENCE_MINIMUM_POWER;
        hasExplicitPreference = true;
        break;
    default:
        dxgiPref = DXGI_GPU_PREFERENCE_UNSPECIFIED;
        break;
    }

    // Strategy 0: Explicit WARP fallback (software D3D12 device).
    if (IsWarpForced() && tryCreateWarpDevice()) {
        OutputDebugStringA("[D3D12Backend] Using forced WARP adapter.\n");
    }

    // Strategy 1: Monitor-associated adapter selection.
    // Only used when no explicit GPU preference is set.
    if (!device_ && !hasExplicitPreference) {
        HMONITOR preferredMonitor = nullptr;
        if (preferredWindow != nullptr) {
            preferredMonitor = MonitorFromWindow(static_cast<HWND>(preferredWindow), MONITOR_DEFAULTTONEAREST);
        }

        if (preferredMonitor) {
            for (UINT adapterIndex = 0;; ++adapterIndex) {
                ComPtr<IDXGIAdapter1> adapter;
                HRESULT hrEnum = dxgiFactory_->EnumAdapters1(adapterIndex, &adapter);
                if (FAILED(hrEnum)) {
                    break;
                }

                if (!AdapterDrivesMonitor(adapter.Get(), preferredMonitor)) {
                    continue;
                }

                if (tryCreateDeviceForAdapter(adapter.Get())) {
                    break;
                }
            }
        }
    }

    // Strategy 2: GPU preference ordering via DXGI 1.6.
    ComPtr<IDXGIFactory6> factory6;
    if (!device_ && SUCCEEDED(dxgiFactory_.As(&factory6))) {
        for (UINT adapterIndex = 0;; ++adapterIndex) {
            ComPtr<IDXGIAdapter1> adapter;
            HRESULT hrEnum = factory6->EnumAdapterByGpuPreference(
                adapterIndex,
                dxgiPref,
                IID_PPV_ARGS(&adapter));
            if (FAILED(hrEnum)) {
                break;
            }

            if (tryCreateDeviceForAdapter(adapter.Get())) {
                break;
            }
        }
    }

    // Strategy 3: Legacy enumeration fallback.
    if (!device_) {
        for (UINT adapterIndex = 0;; ++adapterIndex) {
            ComPtr<IDXGIAdapter1> adapter;
            HRESULT hrEnum = dxgiFactory_->EnumAdapters1(adapterIndex, &adapter);
            if (FAILED(hrEnum)) {
                break;
            }

            if (tryCreateDeviceForAdapter(adapter.Get())) {
                break;
            }
        }
    }

    // Strategy 4: Final WARP fallback when no hardware adapter can be created.
    if (!device_ && tryCreateWarpDevice()) {
        OutputDebugStringA("[D3D12Backend] Falling back to WARP adapter.\n");
    }

    if (!device_) {
        return false;
    }

#if defined(_DEBUG)
    if (IsGpuDebugEnabled()) {
        ComPtr<ID3D12InfoQueue> infoQueue;
        if (SUCCEEDED(device_.As(&infoQueue))) {
            infoQueue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_CORRUPTION, TRUE);
            infoQueue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_ERROR, TRUE);
        }
    }
#endif

    // Create command queue
    D3D12_COMMAND_QUEUE_DESC queueDesc = {};
    queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;

    hr = device_->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&commandQueue_));
    if (FAILED(hr)) {
        return false;
    }

    return true;
}

bool D3D12Backend::CreateDWriteFactory() {
    HRESULT hr = DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED,
        __uuidof(IDWriteFactory5),
        reinterpret_cast<IUnknown**>(dwriteFactory_.GetAddressOf()));

    return SUCCEEDED(hr);
}

bool D3D12Backend::CreateWICFactory() {
    HRESULT hr = CoCreateInstance(
        CLSID_WICImagingFactory,
        nullptr,
        CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(&wicFactory_));

    return SUCCEEDED(hr);
}

RenderTarget* D3D12Backend::CreateRenderTarget(void* hwnd, int32_t width, int32_t height) {
    if (!initialized_ && !Initialize(hwnd)) {
        return nullptr;
    }

    auto rt = new D3D12RenderTarget(this, hwnd, width, height, false);
    if (!rt->Initialize()) {
        delete rt;
        return nullptr;
    }
    return rt;
}

RenderTarget* D3D12Backend::CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height) {
    if (!initialized_ && !Initialize(hwnd)) {
        return nullptr;
    }

    auto rt = new D3D12RenderTarget(this, hwnd, width, height, true);
    if (!rt->Initialize()) {
        delete rt;
        return nullptr;
    }
    return rt;
}

Brush* D3D12Backend::CreateSolidBrush(float r, float g, float b, float a) {
    return new D3D12SolidBrush(r, g, b, a);
}

Brush* D3D12Backend::CreateLinearGradientBrush(
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t spreadMethod)
{
    auto* b = new D3D12LinearGradientBrush(startX, startY, endX, endY, stops, stopCount);
    b->spreadMethod_ = spreadMethod;
    return b;
}

Brush* D3D12Backend::CreateRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t spreadMethod)
{
    auto* b = new D3D12RadialGradientBrush(centerX, centerY, radiusX, radiusY, originX, originY, stops, stopCount);
    b->spreadMethod_ = spreadMethod;
    return b;
}

TextFormat* D3D12Backend::CreateTextFormat(
    const wchar_t* fontFamily, float fontSize,
    int32_t fontWeight, int32_t fontStyle)
{
    if (!initialized_ && !Initialize()) {
        return nullptr;
    }
    return new D3D12TextFormat(dwriteFactory_.Get(), fontFamily, fontSize, fontWeight, fontStyle);
}

Bitmap* D3D12Backend::CreateBitmapFromMemory(const uint8_t* data, uint32_t dataSize) {
    if (!initialized_ && !Initialize()) {
        return nullptr;
    }

    if (!wicFactory_ || !data || dataSize == 0) {
        return nullptr;
    }

    // Create WIC stream from memory
    ComPtr<IWICStream> stream;
    HRESULT hr = wicFactory_->CreateStream(&stream);
    if (FAILED(hr)) return nullptr;

    hr = stream->InitializeFromMemory(const_cast<uint8_t*>(data), dataSize);
    if (FAILED(hr)) return nullptr;

    // Create decoder
    ComPtr<IWICBitmapDecoder> decoder;
    hr = wicFactory_->CreateDecoderFromStream(
        stream.Get(),
        nullptr,
        WICDecodeMetadataCacheOnDemand,
        &decoder);
    if (FAILED(hr)) return nullptr;

    // Get frame
    ComPtr<IWICBitmapFrameDecode> frame;
    hr = decoder->GetFrame(0, &frame);
    if (FAILED(hr)) return nullptr;

    // Convert to BGRA32 format
    ComPtr<IWICFormatConverter> converter;
    hr = wicFactory_->CreateFormatConverter(&converter);
    if (FAILED(hr)) return nullptr;

    hr = converter->Initialize(
        frame.Get(),
        GUID_WICPixelFormat32bppPBGRA,
        WICBitmapDitherTypeNone,
        nullptr,
        0.0f,
        WICBitmapPaletteTypeMedianCut);
    if (FAILED(hr)) return nullptr;

    // Get image dimensions
    UINT width, height;
    hr = converter->GetSize(&width, &height);
    if (FAILED(hr)) return nullptr;

    // Copy pixels
    std::vector<uint8_t> pixels(width * height * 4);
    hr = converter->CopyPixels(
        nullptr,
        width * 4,
        static_cast<UINT>(pixels.size()),
        pixels.data());
    if (FAILED(hr)) return nullptr;

    // Create bitmap object
    auto bitmap = new D3D12Bitmap(this, width, height);
    bitmap->SetBitmapData(pixels.data(), static_cast<uint32_t>(pixels.size()));

    return bitmap;
}

Bitmap* D3D12Backend::CreateBitmapFromPixels(const uint8_t* pixels, uint32_t width, uint32_t height, uint32_t stride) {
    if (!initialized_ && !Initialize()) {
        return nullptr;
    }
    if (!pixels || width == 0 || height == 0 || stride < width * 4u) {
        return nullptr;
    }

    // Pack the input pixels (which may have padding beyond width*4) into a tight BGRA8 buffer.
    const size_t rowBytes = static_cast<size_t>(width) * 4u;
    std::vector<uint8_t> packed(rowBytes * height);
    if (stride == rowBytes) {
        std::memcpy(packed.data(), pixels, packed.size());
    } else {
        for (uint32_t row = 0; row < height; ++row) {
            std::memcpy(packed.data() + row * rowBytes,
                        pixels + static_cast<size_t>(row) * stride,
                        rowBytes);
        }
    }

    auto bitmap = new D3D12Bitmap(this, width, height);
    bitmap->SetBitmapData(packed.data(), static_cast<uint32_t>(packed.size()));
    return bitmap;
}

IRenderBackend* CreateD3D12Backend() {
    return new D3D12Backend();
}

} // namespace jalium
