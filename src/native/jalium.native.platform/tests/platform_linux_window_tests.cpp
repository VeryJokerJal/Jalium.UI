#include "jalium_platform.h"
#include "jalium_api.h"
#include <X11/Xlib.h>
#include <X11/Xatom.h>
#include <X11/Xutil.h>
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <dlfcn.h>
#include <functional>
#include <string>
#include <thread>
#include <vector>
#include <sys/socket.h>
#include <sys/un.h>
#include <unistd.h>

namespace {
int failures = 0, checks = 0;
void Check(bool condition, const char* message)
{
    ++checks;
    if (!condition) { ++failures; std::fprintf(stderr, "FAILED: %s\n", message); }
}
bool Wait(const std::function<bool()>& predicate)
{
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
    do {
        jalium_platform_poll_events();
        if (predicate()) return true;
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    } while (std::chrono::steady_clock::now() < deadline);
    return false;
}
void Pump(int milliseconds = 80)
{
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(milliseconds);
    do { jalium_platform_poll_events(); std::this_thread::sleep_for(std::chrono::milliseconds(2)); }
    while (std::chrono::steady_clock::now() < deadline);
}
JaliumPlatformWindow* Create(uint32_t style = JALIUM_WINDOW_STYLE_BORDERLESS |
    JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_CLOSABLE |
    JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE)
{
    JaliumWindowParams params{};
    params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Jalium Linux Window contracts");
    params.x = 120; params.y = 100; params.width = 420; params.height = 280; params.style = style;
    return jalium_window_create(&params);
}
std::vector<unsigned long> Property(Display* display, Window window, const char* name, Atom type)
{
    unsigned char* bytes = nullptr;
    Atom actualType = None;
    int format = 0;
    unsigned long count = 0, remaining = 0;
    std::vector<unsigned long> values;
    if (XGetWindowProperty(display, window, XInternAtom(display, name, False),
        0, 4096, False, type, &actualType, &format, &count, &remaining, &bytes) == Success &&
        bytes && actualType == type && format == 32)
        values.assign(reinterpret_cast<unsigned long*>(bytes), reinterpret_cast<unsigned long*>(bytes) + count);
    if (bytes) XFree(bytes);
    return values;
}
bool HasState(Display* display, Window window, const char* name)
{
    const auto values = Property(display, window, "_NET_WM_STATE", XA_ATOM);
    return std::find(values.begin(), values.end(), XInternAtom(display, name, False)) != values.end();
}
void X11EdgeConstraintContracts()
{
    auto* window = Create();
    Check(window != nullptr, "create X11 edge-constraint window");
    if (!window) return;
    const auto surface = jalium_window_get_surface(window);
    auto* display = reinterpret_cast<Display*>(surface.handle0);
    const auto handle = static_cast<Window>(surface.handle1);
    const Atom property = XInternAtom(display, "_GTK_EDGE_CONSTRAINTS", False);
    struct Events { int constraints = 0; bool destroy = false; bool destroyed = false; } events;
    jalium_window_set_event_callback(window, [](const JaliumPlatformEvent* event, void* data) {
        auto& state = *static_cast<Events*>(data);
        if (event->type != JALIUM_EVENT_RESIZE_CONSTRAINTS_CHANGED) return;
        ++state.constraints;
        if (state.destroy)
        {
            state.destroyed = true;
            jalium_window_destroy(event->window);
        }
    }, &events);
    const auto set = [&](Window target, unsigned long mask) {
        XChangeProperty(display, target, property, XA_CARDINAL, 32, PropModeReplace,
            reinterpret_cast<const unsigned char*>(&mask), 1);
        XSync(display, False);
        Pump(25);
    };
    const auto restored = [&](int width, int height) {
        int32_t x = 0, y = 0, w = 0, h = 0;
        return jalium_linux_window_get_restore_bounds(window, &x, &y, &w, &h) == JALIUM_OK &&
            w == width && h == height;
    };
    jalium_window_show(window);
    Pump();
    Check(jalium_linux_window_get_resizable_edges(window) == 15 && !jalium_linux_window_is_tiled(window),
        "missing X11 edge hint retains floating unconstrained behavior");
    const Atom netState = XInternAtom(display, "_NET_WM_STATE", False);
    const Atom maxHorizontal = XInternAtom(display, "_NET_WM_STATE_MAXIMIZED_HORZ", False);
    const Atom maxVertical = XInternAtom(display, "_NET_WM_STATE_MAXIMIZED_VERT", False);
    for (const Atom axis : {maxHorizontal, maxVertical})
    {
        XChangeProperty(display, handle, netState, XA_ATOM, 32, PropModeReplace,
            reinterpret_cast<const unsigned char*>(&axis), 1);
        XSync(display, False);
        Pump();
        Check(jalium_linux_window_is_tiled(window) && jalium_linux_window_get_resizable_edges(window) == 15,
            "legacy single-axis maximization preserves WM layout without inventing edge restrictions");
    }
    const Atom bothAxes[] = {maxHorizontal, maxVertical};
    XChangeProperty(display, handle, netState, XA_ATOM, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(bothAxes), 2);
    XSync(display, False);
    Pump();
    Check(!jalium_linux_window_is_tiled(window) && jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MAXIMIZED,
        "legacy full maximization uses the existing maximized state");
    set(handle, 0xaa);
    XChangeProperty(display, handle, netState, XA_ATOM, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(&maxVertical), 1);
    XSync(display, False);
    Pump();
    Check(!jalium_linux_window_is_tiled(window), "an explicit GTK hint overrides legacy partial-maximize inference");
    XDeleteProperty(display, handle, property);
    XSync(display, False);
    Pump();
    Check(jalium_linux_window_is_tiled(window), "removing the GTK hint restores partial-maximize inference");
    XDeleteProperty(display, handle, netState);
    XSync(display, False);
    Pump();
    Check(!jalium_linux_window_is_tiled(window), "removing legacy layout atoms restores floating sizing");
    // GTK bits are top/right/bottom/left pairs: tiled then resizable.
    for (const auto [mask, edges] : std::vector<std::pair<unsigned long, uint32_t>>{
            {0xa9, 14}, {0xa6, 7}, {0x9a, 13}, {0x6a, 11}, {0xff, 15}, {0x55, 0}})
    {
        set(handle, mask);
        Check(jalium_linux_window_is_tiled(window) == 1, "X11 GTK tiled bits publish tiled state");
        Check(jalium_linux_window_get_resizable_edges(window) == edges,
            "X11 GTK resize pairs map to the correct physical edges");
    }
    const int duplicates = events.constraints;
    set(handle, 0x55);
    Check(events.constraints == duplicates, "duplicate X11 edge properties do not repeat notifications");
    Check(jalium_window_begin_resize_drag(window, JALIUM_RESIZE_EDGE_RIGHT) == JALIUM_ERROR_NOT_SUPPORTED,
        "X11 constrained edge rejects native resize before checking pointer input");
    set(handle, 0xff);
    const int tiledOnly = events.constraints;
    set(handle, 0xaa);
    Check(!jalium_linux_window_is_tiled(window) && events.constraints == tiledOnly + 1,
        "X11 untile notifies when all resize edges remain unchanged");
    set(handle, 0x100aa);
    Check(jalium_linux_window_get_resizable_edges(window) == 15 && !jalium_linux_window_is_tiled(window),
        "unknown future X11 constraint bits do not alter known edges");
    set(handle, 0x5d);
    XResizeWindow(display, handle, 600, 400);
    XSync(display, False);
    Pump();
    Check(restored(420, 280), "X11 tile resize preserves floating restore geometry");
    jalium_window_hide(window);
    Check(!jalium_linux_window_is_tiled(window) && jalium_linux_window_get_resizable_edges(window) == 15,
        "hidden X11 windows do not expose active tile constraints");
    jalium_window_resize(window, 600, 400);
    Check(restored(600, 400), "same-size hidden X11 request also replaces stale tile restore bounds");
    jalium_window_resize(window, 480, 320);
    Pump();
    Check(restored(480, 320), "explicit hidden X11 resize replaces the floating restore size");
    XDeleteProperty(display, handle, property);
    jalium_window_show(window);
    Pump();
    Check(!jalium_linux_window_is_tiled(window), "remapping clears a removed X11 tile hint");

    const Window frame = XCreateSimpleWindow(display, DefaultRootWindow(display), 0, 0, 900, 700, 0, 0, 0);
    bool frameDestroyed = false;
    const Window wrapper = XCreateSimpleWindow(display, frame, 0, 0, 800, 600, 0, 0, 0);
    XMapWindow(display, frame);
    XMapWindow(display, wrapper);
    XReparentWindow(display, handle, wrapper, 12, 24);
    jalium_window_show(window);
    Pump();
    set(frame, 0x5d);
    Check(jalium_linux_window_is_tiled(window) && jalium_linux_window_get_resizable_edges(window) == 8,
        "X11 native decoration frame publishes tile constraints without a client resize");
    set(handle, 0xaa);
    Check(!jalium_linux_window_is_tiled(window), "client constraint hint takes precedence over decoration ancestors");
    XDeleteProperty(display, handle, property);
    XSync(display, False);
    Pump();
    Check(jalium_linux_window_is_tiled(window), "removing a client hint falls back to the decoration frame");
    const unsigned long malformed[] = {0x55, 0xaa};
    XChangeProperty(display, frame, property, XA_CARDINAL, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(malformed), 2);
    XSync(display, False);
    Pump();
    Check(!jalium_linux_window_is_tiled(window) && jalium_linux_window_get_resizable_edges(window) == 15,
        "malformed X11 multi-value constraints fall back safely");
    const unsigned char invalid = 0x55;
    XChangeProperty(display, frame, property, XA_CARDINAL, 8, PropModeReplace, &invalid, 1);
    XSync(display, False);
    Pump();
    Check(jalium_linux_window_get_resizable_edges(window) == 15,
        "malformed X11 property width does not constrain a window");
    XChangeProperty(display, frame, property, XA_ATOM, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(malformed), 1);
    XSync(display, False);
    Pump();
    Check(!jalium_linux_window_is_tiled(window) && jalium_linux_window_get_resizable_edges(window) == 15,
        "wrong X11 property type does not constrain a window");
    XChangeProperty(display, frame, property, XA_CARDINAL, 32, PropModeReplace, nullptr, 0);
    XSync(display, False);
    Pump();
    Check(jalium_linux_window_get_resizable_edges(window) == 15,
        "empty X11 constraints do not read an absent value");
    XDeleteProperty(display, frame, property);
    const Window outer = XCreateSimpleWindow(display, DefaultRootWindow(display), 0, 0, 1000, 800, 0, 0, 0);
    XMapWindow(display, outer);
    XReparentWindow(display, frame, outer, 0, 0);
    XSync(display, False);
    Pump();
    set(outer, 0x5d);
    Check(jalium_linux_window_is_tiled(window),
        "reparenting only a decoration ancestor refreshes its new constraint provider");
    XReparentWindow(display, frame, DefaultRootWindow(display), 0, 0);
    XSync(display, False);
    Pump();
    set(outer, 0x55);
    Check(!jalium_linux_window_is_tiled(window), "detached outer frames stop supplying constraints");
    XReparentWindow(display, handle, DefaultRootWindow(display), 120, 100);
    jalium_window_show(window);
    Pump();
    set(frame, 0x55);
    Check(!jalium_linux_window_is_tiled(window), "old decoration frames stop supplying constraints after reparent");
    XReparentWindow(display, handle, wrapper, 12, 24);
    jalium_window_show(window);
    Pump();
    set(frame, 0x5d);
    const unsigned long floating = 0xaa;
    // Queue the property first, then remove its provider before Jalium reads
    // that event. This models a WM replacing decorations during withdrawal.
    XChangeProperty(display, frame, property, XA_CARDINAL, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(&floating), 1);
    XReparentWindow(display, handle, DefaultRootWindow(display), 120, 100);
    XDestroyWindow(display, frame);
    frameDestroyed = true;
    XSync(display, False);
    Pump();
    Check(!jalium_linux_window_is_tiled(window) && jalium_linux_window_get_resizable_edges(window) == 15,
        "queued X11 property events tolerate a decoration provider disappearing before dispatch");
    static int handlerErrors = 0;
    handlerErrors = 0;
    const auto handler = +[](Display*, XErrorEvent*) { ++handlerErrors; return 0; };
    const auto previousHandler = XSetErrorHandler(handler);
    const Window transientFrame = XCreateSimpleWindow(display, DefaultRootWindow(display), 0, 0, 900, 700, 0, 0, 0);
    XMapWindow(display, transientFrame);
    XReparentWindow(display, handle, transientFrame, 0, 0);
    jalium_window_show(window);
    Pump();
    set(transientFrame, 0x5d);
    XChangeProperty(display, transientFrame, property, XA_CARDINAL, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(&floating), 1);
    XReparentWindow(display, handle, DefaultRootWindow(display), 120, 100);
    XDestroyWindow(display, transientFrame);
    XSync(display, False);
    Pump();
    Check(handlerErrors == 0, "vanished-frame query errors do not leak to the caller's X11 error handler");
    // Queue an older, unrelated error before the next checked query starts.
    // It must retain the caller's handling instead of being swallowed.
    XDestroyWindow(display, transientFrame);
    jalium_window_show(window);
    XSync(display, False);
    Check(handlerErrors == 1, "checked X11 queries forward errors outside their own request range");
    Check(XSetErrorHandler(previousHandler) == handler, "checked X11 queries restore the caller's error handler");
    events.destroy = true;
    set(handle, 0x5d);
    Check(events.destroyed, "X11 constraint callback can destroy its window safely");
    if (!events.destroyed) jalium_window_destroy(window);
    if (!frameDestroyed) XDestroyWindow(display, frame);
    XDestroyWindow(display, outer);
    Pump();
}

void BasicContracts(bool wayland)
{
    auto* window = Create();
    Check(window != nullptr, "create contract window");
    if (!window) return;
    Check(jalium_linux_window_is_tiled(nullptr) == 0 && jalium_linux_window_is_tiled(window) == 0,
        "unmapped windows have no active tile state");
    Check(jalium_window_begin_move_drag(window) == JALIUM_ERROR_INVALID_STATE,
        "move requires an active press on this window");
    Check(jalium_window_begin_resize_drag(window, 3) == JALIUM_ERROR_INVALID_ARGUMENT,
        "invalid resize edge is rejected before reaching the compositor");
    Check(jalium_window_begin_resize_drag(window, JALIUM_RESIZE_EDGE_RIGHT) == JALIUM_ERROR_INVALID_STATE,
        "resize requires an active press on this window");
    Check(jalium_window_set_min_max_size(window, 400, 100, 200, 300) == JALIUM_ERROR_INVALID_ARGUMENT,
        "inverted size constraints are rejected");
    Check(jalium_window_set_min_max_size(window, -1, 0, 0, 0) == JALIUM_ERROR_INVALID_ARGUMENT,
        "negative size constraints are rejected");
    Check(jalium_window_set_min_max_size(window, 121, 81, 641, 481) == JALIUM_OK,
        "valid size constraints are accepted");
    int32_t x = 0, y = 0, width = 0, height = 0;
    Check(jalium_linux_window_get_restore_bounds(window, &x, &y, &width, &height) == JALIUM_OK &&
        width == 420 && height == 280, "normal restore bounds exist before first show");
    Check(jalium_linux_window_get_decoration_mode(window) == 1,
        "borderless window uses client decorations");
    Check(jalium_linux_window_set_application_id(window, "org.jalium.WindowContract") == JALIUM_OK,
        "desktop identity can be configured before mapping");
    Check(jalium_linux_window_set_application_id(window, "invalid id") == JALIUM_ERROR_INVALID_ARGUMENT &&
        jalium_linux_window_set_application_id(window, "") == JALIUM_ERROR_INVALID_ARGUMENT,
        "invalid desktop identities are rejected");
    if (!wayland)
    {
        const auto surface = jalium_window_get_surface(window);
        auto* display = reinterpret_cast<Display*>(surface.handle0);
        const auto handle = static_cast<Window>(surface.handle1);
        XClassHint classHints{};
        const bool hasClass = XGetClassHint(display, handle, &classHints);
        Check(hasClass && classHints.res_class && std::strcmp(classHints.res_class, "org.jalium.WindowContract") == 0,
            "X11 WM_CLASS advertises the configured desktop identity");
        if (classHints.res_name) XFree(classHints.res_name);
        if (classHints.res_class) XFree(classHints.res_class);
        const char* resourceName = std::getenv("RESOURCE_NAME");
        const bool hadResourceName = resourceName != nullptr;
        const std::string originalResourceName = resourceName ? resourceName : "";
        setenv("RESOURCE_NAME", "jalium-test-instance", 1);
        JaliumWindowParams childParams{};
        childParams.title = reinterpret_cast<const JaliumUtf16Char*>(u"Identity child");
        childParams.width = 100; childParams.height = 80;
        childParams.style = JALIUM_WINDOW_STYLE_POPUP;
        childParams.parentHandle = static_cast<intptr_t>(handle);
        auto* child = jalium_window_create(&childParams);
        XClassHint childHints{};
        const bool hasChildClass = child && XGetClassHint(display,
            static_cast<Window>(jalium_window_get_native_handle(child)), &childHints);
        Check(hasChildClass && childHints.res_class && childHints.res_name &&
            std::strcmp(childHints.res_class, "org.jalium.WindowContract") == 0 &&
            std::strcmp(childHints.res_name, "jalium-test-instance") == 0,
            "X11 popups inherit application class and honor RESOURCE_NAME instance");
        if (childHints.res_name) XFree(childHints.res_name);
        if (childHints.res_class) XFree(childHints.res_class);
        if (child) jalium_window_destroy(child);
        if (hadResourceName) setenv("RESOURCE_NAME", originalResourceName.c_str(), 1);
        else unsetenv("RESOURCE_NAME");
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_MAXIMIZED);
        Check(HasState(display, handle, "_NET_WM_STATE_MAXIMIZED_HORZ") &&
            HasState(display, handle, "_NET_WM_STATE_MAXIMIZED_VERT"),
            "initial maximize is advertised before mapping");
        Check(jalium_window_set_topmost(window, 1) == JALIUM_OK &&
            HasState(display, handle, "_NET_WM_STATE_ABOVE") &&
            HasState(display, handle, "_NET_WM_STATE_MAXIMIZED_HORZ"),
            "initial topmost preserves other state hints");
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_FULLSCREEN);
        Check(HasState(display, handle, "_NET_WM_STATE_FULLSCREEN") &&
            !HasState(display, handle, "_NET_WM_STATE_MAXIMIZED_HORZ"),
            "fullscreen replaces initial maximized state");
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_NORMAL);
        Check(!HasState(display, handle, "_NET_WM_STATE_FULLSCREEN") &&
            HasState(display, handle, "_NET_WM_STATE_ABOVE"),
            "restore clears only window geometry state");
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_MINIMIZED);
        XWMHints* hints = XGetWMHints(display, handle);
        Check(hints && (hints->flags & StateHint) && hints->initial_state == IconicState,
            "initial minimize uses ICCCM StateHint");
        if (hints) XFree(hints);
        const uint32_t fixedStyle = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE;
        Check(jalium_linux_window_set_style(window, fixedStyle) == JALIUM_OK,
            "apply complete native style");
        const auto motif = Property(display, handle, "_MOTIF_WM_HINTS", XInternAtom(display, "_MOTIF_WM_HINTS", False));
        Check(motif.size() == 5 && motif[0] == 3 && motif[1] == (4 | 32) && motif[2] == 1,
            "native caption functions honor fixed size and disabled minimize/maximize");
    }
    jalium_window_destroy(window);
}

