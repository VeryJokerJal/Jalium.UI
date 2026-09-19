#pragma once

#include "metal_backend.h"

#include <array>
#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>
#include <vector>

#ifdef __APPLE__
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>
#import <CoreVideo/CoreVideo.h>
#endif

namespace jalium {

struct MetalVertex {
    float x;
    float y;
    float u;
    float v;
};
static_assert(sizeof(MetalVertex) == 16);

// The shader consumes this as a flat float array. Keeping the ABI scalar avoids
// C++/MSL alignment drift and makes generated/reflected HLSL bindings easy to
// validate. See kMetalParam* in metal_render_target.cpp.
using MetalShaderParams = std::array<float, 512>;

#ifdef __APPLE__
struct MetalInkLayer {
    id<MTLDevice> device = nil;
    id<MTLTexture> texture = nil;
    id<MTLBuffer> upload = nil;
    uint32_t width = 0;
    uint32_t height = 0;
    uint64_t generation = 0;
    std::mutex mutex;
};

struct MetalBrushShader {
    id<MTLRenderPipelineState> pipeline = nil;
    id<MTLComputePipelineState> fallbackPipeline = nil;
    std::string key;
    std::string source;
    int32_t blendMode = 0;
};
#else
struct MetalInkLayer {
    uint32_t width = 0;
    uint32_t height = 0;
};
struct MetalBrushShader {
    std::string key;
    std::string source;
    int32_t blendMode = 0;
};
#endif

} // namespace jalium
