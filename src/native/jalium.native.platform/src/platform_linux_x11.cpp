#if defined(__linux__) && !defined(__ANDROID__)

#include "jalium_platform.h"

#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/Xatom.h>
#include <X11/keysym.h>
#include <X11/XKBlib.h>
#include <X11/Xresource.h>

#ifdef JALIUM_HAS_XCURSOR
#include <X11/Xcursor/Xcursor.h>
#endif

#ifdef JALIUM_HAS_XINPUT2
#include <X11/extensions/XInput2.h>
#endif

#ifdef JALIUM_HAS_WAYLAND
#include <wayland-client.h>
#include <wayland-cursor.h>
#include <xkbcommon/xkbcommon.h>
#include "xdg-shell-client-protocol.h"
#ifdef JALIUM_HAS_XDG_ACTIVATION_V1
#include "xdg-activation-v1-client-protocol.h"
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
#include "text-input-unstable-v3-client-protocol.h"
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
#include "text-input-unstable-v1-client-protocol.h"
#endif
#ifdef JALIUM_HAS_WAYLAND_TABLET_V2
#include "tablet-unstable-v2-client-protocol.h"
#endif
#ifdef JALIUM_HAS_XDG_FOREIGN_V2
#include "xdg-foreign-unstable-v2-client-protocol.h"
#endif
#ifdef JALIUM_HAS_XDG_DECORATION_V1
#include "xdg-decoration-unstable-v1-client-protocol.h"
#endif
#ifdef JALIUM_HAS_XDG_TOPLEVEL_ICON_V1
#include "xdg-toplevel-icon-v1-client-protocol.h"
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
#include <linux/input-event-codes.h>
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
#include <limits>
#include <new>

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

struct OwnedClipboardItem {
    std::string mimeType;
    std::vector<uint8_t> bytes;
    Atom x11Atom = None;
};

struct X11ClipboardIncrTransfer {
    Window requestor = 0;
    Atom property = None;
    Atom target = None;
    std::vector<uint8_t> bytes;
    size_t offset = 0;
};

static std::vector<OwnedClipboardItem> g_clipboardItems;
static std::vector<X11ClipboardIncrTransfer> g_clipboardIncrTransfers;
static std::recursive_mutex g_clipboardMutex;
static XIM          g_xim = nullptr;
static int          g_epollFd = -1;
static int          g_wakeEventFd = -1;   // eventfd for cross-thread wake
static std::atomic<bool> g_quitRequested{false};
static std::atomic<int32_t> g_exitCode{0};
static std::atomic<uint64_t> g_dragSessionCounter{1};

#ifdef JALIUM_HAS_XINPUT2
static int g_xinputOpcode = -1;
static bool g_xinput2Available = false;

struct XInputPenAxes {
    bool isPen = false;
    bool inRange = false;
    bool inContact = false;
    bool inverted = false;
    JaliumPlatformWindow* window = nullptr;
    int32_t toolType = JALIUM_POINTER_TOOL_UNKNOWN;
    uint32_t buttons = JALIUM_POINTER_BUTTON_NONE;
    float x = 0.0f;
    float y = 0.0f;
    int pressure = -1;
    int tiltX = -1;
    int tiltY = -1;
    int rotation = -1;
    double pressureMin = 0.0;
    double pressureMax = 1.0;
    double tiltXMin = -90.0;
    double tiltXMax = 90.0;
    double tiltYMin = -90.0;
    double tiltYMax = 90.0;
    double rotationMin = 0.0;
    double rotationMax = 360.0;
    float currentPressure = 0.0f;
    float currentTiltX = 0.0f;
    float currentTiltY = 0.0f;
    float currentRotation = 0.0f;
};
static std::unordered_map<int, XInputPenAxes> g_xinputPenAxes;

struct XInputScrollAxis {
    int number = -1;
    int scrollType = 0;
    double increment = 0.0;
    double previousValue = 0.0;
    bool hasPreviousValue = false;
};
static std::unordered_map<int, std::vector<XInputScrollAxis>> g_xinputScrollAxes;

struct XInputTouchContact {
    JaliumPlatformWindow* window = nullptr;
    uint32_t pointerId = 0;
    float x = 0;
    float y = 0;
    uint64_t order = 0;
    bool primary = false;
};
static std::unordered_map<uint64_t, XInputTouchContact> g_xinputTouchContacts;
static uint64_t g_xinputTouchOrder = 0;
#endif

#ifdef JALIUM_HAS_XRANDR
static int g_xrandrEventBase = -1;
static int g_xrandrErrorBase = -1;
static bool g_xrandrAvailable = false;
static bool g_xrandr13Available = false;
static bool g_xrandrMonitorObjectsAvailable = false;
#endif

#ifdef JALIUM_PLATFORM_TEST_HOOKS
static std::atomic<int32_t> g_testTouchPresent{-1};
static std::atomic<int32_t> g_testTouchContacts{0};
static JaliumPlatformWindow* g_testSystemMenuWindow = nullptr;
static int32_t g_testSystemMenuX = 0;
static int32_t g_testSystemMenuY = 0;
static uint32_t g_testSystemMenuSerial = 0;
#endif

enum class LinuxWindowSystem { Disabled, XServer, Wayland };
static LinuxWindowSystem g_windowSystem = LinuxWindowSystem::Disabled;

#ifdef JALIUM_HAS_WAYLAND
static wl_display* g_waylandDisplay = nullptr;
static wl_registry* g_waylandRegistry = nullptr;
static wl_compositor* g_waylandCompositor = nullptr;
static wl_shm* g_waylandShm = nullptr;
static xdg_wm_base* g_xdgWmBase = nullptr;
#ifdef JALIUM_HAS_XDG_ACTIVATION_V1
static xdg_activation_v1* g_xdgActivation = nullptr;
#endif
static wl_seat* g_waylandSeat = nullptr;
static wl_pointer* g_waylandPointer = nullptr;
static wl_keyboard* g_waylandKeyboard = nullptr;
static wl_touch* g_waylandTouch = nullptr;
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

struct WaylandTouchContact {
    JaliumPlatformWindow* window = nullptr;
    float x = 0;
    float y = 0;
    uint64_t order = 0;
    bool primary = false;
};
static std::unordered_map<int32_t, WaylandTouchContact> g_waylandTouchContacts;
static uint64_t g_waylandTouchOrder = 0;

#ifdef JALIUM_HAS_WAYLAND_TABLET_V2
static zwp_tablet_manager_v2* g_waylandTabletManager = nullptr;
static zwp_tablet_seat_v2* g_waylandTabletSeat = nullptr;
static std::atomic<uint32_t> g_waylandTabletPointerIds{0x40000000u};

struct WaylandTabletToolState {
    zwp_tablet_tool_v2* tool = nullptr;
    JaliumPlatformWindow* window = nullptr;
    uint32_t pointerId = 0;
    float x = 0;
    float y = 0;
    float pressure = 0;
    float tiltX = 0;
    float tiltY = 0;
    float twist = 0;
    float distance = 0;
    float slider = 0;
    float wheelDegrees = 0;
    int32_t wheelClicks = 0;
    uint32_t capabilities = 0;
    uint32_t buttons = JALIUM_POINTER_BUTTON_NONE;
    int32_t toolType = JALIUM_POINTER_TOOL_PEN;
    bool inRange = false;
    bool down = false;
    bool pendingDown = false;
    bool pendingUp = false;
    bool pendingCancel = false;
    bool pendingProximityOut = false;
    bool dirty = false;
};
static std::unordered_set<WaylandTabletToolState*> g_waylandTabletTools;
#endif

#ifdef JALIUM_HAS_XDG_FOREIGN_V2
static zxdg_exporter_v2* g_waylandExporter = nullptr;
#endif
#ifdef JALIUM_HAS_XDG_DECORATION_V1
static zxdg_decoration_manager_v1* g_waylandDecorationManager = nullptr;
#endif
#ifdef JALIUM_HAS_XDG_TOPLEVEL_ICON_V1
static xdg_toplevel_icon_manager_v1* g_waylandToplevelIconManager = nullptr;
#endif

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
static uint32_t g_pendingWaylandDeleteBefore = 0;
static uint32_t g_pendingWaylandDeleteAfter = 0;
static bool g_pendingWaylandDeleteSet = false;
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
static zwp_text_input_manager_v1* g_waylandTextInputManagerV1 = nullptr;
static zwp_text_input_v1* g_waylandTextInputV1 = nullptr;
static uint32_t g_waylandTextInputSerialV1 = 0;

// Attached wl_output tracking: feeds monitor enumeration and per-surface
// HiDPI scale (wl_surface.enter tells us which output a window occupies).
struct WaylandOutputInfo {
    wl_output* output = nullptr;
    uint32_t   registryName = 0;
    int32_t    x = 0;
    int32_t    y = 0;
    int32_t    width = 0;        // current mode, physical px
    int32_t    height = 0;
    int32_t    scale = 1;
    int32_t    refreshMilliHz = 0;
};
static std::vector<WaylandOutputInfo*> g_waylandOutputs;

// Themed pointer cursors (wayland-cursor). wl_pointer.set_cursor only accepts
// the serial of the latest pointer.enter, tracked separately from the general
// input serial (button presses overwrite that one).
static wl_cursor_theme* g_waylandCursorTheme = nullptr;
static int g_waylandCursorThemeScale = 0;
static wl_surface* g_waylandCursorSurface = nullptr;
static uint32_t g_waylandPointerEnterSerial = 0;
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
static uint32_t g_doubleClickMilliseconds = 500;
static float g_doubleClickDistance = 4.0f;

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
    Colormap            x11Colormap = 0;
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
    intptr_t            parentHandle = 0;
    bool                enabled = true;
    int32_t             requestedMinWidth = 0;
    int32_t             requestedMinHeight = 0;
    int32_t             requestedMaxWidth = 0;
    int32_t             requestedMaxHeight = 0;
    uint64_t            dragSessionId = 0;
    uint32_t            dragSelectedEffect = JALIUM_DRAG_EFFECT_NONE;
    bool                x11PopupPointerGrabbed = false;
    bool                x11PopupKeyboardGrabbed = false;

    // Anchor of the most recent mouse button press, used by the interactive
    // move/resize ABI (_NET_WM_MOVERESIZE wants the press's root position and
    // button; xdg_toplevel.move wants the press serial, kept globally).
    int32_t             lastPressRootX = 0;
    int32_t             lastPressRootY = 0;
    uint32_t            lastPressButton = 0;

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
    xdg_popup*          xdgPopup = nullptr;
    uint32_t            xdgPopupRepositionToken = 0;
#ifdef JALIUM_HAS_XDG_DECORATION_V1
    zxdg_toplevel_decoration_v1* xdgDecoration = nullptr;
    uint32_t            xdgDecorationMode = ZXDG_TOPLEVEL_DECORATION_V1_MODE_CLIENT_SIDE;
#endif
#ifdef JALIUM_HAS_XDG_FOREIGN_V2
    zxdg_exported_v2*   portalExport = nullptr;
    std::string         portalParentToken;
#endif
    std::string         waylandTitle;
    std::atomic<bool>   waylandConfigured{false};
    std::atomic<bool>   waylandVisible{false};
    std::atomic<bool>   waylandPaintPending{false};
    std::atomic<bool>   waylandDispatchingPaint{false};
    bool                waylandActivated = false;
    bool                imeEnabled = false;
    std::string         imeSurroundingText;
    int32_t             imeCursorByteOffset = 0;
    int32_t             imeAnchorByteOffset = 0;
    int32_t             imeCaretX = 0;
    int32_t             imeCaretY = 0;
    int32_t             imeCaretWidth = 1;
    int32_t             imeCaretHeight = 1;

    // Integer output scale of the wl_output this surface currently occupies.
    // Window width/height (and every pointer coordinate we dispatch) are kept
    // in physical pixels = compositor-logical * waylandScale, with
    // wl_surface_set_buffer_scale telling the compositor buffers match. On a
    // scale-1 compositor everything multiplies by 1 and behavior is identical
    // to the pre-HiDPI code.
    int32_t             waylandScale = 1;
    // wl_surface.enter/leave may name more than one output while a surface
    // straddles monitor boundaries. Keep a registry-name keyed snapshot so
    // scale changes and output removal can update the same state without
    // retaining wl_output pointers after registry removal.
    std::unordered_map<uint32_t, int32_t> waylandEnteredOutputs;
    // Compositor-logical size from the last xdg configure (0 until told).
    int32_t             waylandLogicalWidth = 0;
    int32_t             waylandLogicalHeight = 0;
