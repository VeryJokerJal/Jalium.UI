#pragma once

#include "jalium_media.h"

namespace jalium::media::win {

/// Decodes an in-memory image into BGRA8 / RGBA8 via WIC.
/// Allocates the pixel buffer in image->pixels (caller releases with jalium_image_free).
jalium_media_status_t WicDecodeMemory(
    const uint8_t*        data,
    size_t                size,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image);

/// Decodes a UTF-8 file path via WIC.
jalium_media_status_t WicDecodeFile(
    const char*           utf8_path,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image);

/// Reads dimensions only (no pixel decode) from an in-memory image.
jalium_media_status_t WicReadDimensions(
    const uint8_t* data,
    size_t         size,
    uint32_t*      out_width,
    uint32_t*      out_height);

} // namespace jalium::media::win
