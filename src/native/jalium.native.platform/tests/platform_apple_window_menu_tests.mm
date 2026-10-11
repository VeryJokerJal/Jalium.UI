#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include "jalium_platform.h"
#include <cmath>
#include <cstdio>
#include <dlfcn.h>
#include <functional>
#include <stdexcept>
#include <thread>

static void Require(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
}

// Observe this process's actual menu and actions without blocking for external
// input. Real menu tracking and keyboard cancellation are separate acceptance.
static NSMenu* g_menu;
static NSView* g_view;
static NSPoint g_point;
static int g_popups, g_cancellations;
static std::function<void(NSMenu*)> g_duringPopup;
static BOOL CapturePopup(NSMenu* menu, SEL, NSMenuItem*, NSPoint point, NSView* view)
{
    g_menu = menu; g_view = view; g_point = point; ++g_popups;
    [menu update];
    if (g_duringPopup) g_duringPopup(menu);
    return NO;
}
static void CaptureCancel(NSMenu* menu, SEL)
{
    if (menu == g_menu) ++g_cancellations;
}
struct PopupProbe {
    Method popup = class_getInstanceMethod(NSMenu.class, @selector(popUpMenuPositioningItem:atLocation:inView:));
    Method cancel = class_getInstanceMethod(NSMenu.class, @selector(cancelTrackingWithoutAnimation));
    IMP originalPopup, originalCancel;
    PopupProbe()
    {
        originalPopup = method_setImplementation(popup, reinterpret_cast<IMP>(CapturePopup));
        originalCancel = method_setImplementation(cancel, reinterpret_cast<IMP>(CaptureCancel));
    }
    ~PopupProbe()
    {
        method_setImplementation(popup, originalPopup);
        method_setImplementation(cancel, originalCancel);
    }
};

struct Fixture {
    JaliumPlatformWindow* window;
    NSWindow* native;
    int closeRequests = 0;
    bool acceptClose = false;
    explicit Fixture(uint32_t style)
    {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Window menu validation");
        params.x = 250; params.y = 210; params.width = 640; params.height = 480; params.style = style;
        window = jalium_window_create(&params);
        Require(window != nullptr, "window creation failed");
        native = ((__bridge NSView*)(void*)jalium_window_get_native_handle(window)).window;
        jalium_window_set_event_callback(window, [](const JaliumPlatformEvent* event, void* data) {
            auto& fixture = *static_cast<Fixture*>(data);
            if (event->type == JALIUM_EVENT_CLOSE_REQUESTED) {
                ++fixture.closeRequests;
                if (fixture.acceptClose) fixture.Destroy();
            }
        }, this);
        jalium_apple_window_show(window, 0);
        jalium_platform_poll_events();
    }
    void Destroy() { jalium_window_destroy(window); window = nullptr; }
    ~Fixture() { if (window) Destroy(); g_duringPopup = {}; g_menu = nil; g_view = nil; }
};

static NSMenuItem* Item(NSMenu* menu, SEL action)
{
    for (NSMenuItem* item in menu.itemArray) if (item.action == action) return item;
    throw std::runtime_error("native window action missing from menu");
}
static void Invoke(NSMenu* menu, SEL action)
{
    NSMenuItem* item = Item(menu, action);
    Require(item.target != nil && [item.target respondsToSelector:action], "menu action has no explicit target");
    Require([NSApp sendAction:action to:item.target from:item], "menu action not delivered");
}
static void Open(Fixture& fixture)
{
    int before = g_popups;
    Require(jalium_window_show_system_menu(fixture.window, 48, 80) == JALIUM_OK,
        "system menu request was rejected");
    Require(g_popups == before + 1, "request did not present an AppKit menu");
}

