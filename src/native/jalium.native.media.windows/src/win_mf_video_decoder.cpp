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
#include <d3d11.h>
#include <d3d11_1.h>

#include <cstring>
#include <string>
#include <vector>

#pragma comment(lib, "d3d11.lib")

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

    // Stage 3a: DXVA hardware decode.
    // ID3D11Device + IMFDXGIDeviceManager handed to IMFSourceReader. MF picks
    // the DXVA decoder MFT for H.264 / HEVC / VP9 / AV1 and the GPU
    // VideoProcessorMFT for NV12->BGRA conversion + readback. CopySampleToFrame
    // remains the consumer of the CPU IMFMediaBuffer MF hands back, so no
    // downstream wiring changes. Saves the 30-100 percent CPU otherwise spent
    // on software decode at 1080p.
    //
    // Stage 3b (follow-up PR): MfVideoDecoderAcquireGpuDescriptor returns an
    // NT HANDLE from IDXGIResource1::CreateSharedHandle so D3D12 can
    // OpenSharedHandle it and sample directly, skipping the GPU->CPU readback.
    ComPtr<ID3D11Device>          d3d11Device;
    ComPtr<ID3D11DeviceContext>   d3d11Context;
    ComPtr<IMFDXGIDeviceManager>  dxgiDeviceManager;
    UINT                          dxgiResetToken = 0;
    bool                          dxvaEnabled    = false;

    // Stage 3b.1: shared-able BGRA8 D3D11 texture that mirrors the last GPU
    // decode output. ReadSample's IMFDXGIBuffer / ID3D11Texture2D is owned by
    // MF's internal decoder pool and cannot be shared directly; we own a
    // BGRA8 texture with D3D11_RESOURCE_MISC_SHARED_NTHANDLE +
    // D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX, CopyResource the MF sample into
    // it, then expose its NT handle via IDXGIResource1::CreateSharedHandle.
    // The NT handle is what D3D12 OpenSharedHandle imports in stage 3b.2.
    ComPtr<ID3D11Texture2D>       sharedTexture;          // own SHARED_NTHANDLE texture
    HANDLE                        sharedTextureHandle = nullptr;  // owned NT HANDLE (CloseHandle on dtor)
    bool                          sharedTextureValid  = false;    // true after first successful CopyResource
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

// Stage 3a: DXVA D3D11Device creation.
//
// Create a private D3D11 device (VIDEO_SUPPORT + BGRA_SUPPORT + multithread
// protected) and an IMFDXGIDeviceManager. SourceReader picks the DXVA decoder
// MFT for H.264 / HEVC / VP9 / AV1 + GPU VideoProcessorMFT for NV12 to BGRA32
// conversion once we hand it the manager via MF_SOURCE_READER_D3D_MANAGER.
// Output samples remain CPU IMFMediaBuffer (the GPU-to-CPU readback is MF's
// own concern), so the rest of the code path is unchanged.
//
// On failure dxgiDeviceManager stays null and SourceReader transparently falls
// back to pure CPU software decode. Typical failure causes: WARP-only GPU
// (VMware / remote desktop) or driver without D3D11_CREATE_DEVICE_VIDEO_SUPPORT.
bool TryCreateD3D11VideoDevice(jalium_video_decoder_t* dec)
{
    UINT createFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT
                     | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;

    const D3D_FEATURE_LEVEL levels[] = {
        D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0,
    };

    ComPtr<ID3D11Device>        device;
    ComPtr<ID3D11DeviceContext> context;
    HRESULT hr = D3D11CreateDevice(
        nullptr,                       // default adapter
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        createFlags,
        levels, ARRAYSIZE(levels),
        D3D11_SDK_VERSION,
        device.GetAddressOf(),
        nullptr,
        context.GetAddressOf());
    if (FAILED(hr) || !device) return false;

    // MF reads from a worker thread; D3D11 device must be marked
    // multithread-safe or the reader's calls into it will race the UI thread's
    // share-handle / queryinterface calls (D3D11 doesn't lock by default).
    ComPtr<ID3D10Multithread> mt;
    if (SUCCEEDED(device.As(&mt))) {
        mt->SetMultithreadProtected(TRUE);
    }

    UINT resetToken = 0;
    ComPtr<IMFDXGIDeviceManager> mgr;
    hr = MFCreateDXGIDeviceManager(&resetToken, mgr.GetAddressOf());
    if (FAILED(hr) || !mgr) return false;

    hr = mgr->ResetDevice(device.Get(), resetToken);
    if (FAILED(hr)) return false;

    dec->d3d11Device       = std::move(device);
    dec->d3d11Context      = std::move(context);
    dec->dxgiDeviceManager = std::move(mgr);
    dec->dxgiResetToken    = resetToken;
    dec->dxvaEnabled       = true;
    return true;
}

