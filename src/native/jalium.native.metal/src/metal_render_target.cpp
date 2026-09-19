#include "metal_backend.h"
#include "metal_internal.h"
#include "metal_shaders.h"
#include "metal_shader_compiler.h"
#include "metal_vello.h"
#include "jalium_impeller_stroke.h"
#include "jalium_rendering_engine.h"
#include "jalium_triangulate.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdlib>
#include <cstdio>
#include <cstring>
#include <functional>
#include <limits>
#include <mutex>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

#import <TargetConditionals.h>
#import <Metal/Metal.h>
#import <QuartzCore/QuartzCore.h>
#if TARGET_OS_OSX
#import <AppKit/AppKit.h>
#import <CoreGraphics/CoreGraphics.h>
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#else
#import <UIKit/UIKit.h>
#endif

namespace jalium {

namespace {

constexpr uint32_t kFrameCount = 3;
constexpr size_t kInitialVertexBytes = 4u * 1024u * 1024u;
constexpr float kPi = 3.14159265358979323846f;
constexpr size_t kClipCountParam = 208;
constexpr size_t kClipBaseParam = 212;
constexpr size_t kClipParamStride = 16;
constexpr size_t kMaxShaderClips = 18;

struct Matrix3x2 {
    float m11 = 1, m12 = 0, m21 = 0, m22 = 1, dx = 0, dy = 0;
};

Matrix3x2 Multiply(const Matrix3x2& a, const Matrix3x2& b)
{
    Matrix3x2 r;
    r.m11 = a.m11 * b.m11 + a.m21 * b.m12;
    r.m12 = a.m12 * b.m11 + a.m22 * b.m12;
    r.m21 = a.m11 * b.m21 + a.m21 * b.m22;
    r.m22 = a.m12 * b.m21 + a.m22 * b.m22;
    r.dx = a.m11 * b.dx + a.m21 * b.dy + a.dx;
    r.dy = a.m12 * b.dx + a.m22 * b.dy + a.dy;
    return r;
}

bool Invert(const Matrix3x2& value, Matrix3x2& result)
{
    float det = value.m11 * value.m22 - value.m12 * value.m21;
    if (std::abs(det) < 1e-8f || !std::isfinite(det)) return false;
    float inv = 1.0f / det;
    result.m11 = value.m22 * inv;
    result.m12 = -value.m12 * inv;
    result.m21 = -value.m21 * inv;
    result.m22 = value.m11 * inv;
    result.dx = -(result.m11 * value.dx + result.m21 * value.dy);
    result.dy = -(result.m12 * value.dx + result.m22 * value.dy);
    return true;
}

void Transform(const Matrix3x2& m, float x, float y, float& outX, float& outY)
{
    outX = m.m11 * x + m.m21 * y + m.dx;
    outY = m.m12 * x + m.m22 * y + m.dy;
}

struct RectF {
    float x = 0, y = 0, width = 0, height = 0;
};

RectF TransformBounds(const Matrix3x2& m, float x, float y, float w, float h)
{
    float xs[4], ys[4];
    Transform(m, x, y, xs[0], ys[0]);
    Transform(m, x + w, y, xs[1], ys[1]);
    Transform(m, x + w, y + h, xs[2], ys[2]);
    Transform(m, x, y + h, xs[3], ys[3]);
    auto [minX, maxX] = std::minmax_element(xs, xs + 4);
    auto [minY, maxY] = std::minmax_element(ys, ys + 4);
    return {*minX, *minY, *maxX - *minX, *maxY - *minY};
}

uint32_t NextPowerOfTwo(uint32_t value)
{
    if (value <= 1) return 1;
    --value;
    value |= value >> 1; value |= value >> 2; value |= value >> 4;
    value |= value >> 8; value |= value >> 16;
    return value + 1;
}

uint64_t NowNs()
{
    return static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count());
}

uint64_t Fnv1a64(const void* bytes, size_t size, uint64_t seed = 1469598103934665603ull)
{
    const auto* data = static_cast<const uint8_t*>(bytes);
    uint64_t value = seed;
    for (size_t i = 0; i < size; ++i) { value ^= data[i]; value *= 1099511628211ull; }
    return value;
}

struct StrokeVertex {
    float x, y;
    float r, g, b, a;
};

struct ClipState {
    RectF localRect;
    RectF deviceRect;
    Matrix3x2 deviceToLocal;
    float radii[4] = {};
    bool rounded = false;
    bool exclude = false;
    bool aliased = false;
};

struct CapturedTexture {
    id<MTLTexture> texture = nil;
    RectF bounds;
};

struct MetalRetainedLayer {
    id<MTLTexture> texture = nil;
    id<MTLHeap> heap = nil;
    RectF bounds;
    uint32_t width = 0;
    uint32_t height = 0;
    uint64_t generation = 0;
};

id<MTLTexture> CreateTexture(id<MTLDevice> device, uint32_t width,
    uint32_t height, MTLPixelFormat format = MTLPixelFormatBGRA8Unorm,
    MTLStorageMode storage = MTLStorageModePrivate)
{
    if (!device || width == 0 || height == 0) return nil;
    MTLTextureDescriptor* descriptor = [MTLTextureDescriptor
        texture2DDescriptorWithPixelFormat:format width:width height:height
        mipmapped:NO];
    descriptor.storageMode = storage;
    descriptor.usage = MTLTextureUsageRenderTarget | MTLTextureUsageShaderRead |
        MTLTextureUsageShaderWrite;
    return [device newTextureWithDescriptor:descriptor];
}

void ConfigurePremultipliedBlend(MTLRenderPipelineColorAttachmentDescriptor* color)
{
    color.blendingEnabled = YES;
    color.rgbBlendOperation = MTLBlendOperationAdd;
    color.alphaBlendOperation = MTLBlendOperationAdd;
    color.sourceRGBBlendFactor = MTLBlendFactorOne;
    color.sourceAlphaBlendFactor = MTLBlendFactorOne;
    color.destinationRGBBlendFactor = MTLBlendFactorOneMinusSourceAlpha;
    color.destinationAlphaBlendFactor = MTLBlendFactorOneMinusSourceAlpha;
}

NSString* FindMetallib(NSString* name)
{
    NSString* bundled = [NSBundle.mainBundle pathForResource:name ofType:@"metallib"];
    if (bundled.length != 0) return bundled;
    const char* overrideDirectory = std::getenv("JALIUM_METALLIB_DIR");
    if (overrideDirectory && *overrideDirectory) {
        NSString* directory = [NSString stringWithUTF8String:overrideDirectory];
        NSString* candidate = [directory stringByAppendingPathComponent:
            [name stringByAppendingPathExtension:@"metallib"]];
        if ([NSFileManager.defaultManager fileExistsAtPath:candidate]) return candidate;
    }
    NSString* executableDirectory = NSBundle.mainBundle.executablePath.stringByDeletingLastPathComponent;
    NSString* sibling = [executableDirectory stringByAppendingPathComponent:
        [name stringByAppendingPathExtension:@"metallib"]];
    return [NSFileManager.defaultManager fileExistsAtPath:sibling] ? sibling : nil;
}

} // namespace

struct MetalRenderTarget::Impl {
    MetalBackend* backend = nullptr;
    id<MTLDevice> device = nil;
    id<MTLCommandQueue> queue = nil;
    CAMetalLayer* layer = nil;
    id hostView = nil;
    bool composition = false;
    bool drawing = false;
    bool externalPacing = false;
    bool fullInvalidation = true;
    bool simulatedDeviceLost = false;
    uint32_t frameIndex = 0;
    uint32_t pixelWidth = 0;
    uint32_t pixelHeight = 0;
    float dpiX = 96.0f;
    float dpiY = 96.0f;
    uint32_t pathMsaa = 4;
    uint32_t pendingPathMsaa = 4;
    int shapeType = 0;
    float shapeExponent = 4.0f;
    float opacity = 1.0f;
    JaliumRenderingEngine activeEngine = JALIUM_ENGINE_IMPELLER;
    std::unique_ptr<MetalVelloPipeline> vello;
    VelloSceneEncoder velloEncoder;
    bool flushingVello = false;

    struct FrameResources {
        id<MTLBuffer> vertices = nil;
        size_t capacity = 0;
        size_t offset = 0;
        NSMutableArray<id<MTLBuffer>>* retiredBuffers = nil;
        NSMutableArray* transientHeaps = nil;
        NSMutableArray* transientResources = nil;
        NSMutableArray* gpuRetainedObjects = nil;
    } frames[kFrameCount];
    dispatch_semaphore_t inFlight = nullptr;
    id<MTLSharedEvent> completionEvent = nil;
    std::atomic<uint64_t> submittedEventValue{0};
    std::atomic<uint64_t> completedEventValue{0};

    id<MTLLibrary> library = nil;
    id<MTLRenderPipelineState> shapePipeline = nil;
    id<MTLRenderPipelineState> shapeReplacePipeline = nil;
    id<MTLRenderPipelineState> texturePipeline = nil;
    id<MTLRenderPipelineState> textureReplacePipeline = nil;
    id<MTLRenderPipelineState> presentPipeline = nil;
    id<MTLRenderPipelineState> yuvPipeline = nil;
    id<MTLRenderPipelineState> effectPipeline = nil;
    id<MTLRenderPipelineState> transitionPipeline = nil;
    id<MTLRenderPipelineState> clipPipeline = nil;
    id<MTLComputePipelineState> blurPipeline = nil;
    id<MTLDepthStencilState> contentStencilState = nil;
    id<MTLDepthStencilState> clipStencilState = nil;
    NSMutableDictionary<NSNumber*, id<MTLRenderPipelineState>>* customPipelines = nil;
    id<MTLSamplerState> linearSampler = nil;
    id<MTLSamplerState> nearestSampler = nil;
    id<MTLSamplerState> highQualitySampler = nil;

    id<MTLTexture> sceneTexture = nil;
    id<MTLTexture> msaaTexture = nil;
    id<MTLTexture> msaaBaselineTexture = nil;
    id<MTLTexture> stencilTexture = nil;
    id<MTLTexture> currentTarget = nil;
    id<MTLCommandBuffer> commandBuffer = nil;
    id<MTLRenderCommandEncoder> encoder = nil;
    FrameResources* frame = nullptr;

    std::vector<Matrix3x2> transforms{Matrix3x2{}};
    std::vector<float> opacities;
    std::vector<ClipState> clips;
    std::vector<RectF> dirtyRects;
    std::vector<std::pair<id<MTLTexture>, CapturedTexture>> captureStack;
    std::vector<CapturedTexture> activeEffectCaptures;
    CapturedTexture effectCapture;
    CapturedTexture transitionSlots[2];
    CapturedTexture desktopCapture;
    bool inEffectCapture = false;

    bool readbackRequested = false;
    id<MTLBuffer> readbackBuffer = nil;
    id<MTLCommandBuffer> readbackCommand = nil;
    uint32_t readbackWidth = 0;
    uint32_t readbackHeight = 0;
    uint32_t readbackRowBytes = 0;
    std::mutex readbackMutex;

    std::atomic<int64_t> lastGpuNs{0};
    std::atomic<int64_t> lastCpuNs{0};
    uint64_t beginCpuNs = 0;
    std::atomic<uint64_t> framesSubmitted{0};
    std::atomic<uint64_t> retainedOrphaned{0};
    std::atomic<uint64_t> retainedGraveyard{0};
    NSMutableArray* persistentHeaps = nil;
    NSMutableArray<id<MTLCommandBuffer>>* retirementCommands = nil;

    struct TextCacheEntry {
        id<MTLTexture> texture=nil;
        float x=0,y=0,width=0,height=0;
        uint64_t lastUse=0;
        size_t bytes=0;
    };
    std::unordered_map<uint64_t,TextCacheEntry> textCache;
    size_t textCacheBytes=0;
    uint64_t textCacheClock=0;

    void TrimTextCache()
    {
        constexpr size_t maxEntries=2048;
        constexpr size_t maxBytes=64u*1024u*1024u;
        while(textCache.size()>maxEntries||textCacheBytes>maxBytes){
            auto victim=textCache.end();
            for(auto it=textCache.begin();it!=textCache.end();++it)
                if(victim==textCache.end()||it->second.lastUse<victim->second.lastUse)victim=it;
            if(victim==textCache.end())break;textCacheBytes-=victim->second.bytes;
            textCache.erase(victim);
        }
    }

    id<MTLTexture> NewHeapTexture(NSMutableArray* heaps,
        NSMutableArray* resources, uint32_t width, uint32_t height,
        MTLPixelFormat format = MTLPixelFormatBGRA8Unorm)
    {
        if (!device || !heaps || width == 0 || height == 0) return nil;
        MTLTextureDescriptor* descriptor = [MTLTextureDescriptor
            texture2DDescriptorWithPixelFormat:format width:width height:height
            mipmapped:NO];
        descriptor.storageMode = MTLStorageModePrivate;
        descriptor.usage = MTLTextureUsageRenderTarget | MTLTextureUsageShaderRead |
            MTLTextureUsageShaderWrite;
        for (id value in heaps) {
            id<MTLHeap> heap = (id<MTLHeap>)value;
            id<MTLTexture> texture = [heap newTextureWithDescriptor:descriptor];
            if (texture) {
                if (resources) [resources addObject:texture];
                return texture;
            }
        }
        MTLSizeAndAlign requirement = [device heapTextureSizeAndAlignWithDescriptor:descriptor];
        NSUInteger alignment = std::max<NSUInteger>(requirement.align, 1);
        NSUInteger requested = (requirement.size + alignment - 1) & ~(alignment - 1);
        NSUInteger heapSize = std::max<NSUInteger>(requested * 4,
            16u * 1024u * 1024u);
        MTLHeapDescriptor* heapDescriptor = [MTLHeapDescriptor new];
        heapDescriptor.size = heapSize;
        heapDescriptor.storageMode = MTLStorageModePrivate;
        heapDescriptor.hazardTrackingMode = MTLHazardTrackingModeTracked;
        heapDescriptor.type = MTLHeapTypeAutomatic;
        id<MTLHeap> heap = [device newHeapWithDescriptor:heapDescriptor];
        if (!heap) return nil;
        [heaps addObject:heap];
        id<MTLTexture> texture = [heap newTextureWithDescriptor:descriptor];
        if (texture && resources) [resources addObject:texture];
        return texture;
    }

    id<MTLTexture> NewTransientTexture(uint32_t width, uint32_t height,
        MTLPixelFormat format = MTLPixelFormatBGRA8Unorm)
    {
        if (!frame) return CreateTexture(device, width, height, format);
        return NewHeapTexture(frame->transientHeaps, frame->transientResources,
            width, height, format);
    }

    id<MTLTexture> NewPersistentTexture(uint32_t width, uint32_t height,
        id<MTLHeap>* heapOut)
    {
        if (!persistentHeaps) persistentHeaps = [NSMutableArray array];
        id<MTLTexture> texture = NewHeapTexture(persistentHeaps, nil, width, height);
        if (heapOut) *heapOut = texture ? texture.heap : nil;
        return texture;
    }

