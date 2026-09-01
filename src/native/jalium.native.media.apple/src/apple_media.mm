#include "jalium_media.h"
#include "jalium_media_internal.h"
#include "jalium_video_surface.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

#import <TargetConditionals.h>
#import <Foundation/Foundation.h>
#import <CoreGraphics/CoreGraphics.h>
#import <ImageIO/ImageIO.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>

struct jalium_video_decoder {
    AVURLAsset* asset = nil;
    AVAssetTrack* track = nil;
    AVAssetReader* reader = nil;
    AVAssetReaderTrackOutput* output = nil;
    CMSampleBufferRef sample = nullptr;
    CVPixelBufferRef pixelBuffer = nullptr;
    jalium_pixel_format_t requestedFormat = JALIUM_PF_BGRA8;
    jalium_video_info_t info{};
    std::vector<uint8_t> cpuFrame;
};

@class JaliumCameraDelegate;
struct jalium_camera_source {
    AVCaptureSession* session = nil;
    AVCaptureVideoDataOutput* output = nil;
    JaliumCameraDelegate* delegate = nil;
    dispatch_queue_t queue = nullptr;
    dispatch_semaphore_t ready = nullptr;
    std::mutex mutex;
    CMSampleBufferRef latest = nullptr;
    std::vector<uint8_t> pixels;
    jalium_pixel_format_t format = JALIUM_PF_BGRA8;
};
struct jalium_microphone_source {};
struct jalium_subtitle_decoder {};

