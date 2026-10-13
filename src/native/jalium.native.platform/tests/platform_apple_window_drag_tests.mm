#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#import <objc/runtime.h>
#include <cstdio>
#include <cstring>
#include <functional>
#include <memory>
#include <spawn.h>
#include <stdexcept>
#include <string>
#include <sys/resource.h>
#include <sys/wait.h>
#include <thread>
#include <vector>

extern char** environ;
static void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }

// These fixtures replay only this process's owned delegate contracts. The
// pasteboard and session are local objects: no cross-application drag, physical
// mouse gesture or native drag tracking is claimed by this suite.
@interface JaliumDragTestPasteboard : NSObject
@property(nonatomic, copy) void (^onRead)();
@end
@implementation JaliumDragTestPasteboard
- (NSArray*)types { return @[NSPasteboardTypeString]; }
- (NSString*)stringForType:(NSPasteboardType)type { if (_onRead) { auto action = _onRead; _onRead = nil; action(); } (void)type; return @"拖放保留中文🙂"; }
- (NSData*)dataForType:(NSPasteboardType)type { return [[self stringForType:type] dataUsingEncoding:NSUTF8StringEncoding]; }
@end

// Report an oversized representation without allocating hundreds of MiB. Any
// attempt to read its bytes fails inside this isolated process, so a baseline
// regression is observable without exposing an invalid pointer to memcpy.
@interface JaliumDragOversizedData : NSData
@property(nonatomic) NSUInteger reportedLength;
@property(nonatomic) int byteReads;
@end
@implementation JaliumDragOversizedData
- (NSUInteger)length { return _reportedLength; }
- (const void*)bytes {
    ++_byteReads;
    @throw [NSException exceptionWithName:@"JaliumOwnedOversizedRead" reason:@"oversized representation bytes were read" userInfo:nil];
}
@end

@interface JaliumDragReentrantData : NSData
@property(nonatomic, strong) NSData* payload;
@property(nonatomic, copy) void (^onBytes)();
@end
@implementation JaliumDragReentrantData
- (NSUInteger)length { return _payload.length; }
- (const void*)bytes {
    if (_onBytes) { auto action = _onBytes; _onBytes = nil; action(); }
    return _payload.bytes;
}
@end

@interface JaliumDragRepresentationPasteboard : JaliumDragTestPasteboard
@property(nonatomic, copy) NSArray<NSPasteboardType>* offered;
@property(nonatomic, strong) NSData* firstData;
@property(nonatomic) int dataReads;
@end
@implementation JaliumDragRepresentationPasteboard
- (NSArray*)types { return _offered; }
- (NSData*)dataForType:(NSPasteboardType)type {
    ++_dataReads;
    NSData* value = [type isEqualToString:_offered.firstObject] ? _firstData : nil;
    if (self.onRead) { auto action = self.onRead; self.onRead = nil; action(); }
    return value;
}
- (NSString*)stringForType:(NSPasteboardType)type {
    ++_dataReads;
    return [type isEqualToString:NSPasteboardTypeString] ? @"保留其他格式🙂" : nil;
}
@end

static id g_ownedClipboard;
static NSPasteboard* OwnedGeneralPasteboard(id object, SEL selector) {
    (void)object; (void)selector; return (NSPasteboard*)g_ownedClipboard;
}
struct ClipboardReadProbe {
    Method method;
    IMP original;
    explicit ClipboardReadProbe(id board) {
        method = class_getClassMethod(NSPasteboard.class, @selector(generalPasteboard));
        g_ownedClipboard = board;
        original = method_setImplementation(method, reinterpret_cast<IMP>(OwnedGeneralPasteboard));
    }
    ~ClipboardReadProbe() { method_setImplementation(method, original); g_ownedClipboard = nil; }
};

@interface JaliumDragTestInfo : NSObject
@property(nonatomic) NSInteger draggingSequenceNumber;
@property(nonatomic, strong) id board;
@property(nonatomic, strong) id draggingSource;
@end
@implementation JaliumDragTestInfo
- (NSPoint)draggingLocation { return NSMakePoint(32, 40); }
- (NSDragOperation)draggingSourceOperationMask { return NSDragOperationCopy | NSDragOperationMove; }
- (NSPasteboard*)draggingPasteboard { return (NSPasteboard*)self.board; }
@end

@interface JaliumDragTestSession : NSObject
@property(nonatomic) NSDraggingFormation draggingFormation;
@end
@implementation JaliumDragTestSession
@end

struct Fixture {
    JaliumPlatformWindow* window = nullptr;
    NSView* view = nil;
    NSWindow* native = nil;
    std::vector<JaliumPlatformEvent> events;
    std::function<void(const JaliumPlatformEvent&)> action;
    explicit Fixture(uint32_t style) {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Window drag lifetime validation");
        params.x = 100; params.y = 100; params.width = 420; params.height = 300; params.style = style;
        window = jalium_window_create(&params); Require(window != nullptr, "window creation failed");
        view = (__bridge NSView*)(void*)jalium_window_get_native_handle(window); native = view.window;
        jalium_window_set_event_callback(window, [](const JaliumPlatformEvent* event, void* data) {
            auto& f = *static_cast<Fixture*>(data); f.events.push_back(*event);
            if (f.action) f.action(*event);
        }, this);
        jalium_apple_window_show(window, 0); events.clear();
    }
    void Destroy() { if (window) { auto* original = window; window = nullptr; jalium_window_destroy(original); } }
    ~Fixture() { Destroy(); }
    int Count(JaliumEventType type) const { int n = 0; for (const auto& e : events) n += e.type == type; return n; }
    uint64_t Session(JaliumEventType type) const {
        for (auto it = events.rbegin(); it != events.rend(); ++it) if (it->type == type) return it->drag.sessionId;
        return 0;
    }
    id<NSDraggingSource> Source() const { return (id<NSDraggingSource>)view; }
    id<NSDraggingDestination> Target() const { return (id<NSDraggingDestination>)view; }
};

