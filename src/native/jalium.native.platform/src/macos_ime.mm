#import <AppKit/AppKit.h>
#import <Foundation/Foundation.h>
#import <stdlib.h>
#import <stdint.h>
#import <stdio.h>
#import <stdarg.h>
#import <limits.h>

typedef void (*macos_ime_insert_text_fn)(void* context, const char* text);
typedef void (*macos_ime_set_marked_text_fn)(void* context, const char* text, int selectedStart, int selectedLength, int replacementStart, int replacementLength);
typedef void (*macos_ime_unmark_text_fn)(void* context);
typedef void (*macos_ime_command_fn)(void* context, const char* command);

static BOOL macos_ime_debug_enabled(void)
{
    const char* value = getenv("MACOS_IME_DEBUG");
    if (value == NULL)
        return NO;

    return strcmp(value, "1") == 0 || strcasecmp(value, "true") == 0;
}

static void macos_ime_log(NSString* format, ...)
{
    if (!macos_ime_debug_enabled())
        return;

    va_list args;
    va_start(args, format);
    NSString* message = [[NSString alloc] initWithFormat:format arguments:args];
    va_end(args);

    @try {
        NSString* ts = [[NSDate date] descriptionWithLocale:nil];
        NSString* line = [NSString stringWithFormat:@"%@ %@", ts, message];
        const char* utf8 = [line UTF8String];
        const char* tmp = getenv("TMPDIR");
        if (tmp == NULL) tmp = "/tmp";
        char path[PATH_MAX];
        snprintf(path, sizeof(path), "%s/macos_ime.log", tmp);
        FILE* f = fopen(path, "a");
        if (f) {
            fprintf(f, "%s\n", utf8);
            fclose(f);
        }
    } @catch (NSException* e) {
        (void)e;
    }
}

static int macos_ime_range_value(NSUInteger value)
{
    if (value == NSNotFound) {
        return -1;
    }

    if (value > (NSUInteger)INT_MAX) {
        return INT_MAX;
    }

    return (int)value;
}

@interface MacOsImeInputTextView : NSTextView

@property(assign) void* managedContext;
@property(assign) macos_ime_insert_text_fn insertTextCallback;
@property(assign) macos_ime_set_marked_text_fn setMarkedTextCallback;
@property(assign) macos_ime_unmark_text_fn unmarkTextCallback;
@property(assign) macos_ime_command_fn commandCallback;
@property(assign) NSRect imeCaretRectInWindow;

@end

@implementation MacOsImeInputTextView

+ (NSFont*)macos_ime_editorFont
{
    NSFont* font = [NSFont fontWithName:@"Menlo" size:13.0];
    if (font != nil) {
        return font;
    }

    if (@available(macOS 10.15, *)) {
        return [NSFont monospacedSystemFontOfSize:13.0 weight:NSFontWeightRegular];
    }

    NSFont* fallback = [NSFont userFixedPitchFontOfSize:13.0];
    return fallback ?: [NSFont systemFontOfSize:13.0];
}

- (instancetype)initWithFrame:(NSRect)frameRect
{
    self = [super initWithFrame:frameRect];
    if (self) {
        self.drawsBackground = NO;
        self.editable = YES;
        self.selectable = YES;
        self.richText = NO;
        self.importsGraphics = NO;
        self.automaticQuoteSubstitutionEnabled = NO;
        self.automaticDashSubstitutionEnabled = NO;
        self.automaticDataDetectionEnabled = NO;
        self.automaticSpellingCorrectionEnabled = NO;
        self.automaticTextReplacementEnabled = NO;
        self.continuousSpellCheckingEnabled = NO;
        self.grammarCheckingEnabled = NO;
        self.font = [MacOsImeInputTextView macos_ime_editorFont];
        self.textColor = NSColor.clearColor;
        self.insertionPointColor = NSColor.clearColor;
        self.alphaValue = 1.0;
        self.selectedTextAttributes = @{ NSBackgroundColorAttributeName: NSColor.clearColor, NSForegroundColorAttributeName: NSColor.clearColor };
        self.typingAttributes = @{ NSFontAttributeName: self.font, NSForegroundColorAttributeName: NSColor.clearColor };
        self.string = @"";
        self.selectedRange = NSMakeRange(0, 0);
        self.imeCaretRectInWindow = frameRect;
        macos_ime_log(@"MacOsImeInputTextView init frame=%@", NSStringFromRect(frameRect));
    }
    return self;
}

