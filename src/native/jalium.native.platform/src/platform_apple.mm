#include "jalium_platform.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <pthread.h>
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
    std::atomic<bool> signalPending{false};
    CADisplayLink* displayLink = nil;
    id displayLinkTarget = nil;
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
    __weak UIView* sceneRoot = nil;
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
    std::atomic<bool> dragRunning{false};
    JaliumDragFeedbackCallback dragFeedback = nullptr;
    JaliumDragQueryContinueCallback dragQuery = nullptr;
    void* dragUserData = nullptr;
};

namespace {

std::mutex g_windowsMutex;
std::vector<JaliumPlatformWindow*> g_windows;
std::atomic<int32_t> g_exitCode{0};
std::atomic<bool> g_quit{false};
__weak id g_rootView = nil;
#if !TARGET_OS_OSX
struct SceneRoot { std::string id; __weak UIView* view=nil; uint32_t claims=0; };
std::vector<SceneRoot> g_sceneRoots;
#endif

void RetainDispatcher(JaliumDispatcher* dispatcher)
{ dispatcher->refs.fetch_add(1, std::memory_order_relaxed); }
void ReleaseDispatcher(JaliumDispatcher* dispatcher)
{ if (dispatcher->refs.fetch_sub(1, std::memory_order_acq_rel) == 1) delete dispatcher; }

int64_t MonotonicMillis()
{
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}

void FireTimer(JaliumTimer* timer)
{
    if(!timer||!timer->alive.load(std::memory_order_acquire))return;
    if(!timer->signalPending.exchange(true,std::memory_order_acq_rel))
        dispatch_semaphore_signal(timer->fired);
    JaliumTimerCallback callback=nullptr;void* data=nullptr;
    {std::scoped_lock lock(timer->mutex);callback=timer->callback;data=timer->userData;}
    if(callback)callback(data);
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

#if TARGET_OS_OSX
uint32_t EffectsFromOperation(NSDragOperation operation)
{
    uint32_t result=JALIUM_DRAG_EFFECT_NONE;
    if(operation&NSDragOperationCopy)result|=JALIUM_DRAG_EFFECT_COPY;
    if(operation&NSDragOperationMove)result|=JALIUM_DRAG_EFFECT_MOVE;
    if(operation&NSDragOperationLink)result|=JALIUM_DRAG_EFFECT_LINK;
    return result;
}

NSDragOperation OperationFromEffects(uint32_t effects)
{
    NSDragOperation result=NSDragOperationNone;
    if(effects&JALIUM_DRAG_EFFECT_COPY)result|=NSDragOperationCopy;
    if(effects&JALIUM_DRAG_EFFECT_MOVE)result|=NSDragOperationMove;
    if(effects&JALIUM_DRAG_EFFECT_LINK)result|=NSDragOperationLink;
    return result;
}

std::string PasteboardTypesUtf8(NSPasteboard* pasteboard)
{
    std::string result;
    for(NSPasteboardType type in pasteboard.types){if(!result.empty())result.push_back('\n');
        NSString* mapped=type;
        if([type isEqualToString:NSPasteboardTypeString])mapped=@"text/plain;charset=utf-8";
        else if([type isEqualToString:NSPasteboardTypeURL]||
                [type isEqualToString:NSPasteboardTypeFileURL])mapped=@"text/uri-list";
        else if([type isEqualToString:NSPasteboardTypePNG])mapped=@"image/png";
        else if([type isEqualToString:NSPasteboardTypeTIFF])mapped=@"image/tiff";
        const char* utf8=mapped.UTF8String;if(utf8)result+=utf8;}
    return result;
}

NSPasteboardType PasteboardTypeFromMime(const char* mime)
{
    if(!mime)return nil;NSString* value=[NSString stringWithUTF8String:mime];
    if([value hasPrefix:@"text/plain"])return NSPasteboardTypeString;
    if([value isEqualToString:@"text/uri-list"])return NSPasteboardTypeURL;
    if([value isEqualToString:@"image/png"])return NSPasteboardTypePNG;
    if([value isEqualToString:@"image/tiff"])return NSPasteboardTypeTIFF;
    return value;
}

const char* MimeForPasteboardType(NSPasteboardType type,std::string& storage)
{
    if([type isEqualToString:NSPasteboardTypeString])storage="text/plain;charset=utf-8";
    else if([type isEqualToString:NSPasteboardTypeURL]||
            [type isEqualToString:NSPasteboardTypeFileURL])storage="text/uri-list";
    else if([type isEqualToString:NSPasteboardTypePNG])storage="image/png";
    else if([type isEqualToString:NSPasteboardTypeTIFF])storage="image/tiff";
    else storage=type.UTF8String?:"";return storage.c_str();
}

void DispatchDragEvent(JaliumPlatformWindow* window,id<NSDraggingInfo> info,
    JaliumEventType type,const char* dataMime=nullptr,const uint8_t* data=nullptr,
    uint32_t dataSize=0)
{
    if(!window)return;
    NSPoint point=[window->view convertPoint:info.draggingLocation fromView:nil];
    std::string types=PasteboardTypesUtf8(info.draggingPasteboard);
    window->dragSession=static_cast<uint64_t>(info.draggingSequenceNumber);
    if(type==JALIUM_EVENT_DRAG_ENTER)window->dragEffect=JALIUM_DRAG_EFFECT_NONE;
    JaliumPlatformEvent event{};event.type=type;
    event.drag.x=point.x*window->scale;event.drag.y=point.y*window->scale;
    event.drag.allowedEffects=EffectsFromOperation(info.draggingSourceOperationMask);
    event.drag.sessionId=window->dragSession;event.drag.mimeTypes=types.c_str();
    event.drag.dataMimeType=dataMime;event.drag.data=data;event.drag.dataSize=dataSize;
    DispatchWindowEvent(window,event);
}
#endif

#if !TARGET_OS_OSX
UIView* AcquireRootView()
{
    {std::scoped_lock lock(g_windowsMutex);
        for(auto& root:g_sceneRoots)if(root.view&&root.claims==0){++root.claims;return root.view;}}
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

void ReleaseRootView(UIView* view)
{
    if(!view)return;std::scoped_lock lock(g_windowsMutex);
    for(auto& root:g_sceneRoots)if(root.view==view&&root.claims){--root.claims;break;}
}
#endif

} // namespace

@interface JaliumDisplayLinkTarget : NSObject
@property(nonatomic,assign) JaliumTimer* timer;
- (void)displayLinkTick:(CADisplayLink*)link;
@end
@implementation JaliumDisplayLinkTarget
- (void)displayLinkTick:(CADisplayLink*)link { FireTimer(_timer); (void)link; }
@end

#if TARGET_OS_OSX

@interface JaliumAppleView : NSView <NSTextInputClient,NSDraggingDestination,NSDraggingSource>
@property(nonatomic, assign) JaliumPlatformWindow* jaliumOwner;
@property(nonatomic, strong) NSMutableAttributedString* markedText;
@property(nonatomic) NSRange selectionRange;
@property(nonatomic) NSRect imeRect;
@end

@implementation JaliumAppleView
+ (Class)layerClass { return [CAMetalLayer class]; }
- (instancetype)initWithFrame:(NSRect)frame {self=[super initWithFrame:frame];if(self){
    [self registerForDraggedTypes:@[NSPasteboardTypeString,NSPasteboardTypeURL,
        NSPasteboardTypeFileURL,NSPasteboardTypePNG,NSPasteboardTypeTIFF]];}return self;}
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
- (NSDragOperation)draggingEntered:(id<NSDraggingInfo>)sender {DispatchDragEvent(_jaliumOwner,sender,JALIUM_EVENT_DRAG_ENTER);return OperationFromEffects(_jaliumOwner->dragEffect);}
- (NSDragOperation)draggingUpdated:(id<NSDraggingInfo>)sender {DispatchDragEvent(_jaliumOwner,sender,JALIUM_EVENT_DRAG_OVER);return OperationFromEffects(_jaliumOwner->dragEffect);}
- (void)draggingExited:(id<NSDraggingInfo>)sender {DispatchDragEvent(_jaliumOwner,sender,JALIUM_EVENT_DRAG_LEAVE);_jaliumOwner->dragSession=0;}
- (BOOL)performDragOperation:(id<NSDraggingInfo>)sender {NSPasteboard* pasteboard=sender.draggingPasteboard;NSPasteboardType selected=nil;NSData* payload=nil;for(NSPasteboardType type in pasteboard.types){if([type isEqualToString:NSPasteboardTypeString]||[type isEqualToString:NSPasteboardTypeURL]||[type isEqualToString:NSPasteboardTypeFileURL]){NSString* value=[pasteboard stringForType:type];payload=[value dataUsingEncoding:NSUTF8StringEncoding];}else payload=[pasteboard dataForType:type];if(payload){selected=type;break;}}std::string mimeStorage;const char* mime=MimeForPasteboardType(selected,mimeStorage);DispatchDragEvent(_jaliumOwner,sender,JALIUM_EVENT_DROP,mime,(const uint8_t*)payload.bytes,(uint32_t)payload.length);BOOL accepted=_jaliumOwner->dragEffect!=JALIUM_DRAG_EFFECT_NONE;_jaliumOwner->dragSession=0;return accepted;}
- (NSDragOperation)draggingSession:(NSDraggingSession*)session sourceOperationMaskForDraggingContext:(NSDraggingContext)context {(void)session;(void)context;return OperationFromEffects(_jaliumOwner->dragEffect);}
- (void)draggingSession:(NSDraggingSession*)session movedToPoint:(NSPoint)screenPoint {if(_jaliumOwner->dragQuery){NSEvent* event=NSApp.currentEvent;uint32_t keys=0;if(event.modifierFlags&NSEventModifierFlagControl)keys|=0x08;if(event.modifierFlags&NSEventModifierFlagShift)keys|=0x04;if(event.modifierFlags&NSEventModifierFlagOption)keys|=0x20;JaliumDragContinueAction action=_jaliumOwner->dragQuery(keys,0,_jaliumOwner->dragUserData);if(action==JALIUM_DRAG_CANCEL)[session cancelDragging];}if(_jaliumOwner->dragFeedback)_jaliumOwner->dragFeedback(_jaliumOwner->dragEffect,_jaliumOwner->dragUserData);(void)screenPoint;}
- (void)draggingSession:(NSDraggingSession*)session endedAtPoint:(NSPoint)screenPoint operation:(NSDragOperation)operation {_jaliumOwner->dragEffect=EffectsFromOperation(operation);_jaliumOwner->dragRunning.store(false);JaliumPlatformEvent event{};event.type=JALIUM_EVENT_DRAG_FINISHED;event.drag.allowedEffects=_jaliumOwner->dragEffect;DispatchWindowEvent(_jaliumOwner,event);(void)session;(void)screenPoint;}
- (BOOL)ignoreModifierKeysForDraggingSession:(NSDraggingSession*)session {(void)session;return NO;}
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

#if TARGET_OS_TV
@interface JaliumAppleView : UIView <UIKeyInput>
#else
@interface JaliumAppleView : UIView <UIKeyInput,UIDropInteractionDelegate>
#endif
@property(nonatomic, assign) JaliumPlatformWindow* jaliumOwner;
@property(nonatomic, strong) UITextInputAssistantItem* jaliumAssistant;
@property(nonatomic) NSInteger lastOrientation;
@end
@implementation JaliumAppleView
+ (Class)layerClass{return [CAMetalLayer class];}
- (instancetype)initWithFrame:(CGRect)frame {self=[super initWithFrame:frame];if(self){_lastOrientation=-1;NSNotificationCenter* center=NSNotificationCenter.defaultCenter;[center addObserver:self selector:@selector(keyboardFrameChanged:) name:UIKeyboardWillChangeFrameNotification object:nil];[center addObserver:self selector:@selector(keyboardHidden:) name:UIKeyboardWillHideNotification object:nil];
#if !TARGET_OS_TV
    [self addInteraction:[[UIDropInteraction alloc]initWithDelegate:self]];
#endif
    }return self;}
- (void)dealloc {[NSNotificationCenter.defaultCenter removeObserver:self];}
- (BOOL)canBecomeFirstResponder{return YES;}
- (BOOL)hasText{return YES;}
- (void)insertText:(NSString*)text {for(NSUInteger i=0;i<text.length;){unichar hi=[text characterAtIndex:i++];uint32_t cp=hi;if(hi>=0xd800&&hi<=0xdbff&&i<text.length){unichar lo=[text characterAtIndex:i];if(lo>=0xdc00&&lo<=0xdfff){++i;cp=0x10000+((hi-0xd800)<<10)+(lo-0xdc00);}}JaliumPlatformEvent e{};e.type=JALIUM_EVENT_CHAR_INPUT;e.character.codepoint=cp;DispatchWindowEvent(_jaliumOwner,e);}}
- (void)deleteBackward {JaliumPlatformEvent e{};e.type=JALIUM_EVENT_KEY_DOWN;e.key.keyCode=0x08;DispatchWindowEvent(_jaliumOwner,e);}
- (void)layoutSubviews {[super layoutSubviews];if(!_jaliumOwner)return;CGFloat scale=self.contentScaleFactor;_jaliumOwner->scale=scale;_jaliumOwner->width=lround(self.bounds.size.width*scale);_jaliumOwner->height=lround(self.bounds.size.height*scale);JaliumPlatformEvent e{};e.type=JALIUM_EVENT_RESIZE;e.resize.width=_jaliumOwner->width;e.resize.height=_jaliumOwner->height;DispatchWindowEvent(_jaliumOwner,e);UIInterfaceOrientation native=self.window.windowScene.interfaceOrientation;NSInteger orientation=(native==UIInterfaceOrientationLandscapeLeft?1:native==UIInterfaceOrientationPortraitUpsideDown?2:native==UIInterfaceOrientationLandscapeRight?3:0);if(orientation!=self.lastOrientation){self.lastOrientation=orientation;JaliumPlatformEvent changed{};changed.type=JALIUM_EVENT_ORIENTATION_CHANGED;changed.orientationChanged.orientation=(int32_t)orientation;DispatchWindowEvent(_jaliumOwner,changed);}}
- (void)safeAreaInsetsDidChange {[super safeAreaInsetsDidChange];if(!_jaliumOwner)return;UIEdgeInsets i=self.safeAreaInsets;CGFloat scale=self.contentScaleFactor;JaliumPlatformEvent e{};e.type=JALIUM_EVENT_SAFE_AREA_CHANGED;e.safeArea.top=i.top*scale;e.safeArea.bottom=i.bottom*scale;e.safeArea.left=i.left*scale;e.safeArea.right=i.right*scale;DispatchWindowEvent(_jaliumOwner,e);}
- (void)keyboardFrameChanged:(NSNotification*)notification {if(!_jaliumOwner||!self.window)return;CGRect screen=[notification.userInfo[UIKeyboardFrameEndUserInfoKey] CGRectValue];CGRect local=[self convertRect:screen fromView:nil];CGRect overlap=CGRectIntersection(self.bounds,local);JaliumPlatformEvent e{};e.type=JALIUM_EVENT_KEYBOARD_CHANGED;e.keyboard.visible=!CGRectIsNull(overlap)&&overlap.size.height>0.5;e.keyboard.heightPx=e.keyboard.visible?(int32_t)lround(overlap.size.height*self.contentScaleFactor):0;DispatchWindowEvent(_jaliumOwner,e);}
- (void)keyboardHidden:(NSNotification*)notification {if(!_jaliumOwner)return;JaliumPlatformEvent e{};e.type=JALIUM_EVENT_KEYBOARD_CHANGED;e.keyboard.visible=0;e.keyboard.heightPx=0;DispatchWindowEvent(_jaliumOwner,e);(void)notification;}
- (void)dispatchTouchSample:(UITouch*)touch type:(JaliumEventType)type flags:(uint32_t)sampleFlags primary:(BOOL)primary {CGPoint p=[touch locationInView:self];JaliumPlatformEvent e{};e.type=type;e.pointer.pointerId=(uint32_t)((uintptr_t)(__bridge void*)touch&0xffffffffu);e.pointer.x=p.x*self.contentScaleFactor;e.pointer.y=p.y*self.contentScaleFactor;e.pointer.pressure=touch.maximumPossibleForce>0?touch.force/touch.maximumPossibleForce:1;e.pointer.pointerType=touch.type==UITouchTypePencil?JALIUM_POINTER_PEN:JALIUM_POINTER_TOUCH;e.pointer.flags=JALIUM_POINTER_FLAG_IN_RANGE|sampleFlags|(primary?JALIUM_POINTER_FLAG_PRIMARY:0)|(type==JALIUM_EVENT_POINTER_UP||type==JALIUM_EVENT_POINTER_CANCEL?0:JALIUM_POINTER_FLAG_IN_CONTACT);e.pointer.toolType=touch.type==UITouchTypePencil?JALIUM_POINTER_TOOL_PENCIL:JALIUM_POINTER_TOOL_UNKNOWN;e.pointer.buttons=type==JALIUM_EVENT_POINTER_UP?0:JALIUM_POINTER_BUTTON_PRIMARY;e.pointer.timestampMillis=(int64_t)llround(touch.timestamp*1000.0);if(touch.type==UITouchTypePencil){CGFloat azimuth=[touch azimuthAngleInView:self];CGFloat tilt=(M_PI_2-touch.altitudeAngle)*180.0/M_PI;e.pointer.tiltX=cos(azimuth)*tilt;e.pointer.tiltY=sin(azimuth)*tilt;e.pointer.twist=azimuth*180.0/M_PI;}DispatchWindowEvent(_jaliumOwner,e);}
- (void)dispatchTouches:(NSSet<UITouch*>*)touches event:(UIEvent*)event type:(JaliumEventType)type {UITouch* primary=event.allTouches.anyObject;for(UITouch* touch in touches){BOOL isPrimary=touch==primary;if(type==JALIUM_EVENT_POINTER_MOVE){NSArray<UITouch*>* coalesced=[event coalescedTouchesForTouch:touch];for(UITouch* sample in coalesced)[self dispatchTouchSample:sample type:type flags:JALIUM_POINTER_FLAG_COALESCED primary:isPrimary];NSArray<UITouch*>* predicted=[event predictedTouchesForTouch:touch];for(UITouch* sample in predicted)[self dispatchTouchSample:sample type:type flags:JALIUM_POINTER_FLAG_PREDICTED primary:isPrimary];if(coalesced.count==0)[self dispatchTouchSample:touch type:type flags:0 primary:isPrimary];}else [self dispatchTouchSample:touch type:type flags:0 primary:isPrimary];}}
- (void)touchesBegan:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t event:e type:JALIUM_EVENT_POINTER_DOWN];}
- (void)touchesMoved:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t event:e type:JALIUM_EVENT_POINTER_MOVE];}
- (void)touchesEnded:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t event:e type:JALIUM_EVENT_POINTER_UP];}
- (void)touchesCancelled:(NSSet<UITouch*>*)t withEvent:(UIEvent*)e {[self dispatchTouches:t event:e type:JALIUM_EVENT_POINTER_CANCEL];}
- (int32_t)virtualKeyForPress:(UIPress*)press {UIKey* key=press.key;if(key){NSString* chars=key.charactersIgnoringModifiers;return VirtualKeyFromCharacter(chars.length?[chars characterAtIndex:0]:0);}switch(press.type){case UIPressTypeUpArrow:return 0x26;case UIPressTypeDownArrow:return 0x28;case UIPressTypeLeftArrow:return 0x25;case UIPressTypeRightArrow:return 0x27;case UIPressTypeSelect:return 0x0d;case UIPressTypeMenu:return 0x1b;case UIPressTypePlayPause:return 0xb3;default:return 0;}}
- (void)dispatchPresses:(NSSet<UIPress*>*)presses type:(JaliumEventType)type {for(UIPress* press in presses){int32_t virtualKey=[self virtualKeyForPress:press];if(!virtualKey)continue;UIKey* key=press.key;JaliumPlatformEvent e{};e.type=type;e.key.keyCode=virtualKey;e.key.scanCode=key?(int32_t)key.keyCode:(int32_t)press.type;e.key.modifiers=key?ModifiersFromFlags(key.modifierFlags):0;DispatchWindowEvent(_jaliumOwner,e);}}
- (void)pressesBegan:(NSSet<UIPress*>*)presses withEvent:(UIPressesEvent*)event {[self dispatchPresses:presses type:JALIUM_EVENT_KEY_DOWN];[super pressesBegan:presses withEvent:event];}
- (void)pressesEnded:(NSSet<UIPress*>*)presses withEvent:(UIPressesEvent*)event {[self dispatchPresses:presses type:JALIUM_EVENT_KEY_UP];[super pressesEnded:presses withEvent:event];}
#if !TARGET_OS_TV
- (std::string)dropTypes:(id<UIDropSession>)session {std::string result;for(UIDragItem* item in session.items)for(NSString* type in item.itemProvider.registeredTypeIdentifiers){const char* utf8=type.UTF8String;if(!utf8)continue;if(!result.empty())result.push_back('\n');result+=utf8;}return result;}
- (void)dispatchDropSession:(id<UIDropSession>)session type:(JaliumEventType)type mime:(const char*)mime data:(const uint8_t*)data size:(uint32_t)size {if(!_jaliumOwner)return;CGPoint point=[session locationInView:self];std::string types=[self dropTypes:session];uint64_t identifier=(uint64_t)(__bridge void*)session;_jaliumOwner->dragSession=identifier;if(type==JALIUM_EVENT_DRAG_ENTER)_jaliumOwner->dragEffect=JALIUM_DRAG_EFFECT_NONE;JaliumPlatformEvent event{};event.type=type;event.drag.x=point.x*self.contentScaleFactor;event.drag.y=point.y*self.contentScaleFactor;event.drag.allowedEffects=JALIUM_DRAG_EFFECT_COPY|JALIUM_DRAG_EFFECT_MOVE;event.drag.sessionId=identifier;event.drag.mimeTypes=types.c_str();event.drag.dataMimeType=mime;event.drag.data=data;event.drag.dataSize=size;DispatchWindowEvent(_jaliumOwner,event);}
- (BOOL)dropInteraction:(UIDropInteraction*)interaction canHandleSession:(id<UIDropSession>)session {(void)interaction;return session.items.count>0;}
- (void)dropInteraction:(UIDropInteraction*)interaction sessionDidEnter:(id<UIDropSession>)session {(void)interaction;[self dispatchDropSession:session type:JALIUM_EVENT_DRAG_ENTER mime:nullptr data:nullptr size:0];}
- (UIDropProposal*)dropInteraction:(UIDropInteraction*)interaction sessionDidUpdate:(id<UIDropSession>)session {(void)interaction;[self dispatchDropSession:session type:JALIUM_EVENT_DRAG_OVER mime:nullptr data:nullptr size:0];UIDropOperation operation=UIDropOperationCancel;if(_jaliumOwner->dragEffect&JALIUM_DRAG_EFFECT_MOVE)operation=UIDropOperationMove;else if(_jaliumOwner->dragEffect&JALIUM_DRAG_EFFECT_COPY)operation=UIDropOperationCopy;return [[UIDropProposal alloc]initWithDropOperation:operation];}
- (void)dropInteraction:(UIDropInteraction*)interaction sessionDidExit:(id<UIDropSession>)session {(void)interaction;[self dispatchDropSession:session type:JALIUM_EVENT_DRAG_LEAVE mime:nullptr data:nullptr size:0];_jaliumOwner->dragSession=0;}
- (void)dropInteraction:(UIDropInteraction*)interaction performDrop:(id<UIDropSession>)session {(void)interaction;UIDragItem* item=session.items.firstObject;NSString* type=item.itemProvider.registeredTypeIdentifiers.firstObject;if(!type){[self dispatchDropSession:session type:JALIUM_EVENT_DROP mime:nullptr data:nullptr size:0];return;}__weak JaliumAppleView* weakSelf=self;[item.itemProvider loadDataRepresentationForTypeIdentifier:type completionHandler:^(NSData* data,NSError*){dispatch_async(dispatch_get_main_queue(),^{JaliumAppleView* strongSelf=weakSelf;if(!strongSelf)return;[strongSelf dispatchDropSession:session type:JALIUM_EVENT_DROP mime:type.UTF8String data:(const uint8_t*)data.bytes size:(uint32_t)data.length];strongSelf.jaliumOwner->dragSession=0;});}];}
#endif
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
void jalium_apple_register_scene_root(const char* sceneId,intptr_t nativeView)
{
#if TARGET_OS_OSX
    (void)sceneId;(void)nativeView;
#else
    if(!sceneId||!*sceneId||!nativeView)return;UIView* view=(__bridge UIView*)(void*)nativeView;
    std::scoped_lock lock(g_windowsMutex);auto it=std::find_if(g_sceneRoots.begin(),g_sceneRoots.end(),
        [&](const SceneRoot& root){return root.id==sceneId;});
    if(it==g_sceneRoots.end())g_sceneRoots.push_back({sceneId,view,0});else it->view=view;
    g_rootView=view;
#endif
}
void jalium_apple_unregister_scene_root(const char* sceneId)
{
#if TARGET_OS_OSX
    (void)sceneId;
#else
    if(!sceneId)return;std::vector<JaliumPlatformWindow*> affected;
    {std::scoped_lock lock(g_windowsMutex);auto it=std::find_if(g_sceneRoots.begin(),g_sceneRoots.end(),
        [&](const SceneRoot& root){return root.id==sceneId;});if(it==g_sceneRoots.end())return;
        UIView* view=it->view;for(auto* window:g_windows)if(window->sceneRoot==view)affected.push_back(window);
        g_sceneRoots.erase(it);}
    for(auto* window:affected)DispatchSimple(window,JALIUM_EVENT_CLOSE_REQUESTED);
#endif
}
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
    UIView* root=AcquireRootView();if(!root)return nullptr;
    result->sceneRoot=root;
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
    ReleaseRootView(window->sceneRoot);
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

