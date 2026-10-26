#import <AppKit/AppKit.h>
#include "../../src/native/jalium.native.platform/tests/platform_apple_window_validation_app.h"
#include <functional>

// Qualify the runner's actual AppKit transition delivery independently of the
// Jalium subclass, delegate, state machine and native library.
static bool Pump(const std::function<bool()>& complete)
{
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(8);
    while (!complete() && std::chrono::steady_clock::now() < deadline) {
        while (NSEvent* event = [NSApp nextEventMatchingMask:NSEventMaskAny untilDate:NSDate.date
            inMode:NSDefaultRunLoopMode dequeue:YES]) [NSApp sendEvent:event];
        [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.01]];
    }
    return complete();
}

static bool CheckTransition(NSWindowStyleMask style, const char* label)
{
    NSWindow* window = [[NSWindow alloc] initWithContentRect:NSMakeRect(200, 200, 480, 320)
        styleMask:style backing:NSBackingStoreBuffered defer:NO];
    window.releasedWhenClosed = NO;
    window.restorable = NO;
    window.title = @"Jalium AppKit transition qualification";
    std::atomic<int> counts[3]{};
    auto* const values = counts;
    const double started = NSProcessInfo.processInfo.systemUptime;
    NSMutableArray* observers = [NSMutableArray new];
    for (NSNotificationName name : {NSWindowWillMiniaturizeNotification,
        NSWindowDidMiniaturizeNotification, NSWindowDidDeminiaturizeNotification}) {
        [observers addObject:[NSNotificationCenter.defaultCenter addObserverForName:name
            object:window queue:nil usingBlock:^(NSNotification* note) {
                if ([note.name isEqual:NSWindowWillMiniaturizeNotification]) ++values[0];
                if ([note.name isEqual:NSWindowDidMiniaturizeNotification]) ++values[1];
                if ([note.name isEqual:NSWindowDidDeminiaturizeNotification]) ++values[2];
                std::printf("APPKIT_NOTIFICATION style=%s name=%s elapsed=%.3f main=%d mini=%d visible=%d\n",
                    label, note.name.UTF8String, NSProcessInfo.processInfo.systemUptime - started,
                    NSThread.isMainThread, window.miniaturized, window.visible);
            }]];
    }
    [window makeKeyAndOrderFront:nil];
    bool shown = Pump([&] { return window.visible && window.keyWindow; });
    std::printf("APPKIT_BEFORE style=%s shown=%d active=%d mask=%lu canMini=%d\n",
        label, shown, NSApp.active, (unsigned long)window.styleMask,
        [window standardWindowButton:NSWindowMiniaturizeButton].enabled);
    [window miniaturize:nil];
    bool minimized = Pump([&] { return counts[1] == 1 && window.miniaturized; });
    std::printf("APPKIT_MINIMIZED style=%s passed=%d will=%d did=%d mini=%d visible=%d elapsed=%.3f\n",
        label, minimized, counts[0].load(), counts[1].load(), window.miniaturized, window.visible,
        NSProcessInfo.processInfo.systemUptime - started);
    bool deminiaturized = false;
    if (minimized) {
        [window deminiaturize:nil];
        deminiaturized = Pump([&] { return counts[2] == 1 && !window.miniaturized && window.visible; });
        std::printf("APPKIT_RESTORED style=%s passed=%d did=%d mini=%d visible=%d\n",
            label, deminiaturized, counts[2].load(), window.miniaturized, window.visible);
    }
    for (id observer in observers) [NSNotificationCenter.defaultCenter removeObserver:observer];
    [window orderOut:nil];
    [window close];
    return shown && minimized && deminiaturized && counts[0] == 1 && counts[1] == 1 && counts[2] == 1;
}

static int Contracts(int, char**)
{
    NSArray<NSRunningApplication*>* dock = [NSRunningApplication runningApplicationsWithBundleIdentifier:@"com.apple.dock"];
    std::printf("APPKIT_ENVIRONMENT os=%s screens=%lu dockCount=%lu active=%d running=%d\n",
        NSProcessInfo.processInfo.operatingSystemVersionString.UTF8String,
        (unsigned long)NSScreen.screens.count, (unsigned long)dock.count, NSApp.active, NSApp.running);
    for (NSRunningApplication* app in dock)
        std::printf("APPKIT_DOCK pid=%d terminated=%d finishedLaunching=%d\n",
            app.processIdentifier, app.terminated, app.finishedLaunching);
    const auto common = NSWindowStyleMaskMiniaturizable | NSWindowStyleMaskResizable;
    bool native = CheckTransition(common | NSWindowStyleMaskTitled | NSWindowStyleMaskClosable, "native");
    bool custom = CheckTransition(common | NSWindowStyleMaskBorderless, "borderless");
    std::printf("APPKIT_RESULT native=%d borderless=%d\n", native, custom);
    return native && custom ? 0 : 1;
}

int main(int argc, char** argv)
{
    @autoreleasepool { return RunAutomatedWindowValidation(argc, argv, Contracts); }
}
