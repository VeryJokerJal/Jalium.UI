#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include "jalium_platform.h"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstring>
#include <cstdio>
#include <functional>
#include <memory>
#include <stdexcept>

static void Require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

struct Fixture {
    JaliumPlatformWindow* window;
    NSWindow* native;
    explicit Fixture(uint32_t style)
    {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Window resize drag validation");
        params.x = 200; params.y = 220; params.width = 640; params.height = 480;
        params.style = style;
        window = jalium_window_create(&params);
        Require(window != nullptr, "window creation failed");
        native = ((__bridge NSView*)(void*)jalium_window_get_native_handle(window)).window;
        jalium_apple_window_show(window, 0);
        jalium_platform_poll_events();
    }
    void Destroy() { jalium_window_destroy(window); window = nullptr; }
    ~Fixture() { if (window) jalium_window_destroy(window); }
};

// Supply this process's owned press as currentEvent. The drag loop itself
// still reads real AppKit events and services the actual tracking run loop.
static NSEvent* g_press = nil;
static IMP g_originalCurrentEvent = nullptr;
static NSEvent* CurrentEvent(id object, SEL selector)
{
    return g_press ?: reinterpret_cast<NSEvent* (*)(id, SEL)>(g_originalCurrentEvent)(object, selector);
}

struct Press {
    Method method;
    explicit Press(NSWindow* window)
    {
        g_press = [NSEvent mouseEventWithType:NSEventTypeLeftMouseDown location:NSMakePoint(10, 20)
            modifierFlags:0 timestamp:0 windowNumber:window.windowNumber context:nil
            eventNumber:1 clickCount:1 pressure:1];
        Require(g_press.window == window, "owned press cannot resolve its actual NSWindow");
        method = class_getInstanceMethod(NSApplication.class, @selector(currentEvent));
        g_originalCurrentEvent = method_getImplementation(method);
        method_setImplementation(method, reinterpret_cast<IMP>(CurrentEvent));
    }
    ~Press() { method_setImplementation(method, g_originalCurrentEvent); g_press = nil; }
};

static NSTimer* Schedule(double seconds, const std::function<void()>& action)
{
    std::function<void()> callback = action;
    NSTimer* timer = [NSTimer timerWithTimeInterval:seconds repeats:NO block:^(NSTimer*) { callback(); }];
    [NSRunLoop.mainRunLoop addTimer:timer forMode:NSEventTrackingRunLoopMode];
    return timer;
}

static NSEvent* Mouse(NSWindow* window, NSEventType type, NSPoint location)
{
    return [NSEvent mouseEventWithType:type location:location modifierFlags:0 timestamp:0
        windowNumber:window.windowNumber context:nil eventNumber:2 clickCount:1 pressure:0];
}

static NSEvent* ScreenMouse(NSEventType type, NSPoint location)
{
    // Create an owned event with a fixed global location. Converting back
    // through a window that moved during tracking can use a stale transform.
    CGPoint quartz = CGPointMake(location.x, NSMaxY(NSScreen.screens.firstObject.frame) - location.y);
    CGEventRef core = CGEventCreateMouseEvent(nullptr,
        type == NSEventTypeLeftMouseUp ? kCGEventLeftMouseUp : kCGEventLeftMouseDragged,
        quartz, kCGMouseButtonLeft);
    Require(core != nullptr, "owned screen event creation failed");
    NSEvent* event = [NSEvent eventWithCGEvent:core];
    CFRelease(core);
    Require(event != nil && event.CGEvent != nullptr &&
        CGPointEqualToPoint(CGEventGetLocation(event.CGEvent), quartz), "owned event lost its fixed screen point");
    return event;
}