void X11KeyboardOperationContracts()
{
    void* module = dlopen("libjalium.native.platform.so", RTLD_NOW | RTLD_LOCAL);
    using Get = uint32_t (*)(JaliumPlatformWindow*);
    using Begin = int32_t (*)(JaliumPlatformWindow*, int32_t);
    auto get = reinterpret_cast<Get>(module ? dlsym(module, "jalium_linux_window_get_keyboard_operations") : nullptr);
    auto begin = reinterpret_cast<Begin>(module ? dlsym(module, "jalium_linux_window_begin_keyboard_move_resize") : nullptr);
    Check(get && begin, "keyboard window commands export capability and request entrypoints");
    if (!get || !begin) { if (module) dlclose(module); return; }
    Check(get(nullptr) == 0 && begin(nullptr, 0) == JALIUM_ERROR_INVALID_ARGUMENT,
        "keyboard operation null arguments are safe");
    auto* window = Create();
    Check(window != nullptr, "create X11 keyboard operation fixture");
    if (!window) { dlclose(module); return; }
    auto surface = jalium_window_get_surface(window);
    auto* display = reinterpret_cast<Display*>(surface.handle0);
    Window handle = static_cast<Window>(surface.handle1);
    Window root = DefaultRootWindow(display);
    const Atom supported = XInternAtom(display, "_NET_SUPPORTED", False);
    const Atom moveresize = XInternAtom(display, "_NET_WM_MOVERESIZE", False);
    const auto previous = Property(display, root, "_NET_SUPPORTED", XA_ATOM);
    auto* peer = XOpenDisplay(DisplayString(display));
    Check(peer != nullptr, "open independent EWMH keyboard command observer");
    if (!peer) { jalium_window_destroy(window); dlclose(module); return; }
    XSelectInput(peer, root, SubstructureNotifyMask);
    XSync(peer, False);
    XChangeProperty(display, root, supported, XA_ATOM, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(&moveresize), 1);
    Check(get(window) == 0 && begin(window, 0) == JALIUM_ERROR_INVALID_STATE,
        "hidden windows do not offer keyboard commands");
    jalium_window_show(window);
    Pump();
    Check(get(window) == 3, "EWMH-capable X11 window offers move and resize");
    struct FocusEvents { int gained = 0, lost = 0, resized = 0; bool user = false; } focusEvents;
    jalium_window_set_event_callback(window, [](const JaliumPlatformEvent* event, void* data) {
        auto& events = *static_cast<FocusEvents*>(data);
        if (event->type == JALIUM_EVENT_FOCUS_GAINED) ++events.gained;
        if (event->type == JALIUM_EVENT_FOCUS_LOST) ++events.lost;
        if (event->type == JALIUM_EVENT_RESIZE) { ++events.resized; events.user = event->resize.isUserInitiated != 0; }
    }, &focusEvents);
    XSetInputFocus(display, handle, RevertToParent, CurrentTime);
    Pump();
    focusEvents = {};
    auto key = [&](Window target, int type) {
        XEvent event{};
        event.xkey.type = type;
        event.xkey.display = display;
        event.xkey.window = target;
        event.xkey.root = root;
        event.xkey.same_screen = True;
        event.xkey.keycode = XKeysymToKeycode(display, 0x61);
        XSendEvent(display, target, False, type == KeyPress ? KeyPressMask : KeyReleaseMask, &event);
        XFlush(display);
        Pump();
    };
    key(handle, KeyPress);
    Check((jalium_input_get_key_state(0x41) & 0x8000) != 0, "track held key before an owned popup grab");
    auto* popup = Create(JALIUM_WINDOW_STYLE_BORDERLESS | JALIUM_WINDOW_STYLE_POPUP | JALIUM_WINDOW_STYLE_POPUP_GRAB);
    Check(popup != nullptr, "create owned keyboard-grabbing popup");
    if (popup)
    {
        jalium_window_set_owner(popup, static_cast<intptr_t>(handle));
        jalium_window_show(popup);
        Pump();
        Window focused = 0;
        int revert = 0;
        XGetInputFocus(display, &focused, &revert);
        Check(focused == handle && focusEvents.gained == 0 && focusEvents.lost == 0,
            "owned popup grab leaves logical owner focus and activation unchanged");
        Check((jalium_input_get_key_state(0x41) & 0x8000) != 0,
            "owned popup keyboard handoff preserves held key state");
        key(static_cast<Window>(jalium_window_get_native_handle(popup)), KeyRelease);
        Check((jalium_input_get_key_state(0x41) & 0x8000) == 0,
            "release delivered to the owned popup updates shared key state");
        Check(get(popup) == 0 && begin(popup, 0) == JALIUM_ERROR_INVALID_STATE,
            "popup itself does not offer top-level keyboard commands");
        XResizeWindow(peer, handle, 430, 290);
        XSync(peer, False);
        Check(Wait([&] { return focusEvents.resized > 0; }) && !focusEvents.user,
            "application popup grab is not classified as a WM resize session");
        jalium_window_destroy(popup);
        Pump();
        Check(focusEvents.gained == 0 && focusEvents.lost == 0,
            "closing owned popup does not emit a duplicate owner activation");
    }
    focusEvents = {};
    Check(XGrabKeyboard(peer, root, False, GrabModeAsync, GrabModeAsync, CurrentTime) == GrabSuccess,
        "establish foreign WM-like keyboard grab");
    XSync(peer, False);
    Pump();
    XResizeWindow(peer, handle, 440, 300);
    XSync(peer, False);
    Check(Wait([&] { return focusEvents.resized > 0; }) && focusEvents.user,
        "foreign grab still identifies WM interactive resize");
    Check(focusEvents.gained == 0 && focusEvents.lost == 0,
        "WM grab does not deactivate the logically focused application");
    XUngrabKeyboard(peer, CurrentTime);
    XSync(peer, False);
    Pump();
    focusEvents.resized = 0;
    XResizeWindow(peer, handle, 450, 310);
    XSync(peer, False);
    Check(Wait([&] { return focusEvents.resized > 0; }) && !focusEvents.user,
        "WM ungrab ends interactive resize classification");
    Window other = XCreateSimpleWindow(peer, root, 10, 10, 40, 40, 0, 0, 0);
    XMapWindow(peer, other);
    XSetInputFocus(peer, other, RevertToParent, CurrentTime);
    XSync(peer, False);
    Pump();
    Check(focusEvents.lost == 1, "actual focus transfer still deactivates the owner once");
    XSetInputFocus(peer, handle, RevertToParent, CurrentTime);
    XSync(peer, False);
    Pump();
    Check(focusEvents.gained == 1, "actual focus return still activates the owner once");
    XDestroyWindow(peer, other);
    Check(begin(window, 2) == JALIUM_ERROR_INVALID_ARGUMENT, "invalid keyboard command is rejected");
    for (int resize : {0, 1})
    {
        if (!resize)
        {
            Check(XGrabKeyboard(display, handle, False, GrabModeAsync, GrabModeAsync, CurrentTime) == GrabSuccess &&
                XGrabPointer(display, handle, False, ButtonPressMask, GrabModeAsync, GrabModeAsync,
                    None, None, CurrentTime) == GrabSuccess, "establish application grabs before handoff");
        }
        Check(begin(window, resize) == JALIUM_OK, "keyboard command does not require a pointer press");
        XEvent message{};
        Check(Wait([&] {
            while (XCheckTypedEvent(peer, ClientMessage, &message))
                if (message.xclient.message_type == moveresize && message.xclient.window == handle) return true;
            return false;
        }) && message.xclient.format == 32 && message.xclient.data.l[0] == 0 &&
            message.xclient.data.l[1] == 0 && message.xclient.data.l[2] == (resize ? 9 : 10) &&
            message.xclient.data.l[3] == 0 && message.xclient.data.l[4] == 1,
            "EWMH keyboard command has correct direction, unused fields and source");
        if (!resize)
        {
            Check(XGrabKeyboard(peer, handle, False, GrabModeAsync, GrabModeAsync, CurrentTime) == GrabSuccess &&
                XGrabPointer(peer, handle, False, ButtonPressMask, GrabModeAsync, GrabModeAsync,
                    None, None, CurrentTime) == GrabSuccess, "keyboard handoff releases both application grabs");
            XUngrabPointer(peer, CurrentTime);
            XUngrabKeyboard(peer, CurrentTime);
            XSync(peer, False);
        }
    }
    jalium_window_set_resizable(window, 0);
    Check(get(window) == 1 && begin(window, 1) == JALIUM_ERROR_NOT_SUPPORTED,
        "fixed window retains move but declines keyboard resize");
    jalium_window_set_resizable(window, 1);
    const unsigned long tiled = 0x55;
    const Atom constraints = XInternAtom(display, "_GTK_EDGE_CONSTRAINTS", False);
    XChangeProperty(display, handle, constraints, XA_CARDINAL, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(&tiled), 1);
    Pump();
    Check(get(window) == 1, "fully constrained tile declines keyboard resize");
    XDeleteProperty(display, handle, constraints);
    const Atom allowed = XInternAtom(display, "_NET_WM_ALLOWED_ACTIONS", False);
    XChangeProperty(display, handle, allowed, XA_ATOM, 32, PropModeReplace, nullptr, 0);
    Pump();
    Check(get(window) == 0 && begin(window, 0) == JALIUM_ERROR_NOT_SUPPORTED,
        "WM allowed actions constrain keyboard commands");
    XDeleteProperty(display, handle, allowed);
    Pump();
    const Atom state = XInternAtom(display, "_NET_WM_STATE", False);
    for (const char* name : {"_NET_WM_STATE_MAXIMIZED_VERT", "_NET_WM_STATE_FULLSCREEN"})
    {
        const Atom atoms[] = {XInternAtom(display, name, False),
            XInternAtom(display, "_NET_WM_STATE_MAXIMIZED_HORZ", False)};
        XChangeProperty(display, handle, state, XA_ATOM, 32, PropModeReplace,
            reinterpret_cast<const unsigned char*>(atoms), 2);
        Pump();
        Check(get(window) == 0 && begin(window, 0) == JALIUM_ERROR_INVALID_STATE,
            "large window states decline keyboard geometry commands");
    }
    XDeleteProperty(display, handle, state);
    Pump();
    jalium_window_set_enabled(window, 0);
    Check(get(window) == 0 && begin(window, 0) == JALIUM_ERROR_INVALID_STATE,
        "disabled window declines keyboard commands");
    jalium_window_set_enabled(window, 1);
    XChangeProperty(display, root, supported, XA_CARDINAL, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(&moveresize), 1);
    Check(get(window) == 0, "malformed EWMH support hint does not advertise keyboard commands");
    XDeleteProperty(display, root, supported);
    Check(get(window) == 0 && begin(window, 0) == JALIUM_ERROR_NOT_SUPPORTED,
        "absence of WM support does not report a successful keyboard handoff");
    XChangeProperty(display, root, supported, XA_ATOM, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(previous.data()), static_cast<int>(previous.size()));
    if (previous.empty()) XDeleteProperty(display, root, supported);
    jalium_window_destroy(window);
    XCloseDisplay(peer);
    dlclose(module);
    Pump();
}