JaliumResult jalium_timer_create(JaliumTimer** out){if(!out)return JALIUM_ERROR_INVALID_ARGUMENT;auto* t=new JaliumTimer();t->fired=dispatch_semaphore_create(0);t->source=dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER,0,0,dispatch_get_global_queue(QOS_CLASS_USER_INTERACTIVE,0));if(!t->source){delete t;return JALIUM_ERROR_RESOURCE_CREATION_FAILED;}dispatch_source_set_event_handler(t->source,^{FireTimer(t);});dispatch_source_set_cancel_handler(t->source,^{delete t;});dispatch_resume(t->source);*out=t;return JALIUM_OK;}
static void StopDisplayLink(JaliumTimer* t){if(!t||(!t->displayLink&&!t->displayLinkTarget))return;auto stop=^{[t->displayLink invalidate];t->displayLink=nil;((JaliumDisplayLinkTarget*)t->displayLinkTarget).timer=nullptr;t->displayLinkTarget=nil;};if(pthread_main_np())stop();else dispatch_sync(dispatch_get_main_queue(),stop);}
void jalium_timer_destroy(JaliumTimer* t){if(!t)return;t->alive.store(false);StopDisplayLink(t);dispatch_source_cancel(t->source);}
static void ArmTimer(JaliumTimer* t,int64_t us,bool repeat){if(!t||us<0)return;
    // CompositionTarget's repeating 60/120 Hz timer is synchronized to the
    // display. Longer/general-purpose timers keep dispatch_source semantics.
    if(repeat&&us>0&&us<=25000){dispatch_source_set_timer(t->source,DISPATCH_TIME_FOREVER,DISPATCH_TIME_FOREVER,0);
        auto start=^{StopDisplayLink(t);auto* target=[JaliumDisplayLinkTarget new];target.timer=t;
            CADisplayLink* link=[CADisplayLink displayLinkWithTarget:target selector:@selector(displayLinkTick:)];
            float hz=std::clamp(1000000.0f/static_cast<float>(us),30.0f,240.0f);
            link.preferredFrameRateRange=CAFrameRateRangeMake(30.0f,hz,hz);
            [link addToRunLoop:NSRunLoop.mainRunLoop forMode:NSRunLoopCommonModes];
            t->displayLinkTarget=target;t->displayLink=link;};
        if(pthread_main_np())start();else dispatch_sync(dispatch_get_main_queue(),start);return;}
    StopDisplayLink(t);uint64_t ns=(uint64_t)us*1000;
    dispatch_source_set_timer(t->source,dispatch_time(DISPATCH_TIME_NOW,ns),repeat?ns:DISPATCH_TIME_FOREVER,std::min<uint64_t>(ns/20,1000000));}
