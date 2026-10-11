#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <cstdio>
#include <functional>
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
    explicit Fixture(uint32_t style)
    {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Fullscreen restore regression");
        params.x = 90; params.y = 110; params.width = 320; params.height = 240; params.style = style;
        window = jalium_window_create(&params);
        Require(window != nullptr, "create fixture");
        native = ((__bridge NSView*)(void*)jalium_window_get_native_handle(window)).window;
        native.animationBehavior = NSWindowAnimationBehaviorNone;
        jalium_apple_window_show(window, 0);
    }
    ~Fixture() { jalium_window_destroy(window); }
    NSRect Bounds()
    {
        int32_t x, y, width, height;
        Require(jalium_apple_window_get_restore_bounds(window, &x, &y, &width, &height) == 0,
            "query restore bounds");
        return NSMakeRect(x, y, width, height);
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
                    Require(NSEqualRects(f.Bounds(), expected), "expanded exit replaced restore bounds");
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                    Require(Pump([&] { return !f.native.miniaturized && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }), "return normal");
                    Require(NSEqualRects(f.Bounds(), expected), "normal surface lost saved geometry");
                    // A subsequent ordinary change must still capture its new geometry.
                    jalium_window_move(f.window, 165, 125); jalium_window_resize(f.window, 380, 280);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    Require(NSEqualRects(f.Bounds(), NSMakeRect(165, 125, 380, 280)), "later normal geometry ignored");
                    ++passed; std::printf("PASS %s fullscreen restore mode=%d\n", style == regular ? "Native" : "Custom", mode);
                } catch (const std::exception& error) {
                    std::fprintf(stderr, "FAIL %s fullscreen restore mode=%d: %s\n", style == regular ? "Native" : "Custom", mode, error.what());
                }
            }
        }
        jalium_platform_shutdown();
        std::printf("macOS fullscreen restore checks: %d/12 passed\n", passed);
        return passed == 12 ? 0 : 1;
    }
}