void X11ResizeOriginContracts()
{
    auto* window = Create();
    Check(window != nullptr, "create X11 resize-origin fixture");
    if (!window) return;
    const auto surface = jalium_window_get_surface(window);
    auto* display = reinterpret_cast<Display*>(surface.handle0);
    const Window handle = static_cast<Window>(surface.handle1);
    Display* peer = XOpenDisplay(DisplayString(display));
    Check(peer != nullptr, "open independent X11 resize sender");
    if (!peer) { jalium_window_destroy(window); return; }
    struct Events { std::vector<bool> user; bool close = false, closed = false; } events;
    jalium_window_set_event_callback(window, [](const JaliumPlatformEvent* event, void* data) {
        auto& events = *static_cast<Events*>(data);
        if (event->type != JALIUM_EVENT_RESIZE) return;
        events.user.push_back(event->resize.isUserInitiated != 0);
        if (events.close) { events.closed = true; jalium_window_destroy(event->window); }
    }, &events);
    jalium_window_show(window);
    Pump();
    XSetInputFocus(peer, handle, RevertToParent, CurrentTime);
    XSync(peer, False);
    Pump();
    auto focus = [&](int type, int mode) {
        XEvent event{};
        event.xfocus.type = type;
        event.xfocus.display = peer;
        event.xfocus.window = handle;
        event.xfocus.mode = mode;
        event.xfocus.detail = NotifyNonlinear;
        XSendEvent(peer, handle, False, FocusChangeMask, &event);
        XSync(peer, False);
        Pump();
    };
    auto resize = [&](int width, int height, bool user, const char* message, bool application = false) {
        const size_t before = events.user.size();
        if (application) jalium_window_resize(window, width, height);
        else { XResizeWindow(peer, handle, width, height); XSync(peer, False); }
        Check(Wait([&] { return events.user.size() > before; }) && events.user.back() == user, message);
    };
    resize(430, 290, false, "ordinary configure is not classified as interactive");
    focus(FocusOut, NotifyGrab);
    resize(440, 300, true, "WM grab resize is classified as interactive");
    resize(450, 310, false, "application resize during WM grab stays programmatic", true);
    resize(460, 320, true, "application resize does not end the WM operation");
    const size_t queued = events.user.size();
    jalium_window_resize(window, 470, 330);
    jalium_window_resize(window, 480, 340);
    Check(Wait([&] { return events.user.size() >= queued + 2; }) &&
        !events.user[queued] && !events.user[queued + 1], "queued application resizes retain their origin");
    jalium_window_resize(window, 480, 340);
    Pump();
    resize(490, 350, true, "same-size application request does not end WM operation");
    resize(480, 340, true, "same-size application request cannot hide a later user resize");
    focus(FocusIn, NotifyUngrab);
    resize(500, 360, false, "ungrab clears the interactive session");
    focus(FocusOut, NotifyGrab);
    const unsigned long tiled = 0xab;
    const Atom constraints = XInternAtom(peer, "_GTK_EDGE_CONSTRAINTS", False);
    XChangeProperty(peer, handle, constraints, XA_CARDINAL, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(&tiled), 1);
    XSync(peer, False);
    Pump();
    resize(510, 370, false, "entering a tile is a WM layout change");
    resize(520, 380, true, "resizing an existing tile remains interactive");
    XDeleteProperty(peer, handle, constraints);
    XSync(peer, False);
    Pump();
    resize(530, 390, false, "leaving a tile is a WM layout change");
    const Atom maxAtoms[] = {XInternAtom(peer, "_NET_WM_STATE_MAXIMIZED_HORZ", False),
        XInternAtom(peer, "_NET_WM_STATE_MAXIMIZED_VERT", False)};
    const Atom state = XInternAtom(peer, "_NET_WM_STATE", False);
    XChangeProperty(peer, handle, state, XA_ATOM, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(maxAtoms), 2);
    XSync(peer, False);
    Pump();
    resize(540, 400, false, "maximizing during a grab is a WM layout change");
    XDeleteProperty(peer, handle, state);
    XSync(peer, False);
    Pump();
    resize(550, 410, false, "restore configure is a WM layout change");
    focus(FocusOut, NotifyNormal);
    resize(560, 420, false, "ordinary focus loss clears the interactive session");
    focus(FocusOut, NotifyGrab);
    jalium_window_hide(window);
    Pump();
    jalium_window_show(window);
    Pump();
    resize(570, 430, false, "hide clears an interrupted WM operation");
    focus(FocusIn, NotifyNormal);
    auto button = [&](int type) {
        XEvent event{};
        event.xbutton.type = type;
        event.xbutton.display = peer;
        event.xbutton.window = handle;
        event.xbutton.root = DefaultRootWindow(peer);
        event.xbutton.button = Button1;
        event.xbutton.same_screen = True;
        XSendEvent(peer, handle, False, type == ButtonPress ? ButtonPressMask : ButtonReleaseMask, &event);
        XSync(peer, False);
        Pump();
    };
    button(ButtonPress);
    Check(jalium_window_begin_resize_drag(window, JALIUM_RESIZE_EDGE_RIGHT) == JALIUM_OK,
        "send explicit resize request before the WM takes the grab");
    resize(573, 433, true, "explicit CSD resize is interactive before WM focus grab");
    button(ButtonRelease);
    resize(574, 434, false, "release clears explicit resize if WM never takes the grab");
    focus(FocusOut, NotifyGrab);
    events.close = true;
    resize(580, 440, true, "user resize callback can synchronously destroy its window");
    Check(events.closed, "resize-origin callback lifetime completed");
    XCloseDisplay(peer);
    if (!events.closed) jalium_window_destroy(window);
    Pump();
}

