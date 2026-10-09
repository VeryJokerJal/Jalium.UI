#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include "jalium_platform.h"
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <dlfcn.h>
#include <cstring>
#include <functional>
#include <exception>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>
#include "platform_apple_foreground_validation.h"

static void Require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}

static void Capture(const JaliumPlatformEvent* event, void* context)
{
    static_cast<std::vector<JaliumPlatformEvent>*>(context)->push_back(*event);
}

static bool Pump(const std::function<bool()>& condition, double seconds = 3)
{
    NSDate* deadline = [NSDate dateWithTimeIntervalSinceNow:seconds];
    do {
        jalium_platform_poll_events();
        [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.01]];
        if (condition()) return true;
    } while (deadline.timeIntervalSinceNow > 0);
    return condition();
}

static bool Settable(NSWindow* window, NSString* attribute)
{
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    return [window accessibilityIsAttributeSettable:attribute];
#pragma clang diagnostic pop
}

static id LegacyValue(NSWindow* window, NSString* attribute)
{
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    return [window accessibilityAttributeValue:attribute];
#pragma clang diagnostic pop
}

static void LegacyWrite(NSWindow* window, NSString* attribute, id value)
{
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    [window accessibilitySetValue:value forAttribute:attribute];
#pragma clang diagnostic pop
}

static void TryExcludedLegacyWrite(NSWindow* window, NSString* attribute, id value)
{
    @try { LegacyWrite(window, attribute, value); }
    @catch (NSException*) { /* AppKit may reject a non-settable attribute. */ }
}

@interface JaliumWindowAXZoomDelegate : NSObject<NSWindowDelegate>
@property(nonatomic) NSRect standardFrame;
@property(nonatomic, strong) id<NSWindowDelegate> originalDelegate;
@end
@implementation JaliumWindowAXZoomDelegate
- (NSRect)windowWillUseStandardFrame:(NSWindow*)window defaultFrame:(NSRect)frame {
    (void)window; (void)frame;
    return self.standardFrame;
}
- (BOOL)respondsToSelector:(SEL)selector {
    return [super respondsToSelector:selector] || [self.originalDelegate respondsToSelector:selector];
}
- (id)forwardingTargetForSelector:(SEL)selector {
    if ([self.originalDelegate respondsToSelector:selector]) return self.originalDelegate;
    return [super forwardingTargetForSelector:selector];
}
@end

struct BestFitZoom {
    NSWindow* window;
    JaliumWindowAXZoomDelegate* delegate;
    explicit BestFitZoom(NSWindow* native) : window(native), delegate([JaliumWindowAXZoomDelegate new])
    {
        delegate.originalDelegate = native.delegate;
        delegate.standardFrame = NSMakeRect(120, NSMaxY(native.screen.visibleFrame) - 360, 400, 300);
        native.delegate = delegate;
    }
    ~BestFitZoom() { window.delegate = delegate.originalDelegate; }
};

struct Fixture {
    JaliumPlatformWindow* window;
    NSWindow* native;
    std::vector<JaliumPlatformEvent> events;
    bool callbackRequested = false;
    NSRect callbackFrame = NSZeroRect;
    explicit Fixture(uint32_t style, int x = 200, int y = 220, int width = 640, int height = 480)
    {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Window AX writes validation");
        params.x = x; params.y = y; params.width = width; params.height = height;
        params.style = style;
        window = jalium_window_create(&params);
        Require(window != nullptr, "window creation failed");
        native = ((__bridge NSView*)(void*)jalium_window_get_native_handle(window)).window;
        jalium_window_set_event_callback(window, Capture, &events);
        jalium_apple_window_show(window, 0);
        Pump([] { return true; });
        events.clear();
    }
    void Destroy() { jalium_window_destroy(window); window = nullptr; }
    ~Fixture() { if (window) jalium_window_destroy(window); }
    NSRect ChangedFrame() const
    {
        NSRect frame = native.frame;
        frame.origin.x += 24; frame.origin.y += 16;
        frame.size.width += 40; frame.size.height += 30;
        return frame;
    }
};

// Retain a logical text selection while AppKit changes its actual responder.
// A content peer must not report focus merely because its window remains key.
struct ContentAccessibility {
    Fixture& fixture;
    NSView* view;
    NSAccessibilityElement* root;
    NSAccessibilityElement* child;
    bool focused = true;
    int calls = 0;
    std::function<void(int)> duringQuery;
    explicit ContentAccessibility(Fixture& target) : fixture(target)
    {
        view = (__bridge NSView*)(void*)jalium_window_get_native_handle(fixture.window);
        [fixture.native makeKeyAndOrderFront:nil];
        Require(Pump([&] { return NSApp.active && fixture.native.keyWindow; }),
            "content focus fixture did not become key in the foreground app");
        Require([fixture.native makeFirstResponder:view], "input view rejected focus");
        jalium_apple_window_set_accessibility(fixture.window, Query, this);
        root = view.accessibilityChildren.firstObject;
        child = root.accessibilityChildren.firstObject;
        Require(root && child, "content focus fixture did not expose its text peer");
    }
    ~ContentAccessibility()
    {
        if (fixture.window) jalium_apple_window_set_accessibility(fixture.window, nullptr, nullptr);
    }
    static int32_t Query(JaliumAccessibilityRequest* request, void* data)
    {
        auto& ax = *static_cast<ContentAccessibility*>(data);
        ++ax.calls;
        if (ax.duringQuery) ax.duringQuery(request->operation);
        if (request->nodeId != 1 && request->nodeId != 2) return 0;
        switch (request->operation) {
            case JALIUM_AX_INFO:
                request->role = request->nodeId == 1 ? 32 : 4;
                request->parentId = request->nodeId == 1 ? 0 : 1;
                request->childCount = request->nodeId == 1 ? 1 : 0;
                request->flags = JALIUM_AX_ENABLED;
                if (request->nodeId == 2) request->flags |= JALIUM_AX_FOCUSABLE | JALIUM_AX_TEXT |
                    JALIUM_AX_MULTILINE | (ax.focused ? JALIUM_AX_FOCUSED : 0);
                request->x = 20; request->y = 30; request->width = 100; request->height = 40;
                request->textCount = 7;
                return 1;
            case JALIUM_AX_CHILD:
                if (request->nodeId != 1 || request->index != 0) return 0;
                request->resultId = 2; return 1;
            case JALIUM_AX_FOCUS: request->resultId = ax.focused ? 2 : 0; return 1;
            case JALIUM_AX_ATTACHED: return 1;
            case JALIUM_AX_TEXT_SELECTION: request->textStart = 5; request->textLength = 2; return 1;
            case JALIUM_AX_STRING: {
                constexpr char16_t text[] = u"中文🙂 e\u0301";
                request->textCount = 7;
                if (!request->text) return 1;
                if (request->textCapacity < 7) return 0;
                std::memcpy(request->text, text, 7 * sizeof(uint16_t)); return 1;
            }
            default: return 0;
        }
    }
    void CheckFocused()
    {
        Require(view.accessibilityFocusedUIElement == child && root.accessibilityFocusedUIElement == child &&
            child.accessibilityFocusedUIElement == child && child.accessibilityFocused,
            "actual content focus did not reach the text peer");
        Require(NSEqualRanges(child.accessibilitySelectedTextRange, NSMakeRange(5, 2)),
            "focus query changed the complete combining-character selection");
    }
    void CheckExcluded()
    {
        Require(!view.accessibilityFocusedUIElement && !root.accessibilityFocusedUIElement &&
            !child.accessibilityFocusedUIElement && !child.accessibilityFocused,
            "retained managed focus was exposed without the actual input responder");
    }
};

static void CheckStateReplacement(Fixture& fixture, uint32_t style, JaliumWindowState requested)
{
    if (requested == JALIUM_WINDOW_STATE_NORMAL)
        jalium_window_set_state(fixture.window, JALIUM_WINDOW_STATE_MAXIMIZED);
    struct Context {
        Fixture* original;
        uint32_t style;
        JaliumWindowState requested;
        std::unique_ptr<Fixture> replacement;
        NSRect frame = NSZeroRect;
        bool reusedAddress = false;
    } context{&fixture, style, requested};
    jalium_window_set_event_callback(fixture.window, [](const JaliumPlatformEvent* event, void* data) {
        auto* context = static_cast<Context*>(data);
        if (event->type != JALIUM_EVENT_STATE_CHANGED ||
            event->stateChanged.newState != context->requested || context->replacement) return;
        auto* oldAddress = context->original->window;
        context->original->Destroy();
        context->replacement = std::make_unique<Fixture>(context->style, 500, 300, 560, 360);
        context->reusedAddress = context->replacement->window == oldAddress;
        context->frame = context->replacement->native.frame;
    }, &context);
    jalium_window_set_state(fixture.window, requested);
    Require(context.replacement != nullptr, "state notification did not create a replacement window");
    std::printf("Replacement fixture: state=%d reusedAddress=%d\n", requested, context.reusedAddress);
    Require(NSEqualRects(context.replacement->native.frame, context.frame),
        "the retired state request changed the replacement window frame");
    int width, height;
    jalium_window_get_client_size(context.replacement->window, &width, &height);
    Require(width == 560 && height == 360 &&
        jalium_window_get_state(context.replacement->window) == JALIUM_WINDOW_STATE_NORMAL,
        "the retired state request changed replacement size or state");
}

static NSRect RestoreBounds(Fixture& fixture)
{
    int x, y, width, height;
    Require(jalium_apple_window_get_restore_bounds(fixture.window, &x, &y, &width, &height) == 0,
        "restore bounds query failed");
    return NSMakeRect(x, y, width, height);
}

// Delay only this owned test window's request, so the state machine must wait
// for AppKit's acknowledgement regardless of the desktop animation timing.
static NSWindow* g_deferredWindow = nil;
static IMP g_originalDeminiaturize = nullptr;
static void DeferDeminiaturize(id object, SEL selector, id sender)
{
    if (object != g_deferredWindow)
        reinterpret_cast<void (*)(id, SEL, id)>(g_originalDeminiaturize)(object, selector, sender);
}

struct DeferredDeminiaturize {
    NSWindow* window;
    Method method;
    explicit DeferredDeminiaturize(NSWindow* native) : window(native)
    {
        Class nativeClass = object_getClass(native);
        Method inherited = class_getInstanceMethod(nativeClass, @selector(deminiaturize:));
        g_originalDeminiaturize = method_getImplementation(inherited);
        class_addMethod(nativeClass, @selector(deminiaturize:), g_originalDeminiaturize,
            method_getTypeEncoding(inherited));
        method = class_getInstanceMethod(nativeClass, @selector(deminiaturize:));
        g_deferredWindow = native;
        method_setImplementation(method, reinterpret_cast<IMP>(DeferDeminiaturize));
    }
    void Complete()
    {
        g_deferredWindow = nil;
        reinterpret_cast<void (*)(id, SEL, id)>(g_originalDeminiaturize)(window, @selector(deminiaturize:), nil);
    }
    ~DeferredDeminiaturize()
    {
        g_deferredWindow = nil;
        method_setImplementation(method, g_originalDeminiaturize);
    }
};

