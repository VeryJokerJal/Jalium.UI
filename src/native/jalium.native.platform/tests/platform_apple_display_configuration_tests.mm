#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#include <algorithm>
#include <chrono>
#include <cstdio>
#include <dlfcn.h>
#include <functional>
#include <memory>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace {
void Require(bool value, const std::string& message)
{
    if (!value) throw std::runtime_error(message);
}

struct PlatformLifetime {
    bool active = false;
    void Start() { Require(jalium_platform_init() == JALIUM_OK, "platform init"); active = true; }
    void Stop() { if (active) { active = false; jalium_platform_shutdown(); } }
    ~PlatformLifetime() { Stop(); }
};

struct Fixture {
    JaliumPlatformWindow* window = nullptr;
    NSWindow* native = nil;
    unsigned monitors = 0;
    bool mainThreadOnly = true;
    std::vector<JaliumEventType> events;
    std::function<void()> onMonitors;

    explicit Fixture(uint32_t style)
    {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Owned display configuration validation");
        params.x = 90; params.y = 90; params.width = 360; params.height = 220; params.style = style;
        window = jalium_window_create(&params);
        Require(window != nullptr, "window creation");
        native = ((__bridge NSView*)(void*)jalium_window_get_native_handle(window)).window;
        jalium_window_set_event_callback(window, [](const JaliumPlatformEvent* event, void* context) {
            auto& fixture = *static_cast<Fixture*>(context);
            fixture.events.push_back(event->type);
            if (event->type != JALIUM_EVENT_MONITORS_CHANGED) return;
            ++fixture.monitors;
            fixture.mainThreadOnly &= NSThread.isMainThread;
            if (fixture.onMonitors) fixture.onMonitors();
        }, this);
    }
    void Reset() { monitors = 0; events.clear(); mainThreadOnly = true; }
    void Close()
    {
        if (!window) return;
        auto* old = window; window = nullptr;
        jalium_window_set_event_callback(old, nullptr, nullptr);
        jalium_window_destroy(old);
    }
    ~Fixture() { Close(); }
};

// Process-local AppKit notifications only. No display reconfiguration or input
// injection, distributed notification, or event to another application is used.
void PostDisplayChange()
{
    [NSNotificationCenter.defaultCenter postNotificationName:
        NSApplicationDidChangeScreenParametersNotification object:NSApp];
}

void Pump(double seconds = 0.06)
{
    auto end = std::chrono::steady_clock::now() + std::chrono::duration<double>(seconds);
    do {
        // Match the product host: pending AppKit events and main-queue work
        // both need to run before asserting asynchronous notification counts.
        jalium_platform_poll_events();
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.005, true);
    } while (std::chrono::steady_clock::now() < end);
}

void PostFromBackground()
{
    std::thread worker([] { @autoreleasepool { PostDisplayChange(); } });
    worker.join();
}