    bool InitializePipelines()
    {
        NSError* error = nil;
        NSString* corePath = FindMetallib(@"jalium_core");
        if (corePath) {
            library = [device newLibraryWithURL:[NSURL fileURLWithPath:corePath]
                error:&error];
        }
        // Source trees and debugger-hosted native tests remain self-contained.
        // Formal packages always carry jalium_core.metallib and therefore do
        // not pay this runtime compilation cost.
        if (!library) {
            library = [device newLibraryWithSource:
                [NSString stringWithUTF8String:kMetalCoreShaderSource]
                options:nil error:&error];
        }
        if (!library) return false;
        id<MTLFunction> vertex = [library newFunctionWithName:@"jalium_vertex"];
        id<MTLFunction> shape = [library newFunctionWithName:@"jalium_shape_fragment"];
        id<MTLFunction> texture = [library newFunctionWithName:@"jalium_texture_fragment"];
        id<MTLFunction> yuv = [library newFunctionWithName:@"jalium_yuv_fragment"];
        id<MTLFunction> effect = [library newFunctionWithName:@"jalium_effect_fragment"];
        id<MTLFunction> transition=[library newFunctionWithName:@"jalium_transition_fragment"];
        id<MTLFunction> clip = [library newFunctionWithName:@"jalium_clip_stencil_fragment"];
        id<MTLFunction> blur = [library newFunctionWithName:@"jalium_blur"];
        if (!vertex || !shape || !texture || !yuv || !effect || !transition || !clip || !blur) return false;

        auto createRenderPipeline = [&](id<MTLFunction> fragment, bool blending,
                                        bool usesStencil = true) {
            MTLRenderPipelineDescriptor* descriptor = [MTLRenderPipelineDescriptor new];
            descriptor.vertexFunction = vertex;
            descriptor.fragmentFunction = fragment;
            descriptor.colorAttachments[0].pixelFormat = MTLPixelFormatBGRA8Unorm;
            descriptor.rasterSampleCount = usesStencil ? pathMsaa : 1;
            if (usesStencil)
                descriptor.stencilAttachmentPixelFormat = MTLPixelFormatStencil8;
            if (blending) ConfigurePremultipliedBlend(descriptor.colorAttachments[0]);
            return [device newRenderPipelineStateWithDescriptor:descriptor error:&error];
        };
        shapePipeline = createRenderPipeline(shape, true);
        shapeReplacePipeline = createRenderPipeline(shape, false);
        texturePipeline = createRenderPipeline(texture, true);
        textureReplacePipeline = createRenderPipeline(texture, false);
        presentPipeline = createRenderPipeline(texture, false, false);
        yuvPipeline = createRenderPipeline(yuv, true);
        effectPipeline = createRenderPipeline(effect, true);
        transitionPipeline=createRenderPipeline(transition,true);
        {
            MTLRenderPipelineDescriptor* descriptor = [MTLRenderPipelineDescriptor new];
            descriptor.vertexFunction = vertex;
            descriptor.fragmentFunction = clip;
            descriptor.colorAttachments[0].pixelFormat = MTLPixelFormatBGRA8Unorm;
            descriptor.colorAttachments[0].writeMask = MTLColorWriteMaskNone;
            descriptor.stencilAttachmentPixelFormat = MTLPixelFormatStencil8;
            descriptor.rasterSampleCount = pathMsaa;
            clipPipeline = [device newRenderPipelineStateWithDescriptor:descriptor error:&error];
        }
        blurPipeline = [device newComputePipelineStateWithFunction:blur error:&error];

        MTLStencilDescriptor* contentStencil = [MTLStencilDescriptor new];
        contentStencil.stencilCompareFunction = MTLCompareFunctionEqual;
        contentStencil.stencilFailureOperation = MTLStencilOperationKeep;
        contentStencil.depthFailureOperation = MTLStencilOperationKeep;
        contentStencil.depthStencilPassOperation = MTLStencilOperationKeep;
        MTLDepthStencilDescriptor* contentDepth = [MTLDepthStencilDescriptor new];
        contentDepth.frontFaceStencil = contentStencil;
        contentDepth.backFaceStencil = contentStencil;
        contentStencilState = [device newDepthStencilStateWithDescriptor:contentDepth];

        MTLStencilDescriptor* clipStencil = [MTLStencilDescriptor new];
        clipStencil.stencilCompareFunction = MTLCompareFunctionEqual;
        clipStencil.stencilFailureOperation = MTLStencilOperationKeep;
        clipStencil.depthFailureOperation = MTLStencilOperationKeep;
        clipStencil.depthStencilPassOperation = MTLStencilOperationIncrementClamp;
        MTLDepthStencilDescriptor* clipDepth = [MTLDepthStencilDescriptor new];
        clipDepth.frontFaceStencil = clipStencil;
        clipDepth.backFaceStencil = clipStencil;
        clipStencilState = [device newDepthStencilStateWithDescriptor:clipDepth];

        MTLSamplerDescriptor* sampler = [MTLSamplerDescriptor new];
        sampler.minFilter = MTLSamplerMinMagFilterLinear;
        sampler.magFilter = MTLSamplerMinMagFilterLinear;
        sampler.mipFilter = MTLSamplerMipFilterNotMipmapped;
        sampler.sAddressMode = sampler.tAddressMode = MTLSamplerAddressModeClampToEdge;
        linearSampler = [device newSamplerStateWithDescriptor:sampler];
        sampler.minFilter = MTLSamplerMinMagFilterNearest;
        sampler.magFilter = MTLSamplerMinMagFilterNearest;
        nearestSampler = [device newSamplerStateWithDescriptor:sampler];
        sampler.minFilter = MTLSamplerMinMagFilterLinear;
        sampler.magFilter = MTLSamplerMinMagFilterLinear;
        sampler.mipFilter = MTLSamplerMipFilterLinear;
        sampler.maxAnisotropy = 8;
        highQualitySampler = [device newSamplerStateWithDescriptor:sampler];
        customPipelines = [NSMutableDictionary dictionary];
        if(!persistentHeaps)persistentHeaps = [NSMutableArray array];
        if(!retirementCommands)retirementCommands = [NSMutableArray array];
        return shapePipeline && shapeReplacePipeline && texturePipeline &&
            textureReplacePipeline && presentPipeline && yuvPipeline && effectPipeline && transitionPipeline &&
            clipPipeline && blurPipeline && contentStencilState && clipStencilState &&
            linearSampler && nearestSampler && highQualitySampler;
    }

    id<MTLRenderPipelineState> CustomPipeline(const char* hlsl)
    {
        if(!hlsl||!*hlsl)return nil;
        uint64_t hash=Fnv1a64(hlsl,std::strlen(hlsl));
#ifdef JALIUM_METAL_SHADER_CACHE_ABI
        constexpr const char* cacheAbi=JALIUM_METAL_SHADER_CACHE_ABI;
#else
        constexpr const char* cacheAbi="source-only-msl30000-v1";
#endif
        hash=Fnv1a64(cacheAbi,std::strlen(cacheAbi),hash);
        uint64_t registryId=device.registryID;hash=Fnv1a64(&registryId,sizeof(registryId),hash);
        const char* deviceName=device.name.UTF8String;if(deviceName)
            hash=Fnv1a64(deviceName,std::strlen(deviceName),hash);
        NSNumber* key=@(hash);
        id<MTLRenderPipelineState> cached=customPipelines[key];if(cached)return cached;
        std::string msl,entry,errorText;
        NSString* cacheDirectory=nil;NSArray<NSString*>* roots=NSSearchPathForDirectoriesInDomains(
            NSCachesDirectory,NSUserDomainMask,YES);
        if(roots.count){cacheDirectory=[roots[0] stringByAppendingPathComponent:
            @"Jalium/MetalShaders"];
            [NSFileManager.defaultManager createDirectoryAtPath:cacheDirectory
                withIntermediateDirectories:YES attributes:nil error:nil];}
        NSString* cachePath=cacheDirectory?[cacheDirectory stringByAppendingPathComponent:
            [NSString stringWithFormat:@"%016llx.metal",(unsigned long long)hash]]:nil;
        bool loadedFromDisk=false;
        if(cachePath){NSString* cachedSource=[NSString stringWithContentsOfFile:cachePath
                encoding:NSUTF8StringEncoding error:nil];
            if(cachedSource.length){msl=cachedSource.UTF8String;entry="jalium_custom_fragment";
                loadedFromDisk=true;}}
        if(msl.empty()&&!CompileMetalPixelShader(hlsl,msl,entry,errorText))return nil;
        NSError* error=nil;
        auto makeLibrary=[&](){return [device newLibraryWithSource:
            [NSString stringWithUTF8String:msl.c_str()] options:nil error:&error];};
        id<MTLLibrary> custom=makeLibrary();
        if(!custom&&loadedFromDisk){[NSFileManager.defaultManager removeItemAtPath:cachePath error:nil];
            msl.clear();entry.clear();errorText.clear();
            if(!CompileMetalPixelShader(hlsl,msl,entry,errorText))return nil;
            error=nil;custom=makeLibrary();loadedFromDisk=false;}
        if(!custom)return nil;
        if(cachePath&&!loadedFromDisk&&!msl.empty())[[NSString stringWithUTF8String:msl.c_str()]
            writeToFile:cachePath atomically:YES encoding:NSUTF8StringEncoding error:nil];
        id<MTLFunction> fragment=[custom newFunctionWithName:
            [NSString stringWithUTF8String:entry.c_str()]];
        id<MTLFunction> vertex=[library newFunctionWithName:@"jalium_vertex"];
        if(!fragment||!vertex)return nil;
        MTLRenderPipelineDescriptor* descriptor=[MTLRenderPipelineDescriptor new];
        descriptor.vertexFunction=vertex;descriptor.fragmentFunction=fragment;
        descriptor.colorAttachments[0].pixelFormat=MTLPixelFormatBGRA8Unorm;
        descriptor.stencilAttachmentPixelFormat=MTLPixelFormatStencil8;
        descriptor.rasterSampleCount=pathMsaa;
        ConfigurePremultipliedBlend(descriptor.colorAttachments[0]);
        id<MTLRenderPipelineState> pipeline=[device newRenderPipelineStateWithDescriptor:descriptor error:&error];
        if(pipeline)customPipelines[key]=pipeline;return pipeline;
    }

    Matrix3x2 DeviceTransform() const
    {
        Matrix3x2 dpi;
        dpi.m11 = dpiX / 96.0f;
        dpi.m22 = dpiY / 96.0f;
        return Multiply(dpi, transforms.back());
    }

    bool EnsureSceneTexture()
    {
        if (sceneTexture && sceneTexture.width == pixelWidth &&
            sceneTexture.height == pixelHeight && stencilTexture &&
            stencilTexture.width == pixelWidth && stencilTexture.height == pixelHeight &&
            stencilTexture.sampleCount == pathMsaa &&
            (pathMsaa == 1 || (msaaTexture && msaaTexture.width == pixelWidth &&
                msaaTexture.height == pixelHeight && msaaTexture.sampleCount == pathMsaa &&
                msaaBaselineTexture && msaaBaselineTexture.width == pixelWidth &&
                msaaBaselineTexture.height == pixelHeight)))
            return true;
        sceneTexture = CreateTexture(device, pixelWidth, pixelHeight);
        if(pathMsaa>1){MTLTextureDescriptor* multisample=[MTLTextureDescriptor new];
            multisample.textureType=MTLTextureType2DMultisample;
            multisample.pixelFormat=MTLPixelFormatBGRA8Unorm;
            multisample.width=pixelWidth;multisample.height=pixelHeight;
            multisample.sampleCount=pathMsaa;multisample.usage=MTLTextureUsageRenderTarget;
#if TARGET_OS_OSX
            multisample.storageMode=MTLStorageModePrivate;
#else
            multisample.storageMode=MTLStorageModeMemoryless;
#endif
            msaaTexture=[device newTextureWithDescriptor:multisample];
            msaaBaselineTexture=CreateTexture(device,pixelWidth,pixelHeight);
        }else{msaaTexture=nil;msaaBaselineTexture=nil;}
        MTLTextureDescriptor* stencil = [MTLTextureDescriptor new];
        stencil.textureType=pathMsaa>1?MTLTextureType2DMultisample:MTLTextureType2D;
        stencil.pixelFormat=MTLPixelFormatStencil8;stencil.width=pixelWidth;
        stencil.height=pixelHeight;stencil.sampleCount=pathMsaa;
        stencil.usage = MTLTextureUsageRenderTarget;
#if TARGET_OS_OSX
        stencil.storageMode = MTLStorageModePrivate;
#else
        // The complete clip stack is rebuilt whenever a render encoder starts,
        // so tile-local stencil never needs to leave on-chip memory.
        stencil.storageMode = MTLStorageModeMemoryless;
#endif
        stencilTexture = [device newTextureWithDescriptor:stencil];
        fullInvalidation = true;
        return sceneTexture != nil && stencilTexture != nil &&
            (pathMsaa==1||(msaaTexture!=nil&&msaaBaselineTexture!=nil));
    }

    void EndEncoder()
    {
        if (encoder) { [encoder endEncoding]; encoder = nil; }
    }

    void EncodeStencilStack(id<MTLRenderCommandEncoder> targetEncoder)
    {
        if (!targetEncoder || clips.empty() || !clipPipeline || !clipStencilState)
            return;
        for (size_t i = 0; i < clips.size() && i < 255; ++i) {
            const ClipState& clip = clips[i];
            MetalShaderParams p{};
            p[0] = static_cast<float>(pixelWidth);
            p[1] = static_cast<float>(pixelHeight);
            p[kClipCountParam] = 1.0f;
            const size_t base = kClipBaseParam;
            p[base + 0] = clip.deviceToLocal.m11;
            p[base + 1] = clip.deviceToLocal.m12;
            p[base + 2] = clip.deviceToLocal.m21;
            p[base + 3] = clip.deviceToLocal.m22;
            p[base + 4] = clip.deviceToLocal.dx;
            p[base + 5] = clip.deviceToLocal.dy;
            p[base + 6] = clip.localRect.x;
            p[base + 7] = clip.localRect.y;
            p[base + 8] = clip.localRect.width;
            p[base + 9] = clip.localRect.height;
            p[base + 10] = clip.radii[0];
            p[base + 11] = clip.radii[1];
            p[base + 12] = clip.radii[2];
            p[base + 13] = clip.radii[3];
            p[base + 14] = static_cast<float>(1u |
                (clip.exclude ? 2u : 0u) | (clip.aliased ? 4u : 0u));

            std::vector<MetalVertex> vertices;
            if (clip.exclude) {
                vertices = DeviceQuad(0, 0, static_cast<float>(pixelWidth),
                    static_cast<float>(pixelHeight));
            } else {
                constexpr float edge = 2.0f;
                vertices = DeviceQuad(clip.deviceRect.x - edge,
                    clip.deviceRect.y - edge, clip.deviceRect.width + edge * 2,
                    clip.deviceRect.height + edge * 2);
            }
            NSUInteger offset = 0;
            if (!UploadVertices(vertices.data(), vertices.size(), offset)) return;
            [targetEncoder setRenderPipelineState:clipPipeline];
            [targetEncoder setDepthStencilState:clipStencilState];
            [targetEncoder setStencilReferenceValue:static_cast<uint32_t>(i)];
            [targetEncoder setVertexBuffer:frame->vertices offset:offset atIndex:0];
            [targetEncoder setVertexBytes:p.data() length:p.size()*sizeof(float) atIndex:1];
            [targetEncoder setFragmentBytes:p.data() length:p.size()*sizeof(float) atIndex:0];
            [targetEncoder drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0
                vertexCount:static_cast<NSUInteger>(vertices.size())];
        }
    }

    MTLScissorRect CurrentScissor() const
    {
        float left = 0, top = 0, right = static_cast<float>(pixelWidth),
            bottom = static_cast<float>(pixelHeight);
        if (!fullInvalidation && !dirtyRects.empty()) {
            left = std::numeric_limits<float>::infinity();
            top = std::numeric_limits<float>::infinity();
            right = -std::numeric_limits<float>::infinity();
            bottom = -std::numeric_limits<float>::infinity();
            for (const RectF& r : dirtyRects) {
                left = std::min(left, r.x); top = std::min(top, r.y);
                right = std::max(right, r.x + r.width);
                bottom = std::max(bottom, r.y + r.height);
            }
        }
        for (const ClipState& clip : clips) {
            if (clip.exclude) continue;
            left = std::max(left, clip.deviceRect.x);
            top = std::max(top, clip.deviceRect.y);
            right = std::min(right, clip.deviceRect.x + clip.deviceRect.width);
            bottom = std::min(bottom, clip.deviceRect.y + clip.deviceRect.height);
        }
        left = std::clamp(left, 0.0f, static_cast<float>(pixelWidth));
        top = std::clamp(top, 0.0f, static_cast<float>(pixelHeight));
        right = std::clamp(right, left, static_cast<float>(pixelWidth));
        bottom = std::clamp(bottom, top, static_cast<float>(pixelHeight));
        return MTLScissorRect{static_cast<NSUInteger>(std::floor(left)),
            static_cast<NSUInteger>(std::floor(top)),
            static_cast<NSUInteger>(std::ceil(right - left)),
            static_cast<NSUInteger>(std::ceil(bottom - top))};
    }

