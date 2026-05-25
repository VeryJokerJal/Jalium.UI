// Internal header shared by the audio TUs in jalium.native.media.core.
// NOT part of the public ABI (jalium_audio.h is).
//
// Splits responsibilities so each single-header library has exactly one
// IMPLEMENTATION TU and stays out of the others' compilation:
//   - miniaudio_backend.cpp -> MINIAUDIO_IMPLEMENTATION + audio_device_impl
//   - decoder_wav.cpp       -> DR_WAV_IMPLEMENTATION + WAV decoder impl
//   - audio_device.cpp      -> jalium_audio_device_* ABI -> audio_device_impl
//   - audio_decoder.cpp     -> jalium_audio_decoder_* ABI -> codec dispatch
//   - audio_init.cpp        -> process-wide init/shutdown refcount
//   - audio_stats.cpp       -> atomic counters + query/reset
//
// Headers vendored under src/native/thirdparty/{miniaudio,dr_libs}/ are
// included only from their respective IMPLEMENTATION TUs, never from this
// internal header, so the rest of the audio code compiles without dragging
// in 4 MB of single-header code.

#pragma once

#include "jalium_audio.h"

#include <atomic>
#include <cstdint>
#include <vector>

namespace jalium::audio {

// ---------------------------------------------------------------------------
// Init refcount
// ---------------------------------------------------------------------------

/// Process-wide init refcount. Each successful call to jalium_audio_initialize
/// increments; each jalium_audio_shutdown decrements. The library treats a
/// nonzero refcount as "initialized" and exposes IsInitialized() for the
/// other TUs to gate sensitive paths.
bool IsInitialized() noexcept;

// ---------------------------------------------------------------------------
// Backend device contract
// ---------------------------------------------------------------------------
//
// Defined in miniaudio_backend.cpp. The ABI layer (audio_device.cpp) only
// sees opaque pointers cast to jalium_audio_device_t*.

struct audio_device_impl;

audio_device_impl* DeviceOpen(const jalium_audio_device_config_t& config,
                              jalium_media_status_t& outStatus) noexcept;
jalium_media_status_t DeviceStart(audio_device_impl* device) noexcept;
jalium_media_status_t DeviceStop(audio_device_impl* device) noexcept;
jalium_media_status_t DeviceSubmit(audio_device_impl* device,
                                   const float* pcm,
                                   uint32_t frameCount,
                                   uint32_t& outFramesWritten) noexcept;
jalium_media_status_t DeviceSignalEndOfStream(audio_device_impl* device) noexcept;
jalium_media_status_t DeviceFlush(audio_device_impl* device) noexcept;
jalium_media_status_t DeviceSetVolume(audio_device_impl* device, float v) noexcept;
uint64_t              DevicePlayedFrames(audio_device_impl* device) noexcept;
void                  DeviceClose(audio_device_impl* device) noexcept;

// ---------------------------------------------------------------------------
// Decoder contract
// ---------------------------------------------------------------------------
//
// One audio_decoder_impl per open file. The struct is an abstract base; the
// concrete implementations (WavDecoderImpl in decoder_wav.cpp, etc.) live in
// their respective codec TUs and stay opaque outside of them.

struct audio_decoder_impl {
    jalium_audio_codec_t codec        = JALIUM_ACODEC_AUTO;
    uint32_t             sample_rate  = 0;
    uint32_t             channels     = 0;
    int64_t              duration_us  = 0;

    /// File-via-memory path: when the decoder was opened from a file path on
    /// a platform whose libc fopen() does not understand UTF-8 (notably
    /// Windows, which uses the active ANSI codepage), audio_decoder.cpp
    /// pre-reads the file with a wide-char-safe call and parks the bytes
    /// here so the codec's "open from memory" path can hand a stable pointer
    /// to the underlying single-header library. The vector must outlive the
    /// codec's internal pointer; std::vector's move constructor keeps the
    /// same data() pointer, so transferring ownership here is safe even
    /// after the codec already captured the address.
    std::vector<uint8_t> owned_bytes;

    virtual ~audio_decoder_impl() = default;

    /// Decode up to `frame_capacity` interleaved float frames into `dst`. Returns
    /// the actual number of frames written; 0 means EOS. `dst` is at least
    /// `frame_capacity * channels` floats large.
    virtual uint32_t ReadFramesImpl(float* dst, uint32_t frame_capacity) noexcept = 0;

