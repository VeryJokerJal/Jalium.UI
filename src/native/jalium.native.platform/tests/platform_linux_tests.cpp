#if defined(__linux__) && !defined(__ANDROID__)

#include "jalium_platform.h"
#include "jalium_api.h"

#include <X11/Xatom.h>
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <poll.h>
#include <unistd.h>
#include <dlfcn.h>

#include <atomic>
#include <array>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

using namespace std::chrono_literals;

namespace {

int g_failures = 0;

void Check(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << "FAILED: " << message << '\n';
        ++g_failures;
    }
}

void TestDragEventAbi()
{
    Check(offsetof(JaliumPlatformEvent, window) == 8,
          "platform event window pointer remains at ABI offset 8");
    Check(offsetof(JaliumPlatformEvent, drag.sessionId) == 32,
          "drag session id has stable aligned ABI offset");
    Check(offsetof(JaliumPlatformEvent, drag.mimeTypes) == 40,
          "drag MIME list pointer has stable ABI offset");
    Check(offsetof(JaliumPlatformEvent, drag.data) == 56,
          "drag byte pointer has stable ABI offset");
    Check(sizeof(JaliumPlatformEvent) == 72,
          "64-bit platform event includes complete drag payload union");
    Check(offsetof(JaliumPlatformEvent, pointer.pointerId) == 16 &&
              offsetof(JaliumPlatformEvent, pointer.pressure) == 28 &&
              offsetof(JaliumPlatformEvent, pointer.pointerType) == 44 &&
              offsetof(JaliumPlatformEvent, pointer.modifiers) == 48,
          "rich pointer ABI preserves every existing field offset");
    Check(offsetof(JaliumPlatformEvent, pointer.flags) == 52 &&
              offsetof(JaliumPlatformEvent, pointer.toolType) == 56 &&
              offsetof(JaliumPlatformEvent, pointer.buttons) == 60,
          "rich pointer flags, tool type, and buttons fit inside the union");
    Check(JALIUM_EVENT_DELETE_SURROUNDING_TEXT == 46 &&
              offsetof(JaliumPlatformEvent,
                       deleteSurrounding.beforeUtf8Bytes) == 16 &&
              offsetof(JaliumPlatformEvent,
                       deleteSurrounding.afterUtf8Bytes) == 20,
          "delete-surrounding event 46 carries stable UTF-8 byte lengths");
}

void TestImeAndShellProtocolAbi()
{
    using UpdateImeContext = int32_t (*)(
        JaliumPlatformWindow*, int32_t, const char*, int32_t, int32_t,
        int32_t, int32_t, int32_t, int32_t);
    using ShowSystemMenu = int32_t (*)(
        JaliumPlatformWindow*, int32_t, int32_t);
    using ActivateWindow = int32_t (*)(JaliumPlatformWindow*);
    UpdateImeContext updateImeContext = &jalium_window_update_ime_context;
    ShowSystemMenu showSystemMenu = &jalium_window_show_system_menu;
    ActivateWindow activateWindow = &jalium_window_activate;
    Check(updateImeContext && showSystemMenu && activateWindow,
          "IME context, system-menu, and activation exports match the native ABI");

    Check(updateImeContext(
              nullptr, 1, "a\xE4\xB8\xAD\xF0\x9F\x98\x80z",
              8, 1, 11, 13, 2, 19) == JALIUM_ERROR_INVALID_ARGUMENT,
          "IME context update rejects a null window before inspecting UTF-8 state");
    Check(showSystemMenu(nullptr, 17, 23) == JALIUM_ERROR_INVALID_ARGUMENT,
          "system-menu request rejects a null window");
    Check(activateWindow(nullptr) == JALIUM_ERROR_INVALID_ARGUMENT,
          "activation request rejects a null window");
}

void TestXInputSmoothScrollConversion()
{
    float deltaX = 0.0f;
    float deltaY = 0.0f;
    Check(jalium_test_xinput_smooth_scroll_delta(
              100.0, 102.5, 5.0, 1, &deltaX, &deltaY) == 1 &&
              std::abs(deltaX) < 0.0001f &&
              std::abs(deltaY + 0.5f) < 0.0001f,
          "XI2 vertical scroll preserves fractional valuator units");

    Check(jalium_test_xinput_smooth_scroll_delta(
              100.0, 95.0, 5.0, 1, &deltaX, &deltaY) == 1 &&
              std::abs(deltaX) < 0.0001f &&
              std::abs(deltaY - 1.0f) < 0.0001f,
          "XI2 vertical scroll maps decreasing valuators upward");

    Check(jalium_test_xinput_smooth_scroll_delta(
              10.0, 12.5, 5.0, 0, &deltaX, &deltaY) == 1 &&
              std::abs(deltaX - 0.5f) < 0.0001f &&
              std::abs(deltaY) < 0.0001f,
          "XI2 horizontal scroll preserves fractional valuator units");

    Check(jalium_test_xinput_smooth_scroll_delta(
              10.0, 15.0, -5.0, 1, &deltaX, &deltaY) == 1 &&
              std::abs(deltaX) < 0.0001f &&
              std::abs(deltaY - 1.0f) < 0.0001f,
          "XI2 scroll respects the server-provided increment direction");

    Check(jalium_test_xinput_smooth_scroll_delta(
              0.0, 1000.0, 5.0, 1, &deltaX, &deltaY) == 0 &&
              std::abs(deltaX) < 0.0001f &&
              std::abs(deltaY) < 0.0001f,
          "XI2 valuator wrap or reset is ignored as a new baseline");
    Check(jalium_test_xinput_smooth_scroll_delta(
              0.0, 1.0, 0.0, 1, &deltaX, &deltaY) == 0,
          "XI2 zero scroll increment is rejected");
    Check(jalium_test_xinput_smooth_scroll_delta(
              0.0, 1.0, 1.0, 1, nullptr, &deltaY) == -1,
          "XI2 scroll conversion validates output pointers");

    const uint32_t hoverFlags = jalium_test_xinput_pen_flags(
        JALIUM_POINTER_TOOL_ERASER, 1, 1, 0,
        JALIUM_POINTER_BUTTON_BARREL);
    Check((hoverFlags &
           (JALIUM_POINTER_FLAG_PRIMARY |
            JALIUM_POINTER_FLAG_IN_RANGE |
            JALIUM_POINTER_FLAG_ERASER |
            JALIUM_POINTER_FLAG_INVERTED |
            JALIUM_POINTER_FLAG_BARREL)) ==
          (JALIUM_POINTER_FLAG_PRIMARY |
           JALIUM_POINTER_FLAG_IN_RANGE |
           JALIUM_POINTER_FLAG_ERASER |
           JALIUM_POINTER_FLAG_INVERTED |
           JALIUM_POINTER_FLAG_BARREL) &&
          (hoverFlags & JALIUM_POINTER_FLAG_IN_CONTACT) == 0,
          "XI2 eraser hover and barrel state do not fabricate contact");
    const uint32_t contactFlags = jalium_test_xinput_pen_flags(
        JALIUM_POINTER_TOOL_PEN, 0, 1, 1,
        JALIUM_POINTER_BUTTON_PRIMARY);
    Check((contactFlags &
           (JALIUM_POINTER_FLAG_IN_RANGE |
            JALIUM_POINTER_FLAG_IN_CONTACT)) ==
          (JALIUM_POINTER_FLAG_IN_RANGE |
           JALIUM_POINTER_FLAG_IN_CONTACT),
          "XI2 tip contact is independently represented from tool shape");
}

void TestConfigurableDoubleClickTracking()
{
    Check(jalium_platform_set_double_click_settings(275, 7.0f) == JALIUM_OK,
          "public native ABI accepts desktop-configured double-click thresholds");
    Check(jalium_test_register_click(100, 10.0f, 20.0f, 1, 1) == 1 &&
              jalium_test_register_click(375, 17.0f, 27.0f, 1, 0) == 2,
          "RegisterClick accepts the public time and distance boundary");
    Check(jalium_test_register_click(651, 17.0f, 27.0f, 1, 0) == 1 &&
              jalium_test_register_click(700, 25.0f, 27.0f, 1, 0) == 1,
          "RegisterClick rejects timestamps and coordinates beyond public thresholds");
    Check(jalium_test_register_click(
              UINT32_MAX - 100u, 5.0f, 5.0f, 1, 1) == 1 &&
              jalium_test_register_click(50u, 5.0f, 5.0f, 1, 0) == 2,
          "uint32 X11/Wayland timestamps remain reliable across wraparound");
    Check(jalium_platform_set_double_click_settings(0, 4.0f) ==
              JALIUM_ERROR_INVALID_ARGUMENT &&
              jalium_platform_set_double_click_settings(60001, 4.0f) ==
              JALIUM_ERROR_INVALID_ARGUMENT &&
              jalium_platform_set_double_click_settings(500, -1.0f) ==
              JALIUM_ERROR_INVALID_ARGUMENT &&
              jalium_platform_set_double_click_settings(500, INFINITY) ==
              JALIUM_ERROR_INVALID_ARGUMENT &&
              jalium_platform_set_double_click_settings(500, 16385.0f) ==
              JALIUM_ERROR_INVALID_ARGUMENT,
          "public double-click ABI rejects zero, excessive, negative, and non-finite settings");
    Check(jalium_test_register_click(1000, 0.0f, 0.0f, 1, 1) == 1 &&
              jalium_test_register_click(1275, 7.0f, 7.0f, 1, 0) == 2,
          "rejected settings leave the last valid RegisterClick thresholds active");
    Check(jalium_platform_set_double_click_settings(500, 4.0f) == JALIUM_OK,
          "double-click protocol test restores the native defaults");
}

void TestX11GlobalCursorPosition()
{
    float x = -1.0f;
    float y = -1.0f;
    Check(jalium_input_get_cursor_pos(&x, &y) == JALIUM_OK &&
              std::isfinite(x) && std::isfinite(y),
          "X11 cursor query returns root-window screen coordinates");
    Check(jalium_input_get_cursor_pos(nullptr, &y) == JALIUM_ERROR_INVALID_ARGUMENT,
          "cursor query validates its output pointers");
}

void TestTouchCapabilityAbi()
{
    int32_t present = 0;
    int32_t contacts = 0;
    jalium_test_override_touch_capabilities(1, 0);
    Check(jalium_input_get_touch_capabilities(&present, &contacts) == JALIUM_OK &&
              present == 1 && contacts == 0,
          "touch ABI represents Wayland presence with an unknown contact limit");
    jalium_test_override_touch_capabilities(1, 10);
    Check(jalium_input_get_touch_capabilities(&present, &contacts) == JALIUM_OK &&
              present == 1 && contacts == 10,
          "touch ABI preserves an XI2 maximum contact count");
    jalium_test_override_touch_capabilities(0, 10);
    Check(jalium_input_get_touch_capabilities(&present, &contacts) == JALIUM_OK &&
              present == 0 && contacts == 0,
          "touch ABI clears contacts when no touch device is present");
    Check(jalium_input_get_touch_capabilities(nullptr, &contacts) ==
              JALIUM_ERROR_INVALID_ARGUMENT,
          "touch ABI validates output pointers");

    jalium_test_override_touch_capabilities(-1, 0);
    Check(jalium_input_get_touch_capabilities(&present, &contacts) == JALIUM_OK &&
              (present == 0 ? contacts == 0 : contacts >= 0),
          "live touch capability discovery returns a self-consistent snapshot");
}

std::string ReadNetWmName(Display* display, Window window)
{
    const Atom property = XInternAtom(display, "_NET_WM_NAME", False);
    const Atom utf8String = XInternAtom(display, "UTF8_STRING", False);
    Atom actualType = None;
    int actualFormat = 0;
    unsigned long itemCount = 0;
    unsigned long remaining = 0;
    unsigned char* value = nullptr;

    const int result = XGetWindowProperty(
        display, window, property, 0, 4096, False, utf8String,
        &actualType, &actualFormat, &itemCount, &remaining, &value);
    if (result != Success || actualType != utf8String || actualFormat != 8 || !value)
    {
        if (value) XFree(value);
        return {};
    }

    std::string text(reinterpret_cast<char*>(value), itemCount);
    XFree(value);
    return text;
}

bool AtomListContains(Display* display, Window window, Atom property, Atom expected)
{
    Atom actualType = None;
    int actualFormat = 0;
    unsigned long itemCount = 0;
    unsigned long remaining = 0;
    unsigned char* value = nullptr;
    bool found = false;
    if (XGetWindowProperty(display, window, property, 0, 64, False, XA_ATOM,
                           &actualType, &actualFormat, &itemCount, &remaining,
                           &value) == Success && actualType == XA_ATOM &&
        actualFormat == 32 && value)
    {
        const auto* atoms = reinterpret_cast<const Atom*>(value);
        for (unsigned long index = 0; index < itemCount; ++index)
            found |= atoms[index] == expected;
    }
    if (value) XFree(value);
    return found;
}

struct InputCallbackState
{
    std::atomic<int> mouseDownCalls{0};
    std::atomic<int> monitorsChangedCalls{0};
};

void InputCallback(const JaliumPlatformEvent* event, void* userData)
{
    if (!event || !userData) return;
    auto* state = static_cast<InputCallbackState*>(userData);
    if (event->type == JALIUM_EVENT_MOUSE_DOWN)
    {
        state->mouseDownCalls.fetch_add(1, std::memory_order_release);
    }
    else if (event->type == JALIUM_EVENT_MONITORS_CHANGED)
        state->monitorsChangedCalls.fetch_add(1, std::memory_order_release);
}

struct CardinalPropertySnapshot
{
    bool existed = false;
    std::vector<unsigned long> values;
};

CardinalPropertySnapshot SnapshotCardinalProperty(
    Display* display, Window root, Atom property)
{
    CardinalPropertySnapshot snapshot;
    Atom actualType = None;
    int actualFormat = 0;
    unsigned long itemCount = 0;
    unsigned long remaining = 0;
    unsigned char* data = nullptr;
    if (XGetWindowProperty(display, root, property, 0, 4096, False,
                           XA_CARDINAL, &actualType, &actualFormat,
                           &itemCount, &remaining, &data) == Success &&
        actualType == XA_CARDINAL && actualFormat == 32)
    {
        snapshot.existed = true;
        const auto* values = reinterpret_cast<const unsigned long*>(data);
        snapshot.values.assign(values, values + itemCount);
    }
    if (data) XFree(data);
    return snapshot;
}

void RestoreCardinalProperty(
    Display* display, Window root, Atom property,
    const CardinalPropertySnapshot& snapshot)
{
    if (!snapshot.existed)
    {
        XDeleteProperty(display, root, property);
        return;
    }
    XChangeProperty(
        display, root, property, XA_CARDINAL, 32, PropModeReplace,
        reinterpret_cast<const unsigned char*>(snapshot.values.data()),
        static_cast<int>(snapshot.values.size()));
}

