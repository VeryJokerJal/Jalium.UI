#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <cstdint>
#include <mutex>
#include <unordered_map>

// Process-local instrumentation for our SDK fixture, never the system Dock.
static IMP previousDisplay = nullptr;
static Method displayMethod = nullptr;
static std::mutex displayMutex;
static std::unordered_map<uintptr_t, uint64_t> displays;

static void CountDisplay(id receiver, SEL selector)
{
    {
        std::lock_guard<std::mutex> lock(displayMutex);
        ++displays[(uintptr_t)(__bridge void*)receiver];
    }
    ((void (*)(id, SEL))previousDisplay)(receiver, selector);
}

extern "C" int jalium_dock_display_install()
{
    if (previousDisplay) return 0;
    displayMethod = class_getInstanceMethod([NSDockTile class], @selector(display));
    if (!displayMethod) return 0;
    previousDisplay = method_getImplementation(displayMethod);
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
    return 1;
}
