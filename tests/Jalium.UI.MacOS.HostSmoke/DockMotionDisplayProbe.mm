#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <mutex>
#include <unordered_map>

// Process-local instrumentation for our SDK fixture, never the system Dock.
static IMP previousDisplay = nullptr;
static Method displayMethod = nullptr;
static bool traceDisplay = false;
static std::mutex displayMutex;
static std::unordered_map<uintptr_t, uint64_t> displays;

static void CountDisplay(id receiver, SEL selector)
{
    uint64_t count;
    {
        std::lock_guard<std::mutex> lock(displayMutex);
        count = ++displays[(uintptr_t)(__bridge void*)receiver];
    }
    if (traceDisplay && receiver == NSApp.dockTile) {
        std::fprintf(stderr, "DOCK_DISPLAY %.6f count=%llu\n", NSDate.timeIntervalSinceReferenceDate,
            (unsigned long long)count);
        for (NSString* frame in NSThread.callStackSymbols)
            std::fprintf(stderr, "DOCK_STACK %s\n", frame.UTF8String);
    }
    ((void (*)(id, SEL))previousDisplay)(receiver, selector);
}

extern "C" int jalium_dock_display_install()
{
    if (previousDisplay) return 0;
    displayMethod = class_getInstanceMethod([NSDockTile class], @selector(display));
    if (!displayMethod) return 0;
    previousDisplay = method_getImplementation(displayMethod);
    const char* diagnostics = std::getenv("JALIUM_DOCK_DISPLAY_DIAGNOSTICS");
    traceDisplay = diagnostics && diagnostics[0] == '1';
    method_setImplementation(displayMethod, (IMP)CountDisplay);
    return 1;
}

extern "C" uint64_t jalium_dock_display_count(uintptr_t receiver)
{
    std::lock_guard<std::mutex> lock(displayMutex);
    return displays[receiver];
}

extern "C" int jalium_dock_display_restore()
{
    if (!previousDisplay) return 1;
    if (method_getImplementation(displayMethod) != (IMP)CountDisplay) return 0;
    method_setImplementation(displayMethod, previousDisplay);
    previousDisplay = nullptr;
    traceDisplay = false;
    return 1;
}
