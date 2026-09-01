#include "jalium_platform.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

#import <TargetConditionals.h>
#import <CoreFoundation/CoreFoundation.h>
#import <QuartzCore/QuartzCore.h>
#if TARGET_OS_OSX
#import <AppKit/AppKit.h>
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
};

@class JaliumAppleView;
#if TARGET_OS_OSX
@class JaliumAppleWindowDelegate;
#endif

struct JaliumPlatformWindow {
#if TARGET_OS_OSX
    NSWindow* window = nil;
    JaliumAppleWindowDelegate* delegate = nil;
#else
    UIWindow* window = nil;
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

void RetainDispatcher(JaliumDispatcher* dispatcher)
{ dispatcher->refs.fetch_add(1, std::memory_order_relaxed); }
void ReleaseDispatcher(JaliumDispatcher* dispatcher)
{ if (dispatcher->refs.fetch_sub(1, std::memory_order_acq_rel) == 1) delete dispatcher; }

int64_t MonotonicMillis()
{
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}

NSString* StringFromUtf16(const JaliumUtf16Char* value)
{
    if (!value) return @"";
    size_t length = 0;
    while (value[length] != 0 && length < (1u << 20)) ++length;
    return [[NSString alloc] initWithCharacters:
        reinterpret_cast<const unichar*>(value) length:length];
}

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
    switch (c) {
        case 0x1b: return 0x1b;
        case '\r': case '\n': return 0x0d;
        case '\t': return 0x09;
        case 0x08: case 0x7f: return 0x08;
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
#endif
        default: return static_cast<int32_t>(c);
    }
}

#if !TARGET_OS_OSX
UIView* FindRootView()
{
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
#endif

} // namespace

#if TARGET_OS_OSX

@interface JaliumAppleView : NSView <NSTextInputClient>
@property(nonatomic, assign) JaliumPlatformWindow* jaliumOwner;
@property(nonatomic, strong) NSMutableAttributedString* markedText;
@property(nonatomic) NSRange selectionRange;
@property(nonatomic) NSRect imeRect;
@end

