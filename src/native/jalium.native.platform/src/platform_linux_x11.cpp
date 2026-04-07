#if defined(__linux__) && !defined(__ANDROID__)

#include "jalium_platform.h"

#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/Xatom.h>
#include <X11/keysym.h>
#include <X11/XKBlib.h>

#ifdef JALIUM_HAS_XRANDR
#include <X11/extensions/Xrandr.h>
#endif

#include <sys/eventfd.h>
#include <sys/timerfd.h>
#include <sys/epoll.h>
#include <unistd.h>
#include <poll.h>
#include <time.h>
#include <string.h>
#include <stdlib.h>
#include <wchar.h>
#include <locale.h>

#include <atomic>
#include <mutex>
#include <unordered_map>
#include <vector>
#include <string>

// ============================================================================
// Global State
// ============================================================================

static Display*     g_display = nullptr;
static int          g_screen = 0;
static Window       g_rootWindow = 0;
static Atom         g_wmDeleteWindow = 0;
static Atom         g_wmProtocols = 0;
static XIM          g_xim = nullptr;
static int          g_epollFd = -1;
static int          g_wakeEventFd = -1;   // eventfd for cross-thread wake
static std::atomic<bool> g_quitRequested{false};
static std::atomic<int32_t> g_exitCode{0};

static std::mutex g_windowMapMutex;
static std::unordered_map<Window, JaliumPlatformWindow*> g_windowMap;

// ============================================================================
// Window Structure
// ============================================================================

struct JaliumPlatformWindow {
    Window              xwindow = 0;
    XIC                 xic = nullptr;
    JaliumEventCallback callback = nullptr;
    void*               userData = nullptr;
    uint32_t            style = 0;
    int32_t             width = 0;
    int32_t             height = 0;
    float               dpiScale = 1.0f;
    bool                destroyed = false;

    void DispatchEvent(const JaliumPlatformEvent& evt)
    {
        if (callback && !destroyed)
            callback(&evt, userData);
    }
};

// ============================================================================
// Dispatcher Structure
// ============================================================================

struct JaliumDispatcher {
    int                     eventFd = -1;
    JaliumDispatcherCallback callback = nullptr;
    void*                   userData = nullptr;
};

// ============================================================================
// Timer Structure
// ============================================================================

struct JaliumTimer {
    int                 timerFd = -1;
    JaliumTimerCallback callback = nullptr;
    void*               userData = nullptr;
};

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

// ============================================================================
// Platform Init / Shutdown
// ============================================================================

JaliumResult jalium_platform_init_impl()
{
    setlocale(LC_ALL, "");
    XrmInitialize();

    // Enable thread support
    if (!XInitThreads())
        return JALIUM_ERROR_INITIALIZATION_FAILED;

    g_display = XOpenDisplay(nullptr);
    if (!g_display)
        return JALIUM_ERROR_INITIALIZATION_FAILED;

    g_screen = DefaultScreen(g_display);
    g_rootWindow = RootWindow(g_display, g_screen);

    g_wmDeleteWindow = XInternAtom(g_display, "WM_DELETE_WINDOW", False);
    g_wmProtocols = XInternAtom(g_display, "WM_PROTOCOLS", False);

    // XIM for text input
    g_xim = XOpenIM(g_display, nullptr, nullptr, nullptr);

    // Create epoll for multiplexing X11 + eventfd + timerfds
    g_epollFd = epoll_create1(0);
    if (g_epollFd < 0)
    {
        XCloseDisplay(g_display);
        g_display = nullptr;
        return JALIUM_ERROR_INITIALIZATION_FAILED;
    }

    // Add X11 connection fd to epoll
    int x11Fd = ConnectionNumber(g_display);
    struct epoll_event ev{};
    ev.events = EPOLLIN;
    ev.data.fd = x11Fd;
    epoll_ctl(g_epollFd, EPOLL_CTL_ADD, x11Fd, &ev);

    // Create wake eventfd
    g_wakeEventFd = eventfd(0, EFD_NONBLOCK | EFD_CLOEXEC);
    if (g_wakeEventFd >= 0)
    {
        ev.events = EPOLLIN;
        ev.data.fd = g_wakeEventFd;
        epoll_ctl(g_epollFd, EPOLL_CTL_ADD, g_wakeEventFd, &ev);
    }

    return JALIUM_OK;
}

