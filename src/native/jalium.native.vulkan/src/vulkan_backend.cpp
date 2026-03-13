#include "vulkan_backend.h"

#include "vulkan_render_target.h"
#include "vulkan_runtime.h"

#ifdef _WIN32
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>
#endif

namespace jalium {

bool VulkanBackend::Initialize()
{
    if (initialized_) {
        return true;
    }

    if (!IsExperimentalVulkanEnabled() || !IsVulkanRuntimeAvailable()) {
        return false;
    }

#ifdef _WIN32
    const HRESULT dwriteHr = DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED,
        __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(dwriteFactory_.GetAddressOf()));
    if (FAILED(dwriteHr)) {
        return false;
    }

    const HRESULT wicHr = CoCreateInstance(
        CLSID_WICImagingFactory,
        nullptr,
        CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(&wicFactory_));
    if (FAILED(wicHr)) {
        return false;
    }
#endif

    initialized_ = true;
    return true;
}

RenderTarget* VulkanBackend::CreateRenderTarget(void* hwnd, int32_t width, int32_t height)
{
    JaliumSurfaceDescriptor surface {};
    surface.platform = JALIUM_PLATFORM_WINDOWS;
    surface.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
    surface.handle0 = reinterpret_cast<intptr_t>(hwnd);
    return CreateRenderTargetForSurface(&surface, width, height);
}

RenderTarget* VulkanBackend::CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height)
{
    JaliumSurfaceDescriptor surface {};
    surface.platform = JALIUM_PLATFORM_WINDOWS;
    surface.kind = JALIUM_SURFACE_KIND_COMPOSITION_TARGET;
    surface.handle0 = reinterpret_cast<intptr_t>(hwnd);
    return CreateRenderTargetForCompositionSurface(&surface, width, height);
}

RenderTarget* VulkanBackend::CreateRenderTargetForSurface(
    const JaliumSurfaceDescriptor* surface,
    int32_t width,
    int32_t height)
{
    if (!Initialize() || !surface || surface->handle0 == 0) {
        return nullptr;
    }

    return new VulkanRenderTarget(this, *surface, width, height, false);
}

RenderTarget* VulkanBackend::CreateRenderTargetForCompositionSurface(
    const JaliumSurfaceDescriptor* surface,
    int32_t width,
    int32_t height)
{
    if (!Initialize() || !surface || surface->handle0 == 0) {
        return nullptr;
    }

    return new VulkanRenderTarget(this, *surface, width, height, true);
}

Brush* VulkanBackend::CreateSolidBrush(float r, float g, float b, float a)
{
    return new VulkanSolidBrush(r, g, b, a);
}

Brush* VulkanBackend::CreateLinearGradientBrush(
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops, uint32_t stopCount)
{
    return new VulkanLinearGradientBrush(startX, startY, endX, endY, stops, stopCount);
}

Brush* VulkanBackend::CreateRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount)
{
    return new VulkanRadialGradientBrush(centerX, centerY, radiusX, radiusY, originX, originY, stops, stopCount);
}

TextFormat* VulkanBackend::CreateTextFormat(
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
{
#ifdef _WIN32
    if (!Initialize() || !dwriteFactory_) {
        return nullptr;
    }

    return new VulkanTextFormat(dwriteFactory_.Get(), fontFamily, fontSize, fontWeight, fontStyle);
#else
    return new VulkanTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
#endif
}

Bitmap* VulkanBackend::CreateBitmapFromMemory(const uint8_t* data, uint32_t dataSize)
{
    if (!Initialize() || !data || dataSize == 0) {
        return nullptr;
    }

#ifdef _WIN32
    if (!wicFactory_) {
        return nullptr;
    }

    Microsoft::WRL::ComPtr<IWICStream> stream;
    HRESULT hr = wicFactory_->CreateStream(&stream);
    if (FAILED(hr)) return nullptr;

    hr = stream->InitializeFromMemory(const_cast<uint8_t*>(data), dataSize);
    if (FAILED(hr)) return nullptr;

    Microsoft::WRL::ComPtr<IWICBitmapDecoder> decoder;
    hr = wicFactory_->CreateDecoderFromStream(stream.Get(), nullptr, WICDecodeMetadataCacheOnDemand, &decoder);
    if (FAILED(hr)) return nullptr;

    Microsoft::WRL::ComPtr<IWICBitmapFrameDecode> frame;
    hr = decoder->GetFrame(0, &frame);
    if (FAILED(hr)) return nullptr;

    Microsoft::WRL::ComPtr<IWICFormatConverter> converter;
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

    UINT width = 0;
    UINT height = 0;
    hr = converter->GetSize(&width, &height);
    if (FAILED(hr) || width == 0 || height == 0) return nullptr;

    std::vector<uint8_t> pixels(width * height * 4);
    hr = converter->CopyPixels(nullptr, width * 4, static_cast<UINT>(pixels.size()), pixels.data());
    if (FAILED(hr)) return nullptr;

    return new VulkanBitmap(width, height, std::move(pixels));
#else
    return nullptr;
#endif
}

IRenderBackend* CreateVulkanBackend()
{
    auto* backend = new VulkanBackend();
    if (!backend->Initialize()) {
        delete backend;
        return nullptr;
    }

    return backend;
}

} // namespace jalium