@implementation JaliumAppleView
+ (Class)layerClass { return [CAMetalLayer class]; }
- (BOOL)isFlipped { return YES; }
- (BOOL)acceptsFirstResponder { return YES; }
- (BOOL)becomeFirstResponder { DispatchSimple(_jaliumOwner, JALIUM_EVENT_FOCUS_GAINED); return YES; }
- (BOOL)resignFirstResponder { DispatchSimple(_jaliumOwner, JALIUM_EVENT_FOCUS_LOST); return YES; }
- (void)updateTrackingAreas {
    [super updateTrackingAreas];
    for (NSTrackingArea* area in self.trackingAreas) [self removeTrackingArea:area];
    NSTrackingArea* area = [[NSTrackingArea alloc] initWithRect:self.bounds
        options:NSTrackingMouseEnteredAndExited|NSTrackingMouseMoved|
                NSTrackingActiveInKeyWindow|NSTrackingInVisibleRect
        owner:self userInfo:nil];
    [self addTrackingArea:area];
}
- (void)dispatchMouse:(NSEvent*)native type:(JaliumEventType)type button:(int32_t)button {
    NSPoint point=[self convertPoint:native.locationInWindow fromView:nil];
    JaliumPlatformEvent event{}; event.type=type;
    event.mouse.x=point.x*_jaliumOwner->scale;event.mouse.y=point.y*_jaliumOwner->scale;
    event.mouse.button=button;event.mouse.modifiers=ModifiersFromFlags(native.modifierFlags);
    event.mouse.clickCount=static_cast<int32_t>(native.clickCount);
    DispatchWindowEvent(_jaliumOwner,event);
}
- (void)mouseMoved:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_MOVE button:0];}
- (void)mouseDragged:(NSEvent*)e {[self mouseMoved:e];}
- (void)rightMouseDragged:(NSEvent*)e {[self mouseMoved:e];}
- (void)otherMouseDragged:(NSEvent*)e {[self mouseMoved:e];}
- (void)mouseDown:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_DOWN button:JALIUM_MOUSE_BUTTON_LEFT];}
- (void)mouseUp:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_UP button:JALIUM_MOUSE_BUTTON_LEFT];}
- (void)rightMouseDown:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_DOWN button:JALIUM_MOUSE_BUTTON_RIGHT];}
- (void)rightMouseUp:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_UP button:JALIUM_MOUSE_BUTTON_RIGHT];}
- (void)otherMouseDown:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_DOWN button:JALIUM_MOUSE_BUTTON_MIDDLE];}
- (void)otherMouseUp:(NSEvent*)e {[self dispatchMouse:e type:JALIUM_EVENT_MOUSE_UP button:JALIUM_MOUSE_BUTTON_MIDDLE];}
- (void)mouseEntered:(NSEvent*)e {DispatchSimple(_jaliumOwner,JALIUM_EVENT_MOUSE_ENTER);}
- (void)mouseExited:(NSEvent*)e {DispatchSimple(_jaliumOwner,JALIUM_EVENT_MOUSE_LEAVE);}
- (void)scrollWheel:(NSEvent*)native {
    NSPoint point=[self convertPoint:native.locationInWindow fromView:nil];
    JaliumPlatformEvent event{};event.type=JALIUM_EVENT_MOUSE_WHEEL;
    event.wheel.x=point.x*_jaliumOwner->scale;event.wheel.y=point.y*_jaliumOwner->scale;
    event.wheel.deltaX=native.scrollingDeltaX;event.wheel.deltaY=native.scrollingDeltaY;
    event.wheel.modifiers=ModifiersFromFlags(native.modifierFlags);
    DispatchWindowEvent(_jaliumOwner,event);
}
- (void)keyDown:(NSEvent*)native {
    NSString* chars=native.charactersIgnoringModifiers ?: @"";
    unichar c=chars.length?[chars characterAtIndex:0]:0;
    JaliumPlatformEvent event{};event.type=JALIUM_EVENT_KEY_DOWN;
    event.key.keyCode=VirtualKeyFromCharacter(c);event.key.scanCode=native.keyCode;
    event.key.modifiers=ModifiersFromFlags(native.modifierFlags);event.key.isRepeat=native.isARepeat;
    DispatchWindowEvent(_jaliumOwner,event);
    [self interpretKeyEvents:@[native]];
}
- (void)keyUp:(NSEvent*)native {
    NSString* chars=native.charactersIgnoringModifiers ?: @"";
    JaliumPlatformEvent event{};event.type=JALIUM_EVENT_KEY_UP;
    event.key.keyCode=VirtualKeyFromCharacter(chars.length?[chars characterAtIndex:0]:0);
    event.key.scanCode=native.keyCode;event.key.modifiers=ModifiersFromFlags(native.modifierFlags);
    DispatchWindowEvent(_jaliumOwner,event);
}
- (void)insertText:(id)value replacementRange:(NSRange)range {
    NSString* text=[value isKindOfClass:[NSAttributedString class]]?[value string]:value;
    for(NSUInteger i=0;i<text.length;){
        unichar hi=[text characterAtIndex:i++];uint32_t cp=hi;
        if(hi>=0xd800&&hi<=0xdbff&&i<text.length){unichar lo=[text characterAtIndex:i];
            if(lo>=0xdc00&&lo<=0xdfff){++i;cp=0x10000+((hi-0xd800)<<10)+(lo-0xdc00);}}
        JaliumPlatformEvent event{};event.type=JALIUM_EVENT_CHAR_INPUT;
        event.character.codepoint=cp;DispatchWindowEvent(_jaliumOwner,event);
    }
    self.markedText=nil;(void)range;
}
- (void)setMarkedText:(id)value selectedRange:(NSRange)selected replacementRange:(NSRange)replacement {
    NSString* text=[value isKindOfClass:[NSAttributedString class]]?[value string]:value;
    BOOL starting=!self.markedText.length;
    self.markedText=[[NSMutableAttributedString alloc]initWithString:text?:@""];
    self.selectionRange=selected;
    if(starting)DispatchSimple(_jaliumOwner,JALIUM_EVENT_COMPOSITION_START);
    JaliumPlatformEvent event{};event.type=JALIUM_EVENT_COMPOSITION_UPDATE;
    std::string utf8=text.UTF8String?:"";event.composition.utf8Text=utf8.c_str();
    event.composition.cursor=static_cast<int32_t>(selected.location);
    DispatchWindowEvent(_jaliumOwner,event);(void)replacement;
}
- (void)unmarkText {
    if(self.markedText.length){JaliumPlatformEvent event{};event.type=JALIUM_EVENT_COMPOSITION_END;
        std::string utf8=self.markedText.string.UTF8String?:"";event.composition.utf8Text=utf8.c_str();
        DispatchWindowEvent(_jaliumOwner,event);} self.markedText=nil;
}
- (BOOL)hasMarkedText{return self.markedText.length>0;}
- (NSRange)markedRange{return self.markedText.length?NSMakeRange(0,self.markedText.length):NSMakeRange(NSNotFound,0);}
- (NSRange)selectedRange{return _selectionRange;}
- (void)setSelectedRange:(NSRange)value{_selectionRange=value;}
- (NSArray<NSAttributedStringKey>*)validAttributesForMarkedText{return @[];}
- (NSAttributedString*)attributedSubstringForProposedRange:(NSRange)range actualRange:(NSRangePointer)actual{if(actual)*actual=range;return nil;}
- (NSUInteger)characterIndexForPoint:(NSPoint)point{return 0;}
- (NSRect)firstRectForCharacterRange:(NSRange)range actualRange:(NSRangePointer)actual {
    if(actual)*actual=range;NSRect local=self.imeRect;local.origin=[self.window convertPointToScreen:[self convertPoint:local.origin toView:nil]];return local;
}
- (void)doCommandBySelector:(SEL)selector {if(selector==@selector(deleteBackward:)){
    JaliumPlatformEvent e{};e.type=JALIUM_EVENT_KEY_DOWN;e.key.keyCode=0x08;DispatchWindowEvent(_jaliumOwner,e);}}