struct DragProbe;
static DragProbe* g_probe;
static IMP g_originalBegin, g_originalCurrentEvent, g_originalAddMonitor, g_originalRemoveMonitor;
static NSDraggingSession* BeginControlledDrag(id view, SEL selector, NSArray* items, NSEvent* event, id source);
static NSEvent* CurrentControlledEvent(id object, SEL selector);
static id AddControlledMonitor(id type, SEL selector, NSEventMask mask, id handler);
static void RemoveControlledMonitor(id type, SEL selector, id monitor);
struct DragProbe {
    Fixture& fixture;
    JaliumDragTestSession* session = [JaliumDragTestSession new];
    NSEvent* event;
    Method beginMethod, eventMethod, addMonitorMethod, removeMonitorMethod;
    NSMutableSet* activeMonitors = [NSMutableSet set];
    NSEvent* (^monitorHandler)(NSEvent*) = nil;
    int begins = 0, feedback = 0, queries = 0;
    uint32_t lastKeys = 0;
    bool lastEscape = false;
    JaliumDragContinueAction requestedAction = JALIUM_DRAG_CONTINUE;
    std::vector<JaliumDragDataItem> payloadItems;
    std::function<void(NSArray*)> inspectItems;
    bool refuse = false;
    bool noQuery = false, refuseMonitor = false;
    bool scheduleTick = true;
    std::function<void()> begin, tick, query, feedbackAction;
    explicit DragProbe(Fixture& f) : fixture(f) {
        event = [NSEvent mouseEventWithType:NSEventTypeLeftMouseDown location:NSMakePoint(32, 40)
            modifierFlags:0 timestamp:0 windowNumber:f.native.windowNumber context:nil eventNumber:1 clickCount:1 pressure:1];
        auto install = [](Class cls, SEL selector, IMP replacement, IMP& original) {
            Method inherited = class_getInstanceMethod(cls, selector); original = method_getImplementation(inherited);
            class_addMethod(cls, selector, original, method_getTypeEncoding(inherited));
            Method method = class_getInstanceMethod(cls, selector); method_setImplementation(method, replacement); return method;
        };
        g_probe = this;
        beginMethod = install(object_getClass(f.view), @selector(beginDraggingSessionWithItems:event:source:),
            reinterpret_cast<IMP>(BeginControlledDrag), g_originalBegin);
        eventMethod = install(object_getClass(NSApp), @selector(currentEvent),
            reinterpret_cast<IMP>(CurrentControlledEvent), g_originalCurrentEvent);
        addMonitorMethod = install(object_getClass(NSEvent.class), @selector(addLocalMonitorForEventsMatchingMask:handler:),
            reinterpret_cast<IMP>(AddControlledMonitor), g_originalAddMonitor);
        removeMonitorMethod = install(object_getClass(NSEvent.class), @selector(removeMonitor:),
            reinterpret_cast<IMP>(RemoveControlledMonitor), g_originalRemoveMonitor);
    }
    ~DragProbe() {
        method_setImplementation(beginMethod, g_originalBegin);
        method_setImplementation(eventMethod, g_originalCurrentEvent);
        method_setImplementation(addMonitorMethod, g_originalAddMonitor);
        method_setImplementation(removeMonitorMethod, g_originalRemoveMonitor); g_probe = nullptr;
    }
    NSDraggingSession* Session() const { return (NSDraggingSession*)session; }
    void End(NSDragOperation operation = NSDragOperationCopy) {
        [fixture.Source() draggingSession:Session() endedAtPoint:NSZeroPoint operation:operation];
    }
    JaliumResult Run(uint32_t* performed) {
        static constexpr uint8_t payload[] = {'d', 'r', 'a', 'g'};
        JaliumDragDataItem item{"text/plain;charset=utf-8", payload, sizeof(payload)};
        if (payloadItems.empty()) payloadItems.push_back(item);
        if (noQuery) return jalium_drag_begin(fixture.window, payloadItems.data(), static_cast<uint32_t>(payloadItems.size()),
            JALIUM_DRAG_EFFECT_COPY | JALIUM_DRAG_EFFECT_MOVE, performed);
        return jalium_drag_begin_ex(fixture.window, payloadItems.data(), static_cast<uint32_t>(payloadItems.size()), JALIUM_DRAG_EFFECT_COPY | JALIUM_DRAG_EFFECT_MOVE,
            [](uint32_t effect, void* data) {
                (void)effect; auto& p = *static_cast<DragProbe*>(data); ++p.feedback;
                if (p.feedbackAction) p.feedbackAction();
            },
            [](uint32_t keys, int32_t escape, void* data) {
                auto& p = *static_cast<DragProbe*>(data); ++p.queries; p.lastKeys = keys; p.lastEscape = escape != 0;
                if (p.query) p.query(); return p.requestedAction;
            }, this, performed);
    }
};
static NSDraggingSession* BeginControlledDrag(id view, SEL selector, NSArray* items, NSEvent* event, id source) {
    if (!g_probe || view != g_probe->fixture.view)
        return reinterpret_cast<NSDraggingSession*(*)(id,SEL,NSArray*,NSEvent*,id)>(g_originalBegin)(view,selector,items,event,source);
    auto* p = g_probe; ++p->begins;
    Require(source == p->fixture.view && event == p->event, "begin lost the owned payload or source");
    if (p->inspectItems) p->inspectItems(items);
    else Require(items.count == 1, "unexpected dragging item count");
    NSDraggingItem* item = items.firstObject;
    Require([[(NSPasteboardItem*)item.item stringForType:NSPasteboardTypeString] isEqualToString:@"drag"],
        "the owned text representation was not copied before entering AppKit");
    if (p->begin) p->begin();
    if (p->refuse) return nil;
    if (p->scheduleTick && p->fixture.window && p->fixture.native.visible && p->fixture.native.canBecomeKeyWindow) {
        CFRunLoopPerformBlock(CFRunLoopGetMain(), kCFRunLoopDefaultMode, ^{
            if (p->tick) p->tick(); else p->End();
        });
        CFRunLoopWakeUp(CFRunLoopGetMain());
    }
    return p->Session();
}
static NSEvent* CurrentControlledEvent(id object, SEL selector) {
    if (g_probe && object == NSApp) return g_probe->event;
    return reinterpret_cast<NSEvent*(*)(id,SEL)>(g_originalCurrentEvent)(object,selector);
}
static id AddControlledMonitor(id type, SEL selector, NSEventMask mask, id handler) {
    bool owned = g_probe && mask == (NSEventMaskKeyDown | NSEventMaskFlagsChanged | NSEventMaskLeftMouseUp);
    if (owned && g_probe->refuseMonitor) return nil;
    id monitor = reinterpret_cast<id(*)(id,SEL,NSEventMask,id)>(g_originalAddMonitor)(type,selector,mask,handler);
    if (owned && monitor) {
        [g_probe->activeMonitors addObject:monitor];
        g_probe->monitorHandler = handler;
    }
    return monitor;
}
static void RemoveControlledMonitor(id type, SEL selector, id monitor) {
    if (g_probe) [g_probe->activeMonitors removeObject:monitor];
    reinterpret_cast<void(*)(id,SEL,id)>(g_originalRemoveMonitor)(type,selector,monitor);
}

static void SelectCopy(Fixture& f) {
    f.action = [&](const JaliumPlatformEvent& e) {
        if (e.type == JALIUM_EVENT_DRAG_ENTER || e.type == JALIUM_EVENT_DRAG_OVER || e.type == JALIUM_EVENT_DROP)
            jalium_drag_set_effect(f.window, e.drag.sessionId, JALIUM_DRAG_EFFECT_COPY);
    };
}

