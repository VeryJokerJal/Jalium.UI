#include "jalium_platform.h"
#include "jalium_apple_font.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <limits>
#include <mutex>
#include <pthread.h>
#include <string>
#include <vector>

#import <TargetConditionals.h>
#import <CoreFoundation/CoreFoundation.h>
#import <QuartzCore/QuartzCore.h>
#if TARGET_OS_OSX
#import <AppKit/AppKit.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#include "platform_apple_window_startup_geometry.h"
#include "platform_apple_screen_coordinates.h"
#include <IOKit/hidsystem/IOLLEvent.h>
#else
#import <UIKit/UIKit.h>
#endif

struct JaliumDispatcher {
    std::atomic<uint32_t> refs{1};
    std::atomic<bool> alive{true};
    std::atomic<bool> queued{false};
    std::mutex mutex;
    JaliumDispatcherCallback callback = nullptr;
    void* userData = nullptr;
};

struct JaliumTimer {
    dispatch_source_t source = nullptr;
    dispatch_semaphore_t fired = nullptr;
    std::mutex mutex;
    JaliumTimerCallback callback = nullptr;
    void* userData = nullptr;
    std::atomic<bool> alive{true};
    std::atomic<bool> signalPending{false};
    CADisplayLink* displayLink = nil;
    id displayLinkTarget = nil;
};

@class JaliumAppleView;
#if TARGET_OS_OSX
@class JaliumAppleWindowDelegate;
@class JaliumAppleAccessibilityBridge;
struct JaliumAppleDragSource {
    NSWindow* window = nil;
    NSDraggingSession* session = nil;
    bool running = true;
    bool cancelRequested = false;
    uint32_t allowedEffects = JALIUM_DRAG_EFFECT_NONE;
    uint32_t performedEffect = JALIUM_DRAG_EFFECT_NONE;
    uint32_t feedbackEffect = JALIUM_DRAG_EFFECT_NONE;
    JaliumDragFeedbackCallback feedback = nullptr;
    JaliumDragQueryContinueCallback query = nullptr;
    void* userData = nullptr;
};
#endif

struct JaliumPlatformWindow {
#if TARGET_OS_OSX
    NSWindow* window = nil;
    JaliumAppleWindowDelegate* delegate = nil;
    NSVisualEffectView* backdropView = nil;
    NSMenu* systemMenu = nil;
    JaliumEditingCommandQuery editingCommandQuery = nullptr;
    void* editingCommandUserData = nullptr;
    NSRect restoreFrame = NSZeroRect;
    NSSize restoreContentSize = NSZeroSize;
    NSRect maximizedFrame = NSZeroRect;
    bool hasRestoreFrame = false;
    bool applyingState = false;
    bool applyingWindowState = false;
    bool appKitZooming = false;
    bool requestedNativeZoom = false;
    bool interactiveResize = false;
    bool nativeClosing = false;
    bool fullScreenTransition = false;
    bool fullScreenHadContentFocus = false;
    uint64_t stateRequestSequence = 0;
    uint64_t fullScreenRequestSequence = 0;
    bool miniaturizing = false;
    uint64_t miniaturizationSequence = 0;
    bool pendingActivation = false;
    bool pendingMainSelection = false;
    uint64_t selectionGeneration = 0;
    bool showInTaskbar = true;
    bool extendedTitleBar = false;
    double titleBarContentHeight = 31.0;
    bool aligningTitleBarButtons = false;
    bool dragTargetActive = false;
    uint64_t dragTargetGeneration = 0;
    uint64_t dragTargetNativeSequence = 0;
    NSPasteboard* dragTargetPasteboard = nil;
    id dragTargetSource = nil;
    std::shared_ptr<JaliumAppleDragSource> dragSource;
    NSHashTable<NSDraggingSession*>* retiredDragSources = nil;
    JaliumWindowState requestedState = JALIUM_WINDOW_STATE_NORMAL;
    JaliumWindowState beforeMinimize = JALIUM_WINDOW_STATE_NORMAL;
    JaliumWindowState beforeFullScreen = JALIUM_WINDOW_STATE_NORMAL;
#else
    UIWindow* window = nil;
    __weak UIView* sceneRoot = nil;
#endif
    JaliumAppleView* view = nil;
    JaliumEventCallback callback = nullptr;
    void* userData = nullptr;
    int32_t width = 0;
    int32_t height = 0;
    int32_t x = 0;
    int32_t y = 0;
    uint32_t style = 0;
    JaliumWindowState state = JALIUM_WINDOW_STATE_NORMAL;
    bool visible = false;
    bool destroying = false;
    bool deminiaturizing = false;
    bool enabled = true;
    float scale = 1.0f;
    uint64_t dragSession = 0;
    uint32_t dragEffect = JALIUM_DRAG_EFFECT_NONE;
};

namespace {

std::mutex g_windowsMutex;
std::vector<JaliumPlatformWindow*> g_windows;
std::atomic<int32_t> g_exitCode{0};
std::atomic<bool> g_quit{false};
__weak id g_rootView = nil;
#if !TARGET_OS_OSX
struct SceneRoot { std::string id; __weak UIView* view=nil; uint32_t claims=0; };
std::vector<SceneRoot> g_sceneRoots;
#endif

void RetainDispatcher(JaliumDispatcher* dispatcher)
{ dispatcher->refs.fetch_add(1, std::memory_order_relaxed); }
void ReleaseDispatcher(JaliumDispatcher* dispatcher)
{ if (dispatcher->refs.fetch_sub(1, std::memory_order_acq_rel) == 1) delete dispatcher; }

int64_t MonotonicMillis()
{
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}

void FireTimer(JaliumTimer* timer)
{
    if(!timer||!timer->alive.load(std::memory_order_acquire))return;
    if(!timer->signalPending.exchange(true,std::memory_order_acq_rel))
        dispatch_semaphore_signal(timer->fired);
    JaliumTimerCallback callback=nullptr;void* data=nullptr;
    {std::scoped_lock lock(timer->mutex);callback=timer->callback;data=timer->userData;}
    if(callback)callback(data);
}

NSString* StringFromUtf16(const JaliumUtf16Char* value)
{
    if (!value) return @"";
    size_t length = 0;
    while (value[length] != 0 && length < (1u << 20)) ++length;
    return [[NSString alloc] initWithCharacters:
        reinterpret_cast<const unichar*>(value) length:length];
}

#if TARGET_OS_OSX
std::atomic<uint64_t> g_nextAppleDragTargetId{1};
NSMutableArray<NSMenu*>* g_trackingMenus;
id g_menuTrackingBeginObserver;
id g_menuTrackingEndObserver;

void ObserveNativeMenuTracking()
{
    g_trackingMenus = [NSMutableArray new];
    auto* notifications = NSNotificationCenter.defaultCenter;
    g_menuTrackingBeginObserver = [notifications addObserverForName:NSMenuDidBeginTrackingNotification
        object:nil queue:nil usingBlock:^(NSNotification* notification) {
            if (![notification.object isKindOfClass:NSMenu.class]) return;
            [g_trackingMenus removeObjectIdenticalTo:notification.object];
            [g_trackingMenus addObject:notification.object];
        }];
    g_menuTrackingEndObserver = [notifications addObserverForName:NSMenuDidEndTrackingNotification
        object:nil queue:nil usingBlock:^(NSNotification* notification) {
            [g_trackingMenus removeObjectIdenticalTo:notification.object];
        }];
}

NSUInteger Utf16IndexFromUtf8Offset(NSString* text, int32_t offset)
{
    NSData* bytes = [text dataUsingEncoding:NSUTF8StringEncoding];
    NSUInteger count = std::min<NSUInteger>(std::max(0, offset), bytes.length);
    const auto* data = static_cast<const uint8_t*>(bytes.bytes);
    while (count > 0 && count < bytes.length && (data[count] & 0xc0) == 0x80) --count;
    NSString* prefix = [[NSString alloc] initWithBytes:data length:count encoding:NSUTF8StringEncoding];
    return prefix.length;
}

bool ValidTextRange(NSRange range, NSUInteger length)
{
    return range.location != NSNotFound && range.location <= length &&
        range.length <= length - range.location && range.location <= INT32_MAX && range.length <= INT32_MAX;
}

NSRange NormalizedTextRange(NSString* text, NSRange range)
{
    if (range.length) return [text rangeOfComposedCharacterSequencesForRange:range];
    if (range.location < text.length)
        range.location = [text rangeOfComposedCharacterSequenceAtIndex:range.location].location;
    return range;
}
#endif

void DispatchWindowEvent(JaliumPlatformWindow* window, JaliumPlatformEvent& event)
{
    if (!window || !window->callback) return;
    event.window = window;
    window->callback(&event, window->userData);
}

void DispatchSimple(JaliumPlatformWindow* window, JaliumEventType type)
{
    JaliumPlatformEvent event{};
    event.type = type;
    DispatchWindowEvent(window, event);
}

#if TARGET_OS_OSX
bool IsLiveWindow(JaliumPlatformWindow* window)
{
    std::scoped_lock lock(g_windowsMutex);
    return std::find(g_windows.begin(), g_windows.end(), window) != g_windows.end();
}

bool IsLiveWindow(JaliumPlatformWindow* window, NSWindow* nativeWindow)
{
    // A callback can destroy its window and immediately allocate another at
    // the same address. Retain the original AppKit object across the callback
    // and require both registration and native identity before continuing.
    return IsLiveWindow(window) && window->window == nativeWindow;
}

CGFloat DesktopTop()
{
    return NSMaxY(NSScreen.screens.firstObject.frame);
}

NSPoint FrameworkScreenPoint(NSPoint point, NSScreen* screen)
{
    screen = screen ?: NSScreen.screens.firstObject;
    NSRect frame = screen.frame;
    double primaryScale = NSScreen.screens.firstObject.backingScaleFactor ?: 1.0;
    double scale = screen.backingScaleFactor ?: 1.0;
    using jalium::platform::apple::ScreenCoordinate;
    return NSMakePoint(ScreenCoordinate(point.x, NSMinX(frame), primaryScale, scale),
        ScreenCoordinate(DesktopTop() - point.y, DesktopTop() - NSMaxY(frame), primaryScale, scale));
}

void PublishWindowState(JaliumPlatformWindow* window, JaliumWindowState state)
{
    if (!window || window->state == state) return;
    window->state = state;
    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_STATE_CHANGED;
    event.stateChanged.newState = state;
    DispatchWindowEvent(window, event);
}

void UpdateWindowGeometry(JaliumPlatformWindow* window)
{
    NSRect frame = window->window.frame;
    NSSize size = ((NSView*)window->view).bounds.size;
    window->width = lround(size.width * window->scale);
    window->height = lround(size.height * window->scale);
    NSPoint origin = FrameworkScreenPoint(NSMakePoint(NSMinX(frame), NSMaxY(frame)), window->window.screen);
    window->x = lround(origin.x);
    window->y = lround(origin.y);
}

NSUInteger AppleStyleMask(uint32_t style)
{
    NSUInteger mask = (style & JALIUM_WINDOW_STYLE_BORDERLESS)
        ? NSWindowStyleMaskBorderless : NSWindowStyleMaskTitled;
    if (style & JALIUM_WINDOW_STYLE_CLOSABLE) mask |= NSWindowStyleMaskClosable;
    if (style & JALIUM_WINDOW_STYLE_MINIMIZABLE) mask |= NSWindowStyleMaskMiniaturizable;
    if (style & JALIUM_WINDOW_STYLE_RESIZABLE) mask |= NSWindowStyleMaskResizable;
    return mask;
}

void ApplyAppleSurfaceAppearance(JaliumPlatformWindow* window)
{
    bool explicitTransparency = (window->style & JALIUM_WINDOW_STYLE_TRANSPARENT) != 0;
    bool hasBackdrop = window->backdropView != nil;
    bool reduceTransparency = hasBackdrop &&
        NSWorkspace.sharedWorkspace.accessibilityDisplayShouldReduceTransparency;
    bool transparent = (explicitTransparency || hasBackdrop) && !reduceTransparency;
    window->window.opaque = !transparent;
    window->window.backgroundColor = transparent ? NSColor.clearColor : NSColor.windowBackgroundColor;
    window->window.hasShadow = hasBackdrop || !explicitTransparency;
    window->backdropView.hidden = reduceTransparency;
    // A renderer may already have installed a layer before this runtime update.
    // Keep its alpha contract aligned with the NSWindow and future surfaces.
    // With reduced transparency the window paints an opaque system background
    // beneath the alpha-preserving renderer, instead of losing its clear pixels.
    ((NSView*)window->view).layer.opaque = !(explicitTransparency || hasBackdrop);
}

void RefreshWindowMenuItem(JaliumPlatformWindow* window)
{
    if (!window || !window->window) return;
    window->window.excludedFromWindowsMenu = !window->showInTaskbar;
    if (!window->destroying && window->visible && window->showInTaskbar) {
        // AppKit does not automatically list borderless windows used for
        // managed chrome. Register them through the public Window-menu API.
        [NSApp addWindowsItem:window->window title:window->window.title filename:NO];
        [NSApp changeWindowsItem:window->window title:window->window.title filename:NO];
        [NSApp updateWindowsItem:window->window];
    } else [NSApp removeWindowsItem:window->window];
}

void ApplyAppleWindowParticipation(JaliumPlatformWindow* window)
{
    // Menu/cycling participation is independent of fullscreen eligibility.
    // A regular window stays primary even when omitted from the Window list;
    // only popup surfaces accompany another window's fullscreen Space.
    NSUInteger behavior = (window->style & JALIUM_WINDOW_STYLE_POPUP)
        ? NSWindowCollectionBehaviorFullScreenAuxiliary : NSWindowCollectionBehaviorFullScreenPrimary;
    if (!window->showInTaskbar)
        behavior |= NSWindowCollectionBehaviorTransient | NSWindowCollectionBehaviorIgnoresCycle;
    window->window.collectionBehavior = behavior;
    RefreshWindowMenuItem(window);
}

void AlignAppleTitleBarButtons(JaliumPlatformWindow* window)
{
    if (!window || !window->extendedTitleBar || window->aligningTitleBarButtons ||
        window->fullScreenTransition || !window->window ||
        !(window->window.styleMask & NSWindowStyleMaskTitled) ||
        (window->window.styleMask & NSWindowStyleMaskFullScreen)) return;
    window->aligningTitleBarButtons = true;
    NSView* content = (NSView*)window->view;
    CGFloat height = window->titleBarContentHeight;
    for (NSNumber* number in @[@(NSWindowCloseButton), @(NSWindowMiniaturizeButton), @(NSWindowZoomButton)]) {
        NSButton* button = [window->window standardWindowButton:static_cast<NSWindowButton>(number.unsignedIntegerValue)];
        if (!button || !button.superview) continue;
        // Keep the native hit-test containers tall enough for the moved buttons.
        // Anchor their top edge; the application's client view remains unchanged.
        NSRect targetBounds = [button convertRect:button.bounds toView:content];
        targetBounds.origin.y = (content.isFlipped ? height / 2 : NSHeight(content.bounds) - height / 2)
            - NSHeight(targetBounds) / 2;
        for (NSView* container = button.superview;
             container && container.superview && container != window->window.contentView.superview;
             container = container.superview) {
            NSRect local = [content convertRect:targetBounds toView:container];
            CGFloat overflow = container.isFlipped ? NSMaxY(local) - NSMaxY(container.bounds)
                : NSMinY(container.bounds) - NSMinY(local);
            if (overflow <= 0.01) continue;
            NSRect frame = container.frame;
            if (!container.superview.isFlipped) frame.origin.y -= overflow;
            frame.size.height += overflow;
            container.frame = frame;
        }
        NSPoint center = [button convertPoint:NSMakePoint(NSMidX(button.bounds), NSMidY(button.bounds)) toView:content];
        center.y = content.isFlipped ? height / 2 : NSHeight(content.bounds) - height / 2;
        NSPoint target = [content convertPoint:center toView:button.superview];
        NSPoint origin = button.frame.origin;
        CGFloat y = target.y - NSHeight(button.frame) / 2;
        if (std::abs(origin.y - y) > 0.01) [button setFrameOrigin:NSMakePoint(origin.x, y)];
    }
    window->aligningTitleBarButtons = false;
}

void ApplyAppleStyle(JaliumPlatformWindow* window)
{
    bool registered = IsLiveWindow(window);
    NSWindow* nativeWindow = window->window;
    NSView* contentView = (NSView*)window->view;
    bool hadContentFocus = window->window.firstResponder == contentView;
    ApplyAppleSurfaceAppearance(window);
    window->window.level = (window->style & JALIUM_WINDOW_STYLE_TOPMOST)
        ? NSFloatingWindowLevel : NSNormalWindowLevel;
    if (window->fullScreenTransition ||
        (window->window.styleMask & NSWindowStyleMaskFullScreen)) return;
    NSSize contentSize = ((NSView*)window->view).bounds.size;
    NSPoint topLeft = NSMakePoint(NSMinX(window->window.frame), NSMaxY(window->window.frame));
    window->applyingState = true;
    NSUInteger mask = AppleStyleMask(window->style);
    bool extended = window->extendedTitleBar && (mask & NSWindowStyleMaskTitled);
    if (extended) mask |= NSWindowStyleMaskFullSizeContentView;
    window->window.styleMask = mask;
    if (registered && !IsLiveWindow(window, nativeWindow)) return;
    [window->window setContentSize:contentSize];
    if (registered && !IsLiveWindow(window, nativeWindow)) return;
    [window->window setFrameTopLeftPoint:topLeft];
    if (registered && !IsLiveWindow(window, nativeWindow)) return;
    window->applyingState = false;
    window->window.titleVisibility = extended ? NSWindowTitleHidden : NSWindowTitleVisible;
    window->window.titlebarAppearsTransparent = extended;
    ApplyAppleWindowParticipation(window);
    [window->window standardWindowButton:NSWindowCloseButton].enabled =
        window->enabled && (window->style & JALIUM_WINDOW_STYLE_CLOSABLE);
    [window->window standardWindowButton:NSWindowMiniaturizeButton].enabled =
        window->enabled && (window->style & JALIUM_WINDOW_STYLE_MINIMIZABLE);
    [window->window standardWindowButton:NSWindowZoomButton].enabled =
        window->enabled && (window->style & JALIUM_WINDOW_STYLE_MAXIMIZABLE);
    [window->window layoutIfNeeded];
    // AppKit resets firstResponder when replacing the native frame. The
    // managed focused control survives this change, so retain its input view
    // without activating an unfocused or disabled window.
    if (hadContentFocus && window->enabled && window->window.firstResponder != contentView)
        [window->window makeFirstResponder:contentView];
}

void CaptureNormalFrame(JaliumPlatformWindow* window)
{
    window->restoreFrame = window->window.frame;
    window->restoreContentSize = ((NSView*)window->view).bounds.size;
    window->hasRestoreFrame = true;
}

void ApplyWindowState(JaliumPlatformWindow* window, JaliumWindowState state);
void ApplyQueuedWindowState(JaliumPlatformWindow* window);

void BeginFullScreenTransition(JaliumPlatformWindow* window)
{
    window->fullScreenHadContentFocus = window->window.firstResponder == (NSView*)window->view;
    window->fullScreenRequestSequence = window->stateRequestSequence;
    window->fullScreenTransition = true;
}

void RestoreFullScreenContentFocus(JaliumPlatformWindow* window, bool hadContentFocus)
{
    if (!hadContentFocus || !IsLiveWindow(window) || window->destroying || !window->enabled ||
        !window->visible || !window->window.visible || window->fullScreenTransition) return;
    NSResponder* responder = window->window.firstResponder;
    // AppKit can replace our input view with its default window responder.
    // Preserve a newer native input target and never activate the window here.
    if (!responder || responder == window->window)
        [window->window makeFirstResponder:(NSView*)window->view];
}

void CancelPendingWindowSelection(JaliumPlatformWindow* window)
{
    ++window->selectionGeneration;
    window->pendingActivation = false;
    window->pendingMainSelection = false;
}

void ApplyPendingWindowSelection(JaliumPlatformWindow* window)
{
    if (!IsLiveWindow(window) || window->destroying || !window->enabled || !window->visible ||
        !window->window.visible || window->window.miniaturized || window->miniaturizing ||
        window->deminiaturizing || window->fullScreenTransition || window->appKitZooming ||
        window->applyingWindowState || window->requestedState != window->state) return;
    bool activate = window->pendingActivation, main = window->pendingMainSelection;
    if (!activate && !main) return;
    NSWindow* nativeWindow = window->window;
    window->pendingActivation = false;
    window->pendingMainSelection = false;
    // App activation is a cooperative request. Key/main selection and the
    // eventual activation events still come from AppKit's actual window.
    [NSApp activate];
    if (!IsLiveWindow(window, nativeWindow) || !window->enabled || !window->visible) return;
    if (activate) {
        [nativeWindow makeKeyAndOrderFront:nil];
        if (IsLiveWindow(window, nativeWindow) && window->enabled && window->visible)
            [nativeWindow makeFirstResponder:(NSView*)window->view];
    } else if (nativeWindow.canBecomeMainWindow) {
        [nativeWindow makeMainWindow];
    }
}

void RequestWindowSelection(JaliumPlatformWindow* window, bool activate)
{
    NSWindow* nativeWindow = window->window;
    if (activate) window->pendingActivation = true;
    else window->pendingMainSelection = true;
    if (window->miniaturizing || nativeWindow.miniaturized)
        ApplyWindowState(window, window->beforeMinimize);
    if (IsLiveWindow(window, nativeWindow)) ApplyPendingWindowSelection(window);
}

void BeginWindowMinimize(JaliumPlatformWindow* window)
{
    ++window->stateRequestSequence;
    CancelPendingWindowSelection(window);
    if (window->state == JALIUM_WINDOW_STATE_NORMAL) CaptureNormalFrame(window);
    window->beforeMinimize = window->state;
    window->requestedState = JALIUM_WINDOW_STATE_MINIMIZED;
    window->requestedNativeZoom = false;
    window->miniaturizing = true;
}

NSRect MaximizedFrameForScreen(NSWindow* nativeWindow, NSScreen* screen)
{
    NSRect frame = screen.visibleFrame;
    NSRect content = [nativeWindow contentRectForFrameRect:frame];
    content.size.width = std::clamp(content.size.width,
        nativeWindow.contentMinSize.width, nativeWindow.contentMaxSize.width);
    content.size.height = std::clamp(content.size.height,
        nativeWindow.contentMinSize.height, nativeWindow.contentMaxSize.height);
    NSRect constrained = [nativeWindow frameRectForContentRect:content];
    constrained.origin.y = NSMaxY(frame) - constrained.size.height;
    return constrained;
}

void SetWindowZoomed(JaliumPlatformWindow* window, bool zoomed)
{
    if (zoomed == (window->state == JALIUM_WINDOW_STATE_MAXIMIZED)) return;
    NSWindow* nativeWindow = window->window;
    NSRect frame = window->window.frame;
    if (zoomed) {
        CaptureNormalFrame(window);
        NSScreen* screen = window->window.screen ?: NSScreen.mainScreen;
        frame = MaximizedFrameForScreen(window->window, screen);
        window->maximizedFrame = frame;
    } else if (window->hasRestoreFrame) {
        frame = window->restoreFrame;
    }
    // Queue state requests made by callbacks until this frame change completes.
    // The restore query also uses the saved normal frame during a restore's
    // state notification, before AppKit has applied that frame.
    window->applyingWindowState = true;
    // State precedes resize, so managed RestoreBounds captures the normal size.
    PublishWindowState(window, zoomed ? JALIUM_WINDOW_STATE_MAXIMIZED : JALIUM_WINDOW_STATE_NORMAL);
    if (!IsLiveWindow(window, nativeWindow)) return;
    bool wasApplyingState = window->applyingState;
    window->applyingState = true;
    [nativeWindow setFrame:frame display:YES];
    if (!IsLiveWindow(window, nativeWindow)) return;
    window->applyingState = wasApplyingState;
    window->applyingWindowState = false;
    ApplyQueuedWindowState(window);
}

void ApplyWindowState(JaliumPlatformWindow* window, JaliumWindowState state)
{
    ++window->stateRequestSequence;
    if (state == JALIUM_WINDOW_STATE_MINIMIZED) CancelPendingWindowSelection(window);
    // An explicit framework request replaces any earlier native Zoom action.
    window->requestedNativeZoom = false;
    window->requestedState = state;
    if (window->fullScreenTransition || window->appKitZooming || window->applyingWindowState ||
        window->miniaturizing || window->deminiaturizing) return;
    bool fullScreen = (window->window.styleMask & NSWindowStyleMaskFullScreen) != 0;
    if (fullScreen) {
        if (state != JALIUM_WINDOW_STATE_FULLSCREEN) {
            BeginFullScreenTransition(window);
            [window->window toggleFullScreen:nil];
        }
        return;
    }
    if (state != JALIUM_WINDOW_STATE_MINIMIZED && window->window.miniaturized) {
        // AppKit may reject deminiaturizing an ordered-out window without a
        // completion notification. Retain the state request until Show makes
        // the surface available again instead of entering a permanent wait.
        if (!window->visible) return;
        // AppKit may finish deminiaturizing in a later event. Reapplying Zoom
        // before that acknowledgement mistakes the maximized frame for a new
        // normal frame and destroys RestoreBounds.
        window->deminiaturizing = true;
        [window->window deminiaturize:nil];
        return;
    }
    if (state == JALIUM_WINDOW_STATE_FULLSCREEN) {
        window->beforeFullScreen = window->state;
        BeginFullScreenTransition(window);
        [window->window toggleFullScreen:nil];
    } else if (state == JALIUM_WINDOW_STATE_MINIMIZED) {
        if (!window->window.miniaturized) {
            window->beforeMinimize = window->state;
            [window->window miniaturize:nil];
        }
    } else {
        SetWindowZoomed(window, state == JALIUM_WINDOW_STATE_MAXIMIZED);
    }
}

void ApplyQueuedWindowState(JaliumPlatformWindow* window)
{
    if (!IsLiveWindow(window) || window->destroying || window->fullScreenTransition ||
        window->appKitZooming || window->applyingWindowState || window->miniaturizing || window->deminiaturizing) return;
    bool nativeZoom = window->requestedNativeZoom;
    window->requestedNativeZoom = false;
    if (nativeZoom && (!window->enabled || !window->visible || !window->window.visible ||
        !(window->style & JALIUM_WINDOW_STYLE_MAXIMIZABLE))) {
        window->requestedState = window->state;
        return;
    }
    if (window->requestedState == window->state) {
        ApplyPendingWindowSelection(window);
        return;
    }
    NSWindow* nativeWindow = window->window;
    if (nativeZoom && window->requestedState == JALIUM_WINDOW_STATE_MAXIMIZED)
        [window->window zoom:nil];
    else ApplyWindowState(window, window->requestedState);
    if (IsLiveWindow(window, nativeWindow)) ApplyPendingWindowSelection(window);
}
#endif

int32_t ModifiersFromFlags(NSUInteger flags)
{
    int32_t result = JALIUM_MOD_NONE;
#if TARGET_OS_OSX
    if (flags & NSEventModifierFlagShift) result |= JALIUM_MOD_SHIFT;
    if (flags & NSEventModifierFlagControl) result |= JALIUM_MOD_CTRL;
    if (flags & NSEventModifierFlagOption) result |= JALIUM_MOD_ALT;
    if (flags & NSEventModifierFlagCommand) result |= JALIUM_MOD_META;
    if (flags & NSEventModifierFlagCapsLock) result |= JALIUM_MOD_CAPS;
#else
    if (flags & UIKeyModifierShift) result |= JALIUM_MOD_SHIFT;
    if (flags & UIKeyModifierControl) result |= JALIUM_MOD_CTRL;
    if (flags & UIKeyModifierAlternate) result |= JALIUM_MOD_ALT;
    if (flags & UIKeyModifierCommand) result |= JALIUM_MOD_META;
    if (flags & UIKeyModifierAlphaShift) result |= JALIUM_MOD_CAPS;
#endif
    return result;
}

int32_t VirtualKeyFromCharacter(unichar c)
{
    if (c >= 'a' && c <= 'z') return c - 'a' + 'A';
    if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
#if TARGET_OS_OSX
    if (c >= NSF1FunctionKey && c <= NSF24FunctionKey) return 0x70 + c - NSF1FunctionKey;
#endif
    switch (c) {
        case 0x1b: return 0x1b;
        case '\r': case '\n': return 0x0d;
        case '\t': case 0x19: return 0x09; // AppKit emits back-tab for Shift+Tab.
        case 0x08: case 0x7f: return 0x08;
        case ';': case ':': return 0xba;
        case '=': case '+': return 0xbb;
        case ',': case '<': return 0xbc;
        case '-': case '_': return 0xbd;
        case '.': case '>': return 0xbe;
        case '/': case '?': return 0xbf;
        case '`': case '~': return 0xc0;
        case '[': case '{': return 0xdb;
        case '\\': case '|': return 0xdc;
        case ']': case '}': return 0xdd;
        case '\'': case '"': return 0xde;
#if TARGET_OS_OSX
        case NSLeftArrowFunctionKey: return 0x25;
        case NSUpArrowFunctionKey: return 0x26;
        case NSRightArrowFunctionKey: return 0x27;
        case NSDownArrowFunctionKey: return 0x28;
        case NSHomeFunctionKey: return 0x24;
        case NSEndFunctionKey: return 0x23;
        case NSPageUpFunctionKey: return 0x21;
        case NSPageDownFunctionKey: return 0x22;
        case NSDeleteFunctionKey: return 0x2e;
        case NSInsertFunctionKey: return 0x2d;
        case NSHelpFunctionKey: return 0x2f;
#endif
        default: return static_cast<int32_t>(c);
    }
}

#if TARGET_OS_OSX
int32_t VirtualKeyForAppleEvent(NSEvent* event)
{
    // Navigation, function and numeric-pad identities are independent of text layout.
    // Removing Function/NumericPad during character translation changes arrow,
    // Home and End characters into ASCII control codes on AppKit.
    switch (event.keyCode) {
        case 0x7a: return 0x70; // F1-F20 (HIToolbox physical key codes).
        case 0x78: return 0x71;
        case 0x63: return 0x72;
        case 0x76: return 0x73;
        case 0x60: return 0x74;
        case 0x61: return 0x75;
        case 0x62: return 0x76;
        case 0x64: return 0x77;
        case 0x65: return 0x78;
        case 0x6d: return 0x79;
        case 0x67: return 0x7a;
        case 0x6f: return 0x7b;
        case 0x69: return 0x7c;
        case 0x6b: return 0x7d;
        case 0x71: return 0x7e;
        case 0x6a: return 0x7f;
        case 0x40: return 0x80;
        case 0x4f: return 0x81;
        case 0x50: return 0x82;
        case 0x5a: return 0x83;
        case 0x73: return 0x24;
        case 0x74: return 0x21;
        case 0x75: return 0x2e;
        case 0x77: return 0x23;
        case 0x79: return 0x22;
        case 0x7b: return 0x25;
        case 0x7c: return 0x27;
        case 0x7d: return 0x28;
        case 0x7e: return 0x26;
        case 0x41: return 0x6e;
        case 0x43: return 0x6a;
        case 0x45: return 0x6b;
        case 0x47: return 0x0c;
        case 0x4b: return 0x6f;
        case 0x4c: return 0x0d;
        case 0x4e: return 0x6d;
        case 0x51: return 0xbb;
        case 0x52: return 0x60;
        case 0x53: return 0x61;
        case 0x54: return 0x62;
        case 0x55: return 0x63;
        case 0x56: return 0x64;
        case 0x57: return 0x65;
        case 0x58: return 0x66;
        case 0x59: return 0x67;
        case 0x5b: return 0x68;
        case 0x5c: return 0x69;
    }
    // charactersIgnoringModifiers still preserves Shift. Re-translate with
    // no modifiers so Shift+1 cannot masquerade as the PageUp virtual key.
    NSString* chars = [event charactersByApplyingModifiers:0] ?: event.charactersIgnoringModifiers ?: @"";
    return VirtualKeyFromCharacter(chars.length ? [chars characterAtIndex:0] : 0);
}

uint32_t EffectsFromOperation(NSDragOperation operation)
{
    uint32_t result=JALIUM_DRAG_EFFECT_NONE;
    if(operation&NSDragOperationCopy)result|=JALIUM_DRAG_EFFECT_COPY;
    if(operation&NSDragOperationMove)result|=JALIUM_DRAG_EFFECT_MOVE;
    if(operation&NSDragOperationLink)result|=JALIUM_DRAG_EFFECT_LINK;
    return result;
}

NSDragOperation OperationFromEffects(uint32_t effects)
{
    NSDragOperation result=NSDragOperationNone;
    if(effects&JALIUM_DRAG_EFFECT_COPY)result|=NSDragOperationCopy;
    if(effects&JALIUM_DRAG_EFFECT_MOVE)result|=NSDragOperationMove;
    if(effects&JALIUM_DRAG_EFFECT_LINK)result|=NSDragOperationLink;
    return result;
}

static NSString* const AppleMimeWrapper = @"application/x-jalium-pasteboard-mime-";
// Match ClipboardPlatform.MaxClipboardPayloadBytes before copying a promised
// representation across the native/managed boundary.
static constexpr NSUInteger MaxApplePasteboardPayloadBytes = 256 * 1024 * 1024;

NSPasteboardType PasteboardTypeFromMime(const char* mime)
{
    if (!mime) return nil;
    NSString* value = [NSString stringWithUTF8String:mime];
    NSString* base = [[value componentsSeparatedByString:@";"][0] lowercaseString];
    if ([base isEqualToString:@"text/plain"] || [value isEqualToString:@"UTF8_STRING"]) return NSPasteboardTypeString;
    if ([base isEqualToString:@"text/uri-list"]) return NSPasteboardTypeURL;
    if ([base isEqualToString:@"image/png"]) return NSPasteboardTypePNG;
    if ([base isEqualToString:@"image/tiff"]) return NSPasteboardTypeTIFF;
    if ([base isEqualToString:@"text/html"]) return NSPasteboardTypeHTML;
    if ([base isEqualToString:@"text/rtf"] || [base isEqualToString:@"application/rtf"]) return NSPasteboardTypeRTF;
    if ([base isEqualToString:@"audio/wav"] || [base isEqualToString:@"audio/x-wav"]) return UTTypeWAV.identifier;
    // Pasteboard types are UTIs, not MIME names. Dynamic UTIs retain unknown
    // MIME tags and conform to public.data so generic destinations can accept them.
    if ([value containsString:@"/"]) {
        UTType* type = [UTType typeWithMIMEType:value conformingToType:UTTypeData];
        if (type.dynamic && ![type.preferredMIMEType isEqualToString:value]) {
            // UTType lowercases MIME tags. Registered framework names carry
            // case-sensitive base64, so wrap such dynamic tags in lowercase hex.
            NSMutableString* encoded = [NSMutableString stringWithString:AppleMimeWrapper];
            for (const unsigned char* byte = reinterpret_cast<const unsigned char*>(mime); *byte; ++byte)
                [encoded appendFormat:@"%02x", *byte];
            type = [UTType typeWithMIMEType:encoded conformingToType:UTTypeData];
        }
        return type.identifier;
    }
    return value;
}

const char* MimeForPasteboardType(NSPasteboardType type, std::string& storage)
{
    NSString* value = nil;
    if ([type isEqualToString:NSPasteboardTypeString]) value = @"text/plain;charset=utf-8";
    else if ([type isEqualToString:NSPasteboardTypeURL] || [type isEqualToString:NSPasteboardTypeFileURL]) value = @"text/uri-list";
    else if ([type isEqualToString:NSPasteboardTypePNG]) value = @"image/png";
    else if ([type isEqualToString:NSPasteboardTypeTIFF]) value = @"image/tiff";
    else if ([type isEqualToString:NSPasteboardTypeHTML]) value = @"text/html";
    else if ([type isEqualToString:NSPasteboardTypeRTF]) value = @"text/rtf";
    else if ([type isEqualToString:UTTypeWAV.identifier]) value = @"audio/wav";
    else value = [UTType typeWithIdentifier:type].preferredMIMEType ?: type;
    if ([value hasPrefix:AppleMimeWrapper]) {
        NSString* hex = [value substringFromIndex:AppleMimeWrapper.length];
        NSMutableData* decoded = [NSMutableData data];
        bool valid = hex.length != 0 && hex.length % 2 == 0;
        auto digit = [](unichar ch) -> int { return ch >= '0' && ch <= '9' ? ch - '0' : ch >= 'a' && ch <= 'f' ? ch - 'a' + 10 : -1; };
        for (NSUInteger index = 0; valid && index < hex.length; index += 2) {
            int high = digit([hex characterAtIndex:index]), low = digit([hex characterAtIndex:index + 1]);
            if (high < 0 || low < 0) { valid = false; break; }
            uint8_t byte = static_cast<uint8_t>(high * 16 + low); [decoded appendBytes:&byte length:1];
        }
        NSString* original = valid ? [[NSString alloc] initWithData:decoded encoding:NSUTF8StringEncoding] : nil;
        if ([original containsString:@"/"] && [original rangeOfCharacterFromSet:NSCharacterSet.newlineCharacterSet].location == NSNotFound)
            value = original;
    }
    storage = value.UTF8String ?: "";
    return storage.c_str();
}

std::string PasteboardTypesUtf8(NSPasteboard* pasteboard)
{
    std::string result;
    NSMutableSet<NSString*>* seen = [NSMutableSet set];
    for (NSPasteboardType type in pasteboard.types) {
        std::string mime; MimeForPasteboardType(type, mime);
        NSString* value = [NSString stringWithUTF8String:mime.c_str()];
        if (!value || [seen containsObject:value]) continue;
        [seen addObject:value];
        if (!result.empty()) result.push_back('\n');
        result += mime;
    }
    if ([pasteboard.types containsObject:NSPasteboardTypeTIFF] && ![seen containsObject:@"image/png"])
        result += "\nimage/png";
    return result;
}

NSData* ReadApplePasteboardData(NSPasteboard* pasteboard, const char* mime)
{
    NSPasteboardType type = PasteboardTypeFromMime(mime);
    if (!type) return nil;
    if ([type isEqualToString:NSPasteboardTypeURL]) {
        // Finder supplies one file URL per item. Reading the board-level first
        // value would silently discard every remaining file.
        NSMutableArray<NSString*>* urls = [NSMutableArray array];
        if ([pasteboard respondsToSelector:@selector(pasteboardItems)]) {
            for (NSPasteboardItem* item in pasteboard.pasteboardItems) {
                NSString* value = [item stringForType:NSPasteboardTypeFileURL] ?: [item stringForType:NSPasteboardTypeURL];
                if (value) [urls addObject:value];
            }
        }
        if (!urls.count) {
            NSString* value = [pasteboard stringForType:NSPasteboardTypeFileURL] ?: [pasteboard stringForType:NSPasteboardTypeURL];
            if (value) [urls addObject:value];
        }
        if (!urls.count) return nil;
        return [[[urls componentsJoinedByString:@"\r\n"] stringByAppendingString:@"\r\n"] dataUsingEncoding:NSUTF8StringEncoding];
    }
    if ([type isEqualToString:NSPasteboardTypeString])
        return [[pasteboard stringForType:type] dataUsingEncoding:NSUTF8StringEncoding];
    if ([type isEqualToString:NSPasteboardTypePNG] && ![pasteboard.types containsObject:type]) {
        NSData* tiff = [pasteboard dataForType:NSPasteboardTypeTIFF];
        if (tiff.length > MaxApplePasteboardPayloadBytes) return nil;
        NSBitmapImageRep* image = tiff ? [NSBitmapImageRep imageRepWithData:tiff] : nil;
        return [image representationUsingType:NSBitmapImageFileTypePNG properties:@{}];
    }
    return [pasteboard dataForType:type];
}

JaliumResult CopyApplePasteboardData(NSData* data, uint8_t** out, uint32_t* size)
{
    if (!data) return JALIUM_OK;
    NSUInteger length = data.length;
    if (length > MaxApplePasteboardPayloadBytes) return JALIUM_ERROR_INVALID_ARGUMENT;
    auto* bytes = static_cast<uint8_t*>(malloc(std::max<NSUInteger>(length, 1)));
    if (!bytes) return JALIUM_ERROR_OUT_OF_MEMORY;
    if (length) memcpy(bytes, data.bytes, length);
    *out = bytes; *size = static_cast<uint32_t>(length);
    return JALIUM_OK;
}

uint32_t AppleDragKeyStates(NSEvent* event)
{
    NSEventModifierFlags flags = event ? event.modifierFlags : NSEvent.modifierFlags;
    NSUInteger buttons = NSEvent.pressedMouseButtons;
    // Owned mouse-down/dragged events also cover the initiation callback before
    // AppKit updates pressedMouseButtons (and deterministic delegate fixtures).
    if (event.type == NSEventTypeLeftMouseDown || event.type == NSEventTypeLeftMouseDragged) buttons |= 1;
    if (event.type == NSEventTypeLeftMouseUp) buttons &= ~NSUInteger(1);
    uint32_t keys = 0;
    if (buttons & 1) keys |= 0x01;
    if (buttons & 2) keys |= 0x02;
    if (buttons & 4) keys |= 0x10;
    if (flags & NSEventModifierFlagShift) keys |= 0x04;
    if (flags & NSEventModifierFlagControl) keys |= 0x08;
    if (flags & NSEventModifierFlagOption) keys |= 0x20;
    if (flags & NSEventModifierFlagCommand) keys |= 0x40;
    return keys;
}

bool CanReceiveAppleDrag(JaliumPlatformWindow* window)
{
    return NSThread.isMainThread && IsLiveWindow(window) && !window->destroying &&
        window->enabled && window->visible && window->window.visible;
}

void CancelAppleDragSource(const std::shared_ptr<JaliumAppleDragSource>& source)
{
    if (!source) return;
    // A completed operation keeps its result when its completion callback
    // closes the window. An interrupted operation returns cancellation.
    if (source->running) {
        source->performedEffect = JALIUM_DRAG_EFFECT_NONE;
        if (source->session) {
            NSEvent* escape = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint
                modifierFlags:0 timestamp:NSProcessInfo.processInfo.systemUptime
                windowNumber:source->window.windowNumber context:nil characters:@"\x1b"
                charactersIgnoringModifiers:@"\x1b" isARepeat:NO keyCode:53];
            [NSApp postEvent:escape atStart:YES];
        }
    }
    source->running = false;
    source->feedback = nullptr; source->query = nullptr; source->userData = nullptr;
}