int main()
{
    @autoreleasepool {
        Require(jalium_platform_init() == JALIUM_OK, "platform initialization failed");
        [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory]; [NSApp finishLaunching];
        PopupProbe probe;
        constexpr uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
            JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE;
        constexpr uint32_t custom = (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS;
        NSMenu* previousWindowMenu = NSApp.windowsMenu;
        int passed = 0, total = 0;
        for (uint32_t style : {regular, custom}) {
            auto run = [&](const char* name, const std::function<void(Fixture&)>& check) {
                ++total;
                try { Fixture fixture(style); check(fixture); ++passed;
                    std::printf("PASS %s: %s\n", style == regular ? "Native" : "Custom", name); }
                catch (const std::exception& error) {
                    std::fprintf(stderr, "FAIL %s: %s: %s\n", style == regular ? "Native" : "Custom", name, error.what());
                }
                NSApp.windowsMenu = previousWindowMenu;
            };
            run("default actions and client-local physical coordinates", [](Fixture& f) {
                NSApp.windowsMenu = nil; Open(f);
                Require(g_menu.itemArray.count == 5, "default menu has unexpected actions");
                for (SEL action : {@selector(performMiniaturize:), @selector(zoom:), @selector(toggleFullScreen:), @selector(performClose:)})
                    Require(Item(g_menu, action).enabled, "available action is disabled");
                double scale = jalium_window_get_dpi_scale(f.window);
                Require(g_view == (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window), "menu has wrong client view");
                Require(std::abs(g_point.x - 48 / scale) < .001 && std::abs(g_point.y - 80 / scale) < .001,
                    "menu physical coordinates were not converted to client points");
            });
            run("host labels and shortcuts are copied without mutation", [](Fixture& f) {
                NSMenu* host = [[NSMenu alloc] initWithTitle:@"窗口"];
                NSMenuItem* source = [[NSMenuItem alloc] initWithTitle:@"缩放本窗口" action:@selector(zoom:) keyEquivalent:@"z"];
                source.keyEquivalentModifierMask = NSEventModifierFlagOption;
                [host addItem:source];
                [host addItem:[[NSMenuItem alloc] initWithTitle:@"Unrelated" action:@selector(arrangeInFront:) keyEquivalent:@""]];
                NSApp.windowsMenu = host; Open(f);
                NSMenuItem* item = Item(g_menu, @selector(zoom:));
                Require(item != source && [item.title isEqualToString:source.title] && [item.keyEquivalent isEqualToString:@"z"] &&
                    item.keyEquivalentModifierMask == NSEventModifierFlagOption, "host label or shortcut was lost");
                // AppKit may append its window-list entries to windowsMenu.
                // The supplied commands must retain their identity and target.
                Require(source.target == nil && host.itemArray[0] == source &&
                    [host.itemArray[1].title isEqualToString:@"Unrelated"], "host commands were mutated");
                Require(g_menu.itemArray.count == 5, "unrelated host command was copied");
            });
            run("capabilities validate all menu actions", [=](Fixture& f) {
                for (uint32_t bits = 0; bits < 8; ++bits) {
                    uint32_t capabilities = style & ~(JALIUM_WINDOW_STYLE_CLOSABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE);
                    if (bits & 1) capabilities |= JALIUM_WINDOW_STYLE_CLOSABLE;
                    if (bits & 2) capabilities |= JALIUM_WINDOW_STYLE_MINIMIZABLE;
                    if (bits & 4) capabilities |= JALIUM_WINDOW_STYLE_MAXIMIZABLE;
                    jalium_apple_window_set_style(f.window, capabilities); Open(f);
                    Require(Item(g_menu, @selector(performClose:)).enabled == ((bits & 1) != 0), "close capability ignored");
                    Require(Item(g_menu, @selector(performMiniaturize:)).enabled == ((bits & 2) != 0), "minimize capability ignored");
                    Require(Item(g_menu, @selector(zoom:)).enabled == ((bits & 4) != 0), "zoom capability ignored");
                    Require(Item(g_menu, @selector(toggleFullScreen:)).enabled == ((bits & 4) != 0), "fullscreen capability ignored");
                }
            });
            run("cancel does not request window close", [](Fixture& f) {
                Open(f); Require(f.closeRequests == 0 && f.native.visible, "menu cancellation closed the window");
                Open(f); Require(f.closeRequests == 0, "repeat menu request closed the window");
            });
            run("close keeps application cancellation", [](Fixture& f) {
                g_duringPopup = [&](NSMenu* menu) { Invoke(menu, @selector(performClose:)); };
                Open(f); Require(f.closeRequests == 1 && f.window && f.native.visible, "close did not preserve cancellation");
            });
            run("accepted close can destroy the target during tracking", [](Fixture& f) {
                f.acceptClose = true;
                g_duringPopup = [&](NSMenu* menu) { Invoke(menu, @selector(performClose:)); };
                Open(f); Require(f.closeRequests == 1 && !f.window, "accepted close did not destroy target");
            });
            run("zoom targets the requested window", [=](Fixture& f) {
                Fixture other(style); NSRect otherFrame = other.native.frame;
                g_duringPopup = [&](NSMenu* menu) { Invoke(menu, @selector(zoom:)); };
                Open(f); Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED, "zoom missed requested window");
                Require(NSEqualRects(other.native.frame, otherFrame), "zoom changed another window");
            });
            for (int mutation = 0; mutation < 3; ++mutation) {
                run(mutation == 0 ? "hide cancels tracking and rejects later action" : mutation == 1 ?
                    "disable cancels tracking and rejects later action" : "destroy cancels tracking and rejects later action", [=](Fixture& f) {
                    int before = g_cancellations;
                    g_duringPopup = [&](NSMenu* menu) {
                        if (mutation == 0) jalium_window_hide(f.window);
                        else if (mutation == 1) jalium_window_set_enabled(f.window, 0);
                        else f.Destroy();
                        Invoke(menu, @selector(performClose:));
                    };
                    Open(f); Require(g_cancellations == before + 1 && f.closeRequests == 0,
                        "invalidated target kept tracking or accepted a late action");
                });
            }
            run("nested request is rejected and next request remains usable", [](Fixture& f) {
                int nested = JALIUM_OK;
                g_duringPopup = [&](NSMenu*) { nested = jalium_window_show_system_menu(f.window, 10, 10); };
                Open(f); Require(nested == JALIUM_ERROR_INVALID_STATE, "nested menu tracking was accepted");
                g_duringPopup = {}; Open(f);
            });
            run("hidden disabled and worker requests are rejected", [](Fixture& f) {
                int before = g_popups;
                jalium_window_hide(f.window);
                Require(jalium_window_show_system_menu(f.window, 0, 0) == JALIUM_ERROR_INVALID_STATE, "hidden target accepted menu");
                jalium_apple_window_show(f.window, 0); jalium_window_set_enabled(f.window, 0);
                Require(jalium_window_show_system_menu(f.window, 0, 0) == JALIUM_ERROR_INVALID_STATE, "disabled target accepted menu");
                jalium_window_set_enabled(f.window, 1);
                int result = JALIUM_OK;
                std::thread worker([&] { result = jalium_window_show_system_menu(f.window, 0, 0); }); worker.join();
                Require(result == JALIUM_ERROR_INVALID_STATE && g_popups == before, "worker request accessed AppKit menu");
            });
            run("client origin matches actual flipped view before and after style change", [=](Fixture& f) {
                using Getter = int32_t (*)(JaliumPlatformWindow*, int32_t*, int32_t*);
                auto get = reinterpret_cast<Getter>(dlsym(RTLD_DEFAULT, "jalium_apple_window_get_client_origin"));
                Require(get != nullptr, "native client-origin query is missing");
                for (uint32_t next : {style, style ^ (JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_BORDERLESS)}) {
                    jalium_apple_window_set_style(f.window, next);
                    int32_t x, y; Require(get(f.window, &x, &y) == JALIUM_OK, "client origin query failed");
                    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    NSPoint point = [f.native convertPointToScreen:[view convertPoint:NSZeroPoint toView:nil]];
                    double scale = jalium_window_get_dpi_scale(f.window);
                    double top = NSMaxY(NSScreen.screens.firstObject.frame);
                    Require(x == std::lround(point.x * scale) && y == std::lround((top - point.y) * scale),
                        "client origin included native titlebar decoration or used bottom-left coordinates");
                }
            });
        }
        NSApp.windowsMenu = previousWindowMenu;
        jalium_platform_shutdown();
        std::printf("macOS native Window menu checks: %d/%d passed\n", passed, total);
        return passed == total ? 0 : 1;
    }
}
