#include "jalium_platform.h"

#import <AppKit/AppKit.h>
#import <QuartzCore/QuartzCore.h>
#import <objc/runtime.h>
#include <IOKit/hidsystem/IOLLEvent.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <functional>
#include <vector>
#include "platform_apple_foreground_validation.h"

static void Check(bool condition, const char* message)
{
    if (!condition) { std::fprintf(stderr, "FAIL: %s\n", message); std::abort(); }
}

static void Capture(const JaliumPlatformEvent* event, void* context)
{
    static_cast<std::vector<JaliumPlatformEvent>*>(context)->push_back(*event);
}

@interface JaliumWindowTestDocumentMenuTarget : NSObject
@property(nonatomic) NSUInteger closes;
- (void)closeDocument:(id)sender;
@end
@implementation JaliumWindowTestDocumentMenuTarget
- (void)closeDocument:(id)sender { ++_closes; }
@end

@interface JaliumWindowTestKeyboardSheet : NSPanel
@property(nonatomic) NSUInteger equivalents;
@property(nonatomic) NSUInteger keyEvents;
@property(nonatomic) NSEventType lastKeyType;
@property(nonatomic) NSInteger lastWindowNumber;
- (void)resetKeyEvents;
@end
@implementation JaliumWindowTestKeyboardSheet
- (BOOL)performKeyEquivalent:(NSEvent*)event { ++_equivalents; _lastWindowNumber = event.windowNumber; return YES; }
- (void)sendEvent:(NSEvent*)event {
    if (event.type == NSEventTypeKeyDown || event.type == NSEventTypeKeyUp ||
        event.type == NSEventTypeFlagsChanged) {
        ++_keyEvents;
        _lastKeyType = event.type;
        _lastWindowNumber = event.windowNumber;
        return;
    }
    [super sendEvent:event];
}
- (void)resetKeyEvents { _equivalents = 0; _keyEvents = 0; }
@end

struct TextDocument {
    JaliumPlatformWindow* window;
    NSString* text;
    NSRange selection;
    std::vector<JaliumPlatformEvent> events;
    bool reject = false;
};

static void PublishTextDocument(TextDocument& document)
{
    int32_t cursor = static_cast<int32_t>([[document.text substringToIndex:NSMaxRange(document.selection)]
        lengthOfBytesUsingEncoding:NSUTF8StringEncoding]);
    int32_t anchor = static_cast<int32_t>([[document.text substringToIndex:document.selection.location]
        lengthOfBytesUsingEncoding:NSUTF8StringEncoding]);
    jalium_window_update_ime_context(document.window, 1, document.text.UTF8String, cursor, anchor, 80, 120, 4, 36);
}

static void CaptureTextRequest(const JaliumPlatformEvent* event, void* context)
{
    auto& document = *static_cast<TextDocument*>(context);
    document.events.push_back(*event);
    if (event->type != JALIUM_EVENT_IME_TEXT_REQUEST) return;
    const auto& request = event->imeTextRequest;
    Check(request.applied != nullptr, "text request has a synchronous acknowledgement");
    if (document.reject) { *request.applied = -1; return; }
    NSRange range = NSMakeRange(request.start, request.length);
    Check(range.location <= document.text.length && range.length <= document.text.length - range.location,
        "text request uses valid document UTF-16 offsets");
    if (request.replace) {
        NSString* text = request.utf8Text ? [NSString stringWithUTF8String:request.utf8Text] : @"";
        document.text = [document.text stringByReplacingCharactersInRange:range withString:text];
        document.selection = NSMakeRange(range.location + text.length, 0);
    } else document.selection = range;
    PublishTextDocument(document);
    *request.applied = 1;
}

static bool WaitUntil(const std::function<bool()>& condition)
{
    NSDate* deadline = [NSDate dateWithTimeIntervalSinceNow:8];
    while (!condition() && deadline.timeIntervalSinceNow > 0) {
        jalium_platform_poll_events();
        [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.01]];
    }
    return condition();
}

static JaliumPlatformWindow* Create(uint32_t style, int x = 80, int y = 100)
{
    JaliumWindowParams params{};
    params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Jalium Window validation");
    params.x = x; params.y = y; params.width = 640; params.height = 480;
    params.style = style;
    auto* window = jalium_window_create(&params);
    Check(window != nullptr, "create window");
    return window;
}

static NSView* View(JaliumPlatformWindow* window)
{
    return (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(window));
}

static void CheckExtendedTitleBar(uint32_t style)
{
    auto* window = Create(style);
    NSView* view = View(window);
    NSWindow* native = view.window;
    [native makeFirstResponder:view];
    NSPoint topLeft = NSMakePoint(NSMinX(native.frame), NSMaxY(native.frame));
    NSSize client = view.bounds.size;
    NSButton* close = [native standardWindowButton:NSWindowCloseButton];
    NSPoint originalCenter = [close convertPoint:NSMakePoint(NSMidX(close.bounds), NSMidY(close.bounds)) toView:nil];
    CGFloat originalTop = NSHeight(native.frame) - originalCenter.y;
    for (int cycle = 0; cycle < 2; ++cycle) {
        Check(jalium_apple_window_set_titlebar_extended(window, 1) == JALIUM_OK, "enable extended native title bar");
        Check(jalium_apple_window_set_titlebar_extended(window, 1) == JALIUM_OK, "repeated extension is idempotent");
        Check(native.styleMask & NSWindowStyleMaskFullSizeContentView, "extended content uses AppKit full-size view");
        Check(native.styleMask & NSWindowStyleMaskTitled, "extended title bar remains decorated");
        Check(native.titlebarAppearsTransparent && native.titleVisibility == NSWindowTitleHidden,
            "application toolbar replaces native title background and duplicate title");
        Check(NSEqualSizes(view.bounds.size, client), "extension preserves client dimensions");
        Check(NSMinX(native.frame) == topLeft.x && NSMaxY(native.frame) == topLeft.y,
            "extension preserves window top-left position");
        Check(native.firstResponder == view, "extension preserves content focus");
        for (NSNumber* number in @[@(NSWindowCloseButton), @(NSWindowMiniaturizeButton), @(NSWindowZoomButton)]) {
            NSButton* button = [native standardWindowButton:static_cast<NSWindowButton>(number.unsignedIntegerValue)];
            Check(button && !button.hidden && button.enabled, "extended title bar retains native traffic lights");
        }
        for (double height : {31.0, 43.0, 63.0}) {
            Check(jalium_apple_window_set_titlebar_content_height(window, height) == JALIUM_OK,
                "update native toolbar content height");
            // AppKit relays out its title bar on resizing and theme changes.
            [native setFrame:NSMakeRect(native.frame.origin.x, native.frame.origin.y,
                native.frame.size.width + 20, native.frame.size.height) display:NO];
            [native layoutIfNeeded];
            for (NSNumber* number in @[@(NSWindowCloseButton), @(NSWindowMiniaturizeButton), @(NSWindowZoomButton)]) {
                NSButton* button = [native standardWindowButton:static_cast<NSWindowButton>(number.unsignedIntegerValue)];
                NSPoint center = [button convertPoint:NSMakePoint(NSMidX(button.bounds), NSMidY(button.bounds)) toView:view];
                Check(std::abs(center.y - height / 2) < 0.01, "native traffic lights align with toolbar content center");
                NSView* root = native.contentView.superview;
                NSPoint point = [button convertPoint:NSMakePoint(NSMidX(button.bounds), NSMidY(button.bounds)) toView:root.superview];
                NSView* hit = [root hitTest:point];
                Check(hit == button || [hit isDescendantOf:button], "aligned native traffic lights retain mouse hit targets");
            }
            [native setContentSize:client];
            [native setFrameTopLeftPoint:topLeft];
        }
        Check(jalium_apple_window_set_titlebar_extended(window, 0) == JALIUM_OK, "restore standard native title bar");
        Check(!(native.styleMask & NSWindowStyleMaskFullSizeContentView) && !native.titlebarAppearsTransparent &&
            native.titleVisibility == NSWindowTitleVisible, "disabling extension restores native presentation");
        Check(NSEqualSizes(view.bounds.size, client), "restoring title bar preserves client dimensions");
        close = [native standardWindowButton:NSWindowCloseButton];
        NSPoint center = [close convertPoint:NSMakePoint(NSMidX(close.bounds), NSMidY(close.bounds)) toView:nil];
        Check(std::abs(NSHeight(native.frame) - center.y - originalTop) < 0.01,
            "standard title bar restores AppKit button placement");
    }
    Check(jalium_apple_window_set_titlebar_extended(window, 2) == JALIUM_ERROR_INVALID_ARGUMENT,
        "reject invalid title bar extension value");
    Check(jalium_apple_window_set_titlebar_content_height(window, 0) == JALIUM_ERROR_INVALID_ARGUMENT &&
        jalium_apple_window_set_titlebar_content_height(window, NAN) == JALIUM_ERROR_INVALID_ARGUMENT,
        "reject invalid native title bar content heights");
    jalium_window_destroy(window);
    auto* overlay = Create(JALIUM_WINDOW_STYLE_BORDERLESS);
    Check(jalium_apple_window_set_titlebar_extended(overlay, 1) == JALIUM_OK, "borderless extension request is harmless");
    Check(!(View(overlay).window.styleMask & NSWindowStyleMaskFullSizeContentView), "borderless overlays retain their frame");
    jalium_window_destroy(overlay);
}

// AppKit may constrain an unshown maximized frame during orderFront. Inject
// that resize in this owned window only, independent of the desktop's Dock,
// Stage Manager, or display geometry; other applications are never touched.
static NSWindow* g_constrainedShowWindow = nil;
static IMP g_originalOrderFront = nullptr;
static void ConstrainedOrderFront(id object, SEL selector, id sender)
{
    NSWindow* window = object;
    if (window == g_constrainedShowWindow) {
        NSRect frame = window.frame;
        frame.origin.x += 40;
        frame.size.width -= 40;
        [window setFrame:frame display:NO];
    }
    reinterpret_cast<void (*)(id, SEL, id)>(g_originalOrderFront)(object, selector, sender);
}

static void CheckPreShowMaximize(uint32_t style)
{
    auto* window = Create(style);
    NSWindow* native = View(window).window;
    NSRect normal = native.frame;
    std::vector<JaliumPlatformEvent> events;
    jalium_window_set_event_callback(window, Capture, &events);
    jalium_window_set_state(window, JALIUM_WINDOW_STATE_MAXIMIZED);
    Class nativeClass = object_getClass(native);
    Method inherited = class_getInstanceMethod(nativeClass, @selector(orderFront:));
    g_originalOrderFront = method_getImplementation(inherited);
    // Keep NSWindow's original class: AppKit's dynamic-property dispatch relies
    // on that identity. The temporary override affects this test process only.
    class_addMethod(nativeClass, @selector(orderFront:), g_originalOrderFront, method_getTypeEncoding(inherited));
    Method method = class_getInstanceMethod(nativeClass, @selector(orderFront:));
    method_setImplementation(method, reinterpret_cast<IMP>(ConstrainedOrderFront));
    g_constrainedShowWindow = native;
    events.clear();
    jalium_apple_window_show(window, 0);
    g_constrainedShowWindow = nil;
    method_setImplementation(method, g_originalOrderFront);
    Check(jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MAXIMIZED,
        "show-time frame constraint preserves pre-show maximized state");
    Check(std::none_of(events.begin(), events.end(), [](const auto& event) {
        return event.type == JALIUM_EVENT_RESIZE && event.resize.isUserInitiated;
    }), "show-time frame constraint is not a user resize");
    int x, y, width, height;
    Check(jalium_apple_window_get_restore_bounds(window, &x, &y, &width, &height) == 0 &&
        width == 640 && height == 480, "show-time frame constraint preserves initial normal restore size");
    jalium_window_set_state(window, JALIUM_WINDOW_STATE_NORMAL);
    Check(NSEqualRects(native.frame, normal), "restore after constrained show returns to the original frame");
    jalium_window_destroy(window);
    std::puts("Pre-show maximize: constrained orderFront preserves state, resize origin and normal restore frame");
}

