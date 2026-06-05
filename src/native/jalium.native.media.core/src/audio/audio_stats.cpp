// Atomic counter pool for jalium_audio_stats_t plus the
// jalium_query_audio_stats / jalium_reset_audio_stats exports.
//
// Pattern mirrors jalium_bitmap_stats.cpp: lock-free counters incremented
// inline from the audio paths; the query export snapshots them into the
// caller's POD struct in a single pass.

#include "jalium_audio.h"
#include "audio_internal.h"

#include <atomic>
#include <cstring>

namespace {

struct Counters {
    std::atomic<uint64_t> decoders_opened{0};
    std::atomic<uint64_t> decoders_closed{0};
    std::atomic<uint64_t> devices_opened{0};
    std::atomic<uint64_t> devices_closed{0};
    std::atomic<uint64_t> frames_decoded_wav{0};
    std::atomic<uint64_t> frames_decoded_flac{0};
    std::atomic<uint64_t> frames_decoded_mp3{0};
    std::atomic<uint64_t> frames_decoded_ogg{0};
    std::atomic<uint64_t> frames_decoded_aac{0};
    std::atomic<uint64_t> frames_submitted{0};
    std::atomic<uint64_t> frames_played{0};
    std::atomic<uint64_t> ring_underruns{0};
    std::atomic<uint64_t> ring_overflows{0};
    std::atomic<uint64_t> codec_probe_failures{0};
    std::atomic<uint64_t> platform_aac_fallbacks{0};
};

Counters g_stats;

inline std::atomic<uint64_t>* DecoderFrameCounter(jalium_audio_codec_t codec) noexcept
{
    switch (codec) {
        case JALIUM_ACODEC_WAV:  return &g_stats.frames_decoded_wav;
        case JALIUM_ACODEC_FLAC: return &g_stats.frames_decoded_flac;
        case JALIUM_ACODEC_MP3:  return &g_stats.frames_decoded_mp3;
        case JALIUM_ACODEC_OGG:  return &g_stats.frames_decoded_ogg;
        case JALIUM_ACODEC_AAC:  return &g_stats.frames_decoded_aac;
        default:                 return nullptr;
    }
}

} // namespace

namespace jalium::audio {

void StatsDecoderOpened(jalium_audio_codec_t) noexcept { g_stats.decoders_opened.fetch_add(1, std::memory_order_relaxed); }
void StatsDecoderClosed() noexcept                     { g_stats.decoders_closed.fetch_add(1, std::memory_order_relaxed); }
void StatsDecoderFrames(jalium_audio_codec_t codec, uint64_t frames) noexcept
{
    if (frames == 0) return;
    if (auto* c = DecoderFrameCounter(codec)) c->fetch_add(frames, std::memory_order_relaxed);
}
void StatsDeviceOpened() noexcept                      { g_stats.devices_opened.fetch_add(1, std::memory_order_relaxed); }
void StatsDeviceClosed() noexcept                      { g_stats.devices_closed.fetch_add(1, std::memory_order_relaxed); }
void StatsFramesSubmitted(uint64_t frames) noexcept    { g_stats.frames_submitted.fetch_add(frames, std::memory_order_relaxed); }
void StatsFramesPlayed(uint64_t frames) noexcept       { g_stats.frames_played.fetch_add(frames, std::memory_order_relaxed); }
void StatsRingUnderrun() noexcept                      { g_stats.ring_underruns.fetch_add(1, std::memory_order_relaxed); }
void StatsRingOverflow() noexcept                      { g_stats.ring_overflows.fetch_add(1, std::memory_order_relaxed); }
void StatsCodecProbeFailed() noexcept                  { g_stats.codec_probe_failures.fetch_add(1, std::memory_order_relaxed); }
void StatsPlatformAacFallback() noexcept               { g_stats.platform_aac_fallbacks.fetch_add(1, std::memory_order_relaxed); }

} // namespace jalium::audio

extern "C" {

JALIUM_MEDIA_API void jalium_query_audio_stats(jalium_audio_stats_t* out)
{
    if (!out) return;
    std::memset(out, 0, sizeof(*out));
    out->version                = JALIUM_AUDIO_STATS_VERSION;
    out->decoders_opened        = g_stats.decoders_opened.load(std::memory_order_relaxed);
    out->decoders_closed        = g_stats.decoders_closed.load(std::memory_order_relaxed);
    out->devices_opened         = g_stats.devices_opened.load(std::memory_order_relaxed);
    out->devices_closed         = g_stats.devices_closed.load(std::memory_order_relaxed);
    out->frames_decoded_wav     = g_stats.frames_decoded_wav.load(std::memory_order_relaxed);
    out->frames_decoded_flac    = g_stats.frames_decoded_flac.load(std::memory_order_relaxed);
    out->frames_decoded_mp3     = g_stats.frames_decoded_mp3.load(std::memory_order_relaxed);
    out->frames_decoded_ogg     = g_stats.frames_decoded_ogg.load(std::memory_order_relaxed);
    out->frames_decoded_aac     = g_stats.frames_decoded_aac.load(std::memory_order_relaxed);
    out->frames_submitted       = g_stats.frames_submitted.load(std::memory_order_relaxed);
    out->frames_played          = g_stats.frames_played.load(std::memory_order_relaxed);
    out->ring_underruns         = g_stats.ring_underruns.load(std::memory_order_relaxed);
    out->ring_overflows         = g_stats.ring_overflows.load(std::memory_order_relaxed);
    out->codec_probe_failures   = g_stats.codec_probe_failures.load(std::memory_order_relaxed);
    out->platform_aac_fallbacks = g_stats.platform_aac_fallbacks.load(std::memory_order_relaxed);
}

JALIUM_MEDIA_API void jalium_reset_audio_stats(void)
{
    g_stats.decoders_opened.store(0, std::memory_order_relaxed);
    g_stats.decoders_closed.store(0, std::memory_order_relaxed);
    g_stats.devices_opened.store(0, std::memory_order_relaxed);
    g_stats.devices_closed.store(0, std::memory_order_relaxed);
    g_stats.frames_decoded_wav.store(0, std::memory_order_relaxed);
    g_stats.frames_decoded_flac.store(0, std::memory_order_relaxed);
    g_stats.frames_decoded_mp3.store(0, std::memory_order_relaxed);
    g_stats.frames_decoded_ogg.store(0, std::memory_order_relaxed);
    g_stats.frames_decoded_aac.store(0, std::memory_order_relaxed);
    g_stats.frames_submitted.store(0, std::memory_order_relaxed);
    g_stats.frames_played.store(0, std::memory_order_relaxed);
    g_stats.ring_underruns.store(0, std::memory_order_relaxed);
    g_stats.ring_overflows.store(0, std::memory_order_relaxed);
    g_stats.codec_probe_failures.store(0, std::memory_order_relaxed);
    g_stats.platform_aac_fallbacks.store(0, std::memory_order_relaxed);
}

} // extern "C"