#endif

    void DispatchEvent(const JaliumPlatformEvent& evt)
    {
        if (!enabled)
        {
            switch (evt.type)
            {
            case JALIUM_EVENT_CLOSE_REQUESTED:
            case JALIUM_EVENT_ACTIVATE:
            case JALIUM_EVENT_FOCUS_GAINED:
            case JALIUM_EVENT_MOUSE_MOVE:
            case JALIUM_EVENT_MOUSE_DOWN:
            case JALIUM_EVENT_MOUSE_UP:
            case JALIUM_EVENT_MOUSE_WHEEL:
            case JALIUM_EVENT_MOUSE_ENTER:
            case JALIUM_EVENT_MOUSE_LEAVE:
            case JALIUM_EVENT_KEY_DOWN:
            case JALIUM_EVENT_KEY_UP:
            case JALIUM_EVENT_CHAR_INPUT:
            case JALIUM_EVENT_COMPOSITION_START:
            case JALIUM_EVENT_COMPOSITION_UPDATE:
            case JALIUM_EVENT_DELETE_SURROUNDING_TEXT:
            case JALIUM_EVENT_POINTER_DOWN:
            case JALIUM_EVENT_POINTER_UP:
            case JALIUM_EVENT_POINTER_MOVE:
            case JALIUM_EVENT_POINTER_CANCEL:
            case JALIUM_EVENT_DRAG_ENTER:
            case JALIUM_EVENT_DRAG_OVER:
            case JALIUM_EVENT_DROP:
                return;
            default:
                break;
            }
        }
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
static void CancelXInputContactsForWindow(JaliumPlatformWindow* window);
#ifdef JALIUM_HAS_XRANDR
static void RefreshX11DisplayMetrics();
#endif

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

static void DispatchDeleteSurrounding(
    JaliumPlatformWindow* window, uint32_t beforeUtf8Bytes,
    uint32_t afterUtf8Bytes)
{
    if (!window || (beforeUtf8Bytes == 0 && afterUtf8Bytes == 0)) return;
    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_DELETE_SURROUNDING_TEXT;
    event.window = window;
    event.deleteSurrounding.beforeUtf8Bytes = static_cast<int32_t>(
        std::min<uint32_t>(beforeUtf8Bytes,
                           static_cast<uint32_t>(std::numeric_limits<int32_t>::max())));
    event.deleteSurrounding.afterUtf8Bytes = static_cast<int32_t>(
        std::min<uint32_t>(afterUtf8Bytes,
                           static_cast<uint32_t>(std::numeric_limits<int32_t>::max())));
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
    const bool continues = tracker.window == window && tracker.button == button &&
        static_cast<uint32_t>(time - tracker.time) <= g_doubleClickMilliseconds &&
        std::fabs(x - tracker.x) <= g_doubleClickDistance &&
        std::fabs(y - tracker.y) <= g_doubleClickDistance;
    tracker.count = continues ? std::min(tracker.count + 1, 3) : 1;
    tracker.window = window;
    tracker.button = button;
    tracker.time = time;
    tracker.x = x;
    tracker.y = y;
    return tracker.count;
}

static void LoadDoubleClickSettings()
{
    g_doubleClickMilliseconds = 500;
    g_doubleClickDistance = 4.0f;
    if (const char* value = getenv("JALIUM_DOUBLE_CLICK_TIME"); value && *value)
    {
        char* end = nullptr;
        errno = 0;
        const unsigned long parsed = strtoul(value, &end, 10);
        if (errno == 0 && end && *end == '\0' && parsed > 0 && parsed <= 5000)
            g_doubleClickMilliseconds = static_cast<uint32_t>(parsed);
    }
    if (const char* value = getenv("JALIUM_DOUBLE_CLICK_DISTANCE"); value && *value)
    {
        char* end = nullptr;
        errno = 0;
        const float parsed = strtof(value, &end);
        if (errno == 0 && end && *end == '\0' && std::isfinite(parsed) &&
            parsed >= 0.0f && parsed <= 100.0f)
            g_doubleClickDistance = parsed;
    }
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
                        if (dpi > 0)
                        {
                            XrmDestroyDatabase(db);
                            return dpi / 96.0f;
                        }
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

struct X11MonitorMetrics {
    int32_t x = 0;
    int32_t y = 0;
    int32_t width = 0;
    int32_t height = 0;
    int32_t widthMm = 0;
    int32_t heightMm = 0;
    float scale = 1.0f;
    int32_t refreshRate = 0;
    bool primary = false;
};

static float ComputeX11MonitorScale(
    int32_t width, int32_t height, int32_t widthMm, int32_t heightMm,
    float fallback)
{
    fallback = std::isfinite(fallback) && fallback > 0.0f ? fallback : 1.0f;
    const double dpiX = width > 0 && widthMm > 0
        ? static_cast<double>(width) * 25.4 / static_cast<double>(widthMm)
        : 0.0;
    const double dpiY = height > 0 && heightMm > 0
        ? static_cast<double>(height) * 25.4 / static_cast<double>(heightMm)
        : 0.0;
    const bool validX = std::isfinite(dpiX) && dpiX >= 50.0 && dpiX <= 400.0;
    const bool validY = std::isfinite(dpiY) && dpiY >= 50.0 && dpiY <= 400.0;
    if (!validX && !validY) return fallback;
    if (validX && validY)
    {
        const double relativeDifference =
            std::abs(dpiX - dpiY) / std::max(dpiX, dpiY);
        if (relativeDifference > 0.20)
            return fallback;
    }
    const double dpi = validX && validY ? (dpiX + dpiY) * 0.5
        : (validX ? dpiX : dpiY);
    const float scale = static_cast<float>(dpi / 96.0);
    return scale >= 0.5f && scale <= 4.0f ? scale : fallback;
}

#ifdef JALIUM_HAS_XRANDR
static int32_t GetX11ModeRefreshRate(
    const XRRScreenResources* resources, RRMode modeId)
{
    if (!resources || modeId == None) return 0;
    for (int modeIndex = 0; modeIndex < resources->nmode; ++modeIndex)
    {
        const XRRModeInfo& mode = resources->modes[modeIndex];
        if (mode.id != modeId || mode.hTotal == 0 || mode.vTotal == 0)
            continue;
        double rate = static_cast<double>(mode.dotClock) /
            (static_cast<double>(mode.hTotal) * mode.vTotal);
        if ((mode.modeFlags & RR_DoubleScan) != 0) rate *= 0.5;
        if ((mode.modeFlags & RR_Interlace) != 0) rate *= 2.0;
        return std::isfinite(rate) && rate > 0.0
            ? static_cast<int32_t>(std::lround(rate))
            : 0;
    }
    return 0;
}

static int32_t GetX11FallbackRefreshRate()
{
    if (g_display)
    {
        XRRScreenConfiguration* configuration =
            XRRGetScreenInfo(g_display, g_rootWindow);
        if (configuration)
        {
            const short rate = XRRConfigCurrentRate(configuration);
            XRRFreeScreenConfigInfo(configuration);
            if (rate > 0) return rate;
        }
    }
    return 60;
}

static int32_t GetX11MonitorRefreshRate(
    const XRRMonitorInfo& monitor, XRRScreenResources* resources)
{
    if (!g_display || !resources) return 0;
    for (int outputIndex = 0; outputIndex < monitor.noutput; ++outputIndex)
    {
        XRROutputInfo* output = XRRGetOutputInfo(
            g_display, resources, monitor.outputs[outputIndex]);
        if (!output) continue;
        const RRCrtc crtcId = output->crtc;
        XRRFreeOutputInfo(output);
        if (crtcId == None) continue;

        XRRCrtcInfo* crtc = XRRGetCrtcInfo(g_display, resources, crtcId);
        if (!crtc) continue;
        const int32_t refreshRate =
            GetX11ModeRefreshRate(resources, crtc->mode);
        XRRFreeCrtcInfo(crtc);
        if (refreshRate > 0) return refreshRate;
    }
    return 0;
}

static void FillX11MonitorMetrics(
    const XRRMonitorInfo& monitor, float fallbackScale,
    XRRScreenResources* resources, X11MonitorMetrics& metrics)
{
    metrics.x = monitor.x;
    metrics.y = monitor.y;
    metrics.width = monitor.width;
    metrics.height = monitor.height;
    metrics.widthMm = monitor.mwidth;
    metrics.heightMm = monitor.mheight;
    metrics.primary = monitor.primary != False;
    metrics.scale = ComputeX11MonitorScale(
        metrics.width, metrics.height, metrics.widthMm, metrics.heightMm,
        fallbackScale);
    metrics.refreshRate = GetX11MonitorRefreshRate(monitor, resources);
    if (metrics.refreshRate <= 0)
        metrics.refreshRate = GetX11FallbackRefreshRate();
}

static bool GetX11MonitorMetricsSnapshot(
    std::vector<X11MonitorMetrics>& metrics)
{
    metrics.clear();
    if (!g_display || !g_xrandrAvailable) return false;
    // GetScreenResourcesCurrent and output-primary are RandR 1.3 requests.
    // A 1.2 server still supplies full CRTC topology through the original
    // GetScreenResources request.
    XRRScreenResources* resources = g_xrandr13Available
        ? XRRGetScreenResourcesCurrent(g_display, g_rootWindow)
        : XRRGetScreenResources(g_display, g_rootWindow);
    if (!resources) return false;
    const float fallbackScale = DetectDpiScale();

    if (g_xrandrMonitorObjectsAvailable)
    {
        int monitorCount = 0;
        XRRMonitorInfo* monitors =
            XRRGetMonitors(g_display, g_rootWindow, True, &monitorCount);
        if (monitors)
        {
            metrics.reserve(static_cast<size_t>(std::max(monitorCount, 0)));
            for (int index = 0; index < monitorCount; ++index)
            {
                X11MonitorMetrics monitorMetrics{};
                FillX11MonitorMetrics(
                    monitors[index], fallbackScale, resources, monitorMetrics);
                metrics.push_back(monitorMetrics);
            }
            XRRFreeMonitors(monitors);
        }
    }

    // XRRGetMonitors is a RandR 1.5 server API. On 1.2-1.4, and on a 1.5
    // server without monitor objects, active CRTCs are the authoritative
    // per-screen topology.
    if (metrics.empty())
    {
        const RROutput primaryOutput = g_xrandr13Available
            ? XRRGetOutputPrimary(g_display, g_rootWindow)
            : None;
        metrics.reserve(static_cast<size_t>(std::max(resources->ncrtc, 0)));
        for (int crtcIndex = 0; crtcIndex < resources->ncrtc; ++crtcIndex)
        {
            XRRCrtcInfo* crtc = XRRGetCrtcInfo(
                g_display, resources, resources->crtcs[crtcIndex]);
            if (!crtc) continue;
            if (crtc->mode == None || crtc->width == 0 || crtc->height == 0)
            {
                XRRFreeCrtcInfo(crtc);
                continue;
            }

            X11MonitorMetrics monitorMetrics{};
            monitorMetrics.x = crtc->x;
            monitorMetrics.y = crtc->y;
            monitorMetrics.width = static_cast<int32_t>(crtc->width);
            monitorMetrics.height = static_cast<int32_t>(crtc->height);
            monitorMetrics.refreshRate =
                GetX11ModeRefreshRate(resources, crtc->mode);
            for (int outputIndex = 0; outputIndex < crtc->noutput; ++outputIndex)
            {
                const RROutput outputId = crtc->outputs[outputIndex];
                XRROutputInfo* output =
                    XRRGetOutputInfo(g_display, resources, outputId);
                if (!output) continue;
                if (monitorMetrics.widthMm <= 0 && output->mm_width > 0 &&
                    output->mm_height > 0)
                {
                    monitorMetrics.widthMm = output->mm_width;
                    monitorMetrics.heightMm = output->mm_height;
                    if ((crtc->rotation & (RR_Rotate_90 | RR_Rotate_270)) != 0)
                        std::swap(
                            monitorMetrics.widthMm, monitorMetrics.heightMm);
                }
                monitorMetrics.primary |= outputId == primaryOutput;
                XRRFreeOutputInfo(output);
            }
            monitorMetrics.scale = ComputeX11MonitorScale(
                monitorMetrics.width, monitorMetrics.height,
                monitorMetrics.widthMm, monitorMetrics.heightMm,
                fallbackScale);
            if (monitorMetrics.refreshRate <= 0)
                monitorMetrics.refreshRate = GetX11FallbackRefreshRate();
            metrics.push_back(monitorMetrics);
            XRRFreeCrtcInfo(crtc);
        }
    }

    XRRFreeScreenResources(resources);
    return !metrics.empty();
}

static bool GetX11MonitorMetricsByIndex(int32_t index, X11MonitorMetrics& metrics)
{
    if (index < 0) return false;
    std::vector<X11MonitorMetrics> monitors;
    if (!GetX11MonitorMetricsSnapshot(monitors) ||
        static_cast<size_t>(index) >= monitors.size())
        return false;
    metrics = monitors[static_cast<size_t>(index)];
    return true;
}

static bool GetX11MonitorMetricsForRect(
    int32_t x, int32_t y, int32_t width, int32_t height,
    X11MonitorMetrics& metrics)
{
    std::vector<X11MonitorMetrics> monitors;
    if (!GetX11MonitorMetricsSnapshot(monitors)) return false;

    const double centerX = static_cast<double>(x) + width * 0.5;
    const double centerY = static_cast<double>(y) + height * 0.5;
    int selected = 0;
    double bestDistance = std::numeric_limits<double>::max();
    for (size_t index = 0; index < monitors.size(); ++index)
    {
        const X11MonitorMetrics& monitor = monitors[index];
        if (centerX >= monitor.x && centerX < monitor.x + monitor.width &&
            centerY >= monitor.y && centerY < monitor.y + monitor.height)
        {
            selected = index;
            bestDistance = -1.0;
            break;
        }
        const double monitorCenterX = monitor.x + monitor.width * 0.5;
        const double monitorCenterY = monitor.y + monitor.height * 0.5;
        const double dx = centerX - monitorCenterX;
        const double dy = centerY - monitorCenterY;
        const double distance = dx * dx + dy * dy;
        if (distance < bestDistance)
        {
            bestDistance = distance;
            selected = index;
        }
    }
    metrics = monitors[static_cast<size_t>(selected)];
    return true;
}
#else
static bool GetX11MonitorMetricsByIndex(int32_t, X11MonitorMetrics&) { return false; }
static bool GetX11MonitorMetricsForRect(
    int32_t, int32_t, int32_t, int32_t, X11MonitorMetrics&) { return false; }
#endif

static void GetX11WindowRootRect(
    JaliumPlatformWindow* window,
    int32_t& x, int32_t& y, int32_t& width, int32_t& height)
{
    x = window ? window->x : 0;
    y = window ? window->y : 0;
    width = window ? window->width : 1;
    height = window ? window->height : 1;
    if (!window || !g_display || !window->xwindow) return;

    XWindowAttributes attributes{};
    if (XGetWindowAttributes(g_display, window->xwindow, &attributes))
    {
        width = attributes.width;
        height = attributes.height;
    }
    Window child = None;
    (void)XTranslateCoordinates(
        g_display, window->xwindow, g_rootWindow, 0, 0, &x, &y, &child);
}

static bool GetX11MonitorMetricsForWindow(
    JaliumPlatformWindow* window, X11MonitorMetrics& metrics)
{
    int32_t x = 0, y = 0, width = 1, height = 1;
    GetX11WindowRootRect(window, x, y, width, height);
    return GetX11MonitorMetricsForRect(x, y, width, height, metrics);
}

static bool UpdateX11WindowDpi(
    JaliumPlatformWindow* window,
    const X11MonitorMetrics& metrics)
{
    if (!window || !std::isfinite(metrics.scale) || metrics.scale <= 0.0f ||
        std::abs(window->dpiScale - metrics.scale) < 0.01f)
        return false;

    const float previousScale = window->dpiScale > 0.0f ? window->dpiScale : 1.0f;
    const int32_t suggestedWidth = std::max(
        1, static_cast<int32_t>(std::lround(
            static_cast<double>(window->width) * metrics.scale / previousScale)));
    const int32_t suggestedHeight = std::max(
        1, static_cast<int32_t>(std::lround(
            static_cast<double>(window->height) * metrics.scale / previousScale)));
    window->dpiScale = metrics.scale;

    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_DPI_CHANGED;
    event.window = window;
    event.dpiChanged.dpiX = metrics.scale * 96.0f;
    event.dpiChanged.dpiY = metrics.scale * 96.0f;
    event.dpiChanged.suggestedX = metrics.x;
    event.dpiChanged.suggestedY = metrics.y;
    event.dpiChanged.suggestedWidth = suggestedWidth;
    event.dpiChanged.suggestedHeight = suggestedHeight;
    window->DispatchEvent(event);

    if (g_display && window->xwindow &&
        (suggestedWidth != window->width || suggestedHeight != window->height))
    {
        XResizeWindow(g_display, window->xwindow,
                      static_cast<unsigned int>(suggestedWidth),
                      static_cast<unsigned int>(suggestedHeight));
    }
    return true;
}

#ifdef JALIUM_HAS_XRANDR
static void RefreshX11DisplayMetrics()
{
    std::vector<JaliumPlatformWindow*> windows;
    {
        std::lock_guard<std::mutex> lock(g_windowMapMutex);
        windows.reserve(g_windowMap.size());
        for (const auto& [xwindow, window] : g_windowMap)
        {
            (void)xwindow;
            if (window && !window->destroyed) windows.push_back(window);
        }
    }
    for (JaliumPlatformWindow* window : windows)
    {
        X11MonitorMetrics metrics{};
        if (GetX11MonitorMetricsForWindow(window, metrics))
            UpdateX11WindowDpi(window, metrics);

        JaliumPlatformEvent event{};
        event.type = JALIUM_EVENT_MONITORS_CHANGED;
        event.window = window;
        window->DispatchEvent(event);
    }
}
#endif

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

#ifdef JALIUM_HAS_XDG_ACTIVATION_V1
struct WaylandActivationRequest {
    xdg_activation_token_v1* token = nullptr;
    JaliumPlatformWindow* window = nullptr;
};

static std::unordered_set<WaylandActivationRequest*> g_waylandActivationRequests;

static void CancelWaylandActivationRequests(JaliumPlatformWindow* window)
{
    for (auto iterator = g_waylandActivationRequests.begin();
         iterator != g_waylandActivationRequests.end();)
    {
        WaylandActivationRequest* request = *iterator;
        if (window && request->window != window)
        {
            ++iterator;
            continue;
        }

        if (request->token)
            xdg_activation_token_v1_destroy(request->token);
        delete request;
        iterator = g_waylandActivationRequests.erase(iterator);
    }
}

static void HandleActivationTokenDone(
    void* data, xdg_activation_token_v1* token, const char* activationToken)
{
    auto* request = static_cast<WaylandActivationRequest*>(data);
    if (!request || g_waylandActivationRequests.erase(request) == 0)
        return;

    JaliumPlatformWindow* window = request->window;
    if (g_xdgActivation && activationToken && *activationToken && window &&
        !window->destroyed && window->waylandSurface)
    {
        xdg_activation_v1_activate(
            g_xdgActivation, activationToken, window->waylandSurface);
    }

    if (token)
        xdg_activation_token_v1_destroy(token);
    delete request;
    if (g_waylandDisplay)
        wl_display_flush(g_waylandDisplay);
}

static const xdg_activation_token_v1_listener g_activationTokenListener = {
    HandleActivationTokenDone
};
#else
static void CancelWaylandActivationRequests(JaliumPlatformWindow*) {}
#endif

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

static void ApplyWaylandScale(JaliumPlatformWindow* window, int32_t newScale)
{
    if (!window || newScale <= 0 || newScale == window->waylandScale)
        return;

    window->waylandScale = newScale;
    window->dpiScale = static_cast<float>(newScale);
    if (window->waylandSurface)
        wl_surface_set_buffer_scale(window->waylandSurface, newScale);

    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_DPI_CHANGED;
    event.window = window;
    event.dpiChanged.dpiX = 96.0f * static_cast<float>(newScale);
    event.dpiChanged.dpiY = 96.0f * static_cast<float>(newScale);
    window->DispatchEvent(event);

    // Physical size = logical * scale; the logical size the compositor gave
    // us is unchanged, but every physical-pixel consumer must re-size.
    if (window->waylandLogicalWidth == 0 || window->waylandLogicalHeight == 0)
    {
        // Not configured yet: the seeded width/height were scale-1 physical,
        // i.e. equal to logical.
        window->waylandLogicalWidth = window->width;
        window->waylandLogicalHeight = window->height;
    }

    const int32_t physicalWidth = window->waylandLogicalWidth * newScale;
    const int32_t physicalHeight = window->waylandLogicalHeight * newScale;
    if (physicalWidth != window->width || physicalHeight != window->height)
    {
        window->width = physicalWidth;
        window->height = physicalHeight;
        event = {};
        event.type = JALIUM_EVENT_RESIZE;
        event.window = window;
        event.resize.width = physicalWidth;
        event.resize.height = physicalHeight;
        window->DispatchEvent(event);
    }

    DispatchWaylandPaint(window);
}

static WaylandOutputInfo* FindWaylandOutput(wl_output* output)
{
    for (WaylandOutputInfo* info : g_waylandOutputs)
    {
        if (info && info->output == output)
            return info;
    }
    return nullptr;
}

static void RecomputeWaylandScale(JaliumPlatformWindow* window)
{
    if (!window) return;
    int32_t scale = 1;
    for (const auto& [output, outputScale] : window->waylandEnteredOutputs)
    {
        (void)output;
        scale = std::max(scale, std::max(outputScale, 1));
    }
    ApplyWaylandScale(window, scale);
}

static uint32_t SelectWaylandEnteredOutputId(
    const JaliumPlatformWindow* window)
{
    if (!window) return 0;
    uint32_t selectedId = 0;
    int32_t selectedScale = 0;
    for (const auto& [outputId, scale] : window->waylandEnteredOutputs)
    {
        if (selectedId == 0 || scale > selectedScale ||
            (scale == selectedScale && outputId < selectedId))
        {
            selectedId = outputId;
            selectedScale = scale;
        }
    }
    return selectedId;
}

static void SetWaylandOutputState(
    JaliumPlatformWindow* window,
    uint32_t registryName,
    int32_t scale,
    bool entered)
{
    if (!window) return;
    if (entered)
        window->waylandEnteredOutputs[registryName] = std::max(scale, 1);
    else
        window->waylandEnteredOutputs.erase(registryName);
    RecomputeWaylandScale(window);
}

static std::vector<JaliumPlatformWindow*> SnapshotWaylandWindows()
{
    std::lock_guard<std::mutex> lock(g_windowMapMutex);
    std::vector<JaliumPlatformWindow*> windows;
    windows.reserve(g_waylandWindows.size());
    windows.insert(windows.end(), g_waylandWindows.begin(), g_waylandWindows.end());
    return windows;
}

static void UpdateWaylandOutputScale(uint32_t registryName, int32_t scale)
{
    for (JaliumPlatformWindow* window : SnapshotWaylandWindows())
    {
        auto entered = window->waylandEnteredOutputs.find(registryName);
        if (entered == window->waylandEnteredOutputs.end())
            continue;
        entered->second = std::max(scale, 1);
        RecomputeWaylandScale(window);
    }
}

static void RemoveWaylandOutputFromWindows(uint32_t registryName)
{
    for (JaliumPlatformWindow* window : SnapshotWaylandWindows())
    {
        if (window->waylandEnteredOutputs.erase(registryName) != 0)
            RecomputeWaylandScale(window);
    }
}

static void DispatchWaylandMonitorsChanged()
{
    for (JaliumPlatformWindow* window : SnapshotWaylandWindows())
    {
        JaliumPlatformEvent event{};
        event.type = JALIUM_EVENT_MONITORS_CHANGED;
        event.window = window;
        window->DispatchEvent(event);
    }
}

static void HandleSurfaceEnter(void* data, wl_surface*, wl_output* output)
{
    auto* window = static_cast<JaliumPlatformWindow*>(data);
    WaylandOutputInfo* info = FindWaylandOutput(output);
    if (info)
        SetWaylandOutputState(window, info->registryName, info->scale, true);
}

static void HandleSurfaceLeave(void* data, wl_surface*, wl_output* output)
{
    auto* window = static_cast<JaliumPlatformWindow*>(data);
    WaylandOutputInfo* info = FindWaylandOutput(output);
    if (info)
        SetWaylandOutputState(window, info->registryName, info->scale, false);
}

// v6 members (preferred_buffer_scale/transform) intentionally stay null via
// aggregate initialization on newer headers.
static const wl_surface_listener g_surfaceListener = {
    HandleSurfaceEnter,
    HandleSurfaceLeave,
};

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
    if (width > 0 && height > 0)
    {
        // xdg configure reports compositor-logical size; the window (and every
        // consumer above native) works in physical pixels = logical * scale.
        window->waylandLogicalWidth = width;
        window->waylandLogicalHeight = height;
        const int32_t physicalWidth = width * window->waylandScale;
        const int32_t physicalHeight = height * window->waylandScale;
        if (physicalWidth != window->width || physicalHeight != window->height)
        {
            window->width = physicalWidth;
            window->height = physicalHeight;
            JaliumPlatformEvent event{};
            event.type = JALIUM_EVENT_RESIZE;
            event.window = window;
            event.resize.width = physicalWidth;
            event.resize.height = physicalHeight;
            window->DispatchEvent(event);
        }
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

static void HandleXdgPopupConfigure(
    void* data, xdg_popup*, int32_t x, int32_t y, int32_t width, int32_t height)
{
    auto* window = static_cast<JaliumPlatformWindow*>(data);
    if (!window) return;
    const int32_t scale = window->waylandScale > 0 ? window->waylandScale : 1;
    const int32_t physicalX = x * scale;
    const int32_t physicalY = y * scale;
    const int32_t physicalWidth = width * scale;
    const int32_t physicalHeight = height * scale;

    if (physicalX != window->x || physicalY != window->y)
    {
        window->x = physicalX;
        window->y = physicalY;
        JaliumPlatformEvent event{};
        event.type = JALIUM_EVENT_MOVE;
        event.window = window;
        event.move.x = physicalX;
        event.move.y = physicalY;
        window->DispatchEvent(event);
    }
    if (physicalWidth > 0 && physicalHeight > 0 &&
        (physicalWidth != window->width || physicalHeight != window->height))
    {
        window->width = physicalWidth;
        window->height = physicalHeight;
        window->waylandLogicalWidth = width;
        window->waylandLogicalHeight = height;
        JaliumPlatformEvent event{};
        event.type = JALIUM_EVENT_RESIZE;
        event.window = window;
        event.resize.width = physicalWidth;
        event.resize.height = physicalHeight;
        window->DispatchEvent(event);
    }
}

static void HandleXdgPopupDone(void* data, xdg_popup*)
{
    auto* window = static_cast<JaliumPlatformWindow*>(data);
    if (!window) return;
    JaliumPlatformEvent event{};
    event.type = JALIUM_EVENT_CLOSE_REQUESTED;
    event.window = window;
    window->DispatchEvent(event);
}

#ifdef XDG_POPUP_REPOSITIONED_SINCE_VERSION
static void HandleXdgPopupRepositioned(void*, xdg_popup*, uint32_t) {}
#endif

static const xdg_popup_listener g_xdgPopupListener = {
    HandleXdgPopupConfigure,
    HandleXdgPopupDone
#ifdef XDG_POPUP_REPOSITIONED_SINCE_VERSION
    , HandleXdgPopupRepositioned
#endif
};

static JaliumPlatformWindow* FindWaylandWindowBySurface(wl_surface* surface)
{
    if (!surface) return nullptr;
    std::lock_guard<std::mutex> lock(g_windowMapMutex);
    for (JaliumPlatformWindow* candidate : g_waylandWindows)
    {
        if (candidate && candidate->waylandSurface == surface)
            return candidate;
    }
    return nullptr;
}

static xdg_positioner* CreatePopupPositioner(JaliumPlatformWindow* window)
{
    if (!window || !g_xdgWmBase) return nullptr;
    xdg_positioner* positioner = xdg_wm_base_create_positioner(g_xdgWmBase);
    if (!positioner) return nullptr;
    const int32_t scale = window->waylandScale > 0 ? window->waylandScale : 1;
    const int32_t logicalWidth = std::max(1, (window->width + scale - 1) / scale);
    const int32_t logicalHeight = std::max(1, (window->height + scale - 1) / scale);
    const int32_t logicalX = window->x / scale;
    const int32_t logicalY = window->y / scale;
    xdg_positioner_set_size(positioner, logicalWidth, logicalHeight);
    // Keep the anchor rectangle inside the parent geometry even when the
    // requested popup origin overflows it; offsets are the protocol-defined
    // place for the desired displacement and can then be constrained safely.
    xdg_positioner_set_anchor_rect(positioner, 0, 0, 1, 1);
    xdg_positioner_set_anchor(positioner, XDG_POSITIONER_ANCHOR_TOP_LEFT);
    xdg_positioner_set_gravity(positioner, XDG_POSITIONER_GRAVITY_BOTTOM_RIGHT);
    xdg_positioner_set_offset(positioner, logicalX, logicalY);
    xdg_positioner_set_constraint_adjustment(
        positioner,
        XDG_POSITIONER_CONSTRAINT_ADJUSTMENT_SLIDE_X |
        XDG_POSITIONER_CONSTRAINT_ADJUSTMENT_SLIDE_Y |
        XDG_POSITIONER_CONSTRAINT_ADJUSTMENT_FLIP_X |
        XDG_POSITIONER_CONSTRAINT_ADJUSTMENT_FLIP_Y);
#ifdef XDG_POSITIONER_SET_REACTIVE_SINCE_VERSION
    if (xdg_positioner_get_version(positioner) >=
        XDG_POSITIONER_SET_REACTIVE_SINCE_VERSION)
    {
        xdg_positioner_set_reactive(positioner);
        if (auto* parent = FindWaylandWindowBySurface(
                reinterpret_cast<wl_surface*>(window->parentHandle)))
        {
            const int32_t parentScale = parent->waylandScale > 0
                ? parent->waylandScale : 1;
            xdg_positioner_set_parent_size(
                positioner,
                std::max(1, parent->width / parentScale),
                std::max(1, parent->height / parentScale));
        }
    }
#endif
    return positioner;
}

#ifdef JALIUM_HAS_XDG_FOREIGN_V2
static void HandlePortalExported(void* data, zxdg_exported_v2*, const char* handle)
{
    auto* window = static_cast<JaliumPlatformWindow*>(data);
    if (window)
        window->portalParentToken = handle ? handle : "";
}

static const zxdg_exported_v2_listener g_portalExportListener = {
    HandlePortalExported
};

static void EnsureWaylandPortalExport(JaliumPlatformWindow* window)
{
    if (!window || !window->waylandSurface || window->portalExport ||
        !g_waylandExporter || (window->style & JALIUM_WINDOW_STYLE_POPUP))
        return;
    window->portalExport = zxdg_exporter_v2_export_toplevel(
        g_waylandExporter, window->waylandSurface);
    if (window->portalExport)
        zxdg_exported_v2_add_listener(
            window->portalExport, &g_portalExportListener, window);
}

static void DestroyWaylandPortalExport(JaliumPlatformWindow* window)
{
    if (!window) return;
    if (window->portalExport)
    {
        zxdg_exported_v2_destroy(window->portalExport);
        window->portalExport = nullptr;
    }
    window->portalParentToken.clear();
}
#else
static void EnsureWaylandPortalExport(JaliumPlatformWindow*) {}
static void DestroyWaylandPortalExport(JaliumPlatformWindow*) {}
#endif

#ifdef JALIUM_HAS_XDG_DECORATION_V1
static void HandleWaylandDecorationConfigure(
    void* data, zxdg_toplevel_decoration_v1*, uint32_t mode)
{
    auto* window = static_cast<JaliumPlatformWindow*>(data);
    if (window) window->xdgDecorationMode = mode;
}

static const zxdg_toplevel_decoration_v1_listener g_waylandDecorationListener = {
    HandleWaylandDecorationConfigure
};

static void CreateWaylandDecoration(JaliumPlatformWindow* window)
{
    if (!window || !window->xdgToplevel || window->xdgDecoration ||
        !g_waylandDecorationManager)
        return;
    window->xdgDecoration =
        zxdg_decoration_manager_v1_get_toplevel_decoration(
            g_waylandDecorationManager, window->xdgToplevel);
    if (!window->xdgDecoration) return;
    zxdg_toplevel_decoration_v1_add_listener(
        window->xdgDecoration, &g_waylandDecorationListener, window);
    const bool decorated =
        (window->style & JALIUM_WINDOW_STYLE_BORDERLESS) == 0;
    zxdg_toplevel_decoration_v1_set_mode(
        window->xdgDecoration,
        decorated
            ? ZXDG_TOPLEVEL_DECORATION_V1_MODE_SERVER_SIDE
            : ZXDG_TOPLEVEL_DECORATION_V1_MODE_CLIENT_SIDE);
}

static void DestroyWaylandDecoration(JaliumPlatformWindow* window)
{
    if (window && window->xdgDecoration)
    {
        zxdg_toplevel_decoration_v1_destroy(window->xdgDecoration);
        window->xdgDecoration = nullptr;
    }
}
#else
static void CreateWaylandDecoration(JaliumPlatformWindow*) {}
static void DestroyWaylandDecoration(JaliumPlatformWindow*) {}
#endif

static bool CreateWaylandRole(JaliumPlatformWindow* window)
{
    if (!window || !window->waylandSurface || !g_xdgWmBase) return false;
    if (window->xdgToplevel || window->xdgPopup) return true;

    window->waylandConfigured.store(false, std::memory_order_release);
    window->xdgSurface = xdg_wm_base_get_xdg_surface(g_xdgWmBase, window->waylandSurface);
    if (!window->xdgSurface) return false;
    xdg_surface_add_listener(window->xdgSurface, &g_xdgSurfaceListener, window);

    if (window->style & JALIUM_WINDOW_STYLE_POPUP)
    {
        auto* parent = FindWaylandWindowBySurface(
            reinterpret_cast<wl_surface*>(window->parentHandle));
        if (!parent || !parent->xdgSurface)
        {
            xdg_surface_destroy(window->xdgSurface);
            window->xdgSurface = nullptr;
            return false;
        }
        window->waylandScale = parent->waylandScale > 0 ? parent->waylandScale : 1;
        xdg_positioner* positioner = CreatePopupPositioner(window);
        if (!positioner)
        {
            xdg_surface_destroy(window->xdgSurface);
            window->xdgSurface = nullptr;
            return false;
        }
        window->xdgPopup = xdg_surface_get_popup(
            window->xdgSurface, parent->xdgSurface, positioner);
        xdg_positioner_destroy(positioner);
        if (!window->xdgPopup)
        {
            xdg_surface_destroy(window->xdgSurface);
            window->xdgSurface = nullptr;
            return false;
        }
        xdg_popup_add_listener(window->xdgPopup, &g_xdgPopupListener, window);
        if ((window->style & JALIUM_WINDOW_STYLE_POPUP_GRAB) &&
            g_waylandSeat && g_waylandInputSerial != 0)
        {
            // Must be issued before the initial mapping commit and with the
            // serial of the user action that opened the menu.
            xdg_popup_grab(
                window->xdgPopup, g_waylandSeat, g_waylandInputSerial);
        }
        wl_surface_set_buffer_scale(window->waylandSurface, window->waylandScale);
        wl_surface_commit(window->waylandSurface);
        wl_display_flush(g_waylandDisplay);
        return true;
    }

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
    CreateWaylandDecoration(window);
    if (!(window->style & JALIUM_WINDOW_STYLE_RESIZABLE))
    {
        xdg_toplevel_set_min_size(window->xdgToplevel, window->width, window->height);
        xdg_toplevel_set_max_size(window->xdgToplevel, window->width, window->height);
    }
    wl_surface_commit(window->waylandSurface);
    wl_display_flush(g_waylandDisplay);
    EnsureWaylandPortalExport(window);
    return true;
}

static void DestroyWaylandRole(JaliumPlatformWindow* window)
{
    if (!window) return;
    CancelWaylandActivationRequests(window);
    DestroyWaylandPortalExport(window);
    if (window->xdgPopup) { xdg_popup_destroy(window->xdgPopup); window->xdgPopup = nullptr; }
    DestroyWaylandDecoration(window);
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
    g_waylandDragWindow->xdndX = static_cast<float>(wl_fixed_to_double(x)) *
        static_cast<float>(g_waylandDragWindow->waylandScale);
    g_waylandDragWindow->xdndY = static_cast<float>(wl_fixed_to_double(y)) *
        static_cast<float>(g_waylandDragWindow->waylandScale);
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
    g_waylandDragWindow->xdndX = static_cast<float>(wl_fixed_to_double(x)) *
        static_cast<float>(g_waylandDragWindow->waylandScale);
    g_waylandDragWindow->xdndY = static_cast<float>(wl_fixed_to_double(y)) *
        static_cast<float>(g_waylandDragWindow->waylandScale);
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

static void HandleDataSourceTarget(void*, wl_data_source*, const char*) {}
static void HandleDataSourceSend(void*, wl_data_source*, const char* mimeType, int32_t fd)
{
    if (fd < 0) return;
    std::vector<uint8_t> snapshot;
    if (mimeType)
    {
        std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
        const auto iterator = std::find_if(
            g_clipboardItems.begin(), g_clipboardItems.end(),
            [mimeType](const OwnedClipboardItem& item)
            {
                return item.mimeType == mimeType;
            });
        if (iterator != g_clipboardItems.end()) snapshot = iterator->bytes;
    }
    std::thread([fd, bytes = std::move(snapshot)]() mutable
    {
        size_t offset = 0;
        while (offset < bytes.size())
        {
            const ssize_t written = write(fd, bytes.data() + offset, bytes.size() - offset);
            if (written > 0) offset += static_cast<size_t>(written);
            else if (written < 0 && errno == EINTR) continue;
            else if (written < 0 && (errno == EAGAIN || errno == EWOULDBLOCK))
            {
                struct pollfd descriptor{fd, POLLOUT, 0};
                int result;
                do { result = poll(&descriptor, 1, 2000); }
                while (result < 0 && errno == EINTR);
                if (result > 0) continue;
                break;
            }
            else break;
        }
        close(fd);
    }).detach();
}
static void HandleDataSourceCancelled(void*, wl_data_source* source)
{
    {
        std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
        if (source == g_waylandClipboardSource)
        {
            g_waylandClipboardSource = nullptr;
            g_clipboardItems.clear();
            g_clipboardUtf8.clear();
        }
    }
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
static void ApplyWaylandTextInputV3(JaliumPlatformWindow* window)
{
    if (!window || !g_waylandTextInput || window != g_keyboardFocus)
        return;

    if (!window->imeEnabled)
    {
        zwp_text_input_v3_disable(g_waylandTextInput);
        zwp_text_input_v3_commit(g_waylandTextInput);
        return;
    }

    const int32_t textLength = static_cast<int32_t>(std::min<size_t>(
        window->imeSurroundingText.size(),
        static_cast<size_t>(std::numeric_limits<int32_t>::max())));
    const int32_t cursor = std::clamp(
        window->imeCursorByteOffset, 0, textLength);
    const int32_t anchor = std::clamp(
        window->imeAnchorByteOffset, 0, textLength);
    const int32_t scale = std::max(window->waylandScale, 1);

    zwp_text_input_v3_enable(g_waylandTextInput);
    zwp_text_input_v3_set_content_type(
        g_waylandTextInput, ZWP_TEXT_INPUT_V3_CONTENT_HINT_NONE,
        ZWP_TEXT_INPUT_V3_CONTENT_PURPOSE_NORMAL);
    zwp_text_input_v3_set_surrounding_text(
        g_waylandTextInput, window->imeSurroundingText.c_str(), cursor, anchor);
    zwp_text_input_v3_set_cursor_rectangle(
        g_waylandTextInput,
        window->imeCaretX / scale,
        window->imeCaretY / scale,
        std::max(1, (window->imeCaretWidth + scale - 1) / scale),
        std::max(1, (window->imeCaretHeight + scale - 1) / scale));
    zwp_text_input_v3_commit(g_waylandTextInput);
}

static void HandleTextInputEnter(void*, zwp_text_input_v3* textInput, wl_surface* surface)
{
    JaliumPlatformWindow* window = WaylandWindowFromSurface(surface);
    if (window) g_keyboardFocus = window;
    if (window)
        ApplyWaylandTextInputV3(window);
    else
    {
        zwp_text_input_v3_disable(textInput);
        zwp_text_input_v3_commit(textInput);
    }
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
    g_pendingWaylandDeleteBefore = 0;
    g_pendingWaylandDeleteAfter = 0;
    g_pendingWaylandDeleteSet = false;
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

static void HandleTextInputDeleteSurrounding(
    void*, zwp_text_input_v3*, uint32_t beforeLength, uint32_t afterLength)
{
    g_pendingWaylandDeleteBefore = beforeLength;
    g_pendingWaylandDeleteAfter = afterLength;
    g_pendingWaylandDeleteSet = true;
}

static void HandleTextInputDone(void*, zwp_text_input_v3*, uint32_t)
{
    JaliumPlatformWindow* window = g_keyboardFocus;
    if (!window)
    {
        g_pendingWaylandPreeditSet = false;
        g_pendingWaylandCommitSet = false;
        g_pendingWaylandDeleteBefore = 0;
        g_pendingWaylandDeleteAfter = 0;
        g_pendingWaylandDeleteSet = false;
        return;
    }

    if (g_pendingWaylandDeleteSet)
    {
        DispatchDeleteSurrounding(
            window, g_pendingWaylandDeleteBefore, g_pendingWaylandDeleteAfter);
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
    g_pendingWaylandDeleteBefore = 0;
    g_pendingWaylandDeleteAfter = 0;
    g_pendingWaylandDeleteSet = false;
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
static void ApplyWaylandTextInputV1(JaliumPlatformWindow* window)
{
    if (!window || !g_waylandTextInputV1 || !g_waylandSeat ||
        window != g_keyboardFocus)
        return;
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
    if (g_waylandTextInputManager)
        return;
#endif

    if (!window->imeEnabled)
    {
        zwp_text_input_v1_deactivate(g_waylandTextInputV1, g_waylandSeat);
        g_waylandTextInputV1Active = false;
        zwp_text_input_v1_commit_state(
            g_waylandTextInputV1, ++g_waylandTextInputSerialV1);
        return;
    }

    const int32_t textLength = static_cast<int32_t>(std::min<size_t>(
        window->imeSurroundingText.size(),
        static_cast<size_t>(std::numeric_limits<int32_t>::max())));
    const uint32_t cursor = static_cast<uint32_t>(std::clamp(
        window->imeCursorByteOffset, 0, textLength));
    const uint32_t anchor = static_cast<uint32_t>(std::clamp(
        window->imeAnchorByteOffset, 0, textLength));
    const int32_t scale = std::max(window->waylandScale, 1);

    zwp_text_input_v1_activate(
        g_waylandTextInputV1, g_waylandSeat, window->waylandSurface);
    zwp_text_input_v1_set_surrounding_text(
        g_waylandTextInputV1, window->imeSurroundingText.c_str(), cursor, anchor);
    zwp_text_input_v1_set_content_type(
        g_waylandTextInputV1, ZWP_TEXT_INPUT_V1_CONTENT_HINT_NONE,
        ZWP_TEXT_INPUT_V1_CONTENT_PURPOSE_NORMAL);
    zwp_text_input_v1_set_cursor_rectangle(
        g_waylandTextInputV1,
        window->imeCaretX / scale,
        window->imeCaretY / scale,
        std::max(1, (window->imeCaretWidth + scale - 1) / scale),
        std::max(1, (window->imeCaretHeight + scale - 1) / scale));
    zwp_text_input_v1_commit_state(
        g_waylandTextInputV1, ++g_waylandTextInputSerialV1);
}

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
static void HandleTextInputV1DeleteSurrounding(
    void*, zwp_text_input_v1*, int32_t index, uint32_t length)
{
    if (!g_keyboardFocus || length == 0) return;

    // v1 describes an arbitrary byte interval relative to the cursor, while
    // the public Jalium event deliberately exposes adjacent before/after
    // lengths. Preserve exactness: disconnected intervals cannot be expressed
    // without deleting the gap, so ignore those rare requests instead of
    // corrupting application text.
    const int64_t start = index;
    const int64_t end = start + static_cast<int64_t>(length);
    if (start > 0 || end < 0) return;

    const uint32_t before = start < 0
        ? static_cast<uint32_t>(std::min<int64_t>(
              -start, std::numeric_limits<uint32_t>::max()))
        : 0;
    const uint32_t after = end > 0
        ? static_cast<uint32_t>(std::min<int64_t>(
              end, std::numeric_limits<uint32_t>::max()))
        : 0;
    DispatchDeleteSurrounding(g_keyboardFocus, before, after);
}
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
    g_waylandPointerEnterSerial = serial;
    g_pointerFocus = WaylandWindowFromSurface(surface);
    // Pointer coordinates arrive compositor-logical; everything above native
    // works in physical pixels (= logical * per-surface buffer scale).
    const float enterScale = g_pointerFocus
        ? static_cast<float>(g_pointerFocus->waylandScale)
        : 1.0f;
    g_pointerX = static_cast<float>(wl_fixed_to_double(x)) * enterScale;
    g_pointerY = static_cast<float>(wl_fixed_to_double(y)) * enterScale;
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
    const float motionScale = g_pointerFocus
        ? static_cast<float>(g_pointerFocus->waylandScale)
        : 1.0f;
    g_pointerX = static_cast<float>(wl_fixed_to_double(x)) * motionScale;
    g_pointerY = static_cast<float>(wl_fixed_to_double(y)) * motionScale;
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
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
    ApplyWaylandTextInputV3(g_keyboardFocus);
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
    ApplyWaylandTextInputV1(g_keyboardFocus);
#endif
    if (g_waylandDisplay) wl_display_flush(g_waylandDisplay);
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

#ifdef JALIUM_HAS_WAYLAND_TABLET_V2
static void DispatchWaylandTabletTool(
    WaylandTabletToolState* state, JaliumEventType type)
{
    if (!state || !state->window) return;
    JaliumPlatformEvent event{};
    event.type = type;
    event.window = state->window;
    event.pointer.pointerId = state->pointerId;
    event.pointer.x = state->x;
    event.pointer.y = state->y;
    event.pointer.pressure = state->down &&
        type != JALIUM_EVENT_POINTER_UP &&
        type != JALIUM_EVENT_POINTER_CANCEL
        ? state->pressure : 0.0f;
    event.pointer.tiltX = state->tiltX;
    event.pointer.tiltY = state->tiltY;
    event.pointer.twist = state->twist;
    event.pointer.pointerType =
        state->toolType == JALIUM_POINTER_TOOL_MOUSE ||
        state->toolType == JALIUM_POINTER_TOOL_LENS
        ? JALIUM_POINTER_MOUSE : JALIUM_POINTER_PEN;
    event.pointer.modifiers = g_waylandModifiers;
    uint32_t flags = JALIUM_POINTER_FLAG_PRIMARY;
    if (state->inRange) flags |= JALIUM_POINTER_FLAG_IN_RANGE;
    if (state->down) flags |= JALIUM_POINTER_FLAG_IN_CONTACT;
    if (state->toolType == JALIUM_POINTER_TOOL_ERASER)
        flags |= JALIUM_POINTER_FLAG_ERASER |
                 JALIUM_POINTER_FLAG_INVERTED;
    if (state->toolType != JALIUM_POINTER_TOOL_MOUSE &&
        state->toolType != JALIUM_POINTER_TOOL_LENS &&
        (state->buttons & (JALIUM_POINTER_BUTTON_BARREL |
                           JALIUM_POINTER_BUTTON_SECONDARY)) != 0)
        flags |= JALIUM_POINTER_FLAG_BARREL;
    event.pointer.flags = flags;
    event.pointer.toolType = state->toolType;
    event.pointer.buttons = state->buttons;
    state->window->DispatchEvent(event);
}

static int32_t WaylandTabletToolType(uint32_t type)
{
    switch (type)
    {
    case ZWP_TABLET_TOOL_V2_TYPE_ERASER:
        return JALIUM_POINTER_TOOL_ERASER;
    case ZWP_TABLET_TOOL_V2_TYPE_BRUSH:
        return JALIUM_POINTER_TOOL_BRUSH;
    case ZWP_TABLET_TOOL_V2_TYPE_PENCIL:
        return JALIUM_POINTER_TOOL_PENCIL;
    case ZWP_TABLET_TOOL_V2_TYPE_AIRBRUSH:
        return JALIUM_POINTER_TOOL_AIRBRUSH;
    case ZWP_TABLET_TOOL_V2_TYPE_MOUSE:
        return JALIUM_POINTER_TOOL_MOUSE;
    case ZWP_TABLET_TOOL_V2_TYPE_LENS:
        return JALIUM_POINTER_TOOL_LENS;
    case ZWP_TABLET_TOOL_V2_TYPE_PEN:
        return JALIUM_POINTER_TOOL_PEN;
    default:
        return JALIUM_POINTER_TOOL_UNKNOWN;
    }
}

static void TabletToolType(
    void* data, zwp_tablet_tool_v2*, uint32_t type)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (state) state->toolType = WaylandTabletToolType(type);
}
static void TabletToolHardwareSerial(void*, zwp_tablet_tool_v2*, uint32_t, uint32_t) {}
static void TabletToolHardwareId(void*, zwp_tablet_tool_v2*, uint32_t, uint32_t) {}
static void TabletToolCapability(
    void* data, zwp_tablet_tool_v2*, uint32_t capability)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (state && capability < 32)
        state->capabilities |= 1u << capability;
}
static void TabletToolDone(void*, zwp_tablet_tool_v2*) {}

static void TabletToolRemoved(void* data, zwp_tablet_tool_v2*)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    if (state->window)
    {
        if (state->down)
        {
            state->down = false;
            state->inRange = false;
            state->buttons &= ~JALIUM_POINTER_BUTTON_PRIMARY;
            DispatchWaylandTabletTool(state, JALIUM_EVENT_POINTER_CANCEL);
        }
        else if (state->inRange)
        {
            state->inRange = false;
            DispatchWaylandTabletTool(state, JALIUM_EVENT_POINTER_MOVE);
        }
    }
    g_waylandTabletTools.erase(state);
    if (state->tool) zwp_tablet_tool_v2_destroy(state->tool);
    delete state;
}

static void TabletToolProximityIn(
    void* data, zwp_tablet_tool_v2*, uint32_t serial,
    zwp_tablet_v2*, wl_surface* surface)
{
    g_waylandInputSerial = serial;
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    state->window = WaylandWindowFromSurface(surface);
    state->inRange = state->window != nullptr;
    state->down = false;
    state->pressure = 0.0f;
    state->buttons &= ~JALIUM_POINTER_BUTTON_PRIMARY;
    state->pendingProximityOut = false;
    state->dirty = true;
}

static void TabletToolProximityOut(void* data, zwp_tablet_tool_v2*)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    state->pendingCancel = state->down;
    state->pendingProximityOut = true;
    state->inRange = false;
    state->down = false;
    state->pressure = 0.0f;
    state->buttons &= ~JALIUM_POINTER_BUTTON_PRIMARY;
    state->dirty = true;
}

static void TabletToolDown(void* data, zwp_tablet_tool_v2*, uint32_t serial)
{
    g_waylandInputSerial = serial;
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    state->down = true;
    state->inRange = true;
    state->buttons |= JALIUM_POINTER_BUTTON_PRIMARY;
    state->pendingDown = true;
    state->pendingUp = false;
    state->pendingCancel = false;
    state->dirty = true;
}

static void TabletToolUp(void* data, zwp_tablet_tool_v2*)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    state->down = false;
    state->pressure = 0.0f;
    state->buttons &= ~JALIUM_POINTER_BUTTON_PRIMARY;
    state->pendingUp = true;
    state->pendingDown = false;
    state->dirty = true;
}

static void TabletToolMotion(
    void* data, zwp_tablet_tool_v2*, wl_fixed_t x, wl_fixed_t y)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state || !state->window) return;
    const float scale = static_cast<float>(
        state->window->waylandScale > 0 ? state->window->waylandScale : 1);
    state->x = static_cast<float>(wl_fixed_to_double(x)) * scale;
    state->y = static_cast<float>(wl_fixed_to_double(y)) * scale;
    state->dirty = true;
}

static void TabletToolPressure(void* data, zwp_tablet_tool_v2*, uint32_t pressure)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    state->pressure = std::clamp(
        static_cast<float>(pressure) / 65535.0f, 0.0f, 1.0f);
    state->dirty = true;
}

