#if defined(__linux__) && !defined(__ANDROID__)

#include "jalium_platform.h"

#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/Xatom.h>
#include <X11/keysym.h>
#include <X11/XKBlib.h>
#include <X11/Xresource.h>

#ifdef JALIUM_HAS_WAYLAND
#include <wayland-client.h>
#include <wayland-cursor.h>
#include <xkbcommon/xkbcommon.h>
#include "xdg-shell-client-protocol.h"
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
#include "text-input-unstable-v3-client-protocol.h"
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
#include "text-input-unstable-v1-client-protocol.h"
#endif
#include <sys/mman.h>
#endif

#ifdef JALIUM_HAS_XRANDR
#include <X11/extensions/Xrandr.h>
#endif

#include <sys/eventfd.h>
#include <sys/timerfd.h>
#include <sys/epoll.h>
#include <fcntl.h>
#include <unistd.h>
#include <poll.h>
#include <time.h>
#include <signal.h>
#include <errno.h>
#include <string.h>
#include <stdlib.h>
#include <stdio.h>
#include <locale.h>

#include <atomic>
#include <mutex>
#include <unordered_map>
#include <unordered_set>
#include <vector>
#include <string>
#include <array>
#include <algorithm>
#include <cctype>
#include <cmath>
#include <chrono>
#include <thread>

// ============================================================================
// Global State
// ============================================================================

static Display*     g_display = nullptr;
static int          g_screen = 0;
static Window       g_rootWindow = 0;
static Atom         g_wmDeleteWindow = 0;
static Atom         g_wmProtocols = 0;
static Atom         g_clipboardAtom = 0;
static Atom         g_utf8StringAtom = 0;
static Atom         g_targetsAtom = 0;
static Atom         g_jaliumClipProp = 0;
static Atom         g_textPlainUtf8Atom = 0;
static Atom         g_textPlainAtom = 0;
static Atom         g_incrAtom = 0;
static Atom         g_xdndAwareAtom = 0;
static Atom         g_xdndEnterAtom = 0;
static Atom         g_xdndPositionAtom = 0;
static Atom         g_xdndStatusAtom = 0;
static Atom         g_xdndLeaveAtom = 0;
static Atom         g_xdndDropAtom = 0;
static Atom         g_xdndFinishedAtom = 0;
static Atom         g_xdndSelectionAtom = 0;
static Atom         g_xdndTypeListAtom = 0;
static Atom         g_xdndActionListAtom = 0;
static Atom         g_xdndActionCopyAtom = 0;
static Atom         g_xdndActionMoveAtom = 0;
static Atom         g_xdndActionLinkAtom = 0;
static Atom         g_xdndDataAtom = 0;
static Atom         g_uriListAtom = 0;
static Window       g_clipboardWindow = 0;
static std::string  g_clipboardUtf8;
static std::recursive_mutex g_clipboardMutex;
static XIM          g_xim = nullptr;
static int          g_epollFd = -1;
static int          g_wakeEventFd = -1;   // eventfd for cross-thread wake
static std::atomic<bool> g_quitRequested{false};
static std::atomic<int32_t> g_exitCode{0};
static std::atomic<uint64_t> g_dragSessionCounter{1};

enum class LinuxWindowSystem { Disabled, XServer, Wayland };
static LinuxWindowSystem g_windowSystem = LinuxWindowSystem::Disabled;

#ifdef JALIUM_HAS_WAYLAND
static wl_display* g_waylandDisplay = nullptr;
static wl_registry* g_waylandRegistry = nullptr;
static wl_compositor* g_waylandCompositor = nullptr;
static wl_shm* g_waylandShm = nullptr;
static xdg_wm_base* g_xdgWmBase = nullptr;
static wl_seat* g_waylandSeat = nullptr;
static wl_pointer* g_waylandPointer = nullptr;
static wl_keyboard* g_waylandKeyboard = nullptr;
static xkb_context* g_xkbContext = nullptr;
static xkb_keymap* g_xkbKeymap = nullptr;
static xkb_state* g_xkbState = nullptr;
static int g_waylandFd = -1;
static JaliumPlatformWindow* g_pointerFocus = nullptr;
static JaliumPlatformWindow* g_keyboardFocus = nullptr;
static float g_pointerX = 0;
static float g_pointerY = 0;
static uint32_t g_waylandModifiers = 0;
static uint32_t g_waylandInputSerial = 0;
static uint32_t g_waylandPointerSerial = 0;
static uint32_t g_waylandPointerButtons = 0;
static wl_data_device_manager* g_waylandDataDeviceManager = nullptr;
static wl_data_device* g_waylandDataDevice = nullptr;
static wl_data_source* g_waylandClipboardSource = nullptr;
static bool g_waylandCompositionActive = false;

struct WaylandDataOfferState {
    wl_data_offer* offer = nullptr;
    std::vector<std::string> mimeTypes;
    uint32_t sourceActions = WL_DATA_DEVICE_MANAGER_DND_ACTION_NONE;
    uint32_t selectedAction = WL_DATA_DEVICE_MANAGER_DND_ACTION_NONE;
};
static std::unordered_map<wl_data_offer*, WaylandDataOfferState*> g_waylandOffers;
static WaylandDataOfferState* g_waylandSelectionOffer = nullptr;
static WaylandDataOfferState* g_waylandDragOffer = nullptr;
static JaliumPlatformWindow* g_waylandDragWindow = nullptr;
static uint32_t g_waylandDragSerial = 0;
static bool g_waylandDropPending = false;
static std::string g_waylandDragMime;

#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
static zwp_text_input_manager_v3* g_waylandTextInputManager = nullptr;
static zwp_text_input_v3* g_waylandTextInput = nullptr;
static std::string g_pendingWaylandPreedit;
static std::string g_pendingWaylandCommit;
static int32_t g_pendingWaylandPreeditCursor = 0;
static bool g_pendingWaylandPreeditSet = false;
static bool g_pendingWaylandCommitSet = false;
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
static zwp_text_input_manager_v1* g_waylandTextInputManagerV1 = nullptr;
static zwp_text_input_v1* g_waylandTextInputV1 = nullptr;
static uint32_t g_waylandTextInputSerialV1 = 0;
static int32_t g_waylandTextInputCursorV1 = 0;
static bool g_waylandTextInputV1Active = false;
#endif

static int g_waylandRepeatFd = -1;
static int32_t g_waylandRepeatRate = 0;
static int32_t g_waylandRepeatDelay = 0;
static JaliumPlatformWindow* g_waylandRepeatWindow = nullptr;
static uint32_t g_waylandRepeatKey = 0;
static xkb_keycode_t g_waylandRepeatKeycode = 0;
static xkb_keysym_t g_waylandRepeatSymbol = XKB_KEY_NoSymbol;
static int32_t ProcessWaylandRepeatReady();
#endif

// Bit 0 is the toggle state and bit 7 is the down state.  The public API maps
// these to Win32-compatible low/high result bits so managed Keyboard polling
// agrees with the event stream on both X11 and Wayland.
static std::array<std::atomic<uint8_t>, 256> g_keyStates{};

struct ClickTracker {
    uint32_t time = 0;
    float x = 0;
    float y = 0;
    int32_t button = -1;
    JaliumPlatformWindow* window = nullptr;
    int32_t count = 0;
};
static ClickTracker g_x11ClickTracker;
#ifdef JALIUM_HAS_WAYLAND
static ClickTracker g_waylandClickTracker;
#endif

static std::mutex g_windowMapMutex;
static std::unordered_map<Window, JaliumPlatformWindow*> g_windowMap;
#ifdef JALIUM_HAS_WAYLAND
static std::unordered_set<JaliumPlatformWindow*> g_waylandWindows;
#endif

// ============================================================================
// Window Structure
// ============================================================================

struct JaliumPlatformWindow {
    Window              xwindow = 0;
    XIC                 xic = nullptr;
    XIMCallback         ximPreeditStart{};
    XIMCallback         ximPreeditDone{};
    XIMCallback         ximPreeditDraw{};
    XIMCallback         ximPreeditCaret{};
    std::u32string      ximPreedit;
    JaliumEventCallback callback = nullptr;
    void*               userData = nullptr;
    uint32_t            style = 0;
    int32_t             width = 0;
    int32_t             height = 0;
    float               dpiScale = 1.0f;
    bool                destroyed = false;
    JaliumWindowState   state = JALIUM_WINDOW_STATE_NORMAL;
    int32_t             x = 0;
    int32_t             y = 0;
    uint64_t            dragSessionId = 0;
    uint32_t            dragSelectedEffect = JALIUM_DRAG_EFFECT_NONE;

    // X11 XDND target state. These members stay inert for Wayland windows.
    Window              xdndSource = 0;
    uint32_t            xdndVersion = 0;
    std::vector<Atom>   xdndMimeAtoms;
    Atom                xdndSelectedMime = None;
    uint32_t            xdndAllowedEffects = JALIUM_DRAG_EFFECT_NONE;
    Time                xdndTimestamp = CurrentTime;
    float               xdndX = 0;
    float               xdndY = 0;

#ifdef JALIUM_HAS_WAYLAND
    wl_surface*         waylandSurface = nullptr;
    xdg_surface*        xdgSurface = nullptr;
    xdg_toplevel*       xdgToplevel = nullptr;
    std::string         waylandTitle;
    std::atomic<bool>   waylandConfigured{false};
    std::atomic<bool>   waylandVisible{false};
    std::atomic<bool>   waylandPaintPending{false};
    std::atomic<bool>   waylandDispatchingPaint{false};
    bool                waylandActivated = false;
#endif

    void DispatchEvent(const JaliumPlatformEvent& evt)
    {
        if (callback && !destroyed)
            callback(&evt, userData);
    }
};

struct OwnedDragItem {
    std::string mimeType;
    std::vector<uint8_t> bytes;
    Atom x11Atom = None;
};

struct X11DragSourceState {
    JaliumPlatformWindow* window = nullptr;
    std::vector<OwnedDragItem> items;
    uint32_t allowedEffects = JALIUM_DRAG_EFFECT_NONE;
    Window target = 0;
    Atom requestedAction = None;
    bool targetAccepted = false;
    bool dropSent = false;
    bool finished = false;
    uint32_t performedEffect = JALIUM_DRAG_EFFECT_NONE;
};

static X11DragSourceState* g_x11DragSource = nullptr;

#ifdef JALIUM_HAS_WAYLAND
struct WaylandDragSourceState {
    JaliumPlatformWindow* window = nullptr;
    wl_data_source* source = nullptr;
    std::vector<OwnedDragItem> items;
    uint32_t allowedEffects = JALIUM_DRAG_EFFECT_NONE;
    uint32_t selectedAction = WL_DATA_DEVICE_MANAGER_DND_ACTION_NONE;
    uint32_t performedEffect = JALIUM_DRAG_EFFECT_NONE;
    bool dropPerformed = false;
    bool finished = false;
    bool cancelled = false;
};
static WaylandDragSourceState* g_waylandDragSource = nullptr;
static bool ProcessPendingWaylandDrop();
#endif

static JaliumPlatformWindow* FindWindow(Window xwin);

static uint32_t XdndActionToEffect(Atom action)
{
    if (action == g_xdndActionCopyAtom) return JALIUM_DRAG_EFFECT_COPY;
    if (action == g_xdndActionMoveAtom) return JALIUM_DRAG_EFFECT_MOVE;
    if (action == g_xdndActionLinkAtom) return JALIUM_DRAG_EFFECT_LINK;
    return JALIUM_DRAG_EFFECT_NONE;
}

static Atom XdndEffectToAction(uint32_t effect)
{
    if (effect & JALIUM_DRAG_EFFECT_COPY) return g_xdndActionCopyAtom;
    if (effect & JALIUM_DRAG_EFFECT_MOVE) return g_xdndActionMoveAtom;
    if (effect & JALIUM_DRAG_EFFECT_LINK) return g_xdndActionLinkAtom;
    return None;
}

static uint32_t ReadXdndActionMask(Window source)
{
    Atom actualType = None;
    int actualFormat = 0;
    unsigned long itemCount = 0;
    unsigned long remaining = 0;
    unsigned char* value = nullptr;
    uint32_t mask = JALIUM_DRAG_EFFECT_NONE;
    if (XGetWindowProperty(
            g_display, source, g_xdndActionListAtom, 0, 32, False, XA_ATOM,
            &actualType, &actualFormat, &itemCount, &remaining, &value) == Success &&
        actualType == XA_ATOM && actualFormat == 32 && value)
    {
        const auto* atoms = reinterpret_cast<const unsigned long*>(value);
        for (unsigned long index = 0; index < itemCount; ++index)
            mask |= XdndActionToEffect(static_cast<Atom>(atoms[index]));
    }
    if (value) XFree(value);
    return mask;
}

static std::vector<Atom> ReadXdndTypeList(Window source)
{
    std::vector<Atom> result;
    Atom actualType = None;
    int actualFormat = 0;
    unsigned long itemCount = 0;
    unsigned long remaining = 0;
    unsigned char* value = nullptr;
    if (XGetWindowProperty(
            g_display, source, g_xdndTypeListAtom, 0, 4096, False, XA_ATOM,
            &actualType, &actualFormat, &itemCount, &remaining, &value) == Success &&
        actualType == XA_ATOM && actualFormat == 32 && value)
    {
        const auto* atoms = reinterpret_cast<const unsigned long*>(value);
        result.reserve(itemCount);
        for (unsigned long index = 0; index < itemCount; ++index)
            result.push_back(static_cast<Atom>(atoms[index]));
    }
    if (value) XFree(value);
    return result;
}

static Atom SelectXdndMime(const std::vector<Atom>& types)
{
    const Atom preferred[] = {
        g_uriListAtom, g_textPlainUtf8Atom, g_utf8StringAtom,
        g_textPlainAtom, XA_STRING
    };
    for (Atom candidate : preferred)
        if (std::find(types.begin(), types.end(), candidate) != types.end())
            return candidate;
    return types.empty() ? None : types.front();
}

static std::string JoinXdndMimeNames(const std::vector<Atom>& atoms)
{
    std::string result;
    for (Atom atom : atoms)
    {
        char* name = XGetAtomName(g_display, atom);
        if (!name) continue;
        if (!result.empty()) result.push_back('\n');
        result.append(name);
        XFree(name);
    }
    return result;
}

static uint32_t QueryXdndKeyStates(JaliumPlatformWindow* window)
{
    if (!window || !window->xwindow) return 0;
    Window root = 0;
    Window child = 0;
    int rootX = 0;
    int rootY = 0;
    int windowX = 0;
    int windowY = 0;
    unsigned int mask = 0;
    if (!XQueryPointer(g_display, window->xwindow, &root, &child,
                       &rootX, &rootY, &windowX, &windowY, &mask))
        return 0;
    uint32_t states = 0;
    if (mask & Button1Mask) states |= 1u;
    if (mask & Button3Mask) states |= 2u;
    if (mask & ShiftMask) states |= 4u;
    if (mask & ControlMask) states |= 8u;
    if (mask & Button2Mask) states |= 16u;
    if (mask & Mod1Mask) states |= 32u;
    return states;
}

static void DispatchXdndTargetEvent(
    JaliumPlatformWindow* window, JaliumEventType type,
    const char* dataMimeType = nullptr,
    const uint8_t* data = nullptr, uint32_t dataSize = 0)
{
    if (!window) return;
    const std::string mimeTypes = JoinXdndMimeNames(window->xdndMimeAtoms);
    JaliumPlatformEvent event{};
    event.type = type;
    event.window = window;
    event.drag.x = window->xdndX;
    event.drag.y = window->xdndY;
    event.drag.keyStates = QueryXdndKeyStates(window);
    event.drag.allowedEffects = window->xdndAllowedEffects;
    event.drag.sessionId = window->dragSessionId;
    event.drag.mimeTypes = mimeTypes.c_str();
    event.drag.dataMimeType = dataMimeType;
    event.drag.data = data;
    event.drag.dataSize = dataSize;
    window->DispatchEvent(event);
}

static void SendXdndClientMessage(Window destination, Atom messageType,
                                  long value0, long value1 = 0,
                                  long value2 = 0, long value3 = 0,
                                  long value4 = 0)
{
    if (!destination) return;
    XEvent event{};
    event.xclient.type = ClientMessage;
    event.xclient.display = g_display;
    event.xclient.window = destination;
    event.xclient.message_type = messageType;
    event.xclient.format = 32;
    event.xclient.data.l[0] = value0;
    event.xclient.data.l[1] = value1;
    event.xclient.data.l[2] = value2;
    event.xclient.data.l[3] = value3;
    event.xclient.data.l[4] = value4;
    XSendEvent(g_display, destination, False, NoEventMask, &event);
    XFlush(g_display);
}

static void ClearXdndTarget(JaliumPlatformWindow* window, bool dispatchLeave)
{
    if (!window) return;
    if (dispatchLeave && window->xdndSource)
        DispatchXdndTargetEvent(window, JALIUM_EVENT_DRAG_LEAVE);
    window->xdndSource = 0;
    window->xdndVersion = 0;
    window->xdndMimeAtoms.clear();
    window->xdndSelectedMime = None;
    window->xdndAllowedEffects = JALIUM_DRAG_EFFECT_NONE;
    window->xdndTimestamp = CurrentTime;
    window->dragSessionId = 0;
    window->dragSelectedEffect = JALIUM_DRAG_EFFECT_NONE;
}

static void SendXdndFinished(JaliumPlatformWindow* window, bool success)
{
    if (!window || !window->xdndSource) return;
    const uint32_t effect = success ? window->dragSelectedEffect : 0;
    SendXdndClientMessage(
        window->xdndSource, g_xdndFinishedAtom,
        static_cast<long>(window->xwindow),
        success ? 1L : 0L,
        static_cast<long>(XdndEffectToAction(effect)));
}

static bool ServeXdndSelectionRequest(const XSelectionRequestEvent& request)
{
    if (!g_x11DragSource || request.selection != g_xdndSelectionAtom ||
        request.owner != g_x11DragSource->window->xwindow)
        return false;

    XSelectionEvent response{};
    response.type = SelectionNotify;
    response.display = request.display;
    response.requestor = request.requestor;
    response.selection = request.selection;
    response.target = request.target;
    response.time = request.time;
    response.property = None;
    const Atom property = request.property != None ? request.property : request.target;

    if (request.target == g_targetsAtom)
    {
        std::vector<Atom> targets;
        targets.reserve(g_x11DragSource->items.size() + 1);
        targets.push_back(g_targetsAtom);
        for (const OwnedDragItem& item : g_x11DragSource->items)
            targets.push_back(item.x11Atom);
        XChangeProperty(
            g_display, request.requestor, property, XA_ATOM, 32,
            PropModeReplace,
            reinterpret_cast<const unsigned char*>(targets.data()),
            static_cast<int>(targets.size()));
        response.property = property;
    }
    else
    {
        const auto iterator = std::find_if(
            g_x11DragSource->items.begin(), g_x11DragSource->items.end(),
            [&request](const OwnedDragItem& item) { return item.x11Atom == request.target; });
        if (iterator != g_x11DragSource->items.end())
        {
            XChangeProperty(
                g_display, request.requestor, property, request.target, 8,
                PropModeReplace,
                iterator->bytes.empty() ? nullptr : iterator->bytes.data(),
                static_cast<int>(iterator->bytes.size()));
            response.property = property;
        }
    }

    XSendEvent(g_display, request.requestor, False, NoEventMask,
               reinterpret_cast<XEvent*>(&response));
    XFlush(g_display);
    return true;
}

static bool CompleteXdndDrop(const XSelectionEvent& selection)
{
    if (selection.selection != g_xdndSelectionAtom) return false;
    JaliumPlatformWindow* window = FindWindow(selection.requestor);
    if (!window || !window->xdndSource) return false;
    if (selection.property == None)
    {
        SendXdndFinished(window, false);
        ClearXdndTarget(window, false);
        return true;
    }

    Atom actualType = None;
    int actualFormat = 0;
    unsigned long itemCount = 0;
    unsigned long remaining = 0;
    unsigned char* value = nullptr;
    const int status = XGetWindowProperty(
        g_display, window->xwindow, selection.property, 0, 0x1fffffff,
        True, AnyPropertyType, &actualType, &actualFormat,
        &itemCount, &remaining, &value);
    if (status != Success || actualFormat != 8 || actualType == g_incrAtom)
    {
        if (value) XFree(value);
        SendXdndFinished(window, false);
        ClearXdndTarget(window, false);
        return true;
    }

    char* mimeName = XGetAtomName(g_display, window->xdndSelectedMime);
    std::vector<uint8_t> utf8;
    const uint8_t* bytes = value;
    uint32_t byteCount = static_cast<uint32_t>(itemCount);
    if (window->xdndSelectedMime == XA_STRING && value)
    {
        utf8.reserve(itemCount * 2);
        for (unsigned long index = 0; index < itemCount; ++index)
        {
            const uint8_t code = value[index];
            if (code < 0x80) utf8.push_back(code);
            else
            {
                utf8.push_back(static_cast<uint8_t>(0xC0u | (code >> 6)));
                utf8.push_back(static_cast<uint8_t>(0x80u | (code & 0x3Fu)));
            }
        }
        bytes = utf8.data();
        byteCount = static_cast<uint32_t>(utf8.size());
    }

    window->dragSelectedEffect = JALIUM_DRAG_EFFECT_NONE;
    DispatchXdndTargetEvent(window, JALIUM_EVENT_DROP, mimeName, bytes, byteCount);
    const bool success = window->dragSelectedEffect != JALIUM_DRAG_EFFECT_NONE;
    SendXdndFinished(window, success);
    if (mimeName) XFree(mimeName);
    if (value) XFree(value);
    ClearXdndTarget(window, false);
    return true;
}

static bool ProcessXdndClientMessage(const XClientMessageEvent& message)
{
    if (message.message_type == g_xdndStatusAtom && g_x11DragSource)
    {
        if (static_cast<Window>(message.data.l[0]) == g_x11DragSource->target)
        {
            g_x11DragSource->targetAccepted = (message.data.l[1] & 1L) != 0;
            g_x11DragSource->requestedAction = static_cast<Atom>(message.data.l[4]);
        }
        return true;
    }
    if (message.message_type == g_xdndFinishedAtom && g_x11DragSource)
    {
        if (static_cast<Window>(message.data.l[0]) == g_x11DragSource->target)
        {
            const bool success = (message.data.l[1] & 1L) != 0;
            g_x11DragSource->performedEffect = success
                ? XdndActionToEffect(static_cast<Atom>(message.data.l[2]))
                : JALIUM_DRAG_EFFECT_NONE;
            g_x11DragSource->finished = true;
        }
        return true;
    }

    JaliumPlatformWindow* window = FindWindow(message.window);
    if (!window) return false;
    if (message.message_type == g_xdndEnterAtom)
    {
        if (window->xdndSource) ClearXdndTarget(window, true);
        window->xdndSource = static_cast<Window>(message.data.l[0]);
        window->xdndVersion = static_cast<uint32_t>(
            (static_cast<unsigned long>(message.data.l[1]) >> 24) & 0xffu);
        const bool hasTypeList = (message.data.l[1] & 1L) != 0;
        window->xdndMimeAtoms = hasTypeList
            ? ReadXdndTypeList(window->xdndSource)
            : std::vector<Atom>{
                static_cast<Atom>(message.data.l[2]),
                static_cast<Atom>(message.data.l[3]),
                static_cast<Atom>(message.data.l[4]) };
        window->xdndMimeAtoms.erase(
            std::remove(window->xdndMimeAtoms.begin(), window->xdndMimeAtoms.end(), None),
            window->xdndMimeAtoms.end());
        window->xdndSelectedMime = SelectXdndMime(window->xdndMimeAtoms);
        window->xdndAllowedEffects = ReadXdndActionMask(window->xdndSource);
        if (window->xdndAllowedEffects == JALIUM_DRAG_EFFECT_NONE)
            window->xdndAllowedEffects = JALIUM_DRAG_EFFECT_COPY |
                                         JALIUM_DRAG_EFFECT_MOVE |
                                         JALIUM_DRAG_EFFECT_LINK;
        window->dragSessionId = g_dragSessionCounter.fetch_add(1, std::memory_order_relaxed);
        window->dragSelectedEffect = JALIUM_DRAG_EFFECT_NONE;
        DispatchXdndTargetEvent(window, JALIUM_EVENT_DRAG_ENTER);
        return true;
    }
    if (message.message_type == g_xdndPositionAtom)
    {
        if (window->xdndSource != static_cast<Window>(message.data.l[0])) return true;
        const unsigned long packed = static_cast<unsigned long>(message.data.l[2]);
        const int rootX = static_cast<int16_t>((packed >> 16) & 0xffffu);
        const int rootY = static_cast<int16_t>(packed & 0xffffu);
        int localX = 0;
        int localY = 0;
        Window child = 0;
        XTranslateCoordinates(g_display, g_rootWindow, window->xwindow,
                              rootX, rootY, &localX, &localY, &child);
        window->xdndX = static_cast<float>(localX);
        window->xdndY = static_cast<float>(localY);
        window->xdndTimestamp = static_cast<Time>(message.data.l[3]);
        const uint32_t requested = XdndActionToEffect(static_cast<Atom>(message.data.l[4]));
        if (requested != JALIUM_DRAG_EFFECT_NONE)
            window->xdndAllowedEffects |= requested;
        window->dragSelectedEffect = JALIUM_DRAG_EFFECT_NONE;
        DispatchXdndTargetEvent(window, JALIUM_EVENT_DRAG_OVER);
        window->dragSelectedEffect &= window->xdndAllowedEffects;
        const bool accepted = window->xdndSelectedMime != None &&
                              window->dragSelectedEffect != JALIUM_DRAG_EFFECT_NONE;
        SendXdndClientMessage(window->xdndSource, g_xdndStatusAtom,
            static_cast<long>(window->xwindow), accepted ? 3L : 2L,
            0, 0, static_cast<long>(XdndEffectToAction(window->dragSelectedEffect)));
        return true;
    }
    if (message.message_type == g_xdndLeaveAtom)
    {
        if (window->xdndSource == static_cast<Window>(message.data.l[0]))
            ClearXdndTarget(window, true);
        return true;
    }
    if (message.message_type == g_xdndDropAtom)
    {
        if (window->xdndSource != static_cast<Window>(message.data.l[0])) return true;
        window->xdndTimestamp = static_cast<Time>(message.data.l[2]);
        if (window->xdndSelectedMime == None ||
            window->dragSelectedEffect == JALIUM_DRAG_EFFECT_NONE)
        {
            SendXdndFinished(window, false);
            ClearXdndTarget(window, false);
            return true;
        }
        XDeleteProperty(g_display, window->xwindow, g_xdndDataAtom);
        XConvertSelection(g_display, g_xdndSelectionAtom, window->xdndSelectedMime,
                          g_xdndDataAtom, window->xwindow, window->xdndTimestamp);
        XFlush(g_display);
        return true;
    }
    return false;
}

