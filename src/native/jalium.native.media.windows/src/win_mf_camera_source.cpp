#define JALIUM_MEDIA_EXPORTS
#include "win_mf_camera_source.h"
#include "win_media_init.h"
#include "jalium_media_internal.h"

#include <Windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mferror.h>
#include <mfreadwrite.h>
#include <mfobjects.h>
#include <wrl/client.h>

#include <cstdlib>
#include <cstring>
#include <map>
#include <mutex>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;

struct jalium_camera_source {
    ComPtr<IMFMediaSource>  source;
    ComPtr<IMFSourceReader> reader;
    uint32_t                width        = 0;
    uint32_t                height       = 0;
    uint32_t                stride_bytes = 0;
    jalium_pixel_format_t   format       = JALIUM_PF_BGRA8;

    uint8_t*                frame_buffer       = nullptr;
    size_t                  frame_buffer_size  = 0;
    int64_t                 last_pts_us        = 0;
};

namespace jalium::media::win {

namespace {

std::string WideToUtf8(LPCWSTR w)
{
    if (!w) return {};
    int len = WideCharToMultiByte(CP_UTF8, 0, w, -1, nullptr, 0, nullptr, nullptr);
    if (len <= 0) return {};
    std::string s(static_cast<size_t>(len - 1), '\0');
    WideCharToMultiByte(CP_UTF8, 0, w, -1, s.data(), len, nullptr, nullptr);
    return s;
}

struct DeviceFormatList {
    std::vector<jalium_camera_format_t> formats;
};

void EnumerateFormats(IMFSourceReader* reader, DeviceFormatList& out)
{
    DWORD typeIndex = 0;
    while (true) {
        ComPtr<IMFMediaType> mt;
        HRESULT hr = reader->GetNativeMediaType(
            static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
            typeIndex,
            mt.GetAddressOf());
        if (hr == MF_E_NO_MORE_TYPES) break;
        if (FAILED(hr)) break;

        UINT32 w = 0, h = 0;
        if (SUCCEEDED(MFGetAttributeSize(mt.Get(), MF_MT_FRAME_SIZE, &w, &h)) && w > 0 && h > 0) {
            UINT32 num = 0, den = 0;
            double fps = 0.0;
            if (SUCCEEDED(MFGetAttributeRatio(mt.Get(), MF_MT_FRAME_RATE, &num, &den)) && den != 0) {
                fps = static_cast<double>(num) / static_cast<double>(den);
            }
            out.formats.push_back({w, h, fps});
        }
        ++typeIndex;
    }
}

// Owner block holding the heap allocations behind a jalium_camera_device_t array
// returned by MfCameraEnumerate. Looked up in g_enumOwners by the device-array
// pointer so MfCameraDevicesFree can release everything cleanly.
struct EnumOwner {
    std::vector<std::string>      ids;
    std::vector<std::string>      names;
    std::vector<DeviceFormatList> formats;
};

// File-scope shared registry — two function-local statics would be two distinct
// maps and the owner would never be found.
std::mutex                                       g_enumOwnerMutex;
std::map<jalium_camera_device_t*, EnumOwner*>    g_enumOwners;

} // anonymous

jalium_media_status_t MfCameraEnumerate(
    jalium_camera_device_t** out_devices,
    uint32_t*                out_count)
{
    if (out_devices) *out_devices = nullptr;
    if (out_count) *out_count = 0;
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;

    ComPtr<IMFAttributes> attrs;
    HRESULT hr = MFCreateAttributes(attrs.GetAddressOf(), 1);
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;
    attrs->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                   MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);

    IMFActivate** ppActivate = nullptr;
    UINT32 count = 0;
    hr = MFEnumDeviceSources(attrs.Get(), &ppActivate, &count);
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;
    if (count == 0) {
        if (ppActivate) CoTaskMemFree(ppActivate);
        return JALIUM_MEDIA_OK;
    }