void CancelAppleDrag(JaliumPlatformWindow* window)
{
    window->dragTargetActive = false;
    ++window->dragTargetGeneration;
    window->dragTargetNativeSequence = 0;
    window->dragTargetPasteboard = nil;
    window->dragTargetSource = nil;
    window->dragSession = 0;
    window->dragEffect = JALIUM_DRAG_EFFECT_NONE;
    CancelAppleDragSource(window->dragSource);
}

std::shared_ptr<JaliumAppleDragSource> ActiveAppleDragSource(
    JaliumPlatformWindow* window, NSDraggingSession* session)
{
    if (!CanReceiveAppleDrag(window)) return {};
    auto source = window->dragSource;
    if (!session || !source || !source->running || source->window != window->window ||
        [window->retiredDragSources containsObject:session] ||
        (source->session && source->session != session)) return {};
    return source;
}

std::shared_ptr<JaliumAppleDragSource> AppleDragSourceForView(id candidate)
{
    JaliumPlatformWindow* owner = nullptr;
    {
        std::scoped_lock lock(g_windowsMutex);
        for (auto* peer : g_windows)
            if (peer->view == candidate) { owner = peer; break; }
    }
    return CanReceiveAppleDrag(owner) && owner->dragSource && owner->dragSource->running
        ? owner->dragSource : nullptr;
}

bool AppleDragCancelled(id candidate)
{
    auto source = AppleDragSourceForView(candidate);
    return source && source->cancelRequested;
}

JaliumDragContinueAction QueryAppleDragSource(JaliumPlatformWindow* window,
    const std::shared_ptr<JaliumAppleDragSource>& source, NSEvent* event, NSPoint screenPoint)
{
    if (!source->query || !source->running) return JALIUM_DRAG_CONTINUE;
    bool escape = event.type == NSEventTypeKeyDown && event.keyCode == 53;
    JaliumDragContinueAction action = source->query(AppleDragKeyStates(event) & 0x3f,
        escape ? 1 : 0, source->userData);
    if (!IsLiveWindow(window, source->window) || window->dragSource != source || !source->running)
        return JALIUM_DRAG_CANCEL;
    // AppKit's private tracking loop can bypass a local event monitor and
    // continue preparing the destination before consuming the posted Escape.
    // Retain Cancel so operation masks, owned targets and completion agree.
    if (action == JALIUM_DRAG_CANCEL) {
        source->cancelRequested = true;
        source->feedbackEffect = JALIUM_DRAG_EFFECT_NONE;
    }
    if (action == JALIUM_DRAG_CANCEL && !escape) {
        NSEvent* cancel = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint
            modifierFlags:0 timestamp:NSProcessInfo.processInfo.systemUptime windowNumber:source->window.windowNumber
            context:nil characters:@"\x1b" charactersIgnoringModifiers:@"\x1b" isARepeat:NO keyCode:53];
        [NSApp postEvent:cancel atStart:YES];
    } else if (action == JALIUM_DRAG_DROP && event.type != NSEventTypeLeftMouseUp) {
        NSEvent* release = [NSEvent mouseEventWithType:NSEventTypeLeftMouseUp
            location:[source->window convertPointFromScreen:screenPoint] modifierFlags:event.modifierFlags
            timestamp:NSProcessInfo.processInfo.systemUptime windowNumber:source->window.windowNumber context:nil
            eventNumber:0 clickCount:1 pressure:0];
        [NSApp postEvent:release atStart:YES];
    }
    return action;
}

void DispatchDragEvent(JaliumPlatformWindow* window,id<NSDraggingInfo> info,
    JaliumEventType type,const char* dataMime=nullptr,const uint8_t* data=nullptr,
    uint32_t dataSize=0)
{
    if(!CanReceiveAppleDrag(window))return;
    NSPoint point=info ? [(NSView*)window->view convertPoint:info.draggingLocation fromView:nil] : NSZeroPoint;
    std::string types=info ? PasteboardTypesUtf8(info.draggingPasteboard) : std::string();
    if(type==JALIUM_EVENT_DRAG_ENTER) {
        ++window->dragTargetGeneration;
        window->dragTargetNativeSequence = static_cast<uint64_t>(info.draggingSequenceNumber);
        // AppKit reuses the native sequence when a drag reenters a view. The
        // public response token identifies this visit, including reentrant
        // callbacks, so a retired response cannot select its successor's effect.
        window->dragSession = g_nextAppleDragTargetId.fetch_add(1, std::memory_order_relaxed);
        if (!window->dragSession)
            window->dragSession = g_nextAppleDragTargetId.fetch_add(1, std::memory_order_relaxed);
        window->dragTargetPasteboard = info.draggingPasteboard;
        window->dragTargetSource = [info respondsToSelector:@selector(draggingSource)] ? info.draggingSource : nil;
        window->dragTargetActive = true;
        window->dragEffect = JALIUM_DRAG_EFFECT_NONE;
    }
    JaliumPlatformEvent event{};event.type=type;
    event.drag.x=point.x*window->scale;event.drag.y=point.y*window->scale;
    event.drag.keyStates = AppleDragKeyStates(NSApp.currentEvent);
    event.drag.allowedEffects=EffectsFromOperation(info.draggingSourceOperationMask);
    event.drag.sessionId=window->dragSession;event.drag.mimeTypes=types.c_str();
    event.drag.dataMimeType=dataMime;event.drag.data=data;event.drag.dataSize=dataSize;
    uint64_t generation = window->dragTargetGeneration;
    DispatchWindowEvent(window,event);
    if (!CanReceiveAppleDrag(window) || window->dragTargetGeneration != generation ||
        window->dragSession != event.drag.sessionId || !window->dragTargetActive) return;
    auto source = AppleDragSourceForView(window->dragTargetSource);
    if (!source) return;
    source->feedbackEffect = source->cancelRequested || type == JALIUM_EVENT_DRAG_LEAVE ? JALIUM_DRAG_EFFECT_NONE :
        window->dragEffect & event.drag.allowedEffects;
    if (source->feedback) source->feedback(source->feedbackEffect, source->userData);
}
#endif

#if !TARGET_OS_OSX
UIView* AcquireRootView()
{
    {std::scoped_lock lock(g_windowsMutex);
        for(auto& root:g_sceneRoots)if(root.view&&root.claims==0){++root.claims;return root.view;}}
    if (g_rootView && [g_rootView isKindOfClass:[UIView class]]) return g_rootView;
    for (UIScene* scene in UIApplication.sharedApplication.connectedScenes) {
        if (![scene isKindOfClass:[UIWindowScene class]]) continue;
        for (UIWindow* window in ((UIWindowScene*)scene).windows) {
            if (window.isKeyWindow && window.rootViewController.view)
                return window.rootViewController.view;
        }
    }
    return nil;
}

void ReleaseRootView(UIView* view)
{
    if(!view)return;std::scoped_lock lock(g_windowsMutex);
    for(auto& root:g_sceneRoots)if(root.view==view&&root.claims){--root.claims;break;}
}
#endif

} // namespace

@interface JaliumDisplayLinkTarget : NSObject
@property(nonatomic,assign) JaliumTimer* timer;
- (void)displayLinkTick:(CADisplayLink*)link;
@end
@implementation JaliumDisplayLinkTarget
- (void)displayLinkTick:(CADisplayLink*)link { FireTimer(_timer); (void)link; }
@end

#if TARGET_OS_OSX

static bool QueryWindowAccessibilityInfo(JaliumPlatformWindow* window, JaliumAccessibilityRequest* request);
static id QueryWindowAccessibilityButton(JaliumPlatformWindow* window, int32_t kind);

static bool CanWriteWindowAccessibility(JaliumPlatformWindow* window)
{
    return NSThread.isMainThread && window && IsLiveWindow(window) && !window->destroying &&
        window->enabled && window->visible && (window->window.visible || window->window.miniaturized);
}

static bool CanChangeAccessibilityWindowFrame(JaliumPlatformWindow* window)
{
    return CanWriteWindowAccessibility(window) && !window->window.miniaturized &&
        !window->fullScreenTransition && !window->appKitZooming && !window->applyingWindowState &&
        !window->miniaturizing && !window->deminiaturizing &&
        !(window->window.styleMask & NSWindowStyleMaskFullScreen);
}

static NSSize ConstrainAccessibilityFrameSize(NSWindow* window, NSSize requested)
{
    NSRect content = [window contentRectForFrameRect:NSMakeRect(0, 0, requested.width, requested.height)];
    NSSize minimum = window.contentMinSize, maximum = window.contentMaxSize;
    content.size.width = std::clamp(content.size.width, minimum.width, maximum.width);
    content.size.height = std::clamp(content.size.height, minimum.height, maximum.height);
    return [window frameRectForContentRect:content].size;
}

