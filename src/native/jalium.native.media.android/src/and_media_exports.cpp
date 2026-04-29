#define JALIUM_MEDIA_EXPORTS
#include "jalium_media.h"
#include "jalium_media_internal.h"

#include "and_media_init.h"
#include "and_image_decoder.h"
#include "and_video_decoder.h"
#include "and_camera_source.h"

extern "C" {

// ----- Lifecycle ---------------------------------------------------------

JALIUM_MEDIA_API jalium_media_status_t jalium_media_initialize(void)
{
    return jalium::media::android::Initialize();
}

JALIUM_MEDIA_API void jalium_media_shutdown(void)
{
    jalium::media::android::Shutdown();
}

JALIUM_MEDIA_API uint32_t jalium_media_supported_video_codecs(void)
{
    return jalium::media::android::GetSupportedVideoCodecs();
}

// ----- Image decoding ----------------------------------------------------

JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_memory(
    const uint8_t*        data,
    size_t                size,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image)
{
    if (!data || size == 0 || !out_image) return JALIUM_MEDIA_E_INVALID_ARG;
    return jalium::media::android::DecodeImageMemory(data, size, requested_format, out_image);
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_file(
    const char*           utf8_path,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image)
{
    if (!utf8_path || !out_image) return JALIUM_MEDIA_E_INVALID_ARG;
    return jalium::media::android::DecodeImageFile(utf8_path, requested_format, out_image);
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_read_dimensions(
    const uint8_t* data,
    size_t         size,
    uint32_t*      out_width,
    uint32_t*      out_height)
{
    if (!data || size == 0 || !out_width || !out_height) return JALIUM_MEDIA_E_INVALID_ARG;
    return jalium::media::android::ReadImageDimensions(data, size, out_width, out_height);
}

JALIUM_MEDIA_API void jalium_image_free(jalium_image_t* image)
{
    if (!image || !image->pixels) return;
    jalium_media_aligned_free(image->pixels);
    image->pixels = nullptr;
    image->_reserved = nullptr;
    image->width = 0;
    image->height = 0;
    image->stride_bytes = 0;
}

// ----- Video decoding ----------------------------------------------------

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_open_file(
    const char*              utf8_path,
    jalium_pixel_format_t    requested_format,
    jalium_video_decoder_t** out_decoder)
{
    if (!utf8_path || !out_decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_decoder = nullptr;
    return jalium::media::android::VideoDecoderOpenFile(utf8_path, requested_format, out_decoder);
}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_get_info(
    jalium_video_decoder_t* decoder,
    jalium_video_info_t*    out_info)
{
    if (!decoder || !out_info) return JALIUM_MEDIA_E_INVALID_ARG;
    return jalium::media::android::VideoDecoderGetInfo(decoder, out_info);
}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_read_frame(
    jalium_video_decoder_t* decoder,
    jalium_video_frame_t*   out_frame)
{
    if (!decoder || !out_frame) return JALIUM_MEDIA_E_INVALID_ARG;
    return jalium::media::android::VideoDecoderReadFrame(decoder, out_frame);
}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_seek_microseconds(
    jalium_video_decoder_t* decoder,
    int64_t                 pts_microseconds)
{
    if (!decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    return jalium::media::android::VideoDecoderSeek(decoder, pts_microseconds);
}

JALIUM_MEDIA_API void jalium_video_decoder_close(jalium_video_decoder_t* decoder)
{
    jalium::media::android::VideoDecoderClose(decoder);
}

// ----- Camera capture ----------------------------------------------------

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_enumerate(
    jalium_camera_device_t** out_devices,
    uint32_t*                out_count)
{
    if (!out_devices || !out_count) return JALIUM_MEDIA_E_INVALID_ARG;
    return jalium::media::android::CameraEnumerate(out_devices, out_count);
}

JALIUM_MEDIA_API void jalium_camera_devices_free(
    jalium_camera_device_t* devices,
    uint32_t                count)
{
    jalium::media::android::CameraDevicesFree(devices, count);
}

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_open(
    const char*              device_id,
    uint32_t                 requested_width,
    uint32_t                 requested_height,
    double                   requested_fps,
    jalium_pixel_format_t    requested_format,
    jalium_camera_source_t** out_source)
{
    if (!device_id || !out_source) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_source = nullptr;
    return jalium::media::android::CameraOpen(
        device_id, requested_width, requested_height, requested_fps,
        requested_format, out_source);
}

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_read_frame(
    jalium_camera_source_t* source,
    jalium_video_frame_t*   out_frame)
{
    if (!source || !out_frame) return JALIUM_MEDIA_E_INVALID_ARG;
    return jalium::media::android::CameraReadFrame(source, out_frame);
}

JALIUM_MEDIA_API void jalium_camera_close(jalium_camera_source_t* source)
{
    jalium::media::android::CameraClose(source);
}

} // extern "C"