void TestWindowManagementExtensions()
{
    const char16_t ownerTitle[] = u"Jalium owner";
    JaliumWindowParams ownerParameters{};
    ownerParameters.title = reinterpret_cast<const JaliumUtf16Char*>(ownerTitle);
    ownerParameters.width = 320;
    ownerParameters.height = 200;
    ownerParameters.style = JALIUM_WINDOW_STYLE_RESIZABLE;
    JaliumPlatformWindow* owner = jalium_window_create(&ownerParameters);
    Check(owner != nullptr, "create owner window for management extensions");
    if (!owner) return;

    const char16_t childTitle[] = u"Jalium child";
    JaliumWindowParams childParameters = ownerParameters;
    childParameters.title = reinterpret_cast<const JaliumUtf16Char*>(childTitle);
    childParameters.parentHandle = jalium_window_get_native_handle(owner);
    JaliumPlatformWindow* child = jalium_window_create(&childParameters);
    Check(child != nullptr, "create transient child window");
    if (!child)
    {
        jalium_window_destroy(owner);
        return;
    }

    const JaliumSurfaceDescriptor childSurface = jalium_window_get_surface(child);
    auto* display = reinterpret_cast<Display*>(childSurface.handle0);
    const Window childXWindow = static_cast<Window>(childSurface.handle1);
    const Window ownerXWindow = static_cast<Window>(jalium_window_get_native_handle(owner));

    Check(std::fabs(jalium_test_x11_compute_monitor_scale(
              1920, 1080, 509, 286, 1.75f) - 1.0f) < 0.02f,
          "reasonable RandR millimetres produce a physical per-monitor scale");
    Check(std::fabs(jalium_test_x11_compute_monitor_scale(
              1920, 1080, 0, 0, 1.75f) - 1.75f) < 0.001f &&
              std::fabs(jalium_test_x11_compute_monitor_scale(
                  1920, 1080, 100, 2000, 1.5f) - 1.5f) < 0.001f,
          "missing or unreasonable RandR millimetres fall back to Xft scale");

    const int32_t monitorCount = jalium_platform_get_monitor_count();
    JaliumMonitorInfo monitor{};
    Check(monitorCount > 0 &&
              jalium_platform_get_monitor_info(0, &monitor) == JALIUM_OK &&
              monitor.width > 0 && monitor.height > 0 &&
              std::isfinite(monitor.scale) && monitor.scale > 0.0f &&
              monitor.refreshRate > 0,
          "RandR monitor info exposes per-screen geometry, scale, and refresh");

    if (monitor.width > 20 && monitor.height > 20)
    {
        const Window root = DefaultRootWindow(display);
        const Atom currentDesktop =
            XInternAtom(display, "_NET_CURRENT_DESKTOP", False);
        const Atom workArea = XInternAtom(display, "_NET_WORKAREA", False);
        const CardinalPropertySnapshot desktopSnapshot =
            SnapshotCardinalProperty(display, root, currentDesktop);
        const CardinalPropertySnapshot workAreaSnapshot =
            SnapshotCardinalProperty(display, root, workArea);

        const unsigned long desktop = 1;
        const int32_t expectedX = monitor.x + 7;
        const int32_t expectedY = monitor.y + 9;
        const int32_t expectedWidth = monitor.width - 14;
        const int32_t expectedHeight = monitor.height - 18;
        const unsigned long workAreas[] = {
            0, 0, 1, 1,
            static_cast<unsigned long>(static_cast<uint32_t>(expectedX)),
            static_cast<unsigned long>(static_cast<uint32_t>(expectedY)),
            static_cast<unsigned long>(expectedWidth),
            static_cast<unsigned long>(expectedHeight),
        };
        XChangeProperty(
            display, root, currentDesktop, XA_CARDINAL, 32, PropModeReplace,
            reinterpret_cast<const unsigned char*>(&desktop), 1);
        XChangeProperty(
            display, root, workArea, XA_CARDINAL, 32, PropModeReplace,
            reinterpret_cast<const unsigned char*>(workAreas), 8);
        XSync(display, False);

        JaliumMonitorInfo desktopOneMonitor{};
        const int32_t monitorResult =
            jalium_platform_get_monitor_info(0, &desktopOneMonitor);
        RestoreCardinalProperty(
            display, root, currentDesktop, desktopSnapshot);
        RestoreCardinalProperty(display, root, workArea, workAreaSnapshot);
        XSync(display, False);
        Check(monitorResult == JALIUM_OK &&
                  desktopOneMonitor.workX == expectedX &&
                  desktopOneMonitor.workY == expectedY &&
                  desktopOneMonitor.workWidth == expectedWidth &&
                  desktopOneMonitor.workHeight == expectedHeight,
              "_NET_WORKAREA selects the _NET_CURRENT_DESKTOP tuple");
    }

    Window transientFor = 0;
    Check(XGetTransientForHint(display, childXWindow, &transientFor) != 0 &&
              transientFor == ownerXWindow,
          "parentHandle sets WM_TRANSIENT_FOR");
    Check(jalium_window_set_owner(child, 0) == JALIUM_OK,
          "clear transient owner at runtime");
    Check(XGetTransientForHint(display, childXWindow, &transientFor) == 0,
          "clearing owner removes WM_TRANSIENT_FOR");
    Check(jalium_window_set_owner(child, ownerXWindow) == JALIUM_OK,
          "restore transient owner at runtime");

    Check(jalium_window_set_enabled(child, 0) == JALIUM_OK &&
              jalium_window_set_enabled(child, 1) == JALIUM_OK,
          "native enabled state toggles without user32");

    Check(jalium_window_set_opacity(child, 0.5) == JALIUM_OK,
          "X11 whole-window opacity is supported");
    const Atom opacityAtom = XInternAtom(display, "_NET_WM_WINDOW_OPACITY", False);
    Atom actualType = None;
    int actualFormat = 0;
    unsigned long itemCount = 0;
    unsigned long remaining = 0;
    unsigned char* opacityValue = nullptr;
    Check(XGetWindowProperty(display, childXWindow, opacityAtom, 0, 1, False,
                             XA_CARDINAL, &actualType, &actualFormat, &itemCount,
                             &remaining, &opacityValue) == Success &&
              actualType == XA_CARDINAL && actualFormat == 32 && itemCount == 1,
          "opacity writes _NET_WM_WINDOW_OPACITY");
    if (opacityValue) XFree(opacityValue);

    const Atom netWmState = XInternAtom(display, "_NET_WM_STATE", False);
    const Atom skipTaskbar = XInternAtom(display, "_NET_WM_STATE_SKIP_TASKBAR", False);
    Check(jalium_window_set_show_in_taskbar(child, 0) == JALIUM_OK &&
              AtomListContains(display, childXWindow, netWmState, skipTaskbar),
          "hidden taskbar state is advertised before mapping");
    Check(jalium_window_set_show_in_taskbar(child, 1) == JALIUM_OK &&
              !AtomListContains(display, childXWindow, netWmState, skipTaskbar),
          "taskbar state can be restored before mapping");

    Check(jalium_window_set_resizable(child, 0) == JALIUM_OK,
          "disable interactive resize");
    XSizeHints sizeHints{};
    long supplied = 0;
    Check(XGetWMNormalHints(display, childXWindow, &sizeHints, &supplied) != 0 &&
              (sizeHints.flags & (PMinSize | PMaxSize)) == (PMinSize | PMaxSize) &&
              sizeHints.min_width == sizeHints.max_width &&
              sizeHints.min_height == sizeHints.max_height,
          "non-resizable window publishes fixed normal hints");
    Check(jalium_window_set_resizable(child, 1) == JALIUM_OK &&
              jalium_window_set_min_max_size(child, 120, 80, 640, 480) == JALIUM_OK,
          "restore interactive resize and explicit constraints");

    Check(jalium_window_set_decorated(child, 0) == JALIUM_OK,
          "remove X11 server decorations at runtime");
    const Atom motifHints = XInternAtom(display, "_MOTIF_WM_HINTS", False);
    unsigned char* motifValue = nullptr;
    Check(XGetWindowProperty(display, childXWindow, motifHints, 0, 5, False,
                             motifHints, &actualType, &actualFormat, &itemCount,
                             &remaining, &motifValue) == Success && motifValue &&
              reinterpret_cast<unsigned long*>(motifValue)[2] == 0,
          "borderless state is reflected in _MOTIF_WM_HINTS");
    if (motifValue) XFree(motifValue);
    Check(jalium_window_set_decorated(child, 1) == JALIUM_OK,
          "restore X11 server decorations at runtime");

    InputCallbackState callbacks;
    jalium_window_set_event_callback(child, InputCallback, &callbacks);
    jalium_window_show(child);
    Check(jalium_test_x11_notify_display_change() == JALIUM_OK &&
              callbacks.monitorsChangedCalls.load(
                  std::memory_order_acquire) > 0,
          "RandR display refresh emits a monitor-change platform event");
    Check(jalium_window_activate(child) == JALIUM_OK,
          "X11 activation request is issued through _NET_ACTIVE_WINDOW");

    XEvent buttonEvent{};
    buttonEvent.xbutton.type = ButtonPress;
    buttonEvent.xbutton.display = display;
    buttonEvent.xbutton.window = childXWindow;
    buttonEvent.xbutton.root = DefaultRootWindow(display);
    buttonEvent.xbutton.button = Button1;
    buttonEvent.xbutton.same_screen = True;
    Check(jalium_window_set_enabled(child, 0) == JALIUM_OK,
          "disable X11 input delivery");
    XSendEvent(display, childXWindow, False, NoEventMask, &buttonEvent);
    XSync(display, False);
    for (int attempt = 0; attempt < 5; ++attempt)
        (void)jalium_platform_poll_events();
    Check(callbacks.mouseDownCalls.load(std::memory_order_acquire) == 0,
          "disabled platform window suppresses mouse input callbacks");
    Check(jalium_window_set_enabled(child, 1) == JALIUM_OK,
          "restore X11 input delivery");
    XSendEvent(display, childXWindow, False, NoEventMask, &buttonEvent);
    XSync(display, False);
    for (int attempt = 0;
         attempt < 20 && callbacks.mouseDownCalls.load(std::memory_order_acquire) == 0;
         ++attempt)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(1ms);
    }
    Check(callbacks.mouseDownCalls.load(std::memory_order_acquire) > 0,
          "re-enabled platform window resumes mouse input callbacks");

    jalium_window_destroy(child);
    jalium_window_destroy(owner);
}

void TestX11PopupAndPortalParent()
{
    const char16_t ownerTitle[] = u"Jalium popup owner";
    JaliumWindowParams ownerParameters{};
    ownerParameters.title = reinterpret_cast<const JaliumUtf16Char*>(ownerTitle);
    ownerParameters.x = 80;
    ownerParameters.y = 70;
    ownerParameters.width = 320;
    ownerParameters.height = 200;
    ownerParameters.style = JALIUM_WINDOW_STYLE_RESIZABLE;
    JaliumPlatformWindow* owner = jalium_window_create(&ownerParameters);
    Check(owner != nullptr, "create X11 popup owner");
    if (!owner) return;

    const char16_t popupTitle[] = u"Jalium ARGB popup";
    JaliumWindowParams popupParameters{};
    popupParameters.title = reinterpret_cast<const JaliumUtf16Char*>(popupTitle);
    popupParameters.x = 24;
    popupParameters.y = 32;
    popupParameters.width = 140;
    popupParameters.height = 90;
    popupParameters.style = JALIUM_WINDOW_STYLE_POPUP |
                            JALIUM_WINDOW_STYLE_TRANSPARENT |
                            JALIUM_WINDOW_STYLE_TOPMOST;
    popupParameters.parentHandle = jalium_window_get_native_handle(owner);
    JaliumPlatformWindow* popup = jalium_window_create(&popupParameters);
    Check(popup != nullptr, "create parent-relative X11 ARGB popup");
    if (!popup)
    {
        jalium_window_destroy(owner);
        return;
    }

    const JaliumSurfaceDescriptor ownerSurface = jalium_window_get_surface(owner);
    const JaliumSurfaceDescriptor popupSurface = jalium_window_get_surface(popup);
    auto* display = reinterpret_cast<Display*>(popupSurface.handle0);
    const Window ownerXWindow = static_cast<Window>(ownerSurface.handle1);
    const Window popupXWindow = static_cast<Window>(popupSurface.handle1);
    XWindowAttributes attributes{};
    Check(XGetWindowAttributes(display, popupXWindow, &attributes) != 0 &&
              attributes.override_redirect == True,
          "X11 popup uses override-redirect");
    XVisualInfo argbVisual{};
    const bool argbAvailable =
        XMatchVisualInfo(display, DefaultScreen(display), 32, TrueColor, &argbVisual) != 0;
    Check(!argbAvailable || attributes.depth == 32,
          "transparent X11 popup selects the 32-bit ARGB visual when available");

    Window transientFor = 0;
    Check(XGetTransientForHint(display, popupXWindow, &transientFor) != 0 &&
              transientFor == ownerXWindow,
          "X11 popup publishes its transient owner");

    Window ignored = 0;
    int ownerRootX = 0;
    int ownerRootY = 0;
    int popupRootX = 0;
    int popupRootY = 0;
    XTranslateCoordinates(display, ownerXWindow, DefaultRootWindow(display),
                          0, 0, &ownerRootX, &ownerRootY, &ignored);
    XTranslateCoordinates(display, popupXWindow, DefaultRootWindow(display),
                          0, 0, &popupRootX, &popupRootY, &ignored);
    Check(popupRootX == ownerRootX + popupParameters.x &&
              popupRootY == ownerRootY + popupParameters.y,
          "X11 popup coordinates are translated from parent to root space");

    const uint32_t opaqueRequired =
        jalium_window_get_portal_parent_handle(popup, nullptr, 0);
    const uint32_t nativeRequired =
        jalium_window_get_portal_parent_handle_for_native_handle(
            static_cast<intptr_t>(popupXWindow), nullptr, 0);
    Check(opaqueRequired > 5 && nativeRequired == opaqueRequired,
          "portal parent ABI reports the required X11 UTF-8 buffer size");
    if (opaqueRequired > 0)
    {
        std::vector<char> opaqueValue(opaqueRequired);
        std::vector<char> nativeValue(nativeRequired);
        Check(jalium_window_get_portal_parent_handle(
                  popup, opaqueValue.data(), opaqueRequired) == opaqueRequired &&
                  jalium_window_get_portal_parent_handle_for_native_handle(
                      static_cast<intptr_t>(popupXWindow), nativeValue.data(),
                      nativeRequired) == nativeRequired &&
                  std::strncmp(opaqueValue.data(), "x11:", 4) == 0 &&
                  std::strcmp(opaqueValue.data(), nativeValue.data()) == 0,
              "opaque and native-handle portal parent entry points agree");
    }

    jalium_window_destroy(popup);
    jalium_window_destroy(owner);
}

struct CallbackState
{
    std::atomic<int> calls{0};
};

struct WindowCallbackState
{
    std::atomic<int> paintCalls{0};
    std::atomic<int> paintCallbackDepth{0};
    std::atomic<int> maxPaintCallbackDepth{0};
    std::atomic<int> reentrantInvalidateUntil{0};
    std::atomic<int> resizeCalls{0};
    std::atomic<int> dpiCalls{0};
    std::atomic<int> monitorsChangedCalls{0};
    float lastDpiScale = 1.0f;
    std::atomic<int> focusCalls{0};
    std::atomic<int> mouseDownCalls{0};
    std::atomic<int> pointerDownCalls{0};
    std::atomic<int> pointerMoveCalls{0};
    std::atomic<int> pointerUpCalls{0};
    std::atomic<int> pointerCancelCalls{0};
    uint32_t lastPointerId = 0;
    int32_t lastPointerType = JALIUM_POINTER_MOUSE;
    float lastPointerX = 0;
    float lastPointerY = 0;
    float lastPointerPressure = 0;
    float lastPointerTiltX = 0;
    float lastPointerTiltY = 0;
    float lastPointerTwist = 0;
    uint32_t lastPointerFlags = 0;
    int32_t lastPointerToolType = JALIUM_POINTER_TOOL_UNKNOWN;
    uint32_t lastPointerButtons = 0;
    std::atomic<int> deleteSurroundingCalls{0};
    int32_t lastDeleteBeforeUtf8Bytes = 0;
    int32_t lastDeleteAfterUtf8Bytes = 0;
    std::atomic<int> dragEnterCalls{0};
    std::atomic<int> dragOverCalls{0};
    std::atomic<int> dropCalls{0};
    std::string dropMime;
    std::string dropData;
};