void X11WindowManagerContracts(bool legacy)
{
    auto* window = Create();
    Check(window != nullptr, "create WM integration window");
    if (!window) return;
    const auto surface = jalium_window_get_surface(window);
    auto* display = reinterpret_cast<Display*>(surface.handle0);
    const auto handle = static_cast<Window>(surface.handle1);
    Check(!Property(display, DefaultRootWindow(display), "_NET_SUPPORTING_WM_CHECK", XA_WINDOW).empty(),
        "integration has a real EWMH window manager");
    jalium_window_show(window);
    Check(Wait([&] { XWindowAttributes a{}; XGetWindowAttributes(display, handle, &a); return a.map_state == IsViewable; }),
        "WM maps window");
    Pump();
    Check(jalium_linux_window_set_application_id(window, "org.jalium.Other") == JALIUM_ERROR_INVALID_STATE,
        "mapped X11 identity changes are rejected");
    Check((jalium_linux_window_get_capabilities(window) & JALIUM_WINDOW_CAPABILITY_RESIZE) != 0,
        "real WM allowed-actions advertise interactive resize");
    int normalWidth = 0, normalHeight = 0;
    jalium_window_get_client_size(window, &normalWidth, &normalHeight);
    for (JaliumWindowState state : {JALIUM_WINDOW_STATE_MAXIMIZED, JALIUM_WINDOW_STATE_FULLSCREEN,
        JALIUM_WINDOW_STATE_MAXIMIZED, JALIUM_WINDOW_STATE_NORMAL})
    {
        jalium_window_set_state(window, state);
        Check(Wait([&] { return jalium_window_get_state(window) == state; }),
            "WM acknowledges maximize/fullscreen/restore transition");
    }
    Check(Wait([&] { int w = 0, h = 0; jalium_window_get_client_size(window, &w, &h);
        return w == normalWidth && h == normalHeight; }), "WM restores normal client dimensions");
    for (int iteration = 0; iteration < 2; ++iteration)
    {
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_MINIMIZED);
        Check(Wait([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MINIMIZED; }),
            "WM minimizes window");
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_NORMAL);
        Check(Wait([&] { XWindowAttributes a{}; XGetWindowAttributes(display, handle, &a);
            return a.map_state == IsViewable && jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL; }),
            "restoring a minimized window maps it again");
    }
    if (!legacy)
    {
        for (JaliumWindowState target : {JALIUM_WINDOW_STATE_MAXIMIZED, JALIUM_WINDOW_STATE_FULLSCREEN})
        {
            jalium_window_set_state(window, JALIUM_WINDOW_STATE_MINIMIZED);
            Check(Wait([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MINIMIZED; }),
                "WM minimizes before restoring directly into another state");
            jalium_window_set_state(window, target);
            Check(Wait([&] { XWindowAttributes a{}; XGetWindowAttributes(display, handle, &a);
                return a.map_state == IsViewable && jalium_window_get_state(window) == target; }),
                "minimized X11 window restores directly to maximized or fullscreen");
            jalium_window_set_state(window, JALIUM_WINDOW_STATE_NORMAL);
            Check(Wait([&] { int w = 0, h = 0; jalium_window_get_client_size(window, &w, &h);
                return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL &&
                    w == normalWidth && h == normalHeight; }),
                "direct state restore retains the original floating geometry");
            jalium_window_set_state(window, target);
            Check(Wait([&] { return jalium_window_get_state(window) == target; }),
                "WM enters a large state before minimizing");
            jalium_window_set_state(window, JALIUM_WINDOW_STATE_MINIMIZED);
            Check(Wait([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MINIMIZED; }),
                "WM minimizes a maximized or fullscreen window");
            jalium_window_set_state(window, JALIUM_WINDOW_STATE_NORMAL);
            Check(Wait([&] { XWindowAttributes a{}; XGetWindowAttributes(display, handle, &a);
                int w = 0, h = 0; jalium_window_get_client_size(window, &w, &h);
                return a.map_state == IsViewable && jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL &&
                    w == normalWidth && h == normalHeight; }),
                "restoring a minimized large-state X11 window clears that state and maps its frame");
        }
        Check(jalium_window_set_resizable(window, 0) == JALIUM_OK, "disable WM interactive sizing");
        Check(Wait([&] { return !(jalium_linux_window_get_capabilities(window) & JALIUM_WINDOW_CAPABILITY_RESIZE); }),
            "WM allowed-actions update when interactive resize is disabled");
        jalium_window_resize(window, 470, 310);
        Check(Wait([&] { int w = 0, h = 0; jalium_window_get_client_size(window, &w, &h);
            return w == 470 && h == 310; }), "fixed window still accepts application-driven size changes");
        jalium_window_hide(window);
        Pump();
        jalium_window_set_state(window, JALIUM_WINDOW_STATE_MAXIMIZED);
        XWindowAttributes attributes{};
        XGetWindowAttributes(display, handle, &attributes);
        Check(attributes.map_state == IsUnmapped, "state changes do not show a hidden window");
        jalium_window_set_resizable(window, 1);
        jalium_window_show(window);
        Check(Wait([&] { return jalium_linux_window_get_capabilities(window) & JALIUM_WINDOW_CAPABILITY_RESIZE; }),
            "WM allowed-actions restore interactive resize");
        Check(Wait([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MAXIMIZED; }),
            "hide/show preserves requested state");
        (void)jalium_window_activate(window);
        Check(Wait([&] {
            const auto active = Property(display, DefaultRootWindow(display), "_NET_ACTIVE_WINDOW", XA_WINDOW);
            return !active.empty() && active[0] == handle;
        }), "WM activates the original window");
        auto* passive = Create();
        const auto passiveHandle = static_cast<Window>(jalium_window_get_native_handle(passive));
        jalium_linux_window_show(passive, 0);
        Check(Wait([&] { XWindowAttributes a{}; XGetWindowAttributes(display, passiveHandle, &a); return a.map_state == IsViewable; }),
            "non-activating show still maps the new window");
        Pump();
        const auto active = Property(display, DefaultRootWindow(display), "_NET_ACTIVE_WINDOW", XA_WINDOW);
        Check(!active.empty() && active[0] == handle, "ShowActivated false preserves WM focus");
        jalium_window_destroy(passive);

        // Model the desktop's Restore action without making a Jalium state
        // request. The native cache must follow externally confirmed state.
        XEvent restore{};
        restore.xclient.type = ClientMessage;
        restore.xclient.window = handle;
        restore.xclient.message_type = XInternAtom(display, "_NET_WM_STATE", False);
        restore.xclient.format = 32;
        restore.xclient.data.l[0] = 0;
        restore.xclient.data.l[1] = XInternAtom(display, "_NET_WM_STATE_MAXIMIZED_HORZ", False);
        restore.xclient.data.l[2] = XInternAtom(display, "_NET_WM_STATE_MAXIMIZED_VERT", False);
        restore.xclient.data.l[3] = 2;
        XSendEvent(display, DefaultRootWindow(display), False,
            SubstructureRedirectMask | SubstructureNotifyMask, &restore);
        XFlush(display);
        Check(Wait([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL; }),
            "external WM Restore updates native state");
        jalium_window_resize(window, 480, 330);
        Check(Wait([&] { int w = 0, h = 0; jalium_window_get_client_size(window, &w, &h); return w == 480 && h == 330; }),
            "normal dimensions can change after external Restore");
        jalium_window_hide(window);
        jalium_window_show(window);
        Check(Wait([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL; }),
            "hide/show keeps externally restored state");
        int32_t rx = 0, ry = 0, rw = 0, rh = 0;
        Check(jalium_linux_window_get_restore_bounds(window, &rx, &ry, &rw, &rh) == JALIUM_OK && rw == 480 && rh == 330,
            "restore bounds follow externally restored normal sizing");
    }
    jalium_window_destroy(window);
}

struct WaylandFrame {
    JaliumContext* context = nullptr;
    JaliumRenderTarget* target = nullptr;
    int paints = 0, states = 0, decorations = 0;
    int presentedFrames = 0;
    bool pendingPresent = false;
    int capabilities = 0;
    int renderingAvailability = 0;
    int resizeConstraints = 0;
    float pointerX = 0, pointerY = 0;
    bool destroyOnPaint = false, destroyed = false;
    bool destroyOnRenderingAvailability = false;
    bool destroyOnResizeConstraints = false;
};
void FrameCallback(const JaliumPlatformEvent* event, void* data)
{
    auto& frame = *static_cast<WaylandFrame*>(data);
    if (event->type == JALIUM_EVENT_STATE_CHANGED) ++frame.states;
    if (event->type == JALIUM_EVENT_DECORATIONS_CHANGED) ++frame.decorations;
    if (event->type == JALIUM_EVENT_CAPABILITIES_CHANGED) ++frame.capabilities;
    if (event->type == JALIUM_EVENT_RENDERING_AVAILABILITY_CHANGED) ++frame.renderingAvailability;
    if (event->type == JALIUM_EVENT_RESIZE_CONSTRAINTS_CHANGED) ++frame.resizeConstraints;
    if ((event->type == JALIUM_EVENT_RENDERING_AVAILABILITY_CHANGED && frame.destroyOnRenderingAvailability) ||
        (event->type == JALIUM_EVENT_RESIZE_CONSTRAINTS_CHANGED && frame.destroyOnResizeConstraints))
    {
        frame.destroyed = true;
        jalium_window_destroy(event->window);
        return;
    }
    if (event->type == JALIUM_EVENT_POINTER_DOWN)
    {
        frame.pointerX = event->pointer.x;
        frame.pointerY = event->pointer.y;
    }
    if (event->type != JALIUM_EVENT_PAINT) return;
    ++frame.paints;
    if (frame.destroyOnPaint)
    {
        frame.destroyed = true;
        jalium_window_destroy(event->window);
        return;
    }
    int32_t width = 0, height = 0;
    jalium_window_get_client_size(event->window, &width, &height);
    if (!frame.target)
    {
        const auto surface = jalium_window_get_surface(event->window);
        frame.target = jalium_render_target_create_for_surface(frame.context, &surface, width, height);
    }
    else jalium_render_target_resize(frame.target, width, height);
    if (frame.target && jalium_render_target_begin_draw(frame.target) == JALIUM_OK)
    {
        jalium_render_target_clear(frame.target, 0.06f, 0.18f, 0.12f, 1.0f);
        const auto result = jalium_render_target_end_draw(frame.target);
        if (result == JALIUM_ERROR_PRESENT_FAILED)
        {
            // The finite wl_shm pool can be busy during a configure burst.
            // Match the host's retry contract without polling reentrantly
            // inside a native callback. Fixture teardown waits for recovery.
            if (!frame.pendingPresent)
                std::fprintf(stderr, "Deferred Wayland present: ready=%d suspended=%d client=%dx%d\n",
                    jalium_wayland_surface_is_ready(jalium_window_get_native_handle(event->window)),
                    jalium_linux_window_is_rendering_suspended(event->window), width, height);
            frame.pendingPresent = true;
            jalium_window_invalidate(event->window);
            return;
        }
        frame.pendingPresent = false;
        if (result == JALIUM_OK) ++frame.presentedFrames;
        if (result != JALIUM_OK)
            std::fprintf(stderr, "Wayland present result=%d ready=%d suspended=%d client=%dx%d\n",
                static_cast<int>(result), jalium_wayland_surface_is_ready(jalium_window_get_native_handle(event->window)),
                jalium_linux_window_is_rendering_suspended(event->window), width, height);
        Check(result == JALIUM_OK, "present configured Wayland window");
    }
}
void WaylandClientChosenSizeContracts(JaliumPlatformWindow* window, float scale)
{
    const uint32_t active[] = {4};
    const auto physical = [scale](double logical) { return static_cast<int32_t>(std::lround(logical * scale)); };
    const auto choose = [&](int32_t seedWidth, int32_t seedHeight, const char* label,
                            const std::function<bool(int32_t, int32_t)>& expected) {
        // Coalesce a size suggestion and a subsequent client-choice configure.
        // Read the result before dispatching unrelated compositor events.
        jalium_test_wayland_inject_toplevel_configure(window, seedWidth, seedHeight, active, 1);
        jalium_test_wayland_inject_toplevel_configure(window, 0, 0, active, 1);
        jalium_test_wayland_apply_surface_configuration(window);
        int32_t width = 0, height = 0;
        jalium_window_get_client_size(window, &width, &height);
        Check(expected(width, height), label);
        Pump(30);
    };
    jalium_test_wayland_inject_configure_bounds(window, 0, 0);
    jalium_window_set_min_max_size(window, physical(450), physical(320), 0, 0);
    choose(200, 140, "unknown bounds still enforce explicit minimums for client-chosen sizes",
        [&](int32_t w, int32_t h) { return w >= physical(450) && h >= physical(320); });

    jalium_window_set_min_max_size(window, 0, 0, physical(320), physical(220));
    jalium_test_wayland_inject_configure_bounds(window, 1000, 1000);
    choose(800, 600, "explicit maximums take priority over larger recommended bounds",
        [&](int32_t w, int32_t h) { return w <= physical(320) && h <= physical(220); });
    jalium_test_wayland_inject_configure_bounds(window, 0, 0);
    choose(800, 600, "unknown bounds still enforce explicit maximums for client-chosen sizes",
        [&](int32_t w, int32_t h) { return w <= physical(320) && h <= physical(220); });
    jalium_test_wayland_inject_configure_bounds(window, 0, 160);
    choose(800, 600, "each axis combines its own explicit constraints and optional bounds",
        [&](int32_t w, int32_t h) { return w <= physical(320) && h <= physical(160); });

    jalium_window_set_min_max_size(window, 0, 0, physical(315), 0);
    jalium_test_wayland_inject_configure_bounds(window, 0, 0);
    choose(800, 600, "an unconstrained height remains independent of a bounded width",
        [&](int32_t w, int32_t h) { return w <= physical(315) && h == physical(600); });

    jalium_window_set_min_max_size(window, 257, 173, 257, 173);
    jalium_test_wayland_inject_configure_bounds(window, 1, 1);
    choose(800, 600, "sub-logical-pixel ranges use a valid range at the rounded minimum",
        [&](int32_t w, int32_t h) { return w == physical(std::ceil(257.0 / scale)) &&
            h == physical(std::ceil(173.0 / scale)); });
    jalium_test_wayland_inject_toplevel_configure(window, 80, 55, active, 1);
    jalium_test_wayland_apply_surface_configuration(window);
    int32_t width = 0, height = 0;
    jalium_window_get_client_size(window, &width, &height);
    Check(width == physical(80) && height == physical(55),
        "nonzero compositor dimensions remain authoritative even outside requested minimums");
    Pump(30);

    jalium_window_set_min_max_size(window, 0, 0, physical(320), physical(220));
    jalium_test_wayland_inject_configure_bounds(window, 0, 0);
    jalium_test_wayland_inject_toplevel_configure(window, 420, 280, active, 1);
    const uint32_t maximized[] = {1, 4};
    jalium_test_wayland_inject_toplevel_configure(window, 1000, 700, maximized, 2);
    jalium_test_wayland_inject_toplevel_configure(window, 0, 0, active, 1);
    jalium_test_wayland_apply_surface_configuration(window);
    jalium_window_get_client_size(window, &width, &height);
    Check(width <= physical(320) && height <= physical(220),
        "restore dimensions obey constraints changed since the saved normal size");
    Pump(30);

    jalium_window_set_min_max_size(window, 0, 0, 0, 0);
    jalium_test_wayland_inject_configure_bounds(window, 1000, 1000);
    jalium_test_wayland_inject_toplevel_configure(window, 420, 280, active, 1);
    jalium_window_set_resizable(window, 0);
    jalium_test_wayland_inject_toplevel_configure(window, 1000, 700, maximized, 2);
    jalium_test_wayland_inject_toplevel_configure(window, 0, 0, active, 1);
    jalium_test_wayland_apply_surface_configuration(window);
    jalium_window_get_client_size(window, &width, &height);
    Check(width == physical(420) && height == physical(280),
        "fixed windows restore their saved size rather than locking the maximized buffer size");
    Pump(30);
    jalium_window_set_resizable(window, 1);
    jalium_test_wayland_inject_configure_bounds(window, 0, 0);
    jalium_test_wayland_inject_toplevel_configure(window, 420, 280, active, 1);
    jalium_test_wayland_apply_surface_configuration(window);
    Pump();
}

