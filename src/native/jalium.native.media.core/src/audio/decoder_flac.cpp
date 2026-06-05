// dr_flac single-header IMPLEMENTATION TU + concrete FLAC codec impl.
//
// Mirrors decoder_wav.cpp's shape; the differences are dr_flac's
// pointer-returning init APIs (vs dr_wav's in-place init) and the slightly
// different field names on the drflac struct.

#define DR_FLAC_IMPLEMENTATION
#include "dr_flac.h"

#include "jalium_audio.h"
#include "audio_internal.h"

#include <new>

namespace jalium::audio {

namespace {

struct FlacDecoderImpl final : audio_decoder_impl {
    drflac* flac = nullptr;

    ~FlacDecoderImpl() override
    {
        if (flac) {
            drflac_close(flac);
            flac = nullptr;
        }
    }

    void PopulateMetadata() noexcept
    {
        codec       = JALIUM_ACODEC_FLAC;
        sample_rate = flac->sampleRate;
        channels    = flac->channels;
        if (flac->sampleRate > 0) {
            const drflac_uint64 frames = flac->totalPCMFrameCount;
            duration_us = static_cast<int64_t>(
                (frames * 1'000'000ULL + (flac->sampleRate / 2u)) / flac->sampleRate);
        } else {
            duration_us = 0;
        }
    }

    uint32_t ReadFramesImpl(float* dst, uint32_t frame_capacity) noexcept override
    {
        if (!flac || !dst || frame_capacity == 0 || channels == 0) return 0;
        const drflac_uint64 got = drflac_read_pcm_frames_f32(flac, frame_capacity, dst);
        if (got > 0) StatsDecoderFrames(JALIUM_ACODEC_FLAC, got);
        return static_cast<uint32_t>(got);
    }

    jalium_media_status_t SeekImpl(int64_t position_us) noexcept override
    {
        if (!flac || channels == 0 || sample_rate == 0) return JALIUM_MEDIA_E_INVALID_ARG;
        if (position_us < 0) position_us = 0;
        drflac_uint64 frame =
            static_cast<drflac_uint64>(position_us) * sample_rate / 1'000'000ULL;
        if (flac->totalPCMFrameCount > 0 && frame > flac->totalPCMFrameCount) {
            frame = flac->totalPCMFrameCount;
        }
        return drflac_seek_to_pcm_frame(flac, frame) == DRFLAC_TRUE
            ? JALIUM_MEDIA_OK
            : JALIUM_MEDIA_E_DECODE_FAILED;
    }
};

} // namespace

audio_decoder_impl* FlacDecoderOpenFile(const char* utf8Path,
                                        jalium_media_status_t& outStatus) noexcept
{
    outStatus = JALIUM_MEDIA_OK;
    if (!utf8Path) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    auto* impl = new (std::nothrow) FlacDecoderImpl();
    if (!impl) {
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    impl->flac = drflac_open_file(utf8Path, nullptr);
    if (!impl->flac) {
        delete impl;
        outStatus = JALIUM_MEDIA_E_DECODE_FAILED;
        return nullptr;
    }
    impl->PopulateMetadata();
    StatsDecoderOpened(JALIUM_ACODEC_FLAC);
    return impl;
}

audio_decoder_impl* FlacDecoderOpenMemory(const uint8_t* data,
                                          size_t size,
                                          jalium_media_status_t& outStatus) noexcept
{
    outStatus = JALIUM_MEDIA_OK;
    if (!data || size == 0) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    auto* impl = new (std::nothrow) FlacDecoderImpl();
    if (!impl) {
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    impl->flac = drflac_open_memory(data, size, nullptr);
    if (!impl->flac) {
        delete impl;
        outStatus = JALIUM_MEDIA_E_DECODE_FAILED;
        return nullptr;
    }
    impl->PopulateMetadata();
    StatsDecoderOpened(JALIUM_ACODEC_FLAC);
    return impl;
}

} // namespace jalium::audio
