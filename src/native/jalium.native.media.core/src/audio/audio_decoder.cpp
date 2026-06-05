// jalium_audio_decoder_* ABI surface + codec auto-detect dispatch.
//
// AUTO probe order (today: WAV-only, more codecs land per plan Step 3):
//   1. Try the extension hint (".wav" -> WAV, ".flac" -> FLAC, ...).
//   2. Fall back to a header-byte sniff for codecs without a reliable
//      extension. Today only "RIFF...WAVE" is recognised; the rest stay
//      open for follow-up TUs.
// If a specific codec hint is passed, the probe is skipped.

#include "jalium_audio.h"
#include "audio_internal.h"

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cstdio>
#include <cstring>
#include <string>
#include <string_view>
#include <vector>

#if defined(_WIN32)
    #include <Windows.h>
#endif

namespace jalium::audio {

namespace {
// Hook table for platform-native AAC decoders. Stored as atomic pointers so
// platform dlls can register from their init function while the dispatch
// path may be called from any thread.
std::atomic<AacOpenFileFn>   g_aacOpenFile{nullptr};
std::atomic<AacOpenMemoryFn> g_aacOpenMemory{nullptr};
} // namespace

void RegisterAacDecoderHooks(AacOpenFileFn openFile, AacOpenMemoryFn openMemory) noexcept
{
    g_aacOpenFile.store(openFile, std::memory_order_release);
    g_aacOpenMemory.store(openMemory, std::memory_order_release);
}

} // namespace jalium::audio

using jalium::audio::audio_decoder_impl;
namespace ja = jalium::audio;

namespace {

inline audio_decoder_impl* AsImpl(jalium_audio_decoder_t* d) noexcept
{
    return reinterpret_cast<audio_decoder_impl*>(d);
}

inline jalium_audio_decoder_t* AsAbi(audio_decoder_impl* d) noexcept
{
    return reinterpret_cast<jalium_audio_decoder_t*>(d);
}

bool ExtEquals(std::string_view path, std::string_view extLower) noexcept
{
    if (path.size() < extLower.size()) return false;
    auto tail = path.substr(path.size() - extLower.size());
    for (size_t i = 0; i < extLower.size(); ++i) {
        char a = tail[i];
        if (a >= 'A' && a <= 'Z') a = static_cast<char>(a + 32);
        if (a != extLower[i]) return false;
    }
    return true;
}

/// Reads the whole file at `utf8Path` into `out`. On Windows this routes
/// through MultiByteToWideChar + _wfopen_s so that UTF-8 paths containing
/// non-ASCII characters (CJK, accented Latin, etc.) work — plain fopen()
/// interprets bytes as the active ANSI codepage and would fail on those.
/// On Unix-like platforms the libc fopen() already accepts UTF-8 paths so
/// we use it directly.
bool ReadFileToBytes(const char* utf8Path,
                     std::vector<uint8_t>& out,
                     jalium_media_status_t& outStatus) noexcept
{
    out.clear();
    if (!utf8Path) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return false;
    }

    FILE* fp = nullptr;

#if defined(_WIN32)
    const int wlen = MultiByteToWideChar(CP_UTF8, 0, utf8Path, -1, nullptr, 0);
    if (wlen <= 0) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return false;
    }
    std::wstring wpath(static_cast<size_t>(wlen), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8Path, -1, wpath.data(), wlen);
    if (!wpath.empty() && wpath.back() == L'\0') wpath.pop_back();
    if (_wfopen_s(&fp, wpath.c_str(), L"rb") != 0 || !fp) {
        outStatus = JALIUM_MEDIA_E_IO;
        return false;
    }
#else
    fp = std::fopen(utf8Path, "rb");
    if (!fp) {
        outStatus = JALIUM_MEDIA_E_IO;
        return false;
    }
