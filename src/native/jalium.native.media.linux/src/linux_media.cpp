#include "jalium_media.h"
#include "jalium_media_internal.h"
#include "jalium_audio.h"
#include "audio_internal.h"

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cerrno>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <mutex>
#include <new>
#include <string>
#include <vector>

#if defined(__linux__)
#include <dlfcn.h>
#include <unistd.h>
#endif

// Linux must retain animated GIF and static PNG support even when the optional
// GStreamer runtime is not installed. The built-in PNG path also keeps Linux
// compatible with WIC for otherwise decodable images whose chunk CRC is bad;
// GStreamer's pngdec rejects those before producing a frame. Keep this copy
// translation-unit local so the NativeAOT static aggregation does not collide
// with the renderer's stb_image implementation.
#define STB_IMAGE_STATIC
#define STBI_ONLY_GIF
#define STBI_ONLY_PNG
#define STB_IMAGE_IMPLEMENTATION
#include "stb/stb_image.h"

#ifndef JALIUM_HAS_GSTREAMER
#define JALIUM_HAS_GSTREAMER 0
#endif

#if JALIUM_HAS_GSTREAMER
#include "gstreamer_loader.h"
#endif

#if JALIUM_HAS_GSTREAMER
struct DynamicPadContext {
    GstElement* target = nullptr;
    const char* media_prefix = nullptr;
    // Filled by BuildUriDecodePipeline so the no-more-pads handler can unblock
    // the appsink when the file has no stream of the requested kind (probing a
    // video decoder against an audio-only file used to stall the 15 s
    // first-sample timeout instead of failing immediately).
    GstElement* sink_element = nullptr;
    uint32_t requested_stream_index = 0;
    std::atomic<uint32_t> matching_pad_count{0};
    std::atomic<bool> no_matching_pad{false};
};

struct jalium_video_decoder {
    GstElement* pipeline = nullptr;
    GstAppSink* sink = nullptr;
    GstSample* pending_sample = nullptr;
    DynamicPadContext link_context{};
    jalium_pixel_format_t format = JALIUM_PF_BGRA8;
    jalium_video_info_t info{};
    std::vector<uint8_t> pixels;
    std::string source;
    bool dmabuf_mode = false;
    int64_t last_pts_microseconds = 0;
};

struct jalium_camera_source {
    GstElement* pipeline = nullptr;
    GstAppSink* sink = nullptr;
    GstSample* pending_sample = nullptr;
    jalium_pixel_format_t format = JALIUM_PF_BGRA8;
    std::vector<uint8_t> pixels;
};

struct jalium_microphone_source {
    GstElement* pipeline = nullptr;
    GstAppSink* sink = nullptr;
    GstSample* pending_sample = nullptr;
    std::vector<float> samples;
    uint32_t sample_rate = 0;
    uint32_t channels = 0;
};

struct jalium_subtitle_decoder {
    GstElement* pipeline = nullptr;
    GstAppSink* sink = nullptr;
    GstSample* pending_sample = nullptr;
    DynamicPadContext link_context{};
    std::string text;
};
#else
struct jalium_video_decoder {};
struct jalium_camera_source {};
struct jalium_microphone_source {};
struct jalium_subtitle_decoder {};
#endif

