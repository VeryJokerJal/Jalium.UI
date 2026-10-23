#import <AppKit/AppKit.h>
#include "jalium_platform.h"
#include <cstdio>
#include <functional>
#include <stdexcept>
#include <thread>
#include <utility>

static int checks;
static void Check(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
    ++checks;
}

struct Popup {
    JaliumPlatformWindow* window = nullptr;
    NSWindow* native = nil;
    bool supported = true;
    uint64_t eligible = 1;
    int queries = 0;
    std::function<void()> onQuery;

    static int32_t Query(JaliumAccessibilityRequest* request, void* context)
    {
        auto& popup = *static_cast<Popup*>(context);
        if (request->nodeId != 1) return 0;
        if (request->operation == JALIUM_AX_INFO) {
            request->flags = JALIUM_AX_ENABLED;
            request->width = 240; request->height = 100;
            return 1;
        }
        if (request->operation != JALIUM_AX_POPUP_CAN_BECOME_KEY) return 0;
        ++popup.queries;
        auto callback = std::move(popup.onQuery);
        popup.onQuery = {};
        if (callback) callback();
        request->resultId = popup.eligible;
        return popup.supported;
    }

    explicit Popup(uint32_t style = JALIUM_WINDOW_STYLE_BORDERLESS |
        JALIUM_WINDOW_STYLE_POPUP | JALIUM_WINDOW_STYLE_INPUT_TRANSPARENT)
    {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Jalium popup keyboard contracts");
        params.x = 100; params.y = 120; params.width = 240; params.height = 100; params.style = style;
        window = jalium_window_create(&params);
        Check(window != nullptr, "create popup");
        native = ((__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(window))).window;
        jalium_apple_window_set_accessibility(window, Query, this);
        jalium_apple_window_show(window, 0);
    }
    void Destroy() { auto* old = window; window = nullptr; if (old) jalium_window_destroy(old); }
    ~Popup() { Destroy(); }
};

int main()
{
    @autoreleasepool {
        if (jalium_platform_init() != JALIUM_OK) return 2;
        [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
        [NSApp finishLaunching];
        int result = 0;
        try {
            {
                NSWindow* key = NSApp.keyWindow;
                Popup popup;
                Check(NSApp.keyWindow == key && !popup.native.keyWindow, "pass-through popup opens without activation");
                Check(popup.native.ignoresMouseEvents, "pointer pass-through remains enabled");
                Check(popup.native.canBecomeKeyWindow, "focusable pass-through popup is eligible for keyboard");
                Check(!popup.native.canBecomeMainWindow, "popup does not become the main window");
                Check(NSApp.keyWindow == key, "eligibility query does not select a window");
                popup.eligible = 0;
                Check(!popup.native.canBecomeKeyWindow, "noninteractive content cannot become key");
                popup.eligible = 2;
                Check(!popup.native.canBecomeKeyWindow, "invalid capability is rejected");
                popup.eligible = 1; popup.supported = false;
                Check(!popup.native.canBecomeKeyWindow, "old callbacks preserve non-key fallback");
                popup.supported = true;
                Check(popup.native.canBecomeKeyWindow, "fresh capability recovers without a style update");
                jalium_apple_window_set_accessibility(popup.window, nullptr, nullptr);
                Check(!popup.native.canBecomeKeyWindow, "missing callback preserves non-key fallback");
                jalium_apple_window_set_accessibility(popup.window, Popup::Query, &popup);
                Check(popup.native.canBecomeKeyWindow, "reattached callback recovers");
                popup.onQuery = [&] { Check(!popup.native.canBecomeKeyWindow, "recursive eligibility is bounded"); };
                Check(popup.native.canBecomeKeyWindow, "outer eligibility survives bounded recursion");
                popup.onQuery = [] { @throw [NSException exceptionWithName:@"KeyboardProbe" reason:@"owned query" userInfo:nil]; };
                Check(!popup.native.canBecomeKeyWindow, "provider exception is isolated");
                Check(popup.native.canBecomeKeyWindow, "exception resets recursion guard");
                int queries = popup.queries;
                bool offThread = true;
                std::thread worker([&] { @autoreleasepool { offThread = popup.native.canBecomeKeyWindow; } });
                worker.join();
                Check(!offThread && popup.queries == queries, "foreign thread does not call managed provider");
                for (int mode = 0; mode < 4; ++mode) {
                    popup.onQuery = [&] {
                        if (mode < 2) {
                            jalium_window_hide(popup.window);
                            if (mode == 1) jalium_apple_window_show(popup.window, 0);
                        } else {
                            jalium_window_set_enabled(popup.window, 0);
                            if (mode == 3) jalium_window_set_enabled(popup.window, 1);
                        }
                    };
                    Check(!popup.native.canBecomeKeyWindow, "hide/disable and their ABA transitions retire query");
                    jalium_window_set_enabled(popup.window, 1);
                    jalium_apple_window_show(popup.window, 0);
                    Check(popup.native.canBecomeKeyWindow, "new query recovers after visibility/enable change");
                }
                popup.onQuery = [&] { jalium_apple_window_set_accessibility(popup.window, Popup::Query, &popup); };
                Check(!popup.native.canBecomeKeyWindow, "replaced bridge cannot publish old capability");
                Check(popup.native.canBecomeKeyWindow, "new bridge can answer capability");
                popup.onQuery = [&] { popup.Destroy(); };
                Check(!popup.native.canBecomeKeyWindow, "destroy during query does not use freed owner");
                Check(!popup.native.canBecomeKeyWindow, "retained closed window cannot become key");
            }
            {
                Popup ordinary(JALIUM_WINDOW_STYLE_BORDERLESS | JALIUM_WINDOW_STYLE_INPUT_TRANSPARENT);
                int queries = ordinary.queries;
                Check(!ordinary.native.canBecomeKeyWindow && ordinary.queries == queries,
                    "ordinary pointer-transparent window retains its policy");
            }
            {
                Popup pointer(JALIUM_WINDOW_STYLE_BORDERLESS | JALIUM_WINDOW_STYLE_POPUP);
                jalium_apple_window_set_accessibility(pointer.window, nullptr, nullptr);
                Check(pointer.native.canBecomeKeyWindow && !pointer.native.ignoresMouseEvents,
                    "pointer-enabled popup retains legacy keyboard eligibility");
                jalium_window_set_enabled(pointer.window, 0);
                Check(!pointer.native.canBecomeKeyWindow, "disabled popup cannot become key");
            }
        } catch (const std::exception& error) {
            std::fprintf(stderr, "FAIL popup keyboard: %s\n", error.what()); result = 1;
        }
        jalium_platform_shutdown();
        std::printf("popup keyboard: %d checks, result=%d\n", checks, result);
        return result;
    }
}
