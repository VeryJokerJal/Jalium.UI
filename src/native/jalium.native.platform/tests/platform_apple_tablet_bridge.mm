#include "platform_apple_tablet_test_event.h"
// Explicit own-process SDK messages; never system or HID event injection.
extern "C" __attribute__((visibility("default"))) int jalium_test_apple_tablet_send(
    void* handle, int kind, unsigned int device, int tool, int entering, unsigned int buttons,
    float pressure, float tiltX, float tiltY, float rotation,
    double x, double y, double timestamp, int button, int clicks)
{
    if (!NSThread.isMainThread || !handle) return -1;
    NSView* view = (__bridge NSView*)handle;
    auto* event = [JaliumTabletTestEvent new];
    event.sampleDevice = device; event.sampleTool = static_cast<NSPointingDeviceType>(tool);
    event.sampleEntering = entering != 0; event.sampleButtons = static_cast<NSEventButtonMask>(buttons);
    event.samplePressure = pressure; event.sampleTilt = NSMakePoint(tiltX, tiltY);
    event.sampleRotation = rotation; event.sampleTime = timestamp;
    event.samplePoint = [view convertPoint:NSMakePoint(x,y) toView:nil];
    event.sampleButton = button; event.sampleClicks = clicks;
    event.sampleModifiers = NSEventModifierFlagShift | NSEventModifierFlagCommand;
    event.sampleSubtype = NSEventSubtypeTabletPoint;
    switch (kind) {
        case 0: event.sampleType=NSEventTypeTabletProximity;event.sampleSubtype=NSEventSubtypeMouseEvent;[view tabletProximity:event];break;
        case 1: event.sampleType=NSEventTypeMouseMoved;[view mouseMoved:event];break;
        case 2: event.sampleType=NSEventTypeLeftMouseDown;[view mouseDown:event];break;
        case 3: event.sampleType=NSEventTypeLeftMouseDragged;[view mouseDragged:event];break;
        case 4: event.sampleType=NSEventTypeLeftMouseUp;[view mouseUp:event];break;
        case 5: event.sampleType=NSEventTypeTabletPoint;event.sampleSubtype=NSEventSubtypeMouseEvent;[view tabletPoint:event];break;
        case 6: event.sampleType=NSEventTypeRightMouseDown;[view rightMouseDown:event];break;
        case 7: event.sampleType=NSEventTypeRightMouseDragged;[view rightMouseDragged:event];break;
        case 8: event.sampleType=NSEventTypeRightMouseUp;[view rightMouseUp:event];break;
        case 9: event.sampleType=NSEventTypeOtherMouseDown;[view otherMouseDown:event];break;
        case 10: event.sampleType=NSEventTypeOtherMouseDragged;[view otherMouseDragged:event];break;
        case 11: event.sampleType=NSEventTypeOtherMouseUp;[view otherMouseUp:event];break;
        default:return -2;
    }
    return 0;
}
