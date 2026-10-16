#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include "jalium_platform.h"
#include <dlfcn.h>
#include <cstdio>
#include <functional>
#include <stdexcept>
#include <thread>
#include <utility>

static int checks, activations, orders, keySelections;
static NSWindow* observed;
static IMP originalActivate, originalActivateIgnoring, originalOrder, originalKey, originalResponder;
static std::function<void()> afterKey, afterResponderRead;
static void Check(bool value, const char* message)
{ if (!value) throw std::runtime_error(message); ++checks; }
static void CountActivate(id, SEL) { ++activations; }
static void CountActivateIgnoring(id, SEL, BOOL) { ++activations; }
static void CountOrder(id object, SEL selector, id sender)
{
    if (object == observed) ++orders;
    reinterpret_cast<void(*)(id, SEL, id)>(originalOrder)(object, selector, sender);
}
static void CountKey(id object, SEL selector)
{
    if (object == observed) ++keySelections;
    reinterpret_cast<void(*)(id, SEL)>(originalKey)(object, selector);
    if (object == observed) {
        auto callback = std::move(afterKey); afterKey = {};
        if (callback) callback();
    }
}
static NSResponder* ReadResponder(id object, SEL selector)
{
    NSResponder* result = reinterpret_cast<NSResponder*(*)(id, SEL)>(originalResponder)(object, selector);
    if (object == observed) {
        auto callback = std::move(afterResponderRead); afterResponderRead = {};
        if (callback) callback();
    }
    return result;
}

@interface FocusResponder : NSView
@property(nonatomic, assign) std::function<bool()>* resign;
@end
@implementation FocusResponder
- (BOOL)acceptsFirstResponder { return YES; }
- (BOOL)resignFirstResponder {
    if (!_resign) return YES;
    auto callback = std::move(*_resign); *_resign = {};
    return callback ? callback() : YES;
}
@end

struct Fixture {
    JaliumPlatformWindow* window;
    NSWindow* native;
    NSView* view;
    FocusResponder* responder;
    std::function<bool()> resign;
    std::function<void()> query;
    static int32_t Query(JaliumAccessibilityRequest* request, void* data)
    {
        auto& fixture = *static_cast<Fixture*>(data);
        if (request->nodeId != 1) return 0;
        if (request->operation == JALIUM_AX_INFO) { request->flags = JALIUM_AX_ENABLED; return 1; }
        if (request->operation != JALIUM_AX_POPUP_CAN_BECOME_KEY) return 0;
        auto callback = std::move(fixture.query); fixture.query = {};
        if (callback) callback();
        request->resultId = 1; return 1;
    }
    explicit Fixture(bool transparent = false, bool popup = true)
    {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Jalium keyboard focus contracts");
        params.x = 120; params.y = 140; params.width = 300; params.height = 160;
        params.style = JALIUM_WINDOW_STYLE_BORDERLESS | (popup ? JALIUM_WINDOW_STYLE_POPUP : 0) |
            (transparent ? JALIUM_WINDOW_STYLE_INPUT_TRANSPARENT : 0);
        window = jalium_window_create(&params); Check(window, "create owned fixture");
        view = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(window));
        native = view.window;
        jalium_apple_window_set_accessibility(window, Query, this);
        jalium_apple_window_show(window, 0);
        responder = [[FocusResponder alloc] initWithFrame:NSMakeRect(0, 0, 100, 30)];
        responder.resign = &resign; [view addSubview:responder];
    }
    void InstallResponder()
    { resign = {}; Check([native makeFirstResponder:responder], "install native child responder"); }
    void Destroy()
    { auto* old = window; window = nullptr; if (old) jalium_window_destroy(old); }
    ~Fixture() { responder.resign = nullptr; Destroy(); }
};

struct NativeModal {
    NSWindow* parent;
    NSPanel* panel;
    NSModalSession session = nullptr;
    bool sheet, opened = false;
    NativeModal(NSWindow* owner, bool asSheet) : parent(owner), sheet(asSheet)
    {
        panel = [[NSPanel alloc] initWithContentRect:NSMakeRect(0, 0, 160, 80)
            styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];
    }
    void Open()
    {
        if (sheet) [parent beginSheet:panel completionHandler:nil];
        else session = [NSApp beginModalSessionForWindow:panel];
        opened = true;
    }
    bool OwnsInput() const { return sheet ? parent.attachedSheet == panel : NSApp.modalWindow == panel; }
    void Close()
    {
        if (!opened) return;
        if (sheet && parent.attachedSheet == panel) [parent endSheet:panel];
        if (session) { [NSApp endModalSession:session]; session = nullptr; }
        [panel orderOut:nil];
        NSDate* deadline = [NSDate dateWithTimeIntervalSinceNow:2];
        while (OwnsInput() && deadline.timeIntervalSinceNow > 0) {
            jalium_platform_poll_events();
            [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:.01]];
        }
        opened = false;
    }
    ~NativeModal() { @try { Close(); } @catch (NSException*) { } }
};