- (BOOL)acceptsFirstResponder
{
    return YES;
}

- (BOOL)isOpaque
{
    return NO;
}

- (void)drawRect:(NSRect)dirtyRect
{
}

- (void)drawInsertionPointInRect:(NSRect)rect color:(NSColor*)color turnedOn:(BOOL)flag
{
}

- (BOOL)becomeFirstResponder
{
    BOOL result = [super becomeFirstResponder];
    macos_ime_log(@"becomeFirstResponder result=%d firstResponder=%@", result, self.window.firstResponder);
    return result;
}

- (BOOL)resignFirstResponder
{
    BOOL result = [super resignFirstResponder];
    macos_ime_log(@"resignFirstResponder result=%d firstResponder=%@", result, self.window.firstResponder);
    return result;
}

- (void)insertText:(id)string replacementRange:(NSRange)replacementRange
{
    NSString* committed = [string isKindOfClass:[NSAttributedString class]] ? [(NSAttributedString*)string string] : (NSString*)string;
    macos_ime_log(@"insertText text=%@ replacementRange=%@", committed, NSStringFromRange(replacementRange));
    if (committed.length > 0 && self.insertTextCallback != NULL) {
        self.insertTextCallback(self.managedContext, committed.UTF8String);
    }
    self.string = @"";
    self.selectedRange = NSMakeRange(0, 0);
}

- (void)setMarkedText:(id)string selectedRange:(NSRange)selectedRange replacementRange:(NSRange)replacementRange
{
    NSString* marked = [string isKindOfClass:[NSAttributedString class]] ? [(NSAttributedString*)string string] : (NSString*)string;
    macos_ime_log(@"setMarkedText text=%@ selectedRange=%@ replacementRange=%@", marked, NSStringFromRange(selectedRange), NSStringFromRange(replacementRange));
    if (self.setMarkedTextCallback != NULL) {
        const char* utf8 = marked != nil ? marked.UTF8String : "";
        self.setMarkedTextCallback(
            self.managedContext,
            utf8,
            macos_ime_range_value(selectedRange.location),
            macos_ime_range_value(selectedRange.length),
            macos_ime_range_value(replacementRange.location),
            macos_ime_range_value(replacementRange.length));
    }
    [super setMarkedText:string selectedRange:selectedRange replacementRange:replacementRange];
}

- (void)unmarkText
{
    macos_ime_log(@"unmarkText");
    if (self.unmarkTextCallback != NULL) {
        self.unmarkTextCallback(self.managedContext);
    }
    [super unmarkText];
}

- (NSRange)markedRange
{
    NSRange range = [super markedRange];
    macos_ime_log(@"markedRange=%@", NSStringFromRange(range));
    return range;
}

- (NSRange)selectedRange
{
    NSRange range = [super selectedRange];
    macos_ime_log(@"selectedRange=%@", NSStringFromRange(range));
    return range;
}

- (NSRect)firstRectForCharacterRange:(NSRange)range actualRange:(nullable NSRangePointer)actualRange
{
    if (actualRange != NULL) {
        *actualRange = range;
    }

    NSRect windowRect = self.imeCaretRectInWindow;
    if (NSEqualRects(windowRect, NSZeroRect)) {
        windowRect = [self convertRect:self.bounds toView:nil];
    }

    NSRect rect = [self.window convertRectToScreen:windowRect];
    NSScreen* screen = self.window.screen ?: [NSScreen mainScreen];
    CGFloat backing = screen.backingScaleFactor;
    NSRect rectInPixels = NSMakeRect(rect.origin.x * backing, rect.origin.y * backing, rect.size.width * backing, rect.size.height * backing);
    NSRect roundedRectInPixels = NSMakeRect(round(rectInPixels.origin.x), round(rectInPixels.origin.y), round(rectInPixels.size.width), round(rectInPixels.size.height));
    NSRect roundedRectInPoints = NSMakeRect(roundedRectInPixels.origin.x / backing, roundedRectInPixels.origin.y / backing, roundedRectInPixels.size.width / backing, roundedRectInPixels.size.height / backing);
    macos_ime_log(@"firstRectForCharacterRange range=%@ screenRect=%@ rounded=%@ actualRange=%@", NSStringFromRange(range), NSStringFromRect(rect), NSStringFromRect(roundedRectInPoints), actualRange != NULL ? NSStringFromRange(*actualRange) : @"<null>");
    return roundedRectInPoints;
}