    auto* owner = new (std::nothrow) EnumOwner();
    if (!owner) {
        for (UINT32 i = 0; i < count; ++i) if (ppActivate[i]) ppActivate[i]->Release();
        CoTaskMemFree(ppActivate);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    owner->ids.reserve(count);
    owner->names.reserve(count);
    owner->formats.resize(count);

    auto* devices = new (std::nothrow) jalium_camera_device_t[count];
    if (!devices) {
        delete owner;
        for (UINT32 i = 0; i < count; ++i) if (ppActivate[i]) ppActivate[i]->Release();
        CoTaskMemFree(ppActivate);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }

    for (UINT32 i = 0; i < count; ++i) {
        WCHAR* idW = nullptr;
        UINT32 idLen = 0;
        ppActivate[i]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
                                          &idW, &idLen);
        WCHAR* nameW = nullptr;
        UINT32 nameLen = 0;
        ppActivate[i]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME,
                                          &nameW, &nameLen);

        owner->ids.push_back(WideToUtf8(idW));
        owner->names.push_back(nameW ? WideToUtf8(nameW) : std::string{});

        // Probe formats by activating the device temporarily.
        ComPtr<IMFMediaSource> tmpSource;
        if (SUCCEEDED(ppActivate[i]->ActivateObject(IID_PPV_ARGS(tmpSource.GetAddressOf())))) {
            ComPtr<IMFSourceReader> tmpReader;
            if (SUCCEEDED(MFCreateSourceReaderFromMediaSource(tmpSource.Get(), nullptr, tmpReader.GetAddressOf()))) {
                EnumerateFormats(tmpReader.Get(), owner->formats[i]);
            }
            tmpSource->Shutdown();
        }

        devices[i].id            = owner->ids[i].c_str();
        devices[i].friendly_name = owner->names[i].c_str();
        devices[i].facing        = JALIUM_CAMERA_FACING_UNKNOWN;
        devices[i].format_count  = static_cast<uint32_t>(owner->formats[i].formats.size());
        devices[i].formats       = owner->formats[i].formats.empty()
                                       ? nullptr
                                       : owner->formats[i].formats.data();

        if (idW)   CoTaskMemFree(idW);
        if (nameW) CoTaskMemFree(nameW);
        ppActivate[i]->Release();
    }
    CoTaskMemFree(ppActivate);

    {
        std::lock_guard<std::mutex> lock(g_enumOwnerMutex);
        g_enumOwners[devices] = owner;
    }

    *out_devices = devices;
    *out_count = count;
    return JALIUM_MEDIA_OK;
}

void MfCameraDevicesFree(jalium_camera_device_t* devices, uint32_t /*count*/)
{
    if (!devices) return;
    EnumOwner* owner = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_enumOwnerMutex);
        auto it = g_enumOwners.find(devices);
        if (it != g_enumOwners.end()) {
            owner = it->second;
            g_enumOwners.erase(it);
        }
    }
    delete owner;
    delete[] devices;
}

