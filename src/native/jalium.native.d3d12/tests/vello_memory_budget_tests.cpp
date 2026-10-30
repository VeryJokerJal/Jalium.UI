#include "jalium_vello_encode.h"
#include <iostream>
#include <stdexcept>

using namespace jalium;

static void Require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

static void Rectangle(VelloSceneEncoder& encoder, float width, float height)
{
    float points[] = {0, 0, width, 0, width, height, 0, height};
    EngineBrushData brush;
    brush.r = 1;
    Require(encoder.EncodeFillPolygon(points, 4, brush, FillRule::NonZero,
                                      EngineTransform{}), "rectangle encoding failed");
}

static VelloRenderInfo Finish(VelloSceneEncoder& encoder)
{
    encoder.Finalize();
    encoder.Pack();
    return encoder.BuildRenderInfo();
}

int main()
{
    try {
        VelloSceneEncoder small;
        small.BeginFrame(1500, 920);
        Rectangle(small, 32, 32);
        auto tiny = Finish(small);
        Require(tiny.tileSize <= 16, "a tiny shape retained the large tile budget");
        Require(tiny.ptclSize <= 1024, "a tiny shape retained the large command budget");
        Require(tiny.infoBinDataSize <= 256, "a tiny shape retained the large bin budget");
        Require(tiny.blendSpillSize == 1, "unclipped content allocated blend spill");
        Require(tiny.lineSoupSize == (1u << 21) && tiny.segmentSize == (1u << 21),
                "curve-dependent budgets must retain their existing limits");

        VelloSceneEncoder nested;
        nested.BeginFrame(64, 64);
        for (int i = 0; i < 6; ++i) nested.EncodeBeginClipRect(0, 0, 64, 64);
        Rectangle(nested, 64, 64);
        auto deep = Finish(nested);
        const uint64_t deepTiles = static_cast<uint64_t>(deep.config.width_in_tiles) *
                                   deep.config.height_in_tiles;
        Require(deep.blendSpillSize >= deepTiles * 2 * 16 * 16,
                "six nested clips need spill space beyond four register levels");
        Require(deep.blendSpillSize <= 32768,
                "small nested clips retained the legacy spill budget");

        VelloSceneEncoder siblings;
        siblings.BeginFrame(64, 64);
        for (int i = 0; i < 40; ++i) {
            siblings.EncodeBeginClipRect(0, 0, 64, 64);
            Rectangle(siblings, 64, 64);
            siblings.EncodeEndClip();
        }
        auto separate = Finish(siblings);
        Require(separate.blendSpillSize == 1,
                "sequential clips must not accumulate nesting depth");
        // Each of the 120 draw objects can emit at most seven command words
        // per tile. Reserve enough for commands and 256-word continuation blocks.
        const uint64_t separateTiles = static_cast<uint64_t>(separate.config.width_in_tiles) *
                                       separate.config.height_in_tiles;
        Require(separate.ptclSize >= separateTiles * separate.config.n_drawobj * 7u,
                "multi-block tile command stream is undersized");

        VelloSceneEncoder dense;
        dense.BeginFrame(1920, 1080);
        for (int i = 0; i < 128; ++i) Rectangle(dense, 1920, 1080);
        auto many = Finish(dense);
        const uint64_t tiles = static_cast<uint64_t>(many.config.width_in_tiles) *
                               many.config.height_in_tiles;
        const uint64_t bins = static_cast<uint64_t>(many.widthInBins) * many.heightInBins;
        Require(many.tileSize >= tiles * 128u,
                "overlapping full-screen paths require their own tile storage");
        Require(many.infoBinDataSize >= many.config.bin_data_start + bins * 128u,
                "full-screen draw objects require entries in every bin");
        Require(many.config.tiles_size == many.tileSize &&
                many.config.ptcl_size == many.ptclSize &&
                many.config.blend_size == many.blendSpillSize &&
                many.config.binning_size + many.config.bin_data_start == many.infoBinDataSize,
                "shader config does not match allocated buffer sizes");
        std::cout << "PASS: Vello bounded scratch for tiny, nested, disjoint and dense scenes\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
