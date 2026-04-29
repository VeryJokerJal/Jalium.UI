#pragma once

#include "jalium_media.h"

namespace jalium::media::android {

jalium_media_status_t VideoDecoderOpenFile(
    const char*              utf8_path,
    jalium_pixel_format_t    requested_format,
    jalium_video_decoder_t** out_decoder);

jalium_media_status_t VideoDecoderGetInfo(
    jalium_video_decoder_t* decoder,
    jalium_video_info_t*    out_info);

jalium_media_status_t VideoDecoderReadFrame(
    jalium_video_decoder_t* decoder,
    jalium_video_frame_t*   out_frame);

jalium_media_status_t VideoDecoderSeek(
    jalium_video_decoder_t* decoder,
    int64_t                 pts_microseconds);

void VideoDecoderClose(jalium_video_decoder_t* decoder);

} // namespace jalium::media::android