@interface JaliumAppleWindow : NSWindow
@property(nonatomic, assign) JaliumPlatformWindow* jaliumOwner;
@end
@implementation JaliumAppleWindow
- (void)layoutIfNeeded {
    [super layoutIfNeeded];
    AlignAppleTitleBarButtons(_jaliumOwner);
}
- (BOOL)isAccessibilitySelectorAllowed:(SEL)selector {
    if (selector == @selector(setAccessibilityFrame:))
        return CanChangeAccessibilityWindowFrame(_jaliumOwner);
    if (selector == @selector(setAccessibilityMinimized:))
        return CanChangeAccessibilityWindowFrame(_jaliumOwner)
            ? (_jaliumOwner->style & JALIUM_WINDOW_STYLE_MINIMIZABLE) != 0
            : CanWriteWindowAccessibility(_jaliumOwner) && self.miniaturized &&
                !_jaliumOwner->fullScreenTransition && !_jaliumOwner->appKitZooming && !_jaliumOwner->applyingWindowState &&
                !_jaliumOwner->miniaturizing && !_jaliumOwner->deminiaturizing;
    if (selector == @selector(setAccessibilityFocused:))
        return CanWriteWindowAccessibility(_jaliumOwner) && self.canBecomeKeyWindow;
    if (selector == @selector(setAccessibilityMain:))
        return CanWriteWindowAccessibility(_jaliumOwner) && self.canBecomeMainWindow;
    return [super isAccessibilitySelectorAllowed:selector];
}
- (BOOL)respondsToSelector:(SEL)selector {
    if (selector == @selector(setAccessibilityFrame:) || selector == @selector(setAccessibilityMinimized:) ||
        selector == @selector(setAccessibilityFocused:) || selector == @selector(setAccessibilityMain:))
        return [self isAccessibilitySelectorAllowed:selector];
    return [super respondsToSelector:selector];
}
- (BOOL)accessibilityIsAttributeSettable:(NSAccessibilityAttributeName)attribute {
    // NSWindow's legacy attribute metadata still advertises Minimized from
    // its style mask alone. Keep that client path consistent with the modern
    // selectors and our managed enabled/visible state.
    if ([attribute isEqualToString:NSAccessibilityPositionAttribute])
        return CanChangeAccessibilityWindowFrame(_jaliumOwner);
    if ([attribute isEqualToString:NSAccessibilitySizeAttribute])
        return CanChangeAccessibilityWindowFrame(_jaliumOwner) &&
            (_jaliumOwner->style & JALIUM_WINDOW_STYLE_RESIZABLE) != 0;
    if ([attribute isEqualToString:NSAccessibilityMinimizedAttribute])
        return [self isAccessibilitySelectorAllowed:@selector(setAccessibilityMinimized:)];
    if ([attribute isEqualToString:NSAccessibilityFocusedAttribute])
        return [self isAccessibilitySelectorAllowed:@selector(setAccessibilityFocused:)];
    if ([attribute isEqualToString:NSAccessibilityMainAttribute])
        return [self isAccessibilitySelectorAllowed:@selector(setAccessibilityMain:)];
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    return [super accessibilityIsAttributeSettable:attribute];
#pragma clang diagnostic pop
}
- (void)setAccessibilityFrame:(NSRect)frame {
    if (!CanChangeAccessibilityWindowFrame(_jaliumOwner) ||
        !std::isfinite(frame.origin.x) || !std::isfinite(frame.origin.y) ||
        !std::isfinite(frame.size.width) || !std::isfinite(frame.size.height) ||
        frame.size.width <= 0 || frame.size.height <= 0 ||
        !std::isfinite(NSMaxX(frame)) || !std::isfinite(NSMaxY(frame))) return;
    // Use NSWindow's real geometry setters for both frame styles. Inherited AX
    // setters can leave the actual window unchanged; all clients need the same
    // constraints and callbacks.
    if (!(_jaliumOwner->style & JALIUM_WINDOW_STYLE_RESIZABLE)) {
        frame.size = self.frame.size;
    } else {
        frame.size = ConstrainAccessibilityFrameSize(self, frame.size);
    }
    // Resize at the current top-left first. Borderless setFrame: omits the
    // move notification, so a combined edit must finish with AppKit's origin
    // setter after the size is final. Its event then has the final top-left.
    NSRect before = self.frame;
    if (!NSEqualSizes(frame.size, before.size)) {
        NSRect resized = before;
        resized.size = frame.size;
        resized.origin.y = NSMaxY(before) - frame.size.height;
        [self setFrame:resized display:YES];
        // A resize callback may close the window or issue newer geometry. Do
        // not apply the old combined edit's move over that callback's result.
        if (!_jaliumOwner || !CanChangeAccessibilityWindowFrame(_jaliumOwner) ||
            !NSEqualRects(self.frame, resized)) return;
    }
    if (!_jaliumOwner || !CanChangeAccessibilityWindowFrame(_jaliumOwner)) return;
    if (!NSEqualPoints(frame.origin, self.frame.origin)) [self setFrameOrigin:frame.origin];
}
- (void)accessibilitySetValue:(id)value forAttribute:(NSAccessibilityAttributeName)attribute {
    bool position = [attribute isEqualToString:NSAccessibilityPositionAttribute];
    bool size = [attribute isEqualToString:NSAccessibilitySizeAttribute];
    if (position || size) {
        if (!CanChangeAccessibilityWindowFrame(_jaliumOwner) || ![value isKindOfClass:NSValue.class] ||
            (size && !(_jaliumOwner->style & JALIUM_WINDOW_STYLE_RESIZABLE))) return;
        NSRect frame = self.frame;
        if (position) {
            if (std::strcmp([value objCType], @encode(NSPoint)) != 0) return;
            // AppKit's in-process NSValue position is the frame's bottom-left;
            // ApplicationServices performs the external top-left conversion.
            frame.origin = [value pointValue];
        } else {
            if (std::strcmp([value objCType], @encode(NSSize)) != 0) return;
            CGFloat top = NSMaxY(frame);
            frame.size = [value sizeValue];
            if (!std::isfinite(frame.size.width) || !std::isfinite(frame.size.height) ||
                frame.size.width <= 0 || frame.size.height <= 0) return;
            // AXSize does not request a position. Anchor the already-clamped
            // size, rather than moving the upper edge by the clamp delta.
            frame.size = ConstrainAccessibilityFrameSize(self, frame.size);
            frame.origin.y = top - frame.size.height;
        }
        [self setAccessibilityFrame:frame];
        return;
    }
    if ([attribute isEqualToString:NSAccessibilityMinimizedAttribute] ||
        [attribute isEqualToString:NSAccessibilityFocusedAttribute] ||
        [attribute isEqualToString:NSAccessibilityMainAttribute]) {
        if (!CanWriteWindowAccessibility(_jaliumOwner) || ![value isKindOfClass:NSNumber.class] ||
            !std::isfinite([value doubleValue])) return;
        BOOL requested = [value boolValue];
        if ([attribute isEqualToString:NSAccessibilityMinimizedAttribute]) [self setAccessibilityMinimized:requested];
        else if ([attribute isEqualToString:NSAccessibilityFocusedAttribute]) [self setAccessibilityFocused:requested];
        else [self setAccessibilityMain:requested];
        return;
    }
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    [super accessibilitySetValue:value forAttribute:attribute];
#pragma clang diagnostic pop
}
- (void)setAccessibilityMinimized:(BOOL)minimized {
    if (![self isAccessibilitySelectorAllowed:@selector(setAccessibilityMinimized:)] ||
        self.miniaturized == minimized) return;
    ApplyWindowState(_jaliumOwner, minimized ? JALIUM_WINDOW_STATE_MINIMIZED : _jaliumOwner->beforeMinimize);
}
- (void)setAccessibilityFocused:(BOOL)focused {
    // AXFocused can select a focusable element with YES; NO cannot unfocus it.
    // resignKeyWindow is an AppKit notification override, not a focus action.
    if (!focused || ![self isAccessibilitySelectorAllowed:@selector(setAccessibilityFocused:)]) return;
    uint64_t generation = _jaliumOwner->selectionGeneration;
    __weak JaliumAppleWindow* weakWindow = self;
    dispatch_async(dispatch_get_main_queue(), ^{
        JaliumAppleWindow* window = weakWindow;
        if (!window || !CanWriteWindowAccessibility(window.jaliumOwner) ||
            window.jaliumOwner->selectionGeneration != generation) return;
        // The generic AppKit setter stores an AX-only value. Activation must
        // reach the real key window, and managed activation handlers may open
        // a modal loop, so return to the AX client before running them.
        jalium_window_activate(window.jaliumOwner);
    });
}
- (void)setAccessibilityMain:(BOOL)main {
    // Match NSWindow's AX main selection: select another window with YES.
    // Calling resignMainWindow directly only clears its notification flag,
    // leaving NSApplication.mainWindow pointing at it and breaking reselection.
    if (!main || ![self isAccessibilitySelectorAllowed:@selector(setAccessibilityMain:)]) return;
    uint64_t generation = _jaliumOwner->selectionGeneration;
    __weak JaliumAppleWindow* weakWindow = self;
    dispatch_async(dispatch_get_main_queue(), ^{
        JaliumAppleWindow* window = weakWindow;
        if (!window || !CanWriteWindowAccessibility(window.jaliumOwner) || !window.canBecomeMainWindow ||
            window.jaliumOwner->selectionGeneration != generation) return;
        RequestWindowSelection(window.jaliumOwner, false);
    });
}
- (BOOL)isAccessibilityFocused {
    return NSThread.isMainThread && _jaliumOwner && IsLiveWindow(_jaliumOwner) &&
        !_jaliumOwner->destroying && self.keyWindow;
}
- (BOOL)isAccessibilityMain {
    return NSThread.isMainThread && _jaliumOwner && IsLiveWindow(_jaliumOwner) &&
        !_jaliumOwner->destroying && self.mainWindow;
}
- (id)accessibilityDefaultButton {
    return QueryWindowAccessibilityButton(_jaliumOwner, JALIUM_AX_DEFAULT_BUTTON) ?: [super accessibilityDefaultButton];
}
- (id)accessibilityCancelButton {
    return QueryWindowAccessibilityButton(_jaliumOwner, JALIUM_AX_CANCEL_BUTTON) ?: [super accessibilityCancelButton];
}
- (id)accessibilityCloseButton {
    if (!NSThread.isMainThread || !_jaliumOwner || !IsLiveWindow(_jaliumOwner) || _jaliumOwner->destroying) return nil;
    return [super accessibilityCloseButton] ?: QueryWindowAccessibilityButton(_jaliumOwner, JALIUM_AX_CLOSE_BUTTON);
}
- (id)accessibilityMinimizeButton {
    if (!NSThread.isMainThread || !_jaliumOwner || !IsLiveWindow(_jaliumOwner) || _jaliumOwner->destroying) return nil;
    return [super accessibilityMinimizeButton] ?: QueryWindowAccessibilityButton(_jaliumOwner, JALIUM_AX_MINIMIZE_BUTTON);
}
- (id)accessibilityZoomButton {
    if (!NSThread.isMainThread || !_jaliumOwner || !IsLiveWindow(_jaliumOwner) || _jaliumOwner->destroying) return nil;
    return [super accessibilityZoomButton] ?: QueryWindowAccessibilityButton(_jaliumOwner, JALIUM_AX_ZOOM_BUTTON);
}
- (BOOL)isAccessibilityEnabled {
    // AppKit has no knowledge of the enabled state managed by ShowDialog.
    return NSThread.isMainThread && _jaliumOwner && !_jaliumOwner->destroying && _jaliumOwner->enabled;
}
- (BOOL)isAccessibilityModal {
    JaliumAccessibilityRequest request{};
    if (QueryWindowAccessibilityInfo(_jaliumOwner, &request))
        return (request.flags & JALIUM_AX_MODAL) != 0;
    return [super isAccessibilityModal];
}
- (NSAccessibilitySubrole)accessibilitySubrole {
    bool popup = _jaliumOwner && (_jaliumOwner->style & JALIUM_WINDOW_STYLE_POPUP);
    JaliumAccessibilityRequest request{};
    if (QueryWindowAccessibilityInfo(_jaliumOwner, &request)) {
        if (request.flags & JALIUM_AX_DIALOG) return NSAccessibilityDialogSubrole;
        // AppKit infers a dialog when canBecomeMainWindow returns NO. A
        // disabled ordinary owner remains a standard window during modality.
        if (!popup) return NSAccessibilityStandardWindowSubrole;
    }
    return [super accessibilitySubrole];
}
- (NSString*)accessibilityRoleDescription {
    bool popup = _jaliumOwner && (_jaliumOwner->style & JALIUM_WINDOW_STYLE_POPUP);
    JaliumAccessibilityRequest request{};
    if (QueryWindowAccessibilityInfo(_jaliumOwner, &request)) {
        if (request.flags & JALIUM_AX_DIALOG)
            return NSAccessibilityRoleDescription(self.accessibilityRole, NSAccessibilityDialogSubrole);
        if (!popup)
            return NSAccessibilityRoleDescription(self.accessibilityRole, NSAccessibilityStandardWindowSubrole);
    }
    return [super accessibilityRoleDescription];
}
- (BOOL)canBecomeKeyWindow { return _jaliumOwner && _jaliumOwner->enabled; }
- (BOOL)canBecomeMainWindow { return _jaliumOwner && _jaliumOwner->enabled &&
    !(_jaliumOwner->style & JALIUM_WINDOW_STYLE_POPUP); }
- (BOOL)validateUserInterfaceItem:(id<NSValidatedUserInterfaceItem>)item {
    if (!_jaliumOwner || !_jaliumOwner->enabled) return NO;
    SEL action = item.action;
    if (action == @selector(performClose:) || action == @selector(close))
        return (_jaliumOwner->style & JALIUM_WINDOW_STYLE_CLOSABLE) != 0;
    BOOL changingFrame = _jaliumOwner->fullScreenTransition || _jaliumOwner->appKitZooming ||
        _jaliumOwner->applyingWindowState || _jaliumOwner->miniaturizing || _jaliumOwner->deminiaturizing;
    if (action == @selector(performMiniaturize:) || action == @selector(miniaturize:))
        return !changingFrame && !self.miniaturized && !(self.styleMask & NSWindowStyleMaskFullScreen) &&
            (_jaliumOwner->style & JALIUM_WINDOW_STYLE_MINIMIZABLE);
    if (action == @selector(zoom:) || action == @selector(performZoom:))
        return !changingFrame && !self.miniaturized && !(self.styleMask & NSWindowStyleMaskFullScreen) &&
            (_jaliumOwner->style & JALIUM_WINDOW_STYLE_MAXIMIZABLE);
    if (action == @selector(toggleFullScreen:))
        return !changingFrame && !self.miniaturized &&
            (_jaliumOwner->style & JALIUM_WINDOW_STYLE_MAXIMIZABLE);
    return [super validateUserInterfaceItem:item];
}
- (BOOL)validateMenuItem:(NSMenuItem*)item {
    SEL action = item.action;
    if (action == @selector(toggleFullScreen:) &&
        ([item.title isEqualToString:@"Enter Full Screen"] || [item.title isEqualToString:@"Exit Full Screen"]))
        item.title = (_jaliumOwner && _jaliumOwner->state == JALIUM_WINDOW_STATE_FULLSCREEN)
            ? @"Exit Full Screen" : @"Enter Full Screen";
    if (!_jaliumOwner || !_jaliumOwner->enabled || action == @selector(performClose:) || action == @selector(close) ||
        action == @selector(performMiniaturize:) || action == @selector(miniaturize:) ||
        action == @selector(zoom:) || action == @selector(performZoom:) || action == @selector(toggleFullScreen:))
        return [self validateUserInterfaceItem:item];
    return [super validateMenuItem:item];
}
- (void)miniaturize:(id)sender {
    auto* owner = _jaliumOwner;
    NSWindow* nativeWindow = self;
    if (!owner || !IsLiveWindow(owner, nativeWindow) || owner->miniaturizing || self.miniaturized) return;
    // Cover native chrome/menu actions as well as the platform API. Begin
    // before AppKit's will-miniaturize observers can request a newer state.
    BeginWindowMinimize(owner);
    uint64_t sequence = owner->miniaturizationSequence;
    [super miniaturize:sender];
    // A rejected native request emits no will/did pair. Do not leave later
    // requests waiting for an acknowledgement that cannot arrive.
    if (IsLiveWindow(owner, nativeWindow) && sequence == owner->miniaturizationSequence)
        owner->miniaturizing = false;
}
- (void)performMiniaturize:(id)sender {
    if (!_jaliumOwner || !_jaliumOwner->enabled ||
        !(_jaliumOwner->style & JALIUM_WINDOW_STYLE_MINIMIZABLE)) return;
    // Borderless windows have no native button for performMiniaturize to flash.
    // Use the same state path as their managed caption button and Command-M.
    if (_jaliumOwner->style & JALIUM_WINDOW_STYLE_BORDERLESS)
        ApplyWindowState(_jaliumOwner, JALIUM_WINDOW_STATE_MINIMIZED);
    else [super performMiniaturize:sender];
}
- (void)performClose:(id)sender {
    if (!_jaliumOwner || !_jaliumOwner->enabled ||
        !(_jaliumOwner->style & JALIUM_WINDOW_STYLE_CLOSABLE)) return;
    // AppKit's implementation requires a native close button. Custom chrome
    // has none, so send its menu action through the same cancellable request
    // as the managed caption and Command-W. The callback may destroy us.
    if (_jaliumOwner->style & JALIUM_WINDOW_STYLE_BORDERLESS)
        DispatchSimple(_jaliumOwner, JALIUM_EVENT_CLOSE_REQUESTED);
    else [super performClose:sender];
}
- (BOOL)isZoomed {
    if (_jaliumOwner && _jaliumOwner->appKitZooming) return [super isZoomed];
    return _jaliumOwner && (_jaliumOwner->state == JALIUM_WINDOW_STATE_MAXIMIZED ||
    (_jaliumOwner->state == JALIUM_WINDOW_STATE_MINIMIZED &&
     _jaliumOwner->beforeMinimize == JALIUM_WINDOW_STATE_MAXIMIZED));
}
- (void)zoom:(id)sender {
    if (!_jaliumOwner || !_jaliumOwner->enabled ||
        !(_jaliumOwner->style & JALIUM_WINDOW_STYLE_MAXIMIZABLE) ||
        _jaliumOwner->fullScreenTransition || _jaliumOwner->appKitZooming ||
        (self.styleMask & NSWindowStyleMaskFullScreen)) return;
    if (_jaliumOwner->applyingWindowState || _jaliumOwner->miniaturizing || _jaliumOwner->deminiaturizing) {
        // Preserve AppKit's best-fit Zoom, rather than turning the deferred
        // action into the framework's work-area Fill. A later state request
        // replaces it, and two pending Zoom actions cancel each other.
        bool zoomed = _jaliumOwner->requestedNativeZoom
            ? _jaliumOwner->requestedState == JALIUM_WINDOW_STATE_MAXIMIZED : self.zoomed;
        _jaliumOwner->requestedState = zoomed ? JALIUM_WINDOW_STATE_NORMAL : JALIUM_WINDOW_STATE_MAXIMIZED;
        _jaliumOwner->requestedNativeZoom = true;
        return;
    }
    _jaliumOwner->requestedNativeZoom = false;
    if (self.zoomed) {
        ApplyWindowState(_jaliumOwner, JALIUM_WINDOW_STATE_NORMAL);
        return;
    }
    // AppKit's Zoom uses its standard frame and any delegate best-fit frame.
    // Fill / WindowState.Maximized use our exact work-area frame separately.
    CaptureNormalFrame(_jaliumOwner);
    _jaliumOwner->applyingState = true;
    _jaliumOwner->appKitZooming = true;
    _jaliumOwner->requestedState = JALIUM_WINDOW_STATE_MAXIMIZED;
    [super zoom:sender];
    if (!_jaliumOwner) return;
    _jaliumOwner->applyingState = false;
    _jaliumOwner->appKitZooming = false;
    _jaliumOwner->maximizedFrame = self.frame;
    PublishWindowState(_jaliumOwner, JALIUM_WINDOW_STATE_MAXIMIZED);
    if (_jaliumOwner) ApplyQueuedWindowState(_jaliumOwner);
}
- (void)sendEvent:(NSEvent*)event {
    if (!_jaliumOwner || !_jaliumOwner->enabled) return;
    [super sendEvent:event];
}
- (BOOL)performKeyEquivalent:(NSEvent*)event {
    if (!_jaliumOwner || !_jaliumOwner->enabled) return NO;
    NSString* key = event.charactersIgnoringModifiers.lowercaseString;
    NSUInteger flags = event.modifierFlags;
    if (event.keyCode == 0x30 && (flags & NSEventModifierFlagControl) &&
        !(flags & (NSEventModifierFlagCommand | NSEventModifierFlagOption))) {
        // AppKit consumes Control-Tab for native window tabs before keyDown.
        // Jalium's focusable controls live inside one content responder, so
        // preserve the key for managed focus navigation instead.
        JaliumPlatformEvent managed{}; managed.type = JALIUM_EVENT_KEY_DOWN;
        managed.key.keyCode = 0x09; managed.key.scanCode = event.keyCode;
        managed.key.modifiers = ModifiersFromFlags(flags); managed.key.isRepeat = event.isARepeat;
        DispatchWindowEvent(_jaliumOwner, managed);
        return YES;
    }
    if ((flags & NSEventModifierFlagCommand) &&
        !(flags & (NSEventModifierFlagOption | NSEventModifierFlagShift))) {
        if ([key isEqualToString:@"w"] && !(flags & NSEventModifierFlagControl) &&
            (_jaliumOwner->style & JALIUM_WINDOW_STYLE_CLOSABLE)) {
            // Document hosts can bind Command-W to close a tab. Consult their
            // native menu before falling back to a cancellable window close.
            if ([NSApp.mainMenu performKeyEquivalent:event]) return YES;
            DispatchSimple(_jaliumOwner, JALIUM_EVENT_CLOSE_REQUESTED);
            return YES;
        }
        if ([key isEqualToString:@"m"] && !(flags & NSEventModifierFlagControl) &&
            (_jaliumOwner->style & JALIUM_WINDOW_STYLE_MINIMIZABLE)) {
            ApplyWindowState(_jaliumOwner, JALIUM_WINDOW_STATE_MINIMIZED);
            return YES;
        }
        if ([key isEqualToString:@"f"] && (flags & NSEventModifierFlagControl) &&
            (_jaliumOwner->style & JALIUM_WINDOW_STYLE_MAXIMIZABLE)) {
            ApplyWindowState(_jaliumOwner,
                _jaliumOwner->state == JALIUM_WINDOW_STATE_FULLSCREEN
                    ? _jaliumOwner->beforeFullScreen : JALIUM_WINDOW_STATE_FULLSCREEN);
            return YES;
        }
    }
    return [super performKeyEquivalent:event];
}
@end

@interface JaliumAppleWindowMenuTarget : NSObject <NSMenuItemValidation>
@property(nonatomic, weak) JaliumAppleWindow* window;
@end

@implementation JaliumAppleWindowMenuTarget
- (JaliumAppleWindow*)validWindow {
    JaliumAppleWindow* window = _window;
    auto* owner = window.jaliumOwner;
    return owner && IsLiveWindow(owner, window) && owner->enabled && owner->visible && window.visible
        ? window : nil;
}
- (BOOL)validateMenuItem:(NSMenuItem*)item {
    JaliumAppleWindow* window = [self validWindow];
    return window && [window validateMenuItem:item];
}
- (void)performMiniaturize:(id)sender {
    [[self validWindow] performMiniaturize:sender];
}
- (void)zoom:(id)sender { [[self validWindow] zoom:sender]; }
- (void)performClose:(id)sender { [[self validWindow] performClose:sender]; }
- (void)toggleFullScreen:(id)sender {
    JaliumAppleWindow* window = [self validWindow];
    auto* owner = window.jaliumOwner;
    if (owner && (owner->style & JALIUM_WINDOW_STYLE_MAXIMIZABLE))
        ApplyWindowState(owner, owner->state == JALIUM_WINDOW_STATE_FULLSCREEN
            ? owner->beforeFullScreen : JALIUM_WINDOW_STATE_FULLSCREEN);
}
@end

static NSMenu* CreateAppleWindowMenu(JaliumAppleWindowMenuTarget* target)
{
    NSMenu* menu = [[NSMenu alloc] initWithTitle:@"Window"];
    SEL actions[] = {@selector(performMiniaturize:), @selector(zoom:), @selector(toggleFullScreen:), @selector(performClose:)};
    NSString* titles[] = {@"Minimize", @"Zoom", @"Enter Full Screen", @"Close"};
    NSString* keys[] = {@"m", @"", @"f", @"w"};
    for (int index = 0; index < 4; ++index) {
        NSMenuItem* item = nil;
        // Preserve the host's labels, localization and key equivalents. An
        // explicit target keeps a menu for a secondary window on that window.
        for (NSMenuItem* candidate in NSApp.windowsMenu.itemArray)
            if (candidate.action == actions[index]) { item = candidate.copy; break; }
        if (!item) {
            item = [[NSMenuItem alloc] initWithTitle:titles[index] action:actions[index] keyEquivalent:keys[index]];
            if (index == 2) item.keyEquivalentModifierMask = NSEventModifierFlagCommand | NSEventModifierFlagControl;
        }
        item.target = target;
        if (index == 3) [menu addItem:NSMenuItem.separatorItem];
        [menu addItem:item];
    }
    return menu;
}

static NSCursor* CursorForShape(JaliumCursorShape shape)
{
    switch (shape) {
        case JALIUM_CURSOR_HAND: return NSCursor.pointingHandCursor;
        case JALIUM_CURSOR_IBEAM: return NSCursor.IBeamCursor;
        case JALIUM_CURSOR_CROSSHAIR: return NSCursor.crosshairCursor;
        case JALIUM_CURSOR_RESIZE_ALL: return NSCursor.openHandCursor;
        case JALIUM_CURSOR_NOT_ALLOWED: return NSCursor.operationNotAllowedCursor;
        default: break;
    }
    if (@available(macOS 15.0, *)) {
        switch (shape) {
            case JALIUM_CURSOR_RESIZE_NS: return NSCursor.rowResizeCursor;
            case JALIUM_CURSOR_RESIZE_EW: return NSCursor.columnResizeCursor;
            case JALIUM_CURSOR_RESIZE_NESW:
                return [NSCursor frameResizeCursorFromPosition:NSCursorFrameResizePositionTopRight
                    inDirections:NSCursorFrameResizeDirectionsAll];
            case JALIUM_CURSOR_RESIZE_NWSE:
                return [NSCursor frameResizeCursorFromPosition:NSCursorFrameResizePositionTopLeft
                    inDirections:NSCursorFrameResizeDirectionsAll];
            default: break;
        }
    }
    return NSCursor.arrowCursor;
}

@interface JaliumAppleView : NSView <NSTextInputClient,NSDraggingDestination,NSDraggingSource,NSUserInterfaceValidations>
@property(nonatomic, assign) JaliumPlatformWindow* jaliumOwner;
@property(nonatomic, strong) NSMutableAttributedString* markedText;
@property(nonatomic, copy) NSString* surroundingText;
@property(nonatomic) NSRange selectionRange;
@property(nonatomic) NSRange markedReplacementRange;
@property(nonatomic) NSRange markedSelectionRange;
@property(nonatomic) NSRect imeRect;
@property(nonatomic) BOOL imeEnabled;
@property(nonatomic) BOOL menuEscapeKeyDown;
@property(nonatomic) JaliumCursorShape cursorShape;
@property(nonatomic) BOOL cursorHidden;
@property(nonatomic, strong) NSCursor* shapeCursor;
@property(nonatomic, strong) JaliumAppleAccessibilityBridge* accessibilityBridge;
- (void)applyCursor;
- (void)restoreCursor;
- (void)cancelMarkedText;
@end

#include "platform_apple_accessibility.inc"

@implementation JaliumAppleView
+ (Class)layerClass { return [CAMetalLayer class]; }
- (instancetype)initWithFrame:(NSRect)frame {self=[super initWithFrame:frame];if(self){
    _imeEnabled = YES;
    _selectionRange = NSMakeRange(NSNotFound, 0);
    _markedReplacementRange = NSMakeRange(NSNotFound, 0);
    _shapeCursor = NSCursor.arrowCursor;
    [NSNotificationCenter.defaultCenter addObserver:self
        selector:@selector(scrollerStyleChanged:)
        name:NSPreferredScrollerStyleDidChangeNotification object:nil];
    [NSWorkspace.sharedWorkspace.notificationCenter addObserver:self
        selector:@selector(scrollerStyleChanged:)
        name:NSWorkspaceAccessibilityDisplayOptionsDidChangeNotification object:nil];
    [self registerForDraggedTypes:@[UTTypeData.identifier, UTTypeItem.identifier,
        NSPasteboardTypeHTML, NSPasteboardTypeRTF, NSPasteboardTypeString,NSPasteboardTypeURL,
        NSPasteboardTypeFileURL,NSPasteboardTypePNG,NSPasteboardTypeTIFF]];}return self;}