- (NSInteger)conversationIdentifier{return (NSInteger)(__bridge void*)self;}
@end

@interface JaliumAppleWindowDelegate : NSObject <NSWindowDelegate>
@property(nonatomic, assign) JaliumPlatformWindow* owner;
@end
@implementation JaliumAppleWindowDelegate
- (BOOL)windowShouldClose:(NSWindow*)sender {DispatchSimple(_owner,JALIUM_EVENT_CLOSE_REQUESTED);return NO;}
- (void)windowWillClose:(NSNotification*)note {DispatchSimple(_owner,JALIUM_EVENT_DESTROYED);}
- (void)windowDidResize:(NSNotification*)note {NSSize s=_owner->view.bounds.size;_owner->width=lround(s.width*_owner->scale);_owner->height=lround(s.height*_owner->scale);JaliumPlatformEvent e{};e.type=JALIUM_EVENT_RESIZE;e.resize.width=_owner->width;e.resize.height=_owner->height;DispatchWindowEvent(_owner,e);}
- (void)windowDidMove:(NSNotification*)note {NSPoint p=_owner->window.frame.origin;_owner->x=lround(p.x);_owner->y=lround(p.y);JaliumPlatformEvent e{};e.type=JALIUM_EVENT_MOVE;e.move.x=_owner->x;e.move.y=_owner->y;DispatchWindowEvent(_owner,e);}
- (void)windowDidBecomeKey:(NSNotification*)note {DispatchSimple(_owner,JALIUM_EVENT_ACTIVATE);}
- (void)windowDidResignKey:(NSNotification*)note {DispatchSimple(_owner,JALIUM_EVENT_DEACTIVATE);}
- (void)windowDidEnterFullScreen:(NSNotification*)note {_owner->state=JALIUM_WINDOW_STATE_FULLSCREEN;JaliumPlatformEvent e{};e.type=JALIUM_EVENT_STATE_CHANGED;e.stateChanged.newState=_owner->state;DispatchWindowEvent(_owner,e);}
- (void)windowDidExitFullScreen:(NSNotification*)note {_owner->state=JALIUM_WINDOW_STATE_NORMAL;JaliumPlatformEvent e{};e.type=JALIUM_EVENT_STATE_CHANGED;e.stateChanged.newState=_owner->state;DispatchWindowEvent(_owner,e);}
@end

#else