static size_t Count(const std::vector<JaliumPlatformEvent>& events, JaliumEventType type)
{
    return std::count_if(events.begin(), events.end(), [=](auto& event) { return event.type == type; });
}

struct GeometryDocument {
    TextDocument document;
    int32_t kind = -1, start = -1, length = -1;
    float x = 0, y = 0;
    int32_t character = 4;
    bool reject = false, rejectMarkedPoint = false, invalidRectangle = false;
    int count = 0;
};

static void CaptureTextGeometry(const JaliumPlatformEvent* event, void* context)
{
    auto& capture = *static_cast<GeometryDocument*>(context);
    if (event->type != JALIUM_EVENT_IME_GEOMETRY_REQUEST) {
        CaptureTextRequest(event, &capture.document);
        return;
    }
    const auto& query = event->imeGeometryRequest;
    Check(query.result != nullptr, "geometry result is callback-owned");
    capture.kind = query.kind; capture.start = query.start; capture.length = query.length;
    capture.x = query.x; capture.y = query.y; ++capture.count;
    auto& result = *query.result;
    if (capture.reject || (query.kind == 3 && capture.rejectMarkedPoint)) { result.handled = -1; return; }
    result.handled = 1; result.start = query.start;
    result.length = std::min(query.length, query.kind == 0 && query.start < 7 ? 7 - query.start : query.length);
    result.characterIndex = query.kind == 3 ? 1 : capture.character;
    result.x = capture.invalidRectangle ? NAN : 100;
    result.y = query.kind >= 2 ? 200 : 140;
    result.width = query.length == 0 ? 0 : 30;
    result.height = 36;
}

static void CheckImeTextGeometry(uint32_t style)
{
    static_assert(sizeof(JaliumImeGeometryResult) == 32);
    static_assert(sizeof(JaliumPlatformEvent) == 72, "geometry requests retain the event ABI");
    auto* window = Create(style);
    NSView* view = View(window);
    NSWindow* native = view.window;
    id<NSTextInputClient> client = (id<NSTextInputClient>)view;
    GeometryDocument capture{{window, @"ab😀cd\nef", NSMakeRange(2, 2)}};
    jalium_window_set_event_callback(window, CaptureTextGeometry, &capture);
    PublishTextDocument(capture.document);
    CGFloat scale = native.backingScaleFactor;
    auto screen = [&](CGFloat x, CGFloat y, CGFloat width, CGFloat height) {
        return [native convertRectToScreen:[view convertRect:NSMakeRect(x / scale, y / scale,
            width / scale, height / scale) toView:nil]];
    };
    NSRange actual;
    NSRect rectangle = [client firstRectForCharacterRange:NSMakeRange(0, 9) actualRange:&actual];
    Check(NSEqualRects(rectangle, screen(100, 140, 30, 36)) && NSEqualRanges(actual, NSMakeRange(0, 7)),
        "range geometry converts Retina/flipped rectangles and returns the first line fragment");
    [client firstRectForCharacterRange:NSMakeRange(3, 1) actualRange:&actual];
    Check(capture.start == 2 && capture.length == 2 && NSEqualRanges(actual, NSMakeRange(2, 2)),
        "range geometry never splits a surrogate pair");
    rectangle = [client firstRectForCharacterRange:NSMakeRange(4, 0) actualRange:&actual];
    Check(rectangle.size.width == 0 && NSEqualRanges(actual, NSMakeRange(4, 0)), "insertion rectangles have zero width");
    NSPoint point = NSMakePoint(118 / scale, 158 / scale);
    NSPoint onScreen = [native convertPointToScreen:[view convertPoint:point toView:nil]];
    Check([client characterIndexForPoint:onScreen] == 4 && capture.kind == 1 &&
        std::abs(capture.x - 118) < 0.01 && std::abs(capture.y - 158) < 0.01,
        "point geometry maps screen points to client physical pixels");
    Check(NSEqualRanges(capture.document.selection, NSMakeRange(2, 2)) && [capture.document.text isEqual:@"ab😀cd\nef"],
        "geometry queries preserve document and selection");

    [client setMarkedText:@"pin" selectedRange:NSMakeRange(1, 0) replacementRange:NSMakeRange(NSNotFound, 0)];
    rectangle = [client firstRectForCharacterRange:NSMakeRange(3, 1) actualRange:&actual];
    Check(capture.kind == 2 && capture.start == 1 && capture.length == 1 &&
        NSEqualRanges(actual, NSMakeRange(3, 1)) && NSEqualRects(rectangle, screen(100, 200, 30, 36)),
        "marked geometry uses provisional offsets and maps the actual range back to virtual storage");
    [client firstRectForCharacterRange:NSMakeRange(0, 5) actualRange:&actual];
    Check(capture.kind == 0 && capture.start == 0 && capture.length == 2 &&
        NSEqualRanges(actual, NSMakeRange(0, 2)), "geometry before marked text stops at the provisional boundary");
    [client firstRectForCharacterRange:NSMakeRange(5, 2) actualRange:&actual];
    Check(capture.kind == 0 && capture.start == 4 && NSEqualRanges(actual, NSMakeRange(5, 2)),
        "geometry after marked text adjusts for the original replaced length");
    [client firstRectForCharacterRange:NSMakeRange(5, 0) actualRange:&actual];
    Check(capture.kind == 2 && capture.start == 3 && actual.location == 5,
        "the insertion point at the marked-text end stays in the provisional layout");
    Check([client characterIndexForPoint:onScreen] == 3 && capture.kind == 3,
        "point queries prefer provisional text over the preserved original selection");
    capture.rejectMarkedPoint = true;
    Check([client characterIndexForPoint:onScreen] == 5 && capture.kind == 1,
        "committed point hits after a composition map to virtual indices");
    capture.character = 3;
    Check([client characterIndexForPoint:onScreen] == NSNotFound,
        "characters hidden by the provisional replacement are not reported as virtual text");

    capture.reject = true;
    Check(NSEqualRects([client firstRectForCharacterRange:NSMakeRange(0, 1) actualRange:&actual], NSZeroRect) &&
        actual.location == NSNotFound && [client characterIndexForPoint:onScreen] == NSNotFound,
        "rejected queries report no geometry and no index");
    capture.reject = false; capture.invalidRectangle = true;
    Check(NSEqualRects([client firstRectForCharacterRange:NSMakeRange(0, 1) actualRange:&actual], NSZeroRect) &&
        actual.location == NSNotFound, "non-finite layout responses are rejected");
    int before = capture.count;
    jalium_window_update_ime_context(window, 0, nullptr, 0, 0, 0, 0, 0, 0);
    Check(NSEqualRects([client firstRectForCharacterRange:NSMakeRange(0, 0) actualRange:&actual], NSZeroRect) &&
        [client characterIndexForPoint:onScreen] == NSNotFound && capture.count == before,
        "disabled geometry queries never call into managed input");
    jalium_window_destroy(window);
}

@interface JaliumWindowTestKeyEvent : NSEvent
@property(nonatomic, copy) NSString* unmodifiedCharacters;
@property(nonatomic, copy) NSString* shiftedCharacters;
@property(nonatomic) unsigned short testKeyCode;
@property(nonatomic) NSEventModifierFlags testFlags;
@end
@implementation JaliumWindowTestKeyEvent
- (NSString*)charactersIgnoringModifiers { return self.shiftedCharacters ?: self.unmodifiedCharacters; }
- (NSString*)charactersByApplyingModifiers:(NSEventModifierFlags)modifiers {
    Check(modifiers == 0, "virtual-key translation removes all text modifiers");
    return self.unmodifiedCharacters;
}
- (unsigned short)keyCode { return self.testKeyCode; }
- (NSEventModifierFlags)modifierFlags { return self.testFlags; }
- (BOOL)isARepeat { return NO; }
@end

@interface JaliumWindowTestMouseEvent : NSEvent
@property(nonatomic) NSInteger testButton;
@end
@implementation JaliumWindowTestMouseEvent
- (NSInteger)buttonNumber { return self.testButton; }
- (NSPoint)locationInWindow { return NSMakePoint(10, 20); }
- (NSEventModifierFlags)modifierFlags { return 0; }
- (NSInteger)clickCount { return 1; }
@end

@interface JaliumWindowTestZoomDelegate : NSObject<NSWindowDelegate>
@property(nonatomic) NSRect standardFrame;
@property(nonatomic, strong) id<NSWindowDelegate> originalDelegate;
@end
@implementation JaliumWindowTestZoomDelegate
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

struct CaptionEvents {
    JaliumPlatformWindow* window;
    std::vector<JaliumPlatformEvent> events;
    bool restoreDuringZoom = false;
    int restoreWidthDuringNormal = 0, restoreHeightDuringNormal = 0;
};
static void CaptureCaption(const JaliumPlatformEvent* event, void* context)
{
    auto& captured = *static_cast<CaptionEvents*>(context);
    captured.events.push_back(*event);
    if (event->type == JALIUM_EVENT_STATE_CHANGED && event->stateChanged.newState == JALIUM_WINDOW_STATE_NORMAL) {
        int x, y;
        Check(jalium_apple_window_get_restore_bounds(captured.window, &x, &y,
            &captured.restoreWidthDuringNormal, &captured.restoreHeightDuringNormal) == 0,
            "normal-state callbacks can query the pending restore geometry");
    }
    if (captured.restoreDuringZoom && event->type == JALIUM_EVENT_STATE_CHANGED &&
        event->stateChanged.newState == JALIUM_WINDOW_STATE_MAXIMIZED)
        jalium_window_set_state(captured.window, JALIUM_WINDOW_STATE_NORMAL);
}