static bool ProcessXdndXEvent(XEvent& event)
{
    if (event.type == ClientMessage)
        return ProcessXdndClientMessage(event.xclient);
    if (event.type == SelectionRequest)
        return ServeXdndSelectionRequest(event.xselectionrequest);
    if (event.type == SelectionNotify)
        return CompleteXdndDrop(event.xselection);
    return false;
}

// ============================================================================
// Dispatcher Structure
// ============================================================================

struct JaliumDispatcher {
    std::atomic<int>                      eventFd{-1};
    std::atomic<JaliumDispatcherCallback> callback{nullptr};
    std::atomic<void*>                    userData{nullptr};
    std::atomic<uint32_t>                 references{1};
    std::atomic<bool>                     destroyed{false};
    std::recursive_mutex                  callbackMutex;
};

// ============================================================================
// Timer Structure
// ============================================================================

struct JaliumTimer {
    std::atomic<int>                 timerFd{-1};
    std::atomic<JaliumTimerCallback> callback{nullptr};
    std::atomic<void*>               userData{nullptr};
    std::atomic<uint32_t>            references{1};
    std::atomic<bool>                registeredWithEpoll{false};
    std::atomic<bool>                destroyed{false};
    std::recursive_mutex             callbackMutex;
};

// epoll only gives us an fd back, so keep a guarded fd-to-object registry.
// The short-lived reference acquired by ProcessReadyFd lets a callback destroy
// its own dispatcher/timer safely without invalidating the current invocation.
static std::mutex g_eventSourceMutex;
static std::unordered_map<int, JaliumDispatcher*> g_dispatchersByFd;
static std::unordered_map<int, JaliumTimer*> g_timersByFd;
static std::unordered_set<JaliumDispatcher*> g_allDispatchers;
static std::unordered_set<JaliumTimer*> g_allTimers;

static void EnsureClipboardAtoms();
static bool ProcessClipboardXEvent(XEvent& event);

static bool InputDiagnosticsEnabled()
{
    const char* value = getenv("JALIUM_INPUT_DIAGNOSTICS");
    return value && *value && strcmp(value, "0") != 0;
}
static void EnsureXdndAtoms()
{
    if (!g_display || g_xdndAwareAtom != None) return;
    g_xdndAwareAtom = XInternAtom(g_display, "XdndAware", False);
    g_xdndEnterAtom = XInternAtom(g_display, "XdndEnter", False);
    g_xdndPositionAtom = XInternAtom(g_display, "XdndPosition", False);
    g_xdndStatusAtom = XInternAtom(g_display, "XdndStatus", False);
    g_xdndLeaveAtom = XInternAtom(g_display, "XdndLeave", False);
    g_xdndDropAtom = XInternAtom(g_display, "XdndDrop", False);
    g_xdndFinishedAtom = XInternAtom(g_display, "XdndFinished", False);
    g_xdndSelectionAtom = XInternAtom(g_display, "XdndSelection", False);
    g_xdndTypeListAtom = XInternAtom(g_display, "XdndTypeList", False);
    g_xdndActionListAtom = XInternAtom(g_display, "XdndActionList", False);
    g_xdndActionCopyAtom = XInternAtom(g_display, "XdndActionCopy", False);
    g_xdndActionMoveAtom = XInternAtom(g_display, "XdndActionMove", False);
    g_xdndActionLinkAtom = XInternAtom(g_display, "XdndActionLink", False);
    g_xdndDataAtom = XInternAtom(g_display, "JALIUM_XDND_DATA", False);
    g_uriListAtom = XInternAtom(g_display, "text/uri-list", False);
}
static bool ProcessXdndXEvent(XEvent& event);

static void ReleaseDispatcher(JaliumDispatcher* dispatcher)
{
    if (dispatcher && dispatcher->references.fetch_sub(1, std::memory_order_acq_rel) == 1)
        delete dispatcher;
}

static void ReleaseTimer(JaliumTimer* timer)
{
    if (timer && timer->references.fetch_sub(1, std::memory_order_acq_rel) == 1)
        delete timer;
}

static std::string Utf16ToUtf8(const JaliumUtf16Char* text)
{
    std::string result;
    if (!text) return result;

    auto appendCodePoint = [&result](uint32_t codePoint)
    {
        if (codePoint <= 0x7Fu)
        {
            result.push_back(static_cast<char>(codePoint));
        }
        else if (codePoint <= 0x7FFu)
        {
            result.push_back(static_cast<char>(0xC0u | (codePoint >> 6)));
            result.push_back(static_cast<char>(0x80u | (codePoint & 0x3Fu)));
        }
        else if (codePoint <= 0xFFFFu)
        {
            result.push_back(static_cast<char>(0xE0u | (codePoint >> 12)));
            result.push_back(static_cast<char>(0x80u | ((codePoint >> 6) & 0x3Fu)));
            result.push_back(static_cast<char>(0x80u | (codePoint & 0x3Fu)));
        }
        else
        {
            result.push_back(static_cast<char>(0xF0u | (codePoint >> 18)));
            result.push_back(static_cast<char>(0x80u | ((codePoint >> 12) & 0x3Fu)));
            result.push_back(static_cast<char>(0x80u | ((codePoint >> 6) & 0x3Fu)));
            result.push_back(static_cast<char>(0x80u | (codePoint & 0x3Fu)));
        }
    };

    for (size_t index = 0; text[index] != 0; ++index)
    {
        uint32_t codePoint = text[index];
        if (codePoint >= 0xD800u && codePoint <= 0xDBFFu)
        {
            const uint32_t low = text[index + 1];
            if (low >= 0xDC00u && low <= 0xDFFFu)
            {
                codePoint = 0x10000u + ((codePoint - 0xD800u) << 10) + (low - 0xDC00u);
                ++index;
            }
            else
            {
                codePoint = 0xFFFDu;
            }
        }
        else if (codePoint >= 0xDC00u && codePoint <= 0xDFFFu)
        {
            codePoint = 0xFFFDu;
        }

        appendCodePoint(codePoint);
    }

    return result;
}

static std::u32string Utf8ToUtf32(const char* text, size_t length)
{
    std::u32string result;
    if (!text) return result;
    size_t index = 0;
    while (index < length)
    {
        const uint8_t first = static_cast<uint8_t>(text[index++]);
        uint32_t codePoint = 0;
        size_t continuationCount = 0;
        if (first < 0x80u) codePoint = first;
        else if ((first & 0xE0u) == 0xC0u) { codePoint = first & 0x1Fu; continuationCount = 1; }
        else if ((first & 0xF0u) == 0xE0u) { codePoint = first & 0x0Fu; continuationCount = 2; }
        else if ((first & 0xF8u) == 0xF0u) { codePoint = first & 0x07u; continuationCount = 3; }
        else { result.push_back(0xFFFDu); continue; }

        bool valid = index + continuationCount <= length;
        for (size_t continuation = 0; valid && continuation < continuationCount; ++continuation)
        {
            const uint8_t value = static_cast<uint8_t>(text[index + continuation]);
            if ((value & 0xC0u) != 0x80u) valid = false;
            else codePoint = (codePoint << 6) | (value & 0x3Fu);
        }
        if (!valid)
        {
            result.push_back(0xFFFDu);
            continue;
        }
        index += continuationCount;
        const bool overlong = (continuationCount == 1 && codePoint < 0x80u) ||
                              (continuationCount == 2 && codePoint < 0x800u) ||
                              (continuationCount == 3 && codePoint < 0x10000u);
        if (overlong || codePoint > 0x10FFFFu ||
            (codePoint >= 0xD800u && codePoint <= 0xDFFFu))
            codePoint = 0xFFFDu;
        result.push_back(static_cast<char32_t>(codePoint));
    }
    return result;
}

static std::string Utf32ToUtf8(const std::u32string& text)
{
    std::string result;
    for (uint32_t codePoint : text)
    {
        if (codePoint <= 0x7Fu) result.push_back(static_cast<char>(codePoint));
        else if (codePoint <= 0x7FFu)
        {
            result.push_back(static_cast<char>(0xC0u | (codePoint >> 6)));
            result.push_back(static_cast<char>(0x80u | (codePoint & 0x3Fu)));
        }
        else if (codePoint <= 0xFFFFu)
        {
            result.push_back(static_cast<char>(0xE0u | (codePoint >> 12)));
            result.push_back(static_cast<char>(0x80u | ((codePoint >> 6) & 0x3Fu)));
            result.push_back(static_cast<char>(0x80u | (codePoint & 0x3Fu)));
        }
        else
        {
            result.push_back(static_cast<char>(0xF0u | (codePoint >> 18)));
            result.push_back(static_cast<char>(0x80u | ((codePoint >> 12) & 0x3Fu)));
            result.push_back(static_cast<char>(0x80u | ((codePoint >> 6) & 0x3Fu)));
            result.push_back(static_cast<char>(0x80u | (codePoint & 0x3Fu)));
        }
    }
    return result;
}

static JaliumUtf16Char* Utf8ToUtf16Allocated(const std::string& text)
{
    const std::u32string codePoints = Utf8ToUtf32(text.data(), text.size());
    size_t codeUnits = 0;
    for (uint32_t codePoint : codePoints) codeUnits += codePoint > 0xFFFFu ? 2 : 1;
    auto* result = static_cast<JaliumUtf16Char*>(
        malloc((codeUnits + 1) * sizeof(JaliumUtf16Char)));
    if (!result) return nullptr;
    size_t output = 0;
    for (uint32_t codePoint : codePoints)
    {
        if (codePoint <= 0xFFFFu) result[output++] = static_cast<JaliumUtf16Char>(codePoint);
        else
        {
            codePoint -= 0x10000u;
            result[output++] = static_cast<JaliumUtf16Char>(0xD800u + (codePoint >> 10));
            result[output++] = static_cast<JaliumUtf16Char>(0xDC00u + (codePoint & 0x3FFu));
        }
    }
    result[output] = 0;
    return result;
}

static int32_t Utf8ByteOffsetToUtf16(const std::string& text, int32_t byteOffset)
{
    const size_t clamped = static_cast<size_t>(std::clamp<int32_t>(
        byteOffset, 0, static_cast<int32_t>(text.size())));
    int32_t codeUnits = 0;
    for (uint32_t codePoint : Utf8ToUtf32(text.data(), clamped))
        codeUnits += codePoint > 0xFFFFu ? 2 : 1;
    return codeUnits;
}

static void DispatchUtf8Characters(JaliumPlatformWindow* window, const std::string& text)
{
    if (!window) return;
    for (uint32_t codePoint : Utf8ToUtf32(text.data(), text.size()))
    {
        if (codePoint < 0x20u || codePoint == 0x7Fu) continue;
        JaliumPlatformEvent event{};
        event.type = JALIUM_EVENT_CHAR_INPUT;
        event.window = window;
        event.character.codepoint = codePoint;
        window->DispatchEvent(event);
    }
}

static void DispatchComposition(
    JaliumPlatformWindow* window, JaliumEventType type,
    const std::string& text, int32_t cursor)
{
    if (!window) return;
    JaliumPlatformEvent event{};
    event.type = type;
    event.window = window;
    event.composition.utf8Text = text.c_str();
    event.composition.cursor = cursor;
    window->DispatchEvent(event);
}

static void SetKeyState(int32_t virtualKey, bool down, bool toggleOnPress)
{
    if (virtualKey < 0 || virtualKey >= static_cast<int32_t>(g_keyStates.size())) return;
    uint8_t previous = g_keyStates[virtualKey].load(std::memory_order_relaxed);
    uint8_t next;
    do
    {
        next = down ? static_cast<uint8_t>(previous | 0x80u)
                    : static_cast<uint8_t>(previous & ~0x80u);
        if (down && toggleOnPress && !(previous & 0x80u)) next ^= 0x01u;
    }
    while (!g_keyStates[virtualKey].compare_exchange_weak(
        previous, next, std::memory_order_release, std::memory_order_relaxed));
}

static void SetToggleState(int32_t virtualKey, bool toggled)
{
    if (virtualKey < 0 || virtualKey >= static_cast<int32_t>(g_keyStates.size())) return;
    if (toggled) g_keyStates[virtualKey].fetch_or(0x01u, std::memory_order_release);
    else g_keyStates[virtualKey].fetch_and(0x80u, std::memory_order_release);
}

static void ClearPressedKeyStates()
{
    for (auto& state : g_keyStates)
        state.fetch_and(0x01u, std::memory_order_release);
}

static int32_t RegisterClick(ClickTracker& tracker, JaliumPlatformWindow* window,
                             int32_t button, uint32_t time, float x, float y)
{
    constexpr uint32_t DoubleClickMilliseconds = 500;
    constexpr float DoubleClickDistance = 4.0f;
    const bool continues = tracker.window == window && tracker.button == button &&
        static_cast<uint32_t>(time - tracker.time) <= DoubleClickMilliseconds &&
        std::fabs(x - tracker.x) <= DoubleClickDistance &&
        std::fabs(y - tracker.y) <= DoubleClickDistance;
    tracker.count = continues ? std::min(tracker.count + 1, 3) : 1;
    tracker.window = window;
    tracker.button = button;
    tracker.time = time;
    tracker.x = x;
    tracker.y = y;
    return tracker.count;
}

static void SetX11WindowTitle(Window window, const JaliumUtf16Char* title)
{
    if (!g_display || !window) return;

    const std::string utf8Title = Utf16ToUtf8(title);
    XStoreName(g_display, window, utf8Title.c_str());

    const Atom netWmName = XInternAtom(g_display, "_NET_WM_NAME", False);
    const Atom utf8String = XInternAtom(g_display, "UTF8_STRING", False);
    XChangeProperty(g_display, window, netWmName, utf8String,
                    8, PropModeReplace,
                    reinterpret_cast<const unsigned char*>(utf8Title.data()),
                    static_cast<int>(utf8Title.size()));
}

static bool AddEpollFd(int fd)
{
    if (g_epollFd < 0 || fd < 0) return false;

    struct epoll_event event{};
    event.events = EPOLLIN;
    event.data.fd = fd;
    if (epoll_ctl(g_epollFd, EPOLL_CTL_ADD, fd, &event) == 0)
        return true;

    // Registration is idempotent for callers which replace a callback.
    return errno == EEXIST &&
           epoll_ctl(g_epollFd, EPOLL_CTL_MOD, fd, &event) == 0;
}

static void RemoveEpollFd(int fd)
{
    if (g_epollFd >= 0 && fd >= 0)
        (void)epoll_ctl(g_epollFd, EPOLL_CTL_DEL, fd, nullptr);
}

static bool SignalEventFd(int fd)
{
    if (fd < 0) return false;

    const uint64_t value = 1;
    ssize_t written;
    do
    {
        written = write(fd, &value, sizeof(value));
    }
    while (written < 0 && errno == EINTR);

    // EAGAIN means the counter is already saturated and therefore signalled.
    return written == static_cast<ssize_t>(sizeof(value)) ||
           (written < 0 && (errno == EAGAIN || errno == EWOULDBLOCK));
}

static uint64_t DrainCounterFd(int fd)
{
    uint64_t total = 0;
    for (;;)
    {
        uint64_t value = 0;
        const ssize_t bytesRead = read(fd, &value, sizeof(value));
        if (bytesRead == static_cast<ssize_t>(sizeof(value)))
        {
            total += value;
            continue;
        }
        if (bytesRead < 0 && errno == EINTR)
            continue;
        break;
    }
    return total;
}

static JaliumDispatcher* AcquireDispatcherForFd(int fd, uint64_t& wakeCount)
{
    std::lock_guard<std::mutex> lock(g_eventSourceMutex);
    const auto iterator = g_dispatchersByFd.find(fd);
    if (iterator == g_dispatchersByFd.end() ||
        iterator->second->destroyed.load(std::memory_order_acquire))
    {
        return nullptr;
    }

    iterator->second->references.fetch_add(1, std::memory_order_relaxed);
    wakeCount = DrainCounterFd(fd);
    return iterator->second;
}

static JaliumTimer* AcquireTimerForFd(int fd, uint64_t& expirations)
{
    std::lock_guard<std::mutex> lock(g_eventSourceMutex);
    const auto iterator = g_timersByFd.find(fd);
    if (iterator == g_timersByFd.end() ||
        iterator->second->destroyed.load(std::memory_order_acquire))
    {
        return nullptr;
    }

    iterator->second->references.fetch_add(1, std::memory_order_relaxed);
    expirations = DrainCounterFd(fd);
    return iterator->second;
}

static bool RegisterTimerWithEpoll(JaliumTimer* timer)
{
    if (!timer || timer->destroyed.load(std::memory_order_acquire)) return false;

    const int fd = timer->timerFd.load(std::memory_order_acquire);
    std::lock_guard<std::mutex> lock(g_eventSourceMutex);
    if (fd < 0 || !AddEpollFd(fd)) return false;

    g_timersByFd[fd] = timer;
    timer->registeredWithEpoll.store(true, std::memory_order_release);
    return true;
}

static void UnregisterTimerFromEpoll(JaliumTimer* timer)
{
    if (!timer) return;

    const int fd = timer->timerFd.load(std::memory_order_acquire);
    std::lock_guard<std::mutex> lock(g_eventSourceMutex);
    const auto iterator = g_timersByFd.find(fd);
    if (iterator != g_timersByFd.end() && iterator->second == timer)
        g_timersByFd.erase(iterator);
    RemoveEpollFd(fd);
    timer->registeredWithEpoll.store(false, std::memory_order_release);
}

static int32_t ProcessReadyFd(int fd)
{
    if (fd == g_wakeEventFd)
    {
        (void)DrainCounterFd(fd);
        return 1;
    }

#ifdef JALIUM_HAS_WAYLAND
    if (fd == g_waylandRepeatFd)
        return ProcessWaylandRepeatReady();

    if (fd == g_waylandFd && g_waylandDisplay)
    {
        if (wl_display_dispatch(g_waylandDisplay) >= 0) return 1;
        g_quitRequested.store(true, std::memory_order_release);
        return 0;
    }
#endif

    uint64_t wakeCount = 0;
    if (JaliumDispatcher* dispatcher = AcquireDispatcherForFd(fd, wakeCount))
    {
        if (wakeCount != 0)
        {
            std::lock_guard<std::recursive_mutex> callbackLock(dispatcher->callbackMutex);
            if (!dispatcher->destroyed.load(std::memory_order_acquire))
            {
                const auto callback = dispatcher->callback.load(std::memory_order_acquire);
                if (callback)
                    callback(dispatcher->userData.load(std::memory_order_acquire));
            }
        }
        ReleaseDispatcher(dispatcher);
        return wakeCount != 0 ? 1 : 0;
    }

    uint64_t expirations = 0;
    if (JaliumTimer* timer = AcquireTimerForFd(fd, expirations))
    {
        {
            std::lock_guard<std::recursive_mutex> callbackLock(timer->callbackMutex);
            if (expirations != 0 && !timer->destroyed.load(std::memory_order_acquire))
            {
                const auto callback = timer->callback.load(std::memory_order_acquire);
                if (callback)
                    callback(timer->userData.load(std::memory_order_acquire));
            }
        }
        ReleaseTimer(timer);
        return expirations != 0 ? 1 : 0;
    }

    // The remaining registered descriptor is the X11 connection. XPending()
    // consumes that stream at the beginning of the next loop iteration.
    return 0;
}

static int32_t ProcessEpollEvents(int timeoutMilliseconds)
{
    if (g_epollFd < 0) return 0;

    struct epoll_event events[32];
    int ready;
    do
    {
        ready = epoll_wait(g_epollFd, events, 32, timeoutMilliseconds);
    }
    while (ready < 0 && errno == EINTR &&
           !g_quitRequested.load(std::memory_order_acquire));

    if (ready <= 0) return 0;

    int32_t processed = 0;
    for (int index = 0; index < ready; ++index)
        processed += ProcessReadyFd(events[index].data.fd);
    return processed;
}

// ============================================================================
// Key Mapping: X11 KeySym -> Jalium Virtual Key (Win32 VK compatible)
// ============================================================================

static int32_t KeySymToJaliumVK(KeySym sym)
{
    // Letters
    if (sym >= XK_a && sym <= XK_z) return 'A' + (sym - XK_a);
    if (sym >= XK_A && sym <= XK_Z) return 'A' + (sym - XK_A);

    // Numbers
    if (sym >= XK_0 && sym <= XK_9) return '0' + (sym - XK_0);

    // Function keys
    if (sym >= XK_F1 && sym <= XK_F24) return 0x70 + (sym - XK_F1); // VK_F1

    // Numpad
    if (sym >= XK_KP_0 && sym <= XK_KP_9) return 0x60 + (sym - XK_KP_0); // VK_NUMPAD0

    switch (sym)
    {
    case XK_BackSpace:    return 0x08; // VK_BACK
    case XK_Tab:          return 0x09; // VK_TAB
    case XK_Return:
    case XK_KP_Enter:     return 0x0D; // VK_RETURN
    case XK_Escape:       return 0x1B; // VK_ESCAPE
    case XK_space:        return 0x20; // VK_SPACE
    case XK_Delete:       return 0x2E; // VK_DELETE
    case XK_Insert:       return 0x2D; // VK_INSERT
    case XK_Home:         return 0x24; // VK_HOME
    case XK_End:          return 0x23; // VK_END
    case XK_Prior:        return 0x21; // VK_PRIOR (Page Up)
    case XK_Next:         return 0x22; // VK_NEXT (Page Down)
    case XK_Left:         return 0x25; // VK_LEFT
    case XK_Up:           return 0x26; // VK_UP
    case XK_Right:        return 0x27; // VK_RIGHT
    case XK_Down:         return 0x28; // VK_DOWN
    case XK_Shift_L:
    case XK_Shift_R:      return 0x10; // VK_SHIFT
    case XK_Control_L:
    case XK_Control_R:    return 0x11; // VK_CONTROL
    case XK_Alt_L:
    case XK_Alt_R:        return 0x12; // VK_MENU (Alt)
    case XK_Super_L:
    case XK_Super_R:      return 0x5B; // VK_LWIN
    case XK_Caps_Lock:    return 0x14; // VK_CAPITAL
    case XK_Num_Lock:     return 0x90; // VK_NUMLOCK
    case XK_Scroll_Lock:  return 0x91; // VK_SCROLL
    case XK_Print:        return 0x2C; // VK_SNAPSHOT
    case XK_Pause:        return 0x13; // VK_PAUSE
    case XK_Menu:         return 0x5D; // VK_APPS
    case XK_KP_Add:       return 0x6B; // VK_ADD
    case XK_KP_Subtract:  return 0x6D; // VK_SUBTRACT
    case XK_KP_Multiply:  return 0x6A; // VK_MULTIPLY
    case XK_KP_Divide:    return 0x6F; // VK_DIVIDE
    case XK_KP_Decimal:   return 0x6E; // VK_DECIMAL
    case XK_semicolon:    return 0xBA; // VK_OEM_1
    case XK_equal:        return 0xBB; // VK_OEM_PLUS
    case XK_comma:        return 0xBC; // VK_OEM_COMMA
    case XK_minus:        return 0xBD; // VK_OEM_MINUS
    case XK_period:       return 0xBE; // VK_OEM_PERIOD
    case XK_slash:        return 0xBF; // VK_OEM_2
    case XK_grave:        return 0xC0; // VK_OEM_3
    case XK_bracketleft:  return 0xDB; // VK_OEM_4
    case XK_backslash:    return 0xDC; // VK_OEM_5
    case XK_bracketright: return 0xDD; // VK_OEM_6
    case XK_apostrophe:   return 0xDE; // VK_OEM_7
    default:              return 0;
    }
}

