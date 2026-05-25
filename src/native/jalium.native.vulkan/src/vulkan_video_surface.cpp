// VulkanVideoSurface — stage 2 BGRA8 video staging on Vulkan. Mirrors the
// D3D12 path's role (wraps the existing bitmap upload machinery + exposes a
// Lock/Unlock pair) but routes through VulkanBitmap.UpdatePackedPixels so
// the shared_ptr<vector> COW invariant the framework relies on for safe
// GpuReplayCommand replay stays intact — any in-flight command holding the
// previous frame's pixels keeps that buffer alive on its own.
//
// Stage 5 will replace this with VK_KHR_external_memory_win32 / dma-buf /
// AHardwareBuffer imports to skip the CPU staging hop entirely.

#include "vulkan_resources.h"
#include "vulkan_backend.h"
#include "vulkan_render_target.h"

#include <cstring>

namespace jalium {

VulkanVideoSurface::VulkanVideoSurface(uint32_t width, uint32_t height)
    : bitmap(width, height, std::vector<uint8_t>(static_cast<size_t>(width) * height * 4u, 0))
    , staging(static_cast<size_t>(width) * height * 4u, 0)
{
}

bool VulkanVideoSurface::Lock(uint8_t** outPtr, uint32_t* outStride)
{
    if (!outPtr || !outStride) return false;
    if (staging.empty()) return false;
    *outPtr    = staging.data();
    *outStride = bitmap.GetWidth() * 4u;
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
                              bitmap.GetWidth() * 4u);
    return true;
}

// ─── Backend factory ──────────────────────────────────────────────────────

VideoSurface* VulkanBackend::CreateVideoSurface(uint32_t width, uint32_t height,
                                                 uint32_t /*formatHint*/)
{
    if (width == 0 || height == 0) return nullptr;
    return new VulkanVideoSurface(width, height);
}

VideoSurface* VulkanBackend::WrapExternalVideoSurface(
    const JaliumVideoSurfaceDescriptor* descriptor)
{
    if (!descriptor) return nullptr;

    if (descriptor->kind == JALIUM_VS_KIND_AHARDWAREBUFFER) {
        // Stage 4 真填占位:Android MediaCodec output → AHardwareBuffer →
        // Vulkan VK_ANDROID_external_memory_android_hardware_buffer:
        //   1. vkGetAndroidHardwareBufferPropertiesANDROID(buffer) 拿 memory
        //      requirements + external format
        //   2. vkCreateImage with VkExternalMemoryImageCreateInfo + (optional)
        //      VkExternalFormatANDROID for non-RGB hardware buffer formats
        //   3. vkAllocateMemory with VkImportAndroidHardwareBufferInfoANDROID
        //      pointing at descriptor->handle0 = AHardwareBuffer*
        //   4. vkBindImageMemory + create VkImageView with the YUV sampler
        //      conversion (VkSamplerYcbcrConversion) when external format
        //   5. wrap in ImportedVulkanVideoSurface : VideoSurface,DrawVideoSurface
        //      uses a YUV-aware sampler in the bitmap shader
        //
        // 当前返 nullptr → externalImportFails 计数 +1 → fallback 到 stage 2
        // VulkanVideoSurface BGRA staging(SIMD YUV→BGRA 已在 and_yuv_*.cpp)。
        return nullptr;
    }

    if (descriptor->kind == JALIUM_VS_KIND_VK_IMAGE) {
        // Stage 5 占位:VK_KHR_external_memory_win32 (Windows D3D11 shared) /
        //                VK_EXT_external_memory_dma_buf (Linux VAAPI)。
        // 跟 AHARDWAREBUFFER 模式类似,只是 import handle 类型不同。
        return nullptr;
    }

    // 其它 kind:BGRA8_CPU 走 CreateVideoSurface;D3D11_SHARED / IOSurface /
    // Metal / CVPixelBuffer 是 D3D12 / Apple 后端专用。
    return nullptr;
}

// ─── Render-target draw routing ──────────────────────────────────────────

void VulkanRenderTarget::DrawVideoSurface(VideoSurface* surface,
                                           float x, float y, float w, float h,
                                           float opacity, int scalingMode)
{
    if (!surface) return;
    auto* vs = dynamic_cast<VulkanVideoSurface*>(surface);
    if (!vs) return;
    // The wrapped bitmap already received the new pixels via Unlock; the
    // existing scaling-aware DrawBitmap path handles staging-buffer →
    // VkImage upload + sampler binding + composite.
    DrawBitmap(&vs->bitmap, x, y, w, h, opacity, scalingMode);
}

} // namespace jalium
