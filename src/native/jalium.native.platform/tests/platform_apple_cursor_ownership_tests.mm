#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <cstdio>
#include <dlfcn.h>

extern "C" void jalium_apple_window_show(JaliumPlatformWindow*, int32_t);

// Private, process-local metadata and cursor-call probes. Windows stay hidden,
// no input is posted, and none of the replacement cursor methods changes the
// system cursor. This does not constitute physical pointer acceptance.
static NSWindow* controlledWindow;
static NSView* controlledView;
static NSPanel* controlledSheet;
static NSEvent* controlledEvent;
static NSPoint pointer;
static NSInteger hitWindow;
static NSUInteger pressedButtons;
static bool visible, miniaturized, active, sheet;
static int sets, hides, unhides, clears, invalidations, balance;
static int passed, total;
static const char* chrome;
static NSEvent* dragEvent;
static int dragStarts, dragStartBalance;
static IMP originalVisible, originalMiniaturized, originalActive, originalSheet, originalInvalidate;

static void Check(bool valid, const char* name)
{
    ++total;
    if (valid) ++passed;
    else std::fprintf(stderr, "FAIL %s: %s (set=%d hide=%d unhide=%d clear=%d rect=%d balance=%d)\n",
        chrome, name, sets, hides, unhides, clears, invalidations, balance);
}
static void ResetCalls() { sets = hides = unhides = clears = invalidations = 0; }
static bool NoCursorChange() { return !sets && !hides && !unhides && !clears; }
static void CursorSet(id, SEL) { ++sets; }
static void CursorHide(id, SEL) { ++hides; ++balance; }
static void CursorUnhide(id, SEL) { ++unhides; --balance; }
static void CursorClear(id, SEL, BOOL) { ++clears; }
static NSCursor* CurrentCursor(id, SEL) { return NSCursor.arrowCursor; }
static NSPoint MouseLocation(id, SEL) { return pointer; }
static NSUInteger MouseButtons(id, SEL) { return pressedButtons; }
static NSInteger HitWindow(id, SEL, NSPoint, NSInteger) { return hitWindow; }
static NSEvent* CurrentEvent(id, SEL) { return dragEvent; }
static void KeepHidden(id, SEL, id) {}
static NSDraggingSession* RefuseDrag(id, SEL, NSArray*, NSEvent*, id)
{
    ++dragStarts; dragStartBalance = balance;
    return nil;
}
static BOOL Visible(id self, SEL selector)
{
    return self == controlledWindow ? visible : reinterpret_cast<BOOL(*)(id, SEL)>(originalVisible)(self, selector);
}
static BOOL Miniaturized(id self, SEL selector)
{
    return self == controlledWindow ? miniaturized : reinterpret_cast<BOOL(*)(id, SEL)>(originalMiniaturized)(self, selector);
}
static BOOL Active(id self, SEL selector)
{
    return self == NSApp ? active : reinterpret_cast<BOOL(*)(id, SEL)>(originalActive)(self, selector);
}
static NSWindow* Sheet(id self, SEL selector)
{
    return self == controlledWindow ? (sheet ? controlledSheet : nil) :
        reinterpret_cast<NSWindow*(*)(id, SEL)>(originalSheet)(self, selector);
}
static void Invalidate(id self, SEL selector, NSView* view)
{
    if (self == controlledWindow && view == controlledView) { ++invalidations; return; }
    reinterpret_cast<void(*)(id, SEL, NSView*)>(originalInvalidate)(self, selector, view);
}
struct Replacement {
    Method method;
    IMP original;
    Replacement(Method value, IMP replacement) : method(value), original(method_setImplementation(value, replacement)) {}
    ~Replacement() { method_setImplementation(method, original); }
};
static void Inside()
{
    NSPoint local = NSMakePoint(NSMidX(controlledView.bounds), NSMidY(controlledView.bounds));
    pointer = [controlledWindow convertPointToScreen:[controlledView convertPoint:local toView:nil]];
    hitWindow = controlledWindow.windowNumber;
}
static void Outside()
{
    NSPoint local = NSMakePoint(NSMaxX(controlledView.bounds) + 100, NSMidY(controlledView.bounds));
    pointer = [controlledWindow convertPointToScreen:[controlledView convertPoint:local toView:nil]];
}
static void UpdateCursor() { [controlledView cursorUpdate:controlledEvent]; }
static void ResetShape(JaliumPlatformWindow* window)
{
    jalium_window_set_cursor(window, JALIUM_CURSOR_ARROW);
    ResetCalls();
}
static void DelegateEvent(SEL selector)
{
    id delegate = controlledWindow.delegate;
    reinterpret_cast<void(*)(id, SEL, id)>([delegate methodForSelector:selector])(delegate, selector, nil);
}