static int32_t GetX11Modifiers(unsigned int state)
{
    int32_t mods = JALIUM_MOD_NONE;
    if (state & ShiftMask)   mods |= JALIUM_MOD_SHIFT;
    if (state & ControlMask) mods |= JALIUM_MOD_CTRL;
    if (state & Mod1Mask)    mods |= JALIUM_MOD_ALT;
    if (state & Mod4Mask)    mods |= JALIUM_MOD_META;
    if (state & LockMask)    mods |= JALIUM_MOD_CAPS;
    if (state & Mod2Mask)    mods |= JALIUM_MOD_NUM;
    return mods;
}

static int32_t X11ButtonToJalium(unsigned int button)
{
    switch (button)
    {
    case Button1: return JALIUM_MOUSE_BUTTON_LEFT;
    case Button2: return JALIUM_MOUSE_BUTTON_MIDDLE;
    case Button3: return JALIUM_MOUSE_BUTTON_RIGHT;
    case 8:       return JALIUM_MOUSE_BUTTON_X1;
    case 9:       return JALIUM_MOUSE_BUTTON_X2;
    default:      return JALIUM_MOUSE_BUTTON_LEFT;
    }
}

// ============================================================================
// DPI Detection
// ============================================================================

static float DetectDpiScale()
{
    // Try Xft.dpi resource
    if (g_display)
    {
        char* rms = XResourceManagerString(g_display);
        if (rms)
        {
            XrmDatabase db = XrmGetStringDatabase(rms);
            if (db)
            {
                XrmValue value;
                char* type = nullptr;
                if (XrmGetResource(db, "Xft.dpi", "Xft.Dpi", &type, &value))
                {
                    if (type && strcmp(type, "String") == 0 && value.addr)
                    {
                        float dpi = static_cast<float>(atof(value.addr));
                        XrmDestroyDatabase(db);
                        if (dpi > 0) return dpi / 96.0f;
                    }
                }
                XrmDestroyDatabase(db);
            }
        }

        // Fallback: compute from screen dimensions
        int widthPx = DisplayWidth(g_display, g_screen);
        int widthMm = DisplayWidthMM(g_display, g_screen);
        if (widthMm > 0)
        {
            float dpi = static_cast<float>(widthPx) * 25.4f / static_cast<float>(widthMm);
            return dpi / 96.0f;
        }
    }

    return 1.0f;
}

static std::u32string XimTextToUtf32(const XIMText* text)
{
    if (!text || text->length == 0) return {};
    if (text->encoding_is_wchar)
    {
        std::u32string result;
        result.reserve(text->length);
        for (unsigned short index = 0; index < text->length; ++index)
        {
            uint32_t codePoint = static_cast<uint32_t>(text->string.wide_char[index]);
            if (codePoint > 0x10FFFFu || (codePoint >= 0xD800u && codePoint <= 0xDFFFu))
                codePoint = 0xFFFDu;
            result.push_back(static_cast<char32_t>(codePoint));
        }
        return result;
    }

    return Utf8ToUtf32(text->string.multi_byte, text->length);
}

static int XimPreeditStartCallback(XIC, XPointer clientData, XPointer)
{
    auto* window = reinterpret_cast<JaliumPlatformWindow*>(clientData);
    if (!window) return -1;
    window->ximPreedit.clear();
    DispatchComposition(window, JALIUM_EVENT_COMPOSITION_START, {}, 0);
    return -1; // no client-side preedit length limit
}

static void XimPreeditDoneCallback(XIC, XPointer clientData, XPointer)
{
    auto* window = reinterpret_cast<JaliumPlatformWindow*>(clientData);
    if (!window) return;
    window->ximPreedit.clear();
    DispatchComposition(window, JALIUM_EVENT_COMPOSITION_END, {}, 0);
}

static void XimPreeditDrawCallback(XIC, XPointer clientData, XPointer callData)
{
    auto* window = reinterpret_cast<JaliumPlatformWindow*>(clientData);
    auto* draw = reinterpret_cast<XIMPreeditDrawCallbackStruct*>(callData);
    if (!window || !draw) return;

    const size_t first = std::min<size_t>(draw->chg_first, window->ximPreedit.size());
    const size_t remove = std::min<size_t>(draw->chg_length, window->ximPreedit.size() - first);
    const std::u32string replacement = XimTextToUtf32(draw->text);
    window->ximPreedit.replace(first, remove, replacement);
    const std::string utf8 = Utf32ToUtf8(window->ximPreedit);
    DispatchComposition(window, JALIUM_EVENT_COMPOSITION_UPDATE, utf8, draw->caret);
}

static void XimPreeditCaretCallback(XIC, XPointer clientData, XPointer callData)
{
    auto* window = reinterpret_cast<JaliumPlatformWindow*>(clientData);
    auto* caret = reinterpret_cast<XIMPreeditCaretCallbackStruct*>(callData);
    if (!window || !caret) return;
    const std::string utf8 = Utf32ToUtf8(window->ximPreedit);
    DispatchComposition(window, JALIUM_EVENT_COMPOSITION_UPDATE, utf8, caret->position);
}

// ============================================================================
// Platform Init / Shutdown
// ============================================================================

#ifdef JALIUM_HAS_WAYLAND
static JaliumPlatformWindow* WaylandWindowFromSurface(wl_surface* surface)
{
    return surface ? static_cast<JaliumPlatformWindow*>(wl_surface_get_user_data(surface)) : nullptr;
}

static void DispatchWaylandPaint(JaliumPlatformWindow* window)
{
    if (!window) return;

    // Coalesce invalidations until xdg_surface.configure has been acked. The
    // managed scheduler can invalidate immediately after Window.Show; calling
    // it synchronously before configure used to let the renderer commit the
    // first wl_shm/Vulkan buffer too early. The dispatch guard also prevents a
    // paint callback that invalidates again from recursing on the same stack.
    window->waylandPaintPending.store(true, std::memory_order_release);
    if (!window->waylandVisible.load(std::memory_order_acquire) ||
        !window->waylandConfigured.load(std::memory_order_acquire))
        return;

    bool expected = false;
    if (!window->waylandDispatchingPaint.compare_exchange_strong(
            expected, true, std::memory_order_acq_rel))
        return;

    window->waylandPaintPending.store(false, std::memory_order_release);
    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_PAINT;
    event.window = window;
    window->DispatchEvent(event);
    window->waylandDispatchingPaint.store(false, std::memory_order_release);

    // A re-entrant invalidation stays pending for the next event-loop turn.
    // Wake the loop without recursively dispatching another paint callback.
    if (window->waylandPaintPending.load(std::memory_order_acquire) &&
        g_wakeEventFd >= 0)
        (void)SignalEventFd(g_wakeEventFd);
}

static int32_t DispatchPendingWaylandPaints()
{
    std::vector<JaliumPlatformWindow*> pending;
    {
        std::lock_guard<std::mutex> lock(g_windowMapMutex);
        pending.reserve(g_waylandWindows.size());
        for (JaliumPlatformWindow* window : g_waylandWindows)
        {
            if (window->waylandPaintPending.load(std::memory_order_acquire) &&
                window->waylandVisible.load(std::memory_order_acquire) &&
                window->waylandConfigured.load(std::memory_order_acquire))
                pending.push_back(window);
        }
    }

    for (JaliumPlatformWindow* window : pending)
        DispatchWaylandPaint(window);
    return static_cast<int32_t>(pending.size());
}

static void HandleXdgSurfaceConfigure(void* data, xdg_surface* surface, uint32_t serial)
{
    auto* window = static_cast<JaliumPlatformWindow*>(data);
    xdg_surface_ack_configure(surface, serial);
    window->waylandConfigured.store(true, std::memory_order_release);
    DispatchWaylandPaint(window);
}

static const xdg_surface_listener g_xdgSurfaceListener = {
    HandleXdgSurfaceConfigure
};

static void HandleXdgToplevelConfigure(
    void* data, xdg_toplevel*, int32_t width, int32_t height, wl_array* states)
{
    auto* window = static_cast<JaliumPlatformWindow*>(data);
    bool maximized = false;
    bool fullscreen = false;
    bool activated = false;
    auto* state = static_cast<uint32_t*>(states->data);
    const size_t count = states->size / sizeof(uint32_t);
    for (size_t index = 0; index < count; ++index)
    {
        maximized |= state[index] == XDG_TOPLEVEL_STATE_MAXIMIZED;
        fullscreen |= state[index] == XDG_TOPLEVEL_STATE_FULLSCREEN;
        activated |= state[index] == XDG_TOPLEVEL_STATE_ACTIVATED;
    }

    JaliumWindowState newState = fullscreen ? JALIUM_WINDOW_STATE_FULLSCREEN :
        (maximized ? JALIUM_WINDOW_STATE_MAXIMIZED : JALIUM_WINDOW_STATE_NORMAL);
    if (newState != window->state)
    {
        window->state = newState;
        JaliumPlatformEvent event{};
        event.type = JALIUM_EVENT_STATE_CHANGED;
        event.window = window;
        event.stateChanged.newState = newState;
        window->DispatchEvent(event);
    }
    if (activated != window->waylandActivated)
    {
        window->waylandActivated = activated;
        JaliumPlatformEvent event{};
        event.type = activated ? JALIUM_EVENT_ACTIVATE : JALIUM_EVENT_DEACTIVATE;
        event.window = window;
        window->DispatchEvent(event);
    }
    if (width > 0 && height > 0 && (width != window->width || height != window->height))
    {
        window->width = width;
        window->height = height;
        JaliumPlatformEvent event{};
        event.type = JALIUM_EVENT_RESIZE;
        event.window = window;
        event.resize.width = width;
        event.resize.height = height;
        window->DispatchEvent(event);
    }
}

static void HandleXdgToplevelClose(void* data, xdg_toplevel*)
{
    auto* window = static_cast<JaliumPlatformWindow*>(data);
    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_CLOSE_REQUESTED;
    event.window = window;
    window->DispatchEvent(event);
}

static void HandleXdgToplevelConfigureBounds(void*, xdg_toplevel*, int32_t, int32_t) {}
static void HandleXdgToplevelWmCapabilities(void*, xdg_toplevel*, wl_array*) {}

static const xdg_toplevel_listener g_xdgToplevelListener = {
    HandleXdgToplevelConfigure,
    HandleXdgToplevelClose
#ifdef XDG_TOPLEVEL_CONFIGURE_BOUNDS_SINCE_VERSION
    , HandleXdgToplevelConfigureBounds
#endif
#ifdef XDG_TOPLEVEL_WM_CAPABILITIES_SINCE_VERSION
    , HandleXdgToplevelWmCapabilities
#endif
};

static bool CreateWaylandRole(JaliumPlatformWindow* window)
{
    if (!window || !window->waylandSurface || !g_xdgWmBase) return false;
    if (window->xdgToplevel) return true;

    window->waylandConfigured.store(false, std::memory_order_release);
    window->xdgSurface = xdg_wm_base_get_xdg_surface(g_xdgWmBase, window->waylandSurface);
    if (!window->xdgSurface) return false;
    xdg_surface_add_listener(window->xdgSurface, &g_xdgSurfaceListener, window);
    window->xdgToplevel = xdg_surface_get_toplevel(window->xdgSurface);
    if (!window->xdgToplevel)
    {
        xdg_surface_destroy(window->xdgSurface);
        window->xdgSurface = nullptr;
        return false;
    }
    xdg_toplevel_add_listener(window->xdgToplevel, &g_xdgToplevelListener, window);
    xdg_toplevel_set_title(window->xdgToplevel, window->waylandTitle.c_str());
    xdg_toplevel_set_app_id(window->xdgToplevel, "jalium.ui");
    if (!(window->style & JALIUM_WINDOW_STYLE_RESIZABLE))
    {
        xdg_toplevel_set_min_size(window->xdgToplevel, window->width, window->height);
        xdg_toplevel_set_max_size(window->xdgToplevel, window->width, window->height);
    }
    wl_surface_commit(window->waylandSurface);
    wl_display_flush(g_waylandDisplay);
    return true;
}

static void DestroyWaylandRole(JaliumPlatformWindow* window)
{
    if (!window) return;
    if (window->xdgToplevel) { xdg_toplevel_destroy(window->xdgToplevel); window->xdgToplevel = nullptr; }
    if (window->xdgSurface) { xdg_surface_destroy(window->xdgSurface); window->xdgSurface = nullptr; }
    window->waylandConfigured.store(false, std::memory_order_release);
    window->waylandPaintPending.store(false, std::memory_order_release);
}

static void HandleWmBasePing(void*, xdg_wm_base* wmBase, uint32_t serial)
{
    xdg_wm_base_pong(wmBase, serial);
}

static const xdg_wm_base_listener g_wmBaseListener = { HandleWmBasePing };

static void DestroyWaylandOffer(WaylandDataOfferState*& state)
{
    if (!state) return;
    g_waylandOffers.erase(state->offer);
    if (state->offer) wl_data_offer_destroy(state->offer);
    delete state;
    state = nullptr;
}

static void HandleDataOfferMime(void* data, wl_data_offer*, const char* mimeType)
{
    auto* state = static_cast<WaylandDataOfferState*>(data);
    if (state && mimeType) state->mimeTypes.emplace_back(mimeType);
}
static void HandleDataOfferSourceActions(void* data, wl_data_offer*, uint32_t actions)
{
    auto* state = static_cast<WaylandDataOfferState*>(data);
    if (state) state->sourceActions = actions;
}
static void HandleDataOfferAction(void* data, wl_data_offer*, uint32_t action)
{
    auto* state = static_cast<WaylandDataOfferState*>(data);
    if (state) state->selectedAction = action;
}
static const wl_data_offer_listener g_dataOfferListener = {
    HandleDataOfferMime, HandleDataOfferSourceActions, HandleDataOfferAction
};

static void HandleDataDeviceOffer(void*, wl_data_device*, wl_data_offer* offer)
{
    if (!offer) return;
    auto* state = new WaylandDataOfferState();
    state->offer = offer;
    g_waylandOffers[offer] = state;
    wl_data_offer_add_listener(offer, &g_dataOfferListener, state);
}

static uint32_t WaylandActionToEffect(uint32_t action)
{
    if (action == WL_DATA_DEVICE_MANAGER_DND_ACTION_COPY) return JALIUM_DRAG_EFFECT_COPY;
    if (action == WL_DATA_DEVICE_MANAGER_DND_ACTION_MOVE) return JALIUM_DRAG_EFFECT_MOVE;
    if (action == WL_DATA_DEVICE_MANAGER_DND_ACTION_ASK) return JALIUM_DRAG_EFFECT_LINK;
    return JALIUM_DRAG_EFFECT_NONE;
}

static uint32_t WaylandEffectsToActions(uint32_t effects)
{
    uint32_t actions = WL_DATA_DEVICE_MANAGER_DND_ACTION_NONE;
    if (effects & JALIUM_DRAG_EFFECT_COPY) actions |= WL_DATA_DEVICE_MANAGER_DND_ACTION_COPY;
    if (effects & JALIUM_DRAG_EFFECT_MOVE) actions |= WL_DATA_DEVICE_MANAGER_DND_ACTION_MOVE;
    if (effects & JALIUM_DRAG_EFFECT_LINK) actions |= WL_DATA_DEVICE_MANAGER_DND_ACTION_ASK;
    return actions;
}

static uint32_t WaylandDragKeyStates()
{
    uint32_t states = g_waylandPointerButtons;
    if (g_waylandModifiers & JALIUM_MOD_SHIFT) states |= 4u;
    if (g_waylandModifiers & JALIUM_MOD_CTRL) states |= 8u;
    if (g_waylandModifiers & JALIUM_MOD_ALT) states |= 32u;
    return states;
}

static const char* SelectWaylandDragMime(const WaylandDataOfferState* offer)
{
    if (!offer) return nullptr;
    constexpr const char* preferred[] = {
        "text/uri-list", "text/plain;charset=utf-8", "UTF8_STRING", "text/plain"
    };
    for (const char* candidate : preferred)
        if (std::find(offer->mimeTypes.begin(), offer->mimeTypes.end(), candidate) !=
            offer->mimeTypes.end()) return candidate;
    return offer->mimeTypes.empty() ? nullptr : offer->mimeTypes.front().c_str();
}

static std::string JoinWaylandMimeNames(const WaylandDataOfferState* offer)
{
    std::string result;
    if (!offer) return result;
    for (const std::string& mime : offer->mimeTypes)
    {
        if (!result.empty()) result.push_back('\n');
        result.append(mime);
    }
    return result;
}

static void DispatchWaylandDragEvent(JaliumEventType type,
                                     const char* dataMime = nullptr,
                                     const uint8_t* data = nullptr,
                                     uint32_t dataSize = 0)
{
    if (!g_waylandDragWindow) return;
    const std::string mimes = JoinWaylandMimeNames(g_waylandDragOffer);
    JaliumPlatformEvent event{};
    event.type = type;
    event.window = g_waylandDragWindow;
    event.drag.x = g_waylandDragWindow->xdndX;
    event.drag.y = g_waylandDragWindow->xdndY;
    event.drag.keyStates = WaylandDragKeyStates();
    event.drag.allowedEffects = g_waylandDragWindow->xdndAllowedEffects;
    event.drag.sessionId = g_waylandDragWindow->dragSessionId;
    event.drag.mimeTypes = mimes.c_str();
    event.drag.dataMimeType = dataMime;
    event.drag.data = data;
    event.drag.dataSize = dataSize;
    g_waylandDragWindow->DispatchEvent(event);
}

static void ApplyWaylandTargetEffect()
{
    if (!g_waylandDragOffer || !g_waylandDragOffer->offer || !g_waylandDragWindow)
        return;
    uint32_t effect = g_waylandDragWindow->dragSelectedEffect &
                      g_waylandDragWindow->xdndAllowedEffects;
    const uint32_t actions = WaylandEffectsToActions(effect);
    const uint32_t preferred = effect & JALIUM_DRAG_EFFECT_COPY
        ? WL_DATA_DEVICE_MANAGER_DND_ACTION_COPY
        : (effect & JALIUM_DRAG_EFFECT_MOVE
            ? WL_DATA_DEVICE_MANAGER_DND_ACTION_MOVE
            : (effect & JALIUM_DRAG_EFFECT_LINK
                ? WL_DATA_DEVICE_MANAGER_DND_ACTION_ASK
                : WL_DATA_DEVICE_MANAGER_DND_ACTION_NONE));
    wl_data_offer_accept(g_waylandDragOffer->offer, g_waylandDragSerial,
                         effect != JALIUM_DRAG_EFFECT_NONE
                             ? g_waylandDragMime.c_str() : nullptr);
    if (wl_proxy_get_version(
            reinterpret_cast<wl_proxy*>(g_waylandDragOffer->offer)) >= 3)
        wl_data_offer_set_actions(g_waylandDragOffer->offer, actions, preferred);
}

static void HandleDataDeviceEnter(void*, wl_data_device*, uint32_t serial,
                                  wl_surface* surface, wl_fixed_t x, wl_fixed_t y,
                                  wl_data_offer* offer)
{
    g_waylandInputSerial = serial;
    g_waylandPointerSerial = serial;
    g_waylandDragSerial = serial;
    g_waylandDropPending = false;
    g_waylandDragWindow = WaylandWindowFromSurface(surface);
    const auto iterator = g_waylandOffers.find(offer);
    g_waylandDragOffer = iterator == g_waylandOffers.end() ? nullptr : iterator->second;
    if (!g_waylandDragWindow || !g_waylandDragOffer) return;
    g_waylandDragWindow->xdndX = static_cast<float>(wl_fixed_to_double(x));
    g_waylandDragWindow->xdndY = static_cast<float>(wl_fixed_to_double(y));
    g_waylandDragWindow->dragSessionId =
        g_dragSessionCounter.fetch_add(1, std::memory_order_relaxed);
    g_waylandDragWindow->xdndAllowedEffects =
        WaylandActionToEffect(g_waylandDragOffer->sourceActions &
                             WL_DATA_DEVICE_MANAGER_DND_ACTION_COPY) |
        WaylandActionToEffect(g_waylandDragOffer->sourceActions &
                             WL_DATA_DEVICE_MANAGER_DND_ACTION_MOVE) |
        WaylandActionToEffect(g_waylandDragOffer->sourceActions &
                             WL_DATA_DEVICE_MANAGER_DND_ACTION_ASK);
    if (g_waylandDragWindow->xdndAllowedEffects == JALIUM_DRAG_EFFECT_NONE)
        g_waylandDragWindow->xdndAllowedEffects = JALIUM_DRAG_EFFECT_COPY |
                                                  JALIUM_DRAG_EFFECT_MOVE |
                                                  JALIUM_DRAG_EFFECT_LINK;
    const char* mime = SelectWaylandDragMime(g_waylandDragOffer);
    g_waylandDragMime = mime ? mime : "";
    g_waylandDragWindow->dragSelectedEffect = JALIUM_DRAG_EFFECT_NONE;
    DispatchWaylandDragEvent(JALIUM_EVENT_DRAG_ENTER);
    ApplyWaylandTargetEffect();
}
static void HandleDataDeviceLeave(void*, wl_data_device*)
{
    if (!g_waylandDragWindow || g_waylandDropPending) return;
    DispatchWaylandDragEvent(JALIUM_EVENT_DRAG_LEAVE);
    g_waylandDragWindow->dragSessionId = 0;
    g_waylandDragWindow->dragSelectedEffect = JALIUM_DRAG_EFFECT_NONE;
    if (g_waylandDragOffer && g_waylandDragOffer != g_waylandSelectionOffer)
        DestroyWaylandOffer(g_waylandDragOffer);
    g_waylandDragOffer = nullptr;
    g_waylandDragWindow = nullptr;
    g_waylandDragMime.clear();
}
static void HandleDataDeviceMotion(void*, wl_data_device*, uint32_t,
                                   wl_fixed_t x, wl_fixed_t y)
{
    if (!g_waylandDragWindow) return;
    g_waylandDragWindow->xdndX = static_cast<float>(wl_fixed_to_double(x));
    g_waylandDragWindow->xdndY = static_cast<float>(wl_fixed_to_double(y));
    g_waylandDragWindow->dragSelectedEffect = JALIUM_DRAG_EFFECT_NONE;
    DispatchWaylandDragEvent(JALIUM_EVENT_DRAG_OVER);
    ApplyWaylandTargetEffect();
}
static void HandleDataDeviceDrop(void*, wl_data_device*)
{
    if (!g_waylandDragWindow || !g_waylandDragOffer) return;
    g_waylandDropPending = true;
}

static void HandleDataDeviceSelection(void*, wl_data_device*, wl_data_offer* offer)
{
    WaylandDataOfferState* next = nullptr;
    if (offer)
    {
        const auto iterator = g_waylandOffers.find(offer);
        if (iterator != g_waylandOffers.end()) next = iterator->second;
    }
    if (g_waylandSelectionOffer && g_waylandSelectionOffer != next)
        DestroyWaylandOffer(g_waylandSelectionOffer);
    g_waylandSelectionOffer = next;
}