static void TabletToolDistance(
    void* data, zwp_tablet_tool_v2*, uint32_t distance)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    state->distance = std::clamp(
        static_cast<float>(distance) / 65535.0f, 0.0f, 1.0f);
    state->dirty = true;
}

static void TabletToolTilt(
    void* data, zwp_tablet_tool_v2*, wl_fixed_t x, wl_fixed_t y)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    state->tiltX = static_cast<float>(wl_fixed_to_double(x));
    state->tiltY = static_cast<float>(wl_fixed_to_double(y));
    state->dirty = true;
}

static void TabletToolRotation(void* data, zwp_tablet_tool_v2*, wl_fixed_t degrees)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    state->twist = static_cast<float>(wl_fixed_to_double(degrees));
    state->dirty = true;
}

static void TabletToolSlider(
    void* data, zwp_tablet_tool_v2*, int32_t position)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    state->slider = std::clamp(
        static_cast<float>(position) / 65535.0f, -1.0f, 1.0f);
    state->dirty = true;
}
static void TabletToolWheel(
    void* data, zwp_tablet_tool_v2*, wl_fixed_t degrees, int32_t clicks)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    state->wheelDegrees = static_cast<float>(wl_fixed_to_double(degrees));
    state->wheelClicks = clicks;
    state->dirty = true;
}

static uint32_t WaylandTabletButtonMask(uint32_t button)
{
    switch (button)
    {
    case BTN_TOUCH:
    case BTN_LEFT:
        return JALIUM_POINTER_BUTTON_PRIMARY;
    case BTN_STYLUS:
        return JALIUM_POINTER_BUTTON_BARREL;
    case BTN_STYLUS2:
    case BTN_RIGHT:
        return JALIUM_POINTER_BUTTON_SECONDARY;
    case BTN_MIDDLE:
        return JALIUM_POINTER_BUTTON_TERTIARY;
    default:
        return JALIUM_POINTER_BUTTON_NONE;
    }
}

static void TabletToolButton(
    void* data, zwp_tablet_tool_v2*, uint32_t serial,
    uint32_t button, uint32_t buttonState)
{
    g_waylandInputSerial = serial;
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state) return;
    const uint32_t mask = WaylandTabletButtonMask(button);
    if (buttonState == ZWP_TABLET_TOOL_V2_BUTTON_STATE_PRESSED)
        state->buttons |= mask;
    else
        state->buttons &= ~mask;
    state->dirty = true;
}

static void TabletToolFrame(void* data, zwp_tablet_tool_v2*, uint32_t)
{
    auto* state = static_cast<WaylandTabletToolState*>(data);
    if (!state || !state->dirty) return;
    if (state->window)
    {
        if (state->pendingCancel)
            DispatchWaylandTabletTool(state, JALIUM_EVENT_POINTER_CANCEL);
        else if (state->pendingDown)
            DispatchWaylandTabletTool(state, JALIUM_EVENT_POINTER_DOWN);
        else if (state->pendingUp)
            DispatchWaylandTabletTool(state, JALIUM_EVENT_POINTER_UP);
        else
            DispatchWaylandTabletTool(state, JALIUM_EVENT_POINTER_MOVE);
    }
    const bool clearWindow = state->pendingProximityOut;
    state->pendingDown = false;
    state->pendingUp = false;
    state->pendingCancel = false;
    state->pendingProximityOut = false;
    state->dirty = false;
    if (clearWindow) state->window = nullptr;
}

static const zwp_tablet_tool_v2_listener g_tabletToolListener = {
    TabletToolType,
    TabletToolHardwareSerial,
    TabletToolHardwareId,
    TabletToolCapability,
    TabletToolDone,
    TabletToolRemoved,
    TabletToolProximityIn,
    TabletToolProximityOut,
    TabletToolDown,
    TabletToolUp,
    TabletToolMotion,
    TabletToolPressure,
    TabletToolDistance,
    TabletToolTilt,
    TabletToolRotation,
    TabletToolSlider,
    TabletToolWheel,
    TabletToolButton,
    TabletToolFrame,
};

static void TabletName(void*, zwp_tablet_v2*, const char*) {}
static void TabletId(void*, zwp_tablet_v2*, uint32_t, uint32_t) {}
static void TabletPath(void*, zwp_tablet_v2*, const char*) {}
static void TabletDone(void*, zwp_tablet_v2*) {}
static void TabletRemoved(void*, zwp_tablet_v2* tablet)
{
    zwp_tablet_v2_destroy(tablet);
}
static const zwp_tablet_v2_listener g_tabletListener = {
    TabletName, TabletId, TabletPath, TabletDone, TabletRemoved
};

static void TabletSeatTabletAdded(void*, zwp_tablet_seat_v2*, zwp_tablet_v2* tablet)
{
    zwp_tablet_v2_add_listener(tablet, &g_tabletListener, nullptr);
}

static void TabletSeatToolAdded(void*, zwp_tablet_seat_v2*, zwp_tablet_tool_v2* tool)
{
    auto* state = new WaylandTabletToolState();
    state->tool = tool;
    state->pointerId = g_waylandTabletPointerIds.fetch_add(
        1, std::memory_order_relaxed);
    g_waylandTabletTools.insert(state);
    zwp_tablet_tool_v2_add_listener(tool, &g_tabletToolListener, state);
}

static void TabletSeatPadAdded(void*, zwp_tablet_seat_v2*, zwp_tablet_pad_v2* pad)
{
    // Jalium currently consumes pen tools but exposes no tablet-pad routed
    // event surface. Destroying the optional pad proxy does not affect tools.
    zwp_tablet_pad_v2_destroy(pad);
}

static const zwp_tablet_seat_v2_listener g_tabletSeatListener = {
    TabletSeatTabletAdded,
    TabletSeatToolAdded,
    TabletSeatPadAdded,
};

static void EnsureWaylandTabletSeat()
{
    if (g_waylandTabletSeat || !g_waylandTabletManager || !g_waylandSeat) return;
    g_waylandTabletSeat = zwp_tablet_manager_v2_get_tablet_seat(
        g_waylandTabletManager, g_waylandSeat);
    if (g_waylandTabletSeat)
        zwp_tablet_seat_v2_add_listener(
            g_waylandTabletSeat, &g_tabletSeatListener, nullptr);
}

static void CancelWaylandTabletToolsForWindow(JaliumPlatformWindow* window)
{
    for (WaylandTabletToolState* state : g_waylandTabletTools)
    {
        if (state && state->window == window)
        {
            if (state->down)
            {
                state->down = false;
                state->inRange = false;
                state->pressure = 0.0f;
                state->buttons &= ~JALIUM_POINTER_BUTTON_PRIMARY;
                DispatchWaylandTabletTool(state, JALIUM_EVENT_POINTER_CANCEL);
            }
            state->window = nullptr;
            state->inRange = false;
            state->down = false;
            state->pendingDown = false;
            state->pendingUp = false;
            state->pendingCancel = false;
            state->pendingProximityOut = false;
            state->dirty = false;
        }
    }
}
#else
static void EnsureWaylandTabletSeat() {}
static void CancelWaylandTabletToolsForWindow(JaliumPlatformWindow*) {}
#endif

static uint32_t WaylandTouchPointerId(int32_t touchId)
{
    return 0x10000000u | (static_cast<uint32_t>(touchId) & 0x0fffffffu);
}

static void DispatchWaylandTouch(
    JaliumPlatformWindow* window, JaliumEventType type, int32_t touchId,
    float x, float y, float pressure, bool primary)
{
    if (!window) return;
    JaliumPlatformEvent event{};
    event.type = type;
    event.window = window;
    event.pointer.pointerId = WaylandTouchPointerId(touchId);
    event.pointer.x = x;
    event.pointer.y = y;
    event.pointer.pressure = pressure;
    event.pointer.pointerType = JALIUM_POINTER_TOUCH;
    event.pointer.modifiers = g_waylandModifiers;
    event.pointer.flags = primary ? JALIUM_POINTER_FLAG_PRIMARY : 0;
    if (type == JALIUM_EVENT_POINTER_DOWN ||
        type == JALIUM_EVENT_POINTER_MOVE)
    {
        event.pointer.flags |= JALIUM_POINTER_FLAG_IN_RANGE |
                               JALIUM_POINTER_FLAG_IN_CONTACT;
        event.pointer.buttons = JALIUM_POINTER_BUTTON_PRIMARY;
    }
    event.pointer.toolType = JALIUM_POINTER_TOOL_UNKNOWN;
    window->DispatchEvent(event);
}

static void PromoteNextWaylandPrimaryTouch()
{
    WaylandTouchContact* next = nullptr;
    for (auto& [id, contact] : g_waylandTouchContacts)
    {
        (void)id;
        if (!next || contact.order < next->order)
            next = &contact;
    }
    if (next) next->primary = true;
}

static void HandleTouchDown(
    void*, wl_touch*, uint32_t serial, uint32_t, wl_surface* surface,
    int32_t id, wl_fixed_t x, wl_fixed_t y)
{
    g_waylandInputSerial = serial;
    JaliumPlatformWindow* window = WaylandWindowFromSurface(surface);
    if (!window) return;
    const float scale = static_cast<float>(
        window->waylandScale > 0 ? window->waylandScale : 1);
    WaylandTouchContact contact{};
    contact.window = window;
    contact.x = static_cast<float>(wl_fixed_to_double(x)) * scale;
    contact.y = static_cast<float>(wl_fixed_to_double(y)) * scale;
    contact.order = ++g_waylandTouchOrder;
    contact.primary = g_waylandTouchContacts.empty();
    g_waylandTouchContacts[id] = contact;
    DispatchWaylandTouch(
        window, JALIUM_EVENT_POINTER_DOWN, id,
        contact.x, contact.y, 1.0f, contact.primary);
}

static void HandleTouchUp(void*, wl_touch*, uint32_t serial, uint32_t, int32_t id)
{
    g_waylandInputSerial = serial;
    const auto iterator = g_waylandTouchContacts.find(id);
    if (iterator == g_waylandTouchContacts.end()) return;
    const WaylandTouchContact contact = iterator->second;
    DispatchWaylandTouch(
        contact.window, JALIUM_EVENT_POINTER_UP, id,
        contact.x, contact.y, 0.0f, contact.primary);
    const bool wasPrimary = contact.primary;
    g_waylandTouchContacts.erase(iterator);
    if (wasPrimary) PromoteNextWaylandPrimaryTouch();
}

static void HandleTouchMotion(
    void*, wl_touch*, uint32_t, int32_t id, wl_fixed_t x, wl_fixed_t y)
{
    const auto iterator = g_waylandTouchContacts.find(id);
    if (iterator == g_waylandTouchContacts.end()) return;
    WaylandTouchContact& contact = iterator->second;
    const float scale = static_cast<float>(
        contact.window && contact.window->waylandScale > 0
            ? contact.window->waylandScale : 1);
    contact.x = static_cast<float>(wl_fixed_to_double(x)) * scale;
    contact.y = static_cast<float>(wl_fixed_to_double(y)) * scale;
    DispatchWaylandTouch(
        contact.window, JALIUM_EVENT_POINTER_MOVE, id,
        contact.x, contact.y, 1.0f, contact.primary);
}

static void HandleTouchFrame(void*, wl_touch*) {}

static void HandleTouchCancel(void*, wl_touch*)
{
    for (const auto& [id, contact] : g_waylandTouchContacts)
    {
        DispatchWaylandTouch(
            contact.window, JALIUM_EVENT_POINTER_CANCEL, id,
            contact.x, contact.y, 0.0f, contact.primary);
    }
    g_waylandTouchContacts.clear();
    g_waylandTouchOrder = 0;
}

#ifdef WL_TOUCH_SHAPE_SINCE_VERSION
static void HandleTouchShape(void*, wl_touch*, int32_t, wl_fixed_t, wl_fixed_t) {}
#endif
#ifdef WL_TOUCH_ORIENTATION_SINCE_VERSION
static void HandleTouchOrientation(void*, wl_touch*, int32_t, wl_fixed_t) {}
#endif

static const wl_touch_listener g_touchListener = {
    HandleTouchDown,
    HandleTouchUp,
    HandleTouchMotion,
    HandleTouchFrame,
    HandleTouchCancel
#ifdef WL_TOUCH_SHAPE_SINCE_VERSION
    , HandleTouchShape
#endif
#ifdef WL_TOUCH_ORIENTATION_SINCE_VERSION
    , HandleTouchOrientation
#endif
};

static void CancelWaylandTouchesForWindow(JaliumPlatformWindow* window)
{
    bool removedPrimary = false;
    for (auto iterator = g_waylandTouchContacts.begin();
         iterator != g_waylandTouchContacts.end();)
    {
        if (iterator->second.window == window)
        {
            DispatchWaylandTouch(
                window, JALIUM_EVENT_POINTER_CANCEL, iterator->first,
                iterator->second.x, iterator->second.y, 0.0f,
                iterator->second.primary);
            removedPrimary |= iterator->second.primary;
            iterator = g_waylandTouchContacts.erase(iterator);
        }
        else
        {
            ++iterator;
        }
    }
    if (removedPrimary) PromoteNextWaylandPrimaryTouch();
}

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
    if ((capabilities & WL_SEAT_CAPABILITY_TOUCH) && !g_waylandTouch)
    {
        g_waylandTouch = wl_seat_get_touch(seat);
        wl_touch_add_listener(g_waylandTouch, &g_touchListener, nullptr);
    }
    else if (!(capabilities & WL_SEAT_CAPABILITY_TOUCH) && g_waylandTouch)
    {
        HandleTouchCancel(nullptr, g_waylandTouch);
        wl_touch_destroy(g_waylandTouch);
        g_waylandTouch = nullptr;
    }
}

static void HandleSeatName(void*, wl_seat*, const char*) {}
static const wl_seat_listener g_seatListener = { HandleSeatCapabilities, HandleSeatName };

static void HandleOutputGeometry(void* data, wl_output*, int32_t x, int32_t y,
                                 int32_t /*physW*/, int32_t /*physH*/, int32_t /*subpixel*/,
                                 const char*, const char*, int32_t /*transform*/)
{
    auto* info = static_cast<WaylandOutputInfo*>(data);
    info->x = x;
    info->y = y;
}

static void HandleOutputMode(void* data, wl_output*, uint32_t flags,
                             int32_t width, int32_t height, int32_t refresh)
{
    if ((flags & WL_OUTPUT_MODE_CURRENT) == 0)
        return;
    auto* info = static_cast<WaylandOutputInfo*>(data);
    info->width = width;
    info->height = height;
    info->refreshMilliHz = refresh;
}

static void HandleOutputDone(void*, wl_output*)
{
    DispatchWaylandMonitorsChanged();
}

static void HandleOutputScale(void* data, wl_output*, int32_t factor)
{
    auto* info = static_cast<WaylandOutputInfo*>(data);
    info->scale = factor > 0 ? factor : 1;
    UpdateWaylandOutputScale(info->registryName, info->scale);
}

// Trailing name/description members (wl_output v4 headers) stay null via
// aggregate initialization, which older v3 headers simply don't have.
static const wl_output_listener g_outputListener = {
    HandleOutputGeometry,
    HandleOutputMode,
    HandleOutputDone,
    HandleOutputScale,
};

#ifdef JALIUM_HAS_XDG_TOPLEVEL_ICON_V1
static void HandleWaylandToplevelIconSize(
    void*, xdg_toplevel_icon_manager_v1*, int32_t)
{
}

static void HandleWaylandToplevelIconDone(
    void*, xdg_toplevel_icon_manager_v1*)
{
}

static const xdg_toplevel_icon_manager_v1_listener
    g_waylandToplevelIconManagerListener = {
        HandleWaylandToplevelIconSize,
        HandleWaylandToplevelIconDone,
    };
#endif

static uint32_t WaylandBindVersion(
    uint32_t advertised, const wl_interface& generatedInterface,
    uint32_t implementationMaximum)
{
    // The compositor may advertise a newer protocol than the XML used by the
    // build host's wayland-scanner. Binding that newer version lets the server
    // send event opcodes absent from the generated listener table and causes a
    // fatal "interface has no event" disconnect. Cap against both contracts.
    return std::min(advertised,
                    std::min(implementationMaximum,
                             static_cast<uint32_t>(generatedInterface.version)));
}

static void HandleRegistryGlobal(void*, wl_registry* registry, uint32_t name,
                                 const char* interface, uint32_t version)
{
    if (strcmp(interface, wl_compositor_interface.name) == 0)
        g_waylandCompositor = static_cast<wl_compositor*>(wl_registry_bind(
            registry, name, &wl_compositor_interface,
            WaylandBindVersion(version, wl_compositor_interface, 4)));
    else if (strcmp(interface, wl_shm_interface.name) == 0 && !g_waylandShm)
        g_waylandShm = static_cast<wl_shm*>(
            wl_registry_bind(registry, name, &wl_shm_interface, 1));
    else if (strcmp(interface, xdg_wm_base_interface.name) == 0)
    {
        g_xdgWmBase = static_cast<xdg_wm_base*>(wl_registry_bind(
            registry, name, &xdg_wm_base_interface,
            WaylandBindVersion(version, xdg_wm_base_interface, 6)));
        xdg_wm_base_add_listener(g_xdgWmBase, &g_wmBaseListener, nullptr);
    }
#ifdef JALIUM_HAS_XDG_ACTIVATION_V1
    else if (strcmp(interface, xdg_activation_v1_interface.name) == 0 &&
             !g_xdgActivation)
    {
        g_xdgActivation = static_cast<xdg_activation_v1*>(wl_registry_bind(
            registry, name, &xdg_activation_v1_interface,
            WaylandBindVersion(version, xdg_activation_v1_interface, 1)));
    }
#endif
    else if (strcmp(interface, wl_seat_interface.name) == 0 && !g_waylandSeat)
    {
        g_waylandSeat = static_cast<wl_seat*>(wl_registry_bind(
            registry, name, &wl_seat_interface,
            WaylandBindVersion(version, wl_seat_interface, 9)));
        wl_seat_add_listener(g_waylandSeat, &g_seatListener, nullptr);
        EnsureWaylandDataDevice();
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
        EnsureWaylandTextInput();
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
        EnsureWaylandTextInputV1();
#endif
        EnsureWaylandTabletSeat();
    }
    else if (strcmp(interface, wl_data_device_manager_interface.name) == 0 &&
             !g_waylandDataDeviceManager)
    {
        g_waylandDataDeviceManager = static_cast<wl_data_device_manager*>(
            wl_registry_bind(registry, name, &wl_data_device_manager_interface,
                             WaylandBindVersion(
                                 version, wl_data_device_manager_interface, 3)));
        EnsureWaylandDataDevice();
    }
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
    else if (strcmp(interface, zwp_text_input_manager_v3_interface.name) == 0 &&
             !g_waylandTextInputManager)
    {
        g_waylandTextInputManager = static_cast<zwp_text_input_manager_v3*>(
            wl_registry_bind(
                registry, name, &zwp_text_input_manager_v3_interface,
                WaylandBindVersion(version, zwp_text_input_manager_v3_interface, 1)));
        EnsureWaylandTextInput();
    }
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
    else if (strcmp(interface, zwp_text_input_manager_v1_interface.name) == 0 &&
             !g_waylandTextInputManagerV1)
    {
        g_waylandTextInputManagerV1 = static_cast<zwp_text_input_manager_v1*>(
            wl_registry_bind(
                registry, name, &zwp_text_input_manager_v1_interface,
                WaylandBindVersion(version, zwp_text_input_manager_v1_interface, 1)));
        EnsureWaylandTextInputV1();
    }
#endif
#ifdef JALIUM_HAS_XDG_FOREIGN_V2
    else if (strcmp(interface, zxdg_exporter_v2_interface.name) == 0 &&
             !g_waylandExporter)
    {
        g_waylandExporter = static_cast<zxdg_exporter_v2*>(
            wl_registry_bind(registry, name, &zxdg_exporter_v2_interface,
                             WaylandBindVersion(
                                 version, zxdg_exporter_v2_interface, 1)));
    }
#endif
#ifdef JALIUM_HAS_XDG_DECORATION_V1
    else if (strcmp(interface, zxdg_decoration_manager_v1_interface.name) == 0 &&
             !g_waylandDecorationManager)
    {
        g_waylandDecorationManager =
            static_cast<zxdg_decoration_manager_v1*>(
                wl_registry_bind(
                    registry, name, &zxdg_decoration_manager_v1_interface,
                    WaylandBindVersion(
                        version, zxdg_decoration_manager_v1_interface, 2)));
    }
#endif
#ifdef JALIUM_HAS_XDG_TOPLEVEL_ICON_V1
    else if (strcmp(interface, xdg_toplevel_icon_manager_v1_interface.name) == 0 &&
             !g_waylandToplevelIconManager)
    {
        g_waylandToplevelIconManager =
            static_cast<xdg_toplevel_icon_manager_v1*>(
                wl_registry_bind(
                    registry, name, &xdg_toplevel_icon_manager_v1_interface,
                    WaylandBindVersion(
                        version, xdg_toplevel_icon_manager_v1_interface, 1)));
        if (g_waylandToplevelIconManager)
            xdg_toplevel_icon_manager_v1_add_listener(
                g_waylandToplevelIconManager,
                &g_waylandToplevelIconManagerListener, nullptr);
    }
#endif
#ifdef JALIUM_HAS_WAYLAND_TABLET_V2
    else if (strcmp(interface, zwp_tablet_manager_v2_interface.name) == 0 &&
             !g_waylandTabletManager)
    {
        g_waylandTabletManager = static_cast<zwp_tablet_manager_v2*>(
            wl_registry_bind(registry, name, &zwp_tablet_manager_v2_interface,
                             WaylandBindVersion(
                                 version, zwp_tablet_manager_v2_interface, 1)));
        EnsureWaylandTabletSeat();
    }
#endif
    else if (strcmp(interface, wl_output_interface.name) == 0)
    {
        auto* info = new WaylandOutputInfo();
        info->registryName = name;
        info->output = static_cast<wl_output*>(
            wl_registry_bind(
                registry, name, &wl_output_interface,
                WaylandBindVersion(version, wl_output_interface, 3)));
        wl_output_add_listener(info->output, &g_outputListener, info);
        g_waylandOutputs.push_back(info);
    }
}

static void HandleRegistryRemove(void*, wl_registry*, uint32_t name)
{
    for (size_t i = 0; i < g_waylandOutputs.size(); ++i)
    {
        if (g_waylandOutputs[i]->registryName == name)
        {
            RemoveWaylandOutputFromWindows(name);
            wl_output_destroy(g_waylandOutputs[i]->output);
            delete g_waylandOutputs[i];
            g_waylandOutputs.erase(g_waylandOutputs.begin() + static_cast<ptrdiff_t>(i));
            DispatchWaylandMonitorsChanged();
            return;
        }
    }
}
static const wl_registry_listener g_registryListener = { HandleRegistryGlobal, HandleRegistryRemove };