@interface JaliumAppleView : UIView <UIKeyInput>
@property(nonatomic, assign) JaliumPlatformWindow* jaliumOwner;
@property(nonatomic, strong) UITextInputAssistantItem* jaliumAssistant;
@end
@implementation JaliumAppleView
+ (Class)layerClass{return [CAMetalLayer class];}
- (BOOL)canBecomeFirstResponder{return YES;}
- (BOOL)hasText{return YES;}
- (void)insertText:(NSString*)text {for(NSUInteger i=0;i<text.length;){unichar hi=[text characterAtIndex:i++];uint32_t cp=hi;if(hi>=0xd800&&hi<=0xdbff&&i<text.length){unichar lo=[text characterAtIndex:i];if(lo>=0xdc00&&lo<=0xdfff){++i;cp=0x10000+((hi-0xd800)<<10)+(lo-0xdc00);}}JaliumPlatformEvent e{};e.type=JALIUM_EVENT_CHAR_INPUT;e.character.codepoint=cp;DispatchWindowEvent(_jaliumOwner,e);}}
- (void)deleteBackward {JaliumPlatformEvent e{};e.type=JALIUM_EVENT_KEY_DOWN;e.key.keyCode=0x08;DispatchWindowEvent(_jaliumOwner,e);}
- (void)layoutSubviews {[super layoutSubviews];CGFloat scale=self.contentScaleFactor;_jaliumOwner->scale=scale;_jaliumOwner->width=lround(self.bounds.size.width*scale);_jaliumOwner->height=lround(self.bounds.size.height*scale);JaliumPlatformEvent e{};e.type=JALIUM_EVENT_RESIZE;e.resize.width=_jaliumOwner->width;e.resize.height=_jaliumOwner->height;DispatchWindowEvent(_jaliumOwner,e);}
- (void)safeAreaInsetsDidChange {[super safeAreaInsetsDidChange];UIEdgeInsets i=self.safeAreaInsets;JaliumPlatformEvent e{};e.type=JALIUM_EVENT_SAFE_AREA_CHANGED;e.safeArea.top=i.top;e.safeArea.bottom=i.bottom;e.safeArea.left=i.left;e.safeArea.right=i.right;DispatchWindowEvent(_jaliumOwner,e);}
- (void)dispatchTouches:(NSSet<UITouch*>*)touches type:(JaliumEventType)type {for(UITouch* touch in touches){CGPoint p=[touch locationInView:self];JaliumPlatformEvent e{};e.type=type;e.pointer.pointerId=(uint32_t)((uintptr_t)(__bridge void*)touch&0xffffffffu);e.pointer.x=p.x*self.contentScaleFactor;e.pointer.y=p.y*self.contentScaleFactor;e.pointer.pressure=touch.maximumPossibleForce>0?touch.force/touch.maximumPossibleForce:1;e.pointer.pointerType=touch.type==UITouchTypePencil?JALIUM_POINTER_PEN:JALIUM_POINTER_TOUCH;e.pointer.flags=JALIUM_POINTER_FLAG_IN_RANGE|(type==JALIUM_EVENT_POINTER_UP||type==JALIUM_EVENT_POINTER_CANCEL?0:JALIUM_POINTER_FLAG_IN_CONTACT);e.pointer.toolType=touch.type==UITouchTypePencil?JALIUM_POINTER_TOOL_PENCIL:JALIUM_POINTER_TOOL_UNKNOWN;e.pointer.buttons=type==JALIUM_EVENT_POINTER_UP?0:JALIUM_POINTER_BUTTON_PRIMARY;e.pointer.timestampMillis=MonotonicMillis();if(touch.type==UITouchTypePencil){e.pointer.tiltX=cos(touch.azimuthAngleInView)*touch.altitudeAngle*180/M_PI;e.pointer.tiltY=sin(touch.azimuthAngleInView)*touch.altitudeAngle*180/M_PI;}DispatchWindowEvent(_jaliumOwner,e);}}
- (void)touchesBegan:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t type:JALIUM_EVENT_POINTER_DOWN];}
- (void)touchesMoved:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t type:JALIUM_EVENT_POINTER_MOVE];}
- (void)touchesEnded:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t type:JALIUM_EVENT_POINTER_UP];}
- (void)touchesCancelled:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t type:JALIUM_EVENT_POINTER_CANCEL];}
- (void)pressesBegan:(NSSet<UIPress*>*)presses withEvent:(UIPressesEvent*)event {for(UIPress* press in presses){UIKey* key=press.key;if(!key)continue;JaliumPlatformEvent e{};e.type=JALIUM_EVENT_KEY_DOWN;NSString* chars=key.charactersIgnoringModifiers;e.key.keyCode=VirtualKeyFromCharacter(chars.length?[chars characterAtIndex:0]:0);e.key.scanCode=(int32_t)key.keyCode;e.key.modifiers=ModifiersFromFlags(key.modifierFlags);DispatchWindowEvent(_jaliumOwner,e);} [super pressesBegan:presses withEvent:event];}
- (void)pressesEnded:(NSSet<UIPress*>*)presses withEvent:(UIPressesEvent*)event {for(UIPress* press in presses){UIKey* key=press.key;if(!key)continue;JaliumPlatformEvent e{};e.type=JALIUM_EVENT_KEY_UP;NSString* chars=key.charactersIgnoringModifiers;e.key.keyCode=VirtualKeyFromCharacter(chars.length?[chars characterAtIndex:0]:0);e.key.scanCode=(int32_t)key.keyCode;e.key.modifiers=ModifiersFromFlags(key.modifierFlags);DispatchWindowEvent(_jaliumOwner,e);} [super pressesEnded:presses withEvent:event];}
@end

