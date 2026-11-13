#include "d3d12_simple_stroke.h"
#include "jalium_scanline_rasterizer.h"
#include <chrono>
#include <cstring>
#include <iostream>
#include <limits>
#include <random>
#include <stdexcept>

using namespace jalium;
struct Vertex { float x, y, r, g, b, a; };

static void Require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

static void Reference(float x, float y, const std::vector<float>& commands,
                      float width, const EngineTransform& transform, StrokeContourBuffer& output) {
    auto point = [&](float& px, float& py) {
        const float tx = transform.m11 * px + transform.m21 * py + transform.dx;
        const float ty = transform.m12 * px + transform.m22 * py + transform.dy;
        px = tx; py = ty;
    };
    point(x, y);
    auto pixelCommands = commands;
    for (size_t i = 0; i < pixelCommands.size(); i += 3)
        point(pixelCommands[i + 1], pixelCommands[i + 2]);
    auto contours = FlattenPathToContours(x, y, pixelCommands.data(), (uint32_t)pixelCommands.size());
    std::vector<Vertex> vertices;
    std::vector<uint32_t> indices;
    output.Reset();
    for (const auto& contour : contours)
        ExpandStrokePath(vertices, indices, contour.points.data(), contour.VertexCount(),
            width, ImpellerJoin::Miter, 10.0f, ImpellerCap::Butt, false,
            1, 1, 1, 1, nullptr, 1.0f, &output);
}

int main() {
    try {
        std::mt19937 random(0x51A0BE12);
        std::uniform_real_distribution<float> location(-100.0f, 100.0f);
        std::uniform_real_distribution<float> linear(-2.0f, 2.0f);
        std::uniform_real_distribution<float> thickness(0.05f, 12.0f);
        StrokeContourBuffer reference, candidate;
        std::vector<PixelRect> expected, actual;
        for (int i = 0; i < 10000; ++i) {
            const float x = location(random), y = location(random);
            const float ex = location(random), ey = location(random);
            EngineTransform transform{linear(random), linear(random), linear(random), linear(random),
                                      location(random), location(random)};
            const float width = thickness(random);
            const std::vector<float> commands = i % 3 == 0 ? std::vector<float>{0, ex, ey}
                : i % 3 == 1 ? std::vector<float>{0, x, y, 0, ex, ey}
                             : std::vector<float>{0, ex, ey, 0, ex, ey};
            Reference(x, y, commands, width, transform, reference);
            candidate.Reset();
            Require(TryBuildSimpleStrokeContour(x, y, commands.data(), (uint32_t)commands.size(),
                                               width, transform, candidate), "Valid segment rejected.");
            Require(reference.Count() == candidate.Count(), "Contour count changed.");
            for (size_t c = 0; c < reference.Count(); ++c) {
                const auto& a = reference.Data()[c]; const auto& b = candidate.Data()[c];
                Require(a.closed == b.closed && a.points == b.points, "Widened geometry changed.");
            }
            if (i < 1000) {
                expected.clear(); actual.clear();
                RasterizePathToRects(reference.Data(), reference.Count(), FillRule::NonZero, expected);
                RasterizePathToRects(candidate.Data(), candidate.Count(), FillRule::NonZero, actual);
                Require(expected.size() == actual.size(), "Analytic rectangle count changed.");
                for (size_t r = 0; r < expected.size(); ++r) {
                    const auto& a = expected[r]; const auto& b = actual[r];
                    Require(a.x == b.x && a.y == b.y && a.w == b.w && a.h == b.h && a.alpha == b.alpha,
                            "Analytic pixel coverage changed.");
                }
            }
        }
        EngineTransform identity;
        for (const auto& commands : std::vector<std::vector<float>>{
                 {}, {0, 1}, {1, 1, 2, 3, 4, 5, 6}, {0, 0, 0},
                 {0, 1, 1, 0, 2, 0}, {0, 1, 1, 0, 0, 0}, {0, 1, 1, 5}}) {
            candidate.Reset();
            Require(!TryBuildSimpleStrokeContour(0, 0, commands.data(), (uint32_t)commands.size(),
                                                1, identity, candidate), "Unsupported path accepted.");
            Require(candidate.Count() == 0, "Rejected path modified output.");
        }
        float commands[]{0, 10, 3};
        for (float width : {0.0f, -1.0f, std::numeric_limits<float>::infinity(),
                            std::numeric_limits<float>::quiet_NaN()})
            Require(!TryBuildSimpleStrokeContour(0, 0, commands, 3, width, identity, candidate),
                    "Invalid thickness accepted.");
        identity.dx = std::numeric_limits<float>::quiet_NaN();
        Require(!TryBuildSimpleStrokeContour(0, 0, commands, 3, 1, identity, candidate),
                "Invalid transform accepted.");

        identity = {};
        const std::vector<float> repeated{0, 1, 2, 0, 20, 7};
        auto benchmark = [&](bool fast) {
            const auto begin = std::chrono::steady_clock::now();
            for (int i = 0; i < 100000; ++i) {
                candidate.Reset();
                if (fast) TryBuildSimpleStrokeContour(1, 2, repeated.data(), 6, 2.75f, identity, candidate);
                else Reference(1, 2, repeated, 2.75f, identity, candidate);
            }
            return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - begin).count();
        };
        const double before = benchmark(false), after = benchmark(true);
        std::cout << "{\"geometryCases\":10000,\"coverageCases\":1000,\"iterations\":100000,"
                  << "\"referenceMs\":" << before << ",\"simpleMs\":" << after << "}\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