void jalium_platform_shutdown_impl()
{
    if (g_wakeEventFd >= 0) { close(g_wakeEventFd); g_wakeEventFd = -1; }
    if (g_epollFd >= 0) { close(g_epollFd); g_epollFd = -1; }
    if (g_xim) { XCloseIM(g_xim); g_xim = nullptr; }
    if (g_display) { XCloseDisplay(g_display); g_display = nullptr; }
}

JaliumPlatform jalium_platform_get_current_impl()
{
    return JALIUM_PLATFORM_LINUX_X11;
}

// ============================================================================
// Window Management
// ============================================================================

JaliumPlatformWindow* jalium_window_create(const JaliumWindowParams* params)
{
    if (!params || !g_display) return nullptr;

    auto win = new JaliumPlatformWindow();
    win->style = params->style;
    win->width = params->width > 0 ? params->width : 800;
    win->height = params->height > 0 ? params->height : 600;
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

    int x = (params->x == JALIUM_DEFAULT_POS) ? 0 : params->x;
    int y = (params->y == JALIUM_DEFAULT_POS) ? 0 : params->y;

    win->xwindow = XCreateWindow(
        g_display, g_rootWindow,
        x, y, win->width, win->height,
        0,
        CopyFromParent, InputOutput, CopyFromParent,
        valueMask, &swa
    );

    if (!win->xwindow)
    {
        delete win;
        return nullptr;
    }

    // Set WM_DELETE_WINDOW protocol
    XSetWMProtocols(g_display, win->xwindow, &g_wmDeleteWindow, 1);

    // Set window title
    if (params->title)
    {
        // Convert wchar_t to UTF-8
        size_t wlen = wcslen(params->title);
        size_t bufSize = wlen * 4 + 1;
        char* utf8Title = static_cast<char*>(malloc(bufSize));
        if (utf8Title)
        {
            wcstombs(utf8Title, params->title, bufSize);
            XStoreName(g_display, win->xwindow, utf8Title);

            // Set _NET_WM_NAME for UTF-8
            Atom netWmName = XInternAtom(g_display, "_NET_WM_NAME", False);
            Atom utf8String = XInternAtom(g_display, "UTF8_STRING", False);
            XChangeProperty(g_display, win->xwindow, netWmName, utf8String,
                           8, PropModeReplace,
                           reinterpret_cast<unsigned char*>(utf8Title),
                           static_cast<int>(strlen(utf8Title)));
            free(utf8Title);
        }
    }

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
        win->xic = XCreateIC(g_xim,
                             XNInputStyle, XIMPreeditNothing | XIMStatusNothing,
                             XNClientWindow, win->xwindow,
                             XNFocusWindow, win->xwindow,
                             nullptr);
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
    if (window && g_display)
    {
        XMapRaised(g_display, window->xwindow);
        XFlush(g_display);
    }
}

void jalium_window_hide(JaliumPlatformWindow* window)
{
    if (window && g_display)
    {
        XUnmapWindow(g_display, window->xwindow);
        XFlush(g_display);
    }
}

void jalium_window_set_title(JaliumPlatformWindow* window, const wchar_t* title)
{
    if (!window || !g_display || !title) return;

    size_t wlen = wcslen(title);
    size_t bufSize = wlen * 4 + 1;
    char* utf8 = static_cast<char*>(malloc(bufSize));
    if (utf8)
    {
        wcstombs(utf8, title, bufSize);
        XStoreName(g_display, window->xwindow, utf8);

        Atom netWmName = XInternAtom(g_display, "_NET_WM_NAME", False);
        Atom utf8String = XInternAtom(g_display, "UTF8_STRING", False);
        XChangeProperty(g_display, window->xwindow, netWmName, utf8String,
                       8, PropModeReplace,
                       reinterpret_cast<unsigned char*>(utf8),
                       static_cast<int>(strlen(utf8)));
        free(utf8);
        XFlush(g_display);
    }
}