namespace {

std::mutex g_initMutex;
int g_initCount = 0;

jalium_media_status_t ValidateDimensions(size_t width, size_t height)
{
    if (width == 0 || height == 0 || width > 32768 || height > 32768)
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    if (width > SIZE_MAX / 4 || height > SIZE_MAX / (width * 4))
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    return JALIUM_MEDIA_OK;
}

jalium_media_status_t DecodeImage(CGImageSourceRef source, size_t index,
    jalium_pixel_format_t format, jalium_image_t* out)
{
    if (!source || !out || (format != JALIUM_PF_BGRA8 && format != JALIUM_PF_RGBA8))
        return JALIUM_MEDIA_E_INVALID_ARG;
    CGImageRef image = CGImageSourceCreateImageAtIndex(source, index, nullptr);
    if (!image) return JALIUM_MEDIA_E_DECODE_FAILED;
    size_t width = CGImageGetWidth(image), height = CGImageGetHeight(image);
    jalium_media_status_t validation = ValidateDimensions(width, height);
    if (validation != JALIUM_MEDIA_OK) { CGImageRelease(image); return validation; }
    uint32_t stride = static_cast<uint32_t>(width * 4);
    uint8_t* pixels = static_cast<uint8_t*>(
        jalium_media_aligned_alloc(static_cast<size_t>(stride) * height));
    if (!pixels) { CGImageRelease(image); return JALIUM_MEDIA_E_OUT_OF_MEMORY; }
    CGColorSpaceRef colorSpace = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGContextRef context = CGBitmapContextCreate(pixels, width, height, 8, stride,
        colorSpace, kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
    if (!context) {
        CGColorSpaceRelease(colorSpace); CGImageRelease(image);
        jalium_media_aligned_free(pixels); return JALIUM_MEDIA_E_DECODE_FAILED;
    }
    CGContextTranslateCTM(context, 0, height);
    CGContextScaleCTM(context, 1, -1);
    CGContextDrawImage(context, CGRectMake(0, 0, width, height), image);
    CGContextRelease(context); CGColorSpaceRelease(colorSpace); CGImageRelease(image);
    if (format == JALIUM_PF_RGBA8)
        jalium_media_swap_rb_inplace(pixels, static_cast<uint32_t>(width),
            static_cast<uint32_t>(height), stride);
    *out = {static_cast<uint32_t>(width), static_cast<uint32_t>(height), stride,
        format, pixels, nullptr};
    return JALIUM_MEDIA_OK;
}

CGImageSourceRef ImageSourceFromMemory(const uint8_t* data, size_t size)
{
    if (!data || size == 0 || size > JALIUM_MEDIA_MAX_ENCODED_IMAGE_BYTES) return nullptr;
    CFDataRef bytes = CFDataCreate(kCFAllocatorDefault, data, size);
    CGImageSourceRef source = bytes ? CGImageSourceCreateWithData(bytes, nullptr) : nullptr;
    if (bytes) CFRelease(bytes);
    return source;
}

jalium_video_codec_t CodecFromTrack(AVAssetTrack* track)
{
    for (id description in track.formatDescriptions) {
        CMFormatDescriptionRef format = (__bridge CMFormatDescriptionRef)description;
        FourCharCode codec = CMFormatDescriptionGetMediaSubType(format);
        if (codec == kCMVideoCodecType_H264) return JALIUM_CODEC_H264;
        if (codec == kCMVideoCodecType_HEVC || codec == kCMVideoCodecType_HEVCWithAlpha)
            return JALIUM_CODEC_HEVC;
        if (codec == 'vp09') return JALIUM_CODEC_VP9;
        if (codec == 'av01') return JALIUM_CODEC_AV1;
    }
    return JALIUM_CODEC_NONE;
}

bool ConfigureReader(jalium_video_decoder* decoder, CMTime start)
{
    if (!decoder || !decoder->asset || !decoder->track) return false;
    [decoder->reader cancelReading];
    NSError* error = nil;
    decoder->reader = [[AVAssetReader alloc] initWithAsset:decoder->asset error:&error];
    if (!decoder->reader) return false;
    NSDictionary* settings = @{
        (NSString*)kCVPixelBufferPixelFormatTypeKey: @(kCVPixelFormatType_32BGRA),
        (NSString*)kCVPixelBufferMetalCompatibilityKey: @YES,
        (NSString*)kCVPixelBufferIOSurfacePropertiesKey: @{}
    };
    decoder->output = [[AVAssetReaderTrackOutput alloc] initWithTrack:decoder->track
        outputSettings:settings];
    decoder->output.alwaysCopiesSampleData = NO;
    if (![decoder->reader canAddOutput:decoder->output]) return false;
    [decoder->reader addOutput:decoder->output];
    if (CMTIME_IS_VALID(start) && CMTimeCompare(start, kCMTimeZero) > 0)
        decoder->reader.timeRange = CMTimeRangeMake(start,
            CMTimeSubtract(decoder->asset.duration, start));
    return [decoder->reader startReading];
}

bool ReadNextSample(jalium_video_decoder* decoder)
{
    if (!decoder || !decoder->output) return false;
    if (decoder->sample) { CFRelease(decoder->sample); decoder->sample = nullptr; }
    decoder->pixelBuffer = nullptr;
    decoder->sample = [decoder->output copyNextSampleBuffer];
    if (!decoder->sample) return false;
    decoder->pixelBuffer = CMSampleBufferGetImageBuffer(decoder->sample);
    return decoder->pixelBuffer != nullptr;
}

void RetainPixelBuffer(void* context)
{ if (context) CVPixelBufferRetain((CVPixelBufferRef)context); }
void ReleasePixelBuffer(void* context)
{ if (context) CVPixelBufferRelease((CVPixelBufferRef)context); }

void ResetGpuDescriptor(jalium_video_decoder_gpu_descriptor_t* descriptor)
{
    std::memset(descriptor, 0, sizeof(*descriptor));
    for (auto& plane : descriptor->planes) plane.fd = -1;
    descriptor->acquire_fence_fd = -1;
}

jalium_media_status_t FillGpuDescriptor(jalium_video_decoder* decoder,
    jalium_video_decoder_gpu_descriptor_t* descriptor)
{
    if (!decoder || !decoder->pixelBuffer || !descriptor)
        return JALIUM_MEDIA_E_INVALID_ARG;
    ResetGpuDescriptor(descriptor);
    CVPixelBufferRetain(decoder->pixelBuffer);
    descriptor->kind = JALIUM_VS_KIND_CVPIXELBUFFER;
    descriptor->width = static_cast<uint32_t>(CVPixelBufferGetWidth(decoder->pixelBuffer));
    descriptor->height = static_cast<uint32_t>(CVPixelBufferGetHeight(decoder->pixelBuffer));
    descriptor->handle0 = reinterpret_cast<uint64_t>(decoder->pixelBuffer);
    descriptor->format_hint = JALIUM_VS_FORMAT_BGRA8;
    descriptor->lifetime_context = reinterpret_cast<uint64_t>(decoder->pixelBuffer);
    descriptor->lifetime_retain_callback = reinterpret_cast<uint64_t>(&RetainPixelBuffer);
    descriptor->lifetime_release_callback = reinterpret_cast<uint64_t>(&ReleasePixelBuffer);
    return JALIUM_MEDIA_OK;
}

} // namespace

#if !TARGET_OS_TV
@interface JaliumCameraDelegate : NSObject <AVCaptureVideoDataOutputSampleBufferDelegate>
@property(nonatomic, assign) jalium_camera_source* owner;
@end
@implementation JaliumCameraDelegate
- (void)captureOutput:(AVCaptureOutput*)output didOutputSampleBuffer:(CMSampleBufferRef)sample
       fromConnection:(AVCaptureConnection*)connection
{
    auto* owner=_owner;if(!owner)return;
    CFRetain(sample);
    {std::scoped_lock lock(owner->mutex);if(owner->latest)CFRelease(owner->latest);owner->latest=sample;}
    dispatch_semaphore_signal(owner->ready);
    (void)output;(void)connection;
}
@end
#endif

extern "C" {

JALIUM_MEDIA_API jalium_media_status_t jalium_media_initialize(void)
{ std::scoped_lock lock(g_initMutex); ++g_initCount; return JALIUM_MEDIA_OK; }
JALIUM_MEDIA_API void jalium_media_shutdown(void)
{ std::scoped_lock lock(g_initMutex); if (g_initCount > 0) --g_initCount; }
JALIUM_MEDIA_API uint32_t jalium_media_supported_video_codecs(void)
{ return JALIUM_CODEC_H264 | JALIUM_CODEC_HEVC | JALIUM_CODEC_VP9 | JALIUM_CODEC_AV1; }

JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_memory(
    const uint8_t* data, size_t size, jalium_pixel_format_t format,
    jalium_image_t* out)
{
    if (!out) return JALIUM_MEDIA_E_INVALID_ARG; *out = {};
    CGImageSourceRef source = ImageSourceFromMemory(data, size);
    if (!source) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    auto result = DecodeImage(source, 0, format, out); CFRelease(source); return result;
}
JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_file(
    const char* path, jalium_pixel_format_t format, jalium_image_t* out)
{
    if (!path || !out) return JALIUM_MEDIA_E_INVALID_ARG;
    NSData* data = [NSData dataWithContentsOfFile:[NSString stringWithUTF8String:path]];
    return data ? jalium_image_decode_memory((const uint8_t*)data.bytes, data.length,
        format, out) : JALIUM_MEDIA_E_IO;
}
JALIUM_MEDIA_API jalium_media_status_t jalium_image_read_dimensions(
    const uint8_t* data,size_t size,uint32_t* width,uint32_t* height)
{
    if(!width||!height)return JALIUM_MEDIA_E_INVALID_ARG;
    CGImageSourceRef source=ImageSourceFromMemory(data,size);if(!source)return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    CFDictionaryRef props=CGImageSourceCopyPropertiesAtIndex(source,0,nullptr);
    CFNumberRef w=props?(CFNumberRef)CFDictionaryGetValue(props,kCGImagePropertyPixelWidth):nullptr;
    CFNumberRef h=props?(CFNumberRef)CFDictionaryGetValue(props,kCGImagePropertyPixelHeight):nullptr;
    int wi=0,hi=0;if(w)CFNumberGetValue(w,kCFNumberIntType,&wi);if(h)CFNumberGetValue(h,kCFNumberIntType,&hi);
    if(props)CFRelease(props);CFRelease(source);if(wi<=0||hi<=0)return JALIUM_MEDIA_E_DECODE_FAILED;
    *width=wi;*height=hi;return JALIUM_MEDIA_OK;
}
JALIUM_MEDIA_API jalium_media_status_t jalium_image_read_frame_count(
    const uint8_t* data,size_t size,uint32_t* count)
{if(!count)return JALIUM_MEDIA_E_INVALID_ARG;CGImageSourceRef s=ImageSourceFromMemory(data,size);if(!s)return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;*count=(uint32_t)CGImageSourceGetCount(s);CFRelease(s);return *count?JALIUM_MEDIA_OK:JALIUM_MEDIA_E_DECODE_FAILED;}
JALIUM_MEDIA_API jalium_media_status_t jalium_image_decode_frame(
    const uint8_t* data,size_t size,uint32_t index,jalium_pixel_format_t format,
    jalium_image_t* out,uint32_t* delay)
{if(delay)*delay=0;CGImageSourceRef s=ImageSourceFromMemory(data,size);if(!s)return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;if(index>=CGImageSourceGetCount(s)){CFRelease(s);return JALIUM_MEDIA_E_INVALID_ARG;}auto r=DecodeImage(s,index,format,out);CFRelease(s);return r;}
JALIUM_MEDIA_API void jalium_image_free(jalium_image_t* image)
{if(!image)return;jalium_media_aligned_free(image->pixels);*image={};}

JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_open_file(
    const char* path,jalium_pixel_format_t format,jalium_video_decoder_t** out)
{
    if(!path||!out)return JALIUM_MEDIA_E_INVALID_ARG;*out=nullptr;
    auto decoder=std::make_unique<jalium_video_decoder>();decoder->requestedFormat=format;
    NSURL* url=[NSURL fileURLWithPath:[NSString stringWithUTF8String:path]];
    decoder->asset=[AVURLAsset URLAssetWithURL:url options:nil];
    decoder->track=[[decoder->asset tracksWithMediaType:AVMediaTypeVideo] firstObject];
    if(!decoder->track)return JALIUM_MEDIA_E_UNSUPPORTED_CODEC;
    CGSize size=CGSizeApplyAffineTransform(decoder->track.naturalSize,decoder->track.preferredTransform);
    decoder->info.width=(uint32_t)std::abs(size.width);decoder->info.height=(uint32_t)std::abs(size.height);
    decoder->info.duration_seconds=CMTimeGetSeconds(decoder->asset.duration);
    decoder->info.frame_rate=decoder->track.nominalFrameRate;
    decoder->info.frame_count=decoder->info.frame_rate>0&&decoder->info.duration_seconds>0?
        (uint64_t)llround(decoder->info.frame_rate*decoder->info.duration_seconds):0;
    decoder->info.active_codec=CodecFromTrack(decoder->track);
    if(!ConfigureReader(decoder.get(),kCMTimeZero))return JALIUM_MEDIA_E_DECODE_FAILED;
    *out=decoder.release();return JALIUM_MEDIA_OK;
}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_get_info(
    jalium_video_decoder_t* d,jalium_video_info_t* out){if(!d||!out)return JALIUM_MEDIA_E_INVALID_ARG;*out=d->info;return JALIUM_MEDIA_OK;}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_read_frame(
    jalium_video_decoder_t* d,jalium_video_frame_t* out)
{
    if(!d||!out)return JALIUM_MEDIA_E_INVALID_ARG;if(!ReadNextSample(d))return JALIUM_MEDIA_E_END_OF_STREAM;
    CVPixelBufferLockBaseAddress(d->pixelBuffer,kCVPixelBufferLock_ReadOnly);
    size_t width=CVPixelBufferGetWidth(d->pixelBuffer),height=CVPixelBufferGetHeight(d->pixelBuffer);
    size_t srcStride=CVPixelBufferGetBytesPerRow(d->pixelBuffer),dstStride=width*4;
    d->cpuFrame.resize(dstStride*height);const uint8_t* src=(const uint8_t*)CVPixelBufferGetBaseAddress(d->pixelBuffer);
    for(size_t y=0;y<height;++y)memcpy(d->cpuFrame.data()+y*dstStride,src+y*srcStride,dstStride);
    CVPixelBufferUnlockBaseAddress(d->pixelBuffer,kCVPixelBufferLock_ReadOnly);
    if(d->requestedFormat==JALIUM_PF_RGBA8)jalium_media_swap_rb_inplace(d->cpuFrame.data(),width,height,dstStride);
    out->width=width;out->height=height;out->stride_bytes=dstStride;out->format=d->requestedFormat;
    out->pixels=d->cpuFrame.data();out->pts_microseconds=(int64_t)llround(CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(d->sample))*1e6);out->is_keyframe=1;
    return JALIUM_MEDIA_OK;
}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_seek_microseconds(
    jalium_video_decoder_t* d,int64_t us){if(!d||us<0)return JALIUM_MEDIA_E_INVALID_ARG;return ConfigureReader(d,CMTimeMake(us,1000000))?JALIUM_MEDIA_OK:JALIUM_MEDIA_E_DECODE_FAILED;}
JALIUM_MEDIA_API void jalium_video_decoder_close(jalium_video_decoder_t* d){if(!d)return;[d->reader cancelReading];if(d->sample)CFRelease(d->sample);delete d;}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_acquire_gpu_surface_descriptor(
    jalium_video_decoder_t* d,jalium_video_decoder_gpu_descriptor_t* out){return FillGpuDescriptor(d,out);}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_read_gpu_frame_descriptor(
    jalium_video_decoder_t* d,jalium_video_decoder_gpu_descriptor_t* out,int64_t* pts,int32_t* key){if(!d||!out||!pts||!key)return JALIUM_MEDIA_E_INVALID_ARG;if(!ReadNextSample(d))return JALIUM_MEDIA_E_END_OF_STREAM;*pts=(int64_t)llround(CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(d->sample))*1e6);*key=1;return FillGpuDescriptor(d,out);}