int RunChecks()
{
    Dl_info loaded{}; dladdr((void*)&jalium_platform_init, &loaded);
    std::printf("Loaded display-test platform: %s\n", loaded.dli_fname ?: "<unknown>");
    PlatformLifetime platform; platform.Start();
    [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
    if (!NSApp.running) [NSApp finishLaunching];
    constexpr uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
        JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE;
    const uint32_t custom = (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS;
    int passed = 0, total = 0;
    for (uint32_t style : {regular, custom}) {
        auto run = [&](const char* name, const std::function<void()>& body) {
            ++total;
            try {
                body(); ++passed;
                std::printf("PASS: display configuration %s: %s\n", style == regular ? "Native" : "Custom", name);
            } catch (const std::exception& error) {
                std::fprintf(stderr, "FAIL: display configuration %s: %s: %s\n",
                    style == regular ? "Native" : "Custom", name, error.what());
            }
        };
        run("hidden windows and repeated changes", [&] {
            Fixture fixture(style); fixture.Reset();
            PostDisplayChange();
            Require(fixture.monitors == 1, "stationary hidden window missed the application notification");
            PostDisplayChange(); PostDisplayChange();
            Require(fixture.monitors == 3, "fresh display changes were dropped or duplicated");
            Require(!fixture.native.visible, "notification showed a hidden window");
        });
        run("visible window retains geometry and responder", [&] {
            Fixture fixture(style); jalium_window_show(fixture.window); fixture.Reset();
            NSRect frame = fixture.native.frame;
            NSResponder* responder = fixture.native.firstResponder;
            PostDisplayChange();
            Require(fixture.monitors == 1, "stationary visible window missed the application notification");
            Require(NSEqualRects(frame, fixture.native.frame) && fixture.native.firstResponder == responder,
                "notification changed geometry or responder");
            Require(std::none_of(fixture.events.begin(), fixture.events.end(), [](auto type) {
                return type == JALIUM_EVENT_MOVE || type == JALIUM_EVENT_RESIZE || type == JALIUM_EVENT_STATE_CHANGED;
            }), "display notification fabricated a geometry or state change");
        });
        run("disabled windows still refresh system settings", [&] {
            Fixture fixture(style); jalium_window_set_enabled(fixture.window, 0); fixture.Reset();
            PostDisplayChange();
            Require(fixture.monitors == 1, "disabled window missed the display notification");
            Require(!fixture.native.canBecomeKeyWindow, "display notification enabled a window");
        });
        run("independent owned and popup windows", [&] {
            Fixture owner(style), child(style), popup(JALIUM_WINDOW_STYLE_POPUP | JALIUM_WINDOW_STYLE_BORDERLESS);
            jalium_window_set_owner(child.window, (intptr_t)(__bridge void*)owner.native);
            jalium_window_set_owner(popup.window, (intptr_t)(__bridge void*)owner.native);
            owner.Reset(); child.Reset(); popup.Reset();
            PostDisplayChange();
            Require(owner.monitors == 1 && child.monitors == 1 && popup.monitors == 1,
                "display change did not reach each physical window exactly once");
        });
        run("destroy a later notification target", [&] {
            Fixture first(style), later(style);
            first.onMonitors = [&] { later.Close(); };
            PostDisplayChange();
            Require(first.monitors == 1 && !later.window && later.monitors == 0,
                "broadcast retained a destroyed later target");
            PostDisplayChange(); Require(first.monitors == 2, "destroying a target stopped fresh notifications");
        });
        run("destroyed target replacement waits for the next change", [&] {
            Fixture first(style), later(style); std::unique_ptr<Fixture> replacement;
            first.onMonitors = [&] {
                if (first.monitors == 1) { later.Close(); replacement = std::make_unique<Fixture>(style); }
            };
            PostDisplayChange();
            Require(replacement && first.monitors == 1 && later.monitors == 0 && replacement->monitors == 0,
                "old snapshot notified a replacement for its destroyed target");
            PostDisplayChange();
            Require(first.monitors == 2 && replacement->monitors == 1, "replacement missed the next change");
        });
        run("destroy the current notification target", [&] {
            Fixture first(style), later(style);
            first.onMonitors = [&] { first.Close(); };
            PostDisplayChange();
            Require(first.monitors == 1 && !first.window && later.monitors == 1,
                "destroying the current target broke remaining delivery");
        });
        run("new windows wait for a fresh notification", [&] {
            Fixture first(style); std::unique_ptr<Fixture> created;
            first.onMonitors = [&] { if (!created) created = std::make_unique<Fixture>(style); };
            PostDisplayChange();
            Require(created && first.monitors == 1 && created->monitors == 0,
                "broadcast included a newly created target from outside its snapshot");
            PostDisplayChange();
            Require(first.monitors == 2 && created->monitors == 1, "new target missed the next notification");
        });
        run("reentrant changes coalesce after the current broadcast", [&] {
            Fixture first(style), later(style); int depth = 0, maxDepth = 0;
            first.onMonitors = [&] {
                maxDepth = std::max(maxDepth, ++depth);
                if (first.monitors == 1) { PostDisplayChange(); PostDisplayChange(); }
                --depth;
            };
            PostDisplayChange();
            Require(first.monitors == 1 && later.monitors == 1 && maxDepth == 1,
                "nested display change reentered or interrupted the current snapshot");
            Pump();
            Require(first.monitors == 2 && later.monitors == 2 && maxDepth == 1,
                "nested changes were lost or scheduled duplicate broadcasts: first=" + std::to_string(first.monitors) +
                " later=" + std::to_string(later.monitors) + " depth=" + std::to_string(maxDepth));
        });
        run("background notification reaches only the main thread", [&] {
            Fixture first(style), later(style);
            PostFromBackground();
            Require(first.monitors == 0 && later.monitors == 0, "background post called window callbacks inline");
            Pump();
            Require(first.monitors == 1 && later.monitors == 1 && first.mainThreadOnly && later.mainThreadOnly,
                "background change did not marshal to the main thread: first=" + std::to_string(first.monitors) +
                " later=" + std::to_string(later.monitors) + " main=" +
                std::to_string(first.mainThreadOnly && later.mainThreadOnly));
        });
        run("queued work cannot survive shutdown and reinitialization", [&] {
            Fixture old(style); PostFromBackground();
            old.Close(); platform.Stop(); platform.Start();
            Fixture replacement(style); Pump();
            Require(replacement.monitors == 0, "old observer notified a replacement lifecycle");
            PostDisplayChange();
            Require(replacement.monitors == 1, "new platform lifecycle did not install its observer");
        });
        run("reference counted initialization keeps one observer", [&] {
            Fixture fixture(style);
            { PlatformLifetime extra; extra.Start(); PostDisplayChange();
              Require(fixture.monitors == 1, "repeated initialization duplicated or omitted the observer"); }
            PostDisplayChange(); Require(fixture.monitors == 2, "partial shutdown removed the live observer");
        });
        run("foreign notification objects are ignored", [&] {
            Fixture fixture(style);
            [NSNotificationCenter.defaultCenter postNotificationName:
                NSApplicationDidChangeScreenParametersNotification object:fixture.native];
            Require(fixture.monitors == 0, "foreign notification object entered the application observer");
            PostDisplayChange(); Require(fixture.monitors == 1, "real application object was ignored");
        });
        run("shutdown during a pending broadcast retires its generation", [&] {
            Fixture first(style), later(style); std::unique_ptr<Fixture> replacement;
            first.onMonitors = [&] {
                PostDisplayChange();
                first.Close(); later.Close(); platform.Stop(); platform.Start();
                replacement = std::make_unique<Fixture>(style); PostDisplayChange();
            };
            PostDisplayChange(); Pump();
            Require(replacement && replacement->monitors == 1 && later.monitors == 0,
                "obsolete pending broadcast reached the new platform lifecycle");
            PostDisplayChange(); Require(replacement->monitors == 2, "fresh lifecycle was poisoned by an old broadcast");
        });
    }
    std::printf("macOS display configuration checks: %d/%d passed\n", passed, total);
    return passed == total ? 0 : 1;
}
} // namespace

int RunAppleDisplayConfigurationTests()
{
    std::setvbuf(stdout, nullptr, _IONBF, 0);
    @autoreleasepool {
        try { return RunChecks(); }
        catch (const std::exception& error) {
            std::fprintf(stderr, "FAIL: display configuration setup: %s\n", error.what());
            return 1;
        }
    }
}