void WindowCallback(const JaliumPlatformEvent* event, void* userData)
{
    auto* state = static_cast<WindowCallbackState*>(userData);
    if (event->type == JALIUM_EVENT_PAINT)
    {
        const int depth = state->paintCallbackDepth.fetch_add(
            1, std::memory_order_acq_rel) + 1;
        int observed = state->maxPaintCallbackDepth.load(std::memory_order_acquire);
        while (depth > observed &&
               !state->maxPaintCallbackDepth.compare_exchange_weak(
                   observed, depth, std::memory_order_acq_rel)) {}
        const int calls = state->paintCalls.fetch_add(
            1, std::memory_order_acq_rel) + 1;
        if (calls < state->reentrantInvalidateUntil.load(std::memory_order_acquire))
            jalium_window_invalidate(event->window);
        state->paintCallbackDepth.fetch_sub(1, std::memory_order_acq_rel);
    }
    else if (event->type == JALIUM_EVENT_RESIZE)
        state->resizeCalls.fetch_add(1, std::memory_order_release);
    else if (event->type == JALIUM_EVENT_DPI_CHANGED)
    {
        state->lastDpiScale = event->dpiChanged.dpiX / 96.0f;
        state->dpiCalls.fetch_add(1, std::memory_order_release);
    }
    else if (event->type == JALIUM_EVENT_MONITORS_CHANGED)
        state->monitorsChangedCalls.fetch_add(1, std::memory_order_release);
    else if (event->type == JALIUM_EVENT_FOCUS_GAINED)
        state->focusCalls.fetch_add(1, std::memory_order_release);
    else if (event->type == JALIUM_EVENT_MOUSE_DOWN)
        state->mouseDownCalls.fetch_add(1, std::memory_order_release);
    else if (event->type == JALIUM_EVENT_DELETE_SURROUNDING_TEXT)
    {
        state->lastDeleteBeforeUtf8Bytes =
            event->deleteSurrounding.beforeUtf8Bytes;
        state->lastDeleteAfterUtf8Bytes =
            event->deleteSurrounding.afterUtf8Bytes;
        state->deleteSurroundingCalls.fetch_add(1, std::memory_order_release);
    }
    else if (event->type >= JALIUM_EVENT_POINTER_DOWN &&
             event->type <= JALIUM_EVENT_POINTER_CANCEL)
    {
        if (event->type == JALIUM_EVENT_POINTER_DOWN)
            state->pointerDownCalls.fetch_add(1, std::memory_order_release);
        else if (event->type == JALIUM_EVENT_POINTER_MOVE)
            state->pointerMoveCalls.fetch_add(1, std::memory_order_release);
        else if (event->type == JALIUM_EVENT_POINTER_UP)
            state->pointerUpCalls.fetch_add(1, std::memory_order_release);
        else
            state->pointerCancelCalls.fetch_add(1, std::memory_order_release);
        state->lastPointerId = event->pointer.pointerId;
        state->lastPointerType = event->pointer.pointerType;
        state->lastPointerX = event->pointer.x;
        state->lastPointerY = event->pointer.y;
        state->lastPointerPressure = event->pointer.pressure;
        state->lastPointerTiltX = event->pointer.tiltX;
        state->lastPointerTiltY = event->pointer.tiltY;
        state->lastPointerTwist = event->pointer.twist;
        state->lastPointerFlags = event->pointer.flags;
        state->lastPointerToolType = event->pointer.toolType;
        state->lastPointerButtons = event->pointer.buttons;
    }
    else if (event->type == JALIUM_EVENT_DRAG_ENTER)
    {
        state->dragEnterCalls.fetch_add(1, std::memory_order_release);
        jalium_drag_set_effect(event->window, event->drag.sessionId,
                               JALIUM_DRAG_EFFECT_COPY);
    }
    else if (event->type == JALIUM_EVENT_DRAG_OVER)
    {
        state->dragOverCalls.fetch_add(1, std::memory_order_release);
        jalium_drag_set_effect(event->window, event->drag.sessionId,
                               JALIUM_DRAG_EFFECT_COPY);
    }
    else if (event->type == JALIUM_EVENT_DROP)
    {
        state->dropCalls.fetch_add(1, std::memory_order_release);
        state->dropMime = event->drag.dataMimeType ? event->drag.dataMimeType : "";
        if (event->drag.data && event->drag.dataSize)
            state->dropData.assign(
                reinterpret_cast<const char*>(event->drag.data), event->drag.dataSize);
        else
            state->dropData.clear();
        jalium_drag_set_effect(event->window, event->drag.sessionId,
                               JALIUM_DRAG_EFFECT_COPY);
    }
}

void TestDeleteSurroundingCallbackContract()
{
    WindowCallbackState state;
    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_DELETE_SURROUNDING_TEXT;
    event.deleteSurrounding.beforeUtf8Bytes = 7;
    event.deleteSurrounding.afterUtf8Bytes = 4;
    WindowCallback(&event, &state);
    Check(state.deleteSurroundingCalls.load(std::memory_order_acquire) == 1 &&
              state.lastDeleteBeforeUtf8Bytes == 7 &&
              state.lastDeleteAfterUtf8Bytes == 4,
          "event 46 preserves independent before/after UTF-8 byte lengths through the native callback contract");
}

void SendXdndMessage(Display* display, Window destination, Atom message,
                     long value0, long value1 = 0, long value2 = 0,
                     long value3 = 0, long value4 = 0)
{
    XEvent event{};
    event.xclient.type = ClientMessage;
    event.xclient.display = display;
    event.xclient.window = destination;
    event.xclient.message_type = message;
    event.xclient.format = 32;
    event.xclient.data.l[0] = value0;
    event.xclient.data.l[1] = value1;
    event.xclient.data.l[2] = value2;
    event.xclient.data.l[3] = value3;
    event.xclient.data.l[4] = value4;
    XSendEvent(display, destination, False, NoEventMask, &event);
    XFlush(display);
}

void TestExternalXdndUriDrop()
{
    const char16_t title[] = u"XDND external target";
    JaliumWindowParams parameters{};
    parameters.title = reinterpret_cast<const JaliumUtf16Char*>(title);
    parameters.width = 320;
    parameters.height = 200;
    parameters.style = JALIUM_WINDOW_STYLE_RESIZABLE;
    JaliumPlatformWindow* targetWindow = jalium_window_create(&parameters);
    Check(targetWindow != nullptr, "create XDND target window");
    if (!targetWindow) return;

    WindowCallbackState state;
    jalium_window_set_event_callback(targetWindow, WindowCallback, &state);
    jalium_window_show(targetWindow);
    const JaliumSurfaceDescriptor surface = jalium_window_get_surface(targetWindow);
    auto* targetDisplay = reinterpret_cast<Display*>(surface.handle0);
    const Window target = static_cast<Window>(surface.handle1);
    XSync(targetDisplay, False);

    Display* sourceDisplay = XOpenDisplay(nullptr);
    Check(sourceDisplay != nullptr, "open independent XDND source connection");
    if (!sourceDisplay)
    {
        jalium_window_destroy(targetWindow);
        return;
    }
    const Window source = XCreateSimpleWindow(
        sourceDisplay, DefaultRootWindow(sourceDisplay), 0, 0, 1, 1, 0, 0, 0);
    XSelectInput(sourceDisplay, source, PropertyChangeMask);
    const Atom enter = XInternAtom(sourceDisplay, "XdndEnter", False);
    const Atom position = XInternAtom(sourceDisplay, "XdndPosition", False);
    const Atom drop = XInternAtom(sourceDisplay, "XdndDrop", False);
    const Atom finished = XInternAtom(sourceDisplay, "XdndFinished", False);
    const Atom selection = XInternAtom(sourceDisplay, "XdndSelection", False);
    const Atom actionList = XInternAtom(sourceDisplay, "XdndActionList", False);
    const Atom actionCopy = XInternAtom(sourceDisplay, "XdndActionCopy", False);
    const Atom uriList = XInternAtom(sourceDisplay, "text/uri-list", False);
    XChangeProperty(sourceDisplay, source, actionList, XA_ATOM, 32,
                    PropModeReplace,
                    reinterpret_cast<const unsigned char*>(&actionCopy), 1);
    XSetSelectionOwner(sourceDisplay, selection, source, CurrentTime);

    Window child = 0;
    int rootX = 0;
    int rootY = 0;
    XTranslateCoordinates(targetDisplay, target, DefaultRootWindow(targetDisplay),
                          20, 30, &rootX, &rootY, &child);
    const unsigned long packed =
        (static_cast<unsigned long>(rootX) & 0xfffful) << 16 |
        (static_cast<unsigned long>(rootY) & 0xfffful);
    SendXdndMessage(sourceDisplay, target, enter, static_cast<long>(source),
                    static_cast<long>(5ul << 24), static_cast<long>(uriList));
    SendXdndMessage(sourceDisplay, target, position, static_cast<long>(source), 0,
                    static_cast<long>(packed), CurrentTime, static_cast<long>(actionCopy));
    for (int attempt = 0; attempt < 100 && state.dragOverCalls.load() == 0; ++attempt)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(1ms);
    }
    Check(state.dragEnterCalls.load() == 1, "external XDND emits drag enter");
    Check(state.dragOverCalls.load() > 0, "external XDND emits drag over");

    SendXdndMessage(sourceDisplay, target, drop, static_cast<long>(source), 0, CurrentTime);
    const std::string payload =
        "# test payload\r\nfile:///tmp/Jalium%20XDND.txt\r\n";
    bool selectionServed = false;
    bool finishedReceived = false;
    for (int attempt = 0; attempt < 500 && !finishedReceived; ++attempt)
    {
        (void)jalium_platform_poll_events();
        while (XPending(sourceDisplay))
        {
            XEvent event{};
            XNextEvent(sourceDisplay, &event);
            if (event.type == SelectionRequest)
            {
                const XSelectionRequestEvent& request = event.xselectionrequest;
                XSelectionEvent response{};
                response.type = SelectionNotify;
                response.display = sourceDisplay;
                response.requestor = request.requestor;
                response.selection = request.selection;
                response.target = request.target;
                response.time = request.time;
                response.property = request.property;
                XChangeProperty(sourceDisplay, request.requestor, request.property,
                                uriList, 8, PropModeReplace,
                                reinterpret_cast<const unsigned char*>(payload.data()),
                                static_cast<int>(payload.size()));
                XSendEvent(sourceDisplay, request.requestor, False, NoEventMask,
                           reinterpret_cast<XEvent*>(&response));
                XFlush(sourceDisplay);
                selectionServed = true;
            }
            else if (event.type == ClientMessage &&
                     event.xclient.message_type == finished)
            {
                finishedReceived = true;
            }
        }
        std::this_thread::sleep_for(1ms);
    }

    Check(selectionServed, "external URI selection was requested and served");
    Check(state.dropCalls.load() == 1, "external URI payload emits drop");
    Check(state.dropMime == "text/uri-list", "external URI MIME is preserved");
    Check(state.dropData == payload, "external URI bytes are preserved");
    Check(finishedReceived, "external source receives XdndFinished");

    XDestroyWindow(sourceDisplay, source);
    XCloseDisplay(sourceDisplay);
    jalium_window_destroy(targetWindow);
}

struct DragSourceCallbackState
{
    std::atomic<int> feedbackCalls{0};
    std::atomic<int> queryCalls{0};
    std::atomic<bool> sawCopyFeedback{false};
};

void TestDragFeedback(uint32_t effect, void* userData)
{
    auto* state = static_cast<DragSourceCallbackState*>(userData);
    state->feedbackCalls.fetch_add(1, std::memory_order_release);
    if ((effect & JALIUM_DRAG_EFFECT_COPY) != 0)
        state->sawCopyFeedback.store(true, std::memory_order_release);
}

JaliumDragContinueAction TestQueryContinueDrag(
    uint32_t keyStates, int32_t escapePressed, void* userData)
{
    auto* state = static_cast<DragSourceCallbackState*>(userData);
    state->queryCalls.fetch_add(1, std::memory_order_release);
    if (escapePressed != 0) return JALIUM_DRAG_CANCEL;
    return (keyStates & 0x01u) != 0
        ? JALIUM_DRAG_CONTINUE
        : JALIUM_DRAG_DROP;
}

void TestInProcessXdndSourceRoundTrip()
{
    const char16_t title[] = u"XDND in-process";
    JaliumWindowParams parameters{};
    parameters.title = reinterpret_cast<const JaliumUtf16Char*>(title);
    parameters.width = 300;
    parameters.height = 180;
    parameters.style = JALIUM_WINDOW_STYLE_RESIZABLE;
    JaliumPlatformWindow* window = jalium_window_create(&parameters);
    Check(window != nullptr, "create in-process XDND window");
    if (!window) return;

    WindowCallbackState state;
    jalium_window_set_event_callback(window, WindowCallback, &state);
    jalium_window_show(window);
    const JaliumSurfaceDescriptor surface = jalium_window_get_surface(window);
    auto* display = reinterpret_cast<Display*>(surface.handle0);
    const Window xwindow = static_cast<Window>(surface.handle1);
    XWarpPointer(display, None, xwindow, 0, 0, 0, 0, 40, 40);
    XSync(display, False);

    const std::string text = "Jalium in-process drag";
    const JaliumDragDataItem item{
        "text/plain;charset=utf-8",
        reinterpret_cast<const uint8_t*>(text.data()),
        static_cast<uint32_t>(text.size()) };
    const std::array<uint8_t, 4 * 4 * 4> dragPixels = []
    {
        std::array<uint8_t, 4 * 4 * 4> pixels{};
        for (size_t offset = 0; offset < pixels.size(); offset += 4)
        {
            pixels[offset + 1] = 0x80;
            pixels[offset + 2] = 0xff;
            pixels[offset + 3] = 0xd0;
        }
        return pixels;
    }();
    const JaliumDragImage dragImage{
        dragPixels.data(), 4, 4, 16, 1, 1 };
    DragSourceCallbackState sourceCallbacks;
    uint32_t performed = JALIUM_DRAG_EFFECT_NONE;
    const JaliumResult result = jalium_drag_begin_with_image(
        window, &item, 1, JALIUM_DRAG_EFFECT_COPY,
        TestDragFeedback, TestQueryContinueDrag, &sourceCallbacks,
        &dragImage, &performed);
    Check(result == JALIUM_OK, "in-process drag with X11 ARGB image completes");
    Check(performed == JALIUM_DRAG_EFFECT_COPY, "in-process drag negotiates copy");
    Check(sourceCallbacks.feedbackCalls.load(std::memory_order_acquire) > 0 &&
              sourceCallbacks.sawCopyFeedback.load(std::memory_order_acquire),
          "extended drag reports target-selected feedback");
    Check(sourceCallbacks.queryCalls.load(std::memory_order_acquire) > 0,
          "extended drag routes QueryContinueDrag decisions");
    Check(state.dragEnterCalls.load() > 0 && state.dragOverCalls.load() > 0,
          "in-process drag traverses enter and over callbacks");
    Check(state.dropCalls.load() == 1, "in-process drag emits drop callback");
    Check(state.dropData == text, "in-process text payload round-trips");
    jalium_window_destroy(window);
}

