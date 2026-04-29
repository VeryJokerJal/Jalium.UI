#pragma once

#include <stdint.h>
#include <stddef.h>

// Platform-specific export macros.
// JALIUM_STATIC takes precedence (NativeAOT static-link flavor); JALIUM_MEDIA_EXPORTS
// is set on the producing library; consumers see JALIUM_MEDIA_API as dllimport / visibility default.
#ifdef _WIN32
    #if defined(JALIUM_STATIC)
        #define JALIUM_MEDIA_API
    #elif defined(JALIUM_MEDIA_EXPORTS)
        #define JALIUM_MEDIA_API __declspec(dllexport)
    #else
        #define JALIUM_MEDIA_API __declspec(dllimport)
    #endif
#else
    #define JALIUM_MEDIA_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// ============================================================================
// Status codes
// ============================================================================

typedef enum jalium_media_status {
    JALIUM_MEDIA_OK                       = 0,
    JALIUM_MEDIA_E_INVALID_ARG            = 1,
    JALIUM_MEDIA_E_OUT_OF_MEMORY          = 2,
    JALIUM_MEDIA_E_IO                     = 3,
    JALIUM_MEDIA_E_UNSUPPORTED_FORMAT     = 4,
    JALIUM_MEDIA_E_UNSUPPORTED_CODEC      = 5,
    JALIUM_MEDIA_E_DECODE_FAILED          = 6,
    JALIUM_MEDIA_E_END_OF_STREAM          = 7,
    JALIUM_MEDIA_E_NOT_INITIALIZED        = 8,
    JALIUM_MEDIA_E_PLATFORM               = 9,
    JALIUM_MEDIA_E_NO_DEVICE              = 10,
    JALIUM_MEDIA_E_PERMISSION_DENIED      = 11,
    JALIUM_MEDIA_E_NOT_IMPLEMENTED        = 12,
} jalium_media_status_t;

// ============================================================================
// Pixel formats
// ============================================================================

typedef enum jalium_pixel_format {
    JALIUM_PF_BGRA8 = 0,  ///< Default; matches D3D12 swap chain
    JALIUM_PF_RGBA8 = 1,  ///< Used on Android Vulkan when BGRA8 is not supported
} jalium_pixel_format_t;

// ============================================================================
// Codec capability flags
// ============================================================================

typedef enum jalium_video_codec {
    JALIUM_CODEC_NONE = 0,
    JALIUM_CODEC_H264 = 1 << 0,
    JALIUM_CODEC_HEVC = 1 << 1,
    JALIUM_CODEC_VP9  = 1 << 2,
    JALIUM_CODEC_AV1  = 1 << 3,
} jalium_video_codec_t;

// ============================================================================
// Camera facing
// ============================================================================

typedef enum jalium_camera_facing {
    JALIUM_CAMERA_FACING_UNKNOWN  = 0,
    JALIUM_CAMERA_FACING_FRONT    = 1,
    JALIUM_CAMERA_FACING_BACK     = 2,
    JALIUM_CAMERA_FACING_EXTERNAL = 3,
} jalium_camera_facing_t;

// ============================================================================
// Lifecycle
// ============================================================================

/// Initializes the media subsystem (refcounted; safe to call multiple times).
/// On Windows: CoInitializeEx + MFStartup.
/// On Android: caches API level + JNI globals.
JALIUM_MEDIA_API jalium_media_status_t jalium_media_initialize(void);

/// Tears down one ref count taken by jalium_media_initialize.
JALIUM_MEDIA_API void jalium_media_shutdown(void);

/// Returns a static, human-readable string for a status code.
JALIUM_MEDIA_API const char* jalium_media_status_string(jalium_media_status_t status);

/// Returns a bitfield of supported video codecs (jalium_video_codec_t).
/// Only meaningful after jalium_media_initialize succeeded.
JALIUM_MEDIA_API uint32_t jalium_media_supported_video_codecs(void);