    /// Seek to `position_us` microseconds. Values past duration are clamped.
    virtual jalium_media_status_t SeekImpl(int64_t position_us) noexcept = 0;
};

audio_decoder_impl* DecoderOpenFile(const char* utf8Path,
                                    jalium_audio_codec_t hint,
                                    jalium_media_status_t& outStatus) noexcept;
audio_decoder_impl* DecoderOpenMemory(const uint8_t* data,
                                      size_t size,
                                      jalium_audio_codec_t hint,
                                      jalium_media_status_t& outStatus) noexcept;
jalium_media_status_t DecoderGetInfo(audio_decoder_impl* decoder,
                                     jalium_audio_info_t& outInfo) noexcept;
jalium_media_status_t DecoderReadFrames(audio_decoder_impl* decoder,
                                        float* dst,
                                        uint32_t frameCapacity,
                                        uint32_t& outFramesRead) noexcept;
jalium_media_status_t DecoderSeekUs(audio_decoder_impl* decoder, int64_t us) noexcept;
void                  DecoderClose(audio_decoder_impl* decoder) noexcept;

// ---------------------------------------------------------------------------
// Codec implementations
// ---------------------------------------------------------------------------
//
// Each codec exposes an OpenFile + OpenMemory pair. They return an
// audio_decoder_impl owned by the caller; release via DecoderClose. On
// failure the returned pointer is null and outStatus reflects the reason
// (IO, OOM, decode failure, ...).

audio_decoder_impl* WavDecoderOpenFile(const char* utf8Path,
                                       jalium_media_status_t& outStatus) noexcept;
audio_decoder_impl* WavDecoderOpenMemory(const uint8_t* data,
                                         size_t size,
                                         jalium_media_status_t& outStatus) noexcept;

audio_decoder_impl* FlacDecoderOpenFile(const char* utf8Path,
                                        jalium_media_status_t& outStatus) noexcept;
audio_decoder_impl* FlacDecoderOpenMemory(const uint8_t* data,
                                          size_t size,
                                          jalium_media_status_t& outStatus) noexcept;

audio_decoder_impl* Mp3DecoderOpenFile(const char* utf8Path,
                                       jalium_media_status_t& outStatus) noexcept;
audio_decoder_impl* Mp3DecoderOpenMemory(const uint8_t* data,
                                         size_t size,
                                         jalium_media_status_t& outStatus) noexcept;

audio_decoder_impl* VorbisDecoderOpenFile(const char* utf8Path,
                                          jalium_media_status_t& outStatus) noexcept;
audio_decoder_impl* VorbisDecoderOpenMemory(const uint8_t* data,
                                            size_t size,
                                            jalium_media_status_t& outStatus) noexcept;

// ---------------------------------------------------------------------------
// Platform AAC hooks
// ---------------------------------------------------------------------------
//
// .core has no platform-native AAC implementation; each per-platform library
// (jalium.native.media.{windows,android,apple,linux}) registers its own
// opener function with this hook table at init time. audio_decoder.cpp's
// AAC dispatch invokes the registered hook if present, otherwise it returns
// JALIUM_MEDIA_E_NOT_IMPLEMENTED.
//
// Either function may be nullptr (e.g. Windows can register file-open via MF
// but defer memory-open for now); a null hook routes back to NOT_IMPLEMENTED.

using AacOpenFileFn = audio_decoder_impl* (*)(const char* utf8Path,
                                              jalium_media_status_t& outStatus) noexcept;
using AacOpenMemoryFn = audio_decoder_impl* (*)(const uint8_t* data,
                                                size_t size,
                                                jalium_media_status_t& outStatus) noexcept;

void RegisterAacDecoderHooks(AacOpenFileFn openFile, AacOpenMemoryFn openMemory) noexcept;

// ---------------------------------------------------------------------------
// Telemetry hooks (called from device/decoder paths; aggregated in audio_stats.cpp)
// ---------------------------------------------------------------------------

void StatsDecoderOpened(jalium_audio_codec_t codec) noexcept;
void StatsDecoderClosed() noexcept;
void StatsDecoderFrames(jalium_audio_codec_t codec, uint64_t frames) noexcept;
void StatsDeviceOpened() noexcept;
void StatsDeviceClosed() noexcept;
void StatsFramesSubmitted(uint64_t frames) noexcept;
void StatsFramesPlayed(uint64_t frames) noexcept;
void StatsRingUnderrun() noexcept;
void StatsRingOverflow() noexcept;
void StatsCodecProbeFailed() noexcept;
void StatsPlatformAacFallback() noexcept;

} // namespace jalium::audio
