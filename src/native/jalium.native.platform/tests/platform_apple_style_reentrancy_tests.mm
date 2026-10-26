#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#import <QuartzCore/QuartzCore.h>
#include <cstdio>
#include <cstring>
#include <functional>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

@interface JaliumStyleObserver : NSObject {
@public
    std::function<void()> action;
    unsigned count;
}
@end
@implementation JaliumStyleObserver
- (void)observeValueForKeyPath:(NSString*)key ofObject:(id)object change:(NSDictionary*)change context:(void*)context
{
    ++count; auto callback = action; if (callback) callback();
}
@end

@interface JaliumStyleIntentObserver : NSObject {
@public std::function<void()> action; bool prior; unsigned count;
}
@end
@implementation JaliumStyleIntentObserver
- (void)observeValueForKeyPath:(NSString*)key ofObject:(id)object change:(NSDictionary*)change context:(void*)context
{
    if ([change[NSKeyValueChangeNotificationIsPriorKey] boolValue] == prior) {
        ++count; auto callback = action; if (callback) callback();
    }
}
@end

namespace {
void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
struct Window {
    JaliumPlatformWindow* value = nullptr;
    NSView* view = nil;
    NSWindow* native = nil;
    unsigned destroyed = 0;
    explicit Window(uint32_t style) {
        JaliumWindowParams p{}; p.title = reinterpret_cast<const JaliumUtf16Char*>(u"Owned style reentrancy validation");
        p.width = 360; p.height = 220; p.x = p.y = 100; p.style = style;
        value = jalium_window_create(&p); Require(value != nullptr, "create hidden window");
        view = (__bridge NSView*)(void*)jalium_window_get_native_handle(value); native = view.window;
        view.wantsLayer = YES; Require(native && !native.visible && view.layer, "real hidden AppKit window and backing layer");
        jalium_window_set_event_callback(value, [](const JaliumPlatformEvent* event, void* context) {
            if (event->type == JALIUM_EVENT_DESTROYED) ++static_cast<Window*>(context)->destroyed;
        }, this);
    }
    void Close() { if (value) { auto* old = value; value = nullptr; jalium_window_destroy(old); } }
    ~Window() { Close(); }
};
struct Observation {
    id target; NSString* key; JaliumStyleObserver* observer = [JaliumStyleObserver new];
    Observation(id object, NSString* property) : target(object), key(property) {
        Require(target != nil, "real property observation target");
        [target addObserver:observer forKeyPath:key options:0 context:nullptr];
        id old = [target valueForKey:key];
        if ([key isEqualToString:@"titlebarAppearsTransparent"]) [target setValue:@(![old boolValue]) forKey:key];
        [target setValue:old forKey:key];
        Require(observer->count != 0, "AppKit KVO positive control did not fire");
        observer->count = 0;
    }
    ~Observation() { observer->action = {}; [target removeObserver:observer forKeyPath:key]; }
};
struct Case { bool custom; const char* entry; const char* key; const char* action; int button = -1; };
std::vector<Case> Cases() {
    std::vector<Case> cases;
    for (bool custom : {false, true}) for (const char* action : {"control", "close", "replace"}) {
        for (const char* key : {"level", "styleMask", "titleVisibility", "titlebarAppearsTransparent", "collectionBehavior", "excludedFromWindowsMenu"}) cases.push_back({custom, "style", key, action});
        for (const char* key : {"collectionBehavior", "excludedFromWindowsMenu"}) cases.push_back({custom, "participation", key, action});
        for (const char* key : {"title", "excludedFromWindowsMenu"}) cases.push_back({custom, "title", key, action});
        cases.push_back({custom, "hide", "excludedFromWindowsMenu", action});
        if (!custom) for (int button : {int(NSWindowCloseButton), int(NSWindowMiniaturizeButton), int(NSWindowZoomButton)}) cases.push_back({false, "style", "enabled", action, button});
    }
    for (bool custom : {false, true}) {
        for (const char* action : {"control", "close", "replace"}) cases.push_back({custom, "show", "excludedFromWindowsMenu", action});
        cases.push_back({custom, "style", "level", "replace-owner"});
    }
    for (bool custom : {false, true}) for (const char* entry : {"show", "hide"})
        cases.push_back({custom, entry, "excludedFromWindowsMenu", "visibility"});
    return cases;
}
void Pump() { for (int i = 0; i < 10; ++i) { jalium_platform_poll_events(); CFRunLoopRunInMode(kCFRunLoopDefaultMode, .005, true); } }
void Run(const Case& c) {
    constexpr uint32_t flags = JALIUM_WINDOW_STYLE_CLOSABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE | JALIUM_WINDOW_STYLE_RESIZABLE;
    uint32_t style = flags | (c.custom ? JALIUM_WINDOW_STYLE_BORDERLESS : JALIUM_WINDOW_STYLE_TITLEBAR);
    Window window(style); NSSize original = window.view.bounds.size;
    id target = c.button < 0 ? (id)window.native : (id)[window.native standardWindowButton:(NSWindowButton)c.button];
    Observation observation(target, [NSString stringWithUTF8String:c.key]); bool entered = false;
    if (std::strcmp(c.key, "titlebarAppearsTransparent") == 0) window.native.titlebarAppearsTransparent = YES;
    observation.observer->count = 0;
    uint32_t newest = flags | JALIUM_WINDOW_STYLE_TRANSPARENT | (c.custom ? JALIUM_WINDOW_STYLE_TITLEBAR : JALIUM_WINDOW_STYLE_BORDERLESS);
    std::unique_ptr<Window> replacement;
    observation.observer->action = [&] {
        if (entered) return;
        entered = true;
        if (std::strcmp(c.action, "close") == 0) window.Close();
        if (std::strcmp(c.action, "replace-owner") == 0) { window.Close(); replacement = std::make_unique<Window>(newest); }
        if (std::strcmp(c.action, "visibility") == 0) {
            if (std::strcmp(c.entry, "show") == 0) jalium_window_hide(window.value);
            else jalium_apple_window_show(window.value, 0);
        }
        if (std::strcmp(c.action, "replace") == 0) Require(jalium_apple_window_set_style(window.value, newest) == JALIUM_OK, "nested new style accepted");
    };
    if (std::strcmp(c.entry, "style") == 0) Require(jalium_apple_window_set_style(window.value, style | JALIUM_WINDOW_STYLE_TOPMOST) == JALIUM_OK, "outer style accepted");
    else if (std::strcmp(c.entry, "participation") == 0) Require(jalium_window_set_show_in_taskbar(window.value, 0) == JALIUM_OK, "participation accepted");
    else if (std::strcmp(c.entry, "title") == 0) jalium_window_set_title(window.value, reinterpret_cast<const JaliumUtf16Char*>(u"Changed title"));
    else if (std::strcmp(c.entry, "show") == 0) {
        window.native.level = -1000;
        // Keep this own-process visibility check from activating the application.
        jalium_apple_window_show(window.value, 0);
        if (window.value) Require(window.native.visible == (std::strcmp(c.action, "visibility") != 0), "show respects the newer visibility request");
    } else jalium_window_hide(window.value);
    Require(entered && observation.observer->count != 0, "observed the actual entry setter");
    observation.observer->action = {};
    if (std::strcmp(c.action, "visibility") == 0)
        Require(window.value && window.native.visible == (std::strcmp(c.entry, "hide") == 0), "latest visibility request survives the older caller");
    if ((std::strcmp(c.entry, "show") == 0 || std::strcmp(c.action, "visibility") == 0) && window.value) jalium_window_hide(window.value);
    Pump();
    if (std::strcmp(c.action, "close") == 0 || std::strcmp(c.action, "replace-owner") == 0) {
        Require(!window.value && !window.native.visible, "callback closed its owner");
        if (replacement) Require(replacement->value && !replacement->native.visible && !replacement->native.opaque &&
            !replacement->view.layer.opaque && [replacement->native.backgroundColor isEqual:NSColor.clearColor], "retired style did not mutate its replacement");
    }
    else {
        Require(window.value && !window.native.visible, "surviving hidden window");
        Require(NSEqualSizes(original, window.view.bounds.size), "style and menu update preserve client size");
        if (std::strcmp(c.action, "replace") == 0) {
            Require(!window.native.opaque && !window.view.layer.opaque && [window.native.backgroundColor isEqual:NSColor.clearColor], "latest transparent appearance survives");
            Require(bool(window.native.styleMask & NSWindowStyleMaskTitled) == c.custom && window.native.level == NSNormalWindowLevel, "latest chrome and level survive");
        }
    }
}
struct IntentCase { bool custom, prior; int kind; const char* mode; };
std::vector<IntentCase> IntentCases()
{
    std::vector<IntentCase> cases;
    for (bool custom : {false, true}) for (bool prior : {false, true}) for (int kind = 0; kind < 10; ++kind)
        for (const char* mode : {"control", "latest", "latest-twice", "close", "replacement"})
            cases.push_back({custom, prior, kind, mode});
    return cases;
}
uint32_t IntentBaseStyle(bool custom)
{
    return JALIUM_WINDOW_STYLE_CLOSABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE |
        JALIUM_WINDOW_STYLE_MAXIMIZABLE | JALIUM_WINDOW_STYLE_RESIZABLE |
        (custom ? JALIUM_WINDOW_STYLE_BORDERLESS : JALIUM_WINDOW_STYLE_TITLEBAR);
}
int ApplyIntent(Window& window, const IntentCase& c, bool latest)
{
    if (c.kind == 6 || c.kind == 7) return jalium_window_set_show_in_taskbar(window.value, latest);
    if (c.kind == 8) return jalium_window_set_topmost(window.value, !latest);
    uint32_t style = IntentBaseStyle(c.custom);
    if (!latest) style |= JALIUM_WINDOW_STYLE_TRANSPARENT | JALIUM_WINDOW_STYLE_INPUT_TRANSPARENT | JALIUM_WINDOW_STYLE_TOPMOST;
    if (!latest && c.kind == 9) style |= JALIUM_WINDOW_STYLE_POPUP;
    return jalium_apple_window_set_style(window.value, style);
}
bool MatchesIntent(Window& window, const IntentCase& c, bool latest)
{
    bool appearance = c.kind < 6 || c.kind == 9;
    bool transparent = appearance && !latest;
    bool topmost = !latest && (appearance || c.kind == 8);
    NSUInteger participation = !latest && c.kind == 9
        ? NSWindowCollectionBehaviorFullScreenAuxiliary : NSWindowCollectionBehaviorFullScreenPrimary;
    bool excluded = !latest && (c.kind == 6 || c.kind == 7);
    if (excluded) participation |= NSWindowCollectionBehaviorTransient | NSWindowCollectionBehaviorIgnoresCycle;
    constexpr NSUInteger participationMask = NSWindowCollectionBehaviorFullScreenPrimary |
        NSWindowCollectionBehaviorFullScreenAuxiliary | NSWindowCollectionBehaviorTransient | NSWindowCollectionBehaviorIgnoresCycle;
    return window.native.ignoresMouseEvents == transparent && window.native.opaque == !transparent &&
        [window.native.backgroundColor isEqual:transparent ? NSColor.clearColor : NSColor.windowBackgroundColor] &&
        window.native.hasShadow == !transparent && window.view.layer.opaque == !transparent &&
        window.native.level == (topmost ? NSFloatingWindowLevel : NSNormalWindowLevel) &&
        (window.native.collectionBehavior & participationMask) == participation &&
        window.native.excludedFromWindowsMenu == excluded;
}
void RunIntent(const IntentCase& c)
{
    Window window(IntentBaseStyle(c.custom)); window.view.layer.opaque = YES;
    NSSize original = window.view.bounds.size; std::unique_ptr<Window> replacement;
    NSString* keys[] = {@"ignoresMouseEvents", @"opaque", @"backgroundColor", @"hasShadow", @"opaque", @"level",
        @"collectionBehavior", @"excludedFromWindowsMenu", @"level", @"collectionBehavior"};
    id target = c.kind == 4 ? (id)window.view.layer : (id)window.native;
    auto* observer = [JaliumStyleIntentObserver new]; observer->prior = c.prior;
    bool entered = false, immediate = true; int nestedStatus = JALIUM_OK;
    observer->action = [&] {
        if (entered || std::strcmp(c.mode, "control") == 0) return;
        entered = true;
        if (std::strcmp(c.mode, "close") == 0 || std::strcmp(c.mode, "replacement") == 0) {
            window.Close();
            if (std::strcmp(c.mode, "replacement") == 0) {
                replacement = std::make_unique<Window>(IntentBaseStyle(c.custom)); replacement->view.layer.opaque = YES;
                nestedStatus = ApplyIntent(*replacement, c, true);
            }
            return;
        }
        if (std::strcmp(c.mode, "latest-twice") == 0) nestedStatus = ApplyIntent(window, c, false);
        if (nestedStatus == JALIUM_OK) nestedStatus = ApplyIntent(window, c, true);
        // Direct property requests have no queued style transaction to wait for.
        if (c.kind == 6 || c.kind == 7 || c.kind == 8) immediate = MatchesIntent(window, c, true);
    };
    [target addObserver:observer forKeyPath:keys[c.kind] options:NSKeyValueObservingOptionPrior context:nullptr];
    int outerStatus = ApplyIntent(window, c, false);
    observer->action = {}; [target removeObserver:observer forKeyPath:keys[c.kind]];
    Require(observer->count != 0, "real AppKit notification for the selected property and phase");
    Require(entered == (std::strcmp(c.mode, "control") != 0), "request callback entered the actual outer setter");
    Require(outerStatus == JALIUM_OK && nestedStatus == JALIUM_OK, "outer and nested requests accepted");
    // Only a nested full style at collectionBehavior enters the existing
    // deferred chrome transaction. The other requests complete synchronously.
    if (c.kind == 9) Pump();
    if (!window.value) {
        Require(window.destroyed == 1 && !window.native.visible && window.native.delegate == nil, "closed owner retires once");
        if (replacement) Require(MatchesIntent(*replacement, c, true) && !replacement->native.visible &&
            replacement->destroyed == 0, "older SDK writes cannot change the replacement owner");
        return;
    }
    bool latest = std::strcmp(c.mode, "control") != 0;
    Require(immediate, "direct nested requests remain synchronous");
    Require(MatchesIntent(window, c, latest), "complete latest appearance, level and participation survive unwind");
    Require(!window.native.visible && window.destroyed == 0, "surviving owner stays hidden");
    Require(NSEqualSizes(original, window.view.bounds.size), "style reconciliation preserves client geometry");
    Require(ApplyIntent(window, c, true) == JALIUM_OK && MatchesIntent(window, c, true), "subsequent independent request succeeds");
}

}
int main(int argc, char** argv) {
    @autoreleasepool {
        setvbuf(stdout, nullptr, _IONBF, 0); auto cases = Cases(); auto intentCases = IntentCases();
        if (argc == 2 && std::strcmp(argv[1], "--list") == 0) {
            for (size_t i = 0; i < cases.size(); ++i) { const auto& c = cases[i]; std::printf("%zu %s %s %s %s %d\n", i, c.custom ? "Custom" : "Native", c.entry, c.key, c.action, c.button); }
            for (size_t i = 0; i < intentCases.size(); ++i) {
                const auto& c = intentCases[i]; std::printf("%zu intent %s/%s/%d/%s\n", cases.size() + i,
                    c.custom ? "Custom" : "Native", c.prior ? "prior" : "after", c.kind, c.mode);
            }
            return 0;
        }
        Require(jalium_platform_init() == JALIUM_OK, "platform init"); [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory]; if (!NSApp.running) [NSApp finishLaunching];
        int selected = argc == 2 && std::strncmp(argv[1], "--case=", 7) == 0 ? std::stoi(argv[1] + 7) : -1;
        unsigned passed = 0, total = 0;
        for (size_t i = 0; i < cases.size(); ++i) {
            if (selected >= 0 && int(i) != selected) continue; ++total; const auto& c = cases[i];
            try { Run(c); ++passed; std::printf("PASS style %zu %s/%s/%s/%s/%d\n", i, c.custom ? "Custom" : "Native", c.entry, c.key, c.action, c.button); }
            catch (const std::exception& e) { std::fprintf(stderr, "FAIL style %zu %s/%s/%s/%s/%d: %s\n", i, c.custom ? "Custom" : "Native", c.entry, c.key, c.action, c.button, e.what()); }
        }
        for (size_t i = 0; i < intentCases.size(); ++i) {
            size_t index = cases.size() + i; if (selected >= 0 && int(index) != selected) continue;
            ++total; const auto& c = intentCases[i];
            try { RunIntent(c); ++passed; std::printf("PASS style intent %zu %s/%s/%d/%s\n", index,
                c.custom ? "Custom" : "Native", c.prior ? "prior" : "after", c.kind, c.mode); }
            catch (const std::exception& e) { std::fprintf(stderr, "FAIL style intent %zu %s/%s/%d/%s: %s\n", index,
                c.custom ? "Custom" : "Native", c.prior ? "prior" : "after", c.kind, c.mode, e.what()); }
        }
        jalium_platform_shutdown(); std::printf("macOS style reentrancy checks: %u/%u; own process, initially hidden windows, no input or system writes\n", passed, total); return total && passed == total ? 0 : 1;
    }
}
