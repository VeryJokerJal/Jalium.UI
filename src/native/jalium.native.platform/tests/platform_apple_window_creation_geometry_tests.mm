#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <cmath>
#include <cstdio>
#include <dlfcn.h>
#include <initializer_list>

// Screen data is controlled only for calls originating in the platform image.
// AppKit creates real NSWindows using the real desktop. No screen is installed,
// no input is injected, and these tests do not qualify physical mixed DPI.
@interface JaliumCreationScreen : NSObject
@property NSRect frame;
@property NSRect visibleFrame;
@property CGFloat backingScaleFactor;
@end
@implementation JaliumCreationScreen
@end

static NSArray* topology;
static id mainScreen;
static void* platformImage;
static IMP originalScreens, originalMain, originalWindowScreen, originalScale;
static int passed, total, intercepted;
static bool PlatformCaller(void* address)
{ Dl_info info{}; return dladdr(address,&info) && info.dli_fbase==platformImage; }
#define CALLER PlatformCaller(__builtin_extract_return_addr(__builtin_return_address(0)))
static id Screens(id object, SEL selector)
{ if (CALLER) {++intercepted;return topology;}return reinterpret_cast<id(*)(id,SEL)>(originalScreens)(object,selector); }
static id Main(id object, SEL selector)
{ if (CALLER) {++intercepted;return mainScreen;}return reinterpret_cast<id(*)(id,SEL)>(originalMain)(object,selector); }
static id At(NSWindow* window)
{
    NSRect actual=window.frame;NSPoint top=NSMakePoint(NSMinX(actual),NSMaxY(actual));
    id closest=topology.firstObject;double distance=INFINITY;
    for (JaliumCreationScreen* screen in topology) {
        NSRect f=screen.frame;
        if (NSPointInRect(top,f)) return screen;
        double dx=MAX(MAX(NSMinX(f)-top.x,0),top.x-NSMaxX(f));
        double dy=MAX(MAX(NSMinY(f)-top.y,0),top.y-NSMaxY(f));
        double next=dx*dx+dy*dy;if(next<distance){distance=next;closest=screen;}
    }
    return closest;
}
static id WindowScreen(id object,SEL selector)
{ if(CALLER){++intercepted;return At((NSWindow*)object);}return reinterpret_cast<id(*)(id,SEL)>(originalWindowScreen)(object,selector); }
static CGFloat Scale(id object,SEL selector)
{ if(CALLER){++intercepted;return [At((NSWindow*)object) backingScaleFactor];}return reinterpret_cast<CGFloat(*)(id,SEL)>(originalScale)(object,selector); }
struct Replacements {
    Method screens,main,windowScreen,scale;
    Replacements() {
        screens=class_getClassMethod(NSScreen.class,@selector(screens));originalScreens=method_setImplementation(screens,(IMP)Screens);
        main=class_getClassMethod(NSScreen.class,@selector(mainScreen));originalMain=method_setImplementation(main,(IMP)Main);
        Class type=NSClassFromString(@"JaliumAppleWindow");
        auto replace=[&](SEL selector,IMP next,IMP& old) {
            Method inherited=class_getInstanceMethod(type,selector);old=method_getImplementation(inherited);
            class_addMethod(type,selector,old,method_getTypeEncoding(inherited));
            Method method=class_getInstanceMethod(type,selector);method_setImplementation(method,next);return method;
        };
        windowScreen=replace(@selector(screen),(IMP)WindowScreen,originalWindowScreen);
        scale=replace(@selector(backingScaleFactor),(IMP)Scale,originalScale);
    }
    ~Replacements() {
        method_setImplementation(screens,originalScreens);method_setImplementation(main,originalMain);
        method_setImplementation(windowScreen,originalWindowScreen);method_setImplementation(scale,originalScale);
    }
};
static id Screen(double x,double y,double width,double height,double scale)
{auto* screen=[JaliumCreationScreen new];screen.frame=NSMakeRect(x,y,width,height);screen.visibleFrame=screen.frame;screen.backingScaleFactor=scale;return screen;}
static void Check(bool value,const char* name)
{++total;if(value)++passed;else std::fprintf(stderr,"FAIL %s\n",name);}
static NSPoint Physical(NSPoint native,id screen)
{
    NSRect f=[screen frame];double primary=[topology.firstObject backingScaleFactor],scale=[screen backingScaleFactor];double top=NSMaxY([topology.firstObject frame]);
    return NSMakePoint(NSMinX(f)*primary+(native.x-NSMinX(f))*scale,(top-NSMaxY(f))*primary+(NSMaxY(f)-native.y)*scale);
}
static void Case(const char* name,NSArray* screens,id main,id destination,NSPoint point,uint32_t style)
{
    topology=screens;mainScreen=main;NSPoint expected=Physical(point,destination);double system=[main backingScaleFactor];
    JaliumWindowParams p{};p.title=(const uint16_t*)u"Jalium Window creation coordinates";p.x=lround(expected.x);p.y=lround(expected.y);p.width=lround(220*system);p.height=lround(140*system);p.style=style;
    auto* window=jalium_window_create(&p);Check(window!=nullptr,name);if(!window)return;
    NSView* view=(__bridge NSView*)(void*)jalium_window_get_native_handle(window);NSWindow* native=view.window;
    NSRect frame=native.frame;NSPoint actual=NSMakePoint(NSMinX(frame),NSMaxY(frame));int32_t x=0,y=0;jalium_window_get_position(window,&x,&y);
    bool correct=std::abs(actual.x-point.x)<1 && std::abs(actual.y-point.y)<1 && x==p.x && y==p.y;
    if(!correct)std::fprintf(stderr,"  %s native=(%.2f,%.2f) expected=(%.2f,%.2f), pixel=(%d,%d) expected=(%d,%d)\n",name,actual.x,actual.y,point.x,point.y,x,y,p.x,p.y);
    Check(correct,"creation preserves physical coordinate and native top-left");
    Check(std::abs(view.bounds.size.width-220)<1 && std::abs(view.bounds.size.height-140)<1,"creation retains requested logical client size");
    NSPoint moved=NSMakePoint(point.x+30,point.y-20),pixel=Physical(moved,destination);jalium_window_move(window,lround(pixel.x),lround(pixel.y));
    frame=native.frame;jalium_window_get_position(window,&x,&y);
    Check(std::abs(NSMinX(frame)-moved.x)<1 && std::abs(NSMaxY(frame)-moved.y)<1 && x==lround(pixel.x) && y==lround(pixel.y),"later move agrees with the creation coordinate contract");
    jalium_window_destroy(window);
}
int main()
{
    @autoreleasepool {
        Check(jalium_platform_init()==JALIUM_OK,"initialize AppKit");[NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited];
        Dl_info info{};Check(dladdr((void*)&jalium_window_create,&info)&&info.dli_fbase,"resolve real platform ABI");platformImage=info.dli_fbase;
        std::printf("LOADED_PLATFORM %s\n",info.dli_fname?:"unknown");
        IMP before=method_getImplementation(class_getClassMethod(NSScreen.class,@selector(screens)));
        {
            Replacements scope;id primary1=Screen(0,0,1440,900,1),primary2=Screen(0,0,1440,900,2);
            id right1=Screen(1440,0,1920,900,1),right2=Screen(1440,0,1920,900,2),left1=Screen(-1920,0,1920,900,1),above2=Screen(100,900,1400,800,2),below1=Screen(0,-1080,1920,1080,1);
            for(uint32_t style:{uint32_t(JALIUM_WINDOW_STYLE_BORDERLESS),uint32_t(JALIUM_WINDOW_STYLE_TITLEBAR|JALIUM_WINDOW_STYLE_RESIZABLE)}) {
                Case("uniform primary 1x",@[primary1],primary1,primary1,NSMakePoint(120,780),style);
                Case("uniform primary 2x",@[primary2],primary2,primary2,NSMakePoint(120,780),style);
                Case("right 1x after primary 2x",@[primary2,right1],primary2,right1,NSMakePoint(1540,780),style);
                Case("right 2x after primary 1x",@[primary1,right2],primary1,right2,NSMakePoint(1540,780),style);
                Case("main screen differs from primary",@[primary1,right2],right2,right2,NSMakePoint(1540,780),style);
                Case("negative left 1x",@[primary2,left1],primary2,left1,NSMakePoint(-1820,780),style);
                Case("above primary 2x",@[primary1,above2],primary1,above2,NSMakePoint(200,1600),style);
                Case("below primary 1x",@[primary2,below1],primary2,below1,NSMakePoint(120,-120),style);
            }
            Check(intercepted>50,"production ABI consumed controlled screen data");
        }
        Check(method_getImplementation(class_getClassMethod(NSScreen.class,@selector(screens)))==before,"SDK getters restored");
        jalium_platform_shutdown();std::printf("macOS Window creation coordinate checks: %d/%d passed; physicalMixedDPI=false\n",passed,total);return passed==total?0:1;
    }
}
