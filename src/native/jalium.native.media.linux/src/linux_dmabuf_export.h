#pragma once
#include "jalium_media.h"
#include "jalium_video_surface.h"
#include <cerrno>
#include <cstdlib>
#include <cstring>
#include <initializer_list>
#include <fcntl.h>
#include <unistd.h>

// Include after GStreamer headers (or gstreamer_loader.h in the optional
// runtime-loaded backend). Tests use real GStreamer objects and this same
// metadata/layout exporter; no Vulkan or hardware decoder is needed.
namespace jalium::media::dmabuf {
inline void ResetDescriptor(jalium_video_decoder_gpu_descriptor_t* descriptor) noexcept
{
    std::memset(descriptor, 0, sizeof(*descriptor));
    for (auto& plane : descriptor->planes) plane.fd = -1;
    descriptor->acquire_fence_fd = -1;
}
inline void ClosePlanes(jalium_video_decoder_gpu_descriptor_t* descriptor) noexcept
{
    for (auto& plane : descriptor->planes) {
        if (plane.fd >= 0) close(plane.fd);
        plane.fd = -1;
    }
}
constexpr uint32_t DrmFourcc(char a, char b, char c, char d) noexcept
{
    return static_cast<uint32_t>(static_cast<uint8_t>(a)) |
           (static_cast<uint32_t>(static_cast<uint8_t>(b)) << 8) |
           (static_cast<uint32_t>(static_cast<uint8_t>(c)) << 16) |
           (static_cast<uint32_t>(static_cast<uint8_t>(d)) << 24);
}

inline uint32_t DrmFormatFromCaps(
    const GstStructure* structure,
    uint64_t& modifier,
    jalium_pixel_format_t requested_format,
    uint32_t& format_hint) noexcept
{
    modifier = 0;
    format_hint = 0;
    const char* format = structure
        ? gst_structure_get_string(structure, "format") : nullptr;
    const char* drm_format = structure
        ? gst_structure_get_string(structure, "drm-format") : nullptr;
    const char* value = drm_format && *drm_format ? drm_format : format;
    if (!value) return 0;

    const char* modifier_separator = std::strchr(value, ':');
    if (modifier_separator) {
        if (!modifier_separator[1] || modifier_separator[1] == '-') return 0;
        char* end = nullptr;
        errno = 0;
        const uint64_t parsed = std::strtoull(
            modifier_separator + 1, &end, 0);
        if (!end || end == modifier_separator + 1 || *end != '\0' || errno == ERANGE) return 0;
        modifier = parsed;
    }

    const auto matches = [value](const char* token) {
        const size_t length = std::strlen(token);
        return std::strncmp(value, token, length) == 0 &&
            (value[length] == '\0' || value[length] == ':');
    };
    if (matches("NV12")) {
        format_hint = 1; // JALIUM_VS_FORMAT_NV12
        return DrmFourcc('N', 'V', '1', '2');
    }
    if (matches("P010") ||
        matches("P010_10LE")) {
        format_hint = 2; // JALIUM_VS_FORMAT_P010
        return DrmFourcc('P', '0', '1', '0');
    }
    if (matches("AR24") ||
        std::strcmp(value, "BGRA") == 0) {
        format_hint = 0;
        return DrmFourcc('A', 'R', '2', '4');
    }
    if (matches("XR24") ||
        std::strcmp(value, "BGRx") == 0) {
        format_hint = 0;
        return DrmFourcc('X', 'R', '2', '4');
    }
    if (matches("AB24") ||
        std::strcmp(value, "RGBA") == 0) {
        format_hint = 0;
        return DrmFourcc('A', 'B', '2', '4');
    }
    if (matches("XB24") ||
        std::strcmp(value, "RGBx") == 0) {
        format_hint = 0;
        return DrmFourcc('X', 'B', '2', '4');
    }
    (void)requested_format;
    return 0;
}

inline bool IsVulkanRenderableDmabuf(
    const GstStructure* structure,
    uint32_t drm_fourcc,
    uint32_t format_hint,
    uint32_t plane_count,
    guint memory_count) noexcept
{
    const char* format = structure
        ? gst_structure_get_string(structure, "format") : nullptr;
    if (!format || plane_count == 0 || plane_count > JALIUM_VIDEO_SURFACE_MAX_PLANES ||
        memory_count == 0) {
        return false;
    }
    if (drm_fourcc == DrmFourcc('N', 'V', '1', '2'))
        return format_hint == JALIUM_VS_FORMAT_NV12 && plane_count >= 2;
    if (drm_fourcc == DrmFourcc('P', '0', '1', '0'))
        return format_hint == JALIUM_VS_FORMAT_P010 && plane_count >= 2;
    if (format_hint != JALIUM_VS_FORMAT_BGRA8) return false;
    return drm_fourcc == DrmFourcc('A', 'R', '2', '4') ||
           drm_fourcc == DrmFourcc('X', 'R', '2', '4') ||
           drm_fourcc == DrmFourcc('A', 'B', '2', '4') ||
           drm_fourcc == DrmFourcc('X', 'B', '2', '4');
}

inline jalium_media_status_t ExportLayout(
    GstSample* sample,
    jalium_pixel_format_t requested_format,
    jalium_video_decoder_gpu_descriptor_t* descriptor) noexcept
{
    if (!sample || !descriptor) return JALIUM_MEDIA_E_INVALID_ARG;
    ResetDescriptor(descriptor);
    GstCaps* caps = gst_sample_get_caps(sample);
    GstBuffer* buffer = gst_sample_get_buffer(sample);
    if (!caps || !buffer || gst_caps_get_size(caps) == 0) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
    GstCapsFeatures* features = gst_caps_get_features(caps, 0);
    if (!features ||
        !gst_caps_features_contains(features, GST_CAPS_FEATURE_MEMORY_DMABUF)) {
        return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    }

    const GstStructure* structure = gst_caps_get_structure(caps, 0);
    gint width = 0;
    gint height = 0;
    if (!structure ||
        !gst_structure_get_int(structure, "width", &width) ||
        !gst_structure_get_int(structure, "height", &height) ||
        width <= 0 || height <= 0) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    uint64_t modifier = 0;
    uint32_t format_hint = 0;
    const uint32_t drm_fourcc = DrmFormatFromCaps(
        structure, modifier, requested_format, format_hint);
    if (drm_fourcc == 0) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;

    GstVideoMeta* meta = gst_buffer_get_video_meta(buffer);
    uint32_t plane_count = meta && meta->n_planes > 0
        ? static_cast<uint32_t>(meta->n_planes)
        : (format_hint == 1 || format_hint == 2 ? 2u : 1u);
    const guint memory_count = gst_buffer_n_memory(buffer);
    if (!IsVulkanRenderableDmabuf(
            structure, drm_fourcc, format_hint, plane_count, memory_count)) {
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }

    // Old GStreamer releases cannot parse DMA_DRM into GstVideoInfo. Normalize
    // the known logical format only for metadata, while preserving the real
    // GstVideoMeta/DMABuf offsets and DRM modifier used for the import.
    GstVideoInfo video_info{};
    bool have_info = gst_video_info_from_caps(&video_info, caps);
    if (!have_info && meta) {
        const char* normalized_format = format_hint == JALIUM_VS_FORMAT_NV12 ? "NV12" :
            format_hint == JALIUM_VS_FORMAT_P010 ? "P010_10LE" : "BGRA";
        GstCaps* normalized = gst_caps_new_simple("video/x-raw",
            "format", G_TYPE_STRING, normalized_format,
            "width", G_TYPE_INT, width, "height", G_TYPE_INT, height, nullptr);
        if (!normalized) return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        GstStructure* output = gst_caps_get_structure(normalized, 0);
        for (const char* field : {"colorimetry", "chroma-site", "interlace-mode"}) {
            if (const char* value = gst_structure_get_string(structure, field))
                gst_structure_set(output, field, G_TYPE_STRING, value, nullptr);
        }
        have_info = gst_video_info_from_caps(&video_info, normalized);
        gst_caps_unref(normalized);
    }
    if (!have_info || (!meta && modifier != 0) ||
        GST_VIDEO_INFO_IS_INTERLACED(&video_info)) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    if (const char* color = gst_structure_get_string(structure, "colorimetry")) {
        // GstVideoInfo can silently replace malformed/unknown colorimetry with
        // defaults. Explicit metadata must parse without that substitution.
        if (!gst_video_colorimetry_from_string(&video_info.colorimetry, color))
            return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }
    if (const char* chroma = gst_structure_get_string(structure, "chroma-site")) {
        video_info.chroma_site = gst_video_chroma_from_string(chroma);
        if (video_info.chroma_site == GST_VIDEO_CHROMA_SITE_UNKNOWN)
            return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }

    uint32_t color_space = JALIUM_VS_COLOR_UNSPECIFIED;
    uint32_t color_flags = 0;
    if (format_hint == JALIUM_VS_FORMAT_NV12 || format_hint == JALIUM_VS_FORMAT_P010) {
        if ((width | height) & 1) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
        switch (video_info.colorimetry.matrix) {
        case GST_VIDEO_COLOR_MATRIX_BT601: color_space = JALIUM_VS_COLOR_BT601; break;
        case GST_VIDEO_COLOR_MATRIX_BT709: color_space = JALIUM_VS_COLOR_BT709; break;
        case GST_VIDEO_COLOR_MATRIX_BT2020: color_space = JALIUM_VS_COLOR_BT2020; break;
        default: return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
        }
        if (video_info.colorimetry.range == GST_VIDEO_COLOR_RANGE_0_255)
            color_flags |= JALIUM_VS_FLAG_FULL_RANGE;
        else if (video_info.colorimetry.range != GST_VIDEO_COLOR_RANGE_16_235)
            return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
        if (video_info.chroma_site & GST_VIDEO_CHROMA_SITE_ALT_LINE)
            return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
        if (video_info.chroma_site & GST_VIDEO_CHROMA_SITE_H_COSITED)
            color_flags |= JALIUM_VS_FLAG_CHROMA_COSITED_X;
        if (video_info.chroma_site & GST_VIDEO_CHROMA_SITE_V_COSITED)
            color_flags |= JALIUM_VS_FLAG_CHROMA_COSITED_Y;
        // HDR needs tone mapping, which this SDR swap-chain path does not own.
        switch (static_cast<int>(video_info.colorimetry.transfer)) {
        case GST_VIDEO_TRANSFER_GAMMA10:
        case GST_VIDEO_TRANSFER_GAMMA18:
        case GST_VIDEO_TRANSFER_GAMMA20:
        case GST_VIDEO_TRANSFER_GAMMA22:
        case GST_VIDEO_TRANSFER_BT709:
        case GST_VIDEO_TRANSFER_SMPTE240M:
        case GST_VIDEO_TRANSFER_SRGB:
        case GST_VIDEO_TRANSFER_GAMMA28:
        case GST_VIDEO_TRANSFER_BT2020_12:
        case GST_VIDEO_TRANSFER_ADOBERGB:
        // GStreamer 1.18 additions, with stable ABI enum values. Also accept
        // them when building against older headers and loading a newer runtime.
        case 13: // GST_VIDEO_TRANSFER_BT2020_10
        case 16: // GST_VIDEO_TRANSFER_BT601
            break;
        default: return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
        }
    }

    descriptor->kind = 7; // JALIUM_VS_KIND_LINUX_DMABUF
    descriptor->width = static_cast<uint32_t>(width);
    descriptor->height = static_cast<uint32_t>(height);
    descriptor->format_hint = format_hint;
    descriptor->plane_count = plane_count;
    descriptor->drm_fourcc = drm_fourcc;
    descriptor->color_space = color_space;
    descriptor->descriptor_flags = color_flags;

    for (uint32_t plane_index = 0; plane_index < plane_count; ++plane_index) {
        gsize global_offset = meta
            ? meta->offset[plane_index]
            : GST_VIDEO_INFO_PLANE_OFFSET(&video_info, plane_index);
        guint memory_index = 0;
        guint memory_length = 1;
        gsize skip = 0;
        if (!gst_buffer_find_memory(buffer, global_offset, 1,
                &memory_index, &memory_length, &skip) || memory_length != 1) {
            ClosePlanes(descriptor);
            return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
        }
        GstMemory* memory = gst_buffer_peek_memory(buffer, memory_index);
        if (!memory || !gst_is_dmabuf_memory(memory)) {
            ClosePlanes(descriptor);
            return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
        }
        const int source_fd = gst_dmabuf_memory_get_fd(memory);
        const int exported_fd = source_fd >= 0 ? fcntl(source_fd, F_DUPFD_CLOEXEC, 0) : -1;
        if (exported_fd < 0) {
            ClosePlanes(descriptor);
            return JALIUM_MEDIA_E_PLATFORM;
        }
        gsize memory_offset = 0;
        gsize maximum_size = 0;
        const gsize visible_size =
            gst_memory_get_sizes(memory, &memory_offset, &maximum_size);
        const gint stride = meta ? meta->stride[plane_index]
            : GST_VIDEO_INFO_PLANE_STRIDE(&video_info, plane_index);
        if (stride <= 0 || memory_offset > UINT32_MAX || skip > UINT32_MAX - memory_offset) {
            close(exported_fd);
            ClosePlanes(descriptor);
            return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
        }
        auto& output = descriptor->planes[plane_index];
        output.fd = exported_fd;
        output.stride_bytes = static_cast<uint32_t>(stride);
        output.offset_bytes = static_cast<uint32_t>(memory_offset + skip);
        output.modifier = modifier;
        output.size_bytes = maximum_size != 0 ? maximum_size : visible_size;
    }
    descriptor->handle0 = static_cast<uint64_t>(
        static_cast<uint32_t>(descriptor->planes[0].fd));
    return JALIUM_MEDIA_OK;
}
} // namespace jalium::media::dmabuf
