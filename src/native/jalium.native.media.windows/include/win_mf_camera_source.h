#pragma once

#include "jalium_media.h"

namespace jalium::media::win {

jalium_media_status_t MfCameraEnumerate(
    jalium_camera_device_t** out_devices,
    uint32_t*                out_count);

void MfCameraDevicesFree(jalium_camera_device_t* devices, uint32_t count);

jalium_media_status_t MfCameraOpen(
    const char*              device_id,
    uint32_t                 requested_width,
    uint32_t                 requested_height,
    double                   requested_fps,
    jalium_pixel_format_t    requested_format,
    jalium_camera_source_t** out_source);

jalium_media_status_t MfCameraReadFrame(
    jalium_camera_source_t* source,
    jalium_video_frame_t*   out_frame);

void MfCameraClose(jalium_camera_source_t* source);

} // namespace jalium::media::win