- (void)dealloc {
    [NSNotificationCenter.defaultCenter removeObserver:self];
    [NSWorkspace.sharedWorkspace.notificationCenter removeObserver:self];
}
- (void)scrollerStyleChanged:(NSNotification*)notification {
    if (_jaliumOwner) ApplyAppleSurfaceAppearance(_jaliumOwner);
    DispatchSimple(_jaliumOwner, JALIUM_EVENT_SCROLLBAR_SETTINGS_CHANGED);
    (void)notification;
}
- (BOOL)isFlipped { return YES; }
- (NSArray*)accessibilityChildren {
    if (!NSThread.isMainThread || !_jaliumOwner || !IsLiveWindow(_jaliumOwner, self.window) ||
        _jaliumOwner->destroying || !_jaliumOwner->visible || !self.window.visible) return @[];
    JaliumAppleAccessibilityBridge* bridge = _accessibilityBridge;
    if (!bridge) return super.accessibilityChildren;
    JaliumAccessibilityRequest request{};
    request.nodeId = 1; request.operation = JALIUM_AX_INFO;
    id root = [bridge query:&request] ? [bridge element:1] : nil;
    return root ? @[root] : @[];
}
- (id)accessibilityHitTest:(NSPoint)point {
    if (!NSThread.isMainThread || !_jaliumOwner || !IsLiveWindow(_jaliumOwner, self.window) ||
        _jaliumOwner->destroying || !_jaliumOwner->visible || !self.window.visible) return nil;
    JaliumAppleAccessibilityBridge* bridge = _accessibilityBridge;
    return bridge ? [bridge hitTest:point] : [super accessibilityHitTest:point];
}
- (id)accessibilityFocusedUIElement {
    // A retained view can outlive its closed Window. Do not fall back to
    // AppKit's cached responder after its managed owner has been detached.
    if (!NSThread.isMainThread || !_jaliumOwner || !IsLiveWindow(_jaliumOwner) ||
        _jaliumOwner->destroying || !_jaliumOwner->enabled || !_jaliumOwner->visible ||
        !self.window.visible || !self.window.keyWindow) return nil;
    JaliumAppleAccessibilityBridge* bridge = _accessibilityBridge;
    return bridge ? [bridge focusedElement] : [super accessibilityFocusedUIElement];
}
- (BOOL)acceptsFirstResponder { return _jaliumOwner && _jaliumOwner->enabled; }
- (BOOL)becomeFirstResponder { DispatchSimple(_jaliumOwner, JALIUM_EVENT_FOCUS_GAINED); return YES; }
- (BOOL)resignFirstResponder { [self restoreCursor]; DispatchSimple(_jaliumOwner, JALIUM_EVENT_FOCUS_LOST); return YES; }
- (NSTextInputContext*)inputContext {
    return _jaliumOwner && _jaliumOwner->enabled && self.imeEnabled ? super.inputContext : nil;
}
- (void)restoreCursor {
    // NSCursor hide/unhide uses a process-wide nesting count. Balance only
    // this view's hide request, including when focus or the window disappears.
    if (_cursorHidden) { [NSCursor unhide]; _cursorHidden = NO; }
}
- (void)applyCursor {
    if (_cursorShape == JALIUM_CURSOR_HIDDEN) {
        if (!_cursorHidden) { [NSCursor hide]; _cursorHidden = YES; }
        return;
    }
    [self restoreCursor];
    [NSCursor setHiddenUntilMouseMoves:NO];
    NSCursor* cursor = _shapeCursor;
    if (NSCursor.currentCursor != cursor) [cursor set];
}
- (void)cursorUpdate:(NSEvent*)event { [self applyCursor]; }
- (void)resetCursorRects {
    [super resetCursorRects];
    [self addCursorRect:self.visibleRect cursor:_shapeCursor];
}
- (void)updateTrackingAreas {
    [super updateTrackingAreas];
    for (NSTrackingArea* area in self.trackingAreas) [self removeTrackingArea:area];
    NSTrackingArea* area = [[NSTrackingArea alloc] initWithRect:self.bounds
        options:NSTrackingMouseEnteredAndExited|NSTrackingMouseMoved|NSTrackingCursorUpdate|
                NSTrackingActiveInKeyWindow|NSTrackingInVisibleRect
        owner:self userInfo:nil];
    [self addTrackingArea:area];
}
- (void)dispatchMouse:(NSEvent*)native type:(JaliumEventType)type button:(int32_t)button {
    if (!_jaliumOwner || !_jaliumOwner->enabled) return;
    NSPoint point=[self convertPoint:native.locationInWindow fromView:nil];
    JaliumPlatformEvent event{}; event.type=type;
    event.mouse.x=point.x*_jaliumOwner->scale;event.mouse.y=point.y*_jaliumOwner->scale;
    event.mouse.button=button;event.mouse.modifiers=ModifiersFromFlags(native.modifierFlags);
    event.mouse.clickCount=static_cast<int32_t>(native.clickCount);
    NSUInteger buttons = NSEvent.pressedMouseButtons;
    if (native.type == NSEventTypeLeftMouseDown || native.type == NSEventTypeLeftMouseDragged) buttons |= 1;
    else if (native.type == NSEventTypeLeftMouseUp) buttons &= ~NSUInteger(1);
    if (native.type == NSEventTypeRightMouseDown || native.type == NSEventTypeRightMouseDragged) buttons |= 2;
    else if (native.type == NSEventTypeRightMouseUp) buttons &= ~NSUInteger(2);
    if (native.type == NSEventTypeOtherMouseDown || native.type == NSEventTypeOtherMouseDragged) buttons |= NSUInteger(1) << native.buttonNumber;
    else if (native.type == NSEventTypeOtherMouseUp) buttons &= ~(NSUInteger(1) << native.buttonNumber);
    event.mouse.buttonStates = 0x80000000 | (static_cast<uint32_t>(buttons) & 0x1f);
    DispatchWindowEvent(_jaliumOwner,event);
}
- (void)mouseMoved:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_MOVE button:0];[self applyCursor];}
- (void)mouseDragged:(NSEvent*)e {[self mouseMoved:e];}
- (void)rightMouseDragged:(NSEvent*)e {[self mouseMoved:e];}
- (void)otherMouseDragged:(NSEvent*)e {[self mouseMoved:e];}
- (void)mouseDown:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_DOWN button:JALIUM_MOUSE_BUTTON_LEFT];}
- (void)mouseUp:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_UP button:JALIUM_MOUSE_BUTTON_LEFT];}
- (void)rightMouseDown:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_DOWN button:JALIUM_MOUSE_BUTTON_RIGHT];}
- (void)rightMouseUp:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_UP button:JALIUM_MOUSE_BUTTON_RIGHT];}
- (void)otherMouseDown:(NSEvent*)e {
    if (e.buttonNumber >= 2 && e.buttonNumber <= 4)
        [self dispatchMouse:e type:JALIUM_EVENT_MOUSE_DOWN button:static_cast<int32_t>(e.buttonNumber)];
}
- (void)otherMouseUp:(NSEvent*)e {
    if (e.buttonNumber >= 2 && e.buttonNumber <= 4)
        [self dispatchMouse:e type:JALIUM_EVENT_MOUSE_UP button:static_cast<int32_t>(e.buttonNumber)];
}
- (void)mouseEntered:(NSEvent*)e {
    if (!_jaliumOwner || !_jaliumOwner->enabled) return;
    DispatchSimple(_jaliumOwner,JALIUM_EVENT_MOUSE_ENTER);[self applyCursor];
}
- (void)mouseExited:(NSEvent*)e {
    if (!_jaliumOwner || !_jaliumOwner->enabled) return;
    DispatchSimple(_jaliumOwner,JALIUM_EVENT_MOUSE_LEAVE);[self restoreCursor];[NSCursor.arrowCursor set];
}
- (void)scrollWheel:(NSEvent*)native {
    if (!_jaliumOwner || !_jaliumOwner->enabled) return;
    NSPoint point=[self convertPoint:native.locationInWindow fromView:nil];
    JaliumPlatformEvent event{};event.type=JALIUM_EVENT_MOUSE_WHEEL;
    event.wheel.x=point.x*_jaliumOwner->scale;event.wheel.y=point.y*_jaliumOwner->scale;
    // The platform ABI carries wheel notches. Managed scrolling expands one
    // notch to three 16-point lines; AppKit instead supplies points for precise
    // devices and individual lines for a conventional mouse wheel.
    const CGFloat unitsPerNotch = native.hasPreciseScrollingDeltas ? 48.0 : 3.0;
    // AppKit's positive X moves toward the start; the shared ABI uses rightward X.
    // AppKit has already applied the user's natural-scrolling preference.
    event.wheel.deltaX=-native.scrollingDeltaX/unitsPerNotch;
    event.wheel.deltaY=native.scrollingDeltaY/unitsPerNotch;
    event.wheel.modifiers=ModifiersFromFlags(native.modifierFlags);
    event.wheel.isPrecise=native.hasPreciseScrollingDeltas;
    event.wheel.phase=static_cast<int32_t>(native.phase);
    event.wheel.momentumPhase=static_cast<int32_t>(native.momentumPhase);
    // Wheel and momentum packets can arrive while a button is held, or after
    // its release outside this view. Publish the current system snapshot.
    event.wheel.buttonStates=0x80000000 | (static_cast<uint32_t>(NSEvent.pressedMouseButtons) & 0x1f);
    DispatchWindowEvent(_jaliumOwner,event);
}
- (void)keyDown:(NSEvent*)native {
    if (!_jaliumOwner || !_jaliumOwner->enabled) return;
    if (VirtualKeyForAppleEvent(native) == 0x1b) {
        // Targeted input can reach the content responder while AppKit's menu
        // loop owns the keyboard. Escape dismisses that menu before either
        // managed IsCancel handling or the editor's composition cancellation.
        // Keep repeats and the release paired with the consumed press even
        // though cancelTracking ends tracking synchronously.
        if (self.menuEscapeKeyDown && native.isARepeat) return;
        self.menuEscapeKeyDown = NO;
        NSMenu* menu = g_trackingMenus.lastObject;
        if (menu) {
            self.menuEscapeKeyDown = YES;
            [menu cancelTracking];
            return;
        }
    }
    // Targeted events can arrive at the responder without NSApplication's
    // key-equivalent pass. Keep window shortcuts available on that path too.
    if ((native.modifierFlags & NSEventModifierFlagCommand) &&
        [self.window performKeyEquivalent:native]) return;
    JaliumPlatformEvent event{};event.type=JALIUM_EVENT_KEY_DOWN;
    event.key.keyCode=VirtualKeyForAppleEvent(native);event.key.scanCode=native.keyCode;
    event.key.modifiers=ModifiersFromFlags(native.modifierFlags);event.key.isRepeat=native.isARepeat;
    // While an IME owns marked text, editing keys belong to AppKit. Sending
    // Backspace to managed text as well would delete committed content.
    // Application shortcuts remain available during composition. A shortcut
    // which changes managed focus will discard the old marked segment through
    // the normal IME-context update before AppKit interprets this event.
    if(!self.markedText || (native.modifierFlags & NSEventModifierFlagCommand))
        DispatchWindowEvent(_jaliumOwner,event);
    if (_jaliumOwner && _jaliumOwner->enabled && self.imeEnabled) [self interpretKeyEvents:@[native]];
}
- (void)keyUp:(NSEvent*)native {
    if (self.menuEscapeKeyDown && VirtualKeyForAppleEvent(native) == 0x1b) {
        self.menuEscapeKeyDown = NO;
        return;
    }
    if (!_jaliumOwner || !_jaliumOwner->enabled) return;
    JaliumPlatformEvent event{};event.type=JALIUM_EVENT_KEY_UP;
    event.key.keyCode=VirtualKeyForAppleEvent(native);
    event.key.scanCode=native.keyCode;event.key.modifiers=ModifiersFromFlags(native.modifierFlags);
    DispatchWindowEvent(_jaliumOwner,event);
}
- (void)flagsChanged:(NSEvent*)native {
    if (!_jaliumOwner || !_jaliumOwner->enabled) return;
    int32_t key = 0;
    NSUInteger mask = 0;
    switch (native.keyCode) {
        case 0x38: key = 0xa0; mask = NX_DEVICELSHIFTKEYMASK; break;
        case 0x3c: key = 0xa1; mask = NX_DEVICERSHIFTKEYMASK; break;
        case 0x3b: key = 0xa2; mask = NX_DEVICELCTLKEYMASK; break;
        case 0x3e: key = 0xa3; mask = NX_DEVICERCTLKEYMASK; break;
        case 0x3a: key = 0xa4; mask = NX_DEVICELALTKEYMASK; break;
        case 0x3d: key = 0xa5; mask = NX_DEVICERALTKEYMASK; break;
        case 0x37: key = 0x5b; mask = NX_DEVICELCMDKEYMASK; break;
        case 0x36: key = 0x5c; mask = NX_DEVICERCMDKEYMASK; break;
        case 0x39: key = 0x14; mask = NSEventModifierFlagCapsLock; break;
        default: return;
    }
    JaliumPlatformEvent event{};
    event.type = (native.modifierFlags & mask) ? JALIUM_EVENT_KEY_DOWN : JALIUM_EVENT_KEY_UP;
    event.key.keyCode = key; event.key.scanCode = native.keyCode;
    event.key.modifiers = ModifiersFromFlags(native.modifierFlags);
    DispatchWindowEvent(_jaliumOwner, event);
}
- (int32_t)requestTextRange:(NSRange)range replacement:(NSString*)replacement {
    if (!_jaliumOwner || !_jaliumOwner->enabled || !self.imeEnabled ||
        !self.surroundingText || !ValidTextRange(range, self.surroundingText.length)) return -1;
    JaliumPlatformEvent event{}; event.type = JALIUM_EVENT_IME_TEXT_REQUEST;
    std::string utf8 = replacement.UTF8String ?: "";
    event.imeTextRequest.utf8Text = replacement ? utf8.c_str() : nullptr;
    event.imeTextRequest.start = static_cast<int32_t>(range.location);
    event.imeTextRequest.length = static_cast<int32_t>(range.length);
    event.imeTextRequest.replace = replacement != nil;
    int32_t applied = 0;
    event.imeTextRequest.applied = &applied;
    DispatchWindowEvent(_jaliumOwner, event);
    return _jaliumOwner && _jaliumOwner->enabled && self.imeEnabled ? applied : -1;
}
- (int32_t)queryTextGeometry:(int32_t)kind range:(NSRange)range point:(NSPoint)point
    result:(JaliumImeGeometryResult*)result {
    if (!_jaliumOwner || !_jaliumOwner->enabled || !self.imeEnabled ||
        range.location > INT32_MAX || range.length > INT32_MAX) return -1;
    JaliumPlatformEvent event{}; event.type = JALIUM_EVENT_IME_GEOMETRY_REQUEST;
    event.imeGeometryRequest.kind = kind;
    event.imeGeometryRequest.start = static_cast<int32_t>(range.location);
    event.imeGeometryRequest.length = static_cast<int32_t>(range.length);
    event.imeGeometryRequest.x = point.x * _jaliumOwner->scale;
    event.imeGeometryRequest.y = point.y * _jaliumOwner->scale;
    event.imeGeometryRequest.result = result;
    DispatchWindowEvent(_jaliumOwner, event);
    return _jaliumOwner && _jaliumOwner->enabled && self.imeEnabled ? result->handled : -1;
}
- (void)dispatchCommittedText:(NSString*)text composition:(BOOL)composition {
    if (composition) {
        JaliumPlatformEvent event{}; event.type = JALIUM_EVENT_COMPOSITION_END;
        std::string utf8 = text.UTF8String ?: ""; event.composition.utf8Text = utf8.c_str();
        DispatchWindowEvent(_jaliumOwner, event);
        return;
    }
    for (NSUInteger i = 0; i < text.length;) {
        unichar hi = [text characterAtIndex:i++]; uint32_t cp = hi;
        if (hi >= 0xd800 && hi <= 0xdbff && i < text.length) {
            unichar lo = [text characterAtIndex:i];
            if (lo >= 0xdc00 && lo <= 0xdfff) { ++i; cp = 0x10000 + ((hi - 0xd800) << 10) + (lo - 0xdc00); }
        }
        JaliumPlatformEvent event{}; event.type = JALIUM_EVENT_CHAR_INPUT;
        event.character.codepoint = cp; DispatchWindowEvent(_jaliumOwner, event);
        if (!_jaliumOwner || !_jaliumOwner->enabled || !self.imeEnabled) return;
    }
}
- (NSAttributedString*)textStorageSnapshot {
    if (!self.surroundingText && !self.markedText) return nil;
    auto* storage = [[NSMutableAttributedString alloc] initWithString:self.surroundingText ?: @""];
    if (self.markedText) {
        NSRange replacement = self.markedReplacementRange;
        if (ValidTextRange(replacement, storage.length))
            [storage replaceCharactersInRange:replacement withAttributedString:self.markedText];
        else if (!self.surroundingText)
            [storage appendAttributedString:self.markedText];
    }
    return storage;
}
- (void)insertText:(id)value replacementRange:(NSRange)range {
    if (!_jaliumOwner || !_jaliumOwner->enabled || !self.imeEnabled) return;
    NSString* text = [value isKindOfClass:NSAttributedString.class] ? [value string] : value;
    if (![text isKindOfClass:NSString.class]) return;
    if (self.surroundingText) {
        BOOL hadMarkedText = self.markedText != nil;
        NSRange requested = range.location == NSNotFound
            ? (self.markedText ? self.markedRange : self.selectionRange) : range;
        NSString* virtualText = self.textStorageSnapshot.string;
        if (!ValidTextRange(requested, virtualText.length)) return;
        NSRange committed = requested;
        NSString* replacement = text;
        if (self.markedText) {
            NSRange mark = self.markedRange;
            NSRange original = self.markedReplacementRange;
            NSUInteger end = NSMaxRange(requested);
            NSUInteger committedEnd = end <= mark.location ? end :
                end < NSMaxRange(mark) ? NSMaxRange(original) : end - mark.length + original.length;
            committed.location = std::min(requested.location, original.location);
            committed.length = std::max(committedEnd, NSMaxRange(original)) - committed.location;
            NSString* updated = [virtualText stringByReplacingCharactersInRange:requested withString:text];
            NSUInteger replacementLength = updated.length - (self.surroundingText.length - committed.length);
            replacement = [updated substringWithRange:NSMakeRange(committed.location, replacementLength)];
        }
        self.markedText = nil;
        self.markedReplacementRange = NSMakeRange(NSNotFound, 0);
        int32_t applied = [self requestTextRange:committed replacement:replacement];
        // Preserve ordinary input for native clients which predate the range
        // request event. An explicit rejection never falls through to insertion.
        if (applied == 0 && range.location == NSNotFound)
            [self dispatchCommittedText:text composition:hadMarkedText];
        return;
    }
    // Private editors intentionally expose no document snapshot. They can
    // still compose and commit at the current caret, without range queries.
    if (range.location != NSNotFound) return;
    BOOL hadMarkedText = self.markedText != nil;
    self.markedText=nil;
    [self dispatchCommittedText:text composition:hadMarkedText];
}
- (void)setMarkedText:(id)value selectedRange:(NSRange)selected replacementRange:(NSRange)replacement {
    if (!_jaliumOwner || !_jaliumOwner->enabled || !self.imeEnabled) return;
    NSString* text=[value isKindOfClass:[NSAttributedString class]]?[value string]:value;
    if (![text isKindOfClass:NSString.class]) return;
    if (self.markedText && replacement.location != NSNotFound &&
        !NSEqualRanges(replacement, self.markedRange)) {
        // A replacement of a different span ends the previous marked segment;
        // its preserved prefix/suffix become committed storage before re-anchoring.
        [self unmarkText];
        if (!_jaliumOwner || !_jaliumOwner->enabled || !self.imeEnabled) return;
    }
    BOOL starting=self.markedText==nil;
    if (starting) {
        NSRange anchor = replacement.location == NSNotFound ? self.selectionRange : replacement;
        if (self.surroundingText) {
            if (!ValidTextRange(anchor, self.surroundingText.length)) return;
            if (!NSEqualRanges(anchor, self.selectionRange) && [self requestTextRange:anchor replacement:nil] != 1) return;
            anchor = self.selectionRange;
        } else {
            if (replacement.location != NSNotFound) return;
            anchor = NSMakeRange(0, 0);
        }
        self.markedReplacementRange = anchor;
    }
    self.markedText = [value isKindOfClass:NSAttributedString.class]
        ? [value mutableCopy] : [[NSMutableAttributedString alloc] initWithString:text];
    NSUInteger cursor = std::min(selected.location, text.length);
    self.markedSelectionRange = NSMakeRange(cursor, std::min(selected.length, text.length - cursor));
    if(starting)DispatchSimple(_jaliumOwner,JALIUM_EVENT_COMPOSITION_START);
    if (!_jaliumOwner || !_jaliumOwner->enabled || !self.imeEnabled) return;
    JaliumPlatformEvent event{};event.type=JALIUM_EVENT_COMPOSITION_UPDATE;
    std::string utf8=text.UTF8String?:"";event.composition.utf8Text=utf8.c_str();
    event.composition.cursor=static_cast<int32_t>(cursor);
    DispatchWindowEvent(_jaliumOwner,event);
    if (text.length == 0 && _jaliumOwner) {
        self.markedText = nil;
        self.markedReplacementRange = NSMakeRange(NSNotFound, 0);
        DispatchSimple(_jaliumOwner, JALIUM_EVENT_COMPOSITION_END);
    }
}
- (void)unmarkText {
    if (!self.markedText) return;
    [self insertText:self.markedText.string replacementRange:NSMakeRange(NSNotFound, 0)];
}
- (BOOL)hasMarkedText{return self.markedText.length>0;}
- (NSRange)markedRange{return self.markedText.length?NSMakeRange(self.markedReplacementRange.location,self.markedText.length):NSMakeRange(NSNotFound,0);}
- (NSRange)selectedRange{return self.markedText ? NSMakeRange(self.markedReplacementRange.location + self.markedSelectionRange.location,
    self.markedSelectionRange.length) : _selectionRange;}
