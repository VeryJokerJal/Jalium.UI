// dr_wav single-header IMPLEMENTATION TU + the concrete WAV codec impl.
//
// Anything that depends on drwav internals lives in this file only; the rest
// of the audio subsystem accesses WAV functionality through WavDecoderOpen*
// declared in audio_internal.h, which returns an audio_decoder_impl* sealed
// behind the abstract base.
//
// dr_wav handles all integer / float / IEEE / ADPCM bit-depths and exposes
// drwav_read_pcm_frames_f32() that converts directly to interleaved 32-bit
// float — exactly the output format the rest of our pipeline expects.

#define DR_WAV_IMPLEMENTATION
#define DR_WAV_NO_STDIO_STRINGSTREAM
#include "dr_wav.h"

#include "jalium_audio.h"
#include "audio_internal.h"

#include <algorithm>
#include <cstring>
#include <new>

namespace jalium::audio {

namespace {

struct WavDecoderImpl final : audio_decoder_impl {
    drwav wav{};
    bool  initialized = false;

    ~WavDecoderImpl() override
    {
        if (initialized) {
            drwav_uninit(&wav);
            initialized = false;
        }
    }

    void PopulateMetadata() noexcept
    {
        codec       = JALIUM_ACODEC_WAV;
        sample_rate = wav.sampleRate;
        channels    = wav.channels;
        if (wav.sampleRate > 0) {
            // totalPCMFrameCount * 1e6 / sampleRate, computed carefully against overflow.
            const drwav_uint64 frames = wav.totalPCMFrameCount;
            duration_us = static_cast<int64_t>(
                (frames * 1'000'000ULL + (wav.sampleRate / 2u)) / wav.sampleRate);
        } else {
            duration_us = 0;
        }
    }

    uint32_t ReadFramesImpl(float* dst, uint32_t frame_capacity) noexcept override
    {
        if (!initialized || !dst || frame_capacity == 0 || channels == 0) return 0;
        const drwav_uint64 got = drwav_read_pcm_frames_f32(&wav, frame_capacity, dst);
        if (got > 0) StatsDecoderFrames(JALIUM_ACODEC_WAV, got);
        return static_cast<uint32_t>(got);
    }

    jalium_media_status_t SeekImpl(int64_t position_us) noexcept override
    {
        if (!initialized || channels == 0 || sample_rate == 0) return JALIUM_MEDIA_E_INVALID_ARG;
        if (position_us < 0) position_us = 0;
        // Convert microseconds -> frame index, clamp to total frames.
        drwav_uint64 frame =
            static_cast<drwav_uint64>(position_us) * sample_rate / 1'000'000ULL;
        if (wav.totalPCMFrameCount > 0 && frame > wav.totalPCMFrameCount) {
            frame = wav.totalPCMFrameCount;
        }
        return drwav_seek_to_pcm_frame(&wav, frame) == DRWAV_TRUE
            ? JALIUM_MEDIA_OK
            : JALIUM_MEDIA_E_DECODE_FAILED;
    }
};

} // namespace

audio_decoder_impl* WavDecoderOpenFile(const char* utf8Path,
                                       jalium_media_status_t& outStatus) noexcept
{
    outStatus = JALIUM_MEDIA_OK;
    if (!utf8Path) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    auto* impl = new (std::nothrow) WavDecoderImpl();
    if (!impl) {
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    if (!drwav_init_file(&impl->wav, utf8Path, nullptr)) {
        delete impl;
        outStatus = JALIUM_MEDIA_E_IO;
        return nullptr;
    }
    impl->initialized = true;
    impl->PopulateMetadata();
    StatsDecoderOpened(JALIUM_ACODEC_WAV);
    return impl;
}

audio_decoder_impl* WavDecoderOpenMemory(const uint8_t* data,
                                         size_t size,
                                         jalium_media_status_t& outStatus) noexcept
{
    outStatus = JALIUM_MEDIA_OK;
    if (!data || size == 0) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    auto* impl = new (std::nothrow) WavDecoderImpl();
    if (!impl) {
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    if (!drwav_init_memory(&impl->wav, data, size, nullptr)) {
        delete impl;
        outStatus = JALIUM_MEDIA_E_DECODE_FAILED;
        return nullptr;
    }
    impl->initialized = true;
    impl->PopulateMetadata();
    StatsDecoderOpened(JALIUM_ACODEC_WAV);
    return impl;
}

} // namespace jalium::audio
