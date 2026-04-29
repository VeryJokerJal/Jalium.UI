#define JALIUM_MEDIA_EXPORTS
#include "and_image_decoder.h"
#include "and_media_init.h"
#include "jalium_media_internal.h"

#include <android/imagedecoder.h>
#include <android/log.h>
#include <fcntl.h>
#include <unistd.h>

#include <cstring>
#include <vector>

#define ANDLOG_TAG "jalium.native.media.image"
#define ANDLOGW(...) __android_log_print(ANDROID_LOG_WARN, ANDLOG_TAG, __VA_ARGS__)

namespace jalium::media::android {

namespace {

constexpr int API_LEVEL_AIMAGEDECODER = 30;

// Convert RGBA -> BGRA in place using the shared helper.
void MaybeSwapToBgra(uint8_t* pixels, uint32_t width, uint32_t height, uint32_t stride,
                    jalium_pixel_format_t requested)
{
    if (requested == JALIUM_PF_BGRA8) {
        // AImageDecoder always emits RGBA8 — swap R and B channels.
        jalium_media_swap_rb_inplace(pixels, width, height, stride);
    }
    // requested == JALIUM_PF_RGBA8 → keep as-is.
}

jalium_media_status_t TranslateDecoderResult(int code)
{
    switch (code) {
        case ANDROID_IMAGE_DECODER_SUCCESS:               return JALIUM_MEDIA_OK;
        case ANDROID_IMAGE_DECODER_INCOMPLETE:            return JALIUM_MEDIA_E_DECODE_FAILED;
        case ANDROID_IMAGE_DECODER_ERROR:                 return JALIUM_MEDIA_E_DECODE_FAILED;
        case ANDROID_IMAGE_DECODER_INVALID_CONVERSION:    return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
        case ANDROID_IMAGE_DECODER_INVALID_SCALE:         return JALIUM_MEDIA_E_INVALID_ARG;
        case ANDROID_IMAGE_DECODER_BAD_PARAMETER:         return JALIUM_MEDIA_E_INVALID_ARG;
        case ANDROID_IMAGE_DECODER_INVALID_INPUT:         return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
        case ANDROID_IMAGE_DECODER_SEEK_ERROR:            return JALIUM_MEDIA_E_IO;
        case ANDROID_IMAGE_DECODER_INTERNAL_ERROR:        return JALIUM_MEDIA_E_PLATFORM;
        case ANDROID_IMAGE_DECODER_UNSUPPORTED_FORMAT:    return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
        default:                                          return JALIUM_MEDIA_E_PLATFORM;
    }
}

jalium_media_status_t DecodeWithDecoder(
    AImageDecoder*        decoder,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image)
{
    int rc = AImageDecoder_setAndroidBitmapFormat(decoder, ANDROID_BITMAP_FORMAT_RGBA_8888);
    if (rc != ANDROID_IMAGE_DECODER_SUCCESS) {
        return TranslateDecoderResult(rc);
    }

    const AImageDecoderHeaderInfo* header = AImageDecoder_getHeaderInfo(decoder);
    int32_t width  = AImageDecoderHeaderInfo_getWidth(header);
    int32_t height = AImageDecoderHeaderInfo_getHeight(header);
    if (width <= 0 || height <= 0) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    size_t stride = AImageDecoder_getMinimumStride(decoder);
    if (stride < static_cast<size_t>(width) * 4u) {
        stride = static_cast<size_t>(width) * 4u;
    }
    size_t buffer_size = stride * static_cast<size_t>(height);

    auto* pixels = static_cast<uint8_t*>(jalium_media_aligned_alloc(buffer_size));
    if (!pixels) return JALIUM_MEDIA_E_OUT_OF_MEMORY;

    rc = AImageDecoder_decodeImage(decoder, pixels, stride, buffer_size);
    if (rc != ANDROID_IMAGE_DECODER_SUCCESS) {
        jalium_media_aligned_free(pixels);
        return TranslateDecoderResult(rc);
    }

    MaybeSwapToBgra(pixels, static_cast<uint32_t>(width), static_cast<uint32_t>(height),
                    static_cast<uint32_t>(stride), requested_format);

    out_image->width        = static_cast<uint32_t>(width);
    out_image->height       = static_cast<uint32_t>(height);
    out_image->stride_bytes = static_cast<uint32_t>(stride);
    out_image->format       = requested_format;
    out_image->pixels       = pixels;
    out_image->_reserved    = nullptr;
    return JALIUM_MEDIA_OK;
}

} // anonymous

jalium_media_status_t DecodeImageMemory(
    const uint8_t*        data,
    size_t                size,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!data || size == 0 || !out_image) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_image = {};