static void CheckCancellation(Fixture& fixture, uint32_t style, int change)
{
    Press press(fixture.native);
    bool changed = false, fallbackFired = false;
    std::chrono::steady_clock::time_point changedAt;
    double operationSeconds = 0;
    __block bool didMiniaturize = false;
    __block std::chrono::steady_clock::time_point miniaturizedAt;
    id miniObserver = nil;
    if (change == 4 || change == 6) {
        miniObserver = [NSNotificationCenter.defaultCenter addObserverForName:NSWindowDidMiniaturizeNotification
            object:fixture.native queue:nil usingBlock:^(NSNotification*) {
                didMiniaturize = true; miniaturizedAt = std::chrono::steady_clock::now();
            }];
    }
    std::unique_ptr<Fixture> replacement;
    NSRect replacementFrame = NSZeroRect;
    NSTimer* interrupt = Schedule(0.03, [&] {
        auto operationStarted = std::chrono::steady_clock::now();
        switch (change) {
            case 0: jalium_window_hide(fixture.window); break;
            case 1: jalium_window_set_enabled(fixture.window, 0); break;
            case 2: jalium_apple_window_set_style(fixture.window, style & ~JALIUM_WINDOW_STYLE_RESIZABLE); break;
            case 3: jalium_window_set_state(fixture.window, JALIUM_WINDOW_STATE_MAXIMIZED); break;
            case 4: jalium_window_set_state(fixture.window, JALIUM_WINDOW_STATE_MINIMIZED); break;
            case 5:
                fixture.Destroy(); replacement = std::make_unique<Fixture>(style);
                replacementFrame = replacement->native.frame; break;
            case 6: [fixture.native miniaturize:nil]; break;
        }
        changedAt = std::chrono::steady_clock::now();
        operationSeconds = std::chrono::duration<double>(changedAt - operationStarted).count();
        changed = true;
    });
    NSEvent* release = Mouse(fixture.native, NSEventTypeLeftMouseUp, NSMakePoint(10, 20));
    // A bounded fallback makes the old indefinite wait observable without
    // leaving a hung process or requiring a real mouse release.
    NSTimer* fallback = Schedule(1.0, [&] { fallbackFired = true; [NSApp postEvent:release atStart:NO]; });
    auto started = std::chrono::steady_clock::now();
    int result = jalium_window_begin_resize_drag(fixture.window, 10);
    auto finished = std::chrono::steady_clock::now();
    double elapsed = std::chrono::duration<double>(finished - started).count();
    auto unavailableAt = didMiniaturize ? std::max(changedAt, miniaturizedAt) : changedAt;
    double trackingTail = changed ? std::chrono::duration<double>(finished - unavailableAt).count() : elapsed;
    double transitionSeconds = didMiniaturize ? std::chrono::duration<double>(miniaturizedAt - changedAt).count() : 0;
    [interrupt invalidate]; [fallback invalidate];
    if (miniObserver) [NSNotificationCenter.defaultCenter removeObserver:miniObserver];
    std::printf("Cancellation diagnostic: change=%d changed=%d fallback=%d elapsed=%.3f operation=%.3f transition=%.3f trackingTail=%.3f\n",
        change, changed, fallbackFired, elapsed, operationSeconds, transitionSeconds, trackingTail);
    Require(result == JALIUM_OK && changed, "resize did not enter the real tracking loop");
    // AppKit's nextEvent call itself can service the entire miniaturization
    // animation. Exclude that observed native transition from the loop's wait,
    // while still requiring cancellation without our fallback mouse release.
    Require(!fallbackFired && trackingTail < 0.3, "resize still waited for a mouse release after its target became unavailable");
    if (replacement)
        Require(NSEqualRects(replacement->native.frame, replacementFrame), "retired resize changed the replacement window");
}

