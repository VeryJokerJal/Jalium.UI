#pragma once

#include "jalium_media.h"
#include <cmath>
#include <memory>
#import <TargetConditionals.h>
#import <AVFoundation/AVFoundation.h>

namespace jalium::apple_media {

// The C decoder API is synchronous. Keep its async AVFoundation operation's
// state alive even when cancellation finishes after the caller's deadline.
inline jalium_media_status_t LoadVideoTrack(AVAsset* asset, AVAssetTrack** output,
    dispatch_time_t deadline)
{
    if (!asset || !output) return JALIUM_MEDIA_E_INVALID_ARG;
    *output = nil;
    struct State {
        dispatch_semaphore_t ready = dispatch_semaphore_create(0);
        AVAssetTrack* track = nil;
        NSError* error = nil;
    };
    auto state = std::make_shared<State>();
    [asset loadTracksWithMediaType:AVMediaTypeVideo completionHandler:^(NSArray<AVAssetTrack*>* tracks, NSError* error) {
        state->track = tracks.firstObject;
        state->error = error;
        dispatch_semaphore_signal(state->ready);
    }];
    if (dispatch_semaphore_wait(state->ready, deadline) != 0) {
        [asset cancelLoading];
        return JALIUM_MEDIA_E_PLATFORM;
    }
    if (state->error) return JALIUM_MEDIA_E_IO;
    if (!state->track) return JALIUM_MEDIA_E_UNSUPPORTED_CODEC;
    *output = state->track;
    return JALIUM_MEDIA_OK;
}

#if !TARGET_OS_TV
// visionOS has device formats, but no AVCaptureSession presets. Select a
// supported device format rather than discarding the camera implementation.
inline jalium_media_status_t ConfigureDeviceFormat(AVCaptureDevice* device,
    uint32_t width, uint32_t height, double fps)
{
    if (!device || !std::isfinite(fps) || fps < 0) return JALIUM_MEDIA_E_INVALID_ARG;
    if (!width && !height && fps == 0) return JALIUM_MEDIA_OK;
    AVCaptureDeviceFormat* selected = nil;
    for (AVCaptureDeviceFormat* format in device.formats) {
        CMVideoDimensions size = CMVideoFormatDescriptionGetDimensions(format.formatDescription);
        if ((width && size.width != width) || (height && size.height != height)) continue;
        bool rateSupported = fps == 0;
        for (AVFrameRateRange* range in format.videoSupportedFrameRateRanges)
            if (range.minFrameRate <= fps && fps <= range.maxFrameRate) rateSupported = true;
        if (rateSupported) { selected = format; break; }
    }
    if (!selected) return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    NSError* error = nil;
    if (![device lockForConfiguration:&error]) return JALIUM_MEDIA_E_PLATFORM;
    @try {
        device.activeFormat = selected;
        if (fps > 0) {
            CMTime duration = CMTimeMakeWithSeconds(1.0 / fps, 1000000);
            device.activeVideoMinFrameDuration = duration;
            device.activeVideoMaxFrameDuration = duration;
        }
    } @catch (NSException*) {
        return JALIUM_MEDIA_E_PLATFORM;
    } @finally { [device unlockForConfiguration]; }
    return JALIUM_MEDIA_OK;
}
#endif
}
