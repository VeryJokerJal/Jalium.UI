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
// Runtime capability and stream discovery (Linux GStreamer backend)
// ============================================================================

typedef enum jalium_linux_media_capability {
    JALIUM_LINUX_MEDIA_GSTREAMER_RUNTIME = 1u << 0,
    JALIUM_LINUX_MEDIA_VIDEO_CPU_FRAMES  = 1u << 1,
    JALIUM_LINUX_MEDIA_AUDIO_DECODE      = 1u << 2,
    JALIUM_LINUX_MEDIA_CAMERA_CAPTURE    = 1u << 3,
    JALIUM_LINUX_MEDIA_MIC_CAPTURE       = 1u << 4,
    JALIUM_LINUX_MEDIA_TRACK_DISCOVERY   = 1u << 5,
    JALIUM_LINUX_MEDIA_SUBTITLE_DECODE   = 1u << 6,
    // Reported only after this process has actually negotiated and exported a
    // single-plane packed RGB dma-buf layout accepted by the Vulkan importer.
    // Plugin presence alone is insufficient. NV12/P010, DMA_DRM and
    // multi-plane layouts currently fall back to CPU frames.
    JALIUM_LINUX_MEDIA_DMABUF_EXPORT     = 1u << 7,
} jalium_linux_media_capability_t;

/// Returns the capabilities that are actually usable in this process. An
/// installed build can therefore distinguish "GStreamer headers were present
/// at build time" from "the runtime and required plugins loaded successfully".
JALIUM_MEDIA_API uint32_t jalium_linux_media_capabilities(void);

typedef enum jalium_media_track_kind {
    JALIUM_MEDIA_TRACK_AUDIO    = 1,
    JALIUM_MEDIA_TRACK_SUBTITLE = 2,
} jalium_media_track_kind_t;

typedef struct jalium_media_track_info {
    jalium_media_track_kind_t kind;
    uint32_t                  index;       ///< zero-based within its kind
    const char*               id;          ///< UTF-8 stream id; may be empty
    const char*               label;       ///< UTF-8 human-readable title
    const char*               language;    ///< BCP-47/ISO language tag or empty
    const char*               codec;       ///< GStreamer caps media type
    uint32_t                  channels;     ///< audio only; otherwise zero
    uint32_t                  sample_rate;  ///< audio only; otherwise zero
    int32_t                   is_default;
    int32_t                   is_forced;
} jalium_media_track_info_t;

JALIUM_MEDIA_API jalium_media_status_t jalium_media_discover_tracks(
    const char*                 utf8_path_or_uri,
    jalium_media_track_info_t** out_tracks,
    uint32_t*                   out_count);

JALIUM_MEDIA_API void jalium_media_tracks_free(
    jalium_media_track_info_t* tracks,
    uint32_t                  count);

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

/// Reads the frame count of an in-memory image. Animated formats (GIF, APNG,
/// animated WebP) return >1; static images return 1. Returns 0 only on failure.
JALIUM_MEDIA_API jalium_media_status_t jalium_image_read_frame_count(
    const uint8_t* data,
    size_t         size,
    uint32_t*      out_frame_count);

/// Decodes a single frame from a multi-frame in-memory image. <c>frame_index</c>
/// must be in <c>[0, frame_count)</c>. <c>out_delay_ms</c> receives the frame
/// delay (centiseconds-resolution in source files; converted to milliseconds
/// here). For static formats / frames without a delay tag, <c>*out_delay_ms = 0</c>.
JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_frame(
    const uint8_t*        data,
    size_t                size,
    uint32_t              frame_index,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image,
    uint32_t*             out_delay_ms);

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

// ----------------------------------------------------------------------------
// GPU surface acquisition (stage 3 path — hardware decoder direct GPU output).
//
// When the decoder backend supports hardware decode + GPU-resident output
// (Windows MF DXVA -> ID3D11Texture2D shared via NT handle; Android MediaCodec
// -> AHardwareBuffer; Apple VTDecompressionSession -> CVPixelBuffer / IOSurface;
// Linux VAAPI -> dma-buf), this entry point returns the current frame as a
// platform handle the render backend can import via
// jalium_video_surface_wrap_external.
//
// Returns JALIUM_MEDIA_OK + filled descriptor on success;
// JALIUM_MEDIA_E_NOT_IMPLEMENTED when the active decoder hasn't implemented
// the GPU path (caller falls back to read_frame BGRA path).
//
// `out_descriptor->kind` matches JaliumVideoSurfaceKind in jalium_video_surface.h.
// `handle0` / `handle1` interpretation depends on `kind`.
// The Linux descriptor ABI can describe up to four planes, but the current
// Vulkan consumer accepts only one packed RGB AR24/XR24/AB24/XB24 plane.
// NV12/P010, DMA_DRM and multi-plane samples return an unsupported GPU layout
// and are reopened through the precise-timestamp CPU frame path.
typedef struct jalium_video_decoder_gpu_descriptor {
    int32_t   kind;          // JaliumVideoSurfaceKind (D3D11_SHARED / AHARDWAREBUFFER / ...)
    uint32_t  width;
    uint32_t  height;
    uint64_t  handle0;       // Primary OS handle (NT HANDLE / AHardwareBuffer* / IOSurfaceID).
    uint64_t  handle1;       // Secondary handle (NT-handle owner PID / VkDeviceMemory).
    uint32_t  format_hint;   // JaliumVideoSurfaceFormat (0 = BGRA8).
    uint32_t  reserved;
    uint32_t  plane_count;   // 0 for legacy handles; 1..4 for Linux dma-buf.
    uint32_t  drm_fourcc;    // DRM_FORMAT_* fourcc (e.g. NV12 / AR24).
    uint32_t  descriptor_flags;
    uint32_t  color_space;
    struct {
        int32_t  fd;         // owned descriptor fd; close through release API.
        uint32_t stride_bytes;
        uint32_t offset_bytes;
        uint32_t reserved;
        uint64_t modifier;
        uint64_t size_bytes;
    } planes[4];
    int32_t   acquire_fence_fd; // owned sync_file fd, or -1.
    uint32_t  reserved2;
    // Optional process-local producer lifetime callbacks. The descriptor owns
    // one context reference until release_gpu_surface_descriptor. A renderer
    // that imports the surface invokes retain(context) and later invokes
    // release(context) after its submission fence, preventing decoder-pool
    // reuse while the GPU still samples the dma-buf.
    uint64_t  lifetime_context;
    uint64_t  lifetime_retain_callback;
    uint64_t  lifetime_release_callback;
} jalium_video_decoder_gpu_descriptor_t;

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_acquire_gpu_surface_descriptor(
    jalium_video_decoder_t*                  decoder,
    jalium_video_decoder_gpu_descriptor_t*   out_descriptor);

