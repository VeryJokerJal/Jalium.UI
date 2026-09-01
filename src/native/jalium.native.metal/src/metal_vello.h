#pragma once

#include "jalium_vello_encode.h"

#import <Metal/Metal.h>

#include <memory>

namespace jalium {

/// Metal execution backend for the shared Vello 0.10 scene encoder.
class MetalVelloPipeline {
public:
    explicit MetalVelloPipeline(id<MTLDevice> device);
    ~MetalVelloPipeline();
    bool Initialize();
    bool IsReady() const;
    void PrepareFrame(uint32_t frameIndex);
    bool Record(id<MTLCommandBuffer> commandBuffer, const VelloSubScene& scene,
        uint32_t frameIndex);
    id<MTLTexture> OutputTexture() const;
    VelloRenderRegion OutputRegion() const;
    void ReclaimIdleResources();

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace jalium
