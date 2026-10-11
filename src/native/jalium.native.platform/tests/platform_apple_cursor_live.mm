#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <cstdio>
#include <cstdlib>
#include <dlfcn.h>
#include <unistd.h>

// Manual acceptance host. It uses real pointer, hit-test and activation state.
// The cursor observers forward AppKit's original implementations unchanged.
static FILE* logFile;
static IMP originalSet, originalHide, originalUnhide;
static int cursorSets, cursorHides, cursorUnhides;
static void CursorSet(id self, SEL selector)
{
    ++cursorSets;
    reinterpret_cast<void(*)(id, SEL)>(originalSet)(self, selector);
}
static void CursorHide(id self, SEL selector)
{
    ++cursorHides;
    reinterpret_cast<void(*)(id, SEL)>(originalHide)(self, selector);
}
static void CursorUnhide(id self, SEL selector)
{
    ++cursorUnhides;
    reinterpret_cast<void(*)(id, SEL)>(originalUnhide)(self, selector);
}

@interface CursorCanvas : NSView
@end
@implementation CursorCanvas
- (NSView*)hitTest:(NSPoint)point
{
    NSView* hit = [super hitTest:point];
    return hit == self ? nil : hit;
}
@end

@interface CursorStack : NSStackView
@end
@implementation CursorStack
- (NSView*)hitTest:(NSPoint)point
{
    NSView* hit = [super hitTest:point];
    return hit == self ? nil : hit;
}
@end

@interface CursorAcceptance : NSObject <NSApplicationDelegate>
@property(nonatomic) JaliumPlatformWindow* platform;
@property(nonatomic, strong) NSView* view;
@property(nonatomic, strong) NSTextField* field;
@property(nonatomic, strong) NSTextField* status;
@property(nonatomic, strong) NSSegmentedControl* modes;
@property(nonatomic, strong) NSTimer* timer;
@property(nonatomic, strong) id eventMonitor;
@property(nonatomic, copy) NSString* lastSample;
@property(nonatomic) bool finished;
- (void)sample;
@end

static NSTextField* Label(NSString* text, CGFloat size)
{
    NSTextField* label = [NSTextField wrappingLabelWithString:text];
    label.font = [NSFont systemFontOfSize:size];
    label.translatesAutoresizingMaskIntoConstraints = NO;
    return label;
}