int RunWaylandDndSmoke()
{
    Check(jalium_platform_get_current() == JALIUM_PLATFORM_LINUX_WAYLAND,
          "Wayland DND smoke selected Wayland");
    const char16_t title[] = u"Jalium Wayland DND Smoke";
    JaliumWindowParams parameters{};
    parameters.title = reinterpret_cast<const JaliumUtf16Char*>(title);
    parameters.width = 480;
    parameters.height = 320;
    parameters.style = JALIUM_WINDOW_STYLE_RESIZABLE;
    JaliumPlatformWindow* window = jalium_window_create(&parameters);
    Check(window != nullptr, "create Wayland DND smoke window");
    if (!window) return 1;

    WindowCallbackState state;
    jalium_window_set_event_callback(window, WindowCallback, &state);
    jalium_window_show(window);

    void* softwareModule = dlopen(
        "libjalium.native.software.so", RTLD_NOW | RTLD_GLOBAL);
    auto softwareInit = softwareModule
        ? reinterpret_cast<void (*)()>(dlsym(softwareModule, "jalium_software_init"))
        : nullptr;
    Check(softwareInit != nullptr, "load software backend for mapped Wayland surface");
    if (softwareInit) softwareInit();
    JaliumContext* context = softwareInit
        ? jalium_context_create(JALIUM_BACKEND_SOFTWARE) : nullptr;
    const JaliumSurfaceDescriptor surface = jalium_window_get_surface(window);
    JaliumRenderTarget* renderTarget = context
        ? jalium_render_target_create_for_surface(
              context, &surface, parameters.width, parameters.height)
        : nullptr;
    Check(renderTarget != nullptr, "create mapped Wayland software target for DND");
    bool presented = false;
    for (int attempt = 0; renderTarget && attempt < 250 && !presented; ++attempt)
    {
        (void)jalium_platform_poll_events();
        if (jalium_render_target_begin_draw(renderTarget) == JALIUM_OK)
        {
            jalium_render_target_clear(renderTarget, 0.08f, 0.16f, 0.3f, 1.0f);
            presented = jalium_render_target_end_draw(renderTarget) == JALIUM_OK;
        }
        if (!presented) std::this_thread::sleep_for(2ms);
    }
    Check(presented, "present mapped Wayland DND surface");

    const auto inputDeadline = std::chrono::steady_clock::now() + 10s;
    while (state.mouseDownCalls.load(std::memory_order_acquire) == 0 &&
           std::chrono::steady_clock::now() < inputDeadline)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(2ms);
    }
    Check(state.mouseDownCalls.load() > 0,
          "nested Weston delivered synthetic pointer press");

    const std::string uriList = "file:///tmp/Jalium-Wayland-DND.txt\r\n";
    const JaliumDragDataItem item{
        "text/uri-list",
        reinterpret_cast<const uint8_t*>(uriList.data()),
        static_cast<uint32_t>(uriList.size()) };
    const std::array<uint8_t, 4 * 4 * 4> dragPixels = []
    {
        std::array<uint8_t, 4 * 4 * 4> pixels{};
        for (size_t offset = 0; offset < pixels.size(); offset += 4)
        {
            pixels[offset] = 0xff;
            pixels[offset + 2] = 0x40;
            pixels[offset + 3] = 0xd0;
        }
        return pixels;
    }();
    const JaliumDragImage dragImage{
        dragPixels.data(), 4, 4, 16, 2, 2 };
    uint32_t performed = JALIUM_DRAG_EFFECT_NONE;
    const JaliumResult result = state.mouseDownCalls.load() > 0
        ? jalium_drag_begin_with_image(
              window, &item, 1, JALIUM_DRAG_EFFECT_COPY,
              nullptr, nullptr, nullptr, &dragImage, &performed)
        : JALIUM_ERROR_INVALID_STATE;
    Check(result == JALIUM_OK,
          "Wayland wl_data_device_start_drag with icon surface completes");
    Check(performed == JALIUM_DRAG_EFFECT_COPY,
          "Wayland source receives negotiated copy action");
    Check(state.dragEnterCalls.load() > 0 && state.dragOverCalls.load() > 0,
          "Wayland data offer emits enter and motion");
    Check(state.dropCalls.load() == 1, "Wayland data offer emits drop");
    Check(state.dropMime == "text/uri-list", "Wayland URI MIME is preserved");
    Check(state.dropData == uriList, "Wayland URI bytes round-trip in process");
    if (renderTarget) jalium_render_target_destroy(renderTarget);
    if (context) jalium_context_destroy(context);
    if (softwareModule) dlclose(softwareModule);
    jalium_window_destroy(window);
    return g_failures == 0 ? 0 : 1;
}

void QuitCallback(void* userData)
{
    auto* state = static_cast<CallbackState*>(userData);
    state->calls.fetch_add(1, std::memory_order_release);
    jalium_platform_quit(0);
}

struct SelfDestroyDispatcherState
{
    std::atomic<int> calls{0};
    JaliumDispatcher* dispatcher = nullptr;
};

void SelfDestroyDispatcherCallback(void* userData)
{
    auto* state = static_cast<SelfDestroyDispatcherState*>(userData);
    state->calls.fetch_add(1, std::memory_order_release);
    JaliumDispatcher* dispatcher = state->dispatcher;
    state->dispatcher = nullptr;
    jalium_dispatcher_destroy(dispatcher);
}

void TestUtf16WindowTitles()
{
    static_assert(sizeof(JaliumUtf16Char) == 2);
    static_assert(sizeof(char16_t) == 2);

    const char16_t title[] = u"Jalium Linux \u4E2D\u6587 \U0001F680";
    JaliumWindowParams parameters{};
    parameters.title = reinterpret_cast<const JaliumUtf16Char*>(title);
    parameters.x = JALIUM_DEFAULT_POS;
    parameters.y = JALIUM_DEFAULT_POS;
    parameters.width = 320;
    parameters.height = 200;
    parameters.style = JALIUM_WINDOW_STYLE_DEFAULT;

    JaliumPlatformWindow* window = jalium_window_create(&parameters);
    Check(window != nullptr, "create X11 window for UTF-16 title test");
    if (!window) return;

    const JaliumSurfaceDescriptor surface = jalium_window_get_surface(window);
    auto* display = reinterpret_cast<Display*>(surface.handle0);
    const auto xwindow = static_cast<Window>(surface.handle1);
    XSync(display, False);

    const std::string expected =
        "Jalium Linux " "\xE4\xB8\xAD" "\xE6\x96\x87" " " "\xF0\x9F\x9A\x80";
    Check(ReadNetWmName(display, xwindow) == expected,
          "surrogate pairs and BMP characters convert to UTF-8");

    const JaliumUtf16Char malformed[] = {0xD800u, static_cast<JaliumUtf16Char>('X'), 0};
    jalium_window_set_title(window, malformed);
    XSync(display, False);
    Check(ReadNetWmName(display, xwindow) == "\xEF\xBF\xBD" "X",
          "invalid UTF-16 is replaced instead of reading 32-bit wchar_t data");

    jalium_window_destroy(window);
}

void TestClipboardUtf16AndExternalSelection()
{
    const char16_t ownedText[] = u"Jalium clipboard \u4E2D\u6587 \U0001F680";
    Check(jalium_clipboard_set_text(
              reinterpret_cast<const JaliumUtf16Char*>(ownedText)) == JALIUM_OK,
          "set X11 CLIPBOARD ownership");
    JaliumUtf16Char* copy = nullptr;
    Check(jalium_clipboard_get_text(&copy) == JALIUM_OK && copy,
          "get process-owned clipboard text");
    if (copy)
    {
        Check(std::u16string(reinterpret_cast<char16_t*>(copy)) == ownedText,
              "clipboard UTF-16 preserves BMP and surrogate pairs");
        jalium_platform_free(copy);
    }

    const std::string externalUtf8 =
        "External " "\xE4\xB8\xAD" "\xE6\x96\x87" " " "\xF0\x9F\x98\x80";
    std::atomic<bool> ownerReady{false};
    std::atomic<bool> stopOwner{false};
    std::thread externalOwner([&]
    {
        Display* display = XOpenDisplay(nullptr);
        if (!display) { ownerReady.store(true); return; }
        Window window = XCreateSimpleWindow(
            display, DefaultRootWindow(display), 0, 0, 1, 1, 0, 0, 0);
        const Atom clipboard = XInternAtom(display, "CLIPBOARD", False);
        const Atom targetsAtom = XInternAtom(display, "TARGETS", False);
        const Atom utf8 = XInternAtom(display, "UTF8_STRING", False);
        XSetSelectionOwner(display, clipboard, window, CurrentTime);
        XSync(display, False);
        ownerReady.store(true, std::memory_order_release);
        while (!stopOwner.load(std::memory_order_acquire))
        {
            while (XPending(display))
            {
                XEvent event{};
                XNextEvent(display, &event);
                if (event.type != SelectionRequest) continue;
                const XSelectionRequestEvent& request = event.xselectionrequest;
                XSelectionEvent response{};
                response.type = SelectionNotify;
                response.display = display;
                response.requestor = request.requestor;
                response.selection = request.selection;
                response.target = request.target;
                response.time = request.time;
                response.property = None;
                const Atom property = request.property != None
                    ? request.property : request.target;
                if (request.target == targetsAtom)
                {
                    const Atom targets[] = {targetsAtom, utf8};
                    XChangeProperty(display, request.requestor, property, XA_ATOM, 32,
                                    PropModeReplace,
                                    reinterpret_cast<const unsigned char*>(targets), 2);
                    response.property = property;
                }
                else if (request.target == utf8)
                {
                    XChangeProperty(
                        display, request.requestor, property, utf8, 8,
                        PropModeReplace,
                        reinterpret_cast<const unsigned char*>(externalUtf8.data()),
                        static_cast<int>(externalUtf8.size()));
                    response.property = property;
                }
                XSendEvent(display, request.requestor, False, NoEventMask,
                           reinterpret_cast<XEvent*>(&response));
                XFlush(display);
            }
            struct pollfd descriptor{ConnectionNumber(display), POLLIN, 0};
            (void)poll(&descriptor, 1, 5);
        }
        XDestroyWindow(display, window);
        XCloseDisplay(display);
    });
    while (!ownerReady.load(std::memory_order_acquire)) std::this_thread::yield();
    copy = nullptr;
    Check(jalium_clipboard_get_text(&copy) == JALIUM_OK && copy,
          "read UTF8_STRING from an external X11 selection owner");
    if (copy)
    {
        Check(std::u16string(reinterpret_cast<char16_t*>(copy)) ==
                  u"External \u4E2D\u6587 \U0001F600",
              "external UTF-8 clipboard converts to fixed-width UTF-16 ABI");
        jalium_platform_free(copy);
    }
    stopOwner.store(true, std::memory_order_release);
    externalOwner.join();

    // Re-acquire ownership and verify a separate X11 client receives our
    // UTF8_STRING through SelectionRequest/SelectionNotify.
    Check(jalium_clipboard_set_text(
              reinterpret_cast<const JaliumUtf16Char*>(ownedText)) == JALIUM_OK,
          "re-acquire X11 clipboard after SelectionClear");
    std::atomic<bool> requestDone{false};
    std::string received;
    std::thread externalReader([&]
    {
        Display* display = XOpenDisplay(nullptr);
        if (!display) { requestDone.store(true); return; }
        Window window = XCreateSimpleWindow(
            display, DefaultRootWindow(display), 0, 0, 1, 1, 0, 0, 0);
        const Atom clipboard = XInternAtom(display, "CLIPBOARD", False);
        const Atom utf8 = XInternAtom(display, "UTF8_STRING", False);
        const Atom property = XInternAtom(display, "JALIUM_TEST_CLIPBOARD", False);
        XConvertSelection(display, clipboard, utf8, property, window, CurrentTime);
        XFlush(display);
        for (int attempt = 0; attempt < 400 && !requestDone.load(); ++attempt)
        {
            if (XPending(display))
            {
                XEvent event{};
                XNextEvent(display, &event);
                if (event.type == SelectionNotify && event.xselection.property != None)
                {
                    Atom type = None;
                    int format = 0;
                    unsigned long count = 0, remaining = 0;
                    unsigned char* value = nullptr;
                    if (XGetWindowProperty(
                            display, window, property, 0, 4096, True, utf8,
                            &type, &format, &count, &remaining, &value) == Success &&
                        type == utf8 && format == 8 && value)
                        received.assign(reinterpret_cast<char*>(value), count);
                    if (value) XFree(value);
                    requestDone.store(true, std::memory_order_release);
                }
            }
            struct pollfd descriptor{ConnectionNumber(display), POLLIN, 0};
            (void)poll(&descriptor, 1, 5);
        }
        requestDone.store(true, std::memory_order_release);
        XDestroyWindow(display, window);
        XCloseDisplay(display);
    });
    for (int attempt = 0; attempt < 500 && !requestDone.load(std::memory_order_acquire); ++attempt)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(1ms);
    }
    externalReader.join();
    Check(received == "Jalium clipboard " "\xE4\xB8\xAD" "\xE6\x96\x87"
                      " " "\xF0\x9F\x9A\x80",
          "serve UTF8_STRING to an external X11 selection requestor");
}

