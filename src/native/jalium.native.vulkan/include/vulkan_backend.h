#pragma once

#include "jalium_backend.h"
#include "vulkan_minimal.h"
#include "vulkan_resources.h"
#include "vulkan_ink_layer.h"

#ifndef _WIN32
#include "text_engine.h"
#endif

#include <memory>
#include <mutex>

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

    /// Override: returns the GPU adapter that the first render target picked
    /// during VulkanRenderTarget::Impl::Initialize. VulkanBackend itself does
    /// not own a VkInstance / VkPhysicalDevice (each render target creates its
    /// own — see <see cref="VulkanRenderTarget::Impl"/>), so the first RT
    /// publishes the chosen adapter back here via
    /// <see cref="RegisterAdapterInfo"/> and subsequent GetAdapterInfo calls
    /// just hand out that cached snapshot. Returns NOT_SUPPORTED when no RT
    /// has been created yet (zero-filled out struct).
    JaliumResult GetAdapterInfo(JaliumAdapterInfo* out) const override;

    /// Called by VulkanRenderTarget::Impl after it has picked its
    /// physical device. Copies the relevant fields out of the supplied
    /// Vulkan properties / memory properties into the backend's cached
    /// JaliumAdapterInfo so subsequent GetAdapterInfo calls succeed without
    /// re-enumerating physical devices.
    ///
    /// Idempotent: a second call with the same physicalDevice is a no-op;
    /// a second call with a different physicalDevice overwrites the cache
    /// (DevTools will surface whichever adapter the most recently created
    /// render target landed on, which matches user expectation under
    /// hybrid-graphics adapter switching).
    void RegisterAdapterInfo(VkPhysicalDevice physicalDevice,
                             const VkPhysicalDeviceProperties& props,
                             const VkPhysicalDeviceMemoryProperties& memProps);

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
    VideoSurface* CreateVideoSurface(uint32_t width, uint32_t height, uint32_t formatHint) override;
    VideoSurface* WrapExternalVideoSurface(const JaliumVideoSurfaceDescriptor* descriptor) override;

    // ── InkCanvas GPU pipeline (parity with D3D12 d3d12_ink_layer.cpp) ──────
    // The Vulkan backend owns no device, so the first render target publishes
    // its device context here (RegisterDeviceContext); the ink-layer subsystem
    // then builds its GPU objects on that shared device. All ink methods return
    // the "unsupported" sentinel (nullptr / -1 / no-op) until a device context
    // is registered and DXC is available — managed code falls back to CPU ink.
    void* CreateInkLayerBitmap(uint32_t width, uint32_t height) override;
    void DestroyInkLayerBitmap(void* bitmap) override;
    int32_t ResizeInkLayerBitmap(void* bitmap, uint32_t width, uint32_t height) override;
    void ClearInkLayerBitmap(void* bitmap, float r, float g, float b, float a) override;
    void* CreateBrushShader(const char* shaderKey, const char* brushMainHlsl, int32_t blendMode) override;
    void DestroyBrushShader(void* shader) override;
    int32_t DispatchBrush(void* bitmap, void* shader,
                          const void* strokePoints, uint32_t pointCount,
                          const void* constants,
                          const void* extraParams, uint32_t extraParamsSize) override;

    /// Called by the first VulkanRenderTarget::Impl once it has a live device,
    /// so backend-owned resources (ink layers / brush shaders) can be created
    /// on the same device the window composites with. Idempotent; a second
    /// registration with the same device is a no-op.
    void RegisterDeviceContext(const VulkanDeviceContext& ctx);

#ifndef _WIN32
    TextEngine* GetTextEngine() const { return textEngine_.get(); }
#endif

private:
#ifndef _WIN32
    std::unique_ptr<TextEngine> textEngine_;
#endif
    bool initialized_ = false;

    /// Cached adapter description published by the first render target's
    /// Impl::Initialize. Mutable so the const GetAdapterInfo override can
    /// read it (writes happen exclusively from RegisterAdapterInfo, which is
    /// non-const). adapterInfoValid_ guards against handing out a zero
    /// struct as if it were real adapter data.
    mutable JaliumAdapterInfo adapterInfo_{};
    mutable bool adapterInfoValid_ = false;
    // Identity of the physical device whose props are cached in adapterInfo_.
    // Lets RegisterAdapterInfo short-circuit when re-publishing the same GPU
    // (the typical case once the first RT is alive).
    mutable VkPhysicalDevice cachedPhysicalDevice_ = VK_NULL_HANDLE;

    // ── Backend-owned ink pipeline ──────────────────────────────────────────
    // brushPipeline_ is lazily built on the registered device the first time an
    // ink layer or brush shader is requested. EnsureBrushPipeline() returns it
    // (or nullptr when no device / DXC). Guarded by inkMutex_ because ink ops
    // can in principle arrive from more than one window thread.
    VulkanBrushPipeline* EnsureBrushPipeline();
    std::mutex inkMutex_;
    VulkanDeviceContext deviceContext_{};
    std::unique_ptr<VulkanBrushPipeline> brushPipeline_;
    bool brushPipelineAttempted_ = false;
};

IRenderBackend* CreateVulkanBackend();

} // namespace jalium