- (void)doCommandBySelector:(SEL)selector
{
    if (self.commandCallback != NULL) {
        NSString* name = NSStringFromSelector(selector);
        macos_ime_log(@"doCommandBySelector %@", name);
        self.commandCallback(self.managedContext, name.UTF8String);
        return;
    }
    [super doCommandBySelector:selector];
}

- (void)copy:(id)sender { if (self.commandCallback != NULL) self.commandCallback(self.managedContext, "copy:"); }
- (void)cut:(id)sender { if (self.commandCallback != NULL) self.commandCallback(self.managedContext, "cut:"); }
- (void)paste:(id)sender { if (self.commandCallback != NULL) self.commandCallback(self.managedContext, "paste:"); }
- (void)selectAll:(id)sender { if (self.commandCallback != NULL) self.commandCallback(self.managedContext, "selectAll:"); }
- (void)undo:(id)sender { if (self.commandCallback != NULL) self.commandCallback(self.managedContext, "undo:"); }
- (void)redo:(id)sender { if (self.commandCallback != NULL) self.commandCallback(self.managedContext, "redo:"); }

@end

@interface MacOsImeInputBridge : NSObject
@property(assign) NSWindow* window;
@property(strong) MacOsImeInputTextView* textView;
@property(strong) id keyEventMonitor;
@end

@implementation MacOsImeInputBridge
@end