void WaylandTiledRestoreContracts(JaliumContext* context)
{
    auto* window = Create();
    Check(window != nullptr, "create independent tiling geometry fixture");
    if (!window) return;
    WaylandFrame frame;
    frame.context = context;
    jalium_window_set_event_callback(window, FrameCallback, &frame);
    jalium_window_show(window);
    Check(Wait([&] { return frame.presentedFrames > 0; }), "map independent tiling geometry fixture");
    const float scale = jalium_window_get_dpi_scale(window);
    const uint32_t active[] = {4};
    const uint32_t tiledLeft[] = {4, 5, 7, 8};
    const uint32_t tiledRight[] = {4, 6, 7, 8};
    const uint32_t resizingTile[] = {3, 4, 5, 7, 8};
    const uint32_t maximized[] = {1, 4};
    const auto physical = [scale](int32_t logical) { return static_cast<int32_t>(std::lround(logical * scale)); };
    const auto configure = [&](int32_t width, int32_t height, const uint32_t* states, uint32_t count) {
        jalium_test_wayland_inject_toplevel_configure(window, width, height, states, count);
        jalium_test_wayland_apply_surface_configuration(window);
    };
    const auto hasRestoreSize = [&](int32_t width, int32_t height) {
        int32_t x = 0, y = 0, w = 0, h = 0;
        return jalium_linux_window_get_restore_bounds(window, &x, &y, &w, &h) == JALIUM_OK &&
            w == physical(width) && h == physical(height);
    };
    const auto hasClientSize = [&](int32_t width, int32_t height) {
        int32_t w = 0, h = 0;
        jalium_window_get_client_size(window, &w, &h);
        return w == physical(width) && h == physical(height);
    };

    jalium_window_set_min_max_size(window, 0, 0, 0, 0);
    jalium_test_wayland_inject_configure_bounds(window, 0, 0);
    configure(420, 280, active, 1);
    const int constraintEvents = frame.resizeConstraints;
    jalium_test_wayland_inject_toplevel_configure(window, 600, 700, tiledLeft, 4);
    Check(jalium_linux_window_is_tiled(window) == 0 && frame.resizeConstraints == constraintEvents,
        "tiled state waits for the paired surface configure acknowledgement");
    jalium_test_wayland_apply_surface_configuration(window);
    Check(jalium_linux_window_is_tiled(window) == 1 && frame.resizeConstraints == constraintEvents + 1,
        "acknowledged tiling publishes a constraint event even when all resize edges remain free");
    configure(600, 700, tiledLeft, 4);
    Check(frame.resizeConstraints == constraintEvents + 1, "duplicate tiled state does not publish a constraint event");
    Check(hasClientSize(600, 700) && jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL,
        "tiling accepts compositor geometry while remaining a Normal window");
    Check(hasRestoreSize(420, 280), "tiled geometry preserves the previous floating restore size");
    configure(650, 700, resizingTile, 5);
    Check(hasRestoreSize(420, 280), "interactive tile resizing does not replace floating restore geometry");
    configure(1000, 800, maximized, 2);
    Check(hasRestoreSize(420, 280), "maximizing a tile retains its floating restore geometry");
    configure(0, 0, active, 1);
    Check(hasClientSize(420, 280), "client-chosen restore after tile and maximize uses floating dimensions");

    configure(460, 300, active, 1);
    configure(600, 700, tiledLeft, 4);
    configure(600, 700, tiledRight, 4);
    configure(0, 350, active, 1);
    Check(hasClientSize(460, 350), "leaving tiling restores a zero axis and honors the compositor's other axis");
    Check(hasRestoreSize(460, 350), "accepted floating geometry becomes the next restore size");
    const int beforeSameSizeTile = frame.resizeConstraints;
    configure(460, 350, tiledLeft, 4);
    configure(460, 350, active, 1);
    Check(jalium_linux_window_is_tiled(window) == 0 && frame.resizeConstraints == beforeSameSizeTile + 2,
        "tile and untile notify layout when both dimensions and resize edges are unchanged");
    configure(600, 700, tiledLeft, 4);
    jalium_window_resize(window, physical(500), physical(450));
    Check(hasRestoreSize(460, 350), "programmatic sizing while tiled does not overwrite floating restore geometry");
    if (jalium_test_wayland_inject_fractional_scale(window, 240) == JALIUM_OK)
    {
        int32_t x = 0, y = 0, width = 0, height = 0;
        jalium_linux_window_get_restore_bounds(window, &x, &y, &width, &height);
        Check(width == std::lround(physical(460) * 2.0 / scale) &&
            height == std::lround(physical(350) * 2.0 / scale),
            "scaling a tile scales the floating restore size rather than saving tile dimensions");
        jalium_test_wayland_inject_fractional_scale(window, static_cast<uint32_t>(std::lround(scale * 120)));
        Check(hasRestoreSize(460, 350), "returning to the previous scale retains floating restore dimensions");
    }
    configure(0, 0, active, 1);
    Check(hasClientSize(460, 350), "untile after programmatic sizing returns to floating dimensions");

    configure(600, 700, tiledLeft, 4);
    jalium_window_hide(window);
    Check(jalium_linux_window_is_tiled(window) == 0, "hidden windows do not expose a removed role's tile state");
    const int beforeRemap = frame.presentedFrames;
    jalium_window_show(window);
    Check(Wait([&] { return frame.presentedFrames > beforeRemap && hasClientSize(460, 350); }),
        "a recreated role can select floating size without stale tile state");
    configure(600, 700, tiledLeft, 4);
    jalium_window_hide(window);
    jalium_window_resize(window, physical(512), physical(384));
    Check(hasRestoreSize(512, 384), "explicit sizing after hiding a tile updates its floating restore geometry");
    const int beforeResizedRemap = frame.presentedFrames;
    jalium_window_show(window);
    Check(Wait([&] { return frame.presentedFrames > beforeResizedRemap && hasClientSize(512, 384); }),
        "showing a previously tiled window preserves its new hidden size request");
    Check(Wait([&] { return !frame.pendingPresent; }), "deferred tiled-window presentation recovers before teardown");
    if (frame.target) jalium_render_target_destroy(frame.target);
    jalium_window_destroy(window);
    Pump();
}

void WaylandSystemMenuInputContracts(JaliumContext* context)
{
    struct MenuFrame {
        WaylandFrame frame;
        JaliumPlatformWindow* window = nullptr;
        JaliumPlatformWindow* other = nullptr;
        bool nested = false, destroy = false, destroyed = false;
        int32_t ownResult = -100, otherResult = -100, threadResult = -100;
        uint32_t before = 0, after = 0;
    } a, b;
    a.frame.context = b.frame.context = context;
    a.window = Create();
    b.window = Create();
    Check(a.window && b.window, "create independent Wayland window-menu input fixtures");
    if (!a.window || !b.window) return;
    a.other = b.window;
    b.other = a.window;
    Check(jalium_window_show_system_menu(a.window, 1, 2) != JALIUM_OK,
        "an unshown Wayland window cannot consume an unrelated input serial");
    const auto callback = [](const JaliumPlatformEvent* event, void* data) {
        auto& state = *static_cast<MenuFrame*>(data);
        FrameCallback(event, &state.frame);
        if (event->type != JALIUM_EVENT_POINTER_DOWN) return;
        if (state.destroy)
        {
            state.destroyed = true;
            jalium_window_destroy(state.window);
            Check(jalium_window_show_system_menu(state.window, 1, 2) == JALIUM_ERROR_INVALID_STATE,
                "window-menu request rejects a window destroyed by its input callback");
            return;
        }
        int32_t x = 0, y = 0;
        state.ownResult = jalium_window_show_system_menu(state.window, 11, 12);
        Check(state.ownResult == JALIUM_OK &&
            jalium_test_wayland_get_last_system_menu(state.window, &x, &y, &state.before) == JALIUM_OK &&
            x == 11 && y == 12 && state.before != 0,
            "Wayland menu uses its own active user-action serial");
        state.otherResult = jalium_window_show_system_menu(state.other, 21, 22);
        Check(state.otherResult == JALIUM_ERROR_NOT_SUPPORTED,
            "Wayland menu does not borrow another window's active input");
        std::thread background([&] {
            state.threadResult = jalium_window_show_system_menu(state.window, 31, 32);
        });
        background.join();
        Check(state.threadResult == JALIUM_ERROR_NOT_SUPPORTED,
            "Wayland input authorization does not escape to a background thread");
        if (state.nested)
        {
            jalium_test_wayland_inject_touch(state.other, JALIUM_EVENT_POINTER_DOWN, 4102, 10, 10);
            jalium_test_wayland_inject_touch(state.other, JALIUM_EVENT_POINTER_UP, 4102, 10, 10);
            Check(jalium_window_show_system_menu(state.window, 41, 42) == JALIUM_OK &&
                jalium_test_wayland_get_last_system_menu(state.window, &x, &y, &state.after) == JALIUM_OK &&
                state.after == state.before,
                "nested window input restores the outer window-menu serial");
        }
    };
    jalium_window_set_event_callback(a.window, callback, &a);
    jalium_window_set_event_callback(b.window, callback, &b);
    jalium_window_show(a.window);
    jalium_window_show(b.window);
    Check(Wait([&] { return a.frame.presentedFrames > 0 && b.frame.presentedFrames > 0; }),
        "map both Wayland window-menu input fixtures");
    a.nested = true;
    jalium_test_wayland_inject_touch(a.window, JALIUM_EVENT_POINTER_DOWN, 4101, 10, 10);
    Check(jalium_window_show_system_menu(a.window, 1, 2) == JALIUM_ERROR_NOT_SUPPORTED,
        "queued Wayland menu request cannot borrow a newer action from another window");
    jalium_test_wayland_inject_touch(a.window, JALIUM_EVENT_POINTER_UP, 4101, 10, 10);
    Check(jalium_window_show_system_menu(a.window, 1, 2) == JALIUM_OK,
        "a queued Wayland menu request retains its own latest user action");
    jalium_window_set_enabled(a.window, 0);
    Check(jalium_window_show_system_menu(a.window, 1, 2) == JALIUM_ERROR_INVALID_STATE,
        "disabled Wayland window cannot open a system menu");
    jalium_window_set_enabled(a.window, 1);
    Check(jalium_window_show_system_menu(a.window, 1, 2) == JALIUM_ERROR_NOT_SUPPORTED,
        "re-enabling a Wayland window does not revive old input authorization");
    jalium_test_wayland_inject_touch(b.window, JALIUM_EVENT_POINTER_DOWN, 4104, 10, 10);
    jalium_test_wayland_inject_touch(b.window, JALIUM_EVENT_POINTER_UP, 4104, 10, 10);
    Check(jalium_window_show_system_menu(b.window, 1, 2) == JALIUM_OK,
        "the other window can queue a menu from its own user action");
    jalium_window_hide(b.window);
    Check(jalium_window_show_system_menu(b.window, 1, 2) == JALIUM_ERROR_INVALID_STATE,
        "hidden Wayland window cannot open a system menu");
    const int beforeRemap = b.frame.presentedFrames;
    jalium_window_show(b.window);
    Check(Wait([&] { return b.frame.presentedFrames > beforeRemap; }),
        "remap window-menu input fixture");
    Check(jalium_window_show_system_menu(b.window, 1, 2) == JALIUM_ERROR_NOT_SUPPORTED,
        "remapping a Wayland role does not revive its old surface input");
    if (a.frame.target) jalium_render_target_destroy(a.frame.target);
    a.frame.target = nullptr;
    a.destroy = true;
    jalium_test_wayland_inject_touch(a.window, JALIUM_EVENT_POINTER_DOWN, 4103, 10, 10);
    Check(a.destroyed, "input callback can destroy its window before a menu request");
    if (!a.destroyed) jalium_window_destroy(a.window);
    if (b.frame.target) jalium_render_target_destroy(b.frame.target);
    jalium_window_destroy(b.window);
    Pump();
}