// Retain transition evidence in memory, emitting it only when an assertion
// fails. Observers and callbacks belong exclusively to this fixture window.
static bool g_traceSuccessfulRestores = false;
struct RestoreTrace {
    struct Sample {
        double time;
        const char* label;
        int state, requested;
        bool minimized;
        float scale;
        NSRect frame, restore;
        NSSize content;
    };
    Fixture& fixture;
    std::vector<Sample> samples;
    std::vector<id> observers;
    int requested = -1;
    int exceptions = std::uncaught_exceptions();
    explicit RestoreTrace(Fixture& target) : fixture(target)
    {
        jalium_window_set_event_callback(fixture.window, [](const JaliumPlatformEvent* event, void* data) {
            auto& trace = *static_cast<RestoreTrace*>(data);
            trace.fixture.events.push_back(*event);
            if (event->type == JALIUM_EVENT_STATE_CHANGED) trace.Record("state");
            else if (event->type == JALIUM_EVENT_RESIZE) trace.Record("resize");
            else if (event->type == JALIUM_EVENT_MOVE) trace.Record("move");
        }, this);
        auto* trace = this;
        for (NSNotificationName name : {NSWindowWillMiniaturizeNotification,
                NSWindowDidMiniaturizeNotification, NSWindowDidDeminiaturizeNotification}) {
            id observer = [NSNotificationCenter.defaultCenter addObserverForName:name object:fixture.native
                queue:nil usingBlock:^(NSNotification* notification) {
                    trace->Record([notification.name isEqual:NSWindowWillMiniaturizeNotification] ? "will-mini"
                        : [notification.name isEqual:NSWindowDidMiniaturizeNotification] ? "did-mini" : "did-demini");
                }];
            observers.push_back(observer);
        }
        Record("begin");
    }
    void Record(const char* label)
    {
        samples.push_back({NSProcessInfo.processInfo.systemUptime, label,
            jalium_window_get_state(fixture.window), requested, fixture.native.miniaturized != NO,
            jalium_window_get_dpi_scale(fixture.window),
            fixture.native.frame, RestoreBounds(fixture), fixture.native.contentView.bounds.size});
    }
    void Request(JaliumWindowState state)
    {
        requested = state; Record("request"); jalium_window_set_state(fixture.window, state);
    }
    ~RestoreTrace()
    {
        for (id observer : observers) [NSNotificationCenter.defaultCenter removeObserver:observer];
        jalium_window_set_event_callback(fixture.window, Capture, &fixture.events);
        if (std::uncaught_exceptions() <= exceptions && !g_traceSuccessfulRestores) return;
        for (const auto& s : samples)
            std::fprintf(stderr, "Restore trace %.6f %s request=%d state=%d mini=%d scale=%.1f frame=(%.0f,%.0f,%.0f,%.0f) content=(%.0f,%.0f) restore=(%.0f,%.0f,%.0f,%.0f)\n",
                s.time, s.label, s.requested, s.state, s.minimized, s.scale,
                s.frame.origin.x, s.frame.origin.y, s.frame.size.width, s.frame.size.height,
                s.content.width, s.content.height, s.restore.origin.x, s.restore.origin.y,
                s.restore.size.width, s.restore.size.height);
    }
};

struct WindowNotification {
    id observer;
    WindowNotification(Fixture& fixture, NSNotificationName name, const std::function<void()>& action)
    {
        auto callback = action;
        observer = [NSNotificationCenter.defaultCenter addObserverForName:name object:fixture.native
            queue:nil usingBlock:^(NSNotification*) { callback(); }];
    }
    ~WindowNotification() { [NSNotificationCenter.defaultCenter removeObserver:observer]; }
};

struct FrontRequests;
static FrontRequests* g_frontRequests = nullptr;
static IMP g_originalKeyFront = nullptr, g_originalMakeMain = nullptr;
static void RecordKeyFront(id object, SEL selector, id sender);
static void RecordMakeMain(id object, SEL selector);

// Count explicit framework selection separately from AppKit's own restore
// selection, which may occur before did-deminiaturize. Keep native requests
// observable and invoke every original method; foreground ownership remains
// a system decision and is not fabricated by this probe.
struct FrontRequests {
    NSWindow* window;
    Method keyMethod, mainMethod;
    void* platformImage = nullptr;
    int key = 0, main = 0;
    int externalKey = 0, externalMain = 0;
    int premature = 0;
    bool waiting = false;
    explicit FrontRequests(NSWindow* native) : window(native)
    {
        Dl_info image{};
        Require(dladdr(reinterpret_cast<void*>(&jalium_window_activate), &image) && image.dli_fbase,
            "selection probe could not resolve the platform library");
        platformImage = image.dli_fbase;
        Class cls = object_getClass(native);
        auto install = [&](SEL selector, IMP replacement, IMP& original) {
            Method inherited = class_getInstanceMethod(cls, selector);
            original = method_getImplementation(inherited);
            class_addMethod(cls, selector, original, method_getTypeEncoding(inherited));
            Method method = class_getInstanceMethod(cls, selector);
            method_setImplementation(method, replacement);
            return method;
        };
        keyMethod = install(@selector(makeKeyAndOrderFront:), reinterpret_cast<IMP>(RecordKeyFront), g_originalKeyFront);
        mainMethod = install(@selector(makeMainWindow), reinterpret_cast<IMP>(RecordMakeMain), g_originalMakeMain);
        g_frontRequests = this;
    }
    bool IsPlatformCaller(void* address) const
    {
        Dl_info caller{};
        return dladdr(address, &caller) && caller.dli_fbase == platformImage;
    }
    ~FrontRequests()
    {
        method_setImplementation(keyMethod, g_originalKeyFront);
        method_setImplementation(mainMethod, g_originalMakeMain);
        g_frontRequests = nullptr;
    }
};
static void RecordKeyFront(id object, SEL selector, id sender)
{
    if (g_frontRequests && object == g_frontRequests->window) {
        bool platform = g_frontRequests->IsPlatformCaller(
            __builtin_extract_return_addr(__builtin_return_address(0)));
        if (platform) ++g_frontRequests->key;
        else ++g_frontRequests->externalKey;
        if (platform && g_frontRequests->waiting) {
            ++g_frontRequests->premature;
            if (std::getenv("JALIUM_WINDOW_ACTIVATION_TRACE"))
                std::fprintf(stderr, "Premature key/front request: mini=%d visible=%d stack=%s\n",
                    ((NSWindow*)object).miniaturized, ((NSWindow*)object).visible,
                    [NSThread.callStackSymbols componentsJoinedByString:@" | "].UTF8String);
        }
    }
    reinterpret_cast<void (*)(id, SEL, id)>(g_originalKeyFront)(object, selector, sender);
}
static void RecordMakeMain(id object, SEL selector)
{
    if (g_frontRequests && object == g_frontRequests->window) {
        bool platform = g_frontRequests->IsPlatformCaller(
            __builtin_extract_return_addr(__builtin_return_address(0)));
        if (platform) ++g_frontRequests->main;
        else ++g_frontRequests->externalMain;
        if (platform && g_frontRequests->waiting) {
            ++g_frontRequests->premature;
            if (std::getenv("JALIUM_WINDOW_ACTIVATION_TRACE"))
                std::fprintf(stderr, "Premature main request: mini=%d visible=%d stack=%s\n",
                    ((NSWindow*)object).miniaturized, ((NSWindow*)object).visible,
                    [NSThread.callStackSymbols componentsJoinedByString:@" | "].UTF8String);
        }
    }
    reinterpret_cast<void (*)(id, SEL)>(g_originalMakeMain)(object, selector);
}

// Model AppKit declining one owned window's request without a will/did pair.
static IMP g_originalMiniaturize = nullptr;
static NSWindow* g_rejectedMiniaturizeWindow = nil;
static void RejectMiniaturize(id window, SEL selector, id sender)
{
    if (window != g_rejectedMiniaturizeWindow)
        reinterpret_cast<void (*)(id, SEL, id)>(g_originalMiniaturize)(window, selector, sender);
}
struct RejectedMinimization {
    Method method = class_getInstanceMethod(NSWindow.class, @selector(miniaturize:));
    explicit RejectedMinimization(NSWindow* native)
    {
        g_rejectedMiniaturizeWindow = native;
        g_originalMiniaturize = method_setImplementation(method, reinterpret_cast<IMP>(RejectMiniaturize));
    }
    ~RejectedMinimization()
    {
        method_setImplementation(method, g_originalMiniaturize);
        g_rejectedMiniaturizeWindow = nil;
    }
};

struct ControlledFullScreen;
static ControlledFullScreen* g_fullScreen = nullptr;
static IMP g_originalFullScreenToggle = nullptr, g_originalStyleMask = nullptr;
static void ControlledFullScreenToggle(id object, SEL selector, id sender);
static NSWindowStyleMask ControlledStyleMask(id object, SEL selector);

// Failure recovery is a delegate state-machine check. Only this owned window's
// fullscreen request and mask are controlled; no real Spaces transition is
// claimed. All other AppKit calls and windows keep their original behavior.
struct ControlledFullScreen {
    NSWindow* window;
    Method toggleMethod, maskMethod;
    bool fullScreen = false;
    int toggles = 0;
    explicit ControlledFullScreen(NSWindow* native) : window(native)
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
        toggleMethod = install(@selector(toggleFullScreen:), reinterpret_cast<IMP>(ControlledFullScreenToggle), g_originalFullScreenToggle);
        maskMethod = install(@selector(styleMask), reinterpret_cast<IMP>(ControlledStyleMask), g_originalStyleMask);
        g_fullScreen = this;
    }
    ~ControlledFullScreen()
    {
        method_setImplementation(toggleMethod, g_originalFullScreenToggle);
        method_setImplementation(maskMethod, g_originalStyleMask);
        g_fullScreen = nullptr;
    }
    void Will()
    {
        auto name = fullScreen ? NSWindowWillExitFullScreenNotification : NSWindowWillEnterFullScreenNotification;
        auto note = [NSNotification notificationWithName:name object:window];
        if (fullScreen) [window.delegate windowWillExitFullScreen:note];
        else [window.delegate windowWillEnterFullScreen:note];
    }
    void Complete()
    {
        fullScreen = !fullScreen;
        auto name = fullScreen ? NSWindowDidEnterFullScreenNotification : NSWindowDidExitFullScreenNotification;
        auto note = [NSNotification notificationWithName:name object:window];
        if (fullScreen) [window.delegate windowDidEnterFullScreen:note];
        else [window.delegate windowDidExitFullScreen:note];
    }
    void Fail()
    {
        if (fullScreen) [window.delegate windowDidFailToExitFullScreen:window];
        else [window.delegate windowDidFailToEnterFullScreen:window];
    }
};
static void ControlledFullScreenToggle(id object, SEL selector, id sender)
{
    if (g_fullScreen && object == g_fullScreen->window) {
        ++g_fullScreen->toggles;
        g_fullScreen->Will();
    } else reinterpret_cast<void (*)(id, SEL, id)>(g_originalFullScreenToggle)(object, selector, sender);
}
static NSWindowStyleMask ControlledStyleMask(id object, SEL selector)
{
    auto original = reinterpret_cast<NSWindowStyleMask (*)(id, SEL)>(g_originalStyleMask)(object, selector);
    if (g_fullScreen && object == g_fullScreen->window && g_fullScreen->fullScreen)
        return original | NSWindowStyleMaskFullScreen;
    return original;
}

