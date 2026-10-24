#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include "jalium_platform.h"
#include <cstdio>
#include <functional>
#include <memory>
#include <stdexcept>
#include <string>

// Own-process SDK contracts. These windows never request activation, and the
// getter/child-window hooks are scoped to retained fixture objects only.
static void Require(bool condition, const char* message)
{
    if (!condition) throw std::runtime_error(message);
}
static bool Pump(const std::function<bool()>& predicate)
{
    NSDate* deadline = [NSDate dateWithTimeIntervalSinceNow:1.0];
    do {
        jalium_platform_poll_events();
        [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.005]];
        if (predicate()) return true;
    } while (deadline.timeIntervalSinceNow > 0);
    return predicate();
}
struct Window {
    JaliumPlatformWindow* value;
    NSView* view;
    NSWindow* native;
    explicit Window(uint32_t style)
    {
        JaliumWindowParams p{};
        p.title = reinterpret_cast<const JaliumUtf16Char*>(u"Window owner callback contracts");
        p.x = 100; p.y = 120; p.width = 360; p.height = 220; p.style = style;
        value = jalium_window_create(&p);
        Require(value, "create native owner fixture");
        view = (__bridge NSView*)(void*)jalium_window_get_native_handle(value);
        native = view.window;
        Require(bool(native.styleMask & NSWindowStyleMaskTitled) == bool(style & JALIUM_WINDOW_STYLE_TITLEBAR), "fixture titlebar style matches its case");
    }
    intptr_t Handle(bool window = false) const { return (intptr_t)(__bridge void*)(window ? (id)native : (id)view); }
    void Close() { if (value) { auto* old = value; value = nullptr; jalium_window_destroy(old); } }
    ~Window() { Close(); }
};
struct OwnerHook;
static OwnerHook* g_hook;
static void ChildHook(id, SEL, NSWindow*);
static void AddHook(id, SEL, NSWindow*, NSWindowOrderingMode);
static id ObjectHook(id, SEL);
struct OwnerHook {
    id target;
    NSWindow* child;
    Method method;
    IMP original;
    bool after, fired = false;
    std::function<void()> action;
    OwnerHook(id object, NSWindow* surface, SEL selector, bool afterOriginal, std::function<void()> callback)
        : target(object), child(surface), after(afterOriginal), action(std::move(callback))
    {
        Class cls = object_getClass(object);
        Method inherited = class_getInstanceMethod(cls, selector);
        original = method_getImplementation(inherited);
        class_addMethod(cls, selector, original, method_getTypeEncoding(inherited));
        method = class_getInstanceMethod(cls, selector);
        g_hook = this;
        IMP replacement = reinterpret_cast<IMP>(ObjectHook);
        if (selector == @selector(removeChildWindow:)) replacement = reinterpret_cast<IMP>(ChildHook);
        if (selector == @selector(addChildWindow:ordered:)) replacement = reinterpret_cast<IMP>(AddHook);
        method_setImplementation(method, replacement);
    }
    void Fire(id object, NSWindow* surface = nil)
    {
        if (fired || object != target || (surface && surface != child)) return;
        fired = true;
        action();
    }
    ~OwnerHook() { method_setImplementation(method, original); g_hook = nullptr; }
};
static void ChildHook(id object, SEL selector, NSWindow* child)
{
    auto* hook = g_hook;
    if (!hook->after) hook->Fire(object, child);
    reinterpret_cast<void (*)(id, SEL, NSWindow*)>(hook->original)(object, selector, child);
    if (hook->after) hook->Fire(object, child);
}
static void AddHook(id object, SEL selector, NSWindow* child, NSWindowOrderingMode mode)
{
    auto* hook = g_hook;
    if (!hook->after) hook->Fire(object, child);
    reinterpret_cast<void (*)(id, SEL, NSWindow*, NSWindowOrderingMode)>(hook->original)(object, selector, child, mode);
    if (hook->after) hook->Fire(object, child);
}
static id ObjectHook(id object, SEL selector)
{
    auto* hook = g_hook;
    if (!hook->after) hook->Fire(object);
    id result = reinterpret_cast<id (*)(id, SEL)>(hook->original)(object, selector);
    if (hook->after) hook->Fire(object);
    return result;
}

