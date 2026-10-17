#include "jalium_platform.h"
#include "platform_apple_gesture_test_event.h"
#include <dlfcn.h>
#import <objc/runtime.h>
#include <cmath>
#include <cstdio>
#include <stdexcept>
#include <thread>
#include <vector>

@interface JaliumGestureFallbackProbe : NSResponder {
@public
    int magnifies, rotations, begins, ends;
    std::function<void()> action;
}
@end
@implementation JaliumGestureFallbackProbe
- (void)magnifyWithEvent:(NSEvent*)event { ++magnifies; auto call=std::move(action); action={}; if(call)call(); }
- (void)rotateWithEvent:(NSEvent*)event { ++rotations; auto call=std::move(action); action={}; if(call)call(); }
- (void)beginGestureWithEvent:(NSEvent*)event { ++begins; }
- (void)endGestureWithEvent:(NSEvent*)event { ++ends; }
@end
@interface JaliumGestureBoundaryQueryEvent : JaliumGestureTestEvent
@end
@implementation JaliumGestureBoundaryQueryEvent
- (NSTimeInterval)timestamp { auto call=std::move(getter); getter={}; if(call)call(); return self.sampleTime; }
@end

static __unsafe_unretained NSWindow* hookedWindow;
static std::function<void()> readinessAction;
static IMP originalVisible;
static BOOL ReadinessVisible(id object, SEL selector) {
    BOOL value=reinterpret_cast<BOOL(*)(id,SEL)>(originalVisible)(object,selector);
    if(object==hookedWindow){auto action=std::move(readinessAction);readinessAction={};if(action)action();}
    return value;
}
struct ReadinessHook {
    NSWindow* window;
    Class original;
    explicit ReadinessHook(NSWindow* value):window(value),original(object_getClass(value)) {
        Class probe=objc_getClass("JaliumGestureReadinessProbe");
        if(!probe){probe=objc_allocateClassPair(original,"JaliumGestureReadinessProbe",0);
            Method method=class_getInstanceMethod(original,@selector(isVisible));originalVisible=method_getImplementation(method);
            class_addMethod(probe,@selector(isVisible),reinterpret_cast<IMP>(ReadinessVisible),method_getTypeEncoding(method));objc_registerClassPair(probe);}
        hookedWindow=window;object_setClass(window,probe);
    }
    ~ReadinessHook(){readinessAction={};object_setClass(window,original);hookedWindow=nil;}
};

static int checks;
static void Check(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); ++checks; }
using RegisterGesture = int32_t(*)(JaliumPlatformWindow*,JaliumAppleGestureCallback,void*);
static RegisterGesture registerGesture;
static void Pump() { [NSRunLoop.currentRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:.025]]; }
struct Fixture {
    JaliumPlatformWindow* owner = nullptr;
    NSView* view;
    std::vector<JaliumAppleGestureEvent> events;
    std::function<void(const JaliumAppleGestureEvent&)> action;
    bool accept = true;
    explicit Fixture(uint32_t style) {
        JaliumWindowParams p{};p.title=reinterpret_cast<const uint16_t*>(u"Jalium trackpad protocol");
        p.x=80;p.y=80;p.width=420;p.height=260;p.style=style;
        owner=jalium_window_create(&p);Check(owner,"create own AppKit window");
        view=(__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(owner));
        jalium_apple_window_show(owner,0);Check(view.window.visible,"own SDK window is visible");
        Check(registerGesture(owner,Callback,this)==0,"register optional gesture callback");
    }
    ~Fixture(){if(owner)jalium_window_destroy(owner);}
    static int32_t Callback(const JaliumAppleGestureEvent* e,void* context) {
        auto* f=static_cast<Fixture*>(context);Check(e&&e->size==64,"stable gesture record size");
        f->events.push_back(*e);auto call=f->action;if(call)call(*e);return f->accept;
    }
    JaliumGestureTestEvent* Event(NSEventPhase phase,double value=0) {
        auto* e=[JaliumGestureTestEvent new];e.samplePhase=phase;e.sampleMagnification=value;
        e.sampleRotation=value;e.sampleTime=1+events.size()*.02;
        e.samplePoint=[view convertPoint:NSMakePoint(31.25,47.5) toView:nil];return e;
    }
    void Magnify(NSEventPhase phase,double value=0) {[view magnifyWithEvent:Event(phase,value)];}
    void Rotate(NSEventPhase phase,double value=0) {[view rotateWithEvent:Event(phase,value)];}
    void Begin(){[view beginGestureWithEvent:Event(NSEventPhaseNone)];}
    void End(){[view endGestureWithEvent:Event(NSEventPhaseNone)];}
};

