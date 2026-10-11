#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <cmath>
#include <cstdio>
#include <functional>
#include <memory>
#include <stdexcept>

static void Require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

static bool Pump(const std::function<bool()>& done)
{
    NSDate* deadline = [NSDate dateWithTimeIntervalSinceNow:3];
    while (!done() && deadline.timeIntervalSinceNow > 0) {
        jalium_platform_poll_events();
        [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.005]];
    }
    return done();
}

struct FullScreen;
static FullScreen* controlled = nullptr;
static IMP originalToggle = nullptr, originalMask = nullptr;
static void Toggle(id, SEL, id);
static NSWindowStyleMask Mask(id, SEL);

// The delegate sequence and this owned window's fullscreen flag are controlled.
// The expanded exit frame reproduces the measured CI frame; this is a state
// machine regression, separate from real Spaces/foreground acceptance.
struct FullScreen {
    NSWindow* window;
    Method toggle, mask;
    bool full = false;
    explicit FullScreen(NSWindow* native) : window(native)
    {
        Class cls = object_getClass(native);
        auto install = [&](SEL selector, IMP replacement, IMP& original) {
            Method inherited = class_getInstanceMethod(cls, selector);
            original = method_getImplementation(inherited);
            class_addMethod(cls, selector, original, method_getTypeEncoding(inherited));
            Method method = class_getInstanceMethod(cls, selector);
            method_setImplementation(method, replacement);
            return method;
        };
        toggle = install(@selector(toggleFullScreen:), reinterpret_cast<IMP>(Toggle), originalToggle);
        mask = install(@selector(styleMask), reinterpret_cast<IMP>(Mask), originalMask);
        controlled = this;
    }
    ~FullScreen()
    {
        method_setImplementation(toggle, originalToggle);
        method_setImplementation(mask, originalMask);
        controlled = nullptr;
    }
    void Will()
    {
        NSNotification* note = [NSNotification notificationWithName:full ?
            NSWindowWillExitFullScreenNotification : NSWindowWillEnterFullScreenNotification object:window];
        if (full) [window.delegate windowWillExitFullScreen:note];
        else [window.delegate windowWillEnterFullScreen:note];
    }
    void Complete()
    {
        full = !full;
        NSNotification* note = [NSNotification notificationWithName:full ?
            NSWindowDidEnterFullScreenNotification : NSWindowDidExitFullScreenNotification object:window];
        if (full) [window.delegate windowDidEnterFullScreen:note];
        else [window.delegate windowDidExitFullScreen:note];
    }
    void Fail()
    {
        if (full) [window.delegate windowDidFailToExitFullScreen:window];
        else [window.delegate windowDidFailToEnterFullScreen:window];
    }
};
static void Toggle(id object, SEL selector, id sender)
{
    if (controlled && object == controlled->window) controlled->Will();
    else reinterpret_cast<void (*)(id, SEL, id)>(originalToggle)(object, selector, sender);
}
static NSWindowStyleMask Mask(id object, SEL selector)
{
    auto result = reinterpret_cast<NSWindowStyleMask (*)(id, SEL)>(originalMask)(object, selector);
    return controlled && object == controlled->window && controlled->full ?
        result | NSWindowStyleMaskFullScreen : result;
}

