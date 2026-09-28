#pragma once
#include "jalium_types.h"
#include <algorithm>
#include <cmath>
#include <limits>
#include <memory>
#include <vector>

namespace jalium {

inline bool IsValidEllipticalClip(const JaliumEllipticalRectClip& clip)
{
    if (clip.structSize < sizeof(clip) || (clip.edges & ~15u) != 0 ||
        !std::isfinite(clip.x) || !std::isfinite(clip.y) ||
        !std::isfinite(clip.width) || !std::isfinite(clip.height) ||
        clip.width < 0 || clip.height < 0) return false;
    for (int i = 0; i < 4; ++i)
        if (!std::isfinite(clip.radiusX[i]) || !std::isfinite(clip.radiusY[i]) ||
            clip.radiusX[i] < 0 || clip.radiusY[i] < 0) return false;
    return true;
}

/// Immutable clip state captured at push time, independent of subsequent
/// transforms and intersected scissor bounds. This also supplies a CPU oracle
/// for backend conformance cases.
struct EllipticalClip {
    JaliumEllipticalRectClip shape {};
    double inverse[6] { 1, 0, 0, 1, 0, 0 };
    bool invertible = true;

    EllipticalClip(const JaliumEllipticalRectClip& value, const float matrix[6]) : shape(value)
    {
        const double a = matrix[0], b = matrix[1], c = matrix[2], d = matrix[3];
        const double determinant = a * d - b * c;
        invertible = std::isfinite(determinant) && determinant != 0;
        if (!invertible) return;
        inverse[0] = d / determinant; inverse[1] = -b / determinant;
        inverse[2] = -c / determinant; inverse[3] = a / determinant;
        inverse[4] = -(matrix[4] * inverse[0] + matrix[5] * inverse[2]);
        inverse[5] = -(matrix[4] * inverse[1] + matrix[5] * inverse[3]);
        for (double entry : inverse) if (!std::isfinite(entry)) invertible = false;
    }

    double SignedDistance(double px, double py) const
    {
        if (!invertible) return std::numeric_limits<double>::infinity();
        const double x = px * inverse[0] + py * inverse[2] + inverse[4] - shape.x;
        const double y = px * inverse[1] + py * inverse[3] + inverse[5] - shape.y;
        double distance = -std::numeric_limits<double>::infinity();
        if (shape.edges & 1) distance = std::max(distance, -x);
        if (shape.edges & 2) distance = std::max(distance, -y);
        if (shape.edges & 4) distance = std::max(distance, x - shape.width);
        if (shape.edges & 8) distance = std::max(distance, y - shape.height);
        const double dx[4] { x, shape.width - x, shape.width - x, x };
        const double dy[4] { y, y, shape.height - y, shape.height - y };
        constexpr uint32_t adjoining[4] { 3, 6, 12, 9 };
        for (int i = 0; i < 4; ++i) {
            const double rx = shape.radiusX[i], ry = shape.radiusY[i];
            if ((shape.edges & adjoining[i]) != adjoining[i] || rx == 0 || ry == 0 || dx[i] >= rx || dy[i] >= ry) continue;
            const double ex = (dx[i] - rx) / rx, ey = (dy[i] - ry) / ry;
            const double length = std::sqrt(ex * ex + ey * ey);
            const double gradient = std::sqrt(ex * ex / (rx * rx) + ey * ey / (ry * ry));
            if (gradient > 0) distance = std::max(distance, length * (length - 1.0) / gradient);
        }
        return distance;
    }

    bool Contains(double px, double py) const
    {
        if (!invertible) return false;
        const double x = px * inverse[0] + py * inverse[2] + inverse[4] - shape.x;
        const double y = px * inverse[1] + py * inverse[3] + inverse[5] - shape.y;
        if (((shape.edges & 1) && x < 0) || ((shape.edges & 2) && y < 0) ||
            ((shape.edges & 4) && x >= shape.width) || ((shape.edges & 8) && y >= shape.height)) return false;
        return SignedDistance(px, py) <= 0;
    }

    bool operator==(const EllipticalClip& other) const
    {
        return invertible == other.invertible && shape.edges == other.shape.edges &&
            shape.x == other.shape.x && shape.y == other.shape.y &&
            shape.width == other.shape.width && shape.height == other.shape.height &&
            std::equal(shape.radiusX, shape.radiusX + 4, other.shape.radiusX) &&
            std::equal(shape.radiusY, shape.radiusY + 4, other.shape.radiusY) &&
            std::equal(inverse, inverse + 6, other.inverse);
    }

    template<class Append> void AppendCacheKey(Append&& append) const
    {
        append(shape.edges); append(shape.x); append(shape.y); append(shape.width); append(shape.height);
        for (float radius : shape.radiusX) append(radius);
        for (float radius : shape.radiusY) append(radius);
        for (double value : inverse) append(value);
        append(invertible);
    }

    // A rounded box (including a padding-edge contour cut by the opposite
    // border) is convex. Walk only the clipped ends of an already bounded
    // raster span, leaving its opaque interior eligible for native span copies.
    bool TightenSpan(int32_t y, int32_t& left, int32_t& right) const
    {
        while (left < right && !Contains(left + .5, y + .5)) ++left;
        while (left < right && !Contains(right - .5, y + .5)) --right;
        return left < right;
    }
};

// Shader-readable inverse mapping from physical pixels to the clip's local
// coordinates, followed by box dimensions and independent radii. Five float4s.
struct EllipticalClipGpu {
    float localX[4] {}, localY[4] {}, box[4] {}, radiusX[4] {}, radiusY[4] {};
    EllipticalClipGpu() = default;
    EllipticalClipGpu(const JaliumEllipticalRectClip& shape, const float matrix[6], bool exclude = false)
    {
        EllipticalClip clip(shape, matrix);
        localX[0] = static_cast<float>(clip.inverse[0]); localX[1] = static_cast<float>(clip.inverse[2]);
        localX[2] = static_cast<float>(clip.inverse[4] - shape.x); localX[3] = static_cast<float>(shape.edges);
        localY[0] = static_cast<float>(clip.inverse[1]); localY[1] = static_cast<float>(clip.inverse[3]);
        localY[2] = static_cast<float>(clip.inverse[5] - shape.y);
        box[0] = shape.width; box[1] = shape.height; box[2] = exclude ? 1.f : 0.f;
        box[3] = !clip.invertible || ((shape.edges & 5) == 5 && shape.width == 0) ||
            ((shape.edges & 10) == 10 && shape.height == 0) ? 1.f : 0.f;
        std::copy(shape.radiusX, shape.radiusX + 4, radiusX);
        std::copy(shape.radiusY, shape.radiusY + 4, radiusY);
    }
};
static_assert(sizeof(EllipticalClipGpu) == 80);
using EllipticalClipSnapshot = std::shared_ptr<const std::vector<EllipticalClipGpu>>;

} // namespace jalium