    id<MTLRenderCommandEncoder> EnsureEncoder(bool clear = false,
        MTLClearColor color = MTLClearColorMake(0, 0, 0, 0))
    {
        if (!flushingVello && vello && vello->IsReady() &&
            velloEncoder.HasWork()) FlushVello();
        if (encoder && !clear) return encoder;
        EndEncoder();
        if (!commandBuffer || !currentTarget) return nil;
        if(pathMsaa>1&&!clear){
            id<MTLBlitCommandEncoder> baselineBlit=[commandBuffer blitCommandEncoder];
            if(!baselineBlit)return nil;
            [baselineBlit copyFromTexture:currentTarget sourceSlice:0 sourceLevel:0
                sourceOrigin:MTLOriginMake(0,0,0)
                sourceSize:MTLSizeMake(pixelWidth,pixelHeight,1)
                toTexture:msaaBaselineTexture destinationSlice:0 destinationLevel:0
                destinationOrigin:MTLOriginMake(0,0,0)];
            [baselineBlit endEncoding];
        }
        MTLRenderPassDescriptor* pass = [MTLRenderPassDescriptor renderPassDescriptor];
        pass.colorAttachments[0].texture = pathMsaa>1?msaaTexture:currentTarget;
        pass.colorAttachments[0].resolveTexture = pathMsaa>1?currentTarget:nil;
        pass.colorAttachments[0].loadAction = pathMsaa>1?MTLLoadActionClear:
            (clear ? MTLLoadActionClear : MTLLoadActionLoad);
        pass.colorAttachments[0].storeAction = pathMsaa>1?
            MTLStoreActionMultisampleResolve:MTLStoreActionStore;
        pass.colorAttachments[0].clearColor = color;
        pass.stencilAttachment.texture = stencilTexture;
        pass.stencilAttachment.loadAction = MTLLoadActionClear;
        pass.stencilAttachment.storeAction = MTLStoreActionDontCare;
        pass.stencilAttachment.clearStencil = 0;
        encoder = [commandBuffer renderCommandEncoderWithDescriptor:pass];
        if (encoder) {
            encoder.label = @"Jalium Metal 2D";
            if(pathMsaa>1&&!clear){
                MTLScissorRect full{0,0,pixelWidth,pixelHeight};[encoder setScissorRect:full];
                MetalShaderParams baseline{};baseline[0]=(float)pixelWidth;
                baseline[1]=(float)pixelHeight;baseline[17]=1;
                auto vertices=DeviceQuad(0,0,(float)pixelWidth,(float)pixelHeight);
                NSUInteger offset=0;if(UploadVertices(vertices.data(),vertices.size(),offset)){
                    [encoder setRenderPipelineState:textureReplacePipeline];
                    [encoder setVertexBuffer:frame->vertices offset:offset atIndex:0];
                    [encoder setVertexBytes:baseline.data() length:baseline.size()*sizeof(float) atIndex:1];
                    [encoder setFragmentBytes:baseline.data() length:baseline.size()*sizeof(float) atIndex:0];
                    [encoder setFragmentTexture:msaaBaselineTexture atIndex:0];
                    [encoder setFragmentSamplerState:nearestSampler atIndex:0];
                    if(frame&&frame->gpuRetainedObjects)[frame->gpuRetainedObjects addObject:msaaBaselineTexture];
                    [encoder drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0 vertexCount:6];
                }
            }
            MTLScissorRect scissor = CurrentScissor();
            if (scissor.width > 0 && scissor.height > 0)
                [encoder setScissorRect:scissor];
            EncodeStencilStack(encoder);
            [encoder setDepthStencilState:contentStencilState];
            [encoder setStencilReferenceValue:static_cast<uint32_t>(
                std::min<size_t>(clips.size(), 255))];
        }
        return encoder;
    }

    bool UploadVertices(const MetalVertex* vertices, size_t count,
        NSUInteger& offsetOut)
    {
        if (!frame || !vertices || count == 0) return false;
        const size_t bytes = count * sizeof(MetalVertex);
        const size_t aligned = (frame->offset + 15u) & ~size_t(15u);
        if (!frame->vertices || aligned + bytes > frame->capacity) {
            size_t required = std::max(kInitialVertexBytes, aligned + bytes);
            required = NextPowerOfTwo(static_cast<uint32_t>(
                std::min<size_t>(required, std::numeric_limits<uint32_t>::max())));
            if (frame->vertices) [frame->retiredBuffers addObject:frame->vertices];
            frame->vertices = [device newBufferWithLength:required
                options:MTLResourceStorageModeShared];
            frame->capacity = frame->vertices ? required : 0;
            frame->offset = 0;
        }
        if (!frame->vertices || bytes > frame->capacity) return false;
        offsetOut = static_cast<NSUInteger>(frame->offset);
        std::memcpy(static_cast<uint8_t*>(frame->vertices.contents) + frame->offset,
            vertices, bytes);
#if TARGET_OS_OSX
        [frame->vertices didModifyRange:NSMakeRange(frame->offset, bytes)];
#endif
        frame->offset += bytes;
        return true;
    }

    void PopulateBrush(MetalShaderParams& p, Brush* brush) const
    {
        p[3] = 0;
        p[4] = p[5] = p[6] = 0; p[7] = 1;
        if (auto* solid = dynamic_cast<MetalSolidBrush*>(brush)) {
            p[4] = solid->r; p[5] = solid->g; p[6] = solid->b; p[7] = solid->a;
            return;
        }
        const std::vector<JaliumGradientStop>* stops = nullptr;
        uint32_t spread = 0;
        if (auto* linear = dynamic_cast<MetalLinearGradientBrush*>(brush)) {
            p[3] = 1; p[36] = linear->startX; p[37] = linear->startY;
            p[38] = linear->endX; p[39] = linear->endY;
            stops = &linear->stops; spread = linear->spreadMethod;
        } else if (auto* radial = dynamic_cast<MetalRadialGradientBrush*>(brush)) {
            p[3] = 2; p[36] = radial->centerX; p[37] = radial->centerY;
            p[38] = radial->radiusX; p[39] = radial->radiusY;
            p[40] = radial->originX; p[41] = radial->originY;
            stops = &radial->stops; spread = radial->spreadMethod;
        }
        if (!stops) return;
        p[44] = static_cast<float>(spread);
        uint32_t count = std::min<uint32_t>(static_cast<uint32_t>(stops->size()), 32);
        p[45] = static_cast<float>(count);
        for (uint32_t i = 0; i < count; ++i) {
            size_t base = 48 + static_cast<size_t>(i) * 5;
            p[base] = (*stops)[i].position;
            p[base + 1] = (*stops)[i].r;
            p[base + 2] = (*stops)[i].g;
            p[base + 3] = (*stops)[i].b;
            p[base + 4] = (*stops)[i].a;
        }
    }

    MetalShaderParams Params(Brush* brush, uint32_t primitive) const
    {
        MetalShaderParams p{};
        p[0] = static_cast<float>(pixelWidth); p[1] = static_cast<float>(pixelHeight);
        p[2] = static_cast<float>(primitive); p[17] = opacity;
        Matrix3x2 inverse;
        if (!Invert(DeviceTransform(), inverse)) inverse = {};
        p[20] = inverse.m11; p[21] = inverse.m12; p[22] = inverse.m21;
        p[23] = inverse.m22; p[24] = inverse.dx; p[25] = inverse.dy;
        const size_t clipCount = std::min(clips.size(), kMaxShaderClips);
        p[kClipCountParam] = static_cast<float>(clipCount);
        // Keep the innermost clips when an adversarial tree exceeds the shader
        // fast-path depth. The stencil fallback handles the complete stack;
        // these constants supply the antialiased edge coverage.
        const size_t first = clips.size() - clipCount;
        for (size_t i = 0; i < clipCount; ++i) {
            const ClipState& clip = clips[first + i];
            const size_t base = kClipBaseParam + i * kClipParamStride;
            p[base + 0] = clip.deviceToLocal.m11;
            p[base + 1] = clip.deviceToLocal.m12;
            p[base + 2] = clip.deviceToLocal.m21;
            p[base + 3] = clip.deviceToLocal.m22;
            p[base + 4] = clip.deviceToLocal.dx;
            p[base + 5] = clip.deviceToLocal.dy;
            p[base + 6] = clip.localRect.x;
            p[base + 7] = clip.localRect.y;
            p[base + 8] = clip.localRect.width;
            p[base + 9] = clip.localRect.height;
            p[base + 10] = clip.radii[0];
            p[base + 11] = clip.radii[1];
            p[base + 12] = clip.radii[2];
            p[base + 13] = clip.radii[3];
            uint32_t mode = 1u | (clip.exclude ? 2u : 0u) |
                (clip.aliased ? 4u : 0u);
            p[base + 14] = static_cast<float>(mode);
        }
        PopulateBrush(p, brush);
        return p;
    }

    bool Draw(const std::vector<MetalVertex>& vertices,
        id<MTLRenderPipelineState> pipeline, MetalShaderParams& params,
        id<MTLTexture> texture = nil, id<MTLSamplerState> sampler = nil)
    {
        if (vertices.empty() || !pipeline) return false;
        id<MTLRenderCommandEncoder> e = EnsureEncoder();
        if (!e) return false;
        NSUInteger offset = 0;
        if (!UploadVertices(vertices.data(), vertices.size(), offset)) return false;
        [e setRenderPipelineState:pipeline];
        [e setDepthStencilState:contentStencilState];
        [e setStencilReferenceValue:static_cast<uint32_t>(
            std::min<size_t>(clips.size(), 255))];
        [e setVertexBuffer:frame->vertices offset:offset atIndex:0];
        [e setVertexBytes:params.data() length:params.size() * sizeof(float) atIndex:1];
        [e setFragmentBytes:params.data() length:params.size() * sizeof(float) atIndex:0];
        if (texture) {
            [e setFragmentTexture:texture atIndex:0];
            if(frame&&frame->gpuRetainedObjects)[frame->gpuRetainedObjects addObject:texture];
        }
        if (sampler) [e setFragmentSamplerState:sampler atIndex:0];
        [e drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0
            vertexCount:static_cast<NSUInteger>(vertices.size())];
        return true;
    }

    std::vector<MetalVertex> Quad(float x, float y, float w, float h,
        float u0 = 0, float v0 = 0, float u1 = 1, float v1 = 1) const
    {
        Matrix3x2 transform = DeviceTransform();
        float x0, y0, x1, y1, x2, y2, x3, y3;
        Transform(transform, x, y, x0, y0);
        Transform(transform, x + w, y, x1, y1);
        Transform(transform, x + w, y + h, x2, y2);
        Transform(transform, x, y + h, x3, y3);
        return {{x0,y0,u0,v0},{x1,y1,u1,v0},{x2,y2,u1,v1},
                {x0,y0,u0,v0},{x2,y2,u1,v1},{x3,y3,u0,v1}};
    }

    static std::vector<MetalVertex> DeviceQuad(float x, float y, float w, float h,
        float u0 = 0, float v0 = 0, float u1 = 1, float v1 = 1)
    {
        return {{x,y,u0,v0},{x+w,y,u1,v0},{x+w,y+h,u1,v1},
                {x,y,u0,v0},{x+w,y+h,u1,v1},{x,y+h,u0,v1}};
    }

    void DrawShape(float x, float y, float w, float h, const float radii[4],
        float stroke, Brush* brush, uint32_t primitive, bool replace = false)
    {
        if (!drawing || !brush || w <= 0 || h <= 0) return;
        MetalShaderParams p = Params(brush, primitive);
        p[8] = x; p[9] = y; p[10] = w; p[11] = h;
        p[12] = radii[0]; p[13] = radii[1];
        p[14] = radii[2]; p[15] = radii[3];
        p[16] = std::max(stroke, 0.0f); p[18] = shapeExponent;
        Draw(Quad(x - 1, y - 1, w + 2, h + 2),
            replace ? shapeReplacePipeline : shapePipeline, p);
    }

    void DrawTexture(id<MTLTexture> texture, float x, float y, float w,
        float h, float opacityValue, int scalingMode, RectF source,
        bool replace = false, bool tint = false, const float* tintColor = nullptr)
    {
        if (!texture || w <= 0 || h <= 0) return;
        if (frame && frame->gpuRetainedObjects)
            [frame->gpuRetainedObjects addObject:texture];
        float u0 = source.width > 0 ? source.x / texture.width : 0;
        float v0 = source.height > 0 ? source.y / texture.height : 0;
        float u1 = source.width > 0 ? (source.x + source.width) / texture.width : 1;
        float v1 = source.height > 0 ? (source.y + source.height) / texture.height : 1;
        MetalShaderParams p = Params(nullptr, 0);
        p[17] = opacity * opacityValue;
        if (tint && tintColor) {
            p[4] = tintColor[0]; p[5] = tintColor[1]; p[6] = tintColor[2];
            p[7] = tintColor[3]; p[179] = 1;
        }
        id<MTLSamplerState> sampler = scalingMode == 3 ? nearestSampler :
            ((scalingMode == 0 || scalingMode == 2) ? highQualitySampler : linearSampler);
        Draw(Quad(x, y, w, h, u0, v0, u1, v1),
            replace ? textureReplacePipeline : texturePipeline, p, texture, sampler);
    }

    EngineBrushData EngineBrush(Brush* brush) const
    {
        EngineBrushData result{};
        if(auto* solid=dynamic_cast<MetalSolidBrush*>(brush)){
            result.type=0;result.r=solid->r;result.g=solid->g;result.b=solid->b;result.a=solid->a;
        }else if(auto* linear=dynamic_cast<MetalLinearGradientBrush*>(brush)){
            result.type=1;result.startX=linear->startX;result.startY=linear->startY;
            result.endX=linear->endX;result.endY=linear->endY;result.spreadMethod=linear->spreadMethod;
            result.stops=reinterpret_cast<const EngineBrushData::GradientStop*>(linear->stops.data());
            result.stopCount=static_cast<uint32_t>(linear->stops.size());
        }else if(auto* radial=dynamic_cast<MetalRadialGradientBrush*>(brush)){
            result.type=2;result.centerX=radial->centerX;result.centerY=radial->centerY;
            result.radiusX=radial->radiusX;result.radiusY=radial->radiusY;
            result.originX=radial->originX;result.originY=radial->originY;
            result.spreadMethod=radial->spreadMethod;
            result.stops=reinterpret_cast<const EngineBrushData::GradientStop*>(radial->stops.data());
            result.stopCount=static_cast<uint32_t>(radial->stops.size());
        }
        return result;
    }

    EngineTransform VelloTransform() const
    {
        Matrix3x2 m=DeviceTransform();
        return {m.m11,m.m12,m.m21,m.m22,m.dx,m.dy};
    }

    void DrawDeviceTexture(id<MTLTexture> texture,const VelloRenderRegion& r)
    {
        if(!texture||r.Empty())return;
        MetalShaderParams p=Params(nullptr,0);p[17]=1;
        float x=(float)r.originX,y=(float)r.originY,w=(float)r.width,h=(float)r.height;
        float u=(float)r.width/texture.width,v=(float)r.height/texture.height;
        std::vector<MetalVertex> q={{x,y,0,0},{x+w,y,u,0},{x+w,y+h,u,v},
            {x,y,0,0},{x+w,y+h,u,v},{x,y+h,0,v}};
        Draw(q,texturePipeline,p,texture,nearestSampler);
    }

    void FlushVello()
    {
        if(flushingVello||!vello||!vello->IsReady()||!velloEncoder.HasWork()||!commandBuffer)return;
        flushingVello=true;EndEncoder();VelloSubScene sub;
        if(velloEncoder.CutSubScene(sub)&&vello->Record(commandBuffer,sub,
            (frameIndex-1)%kFrameCount))DrawDeviceTexture(vello->OutputTexture(),vello->OutputRegion());
        MTLScissorRect scissor=CurrentScissor();
        if(!clips.empty())velloEncoder.SetScissor((float)scissor.x,(float)scissor.y,
            (float)(scissor.x+scissor.width),(float)(scissor.y+scissor.height));
        flushingVello=false;
    }

    void DrawDeviceTexture(id<MTLTexture> texture, float x, float y, float w,
        float h, float opacityValue)
    {
        if (!texture || w <= 0 || h <= 0) return;
        MetalShaderParams p = Params(nullptr, 0);
        p[17] = opacity * opacityValue;
        // Rasterize() already applied the complete device transform.  Keep this
        // quad in device space and sample it 1:1; running it through Quad() would
        // apply the rotation twice, while linear filtering would blur it again.
        Draw(DeviceQuad(x, y, w, h), texturePipeline, p, texture, nearestSampler);
    }

    id<MTLTexture> Blur(id<MTLTexture> source, float radius,
        float requestedSigma = 0.0f, uint32_t blurType = JALIUM_BACKDROP_BLUR_GAUSSIAN)
    {
        if (!source || radius <= 0.01f || !commandBuffer) return source;
        EndEncoder();
        id<MTLTexture> temp = NewTransientTexture(
            static_cast<uint32_t>(source.width), static_cast<uint32_t>(source.height));
        id<MTLTexture> output = NewTransientTexture(
            static_cast<uint32_t>(source.width), static_cast<uint32_t>(source.height));
        if (!temp || !output) return source;
        float sigma = requestedSigma > 0.0f ? requestedSigma :
            (blurType == JALIUM_BACKDROP_BLUR_BOX
                ? radius / std::sqrt(3.0f) : radius / 3.0f);
        uint32_t passes=std::clamp<uint32_t>(static_cast<uint32_t>(
            std::ceil((radius/32.0f)*(radius/32.0f))),1,16);
        float passRadius=std::min(radius/std::sqrt(static_cast<float>(passes)),32.0f);
        float params[8] = {passRadius, 1.0f,
            std::max(sigma/std::sqrt(static_cast<float>(passes)), 0.5f),
            blurType == JALIUM_BACKDROP_BLUR_FROSTED ? 1.0f : 0.0f,
            static_cast<float>(blurType),0,0,0};
        auto dispatch = [&](id<MTLTexture> input, id<MTLTexture> destination) {
            id<MTLComputeCommandEncoder> compute = [commandBuffer computeCommandEncoder];
            [compute setComputePipelineState:blurPipeline];
            [compute setTexture:input atIndex:0]; [compute setTexture:destination atIndex:1];
            [compute setBytes:params length:sizeof(params) atIndex:0];
            MTLSize threads = MTLSizeMake(8, 8, 1);
            MTLSize groups = MTLSizeMake((destination.width + 7) / 8,
                (destination.height + 7) / 8, 1);
            [compute dispatchThreadgroups:groups threadsPerThreadgroup:threads];
            [compute endEncoding];
        };
        id<MTLTexture> input=source;
        for(uint32_t pass=0;pass<passes;++pass){params[1]=1;dispatch(input,temp);
            params[1]=0;dispatch(temp,output);input=output;}
        return output;
    }

