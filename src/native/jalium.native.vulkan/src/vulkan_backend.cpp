#include "vulkan_backend.h"

#include "vulkan_render_target.h"
#include "vulkan_runtime.h"

#include <cstring>

#ifndef _WIN32
#define STB_IMAGE_IMPLEMENTATION
#define STBI_NO_STDIO
#define STBI_FAILURE_USERMSG
#include <stb_image.h>
#endif

#ifdef _WIN32
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>
#endif

namespace jalium {

namespace {

#ifndef _WIN32
Bitmap* DecodeBitmapWithStb(const uint8_t* data, uint32_t dataSize)
{
    int width = 0;
    int height = 0;
    int channels = 0;
    stbi_uc* decodedPixels = stbi_load_from_memory(
        data,
        static_cast<int>(dataSize),
        &width,
        &height,
        &channels,
        STBI_rgb_alpha);
    if (!decodedPixels || width <= 0 || height <= 0) {
        if (decodedPixels) {
            stbi_image_free(decodedPixels);
        }
        return nullptr;
    }

    const size_t pixelDataSize = static_cast<size_t>(width) * static_cast<size_t>(height) * 4u;
    if (pixelDataSize == 0) {
        stbi_image_free(decodedPixels);
        return nullptr;
    }

    std::vector<uint8_t> bgraPixels(pixelDataSize, 0);
    // STBI_rgb_alpha guarantees decodedPixels has exactly pixelDataSize bytes (RGBA).
    // Convert RGBA → BGRA in-place for D2D/Vulkan surface compatibility.
    for (size_t offset = 0; offset + 3 < pixelDataSize; offset += 4u) {
        bgraPixels[offset + 0] = decodedPixels[offset + 2];
        bgraPixels[offset + 1] = decodedPixels[offset + 1];
        bgraPixels[offset + 2] = decodedPixels[offset + 0];
        bgraPixels[offset + 3] = decodedPixels[offset + 3];
    }

    stbi_image_free(decodedPixels);
    return new VulkanBitmap(static_cast<uint32_t>(width), static_cast<uint32_t>(height), std::move(bgraPixels));
}
#endif

} // namespace

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
#else
    // Initialize cross-platform text engine (FreeType + HarfBuzz)
    textEngine_ = std::make_unique<TextEngine>();
    JaliumResult textResult = textEngine_->Initialize();
    if (textResult != JALIUM_OK) {
        // Text engine is optional — degrade gracefully
        textEngine_.reset();
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

    auto* rt = new VulkanRenderTarget(this, *surface, width, height, false);
    if (!rt->IsInitialized()) {
        delete rt;
        return nullptr;
    }
    return rt;
}

RenderTarget* VulkanBackend::CreateRenderTargetForCompositionSurface(
    const JaliumSurfaceDescriptor* surface,
    int32_t width,
    int32_t height)
{
    if (!Initialize() || !surface || surface->handle0 == 0) {
        return nullptr;
    }

    auto* rt = new VulkanRenderTarget(this, *surface, width, height, true);
    if (!rt->IsInitialized()) {
        delete rt;
        return nullptr;
    }
    return rt;
}

Brush* VulkanBackend::CreateSolidBrush(float r, float g, float b, float a)
{
    return new VulkanSolidBrush(r, g, b, a);
}

Brush* VulkanBackend::CreateLinearGradientBrush(
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t /*spreadMethod*/)
{
    return new VulkanLinearGradientBrush(startX, startY, endX, endY, stops, stopCount);
}

Brush* VulkanBackend::CreateRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t /*spreadMethod*/)
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
    if (!Initialize()) {
        return nullptr;
    }

    // Use cross-platform text engine if available
    if (textEngine_) {
        return textEngine_->CreateTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
    }

    // Fallback: stub text format with approximate metrics
    return new VulkanTextFormat(textEngine_.get(), fontFamily, fontSize, fontWeight, fontStyle);
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
    return DecodeBitmapWithStb(data, dataSize);
#endif
}

Bitmap* VulkanBackend::CreateBitmapFromPixels(const uint8_t* pixels, uint32_t width, uint32_t height, uint32_t stride)
{
    if (!Initialize() || !pixels || width == 0 || height == 0 || stride < width * 4u) {
        return nullptr;
    }

    const size_t rowBytes = static_cast<size_t>(width) * 4u;
    std::vector<uint8_t> packedPixels(static_cast<size_t>(width) * static_cast<size_t>(height) * 4u, 0);
    for (uint32_t row = 0; row < height; ++row) {
        const auto* sourceRow = pixels + static_cast<size_t>(row) * stride;
        auto* destRow = packedPixels.data() + static_cast<size_t>(row) * rowBytes;
        std::memcpy(destRow, sourceRow, rowBytes);
    }

    return new VulkanBitmap(width, height, std::move(packedPixels));
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
