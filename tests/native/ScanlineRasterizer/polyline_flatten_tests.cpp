#include "d3d12_simple_stroke.h"
#include "jalium_scanline_rasterizer.h"
#include <array>
#include <cstring>
#include <iostream>
#include <limits>
#include <random>
#include <stdexcept>

using namespace jalium;
struct Vertex { float x, y, r, g, b, a; };
static void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }

static std::vector<Contour> Reference(float x, float y, std::vector<float> commands, const EngineTransform& transform) {
    auto point = [&](float& px, float& py) {
        const float tx = transform.m11 * px + transform.m21 * py + transform.dx;
        const float ty = transform.m12 * px + transform.m22 * py + transform.dy;
        px = tx; py = ty;
    };
    point(x, y);
    for (size_t i = 0; i < commands.size(); i += 3) point(commands[i+1], commands[i+2]);
    return FlattenPathToContours(x, y, commands.data(), uint32_t(commands.size()), 0.25f);
}

static void Widen(const float* points, uint32_t count, int join, int cap, float width, float miter,
                  StrokeContourBuffer& output) {
    output.Reset(); std::vector<Vertex> vertices; std::vector<uint32_t> indices;
    ExpandStrokePath(vertices, indices, points, count, width, static_cast<ImpellerJoin>(join), miter,
        static_cast<ImpellerCap>(cap), false, .3f, .2f, .7f, .8f, nullptr, 1.0f, &output);
}

int main() {
    try {
        std::mt19937 random(38017);
        std::vector<float> scratch;
        StrokeContourBuffer before, after;
        size_t widened = 0, rasterized = 0;
        for (int test = 0; test < 240; ++test) {
            float x = float(int(random() % 129) - 64) / 4, y = float(int(random() % 129) - 64) / 4;
            std::vector<float> commands;
            for (int point = 0; point < 1 + test % 23; ++point) {
                float px = float(int(random() % 257) - 128) / 8, py = float(int(random() % 257) - 128) / 8;
                if (point == 0 && test % 2 == 0) { px = x; py = y; }
                if (point != 0 && test % 7 == 0) { px = commands[commands.size()-2]; py = commands.back(); }
                if (point == test % 23 && test % 3 == 0) { px = x; py = y; }
                commands.insert(commands.end(), {0, px, py});
            }
            EngineTransform transform{float(int(random()%9)-4)/2, float(int(random()%7)-3)/4,
                float(int(random()%7)-3)/4, float(int(random()%9)-4)/2, .375f, -.625f};
            auto reference = Reference(x, y, commands, transform);
            Require(reference.size() == 1 && !reference[0].closed, "Reference shape changed");
            Require(TryFlattenStrokePolyline(x, y, commands.data(), uint32_t(commands.size()), transform, scratch), "Eligible polyline rejected");
            Require(reference[0].points.size() == scratch.size(), "Point count changed");
            Require(std::memcmp(reference[0].points.data(), scratch.data(), scratch.size()*sizeof(float)) == 0, "Transformed points changed");
            for (int join = 0; join < 3; ++join) for (int cap = 0; cap < 3; ++cap)
            for (float width : {.1f, 1.0f, 7.25f}) {
                float miter = test % 2 ? 1.0f : 10.0f;
                Widen(reference[0].points.data(), reference[0].VertexCount(), join, cap, width, miter, before);
                Widen(scratch.data(), uint32_t(scratch.size()/2), join, cap, width, miter, after);
                Require(before.Count() == after.Count(), "Widened contour count changed");
                for (size_t i = 0; i < before.Count(); ++i) {
                    const auto& a = before.Data()[i]; const auto& b = after.Data()[i];
                    Require(a.closed == b.closed && a.points.size() == b.points.size(), "Widened contour shape changed");
                    Require(std::memcmp(a.points.data(), b.points.data(), a.points.size()*sizeof(float)) == 0, "Widened contour changed");
                }
                ++widened;
                if (test % 12 == 0) {
                    std::vector<PixelRect> a, b;
                    RasterizePathToRects(before.Data(), before.Count(), FillRule::NonZero, a);
                    RasterizePathToRects(after.Data(), after.Count(), FillRule::NonZero, b);
                    Require(a.size() == b.size(), "Raster size changed");
                    if (!a.empty()) Require(std::memcmp(a.data(), b.data(), a.size()*sizeof(PixelRect)) == 0, "Raster output changed");
                    ++rasterized;
                }
            }
        }
        EngineTransform identity;
        for (const std::vector<float>& commands : std::vector<std::vector<float>>{
            {}, {0,1}, {1,1,2,3,4,5,6}, {2,1,2}, {5}, {.5f,1,2}, {0,INFINITY,2}, {0,NAN,2}}) {
            Require(!TryFlattenStrokePolyline(0, 0, commands.data(), uint32_t(commands.size()), identity, scratch), "Unsupported path accepted");
        }
        const float line[]{0, 2, 3};
        Require(!TryFlattenStrokePolyline(NAN, 0, line, 3, identity, scratch), "Nonfinite start accepted");
        EngineTransform overflow{(std::numeric_limits<float>::max)(), 0, 0, 1, 0, 0};
        Require(!TryFlattenStrokePolyline(0, 0, line, 3, overflow, scratch), "Nonfinite transformed point accepted");
        std::cout << "widened cases: " << widened << ", rasterized cases: " << rasterized << '\n';
        return 0;
    } catch (const std::exception& exception) { std::cerr << exception.what() << '\n'; return 1; }
}