@implementation CursorAcceptance
- (void)applicationDidFinishLaunching:(NSNotification*)notification
{
    JaliumWindowParams params{};
    params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Jalium Cursor Ownership v175");
    params.width = 680; params.height = 660;
    params.style = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_RESIZABLE;
    self.platform = jalium_window_create(&params);
    if (!self.platform) { std::fprintf(logFile, "HOST_CREATE_FAILED\n"); std::exit(2); }
    self.view = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(self.platform));
    NSWindow* window = self.view.window;
    window.contentMinSize = NSMakeSize(480, 640);
    window.acceptsMouseMovedEvents = YES;

    NSTextField* title = Label(@"原生控件的光标交接", 24);
    title.font = [NSFont systemFontOfSize:24 weight:NSFontWeightSemibold];
    NSTextField* instructions = Label(@"切换下方光标，把指针移到空白区域，再移入文本框。文本框应保留 I 形光标并可编辑。", 15);
    self.modes = [NSSegmentedControl segmentedControlWithLabels:@[@"手形", @"隐藏", @"箭头"]
        trackingMode:NSSegmentSwitchTrackingSelectOne target:self action:@selector(changeMode:)];
    self.modes.selectedSegment = 0;
    [self.modes setAccessibilityLabel:@"空白区域的光标"];
    self.modes.translatesAutoresizingMaskIntoConstraints = NO;

    CursorCanvas* canvas = [CursorCanvas new];
    canvas.translatesAutoresizingMaskIntoConstraints = NO;
    canvas.wantsLayer = YES;
    canvas.layer.cornerRadius = 10;
    canvas.layer.borderWidth = 1;
    canvas.layer.borderColor = NSColor.separatorColor.CGColor;
    canvas.layer.backgroundColor = NSColor.controlBackgroundColor.CGColor;
    self.field = [[NSTextField alloc] initWithFrame:NSZeroRect];
    self.field.font = [NSFont systemFontOfSize:16];
    self.field.stringValue = @"原生 NSTextField · 可选择和编辑";
    self.field.translatesAutoresizingMaskIntoConstraints = NO;
    [self.field setAccessibilityLabel:@"原生文本框"];
    [canvas addSubview:self.field];
    NSTextField* canvasHint = Label(@"四周空白区域使用上方选择的光标", 14);
    canvasHint.textColor = NSColor.secondaryLabelColor;
    [canvas addSubview:canvasHint];
    [NSLayoutConstraint activateConstraints:@[
        [canvas.heightAnchor constraintGreaterThanOrEqualToConstant:180],
        [self.field.leadingAnchor constraintEqualToAnchor:canvas.leadingAnchor constant:28],
        [self.field.trailingAnchor constraintEqualToAnchor:canvas.trailingAnchor constant:-28],
        [self.field.centerYAnchor constraintEqualToAnchor:canvas.centerYAnchor constant:-12],
        [self.field.heightAnchor constraintEqualToConstant:34],
        [canvasHint.leadingAnchor constraintEqualToAnchor:self.field.leadingAnchor],
        [canvasHint.trailingAnchor constraintEqualToAnchor:self.field.trailingAnchor],
        [canvasHint.topAnchor constraintEqualToAnchor:self.field.bottomAnchor constant:12]
    ]];
    self.status = Label(@"等待指针进入测试区域", 13);
    self.status.textColor = NSColor.secondaryLabelColor;
    [self.status.heightAnchor constraintEqualToConstant:76].active = YES;
    NSButton* finish = [NSButton buttonWithTitle:@"结束检查" target:self action:@selector(finish:)];
    finish.bezelStyle = NSBezelStyleRounded;
    finish.keyEquivalent = @"\r";
    NSStackView* stack = [CursorStack stackViewWithViews:@[title, instructions, self.modes, canvas, self.status, finish]];
    stack.orientation = NSUserInterfaceLayoutOrientationVertical;
    stack.alignment = NSLayoutAttributeLeading;
    stack.spacing = 16;
    stack.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:stack];
    [NSLayoutConstraint activateConstraints:@[
        [stack.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor constant:24],
        [stack.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor constant:-24],
        [stack.topAnchor constraintEqualToAnchor:self.view.topAnchor constant:24],
        [stack.bottomAnchor constraintEqualToAnchor:self.view.bottomAnchor constant:-24],
        [canvas.widthAnchor constraintEqualToAnchor:stack.widthAnchor],
        [title.widthAnchor constraintEqualToAnchor:stack.widthAnchor],
        [instructions.widthAnchor constraintEqualToAnchor:stack.widthAnchor],
        [self.status.widthAnchor constraintEqualToAnchor:stack.widthAnchor]
    ]];
    __weak CursorAcceptance* weakSelf = self;
    self.eventMonitor = [NSEvent addLocalMonitorForEventsMatchingMask:NSEventMaskKeyDown|NSEventMaskLeftMouseDown|NSEventMaskLeftMouseUp
        handler:^NSEvent*(NSEvent* event) {
            if (event.window == weakSelf.view.window) {
                long source = event.CGEvent ? CGEventGetIntegerValueField(event.CGEvent, kCGEventSourceUnixProcessID) : -1;
                std::fprintf(logFile, "EVENT pid=%d type=%lu source=%ld key=%d\n", getpid(),
                    static_cast<unsigned long>(event.type), source, event.type == NSEventTypeKeyDown ? event.keyCode : 0);
                std::fflush(logFile);
            }
            return event;
        }];
    self.timer = [NSTimer scheduledTimerWithTimeInterval:0.05 repeats:YES block:^(NSTimer*) { [weakSelf sample]; }];
    [[NSRunLoop mainRunLoop] addTimer:self.timer forMode:NSRunLoopCommonModes];
    jalium_window_show(self.platform);
    [window center];
    [window makeKeyAndOrderFront:nil];
    [NSApp activate];
    Dl_info library{}; dladdr(reinterpret_cast<void*>(&jalium_window_set_cursor), &library);
    std::fprintf(logFile, "HOST_STARTED pid=%d platform=%s\n", getpid(), library.dli_fname);
    std::fflush(logFile);
    [self sample];
}
- (void)changeMode:(id)sender { self.lastSample = nil; [self sample]; }
- (void)sample
{
    if (self.finished || !self.platform) return;
    NSPoint point = NSEvent.mouseLocation;
    NSPoint local = [self.view convertPoint:[self.view.window convertPointFromScreen:point] fromView:nil];
    NSView* hit = [self.view hitTest:[self.view convertPoint:local toView:self.view.superview]];
    NSInteger hitWindow = [NSWindow windowNumberAtPoint:point belowWindowWithWindowNumber:0];
    pid_t foreground = NSWorkspace.sharedWorkspace.frontmostApplication.processIdentifier;
    BOOL ownsWindow = hitWindow == self.view.window.windowNumber;
    NSString* area = !ownsWindow ? @"窗口外" : hit == self.view ? @"空白区域" :
        (hit == self.field || [hit isDescendantOf:self.field] || hit == self.field.currentEditor) ? @"原生文本框" : @"原生控件";
    int beforeSets = cursorSets, beforeHides = cursorHides, beforeUnhides = cursorUnhides;
    JaliumCursorShape shape = self.modes.selectedSegment == 0 ? JALIUM_CURSOR_HAND :
        self.modes.selectedSegment == 1 ? JALIUM_CURSOR_HIDDEN : JALIUM_CURSOR_ARROW;
    jalium_window_set_cursor(self.platform, shape);
    NSString* cursor = NSCursor.currentCursor == NSCursor.IBeamCursor ? @"I 形" :
        NSCursor.currentCursor == NSCursor.pointingHandCursor ? @"手形" :
        NSCursor.currentCursor == NSCursor.arrowCursor ? @"箭头" : @"其他";
    NSString* sample = [NSString stringWithFormat:@"area=%@ mode=%ld active=%d foreground=%d appCursor=%@ balance=%d set=%d hide=%d unhide=%d",
        area, static_cast<long>(self.modes.selectedSegment), NSApp.active, foreground, cursor,
        cursorHides-cursorUnhides, cursorSets-beforeSets, cursorHides-beforeHides, cursorUnhides-beforeUnhides];
    if (![sample isEqualToString:self.lastSample]) {
        std::fprintf(logFile, "SAMPLE pid=%d point=%.1f,%.1f %s\n", getpid(), point.x, point.y, sample.UTF8String);
        std::fflush(logFile); self.lastSample = sample;
    }
    self.status.stringValue = [NSString stringWithFormat:@"指针位置：%@　应用光标：%@\n前台进程：%d　验收进程：%d　隐藏计数：%d\n应用光标是 AppKit 状态；实际图形与隐藏效果需观察指针。",
        area, cursor, foreground, getpid(), cursorHides-cursorUnhides];
}
- (void)finish:(id)sender
{
    [self cleanup]; [NSApp terminate:nil];
}
- (void)cleanup
{
    if (self.finished) return;
    self.finished = true; [self.timer invalidate];
    if (self.eventMonitor) [NSEvent removeMonitor:self.eventMonitor];
    jalium_window_destroy(self.platform); self.platform = nullptr;
    std::fprintf(logFile, "HOST_FINISHED pid=%d set=%d hide=%d unhide=%d balance=%d\n", getpid(),
        cursorSets, cursorHides, cursorUnhides, cursorHides-cursorUnhides);
    std::fflush(logFile);
}
- (NSApplicationTerminateReply)applicationShouldTerminate:(NSApplication*)application
{
    [self cleanup];
    return NSTerminateNow;
}
@end

int main()
{
    @autoreleasepool {
        NSString* path = [NSBundle.mainBundle.bundlePath stringByAppendingString:@".log"];
        logFile = std::fopen(path.fileSystemRepresentation, "a");
        if (!logFile) return 2;
        setbuf(logFile, nullptr);
        originalSet = method_setImplementation(class_getInstanceMethod(NSCursor.class, @selector(set)), reinterpret_cast<IMP>(CursorSet));
        originalHide = method_setImplementation(class_getClassMethod(NSCursor.class, @selector(hide)), reinterpret_cast<IMP>(CursorHide));
        originalUnhide = method_setImplementation(class_getClassMethod(NSCursor.class, @selector(unhide)), reinterpret_cast<IMP>(CursorUnhide));
        [NSApplication sharedApplication];
        [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];
        CursorAcceptance* delegate = [CursorAcceptance new]; NSApp.delegate = delegate;
        [NSApp run];
        std::fclose(logFile);
    }
    return 0;
}
