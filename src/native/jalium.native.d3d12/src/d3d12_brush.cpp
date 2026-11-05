#include "jalium_color_interpolation.h"
#include "d3d12_resources.h"

namespace jalium {

JaliumResult D3D12LinearGradientBrush::SetColorInterpolationMode(uint32_t mode) {
    if (mode > 1) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (interpolationSource_.empty()) {
        for (const auto& stop : stops_) interpolationSource_.push_back({stop.position,stop.color.r,stop.color.g,stop.color.b,stop.color.a});
    }
    auto prepared=PrepareSrgbGradientStops(interpolationSource_,mode);
    std::vector<GradStop> result; result.reserve(prepared.size());
    for (const auto& stop : prepared) result.push_back({stop.position,{stop.r,stop.g,stop.b,stop.a}});
    stops_.swap(result);
    colorInterpolationMode=mode; return JALIUM_OK;
}

JaliumResult D3D12RadialGradientBrush::SetColorInterpolationMode(uint32_t mode) {
    if (mode > 1) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (interpolationSource_.empty()) {
        for (const auto& stop : stops_) interpolationSource_.push_back({stop.position,stop.color.r,stop.color.g,stop.color.b,stop.color.a});
    }
    auto prepared=PrepareSrgbGradientStops(interpolationSource_,mode);
    std::vector<GradStop> result; result.reserve(prepared.size());
    for (const auto& stop : prepared) result.push_back({stop.position,{stop.r,stop.g,stop.b,stop.a}});
    stops_.swap(result);
    colorInterpolationMode=mode; return JALIUM_OK;
}


// ============================================================================
// D3D12SolidBrush
// ============================================================================

D3D12SolidBrush::D3D12SolidBrush(float r, float g, float b, float a)
    : r_(r), g_(g), b_(b), a_(a)
{}

// ============================================================================
// D3D12LinearGradientBrush
// ============================================================================

D3D12LinearGradientBrush::D3D12LinearGradientBrush(
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops, uint32_t stopCount)
    : startX_(startX), startY_(startY), endX_(endX), endY_(endY)
{
    if (!stops) stopCount = 0;
    stops_.reserve(stopCount);
    for (uint32_t i = 0; i < stopCount; ++i) {
        GradStop stop;
        stop.position = stops[i].position;
        stop.color = { stops[i].r, stops[i].g, stops[i].b, stops[i].a };
        stops_.push_back(stop);
    }
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
    if (!stops) stopCount = 0;
    stops_.reserve(stopCount);
    for (uint32_t i = 0; i < stopCount; ++i) {
        GradStop stop;
        stop.position = stops[i].position;
        stop.color = { stops[i].r, stops[i].g, stops[i].b, stops[i].a };
        stops_.push_back(stop);
    }
}

} // namespace jalium
