#define JALIUM_MEDIA_EXPORTS
#include "and_camera_source.h"
#include "and_media_init.h"
#include "and_yuv_simd.h"
#include "jalium_media_internal.h"

#include <camera/NdkCameraDevice.h>
#include <camera/NdkCameraError.h>
#include <camera/NdkCameraManager.h>
#include <camera/NdkCameraMetadata.h>
#include <camera/NdkCaptureRequest.h>
#include <media/NdkImage.h>
#include <media/NdkImageReader.h>

#include <android/log.h>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <cstring>
#include <map>
#include <mutex>
#include <new>
#include <string>
#include <vector>

#define ANDLOG_TAG "jalium.native.media.camera"
#define ANDLOGI(...) __android_log_print(ANDROID_LOG_INFO, ANDLOG_TAG, __VA_ARGS__)
#define ANDLOGW(...) __android_log_print(ANDROID_LOG_WARN, ANDLOG_TAG, __VA_ARGS__)
#define ANDLOGE(...) __android_log_print(ANDROID_LOG_ERROR, ANDLOG_TAG, __VA_ARGS__)

struct jalium_camera_source {
    ACameraManager*               manager       = nullptr;
    ACameraDevice*                device        = nullptr;
    AImageReader*                 reader        = nullptr;
    ACaptureSessionOutput*        sessionOutput = nullptr;
    ACaptureSessionOutputContainer* sessionContainer = nullptr;
    ACameraCaptureSession*        session       = nullptr;
    ACaptureRequest*              request       = nullptr;
    ACameraOutputTarget*          outputTarget  = nullptr;

    ANativeWindow*                window        = nullptr;

    uint32_t                      width         = 0;
    uint32_t                      height        = 0;
    uint32_t                      stride_bytes  = 0;
    jalium_pixel_format_t         format        = JALIUM_PF_BGRA8;

    uint8_t*                      frame_buffer       = nullptr;
    size_t                        frame_buffer_size  = 0;
    int64_t                       last_pts_us        = 0;

    std::mutex                    mtx;
    std::condition_variable       cv;
    std::atomic<bool>             frameReady{false};
    std::atomic<bool>             closed{false};
};

namespace jalium::media::android {

namespace {

void OnImageAvailable(void* context, AImageReader* /*reader*/)
{
    auto* src = static_cast<jalium_camera_source_t*>(context);
    if (!src || src->closed.load()) return;
    src->frameReady.store(true);
    src->cv.notify_all();
}

void OnDeviceDisconnected(void* /*context*/, ACameraDevice* /*device*/)
{
    ANDLOGW("Camera device disconnected");
}

void OnDeviceError(void* /*context*/, ACameraDevice* /*device*/, int err)
{
    ANDLOGE("Camera device error: %d", err);
}

void OnSessionActive(void* /*context*/, ACameraCaptureSession* /*session*/)         {}
void OnSessionReady(void*  /*context*/, ACameraCaptureSession* /*session*/)         {}
void OnSessionClosed(void* /*context*/, ACameraCaptureSession* /*session*/)         {}

jalium_camera_facing_t MapFacing(uint8_t facing)
{
    switch (facing) {
        case 0: return JALIUM_CAMERA_FACING_FRONT;     // ACAMERA_LENS_FACING_FRONT
        case 1: return JALIUM_CAMERA_FACING_BACK;      // ACAMERA_LENS_FACING_BACK
        case 2: return JALIUM_CAMERA_FACING_EXTERNAL;  // ACAMERA_LENS_FACING_EXTERNAL
        default: return JALIUM_CAMERA_FACING_UNKNOWN;
    }
}

struct DeviceEnumOwner {
    std::vector<std::string>                ids;
    std::vector<std::string>                names;
    std::vector<std::vector<jalium_camera_format_t>> formats;
};

void CollectSupportedFormats(ACameraMetadata* meta, std::vector<jalium_camera_format_t>& out)
{
    ACameraMetadata_const_entry entry{};
    if (ACameraMetadata_getConstEntry(meta, ACAMERA_SCALER_AVAILABLE_STREAM_CONFIGURATIONS, &entry) != ACAMERA_OK) {
        return;
    }
    // Each entry is a 4-tuple: [format, width, height, input_or_output]. We only want OUTPUT (=0)
    // entries with format == AIMAGE_FORMAT_YUV_420_888 (0x23).
    constexpr int32_t YUV_420_888 = 0x23;
    for (uint32_t i = 0; i + 4 <= entry.count; i += 4) {
        int32_t fmt    = entry.data.i32[i + 0];
        int32_t w      = entry.data.i32[i + 1];
        int32_t h      = entry.data.i32[i + 2];
        int32_t output = entry.data.i32[i + 3];  // 0 = output, 1 = input
        if (fmt == YUV_420_888 && output == 0 && w > 0 && h > 0) {
            out.push_back({static_cast<uint32_t>(w), static_cast<uint32_t>(h), 30.0});
        }
    }
}

std::mutex                                    g_enumOwnerMutex;
std::map<jalium_camera_device_t*, DeviceEnumOwner*> g_enumOwners;

} // anonymous

jalium_media_status_t CameraEnumerate(
    jalium_camera_device_t** out_devices,
    uint32_t*                out_count)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (out_devices) *out_devices = nullptr;
    if (out_count) *out_count = 0;