static void CheckTitleBarPreferences(uint32_t style)
{
    auto* caption = Create((style & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS);
    CaptionEvents captured{caption};
    jalium_window_set_event_callback(caption, CaptureCaption, &captured);
    NSWindow* native = View(caption).window;
    NSRect normal = native.frame;
    NSUserDefaults* defaults = NSUserDefaults.standardUserDefaults;
    NSDictionary* savedArguments = [defaults volatileDomainForName:NSArgumentDomain];
    auto setAction = [&](id action) {
        NSMutableDictionary* arguments = [savedArguments mutableCopy];
        arguments[@"AppleActionOnDoubleClick"] = action;
        // Only this test process sees the overrides; no global setting changes.
        [defaults setVolatileDomain:arguments forName:NSArgumentDomain];
    };
    for (id action in @[@"None", @"unknown-future-action", @42]) {
        setAction(action);
        Check(jalium_apple_window_titlebar_double_click(caption) == 0 &&
            jalium_window_get_state(caption) == JALIUM_WINDOW_STATE_NORMAL && NSEqualRects(native.frame, normal),
            "none, unknown and malformed title-bar preferences leave the frame unchanged");
    }
    setAction(@"Fill");
    Check(jalium_apple_window_titlebar_double_click(caption) == 0 &&
        jalium_window_get_state(caption) == JALIUM_WINDOW_STATE_MAXIMIZED &&
        NSEqualRects(native.frame, native.screen.visibleFrame), "Fill uses the screen work area for custom chrome");
    Check(jalium_apple_window_titlebar_double_click(caption) == 0 &&
        jalium_window_get_state(caption) == JALIUM_WINDOW_STATE_NORMAL && NSEqualRects(native.frame, normal),
        "Fill toggles back to the original user frame");

    id<NSWindowDelegate> originalDelegate = native.delegate;
    auto* bestFit = [JaliumWindowTestZoomDelegate new];
    bestFit.originalDelegate = originalDelegate;
    bestFit.standardFrame = NSMakeRect(120, NSMaxY(native.screen.visibleFrame) - 360, 400, 300);
    native.delegate = bestFit;
    setAction(@"Maximize"); // This is the stored preference for the UI's "Zoom".
    captured.events.clear();
    Check(jalium_apple_window_titlebar_double_click(caption) == 0 &&
        jalium_window_get_state(caption) == JALIUM_WINDOW_STATE_MAXIMIZED &&
        NSEqualRects(native.frame, bestFit.standardFrame), "Zoom honors AppKit's delegate standard frame");
    auto stateEvent = std::find_if(captured.events.begin(), captured.events.end(), [](const auto& event) {
        return event.type == JALIUM_EVENT_STATE_CHANGED;
    });
    auto resizeEvent = std::find_if(captured.events.begin(), captured.events.end(), [](const auto& event) {
        return event.type == JALIUM_EVENT_RESIZE;
    });
    Check(stateEvent != captured.events.end() && resizeEvent != captured.events.end() && stateEvent < resizeEvent,
        "AppKit zoom publishes state before the managed resize");
    int x, y, width, height;
    Check(jalium_apple_window_get_restore_bounds(caption, &x, &y, &width, &height) == 0 &&
        x == 80 && y == 100 && width == 640 && height == 480, "Zoom preserves original normal client geometry");
    Check(jalium_apple_window_titlebar_double_click(caption) == 0 &&
        NSEqualRects(native.frame, normal), "Zoom restores the original frame");
    captured.restoreDuringZoom = true;
    Check(jalium_apple_window_titlebar_double_click(caption) == 0 &&
        jalium_window_get_state(caption) == JALIUM_WINDOW_STATE_NORMAL && NSEqualRects(native.frame, normal),
        "a restore requested by the zoom state callback waits for AppKit to finish");
    captured.restoreDuringZoom = false;
    native.delegate = originalDelegate;
    setAction(@"Fill");
    captured.restoreDuringZoom = true;
    Check(jalium_apple_window_titlebar_double_click(caption) == 0 &&
        jalium_window_get_state(caption) == JALIUM_WINDOW_STATE_NORMAL && NSEqualRects(native.frame, normal),
        "a restore requested during Fill waits for the maximizing frame to complete");
    Check(captured.restoreWidthDuringNormal == 640 && captured.restoreHeightDuringNormal == 480,
        "restore notification queries the original normal client size before its frame is applied");
    captured.restoreDuringZoom = false;
    NSMenuItem* zoomItem = [[NSMenuItem alloc] initWithTitle:@"Zoom" action:@selector(zoom:) keyEquivalent:@""];
    NSMenuItem* minimizeItem = [[NSMenuItem alloc] initWithTitle:@"Minimize" action:@selector(performMiniaturize:) keyEquivalent:@"m"];
    NSMenuItem* fullScreenItem = [[NSMenuItem alloc] initWithTitle:@"Full Screen" action:@selector(toggleFullScreen:) keyEquivalent:@"f"];
    NSMenuItem* closeItem = [[NSMenuItem alloc] initWithTitle:@"Close" action:@selector(performClose:) keyEquivalent:@"w"];
    Check([native validateMenuItem:zoomItem] && [native validateMenuItem:minimizeItem] &&
        [native validateMenuItem:fullScreenItem] && [native validateMenuItem:closeItem],
        "regular custom window exposes its native menu actions");
    Check(jalium_apple_window_set_style(caption,
        JALIUM_WINDOW_STYLE_BORDERLESS | JALIUM_WINDOW_STYLE_CLOSABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE) == 0 &&
        ![native validateMenuItem:zoomItem] && ![native validateMenuItem:fullScreenItem] &&
        [native validateMenuItem:minimizeItem] && [native validateMenuItem:closeItem],
        "minimize-only custom window disables resizing and fullscreen menu actions");
    Check(jalium_apple_window_set_style(caption, JALIUM_WINDOW_STYLE_BORDERLESS) == 0, "fixed caption style");
    Check(![native validateMenuItem:zoomItem] && ![native validateMenuItem:minimizeItem] &&
        ![native validateMenuItem:fullScreenItem] && ![native validateMenuItem:closeItem],
        "fixed custom window disables unavailable native menu actions");
    for (NSString* action in @[@"Fill", @"Maximize", @"Minimize"]) {
        setAction(action);
        Check(jalium_apple_window_titlebar_double_click(caption) == 0 &&
            jalium_window_get_state(caption) == JALIUM_WINDOW_STATE_NORMAL && NSEqualRects(native.frame, normal),
            "title-bar preference respects missing resize and minimize capabilities");
    }
    jalium_window_set_enabled(caption, 0);
    Check(jalium_apple_window_titlebar_double_click(caption) != 0, "disabled title-bar action is rejected");
    Check(![native validateUserInterfaceItem:zoomItem] && ![native validateMenuItem:closeItem],
        "disabled native window rejects menu and toolbar actions");
    [defaults setVolatileDomain:savedArguments forName:NSArgumentDomain];
    jalium_window_destroy(caption);
}

@interface JaliumWindowTestTrackingMenu : NSMenu
@property(nonatomic) NSUInteger cancellations;
@end

@implementation JaliumWindowTestTrackingMenu
- (void)cancelTracking {
    ++_cancellations;
    [NSNotificationCenter.defaultCenter postNotificationName:NSMenuDidEndTrackingNotification object:self];
}
@end

static void CheckMenuTrackingEscape(uint32_t regular)
{
    auto* notifications = NSNotificationCenter.defaultCenter;
    for (uint32_t style : {regular, (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS}) {
        auto* window = Create(style);
        NSView* view = View(window);
        std::vector<JaliumPlatformEvent> events;
        jalium_window_set_event_callback(window, Capture, &events);
        auto escape = [&](NSEventType type, bool repeat = false) {
            return [NSEvent keyEventWithType:type location:NSZeroPoint modifierFlags:0 timestamp:0
                windowNumber:view.window.windowNumber context:nil characters:@"\x1b"
                charactersIgnoringModifiers:@"\x1b" isARepeat:repeat keyCode:0x35];
        };
        auto* menu = [[JaliumWindowTestTrackingMenu alloc] initWithTitle:@"Window"];
        [notifications postNotificationName:NSMenuDidBeginTrackingNotification object:menu];
        [view keyDown:escape(NSEventTypeKeyDown)];
        [view keyDown:escape(NSEventTypeKeyDown, true)];
        [view keyUp:escape(NSEventTypeKeyUp)];
        Check(menu.cancellations == 1 && events.empty(),
            "Escape dismisses a tracking native menu without leaking managed down/up or cancel actions");
        [view keyDown:escape(NSEventTypeKeyDown)];
        [view keyUp:escape(NSEventTypeKeyUp)];
        Check(events.size() == 2 && events[0].type == JALIUM_EVENT_KEY_DOWN &&
            events[1].type == JALIUM_EVENT_KEY_UP && events[0].key.keyCode == 0x1b,
            "the next Escape is delivered normally after native menu tracking ends");

        auto* submenu = [[JaliumWindowTestTrackingMenu alloc] initWithTitle:@"Move & Resize"];
        [notifications postNotificationName:NSMenuDidBeginTrackingNotification object:menu];
        [notifications postNotificationName:NSMenuDidBeginTrackingNotification object:submenu];
        [notifications postNotificationName:NSMenuDidEndTrackingNotification object:submenu];
        events.clear();
        [view keyDown:escape(NSEventTypeKeyDown)];
        [view keyUp:escape(NSEventTypeKeyUp)];
        Check(menu.cancellations == 2 && submenu.cancellations == 0 && events.empty(),
            "ending a submenu preserves Escape ownership by the still tracking parent menu");

        jalium_window_update_ime_context(window, 1, "original", 8, 8, 20, 30, 2, 24);
        id<NSTextInputClient> client = (id)view;
        [client setMarkedText:@"pin" selectedRange:NSMakeRange(3, 0)
            replacementRange:NSMakeRange(NSNotFound, 0)];
        [notifications postNotificationName:NSMenuDidBeginTrackingNotification object:menu];
        events.clear();
        [view keyDown:escape(NSEventTypeKeyDown)];
        [view keyUp:escape(NSEventTypeKeyUp)];
        Check(menu.cancellations == 3 && client.hasMarkedText && events.empty(),
            "menu Escape preserves the editor composition instead of cancelling or committing it");
        jalium_window_update_ime_context(window, 0, nullptr, 0, 0, 0, 0, 0, 0);
        jalium_window_destroy(window);
    }
}

static void CheckMenuClose(uint32_t regular)
{
    for (uint32_t style : {regular, (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS}) {
        auto* window = Create(style);
        NSWindow* native = View(window).window;
        std::vector<JaliumPlatformEvent> events;
        jalium_window_set_event_callback(window, Capture, &events);
        [native performClose:nil];
        Check(Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 1 &&
            Count(events, JALIUM_EVENT_DESTROYED) == 0,
            "Window menu Close requests a cancellable close for decorated and borderless windows");

        jalium_window_set_enabled(window, 0);
        [native performClose:nil];
        Check(Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 1,
            "Window menu Close respects a disabled window");
        jalium_window_set_enabled(window, 1);
        jalium_apple_window_set_style(window, style & ~JALIUM_WINDOW_STYLE_CLOSABLE);
        [native performClose:nil];
        Check(Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 1,
            "Window menu Close respects a missing close capability");
        jalium_apple_window_set_style(window, style);
        struct AcceptedClose {
            JaliumPlatformWindow* window;
            std::vector<JaliumPlatformEvent>* events;
        } accepted{window, &events};
        jalium_window_set_event_callback(window, [](const JaliumPlatformEvent* event, void* context) {
            auto& close = *static_cast<AcceptedClose*>(context);
            close.events->push_back(*event);
            if (event->type == JALIUM_EVENT_CLOSE_REQUESTED) jalium_window_destroy(close.window);
        }, &accepted);
        [native performClose:nil];
        Check(Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 2 &&
            Count(events, JALIUM_EVENT_DESTROYED) == 1,
            "Window menu Close safely accepts destruction inside the close callback");
        [native performClose:nil];
        Check(Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 2 &&
            Count(events, JALIUM_EVENT_DESTROYED) == 1,
            "late Window menu Close ignores a destroyed native window");
    }
}

static void CheckExternalFrameChanges(uint32_t regular)
{
    for (uint32_t style : {regular, (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS}) {
        auto* window = Create(style);
        NSWindow* native = View(window).window;
        std::vector<JaliumPlatformEvent> events;
        jalium_window_set_event_callback(window, Capture, &events);
        int x, y, width, height;
        Check(jalium_apple_window_get_restore_bounds(window, &x, &y, &width, &height) == 0 &&
            x == 80 && y == 100 && width == 640 && height == 480,
            "unmaximized windows expose their current normal client bounds");
        auto userResize = [](const auto& event) {
            return event.type == JALIUM_EVENT_RESIZE && event.resize.isUserInitiated;
        };
        jalium_window_resize(window, 720, 520);
        Check(Count(events, JALIUM_EVENT_RESIZE) > 0 &&
            std::none_of(events.begin(), events.end(), userResize),
            "framework resize keeps SizeToContent enabled");
        events.clear();
        jalium_window_move(window, 120, 140);
        Check(std::none_of(events.begin(), events.end(), userResize),
            "framework move does not classify a frame change as user resize");
        events.clear();
        jalium_window_set_min_max_size(window, 100, 100, 800, 600);
        Check(std::none_of(events.begin(), events.end(), userResize),
            "framework size constraints do not disable SizeToContent");
        jalium_window_set_min_max_size(window, 0, 0, 0, 0);

        events.clear();
        [native setContentSize:NSMakeSize(420, 320)];
        Check(!native.inLiveResize && std::any_of(events.begin(), events.end(), userResize),
            "external AppKit layout is a user resize without live resizing");
        Check(jalium_apple_window_get_restore_bounds(window, &x, &y, &width, &height) == 0 &&
            width == lround(420 * native.backingScaleFactor) && height == lround(320 * native.backingScaleFactor),
            "normal restore bounds follow external AppKit sizing");
        NSRect tiled = native.frame;
        events.clear();
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_MAXIMIZED);
        Check(std::none_of(events.begin(), events.end(), userResize),
            "managed maximization preserves SizeToContent");
        events.clear();
        [native setFrame:tiled display:NO];
        auto state = std::find_if(events.begin(), events.end(), [](const auto& event) {
            return event.type == JALIUM_EVENT_STATE_CHANGED && event.stateChanged.newState == JALIUM_WINDOW_STATE_NORMAL;
        });
        auto resize = std::find_if(events.begin(), events.end(), userResize);
        Check(jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL &&
            state != events.end() && resize != events.end() && state < resize,
            "external layout leaves maximization before publishing its user resize");
        events.clear();
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_MAXIMIZED);
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_NORMAL);
        Check(NSEqualRects(native.frame, tiled) && std::none_of(events.begin(), events.end(), userResize),
            "maximize and restore retain the latest externally sized normal frame");

        events.clear();
        jalium_apple_window_set_style(window, style ^ JALIUM_WINDOW_STYLE_TITLEBAR ^ JALIUM_WINDOW_STYLE_BORDERLESS);
        Check(std::none_of(events.begin(), events.end(), userResize),
            "runtime chrome changes do not disable SizeToContent");
        events.clear();
        [native.delegate windowDidChangeBackingProperties:[NSNotification notificationWithName:
            NSWindowDidChangeBackingPropertiesNotification object:native]];
        Check(Count(events, JALIUM_EVENT_DPI_CHANGED) == 1 &&
            std::none_of(events.begin(), events.end(), userResize),
            "backing-scale refresh does not disable SizeToContent");
        jalium_window_destroy(window);
    }
}

static void CheckRuntimeStyleFocus(uint32_t regular)
{
    const uint32_t custom = (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS;
    for (uint32_t initial : {regular, custom}) {
        auto* window = Create(initial);
        NSView* view = View(window);
        NSWindow* native = view.window;
        Check([native makeFirstResponder:view] && native.firstResponder == view,
            "style focus fixture cannot focus its content view");
        for (uint32_t style : {custom, regular, regular | JALIUM_WINDOW_STYLE_TOPMOST,
                 custom | JALIUM_WINDOW_STYLE_TRANSPARENT, regular}) {
            Check(jalium_apple_window_set_style(window, style) == JALIUM_OK,
                "runtime style focus update failed");
            Check(native.firstResponder == view,
                "runtime title bar or surface style change discarded the content first responder");
        }
        [native makeFirstResponder:nil];
        NSResponder* unfocused = native.firstResponder;
        jalium_apple_window_set_style(window, custom);
        Check(native.firstResponder == unfocused,
            "runtime style change stole focus from unfocused content");
        [native makeFirstResponder:view];
        jalium_window_set_enabled(window, 0);
        jalium_apple_window_set_style(window, regular);
        Check(native.firstResponder != view, "runtime style change focused a disabled window");
        jalium_window_destroy(window);
    }
}

static void CheckManagedChromeWindowMenu(uint32_t regular)
{
    NSMenu* original = NSApp.windowsMenu;
    NSMenu* menu = [[NSMenu alloc] initWithTitle:@"Window"];
    NSApp.windowsMenu = menu;
    auto* window = Create((regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS);
    jalium_window_set_title(window, reinterpret_cast<const JaliumUtf16Char*>(u"Jalium managed chrome menu"));
    auto count = [&](NSString* title) {
        NSUInteger result = 0;
        for (NSMenuItem* item in menu.itemArray) if ([item.title isEqualToString:title]) ++result;
        return result;
    };
    Check(count(@"Jalium managed chrome menu") == 0, "unshown window is absent from the Window menu");
    jalium_apple_window_show(window, 0);
    Check(count(@"Jalium managed chrome menu") == 1, "custom chrome window is registered in the Window menu");
    jalium_apple_window_show(window, 0);
    Check(count(@"Jalium managed chrome menu") == 1, "showing twice does not duplicate the window item");
    jalium_window_set_title(window, reinterpret_cast<const JaliumUtf16Char*>(u"Jalium menu renamed"));
    Check(count(@"Jalium managed chrome menu") == 0 && count(@"Jalium menu renamed") == 1,
        "changing the window title updates its menu entry");
    jalium_window_hide(window);
    Check(count(@"Jalium menu renamed") == 0, "hidden window is removed from the menu");
    jalium_apple_window_show(window, 0);
    Check(count(@"Jalium menu renamed") == 1, "reshown window returns to the menu");
    jalium_window_set_show_in_taskbar(window, 0);
    Check(count(@"Jalium menu renamed") == 0, "ShowInTaskbar=false removes the window item");
    jalium_window_set_show_in_taskbar(window, 1);
    Check(count(@"Jalium menu renamed") == 1, "ShowInTaskbar=true restores the window item");
    jalium_apple_window_set_style(window, regular);
    Check(count(@"Jalium menu renamed") == 1, "changing to a native frame does not duplicate the window item");
    jalium_window_destroy(window);
    Check(count(@"Jalium menu renamed") == 0, "destroying the native window removes its menu entry");
    NSApp.windowsMenu = original;
}

static void CheckAccessibilityActivationWrites(uint32_t style)
{
    auto* first = Create(style);
    auto* second = Create(style);
    NSWindow* selected = View(first).window;
    NSWindow* other = View(second).window;
    std::vector<JaliumPlatformEvent> events;
    jalium_window_set_event_callback(first, Capture, &events);
    jalium_apple_window_show(first, 1);
    jalium_window_activate(first);
    Check(WaitUntil([&] { return NSApp.active && selected.keyWindow && selected.mainWindow &&
        NSApp.keyWindow == selected && NSApp.mainWindow == selected; }),
        "AX selection checks require an active desktop window");
    events.clear();
    [selected setAccessibilityFocused:NO];
    [selected setAccessibilityMain:NO];
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    @try { [selected accessibilitySetValue:@NO forAttribute:NSAccessibilityFocusedAttribute]; }
    @catch (NSException*) { /* AXFocused=NO may be rejected by AppKit. */ }
    @try { [selected accessibilitySetValue:@NO forAttribute:NSAccessibilityMainAttribute]; }
    @catch (NSException*) { /* Declining a deselection must preserve native state. */ }
#pragma clang diagnostic pop
    jalium_platform_poll_events();
    [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.03]];
    Check(selected.keyWindow && selected.mainWindow && selected.isAccessibilityFocused &&
        selected.isAccessibilityMain && NSApp.keyWindow == selected && NSApp.mainWindow == selected &&
        Count(events, JALIUM_EVENT_DEACTIVATE) == 0,
        "AX false preserves actual key/main selection and activation");
    [selected setAccessibilityFocused:YES];
    [selected setAccessibilityMain:YES];
    jalium_platform_poll_events();
    [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.03]];
    Check(selected.keyWindow && selected.mainWindow && NSApp.keyWindow == selected &&
        NSApp.mainWindow == selected, "AX reselection keeps window and application state consistent");

    jalium_apple_window_show(second, 0);
    [other setAccessibilityMain:YES];
    Check(WaitUntil([&] { return other.mainWindow && !selected.mainWindow; }),
        "AX Main selects another document window");
    Check(selected.keyWindow && NSApp.keyWindow == selected && NSApp.mainWindow == other,
        "AX Main selection does not move keyboard focus");
    [selected setAccessibilityMain:YES];
    Check(WaitUntil([&] { return selected.mainWindow && !other.mainWindow; }),
        "AX Main restores the original document window");
    [other setAccessibilityFocused:YES];
    Check(WaitUntil([&] { return other.keyWindow && !selected.keyWindow; }),
        "AX Focused moves actual key selection");
    Check(NSApp.keyWindow == other && other.isAccessibilityFocused && !selected.isAccessibilityFocused &&
        Count(events, JALIUM_EVENT_DEACTIVATE) == 1,
        "AX Focused synchronizes getters, application key window and deactivation");
    [selected setAccessibilityFocused:YES];
    Check(WaitUntil([&] { return selected.keyWindow && selected.mainWindow && !other.keyWindow; }),
        "AX Focused restores the original actual key/main window");
    Check(NSApp.keyWindow == selected && NSApp.mainWindow == selected,
        "AX activation restoration retains application selection");
    Check(jalium_window_set_enabled(first, 0) == 0 && !selected.canBecomeKeyWindow &&
        (NSApp.keyWindow == selected) == selected.keyWindow,
        "disabling a selected window preserves application key consistency");
    Check(jalium_window_set_enabled(first, 1) == 0 && jalium_window_activate(first) == 0 &&
        WaitUntil([&] { return selected.keyWindow && selected.firstResponder == View(first); }),
        "reenabling restores the native input view and actual key selection");
    jalium_window_activate(second);
    Check(WaitUntil([&] { return other.keyWindow; }), "activate the other window before background reenable");
    Check(jalium_window_set_enabled(first, 0) == 0 && jalium_window_set_enabled(first, 1) == 0 &&
        other.keyWindow && NSApp.keyWindow == other,
        "reenabling an unfocused window preserves the other key window");
    jalium_window_destroy(second);
    jalium_window_destroy(first);
    std::printf("PASS %s: actual AX selection, declined false values and application consistency\n",
        style & JALIUM_WINDOW_STYLE_BORDERLESS ? "Custom" : "Native");
}

static void CheckDocumentMenuClose(uint32_t style)
{
    auto* window = Create(style);
    NSView* view = View(window);
    NSWindow* native = view.window;
    std::vector<JaliumPlatformEvent> events;
    jalium_window_set_event_callback(window, Capture, &events);
    NSEvent* closeKey = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint
        modifierFlags:NSEventModifierFlagCommand timestamp:0 windowNumber:native.windowNumber context:nil
        characters:@"w" charactersIgnoringModifiers:@"w" isARepeat:NO keyCode:13];
    NSMenu* savedMainMenu = NSApp.mainMenu;
    auto* target = [JaliumWindowTestDocumentMenuTarget new];
    auto* menu = [NSMenu new];
    auto* item = [[NSMenuItem alloc] initWithTitle:@"Close Document" action:@selector(closeDocument:) keyEquivalent:@"w"];
    item.target = target;
    item.keyEquivalentModifierMask = NSEventModifierFlagCommand;
    [menu addItem:item];
    NSApp.mainMenu = menu;
    Check([native performKeyEquivalent:closeKey] && target.closes == 1 && Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 0,
        "Command-W honors the application document menu without closing the window");
    [view keyDown:closeKey];
    Check(target.closes == 2 && Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 0,
        "responder-delivered Command-W also honors the document menu");
    jalium_window_set_enabled(window, 0);
    Check(![native performKeyEquivalent:closeKey] && target.closes == 2,
        "disabled windows cannot invoke document-close actions");
    jalium_window_set_enabled(window, 1);
    NSApp.mainMenu = [NSMenu new];
    Check([native performKeyEquivalent:closeKey] && Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 1,
        "Command-W falls back to cancellable window close without a document menu");
    [view keyDown:closeKey];
    Check(Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 2,
        "responder-delivered Command-W retains the same window fallback");
    for (NSUInteger flags : {static_cast<NSUInteger>(NSEventModifierFlagControl),
                            static_cast<NSUInteger>(NSEventModifierFlagControl | NSEventModifierFlagShift)}) {
        events.clear();
        NSEvent* tabKey = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint
            modifierFlags:flags timestamp:0 windowNumber:native.windowNumber context:nil
            characters:@"\t" charactersIgnoringModifiers:@"\t" isARepeat:NO keyCode:0x30];
        Check([native performKeyEquivalent:tabKey] && Count(events, JALIUM_EVENT_KEY_DOWN) == 1 &&
            events[0].key.keyCode == 0x09 && (events[0].key.modifiers & JALIUM_MOD_CTRL) &&
            !!(events[0].key.modifiers & JALIUM_MOD_SHIFT) == !!(flags & NSEventModifierFlagShift),
            "Control-Tab reaches managed focus navigation before AppKit native window tabs");
        jalium_window_set_enabled(window, 0);
        Check(![native performKeyEquivalent:tabKey] && Count(events, JALIUM_EVENT_KEY_DOWN) == 1,
            "disabled windows cannot dispatch Control-Tab focus navigation");
        jalium_window_set_enabled(window, 1);
    }
    NSApp.mainMenu = savedMainMenu;
    jalium_window_destroy(window);
}

