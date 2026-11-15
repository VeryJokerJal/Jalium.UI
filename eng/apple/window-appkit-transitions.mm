#import <AppKit/AppKit.h>
#include "../../src/native/jalium.native.platform/tests/platform_apple_window_validation_app.h"

// Run independently of the Jalium subclass, delegate, state machine and native
// library. Every phase returns to the actual NSApplication outer event loop.
@interface JaliumAppKitTransitionProbe : NSObject
@property(nonatomic, copy) void (^complete)(int);
@property(nonatomic, strong) NSWindow* window;
@property(nonatomic, strong) NSMutableArray* observers;
@property(nonatomic) int phase;
@property(nonatomic) int styleIndex;
@property(nonatomic) int passed;
@property(nonatomic) int will;
@property(nonatomic) int did;
@property(nonatomic) int restored;
@property(nonatomic) double deadline;
@property(nonatomic) double started;
- (void)tick:(NSTimer*)timer;
@end

@implementation JaliumAppKitTransitionProbe
- (const char*)label { return self.styleIndex == 0 ? "native" : "borderless"; }
- (void)setPhase:(int)phase {
    _phase = phase;
    _deadline = NSProcessInfo.processInfo.systemUptime + 8;
}
- (void)logState:(const char*)stage passed:(BOOL)passed {
    std::printf("APPKIT_%s style=%s passed=%d phase=%d will=%d did=%d restored=%d mini=%d visible=%d elapsed=%.3f occlusion=%lu activeSpace=%d\n",
        stage, self.label, passed, self.phase, self.will, self.did, self.restored,
        self.window.miniaturized, self.window.visible, NSProcessInfo.processInfo.systemUptime - self.started,
        (unsigned long)self.window.occlusionState, self.window.onActiveSpace);
}
- (void)createWindow {
    const auto common = NSWindowStyleMaskMiniaturizable | NSWindowStyleMaskResizable;
    const auto style = common | (self.styleIndex == 0
        ? NSWindowStyleMaskTitled | NSWindowStyleMaskClosable : NSWindowStyleMaskBorderless);
    self.window = [[NSWindow alloc] initWithContentRect:NSMakeRect(200, 200, 480, 320)
        styleMask:style backing:NSBackingStoreBuffered defer:NO];
    self.window.releasedWhenClosed = NO;
    self.window.restorable = NO;
    self.window.title = @"Jalium AppKit transition qualification";
    self.will = self.did = self.restored = 0;
    self.started = NSProcessInfo.processInfo.systemUptime;
    self.observers = [NSMutableArray new];
    __weak JaliumAppKitTransitionProbe* weakProbe = self;
    for (NSNotificationName name : {NSWindowWillMiniaturizeNotification,
        NSWindowDidMiniaturizeNotification, NSWindowDidDeminiaturizeNotification}) {
        [self.observers addObject:[NSNotificationCenter.defaultCenter addObserverForName:name
            object:self.window queue:nil usingBlock:^(NSNotification* note) {
                if (!NSThread.isMainThread) {
                    std::fprintf(stderr, "FAIL: AppKit transition notification arrived off the main thread\n");
                    return;
                }
                JaliumAppKitTransitionProbe* probe = weakProbe;
                if (!probe) return;
                if ([note.name isEqual:NSWindowWillMiniaturizeNotification]) ++probe->_will;
                if ([note.name isEqual:NSWindowDidMiniaturizeNotification]) ++probe->_did;
                if ([note.name isEqual:NSWindowDidDeminiaturizeNotification]) ++probe->_restored;
                std::printf("APPKIT_NOTIFICATION style=%s name=%s elapsed=%.3f main=1 mini=%d visible=%d\n",
                    probe.label, note.name.UTF8String, NSProcessInfo.processInfo.systemUptime - probe.started,
                    probe.window.miniaturized, probe.window.visible);
            }]];
    }
    self.phase = 1;
    [self.window makeKeyAndOrderFront:nil];
}
- (void)finishWindow:(NSTimer*)timer passed:(BOOL)passed {
    [self logState:"WINDOW" passed:passed];
    if (passed) ++_passed;
    for (id observer in self.observers) [NSNotificationCenter.defaultCenter removeObserver:observer];
    [self.window orderOut:nil];
    [self.window close];
    self.window = nil;
    self.observers = nil;
    if (++_styleIndex < 2) {
        [self createWindow];
    } else {
        [timer invalidate];
        std::printf("APPKIT_RESULT passed=%d total=2 outerLoop=1\n", self.passed);
        self.complete(self.passed == 2 ? 0 : 1);
    }
}
- (void)tick:(NSTimer*)timer {
    if (self.phase == 0) {
        NSArray<NSRunningApplication*>* apps = [NSRunningApplication runningApplicationsWithBundleIdentifier:@"com.apple.dock"];
        bool ready = false;
        for (NSRunningApplication* app in apps)
            if (app.finishedLaunching && !app.terminated) ready = true;
        if (!ready && NSProcessInfo.processInfo.systemUptime < self.deadline) return;
        std::printf("APPKIT_ENVIRONMENT os=%s screens=%lu dockCount=%lu active=%d running=%d outerLoop=1\n",
            NSProcessInfo.processInfo.operatingSystemVersionString.UTF8String,
            (unsigned long)NSScreen.screens.count, (unsigned long)apps.count, NSApp.active, NSApp.running);
        for (NSRunningApplication* app in apps)
            std::printf("APPKIT_DOCK pid=%d terminated=%d finishedLaunching=%d\n",
                app.processIdentifier, app.terminated, app.finishedLaunching);
        if (!ready) {
            [timer invalidate];
            std::fprintf(stderr, "FAIL: The AppKit transition prerequisite requires a running Dock service\n");
            self.complete(2);
        } else [self createWindow];
        return;
    }
    if (NSProcessInfo.processInfo.systemUptime >= self.deadline) {
        [self logState:"TIMEOUT" passed:NO];
        [self finishWindow:timer passed:NO];
    } else if (self.phase == 1 && self.window.visible && self.window.windowNumber > 0 &&
        self.window.onActiveSpace && (self.window.occlusionState & NSWindowOcclusionStateVisible)) {
        // Ordering a window does not prove that WindowServer has exposed it.
        // Qualify the visible window before asking the Dock to animate it.
        std::printf("APPKIT_BEFORE style=%s active=%d mask=%lu key=%d\n", self.label,
            NSApp.active, (unsigned long)self.window.styleMask, self.window.keyWindow);
        self.phase = 2;
        [self.window miniaturize:nil];
    } else if (self.phase == 2 && self.did == 1 && self.window.miniaturized) {
        [self logState:"MINIMIZED" passed:YES];
        self.phase = 3;
        [self.window deminiaturize:nil];
    } else if (self.phase == 3 && self.restored == 1 && !self.window.miniaturized && self.window.visible) {
        [self logState:"RESTORED" passed:YES];
        [self finishWindow:timer passed:self.will == 1 && self.did == 1 && self.restored == 1];
    }
}
@end

int main(int argc, char** argv)
{
    @autoreleasepool {
        return RunAutomatedWindowValidation(argc, argv, nullptr, ^(void (^complete)(int)) {
            JaliumAppKitTransitionProbe* probe = [JaliumAppKitTransitionProbe new];
            probe.complete = complete;
            probe.phase = 0;
            [NSTimer scheduledTimerWithTimeInterval:0.01 repeats:YES block:^(NSTimer* timer) { [probe tick:timer]; }];
        });
    }
}
