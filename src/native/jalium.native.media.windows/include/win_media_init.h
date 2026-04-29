#pragma once

#include "jalium_media.h"

namespace jalium::media::win {

/// Refcounted CoInitializeEx + MFStartup. Safe to call from multiple threads.
jalium_media_status_t Initialize();

/// Decrements the refcount; performs MFShutdown + CoUninitialize when it reaches zero.
void Shutdown();

/// Returns true when Initialize has been called at least once successfully.
bool IsInitialized();

/// Bitfield of jalium_video_codec_t values discovered via MFTEnumEx during Initialize.
uint32_t GetSupportedVideoCodecs();

/// Probes installed Media Foundation video decoders. Implemented in win_mf_codec_caps.cpp.
uint32_t ProbeSupportedCodecs();

} // namespace jalium::media::win