static bool CheckSheetKeyboardRouting(uint32_t style)
{
    auto* window = Create(style);
    NSView* view = View(window);
    NSWindow* native = view.window;
    std::vector<JaliumPlatformEvent> events;
    jalium_window_set_event_callback(window, Capture, &events);
    jalium_apple_window_show(window, 0);
    auto* sheet = [[JaliumWindowTestKeyboardSheet alloc] initWithContentRect:NSMakeRect(0, 0, 320, 180)
        styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];
    [native beginSheet:sheet completionHandler:nil];
    Check(native.attachedSheet == sheet, "keyboard fixture attaches a native sheet");
    bool passed = true;
    auto verify = [&](bool value, const char* label) {
        if (!value) passed = false;
        std::printf("%s %s %s\n", value ? "PASS" : "FAIL",
            style & JALIUM_WINDOW_STYLE_BORDERLESS ? "Custom" : "Native", label);
    };
    struct Key { NSString* text; NSUInteger flags; unsigned short code; const char* label; };
    for (const auto& key : std::vector<Key>{
        {@"w", NSEventModifierFlagCommand, 13, "sheet Command-W"},
        {@"m", NSEventModifierFlagCommand, 46, "sheet Command-M"},
        {@"f", NSEventModifierFlagCommand | NSEventModifierFlagControl, 3, "sheet fullscreen shortcut"},
        {@"\t", NSEventModifierFlagControl, 0x30, "sheet Control-Tab"},
        {@"\t", NSEventModifierFlagControl | NSEventModifierFlagShift, 0x30, "sheet Control-Shift-Tab"},
        {@"\x1b", 0, 0x35, "sheet Escape equivalent"}}) {
        events.clear(); [sheet resetKeyEvents];
        NSEvent* event = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint
            modifierFlags:key.flags timestamp:0 windowNumber:native.windowNumber context:nil
            characters:key.text charactersIgnoringModifiers:key.text isARepeat:NO keyCode:key.code];
        BOOL handled = [native performKeyEquivalent:event];
        verify(handled && sheet.equivalents == 1 && sheet.lastWindowNumber == sheet.windowNumber &&
            events.empty() && !native.miniaturized,
            key.label);
    }
    for (NSEventType type : {NSEventTypeKeyDown, NSEventTypeKeyUp, NSEventTypeFlagsChanged}) {
        NSEvent* event = [NSEvent keyEventWithType:type location:NSZeroPoint
            modifierFlags:type == NSEventTypeFlagsChanged ? NSEventModifierFlagShift | NX_DEVICELSHIFTKEYMASK : 0
            timestamp:0 windowNumber:native.windowNumber context:nil characters:@"a"
            charactersIgnoringModifiers:@"a" isARepeat:NO keyCode:type == NSEventTypeFlagsChanged ? 0x38 : 0];
        events.clear(); [sheet resetKeyEvents];
        [native sendEvent:event];
        verify(sheet.keyEvents == 1 && sheet.lastKeyType == type && sheet.lastWindowNumber == sheet.windowNumber && events.empty(),
            type == NSEventTypeKeyDown ? "sheet owner keyDown" : type == NSEventTypeKeyUp ?
                "sheet owner keyUp" : "sheet owner modifier event");
        events.clear(); [sheet resetKeyEvents];
        if (type == NSEventTypeKeyDown) [view keyDown:event];
        else if (type == NSEventTypeKeyUp) [view keyUp:event];
        else [view flagsChanged:event];
        verify(sheet.keyEvents == 1 && sheet.lastKeyType == type && sheet.lastWindowNumber == sheet.windowNumber && events.empty(),
            type == NSEventTypeKeyDown ? "sheet responder keyDown" : type == NSEventTypeKeyUp ?
                "sheet responder keyUp" : "sheet responder modifier event");
    }
    NSEvent* close = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint
        modifierFlags:NSEventModifierFlagCommand timestamp:0 windowNumber:native.windowNumber context:nil
        characters:@"w" charactersIgnoringModifiers:@"w" isARepeat:NO keyCode:13];
    jalium_window_set_enabled(window, 0);
    events.clear(); [sheet resetKeyEvents];
    verify(![native performKeyEquivalent:close] && sheet.equivalents == 0 && events.empty(),
        "disabled owner does not dispatch an equivalent");
    [native sendEvent:close];
    verify(sheet.keyEvents == 0 && events.empty(), "disabled owner does not send a sheet key");
    [view keyDown:close]; [view keyUp:close]; [view flagsChanged:close];
    verify(sheet.keyEvents == 0 && events.empty(), "disabled responder does not dispatch sheet input");
    jalium_window_set_enabled(window, 1);
    [native endSheet:sheet returnCode:NSModalResponseCancel];
    [sheet orderOut:nil];
    Check(WaitUntil([&] { return native.attachedSheet == nil; }), "keyboard fixture detaches its sheet");
    events.clear(); [sheet resetKeyEvents];
    verify([native performKeyEquivalent:close] && Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 1 &&
        sheet.equivalents == 0, "detached sheet restores document shortcut routing");
    events.clear();
    NSEvent* key = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint modifierFlags:0
        timestamp:0 windowNumber:native.windowNumber context:nil characters:@"a"
        charactersIgnoringModifiers:@"a" isARepeat:NO keyCode:0];
    [view keyDown:key];
    verify(Count(events, JALIUM_EVENT_KEY_DOWN) == 1 && sheet.keyEvents == 0,
        "detached sheet restores document text input");
    jalium_window_destroy(window);
    return passed;
}

