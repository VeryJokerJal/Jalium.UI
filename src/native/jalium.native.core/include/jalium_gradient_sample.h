#pragma once

// jalium_gradient_sample.h
//
// Per-vertex gradient color sampling shared by every backend (D3D12 / Vulkan)
// and every engine (Vello / Impeller). The Impeller-style engines need to bake
// a color into each tessellated vertex; Vello-style engines need to sample a
// reference color when CPU-expanding strokes through the same code path.
//
// SampleLinearGradient / SampleRadialGradient already live in
// jalium_triangulate.h — included here so callers only need this one header.
// SampleSweepGradient + SampleBrushGradient (the brush.type dispatcher) are
// new additions so D3D12 and Vulkan share one implementation.
//
// Stop layout matches the rest of the engine: the caller flattens
// EngineBrushData::stops into a `[pos, r, g, b, a]` interleaved float array.
// SampleBrushGradient does that flatten-then-dispatch in one call so the
// caller does not have to allocate a temp buffer per vertex.

#include "jalium_rendering_engine.h"
#include "jalium_gradient_transform.h"
#include "jalium_radial_geometry.h"
#include "jalium_triangulate.h"
#include <cmath>
#include <cstdint>
#include <vector>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace jalium {

// ---------------------------------------------------------------------------
// SampleSweepGradient — angle-around-center → t, then standard stop lookup.
//
// brush.startX / brush.endX are repurposed as start / end angles (radians)
// when both are non-zero; otherwise the full 2π circle is used. This matches
// the convention adopted by the D3D12 Impeller engine for its sweep brush.
// ---------------------------------------------------------------------------
inline GradientColor SampleSweepGradient(float px, float py,
                                         float centerX, float centerY,
                                         float startAngle, float endAngle,
                                         const float* stops, uint32_t stopCount, uint32_t colorInterpolationMode = 1) {
    if (stopCount == 0) return { 0, 0, 0, 0 };

    float dx = px - centerX;
    float dy = py - centerY;
    float angle = std::atan2(dy, dx); // [-π, π]

    float t;
    if (std::abs(endAngle - startAngle) > 1e-6f) {
        float range = endAngle - startAngle;
        t = (angle - startAngle) / range;
        t = t - std::floor(t); // wrap to [0, 1)
    } else {
        t = (angle + (float)M_PI) / (2.0f * (float)M_PI); // [0, 1]
    }
    if (t < 0.0f) t = 0.0f;
    if (t > 1.0f) t = 1.0f;

    return SampleGradientStops(t, stops, stopCount, colorInterpolationMode);
}

// ---------------------------------------------------------------------------
// FlattenGradientStops — utility to convert EngineBrushData::stops into the
// interleaved [pos, r, g, b, a] float layout expected by the samplers above.
//
// out is reset and filled. Returns out.data() / brush.stopCount for direct
// hand-off into SampleLinearGradient / SampleRadialGradient / SampleSweep.
// ---------------------------------------------------------------------------
inline void FlattenGradientStops(const EngineBrushData& brush,
                                 std::vector<float>& out) {
    out.clear();
    out.reserve(brush.stopCount * 5);
    for (uint32_t i = 0; i < brush.stopCount; ++i) {
        out.push_back(brush.stops[i].position);
        out.push_back(brush.stops[i].r);
        out.push_back(brush.stops[i].g);
        out.push_back(brush.stops[i].b);
        out.push_back(brush.stops[i].a);
    }
}