// Stage 3b.1: lazily create the shared NT-handle BGRA8 texture used to mirror
// the latest decoded frame. Reused across frames (size invariant); we only
// recreate when the stream changes resolution.
bool EnsureSharedTexture(jalium_video_decoder_t* dec)
{
    if (!dec->d3d11Device) return false;

    if (dec->sharedTexture) {
        D3D11_TEXTURE2D_DESC existing{};
        dec->sharedTexture->GetDesc(&existing);
        if (existing.Width == dec->width && existing.Height == dec->height) {
            return true;
        }
        // Resolution changed mid-stream — drop the old texture + handle.
        if (dec->sharedTextureHandle) {
            CloseHandle(dec->sharedTextureHandle);
            dec->sharedTextureHandle = nullptr;
        }
        dec->sharedTexture.Reset();
        dec->sharedTextureValid = false;
    }

    D3D11_TEXTURE2D_DESC desc{};
    desc.Width            = dec->width;
    desc.Height           = dec->height;
    desc.MipLevels        = 1;
    desc.ArraySize        = 1;
    desc.Format           = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage            = D3D11_USAGE_DEFAULT;
    desc.BindFlags        = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
    // NT-handle shared so D3D12 OpenSharedHandle can import. KEYED_MUTEX gives
    // D3D11 writer + D3D12 reader a synchronization handshake (defer to 3b.2
    // — for now we serialize via D3D11 device flush before exposing handle).
    desc.MiscFlags        = D3D11_RESOURCE_MISC_SHARED_NTHANDLE
                          | D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;

    HRESULT hr = dec->d3d11Device->CreateTexture2D(&desc, nullptr, dec->sharedTexture.GetAddressOf());
    if (FAILED(hr) || !dec->sharedTexture) return false;

    ComPtr<IDXGIResource1> res1;
    if (FAILED(dec->sharedTexture.As(&res1))) {
        dec->sharedTexture.Reset();
        return false;
    }
    hr = res1->CreateSharedHandle(
        nullptr,
        DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE,
        nullptr,
        &dec->sharedTextureHandle);
    if (FAILED(hr) || !dec->sharedTextureHandle) {
        dec->sharedTexture.Reset();
        dec->sharedTextureHandle = nullptr;
        return false;
    }
    return true;
}