#endif

JaliumResult jalium_platform_init_impl()
{
#if TARGET_OS_OSX
    [NSApplication sharedApplication];
#endif
    g_quit.store(false);return JALIUM_OK;
}
void jalium_platform_shutdown_impl() {}
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

void jalium_apple_set_root_view(intptr_t nativeView)
{g_rootView=nativeView?(__bridge id)(void*)nativeView:nil;}
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
    NSUInteger mask=NSWindowStyleMaskBorderless;
    if(!(params->style&JALIUM_WINDOW_STYLE_BORDERLESS)){
        mask=NSWindowStyleMaskTitled;
        if(params->style&JALIUM_WINDOW_STYLE_CLOSABLE)mask|=NSWindowStyleMaskClosable;
        if(params->style&JALIUM_WINDOW_STYLE_MINIMIZABLE)mask|=NSWindowStyleMaskMiniaturizable;
        if(params->style&JALIUM_WINDOW_STYLE_RESIZABLE)mask|=NSWindowStyleMaskResizable;
    }
    NSRect rect=NSMakeRect(params->x==JALIUM_DEFAULT_POS?0:params->x,
        params->y==JALIUM_DEFAULT_POS?0:params->y,params->width,params->height);
    result->window=[[NSWindow alloc]initWithContentRect:rect styleMask:mask
        backing:NSBackingStoreBuffered defer:NO];
    if(!result->window)return nullptr;
    result->view=[[JaliumAppleView alloc]initWithFrame:NSMakeRect(0,0,params->width,params->height)];
    result->view.jaliumOwner=result.get();result->window.contentView=result->view;
    result->delegate=[JaliumAppleWindowDelegate new];result->delegate.owner=result.get();
    result->window.delegate=result->delegate;result->window.title=StringFromUtf16(params->title);
    result->window.acceptsMouseMovedEvents=YES;
    result->window.opaque=(params->style&JALIUM_WINDOW_STYLE_TRANSPARENT)==0;
    if(!result->window.opaque){result->window.backgroundColor=NSColor.clearColor;result->window.hasShadow=NO;}
    if(params->style&JALIUM_WINDOW_STYLE_TOPMOST)result->window.level=NSFloatingWindowLevel;
    result->scale=result->window.backingScaleFactor;
#else
    UIView* root=FindRootView();if(!root)return nullptr;
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
    if(!window)return;{std::scoped_lock lock(g_windowsMutex);std::erase(g_windows,window);}
    window->view.jaliumOwner=nullptr;
#if TARGET_OS_OSX
    window->window.delegate=nil;[window->window orderOut:nil];[window->window close];
#else
    [window->view removeFromSuperview];
