#pragma once

#include "jalium_video_surface.h"
#include "vulkan_minimal.h"

namespace jalium::vulkan_video {

constexpr uint32_t Fourcc(char a, char b, char c, char d)
{
    return uint32_t(uint8_t(a)) | (uint32_t(uint8_t(b)) << 8) |
           (uint32_t(uint8_t(c)) << 16) | (uint32_t(uint8_t(d)) << 24);
}

struct Format {
    VkFormat vk = VK_FORMAT_UNDEFINED;
    uint32_t planes = 1;
    uint32_t bytesPerLumaPixel = 4;
    bool ycbcr = false;
    bool opaque = false;
};

inline bool Describe(const JaliumVideoSurfaceDescriptor& descriptor, Format& format)
{
    format = {};
    switch (descriptor.drm_fourcc) {
    case Fourcc('A','R','2','4'): format.vk = VK_FORMAT_B8G8R8A8_UNORM; break;
    case Fourcc('X','R','2','4'):
        format.vk = VK_FORMAT_B8G8R8A8_UNORM; format.opaque = true; break;
    case Fourcc('A','B','2','4'): format.vk = VK_FORMAT_R8G8B8A8_UNORM; break;
    case Fourcc('X','B','2','4'):
        format.vk = VK_FORMAT_R8G8B8A8_UNORM; format.opaque = true; break;
    case Fourcc('N','V','1','2'):
        format.vk = VK_FORMAT_G8_B8R8_2PLANE_420_UNORM;
        format.planes = 2; format.bytesPerLumaPixel = 1; format.ycbcr = true;
        if (descriptor.format_hint != JALIUM_VS_FORMAT_NV12) return false;
        break;
    case Fourcc('P','0','1','0'):
        format.vk = VK_FORMAT_G10X6_B10X6R10X6_2PLANE_420_UNORM_3PACK16;
        format.planes = 2; format.bytesPerLumaPixel = 2; format.ycbcr = true;
        if (descriptor.format_hint != JALIUM_VS_FORMAT_P010) return false;
        break;
    default: return false;
    }
    if (!descriptor.width || !descriptor.height ||
        descriptor.plane_count < format.planes ||
        descriptor.plane_count > JALIUM_VIDEO_SURFACE_MAX_PLANES) return false;
    if (format.ycbcr && ((descriptor.width | descriptor.height) & 1u)) return false;
    if (format.ycbcr &&
        (descriptor.color_space < JALIUM_VS_COLOR_BT601 ||
         descriptor.color_space > JALIUM_VS_COLOR_BT2020 ||
         (descriptor.descriptor_flags & JALIUM_VS_FLAG_HDR_TRANSFER))) return false;
    for (uint32_t i = 0; i < descriptor.plane_count; ++i) {
        const auto& plane = descriptor.planes[i];
        if (plane.fd < 0 || !plane.stride_bytes ||
            plane.modifier != descriptor.planes[0].modifier) return false;
        if (plane.size_bytes && plane.offset_bytes >= plane.size_bytes) return false;
        // Vendor modifiers define opaque memory-plane layouts (including
        // compression). Only LINEAR has a row-by-row byte footprint here.
        if (i < format.planes && plane.modifier == 0) {
            const uint64_t rowBytes = uint64_t(descriptor.width) * format.bytesPerLumaPixel;
            const uint64_t rows = i == 0 ? descriptor.height : descriptor.height / 2;
            if (plane.stride_bytes < rowBytes) return false;
            const uint64_t end = uint64_t(plane.offset_bytes) +
                (rows - 1) * plane.stride_bytes + rowBytes;
            if (plane.size_bytes && end > plane.size_bytes) return false;
        }
    }
    return true;
}

inline VkSamplerYcbcrModelConversion Model(uint32_t colorSpace)
{
    return colorSpace == JALIUM_VS_COLOR_BT601 ? VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_601 :
           colorSpace == JALIUM_VS_COLOR_BT2020 ? VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_2020 :
           VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_709;
}

} // namespace jalium::vulkan_video
