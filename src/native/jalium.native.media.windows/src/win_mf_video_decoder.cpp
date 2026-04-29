#define JALIUM_MEDIA_EXPORTS
#include "win_mf_video_decoder.h"
#include "win_media_init.h"
#include "jalium_media_internal.h"

#include <Windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <propvarutil.h>
#include <wrl/client.h>

#include <cstring>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;

// Opaque struct exposed by jalium_media.h.
struct jalium_video_decoder {
    ComPtr<IMFSourceReader> reader;
    uint32_t                width        = 0;
    uint32_t                height       = 0;
    uint32_t                stride_bytes = 0;
    double                  duration_s   = 0.0;
    double                  fps          = 0.0;
    uint64_t                frame_count  = 0;
    jalium_video_codec_t    active_codec = JALIUM_CODEC_NONE;
    jalium_pixel_format_t   format       = JALIUM_PF_BGRA8;

    // Reusable frame buffer (callee-owned, valid until next read_frame / close).
    uint8_t*                frame_buffer       = nullptr;
    size_t                  frame_buffer_size  = 0;
    int64_t                 last_pts_us        = 0;
    int                     last_keyframe      = 0;
};

namespace jalium::media::win {

namespace {

std::wstring Utf8ToWide(const char* utf8)
{
    if (!utf8) return {};
    int len = MultiByteToWideChar(CP_UTF8, 0, utf8, -1, nullptr, 0);
    if (len <= 0) return {};
    std::wstring w(static_cast<size_t>(len - 1), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8, -1, w.data(), len);
    return w;
}

jalium_video_codec_t MapSubtypeToCodec(const GUID& subtype)
{
    if (subtype == MFVideoFormat_H264) return JALIUM_CODEC_H264;
    if (subtype == MFVideoFormat_HEVC) return JALIUM_CODEC_HEVC;
    if (subtype == MFVideoFormat_VP90) return JALIUM_CODEC_VP9;
    if (subtype == MFVideoFormat_AV1)  return JALIUM_CODEC_AV1;
    return JALIUM_CODEC_NONE;
}

jalium_media_status_t ConfigureOutputType(IMFSourceReader* reader)
{
    // MFVideoFormat_RGB32 in MF terms = B G R X (alpha undefined). We force
    // alpha = 0xFF in the copy step so the output matches the documented BGRA8 contract.
    ComPtr<IMFMediaType> outputType;
    HRESULT hr = MFCreateMediaType(outputType.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    hr = outputType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    hr = outputType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    hr = reader->SetCurrentMediaType(
        static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
        nullptr,
        outputType.Get());
    if (FAILED(hr)) return JALIUM_MEDIA_E_UNSUPPORTED_CODEC;

    // Disable other streams to avoid spurious ReadSample work.
    reader->SetStreamSelection(MF_SOURCE_READER_ALL_STREAMS, FALSE);
    reader->SetStreamSelection(MF_SOURCE_READER_FIRST_VIDEO_STREAM, TRUE);
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t QueryStreamInfo(IMFSourceReader* reader, jalium_video_decoder_t* dec)
{
    ComPtr<IMFMediaType> nativeType;
    HRESULT hr = reader->GetNativeMediaType(
        static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
        0,
        nativeType.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    GUID subtype{};
    nativeType->GetGUID(MF_MT_SUBTYPE, &subtype);
    dec->active_codec = MapSubtypeToCodec(subtype);

    ComPtr<IMFMediaType> currentType;
    hr = reader->GetCurrentMediaType(
        static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
        currentType.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    UINT32 w = 0, h = 0;
    if (FAILED(MFGetAttributeSize(currentType.Get(), MF_MT_FRAME_SIZE, &w, &h)) || w == 0 || h == 0) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
    dec->width = w;
    dec->height = h;
    dec->stride_bytes = jalium_media_compute_stride(w);

    UINT32 num = 0, den = 0;
    if (SUCCEEDED(MFGetAttributeRatio(currentType.Get(), MF_MT_FRAME_RATE, &num, &den)) && den != 0) {
        dec->fps = static_cast<double>(num) / static_cast<double>(den);
    }

    PROPVARIANT durationVar;
    PropVariantInit(&durationVar);
    if (SUCCEEDED(reader->GetPresentationAttribute(
            static_cast<DWORD>(MF_SOURCE_READER_MEDIASOURCE),
            MF_PD_DURATION,
            &durationVar)) && durationVar.vt == VT_UI8) {
        // 100-ns ticks → seconds.
        dec->duration_s = static_cast<double>(durationVar.uhVal.QuadPart) / 10'000'000.0;
        if (dec->fps > 0.0) {
            dec->frame_count = static_cast<uint64_t>(dec->duration_s * dec->fps);
        }
    }
    PropVariantClear(&durationVar);

    return JALIUM_MEDIA_OK;
}

// Copy a single MF RGB32 (BGRX) buffer into the decoder's reusable frame buffer,
// honouring the destination stride and forcing alpha=0xFF.
jalium_media_status_t CopySampleToFrame(jalium_video_decoder_t* dec, IMFSample* sample)
{
    ComPtr<IMFMediaBuffer> buffer;
    HRESULT hr = sample->ConvertToContiguousBuffer(buffer.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_DECODE_FAILED;

    BYTE* src = nullptr;
    DWORD maxLen = 0, curLen = 0;
    hr = buffer->Lock(&src, &maxLen, &curLen);
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    const uint32_t dstStride = dec->stride_bytes;
    const size_t   needed    = static_cast<size_t>(dstStride) * dec->height;

    if (dec->frame_buffer_size < needed) {
        if (dec->frame_buffer) jalium_media_aligned_free(dec->frame_buffer);
        dec->frame_buffer = static_cast<uint8_t*>(jalium_media_aligned_alloc(needed));
        if (!dec->frame_buffer) {
            buffer->Unlock();
            return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        }
        dec->frame_buffer_size = needed;
    }

    // MF can hand us a tightly-packed buffer (curLen == width*height*4) or a
    // padded one (curLen > that). When source stride equals destination stride
    // we memcpy in one shot; otherwise row-by-row with the source pitch derived
    // from curLen / height.
    const uint32_t srcStride = (dec->height > 0)
        ? static_cast<uint32_t>(curLen / dec->height)
        : dstStride;

    if (srcStride == dstStride) {
        std::memcpy(dec->frame_buffer, src, needed);
    } else {
        for (uint32_t row = 0; row < dec->height; ++row) {
            std::memcpy(dec->frame_buffer + row * dstStride,
                        src + row * srcStride,
                        static_cast<size_t>(dstStride));
        }
    }

    // Force alpha = 0xFF (MF RGB32 leaves the X byte undefined).
    for (uint32_t row = 0; row < dec->height; ++row) {
        uint8_t* p = dec->frame_buffer + row * dstStride + 3;
        for (uint32_t col = 0; col < dec->width; ++col) {
            *p = 0xFF;
            p += 4;
        }
    }

    // RGBA8 was requested? Swap R/B in-place.
    if (dec->format == JALIUM_PF_RGBA8) {
        jalium_media_swap_rb_inplace(dec->frame_buffer, dec->width, dec->height, dstStride);
    }

    buffer->Unlock();
    return JALIUM_MEDIA_OK;
}

} // anonymous

jalium_media_status_t MfVideoDecoderOpenFile(
    const char*              utf8_path,
    jalium_pixel_format_t    requested_format,
    jalium_video_decoder_t** out_decoder)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!utf8_path || !out_decoder) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_decoder = nullptr;

    auto wpath = Utf8ToWide(utf8_path);
    if (wpath.empty()) return JALIUM_MEDIA_E_INVALID_ARG;

    ComPtr<IMFAttributes> attrs;
    HRESULT hr = MFCreateAttributes(attrs.GetAddressOf(), 4);
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;
    attrs->SetUINT32(MF_LOW_LATENCY, TRUE);
    attrs->SetUINT32(MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, TRUE);

    ComPtr<IMFSourceReader> reader;
    hr = MFCreateSourceReaderFromURL(wpath.c_str(), attrs.Get(), reader.GetAddressOf());
    if (hr == HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND)) return JALIUM_MEDIA_E_IO;
    if (FAILED(hr)) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;

    auto status = ConfigureOutputType(reader.Get());
    if (status != JALIUM_MEDIA_OK) return status;

    auto* dec = new (std::nothrow) jalium_video_decoder();
    if (!dec) return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    dec->reader = std::move(reader);
    dec->format = requested_format;

    status = QueryStreamInfo(dec->reader.Get(), dec);
    if (status != JALIUM_MEDIA_OK) {
        delete dec;
        return status;
    }

    *out_decoder = dec;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t MfVideoDecoderGetInfo(
    jalium_video_decoder_t* decoder,
    jalium_video_info_t*    out_info)
{
    if (!decoder || !out_info) return JALIUM_MEDIA_E_INVALID_ARG;
    out_info->width            = decoder->width;
    out_info->height           = decoder->height;
    out_info->duration_seconds = decoder->duration_s;
    out_info->frame_rate       = decoder->fps;
    out_info->frame_count      = decoder->frame_count;
    out_info->active_codec     = decoder->active_codec;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t MfVideoDecoderReadFrame(
    jalium_video_decoder_t* decoder,
    jalium_video_frame_t*   out_frame)
{
    if (!decoder || !decoder->reader || !out_frame) return JALIUM_MEDIA_E_INVALID_ARG;

    DWORD     streamIndex   = 0;
    DWORD     flags         = 0;
    LONGLONG  pts100ns      = 0;
    ComPtr<IMFSample> sample;

    HRESULT hr = decoder->reader->ReadSample(
        static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
        0,
        &streamIndex,
        &flags,
        &pts100ns,
        sample.GetAddressOf());

    if (FAILED(hr)) return JALIUM_MEDIA_E_DECODE_FAILED;

    if (flags & MF_SOURCE_READERF_ENDOFSTREAM) {
        return JALIUM_MEDIA_E_END_OF_STREAM;
    }

    if (flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) {
        // Re-query stream info — width/height may have changed.
        QueryStreamInfo(decoder->reader.Get(), decoder);
    }

    if (!sample) {
        // No sample but no EOS either — legitimate for some formats; treat as transient,
        // signal EOS so the caller's loop yields rather than spinning.
        return JALIUM_MEDIA_E_END_OF_STREAM;
    }

    auto status = CopySampleToFrame(decoder, sample.Get());
    if (status != JALIUM_MEDIA_OK) return status;

    decoder->last_pts_us = pts100ns / 10;
    decoder->last_keyframe = 0;

    out_frame->width        = decoder->width;
    out_frame->height       = decoder->height;
    out_frame->stride_bytes = decoder->stride_bytes;
    out_frame->format       = decoder->format;
    out_frame->pixels       = decoder->frame_buffer;
    out_frame->pts_microseconds = decoder->last_pts_us;
    out_frame->is_keyframe  = decoder->last_keyframe;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t MfVideoDecoderSeek(
    jalium_video_decoder_t* decoder,
    int64_t                 pts_microseconds)
{
    if (!decoder || !decoder->reader) return JALIUM_MEDIA_E_INVALID_ARG;

    PROPVARIANT pv;
    PropVariantInit(&pv);
    pv.vt = VT_I8;
    pv.hVal.QuadPart = pts_microseconds * 10;  // µs → 100-ns ticks
    HRESULT hr = decoder->reader->SetCurrentPosition(GUID_NULL, pv);
    PropVariantClear(&pv);
    return SUCCEEDED(hr) ? JALIUM_MEDIA_OK : JALIUM_MEDIA_E_PLATFORM;
}

void MfVideoDecoderClose(jalium_video_decoder_t* decoder)
{
    if (!decoder) return;
    if (decoder->frame_buffer) {
        jalium_media_aligned_free(decoder->frame_buffer);
        decoder->frame_buffer = nullptr;
        decoder->frame_buffer_size = 0;
    }
    decoder->reader.Reset();
    delete decoder;
}

} // namespace jalium::media::win