static const wl_data_device_listener g_dataDeviceListener = {
    HandleDataDeviceOffer, HandleDataDeviceEnter, HandleDataDeviceLeave,
    HandleDataDeviceMotion, HandleDataDeviceDrop, HandleDataDeviceSelection
};

static bool WriteAllAndClose(int fd, const std::string& text)
{
    bool success = true;
    size_t offset = 0;
    while (offset < text.size())
    {
        const ssize_t written = write(fd, text.data() + offset, text.size() - offset);
        if (written > 0) offset += static_cast<size_t>(written);
        else if (written < 0 && errno == EINTR) continue;
        else if (written < 0 && (errno == EAGAIN || errno == EWOULDBLOCK))
        {
            struct pollfd descriptor{fd, POLLOUT, 0};
            int result;
            do { result = poll(&descriptor, 1, 2000); }
            while (result < 0 && errno == EINTR);
            if (result > 0) continue;
            success = false;
            break;
        }
        else { success = false; break; }
    }
    close(fd);
    return success;
}

static void HandleDataSourceTarget(void*, wl_data_source*, const char*) {}
static void HandleDataSourceSend(void*, wl_data_source*, const char* mimeType, int32_t fd)
{
    if (fd < 0) return;
    const bool supported = mimeType &&
        (strcmp(mimeType, "text/plain;charset=utf-8") == 0 ||
         strcmp(mimeType, "text/plain") == 0 ||
         strcmp(mimeType, "UTF8_STRING") == 0);
    std::string snapshot;
    if (supported)
    {
        std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
        snapshot = g_clipboardUtf8;
    }
    (void)WriteAllAndClose(fd, snapshot);
}
static void HandleDataSourceCancelled(void*, wl_data_source* source)
{
    if (source == g_waylandClipboardSource) g_waylandClipboardSource = nullptr;
    wl_data_source_destroy(source);
}
static void HandleDataSourceDropPerformed(void*, wl_data_source*) {}
static void HandleDataSourceFinished(void*, wl_data_source*) {}
static void HandleDataSourceAction(void*, wl_data_source*, uint32_t) {}
static const wl_data_source_listener g_dataSourceListener = {
    HandleDataSourceTarget, HandleDataSourceSend, HandleDataSourceCancelled,
    HandleDataSourceDropPerformed, HandleDataSourceFinished, HandleDataSourceAction
};

static void HandleDragSourceTarget(void*, wl_data_source*, const char*) {}
static void HandleDragSourceSend(void* data, wl_data_source*,
                                 const char* mimeType, int32_t fd)
{
    auto* state = static_cast<WaylandDragSourceState*>(data);
    if (!state || !mimeType || fd < 0) { if (fd >= 0) close(fd); return; }
    const auto iterator = std::find_if(
        state->items.begin(), state->items.end(),
        [mimeType](const OwnedDragItem& item) { return item.mimeType == mimeType; });
    if (iterator == state->items.end()) { close(fd); return; }
    std::vector<uint8_t> bytes = iterator->bytes;
    std::thread([fd, bytes = std::move(bytes)]() mutable
    {
        size_t offset = 0;
        while (offset < bytes.size())
        {
            const ssize_t written = write(fd, bytes.data() + offset, bytes.size() - offset);
            if (written > 0) offset += static_cast<size_t>(written);
            else if (written < 0 && errno == EINTR) continue;
            else break;
        }
        close(fd);
    }).detach();
}
static void HandleDragSourceCancelled(void* data, wl_data_source*)
{
    auto* state = static_cast<WaylandDragSourceState*>(data);
    if (!state) return;
    state->cancelled = true;
    state->finished = true;
    state->performedEffect = JALIUM_DRAG_EFFECT_NONE;
}
static void HandleDragSourceDropPerformed(void* data, wl_data_source*)
{
    auto* state = static_cast<WaylandDragSourceState*>(data);
    if (state) state->dropPerformed = true;
}
static void HandleDragSourceFinished(void* data, wl_data_source*)
{
    auto* state = static_cast<WaylandDragSourceState*>(data);
    if (!state) return;
    state->performedEffect = WaylandActionToEffect(state->selectedAction);
    state->finished = true;
}
static void HandleDragSourceAction(void* data, wl_data_source*, uint32_t action)
{
    auto* state = static_cast<WaylandDragSourceState*>(data);
    if (state) state->selectedAction = action;
}
static const wl_data_source_listener g_dragDataSourceListener = {
    HandleDragSourceTarget, HandleDragSourceSend, HandleDragSourceCancelled,
    HandleDragSourceDropPerformed, HandleDragSourceFinished, HandleDragSourceAction
};

static bool ReceiveWaylandDragData(std::vector<uint8_t>& bytes)
{
    if (!g_waylandDragOffer || !g_waylandDragOffer->offer ||
        g_waylandDragMime.empty()) return false;
    int descriptors[2] = {-1, -1};
    if (pipe2(descriptors, O_CLOEXEC) != 0) return false;
    const int readFlags = fcntl(descriptors[0], F_GETFL, 0);
    if (readFlags < 0 || fcntl(descriptors[0], F_SETFL, readFlags | O_NONBLOCK) != 0)
    {
        close(descriptors[0]);
        close(descriptors[1]);
        return false;
    }
    wl_data_offer_receive(g_waylandDragOffer->offer,
                          g_waylandDragMime.c_str(), descriptors[1]);
    close(descriptors[1]);
    descriptors[1] = -1;
    if (wl_display_flush(g_waylandDisplay) < 0)
    {
        close(descriptors[0]);
        return false;
    }

    bytes.clear();
    bool complete = false;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    while (!complete && std::chrono::steady_clock::now() < deadline)
    {
        for (;;)
        {
            uint8_t buffer[8192];
            const ssize_t count = read(descriptors[0], buffer, sizeof(buffer));
            if (count > 0) bytes.insert(bytes.end(), buffer, buffer + count);
            else if (count == 0) { complete = true; break; }
            else if (errno == EINTR) continue;
            else if (errno == EAGAIN || errno == EWOULDBLOCK) break;
            else { close(descriptors[0]); return false; }
        }
        if (complete) break;

        if (wl_display_dispatch_pending(g_waylandDisplay) < 0)
        {
            close(descriptors[0]);
            return false;
        }
        struct pollfd pollDescriptors[2]{};
        pollDescriptors[0].fd = descriptors[0];
        pollDescriptors[0].events = POLLIN | POLLHUP;
        pollDescriptors[1].fd = g_waylandFd;
        pollDescriptors[1].events = POLLIN;
        int result;
        do { result = poll(pollDescriptors, 2, 20); }
        while (result < 0 && errno == EINTR);
        if (result < 0) { close(descriptors[0]); return false; }
        if (pollDescriptors[1].revents & POLLIN)
        {
            if (wl_display_dispatch(g_waylandDisplay) < 0)
            {
                close(descriptors[0]);
                return false;
            }
        }
        if (pollDescriptors[0].revents & POLLHUP)
        {
            // Drain the final bytes once more before treating HUP as EOF.
            continue;
        }
    }
    close(descriptors[0]);
    return complete;
}

static bool ProcessPendingWaylandDrop()
{
    if (!g_waylandDropPending) return false;
    g_waylandDropPending = false;
    if (!g_waylandDragWindow || !g_waylandDragOffer) return false;

    std::vector<uint8_t> bytes;
    const bool received = g_waylandDragWindow->dragSelectedEffect !=
                              JALIUM_DRAG_EFFECT_NONE &&
                          ReceiveWaylandDragData(bytes);
    if (received)
    {
        g_waylandDragWindow->dragSelectedEffect = JALIUM_DRAG_EFFECT_NONE;
        DispatchWaylandDragEvent(
            JALIUM_EVENT_DROP, g_waylandDragMime.c_str(),
            bytes.empty() ? nullptr : bytes.data(),
            static_cast<uint32_t>(bytes.size()));
    }
    const bool accepted = received &&
        g_waylandDragWindow->dragSelectedEffect != JALIUM_DRAG_EFFECT_NONE;
    if (accepted && wl_proxy_get_version(
            reinterpret_cast<wl_proxy*>(g_waylandDragOffer->offer)) >= 3)
        wl_data_offer_finish(g_waylandDragOffer->offer);

    g_waylandDragWindow->dragSessionId = 0;
    g_waylandDragWindow->dragSelectedEffect = JALIUM_DRAG_EFFECT_NONE;
    if (g_waylandDragOffer != g_waylandSelectionOffer)
        DestroyWaylandOffer(g_waylandDragOffer);
    g_waylandDragOffer = nullptr;
    g_waylandDragWindow = nullptr;
    g_waylandDragMime.clear();
    return true;
}

static void EnsureWaylandDataDevice()
{
    if (g_waylandDataDevice || !g_waylandDataDeviceManager || !g_waylandSeat) return;
    g_waylandDataDevice = wl_data_device_manager_get_data_device(
        g_waylandDataDeviceManager, g_waylandSeat);
    if (g_waylandDataDevice)
        wl_data_device_add_listener(g_waylandDataDevice, &g_dataDeviceListener, nullptr);
}

#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
static void HandleTextInputEnter(void*, zwp_text_input_v3* textInput, wl_surface* surface)
{
    JaliumPlatformWindow* window = WaylandWindowFromSurface(surface);
    if (window) g_keyboardFocus = window;
    zwp_text_input_v3_enable(textInput);
    zwp_text_input_v3_set_content_type(
        textInput, ZWP_TEXT_INPUT_V3_CONTENT_HINT_NONE,
        ZWP_TEXT_INPUT_V3_CONTENT_PURPOSE_NORMAL);
    zwp_text_input_v3_commit(textInput);
}

static void HandleTextInputLeave(void*, zwp_text_input_v3* textInput, wl_surface*)
{
    if (g_waylandCompositionActive && g_keyboardFocus)
        DispatchComposition(g_keyboardFocus, JALIUM_EVENT_COMPOSITION_END, {}, 0);
    g_waylandCompositionActive = false;
    g_pendingWaylandPreedit.clear();
    g_pendingWaylandCommit.clear();
    g_pendingWaylandPreeditSet = false;
    g_pendingWaylandCommitSet = false;
    zwp_text_input_v3_disable(textInput);
    zwp_text_input_v3_commit(textInput);
}

static void HandleTextInputPreedit(void*, zwp_text_input_v3*, const char* text,
                                   int32_t cursorBegin, int32_t)
{
    g_pendingWaylandPreedit = text ? text : "";
    g_pendingWaylandPreeditCursor = text
        ? Utf8ByteOffsetToUtf16(g_pendingWaylandPreedit, cursorBegin) : 0;
    g_pendingWaylandPreeditSet = true;
}

static void HandleTextInputCommit(void*, zwp_text_input_v3*, const char* text)
{
    g_pendingWaylandCommit = text ? text : "";
    g_pendingWaylandCommitSet = true;
}

static void HandleTextInputDeleteSurrounding(void*, zwp_text_input_v3*, uint32_t, uint32_t)
{
    // The current public platform ABI has no surrounding-text mutation event.
    // Committed text still works; delete-surrounding is ignored until that API
    // can carry UTF-8 byte ranges without emulating key presses.
}

static void HandleTextInputDone(void*, zwp_text_input_v3*, uint32_t)
{
    JaliumPlatformWindow* window = g_keyboardFocus;
    if (!window)
    {
        g_pendingWaylandPreeditSet = false;
        g_pendingWaylandCommitSet = false;
        return;
    }

    if (g_pendingWaylandCommitSet && !g_pendingWaylandCommit.empty())
    {
        DispatchComposition(window, JALIUM_EVENT_COMPOSITION_END,
                            g_pendingWaylandCommit, 0);
        g_waylandCompositionActive = false;
    }
    if (g_pendingWaylandPreeditSet)
    {
        if (!g_pendingWaylandPreedit.empty())
        {
            if (!g_waylandCompositionActive)
                DispatchComposition(window, JALIUM_EVENT_COMPOSITION_START, {}, 0);
            g_waylandCompositionActive = true;
            DispatchComposition(window, JALIUM_EVENT_COMPOSITION_UPDATE,
                                g_pendingWaylandPreedit,
                                g_pendingWaylandPreeditCursor);
        }
        else if (g_waylandCompositionActive)
        {
            DispatchComposition(window, JALIUM_EVENT_COMPOSITION_END, {}, 0);
            g_waylandCompositionActive = false;
        }
    }

    g_pendingWaylandPreedit.clear();
    g_pendingWaylandCommit.clear();
    g_pendingWaylandPreeditSet = false;
    g_pendingWaylandCommitSet = false;
}

static const zwp_text_input_v3_listener g_textInputListener = {
    HandleTextInputEnter, HandleTextInputLeave, HandleTextInputPreedit,
    HandleTextInputCommit, HandleTextInputDeleteSurrounding, HandleTextInputDone
};

static void EnsureWaylandTextInput()
{
    if (g_waylandTextInput || !g_waylandTextInputManager || !g_waylandSeat) return;
    g_waylandTextInput = zwp_text_input_manager_v3_get_text_input(
        g_waylandTextInputManager, g_waylandSeat);
    if (g_waylandTextInput)
        zwp_text_input_v3_add_listener(g_waylandTextInput, &g_textInputListener, nullptr);
}
#endif

#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
static void HandleTextInputV1Enter(void*, zwp_text_input_v1*, wl_surface*)
{
    g_waylandTextInputV1Active = true;
}

static void HandleTextInputV1Leave(void*, zwp_text_input_v1*)
{
    if (g_waylandCompositionActive && g_keyboardFocus)
        DispatchComposition(g_keyboardFocus, JALIUM_EVENT_COMPOSITION_END, {}, 0);
    g_waylandCompositionActive = false;
    g_waylandTextInputV1Active = false;
}

static void HandleTextInputV1ModifiersMap(void*, zwp_text_input_v1*, wl_array*) {}
static void HandleTextInputV1PanelState(void*, zwp_text_input_v1*, uint32_t) {}

static void HandleTextInputV1PreeditString(
    void*, zwp_text_input_v1*, uint32_t, const char* text, const char*)
{
    if (!g_keyboardFocus) return;
    const std::string preedit = text ? text : "";
    if (!preedit.empty())
    {
        if (!g_waylandCompositionActive)
            DispatchComposition(g_keyboardFocus, JALIUM_EVENT_COMPOSITION_START, {}, 0);
        g_waylandCompositionActive = true;
        DispatchComposition(
            g_keyboardFocus, JALIUM_EVENT_COMPOSITION_UPDATE, preedit,
            Utf8ByteOffsetToUtf16(preedit, g_waylandTextInputCursorV1));
    }
    else if (g_waylandCompositionActive)
    {
        DispatchComposition(g_keyboardFocus, JALIUM_EVENT_COMPOSITION_END, {}, 0);
        g_waylandCompositionActive = false;
    }
}

static void HandleTextInputV1PreeditStyling(
    void*, zwp_text_input_v1*, uint32_t, uint32_t, uint32_t) {}
static void HandleTextInputV1PreeditCursor(void*, zwp_text_input_v1*, int32_t index)
{
    g_waylandTextInputCursorV1 = std::max(index, 0);
}

static void HandleTextInputV1CommitString(
    void*, zwp_text_input_v1*, uint32_t, const char* text)
{
    if (!g_keyboardFocus) return;
    const std::string commit = text ? text : "";
    if (!commit.empty() || g_waylandCompositionActive)
        DispatchComposition(g_keyboardFocus, JALIUM_EVENT_COMPOSITION_END, commit, 0);
    g_waylandCompositionActive = false;
}

static void HandleTextInputV1CursorPosition(void*, zwp_text_input_v1*, int32_t, int32_t) {}
static void HandleTextInputV1DeleteSurrounding(void*, zwp_text_input_v1*, int32_t, uint32_t) {}
static void HandleTextInputV1Keysym(
    void*, zwp_text_input_v1*, uint32_t, uint32_t, uint32_t, uint32_t, uint32_t) {}
static void HandleTextInputV1Language(void*, zwp_text_input_v1*, uint32_t, const char*) {}
static void HandleTextInputV1Direction(void*, zwp_text_input_v1*, uint32_t, uint32_t) {}

static const zwp_text_input_v1_listener g_textInputV1Listener = {
    HandleTextInputV1Enter,
    HandleTextInputV1Leave,
    HandleTextInputV1ModifiersMap,
    HandleTextInputV1PanelState,
    HandleTextInputV1PreeditString,
    HandleTextInputV1PreeditStyling,
    HandleTextInputV1PreeditCursor,
    HandleTextInputV1CommitString,
    HandleTextInputV1CursorPosition,
    HandleTextInputV1DeleteSurrounding,
    HandleTextInputV1Keysym,
    HandleTextInputV1Language,
    HandleTextInputV1Direction
};

static void EnsureWaylandTextInputV1()
{
    if (g_waylandTextInputV1 || !g_waylandTextInputManagerV1) return;
    g_waylandTextInputV1 = zwp_text_input_manager_v1_create_text_input(
        g_waylandTextInputManagerV1);
    if (g_waylandTextInputV1)
        zwp_text_input_v1_add_listener(
            g_waylandTextInputV1, &g_textInputV1Listener, nullptr);
}
#endif

static void HandlePointerEnter(void*, wl_pointer*, uint32_t serial, wl_surface* surface,
                               wl_fixed_t x, wl_fixed_t y)
{
    g_waylandInputSerial = serial;
    g_waylandPointerSerial = serial;
    g_pointerFocus = WaylandWindowFromSurface(surface);
    g_pointerX = static_cast<float>(wl_fixed_to_double(x));
    g_pointerY = static_cast<float>(wl_fixed_to_double(y));
    if (!g_pointerFocus) return;
    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_MOUSE_ENTER;
    event.window = g_pointerFocus;
    g_pointerFocus->DispatchEvent(event);
}

static void HandlePointerLeave(void*, wl_pointer*, uint32_t, wl_surface*)
{
    if (g_pointerFocus)
    {
        JaliumPlatformEvent event{};
        event.type = JALIUM_EVENT_MOUSE_LEAVE;
        event.window = g_pointerFocus;
        g_pointerFocus->DispatchEvent(event);
    }
    g_pointerFocus = nullptr;
}

static void HandlePointerMotion(void*, wl_pointer*, uint32_t, wl_fixed_t x, wl_fixed_t y)
{
    g_pointerX = static_cast<float>(wl_fixed_to_double(x));
    g_pointerY = static_cast<float>(wl_fixed_to_double(y));
    if (!g_pointerFocus) return;
    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_MOUSE_MOVE;
    event.window = g_pointerFocus;
    event.mouse.x = g_pointerX;
    event.mouse.y = g_pointerY;
    event.mouse.modifiers = g_waylandModifiers;
    g_pointerFocus->DispatchEvent(event);
}

static int32_t WaylandButton(uint32_t button)
{
    switch (button)
    {
    case 0x110: return JALIUM_MOUSE_BUTTON_LEFT;
    case 0x111: return JALIUM_MOUSE_BUTTON_RIGHT;
    case 0x112: return JALIUM_MOUSE_BUTTON_MIDDLE;
    case 0x113: return JALIUM_MOUSE_BUTTON_X1;
    case 0x114: return JALIUM_MOUSE_BUTTON_X2;
    default: return JALIUM_MOUSE_BUTTON_LEFT;
    }
}

static void HandlePointerButton(void*, wl_pointer*, uint32_t serial, uint32_t time,
                                uint32_t button, uint32_t state)
{
    g_waylandInputSerial = serial;
    g_waylandPointerSerial = serial;
    const uint32_t dragButton = button == 0x110 ? 1u :
                                (button == 0x111 ? 2u :
                                 (button == 0x112 ? 16u : 0u));
    if (state == WL_POINTER_BUTTON_STATE_PRESSED)
        g_waylandPointerButtons |= dragButton;
    else
        g_waylandPointerButtons &= ~dragButton;
    if (!g_pointerFocus) return;
    JaliumPlatformEvent event{};
    event.type = state == WL_POINTER_BUTTON_STATE_PRESSED ? JALIUM_EVENT_MOUSE_DOWN : JALIUM_EVENT_MOUSE_UP;
    event.window = g_pointerFocus;
    event.mouse.x = g_pointerX;
    event.mouse.y = g_pointerY;
    event.mouse.button = WaylandButton(button);
    event.mouse.modifiers = g_waylandModifiers;
    event.mouse.clickCount = state == WL_POINTER_BUTTON_STATE_PRESSED
        ? RegisterClick(g_waylandClickTracker, g_pointerFocus, event.mouse.button,
                        time, g_pointerX, g_pointerY)
        : g_waylandClickTracker.count;
    g_pointerFocus->DispatchEvent(event);
}

static void HandlePointerAxis(void*, wl_pointer*, uint32_t, uint32_t axis, wl_fixed_t value)
{
    if (!g_pointerFocus) return;
    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_MOUSE_WHEEL;
    event.window = g_pointerFocus;
    event.wheel.x = g_pointerX;
    event.wheel.y = g_pointerY;
    const float delta = static_cast<float>(-wl_fixed_to_double(value) * 12.0);
    if (axis == WL_POINTER_AXIS_HORIZONTAL_SCROLL) event.wheel.deltaX = delta;
    else event.wheel.deltaY = delta;
    event.wheel.modifiers = g_waylandModifiers;
    g_pointerFocus->DispatchEvent(event);
}

static void HandlePointerFrame(void*, wl_pointer*) {}
static void HandlePointerAxisSource(void*, wl_pointer*, uint32_t) {}
static void HandlePointerAxisStop(void*, wl_pointer*, uint32_t, uint32_t) {}
static void HandlePointerAxisDiscrete(void*, wl_pointer*, uint32_t, int32_t) {}
static void HandlePointerAxisValue120(void*, wl_pointer*, uint32_t, int32_t) {}
static void HandlePointerAxisRelativeDirection(void*, wl_pointer*, uint32_t, uint32_t) {}

static const wl_pointer_listener g_pointerListener = {
    HandlePointerEnter, HandlePointerLeave, HandlePointerMotion, HandlePointerButton,
    HandlePointerAxis
#ifdef WL_POINTER_FRAME_SINCE_VERSION
    , HandlePointerFrame
#endif
#ifdef WL_POINTER_AXIS_SOURCE_SINCE_VERSION
    , HandlePointerAxisSource
#endif
#ifdef WL_POINTER_AXIS_STOP_SINCE_VERSION
    , HandlePointerAxisStop
#endif
#ifdef WL_POINTER_AXIS_DISCRETE_SINCE_VERSION
    , HandlePointerAxisDiscrete
#endif
#ifdef WL_POINTER_AXIS_VALUE120_SINCE_VERSION
    , HandlePointerAxisValue120
#endif
#ifdef WL_POINTER_AXIS_RELATIVE_DIRECTION_SINCE_VERSION
    , HandlePointerAxisRelativeDirection
#endif
};

static void HandleKeyboardKeymap(void*, wl_keyboard*, uint32_t format, int fd, uint32_t size)
{
    if (format != WL_KEYBOARD_KEYMAP_FORMAT_XKB_V1 || fd < 0) { if (fd >= 0) close(fd); return; }
    void* mapping = mmap(nullptr, size, PROT_READ, MAP_PRIVATE, fd, 0);
    close(fd);
    if (mapping == MAP_FAILED) return;
    xkb_keymap* keymap = xkb_keymap_new_from_string(
        g_xkbContext, static_cast<const char*>(mapping), XKB_KEYMAP_FORMAT_TEXT_V1,
        XKB_KEYMAP_COMPILE_NO_FLAGS);
    munmap(mapping, size);
    if (!keymap) return;
    xkb_state* state = xkb_state_new(keymap);
    if (!state) { xkb_keymap_unref(keymap); return; }
    if (g_xkbState) xkb_state_unref(g_xkbState);
    if (g_xkbKeymap) xkb_keymap_unref(g_xkbKeymap);
    g_xkbKeymap = keymap;
    g_xkbState = state;
}

static void StopWaylandRepeat()
{
    if (g_waylandRepeatFd >= 0)
    {
        struct itimerspec timer{};
        (void)timerfd_settime(g_waylandRepeatFd, 0, &timer, nullptr);
    }
    g_waylandRepeatWindow = nullptr;
    g_waylandRepeatKey = 0;
    g_waylandRepeatKeycode = 0;
    g_waylandRepeatSymbol = XKB_KEY_NoSymbol;
}