void WaylandInputDeviceContracts(JaliumContext* context)
{
    const uint32_t original = jalium_test_wayland_get_seat_capabilities();
    Check((original & 3u) == 3u, "input-device contracts require a real pointer and keyboard seat");
    if ((original & 3u) != 3u) return;
    struct InputFrame {
        WaylandFrame frame;
        int lost = 0, leaves = 0, repeats = 0, moves = 0, compositionEnds = 0, characters = 0;
        int modifiers = -1;
        bool destroyOnLoss = false, destroyOnLeave = false, destroyed = false;
        bool resetInKeyDown = false;
        JaliumPlatformWindow* restoreFocus = nullptr;
        uint32_t restoreCapabilities = 0;
        std::chrono::steady_clock::time_point lastFocus = std::chrono::steady_clock::now();
    } a, b;
    a.frame.context = b.frame.context = context;
    auto* first = Create();
    auto* second = Create();
    Check(first && second, "create Wayland input-device lifetime fixtures");
    if (!first || !second) return;
    const auto callback = [](const JaliumPlatformEvent* event, void* data) {
        auto& state = *static_cast<InputFrame*>(data);
        FrameCallback(event, &state.frame);
        if (event->type == JALIUM_EVENT_FOCUS_GAINED || event->type == JALIUM_EVENT_FOCUS_LOST)
            state.lastFocus = std::chrono::steady_clock::now();
        if (event->type == JALIUM_EVENT_KEY_DOWN && !event->key.isRepeat && state.resetInKeyDown)
        {
            state.resetInKeyDown = false;
            jalium_test_wayland_inject_seat_capabilities(state.restoreCapabilities & ~2u);
            jalium_test_wayland_inject_seat_capabilities(state.restoreCapabilities);
            jalium_test_wayland_inject_keyboard(event->window, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
            Check(jalium_window_show_system_menu(event->window, 1, 2) == JALIUM_ERROR_NOT_SUPPORTED,
                "nested device reset does not resurrect the outer input authorization");
        }
        if (event->type == JALIUM_EVENT_KEY_DOWN && event->key.isRepeat) ++state.repeats;
        if (event->type == JALIUM_EVENT_COMPOSITION_END) ++state.compositionEnds;
        if (event->type == JALIUM_EVENT_CHAR_INPUT) ++state.characters;
        if (event->type == JALIUM_EVENT_MOUSE_MOVE) { ++state.moves; state.modifiers = event->mouse.modifiers; }
        if (event->type == JALIUM_EVENT_MOUSE_LEAVE) ++state.leaves;
        if (event->type == JALIUM_EVENT_FOCUS_LOST) ++state.lost;
        if ((event->type == JALIUM_EVENT_FOCUS_LOST && state.destroyOnLoss) ||
            (event->type == JALIUM_EVENT_MOUSE_LEAVE && state.destroyOnLeave))
        {
            state.destroyed = true;
            jalium_window_destroy(event->window);
        }
        else if (event->type == JALIUM_EVENT_FOCUS_LOST && state.restoreFocus)
        {
            auto* next = state.restoreFocus;
            state.restoreFocus = nullptr;
            jalium_test_wayland_inject_seat_capabilities(state.restoreCapabilities);
            jalium_test_wayland_inject_keyboard(next, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
        }
    };
    jalium_window_set_event_callback(first, callback, &a);
    jalium_window_set_event_callback(second, callback, &b);
    jalium_window_show(first);
    jalium_window_show(second);
    Check(Wait([&] { return a.frame.presentedFrames && b.frame.presentedFrames; }),
        "map input-device lifetime fixtures");
    Check(Wait([&] {
        return std::chrono::steady_clock::now() - std::max(a.lastFocus, b.lastFocus) >=
            std::chrono::milliseconds(300);
    }), "compositor startup focus settles before controlled held-key input");
    Check(jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_FOCUS_GAINED, 0, 0) == JALIUM_OK,
        "focus the keyboard-loss fixture");
    Check(jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_KEY_DOWN, 29, 2) == JALIUM_OK &&
        jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_KEY_DOWN, 30, 2) == JALIUM_OK,
        "hold Control and a repeating key before keyboard removal");
    Check(Wait([&] { return a.repeats > 0; }), "real Wayland repeat timer starts before keyboard removal");
    Check((jalium_input_get_key_state(0x41) & 0x8000) != 0 &&
        (jalium_input_get_key_state(0x11) & 0x8000) != 0, "held keys are observable before keyboard removal");
    const int lost = a.lost, repeats = a.repeats;
    jalium_test_wayland_inject_seat_capabilities(original & ~2u);
    Check(a.lost == lost + 1, "keyboard capability removal publishes focus loss exactly once");
    Check((jalium_input_get_key_state(0x41) & 0x8000) == 0 &&
        (jalium_input_get_key_state(0x11) & 0x8000) == 0, "keyboard capability removal releases pressed key states");
    Pump(180);
    Check(a.repeats == repeats, "keyboard capability removal stops pending key repeat");
    Check(jalium_window_show_system_menu(first, 1, 2) == JALIUM_ERROR_NOT_SUPPORTED,
        "keyboard removal invalidates previously queued menu input");
    jalium_test_wayland_inject_mouse(first, JALIUM_EVENT_MOUSE_ENTER, 0, 10, 10);
    jalium_test_wayland_inject_mouse(first, JALIUM_EVENT_MOUSE_MOVE, 0, 12, 12);
    Check(a.modifiers == 0, "remaining pointer no longer inherits removed keyboard modifiers");
    jalium_test_wayland_inject_seat_capabilities(original & ~2u);
    Check(a.lost == lost + 1, "repeated missing-keyboard capabilities do not duplicate focus loss");
    jalium_test_wayland_inject_seat_capabilities(original);
    Pump();
    jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
    jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_KEY_DOWN, 30, 0);
    Check(Wait([&] { return a.repeats > repeats; }), "new keyboard proxy resumes normal repeat");
    jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_KEY_UP, 30, 0);
    jalium_test_wayland_inject_mouse(first, JALIUM_EVENT_MOUSE_ENTER, 0, 10, 10);
    jalium_test_wayland_inject_mouse(first, JALIUM_EVENT_MOUSE_DOWN, 0, 10, 10);
    const int leaves = a.leaves;
    jalium_test_wayland_inject_seat_capabilities(original & ~1u);
    Check(a.leaves == leaves + 1, "pointer capability removal cancels window hover and press state");
    Check(jalium_window_begin_move_drag(first) == JALIUM_ERROR_INVALID_STATE,
        "removed pointer cannot start moving with a stale press");
    Check(jalium_window_show_system_menu(first, 1, 2) == JALIUM_ERROR_NOT_SUPPORTED,
        "pointer removal invalidates previously queued menu input");
    jalium_test_wayland_inject_seat_capabilities(original);
    Pump();
    Check(jalium_window_begin_move_drag(first) == JALIUM_ERROR_INVALID_STATE,
        "recreated pointer does not revive the previous press");
    jalium_test_wayland_inject_mouse(first, JALIUM_EVENT_MOUSE_ENTER, 0, 10, 10);
    jalium_test_wayland_inject_mouse(first, JALIUM_EVENT_MOUSE_DOWN, 0, 10, 10);
    Check(jalium_window_show_system_menu(first, 1, 2) == JALIUM_OK,
        "fresh pointer input can request a window menu after reattachment");
    jalium_test_wayland_inject_mouse(first, JALIUM_EVENT_MOUSE_UP, 0, 10, 10);
    jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
    Check(jalium_test_wayland_inject_preedit(first, "拼音未提交") == JALIUM_OK,
        "start uncommitted Wayland composition before keyboard removal");
    const int compositionEnds = a.compositionEnds, characters = a.characters;
    jalium_test_wayland_inject_seat_capabilities(original & ~2u);
    Check(a.compositionEnds == compositionEnds + 1,
        "keyboard removal cancels composition instead of leaving stale preedit");
    jalium_test_wayland_inject_seat_capabilities(original);
    Pump();
    jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
    jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_KEY_DOWN, 30, 0);
    Check(a.characters > characters, "keyboard reattachment resumes character input after canceled composition");
    jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_KEY_UP, 30, 0);
    const int beforeNestedReset = a.characters;
    a.resetInKeyDown = true;
    a.restoreCapabilities = original;
    jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_KEY_DOWN, 30, 0);
    Check(a.characters == beforeNestedReset,
        "keyboard loss inside a key callback prevents the old event from committing afterward");
    jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_KEY_UP, 30, 0);
    a.restoreFocus = second;
    a.restoreCapabilities = original;
    jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
    jalium_test_wayland_inject_seat_capabilities(original & ~2u);
    Check(jalium_test_wayland_get_seat_capabilities() == original &&
        jalium_test_wayland_inject_keyboard(second, JALIUM_EVENT_KEY_DOWN, 30, 0) == JALIUM_OK,
        "nested device restoration keeps the new keyboard and focus");
    jalium_test_wayland_inject_seat_capabilities(original);
    Pump();
    for (int cycle = 0; cycle < 5; ++cycle)
    {
        jalium_test_wayland_inject_seat_capabilities(0);
        jalium_test_wayland_inject_seat_capabilities(0);
        Check(jalium_test_wayland_get_seat_capabilities() == 0, "all removed input proxies remain absent");
        jalium_test_wayland_inject_seat_capabilities(original);
        Pump(30);
        Check(jalium_test_wayland_get_seat_capabilities() == original, "input proxies recover over repeated reattachment");
    }
    if (a.frame.target) jalium_render_target_destroy(a.frame.target);
    a.frame.target = nullptr;
    a.destroyOnLoss = true;
    jalium_test_wayland_inject_keyboard(first, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
    jalium_test_wayland_inject_seat_capabilities(original & ~2u);
    Check(a.destroyed, "keyboard-loss callback can synchronously destroy its window");
    if (!a.destroyed) jalium_window_destroy(first);
    jalium_test_wayland_inject_seat_capabilities(original);
    Pump();
    if (b.frame.target) jalium_render_target_destroy(b.frame.target);
    b.frame.target = nullptr;
    b.destroyOnLeave = true;
    jalium_test_wayland_inject_mouse(second, JALIUM_EVENT_MOUSE_ENTER, 0, 10, 10);
    jalium_test_wayland_inject_seat_capabilities(original & ~1u);
    Check(b.destroyed, "pointer-loss callback can synchronously destroy its window");
    if (!b.destroyed) jalium_window_destroy(second);
    jalium_test_wayland_inject_seat_capabilities(original);
    Pump();
}

bool SeatCommand(const std::string& command)
{
    const char* path = std::getenv("JALIUM_TEST_SEAT_CONTROL");
    if (!path || std::strlen(path) >= sizeof(sockaddr_un::sun_path)) return false;
    const int fd = socket(AF_UNIX, SOCK_DGRAM | SOCK_CLOEXEC, 0);
    if (fd < 0) return false;
    sockaddr_un address{};
    address.sun_family = AF_UNIX;
    std::strcpy(address.sun_path, path);
    const bool sent = sendto(fd, command.data(), command.size(), 0,
        reinterpret_cast<sockaddr*>(&address), sizeof address) == static_cast<ssize_t>(command.size());
    close(fd);
    return sent;
}