- (void)setSelectedRange:(NSRange)value{_selectionRange=value;}
- (NSArray<NSAttributedStringKey>*)validAttributesForMarkedText{return @[];}
- (NSAttributedString*)attributedSubstringForProposedRange:(NSRange)range actualRange:(NSRangePointer)actual {
    if (actual) *actual = NSMakeRange(NSNotFound, 0);
    if (!_jaliumOwner || !self.imeEnabled) return nil;
    NSAttributedString* storage = self.textStorageSnapshot;
    if (!storage || range.location == NSNotFound || range.location > storage.length) return nil;
    range.length = std::min(range.length, storage.length - range.location);
    if (range.length != 0) range = [storage.string rangeOfComposedCharacterSequencesForRange:range];
    if (actual) *actual = range;
    return [storage attributedSubstringFromRange:range];
}
- (NSAttributedString*)attributedString { return self.textStorageSnapshot ?: [[NSAttributedString alloc] initWithString:@""]; }
- (NSUInteger)characterIndexForPoint:(NSPoint)point {
    if (!self.window || !_jaliumOwner || !_jaliumOwner->enabled || !self.imeEnabled ||
        !std::isfinite(point.x) || !std::isfinite(point.y)) return NSNotFound;
    NSPoint local = [self convertPoint:[self.window convertPointFromScreen:point] fromView:nil];
    NSRange marked = self.markedRange;
    if (marked.location != NSNotFound) {
        JaliumImeGeometryResult result{};
        if ([self queryTextGeometry:3 range:NSMakeRange(0, 0) point:local result:&result] == 1 &&
            result.characterIndex >= 0 && static_cast<NSUInteger>(result.characterIndex) <= marked.length)
            return marked.location + result.characterIndex;
    }
    JaliumImeGeometryResult result{};
    int32_t handled = [self queryTextGeometry:1 range:NSMakeRange(0, 0) point:local result:&result];
    if (handled == 1 && self.surroundingText && result.characterIndex >= 0 &&
        static_cast<NSUInteger>(result.characterIndex) <= self.surroundingText.length) {
        NSUInteger index = result.characterIndex;
        index = NormalizedTextRange(self.surroundingText, NSMakeRange(index, 0)).location;
        if (marked.location != NSNotFound && index >= marked.location) {
            if (index < NSMaxRange(self.markedReplacementRange)) return NSNotFound;
            index = index - self.markedReplacementRange.length + marked.length;
        }
        return index;
    }
    if (handled == 0 && NSPointInRect(local, self.imeRect)) return self.selectedRange.location;
    return NSNotFound;
}
- (NSRect)firstRectForCharacterRange:(NSRange)range actualRange:(NSRangePointer)actual {
    if (actual) *actual = NSMakeRange(NSNotFound, 0);
    if (!self.window || !_jaliumOwner || !_jaliumOwner->enabled || !self.imeEnabled) return NSZeroRect;
    // A request with no document index asks for the current candidate anchor;
    // do not invent an absolute index for private editors or selected text.
    if (range.location == NSNotFound) {
        NSRect caret = self.imeRect; caret.size.width = 0;
        return [self.window convertRectToScreen:[self convertRect:caret toView:nil]];
    }
    NSAttributedString* storage = self.textStorageSnapshot;
    if (!storage || range.location > storage.length) return NSZeroRect;
    range.length = std::min(range.length, storage.length - range.location);
    range = NormalizedTextRange(storage.string, range);
    NSRange query = range;
    NSRange marked = self.markedRange;
    int32_t kind = 0;
    NSInteger documentDelta = 0;
    if (marked.location != NSNotFound) {
        if (range.location < marked.location) {
            query.length = std::min(query.length, marked.location - range.location);
        } else if (range.location < NSMaxRange(marked) ||
            (range.location == NSMaxRange(marked) && range.length == 0)) {
            kind = 2;
            query.location -= marked.location;
            query.length = std::min(query.length, marked.length - query.location);
            documentDelta = marked.location;
        } else {
            documentDelta = static_cast<NSInteger>(marked.length) - static_cast<NSInteger>(self.markedReplacementRange.length);
            query.location = static_cast<NSUInteger>(static_cast<NSInteger>(range.location) - documentDelta);
        }
    }
    JaliumImeGeometryResult result{};
    int32_t handled = [self queryTextGeometry:kind range:query point:NSZeroPoint result:&result];
    if (handled == 1 && result.start >= 0 && result.length >= 0 &&
        static_cast<NSUInteger>(result.start) >= query.location &&
        static_cast<NSUInteger>(result.start) <= NSMaxRange(query) &&
        static_cast<NSUInteger>(result.length) <= NSMaxRange(query) - result.start &&
        (query.length == 0 || result.length > 0) &&
        std::isfinite(result.x) && std::isfinite(result.y) && std::isfinite(result.width) &&
        std::isfinite(result.height) && result.width >= 0 && result.height > 0) {
        if (actual) *actual = NSMakeRange(static_cast<NSUInteger>(result.start + documentDelta), result.length);
        CGFloat scale = _jaliumOwner->scale;
        NSRect rectangle = NSMakeRect(result.x / scale, result.y / scale, result.width / scale, result.height / scale);
        return [self.window convertRectToScreen:[self convertRect:rectangle toView:nil]];
    }
    // Keep caret-only hosts working, but never claim their caret is geometry
    // for an arbitrary character range.
    if (handled == 0 && range.length == 0 && range.location == self.selectedRange.location) {
        if (actual) *actual = range;
        NSRect caret = self.imeRect; caret.size.width = 0;
        return [self.window convertRectToScreen:[self convertRect:caret toView:nil]];
    }
    return NSZeroRect;
}
- (void)doCommandBySelector:(SEL)selector {
    if (!_jaliumOwner || !_jaliumOwner->enabled || !self.imeEnabled) return;
    // A text input method can hand Escape back as cancelOperation: without
    // sending an empty marked-text update. keyDown deliberately did not send
    // that editing key to managed input, so this callback must cancel pre-edit.
    if (selector == @selector(cancelOperation:) && self.markedText) {
        [self cancelMarkedText];
        return;
    }
    // keyDown already forwarded editing commands to managed input. AppKit
    // invokes this callback during interpretKeyEvents; forwarding again would
    // delete two characters for a single Backspace.
    (void)selector;
}
- (void)cancelMarkedText {
    if (!self.markedText) return;
    NSTextInputContext* context = self.inputContext;
    self.markedText = nil;
    self.markedReplacementRange = NSMakeRange(NSNotFound, 0);
    self.markedSelectionRange = NSMakeRange(0, 0);
    // Clear the client first: discard can call back into the text-input client.
    [context discardMarkedText];
    DispatchSimple(_jaliumOwner, JALIUM_EVENT_COMPOSITION_END);
}
- (void)dispatchEditingKey:(int32_t)key modifiers:(uint32_t)modifiers {
    if (!_jaliumOwner || !_jaliumOwner->enabled) return;
    JaliumPlatformEvent event{};event.type=JALIUM_EVENT_KEY_DOWN;
    event.key.keyCode=key;event.key.modifiers=modifiers;
    DispatchWindowEvent(_jaliumOwner,event);
    event.type=JALIUM_EVENT_KEY_UP;DispatchWindowEvent(_jaliumOwner,event);
}
- (BOOL)validateUserInterfaceItem:(id<NSValidatedUserInterfaceItem>)item {
    JaliumPlatformWindow* owner = _jaliumOwner;
    NSWindow* nativeWindow = self.window;
    if (!NSThread.isMainThread || !owner || !IsLiveWindow(owner, nativeWindow) ||
        !owner->enabled || !owner->visible || !nativeWindow.visible || nativeWindow.miniaturized)
        return NO;
    SEL action = item.action;
    int32_t command = action == @selector(copy:) ? JALIUM_EDIT_COPY :
        action == @selector(cut:) ? JALIUM_EDIT_CUT :
        action == @selector(paste:) ? JALIUM_EDIT_PASTE :
        action == @selector(selectAll:) ? JALIUM_EDIT_SELECT_ALL :
        action == @selector(undo:) ? JALIUM_EDIT_UNDO :
        action == @selector(redo:) ? JALIUM_EDIT_REDO : -1;
    // Preserve selectors supplied by the application and hosts predating the query bridge.
    if (command < 0 || !owner->editingCommandQuery) return YES;
    auto query = owner->editingCommandQuery;
    void* userData = owner->editingCommandUserData;
    BOOL allowed = query(command, userData) != 0;
    // Custom CanExecute handlers can close or replace this window while queried.
    return allowed && IsLiveWindow(owner, nativeWindow) && owner->enabled && owner->visible &&
        nativeWindow.visible && !nativeWindow.miniaturized;
}
// Standard AppKit responder actions let native menus, services and automation
// invoke the same managed editing commands as keyboard gestures.
// Preserve their Command identity: physical Control-A is a text navigation
// command, while selectAll: must reach the framework's primary shortcut.
- (void)copy:(id)sender {[self dispatchEditingKey:'C' modifiers:JALIUM_MOD_META];(void)sender;}
- (void)cut:(id)sender {[self dispatchEditingKey:'X' modifiers:JALIUM_MOD_META];(void)sender;}
- (void)paste:(id)sender {[self dispatchEditingKey:'V' modifiers:JALIUM_MOD_META];(void)sender;}
- (void)selectAll:(id)sender {[self dispatchEditingKey:'A' modifiers:JALIUM_MOD_META];(void)sender;}
- (void)undo:(id)sender {[self dispatchEditingKey:'Z' modifiers:JALIUM_MOD_META];(void)sender;}
- (void)redo:(id)sender {[self dispatchEditingKey:'Z' modifiers:JALIUM_MOD_META|JALIUM_MOD_SHIFT];(void)sender;}
- (NSInteger)conversationIdentifier{return (NSInteger)(__bridge void*)self;}
- (NSDragOperation)draggingEntered:(id<NSDraggingInfo>)sender {
    JaliumPlatformWindow* window = _jaliumOwner;
    if (!sender || !CanReceiveAppleDrag(window)) return NSDragOperationNone;
    uint64_t generation = window->dragTargetGeneration + 1;
    DispatchDragEvent(window, sender, JALIUM_EVENT_DRAG_ENTER);
    if (_jaliumOwner != window || !CanReceiveAppleDrag(window) ||
        window->dragTargetGeneration != generation || !window->dragTargetActive ||
        window->dragTargetNativeSequence != static_cast<uint64_t>(sender.draggingSequenceNumber)) return NSDragOperationNone;
    return OperationFromEffects(window->dragEffect & EffectsFromOperation(sender.draggingSourceOperationMask));
}
- (NSDragOperation)draggingUpdated:(id<NSDraggingInfo>)sender {
    JaliumPlatformWindow* window = _jaliumOwner;
    uint64_t sequence = static_cast<uint64_t>(sender.draggingSequenceNumber);
    if (!sender || !CanReceiveAppleDrag(window) || !window->dragTargetActive || window->dragTargetNativeSequence != sequence) return NSDragOperationNone;
    uint64_t generation = window->dragTargetGeneration;
    DispatchDragEvent(window, sender, JALIUM_EVENT_DRAG_OVER);
    if (_jaliumOwner != window || !CanReceiveAppleDrag(window) || !window->dragTargetActive ||
        window->dragTargetGeneration != generation || window->dragTargetNativeSequence != sequence)
        return NSDragOperationNone;
    return OperationFromEffects(window->dragEffect & EffectsFromOperation(sender.draggingSourceOperationMask));
}
- (void)draggingExited:(id<NSDraggingInfo>)sender {
    JaliumPlatformWindow* window = _jaliumOwner;
    if (!CanReceiveAppleDrag(window) || !window->dragTargetActive) return;
    uint64_t sequence = window->dragTargetNativeSequence;
    if (sender && static_cast<uint64_t>(sender.draggingSequenceNumber) != sequence) return;
    uint64_t generation = window->dragTargetGeneration;
    DispatchDragEvent(window, sender, JALIUM_EVENT_DRAG_LEAVE);
    if (_jaliumOwner == window && IsLiveWindow(window) && window->dragTargetGeneration == generation &&
        window->dragTargetNativeSequence == sequence) {
        window->dragTargetActive = false;
        window->dragTargetNativeSequence = 0;
        window->dragTargetPasteboard = nil; window->dragTargetSource = nil;
        window->dragSession = 0;
    }
}
- (void)draggingEnded:(id<NSDraggingInfo>)sender {
    if (sender) [self draggingExited:sender];
}
- (BOOL)prepareForDragOperation:(id<NSDraggingInfo>)sender {
    JaliumPlatformWindow* window = _jaliumOwner;
    return sender && CanReceiveAppleDrag(window) && window->dragTargetActive &&
        !AppleDragCancelled(window->dragTargetSource) &&
        window->dragTargetNativeSequence == static_cast<uint64_t>(sender.draggingSequenceNumber) &&
        (window->dragEffect & EffectsFromOperation(sender.draggingSourceOperationMask)) != JALIUM_DRAG_EFFECT_NONE;
}
- (BOOL)performDragOperation:(id<NSDraggingInfo>)sender {
    JaliumPlatformWindow* window = _jaliumOwner;
    uint64_t sequence = static_cast<uint64_t>(sender.draggingSequenceNumber);
    if (!sender || !CanReceiveAppleDrag(window) || !window->dragTargetActive || window->dragTargetNativeSequence != sequence ||
        AppleDragCancelled(window->dragTargetSource)) return NO;
    uint64_t generation = window->dragTargetGeneration;
    // Keep the visit pasteboard available to the managed lazy reader until all
    // Drop handlers have snapshotted their representations. Legacy listeners
    // still receive the first representation in the unchanged event ABI.
    NSPasteboard* pasteboard = window->dragTargetPasteboard;
    std::string mimeStorage; NSData* payload = nil;
    NSUInteger payloadSize = 0;
    for (NSPasteboardType type in pasteboard.types) {
        MimeForPasteboardType(type, mimeStorage);
        payload = ReadApplePasteboardData(pasteboard, mimeStorage.c_str());
        // Each promised read can retire this visit or start its successor.
        if (_jaliumOwner != window || !CanReceiveAppleDrag(window) || !window->dragTargetActive ||
            window->dragTargetGeneration != generation || window->dragTargetNativeSequence != sequence ||
            AppleDragCancelled(window->dragTargetSource)) return NO;
        payloadSize = payload.length;
        if (payload && payloadSize <= MaxApplePasteboardPayloadBytes) break;
        payload = nil;
    }
    if (!payload) return NO;
    const uint8_t* payloadBytes = static_cast<const uint8_t*>(payload.bytes);
    if (_jaliumOwner != window || !CanReceiveAppleDrag(window) || !window->dragTargetActive ||
        window->dragTargetGeneration != generation || window->dragTargetNativeSequence != sequence ||
        AppleDragCancelled(window->dragTargetSource)) return NO;
    DispatchDragEvent(window, sender, JALIUM_EVENT_DROP, mimeStorage.c_str(),
        payloadBytes, static_cast<uint32_t>(payloadSize));
    if (_jaliumOwner != window || !CanReceiveAppleDrag(window) || !window->dragTargetActive ||
        window->dragTargetGeneration != generation || window->dragTargetNativeSequence != sequence) return NO;
    BOOL accepted = (window->dragEffect & EffectsFromOperation(sender.draggingSourceOperationMask)) != JALIUM_DRAG_EFFECT_NONE;
    window->dragTargetActive = false;
    window->dragTargetNativeSequence = 0;
    window->dragTargetPasteboard = nil; window->dragTargetSource = nil;
    window->dragSession = 0;
    return accepted;
}
- (NSDragOperation)draggingSession:(NSDraggingSession*)session sourceOperationMaskForDraggingContext:(NSDraggingContext)context {
    auto source = ActiveAppleDragSource(_jaliumOwner, session);
    (void)context;
    return source && !source->cancelRequested ? OperationFromEffects(source->allowedEffects) : NSDragOperationNone;
}
- (void)draggingSession:(NSDraggingSession*)session movedToPoint:(NSPoint)screenPoint {
    JaliumPlatformWindow* window = _jaliumOwner;
    auto source = ActiveAppleDragSource(window, session);
    if (!source) return;
    (void)QueryAppleDragSource(window, source, NSApp.currentEvent, screenPoint);
    if (_jaliumOwner != window || ActiveAppleDragSource(window, session) != source) return;
    if (source->feedback) source->feedback(source->feedbackEffect, source->userData);
    (void)screenPoint;
}
- (void)draggingSession:(NSDraggingSession*)session endedAtPoint:(NSPoint)screenPoint operation:(NSDragOperation)operation {
    JaliumPlatformWindow* window = _jaliumOwner;
    auto source = ActiveAppleDragSource(window, session);
    if (!source) return;
    source->performedEffect = source->cancelRequested ? JALIUM_DRAG_EFFECT_NONE :
        EffectsFromOperation(operation) & source->allowedEffects;
    source->running = false;
    JaliumPlatformEvent event{}; event.type = JALIUM_EVENT_DRAG_FINISHED;
    event.drag.allowedEffects = source->performedEffect;
    DispatchWindowEvent(window, event);
    (void)screenPoint;
}
- (BOOL)ignoreModifierKeysForDraggingSession:(NSDraggingSession*)session {(void)session;return NO;}
@end

@interface JaliumAppleWindowDelegate : NSObject <NSWindowDelegate>
@property(nonatomic, assign) JaliumPlatformWindow* owner;
@end
@implementation JaliumAppleWindowDelegate
- (BOOL)windowShouldClose:(NSWindow*)sender {
    if (_owner && _owner->enabled) DispatchSimple(_owner,JALIUM_EVENT_CLOSE_REQUESTED);
    return NO;
}
- (void)windowWillClose:(NSNotification*)note {
    if (!_owner) return;
    _owner->nativeClosing = true;
    jalium_window_destroy(_owner);
}
- (void)windowDidResize:(NSNotification*)note {
    if (!_owner) return;
    AlignAppleTitleBarButtons(_owner);
    NSSize content = ((NSView*)_owner->view).bounds.size;
    bool sizeChanged = lround(content.width * _owner->scale) != _owner->width ||
        lround(content.height * _owner->scale) != _owner->height;
    // System Fill / Move & Resize changes the frame without live resizing.
    // Framework setters, style updates, Zoom and fullscreen transitions mark
    // their own changes, so only external sizing disables managed SizeToContent.
    bool userResize = !_owner->applyingState && !_owner->appKitZooming && !_owner->applyingWindowState &&
        !_owner->miniaturizing && !_owner->deminiaturizing &&
        !_owner->fullScreenTransition &&
        (sizeChanged || _owner->window.inLiveResize || _owner->interactiveResize);
    // Zoom is AppKit-driven, but managed SizeToContent must see the state before
    // it sees the new size. The normal frame was saved before entering AppKit.
    if (_owner->appKitZooming && _owner->state == JALIUM_WINDOW_STATE_NORMAL) {
        PublishWindowState(_owner, JALIUM_WINDOW_STATE_MAXIMIZED);
        if (!_owner) return;
    }
    if (userResize &&
        _owner->state == JALIUM_WINDOW_STATE_MAXIMIZED &&
        !NSEqualRects(_owner->window.frame, _owner->maximizedFrame)) {
        _owner->requestedState = JALIUM_WINDOW_STATE_NORMAL;
        PublishWindowState(_owner, JALIUM_WINDOW_STATE_NORMAL);
        if (!_owner) return;
    }
    int previousX = _owner->x, previousY = _owner->y;
    UpdateWindowGeometry(_owner);
    // setFrame: can resize and relocate a window without windowDidMove:.
    // Publish the actual position before size callbacks so managed Left/Top
    // and subsequent moves agree with the restored frame.
    if (_owner->x != previousX || _owner->y != previousY) {
        JaliumPlatformEvent moved{}; moved.type = JALIUM_EVENT_MOVE;
        moved.move.x = _owner->x; moved.move.y = _owner->y;
        DispatchWindowEvent(_owner, moved);
        if (!_owner) return;
    }
    JaliumPlatformEvent e{}; e.type = JALIUM_EVENT_RESIZE;
    e.resize.width = _owner->width; e.resize.height = _owner->height;
    e.resize.isUserInitiated = userResize;
    DispatchWindowEvent(_owner, e);
}
- (void)windowDidMove:(NSNotification*)note {
    if (!_owner) return;
    if (_owner->appKitZooming && _owner->state == JALIUM_WINDOW_STATE_NORMAL) {
        PublishWindowState(_owner, JALIUM_WINDOW_STATE_MAXIMIZED);
        if (!_owner) return;
    }
    UpdateWindowGeometry(_owner);
    JaliumPlatformEvent e{}; e.type = JALIUM_EVENT_MOVE;
    e.move.x = _owner->x; e.move.y = _owner->y;
    DispatchWindowEvent(_owner, e);
}
- (void)windowDidChangeBackingProperties:(NSNotification*)note {
    if (!_owner) return;
    _owner->scale = _owner->window.backingScaleFactor;
    UpdateWindowGeometry(_owner);
    JaliumPlatformEvent e{}; e.type = JALIUM_EVENT_DPI_CHANGED;
    e.dpiChanged.dpiX = e.dpiChanged.dpiY = lround(96 * _owner->scale);
    e.dpiChanged.suggestedX = _owner->x;
    e.dpiChanged.suggestedY = _owner->y;
    e.dpiChanged.suggestedWidth = _owner->width;
    e.dpiChanged.suggestedHeight = _owner->height;
    DispatchWindowEvent(_owner, e);
    [self windowDidResize:note];
}
- (void)windowDidBecomeKey:(NSNotification*)note {DispatchSimple(_owner,JALIUM_EVENT_ACTIVATE);}
- (void)windowDidResignKey:(NSNotification*)note {if(_owner)[_owner->view restoreCursor];DispatchSimple(_owner,JALIUM_EVENT_DEACTIVATE);}
- (void)windowWillMiniaturize:(NSNotification*)note {
    if (!_owner) return;
    if (!_owner->miniaturizing) BeginWindowMinimize(_owner);
    ++_owner->miniaturizationSequence;
    // Stop custom resize tracking at the start of AppKit's animation, before
    // miniaturized and the framework state change at its completion.
    _owner->interactiveResize = false;
}
- (void)windowDidMiniaturize:(NSNotification*)note {
    if (!_owner) return;
    auto* window = _owner;
    NSWindow* nativeWindow = window->window;
    if (!window->miniaturizing && window->state != JALIUM_WINDOW_STATE_MINIMIZED)
        window->beforeMinimize = window->state;
    window->miniaturizing = true;
    PublishWindowState(window, JALIUM_WINDOW_STATE_MINIMIZED);
    if (!IsLiveWindow(window, nativeWindow)) return;
    window->miniaturizing = false;
    if (window->requestedState != window->state || window->requestedNativeZoom ||
        window->pendingActivation || window->pendingMainSelection) {
        __weak JaliumAppleWindow* weakWindow = (JaliumAppleWindow*)nativeWindow;
        dispatch_async(dispatch_get_main_queue(), ^{
            auto* current = weakWindow.jaliumOwner;
            if (current && IsLiveWindow(current, weakWindow)) ApplyQueuedWindowState(current);
        });
    }
}
- (void)windowDidDeminiaturize:(NSNotification*)note {
    if (!_owner) return;
    auto* window = _owner;
    NSWindow* nativeWindow = window->window;
    window->miniaturizing = false;
    if (!window->deminiaturizing) window->requestedState = window->beforeMinimize;
    window->deminiaturizing = true;
    PublishWindowState(window, window->beforeMinimize);
    if (!IsLiveWindow(window, nativeWindow)) return;
    window->deminiaturizing = false;
    if (window->requestedState != window->state || window->requestedNativeZoom ||
        window->pendingActivation || window->pendingMainSelection) {
        __weak JaliumAppleWindow* weakWindow = (JaliumAppleWindow*)window->window;
        dispatch_async(dispatch_get_main_queue(), ^{
            JaliumPlatformWindow* current = weakWindow.jaliumOwner;
            if (current && IsLiveWindow(current) && !current->destroying)
                ApplyQueuedWindowState(current);
        });
    }
}
- (void)windowWillEnterFullScreen:(NSNotification*)note {
    if (!_owner) return;
    if (_owner->state == JALIUM_WINDOW_STATE_NORMAL) CaptureNormalFrame(_owner);
    _owner->beforeFullScreen = _owner->state;
    if (!_owner->fullScreenTransition) {
        _owner->requestedState = JALIUM_WINDOW_STATE_FULLSCREEN;
        ++_owner->stateRequestSequence;
        BeginFullScreenTransition(_owner);
    }
}
- (void)windowWillExitFullScreen:(NSNotification*)note {
    if (!_owner) return;
    if (!_owner->fullScreenTransition) {
        _owner->requestedState = _owner->beforeFullScreen;
        ++_owner->stateRequestSequence;
        BeginFullScreenTransition(_owner);
    }
}
- (void)windowDidEnterFullScreen:(NSNotification*)note {
    if (!_owner) return;
    // Starting the inverse transition inside AppKit's completion notification
    // is ignored. Drain the pending request after AppKit unwinds its transition.
    __weak JaliumAppleView* weakView = _owner->view;
    uint64_t completedRequest = _owner->fullScreenRequestSequence;
    dispatch_async(dispatch_get_main_queue(), ^{
        JaliumPlatformWindow* window = weakView.jaliumOwner;
        if (!IsLiveWindow(window) || !window->fullScreenTransition ||
            window->fullScreenRequestSequence != completedRequest) return;
        bool hadContentFocus = window->fullScreenHadContentFocus;
        window->fullScreenHadContentFocus = false;
        window->fullScreenTransition = false;
        window->applyingWindowState = true;
        PublishWindowState(window, JALIUM_WINDOW_STATE_FULLSCREEN);
        if (weakView.jaliumOwner != window) return;
        RestoreFullScreenContentFocus(window, hadContentFocus);
        if (weakView.jaliumOwner != window) return;
        window->applyingWindowState = false;
        ApplyQueuedWindowState(window);
    });
}
- (void)windowDidExitFullScreen:(NSNotification*)note {
    if (!_owner) return;
    // AppKit restores its frame after notifying delegates. Applying the next
    // state inside the notification captures the fullscreen frame as RestoreBounds.
    __weak JaliumAppleView* weakView = _owner->view;
    uint64_t completedRequest = _owner->fullScreenRequestSequence;
    dispatch_async(dispatch_get_main_queue(), ^{
        JaliumPlatformWindow* window = weakView.jaliumOwner;
        if (!IsLiveWindow(window) || !window->fullScreenTransition ||
            window->fullScreenRequestSequence != completedRequest) return;
        bool hadContentFocus = window->fullScreenHadContentFocus;
        window->fullScreenHadContentFocus = false;
        window->fullScreenTransition = false;
        window->applyingWindowState = true;
        PublishWindowState(window, window->beforeFullScreen);
        if (weakView.jaliumOwner != window) return;
        ApplyAppleStyle(window);
        if (weakView.jaliumOwner != window) return;
        RestoreFullScreenContentFocus(window, hadContentFocus);
        if (weakView.jaliumOwner != window) return;
        window->applyingWindowState = false;
        ApplyQueuedWindowState(window);
    });
}
- (void)windowDidFailToEnterFullScreen:(NSWindow*)window {
    if (!_owner) return;
    [self finishFailedFullScreenTransitionWithState:_owner->state];
}
- (void)windowDidFailToExitFullScreen:(NSWindow*)window {
    if (!_owner) return;
    [self finishFailedFullScreenTransitionWithState:JALIUM_WINDOW_STATE_FULLSCREEN];
}
- (void)finishFailedFullScreenTransitionWithState:(JaliumWindowState)actualState {
    if (!_owner || !_owner->fullScreenTransition) return;
    uint64_t failedRequest = _owner->fullScreenRequestSequence;
    __weak JaliumAppleView* weakView = _owner->view;
    // Keep callbacks and new transitions out of AppKit's failure stack, just
    // as on successful completion. Reject duplicate or retired completions.
    dispatch_async(dispatch_get_main_queue(), ^{
        JaliumPlatformWindow* current = weakView.jaliumOwner;
        if (!IsLiveWindow(current) || !current->fullScreenTransition ||
            current->fullScreenRequestSequence != failedRequest) return;
        // Abandon only the request that actually failed. A later identical
        // request is still newer and gets one attempt of its own.
        if (current->stateRequestSequence == failedRequest) {
            current->requestedState = actualState;
            current->requestedNativeZoom = false;
        }
        bool hadContentFocus = current->fullScreenHadContentFocus;
        current->fullScreenHadContentFocus = false;
        current->fullScreenTransition = false;
        current->applyingWindowState = true;
        current->state = actualState;
        JaliumPlatformEvent e{}; e.type = JALIUM_EVENT_STATE_CHANGED;
        e.stateChanged.newState = actualState; DispatchWindowEvent(current, e);
        if (weakView.jaliumOwner != current || !IsLiveWindow(current)) return;
        RestoreFullScreenContentFocus(current, hadContentFocus);
        if (weakView.jaliumOwner != current || !IsLiveWindow(current)) return;
        current->applyingWindowState = false;
        ApplyQueuedWindowState(current);
    });
}
- (void)windowDidChangeScreen:(NSNotification*)note {
    if (!_owner) return;
    [self windowDidMove:note];
    if (_owner) DispatchSimple(_owner, JALIUM_EVENT_MONITORS_CHANGED);
}
@end

#else

#if TARGET_OS_TV
@interface JaliumAppleView : UIView <UIKeyInput>
#else
@interface JaliumAppleView : UIView <UIKeyInput,UIDropInteractionDelegate>
#endif
@property(nonatomic, assign) JaliumPlatformWindow* jaliumOwner;
@property(nonatomic, strong) UITextInputAssistantItem* jaliumAssistant;
@property(nonatomic) NSInteger lastOrientation;
@end
@implementation JaliumAppleView
+ (Class)layerClass{return [CAMetalLayer class];}
- (instancetype)initWithFrame:(CGRect)frame {self=[super initWithFrame:frame];if(self){_lastOrientation=-1;NSNotificationCenter* center=NSNotificationCenter.defaultCenter;[center addObserver:self selector:@selector(keyboardFrameChanged:) name:UIKeyboardWillChangeFrameNotification object:nil];[center addObserver:self selector:@selector(keyboardHidden:) name:UIKeyboardWillHideNotification object:nil];
#if !TARGET_OS_TV
    [self addInteraction:[[UIDropInteraction alloc]initWithDelegate:self]];
#endif
    }return self;}
