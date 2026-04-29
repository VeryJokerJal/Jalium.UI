#pragma once

#include "jalium_media.h"

namespace jalium::media::android {

jalium_media_status_t CameraEnumerate(
    jalium_camera_device_t** out_devices,
    uint32_t*                out_count);

void CameraDevicesFree(jalium_camera_device_t* devices, uint32_t count);

jalium_media_status_t CameraOpen(
    const char*              device_id,
    uint32_t                 requested_width,
    uint32_t                 requested_height,
    double                   requested_fps,
    jalium_pixel_format_t    requested_format,
    jalium_camera_source_t** out_source);

jalium_media_status_t CameraReadFrame(
    jalium_camera_source_t* source,
    jalium_video_frame_t*   out_frame);

void CameraClose(jalium_camera_source_t* source);

} // namespace jalium::media::android