static void ShutdownWayland()
{
    g_pointerFocus = nullptr;
    g_keyboardFocus = nullptr;
    g_waylandDragOffer = nullptr;
    g_waylandDragWindow = nullptr;
    g_waylandDropPending = false;
    g_waylandDragMime.clear();
    CancelWaylandActivationRequests(nullptr);
    for (WaylandOutputInfo* info : g_waylandOutputs)
    {
        wl_output_destroy(info->output);
        delete info;
    }
    g_waylandOutputs.clear();
    if (g_waylandCursorSurface)
    {
        wl_surface_destroy(g_waylandCursorSurface);
        g_waylandCursorSurface = nullptr;
    }
    if (g_waylandCursorTheme)
    {
        wl_cursor_theme_destroy(g_waylandCursorTheme);
        g_waylandCursorTheme = nullptr;
        g_waylandCursorThemeScale = 0;
    }
    g_waylandPointerEnterSerial = 0;
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
#ifdef JALIUM_HAS_XDG_FOREIGN_V2
    if (g_waylandExporter)
    {
        zxdg_exporter_v2_destroy(g_waylandExporter);
        g_waylandExporter = nullptr;
    }
#endif
#ifdef JALIUM_HAS_XDG_ACTIVATION_V1
    if (g_xdgActivation)
    {
        xdg_activation_v1_destroy(g_xdgActivation);
        g_xdgActivation = nullptr;
    }
#endif
#ifdef JALIUM_HAS_XDG_DECORATION_V1
    if (g_waylandDecorationManager)
    {
        zxdg_decoration_manager_v1_destroy(g_waylandDecorationManager);
        g_waylandDecorationManager = nullptr;
    }
#endif
#ifdef JALIUM_HAS_XDG_TOPLEVEL_ICON_V1
    if (g_waylandToplevelIconManager)
    {
        xdg_toplevel_icon_manager_v1_destroy(g_waylandToplevelIconManager);
        g_waylandToplevelIconManager = nullptr;
    }
#endif
#ifdef JALIUM_HAS_WAYLAND_TABLET_V2
    if (g_waylandTabletSeat)
    {
        zwp_tablet_seat_v2_destroy(g_waylandTabletSeat);
        g_waylandTabletSeat = nullptr;
    }
    for (WaylandTabletToolState* state : g_waylandTabletTools)
    {
        if (state->tool) zwp_tablet_tool_v2_destroy(state->tool);
        delete state;
    }
    g_waylandTabletTools.clear();
    if (g_waylandTabletManager)
    {
        zwp_tablet_manager_v2_destroy(g_waylandTabletManager);
        g_waylandTabletManager = nullptr;
    }
#endif
    g_waylandCompositionActive = false;
    if (g_waylandTouch)
    {
        HandleTouchCancel(nullptr, g_waylandTouch);
        wl_touch_destroy(g_waylandTouch);
        g_waylandTouch = nullptr;
    }
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

#ifdef JALIUM_HAS_XINPUT2
static void InitializeXInput2()
{
    g_xinputOpcode = -1;
    g_xinput2Available = false;
    int eventBase = 0;
    int errorBase = 0;
    if (!g_display || !XQueryExtension(
            g_display, "XInputExtension", &g_xinputOpcode,
            &eventBase, &errorBase))
        return;
    int major = 2;
    int minor = 2;
    if (XIQueryVersion(g_display, &major, &minor) != Success ||
        major < 2 || (major == 2 && minor < 2))
    {
        g_xinputOpcode = -1;
        return;
    }
    g_xinput2Available = true;
}

static void SelectXInput2Events(Window window)
{
    if (!g_xinput2Available || !g_display || !window) return;
    unsigned char pointerMask[XIMaskLen(XI_LASTEVENT)]{};
    XISetMask(pointerMask, XI_Motion);
    XISetMask(pointerMask, XI_ButtonPress);
    XISetMask(pointerMask, XI_ButtonRelease);
    XISetMask(pointerMask, XI_Enter);
    XISetMask(pointerMask, XI_Leave);
    XISetMask(pointerMask, XI_TouchBegin);
    XISetMask(pointerMask, XI_TouchUpdate);
    XISetMask(pointerMask, XI_TouchEnd);
    XISetMask(pointerMask, XI_TouchOwnership);
    unsigned char deviceMask[XIMaskLen(XI_LASTEVENT)]{};
    XISetMask(deviceMask, XI_DeviceChanged);
    XIEventMask eventMasks[2]{};
    eventMasks[0].deviceid = XIAllMasterDevices;
    eventMasks[0].mask_len = sizeof(pointerMask);
    eventMasks[0].mask = pointerMask;
    eventMasks[1].deviceid = XIAllDevices;
    eventMasks[1].mask_len = sizeof(deviceMask);
    eventMasks[1].mask = deviceMask;
    (void)XISelectEvents(g_display, window, eventMasks, 2);
}
#else
static void InitializeXInput2() {}
static void SelectXInput2Events(Window) {}
#endif

#ifdef JALIUM_HAS_XRANDR
static void InitializeXRandR()
{
    g_xrandrEventBase = -1;
    g_xrandrErrorBase = -1;
    g_xrandrAvailable = false;
    g_xrandr13Available = false;
    g_xrandrMonitorObjectsAvailable = false;
    if (!g_display || !XRRQueryExtension(
            g_display, &g_xrandrEventBase, &g_xrandrErrorBase))
        return;
    int major = 1;
    int minor = 5;
    if (!XRRQueryVersion(g_display, &major, &minor) ||
        major < 1 || (major == 1 && minor < 2))
        return;
    XRRSelectInput(
        g_display, g_rootWindow,
        RRScreenChangeNotifyMask |
        RRCrtcChangeNotifyMask |
        RROutputChangeNotifyMask);
    g_xrandrAvailable = true;
    g_xrandr13Available = major > 1 || (major == 1 && minor >= 3);
    g_xrandrMonitorObjectsAvailable =
        major > 1 || (major == 1 && minor >= 5);
}
#else
static void InitializeXRandR() {}
#endif

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
    InitializeXInput2();
    InitializeXRandR();
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
    LoadDoubleClickSettings();
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
            if (window->x11Colormap && g_display)
            {
                XFreeColormap(g_display, window->x11Colormap);
                window->x11Colormap = 0;
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
#ifdef JALIUM_HAS_XINPUT2
    g_xinputTouchContacts.clear();
    g_xinputTouchOrder = 0;
    g_xinputPenAxes.clear();
    g_xinputScrollAxes.clear();
    g_xinputOpcode = -1;
    g_xinput2Available = false;
#endif
#ifdef JALIUM_HAS_XRANDR
    g_xrandrEventBase = -1;
    g_xrandrErrorBase = -1;
    g_xrandrAvailable = false;
    g_xrandr13Available = false;
    g_xrandrMonitorObjectsAvailable = false;
#endif
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
        g_clipboardItems.clear();
        g_clipboardIncrTransfers.clear();
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
    win->parentHandle = params->parentHandle;

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
        // enter/leave drive per-output HiDPI scale; listener data == user data.
        wl_surface_add_listener(win->waylandSurface, &g_surfaceListener, win);
        if (!CreateWaylandRole(win))
        {
            wl_surface_destroy(win->waylandSurface);
            delete win;
            return nullptr;
        }
        // xdg_popup already receives its parent xdg_surface in
        // CreateWaylandRole. Only toplevel transients use set_parent; calling
        // it with a popup's null xdgToplevel dereferences a null wl_proxy.
        if (win->parentHandle != 0 && win->xdgToplevel)
        {
            auto* parentSurface = reinterpret_cast<wl_surface*>(win->parentHandle);
            std::lock_guard<std::mutex> lock(g_windowMapMutex);
            for (JaliumPlatformWindow* candidate : g_waylandWindows)
            {
                if (candidate && candidate->waylandSurface == parentSurface && candidate->xdgToplevel)
                {
                    xdg_toplevel_set_parent(win->xdgToplevel, candidate->xdgToplevel);
                    wl_surface_commit(win->waylandSurface);
                    break;
                }
            }
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
                     EnterWindowMask | LeaveWindowMask |
                     PropertyChangeMask; // _NET_WM_STATE changes → STATE_CHANGED
    swa.background_pixel = (params->style & JALIUM_WINDOW_STYLE_TRANSPARENT)
        ? 0 : BlackPixel(g_display, g_screen);

    unsigned long valueMask = CWEventMask | CWBackPixel;

    Visual* visual = CopyFromParent;
    int depth = CopyFromParent;
    if (params->style & JALIUM_WINDOW_STYLE_TRANSPARENT)
    {
        XVisualInfo visualInfo{};
        if (XMatchVisualInfo(g_display, g_screen, 32, TrueColor, &visualInfo))
        {
            visual = visualInfo.visual;
            depth = visualInfo.depth;
            win->x11Colormap = XCreateColormap(
                g_display, g_rootWindow, visualInfo.visual, AllocNone);
            swa.colormap = win->x11Colormap;
            swa.border_pixel = 0;
            valueMask |= CWColormap | CWBorderPixel;
        }
    }

    // Override redirect for popup windows
    if (params->style & JALIUM_WINDOW_STYLE_POPUP)
    {
        swa.override_redirect = True;
        valueMask |= CWOverrideRedirect;
    }

    int32_t createX = win->x;
    int32_t createY = win->y;
    if ((params->style & JALIUM_WINDOW_STYLE_POPUP) && win->parentHandle != 0)
    {
        Window ignored = 0;
        XTranslateCoordinates(
            g_display, static_cast<Window>(win->parentHandle), g_rootWindow,
            win->x, win->y, &createX, &createY, &ignored);
    }

    win->xwindow = XCreateWindow(
        g_display, g_rootWindow,
        createX, createY, win->width, win->height,
        0,
        depth, InputOutput, visual,
        valueMask, &swa
    );

    if (!win->xwindow)
    {
        if (win->x11Colormap)
            XFreeColormap(g_display, win->x11Colormap);
        delete win;
        return nullptr;
    }

    X11MonitorMetrics initialMonitor{};
    if (GetX11MonitorMetricsForRect(
            createX, createY, win->width, win->height, initialMonitor))
        win->dpiScale = initialMonitor.scale;

    SelectXInput2Events(win->xwindow);

    if (params->style & JALIUM_WINDOW_STYLE_POPUP)
    {
        // Popup coordinates are part of the cross-platform ABI as
        // parent-relative physical pixels (matching xdg_positioner).
        win->x = params->x == JALIUM_DEFAULT_POS ? 0 : params->x;
        win->y = params->y == JALIUM_DEFAULT_POS ? 0 : params->y;
    }
    else
    {
        win->x = createX;
        win->y = createY;
    }

    if (win->parentHandle != 0)
        XSetTransientForHint(g_display, win->xwindow, static_cast<Window>(win->parentHandle));

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
        CancelWaylandTouchesForWindow(window);
        CancelWaylandTabletToolsForWindow(window);
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

    CancelXInputContactsForWindow(window);
    if (window->x11PopupPointerGrabbed && g_display)
        XUngrabPointer(g_display, CurrentTime);
    if (window->x11PopupKeyboardGrabbed && g_display)
        XUngrabKeyboard(g_display, CurrentTime);
    {
        std::lock_guard<std::mutex> lock(g_windowMapMutex);
        g_windowMap.erase(window->xwindow);
    }

    if (window->xic)
        XDestroyIC(window->xic);

    if (window->xwindow && g_display)
        XDestroyWindow(g_display, window->xwindow);
    if (window->x11Colormap && g_display)
        XFreeColormap(g_display, window->x11Colormap);

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
        if (window->style & JALIUM_WINDOW_STYLE_POPUP_GRAB)
        {
            const int pointerResult = XGrabPointer(
                g_display, window->xwindow, True,
                ButtonPressMask | ButtonReleaseMask | PointerMotionMask,
                GrabModeAsync, GrabModeAsync, None, None, CurrentTime);
            window->x11PopupPointerGrabbed = pointerResult == GrabSuccess;
            const int keyboardResult = XGrabKeyboard(
                g_display, window->xwindow, True,
                GrabModeAsync, GrabModeAsync, CurrentTime);
            window->x11PopupKeyboardGrabbed = keyboardResult == GrabSuccess;
        }
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
        if (window->x11PopupPointerGrabbed)
        {
            XUngrabPointer(g_display, CurrentTime);
            window->x11PopupPointerGrabbed = false;
        }
        if (window->x11PopupKeyboardGrabbed)
        {
            XUngrabKeyboard(g_display, CurrentTime);
            window->x11PopupKeyboardGrabbed = false;
        }
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
        // Requested size is physical pixels; keep the logical mirror in sync
        // so a later scale change re-derives the right physical size.
        window->waylandLogicalWidth = width / window->waylandScale;
        window->waylandLogicalHeight = height / window->waylandScale;
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
        if (window->xdgPopup)
        {
#ifdef XDG_POPUP_REPOSITION_SINCE_VERSION
            if (xdg_popup_get_version(window->xdgPopup) >= XDG_POPUP_REPOSITION_SINCE_VERSION)
            {
                if (xdg_positioner* positioner = CreatePopupPositioner(window))
                {
                    xdg_popup_reposition(
                        window->xdgPopup, positioner, ++window->xdgPopupRepositionToken);
                    xdg_positioner_destroy(positioner);
                    wl_surface_commit(window->waylandSurface);
                    wl_display_flush(g_waylandDisplay);
                }
            }
#endif
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
        // Toplevels intentionally have no absolute positioning API. xdg_popup
        // positions are parent-relative and may be updated with reposition(v3).
        window->x = x;
        window->y = y;
        if (window->xdgPopup)
        {
#ifdef XDG_POPUP_REPOSITION_SINCE_VERSION
            if (xdg_popup_get_version(window->xdgPopup) >= XDG_POPUP_REPOSITION_SINCE_VERSION)
            {
                if (xdg_positioner* positioner = CreatePopupPositioner(window))
                {
                    xdg_popup_reposition(
                        window->xdgPopup, positioner, ++window->xdgPopupRepositionToken);
                    xdg_positioner_destroy(positioner);
                    wl_surface_commit(window->waylandSurface);
                    wl_display_flush(g_waylandDisplay);
                }
            }
#endif
        }
        return;
    }
#endif
    if (window && g_display)
    {
        int32_t targetX = x;
        int32_t targetY = y;
        if ((window->style & JALIUM_WINDOW_STYLE_POPUP) && window->parentHandle != 0)
        {
            Window ignored = 0;
            XTranslateCoordinates(
                g_display, static_cast<Window>(window->parentHandle), g_rootWindow,
                x, y, &targetX, &targetY, &ignored);
        }
        XMoveWindow(g_display, window->xwindow, targetX, targetY);
        XFlush(g_display);
    }
}

void jalium_window_set_state(JaliumPlatformWindow* window, JaliumWindowState state)
{
#ifdef JALIUM_HAS_WAYLAND
    if (window && window->waylandSurface)
    {
        if ((!window->xdgToplevel && !window->xdgPopup) && !CreateWaylandRole(window)) return;
        if (window->xdgPopup) return;
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
    if (!window || !g_display || !window->xwindow)
        return JALIUM_WINDOW_STATE_NORMAL;

    Atom netWmState = XInternAtom(g_display, "_NET_WM_STATE", False);
    Atom netMaxH = XInternAtom(g_display, "_NET_WM_STATE_MAXIMIZED_HORZ", False);
    Atom netMaxV = XInternAtom(g_display, "_NET_WM_STATE_MAXIMIZED_VERT", False);
    Atom netFullscreen = XInternAtom(g_display, "_NET_WM_STATE_FULLSCREEN", False);
    Atom netHidden = XInternAtom(g_display, "_NET_WM_STATE_HIDDEN", False);

    bool maximizedH = false, maximizedV = false, fullscreen = false, hidden = false;

    Atom actualType = None;
    int actualFormat = 0;
    unsigned long itemCount = 0, bytesAfter = 0;
    unsigned char* data = nullptr;
    if (XGetWindowProperty(g_display, window->xwindow, netWmState, 0, 64, False,
                           XA_ATOM, &actualType, &actualFormat, &itemCount,
                           &bytesAfter, &data) == Success && data)
    {
        const Atom* atoms = reinterpret_cast<const Atom*>(data);
        for (unsigned long i = 0; i < itemCount; ++i)
        {
            if (atoms[i] == netMaxH) maximizedH = true;
            else if (atoms[i] == netMaxV) maximizedV = true;
            else if (atoms[i] == netFullscreen) fullscreen = true;
            else if (atoms[i] == netHidden) hidden = true;
        }
        XFree(data);
    }

    // WM_STATE IconicState covers WMs that iconify without _NET_WM_STATE_HIDDEN.
    if (!hidden)
    {
        Atom wmState = XInternAtom(g_display, "WM_STATE", False);
        if (XGetWindowProperty(g_display, window->xwindow, wmState, 0, 2, False,
                               wmState, &actualType, &actualFormat, &itemCount,
                               &bytesAfter, &data) == Success && data)
        {
            if (itemCount >= 1)
            {
                constexpr long kIconicState = 3;
                const long* stateData = reinterpret_cast<const long*>(data);
                if (stateData[0] == kIconicState)
                    hidden = true;
            }
            XFree(data);
        }
    }

    if (hidden) return JALIUM_WINDOW_STATE_MINIMIZED;
    if (fullscreen) return JALIUM_WINDOW_STATE_FULLSCREEN;
    if (maximizedH && maximizedV) return JALIUM_WINDOW_STATE_MAXIMIZED;
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

static uint32_t CopyPortalParentHandle(
    JaliumPlatformWindow* window, char* utf8Buffer, uint32_t bufferSize)
{
#ifdef JALIUM_HAS_XDG_FOREIGN_V2
    if (window && window->waylandSurface)
    {
        EnsureWaylandPortalExport(window);
        if (window->portalExport && window->portalParentToken.empty() &&
            g_waylandDisplay)
        {
            // exported(handle) is asynchronous. Portal APIs need a complete
            // parent string synchronously, so finish this one-time handshake
            // before returning the required UTF-8 buffer size.
            wl_display_flush(g_waylandDisplay);
            (void)wl_display_roundtrip(g_waylandDisplay);
        }
        if (!window->portalParentToken.empty())
        {
            const std::string value = "wayland:" + window->portalParentToken;
            const uint32_t required = static_cast<uint32_t>(value.size() + 1);
            if (utf8Buffer && bufferSize >= required)
                memcpy(utf8Buffer, value.c_str(), required);
            return required;
        }
        return 0;
    }
#endif
    if (window && window->xwindow)
    {
        char value[2 + 1 + sizeof(Window) * 2 + 1]{};
        const int written = snprintf(
            value, sizeof(value), "x11:%lx",
            static_cast<unsigned long>(window->xwindow));
        if (written <= 0) return 0;
        const uint32_t required = static_cast<uint32_t>(written + 1);
        if (utf8Buffer && bufferSize >= required)
            memcpy(utf8Buffer, value, required);
        return required;
    }
    return 0;
}

uint32_t jalium_window_get_portal_parent_handle(
    JaliumPlatformWindow* window, char* utf8Buffer, uint32_t bufferSize)
{
    return CopyPortalParentHandle(window, utf8Buffer, bufferSize);
}

uint32_t jalium_window_get_portal_parent_handle_for_native_handle(
    intptr_t nativeHandle, char* utf8Buffer, uint32_t bufferSize)
{
    if (nativeHandle == 0) return 0;
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        return CopyPortalParentHandle(
            FindWaylandWindowBySurface(
                reinterpret_cast<wl_surface*>(nativeHandle)),
            utf8Buffer, bufferSize);
    }
#endif
    if (g_windowSystem == LinuxWindowSystem::XServer)
        return CopyPortalParentHandle(
            FindWindow(static_cast<Window>(nativeHandle)),
            utf8Buffer, bufferSize);
    return 0;
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
        if (!g_waylandPointer || !g_waylandCompositor || !g_waylandShm)
            return;

        if (cursor == JALIUM_CURSOR_HIDDEN)
        {
            wl_pointer_set_cursor(g_waylandPointer, g_waylandPointerEnterSerial,
                                  nullptr, 0, 0);
            wl_display_flush(g_waylandDisplay);
            return;
        }

        const int scale = window->waylandScale > 0 ? window->waylandScale : 1;
        if (!g_waylandCursorTheme || g_waylandCursorThemeScale != scale)
        {
            if (g_waylandCursorTheme)
                wl_cursor_theme_destroy(g_waylandCursorTheme);
            const char* themeName = getenv("XCURSOR_THEME");
            int themeSize = 24;
            if (const char* sizeEnv = getenv("XCURSOR_SIZE"))
            {
                const int parsed = atoi(sizeEnv);
                if (parsed > 0) themeSize = parsed;
            }
            g_waylandCursorTheme =
                wl_cursor_theme_load(themeName, themeSize * scale, g_waylandShm);
            g_waylandCursorThemeScale = scale;
        }
        if (!g_waylandCursorTheme)
            return;

        // Try the XDG cursor-spec name first, then the legacy X11 name.
        const char* primary = "default";
        const char* fallback = "left_ptr";
        switch (cursor)
        {
        case JALIUM_CURSOR_ARROW:       primary = "default";     fallback = "left_ptr"; break;
        case JALIUM_CURSOR_HAND:        primary = "pointer";     fallback = "hand2"; break;
        case JALIUM_CURSOR_IBEAM:       primary = "text";        fallback = "xterm"; break;
        case JALIUM_CURSOR_CROSSHAIR:   primary = "crosshair";   fallback = "cross"; break;
        case JALIUM_CURSOR_RESIZE_NS:   primary = "ns-resize";   fallback = "sb_v_double_arrow"; break;
        case JALIUM_CURSOR_RESIZE_EW:   primary = "ew-resize";   fallback = "sb_h_double_arrow"; break;
        case JALIUM_CURSOR_RESIZE_NESW: primary = "nesw-resize"; fallback = "fd_double_arrow"; break;
        case JALIUM_CURSOR_RESIZE_NWSE: primary = "nwse-resize"; fallback = "bd_double_arrow"; break;
        case JALIUM_CURSOR_RESIZE_ALL:  primary = "move";        fallback = "fleur"; break;
        case JALIUM_CURSOR_NOT_ALLOWED: primary = "not-allowed"; fallback = "crossed_circle"; break;
        case JALIUM_CURSOR_WAIT:        primary = "wait";        fallback = "watch"; break;
        default: break;
        }

        wl_cursor* themed = wl_cursor_theme_get_cursor(g_waylandCursorTheme, primary);
        if (!themed) themed = wl_cursor_theme_get_cursor(g_waylandCursorTheme, fallback);
        if (!themed) themed = wl_cursor_theme_get_cursor(g_waylandCursorTheme, "left_ptr");
        if (!themed || themed->image_count == 0)
            return;

        wl_cursor_image* image = themed->images[0];
        wl_buffer* buffer = wl_cursor_image_get_buffer(image);
        if (!buffer)
            return;

        if (!g_waylandCursorSurface)
            g_waylandCursorSurface = wl_compositor_create_surface(g_waylandCompositor);
        if (!g_waylandCursorSurface)
            return;

        wl_surface_set_buffer_scale(g_waylandCursorSurface, scale);
        wl_surface_attach(g_waylandCursorSurface, buffer, 0, 0);
        wl_surface_damage(g_waylandCursorSurface, 0, 0,
                          static_cast<int32_t>(image->width),
                          static_cast<int32_t>(image->height));
        wl_surface_commit(g_waylandCursorSurface);
        wl_pointer_set_cursor(g_waylandPointer, g_waylandPointerEnterSerial,
                              g_waylandCursorSurface,
                              static_cast<int32_t>(image->hotspot_x) / scale,
                              static_cast<int32_t>(image->hotspot_y) / scale);
        wl_display_flush(g_waylandDisplay);
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

    if ((window->style & JALIUM_WINDOW_STYLE_POPUP) && window->parentHandle != 0)
    {
        if (x) *x = window->x;
        if (y) *y = window->y;
        return;
    }

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

static bool ComputeSmoothScrollDelta(
    double previousValue,
    double currentValue,
    double increment,
    bool vertical,
    float& deltaX,
    float& deltaY)
{
    if (!std::isfinite(previousValue) || !std::isfinite(currentValue) ||
        !std::isfinite(increment) || std::abs(increment) < 1e-12)
        return false;

    const double units = (currentValue - previousValue) / increment;
    // XI2 scroll valuators are absolute and may wrap/reset at their numeric
    // limit. Treat an implausible jump as a new baseline rather than scrolling
    // the application by thousands of pages.
    if (!std::isfinite(units) || std::abs(units) > 100.0)
        return false;

    if (vertical)
        deltaY -= static_cast<float>(units); // positive XI units mean down
    else
        deltaX += static_cast<float>(units); // positive XI units mean right
    return std::abs(units) > 1e-6;
}

#ifdef JALIUM_HAS_XINPUT2
static bool ContainsInsensitive(std::string value, const char* needle)
{
    std::transform(value.begin(), value.end(), value.begin(),
                   [](unsigned char character) {
                       return static_cast<char>(std::tolower(character));
                   });
    return value.find(needle) != std::string::npos;
}

static XInputPenAxes& GetXInputPenAxes(int sourceId)
{
    const auto cached = g_xinputPenAxes.find(sourceId);
    if (cached != g_xinputPenAxes.end()) return cached->second;

    XInputPenAxes axes{};
    int deviceCount = 0;
    XIDeviceInfo* devices = XIQueryDevice(g_display, sourceId, &deviceCount);
    if (devices && deviceCount > 0)
    {
        const std::string name = devices[0].name ? devices[0].name : "";
        axes.isPen = ContainsInsensitive(name, "stylus") ||
                     ContainsInsensitive(name, "pen") ||
                     ContainsInsensitive(name, "eraser") ||
                     ContainsInsensitive(name, "tablet tool") ||
                     ContainsInsensitive(name, "airbrush") ||
                     ContainsInsensitive(name, "puck") ||
                     ContainsInsensitive(name, "lens") ||
                     (ContainsInsensitive(name, "tablet") &&
                      ContainsInsensitive(name, "cursor"));
        if (ContainsInsensitive(name, "eraser"))
        {
            axes.toolType = JALIUM_POINTER_TOOL_ERASER;
            axes.inverted = true;
        }
        else if (ContainsInsensitive(name, "airbrush"))
            axes.toolType = JALIUM_POINTER_TOOL_AIRBRUSH;
        else if (ContainsInsensitive(name, "brush"))
            axes.toolType = JALIUM_POINTER_TOOL_BRUSH;
        else if (ContainsInsensitive(name, "pencil"))
            axes.toolType = JALIUM_POINTER_TOOL_PENCIL;
        else if (ContainsInsensitive(name, "lens"))
            axes.toolType = JALIUM_POINTER_TOOL_LENS;
        else if (ContainsInsensitive(name, "puck") ||
                 ContainsInsensitive(name, "cursor"))
            axes.toolType = JALIUM_POINTER_TOOL_MOUSE;
        else
            axes.toolType = JALIUM_POINTER_TOOL_PEN;
        for (int index = 0; index < devices[0].num_classes; ++index)
        {
            XIAnyClassInfo* any = devices[0].classes[index];
            if (!any || any->type != XIValuatorClass) continue;
            auto* valuator = reinterpret_cast<XIValuatorClassInfo*>(any);
            char* atomName = valuator->label != None
                ? XGetAtomName(g_display, valuator->label) : nullptr;
            const std::string label = atomName ? atomName : "";
            if (atomName) XFree(atomName);
            if (ContainsInsensitive(label, "pressure"))
            {
                axes.pressure = valuator->number;
                axes.pressureMin = valuator->min;
                axes.pressureMax = valuator->max;
            }
            else if (ContainsInsensitive(label, "tilt x") ||
                     ContainsInsensitive(label, "tilt_x"))
            {
                axes.tiltX = valuator->number;
                axes.tiltXMin = valuator->min;
                axes.tiltXMax = valuator->max;
            }
            else if (ContainsInsensitive(label, "tilt y") ||
                     ContainsInsensitive(label, "tilt_y"))
            {
                axes.tiltY = valuator->number;
                axes.tiltYMin = valuator->min;
                axes.tiltYMax = valuator->max;
            }
            else if (ContainsInsensitive(label, "rotation") ||
                     ContainsInsensitive(label, "twist"))
            {
                axes.rotation = valuator->number;
                axes.rotationMin = valuator->min;
                axes.rotationMax = valuator->max;
            }
        }
        // Some drivers expose a generic device name but provide unmistakable
        // pressure/tilt axes. Classify those as pen only from the actual
        // device classes; never promote an ordinary mouse by name alone.
        axes.isPen |= axes.pressure >= 0 &&
            (axes.tiltX >= 0 || axes.tiltY >= 0 ||
             ContainsInsensitive(name, "tablet"));
    }
    if (devices) XIFreeDeviceInfo(devices);
    return g_xinputPenAxes.emplace(sourceId, axes).first->second;
}

static bool TryGetXInputValuator(
    const XIValuatorState& valuators, int axis, double& result);

static std::vector<XInputScrollAxis>& GetXInputScrollAxes(int sourceId)
{
    const auto cached = g_xinputScrollAxes.find(sourceId);
    if (cached != g_xinputScrollAxes.end()) return cached->second;

    std::vector<XInputScrollAxis> axes;
    int deviceCount = 0;
    XIDeviceInfo* devices = XIQueryDevice(g_display, sourceId, &deviceCount);
    if (devices && deviceCount > 0)
    {
        for (int index = 0; index < devices[0].num_classes; ++index)
        {
            XIAnyClassInfo* any = devices[0].classes[index];
            if (!any || any->type != XIScrollClass) continue;
            auto* scroll = reinterpret_cast<XIScrollClassInfo*>(any);
            if ((scroll->scroll_type != XIScrollTypeVertical &&
                 scroll->scroll_type != XIScrollTypeHorizontal) ||
                !std::isfinite(scroll->increment) ||
                std::abs(scroll->increment) < 1e-12)
                continue;
            axes.push_back(XInputScrollAxis{
                scroll->number,
                scroll->scroll_type,
                scroll->increment,
                0.0,
                false });
        }
    }
    if (devices) XIFreeDeviceInfo(devices);
    return g_xinputScrollAxes.emplace(sourceId, std::move(axes)).first->second;
}

static bool UpdateXInputSmoothScroll(
    int sourceId,
    const XIValuatorState& valuators,
    float& deltaX,
    float& deltaY)
{
    bool changed = false;
    for (XInputScrollAxis& axis : GetXInputScrollAxes(sourceId))
    {
        double currentValue = 0.0;
        if (!TryGetXInputValuator(valuators, axis.number, currentValue))
            continue;

        // Scroll valuators are absolute. The first value after entering a
        // window cannot be compared safely because the device may have moved
        // over another client, so establish a baseline and wait for the next.
        if (!axis.hasPreviousValue)
        {
            axis.previousValue = currentValue;
            axis.hasPreviousValue = true;
            continue;
        }

        changed |= ComputeSmoothScrollDelta(
            axis.previousValue,
            currentValue,
            axis.increment,
            axis.scrollType == XIScrollTypeVertical,
            deltaX,
            deltaY);
        axis.previousValue = currentValue;
    }
    return changed;
}

static void ResetXInputSmoothScroll()
{
    for (auto& [device, axes] : g_xinputScrollAxes)
    {
        (void)device;
        for (XInputScrollAxis& axis : axes)
            axis.hasPreviousValue = false;
    }
}

static bool TryGetXInputValuator(
    const XIValuatorState& valuators, int axis, double& result)
{
    if (axis < 0 || !valuators.mask || !valuators.values) return false;
    const double* current = valuators.values;
    for (int index = 0; index < valuators.mask_len * 8; ++index)
    {
        if (!XIMaskIsSet(valuators.mask, index)) continue;
        if (index == axis)
        {
            result = *current;
            return true;
        }
        ++current;
    }
    return false;
}

static float NormalizeXInputAxis(
    double value, double minimum, double maximum,
    float outputMinimum, float outputMaximum)
{
    if (!std::isfinite(value) || maximum <= minimum) return outputMinimum;
    const double normalized = std::clamp(
        (value - minimum) / (maximum - minimum), 0.0, 1.0);
    return outputMinimum + static_cast<float>(normalized) *
        (outputMaximum - outputMinimum);
}

static void UpdateXInputPenAxes(
    XInputPenAxes& axes, const XIValuatorState& valuators)
{
    double value = 0;
    if (TryGetXInputValuator(valuators, axes.pressure, value))
        axes.currentPressure = NormalizeXInputAxis(
            value, axes.pressureMin, axes.pressureMax, 0.0f, 1.0f);
    if (TryGetXInputValuator(valuators, axes.tiltX, value))
        axes.currentTiltX = NormalizeXInputAxis(
            value, axes.tiltXMin, axes.tiltXMax, -90.0f, 90.0f);
    if (TryGetXInputValuator(valuators, axes.tiltY, value))
        axes.currentTiltY = NormalizeXInputAxis(
            value, axes.tiltYMin, axes.tiltYMax, -90.0f, 90.0f);
    if (TryGetXInputValuator(valuators, axes.rotation, value))
        axes.currentRotation = NormalizeXInputAxis(
            value, axes.rotationMin, axes.rotationMax, 0.0f, 360.0f);
}

static void DispatchXInputPointer(
    JaliumPlatformWindow* window, JaliumEventType type, uint32_t pointerId,
    float x, float y, float pressure, float tiltX, float tiltY, float twist,
    JaliumPointerType pointerType, int32_t modifiers, uint32_t flags,
    int32_t toolType, uint32_t buttons)
{
    if (!window) return;
    JaliumPlatformEvent event{};
    event.type = type;
    event.window = window;
    event.pointer.pointerId = pointerId;
    event.pointer.x = x;
    event.pointer.y = y;
    event.pointer.pressure = pressure;
    event.pointer.tiltX = tiltX;
    event.pointer.tiltY = tiltY;
    event.pointer.twist = twist;
    event.pointer.pointerType = pointerType;
    event.pointer.modifiers = modifiers;
    event.pointer.flags = flags;
    event.pointer.toolType = toolType;
    event.pointer.buttons = buttons;
    window->DispatchEvent(event);
}

static uint32_t XInputPenFlags(const XInputPenAxes& axes)
{
    uint32_t flags = JALIUM_POINTER_FLAG_PRIMARY;
    if (axes.inRange) flags |= JALIUM_POINTER_FLAG_IN_RANGE;
    if (axes.inContact) flags |= JALIUM_POINTER_FLAG_IN_CONTACT;
    if (axes.toolType == JALIUM_POINTER_TOOL_ERASER)
        flags |= JALIUM_POINTER_FLAG_ERASER;
    if (axes.inverted) flags |= JALIUM_POINTER_FLAG_INVERTED;
    if ((axes.buttons & (JALIUM_POINTER_BUTTON_BARREL |
                         JALIUM_POINTER_BUTTON_SECONDARY)) != 0)
        flags |= JALIUM_POINTER_FLAG_BARREL;
    return flags;
}

static JaliumPointerType XInputPointerTypeForTool(const XInputPenAxes& axes)
{
    return axes.toolType == JALIUM_POINTER_TOOL_MOUSE ||
        axes.toolType == JALIUM_POINTER_TOOL_LENS
        ? JALIUM_POINTER_MOUSE : JALIUM_POINTER_PEN;
}

static uint32_t XInputPenButtonMask(int detail)
{
    switch (detail)
    {
    case Button1: return JALIUM_POINTER_BUTTON_PRIMARY;
    case Button2: return JALIUM_POINTER_BUTTON_BARREL;
    case Button3: return JALIUM_POINTER_BUTTON_SECONDARY;
    default: return JALIUM_POINTER_BUTTON_NONE;
    }
}

static void UpdateXInputPenButton(
    XInputPenAxes& axes, int detail, bool pressed)
{
    const uint32_t mask = XInputPenButtonMask(detail);
    if (pressed) axes.buttons |= mask;
    else axes.buttons &= ~mask;
}

static uint32_t XInputPenPointerId(int sourceId)
{
    return 0x80000000u |
        (static_cast<uint32_t>(sourceId) & 0x0fffffffu);
}

static uint64_t XInputTouchKey(int deviceId, uint32_t touchId)
{
    return (static_cast<uint64_t>(static_cast<uint32_t>(deviceId)) << 32) |
        touchId;
}

static uint32_t XInputTouchPointerId(int sourceId, uint32_t touchId)
{
    return 0x20000000u |
        ((static_cast<uint32_t>(sourceId) & 0xffu) << 20) |
        (touchId & 0x000fffffu);
}

static void PromoteNextXInputPrimaryTouch()
{
    XInputTouchContact* next = nullptr;
    for (auto& [key, contact] : g_xinputTouchContacts)
    {
        (void)key;
        if (!next || contact.order < next->order)
            next = &contact;
    }
    if (next) next->primary = true;
}

static bool ProcessXInputEvent(XEvent& xev)
{
    if (!g_xinput2Available || xev.type != GenericEvent ||
        xev.xcookie.extension != g_xinputOpcode)
        return false;
    if (!XGetEventData(g_display, &xev.xcookie)) return true;

    const int eventType = xev.xcookie.evtype;
    if (eventType == XI_TouchOwnership)
    {
        auto* ownership = static_cast<XITouchOwnershipEvent*>(xev.xcookie.data);
        if (ownership)
            (void)XIAllowTouchEvents(
                g_display, ownership->deviceid, ownership->touchid,
                ownership->event, XIAcceptTouch);
        XFreeEventData(g_display, &xev.xcookie);
        return true;
    }

    if (eventType == XI_DeviceChanged)
    {
        auto* changed = static_cast<XIDeviceChangedEvent*>(xev.xcookie.data);
        if (changed)
        {
            const auto removePenState = [](int deviceId) {
                const auto iterator = g_xinputPenAxes.find(deviceId);
                if (iterator == g_xinputPenAxes.end()) return;
                XInputPenAxes& axes = iterator->second;
                if (axes.inContact && axes.window)
                {
                    axes.inContact = false;
                    axes.inRange = false;
                    axes.currentPressure = 0.0f;
                    axes.buttons &= ~JALIUM_POINTER_BUTTON_PRIMARY;
                    DispatchXInputPointer(
                        axes.window, JALIUM_EVENT_POINTER_CANCEL,
                        XInputPenPointerId(deviceId), axes.x, axes.y, 0.0f,
                        axes.currentTiltX, axes.currentTiltY,
                        axes.currentRotation, XInputPointerTypeForTool(axes),
                        JALIUM_MOD_NONE, XInputPenFlags(axes), axes.toolType,
                        axes.buttons);
                }
                g_xinputPenAxes.erase(iterator);
            };
            g_xinputScrollAxes.erase(changed->deviceid);
            g_xinputScrollAxes.erase(changed->sourceid);
            removePenState(changed->deviceid);
            removePenState(changed->sourceid);
        }
        XFreeEventData(g_display, &xev.xcookie);
        return true;
    }

    if (eventType == XI_Enter || eventType == XI_Leave)
    {
        auto* crossing = static_cast<XIEnterEvent*>(xev.xcookie.data);
        if (crossing)
        {
            const int sourceId = crossing->sourceid > 0
                ? crossing->sourceid : crossing->deviceid;
            XInputPenAxes& axes = GetXInputPenAxes(sourceId);
            JaliumPlatformWindow* window = FindWindow(crossing->event);
            if (axes.isPen && (window || axes.window))
            {
                axes.x = static_cast<float>(crossing->event_x);
                axes.y = static_cast<float>(crossing->event_y);
                if (eventType == XI_Enter)
                {
                    axes.window = window;
                    axes.inRange = true;
                    DispatchXInputPointer(
                        axes.window, JALIUM_EVENT_POINTER_MOVE,
                        XInputPenPointerId(sourceId), axes.x, axes.y, 0.0f,
                        axes.currentTiltX, axes.currentTiltY,
                        axes.currentRotation, XInputPointerTypeForTool(axes),
                        GetX11Modifiers(crossing->mods.effective),
                        XInputPenFlags(axes), axes.toolType, axes.buttons);
                }
                else
                {
                    JaliumPlatformWindow* previousWindow = axes.window
                        ? axes.window : window;
                    const bool wasContact = axes.inContact;
                    axes.inContact = false;
                    axes.inRange = false;
                    axes.currentPressure = 0.0f;
                    axes.buttons &= ~JALIUM_POINTER_BUTTON_PRIMARY;
                    DispatchXInputPointer(
                        previousWindow, wasContact
                            ? JALIUM_EVENT_POINTER_CANCEL
                            : JALIUM_EVENT_POINTER_MOVE,
                        XInputPenPointerId(sourceId), axes.x, axes.y, 0.0f,
                        axes.currentTiltX, axes.currentTiltY,
                        axes.currentRotation, XInputPointerTypeForTool(axes),
                        GetX11Modifiers(crossing->mods.effective),
                        XInputPenFlags(axes), axes.toolType, axes.buttons);
                    axes.window = nullptr;
                }
            }
        }
        XFreeEventData(g_display, &xev.xcookie);
        return true;
    }

    auto* device = static_cast<XIDeviceEvent*>(xev.xcookie.data);
    if (!device)
    {
        XFreeEventData(g_display, &xev.xcookie);
        return true;
    }
    JaliumPlatformWindow* window = FindWindow(device->event);
    if (!window)
    {
        XFreeEventData(g_display, &xev.xcookie);
        return true;
    }

    const int32_t modifiers = GetX11Modifiers(device->mods.effective);
    if (eventType == XI_TouchBegin || eventType == XI_TouchUpdate ||
        eventType == XI_TouchEnd)
    {
        const uint32_t touchId = static_cast<uint32_t>(device->detail);
        const int sourceId = device->sourceid > 0
            ? device->sourceid : device->deviceid;
        const uint64_t touchKey = XInputTouchKey(device->deviceid, touchId);
        auto iterator = g_xinputTouchContacts.find(touchKey);
        if (eventType == XI_TouchBegin)
        {
            (void)XIAllowTouchEvents(
                g_display, device->deviceid, touchId,
                device->event, XIAcceptTouch);
            XInputTouchContact contact{};
            contact.window = window;
            contact.pointerId = XInputTouchPointerId(sourceId, touchId);
            contact.x = static_cast<float>(device->event_x);
            contact.y = static_cast<float>(device->event_y);
            contact.order = ++g_xinputTouchOrder;
            contact.primary = g_xinputTouchContacts.empty();
            iterator = g_xinputTouchContacts.emplace(
                touchKey, contact).first;
        }
        else if (iterator != g_xinputTouchContacts.end())
        {
            iterator->second.x = static_cast<float>(device->event_x);
            iterator->second.y = static_cast<float>(device->event_y);
        }
        if (iterator != g_xinputTouchContacts.end())
        {
            const JaliumEventType type = eventType == XI_TouchBegin
                ? JALIUM_EVENT_POINTER_DOWN
                : (eventType == XI_TouchEnd
                    ? JALIUM_EVENT_POINTER_UP : JALIUM_EVENT_POINTER_MOVE);
            DispatchXInputPointer(
                iterator->second.window, type, iterator->second.pointerId,
                iterator->second.x, iterator->second.y,
                eventType == XI_TouchEnd ? 0.0f : 1.0f,
                0.0f, 0.0f, 0.0f, JALIUM_POINTER_TOUCH, modifiers,
                (iterator->second.primary ? JALIUM_POINTER_FLAG_PRIMARY : 0) |
                    (eventType == XI_TouchEnd ? 0 :
                        JALIUM_POINTER_FLAG_IN_RANGE |
                        JALIUM_POINTER_FLAG_IN_CONTACT),
                JALIUM_POINTER_TOOL_UNKNOWN,
                eventType == XI_TouchEnd
                    ? JALIUM_POINTER_BUTTON_NONE
                    : JALIUM_POINTER_BUTTON_PRIMARY);
            if (eventType == XI_TouchEnd)
            {
                const bool wasPrimary = iterator->second.primary;
                g_xinputTouchContacts.erase(iterator);
                if (wasPrimary) PromoteNextXInputPrimaryTouch();
            }
        }
        XFreeEventData(g_display, &xev.xcookie);
        return true;
    }

    if (eventType == XI_Motion || eventType == XI_ButtonPress ||
        eventType == XI_ButtonRelease)
    {
        const int sourceId = device->sourceid > 0
            ? device->sourceid : device->deviceid;
        XInputPenAxes& axes = GetXInputPenAxes(sourceId);
        UpdateXInputPenAxes(axes, device->valuators);
        if (axes.isPen)
        {
            axes.window = window;
            axes.x = static_cast<float>(device->event_x);
            axes.y = static_cast<float>(device->event_y);
            axes.inRange = true;
            JaliumEventType type = JALIUM_EVENT_POINTER_MOVE;
            if (eventType == XI_ButtonPress && device->detail == Button1)
            {
                axes.inContact = true;
                UpdateXInputPenButton(axes, device->detail, true);
                type = JALIUM_EVENT_POINTER_DOWN;
            }
            else if (eventType == XI_ButtonRelease && device->detail == Button1)
            {
                axes.inContact = false;
                axes.currentPressure = 0.0f;
                UpdateXInputPenButton(axes, device->detail, false);
                type = JALIUM_EVENT_POINTER_UP;
            }
            else if (eventType == XI_ButtonPress)
            {
                UpdateXInputPenButton(axes, device->detail, true);
            }
            else if (eventType == XI_ButtonRelease)
            {
                UpdateXInputPenButton(axes, device->detail, false);
            }
            const float pressure = axes.inContact
                ? axes.currentPressure : 0.0f;
            DispatchXInputPointer(
                window, type, XInputPenPointerId(sourceId), axes.x, axes.y,
                pressure, axes.currentTiltX, axes.currentTiltY,
                axes.currentRotation, XInputPointerTypeForTool(axes), modifiers,
                XInputPenFlags(axes), axes.toolType, axes.buttons);
            XFreeEventData(g_display, &xev.xcookie);
            return true;
        }

        // XI2 pointer events replace core pointer delivery. Drop events marked
        // as pointer-emulated because the touch stream above is authoritative
        // and managed code performs its own primary-touch mouse promotion.
        if ((device->flags & XIPointerEmulated) == 0)
        {
            JaliumPlatformEvent event{};
            event.window = window;
            if (eventType == XI_Motion)
            {
                event.type = JALIUM_EVENT_MOUSE_MOVE;
                event.mouse.x = static_cast<float>(device->event_x);
                event.mouse.y = static_cast<float>(device->event_y);
                event.mouse.modifiers = modifiers;
                window->DispatchEvent(event);

                float deltaX = 0.0f;
                float deltaY = 0.0f;
                if (UpdateXInputSmoothScroll(
                        device->sourceid, device->valuators,
                        deltaX, deltaY))
                {
                    JaliumPlatformEvent wheel{};
                    wheel.type = JALIUM_EVENT_MOUSE_WHEEL;
                    wheel.window = window;
                    wheel.wheel.x = static_cast<float>(device->event_x);
                    wheel.wheel.y = static_cast<float>(device->event_y);
                    wheel.wheel.deltaX = deltaX;
                    wheel.wheel.deltaY = deltaY;
                    wheel.wheel.modifiers = modifiers;
                    window->DispatchEvent(wheel);
                }
            }
            else if (device->detail >= Button4 && device->detail <= 7 &&
                     eventType == XI_ButtonPress)
            {
                event.type = JALIUM_EVENT_MOUSE_WHEEL;
                event.wheel.x = static_cast<float>(device->event_x);
                event.wheel.y = static_cast<float>(device->event_y);
                event.wheel.modifiers = modifiers;
                if (device->detail == Button4) event.wheel.deltaY = 1.0f;
                else if (device->detail == Button5) event.wheel.deltaY = -1.0f;
                else if (device->detail == 6) event.wheel.deltaX = -1.0f;
                else event.wheel.deltaX = 1.0f;
                window->DispatchEvent(event);
            }
            else if (!(device->detail >= Button4 && device->detail <= 7))
            {
                event.type = eventType == XI_ButtonPress
                    ? JALIUM_EVENT_MOUSE_DOWN : JALIUM_EVENT_MOUSE_UP;
                event.mouse.x = static_cast<float>(device->event_x);
                event.mouse.y = static_cast<float>(device->event_y);
                event.mouse.button = X11ButtonToJalium(device->detail);
                event.mouse.modifiers = modifiers;
                event.mouse.clickCount = eventType == XI_ButtonPress
                    ? RegisterClick(
                        g_x11ClickTracker, window, event.mouse.button,
                        device->time, event.mouse.x, event.mouse.y)
                    : g_x11ClickTracker.count;
                window->DispatchEvent(event);
            }
        }
    }

    XFreeEventData(g_display, &xev.xcookie);
    return true;
}

static void CancelXInputContactsForWindow(JaliumPlatformWindow* window)
{
    bool removedPrimary = false;
    for (auto iterator = g_xinputTouchContacts.begin();
         iterator != g_xinputTouchContacts.end();)
    {
        if (iterator->second.window == window)
        {
            DispatchXInputPointer(
                window, JALIUM_EVENT_POINTER_CANCEL,
                iterator->second.pointerId,
                iterator->second.x, iterator->second.y, 0.0f,
                0.0f, 0.0f, 0.0f, JALIUM_POINTER_TOUCH,
                JALIUM_MOD_NONE,
                iterator->second.primary ? JALIUM_POINTER_FLAG_PRIMARY : 0,
                JALIUM_POINTER_TOOL_UNKNOWN, JALIUM_POINTER_BUTTON_NONE);
            removedPrimary |= iterator->second.primary;
            iterator = g_xinputTouchContacts.erase(iterator);
        }
        else
        {
            ++iterator;
        }
    }
    if (removedPrimary) PromoteNextXInputPrimaryTouch();

    for (auto& [sourceId, axes] : g_xinputPenAxes)
    {
        if (axes.window != window) continue;
        if (axes.inContact)
        {
            axes.inContact = false;
            axes.inRange = false;
            axes.currentPressure = 0.0f;
            axes.buttons &= ~JALIUM_POINTER_BUTTON_PRIMARY;
            DispatchXInputPointer(
                window, JALIUM_EVENT_POINTER_CANCEL,
                XInputPenPointerId(sourceId), axes.x, axes.y, 0.0f,
                axes.currentTiltX, axes.currentTiltY, axes.currentRotation,
                XInputPointerTypeForTool(axes), JALIUM_MOD_NONE,
                XInputPenFlags(axes),
                axes.toolType, axes.buttons);
        }
        axes.window = nullptr;
        axes.inRange = false;
        axes.inContact = false;
        axes.buttons = JALIUM_POINTER_BUTTON_NONE;
    }
}
#else
static bool ProcessXInputEvent(XEvent&) { return false; }
static void CancelXInputContactsForWindow(JaliumPlatformWindow*) {}
#endif

static bool ProcessXRandREvent(XEvent& event)
{
#ifdef JALIUM_HAS_XRANDR
    if (!g_xrandrAvailable || g_xrandrEventBase < 0)
        return false;
    if (event.type == g_xrandrEventBase + RRScreenChangeNotify)
    {
        XRRUpdateConfiguration(&event);
        RefreshX11DisplayMetrics();
        return true;
    }
    if (event.type == g_xrandrEventBase + RRNotify)
    {
        RefreshX11DisplayMetrics();
        return true;
    }
#else
    (void)event;
#endif
    return false;
}

static void ProcessXEvent(XEvent& xev)
{
    if (ProcessXInputEvent(xev)) return;
    if (ProcessXRandREvent(xev)) return;
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
        {
            const bool sizeChanged =
                xev.xconfigure.width != win->width ||
                xev.xconfigure.height != win->height;
            if (sizeChanged)
            {
                win->width = xev.xconfigure.width;
                win->height = xev.xconfigure.height;
            }

            // Synthetic ConfigureNotify (the WM's move notification) carries
            // root coordinates directly; real events are parent-relative and
            // need a translation round-trip.
            int rootX = xev.xconfigure.x;
            int rootY = xev.xconfigure.y;
            if (!xev.xconfigure.send_event)
            {
                Window child = 0;
                XTranslateCoordinates(g_display, win->xwindow, g_rootWindow,
                                      0, 0, &rootX, &rootY, &child);
            }

            X11MonitorMetrics monitor{};
            const bool dpiChanged = GetX11MonitorMetricsForRect(
                rootX, rootY, win->width, win->height, monitor) &&
                UpdateX11WindowDpi(win, monitor);

            // A DPI transition schedules a scaled XResizeWindow above. Wait
            // for that authoritative ConfigureNotify instead of publishing an
            // intermediate logical size with the new DPI and old pixel size.
            if (sizeChanged && !dpiChanged)
            {
                evt.type = JALIUM_EVENT_RESIZE;
                evt.resize.width = win->width;
                evt.resize.height = win->height;
                win->DispatchEvent(evt);
            }

            int reportedX = rootX;
            int reportedY = rootY;
            if ((win->style & JALIUM_WINDOW_STYLE_POPUP) && win->parentHandle != 0)
            {
                Window child = 0;
                XTranslateCoordinates(
                    g_display, g_rootWindow,
                    static_cast<Window>(win->parentHandle), rootX, rootY,
                    &reportedX, &reportedY, &child);
            }

            if (reportedX != win->x || reportedY != win->y)
            {
                win->x = reportedX;
                win->y = reportedY;
                evt.type = JALIUM_EVENT_MOVE;
                evt.move.x = reportedX;
                evt.move.y = reportedY;
                win->DispatchEvent(evt);
            }
        }
        break;

    case PropertyNotify:
        if (xev.xproperty.atom == XInternAtom(g_display, "_NET_WM_STATE", False))
        {
            JaliumWindowState queried = jalium_window_get_state(win);
            if (queried != win->state)
            {
                win->state = queried;
                evt.type = JALIUM_EVENT_STATE_CHANGED;
                evt.stateChanged.newState = queried;
                win->DispatchEvent(evt);
            }
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
#ifdef JALIUM_HAS_XINPUT2
        if (g_xinput2Available && !xev.xmotion.send_event) break;
#endif
        evt.type = JALIUM_EVENT_MOUSE_MOVE;
        evt.mouse.x = static_cast<float>(xev.xmotion.x);
        evt.mouse.y = static_cast<float>(xev.xmotion.y);
        evt.mouse.modifiers = GetX11Modifiers(xev.xmotion.state);
        win->DispatchEvent(evt);
        break;

    case ButtonPress:
#ifdef JALIUM_HAS_XINPUT2
        if (g_xinput2Available && !xev.xbutton.send_event) break;
#endif
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
            win->lastPressRootX = xev.xbutton.x_root;
            win->lastPressRootY = xev.xbutton.y_root;
            win->lastPressButton = xev.xbutton.button;

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
#ifdef JALIUM_HAS_XINPUT2
        if (g_xinput2Available && !xev.xbutton.send_event) break;
#endif
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
#ifdef JALIUM_HAS_XINPUT2
        // XI2 scroll valuators are absolute. Any movement while the pointer
        // was over another client makes the old baseline invalid.
        ResetXInputSmoothScroll();
#endif
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
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        // Best effort before any surface exists: the first output's scale.
        // Per-window scale is authoritative once wl_surface.enter fires.
        for (const WaylandOutputInfo* info : g_waylandOutputs)
        {
            if (info->scale > 0)
                return static_cast<float>(info->scale);
        }
        return 1.0f;
    }
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
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        const WaylandOutputInfo* selected = nullptr;
        int32_t selectedScale = 0;
        if (window && !window->waylandEnteredOutputs.empty())
        {
            const uint32_t selectedId = SelectWaylandEnteredOutputId(window);
            for (const WaylandOutputInfo* output : g_waylandOutputs)
            {
                if (output->registryName == selectedId)
                {
                    selected = output;
                    selectedScale = std::max(output->scale, 1);
                    break;
                }
            }
        }
        if (!selected)
        {
            for (const WaylandOutputInfo* output : g_waylandOutputs)
            {
                const int32_t scale = std::max(output->scale, 1);
                if (!selected || scale > selectedScale ||
                    (scale == selectedScale &&
                     output->registryName < selected->registryName))
                {
                    selected = output;
                    selectedScale = scale;
                }
            }
        }
        if (selected && selected->refreshMilliHz > 0)
            return (selected->refreshMilliHz + 500) / 1000;
        return 60;
    }
#endif
#ifdef JALIUM_HAS_XRANDR
    X11MonitorMetrics metrics{};
    if ((window && GetX11MonitorMetricsForWindow(window, metrics)) ||
        (!window && GetX11MonitorMetricsByIndex(0, metrics)))
    {
        if (metrics.refreshRate > 0) return metrics.refreshRate;
    }
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

JaliumResult jalium_input_get_touch_capabilities(
    int32_t* touchPresent, int32_t* maxContacts)
{
    if (!touchPresent || !maxContacts)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    *touchPresent = 0;
    *maxContacts = 0;

#ifdef JALIUM_PLATFORM_TEST_HOOKS
    const int32_t testPresent =
        g_testTouchPresent.load(std::memory_order_acquire);
    if (testPresent >= 0)
    {
        *touchPresent = testPresent != 0 ? 1 : 0;
        *maxContacts = *touchPresent != 0
            ? std::max(g_testTouchContacts.load(std::memory_order_acquire), 0)
            : 0;
        return JALIUM_OK;
    }
#endif

#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        // Core wl_touch advertises presence through wl_seat capabilities but
        // deliberately exposes no maximum contact count.
        *touchPresent = g_waylandTouch ? 1 : 0;
        return JALIUM_OK;
    }
#endif

    if (g_windowSystem != LinuxWindowSystem::XServer)
        return JALIUM_ERROR_INVALID_STATE;

#ifdef JALIUM_HAS_XINPUT2
    if (!g_xinput2Available || !g_display)
        return JALIUM_OK;

    int deviceCount = 0;
    XIDeviceInfo* devices = XIQueryDevice(g_display, XIAllDevices, &deviceCount);
    if (!devices)
        return JALIUM_OK;
    for (int deviceIndex = 0; deviceIndex < deviceCount; ++deviceIndex)
    {
        const XIDeviceInfo& device = devices[deviceIndex];
        if (!device.enabled) continue;
        for (int classIndex = 0; classIndex < device.num_classes; ++classIndex)
        {
            XIAnyClassInfo* any = device.classes[classIndex];
            if (!any || any->type != XITouchClass) continue;
            const auto* touch = reinterpret_cast<XITouchClassInfo*>(any);
            *touchPresent = 1;
            *maxContacts = std::max(
                *maxContacts, std::max(static_cast<int32_t>(touch->num_touches), 0));
        }
    }
    XIFreeDeviceInfo(devices);
#endif
    return JALIUM_OK;
}

JaliumResult jalium_platform_set_double_click_settings(
    uint32_t milliseconds, float distance)
{
    if (milliseconds == 0 || milliseconds > 60000 ||
        !std::isfinite(distance) || distance < 0.0f || distance > 16384.0f)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    g_doubleClickMilliseconds = milliseconds;
    g_doubleClickDistance = distance;
    g_x11ClickTracker = {};
#ifdef JALIUM_HAS_WAYLAND
    g_waylandClickTracker = {};
#endif
    return JALIUM_OK;
}

JaliumResult jalium_input_get_cursor_pos(float* x, float* y)
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        // Wayland deliberately exposes only surface-local pointer coordinates.
        // g_pointerX/Y are therefore useful while dispatching that surface's
        // event, but they are not global desktop coordinates and must never be
        // returned through this screen-position ABI.
        if (x) *x = 0.0f;
        if (y) *y = 0.0f;
        return JALIUM_ERROR_NOT_SUPPORTED;
    }
#endif
    if (!x || !y) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (!g_display) { *x = 0.0f; *y = 0.0f; return JALIUM_ERROR_INVALID_STATE; }

    Window root, child;
    int rootX, rootY, winX, winY;
    unsigned int mask;
    if (!XQueryPointer(g_display, g_rootWindow, &root, &child,
                       &rootX, &rootY, &winX, &winY, &mask))
    {
        *x = 0.0f;
        *y = 0.0f;
        return JALIUM_ERROR_INVALID_STATE;
    }
    *x = static_cast<float>(rootX);
    *y = static_cast<float>(rootY);
    return JALIUM_OK;
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

static const OwnedClipboardItem* FindClipboardItemLocked(const char* mimeType)
{
    if (!mimeType) return nullptr;
    const auto iterator = std::find_if(
        g_clipboardItems.begin(), g_clipboardItems.end(),
        [mimeType](const OwnedClipboardItem& item)
        {
            return item.mimeType == mimeType;
        });
    return iterator == g_clipboardItems.end() ? nullptr : &*iterator;
}

static const OwnedClipboardItem* FindClipboardItemLocked(Atom atom)
{
    const auto iterator = std::find_if(
        g_clipboardItems.begin(), g_clipboardItems.end(),
        [atom](const OwnedClipboardItem& item)
        {
            return item.x11Atom == atom;
        });
    return iterator == g_clipboardItems.end() ? nullptr : &*iterator;
}

static const OwnedClipboardItem* FindClipboardTextItemLocked()
{
    constexpr const char* preferred[] = {
        "text/plain;charset=utf-8", "UTF8_STRING", "text/plain"
    };
    for (const char* mimeType : preferred)
        if (const OwnedClipboardItem* item = FindClipboardItemLocked(mimeType))
            return item;
    return nullptr;
}

static void SynchronizeLegacyClipboardTextLocked()
{
    if (const OwnedClipboardItem* item = FindClipboardTextItemLocked())
        g_clipboardUtf8.assign(
            reinterpret_cast<const char*>(item->bytes.data()), item->bytes.size());
    else
        g_clipboardUtf8.clear();
}

static bool StartX11ClipboardTransfer(
    const XSelectionRequestEvent& request, Atom property,
    Atom target, const std::vector<uint8_t>& bytes)
{
    // Keep ordinary properties comfortably below the minimum X11 request
    // limit. Larger payloads use ICCCM INCR and are advanced by requestor
    // PropertyDelete notifications in ProcessClipboardXEvent.
    constexpr size_t directLimit = 64u * 1024u;
    if (bytes.size() <= directLimit)
    {
        XChangeProperty(
            g_display, request.requestor, property, target, 8,
            PropModeReplace, bytes.empty() ? nullptr : bytes.data(),
            static_cast<int>(bytes.size()));
        return true;
    }

    unsigned long totalBytes = static_cast<unsigned long>(bytes.size());
    XSelectInput(g_display, request.requestor, PropertyChangeMask);
    XChangeProperty(
        g_display, request.requestor, property, g_incrAtom, 32,
        PropModeReplace,
        reinterpret_cast<const unsigned char*>(&totalBytes), 1);
    g_clipboardIncrTransfers.push_back(
        X11ClipboardIncrTransfer{request.requestor, property, target, bytes, 0});
    return true;
}

static bool AdvanceX11ClipboardTransfer(const XPropertyEvent& propertyEvent)
{
    if (propertyEvent.state != PropertyDelete) return false;
    std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
    const auto iterator = std::find_if(
        g_clipboardIncrTransfers.begin(), g_clipboardIncrTransfers.end(),
        [&propertyEvent](const X11ClipboardIncrTransfer& transfer)
        {
            return transfer.requestor == propertyEvent.window &&
                   transfer.property == propertyEvent.atom;
        });
    if (iterator == g_clipboardIncrTransfers.end()) return false;

    constexpr size_t chunkSize = 64u * 1024u;
    if (iterator->offset < iterator->bytes.size())
    {
        const size_t count = std::min(
            chunkSize, iterator->bytes.size() - iterator->offset);
        XChangeProperty(
            g_display, iterator->requestor, iterator->property,
            iterator->target, 8, PropModeReplace,
            iterator->bytes.data() + iterator->offset,
            static_cast<int>(count));
        iterator->offset += count;
    }
    else
    {
        XChangeProperty(
            g_display, iterator->requestor, iterator->property,
            iterator->target, 8, PropModeReplace, nullptr, 0);
        g_clipboardIncrTransfers.erase(iterator);
    }
    XFlush(g_display);
    return true;
}

static bool ProcessClipboardXEvent(XEvent& event)
{
    if (!g_display || !g_clipboardWindow) return false;
    if (event.type == PropertyNotify &&
        AdvanceX11ClipboardTransfer(event.xproperty))
        return true;
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
            std::vector<Atom> targets{g_targetsAtom};
            {
                std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
                targets.reserve(g_clipboardItems.size() + 2);
                for (const OwnedClipboardItem& item : g_clipboardItems)
                    if (item.x11Atom != None &&
                        std::find(targets.begin(), targets.end(), item.x11Atom) == targets.end())
                        targets.push_back(item.x11Atom);
                if (FindClipboardTextItemLocked() &&
                    std::find(targets.begin(), targets.end(), XA_STRING) == targets.end())
                    targets.push_back(XA_STRING);
            }
            XChangeProperty(g_display, request.requestor, property, XA_ATOM, 32,
                            PropModeReplace,
                            reinterpret_cast<const unsigned char*>(targets.data()),
                            static_cast<int>(targets.size()));
            response.property = property;
        }
        else
        {
            std::vector<uint8_t> snapshot;
            bool supported = false;
            {
                std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
                if (const OwnedClipboardItem* item = FindClipboardItemLocked(request.target))
                {
                    snapshot = item->bytes;
                    supported = true;
                }
                else if (request.target == XA_STRING && FindClipboardTextItemLocked())
                {
                    const std::string latin1 = Utf8ToLatin1(g_clipboardUtf8);
                    snapshot.assign(latin1.begin(), latin1.end());
                    supported = true;
                }
            }
            if (supported && StartX11ClipboardTransfer(
                    request, property, request.target, snapshot))
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
        std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
        g_clipboardIncrTransfers.clear();
        g_clipboardItems.clear();
        g_clipboardUtf8.clear();
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
        False, AnyPropertyType, &actualType, &actualFormat,
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

        // Selection owners publish the INCR header before SelectionNotify, which
        // also queues PropertyNewValue on our requestor window. Drain that header
        // notification before deleting the property; otherwise it can be mistaken
        // for the first data chunk and an otherwise valid large transfer reads as
        // an empty payload.
        XEvent stalePropertyEvent{};
        while (XCheckTypedWindowEvent(
            g_display, g_clipboardWindow, PropertyNotify, &stalePropertyEvent)) {}
        XDeleteProperty(g_display, g_clipboardWindow, g_jaliumClipProp);
        XFlush(g_display);
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
    XDeleteProperty(g_display, g_clipboardWindow, g_jaliumClipProp);
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

static bool ReadWaylandSelectionData(
    const char* mimeType, std::vector<uint8_t>& bytes)
{
    if (!mimeType || !g_waylandSelectionOffer || !g_waylandSelectionOffer->offer)
        return false;
    if (std::find(
            g_waylandSelectionOffer->mimeTypes.begin(),
            g_waylandSelectionOffer->mimeTypes.end(), mimeType) ==
        g_waylandSelectionOffer->mimeTypes.end())
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

    bytes.clear();
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
            if (count > 0)
                bytes.insert(bytes.end(), buffer, buffer + count);
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

static bool ReadWaylandSelection(std::string& text)
{
    const char* mimeType = SelectWaylandTextMime(g_waylandSelectionOffer);
    std::vector<uint8_t> bytes;
    if (!ReadWaylandSelectionData(mimeType, bytes)) return false;
    text.assign(reinterpret_cast<const char*>(bytes.data()), bytes.size());
    return true;
}
#endif

static bool IsUtf8ClipboardMime(const char* mimeType)
{
    return mimeType &&
        (strcmp(mimeType, "text/plain;charset=utf-8") == 0 ||
         strcmp(mimeType, "UTF8_STRING") == 0 ||
         strcmp(mimeType, "text/plain") == 0);
}

static bool CopyClipboardItems(
    const JaliumClipboardDataItem* items, uint32_t itemCount,
    std::vector<OwnedClipboardItem>& result)
{
    result.clear();
    result.reserve(itemCount);
    for (uint32_t index = 0; index < itemCount; ++index)
    {
        if (!items[index].mimeType || !*items[index].mimeType ||
            strchr(items[index].mimeType, '\n') ||
            (!items[index].data && items[index].dataSize != 0))
            return false;

        OwnedClipboardItem copy;
        copy.mimeType = items[index].mimeType;
        if (items[index].dataSize != 0)
            copy.bytes.assign(
                items[index].data,
                items[index].data + items[index].dataSize);

        const auto existing = std::find_if(
            result.begin(), result.end(),
            [&copy](const OwnedClipboardItem& item)
            {
                return item.mimeType == copy.mimeType;
            });
        if (existing == result.end()) result.push_back(std::move(copy));
        else *existing = std::move(copy);
    }
    return true;
}

static JaliumResult AllocateClipboardData(
    const std::vector<uint8_t>& bytes, uint8_t** outData,
    uint32_t* outDataSize)
{
    if (bytes.size() > static_cast<size_t>(UINT32_MAX))
        return JALIUM_ERROR_OUT_OF_MEMORY;
    auto* allocation = static_cast<uint8_t*>(malloc(std::max<size_t>(1, bytes.size())));
    if (!allocation) return JALIUM_ERROR_OUT_OF_MEMORY;
    if (!bytes.empty()) memcpy(allocation, bytes.data(), bytes.size());
    *outData = allocation;
    *outDataSize = static_cast<uint32_t>(bytes.size());
    return JALIUM_OK;
}

static JaliumResult AllocateClipboardFormats(
    const std::vector<std::string>& formats, char** outMimeTypes)
{
    std::string joined;
    std::unordered_set<std::string> seen;
    for (const std::string& format : formats)
    {
        if (format.empty() || !seen.insert(format).second) continue;
        if (!joined.empty()) joined.push_back('\n');
        joined.append(format);
    }
    auto* allocation = static_cast<char*>(malloc(joined.size() + 1));
    if (!allocation) return JALIUM_ERROR_OUT_OF_MEMORY;
    if (!joined.empty()) memcpy(allocation, joined.data(), joined.size());
    allocation[joined.size()] = '\0';
    *outMimeTypes = allocation;
    return JALIUM_OK;
}

JaliumResult jalium_clipboard_get_formats(char** outMimeTypes)
{
    if (!outMimeTypes) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outMimeTypes = nullptr;
    std::vector<std::string> formats;

#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        if (g_waylandClipboardSource)
        {
            std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
            formats.reserve(g_clipboardItems.size());
            for (const OwnedClipboardItem& item : g_clipboardItems)
                formats.push_back(item.mimeType);
        }
        else if (g_waylandSelectionOffer)
            formats = g_waylandSelectionOffer->mimeTypes;
        return AllocateClipboardFormats(formats, outMimeTypes);
    }
#endif

    if (!g_display || !g_clipboardWindow) return JALIUM_ERROR_INVALID_STATE;
    std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
    EnsureClipboardAtoms();
    const Window owner = XGetSelectionOwner(g_display, g_clipboardAtom);
    if (owner == g_clipboardWindow)
    {
        formats.reserve(g_clipboardItems.size());
        for (const OwnedClipboardItem& item : g_clipboardItems)
            formats.push_back(item.mimeType);
    }
    else if (owner != None)
    {
        Atom actualType = None;
        int actualFormat = 0;
        std::vector<unsigned char> bytes;
        if (RequestX11Selection(g_targetsAtom, actualType, actualFormat, bytes) &&
            actualType == XA_ATOM && actualFormat == 32)
        {
            const auto* targets = reinterpret_cast<const unsigned long*>(bytes.data());
            const size_t count = bytes.size() / sizeof(unsigned long);
            formats.reserve(count);
            for (size_t index = 0; index < count; ++index)
            {
                const Atom target = static_cast<Atom>(targets[index]);
                if (target == g_targetsAtom || target == g_incrAtom) continue;
                char* name = XGetAtomName(g_display, target);
                if (name)
                {
                    formats.emplace_back(name);
                    XFree(name);
                }
            }
        }
    }
    return AllocateClipboardFormats(formats, outMimeTypes);
}

JaliumResult jalium_clipboard_get_data(
    const char* mimeType, uint8_t** outData, uint32_t* outDataSize)
{
    if (!mimeType || !*mimeType || !outData || !outDataSize)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    *outData = nullptr;
    *outDataSize = 0;
    std::vector<uint8_t> bytes;
    bool present = false;

#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        if (g_waylandClipboardSource)
        {
            std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
            const OwnedClipboardItem* item = FindClipboardItemLocked(mimeType);
            if (!item && IsUtf8ClipboardMime(mimeType))
                item = FindClipboardTextItemLocked();
            if (item) { bytes = item->bytes; present = true; }
        }
        else if (g_waylandSelectionOffer)
        {
            const char* requestedMime = mimeType;
            if (std::find(
                    g_waylandSelectionOffer->mimeTypes.begin(),
                    g_waylandSelectionOffer->mimeTypes.end(), mimeType) ==
                g_waylandSelectionOffer->mimeTypes.end())
            {
                requestedMime = IsUtf8ClipboardMime(mimeType)
                    ? SelectWaylandTextMime(g_waylandSelectionOffer) : nullptr;
            }
            present = requestedMime && ReadWaylandSelectionData(requestedMime, bytes);
        }
        return present ? AllocateClipboardData(bytes, outData, outDataSize) : JALIUM_OK;
    }
#endif

    if (!g_display || !g_clipboardWindow) return JALIUM_ERROR_INVALID_STATE;
    std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
    EnsureClipboardAtoms();
    const Window owner = XGetSelectionOwner(g_display, g_clipboardAtom);
    if (owner == None) return JALIUM_OK;
    if (owner == g_clipboardWindow)
    {
        const OwnedClipboardItem* item = FindClipboardItemLocked(mimeType);
        if (!item && IsUtf8ClipboardMime(mimeType))
            item = FindClipboardTextItemLocked();
        if (!item) return JALIUM_OK;
        return AllocateClipboardData(item->bytes, outData, outDataSize);
    }

    Atom selectedTarget = XInternAtom(g_display, mimeType, False);
    if (IsUtf8ClipboardMime(mimeType))
    {
        Atom actualType = None;
        int actualFormat = 0;
        std::vector<unsigned char> targetBytes;
        if (RequestX11Selection(g_targetsAtom, actualType, actualFormat, targetBytes) &&
            actualType == XA_ATOM && actualFormat == 32)
        {
            const auto* targets = reinterpret_cast<const unsigned long*>(targetBytes.data());
            const size_t count = targetBytes.size() / sizeof(unsigned long);
            auto contains = [targets, count](Atom target)
            {
                return std::find(targets, targets + count,
                                 static_cast<unsigned long>(target)) != targets + count;
            };
            if (contains(selectedTarget)) {}
            else if (contains(g_textPlainUtf8Atom)) selectedTarget = g_textPlainUtf8Atom;
            else if (contains(g_utf8StringAtom)) selectedTarget = g_utf8StringAtom;
            else if (contains(g_textPlainAtom)) selectedTarget = g_textPlainAtom;
            else if (contains(XA_STRING)) selectedTarget = XA_STRING;
            else return JALIUM_OK;
        }
    }

    Atom actualType = None;
    int actualFormat = 0;
    std::vector<unsigned char> selectionBytes;
    if (!RequestX11Selection(selectedTarget, actualType, actualFormat, selectionBytes) ||
        actualFormat != 8)
        return JALIUM_OK;
    if (selectedTarget == XA_STRING && IsUtf8ClipboardMime(mimeType))
    {
        const std::string utf8 = Latin1ToUtf8(selectionBytes.data(), selectionBytes.size());
        bytes.assign(utf8.begin(), utf8.end());
    }
    else
        bytes.assign(selectionBytes.begin(), selectionBytes.end());
    return AllocateClipboardData(bytes, outData, outDataSize);
}

JaliumResult jalium_clipboard_set_data(
    const JaliumClipboardDataItem* items, uint32_t itemCount)
{
    if (itemCount == 0) return jalium_clipboard_clear();
    if (!items) return JALIUM_ERROR_INVALID_ARGUMENT;
    std::vector<OwnedClipboardItem> copies;
    if (!CopyClipboardItems(items, itemCount, copies) || copies.empty())
        return JALIUM_ERROR_INVALID_ARGUMENT;

#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        if (!g_waylandDataDeviceManager || !g_waylandDataDevice)
            return JALIUM_ERROR_NOT_SUPPORTED;
        if (g_waylandInputSerial == 0)
            return JALIUM_ERROR_INVALID_STATE;
        wl_data_source* source = wl_data_device_manager_create_data_source(
            g_waylandDataDeviceManager);
        if (!source) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
        wl_data_source_add_listener(source, &g_dataSourceListener, nullptr);
        for (const OwnedClipboardItem& item : copies)
            wl_data_source_offer(source, item.mimeType.c_str());

        wl_data_source* previous = g_waylandClipboardSource;
        {
            std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
            g_clipboardItems = std::move(copies);
            SynchronizeLegacyClipboardTextLocked();
            g_waylandClipboardSource = source;
        }
        wl_data_device_set_selection(
            g_waylandDataDevice, source, g_waylandInputSerial);
        if (previous) wl_data_source_destroy(previous);
        return wl_display_flush(g_waylandDisplay) < 0
            ? JALIUM_ERROR_UNKNOWN : JALIUM_OK;
    }
#endif

    if (!g_display || !g_clipboardWindow) return JALIUM_ERROR_INVALID_STATE;
    std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
    EnsureClipboardAtoms();
    for (OwnedClipboardItem& item : copies)
        item.x11Atom = XInternAtom(g_display, item.mimeType.c_str(), False);
    g_clipboardItems = std::move(copies);
    g_clipboardIncrTransfers.clear();
    SynchronizeLegacyClipboardTextLocked();
    XSetSelectionOwner(g_display, g_clipboardAtom, g_clipboardWindow, CurrentTime);
    XFlush(g_display);
    return XGetSelectionOwner(g_display, g_clipboardAtom) == g_clipboardWindow
        ? JALIUM_OK : JALIUM_ERROR_UNKNOWN;
}

JaliumResult jalium_clipboard_clear(void)
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        if (!g_waylandDataDevice || g_waylandInputSerial == 0)
            return JALIUM_ERROR_INVALID_STATE;
        wl_data_device_set_selection(g_waylandDataDevice, nullptr, g_waylandInputSerial);
        wl_data_source* source = g_waylandClipboardSource;
        g_waylandClipboardSource = nullptr;
        if (source)
        {
            wl_data_source_destroy(source);
        }
        {
            std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
            g_clipboardItems.clear();
            g_clipboardUtf8.clear();
        }
        return wl_display_flush(g_waylandDisplay) < 0
            ? JALIUM_ERROR_UNKNOWN : JALIUM_OK;
    }