// ============================================================================
// Image decoding (callee-owned buffer)
// ============================================================================

typedef struct jalium_image {
    uint32_t              width;
    uint32_t              height;
    uint32_t              stride_bytes;
    jalium_pixel_format_t format;
    uint8_t*              pixels;       ///< owned by lib; release with jalium_image_free
    void*                 _reserved;    ///< back-pointer used by jalium_image_free
} jalium_image_t;

/// Decodes an in-memory image into BGRA8 (or RGBA8) pixels.
JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_memory(
    const uint8_t*        data,
    size_t                size,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image);

/// Decodes a file path into BGRA8 (or RGBA8) pixels.
JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_file(
    const char*           utf8_path,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image);

/// Reads only the dimensions (no pixel decode) from an in-memory image.
JALIUM_MEDIA_API jalium_media_status_t jalium_image_read_dimensions(
    const uint8_t* data,
    size_t         size,
    uint32_t*      out_width,
    uint32_t*      out_height);

/// Releases the buffer owned by a jalium_image_t.
JALIUM_MEDIA_API void jalium_image_free(jalium_image_t* image);

// ============================================================================
// Video decoding (opaque handle, decoder-owned frame buffer)
// ============================================================================

typedef struct jalium_video_decoder jalium_video_decoder_t;

typedef struct jalium_video_info {
    uint32_t             width;
    uint32_t             height;
    double               duration_seconds;   ///< 0 if unknown / live
    double               frame_rate;         ///< best-effort
    uint64_t             frame_count;        ///< 0 if unknown
    jalium_video_codec_t active_codec;       ///< Selected video codec
} jalium_video_info_t;

typedef struct jalium_video_frame {
    uint32_t              width;
    uint32_t              height;
    uint32_t              stride_bytes;
    jalium_pixel_format_t format;
    uint8_t*              pixels;            ///< owned by decoder; valid until next read_frame / close
    int64_t               pts_microseconds;
    int32_t               is_keyframe;
} jalium_video_frame_t;

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_open_file(
    const char*              utf8_path,
    jalium_pixel_format_t    requested_format,
    jalium_video_decoder_t** out_decoder);

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_get_info(
    jalium_video_decoder_t* decoder,
    jalium_video_info_t*    out_info);

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_read_frame(
    jalium_video_decoder_t* decoder,
    jalium_video_frame_t*   out_frame);

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_seek_microseconds(
    jalium_video_decoder_t* decoder,
    int64_t                 pts_microseconds);

JALIUM_MEDIA_API void jalium_video_decoder_close(jalium_video_decoder_t* decoder);

// ============================================================================
// Camera capture (opaque handle, source-owned frame buffer)
// ============================================================================

typedef struct jalium_camera_source jalium_camera_source_t;

typedef struct jalium_camera_format {
    uint32_t width;
    uint32_t height;
    double   fps;
} jalium_camera_format_t;

typedef struct jalium_camera_device {
    const char*                   id;              ///< UTF-8 stable device id
    const char*                   friendly_name;   ///< UTF-8 display name
    jalium_camera_facing_t        facing;
    uint32_t                      format_count;
    const jalium_camera_format_t* formats;         ///< owned by lib; valid until jalium_camera_devices_free
} jalium_camera_device_t;

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_enumerate(
    jalium_camera_device_t** out_devices,
    uint32_t*                out_count);

JALIUM_MEDIA_API void jalium_camera_devices_free(
    jalium_camera_device_t* devices,
    uint32_t                count);

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_open(
    const char*              device_id,
    uint32_t                 requested_width,
    uint32_t                 requested_height,
    double                   requested_fps,
    jalium_pixel_format_t    requested_format,
    jalium_camera_source_t** out_source);

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_read_frame(
    jalium_camera_source_t* source,
    jalium_video_frame_t*   out_frame);

JALIUM_MEDIA_API void jalium_camera_close(jalium_camera_source_t* source);

#ifdef __cplusplus
}
#endif