    CapturedTexture Snapshot(RectF bounds)
    {
        CapturedTexture result;
        if (!currentTarget || !commandBuffer) return result;
        FlushVello();
        EndEncoder();
        result.texture = NewTransientTexture(pixelWidth, pixelHeight);
        result.bounds = bounds;
        if (!result.texture) return {};
        id<MTLBlitCommandEncoder> blit = [commandBuffer blitCommandEncoder];
        [blit copyFromTexture:currentTarget sourceSlice:0 sourceLevel:0
            sourceOrigin:MTLOriginMake(0, 0, 0)
            sourceSize:MTLSizeMake(pixelWidth, pixelHeight, 1)
            toTexture:result.texture destinationSlice:0 destinationLevel:0
            destinationOrigin:MTLOriginMake(0, 0, 0)];
        [blit endEncoding];
        return result;
    }

    RectF EffectSource(float x,float y,float w,float h,float uvOffsetX,
        float uvOffsetY) const
    {
        RectF output=TransformBounds(DeviceTransform(),x,y,w,h);
        return {effectCapture.bounds.x+uvOffsetX*dpiX/96.0f,
            effectCapture.bounds.y+uvOffsetY*dpiY/96.0f,
            output.width,output.height};
    }
};

MetalRenderTarget::MetalRenderTarget(MetalBackend* backend, int32_t width,
    int32_t height, bool composition) : impl_(std::make_unique<Impl>())
{
    width_ = width; height_ = height;
    impl_->backend = backend; impl_->composition = composition;
    impl_->activeEngine = JALIUM_ENGINE_IMPELLER;
    activeEngine_ = pendingEngine_ = JALIUM_ENGINE_IMPELLER;
}

MetalRenderTarget::~MetalRenderTarget()
{
    if (!impl_) return;
    impl_->EndEncoder();
    if (impl_->commandBuffer) { [impl_->commandBuffer commit]; [impl_->commandBuffer waitUntilCompleted]; }
    for (uint32_t i = 0; i < kFrameCount; ++i) {
        if (impl_->inFlight)
            dispatch_semaphore_wait(impl_->inFlight, DISPATCH_TIME_FOREVER);
    }
    for(id<MTLCommandBuffer> command in impl_->retirementCommands)
        [command waitUntilCompleted];
}

bool MetalRenderTarget::Initialize(void* nativeHandle)
{
    if (!nativeHandle) return false;
    JaliumSurfaceDescriptor surface{};
#if TARGET_OS_OSX
    surface.platform = JALIUM_PLATFORM_MACOS;
#elif TARGET_OS_TV
    surface.platform = JALIUM_PLATFORM_TVOS;
#elif TARGET_OS_VISION
    surface.platform = JALIUM_PLATFORM_VISIONOS;
#else
    surface.platform = JALIUM_PLATFORM_IOS;
#endif
    surface.kind = impl_->composition ? JALIUM_SURFACE_KIND_COMPOSITION_TARGET
                                      : JALIUM_SURFACE_KIND_NATIVE_WINDOW;
    surface.handle0 = reinterpret_cast<uintptr_t>(nativeHandle);
    return Initialize(&surface);
}

bool MetalRenderTarget::Initialize(const JaliumSurfaceDescriptor* surface)
{
    if (!surface || surface->handle0 == 0 || !impl_->backend) return false;
    impl_->device = (__bridge id<MTLDevice>)impl_->backend->DeviceHandle();
    impl_->queue = (__bridge id<MTLCommandQueue>)impl_->backend->CommandQueueHandle();
    if (!impl_->device || !impl_->queue) return false;

    id object = (__bridge id)reinterpret_cast<void*>(surface->handle0);
#if TARGET_OS_OSX
    NSView* view = nil;
    if ([object isKindOfClass:[NSWindow class]]) view = [(NSWindow*)object contentView];
    else if ([object isKindOfClass:[NSView class]]) view = (NSView*)object;
    if (!view) return false;
    view.wantsLayer = YES;
    CAMetalLayer* metalLayer = [view.layer isKindOfClass:[CAMetalLayer class]]
        ? (CAMetalLayer*)view.layer : [CAMetalLayer layer];
    if (view.layer != metalLayer) view.layer = metalLayer;
    impl_->hostView = view;
#else
    UIView* view = [object isKindOfClass:[UIView class]] ? (UIView*)object : nil;
    if (!view) return false;
    CAMetalLayer* metalLayer = [view.layer isKindOfClass:[CAMetalLayer class]]
        ? (CAMetalLayer*)view.layer : [CAMetalLayer layer];
    if (metalLayer != view.layer) [view.layer addSublayer:metalLayer];
    metalLayer.frame = view.bounds;
    metalLayer.contentsScale = view.contentScaleFactor;
    impl_->hostView = view;
#endif
    impl_->layer = metalLayer;
    impl_->layer.device = impl_->device;
    impl_->layer.pixelFormat = MTLPixelFormatBGRA8Unorm;
    impl_->layer.framebufferOnly = YES;
    impl_->layer.opaque = !impl_->composition;
    impl_->layer.maximumDrawableCount = kFrameCount;
    impl_->layer.allowsNextDrawableTimeout = YES;
    impl_->layer.presentsWithTransaction = NO;
    CGColorSpaceRef srgb=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    impl_->layer.colorspace=srgb;if(srgb)CGColorSpaceRelease(srgb);
#if TARGET_OS_OSX
    impl_->layer.displaySyncEnabled = YES;
#endif
    impl_->inFlight = dispatch_semaphore_create(kFrameCount);
    impl_->completionEvent = [impl_->device newSharedEvent];
    impl_->completionEvent.label = @"Jalium frame retirement";
    for (auto& frame : impl_->frames) {
        frame.vertices = [impl_->device newBufferWithLength:kInitialVertexBytes
            options:MTLResourceStorageModeShared];
        frame.capacity = frame.vertices ? kInitialVertexBytes : 0;
        frame.retiredBuffers = [NSMutableArray array];
        frame.transientHeaps = [NSMutableArray array];
        frame.transientResources = [NSMutableArray array];
        frame.gpuRetainedObjects = [NSMutableArray array];
        if (!frame.vertices) return false;
    }
    while(impl_->pathMsaa>1&&![impl_->device supportsTextureSampleCount:impl_->pathMsaa])
        impl_->pathMsaa/=2;
    impl_->pendingPathMsaa=impl_->pathMsaa;
    if (!impl_->InitializePipelines()) return false;
    impl_->vello=std::make_unique<MetalVelloPipeline>(impl_->device);
    (void)impl_->vello->Initialize();
    return Resize(width_, height_) == JALIUM_OK;
}

JaliumResult MetalRenderTarget::Resize(int32_t width, int32_t height)
{
    if (width <= 0 || height <= 0) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (impl_->drawing) return JALIUM_ERROR_INVALID_STATE;
    width_ = width; height_ = height;
    impl_->pixelWidth = std::max(1u, static_cast<uint32_t>(
        std::ceil(width * impl_->dpiX / 96.0f)));
    impl_->pixelHeight = std::max(1u, static_cast<uint32_t>(
        std::ceil(height * impl_->dpiY / 96.0f)));
    impl_->layer.drawableSize = CGSizeMake(impl_->pixelWidth, impl_->pixelHeight);
#if !TARGET_OS_OSX
    impl_->layer.frame = ((UIView*)impl_->hostView).bounds;
#endif
    impl_->sceneTexture = nil;
    impl_->msaaTexture = nil;
    impl_->msaaBaselineTexture = nil;
    impl_->stencilTexture = nil;
    impl_->currentTarget = nil;
    impl_->fullInvalidation = true;
    return impl_->EnsureSceneTexture() ? JALIUM_OK : JALIUM_ERROR_RESOURCE_CREATION_FAILED;
}

JaliumResult MetalRenderTarget::BeginDraw()
{
    if (impl_->drawing) return JALIUM_ERROR_INVALID_STATE;
    if (impl_->simulatedDeviceLost || impl_->backend->CheckDeviceStatus() != JALIUM_OK)
        return JALIUM_ERROR_DEVICE_LOST;
    uint32_t supportedMsaa=impl_->pendingPathMsaa;
    while(supportedMsaa>1&&![impl_->device supportsTextureSampleCount:supportedMsaa])
        supportedMsaa/=2;
    if(impl_->pathMsaa!=supportedMsaa){impl_->pathMsaa=supportedMsaa;
        impl_->sceneTexture=nil;impl_->msaaTexture=nil;impl_->msaaBaselineTexture=nil;
        impl_->stencilTexture=nil;
        if(!impl_->InitializePipelines())return JALIUM_ERROR_RESOURCE_CREATION_FAILED;}
    if (!impl_->EnsureSceneTexture()) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    dispatch_semaphore_wait(impl_->inFlight, DISPATCH_TIME_FOREVER);
    impl_->frame = &impl_->frames[impl_->frameIndex++ % kFrameCount];
    NSIndexSet* completedRetirements=[impl_->retirementCommands indexesOfObjectsPassingTest:
        ^BOOL(id<MTLCommandBuffer> command,NSUInteger,BOOL*){
            return command.status==MTLCommandBufferStatusCompleted||
                command.status==MTLCommandBufferStatusError; }];
    [impl_->retirementCommands removeObjectsAtIndexes:completedRetirements];
    impl_->frame->offset = 0;
    [impl_->frame->retiredBuffers removeAllObjects];
    [impl_->frame->transientResources removeAllObjects];
    [impl_->frame->gpuRetainedObjects removeAllObjects];
    impl_->commandBuffer = [impl_->queue commandBuffer];
    if (!impl_->commandBuffer) {
        dispatch_semaphore_signal(impl_->inFlight);
        return JALIUM_ERROR_DEVICE_LOST;
    }
    impl_->currentTarget = impl_->sceneTexture;
    impl_->drawing = true;
    activeEngine_=pendingEngine_;
    impl_->activeEngine=activeEngine_;
    impl_->beginCpuNs = NowNs();
    impl_->transforms.assign(1, Matrix3x2{});
    impl_->clips.clear(); impl_->opacities.clear(); impl_->opacity = 1.0f;
    impl_->velloEncoder.BeginFrame(impl_->pixelWidth,impl_->pixelHeight);
    if(impl_->vello)impl_->vello->PrepareFrame((impl_->frameIndex-1)%kFrameCount);
    return JALIUM_OK;
}

JaliumResult MetalRenderTarget::EndDraw()
{
    if (!impl_->drawing || !impl_->commandBuffer) return JALIUM_ERROR_INVALID_STATE;
    impl_->FlushVello();
    impl_->EndEncoder();

    {
        std::scoped_lock lock(impl_->readbackMutex);
        if (impl_->readbackRequested) {
            impl_->readbackRowBytes = (impl_->pixelWidth * 4u + 255u) & ~255u;
            impl_->readbackBuffer = [impl_->device newBufferWithLength:
                static_cast<NSUInteger>(impl_->readbackRowBytes) * impl_->pixelHeight
                options:MTLResourceStorageModeShared];
            if (impl_->readbackBuffer) {
                id<MTLBlitCommandEncoder> blit = [impl_->commandBuffer blitCommandEncoder];
                [blit copyFromTexture:impl_->sceneTexture sourceSlice:0 sourceLevel:0
                    sourceOrigin:MTLOriginMake(0, 0, 0)
                    sourceSize:MTLSizeMake(impl_->pixelWidth, impl_->pixelHeight, 1)
                    toBuffer:impl_->readbackBuffer destinationOffset:0
                    destinationBytesPerRow:impl_->readbackRowBytes
                    destinationBytesPerImage:impl_->readbackRowBytes * impl_->pixelHeight];
                [blit endEncoding];
                impl_->readbackWidth = impl_->pixelWidth;
                impl_->readbackHeight = impl_->pixelHeight;
                impl_->readbackCommand = impl_->commandBuffer;
            }
            impl_->readbackRequested = false;
        }
    }

    id<CAMetalDrawable> drawable = [impl_->layer nextDrawable];
    if (drawable) {
        MTLRenderPassDescriptor* pass = [MTLRenderPassDescriptor renderPassDescriptor];
        pass.colorAttachments[0].texture = drawable.texture;
        pass.colorAttachments[0].loadAction = MTLLoadActionDontCare;
        pass.colorAttachments[0].storeAction = MTLStoreActionStore;
        id<MTLRenderCommandEncoder> present =
            [impl_->commandBuffer renderCommandEncoderWithDescriptor:pass];
        MetalShaderParams p{};
        p[0] = static_cast<float>(impl_->pixelWidth);
        p[1] = static_cast<float>(impl_->pixelHeight);
        p[17] = 1.0f;
        std::vector<MetalVertex> quad = {{0,0,0,0},
            {static_cast<float>(impl_->pixelWidth),0,1,0},
            {static_cast<float>(impl_->pixelWidth),static_cast<float>(impl_->pixelHeight),1,1},
            {0,0,0,0},
            {static_cast<float>(impl_->pixelWidth),static_cast<float>(impl_->pixelHeight),1,1},
            {0,static_cast<float>(impl_->pixelHeight),0,1}};
        NSUInteger offset = 0;
        if (impl_->UploadVertices(quad.data(), quad.size(), offset)) {
            [present setRenderPipelineState:impl_->presentPipeline];
            [present setVertexBuffer:impl_->frame->vertices offset:offset atIndex:0];
            [present setVertexBytes:p.data() length:p.size() * sizeof(float) atIndex:1];
            [present setFragmentBytes:p.data() length:p.size() * sizeof(float) atIndex:0];
            [present setFragmentTexture:impl_->sceneTexture atIndex:0];
            [present setFragmentSamplerState:impl_->linearSampler atIndex:0];
            [present drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0 vertexCount:6];
        }
        [present endEncoding];
        [impl_->commandBuffer presentDrawable:drawable];
    }

    const uint64_t eventValue = impl_->submittedEventValue.fetch_add(1,
        std::memory_order_acq_rel) + 1;
    if (impl_->completionEvent)
        [impl_->commandBuffer encodeSignalEvent:impl_->completionEvent value:eventValue];
    MetalBackend* backend = impl_->backend;
    dispatch_semaphore_t semaphore = impl_->inFlight;
    std::atomic<int64_t>* gpuNs = &impl_->lastGpuNs;
    std::atomic<uint64_t>* completedValue = &impl_->completedEventValue;
    [impl_->commandBuffer addCompletedHandler:^(id<MTLCommandBuffer> completed) {
        if (completed.status == MTLCommandBufferStatusError)
            backend->NoteDeviceError(static_cast<int64_t>(completed.error.code));
        if (completed.GPUEndTime >= completed.GPUStartTime)
            gpuNs->store(static_cast<int64_t>((completed.GPUEndTime - completed.GPUStartTime) * 1e9),
                std::memory_order_release);
        completedValue->store(eventValue, std::memory_order_release);
        dispatch_semaphore_signal(semaphore);
    }];
    [impl_->commandBuffer commit];
    impl_->lastCpuNs.store(static_cast<int64_t>(NowNs() - impl_->beginCpuNs),
        std::memory_order_release);
    impl_->framesSubmitted.fetch_add(1, std::memory_order_relaxed);
    impl_->commandBuffer = nil; impl_->encoder = nil; impl_->drawing = false;
    impl_->currentTarget = nil; impl_->fullInvalidation = false;
    impl_->dirtyRects.clear();
    return impl_->simulatedDeviceLost ? JALIUM_ERROR_DEVICE_LOST : JALIUM_OK;
}