// Returns only positions where the piecewise-linear RGBA function changes
// slope. Every real colour/alpha transition is retained; mathematically
// redundant stops do not produce thousands of sub-pixel triangles.
inline void BuildGradientRampBreakpoints(
    const EngineBrushData& brush,
    std::vector<float>& out) {
    out.clear();
    if (!brush.stops || brush.stopCount == 0) return;

    out.reserve(brush.stopCount);
    for (uint32_t i = 0; i < brush.stopCount; ++i) {
        bool changesRamp = i == 0 || i + 1 == brush.stopCount;
        if (!changesRamp) {
            const auto& previousStop = brush.stops[i - 1];
            const auto& stop = brush.stops[i];
            const auto& nextStop = brush.stops[i + 1];
            const double previousPosition =
                std::clamp(static_cast<double>(previousStop.position),
                           0.0, 1.0);
            const double position =
                std::clamp(static_cast<double>(stop.position),
                           0.0, 1.0);
            const double nextPosition =
                std::clamp(static_cast<double>(nextStop.position),
                           0.0, 1.0);
            const double span = nextPosition - previousPosition;
            if (span <= 1e-12 ||
                position <= previousPosition ||
                position >= nextPosition) {
                // Equal/reversed positions can encode a hard transition.
                changesRamp = true;
            } else {
                const double fraction =
                    (position - previousPosition) / span;
                constexpr double kCollinearTolerance = 1e-6;
                const double previousChannels[] = {
                    previousStop.r, previousStop.g,
                    previousStop.b, previousStop.a
                };
                const double channels[] = {
                    stop.r, stop.g, stop.b, stop.a
                };
                const double nextChannels[] = {
                    nextStop.r, nextStop.g,
                    nextStop.b, nextStop.a
                };
                for (uint32_t channel = 0; channel < 4; ++channel) {
                    const double expected =
                        previousChannels[channel] +
                        (nextChannels[channel] -
                         previousChannels[channel]) * fraction;
                    if (std::fabs(channels[channel] - expected) >
                        kCollinearTolerance) {
                        changesRamp = true;
                        break;
                    }
                }
                // Collinear straight RGBA can still curve after premultiplying
                // varying alpha. Preserve the adaptive bands required by the
                // vertex-color pipelines rather than collapsing them again.
                const double previousRgb[]={previousStop.r,previousStop.g,previousStop.b};
                const double rgb[]={stop.r,stop.g,stop.b};
                const double nextRgb[]={nextStop.r,nextStop.g,nextStop.b};
                for (uint32_t channel=0;channel<3 && !changesRamp;++channel) {
                    const double a=previousRgb[channel]*previousStop.a;
                    const double b=nextRgb[channel]*nextStop.a;
                    if (std::fabs(rgb[channel]*stop.a-(a+(b-a)*fraction))>kCollinearTolerance)
                        changesRamp=true;
                }
            }
        }
        if (changesRamp) {
            out.push_back(
                std::clamp(brush.stops[i].position, 0.0f, 1.0f));
        }
    }

    std::sort(out.begin(), out.end());
    out.erase(std::unique(out.begin(), out.end()), out.end());
}

// ---------------------------------------------------------------------------
// SampleBrushGradient — single dispatcher used by Impeller-style per-vertex
// color baking. Returns a non-premultiplied GradientColor; the caller is
// responsible for premultiplying alpha before writing into vertex color.
//
// stopData/stopCount must already be flattened (see FlattenGradientStops).
// ---------------------------------------------------------------------------
inline float GradientParameter(const EngineBrushData& brush, float px, float py)
{
    TransformGradientPoint(brush.userToGradient,px,py,px,py);
    if (brush.type == 1) {
        const float dx=brush.endX-brush.startX,dy=brush.endY-brush.startY;
        const float length=dx*dx+dy*dy;
        return length>1e-12f?((px-brush.startX)*dx+(py-brush.startY)*dy)/length:0.0f;
    }
    if (brush.type == 2) {
        if (brush.radialFlags & 1) return float(SvgRadialParameter(px,py,brush.centerX,brush.centerY,
            brush.originX,brush.originY,brush.radiusX,brush.radiusY,brush.focalRadius,brush.radialFlags));
        const float rx=std::fabs(brush.radiusX),ry=std::fabs(brush.radiusY);
        if (rx<=1e-12f||ry<=1e-12f) return 1.0f;
        float fx=(brush.originX-brush.centerX)/rx,fy=(brush.originY-brush.centerY)/ry;
        const float focusLength=std::sqrt(fx*fx+fy*fy);
        if(focusLength>0.99f){fx*=0.99f/focusLength;fy*=0.99f/focusLength;}
        const float dx=(px-brush.centerX)/rx-fx,dy=(py-brush.centerY)/ry-fy;
        const float a=dx*dx+dy*dy,b=fx*dx+fy*dy;
        return a>1e-12f?a/std::max(-b+std::sqrt(std::max(b*b+a*(1-fx*fx-fy*fy),0.0f)),1e-12f):0.0f;
    }
    return 0;
}

inline GradientColor SampleBrushGradient(const EngineBrushData& brush,
                                         const float* stopData,
                                         float px, float py) {
    switch (brush.type) {
        case 1: case 2: {
            float t=GradientParameter(brush,px,py);
            if (std::isnan(t)) return {0,0,0,0};
            if (std::isinf(t) && brush.type==2 && (brush.radialFlags&1) && brush.spreadMethod!=0) {
                GradientColor average;
                AverageGradientStops(brush.stops,brush.stopCount,brush.colorInterpolationMode,
                    average.r,average.g,average.b,average.a);
                return average;
            }
            if (brush.spreadMethod==1) t-=std::floor(t);
            else if(brush.spreadMethod==2){t=std::fmod(t,2.0f);if(t<0)t+=2.0f;if(t>1)t=2-t;}
            else t=std::clamp(t,0.0f,1.0f);
            return SampleGradientStops(t,stopData,brush.stopCount,brush.colorInterpolationMode);
        }
        case 3: // sweep
            TransformGradientPoint(brush.userToGradient,px,py,px,py);
            return SampleSweepGradient(px, py,
                brush.centerX, brush.centerY,
                brush.startX, brush.endX,
                stopData, brush.stopCount, brush.colorInterpolationMode);
        default:
            return { brush.r, brush.g, brush.b, brush.a };
    }
}

} // namespace jalium
