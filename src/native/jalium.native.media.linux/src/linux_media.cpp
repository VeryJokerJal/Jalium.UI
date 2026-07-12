#include "jalium_media.h"
#include "jalium_media_internal.h"
#include "jalium_audio.h"
#include "audio_internal.h"

#include <algorithm>
#include <atomic>
#include <cerrno>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <new>
#include <string>
#include <vector>

#ifndef JALIUM_HAS_GSTREAMER
#define JALIUM_HAS_GSTREAMER 0
#endif

#if JALIUM_HAS_GSTREAMER
#include <gst/app/gstappsink.h>
#include <gst/app/gstappsrc.h>
#include <gst/audio/audio.h>
#include <gst/gst.h>
#include <gst/pbutils/pbutils.h>
#include <gst/video/video.h>
#endif

#if JALIUM_HAS_GSTREAMER
struct DynamicPadContext {
    GstElement* target = nullptr;
    const char* media_prefix = nullptr;
};

struct jalium_video_decoder {
    GstElement* pipeline = nullptr;
    GstAppSink* sink = nullptr;
    GstSample* pending_sample = nullptr;
    DynamicPadContext link_context{};
    jalium_pixel_format_t format = JALIUM_PF_BGRA8;
    jalium_video_info_t info{};
    std::vector<uint8_t> pixels;
};

struct jalium_camera_source {
    GstElement* pipeline = nullptr;
    GstAppSink* sink = nullptr;
    jalium_pixel_format_t format = JALIUM_PF_BGRA8;
    std::vector<uint8_t> pixels;
};
#else
struct jalium_video_decoder {};
struct jalium_camera_source {};
#endif

namespace {

std::mutex g_init_mutex;
uint32_t g_init_count = 0;
bool g_media_available = false;

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

#if JALIUM_HAS_GSTREAMER

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
        GstPad* sink_pad = gst_element_get_static_pad(context->target, "sink");
        if (sink_pad && !gst_pad_is_linked(sink_pad)) {
            (void)gst_pad_link(pad, sink_pad);
        }
        if (sink_pad) gst_object_unref(sink_pad);
    }
    if (caps) gst_caps_unref(caps);
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

    jalium_media_status_t result = fallback;
    if (GST_MESSAGE_TYPE(message) == GST_MESSAGE_EOS) {
        result = JALIUM_MEDIA_E_END_OF_STREAM;
    } else {
        GError* error = nullptr;
        gchar* debug = nullptr;
        gst_message_parse_error(message, &error, &debug);
        const std::string text = error && error->message ? error->message : "";
        if (text.find("Permission denied") != std::string::npos) {
            result = JALIUM_MEDIA_E_PERMISSION_DENIED;
        } else if (text.find("not found") != std::string::npos ||
                   text.find("No such file") != std::string::npos) {
            result = JALIUM_MEDIA_E_IO;
        } else if (error && error->domain == GST_STREAM_ERROR &&
                   (error->code == GST_STREAM_ERROR_CODEC_NOT_FOUND ||
                    error->code == GST_STREAM_ERROR_FORMAT)) {
            result = JALIUM_MEDIA_E_UNSUPPORTED_CODEC;
        }
        if (error) g_error_free(error);
        g_free(debug);
    }
    gst_message_unref(message);
    return result;
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

    gst_bin_add_many(GST_BIN(pipeline), source, decoder, queue, converter,
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
        GST_APP_SRC(source), input);
    gst_app_src_end_of_stream(GST_APP_SRC(source));
    if (push_result != GST_FLOW_OK) {
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    GstSample* sample = gst_app_sink_try_pull_sample(
        GST_APP_SINK(sink_element), 10 * GST_SECOND);
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
    gchar* uri = gst_filename_to_uri(path, &error);
    if (!uri) {
        if (error) g_error_free(error);
        return;
    }
    GstDiscoverer* discoverer = gst_discoverer_new(10 * GST_SECOND, &error);
    if (!discoverer) {
        if (error) g_error_free(error);
        g_free(uri);
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
            auto* video = GST_DISCOVERER_VIDEO_INFO(videos->data);
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
                GST_DISCOVERER_STREAM_INFO(video));
            out_info.active_codec = CodecFromCaps(encoded_caps);
            if (encoded_caps) gst_caps_unref(encoded_caps);
            gst_discoverer_stream_info_list_free(videos);
        }
        gst_discoverer_info_unref(discovered);
    }
    if (error) g_error_free(error);
    gst_object_unref(discoverer);
    g_free(uri);

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
    DynamicPadContext& link_context,
    GstElement** out_pipeline,
    GstAppSink** out_sink) noexcept
{
    *out_pipeline = nullptr;
    *out_sink = nullptr;
    GError* error = nullptr;
    gchar* uri = gst_filename_to_uri(path, &error);
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
        g_free(uri);
        return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    }

    g_object_set(decoder, "uri", uri, nullptr);
    g_object_set(filter, "caps", output_caps, nullptr);
    g_object_set(sink_element, "sync", FALSE, "max-buffers", 4u,
                 "drop", FALSE, nullptr);
    g_free(uri);

    if (resampler) {
        gst_bin_add_many(GST_BIN(pipeline), decoder, queue, converter,
                         resampler, filter, sink_element, nullptr);
        if (!gst_element_link_many(queue, converter, resampler, filter,
                                   sink_element, nullptr)) {
            gst_object_unref(pipeline);
            return JALIUM_MEDIA_E_PLATFORM;
        }
    } else {
        gst_bin_add_many(GST_BIN(pipeline), decoder, queue, converter,
                         filter, sink_element, nullptr);
        if (!gst_element_link_many(queue, converter, filter, sink_element,
                                   nullptr)) {
            gst_object_unref(pipeline);
            return JALIUM_MEDIA_E_PLATFORM;
        }
    }

    link_context.target = queue;
    link_context.media_prefix = media_prefix;
    g_signal_connect(decoder, "pad-added", G_CALLBACK(LinkDecodedPad),
                     &link_context);
    if (gst_element_set_state(pipeline, GST_STATE_PLAYING) ==
        GST_STATE_CHANGE_FAILURE) {
        const auto status = PipelineError(pipeline, JALIUM_MEDIA_E_DECODE_FAILED);
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return status;
    }
    *out_pipeline = pipeline;
    *out_sink = GST_APP_SINK(sink_element);
    return JALIUM_MEDIA_OK;
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