static void Run(uint32_t style) {
    { Fixture f(style);f.Magnify(NSEventPhaseBegan,.25);f.Magnify(NSEventPhaseChanged,-.1);f.Magnify(NSEventPhaseEnded);
      Check(f.events.size()==3,"one begin, change and end");Check(f.events[0].phase==1&&f.events[1].phase==2&&f.events[2].phase==3,"native phases");
      Check(std::abs(f.events[0].scale-1.25)<1e-6&&std::abs(f.events[1].scale-.9)<1e-6,"incremental scale factors");
      Check(f.events[0].sequence==f.events[2].sequence,"stable sequence identity");double dpi=f.view.window.backingScaleFactor;
      Check(std::abs(f.events[0].x-31.25*dpi)<1e-6&&std::abs(f.events[0].y-47.5*dpi)<1e-6,"flipped physical client origin"); }
    { Fixture f(style);f.Rotate(NSEventPhaseBegan,12);f.Rotate(NSEventPhaseEnded);Check(f.events[0].rotation==-12&&f.events[0].scale==1,"clockwise screen rotation"); }
    { Fixture f(style);f.Begin();Check(f.events.empty(),"generic begin does not invent a manipulation");
      f.Magnify(NSEventPhaseBegan,.2);f.Rotate(NSEventPhaseChanged,7);f.Magnify(NSEventPhaseEnded);f.Rotate(NSEventPhaseEnded);f.End();
      Check(f.events.size()==5&&f.events.back().kind==0&&f.events.back().phase==3,"generic end closes combined sequence");
      for(auto& e:f.events)Check(e.sequence==f.events[0].sequence,"magnify/rotate share generic sequence"); }
    { Fixture f(style);f.Magnify(NSEventPhaseBegan,.2);f.Magnify(NSEventPhaseCancelled);auto n=f.events.size();f.Magnify(NSEventPhaseChanged,.3);Check(f.events.size()==n&&f.events.back().phase==4,"cancelled tail cannot restart"); }
    { Fixture f(style);f.Magnify(NSEventPhaseNone,.2);Check(f.events.size()==2&&f.events[0].phase==1&&f.events[1].phase==3,"phase-less operation has bounded lifetime");auto first=f.events[0].sequence;f.Magnify(NSEventPhaseNone,.1);Check(f.events[2].sequence!=first,"phase-less operations have distinct identities"); }
    { Fixture f(style);f.accept=false;f.Begin();f.Magnify(NSEventPhaseBegan,.2);f.Magnify(NSEventPhaseChanged,.2);f.Rotate(NSEventPhaseBegan,2);Check(f.events.size()==1,"declined sequence does not retarget");f.End();f.accept=true;f.Magnify(NSEventPhaseBegan,.2);Check(f.events.size()==2,"new sequence can be accepted"); }
    for(bool hide:{false,true}) { Fixture f(style);f.Begin();f.Magnify(NSEventPhaseBegan,.2);auto first=f.events[0].sequence;
      if(hide){jalium_window_hide(f.owner);jalium_apple_window_show(f.owner,0);}else{jalium_window_set_enabled(f.owner,0);jalium_window_set_enabled(f.owner,1);}
      f.Magnify(NSEventPhaseChanged,.2);Pump();Check(f.events.size()==2&&f.events.back().phase==4,"visibility/enabled roundtrip cancels old sequence");
      f.Begin();f.Magnify(NSEventPhaseBegan,.1);Check(f.events.back().sequence!=first,"fresh gesture survives cancelled predecessor"); }
    { Fixture f(style);f.action=[&](auto&){f.action={};auto* owner=f.owner;f.owner=nullptr;jalium_window_destroy(owner);};f.Magnify(NSEventPhaseBegan,.2);Check(f.events.size()==1,"callback destroys window safely"); }
    { Fixture f(style);f.action=[&](auto&){f.action={};registerGesture(f.owner,nullptr,nullptr);};f.Magnify(NSEventPhaseBegan,.2);Check(f.events.size()==2&&f.events.back().phase==4,"registration replacement cancels an in-flight begin");auto n=f.events.size();f.Magnify(NSEventPhaseChanged,.1);Check(f.events.size()==n,"disabled callback remains retired"); }
    { Fixture f(style);f.Magnify(NSEventPhaseBegan,.2);auto* e=f.Event(NSEventPhaseChanged,.3);
      e->getter=[&]{f.Begin();f.Magnify(NSEventPhaseBegan,.1);};[f.view magnifyWithEvent:e];auto token=f.events.back().sequence;
      f.Magnify(NSEventPhaseChanged,.1);Check(f.events.back().sequence==token&&f.events.back().phase==2,"getter reentry keeps the newer gesture"); }
    { Fixture f(style);bool once=false;f.action=[&](auto& e){if(!once&&e.phase==1){once=true;f.Magnify(NSEventPhaseChanged,.1);}};
      f.Magnify(NSEventPhaseNone,.2);Check(f.events.size()==3&&f.events.back().phase==3,"phase-less reentry still completes once"); }
    { Fixture f(style);auto* event=f.Event(NSEventPhaseBegan,.2);std::thread worker([&]{[f.view magnifyWithEvent:event];});worker.join();Check(f.events.empty(),"background SDK gesture is rejected before AppKit queries"); }
    { Fixture f(style|JALIUM_WINDOW_STYLE_INPUT_TRANSPARENT);f.Magnify(NSEventPhaseBegan,.2);Check(f.events.empty(),"input-transparent surface does not claim gestures"); }
    { Fixture f(style);f.Begin();f.Magnify(NSEventPhaseBegan,.2);NSWindow* original=f.view.window;
      [original setContentView:[NSView new]];Pump();Check(f.events.back().phase==4,"native view detachment cancels the original sequence");
      [original setContentView:f.view];f.Begin();f.Magnify(NSEventPhaseBegan,.1);Check(f.events.back().phase==1,"reattached view accepts a fresh sequence"); }
    { Fixture f(style);NSWindow* modal=[[NSWindow alloc]initWithContentRect:NSMakeRect(90,90,220,120) styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];
      modal.releasedWhenClosed=NO;
      struct ModalGuard { NSModalSession session; NSWindow* window; void End(){if(session){[NSApp endModalSession:session];session=nullptr;[window close];}}~ModalGuard(){End();} } guard{[NSApp beginModalSessionForWindow:modal],modal};
      [NSApp runModalSession:guard.session];
      Check(NSApp.modalWindow==modal,"real AppKit application-modal scope is installed");f.Magnify(NSEventPhaseBegan,.1);Check(f.events.empty(),"foreign native modal rejects gestures");
      guard.End();f.Magnify(NSEventPhaseBegan,.1);Check(f.events.size()==1,"gesture input returns after native modal closes"); }
    { Fixture f(style);int result=0;std::thread t([&]{result=registerGesture(f.owner,nullptr,nullptr);});t.join();Check(result==JALIUM_ERROR_INVALID_STATE,"off-main registration is rejected"); }
}
static void RunFallback(uint32_t style) {
    for(bool rotation:{false,true}) {
        for(bool registered:{false,true}) {
            Fixture f(style);f.accept=false;auto* probe=[JaliumGestureFallbackProbe new];f.view.nextResponder=probe;
            if(!registered)Check(registerGesture(f.owner,nullptr,nullptr)==0,"disable bridge for legacy fallback");
            if(rotation)f.Rotate(NSEventPhaseBegan,8);else f.Magnify(NSEventPhaseBegan,.2);
            Check((rotation?probe->rotations:probe->magnifies)==1,"live unhandled gesture reaches actual next responder once");
        }
        { Fixture f(style);auto* probe=[JaliumGestureFallbackProbe new];f.view.nextResponder=probe;
          if(rotation)f.Rotate(NSEventPhaseBegan,8);else f.Magnify(NSEventPhaseBegan,.2);
          Check(probe->magnifies+probe->rotations==0,"handled gesture does not enter fallback"); }
        for(int mutation=0;mutation<5;++mutation) {
            Fixture f(style);f.accept=false;auto* probe=[JaliumGestureFallbackProbe new];f.view.nextResponder=probe;
            f.action=[&](auto&){f.action={};
                if(mutation==0)jalium_window_hide(f.owner);
                else if(mutation==1){jalium_window_hide(f.owner);jalium_apple_window_show(f.owner,0);}
                else if(mutation==2){jalium_window_set_enabled(f.owner,0);jalium_window_set_enabled(f.owner,1);}
                else if(mutation==3){auto* owner=f.owner;f.owner=nullptr;jalium_window_destroy(owner);}
                else registerGesture(f.owner,nullptr,nullptr);
                f.view.nextResponder=probe;
            };
            if(rotation)f.Rotate(NSEventPhaseBegan,8);else f.Magnify(NSEventPhaseBegan,.2);
            if(probe->magnifies+probe->rotations)std::fprintf(stderr,"stale fallback mutation=%d rotation=%d\n",mutation,rotation);
            Check(probe->magnifies+probe->rotations==0,"retired callback must not forward its gesture");
        }
    }
    { Fixture f(style);f.accept=false;auto* probe=[JaliumGestureFallbackProbe new];f.view.nextResponder=probe;
      f.action=[&](auto&){f.action={};f.Begin();f.accept=true;f.Magnify(NSEventPhaseBegan,.1);f.accept=false;};
      f.Magnify(NSEventPhaseBegan,.2);Check(f.events.size()==2&&f.events[0].sequence!=f.events[1].sequence,"callback starts a distinct newer gesture");
      Check(probe->magnifies==0,"reentrant newer gesture prevents old responder fallback");
      f.accept=true;auto sequence=f.events.back().sequence;f.Magnify(NSEventPhaseChanged,.1);
      Check(f.events.back().sequence==sequence&&f.events.back().phase==2,"newer gesture remains usable after the retired callback");f.End(); }
    { Fixture f(style);f.accept=false;auto* probe=[JaliumGestureFallbackProbe new];f.view.nextResponder=probe;
      auto* event=f.Event(NSEventPhaseBegan,.2);event->getter=[&]{jalium_window_hide(f.owner);jalium_apple_window_show(f.owner,0);};
      [f.view magnifyWithEvent:event];Check(f.events.empty()&&probe->magnifies==0,"retired event getter cannot fall through"); }
    { Fixture f(style);auto* probe=[JaliumGestureFallbackProbe new];f.view.nextResponder=probe;
      f.Begin();f.Magnify(NSEventPhaseBegan,.2);
      f.action=[&](auto&){f.action={};auto* owner=f.owner;f.owner=nullptr;jalium_window_destroy(owner);f.view.nextResponder=probe;};
      f.End();Check(probe->ends==0,"retired generic end cannot forward after completion callback"); }
    { Fixture f(style);auto* probe=[JaliumGestureFallbackProbe new];f.view.nextResponder=probe;
      auto* event=[JaliumGestureBoundaryQueryEvent new];event.samplePhase=NSEventPhaseNone;event.sampleTime=1;
      event->getter=[&]{jalium_window_hide(f.owner);jalium_apple_window_show(f.owner,0);};
      [f.view beginGestureWithEvent:event];Check(probe->begins==0,"retired generic begin query cannot forward"); }
    { Fixture f(style);f.accept=false;auto* probe=[JaliumGestureFallbackProbe new];f.view.nextResponder=probe;
      NSWindow* modal=[[NSWindow alloc]initWithContentRect:NSMakeRect(80,80,220,120) styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];
      modal.releasedWhenClosed=NO;NSModalSession session=nullptr;
      f.action=[&](auto&){f.action={};session=[NSApp beginModalSessionForWindow:modal];[NSApp runModalSession:session];f.view.nextResponder=probe;};
      struct EndModal { NSModalSession& session; NSWindow* modal; ~EndModal(){if(session)[NSApp endModalSession:session];[modal close];} } guard{session,modal};
      f.Magnify(NSEventPhaseBegan,.2);Check(NSApp.modalWindow==modal,"callback installs real native modal scope");
      Check(probe->magnifies==0,"callback modal blocks fallback into the old responder"); }
    { Fixture f(style);f.accept=false;auto* probe=[JaliumGestureFallbackProbe new];f.view.nextResponder=probe;
      __weak NSView* weakView=f.view;__weak NSWindow* weakWindow=f.view.window;
      probe->action=[&]{auto* owner=f.owner;f.owner=nullptr;jalium_window_destroy(owner);f.view=nil;
          Check(weakView!=nil&&weakWindow!=nil,"responder handoff keeps its original view and window alive");};
      f.Magnify(NSEventPhaseBegan,.2);Check(probe->magnifies==1,"fallback can destroy its owner once"); }
}

