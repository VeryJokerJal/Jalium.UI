#pragma once

#include "jalium_elliptical_clip.h"
#include "jalium_triangulate.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <vector>

namespace jalium {

// Device-pixel edges captured when a path clip is pushed. The same edges feed
// the CPU fallback and the shared D3D12/Vulkan shader clip buffer.
class PathClip {
public:
    struct Edge { float x0, y0, x1, y1; };

    PathClip(float startX, float startY, const float* commands,
        uint32_t commandLength, int32_t fillRule, const float matrix[6])
        : fillRule_(fillRule)
    {
        const float scaleX = std::hypot(matrix[0], matrix[1]);
        const float scaleY = std::hypot(matrix[2], matrix[3]);
        const float scale = std::max(scaleX, scaleY);
        const float tolerance = std::isfinite(scale) && scale > 0
            ? std::max(0.01f, 0.5f / scale) : 0.5f;
        auto contours = FlattenPathToContours(
            startX, startY, commands, commandLength, tolerance);
        for (const auto& contour : contours) {
            const uint32_t count = contour.VertexCount();
            if (count < 2) continue;
            std::vector<float> points;
            points.reserve(static_cast<size_t>(count) * 2u);
            for (uint32_t i = 0; i < count; ++i) {
                const float x = contour.X(i) * matrix[0] + contour.Y(i) * matrix[2] + matrix[4];
                const float y = contour.X(i) * matrix[1] + contour.Y(i) * matrix[3] + matrix[5];
                if (!std::isfinite(x) || !std::isfinite(y)) {
                    valid_ = false;
                    edges_.clear();
                    return;
                }
                points.push_back(x);
                points.push_back(y);
                minX_ = std::min(minX_, x);
                minY_ = std::min(minY_, y);
                maxX_ = std::max(maxX_, x);
                maxY_ = std::max(maxY_, y);
            }
            for (uint32_t i = 0; i < count; ++i) {
                const uint32_t next = (i + 1u) % count;
                const Edge edge { points[i * 2u], points[i * 2u + 1u],
                    points[next * 2u], points[next * 2u + 1u] };
                if (edge.x0 == edge.x1 && edge.y0 == edge.y1) continue;
                edges_.push_back(edge);
            }
        }
    }

    bool IsValid() const { return valid_; }
    bool IsEmpty() const { return edges_.empty(); }

    bool Contains(float x, float y) const
    {
        if (edges_.empty() || x < minX_ || y < minY_ || x > maxX_ || y > maxY_)
            return false;
        int winding = 0;
        for (const auto& edge : edges_) {
            const bool up = edge.y0 <= y && edge.y1 > y;
            const bool down = edge.y1 <= y && edge.y0 > y;
            if (!up && !down) continue;
            const float crossing = edge.x0 +
                (y - edge.y0) * (edge.x1 - edge.x0) / (edge.y1 - edge.y0);
            if (crossing > x) winding += up ? 1 : -1;
        }
        return fillRule_ == 0 ? (winding & 1) != 0 : winding != 0;
    }

    void AppendGpu(std::vector<EllipticalClipGpu>& output) const
    {
        // box.w == 2 marks a path header. box.x/y/z contain edge count,
        // fill rule and the number of following five-edge records. The
        // existing five-float4 record is reused without changing descriptor
        // layouts or shader bindings on either GPU backend.
        EllipticalClipGpu header;
        header.localX[0] = minX_;
        header.localX[1] = minY_;
        header.localX[2] = maxX_;
        header.localX[3] = maxY_;
        header.box[0] = static_cast<float>(edges_.size());
        header.box[1] = static_cast<float>(fillRule_);
        header.box[2] = static_cast<float>((edges_.size() + 4u) / 5u);
        header.box[3] = 2.0f;
        output.push_back(header);
        for (size_t offset = 0; offset < edges_.size(); offset += 5u) {
            EllipticalClipGpu record;
            float* slots[5] { record.localX, record.localY, record.box,
                record.radiusX, record.radiusY };
            for (size_t slot = 0; slot < 5u && offset + slot < edges_.size(); ++slot) {
                const auto& edge = edges_[offset + slot];
                slots[slot][0] = edge.x0;
                slots[slot][1] = edge.y0;
                slots[slot][2] = edge.x1;
                slots[slot][3] = edge.y1;
            }
            output.push_back(record);
        }
    }

private:
    std::vector<Edge> edges_;
    int32_t fillRule_ = 0;
    bool valid_ = true;
    float minX_ = std::numeric_limits<float>::infinity();
    float minY_ = std::numeric_limits<float>::infinity();
    float maxX_ = -std::numeric_limits<float>::infinity();
    float maxY_ = -std::numeric_limits<float>::infinity();
};

} // namespace jalium