static void RunExtended(Fixture& f, int scenario, JaliumDragTestInfo* info) {
    struct ActionReset { Fixture& fixture; ~ActionReset() { fixture.action = nullptr; } } actionReset{f};
    auto sender = (id<NSDraggingInfo>)info;
    auto stop = [&](bool disable) {
        if (disable) jalium_window_set_enabled(f.window, 0); else jalium_window_hide(f.window);
    };
    auto restore = [&](bool disable) {
        if (disable) jalium_window_set_enabled(f.window, 1); else jalium_apple_window_show(f.window, 0);
    };
    if (scenario == 16) {
        uint64_t sessionId = 0;
        f.action = [&](const JaliumPlatformEvent& e) {
            if (e.type != JALIUM_EVENT_DRAG_ENTER && e.type != JALIUM_EVENT_DRAG_OVER && e.type != JALIUM_EVENT_DROP) return;
            if (!sessionId) sessionId = e.drag.sessionId;
            Require(sessionId && e.drag.sessionId == sessionId && e.drag.allowedEffects == (JALIUM_DRAG_EFFECT_COPY | JALIUM_DRAG_EFFECT_MOVE), "target lost session or source operation mask");
            Require(e.drag.mimeTypes && std::strstr(e.drag.mimeTypes, "text/plain;charset=utf-8"), "target lost its text type");
            if (e.type == JALIUM_EVENT_DROP) {
                Require(e.drag.dataMimeType && std::strcmp(e.drag.dataMimeType, "text/plain;charset=utf-8") == 0, "drop changed its MIME type");
                Require(std::string(reinterpret_cast<const char*>(e.drag.data), e.drag.dataSize) == "拖放保留中文🙂", "drop corrupted its UTF-8 payload");
            }
            jalium_drag_set_effect(f.window, e.drag.sessionId, JALIUM_DRAG_EFFECT_COPY);
        };
        Require([f.Target() draggingEntered:sender] == NSDragOperationCopy && [f.Target() draggingUpdated:sender] == NSDragOperationCopy, "normal target rejected text");
        Require([f.Target() performDragOperation:sender], "normal target rejected drop");
        size_t count = f.events.size();
        Require([f.Target() draggingUpdated:sender] == NSDragOperationNone && ![f.Target() performDragOperation:sender], "completed target accepted a late callback");
        [f.Target() draggingExited:sender];
        Require(f.events.size() == count && f.Count(JALIUM_EVENT_DROP) == 1, "completed target published twice");
    } else if (scenario == 17) {
        SelectCopy(f); [f.Target() draggingEntered:sender];
        uint64_t retiredId = f.Session(JALIUM_EVENT_DRAG_ENTER);
        auto newer = [JaliumDragTestInfo new]; newer.draggingSequenceNumber = 56; newer.board = info.board;
        Require([f.Target() draggingEntered:(id<NSDraggingInfo>)newer] == NSDragOperationCopy, "new target did not enter");
        f.action = nullptr; size_t count = f.events.size();
        jalium_drag_set_effect(f.window, retiredId, JALIUM_DRAG_EFFECT_MOVE);
        Require([f.Target() draggingUpdated:sender] == NSDragOperationNone && ![f.Target() performDragOperation:sender], "stale target was accepted");
        [f.Target() draggingExited:sender];
        Require(f.events.size() == count, "stale target published an event");
        Require([f.Target() draggingUpdated:(id<NSDraggingInfo>)newer] == NSDragOperationCopy, "stale setter or exit changed the newer effect");
    } else if (scenario == 18) {
        info.draggingSequenceNumber = 0; jalium_drag_set_effect(f.window, 0, JALIUM_DRAG_EFFECT_COPY);
        Require([f.Target() draggingUpdated:sender] == NSDragOperationNone && ![f.Target() performDragOperation:sender], "inactive zero sequence became a target");
        [f.Target() draggingExited:sender]; Require(f.events.empty(), "inactive zero sequence published events");
        SelectCopy(f); Require([f.Target() draggingEntered:sender] == NSDragOperationCopy && [f.Target() performDragOperation:sender], "a real zero-sequence enter was rejected");
        size_t count = f.events.size();
        Require([f.Target() draggingUpdated:sender] == NSDragOperationNone && ![f.Target() performDragOperation:sender], "terminal zero sequence became active again");
        [f.Target() draggingExited:sender]; Require(f.events.size() == count, "terminal zero sequence published events");
    } else if (scenario == 19) {
        for (bool disable : {false, true}) {
            SelectCopy(f); stop(disable); f.events.clear();
            Require([f.Target() draggingEntered:sender] == NSDragOperationNone && f.events.empty(), "unavailable target entered");
            restore(disable); Require([f.Target() draggingEntered:sender] == NSDragOperationCopy, "restored target failed fresh enter");
            stop(disable); restore(disable); f.events.clear();
            Require([f.Target() draggingUpdated:sender] == NSDragOperationNone && ![f.Target() performDragOperation:sender], "restoration revived the interrupted target");
            [f.Target() draggingExited:sender]; Require(f.events.empty(), "interrupted target published after restoration");
        }
    } else if (scenario == 20) {
        for (bool disable : {false, true}) {
            for (auto type : {JALIUM_EVENT_DRAG_ENTER, JALIUM_EVENT_DRAG_OVER, JALIUM_EVENT_DRAG_LEAVE, JALIUM_EVENT_DROP}) {
                SelectCopy(f); f.events.clear();
                if (type != JALIUM_EVENT_DRAG_ENTER) [f.Target() draggingEntered:sender];
                f.action = [&](const JaliumPlatformEvent& e) { if (e.type == type) stop(disable); };
                if (type == JALIUM_EVENT_DRAG_ENTER) Require([f.Target() draggingEntered:sender] == NSDragOperationNone, "enter returned an interrupted effect");
                else if (type == JALIUM_EVENT_DRAG_OVER) Require([f.Target() draggingUpdated:sender] == NSDragOperationNone, "over returned an interrupted effect");
                else if (type == JALIUM_EVENT_DRAG_LEAVE) [f.Target() draggingExited:sender];
                else Require(![f.Target() performDragOperation:sender], "drop accepted an interrupted window");
                Require(f.Count(type) == 1, "interruption lost or duplicated its original event");
                restore(disable); f.action = nullptr; f.events.clear();
                Require([f.Target() draggingUpdated:sender] == NSDragOperationNone, "old callback survived interruption");
            }
        }
    } else if (scenario == 21) {
        DragProbe p(f); p.feedbackAction = [&] { f.Destroy(); };
        p.tick = [&] { [f.Source() draggingSession:p.Session() movedToPoint:NSZeroPoint]; };
        uint32_t performed = 0xffffffff;
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_NONE && p.queries == 1 && p.feedback == 1 && !f.Count(JALIUM_EVENT_DRAG_FINISHED), "feedback close retained source callbacks or an effect");
    } else if (scenario == 22) {
        DragProbe p(f); SelectCopy(f);
        p.tick = [&] {
            Require([f.Target() draggingEntered:sender] == NSDragOperationCopy, "self target rejected copy");
            for (auto context : {NSDraggingContextWithinApplication, NSDraggingContextOutsideApplication})
                Require([f.Source() draggingSession:p.Session() sourceOperationMaskForDraggingContext:context] == (NSDragOperationCopy | NSDragOperationMove), "target selection narrowed the source mask");
            p.End();
        };
        uint32_t performed = 0; Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY, "self source lost its result");
    } else if (scenario == 23) {
        DragProbe p(f); auto foreign = (NSDraggingSession*)[JaliumDragTestSession new];
        p.tick = [&] {
            Require([f.Source() draggingSession:foreign sourceOperationMaskForDraggingContext:NSDraggingContextOutsideApplication] == NSDragOperationNone, "foreign session acquired allowed operations");
            [f.Source() draggingSession:foreign movedToPoint:NSZeroPoint];
            [f.Source() draggingSession:foreign endedAtPoint:NSZeroPoint operation:NSDragOperationMove];
            Require(!p.queries && !p.feedback && !f.Count(JALIUM_EVENT_DRAG_FINISHED), "foreign session called the active source"); p.End();
        };
        uint32_t performed = 0; Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY && f.Count(JALIUM_EVENT_DRAG_FINISHED) == 1, "foreign completion replaced the result");
    } else if (scenario == 24) {
        DragProbe p(f); p.tick = [&] { p.End(); p.End(NSDragOperationMove); };
        uint32_t performed = 0; Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY && f.Count(JALIUM_EVENT_DRAG_FINISHED) == 1, "duplicate completion changed its result");
    } else if (scenario == 25) {
        DragProbe p(f); p.tick = [&] {
            uint32_t nested = 0xffffffff;
            Require(p.Run(&nested) == JALIUM_ERROR_INVALID_STATE && nested == JALIUM_DRAG_EFFECT_NONE && p.begins == 1, "nested source started another session"); p.End();
        };
        uint32_t performed = 0; Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY, "nested refusal broke the outer source");
    } else if (scenario == 26 || scenario == 27) {
        for (bool disable : {false, true}) {
            f.events.clear(); DragProbe p(f);
            if (scenario == 27) p.begin = [&] { stop(disable); };
            else p.tick = [&] {
                stop(disable); [f.Source() draggingSession:p.Session() movedToPoint:NSZeroPoint]; p.End();
            };
            uint32_t performed = 0xffffffff;
            Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_NONE && p.begins == 1 && !p.queries && !p.feedback && !f.Count(JALIUM_EVENT_DRAG_FINISHED), "interrupted source retained its result or callbacks");
            restore(disable);
        }
    } else if (scenario == 28) {
        DragProbe p(f); auto retired = p.Session(); uint32_t performed = 0;
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY, "first source did not finish");
        p.session = [JaliumDragTestSession new];
        p.begin = [&] {
            Require([f.Source() draggingSession:retired sourceOperationMaskForDraggingContext:NSDraggingContextOutsideApplication] == NSDragOperationNone, "retired source acquired a session before native begin returned");
            [f.Source() draggingSession:retired movedToPoint:NSZeroPoint];
            [f.Source() draggingSession:retired endedAtPoint:NSZeroPoint operation:NSDragOperationMove];
            Require(!p.queries && !p.feedback && f.Count(JALIUM_EVENT_DRAG_FINISHED) == 1, "retired early callback finished the new source");
        };
        p.tick = [&] {
            Require([f.Source() draggingSession:retired sourceOperationMaskForDraggingContext:NSDraggingContextOutsideApplication] == NSDragOperationNone, "retired session acquired the next source");
            [f.Source() draggingSession:retired movedToPoint:NSZeroPoint];
            [f.Source() draggingSession:retired endedAtPoint:NSZeroPoint operation:NSDragOperationMove];
            Require(!p.queries && !p.feedback && f.Count(JALIUM_EVENT_DRAG_FINISHED) == 1, "retired source affected the next operation"); p.End();
        };
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY && p.begins == 2 && f.Count(JALIUM_EVENT_DRAG_FINISHED) == 2, "source could not start a new operation");
    } else if (scenario >= 29 && scenario <= 32) {
        DragProbe p(f); uint32_t performed = 0xffffffff; JaliumResult result;
        if (scenario == 29) p.event = nil;
        else if (scenario == 30) p.event = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint modifierFlags:0 timestamp:0 windowNumber:f.native.windowNumber context:nil characters:@"x" charactersIgnoringModifiers:@"x" isARepeat:NO keyCode:7];
        else if (scenario == 31) p.event = [NSEvent mouseEventWithType:NSEventTypeLeftMouseDown location:NSZeroPoint modifierFlags:0 timestamp:0 windowNumber:f.native.windowNumber+1 context:nil eventNumber:1 clickCount:1 pressure:1];
        if (scenario == 32) { std::thread worker([&] { result = p.Run(&performed); }); worker.join(); }
        else result = p.Run(&performed);
        Require(result == JALIUM_ERROR_INVALID_STATE && performed == JALIUM_DRAG_EFFECT_NONE && !p.begins, "invalid event or thread reached AppKit drag start");
    } else if (scenario == 33 || scenario == 34) {
        bool reentered = false; auto terminal = scenario == 33 ? JALIUM_EVENT_DRAG_LEAVE : JALIUM_EVENT_DROP;
        f.action = [&](const JaliumPlatformEvent& e) {
            if (e.type == terminal && !reentered) {
                reentered = true;
                uint64_t retiredId = e.drag.sessionId;
                Require([f.Target() draggingEntered:sender] == NSDragOperationCopy, "terminal callback could not reenter the same native sequence");
                jalium_drag_set_effect(f.window, retiredId, JALIUM_DRAG_EFFECT_MOVE);
            } else if (e.type == JALIUM_EVENT_DRAG_ENTER || e.type == JALIUM_EVENT_DROP)
                jalium_drag_set_effect(f.window, e.drag.sessionId, JALIUM_DRAG_EFFECT_COPY);
        };
        Require([f.Target() draggingEntered:sender] == NSDragOperationCopy, "target did not enter before reentry");
        if (scenario == 33) [f.Target() draggingExited:sender];
        else Require(![f.Target() performDragOperation:sender], "old drop claimed the reentrant generation");
        Require(reentered && [f.Target() draggingUpdated:sender] == NSDragOperationCopy && [f.Target() performDragOperation:sender], "old terminal callback cleared the reentrant target");
    } else if (scenario == 35) {
        SelectCopy(f); Require([f.Target() draggingEntered:sender] == NSDragOperationCopy, "target did not enter before tracking end");
        auto foreign = [JaliumDragTestInfo new]; foreign.draggingSequenceNumber = 56; foreign.board = info.board;
        bool implementsEnd = [(id)f.Target() respondsToSelector:@selector(draggingEnded:)];
        if (implementsEnd) [f.Target() draggingEnded:(id<NSDraggingInfo>)foreign];
        Require(!f.Count(JALIUM_EVENT_DRAG_LEAVE), "foreign tracking end closed the target");
        if (implementsEnd) [f.Target() draggingEnded:sender];
        Require(f.Count(JALIUM_EVENT_DRAG_LEAVE) == 1 && [f.Target() draggingUpdated:sender] == NSDragOperationNone, "tracking end retained the active target");
        if (implementsEnd) [f.Target() draggingEnded:sender]; Require(f.Count(JALIUM_EVENT_DRAG_LEAVE) == 1, "tracking end published duplicate leave");
    } else if (scenario == 36) {
        Require(![f.Target() prepareForDragOperation:sender], "inactive target prepared a drop");
        SelectCopy(f); [f.Target() draggingEntered:sender];
        Require([f.Target() prepareForDragOperation:sender], "accepted active target refused drop preparation");
        auto foreign = [JaliumDragTestInfo new]; foreign.draggingSequenceNumber = 56; foreign.board = info.board;
        Require(![f.Target() prepareForDragOperation:(id<NSDraggingInfo>)foreign], "foreign target prepared a drop");
        jalium_drag_set_effect(f.window, f.Session(JALIUM_EVENT_DRAG_ENTER), JALIUM_DRAG_EFFECT_LINK);
        Require(![f.Target() prepareForDragOperation:sender], "unsupported selected effect prepared a drop");
        jalium_drag_set_effect(f.window, f.Session(JALIUM_EVENT_DRAG_ENTER), JALIUM_DRAG_EFFECT_COPY);
        stop(true); Require(![f.Target() prepareForDragOperation:sender], "disabled target prepared a drop");
        restore(true); Require(![f.Target() prepareForDragOperation:sender], "reenabling revived drop preparation");
    } else if (scenario == 37) {
        SelectCopy(f); [f.Target() draggingEntered:sender]; uint64_t original = f.Session(JALIUM_EVENT_DRAG_ENTER);
        [f.Target() draggingExited:nil];
        Require(f.Count(JALIUM_EVENT_DRAG_LEAVE) == 1 && f.Session(JALIUM_EVENT_DRAG_LEAVE) == original && [f.Target() draggingUpdated:sender] == NSDragOperationNone, "nullable exit did not retire the active target");
        [f.Target() draggingExited:nil]; Require(f.Count(JALIUM_EVENT_DRAG_LEAVE) == 1, "nullable exit published twice");
        bool reentered = false;
        f.action = [&](const JaliumPlatformEvent& e) {
            if (e.type == JALIUM_EVENT_DRAG_LEAVE && !reentered) {
                reentered = true; Require([f.Target() draggingEntered:sender] == NSDragOperationCopy, "nullable leave could not reenter");
            } else if (e.type == JALIUM_EVENT_DRAG_ENTER || e.type == JALIUM_EVENT_DROP)
                jalium_drag_set_effect(f.window, e.drag.sessionId, JALIUM_DRAG_EFFECT_COPY);
        };
        [f.Target() draggingEntered:sender]; [f.Target() draggingExited:nil];
        Require(reentered && [f.Target() draggingUpdated:sender] == NSDragOperationCopy && [f.Target() performDragOperation:sender], "nullable old exit cleared the reentrant target");
    } else if (scenario == 38) {
        DragProbe p(f); p.begin = [&] {
            Require([f.Source() draggingSession:p.Session() sourceOperationMaskForDraggingContext:NSDraggingContextOutsideApplication] == (NSDragOperationCopy | NSDragOperationMove), "valid early source lost its operation mask");
            [f.Source() draggingSession:p.Session() movedToPoint:NSZeroPoint];
        };
        uint32_t performed = 0;
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY && p.queries == 1 && p.feedback == 1 && f.Count(JALIUM_EVENT_DRAG_FINISHED) == 1, "valid early source did not finish normally");
    } else if (scenario == 39) {
        DragProbe p(f); p.scheduleTick = false; p.begin = [&] { p.End(); };
        uint32_t performed = 0;
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY && f.Count(JALIUM_EVENT_DRAG_FINISHED) == 1, "synchronous native completion lost its source result");
    } else throw std::runtime_error("unimplemented drag scenario");
}