- (void)dealloc {[NSNotificationCenter.defaultCenter removeObserver:self];}
- (BOOL)canBecomeFirstResponder{return YES;}
- (BOOL)hasText{return YES;}
- (void)insertText:(NSString*)text {for(NSUInteger i=0;i<text.length;){unichar hi=[text characterAtIndex:i++];uint32_t cp=hi;if(hi>=0xd800&&hi<=0xdbff&&i<text.length){unichar lo=[text characterAtIndex:i];if(lo>=0xdc00&&lo<=0xdfff){++i;cp=0x10000+((hi-0xd800)<<10)+(lo-0xdc00);}}JaliumPlatformEvent e{};e.type=JALIUM_EVENT_CHAR_INPUT;e.character.codepoint=cp;DispatchWindowEvent(_jaliumOwner,e);}}
- (void)deleteBackward {JaliumPlatformEvent e{};e.type=JALIUM_EVENT_KEY_DOWN;e.key.keyCode=0x08;DispatchWindowEvent(_jaliumOwner,e);}
- (void)layoutSubviews {[super layoutSubviews];if(!_jaliumOwner)return;CGFloat scale=self.contentScaleFactor;_jaliumOwner->scale=scale;_jaliumOwner->width=lround(self.bounds.size.width*scale);_jaliumOwner->height=lround(self.bounds.size.height*scale);JaliumPlatformEvent e{};e.type=JALIUM_EVENT_RESIZE;e.resize.width=_jaliumOwner->width;e.resize.height=_jaliumOwner->height;DispatchWindowEvent(_jaliumOwner,e);UIInterfaceOrientation native=self.window.windowScene.interfaceOrientation;NSInteger orientation=(native==UIInterfaceOrientationLandscapeLeft?1:native==UIInterfaceOrientationPortraitUpsideDown?2:native==UIInterfaceOrientationLandscapeRight?3:0);if(orientation!=self.lastOrientation){self.lastOrientation=orientation;JaliumPlatformEvent changed{};changed.type=JALIUM_EVENT_ORIENTATION_CHANGED;changed.orientationChanged.orientation=(int32_t)orientation;DispatchWindowEvent(_jaliumOwner,changed);}}
- (void)safeAreaInsetsDidChange {[super safeAreaInsetsDidChange];if(!_jaliumOwner)return;UIEdgeInsets i=self.safeAreaInsets;CGFloat scale=self.contentScaleFactor;JaliumPlatformEvent e{};e.type=JALIUM_EVENT_SAFE_AREA_CHANGED;e.safeArea.top=i.top*scale;e.safeArea.bottom=i.bottom*scale;e.safeArea.left=i.left*scale;e.safeArea.right=i.right*scale;DispatchWindowEvent(_jaliumOwner,e);}
- (void)keyboardFrameChanged:(NSNotification*)notification {if(!_jaliumOwner||!self.window)return;CGRect screen=[notification.userInfo[UIKeyboardFrameEndUserInfoKey] CGRectValue];CGRect local=[self convertRect:screen fromView:nil];CGRect overlap=CGRectIntersection(self.bounds,local);JaliumPlatformEvent e{};e.type=JALIUM_EVENT_KEYBOARD_CHANGED;e.keyboard.visible=!CGRectIsNull(overlap)&&overlap.size.height>0.5;e.keyboard.heightPx=e.keyboard.visible?(int32_t)lround(overlap.size.height*self.contentScaleFactor):0;DispatchWindowEvent(_jaliumOwner,e);}
- (void)keyboardHidden:(NSNotification*)notification {if(!_jaliumOwner)return;JaliumPlatformEvent e{};e.type=JALIUM_EVENT_KEYBOARD_CHANGED;e.keyboard.visible=0;e.keyboard.heightPx=0;DispatchWindowEvent(_jaliumOwner,e);(void)notification;}
- (void)dispatchTouchSample:(UITouch*)touch type:(JaliumEventType)type flags:(uint32_t)sampleFlags primary:(BOOL)primary {CGPoint p=[touch locationInView:self];JaliumPlatformEvent e{};e.type=type;e.pointer.pointerId=(uint32_t)((uintptr_t)(__bridge void*)touch&0xffffffffu);e.pointer.x=p.x*self.contentScaleFactor;e.pointer.y=p.y*self.contentScaleFactor;e.pointer.pressure=touch.maximumPossibleForce>0?touch.force/touch.maximumPossibleForce:1;e.pointer.pointerType=touch.type==UITouchTypePencil?JALIUM_POINTER_PEN:JALIUM_POINTER_TOUCH;e.pointer.flags=JALIUM_POINTER_FLAG_IN_RANGE|sampleFlags|(primary?JALIUM_POINTER_FLAG_PRIMARY:0)|(type==JALIUM_EVENT_POINTER_UP||type==JALIUM_EVENT_POINTER_CANCEL?0:JALIUM_POINTER_FLAG_IN_CONTACT);e.pointer.toolType=touch.type==UITouchTypePencil?JALIUM_POINTER_TOOL_PENCIL:JALIUM_POINTER_TOOL_UNKNOWN;e.pointer.buttons=type==JALIUM_EVENT_POINTER_UP?0:JALIUM_POINTER_BUTTON_PRIMARY;e.pointer.timestampMillis=(int64_t)llround(touch.timestamp*1000.0);if(touch.type==UITouchTypePencil){CGFloat azimuth=[touch azimuthAngleInView:self];CGFloat tilt=(M_PI_2-touch.altitudeAngle)*180.0/M_PI;e.pointer.tiltX=cos(azimuth)*tilt;e.pointer.tiltY=sin(azimuth)*tilt;e.pointer.twist=azimuth*180.0/M_PI;}DispatchWindowEvent(_jaliumOwner,e);}
- (void)dispatchTouches:(NSSet<UITouch*>*)touches event:(UIEvent*)event type:(JaliumEventType)type {UITouch* primary=event.allTouches.anyObject;for(UITouch* touch in touches){BOOL isPrimary=touch==primary;if(type==JALIUM_EVENT_POINTER_MOVE){NSArray<UITouch*>* coalesced=[event coalescedTouchesForTouch:touch];for(UITouch* sample in coalesced)[self dispatchTouchSample:sample type:type flags:JALIUM_POINTER_FLAG_COALESCED primary:isPrimary];NSArray<UITouch*>* predicted=[event predictedTouchesForTouch:touch];for(UITouch* sample in predicted)[self dispatchTouchSample:sample type:type flags:JALIUM_POINTER_FLAG_PREDICTED primary:isPrimary];if(coalesced.count==0)[self dispatchTouchSample:touch type:type flags:0 primary:isPrimary];}else [self dispatchTouchSample:touch type:type flags:0 primary:isPrimary];}}
- (void)touchesBegan:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t event:e type:JALIUM_EVENT_POINTER_DOWN];}
- (void)touchesMoved:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t event:e type:JALIUM_EVENT_POINTER_MOVE];}
- (void)touchesEnded:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t event:e type:JALIUM_EVENT_POINTER_UP];}
- (void)touchesCancelled:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t event:e type:JALIUM_EVENT_POINTER_CANCEL];}
- (int32_t)virtualKeyForPress:(UIPress*)press {UIKey* key=press.key;if(key){NSString* chars=key.charactersIgnoringModifiers;return VirtualKeyFromCharacter(chars.length?[chars characterAtIndex:0]:0);}switch(press.type){case UIPressTypeUpArrow:return 0x26;case UIPressTypeDownArrow:return 0x28;case UIPressTypeLeftArrow:return 0x25;case UIPressTypeRightArrow:return 0x27;case UIPressTypeSelect:return 0x0d;case UIPressTypeMenu:return 0x1b;case UIPressTypePlayPause:return 0xb3;default:return 0;}}
- (void)dispatchPresses:(NSSet<UIPress*>*)presses type:(JaliumEventType)type {for(UIPress* press in presses){int32_t virtualKey=[self virtualKeyForPress:press];if(!virtualKey)continue;UIKey* key=press.key;JaliumPlatformEvent e{};e.type=type;e.key.keyCode=virtualKey;e.key.scanCode=key?(int32_t)key.keyCode:(int32_t)press.type;e.key.modifiers=key?ModifiersFromFlags(key.modifierFlags):0;DispatchWindowEvent(_jaliumOwner,e);}}
- (void)pressesBegan:(NSSet<UIPress*>*)presses withEvent:(UIPressesEvent*)event {[self dispatchPresses:presses type:JALIUM_EVENT_KEY_DOWN];[super pressesBegan:presses withEvent:event];}
- (void)pressesEnded:(NSSet<UIPress*>*)presses withEvent:(UIPressesEvent*)event {[self dispatchPresses:presses type:JALIUM_EVENT_KEY_UP];[super pressesEnded:presses withEvent:event];}
#if !TARGET_OS_TV
- (std::string)dropTypes:(id<UIDropSession>)session {std::string result;for(UIDragItem* item in session.items)for(NSString* type in item.itemProvider.registeredTypeIdentifiers){const char* utf8=type.UTF8String;if(!utf8)continue;if(!result.empty())result.push_back('\n');result+=utf8;}return result;}
- (void)dispatchDropSession:(id<UIDropSession>)session type:(JaliumEventType)type mime:(const char*)mime data:(const uint8_t*)data size:(uint32_t)size {if(!_jaliumOwner)return;CGPoint point=[session locationInView:self];std::string types=[self dropTypes:session];uint64_t identifier=(uint64_t)(__bridge void*)session;_jaliumOwner->dragSession=identifier;if(type==JALIUM_EVENT_DRAG_ENTER)_jaliumOwner->dragEffect=JALIUM_DRAG_EFFECT_NONE;JaliumPlatformEvent event{};event.type=type;event.drag.x=point.x*self.contentScaleFactor;event.drag.y=point.y*self.contentScaleFactor;event.drag.allowedEffects=JALIUM_DRAG_EFFECT_COPY|JALIUM_DRAG_EFFECT_MOVE;event.drag.sessionId=identifier;event.drag.mimeTypes=types.c_str();event.drag.dataMimeType=mime;event.drag.data=data;event.drag.dataSize=size;DispatchWindowEvent(_jaliumOwner,event);}
- (BOOL)dropInteraction:(UIDropInteraction*)interaction canHandleSession:(id<UIDropSession>)session {(void)interaction;return session.items.count>0;}
- (void)dropInteraction:(UIDropInteraction*)interaction sessionDidEnter:(id<UIDropSession>)session {(void)interaction;[self dispatchDropSession:session type:JALIUM_EVENT_DRAG_ENTER mime:nullptr data:nullptr size:0];}
- (UIDropProposal*)dropInteraction:(UIDropInteraction*)interaction sessionDidUpdate:(id<UIDropSession>)session {(void)interaction;[self dispatchDropSession:session type:JALIUM_EVENT_DRAG_OVER mime:nullptr data:nullptr size:0];UIDropOperation operation=UIDropOperationCancel;if(_jaliumOwner->dragEffect&JALIUM_DRAG_EFFECT_MOVE)operation=UIDropOperationMove;else if(_jaliumOwner->dragEffect&JALIUM_DRAG_EFFECT_COPY)operation=UIDropOperationCopy;return [[UIDropProposal alloc]initWithDropOperation:operation];}
- (void)dropInteraction:(UIDropInteraction*)interaction sessionDidExit:(id<UIDropSession>)session {(void)interaction;[self dispatchDropSession:session type:JALIUM_EVENT_DRAG_LEAVE mime:nullptr data:nullptr size:0];_jaliumOwner->dragSession=0;}
- (void)dropInteraction:(UIDropInteraction*)interaction performDrop:(id<UIDropSession>)session {(void)interaction;UIDragItem* item=session.items.firstObject;NSString* type=item.itemProvider.registeredTypeIdentifiers.firstObject;if(!type){[self dispatchDropSession:session type:JALIUM_EVENT_DROP mime:nullptr data:nullptr size:0];return;}__weak JaliumAppleView* weakSelf=self;[item.itemProvider loadDataRepresentationForTypeIdentifier:type completionHandler:^(NSData* data,NSError*){dispatch_async(dispatch_get_main_queue(),^{JaliumAppleView* strongSelf=weakSelf;if(!strongSelf)return;[strongSelf dispatchDropSession:session type:JALIUM_EVENT_DROP mime:type.UTF8String data:(const uint8_t*)data.bytes size:(uint32_t)data.length];strongSelf.jaliumOwner->dragSession=0;});}];}
#endif
@end

#endif

JaliumResult jalium_platform_init_impl()
{
#if TARGET_OS_OSX
    [NSApplication sharedApplication];
    ObserveNativeMenuTracking();
#endif
    g_quit.store(false);return JALIUM_OK;
}
void jalium_platform_shutdown_impl()
{
#if TARGET_OS_OSX
    auto* notifications = NSNotificationCenter.defaultCenter;
    if (g_menuTrackingBeginObserver) [notifications removeObserver:g_menuTrackingBeginObserver];
    if (g_menuTrackingEndObserver) [notifications removeObserver:g_menuTrackingEndObserver];
    g_menuTrackingBeginObserver = nil;
    g_menuTrackingEndObserver = nil;
    g_trackingMenus = nil;
#endif
}
JaliumPlatform jalium_platform_get_current_impl()
{
#if TARGET_OS_OSX
    return JALIUM_PLATFORM_MACOS;
#elif TARGET_OS_TV
    return JALIUM_PLATFORM_TVOS;
#elif TARGET_OS_VISION
    return JALIUM_PLATFORM_VISIONOS;
#else
    return JALIUM_PLATFORM_IOS;
#endif
}

int32_t jalium_platform_prefers_overlay_scrollbars(void)
{
#if TARGET_OS_OSX
    return NSScroller.preferredScrollerStyle == NSScrollerStyleOverlay;
#else
    return 1;
#endif
}

int32_t jalium_platform_prefers_reduced_motion(void)
{
#if TARGET_OS_OSX
    return NSWorkspace.sharedWorkspace.accessibilityDisplayShouldReduceMotion;
#else
    return UIAccessibilityIsReduceMotionEnabled();
#endif
}

#if TARGET_OS_OSX
namespace {
struct WordNavigationContext {
    NSString* text;
    NSAttributedString* attributed;
    NSTextContentStorage* content;
    NSTextLayoutManager* layout;

    explicit WordNavigationContext(NSString* value) : text(value) {
        attributed = [[NSAttributedString alloc] initWithString:value];
        content = [[NSTextContentStorage alloc] init];
        layout = [[NSTextLayoutManager alloc] init];
        [content addTextLayoutManager:layout];
        // Word navigation needs the logical paragraph's bidi order; it does
        // not use this text container for managed visual-line navigation.
        layout.textContainer = [[NSTextContainer alloc] initWithSize:NSMakeSize(1000000, 1000000)];
        content.attributedString = attributed;
    }
};

NSUInteger SnapWordCluster(NSString* text, NSUInteger index, bool forward) {
    if (index == 0 || index >= text.length) return MIN(index, text.length);
    NSRange cluster = [text rangeOfComposedCharacterSequenceAtIndex:index];
    return index == cluster.location ? index : forward ? NSMaxRange(cluster) : cluster.location;
}

// TextKit objects stay on their creating thread. Keep only one small current
// document per thread; large/transient documents are released after the query.
WordNavigationContext* GetWordContext(NSString* text, std::unique_ptr<WordNavigationContext>& transient) {
    static thread_local std::unique_ptr<WordNavigationContext> current;
    if (text.length > 65536) {
        transient = std::make_unique<WordNavigationContext>(text);
        return transient.get();
    }
    if (!current || ![current->text isEqualToString:text])
        current = std::make_unique<WordNavigationContext>(text);
    return current.get();
}
}
#endif

int32_t jalium_platform_text_word_boundary(
    const JaliumUtf16Char* characters, uint32_t length, int32_t index, int32_t direction)
{
#if TARGET_OS_OSX
    if ((!characters && length) || length > INT32_MAX || index < 0 ||
        static_cast<uint32_t>(index) > length || direction < 0 || direction > 3) return -1;
    if (!length) return 0;
    @autoreleasepool { @try {
        NSString* text = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(characters) length:length];
        if (!text) return -1;
        NSUInteger origin = SnapWordCluster(text, index, direction == 0);
        std::unique_ptr<WordNavigationContext> transient;
        auto* context = GetWordContext(text, transient);
        auto* content = context->content;
        id<NSTextLocation> location = [content locationFromLocation:content.documentRange.location withOffset:origin];
        if (!location) return -1;
        NSTextSelection* selection = [[NSTextSelection alloc] initWithLocation:location affinity:NSTextSelectionAffinityDownstream];
        auto nativeDirection = static_cast<NSTextSelectionNavigationDirection>(direction);
        NSTextSelection* destination = [context->layout.textSelectionNavigation destinationSelectionForTextSelection:selection
            direction:nativeDirection destination:NSTextSelectionNavigationDestinationWord extending:NO confined:NO];
        if (!destination || !destination.textRanges.count) return static_cast<int32_t>(origin);
        NSInteger offset = [content offsetFromLocation:content.documentRange.location
            toLocation:destination.textRanges.firstObject.location];
        if (offset < 0 || offset > length) return -1;
        return static_cast<int32_t>(SnapWordCluster(text, offset, offset >= origin));
    } @catch (NSException*) { return -1; } }
#else
    (void)characters; (void)length; (void)index; (void)direction;
    return -1;
#endif
}

int32_t jalium_platform_text_word_selection_boundary(
    const JaliumUtf16Char* characters, uint32_t length, int32_t start, int32_t rangeLength, int32_t direction)
{
#if TARGET_OS_OSX
    if ((!characters && length) || length > INT32_MAX || start < 0 || rangeLength < 0 ||
        static_cast<uint32_t>(start) > length || static_cast<uint32_t>(rangeLength) > length - start ||
        direction < 0 || direction > 3) return -1;
    if (!rangeLength) return jalium_platform_text_word_boundary(characters, length, start, direction);
    @autoreleasepool { @try {
        NSString* text = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(characters) length:length];
        if (!text) return -1;
        NSUInteger first = SnapWordCluster(text, start, false);
        NSUInteger end = SnapWordCluster(text, start + rangeLength, true);
        std::unique_ptr<WordNavigationContext> transient;
        auto* context = GetWordContext(text, transient);
        auto* content = context->content;
        auto firstLocation = [content locationFromLocation:content.documentRange.location withOffset:first];
        auto endLocation = [content locationFromLocation:content.documentRange.location withOffset:end];
        if (!firstLocation || !endLocation) return -1;
        NSTextRange* range = [[NSTextRange alloc] initWithLocation:firstLocation endLocation:endLocation];
        NSTextSelection* selection = [[NSTextSelection alloc] initWithRange:range affinity:NSTextSelectionAffinityDownstream
            granularity:NSTextSelectionGranularityCharacter];
        NSTextSelection* destination = [context->layout.textSelectionNavigation destinationSelectionForTextSelection:selection
            direction:static_cast<NSTextSelectionNavigationDirection>(direction)
            destination:NSTextSelectionNavigationDestinationWord extending:NO confined:NO];
        if (!destination || !destination.textRanges.count) return -1;
        NSInteger offset = [content offsetFromLocation:content.documentRange.location
            toLocation:destination.textRanges.firstObject.location];
        if (offset < 0 || offset > length) return -1;
        return static_cast<int32_t>(SnapWordCluster(text, offset, offset >= first));
    } @catch (NSException*) { return -1; } }
#else
    (void)characters; (void)length; (void)start; (void)rangeLength; (void)direction;
    return -1;
#endif
}

JaliumResult jalium_platform_text_word_range(
    const JaliumUtf16Char* characters, uint32_t length, int32_t index, int32_t* start, int32_t* rangeLength)
{
    if (start) *start = 0;
    if (rangeLength) *rangeLength = 0;
#if TARGET_OS_OSX
    if (!start || !rangeLength || (!characters && length) || length > INT32_MAX || index < 0 ||
        static_cast<uint32_t>(index) > length) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (!length) return JALIUM_OK;
    @autoreleasepool { @try {
        NSString* text = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(characters) length:length];
        if (!text) return JALIUM_ERROR_INVALID_ARGUMENT;
        NSUInteger origin = SnapWordCluster(text, MIN(static_cast<NSUInteger>(index), text.length - 1), false);
        std::unique_ptr<WordNavigationContext> transient;
        auto* context = GetWordContext(text, transient);
        // AppKit's mouse selection deliberately uses different linguistic
        // units from TextKit keyboard movement (e.g. Japanese and Thai).
        NSRange word = [context->attributed doubleClickAtIndex:origin];
        if (word.location == NSNotFound || word.location > text.length || word.length > text.length - word.location)
            return JALIUM_ERROR_INVALID_ARGUMENT;
        NSUInteger first = SnapWordCluster(text, word.location, false);
        NSUInteger end = SnapWordCluster(text, NSMaxRange(word), true);
        *start = static_cast<int32_t>(first);
        *rangeLength = static_cast<int32_t>(end - first);
        return JALIUM_OK;
    } @catch (NSException*) { return JALIUM_ERROR_INVALID_ARGUMENT; } }
#else
    (void)characters; (void)length; (void)index;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

void jalium_apple_set_root_view(intptr_t nativeView)
{g_rootView=nativeView?(__bridge id)(void*)nativeView:nil;}
void jalium_apple_register_scene_root(const char* sceneId,intptr_t nativeView)
{
#if TARGET_OS_OSX
    (void)sceneId;(void)nativeView;
#else
    if(!sceneId||!*sceneId||!nativeView)return;UIView* view=(__bridge UIView*)(void*)nativeView;
    std::scoped_lock lock(g_windowsMutex);auto it=std::find_if(g_sceneRoots.begin(),g_sceneRoots.end(),
        [&](const SceneRoot& root){return root.id==sceneId;});
    if(it==g_sceneRoots.end())g_sceneRoots.push_back({sceneId,view,0});else it->view=view;
    g_rootView=view;
#endif
}
void jalium_apple_unregister_scene_root(const char* sceneId)
{
#if TARGET_OS_OSX
    (void)sceneId;
#else
    if(!sceneId)return;std::vector<JaliumPlatformWindow*> affected;
    {std::scoped_lock lock(g_windowsMutex);auto it=std::find_if(g_sceneRoots.begin(),g_sceneRoots.end(),
        [&](const SceneRoot& root){return root.id==sceneId;});if(it==g_sceneRoots.end())return;
        UIView* view=it->view;for(auto* window:g_windows)if(window->sceneRoot==view)affected.push_back(window);
        g_sceneRoots.erase(it);}
    for(auto* window:affected)DispatchSimple(window,JALIUM_EVENT_CLOSE_REQUESTED);
#endif
}
void jalium_apple_notify_lifecycle(int32_t eventType)
{
    JaliumEventType type=(JaliumEventType)eventType;
    if(type!=JALIUM_EVENT_APP_PAUSE&&type!=JALIUM_EVENT_APP_RESUME&&
       type!=JALIUM_EVENT_APP_DESTROY&&type!=JALIUM_EVENT_LOW_MEMORY)return;
    std::vector<JaliumPlatformWindow*> copy;{std::scoped_lock lock(g_windowsMutex);copy=g_windows;}
    for(auto* window:copy)DispatchSimple(window,type);
}

JaliumPlatformWindow* jalium_window_create(const JaliumWindowParams* params)
{
    if(!params||params->width<=0||params->height<=0)return nullptr;
    auto result=std::make_unique<JaliumPlatformWindow>();
    result->width=params->width;result->height=params->height;
    result->x=params->x;result->y=params->y;result->style=params->style;
#if TARGET_OS_OSX
    NSUInteger mask = AppleStyleMask(params->style);
    CGFloat scale = NSScreen.mainScreen.backingScaleFactor ?: 1.0;
    NSRect rect=NSMakeRect(0, 0, params->width / scale, params->height / scale);
    result->window=[[JaliumAppleWindow alloc]initWithContentRect:rect styleMask:mask
        backing:NSBackingStoreBuffered defer:NO];
    if(!result->window)return nullptr;
    ((JaliumAppleWindow*)result->window).jaliumOwner = result.get();
    result->window.releasedWhenClosed=NO;
    [result->window center];
    CGFloat desktopTop = NSMaxY(NSScreen.screens.firstObject.frame);
    NSRect frame = result->window.frame;
    NSPoint topLeft = NSMakePoint(
        params->x == JALIUM_DEFAULT_POS ? frame.origin.x : params->x / scale,
        params->y == JALIUM_DEFAULT_POS ? NSMaxY(frame) : desktopTop - params->y / scale);
    [result->window setFrameTopLeftPoint:topLeft];
    result->view=[[JaliumAppleView alloc]initWithFrame:NSMakeRect(0,0,rect.size.width,rect.size.height)];
    result->view.autoresizingMask=NSViewWidthSizable|NSViewHeightSizable;
    result->view.jaliumOwner=result.get();
    // Keep AppKit's material and the Metal surface as siblings: a visual effect
    // inside the rendering view would be composited above its drawable contents.
    NSView* content = [[NSView alloc] initWithFrame:result->view.frame];
    [content addSubview:result->view];
    result->window.contentView=content;
    result->delegate=[JaliumAppleWindowDelegate new];result->delegate.owner=result.get();
    result->window.delegate=result->delegate;result->window.title=StringFromUtf16(params->title);
    result->window.acceptsMouseMovedEvents=YES;
    result->scale=result->window.backingScaleFactor;
    frame=result->window.frame;
    result->x=lround(frame.origin.x*result->scale);
    result->y=lround((desktopTop-NSMaxY(frame))*result->scale);
    ApplyAppleStyle(result.get());
    UpdateWindowGeometry(result.get());
    if (params->parentHandle) jalium_window_set_owner(result.get(), params->parentHandle);
#else
    UIView* root=AcquireRootView();if(!root)return nullptr;
    result->sceneRoot=root;
    result->view=[[JaliumAppleView alloc]initWithFrame:root.bounds];
    result->view.autoresizingMask=UIViewAutoresizingFlexibleWidth|UIViewAutoresizingFlexibleHeight;
    result->view.jaliumOwner=result.get();result->view.contentScaleFactor=UIScreen.mainScreen.scale;
    result->scale=result->view.contentScaleFactor;[root addSubview:result->view];
    result->width=lround(root.bounds.size.width*result->scale);
    result->height=lround(root.bounds.size.height*result->scale);
#endif
    {std::scoped_lock lock(g_windowsMutex);g_windows.push_back(result.get());}
    return result.release();
}

void jalium_window_destroy(JaliumPlatformWindow* window)
{
    if(!window || window->destroying)return;
    window->destroying = true;
    #if TARGET_OS_OSX
    CancelAppleDrag(window);
    [window->systemMenu cancelTrackingWithoutAnimation];
    [window->view.accessibilityBridge detach];
    window->view.accessibilityBridge = nil;
    RefreshWindowMenuItem(window);
    #endif
    {std::scoped_lock lock(g_windowsMutex);std::erase(g_windows,window);}
    window->view.jaliumOwner=nullptr;
#if TARGET_OS_OSX
    [window->view restoreCursor];
    ((JaliumAppleWindow*)window->window).jaliumOwner = nullptr;
    [window->window.parentWindow removeChildWindow:window->window];
    for (NSWindow* child in window->window.childWindows.copy)
        [window->window removeChildWindow:child];
    window->delegate.owner=nullptr;
    window->window.delegate=nil;[window->window orderOut:nil];
    if (!window->nativeClosing) [window->window close];
#else
    ReleaseRootView(window->sceneRoot);
    [window->view removeFromSuperview];
#endif
    DispatchSimple(window,JALIUM_EVENT_DESTROYED);delete window;
}
void jalium_window_show(JaliumPlatformWindow* w){if(!w)return;w->visible=true;
#if TARGET_OS_OSX
    jalium_apple_window_show(w, (w->style & JALIUM_WINDOW_STYLE_POPUP) == 0);
#else
    w->view.hidden=NO;[w->view becomeFirstResponder];
#endif
}
void jalium_apple_window_show(JaliumPlatformWindow* w, int32_t activate)
{
    if (!w) return;
    w->visible = true;
#if TARGET_OS_OSX
    NSWindow* nativeWindow = w->window;
    RefreshWindowMenuItem(w);
    // orderFront may constrain the first displayed frame to the screen's
    // available area. This is part of Show, and must not clear a maximized
    // state requested before showing or replace its saved normal frame.
    bool wasApplyingState = w->applyingState;
    w->applyingState = true;
    if (w->miniaturizing || w->deminiaturizing) {
        if (activate && w->enabled) w->pendingActivation = true;
        w->applyingState = wasApplyingState;
        return;
    }
    if (activate && w->enabled) {
        [nativeWindow makeKeyAndOrderFront:nil];
        if (IsLiveWindow(w, nativeWindow)) [nativeWindow makeFirstResponder:w->view];
    } else {
        [nativeWindow orderFront:nil];
    }
    if (IsLiveWindow(w, nativeWindow)) {
        w->applyingState = wasApplyingState;
        if (w->state == JALIUM_WINDOW_STATE_MAXIMIZED)
            w->maximizedFrame = w->window.frame;
        ApplyQueuedWindowState(w);
    }
#else
    w->view.hidden = NO;
    if (activate && w->enabled) [w->view becomeFirstResponder];
#endif
}