namespace {

jalium_media_status_t SelectFormat(
    IMFSourceReader* reader,
    uint32_t requested_w, uint32_t requested_h, double requested_fps,
    UINT32* out_w, UINT32* out_h)
{
    DWORD bestIndex = MAXDWORD;
    int64_t bestScore = INT64_MAX;
    UINT32 bestW = 0, bestH = 0;

    DWORD typeIndex = 0;
    while (true) {
        ComPtr<IMFMediaType> mt;
        HRESULT hr = reader->GetNativeMediaType(
            static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
            typeIndex,
            mt.GetAddressOf());
        if (hr == MF_E_NO_MORE_TYPES) break;
        if (FAILED(hr)) break;

        UINT32 w = 0, h = 0;
        if (SUCCEEDED(MFGetAttributeSize(mt.Get(), MF_MT_FRAME_SIZE, &w, &h)) && w > 0 && h > 0) {
            UINT32 num = 0, den = 0;
            double fps = 30.0;
            if (SUCCEEDED(MFGetAttributeRatio(mt.Get(), MF_MT_FRAME_RATE, &num, &den)) && den != 0) {
                fps = static_cast<double>(num) / static_cast<double>(den);
            }
            int64_t score = static_cast<int64_t>(std::abs(static_cast<int64_t>(w) - static_cast<int64_t>(requested_w))) +
                            static_cast<int64_t>(std::abs(static_cast<int64_t>(h) - static_cast<int64_t>(requested_h))) +
                            static_cast<int64_t>(std::abs(fps - requested_fps) * 10);
            if (score < bestScore) {
                bestScore = score;
                bestIndex = typeIndex;
                bestW = w;
                bestH = h;
            }
        }
        ++typeIndex;
    }
    if (bestIndex == MAXDWORD) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;

    ComPtr<IMFMediaType> nativeType;
    if (FAILED(reader->GetNativeMediaType(
            static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
            bestIndex,
            nativeType.GetAddressOf()))) {
        return JALIUM_MEDIA_E_PLATFORM;
    }

    // Force RGB32 (BGRX) output via the source reader's built-in MFT chain.
    ComPtr<IMFMediaType> outputType;
    if (FAILED(MFCreateMediaType(outputType.GetAddressOf()))) return JALIUM_MEDIA_E_PLATFORM;
    outputType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    outputType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
    MFSetAttributeSize(outputType.Get(), MF_MT_FRAME_SIZE, bestW, bestH);

    if (FAILED(reader->SetCurrentMediaType(
            static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
            nullptr,
            outputType.Get()))) {
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }

    *out_w = bestW;
    *out_h = bestH;
    return JALIUM_MEDIA_OK;
}

} // anonymous