static void StartWaylandRepeat(JaliumPlatformWindow* window, uint32_t key,
                               xkb_keycode_t keycode, xkb_keysym_t symbol)
{
    StopWaylandRepeat();
    if (!window || g_waylandRepeatFd < 0 || g_waylandRepeatRate <= 0 ||
        g_waylandRepeatDelay < 0 || !g_xkbKeymap ||
        !xkb_keymap_key_repeats(g_xkbKeymap, keycode)) return;

    g_waylandRepeatWindow = window;
    g_waylandRepeatKey = key;
    g_waylandRepeatKeycode = keycode;
    g_waylandRepeatSymbol = symbol;
    struct itimerspec timer{};
    timer.it_value.tv_sec = g_waylandRepeatDelay / 1000;
    timer.it_value.tv_nsec = (g_waylandRepeatDelay % 1000) * 1'000'000;
    const int64_t intervalNanoseconds = 1'000'000'000LL / g_waylandRepeatRate;
    timer.it_interval.tv_sec = intervalNanoseconds / 1'000'000'000LL;
    timer.it_interval.tv_nsec = intervalNanoseconds % 1'000'000'000LL;
    (void)timerfd_settime(g_waylandRepeatFd, 0, &timer, nullptr);
}

static int32_t ProcessWaylandRepeatReady()
{
    uint64_t expirations = 0;
    const ssize_t bytes = read(g_waylandRepeatFd, &expirations, sizeof(expirations));
    if (bytes != static_cast<ssize_t>(sizeof(expirations)) ||
        !g_waylandRepeatWindow || g_waylandRepeatWindow->destroyed) return 0;
    // Coalesce a stalled client to a bounded number of callbacks while still
    // preserving normal repeat cadence.
    expirations = std::min<uint64_t>(expirations, 8);
    for (uint64_t index = 0; index < expirations; ++index)
    {
        JaliumPlatformEvent event{};
        event.type = JALIUM_EVENT_KEY_DOWN;
        event.window = g_waylandRepeatWindow;
        event.key.keyCode = KeySymToJaliumVK(static_cast<KeySym>(g_waylandRepeatSymbol));
        event.key.scanCode = static_cast<int32_t>(g_waylandRepeatKey);
        event.key.modifiers = g_waylandModifiers;
        event.key.isRepeat = 1;
        g_waylandRepeatWindow->DispatchEvent(event);

        const uint32_t codePoint = g_xkbState
            ? xkb_state_key_get_utf32(g_xkbState, g_waylandRepeatKeycode) : 0;
        if (codePoint && !g_waylandCompositionActive
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
            && !g_waylandTextInputV1Active
#endif
           )
        {
            JaliumPlatformEvent character{};
            character.type = JALIUM_EVENT_CHAR_INPUT;
            character.window = g_waylandRepeatWindow;
            character.character.codepoint = codePoint;
            g_waylandRepeatWindow->DispatchEvent(character);
        }
    }
    return 1;
}

static void HandleKeyboardEnter(void*, wl_keyboard*, uint32_t serial,
                                wl_surface* surface, wl_array* keys)
{
    g_waylandInputSerial = serial;
    g_keyboardFocus = WaylandWindowFromSurface(surface);
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
    if (g_waylandTextInputV1 && g_waylandSeat && surface
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
        && !g_waylandTextInputManager
#endif
       )
    {
        zwp_text_input_v1_activate(g_waylandTextInputV1, g_waylandSeat, surface);
        zwp_text_input_v1_set_content_type(
            g_waylandTextInputV1, ZWP_TEXT_INPUT_V1_CONTENT_HINT_NONE,
            ZWP_TEXT_INPUT_V1_CONTENT_PURPOSE_NORMAL);
        zwp_text_input_v1_commit_state(
            g_waylandTextInputV1, ++g_waylandTextInputSerialV1);
        wl_display_flush(g_waylandDisplay);
    }
#endif
    ClearPressedKeyStates();
    if (keys && g_xkbState)
    {
        auto* key = static_cast<uint32_t*>(keys->data);
        const size_t count = keys->size / sizeof(uint32_t);
        for (size_t index = 0; index < count; ++index)
        {
            const xkb_keysym_t symbol = xkb_state_key_get_one_sym(g_xkbState, key[index] + 8);
            SetKeyState(KeySymToJaliumVK(static_cast<KeySym>(symbol)), true, false);
        }
    }
    if (!g_keyboardFocus) return;
    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_FOCUS_GAINED;
    event.window = g_keyboardFocus;
    g_keyboardFocus->DispatchEvent(event);
}

static void HandleKeyboardLeave(void*, wl_keyboard*, uint32_t, wl_surface*)
{
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
    if (g_waylandTextInputV1 && g_waylandSeat && g_waylandTextInputV1Active)
    {
        zwp_text_input_v1_deactivate(g_waylandTextInputV1, g_waylandSeat);
        g_waylandTextInputV1Active = false;
        wl_display_flush(g_waylandDisplay);
    }
#endif
    StopWaylandRepeat();
    ClearPressedKeyStates();
    if (g_keyboardFocus)
    {
        JaliumPlatformEvent event{};
        event.type = JALIUM_EVENT_FOCUS_LOST;
        event.window = g_keyboardFocus;
        g_keyboardFocus->DispatchEvent(event);
    }
    g_keyboardFocus = nullptr;
}

static void HandleKeyboardKey(void*, wl_keyboard*, uint32_t serial, uint32_t,
                              uint32_t key, uint32_t state)
{
    g_waylandInputSerial = serial;
    if (!g_keyboardFocus) return;
    const xkb_keycode_t keycode = key + 8;
    const xkb_keysym_t symbol = g_xkbState ? xkb_state_key_get_one_sym(g_xkbState, keycode) : XKB_KEY_NoSymbol;
    JaliumPlatformEvent event{};
    event.type = state == WL_KEYBOARD_KEY_STATE_PRESSED ? JALIUM_EVENT_KEY_DOWN : JALIUM_EVENT_KEY_UP;
    event.window = g_keyboardFocus;
    event.key.keyCode = KeySymToJaliumVK(static_cast<KeySym>(symbol));
    event.key.scanCode = static_cast<int32_t>(key);
    event.key.modifiers = g_waylandModifiers;
    const bool pressed = state == WL_KEYBOARD_KEY_STATE_PRESSED;
    const bool toggled = event.key.keyCode == 0x14 || event.key.keyCode == 0x90 ||
                         event.key.keyCode == 0x91;
    SetKeyState(event.key.keyCode, pressed, toggled);
    g_keyboardFocus->DispatchEvent(event);
    if (state == WL_KEYBOARD_KEY_STATE_PRESSED && g_xkbState)
    {
        const uint32_t codepoint = xkb_state_key_get_utf32(g_xkbState, keycode);
        if (codepoint && !g_waylandCompositionActive
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
            && !g_waylandTextInputV1Active
#endif
           )
        {
            JaliumPlatformEvent character{};
            character.type = JALIUM_EVENT_CHAR_INPUT;
            character.window = g_keyboardFocus;
            character.character.codepoint = codepoint;
            g_keyboardFocus->DispatchEvent(character);
        }
        StartWaylandRepeat(g_keyboardFocus, key, keycode, symbol);
    }
    else if (key == g_waylandRepeatKey)
        StopWaylandRepeat();
}

static void HandleKeyboardModifiers(void*, wl_keyboard*, uint32_t,
                                    uint32_t depressed, uint32_t latched,
                                    uint32_t locked, uint32_t group)
{
    if (!g_xkbState) return;
    xkb_state_update_mask(g_xkbState, depressed, latched, locked, 0, 0, group);
    g_waylandModifiers = 0;
    if (xkb_state_mod_name_is_active(g_xkbState, XKB_MOD_NAME_SHIFT, XKB_STATE_MODS_EFFECTIVE) > 0) g_waylandModifiers |= 1;
    if (xkb_state_mod_name_is_active(g_xkbState, XKB_MOD_NAME_CTRL, XKB_STATE_MODS_EFFECTIVE) > 0) g_waylandModifiers |= 2;
    if (xkb_state_mod_name_is_active(g_xkbState, XKB_MOD_NAME_ALT, XKB_STATE_MODS_EFFECTIVE) > 0) g_waylandModifiers |= 4;
    if (xkb_state_mod_name_is_active(g_xkbState, XKB_MOD_NAME_LOGO, XKB_STATE_MODS_EFFECTIVE) > 0) g_waylandModifiers |= 8;
    SetToggleState(0x14,
        xkb_state_mod_name_is_active(g_xkbState, XKB_MOD_NAME_CAPS,
                                     XKB_STATE_MODS_LOCKED) > 0);
    SetToggleState(0x90,
        xkb_state_mod_name_is_active(g_xkbState, "NumLock",
                                     XKB_STATE_MODS_LOCKED) > 0);
}

static void HandleKeyboardRepeatInfo(void*, wl_keyboard*, int32_t rate, int32_t delay)
{
    g_waylandRepeatRate = std::max(rate, 0);
    g_waylandRepeatDelay = std::max(delay, 0);
    if (rate <= 0) StopWaylandRepeat();
}
static const wl_keyboard_listener g_keyboardListener = {
    HandleKeyboardKeymap, HandleKeyboardEnter, HandleKeyboardLeave, HandleKeyboardKey,
    HandleKeyboardModifiers, HandleKeyboardRepeatInfo
};

static void HandleSeatCapabilities(void*, wl_seat* seat, uint32_t capabilities)
{
    if ((capabilities & WL_SEAT_CAPABILITY_POINTER) && !g_waylandPointer)
    {
        g_waylandPointer = wl_seat_get_pointer(seat);
        wl_pointer_add_listener(g_waylandPointer, &g_pointerListener, nullptr);
    }
    else if (!(capabilities & WL_SEAT_CAPABILITY_POINTER) && g_waylandPointer)
    {
        wl_pointer_destroy(g_waylandPointer);
        g_waylandPointer = nullptr;
    }
    if ((capabilities & WL_SEAT_CAPABILITY_KEYBOARD) && !g_waylandKeyboard)
    {
        g_waylandKeyboard = wl_seat_get_keyboard(seat);
        wl_keyboard_add_listener(g_waylandKeyboard, &g_keyboardListener, nullptr);
    }
    else if (!(capabilities & WL_SEAT_CAPABILITY_KEYBOARD) && g_waylandKeyboard)
    {
        wl_keyboard_destroy(g_waylandKeyboard);
        g_waylandKeyboard = nullptr;
    }
}

static void HandleSeatName(void*, wl_seat*, const char*) {}
static const wl_seat_listener g_seatListener = { HandleSeatCapabilities, HandleSeatName };

static void HandleRegistryGlobal(void*, wl_registry* registry, uint32_t name,
                                 const char* interface, uint32_t version)
{
    if (strcmp(interface, wl_compositor_interface.name) == 0)
        g_waylandCompositor = static_cast<wl_compositor*>(wl_registry_bind(registry, name, &wl_compositor_interface, std::min(version, 4u)));
    else if (strcmp(interface, wl_shm_interface.name) == 0 && !g_waylandShm)
        g_waylandShm = static_cast<wl_shm*>(
            wl_registry_bind(registry, name, &wl_shm_interface, 1));
    else if (strcmp(interface, xdg_wm_base_interface.name) == 0)
    {
        g_xdgWmBase = static_cast<xdg_wm_base*>(wl_registry_bind(registry, name, &xdg_wm_base_interface, std::min(version, 6u)));
        xdg_wm_base_add_listener(g_xdgWmBase, &g_wmBaseListener, nullptr);
    }
    else if (strcmp(interface, wl_seat_interface.name) == 0 && !g_waylandSeat)
    {
        g_waylandSeat = static_cast<wl_seat*>(wl_registry_bind(registry, name, &wl_seat_interface, std::min(version, 9u)));
        wl_seat_add_listener(g_waylandSeat, &g_seatListener, nullptr);
        EnsureWaylandDataDevice();
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
        EnsureWaylandTextInput();
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
        EnsureWaylandTextInputV1();
#endif
    }
    else if (strcmp(interface, wl_data_device_manager_interface.name) == 0 &&
             !g_waylandDataDeviceManager)
    {
        g_waylandDataDeviceManager = static_cast<wl_data_device_manager*>(
            wl_registry_bind(registry, name, &wl_data_device_manager_interface,
                             std::min(version, 3u)));
        EnsureWaylandDataDevice();
    }
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
    else if (strcmp(interface, zwp_text_input_manager_v3_interface.name) == 0 &&
             !g_waylandTextInputManager)
    {
        g_waylandTextInputManager = static_cast<zwp_text_input_manager_v3*>(
            wl_registry_bind(registry, name, &zwp_text_input_manager_v3_interface, 1));
        EnsureWaylandTextInput();
    }
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
    else if (strcmp(interface, zwp_text_input_manager_v1_interface.name) == 0 &&
             !g_waylandTextInputManagerV1)
    {
        g_waylandTextInputManagerV1 = static_cast<zwp_text_input_manager_v1*>(
            wl_registry_bind(registry, name, &zwp_text_input_manager_v1_interface, 1));
        EnsureWaylandTextInputV1();
    }
#endif
}

static void HandleRegistryRemove(void*, wl_registry*, uint32_t) {}
static const wl_registry_listener g_registryListener = { HandleRegistryGlobal, HandleRegistryRemove };

static void ShutdownWayland()
{
    g_pointerFocus = nullptr;
    g_keyboardFocus = nullptr;
    g_waylandDragOffer = nullptr;
    g_waylandDragWindow = nullptr;
    g_waylandDropPending = false;
    g_waylandDragMime.clear();
    StopWaylandRepeat();
    if (g_waylandRepeatFd >= 0)
    {
        RemoveEpollFd(g_waylandRepeatFd);
        close(g_waylandRepeatFd);
        g_waylandRepeatFd = -1;
    }
    if (g_waylandClipboardSource)
    {
        wl_data_source_destroy(g_waylandClipboardSource);
        g_waylandClipboardSource = nullptr;
    }
    if (g_waylandSelectionOffer) DestroyWaylandOffer(g_waylandSelectionOffer);
    while (!g_waylandOffers.empty())
    {
        WaylandDataOfferState* offer = g_waylandOffers.begin()->second;
        DestroyWaylandOffer(offer);
    }
    if (g_waylandDataDevice)
    {
        wl_data_device_destroy(g_waylandDataDevice);
        g_waylandDataDevice = nullptr;
    }
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
    if (g_waylandTextInput)
    {
        zwp_text_input_v3_destroy(g_waylandTextInput);
        g_waylandTextInput = nullptr;
    }
    if (g_waylandTextInputManager)
    {
        zwp_text_input_manager_v3_destroy(g_waylandTextInputManager);
        g_waylandTextInputManager = nullptr;
    }
    g_pendingWaylandPreedit.clear();
    g_pendingWaylandCommit.clear();
    g_pendingWaylandPreeditSet = false;
    g_pendingWaylandCommitSet = false;
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
    if (g_waylandTextInputV1)
    {
        zwp_text_input_v1_destroy(g_waylandTextInputV1);
        g_waylandTextInputV1 = nullptr;
    }
    if (g_waylandTextInputManagerV1)
    {
        zwp_text_input_manager_v1_destroy(g_waylandTextInputManagerV1);
        g_waylandTextInputManagerV1 = nullptr;
    }
    g_waylandTextInputV1Active = false;
    g_waylandTextInputSerialV1 = 0;
    g_waylandTextInputCursorV1 = 0;
#endif
    g_waylandCompositionActive = false;
    if (g_waylandPointer) { wl_pointer_destroy(g_waylandPointer); g_waylandPointer = nullptr; }
    if (g_waylandKeyboard) { wl_keyboard_destroy(g_waylandKeyboard); g_waylandKeyboard = nullptr; }
    if (g_waylandSeat) { wl_seat_destroy(g_waylandSeat); g_waylandSeat = nullptr; }
    if (g_waylandDataDeviceManager)
    {
        wl_data_device_manager_destroy(g_waylandDataDeviceManager);
        g_waylandDataDeviceManager = nullptr;
    }
    if (g_xdgWmBase) { xdg_wm_base_destroy(g_xdgWmBase); g_xdgWmBase = nullptr; }
    if (g_waylandShm) { wl_shm_destroy(g_waylandShm); g_waylandShm = nullptr; }
    if (g_waylandCompositor) { wl_compositor_destroy(g_waylandCompositor); g_waylandCompositor = nullptr; }
    if (g_waylandRegistry) { wl_registry_destroy(g_waylandRegistry); g_waylandRegistry = nullptr; }
    if (g_xkbState) { xkb_state_unref(g_xkbState); g_xkbState = nullptr; }
    if (g_xkbKeymap) { xkb_keymap_unref(g_xkbKeymap); g_xkbKeymap = nullptr; }
    if (g_xkbContext) { xkb_context_unref(g_xkbContext); g_xkbContext = nullptr; }
    if (g_waylandDisplay) { wl_display_disconnect(g_waylandDisplay); g_waylandDisplay = nullptr; }
    g_waylandFd = -1;
    g_waylandInputSerial = 0;
    g_waylandPointerSerial = 0;
    g_waylandPointerButtons = 0;
}

static bool TryInitializeWayland()
{
    g_waylandDisplay = wl_display_connect(nullptr);
    if (!g_waylandDisplay) return false;
    g_xkbContext = xkb_context_new(XKB_CONTEXT_NO_FLAGS);
    g_waylandRegistry = wl_display_get_registry(g_waylandDisplay);
    if (!g_xkbContext || !g_waylandRegistry) { ShutdownWayland(); return false; }
    wl_registry_add_listener(g_waylandRegistry, &g_registryListener, nullptr);
    if (wl_display_roundtrip(g_waylandDisplay) < 0 ||
        wl_display_roundtrip(g_waylandDisplay) < 0 ||
        !g_waylandCompositor || !g_waylandShm || !g_xdgWmBase)
    {
        ShutdownWayland();
        return false;
    }
    if (InputDiagnosticsEnabled())
    {
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
        if (g_waylandTextInputManager)
            fprintf(stderr, "[Jalium.Input] Wayland IME: text-input-v3.\n");
        else
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
        if (g_waylandTextInputManagerV1)
            fprintf(stderr, "[Jalium.Input] Wayland IME: text-input-v1 compatibility.\n");
        else
#endif
            fprintf(stderr, "[Jalium.Input] Wayland IME unavailable; using xkb UTF-32 commit fallback.\n");
    }
    g_waylandFd = wl_display_get_fd(g_waylandDisplay);
    if (!AddEpollFd(g_waylandFd)) { ShutdownWayland(); return false; }
    g_waylandRepeatFd = timerfd_create(CLOCK_MONOTONIC, TFD_NONBLOCK | TFD_CLOEXEC);
    if (g_waylandRepeatFd < 0 || !AddEpollFd(g_waylandRepeatFd))
    {
        ShutdownWayland();
        return false;
    }
    return true;
}
#endif

void jalium_platform_shutdown_impl();

static bool TryInitializeX11()
{
    XrmInitialize();
    if (!XInitThreads()) return false;
    g_display = XOpenDisplay(nullptr);
    if (!g_display) return false;
    g_screen = DefaultScreen(g_display);
    g_rootWindow = RootWindow(g_display, g_screen);
    g_wmDeleteWindow = XInternAtom(g_display, "WM_DELETE_WINDOW", False);
    g_wmProtocols = XInternAtom(g_display, "WM_PROTOCOLS", False);
    Bool detectableRepeat = False;
    (void)XkbSetDetectableAutoRepeat(g_display, True, &detectableRepeat);
    XSetLocaleModifiers("");
    g_xim = XOpenIM(g_display, nullptr, nullptr, nullptr);
    if (InputDiagnosticsEnabled())
        fprintf(stderr, "%s\n", g_xim
            ? "[Jalium.Input] X11 IME: XIM/Xutf8LookupString."
            : "[Jalium.Input] X11 IME unavailable; using locale XLookupString fallback.");
    EnsureClipboardAtoms();
    EnsureXdndAtoms();
    XSetWindowAttributes clipboardAttributes{};
    clipboardAttributes.event_mask = PropertyChangeMask;
    g_clipboardWindow = XCreateWindow(
        g_display, g_rootWindow, -10, -10, 1, 1, 0, 0, InputOnly,
        CopyFromParent, CWEventMask, &clipboardAttributes);
    if (!g_clipboardWindow)
    {
        if (g_xim) { XCloseIM(g_xim); g_xim = nullptr; }
        XCloseDisplay(g_display);
        g_display = nullptr;
        return false;
    }
    if (!AddEpollFd(ConnectionNumber(g_display)))
    {
        if (g_xim) { XCloseIM(g_xim); g_xim = nullptr; }
        XCloseDisplay(g_display);
        g_display = nullptr;
        return false;
    }
    return true;
}

JaliumResult jalium_platform_init_impl()
{
    setlocale(LC_ALL, "");
    signal(SIGPIPE, SIG_IGN);
    for (auto& state : g_keyStates) state.store(0, std::memory_order_relaxed);
    g_epollFd = epoll_create1(EPOLL_CLOEXEC);
    if (g_epollFd < 0) return JALIUM_ERROR_INITIALIZATION_FAILED;

    std::string requested = getenv("JALIUM_WINDOW_SYSTEM") ? getenv("JALIUM_WINDOW_SYSTEM") : "auto";
    std::transform(requested.begin(), requested.end(), requested.begin(),
                   [](unsigned char value) { return static_cast<char>(std::tolower(value)); });
    if (requested != "auto" && requested != "x11" && requested != "wayland")
    {
        close(g_epollFd);
        g_epollFd = -1;
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    bool initialized = false;
#ifdef JALIUM_HAS_WAYLAND
    const bool waylandAvailable = getenv("WAYLAND_DISPLAY") && *getenv("WAYLAND_DISPLAY");
    if (requested == "wayland" || (requested == "auto" && waylandAvailable))
    {
        initialized = TryInitializeWayland();
        if (initialized) g_windowSystem = LinuxWindowSystem::Wayland;
        else if (requested == "wayland")
        {
            close(g_epollFd);
            g_epollFd = -1;
            return JALIUM_ERROR_INITIALIZATION_FAILED;
        }
    }
#else
    if (requested == "wayland")
    {
        close(g_epollFd);
        g_epollFd = -1;
        return JALIUM_ERROR_NOT_SUPPORTED;
    }
#endif
    if (!initialized && (requested == "auto" || requested == "x11"))
    {
        initialized = TryInitializeX11();
        if (initialized) g_windowSystem = LinuxWindowSystem::XServer;
    }
    if (!initialized)
    {
        close(g_epollFd);
        g_epollFd = -1;
        return JALIUM_ERROR_INITIALIZATION_FAILED;
    }

    g_wakeEventFd = eventfd(0, EFD_NONBLOCK | EFD_CLOEXEC);
    if (g_wakeEventFd < 0 || !AddEpollFd(g_wakeEventFd))
    {
        jalium_platform_shutdown_impl();
        return JALIUM_ERROR_INITIALIZATION_FAILED;
    }
    g_quitRequested.store(false, std::memory_order_release);
    g_exitCode.store(0, std::memory_order_release);
    return JALIUM_OK;
}

void jalium_platform_shutdown_impl()
{
    g_quitRequested.store(true, std::memory_order_release);
    (void)SignalEventFd(g_wakeEventFd);

    // Detach every event source before closing epoll. Keep each opaque wrapper
    // alive so a later managed Dispose remains safe, but release its Linux fd.
    std::vector<JaliumDispatcher*> dispatchers;
    std::vector<int> dispatcherFds;
    std::vector<JaliumTimer*> timers;
    std::vector<int> timerFds;
    {
        std::lock_guard<std::mutex> lock(g_eventSourceMutex);
        dispatchers.reserve(g_allDispatchers.size());
        dispatcherFds.reserve(g_allDispatchers.size());
        for (JaliumDispatcher* dispatcher : g_allDispatchers)
        {
            dispatcher->references.fetch_add(1, std::memory_order_relaxed);
            dispatchers.push_back(dispatcher);
            dispatcherFds.push_back(
                dispatcher->eventFd.exchange(-1, std::memory_order_acq_rel));
        }
        timers.reserve(g_allTimers.size());
        timerFds.reserve(g_allTimers.size());
        for (JaliumTimer* timer : g_allTimers)
        {
            timer->references.fetch_add(1, std::memory_order_relaxed);
            timers.push_back(timer);
            timer->registeredWithEpoll.store(false, std::memory_order_release);
            timerFds.push_back(
                timer->timerFd.exchange(-1, std::memory_order_acq_rel));
        }
        g_dispatchersByFd.clear();
        g_timersByFd.clear();
        g_allDispatchers.clear();
        g_allTimers.clear();
    }

    for (size_t index = 0; index < dispatchers.size(); ++index)
    {
        if (dispatcherFds[index] >= 0) close(dispatcherFds[index]);
        ReleaseDispatcher(dispatchers[index]);
    }
    for (size_t index = 0; index < timers.size(); ++index)
    {
        if (timerFds[index] >= 0) close(timerFds[index]);
        ReleaseTimer(timers[index]);
    }

    if (g_wakeEventFd >= 0) { close(g_wakeEventFd); g_wakeEventFd = -1; }
    if (g_epollFd >= 0) { close(g_epollFd); g_epollFd = -1; }

#ifdef JALIUM_HAS_WAYLAND
    {
        std::lock_guard<std::mutex> lock(g_windowMapMutex);
        for (JaliumPlatformWindow* window : g_waylandWindows)
        {
            window->destroyed = true;
            DestroyWaylandRole(window);
            if (window->waylandSurface)
            {
                wl_surface_destroy(window->waylandSurface);
                window->waylandSurface = nullptr;
            }
        }
        g_waylandWindows.clear();
    }
    ShutdownWayland();
#endif

    // Tear down any X11 resources whose managed wrappers outlived the platform
    // loop. jalium_window_destroy remains safe afterwards and deletes wrappers.
    {
        std::lock_guard<std::mutex> lock(g_windowMapMutex);
        for (auto& entry : g_windowMap)
        {
            JaliumPlatformWindow* window = entry.second;
            window->destroyed = true;
            if (window->xic) { XDestroyIC(window->xic); window->xic = nullptr; }
            if (window->xwindow && g_display)
            {
                XDestroyWindow(g_display, window->xwindow);
                window->xwindow = 0;
            }
        }
        g_windowMap.clear();
    }

    if (g_xim) { XCloseIM(g_xim); g_xim = nullptr; }
    if (g_clipboardWindow && g_display)
    {
        XDestroyWindow(g_display, g_clipboardWindow);
        g_clipboardWindow = 0;
    }
    if (g_display) { XCloseDisplay(g_display); g_display = nullptr; }
    g_screen = 0;
    g_rootWindow = 0;
    g_wmDeleteWindow = 0;
    g_wmProtocols = 0;
    g_clipboardAtom = 0;
    g_utf8StringAtom = 0;
    g_targetsAtom = 0;
    g_jaliumClipProp = 0;
    g_textPlainUtf8Atom = 0;
    g_textPlainAtom = 0;
    g_incrAtom = 0;
    g_xdndAwareAtom = 0;
    g_xdndEnterAtom = 0;
    g_xdndPositionAtom = 0;
    g_xdndStatusAtom = 0;
    g_xdndLeaveAtom = 0;
    g_xdndDropAtom = 0;
    g_xdndFinishedAtom = 0;
    g_xdndSelectionAtom = 0;
    g_xdndTypeListAtom = 0;
    g_xdndActionListAtom = 0;
    g_xdndActionCopyAtom = 0;
    g_xdndActionMoveAtom = 0;
    g_xdndActionLinkAtom = 0;
    g_xdndDataAtom = 0;
    g_uriListAtom = 0;
    {
        std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
        g_clipboardUtf8.clear();
    }
    ClearPressedKeyStates();
    g_windowSystem = LinuxWindowSystem::Disabled;
}

JaliumPlatform jalium_platform_get_current_impl()
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
        return JALIUM_PLATFORM_LINUX_WAYLAND;
#endif
    return g_windowSystem == LinuxWindowSystem::XServer
        ? JALIUM_PLATFORM_LINUX_X11
        : JALIUM_PLATFORM_UNKNOWN;
}

