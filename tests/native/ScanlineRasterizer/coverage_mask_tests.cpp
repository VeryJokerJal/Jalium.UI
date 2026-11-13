#include "d3d12_coverage_rect.h"
#include <array>
#include <cstring>
#include <iostream>
#include <random>
#include <stdexcept>

using namespace jalium;

static void Require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}

static std::array<float, 4> ReferenceBounds(const D3D12CoverageMask& mask, int sx, int sy) {
    std::array<float, 4> bounds{INFINITY, INFINITY, -INFINITY, -INFINITY};
    for (const auto& rect : mask.rects) {
        float x0 = float(rect.x) + float(sx), y0 = float(rect.y) + float(sy);
        float x1 = x0 + float(rect.w), y1 = y0 + float(rect.h);
        if (x0 < bounds[0]) bounds[0] = x0;
        if (y0 < bounds[1]) bounds[1] = y0;
        if (x1 > bounds[2]) bounds[2] = x1;
        if (y1 > bounds[3]) bounds[3] = y1;
    }
    return bounds;
}

static void CompareStreams(const CoverageInstanceStream& first, const CoverageInstanceStream& second) {
    Require(first.size() == second.size(), "Instance count changed");
    Require(first.data_size_bytes() == second.data_size_bytes(), "Payload size changed");
    Require(std::memcmp(first.data(), second.data(), first.data_size_bytes()) == 0, "GPU payload changed");
    Require(std::memcmp(first.offsets(), second.offsets(), first.size() * sizeof(uint32_t)) == 0, "Instance offsets changed");
}

int main() {
    try {
        std::mt19937 random(37017);
        size_t scenarios = 0;
        const int offsets[] = {0, 1, -1, 8192, -8192, 16777215, 16777216, 16777217,
            -16777217, (std::numeric_limits<int>::min)(), (std::numeric_limits<int>::max)()};
        for (int test = 0; test < 160; ++test) {
            auto mask = std::make_shared<D3D12CoverageMask>();
            for (int index = 0; index < 1 + test % 31; ++index) {
                int x = int(random() % 8193) - 4096, y = int(random() % 8193) - 4096;
                if (test % 8 == 0) x += 16777214;
                if (test % 9 == 0) y -= 16777218;
                if (test % 17 == 0) x = (std::numeric_limits<int>::max)() - int(random() % 16);
                mask->rects.push_back({x, y, 1 + int(random() % 91), 1 + int(random() % 33),
                    float(random() % 256) / 255.0f});
            }
            mask->UpdateBounds();
            for (int offset : offsets) {
                int sy = offset == (std::numeric_limits<int>::min)() ? 19 : -offset;
                auto expectedBounds = ReferenceBounds(*mask, offset, sy);
                std::array<float, 4> actualBounds;
                mask->TranslatedBounds(offset, sy, actualBounds[0], actualBounds[1], actualBounds[2], actualBounds[3]);
                Require(std::memcmp(expectedBounds.data(), actualBounds.data(), sizeof(actualBounds)) == 0,
                    "Translated bounds changed");
                for (float scale : {1.0f, 0.8f, 2.0f / 3.0f, 0.5f}) {
                    D3D12CoverageMaskRun run{mask, float(offset), float(sy), 0.21f, 0.13f, 0.47f, 0.63f};
                    std::vector<D3D12CoverageRect> reference;
                    for (const auto& rect : mask->rects) {
                        float x0 = float(rect.x) + run.offsetX, y0 = float(rect.y) + run.offsetY;
                        reference.push_back({x0, y0, x0 + float(rect.w), y0 + float(rect.h),
                            run.r * rect.alpha, run.g * rect.alpha, run.b * rect.alpha, run.a * rect.alpha});
                    }
                    CoverageInstanceStream expected, actual;
                    D3D12CoverageRect head{1, 2, 8, 11, 0.3f, 0.4f, 0.5f, 0.6f};
                    CoverageRectInstance ellipse{3, 4, 10, 12, .1f, .2f, .3f, .7f, 1, 0, 0, 1, 2, 3, 0, 1};
                    expected.AppendRects(&head, 1, scale); actual.AppendRects(&head, 1, scale);
                    expected.AppendEllipse(ellipse); actual.AppendEllipse(ellipse);
                    expected.AppendRects(reference.data(), uint32_t(reference.size()), scale);
                    for (uint32_t first = 0; first < mask->rects.size();) {
                        uint32_t count = (std::min)(uint32_t(mask->rects.size()) - first, uint32_t(7));
                        actual.AppendMaskRects(run, first, count, scale);
                        first += count;
                    }
                    CompareStreams(expected, actual);
                    ++scenarios;
                }
            }
            mask->Reset();
            Require(mask->rects.empty() && !mask->exactBounds, "Recycled bounds were not reset");
            std::array<float, 4> empty;
            mask->TranslatedBounds(0, 0, empty[0], empty[1], empty[2], empty[3]);
            const auto expectedEmpty = ReferenceBounds(*mask, 0, 0);
            Require(std::memcmp(empty.data(), expectedEmpty.data(), sizeof(empty)) == 0, "Empty bounds changed");
        }
        std::cout << "coverage mask parity scenarios: " << scenarios << '\n';
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