    ACameraManager* mgr = ACameraManager_create();
    if (!mgr) return JALIUM_MEDIA_E_PLATFORM;

    ACameraIdList* idList = nullptr;
    if (ACameraManager_getCameraIdList(mgr, &idList) != ACAMERA_OK || !idList) {
        ACameraManager_delete(mgr);
        return JALIUM_MEDIA_E_PLATFORM;
    }
    if (idList->numCameras == 0) {
        ACameraManager_deleteCameraIdList(idList);
        ACameraManager_delete(mgr);
        return JALIUM_MEDIA_OK;
    }

    auto* owner = new (std::nothrow) DeviceEnumOwner();
    auto* devs  = new (std::nothrow) jalium_camera_device_t[idList->numCameras];
    if (!owner || !devs) {
        delete owner;
        delete[] devs;
        ACameraManager_deleteCameraIdList(idList);
        ACameraManager_delete(mgr);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    owner->ids.reserve(idList->numCameras);
    owner->names.reserve(idList->numCameras);
    owner->formats.resize(idList->numCameras);

    for (int i = 0; i < idList->numCameras; ++i) {
        const char* id = idList->cameraIds[i];
        owner->ids.emplace_back(id);
        owner->names.emplace_back("Camera ");
        owner->names.back() += id;

        ACameraMetadata* meta = nullptr;
        if (ACameraManager_getCameraCharacteristics(mgr, id, &meta) == ACAMERA_OK && meta) {
            ACameraMetadata_const_entry facingEntry{};
            uint8_t facing = 255;
            if (ACameraMetadata_getConstEntry(meta, ACAMERA_LENS_FACING, &facingEntry) == ACAMERA_OK
                && facingEntry.count > 0) {
                facing = facingEntry.data.u8[0];
            }
            CollectSupportedFormats(meta, owner->formats[i]);
            ACameraMetadata_free(meta);

            devs[i].facing = MapFacing(facing);
        } else {
            devs[i].facing = JALIUM_CAMERA_FACING_UNKNOWN;
        }

        devs[i].id            = owner->ids[i].c_str();
        devs[i].friendly_name = owner->names[i].c_str();
        devs[i].format_count  = static_cast<uint32_t>(owner->formats[i].size());
        devs[i].formats       = owner->formats[i].empty() ? nullptr : owner->formats[i].data();
    }

    *out_devices = devs;
    *out_count   = static_cast<uint32_t>(idList->numCameras);

    {
        std::lock_guard<std::mutex> lk(g_enumOwnerMutex);
        g_enumOwners[devs] = owner;
    }

    ACameraManager_deleteCameraIdList(idList);
    ACameraManager_delete(mgr);
    return JALIUM_MEDIA_OK;
}

void CameraDevicesFree(jalium_camera_device_t* devices, uint32_t /*count*/)
{
    if (!devices) return;
    DeviceEnumOwner* owner = nullptr;
    {
        std::lock_guard<std::mutex> lk(g_enumOwnerMutex);
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

// Pick the supported format closest to the request.
bool SelectBestFormat(ACameraMetadata* meta,
                      uint32_t reqW, uint32_t reqH,
                      uint32_t* outW, uint32_t* outH)
{
    constexpr int32_t YUV_420_888 = 0x23;
    ACameraMetadata_const_entry entry{};
    if (ACameraMetadata_getConstEntry(meta, ACAMERA_SCALER_AVAILABLE_STREAM_CONFIGURATIONS, &entry) != ACAMERA_OK) {
        return false;
    }

    int64_t bestScore = INT64_MAX;
    bool found = false;
    for (uint32_t i = 0; i + 4 <= entry.count; i += 4) {
        int32_t fmt    = entry.data.i32[i + 0];
        int32_t w      = entry.data.i32[i + 1];
        int32_t h      = entry.data.i32[i + 2];
        int32_t output = entry.data.i32[i + 3];
        if (fmt != YUV_420_888 || output != 0 || w <= 0 || h <= 0) continue;
        int64_t score = std::abs(static_cast<int64_t>(w) - static_cast<int64_t>(reqW)) +
                        std::abs(static_cast<int64_t>(h) - static_cast<int64_t>(reqH));
        if (score < bestScore) {
            bestScore = score;
            *outW = static_cast<uint32_t>(w);
            *outH = static_cast<uint32_t>(h);
            found = true;
        }
    }
    return found;
}

} // anonymous

jalium_media_status_t CameraOpen(
    const char*              device_id,
    uint32_t                 requested_width,
    uint32_t                 requested_height,
    double                   /*requested_fps*/,
    jalium_pixel_format_t    requested_format,
    jalium_camera_source_t** out_source)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!device_id || !out_source) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_source = nullptr;

