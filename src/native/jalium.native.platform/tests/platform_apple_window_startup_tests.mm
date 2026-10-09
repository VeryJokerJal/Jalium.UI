#include "jalium_platform.h"
#include "../src/platform_apple_window_startup_geometry.h"
#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <array>
#include <cmath>
#include <cstdio>
#include <functional>
#include <memory>
#include <stdexcept>
#include <thread>

static void Require(bool condition, const char* message)
{ if (!condition) throw std::runtime_error(message); }
static NSPoint Center(NSRect frame) { return NSMakePoint(NSMidX(frame), NSMidY(frame)); }
static void Near(NSPoint actual, NSPoint expected)
{
    Require(std::abs(actual.x - expected.x) < .01 && std::abs(actual.y - expected.y) < .01,
        "outer frame is not centered in the requested AppKit coordinate space");
}
// Inject a reentrant application callback only after this process's target
// frame setter. Screen discovery and all AppKit screen objects stay real.
static IMP g_originalFrame;
static NSWindow* g_frameTarget;
static std::function<void()> g_afterFrame;
static void SetFrame(id window, SEL selector, NSRect frame, BOOL display)
{
    reinterpret_cast<void(*)(id, SEL, NSRect, BOOL)>(g_originalFrame)(window, selector, frame, display);
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
struct Fixture {
    JaliumPlatformWindow* window;
    NSWindow* native;
    explicit Fixture(uint32_t style)
    {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Startup placement validation");
        params.x = 100; params.y = 100; params.width = 420; params.height = 300; params.style = style;
        window = jalium_window_create(&params); Require(window != nullptr, "window creation failed");
        native = ((__bridge NSView*)(void*)jalium_window_get_native_handle(window)).window;
    }
    void Destroy() { jalium_window_destroy(window); window = nullptr; }
    ~Fixture() { if (window) Destroy(); }
    intptr_t Handle() const { return (intptr_t)(__bridge void*)native; }
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
        for (uint32_t style : {regular, custom}) {
            auto run = [&](const char* name, const std::function<void(Fixture&)>& check) {
                ++total;
                try { Fixture fixture(style); check(fixture); ++passed; std::printf("PASS %s: %s\n", style == regular ? "Native" : "Custom", name); }
                catch (const std::exception& error) { std::fprintf(stderr, "FAIL %s: %s\n", name, error.what()); }
                g_afterFrame = {}; g_frameTarget = nil;
            };
            run("manual and ownerless placement preserve the frame", [](Fixture& f) {
                NSRect frame = f.native.frame;
                Require(jalium_apple_window_apply_startup_location(f.window, 0, 0) == JALIUM_OK, "manual rejected");
                Require(jalium_apple_window_apply_startup_location(f.window, 2, 0) == JALIUM_OK, "ownerless placement rejected");
                Require(NSEqualRects(frame, f.native.frame), "manual geometry changed");
            });
            for (uint32_t parentStyle : {regular, custom})
                run(parentStyle == regular ? "native owner outer frame" : "custom owner outer frame", [=](Fixture& f) {
                    Fixture parent(parentStyle); NSRect work = parent.native.screen.visibleFrame;
                    NSRect frame = parent.native.frame;
                    frame.origin = NSMakePoint(NSMidX(work)-frame.size.width/2,NSMidY(work)-frame.size.height/2);
                    [parent.native setFrame:frame display:NO];
                    Require(jalium_apple_window_apply_startup_location(f.window, 2, parent.Handle()) == JALIUM_OK, "owner center rejected");
                    Near(Center(f.native.frame), Center(parent.native.frame));
                });
            run("screen placement uses actual native work area", [](Fixture& f) {
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "screen center rejected");
                Near(Center(f.native.frame), Center(f.native.screen.visibleFrame));
            });
            run("maximized owner uses its screen work area", [=](Fixture& f) {
                Fixture parent(style); jalium_window_set_state(parent.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                Require(jalium_apple_window_apply_startup_location(f.window, 2, parent.Handle()) == JALIUM_OK, "maximized owner rejected");
                Near(Center(f.native.frame), Center(parent.native.screen.visibleFrame));
            });
            run("pre-show maximize preserves centered restore frame", [](Fixture& f) {
                MoveProbe moves(f);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                Require(jalium_apple_window_apply_startup_location(f.window, 1, 0) == JALIUM_OK, "maximized startup rejected");
                Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED, "startup changed maximized state");
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                Near(Center(f.native.frame), Center(f.native.screen.visibleFrame));
                moves.Matches(f);
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
