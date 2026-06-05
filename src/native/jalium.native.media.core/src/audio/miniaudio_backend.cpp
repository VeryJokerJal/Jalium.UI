// miniaudio single-header IMPLEMENTATION TU + the lock-free ring + the
// data callback that drives playback.
//
// This is the only translation unit that defines MINIAUDIO_IMPLEMENTATION, so
// it is the only one that materialises miniaudio's full code (~4 MB of C).
// Everything else in src/audio/ just calls the small wrapper functions
// declared in audio_internal.h.
//
// Threading:
//   - DeviceOpen / Start / Stop / Submit / SignalEndOfStream / Flush /
//     SetVolume / PlayedFrames / Close are called by the AudioPlayer pump
//     thread (or whatever managed thread owns the device). All synchronisation
//     against the audio callback goes through atomic flags and the SPSC ring.
//   - The miniaudio data callback (OnAudioCallback) runs on the OS audio
//     thread. It is the consumer of the ring; submit is the producer.
//   - The PlaybackEnded callback fires from the audio thread when (a)
//     SignalEndOfStream was set, and (b) the ring drained to zero in a
//     callback. A CAS on ended_fired guarantees at-most-once delivery per
//     (signal,drain) cycle; the next submit clears both flags.

// Enable only the backends we actually want. Everything else is opt-out so
// miniaudio's compile time and binary footprint stay reasonable.
#define MA_NO_DECODING            // We do our own codec decode (dr_wav etc.).
#define MA_NO_ENCODING
#define MA_NO_GENERATION
#define MA_NO_RESOURCE_MANAGER
#define MA_NO_NODE_GRAPH
#define MA_NO_ENGINE
#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include "jalium_audio.h"
#include "audio_internal.h"

#include <atomic>
#include <cstring>
#include <new>

namespace jalium::audio {

struct audio_device_impl {
    ma_device device;
    ma_pcm_rb ring;
    bool      ring_initialized;
    bool      device_initialized;

    uint32_t  sample_rate;
    uint32_t  channels;
    uint32_t  ring_capacity_frames;

    std::atomic<uint64_t> played_frames;
    std::atomic<bool>     eos_signaled;
    std::atomic<bool>     ended_fired;

    jalium_audio_playback_ended_fn ended_fn;
    void*                          ended_user_data;
};

namespace {

constexpr uint32_t kDefaultRingCapacityFrames = 8192u;

void OnAudioCallback(ma_device* dev, void* output, const void* /*input*/, ma_uint32 frameCount) noexcept
{
    auto* impl = static_cast<audio_device_impl*>(dev->pUserData);
    if (!impl || !output || frameCount == 0) return;

    auto* outf = static_cast<float*>(output);
    const uint32_t channels = impl->channels;
    if (channels == 0) {
        std::memset(outf, 0, static_cast<size_t>(frameCount) * sizeof(float));
        return;
    }

    ma_uint32 totalRead = 0;
    while (totalRead < frameCount) {
        ma_uint32 want = frameCount - totalRead;
        void* readPtr = nullptr;
        ma_uint32 framesToRead = want;
        if (ma_pcm_rb_acquire_read(&impl->ring, &framesToRead, &readPtr) != MA_SUCCESS) {
            break;
        }
        if (framesToRead == 0) {
            break;
        }
        std::memcpy(outf + static_cast<size_t>(totalRead) * channels,
                    readPtr,
                    static_cast<size_t>(framesToRead) * channels * sizeof(float));
        ma_pcm_rb_commit_read(&impl->ring, framesToRead);
        totalRead += framesToRead;
    }

    if (totalRead < frameCount) {
        // Underrun: zero-fill the tail and bump telemetry.
        const ma_uint32 missing = frameCount - totalRead;
        std::memset(outf + static_cast<size_t>(totalRead) * channels,
                    0,
                    static_cast<size_t>(missing) * channels * sizeof(float));
        StatsRingUnderrun();

        // EOS handling: only fire once per (signal,drain) cycle. If the caller
        // declared EOS, and the ring genuinely could not give us any frames,
        // raise the playback-ended notification.
        if (impl->eos_signaled.load(std::memory_order_acquire)) {
            bool expected = false;
            if (impl->ended_fired.compare_exchange_strong(
                    expected, true,
                    std::memory_order_acq_rel,
                    std::memory_order_acquire)) {
                if (impl->ended_fn) {
                    impl->ended_fn(impl->ended_user_data);
                }
            }
        }
    }

    if (totalRead > 0) {
        impl->played_frames.fetch_add(totalRead, std::memory_order_relaxed);
        StatsFramesPlayed(totalRead);
    }
}

} // namespace

// ---------------------------------------------------------------------------
// Backend contract (declared in audio_internal.h)
// ---------------------------------------------------------------------------

audio_device_impl* DeviceOpen(const jalium_audio_device_config_t& config,
                              jalium_media_status_t& outStatus) noexcept
{
    outStatus = JALIUM_MEDIA_OK;

    if (config.sample_rate == 0 || config.channels == 0 ||
        config.sample_format != JALIUM_ASF_F32) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }

    auto* impl = new (std::nothrow) audio_device_impl{};
    if (!impl) {
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }

    impl->sample_rate          = config.sample_rate;
    impl->channels             = config.channels;
    impl->ring_capacity_frames = config.ring_capacity_frames > 0
                                     ? config.ring_capacity_frames
                                     : kDefaultRingCapacityFrames;
    impl->played_frames.store(0, std::memory_order_relaxed);
    impl->eos_signaled.store(false, std::memory_order_relaxed);
    impl->ended_fired.store(true, std::memory_order_relaxed); // armed only after eos_signaled
    impl->ended_fn        = config.on_playback_ended;
    impl->ended_user_data = config.ended_user_data;

