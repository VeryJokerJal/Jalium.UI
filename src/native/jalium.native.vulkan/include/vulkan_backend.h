#pragma once

#include "jalium_backend.h"
#include "vulkan_resources.h"

#ifdef _WIN32
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>
#endif

namespace jalium {

/// Vulkan backend scaffold.
/// The registration/export surface is wired up so a concrete renderer can be
/// dropped in without reshaping the ABI again.
class VulkanBackend : public IRenderBackend {
public:
    VulkanBackend() = default;

    bool Initialize();

    JaliumBackend GetType() const override { return JALIUM_BACKEND_VULKAN; }
    const wchar_t* GetName() const override { return L"Vulkan"; }

    RenderTarget* CreateRenderTarget(void* hwnd, int32_t width, int32_t height) override;
    RenderTarget* CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height) override;
    RenderTarget* CreateRenderTargetForSurface(
        const JaliumSurfaceDescriptor* surface,
        int32_t width,
        int32_t height) override;
    RenderTarget* CreateRenderTargetForCompositionSurface(
        const JaliumSurfaceDescriptor* surface,
        int32_t width,
        int32_t height) override;
    Brush* CreateSolidBrush(float r, float g, float b, float a) override;
    Brush* CreateLinearGradientBrush(
        float startX, float startY, float endX, float endY,
        const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0) override;
    Brush* CreateRadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0) override;
    TextFormat* CreateTextFormat(
        const wchar_t* fontFamily,
        float fontSize,
        int32_t fontWeight,
        int32_t fontStyle) override;
    Bitmap* CreateBitmapFromMemory(const uint8_t* data, uint32_t dataSize) override;
    Bitmap* CreateBitmapFromPixels(const uint8_t* pixels, uint32_t width, uint32_t height, uint32_t stride) override;

#ifdef _WIN32
    IDWriteFactory* GetDWriteFactory() const { return dwriteFactory_.Get(); }
#endif

private:
#ifdef _WIN32
    Microsoft::WRL::ComPtr<IDWriteFactory> dwriteFactory_;
    Microsoft::WRL::ComPtr<IWICImagingFactory> wicFactory_;
#endif
    bool initialized_ = false;
};

IRenderBackend* CreateVulkanBackend();

} // namespace jalium
