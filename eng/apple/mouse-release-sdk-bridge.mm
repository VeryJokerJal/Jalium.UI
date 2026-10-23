#import <AppKit/AppKit.h>
#import <objc/runtime.h>

// Test-only snapshots in this process. No event is posted to AppKit or HID.
static NSUInteger sampleButtons;
static NSUInteger SnapshotButtons(id, SEL) { return sampleButtons; }

@interface JaliumMouseReleaseSample : NSEvent
@property NSEventType sampleType;
@property NSInteger sampleButton;
@property NSPoint samplePoint;
@property NSTimeInterval sampleTime;
@end
@implementation JaliumMouseReleaseSample
- (NSEventType)type { return self.sampleType; }
- (NSInteger)buttonNumber { return self.sampleButton; }
- (NSEventSubtype)subtype { return static_cast<NSEventSubtype>(0); }
- (NSPoint)locationInWindow { return self.samplePoint; }
- (NSTimeInterval)timestamp { return self.sampleTime; }
- (NSEventModifierFlags)modifierFlags { return 0; }
- (NSInteger)clickCount { return 1; }
- (float)pressure { return self.sampleType == NSEventTypeLeftMouseUp ? 0 : 1; }
@end

extern "C" __attribute__((visibility("default"))) int jalium_test_mouse_release_send(
    void* handle, int kind, int button, unsigned buttons, double x, double y, double time)
{
    if (!NSThread.isMainThread || !handle || kind < 0 || kind > 2 || button < 0 || button > 4 || buttons > 31) return -1;
    NSView* view = (__bridge NSView*)handle;
    if (!view.window || !view.window.visible) return -2;
    JaliumMouseReleaseSample* event = [JaliumMouseReleaseSample new];
    event.sampleButton = button;
    event.sampleType = kind == 2 ? NSEventTypeMouseMoved : button == 0
        ? (kind == 0 ? NSEventTypeLeftMouseDown : NSEventTypeLeftMouseUp) : button == 1
        ? (kind == 0 ? NSEventTypeRightMouseDown : NSEventTypeRightMouseUp)
        : (kind == 0 ? NSEventTypeOtherMouseDown : NSEventTypeOtherMouseUp);
    event.samplePoint = [view convertPoint:NSMakePoint(x, y) toView:nil];
    event.sampleTime = time;
    Method query = class_getClassMethod(NSEvent.class, @selector(pressedMouseButtons));
    NSUInteger previousButtons = sampleButtons;
    IMP previous = method_setImplementation(query, reinterpret_cast<IMP>(&SnapshotButtons));
    sampleButtons = buttons;
    @try {
        if (kind == 2) [view mouseMoved:event];
        else if (button == 0) { if (kind == 0) [view mouseDown:event]; else [view mouseUp:event]; }
        else if (button == 1) { if (kind == 0) [view rightMouseDown:event]; else [view rightMouseUp:event]; }
        else { if (kind == 0) [view otherMouseDown:event]; else [view otherMouseUp:event]; }
    } @finally {
        method_setImplementation(query, previous);
        sampleButtons = previousButtons;
    }
    return method_getImplementation(query) == previous ? 0 : -3;
}
