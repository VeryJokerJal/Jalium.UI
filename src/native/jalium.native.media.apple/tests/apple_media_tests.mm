#include "apple_media_availability.h"
#include "jalium_video_surface.h"
#include <chrono>
#include <cstdio>
#include <limits>
#include <thread>

// Deterministic API doubles exercise the visionOS helpers on macOS without
// enumerating hardware, requesting camera access, or using a network asset.
@interface TestAsset : NSObject
@property (nonatomic, strong) NSArray<AVAssetTrack*>* tracks;
@property (nonatomic, strong) NSError* error;
@property int64_t delay;
@property NSUInteger loads;
@property NSUInteger cancellations;
@property (nonatomic, strong) dispatch_semaphore_t completed;
@end
@implementation TestAsset
- (void)loadTracksWithMediaType:(AVMediaType)type
    completionHandler:(void (^)(NSArray<AVAssetTrack*>*, NSError*))completion
{
    ++_loads;
    void (^finish)(void) = ^{
        completion(self.tracks, self.error);
        if (self.completed) dispatch_semaphore_signal(self.completed);
    };
    if (_delay) dispatch_after(dispatch_time(DISPATCH_TIME_NOW, _delay),
        dispatch_get_global_queue(QOS_CLASS_DEFAULT, 0), finish);
    else finish();
}
- (void)cancelLoading { ++_cancellations; }
@end

@interface TestRate : NSObject
@property double minFrameRate;
@property double maxFrameRate;
@end
@implementation TestRate
@end

@interface TestFormat : NSObject
@property CMVideoFormatDescriptionRef formatDescription;
@property (nonatomic, strong) NSArray<AVFrameRateRange*>* videoSupportedFrameRateRanges;
@end
@implementation TestFormat
- (void)dealloc { if (_formatDescription) CFRelease(_formatDescription); }
@end

@interface TestDevice : NSObject
@property (nonatomic, strong) NSArray<AVCaptureDeviceFormat*>* formats;
@property (nonatomic, strong) AVCaptureDeviceFormat* activeFormat;
@property CMTime activeVideoMinFrameDuration;
@property CMTime activeVideoMaxFrameDuration;
@property BOOL canLock;
@property BOOL failSet;
@property NSUInteger locks;
@property NSUInteger unlocks;
@end
@implementation TestDevice
- (BOOL)lockForConfiguration:(NSError**)error { ++_locks; return _canLock; }
- (void)unlockForConfiguration { ++_unlocks; }
- (void)setActiveFormat:(AVCaptureDeviceFormat*)value
{
    if (_failSet) [NSException raise:NSInvalidArgumentException format:@"Changed format"];
    _activeFormat = value;
}
@end

static int checks = 0, failures = 0;
static void Check(bool passed, const char* message)
{
    ++checks;
    if (!passed) { ++failures; std::fprintf(stderr, "FAIL: %s\n", message); }
}
static dispatch_time_t Deadline(int64_t delay = 5 * NSEC_PER_SEC)
{ return dispatch_time(DISPATCH_TIME_NOW, delay); }