static int CheckReadinessCallbacks(int32_t(*focus)(JaliumPlatformWindow*))
{
    int failed = 0;
    for (bool sheet : {true, false}) for (int phase = 0; phase != 4; ++phase) {
        const char* boundary = phase == 0 ? "eligibility" : phase == 1 ? "responder" : phase == 2 ? "key" : "responder read";
        try {
            Fixture fixture(phase == 0, phase == 0); fixture.InstallResponder();
            NativeModal modal(fixture.native, sheet);
            struct ResetHook { ~ResetHook() { afterKey = {}; afterResponderRead = {}; observed = nil; } } reset;
            observed = fixture.native;
            bool injected = false; int keysAfterInjection = 0;
            auto inject = [&] {
                modal.Open(); injected = true; keysAfterInjection = keySelections;
            };
            if (phase == 0) fixture.query = inject;
            else if (phase == 1) fixture.resign = [&] { inject(); return true; };
            else if (phase == 2) afterKey = inject;
            else afterKey = [&] { afterResponderRead = inject; };
            int32_t result = focus(fixture.window);
            Check(injected && modal.OwnsInput(), "native modal callback probe actually owns input");
            Check(result != JALIUM_OK, "callback-created native modality rejects old focus request");
            Check(keySelections == keysAfterInjection, "new native modality prevents later parent key requests");
            if (phase == 0)
                Check(fixture.native.firstResponder == fixture.responder, "eligibility callback modality prevents responder acquisition");
            modal.Close();
            Check(focus(fixture.window) == JALIUM_OK, "fresh focus recovers after callback-created modality closes");
            std::printf("PASS readiness %s during %s callback\n", sheet ? "sheet" : "app modal", boundary);
        } catch (const std::exception& error) {
            afterKey = {}; afterResponderRead = {}; observed = nil; ++failed;
            std::fprintf(stderr, "FAIL readiness %s during %s callback: %s\n", sheet ? "sheet" : "app modal", boundary, error.what());
        }
    }
    std::printf("native focus readiness callbacks: %d/8 passed\n", 8 - failed);
    return failed;
}