JALIUM_MEDIA_API void jalium_video_decoder_release_gpu_surface_descriptor(jalium_video_decoder_gpu_descriptor_t* d){if(!d)return;if(d->lifetime_context)ReleasePixelBuffer((void*)d->lifetime_context);ResetGpuDescriptor(d);}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_disable_gpu_output(jalium_video_decoder_t* d){return d?JALIUM_MEDIA_OK:JALIUM_MEDIA_E_INVALID_ARG;}

JALIUM_MEDIA_API jalium_media_status_t jalium_camera_enumerate(jalium_camera_device_t** devices,uint32_t* count){if(!devices||!count)return JALIUM_MEDIA_E_INVALID_ARG;*devices=nullptr;*count=0;
#if TARGET_OS_TV
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#else
    AVCaptureDeviceDiscoverySession* session=[AVCaptureDeviceDiscoverySession discoverySessionWithDeviceTypes:@[AVCaptureDeviceTypeBuiltInWideAngleCamera,AVCaptureDeviceTypeExternal] mediaType:AVMediaTypeVideo position:AVCaptureDevicePositionUnspecified];NSArray<AVCaptureDevice*>* list=session.devices;if(!list.count)return JALIUM_MEDIA_OK;auto* result=(jalium_camera_device_t*)calloc(list.count,sizeof(jalium_camera_device_t));if(!result)return JALIUM_MEDIA_E_OUT_OF_MEMORY;for(NSUInteger i=0;i<list.count;++i){AVCaptureDevice* d=list[i];result[i].id=strdup(d.uniqueID.UTF8String?:"");result[i].friendly_name=strdup(d.localizedName.UTF8String?:"");result[i].facing=d.position==AVCaptureDevicePositionFront?JALIUM_CAMERA_FACING_FRONT:d.position==AVCaptureDevicePositionBack?JALIUM_CAMERA_FACING_BACK:JALIUM_CAMERA_FACING_EXTERNAL;CMVideoDimensions dims=CMVideoFormatDescriptionGetDimensions(d.activeFormat.formatDescription);auto* format=(jalium_camera_format_t*)calloc(1,sizeof(jalium_camera_format_t));if(format){format->width=dims.width;format->height=dims.height;format->fps=d.activeVideoMaxFrameDuration.value?d.activeVideoMaxFrameDuration.timescale/(double)d.activeVideoMaxFrameDuration.value:30;result[i].formats=format;result[i].format_count=1;}}*devices=result;*count=(uint32_t)list.count;return JALIUM_MEDIA_OK;
#endif
}
JALIUM_MEDIA_API void jalium_camera_devices_free(jalium_camera_device_t* d,uint32_t count){if(!d)return;for(uint32_t i=0;i<count;++i){free((void*)d[i].id);free((void*)d[i].friendly_name);free((void*)d[i].formats);}free(d);}
JALIUM_MEDIA_API jalium_media_status_t jalium_camera_open(const char* deviceId,uint32_t width,uint32_t height,double fps,jalium_pixel_format_t format,jalium_camera_source_t** out){if(!deviceId||!out)return JALIUM_MEDIA_E_INVALID_ARG;*out=nullptr;
#if TARGET_OS_TV
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#else
    AVAuthorizationStatus authorization=[AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo];if(authorization==AVAuthorizationStatusDenied||authorization==AVAuthorizationStatusRestricted)return JALIUM_MEDIA_E_PERMISSION_DENIED;
    AVCaptureDevice* device=[AVCaptureDevice deviceWithUniqueID:[NSString stringWithUTF8String:deviceId]];if(!device)return JALIUM_MEDIA_E_NO_DEVICE;NSError* error=nil;AVCaptureDeviceInput* input=[AVCaptureDeviceInput deviceInputWithDevice:device error:&error];if(!input)return JALIUM_MEDIA_E_PLATFORM;
    auto source=std::make_unique<jalium_camera_source>();source->format=format;source->session=[AVCaptureSession new];source->output=[AVCaptureVideoDataOutput new];source->output.videoSettings=@{(NSString*)kCVPixelBufferPixelFormatTypeKey:@(kCVPixelFormatType_32BGRA),(NSString*)kCVPixelBufferMetalCompatibilityKey:@YES};source->output.alwaysDiscardsLateVideoFrames=YES;if(![source->session canAddInput:input]||![source->session canAddOutput:source->output])return JALIUM_MEDIA_E_PLATFORM;[source->session addInput:input];[source->session addOutput:source->output];
    if(width&&height){if(width>=3840)source->session.sessionPreset=AVCaptureSessionPreset3840x2160;else if(width>=1920)source->session.sessionPreset=AVCaptureSessionPreset1920x1080;else if(width>=1280)source->session.sessionPreset=AVCaptureSessionPreset1280x720;else source->session.sessionPreset=AVCaptureSessionPreset640x480;}
    source->delegate=[JaliumCameraDelegate new];source->delegate.owner=source.get();source->queue=dispatch_queue_create("jalium.camera",DISPATCH_QUEUE_SERIAL);source->ready=dispatch_semaphore_create(0);[source->output setSampleBufferDelegate:source->delegate queue:source->queue];[source->session startRunning];*out=source.release();(void)fps;return JALIUM_MEDIA_OK;
#endif
}
JALIUM_MEDIA_API jalium_media_status_t jalium_camera_read_frame(jalium_camera_source_t* source,jalium_video_frame_t* out){if(!source||!out)return JALIUM_MEDIA_E_INVALID_ARG;
#if TARGET_OS_TV
    return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
#else
    if(dispatch_semaphore_wait(source->ready,dispatch_time(DISPATCH_TIME_NOW,NSEC_PER_SEC))!=0)return JALIUM_MEDIA_E_END_OF_STREAM;CMSampleBufferRef sample=nullptr;{std::scoped_lock lock(source->mutex);sample=source->latest;if(sample)CFRetain(sample);}if(!sample)return JALIUM_MEDIA_E_END_OF_STREAM;CVPixelBufferRef pixel=CMSampleBufferGetImageBuffer(sample);CVPixelBufferLockBaseAddress(pixel,kCVPixelBufferLock_ReadOnly);size_t width=CVPixelBufferGetWidth(pixel),height=CVPixelBufferGetHeight(pixel),srcStride=CVPixelBufferGetBytesPerRow(pixel),dstStride=width*4;source->pixels.resize(dstStride*height);const uint8_t* src=(const uint8_t*)CVPixelBufferGetBaseAddress(pixel);for(size_t y=0;y<height;++y)memcpy(source->pixels.data()+y*dstStride,src+y*srcStride,dstStride);CVPixelBufferUnlockBaseAddress(pixel,kCVPixelBufferLock_ReadOnly);if(source->format==JALIUM_PF_RGBA8)jalium_media_swap_rb_inplace(source->pixels.data(),width,height,dstStride);out->width=width;out->height=height;out->stride_bytes=dstStride;out->format=source->format;out->pixels=source->pixels.data();out->pts_microseconds=(int64_t)llround(CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(sample))*1e6);out->is_keyframe=1;CFRelease(sample);return JALIUM_MEDIA_OK;
