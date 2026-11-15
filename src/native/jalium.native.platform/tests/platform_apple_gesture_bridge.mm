#include "platform_apple_gesture_test_event.h"

@interface JaliumScrollTestEvent : NSEvent
@property CGFloat sampleX, sampleY;
@property NSEventPhase samplePhase, sampleMomentum;
@property NSPoint samplePoint;
@property NSTimeInterval sampleTime;
@end
@implementation JaliumScrollTestEvent
- (NSEventType)type { return NSEventTypeScrollWheel; }
- (BOOL)hasPreciseScrollingDeltas { return YES; }
- (CGFloat)scrollingDeltaX { return self.sampleX; }
- (CGFloat)scrollingDeltaY { return self.sampleY; }
- (NSEventPhase)phase { return self.samplePhase; }
- (NSEventPhase)momentumPhase { return self.sampleMomentum; }
- (NSPoint)locationInWindow { return self.samplePoint; }
- (NSEventModifierFlags)modifierFlags { return 0; }
- (NSTimeInterval)timestamp { return self.sampleTime; }
@end

extern "C" __attribute__((visibility("default"))) int jalium_test_apple_scroll_send(
    void* handle, double x, double y, double deltaX, double deltaY,
    int phase, int momentum, double timestamp)
{
    if (!NSThread.isMainThread || !handle) return -1;
    NSView* view = (__bridge NSView*)handle;
    auto* event = [JaliumScrollTestEvent new];
    event.sampleX = deltaX; event.sampleY = deltaY;
    event.samplePhase = static_cast<NSEventPhase>(phase);
    event.sampleMomentum = static_cast<NSEventPhase>(momentum);
    event.samplePoint = [view convertPoint:NSMakePoint(x,y) toView:nil];
    event.sampleTime = timestamp;
    [view scrollWheel:event];
    return 0;
}

// Test-only in-process SDK bridge, built outside the eight-library payload.
extern "C" __attribute__((visibility("default"))) int jalium_test_apple_gesture_send(
    void* handle, int boundary, int kind, int phase, double magnification, double rotation,
    double x, double y, double timestamp)
{
    if (!NSThread.isMainThread || !handle) return -1;
    NSView* view = (__bridge NSView*)handle;
    auto* event = [JaliumGestureTestEvent new];
    event.samplePhase = static_cast<NSEventPhase>(phase);
    event.sampleMagnification = magnification; event.sampleRotation = rotation;
    event.samplePoint = [view convertPoint:NSMakePoint(x,y) toView:nil]; event.sampleTime = timestamp;
    if (boundary == 1) [view beginGestureWithEvent:event];
    else if (boundary == 2) [view endGestureWithEvent:event];
    else if (kind == 1) [view magnifyWithEvent:event];
    else if (kind == 2) [view rotateWithEvent:event];
    else return -2;
    return 0;
}

extern "C" __attribute__((visibility("default"))) int jalium_test_apple_gesture_convert_point(
    void* sourceHandle, void* destinationHandle, double x, double y, double* outX, double* outY)
{
    if (!NSThread.isMainThread || !sourceHandle || !destinationHandle || !outX || !outY) return -1;
    NSView* source = (__bridge NSView*)sourceHandle;
    NSView* destination = (__bridge NSView*)destinationHandle;
    NSPoint screen = [source.window convertPointToScreen:[source convertPoint:NSMakePoint(x,y) toView:nil]];
    NSPoint local = [destination convertPoint:[destination.window convertPointFromScreen:screen] fromView:nil];
    *outX = local.x; *outY = local.y;
    return 0;
}
