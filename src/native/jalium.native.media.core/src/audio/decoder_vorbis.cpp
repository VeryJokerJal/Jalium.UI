// stb_vorbis IMPLEMENTATION TU + concrete Ogg/Vorbis codec impl.
//
// stb_vorbis ships as a .c file that contains both the API and the
// implementation. We #include it directly (no separate header). Other TUs in
// the project never include stb_vorbis, so there is no ODR concern.

// MSVC: stb_vorbis has many implicit-conversion warnings under /W3 we don't
// want to spew. The library itself is well-tested upstream; silence locally.
#if defined(_MSC_VER)
    #pragma warning(push)
    #pragma warning(disable: 4244)  // conversion, possible loss of data
    #pragma warning(disable: 4245)  // signed/unsigned mismatch
    #pragma warning(disable: 4267)  // size_t to smaller int conversion
    #pragma warning(disable: 4456)  // declaration hides previous local
    #pragma warning(disable: 4457)  // declaration hides function parameter
    #pragma warning(disable: 4701)  // potentially uninitialized local
    #pragma warning(disable: 4996)  // deprecated POSIX/CRT names
#endif

#include "stb_vorbis.c"

#if defined(_MSC_VER)
    #pragma warning(pop)
#endif

#include "jalium_audio.h"
#include "audio_internal.h"

#include <limits>
#include <new>

namespace jalium::audio {

namespace {

struct VorbisDecoderImpl final : audio_decoder_impl {
    stb_vorbis* vorb = nullptr;

    ~VorbisDecoderImpl() override
    {
        if (vorb) {
            stb_vorbis_close(vorb);
            vorb = nullptr;
        }
    }

    void PopulateMetadata() noexcept
    {
        codec = JALIUM_ACODEC_OGG;
        const stb_vorbis_info info = stb_vorbis_get_info(vorb);
        sample_rate = info.sample_rate;
        channels    = static_cast<uint32_t>(info.channels);
        const unsigned int totalFrames = stb_vorbis_stream_length_in_samples(vorb);
        if (sample_rate > 0) {
            duration_us = static_cast<int64_t>(
                (static_cast<uint64_t>(totalFrames) * 1'000'000ULL +
                 (sample_rate / 2u)) / sample_rate);
        } else {
            duration_us = 0;
        }
    }

    uint32_t ReadFramesImpl(float* dst, uint32_t frame_capacity) noexcept override
    {
        if (!vorb || !dst || frame_capacity == 0 || channels == 0) return 0;
        // stb_vorbis_get_samples_float_interleaved returns frames written.
        const int got = stb_vorbis_get_samples_float_interleaved(
            vorb,
            static_cast<int>(channels),
            dst,
            static_cast<int>(frame_capacity * channels));
        if (got <= 0) return 0;
        StatsDecoderFrames(JALIUM_ACODEC_OGG, static_cast<uint64_t>(got));
        return static_cast<uint32_t>(got);
    }

    jalium_media_status_t SeekImpl(int64_t position_us) noexcept override
    {
        if (!vorb || channels == 0 || sample_rate == 0) return JALIUM_MEDIA_E_INVALID_ARG;
        if (position_us < 0) position_us = 0;
        uint64_t frame =
            static_cast<uint64_t>(position_us) * sample_rate / 1'000'000ULL;
        if (frame > std::numeric_limits<unsigned int>::max()) {
            frame = std::numeric_limits<unsigned int>::max();
        }
        return stb_vorbis_seek(vorb, static_cast<unsigned int>(frame)) != 0
            ? JALIUM_MEDIA_OK
            : JALIUM_MEDIA_E_DECODE_FAILED;
    }
};

} // namespace

audio_decoder_impl* VorbisDecoderOpenFile(const char* utf8Path,
                                          jalium_media_status_t& outStatus) noexcept
{
    outStatus = JALIUM_MEDIA_OK;
    if (!utf8Path) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    auto* impl = new (std::nothrow) VorbisDecoderImpl();
    if (!impl) {
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    int err = 0;
    impl->vorb = stb_vorbis_open_filename(utf8Path, &err, nullptr);
    if (!impl->vorb) {
        delete impl;
        outStatus = JALIUM_MEDIA_E_DECODE_FAILED;
        return nullptr;
    }
    impl->PopulateMetadata();
    StatsDecoderOpened(JALIUM_ACODEC_OGG);
    return impl;
}

audio_decoder_impl* VorbisDecoderOpenMemory(const uint8_t* data,
                                            size_t size,
                                            jalium_media_status_t& outStatus) noexcept
{
    outStatus = JALIUM_MEDIA_OK;
    if (!data || size == 0) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    if (size > static_cast<size_t>(std::numeric_limits<int>::max())) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    auto* impl = new (std::nothrow) VorbisDecoderImpl();
    if (!impl) {
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    int err = 0;
    impl->vorb = stb_vorbis_open_memory(data, static_cast<int>(size), &err, nullptr);
    if (!impl->vorb) {
        delete impl;
        outStatus = JALIUM_MEDIA_E_DECODE_FAILED;
        return nullptr;
    }
    impl->PopulateMetadata();
    StatsDecoderOpened(JALIUM_ACODEC_OGG);
    return impl;
}

} // namespace jalium::audio