static void TrackLoading()
{
    using jalium::apple_media::LoadVideoTrack;
    TestAsset* asset = [TestAsset new];
    AVAssetTrack* track = nil;
    Check(LoadVideoTrack(nil, &track, Deadline()) == JALIUM_MEDIA_E_INVALID_ARG, "nil asset");
    Check(LoadVideoTrack((AVAsset*)asset, nullptr, Deadline()) == JALIUM_MEDIA_E_INVALID_ARG,
        "nil output");
    Check(asset.loads == 0, "invalid arguments never start loading");
    asset.tracks = @[];
    Check(LoadVideoTrack((AVAsset*)asset, &track, Deadline()) == JALIUM_MEDIA_E_UNSUPPORTED_CODEC
        && track == nil, "asset with no video track");
    asset.error = [NSError errorWithDomain:NSCocoaErrorDomain code:4 userInfo:nil];
    Check(LoadVideoTrack((AVAsset*)asset, &track, Deadline()) == JALIUM_MEDIA_E_IO && !track,
        "loading error");
    asset.error = nil;
    AVAssetTrack* expected = (AVAssetTrack*)[NSObject new];
    asset.tracks = @[expected, (AVAssetTrack*)[NSObject new]];
    Check(LoadVideoTrack((AVAsset*)asset, &track, Deadline()) == JALIUM_MEDIA_OK && track == expected,
        "first video track returned");
    asset.delay = 10 * NSEC_PER_MSEC;
    Check(LoadVideoTrack((AVAsset*)asset, &track, Deadline()) == JALIUM_MEDIA_OK && track == expected,
        "asynchronous completion on main caller");
    asset.delay = 100 * NSEC_PER_MSEC;
    asset.completed = dispatch_semaphore_create(0);
    track = expected;
    Check(LoadVideoTrack((AVAsset*)asset, &track, Deadline(NSEC_PER_MSEC)) == JALIUM_MEDIA_E_PLATFORM
        && !track && asset.cancellations == 1, "timeout clears output and cancels asset");
    Check(dispatch_semaphore_wait(asset.completed, Deadline()) == 0,
        "late completion remains safe after timeout");
    asset.delay = 0;
    Check(LoadVideoTrack((AVAsset*)asset, &track, Deadline()) == JALIUM_MEDIA_OK,
        "helper remains reusable after cancellation");
}

static AVCaptureDeviceFormat* Format(int width, int height, double low, double high)
{
    TestFormat* format = [TestFormat new];
    CMVideoFormatDescriptionRef description = nullptr;
    OSStatus status = CMVideoFormatDescriptionCreate(kCFAllocatorDefault, kCVPixelFormatType_32BGRA,
        width, height, nullptr, &description);
    Check(status == noErr && description, "create deterministic device format");
    format.formatDescription = description;
    TestRate* rate = [TestRate new]; rate.minFrameRate = low; rate.maxFrameRate = high;
    format.videoSupportedFrameRateRanges = @[(AVFrameRateRange*)rate];
    return (AVCaptureDeviceFormat*)format;
}

static void DeviceFormats()
{
    using jalium::apple_media::ConfigureDeviceFormat;
    TestDevice* device = [TestDevice new]; device.canLock = YES;
    AVCaptureDevice* target = (AVCaptureDevice*)device;
    auto small = Format(640, 480, 15, 30), large = Format(1280, 720, 24, 60);
    device.formats = @[small, large]; device.activeFormat = small;
    Check(ConfigureDeviceFormat(nil, 0, 0, 0) == JALIUM_MEDIA_E_INVALID_ARG, "nil device");
    for (double fps : {-1.0, std::numeric_limits<double>::infinity(),
                       std::numeric_limits<double>::quiet_NaN()})
        Check(ConfigureDeviceFormat(target, 0, 0, fps) == JALIUM_MEDIA_E_INVALID_ARG,
            "reject invalid frame rates");
    Check(ConfigureDeviceFormat(target, 0, 0, 0) == JALIUM_MEDIA_OK && device.locks == 0
        && device.activeFormat == small, "unspecified format preserves device defaults");
    Check(ConfigureDeviceFormat(target, 1280, 720, 60) == JALIUM_MEDIA_OK
        && device.activeFormat == large, "select supported size and maximum frame rate");
    Check(std::abs(CMTimeGetSeconds(device.activeVideoMinFrameDuration) - 1.0 / 60) < 0.000002
        && CMTimeCompare(device.activeVideoMinFrameDuration, device.activeVideoMaxFrameDuration) == 0,
        "apply requested frame duration");
    Check(device.locks == 1 && device.unlocks == 1, "balanced format configuration lock");
    Check(ConfigureDeviceFormat(target, 640, 480, 15) == JALIUM_MEDIA_OK
        && device.activeFormat == small, "minimum frame rate boundary");
    Check(ConfigureDeviceFormat(target, 640, 480, 60) == JALIUM_MEDIA_E_UNSUPPORTED_FORMAT,
        "unsupported rate for requested size");
    Check(ConfigureDeviceFormat(target, 1920, 1080, 0) == JALIUM_MEDIA_E_UNSUPPORTED_FORMAT,
        "unsupported dimensions");
    Check(ConfigureDeviceFormat(target, 0, 720, 0) == JALIUM_MEDIA_OK
        && device.activeFormat == large, "partial size request");
    Check(ConfigureDeviceFormat(target, 0, 0, 60) == JALIUM_MEDIA_OK
        && device.activeFormat == large, "frame rate only request");
    auto before = device.unlocks; device.canLock = NO;
    Check(ConfigureDeviceFormat(target, 640, 480, 30) == JALIUM_MEDIA_E_PLATFORM
        && device.unlocks == before, "lock failure does not unlock or mutate device");
    device.canLock = YES; device.failSet = YES;
    Check(ConfigureDeviceFormat(target, 640, 480, 30) == JALIUM_MEDIA_E_PLATFORM
        && device.unlocks == before + 1, "stale device format reports error and unlocks");
}