/// Pulls the next hardware-decoded GPU frame. Unlike the legacy acquire call,
/// this does not require a preceding CPU read/copy. On success the descriptor
/// owns duplicated fds and must be released exactly once after the renderer
/// has imported (or rejected) it.
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_read_gpu_frame_descriptor(
    jalium_video_decoder_t*                decoder,
    jalium_video_decoder_gpu_descriptor_t* out_descriptor,
    int64_t*                               out_pts_microseconds,
    int32_t*                               out_is_keyframe);

JALIUM_MEDIA_API void jalium_video_decoder_release_gpu_surface_descriptor(
    jalium_video_decoder_gpu_descriptor_t* descriptor);

/// Switches a Linux decoder from its dma-buf pipeline to the CPU BGRA/RGBA
/// pipeline at the last decoded timestamp. Other platforms return
/// NOT_IMPLEMENTED. Used when Vulkan rejects a modifier/format at runtime.
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_disable_gpu_output(
    jalium_video_decoder_t* decoder);

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

// ============================================================================
// Microphone capture (Linux GStreamer backend)
// ============================================================================

typedef struct jalium_microphone_source jalium_microphone_source_t;

typedef struct jalium_microphone_device {
    const char* id;
    const char* friendly_name;
} jalium_microphone_device_t;

typedef struct jalium_audio_capture_frame {
    const float* samples;          ///< source-owned until next read/close
    uint32_t     frame_count;
    uint32_t     sample_rate;
    uint32_t     channels;
    int64_t      pts_microseconds;
} jalium_audio_capture_frame_t;

JALIUM_MEDIA_API jalium_media_status_t jalium_microphone_enumerate(
    jalium_microphone_device_t** out_devices,
    uint32_t*                    out_count);

JALIUM_MEDIA_API void jalium_microphone_devices_free(
    jalium_microphone_device_t* devices,
    uint32_t                    count);

JALIUM_MEDIA_API jalium_media_status_t jalium_microphone_open(
    const char*                 device_id,
    uint32_t                    requested_sample_rate,
    uint32_t                    requested_channels,
    jalium_microphone_source_t** out_source);

JALIUM_MEDIA_API jalium_media_status_t jalium_microphone_read_frame(
    jalium_microphone_source_t* source,
    jalium_audio_capture_frame_t* out_frame);

JALIUM_MEDIA_API void jalium_microphone_close(
    jalium_microphone_source_t* source);

// Linux-selected audio track decoder. The returned opaque handle follows the
// normal jalium_audio_decoder_* lifetime/read/seek ABI from jalium_audio.h.
struct jalium_audio_decoder;
JALIUM_MEDIA_API jalium_media_status_t jalium_linux_audio_decoder_open_track(
    const char*                    utf8_path_or_uri,
    uint32_t                       track_index,
    struct jalium_audio_decoder**  out_decoder);

typedef struct jalium_subtitle_decoder jalium_subtitle_decoder_t;

typedef struct jalium_subtitle_cue {
    const char* utf8_text;          ///< decoder-owned until next read/close
    int64_t     start_microseconds;
    int64_t     duration_microseconds;
} jalium_subtitle_cue_t;

JALIUM_MEDIA_API jalium_media_status_t jalium_subtitle_decoder_open(
    const char*                 utf8_path_or_uri,
    uint32_t                    track_index,
    jalium_subtitle_decoder_t** out_decoder);

JALIUM_MEDIA_API jalium_media_status_t jalium_subtitle_decoder_read_cue(
    jalium_subtitle_decoder_t* decoder,
    jalium_subtitle_cue_t*     out_cue);

JALIUM_MEDIA_API jalium_media_status_t jalium_subtitle_decoder_seek_us(
    jalium_subtitle_decoder_t* decoder,
    int64_t                    position_us);

JALIUM_MEDIA_API void jalium_subtitle_decoder_close(
    jalium_subtitle_decoder_t* decoder);

#ifdef __cplusplus
}
#endif
