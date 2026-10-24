#pragma once
#import <AppKit/AppKit.h>
#include <functional>
#include <utility>
#include <stdexcept>

// Own-process SDK fixture. Never posts system/HID events.
@interface JaliumTabletTestEvent : NSEvent {
@public
    std::function<void()> getter;
}
@property SEL sampleTrigger;
@property NSEventType sampleType;
@property NSEventSubtype sampleSubtype;
@property NSPoint samplePoint;
@property NSPoint sampleTilt;
@property double sampleTime;
@property float samplePressure;
@property float sampleRotation;
@property NSUInteger sampleDevice;
@property NSEventButtonMask sampleButtons;
@property NSEventModifierFlags sampleModifiers;
@property NSPointingDeviceType sampleTool;
@property BOOL sampleEntering;
@property NSInteger sampleButton;
@property NSInteger sampleClicks;
@end
@implementation JaliumTabletTestEvent
- (void)query:(SEL)selector { if(self.sampleTrigger&&selector!=self.sampleTrigger)return;auto action=std::exchange(getter,{});if(action)action(); }
- (NSEventType)type { [self query:_cmd];return self.sampleType; }
- (NSEventSubtype)subtype { [self query:_cmd];return self.sampleSubtype; }
- (NSPoint)locationInWindow { [self query:_cmd];return self.samplePoint; }
- (NSEventModifierFlags)modifierFlags { [self query:_cmd];return self.sampleModifiers; }
- (NSTimeInterval)timestamp { [self query:_cmd];return self.sampleTime; }
- (NSUInteger)deviceID { [self query:_cmd];return self.sampleDevice; }
- (void)requirePoint { if(self.sampleType!=NSEventTypeTabletPoint&&self.sampleSubtype!=NSEventSubtypeTabletPoint)throw std::logic_error("point metadata queried on a non-point event"); }
- (void)requireProximity { if(self.sampleType!=NSEventTypeTabletProximity&&self.sampleSubtype!=NSEventSubtypeTabletProximity)throw std::logic_error("proximity metadata queried on a non-proximity event"); }
- (float)pressure { [self requirePoint];[self query:_cmd];return self.samplePressure; }
- (NSPoint)tilt { [self requirePoint];[self query:_cmd];return self.sampleTilt; }
- (float)rotation { [self requirePoint];[self query:_cmd];return self.sampleRotation; }
- (NSEventButtonMask)buttonMask { [self requirePoint];[self query:_cmd];return self.sampleButtons; }
- (NSPointingDeviceType)pointingDeviceType { [self requireProximity];[self query:_cmd];return self.sampleTool; }
- (BOOL)isEnteringProximity { [self requireProximity];[self query:_cmd];return self.sampleEntering; }
- (NSInteger)buttonNumber { [self query:_cmd];return self.sampleButton; }
- (NSInteger)clickCount { [self query:_cmd];return self.sampleClicks; }
@end
