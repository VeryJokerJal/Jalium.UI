#include "d3d12_resources.h"

namespace jalium {

// ============================================================================
// D3D12SolidBrush
// ============================================================================

D3D12SolidBrush::D3D12SolidBrush(float r, float g, float b, float a)
    : r_(r), g_(g), b_(b), a_(a)
{}

ID2D1SolidColorBrush* D3D12SolidBrush::GetOrCreateBrush(ID2D1DeviceContext* context) {
    if (!context) return nullptr;

    // Recreate brush if context changed
    if (context != lastContext_ || !brush_) {
        brush_.Reset();
        HRESULT hr = context->CreateSolidColorBrush(
            D2D1::ColorF(r_, g_, b_, a_),
            &brush_);
        if (FAILED(hr)) return nullptr;
        lastContext_ = context;
    }

    return brush_.Get();
}

// ============================================================================
// D3D12LinearGradientBrush
// ============================================================================

D3D12LinearGradientBrush::D3D12LinearGradientBrush(
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops, uint32_t stopCount)
    : startX_(startX), startY_(startY), endX_(endX), endY_(endY)
{
    stops_.reserve(stopCount);
    for (uint32_t i = 0; i < stopCount; ++i) {
        D2D1_GRADIENT_STOP stop;
        stop.position = stops[i].position;
        stop.color = D2D1::ColorF(stops[i].r, stops[i].g, stops[i].b, stops[i].a);
        stops_.push_back(stop);
    }
}

ID2D1LinearGradientBrush* D3D12LinearGradientBrush::GetOrCreateBrush(ID2D1DeviceContext* context) {
    if (!context) return nullptr;

    // Recreate brush if context changed
    if (context != lastContext_ || !brush_) {
        brush_.Reset();
        stopCollection_.Reset();

        // Create gradient stop collection
        HRESULT hr = context->CreateGradientStopCollection(
            stops_.data(),
            static_cast<UINT32>(stops_.size()),
            D2D1_GAMMA_2_2,
            D2D1_EXTEND_MODE_CLAMP,
            &stopCollection_);
        if (FAILED(hr)) return nullptr;

        // Create linear gradient brush
        D2D1_LINEAR_GRADIENT_BRUSH_PROPERTIES props = {};
        props.startPoint = D2D1::Point2F(startX_, startY_);
        props.endPoint = D2D1::Point2F(endX_, endY_);

        hr = context->CreateLinearGradientBrush(
            props,
            stopCollection_.Get(),
            &brush_);
        if (FAILED(hr)) return nullptr;

        lastContext_ = context;
    }

    return brush_.Get();
}

// ============================================================================
// D3D12RadialGradientBrush
// ============================================================================

D3D12RadialGradientBrush::D3D12RadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount)
    : centerX_(centerX), centerY_(centerY), radiusX_(radiusX), radiusY_(radiusY),
      originX_(originX), originY_(originY)
{
    stops_.reserve(stopCount);
    for (uint32_t i = 0; i < stopCount; ++i) {
        D2D1_GRADIENT_STOP stop;
        stop.position = stops[i].position;
        stop.color = D2D1::ColorF(stops[i].r, stops[i].g, stops[i].b, stops[i].a);
        stops_.push_back(stop);
    }
}

ID2D1RadialGradientBrush* D3D12RadialGradientBrush::GetOrCreateBrush(ID2D1DeviceContext* context) {
    if (!context) return nullptr;

    // Recreate brush if context changed
    if (context != lastContext_ || !brush_) {
        brush_.Reset();
        stopCollection_.Reset();

        // Create gradient stop collection
        HRESULT hr = context->CreateGradientStopCollection(
            stops_.data(),
            static_cast<UINT32>(stops_.size()),
            D2D1_GAMMA_2_2,
            D2D1_EXTEND_MODE_CLAMP,
            &stopCollection_);
        if (FAILED(hr)) return nullptr;

        // Create radial gradient brush
        D2D1_RADIAL_GRADIENT_BRUSH_PROPERTIES props = {};
        props.center = D2D1::Point2F(centerX_, centerY_);
        props.gradientOriginOffset = D2D1::Point2F(originX_ - centerX_, originY_ - centerY_);
        props.radiusX = radiusX_;
        props.radiusY = radiusY_;

        hr = context->CreateRadialGradientBrush(
            props,
            stopCollection_.Get(),
            &brush_);
        if (FAILED(hr)) return nullptr;

        lastContext_ = context;
    }

    return brush_.Get();
}

} // namespace jalium