#endif
    DispatchSimple(window,JALIUM_EVENT_DESTROYED);delete window;
}
void jalium_window_show(JaliumPlatformWindow* w){if(!w)return;w->visible=true;
#if TARGET_OS_OSX
    [w->window makeKeyAndOrderFront:nil];[w->window makeFirstResponder:w->view];
#else
    w->view.hidden=NO;[w->view becomeFirstResponder];
#endif
}
void jalium_window_hide(JaliumPlatformWindow* w){if(!w)return;w->visible=false;
#if TARGET_OS_OSX
    [w->window orderOut:nil];
#else
    w->view.hidden=YES;
#endif
}
void jalium_window_set_title(JaliumPlatformWindow* w,const JaliumUtf16Char* title){if(!w)return;
#if TARGET_OS_OSX
    w->window.title=StringFromUtf16(title);
#else
    (void)title;
#endif
}
void jalium_window_resize(JaliumPlatformWindow* w,int32_t width,int32_t height){if(!w||width<=0||height<=0)return;
#if TARGET_OS_OSX
    [w->window setContentSize:NSMakeSize(width/w->scale,height/w->scale)];
#else
    CGRect f=w->view.frame;f.size=CGSizeMake(width/w->scale,height/w->scale);w->view.frame=f;
#endif
}
void jalium_window_move(JaliumPlatformWindow* w,int32_t x,int32_t y){if(!w)return;w->x=x;w->y=y;
#if TARGET_OS_OSX
    [w->window setFrameOrigin:NSMakePoint(x,y)];
#endif
}
void jalium_window_set_state(JaliumPlatformWindow* w,JaliumWindowState state){if(!w)return;
#if TARGET_OS_OSX
    if(state==JALIUM_WINDOW_STATE_MINIMIZED)[w->window miniaturize:nil];
    else if(state==JALIUM_WINDOW_STATE_MAXIMIZED)[w->window zoom:nil];
    else if(state==JALIUM_WINDOW_STATE_FULLSCREEN&&w->state!=state)[w->window toggleFullScreen:nil];
    else if(w->state==JALIUM_WINDOW_STATE_FULLSCREEN&&state==JALIUM_WINDOW_STATE_NORMAL)[w->window toggleFullScreen:nil];
#endif
    w->state=state;JaliumPlatformEvent e{};e.type=JALIUM_EVENT_STATE_CHANGED;e.stateChanged.newState=state;DispatchWindowEvent(w,e);
}
JaliumWindowState jalium_window_get_state(JaliumPlatformWindow* w){return w?w->state:JALIUM_WINDOW_STATE_NORMAL;}
intptr_t jalium_window_get_native_handle(JaliumPlatformWindow* w){return w?(intptr_t)(__bridge void*)w->view:0;}
JaliumSurfaceDescriptor jalium_window_get_surface(JaliumPlatformWindow* w){JaliumSurfaceDescriptor d{};if(!w)return d;d.platform=jalium_platform_get_current_impl();d.kind=(w->style&JALIUM_WINDOW_STYLE_TRANSPARENT)?JALIUM_SURFACE_KIND_COMPOSITION_TARGET:JALIUM_SURFACE_KIND_NATIVE_WINDOW;d.handle0=jalium_window_get_native_handle(w);return d;}
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
void jalium_window_set_cursor(JaliumPlatformWindow*,JaliumCursorShape cursor){
#if TARGET_OS_OSX
    NSCursor* c=NSCursor.arrowCursor;if(cursor==JALIUM_CURSOR_HAND)c=NSCursor.pointingHandCursor;else if(cursor==JALIUM_CURSOR_IBEAM)c=NSCursor.IBeamCursor;else if(cursor==JALIUM_CURSOR_CROSSHAIR)c=NSCursor.crosshairCursor;else if(cursor==JALIUM_CURSOR_HIDDEN){[NSCursor hide];return;}[c set];
#else
    (void)cursor;
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

JaliumResult jalium_timer_create(JaliumTimer** out){if(!out)return JALIUM_ERROR_INVALID_ARGUMENT;auto* t=new JaliumTimer();t->fired=dispatch_semaphore_create(0);t->source=dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER,0,0,dispatch_get_global_queue(QOS_CLASS_USER_INTERACTIVE,0));if(!t->source){delete t;return JALIUM_ERROR_RESOURCE_CREATION_FAILED;}dispatch_source_set_event_handler(t->source,^{if(!t->alive.load())return;dispatch_semaphore_signal(t->fired);JaliumTimerCallback cb=nullptr;void* data=nullptr;{std::scoped_lock lock(t->mutex);cb=t->callback;data=t->userData;}if(cb)cb(data);});dispatch_source_set_cancel_handler(t->source,^{delete t;});dispatch_resume(t->source);*out=t;return JALIUM_OK;}
void jalium_timer_destroy(JaliumTimer* t){if(!t)return;t->alive.store(false);dispatch_source_cancel(t->source);}
static void ArmTimer(JaliumTimer* t,int64_t us,bool repeat){if(!t||us<0)return;uint64_t ns=(uint64_t)us*1000;dispatch_source_set_timer(t->source,dispatch_time(DISPATCH_TIME_NOW,ns),repeat?ns:DISPATCH_TIME_FOREVER,std::min<uint64_t>(ns/20,1000000));}
void jalium_timer_arm(JaliumTimer* t,int64_t us){ArmTimer(t,us,false);}
void jalium_timer_arm_repeating(JaliumTimer* t,int64_t us){ArmTimer(t,us,true);}
void jalium_timer_disarm(JaliumTimer* t){if(t)dispatch_source_set_timer(t->source,DISPATCH_TIME_FOREVER,DISPATCH_TIME_FOREVER,0);}
void jalium_timer_set_callback(JaliumTimer* t,JaliumTimerCallback cb,void* data){if(!t)return;std::scoped_lock lock(t->mutex);t->callback=cb;t->userData=data;}
int32_t jalium_timer_wait(JaliumTimer* t,uint32_t ms){if(!t)return 0;dispatch_time_t timeout=ms?dispatch_time(DISPATCH_TIME_NOW,(int64_t)ms*NSEC_PER_MSEC):DISPATCH_TIME_FOREVER;return dispatch_semaphore_wait(t->fired,timeout)==0?1:0;}

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
    NSScreen* s=NSScreen.screens[index];NSRect f=s.frame,v=s.visibleFrame;CGFloat scale=s.backingScaleFactor;info->x=lround(f.origin.x*scale);info->y=lround(f.origin.y*scale);info->width=lround(f.size.width*scale);info->height=lround(f.size.height*scale);info->workX=lround(v.origin.x*scale);info->workY=lround(v.origin.y*scale);info->workWidth=lround(v.size.width*scale);info->workHeight=lround(v.size.height*scale);info->scale=scale;NSNumber* number=s.deviceDescription[@"NSScreenNumber"];CGDisplayModeRef mode=number?CGDisplayCopyDisplayMode(number.unsignedIntValue):nullptr;double hz=mode?CGDisplayModeGetRefreshRate(mode):0;if(mode)CGDisplayModeRelease(mode);info->refreshRate=hz>1?(int32_t)llround(hz):60;info->isPrimary=index==0;
