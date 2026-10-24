#pragma once
#import <AppKit/AppKit.h>
#include <functional>

// Public NSResponder protocol fixture. These are SDK messages to our own view,
// not injected hardware input or proof that a physical trackpad produced them.
@interface JaliumGestureTestEvent : NSEvent {
@public
    std::function<void()> getter;
}
@property NSEventPhase samplePhase;
@property NSPoint samplePoint;
@property double sampleMagnification, sampleRotation, sampleTime;
@end
@implementation JaliumGestureTestEvent
- (NSEventPhase)phase { return self.samplePhase; }
- (NSPoint)locationInWindow { return self.samplePoint; }
- (NSTimeInterval)timestamp { return self.sampleTime; }
- (CGFloat)magnification { auto action = std::move(getter); if (action) action(); return self.sampleMagnification; }
- (float)rotation { return self.sampleRotation; }
- (NSEventType)type { return NSEventTypeMagnify; }
@end
