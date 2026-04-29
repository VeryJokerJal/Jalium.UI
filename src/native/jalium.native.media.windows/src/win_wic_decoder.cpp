#define JALIUM_MEDIA_EXPORTS
#include "win_wic_decoder.h"
#include "win_media_init.h"
#include "jalium_media_internal.h"

#include <Windows.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <atomic>
#include <cstring>
#include <mutex>
#include <string>

using Microsoft::WRL::ComPtr;

namespace jalium::media::win {

namespace {

std::mutex                     g_factoryMutex;
ComPtr<IWICImagingFactory>     g_factory;

jalium_media_status_t EnsureFactory(IWICImagingFactory** out_factory)
{
    std::lock_guard<std::mutex> lock(g_factoryMutex);
    if (!g_factory) {
        ComPtr<IWICImagingFactory> factory;
        HRESULT hr = CoCreateInstance(
            CLSID_WICImagingFactory,
            nullptr,
            CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(factory.GetAddressOf()));
        if (FAILED(hr)) {
            return JALIUM_MEDIA_E_PLATFORM;
        }
        g_factory = std::move(factory);
    }
    *out_factory = g_factory.Get();
    (*out_factory)->AddRef();
    return JALIUM_MEDIA_OK;
}

const GUID& PickWicGuid(jalium_pixel_format_t fmt)
{
    return (fmt == JALIUM_PF_RGBA8) ? GUID_WICPixelFormat32bppRGBA
                                    : GUID_WICPixelFormat32bppBGRA;
}

// UTF-8 -> wide string for CreateDecoderFromFilename.
std::wstring Utf8ToWide(const char* utf8)
{
    if (!utf8) return {};
    int len = MultiByteToWideChar(CP_UTF8, 0, utf8, -1, nullptr, 0);
    if (len <= 0) return {};
    std::wstring w(static_cast<size_t>(len - 1), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8, -1, w.data(), len);
    return w;
}

jalium_media_status_t DecodeFromBitmapSource(
    IWICImagingFactory*    factory,
    IWICBitmapSource*      source,
    jalium_pixel_format_t  requested_format,
    jalium_image_t*        out_image)
{
    UINT width = 0, height = 0;
    HRESULT hr = source->GetSize(&width, &height);
    if (FAILED(hr) || width == 0 || height == 0) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    ComPtr<IWICFormatConverter> converter;
    hr = factory->CreateFormatConverter(converter.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    hr = converter->Initialize(
        source,
        PickWicGuid(requested_format),
        WICBitmapDitherTypeNone,
        nullptr,
        0.0,
        WICBitmapPaletteTypeMedianCut);
    if (FAILED(hr)) return JALIUM_MEDIA_E_DECODE_FAILED;

    const uint32_t stride = jalium_media_compute_stride(width);
    const size_t   buffer_size = static_cast<size_t>(stride) * height;

    auto* pixels = static_cast<uint8_t*>(jalium_media_aligned_alloc(buffer_size));
    if (!pixels) return JALIUM_MEDIA_E_OUT_OF_MEMORY;

    hr = converter->CopyPixels(nullptr, stride, static_cast<UINT>(buffer_size), pixels);
    if (FAILED(hr)) {
        jalium_media_aligned_free(pixels);
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    out_image->width        = width;
    out_image->height       = height;
    out_image->stride_bytes = stride;
    out_image->format       = requested_format;
    out_image->pixels       = pixels;
    out_image->_reserved    = nullptr;
    return JALIUM_MEDIA_OK;
}

} // anonymous

jalium_media_status_t WicDecodeMemory(
    const uint8_t*        data,
    size_t                size,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!data || size == 0 || !out_image) return JALIUM_MEDIA_E_INVALID_ARG;
    if (size > static_cast<size_t>(UINT32_MAX)) return JALIUM_MEDIA_E_INVALID_ARG;

    *out_image = {};

    ComPtr<IWICImagingFactory> factory;
    {
        IWICImagingFactory* raw = nullptr;
        auto status = EnsureFactory(&raw);
        if (status != JALIUM_MEDIA_OK) return status;
        factory.Attach(raw);
    }

    ComPtr<IWICStream> stream;
    HRESULT hr = factory->CreateStream(stream.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    // WIC takes a non-const pointer but documents not modifying the buffer when
    // initialized read-only via InitializeFromMemory.
    hr = stream->InitializeFromMemory(const_cast<BYTE*>(data), static_cast<DWORD>(size));
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    ComPtr<IWICBitmapDecoder> decoder;
    hr = factory->CreateDecoderFromStream(
        stream.Get(),
        nullptr,
        WICDecodeMetadataCacheOnDemand,
        decoder.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;

    UINT frameCount = 0;
    decoder->GetFrameCount(&frameCount);
    if (frameCount == 0) return JALIUM_MEDIA_E_DECODE_FAILED;

    ComPtr<IWICBitmapFrameDecode> frame;
    hr = decoder->GetFrame(0, frame.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_DECODE_FAILED;

    return DecodeFromBitmapSource(factory.Get(), frame.Get(), requested_format, out_image);
}

jalium_media_status_t WicDecodeFile(
    const char*           utf8_path,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!utf8_path || !out_image) return JALIUM_MEDIA_E_INVALID_ARG;

    *out_image = {};

    auto wpath = Utf8ToWide(utf8_path);
    if (wpath.empty()) return JALIUM_MEDIA_E_INVALID_ARG;

    ComPtr<IWICImagingFactory> factory;
    {
        IWICImagingFactory* raw = nullptr;
        auto status = EnsureFactory(&raw);
        if (status != JALIUM_MEDIA_OK) return status;
        factory.Attach(raw);
    }

    ComPtr<IWICBitmapDecoder> decoder;
    HRESULT hr = factory->CreateDecoderFromFilename(
        wpath.c_str(),
        nullptr,
        GENERIC_READ,
        WICDecodeMetadataCacheOnDemand,
        decoder.GetAddressOf());
    if (hr == HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND) ||
        hr == HRESULT_FROM_WIN32(ERROR_PATH_NOT_FOUND)) {
        return JALIUM_MEDIA_E_IO;
    }
    if (FAILED(hr)) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;

    ComPtr<IWICBitmapFrameDecode> frame;
    hr = decoder->GetFrame(0, frame.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_DECODE_FAILED;

    return DecodeFromBitmapSource(factory.Get(), frame.Get(), requested_format, out_image);
}

jalium_media_status_t WicReadDimensions(
    const uint8_t* data,
    size_t         size,
    uint32_t*      out_width,
    uint32_t*      out_height)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!data || size == 0 || !out_width || !out_height) return JALIUM_MEDIA_E_INVALID_ARG;
    if (size > static_cast<size_t>(UINT32_MAX)) return JALIUM_MEDIA_E_INVALID_ARG;

    *out_width = 0;
    *out_height = 0;

    ComPtr<IWICImagingFactory> factory;
    {
        IWICImagingFactory* raw = nullptr;
        auto status = EnsureFactory(&raw);
        if (status != JALIUM_MEDIA_OK) return status;
        factory.Attach(raw);
    }

    ComPtr<IWICStream> stream;
    HRESULT hr = factory->CreateStream(stream.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    hr = stream->InitializeFromMemory(const_cast<BYTE*>(data), static_cast<DWORD>(size));
    if (FAILED(hr)) return JALIUM_MEDIA_E_PLATFORM;

    ComPtr<IWICBitmapDecoder> decoder;
    hr = factory->CreateDecoderFromStream(
        stream.Get(),
        nullptr,
        WICDecodeMetadataCacheOnDemand,
        decoder.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;

    ComPtr<IWICBitmapFrameDecode> frame;
    hr = decoder->GetFrame(0, frame.GetAddressOf());
    if (FAILED(hr)) return JALIUM_MEDIA_E_DECODE_FAILED;

    UINT w = 0, h = 0;
    hr = frame->GetSize(&w, &h);
    if (FAILED(hr)) return JALIUM_MEDIA_E_DECODE_FAILED;

    *out_width = w;
    *out_height = h;
    return JALIUM_MEDIA_OK;
}

} // namespace jalium::media::win
