#if defined(__linux__) && !defined(__ANDROID__)

#include "jalium_platform.h"
#include "jalium_api.h"

#include <X11/Xatom.h>
#include <X11/Xlib.h>
#include <poll.h>
#include <unistd.h>
#include <dlfcn.h>

#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <string>
#include <thread>

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
    std::atomic<int> focusCalls{0};
    std::atomic<int> mouseDownCalls{0};
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
    else if (event->type == JALIUM_EVENT_FOCUS_GAINED)
        state->focusCalls.fetch_add(1, std::memory_order_release);
    else if (event->type == JALIUM_EVENT_MOUSE_DOWN)
        state->mouseDownCalls.fetch_add(1, std::memory_order_release);
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
    uint32_t performed = JALIUM_DRAG_EFFECT_NONE;
    const JaliumResult result = jalium_drag_begin(
        window, &item, 1, JALIUM_DRAG_EFFECT_COPY, &performed);
    Check(result == JALIUM_OK, "in-process jalium_drag_begin completes");
    Check(performed == JALIUM_DRAG_EFFECT_COPY, "in-process drag negotiates copy");
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
    uint32_t performed = JALIUM_DRAG_EFFECT_NONE;
    const JaliumResult result = state.mouseDownCalls.load() > 0
        ? jalium_drag_begin(window, &item, 1, JALIUM_DRAG_EFFECT_COPY, &performed)
        : JALIUM_ERROR_INVALID_STATE;
    Check(result == JALIUM_OK, "Wayland wl_data_device_start_drag completes");
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

    WindowCallbackState callbacks;
    jalium_window_set_event_callback(window, WindowCallback, &callbacks);
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

    if (renderTarget)
    {
        const JaliumResult beginResult = jalium_render_target_begin_draw(renderTarget);
        Check(beginResult == JALIUM_OK, "begin configured Wayland Vulkan frame");
        if (beginResult == JALIUM_OK)
        {
            jalium_render_target_clear(renderTarget, 0.05f, 0.1f, 0.2f, 1.0f);
            Check(jalium_render_target_end_draw(renderTarget) == JALIUM_OK,
                  "present configured Wayland Vulkan frame");
        }
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
        Check(std::u16string(reinterpret_cast<char16_t*>(external)) == externalExpected,
              "external clipboard tool UTF-8 reaches Jalium as UTF-16");
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
    TestUtf16WindowTitles();
    TestClipboardUtf16AndExternalSelection();
    TestExternalXdndUriDrop();
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
