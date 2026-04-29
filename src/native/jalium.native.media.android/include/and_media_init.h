#pragma once

#include "jalium_media.h"

namespace jalium::media::android {

/// Refcounted init: caches API level and JNI globals (BitmapFactory class refs etc.).
jalium_media_status_t Initialize();

void Shutdown();

bool IsInitialized();

/// Bitfield of jalium_video_codec_t enum values discovered via MediaCodecList.
uint32_t GetSupportedVideoCodecs();

/// Cached return of android_get_device_api_level (only valid after Initialize).
int GetApiLevel();

/// Probes android.media.MediaCodecList via JNI. Implemented in and_codec_caps.cpp.
uint32_t ProbeSupportedCodecsViaJni();

} // namespace jalium::media::android
