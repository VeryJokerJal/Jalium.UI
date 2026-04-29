#define JALIUM_MEDIA_EXPORTS
#include "and_video_decoder.h"
#include "and_media_init.h"
#include "and_yuv_simd.h"
#include "jalium_media_internal.h"

#include <media/NdkMediaCodec.h>
#include <media/NdkMediaExtractor.h>
#include <media/NdkMediaFormat.h>
#include <media/NdkImage.h>

#include <android/log.h>
#include <fcntl.h>
#include <sys/stat.h>
#include <unistd.h>

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
        || color_format == COLOR_QCOM_FormatYUV420SemiPlanar
        || color_format == COLOR_FormatYUV420Flexible;
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
    int32_t w = 0, h = 0, color = 0, std_ = 0, stride = 0;
    AMediaFormat_getInt32(fmt, AMEDIAFORMAT_KEY_WIDTH,  &w);
    AMediaFormat_getInt32(fmt, AMEDIAFORMAT_KEY_HEIGHT, &h);
    AMediaFormat_getInt32(fmt, AMEDIAFORMAT_KEY_COLOR_FORMAT, &color);
    AMediaFormat_getInt32(fmt, "color-standard", &std_);
    AMediaFormat_getInt32(fmt, AMEDIAFORMAT_KEY_STRIDE, &stride);

    if (w <= 0 || h <= 0) return JALIUM_MEDIA_E_DECODE_FAILED;

    d->width        = static_cast<uint32_t>(w);
    d->height       = static_cast<uint32_t>(h);
    d->stride_bytes = jalium_media_compute_stride(d->width);
    d->color_format = (color != 0) ? color : COLOR_FormatYUV420Flexible;
    d->color_standard = std_;

    const size_t needed = static_cast<size_t>(d->stride_bytes) * d->height;
    if (d->frame_buffer_size < needed) {
        if (d->frame_buffer) jalium_media_aligned_free(d->frame_buffer);
        d->frame_buffer = static_cast<uint8_t*>(jalium_media_aligned_alloc(needed));
        if (!d->frame_buffer) return JALIUM_MEDIA_E_OUT_OF_MEMORY;
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

    auto matrix = DeriveColorMatrix(d->color_standard, d->height);

    if (uPixelStride == 2 && vPixelStride == 2) {
        // Semi-planar: U and V are interleaved. NV12: U first; NV21: V first.
        // The AImage API gives separate U/V "planes" that are actually views into
        // the same interleaved buffer offset by 1 byte. Detect order by pointer math.
        if (uPlane > vPlane) {
            // V comes first → NV21
            NV21ToBgra(yPlane, static_cast<uint32_t>(yRowStride),
                       vPlane, static_cast<uint32_t>(vRowStride),
                       d->frame_buffer, d->stride_bytes,
                       d->width, d->height,
                       matrix, d->format);
        } else {
            // U comes first → NV12
            NV12ToBgra(yPlane, static_cast<uint32_t>(yRowStride),
                       uPlane, static_cast<uint32_t>(uRowStride),
                       d->frame_buffer, d->stride_bytes,
                       d->width, d->height,
                       matrix, d->format);
        }
    } else {
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

} // anonymous

jalium_media_status_t VideoDecoderOpenFile(
    const char*              utf8_path,
    jalium_pixel_format_t    requested_format,
    jalium_video_decoder_t** out_decoder)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!utf8_path || !out_decoder) return JALIUM_MEDIA_E_INVALID_ARG;
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

    if (AMediaCodec_configure(codec, trackFormat, /*surface*/ nullptr, /*crypto*/ nullptr, 0) != AMEDIA_OK) {
        AMediaCodec_delete(codec);
        AMediaFormat_delete(trackFormat);
        AMediaExtractor_delete(ex);
        close(fd);
        return JALIUM_MEDIA_E_UNSUPPORTED_CODEC;
    }
    if (AMediaCodec_start(codec) != AMEDIA_OK) {
        AMediaCodec_delete(codec);
        AMediaFormat_delete(trackFormat);
        AMediaExtractor_delete(ex);
        close(fd);
        return JALIUM_MEDIA_E_PLATFORM;
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

    // Initial dimensions / fps from the extractor track format.
    int32_t w = 0, h = 0, fps_n = 0;
    AMediaFormat_getInt32(trackFormat, AMEDIAFORMAT_KEY_WIDTH,  &w);
    AMediaFormat_getInt32(trackFormat, AMEDIAFORMAT_KEY_HEIGHT, &h);
    AMediaFormat_getInt32(trackFormat, AMEDIAFORMAT_KEY_FRAME_RATE, &fps_n);
    d->width  = static_cast<uint32_t>(w > 0 ? w : 0);
    d->height = static_cast<uint32_t>(h > 0 ? h : 0);
    d->stride_bytes = jalium_media_compute_stride(d->width);
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

jalium_media_status_t VideoDecoderReadFrame(
    jalium_video_decoder_t* d,
    jalium_video_frame_t*   out_frame)
{
    if (!d || !d->codec || !d->extractor || !out_frame) return JALIUM_MEDIA_E_INVALID_ARG;

    constexpr int64_t TIMEOUT_US = 10'000;  // 10 ms

    // Pump input until we have an output sample (or EOS).
    while (true) {
        // Feed input.
        if (!d->input_eos) {
            ssize_t inIdx = AMediaCodec_dequeueInputBuffer(d->codec, TIMEOUT_US);
            if (inIdx >= 0) {
                size_t inBufSize = 0;
                uint8_t* inBuf = AMediaCodec_getInputBuffer(d->codec, static_cast<size_t>(inIdx), &inBufSize);
                if (!inBuf) {
                    return JALIUM_MEDIA_E_PLATFORM;
                }
                ssize_t sampleSize = AMediaExtractor_readSampleData(d->extractor, inBuf, inBufSize);
                int64_t presTime  = AMediaExtractor_getSampleTime(d->extractor);
                if (sampleSize < 0) {
                    AMediaCodec_queueInputBuffer(d->codec, static_cast<size_t>(inIdx), 0, 0, 0,
                                                 AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM);
                    d->input_eos = true;
                } else {
                    AMediaCodec_queueInputBuffer(d->codec, static_cast<size_t>(inIdx), 0,
                                                 static_cast<size_t>(sampleSize),
                                                 static_cast<uint64_t>(presTime), 0);
                    AMediaExtractor_advance(d->extractor);
                }
            }
        }

        // Drain output.
        AMediaCodecBufferInfo info{};
        ssize_t outIdx = AMediaCodec_dequeueOutputBuffer(d->codec, &info, TIMEOUT_US);
        if (outIdx >= 0) {
            if (info.flags & AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM) {
                AMediaCodec_releaseOutputBuffer(d->codec, static_cast<size_t>(outIdx), false);
                d->output_eos = true;
                return JALIUM_MEDIA_E_END_OF_STREAM;
            }

            // Pull the AImage for this output buffer (planar YUV access).
            AImage* img = nullptr;
            if (AMediaCodec_getOutputImage(d->codec, static_cast<size_t>(outIdx), &img) != AMEDIA_OK || !img) {
                AMediaCodec_releaseOutputBuffer(d->codec, static_cast<size_t>(outIdx), false);
                continue;
            }

            int32_t imgWidth = 0, imgHeight = 0;
            AImage_getWidth(img, &imgWidth);
            AImage_getHeight(img, &imgHeight);
            if (imgWidth > 0 && imgHeight > 0 &&
                (static_cast<uint32_t>(imgWidth) != d->width || static_cast<uint32_t>(imgHeight) != d->height)) {
                d->width = static_cast<uint32_t>(imgWidth);
                d->height = static_cast<uint32_t>(imgHeight);
                d->stride_bytes = jalium_media_compute_stride(d->width);
                const size_t needed = static_cast<size_t>(d->stride_bytes) * d->height;
                if (d->frame_buffer_size < needed) {
                    if (d->frame_buffer) jalium_media_aligned_free(d->frame_buffer);
                    d->frame_buffer = static_cast<uint8_t*>(jalium_media_aligned_alloc(needed));
                    if (!d->frame_buffer) {
                        AImage_delete(img);
                        AMediaCodec_releaseOutputBuffer(d->codec, static_cast<size_t>(outIdx), false);
                        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
                    }
                    d->frame_buffer_size = needed;
                }
            } else if (!d->frame_buffer) {
                const size_t needed = static_cast<size_t>(d->stride_bytes) * d->height;
                d->frame_buffer = static_cast<uint8_t*>(jalium_media_aligned_alloc(needed));
                if (!d->frame_buffer) {
                    AImage_delete(img);
                    AMediaCodec_releaseOutputBuffer(d->codec, static_cast<size_t>(outIdx), false);
                    return JALIUM_MEDIA_E_OUT_OF_MEMORY;
                }
                d->frame_buffer_size = needed;
            }

            auto status = ConvertAImageToBgra(d, img);
            AImage_delete(img);
            AMediaCodec_releaseOutputBuffer(d->codec, static_cast<size_t>(outIdx), false);

            if (status != JALIUM_MEDIA_OK) return status;

            d->last_pts_us = info.presentationTimeUs;

            out_frame->width        = d->width;
            out_frame->height       = d->height;
            out_frame->stride_bytes = d->stride_bytes;
            out_frame->format       = d->format;
            out_frame->pixels       = d->frame_buffer;
            out_frame->pts_microseconds = d->last_pts_us;
            out_frame->is_keyframe  = (info.flags & 1) ? 1 : 0;  // BUFFER_FLAG_KEY_FRAME
            return JALIUM_MEDIA_OK;
        } else if (outIdx == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) {
            AMediaFormat* newFmt = AMediaCodec_getOutputFormat(d->codec);
            if (newFmt) {
                ApplyOutputFormat(d, newFmt);
                AMediaFormat_delete(newFmt);
            }
            continue;
        } else if (outIdx == AMEDIACODEC_INFO_TRY_AGAIN_LATER) {
            if (d->input_eos && d->output_eos) {
                return JALIUM_MEDIA_E_END_OF_STREAM;
            }
            // Loop again to feed more input.
            continue;
        } else if (outIdx == AMEDIACODEC_INFO_OUTPUT_BUFFERS_CHANGED) {
            // Deprecated; ignore.
            continue;
        } else {
            return JALIUM_MEDIA_E_DECODE_FAILED;
        }
    }
}

jalium_media_status_t VideoDecoderSeek(
    jalium_video_decoder_t* d,
    int64_t                 pts_microseconds)
{
    if (!d || !d->extractor || !d->codec) return JALIUM_MEDIA_E_INVALID_ARG;
    if (AMediaExtractor_seekTo(d->extractor, pts_microseconds, AMEDIAEXTRACTOR_SEEK_PREVIOUS_SYNC) != AMEDIA_OK) {
        return JALIUM_MEDIA_E_PLATFORM;
    }
    AMediaCodec_flush(d->codec);
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
