// Windows Media Foundation AAC decoder bridge.
//
// IMFSourceReader is configured to consume any audio container MF recognises
// (.m4a, .mp4, .aac ADTS, .mov, ...) and emit float PCM at the source's
// native sample rate and channel count. We declare the codec as AAC for the
// managed side regardless of the underlying container, because the dispatch
// table routes here only when the audio path matches one of the AAC-family
// extensions (or when an explicit AAC hint is given).
//
// Frame buffering: ReadSample returns one MF sample at a time and the size of
// that sample is variable. We pull one sample, copy what fits into the
// caller's frame buffer, and stash the leftover into `pending` for the next
// ReadFramesImpl call. EOS is sticky and only cleared by SeekImpl.

#include "win_mf_aac_decoder.h"
#include "win_media_init.h"

#include <Windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <propvarutil.h>

#include <algorithm>
#include <cstring>
#include <new>
#include <string>
#include <vector>

namespace jalium::media::win {

namespace {

struct MfAacDecoderImpl final : jalium::audio::audio_decoder_impl {
    IMFSourceReader* reader = nullptr;

    // Leftover PCM from the last ReadSample that did not fit into the caller's
    // buffer. Stored as interleaved float; consumed before the next ReadSample.
    std::vector<float> pending;
    size_t             pendingOffsetFloats = 0;

    bool eosReached = false;

    ~MfAacDecoderImpl() override
    {
        if (reader) {
            reader->Release();
            reader = nullptr;
        }
    }

    uint32_t ReadFramesImpl(float* dst, uint32_t frame_capacity) noexcept override
    {
        if (!reader || channels == 0 || !dst || frame_capacity == 0) return 0;

        const uint32_t ch = channels;
        uint32_t framesWritten = 0;

        // 1) Drain pending leftover from the last MF sample first.
        if (pendingOffsetFloats < pending.size()) {
            const size_t pendingFloats   = pending.size() - pendingOffsetFloats;
            const uint32_t availFrames   = static_cast<uint32_t>(pendingFloats / ch);
            const uint32_t copyFrames    = std::min(availFrames, frame_capacity);
            std::memcpy(dst,
                        pending.data() + pendingOffsetFloats,
                        static_cast<size_t>(copyFrames) * ch * sizeof(float));
            pendingOffsetFloats += static_cast<size_t>(copyFrames) * ch;
            framesWritten += copyFrames;
            if (pendingOffsetFloats >= pending.size()) {
                pending.clear();
                pendingOffsetFloats = 0;
            }
        }

        // 2) Pull additional MF samples until the caller's buffer fills or EOS.
        while (framesWritten < frame_capacity && !eosReached) {
            DWORD flags = 0;
            IMFSample* sample = nullptr;
            HRESULT hr = reader->ReadSample(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                0,
                nullptr,
                &flags,
                nullptr,
                &sample);
            if (FAILED(hr)) {
                if (sample) sample->Release();
                break;
            }
            if (flags & MF_SOURCE_READERF_ENDOFSTREAM) {
                eosReached = true;
                if (sample) sample->Release();
                break;
            }
            if (!sample) {
                // Format change / stream tick — skip and try again.
                continue;
            }

            IMFMediaBuffer* buffer = nullptr;
            hr = sample->ConvertToContiguousBuffer(&buffer);
            if (SUCCEEDED(hr) && buffer) {
                BYTE* data = nullptr;
                DWORD lenBytes = 0;
                if (SUCCEEDED(buffer->Lock(&data, nullptr, &lenBytes)) && data) {
                    const float* samples = reinterpret_cast<const float*>(data);
                    const size_t sampleCountFloats = lenBytes / sizeof(float);

                    const uint32_t remainingDstFrames = frame_capacity - framesWritten;
                    const size_t remainingDstFloats  = static_cast<size_t>(remainingDstFrames) * ch;
                    const size_t copyFloats          = std::min(remainingDstFloats, sampleCountFloats);

                    std::memcpy(dst + static_cast<size_t>(framesWritten) * ch,
                                samples,
                                copyFloats * sizeof(float));
                    framesWritten += static_cast<uint32_t>(copyFloats / ch);

                    if (copyFloats < sampleCountFloats) {
                        const size_t leftover = sampleCountFloats - copyFloats;
                        pending.assign(samples + copyFloats, samples + sampleCountFloats);
                        pendingOffsetFloats = 0;
                        (void)leftover; // documented for clarity
                    }

                    buffer->Unlock();
                }
                buffer->Release();
            }
            sample->Release();
        }

        if (framesWritten > 0) {
            jalium::audio::StatsDecoderFrames(JALIUM_ACODEC_AAC, framesWritten);
        }
        return framesWritten;
    }

