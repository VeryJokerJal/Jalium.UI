#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <cmath>
#include <cstdio>

// These doubles supply coordinate data to the real exported ABI. They do not
// move the system pointer, create displays, or validate a physical drag.
@interface JaliumCursorScreen : NSObject
@property NSRect frame;
@property NSRect visibleFrame;
@property CGFloat backingScaleFactor;
@end
@implementation JaliumCursorScreen
@end

static NSArray* screens;
static NSPoint pointer;
static int passed, checked;
static NSArray* TestScreens(id, SEL) { return screens; }
static NSPoint TestPointer(id, SEL) { return pointer; }

static void Check(bool valid, const char* name)
{
    ++checked;
    if (valid) ++passed;
    else std::fprintf(stderr, "FAIL: %s\n", name);
}

class Replacement {
    Method method;
    IMP original;
public:
    Replacement(Class type, SEL selector, IMP replacement)
        : method(class_getClassMethod(type, selector)), original(method_setImplementation(method, replacement)) {}
    ~Replacement() { method_setImplementation(method, original); }
};

static JaliumCursorScreen* Screen(double x, double y, double width, double height, double scale)
{
    auto* result = [JaliumCursorScreen new];
    result.frame = NSMakeRect(x, y, width, height);
    result.visibleFrame = result.frame;
    result.backingScaleFactor = scale;
    return result;
}

static void CheckPoint(const char* name, NSArray* topology, NSPoint position, NSPoint expected,
    JaliumResult result = JALIUM_OK)
{
    screens = topology; pointer = position;
    float x = 77, y = 88;
    auto actual = jalium_input_get_cursor_pos(&x, &y);
    bool valid = actual == result && std::abs(x - expected.x) < .001 && std::abs(y - expected.y) < .001;
    if (!valid) std::fprintf(stderr, "  result=%d, point=(%.3f,%.3f), expected=%d (%.3f,%.3f)\n",
        actual, x, y, result, expected.x, expected.y);
    Check(valid, name);
}

static void LivePointer()
{
    NSArray<NSScreen*>* actualScreens = NSScreen.screens;
    for (int attempt = 0; attempt < 20; ++attempt) {
        NSPoint before = NSEvent.mouseLocation;
        float x = 0, y = 0;
        auto result = jalium_input_get_cursor_pos(&x, &y);
        if (!NSEqualPoints(before, NSEvent.mouseLocation)) continue;
        if (!actualScreens.count) {
            Check(result == JALIUM_ERROR_INVALID_STATE && x == 0 && y == 0, "live unavailable desktop");
            return;
        }
        NSScreen* closest = nil;
        double distance = INFINITY;
        for (NSScreen* screen in actualScreens) {
            NSRect f = screen.frame;
            if (NSIsEmptyRect(f)) continue;
            double dx = MAX(MAX(NSMinX(f) - before.x, 0), before.x - NSMaxX(f));
            double dy = MAX(MAX(NSMinY(f) - before.y, 0), before.y - NSMaxY(f));
            double next = dx * dx + dy * dy;
            if (NSPointInRect(before, f)) { closest = screen; break; }
            if (next < distance) { closest = screen; distance = next; }
        }
        if (!closest) {
            Check(result == JALIUM_ERROR_INVALID_STATE && x == 0 && y == 0, "live invalid desktop frames");
            return;
        }
        CGFloat primary = actualScreens.firstObject.backingScaleFactor ?: 1;
        CGFloat scale = closest.backingScaleFactor ?: 1;
        double top = NSMaxY(actualScreens.firstObject.frame);
        NSRect frame = closest.frame;
        double expectedX = NSMinX(frame) * primary + (before.x - NSMinX(frame)) * scale;
        double expectedY = (top - NSMaxY(frame)) * primary + (NSMaxY(frame) - before.y) * scale;
        std::printf("LIVE: displays=%lu, scale=%.1f, AppKit=(%.3f,%.3f), cursor=(%.3f,%.3f), expected=(%.3f,%.3f)\n",
            actualScreens.count, scale, before.x, before.y, x, y, expectedX, expectedY);
        Check(result == JALIUM_OK && std::abs(x - expectedX) < .001 && std::abs(y - expectedY) < .001,
            "live cursor uses the window coordinate contract");
        return;
    }
    Check(false, "live pointer did not produce a stable sample");
}