void jalium_timer_arm(JaliumTimer* t,int64_t us){ArmTimer(t,us,false);}
void jalium_timer_arm_repeating(JaliumTimer* t,int64_t us){ArmTimer(t,us,true);}
void jalium_timer_disarm(JaliumTimer* t){if(t){StopDisplayLink(t);dispatch_source_set_timer(t->source,DISPATCH_TIME_FOREVER,DISPATCH_TIME_FOREVER,0);t->signalPending.store(false);}}
void jalium_timer_set_callback(JaliumTimer* t,JaliumTimerCallback cb,void* data){if(!t)return;std::scoped_lock lock(t->mutex);t->callback=cb;t->userData=data;}
int32_t jalium_timer_wait(JaliumTimer* t,uint32_t ms){if(!t)return 0;dispatch_time_t timeout=ms?dispatch_time(DISPATCH_TIME_NOW,(int64_t)ms*NSEC_PER_MSEC):DISPATCH_TIME_FOREVER;int success=dispatch_semaphore_wait(t->fired,timeout)==0?1:0;if(success)t->signalPending.store(false,std::memory_order_release);return success;}

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
int16_t jalium_input_get_key_state(int32_t keyCode){
#if TARGET_OS_OSX
    NSEventModifierFlags flags=NSEvent.modifierFlags;
    bool down=(keyCode==0x10&&(flags&NSEventModifierFlagShift))||
        (keyCode==0x11&&(flags&NSEventModifierFlagControl))||
        (keyCode==0x12&&(flags&NSEventModifierFlagOption))||
        ((keyCode==0x5b||keyCode==0x5c)&&(flags&NSEventModifierFlagCommand));
    if(keyCode==0x14)return (flags&NSEventModifierFlagCapsLock)?1:0;
    return down?static_cast<int16_t>(0x8000):0;
#else
    (void)keyCode;return 0;
#endif
}
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
JaliumResult jalium_drag_begin_with_image(JaliumPlatformWindow* window,
    const JaliumDragDataItem* items,uint32_t count,uint32_t allowed,
    JaliumDragFeedbackCallback feedback,JaliumDragQueryContinueCallback query,
    void* user,const JaliumDragImage* dragImage,uint32_t* performed)
{
    if(performed)*performed=JALIUM_DRAG_EFFECT_NONE;
    if(!window||!items||count==0||allowed==JALIUM_DRAG_EFFECT_NONE)
        return JALIUM_ERROR_INVALID_ARGUMENT;
#if TARGET_OS_OSX
    if(!pthread_main_np()||!NSApp.currentEvent)return JALIUM_ERROR_INVALID_STATE;
    NSPasteboardItem* pasteboard=[NSPasteboardItem new];
    for(uint32_t index=0;index<count;++index){if(!items[index].mimeType)continue;
        NSString* type=PasteboardTypeFromMime(items[index].mimeType);
        NSData* data=[NSData dataWithBytes:items[index].data length:items[index].dataSize];
        if(type&&data){if([type isEqualToString:NSPasteboardTypeString]||
            [type isEqualToString:NSPasteboardTypeURL]){NSString* value=[[NSString alloc]
                initWithData:data encoding:NSUTF8StringEncoding];if(value)[pasteboard setString:value forType:type];}
            else [pasteboard setData:data forType:type];}}
    NSDraggingItem* draggingItem=[[NSDraggingItem alloc]initWithPasteboardWriter:pasteboard];
    NSImage* image=nil;NSSize imageSize=NSMakeSize(32,32);NSPoint hotspot=NSMakePoint(0,0);
    if(dragImage&&dragImage->bgraPixels&&dragImage->width&&dragImage->height&&
       dragImage->stride>=dragImage->width*4){
        NSBitmapImageRep* representation=[[NSBitmapImageRep alloc]
            initWithBitmapDataPlanes:nil pixelsWide:dragImage->width pixelsHigh:dragImage->height
            bitsPerSample:8 samplesPerPixel:4 hasAlpha:YES isPlanar:NO
            colorSpaceName:NSDeviceRGBColorSpace
            bitmapFormat:NSBitmapFormatAlphaFirst|NSBitmapFormatThirtyTwoBitLittleEndian
            bytesPerRow:dragImage->width*4 bitsPerPixel:32];
        for(uint32_t y=0;y<dragImage->height;++y)std::memcpy(
            representation.bitmapData+static_cast<size_t>(y)*representation.bytesPerRow,
            dragImage->bgraPixels+static_cast<size_t>(y)*dragImage->stride,
            static_cast<size_t>(dragImage->width)*4);
        image=[[NSImage alloc]initWithSize:NSMakeSize(dragImage->width,dragImage->height)];
        [image addRepresentation:representation];imageSize=image.size;
        hotspot=NSMakePoint(dragImage->hotspotX,dragImage->hotspotY);
    }else image=[NSImage imageWithSystemSymbolName:@"doc" accessibilityDescription:nil];
    NSPoint point=[window->view convertPoint:NSApp.currentEvent.locationInWindow fromView:nil];
    [draggingItem setDraggingFrame:NSMakeRect(point.x-hotspot.x,point.y-hotspot.y,
        imageSize.width,imageSize.height) contents:image];
    window->dragEffect=allowed;window->dragFeedback=feedback;window->dragQuery=query;
    window->dragUserData=user;window->dragRunning.store(true);
    NSDraggingSession* session=[window->view beginDraggingSessionWithItems:@[draggingItem]
        event:NSApp.currentEvent source:window->view];
    session.draggingFormation=NSDraggingFormationNone;
    while(window->dragRunning.load())
        [NSRunLoop.currentRunLoop runMode:NSDefaultRunLoopMode
            beforeDate:[NSDate dateWithTimeIntervalSinceNow:0.01]];
    if(performed)*performed=window->dragEffect;
    window->dragFeedback=nullptr;window->dragQuery=nullptr;window->dragUserData=nullptr;
    return JALIUM_OK;
#else
    (void)feedback;(void)query;(void)user;(void)dragImage;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

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