void jalium_window_resize(JaliumPlatformWindow* window, int32_t width, int32_t height)
{
    if (window && g_display)
    {
        XResizeWindow(g_display, window->xwindow, width, height);
        XFlush(g_display);
    }
}

void jalium_window_move(JaliumPlatformWindow* window, int32_t x, int32_t y)
{
    if (window && g_display)
    {
        XMoveWindow(g_display, window->xwindow, x, y);
        XFlush(g_display);
    }
}

void jalium_window_set_state(JaliumPlatformWindow* window, JaliumWindowState state)
{
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
    // TODO: Query _NET_WM_STATE atoms
    return JALIUM_WINDOW_STATE_NORMAL;
}

intptr_t jalium_window_get_native_handle(JaliumPlatformWindow* window)
{
    if (!window) return 0;
    return static_cast<intptr_t>(window->xwindow);
}

JaliumSurfaceDescriptor jalium_window_get_surface(JaliumPlatformWindow* window)
{
    JaliumSurfaceDescriptor desc{};
    if (window && g_display)
    {
        desc.platform = JALIUM_PLATFORM_LINUX_X11;
        desc.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        desc.handle0 = reinterpret_cast<intptr_t>(g_display);
        desc.handle1 = static_cast<intptr_t>(window->xwindow);
    }
    return desc;
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
    if (!window || !g_display)
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
        evt.type = JALIUM_EVENT_FOCUS_GAINED;
        win->DispatchEvent(evt);
        break;

    case FocusOut:
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
            evt.mouse.clickCount = 1; // TODO: double-click detection
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
        evt.type = JALIUM_EVENT_KEY_DOWN;
        evt.key.keyCode = KeySymToJaliumVK(keysym);
        evt.key.scanCode = xev.xkey.keycode;
        evt.key.modifiers = GetX11Modifiers(xev.xkey.state);
        evt.key.isRepeat = 0; // TODO: detect repeat
        win->DispatchEvent(evt);

        // Text input via XIC
        if (win->xic)
        {
            char buf[32];
            KeySym sym;
            Status status;
            int len = Xutf8LookupString(win->xic, &xev.xkey, buf, sizeof(buf) - 1, &sym, &status);
            if (len > 0 && (status == XLookupChars || status == XLookupBoth))
            {
                buf[len] = '\0';
                // Decode UTF-8 to codepoint(s)
                const unsigned char* p = reinterpret_cast<const unsigned char*>(buf);
                while (*p)
                {
                    uint32_t cp = 0;
                    if (*p < 0x80)
                        cp = *p++;
                    else if (*p < 0xE0) {
                        cp = (*p++ & 0x1F) << 6;
                        if (*p) cp |= (*p++ & 0x3F);
                    } else if (*p < 0xF0) {
                        cp = (*p++ & 0x0F) << 12;
                        if (*p) { cp |= (*p++ & 0x3F) << 6; }
                        if (*p) { cp |= (*p++ & 0x3F); }
                    } else {
                        cp = (*p++ & 0x07) << 18;
                        if (*p) { cp |= (*p++ & 0x3F) << 12; }
                        if (*p) { cp |= (*p++ & 0x3F) << 6; }
                        if (*p) { cp |= (*p++ & 0x3F); }
                    }

                    if (cp >= 0x20 && cp != 0x7F) // Skip control characters
                    {
                        JaliumPlatformEvent charEvt{};
                        charEvt.type = JALIUM_EVENT_CHAR_INPUT;
                        charEvt.window = win;
                        charEvt.character.codepoint = cp;
                        win->DispatchEvent(charEvt);
                    }
                }
            }
        }
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
                XNextEvent(g_display, &next); // consume the KeyPress

                // Re-dispatch as KEY_DOWN with isRepeat=1
                KeySym keysym = XkbKeycodeToKeysym(g_display, next.xkey.keycode, 0, 0);
                JaliumPlatformEvent repeatEvt{};
                repeatEvt.type = JALIUM_EVENT_KEY_DOWN;
                repeatEvt.window = win;
                repeatEvt.key.keyCode = KeySymToJaliumVK(keysym);
                repeatEvt.key.scanCode = next.xkey.keycode;
                repeatEvt.key.modifiers = GetX11Modifiers(next.xkey.state);
                repeatEvt.key.isRepeat = 1;
                win->DispatchEvent(repeatEvt);
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
    g_quitRequested = false;

    while (!g_quitRequested.load(std::memory_order_acquire))
    {
        // Process all pending X11 events
        while (XPending(g_display))
        {
            XEvent xev;
            XNextEvent(g_display, &xev);
            ProcessXEvent(xev);
        }

        if (g_quitRequested.load(std::memory_order_acquire))
            break;

        // Wait for events via epoll (X11 fd + dispatcher eventfds + timerfds)
        struct epoll_event events[16];
        int nfds = epoll_wait(g_epollFd, events, 16, 100); // 100ms timeout

        for (int i = 0; i < nfds; i++)
        {
            if (events[i].data.fd == g_wakeEventFd)
            {
                // Drain eventfd
                uint64_t val;
                read(g_wakeEventFd, &val, sizeof(val));
            }
        }
    }

    return g_exitCode.load(std::memory_order_acquire);
}