#else
    UIScreen* s=UIScreen.mainScreen;CGFloat scale=s.scale;CGRect f=s.bounds;info->width=lround(f.size.width*scale);info->height=lround(f.size.height*scale);info->workWidth=info->width;info->workHeight=info->height;info->scale=scale;info->refreshRate=(int32_t)s.maximumFramesPerSecond;info->isPrimary=1;
#endif
    return JALIUM_OK;}

int32_t jalium_window_set_min_max_size(JaliumPlatformWindow* w,int32_t minW,int32_t minH,int32_t maxW,int32_t maxH){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    w->window.contentMinSize=NSMakeSize(minW/w->scale,minH/w->scale);w->window.contentMaxSize=NSMakeSize(maxW?maxW/w->scale:CGFLOAT_MAX,maxH?maxH/w->scale:CGFLOAT_MAX);return JALIUM_OK;
#else
    (void)minW;(void)minH;(void)maxW;(void)maxH;return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_begin_move_drag(JaliumPlatformWindow* w){
#if TARGET_OS_OSX
    if(!w||!NSApp.currentEvent)return JALIUM_ERROR_INVALID_STATE;[w->window performWindowDragWithEvent:NSApp.currentEvent];return JALIUM_OK;
#else
    (void)w;return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_begin_resize_drag(JaliumPlatformWindow*,int32_t){return JALIUM_ERROR_NOT_SUPPORTED;}
int32_t jalium_window_set_icon(JaliumPlatformWindow*,const uint32_t*,int32_t,int32_t){return JALIUM_ERROR_NOT_SUPPORTED;}
int32_t jalium_window_set_topmost(JaliumPlatformWindow* w,int32_t top){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    w->window.level=top?NSFloatingWindowLevel:NSNormalWindowLevel;return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_set_enabled(JaliumPlatformWindow* w,int32_t enabled){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;w->enabled=enabled!=0;
#if !TARGET_OS_OSX
    w->view.userInteractionEnabled=enabled!=0;
#endif
    return JALIUM_OK;}
int32_t jalium_window_set_opacity(JaliumPlatformWindow* w,double opacity){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;opacity=std::clamp(opacity,0.0,1.0);
#if TARGET_OS_OSX
    w->window.alphaValue=opacity;
#else
    w->view.alpha=opacity;
#endif
    return JALIUM_OK;}
int32_t jalium_window_set_show_in_taskbar(JaliumPlatformWindow*,int32_t){return JALIUM_ERROR_NOT_SUPPORTED;}
int32_t jalium_window_set_resizable(JaliumPlatformWindow* w,int32_t value){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if(value)w->window.styleMask|=NSWindowStyleMaskResizable;else w->window.styleMask&=~NSWindowStyleMaskResizable;return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_set_decorated(JaliumPlatformWindow* w,int32_t value){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    w->window.titleVisibility=value?NSWindowTitleVisible:NSWindowTitleHidden;w->window.titlebarAppearsTransparent=!value;return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_set_owner(JaliumPlatformWindow* w,intptr_t owner){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if(owner){id o=(__bridge id)(void*)owner;NSWindow* ownerWindow=[o isKindOfClass:[NSWindow class]]?o:[o window];if(ownerWindow)[ownerWindow addChildWindow:w->window ordered:NSWindowAbove];}return JALIUM_OK;
#else
    (void)owner;return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
int32_t jalium_window_activate(JaliumPlatformWindow* w){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    [NSApp activateIgnoringOtherApps:YES];[w->window makeKeyAndOrderFront:nil];
#else
    [w->view becomeFirstResponder];
#endif
    return JALIUM_OK;}
int32_t jalium_window_show_system_menu(JaliumPlatformWindow*,int32_t,int32_t){return JALIUM_ERROR_NOT_SUPPORTED;}
int32_t jalium_window_update_ime_context(JaliumPlatformWindow* w,int32_t enabled,const char*,int32_t,int32_t,int32_t x,int32_t y,int32_t width,int32_t height){if(!w)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    w->view.imeRect=NSMakeRect(x/w->scale,y/w->scale,width/w->scale,height/w->scale);if(enabled)[w->window makeFirstResponder:w->view];
#else
    if(enabled)[w->view becomeFirstResponder];else [w->view resignFirstResponder];
#endif
    return JALIUM_OK;}
int16_t jalium_input_get_key_state(int32_t){return 0;}
JaliumResult jalium_input_get_touch_capabilities(int32_t* present,int32_t* contacts){if(!present||!contacts)return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    *present=0;*contacts=0;
#else
    *present=1;*contacts=10;
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
void jalium_drag_set_effect(JaliumPlatformWindow* w,uint64_t id,uint32_t effect){if(w&&w->dragSession==id)w->dragEffect=effect;}
JaliumResult jalium_drag_begin(JaliumPlatformWindow* w,const JaliumDragDataItem* i,uint32_t c,uint32_t a,uint32_t* p){return jalium_drag_begin_with_image(w,i,c,a,nullptr,nullptr,nullptr,nullptr,p);}
JaliumResult jalium_drag_begin_ex(JaliumPlatformWindow* w,const JaliumDragDataItem* i,uint32_t c,uint32_t a,JaliumDragFeedbackCallback f,JaliumDragQueryContinueCallback q,void* u,uint32_t* p){return jalium_drag_begin_with_image(w,i,c,a,f,q,u,nullptr,p);}
JaliumResult jalium_drag_begin_with_image(JaliumPlatformWindow*,const JaliumDragDataItem*,uint32_t,uint32_t,JaliumDragFeedbackCallback,JaliumDragQueryContinueCallback,void*,const JaliumDragImage*,uint32_t* performed){if(performed)*performed=JALIUM_DRAG_EFFECT_NONE;return JALIUM_ERROR_NOT_SUPPORTED;}

JaliumResult jalium_clipboard_get_formats(char** out){if(!out)return JALIUM_ERROR_INVALID_ARGUMENT;*out=nullptr;
#if TARGET_OS_OSX
    NSArray<NSPasteboardType>* types=NSPasteboard.generalPasteboard.types;
#else
    NSArray<NSString*>* types=UIPasteboard.generalPasteboard.types;
#endif
    NSMutableString* joined=[NSMutableString string];for(NSString* type in types){if(joined.length)[joined appendString:@"\n"];[joined appendString:type];}
    const char* utf8=joined.UTF8String?:"";size_t n=strlen(utf8)+1;*out=(char*)malloc(n);if(!*out)return JALIUM_ERROR_OUT_OF_MEMORY;memcpy(*out,utf8,n);return JALIUM_OK;}
JaliumResult jalium_clipboard_get_data(const char* mime,uint8_t** out,uint32_t* size){if(!mime||!out||!size)return JALIUM_ERROR_INVALID_ARGUMENT;*out=nullptr;*size=0;NSString* type=[NSString stringWithUTF8String:mime];
#if TARGET_OS_OSX
    NSData* data=[NSPasteboard.generalPasteboard dataForType:type];
#else
    NSData* data=[UIPasteboard.generalPasteboard dataForPasteboardType:type];
#endif
    if(!data)return JALIUM_OK;*size=(uint32_t)data.length;*out=(uint8_t*)malloc(std::max<NSUInteger>(data.length,1));if(!*out)return JALIUM_ERROR_OUT_OF_MEMORY;if(data.length)memcpy(*out,data.bytes,data.length);return JALIUM_OK;}
JaliumResult jalium_clipboard_set_data(const JaliumClipboardDataItem* items,uint32_t count){
#if TARGET_OS_OSX
    NSPasteboard* pb=NSPasteboard.generalPasteboard;[pb clearContents];NSMutableArray<NSPasteboardType>* types=[NSMutableArray array];for(uint32_t i=0;i<count;++i){if(items[i].mimeType)[types addObject:[NSString stringWithUTF8String:items[i].mimeType]];}[pb declareTypes:types owner:nil];for(uint32_t i=0;i<count;++i){if(!items[i].mimeType)continue;NSString* type=[NSString stringWithUTF8String:items[i].mimeType];NSData* data=[NSData dataWithBytes:items[i].data length:items[i].dataSize];if(![pb setData:data forType:type])return JALIUM_ERROR_INVALID_STATE;}return JALIUM_OK;
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
