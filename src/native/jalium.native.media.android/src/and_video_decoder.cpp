#include "and_video_decoder.h"
#include "and_media_init.h"
#include "and_yuv_simd.h"
#include "and_gpu_video.h"
#include "jalium_media_internal.h"

#include <media/NdkMediaCodec.h>
#include <media/NdkMediaExtractor.h>
#include <media/NdkMediaFormat.h>
#include <media/NdkImage.h>

#include <android/log.h>
#include <fcntl.h>
#include <sys/stat.h>
#include <unistd.h>

#include <chrono>
#include <thread>
#include <cstring>
#include <string>

#define ANDLOG_TAG "jalium.native.media.video"
#define ANDLOGI(...) __android_log_print(ANDROID_LOG_INFO, ANDLOG_TAG, __VA_ARGS__)
#define ANDLOGW(...) __android_log_print(ANDROID_LOG_WARN, ANDLOG_TAG, __VA_ARGS__)
#define ANDLOGE(...) __android_log_print(ANDROID_LOG_ERROR, ANDLOG_TAG, __VA_ARGS__)

// Common Android MediaCodec OMX color formats. Listed in <media/NdkMediaFormat.h>
// only as integer constants — duplicated here for clarity.
static constexpr int32_t COLOR_FormatYUV420Planar              = 19;
static constexpr int32_t COLOR_FormatYUV420SemiPlanar          = 21;  // NV12
static constexpr int32_t COLOR_FormatYUV420Flexible            = 0x7F420888;
static constexpr int32_t COLOR_QCOM_FormatYUV420SemiPlanar     = 0x7FA30C00;

// Forward decl: jalium_video_decoder_t is opaque to the public ABI; we
// give it a real definition in this TU so the decoder can carry state.
struct jalium_video_decoder {
    AMediaExtractor*       extractor       = nullptr;
    AMediaCodec*           codec           = nullptr;
    int                    track_index     = -1;
    int                    fd              = -1;

    uint32_t               width           = 0;
    uint32_t               height          = 0;
    uint32_t               stride_bytes    = 0;
    uint32_t               source_stride   = 0;
    uint32_t               source_slice_height = 0;
    double                 duration_s      = 0.0;
    double                 fps             = 0.0;
    uint64_t               frame_count     = 0;
    jalium_video_codec_t   active_codec    = JALIUM_CODEC_NONE;
    jalium_pixel_format_t  format          = JALIUM_PF_BGRA8;
    int32_t                color_format    = COLOR_FormatYUV420Flexible;
    int32_t                color_standard  = 0;   // KEY_COLOR_STANDARD: 1=BT.709, 2=BT.601 NTSC, 4=BT.601 PAL, 6=BT.2020
    bool                   input_eos       = false;
    bool                   output_eos      = false;

    uint8_t*               frame_buffer       = nullptr;
    size_t                 frame_buffer_size  = 0;
    int64_t                last_pts_us        = 0;
    int64_t                seek_target_us      = -1;
    int32_t                last_keyframe       = 0;
    int32_t                color_range         = 0;
    int32_t                color_transfer      = 0;
    std::string            mime;
    bool                   codec_ready         = true;
    bool                   gpu_attempted       = false;
    bool                   gpu_output          = false;
    std::shared_ptr<jalium::media::android::GpuImageReader> gpu_reader;
    jalium::media::android::GpuImageFrame* gpu_frame = nullptr;
};