jalium_media_status_t MfCameraOpen(
    const char*              device_id,
    uint32_t                 requested_width,
    uint32_t                 requested_height,
    double                   requested_fps,
    jalium_pixel_format_t    requested_format,
    jalium_camera_source_t** out_source)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!device_id || !out_source) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_source = nullptr;

    // Convert UTF-8 device id (MF symbolic link) to wide.
    int wlen = MultiByteToWideChar(CP_UTF8, 0, device_id, -1, nullptr, 0);
    if (wlen <= 0) return JALIUM_MEDIA_E_INVALID_ARG;
    std::wstring wid(static_cast<size_t>(wlen - 1), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, device_id, -1, wid.data(), wlen);

    // Re-enumerate to find the activate matching device_id.
    ComPtr<IMFAttributes> filterAttrs;
    HRESULT hr = MFCreateAttributes(filterAttrs.GetAddressOf(), 1);
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;
    filterAttrs->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                         MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);

    IMFActivate** ppActivate = nullptr;
    UINT32 count = 0;
    hr = MFEnumDeviceSources(filterAttrs.Get(), &ppActivate, &count);
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    ComPtr<IMFActivate> chosen;
    for (UINT32 i = 0; i < count; ++i) {
        WCHAR* idW = nullptr;
        UINT32 idLen = 0;
        ppActivate[i]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
                                          &idW, &idLen);
        if (idW && wid == idW) {
            chosen = ppActivate[i];
        }
        if (idW) CoTaskMemFree(idW);
        ppActivate[i]->Release();
    }
    CoTaskMemFree(ppActivate);

    if (!chosen) return JALIUM_MEDIA_E_NO_DEVICE;

    ComPtr<IMFMediaSource> source;
    if (FAILED(chosen->ActivateObject(IID_PPV_ARGS(source.GetAddressOf())))) {
        return JALIUM_MEDIA_E_PLATFORM;
    }

    ComPtr<IMFSourceReader> reader;
    if (FAILED(MFCreateSourceReaderFromMediaSource(source.Get(), nullptr, reader.GetAddressOf()))) {
        source->Shutdown();
        return JALIUM_MEDIA_E_PLATFORM;
    }

    UINT32 actualW = 0, actualH = 0;
    auto status = SelectFormat(reader.Get(), requested_width, requested_height, requested_fps, &actualW, &actualH);
    if (status != JALIUM_MEDIA_OK) {
        source->Shutdown();
        return status;
    }

    auto* src = new (std::nothrow) jalium_camera_source();
    if (!src) {
        source->Shutdown();
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    src->source       = std::move(source);
    src->reader       = std::move(reader);
    src->width        = actualW;
    src->height       = actualH;
    src->stride_bytes = jalium_media_compute_stride(actualW);
    src->format       = requested_format;

    *out_source = src;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t MfCameraReadFrame(
    jalium_camera_source_t* src,
    jalium_video_frame_t*   out_frame)
{
    if (!src || !src->reader || !out_frame) return JALIUM_MEDIA_E_INVALID_ARG;

    DWORD streamIndex = 0;
    DWORD flags = 0;
    LONGLONG pts100ns = 0;
    ComPtr<IMFSample> sample;
    HRESULT hr = src->reader->ReadSample(
        static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
        0,
        &streamIndex,
        &flags,
        &pts100ns,
        sample.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_DECODE_FAILED;
    if (flags & MF_SOURCE_READERF_ENDOFSTREAM) return JALIUM_MEDIA_E_END_OF_STREAM;
    if (!sample) return JALIUM_MEDIA_E_DECODE_FAILED;

    ComPtr<IMFMediaBuffer> buffer;
    hr = sample->ConvertToContiguousBuffer(buffer.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_DECODE_FAILED;

    BYTE* data = nullptr;
    DWORD maxLen = 0, curLen = 0;
    hr = buffer->Lock(&data, &maxLen, &curLen);
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    const uint32_t dstStride = src->stride_bytes;
    const size_t   needed    = static_cast<size_t>(dstStride) * src->height;
    if (src->frame_buffer_size < needed) {
        if (src->frame_buffer) jalium_media_aligned_free(src->frame_buffer);
        src->frame_buffer = static_cast<uint8_t*>(jalium_media_aligned_alloc(needed));
        if (!src->frame_buffer) {
            buffer->Unlock();
            return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        }
        src->frame_buffer_size = needed;
    }

    const uint32_t srcStride = (src->height > 0) ? static_cast<uint32_t>(curLen / src->height) : dstStride;
    if (srcStride == dstStride) {
        std::memcpy(src->frame_buffer, data, needed);
    } else {
        for (uint32_t row = 0; row < src->height; ++row) {
            std::memcpy(src->frame_buffer + row * dstStride,
                        data + row * srcStride,
                        static_cast<size_t>(dstStride));
        }
    }

    for (uint32_t row = 0; row < src->height; ++row) {
        uint8_t* p = src->frame_buffer + row * dstStride + 3;
        for (uint32_t col = 0; col < src->width; ++col) {
            *p = 0xFF;
            p += 4;
        }
    }

    if (src->format == JALIUM_PF_RGBA8) {
        jalium_media_swap_rb_inplace(src->frame_buffer, src->width, src->height, dstStride);
    }

    buffer->Unlock();
    src->last_pts_us = pts100ns / 10;

    out_frame->width        = src->width;
    out_frame->height       = src->height;
    out_frame->stride_bytes = src->stride_bytes;
    out_frame->format       = src->format;
    out_frame->pixels       = src->frame_buffer;
    out_frame->pts_microseconds = src->last_pts_us;
    out_frame->is_keyframe  = 1;
    return JALIUM_MEDIA_OK;
}

void MfCameraClose(jalium_camera_source_t* src)
{
    if (!src) return;
    if (src->reader) src->reader.Reset();
    if (src->source) {
        src->source->Shutdown();
        src->source.Reset();
    }
    if (src->frame_buffer) {
        jalium_media_aligned_free(src->frame_buffer);
        src->frame_buffer = nullptr;
        src->frame_buffer_size = 0;
    }
    delete src;
}

} // namespace jalium::media::win