int main()
{
    @autoreleasepool {
        LivePointer();
        float x = 77, y = 88;
        Check(jalium_input_get_cursor_pos(nullptr, &y) == JALIUM_ERROR_INVALID_ARGUMENT && y == 88,
            "missing X output leaves Y unchanged");
        Check(jalium_input_get_cursor_pos(&x, nullptr) == JALIUM_ERROR_INVALID_ARGUMENT && x == 77,
            "missing Y output leaves X unchanged");
        Check(jalium_input_get_cursor_pos(nullptr, nullptr) == JALIUM_ERROR_INVALID_ARGUMENT, "missing outputs");
        IMP originalLocation = method_getImplementation(class_getClassMethod(NSEvent.class, @selector(mouseLocation)));
        IMP originalScreens = method_getImplementation(class_getClassMethod(NSScreen.class, @selector(screens)));
        {
            Replacement location(NSEvent.class, @selector(mouseLocation), reinterpret_cast<IMP>(TestPointer));
            Replacement topology(NSScreen.class, @selector(screens), reinterpret_cast<IMP>(TestScreens));
            auto* primary1 = Screen(0, 0, 1440, 900, 1);
            auto* primary2 = Screen(0, 0, 1440, 900, 2);
            auto* right1 = Screen(1440, 100, 1920, 1080, 1);
            auto* right2 = Screen(1440, 0, 1920, 900, 2);
            auto* left1 = Screen(-1920, 0, 1920, 1080, 1);
            auto* above2 = Screen(100, 900, 1400, 800, 2);
            auto* below1 = Screen(0, -1080, 1920, 1080, 1);
            CheckPoint("1x top-left origin", @[primary1], NSMakePoint(100, 850), NSMakePoint(100, 50));
            CheckPoint("2x fractional coordinates", @[primary2], NSMakePoint(100.25, 850.75), NSMakePoint(200.5, 98.5));
            CheckPoint("mixed right display", @[primary2, right1], NSMakePoint(1540, 1100), NSMakePoint(2980, -480));
            CheckPoint("negative left display", @[primary2, left1], NSMakePoint(-1820, 1000), NSMakePoint(-3740, -280));
            CheckPoint("2x display above primary", @[primary1, above2], NSMakePoint(150, 1650), NSMakePoint(200, -700));
            CheckPoint("1x display below primary", @[primary2, below1], NSMakePoint(100, -100), NSMakePoint(100, 1900));
            CheckPoint("primary display stays 1x", @[primary1, right2], NSMakePoint(100, 850), NSMakePoint(100, 50));
            CheckPoint("pointer chooses secondary 2x", @[primary1, right2], NSMakePoint(1640, 800), NSMakePoint(1840, 200));
            CheckPoint("shared X edge chooses containing display", @[primary1, right2], NSMakePoint(1440, 800), NSMakePoint(1440, 200));
            CheckPoint("nearest off-screen display", @[primary2, left1], NSMakePoint(-2000, 1000), NSMakePoint(-3920, -280));
            auto* noWorkArea = Screen(1440, 100, 1920, 1080, 1);
            noWorkArea.visibleFrame = NSZeroRect;
            CheckPoint("cursor uses full frame without a work area", @[primary2, noWorkArea], NSMakePoint(1540, 1100), NSMakePoint(2980, -480));
            CheckPoint("missing scale falls back to 1x", @[Screen(0, 0, 1440, 900, 0)], NSMakePoint(100, 850), NSMakePoint(100, 50));
            CheckPoint("no displays", @[], NSMakePoint(100, 850), NSZeroPoint, JALIUM_ERROR_INVALID_STATE);
            CheckPoint("empty display frame", @[Screen(0, 0, 0, 0, 1)], NSMakePoint(100, 850), NSZeroPoint, JALIUM_ERROR_INVALID_STATE);
            CheckPoint("nonfinite pointer", @[primary1], NSMakePoint(NAN, 850), NSZeroPoint, JALIUM_ERROR_INVALID_STATE);
        }
        Check(method_getImplementation(class_getClassMethod(NSEvent.class, @selector(mouseLocation))) == originalLocation &&
            method_getImplementation(class_getClassMethod(NSScreen.class, @selector(screens))) == originalScreens,
            "coordinate data replacements are restored");
        std::printf("macOS cursor coordinate checks: %d/%d passed\n", passed, checked);
        return passed == checked ? 0 : 1;
    }
}