namespace jalium::media::android {

namespace {

jalium_video_codec_t MapMimeToCodec(const char* mime)
{
    if (!mime) return JALIUM_CODEC_NONE;
    if (std::strcmp(mime, "video/avc") == 0)         return JALIUM_CODEC_H264;
    if (std::strcmp(mime, "video/hevc") == 0)        return JALIUM_CODEC_HEVC;
    if (std::strcmp(mime, "video/x-vnd.on2.vp9") == 0) return JALIUM_CODEC_VP9;
    if (std::strcmp(mime, "video/av01") == 0)        return JALIUM_CODEC_AV1;
    return JALIUM_CODEC_NONE;
}

std::shared_ptr<GpuImageReader> TryCreateGpuReader(uint32_t width, uint32_t height)
{
    const auto& api = GpuImageApi::Get();
    uint32_t stride = 0; size_t bytes = 0;
    if (GetApiLevel() < 26 || !api.Available() ||
        !jalium_media_compute_bgra_layout(width, height, &stride, &bytes)) return {};
    try {
        auto reader = std::make_shared<GpuImageReader>(); reader->api = &api;
        if (api.create(width, height, AIMAGE_FORMAT_PRIVATE,
                AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE, 8, &reader->reader) != AMEDIA_OK ||
            api.window(reader->reader, &reader->window) != AMEDIA_OK || !reader->window) return {};
        return reader;
    } catch (const std::bad_alloc&) { return {}; }
}

// KEY_COLOR_STANDARD constants from android.media.MediaFormat.
ColorMatrix DeriveColorMatrix(int32_t color_standard, uint32_t height)
{
    switch (color_standard) {
        case 1: // COLOR_STANDARD_BT709
            return ColorMatrix::Bt709;
        case 2: // COLOR_STANDARD_BT601_NTSC
        case 4: // COLOR_STANDARD_BT601_PAL
            return ColorMatrix::Bt601;
        case 6: // COLOR_STANDARD_BT2020 — fall back to BT.709 for SDR conversion
            return ColorMatrix::Bt709;
        default:
            // Heuristic: SD (height <= 576) typically uses BT.601, HD+ uses BT.709.
            return (height <= 576) ? ColorMatrix::Bt601 : ColorMatrix::Bt709;
    }
}

bool IsNV12Format(int32_t color_format)
{
    return color_format == COLOR_FormatYUV420SemiPlanar
        || color_format == COLOR_QCOM_FormatYUV420SemiPlanar;
}

bool IsI420Format(int32_t color_format)
{
    return color_format == COLOR_FormatYUV420Planar;
}

void ReleaseDecoderState(jalium_video_decoder_t* d)
{
    if (!d) return;
    if (d->codec) {
        AMediaCodec_stop(d->codec);
        AMediaCodec_delete(d->codec);
        d->codec = nullptr;
    }
    if (d->gpu_frame) {
        GpuImageFrame::Release(reinterpret_cast<uint64_t>(d->gpu_frame));
        d->gpu_frame = nullptr;
    }
    d->gpu_reader.reset();
    if (d->extractor) {
        AMediaExtractor_delete(d->extractor);
        d->extractor = nullptr;
    }
    if (d->fd >= 0) {
        close(d->fd);
        d->fd = -1;
    }
    if (d->frame_buffer) {
        jalium_media_aligned_free(d->frame_buffer);
        d->frame_buffer = nullptr;
        d->frame_buffer_size = 0;
    }
}

jalium_media_status_t ApplyOutputFormat(jalium_video_decoder_t* d, AMediaFormat* fmt)
{
    int32_t w = 0, h = 0, color = 0, std_ = 0, stride = 0, slice_height = 0;
    AMediaFormat_getInt32(fmt, AMEDIAFORMAT_KEY_WIDTH,  &w);
    AMediaFormat_getInt32(fmt, AMEDIAFORMAT_KEY_HEIGHT, &h);
    AMediaFormat_getInt32(fmt, AMEDIAFORMAT_KEY_COLOR_FORMAT, &color);
    AMediaFormat_getInt32(fmt, "color-standard", &std_);
    AMediaFormat_getInt32(fmt, AMEDIAFORMAT_KEY_STRIDE, &stride);
    // The public key constant was introduced after minSdk 24; the literal key
    // is stable and avoids importing a newer NDK data symbol into the ELF.
    AMediaFormat_getInt32(fmt, "slice-height", &slice_height);

    if (w <= 0 || h <= 0) return JALIUM_MEDIA_E_DECODE_FAILED;

    const uint32_t output_width = static_cast<uint32_t>(w);
    const uint32_t output_height = static_cast<uint32_t>(h);
    uint32_t output_stride = 0;
    size_t needed = 0;
    if (!jalium_media_compute_bgra_layout(
            output_width, output_height, &output_stride, &needed)) {
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }

    d->width        = output_width;
    d->height       = output_height;
    d->stride_bytes = output_stride;
    d->source_stride = static_cast<uint32_t>(stride > 0 ? stride : w);
    d->source_slice_height = static_cast<uint32_t>(slice_height > 0 ? slice_height : h);
    d->color_format = (color != 0) ? color : COLOR_FormatYUV420Flexible;
    d->color_standard = std_;
    AMediaFormat_getInt32(fmt, "color-range", &d->color_range);
    AMediaFormat_getInt32(fmt, "color-transfer", &d->color_transfer);

    if (!d->gpu_output && d->frame_buffer_size < needed) {
        auto* replacement = static_cast<uint8_t*>(
            jalium_media_aligned_alloc(needed));
        if (!replacement) return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        if (d->frame_buffer) jalium_media_aligned_free(d->frame_buffer);
        d->frame_buffer = replacement;
        d->frame_buffer_size = needed;
    }
    return JALIUM_MEDIA_OK;
}

// Convert a single AImage (NV12 / I420) into the decoder's BGRA8 frame buffer.
jalium_media_status_t ConvertAImageToBgra(jalium_video_decoder_t* d, AImage* img)
{
    int32_t plane_count = 0;
    if (AImage_getNumberOfPlanes(img, &plane_count) != AMEDIA_OK || plane_count < 3) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    // Plane 0 = Y; planes 1/2 = U/V (or interleaved on NV12 — pixelStride=2).
    uint8_t *yPlane = nullptr, *uPlane = nullptr, *vPlane = nullptr;
    int32_t  yLen = 0, uLen = 0, vLen = 0;
    int32_t  yRowStride = 0, uRowStride = 0, vRowStride = 0;
    int32_t  yPixelStride = 0, uPixelStride = 0, vPixelStride = 0;

    if (AImage_getPlaneData(img, 0, &yPlane, &yLen)        != AMEDIA_OK) return JALIUM_MEDIA_E_DECODE_FAILED;
    if (AImage_getPlaneRowStride(img, 0, &yRowStride)      != AMEDIA_OK) return JALIUM_MEDIA_E_DECODE_FAILED;
    if (AImage_getPlanePixelStride(img, 0, &yPixelStride)  != AMEDIA_OK) yPixelStride = 1;
    if (AImage_getPlaneData(img, 1, &uPlane, &uLen)        != AMEDIA_OK) return JALIUM_MEDIA_E_DECODE_FAILED;
    if (AImage_getPlaneRowStride(img, 1, &uRowStride)      != AMEDIA_OK) return JALIUM_MEDIA_E_DECODE_FAILED;
    if (AImage_getPlanePixelStride(img, 1, &uPixelStride)  != AMEDIA_OK) uPixelStride = 1;
    if (AImage_getPlaneData(img, 2, &vPlane, &vLen)        != AMEDIA_OK) return JALIUM_MEDIA_E_DECODE_FAILED;
    if (AImage_getPlaneRowStride(img, 2, &vRowStride)      != AMEDIA_OK) return JALIUM_MEDIA_E_DECODE_FAILED;
    if (AImage_getPlanePixelStride(img, 2, &vPixelStride)  != AMEDIA_OK) vPixelStride = 1;

    const uint32_t chroma_width = d->width / 2u + d->width % 2u;
    const uint32_t chroma_height = d->height / 2u + d->height % 2u;
    if (yPixelStride != 1 ||
        !IsYuvPlaneReadable(
            yPlane, yLen, yRowStride, yPixelStride,
            d->width, d->height)) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    auto matrix = DeriveColorMatrix(d->color_standard, d->height);

    if (uPixelStride == 2 && vPixelStride == 2) {
        const uintptr_t u_address = reinterpret_cast<uintptr_t>(uPlane);
        const uintptr_t v_address = reinterpret_cast<uintptr_t>(vPlane);
        const uintptr_t plane_delta = u_address > v_address
            ? u_address - v_address
            : v_address - u_address;
        if (plane_delta != 1u || chroma_width > UINT32_MAX / 2u) {
            return JALIUM_MEDIA_E_DECODE_FAILED;
        }
        uint8_t* uv_plane = u_address < v_address ? uPlane : vPlane;
        const int32_t uv_length = u_address < v_address ? uLen : vLen;
        const int32_t uv_row_stride =
            u_address < v_address ? uRowStride : vRowStride;
        if (!IsYuvPlaneReadable(
                uv_plane, uv_length, uv_row_stride, 1,
                chroma_width * 2u, chroma_height)) {
            return JALIUM_MEDIA_E_DECODE_FAILED;
        }
        // Semi-planar: U and V are interleaved. NV12: U first; NV21: V first.
        // The AImage API gives separate U/V "planes" that are actually views into
        // the same interleaved buffer offset by 1 byte. Detect order by pointer math.
        if (u_address > v_address) {
            // V comes first → NV21
            NV21ToBgra(yPlane, static_cast<uint32_t>(yRowStride),
                       uv_plane, static_cast<uint32_t>(uv_row_stride),
                       d->frame_buffer, d->stride_bytes,
                       d->width, d->height,
                       matrix, d->format);
        } else {
            // U comes first → NV12
            NV12ToBgra(yPlane, static_cast<uint32_t>(yRowStride),
                       uv_plane, static_cast<uint32_t>(uv_row_stride),
                       d->frame_buffer, d->stride_bytes,
                       d->width, d->height,
                       matrix, d->format);
        }
    } else {
        if (uPixelStride != 1 || vPixelStride != 1 ||
            !IsYuvPlaneReadable(
                uPlane, uLen, uRowStride, uPixelStride,
                chroma_width, chroma_height) ||
            !IsYuvPlaneReadable(
                vPlane, vLen, vRowStride, vPixelStride,
                chroma_width, chroma_height)) {
            return JALIUM_MEDIA_E_DECODE_FAILED;
        }
        // Planar I420.
        I420ToBgra(yPlane, static_cast<uint32_t>(yRowStride),
                   uPlane, static_cast<uint32_t>(uRowStride),
                   vPlane, static_cast<uint32_t>(vRowStride),
                   d->frame_buffer, d->stride_bytes,
                   d->width, d->height,
                   matrix, d->format);
    }
    return JALIUM_MEDIA_OK;
}

// NDK MediaCodec has never exposed AMediaCodec_getOutputImage. In ByteBuffer
// mode we can safely consume only layouts whose plane arrangement is defined
// by the reported codec color format. Flexible/vendor-private layouts are
// rejected explicitly instead of guessing and risking an out-of-bounds read.
jalium_media_status_t ConvertOutputBufferToBgra(
    jalium_video_decoder_t* d,
    const uint8_t*          data,
    size_t                  size)
{
    if (!d || !data || d->width == 0 || d->height == 0) {
        return JALIUM_MEDIA_E_INVALID_ARG;
    }

    const uint32_t y_stride = d->source_stride > 0 ? d->source_stride : d->width;
    const uint32_t slice_height = d->source_slice_height > 0
        ? d->source_slice_height
        : d->height;
    if (y_stride < d->width || slice_height < d->height) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    uint32_t output_stride = 0;
    size_t output_size = 0;
    if (!jalium_media_compute_bgra_layout(
            d->width, d->height, &output_stride, &output_size) ||
        output_stride != d->stride_bytes) {
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    if (d->frame_buffer_size < output_size) {
        auto* replacement = static_cast<uint8_t*>(
            jalium_media_aligned_alloc(output_size));
        if (!replacement) return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        if (d->frame_buffer) jalium_media_aligned_free(d->frame_buffer);
        d->frame_buffer = replacement;
        d->frame_buffer_size = output_size;
    }

    if (static_cast<size_t>(slice_height) >
        SIZE_MAX / static_cast<size_t>(y_stride)) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
    const size_t y_size = static_cast<size_t>(y_stride) * slice_height;
    const uint32_t chroma_rows = (slice_height + 1u) / 2u;
    auto matrix = DeriveColorMatrix(d->color_standard, d->height);

    if (IsNV12Format(d->color_format)) {
        if (static_cast<size_t>(chroma_rows) >
            (SIZE_MAX - y_size) / static_cast<size_t>(y_stride)) {
            return JALIUM_MEDIA_E_DECODE_FAILED;
        }
        const size_t required =
            y_size + static_cast<size_t>(y_stride) * chroma_rows;
        if (size < required) return JALIUM_MEDIA_E_DECODE_FAILED;

        NV12ToBgra(data, y_stride,
                   data + y_size, y_stride,
                   d->frame_buffer, d->stride_bytes,
                   d->width, d->height,
                   matrix, d->format);
        return JALIUM_MEDIA_OK;
    }

    if (IsI420Format(d->color_format)) {
        const uint32_t chroma_stride = (y_stride + 1u) / 2u;
        const size_t chroma_size = static_cast<size_t>(chroma_stride) * chroma_rows;
        if (chroma_size > (SIZE_MAX - y_size) / 2u) {
            return JALIUM_MEDIA_E_DECODE_FAILED;
        }
        const size_t required = y_size + chroma_size * 2u;
        if (size < required) return JALIUM_MEDIA_E_DECODE_FAILED;

        const uint8_t* u_plane = data + y_size;
        const uint8_t* v_plane = u_plane + chroma_size;
        I420ToBgra(data, y_stride,
                   u_plane, chroma_stride,
                   v_plane, chroma_stride,
                   d->frame_buffer, d->stride_bytes,
                   d->width, d->height,
                   matrix, d->format);
        return JALIUM_MEDIA_OK;
    }

    ANDLOGW("Unsupported MediaCodec ByteBuffer color format 0x%x", d->color_format);
    return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
}

} // anonymous

jalium_media_status_t VideoDecoderOpenFile(
    const char*              utf8_path,
    jalium_pixel_format_t    requested_format,
    jalium_video_decoder_t** out_decoder)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!utf8_path || !out_decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    if (!jalium_media_is_valid_pixel_format(requested_format))
        return JALIUM_MEDIA_E_INVALID_ARG;
    *out_decoder = nullptr;