#endif

    if (std::fseek(fp, 0, SEEK_END) != 0) {
        std::fclose(fp);
        outStatus = JALIUM_MEDIA_E_IO;
        return false;
    }
    const long size = std::ftell(fp);
    if (size < 0) {
        std::fclose(fp);
        outStatus = JALIUM_MEDIA_E_IO;
        return false;
    }
    std::fseek(fp, 0, SEEK_SET);

    out.resize(static_cast<size_t>(size));
    const size_t got = (size == 0) ? 0 : std::fread(out.data(), 1, out.size(), fp);
    std::fclose(fp);
    if (got != out.size()) {
        out.clear();
        outStatus = JALIUM_MEDIA_E_IO;
        return false;
    }
    return true;
}

jalium_audio_codec_t ProbeByExtension(const char* utf8Path) noexcept
{
    if (!utf8Path) return JALIUM_ACODEC_AUTO;
    std::string_view sv{utf8Path};
    if (ExtEquals(sv, ".wav"))  return JALIUM_ACODEC_WAV;
    if (ExtEquals(sv, ".flac")) return JALIUM_ACODEC_FLAC;
    if (ExtEquals(sv, ".mp3"))  return JALIUM_ACODEC_MP3;
    if (ExtEquals(sv, ".ogg"))  return JALIUM_ACODEC_OGG;
    if (ExtEquals(sv, ".oga"))  return JALIUM_ACODEC_OGG;
    if (ExtEquals(sv, ".aac"))  return JALIUM_ACODEC_AAC;
    if (ExtEquals(sv, ".m4a"))  return JALIUM_ACODEC_AAC;
    if (ExtEquals(sv, ".mp4"))  return JALIUM_ACODEC_AAC;
    return JALIUM_ACODEC_AUTO;
}

jalium_audio_codec_t ProbeByHeader(const uint8_t* data, size_t size) noexcept
{
    if (!data) return JALIUM_ACODEC_AUTO;
    if (size >= 12 &&
        std::memcmp(data,     "RIFF", 4) == 0 &&
        std::memcmp(data + 8, "WAVE", 4) == 0) {
        return JALIUM_ACODEC_WAV;
    }
    if (size >= 4 && std::memcmp(data, "fLaC", 4) == 0) return JALIUM_ACODEC_FLAC;
    if (size >= 4 && std::memcmp(data, "OggS", 4) == 0) return JALIUM_ACODEC_OGG;
    // MP3 and AAC framed-streams need deeper probing; left for the codec TUs.
    return JALIUM_ACODEC_AUTO;
}

audio_decoder_impl* DispatchOpenFile(const char* utf8Path,
                                     jalium_audio_codec_t codec,
                                     jalium_media_status_t& outStatus) noexcept
{
    switch (codec) {
        case JALIUM_ACODEC_WAV:  return ja::WavDecoderOpenFile(utf8Path, outStatus);
        case JALIUM_ACODEC_FLAC: return ja::FlacDecoderOpenFile(utf8Path, outStatus);
        case JALIUM_ACODEC_MP3:  return ja::Mp3DecoderOpenFile(utf8Path, outStatus);
        case JALIUM_ACODEC_OGG:  return ja::VorbisDecoderOpenFile(utf8Path, outStatus);
        case JALIUM_ACODEC_AAC: {
            // AAC dispatch goes through the per-platform hook registered by
            // the platform library at init time. Without a hook (e.g. on
            // Linux/Android until those modules land), surface NOT_IMPLEMENTED.
            auto fn = ja::g_aacOpenFile.load(std::memory_order_acquire);
            if (!fn) {
                outStatus = JALIUM_MEDIA_E_NOT_IMPLEMENTED;
                return nullptr;
            }
            return fn(utf8Path, outStatus);
        }
        default:
            outStatus = JALIUM_MEDIA_E_UNSUPPORTED_CODEC;
            return nullptr;
    }
}

