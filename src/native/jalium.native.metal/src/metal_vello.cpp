#include "metal_vello.h"

#include <algorithm>
#include <array>
#include <cstring>
#include <limits>
#include <vector>

#import <Foundation/Foundation.h>

namespace jalium {
namespace {

enum class Res : uint8_t {
    Config, Scene, Bump, Reduced, Reduced2, ReducedScan, TagMonoids,
    PathBbox, LineSoup, DrawReduced, DrawMonoid, InfoBinData, ClipInp,
    ClipBic, ClipEl, ClipBbox, DrawBbox, BinHeader, Path, Tile, SegCount,
    Segment, Ptcl, BlendSpill, Indirect, RampImage, DummyImage, OutputImage,
    Count
};
enum Stage : uint32_t {
    PathtagReduce, PathtagReduce2, PathtagScan1, PathtagScan, BboxClear,
    Flatten, DrawReduce, DrawLeaf, ClipReduce, ClipLeaf, Binning, TileAlloc,
    PathCountSetup, PathCount, Backdrop, Coarse, PathTilingSetup, PathTiling,
    Fine, StageCount
};
struct Binding { uint32_t index; Res resource; bool write; };
struct StageDef { const char* name; const Binding* bindings; uint32_t count; };

#define B(i,r,w) Binding{i,Res::r,w}
const Binding b0[]={B(0,Config,0),B(16,Scene,0),B(48,Reduced,1)};
const Binding b1[]={B(16,Reduced,0),B(48,Reduced2,1)};
const Binding b2[]={B(16,Reduced,0),B(17,Reduced2,0),B(48,ReducedScan,1)};
const Binding b3[]={B(0,Config,0),B(16,Scene,0),B(17,ReducedScan,0),B(48,TagMonoids,1)};
const Binding b4[]={B(0,Config,0),B(48,PathBbox,1)};
const Binding b5[]={B(0,Config,0),B(16,Scene,0),B(17,TagMonoids,0),B(48,PathBbox,1),B(49,Bump,1),B(50,LineSoup,1)};
const Binding b6[]={B(0,Config,0),B(16,Scene,0),B(48,DrawReduced,1)};
const Binding b7[]={B(0,Config,0),B(16,Scene,0),B(17,DrawReduced,0),B(18,PathBbox,0),B(48,DrawMonoid,1),B(49,InfoBinData,1),B(50,ClipInp,1)};
const Binding b8[]={B(16,ClipInp,0),B(17,PathBbox,0),B(48,ClipBic,1),B(49,ClipEl,1)};
const Binding b9[]={B(0,Config,0),B(16,ClipInp,0),B(17,PathBbox,0),B(18,ClipBic,0),B(19,ClipEl,0),B(48,DrawMonoid,1),B(49,ClipBbox,1)};
const Binding b10[]={B(0,Config,0),B(16,DrawMonoid,0),B(17,PathBbox,0),B(18,ClipBbox,0),B(48,DrawBbox,1),B(49,Bump,1),B(50,InfoBinData,1),B(51,BinHeader,1)};
const Binding b11[]={B(0,Config,0),B(16,Scene,0),B(17,DrawBbox,0),B(48,Bump,1),B(49,Path,1),B(50,Tile,1)};
const Binding b12[]={B(48,Bump,1),B(49,Indirect,1)};
const Binding b13[]={B(0,Config,0),B(16,LineSoup,0),B(17,Path,0),B(48,Bump,1),B(49,Tile,1),B(50,SegCount,1)};
const Binding b14[]={B(0,Config,0),B(16,Path,0),B(48,Bump,1),B(49,Tile,1)};
const Binding b15[]={B(0,Config,0),B(16,Scene,0),B(17,DrawMonoid,0),B(18,BinHeader,0),B(19,InfoBinData,0),B(20,Path,0),B(48,Tile,1),B(49,Bump,1),B(50,Ptcl,1)};
const Binding b16[]={B(48,Bump,1),B(49,Indirect,1),B(50,Ptcl,1)};
const Binding b17[]={B(16,SegCount,0),B(17,LineSoup,0),B(18,Path,0),B(19,Tile,0),B(48,Bump,1),B(49,Segment,1)};
const Binding b18[]={B(0,Config,0),B(16,Segment,0),B(17,Ptcl,0),B(18,InfoBinData,0),B(19,RampImage,0),B(20,DummyImage,0),B(48,BlendSpill,1),B(49,OutputImage,1)};
#undef B
#define STAGE(n,b) StageDef{"vello_" n,b,(uint32_t)(sizeof(b)/sizeof(b[0]))}
const StageDef stages[]={STAGE("pathtag_reduce",b0),STAGE("pathtag_reduce2",b1),
 STAGE("pathtag_scan1",b2),STAGE("pathtag_scan",b3),STAGE("bbox_clear",b4),
 STAGE("flatten",b5),STAGE("draw_reduce",b6),STAGE("draw_leaf",b7),
 STAGE("clip_reduce",b8),STAGE("clip_leaf",b9),STAGE("binning",b10),
 STAGE("tile_alloc",b11),STAGE("path_count_setup",b12),STAGE("path_count",b13),
 STAGE("backdrop",b14),STAGE("coarse",b15),STAGE("path_tiling_setup",b16),
 STAGE("path_tiling",b17),STAGE("fine",b18)};
#undef STAGE

NSUInteger Bytes(uint32_t count, uint32_t stride)
{
    uint64_t value=static_cast<uint64_t>(std::max(count,1u))*stride;
    return static_cast<NSUInteger>(std::min<uint64_t>(
        std::max<uint64_t>(value,256),std::numeric_limits<NSUInteger>::max()));
}
}

struct MetalVelloPipeline::Impl {
    id<MTLDevice> device=nil;
    id<MTLLibrary> library=nil;
    id<MTLComputePipelineState> pipelines[StageCount]={};
    id<MTLArgumentEncoder> argumentEncoders[StageCount]={};
    id<MTLSamplerState> sampler=nil;
    id<MTLTexture> output=nil;
    id<MTLTexture> dummy=nil;
    VelloRenderRegion region{};
    bool ready=false;

