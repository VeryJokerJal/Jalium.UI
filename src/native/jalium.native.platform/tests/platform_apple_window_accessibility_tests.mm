#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include "jalium_platform.h"
#include <algorithm>
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
@property(nonatomic) BOOL forwardingResize;
@end
@implementation JaliumWindowAXZoomDelegate
- (void)windowDidResize:(NSNotification*)note {
    self.forwardingResize = YES;
    [self.originalDelegate windowDidResize:note];
    self.forwardingResize = NO;
}
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

// Override only this test process's argument domain, never Desktop & Dock.
struct DoubleClickPreference {
    NSDictionary* previous;
    explicit DoubleClickPreference(NSString* action)
    {
        auto* defaults = NSUserDefaults.standardUserDefaults;
        previous = [defaults volatileDomainForName:NSArgumentDomain];
        NSMutableDictionary* domain = [previous mutableCopy] ?: [NSMutableDictionary new];
        domain[@"AppleActionOnDoubleClick"] = action;
        [defaults setVolatileDomain:domain forName:NSArgumentDomain];
    }
    ~DoubleClickPreference()
    {
        [NSUserDefaults.standardUserDefaults setVolatileDomain:previous ?: @{} forName:NSArgumentDomain];
    }
};

struct StyleFrameProbe;
static StyleFrameProbe* g_styleFrame = nullptr;
static void ApplyStyleFrame(id object, SEL selector, NSUInteger mask);

// Invoke application code after AppKit has replaced this owned frame.
struct StyleFrameProbe {
    NSWindow* window;
    Method method;
    IMP original;
    bool called = false;
    std::function<void()> onApply;
    StyleFrameProbe(NSWindow* target, std::function<void()> callback) : window(target), onApply(std::move(callback))
    {
        Class cls = object_getClass(window);
        Method inherited = class_getInstanceMethod(cls, @selector(setStyleMask:));
        original = method_getImplementation(inherited);
        class_addMethod(cls, @selector(setStyleMask:), original, method_getTypeEncoding(inherited));
        method = class_getInstanceMethod(cls, @selector(setStyleMask:));
        g_styleFrame = this;
        method_setImplementation(method, reinterpret_cast<IMP>(ApplyStyleFrame));
    }
    ~StyleFrameProbe() { method_setImplementation(method, original); g_styleFrame = nullptr; }
};
static void ApplyStyleFrame(id object, SEL selector, NSUInteger mask)
{
    auto* probe = g_styleFrame;
    reinterpret_cast<void (*)(id, SEL, NSUInteger)>(probe->original)(object, selector, mask);
    if (object == probe->window && !probe->called) { probe->called = true; probe->onApply(); }
}

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

static NSSize ScreenFittingClientMaximum(Fixture& fixture)
{
    // A titled NSWindow automatically constrains an oversized frame to its
    // screen. Keep these client-limit tests inside that independent OS limit.
    NSRect visible = fixture.native.screen.visibleFrame;
    double scale = jalium_window_get_dpi_scale(fixture.window);
    NSRect content = [fixture.native contentRectForFrameRect:fixture.native.frame];
    CGFloat decoration = fixture.native.frame.size.height - content.size.height;
    NSSize maximum = NSMakeSize(std::min(900.0, std::floor((visible.size.width - 80) * scale)),
        std::min(720.0, std::floor((visible.size.height - decoration - 40) * scale)));
    Require(maximum.width >= 480 && maximum.height >= 360, "screen cannot fit the client-limit fixture");
    [fixture.native setFrameTopLeftPoint:NSMakePoint(NSMinX(visible) + 40, NSMaxY(visible) - 20)];
    return maximum;
}

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

struct WorkAreaProbe;
static WorkAreaProbe* g_workArea = nullptr;
static NSRect ReadWorkArea(id object, SEL selector);
struct WorkAreaProbe {
    NSScreen* screen;
    Method method;
    IMP original;
    NSRect visible;
    std::function<bool()> ready;
    std::function<void()> onRead;
    explicit WorkAreaProbe(NSScreen* target) : screen(target), visible(target.visibleFrame) {
        Class cls = object_getClass(screen);
        Method inherited = class_getInstanceMethod(cls, @selector(visibleFrame));
        original = method_getImplementation(inherited);
        class_addMethod(cls, @selector(visibleFrame), original, method_getTypeEncoding(inherited));
        method = class_getInstanceMethod(cls, @selector(visibleFrame));
        g_workArea = this;
        method_setImplementation(method, reinterpret_cast<IMP>(ReadWorkArea));
    }
    ~WorkAreaProbe() { method_setImplementation(method, original); g_workArea = nullptr; }
};
static NSRect ReadWorkArea(id object, SEL selector) {
    auto* probe = g_workArea;
    NSRect real = reinterpret_cast<NSRect (*)(id, SEL)>(probe->original)(object, selector);
    if (object != probe->screen) return real;
    if (probe->onRead && (!probe->ready || probe->ready())) {
        std::function<void()> action; action.swap(probe->onRead); action();
    }
    return probe->visible;
}

struct RestoreSnapshotProbe;
static RestoreSnapshotProbe* g_restoreSnapshot = nullptr;
static NSRect ReadRestoreSnapshotRect(id object, SEL selector);
static NSUInteger ReadRestoreSnapshotMask(id object, SEL selector);
static CGFloat ReadRestoreSnapshotScale(id object, SEL selector);
static BOOL ReadRestoreSnapshotLiveResize(id object, SEL selector);
static id ReadRestoreSnapshotObject(id object, SEL selector);
static id ReadRestoreSnapshotButton(id object, SEL selector, NSWindowButton kind);
static NSPoint ReadRestoreSnapshotPoint(id object, SEL selector, NSPoint point);
static NSPoint ReadRestoreSnapshotViewPoint(id object, SEL selector, NSPoint point, NSView* view);