void TestClipboardMultiFormatAndIncr()
{
    const std::string html = "<p>Jalium <b>HTML</b></p>";
    const std::string uriList =
        "file:///tmp/jalium%20clipboard.txt\r\n"
        "file:///tmp/jalium-%E4%B8%AD%E6%96%87.txt\r\n";
    std::vector<uint8_t> largePayload(256u * 1024u + 37u);
    for (size_t index = 0; index < largePayload.size(); ++index)
        largePayload[index] = static_cast<uint8_t>((index * 31u + 17u) & 0xffu);

    const JaliumClipboardDataItem items[] = {
        {"text/html", reinterpret_cast<const uint8_t*>(html.data()),
         static_cast<uint32_t>(html.size())},
        {"text/uri-list", reinterpret_cast<const uint8_t*>(uriList.data()),
         static_cast<uint32_t>(uriList.size())},
        {"application/vnd.jalium.incr-test", largePayload.data(),
         static_cast<uint32_t>(largePayload.size())},
    };
    Check(jalium_clipboard_set_data(items, 3) == JALIUM_OK,
          "atomically publish HTML, URI list, and custom clipboard MIME data");

    char* formatList = nullptr;
    Check(jalium_clipboard_get_formats(&formatList) == JALIUM_OK && formatList,
          "query process-owned multi-format clipboard targets");
    if (formatList)
    {
        const std::string formats(formatList);
        Check(formats.find("text/html") != std::string::npos &&
                  formats.find("text/uri-list") != std::string::npos &&
                  formats.find("application/vnd.jalium.incr-test") != std::string::npos,
              "owned clipboard format list preserves every MIME representation");
        jalium_platform_free(formatList);
    }

    uint8_t* ownedCopy = nullptr;
    uint32_t ownedSize = 0;
    Check(jalium_clipboard_get_data(
              "application/vnd.jalium.incr-test", &ownedCopy, &ownedSize) == JALIUM_OK &&
              ownedCopy && ownedSize == largePayload.size() &&
              std::memcmp(ownedCopy, largePayload.data(), largePayload.size()) == 0,
          "process-owned custom MIME data round-trips without truncation");
    if (ownedCopy) jalium_platform_free(ownedCopy);

    // A separate X11 client requests the large custom target. This must take the
    // owner-side ICCCM INCR path rather than relying on the in-process shortcut.
    std::atomic<bool> readerDone{false};
    std::vector<uint8_t> readerBytes;
    std::thread externalReader([&]
    {
        Display* display = XOpenDisplay(nullptr);
        if (!display) { readerDone.store(true, std::memory_order_release); return; }
        Window window = XCreateSimpleWindow(
            display, DefaultRootWindow(display), 0, 0, 1, 1, 0, 0, 0);
        XSelectInput(display, window, PropertyChangeMask);
        const Atom clipboard = XInternAtom(display, "CLIPBOARD", False);
        const Atom target = XInternAtom(display, "application/vnd.jalium.incr-test", False);
        const Atom property = XInternAtom(display, "JALIUM_INCR_READER", False);
        const Atom incr = XInternAtom(display, "INCR", False);
        XConvertSelection(display, clipboard, target, property, window, CurrentTime);
        XFlush(display);

        bool selectionReceived = false;
        const auto deadline = std::chrono::steady_clock::now() + 4s;
        while (std::chrono::steady_clock::now() < deadline && !readerDone.load())
        {
            while (XPending(display))
            {
                XEvent event{};
                XNextEvent(display, &event);
                if (event.type != SelectionNotify || selectionReceived)
                    continue;
                selectionReceived = true;
                if (event.xselection.property == None)
                {
                    readerDone.store(true, std::memory_order_release);
                    break;
                }

                Atom type = None;
                int format = 0;
                unsigned long count = 0;
                unsigned long remaining = 0;
                unsigned char* value = nullptr;
                if (XGetWindowProperty(
                        display, window, property, 0, 0x1fffffff, False,
                        AnyPropertyType, &type, &format, &count, &remaining,
                        &value) != Success)
                {
                    if (value) XFree(value);
                    readerDone.store(true, std::memory_order_release);
                    break;
                }

                if (type != incr)
                {
                    if (value && format == 8)
                        readerBytes.assign(value, value + count);
                    if (value) XFree(value);
                    XDeleteProperty(display, window, property);
                    readerDone.store(true, std::memory_order_release);
                    break;
                }

                if (value) XFree(value);
                XEvent stale{};
                while (XCheckTypedWindowEvent(display, window, PropertyNotify, &stale)) {}
                XDeleteProperty(display, window, property);
                XFlush(display);
                while (std::chrono::steady_clock::now() < deadline && !readerDone.load())
                {
                    XEvent chunkEvent{};
                    if (!XCheckTypedWindowEvent(display, window, PropertyNotify, &chunkEvent))
                    {
                        struct pollfd descriptor{ConnectionNumber(display), POLLIN, 0};
                        (void)poll(&descriptor, 1, 5);
                        continue;
                    }
                    if (chunkEvent.xproperty.atom != property ||
                        chunkEvent.xproperty.state != PropertyNewValue)
                        continue;

                    Atom chunkType = None;
                    int chunkFormat = 0;
                    unsigned long chunkCount = 0;
                    unsigned long chunkRemaining = 0;
                    unsigned char* chunk = nullptr;
                    if (XGetWindowProperty(
                            display, window, property, 0, 0x1fffffff, True,
                            AnyPropertyType, &chunkType, &chunkFormat,
                            &chunkCount, &chunkRemaining, &chunk) != Success)
                    {
                        if (chunk) XFree(chunk);
                        readerDone.store(true, std::memory_order_release);
                        break;
                    }
                    if (chunkCount == 0)
                    {
                        if (chunk) XFree(chunk);
                        readerDone.store(true, std::memory_order_release);
                        break;
                    }
                    if (chunk && chunkFormat == 8)
                        readerBytes.insert(readerBytes.end(), chunk, chunk + chunkCount);
                    if (chunk) XFree(chunk);
                }
            }
            struct pollfd descriptor{ConnectionNumber(display), POLLIN, 0};
            (void)poll(&descriptor, 1, 5);
        }
        readerDone.store(true, std::memory_order_release);
        XDestroyWindow(display, window);
        XCloseDisplay(display);
    });
    for (int attempt = 0;
         attempt < 5000 && !readerDone.load(std::memory_order_acquire);
         ++attempt)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(1ms);
    }
    externalReader.join();
    Check(readerBytes == largePayload,
          "external X11 client receives the complete owner-side INCR payload");

    // Now reverse the direction: an external owner serves TARGETS and the same
    // large custom representation through INCR while Jalium is the requestor.
    std::atomic<bool> ownerReady{false};
    std::atomic<bool> stopOwner{false};
    std::thread externalOwner([&]
    {
        Display* display = XOpenDisplay(nullptr);
        if (!display)
        {
            ownerReady.store(true, std::memory_order_release);
            return;
        }
        Window window = XCreateSimpleWindow(
            display, DefaultRootWindow(display), 0, 0, 1, 1, 0, 0, 0);
        const Atom clipboard = XInternAtom(display, "CLIPBOARD", False);
        const Atom targetsAtom = XInternAtom(display, "TARGETS", False);
        const Atom htmlAtom = XInternAtom(display, "text/html", False);
        const Atom customAtom = XInternAtom(
            display, "application/vnd.jalium.external-incr", False);
        const Atom incr = XInternAtom(display, "INCR", False);
        Window requestor = None;
        Atom property = None;
        size_t offset = 0;
        bool transferActive = false;

        XSetSelectionOwner(display, clipboard, window, CurrentTime);
        XSync(display, False);
        ownerReady.store(true, std::memory_order_release);
        while (!stopOwner.load(std::memory_order_acquire) || transferActive)
        {
            while (XPending(display))
            {
                XEvent event{};
                XNextEvent(display, &event);
                if (event.type == SelectionRequest)
                {
                    const XSelectionRequestEvent& request = event.xselectionrequest;
                    XSelectionEvent response{};
                    response.type = SelectionNotify;
                    response.display = display;
                    response.requestor = request.requestor;
                    response.selection = request.selection;
                    response.target = request.target;
                    response.time = request.time;
                    response.property = None;
                    const Atom responseProperty = request.property != None
                        ? request.property : request.target;
                    if (request.target == targetsAtom)
                    {
                        const Atom targets[] = {targetsAtom, htmlAtom, customAtom};
                        XChangeProperty(
                            display, request.requestor, responseProperty,
                            XA_ATOM, 32, PropModeReplace,
                            reinterpret_cast<const unsigned char*>(targets), 3);
                        response.property = responseProperty;
                    }
                    else if (request.target == htmlAtom)
                    {
                        XChangeProperty(
                            display, request.requestor, responseProperty,
                            htmlAtom, 8, PropModeReplace,
                            reinterpret_cast<const unsigned char*>(html.data()),
                            static_cast<int>(html.size()));
                        response.property = responseProperty;
                    }
                    else if (request.target == customAtom)
                    {
                        requestor = request.requestor;
                        property = responseProperty;
                        offset = 0;
                        transferActive = true;
                        XSelectInput(display, requestor, PropertyChangeMask);
                        const unsigned long totalBytes =
                            static_cast<unsigned long>(largePayload.size());
                        XChangeProperty(
                            display, requestor, property, incr, 32,
                            PropModeReplace,
                            reinterpret_cast<const unsigned char*>(&totalBytes), 1);
                        response.property = property;
                    }
                    XSendEvent(display, request.requestor, False, NoEventMask,
                               reinterpret_cast<XEvent*>(&response));
                    XFlush(display);
                }
                else if (event.type == PropertyNotify && transferActive &&
                         event.xproperty.window == requestor &&
                         event.xproperty.atom == property &&
                         event.xproperty.state == PropertyDelete)
                {
                    constexpr size_t chunkSize = 48u * 1024u;
                    if (offset < largePayload.size())
                    {
                        const size_t count = std::min(
                            chunkSize, largePayload.size() - offset);
                        XChangeProperty(
                            display, requestor, property, customAtom, 8,
                            PropModeReplace, largePayload.data() + offset,
                            static_cast<int>(count));
                        offset += count;
                    }
                    else
                    {
                        XChangeProperty(
                            display, requestor, property, customAtom, 8,
                            PropModeReplace, nullptr, 0);
                        transferActive = false;
                    }
                    XFlush(display);
                }
            }
            struct pollfd descriptor{ConnectionNumber(display), POLLIN, 0};
            (void)poll(&descriptor, 1, 5);
        }
        XDestroyWindow(display, window);
        XCloseDisplay(display);
    });
    while (!ownerReady.load(std::memory_order_acquire))
        std::this_thread::yield();

    formatList = nullptr;
    Check(jalium_clipboard_get_formats(&formatList) == JALIUM_OK && formatList,
          "query TARGETS from an external multi-format X11 owner");
    if (formatList)
    {
        const std::string formats(formatList);
        Check(formats.find("text/html") != std::string::npos &&
                  formats.find("application/vnd.jalium.external-incr") != std::string::npos,
              "external TARGETS map to MIME names without loss");
        jalium_platform_free(formatList);
    }

    uint8_t* externalCopy = nullptr;
    uint32_t externalSize = 0;
    Check(jalium_clipboard_get_data(
              "application/vnd.jalium.external-incr",
              &externalCopy, &externalSize) == JALIUM_OK &&
              externalCopy && externalSize == largePayload.size() &&
              std::memcmp(externalCopy, largePayload.data(), largePayload.size()) == 0,
          "Jalium requestor receives every chunk from an external INCR owner");
    if (externalCopy) jalium_platform_free(externalCopy);
    stopOwner.store(true, std::memory_order_release);
    externalOwner.join();

    Check(jalium_clipboard_clear() == JALIUM_OK,
          "native clipboard clear releases all multi-format representations");
    formatList = nullptr;
    Check(jalium_clipboard_get_formats(&formatList) == JALIUM_OK && formatList &&
              std::strlen(formatList) == 0,
          "cleared native clipboard advertises no formats");
    if (formatList) jalium_platform_free(formatList);
}

void TestDispatcherInBlockingLoop()
{
    JaliumDispatcher* dispatcher = nullptr;
    Check(jalium_dispatcher_create(&dispatcher) == JALIUM_OK && dispatcher,
          "create dispatcher");
    if (!dispatcher) return;

    CallbackState state;
    jalium_dispatcher_set_callback(dispatcher, QuitCallback, &state);
    std::thread wakeThread([dispatcher, &state]
    {
        std::this_thread::sleep_for(10ms);
        jalium_dispatcher_wake(dispatcher);
        for (int attempt = 0; attempt < 400 && state.calls.load(std::memory_order_acquire) == 0; ++attempt)
            std::this_thread::sleep_for(5ms);
        if (state.calls.load(std::memory_order_acquire) == 0)
            jalium_platform_quit(91);
    });

    const int exitCode = jalium_platform_run_message_loop();
    wakeThread.join();
    Check(exitCode == 0, "dispatcher wakes blocking epoll loop before watchdog");
    Check(state.calls.load(std::memory_order_acquire) == 1,
          "dispatcher callback runs exactly once for a coalesced wake");
    jalium_dispatcher_destroy(dispatcher);
}

void TestDispatcherInPollingLoopAndSelfDestroy()
{
    SelfDestroyDispatcherState state;
    Check(jalium_dispatcher_create(&state.dispatcher) == JALIUM_OK && state.dispatcher,
          "create self-destroying dispatcher");
    if (!state.dispatcher) return;

    jalium_dispatcher_set_callback(
        state.dispatcher, SelfDestroyDispatcherCallback, &state);
    jalium_dispatcher_wake(state.dispatcher);

    for (int attempt = 0; attempt < 100 && state.calls.load(std::memory_order_acquire) == 0; ++attempt)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(1ms);
    }

    Check(state.calls.load(std::memory_order_acquire) == 1,
          "poll_events invokes dispatcher callback and callback may destroy itself");
}