GList* GetVideoDevices(GstDeviceMonitor** out_monitor) noexcept
{
    *out_monitor = gst_device_monitor_new();
    if (!*out_monitor) return nullptr;
    (void)gst_device_monitor_add_filter(*out_monitor, "Video/Source", nullptr);
    if (!gst_device_monitor_start(*out_monitor)) {
        gst_object_unref(*out_monitor);
        *out_monitor = nullptr;
        return nullptr;
    }
    return gst_device_monitor_get_devices(*out_monitor);
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
        auto* device = GST_DEVICE(node->data);
        if (GstDeviceId(device) == requested_id) {
            match = GST_DEVICE(gst_object_ref(device));
            break;
        }
    }
    ReleaseVideoDevices(monitor, devices);
    return match;
}

class GstAacDecoder final : public jalium::audio::audio_decoder_impl {
public:
    GstElement* pipeline = nullptr;
    GstAppSink* sink = nullptr;
    DynamicPadContext link_context{};
    std::vector<float> pending;
    size_t pending_offset = 0;
    bool eos = false;

    ~GstAacDecoder() override
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
            jalium::audio::StatsDecoderFrames(JALIUM_ACODEC_AAC, written);
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
        return gst_element_seek_simple(
            pipeline, GST_FORMAT_TIME,
            static_cast<GstSeekFlags>(GST_SEEK_FLAG_FLUSH | GST_SEEK_FLAG_KEY_UNIT),
            static_cast<gint64>(position_us) * GST_USECOND)
            ? JALIUM_MEDIA_OK : JALIUM_MEDIA_E_DECODE_FAILED;
    }
};