// A native getter may execute embedding code before returning its original
// value. Intercept only this fixture's object and restore the actual getter.
struct RestoreSnapshotProbe {
    id target;
    Method method;
    IMP original;
    std::function<void()> onRead;
    bool called = false;
    bool executing = false;
    int targetReads = 0, invokeOnRead, remainingActions;
    RestoreSnapshotProbe(Fixture& fixture, SEL selector, std::function<void()> action,
        int firstRead = 1, int actionCount = 1)
        : RestoreSnapshotProbe(selector == @selector(bounds)
            ? (__bridge id)(void*)jalium_window_get_native_handle(fixture.window) : fixture.native,
          selector, std::move(action), firstRead, actionCount) {}
    RestoreSnapshotProbe(id object, SEL selector, std::function<void()> action,
        int firstRead = 1, int actionCount = 1)
        : target(object), onRead(std::move(action)), invokeOnRead(firstRead), remainingActions(actionCount)
    {
        Class cls = object_getClass(target);
        Method inherited = class_getInstanceMethod(cls, selector);
        original = method_getImplementation(inherited);
        // Keep AppKit/KVO's object class intact. Other objects use the real
        // getter; only this retained target executes the callback once.
        class_addMethod(cls, selector, original, method_getTypeEncoding(inherited));
        method = class_getInstanceMethod(cls, selector);
        g_restoreSnapshot = this;
        IMP replacement = reinterpret_cast<IMP>(ReadRestoreSnapshotRect);
        if (selector == @selector(styleMask)) replacement = reinterpret_cast<IMP>(ReadRestoreSnapshotMask);
        else if (selector == @selector(backingScaleFactor)) replacement = reinterpret_cast<IMP>(ReadRestoreSnapshotScale);
        else if (selector == @selector(inLiveResize) || selector == @selector(isFlipped) ||
            selector == @selector(isMiniaturized) || selector == @selector(isZoomed))
            replacement = reinterpret_cast<IMP>(ReadRestoreSnapshotLiveResize);
        else if (selector == @selector(screen) || selector == @selector(contentView) || selector == @selector(superview) ||
            selector == @selector(delegate) || selector == @selector(currentEditor)) replacement = reinterpret_cast<IMP>(ReadRestoreSnapshotObject);
        else if (selector == @selector(standardWindowButton:)) replacement = reinterpret_cast<IMP>(ReadRestoreSnapshotButton);
        else if (selector == @selector(convertPointToScreen:)) replacement = reinterpret_cast<IMP>(ReadRestoreSnapshotPoint);
        else if (selector == @selector(convertPoint:toView:)) replacement = reinterpret_cast<IMP>(ReadRestoreSnapshotViewPoint);
        method_setImplementation(method, replacement);
    }
    ~RestoreSnapshotProbe() {
        method_setImplementation(method, original);
        g_restoreSnapshot = nullptr;
    }
    void Invoke(id object) {
        if (object != target || executing || remainingActions == 0) return;
        if (++targetReads < invokeOnRead) return;
        called = true;
        --remainingActions;
        executing = true;
        try { onRead(); }
        catch (...) { executing = false; throw; }
        executing = false;
    }
};
static NSRect ReadRestoreSnapshotRect(id object, SEL selector)
{
    auto* probe = g_restoreSnapshot;
    NSRect value = reinterpret_cast<NSRect (*)(id, SEL)>(probe->original)(object, selector);
    probe->Invoke(object);
    return value;
}
static NSUInteger ReadRestoreSnapshotMask(id object, SEL selector)
{
    auto* probe = g_restoreSnapshot;
    NSUInteger value = reinterpret_cast<NSUInteger (*)(id, SEL)>(probe->original)(object, selector);
    probe->Invoke(object);
    return value;
}
static CGFloat ReadRestoreSnapshotScale(id object, SEL selector)
{
    auto* probe = g_restoreSnapshot;
    CGFloat value = reinterpret_cast<CGFloat (*)(id, SEL)>(probe->original)(object, selector);
    probe->Invoke(object);
    return value;
}
static BOOL ReadRestoreSnapshotLiveResize(id object, SEL selector)
{
    auto* probe = g_restoreSnapshot;
    BOOL value = reinterpret_cast<BOOL (*)(id, SEL)>(probe->original)(object, selector);
    probe->Invoke(object);
    return value;
}
static id ReadRestoreSnapshotObject(id object, SEL selector)
{
    auto* probe = g_restoreSnapshot;
    id value = reinterpret_cast<id (*)(id, SEL)>(probe->original)(object, selector);
    probe->Invoke(object);
    return value;
}
static id ReadRestoreSnapshotButton(id object, SEL selector, NSWindowButton kind)
{
    auto* probe = g_restoreSnapshot;
    id value = reinterpret_cast<id (*)(id, SEL, NSWindowButton)>(probe->original)(object, selector, kind);
    probe->Invoke(object);
    return value;
}
static NSPoint ReadRestoreSnapshotPoint(id object, SEL selector, NSPoint point)
{
    auto* probe = g_restoreSnapshot;
    NSPoint value = reinterpret_cast<NSPoint (*)(id, SEL, NSPoint)>(probe->original)(object, selector, point);
    probe->Invoke(object);
    return value;
}
static NSPoint ReadRestoreSnapshotViewPoint(id object, SEL selector, NSPoint point, NSView* view)
{
    auto* probe = g_restoreSnapshot;
    NSPoint value = reinterpret_cast<NSPoint (*)(id, SEL, NSPoint, NSView*)>(probe->original)(object, selector, point, view);
    probe->Invoke(object);
    return value;
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

struct TrackingVisibilityProbe;
static TrackingVisibilityProbe* g_trackingVisibility = nullptr;
static BOOL ReadTrackingVisibility(id object, SEL selector);

// Execute one owned-window callback during a native visibility query. Keep
// the original getter and restore it before this fixture leaves its scope.
struct TrackingVisibilityProbe {
    NSWindow* window;
    Method method;
    IMP original;
    std::function<void()> onRead;
    TrackingVisibilityProbe(NSWindow* native, std::function<void()> callback)
        : window(native), onRead(std::move(callback))
    {
        Class cls = object_getClass(native);
        Method inherited = class_getInstanceMethod(cls, @selector(isVisible));
        original = method_getImplementation(inherited);
        class_addMethod(cls, @selector(isVisible), original, method_getTypeEncoding(inherited));
        method = class_getInstanceMethod(cls, @selector(isVisible));
        g_trackingVisibility = this;
        method_setImplementation(method, reinterpret_cast<IMP>(ReadTrackingVisibility));
    }
    ~TrackingVisibilityProbe()
    {
        method_setImplementation(method, original);
        g_trackingVisibility = nullptr;
    }
};
static BOOL ReadTrackingVisibility(id object, SEL selector)
{
    auto* probe = g_trackingVisibility;
    BOOL result = reinterpret_cast<BOOL (*)(id, SEL)>(probe->original)(object, selector);
    if (object == probe->window && probe->onRead) {
        std::function<void()> callback;
        callback.swap(probe->onRead);
        callback();
    }
    return result;
}

// Target an owned responder directly to model a packet AppKit queued before
// that responder's availability changed. No system input is posted.
@interface JaliumWindowPointerPacket : NSEvent
@property NSEventType packetType;
@property NSInteger packetButton;
@property NSPoint packetLocation;
@property BOOL packetPrecise;
@property CGFloat packetDeltaX;
@property CGFloat packetDeltaY;
@property NSEventPhase packetPhase;
@property NSEventPhase packetMomentum;
@property SEL callbackSelector;
@property int callbackCount;
@property(copy) void (^onQuery)(void);
@end
@implementation JaliumWindowPointerPacket
- (void)query:(SEL)selector {
    if (selector != self.callbackSelector || !self.onQuery) return;
    void (^callback)(void) = self.onQuery;
    self.onQuery = nil;
    ++self.callbackCount;
    callback();
}
- (NSEventType)type { [self query:_cmd]; return self.packetType; }
- (NSInteger)buttonNumber { [self query:_cmd]; return self.packetButton; }
- (NSPoint)locationInWindow { [self query:_cmd]; return self.packetLocation; }
- (NSEventModifierFlags)modifierFlags { [self query:_cmd]; return NSEventModifierFlagShift; }
- (NSInteger)clickCount { [self query:_cmd]; return 2; }
- (BOOL)hasPreciseScrollingDeltas { [self query:_cmd]; return self.packetPrecise; }
- (CGFloat)scrollingDeltaX { [self query:_cmd]; return self.packetDeltaX; }
- (CGFloat)scrollingDeltaY { [self query:_cmd]; return self.packetDeltaY; }
- (NSEventPhase)phase { [self query:_cmd]; return self.packetPhase; }
- (NSEventPhase)momentumPhase { [self query:_cmd]; return self.packetMomentum; }
@end

static JaliumWindowPointerPacket* PointerPacket(int route)
{
    auto* packet = [JaliumWindowPointerPacket new];
    packet.packetLocation = NSMakePoint(20, 35);
    packet.packetButton = route < 4 ? 0 : route < 7 ? 1 : 3;
    const NSEventType types[] = {NSEventTypeMouseMoved, NSEventTypeLeftMouseDown,
        NSEventTypeLeftMouseDragged, NSEventTypeLeftMouseUp, NSEventTypeRightMouseDown,
        NSEventTypeRightMouseDragged, NSEventTypeRightMouseUp, NSEventTypeOtherMouseDown,
        NSEventTypeOtherMouseDragged, NSEventTypeOtherMouseUp, NSEventTypeScrollWheel};
    packet.packetType = types[route];
    packet.packetPrecise = YES;
    packet.packetDeltaX = -12;
    packet.packetDeltaY = 24;
    packet.packetPhase = NSEventPhaseChanged;
    packet.packetMomentum = NSEventPhaseBegan;
    return packet;
}

static void DeliverPointerPacket(NSView* view, JaliumWindowPointerPacket* packet, int route)
{
    switch (route) {
        case 0: [view mouseMoved:packet]; break;
        case 1: [view mouseDown:packet]; break;
        case 2: [view mouseDragged:packet]; break;
        case 3: [view mouseUp:packet]; break;
        case 4: [view rightMouseDown:packet]; break;
        case 5: [view rightMouseDragged:packet]; break;
        case 6: [view rightMouseUp:packet]; break;
        case 7: [view otherMouseDown:packet]; break;
        case 8: [view otherMouseDragged:packet]; break;
        case 9: [view otherMouseUp:packet]; break;
        case 10: [view scrollWheel:packet]; break;
    }
}

static bool IsPointerPacket(const JaliumPlatformEvent& event)
{
    return event.type == JALIUM_EVENT_MOUSE_MOVE || event.type == JALIUM_EVENT_MOUSE_DOWN ||
        event.type == JALIUM_EVENT_MOUSE_UP || event.type == JALIUM_EVENT_MOUSE_WHEEL;
}

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
        std::string origin;
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
        double time = NSProcessInfo.processInfo.systemUptime;
        int state = jalium_window_get_state(fixture.window);
        std::string origin;
        if (std::strcmp(label, "state") == 0 && requested == JALIUM_WINDOW_STATE_MAXIMIZED &&
            state == JALIUM_WINDOW_STATE_NORMAL) {
            // Retain the actual callback origin only for a return to Normal
            // while the trace still expects Maximize. Emit it only on failure.
            NSEvent* event = NSApp.currentEvent;
            NSMutableString* context = [NSMutableString stringWithFormat:
                @"Restore origin target=%ld event=%ld eventOnTarget=%d active=%d foreground=%d visible=%d key=%d main=%d liveResize=%d\n",
                static_cast<long>(fixture.native.windowNumber), static_cast<long>(event.type),
                event.window == fixture.native, NSApp.active,
                NSWorkspace.sharedWorkspace.frontmostApplication.processIdentifier,
                fixture.native.visible, fixture.native.keyWindow, fixture.native.mainWindow,
                fixture.native.inLiveResize];
            NSUInteger count = 0;
            for (NSString* frame in NSThread.callStackSymbols) {
                if (count++ == 32) break;
                [context appendFormat:@"  %@\n", frame];
            }
            origin = context.UTF8String;
        }
        samples.push_back({time, label,
            state, requested, fixture.native.miniaturized != NO,
            jalium_window_get_dpi_scale(fixture.window),
            fixture.native.frame, RestoreBounds(fixture), fixture.native.contentView.bounds.size, origin});
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
        for (const auto& s : samples) {
            std::fprintf(stderr, "Restore trace %.6f %s request=%d state=%d mini=%d scale=%.1f frame=(%.0f,%.0f,%.0f,%.0f) content=(%.0f,%.0f) restore=(%.0f,%.0f,%.0f,%.0f)\n",
                s.time, s.label, s.requested, s.state, s.minimized, s.scale,
                s.frame.origin.x, s.frame.origin.y, s.frame.size.width, s.frame.size.height,
                s.content.width, s.content.height, s.restore.origin.x, s.restore.origin.y,
                s.restore.size.width, s.restore.size.height);
            if (!s.origin.empty()) std::fputs(s.origin.c_str(), stderr);
        }
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

// Return from the primitive before its start notification. Completion still
// runs the original AppKit method; only this owned window's request is held.
struct DeferredMinimizationStart : RejectedMinimization {
    NSWindow* window;
    explicit DeferredMinimizationStart(NSWindow* native)
        : RejectedMinimization(native), window(native) {}
    void Complete()
    {
        g_rejectedMiniaturizeWindow = nil;
        reinterpret_cast<void (*)(id, SEL, id)>(g_originalMiniaturize)(window, @selector(miniaturize:), nil);
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
    bool restoreCaptureOnly = argc == 2 && std::strcmp(argv[1], "--restore-capture-only") == 0;
    bool restoreGeometryOnly = argc == 2 && std::strcmp(argv[1], "--restore-geometry-only") == 0;
    bool workAreaOnly = argc == 2 && std::strcmp(argv[1], "--work-area-only") == 0;
    bool geometryLifecycleOnly = argc == 2 && std::strcmp(argv[1], "--geometry-lifecycle-only") == 0;
    bool titlebarLifecycleOnly = argc == 2 && std::strcmp(argv[1], "--titlebar-lifecycle-only") == 0;
    bool geometryQueryOnly = argc == 2 && std::strcmp(argv[1], "--geometry-query-only") == 0;
    bool titlebarActionOnly = argc == 2 && std::strcmp(argv[1], "--titlebar-action-lifecycle-only") == 0;
    bool styleInputFocusOnly = argc == 2 && std::strcmp(argv[1], "--style-input-focus-only") == 0;
    bool minimizeTransitionOnly = argc == 2 && std::strcmp(argv[1], "--minimize-transition-only") == 0;
    bool minimizeStartOnly = argc == 2 && std::strcmp(argv[1], "--minimize-start-only") == 0;
    bool activationTransitionOnly = argc == 2 && std::strcmp(argv[1], "--activation-transition-only") == 0;
    bool fullScreenFailureOnly = argc == 2 && std::strcmp(argv[1], "--fullscreen-failure-only") == 0;
    bool fullScreenFocusOnly = argc == 2 && std::strcmp(argv[1], "--fullscreen-focus-only") == 0;
    bool contentFocusOnly = argc == 2 && std::strcmp(argv[1], "--content-focus-only") == 0;
    bool selectionProbeOnly = argc == 2 && std::strcmp(argv[1], "--selection-probe-only") == 0;
    bool frameOnly = (argc == 2 && std::strcmp(argv[1], "--frame-only") == 0) ||
        [[NSBundle.mainBundle objectForInfoDictionaryKey:@"JaliumValidationFrameOnly"] boolValue];
    const char* caseName = argc == 2 && std::strncmp(argv[1], "--case-name=", 12) == 0 ? argv[1] + 12 : nullptr;
    if (argc != 1 && !delayedRestoreOnly && !replacementOnly && !restoreCaptureOnly && !restoreGeometryOnly && !workAreaOnly && !geometryLifecycleOnly && !titlebarLifecycleOnly && !geometryQueryOnly && !titlebarActionOnly && !styleInputFocusOnly && !minimizeTransitionOnly && !minimizeStartOnly && !activationTransitionOnly && !fullScreenFailureOnly && !fullScreenFocusOnly && !contentFocusOnly && !selectionProbeOnly && !frameOnly && !caseName) return 2;
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
                if (styleInputFocusOnly && std::strstr(name, "style input focus") == nullptr) return;
                if (!styleInputFocusOnly && !caseName && std::strstr(name, "style input focus") != nullptr) return;
                if (titlebarActionOnly && std::strstr(name, "titlebar action") == nullptr) return;
                if (!titlebarActionOnly && !caseName && std::strstr(name, "titlebar action") != nullptr) return;
                if (geometryQueryOnly && std::strstr(name, "geometry query") == nullptr) return;
                if (!geometryQueryOnly && !caseName && std::strstr(name, "geometry query") != nullptr) return;
                if (titlebarLifecycleOnly && std::strstr(name, "titlebar alignment") == nullptr) return;
                if (!titlebarLifecycleOnly && !caseName && std::strstr(name, "titlebar alignment") != nullptr) return;
                if (geometryLifecycleOnly && std::strstr(name, "geometry callback") == nullptr) return;
                if (!geometryLifecycleOnly && !caseName && std::strstr(name, "geometry callback") != nullptr) return;
                if (delayedRestoreOnly &&
                    std::strcmp(name, "delayed deminiaturization drains the latest state request") != 0) return;
                if (replacementOnly && std::strstr(name, "replacement window") == nullptr) return;
                if (restoreCaptureOnly && std::strstr(name, "restore capture") == nullptr) return;
                if (restoreGeometryOnly && std::strstr(name, "restore capture geometry") == nullptr) return;
                if (workAreaOnly && std::strstr(name, "restore capture geometry Fill") == nullptr) return;
                // Keep the existing AX process within its original timeout.
                // CTest runs the new geometry matrix as its own native process.
                if (!restoreGeometryOnly && !restoreCaptureOnly && !workAreaOnly && !caseName &&
                    std::strstr(name, "restore capture geometry") != nullptr) return;
                if (minimizeTransitionOnly && std::strstr(name, "minimize completion") == nullptr) return;
                if (minimizeStartOnly && std::strstr(name, "minimize start") == nullptr &&
                    std::strcmp(name, "minimize completion rejection releases later requests") != 0) return;
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
            auto addControl = [](Fixture& f, int kind, bool renderingChild) {
                NSTextField* control = kind == 1 ? [NSSecureTextField new] :
                    kind == 2 ? [NSSearchField new] : [NSTextField new];
                control.frame = NSMakeRect(20, 50, 320, 32);
                control.stringValue = @"中文🙂 e\u0301 Native";
                NSView* parent = renderingChild ? (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window) : f.native.contentView;
                [parent addSubview:control];
                [f.native makeKeyWindow];
                Require([f.native makeFirstResponder:control], "native control rejected initial focus");
                Require(control.currentEditor && f.native.firstResponder == control.currentEditor, "native control has no active field editor");
                return control;
            };
            auto changeInputStyle = [regular, custom](Fixture& f, uint32_t initial, int operation) {
                if (operation == 0) {
                    jalium_apple_window_set_style(f.window, initial == regular ? custom : regular);
                    jalium_apple_window_set_style(f.window, initial);
                } else if (operation == 1) {
                    jalium_apple_window_set_titlebar_extended(f.window, 1);
                    jalium_apple_window_set_titlebar_extended(f.window, 0);
                } else {
                    jalium_apple_window_set_style(f.window, initial & ~JALIUM_WINDOW_STYLE_RESIZABLE);
                    jalium_apple_window_set_style(f.window, initial);
                }
            };
            for (int kind = 0; kind < 3; ++kind) for (int operation = 0; operation < 3; ++operation)
                for (bool renderingChild : {false, true}) {
                    std::string name = "style input focus control " + std::to_string(kind) + " operation " +
                        std::to_string(operation) + (renderingChild ? " rendering child" : " content child");
                    run(name.c_str(), [=](Fixture& f) {
                        NSTextField* control = addControl(f, kind, renderingChild);
                        NSString* text = control.stringValue;
                        NSRange selection = NSMakeRange(2, 2);
                        for (int pass = 0; pass < 2; ++pass) {
                            control.currentEditor.selectedRange = selection;
                            changeInputStyle(f, style, operation);
                            Require(control.currentEditor && f.native.firstResponder == control.currentEditor,
                                "style round trip lost its logical native control");
                            Require([control.stringValue isEqualToString:text] && NSEqualRanges(control.currentEditor.selectedRange, selection),
                                "style round trip changed native text or UTF-16 selection");
                        }
                        NSTextView* editor = (NSTextView*)control.currentEditor;
                        [editor insertText:@"A" replacementRange:selection];
                        Require([control.stringValue isEqualToString:[text stringByReplacingCharactersInRange:selection withString:@"A"]],
                            "restored field editor could not continue native editing");
                    });
                }
            for (bool managedRoot : {false, true}) for (int operation = 0; operation < 3; ++operation) {
                std::string name = std::string("style input focus ") + (managedRoot ? "managed root" : "native text view") +
                    " operation " + std::to_string(operation);
                run(name.c_str(), [=](Fixture& f) {
                    NSView* input = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    NSTextView* text = nil;
                    if (!managedRoot) {
                        text = [[NSTextView alloc] initWithFrame:NSMakeRect(20, 30, 240, 80)];
                        text.string = @"中文🙂 e\u0301 Native";
                        [f.native.contentView addSubview:text];
                        input = text;
                    }
                    [f.native makeKeyWindow];
                    Require([f.native makeFirstResponder:input], "input view rejected initial focus");
                    if (text) text.selectedRange = NSMakeRange(2, 2);
                    changeInputStyle(f, style, operation);
                    Require(f.native.firstResponder == input, "style round trip replaced the original input view");
                    if (text) Require([text.string isEqualToString:@"中文🙂 e\u0301 Native"] &&
                        NSEqualRanges(text.selectedRange, NSMakeRange(2, 2)), "style round trip changed native text view selection");
                });
            }
            for (int action = 0; action < 9; ++action) {
                std::string name = "style input focus post-frame action " + std::to_string(action);
                run(name.c_str(), [=](Fixture& f) {
                    NSTextField* control = addControl(f, 0, false);
                    control.currentEditor.selectedRange = NSMakeRange(2, 2);
                    NSTextField* newer = [[NSTextField alloc] initWithFrame:NSMakeRect(20, 100, 300, 32)];
                    newer.stringValue = @"newer"; [f.native.contentView addSubview:newer];
                    std::unique_ptr<Fixture> replacement;
                    NSRect replacementFrame = NSZeroRect;
                    StyleFrameProbe probe(f.native, [&] {
                        if (action == 0) jalium_window_hide(f.window);
                        else if (action == 1) jalium_window_set_enabled(f.window, 0);
                        else if (action == 2) [control removeFromSuperview];
                        else if (action == 3) control.hidden = YES;
                        else if (action == 4) control.enabled = NO;
                        else if (action == 5) Require([f.native makeFirstResponder:newer], "newer control rejected focus");
                        else if (action == 6) control.stringValue = @"changed";
                        else {
                            f.Destroy();
                            if (action == 8) {
                                replacement = std::make_unique<Fixture>(style, 500, 300, 560, 360);
                                replacementFrame = replacement->native.frame; replacement->events.clear();
                            }
                        }
                    });
                    jalium_apple_window_set_style(f.window, style == regular ? custom : regular);
                    Require(probe.called, "style input callback did not run after the native frame changed");
                    if (action >= 7) {
                        Require(!f.window, "closed input style retained its owner");
                        if (replacement) Require(NSEqualRects(replacement->native.frame, replacementFrame) && replacement->events.empty(),
                            "old input style changed its replacement");
                    } else if (action == 5) Require(newer.currentEditor && f.native.firstResponder == newer.currentEditor,
                        "style input restoration stole a newer native focus");
                    else if (action == 6) Require(control.currentEditor && f.native.firstResponder == control.currentEditor &&
                        [control.stringValue isEqualToString:@"changed"] && !NSEqualRanges(control.currentEditor.selectedRange, NSMakeRange(2, 2)),
                        "style input restoration replaced new text or reused its old selection");
                    else Require(!control.currentEditor || f.native.firstResponder != control.currentEditor,
                        "style input restoration focused an excluded control");
                });
            }
            for (bool delegateRead : {false, true}) for (bool replace : {false, true}) {
                std::string name = std::string("style input focus capture ") + (delegateRead ? "delegate" : "editor") +
                    (replace ? " replacement" : " closes");
                run(name.c_str(), [=](Fixture& f) {
                    NSTextField* control = addControl(f, 0, false);
                    id target = delegateRead ? (id)control.currentEditor : (id)control;
                    std::unique_ptr<Fixture> replacement;
                    NSRect replacementFrame = NSZeroRect;
                    RestoreSnapshotProbe probe(target, delegateRead ? @selector(delegate) : @selector(currentEditor), [&] {
                        f.Destroy();
                        if (replace) {
                            replacement = std::make_unique<Fixture>(style, 500, 300, 560, 360);
                            replacementFrame = replacement->native.frame; replacement->events.clear();
                        }
                    });
                    jalium_apple_window_set_style(f.window, style == regular ? custom : regular);
                    Require(probe.called && !f.window, "style input getter did not retire its original owner");
                    if (replacement) Require(NSEqualRects(replacement->native.frame, replacementFrame) && replacement->events.empty(),
                        "retired input capture changed its replacement");
                });
            }
            const char* actionRoutes[] = {"native Zoom", "queued Zoom", "double click minimized", "double click style"};
            run("titlebar action double click rejects foreign thread", [](Fixture& f) {
                NSRect frame = f.native.frame;
                int result = JALIUM_OK;
                std::thread worker([&] { result = jalium_apple_window_titlebar_double_click(f.window); });
                worker.join();
                Require(result == JALIUM_ERROR_INVALID_STATE && NSEqualRects(frame, f.native.frame) && f.events.empty(),
                    "foreign thread titlebar action changed its target");
            });
            run("titlebar action double click rejects retired target", [](Fixture& f) {
                auto* retired = f.window;
                f.Destroy();
                size_t count = f.events.size();
                Require(jalium_apple_window_titlebar_double_click(retired) == JALIUM_ERROR_INVALID_STATE &&
                    jalium_apple_window_titlebar_double_click(nullptr) == JALIUM_ERROR_INVALID_ARGUMENT && f.events.size() == count,
                    "invalid target titlebar action accessed a retired window");
            });
            SEL actionGetters[] = {@selector(isZoomed), @selector(isZoomed), @selector(isMiniaturized), @selector(styleMask)};
            const char* actionChanges[] = {"keeps live action", "closes original", "preserves replacement",
                "hides", "disables", "removes Zoom capability", "preserves newer state"};
            for (int route = 0; route < 4; ++route) for (int action = 0; action < 7; ++action) {
                std::string name = std::string("titlebar action ") + actionRoutes[route] + " " + actionChanges[action];
                run(name.c_str(), [=](Fixture& f) {
                    DoubleClickPreference preference(@"Fill");
                    BestFitZoom bestFit(f.native);
                    NSRect originalFrame = f.native.frame, originalRestore = RestoreBounds(f);
                    std::unique_ptr<Fixture> replacement;
                    NSRect replacementFrame = NSZeroRect;
                    bool exercised = false, entered = false;
                    size_t eventsAfterRetirement = 0;
                    auto duringRead = [&] {
                        if (action == 1 || action == 2) {
                            f.Destroy();
                            eventsAfterRetirement = f.events.size();
                            if (action == 2) {
                                replacement = std::make_unique<Fixture>(style, 500, 300, 560, 360);
                                replacementFrame = replacement->native.frame;
                                replacement->events.clear();
                            }
                        } else if (action == 3) jalium_window_hide(f.window);
                        else if (action == 4) jalium_window_set_enabled(f.window, 0);
                        else if (action == 5) jalium_apple_window_set_style(f.window, style & ~JALIUM_WINDOW_STYLE_MAXIMIZABLE);
                        else if (action == 6) jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    };
                    auto perform = [&] {
                        RestoreSnapshotProbe probe(f, actionGetters[route], duringRead);
                        if (route < 2) [f.native zoom:nil];
                        else {
                            int result = jalium_apple_window_titlebar_double_click(f.window);
                            Require(result == (action == 1 || action == 2 ? JALIUM_ERROR_INVALID_STATE : JALIUM_OK),
                                "double click did not report its actual live target");
                        }
                        exercised = probe.called;
                    };
                    std::function<void(const JaliumPlatformEvent*)> capture = [&](const JaliumPlatformEvent* event) {
                        f.events.push_back(*event);
                        if (event->type == JALIUM_EVENT_STATE_CHANGED &&
                            event->stateChanged.newState == JALIUM_WINDOW_STATE_MAXIMIZED && !entered) {
                            entered = true;
                            perform();
                        }
                    };
                    struct RestoreCallback {
                        Fixture& fixture;
                        ~RestoreCallback() { if (fixture.window) jalium_window_set_event_callback(fixture.window, Capture, &fixture.events); }
                    } restoreCallback{f};
                    if (route == 1) {
                        jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* data) {
                            (*static_cast<std::function<void(const JaliumPlatformEvent*)>*>(data))(event);
                        }, &capture);
                        jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    } else perform();
                    Require(exercised, "titlebar action did not exercise the intended native getter");
                    if (action == 1 || action == 2) {
                        Require(!f.window && f.events.size() == eventsAfterRetirement,
                            "retired titlebar action published another event");
                        if (replacement) Require(NSEqualRects(replacement->native.frame, replacementFrame) &&
                            jalium_window_get_state(replacement->window) == JALIUM_WINDOW_STATE_NORMAL && replacement->events.empty(),
                            "retired titlebar action changed its replacement");
                        return;
                    }
                    int expectedState = action == 6 || (route == 1 && action != 0) || (route != 1 && action == 0)
                        ? JALIUM_WINDOW_STATE_MAXIMIZED : JALIUM_WINDOW_STATE_NORMAL;
                    Require(jalium_window_get_state(f.window) == expectedState, "titlebar action replaced the newer or excluded state");
                    Require(NSEqualRects(RestoreBounds(f), originalRestore), "titlebar action changed normal restore bounds");
                    if (action == 0 && route == 0) Require(NSEqualRects(f.native.frame, bestFit.delegate.standardFrame),
                        "native Zoom lost AppKit best-fit geometry");
                    if ((action == 0 && route == 1) || (action >= 3 && action <= 5 && route != 1))
                        Require(NSEqualRects(f.native.frame, originalFrame), "excluded or paired titlebar action changed its frame");
                    if (action == 3) Require(!f.native.visible, "titlebar action showed its hidden target");
                    if (action == 4) Require(!f.native.canBecomeKeyWindow, "titlebar action enabled its excluded target");
                });
            }
            const char* captureGetters[] = {"frame", "bounds", "style"};
            auto frameworkPoint = [](Fixture& f, NSPoint point) {
                NSScreen* primary = NSScreen.screens.firstObject;
                NSScreen* screen = f.native.screen ?: primary;
                NSRect frame = screen.frame;
                double top = NSMaxY(primary.frame), baseScale = primary.backingScaleFactor, scale = screen.backingScaleFactor;
                return NSMakePoint(NSMinX(frame) * baseScale + (point.x - NSMinX(frame)) * scale,
                    (top - NSMaxY(frame)) * baseScale + (NSMaxY(frame) - point.y) * scale);
            };
            const char* queryNames[] = {"restore frame", "restore bounds", "restore screen", "client view point", "client screen point", "client screen"};
            SEL querySelectors[] = {@selector(frame), @selector(bounds), @selector(screen), @selector(convertPoint:toView:),
                @selector(convertPointToScreen:), @selector(screen)};
            for (int getter = 0; getter < 6; ++getter) for (int firstRead : {1, 2}) for (int action = 0; action < 6; ++action) {
                const char* actions[] = {"keeps live output", "closes original", "preserves replacement", "retains newer frame", "retains newer state", "retains changed bounds"};
                std::string name = std::string("geometry query ") + queryNames[getter] + " read " + std::to_string(firstRead) + " " + actions[action];
                run(name.c_str(), [=](Fixture& f) {
                    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    NSRect expectedFrame = f.native.frame; NSSize expectedContent = view.bounds.size;
                    int x = 71, y = 72, width = 73, height = 74;
                    std::unique_ptr<Fixture> replacement; NSRect replacementFrame = NSZeroRect; size_t expectedEvents = 0;
                    id target = getter == 1 || getter == 3 ? (id)view : (id)f.native;
                    RestoreSnapshotProbe probe(target, querySelectors[getter], [&] {
                        if (action == 1 || action == 2) {
                            f.Destroy();
                            if (action == 2) {
                                replacement = std::make_unique<Fixture>(style, 500, 300, 560, 360);
                                replacementFrame = replacement->native.frame; replacement->events.clear();
                            }
                        } else if (action == 3) {
                            [f.native setFrame:f.ChangedFrame() display:NO]; expectedFrame = f.native.frame; expectedContent = view.bounds.size;
                        } else if (action == 4) jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                        else if (action == 5) {
                            NSRect bounds = view.bounds; bounds.origin.x += 5; bounds.origin.y += 7;
                            bounds.size.width += 11; bounds.size.height += 13; view.bounds = bounds; expectedContent = view.bounds.size;
                        }
                        expectedEvents = f.events.size();
                    }, firstRead);
                    f.events.clear();
                    int result = getter < 3 ? jalium_apple_window_get_restore_bounds(f.window, &x, &y, &width, &height)
                        : jalium_apple_window_get_client_origin(f.window, &x, &y);
                    Require(probe.called, "geometry query did not reach its target read");
                    Require(f.events.size() == expectedEvents, "pure geometry query published a window event");
                    if (action == 1 || action == 2) {
                        Require(!f.window && result == JALIUM_ERROR_INVALID_STATE, "retired query returned success");
                        Require(x == 71 && y == 72 && width == 73 && height == 74, "retired query changed caller output");
                    } else {
                        Require(result == 0, "live or newer geometry query was rejected");
                        probe.remainingActions = 0;
                        if (getter < 3) {
                            NSPoint expected = frameworkPoint(f, NSMakePoint(NSMinX(expectedFrame), NSMaxY(expectedFrame)));
                            float scale = jalium_window_get_dpi_scale(f.window);
                            Require(x == lround(expected.x) && y == lround(expected.y) && width == lround(expectedContent.width * scale) &&
                                height == lround(expectedContent.height * scale), "restore query returned incoherent geometry");
                        } else {
                            NSPoint expected = frameworkPoint(f, [f.native convertPointToScreen:[view convertPoint:NSZeroPoint toView:nil]]);
                            Require(x == lround(expected.x) && y == lround(expected.y) && width == 73 && height == 74,
                                "client origin query returned incoherent geometry or changed unused output");
                        }
                    }
                    if (replacement) Require(NSEqualRects(replacement->native.frame, replacementFrame) && replacement->events.empty(),
                        "retired query altered replacement window");
                });
            }
            const char* viewSetters[] = {"frame", "frame origin", "frame size", "bounds", "bounds origin", "bounds size", "frame rotation", "bounds rotation"};
            for (int getter = 0; getter < 6; ++getter) for (int setter = 0; setter < 8; ++setter) for (bool noop : {false, true}) {
                std::string name = std::string("geometry query ") + queryNames[getter] + " second read " + viewSetters[setter] +
                    (noop ? " accepts unchanged geometry" : " tracks late view geometry");
                run(name.c_str(), [=](Fixture& f) {
                    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    id target = getter == 1 || getter == 3 ? (id)view : (id)f.native;
                    RestoreSnapshotProbe probe(target, querySelectors[getter], [&] {
                        NSRect frame = view.frame, bounds = view.bounds;
                        if (!noop) { frame.origin.x += 5; frame.origin.y += 7; frame.size.width += 11; frame.size.height += 13;
                            bounds.origin.x += 5; bounds.origin.y += 7; bounds.size.width += 11; bounds.size.height += 13; }
                        switch (setter) {
                            case 0: view.frame = frame; break;
                            case 1: [view setFrameOrigin:frame.origin]; break;
                            case 2: [view setFrameSize:frame.size]; break;
                            case 3: view.bounds = bounds; break;
                            case 4: [view setBoundsOrigin:bounds.origin]; break;
                            case 5: [view setBoundsSize:bounds.size]; break;
                            case 6: view.frameRotation += noop ? 0 : 9; break;
                            case 7: view.boundsRotation += noop ? 0 : 9; break;
                        }
                    }, 2, noop ? 100000 : 1);
                    int x = 71, y = 72, width = 73, height = 74;
                    int result = getter < 3 ? jalium_apple_window_get_restore_bounds(f.window, &x, &y, &width, &height)
                        : jalium_apple_window_get_client_origin(f.window, &x, &y);
                    Require(probe.called && result == 0, "late or unchanged view geometry query was rejected");
                    probe.remainingActions = 0;
                    if (getter < 3) {
                        NSRect frame = f.native.frame; NSPoint expected = frameworkPoint(f, NSMakePoint(NSMinX(frame), NSMaxY(frame)));
                        NSSize content = view.bounds.size; float scale = jalium_window_get_dpi_scale(f.window);
                        Require(x == lround(expected.x) && y == lround(expected.y) && width == lround(content.width * scale) &&
                            height == lround(content.height * scale), "query returned bounds from before its late view change");
                    } else {
                        NSPoint expected = frameworkPoint(f, [f.native convertPointToScreen:[view convertPoint:NSZeroPoint toView:nil]]);
                        Require(x == lround(expected.x) && y == lround(expected.y), "query returned a point from before its late view change");
                    }
                    Require(f.events.empty(), "view-only query changes published a Window event");
                });
            }
            run("geometry query saved restore bounds ignore current view changes", [&](Fixture& f) {
                NSRect expectedFrame = f.native.frame;
                NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                NSSize expectedContent = view.bounds.size; float scale = jalium_window_get_dpi_scale(f.window);
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED); f.events.clear();
                RestoreSnapshotProbe probe(f, @selector(screen), [&] {
                    NSRect bounds = view.bounds; bounds.origin.x += 1; bounds.size.width += 1; view.bounds = bounds;
                }, 1, 100000);
                int x = 71, y = 72, width = 73, height = 74;
                Require(jalium_apple_window_get_restore_bounds(f.window, &x, &y, &width, &height) == 0 && probe.called,
                    "current maximized view changes invalidated saved normal geometry");
                probe.remainingActions = 0;
                NSPoint expected = frameworkPoint(f, NSMakePoint(NSMinX(expectedFrame), NSMaxY(expectedFrame)));
                Require(x == lround(expected.x) && y == lround(expected.y) && width == lround(expectedContent.width * scale) &&
                    height == lround(expectedContent.height * scale) && f.events.empty(), "saved query changed normal geometry or Window events");
            });
            run("geometry query hidden window remains readable", [&](Fixture& f) {
                jalium_window_hide(f.window); f.events.clear(); int x, y, width, height;
                Require(jalium_apple_window_get_restore_bounds(f.window, &x, &y, &width, &height) == 0 &&
                    jalium_apple_window_get_client_origin(f.window, &x, &y) == 0 && f.events.empty(), "hidden geometry query failed or published events");
            });
            for (bool client : {false, true}) run(client ? "geometry query client instability is bounded and recovers" :
                "geometry query restore instability is bounded and recovers", [&](Fixture& f) {
                NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                NSRect original = view.bounds; int x = 71, y = 72, width = 73, height = 74;
                RestoreSnapshotProbe probe(client ? (id)f.native : (id)view, client ? @selector(convertPointToScreen:) : @selector(bounds), [&] {
                    NSRect bounds = view.bounds; bounds.origin.x += 1; bounds.size.width += 1; view.bounds = bounds;
                }, 1, 100000);
                int result = client ? jalium_apple_window_get_client_origin(f.window, &x, &y)
                    : jalium_apple_window_get_restore_bounds(f.window, &x, &y, &width, &height);
                Require(result == JALIUM_ERROR_INVALID_STATE && probe.targetReads < 1000, "unstable query was not rejected within its bound");
                Require(x == 71 && y == 72 && width == 73 && height == 74 && f.events.empty(), "unstable query changed output or events");
                probe.remainingActions = 0; view.bounds = original;
                result = client ? jalium_apple_window_get_client_origin(f.window, &x, &y)
                    : jalium_apple_window_get_restore_bounds(f.window, &x, &y, &width, &height);
                Require(result == 0, "query could not recover after instability ended");
            });
            run("geometry query foreign thread rejects native reads and preserves outputs", [](Fixture& f) {
                int x = 71, y = 72, width = 73, height = 74, restoreResult = 0, clientResult = 0;
                RestoreSnapshotProbe probe(f, @selector(frame), [] {});
                std::thread worker([&] {
                    restoreResult = jalium_apple_window_get_restore_bounds(f.window, &x, &y, &width, &height);
                    clientResult = jalium_apple_window_get_client_origin(f.window, &x, &y);
                }); worker.join();
                Require(restoreResult == JALIUM_ERROR_INVALID_STATE && clientResult == JALIUM_ERROR_INVALID_STATE && !probe.called,
                    "foreign geometry query reached AppKit or succeeded");
                Require(x == 71 && y == 72 && width == 73 && height == 74, "foreign query changed output");
            });
            run("geometry query retired handles and invalid pointers preserve output", [](Fixture& f) {
                int x = 71, y = 72, width = 73, height = 74; auto* retired = f.window; f.Destroy();
                Require(jalium_apple_window_get_restore_bounds(retired, &x, &y, &width, &height) == JALIUM_ERROR_INVALID_STATE &&
                    jalium_apple_window_get_client_origin(retired, &x, &y) == JALIUM_ERROR_INVALID_STATE, "retired query reached released owner");
                Require(jalium_apple_window_get_restore_bounds(nullptr, &x, &y, &width, &height) == JALIUM_ERROR_INVALID_ARGUMENT &&
                    jalium_apple_window_get_client_origin(nullptr, &x, &y) == JALIUM_ERROR_INVALID_ARGUMENT,
                    "null handle query used the wrong error");
                Require(x == 71 && y == 72 && width == 73 && height == 74, "rejected query changed output");
            });
            auto checkTitlebar = [](Fixture& f, double height) {
                NSView* content = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                for (NSWindowButton kind : {NSWindowCloseButton, NSWindowMiniaturizeButton, NSWindowZoomButton}) {
                    NSButton* button = [f.native standardWindowButton:kind];
                    Require(button != nil, "extended titlebar lost a system button");
                    NSPoint center = [button convertPoint:NSMakePoint(NSMidX(button.bounds), NSMidY(button.bounds)) toView:content];
                    double top = content.isFlipped ? center.y : NSHeight(content.bounds) - center.y;
                    Require(std::abs(top - height / 2) <= 0.01, "system button did not use the latest toolbar height");
                    NSView* root = button;
                    while (root.superview) root = root.superview;
                    NSPoint hitPoint = [button convertPoint:NSMakePoint(NSMidX(button.bounds), NSMidY(button.bounds)) toView:root.superview];
                    NSView* hit = [root hitTest:hitPoint];
                    Require(hit == button || [hit isDescendantOf:button], "aligned system button lost its native hit area");
                    id exposed = kind == NSWindowCloseButton ? f.native.accessibilityCloseButton :
                        kind == NSWindowMiniaturizeButton ? f.native.accessibilityMinimizeButton : f.native.accessibilityZoomButton;
                    Require([exposed isAccessibilityElement] && [[exposed accessibilityRole] isEqualToString:NSAccessibilityButtonRole],
                        "window lost its exposed native accessibility button");
                }
            };
            if (style == regular) {
                const char* routes[] = {"resize", "layout", "height setter"};
                const char* getters[] = {"style", "system button", "content", "content bounds", "content flipped",
                    "button parent", "button bounds", "button frame", "container parent", "container bounds", "container flipped"};
                SEL selectors[] = {@selector(styleMask), @selector(standardWindowButton:), @selector(contentView),
                    @selector(bounds), @selector(isFlipped), @selector(superview), @selector(bounds), @selector(frame),
                    @selector(superview), @selector(bounds), @selector(isFlipped)};
                for (int route = 0; route < 3; ++route) for (int getter = 0; getter < 11; ++getter)
                    for (int action = 0; action < 6; ++action) {
                        const char* actions[] = {"keeps live buttons", "closes original", "preserves replacement", "retains newer height", "retains newer frame", "retains disabled extension"};
                        std::string name = std::string("titlebar alignment ") + routes[route] + " " + getters[getter] + " " + actions[action];
                        run(name.c_str(), [=](Fixture& f) {
                            Require(jalium_apple_window_set_titlebar_extended(f.window, 1) == 0 &&
                                jalium_apple_window_set_titlebar_content_height(f.window, 48) == 0, "titlebar fixture setup failed");
                            NSView* content = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                            NSButton* button = [f.native standardWindowButton:NSWindowCloseButton];
                            id target = getter < 3 ? (id)f.native : getter < 5 ? (id)content : getter < 8 ? (id)button : (id)button.superview;
                            id<NSWindowDelegate> delegate = f.native.delegate;
                            std::unique_ptr<Fixture> replacement; NSRect replacementFrame = NSZeroRect, changedFrame = NSZeroRect;
                            RestoreSnapshotProbe probe(target, selectors[getter], [&] {
                                if (action == 1 || action == 2) {
                                    f.Destroy();
                                    if (action == 2) {
                                        replacement = std::make_unique<Fixture>(style, 500, 300, 560, 360);
                                        replacementFrame = replacement->native.frame; replacement->events.clear();
                                    }
                                } else if (action == 3) Require(jalium_apple_window_set_titlebar_content_height(f.window, 72) == 0, "nested height failed");
                                else if (action == 4) { changedFrame = f.ChangedFrame(); [f.native setFrame:changedFrame display:NO]; changedFrame = f.native.frame; }
                                else if (action == 5) Require(jalium_apple_window_set_titlebar_extended(f.window, 0) == 0, "nested extension disable failed");
                            });
                            f.events.clear(); int result = 0;
                            if (route == 0) [delegate windowDidResize:[NSNotification notificationWithName:NSWindowDidResizeNotification object:f.native]];
                            else if (route == 1) [f.native layoutIfNeeded];
                            else result = jalium_apple_window_set_titlebar_content_height(f.window, 64);
                            Require(probe.called, "titlebar route did not read its probed getter");
                            if (action == 1 || action == 2) {
                                Require(!f.window, "titlebar callback did not retire original");
                                if (route == 2) Require(result == JALIUM_ERROR_INVALID_STATE, "closed height request reported success");
                                for (const auto& value : f.events) Require(value.type != JALIUM_EVENT_MOVE && value.type != JALIUM_EVENT_RESIZE,
                                    "retired titlebar callback published geometry");
                            } else {
                                Require(result == 0, "live height request failed");
                                probe.remainingActions = 0;
                                if (action == 5) {
                                    Require(!(f.native.styleMask & NSWindowStyleMaskFullSizeContentView), "older alignment restored disabled extension");
                                    Require(jalium_apple_window_set_titlebar_extended(f.window, 1) == 0, "extension could not be reenabled");
                                }
                                if (action == 4) Require(NSEqualRects(f.native.frame, changedFrame), "older alignment changed the new window frame");
                                checkTitlebar(f, action == 3 ? 72 : route == 2 ? 64 : 48);
                            }
                            if (replacement) Require(NSEqualRects(replacement->native.frame, replacementFrame) && replacement->events.empty(),
                                "retired titlebar alignment touched replacement");
                        });
                    }
                run("titlebar alignment unstable height remains bounded and recovers", [&](Fixture& f) {
                    Require(jalium_apple_window_set_titlebar_extended(f.window, 1) == 0, "extension failed");
                    int height = 48;
                    RestoreSnapshotProbe probe(f.native, @selector(styleMask), [&] {
                        Require(jalium_apple_window_set_titlebar_content_height(f.window, ++height) == 0, "nested height failed");
                    }, 1, 100000);
                    [f.native layoutIfNeeded];
                    Require(probe.called && probe.targetReads < 1000, "unstable alignment did not remain bounded");
                    probe.remainingActions = 0;
                    Require(jalium_apple_window_set_titlebar_content_height(f.window, 48) == 0, "stable height could not recover");
                    checkTitlebar(f, 48);
                });
                for (double height : {24.0, 48.0, 72.0}) run((std::string("titlebar alignment stable height ") + std::to_string((int)height)).c_str(), [&](Fixture& f) {
                    Require(jalium_apple_window_set_titlebar_extended(f.window, 1) == 0, "extension failed");
                    NSRect frame = f.native.frame;
                    Require(jalium_apple_window_set_titlebar_content_height(f.window, height) == 0, "height failed");
                    Require(NSEqualRects(frame, f.native.frame), "toolbar height changed the outer window frame");
                    checkTitlebar(f, height);
                });
            }
            run("titlebar alignment ordinary caption remains unchanged", [](Fixture& f) {
                NSRect frame = f.native.frame;
                Require(jalium_apple_window_set_titlebar_content_height(f.window, 72) == 0, "plain caption height failed");
                Require(NSEqualRects(frame, f.native.frame) && !(f.native.styleMask & NSWindowStyleMaskFullSizeContentView),
                    "plain caption became extended or changed geometry");
            });
            auto notifyGeometry = [](Fixture& f, int event, id<NSWindowDelegate> delegate) {
                NSNotification* note = [NSNotification notificationWithName:event == 0 ? NSWindowDidResizeNotification :
                    event == 1 ? NSWindowDidMoveNotification : NSWindowDidChangeBackingPropertiesNotification object:f.native];
                if (event == 0) [delegate windowDidResize:note];
                else if (event == 1) [delegate windowDidMove:note];
                else [delegate windowDidChangeBackingProperties:note];
            };
            const char* geometryEvents[] = {"resize", "move", "DPI"};
            const char* geometryGetters[] = {"frame", "bounds", "screen", "backing scale", "live resize"};
            SEL geometrySelectors[] = {@selector(frame), @selector(bounds), @selector(screen),
                @selector(backingScaleFactor), @selector(inLiveResize)};
            for (int event = 0; event < 3; ++event) for (int getter = 0; getter < 5; ++getter) {
                if ((getter == 3 && event != 2) || (getter == 4 && event != 0)) continue;
                for (int action = 0; action < 4; ++action) {
                    const char* actions[] = {"keeps live geometry", "closes its original", "preserves a replacement", "retains newer geometry"};
                    std::string name = std::string("geometry callback ") + geometryEvents[event] + " " +
                        geometryGetters[getter] + " " + actions[action];
                    run(name.c_str(), [=](Fixture& f) {
                        id<NSWindowDelegate> delegate = f.native.delegate;
                        std::unique_ptr<Fixture> replacement;
                        NSRect replacementFrame = NSZeroRect, changedFrame = NSZeroRect;
                        RestoreSnapshotProbe probe(f, geometrySelectors[getter], [&] {
                            if (!action) return;
                            if (action == 3) {
                                changedFrame = f.ChangedFrame();
                                [f.native setFrame:changedFrame display:NO];
                                changedFrame = f.native.frame;
                                return;
                            }
                            f.Destroy();
                            if (action == 2) {
                                replacement = std::make_unique<Fixture>(style, 500, 300, 560, 360);
                                replacementFrame = replacement->native.frame;
                                replacement->events.clear();
                            }
                        });
                        notifyGeometry(f, event, delegate);
                        Require(probe.called, "geometry callback did not read its probed getter");
                        if (action == 1 || action == 2) {
                            Require(!f.window, "geometry getter did not retire its original owner");
                            for (const auto& value : f.events)
                                Require(value.type != JALIUM_EVENT_MOVE && value.type != JALIUM_EVENT_RESIZE &&
                                    value.type != JALIUM_EVENT_DPI_CHANGED, "retired geometry callback published an event");
                        }
                        if (replacement) {
                            Require(NSEqualRects(replacement->native.frame, replacementFrame) &&
                                jalium_window_get_state(replacement->window) == JALIUM_WINDOW_STATE_NORMAL,
                                "retired geometry callback changed replacement frame or state");
                            Require(replacement->events.empty(), "retired geometry callback published to replacement");
                        }
                        if (!action || action == 3) {
                            NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                            NSSize actualSize = view.bounds.size;
                            float scale = jalium_window_get_dpi_scale(f.window);
                            int width, height; jalium_window_get_client_size(f.window, &width, &height);
                            NSRect position = RestoreBounds(f);
                            Require(width == lround(actualSize.width * scale) && height == lround(actualSize.height * scale),
                                "geometry callback left stale cached client dimensions");
                            if (action == 3) Require(NSEqualRects(f.native.frame, changedFrame), "older geometry callback replaced the new frame");
                            for (const auto& value : f.events) {
                                if (value.type == JALIUM_EVENT_MOVE) Require(value.move.x == lround(position.origin.x) &&
                                    value.move.y == lround(position.origin.y), "older geometry callback published stale position");
                                if (value.type == JALIUM_EVENT_RESIZE) Require(value.resize.width == width && value.resize.height == height,
                                    "older geometry callback published stale dimensions");
                                if (value.type == JALIUM_EVENT_DPI_CHANGED) Require(value.dpiChanged.suggestedWidth == width &&
                                    value.dpiChanged.suggestedHeight == height, "older DPI callback published stale dimensions");
                            }
                        }
                    });
                }
            }
            for (int event = 0; event < 3; ++event) {
                for (int getter = 0; getter < 2; ++getter) {
                    std::string name = std::string("geometry callback ") + geometryEvents[event] + " " +
                        geometryGetters[getter] + " retires during reread";
                    run(name.c_str(), [=](Fixture& f) {
                        id<NSWindowDelegate> delegate = f.native.delegate;
                        RestoreSnapshotProbe probe(f, geometrySelectors[getter], [&] { f.Destroy(); }, 2);
                        notifyGeometry(f, event, delegate);
                        Require(probe.called && !f.window, "geometry reread did not retire the original owner");
                        for (const auto& value : f.events)
                            Require(value.type != JALIUM_EVENT_MOVE && value.type != JALIUM_EVENT_RESIZE &&
                                value.type != JALIUM_EVENT_DPI_CHANGED, "retired reread published geometry");
                    });
                }
                std::string name = std::string("geometry callback ") + geometryEvents[event] + " bounds rejects an unstable snapshot";
                run(name.c_str(), [=](Fixture& f) {
                    id<NSWindowDelegate> delegate = f.native.delegate;
                    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    NSRect originalBounds = view.bounds;
                    int width, height; jalium_window_get_client_size(f.window, &width, &height);
                    RestoreSnapshotProbe probe(f, @selector(bounds), [&] {
                        NSRect bounds = view.bounds; bounds.size.width += 1; view.bounds = bounds;
                    }, 1, 100000);
                    notifyGeometry(f, event, delegate);
                    Require(probe.called && probe.targetReads < 1000, "geometry read loop did not remain bounded");
                    for (const auto& value : f.events)
                        Require(value.type != JALIUM_EVENT_MOVE && value.type != JALIUM_EVENT_RESIZE &&
                            value.type != JALIUM_EVENT_DPI_CHANGED, "unstable snapshot published partial geometry");
                    int cachedWidth, cachedHeight; jalium_window_get_client_size(f.window, &cachedWidth, &cachedHeight);
                    Require(cachedWidth == width && cachedHeight == height, "unstable snapshot replaced the previous cache");
                    probe.remainingActions = 0; view.bounds = originalBounds;
                    notifyGeometry(f, event, delegate);
                    Require(!f.events.empty(), "unstable getter blocked later stable geometry");
                });
            }
            for (bool replace : {false, true}) {
                std::string name = std::string("geometry callback DPI delivery ") + (replace ? "preserves replacement" : "closes original");
                run(name.c_str(), [=](Fixture& f) {
                    id<NSWindowDelegate> delegate = f.native.delegate;
                    struct Context { Fixture* fixture; uint32_t style; bool replace, called = false;
                        std::unique_ptr<Fixture> replacement; NSRect frame; } context{&f, style, replace};
                    jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* value, void* data) {
                        auto& c = *static_cast<Context*>(data); c.fixture->events.push_back(*value);
                        if (value->type != JALIUM_EVENT_DPI_CHANGED || c.called) return;
                        c.called = true; c.fixture->Destroy();
                        if (c.replace) {
                            c.replacement = std::make_unique<Fixture>(c.style, 500, 300, 560, 360);
                            c.frame = c.replacement->native.frame; c.replacement->events.clear();
                        }
                    }, &context);
                    notifyGeometry(f, 2, delegate);
                    Require(context.called && !f.window, "DPI delivery did not close the original");
                    Require(!f.events.empty() && f.events.front().type == JALIUM_EVENT_DPI_CHANGED,
                        "DPI delivery lost its original event");
                    for (const auto& value : f.events) Require(value.type == JALIUM_EVENT_DPI_CHANGED ||
                        value.type == JALIUM_EVENT_DESTROYED, "DPI delivery continued into a retired resize");
                    if (context.replacement) Require(context.replacement->events.empty() &&
                        NSEqualRects(context.replacement->native.frame, context.frame), "retired DPI delivery changed replacement");
                });
            }
            const SEL captureSelectors[] = {@selector(frame), @selector(bounds), @selector(styleMask)};
            const char* captureOperations[] = {"maximize", "native Zoom", "native minimize", "fullscreen will"};
            for (int shift : {-1, 1, 20}) {
                std::string name = "restore capture geometry Fill follows work area correction " + std::to_string(shift);
                run(name.c_str(), [=](Fixture& f) {
                    NSRect normal = f.native.frame, restore = RestoreBounds(f);
                    WorkAreaProbe probe(f.native.screen);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    f.events.clear();
                    probe.visible.origin.y += shift; probe.visible.size.height -= shift;
                    [f.native setFrame:probe.visible display:NO];
                    Require(!f.native.inLiveResize && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED &&
                        NSEqualRects(f.native.frame, probe.visible) && NSEqualRects(RestoreBounds(f), restore),
                        "work-area correction normalized Fill or replaced its normal restore geometry");
                    for (const auto& event : f.events) {
                        Require(event.type != JALIUM_EVENT_STATE_CHANGED, "work-area correction published Normal");
                        Require(event.type != JALIUM_EVENT_RESIZE || !event.resize.isUserInitiated,
                            "work-area correction was reported as a user resize");
                    }
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                    Require(NSEqualRects(f.native.frame, normal) && NSEqualRects(RestoreBounds(f), restore),
                        "Fill could not restore after the work area changed");
                });
            }
            for (int change : {1, 20}) {
                std::string name = "restore capture geometry Fill normalizes a non-work-area resize " + std::to_string(change);
                run(name.c_str(), [=](Fixture& f) {
                    WorkAreaProbe probe(f.native.screen);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    f.events.clear();
                    NSRect edited = f.native.frame; edited.size.width -= change;
                    [f.native setFrame:edited display:NO];
                    Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL &&
                        NSEqualRects(f.native.frame, edited), "a non-work-area resize remained Maximized");
                    bool user = false;
                    for (const auto& event : f.events) if (event.type == JALIUM_EVENT_RESIZE) user |= event.resize.isUserInitiated;
                    Require(user, "a non-work-area resize lost its user-initiated classification");
                });
            }
            for (bool replace : {false, true}) {
                std::string name = std::string("restore capture geometry Fill work-area getter ") +
                    (replace ? "preserves a replacement window" : "closes its original");
                run(name.c_str(), [=](Fixture& f) {
                    WorkAreaProbe probe(f.native.screen);
                    BestFitZoom delegate(f.native);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    probe.visible.origin.y += 1; probe.visible.size.height -= 1;
                    bool called = false;
                    std::unique_ptr<Fixture> replacement;
                    NSRect replacementFrame, replacementRestore;
                    probe.ready = [&] { return delegate.delegate.forwardingResize; };
                    probe.onRead = [&] {
                        called = true; f.Destroy();
                        if (replace) {
                            replacement = std::make_unique<Fixture>(style, 500, 300, 560, 360);
                            replacementFrame = replacement->native.frame; replacementRestore = RestoreBounds(*replacement);
                        }
                    };
                    [f.native setFrame:probe.visible display:NO];
                    Require(called && !f.window, "work-area getter did not execute its close callback during resize");
                    if (replacement)
                        Require(jalium_window_get_state(replacement->window) == JALIUM_WINDOW_STATE_NORMAL &&
                            NSEqualRects(replacement->native.frame, replacementFrame) &&
                            NSEqualRects(RestoreBounds(*replacement), replacementRestore),
                            "retired work-area correction changed the replacement window");
                });
            }
            for (int getter = 0; getter < 3; ++getter) {
                for (int operation = 0; operation < 4; ++operation) {
                    for (int action = 0; action < 4; ++action) for (int initialShift : {0, -2}) {
                        const char* actions[] = {"Resize", "Move", "native frame", "repeated native frame"};
                        std::string name = std::string("restore capture geometry ") + captureOperations[operation] +
                            " " + captureGetters[getter] + " retains " + actions[action];
                        if (initialShift) name += " with adjusted initial frame";
                        run(name.c_str(), [=](Fixture& f) {
                            if (initialShift) {
                                NSRect frame = f.native.frame;
                                frame.origin.y += initialShift;
                                [f.native setFrame:frame display:NO];
                                Require(NSEqualRects(f.native.frame, frame),
                                    "geometry target could not apply its adjusted initial frame");
                                f.events.clear();
                            }
                            auto changeGeometry = [action](Fixture& target) {
                                if (action == 0) jalium_window_resize(target.window, 720, 540);
                                else if (action == 1) jalium_window_move(target.window, 344, 312);
                                else [target.native setFrame:target.ChangedFrame() display:NO];
                            };
                            NSRect expectedFrame, expectedRestore;
                            NSRect initialTarget = f.native.frame;
                            int repetitions = action == 3 ? 4 : 1;
                            std::vector<NSRect> expectedFrames;
                            std::vector<NSSize> expectedContents;
                            {
                                // Let an equivalent NSWindow establish its own content-size
                                // anchor, screen conversion and frame alignment independently.
                                Fixture control(style);
                                // First display can constrain a new reference differently
                                // from the existing target after the work area changes.
                                // Both primitives must start from the same actual frame.
                                [control.native setFrame:initialTarget display:NO];
                                Require(NSEqualRects(control.native.frame, initialTarget),
                                    "geometry reference did not start from the target frame");
                                for (int step = 0; step < repetitions; ++step) {
                                    changeGeometry(control);
                                    expectedFrames.push_back(control.native.frame);
                                    expectedContents.push_back(((__bridge NSView*)(void*)jalium_window_get_native_handle(control.window)).bounds.size);
                                }
                                expectedFrame = control.native.frame;
                                expectedRestore = RestoreBounds(control);
                            }
                            BestFitZoom bestFit(f.native);
                            ControlledFullScreen fullscreen(f.native);
                            NSRect targetBeforeCapture = f.native.frame;
                            int changes = 0;
                            RestoreSnapshotProbe probe(f, captureSelectors[getter], [&] {
                                changeGeometry(f);
                                NSRect actualFrame = f.native.frame;
                                NSSize actualContent = ((__bridge NSView*)(void*)jalium_window_get_native_handle(f.window)).bounds.size;
                                bool applied = NSEqualRects(actualFrame, expectedFrames.at(changes)) &&
                                    NSEqualSizes(actualContent, expectedContents.at(changes));
                                if (!applied) {
                                    NSScreen* screen = f.native.screen;
                                    std::fprintf(stderr, "Restore callback geometry: getter=%s operation=%s action=%s step=%d initial=%s before-capture=%s expected-frame=%s actual-frame=%s expected-content=%s actual-content=%s screen=%s work=%s scale=%.2f state=%d mini=%d\n",
                                        captureGetters[getter], captureOperations[operation], actions[action], changes,
                                        NSStringFromRect(initialTarget).UTF8String, NSStringFromRect(targetBeforeCapture).UTF8String,
                                        NSStringFromRect(expectedFrames.at(changes)).UTF8String, NSStringFromRect(actualFrame).UTF8String,
                                        NSStringFromSize(expectedContents.at(changes)).UTF8String, NSStringFromSize(actualContent).UTF8String,
                                        NSStringFromRect(screen.frame).UTF8String, NSStringFromRect(screen.visibleFrame).UTF8String,
                                        screen.backingScaleFactor, jalium_window_get_state(f.window), f.native.miniaturized);
                                }
                                Require(applied,
                                    "getter callback did not apply the independent geometry");
                                ++changes;
                            }, 1, repetitions);
                            bool minimized = false;
                            WindowNotification acknowledged(f, NSWindowDidMiniaturizeNotification, [&] { minimized = true; });
                            if (operation == 0) jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                            else if (operation == 1) [f.native zoom:nil];
                            else if (operation == 2) [f.native miniaturize:nil];
                            else fullscreen.Will();
                            Require(probe.called && changes == repetitions,
                                "geometry case did not exercise every getter callback");
                            if (operation == 2) {
                                Require(Pump([&] { return minimized && f.native.miniaturized; }),
                                    "geometry capture did not reach actual native minimize completion");
                            } else if (operation == 3) {
                                fullscreen.Complete();
                                Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_FULLSCREEN; }),
                                    "geometry capture did not reach controlled fullscreen completion");
                            } else {
                                Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED,
                                    "geometry capture lost its requested maximized state");
                            }
                            NSRect saved = RestoreBounds(f);
                            std::printf("Restore geometry: getter=%s operation=%s action=%s expected=%s saved=%s\n",
                                captureGetters[getter], captureOperations[operation], actions[action],
                                NSStringFromRect(expectedRestore).UTF8String, NSStringFromRect(saved).UTF8String);
                            Require(NSEqualRects(saved, expectedRestore),
                                "capture replaced callback geometry with an older restore tuple");
                            jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                            if (operation == 3) fullscreen.Complete();
                            Require(Pump([&] { return !f.native.miniaturized &&
                                jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }),
                                "geometry capture could not restore its normal state");
                            Require(NSEqualRects(f.native.frame, expectedFrame) &&
                                NSEqualRects(RestoreBounds(f), expectedRestore),
                                "normal restoration lost the callback's complete geometry");
                        });
                    }
                }
            }
            for (int operation = 0; operation < 3; ++operation) {
                std::string name = std::string("restore capture geometry ") + captureOperations[operation] +
                    " rejects continuously changing bounds without blocking";
                run(name.c_str(), [=](Fixture& f) {
                    NSRect normal = f.native.frame;
                    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    NSRect normalBounds = view.bounds;
                    bool minimized = false;
                    WindowNotification acknowledged(f, NSWindowDidMiniaturizeNotification, [&] { minimized = true; });
                    RestoreSnapshotProbe probe(f, @selector(bounds), [&] {
                        NSRect bounds = view.bounds;
                        bounds.size.width += 1;
                        view.bounds = bounds;
                    }, 1, 100000);
                    if (operation == 0) jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    else if (operation == 1) [f.native zoom:nil];
                    else [f.native miniaturize:nil];
                    probe.remainingActions = 0;
                    if (operation == 2)
                        Require(!Pump([&] { return minimized || f.native.miniaturized; }),
                            "incoherent capture reached actual native minimize completion");
                    Require(probe.called && probe.targetReads < 1000,
                        "capture kept reading a continuously changing getter");
                    Require(!f.native.miniaturized && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL &&
                        NSEqualRects(f.native.frame, normal),
                        "incoherent capture applied a state or replaced normal geometry");
                    for (const auto& event : f.events)
                        Require(event.type != JALIUM_EVENT_STATE_CHANGED,
                            "incoherent capture published a state");
                    view.bounds = normalBounds;
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED,
                        "incoherent capture blocked a later stable state request");
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                    Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL &&
                        NSEqualRects(f.native.frame, normal) && NSEqualRects(view.bounds, normalBounds),
                        "capture recovery lost the original normal geometry");
                });
            }
            for (int getter = 0; getter < 3; ++getter) {
                for (int operation = 0; operation < 4; ++operation) {
                    std::string name = std::string("restore capture reread ") + captureOperations[operation] +
                        " " + captureGetters[getter] + " closes the original";
                    run(name.c_str(), [=](Fixture& f) {
                        NSRect normal = f.native.frame;
                        BestFitZoom bestFit(f.native);
                        ControlledFullScreen fullscreen(f.native);
                        RestoreSnapshotProbe probe(f, captureSelectors[getter], [&] { f.Destroy(); }, 2);
                        if (operation == 0) jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                        else if (operation == 1) [f.native zoom:nil];
                        else if (operation == 2) [f.native miniaturize:nil];
                        else fullscreen.Will();
                        Require(probe.called && !f.window && NSEqualRects(f.native.frame, normal),
                            "reread closure kept changing its retired native window");
                        for (const auto& event : f.events)
                            Require(event.type != JALIUM_EVENT_STATE_CHANGED,
                                "reread closure published a state after retirement");
                    });
                }
            }
            for (int getter = 0; getter < 3; ++getter) {
                for (int operation = 0; operation < 4; ++operation) {
                    for (int action = 0; action < 3; ++action) {
                        const char* actions[] = {"keeps live geometry", "closes the original", "preserves a replacement window"};
                        std::string name = std::string("restore capture ") + captureOperations[operation] +
                            " " + captureGetters[getter] + " " + actions[action];
                        run(name.c_str(), [=](Fixture& f) {
                            NSRect normal = f.native.frame, restore = RestoreBounds(f);
                            std::unique_ptr<Fixture> replacement;
                            NSRect replacementFrame = NSZeroRect, replacementRestore = NSZeroRect;
                            BestFitZoom bestFit(f.native);
                            ControlledFullScreen fullscreen(f.native);
                            RestoreSnapshotProbe probe(f, captureSelectors[getter], [&] {
                                if (!action) return;
                                auto* address = f.window;
                                f.Destroy();
                                if (action == 2) {
                                    replacement = std::make_unique<Fixture>(style, 500, 300, 560, 360);
                                    replacementFrame = replacement->native.frame;
                                    replacementRestore = RestoreBounds(*replacement);
                                    std::printf("Restore capture replacement: reusedAddress=%d\n", replacement->window == address);
                                }
                            });
                            bool completed = false;
                            WindowNotification acknowledged(f, NSWindowDidMiniaturizeNotification, [&] { completed = true; });
                            if (operation == 0) jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                            else if (operation == 1) [f.native zoom:nil];
                            else if (operation == 2) [f.native miniaturize:nil];
                            else fullscreen.Will();
                            Require(probe.called, "restore capture did not exercise the getter callback");
                            if (action) {
                                Require(!f.window && NSEqualRects(f.native.frame, normal),
                                    "retired capture continued changing its original native window");
                                for (const auto& event : f.events)
                                    Require(event.type != JALIUM_EVENT_STATE_CHANGED,
                                        "retired capture published another state");
                                if (replacement) {
                                    Require(jalium_window_get_state(replacement->window) == JALIUM_WINDOW_STATE_NORMAL &&
                                        NSEqualRects(replacement->native.frame, replacementFrame) &&
                                        NSEqualRects(RestoreBounds(*replacement), replacementRestore),
                                        "retired capture changed its replacement's state or geometry");
                                    for (const auto& event : replacement->events)
                                        Require(event.type != JALIUM_EVENT_STATE_CHANGED,
                                            "retired capture dispatched a state to its replacement");
                                }
                                return;
                            }
                            if (operation == 2) {
                                Require(Pump([&] { return completed && f.native.miniaturized; }),
                                    "live capture did not reach actual native minimize completion");
                            } else if (operation == 3) {
                                fullscreen.Complete();
                                Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_FULLSCREEN; }),
                                    "live fullscreen capture did not publish its completion");
                            } else {
                                Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED,
                                    "live capture lost its requested maximized state");
                            }
                            Require(NSEqualRects(RestoreBounds(f), restore), "live capture changed normal restore bounds");
                            jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                            if (operation == 3) fullscreen.Complete();
                            Require(Pump([&] { return !f.native.miniaturized &&
                                jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }),
                                "live capture could not restore its normal state");
                            Require(NSEqualRects(f.native.frame, normal) && NSEqualRects(RestoreBounds(f), restore),
                                "live capture lost its final normal geometry");
                        });
                    }
                    if (operation == 3) continue;
                    std::string name = std::string("restore capture ") + captureOperations[operation] +
                        " " + captureGetters[getter] + " preserves a newer normal request";
                    run(name.c_str(), [=](Fixture& f) {
                        NSRect normal = f.native.frame, restore = RestoreBounds(f);
                        RestoreSnapshotProbe probe(f, captureSelectors[getter], [&] {
                            jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                        });
                        if (operation == 0) jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                        else if (operation == 1) [f.native zoom:nil];
                        else [f.native miniaturize:nil];
                        Require(probe.called, "newer request did not exercise the getter callback");
                        Require(!f.native.miniaturized && jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL &&
                            NSEqualRects(f.native.frame, normal) && NSEqualRects(RestoreBounds(f), restore),
                            "older capture overrode a newer normal request");
                        for (const auto& event : f.events)
                            Require(event.type != JALIUM_EVENT_STATE_CHANGED,
                                "older capture published a state after its request was replaced");
                    });
                }
                std::string name = std::string("restore capture fullscreen will ") + captureGetters[getter] +
                    " preserves a newer normal request";
                run(name.c_str(), [=](Fixture& f) {
                    NSRect normal = f.native.frame, restore = RestoreBounds(f);
                    ControlledFullScreen fullscreen(f.native);
                    RestoreSnapshotProbe probe(f, captureSelectors[getter], [&] {
                        jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                    });
                    fullscreen.Will();
                    Require(probe.called, "fullscreen did not exercise the getter callback");
                    fullscreen.Complete();
                    Require(Pump([&] { return fullscreen.toggles == 1; }),
                        "fullscreen capture lost the newer normal request");
                    fullscreen.Complete();
                    Require(Pump([&] { return jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }) &&
                        NSEqualRects(f.native.frame, normal) && NSEqualRects(RestoreBounds(f), restore),
                        "fullscreen capture replaced normal restore geometry");
                });
            }
            for (auto requested : {JALIUM_WINDOW_STATE_NORMAL, JALIUM_WINDOW_STATE_MINIMIZED,
                JALIUM_WINDOW_STATE_MAXIMIZED}) {
                std::string name = "restore capture state validation preserves newer native Zoom " +
                    std::to_string(static_cast<int>(requested));
                run(name.c_str(), [=](Fixture& f) {
                    NSRect restore = RestoreBounds(f);
                    BestFitZoom bestFit(f.native);
                    bool minimized = false;
                    WindowNotification acknowledged(f, NSWindowDidMiniaturizeNotification, [&] { minimized = true; });
                    RestoreSnapshotProbe probe(f, @selector(styleMask), [&] { [f.native zoom:nil]; });
                    jalium_window_set_state(f.window, requested);
                    Require(probe.called, "state validation did not exercise the native Zoom callback");
                    if (requested == JALIUM_WINDOW_STATE_MINIMIZED)
                        Require(!Pump([&] { return minimized; }), "an older state minimized the newer native Zoom");
                    Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED &&
                        NSEqualRects(f.native.frame, bestFit.delegate.standardFrame) &&
                        NSEqualRects(RestoreBounds(f), restore),
                        "an older state overrode native Zoom best-fit or normal restore geometry");
                });
            }
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
                NSSize maximum = ScreenFittingClientMaximum(f);
                Require(jalium_window_set_min_max_size(f.window, 480, 360, maximum.width, maximum.height) == 0,
                    "size constraints failed");
                NSRect requested = f.native.frame;
                requested.size = NSMakeSize(40, 40);
                [f.native setAccessibilityFrame:requested];
                int width, height;
                jalium_window_get_client_size(f.window, &width, &height);
                Require(width == 480 && height == 360, "AX size did not clamp to the client minimum");
                requested = f.native.frame; requested.size = NSMakeSize(1000, 1000);
                [f.native setAccessibilityFrame:requested];
                jalium_window_get_client_size(f.window, &width, &height);
                Require(width == maximum.width && height == maximum.height, "AX size did not clamp to the client maximum");
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
                Require(Pump([&] { return !f.native.miniaturized &&
                    jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL &&
                    Settable(f.native, NSAccessibilityPositionAttribute); }),
                    "rejected minimization left its transition active");
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                Require(jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED,
                    "later state request remained queued after native rejection");
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                Require(NSEqualRects(RestoreBounds(f), restore), "rejected minimization lost normal geometry");
            });
            for (auto initial : {JALIUM_WINDOW_STATE_NORMAL, JALIUM_WINDOW_STATE_MAXIMIZED})
                for (auto requested : {JALIUM_WINDOW_STATE_NORMAL, JALIUM_WINDOW_STATE_MAXIMIZED})
                    for (bool nativeAction : {false, true}) {
                        char name[160];
                        std::snprintf(name, sizeof(name), "minimize start queues %s from %s via %s",
                            requested == JALIUM_WINDOW_STATE_NORMAL ? "restore" : "maximize",
                            initial == JALIUM_WINDOW_STATE_NORMAL ? "normal" : "maximized",
                            nativeAction ? "native action" : "platform API");
                        run(name, [=](Fixture& f) {
                            NSRect restore = RestoreBounds(f);
                            jalium_window_set_state(f.window, initial);
                            NSRect frame = f.native.frame;
                            f.events.clear();
                            DeferredMinimizationStart delayed(f.native);
                            if (nativeAction) [f.native miniaturize:nil];
                            else jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MINIMIZED);
                            jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                            jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                            jalium_window_set_state(f.window, requested);
                            Require(!f.native.miniaturized && jalium_window_get_state(f.window) == initial &&
                                NSEqualRects(f.native.frame, frame), "request changed geometry before native minimize start");
                            Require(!Settable(f.native, NSAccessibilityPositionAttribute) &&
                                !Settable(f.native, NSAccessibilitySizeAttribute), "pending native start exposed writable geometry");
                            for (const auto& event : f.events)
                                Require(event.type != JALIUM_EVENT_STATE_CHANGED,
                                    "queued state was published before native minimize start");
                            bool completed = false;
                            WindowNotification acknowledged(f, NSWindowDidMiniaturizeNotification, [&] { completed = true; });
                            delayed.Complete();
                            Require(Pump([&] { return completed && !f.native.miniaturized &&
                                jalium_window_get_state(f.window) == requested; }), "native start lost the latest queued state");
                            Require(NSEqualRects(RestoreBounds(f), restore), "native start replaced normal restore bounds");
                        });
                    }
            run("minimize start preserves paired native Zoom cancellation", [](Fixture& f) {
                NSRect restore = RestoreBounds(f), frame = f.native.frame;
                DeferredMinimizationStart delayed(f.native);
                [f.native miniaturize:nil];
                f.events.clear();
                [f.native zoom:nil]; [f.native zoom:nil];
                Require(NSEqualRects(f.native.frame, frame), "paired Zoom changed frame before native minimize start");
                for (const auto& event : f.events)
                    Require(event.type != JALIUM_EVENT_STATE_CHANGED,
                        "paired Zoom was published before native minimize start");
                bool completed = false;
                WindowNotification acknowledged(f, NSWindowDidMiniaturizeNotification, [&] { completed = true; });
                delayed.Complete();
                Require(Pump([&] { return completed && !f.native.miniaturized &&
                    jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }), "native start lost paired Zoom cancellation");
                Require(NSEqualRects(RestoreBounds(f), restore), "paired start Zoom replaced normal restore bounds");
            });
            run("minimize start retains native Zoom best-fit", [](Fixture& f) {
                NSRect restore = RestoreBounds(f), frame = f.native.frame;
                BestFitZoom bestFit(f.native);
                DeferredMinimizationStart delayed(f.native);
                [f.native miniaturize:nil];
                [f.native zoom:nil];
                Require(NSEqualRects(f.native.frame, frame), "native Zoom changed frame before minimize start");
                bool completed = false;
                WindowNotification acknowledged(f, NSWindowDidMiniaturizeNotification, [&] { completed = true; });
                delayed.Complete();
                Require(Pump([&] { return completed && !f.native.miniaturized &&
                    jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_MAXIMIZED; }), "native start lost best-fit Zoom");
                Require(NSEqualRects(f.native.frame, bestFit.delegate.standardFrame) &&
                    NSEqualRects(RestoreBounds(f), restore), "native start replaced best-fit or normal geometry");
            });
            for (auto requested : {JALIUM_WINDOW_STATE_NORMAL, JALIUM_WINDOW_STATE_MAXIMIZED}) {
                run(requested == JALIUM_WINDOW_STATE_NORMAL
                    ? "minimize start after recovery preserves latest restore"
                    : "minimize start after recovery preserves latest maximize", [=](Fixture& f) {
                    NSRect restore = RestoreBounds(f);
                    DeferredMinimizationStart delayed(f.native);
                    [f.native miniaturize:nil];
                    Require(Pump([&] { return Settable(f.native, NSAccessibilityPositionAttribute); }),
                        "missing native start blocked later geometry forever");
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                    jalium_window_set_state(f.window, requested);
                    Require(jalium_window_get_state(f.window) == requested,
                        "later state remained blocked after missing-start recovery");
                    bool completed = false;
                    WindowNotification acknowledged(f, NSWindowDidMiniaturizeNotification, [&] { completed = true; });
                    delayed.Complete();
                    Require(Pump([&] { return completed && !f.native.miniaturized &&
                        jalium_window_get_state(f.window) == requested; }), "late native start overwrote the latest state");
                    Require(NSEqualRects(RestoreBounds(f), restore), "late native start replaced normal restore bounds");
                });
            }
            for (int geometry = 0; geometry < 3; ++geometry) {
                const char* names[] = {"minimize start preserves explicit resize",
                    "minimize start preserves explicit move", "minimize start preserves explicit resize then move"};
                run(names[geometry], [=](Fixture& f) {
                    NSRect expected = RestoreBounds(f);
                    NSRect originalFrame = NSZeroRect;
                    NSRect referenceOrderedFrame = NSZeroRect, referenceFrame = NSZeroRect;
                    NSRect referenceWork = NSZeroRect;
                    DeferredMinimizationStart delayed(f.native);
                    [f.native miniaturize:nil];
                    if (geometry != 1) {
                        originalFrame = f.native.frame;
                        NSWindow* reference = [[NSWindow alloc] initWithContentRect:
                            [f.native contentRectForFrameRect:originalFrame] styleMask:f.native.styleMask
                            backing:NSBackingStoreBuffered defer:YES];
                        reference.releasedWhenClosed = NO;
                        reference.contentMinSize = f.native.contentMinSize;
                        reference.contentMaxSize = f.native.contentMaxSize;
                        [reference setFrame:originalFrame display:NO];
                        [reference orderFront:nil];
                        referenceOrderedFrame = reference.frame;
                        // Match the already displayed target after AppKit has applied
                        // first-show constraints to this independent reference.
                        [reference setFrame:originalFrame display:NO];
                        Require(NSEqualRects(reference.frame, originalFrame),
                            "resize reference did not start from the target frame");
                        double scale = jalium_window_get_dpi_scale(f.window);
                        [reference setContentSize:NSMakeSize(600 / scale, 400 / scale)];
                        referenceFrame = reference.frame;
                        referenceWork = reference.screen.visibleFrame;
                        expected.origin.x += (NSMinX(referenceFrame) - NSMinX(originalFrame)) * scale;
                        expected.origin.y += (NSMaxY(originalFrame) - NSMaxY(referenceFrame)) * scale;
                        [reference close];
                        jalium_window_resize(f.window, 600, 400);
                        expected.size = NSMakeSize(600, 400);
                    }
                    if (geometry != 0) {
                        jalium_window_move(f.window, 180, 200);
                        expected.origin = NSMakePoint(180, 200);
                    }
                    NSRect actual = RestoreBounds(f);
                    if (!NSEqualRects(actual, expected)) {
                        std::fprintf(stderr, "Minimize start geometry: expected=%s actual=%s native=%s scale=%.2f\n",
                            NSStringFromRect(expected).UTF8String, NSStringFromRect(actual).UTF8String,
                            NSStringFromRect(f.native.frame).UTF8String, jalium_window_get_dpi_scale(f.window));
                        NSScreen* screen = f.native.screen;
                        std::fprintf(stderr, "Minimize resize reference: original=%s ordered=%s resized=%s reference-work=%s screen=%s target-work=%s style=%lu class=%s\n",
                            NSStringFromRect(originalFrame).UTF8String, NSStringFromRect(referenceOrderedFrame).UTF8String,
                            NSStringFromRect(referenceFrame).UTF8String, NSStringFromRect(referenceWork).UTF8String,
                            NSStringFromRect(screen.frame).UTF8String, NSStringFromRect(screen.visibleFrame).UTF8String,
                            static_cast<unsigned long>(f.native.styleMask), object_getClassName(f.native));
                    }
                    Require(NSEqualRects(actual, expected), "pending start lost explicit normal geometry");
                    jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_NORMAL);
                    bool completed = false;
                    WindowNotification acknowledged(f, NSWindowDidMiniaturizeNotification, [&] { completed = true; });
                    delayed.Complete();
                    Require(Pump([&] { return completed && !f.native.miniaturized &&
                        jalium_window_get_state(f.window) == JALIUM_WINDOW_STATE_NORMAL; }), "explicit geometry did not return to normal");
                    Require(NSEqualRects(RestoreBounds(f), expected), "acknowledgement replaced explicit normal geometry");
                });
            }
            run("minimize start recovery ignores a retired target", [=](Fixture& f) {
                DeferredMinimizationStart delayed(f.native);
                [f.native miniaturize:nil];
                f.Destroy();
                Fixture replacement(style, 300, 200, 420, 300);
                NSRect frame = replacement.native.frame, restore = RestoreBounds(replacement);
                Pump([] { return false; }, 1.1);
                Require(NSEqualRects(replacement.native.frame, frame) &&
                    NSEqualRects(RestoreBounds(replacement), restore) &&
                    jalium_window_get_state(replacement.window) == JALIUM_WINDOW_STATE_NORMAL,
                    "retired minimize recovery changed its replacement");
            });
            run("minimize start recovery callback preserves its replacement", [=](Fixture& f) {
                DeferredMinimizationStart delayed(f.native);
                [f.native miniaturize:nil];
                jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MAXIMIZED);
                struct Context {
                    Fixture* original; uint32_t style; std::unique_ptr<Fixture> replacement; NSRect frame;
                } context{&f, style, nullptr, NSZeroRect};
                jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* data) {
                    auto& c = *static_cast<Context*>(data);
                    if (event->type != JALIUM_EVENT_STATE_CHANGED || c.replacement ||
                        event->stateChanged.newState != JALIUM_WINDOW_STATE_MAXIMIZED) return;
                    c.original->Destroy();
                    c.replacement = std::make_unique<Fixture>(c.style, 300, 200, 420, 300);
                    c.frame = c.replacement->native.frame;
                }, &context);
                Require(Pump([&] { return context.replacement != nullptr; }), "missing-start recovery did not drain the queued callback");
                Pump([] { return true; });
                Require(NSEqualRects(context.replacement->native.frame, context.frame) &&
                    jalium_window_get_state(context.replacement->window) == JALIUM_WINDOW_STATE_NORMAL,
                    "recovery callback changed a replacement window");
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
            const char* trackingModes[] = {"visible", "hidden", "ordered out", "hidden view", "hidden ancestor",
                "detached view", "disabled", "closed", "minimizing"};
            for (int mode = 0; mode < 9; ++mode) {
                std::string name = std::string("tracking entry revalidates ") + trackingModes[mode];
                run(name.c_str(), [=](Fixture& f) {
                    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    NSEvent* enter = [NSEvent enterExitEventWithType:NSEventTypeMouseEntered
                        location:NSMakePoint(10, 10) modifierFlags:0 timestamp:NSProcessInfo.processInfo.systemUptime
                        windowNumber:f.native.windowNumber context:nil eventNumber:1 trackingNumber:0 userData:nullptr];
                    Require(enter != nil, "tracking event creation failed");
                    if (mode == 1) jalium_window_hide(f.window);
                    else if (mode == 2) [f.native orderOut:nil];
                    else if (mode == 3) view.hidden = YES;
                    else if (mode == 4) {
                        Require(view.superview != nil, "tracking view has no ancestor");
                        view.superview.hidden = YES;
                    } else if (mode == 5) f.native.contentView = [NSView new];
                    else if (mode == 6) jalium_window_set_enabled(f.window, 0);
                    else if (mode == 7) f.Destroy();
                    f.events.clear();
                    auto entries = [&] {
                        return std::count_if(f.events.begin(), f.events.end(), [](const auto& event) {
                            return event.type == JALIUM_EVENT_MOUSE_ENTER;
                        });
                    };
                    if (mode == 8) {
                        bool started = false, enteredDuringMinimize = false;
                        WindowNotification began(f, NSWindowWillMiniaturizeNotification, [&] {
                            started = true;
                            [view mouseEntered:enter];
                            enteredDuringMinimize = entries() != 0;
                        });
                        jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MINIMIZED);
                        Require(Pump([&] { return f.native.miniaturized; }) && started,
                            "tracking fixture did not minimize");
                        Require(!enteredDuringMinimize, "tracking entry reached a minimizing surface");
                    }
                    [view mouseEntered:enter];
                    Require(entries() == (mode == 0 ? 1 : 0), "tracking entry reached an excluded surface");
                    if (mode == 0) {
                        NSEvent* exit = [NSEvent enterExitEventWithType:NSEventTypeMouseExited
                            location:NSMakePoint(10, 10) modifierFlags:0 timestamp:NSProcessInfo.processInfo.systemUptime
                            windowNumber:f.native.windowNumber context:nil eventNumber:2 trackingNumber:0 userData:nullptr];
                        [view mouseExited:exit];
                        Require(std::count_if(f.events.begin(), f.events.end(), [](const auto& event) {
                            return event.type == JALIUM_EVENT_MOUSE_LEAVE;
                        }) == 1, "visible tracking exit was lost");
                    }
                });
            }
            const char* trackingCallbacks[] = {"hide", "detach", "disable", "close"};
            for (int mode = 0; mode < 4; ++mode) {
                std::string name = std::string("tracking entry revalidates visibility callback ") + trackingCallbacks[mode];
                run(name.c_str(), [=](Fixture& f) {
                    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    NSEvent* enter = [NSEvent enterExitEventWithType:NSEventTypeMouseEntered
                        location:NSMakePoint(10, 10) modifierFlags:0 timestamp:NSProcessInfo.processInfo.systemUptime
                        windowNumber:f.native.windowNumber context:nil eventNumber:1 trackingNumber:0 userData:nullptr];
                    Require(enter != nil, "tracking callback event creation failed");
                    bool changed = false;
                    TrackingVisibilityProbe query(f.native, [&] {
                        changed = true;
                        if (mode == 0) jalium_window_hide(f.window);
                        else if (mode == 1) f.native.contentView = [NSView new];
                        else if (mode == 2) jalium_window_set_enabled(f.window, 0);
                        else f.Destroy();
                    });
                    f.events.clear();
                    [view mouseEntered:enter];
                    Require(changed, "tracking visibility callback did not run");
                    Require(std::none_of(f.events.begin(), f.events.end(), [](const auto& event) {
                        return event.type == JALIUM_EVENT_MOUSE_ENTER;
                    }), "tracking query revived a retired surface");
                });
            }
            const char* pointerModes[] = {"visible", "hidden", "ordered out", "hidden view", "hidden ancestor",
                "detached view", "disabled", "closed", "minimizing", "hidden but ordered front"};
            for (int mode = 0; mode < 10; ++mode) {
                std::string name = std::string("pointer packets revalidate ") + pointerModes[mode];
                run(name.c_str(), [=](Fixture& f) {
                    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    auto deliverAll = [&] {
                        for (int route = 0; route < 11; ++route)
                            DeliverPointerPacket(view, PointerPacket(route), route);
                    };
                    if (mode == 1 || mode == 9) jalium_window_hide(f.window);
                    if (mode == 9) {
                        [f.native orderFront:nil];
                        Require(f.native.visible, "logical-hidden control did not order front");
                    } else if (mode == 2) [f.native orderOut:nil];
                    else if (mode == 3) view.hidden = YES;
                    else if (mode == 4) {
                        Require(view.superview != nil, "pointer view has no ancestor");
                        view.superview.hidden = YES;
                    } else if (mode == 5) f.native.contentView = [NSView new];
                    else if (mode == 6) jalium_window_set_enabled(f.window, 0);
                    else if (mode == 7) f.Destroy();
                    f.events.clear();
                    if (mode == 8) {
                        bool started = false, leaked = false;
                        WindowNotification began(f, NSWindowWillMiniaturizeNotification, [&] {
                            started = true;
                            deliverAll();
                            leaked = std::any_of(f.events.begin(), f.events.end(), IsPointerPacket);
                        });
                        jalium_window_set_state(f.window, JALIUM_WINDOW_STATE_MINIMIZED);
                        Require(Pump([&] { return f.native.miniaturized; }) && started,
                            "pointer fixture did not minimize");
                        Require(!leaked, "pointer packet reached a minimizing surface");
                        f.events.clear();
                    }
                    deliverAll();
                    auto count = std::count_if(f.events.begin(), f.events.end(), IsPointerPacket);
                    Require(count == (mode == 0 ? 11 : 0), "pointer packet reached an excluded surface");
                });
            }
            run("pointer packets exclude an attached native sheet", [](Fixture& f) {
                NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                NSPanel* sheet = [[NSPanel alloc] initWithContentRect:NSMakeRect(0, 0, 200, 100)
                    styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];
                struct SheetCleanup {
                    NSWindow* parent;
                    NSPanel* sheet;
                    ~SheetCleanup() { if (parent.attachedSheet == sheet) [parent endSheet:sheet]; [sheet close]; }
                } cleanup{f.native, sheet};
                [f.native beginSheet:sheet completionHandler:nil];
                Require(Pump([&] { return f.native.attachedSheet == sheet; }), "native sheet did not attach");
                f.events.clear();
                for (int route = 0; route < 11; ++route) DeliverPointerPacket(view, PointerPacket(route), route);
                Require(std::none_of(f.events.begin(), f.events.end(), IsPointerPacket),
                    "parent pointer input bypassed the attached native sheet");
            });
            const char* pointerChanges[] = {"hide", "order out", "hide view", "detach", "disable", "close"};
            for (int route : {0, 10}) for (int change = 0; change < 6; ++change) {
                std::string name = std::string("pointer visibility callback ") + (route ? "wheel " : "mouse ") + pointerChanges[change];
                run(name.c_str(), [=](Fixture& f) {
                    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    int callbacks = 0;
                    TrackingVisibilityProbe query(f.native, [&] {
                        ++callbacks;
                        if (change == 0) jalium_window_hide(f.window);
                        else if (change == 1) [f.native orderOut:nil];
                        else if (change == 2) view.hidden = YES;
                        else if (change == 3) f.native.contentView = [NSView new];
                        else if (change == 4) jalium_window_set_enabled(f.window, 0);
                        else f.Destroy();
                    });
                    f.events.clear();
                    DeliverPointerPacket(view, PointerPacket(route), route);
                    Require(callbacks == 1, "pointer visibility callback was not consumed exactly once");
                    Require(std::none_of(f.events.begin(), f.events.end(), IsPointerPacket),
                        "visibility query revived unavailable pointer input");
                });
            }
            for (int route : {1, 7, 10}) for (SEL query : {@selector(buttonNumber), @selector(type),
                @selector(locationInWindow), @selector(modifierFlags)})
                for (int change = 0; change < 6; ++change) {
                if (route == 10 && (query == @selector(buttonNumber) || query == @selector(type))) continue;
                std::string name = std::string("pointer packet getter ") + (route == 10 ? "wheel " :
                    route == 7 ? "other mouse " : "mouse ") +
                    sel_getName(query) + " " + pointerChanges[change];
                run(name.c_str(), [=](Fixture& f) {
                    NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    auto* packet = PointerPacket(route);
                    packet.callbackSelector = query;
                    auto* fixture = &f;
                    packet.onQuery = ^{
                        if (change == 0) jalium_window_hide(fixture->window);
                        else if (change == 1) [fixture->native orderOut:nil];
                        else if (change == 2) view.hidden = YES;
                        else if (change == 3) fixture->native.contentView = [NSView new];
                        else if (change == 4) jalium_window_set_enabled(fixture->window, 0);
                        else fixture->Destroy();
                    };
                    f.events.clear();
                    DeliverPointerPacket(view, packet, route);
                    Require(packet.callbackCount == 1, "pointer event getter callback did not run exactly once");
                    Require(std::none_of(f.events.begin(), f.events.end(), IsPointerPacket),
                        "event getter revived unavailable pointer input");
                });
            }
            for (int route : {0, 3, 6, 9, 10}) {
                std::string name = "pointer delivery retains its client route " + std::to_string(route);
                run(name.c_str(), [=](Fixture& f) {
                    __unsafe_unretained NSView* target = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                    struct Context {
                        Fixture* fixture;
                        __weak NSView* view;
                        bool retained = false;
                        int calls = 0;
                    } context{&f, target};
                    jalium_window_set_event_callback(f.window, [](const JaliumPlatformEvent* event, void* data) {
                        auto& context = *static_cast<Context*>(data);
                        if (!IsPointerPacket(*event)) return;
                        ++context.calls;
                        jalium_window_set_event_callback(context.fixture->window, nullptr, nullptr);
                        context.fixture->native.contentView = [NSView new];
                        context.fixture->Destroy();
                        context.retained = context.view != nil;
                    }, &context);
                    DeliverPointerPacket(target, PointerPacket(route), route);
                    Require(context.calls == 1 && context.retained && !f.window,
                        "pointer delivery released its client during owner destruction");
                });
            }
            run("pointer packets retain outside coordinates and wheel metadata", [](Fixture& f) {
                NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                for (int route = 0; route < 11; ++route) {
                    auto* packet = PointerPacket(route);
                    packet.packetLocation = NSMakePoint(-30, NSHeight(view.bounds) + 40);
                    NSPoint expected = [view convertPoint:packet.packetLocation fromView:nil];
                    double scale = jalium_window_get_dpi_scale(f.window);
                    f.events.clear();
                    DeliverPointerPacket(view, packet, route);
                    Require(std::count_if(f.events.begin(), f.events.end(), IsPointerPacket) == 1,
                        "outside pointer packet was dropped or duplicated");
                    const auto& event = *std::find_if(f.events.begin(), f.events.end(), IsPointerPacket);
                    if (route == 10) {
                        Require(event.wheel.x == expected.x * scale && event.wheel.y == expected.y * scale &&
                            event.wheel.deltaX == .25f && event.wheel.deltaY == .5f &&
                            event.wheel.isPrecise && event.wheel.phase == NSEventPhaseChanged &&
                            event.wheel.momentumPhase == NSEventPhaseBegan && event.wheel.modifiers == 1 &&
                            (event.wheel.buttonStates & 0xffffffe0) == 0x80000000,
                            "pointer validation changed precise wheel payload");
                    } else Require(event.mouse.x == expected.x * scale && event.mouse.y == expected.y * scale &&
                        event.mouse.modifiers == 1 && event.mouse.clickCount == 2 &&
                        (event.mouse.buttonStates & 0xffffffe0) == 0x80000000,
                        "pointer validation changed mouse payload");
                }
            });
            run("hidden pointer exit retains leave cleanup", [](Fixture& f) {
                NSView* view = (__bridge NSView*)(void*)jalium_window_get_native_handle(f.window);
                jalium_window_hide(f.window);
                f.events.clear();
                [view mouseExited:PointerPacket(0)];
                Require(std::count_if(f.events.begin(), f.events.end(), [](const auto& e) {
                    return e.type == JALIUM_EVENT_MOUSE_LEAVE;
                }) == 1, "hidden pointer exit lost its leave cleanup");
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
                NSSize maximum = ScreenFittingClientMaximum(f);
                Require(jalium_window_set_min_max_size(f.window, 480, 360, maximum.width, maximum.height) == JALIUM_OK,
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
                    Require(width == (minimum ? 480 : maximum.width) && height == (minimum ? 360 : maximum.height) &&
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
