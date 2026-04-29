#pragma once

#include "jalium_media.h"

namespace jalium::media::win {

/// Opens a video file path via Media Foundation IMFSourceReader.
jalium_media_status_t MfVideoDecoderOpenFile(
    const char*              utf8_path,
    jalium_pixel_format_t    requested_format,
    jalium_video_decoder_t** out_decoder);

jalium_media_status_t MfVideoDecoderGetInfo(
    jalium_video_decoder_t* decoder,
    jalium_video_info_t*    out_info);

jalium_media_status_t MfVideoDecoderReadFrame(
    jalium_video_decoder_t* decoder,
    jalium_video_frame_t*   out_frame);

jalium_media_status_t MfVideoDecoderSeek(
    jalium_video_decoder_t* decoder,
    int64_t                 pts_microseconds);

void MfVideoDecoderClose(jalium_video_decoder_t* decoder);

} // namespace jalium::media::win
