#pragma once

#include "jalium_impeller_stroke.h"
#include <cmath>
#include <cstring>

namespace jalium {

// Preserve the generic flattener's exact point order for one LineTo-only
// contour, including coincident vertices. Unsupported commands and nonfinite
// transformed points keep the caller on its existing general path.
// Input commands must not alias the retained output scratch vector.
inline bool TryFlattenStrokePolyline(
    float startX, float startY, const float* commands, uint32_t commandLength,
    const EngineTransform& transform, std::vector<float>& output)
{
    if (!commands || commandLength < 3 || commandLength % 3 != 0 ||
        !std::isfinite(startX) || !std::isfinite(startY)) return false;
    for (uint32_t i = 0; i < commandLength; i += 3) {
        if (commands[i] != 0.0f || !std::isfinite(commands[i+1]) || !std::isfinite(commands[i+2])) return false;
    }
    output.clear();
    output.reserve(2 + size_t(commandLength / 3) * 2);
    auto append = [&](float x, float y) {
        const float px = transform.m11 * x + transform.m21 * y + transform.dx;
        const float py = transform.m12 * x + transform.m22 * y + transform.dy;
        if (!std::isfinite(px) || !std::isfinite(py)) return false;
        output.push_back(px); output.push_back(py);
        return true;
    };
    if (!append(startX, startY)) return false;
    for (uint32_t i = 0; i < commandLength; i += 3)
        if (!append(commands[i+1], commands[i+2])) return false;
    return true;
}

// Pixel-space equivalent of flattening and widening an open, undashed,
// butt-capped segment. The caller retains the normal analytic rasterizer.
// Restrict the command count so checking an ineligible path stays cheap.
inline bool TryBuildSimpleStrokeContour(
    float startX, float startY, const float* commands, uint32_t commandLength,
    float pixelStrokeWidth, const EngineTransform& transform,
    StrokeContourBuffer& output)
{
    if (!commands || (commandLength != 3 && commandLength != 6) ||
        !(pixelStrokeWidth > 0.0f) || !std::isfinite(pixelStrokeWidth)) return false;

    float points[6];
    uint32_t count = 0;
    auto append = [&](float x, float y) {
        const float px = transform.m11 * x + transform.m21 * y + transform.dx;
        const float py = transform.m12 * x + transform.m22 * y + transform.dy;
        if (!std::isfinite(px) || !std::isfinite(py)) return false;
        if (count != 0) {
            const float dx = px - points[(count - 1) * 2];
            const float dy = py - points[(count - 1) * 2 + 1];
            // Match ExpandStrokePath's consecutive-point compaction.
            if (dx * dx + dy * dy <= 1e-12f) return true;
        }
        points[count * 2] = px;
        points[count * 2 + 1] = py;
        ++count;
        return true;
    };
    if (!append(startX, startY)) return false;
    for (uint32_t i = 0; i < commandLength; i += 3) {
        if (commands[i] != 0.0f || !append(commands[i + 1], commands[i + 2])) return false;
    }
    if (count != 2) return false;

    const float dx = points[2] - points[0], dy = points[3] - points[1];
    const float length = std::sqrt(dx * dx + dy * dy);
    if (!(length >= 1e-6f) || !std::isfinite(length)) return false;
    const float halfWidth = pixelStrokeWidth * 0.5f;
    const float nx = (-dy / length) * halfWidth;
    const float ny = (dx / length) * halfWidth;
    const float quad[8] = {
        points[0] + nx, points[1] + ny,
        points[0] - nx, points[1] - ny,
        points[2] - nx, points[3] - ny,
        points[2] + nx, points[3] + ny
    };
    double area = 0.0;
    for (uint32_t i = 0; i < 4; ++i) {
        const uint32_t j = (i + 1) % 4;
        area += (double)quad[i * 2] * quad[j * 2 + 1]
              - (double)quad[j * 2] * quad[i * 2 + 1];
    }
    if (!std::isfinite(area) || std::abs(area) < 1e-7) return false;

    auto& pointsOut = output.Append().points;
    pointsOut.resize(8);
    if (area > 0.0) {
        std::memcpy(pointsOut.data(), quad, sizeof(quad));
    } else {
        for (uint32_t i = 0; i < 4; ++i) {
            pointsOut[i * 2] = quad[(3 - i) * 2];
            pointsOut[i * 2 + 1] = quad[(3 - i) * 2 + 1];
        }
    }
    return true;
}

} // namespace jalium