@interface JaliumCursorGestureEvent : NSEvent
@property(nonatomic) NSEventType kind;
@property(nonatomic) NSInteger button;
@property(nonatomic) NSPoint point;
@end
@implementation JaliumCursorGestureEvent
- (NSEventType)type { return _kind; }
- (NSInteger)buttonNumber { return _button; }
- (NSPoint)locationInWindow { return _point; }
- (NSEventModifierFlags)modifierFlags { return 0; }
- (NSInteger)clickCount { return 1; }
@end
@interface JaliumCursorDragVisit : NSObject
@property(nonatomic, strong) NSPasteboard* board;
@end
@implementation JaliumCursorDragVisit
- (NSPoint)draggingLocation { return [controlledWindow convertPointFromScreen:pointer]; }
- (NSPasteboard*)draggingPasteboard { return _board; }
- (NSDragOperation)draggingSourceOperationMask { return NSDragOperationCopy; }
- (NSInteger)draggingSequenceNumber { return 171; }
- (id)draggingSource { return nil; }
@end
@interface JaliumCursorPassThroughView : NSView
@end
@implementation JaliumCursorPassThroughView
- (NSView*)hitTest:(NSPoint)point { return nil; }
@end
@interface JaliumCursorReentrantView : NSView
@property(nonatomic, copy) void (^onHit)(void);
@end
@implementation JaliumCursorReentrantView
- (NSView*)hitTest:(NSPoint)point
{
    void (^action)(void) = self.onHit;
    self.onHit = nil;
    if (action) action();
    return nil;
}
@end
static void Gesture(int button, int phase)
{
    auto* event = [JaliumCursorGestureEvent new];
    event.button = button;
    event.point = [controlledWindow convertPointFromScreen:pointer];
    event.kind = button == 0 ? (phase == 0 ? NSEventTypeLeftMouseDown : phase == 1 ? NSEventTypeLeftMouseDragged : NSEventTypeLeftMouseUp) :
        button == 1 ? (phase == 0 ? NSEventTypeRightMouseDown : phase == 1 ? NSEventTypeRightMouseDragged : NSEventTypeRightMouseUp) :
        (phase == 0 ? NSEventTypeOtherMouseDown : phase == 1 ? NSEventTypeOtherMouseDragged : NSEventTypeOtherMouseUp);
    if (button == 0) {
        if (!phase) [controlledView mouseDown:event]; else if (phase == 1) [controlledView mouseDragged:event]; else [controlledView mouseUp:event];
    } else if (button == 1) {
        if (!phase) [controlledView rightMouseDown:event]; else if (phase == 1) [controlledView rightMouseDragged:event]; else [controlledView rightMouseUp:event];
    } else {
        if (!phase) [controlledView otherMouseDown:event]; else if (phase == 1) [controlledView otherMouseDragged:event]; else [controlledView otherMouseUp:event];
    }
}
static void Probe(uint32_t style, const char* name)
{
    chrome = name;
    JaliumWindowParams params{};
    params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Hidden cursor contract");
    params.width = 320; params.height = 240; params.style = style;
    JaliumPlatformWindow* window = jalium_window_create(&params);
    Check(window != nullptr, "create hidden cursor window");
    if (!window) return;
    controlledView = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(window));
    controlledWindow = controlledView.window;
    controlledEvent = [NSEvent mouseEventWithType:NSEventTypeMouseMoved location:NSZeroPoint
        modifierFlags:0 timestamp:0 windowNumber:controlledWindow.windowNumber context:nil
        eventNumber:0 clickCount:0 pressure:0];
    visible = miniaturized = sheet = false; active = true;
    NSView* foreignOwner = [NSView new];
    NSTrackingArea* foreign = [[NSTrackingArea alloc] initWithRect:controlledView.bounds
        options:NSTrackingMouseMoved|NSTrackingActiveInActiveApp owner:foreignOwner userInfo:nil];
    [controlledView addTrackingArea:foreign];
    for (int update = 0; update < 2; ++update) {
        [controlledView updateTrackingAreas];
        Check([controlledView.trackingAreas containsObject:foreign], "tracking refresh preserves another owner's area");
        NSMutableArray<NSTrackingArea*>* owned = [controlledView.trackingAreas mutableCopy];
        [owned removeObject:foreign];
        Check(owned.count == 1, "repeated refresh keeps exactly one platform tracking area");
        NSTrackingAreaOptions options = owned.firstObject.options;
        Check((options & NSTrackingActiveInActiveApp) && !(options & NSTrackingActiveInKeyWindow),
            "non-key windows track pointer entry while their application is active");
        Check((options & NSTrackingEnabledDuringMouseDrag) && (options & NSTrackingCursorUpdate),
            "tracking keeps cursor and entry events available during drag");
    }
    if ([controlledView.trackingAreas containsObject:foreign]) [controlledView removeTrackingArea:foreign];
    Inside(); ResetCalls();

    jalium_window_set_cursor(window, JALIUM_CURSOR_HAND);
    Check(NoCursorChange(), "hidden window cannot replace another cursor");
    Check(invalidations == 1, "shape changes refresh AppKit cursor rectangles");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "hidden window cannot hide the cursor");
    ResetShape(window);

    visible = true; active = false;
    jalium_window_set_cursor(window, JALIUM_CURSOR_HAND);
    Check(NoCursorChange(), "inactive application defers cursor application");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "inactive application cannot add a hide request");
    ResetShape(window); active = true;

    Inside();
    NSTextField* nativeChild = [[NSTextField alloc] initWithFrame:NSInsetRect(controlledView.bounds,
        NSWidth(controlledView.bounds) / 4, NSHeight(controlledView.bounds) / 4)];
    [controlledView addSubview:nativeChild];
    NSPoint childPoint = [controlledView convertPoint:
        NSMakePoint(NSMidX(controlledView.bounds), NSMidY(controlledView.bounds)) toView:controlledView.superview];
    Check([controlledView hitTest:childPoint] == nativeChild, "SDK hit testing reaches the embedded native text field");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HAND);
    Check(NoCursorChange(), "native child cursor is not replaced by its platform parent");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "native child cursor is not hidden by its platform parent");
    nativeChild.hidden = YES; ResetCalls(); UpdateCursor();
    Check(hides == 1 && balance == 1, "hidden native child no longer blocks the parent's configured cursor");
    nativeChild.hidden = NO; ResetCalls(); UpdateCursor();
    Check(unhides == 1 && !sets && balance == 0, "revealing a native child releases its parent's hide");
    ResetShape(window);
    NSPoint edge = NSMakePoint(NSMinX(controlledView.bounds) + 5, NSMinY(controlledView.bounds) + 5);
    pointer = [controlledWindow convertPointToScreen:[controlledView convertPoint:edge toView:nil]];
    Check([controlledView hitTest:[controlledView convertPoint:edge toView:controlledView.superview]] == controlledView,
        "captured gesture starts on the platform view outside the native child");
    pressedButtons = 1; jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN); Gesture(0, 0);
    Inside(); ResetCalls(); Gesture(0, 1);
    Check(NoCursorChange() && balance == 1, "captured parent gesture keeps its cursor over a native child");
    pressedButtons = 0; ResetCalls(); Gesture(0, 2);
    Check(unhides == 1 && !sets && balance == 0, "release over a native child immediately returns cursor ownership");
    ResetShape(window); jalium_window_set_cursor(window, JALIUM_CURSOR_HAND);
    [nativeChild removeFromSuperview]; ResetCalls(); UpdateCursor();
    Check(sets == 1 && !hides && !unhides, "removing the native child applies the parent's deferred shape");
    NSView* overlay = [[JaliumCursorPassThroughView alloc] initWithFrame:controlledView.bounds];
    [controlledView addSubview:overlay];
    Check([controlledView hitTest:childPoint] == controlledView, "SDK hit testing skips a pass-through overlay");
    ResetCalls(); UpdateCursor();
    Check(sets == 1 && !hides && !unhides, "pass-through native overlay preserves platform cursor ownership");
    [overlay removeFromSuperview]; ResetShape(window);

    Outside();
    jalium_window_set_cursor(window, JALIUM_CURSOR_HAND);
    Check(NoCursorChange(), "pointer outside client view keeps its current cursor");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "pointer outside client view is not hidden");
    ResetShape(window); Inside(); hitWindow = controlledWindow.windowNumber + 1000;
    jalium_window_set_cursor(window, JALIUM_CURSOR_HAND);
    Check(NoCursorChange(), "covered client view cannot replace the frontmost cursor");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "covered client view cannot hide the frontmost cursor");
    ResetShape(window); Inside();
    controlledView.hidden = YES;
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "hidden client view cannot hide the cursor");
    ResetShape(window); controlledView.hidden = NO;
    controlledView.superview.hidden = YES;
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "hidden client ancestor cannot hide the cursor");
    ResetShape(window); controlledView.superview.hidden = NO;
    visible = false; pressedButtons = 1; Gesture(0, 0);
    visible = true; Outside(); hitWindow += 1000; ResetCalls();
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "queued down on a hidden window cannot revive capture after reveal");
    ResetShape(window); Inside(); controlledView.hidden = YES; Gesture(0, 0);
    controlledView.hidden = NO; Outside(); hitWindow += 1000; ResetCalls();
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "queued down on a hidden view cannot revive capture after reveal");
    ResetShape(window); Inside(); active = false; Gesture(0, 0);
    active = true; Outside(); hitWindow += 1000; ResetCalls();
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "queued down in an inactive application cannot revive capture after activation");
    ResetShape(window); pressedButtons = 0; Inside();

    sheet = true;
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "sheet owner cannot hide the panel cursor");
    ResetShape(window); sheet = false;
    miniaturized = true;
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "minimized window cannot hide the cursor");
    ResetShape(window); miniaturized = false;
    jalium_window_set_enabled(window, 0); ResetCalls();
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "disabled window cannot hide the cursor");
    ResetShape(window); jalium_window_set_enabled(window, 1); Inside(); ResetCalls();

    jalium_window_set_cursor(window, JALIUM_CURSOR_HAND);
    Check(sets == 1 && !hides && !unhides && clears == 1, "owning client applies its cursor immediately");
    Check(invalidations == 1, "visible shape change refreshes cursor rectangles");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HAND);
    Check(invalidations == 0, "unchanged shape does not invalidate cursor rectangles again");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(hides == 1 && !sets && !unhides && balance == 1, "owning client hides once");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN); UpdateCursor();
    Check(NoCursorChange() && balance == 1, "repeated requests do not nest hidden cursors");
    Outside(); ResetCalls(); UpdateCursor();
    Check(unhides == 1 && !hides && !sets && !clears && balance == 0, "leaving cursor ownership balances its hide request");
    ResetCalls(); UpdateCursor();
    Check(NoCursorChange() && balance == 0, "repeated outside updates do not unhide another cursor");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HAND);
    Check(NoCursorChange() && invalidations == 1, "deferred cursor keeps the new shape");
    Inside(); ResetCalls(); UpdateCursor();
    Check(sets == 1 && !hides && !unhides, "cursor update applies the deferred shape");

    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(hides == 1 && balance == 1, "hide before pointer exit");
    hitWindow += 1000; ResetCalls(); [controlledView mouseExited:controlledEvent];
    Check(unhides == 1 && !sets && !hides && !clears && balance == 0,
        "mouse exit releases its hide without forcing an arrow over another view");
    Inside(); ResetCalls(); [controlledView mouseEntered:controlledEvent];
    Check(hides == 1 && balance == 1, "mouse entry reapplies the configured hidden cursor");
    sheet = true; ResetCalls(); UpdateCursor();
    Check(unhides == 1 && !hides && balance == 0, "panel ownership releases an existing hide");
    sheet = false; ResetCalls(); UpdateCursor();
    Check(hides == 1 && balance == 1, "owner regains the configured cursor after its sheet");
    ResetCalls(); jalium_window_set_enabled(window, 0);
    Check(unhides == 1 && balance == 0, "disabling releases an existing hide exactly once");
    jalium_window_set_enabled(window, 1); Inside(); ResetCalls(); UpdateCursor();
    Check(hides == 1 && balance == 1, "enabled owner can hide again");
    ResetCalls(); jalium_window_hide(window);
    Check(unhides == 1 && balance == 0, "hiding releases an existing hide exactly once");

    // The getters remain controlled metadata; no native window is shown.
    visible = true; Inside(); ResetCalls(); UpdateCursor();
    Check(hides == 1 && balance == 1, "hide before application deactivation");
    active = false; ResetCalls(); DelegateEvent(@selector(windowDidResignKey:));
    Check(unhides == 1 && balance == 0, "deactivation releases an existing hide");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "deactivated owner cannot immediately hide again");
    active = true; ResetCalls(); DelegateEvent(@selector(windowDidBecomeKey:));
    Check(hides == 1 && balance == 1, "activation reapplies the cursor under the pointer");
    ResetShape(window);
    hitWindow += 1000; ResetCalls(); DelegateEvent(@selector(windowDidBecomeKey:));
    Check(NoCursorChange(), "activation does not replace another window's cursor");

    for (int button = 0; button < 5; ++button) {
        Inside(); ResetShape(window); pressedButtons = NSUInteger(1) << button;
        jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN); Gesture(button, 0);
        Outside(); hitWindow += 1000; ResetCalls(); Gesture(button, 1);
        Check(NoCursorChange() && balance == 1, "captured drag keeps its cursor outside and over another window");
        ResetCalls(); [controlledView mouseExited:controlledEvent];
        Check(NoCursorChange() && balance == 1, "tracking exit does not cancel the held gesture cursor");
        pressedButtons = 0; ResetCalls(); Gesture(button, 2);
        Check(unhides == 1 && !sets && !hides && balance == 0, "mouse-up outside releases the captured cursor immediately");
    }
    Inside(); ResetShape(window); pressedButtons = 1;
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN); Gesture(0, 0);
    Outside(); hitWindow += 1000; pressedButtons = 0; ResetCalls(); UpdateCursor();
    Check(unhides == 1 && balance == 0, "fresh released state cancels capture without a delivered mouse-up");
    ResetShape(window); pressedButtons = 1;
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "a button held by another view cannot claim this cursor");
    Inside(); ResetShape(window);
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN); Gesture(0, 0); Outside(); hitWindow += 1000;
    active = false; ResetCalls(); DelegateEvent(@selector(windowDidResignKey:));
    Check(unhides == 1 && balance == 0, "deactivation cancels a held cursor gesture");
    active = true; ResetCalls(); DelegateEvent(@selector(windowDidBecomeKey:));
    Check(NoCursorChange() && balance == 0, "reactivation does not revive the old captured gesture");
    Inside(); ResetShape(window); Gesture(0, 0); Outside(); hitWindow += 1000; ResetCalls();
    jalium_window_set_cursor(window, JALIUM_CURSOR_HAND);
    Check(sets == 1 && !hides && !unhides, "shape changes during capture apply outside the client");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(hides == 1 && balance == 1, "captured cursor can become hidden outside the client");
    pressedButtons = 0; ResetCalls(); UpdateCursor();
    Check(unhides == 1 && balance == 0, "captured shape changes still release on the fresh snapshot");

    // Show only the platform's logical state. orderFront is interposed and the
    // original SDK getter below verifies that no native window became visible.
    Inside(); ResetShape(window); jalium_apple_window_show(window, 0);
    Check(!reinterpret_cast<BOOL(*)(id, SEL)>(originalVisible)(controlledWindow, @selector(isVisible)),
        "logical drag setup leaves the actual native window hidden");
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    dragEvent = [NSEvent mouseEventWithType:NSEventTypeLeftMouseDown location:NSZeroPoint
        modifierFlags:0 timestamp:0 windowNumber:controlledWindow.windowNumber context:nil
        eventNumber:0 clickCount:1 pressure:1];
    const uint8_t payload[] = {'c','u','r','s','o','r'};
    JaliumDragDataItem data{"text/plain", payload, sizeof(payload)};
    uint32_t performed = 99; dragStarts = 0; dragStartBalance = -1; ResetCalls();
    auto result = jalium_drag_begin(window, &data, 1, JALIUM_DRAG_EFFECT_COPY, &performed);
    Check(dragStarts == 1 && dragStartBalance == 0, "AppKit drag startup receives ownership without a hidden cursor");
    Check(result == JALIUM_ERROR_INVALID_STATE && performed == JALIUM_DRAG_EFFECT_NONE,
        "controlled AppKit startup refusal preserves its ABI result");
    Check(unhides == 1 && hides == 1 && balance == 1, "startup refusal restores the configured hover cursor");
    dragEvent = nil; ResetShape(window);
    auto* visit = [JaliumCursorDragVisit new];
    visit.board = [NSPasteboard pasteboardWithUniqueName];
    [visit.board setString:@"cursor" forType:NSPasteboardTypeString];
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN); ResetCalls();
    [controlledView draggingEntered:(id<NSDraggingInfo>)visit];
    Check(unhides == 1 && balance == 0, "incoming AppKit drag releases the destination's hidden cursor");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "active destination cannot override AppKit's drag cursor");
    [controlledView draggingExited:(id<NSDraggingInfo>)visit]; ResetCalls(); UpdateCursor();
    Check(hides == 1 && balance == 1, "hover cursor can return after the destination visit ends");
    [visit.board releaseGlobally]; ResetShape(window);

    Inside(); [NSCursor hide]; ResetCalls();
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(hides == 1 && balance == 2, "owner adds only its own hide beside a foreign hide");
    Outside(); ResetCalls(); UpdateCursor();
    Check(unhides == 1 && balance == 1, "ownership loss preserves the foreign hide");
    ResetCalls(); UpdateCursor();
    Check(NoCursorChange() && balance == 1, "repeated ownership loss keeps the foreign hide balanced");
    [NSCursor unhide]; Inside(); ResetCalls(); UpdateCursor();
    Check(hides == 1 && balance == 1, "hide before minimize transition");
    ResetCalls(); DelegateEvent(@selector(windowWillMiniaturize:));
    Check(unhides == 1 && balance == 0, "minimize start releases its hide before the animation");
    ResetCalls(); jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "pending minimize cannot add a new hide");
    jalium_window_destroy(window); ResetCalls(); UpdateCursor();
    Check(NoCursorChange() && balance == 0, "retained old view cannot change the cursor after destruction");
    // Isolate title-bar cases even when an old platform incorrectly hides from
    // this retained view. This private cleanup is not counted as product success.
    SEL restore = @selector(restoreCursor);
    reinterpret_cast<void(*)(id, SEL)>([controlledView methodForSelector:restore])(controlledView, restore);
    Check(balance == 0, "private case cleanup leaves no hide request for the next fixture");
    controlledView = nil; controlledWindow = nil; controlledEvent = nil;
}