void MetalRenderTarget::Clear(float r, float g, float b, float a)
{
    if (!impl_->drawing) return;
    if (impl_->fullInvalidation || impl_->currentTarget != impl_->sceneTexture ||
        impl_->dirtyRects.empty()) {
        impl_->EnsureEncoder(true, MTLClearColorMake(r * a, g * a, b * a, a));
        return;
    }

    // A loadAction clear is attachment-wide and ignores the damage scissor.
    // On a partial frame it would erase the persistent scene outside the dirty
    // region. Clear damage with a replace-blended GPU quad instead.
    MetalSolidBrush clearBrush(r, g, b, a);
    MetalShaderParams p = impl_->Params(&clearBrush, 0);
    for (const RectF& dirty : impl_->dirtyRects) {
        RectF clipped{
            std::max(dirty.x, 0.0f), std::max(dirty.y, 0.0f),
            std::min(dirty.x + dirty.width, static_cast<float>(impl_->pixelWidth)) -
                std::max(dirty.x, 0.0f),
            std::min(dirty.y + dirty.height, static_cast<float>(impl_->pixelHeight)) -
                std::max(dirty.y, 0.0f)};
        if (clipped.width <= 0 || clipped.height <= 0) continue;
        impl_->Draw(Impl::DeviceQuad(clipped.x, clipped.y, clipped.width,
            clipped.height), impl_->shapeReplacePipeline, p);
    }
}

void MetalRenderTarget::FillRectangle(float x, float y, float w, float h,
    Brush* brush)
{
    const float radii[4] = {0,0,0,0};
    uint32_t primitive = impl_->shapeType == 1 ? 3u : 1u;
    impl_->DrawShape(x, y, w, h, radii, 0, brush, primitive);
}

void MetalRenderTarget::DrawRectangle(float x, float y, float w, float h,
    Brush* brush, float strokeWidth)
{
    const float radii[4] = {0,0,0,0};
    uint32_t primitive = impl_->shapeType == 1 ? 3u : 1u;
    impl_->DrawShape(x, y, w, h, radii, strokeWidth, brush, primitive);
}

void MetalRenderTarget::FillRoundedRectangle(float x, float y, float w,
    float h, float rx, float ry, Brush* brush)
{
    float radius = std::max(0.0f, std::min({rx, ry, w * 0.5f, h * 0.5f}));
    const float radii[4] = {radius,radius,radius,radius};
    impl_->DrawShape(x, y, w, h, radii, 0, brush,
        impl_->shapeType == 1 ? 3u : 1u);
}

void MetalRenderTarget::DrawRoundedRectangle(float x, float y, float w,
    float h, float rx, float ry, Brush* brush, float strokeWidth)
{
    float radius = std::max(0.0f, std::min({rx, ry, w * 0.5f, h * 0.5f}));
    const float radii[4] = {radius,radius,radius,radius};
    impl_->DrawShape(x, y, w, h, radii, strokeWidth, brush,
        impl_->shapeType == 1 ? 3u : 1u);
}

void MetalRenderTarget::FillPerCornerRoundedRectangle(float x, float y,
    float w, float h, float tl, float tr, float br, float bl, Brush* brush)
{
    const float limit = std::min(w, h) * 0.5f;
    float radii[4] = {std::clamp(tl,0.0f,limit),std::clamp(tr,0.0f,limit),
        std::clamp(br,0.0f,limit),std::clamp(bl,0.0f,limit)};
    impl_->DrawShape(x, y, w, h, radii, 0, brush,
        impl_->shapeType == 1 ? 3u : 1u);
}

void MetalRenderTarget::DrawPerCornerRoundedRectangle(float x, float y,
    float w, float h, float tl, float tr, float br, float bl, Brush* brush,
    float strokeWidth)
{
    const float limit = std::min(w, h) * 0.5f;
    float radii[4] = {std::clamp(tl,0.0f,limit),std::clamp(tr,0.0f,limit),
        std::clamp(br,0.0f,limit),std::clamp(bl,0.0f,limit)};
    impl_->DrawShape(x, y, w, h, radii, strokeWidth, brush,
        impl_->shapeType == 1 ? 3u : 1u);
}

void MetalRenderTarget::FillEllipse(float cx, float cy, float rx, float ry,
    Brush* brush)
{
    if(impl_->activeEngine==JALIUM_ENGINE_VELLO&&impl_->vello&&impl_->vello->IsReady()){
        impl_->velloEncoder.EncodeFillEllipse(cx,cy,rx,ry,impl_->EngineBrush(brush),
            impl_->VelloTransform(),impl_->opacity);return;
    }
    const float radii[4] = {};
    impl_->DrawShape(cx-rx, cy-ry, rx*2, ry*2, radii, 0, brush, 2);
}

void MetalRenderTarget::DrawEllipse(float cx, float cy, float rx, float ry,
    Brush* brush, float strokeWidth)
{
    const float radii[4] = {};
    impl_->DrawShape(cx-rx, cy-ry, rx*2, ry*2, radii, strokeWidth, brush, 2);
}

void MetalRenderTarget::FillEllipseBatch(const float* data, uint32_t count)
{
    if (!data) return;
    for (uint32_t i = 0; i < count; ++i) {
        const float* item = data + static_cast<size_t>(i) * 5;
        uint32_t packed = 0; std::memcpy(&packed, item + 4, sizeof(packed));
        MetalSolidBrush brush(((packed >> 0) & 0xff) / 255.0f,
            ((packed >> 8) & 0xff) / 255.0f,
            ((packed >> 16) & 0xff) / 255.0f,
            ((packed >> 24) & 0xff) / 255.0f);
        FillEllipse(item[0], item[1], item[2], item[3], &brush);
    }
}

void MetalRenderTarget::DrawLine(float x1, float y1, float x2, float y2,
    Brush* brush, float strokeWidth)
{
    if (!impl_->drawing || !brush || !(strokeWidth > 0)) return;
    float dx = x2 - x1, dy = y2 - y1;
    float length = std::sqrt(dx * dx + dy * dy);
    if (length < 1e-6f) { FillEllipse(x1, y1, strokeWidth * 0.5f,
        strokeWidth * 0.5f, brush); return; }
    float nx = -dy / length * strokeWidth * 0.5f;
    float ny = dx / length * strokeWidth * 0.5f;
    float local[] = {x1+nx,y1+ny, x2+nx,y2+ny, x2-nx,y2-ny,
                     x1+nx,y1+ny, x2-nx,y2-ny, x1-nx,y1-ny};
    Matrix3x2 transform = impl_->DeviceTransform();
    std::vector<MetalVertex> vertices(6);
    for (size_t i = 0; i < 6; ++i)
        Transform(transform, local[i*2], local[i*2+1], vertices[i].x, vertices[i].y);
    MetalShaderParams params = impl_->Params(brush, 0);
    impl_->Draw(vertices, impl_->shapePipeline, params);
}

void MetalRenderTarget::FillPolygon(const float* points, uint32_t pointCount,
    Brush* brush, int32_t fillRule)
{
    if (!impl_->drawing || !points || pointCount < 3 || !brush) return;
    if(impl_->activeEngine==JALIUM_ENGINE_VELLO&&impl_->vello&&impl_->vello->IsReady()){
        impl_->velloEncoder.EncodeFillPolygon(points,pointCount,impl_->EngineBrush(brush),
            fillRule==0?FillRule::EvenOdd:FillRule::NonZero,impl_->VelloTransform(),impl_->opacity);
        return;
    }
    std::vector<uint32_t> indices;
    if (!TriangulatePolygonRobust(points, pointCount, indices)) return;
    Matrix3x2 transform = impl_->DeviceTransform();
    std::vector<MetalVertex> vertices;
    vertices.reserve(indices.size());
    for (uint32_t index : indices) {
        MetalVertex vertex{};
        Transform(transform, points[index * 2], points[index * 2 + 1],
            vertex.x, vertex.y);
        vertices.push_back(vertex);
    }
    MetalShaderParams params = impl_->Params(brush, 0);
    impl_->Draw(vertices, impl_->shapePipeline, params);
    (void)fillRule;
}

void MetalRenderTarget::DrawPolygon(const float* points, uint32_t pointCount,
    Brush* brush, float strokeWidth, bool closed, int32_t lineJoin,
    float miterLimit)
{
    if (!points || pointCount < 2 || !brush || !(strokeWidth > 0)) return;
    std::vector<StrokeVertex> strokeVertices;
    std::vector<uint32_t> indices;
    if (!ExpandStrokePath(strokeVertices, indices, points, pointCount,
        strokeWidth, static_cast<ImpellerJoin>(std::clamp(lineJoin, 0, 2)),
        miterLimit, ImpellerCap::Butt, closed, 1, 1, 1, 1)) return;
    Matrix3x2 transform = impl_->DeviceTransform();
    std::vector<MetalVertex> vertices;
    vertices.reserve(indices.size());
    for (uint32_t index : indices) {
        if (index >= strokeVertices.size()) continue;
        MetalVertex vertex{};
        Transform(transform, strokeVertices[index].x, strokeVertices[index].y,
            vertex.x, vertex.y);
        vertices.push_back(vertex);
    }
    MetalShaderParams params = impl_->Params(brush, 0);
    impl_->Draw(vertices, impl_->shapePipeline, params);
}

void MetalRenderTarget::FillPath(float startX, float startY,
    const float* commands, uint32_t commandLength, Brush* brush,
    int32_t fillRule, int32_t edgeMode)
{
    if (!commands || commandLength == 0 || !brush) return;
    if(impl_->activeEngine==JALIUM_ENGINE_VELLO&&impl_->vello&&impl_->vello->IsReady()){
        impl_->velloEncoder.EncodeFillPath(startX,startY,commands,commandLength,
            impl_->EngineBrush(brush),fillRule==0?FillRule::EvenOdd:FillRule::NonZero,
            impl_->VelloTransform(),impl_->opacity);return;
    }
    float scale = std::max(std::abs(impl_->DeviceTransform().m11),
        std::abs(impl_->DeviceTransform().m22));
    auto contours = FlattenPathToContours(startX, startY, commands,
        commandLength, std::max(0.1f, 0.35f / std::max(scale, 0.01f)));
    std::vector<float> triangles;
    if (!TriangulateCompoundPath(contours, fillRule, triangles)) return;
    Matrix3x2 transform = impl_->DeviceTransform();
    std::vector<MetalVertex> vertices(triangles.size() / 2);
    for (size_t i = 0; i < vertices.size(); ++i)
        Transform(transform, triangles[i*2], triangles[i*2+1],
            vertices[i].x, vertices[i].y);
    MetalShaderParams params = impl_->Params(brush, 0);
    impl_->Draw(vertices, impl_->shapePipeline, params);
    (void)edgeMode;
}

void MetalRenderTarget::StrokePath(float startX, float startY,
    const float* commands, uint32_t commandLength, Brush* brush,
    float strokeWidth, bool closed, int32_t lineJoin, float miterLimit,
    int32_t lineCap, const float* dashPattern, uint32_t dashCount,
    float dashOffset, int32_t edgeMode)
{
    if (!commands || commandLength == 0 || !brush || !(strokeWidth > 0)) return;
    if(impl_->activeEngine==JALIUM_ENGINE_VELLO&&impl_->vello&&impl_->vello->IsReady()){
        impl_->velloEncoder.EncodeStrokePath(startX,startY,commands,commandLength,
            impl_->EngineBrush(brush),strokeWidth,closed,lineJoin,miterLimit,lineCap,
            dashPattern,dashCount,dashOffset,impl_->VelloTransform(),impl_->opacity);return;
    }
    float scale = std::max(std::abs(impl_->DeviceTransform().m11),
        std::abs(impl_->DeviceTransform().m22));
    auto contours = FlattenPathToContours(startX, startY, commands,
        commandLength, std::max(0.1f, 0.25f / std::max(scale, 0.01f)));
    std::vector<StrokeVertex> allVertices;
    std::vector<uint32_t> allIndices;
    auto emit = [&](const float* points, uint32_t count, bool subClosed,
                    ImpellerCap cap) {
        std::vector<StrokeVertex> vertices;
        std::vector<uint32_t> indices;
        if (!ExpandStrokePath(vertices, indices, points, count, strokeWidth,
            static_cast<ImpellerJoin>(std::clamp(lineJoin, 0, 2)), miterLimit,
            cap, subClosed, 1, 1, 1, 1)) return;
        uint32_t base = static_cast<uint32_t>(allVertices.size());
        allVertices.insert(allVertices.end(), vertices.begin(), vertices.end());
        for (uint32_t index : indices) allIndices.push_back(base + index);
    };
    ImpellerCap cap = static_cast<ImpellerCap>(std::clamp(lineCap, 0, 2));
    for (const Contour& contour : contours) {
        uint32_t count = contour.VertexCount();
        if (count < 2) continue;
        if (dashPattern && dashCount > 0) {
            WalkDashPattern(contour.points.data(), count, dashPattern, dashCount,
                dashOffset, [&](const float* sub, uint32_t subCount,
                                bool, bool) {
                    emit(sub, subCount, false, cap);
                });
        } else {
            emit(contour.points.data(), count, closed, cap);
        }
    }
    Matrix3x2 transform = impl_->DeviceTransform();
    std::vector<MetalVertex> vertices;
    vertices.reserve(allIndices.size());
    for (uint32_t index : allIndices) {
        if (index >= allVertices.size()) continue;
        MetalVertex vertex{};
        Transform(transform, allVertices[index].x, allVertices[index].y,
            vertex.x, vertex.y);
        vertices.push_back(vertex);
    }
    MetalShaderParams params = impl_->Params(brush, 0);
    impl_->Draw(vertices, impl_->shapePipeline, params);
    (void)edgeMode;
}

void MetalRenderTarget::DrawContentBorder(float x, float y, float w, float h,
    float blRadius, float brRadius, Brush* fillBrush, Brush* strokeBrush,
    float strokeWidth)
{
    if (fillBrush)
        FillPerCornerRoundedRectangle(x, y, w, h, 0, 0, brRadius, blRadius,
            fillBrush);
    if (!strokeBrush || !(strokeWidth > 0)) return;
    DrawLine(x, y, x, y + h - blRadius, strokeBrush, strokeWidth);
    DrawLine(x + w, y, x + w, y + h - brRadius, strokeBrush, strokeWidth);
    std::vector<float> points = {x, y+h-blRadius, x+blRadius,y+h,
        x+w-brRadius,y+h, x+w,y+h-brRadius};
    DrawPolygon(points.data(), static_cast<uint32_t>(points.size()/2),
        strokeBrush, strokeWidth, false, 2, 10);
}

void MetalRenderTarget::RenderText(const wchar_t* text, uint32_t textLength,
    TextFormat* format, float x, float y, float w, float h, Brush* brush)
{
    auto* metalFormat = dynamic_cast<MetalTextFormat*>(format);
    if (!metalFormat || !brush || !text || textLength == 0 || w <= 0 || h <= 0)
        return;
    float r=1,g=1,b=1,a=1;
    if (auto* solid = dynamic_cast<MetalSolidBrush*>(brush)) {
        r=solid->r; g=solid->g; b=solid->b; a=solid->a;
    } else if (auto* linear = dynamic_cast<MetalLinearGradientBrush*>(brush);
               linear && !linear->stops.empty()) {
        r=linear->stops.front().r; g=linear->stops.front().g;
        b=linear->stops.front().b; a=linear->stops.front().a;
    } else if (auto* radial = dynamic_cast<MetalRadialGradientBrush*>(brush);
               radial && !radial->stops.empty()) {
        r=radial->stops.front().r; g=radial->stops.front().g;
        b=radial->stops.front().b; a=radial->stops.front().a;
    }
    std::vector<uint8_t> pixels;
    uint32_t pixelWidth=0, pixelHeight=0;
    float deviceX=0, deviceY=0;
    Matrix3x2 transform = impl_->DeviceTransform();
    float matrix[6] = {transform.m11, transform.m12, transform.m21,
        transform.m22, transform.dx, transform.dy};
    MTLScissorRect scissor = impl_->CurrentScissor();
    if (scissor.width == 0 || scissor.height == 0) return;
    uint64_t cacheKey=Fnv1a64(text,static_cast<size_t>(textLength)*sizeof(wchar_t));
    auto mix=[&](const void* value,size_t bytes){cacheKey=Fnv1a64(value,bytes,cacheKey);};
    uint64_t formatIdentity=metalFormat->CacheIdentity();mix(&formatIdentity,sizeof(formatIdentity));
    float layout[]={x,y,w,h,r,g,b,a};mix(layout,sizeof(layout));mix(matrix,sizeof(matrix));
    NSUInteger clipValues[]={scissor.x,scissor.y,scissor.width,scissor.height};
    mix(clipValues,sizeof(clipValues));
    if(auto cached=impl_->textCache.find(cacheKey);cached!=impl_->textCache.end()){
        cached->second.lastUse=++impl_->textCacheClock;
        impl_->DrawDeviceTexture(cached->second.texture,cached->second.x,cached->second.y,
            cached->second.width,cached->second.height,1.0f);return;
    }
    if (!metalFormat->Rasterize(text, textLength, x, y, w, h, matrix,
        static_cast<float>(scissor.x), static_cast<float>(scissor.y),
        static_cast<float>(scissor.width), static_cast<float>(scissor.height),
        r, g, b, a, pixels, pixelWidth, pixelHeight, deviceX, deviceY)) return;
    MTLTextureDescriptor* descriptor=[MTLTextureDescriptor
        texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
        width:pixelWidth height:pixelHeight mipmapped:NO];
    descriptor.storageMode=MTLStorageModeShared;descriptor.usage=MTLTextureUsageShaderRead;
    id<MTLTexture> texture=[impl_->device newTextureWithDescriptor:descriptor];
    if(!texture)return;[texture replaceRegion:MTLRegionMake2D(0,0,pixelWidth,pixelHeight)
        mipmapLevel:0 withBytes:pixels.data() bytesPerRow:pixelWidth*4];
    Impl::TextCacheEntry entry{texture,deviceX,deviceY,static_cast<float>(pixelWidth),
        static_cast<float>(pixelHeight),++impl_->textCacheClock,pixels.size()};
    impl_->textCacheBytes+=entry.bytes;impl_->textCache[cacheKey]=entry;
    impl_->TrimTextCache();
    impl_->DrawDeviceTexture(texture, deviceX, deviceY,
        static_cast<float>(pixelWidth), static_cast<float>(pixelHeight), 1.0f);
}

