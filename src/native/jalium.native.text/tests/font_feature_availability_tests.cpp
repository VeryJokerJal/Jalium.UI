#include "jalium_font_features.h"
#include <cstdio>
#include <stdexcept>

namespace {
int checks = 0;
void Check(bool value) { if (!value) throw std::runtime_error("FeatureList check failed"); ++checks; }
void Put16(std::vector<uint8_t>& bytes, size_t at, uint16_t value) { bytes[at] = value >> 8; bytes[at + 1] = value & 255; }
void Put32(std::vector<uint8_t>& bytes, size_t at, uint32_t value) { Put16(bytes, at, value >> 16); Put16(bytes, at + 2, value & 65535); }
std::vector<uint8_t> Table()
{
    // Two defined features with empty lookup arrays, a valid availability case.
    std::vector<uint8_t> bytes(36);
    Put32(bytes, 0, 0x10000); Put16(bytes, 4, 10); Put16(bytes, 6, 12); Put16(bytes, 8, 34);
    Put16(bytes, 12, 2); Put32(bytes, 14, 0x70636170); Put16(bytes, 18, 14);
    Put32(bytes, 20, 0x736d6370); Put16(bytes, 24, 18);
    return bytes;
}
}
int main()
{
    try {
        auto bytes = Table(); std::vector<uint32_t> found;
        Check(jalium::font_features::Collect(bytes.data(), bytes.size(), found));
        Check(found == std::vector<uint32_t>{0x70636170, 0x736d6370});
        const uint32_t tags[] = {0x736d6370, 0x58585858, 0x70636170, 0x736d6370}; uint8_t supported[4]{};
        jalium::font_features::Query(found, tags, 4, supported);
        Check(supported[0] == 1 && supported[1] == 0 && supported[2] == 1 && supported[3] == 1);
        jalium::font_features::Query(found, nullptr, 0, nullptr);
        for (size_t length = 0; length < 34; ++length) {
            std::vector<uint32_t> sentinel{0x12345678};
            Check(!jalium::font_features::Collect(bytes.data(), length, sentinel));
            Check(sentinel == std::vector<uint32_t>{0x12345678});
        }
        for (auto mutation : {0, 2, 6, 12, 18, 24, 32}) {
            auto invalid = bytes; Put16(invalid, mutation, 65535);
            std::vector<uint32_t> sentinel{0x12345678};
            Check(!jalium::font_features::Collect(invalid.data(), invalid.size(), sentinel));
            Check(sentinel == std::vector<uint32_t>{0x12345678});
        }
        Check(!jalium::font_features::Collect(nullptr, 100, found));
        // A 1.1 header must include its FeatureVariations offset.
        auto v11 = bytes; Put16(v11, 2, 1);
        std::vector<uint32_t> sentinel{0x12345678};
        Check(!jalium::font_features::Collect(v11.data(), v11.size(), sentinel));
        Check(sentinel == std::vector<uint32_t>{0x12345678});
        uint32_t random = 0x2468ac01;
        for (unsigned trial = 0; trial < 10000; ++trial) {
            std::vector<uint8_t> fuzz(trial % 128);
            for (auto& byte : fuzz) { random = random * 1664525u + 1013904223u; byte = random >> 24; }
            auto before = found;
            if (!jalium::font_features::Collect(fuzz.data(), fuzz.size(), found)) Check(found == before);
        }
        std::printf("Font feature parser: %d checks passed\n", checks); return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "%s\n", error.what()); return 1; }
}