// ============================================================================
// Window Management
// ============================================================================

JaliumPlatformWindow* jalium_window_create(const JaliumWindowParams* params)
{
    if (!params) return nullptr;

    auto win = new JaliumPlatformWindow();
    win->style = params->style;
    win->width = params->width > 0 ? params->width : 800;
    win->height = params->height > 0 ? params->height : 600;
    win->x = params->x == JALIUM_DEFAULT_POS ? 0 : params->x;
    win->y = params->y == JALIUM_DEFAULT_POS ? 0 : params->y;

#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland && g_waylandCompositor)
    {
        win->dpiScale = 1.0f;
        win->waylandTitle = Utf16ToUtf8(params->title);
        win->waylandSurface = wl_compositor_create_surface(g_waylandCompositor);
        if (!win->waylandSurface)
        {
            delete win;
            return nullptr;
        }
        wl_surface_set_user_data(win->waylandSurface, win);
        if (!CreateWaylandRole(win))
        {
            wl_surface_destroy(win->waylandSurface);
            delete win;
            return nullptr;
        }
        {
            std::lock_guard<std::mutex> lock(g_windowMapMutex);
            g_waylandWindows.insert(win);
        }
        return win;
    }
#endif

    if (g_windowSystem != LinuxWindowSystem::XServer || !g_display)
    {
        delete win;
        return nullptr;
    }
    win->dpiScale = DetectDpiScale();

    XSetWindowAttributes swa{};
    swa.event_mask = ExposureMask | KeyPressMask | KeyReleaseMask |
                     ButtonPressMask | ButtonReleaseMask | PointerMotionMask |
                     StructureNotifyMask | FocusChangeMask |
                     EnterWindowMask | LeaveWindowMask;
    swa.background_pixel = BlackPixel(g_display, g_screen);

    unsigned long valueMask = CWEventMask | CWBackPixel;

    // Override redirect for popup windows
    if (params->style & JALIUM_WINDOW_STYLE_POPUP)
    {
        swa.override_redirect = True;
        valueMask |= CWOverrideRedirect;
    }

    win->xwindow = XCreateWindow(
        g_display, g_rootWindow,
        win->x, win->y, win->width, win->height,
        0,
        CopyFromParent, InputOutput, CopyFromParent,
        valueMask, &swa
    );

    if (!win->xwindow)
    {
        delete win;
        return nullptr;
    }

    // XDND version 5 is backward compatible with v3/v4 sources and supports
    // negotiated copy/move/link actions plus XdndTypeList.
    const unsigned long xdndVersion = 5;
    XChangeProperty(g_display, win->xwindow, g_xdndAwareAtom, XA_ATOM, 32,
                    PropModeReplace,
                    reinterpret_cast<const unsigned char*>(&xdndVersion), 1);

    // Set WM_DELETE_WINDOW protocol
    XSetWMProtocols(g_display, win->xwindow, &g_wmDeleteWindow, 1);

    // Set window title
    SetX11WindowTitle(win->xwindow, params->title);

    // Window size hints
    if (!(params->style & JALIUM_WINDOW_STYLE_RESIZABLE))
    {
        XSizeHints hints{};
        hints.flags = PMinSize | PMaxSize;
        hints.min_width = hints.max_width = win->width;
        hints.min_height = hints.max_height = win->height;
        XSetWMNormalHints(g_display, win->xwindow, &hints);
    }

    // Borderless: set _MOTIF_WM_HINTS
    if (params->style & JALIUM_WINDOW_STYLE_BORDERLESS)
    {
        Atom motifHints = XInternAtom(g_display, "_MOTIF_WM_HINTS", False);
        struct {
            unsigned long flags;
            unsigned long functions;
            unsigned long decorations;
            long          inputMode;
            unsigned long status;
        } hints = {2, 0, 0, 0, 0}; // flags=MWM_HINTS_DECORATIONS, decorations=0
        XChangeProperty(g_display, win->xwindow, motifHints, motifHints,
                       32, PropModeReplace,
                       reinterpret_cast<unsigned char*>(&hints), 5);
    }

    // Topmost: set _NET_WM_STATE_ABOVE
    if (params->style & JALIUM_WINDOW_STYLE_TOPMOST)
    {
        Atom netWmState = XInternAtom(g_display, "_NET_WM_STATE", False);
        Atom netWmStateAbove = XInternAtom(g_display, "_NET_WM_STATE_ABOVE", False);
        XChangeProperty(g_display, win->xwindow, netWmState, XA_ATOM,
                       32, PropModeReplace,
                       reinterpret_cast<unsigned char*>(&netWmStateAbove), 1);
    }

    // Create XIC for text input
    if (g_xim)
    {
        win->ximPreeditStart.client_data = reinterpret_cast<XPointer>(win);
        win->ximPreeditStart.callback = reinterpret_cast<XIMProc>(XimPreeditStartCallback);
        win->ximPreeditDone.client_data = reinterpret_cast<XPointer>(win);
        win->ximPreeditDone.callback = reinterpret_cast<XIMProc>(XimPreeditDoneCallback);
        win->ximPreeditDraw.client_data = reinterpret_cast<XPointer>(win);
        win->ximPreeditDraw.callback = reinterpret_cast<XIMProc>(XimPreeditDrawCallback);
        win->ximPreeditCaret.client_data = reinterpret_cast<XPointer>(win);
        win->ximPreeditCaret.callback = reinterpret_cast<XIMProc>(XimPreeditCaretCallback);

        XVaNestedList preedit = XVaCreateNestedList(
            0,
            XNPreeditStartCallback, &win->ximPreeditStart,
            XNPreeditDoneCallback, &win->ximPreeditDone,
            XNPreeditDrawCallback, &win->ximPreeditDraw,
            XNPreeditCaretCallback, &win->ximPreeditCaret,
            nullptr);
        if (preedit)
        {
            win->xic = XCreateIC(g_xim,
                                 XNInputStyle, XIMPreeditCallbacks | XIMStatusNothing,
                                 XNClientWindow, win->xwindow,
                                 XNFocusWindow, win->xwindow,
                                 XNPreeditAttributes, preedit,
                                 nullptr);
            XFree(preedit);
        }
        if (!win->xic)
        {
            // Some minimal XIM implementations only expose PreeditNothing.
            // Commit text still flows through Xutf8LookupString in that mode.
            win->xic = XCreateIC(g_xim,
                                 XNInputStyle, XIMPreeditNothing | XIMStatusNothing,
                                 XNClientWindow, win->xwindow,
                                 XNFocusWindow, win->xwindow,
                                 nullptr);
        }
    }

    // Register in window map
    {
        std::lock_guard<std::mutex> lock(g_windowMapMutex);
        g_windowMap[win->xwindow] = win;
    }

    XFlush(g_display);
    return win;
}

void jalium_window_destroy(JaliumPlatformWindow* window)
{
    if (!window) return;

#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
        {
            std::lock_guard<std::mutex> lock(g_windowMapMutex);
            g_waylandWindows.erase(window);
        }
        if (g_pointerFocus == window) g_pointerFocus = nullptr;
        if (g_keyboardFocus == window) g_keyboardFocus = nullptr;
        if (g_waylandRepeatWindow == window) StopWaylandRepeat();
        DestroyWaylandRole(window);
        wl_surface_destroy(window->waylandSurface);
        window->waylandSurface = nullptr;
        if (g_waylandDisplay) wl_display_flush(g_waylandDisplay);
        delete window;
        return;
    }
#endif

    {
        std::lock_guard<std::mutex> lock(g_windowMapMutex);
        g_windowMap.erase(window->xwindow);
    }

    if (window->xic)
        XDestroyIC(window->xic);

    if (window->xwindow && g_display)
        XDestroyWindow(g_display, window->xwindow);

    delete window;
}

void jalium_window_show(JaliumPlatformWindow* window)
{
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface)
    {
        window->waylandVisible.store(true, std::memory_order_release);
        (void)CreateWaylandRole(window);
        wl_surface_commit(window->waylandSurface);
        wl_display_flush(g_waylandDisplay);
        DispatchWaylandPaint(window);
        return;
    }
#endif
    if (window && g_display)
    {
        XMapRaised(g_display, window->xwindow);
        XFlush(g_display);
    }
}

void jalium_window_hide(JaliumPlatformWindow* window)
{
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface)
    {
        window->waylandVisible.store(false, std::memory_order_release);
        window->waylandPaintPending.store(false, std::memory_order_release);
        wl_surface_attach(window->waylandSurface, nullptr, 0, 0);
        wl_surface_commit(window->waylandSurface);
        DestroyWaylandRole(window);
        wl_display_flush(g_waylandDisplay);
        return;
    }
#endif
    if (window && g_display)
    {
        XUnmapWindow(g_display, window->xwindow);
        XFlush(g_display);
    }
}

void jalium_window_set_title(JaliumPlatformWindow* window, const JaliumUtf16Char* title)
{
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface)
    {
        window->waylandTitle = Utf16ToUtf8(title);
        if (window->xdgToplevel)
            xdg_toplevel_set_title(window->xdgToplevel, window->waylandTitle.c_str());
        wl_display_flush(g_waylandDisplay);
        return;
    }
#endif
    if (!window || !g_display) return;
    SetX11WindowTitle(window->xwindow, title);
    XFlush(g_display);
}

void jalium_window_resize(JaliumPlatformWindow* window, int32_t width, int32_t height)
{
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface && width > 0 && height > 0)
    {
        if (window->width != width || window->height != height)
        {
            window->width = width;
            window->height = height;
            JaliumPlatformEvent event{};
            event.type = JALIUM_EVENT_RESIZE;
            event.window = window;
            event.resize.width = width;
            event.resize.height = height;
            window->DispatchEvent(event);
        }
        DispatchWaylandPaint(window);
        return;
    }
#endif
    if (window && g_display)
    {
        XResizeWindow(g_display, window->xwindow, width, height);
        XFlush(g_display);
    }
}

void jalium_window_move(JaliumPlatformWindow* window, int32_t x, int32_t y)
{
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface)
    {
        // xdg-shell intentionally gives clients no absolute positioning API.
        window->x = x;
        window->y = y;
        return;
    }
#endif
    if (window && g_display)
    {
        XMoveWindow(g_display, window->xwindow, x, y);
        XFlush(g_display);
    }
}

void jalium_window_set_state(JaliumPlatformWindow* window, JaliumWindowState state)
{
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface)
    {
        if (!window->xdgToplevel && !CreateWaylandRole(window)) return;
        switch (state)
        {
        case JALIUM_WINDOW_STATE_NORMAL:
            xdg_toplevel_unset_maximized(window->xdgToplevel);
            xdg_toplevel_unset_fullscreen(window->xdgToplevel);
            break;
        case JALIUM_WINDOW_STATE_MINIMIZED:
            xdg_toplevel_set_minimized(window->xdgToplevel);
            break;
        case JALIUM_WINDOW_STATE_MAXIMIZED:
            xdg_toplevel_set_maximized(window->xdgToplevel);
            break;
        case JALIUM_WINDOW_STATE_FULLSCREEN:
            xdg_toplevel_set_fullscreen(window->xdgToplevel, nullptr);
            break;
        }
        window->state = state;
        wl_display_flush(g_waylandDisplay);
        return;
    }
#endif
    if (!window || !g_display) return;

    Atom netWmState = XInternAtom(g_display, "_NET_WM_STATE", False);
    Atom netMaxH = XInternAtom(g_display, "_NET_WM_STATE_MAXIMIZED_HORZ", False);
    Atom netMaxV = XInternAtom(g_display, "_NET_WM_STATE_MAXIMIZED_VERT", False);
    Atom netFullscreen = XInternAtom(g_display, "_NET_WM_STATE_FULLSCREEN", False);
    Atom netHidden = XInternAtom(g_display, "_NET_WM_STATE_HIDDEN", False);

    XEvent ev{};
    ev.type = ClientMessage;
    ev.xclient.window = window->xwindow;
    ev.xclient.message_type = netWmState;
    ev.xclient.format = 32;

    switch (state)
    {
    case JALIUM_WINDOW_STATE_NORMAL:
        // Remove maximized and fullscreen
        ev.xclient.data.l[0] = 0; // _NET_WM_STATE_REMOVE
        ev.xclient.data.l[1] = netMaxH;
        ev.xclient.data.l[2] = netMaxV;
        XSendEvent(g_display, g_rootWindow, False, SubstructureRedirectMask | SubstructureNotifyMask, &ev);
        ev.xclient.data.l[1] = netFullscreen;
        ev.xclient.data.l[2] = 0;
        XSendEvent(g_display, g_rootWindow, False, SubstructureRedirectMask | SubstructureNotifyMask, &ev);
        break;

    case JALIUM_WINDOW_STATE_MINIMIZED:
        XIconifyWindow(g_display, window->xwindow, g_screen);
        break;

    case JALIUM_WINDOW_STATE_MAXIMIZED:
        ev.xclient.data.l[0] = 1; // _NET_WM_STATE_ADD
        ev.xclient.data.l[1] = netMaxH;
        ev.xclient.data.l[2] = netMaxV;
        XSendEvent(g_display, g_rootWindow, False, SubstructureRedirectMask | SubstructureNotifyMask, &ev);
        break;

    case JALIUM_WINDOW_STATE_FULLSCREEN:
        ev.xclient.data.l[0] = 1; // _NET_WM_STATE_ADD
        ev.xclient.data.l[1] = netFullscreen;
        ev.xclient.data.l[2] = 0;
        XSendEvent(g_display, g_rootWindow, False, SubstructureRedirectMask | SubstructureNotifyMask, &ev);
        break;
    }

    XFlush(g_display);
}

JaliumWindowState jalium_window_get_state(JaliumPlatformWindow* window)
{
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface) return window->state;
#endif
    // TODO: Query _NET_WM_STATE atoms
    return JALIUM_WINDOW_STATE_NORMAL;
}

intptr_t jalium_window_get_native_handle(JaliumPlatformWindow* window)
{
    if (!window) return 0;
#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface) return reinterpret_cast<intptr_t>(window->waylandSurface);
#endif
    return static_cast<intptr_t>(window->xwindow);
}

JaliumSurfaceDescriptor jalium_window_get_surface(JaliumPlatformWindow* window)
{
    JaliumSurfaceDescriptor desc{};
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface && g_waylandDisplay)
    {
        desc.platform = JALIUM_PLATFORM_LINUX_WAYLAND;
        desc.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        desc.handle0 = reinterpret_cast<intptr_t>(g_waylandDisplay);
        desc.handle1 = reinterpret_cast<intptr_t>(window->waylandSurface);
        // Borrowed wl_shm proxy. Render backends may create pools/buffers from
        // it but must not destroy it; the platform owns it until shutdown.
        desc.handle2 = reinterpret_cast<intptr_t>(g_waylandShm);
        return desc;
    }
#endif
    if (window && g_display)
    {
        desc.platform = JALIUM_PLATFORM_LINUX_X11;
        desc.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        desc.handle0 = reinterpret_cast<intptr_t>(g_display);
        desc.handle1 = static_cast<intptr_t>(window->xwindow);
    }
    return desc;
}

int32_t jalium_wayland_surface_is_ready(intptr_t waylandSurface)
{
    if (waylandSurface == 0) return 0;
#ifdef JALIUM_HAS_WAYLAND
    std::lock_guard<std::mutex> lock(g_windowMapMutex);
    for (JaliumPlatformWindow* window : g_waylandWindows)
    {
        if (reinterpret_cast<intptr_t>(window->waylandSurface) == waylandSurface)
        {
            return window->waylandVisible.load(std::memory_order_acquire) &&
                   window->waylandConfigured.load(std::memory_order_acquire)
                ? 1 : 0;
        }
    }
#endif
    // An external wl_surface is configured by its embedder, not Jalium's
    // xdg-shell role manager, so retain the pre-existing presentation contract.
    return 1;
}

void jalium_window_set_event_callback(JaliumPlatformWindow* window,
                                       JaliumEventCallback callback, void* userData)
{
    if (!window) return;
    window->callback = callback;
    window->userData = userData;
}

void jalium_window_invalidate(JaliumPlatformWindow* window)
{
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface)
    {
        DispatchWaylandPaint(window);
        return;
    }
#endif
    if (!window || !g_display) return;

    XEvent ev{};
    ev.type = Expose;
    ev.xexpose.window = window->xwindow;
    ev.xexpose.count = 0;
    XSendEvent(g_display, window->xwindow, False, ExposureMask, &ev);
    XFlush(g_display);
}

void jalium_window_set_cursor(JaliumPlatformWindow* window, JaliumCursorShape cursor)
{
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface)
    {
        // Cursor themes are compositor/scale dependent. Pointer delivery is
        // complete; themed cursor surfaces are added by the desktop-integration
        // layer without changing this window ABI.
        (void)cursor;
        return;
    }
#endif
    if (!window || !g_display) return;

    unsigned int cursorShape;
    switch (cursor)
    {
    case JALIUM_CURSOR_HAND:        cursorShape = 60; break; // XC_hand2
    case JALIUM_CURSOR_IBEAM:       cursorShape = 152; break; // XC_xterm
    case JALIUM_CURSOR_CROSSHAIR:   cursorShape = 34; break; // XC_crosshair
    case JALIUM_CURSOR_RESIZE_NS:   cursorShape = 116; break; // XC_sb_v_double_arrow
    case JALIUM_CURSOR_RESIZE_EW:   cursorShape = 108; break; // XC_sb_h_double_arrow
    case JALIUM_CURSOR_RESIZE_NESW: cursorShape = 12; break; // XC_bottom_left_corner
    case JALIUM_CURSOR_RESIZE_NWSE: cursorShape = 14; break; // XC_bottom_right_corner
    case JALIUM_CURSOR_RESIZE_ALL:  cursorShape = 52; break; // XC_fleur
    case JALIUM_CURSOR_NOT_ALLOWED: cursorShape = 0; break; // XC_X_cursor
    case JALIUM_CURSOR_WAIT:        cursorShape = 150; break; // XC_watch
    case JALIUM_CURSOR_HIDDEN:
    {
        // Create invisible cursor
        Pixmap pixmap = XCreatePixmap(g_display, window->xwindow, 1, 1, 1);
        XColor color{};
        Cursor blankCursor = XCreatePixmapCursor(g_display, pixmap, pixmap, &color, &color, 0, 0);
        XDefineCursor(g_display, window->xwindow, blankCursor);
        XFreeCursor(g_display, blankCursor);
        XFreePixmap(g_display, pixmap);
        XFlush(g_display);
        return;
    }
    default: cursorShape = 68; break; // XC_left_ptr
    }

    Cursor xCursor = XCreateFontCursor(g_display, cursorShape);
    XDefineCursor(g_display, window->xwindow, xCursor);
    XFreeCursor(g_display, xCursor);
    XFlush(g_display);
}

void jalium_window_get_client_size(JaliumPlatformWindow* window, int32_t* width, int32_t* height)
{
    if (!window || (!g_display
#ifdef JALIUM_HAS_WAYLAND
        && !window->waylandSurface
#endif
        ))
    {
        if (width) *width = 0;
        if (height) *height = 0;
        return;
    }
    if (width) *width = window->width;
    if (height) *height = window->height;
}

void jalium_window_get_position(JaliumPlatformWindow* window, int32_t* x, int32_t* y)
{
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface)
    {
        if (x) *x = window->x;
        if (y) *y = window->y;
        return;
    }
#endif
    if (!window || !g_display) { if (x) *x = 0; if (y) *y = 0; return; }

    int rx, ry;
    Window child;
    XTranslateCoordinates(g_display, window->xwindow, g_rootWindow, 0, 0, &rx, &ry, &child);
    if (x) *x = rx;
    if (y) *y = ry;
}

// ============================================================================
// Event Processing
// ============================================================================

static JaliumPlatformWindow* FindWindow(Window xwin)
{
    std::lock_guard<std::mutex> lock(g_windowMapMutex);
    auto it = g_windowMap.find(xwin);
    return (it != g_windowMap.end()) ? it->second : nullptr;
}