    auto* src = new (std::nothrow) jalium_camera_source();
    if (!src) return JALIUM_MEDIA_E_OUT_OF_MEMORY;

    src->manager = ACameraManager_create();
    if (!src->manager) {
        CameraClose(src);
        return JALIUM_MEDIA_E_PLATFORM;
    }

    ACameraMetadata* meta = nullptr;
    if (ACameraManager_getCameraCharacteristics(src->manager, device_id, &meta) != ACAMERA_OK || !meta) {
        CameraClose(src);
        return JALIUM_MEDIA_E_NO_DEVICE;
    }
    uint32_t selW = requested_width, selH = requested_height;
    if (!SelectBestFormat(meta, requested_width, requested_height, &selW, &selH)) {
        ACameraMetadata_free(meta);
        CameraClose(src);
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }
    ACameraMetadata_free(meta);

    src->width        = selW;
    src->height       = selH;
    src->stride_bytes = jalium_media_compute_stride(selW);
    src->format       = requested_format;

    constexpr int32_t YUV_420_888 = 0x23;
    if (AImageReader_new(static_cast<int32_t>(selW), static_cast<int32_t>(selH),
                         YUV_420_888, /*maxImages*/ 4, &src->reader) != AMEDIA_OK || !src->reader) {
        CameraClose(src);
        return JALIUM_MEDIA_E_PLATFORM;
    }

    AImageReader_ImageListener listener{ src, &OnImageAvailable };
    AImageReader_setImageListener(src->reader, &listener);

    if (AImageReader_getWindow(src->reader, &src->window) != AMEDIA_OK || !src->window) {
        CameraClose(src);
        return JALIUM_MEDIA_E_PLATFORM;
    }