    struct Buffer { id<MTLBuffer> object=nil; NSUInteger capacity=0; };
    struct Slot {
        std::array<Buffer,(size_t)Res::Count> buffers;
        NSMutableArray* retained=nil;
    } slots[3];

    explicit Impl(id<MTLDevice> d):device(d)
    {for(auto& slot:slots)slot.retained=[NSMutableArray array];}

    bool Ensure(Buffer& buffer, NSUInteger bytes)
    {
        if(buffer.object&&buffer.capacity>=bytes)return true;
        NSUInteger capacity=256;while(capacity<bytes&&capacity<=NSUIntegerMax/2)capacity*=2;
        id<MTLBuffer> candidate=[device newBufferWithLength:capacity
            options:MTLResourceStorageModePrivate];
        if(!candidate)return false;buffer.object=candidate;buffer.capacity=capacity;return true;
    }
    id<MTLTexture> Texture(uint32_t width,uint32_t height,MTLPixelFormat format,
        MTLStorageMode storage=MTLStorageModePrivate)
    {
        MTLTextureDescriptor* d=[MTLTextureDescriptor texture2DDescriptorWithPixelFormat:format
            width:width height:height mipmapped:NO];d.storageMode=storage;
        d.usage=MTLTextureUsageShaderRead|MTLTextureUsageShaderWrite|MTLTextureUsageRenderTarget;
        return [device newTextureWithDescriptor:d];
    }
    id<MTLBuffer> ResourceBuffer(Slot& slot,Res resource,id<MTLBuffer> config,
        id<MTLBuffer> scene)
    {
        if(resource==Res::Config)return config;if(resource==Res::Scene)return scene;
        return slot.buffers[(size_t)resource].object;
    }
};

MetalVelloPipeline::MetalVelloPipeline(id<MTLDevice> device)
    :impl_(std::make_unique<Impl>(device)){}
MetalVelloPipeline::~MetalVelloPipeline()=default;

bool MetalVelloPipeline::Initialize()
{
    NSString* path=[NSBundle.mainBundle pathForResource:@"jalium_vello" ofType:@"metallib"];
    if(!path)return false;NSError* error=nil;
    impl_->library=[impl_->device newLibraryWithURL:[NSURL fileURLWithPath:path]
        error:&error];if(!impl_->library)return false;
    for(uint32_t i=0;i<StageCount;++i){id<MTLFunction> function=[impl_->library
        newFunctionWithName:[NSString stringWithUTF8String:stages[i].name]];if(!function)return false;
        impl_->pipelines[i]=[impl_->device newComputePipelineStateWithFunction:function error:&error];
        impl_->argumentEncoders[i]=[function newArgumentEncoderWithBufferIndex:0];
        if(!impl_->pipelines[i]||!impl_->argumentEncoders[i])return false;}
    MTLSamplerDescriptor* sd=[MTLSamplerDescriptor new];sd.minFilter=sd.magFilter=MTLSamplerMinMagFilterNearest;
    sd.sAddressMode=sd.tAddressMode=MTLSamplerAddressModeClampToEdge;
    impl_->sampler=[impl_->device newSamplerStateWithDescriptor:sd];
    impl_->dummy=impl_->Texture(1,1,MTLPixelFormatRGBA8Unorm,MTLStorageModeShared);
    uint32_t zero=0;if(impl_->dummy)[impl_->dummy replaceRegion:MTLRegionMake2D(0,0,1,1)
        mipmapLevel:0 withBytes:&zero bytesPerRow:4];
    impl_->ready=impl_->sampler&&impl_->dummy;return impl_->ready;
}
bool MetalVelloPipeline::IsReady()const{return impl_->ready;}
void MetalVelloPipeline::PrepareFrame(uint32_t frameIndex)
{[impl_->slots[frameIndex%3].retained removeAllObjects];}

bool MetalVelloPipeline::Record(id<MTLCommandBuffer> command,const VelloSubScene& sub,
    uint32_t frameIndex)
{
    if(!impl_->ready||!command||sub.packed.data.empty()||sub.packed.region.Empty())return false;
    auto& slot=impl_->slots[frameIndex%3];const VelloRenderInfo& ri=sub.ri;
    auto ensure=[&](Res r,NSUInteger bytes){return impl_->Ensure(slot.buffers[(size_t)r],bytes);};
    bool ok=true;
    ok&=ensure(Res::Bump,sizeof(VelloBumpAllocators));ok&=ensure(Res::Indirect,64);
    ok&=ensure(Res::Reduced,Bytes(ri.reducedSize,kVelloStrideTagMonoid));
    ok&=ensure(Res::Reduced2,Bytes(ri.reduced2Size,kVelloStrideTagMonoid));
    ok&=ensure(Res::ReducedScan,Bytes(ri.reducedScanSize,kVelloStrideTagMonoid));
    ok&=ensure(Res::TagMonoids,Bytes(ri.tagMonoidsSize,kVelloStrideTagMonoid));
    ok&=ensure(Res::PathBbox,Bytes(ri.pathBboxSize,kVelloStridePathBbox));
    ok&=ensure(Res::LineSoup,Bytes(ri.lineSoupSize,kVelloStrideLineSoup));
    ok&=ensure(Res::DrawReduced,Bytes(ri.drawReducedSize,kVelloStrideDrawMonoid));
    ok&=ensure(Res::DrawMonoid,Bytes(ri.drawMonoidSize,kVelloStrideDrawMonoid));
    ok&=ensure(Res::InfoBinData,Bytes(ri.infoBinDataSize,4));
    ok&=ensure(Res::ClipInp,Bytes(ri.clipInpSize,kVelloStrideClipInp));
    ok&=ensure(Res::ClipBic,Bytes(ri.clipBicSize,kVelloStrideClipBic));
    ok&=ensure(Res::ClipEl,Bytes(ri.clipElSize,kVelloStrideClipEl));
    ok&=ensure(Res::ClipBbox,Bytes(ri.clipBboxSize,kVelloStrideClipBbox));
    ok&=ensure(Res::DrawBbox,Bytes(ri.drawBboxSize,kVelloStrideDrawBbox));
    ok&=ensure(Res::BinHeader,Bytes(ri.binHeaderSize,kVelloStrideBinHeader));
    ok&=ensure(Res::Path,Bytes(ri.pathSize,kVelloStridePath));
    ok&=ensure(Res::Tile,Bytes(ri.tileSize,kVelloStrideTile));
    ok&=ensure(Res::SegCount,Bytes(ri.segCountSize,kVelloStrideSegCount));
    ok&=ensure(Res::Segment,Bytes(ri.segmentSize,kVelloStrideSegment));
    ok&=ensure(Res::Ptcl,Bytes(ri.ptclSize,4));
    ok&=ensure(Res::BlendSpill,Bytes(ri.blendSpillSize,4));if(!ok)return false;

    const NSUInteger sceneBytes=sub.packed.data.size()*sizeof(uint32_t);
    id<MTLBuffer> scene=[impl_->device newBufferWithBytes:sub.packed.data.data()
        length:std::max<NSUInteger>(sceneBytes,4) options:MTLResourceStorageModeShared];
    id<MTLBuffer> config=[impl_->device newBufferWithBytes:&ri.config length:sizeof(ri.config)
        options:MTLResourceStorageModeShared];
    id<MTLTexture> ramp=impl_->Texture(kVelloRampWidth,std::max(sub.rampCount,1u),
        MTLPixelFormatRGBA8Unorm,MTLStorageModeShared);
    if(!scene||!config||!ramp)return false;
    if(sub.rampCount&&sub.rampData.size()>=static_cast<size_t>(kVelloRampWidth)*sub.rampCount)
        [ramp replaceRegion:MTLRegionMake2D(0,0,kVelloRampWidth,sub.rampCount) mipmapLevel:0
            withBytes:sub.rampData.data() bytesPerRow:kVelloRampWidth*4];
    uint32_t outW=std::max(sub.packed.region.width,1u),outH=std::max(sub.packed.region.height,1u);
    if(!impl_->output||impl_->output.width<outW||impl_->output.height<outH){
        if(impl_->output)[slot.retained addObject:impl_->output];
        impl_->output=impl_->Texture(std::max<uint32_t>(outW,(uint32_t)(impl_->output?impl_->output.width:0)),
            std::max<uint32_t>(outH,(uint32_t)(impl_->output?impl_->output.height:0)),MTLPixelFormatRGBA8Unorm);
    }
    if(!impl_->output)return false;impl_->region=sub.packed.region;
    [slot.retained addObject:scene];[slot.retained addObject:config];[slot.retained addObject:ramp];

    // Ordered GPU clears: multiple sub-scenes may reuse scratch/output before
    // this command buffer is committed, so CPU memset would race queued reads.
    id<MTLBlitCommandEncoder> blit=[command blitCommandEncoder];
    [blit fillBuffer:slot.buffers[(size_t)Res::Bump].object range:NSMakeRange(0,
        slot.buffers[(size_t)Res::Bump].capacity) value:0];[blit endEncoding];
    MTLRenderPassDescriptor* clear=[MTLRenderPassDescriptor renderPassDescriptor];
    clear.colorAttachments[0].texture=impl_->output;clear.colorAttachments[0].loadAction=MTLLoadActionClear;
    clear.colorAttachments[0].storeAction=MTLStoreActionStore;clear.colorAttachments[0].clearColor=MTLClearColorMake(0,0,0,0);
    id<MTLRenderCommandEncoder> clearEncoder=[command renderCommandEncoderWithDescriptor:clear];[clearEncoder endEncoding];

    auto dispatch=[&](uint32_t stage,uint32_t x,uint32_t y,uint32_t z,bool indirect){
        if(x==0||y==0||z==0)return;
        id<MTLComputeCommandEncoder> encoder=[command computeCommandEncoder];
        id<MTLArgumentEncoder> argumentEncoder=impl_->argumentEncoders[stage];
        id<MTLBuffer> args=[impl_->device newBufferWithLength:argumentEncoder.encodedLength
            options:MTLResourceStorageModeShared];[slot.retained addObject:args];
        [argumentEncoder setArgumentBuffer:args offset:0];
        for(uint32_t i=0;i<stages[stage].count;++i){const Binding& binding=stages[stage].bindings[i];
            if(binding.resource==Res::RampImage){[argumentEncoder setTexture:ramp atIndex:binding.index];[encoder useResource:ramp usage:MTLResourceUsageRead];}
            else if(binding.resource==Res::DummyImage){[argumentEncoder setTexture:impl_->dummy atIndex:binding.index];[encoder useResource:impl_->dummy usage:MTLResourceUsageRead];}
            else if(binding.resource==Res::OutputImage){[argumentEncoder setTexture:impl_->output atIndex:binding.index];[encoder useResource:impl_->output usage:MTLResourceUsageWrite];}
            else{id<MTLBuffer> buffer=impl_->ResourceBuffer(slot,binding.resource,config,scene);[argumentEncoder setBuffer:buffer offset:0 atIndex:binding.index];[encoder useResource:buffer usage:binding.write?(MTLResourceUsageRead|MTLResourceUsageWrite):MTLResourceUsageRead];}}
        [encoder setComputePipelineState:impl_->pipelines[stage]];[encoder setBuffer:args offset:0 atIndex:0];
        MTLSize threads=stage==Fine?MTLSizeMake(4,16,1):((stage==PathCountSetup||stage==PathTilingSetup)?MTLSizeMake(1,1,1):MTLSizeMake(256,1,1));
        if(indirect)[encoder dispatchThreadgroupsWithIndirectBuffer:slot.buffers[(size_t)Res::Indirect].object indirectBufferOffset:0 threadsPerThreadgroup:threads];
        else [encoder dispatchThreadgroups:MTLSizeMake(x,y,z) threadsPerThreadgroup:threads];
        [encoder memoryBarrierWithScope:MTLBarrierScopeBuffers|MTLBarrierScopeTextures];[encoder endEncoding];
    };
    dispatch(PathtagReduce,ri.pathtagReduceWgs,1,1,false);dispatch(PathtagReduce2,ri.pathtagReduce2Wgs,1,1,false);
    dispatch(PathtagScan1,ri.pathtagScan1Wgs,1,1,false);dispatch(PathtagScan,ri.pathtagScanWgs,1,1,false);
    dispatch(BboxClear,ri.bboxClearWgs,1,1,false);dispatch(Flatten,ri.flattenWgs,1,1,false);
    dispatch(DrawReduce,ri.drawReduceWgs,1,1,false);dispatch(DrawLeaf,ri.drawReduceWgs,1,1,false);
    if(ri.clipReduceWgs)dispatch(ClipReduce,ri.clipReduceWgs,1,1,false);
    if(ri.clipLeafWgs)dispatch(ClipLeaf,ri.clipLeafWgs,1,1,false);
    dispatch(Binning,ri.binningWgs,1,1,false);dispatch(TileAlloc,ri.tileAllocWgs,1,1,false);
    dispatch(PathCountSetup,1,1,1,false);dispatch(PathCount,1,1,1,true);
    dispatch(Backdrop,ri.backdropWgs,1,1,false);dispatch(Coarse,ri.widthInBins,ri.heightInBins,1,false);
    dispatch(PathTilingSetup,1,1,1,false);dispatch(PathTiling,1,1,1,true);
    dispatch(Fine,ri.config.width_in_tiles,ri.config.height_in_tiles,1,false);
    return true;
}

id<MTLTexture> MetalVelloPipeline::OutputTexture()const{return impl_->output;}
VelloRenderRegion MetalVelloPipeline::OutputRegion()const{return impl_->region;}
void MetalVelloPipeline::ReclaimIdleResources()
{impl_->output=nil;for(auto& slot:impl_->slots){for(auto& b:slot.buffers){b.object=nil;b.capacity=0;}[slot.retained removeAllObjects];}}

} // namespace jalium