static void CheckGesture(Fixture& fixture, int edge, double delay, bool throughView = false)
{
    Press press(fixture.native);
    NSRect before = fixture.native.frame;
    bool dragged = false, released = false;
    NSTimer* drag = Schedule(delay, [&] {
        dragged = true;
        [NSApp postEvent:Mouse(fixture.native, NSEventTypeLeftMouseDragged, NSMakePoint(40, 0)) atStart:NO];
    });
    NSTimer* up = Schedule(delay + 0.03, [&] {
        released = true;
        [NSApp postEvent:Mouse(fixture.native, NSEventTypeLeftMouseUp, NSMakePoint(40, 0)) atStart:NO];
    });
    int result;
    if (throughView) {
        struct Context { JaliumPlatformWindow* window; int edge, result, presses; };
        Context context{fixture.window, edge, JALIUM_ERROR_INVALID_STATE, 0};
        jalium_window_set_event_callback(fixture.window, [](const JaliumPlatformEvent* event, void* data) {
            auto& context = *static_cast<Context*>(data);
            if (event->type == JALIUM_EVENT_MOUSE_DOWN) {
                ++context.presses;
                context.result = jalium_window_begin_resize_drag(context.window, context.edge);
            }
        }, &context);
        NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(fixture.window);
        [view mouseDown:g_press];
        jalium_window_set_event_callback(fixture.window, nullptr, nullptr);
        result = context.presses == 1 ? context.result : JALIUM_ERROR_INVALID_STATE;
    } else result = jalium_window_begin_resize_drag(fixture.window, edge);
    [drag invalidate]; [up invalidate];
    Require(result == JALIUM_OK && dragged && released, "idle tracking ended before the queued drag and release");
    NSRect expected = before;
    if (edge & 4) { expected.size.width -= 30; expected.origin.x += 30; }
    if (edge & 8) expected.size.width += 30;
    if (edge & 1) expected.size.height -= 20;
    if (edge & 2) { expected.size.height += 20; expected.origin.y -= 20; }
    Require(NSEqualRects(fixture.native.frame, expected), "resize edge changed the wrong frame or anchor");
}

static void CheckUpdatedConstraints(Fixture& fixture, int edge, bool minimum)
{
    Press press(fixture.native);
    NSRect before = fixture.native.frame;
    NSRect content = [fixture.native contentRectForFrameRect:before];
    double scale = jalium_window_get_dpi_scale(fixture.window);
    double boundWidth = content.size.width + (minimum ? -20 : 20);
    double boundHeight = content.size.height + (minimum ? -10 : 10);
    int changeResult = JALIUM_ERROR_INVALID_STATE;
    bool dragged = false, released = false;
    NSTimer* constraints = Schedule(0.03, [&] {
        int width = static_cast<int>(std::lround(boundWidth * scale));
        int height = static_cast<int>(std::lround(boundHeight * scale));
        changeResult = jalium_window_set_min_max_size(fixture.window,
            minimum ? width : 0, minimum ? height : 0,
            minimum ? 0 : width, minimum ? 0 : height);
    });
    NSPoint location = g_press.locationInWindow;
    double widthDelta = minimum ? -80 : 80;
    double heightDelta = minimum ? -60 : 60;
    if (edge & 4) location.x -= widthDelta;
    else if (edge & 8) location.x += widthDelta;
    if (edge & 1) location.y += heightDelta;
    else if (edge & 2) location.y -= heightDelta;
    NSTimer* drag = Schedule(0.06, [&] {
        dragged = true;
        [NSApp postEvent:Mouse(fixture.native, NSEventTypeLeftMouseDragged, location) atStart:NO];
    });
    NSTimer* up = Schedule(0.09, [&] {
        released = true;
        [NSApp postEvent:Mouse(fixture.native, NSEventTypeLeftMouseUp, location) atStart:NO];
    });
    int result = jalium_window_begin_resize_drag(fixture.window, edge);
    [constraints invalidate]; [drag invalidate]; [up invalidate];
    Require(result == JALIUM_OK && changeResult == JALIUM_OK && dragged && released,
        "constraint update did not run inside the real resize gesture");
    NSRect expected = before;
    if (edge & (4 | 8)) expected.size.width += minimum ? -20 : 20;
    if (edge & (1 | 2)) expected.size.height += minimum ? -10 : 10;
    if (edge & 4) expected.origin.x = NSMaxX(before) - expected.size.width;
    if (edge & 2) expected.origin.y = NSMaxY(before) - expected.size.height;
    NSRect actual = fixture.native.frame;
    if (!NSEqualRects(actual, expected))
        std::printf("Constraint diagnostic: edge=%d minimum=%d expected=%.1f,%.1f %.1fx%.1f actual=%.1f,%.1f %.1fx%.1f\n",
            edge, minimum, expected.origin.x, expected.origin.y, expected.size.width, expected.size.height,
            actual.origin.x, actual.origin.y, actual.size.width, actual.size.height);
    Require(NSEqualRects(actual, expected), "resize used stale constraints or changed the fixed anchor");
}