#endif
}
JALIUM_MEDIA_API void jalium_camera_close(jalium_camera_source_t* source){if(!source)return;
#if !TARGET_OS_TV
    [source->output setSampleBufferDelegate:nil queue:nil];[source->session stopRunning];source->delegate.owner=nullptr;{std::scoped_lock lock(source->mutex);if(source->latest)CFRelease(source->latest);source->latest=nullptr;}
#endif
    delete source;}

JALIUM_MEDIA_API uint32_t jalium_linux_media_capabilities(void){return 0;}
JALIUM_MEDIA_API jalium_media_status_t jalium_media_discover_tracks(const char*,jalium_media_track_info_t** tracks,uint32_t* count){if(tracks)*tracks=nullptr;if(count)*count=0;return JALIUM_MEDIA_E_NOT_IMPLEMENTED;}
JALIUM_MEDIA_API void jalium_media_tracks_free(jalium_media_track_info_t* tracks,uint32_t){free(tracks);}
JALIUM_MEDIA_API jalium_media_status_t jalium_microphone_enumerate(jalium_microphone_device_t** d,uint32_t* c){if(d)*d=nullptr;if(c)*c=0;return JALIUM_MEDIA_E_NOT_IMPLEMENTED;}
JALIUM_MEDIA_API void jalium_microphone_devices_free(jalium_microphone_device_t* d,uint32_t){free(d);}
JALIUM_MEDIA_API jalium_media_status_t jalium_microphone_open(const char*,uint32_t,uint32_t,jalium_microphone_source_t** out){if(out)*out=nullptr;return JALIUM_MEDIA_E_NOT_IMPLEMENTED;}
JALIUM_MEDIA_API jalium_media_status_t jalium_microphone_read_frame(jalium_microphone_source_t*,jalium_audio_capture_frame_t*){return JALIUM_MEDIA_E_NOT_IMPLEMENTED;}
JALIUM_MEDIA_API void jalium_microphone_close(jalium_microphone_source_t* source){delete source;}
JALIUM_MEDIA_API jalium_media_status_t jalium_linux_audio_decoder_open_track(const char*,uint32_t,struct jalium_audio_decoder** out){if(out)*out=nullptr;return JALIUM_MEDIA_E_NOT_IMPLEMENTED;}
JALIUM_MEDIA_API jalium_media_status_t jalium_subtitle_decoder_open(const char*,uint32_t,jalium_subtitle_decoder_t** out){if(out)*out=nullptr;return JALIUM_MEDIA_E_NOT_IMPLEMENTED;}
JALIUM_MEDIA_API jalium_media_status_t jalium_subtitle_decoder_read_cue(jalium_subtitle_decoder_t*,jalium_subtitle_cue_t*){return JALIUM_MEDIA_E_NOT_IMPLEMENTED;}
JALIUM_MEDIA_API jalium_media_status_t jalium_subtitle_decoder_seek_us(jalium_subtitle_decoder_t*,int64_t){return JALIUM_MEDIA_E_NOT_IMPLEMENTED;}
JALIUM_MEDIA_API void jalium_subtitle_decoder_close(jalium_subtitle_decoder_t* d){delete d;}

} // extern "C"