struct Fixture {
    JaliumPlatformWindow* window;
    NSWindow* native;
    bool callback = false;
    bool zoom = false;
    explicit Fixture(uint32_t style, bool show = true)
    {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Fullscreen restore regression");
        params.x = 90; params.y = 110; params.width = 320; params.height = 240; params.style = style;
        window = jalium_window_create(&params);
        Require(window != nullptr, "create fixture");
        native = ((__bridge NSView*)(void*)jalium_window_get_native_handle(window)).window;
        native.animationBehavior = NSWindowAnimationBehaviorNone;
        if (show) jalium_apple_window_show(window, 0);
    }
    ~Fixture() { if (window) jalium_window_destroy(window); }
    NSRect Bounds()
    {
        int32_t x, y, width, height;
        Require(jalium_apple_window_get_restore_bounds(window, &x, &y, &width, &height) == 0,
            "query restore bounds");
        return NSMakeRect(x, y, width, height);
    }
    void RequireBounds(NSRect expected, const char* message)
    {
        NSRect actual = Bounds();
        if (!NSEqualRects(actual, expected)) {
            std::fprintf(stderr, "Fullscreen geometry: expected=%s actual=%s frame=%s client=%s "
                "state=%d mask=%lu mini=%d visible=%d active=%d work=%s\n",
                NSStringFromRect(expected).UTF8String, NSStringFromRect(actual).UTF8String,
                NSStringFromRect(native.frame).UTF8String,
                NSStringFromSize(((__bridge NSView*)(void*)jalium_window_get_native_handle(window)).bounds.size).UTF8String,
                jalium_window_get_state(window), (unsigned long)native.styleMask, native.miniaturized,
                native.visible, NSApp.active, NSStringFromRect(native.screen.visibleFrame).UTF8String);
        }
        Require(NSEqualRects(actual, expected), message);
    }
    static void OnNormal(const JaliumPlatformEvent* event, void* context)
    {
        auto& f = *static_cast<Fixture*>(context);
        if (event->type != JALIUM_EVENT_STATE_CHANGED ||
            event->stateChanged.newState != JALIUM_WINDOW_STATE_NORMAL || f.callback) return;
        f.callback = true;
        if (f.zoom) [f.native zoom:nil];
        else {
            jalium_window_move(f.window, 175, 135);
            jalium_window_resize(f.window, 420, 300);
            jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
        }
    }
};

struct RecoveryEvents {
    int count = 0;
    static void Record(const JaliumPlatformEvent* event, void* context)
    {
        if (event->type == JALIUM_EVENT_STATE_CHANGED)
            ++static_cast<RecoveryEvents*>(context)->count;
    }
};
static NSWindow* closeFrameTarget = nil;
static std::function<void()> closeAfterFrame;
static IMP originalFrame = nullptr;
static void ClosingFrame(id object, SEL selector, NSRect frame, BOOL display)
{
    reinterpret_cast<void (*)(id, SEL, NSRect, BOOL)>(originalFrame)(object, selector, frame, display);
    if (object == closeFrameTarget && closeAfterFrame) {
        auto call = std::move(closeAfterFrame); closeFrameTarget = nil; call();
    }
}
struct FrameClose {
    Method method;
    FrameClose(NSWindow* target, std::function<void()> action)
    {
        Class cls = object_getClass(target);
        SEL selector = @selector(setFrame:display:);
        Method inherited = class_getInstanceMethod(cls, selector);
        originalFrame = method_getImplementation(inherited);
        class_addMethod(cls, selector, originalFrame, method_getTypeEncoding(inherited));
        method = class_getInstanceMethod(cls, selector);
        method_setImplementation(method, reinterpret_cast<IMP>(ClosingFrame));
        closeFrameTarget = target; closeAfterFrame = std::move(action);
    }
    ~FrameClose()
    {
        method_setImplementation(method, originalFrame);
        closeAfterFrame = {}; closeFrameTarget = nil;
    }
};
static NSRect StyledNormal(NSWindow* target, NSSize client, NSPoint topLeft)
{
    NSWindow* control = [[NSWindow alloc] initWithContentRect:NSMakeRect(0, 0, client.width, client.height)
        styleMask:target.styleMask backing:NSBackingStoreBuffered defer:NO];
    control.releasedWhenClosed = NO;
    [control setContentSize:client]; [control setFrameTopLeftPoint:topLeft];
    NSRect frame = control.frame; [control close]; return frame;
}
static bool SameFrame(NSRect a, NSRect b)
{
    return std::abs(a.origin.x-b.origin.x) < .01 && std::abs(a.origin.y-b.origin.y) < .01 &&
        std::abs(a.size.width-b.size.width) < .01 && std::abs(a.size.height-b.size.height) < .01;
}
struct RecoveryAction {
    Fixture* fixture;
    bool titled, close;
    NSRect expectedBounds;
    int calls = 0;
    bool observedStyle = false, observedBounds = false;
    uint32_t replacementStyle;
    bool resizeFirst = false;
    NSRect expectedGeometry = NSZeroRect;
    std::unique_ptr<Fixture> replacement;
    NSRect replacementFrame = NSZeroRect;
    static void Invoke(const JaliumPlatformEvent* event, void* context)
    {
        auto& action = *static_cast<RecoveryAction*>(context);
        if (event->type != JALIUM_EVENT_STATE_CHANGED ||
            event->stateChanged.newState != JALIUM_WINDOW_STATE_NORMAL || action.calls) return;
        ++action.calls;
        auto& f = *action.fixture;
        action.observedStyle = ((f.native.styleMask & NSWindowStyleMaskTitled) != 0) == action.titled;
        int32_t x, y, width, height;
        action.observedBounds = jalium_apple_window_get_restore_bounds(f.window, &x, &y, &width, &height) == JALIUM_OK &&
            NSEqualRects(NSMakeRect(x, y, width, height), action.expectedBounds);
        if (action.close) {
            jalium_window_destroy(f.window); f.window = nullptr;
            action.replacement = std::make_unique<Fixture>(action.replacementStyle, false);
            action.replacementFrame = action.replacement->native.frame;
        } else {
            CGFloat scale = f.native.backingScaleFactor;
            NSWindow* control = [[NSWindow alloc] initWithContentRect:NSMakeRect(0, 0, 320 / scale, 240 / scale)
                styleMask:f.native.styleMask backing:NSBackingStoreBuffered defer:NO];
            control.releasedWhenClosed = NO;
            [control setFrame:f.native.frame display:NO];
            NSPoint topLeft = NSMakePoint(175 / scale, NSMaxY(NSScreen.screens.firstObject.frame) - 135 / scale);
            NSSize client = NSMakeSize(420 / scale, 300 / scale);
            if (action.resizeFirst) {
                [control setContentSize:client]; [control setFrameTopLeftPoint:topLeft];
                jalium_window_resize(f.window, 420, 300); jalium_window_move(f.window, 175, 135);
            } else {
                [control setFrameTopLeftPoint:topLeft]; [control setContentSize:client];
                jalium_window_move(f.window, 175, 135); jalium_window_resize(f.window, 420, 300);
            }
            action.expectedGeometry = control.frame; [control close];
            jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
        }
    }
};

