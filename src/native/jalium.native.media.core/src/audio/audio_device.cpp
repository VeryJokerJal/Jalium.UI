// jalium_audio_device_* ABI surface. Every export is a thin trampoline that
// reinterprets the opaque jalium_audio_device_t* as an audio_device_impl* and
// dispatches into the miniaudio backend declared in audio_internal.h.

#include "jalium_audio.h"
#include "audio_internal.h"

using jalium::audio::audio_device_impl;
namespace ja = jalium::audio;

namespace {

inline audio_device_impl* AsImpl(jalium_audio_device_t* d) noexcept
{
    return reinterpret_cast<audio_device_impl*>(d);
}

inline jalium_audio_device_t* AsAbi(audio_device_impl* d) noexcept
{
    return reinterpret_cast<jalium_audio_device_t*>(d);
}

} // namespace

extern "C" {

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_device_open(
    const jalium_audio_device_config_t* config,
    jalium_audio_device_t** out_device)
{
    if (out_device) *out_device = nullptr;
    if (!config || !out_device) return JALIUM_MEDIA_E_INVALID_ARG;
    if (!ja::IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;

    jalium_media_status_t status = JALIUM_MEDIA_OK;
    audio_device_impl* impl = ja::DeviceOpen(*config, status);
    if (status != JALIUM_MEDIA_OK || !impl) {
        return status != JALIUM_MEDIA_OK ? status : JALIUM_MEDIA_E_PLATFORM;
    }
    *out_device = AsAbi(impl);
    return JALIUM_MEDIA_OK;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_device_start(jalium_audio_device_t* device)
{
    return ja::DeviceStart(AsImpl(device));
}

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_device_stop(jalium_audio_device_t* device)
{
    return ja::DeviceStop(AsImpl(device));
}

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_device_submit(
    jalium_audio_device_t* device,
    const float* pcm,
    uint32_t frame_count,
    uint32_t* out_frames_written)
{
    uint32_t written = 0;
    jalium_media_status_t rc = ja::DeviceSubmit(AsImpl(device), pcm, frame_count, written);
    if (out_frames_written) *out_frames_written = written;
    return rc;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_device_signal_end_of_stream(jalium_audio_device_t* device)
{
    return ja::DeviceSignalEndOfStream(AsImpl(device));
}

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_device_flush(jalium_audio_device_t* device)
{
    return ja::DeviceFlush(AsImpl(device));
}

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_device_set_volume(
    jalium_audio_device_t* device,
    float linear_volume)
{
    return ja::DeviceSetVolume(AsImpl(device), linear_volume);
}

JALIUM_MEDIA_API uint64_t jalium_audio_device_played_frames(jalium_audio_device_t* device)
{
    return ja::DevicePlayedFrames(AsImpl(device));
}

JALIUM_MEDIA_API void jalium_audio_device_close(jalium_audio_device_t* device)
{
    ja::DeviceClose(AsImpl(device));
}

} // extern "C"