    ACameraDevice_StateCallbacks devCallbacks{ src, &OnDeviceDisconnected, &OnDeviceError };
    if (ACameraManager_openCamera(src->manager, device_id, &devCallbacks, &src->device) != ACAMERA_OK) {
        CameraClose(src);
        return JALIUM_MEDIA_E_PERMISSION_DENIED;
    }

    if (ACameraOutputTarget_create(src->window, &src->outputTarget) != ACAMERA_OK ||
        ACameraDevice_createCaptureRequest(src->device, TEMPLATE_PREVIEW, &src->request) != ACAMERA_OK ||
        ACaptureRequest_addTarget(src->request, src->outputTarget) != ACAMERA_OK ||
        ACaptureSessionOutput_create(src->window, &src->sessionOutput) != ACAMERA_OK ||
        ACaptureSessionOutputContainer_create(&src->sessionContainer) != ACAMERA_OK ||
        ACaptureSessionOutputContainer_add(src->sessionContainer, src->sessionOutput) != ACAMERA_OK)
    {
        CameraClose(src);
        return JALIUM_MEDIA_E_PLATFORM;
    }

    ACameraCaptureSession_stateCallbacks sessCallbacks{ src, &OnSessionClosed, &OnSessionReady, &OnSessionActive };
    if (ACameraDevice_createCaptureSession(src->device, src->sessionContainer, &sessCallbacks, &src->session) != ACAMERA_OK) {
        CameraClose(src);
        return JALIUM_MEDIA_E_PLATFORM;
    }

    if (ACameraCaptureSession_setRepeatingRequest(src->session, nullptr, 1, &src->request, nullptr) != ACAMERA_OK) {
        CameraClose(src);
        return JALIUM_MEDIA_E_PLATFORM;
    }