int main(int argc, char** argv)
{
    // Preserve completed case results even if a later desktop wait times out.
    std::setvbuf(stdout, nullptr, _IOLBF, 0);
    g_traceSuccessfulRestores = argc == 2 && std::strcmp(argv[1], "--delayed-restore-trace") == 0;
    bool delayedRestoreOnly = g_traceSuccessfulRestores ||
        (argc == 2 && std::strcmp(argv[1], "--delayed-restore-only") == 0);
    bool replacementOnly = argc == 2 && std::strcmp(argv[1], "--replacement-only") == 0;
    bool minimizeTransitionOnly = argc == 2 && std::strcmp(argv[1], "--minimize-transition-only") == 0;
    bool activationTransitionOnly = argc == 2 && std::strcmp(argv[1], "--activation-transition-only") == 0;
    bool fullScreenFailureOnly = argc == 2 && std::strcmp(argv[1], "--fullscreen-failure-only") == 0;
    bool fullScreenFocusOnly = argc == 2 && std::strcmp(argv[1], "--fullscreen-focus-only") == 0;
    bool contentFocusOnly = argc == 2 && std::strcmp(argv[1], "--content-focus-only") == 0;
    bool selectionProbeOnly = argc == 2 && std::strcmp(argv[1], "--selection-probe-only") == 0;
    bool frameOnly = (argc == 2 && std::strcmp(argv[1], "--frame-only") == 0) ||
        [[NSBundle.mainBundle objectForInfoDictionaryKey:@"JaliumValidationFrameOnly"] boolValue];
    const char* caseName = argc == 2 && std::strncmp(argv[1], "--case-name=", 12) == 0 ? argv[1] + 12 : nullptr;
    if (argc != 1 && !delayedRestoreOnly && !replacementOnly && !minimizeTransitionOnly && !activationTransitionOnly && !fullScreenFailureOnly && !fullScreenFocusOnly && !contentFocusOnly && !selectionProbeOnly && !frameOnly && !caseName) return 2;
    @autoreleasepool {
        Require(jalium_platform_init() == 0, "platform initialization failed");
        bool foregroundHost = UsesForegroundValidationHost();
        [NSApp setActivationPolicy:foregroundHost ? NSApplicationActivationPolicyRegular : NSApplicationActivationPolicyAccessory];
        [NSApp finishLaunching];
        if (foregroundHost && !AwaitForegroundValidation(@"Window AX · 前台回归")) {
            jalium_platform_shutdown();
            return 3;
        }
        if (contentFocusOnly && !foregroundHost) {
            std::fprintf(stderr, "Content focus checks require the bundled foreground validation host; no assertions ran.\n");
            jalium_platform_shutdown();
            return 3;
        }
        if (g_traceSuccessfulRestores) {
            for (NSScreen* screen in NSScreen.screens)
                std::fprintf(stderr, "Screen scale=%.1f frame=%s work=%s\n", screen.backingScaleFactor,
                    NSStringFromRect(screen.frame).UTF8String, NSStringFromRect(screen.visibleFrame).UTF8String);
        }
        constexpr uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
            JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE;
        const uint32_t custom = (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS;
        int passed = 0, total = 0;
        for (uint32_t style : {regular, custom}) {
            auto run = [&](const char* name, const std::function<void(Fixture&)>& check) {
                if (!foregroundHost && std::strstr(name, "content AX focus") != nullptr) return;
                if (frameOnly && std::strstr(name, "frame") == nullptr &&
                    std::strstr(name, "position") == nullptr && std::strstr(name, "fixed window") == nullptr &&
                    std::strstr(name, "legacy") == nullptr && std::strstr(name, "resize callback") == nullptr &&
                    std::strstr(name, "foreign-thread") == nullptr) return;
                if (caseName && std::strcmp(caseName, name) != 0) return;
                if (delayedRestoreOnly &&
                    std::strcmp(name, "delayed deminiaturization drains the latest state request") != 0) return;
                if (replacementOnly && std::strstr(name, "replacement window") == nullptr) return;
                if (minimizeTransitionOnly && std::strstr(name, "minimize completion") == nullptr) return;
                if (activationTransitionOnly && std::strstr(name, "activation completion") == nullptr) return;
                if (fullScreenFailureOnly && std::strstr(name, "fullscreen failure") == nullptr) return;
                if (fullScreenFocusOnly && std::strstr(name, "fullscreen focus") == nullptr) return;
                if (contentFocusOnly && std::strstr(name, "content AX focus") == nullptr) return;
                if (selectionProbeOnly && std::strstr(name, "selection probe") == nullptr) return;
                ++total;
                try {
                    Fixture fixture(style);
                    check(fixture);
                    ++passed;
                    std::printf("PASS %s: %s\n", style == regular ? "Native" : "Custom", name);
                } catch (const std::exception& error) {
                    std::fprintf(stderr, "FAIL %s: %s: %s\n", style == regular ? "Native" : "Custom", name, error.what());
                }
            };
            run("content AX focus follows the actual input view", [](Fixture& f) {
                ContentAccessibility ax(f); ax.CheckFocused();
            });
            run("content AX focus excludes the default window responder", [](Fixture& f) {
                ContentAccessibility ax(f);
                Require([f.native makeFirstResponder:nil] && f.native.firstResponder == f.native,
                    "default window responder was not installed");
                ax.CheckExcluded();
                Require(f.native.accessibilityFocusedUIElement == f.native,
                    "the actual window responder was replaced in AppKit's focus query");
                [f.native makeFirstResponder:ax.view]; ax.CheckFocused();
            });
            run("content AX focus preserves a native text responder", [](Fixture& f) {
                ContentAccessibility ax(f);
                NSTextView* text = [[NSTextView alloc] initWithFrame:NSMakeRect(0, 0, 20, 20)];
                [ax.view addSubview:text];
                Require([f.native makeFirstResponder:text], "native text responder was rejected");
                ax.CheckExcluded();
                Require(f.native.firstResponder == text && f.native.accessibilityFocusedUIElement == text,
                    "managed focus replaced the native text responder");
                [f.native makeFirstResponder:ax.view]; [text removeFromSuperview]; ax.CheckFocused();
            });
            run("content AX focus excludes a disabled window", [](Fixture& f) {
                ContentAccessibility ax(f); jalium_window_set_enabled(f.window, 0); ax.CheckExcluded();
                jalium_window_set_enabled(f.window, 1); [f.native makeKeyWindow];
                [f.native makeFirstResponder:ax.view]; ax.CheckFocused();
            });
            run("content AX focus excludes a hidden window", [](Fixture& f) {
                ContentAccessibility ax(f); jalium_window_hide(f.window); ax.CheckExcluded();
                jalium_apple_window_show(f.window, 0); [f.native makeKeyWindow];
                [f.native makeFirstResponder:ax.view]; ax.CheckFocused();
            });
            run("content AX focus excludes a window that lost key selection", [style](Fixture& f) {
                ContentAccessibility ax(f); Fixture other(style); [other.native makeKeyWindow];
                Require(other.native.keyWindow && !f.native.keyWindow, "second window did not gain key selection");
                ax.CheckExcluded(); [f.native makeKeyWindow]; ax.CheckFocused();
            });
            run("content AX focus excludes an unfocused managed provider", [](Fixture& f) {
                ContentAccessibility ax(f); ax.focused = false; ax.CheckExcluded();
                ax.focused = true; ax.CheckFocused();
            });
            for (int operation : {JALIUM_AX_FOCUS, JALIUM_AX_INFO}) for (int action = 0; action < 4; ++action) {
                const char* actions[] = {"responder replacement", "hide", "disable", "close"};
                std::string name = std::string("content AX focus rechecks ") + actions[action] +
                    (operation == JALIUM_AX_FOCUS ? " after focus query" : " after focused flag query");
                run(name.c_str(), [=](Fixture& f) {
                    ContentAccessibility ax(f); bool changed = false;
                    ax.duringQuery = [&](int currentOperation) {
                        if (changed || currentOperation != operation) return;
                        changed = true;
                        if (action == 0) [f.native makeFirstResponder:nil];
                        else if (action == 1) jalium_window_hide(f.window);
                        else if (action == 2) jalium_window_set_enabled(f.window, 0);
                        else f.Destroy();
                    };
                    if (operation == JALIUM_AX_FOCUS)
                        Require(!ax.view.accessibilityFocusedUIElement, "a reentrant provider retained stale content focus");
                    else Require(!ax.child.accessibilityFocused, "a reentrant provider retained a stale focused flag");
                    Require(changed, "reentrant provider did not run");
                    ax.duringQuery = nullptr; ax.CheckExcluded();
                });
            }
            run("content AX focus rejects detached peers", [](Fixture& f) {
                ContentAccessibility ax(f); jalium_apple_window_set_accessibility(f.window, nullptr, nullptr);
                Require(!ax.root.accessibilityFocusedUIElement && !ax.child.accessibilityFocused,
                    "detached content retained accessibility focus");
            });
            run("content AX focus rejects closed peers", [](Fixture& f) {
                ContentAccessibility ax(f); f.Destroy(); ax.CheckExcluded();
            });
            run("content AX focus rejects foreign thread queries", [](Fixture& f) {
                ContentAccessibility ax(f); int calls = ax.calls; bool rejected = false;
                std::thread thread([&] {
                    @autoreleasepool {
                        rejected = !ax.view.accessibilityFocusedUIElement && !ax.root.accessibilityFocusedUIElement &&
                            !ax.child.accessibilityFocusedUIElement && !ax.child.accessibilityFocused;
                    }
                });
                thread.join();
                Require(rejected && ax.calls == calls, "foreign thread entered the managed focus provider");
                ax.CheckFocused();
            });
            run("selection probe distinguishes explicit platform requests and native requests", [](Fixture& f) {
                FrontRequests front(f.native);
                front.waiting = true;
                [f.native makeKeyAndOrderFront:nil];
                [f.native makeMainWindow];
                Require(front.key == 0 && front.main == 0 && front.premature == 0 &&
                    front.externalKey > 0 && front.externalMain > 0,
                    "native selection was attributed to an explicit platform request");
                Require(jalium_window_activate(f.window) == 0 && front.key == 1 && front.premature > 0,
                    "selection probe did not detect an explicit platform request before acknowledgement");
                front.waiting = false;
            });
            run("frame updates native and published user geometry", [](Fixture& f) {
                NSRect requested = f.ChangedFrame();
                [f.native setAccessibilityFrame:requested];
                Require(NSEqualRects(f.native.frame, requested), "AX frame did not update the real window");
                Require(NSEqualRects(f.native.accessibilityFrame, f.native.frame), "AX frame disagrees with the window");
                bool geometryPublished = Pump([&] {
                    bool userResize = false, moved = false;
                    for (const auto& event : f.events) {
                        userResize |= event.type == JALIUM_EVENT_RESIZE && event.resize.isUserInitiated;
                        moved |= event.type == JALIUM_EVENT_MOVE;
                    }
                    return userResize && moved;
                });
                if (!geometryPublished)
                    for (const auto& event : f.events)
                        std::fprintf(stderr, "AX geometry event type=%d resize=%dx%d user=%d move=%d,%d\n",
                            event.type, event.resize.width, event.resize.height, event.resize.isUserInitiated,
                            event.move.x, event.move.y);
                Require(geometryPublished, "AX geometry did not publish user resize and move events");
                int width, height;
                jalium_window_get_client_size(f.window, &width, &height);
                NSSize content = f.native.contentView.bounds.size;
                Require(width == std::lround(content.width * f.native.backingScaleFactor) &&
                    height == std::lround(content.height * f.native.backingScaleFactor), "client geometry is stale");
                int x, y;
                jalium_window_get_position(f.window, &x, &y);
                const JaliumPlatformEvent* lastMove = nullptr;
                for (const auto& event : f.events)
                    if (event.type == JALIUM_EVENT_MOVE) lastMove = &event;
                Require(lastMove && lastMove->move.x == x && lastMove->move.y == y,
                    "AX combined size/position published an intermediate top-left position");
            });
            run("frame respects client minimum and maximum", [](Fixture& f) {
                Require(jalium_window_set_min_max_size(f.window, 480, 360, 900, 720) == 0, "size constraints failed");
                NSRect requested = f.native.frame;
                requested.size = NSMakeSize(40, 40);
                [f.native setAccessibilityFrame:requested];
                int width, height;
                jalium_window_get_client_size(f.window, &width, &height);
                Require(width == 480 && height == 360, "AX size did not clamp to the client minimum");
                requested = f.native.frame; requested.size = NSMakeSize(1000, 1000);
                [f.native setAccessibilityFrame:requested];
                jalium_window_get_client_size(f.window, &width, &height);
                Require(width == 900 && height == 720, "AX size did not clamp to the client maximum");
            });
            run("fixed window remains movable without resizing", [style](Fixture& f) {
                jalium_apple_window_set_style(f.window, style & ~JALIUM_WINDOW_STYLE_RESIZABLE);
                NSRect before = f.native.frame, requested = f.ChangedFrame();
                [f.native setAccessibilityFrame:requested];
                Require(NSEqualSizes(f.native.frame.size, before.size), "AX resized a fixed window");
                Require(NSEqualPoints(f.native.frame.origin, requested.origin), "fixed window lost position editing");
            });
            run("disabled frame write is rejected", [](Fixture& f) {
                jalium_window_set_enabled(f.window, 0);
                NSRect before = f.native.frame;
                Require(![f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityFrame:)], "disabled frame advertised writable");
                [f.native setAccessibilityFrame:f.ChangedFrame()];
                Require(NSEqualRects(f.native.frame, before), "AX changed a disabled window");
            });
            run("hidden frame write is rejected", [](Fixture& f) {
                jalium_window_hide(f.window);
                NSRect before = f.native.frame;
                Require(![f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityFrame:)], "hidden frame advertised writable");
                [f.native setAccessibilityFrame:f.ChangedFrame()];
                Require(NSEqualRects(f.native.frame, before), "AX changed a hidden window");
            });
            run("destroyed frame reference rejects writes", [](Fixture& f) {
                f.Destroy();
                NSRect before = f.native.frame;
                Require(![f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityFrame:)], "destroyed frame advertised writable");
                [f.native setAccessibilityFrame:f.ChangedFrame()];
                Require(NSEqualRects(f.native.frame, before), "AX changed a destroyed window");
            });
            run("missing minimize capability rejects writes", [style](Fixture& f) {
                jalium_apple_window_set_style(f.window, style & ~JALIUM_WINDOW_STYLE_MINIMIZABLE);
                Require(![f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityMinimized:)], "fixed minimize advertised writable");
                [f.native setAccessibilityMinimized:YES];
                Pump([] { return true; });
                Require(!f.native.miniaturized, "AX bypassed the minimize capability");
            });
            run("disabled minimize write is rejected", [](Fixture& f) {
                jalium_window_set_enabled(f.window, 0);
                bool allowed = [f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityMinimized:)];
                [f.native setAccessibilityMinimized:YES];
                Pump([] { return true; });
                Require(!f.native.miniaturized, "AX minimized a disabled window");
                Require(!allowed, "disabled minimize advertised writable");
            });
            run("hidden minimize write is rejected", [](Fixture& f) {
                jalium_window_hide(f.window);
                bool allowed = [f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityMinimized:)];
                [f.native setAccessibilityMinimized:YES];
                Pump([] { return true; });
                Require(!f.native.miniaturized, "AX minimized a hidden window");
                Require(!allowed, "hidden minimize advertised writable");
            });
            run("minimize and restore preserve maximized state", [](Fixture& f) {
                NSRect restore = RestoreBounds(f);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                [f.native setAccessibilityMinimized:YES];
                Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MINIMIZED; }), "AX did not minimize the window");
                Require(f.native.isAccessibilityMinimized && f.native.miniaturized, "AX minimized state disagrees");
                Require([f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityMinimized:)], "minimized window cannot restore");
                [f.native setAccessibilityMinimized:NO];
                Require(Pump([&] { return !f.native.miniaturized && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED; }), "AX restore lost maximized state");
                Require(NSEqualRects(RestoreBounds(f), restore), "AX restore replaced normal restore bounds");
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                Require(NSEqualRects(RestoreBounds(f), restore), "return to normal lost original geometry");
            });
            for (auto initial : {JALIUM_WINDOW_STATE_NORMAL, JALIUM_WINDOW_STATE_MAXIMIZED})
                for (auto requested : {JALIUM_WINDOW_STATE_NORMAL, JALIUM_WINDOW_STATE_MAXIMIZED})
                    for (bool nativeAction : {false, true}) {
                        char name[160];
                        std::snprintf(name, sizeof(name), "minimize completion preserves %s request from %s via %s",
                            requested == JALIUM_WINDOW_STATE_NORMAL ? "restore" : "maximize",
                            initial == JALIUM_WINDOW_STATE_NORMAL ? "normal" : "maximized",
                            nativeAction ? "native action" : "platform API");
                        run(name, [=](Fixture& f) {
                            NSRect restore = RestoreBounds(f);
                            RestoreTrace trace(f); trace.Request(initial);
                            bool entered = false, completed = false, geometryWritable = false;
                            WindowNotification began(f, NSWindowWillMiniaturizeNotification, [&] {
                                if (entered) return;
                                entered = true;
                                geometryWritable = Settable(f.native, NSAccessibilityPositionAttribute) ||
                                    Settable(f.native, NSAccessibilitySizeAttribute);
                                trace.Request(JALIUM_WINDOW_STATE_NORMAL);
                                trace.Request(JALIUM_WINDOW_STATE_MAXIMIZED);
                                trace.Request(requested);
                            });
                            WindowNotification ended(f, NSWindowDidMiniaturizeNotification, [&] { completed = true; });
                            if (nativeAction) [f.native performMiniaturize:nil];
                            else trace.Request(JALIUM_WINDOW_STATE_MINIMIZED);
                            Require(Pump([&] { return completed; }), "real minimization did not acknowledge completion");
                            Require(entered, "real minimization did not begin");
                            Require(Pump([&] { return jalium_window_get_state(f.window) == requested && !f.native.miniaturized; }),
                                "latest request was lost while minimizing");
                            Require(!geometryWritable, "in-flight minimization exposed writable geometry");
                            Require(NSEqualRects(RestoreBounds(f), restore), "minimize completion replaced normal bounds");
                            trace.Request(JALIUM_WINDOW_STATE_NORMAL);
                            Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL &&
                                !f.native.miniaturized; }), "final normal state did not complete");
                            Require(NSEqualRects(RestoreBounds(f), restore), "final restored geometry differs");
                        });
                    }
            for (auto requested : {JALIUM_WINDOW_STATE_NORMAL, JALIUM_WINDOW_STATE_MAXIMIZED}) {
                run(requested == JALIUM_WINDOW_STATE_NORMAL
                    ? "minimize completion callback queues restore" : "minimize completion callback queues maximize", [=](Fixture& f) {
                    NSRect restore = RestoreBounds(f);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    struct Context { Fixture* fixture; JaliumWindowState requested; bool called = false; } context{&f, requested};
                    jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* data) {
                        auto& c = *static_cast<Context*>(data); c.fixture->events.push_back(*event);
                        if (event->type != JALIUM_EVENT_STATE_CHANGED ||
                            event->stateChanged.newState != JALIUM_WINDOW_STATE_MINIMIZED || c.called) return;
                        c.called = true; jalium_window_set_state(c.fixture->window, c.requested);
                    }, &context);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MINIMIZED);
                    bool completed = Pump([&] { return context.called && !f.native.miniaturized &&
                        jalium_window_get_state(f.window) == requested; });
                    jalium_window_set_event_callback(f.window, Capture, &f.events);
                    Require(completed && NSEqualRects(RestoreBounds(f), restore), "completion callback lost requested state or restore bounds");
                });
            }
            run("minimize completion rejection releases later requests", [](Fixture& f) {
                NSRect restore = RestoreBounds(f); RejectedMinimization rejected(f.native);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MINIMIZED);
                Require(!f.native.miniaturized && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL &&
                    Settable(f.native, NSAccessibilityPositionAttribute), "rejected minimization left its transition active");
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED,
                    "later state request remained queued after native rejection");
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                Require(NSEqualRects(RestoreBounds(f), restore), "rejected minimization lost normal geometry");
            });
            run("minimize completion retains replacement after a start callback closes its window", [=](Fixture& f) {
                std::unique_ptr<Fixture> replacement; NSRect saved;
                WindowNotification began(f, NSWindowWillMiniaturizeNotification, [&] {
                    if (replacement) return;
                    f.Destroy(); replacement = std::make_unique<Fixture>(style, 400, 320, 520, 360); saved = replacement->native.frame;
                });
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MINIMIZED);
                Require(Pump([&] { return replacement != nullptr; }), "minimize start did not create replacement");
                Require(NSEqualRects(replacement->native.frame, saved) &&
                    jalium_window_get_state(replacement->window) == JALIUM_WINDOW_STATE_NORMAL,
                    "retired minimization changed replacement geometry or state");
            });
            for (auto initial : {JALIUM_WINDOW_STATE_NORMAL, JALIUM_WINDOW_STATE_MAXIMIZED})
                for (bool completionCallback : {false, true})
                    for (bool mainOnly : {false, true}) {
                        char name[160];
                        std::snprintf(name, sizeof(name), "activation completion preserves %s from %s via %s",
                            completionCallback ? "completion callback" : "start callback",
                            initial == JALIUM_WINDOW_STATE_NORMAL ? "normal" : "maximized",
                            mainOnly ? "AX main" : "platform Activate");
                        run(name, [=](Fixture& f) {
                            NSRect restore = RestoreBounds(f);
                            jalium_window_set_state(f.window, initial);
                            FrontRequests front(f.native);
                            bool entered = false, acknowledged = false;
                            int earlyRequests = -1;
                            WindowNotification requested(f, completionCallback ? NSWindowDidMiniaturizeNotification : NSWindowWillMiniaturizeNotification, [&] {
                                if (entered) return;
                                entered = true;
                                if (mainOnly) [f.native setAccessibilityMain:YES];
                                else jalium_window_activate(f.window);
                                earlyRequests = front.key + front.main;
                            });
                            WindowNotification completed(f, NSWindowDidMiniaturizeNotification, [&] { acknowledged = true; });
                            WindowNotification restored(f, NSWindowDidDeminiaturizeNotification, [&] { front.waiting = false; });
                            front.waiting = true;
                            jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MINIMIZED);
                            bool settled = Pump([&] { return acknowledged && !f.native.miniaturized &&
                                jalium_window_get_state(f.window) == initial && (mainOnly ? front.main > 0 : front.key > 0); });
                            if (!entered || earlyRequests != 0 || front.premature != 0 || !settled)
                                std::fprintf(stderr, "Activation trace: entered=%d early=%d premature=%d waiting=%d acknowledged=%d settled=%d key=%d main=%d externalKey=%d externalMain=%d mini=%d visible=%d state=%d expected=%d\n",
                                    entered, earlyRequests, front.premature, front.waiting, acknowledged, settled,
                                    front.key, front.main, front.externalKey, front.externalMain,
                                    f.native.miniaturized, f.native.visible,
                                    jalium_window_get_state(f.window), initial);
                            Require(entered && earlyRequests == 0 && front.premature == 0, "selection was requested before minimization completed");
                            Require(settled, "activation lost the restore or selection request");
                            Require(mainOnly ? front.main == 1 && front.key == 0 : front.key == 1,
                                "queued selection duplicated or AX main changed key selection");
                            Require(NSEqualRects(RestoreBounds(f), restore), "queued activation lost normal bounds");
                        });
                    }
            for (int cancellation : {0, 1, 2})
                for (bool mainOnly : {false, true}) {
                    char name[160];
                    std::snprintf(name, sizeof(name), "activation completion cancelled by %s via %s",
                        cancellation == 0 ? "hide" : cancellation == 1 ? "disable" : "new minimize", mainOnly ? "AX main" : "platform Activate");
                    run(name, [=](Fixture& f) {
                        FrontRequests front(f.native);
                        bool entered = false, acknowledged = false;
                        WindowNotification requested(f, NSWindowWillMiniaturizeNotification, [&] {
                            if (entered) return;
                            entered = true;
                            if (mainOnly) [f.native setAccessibilityMain:YES];
                            else jalium_window_activate(f.window);
                            if (cancellation == 0) jalium_window_hide(f.window);
                            else if (cancellation == 1) jalium_window_set_enabled(f.window, 0);
                            else jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MINIMIZED);
                        });
                        WindowNotification completed(f, NSWindowDidMiniaturizeNotification, [&] { acknowledged = true; });
                        jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MINIMIZED);
                        Require(Pump([&] { return acknowledged; }), "cancelled minimization did not complete");
                        Pump([] { return false; }, 0.7);
                        Require(entered && front.key == 0 && front.main == 0, "cancelled activation still selected its window");
                        if (cancellation == 0) Require(!f.native.visible, "queued restore showed a hidden window");
                        if (cancellation == 2) Require(f.native.miniaturized, "new minimize did not cancel pending restore");
                        if (cancellation == 1) jalium_window_set_enabled(f.window, 1);
                        front.key = front.main = 0;
                        Require(jalium_window_activate(f.window) == 0, "later explicit activation was rejected");
                        bool reactivated = Pump([&] { return !f.native.miniaturized && f.native.visible && front.key == 1 &&
                            jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; });
                        if (!reactivated)
                            std::fprintf(stderr, "Cancelled activation trace: cancellation=%d key=%d main=%d externalKey=%d externalMain=%d mini=%d visible=%d state=%d\n",
                                cancellation, front.key, front.main, front.externalKey, front.externalMain,
                                f.native.miniaturized, f.native.visible,
                                jalium_window_get_state(f.window));
                        Require(reactivated,
                            "cancelled selection left the window unable to activate later");
                    });
                }
            for (bool mainOnly : {false, true}) {
                run(mainOnly ? "activation completion retains replacement after AX main" : "activation completion retains replacement after Activate", [=](Fixture& f) {
                    FrontRequests front(f.native); std::unique_ptr<Fixture> replacement; NSRect saved;
                    WindowNotification requested(f, NSWindowWillMiniaturizeNotification, [&] {
                        if (replacement) return;
                        if (mainOnly) [f.native setAccessibilityMain:YES];
                        else jalium_window_activate(f.window);
                        f.Destroy(); replacement = std::make_unique<Fixture>(style, 400, 320, 520, 360); saved = replacement->native.frame;
                    });
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MINIMIZED);
                    Require(Pump([&] { return replacement != nullptr; }), "activation start did not create replacement");
                    Pump([] { return false; }, 0.3);
                    Require(front.key == 0 && NSEqualRects(replacement->native.frame, saved) &&
                        jalium_window_get_state(replacement->window) == JALIUM_WINDOW_STATE_NORMAL,
                        "retired activation selected or changed a replacement window");
                });
            }
            run("delayed deminiaturization drains the latest state request", [](Fixture& f) {
                NSRect restore = RestoreBounds(f);
                RestoreTrace trace(f);
                for (auto requested : {JALIUM_WINDOW_STATE_NORMAL, JALIUM_WINDOW_STATE_MAXIMIZED, JALIUM_WINDOW_STATE_MINIMIZED}) {
                    trace.Request(JALIUM_WINDOW_STATE_MAXIMIZED);
                    [f.native setAccessibilityMinimized:YES];
                    Require(Pump([&] { return f.native.miniaturized; }), "initial minimize did not finish");
                    DeferredDeminiaturize deferred(f.native);
                    [f.native setAccessibilityMinimized:NO];
                    Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MINIMIZED,
                        "state changed before AppKit acknowledged deminiaturization");
                    Require(!Settable(f.native, NSAccessibilityPositionAttribute) &&
                        !Settable(f.native, NSAccessibilitySizeAttribute) &&
                        !Settable(f.native, NSAccessibilityMinimizedAttribute), "transition advertised writable geometry");
                    NSRect before = f.native.frame;
                    [f.native setAccessibilityFrame:f.ChangedFrame()];
                    Require(NSEqualRects(f.native.frame, before), "AX changed geometry during deminiaturization");
                    trace.Request(JALIUM_WINDOW_STATE_NORMAL);
                    trace.Request(JALIUM_WINDOW_STATE_MAXIMIZED);
                    trace.Request(requested);
                    deferred.Complete();
                    Require(Pump([&] { return jalium_window_get_state(f.window) == requested &&
                        f.native.miniaturized == (requested == JALIUM_WINDOW_STATE_MINIMIZED); }), "latest queued state was lost");
                    trace.Record("observed");
                    NSRect actualRestore = RestoreBounds(f);
                    if (!NSEqualRects(actualRestore, restore)) {
                        std::fprintf(stderr,
                            "Restore diagnostic: requested=%d state=%d mini=%d expected=(%.0f,%.0f,%.0f,%.0f) actual=(%.0f,%.0f,%.0f,%.0f) native=(%.0f,%.0f,%.0f,%.0f)\n",
                            requested, jalium_window_get_state(f.window), f.native.miniaturized,
                            restore.origin.x, restore.origin.y, restore.size.width, restore.size.height,
                            actualRestore.origin.x, actualRestore.origin.y, actualRestore.size.width, actualRestore.size.height,
                            f.native.frame.origin.x, f.native.frame.origin.y, f.native.frame.size.width, f.native.frame.size.height);
                    }
                    Require(NSEqualRects(actualRestore, restore), "queued restore replaced normal bounds");
                    trace.Request(JALIUM_WINDOW_STATE_NORMAL);
                    Require(Pump([&] { return !f.native.miniaturized &&
                        jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }), "final normal restore failed");
                    Require(NSEqualRects(RestoreBounds(f), restore), "normal bounds changed after queued restore");
                    trace.Record("round-end");
                }
            });
            run("maximize callback preserves a replacement window", [style](Fixture& f) {
                CheckStateReplacement(f, style, JALIUM_WINDOW_STATE_MAXIMIZED);
            });
            run("restore callback preserves a replacement window", [style](Fixture& f) {
                CheckStateReplacement(f, style, JALIUM_WINDOW_STATE_NORMAL);
            });
            run("native deminiaturization preserves maximized restore bounds", [](Fixture& f) {
                NSRect restore = RestoreBounds(f);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                [f.native miniaturize:nil];
                Require(Pump([&] { return f.native.miniaturized; }), "native minimize did not finish");
                [f.native deminiaturize:nil];
                Require(Pump([&] { return !f.native.miniaturized &&
                    jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED; }), "native restore did not retain maximized state");
                Require(NSEqualRects(RestoreBounds(f), restore), "native restore replaced normal bounds");
            });
            run("native Zoom queued during delayed restore retains AppKit best-fit", [](Fixture& f) {
                NSRect restore = RestoreBounds(f);
                BestFitZoom bestFit(f.native);
                [f.native setAccessibilityMinimized:YES];
                Require(Pump([&] { return f.native.miniaturized; }), "initial minimize did not finish");
                DeferredDeminiaturize deferred(f.native);
                [f.native setAccessibilityMinimized:NO];
                [f.native zoom:nil];
                deferred.Complete();
                Require(Pump([&] { return !f.native.miniaturized &&
                    jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED; }), "restore lost the queued native Zoom");
                Require(NSEqualRects(f.native.frame, bestFit.delegate.standardFrame) &&
                    NSEqualRects(RestoreBounds(f), restore), "queued restore Zoom lost best-fit or normal geometry");
            });
            run("two native Zoom actions during delayed restore cancel", [](Fixture& f) {
                NSRect restore = RestoreBounds(f);
                [f.native setAccessibilityMinimized:YES];
                Require(Pump([&] { return f.native.miniaturized; }), "initial minimize did not finish");
                DeferredDeminiaturize deferred(f.native);
                [f.native setAccessibilityMinimized:NO];
                [f.native zoom:nil]; [f.native zoom:nil];
                deferred.Complete();
                Require(Pump([&] { return !f.native.miniaturized &&
                    jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }), "restore did not cancel paired Zoom requests");
                Pump([] { return true; });
                NSRect actualRestore = RestoreBounds(f);
                if (!NSEqualRects(actualRestore, restore))
                    std::fprintf(stderr, "Paired restore Zoom: expected=%s actual=%s frame=%s state=%d\n",
                        NSStringFromRect(restore).UTF8String, NSStringFromRect(actualRestore).UTF8String,
                        NSStringFromRect(f.native.frame).UTF8String, jalium_window_get_state(f.window));
                Require(NSEqualRects(actualRestore, restore), "paired restore Zoom requests lost normal geometry");
            });
            run("deminiaturization callback can queue another state", [](Fixture& f) {
                NSRect restore = RestoreBounds(f);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                [f.native setAccessibilityMinimized:YES];
                Require(Pump([&] { return f.native.miniaturized; }), "initial minimize did not finish");
                jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* context) {
                    auto* fixture = static_cast<Fixture*>(context);
                    if (event->type == JALIUM_EVENT_STATE_CHANGED &&
                        event->stateChanged.newState == JALIUM_WINDOW_STATE_MAXIMIZED)
                        jalium_window_set_state(fixture->window, JALIUM_WINDOW_STATE_NORMAL);
                }, &f);
                [f.native setAccessibilityMinimized:NO];
                Require(Pump([&] { return !f.native.miniaturized &&
                    jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }), "state requested by restore callback was lost");
                Require(NSEqualRects(RestoreBounds(f), restore), "callback request replaced normal bounds");
            });
            run("deminiaturization callback can destroy its window", [](Fixture& f) {
                [f.native setAccessibilityMinimized:YES];
                Require(Pump([&] { return f.native.miniaturized; }), "initial minimize did not finish");
                jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* context) {
                    auto* fixture = static_cast<Fixture*>(context);
                    if (event->type == JALIUM_EVENT_STATE_CHANGED &&
                        event->stateChanged.newState != JALIUM_WINDOW_STATE_MINIMIZED) fixture->Destroy();
                }, &f);
                [f.native setAccessibilityMinimized:NO];
                Require(Pump([&] { return f.window == nullptr; }), "restore callback did not destroy the window");
                Require(![f.native respondsToSelector:@selector(setAccessibilityMinimized:)],
                    "destroyed restore reference stayed writable");
            });
            run("native Zoom requested by a restore callback preserves normal bounds", [](Fixture& f) {
                NSRect restore = RestoreBounds(f);
                BestFitZoom bestFit(f.native);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* context) {
                    auto* fixture = static_cast<Fixture*>(context);
                    if (event->type == JALIUM_EVENT_STATE_CHANGED &&
                        event->stateChanged.newState == JALIUM_WINDOW_STATE_NORMAL && !fixture->callbackRequested) {
                        fixture->callbackRequested = true;
                        [fixture->native zoom:nil];
                    }
                }, &f);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                Require(Pump([&] { return f.callbackRequested &&
                    jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED; }), "restore callback lost native Zoom");
                Require(NSEqualRects(RestoreBounds(f), restore), "reentrant native Zoom captured the maximized restore frame");
                Require(NSEqualRects(f.native.frame, bestFit.delegate.standardFrame),
                    "queued native Zoom used work-area Fill instead of AppKit best-fit geometry");
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                Require(NSEqualRects(RestoreBounds(f), restore), "normal geometry was lost after reentrant native Zoom");
            });
            run("a newer explicit state request replaces queued native Zoom", [](Fixture& f) {
                NSRect restore = RestoreBounds(f);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* context) {
                    auto* fixture = static_cast<Fixture*>(context);
                    if (event->type == JALIUM_EVENT_STATE_CHANGED &&
                        event->stateChanged.newState == JALIUM_WINDOW_STATE_NORMAL && !fixture->callbackRequested) {
                        fixture->callbackRequested = true;
                        [fixture->native zoom:nil];
                        jalium_window_set_state(fixture->window, JALIUM_WINDOW_STATE_NORMAL);
                    }
                }, &f);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                Pump([] { return true; });
                Require(f.callbackRequested && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL &&
                    NSEqualRects(RestoreBounds(f), restore), "an older native Zoom overrode the newer explicit request");
            });
            run("two queued native Zoom requests cancel each other", [](Fixture& f) {
                NSRect restore = RestoreBounds(f);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* context) {
                    auto* fixture = static_cast<Fixture*>(context);
                    if (event->type == JALIUM_EVENT_STATE_CHANGED &&
                        event->stateChanged.newState == JALIUM_WINDOW_STATE_NORMAL && !fixture->callbackRequested) {
                        fixture->callbackRequested = true;
                        [fixture->native zoom:nil]; [fixture->native zoom:nil];
                    }
                }, &f);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                Pump([] { return true; });
                Require(f.callbackRequested && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL &&
                    NSEqualRects(RestoreBounds(f), restore), "two queued native Zoom requests did not cancel");
            });
            run("queued native Zoom revalidates hidden disabled and fixed windows", [style](Fixture& f) {
                NSRect restore = RestoreBounds(f);
                for (int phase = 0; phase < 3; ++phase) {
                    f.callbackRequested = false;
                    jalium_window_set_event_callback(f.window, nullptr, nullptr);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    f.events.clear();
                    struct Context { Fixture* fixture; uint32_t style; int phase; } context{&f, style, phase};
                    jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* data) {
                        auto* context = static_cast<Context*>(data);
                        auto* fixture = context->fixture;
                        if (event->type == JALIUM_EVENT_STATE_CHANGED &&
                            event->stateChanged.newState == JALIUM_WINDOW_STATE_NORMAL && !fixture->callbackRequested) {
                            fixture->callbackRequested = true;
                            [fixture->native zoom:nil];
                            if (context->phase == 0) jalium_window_hide(fixture->window);
                            else if (context->phase == 1) jalium_window_set_enabled(fixture->window, 0);
                            else jalium_apple_window_set_style(fixture->window, context->style & ~JALIUM_WINDOW_STYLE_MAXIMIZABLE);
                        }
                    }, &context);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                    // Retire the stack callback before either assertions or
                    // the fixture destructor can deliver another event.
                    jalium_window_set_event_callback(f.window, Capture, &f.events);
                    Require(f.callbackRequested && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL &&
                        NSEqualRects(RestoreBounds(f), restore), "queued native Zoom reached an excluded window");
                    if (phase == 0) jalium_apple_window_show(f.window, 0);
                    else if (phase == 1) jalium_window_set_enabled(f.window, 1);
                    else jalium_apple_window_set_style(f.window, style);
                }
            });
            for (bool entering : {true, false}) {
                auto begin = [entering](Fixture& f, ControlledFullScreen& transition, bool maximized) {
                    if (maximized) jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    if (!entering) {
                        transition.Will(); transition.Complete();
                        Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_FULLSCREEN; }, 0.4),
                            "controlled fullscreen entry did not finish");
                    }
                    jalium_window_set_state(f.window, entering ? JALIUM_WINDOW_STATE_FULLSCREEN
                        : maximized ? JALIUM_WINDOW_STATE_MAXIMIZED : JALIUM_WINDOW_STATE_NORMAL);
                    Require(transition.toggles == 1, "controlled transition did not start exactly once");
                    f.events.clear();
                };
                const char* coreNames[] = {"rolls back without retry", "preserves the latest queued state",
                    "retries a newer identical request once", "drains pending activation", "drains pending AX main",
                    "defers its callback's newer state"};
                for (bool maximized : {false, true}) for (int phase = 0; phase < 6; ++phase) {
                    std::string name = std::string("fullscreen failure ") + (entering ? "enter " : "exit ")
                        + (maximized ? "from maximized " : "from normal ") + coreNames[phase];
                    run(name.c_str(), [=](Fixture& f) {
                        NSRect restore = RestoreBounds(f);
                        ControlledFullScreen transition(f.native);
                        begin(f, transition, maximized);
                        auto actual = entering ? (maximized ? JALIUM_WINDOW_STATE_MAXIMIZED : JALIUM_WINDOW_STATE_NORMAL)
                            : JALIUM_WINDOW_STATE_FULLSCREEN;
                        auto target = maximized ? JALIUM_WINDOW_STATE_NORMAL : JALIUM_WINDOW_STATE_MAXIMIZED;
                        if (phase == 0) {
                            transition.Fail();
                            Require(Pump([&] {
                                for (const auto& event : f.events) if (event.type == JALIUM_EVENT_STATE_CHANGED) return true;
                                return false;
                            }, 0.4), "failure did not publish the actual state");
                            Pump([] { return false; }, 0.08);
                            Require(transition.toggles == 1 && jalium_window_get_state(f.window) == actual,
                                "failed request was retried without a newer request");
                        } else if (phase == 1) {
                            jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                            jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                            jalium_window_set_state(f.window, target);
                            transition.Fail();
                            if (!entering) {
                                Require(Pump([&] { return transition.toggles == 2; }, 0.4), "exit failure lost the newer state request");
                                transition.Complete();
                            }
                            Require(Pump([&] { return jalium_window_get_state(f.window) == target; }, 0.4),
                                "enter failure lost the newer state request");
                        } else if (phase == 2) {
                            jalium_window_set_state(f.window, entering ? JALIUM_WINDOW_STATE_FULLSCREEN
                                : maximized ? JALIUM_WINDOW_STATE_MAXIMIZED : JALIUM_WINDOW_STATE_NORMAL);
                            transition.Fail();
                            Require(Pump([&] { return transition.toggles == 2; }, 0.4), "new identical request was discarded");
                            transition.Fail();
                            Pump([] { return false; }, 0.08);
                            Require(transition.toggles == 2 && jalium_window_get_state(f.window) == actual,
                                "the second failed request retried automatically");
                        } else if (phase == 3 || phase == 4) {
                            FrontRequests front(f.native); front.waiting = true;
                            if (phase == 3) Require(jalium_window_activate(f.window) == JALIUM_OK, "activation was rejected");
                            else [f.native setAccessibilityMain:YES];
                            Pump([] { return true; });
                            Require(front.key == 0 && front.main == 0, "window selected during fullscreen transition");
                            transition.Fail();
                            Require(front.key == 0 && front.main == 0, "window selected inside the failure delegate");
                            front.waiting = false;
                            Require(Pump([&] { return phase == 3 ? front.key == 1 : front.main == 1; }, 0.4),
                                "failure left the selection request undrained");
                            Require(front.premature == 0 && (phase == 3 || front.key == 0), "AX main changed keyboard selection");
                            Require(jalium_window_get_state(f.window) == actual, "selection changed the actual recovered state");
                        } else {
                            struct Context { Fixture* fixture; ControlledFullScreen* transition; JaliumWindowState target;
                                bool called = false, premature = false; } context{&f, &transition, target};
                            jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* data) {
                                auto* c = static_cast<Context*>(data);
                                if (event->type != JALIUM_EVENT_STATE_CHANGED || c->called) return;
                                c->called = true; NSRect frame = c->fixture->native.frame; int toggles = c->transition->toggles;
                                jalium_window_set_state(c->fixture->window, c->target);
                                c->premature = !NSEqualRects(frame, c->fixture->native.frame) || toggles != c->transition->toggles;
                            }, &context);
                            transition.Fail();
                            bool called = Pump([&] { return context.called; }, 0.4);
                            jalium_window_set_event_callback(f.window, Capture, &f.events);
                            Require(called && !context.premature, "failure callback started a new transition before unwinding");
                            if (!entering) {
                                Require(Pump([&] { return transition.toggles == 2; }, 0.4), "callback request did not retry exit");
                                transition.Complete();
                            }
                            Require(Pump([&] { return jalium_window_get_state(f.window) == target; }, 0.4), "callback request was lost");
                        }
                        Require(NSEqualRects(RestoreBounds(f), restore), "fullscreen failure replaced normal restore geometry");
                    });
                }
                std::string prefix = std::string("fullscreen failure ") + (entering ? "enter " : "exit ");
                run((prefix + "preserves a request after the failure report").c_str(), [=](Fixture& f) {
                    ControlledFullScreen transition(f.native); begin(f, transition, false);
                    NSRect frame = f.native.frame;
                    transition.Fail();
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    Require(NSEqualRects(frame, f.native.frame) && transition.toggles == 1,
                        "new request ran before the failure delegate unwound");
                    if (!entering) {
                        Require(Pump([&] { return transition.toggles == 2; }, 0.4), "late request did not retry exit");
                        transition.Complete();
                    }
                    Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED; }, 0.4),
                        "failure completion overwrote the later request");
                });
                run((prefix + "leaves a callback replacement untouched").c_str(), [=](Fixture& f) {
                    ControlledFullScreen transition(f.native); begin(f, transition, false);
                    FrontRequests front(f.native);
                    jalium_window_activate(f.window);
                    struct Context { Fixture* fixture; ControlledFullScreen* transition; uint32_t style;
                        std::unique_ptr<Fixture> replacement; NSRect frame; } context{&f, &transition, style, nullptr, NSZeroRect};
                    jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* data) {
                        auto* c = static_cast<Context*>(data);
                        if (event->type != JALIUM_EVENT_STATE_CHANGED || c->replacement) return;
                        c->transition->fullScreen = false;
                        c->fixture->Destroy();
                        c->replacement = std::make_unique<Fixture>(c->style, 400, 320, 520, 360);
                        c->frame = c->replacement->native.frame;
                    }, &context);
                    transition.Fail();
                    bool replaced = Pump([&] { return context.replacement != nullptr; }, 0.4);
                    if (f.window) jalium_window_set_event_callback(f.window, Capture, &f.events);
                    Require(replaced, "failure callback did not create a replacement");
                    Pump([] { return false; }, 0.08);
                    Require(front.key == 0 && NSEqualRects(context.frame, context.replacement->native.frame) &&
                        jalium_window_get_state(context.replacement->window) == JALIUM_WINDOW_STATE_NORMAL,
                        "retired failure completion selected or changed the replacement");
                });
                run((prefix + "respects cancelled activation and permits later activation").c_str(), [=](Fixture&) {
                    for (int cancel = 0; cancel < 3; ++cancel) {
                        Fixture f(style); ControlledFullScreen transition(f.native); begin(f, transition, false);
                        FrontRequests front(f.native); jalium_window_activate(f.window);
                        if (cancel == 0) jalium_window_hide(f.window);
                        else if (cancel == 1) jalium_window_set_enabled(f.window, 0);
                        else jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MINIMIZED);
                        transition.Fail();
                        if (cancel == 2 && !entering) {
                            Require(Pump([&] { return transition.toggles == 2; }, 0.4), "new minimize did not survive exit failure");
                            transition.Complete();
                        }
                        if (cancel == 2) Require(Pump([&] { return f.native.miniaturized; }), "new minimize did not finish after failure");
                        Pump([] { return false; }, 0.08);
                        Require(front.key == 0 && front.main == 0, "failure restored a cancelled selection");
                        if (cancel == 1) jalium_window_set_enabled(f.window, 1);
                        Require(jalium_window_activate(f.window) == JALIUM_OK &&
                            Pump([&] { return front.key == 1 && !f.native.miniaturized; }), "failure blocked later activation");
                    }
                });
                run((prefix + "ignores duplicate failure completion").c_str(), [=](Fixture& f) {
                    ControlledFullScreen transition(f.native); begin(f, transition, false);
                    FrontRequests front(f.native); jalium_window_activate(f.window);
                    transition.Fail(); transition.Fail();
                    Pump([] { return false; }, 0.08);
                    int notifications = 0;
                    for (const auto& event : f.events) notifications += event.type == JALIUM_EVENT_STATE_CHANGED;
                    Require(notifications == 1 && front.key == 1 && transition.toggles == 1,
                        "duplicate failure notified or selected twice");
                });
            }
            const char* focusOutcomes[] = {"enter success", "exit success", "enter failure", "exit failure"};
            const char* focusTargets[] = {"input view", "previously unfocused", "hidden", "disabled", "new native responder"};
            for (int outcome = 0; outcome < 4; ++outcome) for (int target = 0; target < 5; ++target) {
                std::string name = std::string("fullscreen focus ") + focusOutcomes[outcome] + " retains " + focusTargets[target];
                run(name.c_str(), [=](Fixture& f) {
                    ControlledFullScreen transition(f.native);
                    bool exiting = (outcome & 1) != 0;
                    if (exiting) {
                        transition.Will(); transition.Complete();
                        Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_FULLSCREEN; }, 0.4),
                            "controlled initial fullscreen did not finish");
                    }
                    std::unique_ptr<ContentAccessibility> ax;
                    if (foregroundHost) ax = std::make_unique<ContentAccessibility>(f);
                    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    [f.native makeFirstResponder:target == 1 ? nil : view];
                    Require((f.native.firstResponder == view) == (target != 1), "initial responder was not installed");
                    FrontRequests front(f.native);
                    jalium_window_set_state(f.window, exiting ? JALIUM_WINDOW_STATE_NORMAL : JALIUM_WINDOW_STATE_FULLSCREEN);
                    // Actual desktop evidence showed AppKit replacing the view
                    // with the default window responder during the transition.
                    [f.native makeFirstResponder:nil];
                    if (ax) ax->CheckExcluded();
                    NSTextView* other = nil;
                    if (target == 2) jalium_window_hide(f.window);
                    else if (target == 3) jalium_window_set_enabled(f.window, 0);
                    else if (target == 4) {
                        other = [[NSTextView alloc] initWithFrame:NSMakeRect(0, 0, 8, 8)];
                        [view addSubview:other];
                        Require([f.native makeFirstResponder:other], "new native responder was rejected");
                    }
                    // Explicit hiding legitimately resigns key selection.
                    // Compare completion with the state after those requests.
                    bool key = f.native.keyWindow;
                    f.events.clear();
                    if (outcome >= 2) transition.Fail(); else transition.Complete();
                    Require(Pump([&] {
                        for (const auto& event : f.events) if (event.type == JALIUM_EVENT_STATE_CHANGED) return true;
                        return false;
                    }, 0.4), "fullscreen completion did not publish state");
                    Require((f.native.firstResponder == view) == (target == 0), "completion lost or stole input view focus");
                    Require(target != 4 || f.native.firstResponder == other, "completion replaced a newer native responder");
                    Require(front.key == 0 && front.main == 0 && f.native.keyWindow == key, "focus restoration activated a window");
                    if (ax) { if (target == 0) ax->CheckFocused(); else ax->CheckExcluded(); }
                    if (other) { [f.native makeFirstResponder:nil]; [other removeFromSuperview]; }
                });
            }
            run("fullscreen exit notification preserves its callback's latest state", [](Fixture& f) {
                NSRect restore = RestoreBounds(f);
                auto notification = [NSNotification notificationWithName:NSWindowWillEnterFullScreenNotification object:f.native];
                // Replay the owned delegate's notification sequence. This is
                // a state-machine check, not a real Spaces acceptance test.
                [f.native.delegate windowWillEnterFullScreen:notification];
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_FULLSCREEN);
                [f.native.delegate windowDidEnterFullScreen:notification];
                Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_FULLSCREEN; }), "fullscreen state notification did not finish");
                [f.native.delegate windowWillExitFullScreen:notification];
                jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* context) {
                    auto* fixture = static_cast<Fixture*>(context);
                    if (event->type == JALIUM_EVENT_STATE_CHANGED &&
                        event->stateChanged.newState == JALIUM_WINDOW_STATE_NORMAL && !fixture->callbackRequested) {
                        fixture->callbackRequested = true;
                        jalium_window_set_state(fixture->window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    }
                }, &f);
                [f.native.delegate windowDidExitFullScreen:notification];
                Require(Pump([&] { return f.callbackRequested &&
                    jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED; }), "fullscreen exit overwrote the newer callback request");
                Require(NSEqualRects(RestoreBounds(f), restore), "fullscreen exit callback replaced normal geometry");
            });
            run("destroyed minimize reference rejects writes", [](Fixture& f) {
                f.Destroy();
                bool allowed = [f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityMinimized:)];
                [f.native setAccessibilityMinimized:YES];
                Require(!f.native.miniaturized, "AX minimized a destroyed window");
                Require(!allowed, "destroyed minimize advertised writable");
            });
            run("disabled focus and main writes are rejected", [](Fixture& f) {
                jalium_window_set_enabled(f.window, 0);
                NSWindow* key = NSApp.keyWindow; NSWindow* main = NSApp.mainWindow;
                Require(![f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityFocused:)], "disabled focus advertised writable");
                Require(![f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityMain:)], "disabled main advertised writable");
                [f.native setAccessibilityFocused:YES]; [f.native setAccessibilityMain:YES];
                Require(NSApp.keyWindow == key && NSApp.mainWindow == main, "AX activated a disabled window");
            });
            run("main and focus writes never expose AX-only state", [](Fixture& f) {
                Require(Settable(f.native, NSAccessibilityFocusedAttribute) &&
                    Settable(f.native, NSAccessibilityMainAttribute), "live focus/main metadata is read-only");
                [f.native setAccessibilityMain:YES]; [f.native setAccessibilityFocused:YES];
                Pump([] { return true; });
                // An unbundled, inactive validation process cannot prove
                // foreground activation. The external Lab checks that path.
                Require(f.native.isAccessibilityMain == f.native.mainWindow &&
                    f.native.isAccessibilityFocused == f.native.keyWindow, "AX focus/main getters disagree with AppKit");
            });
            run("hidden and destroyed focus references cannot activate", [](Fixture& f) {
                NSWindow* key = NSApp.keyWindow; NSWindow* main = NSApp.mainWindow;
                jalium_window_hide(f.window);
                [f.native setAccessibilityFocused:YES]; [f.native setAccessibilityMain:YES];
                Require(!Settable(f.native, NSAccessibilityFocusedAttribute) &&
                    !Settable(f.native, NSAccessibilityMainAttribute) &&
                    NSApp.keyWindow == key && NSApp.mainWindow == main, "hidden AX reference activated its window");
                f.Destroy();
                [f.native setAccessibilityFocused:YES]; [f.native setAccessibilityMain:YES];
                Require(!Settable(f.native, NSAccessibilityFocusedAttribute) &&
                    !Settable(f.native, NSAccessibilityMainAttribute) &&
                    NSApp.keyWindow == key && NSApp.mainWindow == main, "destroyed AX reference activated its window");
            });
            run("queued focus and main writes revalidate their window", [](Fixture& f) {
                for (int phase = 0; phase < 3; ++phase) {
                    [f.native setAccessibilityFocused:YES]; [f.native setAccessibilityMain:YES];
                    if (phase == 0) jalium_window_hide(f.window);
                    else if (phase == 1) jalium_window_set_enabled(f.window, 0);
                    else f.Destroy();
                    NSWindow* key = NSApp.keyWindow; NSWindow* main = NSApp.mainWindow;
                    f.events.clear(); Pump([] { return true; });
                    if (NSApp.keyWindow != key || NSApp.mainWindow != main || !f.events.empty()) {
                        std::fprintf(stderr, "Queued AX selection: phase=%d beforeKey=%p afterKey=%p beforeMain=%p afterMain=%p events=",
                            phase, (__bridge void*)key, (__bridge void*)NSApp.keyWindow,
                            (__bridge void*)main, (__bridge void*)NSApp.mainWindow);
                        for (const auto& event : f.events) std::fprintf(stderr, "%d,", event.type);
                        std::fprintf(stderr, "\n");
                    }
                    Require(NSApp.keyWindow == key && NSApp.mainWindow == main && f.events.empty(),
                        "cached queued request activated an excluded window");
                    if (phase == 0) jalium_apple_window_show(f.window, 0);
                    else if (phase == 1) jalium_window_set_enabled(f.window, 1);
                }
            });
            run("writable metadata follows runtime capabilities", [style](Fixture& f) {
                Require(Settable(f.native, NSAccessibilityPositionAttribute), "live position is read-only");
                Require(Settable(f.native, NSAccessibilitySizeAttribute), "resizable size is read-only");
                Require(Settable(f.native, NSAccessibilityMinimizedAttribute), "live minimize is read-only");
                jalium_apple_window_set_style(f.window, style & ~JALIUM_WINDOW_STYLE_RESIZABLE);
                Require(Settable(f.native, NSAccessibilityPositionAttribute), "fixed window position is read-only");
                Require(!Settable(f.native, NSAccessibilitySizeAttribute), "fixed size is writable");
                jalium_window_set_enabled(f.window, 0);
                Require(!Settable(f.native, NSAccessibilityPositionAttribute) &&
                    !Settable(f.native, NSAccessibilitySizeAttribute) &&
                    !Settable(f.native, NSAccessibilityMinimizedAttribute), "disabled AX attributes remain writable");
            });
            run("position-only edits preserve size without user resize", [](Fixture& f) {
                NSRect requested = f.native.frame;
                requested.origin.x += 30; requested.origin.y -= 20;
                [f.native setAccessibilityFrame:requested];
                Require(NSEqualRects(f.native.frame, requested), "position-only AX request failed");
                for (const auto& event : f.events)
                    Require(event.type != JALIUM_EVENT_RESIZE || !event.resize.isUserInitiated,
                        "moving a window incorrectly became a user resize");
            });
            run("legacy client position and size writes reach real geometry", [](Fixture& f) {
                NSRect before = f.native.frame;
                NSPoint position = [LegacyValue(f.native, NSAccessibilityPositionAttribute) pointValue];
                position.x += 24; position.y += 18;
                LegacyWrite(f.native, NSAccessibilityPositionAttribute, [NSValue valueWithPoint:position]);
                // AppKit's NSValue position is bottom-left; the external
                // ApplicationServices AX position is converted to top-left.
                Require(NSEqualPoints(f.native.frame.origin, NSMakePoint(before.origin.x + 24, before.origin.y + 18)) &&
                    NSEqualSizes(f.native.frame.size, before.size), "legacy position write did not move the real window");
                NSSize size = [LegacyValue(f.native, NSAccessibilitySizeAttribute) sizeValue];
                size.width += 40; size.height += 30;
                LegacyWrite(f.native, NSAccessibilitySizeAttribute, [NSValue valueWithSize:size]);
                Require(NSEqualSizes(f.native.frame.size, NSMakeSize(before.size.width + 40, before.size.height + 30)),
                    "legacy size write did not resize the real window");
                // Window size edits preserve the upper edge, which moves its
                // bottom-left origin down by the added height.
                Require(NSMinX(f.native.frame) == position.x && NSMaxY(f.native.frame) == NSMaxY(before) + 18,
                    "legacy size write moved the requested top-left");
            });
            run("legacy writes reject disabled hidden and destroyed references", [](Fixture& f) {
                for (int phase = 0; phase < 3; ++phase) {
                    if (phase == 0) jalium_window_set_enabled(f.window, 0);
                    else if (phase == 1) { jalium_window_set_enabled(f.window, 1); jalium_window_hide(f.window); }
                    else f.Destroy();
                    NSRect before = f.native.frame;
                    NSWindow* key = NSApp.keyWindow; NSWindow* main = NSApp.mainWindow;
                    NSPoint position = [LegacyValue(f.native, NSAccessibilityPositionAttribute) pointValue];
                    position.x += 24; position.y += 18;
                    NSSize size = before.size; size.width += 40; size.height += 30;
                    TryExcludedLegacyWrite(f.native, NSAccessibilityPositionAttribute, [NSValue valueWithPoint:position]);
                    TryExcludedLegacyWrite(f.native, NSAccessibilitySizeAttribute, [NSValue valueWithSize:size]);
                    TryExcludedLegacyWrite(f.native, NSAccessibilityMinimizedAttribute, @YES);
                    TryExcludedLegacyWrite(f.native, NSAccessibilityFocusedAttribute, @YES);
                    TryExcludedLegacyWrite(f.native, NSAccessibilityMainAttribute, @YES);
                    Pump([] { return true; });
                    Require(NSEqualRects(f.native.frame, before) && !f.native.miniaturized &&
                        NSApp.keyWindow == key && NSApp.mainWindow == main,
                        "legacy client changed an excluded window");
                }
            });
            run("legacy size rejects fixed windows while position remains writable", [style](Fixture& f) {
                jalium_apple_window_set_style(f.window, style & ~JALIUM_WINDOW_STYLE_RESIZABLE);
                NSRect before = f.native.frame;
                TryExcludedLegacyWrite(f.native, NSAccessibilitySizeAttribute,
                    [NSValue valueWithSize:NSMakeSize(before.size.width + 80, before.size.height + 60)]);
                Require(NSEqualRects(f.native.frame, before), "legacy size resized a fixed window");
                NSPoint position = [LegacyValue(f.native, NSAccessibilityPositionAttribute) pointValue];
                position.x += 20; position.y += 14;
                LegacyWrite(f.native, NSAccessibilityPositionAttribute, [NSValue valueWithPoint:position]);
                Require(NSEqualPoints(f.native.frame.origin, position) && NSEqualSizes(f.native.frame.size, before.size),
                    "legacy position failed on a fixed window");
            });
            run("legacy size constraints preserve the upper edge", [](Fixture& f) {
                Require(jalium_window_set_min_max_size(f.window, 480, 360, 900, 720) == JALIUM_OK,
                    "legacy size constraints were rejected");
                NSRect before = f.native.frame;
                int width, height;
                for (bool minimum : {true, false}) {
                    NSSize requested = minimum ? NSMakeSize(40, 40) : NSMakeSize(1400, 1200);
                    LegacyWrite(f.native, NSAccessibilitySizeAttribute, [NSValue valueWithSize:requested]);
                    jalium_window_get_client_size(f.window, &width, &height);
                    if (NSMaxY(f.native.frame) != NSMaxY(before))
                        std::fprintf(stderr, "Legacy size anchor: minimum=%d before=%s after=%s client=%dx%d\n",
                            minimum, NSStringFromRect(before).UTF8String, NSStringFromRect(f.native.frame).UTF8String,
                            width, height);
                    Require(width == (minimum ? 480 : 900) && height == (minimum ? 360 : 720) &&
                        NSMinX(f.native.frame) == NSMinX(before) && NSMaxY(f.native.frame) == NSMaxY(before),
                        "constrained legacy size changed the top-left anchor");
                }
            });
            run("legacy frame rejects malformed and non-finite payloads", [](Fixture& f) {
                NSRect before = f.native.frame;
                for (id value in @[@YES, @"invalid", [NSValue valueWithRect:before],
                        [NSValue valueWithPoint:NSMakePoint(NAN, 20)],
                        [NSValue valueWithPoint:NSMakePoint(INFINITY, 20)]])
                    TryExcludedLegacyWrite(f.native, NSAccessibilityPositionAttribute, value);
                for (id value in @[@YES, @"invalid", [NSValue valueWithRect:before],
                        [NSValue valueWithSize:NSMakeSize(0, 40)],
                        [NSValue valueWithSize:NSMakeSize(-20, 40)],
                        [NSValue valueWithSize:NSMakeSize(INFINITY, 40)]])
                    TryExcludedLegacyWrite(f.native, NSAccessibilitySizeAttribute, value);
                Require(NSEqualRects(f.native.frame, before) && f.events.empty(), "invalid legacy frame reached AppKit");
            });
            run("legacy resize callback may destroy before the final move", [](Fixture& f) {
                NSRect before = f.native.frame;
                jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* context) {
                    auto* fixture = static_cast<Fixture*>(context);
                    if (event->type == JALIUM_EVENT_RESIZE && fixture->window) fixture->Destroy();
                }, &f);
                LegacyWrite(f.native, NSAccessibilitySizeAttribute,
                    [NSValue valueWithSize:NSMakeSize(before.size.width + 40, before.size.height + 30)]);
                Require(!f.window && !f.native.visible && !Settable(f.native, NSAccessibilityPositionAttribute),
                    "legacy resize did not retire the callback-closed window");
                NSRect closed = f.native.frame;
                TryExcludedLegacyWrite(f.native, NSAccessibilityPositionAttribute,
                    [NSValue valueWithPoint:NSMakePoint(before.origin.x + 60, before.origin.y + 40)]);
                Require(NSEqualRects(f.native.frame, closed), "late legacy move changed a closed window");
            });
            run("legacy selection and minimize use native actions", [](Fixture& f) {
                {
                    FrontRequests front(f.native);
                    LegacyWrite(f.native, NSAccessibilityMainAttribute, @YES);
                    LegacyWrite(f.native, NSAccessibilityFocusedAttribute, @YES);
                    Require(Pump([&] { return front.key == 1 && front.main == 1; }),
                        "legacy selection did not reach explicit native requests");
                }
                LegacyWrite(f.native, NSAccessibilityMinimizedAttribute, @YES);
                Require(Pump([&] { return f.native.miniaturized &&
                    jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MINIMIZED; }),
                    "legacy minimize did not reach native state");
                LegacyWrite(f.native, NSAccessibilityMinimizedAttribute, @NO);
                Require(Pump([&] { return !f.native.miniaturized &&
                    jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }),
                    "legacy restore did not reach native state");
            });
            run("invalid frame values leave real geometry unchanged", [](Fixture& f) {
                NSRect before = f.native.frame;
                NSRect invalid = before; invalid.origin.x = std::numeric_limits<CGFloat>::quiet_NaN();
                [f.native setAccessibilityFrame:invalid];
                invalid = before; invalid.size.height = std::numeric_limits<CGFloat>::infinity();
                [f.native setAccessibilityFrame:invalid];
                invalid = before; invalid.size.width = -1;
                [f.native setAccessibilityFrame:invalid];
                Require(NSEqualRects(f.native.frame, before) && f.events.empty(), "invalid AX frame reached AppKit");
            });
            run("foreign-thread writes are rejected", [](Fixture& f) {
                NSRect before = f.native.frame, requested = f.ChangedFrame();
                NSWindow* key = NSApp.keyWindow; NSWindow* main = NSApp.mainWindow;
                bool allowed = false;
                std::thread worker([&] {
                    allowed = [f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityFrame:)] ||
                        [f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityMinimized:)] ||
                        [f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityFocused:)] ||
                        [f.native isAccessibilitySelectorAllowed:@selector(setAccessibilityMain:)];
                    [f.native setAccessibilityFrame:requested]; [f.native setAccessibilityMinimized:YES];
                    [f.native setAccessibilityFocused:YES]; [f.native setAccessibilityMain:YES];
                });
                worker.join();
                Require(!allowed && NSEqualRects(f.native.frame, before) && !f.native.miniaturized &&
                    NSApp.keyWindow == key && NSApp.mainWindow == main && f.events.empty(), "worker changed AppKit state");
            });
            run("resize callback can destroy its window safely", [](Fixture& f) {
                jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* context) {
                    auto* fixture = static_cast<Fixture*>(context);
                    if (event->type == JALIUM_EVENT_RESIZE && fixture->window) fixture->Destroy();
                }, &f);
                [f.native setAccessibilityFrame:f.ChangedFrame()];
                Require(!f.window && !f.native.visible &&
                    ![f.native respondsToSelector:@selector(setAccessibilityFrame:)], "closed during resize stayed writable");
            });
            run("resize callback geometry supersedes the remaining AX move", [](Fixture& f) {
                jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* context) {
                    auto& fixture = *static_cast<Fixture*>(context);
                    fixture.events.push_back(*event);
                    if (event->type == JALIUM_EVENT_RESIZE && !fixture.callbackRequested) {
                        fixture.callbackRequested = true;
                        jalium_window_move(fixture.window, 490, 320);
                        fixture.callbackFrame = fixture.native.frame;
                    }
                }, &f);
                [f.native setAccessibilityFrame:f.ChangedFrame()];
                Require(f.callbackRequested && NSEqualRects(f.native.frame, f.callbackFrame),
                    "AX move replaced geometry written by its resize callback");
            });
        }
        jalium_platform_shutdown();
        if (caseName && total == 0) {
            std::fprintf(stderr, "Unknown Window AX case: %s\n", caseName);
            return 2;
        }
        std::printf("macOS native Window AX write checks: %d/%d passed\n", passed, total);
        return passed == total ? 0 : 1;
    }
}
