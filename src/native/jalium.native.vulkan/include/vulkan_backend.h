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

    /// Override: reports the registered device generation's lost state.
    /// Vulkan has no GetDeviceRemovedReason-style query — the only signal is a
    /// VK_ERROR_DEVICE_LOST from an actual call — so the render targets latch
    /// the loss into their VulkanDeviceGeneration and this override surfaces
    /// it (the base default would report a permanently healthy device,
    /// leaving jalium_context_check_device_status blind on Vulkan). With no
    /// generation registered yet there is nothing to be lost → OK, matching
    /// the pre-first-window state.
    ///
    /// Semantics caveat (differs from D3D12's single global device): Vulkan
    /// creates one VkDevice per render target, and this reports only the MOST
    /// RECENTLY REGISTERED generation — with multiple windows it can miss an
    /// older window's loss or report a loss the other windows don't share.
    /// The recovery chain does not consume it (each RT's BeginDraw/EndDraw
    /// gate reports its own precise state); this exists for ABI completeness
    /// and diagnostics. It is also passively latched, not an active probe: a
    /// dead-but-unobserved device still reports OK until some call fails.
    JaliumResult CheckDeviceStatus() override;

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

    /// Called by every VulkanRenderTarget::Impl once it has a live device, so
    /// backend-owned resources (ink layers / brush shaders) can be created on
    /// the same device the window composites with. Most-recent registration
    /// wins. A registration with the same device is a no-op; a different
    /// device drops only the backend's reference to the previous brush
    /// pipeline — ink objects still alive on the old generation keep it (and
    /// its VkDevice) alive through their own shared_ptr references, so they
    /// stay destructible and CPU-readable instead of dangling.
    void RegisterDeviceContext(std::shared_ptr<VulkanDeviceGeneration> generation);

    /// Called by VulkanRenderTarget::Impl::Destroy. If the dying render
    /// target's generation is the one currently registered, the backend drops
    /// its references (generation + brush pipeline) so (a) a closed window's
    /// VkDevice/VkInstance don't stay resident behind the backend's pin, and
    /// (b) ink layers are never silently created on a dead window's orphan
    /// device. Ink objects already alive keep their own shared_ptr keep-alive;
    /// with no generation registered, new ink requests return the
    /// "unsupported" sentinel and managed falls back to CPU ink until the next
    /// render target registers. A different registered generation (another
    /// window superseded this one) is left untouched.
    void UnregisterDeviceContext(const std::shared_ptr<VulkanDeviceGeneration>& generation);

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
    // brushPipeline_ is lazily built on the registered device generation the
    // first time an ink layer or brush shader is requested.
    // EnsureBrushPipeline() returns it (or nullptr when no device / lost
    // generation / DXC). Both the pipeline and the generation are shared_ptr:
    // ink objects handed out to managed code co-own them, so swapping the
    // registered generation never dangles a live ink object. Guarded by
    // inkMutex_ because ink ops can in principle arrive from more than one
    // window thread.
    std::shared_ptr<VulkanBrushPipeline> EnsureBrushPipeline();
    std::mutex inkMutex_;
    std::shared_ptr<VulkanDeviceGeneration> deviceGeneration_;
    std::shared_ptr<VulkanBrushPipeline> brushPipeline_;
    bool brushPipelineAttempted_ = false;
};

IRenderBackend* CreateVulkanBackend();

} // namespace jalium