    *out_source = src;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t CameraReadFrame(
    jalium_camera_source_t* src,
    jalium_video_frame_t*   out_frame)
{
    if (!src || !src->reader || !out_frame) return JALIUM_MEDIA_E_INVALID_ARG;
    if (src->closed.load()) return JALIUM_MEDIA_E_END_OF_STREAM;

    {
        std::unique_lock<std::mutex> lk(src->mtx);
        src->cv.wait_for(lk, std::chrono::milliseconds(500),
                         [&] { return src->frameReady.load() || src->closed.load(); });
        src->frameReady.store(false);
    }
    if (src->closed.load()) return JALIUM_MEDIA_E_END_OF_STREAM;

    AImage* img = nullptr;
    media_status_t rc = AImageReader_acquireLatestImage(src->reader, &img);
    if (rc == AMEDIA_IMGREADER_NO_BUFFER_AVAILABLE) return JALIUM_MEDIA_E_DECODE_FAILED;
    if (rc != AMEDIA_OK || !img) return JALIUM_MEDIA_E_DECODE_FAILED;

    int32_t imgW = 0, imgH = 0;
    AImage_getWidth(img, &imgW);
    AImage_getHeight(img, &imgH);
    if (imgW > 0 && imgH > 0 &&
        (static_cast<uint32_t>(imgW) != src->width || static_cast<uint32_t>(imgH) != src->height)) {
        src->width = static_cast<uint32_t>(imgW);
        src->height = static_cast<uint32_t>(imgH);
        src->stride_bytes = jalium_media_compute_stride(src->width);
    }

    const size_t needed = static_cast<size_t>(src->stride_bytes) * src->height;
    if (src->frame_buffer_size < needed) {
        if (src->frame_buffer) jalium_media_aligned_free(src->frame_buffer);
        src->frame_buffer = static_cast<uint8_t*>(jalium_media_aligned_alloc(needed));
        if (!src->frame_buffer) {
            AImage_delete(img);
            return JALIUM_MEDIA_E_OUT_OF_MEMORY;
        }
        src->frame_buffer_size = needed;
    }

    // Read planes (Y, U, V) — same layout as MediaCodec output.
    uint8_t *yPlane = nullptr, *uPlane = nullptr, *vPlane = nullptr;
    int32_t  yLen = 0, uLen = 0, vLen = 0;
    int32_t  yRowStride = 0, uRowStride = 0, vRowStride = 0;
    int32_t  uPixelStride = 0, vPixelStride = 0;

    AImage_getPlaneData(img, 0, &yPlane, &yLen);
    AImage_getPlaneRowStride(img, 0, &yRowStride);
    AImage_getPlaneData(img, 1, &uPlane, &uLen);
    AImage_getPlaneRowStride(img, 1, &uRowStride);
    AImage_getPlanePixelStride(img, 1, &uPixelStride);
    AImage_getPlaneData(img, 2, &vPlane, &vLen);
    AImage_getPlaneRowStride(img, 2, &vRowStride);
    AImage_getPlanePixelStride(img, 2, &vPixelStride);

    auto matrix = (src->height <= 576) ? ColorMatrix::Bt601 : ColorMatrix::Bt709;

    if (uPixelStride == 2 && vPixelStride == 2) {
        if (uPlane > vPlane) {
            NV21ToBgra(yPlane, static_cast<uint32_t>(yRowStride),
                       vPlane, static_cast<uint32_t>(vRowStride),
                       src->frame_buffer, src->stride_bytes,
                       src->width, src->height, matrix, src->format);
        } else {
            NV12ToBgra(yPlane, static_cast<uint32_t>(yRowStride),
                       uPlane, static_cast<uint32_t>(uRowStride),
                       src->frame_buffer, src->stride_bytes,
                       src->width, src->height, matrix, src->format);
        }
    } else {
        I420ToBgra(yPlane, static_cast<uint32_t>(yRowStride),
                   uPlane, static_cast<uint32_t>(uRowStride),
                   vPlane, static_cast<uint32_t>(vRowStride),
                   src->frame_buffer, src->stride_bytes,
                   src->width, src->height, matrix, src->format);
    }

    int64_t timestamp = 0;
    AImage_getTimestamp(img, &timestamp);
    src->last_pts_us = timestamp / 1000;  // ns → µs

    AImage_delete(img);

    out_frame->width        = src->width;
    out_frame->height       = src->height;
    out_frame->stride_bytes = src->stride_bytes;
    out_frame->format       = src->format;
    out_frame->pixels       = src->frame_buffer;
    out_frame->pts_microseconds = src->last_pts_us;
    out_frame->is_keyframe  = 1;
    return JALIUM_MEDIA_OK;
}

void CameraClose(jalium_camera_source_t* src)
{
    if (!src) return;
    src->closed.store(true);
    src->cv.notify_all();

    if (src->session) {
        ACameraCaptureSession_stopRepeating(src->session);
        ACameraCaptureSession_close(src->session);
        src->session = nullptr;
    }
    if (src->sessionContainer) {
        ACaptureSessionOutputContainer_free(src->sessionContainer);
        src->sessionContainer = nullptr;
    }
    if (src->sessionOutput) {
        ACaptureSessionOutput_free(src->sessionOutput);
        src->sessionOutput = nullptr;
    }
    if (src->request) {
        ACaptureRequest_free(src->request);
        src->request = nullptr;
    }
    if (src->outputTarget) {
        ACameraOutputTarget_free(src->outputTarget);
        src->outputTarget = nullptr;
    }
    if (src->device) {
        ACameraDevice_close(src->device);
        src->device = nullptr;
    }
    if (src->reader) {
        AImageReader_setImageListener(src->reader, nullptr);
        AImageReader_delete(src->reader);
        src->reader = nullptr;
        src->window = nullptr;  // owned by the reader
    }
    if (src->manager) {
        ACameraManager_delete(src->manager);
        src->manager = nullptr;
    }
    if (src->frame_buffer) {
        jalium_media_aligned_free(src->frame_buffer);
        src->frame_buffer = nullptr;
        src->frame_buffer_size = 0;
    }
    delete src;
}

} // namespace jalium::media::android