static bool WriteVideo(NSURL* url)
{
    NSError* error = nil;
    AVAssetWriter* writer = [[AVAssetWriter alloc] initWithURL:url fileType:AVFileTypeQuickTimeMovie
        error:&error];
    AVAssetWriterInput* input = [AVAssetWriterInput assetWriterInputWithMediaType:AVMediaTypeVideo
        outputSettings:@{AVVideoCodecKey:AVVideoCodecTypeH264, AVVideoWidthKey:@64, AVVideoHeightKey:@48}];
    AVAssetWriterInputPixelBufferAdaptor* adaptor = [AVAssetWriterInputPixelBufferAdaptor
        assetWriterInputPixelBufferAdaptorWithAssetWriterInput:input
        sourcePixelBufferAttributes:@{(NSString*)kCVPixelBufferPixelFormatTypeKey:@(kCVPixelFormatType_32BGRA),
            (NSString*)kCVPixelBufferWidthKey:@64, (NSString*)kCVPixelBufferHeightKey:@48}];
    if (!writer || ![writer canAddInput:input]) return false;
    [writer addInput:input]; if (![writer startWriting]) return false;
    [writer startSessionAtSourceTime:kCMTimeZero];
    for (int frame = 0; frame < 4; ++frame) {
        auto until = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        while (!input.readyForMoreMediaData && std::chrono::steady_clock::now() < until)
            std::this_thread::sleep_for(std::chrono::milliseconds(2));
        if (!input.readyForMoreMediaData) { [writer cancelWriting]; return false; }
        CVPixelBufferRef buffer = nullptr;
        if (CVPixelBufferPoolCreatePixelBuffer(nullptr, adaptor.pixelBufferPool, &buffer) != kCVReturnSuccess)
            { [writer cancelWriting]; return false; }
        CVPixelBufferLockBaseAddress(buffer, 0);
        for (size_t y = 0; y < 48; ++y) {
            auto row = (uint8_t*)CVPixelBufferGetBaseAddress(buffer) + y * CVPixelBufferGetBytesPerRow(buffer);
            for (size_t x = 0; x < 64; ++x) {
                row[4*x] = 20; row[4*x+1] = 40; row[4*x+2] = 220; row[4*x+3] = 255;
            }
        }
        CVPixelBufferUnlockBaseAddress(buffer, 0);
        BOOL appended = [adaptor appendPixelBuffer:buffer withPresentationTime:CMTimeMake(frame, 12)];
        CFRelease(buffer); if (!appended) { [writer cancelWriting]; return false; }
    }
    [input markAsFinished];
    dispatch_semaphore_t finished = dispatch_semaphore_create(0);
    [writer finishWritingWithCompletionHandler:^{ dispatch_semaphore_signal(finished); }];
    return dispatch_semaphore_wait(finished, Deadline()) == 0 && writer.status == AVAssetWriterStatusCompleted;
}