static std::string ReadVisit(Fixture& f, uint64_t token, const char* mime, bool expectPresent = true) {
    uint8_t* data = nullptr; uint32_t size = 0;
    Require(jalium_apple_drag_get_data(f.window, token, mime, &data, &size) == JALIUM_OK, "live visit data lookup failed");
    Require((data != nullptr) == expectPresent, "missing and empty representations were conflated");
    std::string result(data ? reinterpret_cast<const char*>(data) : "", size);
    jalium_platform_free(data); return result;
}
static void RequireRetiredRead(Fixture& f, uint64_t token) {
    uint8_t* data = reinterpret_cast<uint8_t*>(1); uint32_t size = 99;
    Require(jalium_apple_drag_get_data(f.window, token, "text/plain", &data, &size) == JALIUM_ERROR_INVALID_STATE &&
        !data && !size, "retired visit retained readable data");
    Require(jalium_apple_drag_get_source(f.window, token) == 0, "retired visit retained its source");
}
static void RunDataCase(Fixture& f, int scenario, JaliumDragTestInfo* info) {
    auto sender = (id<NSDraggingInfo>)info;
    SelectCopy(f);
    if (scenario == 40 || scenario == 41) {
        NSPasteboard* board = [NSPasteboard pasteboardWithUniqueName];
        NSPasteboardItem* first = [NSPasteboardItem new];
        NSPasteboardItem* second = [NSPasteboardItem new];
        if (scenario == 40) {
            [first setString:@"中文🙂" forType:NSPasteboardTypeString];
            [first setData:[@"<b>中文🙂</b>" dataUsingEncoding:NSUTF8StringEncoding] forType:NSPasteboardTypeHTML];
            NSString* custom = [UTType typeWithMIMEType:@"application/x-jalium-empty" conformingToType:UTTypeData].identifier;
            [first setData:[NSData data] forType:custom];
            Require([board writeObjects:@[first]], "owned multi-format pasteboard write failed");
        } else {
            [first setString:@"file:///tmp/one%20file.txt" forType:NSPasteboardTypeFileURL];
            [second setString:@"file:///tmp/two.txt" forType:NSPasteboardTypeFileURL];
            Require([board writeObjects:@[first, second]], "owned file-list pasteboard write failed");
        }
        info.board = board;
        [f.Target() draggingEntered:sender]; uint64_t token = f.Session(JALIUM_EVENT_DRAG_ENTER);
        if (scenario == 40) {
            Require(ReadVisit(f, token, "text/plain") == "中文🙂", "UTF-8 drag representation changed");
            Require(ReadVisit(f, token, "text/html") == "<b>中文🙂</b>", "secondary HTML representation was lost");
            Require(ReadVisit(f, token, "application/x-jalium-empty").empty(), "empty custom MIME changed");
            (void)ReadVisit(f, token, "application/x-missing", false);
        } else Require(ReadVisit(f, token, "text/uri-list") == "file:///tmp/one%20file.txt\r\nfile:///tmp/two.txt\r\n",
            "multiple pasteboard file items were truncated");
        Require([f.Target() performDragOperation:sender], "multi-format Drop failed");
        RequireRetiredRead(f, token);
        [board releaseGlobally];
    } else if (scenario == 42) {
        RequireRetiredRead(f, 0);
        [f.Target() draggingEntered:sender]; uint64_t old = f.Session(JALIUM_EVENT_DRAG_ENTER);
        [f.Target() draggingExited:sender]; RequireRetiredRead(f, old);
        [f.Target() draggingEntered:sender]; uint64_t next = f.Session(JALIUM_EVENT_DRAG_ENTER);
        RequireRetiredRead(f, old); Require(!ReadVisit(f, next, "text/plain").empty(), "new visit lost its reader");
        bool rejected = false;
        std::thread worker([&] { uint8_t* bytes = nullptr; uint32_t size = 0;
            rejected = jalium_apple_drag_get_data(f.window, next, "text/plain", &bytes, &size) == JALIUM_ERROR_INVALID_STATE && !bytes && !size; });
        worker.join(); Require(rejected, "worker-thread AppKit data read was accepted");
    } else if (scenario == 43 || scenario == 44) {
        [f.Target() draggingEntered:sender]; uint64_t token = f.Session(JALIUM_EVENT_DRAG_ENTER);
        if (scenario == 43) jalium_window_hide(f.window); else jalium_window_set_enabled(f.window, 0);
        RequireRetiredRead(f, token);
    } else if (scenario == 45) {
        auto board = (JaliumDragTestPasteboard*)info.board;
        [f.Target() draggingEntered:sender]; uint64_t token = f.Session(JALIUM_EVENT_DRAG_ENTER);
        board.onRead = ^{ [f.Target() draggingEntered:sender]; };
        RequireRetiredRead(f, token);
        Require(f.Count(JALIUM_EVENT_DRAG_ENTER) == 2 && !ReadVisit(f, f.Session(JALIUM_EVENT_DRAG_ENTER), "text/plain").empty(),
            "promised-data reentry overwrote its successor");
    } else if (scenario == 46) {
        DragProbe p(f); uint32_t performed = 0; info.draggingSource = f.view;
        p.tick = [&] {
            [f.Target() draggingEntered:sender]; auto token = f.Session(JALIUM_EVENT_DRAG_ENTER);
            Require(jalium_apple_drag_get_source(f.window, token) == reinterpret_cast<intptr_t>((__bridge void*)f.view),
                "in-process drag lost its live source identity");
            p.End();
        };
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY, "owned source identity interrupted completion");
        Require(jalium_apple_drag_get_source(f.window, f.Session(JALIUM_EVENT_DRAG_ENTER)) == 0, "finished source remained discoverable");
    } else if (scenario == 47) {
        DragProbe p(f); uint32_t performed = 0;
        const std::string files = "file:///tmp/one%20file.txt\r\nfile:///tmp/two.txt\r\n";
        const std::string text = "drag", custom = "opaque";
        const char* registered = "application/x-jalium-clipboard-format-SmFsaXVtLldpbmRvd0RyYWcuU2FtcGxl";
        NSPasteboard* board = [NSPasteboard pasteboardWithUniqueName];
        std::string offered;
        f.action = [&](const JaliumPlatformEvent& e) {
            if (e.type == JALIUM_EVENT_DRAG_ENTER) offered = e.drag.mimeTypes;
            if (e.type == JALIUM_EVENT_DRAG_ENTER || e.type == JALIUM_EVENT_DROP) jalium_drag_set_effect(f.window, e.drag.sessionId, JALIUM_DRAG_EFFECT_COPY);
        };
        p.payloadItems = {{"text/plain", reinterpret_cast<const uint8_t*>(text.data()), static_cast<uint32_t>(text.size())},
            {"text/uri-list", reinterpret_cast<const uint8_t*>(files.data()), static_cast<uint32_t>(files.size())},
            {"application/x-jalium-custom", reinterpret_cast<const uint8_t*>(custom.data()), static_cast<uint32_t>(custom.size())},
            {registered, reinterpret_cast<const uint8_t*>(custom.data()), static_cast<uint32_t>(custom.size())}};
        p.inspectItems = [&](NSArray* items) {
            Require(items.count == 2, "file-list source did not create one item per URL");
            for (NSDraggingItem* item in items) Require([(NSPasteboardItem*)item.item stringForType:NSPasteboardTypeFileURL] != nil,
                "file source was not offered as a file URL");
            NSPasteboardItem* first = ((NSDraggingItem*)items.firstObject).item;
            NSString* type = [UTType typeWithMIMEType:@"application/x-jalium-custom" conformingToType:UTTypeData].identifier;
            Require([first dataForType:type].length == custom.size() && ![type containsString:@"/"], "custom source MIME was not published as a UTI");
            NSMutableArray* writers = [NSMutableArray array];
            for (NSDraggingItem* item in items) [writers addObject:item.item];
            Require([board writeObjects:writers], "source representations could not be read by a destination"); info.board = board;
        };
        p.tick = [&] {
            [f.Target() draggingEntered:sender];
            Require(offered.find(registered) != std::string::npos && ReadVisit(f, f.Session(JALIUM_EVENT_DRAG_ENTER), registered) == "opaque",
                "UTType lowercasing corrupted the registered format name");
            p.End();
        };
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY, "multi-file source failed");
        [board releaseGlobally];
    } else if (scenario == 48) {
        DragProbe p(f); uint32_t performed = 0;
        p.event = [NSEvent mouseEventWithType:NSEventTypeLeftMouseDragged location:NSMakePoint(32, 40)
            modifierFlags:NSEventModifierFlagControl | NSEventModifierFlagOption | NSEventModifierFlagShift | NSEventModifierFlagCommand
            timestamp:0 windowNumber:f.native.windowNumber context:nil eventNumber:2 clickCount:1 pressure:1];
        p.tick = [&] {
            [f.Source() draggingSession:p.Session() movedToPoint:NSZeroPoint];
            Require((p.lastKeys & 0x3f) == 0x2d, "source query lost mouse buttons or modifier states");
            [f.Target() draggingEntered:sender];
            Require((f.events.back().drag.keyStates & 0x7f) == 0x6d, "target event lost AppKit modifier states");
            p.End();
        };
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY, "modified source failed");
    } else if (scenario == 49) {
        DragProbe p(f); uint32_t performed = 0; p.requestedAction = JALIUM_DRAG_DROP;
        p.tick = [&] {
            [f.Source() draggingSession:p.Session() movedToPoint:NSZeroPoint];
            NSEvent* release = [NSApp nextEventMatchingMask:NSEventMaskLeftMouseUp untilDate:NSDate.date inMode:NSDefaultRunLoopMode dequeue:YES];
            Require(release && release.windowNumber == f.native.windowNumber, "requested Drop did not release the owned native drag");
            p.End();
        };
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY, "requested source Drop lost completion");
    } else if (scenario == 50) {
        NSPasteboard* board = [NSPasteboard pasteboardWithUniqueName];
        auto image = [[NSBitmapImageRep alloc] initWithBitmapDataPlanes:nil pixelsWide:1 pixelsHigh:1
            bitsPerSample:8 samplesPerPixel:4 hasAlpha:YES isPlanar:NO colorSpaceName:NSDeviceRGBColorSpace
            bitmapFormat:0 bytesPerRow:4 bitsPerPixel:32];
        const uint8_t rgba[] = {0x10, 0x20, 0x30, 0xff}; memcpy(image.bitmapData, rgba, 4);
        NSPasteboardItem* item = [NSPasteboardItem new];
        [item setData:image.TIFFRepresentation forType:NSPasteboardTypeTIFF];
        [item setData:[NSData dataWithBytes:rgba length:4] forType:UTTypeWAV.identifier];
        Require([board writeObjects:@[item]], "owned bitmap pasteboard failed"); info.board = board;
        std::string offered;
        f.action = [&](const JaliumPlatformEvent& e) {
            if (e.type == JALIUM_EVENT_DRAG_ENTER) offered = e.drag.mimeTypes;
            if (e.type == JALIUM_EVENT_DRAG_ENTER || e.type == JALIUM_EVENT_DROP) jalium_drag_set_effect(f.window, e.drag.sessionId, JALIUM_DRAG_EFFECT_COPY);
        };
        [f.Target() draggingEntered:sender]; auto token = f.Session(JALIUM_EVENT_DRAG_ENTER);
        Require(offered.find("image/png") != std::string::npos && offered.find("audio/wav") != std::string::npos,
            "TIFF and Wave representations lost their canonical formats");
        auto png = ReadVisit(f, token, "image/png");
        auto decoded = [NSBitmapImageRep imageRepWithData:[NSData dataWithBytes:png.data() length:png.size()]];
        Require(decoded.pixelsWide == 1 && decoded.pixelsHigh == 1 && ReadVisit(f, token, "audio/wav").size() == 4,
            "native bitmap conversion or Wave lookup failed");
        [f.Target() draggingExited:sender]; RequireRetiredRead(f, token); [board releaseGlobally];
    } else if (scenario == 51) {
        uint32_t pressed = 0, moved = 0, released = 0;
        f.action = [&](const JaliumPlatformEvent& e) {
            if (e.type == JALIUM_EVENT_MOUSE_DOWN) pressed = e.mouse.buttonStates;
            if (e.type == JALIUM_EVENT_MOUSE_MOVE) moved = e.mouse.buttonStates;
            if (e.type == JALIUM_EVENT_MOUSE_UP) released = e.mouse.buttonStates;
        };
        auto event = [&](NSEventType type) { return [NSEvent mouseEventWithType:type location:NSMakePoint(32,40)
            modifierFlags:0 timestamp:0 windowNumber:f.native.windowNumber context:nil eventNumber:1 clickCount:1 pressure:1]; };
        [f.view mouseDown:event(NSEventTypeLeftMouseDown)]; [f.view mouseDragged:event(NSEventTypeLeftMouseDragged)];
        [f.view mouseUp:event(NSEventTypeLeftMouseUp)];
        Require((pressed & 0x80000001) == 0x80000001 && (moved & 0x80000001) == 0x80000001 &&
            (released & 0x80000001) == 0x80000000, "owned mouse drag did not preserve press/release state");
    } else if (scenario == 52) {
        DragProbe p(f); uint32_t performed = 99; p.requestedAction = JALIUM_DRAG_CANCEL;
        p.tick = [&] {
            auto release = [NSEvent mouseEventWithType:NSEventTypeLeftMouseUp location:NSMakePoint(32,40)
                modifierFlags:0 timestamp:0 windowNumber:f.native.windowNumber context:nil eventNumber:2 clickCount:1 pressure:0];
            [NSApp sendEvent:release];
            Require(p.queries == 1 && !(p.lastKeys & 1) && f.Count(JALIUM_EVENT_MOUSE_UP) == 0,
                "Cancel at release forwarded MouseUp before its replacement Escape");
            NSEvent* escape = [NSApp nextEventMatchingMask:NSEventMaskKeyDown untilDate:NSDate.date inMode:NSDefaultRunLoopMode dequeue:YES];
            Require(escape && escape.keyCode == 53, "Cancel at release did not post the owned Escape");
            p.End(NSDragOperationNone);
        };
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_NONE,
            "canceled release retained an operation result");
    } else if (scenario == 53) {
        DragProbe p(f); uint32_t performed = 99; p.requestedAction = JALIUM_DRAG_CANCEL;
        info.draggingSource = f.view;
        p.tick = [&] {
            SelectCopy(f); [f.Target() draggingEntered:sender];
            Require([f.Target() prepareForDragOperation:sender], "owned target could not prepare before cancellation");
            // AppKit's own tracking can call movedToPoint at release without
            // passing the event through NSApplication's local monitor.
            p.event = [NSEvent mouseEventWithType:NSEventTypeLeftMouseUp location:NSMakePoint(32,40)
                modifierFlags:0 timestamp:0 windowNumber:f.native.windowNumber context:nil eventNumber:2 clickCount:1 pressure:0];
            [f.Source() draggingSession:p.Session() movedToPoint:NSZeroPoint];
            Require(p.queries == 1 && !(p.lastKeys & 1), "native tracking release did not query the source");
            Require([f.Source() draggingSession:p.Session() sourceOperationMaskForDraggingContext:NSDraggingContextOutsideApplication] == NSDragOperationNone,
                "canceled source still advertised operations to an external destination");
            Require(![f.Target() prepareForDragOperation:sender] && ![f.Target() performDragOperation:sender] && !f.Count(JALIUM_EVENT_DROP),
                "native tracking release committed an owned target after Cancel");
            p.End(NSDragOperationCopy); // A stale native result cannot undo Cancel.
        };
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_NONE,
            "native tracking cancellation retained a stale operation result");
    } else if (scenario >= 54 && scenario <= 56) {
        DragProbe p(f); uint32_t performed = 99;
        p.requestedAction = scenario == 54 ? JALIUM_DRAG_CANCEL : scenario == 55 ? JALIUM_DRAG_CONTINUE : JALIUM_DRAG_DROP;
        p.begin = [&] {
            NSEvent* terminal = scenario == 56 ?
                [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint modifierFlags:0 timestamp:0
                    windowNumber:f.native.windowNumber context:nil characters:@"\x1b" charactersIgnoringModifiers:@"\x1b" isARepeat:NO keyCode:53] :
                [NSEvent mouseEventWithType:NSEventTypeLeftMouseUp location:NSMakePoint(32,40)
                    modifierFlags:0 timestamp:0 windowNumber:f.native.windowNumber context:nil eventNumber:2 clickCount:1 pressure:0];
            [NSApp sendEvent:terminal];
            Require(p.queries == 1 && p.lastEscape == (scenario == 56), "startup terminal event escaped the owned drag query");
            Require(!f.Count(JALIUM_EVENT_MOUSE_UP) && !f.Count(JALIUM_EVENT_KEY_DOWN), "startup terminal event reached normal window input");
            if (scenario == 54 || scenario == 56) {
                NSEventMask mask = scenario == 54 ? NSEventMaskKeyDown : NSEventMaskLeftMouseUp;
                NSEvent* replacement = [NSApp nextEventMatchingMask:mask untilDate:NSDate.date inMode:NSDefaultRunLoopMode dequeue:YES];
                Require(replacement && replacement.windowNumber == f.native.windowNumber &&
                    (scenario == 54 ? replacement.keyCode == 53 : replacement.type == NSEventTypeLeftMouseUp),
                    "startup action did not post the owned replacement event");
            }
        };
        Require(p.Run(&performed) == JALIUM_OK && performed == (scenario == 54 ? JALIUM_DRAG_EFFECT_NONE : JALIUM_DRAG_EFFECT_COPY),
            "startup query lost the final source effect");
        int queried = p.queries;
        auto release = [NSEvent mouseEventWithType:NSEventTypeLeftMouseUp location:NSMakePoint(32,40)
            modifierFlags:0 timestamp:0 windowNumber:f.native.windowNumber context:nil eventNumber:3 clickCount:1 pressure:0];
        [NSApp sendEvent:release];
        Require(p.queries == queried && !p.activeMonitors.count, "finished source retained its event monitor");
    } else if (scenario == 57) {
        DragProbe p(f); p.scheduleTick = false; uint32_t performed = 99;
        p.begin = [&] { @throw [NSException exceptionWithName:@"JaliumOwnedRejectedDrag" reason:@"owned startup rejection" userInfo:nil]; };
        bool escaped = false; JaliumResult result = JALIUM_OK;
        @try { result = p.Run(&performed); }
        @catch (NSException*) { escaped = true; }
        Require(!escaped && result == JALIUM_ERROR_INVALID_STATE && performed == JALIUM_DRAG_EFFECT_NONE,
            "AppKit startup exception escaped the C ABI or retained an effect");
        Require(!p.queries && !p.feedback && !f.Count(JALIUM_EVENT_DRAG_FINISHED), "rejected startup delivered source callbacks");
        Require(!p.activeMonitors.count, "rejected startup retained its event monitor");
        p.begin = {}; p.scheduleTick = true;
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY && p.begins == 2,
            "AppKit startup exception prevented the next owned drag");
        Require(!p.activeMonitors.count, "repeated source retained its event monitor");
    } else if (scenario == 58) {
        DragProbe p(f); p.noQuery = true; uint32_t performed = 99;
        p.begin = [&] {
            auto release = [NSEvent mouseEventWithType:NSEventTypeLeftMouseUp location:NSMakePoint(32,40)
                modifierFlags:0 timestamp:0 windowNumber:f.native.windowNumber context:nil eventNumber:2 clickCount:1 pressure:0];
            auto escape = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint modifierFlags:0 timestamp:0
                windowNumber:f.native.windowNumber context:nil characters:@"\x1b" charactersIgnoringModifiers:@"\x1b" isARepeat:NO keyCode:53];
            Require(!p.monitorHandler || (p.monitorHandler(release) == release && p.monitorHandler(escape) == escape),
                "source without a query callback swallowed AppKit termination");
        };
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY && !p.queries && !p.feedback && !p.activeMonitors.count,
            "legacy source lost its operation or retained callbacks");
    } else if (scenario == 59) {
        DragProbe p(f); p.refuseMonitor = true; uint32_t performed = 99;
        Require(p.Run(&performed) == JALIUM_ERROR_INVALID_STATE && performed == JALIUM_DRAG_EFFECT_NONE && !p.begins && !p.activeMonitors.count,
            "source started without its required query monitor");
        p.refuseMonitor = false;
        Require(p.Run(&performed) == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY && p.begins == 1,
            "query monitor refusal prevented the next drag");
    } else if (scenario == 60) {
        SelectCopy(f); [f.Target() draggingEntered:sender]; uint64_t incoming = f.Session(JALIUM_EVENT_DRAG_ENTER);
        DragProbe p(f); p.begin = [&] { @throw [NSException exceptionWithName:@"JaliumOwnedRejectedDrag" reason:@"outgoing startup rejection" userInfo:nil]; };
        uint32_t performed = 99;
        Require(p.Run(&performed) == JALIUM_ERROR_INVALID_STATE && performed == JALIUM_DRAG_EFFECT_NONE,
            "outgoing startup exception did not return failure");
        Require(f.Session(JALIUM_EVENT_DRAG_ENTER) == incoming && [f.Target() prepareForDragOperation:sender],
            "outgoing startup failure invalidated an unrelated incoming target");
        Require(ReadVisit(f, incoming, "text/plain") == "拖放保留中文🙂", "outgoing failure detached incoming data");
    } else throw std::runtime_error("unknown data scenario");
}

