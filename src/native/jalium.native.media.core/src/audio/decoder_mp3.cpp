// minimp3_ex IMPLEMENTATION TU + concrete MP3 codec impl.
//
// MINIMP3_FLOAT_OUTPUT makes mp3d_sample_t a float, matching the rest of
// our audio pipeline (interleaved 32-bit float). mp3dec_ex_t handles the
// metadata scan + seek table; counts are in "samples" (channels * frames).

#define MINIMP3_FLOAT_OUTPUT
#define MINIMP3_IMPLEMENTATION
// minimp3 1.x uses x86 SSE-style `__m128 = {f,f,f,f}` brace-initializers in its
// MINIMP3_FLOAT_OUTPUT SIMD path. On ARM64 MSVC the f4 typedef becomes
// float32x4_t which does not accept brace-init (C2078). Disable SIMD on ARM64
// only; x64/x86 stay on SSE.
#if defined(_M_ARM64) || defined(__aarch64__)
#define MINIMP3_NO_SIMD
#endif
#include "minimp3_ex.h"

#include "jalium_audio.h"
#include "audio_internal.h"

#include <new>

namespace jalium::audio {

namespace {

struct Mp3DecoderImpl final : audio_decoder_impl {
    mp3dec_ex_t dec{};
    bool        initialized = false;

    ~Mp3DecoderImpl() override
    {
        if (initialized) {
            mp3dec_ex_close(&dec);
            initialized = false;
        }
    }

    void PopulateMetadata() noexcept
    {
        codec       = JALIUM_ACODEC_MP3;
        sample_rate = static_cast<uint32_t>(dec.info.hz);
        channels    = static_cast<uint32_t>(dec.info.channels);
        if (sample_rate > 0 && channels > 0) {
            // dec.samples is total interleaved samples (frames * channels).
            const uint64_t totalFrames = dec.samples / channels;
            duration_us = static_cast<int64_t>(
                (totalFrames * 1'000'000ULL + (sample_rate / 2u)) / sample_rate);
        } else {
            duration_us = 0;
        }
    }

    uint32_t ReadFramesImpl(float* dst, uint32_t frame_capacity) noexcept override
    {
        if (!initialized || !dst || frame_capacity == 0 || channels == 0) return 0;
        const size_t samplesToRead = static_cast<size_t>(frame_capacity) * channels;
        const size_t got = mp3dec_ex_read(&dec, dst, samplesToRead);
        const uint32_t framesGot = static_cast<uint32_t>(got / channels);
        if (framesGot > 0) StatsDecoderFrames(JALIUM_ACODEC_MP3, framesGot);
        return framesGot;
    }

    jalium_media_status_t SeekImpl(int64_t position_us) noexcept override
    {
        if (!initialized || channels == 0 || sample_rate == 0) return JALIUM_MEDIA_E_INVALID_ARG;
        if (position_us < 0) position_us = 0;
        uint64_t frameIndex =
            static_cast<uint64_t>(position_us) * sample_rate / 1'000'000ULL;
        const uint64_t totalFrames = dec.samples / channels;
        if (totalFrames > 0 && frameIndex > totalFrames) frameIndex = totalFrames;
        // mp3dec_ex_seek takes "interleaved sample" position.
        return mp3dec_ex_seek(&dec, frameIndex * channels) == 0
            ? JALIUM_MEDIA_OK
            : JALIUM_MEDIA_E_DECODE_FAILED;
    }
};

} // namespace

audio_decoder_impl* Mp3DecoderOpenFile(const char* utf8Path,
                                       jalium_media_status_t& outStatus) noexcept
{
    outStatus = JALIUM_MEDIA_OK;
    if (!utf8Path) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    auto* impl = new (std::nothrow) Mp3DecoderImpl();
    if (!impl) {
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    if (mp3dec_ex_open(&impl->dec, utf8Path, MP3D_SEEK_TO_SAMPLE) != 0) {
        delete impl;
        outStatus = JALIUM_MEDIA_E_DECODE_FAILED;
        return nullptr;
    }
    impl->initialized = true;
    impl->PopulateMetadata();
    StatsDecoderOpened(JALIUM_ACODEC_MP3);
    return impl;
}

audio_decoder_impl* Mp3DecoderOpenMemory(const uint8_t* data,
                                         size_t size,
                                         jalium_media_status_t& outStatus) noexcept
{
    outStatus = JALIUM_MEDIA_OK;
    if (!data || size == 0) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    auto* impl = new (std::nothrow) Mp3DecoderImpl();
    if (!impl) {
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    if (mp3dec_ex_open_buf(&impl->dec, data, size, MP3D_SEEK_TO_SAMPLE) != 0) {
        delete impl;
        outStatus = JALIUM_MEDIA_E_DECODE_FAILED;
        return nullptr;
    }
    impl->initialized = true;
    impl->PopulateMetadata();
    StatsDecoderOpened(JALIUM_ACODEC_MP3);
    return impl;
}

} // namespace jalium::audio