static void ApplyGeometryUpdate(Fixture& fixture, uint32_t style, int change)
{
    double scale = jalium_window_get_dpi_scale(fixture.window);
    if (change == 0 || change == 3) {
        int32_t width, height;
        jalium_window_get_client_size(fixture.window, &width, &height);
        jalium_window_resize(fixture.window, width + std::lround(40 * scale), height + std::lround(30 * scale));
    }
    if (change == 1 || change == 3) {
        int32_t x, y;
        jalium_window_get_position(fixture.window, &x, &y);
        jalium_window_move(fixture.window, x + std::lround(40 * scale), y + std::lround(25 * scale));
    }
    if (change == 2)
        jalium_apple_window_set_style(fixture.window,
            style ^ (JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_BORDERLESS));
}

static void CheckGeometryUpdate(Fixture& fixture, uint32_t style, int edge, int change, bool fromCallback,
    bool capturedBeforeUpdate = false)
{
    Press press(fixture.native);
    NSRect before = fixture.native.frame;
    NSPoint origin = [fixture.native convertPointToScreen:g_press.locationInWindow];
    bool changed = false, dragged = false, released = false;
    NSRect afterUpdate = NSZeroRect;
    auto update = [&] {
        ApplyGeometryUpdate(fixture, style, change);
        afterUpdate = fixture.native.frame;
        changed = true;
    };
    struct Context { bool triggered = false; std::function<void()> action; };
    Context context{false, update};
    NSTimer* mutation = nil;
    if (fromCallback) {
        jalium_window_set_event_callback(fixture.window, [](const JaliumPlatformEvent* event, void* data) {
            auto& context = *static_cast<Context*>(data);
            if (event->type == JALIUM_EVENT_RESIZE && event->resize.isUserInitiated && !context.triggered) {
                context.triggered = true; context.action();
            }
        }, &context);
    } else mutation = Schedule(0.03, update);
    auto post = [&](NSEventType type, NSPoint screen) {
        [NSApp postEvent:ScreenMouse(type, screen) atStart:NO];
    };
    NSPoint first = NSMakePoint(origin.x + 15, origin.y - 12);
    NSPoint final = fromCallback ? NSMakePoint(origin.x + 30, origin.y - 24) : first;
    NSEvent* captured = capturedBeforeUpdate ? ScreenMouse(NSEventTypeLeftMouseDragged, final) : nil;
    NSTimer* initial = nil;
    if (fromCallback) initial = Schedule(0.03, [=] { post(NSEventTypeLeftMouseDragged, first); });
    NSTimer* drag = Schedule(fromCallback ? 0.08 : 0.06, [&] {
        dragged = true;
        if (captured) [NSApp postEvent:captured atStart:NO];
        else post(NSEventTypeLeftMouseDragged, final);
    });
    NSTimer* up = Schedule(fromCallback ? 0.12 : 0.09, [&] {
        released = true; post(NSEventTypeLeftMouseUp, final);
    });
    int result = jalium_window_begin_resize_drag(fixture.window, edge);
    [mutation invalidate]; [initial invalidate]; [drag invalidate]; [up invalidate];
    jalium_window_set_event_callback(fixture.window, nullptr, nullptr);
    Require(result == JALIUM_OK && changed && dragged && released,
        "geometry update did not run during an ongoing gesture");
    Require(!NSEqualRects(afterUpdate, before), "programmatic geometry update had no effect");
    NSRect expected = afterUpdate;
    if (edge & 4) { expected.size.width -= 15; expected.origin.x += 15; }
    if (edge & 8) expected.size.width += 15;
    if (edge & 1) expected.size.height -= 12;
    if (edge & 2) { expected.size.height += 12; expected.origin.y -= 12; }
    NSRect actual = fixture.native.frame;
    if (!NSEqualRects(actual, expected))
        std::printf("Geometry diagnostic: edge=%d change=%d callback=%d expected=%.1f,%.1f %.1fx%.1f actual=%.1f,%.1f %.1fx%.1f\n",
            edge, change, fromCallback, expected.origin.x, expected.origin.y, expected.size.width, expected.size.height,
            actual.origin.x, actual.origin.y, actual.size.width, actual.size.height);
    Require(NSEqualRects(actual, expected), "next drag discarded the application's current geometry or fixed anchor");
}