void TestTimerCallbackAndWaitModes()
{
    JaliumTimer* callbackTimer = nullptr;
    Check(jalium_timer_create(&callbackTimer) == JALIUM_OK && callbackTimer,
          "create callback timer");
    if (!callbackTimer) return;

    CallbackState callbackState;
    jalium_timer_set_callback(callbackTimer, QuitCallback, &callbackState);
    jalium_timer_arm(callbackTimer, 5'000);
    std::thread watchdog([&callbackState]
    {
        for (int attempt = 0; attempt < 400 && callbackState.calls.load(std::memory_order_acquire) == 0; ++attempt)
            std::this_thread::sleep_for(5ms);
        if (callbackState.calls.load(std::memory_order_acquire) == 0)
            jalium_platform_quit(92);
    });
    const int timerLoopExitCode = jalium_platform_run_message_loop();
    watchdog.join();
    Check(timerLoopExitCode == 0, "timer wakes blocking epoll loop before watchdog");
    Check(callbackState.calls.load(std::memory_order_acquire) == 1,
          "timerfd registered with epoll invokes its callback exactly once");
    jalium_timer_set_callback(callbackTimer, nullptr, nullptr);
    jalium_timer_arm(callbackTimer, 5'000);
    Check(jalium_timer_wait(callbackTimer, 500) == 1,
          "removing a timer callback unregisters it from epoll for timer_wait");
    jalium_timer_destroy(callbackTimer);

    JaliumTimer* waitTimer = nullptr;
    Check(jalium_timer_create(&waitTimer) == JALIUM_OK && waitTimer,
          "create wait timer");
    if (!waitTimer) return;
    jalium_timer_arm(waitTimer, 5'000);
    Check(jalium_timer_wait(waitTimer, 500) == 1,
          "timer without callback remains available to timer_wait");
    jalium_timer_destroy(waitTimer);
}

size_t CountOpenFileDescriptors()
{
    size_t count = 0;
    for (const auto& entry : std::filesystem::directory_iterator("/proc/self/fd"))
    {
        (void)entry;
        ++count;
    }
    return count;
}

void TestEventSourceDestructionClosesFileDescriptors()
{
    const size_t before = CountOpenFileDescriptors();
    for (int index = 0; index < 64; ++index)
    {
        JaliumDispatcher* dispatcher = nullptr;
        JaliumTimer* timer = nullptr;
        Check(jalium_dispatcher_create(&dispatcher) == JALIUM_OK && dispatcher,
              "create dispatcher for fd cleanup test");
        Check(jalium_timer_create(&timer) == JALIUM_OK && timer,
              "create timer for fd cleanup test");
        jalium_dispatcher_destroy(dispatcher);
        jalium_timer_destroy(timer);
    }
    Check(CountOpenFileDescriptors() == before,
          "dispatcher/timer create-destroy cycles do not leak Linux fds");
}

int RunWaylandSmoke(bool testVulkan)
{
    Check(jalium_platform_get_current() == JALIUM_PLATFORM_LINUX_WAYLAND,
          "forced Wayland selects the Wayland backend");
    TestDragEventAbi();
    TestImeAndShellProtocolAbi();
    TestDeleteSurroundingCallbackContract();
    TestTouchCapabilityAbi();
    TestConfigurableDoubleClickTracking();

    float globalCursorX = 123.0f;
    float globalCursorY = 456.0f;
    Check(jalium_input_get_cursor_pos(&globalCursorX, &globalCursorY) ==
              JALIUM_ERROR_NOT_SUPPORTED &&
              globalCursorX == 0.0f && globalCursorY == 0.0f,
          "Wayland rejects global cursor queries instead of returning surface-local coordinates");

    const char16_t title[] = u"Jalium Wayland \u4E2D\u6587";
    JaliumWindowParams parameters{};
    parameters.title = reinterpret_cast<const JaliumUtf16Char*>(title);
    parameters.x = JALIUM_DEFAULT_POS;
    parameters.y = JALIUM_DEFAULT_POS;
    parameters.width = 360;
    parameters.height = 240;
    parameters.style = JALIUM_WINDOW_STYLE_DEFAULT;
    JaliumPlatformWindow* window = jalium_window_create(&parameters);
    Check(window != nullptr, "create xdg-shell window");
    if (!window) return 1;

    const JaliumSurfaceDescriptor surface = jalium_window_get_surface(window);
    Check(surface.platform == JALIUM_PLATFORM_LINUX_WAYLAND,
          "Wayland window returns Wayland surface descriptor");
    Check(surface.handle0 != 0 && surface.handle1 != 0,
          "Wayland descriptor contains wl_display and wl_surface");
    Check(surface.handle2 != 0,
          "Wayland descriptor contains the platform-owned wl_shm global");
    Check(jalium_window_get_native_handle(window) == surface.handle1,
          "native handle is the wl_surface");
    Check(jalium_wayland_surface_is_ready(surface.handle1) == 0,
          "unshown Wayland surface is not ready for a buffer commit");

    JaliumWindowParams childParameters = parameters;
    childParameters.parentHandle = surface.handle1;
    JaliumPlatformWindow* child = jalium_window_create(&childParameters);
    Check(child != nullptr, "create Wayland transient child");
    if (child)
    {
        Check(jalium_window_set_owner(child, surface.handle1) == JALIUM_OK,
              "Wayland transient owner maps wl_surface to xdg_toplevel");
        Check(jalium_window_set_owner(child, 0) == JALIUM_OK,
              "Wayland transient owner can be cleared");
        jalium_window_destroy(child);
    }

    Check(jalium_window_set_enabled(window, 0) == JALIUM_OK &&
              jalium_window_set_enabled(window, 1) == JALIUM_OK,
          "Wayland native input delivery can be disabled and restored");
    Check(jalium_window_set_resizable(window, 0) == JALIUM_OK &&
              jalium_window_set_resizable(window, 1) == JALIUM_OK,
          "Wayland xdg_toplevel resize constraints are live");
    Check(jalium_window_set_opacity(window, 0.5) == JALIUM_ERROR_NOT_SUPPORTED,
          "Wayland reports missing whole-window opacity protocol");
    Check(jalium_window_set_show_in_taskbar(window, 0) == JALIUM_ERROR_NOT_SUPPORTED,
          "Wayland reports missing taskbar visibility protocol");
    const int32_t decorationResult = jalium_window_set_decorated(window, 0);
    Check(decorationResult == JALIUM_OK ||
              decorationResult == JALIUM_ERROR_NOT_SUPPORTED,
          "Wayland decoration mode uses xdg-decoration when advertised");
    if (decorationResult == JALIUM_OK)
        Check(jalium_window_set_decorated(window, 1) == JALIUM_OK,
              "Wayland decoration mode can return to server-side decoration");
    int32_t menuX = 0;
    int32_t menuY = 0;
    uint32_t menuSerial = 0;
    Check(jalium_window_show_system_menu(window, 37, 53) ==
              JALIUM_ERROR_NOT_SUPPORTED,
          "Wayland system menu rejects requests without a user-input serial");
    Check(jalium_test_wayland_get_last_system_menu(
              window, &menuX, &menuY, &menuSerial) ==
              JALIUM_ERROR_INVALID_STATE,
          "a rejected system-menu request does not publish stale test state");

    const char imeText[] = "a\xE4\xB8\xAD\xF0\x9F\x98\x80z";
    const int32_t imeResult = jalium_window_update_ime_context(
        window, 1, imeText, 8, 1, 23, 29, 2, 18);
    Check(imeResult == JALIUM_OK ||
              imeResult == JALIUM_ERROR_NOT_SUPPORTED,
          "Wayland IME context reports text-input protocol availability explicitly");
    std::array<char, 64> imeSnapshot{};
    int32_t imeEnabled = 0;
    int32_t imeCursor = -1;
    int32_t imeAnchor = -1;
    int32_t imeX = 0;
    int32_t imeY = 0;
    int32_t imeWidth = 0;
    int32_t imeHeight = 0;
    Check(jalium_test_wayland_get_ime_context(
              window, &imeEnabled, imeSnapshot.data(),
              static_cast<uint32_t>(imeSnapshot.size()),
              &imeCursor, &imeAnchor, &imeX, &imeY,
              &imeWidth, &imeHeight) == JALIUM_OK &&
              imeEnabled == 1 && std::strcmp(imeSnapshot.data(), imeText) == 0 &&
              imeCursor == 8 && imeAnchor == 1 &&
              imeX == 23 && imeY == 29 && imeWidth == 2 && imeHeight == 18,
          "Wayland IME snapshot preserves UTF-8 byte offsets and the physical caret rectangle");
    std::array<char, 4> undersizedImeBuffer{};
    Check(jalium_test_wayland_get_ime_context(
              window, &imeEnabled, undersizedImeBuffer.data(),
              static_cast<uint32_t>(undersizedImeBuffer.size()),
              &imeCursor, &imeAnchor, &imeX, &imeY,
              &imeWidth, &imeHeight) == JALIUM_ERROR_OUT_OF_MEMORY,
          "IME snapshot refuses to truncate multibyte surrounding text");
    Check(jalium_window_update_ime_context(
              window, 1, imeText, -1, 1, 23, 29, 2, 18) ==
              JALIUM_ERROR_INVALID_ARGUMENT &&
              jalium_window_update_ime_context(
                  window, 1, imeText, 8, 1, 23, 29, -1, 18) ==
              JALIUM_ERROR_INVALID_ARGUMENT,
          "IME byte offsets and physical caret dimensions reject negative values");
    const int32_t clampedImeResult = jalium_window_update_ime_context(
        window, 1, imeText, 80, 90, -3, 7, 0, 0);
    imeSnapshot.fill('\0');
    Check((clampedImeResult == JALIUM_OK ||
           clampedImeResult == JALIUM_ERROR_NOT_SUPPORTED) &&
              jalium_test_wayland_get_ime_context(
                  window, &imeEnabled, imeSnapshot.data(),
                  static_cast<uint32_t>(imeSnapshot.size()),
                  &imeCursor, &imeAnchor, &imeX, &imeY,
                  &imeWidth, &imeHeight) == JALIUM_OK &&
              imeCursor == 9 && imeAnchor == 9 &&
              imeX == -3 && imeY == 7 && imeWidth == 1 && imeHeight == 1,
          "IME state clamps byte offsets to UTF-8 storage and keeps a non-empty caret rectangle");
    const int32_t unavailableTextResult = jalium_window_update_ime_context(
        window, 1, nullptr, 0, 0, 23, 29, 0, 0);
    Check(unavailableTextResult == JALIUM_OK ||
              unavailableTextResult == JALIUM_ERROR_NOT_SUPPORTED,
          "IME context permits intentionally unavailable surrounding text");
    const int32_t disableImeResult = jalium_window_update_ime_context(
        window, 0, nullptr, 0, 0, 0, 0, 0, 0);
    Check(disableImeResult == JALIUM_OK ||
              disableImeResult == JALIUM_ERROR_NOT_SUPPORTED,
          "Wayland IME context can be disabled without surrounding text");

    imeSnapshot.fill('x');
    Check(jalium_test_wayland_get_ime_context(
              window, &imeEnabled, imeSnapshot.data(),
              static_cast<uint32_t>(imeSnapshot.size()),
              &imeCursor, &imeAnchor, &imeX, &imeY,
              &imeWidth, &imeHeight) == JALIUM_OK &&
              imeEnabled == 0 && imeSnapshot[0] == '\0' &&
              imeCursor == 0 && imeAnchor == 0 &&
              imeX == 0 && imeY == 0 && imeWidth == 1 && imeHeight == 1,
          "disabling IME clears surrounding text while retaining a valid native caret snapshot");

    const int32_t activationResult = jalium_window_activate(window);
    const int32_t hasActivation = jalium_test_wayland_has_activation();
    Check((hasActivation == 1 && activationResult == JALIUM_OK) ||
              (hasActivation == 0 &&
               activationResult == JALIUM_ERROR_NOT_SUPPORTED),
          "Wayland activation returns OK exactly when xdg-activation is advertised");

    // Deliberately rectangular and partially transparent: the optional
    // xdg-toplevel-icon path must premultiply and pad it to a square wl_shm
    // buffer, while older compositors remain an explicit supported fallback.
    const std::array<uint32_t, 6> iconPixels = {
        0xffff0000u, 0x8000ff00u,
        0xff0000ffu, 0x40ffffffu,
        0xff101010u, 0x00000000u,
    };
    const int32_t iconResult = jalium_window_set_icon(
        window, iconPixels.data(), 2, 3);
    Check(iconResult == JALIUM_OK ||
              iconResult == JALIUM_ERROR_NOT_SUPPORTED,
          "Wayland window icon uses xdg-toplevel-icon-v1 when advertised");
    if (iconResult == JALIUM_OK)
        Check(jalium_window_set_icon(window, nullptr, 0, 0) == JALIUM_OK,
              "Wayland xdg-toplevel icon can be reset to the app default");

    WindowCallbackState callbacks;
    jalium_window_set_event_callback(window, WindowCallback, &callbacks);
    Check(jalium_test_wayland_inject_delete_surrounding(window, 7, 4) ==
              JALIUM_OK &&
              callbacks.deleteSurroundingCalls.load(
                  std::memory_order_acquire) == 1 &&
              callbacks.lastDeleteBeforeUtf8Bytes == 7 &&
              callbacks.lastDeleteAfterUtf8Bytes == 4,
          "text-input delete-surrounding dispatches independent UTF-8 byte lengths");
    Check(jalium_test_wayland_inject_delete_surrounding(window, 0, 0) ==
              JALIUM_OK &&
              callbacks.deleteSurroundingCalls.load(
                  std::memory_order_acquire) == 1,
          "an empty text-input deletion does not dispatch a spurious edit");
    Check(jalium_test_wayland_inject_delete_surrounding(
              window, UINT32_MAX, UINT32_MAX) == JALIUM_OK &&
              callbacks.deleteSurroundingCalls.load(
                  std::memory_order_acquire) == 2 &&
              callbacks.lastDeleteBeforeUtf8Bytes == INT32_MAX &&
              callbacks.lastDeleteAfterUtf8Bytes == INT32_MAX,
          "oversized compositor deletion lengths saturate at the public signed ABI limit");
    jalium_window_invalidate(window);
    Check(callbacks.paintCalls.load(std::memory_order_acquire) == 0,
          "configure-pending invalidation is coalesced without a paint callback");
    jalium_window_show(window);
    Check(jalium_wayland_surface_is_ready(surface.handle1) == 0,
          "shown Wayland surface remains gated before configure dispatch");
    Check(callbacks.paintCalls.load(std::memory_order_acquire) == 0,
          "show does not recursively paint before configure");

    JaliumContext* context = nullptr;
    JaliumRenderTarget* renderTarget = nullptr;
    void* softwareModule = nullptr;
    if (testVulkan)
    {
        context = jalium_context_create(JALIUM_BACKEND_VULKAN);
        Check(context != nullptr, "create Vulkan context for Wayland");
        if (context)
        {
            renderTarget = jalium_render_target_create_for_surface(
                context, &surface, parameters.width, parameters.height);
            Check(renderTarget != nullptr, "vkCreateWaylandSurfaceKHR and swapchain succeed");
            if (renderTarget)
            {
                const JaliumResult beginResult = jalium_render_target_begin_draw(renderTarget);
                Check(beginResult == JALIUM_OK, "begin pre-configure Wayland Vulkan frame");
                if (beginResult == JALIUM_OK)
                {
                    jalium_render_target_clear(renderTarget, 0.05f, 0.1f, 0.2f, 1.0f);
                    Check(jalium_render_target_end_draw(renderTarget) ==
                              JALIUM_ERROR_PRESENT_FAILED,
                          "pre-configure Vulkan present is deferred without committing a buffer");
                }
            }
        }
    }

    for (int attempt = 0; attempt < 200 && callbacks.paintCalls.load(std::memory_order_acquire) == 0; ++attempt)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(2ms);
    }
    Check(callbacks.paintCalls.load(std::memory_order_acquire) > 0,
          "xdg_surface configure is acknowledged and schedules paint");
    Check(jalium_wayland_surface_is_ready(surface.handle1) == 1,
          "Wayland buffer commits become ready only after configure ack");

    int32_t logicalWidth = 0;
    int32_t logicalHeight = 0;
    Check(jalium_test_wayland_reset_outputs(window) == JALIUM_OK,
          "Wayland output scale test starts from the scale-1 fallback");
    jalium_window_get_client_size(window, &logicalWidth, &logicalHeight);
    Check(jalium_test_wayland_set_output(window, 101, 1, 1) == JALIUM_OK &&
              std::fabs(jalium_window_get_dpi_scale(window) - 1.0f) < 0.01f,
          "entering a scale-1 output preserves scale 1");
    Check(jalium_test_wayland_set_output(window, 202, 2, 1) == JALIUM_OK &&
              std::fabs(jalium_window_get_dpi_scale(window) - 2.0f) < 0.01f,
          "straddling scale-1 and scale-2 outputs selects the maximum scale");
    int32_t scaledWidth = 0;
    int32_t scaledHeight = 0;
    jalium_window_get_client_size(window, &scaledWidth, &scaledHeight);
    Check(scaledWidth == logicalWidth * 2 && scaledHeight == logicalHeight * 2,
          "Wayland output scale change resizes the physical client buffer");

    Check(jalium_test_wayland_reset_outputs(window) == JALIUM_OK &&
              jalium_test_wayland_set_output(window, 202, 2, 1) == JALIUM_OK &&
              jalium_test_wayland_set_output(window, 101, 1, 1) == JALIUM_OK &&
              std::fabs(jalium_window_get_dpi_scale(window) - 2.0f) < 0.01f &&
              jalium_test_wayland_get_selected_output(window) == 202,
          "Wayland max scale is independent of output-enter order");
    Check(jalium_test_wayland_set_output(window, 303, 2, 1) == JALIUM_OK &&
              jalium_test_wayland_get_selected_output(window) == 202 &&
              jalium_test_wayland_set_output(window, 303, 2, 0) == JALIUM_OK,
          "Wayland refresh selection uses max scale then stable output id");
    Check(jalium_test_wayland_set_output(window, 202, 2, 0) == JALIUM_OK &&
              std::fabs(jalium_window_get_dpi_scale(window) - 1.0f) < 0.01f,
          "leaving the high-DPI output restores the remaining output scale");
    Check(jalium_test_wayland_set_output(window, 101, 2, 1) == JALIUM_OK &&
              std::fabs(jalium_window_get_dpi_scale(window) - 2.0f) < 0.01f &&
              callbacks.dpiCalls.load(std::memory_order_acquire) >= 4,
          "runtime wl_output.scale changes recompute entered window DPI");
    Check(jalium_test_wayland_reset_outputs(window) == JALIUM_OK,
          "Wayland scale transition test restores scale 1");

    // Headless compositors commonly expose neither a touch seat nor tablet
    // hardware and may omit xdg-decoration. Inject the generated protocol
    // listener callbacks so their state/event translations stay executable in
    // CI instead of being compile-only coverage.
    Check(jalium_test_wayland_inject_touch(
              window, JALIUM_EVENT_POINTER_DOWN, 7, 12.5f, 24.5f) == JALIUM_OK &&
              (callbacks.lastPointerFlags &
               (JALIUM_POINTER_FLAG_PRIMARY |
                JALIUM_POINTER_FLAG_IN_CONTACT)) ==
               (JALIUM_POINTER_FLAG_PRIMARY |
                JALIUM_POINTER_FLAG_IN_CONTACT),
          "the first active wl_touch contact is primary and in contact");
    const int32_t systemMenuResult =
        jalium_window_show_system_menu(window, 37, 53);
    const int32_t systemMenuSnapshotResult =
        jalium_test_wayland_get_last_system_menu(
            window, &menuX, &menuY, &menuSerial);
    Check(systemMenuResult == JALIUM_OK ||
              systemMenuResult == JALIUM_ERROR_NOT_SUPPORTED,
          "Wayland system menu reports whether the compositor exposes an input seat");
    if (systemMenuResult == JALIUM_OK)
    {
        Check(systemMenuSnapshotResult == JALIUM_OK &&
                  menuX == 37 && menuY == 53 && menuSerial != 0,
              "Wayland system menu records physical client coordinates and the latest input serial");
    }
    else
        Check(systemMenuSnapshotResult == JALIUM_ERROR_INVALID_STATE,
              "a compositor without an input seat leaves system-menu request state untouched");
    Check(jalium_test_wayland_inject_touch(
              window, JALIUM_EVENT_POINTER_DOWN, 8, 4.0f, 5.0f) == JALIUM_OK &&
              (callbacks.lastPointerFlags & JALIUM_POINTER_FLAG_PRIMARY) == 0 &&
              (callbacks.lastPointerFlags & JALIUM_POINTER_FLAG_IN_CONTACT) != 0,
          "a second concurrent wl_touch contact is not primary");
    Check(jalium_test_wayland_inject_touch(
              window, JALIUM_EVENT_POINTER_MOVE, 7, 18.0f, 30.0f) == JALIUM_OK &&
              callbacks.lastPointerType == JALIUM_POINTER_TOUCH &&
              callbacks.lastPointerId == (0x10000000u | 7u) &&
              std::fabs(callbacks.lastPointerX - 18.0f) < 0.01f &&
              std::fabs(callbacks.lastPointerY - 30.0f) < 0.01f &&
              callbacks.lastPointerPressure == 1.0f &&
              callbacks.lastPointerButtons == JALIUM_POINTER_BUTTON_PRIMARY &&
              (callbacks.lastPointerFlags & JALIUM_POINTER_FLAG_PRIMARY) != 0,
          "wl_touch motion preserves the primary contact and physical data");
    Check(jalium_test_wayland_inject_touch(
              window, JALIUM_EVENT_POINTER_UP, 7, 18.0f, 30.0f) == JALIUM_OK &&
              callbacks.lastPointerPressure == 0.0f &&
              (callbacks.lastPointerFlags & JALIUM_POINTER_FLAG_IN_CONTACT) == 0 &&
              jalium_test_wayland_inject_touch(
                  window, JALIUM_EVENT_POINTER_MOVE, 8, 6.0f, 7.0f) == JALIUM_OK &&
              (callbacks.lastPointerFlags & JALIUM_POINTER_FLAG_PRIMARY) != 0 &&
              jalium_test_wayland_inject_touch(
                  window, JALIUM_EVENT_POINTER_UP, 8, 6.0f, 7.0f) == JALIUM_OK,
          "wl_touch promotes the oldest remaining contact after primary up");
    Check(jalium_test_wayland_inject_touch(
              window, JALIUM_EVENT_POINTER_DOWN, 9, 4.0f, 5.0f) == JALIUM_OK &&
          jalium_test_wayland_inject_touch(
              window, JALIUM_EVENT_POINTER_CANCEL, 9, 4.0f, 5.0f) == JALIUM_OK &&
              callbacks.pointerCancelCalls.load(std::memory_order_acquire) == 1 &&
              (callbacks.lastPointerFlags & JALIUM_POINTER_FLAG_IN_CONTACT) == 0,
          "Wayland wl_touch cancel terminates active contacts without stale contact flags");

    const int movesBeforeTablet =
        callbacks.pointerMoveCalls.load(std::memory_order_acquire);
    const int32_t tabletHover = jalium_test_wayland_inject_tablet_state(
        window, JALIUM_EVENT_POINTER_MOVE, 3,
        41.0f, 52.0f, 0.65f, 12.0f, -8.0f, 37.0f,
        JALIUM_POINTER_TOOL_ERASER, JALIUM_POINTER_BUTTON_BARREL,
        0.4f, -0.25f);
    Check(tabletHover == JALIUM_OK ||
              tabletHover == JALIUM_ERROR_NOT_SUPPORTED,
          "tablet-v2 extended injection reports protocol availability");
    if (tabletHover == JALIUM_OK)
    {
        Check(callbacks.pointerMoveCalls.load(std::memory_order_acquire) ==
                  movesBeforeTablet + 1 &&
                  callbacks.lastPointerType == JALIUM_POINTER_PEN &&
                  callbacks.lastPointerToolType == JALIUM_POINTER_TOOL_ERASER &&
                  callbacks.lastPointerPressure == 0.0f &&
                  (callbacks.lastPointerFlags &
                   (JALIUM_POINTER_FLAG_IN_RANGE |
                    JALIUM_POINTER_FLAG_PRIMARY |
                    JALIUM_POINTER_FLAG_ERASER |
                    JALIUM_POINTER_FLAG_INVERTED |
                    JALIUM_POINTER_FLAG_BARREL)) ==
                   (JALIUM_POINTER_FLAG_IN_RANGE |
                    JALIUM_POINTER_FLAG_PRIMARY |
                    JALIUM_POINTER_FLAG_ERASER |
                    JALIUM_POINTER_FLAG_INVERTED |
                    JALIUM_POINTER_FLAG_BARREL) &&
                  (callbacks.lastPointerFlags &
                   JALIUM_POINTER_FLAG_IN_CONTACT) == 0 &&
                  callbacks.lastPointerButtons == JALIUM_POINTER_BUTTON_BARREL,
              "tablet proximity emits honest eraser hover, inverted, and barrel semantics");

        const int movesBeforeAxes =
            callbacks.pointerMoveCalls.load(std::memory_order_acquire);
        Check(jalium_test_wayland_inject_tablet_state(
                  window, JALIUM_EVENT_POINTER_MOVE, 3,
                  41.0f, 52.0f, 0.65f, 12.0f, -8.0f, 37.0f,
                  JALIUM_POINTER_TOOL_ERASER, JALIUM_POINTER_BUTTON_BARREL,
                  0.8f, 0.5f) == JALIUM_OK &&
                  callbacks.pointerMoveCalls.load(std::memory_order_acquire) ==
                      movesBeforeAxes + 1,
              "tablet distance and slider changes participate in frame aggregation");

        Check(jalium_test_wayland_inject_tablet_state(
                  window, JALIUM_EVENT_POINTER_DOWN, 3,
                  41.0f, 52.0f, 0.65f, 12.0f, -8.0f, 37.0f,
                  JALIUM_POINTER_TOOL_ERASER, JALIUM_POINTER_BUTTON_BARREL,
                  0.0f, 0.0f) == JALIUM_OK &&
                  std::fabs(callbacks.lastPointerPressure - 0.65f) < 0.01f &&
                  std::fabs(callbacks.lastPointerTiltX - 12.0f) < 0.01f &&
                  std::fabs(callbacks.lastPointerTiltY + 8.0f) < 0.01f &&
                  std::fabs(callbacks.lastPointerTwist - 37.0f) < 0.01f &&
                  (callbacks.lastPointerFlags &
                   JALIUM_POINTER_FLAG_IN_CONTACT) != 0 &&
                  (callbacks.lastPointerButtons &
                   (JALIUM_POINTER_BUTTON_PRIMARY |
                    JALIUM_POINTER_BUTTON_BARREL)) ==
                   (JALIUM_POINTER_BUTTON_PRIMARY |
                    JALIUM_POINTER_BUTTON_BARREL),
              "tablet down adds contact and tip-button state without losing axes");
        Check(jalium_test_wayland_inject_tablet_state(
                  window, JALIUM_EVENT_POINTER_MOVE, 3,
                  44.0f, 55.0f, 0.8f, 14.0f, -6.0f, 42.0f,
                  JALIUM_POINTER_TOOL_ERASER,
                  JALIUM_POINTER_BUTTON_BARREL |
                      JALIUM_POINTER_BUTTON_SECONDARY,
                  0.0f, 0.0f) == JALIUM_OK &&
                  (callbacks.lastPointerButtons &
                   JALIUM_POINTER_BUTTON_SECONDARY) != 0 &&
              jalium_test_wayland_inject_tablet_state(
                  window, JALIUM_EVENT_POINTER_UP, 3,
                  44.0f, 55.0f, 0.8f, 14.0f, -6.0f, 42.0f,
                  JALIUM_POINTER_TOOL_ERASER,
                  JALIUM_POINTER_BUTTON_BARREL |
                      JALIUM_POINTER_BUTTON_SECONDARY,
                  0.0f, 0.0f) == JALIUM_OK &&
                  callbacks.lastPointerPressure == 0.0f &&
                  (callbacks.lastPointerFlags &
                   JALIUM_POINTER_FLAG_IN_CONTACT) == 0 &&
                  (callbacks.lastPointerFlags &
                   JALIUM_POINTER_FLAG_IN_RANGE) != 0 &&
                  (callbacks.lastPointerButtons &
                   JALIUM_POINTER_BUTTON_PRIMARY) == 0,
              "tablet buttons persist independently while up returns to hover");
        Check(jalium_test_wayland_inject_tablet_state(
                  window, JALIUM_EVENT_POINTER_CANCEL, 3,
                  44.0f, 55.0f, 0.0f, 14.0f, -6.0f, 42.0f,
                  JALIUM_POINTER_TOOL_ERASER,
                  JALIUM_POINTER_BUTTON_NONE, 1.0f, 0.0f) == JALIUM_OK &&
                  (callbacks.lastPointerFlags &
                   (JALIUM_POINTER_FLAG_IN_RANGE |
                    JALIUM_POINTER_FLAG_IN_CONTACT)) == 0,
              "tablet proximity-out emits a hover move with range cleared");

        const int cancelsBeforeActiveTablet =
            callbacks.pointerCancelCalls.load(std::memory_order_acquire);
        Check(jalium_test_wayland_inject_tablet_state(
                  window, JALIUM_EVENT_POINTER_DOWN, 4,
                  20.0f, 21.0f, 0.5f, 0.0f, 0.0f, 0.0f,
                  JALIUM_POINTER_TOOL_PEN, JALIUM_POINTER_BUTTON_NONE,
                  0.0f, 0.0f) == JALIUM_OK &&
              jalium_test_wayland_inject_tablet_state(
                  window, JALIUM_EVENT_POINTER_CANCEL, 4,
                  20.0f, 21.0f, 0.5f, 0.0f, 0.0f, 0.0f,
                  JALIUM_POINTER_TOOL_PEN, JALIUM_POINTER_BUTTON_NONE,
                  1.0f, 0.0f) == JALIUM_OK &&
                  callbacks.pointerCancelCalls.load(std::memory_order_acquire) ==
                      cancelsBeforeActiveTablet + 1 &&
                  callbacks.lastPointerPressure == 0.0f &&
                  (callbacks.lastPointerFlags &
                   (JALIUM_POINTER_FLAG_IN_RANGE |
                    JALIUM_POINTER_FLAG_IN_CONTACT)) == 0,
              "tablet proximity loss cancels an active contact without fabricating hover contact");
    }

    const int32_t decorationInjection =
        jalium_test_wayland_inject_decoration_configure(window, 2);
    Check(decorationInjection == JALIUM_OK ||
              decorationInjection == JALIUM_ERROR_NOT_SUPPORTED,
          "xdg-decoration configure callback reports protocol availability");
    if (decorationInjection == JALIUM_OK)
    {
        Check(jalium_test_wayland_get_decoration_mode(window) == 2 &&
              jalium_test_wayland_inject_decoration_configure(window, 1) == JALIUM_OK &&
              jalium_test_wayland_get_decoration_mode(window) == 1,
              "xdg-decoration configure listener records server/client modes");
    }

    if (!testVulkan)
    {
        softwareModule = dlopen(
            "libjalium.native.software.so", RTLD_NOW | RTLD_GLOBAL);
        auto softwareInit = softwareModule
            ? reinterpret_cast<void (*)()>(
                  dlsym(softwareModule, "jalium_software_init"))
            : nullptr;
        Check(softwareInit != nullptr,
              "load software backend for mapped Wayland popup parent");
        if (softwareInit) softwareInit();
        context = softwareInit
            ? jalium_context_create(JALIUM_BACKEND_SOFTWARE)
            : nullptr;
        renderTarget = context
            ? jalium_render_target_create_for_surface(
                  context, &surface, parameters.width, parameters.height)
            : nullptr;
        Check(renderTarget != nullptr,
              "create software target for mapped Wayland popup parent");
    }

    if (renderTarget)
    {
        const JaliumResult beginResult = jalium_render_target_begin_draw(renderTarget);
        Check(beginResult == JALIUM_OK, "begin configured Wayland frame");
        if (beginResult == JALIUM_OK)
        {
            jalium_render_target_clear(renderTarget, 0.05f, 0.1f, 0.2f, 1.0f);
            Check(jalium_render_target_end_draw(renderTarget) == JALIUM_OK,
                  "present configured Wayland frame before creating a popup");
        }
    }

    const uint32_t portalRequired =
        jalium_window_get_portal_parent_handle_for_native_handle(
            surface.handle1, nullptr, 0);
    if (portalRequired > 0)
    {
        std::vector<char> portalValue(portalRequired);
        Check(jalium_window_get_portal_parent_handle_for_native_handle(
                  surface.handle1, portalValue.data(), portalRequired) == portalRequired &&
                  std::strncmp(portalValue.data(), "wayland:", 8) == 0,
              "xdg-foreign-v2 exports a Wayland portal parent handle");
        std::vector<char> opaquePortalValue(portalRequired);
        Check(jalium_window_get_portal_parent_handle(
                  window, opaquePortalValue.data(), portalRequired) == portalRequired &&
                  std::strcmp(portalValue.data(), opaquePortalValue.data()) == 0,
              "Wayland portal parent ABI agrees for native and opaque handles");
    }

    const char16_t popupTitle[] = u"Jalium Wayland popup";
    JaliumWindowParams popupParameters{};
    popupParameters.title = reinterpret_cast<const JaliumUtf16Char*>(popupTitle);
    popupParameters.x = 28;
    popupParameters.y = 36;
    popupParameters.width = 150;
    popupParameters.height = 92;
    popupParameters.style = JALIUM_WINDOW_STYLE_POPUP |
                            JALIUM_WINDOW_STYLE_TRANSPARENT;
    popupParameters.parentHandle = surface.handle1;
    JaliumPlatformWindow* popup = jalium_window_create(&popupParameters);
    Check(popup != nullptr, "create Wayland xdg_popup with an xdg_positioner");
    if (popup)
    {
        WindowCallbackState popupCallbacks;
        jalium_window_set_event_callback(popup, WindowCallback, &popupCallbacks);
        const JaliumSurfaceDescriptor popupSurface = jalium_window_get_surface(popup);
        jalium_window_show(popup);
        for (int attempt = 0;
             attempt < 200 && popupCallbacks.paintCalls.load(std::memory_order_acquire) == 0;
             ++attempt)
        {
            (void)jalium_platform_poll_events();
            std::this_thread::sleep_for(2ms);
        }
        Check(popupCallbacks.paintCalls.load(std::memory_order_acquire) > 0 &&
                  jalium_wayland_surface_is_ready(popupSurface.handle1) == 1,
              "xdg_popup configure is acknowledged and schedules paint");

        const int resizeBase = popupCallbacks.resizeCalls.load(std::memory_order_acquire);
        jalium_window_move(popup, 44, 52);
        jalium_window_resize(popup, 180, 110);
        for (int attempt = 0; attempt < 50; ++attempt)
        {
            (void)jalium_platform_poll_events();
            std::this_thread::sleep_for(1ms);
        }
        int32_t popupWidth = 0;
        int32_t popupHeight = 0;
        jalium_window_get_client_size(popup, &popupWidth, &popupHeight);
        Check(popupWidth > 0 && popupHeight > 0 &&
                  popupCallbacks.resizeCalls.load(std::memory_order_acquire) > resizeBase,
              "xdg_popup resize/reposition remains live after initial configure");

        jalium_window_hide(popup);
        Check(jalium_wayland_surface_is_ready(popupSurface.handle1) == 0,
              "hiding an xdg_popup tears down its mapped role");
        jalium_window_show(popup);
        for (int attempt = 0;
             attempt < 200 && jalium_wayland_surface_is_ready(popupSurface.handle1) == 0;
             ++attempt)
        {
            (void)jalium_platform_poll_events();
            std::this_thread::sleep_for(2ms);
        }
        Check(jalium_wayland_surface_is_ready(popupSurface.handle1) == 1,
              "re-showing an xdg_popup completes a new configure handshake");
        jalium_window_destroy(popup);
    }

    const int paintBase = callbacks.paintCalls.load(std::memory_order_acquire);
    callbacks.reentrantInvalidateUntil.store(paintBase + 3, std::memory_order_release);
    jalium_window_invalidate(window);
    for (int attempt = 0;
         attempt < 50 && callbacks.paintCalls.load(std::memory_order_acquire) < paintBase + 3;
         ++attempt)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(1ms);
    }
    callbacks.reentrantInvalidateUntil.store(0, std::memory_order_release);
    Check(callbacks.paintCalls.load(std::memory_order_acquire) >= paintBase + 3,
          "re-entrant invalidations drain on later event-loop turns");
    Check(callbacks.maxPaintCallbackDepth.load(std::memory_order_acquire) == 1,
          "Wayland paint invalidation never recursively re-enters the callback");

    jalium_window_resize(window, 420, 280);
    if (renderTarget)
        Check(jalium_render_target_resize(renderTarget, 420, 280) == JALIUM_OK,
              "resize Wayland Vulkan swapchain for the new logical size");
    int32_t width = 0;
    int32_t height = 0;
    jalium_window_get_client_size(window, &width, &height);
    Check(width == 420 && height == 280, "Wayland logical resize updates client size");
    Check(callbacks.resizeCalls.load(std::memory_order_acquire) > 0,
          "Wayland logical resize emits resize event");
    jalium_window_set_state(window, JALIUM_WINDOW_STATE_MAXIMIZED);
    Check(jalium_window_get_state(window) == JALIUM_WINDOW_STATE_MAXIMIZED,
          "Wayland state request is observable immediately");
    jalium_window_set_state(window, JALIUM_WINDOW_STATE_FULLSCREEN);
    Check(jalium_window_get_state(window) == JALIUM_WINDOW_STATE_FULLSCREEN,
          "Wayland fullscreen is preserved instead of downgraded to maximized");
    jalium_window_hide(window);
    Check(jalium_wayland_surface_is_ready(surface.handle1) == 0,
          "hidden Wayland surface is not ready for buffer commits");
    if (renderTarget && jalium_render_target_begin_draw(renderTarget) == JALIUM_OK)
    {
        jalium_render_target_clear(renderTarget, 0.2f, 0.05f, 0.1f, 1.0f);
        Check(jalium_render_target_end_draw(renderTarget) == JALIUM_ERROR_PRESENT_FAILED,
              "hidden Vulkan surface defers presentation");
    }

    const int reshownPaintBase = callbacks.paintCalls.load(std::memory_order_acquire);
    jalium_window_show(window);
    Check(jalium_wayland_surface_is_ready(surface.handle1) == 0,
          "re-shown Wayland surface waits for its new configure handshake");
    if (renderTarget && jalium_render_target_begin_draw(renderTarget) == JALIUM_OK)
    {
        jalium_render_target_clear(renderTarget, 0.2f, 0.05f, 0.1f, 1.0f);
        Check(jalium_render_target_end_draw(renderTarget) == JALIUM_ERROR_PRESENT_FAILED,
              "Vulkan stays gated during the re-show configure handshake");
    }
    for (int attempt = 0;
         attempt < 200 && jalium_wayland_surface_is_ready(surface.handle1) == 0;
         ++attempt)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(2ms);
    }
    Check(jalium_wayland_surface_is_ready(surface.handle1) == 1,
          "re-shown Wayland surface becomes ready after its second configure ack");
    Check(callbacks.paintCalls.load(std::memory_order_acquire) > reshownPaintBase,
          "second configure handshake releases one pending paint");
    if (renderTarget && jalium_render_target_begin_draw(renderTarget) == JALIUM_OK)
    {
        jalium_render_target_clear(renderTarget, 0.1f, 0.3f, 0.2f, 1.0f);
        Check(jalium_render_target_end_draw(renderTarget) == JALIUM_OK,
              "Vulkan presents after the re-show configure ack");
    }

    if (renderTarget) jalium_render_target_destroy(renderTarget);
    if (context) jalium_context_destroy(context);
    if (softwareModule) dlclose(softwareModule);
    jalium_window_destroy(window);
    return g_failures == 0 ? 0 : 1;
}