#endif
    if (!g_display || !g_clipboardWindow) return JALIUM_ERROR_INVALID_STATE;
    std::lock_guard<std::recursive_mutex> lock(g_clipboardMutex);
    EnsureClipboardAtoms();
    g_clipboardItems.clear();
    g_clipboardIncrTransfers.clear();
    g_clipboardUtf8.clear();
    XSetSelectionOwner(g_display, g_clipboardAtom, g_clipboardWindow, CurrentTime);
    XFlush(g_display);
    return XGetSelectionOwner(g_display, g_clipboardAtom) == g_clipboardWindow
        ? JALIUM_OK : JALIUM_ERROR_UNKNOWN;
}

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
            if (!FindClipboardTextItemLocked()) return JALIUM_OK;
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
        if (!FindClipboardTextItemLocked()) return JALIUM_OK;
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
    const std::string utf8 = Utf16ToUtf8(text);
    if (utf8.size() > static_cast<size_t>(UINT32_MAX))
        return JALIUM_ERROR_OUT_OF_MEMORY;
    const char* mimeTypes[] = {
        "text/plain;charset=utf-8", "text/plain", "UTF8_STRING"
    };
    JaliumClipboardDataItem items[3]{};
    for (size_t index = 0; index < std::size(items); ++index)
    {
        items[index].mimeType = mimeTypes[index];
        items[index].data = reinterpret_cast<const uint8_t*>(utf8.data());
        items[index].dataSize = static_cast<uint32_t>(utf8.size());
    }
    return jalium_clipboard_set_data(items, static_cast<uint32_t>(std::size(items)));
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