jalium::audio::audio_decoder_impl* OpenGstAacFile(
    const char* path, jalium_media_status_t& out_status) noexcept
{
    out_status = JALIUM_MEDIA_OK;
    if (!path || !*path) {
        out_status = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    if (!FileExists(path)) {
        out_status = JALIUM_MEDIA_E_IO;
        return nullptr;
    }
    if (!g_media_available) {
        out_status = JALIUM_MEDIA_E_NOT_IMPLEMENTED;
        return nullptr;
    }

    auto* decoder = new (std::nothrow) GstAacDecoder();
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
        path, "audio/x-raw", converter, resampler, caps,
        decoder->link_context, &decoder->pipeline, &decoder->sink);
    gst_caps_unref(caps);
    if (out_status != JALIUM_MEDIA_OK) {
        delete decoder;
        return nullptr;
    }

    GstSample* first = gst_app_sink_try_pull_sample(decoder->sink, 15 * GST_SECOND);
    if (!first) {
        out_status = PipelineError(
            decoder->pipeline, JALIUM_MEDIA_E_UNSUPPORTED_CODEC);
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
    decoder->codec = JALIUM_ACODEC_AAC;
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
    jalium::audio::StatsDecoderOpened(JALIUM_ACODEC_AAC);
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
    GError* error = nullptr;
    g_media_available = gst_init_check(nullptr, nullptr, &error) != FALSE;
    if (error) g_error_free(error);
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

JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_memory(
    const uint8_t* data, size_t size, jalium_pixel_format_t requested_format,
    jalium_image_t* out_image)
{
    if (!data || size == 0 || !out_image) return JALIUM_MEDIA_E_INVALID_ARG;
    if (!IsSupportedFormat(requested_format)) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    std::memset(out_image, 0, sizeof(*out_image));
#if JALIUM_HAS_GSTREAMER
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
    if (frame_index != 0) return JALIUM_MEDIA_E_INVALID_ARG;
    if (out_delay_ms) *out_delay_ms = 0;
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
    if (!FileExists(utf8_path)) return JALIUM_MEDIA_E_IO;
#if JALIUM_HAS_GSTREAMER
    if (!g_media_available) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    auto* decoder = new (std::nothrow) jalium_video_decoder();
    if (!decoder) return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    decoder->format = requested_format;

    GstElement* converter = gst_element_factory_make("videoconvert", nullptr);
    GstCaps* caps = gst_caps_new_simple(
        "video/x-raw", "format", G_TYPE_STRING,
        GstPixelFormat(requested_format), nullptr);
    const auto status = BuildUriDecodePipeline(
        utf8_path, "video/x-raw", converter, nullptr, caps,
        decoder->link_context, &decoder->pipeline, &decoder->sink);
    gst_caps_unref(caps);
    if (status != JALIUM_MEDIA_OK) {
        delete decoder;
        return status;
    }

    decoder->pending_sample = gst_app_sink_try_pull_sample(
        decoder->sink, 15 * GST_SECOND);
    if (!decoder->pending_sample) {
        const auto pull_status = PipelineError(
            decoder->pipeline, JALIUM_MEDIA_E_UNSUPPORTED_CODEC);
        gst_element_set_state(decoder->pipeline, GST_STATE_NULL);
        gst_object_unref(decoder->pipeline);
        delete decoder;
        return pull_status;
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
        static_cast<GstSeekFlags>(GST_SEEK_FLAG_FLUSH | GST_SEEK_FLAG_KEY_UNIT),
        static_cast<gint64>(pts_microseconds) * GST_USECOND);
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
    if (decoder->pending_sample) gst_sample_unref(decoder->pending_sample);
    if (decoder->pipeline) {
        gst_element_set_state(decoder->pipeline, GST_STATE_NULL);
        gst_object_unref(decoder->pipeline);
    }
#endif
    delete decoder;
}

JALIUM_MEDIA_API jalium_media_status_t
jalium_video_decoder_acquire_gpu_surface_descriptor(
    jalium_video_decoder_t* decoder,
    jalium_video_decoder_gpu_descriptor_t* out_descriptor)
{
    if (!decoder || !out_descriptor) return JALIUM_MEDIA_E_INVALID_ARG;
    std::memset(out_descriptor, 0, sizeof(*out_descriptor));
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_enumerate(
    jalium_camera_device_t** out_devices, uint32_t* out_count)
{
    if (!out_devices || !out_count) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_devices = nullptr;
    *out_count = 0;
#if JALIUM_HAS_GSTREAMER
    if (!g_media_available) return JALIUM_MEDIA_OK;
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
        auto* device = GST_DEVICE(node->data);
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

    gst_bin_add_many(GST_BIN(pipeline), capture, queue, converter, filter,
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

    auto* source = new (std::nothrow) jalium_camera_source();
    if (!source) {
        gst_element_set_state(pipeline, GST_STATE_NULL);
        gst_object_unref(pipeline);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    source->pipeline = pipeline;
    source->sink = GST_APP_SINK(sink_element);
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
    GstSample* sample = gst_app_sink_try_pull_sample(source->sink, 15 * GST_SECOND);
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
    if (source->pipeline) {
        gst_element_set_state(source->pipeline, GST_STATE_NULL);
        gst_object_unref(source->pipeline);
    }
#endif
    delete source;
}

} // extern "C"
