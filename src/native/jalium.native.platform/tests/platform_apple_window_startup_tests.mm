#include "jalium_platform.h"
#include "../src/platform_apple_window_startup_geometry.h"
#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <exception>
#include <functional>
#include <memory>
#include <stdexcept>
#include <thread>
#include <vector>

static void Require(bool condition, const char* message)
{ if (!condition) throw std::runtime_error(message); }
static NSPoint Center(NSRect frame) { return NSMakePoint(NSMidX(frame), NSMidY(frame)); }
static bool SameFrame(NSRect actual, NSRect expected)
{
    return std::abs(actual.origin.x - expected.origin.x) < .01 &&
        std::abs(actual.origin.y - expected.origin.y) < .01 &&
        std::abs(actual.size.width - expected.size.width) < .01 &&
        std::abs(actual.size.height - expected.size.height) < .01;
}
static uint32_t ScreenID(NSScreen* screen)
{ return [screen.deviceDescription[@"NSScreenNumber"] unsignedIntValue]; }
static void PrintRect(const char* name, NSRect frame)
{
    std::fprintf(stderr, "%s=(%.6f,%.6f,%.6f,%.6f) ", name,
        frame.origin.x, frame.origin.y, frame.size.width, frame.size.height);
}
struct ScreenSample { uint32_t id; NSRect frame, work; CGFloat scale; };
static std::vector<ScreenSample> SampleScreens()
{
    std::vector<ScreenSample> screens;
    for (NSScreen* screen in NSScreen.screens)
        screens.push_back({ScreenID(screen), screen.frame, screen.visibleFrame, screen.backingScaleFactor});
    return screens;
}
static void PrintScreens(const char* phase, const std::vector<ScreenSample>& screens)
{
    for (const auto& screen : screens) {
        std::fprintf(stderr, "%s screen=%u scale=%.6f ", phase, screen.id, screen.scale);
        PrintRect("frame", screen.frame); PrintRect("work", screen.work); std::fprintf(stderr, "\n");
    }
}
// Resolve the expected display before placement. A hidden/offscreen window's
// screen may be nil, and a screen's visibleFrame can change between calls.
static NSScreen* TestStartupScreen(NSWindow* owner = nil)
{
    if (owner.screen && !NSIsEmptyRect(owner.screen.visibleFrame)) return owner.screen;
    NSPoint point = owner ? Center(owner.frame) : NSEvent.mouseLocation;
    NSScreen* nearest = nil; double distance = INFINITY;
    for (NSScreen* screen in NSScreen.screens) {
        if (NSIsEmptyRect(screen.visibleFrame)) continue;
        NSRect frame = screen.frame;
        if (NSPointInRect(point, frame)) return screen;
        double dx = std::max({NSMinX(frame) - point.x, 0.0, point.x - NSMaxX(frame)});
        double dy = std::max({NSMinY(frame) - point.y, 0.0, point.y - NSMaxY(frame)});
        double candidate = std::hypot(dx, dy);
        if (candidate < distance) { nearest = screen; distance = candidate; }
    }
    Require(nearest != nil, "no real display is available for startup placement");
    return nearest;
}
struct FrameSample { NSRect requested, actual; uint32_t screen; };
static NSWindow* g_placementTarget;
static std::vector<FrameSample>* g_placementFrames;
// Inject a reentrant application callback only after this process's target
// frame setter. Screen discovery and all AppKit screen objects stay real.
static IMP g_originalFrame;
static NSWindow* g_frameTarget;
static std::function<void()> g_afterFrame;
static void SetFrame(id window, SEL selector, NSRect frame, BOOL display)
{
    reinterpret_cast<void(*)(id, SEL, NSRect, BOOL)>(g_originalFrame)(window, selector, frame, display);
    if (window == g_placementTarget && g_placementFrames)
        g_placementFrames->push_back({frame, ((NSWindow*)window).frame, ScreenID(((NSWindow*)window).screen)});
    if (window == g_frameTarget && g_afterFrame) {
        auto callback = std::move(g_afterFrame); g_frameTarget = nil;
        callback();
    }
}
struct FrameProbe {
    Method frame = class_getInstanceMethod(NSWindow.class, @selector(setFrame:display:));
    FrameProbe() { g_originalFrame = method_setImplementation(frame, reinterpret_cast<IMP>(SetFrame)); }
    ~FrameProbe() { method_setImplementation(frame, g_originalFrame); g_afterFrame = {}; g_frameTarget = nil; }
};
static NSWindow* g_orderTarget;
static IMP g_originalOrder;
static void OrderFront(id window, SEL selector, id sender)
{
    if (window != g_orderTarget)
        reinterpret_cast<void(*)(id, SEL, id)>(g_originalOrder)(window, selector, sender);
}
struct OrderProbe {
    Method order = class_getInstanceMethod(NSWindow.class, @selector(orderFront:));
    explicit OrderProbe(NSWindow* target)
    {
        Require(!g_orderTarget && !target.visible, "order probe requires a hidden exclusive target");
        g_orderTarget = target;
        g_originalOrder = method_setImplementation(order, reinterpret_cast<IMP>(OrderFront));
    }
    ~OrderProbe() { method_setImplementation(order, g_originalOrder); g_orderTarget = nil; }
};
struct Fixture {
    JaliumPlatformWindow* window;
    NSWindow* native;
    explicit Fixture(uint32_t style, int width = 420, int height = 300)
    {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Startup placement validation");
        params.x = 100; params.y = 100; params.width = width; params.height = height; params.style = style;
        window = jalium_window_create(&params); Require(window != nullptr, "window creation failed");
        native = ((__bridge NSView*)(void*)jalium_window_get_native_handle(window)).window;
    }
    void Destroy() { jalium_window_destroy(window); window = nullptr; }
    ~Fixture() { if (window) Destroy(); }
    intptr_t Handle() const { return (intptr_t)(__bridge void*)native; }
};
struct MiniProbe;
static MiniProbe* g_miniProbe;
static IMP g_originalMini, g_originalDemini, g_originalMiniState;
static void Miniaturize(id, SEL, id);
static void Deminiaturize(id, SEL, id);
static BOOL IsMiniaturized(id, SEL);
// Control this hidden SDK window's notification sequence, while retaining
// real NSView/NSWindow geometry, display selection and frame alignment.
struct MiniProbe {
    NSWindow* target;
    bool minimized = false;
    std::function<void()> afterWill;
    Method mini = class_getInstanceMethod(NSWindow.class, @selector(miniaturize:));
    Method demini = class_getInstanceMethod(NSWindow.class, @selector(deminiaturize:));
    Method state = class_getInstanceMethod(NSWindow.class, @selector(isMiniaturized));
    explicit MiniProbe(NSWindow* target) : target(target)
    {
        Require(g_miniProbe == nullptr && !target.visible, "mini probe requires a hidden exclusive target");
        g_miniProbe = this;
        g_originalMini = method_setImplementation(mini, reinterpret_cast<IMP>(Miniaturize));
        g_originalDemini = method_setImplementation(demini, reinterpret_cast<IMP>(Deminiaturize));
        g_originalMiniState = method_setImplementation(state, reinterpret_cast<IMP>(IsMiniaturized));
    }
    ~MiniProbe()
    {
        method_setImplementation(mini, g_originalMini);
        method_setImplementation(demini, g_originalDemini);
        method_setImplementation(state, g_originalMiniState);
        g_miniProbe = nullptr;
        if (g_frameTarget == target) { g_afterFrame = {}; g_frameTarget = nil; }
    }
};
static void Miniaturize(id object, SEL selector, id sender)
{
    if (!g_miniProbe || object != g_miniProbe->target) {
        reinterpret_cast<void(*)(id, SEL, id)>(g_originalMini)(object, selector, sender); return;
    }
    NSWindow* window = g_miniProbe->target;
    [window.delegate windowWillMiniaturize:[NSNotification notificationWithName:NSWindowWillMiniaturizeNotification object:window]];
    if (g_miniProbe->afterWill) { auto action = std::move(g_miniProbe->afterWill); action(); }
    g_miniProbe->minimized = true;
    [window.delegate windowDidMiniaturize:[NSNotification notificationWithName:NSWindowDidMiniaturizeNotification object:window]];
}
static void Deminiaturize(id object, SEL selector, id sender)
{
    if (!g_miniProbe || object != g_miniProbe->target) {
        reinterpret_cast<void(*)(id, SEL, id)>(g_originalDemini)(object, selector, sender); return;
    }
    g_miniProbe->minimized = false;
    NSWindow* window = g_miniProbe->target;
    [window.delegate windowDidDeminiaturize:[NSNotification notificationWithName:NSWindowDidDeminiaturizeNotification object:window]];
}
static BOOL IsMiniaturized(id object, SEL selector)
{
    return g_miniProbe && object == g_miniProbe->target ? g_miniProbe->minimized :
        reinterpret_cast<BOOL(*)(id, SEL)>(g_originalMiniState)(object, selector);
}
struct PlacementCheck {
    NSWindow* target;
    NSRect before, work, reference, requested, expected;
    NSPoint mouse;
    uint32_t selectedScreen, initialScreen;
    std::vector<ScreenSample> screens;
    std::vector<FrameSample> frames;
    PlacementCheck(NSWindow* target, NSRect normalFrame, NSScreen* screen, NSRect reference)
        : target(target), before(target.frame), work(screen.visibleFrame), reference(reference),
          mouse(NSEvent.mouseLocation), selectedScreen(ScreenID(screen)), initialScreen(ScreenID(target.screen)),
          screens(SampleScreens())
    {
        Require(!NSIsEmptyRect(work), "startup work area is empty");
        requested = normalFrame;
        requested.origin.x = normalFrame.size.width > work.size.width ? NSMinX(work) :
            std::clamp(NSMidX(reference) - normalFrame.size.width / 2, NSMinX(work), NSMaxX(work) - normalFrame.size.width);
        requested.origin.y = normalFrame.size.height > work.size.height ? NSMaxY(work) - normalFrame.size.height :
            std::clamp(NSMidY(reference) - normalFrame.size.height / 2, NSMinY(work), NSMaxY(work) - normalFrame.size.height);
        // Use an ordinary AppKit window as the pixel-alignment oracle. This
        // keeps the 0.01-point comparison while allowing only AppKit's actual
        // rounding, rather than permitting arbitrary one-point offsets.
        NSWindow* control = [[NSWindow alloc] initWithContentRect:
            [NSWindow contentRectForFrameRect:requested styleMask:target.styleMask]
            styleMask:target.styleMask backing:NSBackingStoreBuffered defer:NO];
        Require(control != nil, "AppKit alignment control could not be created");
        control.releasedWhenClosed = NO;
        [control setFrame:requested display:NO]; expected = control.frame; [control close];
        Require(g_placementTarget == nil, "nested startup frame observation");
        g_placementTarget = target; g_placementFrames = &frames;
    }
    void Verify() const
    { Require(SameFrame(target.frame, expected), "outer frame differs from AppKit-aligned startup placement"); }
    ~PlacementCheck()
    {
        g_placementTarget = nil; g_placementFrames = nullptr;
        if (!std::uncaught_exceptions()) return;
        NSPoint currentMouse = NSEvent.mouseLocation;
        std::fprintf(stderr, "PLACEMENT selected-screen=%u initial-screen=%u final-screen=%u mouse-before=(%.6f,%.6f) mouse-after=(%.6f,%.6f)\n",
            selectedScreen, initialScreen, ScreenID(target.screen), mouse.x, mouse.y, currentMouse.x, currentMouse.y);
        PrintRect("before", before); PrintRect("reference", reference); PrintRect("work", work); std::fprintf(stderr, "\n");
        PrintRect("requested", requested); PrintRect("expected-AppKit", expected); PrintRect("actual", target.frame); std::fprintf(stderr, "\n");
        for (const auto& frame : frames) {
            std::fprintf(stderr, "SETFRAME screen=%u ", frame.screen);
            PrintRect("requested", frame.requested); PrintRect("actual", frame.actual); std::fprintf(stderr, "\n");
        }
        PrintScreens("before", screens); PrintScreens("after", SampleScreens());
    }
};
struct MoveProbe {
    Fixture& fixture;
    int count = 0;
    int x = 0, y = 0;
    std::function<void()> afterMove;
    explicit MoveProbe(Fixture& fixture) : fixture(fixture) {
        jalium_window_set_event_callback(fixture.window, [](const JaliumPlatformEvent* event, void* data) {
            auto& probe = *static_cast<MoveProbe*>(data);
            if (event->type != JALIUM_EVENT_MOVE) return;
            ++probe.count; probe.x = event->move.x; probe.y = event->move.y;
            if (probe.afterMove) { auto action = std::move(probe.afterMove); action(); }
        }, this);
    }
    ~MoveProbe() { if (fixture.window) jalium_window_set_event_callback(fixture.window, nullptr, nullptr); }
    void Matches(Fixture& fixture) {
        int x, y; jalium_window_get_position(fixture.window, &x, &y);
        Require(count > 0 && this->x == x && this->y == y, "move notification does not match real geometry");
    }
};
struct CallbackReset {
    Fixture& fixture;
    ~CallbackReset() { if (fixture.window) jalium_window_set_event_callback(fixture.window, nullptr, nullptr); }
};
static void PumpUntil(const std::function<bool()>& done)
{
    auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
    while (!done() && std::chrono::steady_clock::now() < deadline)
        [[NSRunLoop mainRunLoop] runUntilDate:[NSDate dateWithTimeIntervalSinceNow:.01]];
    Require(done(), "queued startup placement did not finish within three seconds");
}
int main()
{
    @autoreleasepool {
        int passed = 0, total = 0;
        auto geometry = [&](const char* name, const std::function<void()>& check) {
            ++total;
            try { check(); ++passed; std::printf("PASS geometry: %s\n", name); }
            catch (const std::exception& error) { std::fprintf(stderr, "FAIL %s: %s\n", name, error.what()); }
        };
        // A Retina primary, a scale-1 right/left display, and a Retina display
        // above it. Frames are AppKit points, regardless of each backing scale.
        std::array<NSRect, 4> screens = {NSMakeRect(0,0,1440,900), NSMakeRect(1440,0,2560,1440),
            NSMakeRect(-1920,-160,1920,1080), NSMakeRect(0,900,1920,1080)};
        for (size_t index = 0; index < screens.size(); ++index)
            geometry("primary right left and above point selection", [&] {
                Require(jalium::platform::apple::NearestStartupScreen(Center(screens[index]), screens) == index,
                    "wrong monitor in point-based desktop layout");
            });
        for (auto pair : {std::pair{NSMakePoint(9000,300), size_t(1)}, std::pair{NSMakePoint(-3000,100), size_t(2)},
             std::pair{NSMakePoint(800,2500), size_t(3)}, std::pair{NSMakePoint(2000,1600), size_t(3)}})
            geometry("nearest monitor outside or between screens", [&] {
                Require(jalium::platform::apple::NearestStartupScreen(pair.first, screens) == pair.second,
                    "nearest monitor selection used primary fallback");
            });
        geometry("missing and nonfinite geometry is rejected", [&] {
            Require(jalium::platform::apple::NearestStartupScreen(NSMakePoint(NAN,0), screens) == screens.size(), "NaN point accepted");
            std::array<NSRect,2> empty = {NSZeroRect, NSMakeRect(INFINITY,0,10,10)};
            Require(jalium::platform::apple::NearestStartupScreen(NSZeroPoint, empty) == empty.size(), "invalid screen accepted");
        });
        for (int decoration : {0,32})
            geometry("client decoration remains in outer frame centering", [&] {
                NSRect actual = jalium::platform::apple::CenterStartupFrame(NSMakeRect(0,0,420,300+decoration),
                    NSMakeRect(300,280,680,560), NSMakeRect(0,40,1440,830));
                Require(NSEqualRects(actual, NSMakeRect(430,410-decoration/2,420,300+decoration)), "outer decoration omitted");
            });
        for (NSRect owner : {NSMakeRect(-600,300,680,560), NSMakeRect(1380,300,680,560),
             NSMakeRect(600,-600,680,560), NSMakeRect(600,840,680,560)})
            geometry("owner clipping at each work-area edge", [&] {
                NSRect work = NSMakeRect(0,40,1440,830);
                Require(NSContainsRect(work, jalium::platform::apple::CenterStartupFrame(NSMakeRect(0,0,420,332),owner,work)),
                    "centered child is outside work area");
            });
        geometry("oversized frame retains accessible top-left chrome", [] {
            NSRect actual = jalium::platform::apple::CenterStartupFrame(NSMakeRect(0,0,1800,1200),
                NSMakeRect(0,40,1440,830), NSMakeRect(0,40,1440,830));
            Require(actual.origin.x == 0 && NSMaxY(actual) == 870, "oversized frame loses its top-left edge");
        });

        Require(jalium_platform_init() == JALIUM_OK, "platform initialization failed");
        NSApp.activationPolicy = NSApplicationActivationPolicyAccessory; [NSApp finishLaunching];
        FrameProbe probe;
        constexpr uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
            JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE;
        constexpr uint32_t custom = (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS;
        for (uint32_t style : {regular, custom}) for (auto [width, height] :
             {std::pair{420, 300}, std::pair{421, 301}, std::pair{420, 301}, std::pair{421, 300}}) {
            const bool baselineSize = width == 420 && height == 300;
            auto run = [&](const char* name, const std::function<void(Fixture&)>& check) {
                ++total;
                try { Fixture fixture(style, width, height); check(fixture); ++passed; std::printf("PASS %s %dx%d: %s\n", style == regular ? "Native" : "Custom", width, height, name); }
                catch (const std::exception& error) { std::fprintf(stderr, "FAIL %s %dx%d %s: %s\n", style == regular ? "Native" : "Custom", width, height, name, error.what()); }
                g_afterFrame = {}; g_frameTarget = nil;
            };
            if (baselineSize) run("manual and ownerless placement preserve the frame", [](Fixture& f) {
                NSRect frame = f.native.frame;
                Require(jalium_apple_window_apply_startup_location(f.window, 0, 0) == JALIUM_OK, "manual rejected");
                Require(jalium_apple_window_apply_startup_location(f.window, 2, 0) == JALIUM_OK, "ownerless placement rejected");
                Require(NSEqualRects(frame, f.native.frame), "manual geometry changed");
            });
            for (uint32_t parentStyle : {regular, custom})
                run(parentStyle == regular ? "native owner outer frame" : "custom owner outer frame", [=](Fixture& f) {
                    Fixture parent(parentStyle); NSScreen* screen = TestStartupScreen(parent.native);
                    NSRect work = screen.visibleFrame;
                    NSRect frame = parent.native.frame;
                    frame.origin = NSMakePoint(NSMidX(work)-frame.size.width/2,NSMidY(work)-frame.size.height/2);
                    [parent.native setFrame:frame display:NO];
                    PlacementCheck placement(f.native, f.native.frame, screen, parent.native.frame);
                    Require(jalium_apple_window_apply_startup_location(f.window, 2, parent.Handle()) == JALIUM_OK, "owner center rejected");
                    placement.Verify();
                });
            run("screen placement uses actual native work area", [](Fixture& f) {
                NSScreen* screen = TestStartupScreen();
                PlacementCheck placement(f.native, f.native.frame, screen, screen.visibleFrame);
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "screen center rejected");
                placement.Verify();
            });
            run("maximized owner uses its screen work area", [=](Fixture& f) {
                Fixture parent(style); jalium_window_set_state(parent.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                Require(jalium_window_get_state(parent.window) == JALIUM_WINDOW_STATE_MAXIMIZED, "owner did not maximize");
                NSScreen* screen = TestStartupScreen(parent.native);
                PlacementCheck placement(f.native, f.native.frame, screen, screen.visibleFrame);
                Require(jalium_apple_window_apply_startup_location(f.window, 2, parent.Handle()) == JALIUM_OK, "maximized owner rejected");
                placement.Verify();
            });
            run("pre-show maximize preserves centered restore frame", [](Fixture& f) {
                NSScreen* screen = TestStartupScreen();
                PlacementCheck placement(f.native, f.native.frame, screen, screen.visibleFrame);
                MoveProbe moves(f);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "maximized startup rejected");
                Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED, "startup changed maximized state");
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                placement.Verify();
                moves.Matches(f);
            });
            run("minimized placement survives native normal restore", [](Fixture& f) {
                NSScreen* screen = TestStartupScreen();
                PlacementCheck placement(f.native, f.native.frame, screen, screen.visibleFrame);
                MiniProbe mini(f.native);
                [f.native miniaturize:nil];
                Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MINIMIZED,
                    "controlled SDK minimization did not finish");
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK,
                    "minimized startup placement rejected");
                [f.native deminiaturize:nil];
                Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL && !f.native.visible,
                    "restore must finish without displaying the fixture");
                placement.Verify();
            });
            run("miniaturizing placement waits for normal restore", [](Fixture& f) {
                NSScreen* screen = TestStartupScreen();
                NSRect before = f.native.frame;
                PlacementCheck placement(f.native, before, screen, screen.visibleFrame);
                MiniProbe mini(f.native);
                bool deferred = false;
                mini.afterWill = [&] {
                    Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK,
                        "in-flight startup placement rejected");
                    deferred = NSEqualRects(f.native.frame, before);
                };
                [f.native miniaturize:nil];
                Require(deferred, "startup placement changed native geometry during minimization");
                [f.native deminiaturize:nil];
                Require(!f.native.visible, "controlled restore displayed its fixture");
                placement.Verify();
            });
            if (!baselineSize) continue;
            run("deferred frame callback retains newer owner placement before maximize", [=](Fixture& f) {
                Fixture owner(style);
                NSRect ownerFrame = owner.native.frame;
                ownerFrame.origin.x += 180; ownerFrame.origin.y -= 120;
                [owner.native setFrame:ownerFrame display:NO];
                PlacementCheck placement(f.native, f.native.frame, TestStartupScreen(owner.native), owner.native.frame);
                MiniProbe mini(f.native); [f.native miniaturize:nil];
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "initial minimized placement rejected");
                bool called = false;
                g_frameTarget = f.native;
                g_afterFrame = [&] {
                    called = true;
                    Require(jalium_apple_window_apply_startup_location(f.window, 2, owner.Handle()) == JALIUM_OK,
                        "newer owner placement rejected");
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                };
                [f.native deminiaturize:nil];
                PumpUntil([&] { return called && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED; });
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                placement.Verify();
                Require(!f.native.visible, "deferred state request displayed its fixture");
            });
            run("deferred frame callback can close and replace its target", [=](Fixture& f) {
                std::unique_ptr<Fixture> replacement; NSRect replacementFrame;
                MiniProbe mini(f.native); [f.native miniaturize:nil];
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "minimized placement rejected");
                g_frameTarget = f.native;
                g_afterFrame = [&] {
                    Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "queued replacement precursor rejected");
                    f.Destroy(); replacement = std::make_unique<Fixture>(style); replacementFrame = replacement->native.frame;
                };
                [f.native deminiaturize:nil];
                Require(replacement != nullptr, "deferred frame callback did not execute");
                [[NSRunLoop mainRunLoop] runUntilDate:[NSDate dateWithTimeIntervalSinceNow:.03]];
                Require(NSEqualRects(replacement->native.frame, replacementFrame) && !replacement->native.visible,
                    "retired placement modified its replacement");
            });
            for (int sequence : {0, 1, 2, 3})
                run(sequence == 0 ? "newer move then resize survives minimized placement" :
                    sequence == 1 ? "newer resize then move survives minimized placement" :
                    sequence == 2 ? "newer resize retains minimized centered position" :
                    "newer move retains minimized normal size", [=](Fixture& f) {
                    NSScreen* screen = TestStartupScreen();
                    PlacementCheck placement(f.native, f.native.frame, screen, screen.visibleFrame);
                    NSWindow* control = [[NSWindow alloc] initWithContentRect:
                        [NSWindow contentRectForFrameRect:placement.expected styleMask:f.native.styleMask]
                        styleMask:f.native.styleMask backing:NSBackingStoreBuffered defer:NO];
                    control.releasedWhenClosed = NO;
                    [control setFrame:placement.expected display:NO];
                    MiniProbe mini(f.native); [f.native miniaturize:nil];
                    Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "minimized placement rejected");
                    CGFloat scale = f.native.backingScaleFactor;
                    const int x = 210, y = 190, width = 487, height = 347;
                    NSPoint topLeft = NSMakePoint(x / scale, NSMaxY(NSScreen.screens.firstObject.frame) - y / scale);
                    auto move = [&] { [control setFrameTopLeftPoint:topLeft]; jalium_window_move(f.window, x, y); };
                    auto resize = [&] { [control setContentSize:NSMakeSize(width / scale, height / scale)]; jalium_window_resize(f.window, width, height); };
                    if (sequence == 0) { move(); resize(); }
                    else if (sequence == 1) { resize(); move(); }
                    else if (sequence == 2) resize();
                    else move();
                    NSRect expected = control.frame; [control close];
                    [f.native deminiaturize:nil];
                    if (!SameFrame(f.native.frame, expected)) {
                        PrintRect("expected-new-geometry-AppKit", expected); PrintRect("actual-new-geometry", f.native.frame);
                        std::fprintf(stderr, "\n");
                    }
                    Require(SameFrame(f.native.frame, expected), "deferred startup placement overwrote newer explicit geometry");
                    Require(!f.native.visible, "merged geometry displayed its fixture");
                });
            run("normal notification retains a newer explicit move", [](Fixture& f) {
                MiniProbe mini(f.native); [f.native miniaturize:nil];
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "minimized placement rejected");
                bool moved = false;
                std::pair<Fixture*, bool*> context{&f, &moved};
                CallbackReset reset{f};
                jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* data) {
                    auto& context = *static_cast<std::pair<Fixture*, bool*>*>(data);
                    if (event->type == JALIUM_EVENT_STATE_CHANGED && event->stateChanged.newState == JALIUM_WINDOW_STATE_NORMAL) {
                        *context.second = true; jalium_window_move(context.first->window, 230, 170);
                    }
                }, &context);
                [f.native deminiaturize:nil];
                [[NSRunLoop mainRunLoop] runUntilDate:[NSDate dateWithTimeIntervalSinceNow:.03]];
                int x, y; jalium_window_get_position(f.window, &x, &y);
                Require(moved && x == 230 && y == 170 && !f.native.visible, "normal callback move was overwritten");
            });
            run("minimized maximized placement keeps the normal restore frame", [](Fixture& f) {
                NSScreen* screen = TestStartupScreen();
                PlacementCheck placement(f.native, f.native.frame, screen, screen.visibleFrame);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                MiniProbe mini(f.native); [f.native miniaturize:nil];
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "minimized maximized placement rejected");
                [f.native deminiaturize:nil];
                Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED, "restore lost prior maximized state");
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                placement.Verify();
                Require(!f.native.visible, "maximized restore displayed its fixture");
            });
            run("newer external normal resize retires queued maximized placement", [](Fixture& f) {
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "maximized placement rejected");
                [f.native setContentSize:NSMakeSize(487, 347)];
                Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL, "external resize did not restore normal state");
                NSRect latest = f.native.frame;
                OrderProbe order(f.native);
                jalium_apple_window_show(f.window, 0);
                Require(!f.native.visible && SameFrame(f.native.frame, latest), "Show reapplied retired startup geometry");
                jalium_window_hide(f.window);
            });
            run("alignment oracle rejects a one-point placement error", [](Fixture& f) {
                NSScreen* screen = TestStartupScreen();
                PlacementCheck placement(f.native, f.native.frame, screen, screen.visibleFrame);
                g_frameTarget = f.native;
                g_afterFrame = [&] {
                    NSRect displaced = f.native.frame; displaced.origin.x += 1;
                    [f.native setFrame:displaced display:NO];
                };
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "displacement fixture failed");
                Require(!SameFrame(f.native.frame, placement.expected), "alignment oracle accepted an incorrect one-point displacement");
            });
            run("startup move callback can close and replace its target", [=](Fixture& f) {
                MoveProbe moves(f); std::unique_ptr<Fixture> replacement; NSRect saved;
                moves.afterMove = [&] { f.Destroy(); replacement = std::make_unique<Fixture>(style); saved = replacement->native.frame; };
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_ERROR_INVALID_STATE,
                    "startup move callback continued after closing target");
                Require(replacement && NSEqualRects(replacement->native.frame, saved), "startup modified replacement");
            });
            run("restore move callback queues a newer state", [](Fixture& f) {
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                MoveProbe moves(f);
                moves.afterMove = [&] { jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED); };
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                Require(moves.count > 0 && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED,
                    "restore move callback state request was lost");
                moves.Matches(f);
            });
            run("restore move callback can close and replace the target", [=](Fixture& f) {
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                MoveProbe moves(f); std::unique_ptr<Fixture> replacement; NSRect saved;
                moves.afterMove = [&] { f.Destroy(); replacement = std::make_unique<Fixture>(style); saved = replacement->native.frame; };
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                Require(replacement && NSEqualRects(replacement->native.frame, saved),
                    "restore move callback did not preserve replacement geometry");
            });
            run("invalid arguments preserve geometry", [](Fixture& f) {
                NSRect frame = f.native.frame;
                Require(jalium_apple_window_apply_startup_location(nullptr, 1, 0) == JALIUM_ERROR_INVALID_ARGUMENT, "null accepted");
                Require(jalium_apple_window_apply_startup_location(f.window, 3, 0) == JALIUM_ERROR_INVALID_ARGUMENT, "invalid location accepted");
                Require(jalium_apple_window_apply_startup_location(f.window, 2, f.Handle()) == JALIUM_ERROR_INVALID_ARGUMENT, "self owner accepted");
                Require(NSEqualRects(frame, f.native.frame), "invalid request changed frame");
            });
            run("closed owner and worker requests are rejected", [=](Fixture& f) {
                Fixture parent(style); parent.Destroy(); NSRect frame = f.native.frame;
                Require(jalium_apple_window_apply_startup_location(f.window, 2, parent.Handle()) == JALIUM_ERROR_INVALID_STATE, "closed owner accepted");
                int result = JALIUM_OK;
                std::thread worker([&] { result = jalium_apple_window_apply_startup_location(f.window, 1, 0); }); worker.join();
                Require(result == JALIUM_ERROR_INVALID_STATE && NSEqualRects(frame, f.native.frame), "worker accessed AppKit placement");
            });
            run("frame callback can close and replace the target", [=](Fixture& f) {
                std::unique_ptr<Fixture> replacement; NSRect replacementFrame;
                g_frameTarget = f.native;
                g_afterFrame = [&] { f.Destroy(); replacement = std::make_unique<Fixture>(style); replacementFrame = replacement->native.frame; };
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_ERROR_INVALID_STATE,
                    "closed target continued placement");
                Require(replacement && NSEqualRects(replacementFrame, replacement->native.frame), "replacement geometry changed");
            });
        }
        std::printf("macOS native Window startup checks: %d/%d passed\n", passed, total);
        return passed == total ? 0 : 1;
    }
}