audio_decoder_impl* DispatchOpenMemory(const uint8_t* data,
                                       size_t size,
                                       jalium_audio_codec_t codec,
                                       jalium_media_status_t& outStatus) noexcept
{
    switch (codec) {
        case JALIUM_ACODEC_WAV:  return ja::WavDecoderOpenMemory(data, size, outStatus);
        case JALIUM_ACODEC_FLAC: return ja::FlacDecoderOpenMemory(data, size, outStatus);
        case JALIUM_ACODEC_MP3:  return ja::Mp3DecoderOpenMemory(data, size, outStatus);
        case JALIUM_ACODEC_OGG:  return ja::VorbisDecoderOpenMemory(data, size, outStatus);
        case JALIUM_ACODEC_AAC: {
            auto fn = ja::g_aacOpenMemory.load(std::memory_order_acquire);
            if (!fn) {
                outStatus = JALIUM_MEDIA_E_NOT_IMPLEMENTED;
                return nullptr;
            }
            return fn(data, size, outStatus);
        }
        default:
            outStatus = JALIUM_MEDIA_E_UNSUPPORTED_CODEC;
            return nullptr;
    }
}

} // namespace

namespace jalium::audio {

audio_decoder_impl* DecoderOpenFile(const char* utf8Path,
                                    jalium_audio_codec_t hint,
                                    jalium_media_status_t& outStatus) noexcept
{
    if (!utf8Path) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    jalium_audio_codec_t codec = (hint != JALIUM_ACODEC_AUTO)
                                     ? hint
                                     : ProbeByExtension(utf8Path);
    if (codec == JALIUM_ACODEC_AUTO) {
        StatsCodecProbeFailed();
        outStatus = JALIUM_MEDIA_E_UNSUPPORTED_CODEC;
        return nullptr;
    }

    // AAC dispatches into the platform-native bridge (Windows MF / Android
    // MediaCodec / Apple AudioToolbox / Linux GStreamer), each of which owns
    // its own Unicode-safe path handling — keep it on the file path so we
    // don't gratuitously buffer huge files in memory.
    if (codec == JALIUM_ACODEC_AAC) {
        auto* impl = DispatchOpenFile(utf8Path, codec, outStatus);
        if (!impl && outStatus == JALIUM_MEDIA_OK) outStatus = JALIUM_MEDIA_E_DECODE_FAILED;
        return impl;
    }

    // For every other codec (WAV / FLAC / MP3 / Vorbis) the single-header
    // libs go through libc fopen() under the hood. On Windows that uses the
    // ANSI codepage and silently fails on any UTF-8 path with CJK / accented
    // characters. Side-step the whole problem by pre-reading the file with a
    // wide-char-safe call and feeding the codec from memory — the bytes are
    // parked on the impl so they outlive the codec's internal pointer.
    std::vector<uint8_t> bytes;
    jalium_media_status_t readStatus = JALIUM_MEDIA_OK;
    if (!ReadFileToBytes(utf8Path, bytes, readStatus)) {
        outStatus = readStatus;
        return nullptr;
    }

    auto* impl = DispatchOpenMemory(bytes.data(), bytes.size(), codec, outStatus);
    if (!impl) {
        if (outStatus == JALIUM_MEDIA_OK) outStatus = JALIUM_MEDIA_E_DECODE_FAILED;
        return nullptr;
    }
    // Transfer ownership of the file bytes onto the impl. std::vector's move
    // constructor keeps data() stable so the pointer already handed to the
    // codec remains valid after this assignment.
    impl->owned_bytes = std::move(bytes);
    return impl;
}

audio_decoder_impl* DecoderOpenMemory(const uint8_t* data,
                                      size_t size,
                                      jalium_audio_codec_t hint,
                                      jalium_media_status_t& outStatus) noexcept
{
    if (!data || size == 0) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    jalium_audio_codec_t codec = (hint != JALIUM_ACODEC_AUTO)
                                     ? hint
                                     : ProbeByHeader(data, size);
    if (codec == JALIUM_ACODEC_AUTO) {
        StatsCodecProbeFailed();
        outStatus = JALIUM_MEDIA_E_UNSUPPORTED_CODEC;
        return nullptr;
    }
    auto* impl = DispatchOpenMemory(data, size, codec, outStatus);
    if (!impl && outStatus == JALIUM_MEDIA_OK) {
        outStatus = JALIUM_MEDIA_E_DECODE_FAILED;
    }
    return impl;
}

jalium_media_status_t DecoderGetInfo(audio_decoder_impl* decoder,
                                     jalium_audio_info_t& outInfo) noexcept
{
    if (!decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    outInfo.sample_rate   = decoder->sample_rate;
    outInfo.channels      = decoder->channels;
    outInfo.duration_us   = decoder->duration_us;
    outInfo.codec         = decoder->codec;
    outInfo.output_format = JALIUM_ASF_F32;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t DecoderReadFrames(audio_decoder_impl* decoder,
                                        float* dst,
                                        uint32_t frameCapacity,
                                        uint32_t& outFramesRead) noexcept
{
    outFramesRead = 0;
    if (!decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    if (!dst || frameCapacity == 0) return JALIUM_MEDIA_OK;
    uint32_t got = decoder->ReadFramesImpl(dst, frameCapacity);
    outFramesRead = got;
    return got > 0 ? JALIUM_MEDIA_OK : JALIUM_MEDIA_E_END_OF_STREAM;
}

jalium_media_status_t DecoderSeekUs(audio_decoder_impl* decoder, int64_t us) noexcept
{
    if (!decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    return decoder->SeekImpl(us);
}

void DecoderClose(audio_decoder_impl* decoder) noexcept
{
    if (!decoder) return;
    StatsDecoderClosed();
    delete decoder;
}

} // namespace jalium::audio

// ---------------------------------------------------------------------------
// ABI surface
// ---------------------------------------------------------------------------

extern "C" {

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_decoder_open_file(
    const char* utf8_path,
    jalium_audio_codec_t hint,
    jalium_audio_decoder_t** out_decoder)
{
    if (out_decoder) *out_decoder = nullptr;
    if (!utf8_path || !out_decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    if (!ja::IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;

    jalium_media_status_t status = JALIUM_MEDIA_OK;
    auto* impl = ja::DecoderOpenFile(utf8_path, hint, status);
    if (!impl) return status != JALIUM_MEDIA_OK ? status : JALIUM_MEDIA_E_DECODE_FAILED;
    *out_decoder = AsAbi(impl);
    return JALIUM_MEDIA_OK;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_decoder_open_memory(
    const uint8_t* data,
    size_t size,
    jalium_audio_codec_t hint,
    jalium_audio_decoder_t** out_decoder)
{
    if (out_decoder) *out_decoder = nullptr;
    if (!data || size == 0 || !out_decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    if (!ja::IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;

    jalium_media_status_t status = JALIUM_MEDIA_OK;
    auto* impl = ja::DecoderOpenMemory(data, size, hint, status);
    if (!impl) return status != JALIUM_MEDIA_OK ? status : JALIUM_MEDIA_E_DECODE_FAILED;
    *out_decoder = AsAbi(impl);
    return JALIUM_MEDIA_OK;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_decoder_get_info(
    jalium_audio_decoder_t* decoder,
    jalium_audio_info_t* out_info)
{
    if (!out_info) return JALIUM_MEDIA_E_INVALID_ARG;
    return ja::DecoderGetInfo(AsImpl(decoder), *out_info);
}

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_decoder_read_frames(
    jalium_audio_decoder_t* decoder,
    float* dst,
    uint32_t frame_capacity,
    uint32_t* out_frames_read)
{
    uint32_t got = 0;
    jalium_media_status_t st = ja::DecoderReadFrames(AsImpl(decoder), dst, frame_capacity, got);
    if (out_frames_read) *out_frames_read = got;
    return st;
}

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_decoder_seek_us(
    jalium_audio_decoder_t* decoder,
    int64_t position_us)
{
    return ja::DecoderSeekUs(AsImpl(decoder), position_us);
}

JALIUM_MEDIA_API void jalium_audio_decoder_close(jalium_audio_decoder_t* decoder)
{
    ja::DecoderClose(AsImpl(decoder));
}

} // extern "C"