int32_t jalium_platform_poll_events(void)
{
    int32_t count = 0;
    while (XPending(g_display))
    {
        XEvent xev;
        XNextEvent(g_display, &xev);
        ProcessXEvent(xev);
        count++;
    }
    return count;
}

void jalium_platform_quit(int32_t exitCode)
{
    g_exitCode = exitCode;
    g_quitRequested = true;

    // Wake the event loop
    if (g_wakeEventFd >= 0)
    {
        uint64_t val = 1;
        write(g_wakeEventFd, &val, sizeof(val));
    }
}

// ============================================================================
// Dispatcher (eventfd)
// ============================================================================

JaliumResult jalium_dispatcher_create(JaliumDispatcher** outDispatcher)
{
    if (!outDispatcher) return JALIUM_ERROR_INVALID_ARGUMENT;

    auto disp = new JaliumDispatcher();
    disp->eventFd = eventfd(0, EFD_NONBLOCK | EFD_CLOEXEC);
    if (disp->eventFd < 0)
    {
        delete disp;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    // Add to epoll for event loop integration
    if (g_epollFd >= 0)
    {
        struct epoll_event ev{};
        ev.events = EPOLLIN;
        ev.data.fd = disp->eventFd;
        epoll_ctl(g_epollFd, EPOLL_CTL_ADD, disp->eventFd, &ev);
    }

    *outDispatcher = disp;
    return JALIUM_OK;
}

void jalium_dispatcher_destroy(JaliumDispatcher* dispatcher)
{
    if (!dispatcher) return;
    if (dispatcher->eventFd >= 0)
    {
        if (g_epollFd >= 0)
            epoll_ctl(g_epollFd, EPOLL_CTL_DEL, dispatcher->eventFd, nullptr);
        close(dispatcher->eventFd);
    }
    delete dispatcher;
}

void jalium_dispatcher_wake(JaliumDispatcher* dispatcher)
{
    if (dispatcher && dispatcher->eventFd >= 0)
    {
        uint64_t val = 1;
        write(dispatcher->eventFd, &val, sizeof(val));
    }
}

void jalium_dispatcher_set_callback(JaliumDispatcher* dispatcher,
                                     JaliumDispatcherCallback callback, void* userData)
{
    if (!dispatcher) return;
    dispatcher->callback = callback;
    dispatcher->userData = userData;
}

// ============================================================================
// High-Resolution Timer (timerfd)
// ============================================================================

JaliumResult jalium_timer_create(JaliumTimer** outTimer)
{
    if (!outTimer) return JALIUM_ERROR_INVALID_ARGUMENT;

    auto timer = new JaliumTimer();
    timer->timerFd = timerfd_create(CLOCK_MONOTONIC, TFD_NONBLOCK | TFD_CLOEXEC);
    if (timer->timerFd < 0)
    {
        delete timer;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    *outTimer = timer;
    return JALIUM_OK;
}

void jalium_timer_destroy(JaliumTimer* timer)
{
    if (!timer) return;
    if (timer->timerFd >= 0)
        close(timer->timerFd);
    delete timer;
}

void jalium_timer_arm(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer || timer->timerFd < 0) return;

    struct itimerspec its{};
    its.it_value.tv_sec = intervalMicroseconds / 1000000;
    its.it_value.tv_nsec = (intervalMicroseconds % 1000000) * 1000;
    // it_interval = 0 → one-shot
    timerfd_settime(timer->timerFd, 0, &its, nullptr);
}

void jalium_timer_arm_repeating(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer || timer->timerFd < 0) return;

    struct itimerspec its{};
    its.it_value.tv_sec = intervalMicroseconds / 1000000;
    its.it_value.tv_nsec = (intervalMicroseconds % 1000000) * 1000;
    its.it_interval = its.it_value; // Repeating
    timerfd_settime(timer->timerFd, 0, &its, nullptr);
}

