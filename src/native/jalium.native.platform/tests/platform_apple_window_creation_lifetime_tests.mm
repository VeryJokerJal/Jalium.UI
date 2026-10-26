#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#include <cstdio>
#include <cstring>
#include <functional>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

static void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
@interface JaliumCreationObserver : NSObject {
@public
    NSWindow* window;
    NSMutableArray<NSString*>* keys;
    std::function<void(NSString*, bool)> property;
}
@end
@implementation JaliumCreationObserver
- (instancetype)init { if ((self = [super init])) keys = [NSMutableArray new]; return self; }
- (void)observeValueForKeyPath:(NSString*)key ofObject:(id)object change:(NSDictionary*)change context:(void*)context {
    auto callback = property; if (callback) callback(key, [change[NSKeyValueChangeNotificationIsPriorKey] boolValue]);
}
@end
@interface JaliumCreationParent : NSWindow
@property bool closeChild;
@property bool closeBefore;
@property unsigned additions;
@end
@implementation JaliumCreationParent
- (void)addChildWindow:(NSWindow*)child ordered:(NSWindowOrderingMode)place {
    ++_additions;
    if (_closeChild && _closeBefore) [child close];
    [super addChildWindow:child ordered:place];
    if (_closeChild && !_closeBefore) [child close];
}
@end