extern "C" {

void* macos_ime_create(
    void* windowHandle,
    void* managedContext,
    macos_ime_insert_text_fn insertTextCallback,
    macos_ime_set_marked_text_fn setMarkedTextCallback,
    macos_ime_unmark_text_fn unmarkTextCallback,
    macos_ime_command_fn commandCallback)
{
    NSWindow* window = (__bridge NSWindow*)windowHandle;
    if (window == nil || window.contentView == nil) {
        macos_ime_log(@"macos_ime_create failed: window or contentView was nil.");
        return NULL;
    }

    MacOsImeInputBridge* bridge = [[MacOsImeInputBridge alloc] init];
    bridge.window = window;
    bridge.textView = [[MacOsImeInputTextView alloc] initWithFrame:NSMakeRect(0, 0, 2, 18)];
    bridge.textView.managedContext = managedContext;
    bridge.textView.insertTextCallback = insertTextCallback;
    bridge.textView.setMarkedTextCallback = setMarkedTextCallback;
    bridge.textView.unmarkTextCallback = unmarkTextCallback;
    bridge.textView.commandCallback = commandCallback;
    [window.contentView addSubview:bridge.textView];
    bridge.keyEventMonitor = [NSEvent addLocalMonitorForEventsMatchingMask:NSEventMaskKeyDown handler:^NSEvent* _Nullable(NSEvent* event) {
        if (bridge.window == nil || bridge.textView == nil) {
            return event;
        }
        if (bridge.window.firstResponder != bridge.textView) {
            return event;
        }
        macos_ime_log(@"local key monitor keyCode=%hu characters=%@ charactersIgnoringModifiers=%@", event.keyCode, event.characters, event.charactersIgnoringModifiers);
        [bridge.textView keyDown:event];
        return nil;
    }];
    macos_ime_log(@"macos_ime_create success window=%p contentView=%@ textView=%@", window, window.contentView, bridge.textView);
    return (__bridge_retained void*)bridge;
}

void macos_ime_destroy(void* bridgeHandle)
{
    if (bridgeHandle == NULL) {
        return;
    }

    MacOsImeInputBridge* bridge = (__bridge_transfer MacOsImeInputBridge*)bridgeHandle;
    macos_ime_log(@"macos_ime_destroy textView=%@", bridge.textView);
    if (bridge.keyEventMonitor != nil) {
        [NSEvent removeMonitor:bridge.keyEventMonitor];
        bridge.keyEventMonitor = nil;
    }
    [bridge.textView removeFromSuperview];
}

void macos_ime_focus(void* bridgeHandle, bool focus)
{
    MacOsImeInputBridge* bridge = (__bridge MacOsImeInputBridge*)bridgeHandle;
    if (bridge == nil || bridge.window == nil || bridge.textView == nil) {
        macos_ime_log(@"macos_ime_focus ignored: bridge/window/textView missing.");
        return;
    }

    if (focus) {
        macos_ime_log(@"macos_ime_focus requesting first responder. Existing firstResponder=%@", bridge.window.firstResponder);
        [bridge.window makeFirstResponder:bridge.textView];
        macos_ime_log(@"macos_ime_focus completed. New firstResponder=%@", bridge.window.firstResponder);
    }
}

void macos_ime_update_caret_rect(void* bridgeHandle, unsigned long long eventId, double x, double y, double width, double height)
{
    MacOsImeInputBridge* bridge = (__bridge MacOsImeInputBridge*)bridgeHandle;
    if (bridge == nil || bridge.window == nil || bridge.window.contentView == nil || bridge.textView == nil) {
        macos_ime_log(@"macos_ime_update_caret_rect ignored: bridge/window/contentView/textView missing. id=%llu", (unsigned long long)eventId);
        return;
    }

    NSView* contentView = bridge.window.contentView;
    macos_ime_log(@"macos_ime_update_caret_rect id=%llu raw x=%f y=%f w=%f h=%f", (unsigned long long)eventId, x, y, width, height);
    NSScreen* screen = bridge.window.screen ?: [NSScreen mainScreen];
    CGFloat backing = screen.backingScaleFactor;
    macos_ime_log(@"macos_ime_update_caret_rect id=%llu backingScaleFactor=%f", (unsigned long long)eventId, backing);
    NSRect bounds = contentView.bounds;
    CGFloat caretWidth = (CGFloat)MAX(width, 2.0);
    CGFloat caretHeight = (CGFloat)MAX(height, 18.0);
    CGFloat caretX = (CGFloat)x;
    BOOL isFlipped = contentView.isFlipped;
    CGFloat caretY = isFlipped ? (CGFloat)y : (CGFloat)(bounds.size.height - y - caretHeight);

    bridge.textView.frame = NSMakeRect(caretX, caretY, caretWidth, caretHeight);
    bridge.textView.imeCaretRectInWindow = [contentView convertRect:bridge.textView.frame toView:nil];
    NSRect imeRectInWindow = bridge.textView.imeCaretRectInWindow;
    NSRect screenRect = [bridge.window convertRectToScreen:imeRectInWindow];

    NSRect screenRectInPixels = NSMakeRect(screenRect.origin.x * backing, screenRect.origin.y * backing, screenRect.size.width * backing, screenRect.size.height * backing);
    NSRect roundedScreenRectInPixels = NSMakeRect(round(screenRectInPixels.origin.x), round(screenRectInPixels.origin.y), round(screenRectInPixels.size.width), round(screenRectInPixels.size.height));
    NSRect roundedScreenRectInPoints = NSMakeRect(roundedScreenRectInPixels.origin.x / backing, roundedScreenRectInPixels.origin.y / backing, roundedScreenRectInPixels.size.width / backing, roundedScreenRectInPixels.size.height / backing);

    NSRect roundedWindowRect = [bridge.window convertRectFromScreen:roundedScreenRectInPoints];
    bridge.textView.imeCaretRectInWindow = roundedWindowRect;

    macos_ime_log(@"macos_ime_update_caret_rect id=%llu frame=%@ bounds=%@ imeCaretRectInWindow=%@ screenRect_points=%@ screenRect_pixels=%@ rounded_points=%@ rounded_pixels=%@ flipped=%d", (unsigned long long)eventId, NSStringFromRect(bridge.textView.frame), NSStringFromRect(bounds), NSStringFromRect(imeRectInWindow), NSStringFromRect(screenRect), NSStringFromRect(screenRectInPixels), NSStringFromRect(roundedScreenRectInPoints), NSStringFromRect(roundedScreenRectInPixels), isFlipped);
}

}