static void ProbeReentrant(uint32_t style, const char* name)
{
    chrome = name;
    JaliumWindowParams params{};
    params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Hidden reentrant cursor contract");
    params.width = 320; params.height = 240; params.style = style;
    JaliumPlatformWindow* window = jalium_window_create(&params);
    Check(window != nullptr, "create hidden reentrant cursor window");
    if (!window) return;
    controlledView = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(window));
    controlledWindow = controlledView.window;
    visible = active = true; miniaturized = sheet = false; pressedButtons = 0;
    jalium_apple_window_show(window, 0);
    Check(!reinterpret_cast<BOOL(*)(id, SEL)>(originalVisible)(controlledWindow, @selector(isVisible)),
        "reentrant SDK hit tests keep the real native window hidden");
    JaliumCursorReentrantView* child = [[JaliumCursorReentrantView alloc] initWithFrame:controlledView.bounds];
    [controlledView addSubview:child];
    NSRect clientFrame = controlledView.frame;
    NSRect ancestorFrame = controlledView.superview.frame;
    SEL restore = @selector(restoreCursor);
    auto reset = [&] {
        // Isolate cases even if the baseline issued an invalid hide. This is
        // private cursor-call cleanup, never evidence that the product passed.
        reinterpret_cast<void(*)(id, SEL)>([controlledView methodForSelector:restore])(controlledView, restore);
        controlledView.hidden = NO; controlledView.superview.hidden = NO;
        controlledView.superview.frame = ancestorFrame;
        controlledView.frame = clientFrame;
        for (NSView* view in controlledView.subviews.copy)
            if (view != child) [view removeFromSuperview];
        active = visible = true; miniaturized = sheet = false;
        jalium_window_set_enabled(window, 1); jalium_apple_window_show(window, 0);
        Inside(); ResetShape(window);
    };
    auto check = [&](void (^action)(void), const char* label) {
        reset(); child.onHit = action; ResetCalls();
        jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
        Check(NoCursorChange() && balance == 0, label);
    };
    check(^{ jalium_window_set_enabled(window, 0); }, "hit-test disabling cannot add a new hide");
    check(^{ controlledView.hidden = YES; }, "hit-test hiding the client cannot add a new hide");
    check(^{ controlledView.superview.hidden = YES; }, "hit-test hiding an ancestor cannot add a new hide");
    check(^{ visible = false; jalium_window_hide(window); }, "hit-test hiding the window cannot add a new hide");
    check(^{ active = false; }, "hit-test application deactivation cannot add a new hide");
    check(^{ sheet = true; }, "hit-test attaching a sheet cannot add a new hide");
    check(^{ Outside(); }, "hit-test pointer departure cannot apply an obsolete hit");
    check(^{ hitWindow += 1000; }, "hit-test occlusion cannot apply an obsolete hit");
    check(^{ jalium_window_move(window, 1000, 1000); }, "hit-test window movement cannot apply obsolete client coordinates");
    check(^{
        NSRect frame = controlledView.frame; frame.size.width /= 4;
        controlledView.frame = frame;
    }, "hit-test client contraction cannot apply a now-clipped point");
    check(^{
        NSRect frame = controlledView.superview.frame; frame.size.width /= 4;
        controlledView.superview.frame = frame;
    }, "hit-test ancestor contraction cannot apply a now-clipped point");
    check(^{
        NSTextField* nativeChild = [[NSTextField alloc] initWithFrame:NSInsetRect(controlledView.bounds,
            NSWidth(controlledView.bounds) / 4, NSHeight(controlledView.bounds) / 4)];
        [controlledView addSubview:nativeChild positioned:NSWindowAbove relativeTo:child];
    }, "hit-test native child insertion cannot retain the parent's cursor");
    reset(); child.onHit = ^{ jalium_window_destroy(window); }; ResetCalls();
    jalium_window_set_cursor(window, JALIUM_CURSOR_HIDDEN);
    Check(NoCursorChange() && balance == 0, "hit-test destruction cannot revive the retained view's cursor");
    reinterpret_cast<void(*)(id, SEL)>([controlledView methodForSelector:restore])(controlledView, restore);
    Check(balance == 0, "reentrant private cleanup balances every hide");
    controlledView = nil; controlledWindow = nil; controlledEvent = nil;
}