void MetalRenderTarget::PushTransform(const float* matrix)
{
    if (!matrix) return;
    Matrix3x2 value{matrix[0],matrix[1],matrix[2],matrix[3],matrix[4],matrix[5]};
    impl_->transforms.push_back(Multiply(impl_->transforms.back(), value));
}
void MetalRenderTarget::PopTransform()
{ if (impl_->transforms.size() > 1) impl_->transforms.pop_back(); }

void MetalRenderTarget::PushClip(float x, float y, float w, float h)
{
    impl_->FlushVello();
    impl_->EndEncoder();
    ClipState clip;
    Matrix3x2 transform = impl_->DeviceTransform();
    clip.localRect = {x,y,w,h};
    clip.deviceRect = TransformBounds(transform, x, y, w, h);
    if (!Invert(transform, clip.deviceToLocal)) clip.deviceToLocal = {};
    impl_->clips.push_back(clip);
    MTLScissorRect velloScissor=impl_->CurrentScissor();
    impl_->velloEncoder.SetScissor((float)velloScissor.x,(float)velloScissor.y,
        (float)(velloScissor.x+velloScissor.width),(float)(velloScissor.y+velloScissor.height));
    if (impl_->encoder) {
        MTLScissorRect scissor = impl_->CurrentScissor();
        if (scissor.width && scissor.height) [impl_->encoder setScissorRect:scissor];
    }
}
void MetalRenderTarget::PushClipAliased(float x, float y, float w, float h)
{ PushClip(x,y,w,h); impl_->clips.back().aliased = true; }
void MetalRenderTarget::PushRoundedRectClip(float x, float y, float w, float h,
    float rx, float ry)
{
    PushPerCornerRoundedRectClip(x,y,w,h,std::min(rx,ry),std::min(rx,ry),
        std::min(rx,ry),std::min(rx,ry));
}
void MetalRenderTarget::PushPerCornerRoundedRectClip(float x, float y, float w,
    float h, float tl, float tr, float br, float bl)
{
    impl_->FlushVello();
    impl_->EndEncoder();
    ClipState clip;
    Matrix3x2 transform = impl_->DeviceTransform();
    clip.localRect = {x,y,w,h};
    clip.deviceRect = TransformBounds(transform, x,y,w,h);
    if (!Invert(transform, clip.deviceToLocal)) clip.deviceToLocal = {};
    clip.radii[0]=tl; clip.radii[1]=tr;
    clip.radii[2]=br; clip.radii[3]=bl; clip.rounded=true;
    impl_->clips.push_back(clip);
    MTLScissorRect velloScissor=impl_->CurrentScissor();
    impl_->velloEncoder.SetScissor((float)velloScissor.x,(float)velloScissor.y,
        (float)(velloScissor.x+velloScissor.width),(float)(velloScissor.y+velloScissor.height));
    if (impl_->encoder) {
        MTLScissorRect scissor=impl_->CurrentScissor();
        if(scissor.width&&scissor.height)[impl_->encoder setScissorRect:scissor];
    }
}
void MetalRenderTarget::PushRoundedRectClipExclude(float x, float y, float w,
    float h, float rx, float ry)
{ PushRoundedRectClip(x,y,w,h,rx,ry); impl_->clips.back().exclude=true; }
void MetalRenderTarget::PopClip()
{
    if (impl_->clips.empty()) return;
    impl_->FlushVello();
    impl_->EndEncoder();
    impl_->clips.pop_back();
    if(impl_->clips.empty())impl_->velloEncoder.ClearScissor();
    else{MTLScissorRect velloScissor=impl_->CurrentScissor();impl_->velloEncoder.SetScissor(
        (float)velloScissor.x,(float)velloScissor.y,(float)(velloScissor.x+velloScissor.width),
        (float)(velloScissor.y+velloScissor.height));}
    if (impl_->encoder) {
        MTLScissorRect scissor=impl_->CurrentScissor();
        if(scissor.width&&scissor.height)[impl_->encoder setScissorRect:scissor];
    }
}
void MetalRenderTarget::PunchTransparentRect(float x,float y,float w,float h)
{
    MetalSolidBrush clear(0,0,0,0); const float radii[4]={};
    impl_->DrawShape(x,y,w,h,radii,0,&clear,0,true);
}
void MetalRenderTarget::PushOpacity(float value)
{ impl_->opacities.push_back(impl_->opacity); impl_->opacity*=std::clamp(value,0.0f,1.0f); }
void MetalRenderTarget::PopOpacity()
{ if(!impl_->opacities.empty()){impl_->opacity=impl_->opacities.back();impl_->opacities.pop_back();} }
void MetalRenderTarget::SetShapeType(int type,float n)
{ impl_->shapeType=type; impl_->shapeExponent=std::max(n,1.0f); }
void MetalRenderTarget::SetVSyncEnabled(bool enabled)
{
    vsyncEnabled_=enabled;
#if TARGET_OS_OSX
    impl_->layer.displaySyncEnabled=enabled;
#endif
}
void MetalRenderTarget::SetExternalPresentPacing(bool enabled)
{ impl_->externalPacing=enabled; }
void MetalRenderTarget::SetPathMsaaSampleCount(uint32_t count)
{ if(count==1||count==2||count==4||count==8)impl_->pendingPathMsaa=count; }
void MetalRenderTarget::SetDpi(float x,float y)
{
    if(!(x>0)||!(y>0))return; impl_->dpiX=x;impl_->dpiY=y;
    if(!impl_->drawing)(void)Resize(width_,height_);
}
void MetalRenderTarget::AddDirtyRect(float x,float y,float w,float h)
{
    if(w<=0||h<=0)return;
    impl_->dirtyRects.push_back(TransformBounds(impl_->DeviceTransform(),x,y,w,h));
}
void MetalRenderTarget::SetFullInvalidation(){impl_->fullInvalidation=true;}

void MetalRenderTarget::DrawBitmap(Bitmap* bitmap,float x,float y,float w,float h,
    float opacity)
{ DrawBitmap(bitmap,x,y,w,h,opacity,0); }
void MetalRenderTarget::DrawBitmap(Bitmap* bitmap,float x,float y,float w,float h,
    float opacity,int scalingMode)
{
    auto* metalBitmap=dynamic_cast<MetalBitmap*>(bitmap);if(!metalBitmap)return;
    id<MTLTexture> texture=(__bridge id<MTLTexture>)metalBitmap->EnsureTexture(
        impl_->backend->DeviceHandle());
    impl_->DrawTexture(texture,x,y,w,h,opacity,scalingMode,
        {0,0,static_cast<float>(metalBitmap->GetWidth()),
         static_cast<float>(metalBitmap->GetHeight())});
}

void MetalRenderTarget::DrawVideoSurface(VideoSurface* surface,float x,float y,
    float w,float h,float opacity,int scalingMode)
{
    auto* metal=dynamic_cast<MetalVideoSurface*>(surface);if(!metal)return;
    metal->RetainForCommandBuffer((__bridge void*)impl_->commandBuffer);
    id<MTLTexture> texture=(__bridge id<MTLTexture>)metal->TextureHandle(0);
    id<MTLTexture> chroma=(__bridge id<MTLTexture>)metal->TextureHandle(1);
    if(chroma){
        MetalShaderParams p=impl_->Params(nullptr,0);p[17]=impl_->opacity*opacity;
        p[300]=metal->IsVideoRange()?1.0f:0.0f;
        p[301]=static_cast<float>(metal->YuvMatrix());
        auto quad=impl_->Quad(x,y,w,h);id<MTLRenderCommandEncoder> encoder=impl_->EnsureEncoder();
        NSUInteger offset=0;if(!encoder||!impl_->UploadVertices(quad.data(),quad.size(),offset))return;
        [encoder setRenderPipelineState:impl_->yuvPipeline];
        [encoder setVertexBuffer:impl_->frame->vertices offset:offset atIndex:0];
        [encoder setVertexBytes:p.data() length:p.size()*sizeof(float) atIndex:1];
        [encoder setFragmentBytes:p.data() length:p.size()*sizeof(float) atIndex:0];
        [encoder setFragmentTexture:texture atIndex:0];[encoder setFragmentTexture:chroma atIndex:1];
        id<MTLSamplerState> sampler=scalingMode==3?impl_->nearestSampler:
            ((scalingMode==0||scalingMode==2)?impl_->highQualitySampler:impl_->linearSampler);
        [encoder setFragmentSamplerState:sampler atIndex:0];
        [encoder drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0 vertexCount:6];
    }else impl_->DrawTexture(texture,x,y,w,h,opacity,scalingMode,
        {0,0,static_cast<float>(metal->GetWidth()),static_cast<float>(metal->GetHeight())});
}

void MetalRenderTarget::BlitInkLayer(void* bitmap,float x,float y,float opacity)
{
    auto* layer=static_cast<MetalInkLayer*>(bitmap);if(!layer||!layer->texture)return;
    impl_->DrawTexture(layer->texture,x,y,static_cast<float>(layer->width),
        static_cast<float>(layer->height),opacity,0,
        {0,0,static_cast<float>(layer->width),static_cast<float>(layer->height)});
}

bool MetalRenderTarget::SupportsRetainedLayers() const { return true; }

void* MetalRenderTarget::RealizeLayerBegin(void* existingLayer, float x,
    float y, float w, float h)
{
    if (!impl_->drawing || w <= 0 || h <= 0) return nullptr;
    impl_->FlushVello();
    auto* layer = static_cast<MetalRetainedLayer*>(existingLayer);
    if (!layer) layer = new MetalRetainedLayer();
    if (!layer->texture || layer->width != impl_->pixelWidth ||
        layer->height != impl_->pixelHeight) {
        layer->texture = impl_->NewPersistentTexture(impl_->pixelWidth,
            impl_->pixelHeight, &layer->heap);
        layer->width = impl_->pixelWidth; layer->height = impl_->pixelHeight;
        ++layer->generation;
    }
    if (!layer->texture) { if (!existingLayer) delete layer; return nullptr; }
    impl_->EndEncoder();
    CapturedTexture capture{layer->texture,
        TransformBounds(impl_->DeviceTransform(), x, y, w, h)};
    impl_->captureStack.push_back({impl_->currentTarget, capture});
    impl_->currentTarget = layer->texture;
    layer->bounds = capture.bounds;
    impl_->EnsureEncoder(true, MTLClearColorMake(0,0,0,0));
    return layer;
}

void MetalRenderTarget::RealizeLayerEnd(void* layerHandle)
{
    if (!layerHandle || impl_->captureStack.empty()) return;
    impl_->EndEncoder();
    impl_->currentTarget = impl_->captureStack.back().first;
    impl_->captureStack.pop_back();
}

void MetalRenderTarget::CompositeLayer(void* layerHandle, float x, float y,
    float w, float h, float opacity)
{
    auto* layer = static_cast<MetalRetainedLayer*>(layerHandle);
    if (!layer || !layer->texture) return;
    if (impl_->frame && impl_->frame->gpuRetainedObjects) {
        [impl_->frame->gpuRetainedObjects addObject:layer->texture];
        if (layer->heap) [impl_->frame->gpuRetainedObjects addObject:layer->heap];
    }
    impl_->DrawTexture(layer->texture, x, y, w, h, opacity, 0, layer->bounds);
}

void MetalRenderTarget::DestroyRetainedLayer(void* layerHandle)
{
    auto* layer = static_cast<MetalRetainedLayer*>(layerHandle);
    if (!layer) return;
    id<MTLCommandBuffer> retirement=[impl_->queue commandBuffer];
    if(!retirement){impl_->retainedOrphaned.fetch_add(1);delete layer;return;}
    std::atomic<uint64_t>* orphaned=&impl_->retainedOrphaned;
    std::atomic<uint64_t>* graveyard=&impl_->retainedGraveyard;
    bool deviceLost=impl_->simulatedDeviceLost;
    [retirement addCompletedHandler:^(id<MTLCommandBuffer> command){
        if(deviceLost||command.status==MTLCommandBufferStatusError)
            orphaned->fetch_add(1,std::memory_order_relaxed);
        else graveyard->fetch_add(1,std::memory_order_relaxed);
        delete layer;
    }];
    [impl_->retirementCommands addObject:retirement];[retirement commit];
}

void MetalRenderTarget::DrawBackdropFilter(float x,float y,float w,float h,
    const char* filter,const char* material,const char* tint,float tintOpacity,
    float blurRadius,float tl,float tr,float br,float bl)
{
    DrawBackdropFilterEx(x,y,w,h,filter,material,tint,tintOpacity,blurRadius,
        0,1,1,tl,tr,br,bl);
}

void MetalRenderTarget::DrawBackdropFilterEx(float x,float y,float w,float h,
    const char*,const char*,const char* tint,float tintOpacity,float blurRadius,
    float noiseIntensity,float saturation,float luminosity,float tl,float tr,
    float br,float bl)
{
    float rr=0.5f,gg=0.5f,bb=0.5f;
    if(tint&&tint[0]=='#'){
        unsigned value=0;
        if(std::sscanf(tint+1,"%06x",&value)==1){
            rr=((value>>16)&255)/255.0f;gg=((value>>8)&255)/255.0f;bb=(value&255)/255.0f;
        }
    }
    JaliumBackdropMaterialDesc descriptor{};
    descriptor.structSize=sizeof(descriptor);
    descriptor.blurType=JALIUM_BACKDROP_BLUR_GAUSSIAN;
    descriptor.x=x;descriptor.y=y;descriptor.width=w;descriptor.height=h;
    descriptor.blurRadius=blurRadius;descriptor.blurSigma=0;
    descriptor.noiseIntensity=noiseIntensity;
    descriptor.tintR=rr;descriptor.tintG=gg;descriptor.tintB=bb;
    descriptor.tintA=tintOpacity;descriptor.saturation=saturation;
    descriptor.luminosity=luminosity;descriptor.brightness=1;
    descriptor.contrast=1;descriptor.opacity=1;
    descriptor.cornerRadiusTL=tl;descriptor.cornerRadiusTR=tr;
    descriptor.cornerRadiusBR=br;descriptor.cornerRadiusBL=bl;
    DrawBackdropMaterial(descriptor);
}

void MetalRenderTarget::DrawBackdropMaterial(const JaliumBackdropMaterialDesc& d)
{
    if (!impl_->drawing || d.width <= 0 || d.height <= 0) return;
    RectF bounds=TransformBounds(impl_->DeviceTransform(),d.x,d.y,d.width,d.height);
    CapturedTexture snapshot=impl_->Snapshot(bounds);
    float scale=std::max(impl_->dpiX,impl_->dpiY)/96.0f;
    id<MTLTexture> blurred=impl_->Blur(snapshot.texture,d.blurRadius*scale,
        d.blurSigma*scale,d.blurType);
    PushPerCornerRoundedRectClip(d.x,d.y,d.width,d.height,d.cornerRadiusTL,
        d.cornerRadiusTR,d.cornerRadiusBR,d.cornerRadiusBL);

    MetalShaderParams p=impl_->Params(nullptr,0);p[180]=3;
    p[320]=std::max(d.brightness,0.0f);
    p[321]=std::max(d.contrast,0.0f);
    p[322]=std::max(d.saturation,0.0f);
    p[323]=d.hueRotation;
    p[324]=std::clamp(d.grayscale,0.0f,1.0f);
    p[325]=std::clamp(d.sepia,0.0f,1.0f);
    p[326]=std::clamp(d.invert,0.0f,1.0f);
    p[327]=std::max(d.luminosity,0.0f);
    p[328]=d.tintR;p[329]=d.tintG;p[330]=d.tintB;
    p[331]=std::clamp(d.tintA,0.0f,1.0f);
    p[332]=std::max(d.noiseIntensity,0.0f);
    p[333]=std::clamp(d.opacity,0.0f,1.0f);
    impl_->Draw(impl_->Quad(d.x,d.y,d.width,d.height,
        bounds.x/blurred.width,bounds.y/blurred.height,
        (bounds.x+bounds.width)/blurred.width,
        (bounds.y+bounds.height)/blurred.height),impl_->effectPipeline,p,
        blurred,impl_->linearSampler);
    PopClip();
}

