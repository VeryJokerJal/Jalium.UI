// VulkanVideoSurface — stage 2 BGRA8 video staging on Vulkan. Mirrors the
// D3D12 path's role (wraps the existing bitmap upload machinery + exposes a
// Lock/Unlock pair) but routes through VulkanBitmap.UpdatePackedPixels so
// the shared_ptr<vector> COW invariant the framework relies on for safe
// GpuReplayCommand replay stays intact — any in-flight command holding the
// previous frame's pixels keeps that buffer alive on its own.
//
// Linux dma-buf import and YCbCr conversion live in vulkan_video_import.cpp.

#include "vulkan_resources.h"
#include "vulkan_backend.h"
#include "vulkan_render_target.h"
#include "vulkan_ink_layer.h"

#include <cstring>

#if defined(__linux__) && !defined(__ANDROID__)
#include <unistd.h>
#endif

namespace jalium {

namespace {

std::vector<uint8_t> CreateVideoPixelBuffer(uint32_t width, uint32_t height)
{
    PackedBgraLayout layout{};
    if (!TryComputeTightlyPackedBgraLayout(width, height, layout)) {
        return {};
    }
    return std::vector<uint8_t>(layout.packedBytes, 0);
}

} // namespace

VulkanVideoSurface::VulkanVideoSurface(uint32_t width, uint32_t height)
    : bitmap(width, height, CreateVideoPixelBuffer(width, height))
    , staging(CreateVideoPixelBuffer(width, height))
{
}

bool VulkanVideoSurface::Lock(uint8_t** outPtr, uint32_t* outStride)
{
    if (!outPtr || !outStride) return false;
    if (staging.empty()) return false;
    *outPtr    = staging.data();
    *outStride = static_cast<uint32_t>(
        static_cast<uint64_t>(bitmap.GetWidth()) * 4u);
    return true;
}

bool VulkanVideoSurface::Unlock(const JaliumVideoSurfaceDirtyRect* /*dirty*/)
{
    // Hand the staging buffer to the bitmap. UpdatePackedPixels allocates
    // a fresh shared_ptr<vector> inside, so any in-flight GpuReplayCommand
    // holding the previous frame's pixels stays valid until that command
    // completes and drops its ref — see [[project_vulkan_bitmap_shared_ptr_cow]].
    bitmap.UpdatePackedPixels(staging.data(),
                              bitmap.GetWidth(), bitmap.GetHeight(),
                              static_cast<uint32_t>(
                                  static_cast<uint64_t>(bitmap.GetWidth()) * 4u));
    return true;
}

// ─── Backend factory ──────────────────────────────────────────────────────

VideoSurface* VulkanBackend::CreateVideoSurface(uint32_t width, uint32_t height,
                                                 uint32_t /*formatHint*/)
{
    PackedBgraLayout layout{};
    if (!TryComputeTightlyPackedBgraLayout(width, height, layout)) return nullptr;
    return new VulkanVideoSurface(width, height);
}

VideoSurface* VulkanBackend::WrapExternalVideoSurface(
    const JaliumVideoSurfaceDescriptor* descriptor)
{
    if (!descriptor) return nullptr;

    if (descriptor->kind == JALIUM_VS_KIND_LINUX_DMABUF ||
        descriptor->kind == JALIUM_VS_KIND_D3D11_SHARED ||
        descriptor->kind == JALIUM_VS_KIND_AHARDWAREBUFFER) {
        auto generation = AcquireExternalImportGeneration();
        if (!generation) return nullptr;
        return VulkanImportedVideoSurface::Create(std::move(generation), *descriptor);
    }

    // Raw VkImage handles lack device/ownership metadata; never guess that
    // they belong to this generation. Unsupported kinds use CPU staging.
    return nullptr;
}

// ─── Render-target draw routing ──────────────────────────────────────────

void VulkanRenderTarget::DrawVideoSurface(VideoSurface* surface,
                                           float x, float y, float w, float h,
                                           float opacity, int scalingMode)
{
    if (!surface || w <= 0 || h <= 0 || opacity <= 0 || GetCurrentOpacity() <= 0) return;
    auto* vs = dynamic_cast<VulkanVideoSurface*>(surface);
    if (!vs) {
        auto* imported = dynamic_cast<VulkanImportedVideoSurface*>(surface);
        if (imported) {
            TouchFrame();
            if (!TryRecordGpuExternalVideoCommand(imported, x, y, w, h, opacity, scalingMode)) {
                if (auto pixels = imported->ReadbackPixels()) {
                    VulkanBitmap bitmap(imported->GetWidth(), imported->GetHeight(), std::move(pixels));
                    DrawBitmap(&bitmap, x, y, w, h, opacity, scalingMode);
                }
            }
        }
        return;
    }
    // The wrapped bitmap already received the new pixels via Unlock; the
    // existing scaling-aware DrawBitmap path handles staging-buffer →
    // VkImage upload + sampler binding + composite.
    DrawBitmap(&vs->bitmap, x, y, w, h, opacity, scalingMode);
}

} // namespace jalium