void WaylandSeatContracts(JaliumContext* context)
{
    struct State {
        WaylandFrame frame;
        int lost = 0, cancels = 0, repeats = 0, keys = 0, compositionEnds = 0;
        int touchDown = 0, touchCancel = 0;
        bool restoreOnLoss = false;
        bool destroyOnLoss = false, destroyed = false;
    } state;
    state.frame.context = context;
    auto* window = Create();
    Check(window != nullptr, "create seat-global fixture");
    if (!window) return;
    jalium_window_set_event_callback(window, [](const JaliumPlatformEvent* event, void* user) {
        auto& state = *static_cast<State*>(user);
        FrameCallback(event, &state.frame);
        if (event->type == JALIUM_EVENT_FOCUS_LOST) ++state.lost;
        if (event->type == JALIUM_EVENT_MOUSE_CANCEL) ++state.cancels;
        if (event->type == JALIUM_EVENT_KEY_DOWN) {
            ++state.keys;
            if (event->key.isRepeat) ++state.repeats;
        }
        if (event->type == JALIUM_EVENT_COMPOSITION_END) ++state.compositionEnds;
        if (event->type == JALIUM_EVENT_POINTER_DOWN && event->pointer.pointerType == JALIUM_POINTER_TOUCH)
            ++state.touchDown;
        if (event->type == JALIUM_EVENT_POINTER_CANCEL && event->pointer.pointerType == JALIUM_POINTER_TOUCH)
            ++state.touchCancel;
        if (event->type == JALIUM_EVENT_FOCUS_LOST && state.restoreOnLoss) {
            state.restoreOnLoss = false;
            Check(SeatCommand("add0"), "announce a replacement from inside the focus-loss callback");
            Pump(100);
            Check((jalium_test_wayland_get_seat_capabilities() & 2u) == 0,
                "nested registry dispatch cannot bind the new keyboard before old cleanup completes");
        }
        if (event->type == JALIUM_EVENT_FOCUS_LOST && state.destroyOnLoss) {
            state.destroyed = true;
            jalium_window_destroy(event->window);
        }
    }, &state);
    jalium_window_show(window);
    Check(Wait([&] { return state.frame.presentedFrames > 0; }), "map seat-global fixture");
    Pump(150); // Allow the committed buffer to enter Weston's view list.
    Check(Wait([] { return jalium_test_wayland_get_seat_capabilities() == 7; }),
        "controlled compositor supplies pointer keyboard and touch");
    const std::string hold = "hold " + std::to_string(getpid());
    for (int cycle = 0; cycle < 2; ++cycle) {
        const int keys = state.keys;
        const int beforeRepeats = state.repeats;
        const int touchDown = state.touchDown, touchCancel = state.touchCancel;
        Check(SeatCommand(hold) && Wait([&] { return state.keys > keys; }),
            "real seat key events reach the mapped window");
        Check(state.touchDown == touchDown + 2, "real seat supplies two simultaneous touch contacts");
        Check((jalium_input_get_key_state(0x41) & 0x8000) != 0 &&
            (jalium_input_get_key_state(0x11) & 0x8000) != 0, "seat fixture holds real Control and A");
        Check(Wait([&] { return state.repeats > beforeRepeats; }), "seat fixture starts real key repeat");
        Check(jalium_test_wayland_inject_preedit(window, "拼音未提交") == JALIUM_OK,
            "start composition before whole-seat removal");
        const int cancels = state.cancels, ends = state.compositionEnds;
        Check(SeatCommand("remove0") && Wait([] { return jalium_test_wayland_get_seat_capabilities() == 0; }),
            "real registry global_remove releases every input proxy");
        Check(state.cancels == cancels + 1, "whole-seat removal cancels the captured pointer exactly once");
        Check(state.touchCancel == touchCancel + 2, "whole-seat removal cancels both touch contacts exactly once");
        Check(state.compositionEnds == ends + 1, "whole-seat removal cancels uncommitted composition");
        Check((jalium_input_get_key_state(0x41) & 0x8000) == 0 &&
            (jalium_input_get_key_state(0x11) & 0x8000) == 0, "whole-seat removal clears held keys");
        const int repeats = state.repeats;
        Pump(150);
        Check(state.repeats == repeats, "whole-seat removal leaves no repeat timer");
        Check(jalium_window_show_system_menu(window, 1, 2) == JALIUM_ERROR_NOT_SUPPORTED,
            "removed seat cannot authorize a stale menu request");
        Check(SeatCommand("add0") && Wait([] { return jalium_test_wayland_get_seat_capabilities() == 7; }),
            "new registry seat recovers input without recreating the window");
        Pump();
        Check(jalium_window_show_system_menu(window, 1, 2) == JALIUM_ERROR_NOT_SUPPORTED,
            "new seat does not revive the previous seat's menu authorization");
    }
    Check(SeatCommand("add1"), "announce a second seat while the first is active");
    Pump();
    Check(SeatCommand("remove0"), "remove active seat with an already announced replacement");
    Pump();
    const int keys = state.keys;
    Check(SeatCommand(hold) && Wait([&] { return state.keys > keys; }),
        "already announced second seat takes over and delivers real key input");
    Check(SeatCommand("remove0"), "ignore removal of an inactive seat");
    Pump();
    Check(jalium_test_wayland_get_seat_capabilities() == 7, "unrelated removal preserves the active replacement");
    Check(SeatCommand("remove1") && Wait([] { return jalium_test_wayland_get_seat_capabilities() == 0; }),
        "replacement can also be removed cleanly");
    Check(SeatCommand("add0") && Wait([] { return jalium_test_wayland_get_seat_capabilities() == 7; }),
        "input seat can return after all seats disappeared");
    Pump();
    Check(SeatCommand("focus " + std::to_string(getpid())), "focus returning seat before callback teardown");
    Pump();
    const int lost = state.lost;
    state.restoreOnLoss = true;
    Check(SeatCommand("remove0") && Wait([&] {
        return state.lost == lost + 1 && jalium_test_wayland_get_seat_capabilities() == 7;
    }), "seat announced during a removal callback binds after cleanup and remains usable");
    const int restoredKeys = state.keys;
    Check(SeatCommand(hold) && Wait([&] { return state.keys > restoredKeys; }),
        "callback-announced replacement delivers real key input");
    if (state.frame.target) jalium_render_target_destroy(state.frame.target);
    state.frame.target = nullptr;
    state.destroyOnLoss = true;
    Check(SeatCommand("remove0") && Wait([&] { return state.destroyed; }),
        "whole-seat focus-loss callback can destroy its window");
    if (!state.destroyed) jalium_window_destroy(window);
    Check(SeatCommand("add0"), "restore seat after callback teardown");
    Pump();
}