    int fd = open(utf8_path, O_RDONLY | O_CLOEXEC);
    if (fd < 0) return JALIUM_MEDIA_E_IO;

    struct stat st{};
    if (fstat(fd, &st) != 0 || st.st_size <= 0) {
        close(fd);
        return JALIUM_MEDIA_E_IO;
    }

    AMediaExtractor* ex = AMediaExtractor_new();
    if (!ex) {
        close(fd);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    if (AMediaExtractor_setDataSourceFd(ex, fd, 0, st.st_size) != AMEDIA_OK) {
        AMediaExtractor_delete(ex);
        close(fd);
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }

    // Find the first video track.
    int trackCount = static_cast<int>(AMediaExtractor_getTrackCount(ex));
    int videoTrack = -1;
    AMediaFormat* trackFormat = nullptr;
    const char* mime = nullptr;
    for (int i = 0; i < trackCount; ++i) {
        AMediaFormat* tf = AMediaExtractor_getTrackFormat(ex, i);
        const char* m = nullptr;
        if (tf && AMediaFormat_getString(tf, AMEDIAFORMAT_KEY_MIME, &m) && m && std::strncmp(m, "video/", 6) == 0) {
            videoTrack = i;
            trackFormat = tf;
            mime = m;
            break;
        }
        if (tf) AMediaFormat_delete(tf);
    }
    if (videoTrack < 0 || !trackFormat) {
        AMediaExtractor_delete(ex);
        close(fd);
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }

    AMediaExtractor_selectTrack(ex, videoTrack);

    AMediaCodec* codec = AMediaCodec_createDecoderByType(mime);
    if (!codec) {
        AMediaFormat_delete(trackFormat);
        AMediaExtractor_delete(ex);
        close(fd);
        return JALIUM_MEDIA_E_UNSUPPORTED_CODEC;
    }

    // Hint the codec to deliver a flexible YUV layout we can read via AImage planes.
    AMediaFormat_setInt32(trackFormat, AMEDIAFORMAT_KEY_COLOR_FORMAT, COLOR_FormatYUV420Flexible);

    std::shared_ptr<GpuImageReader> initialReader;
    media_status_t configured = AMediaCodec_configure(codec, trackFormat, nullptr, nullptr, 0);
    if (configured == AMEDIA_OK) configured = AMediaCodec_start(codec);
    if (configured != AMEDIA_OK) {
        // Some hardware codecs only support Surface output. Do not require a
        // working ByteBuffer configuration merely to open their GPU path.
        AMediaCodec_delete(codec); codec = nullptr;
        int32_t width = 0, height = 0;
        AMediaFormat_getInt32(trackFormat, AMEDIAFORMAT_KEY_WIDTH, &width);
        AMediaFormat_getInt32(trackFormat, AMEDIAFORMAT_KEY_HEIGHT, &height);
        if (width > 0 && height > 0) initialReader = TryCreateGpuReader(width, height);
        auto* surfaceFormat = initialReader ? AMediaExtractor_getTrackFormat(ex, videoTrack) : nullptr;
        codec = surfaceFormat ? AMediaCodec_createDecoderByType(mime) : nullptr;
        configured = codec ? AMediaCodec_configure(codec, surfaceFormat, initialReader->window, nullptr, 0) : AMEDIA_ERROR_UNKNOWN;
        if (surfaceFormat) AMediaFormat_delete(surfaceFormat);
        if (configured == AMEDIA_OK) configured = AMediaCodec_start(codec);
        if (configured != AMEDIA_OK) {
            if (codec) AMediaCodec_delete(codec);
            AMediaFormat_delete(trackFormat); AMediaExtractor_delete(ex); close(fd);
            return JALIUM_MEDIA_E_UNSUPPORTED_CODEC;
        }
    }

    auto* d = new (std::nothrow) jalium_video_decoder();
    if (!d) {
        AMediaCodec_stop(codec);
        AMediaCodec_delete(codec);
        AMediaFormat_delete(trackFormat);
        AMediaExtractor_delete(ex);
        close(fd);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    d->extractor    = ex;
    d->codec        = codec;
    d->track_index  = videoTrack;
    d->fd           = fd;
    d->format       = requested_format;
    d->active_codec = MapMimeToCodec(mime);
    try { d->mime = mime; }
    catch (const std::bad_alloc&) {
        ReleaseDecoderState(d); delete d; AMediaFormat_delete(trackFormat);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    d->gpu_reader = std::move(initialReader);
    d->gpu_output = d->gpu_reader != nullptr;
    d->gpu_attempted = d->gpu_output;

    // Initial dimensions / fps from the extractor track format.
    int32_t w = 0, h = 0, fps_n = 0;
    AMediaFormat_getInt32(trackFormat, AMEDIAFORMAT_KEY_WIDTH,  &w);
    AMediaFormat_getInt32(trackFormat, AMEDIAFORMAT_KEY_HEIGHT, &h);
    AMediaFormat_getInt32(trackFormat, AMEDIAFORMAT_KEY_FRAME_RATE, &fps_n);
    d->width  = static_cast<uint32_t>(w > 0 ? w : 0);
    d->height = static_cast<uint32_t>(h > 0 ? h : 0);
    d->stride_bytes = jalium_media_compute_stride(d->width);
    d->source_stride = d->width;
    d->source_slice_height = d->height;
    d->fps = static_cast<double>(fps_n);

    int64_t durUs = 0;
    if (AMediaFormat_getInt64(trackFormat, AMEDIAFORMAT_KEY_DURATION, &durUs) && durUs > 0) {
        d->duration_s = static_cast<double>(durUs) / 1'000'000.0;
        if (d->fps > 0.0) {
            d->frame_count = static_cast<uint64_t>(d->duration_s * d->fps);
        }
    }

    AMediaFormat_delete(trackFormat);

    *out_decoder = d;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t VideoDecoderGetInfo(
    jalium_video_decoder_t* decoder,
    jalium_video_info_t*    out_info)
{
    if (!decoder || !out_info) return JALIUM_MEDIA_E_INVALID_ARG;
    out_info->width            = decoder->width;
    out_info->height           = decoder->height;
    out_info->duration_seconds = decoder->duration_s;
    out_info->frame_rate       = decoder->fps;
    out_info->frame_count      = decoder->frame_count;
    out_info->active_codec     = decoder->active_codec;
    return JALIUM_MEDIA_OK;
}

namespace {
jalium_media_status_t ReadCodecOutput(jalium_video_decoder_t* d,
    size_t& index, AMediaCodecBufferInfo& info)
{
    if (!d->codec || !d->codec_ready) return JALIUM_MEDIA_E_PLATFORM;
    if (d->output_eos) return JALIUM_MEDIA_E_END_OF_STREAM;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (std::chrono::steady_clock::now() < deadline) {
        if (!d->input_eos) {
            ssize_t inIdx = AMediaCodec_dequeueInputBuffer(d->codec, 10000);
            if (inIdx >= 0) {
                size_t capacity = 0;
                auto* input = AMediaCodec_getInputBuffer(d->codec, size_t(inIdx), &capacity);
                if (!input) return JALIUM_MEDIA_E_PLATFORM;
                ssize_t size = AMediaExtractor_readSampleData(d->extractor, input, capacity);
                int64_t pts = AMediaExtractor_getSampleTime(d->extractor);
                if (size > 0 && size_t(size) > capacity) return JALIUM_MEDIA_E_DECODE_FAILED;
                auto result = AMediaCodec_queueInputBuffer(d->codec, size_t(inIdx), 0,
                    size < 0 ? 0 : size_t(size), size < 0 ? 0 : pts,
                    size < 0 ? AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM : 0);
                if (result != AMEDIA_OK) return JALIUM_MEDIA_E_DECODE_FAILED;
                if (size < 0) d->input_eos = true;
                else AMediaExtractor_advance(d->extractor);
            }
        }
        ssize_t outIdx = AMediaCodec_dequeueOutputBuffer(d->codec, &info, 10000);
        if (outIdx >= 0) {
            d->output_eos = (info.flags & AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM) != 0;
            if ((d->output_eos && info.size == 0) ||
                (info.flags & AMEDIACODEC_BUFFER_FLAG_CODEC_CONFIG) ||
                (d->seek_target_us >= 0 && info.presentationTimeUs < d->seek_target_us)) {
                AMediaCodec_releaseOutputBuffer(d->codec, size_t(outIdx), false);
                if (d->output_eos) return JALIUM_MEDIA_E_END_OF_STREAM;
                continue;
            }
            d->seek_target_us = -1;
            index = size_t(outIdx);
            d->last_pts_us = info.presentationTimeUs;
            d->last_keyframe = (info.flags & 1) != 0;
            return JALIUM_MEDIA_OK;
        }
        if (outIdx == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) {
            auto* format = AMediaCodec_getOutputFormat(d->codec);
            if (!format) return JALIUM_MEDIA_E_PLATFORM;
            auto status = ApplyOutputFormat(d, format);
            AMediaFormat_delete(format);
            if (status != JALIUM_MEDIA_OK) return status;
        } else if (outIdx != AMEDIACODEC_INFO_TRY_AGAIN_LATER &&
                   outIdx != AMEDIACODEC_INFO_OUTPUT_BUFFERS_CHANGED) {
            return JALIUM_MEDIA_E_DECODE_FAILED;
        }
    }
    return JALIUM_MEDIA_E_DECODE_FAILED;
}

jalium_media_status_t ReconfigureOutput(jalium_video_decoder_t* d,
    std::shared_ptr<GpuImageReader> reader, int64_t target)
{
    d->codec_ready = false;
    if (d->codec) {
        AMediaCodec_stop(d->codec); AMediaCodec_delete(d->codec); d->codec = nullptr;
    }
    if (d->gpu_frame) {
        GpuImageFrame::Release(reinterpret_cast<uint64_t>(d->gpu_frame)); d->gpu_frame = nullptr;
    }
    d->gpu_reader = std::move(reader);
    d->gpu_output = d->gpu_reader != nullptr;
    d->input_eos = d->output_eos = false;
    d->seek_target_us = target;
    if (AMediaExtractor_seekTo(d->extractor, target, AMEDIAEXTRACTOR_SEEK_PREVIOUS_SYNC) != AMEDIA_OK)
        return JALIUM_MEDIA_E_PLATFORM;
    auto* format = AMediaExtractor_getTrackFormat(d->extractor, d->track_index);
    if (!format) return JALIUM_MEDIA_E_PLATFORM;
    d->codec = AMediaCodec_createDecoderByType(d->mime.c_str());
    if (!d->gpu_output) AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_COLOR_FORMAT, COLOR_FormatYUV420Flexible);
    auto status = d->codec ? AMediaCodec_configure(d->codec, format,
        d->gpu_output ? d->gpu_reader->window : nullptr, nullptr, 0) : AMEDIA_ERROR_UNKNOWN;
    AMediaFormat_delete(format);
    if (status != AMEDIA_OK || AMediaCodec_start(d->codec) != AMEDIA_OK)
        return JALIUM_MEDIA_E_PLATFORM;
    d->codec_ready = true;
    return JALIUM_MEDIA_OK;
}
} // namespace

jalium_media_status_t VideoDecoderReadFrame(jalium_video_decoder_t* d, jalium_video_frame_t* frame)
{
    if (!d || !d->codec || !frame) return JALIUM_MEDIA_E_INVALID_ARG;
    *frame = {};
    if (d->gpu_output) {
        auto status = VideoDecoderDisableGpuOutput(d);
        if (status != JALIUM_MEDIA_OK) return status;
    }
    size_t index = 0;
    AMediaCodecBufferInfo info{};
    auto status = ReadCodecOutput(d, index, info);
    if (status != JALIUM_MEDIA_OK) return status;
    size_t capacity = 0;
    auto* output = AMediaCodec_getOutputBuffer(d->codec, index, &capacity);
    bool readable = output && info.offset >= 0 && info.size >= 0;
    if (readable && GetApiLevel() >= 36) {
        readable = size_t(info.offset) <= capacity && size_t(info.size) <= capacity - size_t(info.offset);
    }
    status = readable ? ConvertOutputBufferToBgra(d, output + info.offset, size_t(info.size))
                      : JALIUM_MEDIA_E_DECODE_FAILED;
    AMediaCodec_releaseOutputBuffer(d->codec, index, false);
    if (status != JALIUM_MEDIA_OK) return status;
    frame->width = d->width; frame->height = d->height; frame->stride_bytes = d->stride_bytes;
    frame->format = d->format; frame->pixels = d->frame_buffer;
    frame->pts_microseconds = d->last_pts_us; frame->is_keyframe = d->last_keyframe;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t VideoDecoderReadGpuFrame(jalium_video_decoder_t* d,
    jalium_video_decoder_gpu_descriptor_t* descriptor, int64_t* pts, int32_t* keyframe)
{
    if (!d || !descriptor || !pts || !keyframe || !d->codec) return JALIUM_MEDIA_E_INVALID_ARG;
    if (!d->gpu_attempted) {
        d->gpu_attempted = true;
        auto reader = TryCreateGpuReader(d->width, d->height);
        if (!reader) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
        const int64_t target = d->seek_target_us >= 0 ? d->seek_target_us : d->last_pts_us;
        if (ReconfigureOutput(d, reader, target) != JALIUM_MEDIA_OK) {
            auto status = ReconfigureOutput(d, {}, target);
            return status == JALIUM_MEDIA_OK ? JALIUM_MEDIA_E_NOT_IMPLEMENTED : status;
        }
    }
    if (!d->gpu_output) return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    if (d->gpu_frame) {
        GpuImageFrame::Release(reinterpret_cast<uint64_t>(d->gpu_frame)); d->gpu_frame = nullptr;
    }
    size_t index = 0;
    AMediaCodecBufferInfo info{};
    auto status = ReadCodecOutput(d, index, info);
    if (status != JALIUM_MEDIA_OK) return status;
    if (AMediaCodec_releaseOutputBuffer(d->codec, index, true) != AMEDIA_OK) return JALIUM_MEDIA_E_PLATFORM;
    std::unique_ptr<GpuImageFrame> frame(new (std::nothrow) GpuImageFrame());
    if (!frame) return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    frame->owner = d->gpu_reader;
    const auto& api = *frame->owner->api;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(500);
    media_status_t acquired;
    do {
        acquired = api.acquire(frame->owner->reader, &frame->image, &frame->acquireFence);
        if (acquired != AMEDIA_IMGREADER_NO_BUFFER_AVAILABLE) break;
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    } while (std::chrono::steady_clock::now() < deadline);
    if (acquired != AMEDIA_OK || !frame->image || api.buffer(frame->image, &frame->buffer) != AMEDIA_OK || !frame->buffer)
        return JALIUM_MEDIA_E_PLATFORM;
    AHardwareBuffer_Desc description{}; api.describe(frame->buffer, &description);
    AImageCropRect crop{};
    if (AImage_getCropRect(frame->image, &crop) != AMEDIA_OK || crop.left || crop.top ||
        crop.right != int32_t(description.width) || crop.bottom != int32_t(description.height))
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    frame->width = description.width; frame->height = description.height;
    frame->formatHint = description.format == 0x36 ? 2 : 1; // YCbCr_P010 / external YUV
    switch (d->color_standard) {
    case 1: frame->colorSpace = 2; break; // BT.709
    case 2: case 4: frame->colorSpace = 1; break; // BT.601
    case 6: frame->colorSpace = 3; break; // BT.2020
    default: break; // use gralloc's conversion suggestions
    }
    if (d->color_range == 1) frame->flags |= 1u; // full range
    if (d->color_transfer == 6 || d->color_transfer == 7) frame->flags |= 8u; // PQ / HLG
    status = frame->Export(*descriptor);
    if (status != JALIUM_MEDIA_OK) return status;
    d->gpu_frame = frame.release();
    *pts = d->last_pts_us; *keyframe = d->last_keyframe;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t VideoDecoderAcquireGpuDescriptor(jalium_video_decoder_t* d,
    jalium_video_decoder_gpu_descriptor_t* descriptor)
{
    if (!d || !descriptor) return JALIUM_MEDIA_E_INVALID_ARG;
    return d->gpu_frame ? d->gpu_frame->Export(*descriptor) : JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

jalium_media_status_t VideoDecoderDisableGpuOutput(jalium_video_decoder_t* d)
{
    if (!d) return JALIUM_MEDIA_E_INVALID_ARG;
    d->gpu_attempted = true;
    return d->gpu_output || !d->codec_ready ? ReconfigureOutput(d, {}, d->last_pts_us) : JALIUM_MEDIA_OK;
}

jalium_media_status_t VideoDecoderSeek(
    jalium_video_decoder_t* d,
    int64_t                 pts_microseconds)
{
    if (!d || !d->extractor || !d->codec) return JALIUM_MEDIA_E_INVALID_ARG;
    if (pts_microseconds < 0) return JALIUM_MEDIA_E_INVALID_ARG;
    if (d->gpu_output) {
        // A delayed pre-seek buffer can still arrive after stopping the codec.
        // Give the new decode a fresh BufferQueue; outstanding old frames keep
        // their old reader alive independently through their render fences.
        auto reader = TryCreateGpuReader(d->width, d->height);
        if (!reader) return JALIUM_MEDIA_E_PLATFORM;
        return ReconfigureOutput(d, std::move(reader), pts_microseconds);
    }
    d->seek_target_us = pts_microseconds;
    if (AMediaExtractor_seekTo(d->extractor, pts_microseconds, AMEDIAEXTRACTOR_SEEK_PREVIOUS_SYNC) != AMEDIA_OK) {
        return JALIUM_MEDIA_E_PLATFORM;
    }
    if (AMediaCodec_flush(d->codec) != AMEDIA_OK) return JALIUM_MEDIA_E_PLATFORM;
    d->input_eos = false;
    d->output_eos = false;
    return JALIUM_MEDIA_OK;
}

void VideoDecoderClose(jalium_video_decoder_t* d)
{
    if (!d) return;
    ReleaseDecoderState(d);
    delete d;
}

} // namespace jalium::media::android
