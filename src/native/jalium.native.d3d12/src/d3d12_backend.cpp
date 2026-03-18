#include "d3d12_backend.h"
#include "d3d12_render_target.h"
#include "d3d12_resources.h"
#include "liquid_glass_effects.h"
#include "transition_shader_effect.h"
#include <cstdlib>
#include <cwchar>
#include <vector>

#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "d2d1.lib")
#pragma comment(lib, "dwrite.lib")
#pragma comment(lib, "windowscodecs.lib")

namespace jalium {

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
    // Release in reverse order of creation
    wicFactory_.Reset();
    dwriteFactory_.Reset();
    d2dDevice_.Reset();
    d2dFactory_.Reset();
    d3d11On12Device_.Reset();
    d3d11Context_.Reset();
    d3d11Device_.Reset();
    commandQueue_.Reset();
    device_.Reset();
    dxgiFactory_.Reset();
}

void D3D12Backend::ReleasePartialInit() {
    // Clean up resources allocated during a partially-failed Initialize().
    // Without this, successful sub-steps (e.g. CreateD3D12Device) would leak
    // if a later sub-step (e.g. CreateD2DDevice) fails.
    wicFactory_.Reset();
    dwriteFactory_.Reset();
    d2dDevice_.Reset();
    d2dFactory_.Reset();
    d3d11On12Device_.Reset();
    d3d11Context_.Reset();
    d3d11Device_.Reset();
    commandQueue_.Reset();
    device_.Reset();
    dxgiFactory_.Reset();
}

JaliumResult D3D12Backend::CheckDeviceStatus() {
    if (!device_) return JALIUM_ERROR_DEVICE_LOST;

    HRESULT hr = device_->GetDeviceRemovedReason();
    if (SUCCEEDED(hr)) return JALIUM_OK;

    return JALIUM_ERROR_DEVICE_LOST;
}

bool D3D12Backend::Initialize(void* preferredWindow) {
    if (initialized_) return true;

    if (!CreateD3D12Device(preferredWindow)) {
        return false;
    }

    if (!CreateD2DDevice()) {
        ReleasePartialInit();
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

#if defined(_DEBUG)
    if (IsGpuDebugEnabled()) {
        ComPtr<ID3D12Debug> debugController;
        if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debugController)))) {
            debugController->EnableDebugLayer();
            dxgiFactoryFlags |= DXGI_CREATE_FACTORY_DEBUG;
        }
    }
#endif

    // Create DXGI factory
    HRESULT hr = CreateDXGIFactory2(dxgiFactoryFlags, IID_PPV_ARGS(&dxgiFactory_));
    if (FAILED(hr)) {
        return false;
    }

    auto tryCreateDeviceForAdapter = [this](IDXGIAdapter1* adapter) -> bool {
        if (!adapter) {
            return false;
        }

        DXGI_ADAPTER_DESC1 desc{};
        if (FAILED(adapter->GetDesc1(&desc))) {
            return false;
        }

        // Skip software adapters (WARP fallback can be added later if needed).
        if (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) {
            return false;
        }

        return SUCCEEDED(D3D12CreateDevice(
            adapter,
            D3D_FEATURE_LEVEL_11_0,
            IID_PPV_ARGS(&device_)));
    };

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

    // Prefer OS GPU preference ordering (hybrid GPU aware), then fall back to legacy enumeration.
    ComPtr<IDXGIFactory6> factory6;
    if (!device_ && SUCCEEDED(dxgiFactory_.As(&factory6))) {
        for (UINT adapterIndex = 0;; ++adapterIndex) {
            ComPtr<IDXGIAdapter1> adapter;
            HRESULT hrEnum = factory6->EnumAdapterByGpuPreference(
                adapterIndex,
                DXGI_GPU_PREFERENCE_UNSPECIFIED,
                IID_PPV_ARGS(&adapter));
            if (FAILED(hrEnum)) {
                break;
            }

            if (tryCreateDeviceForAdapter(adapter.Get())) {
                break;
            }
        }
    }

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

    if (!device_) {
        return false;
    }

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

bool D3D12Backend::CreateD2DDevice() {
    // Create D2D factory
    D2D1_FACTORY_OPTIONS factoryOptions = {};
#if defined(_DEBUG)
    if (IsGpuDebugEnabled()) {
        factoryOptions.debugLevel = D2D1_DEBUG_LEVEL_INFORMATION;
    }
#endif

    HRESULT hr = D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED,
        __uuidof(ID2D1Factory3),
        &factoryOptions,
        reinterpret_cast<void**>(d2dFactory_.GetAddressOf()));

    if (FAILED(hr)) {
        return false;
    }

    // Create D3D11on12 device
    ComPtr<IUnknown> commandQueues[] = { commandQueue_.Get() };
    UINT d3d11DeviceFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#if defined(_DEBUG)
    if (IsGpuDebugEnabled()) {
        d3d11DeviceFlags |= D3D11_CREATE_DEVICE_DEBUG;
    }
#endif

    hr = D3D11On12CreateDevice(
        device_.Get(),
        d3d11DeviceFlags,
        nullptr, 0,  // Feature levels
        reinterpret_cast<IUnknown**>(commandQueues),
        1,
        0,
        &d3d11Device_,
        &d3d11Context_,
        nullptr);

    if (FAILED(hr)) {
        return false;
    }

    hr = d3d11Device_.As(&d3d11On12Device_);
    if (FAILED(hr)) {
        return false;
    }

    // Get DXGI device from D3D11 device
    ComPtr<IDXGIDevice> dxgiDevice;
    hr = d3d11Device_.As(&dxgiDevice);
    if (FAILED(hr)) {
        return false;
    }

    // Create D2D device
    hr = d2dFactory_->CreateDevice(dxgiDevice.Get(), &d2dDevice_);
    if (FAILED(hr)) {
        return false;
    }

    // Register custom D2D1 effects
    ComPtr<ID2D1Factory1> factory1;
    hr = d2dFactory_.As(&factory1);
    if (SUCCEEDED(hr) && factory1) {
        LiquidGlassEffect::Register(factory1.Get());
        TransitionShaderEffect::Register(factory1.Get());
        // Registration failure is non-fatal; effects will fall back gracefully
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
    const JaliumGradientStop* stops, uint32_t stopCount)
{
    return new D3D12LinearGradientBrush(startX, startY, endX, endY, stops, stopCount);
}

Brush* D3D12Backend::CreateRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount)
{
    return new D3D12RadialGradientBrush(centerX, centerY, radiusX, radiusY, originX, originY, stops, stopCount);
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
    auto bitmap = new D3D12Bitmap(width, height);
    bitmap->SetBitmapData(pixels.data(), static_cast<uint32_t>(pixels.size()));

    return bitmap;
}

IRenderBackend* CreateD3D12Backend() {
    return new D3D12Backend();
}

} // namespace jalium
