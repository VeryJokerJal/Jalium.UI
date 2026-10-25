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

namespace {
void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
struct Window {
    JaliumPlatformWindow* value = nullptr;
    NSView* view = nil;
    NSWindow* native = nil;
    explicit Window(uint32_t style) {
        JaliumWindowParams p{}; p.title = reinterpret_cast<const JaliumUtf16Char*>(u"Owned style reentrancy validation");
        p.width = 360; p.height = 220; p.x = p.y = 100; p.style = style;
        value = jalium_window_create(&p); Require(value != nullptr, "create hidden window");
        view = (__bridge NSView*)(void*)jalium_window_get_native_handle(value); native = view.window;
        view.wantsLayer = YES; Require(native && !native.visible && view.layer, "real hidden AppKit window and backing layer");
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
}
int main(int argc, char** argv) {
    @autoreleasepool {
        setvbuf(stdout, nullptr, _IONBF, 0); auto cases = Cases();
        if (argc == 2 && std::strcmp(argv[1], "--list") == 0) {
            for (size_t i = 0; i < cases.size(); ++i) { const auto& c = cases[i]; std::printf("%zu %s %s %s %s %d\n", i, c.custom ? "Custom" : "Native", c.entry, c.key, c.action, c.button); } return 0;
        }
        Require(jalium_platform_init() == JALIUM_OK, "platform init"); [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory]; if (!NSApp.running) [NSApp finishLaunching];
        int selected = argc == 2 && std::strncmp(argv[1], "--case=", 7) == 0 ? std::stoi(argv[1] + 7) : -1;
        unsigned passed = 0, total = 0;
        for (size_t i = 0; i < cases.size(); ++i) {
            if (selected >= 0 && int(i) != selected) continue; ++total; const auto& c = cases[i];
            try { Run(c); ++passed; std::printf("PASS style %zu %s/%s/%s/%s/%d\n", i, c.custom ? "Custom" : "Native", c.entry, c.key, c.action, c.button); }
            catch (const std::exception& e) { std::fprintf(stderr, "FAIL style %zu %s/%s/%s/%s/%d: %s\n", i, c.custom ? "Custom" : "Native", c.entry, c.key, c.action, c.button, e.what()); }
        }
        jalium_platform_shutdown(); std::printf("macOS style reentrancy checks: %u/%u; own process, initially hidden windows, no input or system writes\n", passed, total); return total && passed == total ? 0 : 1;
    }
}
