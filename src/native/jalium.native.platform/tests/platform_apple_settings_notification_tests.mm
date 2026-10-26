#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#import <QuartzCore/QuartzCore.h>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <dlfcn.h>
#include <functional>
#include <memory>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>

// Observe real AppKit properties, so reentrancy runs inside the native setter.
@interface JaliumSettingsAppearanceObserver : NSObject {
@public
    std::function<void()> action;
}
@end
@implementation JaliumSettingsAppearanceObserver
- (void)observeValueForKeyPath:(NSString*)path ofObject:(id)object change:(NSDictionary*)change context:(void*)context
{
    auto callback = action;
    if (callback) callback();
}
@end

namespace {
void Require(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
}
struct Platform {
    bool active = false;
    void Start() { Require(jalium_platform_init() == JALIUM_OK, "platform init"); active = true; }
    void Stop() { if (active) { active = false; jalium_platform_shutdown(); } }
    ~Platform() { Stop(); }
};
struct Source {
    NSNotificationCenter* center;
    NSNotificationName name;
    NSObject* object;
    const char* label;
    void Post() const { [center postNotificationName:name object:object]; }
    void Worker(unsigned count = 1) const
    {
        std::string exception;
        std::thread thread([&] {
            @autoreleasepool {
                @try { for (unsigned i = 0; i < count; ++i) Post(); }
                @catch (NSException* error) { exception = error.reason.UTF8String ?: "NSException"; }
            }
        });
        thread.join();
        if (!exception.empty()) throw std::runtime_error(exception);
    }
};
struct Window {
    JaliumPlatformWindow* value = nullptr;
    NSView* view = nil;
    NSWindow* native = nil;
    std::atomic<unsigned> notifications{0};
    std::atomic<bool> offMain{false};
    std::function<void()> changed;
    explicit Window(uint32_t style)
    {
        JaliumWindowParams p{};
        p.title = reinterpret_cast<const JaliumUtf16Char*>(u"Owned settings notification validation");
        p.width = 360; p.height = 220; p.x = p.y = 90; p.style = style;
        value = jalium_window_create(&p);
        Require(value != nullptr, "create hidden native window");
        view = (__bridge NSView*)(void*)jalium_window_get_native_handle(value);
        native = view.window;
        Require(native != nil && !native.visible, "window remains hidden");
        jalium_window_set_event_callback(value, [](const JaliumPlatformEvent* event, void* context) {
            if (event->type != JALIUM_EVENT_SCROLLBAR_SETTINGS_CHANGED) return;
            auto& w = *static_cast<Window*>(context);
            ++w.notifications;
            if (!NSThread.isMainThread) w.offMain.store(true);
            auto changed = w.changed;
            if (changed) changed();
        }, this);
    }
    void Close()
    {
        if (!value) return;
        auto* old = value; value = nullptr;
        jalium_window_set_event_callback(old, nullptr, nullptr);
        jalium_window_destroy(old);
    }
    ~Window() { Close(); }
};
struct AppearanceObserver {
    NSWindow* native;
    NSString* key;
    JaliumSettingsAppearanceObserver* observer = [JaliumSettingsAppearanceObserver new];
    AppearanceObserver(NSWindow* window, NSString* property, std::function<void()> callback)
        : native(window), key(property)
    {
        observer->action = std::move(callback);
        [native addObserver:observer forKeyPath:key options:0 context:nullptr];
    }
    ~AppearanceObserver()
    {
        observer->action = {};
        [native removeObserver:observer forKeyPath:key];
    }
};
void Pump(double duration = .07)
{
    auto end = std::chrono::steady_clock::now() + std::chrono::duration<double>(duration);
    do {
        jalium_platform_poll_events();
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, .005, true);
    } while (std::chrono::steady_clock::now() < end);
}
int Run(bool threadOnly)
{
    Dl_info loaded{}; dladdr((void*)&jalium_platform_init, &loaded);
    std::printf("Loaded settings-test platform: %s\n", loaded.dli_fname ?: "<unknown>");
    Platform platform; platform.Start();
    [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
    if (!NSApp.running) [NSApp finishLaunching];
    constexpr uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE;
    const uint32_t custom = JALIUM_WINDOW_STYLE_BORDERLESS | JALIUM_WINDOW_STYLE_CLOSABLE;
    Source sources[] = {
        {NSNotificationCenter.defaultCenter, NSPreferredScrollerStyleDidChangeNotification, nil, "scroller"},
        {NSWorkspace.sharedWorkspace.notificationCenter, NSWorkspaceAccessibilityDisplayOptionsDidChangeNotification,
         NSWorkspace.sharedWorkspace, "workspace"}
    };
    int passed = 0, total = 0;
    for (uint32_t style : {regular, custom}) for (const auto& source : sources) {
        auto test = [&](const char* name, const std::function<void()>& body) {
            ++total;
            try { body(); ++passed; std::printf("PASS settings %s/%s: %s\n", style == regular ? "Native" : "Custom", source.label, name); }
            catch (const std::exception& error) {
                std::fprintf(stderr, "FAIL settings %s/%s: %s: %s\n", style == regular ? "Native" : "Custom", source.label, name, error.what());
            }
            Pump(.01);
        };
        test("main notification remains synchronous", [&] {
            Window w(style); NSRect before = w.native.frame;
            source.Post();
            Require(w.notifications == 1 && !w.offMain, "main post must synchronously notify once on main");
            Require(!w.native.visible && NSEqualRects(before, w.native.frame), "settings must preserve hidden geometry");
        });
        test("worker returns before main delivery", [&] {
            Window first(style), later(style);
            source.Worker();
            std::printf("Worker return %s/%s: first=%u later=%u offMain=%d\n", style == regular ? "Native" : "Custom",
                source.label, first.notifications.load(), later.notifications.load(), first.offMain || later.offMain);
            Require(first.notifications == 0 && later.notifications == 0, "worker called native window callbacks inline");
            Pump();
            Require(first.notifications == 1 && later.notifications == 1 && !first.offMain && !later.offMain,
                "worker notification must reach each window once on main");
        });
        if (threadOnly) continue;
        test("queued burst preserves each notification", [&] {
            Window w(style); source.Worker(3);
            Require(w.notifications == 0, "burst dispatched on the worker");
            Pump(); Require(w.notifications == 3 && !w.offMain, "queued notifications were lost or duplicated");
        });
        test("destroyed retained view retires queued work", [&] {
            Window w(style); source.Worker(); w.Close(); Pump();
            Require(w.notifications == 0, "queued work notified a closed window retained by its view");
            Require(w.view != nil && !w.native.visible, "test retains the retired view and native object");
        });
        test("replacement cannot inherit an old queued notification", [&] {
            Window old(style); source.Worker(); old.Close(); Window replacement(style); Pump();
            Require(old.notifications == 0 && replacement.notifications == 0, "queued work crossed window lifetimes");
            source.Post(); Require(replacement.notifications == 1, "replacement missed a fresh notification");
        });
        test("two background snapshots keep independent live targets", [&] {
            Window old(style), survivor(style); source.Worker(); old.Close(); Window replacement(style);
            source.Worker(); Pump();
            Require(old.notifications == 0 && survivor.notifications == 2 && replacement.notifications == 1,
                "queued work included a later window or lost the original survivor");
        });
        test("callback can destroy its current native window", [&] {
            Window closing(style), survivor(style); closing.changed = [&] { closing.Close(); };
            source.Worker(); Pump();
            Require(closing.value == nullptr && closing.notifications == 1 && survivor.notifications == 1,
                "self-close broke the callback or remaining target");
        });
        test("shutdown retires old queued view work", [&] {
            Window old(style); source.Worker(); old.Close(); platform.Stop(); platform.Start();
            Window replacement(style); Pump();
            Require(old.notifications == 0 && replacement.notifications == 0, "old work crossed platform lifetimes");
            source.Post(); Require(replacement.notifications == 1, "new lifecycle missed its fresh notification");
        });
        test("nested dispatcher pumping can drain main delivery", [&] {
            Window w(style);
            struct Probe { Source source; Window* window; JaliumDispatcher* dispatcher = nullptr; bool done = false; unsigned inside = 0; std::string failure;
                ~Probe() { if (dispatcher) jalium_dispatcher_destroy(dispatcher); } } probe{source, &w};
            Require(jalium_dispatcher_create(&probe.dispatcher) == JALIUM_OK, "create dispatcher");
            jalium_dispatcher_set_callback(probe.dispatcher, [](void* context) {
                auto& p = *static_cast<Probe*>(context);
                try { p.source.Worker(); Pump(); p.inside = p.window->notifications; }
                catch (const std::exception& error) { p.failure = error.what(); }
                p.done = true;
            }, &probe);
            jalium_dispatcher_wake(probe.dispatcher); Pump(.12);
            Require(probe.done && probe.failure.empty() && probe.inside == 1 && !w.offMain,
                "serial main-queue callback starved or mis-threaded native settings delivery");
        });
        test("disabled and popup physical windows still receive settings", [&] {
            Window owner(style), popup(JALIUM_WINDOW_STYLE_POPUP | JALIUM_WINDOW_STYLE_BORDERLESS);
            jalium_window_set_enabled(owner.value, 0);
            jalium_window_set_owner(popup.value, (intptr_t)(__bridge void*)owner.native);
            source.Worker(); Pump();
            Require(owner.notifications == 1 && popup.notifications == 1 && !owner.offMain && !popup.offMain,
                "disabled or popup settings delivery was lost");
            Require(!owner.native.canBecomeKeyWindow && !owner.native.visible && !popup.native.visible,
                "settings changed disabled state or visibility");
        });
        for (NSString* key : @[@"opaque", @"backgroundColor"]) {
            auto prepare = [](Window& w) {
                w.view.wantsLayer = YES;
                Require(w.view.layer != nil, "appearance control owns a real backing layer");
                w.native.opaque = NO;
                w.native.backgroundColor = NSColor.magentaColor;
            };
            std::string suffix = std::string(" through ") + key.UTF8String;
            test(("appearance observer can close during settings" + suffix).c_str(), [&] {
                Window w(style), survivor(style); prepare(w); bool entered = false;
                AppearanceObserver observation(w.native, key, [&] {
                    if (entered) return;
                    entered = true; w.Close();
                });
                source.Post();
                Require(entered && !w.value && w.notifications == 0 && survivor.notifications == 1,
                    "property close must retire the settings callback and preserve other windows");
            });
            test(("appearance observer can close during style" + suffix).c_str(), [&] {
                Window w(style); prepare(w); bool entered = false;
                AppearanceObserver observation(w.native, key, [&] {
                    if (entered) return;
                    entered = true; w.Close();
                });
                auto result = jalium_apple_window_set_style(w.value, style | JALIUM_WINDOW_STYLE_INPUT_TRANSPARENT);
                Require(result == JALIUM_OK && entered && !w.value && w.notifications == 0,
                    "style caller continued after its appearance observer closed the owner");
            });
            test(("newer transparent style survives settings" + suffix).c_str(), [&] {
                Window w(style); prepare(w); bool entered = false;
                AppearanceObserver observation(w.native, key, [&] {
                    if (entered) return;
                    entered = true;
                    Require(jalium_apple_window_set_style(w.value, style | JALIUM_WINDOW_STYLE_TRANSPARENT) == JALIUM_OK,
                        "apply nested transparent style");
                });
                source.Post();
                Require(entered && w.value && w.notifications == 1 && !w.native.opaque && !w.view.layer.opaque &&
                    [w.native.backgroundColor isEqual:NSColor.clearColor], "older appearance overwrote the newer style");
            });
            test(("newer transparent style survives style caller" + suffix).c_str(), [&] {
                Window w(style); prepare(w); bool entered = false;
                AppearanceObserver observation(w.native, key, [&] {
                    if (entered) return;
                    entered = true;
                    Require(jalium_apple_window_set_style(w.value, style | JALIUM_WINDOW_STYLE_TRANSPARENT) == JALIUM_OK,
                        "apply nested transparent style");
                });
                Require(jalium_apple_window_set_style(w.value, style | JALIUM_WINDOW_STYLE_INPUT_TRANSPARENT) == JALIUM_OK,
                    "apply outer style");
                Require(entered && !w.native.opaque && !w.view.layer.opaque && !w.native.ignoresMouseEvents &&
                    [w.native.backgroundColor isEqual:NSColor.clearColor], "style caller resumed a superseded appearance");
            });
        }
        test("nested settings do not cancel the outer style request", [&] {
            Window w(style); bool entered = false; w.native.opaque = NO;
            AppearanceObserver observation(w.native, @"opaque", [&] {
                if (entered) return;
                entered = true; source.Post();
            });
            Require(jalium_apple_window_set_style(w.value,
                style | JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_TOPMOST) == JALIUM_OK, "outer style update");
            Require(entered && w.notifications == 1 && (w.native.styleMask & NSWindowStyleMaskResizable) &&
                w.native.level == NSFloatingWindowLevel, "nested equal-style appearance canceled native chrome or level");
        });
        test("nested settings preserve one callback per notification", [&] {
            Window w(style); bool entered = false; w.native.opaque = NO;
            AppearanceObserver observation(w.native, @"opaque", [&] {
                if (entered) return;
                entered = true; source.Post();
            });
            source.Post();
            Require(entered && w.notifications == 2 && !w.offMain,
                "a superseded appearance lost or duplicated a live settings event");
        });
        test("surface appearance is written only when main drains", [&] {
            for (bool transparent : {false, true}) {
                Window w(style | (transparent ? JALIUM_WINDOW_STYLE_TRANSPARENT : 0));
                w.view.wantsLayer = YES;
                Require(w.view.layer != nil, "appearance control owns a real backing layer");
                w.native.opaque = transparent; w.view.layer.opaque = transparent;
                w.native.backgroundColor = NSColor.magentaColor;
                source.Worker();
                Require(w.notifications == 0 && w.native.opaque == transparent && w.view.layer.opaque == transparent &&
                    [w.native.backgroundColor isEqual:NSColor.magentaColor], "worker mutated AppKit surface appearance");
                Pump();
                Require(w.notifications == 1 && w.native.opaque == !transparent && w.view.layer.opaque == !transparent,
                    "main delivery did not restore the correct alpha contract");
                Require(![w.native.backgroundColor isEqual:NSColor.magentaColor], "main did not refresh the window background");
            }
        });
    }
    std::printf("macOS settings notification checks: %d/%d passed; own process, hidden windows, no input or system writes\n", passed, total);
    return passed == total ? 0 : 1;
}
}
int main(int argc, char** argv)
{
    @autoreleasepool {
        setvbuf(stdout, nullptr, _IONBF, 0);
        try { return Run(argc == 2 && std::strcmp(argv[1], "--thread-only") == 0); }
        catch (const std::exception& error) { std::fprintf(stderr, "FAIL settings setup: %s\n", error.what()); return 1; }
    }
}