static void RunRepresentationCase(Fixture& f, int scenario, JaliumDragTestInfo* info) {
    struct ActionReset { Fixture& fixture; ~ActionReset() { fixture.action = nullptr; } } actionReset{f};
    constexpr NSUInteger limit = 256 * 1024 * 1024;
    auto oversized = [JaliumDragOversizedData new];
    oversized.reportedLength = scenario == 62 || scenario == 64 || scenario == 69
        ? static_cast<NSUInteger>(UINT32_MAX) + 1 : limit + 1;
    auto board = [JaliumDragRepresentationPasteboard new];
    board.offered = @[NSPasteboardTypeHTML];
    board.firstData = (NSData*)oversized; info.board = board;
    auto sender = (id<NSDraggingInfo>)info;
    SelectCopy(f);
    if (scenario == 68 || scenario == 69) {
        ClipboardReadProbe probe(board); // Never reads or writes the user's clipboard.
        uint8_t* bytes = reinterpret_cast<uint8_t*>(1); uint32_t size = 99;
        Require(jalium_clipboard_get_data("text/html", &bytes, &size) == JALIUM_ERROR_INVALID_ARGUMENT && !bytes && !size,
            "oversized clipboard representation was copied or truncated");
        Require(!oversized.byteReads && board.dataReads == 1, "oversized clipboard bytes were accessed");
        return;
    }
    if (scenario == 70) {
        ClipboardReadProbe probe(board);
        for (int index = 0; index < 3; ++index) {
            NSData* expected = index == 0 ? [@"<b>保留🙂</b>" dataUsingEncoding:NSUTF8StringEncoding] : index == 1 ? [NSData data] : nil;
            board.firstData = expected;
            uint8_t* bytes = reinterpret_cast<uint8_t*>(1); uint32_t size = 99;
            Require(jalium_clipboard_get_data("text/html", &bytes, &size) == JALIUM_OK &&
                (bytes != nullptr) == (expected != nil) && size == expected.length,
                "valid, empty and missing clipboard representations were conflated");
            if (size) Require(std::memcmp(bytes, expected.bytes, size) == 0, "clipboard bytes changed during copy");
            jalium_platform_free(bytes);
        }
        return;
    }
    if (scenario == 65 || scenario == 66) board.offered = @[NSPasteboardTypeHTML, NSPasteboardTypeString];
    if (scenario == 66) {
        board.firstData = nil;
        board.onRead = ^{ jalium_window_hide(f.window); };
    } else if (scenario == 67) {
        board.onRead = ^{
            info.board = [JaliumDragTestPasteboard new]; ++info.draggingSequenceNumber;
            [f.Target() draggingEntered:sender];
        };
    } else if (scenario == 71) board.offered = @[NSPasteboardTypeTIFF];
    else if (scenario == 73) board.firstData = [NSData data];
    else if (scenario == 74 || scenario == 75) {
        auto reentrant = [JaliumDragReentrantData new];
        reentrant.payload = [@"owned" dataUsingEncoding:NSUTF8StringEncoding];
        reentrant.onBytes = ^{
            info.board = [JaliumDragTestPasteboard new]; ++info.draggingSequenceNumber;
            [f.Target() draggingEntered:sender];
        };
        board.firstData = reentrant;
    }
    [f.Target() draggingEntered:sender]; uint64_t token = f.Session(JALIUM_EVENT_DRAG_ENTER);
    if (scenario == 61 || scenario == 62) {
        uint8_t* bytes = reinterpret_cast<uint8_t*>(1); uint32_t size = 99;
        Require(jalium_apple_drag_get_data(f.window, token, "text/html", &bytes, &size) == JALIUM_ERROR_INVALID_ARGUMENT && !bytes && !size,
            "oversized lazy drag representation was copied");
        Require(!oversized.byteReads && board.dataReads == 1, "oversized drag bytes were accessed");
        Require([f.Target() prepareForDragOperation:sender], "rejected representation retired its live visit");
    } else if (scenario == 71) {
        (void)ReadVisit(f, token, "image/png", false);
        Require(!oversized.byteReads && board.dataReads == 1, "oversized TIFF was decoded to synthesize PNG");
    } else if (scenario == 74 || scenario == 75) {
        if (scenario == 74) {
            uint8_t* bytes = nullptr; uint32_t size = 0;
            JaliumResult result = jalium_apple_drag_get_data(f.window, token, "text/html", &bytes, &size);
            bool rejected = result == JALIUM_ERROR_INVALID_STATE && !bytes && !size;
            jalium_platform_free(bytes);
            Require(rejected, "bytes accessor reentry returned data from the retired visit");
        } else Require(![f.Target() performDragOperation:sender] && !f.Count(JALIUM_EVENT_DROP),
            "bytes accessor reentry delivered Drop into the successor");
        uint64_t successor = f.Session(JALIUM_EVENT_DRAG_ENTER);
        Require(successor != token && [f.Target() prepareForDragOperation:sender], "bytes accessor reentry retired its successor");
        Require(!ReadVisit(f, successor, "text/plain").empty(), "successor lost its data after bytes accessor reentry");
    } else if (scenario == 65 || scenario == 73) {
        std::string delivered, mime; uint32_t deliveredSize = 99;
        auto original = f.action;
        f.action = [&](const JaliumPlatformEvent& e) {
            original(e);
            if (e.type == JALIUM_EVENT_DROP) {
                deliveredSize = e.drag.dataSize; mime = e.drag.dataMimeType;
                delivered.assign(e.drag.dataSize ? reinterpret_cast<const char*>(e.drag.data) : "", e.drag.dataSize);
            }
        };
        Require([f.Target() performDragOperation:sender] && f.Count(JALIUM_EVENT_DROP) == 1,
            "valid secondary or empty representation did not reach Drop");
        Require(!oversized.byteReads && deliveredSize == (scenario == 65 ? std::strlen("保留其他格式🙂") : 0),
            "Drop accessed oversized bytes or changed the valid payload size");
        if (scenario == 65) Require(delivered == "保留其他格式🙂" && mime == "text/plain;charset=utf-8" && board.dataReads == 2,
            "rejected primary representation discarded the valid secondary format");
        RequireRetiredRead(f, token); f.action = original;
    } else {
        Require(![f.Target() performDragOperation:sender] && !f.Count(JALIUM_EVENT_DROP),
            "oversized or retired representation reached Drop");
        Require(!oversized.byteReads && board.dataReads == 1, "Drop read oversized bytes or continued after retirement");
        if (scenario == 67) {
            uint64_t successor = f.Session(JALIUM_EVENT_DRAG_ENTER);
            Require(successor != token && [f.Target() prepareForDragOperation:sender], "failed Drop retired its successor");
            Require(!ReadVisit(f, successor, "text/plain").empty(), "successor lost its readable representation");
        } else if (scenario == 72) {
            [f.Target() draggingExited:sender]; RequireRetiredRead(f, token);
            info.board = [JaliumDragTestPasteboard new]; ++info.draggingSequenceNumber;
            [f.Target() draggingEntered:sender];
            Require([f.Target() performDragOperation:sender] && f.Count(JALIUM_EVENT_DROP) == 1,
                "an oversized Drop prevented the next valid visit");
        }
    }
}

