#include "jalium_internal.h"

// ============================================================================
// Bitmap C API Implementation
// ============================================================================

extern "C" {

JALIUM_API JaliumImage* jalium_bitmap_create_from_memory(
    JaliumContext* ctx,
    const uint8_t* data,
    uint32_t dataSize)
{
    if (!ctx || !data || dataSize == 0) {
        return nullptr;
    }

    auto backend = jalium::GetBackendFromContext(ctx);
    if (!backend) {
        return nullptr;
    }

    auto bitmap = backend->CreateBitmapFromMemory(data, dataSize);
    return reinterpret_cast<JaliumImage*>(bitmap);
}

JALIUM_API JaliumImage* jalium_bitmap_create_from_pixels(
    JaliumContext* ctx,
    const uint8_t* pixels,
    uint32_t width,
    uint32_t height,
    uint32_t stride)
{
    if (!ctx || !pixels || width == 0 || height == 0 || stride < width * 4u) {
        return nullptr;
    }

    auto backend = jalium::GetBackendFromContext(ctx);
    if (!backend) {
        return nullptr;
    }

    auto bitmap = backend->CreateBitmapFromPixels(pixels, width, height, stride);
    return reinterpret_cast<JaliumImage*>(bitmap);
}

JALIUM_API uint32_t jalium_bitmap_get_width(JaliumImage* bitmap) {
    if (!bitmap) return 0;
    return reinterpret_cast<jalium::Bitmap*>(bitmap)->GetWidth();
}

JALIUM_API uint32_t jalium_bitmap_get_height(JaliumImage* bitmap) {
    if (!bitmap) return 0;
    return reinterpret_cast<jalium::Bitmap*>(bitmap)->GetHeight();
}

JALIUM_API void jalium_bitmap_destroy(JaliumImage* bitmap) {
    if (bitmap) {
        delete reinterpret_cast<jalium::Bitmap*>(bitmap);
    }
}

} // extern "C"