static void ProcessXEvent(XEvent& xev)
{
    if (ProcessClipboardXEvent(xev)) return;
    if (ProcessXdndXEvent(xev)) return;
    JaliumPlatformWindow* win = FindWindow(xev.xany.window);
    if (!win) return;

    JaliumPlatformEvent evt{};
    evt.window = win;

    switch (xev.type)
    {
    case Expose:
        if (xev.xexpose.count == 0)
        {
            evt.type = JALIUM_EVENT_PAINT;
            win->DispatchEvent(evt);
        }
        break;

    case ConfigureNotify:
        if (xev.xconfigure.width != win->width || xev.xconfigure.height != win->height)
        {
            win->width = xev.xconfigure.width;
            win->height = xev.xconfigure.height;
            evt.type = JALIUM_EVENT_RESIZE;
            evt.resize.width = win->width;
            evt.resize.height = win->height;
            win->DispatchEvent(evt);
        }
        break;

    case ClientMessage:
        if (static_cast<Atom>(xev.xclient.data.l[0]) == g_wmDeleteWindow)
        {
            evt.type = JALIUM_EVENT_CLOSE_REQUESTED;
            win->DispatchEvent(evt);
        }
        break;

    case FocusIn:
        if (win->xic) XSetICFocus(win->xic);
        evt.type = JALIUM_EVENT_FOCUS_GAINED;
        win->DispatchEvent(evt);
        break;

    case FocusOut:
        if (win->xic) XUnsetICFocus(win->xic);
        ClearPressedKeyStates();
        evt.type = JALIUM_EVENT_FOCUS_LOST;
        win->DispatchEvent(evt);
        break;

    case MotionNotify:
        evt.type = JALIUM_EVENT_MOUSE_MOVE;
        evt.mouse.x = static_cast<float>(xev.xmotion.x);
        evt.mouse.y = static_cast<float>(xev.xmotion.y);
        evt.mouse.modifiers = GetX11Modifiers(xev.xmotion.state);
        win->DispatchEvent(evt);
        break;

    case ButtonPress:
        // Scroll wheel
        if (xev.xbutton.button == Button4 || xev.xbutton.button == Button5 ||
            xev.xbutton.button == 6 || xev.xbutton.button == 7)
        {
            evt.type = JALIUM_EVENT_MOUSE_WHEEL;
            evt.wheel.x = static_cast<float>(xev.xbutton.x);
            evt.wheel.y = static_cast<float>(xev.xbutton.y);
            evt.wheel.modifiers = GetX11Modifiers(xev.xbutton.state);
            if (xev.xbutton.button == Button4) evt.wheel.deltaY = 1.0f;
            else if (xev.xbutton.button == Button5) evt.wheel.deltaY = -1.0f;
            else if (xev.xbutton.button == 6) evt.wheel.deltaX = -1.0f;
            else evt.wheel.deltaX = 1.0f;
            win->DispatchEvent(evt);
        }
        else
        {
            evt.type = JALIUM_EVENT_MOUSE_DOWN;
            evt.mouse.x = static_cast<float>(xev.xbutton.x);
            evt.mouse.y = static_cast<float>(xev.xbutton.y);
            evt.mouse.button = X11ButtonToJalium(xev.xbutton.button);
            evt.mouse.modifiers = GetX11Modifiers(xev.xbutton.state);
            evt.mouse.clickCount = RegisterClick(
                g_x11ClickTracker, win, evt.mouse.button, xev.xbutton.time,
                evt.mouse.x, evt.mouse.y);
            win->DispatchEvent(evt);
        }
        break;

    case ButtonRelease:
        if (xev.xbutton.button >= Button4 && xev.xbutton.button <= 7)
            break; // Ignore scroll button releases

        evt.type = JALIUM_EVENT_MOUSE_UP;
        evt.mouse.x = static_cast<float>(xev.xbutton.x);
        evt.mouse.y = static_cast<float>(xev.xbutton.y);
        evt.mouse.button = X11ButtonToJalium(xev.xbutton.button);
        evt.mouse.modifiers = GetX11Modifiers(xev.xbutton.state);
        win->DispatchEvent(evt);
        break;

    case EnterNotify:
        evt.type = JALIUM_EVENT_MOUSE_ENTER;
        win->DispatchEvent(evt);
        break;

    case LeaveNotify:
        evt.type = JALIUM_EVENT_MOUSE_LEAVE;
        win->DispatchEvent(evt);
        break;

    case KeyPress:
    {
        KeySym keysym = XkbKeycodeToKeysym(g_display, xev.xkey.keycode, 0, 0);
        const int32_t virtualKey = KeySymToJaliumVK(keysym);
        const bool wasDown = virtualKey >= 0 && virtualKey < static_cast<int32_t>(g_keyStates.size()) &&
            (g_keyStates[virtualKey].load(std::memory_order_acquire) & 0x80u) != 0;
        const bool toggled = virtualKey == 0x14 || virtualKey == 0x90 || virtualKey == 0x91;
        SetKeyState(virtualKey, true, toggled && !wasDown);
        evt.type = JALIUM_EVENT_KEY_DOWN;
        evt.key.keyCode = virtualKey;
        evt.key.scanCode = xev.xkey.keycode;
        evt.key.modifiers = GetX11Modifiers(xev.xkey.state);
        evt.key.isRepeat = wasDown ? 1 : 0;
        win->DispatchEvent(evt);

        std::string committedText;
        // Text input via XIC. XBufferOverflow is common for multi-character
        // CJK commits, so retry with the exact size instead of truncating it.
        if (win->xic)
        {
            std::array<char, 64> stackBuffer{};
            KeySym sym;
            Status status;
            int len = Xutf8LookupString(
                win->xic, &xev.xkey, stackBuffer.data(),
                static_cast<int>(stackBuffer.size() - 1), &sym, &status);
            if (status == XBufferOverflow && len > 0)
            {
                std::vector<char> dynamicBuffer(static_cast<size_t>(len) + 1);
                len = Xutf8LookupString(win->xic, &xev.xkey, dynamicBuffer.data(),
                                        len, &sym, &status);
                if (len > 0 && (status == XLookupChars || status == XLookupBoth))
                    committedText.assign(dynamicBuffer.data(), static_cast<size_t>(len));
            }
            else if (len > 0 && (status == XLookupChars || status == XLookupBoth))
                committedText.assign(stackBuffer.data(), static_cast<size_t>(len));
        }
        else
        {
            std::array<char, 64> buffer{};
            KeySym sym = NoSymbol;
            const int len = XLookupString(&xev.xkey, buffer.data(),
                                          static_cast<int>(buffer.size()), &sym, nullptr);
            if (len > 0) committedText.assign(buffer.data(), static_cast<size_t>(len));
        }
        DispatchUtf8Characters(win, committedText);
        break;
    }

    case KeyRelease:
    {
        // Check for auto-repeat (next event is KeyPress with same keycode and time)
        bool isRepeat = false;
        if (XEventsQueued(g_display, QueuedAfterReading))
        {
            XEvent next;
            XPeekEvent(g_display, &next);
            if (next.type == KeyPress &&
                next.xkey.keycode == xev.xkey.keycode &&
                next.xkey.time == xev.xkey.time)
            {
                isRepeat = true;
                // Keep the KeyPress queued: the normal path marks it repeat and
                // performs XIM lookup, preserving repeated character input.
            }
        }

        if (!isRepeat)
        {
            KeySym keysym = XkbKeycodeToKeysym(g_display, xev.xkey.keycode, 0, 0);
            evt.type = JALIUM_EVENT_KEY_UP;
            evt.key.keyCode = KeySymToJaliumVK(keysym);
            evt.key.scanCode = xev.xkey.keycode;
            evt.key.modifiers = GetX11Modifiers(xev.xkey.state);
            evt.key.isRepeat = 0;
            SetKeyState(evt.key.keyCode, false, false);
            win->DispatchEvent(evt);
        }
        break;
    }

    default:
        break;
    }
}

// ============================================================================
// Event Loop
// ============================================================================

int32_t jalium_platform_run_message_loop(void)
{
    if (g_windowSystem == LinuxWindowSystem::Disabled || g_epollFd < 0)
        return g_exitCode.load(std::memory_order_acquire);

    g_quitRequested.store(false, std::memory_order_release);

    while (!g_quitRequested.load(std::memory_order_acquire))
    {
        // Process all events already buffered by the selected display server.
        while (g_display && XPending(g_display))
        {
            XEvent xev;
            XNextEvent(g_display, &xev);
            if (!XFilterEvent(&xev, None)) ProcessXEvent(xev);
        }
#ifdef JALIUM_HAS_WAYLAND
        if (g_waylandDisplay)
        {
            if (wl_display_dispatch_pending(g_waylandDisplay) < 0) break;
            (void)ProcessPendingWaylandDrop();
            (void)DispatchPendingWaylandPaints();
            wl_display_flush(g_waylandDisplay);
        }
#endif

        if (g_quitRequested.load(std::memory_order_acquire))
            break;

        // A global wake, dispatcher wake, timer expiry, or X11 connection data
        // all make this wait return. No polling timeout is needed.
        (void)ProcessEpollEvents(-1);
    }

    return g_exitCode.load(std::memory_order_acquire);
}

int32_t jalium_platform_poll_events(void)
{
    int32_t count = 0;
    if (g_windowSystem == LinuxWindowSystem::Disabled || g_epollFd < 0) return count;

    // Drive native callback sources even when an embedder owns the outer loop.
    count += ProcessEpollEvents(0);
    while (g_display && XPending(g_display))
    {
        XEvent xev;
        XNextEvent(g_display, &xev);
        if (!XFilterEvent(&xev, None)) ProcessXEvent(xev);
        count++;
    }
#ifdef JALIUM_HAS_WAYLAND
    if (g_waylandDisplay)
    {
        const int dispatched = wl_display_dispatch_pending(g_waylandDisplay);
        if (dispatched > 0) count += dispatched;
        if (ProcessPendingWaylandDrop()) ++count;
        count += DispatchPendingWaylandPaints();
        wl_display_flush(g_waylandDisplay);
    }
#endif
    return count;
}

void jalium_platform_quit(int32_t exitCode)
{
    g_exitCode = exitCode;
    g_quitRequested = true;

    // Wake the event loop
    if (g_wakeEventFd >= 0)
        (void)SignalEventFd(g_wakeEventFd);
}

// ============================================================================
// Dispatcher (eventfd)
// ============================================================================

JaliumResult jalium_dispatcher_create(JaliumDispatcher** outDispatcher)
{
    if (!outDispatcher) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outDispatcher = nullptr;
    if (g_epollFd < 0) return JALIUM_ERROR_INVALID_STATE;

    auto disp = new JaliumDispatcher();
    const int eventFd = eventfd(0, EFD_NONBLOCK | EFD_CLOEXEC);
    disp->eventFd.store(eventFd, std::memory_order_release);
    if (eventFd < 0)
    {
        delete disp;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    {
        std::lock_guard<std::mutex> lock(g_eventSourceMutex);
        if (!AddEpollFd(eventFd))
        {
            close(eventFd);
            delete disp;
            return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
        }
        g_dispatchersByFd[eventFd] = disp;
        g_allDispatchers.insert(disp);
    }

    *outDispatcher = disp;
    return JALIUM_OK;
}

void jalium_dispatcher_destroy(JaliumDispatcher* dispatcher)
{
    if (!dispatcher || dispatcher->destroyed.exchange(true, std::memory_order_acq_rel))
        return;

    {
        // Wait for an in-flight external callback. recursive_mutex also permits
        // a callback to destroy its own dispatcher without deadlocking.
        std::lock_guard<std::recursive_mutex> callbackLock(dispatcher->callbackMutex);
        dispatcher->callback.store(nullptr, std::memory_order_release);
        dispatcher->userData.store(nullptr, std::memory_order_release);
    }

    int fd;
    {
        std::lock_guard<std::mutex> lock(g_eventSourceMutex);
        fd = dispatcher->eventFd.exchange(-1, std::memory_order_acq_rel);
        const auto iterator = g_dispatchersByFd.find(fd);
        if (iterator != g_dispatchersByFd.end() && iterator->second == dispatcher)
            g_dispatchersByFd.erase(iterator);
        g_allDispatchers.erase(dispatcher);
        RemoveEpollFd(fd);
    }
    if (fd >= 0) close(fd);
    ReleaseDispatcher(dispatcher);
}

void jalium_dispatcher_wake(JaliumDispatcher* dispatcher)
{
    if (!dispatcher) return;
    std::lock_guard<std::mutex> lock(g_eventSourceMutex);
    if (dispatcher->destroyed.load(std::memory_order_acquire)) return;
    (void)SignalEventFd(dispatcher->eventFd.load(std::memory_order_acquire));
}

void jalium_dispatcher_set_callback(JaliumDispatcher* dispatcher,
                                     JaliumDispatcherCallback callback, void* userData)
{
    if (!dispatcher) return;
    std::lock_guard<std::recursive_mutex> callbackLock(dispatcher->callbackMutex);
    if (dispatcher->destroyed.load(std::memory_order_acquire)) return;
    if (callback)
    {
        dispatcher->userData.store(userData, std::memory_order_release);
        dispatcher->callback.store(callback, std::memory_order_release);
    }
    else
    {
        dispatcher->callback.store(nullptr, std::memory_order_release);
        dispatcher->userData.store(userData, std::memory_order_release);
    }
}

// ============================================================================
// High-Resolution Timer (timerfd)
// ============================================================================

JaliumResult jalium_timer_create(JaliumTimer** outTimer)
{
    if (!outTimer) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outTimer = nullptr;

    auto timer = new JaliumTimer();
    const int timerFd = timerfd_create(CLOCK_MONOTONIC, TFD_NONBLOCK | TFD_CLOEXEC);
    timer->timerFd.store(timerFd, std::memory_order_release);
    if (timerFd < 0)
    {
        delete timer;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    {
        std::lock_guard<std::mutex> lock(g_eventSourceMutex);
        g_allTimers.insert(timer);
    }

    *outTimer = timer;
    return JALIUM_OK;
}

void jalium_timer_destroy(JaliumTimer* timer)
{
    if (!timer || timer->destroyed.exchange(true, std::memory_order_acq_rel)) return;

    {
        std::lock_guard<std::recursive_mutex> callbackLock(timer->callbackMutex);
        timer->callback.store(nullptr, std::memory_order_release);
        timer->userData.store(nullptr, std::memory_order_release);
    }

    UnregisterTimerFromEpoll(timer);
    const int fd = timer->timerFd.exchange(-1, std::memory_order_acq_rel);
    {
        std::lock_guard<std::mutex> lock(g_eventSourceMutex);
        g_allTimers.erase(timer);
    }
    if (fd >= 0) close(fd);
    ReleaseTimer(timer);
}

void jalium_timer_arm(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer || intervalMicroseconds <= 0) return;
    std::lock_guard<std::mutex> lock(g_eventSourceMutex);
    if (timer->destroyed.load(std::memory_order_acquire)) return;
    const int fd = timer->timerFd.load(std::memory_order_acquire);
    if (fd < 0) return;

    struct itimerspec its{};
    its.it_value.tv_sec = intervalMicroseconds / 1000000;
    its.it_value.tv_nsec = (intervalMicroseconds % 1000000) * 1000;
    // it_interval = 0 → one-shot
    (void)timerfd_settime(fd, 0, &its, nullptr);
}

void jalium_timer_arm_repeating(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer || intervalMicroseconds <= 0) return;
    std::lock_guard<std::mutex> lock(g_eventSourceMutex);
    if (timer->destroyed.load(std::memory_order_acquire)) return;
    const int fd = timer->timerFd.load(std::memory_order_acquire);
    if (fd < 0) return;

    struct itimerspec its{};
    its.it_value.tv_sec = intervalMicroseconds / 1000000;
    its.it_value.tv_nsec = (intervalMicroseconds % 1000000) * 1000;
    its.it_interval = its.it_value; // Repeating
    (void)timerfd_settime(fd, 0, &its, nullptr);
}

void jalium_timer_disarm(JaliumTimer* timer)
{
    if (!timer) return;
    std::lock_guard<std::mutex> lock(g_eventSourceMutex);
    if (timer->destroyed.load(std::memory_order_acquire)) return;
    const int fd = timer->timerFd.load(std::memory_order_acquire);
    if (fd < 0) return;

    struct itimerspec its{};
    (void)timerfd_settime(fd, 0, &its, nullptr);
}

void jalium_timer_set_callback(JaliumTimer* timer, JaliumTimerCallback callback, void* userData)
{
    if (!timer) return;
    {
        std::lock_guard<std::recursive_mutex> callbackLock(timer->callbackMutex);
        if (timer->destroyed.load(std::memory_order_acquire)) return;
        if (callback)
        {
            timer->userData.store(userData, std::memory_order_release);
            timer->callback.store(callback, std::memory_order_release);
        }
        else
        {
            timer->callback.store(nullptr, std::memory_order_release);
            timer->userData.store(userData, std::memory_order_release);
        }
    }
    if (callback)
        (void)RegisterTimerWithEpoll(timer);
    else
        UnregisterTimerFromEpoll(timer);
}

int32_t jalium_timer_wait(JaliumTimer* timer, uint32_t timeoutMs)
{
    if (!timer) return 0;
    const int fd = timer->timerFd.load(std::memory_order_acquire);
    if (fd < 0) return 0;

    struct pollfd pfd{};
    pfd.fd = fd;
    pfd.events = POLLIN;

    int timeout = (timeoutMs == 0) ? -1 : static_cast<int>(timeoutMs);
    int ret;
    do
    {
        ret = poll(&pfd, 1, timeout);
    }
    while (ret < 0 && errno == EINTR);
    if (ret > 0 && (pfd.revents & POLLIN))
        return DrainCounterFd(fd) != 0 ? 1 : 0;
    return 0;
}

// ============================================================================
// DPI and Display
// ============================================================================

float jalium_platform_get_system_dpi_scale(void)
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland) return 1.0f;
#endif
    return DetectDpiScale();
}

float jalium_window_get_dpi_scale(JaliumPlatformWindow* window)
{
    if (!window) return 1.0f;
    return window->dpiScale;
}

int32_t jalium_window_get_monitor_refresh_rate(JaliumPlatformWindow* window)
{
#ifdef JALIUM_HAS_XRANDR
    if (g_display)
    {
        XRRScreenConfiguration* conf = XRRGetScreenInfo(g_display, g_rootWindow);
        if (conf)
        {
            short rate = XRRConfigCurrentRate(conf);
            XRRFreeScreenConfigInfo(conf);
            if (rate > 0) return rate;
        }
    }
#endif
    return 60;
}

// ============================================================================
// Input State Polling
// ============================================================================

int16_t jalium_input_get_key_state(int32_t jaliumVirtualKey)
{
    if (jaliumVirtualKey < 0 ||
        jaliumVirtualKey >= static_cast<int32_t>(g_keyStates.size())) return 0;
    if (g_windowSystem == LinuxWindowSystem::XServer && g_display &&
        (jaliumVirtualKey == 0x14 || jaliumVirtualKey == 0x90))
    {
        XkbStateRec state{};
        if (XkbGetState(g_display, XkbUseCoreKbd, &state) == Success)
        {
            if (jaliumVirtualKey == 0x14) SetToggleState(0x14, (state.locked_mods & LockMask) != 0);
            else SetToggleState(0x90, (state.locked_mods & Mod2Mask) != 0);
        }
    }
    const uint8_t state = g_keyStates[jaliumVirtualKey].load(std::memory_order_acquire);
    return static_cast<int16_t>(((state & 0x80u) ? 0x8000u : 0u) |
                                ((state & 0x01u) ? 0x0001u : 0u));
}

void jalium_input_get_cursor_pos(float* x, float* y)
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        if (x) *x = g_pointerX;
        if (y) *y = g_pointerY;
        return;
    }
#endif
    if (!g_display) { if (x) *x = 0; if (y) *y = 0; return; }

    Window root, child;
    int rootX, rootY, winX, winY;
    unsigned int mask;
    XQueryPointer(g_display, g_rootWindow, &root, &child, &rootX, &rootY, &winX, &winY, &mask);
    if (x) *x = static_cast<float>(rootX);
    if (y) *y = static_cast<float>(rootY);
}

// ============================================================================
// Clipboard (X11 Selections)
// ============================================================================

static void EnsureClipboardAtoms()
{
    if (!g_clipboardAtom && g_display)
    {
        g_clipboardAtom = XInternAtom(g_display, "CLIPBOARD", False);
        g_utf8StringAtom = XInternAtom(g_display, "UTF8_STRING", False);
        g_targetsAtom = XInternAtom(g_display, "TARGETS", False);
        g_jaliumClipProp = XInternAtom(g_display, "JALIUM_CLIPBOARD", False);
        g_textPlainUtf8Atom = XInternAtom(g_display, "text/plain;charset=utf-8", False);
        g_textPlainAtom = XInternAtom(g_display, "text/plain", False);
        g_incrAtom = XInternAtom(g_display, "INCR", False);
    }
}

static std::string Utf8ToLatin1(const std::string& text)
{
    std::string result;
    for (uint32_t codePoint : Utf8ToUtf32(text.data(), text.size()))
        result.push_back(codePoint <= 0xFFu ? static_cast<char>(codePoint) : '?');
    return result;
}

static std::string Latin1ToUtf8(const unsigned char* data, size_t length)
{
    std::u32string codePoints;
    codePoints.reserve(length);
    for (size_t index = 0; index < length; ++index)
        codePoints.push_back(static_cast<char32_t>(data[index]));
    return Utf32ToUtf8(codePoints);
}

static bool IsX11TextTarget(Atom target)
{
    return target == g_utf8StringAtom || target == g_textPlainUtf8Atom ||
           target == g_textPlainAtom || target == XA_STRING;
}

static bool ProcessClipboardXEvent(XEvent& event)
{
    if (!g_display || !g_clipboardWindow) return false;
    if (event.type == SelectionRequest)
    {
        const XSelectionRequestEvent& request = event.xselectionrequest;
        if (request.owner != g_clipboardWindow || request.selection != g_clipboardAtom)
            return false;

        XSelectionEvent response{};
        response.type = SelectionNotify;
        response.display = request.display;
        response.requestor = request.requestor;
        response.selection = request.selection;
        response.target = request.target;
        response.time = request.time;
        response.property = None;
        const Atom property = request.property != None ? request.property : request.target;

        if (request.target == g_targetsAtom)
        {
            const Atom targets[] = {
                g_targetsAtom, g_utf8StringAtom, g_textPlainUtf8Atom,
                g_textPlainAtom, XA_STRING
            };
            XChangeProperty(g_display, request.requestor, property, XA_ATOM, 32,
                            PropModeReplace,
                            reinterpret_cast<const unsigned char*>(targets),
                            static_cast<int>(std::size(targets)));
            response.property = property;
        }
        else if (IsX11TextTarget(request.target))
        {
            std::string snapshot;
            {
                std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
                snapshot = request.target == XA_STRING
                    ? Utf8ToLatin1(g_clipboardUtf8) : g_clipboardUtf8;
            }
            XChangeProperty(g_display, request.requestor, property, request.target, 8,
                            PropModeReplace,
                            reinterpret_cast<const unsigned char*>(snapshot.data()),
                            static_cast<int>(snapshot.size()));
            response.property = property;
        }

        XSendEvent(g_display, request.requestor, False, NoEventMask,
                   reinterpret_cast<XEvent*>(&response));
        XFlush(g_display);
        return true;
    }
    if (event.type == SelectionClear &&
        event.xselectionclear.window == g_clipboardWindow &&
        event.xselectionclear.selection == g_clipboardAtom)
    {
        return true;
    }
    return (event.type == SelectionNotify &&
            event.xselection.requestor == g_clipboardWindow) ||
           (event.type == PropertyNotify &&
            event.xproperty.window == g_clipboardWindow);
}

static bool WaitForClipboardEvent(int type, XEvent& result, uint32_t timeoutMilliseconds)
{
    const auto deadline = std::chrono::steady_clock::now() +
        std::chrono::milliseconds(timeoutMilliseconds);
    while (std::chrono::steady_clock::now() < deadline)
    {
        if (XCheckTypedWindowEvent(g_display, g_clipboardWindow, type, &result))
            return true;

        // Continue serving our own selection while synchronously waiting on a
        // different owner. XCheckTypedWindowEvent only removes matching events,
        // so application window input remains queued for the normal loop.
        XEvent request{};
        while (XCheckTypedWindowEvent(
            g_display, g_clipboardWindow, SelectionRequest, &request))
            (void)ProcessClipboardXEvent(request);
        while (XCheckTypedWindowEvent(
            g_display, g_clipboardWindow, SelectionClear, &request))
            (void)ProcessClipboardXEvent(request);

        const auto remaining = std::chrono::duration_cast<std::chrono::milliseconds>(
            deadline - std::chrono::steady_clock::now()).count();
        if (remaining <= 0) break;
        struct pollfd descriptor{};
        descriptor.fd = ConnectionNumber(g_display);
        descriptor.events = POLLIN;
        int pollResult;
        do
        {
            pollResult = poll(&descriptor, 1, static_cast<int>(std::min<int64_t>(remaining, 25)));
        }
        while (pollResult < 0 && errno == EINTR);
        if (pollResult > 0) (void)XEventsQueued(g_display, QueuedAfterReading);
    }
    return false;
}