int main()
{
    @autoreleasepool {
        [NSApplication sharedApplication];
        [NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited];
        Dl_info library{};
        if (dladdr(reinterpret_cast<const void*>(jalium_window_set_cursor), &library))
            std::printf("LOADED_PLATFORM %s\n", library.dli_fname);
        controlledSheet = [[NSPanel alloc] initWithContentRect:NSMakeRect(0, 0, 80, 40)
            styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];
        Replacement cursorSet(class_getInstanceMethod(NSCursor.class, @selector(set)), reinterpret_cast<IMP>(CursorSet));
        Replacement cursorHide(class_getClassMethod(NSCursor.class, @selector(hide)), reinterpret_cast<IMP>(CursorHide));
        Replacement cursorUnhide(class_getClassMethod(NSCursor.class, @selector(unhide)), reinterpret_cast<IMP>(CursorUnhide));
        Replacement cursorClear(class_getClassMethod(NSCursor.class, @selector(setHiddenUntilMouseMoves:)), reinterpret_cast<IMP>(CursorClear));
        Replacement current(class_getClassMethod(NSCursor.class, @selector(currentCursor)), reinterpret_cast<IMP>(CurrentCursor));
        Replacement mouse(class_getClassMethod(NSEvent.class, @selector(mouseLocation)), reinterpret_cast<IMP>(MouseLocation));
        Replacement buttons(class_getClassMethod(NSEvent.class, @selector(pressedMouseButtons)), reinterpret_cast<IMP>(MouseButtons));
        Replacement event(class_getInstanceMethod(NSApplication.class, @selector(currentEvent)), reinterpret_cast<IMP>(CurrentEvent));
        Replacement order(class_getInstanceMethod(NSWindow.class, @selector(orderFront:)), reinterpret_cast<IMP>(KeepHidden));
        Replacement drag(class_getInstanceMethod(NSView.class, @selector(beginDraggingSessionWithItems:event:source:)), reinterpret_cast<IMP>(RefuseDrag));
        Replacement hit(class_getClassMethod(NSWindow.class, @selector(windowNumberAtPoint:belowWindowWithWindowNumber:)), reinterpret_cast<IMP>(HitWindow));
        Replacement visibility(class_getInstanceMethod(NSWindow.class, @selector(isVisible)), reinterpret_cast<IMP>(Visible)); originalVisible = visibility.original;
        Replacement minimization(class_getInstanceMethod(NSWindow.class, @selector(isMiniaturized)), reinterpret_cast<IMP>(Miniaturized)); originalMiniaturized = minimization.original;
        Replacement activation(class_getInstanceMethod(NSApplication.class, @selector(isActive)), reinterpret_cast<IMP>(Active)); originalActive = activation.original;
        Replacement panel(class_getInstanceMethod(NSWindow.class, @selector(attachedSheet)), reinterpret_cast<IMP>(Sheet)); originalSheet = panel.original;
        Replacement rects(class_getInstanceMethod(NSWindow.class, @selector(invalidateCursorRectsForView:)), reinterpret_cast<IMP>(Invalidate)); originalInvalidate = rects.original;
        Probe(JALIUM_WINDOW_STYLE_DEFAULT, "native");
        Probe(JALIUM_WINDOW_STYLE_RESIZABLE, "custom");
        ProbeReentrant(JALIUM_WINDOW_STYLE_DEFAULT, "reentrant native");
        ProbeReentrant(JALIUM_WINDOW_STYLE_RESIZABLE, "reentrant custom");
        chrome = "final";
        Check(balance == 0, "all private hide requests are balanced");
        std::printf("macOS cursor ownership checks: %d/%d passed\n", passed, total);
        controlledSheet = nil;
        return passed == total ? 0 : 1;
    }
}