void MetalRenderTarget::DrawGlowingBorderHighlight(float x,float y,float w,
    float h,float phase,float r,float g,float b,float stroke,float trail,
    float dim,float screenW,float screenH)
{
    MetalSolidBrush shade(0,0,0,dim*impl_->opacity);
    if(y>0)FillRectangle(0,0,screenW,y,&shade);
    if(y+h<screenH)FillRectangle(0,y+h,screenW,screenH-y-h,&shade);
    if(x>0)FillRectangle(0,y,x,h,&shade);
    if(x+w<screenW)FillRectangle(x+w,y,screenW-x-w,h,&shade);
    float alpha=0.45f+0.55f*std::sin(phase*2*kPi);
    MetalSolidBrush glow(r,g,b,std::clamp(alpha,0.0f,1.0f));
    DrawRoundedRectangle(x,y,w,h,4,4,&glow,stroke);
    (void)trail;
}

void MetalRenderTarget::DrawGlowingBorderTransition(float fx,float fy,float fw,
    float fh,float tx,float ty,float tw,float th,float head,float tail,float phase,
    float r,float g,float b,float stroke,float trail,float dim,float screenW,
    float screenH)
{
    float t=std::clamp((head+tail)*0.5f,0.0f,1.0f);
    DrawGlowingBorderHighlight(fx+(tx-fx)*t,fy+(ty-fy)*t,fw+(tw-fw)*t,
        fh+(th-fh)*t,phase,r,g,b,stroke,trail,dim,screenW,screenH);
}

void MetalRenderTarget::DrawRippleEffect(float x,float y,float w,float h,
    float progress,float r,float g,float b,float stroke,float dim,float screenW,
    float screenH)
{
    float expand=progress*20.0f;
    MetalSolidBrush glow(r,g,b,1.0f-std::clamp(progress,0.0f,1.0f));
    DrawRoundedRectangle(x-expand,y-expand,w+expand*2,h+expand*2,4+expand,
        4+expand,&glow,std::max(0.5f,stroke*(1-progress*0.5f)));
    (void)dim;(void)screenW;(void)screenH;
}

void MetalRenderTarget::CaptureDesktopArea(int32_t screenX,int32_t screenY,
    int32_t width,int32_t height)
{
#if TARGET_OS_OSX
    if(width<=0||height<=0)return;
    __block CGImageRef image=nullptr;
    dispatch_semaphore_t finished=dispatch_semaphore_create(0);
    [SCShareableContent getShareableContentExcludingDesktopWindows:NO
        onScreenWindowsOnly:YES completionHandler:^(SCShareableContent* content,NSError* error){
        if(error||content.displays.count==0){dispatch_semaphore_signal(finished);return;}
        CGRect requested=CGRectMake(screenX,screenY,width,height);
        SCDisplay* selected=content.displays.firstObject;
        for(SCDisplay* display in content.displays){
            if(CGRectIntersectsRect(display.frame,requested)){selected=display;break;}}
        SCContentFilter* filter=[[SCContentFilter alloc]initWithDisplay:selected
            excludingWindows:@[]];
        SCStreamConfiguration* configuration=[SCStreamConfiguration new];
        configuration.width=width;configuration.height=height;
        configuration.showsCursor=NO;configuration.capturesAudio=NO;
        configuration.sourceRect=CGRectMake(screenX-selected.frame.origin.x,
            screenY-selected.frame.origin.y,width,height);
        [SCScreenshotManager captureImageWithFilter:filter configuration:configuration
            completionHandler:^(CGImageRef captured,NSError*){
                if(captured)image=CGImageRetain(captured);
                dispatch_semaphore_signal(finished);
            }];
    }];
    if(dispatch_semaphore_wait(finished,dispatch_time(DISPATCH_TIME_NOW,
        2*NSEC_PER_SEC))!=0)return;
    if(!image)return;
    std::vector<uint8_t> pixels(static_cast<size_t>(width)*height*4);
    CGColorSpaceRef cs=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGContextRef ctx=CGBitmapContextCreate(pixels.data(),width,height,8,width*4,cs,
        kCGImageAlphaPremultipliedFirst|kCGBitmapByteOrder32Little);
    if(ctx)CGContextDrawImage(ctx,CGRectMake(0,0,width,height),image);
    if(ctx)CGContextRelease(ctx);if(cs)CGColorSpaceRelease(cs);CGImageRelease(image);
    MetalBitmap bitmap(width,height,std::move(pixels));
    impl_->desktopCapture.texture=(__bridge id<MTLTexture>)bitmap.EnsureTexture(
        impl_->backend->DeviceHandle());
    impl_->desktopCapture.bounds={0,0,static_cast<float>(width),static_cast<float>(height)};
#else
    (void)screenX;(void)screenY;(void)width;(void)height;
#endif
}

void MetalRenderTarget::DrawDesktopBackdrop(float x,float y,float w,float h,
    float blur,float r,float g,float b,float tint,float noise,float saturation)
{
    CapturedTexture source=impl_->desktopCapture.texture?impl_->desktopCapture:
        impl_->Snapshot(TransformBounds(impl_->DeviceTransform(),x,y,w,h));
    id<MTLTexture> blurred=impl_->Blur(source.texture,blur);
    if(!blurred)return;
    MetalShaderParams p=impl_->Params(nullptr,0);p[180]=3;
    p[320]=1;p[321]=1;p[322]=std::max(saturation,0.0f);p[323]=0;
    p[324]=p[325]=p[326]=0;p[327]=1;
    p[328]=r;p[329]=g;p[330]=b;p[331]=std::clamp(tint,0.0f,1.0f);
    p[332]=std::max(noise,0.0f);p[333]=1;
    RectF s=source.bounds;
    impl_->Draw(impl_->Quad(x,y,w,h,s.x/blurred.width,s.y/blurred.height,
        (s.x+s.width)/blurred.width,(s.y+s.height)/blurred.height),
        impl_->effectPipeline,p,blurred,impl_->linearSampler);
}

void MetalRenderTarget::BeginTransitionCapture(int slot,float x,float y,float w,float h)
{
    if(slot<0||slot>1||!impl_->drawing)return;
    impl_->FlushVello();
    impl_->EndEncoder();
    CapturedTexture capture{impl_->NewTransientTexture(impl_->pixelWidth,impl_->pixelHeight),
        TransformBounds(impl_->DeviceTransform(),x,y,w,h)};
    if(!capture.texture)return;
    impl_->captureStack.push_back({impl_->currentTarget,capture});
    impl_->currentTarget=capture.texture;
    impl_->transitionSlots[slot]=capture;
    impl_->EnsureEncoder(true,MTLClearColorMake(0,0,0,0));
}
void MetalRenderTarget::EndTransitionCapture(int slot)
{
    if(slot<0||slot>1||impl_->captureStack.empty())return;
    impl_->EndEncoder();impl_->currentTarget=impl_->captureStack.back().first;
    impl_->captureStack.pop_back();
}
void MetalRenderTarget::DrawTransitionShader(float x,float y,float w,float h,
    float progress,int mode,float cornerRadius)
{
    progress=std::clamp(progress,0.0f,1.0f);
    auto& from=impl_->transitionSlots[0];auto& to=impl_->transitionSlots[1];
    if(!from.texture||!to.texture)return;
    PushRoundedRectClip(x,y,w,h,cornerRadius,cornerRadius);
    MetalShaderParams p=impl_->Params(nullptr,0);p[400]=progress;
    p[401]=static_cast<float>(std::clamp(mode,0,9));p[402]=1.0f;
    p[404]=from.bounds.x/from.texture.width;p[405]=from.bounds.y/from.texture.height;
    p[406]=from.bounds.width/from.texture.width;p[407]=from.bounds.height/from.texture.height;
    p[408]=to.bounds.x/to.texture.width;p[409]=to.bounds.y/to.texture.height;
    p[410]=to.bounds.width/to.texture.width;p[411]=to.bounds.height/to.texture.height;
    auto vertices=impl_->Quad(x,y,w,h);id<MTLRenderCommandEncoder> encoder=impl_->EnsureEncoder();
    NSUInteger offset=0;if(encoder&&impl_->UploadVertices(vertices.data(),vertices.size(),offset)){
        [encoder setRenderPipelineState:impl_->transitionPipeline];
        [encoder setDepthStencilState:impl_->contentStencilState];
        [encoder setStencilReferenceValue:static_cast<uint32_t>(std::min<size_t>(impl_->clips.size(),255))];
        [encoder setVertexBuffer:impl_->frame->vertices offset:offset atIndex:0];
        [encoder setVertexBytes:p.data() length:p.size()*sizeof(float) atIndex:1];
        [encoder setFragmentBytes:p.data() length:p.size()*sizeof(float) atIndex:0];
        [encoder setFragmentTexture:from.texture atIndex:0];
        [encoder setFragmentTexture:to.texture atIndex:1];
        [encoder setFragmentSamplerState:impl_->linearSampler atIndex:0];
        [impl_->frame->gpuRetainedObjects addObject:from.texture];
        [impl_->frame->gpuRetainedObjects addObject:to.texture];
        [encoder drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0 vertexCount:6];}
    PopClip();
}
void MetalRenderTarget::DrawCapturedTransition(int slot,float x,float y,float w,
    float h,float opacity)
{if(slot>=0&&slot<2&&impl_->transitionSlots[slot].texture)impl_->DrawTexture(
    impl_->transitionSlots[slot].texture,x,y,w,h,opacity,0,impl_->transitionSlots[slot].bounds);}

void MetalRenderTarget::BeginEffectCapture(float x,float y,float w,float h)
{
    if(!impl_->drawing)return;impl_->FlushVello();impl_->EndEncoder();
    CapturedTexture capture{impl_->NewTransientTexture(impl_->pixelWidth,impl_->pixelHeight),
        TransformBounds(impl_->DeviceTransform(),x,y,w,h)};
    if(!capture.texture)return;
    impl_->captureStack.push_back({impl_->currentTarget,capture});
    impl_->currentTarget=capture.texture;impl_->activeEffectCaptures.push_back(capture);
    impl_->inEffectCapture=true;impl_->EnsureEncoder(true,MTLClearColorMake(0,0,0,0));
}
void MetalRenderTarget::EndEffectCapture()
{
    if(!impl_->inEffectCapture||impl_->captureStack.empty()||
        impl_->activeEffectCaptures.empty())return;
    impl_->EndEncoder();impl_->currentTarget=impl_->captureStack.back().first;
    impl_->captureStack.pop_back();impl_->effectCapture=impl_->activeEffectCaptures.back();
    impl_->activeEffectCaptures.pop_back();
    impl_->inEffectCapture=!impl_->activeEffectCaptures.empty();
}
void MetalRenderTarget::DrawBlurEffect(float x,float y,float w,float h,
    float radius,float uvOffsetX,float uvOffsetY)
{
    id<MTLTexture> blurred=impl_->Blur(impl_->effectCapture.texture,radius);
    impl_->DrawTexture(blurred,x,y,w,h,1,0,
        impl_->EffectSource(x,y,w,h,uvOffsetX,uvOffsetY));
}

void MetalRenderTarget::DrawDropShadowEffect(float x,float y,float w,float h,
    float radius,float ox,float oy,float r,float g,float b,float a,float uvOffsetX,
    float uvOffsetY,float,float,float,float)
{
    id<MTLTexture> blurred=impl_->Blur(impl_->effectCapture.texture,radius);
    float tint[4]={r,g,b,a};
    RectF source=impl_->EffectSource(x,y,w,h,uvOffsetX,uvOffsetY);
    RectF shadowSource=source;shadowSource.x-=radius*impl_->dpiX/96.0f;
    shadowSource.y-=radius*impl_->dpiY/96.0f;
    shadowSource.width+=radius*2*impl_->dpiX/96.0f;
    shadowSource.height+=radius*2*impl_->dpiY/96.0f;
    impl_->DrawTexture(blurred,x+ox-radius,y+oy-radius,w+radius*2,h+radius*2,1,0,shadowSource,
        false,true,tint);
    impl_->DrawTexture(impl_->effectCapture.texture,x,y,w,h,1,0,source);
}
void MetalRenderTarget::DrawOuterGlowEffect(float x,float y,float w,float h,
    float size,float r,float g,float b,float a,float intensity,float uvOffsetX,
    float uvOffsetY,float,float,float,float)
{
    id<MTLTexture> blurred=impl_->Blur(impl_->effectCapture.texture,size);
    float tint[4]={r,g,b,std::clamp(a*intensity,0.0f,1.0f)};
    RectF source=impl_->EffectSource(x,y,w,h,uvOffsetX,uvOffsetY);
    RectF glowSource=source;glowSource.x-=size*impl_->dpiX/96.0f;
    glowSource.y-=size*impl_->dpiY/96.0f;
    glowSource.width+=size*2*impl_->dpiX/96.0f;
    glowSource.height+=size*2*impl_->dpiY/96.0f;
    impl_->DrawTexture(blurred,x-size,y-size,w+size*2,h+size*2,1,0,glowSource,
        false,true,tint);
    impl_->DrawTexture(impl_->effectCapture.texture,x,y,w,h,1,0,source);
}
void MetalRenderTarget::DrawInnerShadowEffect(float x,float y,float w,float h,
    float radius,float ox,float oy,float r,float g,float b,float a,float uvOffsetX,
    float uvOffsetY,
    float tl,float tr,float br,float bl)
{
    RectF source=impl_->EffectSource(x,y,w,h,uvOffsetX,uvOffsetY);
    impl_->DrawTexture(impl_->effectCapture.texture,x,y,w,h,1,0,source);
    if(a<=0.004f||w<=1||h<=1)return;
    PushPerCornerRoundedRectClip(x,y,w,h,tl,tr,br,bl);
    constexpr int layers=5;float blur=std::max(radius,1.0f);
    float strokeWidth=std::max(1.5f,blur*0.7f);
    MetalSolidBrush shadow(r,g,b,1);
    for(int i=1;i<=layers;++i){float t=static_cast<float>(i)/layers;
        float inset=blur*(t-1.0f/layers);float alpha=a*(1-t)*0.65f;
        float sw=w-inset*2,sh=h-inset*2;if(sw<=1||sh<=1)break;
        if(alpha<=0.004f)continue;shadow.a=alpha;
        DrawPerCornerRoundedRectangle(x+ox+inset,y+oy+inset,sw,sh,
            std::max(0.0f,tl-inset),std::max(0.0f,tr-inset),
            std::max(0.0f,br-inset),std::max(0.0f,bl-inset),
            &shadow,strokeWidth);
    }
    PopClip();
}