int main()
{
    std::setvbuf(stdout, nullptr, _IOLBF, 0);
    @autoreleasepool {
        [NSApplication sharedApplication];
        [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
        Require(jalium_platform_init() == JALIUM_OK, "initialize platform");
        int passed = 0, total = 0;
        auto run = [&](const std::string& name, auto check) {
            ++total;
            try { check(); ++passed; std::printf("PASS %s\n", name.c_str()); }
            catch (const std::exception& error) { std::printf("FAIL %s: %s\n", name.c_str(), error.what()); }
        };
        constexpr uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
            JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE;
        constexpr uint32_t custom = (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS;
        for (uint32_t style : {regular, custom}) {
            std::string prefix = (style & JALIUM_WINDOW_STYLE_BORDERLESS) ? "custom " : "native ";
            for (int operation = 0; operation < 10; ++operation) for (int mutation = 0; mutation < 6; ++mutation) {
                std::string name = prefix + "owner callback " + std::to_string(operation) + "/" + std::to_string(mutation);
                run(name, [=] {
                    Window old(style), next(style), latest(style), child(style);
                    Require(jalium_window_set_owner(child.value, old.Handle()) == JALIUM_OK, "attach initial owner");
                    std::unique_ptr<Window> replacement;
                    bool inside = false;
                    id object = operation < 2 ? (id)old.native : operation < 4 ? (id)next.native :
                        operation < 6 ? (id)next.view : operation < 8 ? (id)child.native : (id)next.native;
                    SEL selector = operation < 2 ? @selector(removeChildWindow:) : operation < 4 ? @selector(addChildWindow:ordered:) :
                        operation < 6 ? @selector(window) : @selector(parentWindow);
                    OwnerHook hook(object, child.native, selector, (operation & 1) != 0, [&] {
                        inside = true;
                        if (mutation == 0 || mutation == 2) {
                            Require(jalium_window_set_owner(child.value, latest.Handle()) == JALIUM_OK, "queue latest owner");
                            if (mutation == 2) Require(jalium_window_set_owner(child.value, old.Handle(true)) == JALIUM_OK, "queue final owner");
                        } else if (mutation == 1) {
                            Require(jalium_window_set_owner(child.value, 0) == JALIUM_OK, "queue detach");
                        } else if (mutation == 3 || mutation == 5) {
                            next.Close();
                            if (mutation == 5) replacement = std::make_unique<Window>(style);
                        } else { child.Close(); replacement = std::make_unique<Window>(style); }
                        inside = false;
                    });
                    int32_t result = jalium_window_set_owner(child.value, next.Handle());
                    Require(hook.fired && !inside, "callback not exercised");
                    if (mutation < 3) {
                        Require(result == JALIUM_OK, "valid reparent result");
                        NSWindow* expected = mutation == 0 ? latest.native : mutation == 1 ? nil : old.native;
                        Require(Pump([&] { return child.native.parentWindow == expected; }), "stale native owner overwrote the latest request");
                        Require(![next.native.childWindows containsObject:child.native], "retired parent still contains the child");
                        Require(!expected || [expected.childWindows containsObject:child.native], "latest parent lost its child");
                    } else if (mutation == 3 || mutation == 5) {
                        Require(Pump([&] { return child.native.parentWindow == old.native; }), "closed target lost the original parent");
                        Require(![next.native.childWindows containsObject:child.native], "closed owner retained a live child");
                        if (replacement) Require(replacement->native.parentWindow == nil &&
                            ![replacement->native.childWindows containsObject:child.native], "retired target request changed its replacement");
                    } else {
                        Require(Pump([&] { return child.native.parentWindow == nil; }), "retired native child remained attached");
                        Require(replacement && replacement->native.parentWindow == nil, "retired request changed a replacement window");
                    }
                });
            }
            run(prefix + "ordinary owner and detach", [=] {
                Window owner(style), child(style);
                for (bool nativeHandle : {false, true}) {
                    Require(jalium_window_set_owner(child.value, owner.Handle(nativeHandle)) == JALIUM_OK &&
                        child.native.parentWindow == owner.native, "ordinary owner assignment");
                    Require(jalium_window_set_owner(owner.value, child.Handle()) != JALIUM_OK, "reject ownership cycle");
                    Require(jalium_window_set_owner(child.value, child.Handle(nativeHandle)) != JALIUM_OK, "reject self ownership");
                    Require(jalium_window_set_owner(child.value, 0) == JALIUM_OK && child.native.parentWindow == nil, "ordinary owner detach");
                }
            });
            run(prefix + "invalid nested owner keeps the valid outer request", [=] {
                Window owner(style), child(style);
                OwnerHook hook(owner.native, child.native, @selector(addChildWindow:ordered:), false, [&] {
                    Require(jalium_window_set_owner(child.value, child.Handle()) != JALIUM_OK, "nested self ownership must fail");
                });
                Require(jalium_window_set_owner(child.value, owner.Handle()) == JALIUM_OK &&
                    child.native.parentWindow == owner.native && hook.fired, "invalid request retired a valid assignment");
            });
            run(prefix + "closed owned objects cannot become parents", [=] {
                Window owner(style), child(style);
                owner.Close();
                for (bool nativeHandle : {false, true})
                    Require(jalium_window_set_owner(child.value, owner.Handle(nativeHandle)) != JALIUM_OK &&
                        child.native.parentWindow == nil, "closed AppKit owner was mistaken for an external window");
            });
            run(prefix + "retired previous-parent snapshot is never resurrected", [=] {
                Window old(style), next(style), child(style);
                Require(jalium_window_set_owner(child.value, old.Handle()) == JALIUM_OK, "initial parent");
                OwnerHook hook(child.native, child.native, @selector(parentWindow), true, [&] { old.Close(); next.Close(); });
                Require(jalium_window_set_owner(child.value, next.Handle()) != JALIUM_OK && hook.fired, "target loss is reported");
                Require(Pump([&] { return child.native.parentWindow == nil; }), "retired restore parent was resurrected");
            });
            for (bool nativeHandle : {false, true}) {
                run(prefix + "queued target closes " + std::to_string(nativeHandle), [=] {
                    Window old(style), next(style), latest(style), child(style);
                    Require(jalium_window_set_owner(child.value, old.Handle()) == JALIUM_OK, "initial parent");
                    OwnerHook hook(old.native, child.native, @selector(removeChildWindow:), true, [&] {
                        Require(jalium_window_set_owner(child.value, latest.Handle(nativeHandle)) == JALIUM_OK, "queue future owner");
                        latest.Close();
                    });
                    Require(jalium_window_set_owner(child.value, next.Handle()) == JALIUM_OK && hook.fired, "outer assignment");
                    Pump([] { return false; });
                    Require(child.native.parentWindow != latest.native, "a queued request attached to its retired owner");
                    Require(jalium_window_set_owner(child.value, old.Handle()) == JALIUM_OK &&
                        child.native.parentWindow == old.native, "expired queued target blocked later assignment");
                });
                run(prefix + "later direct assignment retires queued work " + std::to_string(nativeHandle), [=] {
                    Window old(style), next(style), latest(style), child(style);
                    Require(jalium_window_set_owner(child.value, old.Handle()) == JALIUM_OK, "initial parent");
                    OwnerHook hook(old.native, child.native, @selector(removeChildWindow:), true, [&] {
                        Require(jalium_window_set_owner(child.value, latest.Handle(nativeHandle)) == JALIUM_OK, "queue future owner");
                        jalium_platform_poll_events();
                    });
                    Require(jalium_window_set_owner(child.value, next.Handle()) == JALIUM_OK && hook.fired, "outer assignment");
                    Require(jalium_window_set_owner(child.value, old.Handle()) == JALIUM_OK, "new direct assignment");
                    Pump([] { return false; });
                    Require(child.native.parentWindow == old.native, "old continuation overrode a later direct assignment");
                });
                run(prefix + "nested cycle keeps the valid assignment " + std::to_string(nativeHandle), [=] {
                    Window owner(style), cyclic(style), child(style);
                    Require(jalium_window_set_owner(cyclic.value, child.Handle()) == JALIUM_OK, "prepare cycle");
                    OwnerHook hook(owner.native, child.native, @selector(addChildWindow:ordered:), false, [&] {
                        Require(jalium_window_set_owner(child.value, cyclic.Handle(nativeHandle)) != JALIUM_OK, "nested cycle must fail");
                    });
                    Require(jalium_window_set_owner(child.value, owner.Handle()) == JALIUM_OK && hook.fired &&
                        child.native.parentWindow == owner.native, "invalid cycle retired valid ownership");
                });
            }
        }
        jalium_platform_shutdown();
        std::printf("macOS Window owner SDK contracts: %d/%d passed\n", passed, total);
        return passed == total ? 0 : 1;
    }
}
