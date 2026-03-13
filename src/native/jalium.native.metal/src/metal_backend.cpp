#include "metal_backend.h"

namespace jalium {

RenderTarget* MetalBackend::CreateRenderTarget(void* hwnd, int32_t width, int32_t height)
{
    (void)hwnd;
    (void)width;
    (void)height;
    return nullptr;
}

RenderTarget* MetalBackend::CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height)
{
    (void)hwnd;
    (void)width;
    (void)height;
    return nullptr;
}

Brush* MetalBackend::CreateSolidBrush(float r, float g, float b, float a)
{
    (void)r;
    (void)g;
    (void)b;
    (void)a;
    return nullptr;
}

Brush* MetalBackend::CreateLinearGradientBrush(
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops, uint32_t stopCount)
{
    (void)startX;
    (void)startY;
    (void)endX;
    (void)endY;
    (void)stops;
    (void)stopCount;
    return nullptr;
}

Brush* MetalBackend::CreateRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount)
{
    (void)centerX;
    (void)centerY;
    (void)radiusX;
    (void)radiusY;
    (void)originX;
    (void)originY;
    (void)stops;
    (void)stopCount;
    return nullptr;
}

TextFormat* MetalBackend::CreateTextFormat(
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
{
    (void)fontFamily;
    (void)fontSize;
    (void)fontWeight;
    (void)fontStyle;
    return nullptr;
}

Bitmap* MetalBackend::CreateBitmapFromMemory(const uint8_t* data, uint32_t dataSize)
{
    (void)data;
    (void)dataSize;
    return nullptr;
}

IRenderBackend* CreateMetalBackend()
{
    return new MetalBackend();
}

} // namespace jalium
