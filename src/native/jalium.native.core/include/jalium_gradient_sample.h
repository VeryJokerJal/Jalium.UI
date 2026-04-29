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
                                         const float* stops, uint32_t stopCount) {
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

    // Standard stop lookup: project t into the stop range and interpolate.
    if (stopCount == 1 || t <= stops[0]) {
        return { stops[1], stops[2], stops[3], stops[4] };
    }
    for (uint32_t i = 1; i < stopCount; ++i) {
        float pos = stops[i * 5];
        if (t <= pos || i == stopCount - 1) {
            float prevPos = stops[(i - 1) * 5];
            float span = pos - prevPos;
            float frac = (span > 1e-6f) ? (t - prevPos) / span : 0.0f;
            if (frac < 0.0f) frac = 0.0f;
            if (frac > 1.0f) frac = 1.0f;
            return {
                stops[(i-1)*5+1] + frac * (stops[i*5+1] - stops[(i-1)*5+1]),
                stops[(i-1)*5+2] + frac * (stops[i*5+2] - stops[(i-1)*5+2]),
                stops[(i-1)*5+3] + frac * (stops[i*5+3] - stops[(i-1)*5+3]),
                stops[(i-1)*5+4] + frac * (stops[i*5+4] - stops[(i-1)*5+4])
            };
        }
    }
    uint32_t last = stopCount - 1;
    return { stops[last*5+1], stops[last*5+2], stops[last*5+3], stops[last*5+4] };
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

// ---------------------------------------------------------------------------
// SampleBrushGradient — single dispatcher used by Impeller-style per-vertex
// color baking. Returns a non-premultiplied GradientColor; the caller is
// responsible for premultiplying alpha before writing into vertex color.
//
// stopData/stopCount must already be flattened (see FlattenGradientStops).
// ---------------------------------------------------------------------------
inline GradientColor SampleBrushGradient(const EngineBrushData& brush,
                                         const float* stopData,
                                         float px, float py) {
    switch (brush.type) {
        case 1: // linear
            return SampleLinearGradient(px, py,
                brush.startX, brush.startY, brush.endX, brush.endY,
                stopData, brush.stopCount);
        case 2: // radial
            return SampleRadialGradient(px, py,
                brush.centerX, brush.centerY,
                brush.radiusX, brush.radiusY,
                stopData, brush.stopCount);
        case 3: // sweep
            return SampleSweepGradient(px, py,
                brush.centerX, brush.centerY,
                brush.startX, brush.endX,
                stopData, brush.stopCount);
        default:
            return { brush.r, brush.g, brush.b, brush.a };
    }
}

} // namespace jalium
