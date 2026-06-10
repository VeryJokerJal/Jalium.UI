#define JALIUM_MEDIA_EXPORTS
#include "jalium_media.h"
#include "jalium_media_internal.h"

extern "C" {

JALIUM_MEDIA_API jalium_media_status_t jalium_media_initialize(void)
{
    return JALIUM_MEDIA_OK;
}

JALIUM_MEDIA_API void jalium_media_shutdown(void)
{
}

JALIUM_MEDIA_API uint32_t jalium_media_supported_video_codecs(void)
{
    return 0;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_memory(
    const uint8_t* data,
    size_t size,
    jalium_pixel_format_t requested_format,
    jalium_image_t* out_image)
{
    (void)data;
    (void)size;
    (void)requested_format;
    (void)out_image;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_file(
    const char* utf8_path,
    jalium_pixel_format_t requested_format,
    jalium_image_t* out_image)
{
    (void)utf8_path;
    (void)requested_format;
    (void)out_image;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_read_dimensions(
    const uint8_t* data,
    size_t size,
    uint32_t* out_width,
    uint32_t* out_height)
{
    (void)data;
    (void)size;
    (void)out_width;
    (void)out_height;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_read_frame_count(
    const uint8_t* data,
    size_t size,
    uint32_t* out_frame_count)
{
    (void)data;
    (void)size;
    (void)out_frame_count;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_frame(
    const uint8_t* data,
    size_t size,
    uint32_t frame_index,
    jalium_pixel_format_t requested_format,
    jalium_image_t* out_image,
    uint32_t* out_delay_ms)
{
    (void)data;
    (void)size;
    (void)frame_index;
    (void)requested_format;
    (void)out_image;
    (void)out_delay_ms;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
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

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_open_file(
    const char* utf8_path,
    jalium_pixel_format_t requested_format,
    jalium_video_decoder_t** out_decoder)
{
    (void)utf8_path;
    (void)requested_format;
    if (out_decoder) *out_decoder = nullptr;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_get_info(
    jalium_video_decoder_t* decoder,
    jalium_video_info_t* out_info)
{
    (void)decoder;
    (void)out_info;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_read_frame(
    jalium_video_decoder_t* decoder,
    jalium_video_frame_t* out_frame)
{
    (void)decoder;
    (void)out_frame;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_seek_microseconds(
    jalium_video_decoder_t* decoder,
    int64_t pts_microseconds)
{
    (void)decoder;
    (void)pts_microseconds;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API void jalium_video_decoder_close(jalium_video_decoder_t* decoder)
{
    (void)decoder;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_acquire_gpu_surface_descriptor(
    jalium_video_decoder_t* decoder,
    jalium_video_decoder_gpu_descriptor_t* out_descriptor)
{
    (void)decoder;
    if (out_descriptor)
    {
        out_descriptor->kind = 0;
        out_descriptor->width = 0;
        out_descriptor->height = 0;
        out_descriptor->handle0 = 0;
        out_descriptor->handle1 = 0;
        out_descriptor->format_hint = 0;
        out_descriptor->reserved = 0;
    }
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_enumerate(
    jalium_camera_device_t** out_devices,
    uint32_t* out_count)
{
    if (out_devices) *out_devices = nullptr;
    if (out_count) *out_count = 0;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API void jalium_camera_devices_free(
    jalium_camera_device_t* devices,
    uint32_t count)
{
    (void)devices;
    (void)count;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_open(
    const char* device_id,
    uint32_t requested_width,
    uint32_t requested_height,
    double requested_fps,
    jalium_pixel_format_t requested_format,
    jalium_camera_source_t** out_source)
{
    (void)device_id;
    (void)requested_width;
    (void)requested_height;
    (void)requested_fps;
    (void)requested_format;
    if (out_source) *out_source = nullptr;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_read_frame(
    jalium_camera_source_t* source,
    jalium_video_frame_t* out_frame)
{
    (void)source;
    (void)out_frame;
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
}

JALIUM_MEDIA_API void jalium_camera_close(jalium_camera_source_t* source)
{
    (void)source;
}

}