// Stage 3b.1: when MF gave us a GPU sample (IMFDXGIBuffer), CopyResource into
// our owned shared NT texture so D3D12 (a different device) can OpenSharedHandle
// it. Caller (ReadFrame) drives this whenever it sees a GPU sample. Returns
// true on success; on false caller falls back to the existing CPU path
// (CopySampleToFrame) so the frame still renders.
bool TryCopySampleToSharedTexture(jalium_video_decoder_t* dec, IMFSample* sample)
{
    if (!dec->dxvaEnabled || !dec->d3d11Context || !sample) return false;
    if (!EnsureSharedTexture(dec)) return false;

    ComPtr<IMFMediaBuffer> buffer;
    if (FAILED(sample->GetBufferByIndex(0, buffer.GetAddressOf()))) return false;

    ComPtr<IMFDXGIBuffer> dxgiBuffer;
    if (FAILED(buffer.As(&dxgiBuffer))) return false;

    ComPtr<ID3D11Texture2D> mfTex;
    if (FAILED(dxgiBuffer->GetResource(IID_PPV_ARGS(mfTex.GetAddressOf())))) return false;

    UINT subresource = 0;
    dxgiBuffer->GetSubresourceIndex(&subresource);

    // Acquire keyed mutex on the writer side (key 0 means "available").
    ComPtr<IDXGIKeyedMutex> writeMutex;
    if (FAILED(dec->sharedTexture.As(&writeMutex))) return false;
    if (FAILED(writeMutex->AcquireSync(0, 16))) return false;  // 16ms timeout

    dec->d3d11Context->CopySubresourceRegion(
        dec->sharedTexture.Get(), 0, 0, 0, 0,
        mfTex.Get(), subresource, nullptr);
    dec->d3d11Context->Flush();

    // Release key 1 so D3D12 reader can pick up — D3D12 side then re-releases
    // back to key 0 after it samples. Stage 3b.2 ImportedD3D12VideoSurface
    // handles the reader side of the handshake.
    writeMutex->ReleaseSync(1);

    dec->sharedTextureValid = true;
    return true;
}

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

    auto* dec = new (std::nothrow) jalium_video_decoder();
    if (!dec) return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    dec->format = requested_format;

    // Best-effort DXVA setup. Failure leaves dxvaEnabled=false and the reader
    // falls back to CPU decode transparently.
    TryCreateD3D11VideoDevice(dec);

    ComPtr<IMFAttributes> attrs;
    HRESULT hr = MFCreateAttributes(attrs.GetAddressOf(), 6);
    if (FAILED(hr)) { delete dec; return JALIUM_MEDIA_E_PLATFORM; }
    attrs->SetUINT32(MF_LOW_LATENCY, TRUE);
    attrs->SetUINT32(MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, TRUE);
    if (dec->dxvaEnabled) {
        // Hand the SourceReader the D3D11 device manager so MF picks the DXVA
        // decoder MFT and the GPU VideoProcessorMFT for NV12→BGRA conversion.
        attrs->SetUnknown(MF_SOURCE_READER_D3D_MANAGER, dec->dxgiDeviceManager.Get());
        attrs->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE);
    }

    ComPtr<IMFSourceReader> reader;
    hr = MFCreateSourceReaderFromURL(wpath.c_str(), attrs.Get(), reader.GetAddressOf());
    if (FAILED(hr)) {
        delete dec;
        if (hr == HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND)) return JALIUM_MEDIA_E_IO;
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }

    auto status = ConfigureOutputType(reader.Get());
    if (status != JALIUM_MEDIA_OK) { delete dec; return status; }

    dec->reader = std::move(reader);

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

    // Stage 3b.1: when DXVA is on, try the GPU path first — mirror the GPU
    // sample into our shared NT texture so AcquireGpuDescriptor can hand the
    // NT HANDLE to D3D12 OpenSharedHandle. Failure (non-DXGI buffer, mutex
    // timeout, no SHARED_NTHANDLE support on driver, ...) drops to the CPU
    // CopySampleToFrame path so the frame still renders via stage 2 BGRA
    // staging.
    bool sharedOk = false;
    if (decoder->dxvaEnabled) {
        sharedOk = TryCopySampleToSharedTexture(decoder, sample.Get());
    }

    auto status = CopySampleToFrame(decoder, sample.Get());
    if (status != JALIUM_MEDIA_OK) return status;
    (void)sharedOk;  // shared-texture liveness is reported through AcquireGpuDescriptor

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
    // Stop the reader before tearing down the D3D11 device manager — outstanding
    // ReadSample work might still be running on MF's worker threads.
    decoder->reader.Reset();
    // Close NT HANDLE before its texture (handle owns a refcount we add'ed via CreateSharedHandle).
    if (decoder->sharedTextureHandle) {
        CloseHandle(decoder->sharedTextureHandle);
        decoder->sharedTextureHandle = nullptr;
    }
    decoder->sharedTexture.Reset();
    decoder->dxgiDeviceManager.Reset();
    decoder->d3d11Context.Reset();
    decoder->d3d11Device.Reset();
    delete decoder;
}

// Stage 3b.1: hand the shared NT HANDLE for the most-recently-decoded GPU
// frame to the caller. Caller (managed NativeVideoDecoder.AcquireGpuSurface)
// passes the descriptor into jalium_video_surface_wrap_external with
// kind=D3D11_SHARED, which the D3D12 backend translates via OpenSharedHandle
// into an ID3D12Resource. Returns NotImplemented when DXVA is off or the
// shared texture hasn't been written yet (caller falls back to BGRA path).
//
// Note: the NT HANDLE returned here is *not* duplicated — the decoder owns
// it for its lifetime; the consumer (D3D12) only OpenSharedHandle's it and
// must not CloseHandle. This matches the lifetime model the descriptor
// implies (the surface stays valid until the next ReadFrame).
jalium_media_status_t MfVideoDecoderAcquireGpuDescriptor(
    jalium_video_decoder_t*                decoder,
    jalium_video_decoder_gpu_descriptor_t* out_descriptor)
{
    if (!decoder || !out_descriptor) return JALIUM_MEDIA_E_INVALID_ARG;

    out_descriptor->kind        = 0;
    out_descriptor->width       = 0;
    out_descriptor->height      = 0;
    out_descriptor->handle0     = 0;
    out_descriptor->handle1     = 0;
    out_descriptor->format_hint = 0;
    out_descriptor->reserved    = 0;

    if (!decoder->dxvaEnabled || !decoder->sharedTextureValid || !decoder->sharedTextureHandle) {
        return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    }

    // 1 == JALIUM_VS_KIND_D3D11_SHARED (see jalium_video_surface.h).
    out_descriptor->kind        = 1;
    out_descriptor->width       = decoder->width;
    out_descriptor->height      = decoder->height;
    out_descriptor->handle0     = reinterpret_cast<uint64_t>(decoder->sharedTextureHandle);
    out_descriptor->format_hint = 0;  // BGRA8
    return JALIUM_MEDIA_OK;
}

} // namespace jalium::media::win