struct DragSourceCallbacks {
    JaliumDragFeedbackCallback feedback = nullptr;
    JaliumDragQueryContinueCallback queryContinue = nullptr;
    void* userData = nullptr;
};

static uint32_t X11DragKeyStates(unsigned int mask)
{
    uint32_t states = 0;
    if (mask & Button1Mask) states |= 1u;
    if (mask & Button3Mask) states |= 2u;
    if (mask & ShiftMask) states |= 4u;
    if (mask & ControlMask) states |= 8u;
    if (mask & Button2Mask) states |= 16u;
    if (mask & Mod1Mask) states |= 32u;
    return states;
}

static bool IsEscapePressed()
{
    return (g_keyStates[0x1B].load(std::memory_order_acquire) & 0x80u) != 0;
}

static JaliumDragContinueAction QueryDragContinuation(
    const DragSourceCallbacks& callbacks,
    uint32_t keyStates,
    bool escapePressed,
    bool defaultDrop)
{
    if (callbacks.queryContinue)
    {
        const JaliumDragContinueAction action = callbacks.queryContinue(
            keyStates, escapePressed ? 1 : 0, callbacks.userData);
        if (action == JALIUM_DRAG_DROP || action == JALIUM_DRAG_CANCEL)
            return action;
        return JALIUM_DRAG_CONTINUE;
    }
    if (escapePressed) return JALIUM_DRAG_CANCEL;
    return defaultDrop ? JALIUM_DRAG_DROP : JALIUM_DRAG_CONTINUE;
}

static void NotifyDragFeedback(
    const DragSourceCallbacks& callbacks, uint32_t effect)
{
    if (callbacks.feedback)
        callbacks.feedback(effect, callbacks.userData);
}

static bool IsValidDragImage(const JaliumDragImage* image)
{
    if (!image) return false;
    if (!image->bgraPixels || image->width == 0 || image->height == 0)
        return false;
    if (image->width > 4096 || image->height > 4096 ||
        image->stride < image->width * 4u)
        return false;
    const uint64_t byteCount =
        static_cast<uint64_t>(image->stride) * image->height;
    return byteCount <= static_cast<uint64_t>(SIZE_MAX);
}

static uint32_t PremultipliedArgb(const uint8_t* bgra)
{
    const uint32_t alpha = bgra[3];
    const uint32_t red = (static_cast<uint32_t>(bgra[2]) * alpha + 127u) / 255u;
    const uint32_t green = (static_cast<uint32_t>(bgra[1]) * alpha + 127u) / 255u;
    const uint32_t blue = (static_cast<uint32_t>(bgra[0]) * alpha + 127u) / 255u;
    return alpha << 24 | red << 16 | green << 8 | blue;
}

static Cursor CreateX11DragCursor(const JaliumDragImage* image)
{
#ifdef JALIUM_HAS_XCURSOR
    if (!g_display || !IsValidDragImage(image)) return None;
    XcursorImage* cursorImage = XcursorImageCreate(image->width, image->height);
    if (!cursorImage) return None;
    cursorImage->xhot = static_cast<XcursorDim>(std::clamp(
        image->hotspotX, 0, static_cast<int32_t>(image->width - 1)));
    cursorImage->yhot = static_cast<XcursorDim>(std::clamp(
        image->hotspotY, 0, static_cast<int32_t>(image->height - 1)));
    for (uint32_t y = 0; y < image->height; ++y)
    {
        const uint8_t* sourceRow = image->bgraPixels +
            static_cast<size_t>(y) * image->stride;
        for (uint32_t x = 0; x < image->width; ++x)
            cursorImage->pixels[static_cast<size_t>(y) * image->width + x] =
                PremultipliedArgb(sourceRow + static_cast<size_t>(x) * 4u);
    }
    const Cursor cursor = XcursorImageLoadCursor(g_display, cursorImage);
    XcursorImageDestroy(cursorImage);
    return cursor;
#else
    (void)image;
    return None;
#endif
}

static JaliumResult BeginX11Drag(
    JaliumPlatformWindow* window, std::vector<OwnedDragItem> items,
    uint32_t allowedEffects, const DragSourceCallbacks& callbacks,
    const JaliumDragImage* dragImage,
    uint32_t* performedEffect)
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

    const Cursor dragCursor = CreateX11DragCursor(dragImage);
    const int grab = XGrabPointer(
        g_display, window->xwindow, False,
        ButtonReleaseMask | PointerMotionMask,
        GrabModeAsync, GrabModeAsync, None, dragCursor, CurrentTime);
    if (grab != GrabSuccess)
    {
        if (dragCursor != None) XFreeCursor(g_display, dragCursor);
        XSetSelectionOwner(g_display, g_xdndSelectionAtom, None, CurrentTime);
        return JALIUM_ERROR_BUSY;
    }

    g_x11DragSource = &state;
    bool released = false;
    bool cancelled = false;
    while (!released && !state.finished && !cancelled)
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

        const uint32_t selectedEffect = state.targetAccepted
            ? (XdndActionToEffect(state.requestedAction) & allowedEffects)
            : JALIUM_DRAG_EFFECT_NONE;
        NotifyDragFeedback(callbacks, selectedEffect);
        const bool physicalReleased = (mask & Button1Mask) == 0;
        const JaliumDragContinueAction continuation = QueryDragContinuation(
            callbacks, X11DragKeyStates(mask), IsEscapePressed(),
            physicalReleased);
        cancelled = continuation == JALIUM_DRAG_CANCEL;
        released = continuation == JALIUM_DRAG_DROP;
        if (!released && !cancelled)
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
    if (dragCursor != None) XFreeCursor(g_display, dragCursor);
    // XdndStatus is an asynchronous ClientMessage. On an immediate release
    // (including the deterministic in-process test path) it can still be in
    // the X server after the last Position was flushed, so give the target a
    // short bounded window to answer before deciding whether to Drop/Leave.
    if (!cancelled && !state.finished && state.target && !state.targetAccepted)
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
    // An immediate release may learn the accepted action only in the bounded
    // status drain above. Surface that final cursor/effect decision before the
    // Drop message so GiveFeedback observes the same negotiated result that
    // is returned through performedEffect.
    NotifyDragFeedback(
        callbacks,
        state.targetAccepted
            ? (XdndActionToEffect(state.requestedAction) & allowedEffects)
            : JALIUM_DRAG_EFFECT_NONE);
    if (!cancelled && !state.finished && state.target && state.targetAccepted)
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
    *performedEffect = cancelled
        ? JALIUM_DRAG_EFFECT_NONE : state.performedEffect;
    return JALIUM_OK;
}

#ifdef JALIUM_HAS_WAYLAND
struct WaylandDragIcon {
    wl_surface* surface = nullptr;
    wl_buffer* buffer = nullptr;
    void* pixels = MAP_FAILED;
    size_t byteCount = 0;
    int32_t width = 0;
    int32_t height = 0;
    int32_t hotspotX = 0;
    int32_t hotspotY = 0;
};

static void DestroyWaylandDragIcon(WaylandDragIcon& icon)
{
    if (icon.buffer) wl_buffer_destroy(icon.buffer);
    if (icon.surface) wl_surface_destroy(icon.surface);
    if (icon.pixels != MAP_FAILED) munmap(icon.pixels, icon.byteCount);
    icon = {};
}