int main()
{
    @autoreleasepool {
        if (jalium_platform_init() != JALIUM_OK) return 2;
        [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory]; [NSApp finishLaunching];
        // Observe/block activation only in this owned process; the desktop and
        // other applications are not manipulated by these contract checks.
        Method activate = class_getInstanceMethod(NSApplication.class, @selector(activate));
        Method ignoring = class_getInstanceMethod(NSApplication.class, @selector(activateIgnoringOtherApps:));
        Method order = class_getInstanceMethod(NSWindow.class, @selector(orderFront:));
        Method key = class_getInstanceMethod(NSWindow.class, @selector(makeKeyWindow));
        Method responder = class_getInstanceMethod(NSWindow.class, @selector(firstResponder));
        originalActivate = method_setImplementation(activate, (IMP)CountActivate);
        originalActivateIgnoring = method_setImplementation(ignoring, (IMP)CountActivateIgnoring);
        originalOrder = method_setImplementation(order, (IMP)CountOrder);
        originalKey = method_setImplementation(key, (IMP)CountKey);
        originalResponder = method_setImplementation(responder, (IMP)ReadResponder);
        int code = 0;
        try {
            using SetFocus = int32_t(*)(JaliumPlatformWindow*);
            auto focus = reinterpret_cast<SetFocus>(dlsym(RTLD_DEFAULT, "jalium_apple_window_set_keyboard_focus"));
            Check(focus != nullptr, "native keyboard focus entry point exists");
            Check(focus(nullptr) != JALIUM_OK, "null window rejected");
            for (bool transparent : {false, true}) {
                Fixture fixture(transparent); observed = fixture.native;
                fixture.InstallResponder(); int activationCount = activations, orderCount = orders;
                Check(focus(fixture.window) == JALIUM_OK, "acquire visible input surface");
                Check(fixture.native.firstResponder == fixture.view, "managed surface replaces child responder");
                Check(activations == activationCount && orders == orderCount, "focus neither activates app nor orders window");
                Check(fixture.native.ignoresMouseEvents == transparent, "focus preserves pointer policy");
                fixture.InstallResponder();
                Check(focus(fixture.window) == JALIUM_OK && fixture.native.firstResponder == fixture.view,
                    "repeated acquisition restores displaced responder");
                fixture.InstallResponder(); fixture.resign = [] { return false; };
                Check(focus(fixture.window) != JALIUM_OK, "responder refusal propagates");
                Check(fixture.native.firstResponder == fixture.responder, "refusal retains native child");
                Check(focus(fixture.window) == JALIUM_OK, "acquisition recovers after refusal");
                fixture.InstallResponder(); fixture.resign = []() -> bool {
                    @throw [NSException exceptionWithName:@"FocusProbe" reason:@"owned responder" userInfo:nil];
                };
                Check(focus(fixture.window) != JALIUM_OK, "responder exception is isolated");
                Check(focus(fixture.window) == JALIUM_OK, "acquisition recovers after exception");
                for (int mutation = 0; mutation < 4; ++mutation) {
                    fixture.InstallResponder(); fixture.resign = [&] {
                        if (mutation < 2) {
                            jalium_window_hide(fixture.window);
                            if (mutation == 1) jalium_apple_window_show(fixture.window, 0);
                        } else {
                            jalium_window_set_enabled(fixture.window, 0);
                            if (mutation == 3) jalium_window_set_enabled(fixture.window, 1);
                        }
                        return true;
                    };
                    Check(focus(fixture.window) != JALIUM_OK, "visibility/enable callback retires acquisition including ABA");
                    jalium_window_set_enabled(fixture.window, 1); jalium_apple_window_show(fixture.window, 0);
                    Check(focus(fixture.window) == JALIUM_OK, "fresh acquisition recovers after callback");
                }
                jalium_window_hide(fixture.window);
                Check(focus(fixture.window) != JALIUM_OK && !fixture.native.visible, "hidden focus does not show window");
                jalium_apple_window_show(fixture.window, 0); jalium_window_set_enabled(fixture.window, 0);
                Check(focus(fixture.window) != JALIUM_OK, "disabled window cannot acquire focus");
                jalium_window_set_enabled(fixture.window, 1);
                fixture.InstallResponder(); auto* before = fixture.native.firstResponder;
                int32_t foreign = JALIUM_OK;
                std::thread thread([&] { @autoreleasepool { foreign = focus(fixture.window); } }); thread.join();
                Check(foreign != JALIUM_OK && fixture.native.firstResponder == before, "foreign thread leaves responder unchanged");
                Fixture newer; newer.InstallResponder(); fixture.InstallResponder();
                fixture.resign = [&] { Check(focus(newer.window) == JALIUM_OK, "newer window acquires focus"); return true; };
                Check(focus(fixture.window) != JALIUM_OK, "newer cross-window acquisition retires old request");
                Check(newer.native.firstResponder == newer.view, "old request does not replace newer responder");
                fixture.InstallResponder(); fixture.resign = [&] { fixture.Destroy(); return true; };
                auto* retired = fixture.window;
                Check(focus(retired) != JALIUM_OK, "destroy during responder callback is safe");
                Check(focus(retired) != JALIUM_OK, "retired platform handle rejected");
                observed = nil;
            }
            {
                Fixture focused, declined; focused.InstallResponder();
                Check(focus(focused.window) == JALIUM_OK, "initial surface ready before refused cross-window focus");
                declined.InstallResponder(); declined.resign = [] { return false; }; observed = declined.native;
                int before = keySelections;
                Check(focus(declined.window) != JALIUM_OK, "cross-window responder refusal propagates");
                Check(keySelections == before, "declined responder must not request key selection");
                observed = nil;
            }
            {
                Fixture fixture, other;
                [other.native setContentView:fixture.view]; observed = fixture.native;
                int before = keySelections;
                Check(focus(fixture.window) != JALIUM_OK, "reparented input view rejects its obsolete physical window");
                Check(keySelections == before, "obsolete view ownership cannot select key window");
                observed = nil;
            }
            {
                Fixture fixture(true); fixture.query = [&] { fixture.Destroy(); };
                auto* retired = fixture.window;
                Check(focus(retired) != JALIUM_OK, "destroy inside key-capability query is safe");
            }
            {
                Fixture fixture(false, false); observed = fixture.native;
                NSPanel* sheet = [[NSPanel alloc] initWithContentRect:NSMakeRect(0, 0, 160, 80)
                    styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];
                [fixture.native beginSheet:sheet completionHandler:nil];
                Check(fixture.native.attachedSheet == sheet, "owned native sheet attached");
                int activationCount = activations, orderCount = orders;
                Check(focus(fixture.window) != JALIUM_OK, "ordinary focus cannot bypass attached native sheet");
                Check(fixture.native.attachedSheet == sheet && activations == activationCount && orders == orderCount,
                    "rejected sheet focus leaves ownership and activation unchanged");
                [fixture.native endSheet:sheet]; [sheet orderOut:nil];
                NSDate* deadline = [NSDate dateWithTimeIntervalSinceNow:2];
                while (fixture.native.attachedSheet && deadline.timeIntervalSinceNow > 0) {
                    jalium_platform_poll_events();
                    [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:.01]];
                }
                Check(focus(fixture.window) == JALIUM_OK, "ordinary focus recovers after sheet closes");
                observed = nil;
            }
            if (CheckReadinessCallbacks(focus)) code = 1;
        } catch (const std::exception& error) {
            std::fprintf(stderr, "FAIL keyboard focus: %s\n", error.what()); code = 1;
        }
        observed = nil;
        method_setImplementation(activate, originalActivate);
        method_setImplementation(ignoring, originalActivateIgnoring);
        method_setImplementation(order, originalOrder);
        method_setImplementation(key, originalKey);
        method_setImplementation(responder, originalResponder);
        jalium_platform_shutdown();
        std::printf("keyboard focus: %d checks, result=%d\n", checks, code);
        return code;
    }
}