int main()
{
    @autoreleasepool {
        if (jalium_platform_init() != JALIUM_OK) return 2;
        [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
        [NSApp finishLaunching];
        constexpr uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
            JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE;
        int passed = 0;
        for (uint32_t style : {regular, (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS}) {
            for (int mode = 0; mode < 6; ++mode) {
                try {
                    Fixture f(style);
                    NSRect expected = f.Bounds();
                    if (mode == 4) jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    FullScreen transition(f.native);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_FULLSCREEN);
                    transition.Complete();
                    Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_FULLSCREEN; }), "enter state");
                    if (mode == 2 || mode == 5) {
                        f.zoom = mode == 2;
                        jalium_window_set_event_callback(f.window, Fixture::OnNormal, &f);
                    }
                    JaliumWindowState target = mode == 0 ? JALIUM_WINDOW_STATE_MAXIMIZED :
                        mode == 1 ? JALIUM_WINDOW_STATE_MINIMIZED : JALIUM_WINDOW_STATE_NORMAL;
                    jalium_window_set_state(f.window, target);
                    [f.native setFrame:f.native.screen.visibleFrame display:NO];
                    transition.Complete();
                    if (mode == 2 || mode == 5) target = JALIUM_WINDOW_STATE_MAXIMIZED;
                    Require(Pump([&] { return jalium_window_get_state(f.window) == target; }), "drain exit state");
                    if (mode == 5) expected = NSMakeRect(175, 135, 420, 300);
                    f.RequireBounds(expected, "expanded exit replaced restore bounds");
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                    Require(Pump([&] { return !f.native.miniaturized && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }), "return normal");
                    f.RequireBounds(expected, "normal surface lost saved geometry");
                    // A subsequent ordinary change must still capture its new geometry.
                    jalium_window_move(f.window, 165, 125); jalium_window_resize(f.window, 380, 280);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    f.RequireBounds(NSMakeRect(165, 125, 380, 280), "later normal geometry ignored");
                    ++passed; std::printf("PASS %s fullscreen restore mode=%d\n", style == regular ? "Native" : "Custom", mode);
                } catch (const std::exception& error) {
                    std::fprintf(stderr, "FAIL %s fullscreen restore mode=%d: %s\n", style == regular ? "Native" : "Custom", mode, error.what());
                }
            }
            uint32_t custom = (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS;
            for (int mutation = 0; mutation < 3; ++mutation) for (bool maximized : {false, true}) {
                try {
                    Fixture f(style, false);
                    if (mutation == 2) Require(jalium_apple_window_set_titlebar_extended(f.window, 1) == JALIUM_OK,
                        "extended setup rejected");
                    NSSize client = ((__bridge NSView*)(void*)jalium_window_get_native_handle(f.window)).bounds.size;
                    NSPoint topLeft = NSMakePoint(NSMinX(f.native.frame), NSMaxY(f.native.frame));
                    if (maximized) jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    FullScreen transition(f.native);
                    RecoveryEvents events;
                    jalium_window_set_event_callback(f.window, RecoveryEvents::Record, &events);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_FULLSCREEN);
                    uint32_t desired = mutation == 0 ? (style == regular ? custom : regular) : style;
                    if (mutation == 0) Require(jalium_apple_window_set_style(f.window, desired) == JALIUM_OK,
                        "deferred style rejected");
                    else Require(jalium_apple_window_set_titlebar_extended(f.window, mutation == 1) == JALIUM_OK,
                        "deferred extension rejected");
                    transition.Fail(); transition.Fail();
                    Require(Pump([&] { return events.count != 0; }), "failed entry did not publish recovery");
                    Require(events.count == 1 && jalium_window_get_state(f.window) ==
                        (maximized ? JALIUM_WINDOW_STATE_MAXIMIZED : JALIUM_WINDOW_STATE_NORMAL),
                        "failed entry duplicated or changed recovered state");
                    bool titled = (desired & JALIUM_WINDOW_STYLE_TITLEBAR) != 0;
                    bool extended = titled && mutation == 1;
                    Require(((f.native.styleMask & NSWindowStyleMaskTitled) != 0) == titled &&
                        ((f.native.styleMask & NSWindowStyleMaskFullSizeContentView) != 0) == extended &&
                        (f.native.titleVisibility == NSWindowTitleHidden) == extended &&
                        f.native.titlebarAppearsTransparent == extended,
                        "failed fullscreen entry lost deferred title-bar style");
                    if (maximized) Require(SameFrame(f.native.frame, f.native.screen.visibleFrame),
                        "failed styled entry lost maximized work area");
                    NSRect expected = StyledNormal(f.native, client, topLeft);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                    Require(!f.native.visible && SameFrame(f.native.frame, expected) &&
                        NSEqualSizes(((__bridge NSView*)(void*)jalium_window_get_native_handle(f.window)).bounds.size, client),
                        "failed styled entry changed normal client geometry");
                    ++passed; std::printf("PASS %s failed-entry style=%d maximized=%d\n",
                        style == regular ? "Native" : "Custom", mutation, maximized);
                } catch (const std::exception& error) {
                    std::fprintf(stderr, "FAIL %s failed-entry style=%d maximized=%d: %s\n",
                        style == regular ? "Native" : "Custom", mutation, maximized, error.what());
                }
            }
            for (int mutation = 0; mutation < 3; ++mutation) {
                try {
                    Fixture f(style, false);
                    if (mutation == 2) Require(jalium_apple_window_set_titlebar_extended(f.window, 1) == JALIUM_OK,
                        "extended exit setup rejected");
                    NSSize client = ((__bridge NSView*)(void*)jalium_window_get_native_handle(f.window)).bounds.size;
                    NSPoint topLeft = NSMakePoint(NSMinX(f.native.frame), NSMaxY(f.native.frame));
                    FullScreen transition(f.native);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_FULLSCREEN); transition.Complete();
                    Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_FULLSCREEN; }),
                        "exit fixture did not enter");
                    uint32_t desired = mutation == 0 ? (style == regular ? custom : regular) : style;
                    if (mutation == 0) jalium_apple_window_set_style(f.window, desired);
                    else jalium_apple_window_set_titlebar_extended(f.window, mutation == 1);
                    NSWindowStyleMask deferredMask = f.native.styleMask;
                    RecoveryEvents events;
                    jalium_window_set_event_callback(f.window, RecoveryEvents::Record, &events);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL); transition.Fail(); transition.Fail();
                    Require(Pump([&] { return events.count != 0; }), "failed exit did not publish recovery");
                    Require(events.count == 1 && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_FULLSCREEN &&
                        f.native.styleMask == deferredMask, "failed exit applied style inside fullscreen");
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL); transition.Complete();
                    Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }),
                        "later styled exit did not finish");
                    bool titled = (desired & JALIUM_WINDOW_STYLE_TITLEBAR) != 0;
                    bool extended = titled && mutation == 1;
                    Require(((f.native.styleMask & NSWindowStyleMaskTitled) != 0) == titled &&
                        ((f.native.styleMask & NSWindowStyleMaskFullSizeContentView) != 0) == extended &&
                        !f.native.visible && SameFrame(f.native.frame, StyledNormal(f.native, client, topLeft)) &&
                        NSEqualSizes(((__bridge NSView*)(void*)jalium_window_get_native_handle(f.window)).bounds.size, client),
                        "later exit lost deferred style or normal geometry");
                    ++passed; std::printf("PASS %s failed-exit deferred style=%d\n", style == regular ? "Native" : "Custom", mutation);
                } catch (const std::exception& error) {
                    std::fprintf(stderr, "FAIL %s failed-exit deferred style=%d: %s\n", style == regular ? "Native" : "Custom",
                        mutation, error.what());
                }
            }
            for (int mode = 0; mode < 3; ++mode) {
                try {
                    bool close = mode == 2;
                    Fixture f(style, false);
                    FullScreen transition(f.native);
                    uint32_t desired = style == regular ? custom : regular;
                    RecoveryAction action{&f, (desired & JALIUM_WINDOW_STYLE_TITLEBAR) != 0, close, f.Bounds()};
                    action.replacementStyle = style;
                    action.resizeFirst = mode == 1;
                    jalium_window_set_event_callback(f.window, RecoveryAction::Invoke, &action);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_FULLSCREEN);
                    jalium_apple_window_set_style(f.window, desired);
                    transition.Fail();
                    Require(Pump([&] { return action.calls != 0; }), "recovered style callback did not run");
                    Require(action.observedStyle && action.observedBounds, "recovery callback observed stale style or normal bounds");
                    if (close) {
                        Require(!f.window && action.replacement && !action.replacement->native.visible &&
                            SameFrame(action.replacement->native.frame, action.replacementFrame),
                            "failed transition touched a replacement window");
                    } else {
                        Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED; }),
                            "recovery callback lost its newer state");
                        jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                        CGFloat scale = f.native.backingScaleFactor;
                        NSRect expected = action.expectedGeometry;
                        f.RequireBounds(NSMakeRect(std::lround(NSMinX(expected) * scale),
                            std::lround((NSMaxY(NSScreen.screens.firstObject.frame) - NSMaxY(expected)) * scale), 420, 300),
                            "recovery callback lost newer normal geometry");
                        Require(SameFrame(f.native.frame, expected) &&
                            NSEqualSizes(((__bridge NSView*)(void*)jalium_window_get_native_handle(f.window)).bounds.size,
                                NSMakeSize(420 / scale, 300 / scale)), "recovery callback diverged from SDK geometry");
                        Require(!f.native.visible, "recovery callback displayed its fixture");
                    }
                    ++passed; std::printf("PASS %s failed-entry callback mode=%d\n", style == regular ? "Native" : "Custom", mode);
                } catch (const std::exception& error) {
                    std::fprintf(stderr, "FAIL %s failed-entry callback mode=%d: %s\n", style == regular ? "Native" : "Custom",
                        mode, error.what());
                }
            }
            try {
                Fixture f(style, false);
                FullScreen transition(f.native);
                RecoveryEvents events;
                jalium_window_set_event_callback(f.window, RecoveryEvents::Record, &events);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_FULLSCREEN);
                jalium_apple_window_set_style(f.window, style == regular ? custom : regular);
                std::unique_ptr<Fixture> replacement; NSRect saved = NSZeroRect;
                FrameClose close(f.native, [&] {
                    jalium_window_destroy(f.window); f.window = nullptr;
                    replacement = std::make_unique<Fixture>(style, false); saved = replacement->native.frame;
                });
                transition.Fail();
                Require(Pump([&] { return replacement || events.count != 0; }), "frame-close recovery did not finish");
                Require(!f.window && replacement && events.count == 0 && !replacement->native.visible &&
                    SameFrame(replacement->native.frame, saved), "style frame callback did not stop retired recovery");
                ++passed; std::printf("PASS %s failed-entry style frame close\n", style == regular ? "Native" : "Custom");
            } catch (const std::exception& error) {
                std::fprintf(stderr, "FAIL %s failed-entry style frame close: %s\n", style == regular ? "Native" : "Custom",
                    error.what());
            }
        }
        jalium_platform_shutdown();
        std::printf("macOS fullscreen restore checks: %d/38 passed\n", passed);
        return passed == 38 ? 0 : 1;
    }
}
