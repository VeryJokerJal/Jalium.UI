#pragma once

#include "jalium_media.h"

namespace jalium::media::android {

/// Decodes an in-memory image. Routes to AImageDecoder on API 30+ or to the
/// JNI BitmapFactory fallback on API 24-29.
jalium_media_status_t DecodeImageMemory(
    const uint8_t*        data,
    size_t                size,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image);

jalium_media_status_t DecodeImageFile(
    const char*           utf8_path,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image);

jalium_media_status_t ReadImageDimensions(
    const uint8_t* data,
    size_t         size,
    uint32_t*      out_width,
    uint32_t*      out_height);

/// Implemented in and_jni_image_fallback.cpp. Used internally for API < 30.
jalium_media_status_t DecodeImageMemoryViaJni(
    const uint8_t*        data,
    size_t                size,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image);

} // namespace jalium::media::android