int RunClipboardToolSmoke(bool wayland)
{
    JaliumPlatformWindow* window = nullptr;
    JaliumContext* context = nullptr;
    JaliumRenderTarget* renderTarget = nullptr;
    WindowCallbackState callbacks;
    if (wayland)
    {
        const char16_t title[] = u"Jalium clipboard smoke";
        JaliumWindowParams parameters{};
        parameters.title = reinterpret_cast<const JaliumUtf16Char*>(title);
        parameters.x = JALIUM_DEFAULT_POS;
        parameters.y = JALIUM_DEFAULT_POS;
        parameters.width = 240;
        parameters.height = 120;
        parameters.style = JALIUM_WINDOW_STYLE_DEFAULT;
        window = jalium_window_create(&parameters);
        Check(window != nullptr, "create Wayland clipboard serial window");
        if (window)
        {
            jalium_window_set_event_callback(window, WindowCallback, &callbacks);
            jalium_window_show(window);
            for (int attempt = 0; attempt < 500 && callbacks.paintCalls.load() == 0; ++attempt)
            {
                (void)jalium_platform_poll_events();
                std::this_thread::sleep_for(2ms);
            }

            // xdg-shell surfaces are not keyboard-focusable until a renderer
            // attaches their first buffer. Use the production software
            // backend so this smoke exercises the same wl_shm mapping path as
            // a real Jalium window before requesting a clipboard serial.
            setenv("JALIUM_RENDER_BACKEND", "software", 1);
            context = jalium_context_create(JALIUM_BACKEND_SOFTWARE);
            const JaliumSurfaceDescriptor surface = jalium_window_get_surface(window);
            renderTarget = context
                ? jalium_render_target_create_for_surface(
                      context, &surface, parameters.width, parameters.height)
                : nullptr;
            Check(context != nullptr && renderTarget != nullptr,
                  "map Wayland clipboard window with software wl_shm");
            if (renderTarget)
            {
                for (int attempt = 0; attempt < 250; ++attempt)
                {
                    (void)jalium_platform_poll_events();
                    if (jalium_render_target_begin_draw(renderTarget) == JALIUM_OK)
                    {
                        jalium_render_target_clear(renderTarget, 0.1f, 0.2f, 0.35f, 1.0f);
                        if (jalium_render_target_end_draw(renderTarget) == JALIUM_OK) break;
                    }
                    std::this_thread::sleep_for(2ms);
                }
            }
            for (int attempt = 0; attempt < 2500 && callbacks.focusCalls.load() == 0; ++attempt)
            {
                (void)jalium_platform_poll_events();
                std::this_thread::sleep_for(2ms);
            }
            Check(callbacks.focusCalls.load() > 0,
                  "Wayland keyboard enter supplies selection serial");
        }
    }

    if (!wayland)
    {
        Check(std::system(
                  "printf 'external-中文-😀' | xclip -selection clipboard -in") == 0,
              "real clipboard tool publishes external UTF-8 text");
    }
    const char16_t externalExpected[] = u"external-\u4E2D\u6587-\U0001F600";
    JaliumUtf16Char* external = nullptr;
    for (int attempt = 0; attempt < 100 && !external; ++attempt)
    {
        (void)jalium_platform_poll_events();
        if (jalium_clipboard_get_text(&external) != JALIUM_OK) external = nullptr;
        if (!external) std::this_thread::sleep_for(5ms);
    }
    Check(external != nullptr, "read text owned by xclip/wl-copy");
    if (external)
    {
        const std::u16string actualExternal(reinterpret_cast<char16_t*>(external));
        Check(actualExternal == externalExpected,
              "external clipboard tool UTF-8 reaches Jalium as UTF-16");
        if (actualExternal != externalExpected)
        {
            std::cerr << "  external UTF-16 code units:";
            for (char16_t value : actualExternal)
                std::cerr << ' ' << std::hex << static_cast<unsigned int>(value);
            std::cerr << std::dec << '\n';
        }
        jalium_platform_free(external);
    }

    const char16_t ownedText[] = u"jalium-\u526A\u8D34\u677F-\U0001F680";
    Check(jalium_clipboard_set_text(
              reinterpret_cast<const JaliumUtf16Char*>(ownedText)) == JALIUM_OK,
          "publish clipboard text for xclip/wl-paste");

    std::atomic<bool> readerDone{false};
    std::string toolOutput;
    std::thread reader([&]
    {
        const char* command = wayland
            ? "wl-paste --no-newline"
            : "xclip -selection clipboard -out";
        FILE* pipe = popen(command, "r");
        if (pipe)
        {
            char buffer[1024];
            while (size_t count = fread(buffer, 1, sizeof(buffer), pipe))
                toolOutput.append(buffer, count);
            (void)pclose(pipe);
        }
        readerDone.store(true, std::memory_order_release);
    });
    for (int attempt = 0; attempt < 1000 && !readerDone.load(std::memory_order_acquire); ++attempt)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(2ms);
    }
    reader.join();
    Check(toolOutput == "jalium-" "\xE5\x89\xAA" "\xE8\xB4\xB4" "\xE6\x9D\xBF"
                        "-" "\xF0\x9F\x9A\x80",
          "xclip/wl-paste receives Jalium UTF-8 selection data");
    if (toolOutput != "jalium-" "\xE5\x89\xAA" "\xE8\xB4\xB4" "\xE6\x9D\xBF"
                      "-" "\xF0\x9F\x9A\x80")
    {
        std::cerr << "  clipboard tool bytes (" << toolOutput.size() << "):";
        for (unsigned char value : toolOutput)
            std::cerr << ' ' << std::hex << static_cast<unsigned int>(value);
        std::cerr << std::dec << '\n';
    }

    // Replace Jalium's selection with a real external HTML owner and verify
    // MIME-specific reads do not collapse to the plain-text compatibility API.
    const char* publishExternalHtml = wayland
        ? "printf '<p>external-tool-html</p>' | wl-copy --type text/html"
        : "printf '<p>external-tool-html</p>' | xclip -selection clipboard -in -target text/html";
    Check(std::system(publishExternalHtml) == 0,
          "external clipboard tool publishes an explicit text/html target");
    uint8_t* externalHtml = nullptr;
    uint32_t externalHtmlSize = 0;
    for (int attempt = 0; attempt < 400 && !externalHtml; ++attempt)
    {
        (void)jalium_platform_poll_events();
        if (jalium_clipboard_get_data(
                "text/html", &externalHtml, &externalHtmlSize) != JALIUM_OK)
            externalHtml = nullptr;
        if (!externalHtml) std::this_thread::sleep_for(5ms);
    }
    Check(externalHtml &&
              std::string(reinterpret_cast<char*>(externalHtml), externalHtmlSize) ==
                  "<p>external-tool-html</p>",
          "Jalium reads text/html bytes from xclip/wl-copy without text coercion");
    if (externalHtml &&
        std::string(reinterpret_cast<char*>(externalHtml), externalHtmlSize) !=
            "<p>external-tool-html</p>")
    {
        std::cerr << "  external HTML: "
                  << std::string(reinterpret_cast<char*>(externalHtml), externalHtmlSize)
                  << '\n';
    }
    if (externalHtml) jalium_platform_free(externalHtml);

    const std::string ownedHtml = "<p>jalium-tool-html</p>";
    const std::string ownedUris =
        "file:///tmp/jalium%20tool.txt\r\nfile:///tmp/jalium-tool-two.txt\r\n";
    std::string ownedCustom(192u * 1024u + 19u, '\0');
    for (size_t index = 0; index < ownedCustom.size(); ++index)
        ownedCustom[index] = static_cast<char>('!' + (index % 90u));
    const JaliumClipboardDataItem multiItems[] = {
        {"text/html", reinterpret_cast<const uint8_t*>(ownedHtml.data()),
         static_cast<uint32_t>(ownedHtml.size())},
        {"text/uri-list", reinterpret_cast<const uint8_t*>(ownedUris.data()),
         static_cast<uint32_t>(ownedUris.size())},
        {"application/vnd.jalium.tool-smoke",
         reinterpret_cast<const uint8_t*>(ownedCustom.data()),
         static_cast<uint32_t>(ownedCustom.size())},
    };
    Check(jalium_clipboard_set_data(multiItems, 3) == JALIUM_OK,
          "publish a multi-format clipboard transaction for real tool readers");

    std::string toolHtml;
    std::string toolUris;
    std::string toolCustom;
    readerDone.store(false, std::memory_order_release);
    std::thread multiReader([&]
    {
        auto readCommand = [](const char* command)
        {
            std::string output;
            FILE* commandPipe = popen(command, "r");
            if (!commandPipe) return output;
            char buffer[8192];
            while (size_t count = fread(buffer, 1, sizeof(buffer), commandPipe))
                output.append(buffer, count);
            (void)pclose(commandPipe);
            return output;
        };
        if (wayland)
        {
            toolHtml = readCommand("wl-paste --no-newline --type text/html");
            toolUris = readCommand("wl-paste --no-newline --type text/uri-list");
            toolCustom = readCommand(
                "wl-paste --no-newline --type application/vnd.jalium.tool-smoke");
        }
        else
        {
            toolHtml = readCommand("xclip -selection clipboard -out -target text/html");
            toolUris = readCommand("xclip -selection clipboard -out -target text/uri-list");
            toolCustom = readCommand(
                "xclip -selection clipboard -out -target application/vnd.jalium.tool-smoke");
        }
        readerDone.store(true, std::memory_order_release);
    });
    for (int attempt = 0;
         attempt < 5000 && !readerDone.load(std::memory_order_acquire);
         ++attempt)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(2ms);
    }
    multiReader.join();
    Check(toolHtml == ownedHtml,
          "xclip/wl-paste selects Jalium's text/html representation");
    Check(toolUris == ownedUris,
          "xclip/wl-paste selects Jalium's text/uri-list representation");
    Check(toolCustom == ownedCustom,
          "xclip/wl-paste receives the complete large custom MIME representation");

    Check(jalium_clipboard_clear() == JALIUM_OK,
          "clear the clipboard after real tool interoperability smoke");
    if (renderTarget) jalium_render_target_destroy(renderTarget);
    if (context) jalium_context_destroy(context);
    if (window) jalium_window_destroy(window);
    return g_failures == 0 ? 0 : 1;
}

} // namespace