static constexpr int CasesPerStyle = 76;

static int RunCase(int index) {
    constexpr uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
        JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE;
    uint32_t style = index < CasesPerStyle ? regular : (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS;
    int scenario = index % CasesPerStyle;
    @autoreleasepool {
        Require(jalium_platform_init() == JALIUM_OK, "platform initialization failed");
        [NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited]; [NSApp finishLaunching];
        Fixture f(style);
        auto info = [JaliumDragTestInfo new]; info.draggingSequenceNumber = 55; info.board = [JaliumDragTestPasteboard new];
        auto sender = (id<NSDraggingInfo>)info;
        auto retiredSession = (NSDraggingSession*)[JaliumDragTestSession new];
        if (scenario < 7) {
            f.Destroy(); size_t count = f.events.size();
            switch (scenario) {
                case 0: Require([f.Source() draggingSession:retiredSession sourceOperationMaskForDraggingContext:NSDraggingContextOutsideApplication] == NSDragOperationNone, "retired source allowed an operation"); break;
                case 1: [f.Source() draggingSession:retiredSession endedAtPoint:NSZeroPoint operation:NSDragOperationCopy]; break;
                case 2: [f.Source() draggingSession:retiredSession movedToPoint:NSZeroPoint]; break;
                case 3: Require([f.Target() draggingEntered:sender] == NSDragOperationNone, "retired target entered"); break;
                case 4: Require([f.Target() draggingUpdated:sender] == NSDragOperationNone, "retired target updated"); break;
                case 5: [f.Target() draggingExited:sender]; break;
                case 6: Require(![f.Target() performDragOperation:sender], "retired target accepted a drop"); break;
            }
            Require(f.events.size() == count, "retired view published a drag event");
        } else if (scenario < 11) {
            const JaliumEventType types[] = {JALIUM_EVENT_DRAG_ENTER, JALIUM_EVENT_DRAG_OVER, JALIUM_EVENT_DRAG_LEAVE, JALIUM_EVENT_DROP};
            auto type = types[scenario - 7];
            if (type != JALIUM_EVENT_DRAG_ENTER) [f.Target() draggingEntered:sender];
            std::unique_ptr<Fixture> replacement;
            f.action = [&](const JaliumPlatformEvent& e) {
                if (e.type != type) return;
                f.Destroy(); replacement = std::make_unique<Fixture>(style); replacement->events.clear();
            };
            switch (type) {
                case JALIUM_EVENT_DRAG_ENTER: Require([f.Target() draggingEntered:sender] == NSDragOperationNone, "closed callback target returned an effect"); break;
                case JALIUM_EVENT_DRAG_OVER: Require([f.Target() draggingUpdated:sender] == NSDragOperationNone, "closed callback target returned an effect"); break;
                case JALIUM_EVENT_DRAG_LEAVE: [f.Target() draggingExited:sender]; break;
                case JALIUM_EVENT_DROP: Require(![f.Target() performDragOperation:sender], "closed callback target accepted a drop"); break;
                default: break;
            }
            Require(replacement && replacement->events.empty() && replacement->native.visible,
                "old target callback changed the replacement");
        } else if (scenario >= 61) {
            RunRepresentationCase(f, scenario, info);
        } else if (scenario >= 40) {
            RunDataCase(f, scenario, info);
        } else if (scenario >= 16) {
            RunExtended(f, scenario, info);
        } else {
            DragProbe p(f); uint32_t performed = 0xffffffff;
            std::unique_ptr<Fixture> replacement;
            if (scenario == 11) p.refuse = true;
            else if (scenario == 12) p.begin = [&] { f.Destroy(); };
            else if (scenario == 13) {
                p.query = [&] { f.Destroy(); };
                p.tick = [&] { [f.Source() draggingSession:p.Session() movedToPoint:NSZeroPoint]; };
            } else if (scenario == 14) {
                f.action = [&](const JaliumPlatformEvent& e) {
                    if (e.type != JALIUM_EVENT_DRAG_FINISHED) return;
                    f.Destroy(); replacement = std::make_unique<Fixture>(style); replacement->events.clear();
                };
            }
            JaliumResult result = p.Run(&performed);
            if (scenario == 11) Require(result == JALIUM_ERROR_INVALID_STATE && performed == JALIUM_DRAG_EFFECT_NONE,
                "refused native session did not return without an effect");
            else if (scenario < 14) Require(result == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_NONE && p.feedback == 0,
                "destroyed source retained an effect or feedback callback");
            else Require(result == JALIUM_OK && performed == JALIUM_DRAG_EFFECT_COPY && f.Count(JALIUM_EVENT_DRAG_FINISHED) == 1,
                "successful completion lost its effect or fired more than once");
            Require(!replacement || replacement->events.empty(), "old source completion changed the replacement");
        }
        std::printf("PASS %d: %s, drag lifecycle scenario=%d\n", index, index < CasesPerStyle ? "Native" : "Custom", scenario);
        return 0;
    }
}
int main(int argc, char** argv) {
    std::setvbuf(stdout, nullptr, _IOLBF, 0);
    struct rlimit coreLimit{0, 0}; setrlimit(RLIMIT_CORE, &coreLimit);
    if (argc == 2 && std::strncmp(argv[1], "--case=", 7) == 0) {
        int index = std::stoi(argv[1] + 7); if (index < 0 || index >= CasesPerStyle * 2) return 2;
        @try {
            try { return RunCase(index); }
            catch (const std::exception& e) { std::fprintf(stderr, "FAIL %d: %s\n", index, e.what()); return 1; }
        } @catch (NSException* exception) {
            std::fprintf(stderr, "FAIL %d: %s: %s\n", index, exception.name.UTF8String, exception.reason.UTF8String); return 1;
        }
    }
    bool coreOnly = argc == 2 && std::strcmp(argv[1], "--core-only") == 0;
    bool extendedOnly = argc == 2 && std::strcmp(argv[1], "--extended-only") == 0;
    bool representationsOnly = argc == 2 && std::strcmp(argv[1], "--representations-only") == 0;
    if (argc != 1 && !coreOnly && !extendedOnly && !representationsOnly) return 2;
    int passed = 0, total = 0;
    for (int index = 0; index < CasesPerStyle * 2; ++index) {
        if ((coreOnly && index % CasesPerStyle >= 16) || (extendedOnly && index % CasesPerStyle < 16) ||
            (representationsOnly && index % CasesPerStyle < 61)) continue;
        ++total;
        std::string argument = "--case=" + std::to_string(index);
        char* child[] = {argv[0], argument.data(), nullptr}; pid_t pid; int status = 0;
        if (posix_spawn(&pid, argv[0], nullptr, nullptr, child, environ) == 0 &&
            waitpid(pid, &status, 0) == pid && WIFEXITED(status) && WEXITSTATUS(status) == 0) ++passed;
    }
    std::printf("macOS native Window drag lifetime checks: %d/%d passed\n", passed, total);
    return passed == total ? 0 : 1;
}