static void RealVideo()
{
    NSString* folder = [NSTemporaryDirectory() stringByAppendingPathComponent:
        [@"jalium-apple-media-" stringByAppendingString:NSUUID.UUID.UUIDString]];
    [[NSFileManager defaultManager] createDirectoryAtPath:folder withIntermediateDirectories:YES
        attributes:nil error:nil];
    @try {
        NSURL* url = [NSURL fileURLWithPath:[folder stringByAppendingPathComponent:@"video.mov"]];
        bool written = WriteVideo(url); Check(written, "write owned local video fixture");
        if (!written) return;
        AVAssetTrack* track = nil;
        Check(jalium::apple_media::LoadVideoTrack([AVURLAsset URLAssetWithURL:url options:nil],
            &track, Deadline()) == JALIUM_MEDIA_OK && track != nil, "real async track load from main thread");
        dispatch_semaphore_t finished = dispatch_semaphore_create(0);
        __block jalium_media_status_t loaded = JALIUM_MEDIA_E_PLATFORM;
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_DEFAULT, 0), ^{
            AVAssetTrack* backgroundTrack = nil;
            loaded = jalium::apple_media::LoadVideoTrack([AVURLAsset URLAssetWithURL:url options:nil],
                &backgroundTrack, Deadline());
            dispatch_semaphore_signal(finished);
        });
        Check(dispatch_semaphore_wait(finished, Deadline()) == 0 && loaded == JALIUM_MEDIA_OK,
            "real async track load from background caller");
        for (auto pixelFormat : {JALIUM_PF_BGRA8, JALIUM_PF_RGBA8}) {
            jalium_video_decoder_t* decoder = nullptr;
            bool opened = jalium_video_decoder_open_file(url.path.UTF8String, pixelFormat, &decoder)
                == JALIUM_MEDIA_OK;
            Check(opened && decoder, "public decoder opens owned video");
            if (!decoder) continue;
            jalium_video_info_t info{};
            Check(jalium_video_decoder_get_info(decoder, &info) == JALIUM_MEDIA_OK
                && info.width == 64 && info.height == 48 && info.frame_rate > 0,
                "video dimensions and frame rate");
            jalium_video_frame_t frame{};
            bool read = jalium_video_decoder_read_frame(decoder, &frame) == JALIUM_MEDIA_OK;
            Check(read && frame.width == 64 && frame.height == 48 && frame.pixels, "read decoded pixels");
            if (read && frame.pixels) {
                int red = pixelFormat == JALIUM_PF_RGBA8 ? 0 : 2;
                Check(frame.pixels[red] > 190 && frame.pixels[1] < 65
                    && frame.pixels[2-red] < 45, "decoded channel ordering");
            }
            Check(jalium_video_decoder_seek_microseconds(decoder, 0) == JALIUM_MEDIA_OK
                && jalium_video_decoder_read_frame(decoder, &frame) == JALIUM_MEDIA_OK,
                "seek and decode again");
            jalium_video_decoder_gpu_descriptor_t descriptor{};
            bool acquired = jalium_video_decoder_acquire_gpu_surface_descriptor(decoder, &descriptor)
                == JALIUM_MEDIA_OK;
            Check(acquired && descriptor.lifetime_context, "retain native video surface descriptor");
            jalium_video_decoder_close(decoder);
            if (acquired) {
                CVPixelBufferRef retained = (CVPixelBufferRef)descriptor.lifetime_context;
                Check(CVPixelBufferGetWidth(retained) == 64, "surface remains alive after decoder close");
                jalium_video_decoder_release_gpu_surface_descriptor(&descriptor);
                Check(descriptor.lifetime_context == 0, "surface release clears descriptor");
            }
        }
        NSURL* missing = [NSURL fileURLWithPath:[folder stringByAppendingPathComponent:@"missing.mov"]];
        track = nil;
        Check(jalium::apple_media::LoadVideoTrack([AVURLAsset URLAssetWithURL:missing options:nil],
            &track, Deadline()) == JALIUM_MEDIA_E_IO && !track, "real missing file reports loading error");
    } @finally {
        [[NSFileManager defaultManager] removeItemAtPath:folder error:nil];
    }
}

int main()
{
    @autoreleasepool {
        TrackLoading(); DeviceFormats(); RealVideo();
        std::printf("Apple media: %d/%d checks passed\n", checks - failures, checks);
    }
    return failures ? 1 : 0;
}