int main(int argc, char** argv)
{
    if (jalium_platform_init() != JALIUM_OK)
    {
        std::cerr << "FAILED: platform initialization (DISPLAY must point to X11)\n";
        return 1;
    }

    if (argc > 1 && (std::strcmp(argv[1], "--wayland-smoke") == 0 ||
                     std::strcmp(argv[1], "--wayland-vulkan-smoke") == 0))
    {
        const int result = RunWaylandSmoke(
            std::strcmp(argv[1], "--wayland-vulkan-smoke") == 0);
        jalium_platform_shutdown();
        if (result == 0) std::cout << "Wayland platform smoke test passed.\n";
        return result;
    }

    if (argc > 1 && std::strcmp(argv[1], "--x11-clipboard-multiformat-smoke") == 0)
    {
        TestClipboardMultiFormatAndIncr();
        jalium_platform_shutdown();
        if (g_failures == 0)
            std::cout << "X11 multi-format and INCR clipboard smoke passed.\n";
        return g_failures == 0 ? 0 : 1;
    }

    if (argc > 1 && std::strcmp(argv[1], "--wayland-dnd-smoke") == 0)
    {
        const int result = RunWaylandDndSmoke();
        jalium_platform_shutdown();
        if (result == 0) std::cout << "Wayland drag-and-drop smoke test passed.\n";
        return result;
    }

    if (argc > 1 && std::strcmp(argv[1], "--x11-dnd-smoke") == 0)
    {
        TestDragEventAbi();
        TestExternalXdndUriDrop();
        jalium_platform_shutdown();
        if (g_failures == 0) std::cout << "X11 external XDND smoke test passed.\n";
        return g_failures == 0 ? 0 : 1;
    }

    if (argc > 1 && std::strcmp(argv[1], "--x11-dnd-self-smoke") == 0)
    {
        TestInProcessXdndSourceRoundTrip();
        jalium_platform_shutdown();
        if (g_failures == 0) std::cout << "X11 in-process XDND smoke test passed.\n";
        return g_failures == 0 ? 0 : 1;
    }

    if (argc > 1 && (std::strcmp(argv[1], "--x11-clipboard-tool-smoke") == 0 ||
                     std::strcmp(argv[1], "--wayland-clipboard-tool-smoke") == 0))
    {
        const bool wayland = std::strcmp(
            argv[1], "--wayland-clipboard-tool-smoke") == 0;
        const int result = RunClipboardToolSmoke(wayland);
        jalium_platform_shutdown();
        if (result == 0) std::cout << (wayland ? "Wayland" : "X11")
                                   << " clipboard tool smoke passed.\n";
        return result;
    }

    TestDragEventAbi();
    TestImeAndShellProtocolAbi();
    TestDeleteSurroundingCallbackContract();
    TestXInputSmoothScrollConversion();
    TestConfigurableDoubleClickTracking();
    TestX11GlobalCursorPosition();
    TestTouchCapabilityAbi();
    TestUtf16WindowTitles();
    TestClipboardUtf16AndExternalSelection();
    TestClipboardMultiFormatAndIncr();
    TestExternalXdndUriDrop();
    TestWindowManagementExtensions();
    TestX11PopupAndPortalParent();
    // A programmatic source without a real button grab is deterministic under
    // Xvfb, but WSLg/Xwayland correctly rejects that synthetic grab. Keep the
    // self-round-trip as an explicit harness test instead of making every
    // platform regression run depend on compositor grab policy.
    if (const char* syntheticDnd = std::getenv("JALIUM_TEST_SYNTHETIC_DND");
        syntheticDnd && std::strcmp(syntheticDnd, "1") == 0)
        TestInProcessXdndSourceRoundTrip();
    TestDispatcherInBlockingLoop();
    TestDispatcherInPollingLoopAndSelfDestroy();
    TestTimerCallbackAndWaitModes();
    TestEventSourceDestructionClosesFileDescriptors();

    JaliumDispatcher* liveDispatcher = nullptr;
    JaliumTimer* liveTimer = nullptr;
    Check(jalium_dispatcher_create(&liveDispatcher) == JALIUM_OK && liveDispatcher,
          "create live dispatcher for shutdown cleanup test");
    Check(jalium_timer_create(&liveTimer) == JALIUM_OK && liveTimer,
          "create live timer for shutdown cleanup test");
    const size_t descriptorsBeforeShutdown = CountOpenFileDescriptors();
    jalium_platform_shutdown();
    Check(CountOpenFileDescriptors() < descriptorsBeforeShutdown,
          "platform shutdown closes epoll, wake, dispatcher, timer, and X11 descriptors");
    // Opaque wrappers remain safe to dispose after platform shutdown.
    jalium_dispatcher_destroy(liveDispatcher);
    jalium_timer_destroy(liveTimer);

    if (g_failures == 0)
        std::cout << "All Linux platform tests passed.\n";
    return g_failures == 0 ? 0 : 1;
}

#endif
