#define JALIUM_MEDIA_EXPORTS
#include "jalium_media.h"
#include "jalium_media_internal.h"

#include <cstdlib>
#include <cstdint>

#if defined(_WIN32)
    #include <malloc.h>
#endif

extern "C" {

JALIUM_MEDIA_API const char* jalium_media_status_string(jalium_media_status_t status)
{
    switch (status) {
        case JALIUM_MEDIA_OK:                   return "OK";
        case JALIUM_MEDIA_E_INVALID_ARG:        return "Invalid argument";
        case JALIUM_MEDIA_E_OUT_OF_MEMORY:      return "Out of memory";
        case JALIUM_MEDIA_E_IO:                 return "I/O error";
        case JALIUM_MEDIA_E_UNSUPPORTED_FORMAT: return "Unsupported container or pixel format";
        case JALIUM_MEDIA_E_UNSUPPORTED_CODEC:  return "Unsupported codec";
        case JALIUM_MEDIA_E_DECODE_FAILED:      return "Decode failed";
        case JALIUM_MEDIA_E_END_OF_STREAM:      return "End of stream";
        case JALIUM_MEDIA_E_NOT_INITIALIZED:    return "jalium_media_initialize was not called or failed";
        case JALIUM_MEDIA_E_PLATFORM:           return "Platform error";
        case JALIUM_MEDIA_E_NO_DEVICE:          return "No matching device";
        case JALIUM_MEDIA_E_PERMISSION_DENIED:  return "Permission denied";
        case JALIUM_MEDIA_E_NOT_IMPLEMENTED:    return "Not implemented";
        default:                                return "Unknown status";
    }
}

JALIUM_MEDIA_API void* jalium_media_aligned_alloc(size_t size_bytes)
{
    if (size_bytes == 0) {
        return nullptr;
    }
#if defined(_WIN32)
    return _aligned_malloc(size_bytes, 64);
#else
    void* p = nullptr;
    if (posix_memalign(&p, 64, size_bytes) != 0) {
        return nullptr;
    }
    return p;
#endif
}

JALIUM_MEDIA_API void jalium_media_aligned_free(void* ptr)
{
    if (!ptr) return;
#if defined(_WIN32)
    _aligned_free(ptr);
#else
    free(ptr);
#endif
}

JALIUM_MEDIA_API void jalium_media_swap_rb_inplace(
    uint8_t* pixels,
    uint32_t width,
    uint32_t height,
    uint32_t stride_bytes)
{
    if (!pixels || width == 0 || height == 0 || stride_bytes < width * 4u) {
        return;
    }
    for (uint32_t y = 0; y < height; ++y) {
        uint8_t* row = pixels + static_cast<size_t>(y) * stride_bytes;
        for (uint32_t x = 0; x < width; ++x) {
            uint8_t tmp = row[0];
            row[0] = row[2];
            row[2] = tmp;
            row += 4;
        }
    }
}

} // extern "C"