void WaylandWindowContracts(bool menuInput = false, bool inputDevices = false, bool seatGlobals = false)
{
    void* module = dlopen("libjalium.native.software.so", RTLD_NOW | RTLD_GLOBAL);
    auto initialize = module ? reinterpret_cast<void(*)()>(dlsym(module, "jalium_software_init")) : nullptr;
    Check(initialize != nullptr, "load software presenter for real Wayland mapping");
    if (!initialize) return;
    initialize();
    WaylandFrame frame;
    frame.context = jalium_context_create(JALIUM_BACKEND_SOFTWARE);
    if (seatGlobals && frame.context) WaylandSeatContracts(frame.context);
    auto* window = Create();
    if (!window || !frame.context) { Check(false, "create Wayland render fixture"); return; }
    jalium_window_set_event_callback(window, FrameCallback, &frame);
    jalium_window_show(window);
    Check(Wait([&] { return frame.presentedFrames > 0; }), "Wayland maps and paints initial window");
    if (menuInput) WaylandSystemMenuInputContracts(frame.context);
    if (inputDevices) WaylandInputDeviceContracts(frame.context);
    auto keyboardOperations = reinterpret_cast<uint32_t (*)(JaliumPlatformWindow*)>(
        dlsym(RTLD_DEFAULT, "jalium_linux_window_get_keyboard_operations"));
    auto keyboardBegin = reinterpret_cast<int32_t (*)(JaliumPlatformWindow*, int32_t)>(
        dlsym(RTLD_DEFAULT, "jalium_linux_window_begin_keyboard_move_resize"));
    Check(keyboardOperations && keyboardBegin && keyboardOperations(window) == 0 &&
        keyboardBegin(window, 0) == JALIUM_ERROR_NOT_SUPPORTED &&
        keyboardBegin(window, 1) == JALIUM_ERROR_NOT_SUPPORTED,
        "Wayland does not advertise unacknowledged keyboard geometry handoff");
    Check(jalium_linux_window_set_application_id(window, "org.jalium.WindowMapped") == JALIUM_OK,
        "Wayland mapped identity updates are supported");
    const float initialScale = jalium_window_get_dpi_scale(window);
    Pump();
    Check(jalium_linux_window_get_resizable_edges(window) == 15, "older shells default to all resizable edges");
    const int constraintEvents = frame.resizeConstraints;
    const uint32_t constrained[] = {4, 10, 12, 99}; // activated, constrained left/top, unknown
    Check(jalium_test_wayland_inject_toplevel_configure(window, 0, 0, constrained, 4) == JALIUM_OK &&
        jalium_linux_window_get_resizable_edges(window) == 15 && frame.resizeConstraints == constraintEvents,
        "constrained edges wait for the paired surface acknowledgement");
    jalium_test_wayland_apply_surface_configuration(window);
    // Observe these injected configure transactions before polling unrelated
    // compositor replies, which can legitimately change the constraints again.
    Check(jalium_linux_window_get_resizable_edges(window) == 10 && frame.resizeConstraints == constraintEvents + 1,
        "acknowledged edge constraints preserve the free bottom and right edges");
    Check(jalium_window_begin_resize_drag(window, JALIUM_RESIZE_EDGE_LEFT) == JALIUM_ERROR_NOT_SUPPORTED &&
        jalium_window_begin_resize_drag(window, JALIUM_RESIZE_EDGE_TOP_RIGHT) == JALIUM_ERROR_NOT_SUPPORTED &&
        jalium_window_begin_resize_drag(window, JALIUM_RESIZE_EDGE_RIGHT) == JALIUM_ERROR_INVALID_STATE,
        "native requests reject constrained edges and require a grab for free edges");
    jalium_test_wayland_inject_toplevel_configure(window, 0, 0, constrained, 4);
    jalium_test_wayland_apply_surface_configuration(window);
    Check(frame.resizeConstraints == constraintEvents + 1, "duplicate constraints do not publish duplicate notifications");
    const uint32_t unconstrained[] = {4};
    jalium_test_wayland_inject_toplevel_configure(window, 0, 0, unconstrained, 1);
    jalium_test_wayland_apply_surface_configuration(window);
    Check(jalium_linux_window_get_resizable_edges(window) == 15 && frame.resizeConstraints == constraintEvents + 2,
        "untiled configure restores all resize edges");
    jalium_test_wayland_inject_toplevel_configure(window, 0, 0, constrained, 4);
    jalium_test_wayland_apply_surface_configuration(window);
    Pump(30);
    jalium_window_hide(window);
    jalium_window_show(window);
    Check(Wait([&] { return jalium_linux_window_get_resizable_edges(window) == 15; }),
        "new roles discard old edge constraints after their first configure");
    Pump();

    int32_t boundedWidth = 0, boundedHeight = 0;
    const int logicalWidth = 420, logicalHeight = 280;
    const int initialWidth = static_cast<int>(std::lround(logicalWidth * initialScale));
    const int initialHeight = static_cast<int>(std::lround(logicalHeight * initialScale));
    Check(jalium_test_wayland_inject_configure_bounds(window, 180, 120) == JALIUM_OK,
        "configure_bounds accepts compositor recommendations");
    jalium_window_get_client_size(window, &boundedWidth, &boundedHeight);
    Check(boundedWidth == initialWidth && boundedHeight == initialHeight, "bounds alone do not resize or repaint the surface");
    jalium_test_wayland_inject_toplevel_configure(window, 0, 0, unconstrained, 1);
    jalium_test_wayland_apply_surface_configuration(window);
    Pump(30);
    jalium_window_get_client_size(window, &boundedWidth, &boundedHeight);
    Check(boundedWidth == std::lround(std::min(logicalWidth, 180) * initialScale) &&
        boundedHeight == std::lround(std::min(logicalHeight, 120) * initialScale),
        "client-chosen dimensions fit bounds in logical coordinates at the current scale");
    jalium_test_wayland_inject_toplevel_configure(window, logicalWidth, logicalHeight, unconstrained, 1);
    jalium_test_wayland_apply_surface_configuration(window);
    Pump(30);
    jalium_window_get_client_size(window, &boundedWidth, &boundedHeight);
    Check(boundedWidth == initialWidth && boundedHeight == initialHeight, "nonzero compositor dimensions are not clipped by recommendations");
    jalium_window_set_min_max_size(window, 300, 200, 0, 0);
    jalium_test_wayland_inject_toplevel_configure(window, 0, 0, unconstrained, 1);
    jalium_test_wayland_apply_surface_configuration(window);
    Pump(30);
    jalium_window_get_client_size(window, &boundedWidth, &boundedHeight);
    Check(boundedWidth >= 300 && boundedHeight >= 200, "explicit minimum constraints take priority over recommended bounds");
    jalium_window_set_min_max_size(window, 0, 0, 0, 0);
    jalium_test_wayland_inject_configure_bounds(window, 0, 0);
    jalium_test_wayland_inject_toplevel_configure(window, logicalWidth, logicalHeight, unconstrained, 1);
    jalium_test_wayland_apply_surface_configuration(window);
    Pump(30);
    const uint32_t maximized[] = {1, 4};
    jalium_test_wayland_inject_toplevel_configure(window, logicalWidth + 20, 0, maximized, 2);
    jalium_test_wayland_apply_surface_configuration(window);
    Pump(30);
    jalium_window_get_client_size(window, &boundedWidth, &boundedHeight);
    Check(boundedWidth == std::lround((logicalWidth + 20) * initialScale) && boundedHeight == initialHeight,
        "zero dimensions remain independently client-chosen outside Normal state");
    jalium_test_wayland_inject_toplevel_configure(window, 0, 0, unconstrained, 1);
    jalium_test_wayland_apply_surface_configuration(window);
    Pump(30);
    jalium_window_get_client_size(window, &boundedWidth, &boundedHeight);
    Check(boundedWidth == initialWidth && boundedHeight == initialHeight, "unknown bounds preserve saved normal restore geometry");
    WaylandClientChosenSizeContracts(window, initialScale);
    const int paintsBeforeSuspension = frame.paints;
    const int renderingEventsBefore = frame.renderingAvailability;
    Check(jalium_test_wayland_inject_suspended_configure(window, 1) == JALIUM_OK &&
        !jalium_linux_window_is_rendering_suspended(window) && frame.renderingAvailability == renderingEventsBefore,
        "suspension waits for the paired surface configure acknowledgement");
    jalium_test_wayland_apply_surface_configuration(window);
    Check(jalium_linux_window_is_rendering_suspended(window) &&
        !jalium_wayland_surface_is_ready(jalium_window_get_native_handle(window)) &&
        frame.renderingAvailability == renderingEventsBefore + 1,
        "acknowledged suspension pauses presentation and notifies the application");
    for (int i = 0; i < 10; ++i) jalium_window_invalidate(window);
    Pump();
    Check(frame.paints == paintsBeforeSuspension && jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL,
        "suspended invalidations do not paint or invent a minimized state");
    jalium_test_wayland_inject_suspended_configure(window, 0);
    Check(jalium_linux_window_is_rendering_suspended(window), "resumption also waits for surface acknowledgement");
    jalium_test_wayland_apply_surface_configuration(window);
    Check(!jalium_linux_window_is_rendering_suspended(window) && frame.paints > paintsBeforeSuspension &&
        frame.renderingAvailability == renderingEventsBefore + 2,
        "resumption paints pending content and restores presentation readiness");
    jalium_test_wayland_inject_suspended_configure(window, 0);
    jalium_test_wayland_apply_surface_configuration(window);
    Check(frame.renderingAvailability == renderingEventsBefore + 2, "unchanged availability has no duplicate event");
    jalium_test_wayland_inject_suspended_configure(window, 1);
    jalium_test_wayland_apply_surface_configuration(window);
    jalium_window_hide(window);
    const int beforeSuspendedReshow = frame.paints;
    jalium_window_show(window);
    Check(Wait([&] { return !jalium_linux_window_is_rendering_suspended(window) && frame.paints > beforeSuspendedReshow; }),
        "hide/show clears obsolete suspension after the new role's configure");
    if (const char* expected = std::getenv("JALIUM_TEST_FRACTIONAL_SCALE"))
        Check(std::abs(initialScale - std::atoi(expected) / 120.0f) < 0.001f,
            "real preferred_scale protocol event selects fractional rendering");
    const int32_t decorationInjection = jalium_test_wayland_inject_decoration_configure(window, 2);
    if (decorationInjection == JALIUM_OK)
    {
        Check(jalium_linux_window_get_decoration_mode(window) == 1 && frame.decorations == 0,
            "decoration mode waits for surface configure acknowledgement");
        Check(jalium_test_wayland_apply_pending_decoration(window) == JALIUM_OK &&
            jalium_linux_window_get_decoration_mode(window) == 2 && frame.decorations == 1,
            "server decoration acknowledgement notifies the application");
        jalium_test_wayland_inject_decoration_configure(window, 1);
        jalium_test_wayland_apply_pending_decoration(window);
        Check(jalium_linux_window_get_decoration_mode(window) == 1 && frame.decorations == 2,
            "client decoration acknowledgement restores application chrome");
        jalium_test_wayland_apply_pending_decoration(window);
        Check(frame.decorations == 2, "unchanged decoration mode does not emit duplicate events");
    }
    for (JaliumWindowState state : {JALIUM_WINDOW_STATE_MAXIMIZED, JALIUM_WINDOW_STATE_FULLSCREEN,
        JALIUM_WINDOW_STATE_MAXIMIZED, JALIUM_WINDOW_STATE_NORMAL})
    {
        const int before = frame.states;
        jalium_window_set_state(window, state);
        Check(Wait([&] { return jalium_window_get_state(window) == state && frame.states > before; }),
            "Wayland state transition publishes compositor acknowledgement");
    }
    Check(Wait([&] { int w = 0, h = 0; jalium_window_get_client_size(window, &w, &h);
        return w == std::lround(420 * initialScale) && h == std::lround(280 * initialScale); }),
        "Wayland returns to saved normal dimensions");
    jalium_window_set_state(window, JALIUM_WINDOW_STATE_FULLSCREEN);
    Check(Wait([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_FULLSCREEN; }),
        "enter fullscreen before superseding a pending maximize request");
    jalium_window_set_state(window, JALIUM_WINDOW_STATE_MAXIMIZED);
    jalium_window_set_state(window, JALIUM_WINDOW_STATE_NORMAL);
    Check(Wait([&] { return jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL; }),
        "new restore request cancels deferred maximize");
    Pump();
    Check(jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL,
        "cancelled maximize does not replay after restoration");
    int32_t cancelledWidth = 0, cancelledHeight = 0;
    jalium_window_get_client_size(window, &cancelledWidth, &cancelledHeight);
    Check(cancelledWidth == std::lround(420 * initialScale) && cancelledHeight == std::lround(280 * initialScale),
        "pending configure suggestions do not overwrite normal restore dimensions");
    int32_t rx = 0, ry = 0, rw = 0, rh = 0, cw = 0, ch = 0;
    jalium_linux_window_get_restore_bounds(window, &rx, &ry, &rw, &rh);
    jalium_window_get_client_size(window, &cw, &ch);
    std::printf("Wayland restored client=%dx%d normal=%dx%d\n", cw, ch, rw, rh);
    const uint32_t originalCapabilities = jalium_linux_window_get_capabilities(window);
    const int capabilityEvents = frame.capabilities;
    const uint32_t partialCapabilities[] = {2, 99};
    Check(jalium_test_wayland_inject_wm_capabilities(window, partialCapabilities, 2) == JALIUM_OK &&
        jalium_linux_window_get_capabilities(window) == originalCapabilities && frame.capabilities == capabilityEvents,
        "capability changes wait for surface acknowledgement");
    jalium_test_wayland_apply_pending_capabilities(window);
    const uint32_t partial = jalium_linux_window_get_capabilities(window);
    Check((partial & JALIUM_WINDOW_CAPABILITY_MAXIMIZE) && !(partial & JALIUM_WINDOW_CAPABILITY_MINIMIZE) &&
        !(partial & JALIUM_WINDOW_CAPABILITY_FULLSCREEN) && !(partial & JALIUM_WINDOW_CAPABILITY_WINDOW_MENU) &&
        (partial & JALIUM_WINDOW_CAPABILITY_STATE_REQUESTS_RESTRICTED) && frame.capabilities == capabilityEvents + 1,
        "compositor capability list publishes supported actions and ignores unknown entries");
    jalium_window_set_state(window, JALIUM_WINDOW_STATE_MINIMIZED);
    Check(jalium_window_get_state(window) == JALIUM_WINDOW_STATE_NORMAL,
        "unsupported minimize does not invent a minimized state");
    Check(jalium_window_show_system_menu(window, 0, 0) == JALIUM_ERROR_NOT_SUPPORTED,
        "missing compositor menu capability allows framework fallback");
    const uint32_t allCapabilities[] = {1, 2, 3, 4};
    jalium_test_wayland_inject_wm_capabilities(window, allCapabilities, 4);
    jalium_test_wayland_apply_pending_capabilities(window);
    Check((jalium_linux_window_get_capabilities(window) & JALIUM_WINDOW_CAPABILITY_ALL) == JALIUM_WINDOW_CAPABILITY_ALL,
        "capabilities can be restored at runtime");
    const int32_t fractionalResult = jalium_test_wayland_inject_fractional_scale(window, 150);
    if (fractionalResult == JALIUM_OK)
    {
        for (uint32_t scale120 : {150u, 180u, 210u, 120u, 240u, 150u})
        {
            const float scale = scale120 / 120.0f;
            const int paintsBefore = frame.paints;
            jalium_test_wayland_inject_fractional_scale(window, scale120);
            jalium_window_invalidate(window);
            Check(Wait([&] { return frame.paints > paintsBefore; }) &&
                std::abs(jalium_window_get_dpi_scale(window) - scale) < 0.001f,
                "fractional transition repaints at preferred scale");
            jalium_window_get_client_size(window, &cw, &ch);
            Check(cw == std::lround(420 * scale) && ch == std::lround(280 * scale),
                "fractional buffer dimensions round without logical-size drift");
            jalium_test_wayland_inject_touch(window, JALIUM_EVENT_POINTER_DOWN, 701, 37.5f, 62.5f);
            Check(std::abs(frame.pointerX - 37.5f) < 0.02f && std::abs(frame.pointerY - 62.5f) < 0.02f,
                "fractional touch coordinates remain physical pixels");
            jalium_test_wayland_inject_touch(window, JALIUM_EVENT_POINTER_UP, 701, 37.5f, 62.5f);
        }
        jalium_test_wayland_set_output(window, 909, 3, 1);
        Check(std::abs(jalium_window_get_dpi_scale(window) - 1.25f) < 0.001f,
            "fractional preference takes precedence over integer output scale");
        Check(jalium_test_wayland_inject_fractional_scale(window, 0) == JALIUM_ERROR_INVALID_ARGUMENT,
            "zero fractional preference is rejected");
        jalium_test_wayland_inject_fractional_scale(window, static_cast<uint32_t>(std::lround(initialScale * 120)));
    }
    jalium_window_hide(window);
    const auto handle = jalium_window_get_native_handle(window);
    Check(jalium_window_set_min_max_size(window, 140, 90, 700, 500) == JALIUM_OK,
        "hidden Wayland window accepts new constraints");
    jalium_window_set_state(window, JALIUM_WINDOW_STATE_MAXIMIZED);
    Check(jalium_wayland_surface_is_ready(handle) == 0, "hidden state changes leave Wayland surface unmapped");
    const int before = frame.paints;
    jalium_window_show(window);
    Check(Wait([&] { return frame.paints > before && jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MAXIMIZED; }),
        "Wayland hide/show reapplies requested state and repaints");
    Check(Wait([&] { return !frame.pendingPresent; }), "deferred Wayland presentation recovers before teardown");
    if (frame.target) jalium_render_target_destroy(frame.target);
    jalium_window_destroy(window);
    WaylandTiledRestoreContracts(frame.context);
    // Closing from a paint callback must not leave the dispatcher touching a
    // freed window after the managed/native callback returns.
    auto* selfClosing = Create();
    WaylandFrame closing;
    closing.context = frame.context;
    closing.destroyOnPaint = true;
    jalium_window_set_event_callback(selfClosing, FrameCallback, &closing);
    jalium_window_show(selfClosing);
    Check(Wait([&] { return closing.destroyed; }), "Wayland paint callback can synchronously destroy its window");
    Pump();
    auto* suspensionClosing = Create();
    WaylandFrame suspendedClosing;
    suspendedClosing.context = frame.context;
    jalium_window_set_event_callback(suspensionClosing, FrameCallback, &suspendedClosing);
    jalium_window_show(suspensionClosing);
    Check(Wait([&] { return suspendedClosing.presentedFrames > 0; }), "map rendering-availability callback lifetime fixture");
    if (suspendedClosing.target) jalium_render_target_destroy(suspendedClosing.target);
    suspendedClosing.target = nullptr;
    suspendedClosing.destroyOnRenderingAvailability = true;
    jalium_test_wayland_inject_suspended_configure(suspensionClosing, 1);
    jalium_test_wayland_apply_surface_configuration(suspensionClosing);
    Check(suspendedClosing.destroyed, "rendering-availability callback can synchronously destroy its window");
    Pump();
    WaylandFrame constraintsClosing;
    constraintsClosing.context = frame.context;
    auto* edgeClosing = Create();
    jalium_window_set_event_callback(edgeClosing, FrameCallback, &constraintsClosing);
    jalium_window_show(edgeClosing);
    Check(Wait([&] { return constraintsClosing.presentedFrames > 0; }), "map resize-constraints callback lifetime fixture");
    if (constraintsClosing.target) jalium_render_target_destroy(constraintsClosing.target);
    constraintsClosing.target = nullptr;
    constraintsClosing.destroyOnResizeConstraints = true;
    jalium_test_wayland_inject_toplevel_configure(edgeClosing, 0, 0, constrained, 4);
    jalium_test_wayland_apply_surface_configuration(edgeClosing);
    Check(constraintsClosing.destroyed, "resize-constraints callback can synchronously destroy its window");
    Pump();
    WaylandFrame tiledClosing;
    tiledClosing.context = frame.context;
    auto* tileClosing = Create();
    jalium_window_set_event_callback(tileClosing, FrameCallback, &tiledClosing);
    jalium_window_show(tileClosing);
    Check(Wait([&] { return tiledClosing.presentedFrames > 0; }), "map tile-state callback lifetime fixture");
    if (tiledClosing.target) jalium_render_target_destroy(tiledClosing.target);
    tiledClosing.target = nullptr;
    tiledClosing.destroyOnResizeConstraints = true;
    const uint32_t tiled[] = {4, 5};
    jalium_test_wayland_inject_toplevel_configure(tileClosing, 420, 280, tiled, 2);
    jalium_test_wayland_apply_surface_configuration(tileClosing);
    Check(tiledClosing.destroyed, "tile-only constraint callback can synchronously destroy its window");
    Pump();
    jalium_context_destroy(frame.context);
    dlclose(module);
}
}
int main(int argc, char** argv)
{
    const bool menuInput = argc > 1 && std::strcmp(argv[1], "--wayland-system-menu") == 0;
    const bool inputDevices = argc > 1 && std::strcmp(argv[1], "--wayland-input-devices") == 0;
    const bool seatGlobals = argc > 1 && std::strcmp(argv[1], "--wayland-seat-globals") == 0;
    const bool wayland = menuInput || inputDevices || seatGlobals || (argc > 1 && std::strcmp(argv[1], "--wayland") == 0);
    const bool wm = argc > 1 && std::strcmp(argv[1], "--x11-wm") == 0;
    const bool x11Constraints = argc > 1 && std::strcmp(argv[1], "--x11-constraints") == 0;
    const bool x11ResizeOrigin = argc > 1 && std::strcmp(argv[1], "--x11-resize-origin") == 0;
    const bool x11Keyboard = argc > 1 && std::strcmp(argv[1], "--x11-keyboard") == 0;
    const bool legacy = argc > 2 && std::strcmp(argv[2], "--legacy-baseline") == 0;
    if (jalium_platform_init() != JALIUM_OK) return 2;
    if (!legacy) BasicContracts(wayland);
    if (wm) X11WindowManagerContracts(legacy);
    if (x11Constraints) X11EdgeConstraintContracts();
    if (x11ResizeOrigin) X11ResizeOriginContracts();
    if (x11Keyboard) X11KeyboardOperationContracts();
    if (wayland) WaylandWindowContracts(menuInput, inputDevices, seatGlobals);
    jalium_platform_shutdown();
    std::printf("Linux Window checks: %d passed, %d failed\n", checks - failures, failures);
    return failures == 0 ? 0 : 1;
}
