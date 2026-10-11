#pragma once

#import <AppKit/AppKit.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <unistd.h>
#include <mach-o/dyld.h>

static bool UsesAutomatedWindowValidationHost()
{
    return [[[NSBundle mainBundle] objectForInfoDictionaryKey:@"JaliumAutomatedWindowHost"] boolValue];
}

static int RunAutomatedWindowValidation(int argc, char** argv, int (*contracts)(int, char**))
{
    NSString* directory = nil;
    NSString* nonce = nil;
    if (argc != 1) {
        if (argc != 5 || std::strcmp(argv[1], "--window-validation-directory") != 0 ||
            std::strcmp(argv[3], "--window-validation-nonce") != 0) return 4;
        directory = [NSString stringWithUTF8String:argv[2]];
        nonce = [NSString stringWithUTF8String:argv[4]];
        BOOL isDirectory = NO;
        if (!directory.length || !nonce.length ||
            ![NSFileManager.defaultManager fileExistsAtPath:directory isDirectory:&isDirectory] ||
            !isDirectory) return 4;
    }
    NSString* logPath = directory ? [directory stringByAppendingPathComponent:@"host.log"] :
        [[NSBundle mainBundle] objectForInfoDictionaryKey:@"JaliumAutomatedWindowLog"];
    if (logPath.length && (!freopen(logPath.fileSystemRepresentation, "a", stdout) ||
        !freopen(logPath.fileSystemRepresentation, "a", stderr))) return 4;
    setvbuf(stdout, nullptr, _IONBF, 0);
    setvbuf(stderr, nullptr, _IONBF, 0);

    [NSApplication sharedApplication];
    [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];
    static std::atomic<bool> completed{false};
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 55 * NSEC_PER_SEC),
        dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{
            if (!completed.load()) {
                std::fprintf(stderr, "FAIL: Window validation application watchdog\n");
                _Exit(124);
            }
        });
    std::printf("HOST_STARTED pid=%d bundle=%s\n", getpid(), NSBundle.mainBundle.bundleIdentifier.UTF8String);
    for (uint32_t index = 0; index < _dyld_image_count(); ++index) {
        const char* name = _dyld_get_image_name(index);
        if (name && std::strstr(name, "libjalium.native.")) std::printf("HOST_LOADED %s\n", name);
    }
    __block int result = 5;
    dispatch_async(dispatch_get_main_queue(), ^{
        result = contracts(1, argv);
        std::printf("HOST_FINISHED pid=%d result=%d\n", getpid(), result);
        [NSApp stop:nil];
        [NSApp postEvent:[NSEvent otherEventWithType:NSEventTypeApplicationDefined location:NSZeroPoint
            modifierFlags:0 timestamp:0 windowNumber:0 context:nil subtype:0 data1:0 data2:0] atStart:NO];
    });
    [NSApp run];
    std::printf("HOST_EXIT pid=%d result=%d\n", getpid(), result);
    if (directory) {
        NSDictionary* record = @{@"schema":@1, @"nonce":nonce, @"pid":@(getpid()), @"result":@(result)};
        NSError* error = nil;
        NSData* data = [NSJSONSerialization dataWithJSONObject:record options:0 error:&error];
        NSString* path = [directory stringByAppendingPathComponent:@"result.json"];
        if (!data || ![data writeToFile:path options:NSDataWritingAtomic error:&error]) {
            std::fprintf(stderr, "FAIL: Cannot publish Window validation result: %s\n",
                error.localizedDescription.UTF8String);
            return 4;
        }
    }
    completed.store(true);
    return result;
}