void MetalRenderTarget::DrawColorMatrixEffect(float x,float y,float w,float h,
    const float* matrix)
{
    if(!matrix||!impl_->effectCapture.texture)return;
    MetalShaderParams p=impl_->Params(nullptr,0);p[180]=1;
    for(int i=0;i<20;++i)p[181+i]=matrix[i];
    RectF s=impl_->effectCapture.bounds;
    impl_->Draw(impl_->Quad(x,y,w,h,s.x/impl_->effectCapture.texture.width,
        s.y/impl_->effectCapture.texture.height,(s.x+s.width)/impl_->effectCapture.texture.width,
        (s.y+s.height)/impl_->effectCapture.texture.height),impl_->effectPipeline,p,
        impl_->effectCapture.texture,impl_->linearSampler);
}
void MetalRenderTarget::DrawEmbossEffect(float x,float y,float w,float h,
    float amount,float lightX,float lightY,float relief)
{
    if(!impl_->effectCapture.texture)return;
    MetalShaderParams p=impl_->Params(nullptr,0);p[180]=2;
    p[181]=lightX*relief/std::max<float>(impl_->effectCapture.texture.width,1);
    p[182]=lightY*relief/std::max<float>(impl_->effectCapture.texture.height,1);
    p[183]=amount;
    RectF s=impl_->effectCapture.bounds;
    impl_->Draw(impl_->Quad(x,y,w,h,s.x/impl_->effectCapture.texture.width,
        s.y/impl_->effectCapture.texture.height,(s.x+s.width)/impl_->effectCapture.texture.width,
        (s.y+s.height)/impl_->effectCapture.texture.height),impl_->effectPipeline,p,
        impl_->effectCapture.texture,impl_->linearSampler);
}
void MetalRenderTarget::DrawShaderEffect(float x,float y,float w,float h,
    const uint8_t*,uint32_t,const float*,uint32_t)
{
    // DXBC is a Windows-specific legacy payload. Preserve captured content and
    // let the managed PixelShader raise its established invalid-shader signal.
    if(impl_->effectCapture.texture)impl_->DrawTexture(impl_->effectCapture.texture,
        x,y,w,h,1,0,impl_->effectCapture.bounds);
}
void MetalRenderTarget::DrawShaderEffectFromSource(float x,float y,float w,float h,
    const char* hlsl,const float* constants,uint32_t constantCount)
{
    if(!impl_->effectCapture.texture)return;
    id<MTLRenderPipelineState> pipeline=impl_->CustomPipeline(hlsl);
    if(!pipeline){impl_->DrawTexture(impl_->effectCapture.texture,x,y,w,h,1,0,
        impl_->effectCapture.bounds);return;}
    RectF s=impl_->effectCapture.bounds;
    auto vertices=impl_->Quad(x,y,w,h,s.x/impl_->effectCapture.texture.width,
        s.y/impl_->effectCapture.texture.height,(s.x+s.width)/impl_->effectCapture.texture.width,
        (s.y+s.height)/impl_->effectCapture.texture.height);
    id<MTLRenderCommandEncoder> encoder=impl_->EnsureEncoder();NSUInteger offset=0;
    if(!encoder||!impl_->UploadVertices(vertices.data(),vertices.size(),offset))return;
    MetalShaderParams vertexParams=impl_->Params(nullptr,0);
    uint32_t valueCount=std::max<uint32_t>(constantCount,4);
    std::vector<float> zeroConstants(valueCount,0);
    const float* values=constants?constants:zeroConstants.data();
    [encoder setRenderPipelineState:pipeline];
    [encoder setDepthStencilState:impl_->contentStencilState];
    [encoder setStencilReferenceValue:static_cast<uint32_t>(
        std::min<size_t>(impl_->clips.size(),255))];
    [encoder setVertexBuffer:impl_->frame->vertices offset:offset atIndex:0];
    [encoder setVertexBytes:vertexParams.data() length:vertexParams.size()*sizeof(float) atIndex:1];
    NSUInteger constantBytes=static_cast<NSUInteger>(valueCount)*sizeof(float);
    if(constantBytes<=4096)[encoder setFragmentBytes:values length:constantBytes atIndex:0];
    else{id<MTLBuffer> constantBuffer=[impl_->device newBufferWithBytes:values
            length:constantBytes options:MTLResourceStorageModeShared];
        if(!constantBuffer)return;[impl_->frame->gpuRetainedObjects addObject:constantBuffer];
        [encoder setFragmentBuffer:constantBuffer offset:0 atIndex:0];}
    [encoder setFragmentTexture:impl_->effectCapture.texture atIndex:0];
    [impl_->frame->gpuRetainedObjects addObject:impl_->effectCapture.texture];
    [encoder setFragmentSamplerState:impl_->linearSampler atIndex:0];
    [encoder drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0 vertexCount:6];
}
void MetalRenderTarget::DrawLiquidGlass(float x,float y,float w,float h,
    float corner,float blur,float refraction,float chromatic,float r,float g,
    float b,float tint,float lightX,float lightY,float highlight,int shape,
    float exponent,int neighbors,float fusion,const float* neighborData)
{
    if(!impl_->drawing||w<=0||h<=0)return;
    constexpr float padding=32.0f;
    RectF captureBounds=TransformBounds(impl_->DeviceTransform(),x-padding,
        y-padding,w+padding*2,h+padding*2);
    CapturedTexture snap=impl_->Snapshot(captureBounds);
    id<MTLTexture> blurred=impl_->Blur(snap.texture,blur);
    if(!blurred)return;
    MetalShaderParams p=impl_->Params(nullptr,0);p[180]=4;
    p[340]=std::max(refraction,0.0f);p[341]=std::max(chromatic,0.0f);
    p[342]=lightX;p[343]=lightY;p[344]=std::clamp(highlight,0.0f,1.0f);
    p[345]=r;p[346]=g;p[347]=b;p[348]=std::clamp(tint,0.0f,1.0f);
    p[349]=shape==1?1.0f:0.0f;p[350]=std::max(exponent,1.0f);
    p[351]=x;p[352]=y;p[353]=w;p[354]=h;p[355]=std::max(corner,0.0f);
    p[356]=static_cast<float>(blurred.width);p[357]=static_cast<float>(blurred.height);
    p[358]=impl_->dpiX/96.0f;p[359]=impl_->dpiY/96.0f;
    int count=std::clamp(neighbors,0,4);p[360]=static_cast<float>(count);
    p[361]=std::max(fusion,0.0f);
    if(neighborData)for(int i=0;i<count;++i)for(int j=0;j<5;++j)
        p[365+i*5+j]=neighborData[i*5+j];

    auto vertices=impl_->Quad(x-padding,y-padding,w+padding*2,h+padding*2);
    for(auto& vertex:vertices){vertex.u=vertex.x/std::max<float>(blurred.width,1);
        vertex.v=vertex.y/std::max<float>(blurred.height,1);}
    impl_->Draw(vertices,impl_->effectPipeline,p,blurred,impl_->linearSampler);
}

JaliumResult MetalRenderTarget::CreateWebViewVisual(void** visualOut)
{
    if(!visualOut)return JALIUM_ERROR_INVALID_ARGUMENT;*visualOut=nullptr;
    if(!impl_->hostView)return JALIUM_ERROR_NOT_SUPPORTED;
    CALayer* visual=[CALayer layer];visual.masksToBounds=YES;
    visual.anchorPoint=CGPointMake(0,0);
    [((CALayer*)[impl_->hostView layer]) addSublayer:visual];
    *visualOut=(__bridge_retained void*)visual;
    return JALIUM_OK;
}
JaliumResult MetalRenderTarget::DestroyWebViewVisual(void* visual)
{
    if(!visual)return JALIUM_ERROR_INVALID_ARGUMENT;
    CALayer* layer=(__bridge_transfer CALayer*)visual;[layer removeFromSuperlayer];
    return JALIUM_OK;
}
JaliumResult MetalRenderTarget::SetWebViewVisualPlacement(void* visual,int32_t x,
    int32_t y,int32_t width,int32_t height,int32_t offsetX,int32_t offsetY)
{
    if(!visual||width<0||height<0)return JALIUM_ERROR_INVALID_ARGUMENT;
    CALayer* layer=(__bridge CALayer*)visual;
    [CATransaction begin];[CATransaction setDisableActions:YES];
    layer.frame=CGRectMake(x,y,width,height);
    layer.sublayerTransform=CATransform3DMakeTranslation(offsetX,offsetY,0);
    [CATransaction commit];return JALIUM_OK;
}
JaliumResult MetalRenderTarget::CreateAnimProbe(int32_t x,int32_t y,int32_t width,
    int32_t height,float travel,float period,uint32_t argb,int32_t vertical,
    void** visualOut)
{
    if(!visualOut||width<=0||height<=0||period<=0)return JALIUM_ERROR_INVALID_ARGUMENT;
    *visualOut=nullptr;CALayer* layer=[CALayer layer];
    layer.frame=CGRectMake(x,y,width,height);
    CGFloat a=((argb>>24)&255)/255.0,r=((argb>>16)&255)/255.0,
        g=((argb>>8)&255)/255.0,b=(argb&255)/255.0;
    CGColorSpaceRef colorSpace=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGFloat components[4]={r,g,b,a};
    CGColorRef probeColor=CGColorCreate(colorSpace,components);
    layer.backgroundColor=probeColor;
    if(probeColor)CGColorRelease(probeColor);
    if(colorSpace)CGColorSpaceRelease(colorSpace);
    NSString* key=vertical?@"position.y":@"position.x";
    CABasicAnimation* animation=[CABasicAnimation animationWithKeyPath:key];
    animation.byValue=@(travel);animation.duration=period;
    animation.autoreverses=YES;animation.repeatCount=HUGE_VALF;
    [layer addAnimation:animation forKey:@"jalium.independent-probe"];
    [((CALayer*)[impl_->hostView layer]) addSublayer:layer];
    *visualOut=(__bridge_retained void*)layer;return JALIUM_OK;
}
JaliumResult MetalRenderTarget::DestroyAnimProbe(void* visual)
{return DestroyWebViewVisual(visual);}

JaliumResult MetalRenderTarget::QueryGpuStats(JaliumGpuStats* out) const
{
    if(!out)return JALIUM_ERROR_INVALID_ARGUMENT;*out={};
    out->glyphSlotsUsed=static_cast<int32_t>(std::min<size_t>(impl_->textCache.size(),
        static_cast<size_t>(std::numeric_limits<int32_t>::max())));
    out->glyphSlotsTotal=2048;out->glyphBytes=static_cast<int64_t>(impl_->textCacheBytes);
    int textures=impl_->sceneTexture?1:0;
    if(impl_->msaaTexture)++textures;if(impl_->msaaBaselineTexture)++textures;
    if(impl_->stencilTexture)++textures;
    int64_t heapBytes=0;
    for(const auto& frame:impl_->frames){
        textures+=static_cast<int>(frame.transientResources.count);
        for(id value in frame.transientHeaps)
            heapBytes+=static_cast<int64_t>(((id<MTLHeap>)value).currentAllocatedSize);
    }
    for(id value in impl_->persistentHeaps)
        heapBytes+=static_cast<int64_t>(((id<MTLHeap>)value).currentAllocatedSize);
    for(const auto& slot:impl_->transitionSlots)if(slot.texture)++textures;
    if(impl_->effectCapture.texture)++textures;if(impl_->desktopCapture.texture)++textures;
    out->textureCount=textures;
    const int64_t sceneBytes=impl_->sceneTexture?
        static_cast<int64_t>(impl_->pixelWidth)*impl_->pixelHeight*4:0;
    int64_t attachmentBytes=sceneBytes;
    if(impl_->msaaTexture)attachmentBytes+=sceneBytes*impl_->pathMsaa;
    if(impl_->msaaBaselineTexture)attachmentBytes+=sceneBytes;
    if(impl_->stencilTexture)attachmentBytes+=
        static_cast<int64_t>(impl_->pixelWidth)*impl_->pixelHeight*impl_->pathMsaa;
    out->textureBytes=attachmentBytes+heapBytes;
    out->swapBufferCount=kFrameCount;
    out->lastFramePresentToReadyNs=impl_->lastGpuNs.load(std::memory_order_acquire);
    out->presentBlockNs=0;out->frameGpuWaitNs=0;out->frameWaitableWaitNs=0;
    return JALIUM_OK;
}
JaliumResult MetalRenderTarget::QueryGpuTiming(JaliumGpuTimingStats* out) const
{
    if(!out)return JALIUM_ERROR_INVALID_ARGUMENT;*out={};
    out->totalGpuNs=impl_->lastGpuNs.load(std::memory_order_acquire);
    out->otherNs=out->totalGpuNs;
    out->timingValid=impl_->framesSubmitted.load(std::memory_order_acquire)>0?1:0;
    return JALIUM_OK;
}
JaliumResult MetalRenderTarget::GetPresentInfo(JaliumPresentInfo* out) const
{
    if(!out)return JALIUM_ERROR_INVALID_ARGUMENT;*out={};
    // Metal has no DXGI/Vulkan swap-effect namespace. 0 denotes the canonical
    // CAMetalLayer drawable queue and callers already branch on backend type.
    out->swapEffect=0;out->bufferCount=kFrameCount;
    out->tearingEnabled=vsyncEnabled_?0:1;out->waitableEnabled=0;
    out->maxFrameLatency=0;out->composition=impl_->composition?1:0;
    return JALIUM_OK;
}
JaliumResult MetalRenderTarget::SetRenderingEngine(JaliumRenderingEngine engine)
{
    JaliumRenderingEngine resolved=ResolveRenderingEngine(engine,JALIUM_BACKEND_METAL);
    if(resolved!=JALIUM_ENGINE_IMPELLER&&resolved!=JALIUM_ENGINE_VELLO)
        return JALIUM_ERROR_NOT_SUPPORTED;
    if(resolved==JALIUM_ENGINE_VELLO&&(!impl_->vello||!impl_->vello->IsReady()))
        return JALIUM_ERROR_NOT_SUPPORTED;
    if(impl_->drawing)impl_->FlushVello();
    pendingEngine_=resolved;
    if(!impl_->drawing){activeEngine_=resolved;impl_->activeEngine=resolved;}
    return JALIUM_OK;
}
JaliumResult MetalRenderTarget::ReclaimIdleResources()
{
    if(impl_->drawing)return JALIUM_ERROR_INVALID_STATE;
    // Acquire every present credit to establish that no command buffer still
    // references frame-local heaps, then restore the semaphore depth.
    for(uint32_t i=0;i<kFrameCount;++i)
        dispatch_semaphore_wait(impl_->inFlight,DISPATCH_TIME_FOREVER);
    for(id<MTLCommandBuffer> command in impl_->retirementCommands)
        [command waitUntilCompleted];
    [impl_->retirementCommands removeAllObjects];
    impl_->effectCapture={};impl_->desktopCapture={};
    impl_->activeEffectCaptures.clear();
    impl_->textCache.clear();impl_->textCacheBytes=0;
    impl_->transitionSlots[0]={};impl_->transitionSlots[1]={};
    if(impl_->vello)impl_->vello->ReclaimIdleResources();
    for(auto& frame:impl_->frames){
        [frame.retiredBuffers removeAllObjects];
        [frame.transientResources removeAllObjects];
        [frame.gpuRetainedObjects removeAllObjects];
        [frame.transientHeaps removeAllObjects];
    }
    NSIndexSet* emptyPersistent=[impl_->persistentHeaps indexesOfObjectsPassingTest:
        ^BOOL(id value,NSUInteger,BOOL*){return ((id<MTLHeap>)value).currentAllocatedSize==0;}];
    [impl_->persistentHeaps removeObjectsAtIndexes:emptyPersistent];
    for(uint32_t i=0;i<kFrameCount;++i)dispatch_semaphore_signal(impl_->inFlight);
    return JALIUM_OK;
}
JaliumResult MetalRenderTarget::RequestReadback()
{
    std::scoped_lock lock(impl_->readbackMutex);
    if(impl_->readbackRequested||impl_->readbackCommand)return JALIUM_ERROR_INVALID_STATE;
    impl_->readbackRequested=true;return JALIUM_OK;
}
JaliumResult MetalRenderTarget::FetchReadback(uint8_t* buffer,uint32_t stride,
    int32_t* outWidth,int32_t* outHeight)
{
    std::unique_lock lock(impl_->readbackMutex);
    if(!outWidth||!outHeight)return JALIUM_ERROR_INVALID_ARGUMENT;
    *outWidth=static_cast<int32_t>(impl_->readbackWidth);
    *outHeight=static_cast<int32_t>(impl_->readbackHeight);
    if(!impl_->readbackCommand||!impl_->readbackBuffer)return JALIUM_ERROR_INVALID_STATE;
    if(!buffer)return JALIUM_OK;
    if(stride<impl_->readbackWidth*4)return JALIUM_ERROR_INVALID_ARGUMENT;
    id<MTLCommandBuffer> command=impl_->readbackCommand;
    lock.unlock();[command waitUntilCompleted];lock.lock();
    if(command.status==MTLCommandBufferStatusError)return JALIUM_ERROR_DEVICE_LOST;
    const uint8_t* source=static_cast<const uint8_t*>(impl_->readbackBuffer.contents);
    for(uint32_t y=0;y<impl_->readbackHeight;++y)
        std::memcpy(buffer+static_cast<size_t>(y)*stride,
            source+static_cast<size_t>(y)*impl_->readbackRowBytes,
            static_cast<size_t>(impl_->readbackWidth)*4);
    impl_->readbackBuffer=nil;impl_->readbackCommand=nil;
    impl_->readbackWidth=impl_->readbackHeight=impl_->readbackRowBytes=0;
    return JALIUM_OK;
}
bool MetalRenderTarget::DebugRemoveDevice()
{
    impl_->simulatedDeviceLost=true;
    impl_->backend->NoteDeviceError(-1);return true;
}
bool MetalRenderTarget::DebugGetRetainedDestroyCounts(uint64_t* orphaned,
    uint64_t* graveyard)
{
    if(!orphaned||!graveyard)return false;
    *orphaned=impl_->retainedOrphaned.load();*graveyard=impl_->retainedGraveyard.load();
    return true;
}
uint64_t MetalRenderTarget::DebugDevicePointer()
{return reinterpret_cast<uint64_t>(impl_->backend->DeviceHandle());}
bool MetalRenderTarget::DebugInOffscreenCapture()
{return impl_->inEffectCapture||!impl_->captureStack.empty();}

} // namespace jalium