namespace {

std::mutex g_init_mutex;
uint32_t g_init_count = 0;
bool g_media_available = false;
// This is deliberately evidence-based rather than a plugin-presence guess.
// It becomes true only after this process has exported a descriptor whose
// exact layout is accepted by the current Vulkan importer.
std::atomic<bool> g_renderable_rgb_dmabuf_observed{false};

bool IsSupportedFormat(jalium_pixel_format_t format) noexcept
{
    return format == JALIUM_PF_BGRA8 || format == JALIUM_PF_RGBA8;
}

bool FileExists(const char* path) noexcept
{
    if (!path || !*path) return false;
    FILE* file = std::fopen(path, "rb");
    if (!file) return false;
    std::fclose(file);
    return true;
}

bool IsUri(const char* path) noexcept
{
    if (!path) return false;
    const char* separator = std::strstr(path, "://");
    if (!separator || separator == path) return false;
    return std::all_of(path, separator, [](unsigned char value) {
        return (value >= 'a' && value <= 'z') ||
               (value >= 'A' && value <= 'Z') ||
               (value >= '0' && value <= '9') ||
               value == '+' || value == '-' || value == '.';
    });
}

jalium_media_status_t ReadFile(
    const char* path,
    std::vector<uint8_t>& bytes) noexcept
{
    bytes.clear();
    if (!path || !*path) return JALIUM_MEDIA_E_INVALID_ARG;
    FILE* file = std::fopen(path, "rb");
    if (!file) {
        return errno == EACCES ? JALIUM_MEDIA_E_PERMISSION_DENIED
                               : JALIUM_MEDIA_E_IO;
    }
    if (std::fseek(file, 0, SEEK_END) != 0) {
        std::fclose(file);
        return JALIUM_MEDIA_E_IO;
    }
    const long length = std::ftell(file);
    if (length <= 0 || std::fseek(file, 0, SEEK_SET) != 0) {
        std::fclose(file);
        return JALIUM_MEDIA_E_IO;
    }
    try {
        bytes.resize(static_cast<size_t>(length));
    } catch (...) {
        std::fclose(file);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    const size_t read = std::fread(bytes.data(), 1, bytes.size(), file);
    std::fclose(file);
    if (read != bytes.size()) {
        bytes.clear();
        return JALIUM_MEDIA_E_IO;
    }
    return JALIUM_MEDIA_OK;
}

bool IsGif(const uint8_t* data, size_t size) noexcept
{
    return data && size >= 6 &&
           (std::memcmp(data, "GIF87a", 6) == 0 ||
           std::memcmp(data, "GIF89a", 6) == 0);
}

bool IsPng(const uint8_t* data, size_t size) noexcept
{
    static constexpr uint8_t kPngSignature[] = {
        0x89, 'P', 'N', 'G', 0x0d, 0x0a, 0x1a, 0x0a
    };
    return data && size >= sizeof(kPngSignature) &&
           std::memcmp(data, kPngSignature, sizeof(kPngSignature)) == 0;
}

bool IsWebP(const uint8_t* data, size_t size) noexcept
{
    return data && size >= 12 &&
           std::memcmp(data, "RIFF", 4) == 0 &&
           std::memcmp(data + 8, "WEBP", 4) == 0;
}

uint32_t ReadLittleEndian24(const uint8_t* value) noexcept
{
    return static_cast<uint32_t>(value[0]) |
           (static_cast<uint32_t>(value[1]) << 8) |
           (static_cast<uint32_t>(value[2]) << 16);
}

uint32_t ReadLittleEndian32(const uint8_t* value) noexcept
{
    return static_cast<uint32_t>(value[0]) |
           (static_cast<uint32_t>(value[1]) << 8) |
           (static_cast<uint32_t>(value[2]) << 16) |
           (static_cast<uint32_t>(value[3]) << 24);
}

void AppendLittleEndian32(std::vector<uint8_t>& output, uint32_t value)
{
    output.push_back(static_cast<uint8_t>(value));
    output.push_back(static_cast<uint8_t>(value >> 8));
    output.push_back(static_cast<uint8_t>(value >> 16));
    output.push_back(static_cast<uint8_t>(value >> 24));
}

struct WebPFrameData {
    uint32_t x = 0;
    uint32_t y = 0;
    uint32_t width = 0;
    uint32_t height = 0;
    uint32_t delay_ms = 0;
    bool dispose_to_background = false;
    bool no_blend = false;
    const uint8_t* payload = nullptr;
    size_t payload_size = 0;
};

struct WebPAnimationData {
    uint32_t canvas_width = 0;
    uint32_t canvas_height = 0;
    std::vector<WebPFrameData> frames;
};

bool ParseAnimatedWebP(
    const uint8_t* data,
    size_t size,
    WebPAnimationData& animation) noexcept
{
    animation = {};
    if (!IsWebP(data, size)) return false;

    try {
        bool animation_flag = false;
        bool animation_chunk = false;
        size_t offset = 12;
        while (offset <= size && size - offset >= 8) {
            const uint8_t* type = data + offset;
            const uint32_t length = ReadLittleEndian32(data + offset + 4);
            const size_t payload_offset = offset + 8;
            if (length > size - payload_offset) return false;
            const size_t padded_length = static_cast<size_t>(length) +
                (static_cast<size_t>(length) & 1u);
            if (padded_length > size - payload_offset) return false;
            const uint8_t* payload = data + payload_offset;

            if (std::memcmp(type, "VP8X", 4) == 0) {
                if (length != 10) return false;
                animation_flag = (payload[0] & 0x02u) != 0;
                animation.canvas_width = ReadLittleEndian24(payload + 4) + 1u;
                animation.canvas_height = ReadLittleEndian24(payload + 7) + 1u;
            } else if (std::memcmp(type, "ANIM", 4) == 0) {
                if (length != 6) return false;
                animation_chunk = true;
            } else if (std::memcmp(type, "ANMF", 4) == 0) {
                if (length < 16) return false;
                WebPFrameData frame{};
                // The WebP container stores frame offsets in two-pixel units.
                frame.x = ReadLittleEndian24(payload) * 2u;
                frame.y = ReadLittleEndian24(payload + 3) * 2u;
                frame.width = ReadLittleEndian24(payload + 6) + 1u;
                frame.height = ReadLittleEndian24(payload + 9) + 1u;
                frame.delay_ms = ReadLittleEndian24(payload + 12);
                frame.dispose_to_background = (payload[15] & 0x01u) != 0;
                frame.no_blend = (payload[15] & 0x02u) != 0;
                frame.payload = payload + 16;
                frame.payload_size = static_cast<size_t>(length) - 16u;
                if (frame.width == 0 || frame.height == 0 ||
                    frame.payload_size < 8) {
                    return false;
                }
                animation.frames.push_back(frame);
            }

            offset = payload_offset + padded_length;
        }

        if (!animation_flag || !animation_chunk ||
            animation.canvas_width == 0 || animation.canvas_height == 0 ||
            animation.frames.empty()) {
            return false;
        }

        for (const WebPFrameData& frame : animation.frames) {
            if (frame.x > animation.canvas_width ||
                frame.y > animation.canvas_height ||
                frame.width > animation.canvas_width - frame.x ||
                frame.height > animation.canvas_height - frame.y) {
                return false;
            }
        }
        return true;
    } catch (...) {
        animation = {};
        return false;
    }
}

struct WebPRuntime {
    using DecodeRgba = uint8_t* (*)(const uint8_t*, size_t, int*, int*);
    using Free = void (*)(void*);

    void* library = nullptr;
    DecodeRgba decode_rgba = nullptr;
    Free free = nullptr;

    bool IsAvailable() const noexcept
    {
        return library && decode_rgba && free;
    }
};

const WebPRuntime& GetWebPRuntime() noexcept
{
    static const WebPRuntime runtime = [] {
        WebPRuntime value{};
#if defined(__linux__)
        constexpr const char* names[] = {
            "libwebp.so.7", "libwebp.so.6", "libwebp.so.5", "libwebp.so"
        };
        for (const char* name : names) {
            value.library = dlopen(name, RTLD_NOW | RTLD_LOCAL);
            if (value.library) break;
        }
        if (value.library) {
            value.decode_rgba = reinterpret_cast<WebPRuntime::DecodeRgba>(
                dlsym(value.library, "WebPDecodeRGBA"));
            value.free = reinterpret_cast<WebPRuntime::Free>(
                dlsym(value.library, "WebPFree"));
            if (!value.decode_rgba || !value.free) {
                dlclose(value.library);
                value = {};
            }
        }
#endif
        return value;
    }();
    return runtime;
}

bool BuildWebPFrameFile(
    const WebPFrameData& frame,
    std::vector<uint8_t>& output) noexcept
{
    try {
        if (frame.payload_size >
            static_cast<size_t>(std::numeric_limits<uint32_t>::max() - 4u)) {
            return false;
        }
        output.clear();
        output.reserve(frame.payload_size + 12u);
        output.insert(output.end(), {'R', 'I', 'F', 'F'});
        AppendLittleEndian32(
            output, static_cast<uint32_t>(frame.payload_size + 4u));
        output.insert(output.end(), {'W', 'E', 'B', 'P'});
        output.insert(
            output.end(), frame.payload, frame.payload + frame.payload_size);
        return true;
    } catch (...) {
        output.clear();
        return false;
    }
}

void BlendWebPPixel(uint8_t* destination, const uint8_t* source) noexcept
{
    const uint32_t source_alpha = source[3];
    if (source_alpha == 255u) {
        std::memcpy(destination, source, 4);
        return;
    }
    if (source_alpha == 0u) return;

    const uint32_t destination_alpha = destination[3];
    const uint32_t inverse_alpha = 255u - source_alpha;
    const uint32_t output_alpha = source_alpha +
        (destination_alpha * inverse_alpha + 127u) / 255u;
    for (uint32_t channel = 0; channel < 3; ++channel) {
        const uint32_t premultiplied = source[channel] * source_alpha +
            (destination[channel] * destination_alpha * inverse_alpha + 127u) /
                255u;
        destination[channel] = output_alpha == 0
            ? 0
            : static_cast<uint8_t>(std::min(
                255u, (premultiplied + output_alpha / 2u) / output_alpha));
    }
    destination[3] = static_cast<uint8_t>(output_alpha);
}

jalium_media_status_t DecodeWebPFrame(
    const uint8_t* data,
    size_t size,
    uint32_t frame_index,
    jalium_pixel_format_t format,
    jalium_image_t* out_image,
    uint32_t* out_delay_ms,
    uint32_t* out_frame_count = nullptr) noexcept
{
    if (!IsWebP(data, size)) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;

    WebPAnimationData animation;
    const bool animated = ParseAnimatedWebP(data, size, animation);
    if (out_frame_count) {
        *out_frame_count = animated
            ? static_cast<uint32_t>(animation.frames.size())
            : 1u;
    }
    if (!out_image) return JALIUM_MEDIA_OK;
    const uint32_t frame_count = animated
        ? static_cast<uint32_t>(animation.frames.size())
        : 1u;
    if (frame_index >= frame_count) return JALIUM_MEDIA_E_INVALID_ARG;

    const WebPRuntime& webp = GetWebPRuntime();
    if (!webp.IsAvailable()) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;

    try {
        uint32_t canvas_width = 0;
        uint32_t canvas_height = 0;
        std::vector<uint8_t> canvas;
        std::vector<uint8_t> frame_file;

        if (!animated) {
            int decoded_width = 0;
            int decoded_height = 0;
            uint8_t* decoded = webp.decode_rgba(
                data, size, &decoded_width, &decoded_height);
            if (!decoded || decoded_width <= 0 || decoded_height <= 0) {
                if (decoded) webp.free(decoded);
                return JALIUM_MEDIA_E_DECODE_FAILED;
            }
            canvas_width = static_cast<uint32_t>(decoded_width);
            canvas_height = static_cast<uint32_t>(decoded_height);
            if (canvas_height > std::numeric_limits<size_t>::max() /
                    (static_cast<size_t>(canvas_width) * 4u)) {
                webp.free(decoded);
                return JALIUM_MEDIA_E_OUT_OF_MEMORY;
            }
            const size_t byte_count = static_cast<size_t>(canvas_width) *
                canvas_height * 4u;
            canvas.assign(decoded, decoded + byte_count);
            webp.free(decoded);
        } else {
            canvas_width = animation.canvas_width;
            canvas_height = animation.canvas_height;
            if (canvas_width > std::numeric_limits<size_t>::max() / 4u ||
                canvas_height > std::numeric_limits<size_t>::max() /
                    (static_cast<size_t>(canvas_width) * 4u)) {
                return JALIUM_MEDIA_E_OUT_OF_MEMORY;
            }
            const size_t canvas_size = static_cast<size_t>(canvas_width) *
                canvas_height * 4u;
            // Keep untrusted image allocations bounded even on 64-bit hosts.
            if (canvas_size > 1024u * 1024u * 1024u) {
                return JALIUM_MEDIA_E_OUT_OF_MEMORY;
            }
            canvas.assign(canvas_size, 0);

            for (uint32_t index = 0; index <= frame_index; ++index) {
                const WebPFrameData& frame = animation.frames[index];
                if (!BuildWebPFrameFile(frame, frame_file)) {
                    return JALIUM_MEDIA_E_DECODE_FAILED;
                }
                int decoded_width = 0;
                int decoded_height = 0;
                uint8_t* decoded = webp.decode_rgba(
                    frame_file.data(), frame_file.size(),
                    &decoded_width, &decoded_height);
                if (!decoded || decoded_width != static_cast<int>(frame.width) ||
                    decoded_height != static_cast<int>(frame.height)) {
                    if (decoded) webp.free(decoded);
                    return JALIUM_MEDIA_E_DECODE_FAILED;
                }

                for (uint32_t y = 0; y < frame.height; ++y) {
                    uint8_t* destination = canvas.data() +
                        (static_cast<size_t>(frame.y + y) * canvas_width +
                         frame.x) * 4u;
                    const uint8_t* source = decoded +
                        static_cast<size_t>(y) * frame.width * 4u;
                    if (frame.no_blend) {
                        std::memcpy(destination, source,
                                    static_cast<size_t>(frame.width) * 4u);
                    } else {
                        for (uint32_t x = 0; x < frame.width; ++x) {
                            BlendWebPPixel(destination + x * 4u,
                                           source + x * 4u);
                        }
                    }
                }
                webp.free(decoded);

                if (index == frame_index) break;
                if (frame.dispose_to_background) {
                    for (uint32_t y = 0; y < frame.height; ++y) {
                        std::memset(
                            canvas.data() +
                                (static_cast<size_t>(frame.y + y) *
                                 canvas_width + frame.x) * 4u,
                            0, static_cast<size_t>(frame.width) * 4u);
                    }
                }
            }
        }

        const uint32_t stride = jalium_media_compute_stride(canvas_width);
        if (stride != canvas_width * 4u ||
            canvas_height > std::numeric_limits<size_t>::max() / stride) {
            return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        }
        const size_t output_size = static_cast<size_t>(stride) * canvas_height;
        auto* output = static_cast<uint8_t*>(
            jalium_media_aligned_alloc(output_size));
        if (!output) return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        std::memcpy(output, canvas.data(), output_size);
        if (format == JALIUM_PF_BGRA8) {
            jalium_media_swap_rb_inplace(
                output, canvas_width, canvas_height, stride);
        }

        out_image->width = canvas_width;
        out_image->height = canvas_height;
        out_image->stride_bytes = stride;
        out_image->format = format;
        out_image->pixels = output;
        out_image->_reserved = output;
        if (out_delay_ms) {
            *out_delay_ms = animated
                ? std::max(animation.frames[frame_index].delay_ms, 10u)
                : 0u;
        }
        return JALIUM_MEDIA_OK;
    } catch (const std::bad_alloc&) {
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    } catch (...) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
}

uint32_t ReadBigEndian32(const uint8_t* value) noexcept
{
    return (static_cast<uint32_t>(value[0]) << 24) |
           (static_cast<uint32_t>(value[1]) << 16) |
           (static_cast<uint32_t>(value[2]) << 8) |
           static_cast<uint32_t>(value[3]);
}

bool IsAnimatedPng(const uint8_t* data, size_t size) noexcept
{
    if (!IsPng(data, size)) return false;

    // APNG announces itself with an acTL chunk before the first IDAT. Walk
    // chunks instead of byte-searching so a compressed payload cannot produce
    // a false positive.
    size_t offset = 8;
    while (offset <= size && size - offset >= 12) {
        const uint32_t length = ReadBigEndian32(data + offset);
        const size_t chunk_size = static_cast<size_t>(length) + 12;
        if (chunk_size < 12 || chunk_size > size - offset) return false;
        const uint8_t* type = data + offset + 4;
        if (std::memcmp(type, "acTL", 4) == 0) return length == 8;
        if (std::memcmp(type, "IDAT", 4) == 0 ||
            std::memcmp(type, "IEND", 4) == 0) {
            return false;
        }
        offset += chunk_size;
    }
    return false;
}

jalium_media_status_t DecodeStaticPng(
    const uint8_t* data,
    size_t size,
    jalium_pixel_format_t format,
    jalium_image_t* out_image) noexcept
{
    if (size > static_cast<size_t>(std::numeric_limits<int>::max())) {
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }

    int width = 0;
    int height = 0;
    int components = 0;
    stbi_uc* decoded = stbi_load_from_memory(
        data, static_cast<int>(size), &width, &height, &components, 4);
    if (!decoded || width <= 0 || height <= 0) {
        stbi_image_free(decoded);
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
    if (static_cast<uint64_t>(width) * 4u >
            std::numeric_limits<uint32_t>::max()) {
        stbi_image_free(decoded);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }

    const uint32_t output_width = static_cast<uint32_t>(width);
    const uint32_t output_height = static_cast<uint32_t>(height);
    const uint32_t stride = jalium_media_compute_stride(output_width);
    if (output_height > std::numeric_limits<size_t>::max() / stride) {
        stbi_image_free(decoded);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    const size_t output_size = static_cast<size_t>(stride) * output_height;
    auto* output = static_cast<uint8_t*>(
        jalium_media_aligned_alloc(output_size));
    if (!output) {
        stbi_image_free(decoded);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }

    std::memcpy(output, decoded, output_size);
    stbi_image_free(decoded);
    if (format == JALIUM_PF_BGRA8) {
        jalium_media_swap_rb_inplace(
            output, output_width, output_height, stride);
    }

    out_image->width = output_width;
    out_image->height = output_height;
    out_image->stride_bytes = stride;
    out_image->format = format;
    out_image->pixels = output;
    out_image->_reserved = output;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t DecodeGifFrame(
    const uint8_t* data,
    size_t size,
    uint32_t frame_index,
    jalium_pixel_format_t format,
    jalium_image_t* out_image,
    uint32_t* out_delay_ms,
    uint32_t* out_frame_count = nullptr) noexcept
{
    if (size > static_cast<size_t>(std::numeric_limits<int>::max())) {
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }

    int* delays = nullptr;
    int width = 0;
    int height = 0;
    int frame_count = 0;
    int components = 0;
    stbi_uc* frames = stbi_load_gif_from_memory(
        data, static_cast<int>(size), &delays, &width, &height,
        &frame_count, &components, 4);
    if (!frames || width <= 0 || height <= 0 || frame_count <= 0) {
        stbi_image_free(frames);
        stbi_image_free(delays);
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    if (out_frame_count) *out_frame_count = static_cast<uint32_t>(frame_count);
    if (!out_image) {
        stbi_image_free(frames);
        stbi_image_free(delays);
        return JALIUM_MEDIA_OK;
    }
    if (frame_index >= static_cast<uint32_t>(frame_count)) {
        stbi_image_free(frames);
        stbi_image_free(delays);
        return JALIUM_MEDIA_E_INVALID_ARG;
    }

    const uint32_t output_width = static_cast<uint32_t>(width);
    const uint32_t output_height = static_cast<uint32_t>(height);
    const uint32_t stride = jalium_media_compute_stride(output_width);
    const size_t frame_size = static_cast<size_t>(stride) * output_height;
    auto* output = static_cast<uint8_t*>(jalium_media_aligned_alloc(frame_size));
    if (!output) {
        stbi_image_free(frames);
        stbi_image_free(delays);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }

    const uint8_t* source = frames + frame_size * frame_index;
    std::memcpy(output, source, frame_size);
    if (format == JALIUM_PF_BGRA8) {
        jalium_media_swap_rb_inplace(
            output, output_width, output_height, stride);
    }

    out_image->width = output_width;
    out_image->height = output_height;
    out_image->stride_bytes = stride;
    out_image->format = format;
    out_image->pixels = output;
    out_image->_reserved = output;
    if (out_delay_ms) {
        // GIF delay is centisecond based. stb_image already converts it to ms;
        // match browsers/WIC by preventing malformed zero-delay animations
        // from spinning the UI thread.
        const int delay = delays ? delays[frame_index] : 0;
        *out_delay_ms = static_cast<uint32_t>(std::max(delay, 10));
    }

    stbi_image_free(frames);
    stbi_image_free(delays);
    return JALIUM_MEDIA_OK;
}

void ResetGpuDescriptor(
    jalium_video_decoder_gpu_descriptor_t* descriptor) noexcept
{
    std::memset(descriptor, 0, sizeof(*descriptor));
    for (auto& plane : descriptor->planes) plane.fd = -1;
    descriptor->acquire_fence_fd = -1;
}

void CloseGpuDescriptorFds(
    jalium_video_decoder_gpu_descriptor_t* descriptor) noexcept
{
    if (!descriptor) return;
#if defined(__linux__)
    for (auto& plane : descriptor->planes) {
        if (plane.fd >= 0) close(plane.fd);
        plane.fd = -1;
    }
    if (descriptor->acquire_fence_fd >= 0) {
        close(descriptor->acquire_fence_fd);
        descriptor->acquire_fence_fd = -1;
    }
#endif
    if (descriptor->lifetime_context != 0 &&
        descriptor->lifetime_release_callback != 0) {
        using LifetimeCallback = void (*)(uint64_t);
        auto release = reinterpret_cast<LifetimeCallback>(
            static_cast<uintptr_t>(
                descriptor->lifetime_release_callback));
        release(descriptor->lifetime_context);
        descriptor->lifetime_context = 0;
        descriptor->lifetime_retain_callback = 0;
        descriptor->lifetime_release_callback = 0;
    }
}

#if JALIUM_HAS_GSTREAMER

void RetainGstSampleLifetime(uint64_t context)
{
    if (context != 0) {
        (void)gst_sample_ref(reinterpret_cast<GstSample*>(
            static_cast<uintptr_t>(context)));
    }
}

void ReleaseGstSampleLifetime(uint64_t context)
{
    if (context != 0) {
        gst_sample_unref(reinterpret_cast<GstSample*>(
            static_cast<uintptr_t>(context)));
    }
}

const char* ResolveGstUri(const char* path, gchar** owned_uri,
                          GError** error) noexcept
{
    *owned_uri = nullptr;
    if (IsUri(path)) return path;
    *owned_uri = gst_filename_to_uri(path, error);
    return *owned_uri;
}

void LinkDecodedPad(GstElement*, GstPad* pad, gpointer user_data)
{
    auto* context = static_cast<DynamicPadContext*>(user_data);
    if (!context || !context->target) return;

    GstCaps* caps = gst_pad_get_current_caps(pad);
    if (!caps) caps = gst_pad_query_caps(pad, nullptr);
    const GstStructure* structure = caps && gst_caps_get_size(caps) > 0
        ? gst_caps_get_structure(caps, 0) : nullptr;
    const char* name = structure ? gst_structure_get_name(structure) : nullptr;

    if (name && g_str_has_prefix(name, context->media_prefix)) {
        const uint32_t index = context->matching_pad_count.fetch_add(
            1, std::memory_order_acq_rel);
        if (index != context->requested_stream_index) {
            if (caps) gst_caps_unref(caps);
            return;
        }
        GstPad* sink_pad = gst_element_get_static_pad(context->target, "sink");
        if (sink_pad && !gst_pad_is_linked(sink_pad)) {
            (void)gst_pad_link(pad, sink_pad);
        }
        if (sink_pad) gst_object_unref(sink_pad);
    }
    if (caps) gst_caps_unref(caps);
}

void HandleNoMorePads(GstElement*, gpointer user_data)
{
    auto* context = static_cast<DynamicPadContext*>(user_data);
    if (!context || !context->target) return;

    GstPad* sink_pad = gst_element_get_static_pad(context->target, "sink");
    const bool linked = sink_pad && gst_pad_is_linked(sink_pad);
    if (sink_pad) gst_object_unref(sink_pad);
    if (linked) return;

    // uridecodebin exposed every stream and none matched media_prefix: the
    // appsink will never see data or EOS on its own, so inject EOS to release
    // the first-sample wait right away.
    context->no_matching_pad.store(true, std::memory_order_release);
    if (context->sink_element) {
        GstPad* appsink_pad =
            gst_element_get_static_pad(context->sink_element, "sink");
        if (appsink_pad) {
            (void)gst_pad_send_event(appsink_pad, gst_event_new_eos());
            gst_object_unref(appsink_pad);
        }
    }
}

jalium_media_status_t MessageStatus(GstMessage* message,
                                    jalium_media_status_t fallback) noexcept
{
    if (!message) return fallback;

    jalium_media_status_t result = fallback;
    if (GST_MESSAGE_TYPE(message) == GST_MESSAGE_EOS) {
        result = JALIUM_MEDIA_E_END_OF_STREAM;
    } else {
        GError* error = nullptr;
        gchar* debug = nullptr;
        gst_message_parse_error(message, &error, &debug);
        std::string text = error && error->message ? error->message : "";
        std::transform(text.begin(), text.end(), text.begin(),
                       [](unsigned char value) {
                           return static_cast<char>(std::tolower(value));
                       });
        if (text.find("permission denied") != std::string::npos ||
            text.find("not authorized") != std::string::npos ||
            text.find("access denied") != std::string::npos) {
            result = JALIUM_MEDIA_E_PERMISSION_DENIED;
        } else if (text.find("not found") != std::string::npos ||
                   text.find("no such file") != std::string::npos) {
            result = JALIUM_MEDIA_E_IO;
        } else if (error && error->domain == GST_STREAM_ERROR &&
                   (error->code == GST_STREAM_ERROR_CODEC_NOT_FOUND ||
                    error->code == GST_STREAM_ERROR_FORMAT)) {
            result = JALIUM_MEDIA_E_UNSUPPORTED_CODEC;
        }
        if (error) g_error_free(error);
        g_free(debug);
    }
    return result;
}

jalium_media_status_t PipelineError(GstElement* pipeline,
                                    jalium_media_status_t fallback) noexcept
{
    if (!pipeline) return fallback;
    GstBus* bus = gst_element_get_bus(pipeline);
    if (!bus) return fallback;
    GstMessage* message = gst_bus_pop_filtered(
        bus, static_cast<GstMessageType>(GST_MESSAGE_ERROR | GST_MESSAGE_EOS));
    gst_object_unref(bus);
    if (!message) return fallback;

    const auto result = MessageStatus(message, fallback);
    gst_message_unref(message);
    return result;
}

jalium_media_status_t SeekPipelineAccurately(
    GstElement* pipeline, int64_t position_us) noexcept
{
    if (!pipeline) return JALIUM_MEDIA_E_INVALID_ARG;
    if (position_us < 0) position_us = 0;

    // appsink pipelines run with sync=false and often reach EOS before the
    // caller seeks. A flushing seek clears that state asynchronously. Drain
    // the old bus state, then wait for this seek's ASYNC_DONE so the next pull
    // cannot mistake the previous segment's EOS for the new result.
    GstBus* bus = gst_element_get_bus(pipeline);
    if (!bus) return JALIUM_MEDIA_E_PLATFORM;
    while (GstMessage* stale = gst_bus_pop_filtered(
               bus, static_cast<GstMessageType>(GST_MESSAGE_ERROR |
                                                GST_MESSAGE_EOS |
                                                GST_MESSAGE_ASYNC_DONE))) {
        // These messages describe the segment that already produced usable
        // samples. uridecodebin can also post errors for unselected sibling
        // streams, so none of them is authoritative for the new seek.
        gst_message_unref(stale);
    }

    if (!gst_element_seek_simple(
            pipeline, GST_FORMAT_TIME,
            static_cast<GstSeekFlags>(GST_SEEK_FLAG_FLUSH |
                                      GST_SEEK_FLAG_ACCURATE),
            static_cast<gint64>(position_us) * GST_USECOND)) {
        gst_object_unref(bus);
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    // A stale EOS may still race with the drain and arrive after the seek.
    // ASYNC_DONE, not EOS, is the completion signal for the new segment.
    GstMessage* completion = gst_bus_timed_pop_filtered(
        bus, 15 * GST_SECOND,
        static_cast<GstMessageType>(GST_MESSAGE_ERROR |
                                    GST_MESSAGE_ASYNC_DONE));
    gst_object_unref(bus);
    if (!completion) return JALIUM_MEDIA_E_DECODE_FAILED;
    const auto status = GST_MESSAGE_TYPE(completion) == GST_MESSAGE_ASYNC_DONE
        ? JALIUM_MEDIA_OK
        : MessageStatus(completion, JALIUM_MEDIA_E_DECODE_FAILED);
    gst_message_unref(completion);
    return status;
}

const char* GstPixelFormat(jalium_pixel_format_t format) noexcept
{
    return format == JALIUM_PF_RGBA8 ? "RGBA" : "BGRA";
}

jalium_media_status_t CopyVideoSample(
    GstSample* sample,
    jalium_pixel_format_t format,
    std::vector<uint8_t>& pixels,
    uint32_t& width,
    uint32_t& height,
    uint32_t& stride,
    int64_t* pts_us = nullptr,
    int32_t* keyframe = nullptr) noexcept
{
    if (!sample) return JALIUM_MEDIA_E_DECODE_FAILED;
    GstCaps* caps = gst_sample_get_caps(sample);
    GstBuffer* buffer = gst_sample_get_buffer(sample);
    GstVideoInfo info;
    if (!caps || !buffer || !gst_video_info_from_caps(&info, caps)) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    width = GST_VIDEO_INFO_WIDTH(&info);
    height = GST_VIDEO_INFO_HEIGHT(&info);
    stride = jalium_media_compute_stride(width);
    if (width == 0 || height == 0) return JALIUM_MEDIA_E_DECODE_FAILED;

    GstVideoFrame frame;
    if (!gst_video_frame_map(&frame, &info, buffer, GST_MAP_READ)) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    const auto* source = static_cast<const uint8_t*>(
        GST_VIDEO_FRAME_PLANE_DATA(&frame, 0));
    const int source_stride = GST_VIDEO_FRAME_PLANE_STRIDE(&frame, 0);
    const size_t size = static_cast<size_t>(stride) * height;
    try {
        pixels.resize(size);
    } catch (...) {
        gst_video_frame_unmap(&frame);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    for (uint32_t row = 0; row < height; ++row) {
        const uint8_t* source_row = source_stride >= 0
            ? source + static_cast<size_t>(row) * source_stride
            : source + static_cast<size_t>(height - row - 1) *
                         static_cast<size_t>(-source_stride);
        std::memcpy(pixels.data() + static_cast<size_t>(row) * stride,
                    source_row, stride);
    }
    gst_video_frame_unmap(&frame);

    if (pts_us) {
        const GstClockTime pts = GST_BUFFER_PTS(buffer);
        *pts_us = GST_CLOCK_TIME_IS_VALID(pts)
            ? static_cast<int64_t>(pts / GST_USECOND) : 0;
    }
    if (keyframe) {
        *keyframe = GST_BUFFER_FLAG_IS_SET(buffer, GST_BUFFER_FLAG_DELTA_UNIT)
            ? 0 : 1;
    }
    (void)format;
    return JALIUM_MEDIA_OK;
}

constexpr uint32_t DrmFourcc(char a, char b, char c, char d) noexcept
{
    return static_cast<uint32_t>(static_cast<uint8_t>(a)) |
           (static_cast<uint32_t>(static_cast<uint8_t>(b)) << 8) |
           (static_cast<uint32_t>(static_cast<uint8_t>(c)) << 16) |
           (static_cast<uint32_t>(static_cast<uint8_t>(d)) << 24);
}

uint32_t DrmFormatFromCaps(
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
    if (modifier_separator && modifier_separator[1]) {
        char* end = nullptr;
        const uint64_t parsed = std::strtoull(
            modifier_separator + 1, &end, 0);
        if (end && end != modifier_separator + 1) modifier = parsed;
    }

    if (std::strncmp(value, "NV12", 4) == 0) {
        format_hint = 1; // JALIUM_VS_FORMAT_NV12
        return DrmFourcc('N', 'V', '1', '2');
    }
    if (std::strncmp(value, "P010", 4) == 0 ||
        std::strncmp(value, "P010_10LE", 9) == 0) {
        format_hint = 2; // JALIUM_VS_FORMAT_P010
        return DrmFourcc('P', '0', '1', '0');
    }
    if (std::strncmp(value, "AR24", 4) == 0 ||
        std::strcmp(value, "BGRA") == 0) {
        format_hint = 0;
        return DrmFourcc('A', 'R', '2', '4');
    }
    if (std::strncmp(value, "XR24", 4) == 0 ||
        std::strcmp(value, "BGRx") == 0) {
        format_hint = 0;
        return DrmFourcc('X', 'R', '2', '4');
    }
    if (std::strncmp(value, "AB24", 4) == 0 ||
        std::strcmp(value, "RGBA") == 0) {
        format_hint = 0;
        return DrmFourcc('A', 'B', '2', '4');
    }
    if (std::strncmp(value, "XB24", 4) == 0 ||
        std::strcmp(value, "RGBx") == 0) {
        format_hint = 0;
        return DrmFourcc('X', 'B', '2', '4');
    }
    (void)requested_format;
    return 0;
}

bool IsVulkanRenderableRgbDmabuf(
    const GstStructure* structure,
    uint32_t drm_fourcc,
    uint32_t format_hint,
    uint32_t plane_count,
    guint memory_count) noexcept
{
    // The Vulkan importer currently has no immutable YCbCr sampler and does
    // not consume generic DMA_DRM/multi-plane layouts. Keep the producer's
    // success contract identical to what the renderer can actually import:
    // one DMABuf-backed packed RGB plane with an explicit supported fourcc.
    const char* format = structure
        ? gst_structure_get_string(structure, "format") : nullptr;
    if (!format || std::strcmp(format, "DMA_DRM") == 0 ||
        format_hint != 0 || plane_count != 1 || memory_count != 1) {
        return false;
    }
    return drm_fourcc == DrmFourcc('A', 'R', '2', '4') ||
           drm_fourcc == DrmFourcc('X', 'R', '2', '4') ||
           drm_fourcc == DrmFourcc('A', 'B', '2', '4') ||
           drm_fourcc == DrmFourcc('X', 'B', '2', '4');
}

jalium_media_status_t ExportDmabufSample(
    GstSample* sample,
    jalium_pixel_format_t requested_format,
    jalium_video_decoder_gpu_descriptor_t* descriptor,
    int64_t* pts_microseconds,
    int32_t* keyframe) noexcept
{
    if (!sample || !descriptor) return JALIUM_MEDIA_E_INVALID_ARG;
    ResetGpuDescriptor(descriptor);
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
    if (!IsVulkanRenderableRgbDmabuf(
            structure, drm_fourcc, format_hint, plane_count, memory_count)) {
        // NV12/P010, DMA_DRM and all multi-plane layouts use the precise-PTS
        // CPU reopen path until Vulkan YCbCr import is implemented.
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }

    descriptor->kind = 7; // JALIUM_VS_KIND_LINUX_DMABUF
    descriptor->width = static_cast<uint32_t>(width);
    descriptor->height = static_cast<uint32_t>(height);
    descriptor->format_hint = format_hint;
    descriptor->plane_count = plane_count;
    descriptor->drm_fourcc = drm_fourcc;

    for (uint32_t plane_index = 0; plane_index < plane_count; ++plane_index) {
        gsize global_offset = meta
            ? meta->offset[plane_index]
            : (plane_index == 0 ? 0 :
                static_cast<gsize>(width) * static_cast<gsize>(height));
        guint memory_index = std::min<guint>(plane_index, memory_count - 1);
        guint memory_length = 1;
        gsize skip = 0;
        if (meta) {
            (void)gst_buffer_find_memory(
                buffer, global_offset, 1, &memory_index, &memory_length, &skip);
        }
        GstMemory* memory = gst_buffer_peek_memory(buffer, memory_index);
        if (!memory || !gst_is_dmabuf_memory(memory)) {
            CloseGpuDescriptorFds(descriptor);
            return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
        }
        const int source_fd = gst_dmabuf_memory_get_fd(memory);
        const int exported_fd = source_fd >= 0 ? dup(source_fd) : -1;
        if (exported_fd < 0) {
            CloseGpuDescriptorFds(descriptor);
            return JALIUM_MEDIA_E_PLATFORM;
        }
        gsize memory_offset = 0;
        gsize maximum_size = 0;
        const gsize visible_size =
            gst_memory_get_sizes(memory, &memory_offset, &maximum_size);
        auto& output = descriptor->planes[plane_index];
        output.fd = exported_fd;
        output.stride_bytes = meta
            ? static_cast<uint32_t>(std::max<gint>(0, meta->stride[plane_index]))
            : (format_hint == 0 ? static_cast<uint32_t>(width) * 4u
                                : static_cast<uint32_t>(width) *
                                      (format_hint == 2 ? 2u : 1u));
        output.offset_bytes = static_cast<uint32_t>(memory_offset + skip);
        output.modifier = modifier;
        output.size_bytes = maximum_size != 0 ? maximum_size : visible_size;
    }
    descriptor->handle0 = static_cast<uint64_t>(
        static_cast<uint32_t>(descriptor->planes[0].fd));
    const GstClockTime pts = GST_BUFFER_PTS(buffer);
    if (pts_microseconds) {
        *pts_microseconds = GST_CLOCK_TIME_IS_VALID(pts)
            ? static_cast<int64_t>(pts / GST_USECOND) : 0;
    }
    if (keyframe) {
        *keyframe = GST_BUFFER_FLAG_IS_SET(buffer, GST_BUFFER_FLAG_DELTA_UNIT)
            ? 0 : 1;
    }
    // A duplicated fd keeps the GEM allocation alive but does not stop the
    // decoder pool from reusing and overwriting that surface. Give the
    // descriptor its own GstSample reference and expose retain/release hooks
    // so the Vulkan import can hold it through the submission fence.
    (void)gst_sample_ref(sample);
    descriptor->lifetime_context = static_cast<uint64_t>(
        reinterpret_cast<uintptr_t>(sample));
    descriptor->lifetime_retain_callback = static_cast<uint64_t>(
        reinterpret_cast<uintptr_t>(&RetainGstSampleLifetime));
    descriptor->lifetime_release_callback = static_cast<uint64_t>(
        reinterpret_cast<uintptr_t>(&ReleaseGstSampleLifetime));
    g_renderable_rgb_dmabuf_observed.store(true, std::memory_order_release);
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t DecodeImage(
    const uint8_t* data,
    size_t size,
    jalium_pixel_format_t format,
    jalium_image_t* out_image) noexcept
{
    if (!g_media_available) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;

    GstElement* pipeline = gst_pipeline_new(nullptr);
    GstElement* source = gst_element_factory_make("appsrc", nullptr);
    GstElement* decoder = gst_element_factory_make("decodebin", nullptr);
    GstElement* queue = gst_element_factory_make("queue", nullptr);
    GstElement* converter = gst_element_factory_make("videoconvert", nullptr);
    GstElement* filter = gst_element_factory_make("capsfilter", nullptr);
    GstElement* sink_element = gst_element_factory_make("appsink", nullptr);
    if (!pipeline || !source || !decoder || !queue || !converter || !filter ||
        !sink_element) {
        if (pipeline) gst_object_unref(pipeline);
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }

    GstCaps* raw_caps = gst_caps_new_simple(
        "video/x-raw", "format", G_TYPE_STRING, GstPixelFormat(format), nullptr);
    g_object_set(filter, "caps", raw_caps, nullptr);
    gst_caps_unref(raw_caps);
    g_object_set(source, "format", GST_FORMAT_BYTES, "is-live", FALSE,
                 "block", TRUE, nullptr);
    g_object_set(sink_element, "sync", FALSE, "max-buffers", 1u,
                 "drop", FALSE, nullptr);

    gst_bin_add_many(reinterpret_cast<GstBin*>(pipeline), source, decoder, queue, converter,
                     filter, sink_element, nullptr);
    if (!gst_element_link(source, decoder) ||
        !gst_element_link_many(queue, converter, filter, sink_element, nullptr)) {
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return JALIUM_MEDIA_E_PLATFORM;
    }

    DynamicPadContext link_context{queue, "video/x-raw"};
    g_signal_connect(decoder, "pad-added", G_CALLBACK(LinkDecodedPad),
                     &link_context);

    GstBuffer* input = gst_buffer_new_allocate(nullptr, size, nullptr);
    if (!input) {
        gst_object_unref(pipeline);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    gst_buffer_fill(input, 0, data, size);

    if (gst_element_set_state(pipeline, GST_STATE_PLAYING) ==
        GST_STATE_CHANGE_FAILURE) {
        gst_buffer_unref(input);
        const auto status = PipelineError(pipeline, JALIUM_MEDIA_E_DECODE_FAILED);
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return status;
    }
    const GstFlowReturn push_result = gst_app_src_push_buffer(
        reinterpret_cast<GstAppSrc*>(source), input);
    gst_app_src_end_of_stream(reinterpret_cast<GstAppSrc*>(source));
    if (push_result != GST_FLOW_OK) {
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    GstSample* sample = gst_app_sink_try_pull_sample(
        reinterpret_cast<GstAppSink*>(sink_element), 10 * GST_SECOND);
    jalium_media_status_t status = JALIUM_MEDIA_E_DECODE_FAILED;
    if (sample) {
        std::vector<uint8_t> pixels;
        uint32_t width = 0, height = 0, stride = 0;
        status = CopyVideoSample(sample, format, pixels, width, height, stride);
        if (status == JALIUM_MEDIA_OK) {
            void* output = jalium_media_aligned_alloc(pixels.size());
            if (!output) {
                status = JALIUM_MEDIA_E_OUT_OF_MEMORY;
            } else {
                std::memcpy(output, pixels.data(), pixels.size());
                out_image->width = width;
                out_image->height = height;
                out_image->stride_bytes = stride;
                out_image->format = format;
                out_image->pixels = static_cast<uint8_t*>(output);
                out_image->_reserved = output;
            }
        }
        gst_sample_unref(sample);
    } else {
        status = PipelineError(pipeline, JALIUM_MEDIA_E_UNSUPPORTED_FORMAT);
    }

    gst_element_set_state(pipeline, GST_STATE_NULL);
    gst_object_unref(pipeline);
    return status;
}

struct PngAncillaryChunk {
    uint32_t type = 0;
    std::vector<uint8_t> data;
};

struct ApngFrameData {
    uint32_t width = 0;
    uint32_t height = 0;
    uint32_t x = 0;
    uint32_t y = 0;
    uint16_t delay_numerator = 0;
    uint16_t delay_denominator = 0;
    uint8_t dispose = 0;
    uint8_t blend = 0;
    std::vector<uint8_t> compressed;
};

constexpr uint32_t PngChunkType(char a, char b, char c, char d) noexcept
{
    return (static_cast<uint32_t>(static_cast<uint8_t>(a)) << 24) |
           (static_cast<uint32_t>(static_cast<uint8_t>(b)) << 16) |
           (static_cast<uint32_t>(static_cast<uint8_t>(c)) << 8) |
           static_cast<uint32_t>(static_cast<uint8_t>(d));
}

uint16_t ReadBigEndian16(const uint8_t* value) noexcept
{
    return static_cast<uint16_t>(
        (static_cast<uint16_t>(value[0]) << 8) | value[1]);
}

void AppendBigEndian32(std::vector<uint8_t>& output, uint32_t value)
{
    output.push_back(static_cast<uint8_t>(value >> 24));
    output.push_back(static_cast<uint8_t>(value >> 16));
    output.push_back(static_cast<uint8_t>(value >> 8));
    output.push_back(static_cast<uint8_t>(value));
}

uint32_t PngCrc(const uint8_t* data, size_t size) noexcept
{
    uint32_t crc = 0xffffffffu;
    for (size_t i = 0; i < size; ++i) {
        crc ^= data[i];
        for (int bit = 0; bit < 8; ++bit) {
            const uint32_t mask = 0u - (crc & 1u);
            crc = (crc >> 1) ^ (0xedb88320u & mask);
        }
    }
    return crc ^ 0xffffffffu;
}

void AppendPngChunk(
    std::vector<uint8_t>& output,
    uint32_t type,
    const uint8_t* data,
    size_t size)
{
    AppendBigEndian32(output, static_cast<uint32_t>(size));
    const size_t crc_offset = output.size();
    AppendBigEndian32(output, type);
    if (size != 0) output.insert(output.end(), data, data + size);
    AppendBigEndian32(
        output, PngCrc(output.data() + crc_offset, size + 4));
}

bool IsPreservedPngChunk(uint32_t type) noexcept
{
    return type == PngChunkType('P', 'L', 'T', 'E') ||
           type == PngChunkType('t', 'R', 'N', 'S') ||
           type == PngChunkType('g', 'A', 'M', 'A') ||
           type == PngChunkType('c', 'H', 'R', 'M') ||
           type == PngChunkType('s', 'R', 'G', 'B') ||
           type == PngChunkType('i', 'C', 'C', 'P') ||
           type == PngChunkType('s', 'B', 'I', 'T') ||
           type == PngChunkType('p', 'H', 'Y', 's');
}

bool ParseApng(
    const uint8_t* data,
    size_t size,
    uint8_t (&ihdr)[13],
    std::vector<PngAncillaryChunk>& ancillary,
    std::vector<ApngFrameData>& frames) noexcept
{
    try {
        ancillary.clear();
        frames.clear();
        if (!IsAnimatedPng(data, size)) return false;

        size_t offset = 8;
        uint32_t declared_frames = 0;
        bool have_ihdr = false;
        bool saw_image_data = false;
        bool have_current = false;
        ApngFrameData current{};

        while (offset <= size && size - offset >= 12) {
            const uint32_t length = ReadBigEndian32(data + offset);
            const size_t chunk_size = static_cast<size_t>(length) + 12;
            if (chunk_size < 12 || chunk_size > size - offset) return false;
            const uint8_t* type_bytes = data + offset + 4;
            const uint32_t type = ReadBigEndian32(type_bytes);
            const uint8_t* payload = data + offset + 8;

            if (type == PngChunkType('I', 'H', 'D', 'R')) {
                if (length != 13 || have_ihdr) return false;
                std::memcpy(ihdr, payload, 13);
                have_ihdr = true;
            } else if (type == PngChunkType('a', 'c', 'T', 'L')) {
                if (length != 8) return false;
                declared_frames = ReadBigEndian32(payload);
            } else if (type == PngChunkType('f', 'c', 'T', 'L')) {
                if (length != 26) return false;
                if (have_current) {
                    if (current.compressed.empty()) return false;
                    frames.push_back(std::move(current));
                    current = {};
                }
                current.width = ReadBigEndian32(payload + 4);
                current.height = ReadBigEndian32(payload + 8);
                current.x = ReadBigEndian32(payload + 12);
                current.y = ReadBigEndian32(payload + 16);
                current.delay_numerator = ReadBigEndian16(payload + 20);
                current.delay_denominator = ReadBigEndian16(payload + 22);
                current.dispose = payload[24];
                current.blend = payload[25];
                have_current = true;
            } else if (type == PngChunkType('I', 'D', 'A', 'T')) {
                saw_image_data = true;
                // IDAT belongs to animation frame zero only when fcTL appeared
                // before it. Otherwise it is the APNG default/poster image.
                if (have_current && frames.empty()) {
                    current.compressed.insert(
                        current.compressed.end(), payload, payload + length);
                }
            } else if (type == PngChunkType('f', 'd', 'A', 'T')) {
                saw_image_data = true;
                if (!have_current || length < 4) return false;
                current.compressed.insert(
                    current.compressed.end(), payload + 4, payload + length);
            } else if (type == PngChunkType('I', 'E', 'N', 'D')) {
                break;
            } else if (!saw_image_data && IsPreservedPngChunk(type)) {
                PngAncillaryChunk chunk{};
                chunk.type = type;
                chunk.data.assign(payload, payload + length);
                ancillary.push_back(std::move(chunk));
            }
            offset += chunk_size;
        }

        if (have_current) {
            if (current.compressed.empty()) return false;
            frames.push_back(std::move(current));
        }
        if (!have_ihdr || declared_frames == 0 ||
            frames.size() != declared_frames) {
            return false;
        }

        const uint32_t canvas_width = ReadBigEndian32(ihdr);
        const uint32_t canvas_height = ReadBigEndian32(ihdr + 4);
        if (canvas_width == 0 || canvas_height == 0) return false;
        for (const auto& frame : frames) {
            if (frame.width == 0 || frame.height == 0 ||
                frame.x > canvas_width || frame.y > canvas_height ||
                frame.width > canvas_width - frame.x ||
                frame.height > canvas_height - frame.y ||
                frame.dispose > 2 || frame.blend > 1) {
                return false;
            }
        }
        return true;
    } catch (...) {
        ancillary.clear();
        frames.clear();
        return false;
    }
}

std::vector<uint8_t> BuildStandalonePng(
    const uint8_t (&source_ihdr)[13],
    const std::vector<PngAncillaryChunk>& ancillary,
    const ApngFrameData& frame)
{
    static constexpr uint8_t kPngSignature[] = {
        0x89, 'P', 'N', 'G', 0x0d, 0x0a, 0x1a, 0x0a
    };
    std::vector<uint8_t> png;
    png.reserve(64 + frame.compressed.size());
    png.insert(png.end(), std::begin(kPngSignature), std::end(kPngSignature));
    uint8_t ihdr[13];
    std::memcpy(ihdr, source_ihdr, sizeof(ihdr));
    ihdr[0] = static_cast<uint8_t>(frame.width >> 24);
    ihdr[1] = static_cast<uint8_t>(frame.width >> 16);
    ihdr[2] = static_cast<uint8_t>(frame.width >> 8);
    ihdr[3] = static_cast<uint8_t>(frame.width);
    ihdr[4] = static_cast<uint8_t>(frame.height >> 24);
    ihdr[5] = static_cast<uint8_t>(frame.height >> 16);
    ihdr[6] = static_cast<uint8_t>(frame.height >> 8);
    ihdr[7] = static_cast<uint8_t>(frame.height);
    AppendPngChunk(png, PngChunkType('I', 'H', 'D', 'R'), ihdr, sizeof(ihdr));
    for (const auto& chunk : ancillary) {
        AppendPngChunk(png, chunk.type, chunk.data.data(), chunk.data.size());
    }
    AppendPngChunk(png, PngChunkType('I', 'D', 'A', 'T'),
                   frame.compressed.data(), frame.compressed.size());
    AppendPngChunk(png, PngChunkType('I', 'E', 'N', 'D'), nullptr, 0);
    return png;
}

void ClearApngRect(
    std::vector<uint8_t>& canvas,
    uint32_t canvas_width,
    const ApngFrameData& frame) noexcept
{
    for (uint32_t y = 0; y < frame.height; ++y) {
        std::memset(
            canvas.data() +
                (static_cast<size_t>(frame.y + y) * canvas_width + frame.x) * 4,
            0, static_cast<size_t>(frame.width) * 4);
    }
}

void CompositeApngFrame(
    std::vector<uint8_t>& canvas,
    uint32_t canvas_width,
    const ApngFrameData& frame,
    const jalium_image_t& decoded) noexcept
{
    for (uint32_t y = 0; y < frame.height; ++y) {
        const uint8_t* source =
            decoded.pixels + static_cast<size_t>(y) * decoded.stride_bytes;
        uint8_t* target = canvas.data() +
            (static_cast<size_t>(frame.y + y) * canvas_width + frame.x) * 4;
        for (uint32_t x = 0; x < frame.width; ++x) {
            const uint8_t* src = source + static_cast<size_t>(x) * 4;
            uint8_t* dst = target + static_cast<size_t>(x) * 4;
            if (frame.blend == 0) {
                std::memcpy(dst, src, 4);
                continue;
            }

            const uint32_t source_alpha = src[3];
            const uint32_t destination_alpha = dst[3];
            const uint32_t inverse_alpha = 255 - source_alpha;
            const uint32_t output_alpha = source_alpha +
                (destination_alpha * inverse_alpha + 127) / 255;
            if (output_alpha == 0) {
                std::memset(dst, 0, 4);
                continue;
            }
            for (int channel = 0; channel < 3; ++channel) {
                const uint32_t premultiplied =
                    static_cast<uint32_t>(src[channel]) * source_alpha +
                    (static_cast<uint32_t>(dst[channel]) * destination_alpha *
                         inverse_alpha + 127) / 255;
                dst[channel] = static_cast<uint8_t>(
                    std::min(255u, (premultiplied + output_alpha / 2) /
                                       output_alpha));
            }
            dst[3] = static_cast<uint8_t>(output_alpha);
        }
    }
}

jalium_media_status_t DecodeApngFrame(
    const uint8_t* data,
    size_t size,
    uint32_t frame_index,
    jalium_pixel_format_t format,
    jalium_image_t* out_image,
    uint32_t* out_delay_ms,
    uint32_t* out_frame_count = nullptr) noexcept
{
    if (!g_media_available) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    try {
        uint8_t ihdr[13]{};
        std::vector<PngAncillaryChunk> ancillary;
        std::vector<ApngFrameData> frames;
        if (!ParseApng(data, size, ihdr, ancillary, frames)) {
            return JALIUM_MEDIA_E_DECODE_FAILED;
        }
        if (out_frame_count) {
            *out_frame_count = static_cast<uint32_t>(frames.size());
        }
        if (!out_image) return JALIUM_MEDIA_OK;
        if (frame_index >= frames.size()) return JALIUM_MEDIA_E_INVALID_ARG;

        const uint32_t canvas_width = ReadBigEndian32(ihdr);
        const uint32_t canvas_height = ReadBigEndian32(ihdr + 4);
        if (canvas_height > std::numeric_limits<size_t>::max() /
                                (static_cast<size_t>(canvas_width) * 4)) {
            return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        }
        const size_t canvas_size =
            static_cast<size_t>(canvas_width) * canvas_height * 4;
        std::vector<uint8_t> canvas(canvas_size, 0);
        std::vector<uint8_t> restore_canvas;

        for (uint32_t index = 0; index <= frame_index; ++index) {
            if (index > 0) {
                const auto& previous = frames[index - 1];
                if (previous.dispose == 1) {
                    ClearApngRect(canvas, canvas_width, previous);
                } else if (previous.dispose == 2 &&
                           restore_canvas.size() == canvas.size()) {
                    canvas = restore_canvas;
                }
            }

            const auto& frame = frames[index];
            if (frame.dispose == 2) restore_canvas = canvas;
            std::vector<uint8_t> png =
                BuildStandalonePng(ihdr, ancillary, frame);
            jalium_image_t decoded{};
            const auto status = DecodeImage(
                png.data(), png.size(), JALIUM_PF_RGBA8, &decoded);
            if (status != JALIUM_MEDIA_OK) return status;
            if (decoded.width != frame.width || decoded.height != frame.height ||
                decoded.format != JALIUM_PF_RGBA8 || !decoded.pixels) {
                jalium_image_free(&decoded);
                return JALIUM_MEDIA_E_DECODE_FAILED;
            }
            CompositeApngFrame(canvas, canvas_width, frame, decoded);
            jalium_image_free(&decoded);
        }

        auto* output = static_cast<uint8_t*>(
            jalium_media_aligned_alloc(canvas_size));
        if (!output) return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        std::memcpy(output, canvas.data(), canvas_size);
        if (format == JALIUM_PF_BGRA8) {
            jalium_media_swap_rb_inplace(
                output, canvas_width, canvas_height, canvas_width * 4);
        }
        out_image->width = canvas_width;
        out_image->height = canvas_height;
        out_image->stride_bytes = canvas_width * 4;
        out_image->format = format;
        out_image->pixels = output;
        out_image->_reserved = output;

        if (out_delay_ms) {
            const auto& frame = frames[frame_index];
            const uint32_t denominator =
                frame.delay_denominator == 0 ? 100 : frame.delay_denominator;
            const uint64_t milliseconds =
                (static_cast<uint64_t>(frame.delay_numerator) * 1000 +
                 denominator / 2) / denominator;
            *out_delay_ms = static_cast<uint32_t>(
                std::clamp<uint64_t>(milliseconds, 10,
                                     std::numeric_limits<uint32_t>::max()));
        }
        return JALIUM_MEDIA_OK;
    } catch (const std::bad_alloc&) {
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    } catch (...) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
}

bool HasDecoder(const char* caps_name) noexcept
{
    GstCaps* caps = gst_caps_new_empty_simple(caps_name);
    GList* factories = gst_element_factory_list_get_elements(
        static_cast<GstElementFactoryListType>(
            GST_ELEMENT_FACTORY_TYPE_DECODER | GST_ELEMENT_FACTORY_TYPE_MEDIA_VIDEO),
        GST_RANK_NONE);
    GList* matches = gst_element_factory_list_filter(
        factories, caps, GST_PAD_SINK, FALSE);
    const bool found = matches != nullptr;
    gst_plugin_feature_list_free(matches);
    gst_plugin_feature_list_free(factories);
    gst_caps_unref(caps);
    return found;
}

jalium_video_codec_t CodecFromCaps(GstCaps* caps) noexcept
{
    if (!caps || gst_caps_get_size(caps) == 0) return JALIUM_CODEC_NONE;
    const GstStructure* structure = gst_caps_get_structure(caps, 0);
    const char* name = structure ? gst_structure_get_name(structure) : nullptr;
    if (!name) return JALIUM_CODEC_NONE;
    if (std::strcmp(name, "video/x-h264") == 0) return JALIUM_CODEC_H264;
    if (std::strcmp(name, "video/x-h265") == 0 ||
        std::strcmp(name, "video/x-hevc") == 0) return JALIUM_CODEC_HEVC;
    if (std::strcmp(name, "video/x-vp9") == 0) return JALIUM_CODEC_VP9;
    if (std::strcmp(name, "video/x-av1") == 0) return JALIUM_CODEC_AV1;
    return JALIUM_CODEC_NONE;
}

void PopulateDiscoveredVideoInfo(const char* path,
                                 jalium_video_info_t& out_info) noexcept
{
    GError* error = nullptr;
    gchar* owned_uri = nullptr;
    const char* uri = ResolveGstUri(path, &owned_uri, &error);
    if (!uri) {
        if (error) g_error_free(error);
        return;
    }
    GstDiscoverer* discoverer = gst_discoverer_new(10 * GST_SECOND, &error);
    if (!discoverer) {
        if (error) g_error_free(error);
        g_free(owned_uri);
        return;
    }
    GstDiscovererInfo* discovered =
        gst_discoverer_discover_uri(discoverer, uri, &error);
    if (discovered) {
        const GstClockTime duration = gst_discoverer_info_get_duration(discovered);
        if (GST_CLOCK_TIME_IS_VALID(duration)) {
            out_info.duration_seconds =
                static_cast<double>(duration) / static_cast<double>(GST_SECOND);
        }
        GList* videos = gst_discoverer_info_get_video_streams(discovered);
        if (videos) {
            auto* video = reinterpret_cast<GstDiscovererVideoInfo*>(videos->data);
            const guint width = gst_discoverer_video_info_get_width(video);
            const guint height = gst_discoverer_video_info_get_height(video);
            const guint fps_n = gst_discoverer_video_info_get_framerate_num(video);
            const guint fps_d = gst_discoverer_video_info_get_framerate_denom(video);
            if (width) out_info.width = width;
            if (height) out_info.height = height;
            if (fps_n && fps_d) {
                out_info.frame_rate = static_cast<double>(fps_n) / fps_d;
            }
            GstCaps* encoded_caps = gst_discoverer_stream_info_get_caps(
                reinterpret_cast<GstDiscovererStreamInfo*>(video));
            out_info.active_codec = CodecFromCaps(encoded_caps);
            if (encoded_caps) gst_caps_unref(encoded_caps);
            gst_discoverer_stream_info_list_free(videos);
        }
        gst_discoverer_info_unref(discovered);
    }
    if (error) g_error_free(error);
    gst_object_unref(discoverer);
    g_free(owned_uri);

    if (out_info.frame_rate > 0.0 && out_info.duration_seconds > 0.0) {
        out_info.frame_count = static_cast<uint64_t>(
            std::llround(out_info.duration_seconds * out_info.frame_rate));
    }
}

jalium_media_status_t BuildUriDecodePipeline(
    const char* path,
    const char* media_prefix,
    GstElement* converter,
    GstElement* resampler,
    GstCaps* output_caps,
    uint32_t requested_stream_index,
    DynamicPadContext& link_context,
    GstElement** out_pipeline,
    GstAppSink** out_sink) noexcept
{
    *out_pipeline = nullptr;
    *out_sink = nullptr;
    GError* error = nullptr;
    gchar* owned_uri = nullptr;
    const char* uri = ResolveGstUri(path, &owned_uri, &error);
    if (!uri) {
        if (error) g_error_free(error);
        return JALIUM_MEDIA_E_IO;
    }

    GstElement* pipeline = gst_pipeline_new(nullptr);
    GstElement* decoder = gst_element_factory_make("uridecodebin", nullptr);
    GstElement* queue = gst_element_factory_make("queue", nullptr);
    GstElement* filter = gst_element_factory_make("capsfilter", nullptr);
    GstElement* sink_element = gst_element_factory_make("appsink", nullptr);
    if (!pipeline || !decoder || !queue || !converter || !filter || !sink_element) {
        if (pipeline) gst_object_unref(pipeline);
        else {
            if (decoder) gst_object_unref(decoder);
            if (queue) gst_object_unref(queue);
            if (converter) gst_object_unref(converter);
            if (resampler) gst_object_unref(resampler);
            if (filter) gst_object_unref(filter);
            if (sink_element) gst_object_unref(sink_element);
        }
        g_free(owned_uri);
        return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    }

    g_object_set(decoder, "uri", uri, nullptr);
    g_object_set(filter, "caps", output_caps, nullptr);
    g_object_set(sink_element, "sync", FALSE, "max-buffers", 4u,
                 "drop", FALSE, nullptr);
    g_free(owned_uri);

    if (resampler) {
        gst_bin_add_many(reinterpret_cast<GstBin*>(pipeline), decoder, queue, converter,
                         resampler, filter, sink_element, nullptr);
        if (!gst_element_link_many(queue, converter, resampler, filter,
                                   sink_element, nullptr)) {
            gst_object_unref(pipeline);
            return JALIUM_MEDIA_E_PLATFORM;
        }
    } else {
        gst_bin_add_many(reinterpret_cast<GstBin*>(pipeline), decoder, queue, converter,
                         filter, sink_element, nullptr);
        if (!gst_element_link_many(queue, converter, filter, sink_element,
                                   nullptr)) {
            gst_object_unref(pipeline);
            return JALIUM_MEDIA_E_PLATFORM;
        }
    }

    link_context.target = queue;
    link_context.media_prefix = media_prefix;
    link_context.sink_element = sink_element;
    link_context.requested_stream_index = requested_stream_index;
    link_context.matching_pad_count.store(0, std::memory_order_release);
    link_context.no_matching_pad.store(false, std::memory_order_release);
    g_signal_connect(decoder, "pad-added", G_CALLBACK(LinkDecodedPad),
                     &link_context);
    g_signal_connect(decoder, "no-more-pads", G_CALLBACK(HandleNoMorePads),
                     &link_context);
    if (gst_element_set_state(pipeline, GST_STATE_PLAYING) ==
        GST_STATE_CHANGE_FAILURE) {
        const auto status = PipelineError(pipeline, JALIUM_MEDIA_E_DECODE_FAILED);
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return status;
    }
    *out_pipeline = pipeline;
    *out_sink = reinterpret_cast<GstAppSink*>(sink_element);
    return JALIUM_MEDIA_OK;
}

void CloseVideoPipeline(jalium_video_decoder* decoder) noexcept
{
    if (!decoder) return;
    if (decoder->pending_sample) {
        gst_sample_unref(decoder->pending_sample);
        decoder->pending_sample = nullptr;
    }
    if (decoder->pipeline) {
        gst_element_set_state(decoder->pipeline, GST_STATE_NULL);
        gst_object_unref(decoder->pipeline);
        decoder->pipeline = nullptr;
    }
    decoder->sink = nullptr;
}

jalium_media_status_t OpenCpuVideoPipeline(
    jalium_video_decoder* decoder,
    int64_t seek_microseconds = 0) noexcept
{
    GstElement* converter = gst_element_factory_make("videoconvert", nullptr);
    GstCaps* caps = gst_caps_new_simple(
        "video/x-raw", "format", G_TYPE_STRING,
        GstPixelFormat(decoder->format), nullptr);
    if (!caps) {
        if (converter) gst_object_unref(converter);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    const auto status = BuildUriDecodePipeline(
        decoder->source.c_str(), "video/x-raw", converter, nullptr, caps, 0,
        decoder->link_context, &decoder->pipeline, &decoder->sink);
    gst_caps_unref(caps);
    if (status != JALIUM_MEDIA_OK) return status;

    if (seek_microseconds > 0 &&
        !gst_element_seek_simple(
            decoder->pipeline, GST_FORMAT_TIME,
            static_cast<GstSeekFlags>(GST_SEEK_FLAG_FLUSH |
                                      GST_SEEK_FLAG_ACCURATE),
            static_cast<gint64>(seek_microseconds) * GST_USECOND)) {
        CloseVideoPipeline(decoder);
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
    decoder->pending_sample = gst_app_sink_try_pull_sample(
        decoder->sink, 15 * GST_SECOND);
    if (!decoder->pending_sample) {
        const auto pull_status =
            decoder->link_context.no_matching_pad.load(std::memory_order_acquire)
                ? JALIUM_MEDIA_E_UNSUPPORTED_FORMAT
                : PipelineError(decoder->pipeline,
                                JALIUM_MEDIA_E_UNSUPPORTED_CODEC);
        CloseVideoPipeline(decoder);
        return pull_status;
    }
    decoder->dmabuf_mode = false;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t OpenDmabufVideoPipeline(
    jalium_video_decoder* decoder) noexcept
{
    GstElement* postprocess = gst_element_factory_make("vapostproc", nullptr);
    if (!postprocess) {
        postprocess = gst_element_factory_make("vaapipostproc", nullptr);
    }
    if (!postprocess) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;

    GstCaps* caps = gst_caps_from_string(
        "video/x-raw(memory:DMABuf),"
        // Only negotiate layouts the current Vulkan importer can consume.
        // Common VA NV12/P010 and generic DMA_DRM layouts intentionally fail
        // this branch and reopen at the same PTS through the CPU pipeline.
        "format=(string){BGRA,BGRx,RGBA,RGBx}");
    if (!caps) {
        gst_object_unref(postprocess);
        return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    }
    const auto status = BuildUriDecodePipeline(
        decoder->source.c_str(), "video/x-raw", postprocess, nullptr, caps, 0,
        decoder->link_context, &decoder->pipeline, &decoder->sink);
    gst_caps_unref(caps);
    if (status != JALIUM_MEDIA_OK) return status;

    decoder->pending_sample = gst_app_sink_try_pull_sample(
        decoder->sink, 5 * GST_SECOND);
    if (!decoder->pending_sample) {
        const auto pull_status = PipelineError(
            decoder->pipeline, JALIUM_MEDIA_E_NOT_IMPLEMENTED);
        CloseVideoPipeline(decoder);
        return pull_status;
    }

    jalium_video_decoder_gpu_descriptor_t probe{};
    const auto probe_status = ExportDmabufSample(
        decoder->pending_sample, decoder->format, &probe, nullptr, nullptr);
    CloseGpuDescriptorFds(&probe);
    if (probe_status != JALIUM_MEDIA_OK) {
        CloseVideoPipeline(decoder);
        return probe_status;
    }
    decoder->dmabuf_mode = true;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t SwitchVideoDecoderToCpu(
    jalium_video_decoder* decoder) noexcept
{
    if (!decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    const int64_t resume_at = decoder->last_pts_microseconds;
    CloseVideoPipeline(decoder);
    decoder->dmabuf_mode = false;
    return OpenCpuVideoPipeline(decoder, resume_at);
}

std::string GstDeviceId(GstDevice* device)
{
    std::string id;
    GstStructure* properties = gst_device_get_properties(device);
    if (properties) {
        constexpr const char* keys[] = {
            "device.path", "api.v4l2.path", "object.path", "node.name",
            "device.id"
        };
        for (const char* key : keys) {
            const char* value = gst_structure_get_string(properties, key);
            if (value && *value) {
                id = value;
                break;
            }
        }
        gst_structure_free(properties);
    }
    if (id.empty()) {
        const char* display_name = gst_device_get_display_name(device);
        if (display_name) id = display_name;
    }
    return id;
}

char* DuplicateString(const std::string& value) noexcept
{
    auto* result = new (std::nothrow) char[value.size() + 1];
    if (!result) return nullptr;
    std::memcpy(result, value.c_str(), value.size() + 1);
    return result;
}

std::vector<jalium_camera_format_t> GstDeviceFormats(GstDevice* device)
{
    std::vector<jalium_camera_format_t> formats;
    GstCaps* caps = gst_device_get_caps(device);
    if (caps) {
        const guint count = gst_caps_get_size(caps);
        for (guint i = 0; i < count; ++i) {
            const GstStructure* structure = gst_caps_get_structure(caps, i);
            gint width = 0, height = 0, fps_n = 0, fps_d = 1;
            if (!gst_structure_get_int(structure, "width", &width) ||
                !gst_structure_get_int(structure, "height", &height)) {
                continue;
            }
            (void)gst_structure_get_fraction(
                structure, "framerate", &fps_n, &fps_d);
            jalium_camera_format_t format{
                static_cast<uint32_t>(width), static_cast<uint32_t>(height),
                fps_n > 0 && fps_d > 0 ? static_cast<double>(fps_n) / fps_d
                                       : 30.0
            };
            const auto duplicate = std::find_if(
                formats.begin(), formats.end(), [&](const auto& existing) {
                    return existing.width == format.width &&
                           existing.height == format.height &&
                           std::abs(existing.fps - format.fps) < 0.01;
                });
            if (duplicate == formats.end()) formats.push_back(format);
        }
        gst_caps_unref(caps);
    }
    if (formats.empty()) formats.push_back({640, 480, 30.0});
    return formats;
}

GList* GetDevices(const char* device_class,
                  GstDeviceMonitor** out_monitor) noexcept
{
    *out_monitor = gst_device_monitor_new();
    if (!*out_monitor) return nullptr;
    (void)gst_device_monitor_add_filter(*out_monitor, device_class, nullptr);
    if (!gst_device_monitor_start(*out_monitor)) {
        gst_object_unref(*out_monitor);
        *out_monitor = nullptr;
        return nullptr;
    }
    return gst_device_monitor_get_devices(*out_monitor);
}

GList* GetVideoDevices(GstDeviceMonitor** out_monitor) noexcept
{
    return GetDevices("Video/Source", out_monitor);
}

void ReleaseVideoDevices(GstDeviceMonitor* monitor, GList* devices) noexcept
{
    if (devices) g_list_free_full(devices, gst_object_unref);
    if (monitor) {
        gst_device_monitor_stop(monitor);
        gst_object_unref(monitor);
    }
}

GstDevice* FindVideoDevice(const char* requested_id) noexcept
{
    GstDeviceMonitor* monitor = nullptr;
    GList* devices = GetVideoDevices(&monitor);
    GstDevice* match = nullptr;
    for (GList* node = devices; node; node = node->next) {
        auto* device = reinterpret_cast<GstDevice*>(node->data);
        if (GstDeviceId(device) == requested_id) {
            match = reinterpret_cast<GstDevice*>(gst_object_ref(device));
            break;
        }
    }
    ReleaseVideoDevices(monitor, devices);
    return match;
}

GstDevice* FindDevice(const char* device_class,
                      const char* requested_id) noexcept
{
    GstDeviceMonitor* monitor = nullptr;
    GList* devices = GetDevices(device_class, &monitor);
    GstDevice* match = nullptr;
    for (GList* node = devices; node; node = node->next) {
        auto* device = reinterpret_cast<GstDevice*>(node->data);
        if (GstDeviceId(device) == requested_id) {
            match = reinterpret_cast<GstDevice*>(gst_object_ref(device));
            break;
        }
    }
    ReleaseVideoDevices(monitor, devices);
    return match;
}

jalium_media_status_t CopyAudioSample(
    GstSample* sample,
    std::vector<float>& samples,
    uint32_t& sample_rate,
    uint32_t& channels,
    int64_t& pts_us) noexcept
{
    if (!sample) return JALIUM_MEDIA_E_DECODE_FAILED;
    GstCaps* caps = gst_sample_get_caps(sample);
    GstBuffer* buffer = gst_sample_get_buffer(sample);
    GstAudioInfo info;
    if (!caps || !buffer || !gst_audio_info_from_caps(&info, caps)) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
    sample_rate = GST_AUDIO_INFO_RATE(&info);
    channels = GST_AUDIO_INFO_CHANNELS(&info);
    if (sample_rate == 0 || channels == 0) return JALIUM_MEDIA_E_DECODE_FAILED;

    GstMapInfo map{};
    if (!gst_buffer_map(buffer, &map, GST_MAP_READ)) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
    const size_t sample_count = map.size / sizeof(float);
    if (sample_count < channels) {
        gst_buffer_unmap(buffer, &map);
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
    try {
        samples.assign(reinterpret_cast<const float*>(map.data),
                       reinterpret_cast<const float*>(map.data) + sample_count);
    } catch (...) {
        gst_buffer_unmap(buffer, &map);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    gst_buffer_unmap(buffer, &map);

    const GstClockTime pts = GST_BUFFER_PTS(buffer);
    pts_us = GST_CLOCK_TIME_IS_VALID(pts)
        ? static_cast<int64_t>(pts / GST_USECOND) : 0;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t CopySubtitleSample(
    GstSample* sample,
    std::string& text,
    int64_t& start_us,
    int64_t& duration_us) noexcept
{
    if (!sample) return JALIUM_MEDIA_E_DECODE_FAILED;
    GstBuffer* buffer = gst_sample_get_buffer(sample);
    GstMapInfo map{};
    if (!buffer || !gst_buffer_map(buffer, &map, GST_MAP_READ)) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
    try {
        text.assign(reinterpret_cast<const char*>(map.data), map.size);
        while (!text.empty() && text.back() == '\0') text.pop_back();
    } catch (...) {
        gst_buffer_unmap(buffer, &map);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    gst_buffer_unmap(buffer, &map);

    const GstClockTime pts = GST_BUFFER_PTS(buffer);
    const GstClockTime duration = GST_BUFFER_DURATION(buffer);
    start_us = GST_CLOCK_TIME_IS_VALID(pts)
        ? static_cast<int64_t>(pts / GST_USECOND) : 0;
    duration_us = GST_CLOCK_TIME_IS_VALID(duration)
        ? static_cast<int64_t>(duration / GST_USECOND) : 0;
    return JALIUM_MEDIA_OK;
}

std::string TrackTag(GstDiscovererStreamInfo* stream,
                     const char* tag_name)
{
    const GstTagList* tags = gst_discoverer_stream_info_get_tags(stream);
    if (!tags) return {};
    gchar* value = nullptr;
    if (!gst_tag_list_get_string(tags, tag_name, &value) || !value) return {};
    std::string result(value);
    g_free(value);
    return result;
}

jalium_media_status_t FillTrack(
    jalium_media_track_info_t& output,
    GstDiscovererStreamInfo* stream,
    jalium_media_track_kind_t kind,
    uint32_t index) noexcept
{
    std::string id;
    if (const char* value = gst_discoverer_stream_info_get_stream_id(stream)) {
        id = value;
    }
    std::string language = TrackTag(stream, GST_TAG_LANGUAGE_CODE);
    if (language.empty()) language = TrackTag(stream, GST_TAG_LANGUAGE_NAME);
    std::string label = TrackTag(stream, GST_TAG_TITLE);
    if (label.empty()) {
        label = kind == JALIUM_MEDIA_TRACK_AUDIO ? "Audio " : "Subtitle ";
        label += std::to_string(index + 1);
    }
    std::string codec;
    GstCaps* caps = gst_discoverer_stream_info_get_caps(stream);
    if (caps && gst_caps_get_size(caps) > 0) {
        const GstStructure* structure = gst_caps_get_structure(caps, 0);
        const char* name = structure ? gst_structure_get_name(structure) : nullptr;
        if (name) codec = name;
    }
    if (caps) gst_caps_unref(caps);

    output.kind = kind;
    output.index = index;
    output.id = DuplicateString(id);
    output.label = DuplicateString(label);
    output.language = DuplicateString(language);
    output.codec = DuplicateString(codec);
    output.is_default = index == 0 ? 1 : 0;
    output.is_forced = 0;
    if (kind == JALIUM_MEDIA_TRACK_AUDIO) {
        auto* audio = reinterpret_cast<GstDiscovererAudioInfo*>(stream);
        output.channels = gst_discoverer_audio_info_get_channels(audio);
        output.sample_rate = gst_discoverer_audio_info_get_sample_rate(audio);
    }
    return output.id && output.label && output.language && output.codec
        ? JALIUM_MEDIA_OK : JALIUM_MEDIA_E_OUT_OF_MEMORY;
}

class GstAudioDecoder final : public jalium::audio::audio_decoder_impl {
public:
    GstElement* pipeline = nullptr;
    GstAppSink* sink = nullptr;
    DynamicPadContext link_context{};
    std::vector<float> pending;
    size_t pending_offset = 0;
    bool eos = false;

    ~GstAudioDecoder() override
    {
        if (pipeline) {
            gst_element_set_state(pipeline, GST_STATE_NULL);
            gst_object_unref(pipeline);
        }
    }

    bool AppendSample(GstSample* sample) noexcept
    {
        if (!sample) return false;
        GstBuffer* buffer = gst_sample_get_buffer(sample);
        GstMapInfo map{};
        if (!buffer || !gst_buffer_map(buffer, &map, GST_MAP_READ)) return false;
        const size_t sample_count = map.size / sizeof(float);
        try {
            if (pending_offset >= pending.size()) {
                pending.assign(reinterpret_cast<const float*>(map.data),
                               reinterpret_cast<const float*>(map.data) + sample_count);
                pending_offset = 0;
            } else {
                pending.insert(pending.end(),
                               reinterpret_cast<const float*>(map.data),
                               reinterpret_cast<const float*>(map.data) + sample_count);
            }
        } catch (...) {
            gst_buffer_unmap(buffer, &map);
            return false;
        }
        gst_buffer_unmap(buffer, &map);
        return true;
    }

    uint32_t ReadFramesImpl(float* dst, uint32_t frame_capacity) noexcept override
    {
        if (!dst || frame_capacity == 0 || channels == 0) return 0;
        uint32_t written = 0;
        while (written < frame_capacity) {
            const size_t available_samples = pending.size() - pending_offset;
            const uint32_t available_frames = static_cast<uint32_t>(
                available_samples / channels);
            if (available_frames > 0) {
                const uint32_t take = std::min(
                    available_frames, frame_capacity - written);
                const size_t take_samples = static_cast<size_t>(take) * channels;
                std::memcpy(dst + static_cast<size_t>(written) * channels,
                            pending.data() + pending_offset,
                            take_samples * sizeof(float));
                pending_offset += take_samples;
                written += take;
                if (pending_offset >= pending.size()) {
                    pending.clear();
                    pending_offset = 0;
                }
                continue;
            }
            if (eos) break;
            GstSample* sample = gst_app_sink_try_pull_sample(sink, 10 * GST_SECOND);
            if (!sample) {
                eos = gst_app_sink_is_eos(sink) != FALSE;
                break;
            }
            const bool appended = AppendSample(sample);
            gst_sample_unref(sample);
            if (!appended) break;
        }
        if (written > 0) {
            jalium::audio::StatsDecoderFrames(codec, written);
        }
        return written;
    }

    jalium_media_status_t SeekImpl(int64_t position_us) noexcept override
    {
        if (!pipeline || !sink) return JALIUM_MEDIA_E_INVALID_ARG;
        if (position_us < 0) position_us = 0;
        pending.clear();
        pending_offset = 0;
        eos = false;
        while (GstSample* sample = gst_app_sink_try_pull_sample(sink, 0)) {
            gst_sample_unref(sample);
        }
        return SeekPipelineAccurately(pipeline, position_us);
    }
};

jalium::audio::audio_decoder_impl* OpenGstAudioFile(
    const char* path,
    uint32_t track_index,
    jalium_audio_codec_t reported_codec,
    jalium_media_status_t& out_status) noexcept
{
    out_status = JALIUM_MEDIA_OK;
    if (!path || !*path) {
        out_status = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    if (!IsUri(path) && !FileExists(path)) {
        out_status = JALIUM_MEDIA_E_IO;
        return nullptr;
    }
    if (!g_media_available) {
        out_status = JALIUM_MEDIA_E_NOT_IMPLEMENTED;
        return nullptr;
    }

    auto* decoder = new (std::nothrow) GstAudioDecoder();
    if (!decoder) {
        out_status = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    GstElement* converter = gst_element_factory_make("audioconvert", nullptr);
    GstElement* resampler = gst_element_factory_make("audioresample", nullptr);
    GstCaps* caps = gst_caps_new_simple(
        "audio/x-raw",
        "format", G_TYPE_STRING, "F32LE",
        "layout", G_TYPE_STRING, "interleaved",
        nullptr);
    out_status = BuildUriDecodePipeline(
        path, "audio/x-raw", converter, resampler, caps, track_index,
        decoder->link_context, &decoder->pipeline, &decoder->sink);
    gst_caps_unref(caps);
    if (out_status != JALIUM_MEDIA_OK) {
        if (decoder->pipeline) {
            gst_element_set_state(decoder->pipeline, GST_STATE_NULL);
            gst_object_unref(decoder->pipeline);
            decoder->pipeline = nullptr;
        }
        delete decoder;
        return nullptr;
    }

    GstSample* first = gst_app_sink_try_pull_sample(decoder->sink, 15 * GST_SECOND);
    if (!first) {
        out_status =
            decoder->link_context.no_matching_pad.load(std::memory_order_acquire)
                ? JALIUM_MEDIA_E_UNSUPPORTED_FORMAT // no audio stream in the file
                : PipelineError(decoder->pipeline, JALIUM_MEDIA_E_UNSUPPORTED_CODEC);
        delete decoder;
        return nullptr;
    }
    GstAudioInfo info;
    GstCaps* sample_caps = gst_sample_get_caps(first);
    if (!sample_caps || !gst_audio_info_from_caps(&info, sample_caps) ||
        GST_AUDIO_INFO_RATE(&info) == 0 || GST_AUDIO_INFO_CHANNELS(&info) == 0) {
        gst_sample_unref(first);
        out_status = JALIUM_MEDIA_E_DECODE_FAILED;
        delete decoder;
        return nullptr;
    }
    decoder->sample_rate = GST_AUDIO_INFO_RATE(&info);
    decoder->channels = GST_AUDIO_INFO_CHANNELS(&info);
    decoder->codec = reported_codec;
    gint64 duration = GST_CLOCK_TIME_NONE;
    if (gst_element_query_duration(
            decoder->pipeline, GST_FORMAT_TIME, &duration) && duration >= 0) {
        decoder->duration_us = duration / GST_USECOND;
    }
    if (!decoder->AppendSample(first)) {
        gst_sample_unref(first);
        out_status = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        delete decoder;
        return nullptr;
    }
    gst_sample_unref(first);
    jalium::audio::StatsDecoderOpened(reported_codec);
    return decoder;
}

jalium::audio::audio_decoder_impl* OpenGstAacFile(
    const char* path, jalium_media_status_t& out_status) noexcept
{
    return OpenGstAudioFile(path, 0, JALIUM_ACODEC_AAC, out_status);
}

jalium_subtitle_decoder* OpenGstSubtitle(
    const char* path,
    uint32_t track_index,
    jalium_media_status_t& out_status) noexcept
{
    out_status = JALIUM_MEDIA_OK;
    if (!path || !*path) {
        out_status = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    if (!IsUri(path) && !FileExists(path)) {
        out_status = JALIUM_MEDIA_E_IO;
        return nullptr;
    }
    if (!g_media_available) {
        out_status = JALIUM_MEDIA_E_NOT_IMPLEMENTED;
        return nullptr;
    }

    auto* decoder = new (std::nothrow) jalium_subtitle_decoder();
    if (!decoder) {
        out_status = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    GstElement* identity = gst_element_factory_make("identity", nullptr);
    // decodebin may expose subtitle parsers with already-fixed
    // `pango-markup` caps even when the payload itself is plain UTF-8. A
    // downstream utf8-only capsfilter makes gst_pad_link return NOFORMAT and
    // incorrectly reports that the container has no subtitle stream. Accept
    // both standard text/x-raw representations; the ABI deliberately returns
    // the UTF-8 byte payload unchanged so callers can preserve inline markup.
    GstCaps* caps = gst_caps_new_empty_simple("text/x-raw");
    out_status = BuildUriDecodePipeline(
        path, "text/", identity, nullptr, caps, track_index,
        decoder->link_context, &decoder->pipeline, &decoder->sink);
    gst_caps_unref(caps);
    if (out_status != JALIUM_MEDIA_OK) {
        if (decoder->pipeline) {
            gst_element_set_state(decoder->pipeline, GST_STATE_NULL);
            gst_object_unref(decoder->pipeline);
            decoder->pipeline = nullptr;
        }
        delete decoder;
        return nullptr;
    }

    decoder->pending_sample = gst_app_sink_try_pull_sample(
        decoder->sink, 15 * GST_SECOND);
    if (!decoder->pending_sample) {
        out_status = decoder->link_context.no_matching_pad.load(
            std::memory_order_acquire)
            ? JALIUM_MEDIA_E_UNSUPPORTED_FORMAT
            : PipelineError(decoder->pipeline, JALIUM_MEDIA_E_DECODE_FAILED);
        gst_element_set_state(decoder->pipeline, GST_STATE_NULL);
        gst_object_unref(decoder->pipeline);
        decoder->pipeline = nullptr;
        delete decoder;
        return nullptr;
    }
    return decoder;
}

#endif // JALIUM_HAS_GSTREAMER

} // namespace

extern "C" {

JALIUM_MEDIA_API jalium_media_status_t jalium_media_initialize(void)
{
    std::lock_guard<std::mutex> lock(g_init_mutex);
    if (g_init_count++ > 0) return JALIUM_MEDIA_OK;
#if JALIUM_HAS_GSTREAMER
    if (jalium::media::gst_runtime::Load()) {
        GError* error = nullptr;
        g_media_available = gst_init_check(nullptr, nullptr, &error) != FALSE;
        if (error) g_error_free(error);
        if (!g_media_available) jalium::media::gst_runtime::Unload();
    }
    if (g_media_available) {
        jalium::audio::RegisterAacDecoderHooks(&OpenGstAacFile, nullptr);
    }
#else
    g_media_available = false;
#endif
    // Optional media capability must never prevent the UI process from
    // starting. Individual entry points report their precise fallback status.
    return JALIUM_MEDIA_OK;
}

JALIUM_MEDIA_API void jalium_media_shutdown(void)
{
    std::lock_guard<std::mutex> lock(g_init_mutex);
    if (g_init_count > 0) --g_init_count;
    if (g_init_count == 0) {
#if JALIUM_HAS_GSTREAMER
        jalium::audio::RegisterAacDecoderHooks(nullptr, nullptr);
#endif
        g_media_available = false;
    }
}

JALIUM_MEDIA_API uint32_t jalium_media_supported_video_codecs(void)
{
#if JALIUM_HAS_GSTREAMER
    if (!g_media_available) return 0;
    uint32_t codecs = 0;
    if (HasDecoder("video/x-h264")) codecs |= JALIUM_CODEC_H264;
    if (HasDecoder("video/x-h265")) codecs |= JALIUM_CODEC_HEVC;
    if (HasDecoder("video/x-vp9"))  codecs |= JALIUM_CODEC_VP9;
    if (HasDecoder("video/x-av1"))  codecs |= JALIUM_CODEC_AV1;
    return codecs;
#else
    return 0;
#endif
}

JALIUM_MEDIA_API uint32_t jalium_linux_media_capabilities(void)
{
#if JALIUM_HAS_GSTREAMER
    if (!g_media_available) return 0;
    uint32_t capabilities = JALIUM_LINUX_MEDIA_GSTREAMER_RUNTIME |
           JALIUM_LINUX_MEDIA_VIDEO_CPU_FRAMES |
           JALIUM_LINUX_MEDIA_AUDIO_DECODE |
           JALIUM_LINUX_MEDIA_CAMERA_CAPTURE |
           JALIUM_LINUX_MEDIA_MIC_CAPTURE |
           JALIUM_LINUX_MEDIA_TRACK_DISCOVERY |
           JALIUM_LINUX_MEDIA_SUBTITLE_DECODE;
    // Creating a VA post-process element proves only that a plugin exists; it
    // does not prove the active driver can negotiate/export a layout Vulkan
    // accepts. Advertise this bit only after an actual decoder sample has
    // exported a single-plane packed RGB descriptor successfully.
    if (g_renderable_rgb_dmabuf_observed.load(std::memory_order_acquire)) {
        capabilities |= JALIUM_LINUX_MEDIA_DMABUF_EXPORT;
    }
    return capabilities;
#else
    return 0;
#endif
}

JALIUM_MEDIA_API jalium_media_status_t jalium_media_discover_tracks(
    const char* utf8_path_or_uri,
    jalium_media_track_info_t** out_tracks,
    uint32_t* out_count)
{
    if (!utf8_path_or_uri || !out_tracks || !out_count) {
        return JALIUM_MEDIA_E_INVALID_ARG;
    }
    *out_tracks = nullptr;
    *out_count = 0;
#if JALIUM_HAS_GSTREAMER
    if (!g_media_available) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    if (!IsUri(utf8_path_or_uri) && !FileExists(utf8_path_or_uri)) {
        return JALIUM_MEDIA_E_IO;
    }

    GError* error = nullptr;
    gchar* owned_uri = nullptr;
    const char* uri = ResolveGstUri(utf8_path_or_uri, &owned_uri, &error);
    if (!uri) {
        if (error) g_error_free(error);
        return JALIUM_MEDIA_E_IO;
    }
    GstDiscoverer* discoverer = gst_discoverer_new(15 * GST_SECOND, &error);
    if (!discoverer) {
        if (error) g_error_free(error);
        g_free(owned_uri);
        return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    }
    GstDiscovererInfo* info = gst_discoverer_discover_uri(
        discoverer, uri, &error);
    g_free(owned_uri);
    if (!info) {
        const jalium_media_status_t status = error && error->message &&
            std::strstr(error->message, "Permission")
            ? JALIUM_MEDIA_E_PERMISSION_DENIED
            : JALIUM_MEDIA_E_DECODE_FAILED;
        if (error) g_error_free(error);
        gst_object_unref(discoverer);
        return status;
    }
    if (error) g_error_free(error);

    GList* audio = gst_discoverer_info_get_audio_streams(info);
    GList* subtitles = gst_discoverer_info_get_subtitle_streams(info);
    const uint32_t audio_count = static_cast<uint32_t>(g_list_length(audio));
    const uint32_t subtitle_count = static_cast<uint32_t>(g_list_length(subtitles));
    const uint32_t count = audio_count + subtitle_count;
    if (count == 0) {
        gst_discoverer_stream_info_list_free(audio);
        gst_discoverer_stream_info_list_free(subtitles);
        gst_discoverer_info_unref(info);
        gst_object_unref(discoverer);
        return JALIUM_MEDIA_OK;
    }

    auto* tracks = new (std::nothrow) jalium_media_track_info_t[count]{};
    if (!tracks) {
        gst_discoverer_stream_info_list_free(audio);
        gst_discoverer_stream_info_list_free(subtitles);
        gst_discoverer_info_unref(info);
        gst_object_unref(discoverer);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }

    jalium_media_status_t status = JALIUM_MEDIA_OK;
    uint32_t output_index = 0;
    uint32_t kind_index = 0;
    for (GList* node = audio; node; node = node->next, ++kind_index) {
        status = FillTrack(tracks[output_index++],
                           reinterpret_cast<GstDiscovererStreamInfo*>(node->data),
                           JALIUM_MEDIA_TRACK_AUDIO, kind_index);
        if (status != JALIUM_MEDIA_OK) break;
    }
    kind_index = 0;
    if (status == JALIUM_MEDIA_OK) {
        for (GList* node = subtitles; node; node = node->next, ++kind_index) {
            status = FillTrack(tracks[output_index++],
                               reinterpret_cast<GstDiscovererStreamInfo*>(node->data),
                               JALIUM_MEDIA_TRACK_SUBTITLE, kind_index);
            if (status != JALIUM_MEDIA_OK) break;
        }
    }
    gst_discoverer_stream_info_list_free(audio);
    gst_discoverer_stream_info_list_free(subtitles);
    gst_discoverer_info_unref(info);
    gst_object_unref(discoverer);

    if (status != JALIUM_MEDIA_OK) {
        jalium_media_tracks_free(tracks, count);
        return status;
    }
    *out_tracks = tracks;
    *out_count = count;
    return JALIUM_MEDIA_OK;
#else
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API void jalium_media_tracks_free(
    jalium_media_track_info_t* tracks,
    uint32_t count)
{
    if (!tracks) return;
    for (uint32_t i = 0; i < count; ++i) {
        delete[] tracks[i].id;
        delete[] tracks[i].label;
        delete[] tracks[i].language;
        delete[] tracks[i].codec;
    }
    delete[] tracks;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_memory(
    const uint8_t* data, size_t size, jalium_pixel_format_t requested_format,
    jalium_image_t* out_image)
{
    if (!data || size == 0 || !out_image) return JALIUM_MEDIA_E_INVALID_ARG;
    if (!IsSupportedFormat(requested_format)) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    std::memset(out_image, 0, sizeof(*out_image));
    if (IsGif(data, size)) {
        return DecodeGifFrame(
            data, size, 0, requested_format, out_image, nullptr);
    }
    if (IsWebP(data, size)) {
        return DecodeWebPFrame(
            data, size, 0, requested_format, out_image, nullptr);
    }
    const bool animated_png = IsAnimatedPng(data, size);
    if (IsPng(data, size) && !animated_png) {
        return DecodeStaticPng(
            data, size, requested_format, out_image);
    }
#if JALIUM_HAS_GSTREAMER
    if (!g_media_available) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    if (animated_png) {
        return DecodeApngFrame(
            data, size, 0, requested_format, out_image, nullptr);
    }
    return DecodeImage(data, size, requested_format, out_image);
#else
    return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
#endif
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_file(
    const char* utf8_path, jalium_pixel_format_t requested_format,
    jalium_image_t* out_image)
{
    if (!utf8_path || !out_image) return JALIUM_MEDIA_E_INVALID_ARG;
    std::vector<uint8_t> bytes;
    const auto status = ReadFile(utf8_path, bytes);
    if (status != JALIUM_MEDIA_OK) return status;
    return jalium_image_decode_memory(bytes.data(), bytes.size(),
                                      requested_format, out_image);
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_read_dimensions(
    const uint8_t* data, size_t size, uint32_t* out_width, uint32_t* out_height)
{
    if (!data || size == 0 || !out_width || !out_height)
        return JALIUM_MEDIA_E_INVALID_ARG;
    jalium_image_t image{};
    const auto status = jalium_image_decode_memory(
        data, size, JALIUM_PF_BGRA8, &image);
    if (status == JALIUM_MEDIA_OK) {
        *out_width = image.width;
        *out_height = image.height;
        jalium_image_free(&image);
    }
    return status;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_read_frame_count(
    const uint8_t* data, size_t size, uint32_t* out_frame_count)
{
    if (!data || size == 0 || !out_frame_count)
        return JALIUM_MEDIA_E_INVALID_ARG;
    *out_frame_count = 0;
    if (IsGif(data, size)) {
        return DecodeGifFrame(
            data, size, 0, JALIUM_PF_RGBA8, nullptr, nullptr,
            out_frame_count);
    }
    if (IsWebP(data, size)) {
        return DecodeWebPFrame(
            data, size, 0, JALIUM_PF_RGBA8, nullptr, nullptr,
            out_frame_count);
    }
#if JALIUM_HAS_GSTREAMER
    if (IsAnimatedPng(data, size)) {
        return DecodeApngFrame(
            data, size, 0, JALIUM_PF_RGBA8, nullptr, nullptr,
            out_frame_count);
    }
#endif
    uint32_t width = 0, height = 0;
    const auto status = jalium_image_read_dimensions(
        data, size, &width, &height);
    if (status == JALIUM_MEDIA_OK) *out_frame_count = 1;
    return status;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_frame(
    const uint8_t* data, size_t size, uint32_t frame_index,
    jalium_pixel_format_t requested_format, jalium_image_t* out_image,
    uint32_t* out_delay_ms)
{
    if (!data || size == 0 || !out_image)
        return JALIUM_MEDIA_E_INVALID_ARG;
    if (!IsSupportedFormat(requested_format))
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    std::memset(out_image, 0, sizeof(*out_image));
    if (out_delay_ms) *out_delay_ms = 0;
    if (IsGif(data, size)) {
        return DecodeGifFrame(
            data, size, frame_index, requested_format, out_image,
            out_delay_ms);
    }
    if (IsWebP(data, size)) {
        return DecodeWebPFrame(
            data, size, frame_index, requested_format, out_image,
            out_delay_ms);
    }
#if JALIUM_HAS_GSTREAMER
    if (IsAnimatedPng(data, size)) {
        return DecodeApngFrame(
            data, size, frame_index, requested_format, out_image,
            out_delay_ms);
    }
#endif
    if (frame_index != 0) return JALIUM_MEDIA_E_INVALID_ARG;
    return jalium_image_decode_memory(data, size, requested_format, out_image);
}

JALIUM_MEDIA_API void jalium_image_free(jalium_image_t* image)
{
    if (!image) return;
    jalium_media_aligned_free(image->pixels);
    std::memset(image, 0, sizeof(*image));
}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_open_file(
    const char* utf8_path, jalium_pixel_format_t requested_format,
    jalium_video_decoder_t** out_decoder)
{
    if (!utf8_path || !out_decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_decoder = nullptr;
    if (!IsSupportedFormat(requested_format)) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    // GStreamer accepts HTTP(S) and other registered URI schemes directly.
    // Only plain local paths require an eager existence check.
    if (!IsUri(utf8_path) && !FileExists(utf8_path)) return JALIUM_MEDIA_E_IO;
#if JALIUM_HAS_GSTREAMER
    if (!g_media_available) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    auto* decoder = new (std::nothrow) jalium_video_decoder();
    if (!decoder) return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    decoder->format = requested_format;
    decoder->source = utf8_path;

    // Prefer VAAPI/VAMemory -> dma-buf only when the post-process element is
    // installed and negotiation produces a real DMABuf-backed sample. Any
    // missing plugin, unsupported modifier or software-only decoder falls
    // straight back to the existing BGRA/RGBA pipeline.
    auto status = OpenDmabufVideoPipeline(decoder);
    if (status != JALIUM_MEDIA_OK) status = OpenCpuVideoPipeline(decoder);
    if (status != JALIUM_MEDIA_OK) {
        delete decoder;
        return status;
    }

    GstVideoInfo sample_info;
    GstCaps* sample_caps = gst_sample_get_caps(decoder->pending_sample);
    if (sample_caps && gst_video_info_from_caps(&sample_info, sample_caps)) {
        decoder->info.width = GST_VIDEO_INFO_WIDTH(&sample_info);
        decoder->info.height = GST_VIDEO_INFO_HEIGHT(&sample_info);
        const int fps_n = GST_VIDEO_INFO_FPS_N(&sample_info);
        const int fps_d = GST_VIDEO_INFO_FPS_D(&sample_info);
        if (fps_n > 0 && fps_d > 0) {
            decoder->info.frame_rate = static_cast<double>(fps_n) / fps_d;
        }
    } else if (sample_caps && gst_caps_get_size(sample_caps) > 0) {
        const GstStructure* structure = gst_caps_get_structure(sample_caps, 0);
        gint width = 0, height = 0;
        if (structure &&
            gst_structure_get_int(structure, "width", &width) &&
            gst_structure_get_int(structure, "height", &height)) {
            decoder->info.width = static_cast<uint32_t>(std::max(width, 0));
            decoder->info.height = static_cast<uint32_t>(std::max(height, 0));
        }
    }
    PopulateDiscoveredVideoInfo(utf8_path, decoder->info);
    *out_decoder = decoder;
    return JALIUM_MEDIA_OK;
#else
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_get_info(
    jalium_video_decoder_t* decoder, jalium_video_info_t* out_info)
{
    if (!decoder || !out_info) return JALIUM_MEDIA_E_INVALID_ARG;
#if JALIUM_HAS_GSTREAMER
    *out_info = decoder->info;
    return JALIUM_MEDIA_OK;
#else
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_read_frame(
    jalium_video_decoder_t* decoder, jalium_video_frame_t* out_frame)
{
    if (!decoder || !out_frame) return JALIUM_MEDIA_E_INVALID_ARG;
#if JALIUM_HAS_GSTREAMER
    std::memset(out_frame, 0, sizeof(*out_frame));
    if (decoder->dmabuf_mode) {
        const auto fallback_status = SwitchVideoDecoderToCpu(decoder);
        if (fallback_status != JALIUM_MEDIA_OK) return fallback_status;
    }
    GstSample* sample = decoder->pending_sample;
    decoder->pending_sample = nullptr;
    if (!sample) {
        sample = gst_app_sink_try_pull_sample(decoder->sink, 15 * GST_SECOND);
    }
    if (!sample) {
        if (gst_app_sink_is_eos(decoder->sink)) return JALIUM_MEDIA_E_END_OF_STREAM;
        return PipelineError(decoder->pipeline, JALIUM_MEDIA_E_DECODE_FAILED);
    }

    uint32_t width = 0, height = 0, stride = 0;
    int64_t pts = 0;
    int32_t keyframe = 0;
    const auto status = CopyVideoSample(
        sample, decoder->format, decoder->pixels, width, height, stride,
        &pts, &keyframe);
    gst_sample_unref(sample);
    if (status != JALIUM_MEDIA_OK) return status;

    out_frame->width = width;
    out_frame->height = height;
    out_frame->stride_bytes = stride;
    out_frame->format = decoder->format;
    out_frame->pixels = decoder->pixels.data();
    out_frame->pts_microseconds = pts;
    out_frame->is_keyframe = keyframe;
    decoder->last_pts_microseconds = pts;
    return JALIUM_MEDIA_OK;
#else
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_seek_microseconds(
    jalium_video_decoder_t* decoder, int64_t pts_microseconds)
{
    if (!decoder) return JALIUM_MEDIA_E_INVALID_ARG;
#if JALIUM_HAS_GSTREAMER
    if (pts_microseconds < 0) pts_microseconds = 0;
    if (decoder->pending_sample) {
        gst_sample_unref(decoder->pending_sample);
        decoder->pending_sample = nullptr;
    }
    while (GstSample* queued = gst_app_sink_try_pull_sample(decoder->sink, 0)) {
        gst_sample_unref(queued);
    }
    const gboolean sought = gst_element_seek_simple(
        decoder->pipeline, GST_FORMAT_TIME,
        static_cast<GstSeekFlags>(GST_SEEK_FLAG_FLUSH | GST_SEEK_FLAG_ACCURATE),
        static_cast<gint64>(pts_microseconds) * GST_USECOND);
    if (sought) decoder->last_pts_microseconds = pts_microseconds;
    return sought ? JALIUM_MEDIA_OK : JALIUM_MEDIA_E_DECODE_FAILED;
#else
    (void)pts_microseconds;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API void jalium_video_decoder_close(jalium_video_decoder_t* decoder)
{
#if JALIUM_HAS_GSTREAMER
    if (!decoder) return;
    CloseVideoPipeline(decoder);
#endif
    delete decoder;
}

JALIUM_MEDIA_API jalium_media_status_t
jalium_video_decoder_acquire_gpu_surface_descriptor(
    jalium_video_decoder_t* decoder,
    jalium_video_decoder_gpu_descriptor_t* out_descriptor)
{
    if (!decoder || !out_descriptor) return JALIUM_MEDIA_E_INVALID_ARG;
    ResetGpuDescriptor(out_descriptor);
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API jalium_media_status_t
jalium_video_decoder_read_gpu_frame_descriptor(
    jalium_video_decoder_t* decoder,
    jalium_video_decoder_gpu_descriptor_t* out_descriptor,
    int64_t* out_pts_microseconds,
    int32_t* out_is_keyframe)
{
    if (!decoder || !out_descriptor || !out_pts_microseconds ||
        !out_is_keyframe) {
        return JALIUM_MEDIA_E_INVALID_ARG;
    }
    ResetGpuDescriptor(out_descriptor);
    *out_pts_microseconds = 0;
    *out_is_keyframe = 0;
#if JALIUM_HAS_GSTREAMER
    if (!decoder->dmabuf_mode) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    GstSample* sample = decoder->pending_sample;
    decoder->pending_sample = nullptr;
    if (!sample) {
        sample = gst_app_sink_try_pull_sample(decoder->sink, 15 * GST_SECOND);
    }
    if (!sample) {
        if (gst_app_sink_is_eos(decoder->sink)) return JALIUM_MEDIA_E_END_OF_STREAM;
        return PipelineError(decoder->pipeline, JALIUM_MEDIA_E_DECODE_FAILED);
    }
    const auto status = ExportDmabufSample(
        sample, decoder->format, out_descriptor,
        out_pts_microseconds, out_is_keyframe);
    gst_sample_unref(sample);
    if (status == JALIUM_MEDIA_OK) {
        decoder->last_pts_microseconds = *out_pts_microseconds;
    }
    return status;
#else
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API void
jalium_video_decoder_release_gpu_surface_descriptor(
    jalium_video_decoder_gpu_descriptor_t* descriptor)
{
    if (!descriptor) return;
    CloseGpuDescriptorFds(descriptor);
    ResetGpuDescriptor(descriptor);
}

JALIUM_MEDIA_API jalium_media_status_t
jalium_video_decoder_disable_gpu_output(jalium_video_decoder_t* decoder)
{
    if (!decoder) return JALIUM_MEDIA_E_INVALID_ARG;
#if JALIUM_HAS_GSTREAMER
    if (!decoder->dmabuf_mode) return JALIUM_MEDIA_OK;
    return SwitchVideoDecoderToCpu(decoder);
#else
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API jalium_media_status_t jalium_linux_audio_decoder_open_track(
    const char* utf8_path_or_uri,
    uint32_t track_index,
    jalium_audio_decoder_t** out_decoder)
{
    if (!utf8_path_or_uri || !out_decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_decoder = nullptr;
#if JALIUM_HAS_GSTREAMER
    jalium_media_status_t status = JALIUM_MEDIA_OK;
    auto* decoder = OpenGstAudioFile(
        utf8_path_or_uri, track_index, JALIUM_ACODEC_AUTO, status);
    if (!decoder) return status;
    *out_decoder = reinterpret_cast<jalium_audio_decoder_t*>(decoder);
    return JALIUM_MEDIA_OK;
#else
    (void)track_index;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API jalium_media_status_t jalium_subtitle_decoder_open(
    const char* utf8_path_or_uri,
    uint32_t track_index,
    jalium_subtitle_decoder_t** out_decoder)
{
    if (!utf8_path_or_uri || !out_decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_decoder = nullptr;
#if JALIUM_HAS_GSTREAMER
    jalium_media_status_t status = JALIUM_MEDIA_OK;
    auto* decoder = OpenGstSubtitle(utf8_path_or_uri, track_index, status);
    if (!decoder) return status;
    *out_decoder = decoder;
    return JALIUM_MEDIA_OK;
#else
    (void)track_index;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API jalium_media_status_t jalium_subtitle_decoder_read_cue(
    jalium_subtitle_decoder_t* decoder,
    jalium_subtitle_cue_t* out_cue)
{
    if (!decoder || !out_cue) return JALIUM_MEDIA_E_INVALID_ARG;
    std::memset(out_cue, 0, sizeof(*out_cue));
#if JALIUM_HAS_GSTREAMER
    GstSample* sample = decoder->pending_sample;
    decoder->pending_sample = nullptr;
    if (!sample) {
        sample = gst_app_sink_try_pull_sample(decoder->sink, 15 * GST_SECOND);
    }
    if (!sample) {
        if (gst_app_sink_is_eos(decoder->sink)) return JALIUM_MEDIA_E_END_OF_STREAM;
        return PipelineError(decoder->pipeline, JALIUM_MEDIA_E_DECODE_FAILED);
    }
    int64_t start = 0;
    int64_t duration = 0;
    const auto status = CopySubtitleSample(
        sample, decoder->text, start, duration);
    gst_sample_unref(sample);
    if (status != JALIUM_MEDIA_OK) return status;
    out_cue->utf8_text = decoder->text.c_str();
    out_cue->start_microseconds = start;
    out_cue->duration_microseconds = duration;
    return JALIUM_MEDIA_OK;
#else
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API jalium_media_status_t jalium_subtitle_decoder_seek_us(
    jalium_subtitle_decoder_t* decoder,
    int64_t position_us)
{
    if (!decoder) return JALIUM_MEDIA_E_INVALID_ARG;
#if JALIUM_HAS_GSTREAMER
    if (position_us < 0) position_us = 0;
    if (decoder->pending_sample) {
        gst_sample_unref(decoder->pending_sample);
        decoder->pending_sample = nullptr;
    }
    while (GstSample* sample = gst_app_sink_try_pull_sample(decoder->sink, 0)) {
        gst_sample_unref(sample);
    }
    decoder->text.clear();

    return SeekPipelineAccurately(decoder->pipeline, position_us);
#else
    (void)position_us;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API void jalium_subtitle_decoder_close(
    jalium_subtitle_decoder_t* decoder)
{
    if (!decoder) return;
#if JALIUM_HAS_GSTREAMER
    if (decoder->pending_sample) gst_sample_unref(decoder->pending_sample);
    if (decoder->pipeline) {
        gst_element_set_state(decoder->pipeline, GST_STATE_NULL);
        gst_object_unref(decoder->pipeline);
    }
#endif
    delete decoder;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_enumerate(
    jalium_camera_device_t** out_devices, uint32_t* out_count)
{
    if (!out_devices || !out_count) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_devices = nullptr;
    *out_count = 0;
#if JALIUM_HAS_GSTREAMER
    if (!g_media_available) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    GstDeviceMonitor* monitor = nullptr;
    GList* devices = GetVideoDevices(&monitor);
    const uint32_t count = static_cast<uint32_t>(g_list_length(devices));
    if (count == 0) {
        ReleaseVideoDevices(monitor, devices);
        return JALIUM_MEDIA_OK;
    }

    auto* result = new (std::nothrow) jalium_camera_device_t[count]{};
    if (!result) {
        ReleaseVideoDevices(monitor, devices);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }

    uint32_t index = 0;
    for (GList* node = devices; node && index < count; node = node->next, ++index) {
        auto* device = reinterpret_cast<GstDevice*>(node->data);
        const std::string id = GstDeviceId(device);
        const char* display_name = gst_device_get_display_name(device);
        const auto formats = GstDeviceFormats(device);

        result[index].id = DuplicateString(id);
        result[index].friendly_name = DuplicateString(
            display_name ? display_name : id);
        result[index].facing = JALIUM_CAMERA_FACING_EXTERNAL;
        auto* native_formats = new (std::nothrow)
            jalium_camera_format_t[formats.size()];
        if (!result[index].id || !result[index].friendly_name ||
            !native_formats) {
            ReleaseVideoDevices(monitor, devices);
            jalium_camera_devices_free(result, count);
            return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        }
        std::copy(formats.begin(), formats.end(), native_formats);
        result[index].format_count = static_cast<uint32_t>(formats.size());
        result[index].formats = native_formats;
    }
    ReleaseVideoDevices(monitor, devices);
    *out_devices = result;
    *out_count = count;
#endif
    return JALIUM_MEDIA_OK;
}

JALIUM_MEDIA_API void jalium_camera_devices_free(
    jalium_camera_device_t* devices, uint32_t count)
{
    if (!devices) return;
#if JALIUM_HAS_GSTREAMER
    for (uint32_t i = 0; i < count; ++i) {
        delete[] devices[i].id;
        delete[] devices[i].friendly_name;
        delete[] devices[i].formats;
    }
#else
    (void)count;
#endif
    delete[] devices;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_open(
    const char* device_id, uint32_t requested_width,
    uint32_t requested_height, double requested_fps,
    jalium_pixel_format_t requested_format, jalium_camera_source_t** out_source)
{
    if (!device_id || !out_source) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_source = nullptr;
    if (!IsSupportedFormat(requested_format)) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
#if JALIUM_HAS_GSTREAMER
    if (!g_media_available) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    GstDevice* device = FindVideoDevice(device_id);
    if (!device) return JALIUM_MEDIA_E_NO_DEVICE;
    GstElement* capture = gst_device_create_element(device, nullptr);
    gst_object_unref(device);
    if (!capture) return JALIUM_MEDIA_E_NO_DEVICE;

    GstElement* pipeline = gst_pipeline_new(nullptr);
    GstElement* queue = gst_element_factory_make("queue", nullptr);
    GstElement* converter = gst_element_factory_make("videoconvert", nullptr);
    GstElement* filter = gst_element_factory_make("capsfilter", nullptr);
    GstElement* sink_element = gst_element_factory_make("appsink", nullptr);
    if (!pipeline || !queue || !converter || !filter || !sink_element) {
        if (pipeline) gst_object_unref(pipeline);
        else gst_object_unref(capture);
        return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    }

    GstCaps* caps = gst_caps_new_simple(
        "video/x-raw", "format", G_TYPE_STRING,
        GstPixelFormat(requested_format), nullptr);
    GstStructure* structure = gst_caps_get_structure(caps, 0);
    if (requested_width > 0) {
        gst_structure_set(structure, "width", G_TYPE_INT,
                          static_cast<int>(requested_width), nullptr);
    }
    if (requested_height > 0) {
        gst_structure_set(structure, "height", G_TYPE_INT,
                          static_cast<int>(requested_height), nullptr);
    }
    if (requested_fps > 0.0) {
        gint fps_n = 0, fps_d = 1;
        gst_util_double_to_fraction(requested_fps, &fps_n, &fps_d);
        gst_structure_set(structure, "framerate", GST_TYPE_FRACTION,
                          fps_n, fps_d, nullptr);
    }
    g_object_set(filter, "caps", caps, nullptr);
    gst_caps_unref(caps);
    g_object_set(sink_element, "sync", FALSE, "max-buffers", 2u,
                 "drop", TRUE, nullptr);

    gst_bin_add_many(reinterpret_cast<GstBin*>(pipeline), capture, queue, converter, filter,
                     sink_element, nullptr);
    if (!gst_element_link_many(capture, queue, converter, filter, sink_element,
                               nullptr)) {
        gst_object_unref(pipeline);
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }
    if (gst_element_set_state(pipeline, GST_STATE_PLAYING) ==
        GST_STATE_CHANGE_FAILURE) {
        const auto status = PipelineError(pipeline, JALIUM_MEDIA_E_PLATFORM);
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return status;
    }

    // Opening a V4L2/PipeWire device is asynchronous. Do not report
    // CameraOpened until the source has produced a real frame; this also turns
    // EACCES and portal denials into a deterministic open failure.
    GstSample* first = gst_app_sink_try_pull_sample(
        reinterpret_cast<GstAppSink*>(sink_element), 10 * GST_SECOND);
    if (!first) {
        const auto status = PipelineError(pipeline, JALIUM_MEDIA_E_NO_DEVICE);
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return status;
    }

    auto* source = new (std::nothrow) jalium_camera_source();
    if (!source) {
        gst_sample_unref(first);
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    source->pipeline = pipeline;
    source->sink = reinterpret_cast<GstAppSink*>(sink_element);
    source->pending_sample = first;
    source->format = requested_format;
    *out_source = source;
    return JALIUM_MEDIA_OK;
#else
    (void)requested_width;
    (void)requested_height;
    (void)requested_fps;
    return JALIUM_MEDIA_E_NO_DEVICE;
#endif
}

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_read_frame(
    jalium_camera_source_t* source, jalium_video_frame_t* out_frame)
{
    if (!source || !out_frame) return JALIUM_MEDIA_E_INVALID_ARG;
#if JALIUM_HAS_GSTREAMER
    std::memset(out_frame, 0, sizeof(*out_frame));
    GstSample* sample = source->pending_sample;
    source->pending_sample = nullptr;
    if (!sample) {
        sample = gst_app_sink_try_pull_sample(source->sink, 500 * GST_MSECOND);
    }
    if (!sample) {
        if (gst_app_sink_is_eos(source->sink)) return JALIUM_MEDIA_E_END_OF_STREAM;
        return PipelineError(source->pipeline, JALIUM_MEDIA_E_PLATFORM);
    }
    uint32_t width = 0, height = 0, stride = 0;
    int64_t pts = 0;
    int32_t keyframe = 0;
    const auto status = CopyVideoSample(
        sample, source->format, source->pixels, width, height, stride,
        &pts, &keyframe);
    gst_sample_unref(sample);
    if (status != JALIUM_MEDIA_OK) return status;
    out_frame->width = width;
    out_frame->height = height;
    out_frame->stride_bytes = stride;
    out_frame->format = source->format;
    out_frame->pixels = source->pixels.data();
    out_frame->pts_microseconds = pts;
    out_frame->is_keyframe = keyframe;
    return JALIUM_MEDIA_OK;
#else
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API void jalium_camera_close(jalium_camera_source_t* source)
{
#if JALIUM_HAS_GSTREAMER
    if (!source) return;
    if (source->pending_sample) gst_sample_unref(source->pending_sample);
    if (source->pipeline) {
        gst_element_set_state(source->pipeline, GST_STATE_NULL);
        gst_object_unref(source->pipeline);
    }
#endif
    delete source;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_microphone_enumerate(
    jalium_microphone_device_t** out_devices,
    uint32_t* out_count)
{
    if (!out_devices || !out_count) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_devices = nullptr;
    *out_count = 0;
#if JALIUM_HAS_GSTREAMER
    if (!g_media_available) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    GstDeviceMonitor* monitor = nullptr;
    GList* devices = GetDevices("Audio/Source", &monitor);
    const uint32_t count = static_cast<uint32_t>(g_list_length(devices));
    if (count == 0) {
        ReleaseVideoDevices(monitor, devices);
        return JALIUM_MEDIA_OK;
    }

    auto* result = new (std::nothrow) jalium_microphone_device_t[count]{};
    if (!result) {
        ReleaseVideoDevices(monitor, devices);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    uint32_t index = 0;
    for (GList* node = devices; node && index < count; node = node->next, ++index) {
        auto* device = reinterpret_cast<GstDevice*>(node->data);
        const std::string id = GstDeviceId(device);
        const char* display_name = gst_device_get_display_name(device);
        result[index].id = DuplicateString(id);
        result[index].friendly_name = DuplicateString(
            display_name ? display_name : id);
        if (!result[index].id || !result[index].friendly_name) {
            ReleaseVideoDevices(monitor, devices);
            jalium_microphone_devices_free(result, count);
            return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        }
    }
    ReleaseVideoDevices(monitor, devices);
    *out_devices = result;
    *out_count = count;
    return JALIUM_MEDIA_OK;
#else
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API void jalium_microphone_devices_free(
    jalium_microphone_device_t* devices,
    uint32_t count)
{
    if (!devices) return;
    for (uint32_t i = 0; i < count; ++i) {
        delete[] devices[i].id;
        delete[] devices[i].friendly_name;
    }
    delete[] devices;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_microphone_open(
    const char* device_id,
    uint32_t requested_sample_rate,
    uint32_t requested_channels,
    jalium_microphone_source_t** out_source)
{
    if (!device_id || !out_source) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_source = nullptr;
    if (requested_channels > 8) return JALIUM_MEDIA_E_INVALID_ARG;
#if JALIUM_HAS_GSTREAMER
    if (!g_media_available) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    GstDevice* device = FindDevice("Audio/Source", device_id);
    if (!device) return JALIUM_MEDIA_E_NO_DEVICE;
    GstElement* capture = gst_device_create_element(device, nullptr);
    gst_object_unref(device);
    if (!capture) return JALIUM_MEDIA_E_NO_DEVICE;

    GstElement* pipeline = gst_pipeline_new(nullptr);
    GstElement* queue = gst_element_factory_make("queue", nullptr);
    GstElement* converter = gst_element_factory_make("audioconvert", nullptr);
    GstElement* resampler = gst_element_factory_make("audioresample", nullptr);
    GstElement* filter = gst_element_factory_make("capsfilter", nullptr);
    GstElement* sink_element = gst_element_factory_make("appsink", nullptr);
    if (!pipeline || !queue || !converter || !resampler || !filter ||
        !sink_element) {
        if (pipeline) gst_object_unref(pipeline);
        else gst_object_unref(capture);
        return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    }

    GstCaps* caps = gst_caps_new_simple(
        "audio/x-raw",
        "format", G_TYPE_STRING, "F32LE",
        "layout", G_TYPE_STRING, "interleaved",
        nullptr);
    GstStructure* structure = gst_caps_get_structure(caps, 0);
    if (requested_sample_rate > 0) {
        gst_structure_set(structure, "rate", G_TYPE_INT,
                          static_cast<int>(requested_sample_rate), nullptr);
    }
    if (requested_channels > 0) {
        gst_structure_set(structure, "channels", G_TYPE_INT,
                          static_cast<int>(requested_channels), nullptr);
    }
    g_object_set(filter, "caps", caps, nullptr);
    gst_caps_unref(caps);
    g_object_set(sink_element, "sync", FALSE, "max-buffers", 8u,
                 "drop", TRUE, nullptr);

    gst_bin_add_many(reinterpret_cast<GstBin*>(pipeline), capture, queue, converter, resampler,
                     filter, sink_element, nullptr);
    if (!gst_element_link_many(capture, queue, converter, resampler, filter,
                               sink_element, nullptr)) {
        gst_object_unref(pipeline);
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }
    if (gst_element_set_state(pipeline, GST_STATE_PLAYING) ==
        GST_STATE_CHANGE_FAILURE) {
        const auto status = PipelineError(pipeline, JALIUM_MEDIA_E_PLATFORM);
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return status;
    }

    GstSample* first = gst_app_sink_try_pull_sample(
        reinterpret_cast<GstAppSink*>(sink_element), 10 * GST_SECOND);
    if (!first) {
        const auto status = PipelineError(pipeline, JALIUM_MEDIA_E_NO_DEVICE);
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return status;
    }
    GstAudioInfo info;
    GstCaps* first_caps = gst_sample_get_caps(first);
    if (!first_caps || !gst_audio_info_from_caps(&info, first_caps) ||
        GST_AUDIO_INFO_RATE(&info) == 0 || GST_AUDIO_INFO_CHANNELS(&info) == 0) {
        gst_sample_unref(first);
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    auto* source = new (std::nothrow) jalium_microphone_source();
    if (!source) {
        gst_sample_unref(first);
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    source->pipeline = pipeline;
    source->sink = reinterpret_cast<GstAppSink*>(sink_element);
    source->pending_sample = first;
    source->sample_rate = GST_AUDIO_INFO_RATE(&info);
    source->channels = GST_AUDIO_INFO_CHANNELS(&info);
    *out_source = source;
    return JALIUM_MEDIA_OK;
#else
    (void)requested_sample_rate;
    (void)requested_channels;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API jalium_media_status_t jalium_microphone_read_frame(
    jalium_microphone_source_t* source,
    jalium_audio_capture_frame_t* out_frame)
{
    if (!source || !out_frame) return JALIUM_MEDIA_E_INVALID_ARG;
    std::memset(out_frame, 0, sizeof(*out_frame));
#if JALIUM_HAS_GSTREAMER
    GstSample* sample = source->pending_sample;
    source->pending_sample = nullptr;
    if (!sample) {
        sample = gst_app_sink_try_pull_sample(source->sink, 15 * GST_SECOND);
    }
    if (!sample) {
        if (gst_app_sink_is_eos(source->sink)) return JALIUM_MEDIA_E_END_OF_STREAM;
        return PipelineError(source->pipeline, JALIUM_MEDIA_E_PLATFORM);
    }
    uint32_t sample_rate = 0;
    uint32_t channels = 0;
    int64_t pts = 0;
    const auto status = CopyAudioSample(
        sample, source->samples, sample_rate, channels, pts);
    gst_sample_unref(sample);
    if (status != JALIUM_MEDIA_OK) return status;
    source->sample_rate = sample_rate;
    source->channels = channels;
    out_frame->samples = source->samples.data();
    out_frame->frame_count = static_cast<uint32_t>(
        source->samples.size() / channels);
    out_frame->sample_rate = sample_rate;
    out_frame->channels = channels;
    out_frame->pts_microseconds = pts;
    return JALIUM_MEDIA_OK;
#else
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#endif
}

JALIUM_MEDIA_API void jalium_microphone_close(
    jalium_microphone_source_t* source)
{
    if (!source) return;
#if JALIUM_HAS_GSTREAMER
    if (source->pending_sample) gst_sample_unref(source->pending_sample);
    if (source->pipeline) {
        gst_element_set_state(source->pipeline, GST_STATE_NULL);
        gst_object_unref(source->pipeline);
    }
#endif
    delete source;
}

} // extern "C"