    if (GetApiLevel() < API_LEVEL_AIMAGEDECODER) {
        // API 24-29 → JNI BitmapFactory fallback.
        return DecodeImageMemoryViaJni(data, size, requested_format, out_image);
    }

    AImageDecoder* decoder = nullptr;
    int rc = AImageDecoder_createFromBuffer(data, size, &decoder);
    if (rc != ANDROID_IMAGE_DECODER_SUCCESS) {
        return TranslateDecoderResult(rc);
    }
    auto status = DecodeWithDecoder(decoder, requested_format, out_image);
    AImageDecoder_delete(decoder);
    return status;
}

jalium_media_status_t DecodeImageFile(
    const char*           utf8_path,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!utf8_path || !out_image) return JALIUM_MEDIA_E_INVALID_ARG;
    *out_image = {};

    int fd = open(utf8_path, O_RDONLY | O_CLOEXEC);
    if (fd < 0) return JALIUM_MEDIA_E_IO;

    if (GetApiLevel() < API_LEVEL_AIMAGEDECODER) {
        // API 24-29 → JNI BitmapFactory fallback expects a memory blob.
        // Read the file into memory and dispatch.
        off_t size = lseek(fd, 0, SEEK_END);
        if (size <= 0 || size > 256 * 1024 * 1024) {
            close(fd);
            return JALIUM_MEDIA_E_IO;
        }
        lseek(fd, 0, SEEK_SET);
        std::vector<uint8_t> blob(static_cast<size_t>(size));
        ssize_t total = 0;
        while (total < size) {
            ssize_t n = read(fd, blob.data() + total, static_cast<size_t>(size - total));
            if (n <= 0) {
                close(fd);
                return JALIUM_MEDIA_E_IO;
            }
            total += n;
        }
        close(fd);
        return DecodeImageMemoryViaJni(blob.data(), blob.size(), requested_format, out_image);
    }

    AImageDecoder* decoder = nullptr;
    int rc = AImageDecoder_createFromFd(fd, &decoder);
    // AImageDecoder takes ownership of the fd on success.
    if (rc != ANDROID_IMAGE_DECODER_SUCCESS) {
        close(fd);
        return TranslateDecoderResult(rc);
    }
    auto status = DecodeWithDecoder(decoder, requested_format, out_image);
    AImageDecoder_delete(decoder);
    return status;
}

jalium_media_status_t ReadImageDimensions(
    const uint8_t* data,
    size_t         size,
    uint32_t*      out_width,
    uint32_t*      out_height)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!data || size == 0 || !out_width || !out_height) return JALIUM_MEDIA_E_INVALID_ARG;

    *out_width = 0;
    *out_height = 0;

    if (GetApiLevel() < API_LEVEL_AIMAGEDECODER) {
        // For API < 30 we bounce via the JNI path (BitmapFactory.Options.inJustDecodeBounds
        // would be more efficient — left for a follow-up commit).
        jalium_image_t image{};
        auto status = DecodeImageMemoryViaJni(data, size, JALIUM_PF_BGRA8, &image);
        if (status == JALIUM_MEDIA_OK) {
            *out_width = image.width;
            *out_height = image.height;
            jalium_media_aligned_free(image.pixels);
        }
        return status;
    }

    AImageDecoder* decoder = nullptr;
    int rc = AImageDecoder_createFromBuffer(data, size, &decoder);
    if (rc != ANDROID_IMAGE_DECODER_SUCCESS) {
        return TranslateDecoderResult(rc);
    }
    const AImageDecoderHeaderInfo* header = AImageDecoder_getHeaderInfo(decoder);
    int32_t w = AImageDecoderHeaderInfo_getWidth(header);
    int32_t h = AImageDecoderHeaderInfo_getHeight(header);
    AImageDecoder_delete(decoder);

    if (w <= 0 || h <= 0) return JALIUM_MEDIA_E_DECODE_FAILED;
    *out_width = static_cast<uint32_t>(w);
    *out_height = static_cast<uint32_t>(h);
    return JALIUM_MEDIA_OK;
}

} // namespace jalium::media::android