int main(int argc, char** argv)
{
    @autoreleasepool {
        Require(jalium_platform_init() == JALIUM_OK, "platform initialization failed");
        [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory]; [NSApp finishLaunching];
        constexpr uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
            JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE;
        constexpr uint32_t custom = (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS;
        const char* cancellation[] = {"hidden", "disabled", "fixed", "maximized", "minimized", "closed and replaced", "native miniaturization"};
        bool geometryOnly = argc == 2 && std::strcmp(argv[1], "--geometry-only") == 0;
        int passed = 0, total = 0;
        for (uint32_t style : {regular, custom}) {
            auto run = [&](const char* name, const std::function<void(Fixture&)>& check) {
                ++total;
                try { Fixture fixture(style); check(fixture); ++passed; std::printf("PASS %s: %s\n", style == regular ? "Native" : "Custom", name); }
                catch (const std::exception& error) { std::fprintf(stderr, "FAIL %s: %s: %s\n", style == regular ? "Native" : "Custom", name, error.what()); }
            };
            if (!geometryOnly) {
                for (int change = 0; change < 7; ++change)
                    run(cancellation[change], [=](Fixture& f) { CheckCancellation(f, style, change); });
                for (int edge : {1, 2, 4, 5, 6, 8, 9, 10})
                    run("edge geometry and fixed anchor", [=](Fixture& f) { CheckGesture(f, edge, 0.03); });
                run("stationary held press remains in tracking", [](Fixture& f) { CheckGesture(f, 10, 0.12); });
                run("sequential native view presses remain usable", [](Fixture& f) {
                    CheckGesture(f, 10, 0.03, true); CheckGesture(f, 10, 0.03, true);
                });
                for (int edge : {1, 2, 4, 5, 6, 8, 9, 10}) {
                    run("updated minimum and fixed anchor", [=](Fixture& f) { CheckUpdatedConstraints(f, edge, true); });
                    run("updated maximum and fixed anchor", [=](Fixture& f) { CheckUpdatedConstraints(f, edge, false); });
                }
            }
            for (int change = 0; change < 4; ++change) {
                for (int edge : {1, 2, 4, 5, 6, 8, 9, 10})
                    run("programmatic geometry remains in the gesture", [=](Fixture& f) {
                        CheckGeometryUpdate(f, style, edge, change, false);
                    });
                run("resize callback geometry remains in the gesture", [=](Fixture& f) {
                    CheckGeometryUpdate(f, style, 10, change, true);
                });
                run("captured screen point survives application geometry", [=](Fixture& f) {
                    CheckGeometryUpdate(f, style, 10, change, false, true);
                });
            }
        }
        jalium_platform_shutdown();
        std::printf("macOS native resize drag checks: %d/%d passed\n", passed, total);
        return passed == total ? 0 : 1;
    }
}