    ma_result rc = ma_pcm_rb_init(ma_format_f32,
                                  impl->channels,
                                  impl->ring_capacity_frames,
                                  /*pOptionalPreallocatedBuffer*/ nullptr,
                                  /*pAllocationCallbacks*/ nullptr,
                                  &impl->ring);
    if (rc != MA_SUCCESS) {
        delete impl;
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    impl->ring_initialized = true;

    ma_device_config devCfg = ma_device_config_init(ma_device_type_playback);
    devCfg.playback.format   = ma_format_f32;
    devCfg.playback.channels = impl->channels;
    devCfg.sampleRate        = impl->sample_rate;
    devCfg.dataCallback      = &OnAudioCallback;
    devCfg.pUserData         = impl;

    rc = ma_device_init(/*context*/ nullptr, &devCfg, &impl->device);
    if (rc != MA_SUCCESS) {
        ma_pcm_rb_uninit(&impl->ring);
        delete impl;
        outStatus = (rc == MA_NO_DEVICE) ? JALIUM_MEDIA_E_NO_DEVICE : JALIUM_MEDIA_E_PLATFORM;
        return nullptr;
    }
    impl->device_initialized = true;

    StatsDeviceOpened();
    return impl;
}

jalium_media_status_t DeviceStart(audio_device_impl* device) noexcept
{
    if (!device || !device->device_initialized) return JALIUM_MEDIA_E_INVALID_ARG;
    if (ma_device_is_started(&device->device)) return JALIUM_MEDIA_OK;
    return ma_device_start(&device->device) == MA_SUCCESS
        ? JALIUM_MEDIA_OK
        : JALIUM_MEDIA_E_PLATFORM;
}

jalium_media_status_t DeviceStop(audio_device_impl* device) noexcept
{
    if (!device || !device->device_initialized) return JALIUM_MEDIA_E_INVALID_ARG;
    if (!ma_device_is_started(&device->device)) return JALIUM_MEDIA_OK;
    return ma_device_stop(&device->device) == MA_SUCCESS
        ? JALIUM_MEDIA_OK
        : JALIUM_MEDIA_E_PLATFORM;
}

jalium_media_status_t DeviceSubmit(audio_device_impl* device,
                                   const float* pcm,
                                   uint32_t frameCount,
                                   uint32_t& outFramesWritten) noexcept
{
    outFramesWritten = 0;
    if (!device || !device->ring_initialized) return JALIUM_MEDIA_E_INVALID_ARG;
    if (!pcm || frameCount == 0) return JALIUM_MEDIA_OK;

    // Any new data invalidates a prior EOS declaration.
    device->eos_signaled.store(false, std::memory_order_release);
    device->ended_fired.store(true, std::memory_order_release); // disarm

    const uint32_t channels = device->channels;
    uint32_t written = 0;
    bool overflowed = false;
    while (written < frameCount) {
        ma_uint32 want = frameCount - written;
        void* writePtr = nullptr;
        ma_uint32 framesToWrite = want;
        if (ma_pcm_rb_acquire_write(&device->ring, &framesToWrite, &writePtr) != MA_SUCCESS) {
            break;
        }
        if (framesToWrite == 0) {
            overflowed = true;
            break;
        }
        std::memcpy(writePtr,
                    pcm + static_cast<size_t>(written) * channels,
                    static_cast<size_t>(framesToWrite) * channels * sizeof(float));
        ma_pcm_rb_commit_write(&device->ring, framesToWrite);
        written += framesToWrite;
    }

    outFramesWritten = written;
    if (written > 0) {
        StatsFramesSubmitted(written);
    }
    if (overflowed || written < frameCount) {
        StatsRingOverflow();
    }
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t DeviceSignalEndOfStream(audio_device_impl* device) noexcept
{
    if (!device) return JALIUM_MEDIA_E_INVALID_ARG;
    device->ended_fired.store(false, std::memory_order_release); // arm
    device->eos_signaled.store(true,  std::memory_order_release);
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t DeviceFlush(audio_device_impl* device) noexcept
{
    if (!device || !device->ring_initialized) return JALIUM_MEDIA_E_INVALID_ARG;
    ma_pcm_rb_reset(&device->ring);
    // Reset disarms EOS so the next submit can re-arm it cleanly.
    device->eos_signaled.store(false, std::memory_order_release);
    device->ended_fired.store(true,  std::memory_order_release);
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t DeviceSetVolume(audio_device_impl* device, float v) noexcept
{
    if (!device || !device->device_initialized) return JALIUM_MEDIA_E_INVALID_ARG;
    if (v < 0.0f) v = 0.0f;
    return ma_device_set_master_volume(&device->device, v) == MA_SUCCESS
        ? JALIUM_MEDIA_OK
        : JALIUM_MEDIA_E_PLATFORM;
}

uint64_t DevicePlayedFrames(audio_device_impl* device) noexcept
{
    if (!device) return 0;
    return device->played_frames.load(std::memory_order_relaxed);
}

void DeviceClose(audio_device_impl* device) noexcept
{
    if (!device) return;
    if (device->device_initialized) {
        ma_device_uninit(&device->device);
        device->device_initialized = false;
    }
    if (device->ring_initialized) {
        ma_pcm_rb_uninit(&device->ring);
        device->ring_initialized = false;
    }
    StatsDeviceClosed();
    delete device;
}

} // namespace jalium::audio