void jalium_timer_disarm(JaliumTimer* timer)
{
    if (!timer || timer->timerFd < 0) return;

    struct itimerspec its{};
    timerfd_settime(timer->timerFd, 0, &its, nullptr);
}

void jalium_timer_set_callback(JaliumTimer* timer, JaliumTimerCallback callback, void* userData)
{
    if (!timer) return;
    timer->callback = callback;
    timer->userData = userData;
}

int32_t jalium_timer_wait(JaliumTimer* timer, uint32_t timeoutMs)
{
    if (!timer || timer->timerFd < 0) return 0;

    struct pollfd pfd{};
    pfd.fd = timer->timerFd;
    pfd.events = POLLIN;

    int timeout = (timeoutMs == 0) ? -1 : static_cast<int>(timeoutMs);
    int ret = poll(&pfd, 1, timeout);
    if (ret > 0 && (pfd.revents & POLLIN))
    {
        uint64_t expirations;
        read(timer->timerFd, &expirations, sizeof(expirations));
        return 1;
    }
    return 0;
}

// ============================================================================
// DPI and Display
// ============================================================================

float jalium_platform_get_system_dpi_scale(void)
{
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
    // TODO: Maintain key state table from events
    // For now, query X11 keyboard state
    if (!g_display) return 0;

    char keys[32];
    XQueryKeymap(g_display, keys);

    // We need to map Jalium VK back to X11 keycode - reverse mapping
    // For simplicity, this is a basic implementation
    // A full implementation would maintain a state table from events
    return 0;
}

void jalium_input_get_cursor_pos(float* x, float* y)
{
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

static Atom g_clipboardAtom = 0;
static Atom g_utf8StringAtom = 0;
static Atom g_targetsAtom = 0;
static Atom g_jaliumClipProp = 0;

static void EnsureClipboardAtoms()
{
    if (!g_clipboardAtom && g_display)
    {
        g_clipboardAtom = XInternAtom(g_display, "CLIPBOARD", False);
        g_utf8StringAtom = XInternAtom(g_display, "UTF8_STRING", False);
        g_targetsAtom = XInternAtom(g_display, "TARGETS", False);
        g_jaliumClipProp = XInternAtom(g_display, "JALIUM_CLIPBOARD", False);
    }
}

JaliumResult jalium_clipboard_get_text(wchar_t** outText)
{
    if (!outText) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outText = nullptr;

    // TODO: Implement full X11 clipboard (CLIPBOARD selection with SelectionRequest/Notify)
    // This requires a persistent window to own the selection. For now, return empty.
    return JALIUM_OK;
}

JaliumResult jalium_clipboard_set_text(const wchar_t* text)
{
    if (!text) return JALIUM_ERROR_INVALID_ARGUMENT;

    // TODO: Implement full X11 clipboard (own CLIPBOARD selection, respond to SelectionRequest)
    return JALIUM_OK;
}

#endif // __linux__ && !__ANDROID__
