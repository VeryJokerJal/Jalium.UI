#pragma once

#include "jalium_media.h"
#include <android/hardware_buffer.h>
#include <media/NdkImage.h>
#include <media/NdkImageReader.h>
#include <atomic>
#include <dlfcn.h>
#include <fcntl.h>
#include <memory>
#include <unistd.h>

namespace jalium::media::android {

// Resolve API 26 symbols at runtime so API 24/25 devices can still load the
// media library and use its ByteBuffer/SIMD fallback.
struct GpuImageApi {
    media_status_t (*create)(int32_t, int32_t, int32_t, uint64_t, int32_t, AImageReader**) = nullptr;
    void (*destroy)(AImageReader*) = nullptr;
    media_status_t (*window)(AImageReader*, ANativeWindow**) = nullptr;
    media_status_t (*acquire)(AImageReader*, AImage**, int*) = nullptr;
    media_status_t (*buffer)(const AImage*, AHardwareBuffer**) = nullptr;
    void (*release)(AImage*, int) = nullptr;
    void (*describe)(const AHardwareBuffer*, AHardwareBuffer_Desc*) = nullptr;

    static const GpuImageApi& Get() {
        static const GpuImageApi api = [] {
            GpuImageApi a;
#define LOAD(field, symbol) a.field = reinterpret_cast<decltype(a.field)>(dlsym(RTLD_DEFAULT, #symbol))
            LOAD(create, AImageReader_newWithUsage); LOAD(destroy, AImageReader_delete);
            LOAD(window, AImageReader_getWindow); LOAD(acquire, AImageReader_acquireNextImageAsync);
            LOAD(buffer, AImage_getHardwareBuffer); LOAD(release, AImage_deleteAsync);
            LOAD(describe, AHardwareBuffer_describe);
#undef LOAD
            return a;
        }();
        return api;
    }
    bool Available() const { return create && destroy && window && acquire && buffer && release && describe; }
};

struct GpuImageReader {
    const GpuImageApi* api = nullptr;
    AImageReader* reader = nullptr;
    ANativeWindow* window = nullptr; // borrowed from reader
    ~GpuImageReader() { if (reader) api->destroy(reader); }
};

// Holding the AHardwareBuffer allocation alone does not prevent BufferQueue
// from recycling its contents. Keep the acquired AImage AND its reader alive
// until the renderer observes the final submission fence.
struct GpuImageFrame {
    std::atomic<uint32_t> references{1};
    std::shared_ptr<GpuImageReader> owner;
    AImage* image = nullptr;
    AHardwareBuffer* buffer = nullptr;
    int acquireFence = -1;
    uint32_t width = 0, height = 0;
    uint32_t colorSpace = 0, flags = 0, formatHint = 1;
    ~GpuImageFrame() {
        if (image) owner->api->release(image, acquireFence); // consumes fence
        else if (acquireFence >= 0) close(acquireFence);
    }
    static void Retain(uint64_t context) {
        reinterpret_cast<GpuImageFrame*>(context)->references.fetch_add(1, std::memory_order_relaxed);
    }
    static void Release(uint64_t context) {
        auto* frame = reinterpret_cast<GpuImageFrame*>(context);
        if (frame->references.fetch_sub(1, std::memory_order_acq_rel) == 1) delete frame;
    }
    jalium_media_status_t Export(jalium_video_decoder_gpu_descriptor_t& descriptor) {
        descriptor = {};
        for (auto& plane : descriptor.planes) plane.fd = -1;
        descriptor.acquire_fence_fd = -1;
        if (acquireFence >= 0) {
            descriptor.acquire_fence_fd = fcntl(acquireFence, F_DUPFD_CLOEXEC, 0);
            if (descriptor.acquire_fence_fd < 0) return JALIUM_MEDIA_E_PLATFORM;
        }
        descriptor.kind = 3; // JALIUM_VS_KIND_AHARDWAREBUFFER
        descriptor.width = width; descriptor.height = height;
        descriptor.handle0 = reinterpret_cast<uint64_t>(buffer);
        descriptor.format_hint = formatHint;
        descriptor.color_space = colorSpace;
        descriptor.descriptor_flags = flags;
        if (acquireFence < 0) descriptor.descriptor_flags |= JALIUM_VIDEO_GPU_DESCRIPTOR_IMMUTABLE_READY;
        descriptor.lifetime_context = reinterpret_cast<uint64_t>(this);
        descriptor.lifetime_retain_callback = reinterpret_cast<uint64_t>(&Retain);
        descriptor.lifetime_release_callback = reinterpret_cast<uint64_t>(&Release);
        Retain(descriptor.lifetime_context);
        return JALIUM_MEDIA_OK;
    }
};

inline void ReleaseGpuDescriptor(jalium_video_decoder_gpu_descriptor_t& descriptor)
{
    if (descriptor.acquire_fence_fd >= 0) close(descriptor.acquire_fence_fd);
    if (descriptor.lifetime_context && descriptor.lifetime_release_callback) {
        auto release = reinterpret_cast<void (*)(uint64_t)>(static_cast<uintptr_t>(descriptor.lifetime_release_callback));
        release(descriptor.lifetime_context);
    }
    descriptor = {};
    for (auto& plane : descriptor.planes) plane.fd = -1;
    descriptor.acquire_fence_fd = -1;
}
} // namespace jalium::media::android