    jalium_media_status_t SeekImpl(int64_t position_us) noexcept override
    {
        if (!reader) return JALIUM_MEDIA_E_INVALID_ARG;
        if (position_us < 0) position_us = 0;

        // MF positions are 100-ns ticks (LONGLONG).
        PROPVARIANT pos;
        PropVariantInit(&pos);
        pos.vt = VT_I8;
        pos.hVal.QuadPart = position_us * 10;

        HRESULT hr = reader->SetCurrentPosition(GUID_NULL, pos);
        PropVariantClear(&pos);

        // Clear any half-consumed frame buffer so the next read starts fresh.
        pending.clear();
        pendingOffsetFloats = 0;
        eosReached = false;

        return SUCCEEDED(hr) ? JALIUM_MEDIA_OK : JALIUM_MEDIA_E_DECODE_FAILED;
    }
};

std::wstring Utf8ToWide(const char* utf8) noexcept
{
    if (!utf8 || !*utf8) return std::wstring();
    const int wlen = MultiByteToWideChar(CP_UTF8, 0, utf8, -1, nullptr, 0);
    if (wlen <= 0) return std::wstring();
    std::wstring out(static_cast<size_t>(wlen), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8, -1, out.data(), wlen);
    if (!out.empty() && out.back() == L'\0') out.pop_back();
    return out;
}

jalium_media_status_t HResultToStatus(HRESULT hr) noexcept
{
    if (SUCCEEDED(hr)) return JALIUM_MEDIA_OK;
    switch (hr) {
        case HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND):
        case HRESULT_FROM_WIN32(ERROR_PATH_NOT_FOUND):
        case HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED):
            return JALIUM_MEDIA_E_IO;
        case MF_E_UNSUPPORTED_BYTESTREAM_TYPE:
        case MF_E_UNSUPPORTED_FORMAT:
        case MF_E_INVALIDMEDIATYPE:
            return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
        case E_OUTOFMEMORY:
            return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        default:
            return JALIUM_MEDIA_E_DECODE_FAILED;
    }
}

} // namespace

jalium::audio::audio_decoder_impl* MfAacDecoderOpenFile(
    const char*            utf8_path,
    jalium_media_status_t& outStatus) noexcept
{
    outStatus = JALIUM_MEDIA_OK;
    if (!utf8_path) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    if (!IsInitialized()) {
        outStatus = JALIUM_MEDIA_E_NOT_INITIALIZED;
        return nullptr;
    }

    const std::wstring wpath = Utf8ToWide(utf8_path);
    if (wpath.empty()) {
        outStatus = JALIUM_MEDIA_E_INVALID_ARG;
        return nullptr;
    }
    // GetFileAttributes pre-check so we surface IO vs decode failures cleanly.
    DWORD attrs = GetFileAttributesW(wpath.c_str());
    if (attrs == INVALID_FILE_ATTRIBUTES) {
        outStatus = JALIUM_MEDIA_E_IO;
        return nullptr;
    }

    IMFSourceReader* reader = nullptr;
    HRESULT hr = MFCreateSourceReaderFromURL(wpath.c_str(), nullptr, &reader);
    if (FAILED(hr) || !reader) {
        if (reader) reader->Release();
        outStatus = HResultToStatus(hr);
        return nullptr;
    }

    // Select only the first audio stream.
    reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE);
    reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), TRUE);

    // Pull native channels / sample rate so we can configure output without
    // forcing MF to do a sample-rate conversion we didn't ask for.
    UINT32 nativeChannels   = 2;
    UINT32 nativeSampleRate = 44100;
    {
        IMFMediaType* nativeType = nullptr;
        if (SUCCEEDED(reader->GetNativeMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, &nativeType)) &&
            nativeType) {
            nativeType->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &nativeChannels);
            nativeType->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &nativeSampleRate);
            nativeType->Release();
        }
    }

    // Configure output media type as IEEE float PCM at the native rate.
    IMFMediaType* outType = nullptr;
    if (FAILED(MFCreateMediaType(&outType)) || !outType) {
        reader->Release();
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    outType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
    outType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_Float);
    outType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, nativeChannels);
    outType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, nativeSampleRate);
    outType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 32);
    outType->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, 4u * nativeChannels);
    outType->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 4u * nativeChannels * nativeSampleRate);
    outType->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE);

    hr = reader->SetCurrentMediaType(
        static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), nullptr, outType);
    outType->Release();
    if (FAILED(hr)) {
        reader->Release();
        outStatus = HResultToStatus(hr);
        return nullptr;
    }

    // Duration via presentation descriptor; tolerated absent (live streams etc.).
    int64_t durationUs = 0;
    {
        PROPVARIANT dur;
        PropVariantInit(&dur);
        if (SUCCEEDED(reader->GetPresentationAttribute(
                static_cast<DWORD>(MF_SOURCE_READER_MEDIASOURCE), MF_PD_DURATION, &dur))) {
            if (dur.vt == VT_UI8) {
                durationUs = static_cast<int64_t>(dur.uhVal.QuadPart) / 10; // 100ns -> us
            }
        }
        PropVariantClear(&dur);
    }

    auto* impl = new (std::nothrow) MfAacDecoderImpl();
    if (!impl) {
        reader->Release();
        outStatus = JALIUM_MEDIA_E_OUT_OF_MEMORY;
        return nullptr;
    }
    impl->reader      = reader;
    impl->codec       = JALIUM_ACODEC_AAC;
    impl->sample_rate = nativeSampleRate;
    impl->channels    = nativeChannels;
    impl->duration_us = durationUs;

    jalium::audio::StatsDecoderOpened(JALIUM_ACODEC_AAC);
    return impl;
}

} // namespace jalium::media::win
