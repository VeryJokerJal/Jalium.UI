#include <gst/gst.h>
#include <gst/video/video.h>
#include <gst/allocators/gstdmabuf.h>
#include "linux_dmabuf_export.h"

#include <dirent.h>
#include <fcntl.h>
#include <iostream>
#include <sys/mman.h>

namespace {
int failures = 0;
void Check(bool condition, const char* message)
{
    if (!condition) { std::cerr << "FAILED: " << message << '\n'; ++failures; }
}
int FdCount()
{
    int count = 0;
    if (DIR* directory = opendir("/proc/self/fd")) {
        while (readdir(directory)) ++count;
        closedir(directory);
    }
    return count;
}
GstSample* Sample(bool p010, bool split, const char* color = "bt709",
                  const char* chroma = "mpeg2", const char* drm = nullptr,
                  bool badStride = false, bool shortBuffer = false)
{
    GstCaps* caps = gst_caps_new_simple("video/x-raw",
        "format", G_TYPE_STRING, drm ? "DMA_DRM" : p010 ? "P010_10LE" : "NV12",
        "width", G_TYPE_INT, 16, "height", G_TYPE_INT, 16,
        "colorimetry", G_TYPE_STRING, color, "chroma-site", G_TYPE_STRING, chroma, nullptr);
    gst_caps_set_features(caps, 0, gst_caps_features_new(GST_CAPS_FEATURE_MEMORY_DMABUF, nullptr));
    if (drm) gst_structure_set(gst_caps_get_structure(caps, 0), "drm-format", G_TYPE_STRING, drm, nullptr);
    GstBuffer* buffer = gst_buffer_new();
    GstAllocator* allocator = gst_dmabuf_allocator_new();
    for (int i = 0; i < (split ? 2 : 1); ++i) {
        const int fd = memfd_create("jalium-media-export-fixture", MFD_CLOEXEC);
        const size_t size = shortBuffer ? 512 : 4096;
        if (fd < 0 || ftruncate(fd, size) != 0) {
            if (fd >= 0) close(fd);
            gst_object_unref(allocator); gst_buffer_unref(buffer); gst_caps_unref(caps);
            return nullptr;
        }
        gst_buffer_append_memory(buffer, gst_dmabuf_allocator_alloc(allocator, fd, size));
    }
    gst_object_unref(allocator);
    gsize offsets[GST_VIDEO_MAX_PLANES] = {0, split ? 4096u : 1024u, 0, 0};
    gint strides[GST_VIDEO_MAX_PLANES] = {64, badStride ? -64 : 64, 0, 0};
    gst_buffer_add_video_meta_full(buffer, GST_VIDEO_FRAME_FLAG_NONE,
        p010 ? GST_VIDEO_FORMAT_P010_10LE : GST_VIDEO_FORMAT_NV12, 16, 16, 2, offsets, strides);
    GstSample* sample = gst_sample_new(buffer, caps, nullptr, nullptr);
    gst_buffer_unref(buffer); gst_caps_unref(caps);
    return sample;
}
} // namespace

int main(int argc, char** argv)
{
    gst_init(&argc, &argv);
    const int baselineFds = FdCount();
    for (bool p010 : {false, true}) for (bool split : {false, true}) {
        auto* sample = Sample(p010, split);
        Check(sample != nullptr, "create real GStreamer DMABuf sample metadata");
        if (!sample) continue;
        const int producerFds = FdCount();
        jalium_video_decoder_gpu_descriptor_t descriptor{};
        auto status = jalium::media::dmabuf::ExportLayout(sample, JALIUM_PF_BGRA8, &descriptor);
        Check(status == JALIUM_MEDIA_OK, "export NV12/P010 from one or two GstMemory allocations");
        Check(descriptor.plane_count == 2 && descriptor.planes[0].stride_bytes == 64 &&
              descriptor.planes[1].stride_bytes == 64 &&
              descriptor.planes[1].offset_bytes == (split ? 0u : 1024u),
              "global GstVideoMeta offsets map into the correct fd allocation");
        Check(descriptor.color_space == JALIUM_VS_COLOR_BT709 &&
              descriptor.descriptor_flags == JALIUM_VS_FLAG_CHROMA_COSITED_X,
              "matrix, studio range and MPEG2 chroma siting are preserved");
        Check(descriptor.acquire_fence_fd == -1 &&
              !(descriptor.descriptor_flags & JALIUM_VS_FLAG_IMMUTABLE_READY),
              "export does not pretend producer writes are already complete");
        Check(FdCount() == producerFds + 2, "every exported plane owns a duplicate fd");
        Check((fcntl(descriptor.planes[0].fd, F_GETFD) & FD_CLOEXEC) &&
              (fcntl(descriptor.planes[1].fd, F_GETFD) & FD_CLOEXEC), "exported fds do not survive child exec");
        jalium::media::dmabuf::ClosePlanes(&descriptor);
        Check(FdCount() == producerFds, "descriptor cleanup preserves producer fd ownership");
        gst_sample_unref(sample);
    }
    {
        auto* sample = Sample(false, false, "1:4:5:4", "jpeg", "NV12:0x1234");
        jalium_video_decoder_gpu_descriptor_t descriptor{};
        Check(sample && jalium::media::dmabuf::ExportLayout(sample, JALIUM_PF_BGRA8, &descriptor) == JALIUM_MEDIA_OK,
              "DMA_DRM normalizes only logical color metadata");
        Check(descriptor.color_space == JALIUM_VS_COLOR_BT601 &&
              (descriptor.descriptor_flags & JALIUM_VS_FLAG_FULL_RANGE) &&
              descriptor.planes[0].modifier == 0x1234 && descriptor.planes[1].offset_bytes == 1024,
              "DRM modifier, explicit layout and full-range metadata survive normalization");
        jalium::media::dmabuf::ClosePlanes(&descriptor);
        if (sample) gst_sample_unref(sample);
    }
    for (int scenario = 0; scenario < 6; ++scenario) {
        auto* sample = Sample(false, false,
            scenario == 0 ? "1:3:14:1" : scenario == 1 ? "invalid-color" : "bt709",
            scenario == 2 ? "dv" : "mpeg2",
            scenario == 3 ? "NV12:garbage" : nullptr, scenario == 4, scenario == 5);
        Check(sample != nullptr, "create rejected-layout fixture");
        if (!sample) continue;
        const int producerFds = FdCount();
        jalium_video_decoder_gpu_descriptor_t descriptor{};
        Check(jalium::media::dmabuf::ExportLayout(sample, JALIUM_PF_BGRA8, &descriptor) != JALIUM_MEDIA_OK,
              "HDR, malformed metadata, alternate-line chroma and invalid layouts use fallback");
        Check(FdCount() == producerFds, "rejected exports close every partial duplicate");
        gst_sample_unref(sample);
    }
    Check(FdCount() == baselineFds, "GStreamer samples and exported descriptors balance all fds");
    if (!failures) std::cout << "Linux DMABuf metadata/export tests passed.\n";
    return failures ? 1 : 0;
}