int32_t jalium_apple_window_set_style(JaliumPlatformWindow* w, uint32_t style)
{
    if (!w) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    w->style = style;
    ApplyAppleStyle(w);
    return JALIUM_OK;
#else
    (void)style;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_apple_window_set_editing_command_query(
    JaliumPlatformWindow* w, JaliumEditingCommandQuery query, void* userData)
{
    if (!w) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if (!NSThread.isMainThread || !IsLiveWindow(w)) return JALIUM_ERROR_INVALID_STATE;
    w->editingCommandQuery = query;
    w->editingCommandUserData = userData;
    return JALIUM_OK;
#else
    (void)query; (void)userData;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_apple_window_set_titlebar_extended(JaliumPlatformWindow* w, int32_t extended)
{
    if (!w || (extended != 0 && extended != 1)) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    bool requested = extended != 0;
    if (w->extendedTitleBar == requested) return JALIUM_OK;
    w->extendedTitleBar = requested;
    // ApplyAppleStyle preserves the client size, top-left position and focus.
    // During fullscreen it defers frame changes until AppKit completes exit.
    ApplyAppleStyle(w);
    return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

int32_t jalium_apple_window_set_titlebar_content_height(JaliumPlatformWindow* w, double height)
{
    if (!w || !std::isfinite(height) || height <= 0) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    w->titleBarContentHeight = height;
    [w->window layoutIfNeeded];
    return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

int32_t jalium_apple_window_get_restore_bounds(JaliumPlatformWindow* w,
    int32_t* x, int32_t* y, int32_t* width, int32_t* height)
{
    if (!w || !x || !y || !width || !height) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    bool currentFrame = w->state == JALIUM_WINDOW_STATE_NORMAL &&
        !w->fullScreenTransition && !w->applyingWindowState && !w->miniaturizing && !w->deminiaturizing;
    if (!currentFrame && !w->hasRestoreFrame) return JALIUM_ERROR_INVALID_STATE;
    NSRect frame = currentFrame ? w->window.frame : w->restoreFrame;
    NSSize content = currentFrame ? ((NSView*)w->view).bounds.size : w->restoreContentSize;
    NSPoint origin = FrameworkScreenPoint(NSMakePoint(NSMinX(frame), NSMaxY(frame)), w->window.screen);
    *x = lround(origin.x);
    *y = lround(origin.y);
    *width = lround(content.width * w->scale);
    *height = lround(content.height * w->scale);
    return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_apple_window_get_client_origin(JaliumPlatformWindow* w, int32_t* x, int32_t* y)
{
    if (!w || !x || !y) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if (!NSThread.isMainThread || !IsLiveWindow(w)) return JALIUM_ERROR_INVALID_STATE;
    NSPoint point = [w->window convertPointToScreen:[w->view convertPoint:NSZeroPoint toView:nil]];
    NSPoint converted = FrameworkScreenPoint(point, w->window.screen);
    *x = lround(converted.x);
    *y = lround(converted.y);
    return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

#if TARGET_OS_OSX
static NSScreen* StartupScreenAtPoint(NSPoint point)
{
    NSArray<NSScreen*>* screens = NSScreen.screens;
    std::vector<NSRect> frames;
    for (NSScreen* screen in screens)
        frames.push_back(NSIsEmptyRect(screen.visibleFrame) ? NSZeroRect : screen.frame);
    size_t index = jalium::platform::apple::NearestStartupScreen(point, frames);
    return index < screens.count ? screens[index] : nil;
}
#endif

int32_t jalium_apple_window_apply_startup_location(JaliumPlatformWindow* w, int32_t location, intptr_t owner)
{
    if (!w || location < 0 || location > 2) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if (!NSThread.isMainThread || !IsLiveWindow(w)) return JALIUM_ERROR_INVALID_STATE;
    if (location == 0 || (location == 2 && !owner)) return JALIUM_OK;
    NSWindow* nativeWindow = w->window;
    NSWindow* ownerWindow = nil;
    if (owner) {
        id object = (__bridge id)(void*)owner;
        if ([object isKindOfClass:NSWindow.class]) ownerWindow = object;
        else if ([object isKindOfClass:NSView.class]) ownerWindow = [(NSView*)object window];
        if (!ownerWindow || ownerWindow == nativeWindow) return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    auto* ownerState = [ownerWindow isKindOfClass:JaliumAppleWindow.class]
        ? ((JaliumAppleWindow*)ownerWindow).jaliumOwner : nullptr;
    if ([ownerWindow isKindOfClass:JaliumAppleWindow.class] &&
        (!ownerState || !IsLiveWindow(ownerState, ownerWindow))) return JALIUM_ERROR_INVALID_STATE;
    NSScreen* screen = ownerWindow.screen;
    if (!screen && ownerWindow) {
        NSRect frame = ownerWindow.frame;
        screen = StartupScreenAtPoint(NSMakePoint(NSMidX(frame), NSMidY(frame)));
    }
    if (!screen) screen = StartupScreenAtPoint(NSEvent.mouseLocation);
    if (!screen || NSIsEmptyRect(screen.visibleFrame)) return JALIUM_ERROR_INVALID_STATE;

    bool useRestore = w->state != JALIUM_WINDOW_STATE_NORMAL || w->fullScreenTransition || w->applyingWindowState;
    if (useRestore && !w->hasRestoreFrame) return JALIUM_ERROR_INVALID_STATE;
    NSRect frame = useRestore ? w->restoreFrame : nativeWindow.frame;
    NSRect work = screen.visibleFrame;
    NSRect reference = work;
    bool normalOwner = ownerWindow && !ownerWindow.miniaturized &&
        !(ownerWindow.styleMask & NSWindowStyleMaskFullScreen) &&
        (ownerState ? ownerState->state == JALIUM_WINDOW_STATE_NORMAL : !ownerWindow.zoomed);
    if (location == 2 && normalOwner) reference = ownerWindow.frame;
    // Keep the entire frame accessible when it fits; oversized frames retain
    // an accessible top-left edge instead of placing chrome off screen.
    frame = jalium::platform::apple::CenterStartupFrame(frame, reference, work);
    if (useRestore) {
        w->restoreFrame = frame;
        if (w->state != JALIUM_WINDOW_STATE_MAXIMIZED || w->fullScreenTransition || w->applyingWindowState)
            return JALIUM_OK;
        frame = MaximizedFrameForScreen(nativeWindow, screen);
    }
    bool wasApplyingState = w->applyingState;
    w->applyingState = true;
    [nativeWindow setFrame:frame display:NO];
    if (!IsLiveWindow(w, nativeWindow)) return JALIUM_ERROR_INVALID_STATE;
    w->applyingState = wasApplyingState;
    if (useRestore) w->maximizedFrame = nativeWindow.frame;
    UpdateWindowGeometry(w);
    return JALIUM_OK;
#else
    (void)owner;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

int32_t jalium_apple_window_titlebar_double_click(JaliumPlatformWindow* w)
{
    if (!w) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if (!w->enabled) return JALIUM_ERROR_INVALID_STATE;
    if (w->fullScreenTransition || w->appKitZooming || w->window.miniaturized ||
        (w->window.styleMask & NSWindowStyleMaskFullScreen)) return JALIUM_OK;
    // Read on each gesture so changing Desktop & Dock preferences takes effect
    // without restarting the application. Never write the user's preference.
    id action = [NSUserDefaults.standardUserDefaults objectForKey:@"AppleActionOnDoubleClick"];
    if (action && ![action isKindOfClass:NSString.class]) return JALIUM_OK;
    if ([action isEqualToString:@"Minimize"]) {
        if (w->style & JALIUM_WINDOW_STYLE_MINIMIZABLE)
            ApplyWindowState(w, JALIUM_WINDOW_STATE_MINIMIZED);
    } else if ([action isEqualToString:@"Fill"]) {
        if (w->style & JALIUM_WINDOW_STYLE_MAXIMIZABLE)
            ApplyWindowState(w, w->state == JALIUM_WINDOW_STATE_MAXIMIZED
                ? JALIUM_WINDOW_STATE_NORMAL : JALIUM_WINDOW_STATE_MAXIMIZED);
    } else if (!action || [action isEqualToString:@"Maximize"]) {
        [w->window zoom:nil];
    }
    // "None" and unknown future settings deliberately do not resize the window.
    return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_apple_window_set_system_backdrop(JaliumPlatformWindow* w, int32_t backdrop)
{
    if (!w || backdrop < 0 || backdrop > 4) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if (backdrop == 0) {
        [w->backdropView removeFromSuperview];
        w->backdropView = nil;
    } else {
        if (!w->backdropView) {
            w->backdropView = [[NSVisualEffectView alloc] initWithFrame:w->window.contentView.bounds];
            w->backdropView.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
            w->backdropView.blendingMode = NSVisualEffectBlendingModeBehindWindow;
            w->backdropView.state = NSVisualEffectStateFollowsWindowActiveState;
            w->backdropView.accessibilityElement = NO;
            [w->window.contentView addSubview:w->backdropView positioned:NSWindowBelow
                relativeTo:(NSView*)w->view];
        }
        switch (backdrop) {
            case 3: w->backdropView.material = NSVisualEffectMaterialPopover; break;
            case 4: w->backdropView.material = NSVisualEffectMaterialUnderWindowBackground; break;
            default: w->backdropView.material = NSVisualEffectMaterialWindowBackground; break;
        }
    }
    ApplyAppleSurfaceAppearance(w);
    return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
void jalium_window_hide(JaliumPlatformWindow* w){if(!w)return;w->visible=false;
#if TARGET_OS_OSX
    CancelAppleDrag(w);
    NSWindow* nativeWindow = w->window;
    CancelPendingWindowSelection(w);
    [w->systemMenu cancelTrackingWithoutAnimation];
    if (!IsLiveWindow(w, nativeWindow)) return;
    RefreshWindowMenuItem(w);
    [w->view restoreCursor];
    [w->window orderOut:nil];
#else
    w->view.hidden=YES;
#endif
}
void jalium_window_set_title(JaliumPlatformWindow* w,const JaliumUtf16Char* title){if(!w)return;
#if TARGET_OS_OSX
    w->window.title=StringFromUtf16(title);
    RefreshWindowMenuItem(w);
#else
    (void)title;
#endif
}
void jalium_window_resize(JaliumPlatformWindow* w,int32_t width,int32_t height){if(!w||width<=0||height<=0)return;
#if TARGET_OS_OSX
    NSWindow* nativeWindow = w->window;
    bool wasApplyingState = w->applyingState;
    w->applyingState = true;
    [nativeWindow setContentSize:NSMakeSize(width/w->scale,height/w->scale)];
    if (IsLiveWindow(w, nativeWindow)) w->applyingState = wasApplyingState;
#else
    CGRect f=w->view.frame;f.size=CGSizeMake(width/w->scale,height/w->scale);w->view.frame=f;
#endif
}
void jalium_window_move(JaliumPlatformWindow* w,int32_t x,int32_t y){if(!w)return;w->x=x;w->y=y;
#if TARGET_OS_OSX
    NSWindow* nativeWindow = w->window;
    CGFloat desktopTop=NSMaxY(NSScreen.screens.firstObject.frame);
    bool wasApplyingState = w->applyingState;
    w->applyingState = true;
    NSScreen* destination = nativeWindow.screen ?: NSScreen.screens.firstObject;
    for (NSScreen* screen in NSScreen.screens) {
        NSPoint origin = FrameworkScreenPoint(NSMakePoint(NSMinX(screen.frame), NSMaxY(screen.frame)), screen);
        NSRect bounds = NSMakeRect(origin.x, origin.y,
            screen.frame.size.width * screen.backingScaleFactor, screen.frame.size.height * screen.backingScaleFactor);
        if (NSPointInRect(NSMakePoint(x, y), bounds)) { destination = screen; break; }
    }
    double primaryScale = NSScreen.screens.firstObject.backingScaleFactor ?: 1.0;
    using jalium::platform::apple::ScreenPoint;
    [nativeWindow setFrameTopLeftPoint:NSMakePoint(
        ScreenPoint(x, NSMinX(destination.frame), primaryScale, destination.backingScaleFactor),
        desktopTop - ScreenPoint(y, desktopTop - NSMaxY(destination.frame), primaryScale, destination.backingScaleFactor))];
    if (IsLiveWindow(w, nativeWindow)) w->applyingState = wasApplyingState;
#endif
}
void jalium_window_set_state(JaliumPlatformWindow* w,JaliumWindowState state){if(!w)return;
#if TARGET_OS_OSX
    if (state < JALIUM_WINDOW_STATE_NORMAL || state > JALIUM_WINDOW_STATE_FULLSCREEN) return;
    ApplyWindowState(w, state);
#else
    w->state=state;JaliumPlatformEvent e{};e.type=JALIUM_EVENT_STATE_CHANGED;e.stateChanged.newState=state;DispatchWindowEvent(w,e);
#endif
}
JaliumWindowState jalium_window_get_state(JaliumPlatformWindow* w){return w?w->state:JALIUM_WINDOW_STATE_NORMAL;}
intptr_t jalium_window_get_native_handle(JaliumPlatformWindow* w){return w?(intptr_t)(__bridge void*)w->view:0;}
JaliumSurfaceDescriptor jalium_window_get_surface(JaliumPlatformWindow* w){JaliumSurfaceDescriptor d{};if(!w)return d;d.platform=jalium_platform_get_current_impl();bool composition=(w->style&JALIUM_WINDOW_STYLE_TRANSPARENT)!=0;
#if TARGET_OS_OSX
    composition = composition || w->backdropView != nil;
#endif
    d.kind=composition?JALIUM_SURFACE_KIND_COMPOSITION_TARGET:JALIUM_SURFACE_KIND_NATIVE_WINDOW;d.handle0=jalium_window_get_native_handle(w);return d;}
uint32_t jalium_window_get_portal_parent_handle(JaliumPlatformWindow*,char*,uint32_t){return 0;}
uint32_t jalium_window_get_portal_parent_handle_for_native_handle(intptr_t,char*,uint32_t){return 0;}
int32_t jalium_wayland_surface_is_ready(intptr_t surface){return surface?1:0;}
void jalium_window_set_event_callback(JaliumPlatformWindow* w,JaliumEventCallback cb,void* data){if(w){w->callback=cb;w->userData=data;}}
void jalium_window_invalidate(JaliumPlatformWindow* w){if(!w)return;
#if TARGET_OS_OSX
    [w->view setNeedsDisplay:YES];
#else
    [w->view setNeedsDisplay];
#endif
    DispatchSimple(w,JALIUM_EVENT_PAINT);
}
void jalium_window_set_cursor(JaliumPlatformWindow* window,JaliumCursorShape cursor){
#if TARGET_OS_OSX
    if (!window) return;
    if (window->view.cursorShape != cursor) {
        window->view.cursorShape = cursor;
        window->view.shapeCursor = CursorForShape(cursor);
    }
    [window->view applyCursor];
#else
    (void)window;(void)cursor;
#endif
}
void jalium_window_get_client_size(JaliumPlatformWindow* w,int32_t* width,int32_t* height){if(width)*width=w?w->width:0;if(height)*height=w?w->height:0;}
void jalium_window_get_position(JaliumPlatformWindow* w,int32_t* x,int32_t* y){if(x)*x=w?w->x:0;if(y)*y=w?w->y:0;}

int32_t jalium_platform_run_message_loop(void){
#if TARGET_OS_OSX
    g_quit.store(false);[NSApp run];return g_exitCode.load();
#else
    return JALIUM_ERROR_INVALID_STATE;
#endif
}
int32_t jalium_platform_poll_events(void){
#if TARGET_OS_OSX
    int count=0;for(;;){NSEvent* e=[NSApp nextEventMatchingMask:NSEventMaskAny untilDate:NSDate.date inMode:NSDefaultRunLoopMode dequeue:YES];if(!e)break;[NSApp sendEvent:e];++count;}return count;
#else
    return CFRunLoopRunInMode(kCFRunLoopDefaultMode,0,true)==kCFRunLoopRunHandledSource?1:0;
#endif
}
void jalium_platform_quit(int32_t code){g_exitCode.store(code);g_quit.store(true);
#if TARGET_OS_OSX
    dispatch_async(dispatch_get_main_queue(),^{[NSApp stop:nil];NSEvent* e=[NSEvent otherEventWithType:NSEventTypeApplicationDefined location:NSZeroPoint modifierFlags:0 timestamp:0 windowNumber:0 context:nil subtype:0 data1:0 data2:0];[NSApp postEvent:e atStart:NO];});
#endif
}

JaliumResult jalium_dispatcher_create(JaliumDispatcher** out){if(!out)return JALIUM_ERROR_INVALID_ARGUMENT;*out=new JaliumDispatcher();return JALIUM_OK;}
void jalium_dispatcher_destroy(JaliumDispatcher* d){if(!d)return;d->alive.store(false);ReleaseDispatcher(d);}
void jalium_dispatcher_set_callback(JaliumDispatcher* d,JaliumDispatcherCallback cb,void* data){if(!d)return;std::scoped_lock lock(d->mutex);d->callback=cb;d->userData=data;}
void jalium_dispatcher_wake(JaliumDispatcher* d){if(!d||!d->alive.load()||d->queued.exchange(true))return;RetainDispatcher(d);dispatch_async(dispatch_get_main_queue(),^{if(d->alive.load()){d->queued.store(false);JaliumDispatcherCallback cb=nullptr;void* data=nullptr;{std::scoped_lock lock(d->mutex);cb=d->callback;data=d->userData;}if(cb)cb(data);}ReleaseDispatcher(d);});}

JaliumResult jalium_timer_create(JaliumTimer** out){if(!out)return JALIUM_ERROR_INVALID_ARGUMENT;auto* t=new JaliumTimer();t->fired=dispatch_semaphore_create(0);t->source=dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER,0,0,dispatch_get_global_queue(QOS_CLASS_USER_INTERACTIVE,0));if(!t->source){delete t;return JALIUM_ERROR_RESOURCE_CREATION_FAILED;}dispatch_source_set_event_handler(t->source,^{FireTimer(t);});dispatch_source_set_cancel_handler(t->source,^{delete t;});dispatch_resume(t->source);*out=t;return JALIUM_OK;}
static void StopDisplayLink(JaliumTimer* t){if(!t||(!t->displayLink&&!t->displayLinkTarget))return;auto stop=^{[t->displayLink invalidate];t->displayLink=nil;((JaliumDisplayLinkTarget*)t->displayLinkTarget).timer=nullptr;t->displayLinkTarget=nil;};if(pthread_main_np())stop();else dispatch_sync(dispatch_get_main_queue(),stop);}
void jalium_timer_destroy(JaliumTimer* t){if(!t)return;t->alive.store(false);StopDisplayLink(t);dispatch_source_cancel(t->source);}
static void ArmTimer(JaliumTimer* t,int64_t us,bool repeat){if(!t||us<0)return;
    // CompositionTarget's repeating 60/120 Hz timer is synchronized to the
    // display. Longer/general-purpose timers keep dispatch_source semantics.
    if(repeat&&us>0&&us<=25000){dispatch_source_set_timer(t->source,DISPATCH_TIME_FOREVER,DISPATCH_TIME_FOREVER,0);
        auto start=^{StopDisplayLink(t);auto* target=[JaliumDisplayLinkTarget new];target.timer=t;
#if TARGET_OS_OSX
            CADisplayLink* link=[NSScreen.mainScreen displayLinkWithTarget:target selector:@selector(displayLinkTick:)];
#else
            CADisplayLink* link=[CADisplayLink displayLinkWithTarget:target selector:@selector(displayLinkTick:)];
#endif
            float hz=std::clamp(1000000.0f/static_cast<float>(us),30.0f,240.0f);
            link.preferredFrameRateRange=CAFrameRateRangeMake(30.0f,hz,hz);
            [link addToRunLoop:NSRunLoop.mainRunLoop forMode:NSRunLoopCommonModes];
            t->displayLinkTarget=target;t->displayLink=link;};
        if(pthread_main_np())start();else dispatch_sync(dispatch_get_main_queue(),start);return;}
    StopDisplayLink(t);uint64_t ns=(uint64_t)us*1000;
    dispatch_source_set_timer(t->source,dispatch_time(DISPATCH_TIME_NOW,ns),repeat?ns:DISPATCH_TIME_FOREVER,std::min<uint64_t>(ns/20,1000000));}
void jalium_timer_arm(JaliumTimer* t,int64_t us){ArmTimer(t,us,false);}
void jalium_timer_arm_repeating(JaliumTimer* t,int64_t us){ArmTimer(t,us,true);}
void jalium_timer_disarm(JaliumTimer* t){if(t){StopDisplayLink(t);dispatch_source_set_timer(t->source,DISPATCH_TIME_FOREVER,DISPATCH_TIME_FOREVER,0);t->signalPending.store(false);}}
void jalium_timer_set_callback(JaliumTimer* t,JaliumTimerCallback cb,void* data){if(!t)return;std::scoped_lock lock(t->mutex);t->callback=cb;t->userData=data;}
int32_t jalium_timer_wait(JaliumTimer* t,uint32_t ms){if(!t)return 0;dispatch_time_t timeout=ms?dispatch_time(DISPATCH_TIME_NOW,(int64_t)ms*NSEC_PER_MSEC):DISPATCH_TIME_FOREVER;int success=dispatch_semaphore_wait(t->fired,timeout)==0?1:0;if(success)t->signalPending.store(false,std::memory_order_release);return success;}

float jalium_platform_get_system_dpi_scale(void){
#if TARGET_OS_OSX
    return NSScreen.mainScreen.backingScaleFactor;
#else
    return UIScreen.mainScreen.scale;
#endif
}
float jalium_window_get_dpi_scale(JaliumPlatformWindow* w){return w?w->scale:jalium_platform_get_system_dpi_scale();}
int32_t jalium_window_get_monitor_refresh_rate(JaliumPlatformWindow* w){
#if TARGET_OS_OSX
    NSScreen* screen=w&&w->window.screen?w->window.screen:NSScreen.mainScreen;
    NSNumber* number=screen.deviceDescription[@"NSScreenNumber"];
    CGDisplayModeRef mode=number?CGDisplayCopyDisplayMode(number.unsignedIntValue):nullptr;
    double hz=mode?CGDisplayModeGetRefreshRate(mode):0;if(mode)CGDisplayModeRelease(mode);
    return hz>1?(int32_t)llround(hz):60;
#else
    return (int32_t)UIScreen.mainScreen.maximumFramesPerSecond;
#endif
}
int32_t jalium_platform_get_monitor_count(void){
#if TARGET_OS_OSX
    return (int32_t)NSScreen.screens.count;
#else
    return 1;
#endif
}
int32_t jalium_platform_get_monitor_info(int32_t index,JaliumMonitorInfo* info){if(!info||index<0||index>=jalium_platform_get_monitor_count())return JALIUM_ERROR_INVALID_ARGUMENT;*info={};
#if TARGET_OS_OSX
    NSScreen* s=NSScreen.screens[index];NSRect f=s.frame,v=s.visibleFrame;CGFloat scale=s.backingScaleFactor;NSPoint origin=FrameworkScreenPoint(NSMakePoint(NSMinX(f),NSMaxY(f)),s);info->x=lround(origin.x);info->y=lround(origin.y);info->width=lround(f.size.width*scale);info->height=lround(f.size.height*scale);NSPoint work=FrameworkScreenPoint(NSMakePoint(NSMinX(v),NSMaxY(v)),s);info->workX=lround(work.x);info->workY=lround(work.y);info->workWidth=lround(v.size.width*scale);info->workHeight=lround(v.size.height*scale);info->scale=scale;NSNumber* number=s.deviceDescription[@"NSScreenNumber"];CGDisplayModeRef mode=number?CGDisplayCopyDisplayMode(number.unsignedIntValue):nullptr;double hz=mode?CGDisplayModeGetRefreshRate(mode):0;if(mode)CGDisplayModeRelease(mode);info->refreshRate=hz>1?(int32_t)llround(hz):60;info->isPrimary=index==0;
#else
    UIScreen* s=UIScreen.mainScreen;CGFloat scale=s.scale;CGRect f=s.bounds;info->width=lround(f.size.width*scale);info->height=lround(f.size.height*scale);info->workWidth=info->width;info->workHeight=info->height;info->scale=scale;info->refreshRate=(int32_t)s.maximumFramesPerSecond;info->isPrimary=1;
#endif
    return JALIUM_OK;}

int32_t jalium_window_set_min_max_size(JaliumPlatformWindow* w,int32_t minW,int32_t minH,int32_t maxW,int32_t maxH){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    NSWindow* nativeWindow = w->window;
    minW = std::max(0, minW); minH = std::max(0, minH);
    if (maxW > 0) maxW = std::max(minW, maxW);
    if (maxH > 0) maxH = std::max(minH, maxH);
    bool wasApplyingState = w->applyingState;
    w->applyingState = true;
    nativeWindow.contentMinSize = NSMakeSize(minW/w->scale, minH/w->scale);
    if (!IsLiveWindow(w, nativeWindow)) return JALIUM_ERROR_INVALID_STATE;
    nativeWindow.contentMaxSize = NSMakeSize(maxW > 0 ? maxW/w->scale : CGFLOAT_MAX,
        maxH > 0 ? maxH/w->scale : CGFLOAT_MAX);
    if (IsLiveWindow(w, nativeWindow)) w->applyingState = wasApplyingState;
    return JALIUM_OK;
#else
    (void)minW;(void)minH;(void)maxW;(void)maxH;return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_begin_move_drag(JaliumPlatformWindow* w){
#if TARGET_OS_OSX
    NSEvent* event = NSApp.currentEvent;
    if(!w||!w->enabled||!event||event.window!=w->window||
       (event.type!=NSEventTypeLeftMouseDown&&event.type!=NSEventTypeLeftMouseDragged))
        return JALIUM_ERROR_INVALID_STATE;
    [w->window performWindowDragWithEvent:event];return JALIUM_OK;
#else
    (void)w;return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_begin_resize_drag(JaliumPlatformWindow* w,int32_t edge)
{
#if TARGET_OS_OSX
    if (!w || (edge != 1 && edge != 2 && edge != 4 && edge != 5 &&
               edge != 6 && edge != 8 && edge != 9 && edge != 10))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    NSEvent* press = NSApp.currentEvent;
    if (!w->enabled || !(w->style & JALIUM_WINDOW_STYLE_RESIZABLE) ||
        w->state != JALIUM_WINDOW_STATE_NORMAL || !press || press.window != w->window ||
        press.type != NSEventTypeLeftMouseDown) return JALIUM_ERROR_INVALID_STATE;
    NSWindow* nativeWindow = w->window;
    NSRect start = nativeWindow.frame;
    NSPoint origin = [nativeWindow convertPointToScreen:press.locationInWindow];
    NSRect lastFrame = start;
    NSPoint lastPoint = origin;
    bool left = (edge & 4) != 0, right = (edge & 8) != 0;
    bool top = (edge & 1) != 0, bottom = (edge & 2) != 0;
    auto canContinue = [&] {
        return IsLiveWindow(w, nativeWindow) && w->enabled && w->visible && nativeWindow.visible &&
            (w->style & JALIUM_WINDOW_STYLE_RESIZABLE) && w->state == JALIUM_WINDOW_STATE_NORMAL &&
            w->requestedState == JALIUM_WINDOW_STATE_NORMAL &&
            !w->fullScreenTransition && !nativeWindow.miniaturized &&
            !(nativeWindow.styleMask & NSWindowStyleMaskFullScreen);
    };
    if (!canContinue()) return JALIUM_ERROR_INVALID_STATE;
    w->interactiveResize = true;
    [nativeWindow disableCursorRects];
    while (canContinue() && w->interactiveResize) {
        // A tracking-mode timer can hide, disable or close the window without
        // producing a mouse event. Bound the wait so those changes can end the
        // gesture; an idle timeout alone must keep a held press in tracking.
        NSEvent* event = [NSApp nextEventMatchingMask:NSEventMaskLeftMouseDragged | NSEventMaskLeftMouseUp
            untilDate:[NSDate dateWithTimeIntervalSinceNow:1.0 / 60.0] inMode:NSEventTrackingRunLoopMode dequeue:YES];
        if (!canContinue() || !w->interactiveResize || event.type == NSEventTypeLeftMouseUp) break;
        if (!event) continue;
        NSRect current = nativeWindow.frame;
        if (!NSEqualRects(current, lastFrame)) {
            // Preserve geometry changed by the application between samples.
            // Subsequent pointer motion continues from its latest frame.
            start = current;
            origin = lastPoint;
        }
        NSPoint point = [nativeWindow convertPointToScreen:event.locationInWindow];
        CGFloat dx = point.x - origin.x, dy = point.y - origin.y;
        NSRect content = [nativeWindow contentRectForFrameRect:start];
        CGFloat frameWidth = start.size.width - content.size.width;
        CGFloat frameHeight = start.size.height - content.size.height;
        // A timer or resize callback can update the bounds during this
        // gesture. Apply the current constraints to every subsequent sample.
        NSSize minimum = nativeWindow.contentMinSize;
        NSSize maximum = nativeWindow.contentMaxSize;
        NSRect frame = start;
        if (left || right) {
            frame.size.width = std::clamp(start.size.width + (left ? -dx : dx),
                std::max(1.0, minimum.width + frameWidth), maximum.width + frameWidth);
            if (left) frame.origin.x = NSMaxX(start) - frame.size.width;
        }
        if (top || bottom) {
            frame.size.height = std::clamp(start.size.height + (bottom ? -dy : dy),
                std::max(1.0, minimum.height + frameHeight), maximum.height + frameHeight);
            if (bottom) frame.origin.y = NSMaxY(start) - frame.size.height;
        }
        [nativeWindow setFrame:frame display:YES];
        if (!canContinue() || !w->interactiveResize) break;
        lastFrame = nativeWindow.frame;
        lastPoint = point;
        if (!NSEqualRects(lastFrame, frame)) {
            // Resize callbacks may synchronously replace this sample's frame.
            start = lastFrame;
            origin = point;
        }
    }
    [nativeWindow enableCursorRects];
    if (IsLiveWindow(w, nativeWindow)) w->interactiveResize = false;
    return JALIUM_OK;
#else
    (void)w; (void)edge; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_set_icon(JaliumPlatformWindow* w,const uint32_t* pixels,int32_t width,int32_t height)
{
    if (!w) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if (!pixels) { w->window.miniwindowImage = nil; return JALIUM_OK; }
    if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return JALIUM_ERROR_INVALID_ARGUMENT;
    auto* rep = [[NSBitmapImageRep alloc] initWithBitmapDataPlanes:nullptr pixelsWide:width
        pixelsHigh:height bitsPerSample:8 samplesPerPixel:4 hasAlpha:YES isPlanar:NO
        colorSpaceName:NSDeviceRGBColorSpace bitmapFormat:NSBitmapFormatAlphaNonpremultiplied
        bytesPerRow:width * 4 bitsPerPixel:32];
    if (!rep) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    for (size_t i = 0; i < static_cast<size_t>(width) * height; ++i) {
        uint32_t pixel = pixels[i];
        rep.bitmapData[i * 4] = (pixel >> 16) & 0xff;
        rep.bitmapData[i * 4 + 1] = (pixel >> 8) & 0xff;
        rep.bitmapData[i * 4 + 2] = pixel & 0xff;
        rep.bitmapData[i * 4 + 3] = pixel >> 24;
    }
    NSImage* image = [[NSImage alloc] initWithSize:NSMakeSize(width, height)];
    [image addRepresentation:rep]; w->window.miniwindowImage = image;
    return JALIUM_OK;
#else
    (void)pixels; (void)width; (void)height; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_set_topmost(JaliumPlatformWindow* w,int32_t top){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if (top) w->style |= JALIUM_WINDOW_STYLE_TOPMOST;
    else w->style &= ~JALIUM_WINDOW_STYLE_TOPMOST;
    w->window.level=top?NSFloatingWindowLevel:NSNormalWindowLevel;return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_set_enabled(JaliumPlatformWindow* w,int32_t enabled){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;w->enabled=enabled!=0;
#if TARGET_OS_OSX
    JaliumAppleWindow* nativeWindow = (JaliumAppleWindow*)w->window;
    if (!w->enabled) {
        CancelAppleDrag(w);
        CancelPendingWindowSelection(w);
        [w->systemMenu cancelTrackingWithoutAnimation];
        [w->view restoreCursor];
        [nativeWindow makeFirstResponder:nil];
        if (nativeWindow.jaliumOwner != w) return JALIUM_OK;
    }
    // Input and canBecomeKeyWindow already reject disabled windows. Let AppKit
    // transfer key selection when another window becomes key; calling its
    // resign notification directly leaves NSApplication.keyWindow stale.
    ApplyAppleStyle(w);
    if (nativeWindow.jaliumOwner != w) return JALIUM_OK;
    if (w->enabled && nativeWindow.keyWindow &&
        (!nativeWindow.firstResponder || nativeWindow.firstResponder == nativeWindow))
        [nativeWindow makeFirstResponder:w->view];
#else
    w->view.userInteractionEnabled=enabled!=0;
#endif
    return JALIUM_OK;}
int32_t jalium_window_set_opacity(JaliumPlatformWindow* w,double opacity){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;opacity=std::clamp(opacity,0.0,1.0);
#if TARGET_OS_OSX
    if (!std::isfinite(opacity)) return JALIUM_ERROR_INVALID_ARGUMENT;
    w->window.alphaValue=opacity;
#else
    w->view.alpha=opacity;
#endif
    return JALIUM_OK;}
int32_t jalium_window_set_show_in_taskbar(JaliumPlatformWindow* w,int32_t show)
{
    if (!w) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    // The Dock belongs to the application. Per-window visibility controls
    // Window-menu, cycling and Mission Control participation instead.
    w->showInTaskbar = show != 0;
    // This setter also runs during fullscreen transitions. Updating the menu
    // must not depend on the style-mask guard or resize the current frame.
    ApplyAppleWindowParticipation(w);
    return JALIUM_OK;
#else
    (void)show; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_set_resizable(JaliumPlatformWindow* w,int32_t value){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if(value)w->style|=JALIUM_WINDOW_STYLE_RESIZABLE;else w->style&=~JALIUM_WINDOW_STYLE_RESIZABLE;
    ApplyAppleStyle(w);return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_set_decorated(JaliumPlatformWindow* w,int32_t value){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if(value)w->style&=~JALIUM_WINDOW_STYLE_BORDERLESS;else w->style|=JALIUM_WINDOW_STYLE_BORDERLESS;
    ApplyAppleStyle(w);return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_set_owner(JaliumPlatformWindow* w,intptr_t owner){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    bool registered = IsLiveWindow(w);
    NSWindow* nativeWindow = w->window;
    NSWindow* ownerWindow = nil;
    if (owner) {
        id object = (__bridge id)(void*)owner;
        if ([object isKindOfClass:NSWindow.class]) ownerWindow = object;
        else if ([object isKindOfClass:NSView.class]) ownerWindow = [(NSView*)object window];
        if (!ownerWindow || ownerWindow == w->window) return JALIUM_ERROR_INVALID_ARGUMENT;
        for (NSWindow* ancestor = ownerWindow; ancestor; ancestor = ancestor.parentWindow)
            if (ancestor == w->window) return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    [nativeWindow.parentWindow removeChildWindow:nativeWindow];
    if (registered && !IsLiveWindow(w, nativeWindow)) return JALIUM_OK;
    if (ownerWindow) [ownerWindow addChildWindow:nativeWindow ordered:NSWindowAbove];
    return JALIUM_OK;
#else
    (void)owner;return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_activate(JaliumPlatformWindow* w){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if (!NSThread.isMainThread || !IsLiveWindow(w)) return JALIUM_ERROR_INVALID_STATE;
    NSWindow* nativeWindow = w->window;
    if (!w->enabled) return JALIUM_ERROR_INVALID_STATE;
    if (!w->visible) jalium_apple_window_show(w, 0);
    if (!IsLiveWindow(w, nativeWindow)) return JALIUM_ERROR_INVALID_STATE;
    RequestWindowSelection(w, true);
    if (!IsLiveWindow(w, nativeWindow) || !w->enabled || !w->visible) return JALIUM_ERROR_INVALID_STATE;
#else
    [w->view becomeFirstResponder];
#endif
    return JALIUM_OK;}
int32_t jalium_window_show_system_menu(JaliumPlatformWindow* w,int32_t x,int32_t y)
{
    if (!w) return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if (!NSThread.isMainThread || !IsLiveWindow(w) || !w->enabled || !w->visible ||
        !w->window.visible || w->window.miniaturized || w->fullScreenTransition || w->systemMenu)
        return JALIUM_ERROR_INVALID_STATE;
    JaliumAppleWindow* nativeWindow = (JaliumAppleWindow*)w->window;
    NSView* view = w->view;
    __attribute__((objc_precise_lifetime)) JaliumAppleWindowMenuTarget* target = [JaliumAppleWindowMenuTarget new];
    target.window = nativeWindow;
    NSMenu* menu = CreateAppleWindowMenu(target);
    w->systemMenu = menu;
    @try {
        [menu popUpMenuPositioningItem:nil atLocation:NSMakePoint(x / w->scale, y / w->scale) inView:view];
    } @finally {
        // Selection can synchronously close and replace the C++ owner. Keep
        // the original native identity and never clear a replacement's menu.
        if (IsLiveWindow(w, nativeWindow) && w->systemMenu == menu) w->systemMenu = nil;
    }
    return JALIUM_OK; // Native cancellation still handled the menu request.
#else
    (void)x; (void)y;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_update_ime_context(JaliumPlatformWindow* w,int32_t enabled,const char* surrounding,int32_t cursor,int32_t anchor,int32_t x,int32_t y,int32_t width,int32_t height){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    NSWindow* nativeWindow = w->window;
    auto* context = w->view.inputContext;
    w->view.imeEnabled = enabled && w->enabled;
    w->view.surroundingText = w->view.imeEnabled && surrounding ? [NSString stringWithUTF8String:surrounding] : nil;
    if (w->view.surroundingText) {
        NSUInteger active = Utf16IndexFromUtf8Offset(w->view.surroundingText, cursor);
        NSUInteger opposite = Utf16IndexFromUtf8Offset(w->view.surroundingText, anchor);
        w->view.selectionRange = NSMakeRange(std::min(active, opposite),
            active > opposite ? active - opposite : opposite - active);
    } else w->view.selectionRange = NSMakeRange(NSNotFound, 0);
    w->view.imeRect = NSMakeRect(x / w->scale, y / w->scale,
        std::max(1, width) / w->scale, std::max(1, height) / w->scale);
    if (!w->view.imeEnabled && w->view.markedText) {
        // The previously enabled context is needed after inputContext becomes
        // nil; clear its conversion session without changing first responder.
        w->view.markedText = nil;
        w->view.markedReplacementRange = NSMakeRange(NSNotFound, 0);
        w->view.markedSelectionRange = NSMakeRange(0, 0);
        [context discardMarkedText];
        if (!IsLiveWindow(w, nativeWindow)) return JALIUM_OK;
        DispatchSimple(w, JALIUM_EVENT_COMPOSITION_END);
        if (!IsLiveWindow(w, nativeWindow)) return JALIUM_OK;
    }
    [w->view.inputContext invalidateCharacterCoordinates];
#else
    if(enabled)[w->view becomeFirstResponder];else [w->view resignFirstResponder];
#endif
    return JALIUM_OK;}
int16_t jalium_input_get_key_state(int32_t keyCode){
#if TARGET_OS_OSX
    NSEventModifierFlags flags=NSEvent.modifierFlags;
    bool down=(keyCode==0x10&&(flags&NSEventModifierFlagShift))||
        (keyCode==0x11&&(flags&NSEventModifierFlagControl))||
        (keyCode==0x12&&(flags&NSEventModifierFlagOption))||
        (keyCode==0xa0&&(flags&NX_DEVICELSHIFTKEYMASK))||
        (keyCode==0xa1&&(flags&NX_DEVICERSHIFTKEYMASK))||
        (keyCode==0xa2&&(flags&NX_DEVICELCTLKEYMASK))||
        (keyCode==0xa3&&(flags&NX_DEVICERCTLKEYMASK))||
        (keyCode==0xa4&&(flags&NX_DEVICELALTKEYMASK))||
        (keyCode==0xa5&&(flags&NX_DEVICERALTKEYMASK))||
        (keyCode==0x5b&&(flags&NX_DEVICELCMDKEYMASK))||
        (keyCode==0x5c&&(flags&NX_DEVICERCMDKEYMASK));
    if(keyCode==0x14)return (flags&NSEventModifierFlagCapsLock)?1:0;
    return down?static_cast<int16_t>(0x8000):0;
#else
    (void)keyCode;return 0;
#endif
}
JaliumResult jalium_input_get_touch_capabilities(int32_t* present,int32_t* contacts){if(!present||!contacts)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    *present=0;*contacts=0;
#else
    *present=1;*contacts=10;
#endif
    return JALIUM_OK;}
JaliumResult jalium_input_get_pointing_capabilities(int32_t* capabilities){if(!capabilities)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    *capabilities=JALIUM_POINTING_FINE|JALIUM_POINTING_HOVER;
#elif TARGET_OS_IOS
    // UIKit touch is known; optional mice/pencils are not enumerable here yet.
    *capabilities=JALIUM_POINTING_COARSE;
#else
    *capabilities=0;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
    return JALIUM_OK;}
JaliumResult jalium_platform_set_double_click_settings(uint32_t,float){return JALIUM_OK;}
JaliumResult jalium_input_get_cursor_pos(float* x,float* y){if(!x||!y)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    NSPoint p=NSEvent.mouseLocation;*x=p.x;*y=p.y;return JALIUM_OK;
#else
    *x=*y=0;return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
void jalium_drag_set_effect(JaliumPlatformWindow* w,uint64_t id,uint32_t effect){
#if TARGET_OS_OSX
    if (CanReceiveAppleDrag(w) && w->dragTargetActive && w->dragSession == id)
        w->dragEffect = effect;
#else
    if(w&&w->dragSession==id)w->dragEffect=effect;
#endif
}
JaliumResult jalium_apple_drag_get_data(JaliumPlatformWindow* window, uint64_t sessionId,
    const char* mime, uint8_t** out, uint32_t* size)
{
    if (!out || !size || !mime) return JALIUM_ERROR_INVALID_ARGUMENT;
    *out = nullptr; *size = 0;
#if TARGET_OS_OSX
    if (!CanReceiveAppleDrag(window) || !window->dragTargetActive || window->dragSession != sessionId)
        return JALIUM_ERROR_INVALID_STATE;
    NSPasteboard* board = window->dragTargetPasteboard;
    uint64_t generation = window->dragTargetGeneration;
    NSData* data = ReadApplePasteboardData(board, mime);
    // A promised representation can run arbitrary source application code.
    if (!CanReceiveAppleDrag(window) || !window->dragTargetActive ||
        window->dragTargetGeneration != generation || window->dragSession != sessionId ||
        window->dragTargetPasteboard != board) return JALIUM_ERROR_INVALID_STATE;
    JaliumResult result = CopyApplePasteboardData(data, out, size);
    if (!CanReceiveAppleDrag(window) || !window->dragTargetActive ||
        window->dragTargetGeneration != generation || window->dragSession != sessionId ||
        window->dragTargetPasteboard != board) {
        free(*out); *out = nullptr; *size = 0;
        return JALIUM_ERROR_INVALID_STATE;
    }
    return result;
#else
    (void)window; (void)sessionId; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
intptr_t jalium_apple_drag_get_source(JaliumPlatformWindow* window, uint64_t sessionId)
{
#if TARGET_OS_OSX
    if (!CanReceiveAppleDrag(window) || !window->dragTargetActive || window->dragSession != sessionId) return 0;
    id candidate = window->dragTargetSource;
    if (![candidate isKindOfClass:JaliumAppleView.class]) return 0;
    auto* source = ((JaliumAppleView*)candidate).jaliumOwner;
    return CanReceiveAppleDrag(source) && source->dragSource && source->dragSource->running
        ? reinterpret_cast<intptr_t>((__bridge void*)candidate) : 0;
#else
    (void)window; (void)sessionId; return 0;
#endif
}
JaliumResult jalium_drag_begin(JaliumPlatformWindow* w,const JaliumDragDataItem* i,uint32_t c,uint32_t a,uint32_t* p){return jalium_drag_begin_with_image(w,i,c,a,nullptr,nullptr,nullptr,nullptr,p);}
JaliumResult jalium_drag_begin_ex(JaliumPlatformWindow* w,const JaliumDragDataItem* i,uint32_t c,uint32_t a,JaliumDragFeedbackCallback f,JaliumDragQueryContinueCallback q,void* u,uint32_t* p){return jalium_drag_begin_with_image(w,i,c,a,f,q,u,nullptr,p);}
JaliumResult jalium_drag_begin_with_image(JaliumPlatformWindow* window,
    const JaliumDragDataItem* items,uint32_t count,uint32_t allowed,
    JaliumDragFeedbackCallback feedback,JaliumDragQueryContinueCallback query,
    void* user,const JaliumDragImage* dragImage,uint32_t* performed)
{
    if(performed)*performed=JALIUM_DRAG_EFFECT_NONE;
    if(!window||!items||count==0||allowed==JALIUM_DRAG_EFFECT_NONE)
        return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if (!pthread_main_np() || !CanReceiveAppleDrag(window) || window->dragSource)
        return JALIUM_ERROR_INVALID_STATE;
    NSEvent* dragEvent = NSApp.currentEvent;
    if (!dragEvent || dragEvent.windowNumber != window->window.windowNumber ||
        (dragEvent.type != NSEventTypeLeftMouseDown && dragEvent.type != NSEventTypeLeftMouseDragged))
        return JALIUM_ERROR_INVALID_STATE;
    NSPasteboardItem* pasteboard = [NSPasteboardItem new];
    NSMutableArray<NSPasteboardItem*>* boards = [NSMutableArray arrayWithObject:pasteboard];
    NSString* uriList = nil;
    for (uint32_t index = 0; index < count; ++index) {
        if (!items[index].mimeType || (!items[index].data && items[index].dataSize)) return JALIUM_ERROR_INVALID_ARGUMENT;
        NSString* type = PasteboardTypeFromMime(items[index].mimeType);
        NSData* data = [NSData dataWithBytes:items[index].data length:items[index].dataSize];
        if (!type || !data) continue;
        if ([type isEqualToString:NSPasteboardTypeURL]) {
            uriList = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
        } else if ([type isEqualToString:NSPasteboardTypeString]) {
            NSString* value = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
            if (value) [pasteboard setString:value forType:type];
        } else [pasteboard setData:data forType:type];
    }
    bool firstURL = true;
    for (NSString* raw in [uriList componentsSeparatedByCharactersInSet:NSCharacterSet.newlineCharacterSet]) {
        NSString* value = [raw stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceCharacterSet];
        if (!value.length || [value hasPrefix:@"#"]) continue;
        NSURL* url = [NSURL URLWithString:value];
        if (!url.scheme.length) continue;
        NSPasteboardItem* board = firstURL ? pasteboard : [NSPasteboardItem new];
        if (!firstURL) [boards addObject:board];
        firstURL = false;
        [board setString:url.absoluteString forType:NSPasteboardTypeURL];
        if (url.fileURL) [board setString:url.absoluteString forType:NSPasteboardTypeFileURL];
    }
    if (!pasteboard.types.count) return JALIUM_ERROR_INVALID_ARGUMENT;
    NSMutableArray<NSDraggingItem*>* draggingItems = [NSMutableArray array];
    for (NSPasteboardItem* board in boards)
        [draggingItems addObject:[[NSDraggingItem alloc] initWithPasteboardWriter:board]];
    NSImage* image=nil;NSSize imageSize=NSMakeSize(32,32);NSPoint hotspot=NSMakePoint(0,0);
    if(dragImage&&dragImage->bgraPixels&&dragImage->width&&dragImage->height&&
       dragImage->stride>=dragImage->width*4){
        NSBitmapImageRep* representation=[[NSBitmapImageRep alloc]
            initWithBitmapDataPlanes:nil pixelsWide:dragImage->width pixelsHigh:dragImage->height
            bitsPerSample:8 samplesPerPixel:4 hasAlpha:YES isPlanar:NO
            colorSpaceName:NSDeviceRGBColorSpace
            bitmapFormat:NSBitmapFormatAlphaFirst|NSBitmapFormatThirtyTwoBitLittleEndian
            bytesPerRow:dragImage->width*4 bitsPerPixel:32];
        for(uint32_t y=0;y<dragImage->height;++y)std::memcpy(
            representation.bitmapData+static_cast<size_t>(y)*representation.bytesPerRow,
            dragImage->bgraPixels+static_cast<size_t>(y)*dragImage->stride,
            static_cast<size_t>(dragImage->width)*4);
        image=[[NSImage alloc]initWithSize:NSMakeSize(dragImage->width,dragImage->height)];
        [image addRepresentation:representation];imageSize=image.size;
        hotspot=NSMakePoint(dragImage->hotspotX,dragImage->hotspotY);
    }else image=[NSImage imageWithSystemSymbolName:@"doc" accessibilityDescription:nil];
    NSView* view = (NSView*)window->view;
    NSWindow* nativeWindow = window->window;
    NSPoint point=[view convertPoint:dragEvent.locationInWindow fromView:nil];
    for (NSDraggingItem* draggingItem in draggingItems)
        [draggingItem setDraggingFrame:NSMakeRect(point.x-hotspot.x,point.y-hotspot.y,
            imageSize.width,imageSize.height) contents:image];
    // The nested AppKit loop and every application callback can destroy the
    // platform window. Keep the operation result alive independently of it.
    auto source = std::make_shared<JaliumAppleDragSource>();
    source->window = nativeWindow;
    source->allowedEffects = allowed;
    source->feedback = feedback; source->query = query; source->userData = user;
    if (!window->retiredDragSources)
        window->retiredDragSources = [NSHashTable hashTableWithOptions:
            NSPointerFunctionsWeakMemory | NSPointerFunctionsObjectPointerPersonality];
    window->dragSource = source;
    // Movement alone does not cover stationary Escape, modifier changes or
    // button release. Install before entering AppKit: startup can synchronously
    // dispatch terminal events before returning its NSDraggingSession.
    id monitor = query ? [NSEvent addLocalMonitorForEventsMatchingMask:
        NSEventMaskKeyDown | NSEventMaskFlagsChanged | NSEventMaskLeftMouseUp
        handler:^NSEvent*(NSEvent* event) {
            if (!IsLiveWindow(window, nativeWindow) || window->dragSource != source || !source->running) return event;
            auto action = QueryAppleDragSource(window, source, event, NSEvent.mouseLocation);
            bool release = event.type == NSEventTypeLeftMouseUp;
            bool escape = event.type == NSEventTypeKeyDown && event.keyCode == 53;
            // A replacement terminal event must win over the one currently
            // being monitored; forwarding MouseUp would commit before Cancel's
            // posted Escape, and forwarding Escape would cancel before Drop.
            bool suppress = ((release || escape) && action == JALIUM_DRAG_CONTINUE) ||
                (release && action == JALIUM_DRAG_CANCEL) || (escape && action == JALIUM_DRAG_DROP);
            return suppress ? nil : event;
        }] : nil;
    NSDraggingSession* session = nil;
    JaliumResult result = JALIUM_OK;
    @try {
        if (query && !monitor) {
            result = JALIUM_ERROR_INVALID_STATE;
            source->running = false;
        } else {
            session = [view beginDraggingSessionWithItems:draggingItems
                event:dragEvent source:(id<NSDraggingSource>)view];
            source->session = session;
            if (!session) source->running = false;
            else session.draggingFormation = NSDraggingFormationNone;
            while (source->running)
                [NSRunLoop.currentRunLoop runMode:NSDefaultRunLoopMode
                    beforeDate:[NSDate dateWithTimeIntervalSinceNow:0.01]];
        }
    } @catch (NSException*) {
        // An AppKit startup refusal must not unwind across the managed C ABI.
        result = JALIUM_ERROR_INVALID_STATE;
        source->performedEffect = JALIUM_DRAG_EFFECT_NONE;
    } @finally {
        // A source startup failure does not own an incoming target visit.
        CancelAppleDragSource(source);
        if (monitor) [NSEvent removeMonitor:monitor];
        source->feedback = nullptr; source->query = nullptr; source->userData = nullptr;
        if (IsLiveWindow(window, nativeWindow) && window->dragSource == source) {
            if (session) [window->retiredDragSources addObject:session];
            window->dragSource.reset();
        }
    }
    if (performed) *performed = source->performedEffect;
    return result != JALIUM_OK ? result : session ? JALIUM_OK : JALIUM_ERROR_INVALID_STATE;
#else
    (void)feedback;(void)query;(void)user;(void)dragImage;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult jalium_clipboard_get_formats(char** out){if(!out)return JALIUM_ERROR_INVALID_ARGUMENT;*out=nullptr;
#if TARGET_OS_OSX
    std::string formats=PasteboardTypesUtf8(NSPasteboard.generalPasteboard);
    const char* utf8=formats.c_str();
#else
    NSArray<NSString*>* types=UIPasteboard.generalPasteboard.types;
    NSMutableString* joined=[NSMutableString string];for(NSString* type in types){if(joined.length)[joined appendString:@"\n"];[joined appendString:type];}
    const char* utf8=joined.UTF8String?:"";
#endif
    size_t n=strlen(utf8)+1;*out=(char*)malloc(n);if(!*out)return JALIUM_ERROR_OUT_OF_MEMORY;memcpy(*out,utf8,n);return JALIUM_OK;}
JaliumResult jalium_clipboard_get_data(const char* mime,uint8_t** out,uint32_t* size){if(!mime||!out||!size)return JALIUM_ERROR_INVALID_ARGUMENT;*out=nullptr;*size=0;
#if TARGET_OS_OSX
    return CopyApplePasteboardData(ReadApplePasteboardData(NSPasteboard.generalPasteboard,mime), out, size);
#else
    NSString* type=[NSString stringWithUTF8String:mime];
    NSData* data=[UIPasteboard.generalPasteboard dataForPasteboardType:type];
    if(!data)return JALIUM_OK;*size=(uint32_t)data.length;*out=(uint8_t*)malloc(std::max<NSUInteger>(data.length,1));if(!*out)return JALIUM_ERROR_OUT_OF_MEMORY;if(data.length)memcpy(*out,data.bytes,data.length);return JALIUM_OK;
#endif
}
JaliumResult jalium_clipboard_set_data(const JaliumClipboardDataItem* items,uint32_t count){
#if TARGET_OS_OSX
    NSPasteboard* pb=NSPasteboard.generalPasteboard;[pb clearContents];NSMutableOrderedSet<NSPasteboardType>* types=[NSMutableOrderedSet orderedSet];for(uint32_t i=0;i<count;++i){if(items[i].mimeType)[types addObject:PasteboardTypeFromMime(items[i].mimeType)];}[pb declareTypes:types.array owner:nil];for(uint32_t i=0;i<count;++i){if(!items[i].mimeType)continue;NSString* type=PasteboardTypeFromMime(items[i].mimeType);NSData* data=[NSData dataWithBytes:items[i].data length:items[i].dataSize];if(![pb setData:data forType:type])return JALIUM_ERROR_INVALID_STATE;}return JALIUM_OK;
#else
    UIPasteboard* pb=UIPasteboard.generalPasteboard;NSMutableDictionary* entry=[NSMutableDictionary dictionary];for(uint32_t i=0;i<count;++i){if(!items[i].mimeType)continue;entry[[NSString stringWithUTF8String:items[i].mimeType]]=[NSData dataWithBytes:items[i].data length:items[i].dataSize];}pb.items=count?@[entry]:@[];return JALIUM_OK;
#endif
}
JaliumResult jalium_clipboard_clear(void){return jalium_clipboard_set_data(nullptr,0);}
JaliumResult jalium_clipboard_get_text(JaliumUtf16Char** out){if(!out)return JALIUM_ERROR_INVALID_ARGUMENT;*out=nullptr;
#if TARGET_OS_OSX
    NSString* text=[NSPasteboard.generalPasteboard stringForType:NSPasteboardTypeString];
#else
    NSString* text=UIPasteboard.generalPasteboard.string;
#endif
    if(!text)return JALIUM_OK;NSUInteger length=text.length;auto* result=(JaliumUtf16Char*)malloc((length+1)*sizeof(JaliumUtf16Char));if(!result)return JALIUM_ERROR_OUT_OF_MEMORY;[text getCharacters:(unichar*)result range:NSMakeRange(0,length)];result[length]=0;*out=result;return JALIUM_OK;}
JaliumResult jalium_clipboard_set_text(const JaliumUtf16Char* text){if(!text)return jalium_clipboard_clear();NSString* value=StringFromUtf16(text);
#if TARGET_OS_OSX
    NSPasteboard* pb=NSPasteboard.generalPasteboard;[pb clearContents];return [pb setString:value forType:NSPasteboardTypeString]?JALIUM_OK:JALIUM_ERROR_INVALID_STATE;
#else
    UIPasteboard.generalPasteboard.string=value;return JALIUM_OK;
#endif
}
