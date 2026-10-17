#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <dlfcn.h>
#include <initializer_list>
#include <stdexcept>
#include <string>
#include <thread>

static std::atomic<unsigned> markerCount{0};
static std::atomic<bool> markerOffMain{false};
static constexpr short MarkerSubtype = 29173;

@interface JaliumThreadOwnershipApplication : NSApplication
@end
@implementation JaliumThreadOwnershipApplication
- (void)sendEvent:(NSEvent*)event
{
    if (event.type == NSEventTypeApplicationDefined && event.subtype == MarkerSubtype) {
        if (!NSThread.isMainThread) markerOffMain.store(true);
        ++markerCount;
        return;
    }
    [super sendEvent:event];
}
@end

namespace {
int checks = 0;
void Check(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
    ++checks;
}

JaliumWindowParams Params(uint32_t style)
{
    JaliumWindowParams value{};
    value.title = reinterpret_cast<const JaliumUtf16Char*>(u"Owned AppKit thread validation");
    value.x = value.y = 100; value.width = 320; value.height = 180; value.style = style;
    return value;
}

struct WorkerResult {
    bool mainThread = true;
    std::string exception;
    JaliumPlatformWindow* window = nullptr;
    int32_t poll = 123456;
};

WorkerResult Worker(bool create, bool poll, uint32_t style)
{
    WorkerResult result;
    std::thread worker([&] {
        @autoreleasepool {
            result.mainThread = NSThread.isMainThread;
            @try {
                if (create) { auto parameters = Params(style); result.window = jalium_window_create(&parameters); }
                if (poll) result.poll = jalium_platform_poll_events();
            } @catch (NSException* error) {
                result.exception = error.reason.UTF8String ?: "NSException";
            }
        }
    });
    worker.join();
    return result;
}

void Cold(bool create)
{
    Check(NSThread.isMainThread, "test starts on the actual main thread");
    Check(NSApp == nil, "cold process has no NSApplication");
    auto result = Worker(create, !create, JALIUM_WINDOW_STYLE_TITLEBAR);
    Check(!result.mainThread, "entry really ran on a foreign thread");
    if (!result.exception.empty()) std::fprintf(stderr, "Worker NSException: %s\n", result.exception.c_str());
    Check(result.exception.empty(), "foreign call returns without entering unsafe AppKit");
    Check(create ? result.window == nullptr : result.poll == JALIUM_ERROR_INVALID_STATE,
        create ? "foreign creation is rejected before application initialization" :
                 "foreign polling is rejected before application initialization");
    Check(NSApp == nil, "rejection did not initialize NSApplication");
}

void InitializedChecks(bool create)
{
    Check(NSThread.isMainThread && NSApp.running, "positive control runs inside the actual main loop");
    for (uint32_t style : {uint32_t(JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE),
                           uint32_t(JALIUM_WINDOW_STYLE_BORDERLESS | JALIUM_WINDOW_STYLE_CLOSABLE)}) {
        auto parameters = Params(style);
        auto* window = jalium_window_create(&parameters);
        Check(window != nullptr, "main thread still creates the requested titlebar style");
        NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(window);
        NSWindow* native = view.window;
        Check(native != nil && !native.visible, "positive control owns a hidden AppKit window");
        NSArray<NSWindow*>* before = NSApp.windows.copy;
        NSRect frame = native.frame;
        NSResponder* responder = native.firstResponder;
        unsigned delivered = markerCount.load();
        // This is a marker in this process's AppKit queue, not injected input.
        [NSApp postEvent:[NSEvent otherEventWithType:NSEventTypeApplicationDefined location:NSZeroPoint
            modifierFlags:0 timestamp:0 windowNumber:0 context:nil subtype:MarkerSubtype data1:0 data2:0]
            atStart:NO];
        auto result = Worker(create, !create, style);
        Check(!result.mainThread, "initialized entry really ran on a foreign thread");
        if (!result.exception.empty()) std::fprintf(stderr, "Worker NSException: %s\n", result.exception.c_str());
        Check(result.exception.empty(), "initialized foreign call does not throw an AppKit exception");
        if (create) Check(result.window == nullptr, "foreign creation does not construct another window");
        else Check(result.poll == JALIUM_ERROR_INVALID_STATE, "foreign polling rejects the AppKit queue");
        Check([before isEqualToArray:NSApp.windows], "rejected calls do not register partial windows");
        Check(NSEqualRects(frame, native.frame) && responder == native.firstResponder,
            "rejected calls preserve the existing frame and responder");
        Check(markerCount.load() == delivered, "foreign polling leaves the queued marker untouched");
        int32_t processed = jalium_platform_poll_events();
        std::fprintf(stderr, "Main queue control: class=%s running=%d processed=%d marker=%u expected=%u\n",
            NSStringFromClass(NSApp.class).UTF8String, NSApp.running, processed, markerCount.load(), delivered + 1);
        Check(processed >= 1, "main thread still drains its AppKit events");
        Check(markerCount.load() == delivered + 1 && !markerOffMain.load(),
            "main polling alone delivers the original marker exactly once");
        jalium_window_destroy(window);
    }
}

void Initialized(bool create)
{
    [JaliumThreadOwnershipApplication sharedApplication];
    [NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited];
    Check(jalium_platform_init() == JALIUM_OK, "initialize the native platform on main");
    __block bool ran = false;
    __block std::string failure;
    [NSTimer scheduledTimerWithTimeInterval:.01 repeats:NO block:^(NSTimer*) {
        try { InitializedChecks(create); ran = true; }
        catch (const std::exception& error) { failure = error.what(); }
        [NSApp stop:nil];
        [NSApp postEvent:[NSEvent otherEventWithType:NSEventTypeApplicationDefined location:NSZeroPoint
            modifierFlags:0 timestamp:0 windowNumber:0 context:nil subtype:0 data1:0 data2:0] atStart:YES];
    }];
    [NSApp run];
    if (!failure.empty()) throw std::runtime_error(failure);
    Check(ran, "main-loop callback completed");
    jalium_platform_shutdown();
}
}

int main(int argc, char** argv)
{
    @autoreleasepool {
        setvbuf(stdout, nullptr, _IONBF, 0);
        Dl_info loaded{}; dladdr((void*)&jalium_window_create, &loaded);
        std::printf("Loaded thread-test platform: %s\n", loaded.dli_fname ?: "<unknown>");
        try {
            if (argc == 2 && std::strcmp(argv[1], "--cold-create") == 0) Cold(true);
            else if (argc == 2 && std::strcmp(argv[1], "--cold-poll") == 0) Cold(false);
            else if (argc == 2 && std::strcmp(argv[1], "--initialized-create") == 0) Initialized(true);
            else if (argc == 2 && std::strcmp(argv[1], "--initialized-poll") == 0) Initialized(false);
            else throw std::runtime_error("expected a cold or initialized create/poll case");
            std::printf("PASS AppKit thread ownership: %d checks; own queue, no physical input\n", checks);
            return 0;
        } catch (const std::exception& error) {
            std::fprintf(stderr, "FAIL AppKit thread ownership: %s after %d checks\n", error.what(), checks);
            return 1;
        }
    }
}