int main(int argc, char** argv)
{
    bool propertiesOnly = argc == 2 && std::strcmp(argv[1], "--properties-only") == 0;
    bool menuTrackingOnly = argc == 2 && std::strcmp(argv[1], "--menu-tracking-only") == 0;
    bool documentMenuOnly = argc == 2 && std::strcmp(argv[1], "--document-menu-only") == 0;
    bool sheetKeyboardOnly = argc == 2 && std::strcmp(argv[1], "--sheet-keyboard-only") == 0;
    Check(argc == 1 || propertiesOnly || menuTrackingOnly || documentMenuOnly || sheetKeyboardOnly, "supported validation mode");
    @autoreleasepool {
        Check(jalium_platform_init() == JALIUM_OK, "initialize platform");
        bool foregroundHost = UsesForegroundValidationHost();
        [NSApp setActivationPolicy:foregroundHost ? NSApplicationActivationPolicyRegular : NSApplicationActivationPolicyAccessory];
        [NSApp finishLaunching];
        if (!documentMenuOnly && !sheetKeyboardOnly && foregroundHost && !AwaitForegroundValidation(@"Window 生命周期 · 前台回归")) {
            jalium_platform_shutdown();
            return 3;
        }
        constexpr uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
            JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE;
        if (sheetKeyboardOnly) {
            bool passed = CheckSheetKeyboardRouting(regular);
            passed = CheckSheetKeyboardRouting(JALIUM_WINDOW_STYLE_BORDERLESS | JALIUM_WINDOW_STYLE_CLOSABLE |
                JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE) && passed;
            jalium_platform_shutdown();
            return passed ? 0 : 1;
        }
        if (documentMenuOnly) {
            CheckDocumentMenuClose(regular);
            jalium_platform_shutdown();
            std::puts("macOS native document-menu and Control-Tab tests passed");
            return 0;
        }
        if (menuTrackingOnly) {
            CheckMenuTrackingEscape(regular);
            jalium_platform_shutdown();
            std::puts("macOS native menu tracking Escape tests passed");
            return 0;
        }
        CheckExtendedTitleBar(regular);
        auto* window = Create(regular);
        NSView* view = View(window);
        NSWindow* native = view.window;
        std::vector<JaliumPlatformEvent> events;
        jalium_window_set_event_callback(window, Capture, &events);
        int width, height, x, y;
        jalium_window_get_client_size(window, &width, &height);
        Check(width == 640 && height == 480, "Retina client dimensions");
        jalium_window_get_position(window, &x, &y);
        Check(x == 80 && y == 100, "initial top-left position");
        jalium_window_move(window, 140, 180);
        jalium_window_get_position(window, &x, &y);
        Check(x == 140 && y == 180, "move and position use the same coordinate space");
        jalium_window_resize(window, 720, 520);
        jalium_window_get_client_size(window, &width, &height);
        Check(width == 720 && height == 520, "resize publishes actual client dimensions");
        Check(Count(events, JALIUM_EVENT_RESIZE) > 0, "resize callback");

        JaliumMonitorInfo monitor{};
        Check(jalium_platform_get_monitor_info(0, &monitor) == JALIUM_OK, "monitor information");
        NSRect visible = NSScreen.screens.firstObject.visibleFrame;
        CGFloat top = NSMaxY(NSScreen.screens.firstObject.frame);
        Check(monitor.workY == lround((top - NSMaxY(visible)) * monitor.scale), "work area uses top-left coordinates");

        auto originalSize = view.bounds.size;
        Check(jalium_apple_window_set_style(window, JALIUM_WINDOW_STYLE_BORDERLESS | JALIUM_WINDOW_STYLE_CLOSABLE) == 0,
            "switch to a fixed custom frame");
        Check(!(native.styleMask & NSWindowStyleMaskTitled), "remove native title bar");
        Check(!(native.styleMask & NSWindowStyleMaskResizable), "fixed window cannot resize");
        Check(NSEqualSizes(originalSize, view.bounds.size), "style change preserves client size");
        Check(jalium_apple_window_set_style(window, regular) == 0, "restore native frame");
        Check((native.styleMask & NSWindowStyleMaskTitled) != 0, "restore native title bar");
        view.wantsLayer = YES;
        Check(jalium_apple_window_set_style(window,
            regular | JALIUM_WINDOW_STYLE_TRANSPARENT | JALIUM_WINDOW_STYLE_TOPMOST) == 0,
            "runtime transparent and topmost style");
        Check(!native.opaque && !view.layer.opaque && native.backgroundColor.alphaComponent == 0 && !native.hasShadow,
            "transparent window and existing layer preserve alpha");
        Check(native.level == NSFloatingWindowLevel &&
            jalium_window_get_surface(window).kind == JALIUM_SURFACE_KIND_COMPOSITION_TARGET,
            "runtime style updates level and surface contract");
        jalium_apple_window_set_style(window, regular);
        Check(native.opaque && view.layer.opaque && native.backgroundColor.alphaComponent == 1 && native.hasShadow &&
            native.level == NSNormalWindowLevel && NSEqualSizes(originalSize, view.bounds.size),
            "opaque style restores background, shadow and level without resizing");
        Check(jalium_apple_window_set_style(window, JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
            JALIUM_WINDOW_STYLE_MINIMIZABLE) == 0, "minimize-only capabilities");
        Check([native standardWindowButton:NSWindowMiniaturizeButton].enabled, "minimize-only button enabled");
        Check(![native standardWindowButton:NSWindowZoomButton].enabled, "minimize-only zoom button disabled");
        jalium_apple_window_set_style(window, regular);

        NSResponder* responderBeforeBackdrop = native.firstResponder;
        Check(jalium_apple_window_set_system_backdrop(window, 1) == 0, "automatic AppKit backdrop");
        auto* effect = (NSVisualEffectView*)native.contentView.subviews.firstObject;
        Check([effect isKindOfClass:NSVisualEffectView.class] &&
            native.contentView.subviews.lastObject == view && view.window == native,
            "material is below the renderer in a shared content container");
        Check(effect.material == NSVisualEffectMaterialWindowBackground &&
            effect.blendingMode == NSVisualEffectBlendingModeBehindWindow &&
            effect.state == NSVisualEffectStateFollowsWindowActiveState,
            "material follows system appearance and window activation");
        bool reduced = NSWorkspace.sharedWorkspace.accessibilityDisplayShouldReduceTransparency;
        Check(effect.hidden == reduced && native.opaque == reduced && !view.layer.opaque && native.hasShadow,
            "backdrop preserves render alpha and respects reduced transparency");
        Check(NSEqualSizes(originalSize, view.bounds.size) && native.firstResponder == responderBeforeBackdrop &&
            jalium_window_get_surface(window).kind == JALIUM_SURFACE_KIND_COMPOSITION_TARGET,
            "material changes preserve geometry, responder and future surface alpha");
        jalium_window_resize(window, 800, 600);
        Check(NSEqualSizes(effect.frame.size, view.bounds.size), "material tracks the content size");
        jalium_window_resize(window, 720, 520);
        Check(jalium_apple_window_set_system_backdrop(window, 2) == 0 &&
            effect.material == NSVisualEffectMaterialWindowBackground, "Mica maps to window background material");
        Check(jalium_apple_window_set_system_backdrop(window, 3) == 0 &&
            effect.material == NSVisualEffectMaterialPopover, "Acrylic maps to popover material");
        Check(jalium_apple_window_set_system_backdrop(window, 4) == 0 &&
            effect.material == NSVisualEffectMaterialUnderWindowBackground &&
            native.contentView.subviews.count == 2, "Mica Alt updates the existing material view");
        Check(jalium_apple_window_set_system_backdrop(window, -1) != 0 &&
            jalium_apple_window_set_system_backdrop(window, 5) != 0, "invalid backdrop values are rejected");
        jalium_apple_window_set_style(window, regular);
        Check(!view.layer.opaque, "style update preserves active backdrop alpha");
        Check(jalium_apple_window_set_system_backdrop(window, 0) == 0 && effect.superview == nil &&
            native.contentView.subviews.count == 1 && native.opaque && view.layer.opaque &&
            jalium_window_get_surface(window).kind == JALIUM_SURFACE_KIND_NATIVE_WINDOW,
            "removing material restores the opaque surface contract");
        jalium_apple_window_set_style(window, regular | JALIUM_WINDOW_STYLE_TRANSPARENT);
        jalium_apple_window_set_system_backdrop(window, 3);
        jalium_apple_window_set_system_backdrop(window, 0);
        Check(!native.opaque && !view.layer.opaque && !native.hasShadow,
            "removing material preserves explicit window transparency");
        jalium_apple_window_set_style(window, regular);

        auto* owner = Create(regular, 20, 40);
        auto* other = Create(regular, 40, 60);
        Check(jalium_window_set_owner(window, jalium_window_get_native_handle(owner)) == 0, "assign owner");
        Check(native.parentWindow == View(owner).window, "native owner relationship");
        Check(jalium_window_set_owner(window, jalium_window_get_native_handle(other)) == 0, "reassign owner");
        Check(native.parentWindow == View(other).window && View(owner).window.childWindows.count == 0,
            "reassignment detaches previous owner");
        Check(jalium_window_set_owner(other, jalium_window_get_native_handle(window)) != 0, "reject ownership cycle");
        Check(jalium_window_set_owner(window, jalium_window_get_native_handle(window)) != 0, "reject self ownership");
        Check(jalium_window_set_owner(window, 0) == 0 && native.parentWindow == nil, "clear owner");

        Check(jalium_window_set_opacity(window, 0.45) == 0 && std::abs(native.alphaValue - 0.45) < 0.001,
            "whole-window opacity");
        jalium_window_set_opacity(window, 1);
        NSRect participationFrame = native.frame;
        size_t participationResizes = Count(events, JALIUM_EVENT_RESIZE);
        jalium_window_set_show_in_taskbar(window, 0);
        Check(native.excludedFromWindowsMenu && (native.collectionBehavior & NSWindowCollectionBehaviorIgnoresCycle),
            "hide from window menu and cycling");
        Check((native.collectionBehavior & NSWindowCollectionBehaviorFullScreenPrimary) &&
            !(native.collectionBehavior & NSWindowCollectionBehaviorFullScreenAuxiliary),
            "ShowInTaskbar=false preserves fullscreen eligibility for a regular window");
        Check(NSEqualRects(native.frame, participationFrame) &&
            Count(events, JALIUM_EVENT_RESIZE) == participationResizes,
            "changing Window-menu participation does not resize the window");
        jalium_apple_window_set_style(window, regular | JALIUM_WINDOW_STYLE_POPUP);
        Check((native.collectionBehavior & NSWindowCollectionBehaviorFullScreenAuxiliary) &&
            !(native.collectionBehavior & NSWindowCollectionBehaviorFullScreenPrimary),
            "popup windows can accompany a fullscreen primary window");
        jalium_apple_window_set_style(window, regular);
        jalium_window_set_show_in_taskbar(window, 1);
        Check(!native.excludedFromWindowsMenu && !(native.collectionBehavior & NSWindowCollectionBehaviorIgnoresCycle),
            "restore window participation");
        jalium_window_set_topmost(window, 1);
        Check(native.level == NSFloatingWindowLevel, "topmost level");
        jalium_window_set_topmost(window, 0);
        Check(native.level == NSNormalWindowLevel, "normal level");
        uint32_t pixels[] = {0xffff0000, 0xff00ff00, 0xff0000ff, 0x80ffffff};
        Check(jalium_window_set_icon(window, pixels, 2, 2) == 0 && native.miniwindowImage != nil, "miniwindow icon");
        auto* rep = (NSBitmapImageRep*)native.miniwindowImage.representations.firstObject;
        Check(rep.bitmapData[0] == 255 && rep.bitmapData[1] == 0 && rep.bitmapData[2] == 0,
            "BGRA icon channels");
        Check(jalium_window_set_icon(window, nullptr, 0, 0) == 0 && native.miniwindowImage == nil, "clear icon");

        TextDocument document{window, @"left中文😀right", NSMakeRange(4, 4)};
        jalium_window_set_event_callback(window, CaptureTextRequest, &document);
        PublishTextDocument(document);
        id<NSTextInputClient> client = (id)view;
        Check(NSEqualRanges(client.selectedRange, document.selection), "UTF-8 context selection becomes document UTF-16 range");
        NSRange actual;
        NSAttributedString* substring = [client attributedSubstringForProposedRange:NSMakeRange(7, 1) actualRange:&actual];
        Check([substring.string isEqualToString:@"😀"] && NSEqualRanges(actual, NSMakeRange(6, 2)),
            "substring queries preserve a full emoji grapheme");
        [client setMarkedText:@"pin" selectedRange:NSMakeRange(1, 1) replacementRange:NSMakeRange(NSNotFound, 0)];
        Check(NSEqualRanges(client.markedRange, NSMakeRange(4, 3)) &&
            NSEqualRanges(client.selectedRange, NSMakeRange(5, 1)), "marked and selected ranges use document offsets");
        Check([client.attributedString.string isEqualToString:@"leftpinright"] &&
            [document.text isEqualToString:@"left中文😀right"], "marked snapshot replaces selected text without committing it");
        [client insertText:@"拼音" replacementRange:NSMakeRange(NSNotFound, 0)];
        Check([document.text isEqualToString:@"left拼音right"] && !client.hasMarkedText &&
            NSEqualRanges(client.selectedRange, NSMakeRange(6, 0)), "composition commit replaces its original UTF-16 span once");
        [client insertText:@"😀" replacementRange:NSMakeRange(4, 2)];
        Check([document.text isEqualToString:@"left😀right"], "explicit range replacement can differ from the current caret");
        [client insertText:@"" replacementRange:NSMakeRange(4, 2)];
        Check([document.text isEqualToString:@"leftright"], "empty insertion deletes the requested range");
        document.reject = true;
        document.events.clear();
        [client insertText:@"rejected" replacementRange:NSMakeRange(NSNotFound, 0)];
        Check([document.text isEqualToString:@"leftright"] && Count(document.events, JALIUM_EVENT_CHAR_INPUT) == 0,
            "explicitly rejected requests never fall through to character insertion");
        document.reject = false;
        document.text = @"left中文right"; document.selection = NSMakeRange(4, 2); PublishTextDocument(document);
        [client setMarkedText:@"abc" selectedRange:NSMakeRange(3, 0) replacementRange:NSMakeRange(NSNotFound, 0)];
        [client setMarkedText:@"X" selectedRange:NSMakeRange(0, 0) replacementRange:NSMakeRange(5, 1)];
        Check([client.attributedString.string isEqualToString:@"leftaXcright"] &&
            NSEqualRanges(client.markedRange, NSMakeRange(5, 1)), "partial marked replacement preserves its prefix and suffix");
        [client insertText:@"Y" replacementRange:NSMakeRange(NSNotFound, 0)];
        Check([document.text isEqualToString:@"leftaYcright"], "re-anchored marked segment commits to its actual range");
        [client setMarkedText:@"pin" selectedRange:NSMakeRange(3, 0) replacementRange:NSMakeRange(NSNotFound, 0)];
        [client setMarkedText:@"" selectedRange:NSMakeRange(0, 0) replacementRange:NSMakeRange(NSNotFound, 0)];
        Check(!client.hasMarkedText && [document.text isEqualToString:@"leftaYcright"], "empty pre-edit cancels without deleting committed text");
        document.selection = NSMakeRange(4, 3); PublishTextDocument(document);
        document.events.clear();
        [client setMarkedText:@"pin" selectedRange:NSMakeRange(3, 0) replacementRange:NSMakeRange(NSNotFound, 0)];
        [client doCommandBySelector:@selector(cancelOperation:)];
        Check(!client.hasMarkedText && [document.text isEqualToString:@"leftaYcright"] &&
            NSEqualRanges(client.selectedRange, NSMakeRange(4, 3)) &&
            Count(document.events, JALIUM_EVENT_COMPOSITION_END) == 1 &&
            Count(document.events, JALIUM_EVENT_IME_TEXT_REQUEST) == 0,
            "AppKit cancellation discards pre-edit once while preserving committed text and selection");
        [client doCommandBySelector:@selector(cancelOperation:)];
        Check(Count(document.events, JALIUM_EVENT_COMPOSITION_END) == 1,
            "repeated AppKit cancellation has no duplicate composition end");
        [client setMarkedText:@"pin" selectedRange:NSMakeRange(3, 0) replacementRange:NSMakeRange(NSNotFound, 0)];
        NSEvent* shortcut = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint
            modifierFlags:NSEventModifierFlagCommand timestamp:0 windowNumber:native.windowNumber context:nil
            characters:@"k" charactersIgnoringModifiers:@"k" isARepeat:NO keyCode:40];
        document.events.clear(); [view keyDown:shortcut];
        Check(Count(document.events, JALIUM_EVENT_KEY_DOWN) == 1 &&
            std::any_of(document.events.begin(), document.events.end(), [](const auto& event) {
                return event.type == JALIUM_EVENT_KEY_DOWN && event.key.keyCode == 'K' &&
                    (event.key.modifiers & JALIUM_MOD_META);
            }), "application shortcuts reach managed focus routing during composition");
        [client doCommandBySelector:@selector(cancelOperation:)];
        document.text = [@"" stringByPaddingToLength:6000 withString:@"a" startingAtIndex:0];
        document.selection = NSMakeRange(5000, 0); PublishTextDocument(document);
        substring = [client attributedSubstringForProposedRange:NSMakeRange(4999, 3) actualRange:&actual];
        Check([substring.string isEqualToString:@"aaa"] && actual.location == 4999 && client.selectedRange.location == 5000,
            "long document queries keep absolute offsets beyond Wayland's surrounding-text limit");
        jalium_window_update_ime_context(window, 1, nullptr, 0, 0, 80, 120, 4, 36);
        Check([client attributedSubstringForProposedRange:NSMakeRange(0, 1) actualRange:&actual] == nil &&
            actual.location == NSNotFound, "private input context exposes no document text");
        jalium_window_set_event_callback(window, Capture, &events);

        NSResponder* responderBefore = native.firstResponder;
        Check(jalium_window_update_ime_context(window, 1, "a中", 4, 4, 80, 120, 4, 36) == 0,
            "update IME caret geometry");
        Check(native.firstResponder == responderBefore, "IME geometry updates do not steal focus");
        CGFloat scale = native.backingScaleFactor;
        NSRect expectedCaret = [native convertRectToScreen:[view convertRect:
            NSMakeRect(80 / scale, 120 / scale, 0, 36 / scale) toView:nil]];
        Check(NSEqualRects([client firstRectForCharacterRange:NSMakeRange(2, 0) actualRange:nil], expectedCaret),
            "IME caret rectangle accounts for Retina scale and flipped coordinates");
        events.clear();
        [client setMarkedText:@"pin" selectedRange:NSMakeRange(3, 0) replacementRange:NSMakeRange(NSNotFound, 0)];
        Check(client.hasMarkedText && Count(events, JALIUM_EVENT_COMPOSITION_START) == 1,
            "enabled IME starts composition");
        jalium_window_update_ime_context(window, 0, nullptr, 0, 0, 0, 0, 0, 0);
        Check(!client.hasMarkedText && view.inputContext == nil && Count(events, JALIUM_EVENT_COMPOSITION_END) == 1,
            "disabling IME cancels marked text and detaches text-input context");
        events.clear();
        [client setMarkedText:@"ignored" selectedRange:NSMakeRange(0, 0) replacementRange:NSMakeRange(NSNotFound, 0)];
        [client insertText:@"ignored" replacementRange:NSMakeRange(NSNotFound, 0)];
        Check(events.empty(), "disabled IME rejects late composition and commits");
        jalium_window_update_ime_context(window, 1, nullptr, 0, 0, 80, 120, 4, 36);
        [client insertText:@"中" replacementRange:NSMakeRange(NSNotFound, 0)];
        Check(Count(events, JALIUM_EVENT_CHAR_INPUT) == 1 && events.back().character.codepoint == 0x4e2d,
            "reenabling IME restores text input");

        jalium_window_update_ime_context(window, 0, nullptr, 0, 0, 0, 0, 0, 0);
        auto* keyboard = [JaliumWindowTestKeyEvent new];
        const struct { unichar character; int virtualKey; } keys[] = {
            {'-', 0xbd}, {'=', 0xbb}, {'[', 0xdb}, {']', 0xdd}, {'\\', 0xdc}, {';', 0xba},
            {'\'', 0xde}, {',', 0xbc}, {'.', 0xbe}, {'/', 0xbf}, {'`', 0xc0},
            {NSF1FunctionKey, 0x70}, {NSF12FunctionKey, 0x7b}, {NSF24FunctionKey, 0x87}
        };
        for (auto item : keys) {
            keyboard.unmodifiedCharacters = [NSString stringWithCharacters:&item.character length:1];
            events.clear();
            [view keyDown:keyboard]; [view keyUp:keyboard];
            Check(events.size() == 2 && events[0].key.keyCode == item.virtualKey && events[1].key.keyCode == item.virtualKey,
                "punctuation and function keys retain managed key identity");
        }
        const struct { unsigned short scanCode; unichar character; int virtualKey; } navigationKeys[] = {
            {0x73, NSHomeFunctionKey, 0x24}, {0x74, NSPageUpFunctionKey, 0x21},
            {0x75, NSDeleteFunctionKey, 0x2e}, {0x77, NSEndFunctionKey, 0x23},
            {0x79, NSPageDownFunctionKey, 0x22}, {0x7b, NSLeftArrowFunctionKey, 0x25},
            {0x7c, NSRightArrowFunctionKey, 0x27}, {0x7d, NSDownArrowFunctionKey, 0x28},
            {0x7e, NSUpArrowFunctionKey, 0x26},
            {0x63, NSF3FunctionKey, 0x72}, {0x6f, NSF12FunctionKey, 0x7b}
        };
        for (auto item : navigationKeys) {
            NSString* characters = [NSString stringWithCharacters:&item.character length:1];
            // Real AppKit translation with Function/NumericPad removed produces
            // control characters instead of navigation-key characters.
            NSEvent* navigation = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint
                modifierFlags:NSEventModifierFlagFunction | NSEventModifierFlagNumericPad | NSEventModifierFlagShift
                timestamp:0 windowNumber:native.windowNumber context:nil characters:characters
                charactersIgnoringModifiers:characters isARepeat:NO keyCode:item.scanCode];
            events.clear();
            [view keyDown:navigation]; [view keyUp:navigation];
            Check(events.size() == 2 && events[0].key.keyCode == item.virtualKey &&
                events[1].key.keyCode == item.virtualKey &&
                (events[0].key.modifiers & JALIUM_MOD_SHIFT) &&
                (events[1].key.modifiers & JALIUM_MOD_SHIFT),
                "navigation keys retain managed identity after AppKit modifier translation");
        }
        keyboard.unmodifiedCharacters = @"1"; keyboard.shiftedCharacters = @"!";
        keyboard.testFlags = NSEventModifierFlagShift;
        events.clear(); [view keyDown:keyboard];
        Check(events.back().key.keyCode == '1' && (events.back().key.modifiers & JALIUM_MOD_SHIFT),
            "Shift+1 remains a number key rather than PageUp");
        keyboard.testKeyCode = 0x58; keyboard.unmodifiedCharacters = @"6"; keyboard.testFlags = 0;
        events.clear(); [view keyDown:keyboard];
        Check(events.back().key.keyCode == 0x66, "numeric keypad preserves NumPad6 identity");
        keyboard.testKeyCode = 0x38;
        keyboard.testFlags = NSEventModifierFlagShift | NX_DEVICELSHIFTKEYMASK | NX_DEVICERSHIFTKEYMASK;
        events.clear(); [view flagsChanged:keyboard];
        keyboard.testFlags = NSEventModifierFlagShift | NX_DEVICERSHIFTKEYMASK;
        [view flagsChanged:keyboard];
        Check(events.size() == 2 && events[0].type == JALIUM_EVENT_KEY_DOWN && events[0].key.keyCode == 0xa0 &&
            events[1].type == JALIUM_EVENT_KEY_UP && (events[1].key.modifiers & JALIUM_MOD_SHIFT),
            "releasing left Shift while right Shift stays down produces the correct key-up");
        auto* mouseButtons = [JaliumWindowTestMouseEvent new];
        for (int button = 2; button <= 4; ++button) {
            mouseButtons.testButton = button;
            events.clear(); [view otherMouseDown:mouseButtons]; [view otherMouseUp:mouseButtons];
            Check(events.size() == 2 && events[0].mouse.button == button && events[1].mouse.button == button,
                "middle and side mouse buttons retain their identity");
        }
        mouseButtons.testButton = 5;
        events.clear(); [view otherMouseDown:mouseButtons];
        Check(events.empty(), "unsupported extra mouse buttons are ignored");
        jalium_window_update_ime_context(window, 1, nullptr, 0, 0, 80, 120, 4, 36);

        NSRect beforeTransition = native.frame;
        [native.delegate windowWillEnterFullScreen:[NSNotification notificationWithName:
            NSWindowWillEnterFullScreenNotification object:native]];
        [native setContentSize:NSMakeSize(view.bounds.size.width + 100, view.bounds.size.height + 80)];
        Check(jalium_apple_window_get_restore_bounds(window, &x, &y, &width, &height) == 0 &&
            x == 140 && y == 180 && width == 720 && height == 520,
            "fullscreen transition keeps normal geometry after native resize");
        [native.delegate windowDidFailToEnterFullScreen:native];
        [native setFrame:beforeTransition display:NO];

        jalium_window_set_enabled(window, 0);
        events.clear();
        NSEvent* key = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint modifierFlags:0
            timestamp:0 windowNumber:native.windowNumber context:nil characters:@"a"
            charactersIgnoringModifiers:@"a" isARepeat:NO keyCode:0];
        [view keyDown:key]; [view keyUp:key]; [(id)view insertText:@"中" replacementRange:NSMakeRange(NSNotFound, 0)];
        [(id)view paste:nil];
        NSEvent* mouse = [NSEvent mouseEventWithType:NSEventTypeLeftMouseDown location:NSMakePoint(10, 20)
            modifierFlags:0 timestamp:0 windowNumber:native.windowNumber context:nil eventNumber:0 clickCount:1 pressure:1];
        [view mouseDown:mouse];
        [view mouseEntered:mouse]; [view mouseExited:mouse];
        Check(events.empty(), "disabled windows reject pointer, keyboard, text and responder actions");
        Check(!native.canBecomeKeyWindow && jalium_window_activate(window) != 0, "disabled windows cannot activate");
        jalium_window_set_enabled(window, 1);
        Check(native.canBecomeKeyWindow, "reenable activation");

        CheckRuntimeStyleFocus(regular);
        CheckTitleBarPreferences(regular);
        CheckImeTextGeometry(regular);
        CheckManagedChromeWindowMenu(regular);
        CheckExternalFrameChanges(regular);
        CheckPreShowMaximize(regular);
        CheckMenuClose(regular);
        CheckMenuTrackingEscape(regular);

        if (propertiesOnly) {
            jalium_window_destroy(window); jalium_window_destroy(owner); jalium_window_destroy(other);
            jalium_platform_shutdown();
            std::puts("macOS Window property, input and IME geometry tests passed");
            return 0;
        }

        jalium_apple_window_show(owner, 1);
        Check(jalium_window_activate(owner) == 0, "activate owner");
        Check(WaitUntil([&] { return NSApp.active && View(owner).window.keyWindow; }),
            "nonactivating show checks require actual owner activation");
        NSWindow* keyBefore = NSApp.keyWindow;
        events.clear();
        jalium_apple_window_show(window, 0);
        Check(native.visible && NSApp.keyWindow == keyBefore, "nonactivating show preserves key window");
        jalium_window_activate(window);
        bool activated = WaitUntil([&] { return native.keyWindow; });
        if (!activated) {
            std::fprintf(stderr, "Activation: active=%d policy=%ld key=%s main=%s requested visible=%d key-capable=%d owner visible=%d key-capable=%d\n",
                NSApp.active, (long)NSApp.activationPolicy, NSApp.keyWindow.title.UTF8String,
                NSApp.mainWindow.title.UTF8String, native.visible, native.canBecomeKeyWindow,
                View(owner).window.visible, View(owner).window.canBecomeKeyWindow);
        }
        Check(activated, "activate requested window");
        Check(Count(events, JALIUM_EVENT_ACTIVATE) == 1, "activation callback once");
        jalium_window_hide(window);
        Check(!native.visible && Count(events, JALIUM_EVENT_DEACTIVATE) == 1, "hide deactivates window");
        jalium_window_show(window);

        events.clear();
        NSEvent* backTab = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint
            modifierFlags:NSEventModifierFlagShift timestamp:0 windowNumber:native.windowNumber context:nil
            characters:@"\x19" charactersIgnoringModifiers:@"\x19" isARepeat:NO keyCode:48];
        [view keyDown:backTab]; [view keyUp:backTab];
        Check(Count(events, JALIUM_EVENT_KEY_DOWN) == 1 && Count(events, JALIUM_EVENT_KEY_UP) == 1,
            "back-tab dispatches one key pair");
        Check(events.front().key.keyCode == 0x09 && (events.front().key.modifiers & JALIUM_MOD_SHIFT),
            "Shift+Tab maps to Tab with its modifier");

        NSRect normal = native.frame;
        events.clear();
        [native zoom:nil];
        Check(jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MAXIMIZED && native.zoomed, "native zoom synchronizes state");
        Check(Count(events, JALIUM_EVENT_STATE_CHANGED) == 1, "zoom publishes one state event");
        [native zoom:nil];
        Check(jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL && NSEqualRects(native.frame, normal),
            "native zoom restores normal bounds");
        jalium_apple_window_set_style(window, (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS);
        normal = native.frame;
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_MAXIMIZED);
        Check(jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MAXIMIZED, "borderless maximization");
        auto* defaults = NSUserDefaults.standardUserDefaults;
        NSDictionary* savedArguments = [defaults volatileDomainForName:NSArgumentDomain];
        NSMutableDictionary* arguments = [savedArguments mutableCopy];
        arguments[@"AppleActionOnDoubleClick"] = @"Minimize";
        [defaults setVolatileDomain:arguments forName:NSArgumentDomain];
        Check(jalium_apple_window_titlebar_double_click(window) == 0, "title-bar minimize preference");
        bool minimizedByCaption = WaitUntil([&] { return native.miniaturized; });
        if (!minimizedByCaption)
            std::fprintf(stderr, "Caption minimize: preference=%s state=%d mask=%lu visible=%d active=%d\n",
                [defaults stringForKey:@"AppleActionOnDoubleClick"].UTF8String ?: "(null)",
                jalium_window_get_state(window), (unsigned long)native.styleMask, native.visible, NSApp.active);
        Check(minimizedByCaption, "minimize borderless window");
        Check(jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MINIMIZED, "native minimize callback");
        [defaults setVolatileDomain:savedArguments forName:NSArgumentDomain];
        [native deminiaturize:nil];
        Check(WaitUntil([&] { return !native.miniaturized; }) &&
            jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MAXIMIZED, "deminiaturize restores previous state");
        [native performMiniaturize:nil];
        Check(WaitUntil([&] { return native.miniaturized; }), "minimize before reactivation");
        Check(jalium_window_activate(window) == 0 &&
            WaitUntil([&] { return !native.miniaturized; }) &&
            jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MAXIMIZED,
            "reactivation preserves maximized state");
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_NORMAL);
        Check(NSEqualRects(native.frame, normal), "restore after maximize and minimize");
        jalium_apple_window_set_style(window, regular);
        normal = native.frame;
        Check([native makeFirstResponder:view], "input view accepts focus before real fullscreen");
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_FULLSCREEN);
        Check(WaitUntil([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_FULLSCREEN; }), "enter real AppKit full screen");
        Check(native.firstResponder == view, "real fullscreen entry preserves the input responder");
        Check(jalium_apple_window_get_restore_bounds(window, &x, &y, &width, &height) == 0 &&
            width == 720 && height == 520, "real fullscreen exposes normal client restore size");
        NSRect fullscreenFrame = native.frame;
        jalium_window_set_show_in_taskbar(window, 0);
        Check(jalium_window_get_state(window) == JALIUM_WINDOW_STATE_FULLSCREEN &&
            (native.styleMask & NSWindowStyleMaskFullScreen) &&
            (native.collectionBehavior & NSWindowCollectionBehaviorFullScreenPrimary) &&
            native.excludedFromWindowsMenu && NSEqualRects(native.frame, fullscreenFrame),
            "changing Window-menu participation preserves active fullscreen state and frame");
        jalium_window_set_show_in_taskbar(window, 1);
        Check(!native.excludedFromWindowsMenu && NSEqualRects(native.frame, fullscreenFrame),
            "restoring Window-menu participation preserves active fullscreen frame");
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_MAXIMIZED);
        bool maximized = WaitUntil([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MAXIMIZED &&
            !(native.styleMask & NSWindowStyleMaskFullScreen); });
        if (!maximized) {
            std::fprintf(stderr, "Full screen exit: state=%d mask=%lu frame=%s\n",
                jalium_window_get_state(window), (unsigned long)native.styleMask, NSStringFromRect(native.frame).UTF8String);
            for (auto& event : events) if (event.type == JALIUM_EVENT_STATE_CHANGED)
                std::fprintf(stderr, "  state event: %d\n", event.stateChanged.newState);
        }
        Check(maximized, "fullscreen to maximized");
        Check(native.firstResponder == view, "real fullscreen exit preserves the input responder");
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_NORMAL);
        Check(NSEqualRects(native.frame, normal), "fullscreen transition retains restore frame");
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_FULLSCREEN);
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_MINIMIZED);
        bool minimized = WaitUntil([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MINIMIZED &&
            !(native.styleMask & NSWindowStyleMaskFullScreen); });
        if (!minimized) {
            std::fprintf(stderr, "Queued minimize: state=%d mask=%lu mini=%d\n", jalium_window_get_state(window),
                (unsigned long)native.styleMask, native.miniaturized);
            for (auto& event : events) if (event.type == JALIUM_EVENT_STATE_CHANGED)
                std::fprintf(stderr, "  state event: %d\n", event.stateChanged.newState);
        }
        Check(minimized, "queue minimize during fullscreen transition");
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_NORMAL);
        Check(WaitUntil([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL; }), "restore queued transition");

        CheckAccessibilityActivationWrites(regular);
        CheckAccessibilityActivationWrites((regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS);

        events.clear();
        [native performClose:nil];
        Check(native.visible && Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 1, "native close request is cancellable");
        NSEvent* closeKey = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint
            modifierFlags:NSEventModifierFlagCommand timestamp:0 windowNumber:native.windowNumber context:nil
            characters:@"w" charactersIgnoringModifiers:@"w" isARepeat:NO keyCode:13];
        Check([native performKeyEquivalent:closeKey] && Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 2,
            "Command-W requests a cancellable window close");
        [view keyDown:closeKey];
        Check(Count(events, JALIUM_EVENT_CLOSE_REQUESTED) == 3,
            "responder-delivered Command-W requests the same close");
        Check(jalium_window_begin_resize_drag(window, 0) != 0, "reject invalid resize edge");
        Check(jalium_window_begin_move_drag(window) != 0, "drag requires live mouse press");
        jalium_window_destroy(window);
        Check(Count(events, JALIUM_EVENT_DESTROYED) == 1, "destroy event once");
        auto count = events.size();
        [view keyDown:key]; [view keyUp:key]; [view mouseDown:mouse]; [(id)view paste:nil];
        Check(events.size() == count, "late native input after destruction is ignored");
        jalium_window_destroy(owner); jalium_window_destroy(other);
        auto* externallyClosed = Create(regular);
        NSView* closedView = View(externallyClosed);
        NSWindow* closedWindow = closedView.window;
        events.clear();
        jalium_window_set_event_callback(externallyClosed, Capture, &events);
        [closedWindow close];
        Check(Count(events, JALIUM_EVENT_DESTROYED) == 1, "external native close releases the platform window once");
        [closedView keyDown:key];
        Check(events.size() == 1, "external native close detaches input callbacks");
        jalium_platform_shutdown();
        std::puts("macOS Window platform tests passed");
    }
    return 0;
}