namespace {
struct Window {
    JaliumPlatformWindow* value = nullptr;
    NSWindow* native = nil;
    explicit Window(uint32_t style) {
        JaliumWindowParams p{}; p.title = reinterpret_cast<const JaliumUtf16Char*>(u"创建回调中文🙂 Miii");
        p.width = 360; p.height = 220; p.x = 117; p.y = 139; p.style = style;
        value = jalium_window_create(&p); Require(value != nullptr, "create independent fixture");
        native = ((__bridge NSView*)(void*)jalium_window_get_native_handle(value)).window;
    }
    void Close() { if (value) { auto* old = value; value = nullptr; jalium_window_destroy(old); } }
    ~Window() { Close(); }
};
struct Observation {
    JaliumCreationObserver* probe = [JaliumCreationObserver new]; id notifications = nil;
    std::function<void(NSNotification*)> receive;
    Observation() {
        notifications = [NSNotificationCenter.defaultCenter addObserverForName:nil object:nil queue:nil usingBlock:^(NSNotification* note) {
            if (![note.object isKindOfClass:NSWindow.class]) return;
            NSWindow* native = note.object;
            if (![NSStringFromClass(native.class) containsString:@"JaliumAppleWindow"]) return;
            if (!probe->window) {
                probe->window = native;
                for (NSString* key in @[@"title", @"contentView", @"opaque", @"level", @"styleMask", @"acceptsMouseMovedEvents", @"titleVisibility", @"delegate", @"releasedWhenClosed"]) {
                    [native addObserver:probe forKeyPath:key options:([key isEqualToString:@"delegate"] || [key isEqualToString:@"releasedWhenClosed"]) ? NSKeyValueObservingOptionPrior : 0 context:nullptr];
                    [probe->keys addObject:key];
                }
            }
            if (native == probe->window && receive) receive(note);
        }];
    }
    ~Observation() {
        receive = {}; probe->property = {};
        [NSNotificationCenter.defaultCenter removeObserver:notifications];
        for (NSString* key in probe->keys) [probe->window removeObserver:probe forKeyPath:key];
    }
};
struct Case { bool custom, transparent; const char* mode; };
std::vector<Case> Cases() {
    std::vector<Case> cases;
    for (bool custom : {false, true}) for (bool transparent : {false, true})
        for (const char* mode : {"control", "move-first", "move-second", "title", "contentView", "opaque", "level", "styleMask", "acceptsMouseMovedEvents", "titleVisibility", "delegate-prior", "releasedWhenClosed-prior", "owner-child", "close-other", "nested-replacement"})
            cases.push_back({custom, transparent, mode});
    for (bool custom : {false, true}) for (bool transparent : {false, true}) cases.push_back({custom, transparent, "owner-child-before"});
    return cases;
}
void Run(const Case& c) {
    uint32_t style = JALIUM_WINDOW_STYLE_CLOSABLE | JALIUM_WINDOW_STYLE_RESIZABLE |
        (c.custom ? JALIUM_WINDOW_STYLE_BORDERLESS : JALIUM_WINDOW_STYLE_TITLEBAR) |
        (c.transparent ? JALIUM_WINDOW_STYLE_TRANSPARENT : 0);
    std::unique_ptr<Window> other, replacement;
    bool closeOther = std::strcmp(c.mode, "close-other") == 0;
    if (closeOther) other = std::make_unique<Window>(style);
    JaliumCreationParent* parent = nil;
    if (std::strcmp(c.mode, "owner-child") == 0 || std::strcmp(c.mode, "owner-child-before") == 0) {
        parent = [[JaliumCreationParent alloc] initWithContentRect:NSMakeRect(50, 50, 240, 180)
            styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];
        parent.releasedWhenClosed = NO; parent.closeChild = true; parent.closeBefore = std::strcmp(c.mode, "owner-child-before") == 0;
    }
    bool fired = false, inside = false; unsigned moves = 0, properties = 0, closedNotes = 0;
    Observation observation;
    auto act = [&] {
        if (fired) return;
        Require(inside, "callback runs before the factory returns"); fired = true;
        if (closeOther) other->Close();
        else [observation.probe->window close];
        if (std::strcmp(c.mode, "nested-replacement") == 0) replacement = std::make_unique<Window>(style);
    };
    observation.probe->property = [&](NSString* key, bool prior) {
        if (!inside) return;
        ++properties;
        if ((!prior && [key isEqualToString:[NSString stringWithUTF8String:c.mode]]) ||
            (prior && [key isEqualToString:@"delegate"] && std::strcmp(c.mode, "delegate-prior") == 0) ||
            (prior && [key isEqualToString:@"releasedWhenClosed"] && std::strcmp(c.mode, "releasedWhenClosed-prior") == 0) ||
            (!prior && [key isEqualToString:@"title"] && std::strcmp(c.mode, "nested-replacement") == 0)) act();
    };
    observation.receive = [&](NSNotification* note) {
        if (!inside) return;
        if ([note.name isEqual:NSWindowWillCloseNotification]) ++closedNotes;
        if ([note.name isEqual:NSWindowDidMoveNotification]) {
            ++moves;
            if ((moves == 1 && (std::strcmp(c.mode, "move-first") == 0 || closeOther)) ||
                (moves == 2 && std::strcmp(c.mode, "move-second") == 0)) act();
        }
    };
    JaliumWindowParams p{}; p.title = reinterpret_cast<const JaliumUtf16Char*>(u"创建回调中文🙂 Miii");
    p.width = 360; p.height = 220; p.x = 117; p.y = 139; p.style = style; p.parentHandle = (intptr_t)(__bridge void*)parent;
    inside = true; auto* value = jalium_window_create(&p); inside = false;
    NSWindow* native = observation.probe->window;
    observation.receive = {}; observation.probe->property = {};
    bool control = std::strcmp(c.mode, "control") == 0;
    if (parent) fired = parent.additions != 0;
    Require(native != nil && (fired || control), "the real native callback was reached");
    if (control || closeOther) {
        Require(value != nullptr && native.delegate && !native.visible && properties > 0, "uncancelled factory returns a hidden live window");
        int width, height, x, y; jalium_window_get_client_size(value, &width, &height); jalium_window_get_position(value, &x, &y);
        Require(width == 360 && height == 220 && x == 117 && y == 139, "creation retains requested geometry");
        if (closeOther) Require(!other->value, "closing another registered window does not cancel this creation");
        jalium_window_destroy(value); value = nullptr;
    } else {
        if (value) { jalium_window_destroy(value); value = nullptr; throw std::runtime_error("closed creation returned a non-null handle"); }
        Require(closedNotes == 1 && !native.visible && native.delegate == nil, "cancelled native window closed once and detached before returning");
    }
    if (parent) Require(parent.childWindows.count == 0, "cancelled child detached from its external SDK parent");
    if (!replacement) replacement = std::make_unique<Window>(style);
    unsigned notifications = 0;
    jalium_window_set_event_callback(replacement->value, [](const JaliumPlatformEvent* event, void* context) {
        if (event->type == JALIUM_EVENT_LOW_MEMORY) ++*static_cast<unsigned*>(context);
    }, &notifications);
    jalium_apple_notify_lifecycle(JALIUM_EVENT_LOW_MEMORY);
    Require(notifications == 1, "fresh registered window receives one lifecycle event after cancellation");
    [NSNotificationCenter.defaultCenter postNotificationName:NSPreferredScrollerStyleDidChangeNotification object:nil];
    [NSNotificationCenter.defaultCenter postNotificationName:NSWindowDidMiniaturizeNotification object:native];
    Require(replacement->native && !replacement->native.visible, "retained cancelled native object cannot retire its replacement");
    if (parent) [parent close];
}
}
int main(int argc, char** argv) {
    @autoreleasepool {
        setvbuf(stdout, nullptr, _IONBF, 0); auto cases = Cases();
        if (argc == 2 && std::strcmp(argv[1], "--list") == 0) {
            for (size_t i = 0; i < cases.size(); ++i) std::printf("%zu %s %s %s\n", i, cases[i].custom ? "Custom" : "Native", cases[i].transparent ? "Transparent" : "Opaque", cases[i].mode);
            return 0;
        }
        if (jalium_platform_init() != JALIUM_OK) return 2;
        [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory]; if (!NSApp.running) [NSApp finishLaunching];
        int selected = argc == 2 && std::strncmp(argv[1], "--case=", 7) == 0 ? std::stoi(argv[1]+7) : -1;
        unsigned passed = 0, total = 0;
        for (size_t i = 0; i < cases.size(); ++i) {
            if (selected >= 0 && int(i) != selected) continue; ++total; const auto& c = cases[i];
            try { Run(c); ++passed; std::printf("PASS creation %zu %s/%s/%s\n", i, c.custom ? "Custom" : "Native", c.transparent ? "Transparent" : "Opaque", c.mode); }
            catch (const std::exception& error) { std::fprintf(stderr,"FAIL creation %zu %s/%s/%s: %s\n",i,c.custom ? "Custom":"Native",c.transparent ? "Transparent":"Opaque",c.mode,error.what()); }
        }
        jalium_platform_shutdown(); std::printf("macOS creation lifetime checks: %u/%u; own process, hidden windows, no input or system writes\n",passed,total); return total && passed==total ? 0 : 1;
    }
}