static void RunPostReady(uint32_t style) {
    for (bool rotate : {false, true}) {
        Fixture f(style);
        f.accept = false;
        auto* probe = [JaliumGestureFallbackProbe new];
        f.view.nextResponder = probe;
        ReadinessHook hook(f.view.window);
        f.action = [&](auto&) {
            f.action = {};
            readinessAction = [&] {
                f.Begin();
                f.accept = true;
                f.Magnify(NSEventPhaseBegan, .1);
                f.accept = false;
            };
        };
        if (rotate) f.Rotate(NSEventPhaseBegan, 2);
        else f.Magnify(NSEventPhaseBegan, .2);
        Check(probe->magnifies == 0 && probe->rotations == 0,
            "post-dispatch readiness reentry must not forward the older gesture");
    }
    {
        Fixture f(style);
        registerGesture(f.owner, nullptr, nullptr);
        auto* probe = [JaliumGestureFallbackProbe new];
        f.view.nextResponder = probe;
        ReadinessHook hook(f.view.window);
        readinessAction = [&] { f.Magnify(NSEventPhaseBegan, .1); };
        f.Magnify(NSEventPhaseBegan, .2);
        Check(probe->magnifies == 1,
            "unregistered callback forwards the newer readiness gesture exactly once");
    }
}

int main(){@autoreleasepool{try{[NSApplication sharedApplication];
    registerGesture=reinterpret_cast<RegisterGesture>(dlsym(RTLD_DEFAULT,"jalium_apple_window_set_gesture_callback"));
    Check(registerGesture!=nullptr,"optional native gesture entry point exists");
    static_assert(sizeof(JaliumAppleGestureEvent)==64);static_assert(sizeof(JaliumPlatformEvent)==72);
    for(uint32_t style:{uint32_t(JALIUM_WINDOW_STYLE_TITLEBAR|JALIUM_WINDOW_STYLE_RESIZABLE),uint32_t(JALIUM_WINDOW_STYLE_BORDERLESS|JALIUM_WINDOW_STYLE_RESIZABLE)}){Run(style);RunFallback(style);RunPostReady(style);}
    Pump();std::printf("PASS AppKit gestures: %d checks; own NSResponder SDK messages, no physical trackpad input\n",checks);return 0;
}catch(const std::exception& e){std::fprintf(stderr,"FAIL AppKit gestures: %s after %d checks\n",e.what(),checks);return 1;}}}
