#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <atomic>

// The override is confined to this test process. No system preference is written.
static std::atomic<int> reduced{0};
static Method getter;
static IMP original;
static BOOL read_override(id, SEL) { return reduced.load() != 0; }

extern "C" int jalium_motion_test_install(int value) {
    if (original) return 0;
    getter = class_getInstanceMethod(object_getClass(NSWorkspace.sharedWorkspace), @selector(accessibilityDisplayShouldReduceMotion));
    if (!getter) return 0;
    reduced.store(value != 0);
    original = method_setImplementation(getter, (IMP)read_override);
    return original != nullptr;
}
extern "C" void jalium_motion_test_set(int value) { reduced.store(value != 0); }
extern "C" int jalium_motion_test_read(void) { return NSWorkspace.sharedWorkspace.accessibilityDisplayShouldReduceMotion; }
extern "C" int jalium_motion_test_restore(void) {
    if (!original || method_getImplementation(getter) != (IMP)read_override) return 0;
    method_setImplementation(getter, original);
    original = nullptr;
    return 1;
}