static bool ReadX11Property(Atom& actualType, int& actualFormat,
                            std::vector<unsigned char>& bytes)
{
    unsigned long itemCount = 0;
    unsigned long remaining = 0;
    unsigned char* value = nullptr;
    const int status = XGetWindowProperty(
        g_display, g_clipboardWindow, g_jaliumClipProp, 0, 0x1fffffff,
        True, AnyPropertyType, &actualType, &actualFormat,
        &itemCount, &remaining, &value);
    if (status != Success)
    {
        if (value) XFree(value);
        return false;
    }

    if (actualType == g_incrAtom)
    {
        if (value) XFree(value);
        bytes.clear();
        for (;;)
        {
            XEvent propertyEvent{};
            if (!WaitForClipboardEvent(PropertyNotify, propertyEvent, 2000)) return false;
            if (propertyEvent.xproperty.atom != g_jaliumClipProp ||
                propertyEvent.xproperty.state != PropertyNewValue) continue;
            Atom chunkType = None;
            int chunkFormat = 0;
            unsigned long chunkItems = 0;
            unsigned long chunkRemaining = 0;
            unsigned char* chunk = nullptr;
            if (XGetWindowProperty(
                    g_display, g_clipboardWindow, g_jaliumClipProp, 0, 0x1fffffff,
                    True, AnyPropertyType, &chunkType, &chunkFormat,
                    &chunkItems, &chunkRemaining, &chunk) != Success)
            {
                if (chunk) XFree(chunk);
                return false;
            }
            if (chunkItems == 0)
            {
                if (chunk) XFree(chunk);
                actualType = chunkType;
                actualFormat = chunkFormat;
                return true;
            }
            if (chunkFormat != 8)
            {
                if (chunk) XFree(chunk);
                return false;
            }
            bytes.insert(bytes.end(), chunk, chunk + chunkItems);
            actualType = chunkType;
            actualFormat = chunkFormat;
            XFree(chunk);
        }
    }

    const size_t bytesPerItem = actualFormat == 32 ? sizeof(unsigned long) :
                                (actualFormat == 16 ? 2u : 1u);
    if (value && itemCount)
        bytes.assign(value, value + itemCount * bytesPerItem);
    else
        bytes.clear();
    if (value) XFree(value);
    return true;
}

static bool RequestX11Selection(Atom target, Atom& actualType, int& actualFormat,
                                std::vector<unsigned char>& bytes)
{
    XDeleteProperty(g_display, g_clipboardWindow, g_jaliumClipProp);
    XConvertSelection(g_display, g_clipboardAtom, target, g_jaliumClipProp,
                      g_clipboardWindow, CurrentTime);
    XFlush(g_display);
    XEvent notification{};
    if (!WaitForClipboardEvent(SelectionNotify, notification, 2000) ||
        notification.xselection.property == None)
        return false;
    return ReadX11Property(actualType, actualFormat, bytes);
}

#ifdef JALIUM_HAS_WAYLAND
static const char* SelectWaylandTextMime(const WaylandDataOfferState* offer)
{
    if (!offer) return nullptr;
    constexpr const char* preferred[] = {
        "text/plain;charset=utf-8", "UTF8_STRING", "text/plain"
    };
    for (const char* candidate : preferred)
        if (std::find(offer->mimeTypes.begin(), offer->mimeTypes.end(), candidate) !=
            offer->mimeTypes.end()) return candidate;
    return nullptr;
}

static bool ReadWaylandSelection(std::string& text)
{
    const char* mimeType = SelectWaylandTextMime(g_waylandSelectionOffer);
    if (!mimeType || !g_waylandSelectionOffer || !g_waylandSelectionOffer->offer)
        return false;
    int descriptors[2] = {-1, -1};
    if (pipe2(descriptors, O_CLOEXEC | O_NONBLOCK) != 0) return false;
    wl_data_offer_receive(g_waylandSelectionOffer->offer, mimeType, descriptors[1]);
    close(descriptors[1]);
    descriptors[1] = -1;
    if (wl_display_flush(g_waylandDisplay) < 0)
    {
        close(descriptors[0]);
        return false;
    }

    text.clear();
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    bool complete = false;
    while (std::chrono::steady_clock::now() < deadline && !complete)
    {
        struct pollfd descriptor{};
        descriptor.fd = descriptors[0];
        descriptor.events = POLLIN | POLLHUP;
        const auto remaining = std::chrono::duration_cast<std::chrono::milliseconds>(
            deadline - std::chrono::steady_clock::now()).count();
        int result;
        do { result = poll(&descriptor, 1, static_cast<int>(std::max<int64_t>(1, remaining))); }
        while (result < 0 && errno == EINTR);
        if (result <= 0) break;
        for (;;)
        {
            char buffer[8192];
            const ssize_t count = read(descriptors[0], buffer, sizeof(buffer));
            if (count > 0) text.append(buffer, static_cast<size_t>(count));
            else if (count == 0) { complete = true; break; }
            else if (errno == EINTR) continue;
            else if (errno == EAGAIN || errno == EWOULDBLOCK) break;
            else { close(descriptors[0]); return false; }
        }
        if ((descriptor.revents & POLLHUP) != 0) complete = true;
    }
    close(descriptors[0]);
    return complete;
}
#endif

JaliumResult jalium_clipboard_get_text(JaliumUtf16Char** outText)
{
    if (!outText) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outText = nullptr;

#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        std::string text;
        if (g_waylandClipboardSource)
        {
            std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
            text = g_clipboardUtf8;
        }
        else if (!ReadWaylandSelection(text))
            return JALIUM_OK;
        *outText = Utf8ToUtf16Allocated(text);
        return *outText ? JALIUM_OK : JALIUM_ERROR_OUT_OF_MEMORY;
    }
#endif
    if (!g_display || !g_clipboardWindow) return JALIUM_ERROR_INVALID_STATE;
    std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
    EnsureClipboardAtoms();
    const Window owner = XGetSelectionOwner(g_display, g_clipboardAtom);
    if (owner == None) return JALIUM_OK;
    if (owner == g_clipboardWindow)
    {
        *outText = Utf8ToUtf16Allocated(g_clipboardUtf8);
        return *outText ? JALIUM_OK : JALIUM_ERROR_OUT_OF_MEMORY;
    }

    Atom selectedTarget = g_utf8StringAtom;
    Atom actualType = None;
    int actualFormat = 0;
    std::vector<unsigned char> bytes;
    if (RequestX11Selection(g_targetsAtom, actualType, actualFormat, bytes) &&
        actualType == XA_ATOM && actualFormat == 32)
    {
        const auto* targets = reinterpret_cast<const unsigned long*>(bytes.data());
        const size_t count = bytes.size() / sizeof(unsigned long);
        auto contains = [targets, count](Atom target)
        {
            return std::find(targets, targets + count, static_cast<unsigned long>(target)) !=
                   targets + count;
        };
        if (contains(g_utf8StringAtom)) selectedTarget = g_utf8StringAtom;
        else if (contains(g_textPlainUtf8Atom)) selectedTarget = g_textPlainUtf8Atom;
        else if (contains(g_textPlainAtom)) selectedTarget = g_textPlainAtom;
        else if (contains(XA_STRING)) selectedTarget = XA_STRING;
        else return JALIUM_OK;
    }

    bytes.clear();
    if (!RequestX11Selection(selectedTarget, actualType, actualFormat, bytes) ||
        actualFormat != 8) return JALIUM_OK;
    const std::string text = selectedTarget == XA_STRING
        ? Latin1ToUtf8(bytes.data(), bytes.size())
        : std::string(reinterpret_cast<const char*>(bytes.data()), bytes.size());
    *outText = Utf8ToUtf16Allocated(text);
    return *outText ? JALIUM_OK : JALIUM_ERROR_OUT_OF_MEMORY;
}

JaliumResult jalium_clipboard_set_text(const JaliumUtf16Char* text)
{
    if (!text) return JALIUM_ERROR_INVALID_ARGUMENT;
    {
        std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
        g_clipboardUtf8 = Utf16ToUtf8(text);
    }

#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        if (!g_waylandDataDeviceManager || !g_waylandDataDevice)
            return JALIUM_ERROR_NOT_SUPPORTED;
        if (g_waylandInputSerial == 0)
            return JALIUM_ERROR_INVALID_STATE;
        if (g_waylandClipboardSource)
        {
            wl_data_source_destroy(g_waylandClipboardSource);
            g_waylandClipboardSource = nullptr;
        }
        g_waylandClipboardSource = wl_data_device_manager_create_data_source(
            g_waylandDataDeviceManager);
        if (!g_waylandClipboardSource) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
        wl_data_source_add_listener(
            g_waylandClipboardSource, &g_dataSourceListener, nullptr);
        wl_data_source_offer(g_waylandClipboardSource, "text/plain;charset=utf-8");
        wl_data_source_offer(g_waylandClipboardSource, "text/plain");
        wl_data_source_offer(g_waylandClipboardSource, "UTF8_STRING");
        wl_data_device_set_selection(g_waylandDataDevice, g_waylandClipboardSource,
                                     g_waylandInputSerial);
        if (wl_display_flush(g_waylandDisplay) < 0) return JALIUM_ERROR_UNKNOWN;
        return JALIUM_OK;
    }
#endif
    if (!g_display || !g_clipboardWindow) return JALIUM_ERROR_INVALID_STATE;
    std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
    EnsureClipboardAtoms();
    XSetSelectionOwner(g_display, g_clipboardAtom, g_clipboardWindow, CurrentTime);
    XFlush(g_display);
    return XGetSelectionOwner(g_display, g_clipboardAtom) == g_clipboardWindow
        ? JALIUM_OK : JALIUM_ERROR_UNKNOWN;
}

// ============================================================================
// Drag Source API (X11 XDND / Wayland data device)
// ============================================================================

static std::vector<OwnedDragItem> CopyDragItems(
    const JaliumDragDataItem* items, uint32_t itemCount)
{
    std::vector<OwnedDragItem> result;
    result.reserve(itemCount);
    for (uint32_t index = 0; index < itemCount; ++index)
    {
        if (!items[index].mimeType ||
            (!items[index].data && items[index].dataSize != 0))
            continue;
        OwnedDragItem item;
        item.mimeType = items[index].mimeType;
        if (items[index].dataSize != 0)
            item.bytes.assign(items[index].data,
                              items[index].data + items[index].dataSize);
        if (g_display)
            item.x11Atom = XInternAtom(g_display, item.mimeType.c_str(), False);
        result.push_back(std::move(item));
    }
    return result;
}

static Window FindXdndTargetAtPointer(int& rootX, int& rootY, unsigned int& mask)
{
    rootX = rootY = 0;
    mask = 0;
    Window root = 0;
    Window child = 0;
    int localX = 0;
    int localY = 0;
    if (!XQueryPointer(g_display, g_rootWindow, &root, &child,
                       &rootX, &rootY, &localX, &localY, &mask) || child == None)
        return 0;

    Window candidate = child;
    for (int depth = 0; candidate != None && depth < 32; ++depth)
    {
        Atom actualType = None;
        int actualFormat = 0;
        unsigned long itemCount = 0;
        unsigned long remaining = 0;
        unsigned char* value = nullptr;
        const int status = XGetWindowProperty(
            g_display, candidate, g_xdndAwareAtom, 0, 1, False, XA_ATOM,
            &actualType, &actualFormat, &itemCount, &remaining, &value);
        const bool aware = status == Success && actualType == XA_ATOM &&
                           actualFormat == 32 && itemCount != 0;
        if (value) XFree(value);
        if (aware) return candidate;

        Window nextRoot = 0;
        Window nextChild = 0;
        if (!XQueryPointer(g_display, candidate, &nextRoot, &nextChild,
                           &rootX, &rootY, &localX, &localY, &mask) ||
            nextChild == None || nextChild == candidate)
            break;
        candidate = nextChild;
    }
    return 0;
}

static Atom ChooseXdndSourceAction(uint32_t allowedEffects, unsigned int mask)
{
    if ((mask & ControlMask) && (allowedEffects & JALIUM_DRAG_EFFECT_COPY))
        return g_xdndActionCopyAtom;
    if ((mask & ShiftMask) && (allowedEffects & JALIUM_DRAG_EFFECT_MOVE))
        return g_xdndActionMoveAtom;
    if ((mask & Mod1Mask) && (allowedEffects & JALIUM_DRAG_EFFECT_LINK))
        return g_xdndActionLinkAtom;
    if (allowedEffects & JALIUM_DRAG_EFFECT_COPY) return g_xdndActionCopyAtom;
    if (allowedEffects & JALIUM_DRAG_EFFECT_MOVE) return g_xdndActionMoveAtom;
    if (allowedEffects & JALIUM_DRAG_EFFECT_LINK) return g_xdndActionLinkAtom;
    return None;
}

static void SendXdndEnter(const X11DragSourceState& state, Window target)
{
    const bool hasTypeList = state.items.size() > 3;
    unsigned long flags = 5ul << 24;
    if (hasTypeList) flags |= 1ul;
    const Atom type0 = state.items.size() > 0 ? state.items[0].x11Atom : None;
    const Atom type1 = state.items.size() > 1 ? state.items[1].x11Atom : None;
    const Atom type2 = state.items.size() > 2 ? state.items[2].x11Atom : None;
    SendXdndClientMessage(
        target, g_xdndEnterAtom,
        static_cast<long>(state.window->xwindow), static_cast<long>(flags),
        static_cast<long>(type0), static_cast<long>(type1), static_cast<long>(type2));
}

static void PumpX11DragEvents()
{
    while (XPending(g_display))
    {
        XEvent event{};
        XNextEvent(g_display, &event);
        if (!XFilterEvent(&event, None)) ProcessXEvent(event);
    }
}

static JaliumResult BeginX11Drag(
    JaliumPlatformWindow* window, std::vector<OwnedDragItem> items,
    uint32_t allowedEffects, uint32_t* performedEffect)
{
    if (!g_display || !window || !window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;
    if (g_x11DragSource)
        return JALIUM_ERROR_BUSY;

    EnsureXdndAtoms();
    X11DragSourceState state;
    state.window = window;
    state.items = std::move(items);
    state.allowedEffects = allowedEffects;
    for (OwnedDragItem& item : state.items)
        item.x11Atom = XInternAtom(g_display, item.mimeType.c_str(), False);

    std::vector<Atom> typeAtoms;
    typeAtoms.reserve(state.items.size());
    for (const OwnedDragItem& item : state.items) typeAtoms.push_back(item.x11Atom);
    XChangeProperty(g_display, window->xwindow, g_xdndTypeListAtom, XA_ATOM, 32,
                    PropModeReplace,
                    reinterpret_cast<const unsigned char*>(typeAtoms.data()),
                    static_cast<int>(typeAtoms.size()));
    std::vector<Atom> actions;
    if (allowedEffects & JALIUM_DRAG_EFFECT_COPY) actions.push_back(g_xdndActionCopyAtom);
    if (allowedEffects & JALIUM_DRAG_EFFECT_MOVE) actions.push_back(g_xdndActionMoveAtom);
    if (allowedEffects & JALIUM_DRAG_EFFECT_LINK) actions.push_back(g_xdndActionLinkAtom);
    XChangeProperty(g_display, window->xwindow, g_xdndActionListAtom, XA_ATOM, 32,
                    PropModeReplace,
                    reinterpret_cast<const unsigned char*>(actions.data()),
                    static_cast<int>(actions.size()));
    XSetSelectionOwner(g_display, g_xdndSelectionAtom, window->xwindow, CurrentTime);
    if (XGetSelectionOwner(g_display, g_xdndSelectionAtom) != window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;

    const int grab = XGrabPointer(
        g_display, window->xwindow, False,
        ButtonReleaseMask | PointerMotionMask,
        GrabModeAsync, GrabModeAsync, None, None, CurrentTime);
    if (grab != GrabSuccess)
    {
        XSetSelectionOwner(g_display, g_xdndSelectionAtom, None, CurrentTime);
        return JALIUM_ERROR_BUSY;
    }

    g_x11DragSource = &state;
    bool released = false;
    while (!released && !state.finished)
    {
        PumpX11DragEvents();
        int rootX = 0;
        int rootY = 0;
        unsigned int mask = 0;
        const Window target = FindXdndTargetAtPointer(rootX, rootY, mask);
        if (target != state.target)
        {
            if (state.target)
                SendXdndClientMessage(state.target, g_xdndLeaveAtom,
                                      static_cast<long>(window->xwindow));
            state.target = target;
            state.targetAccepted = false;
            state.requestedAction = None;
            if (state.target) SendXdndEnter(state, state.target);
        }

        const Atom action = ChooseXdndSourceAction(allowedEffects, mask);
        if (state.target)
        {
            const unsigned long packed =
                (static_cast<unsigned long>(rootX) & 0xfffful) << 16 |
                (static_cast<unsigned long>(rootY) & 0xfffful);
            SendXdndClientMessage(
                state.target, g_xdndPositionAtom,
                static_cast<long>(window->xwindow), 0,
                static_cast<long>(packed), CurrentTime, static_cast<long>(action));
        }

        // A target on this same X connection answers through a queued
        // XdndStatus. Drain it before examining the release state so an
        // immediate/programmatic in-process drag can still complete.
        XSync(g_display, False);
        PumpX11DragEvents();

        released = (mask & Button1Mask) == 0;
        if (!released)
        {
            struct pollfd descriptor{};
            descriptor.fd = ConnectionNumber(g_display);
            descriptor.events = POLLIN;
            int result;
            do { result = poll(&descriptor, 1, 10); }
            while (result < 0 && errno == EINTR);
            if (result > 0) (void)XEventsQueued(g_display, QueuedAfterReading);
        }
    }

    XUngrabPointer(g_display, CurrentTime);
    // XdndStatus is an asynchronous ClientMessage. On an immediate release
    // (including the deterministic in-process test path) it can still be in
    // the X server after the last Position was flushed, so give the target a
    // short bounded window to answer before deciding whether to Drop/Leave.
    if (!state.finished && state.target && !state.targetAccepted)
    {
        const auto statusDeadline =
            std::chrono::steady_clock::now() + std::chrono::milliseconds(250);
        while (!state.targetAccepted && std::chrono::steady_clock::now() < statusDeadline)
        {
            PumpX11DragEvents();
            if (state.targetAccepted) break;
            struct pollfd descriptor{};
            descriptor.fd = ConnectionNumber(g_display);
            descriptor.events = POLLIN;
            int result;
            do { result = poll(&descriptor, 1, 5); }
            while (result < 0 && errno == EINTR);
            if (result > 0) (void)XEventsQueued(g_display, QueuedAfterReading);
        }
    }
    if (!state.finished && state.target && state.targetAccepted)
    {
        state.dropSent = true;
        SendXdndClientMessage(
            state.target, g_xdndDropAtom,
            static_cast<long>(window->xwindow), 0, CurrentTime);
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        while (!state.finished && std::chrono::steady_clock::now() < deadline)
        {
            PumpX11DragEvents();
            if (state.finished) break;
            struct pollfd descriptor{};
            descriptor.fd = ConnectionNumber(g_display);
            descriptor.events = POLLIN;
            int result;
            do { result = poll(&descriptor, 1, 10); }
            while (result < 0 && errno == EINTR);
            if (result > 0) (void)XEventsQueued(g_display, QueuedAfterReading);
        }
    }
    else if (!state.finished && state.target)
    {
        SendXdndClientMessage(state.target, g_xdndLeaveAtom,
                              static_cast<long>(window->xwindow));
    }

    g_x11DragSource = nullptr;
    XSetSelectionOwner(g_display, g_xdndSelectionAtom, None, CurrentTime);
    XDeleteProperty(g_display, window->xwindow, g_xdndTypeListAtom);
    XDeleteProperty(g_display, window->xwindow, g_xdndActionListAtom);
    XFlush(g_display);
    *performedEffect = state.performedEffect;
    return JALIUM_OK;
}

#ifdef JALIUM_HAS_WAYLAND
static JaliumResult BeginWaylandDrag(
    JaliumPlatformWindow* window, std::vector<OwnedDragItem> items,
    uint32_t allowedEffects, uint32_t* performedEffect)
{
    if (!g_waylandDisplay || !g_waylandDataDeviceManager ||
        !g_waylandDataDevice || !window || !window->waylandSurface ||
        g_waylandPointerSerial == 0)
        return JALIUM_ERROR_INVALID_STATE;
    if (g_waylandDragSource)
        return JALIUM_ERROR_BUSY;

    WaylandDragSourceState state;
    state.window = window;
    state.items = std::move(items);
    state.allowedEffects = allowedEffects;
    state.source = wl_data_device_manager_create_data_source(
        g_waylandDataDeviceManager);
    if (!state.source) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    wl_data_source_add_listener(state.source, &g_dragDataSourceListener, &state);
    for (const OwnedDragItem& item : state.items)
        wl_data_source_offer(state.source, item.mimeType.c_str());
    if (wl_proxy_get_version(reinterpret_cast<wl_proxy*>(state.source)) >= 3)
        wl_data_source_set_actions(state.source, WaylandEffectsToActions(allowedEffects));

    g_waylandDragSource = &state;
    wl_data_device_start_drag(
        g_waylandDataDevice, state.source, window->waylandSurface,
        nullptr, g_waylandPointerSerial);
    if (wl_display_flush(g_waylandDisplay) < 0)
    {
        g_waylandDragSource = nullptr;
        wl_data_source_destroy(state.source);
        return JALIUM_ERROR_UNKNOWN;
    }

    // jalium_drag_begin is intentionally synchronous (matching OLE/XDND).
    // Drive the default queue until the compositor reports dnd_finished or
    // cancelled. This also supports a self-drop: ProcessPendingWaylandDrop
    // receives the offer, dispatches managed Drop, and calls offer.finish.
    while (!state.finished && !g_quitRequested.load(std::memory_order_acquire))
    {
        if (wl_display_dispatch_pending(g_waylandDisplay) < 0)
        {
            state.cancelled = true;
            break;
        }
        (void)ProcessPendingWaylandDrop();
        if (state.finished) break;
        wl_display_flush(g_waylandDisplay);

        struct pollfd descriptor{};
        descriptor.fd = g_waylandFd;
        descriptor.events = POLLIN | POLLERR | POLLHUP;
        int result;
        do { result = poll(&descriptor, 1, 20); }
        while (result < 0 && errno == EINTR);
        if (result < 0 || (descriptor.revents & (POLLERR | POLLHUP)))
        {
            state.cancelled = true;
            break;
        }
        if (descriptor.revents & POLLIN)
        {
            if (wl_display_dispatch(g_waylandDisplay) < 0)
            {
                state.cancelled = true;
                break;
            }
        }
    }

    (void)ProcessPendingWaylandDrop();
    g_waylandDragSource = nullptr;
    wl_data_source_destroy(state.source);
    state.source = nullptr;
    *performedEffect = state.cancelled
        ? JALIUM_DRAG_EFFECT_NONE : state.performedEffect;

    JaliumPlatformEvent finished{};
    finished.type = JALIUM_EVENT_DRAG_FINISHED;
    finished.window = window;
    finished.drag.sessionId = 0;
    finished.drag.allowedEffects = *performedEffect;
    window->DispatchEvent(finished);
    return state.cancelled ? JALIUM_ERROR_UNKNOWN : JALIUM_OK;
}
#endif

void jalium_drag_set_effect(
    JaliumPlatformWindow* window, uint64_t sessionId, uint32_t effect)
{
    if (!window || window->dragSessionId != sessionId) return;
    const uint32_t requested = effect &
        (JALIUM_DRAG_EFFECT_COPY | JALIUM_DRAG_EFFECT_MOVE | JALIUM_DRAG_EFFECT_LINK);
    window->dragSelectedEffect = requested & window->xdndAllowedEffects;
}

JaliumResult jalium_drag_begin(
    JaliumPlatformWindow* window,
    const JaliumDragDataItem* items,
    uint32_t itemCount,
    uint32_t allowedEffects,
    uint32_t* performedEffect)
{
    if (!window || !items || itemCount == 0 || !performedEffect)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    *performedEffect = JALIUM_DRAG_EFFECT_NONE;
    allowedEffects &= JALIUM_DRAG_EFFECT_COPY |
                      JALIUM_DRAG_EFFECT_MOVE |
                      JALIUM_DRAG_EFFECT_LINK;
    if (allowedEffects == JALIUM_DRAG_EFFECT_NONE)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    std::vector<OwnedDragItem> copied = CopyDragItems(items, itemCount);
    if (copied.empty()) return JALIUM_ERROR_INVALID_ARGUMENT;

#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
        return BeginWaylandDrag(
            window, std::move(copied), allowedEffects, performedEffect);
#endif
    if (g_windowSystem == LinuxWindowSystem::XServer)
        return BeginX11Drag(window, std::move(copied), allowedEffects, performedEffect);
    return JALIUM_ERROR_INVALID_STATE;
}

#endif // __linux__ && !__ANDROID__