static bool CreateWaylandDragIcon(
    const JaliumDragImage* image, WaylandDragIcon& icon)
{
    if (!g_waylandCompositor || !g_waylandShm) return false;
    const bool hasImage = IsValidDragImage(image);
    icon.width = hasImage ? static_cast<int32_t>(image->width) : 1;
    icon.height = hasImage ? static_cast<int32_t>(image->height) : 1;
    icon.hotspotX = hasImage
        ? std::clamp(image->hotspotX, 0, icon.width - 1) : 0;
    icon.hotspotY = hasImage
        ? std::clamp(image->hotspotY, 0, icon.height - 1) : 0;
    const int32_t stride = icon.width * 4;
    icon.byteCount = static_cast<size_t>(stride) * icon.height;

    char path[] = "/tmp/jalium-drag-icon-XXXXXX";
    const int fd = mkstemp(path);
    if (fd < 0) return false;
    unlink(path);
    (void)fcntl(fd, F_SETFD, FD_CLOEXEC);
    if (ftruncate(fd, static_cast<off_t>(icon.byteCount)) != 0)
    {
        close(fd);
        return false;
    }
    icon.pixels = mmap(
        nullptr, icon.byteCount, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (icon.pixels == MAP_FAILED)
    {
        close(fd);
        return false;
    }

    auto* destination = static_cast<uint8_t*>(icon.pixels);
    memset(destination, 0, icon.byteCount);
    if (hasImage)
    {
        for (int32_t y = 0; y < icon.height; ++y)
        {
            const uint8_t* sourceRow = image->bgraPixels +
                static_cast<size_t>(y) * image->stride;
            uint32_t* destinationRow = reinterpret_cast<uint32_t*>(
                destination + static_cast<size_t>(y) * stride);
            for (int32_t x = 0; x < icon.width; ++x)
                destinationRow[x] = PremultipliedArgb(
                    sourceRow + static_cast<size_t>(x) * 4u);
        }
    }

    wl_shm_pool* pool = wl_shm_create_pool(
        g_waylandShm, fd, static_cast<int32_t>(icon.byteCount));
    close(fd);
    if (!pool)
    {
        DestroyWaylandDragIcon(icon);
        return false;
    }
    icon.buffer = wl_shm_pool_create_buffer(
        pool, 0, icon.width, icon.height, stride, WL_SHM_FORMAT_ARGB8888);
    wl_shm_pool_destroy(pool);
    icon.surface = wl_compositor_create_surface(g_waylandCompositor);
    if (!icon.buffer || !icon.surface)
    {
        DestroyWaylandDragIcon(icon);
        return false;
    }
    return true;
}

static void CommitWaylandDragIcon(const WaylandDragIcon& icon)
{
    wl_surface_attach(
        icon.surface, icon.buffer, -icon.hotspotX, -icon.hotspotY);
    wl_surface_damage(icon.surface, 0, 0, icon.width, icon.height);
    wl_surface_commit(icon.surface);
}

static JaliumResult BeginWaylandDrag(
    JaliumPlatformWindow* window, std::vector<OwnedDragItem> items,
    uint32_t allowedEffects, const DragSourceCallbacks& callbacks,
    const JaliumDragImage* dragImage,
    uint32_t* performedEffect)
{
    if (!g_waylandDisplay || !g_waylandDataDeviceManager ||
        !g_waylandDataDevice || !g_waylandCompositor || !g_waylandShm ||
        !window || !window->waylandSurface ||
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

    WaylandDragIcon icon;
    if (!CreateWaylandDragIcon(dragImage, icon))
    {
        wl_data_source_destroy(state.source);
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    g_waylandDragSource = &state;
    wl_data_device_start_drag(
        g_waylandDataDevice, state.source, window->waylandSurface,
        icon.surface, g_waylandPointerSerial);
    CommitWaylandDragIcon(icon);
    if (wl_display_flush(g_waylandDisplay) < 0)
    {
        g_waylandDragSource = nullptr;
        wl_data_source_destroy(state.source);
        DestroyWaylandDragIcon(icon);
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
        const uint32_t selectedEffect =
            WaylandActionToEffect(state.selectedAction) & allowedEffects;
        NotifyDragFeedback(callbacks, selectedEffect);
        const JaliumDragContinueAction continuation = QueryDragContinuation(
            callbacks, WaylandDragKeyStates(), IsEscapePressed(), false);
        if (continuation == JALIUM_DRAG_CANCEL)
        {
            state.cancelled = true;
            break;
        }
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
    DestroyWaylandDragIcon(icon);
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

JaliumResult jalium_drag_begin_with_image(
    JaliumPlatformWindow* window,
    const JaliumDragDataItem* items,
    uint32_t itemCount,
    uint32_t allowedEffects,
    JaliumDragFeedbackCallback feedbackCallback,
    JaliumDragQueryContinueCallback queryContinueCallback,
    void* callbackUserData,
    const JaliumDragImage* dragImage,
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
    const DragSourceCallbacks callbacks{
        feedbackCallback, queryContinueCallback, callbackUserData };

#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
        return BeginWaylandDrag(
            window, std::move(copied), allowedEffects, callbacks,
            dragImage,
            performedEffect);
#endif
    if (g_windowSystem == LinuxWindowSystem::XServer)
        return BeginX11Drag(
            window, std::move(copied), allowedEffects, callbacks,
            dragImage,
            performedEffect);
    return JALIUM_ERROR_INVALID_STATE;
}

JaliumResult jalium_drag_begin_ex(
    JaliumPlatformWindow* window,
    const JaliumDragDataItem* items,
    uint32_t itemCount,
    uint32_t allowedEffects,
    JaliumDragFeedbackCallback feedbackCallback,
    JaliumDragQueryContinueCallback queryContinueCallback,
    void* callbackUserData,
    uint32_t* performedEffect)
{
    return jalium_drag_begin_with_image(
        window, items, itemCount, allowedEffects,
        feedbackCallback, queryContinueCallback, callbackUserData,
        nullptr, performedEffect);
}

JaliumResult jalium_drag_begin(
    JaliumPlatformWindow* window,
    const JaliumDragDataItem* items,
    uint32_t itemCount,
    uint32_t allowedEffects,
    uint32_t* performedEffect)
{
    return jalium_drag_begin_ex(
        window, items, itemCount, allowedEffects,
        nullptr, nullptr, nullptr, performedEffect);
}

#ifdef JALIUM_PLATFORM_TEST_HOOKS
int32_t jalium_test_wayland_inject_touch(
    JaliumPlatformWindow* window, JaliumEventType type, int32_t touchId,
    float x, float y)
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem != LinuxWindowSystem::Wayland || !window ||
        !window->waylandSurface)
        return JALIUM_ERROR_INVALID_STATE;
    const float scale = static_cast<float>(
        window->waylandScale > 0 ? window->waylandScale : 1);
    const wl_fixed_t fixedX = wl_fixed_from_double(x / scale);
    const wl_fixed_t fixedY = wl_fixed_from_double(y / scale);
    const uint32_t serial = ++g_waylandInputSerial;
    switch (type)
    {
    case JALIUM_EVENT_POINTER_DOWN:
        HandleTouchDown(
            nullptr, nullptr, serial, 1, window->waylandSurface,
            touchId, fixedX, fixedY);
        return JALIUM_OK;
    case JALIUM_EVENT_POINTER_MOVE:
        if (g_waylandTouchContacts.find(touchId) == g_waylandTouchContacts.end())
            return JALIUM_ERROR_INVALID_STATE;
        HandleTouchMotion(nullptr, nullptr, 2, touchId, fixedX, fixedY);
        return JALIUM_OK;
    case JALIUM_EVENT_POINTER_UP:
        if (g_waylandTouchContacts.find(touchId) == g_waylandTouchContacts.end())
            return JALIUM_ERROR_INVALID_STATE;
        HandleTouchUp(nullptr, nullptr, serial, 3, touchId);
        return JALIUM_OK;
    case JALIUM_EVENT_POINTER_CANCEL:
        HandleTouchCancel(nullptr, nullptr);
        return JALIUM_OK;
    default:
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }
#else
    (void)window; (void)type; (void)touchId; (void)x; (void)y;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

int32_t jalium_test_wayland_inject_tablet(
    JaliumPlatformWindow* window, JaliumEventType type, int32_t toolId,
    float x, float y, float pressure, float tiltX, float tiltY, float twist)
{
    return jalium_test_wayland_inject_tablet_state(
        window, type, toolId, x, y, pressure, tiltX, tiltY, twist,
        JALIUM_POINTER_TOOL_PEN, JALIUM_POINTER_BUTTON_NONE, 0.0f, 0.0f);
}

int32_t jalium_test_wayland_inject_tablet_state(
    JaliumPlatformWindow* window, JaliumEventType type, int32_t toolId,
    float x, float y, float pressure, float tiltX, float tiltY, float twist,
    int32_t toolType, uint32_t buttons, float distance, float slider)
{
#if defined(JALIUM_HAS_WAYLAND) && defined(JALIUM_HAS_WAYLAND_TABLET_V2)
    if (g_windowSystem != LinuxWindowSystem::Wayland || !window ||
        !window->waylandSurface)
        return JALIUM_ERROR_INVALID_STATE;
    if (toolType < JALIUM_POINTER_TOOL_UNKNOWN ||
        toolType > JALIUM_POINTER_TOOL_LENS ||
        !std::isfinite(distance) || !std::isfinite(slider))
        return JALIUM_ERROR_INVALID_ARGUMENT;

    static std::unordered_map<int32_t, WaylandTabletToolState> tools;
    auto iterator = tools.find(toolId);
    if (type == JALIUM_EVENT_POINTER_DOWN)
    {
        if (iterator != tools.end())
        {
            TabletToolProximityOut(&iterator->second, nullptr);
            TabletToolFrame(&iterator->second, nullptr, 0);
            tools.erase(iterator);
        }
        iterator = tools.end();
    }
    if (iterator == tools.end() &&
        (type == JALIUM_EVENT_POINTER_DOWN ||
         type == JALIUM_EVENT_POINTER_MOVE))
    {
        iterator = tools.try_emplace(toolId).first;
        iterator->second.pointerId =
            0x20000000u | (static_cast<uint32_t>(toolId) & 0x0fffffffu);
        uint32_t protocolToolType = ZWP_TABLET_TOOL_V2_TYPE_PEN;
        switch (toolType)
        {
        case JALIUM_POINTER_TOOL_ERASER:
            protocolToolType = ZWP_TABLET_TOOL_V2_TYPE_ERASER; break;
        case JALIUM_POINTER_TOOL_BRUSH:
            protocolToolType = ZWP_TABLET_TOOL_V2_TYPE_BRUSH; break;
        case JALIUM_POINTER_TOOL_PENCIL:
            protocolToolType = ZWP_TABLET_TOOL_V2_TYPE_PENCIL; break;
        case JALIUM_POINTER_TOOL_AIRBRUSH:
            protocolToolType = ZWP_TABLET_TOOL_V2_TYPE_AIRBRUSH; break;
        case JALIUM_POINTER_TOOL_MOUSE:
            protocolToolType = ZWP_TABLET_TOOL_V2_TYPE_MOUSE; break;
        case JALIUM_POINTER_TOOL_LENS:
            protocolToolType = ZWP_TABLET_TOOL_V2_TYPE_LENS; break;
        default:
            break;
        }
        TabletToolType(&iterator->second, nullptr, protocolToolType);
        TabletToolCapability(
            &iterator->second, nullptr,
            ZWP_TABLET_TOOL_V2_CAPABILITY_PRESSURE);
        TabletToolCapability(
            &iterator->second, nullptr,
            ZWP_TABLET_TOOL_V2_CAPABILITY_TILT);
        TabletToolCapability(
            &iterator->second, nullptr,
            ZWP_TABLET_TOOL_V2_CAPABILITY_ROTATION);
        TabletToolCapability(
            &iterator->second, nullptr,
            ZWP_TABLET_TOOL_V2_CAPABILITY_DISTANCE);
        TabletToolCapability(
            &iterator->second, nullptr,
            ZWP_TABLET_TOOL_V2_CAPABILITY_SLIDER);
        TabletToolProximityIn(
            &iterator->second, nullptr, ++g_waylandInputSerial,
            nullptr, window->waylandSurface);
    }
    else if (iterator == tools.end())
    {
        return JALIUM_ERROR_INVALID_STATE;
    }

    WaylandTabletToolState& state = iterator->second;
    const auto applyButton = [&](uint32_t mask, uint32_t nativeButton) {
        const bool requested = (buttons & mask) != 0;
        const bool active = (state.buttons & mask) != 0;
        if (requested == active) return;
        TabletToolButton(
            &state, nullptr, ++g_waylandInputSerial, nativeButton,
            requested ? ZWP_TABLET_TOOL_V2_BUTTON_STATE_PRESSED
                      : ZWP_TABLET_TOOL_V2_BUTTON_STATE_RELEASED);
    };
    applyButton(JALIUM_POINTER_BUTTON_BARREL, BTN_STYLUS);
    applyButton(JALIUM_POINTER_BUTTON_SECONDARY, BTN_STYLUS2);
    applyButton(JALIUM_POINTER_BUTTON_TERTIARY, BTN_MIDDLE);
    const float scale = static_cast<float>(
        window->waylandScale > 0 ? window->waylandScale : 1);
    TabletToolMotion(
        &state, nullptr,
        wl_fixed_from_double(x / scale), wl_fixed_from_double(y / scale));
    TabletToolPressure(
        &state, nullptr,
        static_cast<uint32_t>(std::lround(
            std::clamp(pressure, 0.0f, 1.0f) * 65535.0f)));
    TabletToolTilt(
        &state, nullptr, wl_fixed_from_double(tiltX), wl_fixed_from_double(tiltY));
    TabletToolRotation(&state, nullptr, wl_fixed_from_double(twist));
    TabletToolDistance(
        &state, nullptr,
        static_cast<uint32_t>(std::lround(
            std::clamp(distance, 0.0f, 1.0f) * 65535.0f)));
    TabletToolSlider(
        &state, nullptr,
        static_cast<int32_t>(std::lround(
            std::clamp(slider, -1.0f, 1.0f) * 65535.0f)));

    switch (type)
    {
    case JALIUM_EVENT_POINTER_DOWN:
        TabletToolDown(&state, nullptr, ++g_waylandInputSerial);
        TabletToolFrame(&state, nullptr, 1);
        return JALIUM_OK;
    case JALIUM_EVENT_POINTER_MOVE:
        TabletToolFrame(&state, nullptr, 2);
        return JALIUM_OK;
    case JALIUM_EVENT_POINTER_UP:
        TabletToolUp(&state, nullptr);
        TabletToolFrame(&state, nullptr, 3);
        return JALIUM_OK;
    case JALIUM_EVENT_POINTER_CANCEL:
        TabletToolProximityOut(&state, nullptr);
        TabletToolFrame(&state, nullptr, 4);
        tools.erase(iterator);
        return JALIUM_OK;
    default:
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }
#else
    (void)window; (void)type; (void)toolId; (void)x; (void)y;
    (void)pressure; (void)tiltX; (void)tiltY; (void)twist;
    (void)toolType; (void)buttons; (void)distance; (void)slider;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

int32_t jalium_test_wayland_inject_decoration_configure(
    JaliumPlatformWindow* window, uint32_t mode)
{
#if defined(JALIUM_HAS_WAYLAND) && defined(JALIUM_HAS_XDG_DECORATION_V1)
    if (g_windowSystem != LinuxWindowSystem::Wayland || !window)
        return JALIUM_ERROR_INVALID_STATE;
    if (mode != ZXDG_TOPLEVEL_DECORATION_V1_MODE_CLIENT_SIDE &&
        mode != ZXDG_TOPLEVEL_DECORATION_V1_MODE_SERVER_SIDE)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    HandleWaylandDecorationConfigure(window, nullptr, mode);
    return JALIUM_OK;
#else
    (void)window; (void)mode;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

uint32_t jalium_test_wayland_get_decoration_mode(JaliumPlatformWindow* window)
{
#if defined(JALIUM_HAS_WAYLAND) && defined(JALIUM_HAS_XDG_DECORATION_V1)
    return window ? window->xdgDecorationMode : 0;
#else
    (void)window;
    return 0;
#endif
}

int32_t jalium_test_wayland_set_output(
    JaliumPlatformWindow* window, uint32_t outputId, int32_t scale,
    int32_t entered)
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem != LinuxWindowSystem::Wayland || !window ||
        !window->waylandSurface)
        return JALIUM_ERROR_INVALID_STATE;
    if (outputId == 0 || scale <= 0 || (entered != 0 && entered != 1))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    SetWaylandOutputState(window, outputId, scale, entered != 0);
    return JALIUM_OK;
#else
    (void)window; (void)outputId; (void)scale; (void)entered;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

int32_t jalium_test_wayland_reset_outputs(JaliumPlatformWindow* window)
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem != LinuxWindowSystem::Wayland || !window ||
        !window->waylandSurface)
        return JALIUM_ERROR_INVALID_STATE;
    window->waylandEnteredOutputs.clear();
    RecomputeWaylandScale(window);
    return JALIUM_OK;
#else
    (void)window;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

uint32_t jalium_test_wayland_get_selected_output(JaliumPlatformWindow* window)
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem != LinuxWindowSystem::Wayland || !window)
        return 0;
    return SelectWaylandEnteredOutputId(window);
#else
    (void)window;
    return 0;
#endif
}

int32_t jalium_test_x11_notify_display_change(void)
{
#ifdef JALIUM_HAS_XRANDR
    if (g_windowSystem != LinuxWindowSystem::XServer || !g_xrandrAvailable)
        return JALIUM_ERROR_NOT_SUPPORTED;
    RefreshX11DisplayMetrics();
    return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

float jalium_test_x11_compute_monitor_scale(
    int32_t width, int32_t height, int32_t widthMm, int32_t heightMm,
    float fallbackScale)
{
    return ComputeX11MonitorScale(
        width, height, widthMm, heightMm, fallbackScale);
}

int32_t jalium_test_override_double_click_settings(
    uint32_t milliseconds, float distance)
{
    if (milliseconds == 0 || milliseconds > 5000 ||
        !std::isfinite(distance) || distance < 0.0f || distance > 100.0f)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    g_doubleClickMilliseconds = milliseconds;
    g_doubleClickDistance = distance;
    g_x11ClickTracker = {};
#ifdef JALIUM_HAS_WAYLAND
    g_waylandClickTracker = {};
#endif
    return JALIUM_OK;
}

int32_t jalium_test_register_click(
    uint32_t time, float x, float y, int32_t button, int32_t reset)
{
    static ClickTracker tracker;
    if (reset != 0) tracker = {};
    return RegisterClick(
        tracker, reinterpret_cast<JaliumPlatformWindow*>(1),
        button, time, x, y);
}

void jalium_test_override_touch_capabilities(
    int32_t touchPresent, int32_t maxContacts)
{
    g_testTouchPresent.store(touchPresent, std::memory_order_release);
    g_testTouchContacts.store(
        std::max(maxContacts, 0), std::memory_order_release);
}

int32_t jalium_test_xinput_smooth_scroll_delta(
    double previousValue, double currentValue, double increment,
    int32_t vertical, float* deltaX, float* deltaY)
{
    if (!deltaX || !deltaY) return -1;
    *deltaX = 0.0f;
    *deltaY = 0.0f;
    return ComputeSmoothScrollDelta(
        previousValue, currentValue, increment, vertical != 0,
        *deltaX, *deltaY) ? 1 : 0;
}

uint32_t jalium_test_xinput_pen_flags(
    int32_t toolType, int32_t inverted, int32_t inRange,
    int32_t inContact, uint32_t buttons)
{
#ifdef JALIUM_HAS_XINPUT2
    XInputPenAxes axes{};
    axes.toolType = toolType;
    axes.inverted = inverted != 0;
    axes.inRange = inRange != 0;
    axes.inContact = inContact != 0;
    axes.buttons = buttons;
    return XInputPenFlags(axes);
#else
    (void)toolType; (void)inverted; (void)inRange; (void)inContact;
    (void)buttons;
    return 0;
#endif
}

int32_t jalium_test_wayland_get_ime_context(
    JaliumPlatformWindow* window, int32_t* enabled,
    char* utf8Buffer, uint32_t bufferCapacity,
    int32_t* cursorByteOffset, int32_t* anchorByteOffset,
    int32_t* x, int32_t* y, int32_t* width, int32_t* height)
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem != LinuxWindowSystem::Wayland || !window ||
        !window->waylandSurface)
        return JALIUM_ERROR_INVALID_STATE;
    if (!enabled || !utf8Buffer || bufferCapacity == 0 ||
        !cursorByteOffset || !anchorByteOffset || !x || !y || !width || !height)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    if (window->imeSurroundingText.size() + 1 > bufferCapacity)
        return JALIUM_ERROR_OUT_OF_MEMORY;

    memcpy(utf8Buffer, window->imeSurroundingText.c_str(),
           window->imeSurroundingText.size() + 1);
    *enabled = window->imeEnabled ? 1 : 0;
    *cursorByteOffset = window->imeCursorByteOffset;
    *anchorByteOffset = window->imeAnchorByteOffset;
    *x = window->imeCaretX;
    *y = window->imeCaretY;
    *width = window->imeCaretWidth;
    *height = window->imeCaretHeight;
    return JALIUM_OK;
#else
    (void)window; (void)enabled; (void)utf8Buffer; (void)bufferCapacity;
    (void)cursorByteOffset; (void)anchorByteOffset; (void)x; (void)y;
    (void)width; (void)height;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

int32_t jalium_test_wayland_inject_delete_surrounding(
    JaliumPlatformWindow* window, uint32_t beforeUtf8Bytes,
    uint32_t afterUtf8Bytes)
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem != LinuxWindowSystem::Wayland || !window ||
        !window->waylandSurface)
        return JALIUM_ERROR_INVALID_STATE;
    DispatchDeleteSurrounding(window, beforeUtf8Bytes, afterUtf8Bytes);
    return JALIUM_OK;
#else
    (void)window; (void)beforeUtf8Bytes; (void)afterUtf8Bytes;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

int32_t jalium_test_wayland_get_last_system_menu(
    JaliumPlatformWindow* window, int32_t* x, int32_t* y,
    uint32_t* inputSerial)
{
    if (!window || !x || !y || !inputSerial)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    if (g_testSystemMenuWindow != window)
        return JALIUM_ERROR_INVALID_STATE;
    *x = g_testSystemMenuX;
    *y = g_testSystemMenuY;
    *inputSerial = g_testSystemMenuSerial;
    return JALIUM_OK;
}

int32_t jalium_test_wayland_has_activation(void)
{
#if defined(JALIUM_HAS_WAYLAND) && defined(JALIUM_HAS_XDG_ACTIVATION_V1)
    return g_windowSystem == LinuxWindowSystem::Wayland && g_xdgActivation
        ? 1 : 0;
#else
    return 0;
#endif
}
#endif

// ============================================================================
// Window management extensions (monitors, size limits, interactive move/
// resize, icon, topmost)
// ============================================================================

namespace {

#ifndef JALIUM_HAS_WAYLAND
struct WaylandOutputInfo; // keep the signatures below uniform
#endif

bool GetX11WorkArea(int32_t* x, int32_t* y, int32_t* width, int32_t* height)
{
    if (!g_display || !x || !y || !width || !height) return false;
    unsigned long currentDesktop = 0;
    {
        const Atom currentDesktopAtom =
            XInternAtom(g_display, "_NET_CURRENT_DESKTOP", False);
        Atom actualType = None;
        int actualFormat = 0;
        unsigned long itemCount = 0, bytesAfter = 0;
        unsigned char* data = nullptr;
        if (XGetWindowProperty(
                g_display, g_rootWindow, currentDesktopAtom, 0, 1, False,
                XA_CARDINAL, &actualType, &actualFormat, &itemCount,
                &bytesAfter, &data) == Success && data)
        {
            if (actualType == XA_CARDINAL && actualFormat == 32 && itemCount >= 1)
                currentDesktop = *reinterpret_cast<unsigned long*>(data);
            XFree(data);
        }
    }
    if (currentDesktop > static_cast<unsigned long>(
            std::numeric_limits<long>::max() / 4))
        return false;

    Atom workAreaAtom = XInternAtom(g_display, "_NET_WORKAREA", False);
    Atom actualType = None;
    int actualFormat = 0;
    unsigned long itemCount = 0, bytesAfter = 0;
    unsigned char* data = nullptr;
    bool ok = false;
    const long propertyOffset = static_cast<long>(currentDesktop * 4);
    if (XGetWindowProperty(g_display, g_rootWindow, workAreaAtom,
                           propertyOffset, 4, False,
                           XA_CARDINAL, &actualType, &actualFormat, &itemCount,
                           &bytesAfter, &data) == Success && data)
    {
        if (actualType == XA_CARDINAL && actualFormat == 32 && itemCount >= 4)
        {
            const long* values = reinterpret_cast<const long*>(data);
            *x = static_cast<int32_t>(values[0]);
            *y = static_cast<int32_t>(values[1]);
            *width = static_cast<int32_t>(values[2]);
            *height = static_cast<int32_t>(values[3]);
            ok = *width > 0 && *height > 0;
        }
        XFree(data);
    }
    return ok;
}

} // namespace

int32_t jalium_platform_get_monitor_count(void)
{
#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
        return static_cast<int32_t>(g_waylandOutputs.size());
#endif
    if (g_windowSystem != LinuxWindowSystem::XServer || !g_display)
        return 0;
#ifdef JALIUM_HAS_XRANDR
    std::vector<X11MonitorMetrics> monitors;
    if (GetX11MonitorMetricsSnapshot(monitors))
        return static_cast<int32_t>(monitors.size());
#endif
    return 1;
}

int32_t jalium_platform_get_monitor_info(int32_t index, JaliumMonitorInfo* info)
{
    if (!info || index < 0)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    *info = JaliumMonitorInfo{};
    info->scale = 1.0f;

#ifdef JALIUM_HAS_WAYLAND
    if (g_windowSystem == LinuxWindowSystem::Wayland)
    {
        if (static_cast<size_t>(index) >= g_waylandOutputs.size())
            return JALIUM_ERROR_INVALID_ARGUMENT;
        const WaylandOutputInfo* output = g_waylandOutputs[static_cast<size_t>(index)];
        info->x = output->x;
        info->y = output->y;
        info->width = output->width;
        info->height = output->height;
        info->workX = output->x;
        info->workY = output->y;
        info->workWidth = output->width;
        info->workHeight = output->height;
        info->scale = static_cast<float>(output->scale > 0 ? output->scale : 1);
        info->refreshRate = output->refreshMilliHz > 0
            ? (output->refreshMilliHz + 500) / 1000
            : 0;
        info->isPrimary = index == 0 ? 1 : 0;
        return JALIUM_OK;
    }
#endif
    if (g_windowSystem != LinuxWindowSystem::XServer || !g_display)
        return JALIUM_ERROR_INVALID_STATE;

    int32_t workX = 0, workY = 0, workWidth = 0, workHeight = 0;
    const bool hasWorkArea = GetX11WorkArea(&workX, &workY, &workWidth, &workHeight);
    X11MonitorMetrics monitor{};
    if (GetX11MonitorMetricsByIndex(index, monitor))
    {
        info->x = monitor.x;
        info->y = monitor.y;
        info->width = monitor.width;
        info->height = monitor.height;
        info->isPrimary = monitor.primary ? 1 : 0;
        info->scale = monitor.scale;
        info->refreshRate = monitor.refreshRate;

        // _NET_WORKAREA is desktop-global; intersect it with this monitor.
        if (hasWorkArea)
        {
            const int32_t left = std::max(info->x, workX);
            const int32_t top = std::max(info->y, workY);
            const int32_t right = std::min(info->x + info->width, workX + workWidth);
            const int32_t bottom = std::min(info->y + info->height, workY + workHeight);
            if (right > left && bottom > top)
            {
                info->workX = left;
                info->workY = top;
                info->workWidth = right - left;
                info->workHeight = bottom - top;
            }
        }
        if (info->workWidth <= 0 || info->workHeight <= 0)
        {
            info->workX = info->x;
            info->workY = info->y;
            info->workWidth = info->width;
            info->workHeight = info->height;
        }
        return JALIUM_OK;
    }

    if (index != 0)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    info->x = 0;
    info->y = 0;
    info->width = DisplayWidth(g_display, g_screen);
    info->height = DisplayHeight(g_display, g_screen);
    info->scale = DetectDpiScale();
    info->refreshRate = jalium_window_get_monitor_refresh_rate(nullptr);
    info->isPrimary = 1;
    if (hasWorkArea)
    {
        info->workX = workX;
        info->workY = workY;
        info->workWidth = workWidth;
        info->workHeight = workHeight;
    }
    else
    {
        info->workX = info->x;
        info->workY = info->y;
        info->workWidth = info->width;
        info->workHeight = info->height;
    }
    return JALIUM_OK;
}

int32_t jalium_window_set_min_max_size(
    JaliumPlatformWindow* window,
    int32_t minWidth, int32_t minHeight,
    int32_t maxWidth, int32_t maxHeight)
{
    if (!window)
        return JALIUM_ERROR_INVALID_ARGUMENT;

    window->requestedMinWidth = std::max(0, minWidth);
    window->requestedMinHeight = std::max(0, minHeight);
    window->requestedMaxWidth = std::max(0, maxWidth);
    window->requestedMaxHeight = std::max(0, maxHeight);

#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
        if (!window->xdgToplevel && !CreateWaylandRole(window))
            return JALIUM_ERROR_INVALID_STATE;
        // xdg_toplevel speaks compositor-logical units.
        const int32_t scale = window->waylandScale > 0 ? window->waylandScale : 1;
        const bool resizable = (window->style & JALIUM_WINDOW_STYLE_RESIZABLE) != 0;
        const int32_t effectiveMinWidth = resizable ? window->requestedMinWidth : window->width;
        const int32_t effectiveMinHeight = resizable ? window->requestedMinHeight : window->height;
        const int32_t effectiveMaxWidth = resizable ? window->requestedMaxWidth : window->width;
        const int32_t effectiveMaxHeight = resizable ? window->requestedMaxHeight : window->height;
        xdg_toplevel_set_min_size(window->xdgToplevel,
                                  effectiveMinWidth > 0 ? effectiveMinWidth / scale : 0,
                                  effectiveMinHeight > 0 ? effectiveMinHeight / scale : 0);
        xdg_toplevel_set_max_size(window->xdgToplevel,
                                  effectiveMaxWidth > 0 ? effectiveMaxWidth / scale : 0,
                                  effectiveMaxHeight > 0 ? effectiveMaxHeight / scale : 0);
        wl_surface_commit(window->waylandSurface);
        wl_display_flush(g_waylandDisplay);
        return JALIUM_OK;
    }
#endif
    if (!g_display || !window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;

    XSizeHints hints{};
    long supplied = 0;
    XGetWMNormalHints(g_display, window->xwindow, &hints, &supplied);
    hints.flags &= ~(PMinSize | PMaxSize);
    const bool resizable = (window->style & JALIUM_WINDOW_STYLE_RESIZABLE) != 0;
    const int32_t effectiveMinWidth = resizable ? window->requestedMinWidth : window->width;
    const int32_t effectiveMinHeight = resizable ? window->requestedMinHeight : window->height;
    const int32_t effectiveMaxWidth = resizable ? window->requestedMaxWidth : window->width;
    const int32_t effectiveMaxHeight = resizable ? window->requestedMaxHeight : window->height;
    if (effectiveMinWidth > 0 || effectiveMinHeight > 0)
    {
        hints.flags |= PMinSize;
        hints.min_width = effectiveMinWidth > 0 ? effectiveMinWidth : 0;
        hints.min_height = effectiveMinHeight > 0 ? effectiveMinHeight : 0;
    }
    if (effectiveMaxWidth > 0 || effectiveMaxHeight > 0)
    {
        hints.flags |= PMaxSize;
        hints.max_width = effectiveMaxWidth > 0 ? effectiveMaxWidth : INT32_MAX;
        hints.max_height = effectiveMaxHeight > 0 ? effectiveMaxHeight : INT32_MAX;
    }
    XSetWMNormalHints(g_display, window->xwindow, &hints);
    XFlush(g_display);
    return JALIUM_OK;
}

namespace {

// _NET_WM_MOVERESIZE directions.
constexpr long kNetWmMoveResizeMove = 8;

int32_t BeginX11MoveResize(JaliumPlatformWindow* window, long direction)
{
    if (!g_display || !window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;

    // The pointer grab from the triggering press blocks the WM from taking
    // over; hand it back before delegating.
    XUngrabPointer(g_display, CurrentTime);

    Atom moveResizeAtom = XInternAtom(g_display, "_NET_WM_MOVERESIZE", False);
    XEvent ev{};
    ev.type = ClientMessage;
    ev.xclient.window = window->xwindow;
    ev.xclient.message_type = moveResizeAtom;
    ev.xclient.format = 32;
    ev.xclient.data.l[0] = window->lastPressRootX;
    ev.xclient.data.l[1] = window->lastPressRootY;
    ev.xclient.data.l[2] = direction;
    ev.xclient.data.l[3] = static_cast<long>(
        window->lastPressButton > 0 ? window->lastPressButton : Button1);
    ev.xclient.data.l[4] = 1; // source: normal application
    XSendEvent(g_display, g_rootWindow, False,
               SubstructureRedirectMask | SubstructureNotifyMask, &ev);
    XFlush(g_display);
    return JALIUM_OK;
}

} // namespace

int32_t jalium_window_begin_move_drag(JaliumPlatformWindow* window)
{
    if (!window)
        return JALIUM_ERROR_INVALID_ARGUMENT;

#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
        if (!window->xdgToplevel || !g_waylandSeat)
            return JALIUM_ERROR_INVALID_STATE;
        xdg_toplevel_move(window->xdgToplevel, g_waylandSeat, g_waylandPointerSerial);
        wl_display_flush(g_waylandDisplay);
        return JALIUM_OK;
    }
#endif
    return BeginX11MoveResize(window, kNetWmMoveResizeMove);
}

int32_t jalium_window_begin_resize_drag(JaliumPlatformWindow* window, int32_t edge)
{
    if (!window)
        return JALIUM_ERROR_INVALID_ARGUMENT;

#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
        if (!window->xdgToplevel || !g_waylandSeat)
            return JALIUM_ERROR_INVALID_STATE;
        // JaliumResizeEdge deliberately mirrors xdg_toplevel_resize_edge.
        xdg_toplevel_resize(window->xdgToplevel, g_waylandSeat,
                            g_waylandPointerSerial,
                            static_cast<uint32_t>(edge));
        wl_display_flush(g_waylandDisplay);
        return JALIUM_OK;
    }
#endif
    // Map xdg-style edges onto _NET_WM_MOVERESIZE directions.
    long direction;
    switch (edge)
    {
    case JALIUM_RESIZE_EDGE_TOP:          direction = 1; break;
    case JALIUM_RESIZE_EDGE_TOP_RIGHT:    direction = 2; break;
    case JALIUM_RESIZE_EDGE_RIGHT:        direction = 3; break;
    case JALIUM_RESIZE_EDGE_BOTTOM_RIGHT: direction = 4; break;
    case JALIUM_RESIZE_EDGE_BOTTOM:       direction = 5; break;
    case JALIUM_RESIZE_EDGE_BOTTOM_LEFT:  direction = 6; break;
    case JALIUM_RESIZE_EDGE_LEFT:         direction = 7; break;
    case JALIUM_RESIZE_EDGE_TOP_LEFT:     direction = 0; break;
    default: return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    return BeginX11MoveResize(window, direction);
}

#if defined(JALIUM_HAS_WAYLAND) && defined(JALIUM_HAS_XDG_TOPLEVEL_ICON_V1)
namespace {

struct WaylandToplevelIconBuffer {
    wl_buffer* buffer = nullptr;
    void* pixels = MAP_FAILED;
    size_t byteCount = 0;
};

void DestroyWaylandToplevelIconBuffer(WaylandToplevelIconBuffer& icon)
{
    if (icon.buffer) wl_buffer_destroy(icon.buffer);
    if (icon.pixels != MAP_FAILED) munmap(icon.pixels, icon.byteCount);
    icon = {};
}

bool CreateWaylandToplevelIconBuffer(
    const uint32_t* bgraPixels, int32_t width, int32_t height,
    WaylandToplevelIconBuffer& icon)
{
    if (!g_waylandShm || !bgraPixels || width <= 0 || height <= 0 ||
        width > 4096 || height > 4096)
        return false;

    // The protocol requires every wl_shm buffer to be square. Preserve a
    // rectangular source without distortion by centering it in transparent
    // padding at the larger edge length.
    const int32_t edge = std::max(width, height);
    const int32_t stride = edge * 4;
    icon.byteCount = static_cast<size_t>(stride) * static_cast<size_t>(edge);

    char path[] = "/tmp/jalium-window-icon-XXXXXX";
    const int fd = mkstemp(path);
    if (fd < 0) return false;
    unlink(path);
    (void)fcntl(fd, F_SETFD, FD_CLOEXEC);
    if (ftruncate(fd, static_cast<off_t>(icon.byteCount)) != 0)
    {
        close(fd);
        return false;
    }
    icon.pixels = mmap(
        nullptr, icon.byteCount, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (icon.pixels == MAP_FAILED)
    {
        close(fd);
        return false;
    }

    auto* destination = static_cast<uint8_t*>(icon.pixels);
    memset(destination, 0, icon.byteCount);
    const int32_t offsetX = (edge - width) / 2;
    const int32_t offsetY = (edge - height) / 2;
    const auto* source = reinterpret_cast<const uint8_t*>(bgraPixels);
    for (int32_t y = 0; y < height; ++y)
    {
        auto* destinationRow = reinterpret_cast<uint32_t*>(
            destination + static_cast<size_t>(y + offsetY) * stride);
        const uint8_t* sourceRow =
            source + static_cast<size_t>(y) * static_cast<size_t>(width) * 4u;
        for (int32_t x = 0; x < width; ++x)
            destinationRow[x + offsetX] = PremultipliedArgb(
                sourceRow + static_cast<size_t>(x) * 4u);
    }

    wl_shm_pool* pool = wl_shm_create_pool(
        g_waylandShm, fd, static_cast<int32_t>(icon.byteCount));
    close(fd);
    if (!pool)
    {
        DestroyWaylandToplevelIconBuffer(icon);
        return false;
    }
    icon.buffer = wl_shm_pool_create_buffer(
        pool, 0, edge, edge, stride, WL_SHM_FORMAT_ARGB8888);
    wl_shm_pool_destroy(pool);
    if (!icon.buffer)
    {
        DestroyWaylandToplevelIconBuffer(icon);
        return false;
    }
    return true;
}

} // namespace
#endif

int32_t jalium_window_set_icon(
    JaliumPlatformWindow* window,
    const uint32_t* bgraPixels,
    int32_t width,
    int32_t height)
{
    if (!window)
        return JALIUM_ERROR_INVALID_ARGUMENT;

#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
#ifdef JALIUM_HAS_XDG_TOPLEVEL_ICON_V1
        if (!window->xdgToplevel || !g_waylandToplevelIconManager)
            return JALIUM_ERROR_NOT_SUPPORTED;

        if (!bgraPixels || width <= 0 || height <= 0)
        {
            xdg_toplevel_icon_manager_v1_set_icon(
                g_waylandToplevelIconManager, window->xdgToplevel, nullptr);
            wl_surface_commit(window->waylandSurface);
            wl_display_flush(g_waylandDisplay);
            return JALIUM_OK;
        }

        WaylandToplevelIconBuffer buffer;
        if (!CreateWaylandToplevelIconBuffer(
                bgraPixels, width, height, buffer))
            return (width > 4096 || height > 4096)
                ? JALIUM_ERROR_INVALID_ARGUMENT
                : JALIUM_ERROR_RESOURCE_CREATION_FAILED;

        xdg_toplevel_icon_v1* icon =
            xdg_toplevel_icon_manager_v1_create_icon(
                g_waylandToplevelIconManager);
        if (!icon)
        {
            DestroyWaylandToplevelIconBuffer(buffer);
            return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
        }
        xdg_toplevel_icon_v1_add_buffer(icon, buffer.buffer, 1);
        xdg_toplevel_icon_manager_v1_set_icon(
            g_waylandToplevelIconManager, window->xdgToplevel, icon);
        wl_surface_commit(window->waylandSurface);

        // set_icon snapshots the immutable icon state. Protocol request order
        // guarantees the server consumes add_buffer/set_icon before these
        // destructor requests, so no per-window client resource is retained.
        xdg_toplevel_icon_v1_destroy(icon);
        DestroyWaylandToplevelIconBuffer(buffer);
        wl_display_flush(g_waylandDisplay);
        return JALIUM_OK;
#else
        (void)bgraPixels; (void)width; (void)height;
        return JALIUM_ERROR_NOT_SUPPORTED;
#endif
    }
#endif
    if (!g_display || !window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;

    Atom iconAtom = XInternAtom(g_display, "_NET_WM_ICON", False);
    if (!bgraPixels || width <= 0 || height <= 0)
    {
        XDeleteProperty(g_display, window->xwindow, iconAtom);
        XFlush(g_display);
        return JALIUM_OK;
    }

    // _NET_WM_ICON: CARDINAL[] of {width, height, ARGB pixels...}. BGRA bytes
    // little-endian *are* 0xAARRGGBB values, so pixels copy through unchanged;
    // the property array itself is `long`-sized.
    const size_t pixelCount = static_cast<size_t>(width) * static_cast<size_t>(height);
    std::vector<unsigned long> propertyData(2 + pixelCount);
    propertyData[0] = static_cast<unsigned long>(width);
    propertyData[1] = static_cast<unsigned long>(height);
    for (size_t i = 0; i < pixelCount; ++i)
        propertyData[2 + i] = bgraPixels[i];

    XChangeProperty(g_display, window->xwindow, iconAtom, XA_CARDINAL, 32,
                    PropModeReplace,
                    reinterpret_cast<const unsigned char*>(propertyData.data()),
                    static_cast<int>(propertyData.size()));
    XFlush(g_display);
    return JALIUM_OK;
}

int32_t jalium_window_set_topmost(JaliumPlatformWindow* window, int32_t topmost)
{
    if (!window)
        return JALIUM_ERROR_INVALID_ARGUMENT;

#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
        // No always-on-top in xdg-shell; compositors own stacking.
        (void)topmost;
        return JALIUM_ERROR_NOT_SUPPORTED;
    }
#endif
    if (!g_display || !window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;

    Atom netWmState = XInternAtom(g_display, "_NET_WM_STATE", False);
    Atom aboveAtom = XInternAtom(g_display, "_NET_WM_STATE_ABOVE", False);
    XEvent ev{};
    ev.type = ClientMessage;
    ev.xclient.window = window->xwindow;
    ev.xclient.message_type = netWmState;
    ev.xclient.format = 32;
    ev.xclient.data.l[0] = topmost ? 1 : 0; // _NET_WM_STATE_ADD / _REMOVE
    ev.xclient.data.l[1] = static_cast<long>(aboveAtom);
    XSendEvent(g_display, g_rootWindow, False,
               SubstructureRedirectMask | SubstructureNotifyMask, &ev);
    XFlush(g_display);
    return JALIUM_OK;
}

int32_t jalium_window_set_enabled(JaliumPlatformWindow* window, int32_t enabled)
{
    if (!window)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    window->enabled = enabled != 0;
    return JALIUM_OK;
}

int32_t jalium_window_set_opacity(JaliumPlatformWindow* window, double opacity)
{
    if (!window || !std::isfinite(opacity))
        return JALIUM_ERROR_INVALID_ARGUMENT;

#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
        // Core xdg-shell has no whole-toplevel opacity request. Per-pixel alpha
        // remains available through the transparent surface/render path.
        return JALIUM_ERROR_NOT_SUPPORTED;
    }
#endif
    if (!g_display || !window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;

    const double clamped = std::clamp(opacity, 0.0, 1.0);
    const uint32_t value = static_cast<uint32_t>(
        std::llround(clamped * static_cast<double>(UINT32_MAX)));
    const Atom opacityAtom = XInternAtom(g_display, "_NET_WM_WINDOW_OPACITY", False);
    const unsigned long propertyValue = value;
    XChangeProperty(g_display, window->xwindow, opacityAtom, XA_CARDINAL, 32,
                    PropModeReplace,
                    reinterpret_cast<const unsigned char*>(&propertyValue), 1);
    XFlush(g_display);
    return JALIUM_OK;
}

int32_t jalium_window_set_show_in_taskbar(
    JaliumPlatformWindow* window, int32_t showInTaskbar)
{
    if (!window)
        return JALIUM_ERROR_INVALID_ARGUMENT;
#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
        (void)showInTaskbar;
        return JALIUM_ERROR_NOT_SUPPORTED;
    }
#endif
    if (!g_display || !window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;

    const Atom netWmState = XInternAtom(g_display, "_NET_WM_STATE", False);
    const Atom skipTaskbar = XInternAtom(g_display, "_NET_WM_STATE_SKIP_TASKBAR", False);
    XWindowAttributes attributes{};
    if (XGetWindowAttributes(g_display, window->xwindow, &attributes) &&
        attributes.map_state != IsUnmapped)
    {
        XEvent event{};
        event.type = ClientMessage;
        event.xclient.window = window->xwindow;
        event.xclient.message_type = netWmState;
        event.xclient.format = 32;
        event.xclient.data.l[0] = showInTaskbar ? 0 : 1; // remove / add
        event.xclient.data.l[1] = static_cast<long>(skipTaskbar);
        event.xclient.data.l[3] = 1; // normal application source
        XSendEvent(g_display, g_rootWindow, False,
                   SubstructureRedirectMask | SubstructureNotifyMask, &event);
    }
    else
    {
        Atom actualType = None;
        int actualFormat = 0;
        unsigned long itemCount = 0;
        unsigned long remaining = 0;
        unsigned char* data = nullptr;
        std::vector<Atom> states;
        if (XGetWindowProperty(g_display, window->xwindow, netWmState, 0, 64,
                               False, XA_ATOM, &actualType, &actualFormat,
                               &itemCount, &remaining, &data) == Success && data)
        {
            const auto* atoms = reinterpret_cast<const Atom*>(data);
            states.assign(atoms, atoms + itemCount);
            XFree(data);
        }
        states.erase(std::remove(states.begin(), states.end(), skipTaskbar), states.end());
        if (!showInTaskbar)
            states.push_back(skipTaskbar);
        XChangeProperty(g_display, window->xwindow, netWmState, XA_ATOM, 32,
                        PropModeReplace,
                        reinterpret_cast<const unsigned char*>(states.data()),
                        static_cast<int>(states.size()));
    }
    XFlush(g_display);
    return JALIUM_OK;
}

int32_t jalium_window_set_resizable(JaliumPlatformWindow* window, int32_t resizable)
{
    if (!window)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    if (resizable)
        window->style |= JALIUM_WINDOW_STYLE_RESIZABLE;
    else
        window->style &= ~static_cast<uint32_t>(JALIUM_WINDOW_STYLE_RESIZABLE);

    return jalium_window_set_min_max_size(
        window,
        window->requestedMinWidth,
        window->requestedMinHeight,
        window->requestedMaxWidth,
        window->requestedMaxHeight);
}

int32_t jalium_window_set_decorated(JaliumPlatformWindow* window, int32_t decorated)
{
    if (!window)
        return JALIUM_ERROR_INVALID_ARGUMENT;
#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
        if (decorated)
            window->style &= ~static_cast<uint32_t>(JALIUM_WINDOW_STYLE_BORDERLESS);
        else
            window->style |= JALIUM_WINDOW_STYLE_BORDERLESS;
#ifdef JALIUM_HAS_XDG_DECORATION_V1
        CreateWaylandDecoration(window);
        if (!window->xdgDecoration)
            return JALIUM_ERROR_NOT_SUPPORTED;
        zxdg_toplevel_decoration_v1_set_mode(
            window->xdgDecoration,
            decorated
                ? ZXDG_TOPLEVEL_DECORATION_V1_MODE_SERVER_SIDE
                : ZXDG_TOPLEVEL_DECORATION_V1_MODE_CLIENT_SIDE);
        wl_surface_commit(window->waylandSurface);
        return wl_display_flush(g_waylandDisplay) < 0
            ? JALIUM_ERROR_UNKNOWN : JALIUM_OK;
#else
        return JALIUM_ERROR_NOT_SUPPORTED;
#endif
    }
#endif
    if (!g_display || !window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;

    const Atom motifHints = XInternAtom(g_display, "_MOTIF_WM_HINTS", False);
    struct MotifHints {
        unsigned long flags;
        unsigned long functions;
        unsigned long decorations;
        long inputMode;
        unsigned long status;
    } hints{2, 0, decorated ? 1ul : 0ul, 0, 0};
    XChangeProperty(g_display, window->xwindow, motifHints, motifHints, 32,
                    PropModeReplace,
                    reinterpret_cast<const unsigned char*>(&hints), 5);
    if (decorated)
        window->style &= ~static_cast<uint32_t>(JALIUM_WINDOW_STYLE_BORDERLESS);
    else
        window->style |= JALIUM_WINDOW_STYLE_BORDERLESS;
    XFlush(g_display);
    return JALIUM_OK;
}

int32_t jalium_window_set_owner(
    JaliumPlatformWindow* window, intptr_t ownerNativeHandle)
{
    if (!window || ownerNativeHandle == jalium_window_get_native_handle(window))
        return JALIUM_ERROR_INVALID_ARGUMENT;

    window->parentHandle = ownerNativeHandle;
#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
        if (!window->xdgToplevel && !CreateWaylandRole(window))
            return JALIUM_ERROR_INVALID_STATE;
        xdg_toplevel* parentToplevel = nullptr;
        if (ownerNativeHandle != 0)
        {
            auto* parentSurface = reinterpret_cast<wl_surface*>(ownerNativeHandle);
            std::lock_guard<std::mutex> lock(g_windowMapMutex);
            for (JaliumPlatformWindow* candidate : g_waylandWindows)
            {
                if (candidate && candidate->waylandSurface == parentSurface)
                {
                    parentToplevel = candidate->xdgToplevel;
                    break;
                }
            }
            if (!parentToplevel)
                return JALIUM_ERROR_INVALID_ARGUMENT;
        }
        xdg_toplevel_set_parent(window->xdgToplevel, parentToplevel);
        wl_surface_commit(window->waylandSurface);
        wl_display_flush(g_waylandDisplay);
        return JALIUM_OK;
    }
#endif
    if (!g_display || !window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;

    if (ownerNativeHandle != 0)
        XSetTransientForHint(g_display, window->xwindow, static_cast<Window>(ownerNativeHandle));
    else
        XDeleteProperty(g_display, window->xwindow, XInternAtom(g_display, "WM_TRANSIENT_FOR", False));
    XFlush(g_display);
    return JALIUM_OK;
}

int32_t jalium_window_activate(JaliumPlatformWindow* window)
{
    if (!window)
        return JALIUM_ERROR_INVALID_ARGUMENT;
#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
#ifdef JALIUM_HAS_XDG_ACTIVATION_V1
        if (!g_xdgActivation || !window->xdgToplevel)
            return JALIUM_ERROR_NOT_SUPPORTED;

        const char* inheritedToken = getenv("XDG_ACTIVATION_TOKEN");
        if (inheritedToken && *inheritedToken)
        {
            xdg_activation_v1_activate(
                g_xdgActivation, inheritedToken, window->waylandSurface);
            unsetenv("XDG_ACTIVATION_TOKEN");
            return wl_display_flush(g_waylandDisplay) < 0
                ? JALIUM_ERROR_UNKNOWN : JALIUM_OK;
        }

        xdg_activation_token_v1* token =
            xdg_activation_v1_get_activation_token(g_xdgActivation);
        if (!token)
            return JALIUM_ERROR_UNKNOWN;

        auto* request = new (std::nothrow) WaylandActivationRequest();
        if (!request)
        {
            xdg_activation_token_v1_destroy(token);
            return JALIUM_ERROR_OUT_OF_MEMORY;
        }
        request->token = token;
        request->window = window;
        g_waylandActivationRequests.insert(request);
        xdg_activation_token_v1_add_listener(
            token, &g_activationTokenListener, request);
        if (g_waylandSeat && g_waylandInputSerial != 0)
        {
            xdg_activation_token_v1_set_serial(
                token, g_waylandInputSerial, g_waylandSeat);
        }
        xdg_activation_token_v1_set_surface(token, window->waylandSurface);
        xdg_activation_token_v1_commit(token);
        if (wl_display_flush(g_waylandDisplay) < 0)
        {
            g_waylandActivationRequests.erase(request);
            xdg_activation_token_v1_destroy(token);
            delete request;
            return JALIUM_ERROR_UNKNOWN;
        }
        return JALIUM_OK;
#else
        return JALIUM_ERROR_NOT_SUPPORTED;
#endif
    }
#endif
    if (!g_display || !window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;

    XWindowAttributes attributes{};
    if (!XGetWindowAttributes(g_display, window->xwindow, &attributes) ||
        attributes.map_state == IsUnmapped)
    {
        XMapRaised(g_display, window->xwindow);
    }

    XEvent event{};
    event.type = ClientMessage;
    event.xclient.window = window->xwindow;
    event.xclient.message_type = XInternAtom(g_display, "_NET_ACTIVE_WINDOW", False);
    event.xclient.format = 32;
    event.xclient.data.l[0] = 1; // normal application
    event.xclient.data.l[1] = CurrentTime;
    XSendEvent(g_display, g_rootWindow, False,
               SubstructureRedirectMask | SubstructureNotifyMask, &event);
    XFlush(g_display);
    return JALIUM_OK;
}

int32_t jalium_window_show_system_menu(
    JaliumPlatformWindow* window, int32_t x, int32_t y)
{
    if (!window)
        return JALIUM_ERROR_INVALID_ARGUMENT;
#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
        if (!window->xdgToplevel || !g_waylandSeat ||
            g_waylandInputSerial == 0)
            return JALIUM_ERROR_NOT_SUPPORTED;

        const int32_t scale = std::max(window->waylandScale, 1);
#ifdef JALIUM_PLATFORM_TEST_HOOKS
        g_testSystemMenuWindow = window;
        g_testSystemMenuX = x;
        g_testSystemMenuY = y;
        g_testSystemMenuSerial = g_waylandInputSerial;
#endif
        xdg_toplevel_show_window_menu(
            window->xdgToplevel, g_waylandSeat, g_waylandInputSerial,
            x / scale, y / scale);
        return wl_display_flush(g_waylandDisplay) < 0
            ? JALIUM_ERROR_UNKNOWN : JALIUM_OK;
    }
#endif
    if (!g_display || !window->xwindow)
        return JALIUM_ERROR_INVALID_STATE;

    const Atom showMenu = XInternAtom(g_display, "_GTK_SHOW_WINDOW_MENU", False);
    const Atom netSupported = XInternAtom(g_display, "_NET_SUPPORTED", False);
    Atom actualType = None;
    int actualFormat = 0;
    unsigned long itemCount = 0;
    unsigned long remaining = 0;
    unsigned char* raw = nullptr;
    bool supported = false;
    if (XGetWindowProperty(
            g_display, g_rootWindow, netSupported, 0, 4096, False, XA_ATOM,
            &actualType, &actualFormat, &itemCount, &remaining, &raw) == Success &&
        raw && actualType == XA_ATOM && actualFormat == 32)
    {
        const auto* atoms = reinterpret_cast<const Atom*>(raw);
        supported = std::find(atoms, atoms + itemCount, showMenu) !=
                    atoms + itemCount;
    }
    if (raw) XFree(raw);
    if (!supported)
        return JALIUM_ERROR_NOT_SUPPORTED;

    int32_t rootX = x;
    int32_t rootY = y;
    Window child = None;
    if (!XTranslateCoordinates(
            g_display, window->xwindow, g_rootWindow, x, y,
            &rootX, &rootY, &child))
        return JALIUM_ERROR_UNKNOWN;

    int32_t deviceId = 0;
#ifdef JALIUM_HAS_XINPUT2
    if (g_xinput2Available)
        (void)XIGetClientPointer(g_display, window->xwindow, &deviceId);
#endif

    XEvent event{};
    event.type = ClientMessage;
    event.xclient.window = window->xwindow;
    event.xclient.message_type = showMenu;
    event.xclient.format = 32;
    event.xclient.data.l[0] = deviceId;
    event.xclient.data.l[1] = rootX;
    event.xclient.data.l[2] = rootY;
    XSendEvent(g_display, g_rootWindow, False,
               SubstructureRedirectMask | SubstructureNotifyMask, &event);
    XFlush(g_display);
    return JALIUM_OK;
}

int32_t jalium_window_update_ime_context(
    JaliumPlatformWindow* window, int32_t enabled, const char* utf8Text,
    int32_t cursorByteOffset, int32_t anchorByteOffset,
    int32_t x, int32_t y, int32_t width, int32_t height)
{
    if (!window || cursorByteOffset < 0 || anchorByteOffset < 0 ||
        width < 0 || height < 0)
        return JALIUM_ERROR_INVALID_ARGUMENT;

#ifdef JALIUM_HAS_WAYLAND
    if (window->waylandSurface)
    {
        window->imeEnabled = enabled != 0;
        window->imeSurroundingText = utf8Text ? utf8Text : "";
        const int32_t textLength = static_cast<int32_t>(std::min<size_t>(
            window->imeSurroundingText.size(),
            static_cast<size_t>(std::numeric_limits<int32_t>::max())));
        window->imeCursorByteOffset = std::clamp(cursorByteOffset, 0, textLength);
        window->imeAnchorByteOffset = std::clamp(anchorByteOffset, 0, textLength);
        window->imeCaretX = x;
        window->imeCaretY = y;
        window->imeCaretWidth = std::max(width, 1);
        window->imeCaretHeight = std::max(height, 1);
        if (!window->imeEnabled)
        {
            window->imeSurroundingText.clear();
            window->imeCursorByteOffset = 0;
            window->imeAnchorByteOffset = 0;
        }

        bool supported = false;
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V3
        if (g_waylandTextInputManager && g_waylandTextInput)
        {
            supported = true;
            ApplyWaylandTextInputV3(window);
        }
#endif
#ifdef JALIUM_HAS_WAYLAND_TEXT_INPUT_V1
        if (!supported && g_waylandTextInputManagerV1 && g_waylandTextInputV1)
        {
            supported = true;
            ApplyWaylandTextInputV1(window);
        }
#endif
        if (!supported)
            return JALIUM_ERROR_NOT_SUPPORTED;
        return wl_display_flush(g_waylandDisplay) < 0
            ? JALIUM_ERROR_UNKNOWN : JALIUM_OK;
    }
#endif

    // XIM handles preedit/commit, but X11 has no portable surrounding-text or
    // candidate-rectangle contract equivalent to text-input-v3.
    return JALIUM_ERROR_NOT_SUPPORTED;
}

#endif // __linux__ && !__ANDROID__
