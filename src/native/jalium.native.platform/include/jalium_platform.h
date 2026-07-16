#pragma once

#include "jalium_types.h"

// Platform-specific export macros.
// JALIUM_STATIC (set globally for the NativeAOT static-link flavor) takes
// precedence over the legacy JALIUM_PLATFORM_STATIC; either collapses the
// macro so the same headers compile cleanly into a .lib without dllimport
// stubs.
#ifdef _WIN32
    #if defined(JALIUM_STATIC) || defined(JALIUM_PLATFORM_STATIC)
        #define JALIUM_PLATFORM_API
    #elif defined(JALIUM_PLATFORM_EXPORTS)
        #define JALIUM_PLATFORM_API __declspec(dllexport)
    #else
        #define JALIUM_PLATFORM_API __declspec(dllimport)
    #endif
#else
    #define JALIUM_PLATFORM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// ============================================================================
// Opaque Types
// ============================================================================

typedef struct JaliumPlatformWindow JaliumPlatformWindow;
typedef struct JaliumDispatcher JaliumDispatcher;
typedef struct JaliumTimer JaliumTimer;

/// A single UTF-16 code unit used by the platform C ABI.
///
/// Do not use wchar_t at this boundary: wchar_t is 16-bit on Windows but
/// 32-bit on Linux. Managed strings are UTF-16 on every supported runtime, so
/// a fixed-width type keeps the same ABI on every host.
typedef uint16_t JaliumUtf16Char;

// ============================================================================
// Enumerations
// ============================================================================

/// Window style flags (combined with OR).
typedef enum JaliumWindowStyle {
    JALIUM_WINDOW_STYLE_DEFAULT       = 0,
    JALIUM_WINDOW_STYLE_BORDERLESS    = 1 << 0,
    JALIUM_WINDOW_STYLE_RESIZABLE     = 1 << 1,
    JALIUM_WINDOW_STYLE_TITLEBAR      = 1 << 2,
    JALIUM_WINDOW_STYLE_CLOSABLE      = 1 << 3,
    JALIUM_WINDOW_STYLE_MINIMIZABLE   = 1 << 4,
    JALIUM_WINDOW_STYLE_MAXIMIZABLE   = 1 << 5,
    JALIUM_WINDOW_STYLE_TOPMOST       = 1 << 6,
    JALIUM_WINDOW_STYLE_POPUP         = 1 << 7,
    JALIUM_WINDOW_STYLE_TRANSPARENT   = 1 << 8,  ///< Per-pixel alpha (WS_EX_NOREDIRECTIONBITMAP)
    JALIUM_WINDOW_STYLE_POPUP_GRAB    = 1 << 9,  ///< Light-dismiss/menu popup with an explicit input grab
} JaliumWindowStyle;

/// Window state.
typedef enum JaliumWindowState {
    JALIUM_WINDOW_STATE_NORMAL    = 0,
    JALIUM_WINDOW_STATE_MINIMIZED = 1,
    JALIUM_WINDOW_STATE_MAXIMIZED = 2,
    JALIUM_WINDOW_STATE_FULLSCREEN = 3,
} JaliumWindowState;

/// Platform event types.
typedef enum JaliumEventType {
    JALIUM_EVENT_NONE = 0,

    // Window lifecycle
    JALIUM_EVENT_CLOSE_REQUESTED  = 1,
    JALIUM_EVENT_DESTROYED        = 2,
    JALIUM_EVENT_RESIZE           = 3,
    JALIUM_EVENT_MOVE             = 4,
    JALIUM_EVENT_DPI_CHANGED      = 5,
    JALIUM_EVENT_PAINT            = 6,
    JALIUM_EVENT_ACTIVATE         = 7,
    JALIUM_EVENT_DEACTIVATE       = 8,
    JALIUM_EVENT_STATE_CHANGED    = 9,
    JALIUM_EVENT_MONITORS_CHANGED = 10,

    // Focus
    JALIUM_EVENT_FOCUS_GAINED     = 20,
    JALIUM_EVENT_FOCUS_LOST       = 21,

    // Mouse
    JALIUM_EVENT_MOUSE_MOVE       = 30,
    JALIUM_EVENT_MOUSE_DOWN       = 31,
    JALIUM_EVENT_MOUSE_UP         = 32,
    JALIUM_EVENT_MOUSE_WHEEL      = 33,
    JALIUM_EVENT_MOUSE_ENTER      = 34,
    JALIUM_EVENT_MOUSE_LEAVE      = 35,

    // Keyboard
    JALIUM_EVENT_KEY_DOWN         = 40,
    JALIUM_EVENT_KEY_UP           = 41,
    JALIUM_EVENT_CHAR_INPUT       = 42,
    JALIUM_EVENT_COMPOSITION_START = 43,
    JALIUM_EVENT_COMPOSITION_UPDATE = 44,
    JALIUM_EVENT_COMPOSITION_END   = 45,
    JALIUM_EVENT_DELETE_SURROUNDING_TEXT = 46,

    // Pointer (touch/pen)
    JALIUM_EVENT_POINTER_DOWN     = 50,
    JALIUM_EVENT_POINTER_UP       = 51,
    JALIUM_EVENT_POINTER_MOVE     = 52,
    JALIUM_EVENT_POINTER_CANCEL   = 53,

    // Application lifecycle (mobile)
    JALIUM_EVENT_APP_PAUSE        = 60,
    JALIUM_EVENT_APP_RESUME       = 61,
    JALIUM_EVENT_APP_DESTROY      = 62,
    JALIUM_EVENT_LOW_MEMORY       = 63,
    JALIUM_EVENT_SAFE_AREA_CHANGED = 64,
    JALIUM_EVENT_KEYBOARD_CHANGED  = 65,
    JALIUM_EVENT_ORIENTATION_CHANGED = 66,

    // Dispatcher
    JALIUM_EVENT_DISPATCHER_WAKE  = 70,

    // Drag and drop. String/data pointers in these events are valid only for
    // the duration of the synchronous window callback.
    JALIUM_EVENT_DRAG_ENTER       = 80,
    JALIUM_EVENT_DRAG_OVER        = 81,
    JALIUM_EVENT_DRAG_LEAVE       = 82,
    JALIUM_EVENT_DROP             = 83,
    JALIUM_EVENT_DRAG_FINISHED    = 84,

    // Application
    JALIUM_EVENT_QUIT             = 99,
} JaliumEventType;

/// Drag operation effects (combined with OR). Values intentionally match
/// Jalium.UI DragDropEffects and the common X11/Wayland action mapping.
typedef enum JaliumDragEffect {
    JALIUM_DRAG_EFFECT_NONE = 0,
    JALIUM_DRAG_EFFECT_COPY = 1 << 0,
    JALIUM_DRAG_EFFECT_MOVE = 1 << 1,
    JALIUM_DRAG_EFFECT_LINK = 1 << 2,
} JaliumDragEffect;

/// Mouse button identifiers.
typedef enum JaliumMouseButton {
    JALIUM_MOUSE_BUTTON_LEFT   = 0,
    JALIUM_MOUSE_BUTTON_RIGHT  = 1,
    JALIUM_MOUSE_BUTTON_MIDDLE = 2,
    JALIUM_MOUSE_BUTTON_X1     = 3,
    JALIUM_MOUSE_BUTTON_X2     = 4,
} JaliumMouseButton;

/// Modifier key flags (combined with OR).
typedef enum JaliumModifiers {
    JALIUM_MOD_NONE    = 0,
    JALIUM_MOD_SHIFT   = 1 << 0,
    JALIUM_MOD_CTRL    = 1 << 1,
    JALIUM_MOD_ALT     = 1 << 2,
    JALIUM_MOD_META    = 1 << 3,  ///< Windows key / Command / Super
    JALIUM_MOD_CAPS    = 1 << 4,
    JALIUM_MOD_NUM     = 1 << 5,
} JaliumModifiers;

/// Pointer device type.
typedef enum JaliumPointerType {
    JALIUM_POINTER_MOUSE = 0,
    JALIUM_POINTER_TOUCH = 1,
    JALIUM_POINTER_PEN   = 2,
} JaliumPointerType;

/// Rich pointer state flags (combined with OR). InRange and InContact are
/// intentionally independent so pen hover is represented without inventing a
/// press. Eraser describes the active end; Inverted records a physically
/// flipped/inverted tool when the backend can prove it.
typedef enum JaliumPointerFlags {
    JALIUM_POINTER_FLAG_NONE       = 0,
    JALIUM_POINTER_FLAG_IN_RANGE   = 1 << 0,
    JALIUM_POINTER_FLAG_IN_CONTACT = 1 << 1,
    JALIUM_POINTER_FLAG_PRIMARY    = 1 << 2,
    JALIUM_POINTER_FLAG_ERASER     = 1 << 3,
    JALIUM_POINTER_FLAG_INVERTED   = 1 << 4,
    JALIUM_POINTER_FLAG_BARREL     = 1 << 5,
} JaliumPointerFlags;

/// Physical tablet tool shape. Unknown is used for touch and for backends that
/// cannot distinguish a specific tool without guessing.
typedef enum JaliumPointerToolType {
    JALIUM_POINTER_TOOL_UNKNOWN  = 0,
    JALIUM_POINTER_TOOL_PEN      = 1,
    JALIUM_POINTER_TOOL_ERASER   = 2,
    JALIUM_POINTER_TOOL_BRUSH    = 3,
    JALIUM_POINTER_TOOL_PENCIL   = 4,
    JALIUM_POINTER_TOOL_AIRBRUSH = 5,
    JALIUM_POINTER_TOOL_MOUSE    = 6,
    JALIUM_POINTER_TOOL_LENS     = 7,
} JaliumPointerToolType;

/// Pointer/tablet button state (combined with OR). Primary is the contact/tip
/// button. Barrel and secondary stay separate so two-button styli are not
/// collapsed into a single Boolean.
typedef enum JaliumPointerButtons {
    JALIUM_POINTER_BUTTON_NONE      = 0,
    JALIUM_POINTER_BUTTON_PRIMARY   = 1 << 0,
    JALIUM_POINTER_BUTTON_SECONDARY = 1 << 1,
    JALIUM_POINTER_BUTTON_TERTIARY  = 1 << 2,
    JALIUM_POINTER_BUTTON_BARREL    = 1 << 3,
    JALIUM_POINTER_BUTTON_ERASER    = 1 << 4,
} JaliumPointerButtons;

/// System cursor shapes.
typedef enum JaliumCursorShape {
    JALIUM_CURSOR_ARROW       = 0,
    JALIUM_CURSOR_HAND        = 1,
    JALIUM_CURSOR_IBEAM       = 2,
    JALIUM_CURSOR_CROSSHAIR   = 3,
    JALIUM_CURSOR_RESIZE_NS   = 4,
    JALIUM_CURSOR_RESIZE_EW   = 5,
    JALIUM_CURSOR_RESIZE_NESW = 6,
    JALIUM_CURSOR_RESIZE_NWSE = 7,
    JALIUM_CURSOR_RESIZE_ALL  = 8,
    JALIUM_CURSOR_NOT_ALLOWED = 9,
    JALIUM_CURSOR_WAIT        = 10,
    JALIUM_CURSOR_HIDDEN      = 11,
} JaliumCursorShape;

// ============================================================================
// Structures
// ============================================================================

/// Window creation parameters.
typedef struct JaliumWindowParams {
    const JaliumUtf16Char* title;    ///< Null-terminated UTF-16 title.
    int32_t         x;              ///< Initial X position (JALIUM_DEFAULT_POS = -1 for system default)
    int32_t         y;              ///< Initial Y position
    int32_t         width;
    int32_t         height;
    uint32_t        style;          ///< Combination of JaliumWindowStyle flags
    intptr_t        parentHandle;   ///< Parent window native handle (0 = no parent)
} JaliumWindowParams;

#define JALIUM_DEFAULT_POS (-1)

/// Unified platform event structure.
typedef struct JaliumPlatformEvent {
    JaliumEventType type;
    JaliumPlatformWindow* window;

    union {
        // JALIUM_EVENT_RESIZE
        struct {
            int32_t width;
            int32_t height;
        } resize;

        // JALIUM_EVENT_MOVE
        struct {
            int32_t x;
            int32_t y;
        } move;

        // JALIUM_EVENT_DPI_CHANGED
        struct {
            float   dpiX;
            float   dpiY;
            int32_t suggestedX;
            int32_t suggestedY;
            int32_t suggestedWidth;
            int32_t suggestedHeight;
        } dpiChanged;

        // JALIUM_EVENT_STATE_CHANGED
        struct {
            int32_t newState;   ///< JaliumWindowState value
        } stateChanged;

        // JALIUM_EVENT_MOUSE_MOVE / MOUSE_DOWN / MOUSE_UP
        struct {
            float   x;
            float   y;
            int32_t button;     ///< JaliumMouseButton value (for DOWN/UP)
            int32_t modifiers;  ///< JaliumModifiers flags
            int32_t clickCount; ///< 1 = single, 2 = double, etc.
        } mouse;

        // JALIUM_EVENT_MOUSE_WHEEL
        struct {
            float   x;
            float   y;
            float   deltaX;     ///< Horizontal scroll delta
            float   deltaY;     ///< Vertical scroll delta
            int32_t modifiers;
        } wheel;

        // JALIUM_EVENT_KEY_DOWN / KEY_UP
        struct {
            int32_t keyCode;    ///< Platform-neutral virtual key code (Jalium VK)
            int32_t scanCode;   ///< Hardware scan code
            int32_t modifiers;  ///< JaliumModifiers flags
            int32_t isRepeat;   ///< Non-zero if this is a repeat event
        } key;

        // JALIUM_EVENT_CHAR_INPUT
        struct {
            uint32_t codepoint; ///< Unicode code point
        } character;

        // JALIUM_EVENT_COMPOSITION_START / UPDATE / END. The UTF-8 pointer is
        // valid only for the duration of the synchronous event callback.
        struct {
            const char* utf8Text;
            int32_t cursor;
        } composition;

        // JALIUM_EVENT_DELETE_SURROUNDING_TEXT. Lengths are UTF-8 bytes
        // immediately before/after the current selection/cursor.
        struct {
            int32_t beforeUtf8Bytes;
            int32_t afterUtf8Bytes;
        } deleteSurrounding;

        // JALIUM_EVENT_POINTER_DOWN / UP / MOVE / CANCEL
        struct {
            uint32_t pointerId;
            float    x;
            float    y;
            float    pressure;  ///< 0.0 - 1.0
            float    tiltX;     ///< Degrees, -90 to 90
            float    tiltY;
            float    twist;     ///< Degrees, 0 to 360
            int32_t  pointerType;  ///< JaliumPointerType
            int32_t  modifiers;
            uint32_t flags;     ///< JaliumPointerFlags
            int32_t  toolType;  ///< JaliumPointerToolType
            uint32_t buttons;   ///< JaliumPointerButtons
        } pointer;

        // JALIUM_EVENT_SAFE_AREA_CHANGED
        struct {
            float top;
            float bottom;
            float left;
            float right;
        } safeArea;

        // JALIUM_EVENT_KEYBOARD_CHANGED
        struct {
            int32_t visible;    ///< Non-zero if keyboard is visible
            int32_t heightPx;   ///< Keyboard height in physical pixels
        } keyboard;

        // JALIUM_EVENT_ORIENTATION_CHANGED
        struct {
            int32_t orientation; ///< 0=portrait, 1=landscape, 2=portrait-reverse, 3=landscape-reverse
        } orientationChanged;

        // JALIUM_EVENT_DRAG_ENTER / OVER / LEAVE / DROP / FINISHED.
        // mimeTypes is a newline-delimited list. data/dataMimeType are present
        // for DROP and point at the selected representation. sessionId remains
        // stable from ENTER through DROP/LEAVE and lets callers reject stale
        // effect responses.
        struct {
            float x;
            float y;
            uint32_t keyStates;
            uint32_t allowedEffects;
            uint64_t sessionId;
            const char* mimeTypes;
            const char* dataMimeType;
            const uint8_t* data;
            uint32_t dataSize;
        } drag;
    };
} JaliumPlatformEvent;

/// One representation supplied by a native drag source. The native backend
/// copies both MIME names and bytes before this call returns or enters a nested
/// protocol loop; callers retain ownership of every pointer.
typedef struct JaliumDragDataItem {
    const char* mimeType;
    const uint8_t* data;
    uint32_t dataSize;
} JaliumDragDataItem;

/// Optional native drag image. Pixels are straight-alpha BGRA32 in row-major
/// order. The platform backend copies/consumes them synchronously while the
/// drag loop is active; ownership remains with the caller.
typedef struct JaliumDragImage {
    const uint8_t* bgraPixels;
    uint32_t width;
    uint32_t height;
    uint32_t stride;
    int32_t hotspotX;
    int32_t hotspotY;
} JaliumDragImage;

/// Event callback type.
typedef void (*JaliumEventCallback)(const JaliumPlatformEvent* event, void* userData);

/// Dispatcher callback type (invoked when dispatcher is woken).
typedef void (*JaliumDispatcherCallback)(void* userData);

/// Timer callback type.
typedef void (*JaliumTimerCallback)(void* userData);

// ============================================================================
// Platform Initialization
// ============================================================================

/// Initializes the platform subsystem. Must be called once before any other
/// platform API calls. Safe to call multiple times (ref-counted).
/// On Linux, the first successful caller owns the display and event loop until
/// the matching final shutdown.
JALIUM_PLATFORM_API JaliumResult jalium_platform_init(void);

/// Shuts down the platform subsystem. Must be called once for each
/// successful jalium_platform_init() call.
/// On Linux, the final matching shutdown must run on the platform initialization
/// thread because it releases the owned X11/Wayland display resources.
JALIUM_PLATFORM_API void jalium_platform_shutdown(void);

/// Returns the current host platform identifier.
JALIUM_PLATFORM_API JaliumPlatform jalium_platform_get_current(void);

// ============================================================================
// Window Management
// ============================================================================

/// Creates a new platform window.
/// @param params Window creation parameters.
/// @return The created window, or NULL on failure.
JALIUM_PLATFORM_API JaliumPlatformWindow* jalium_window_create(
    const JaliumWindowParams* params);

/// Destroys a platform window.
JALIUM_PLATFORM_API void jalium_window_destroy(JaliumPlatformWindow* window);

/// Shows a window.
JALIUM_PLATFORM_API void jalium_window_show(JaliumPlatformWindow* window);

/// Hides a window.
JALIUM_PLATFORM_API void jalium_window_hide(JaliumPlatformWindow* window);

/// Sets the window title.
JALIUM_PLATFORM_API void jalium_window_set_title(
    JaliumPlatformWindow* window,
    const JaliumUtf16Char* title);

/// Resizes the client area of the window.
JALIUM_PLATFORM_API void jalium_window_resize(
    JaliumPlatformWindow* window,
    int32_t width,
    int32_t height);

/// Moves the window to the specified position.
JALIUM_PLATFORM_API void jalium_window_move(
    JaliumPlatformWindow* window,
    int32_t x,
    int32_t y);

/// Sets the window state (normal / minimized / maximized / fullscreen).
JALIUM_PLATFORM_API void jalium_window_set_state(
    JaliumPlatformWindow* window,
    JaliumWindowState state);

/// Gets the window state.
JALIUM_PLATFORM_API JaliumWindowState jalium_window_get_state(
    JaliumPlatformWindow* window);

/// Gets the native window handle (HWND on Windows, X11 Window on Linux,
/// ANativeWindow* on Android).
JALIUM_PLATFORM_API intptr_t jalium_window_get_native_handle(
    JaliumPlatformWindow* window);

/// Gets a platform-neutral surface descriptor suitable for creating
/// render targets. The caller does not own the returned data.
JALIUM_PLATFORM_API JaliumSurfaceDescriptor jalium_window_get_surface(
    JaliumPlatformWindow* window);

/// Copies the desktop-portal parent identifier for this window as UTF-8.
/// The result is "x11:<hex-XID>" on X11 or, when xdg-foreign-v2 is available,
/// "wayland:<exported-token>" on Wayland. The Wayland export is revoked
/// automatically when the surface is destroyed.
///
/// Returns the required byte count including the trailing NUL. Passing a null
/// buffer or a buffer smaller than the returned size performs no copy.
JALIUM_PLATFORM_API uint32_t jalium_window_get_portal_parent_handle(
    JaliumPlatformWindow* window,
    char* utf8Buffer,
    uint32_t bufferSize);

/// Native-handle form of jalium_window_get_portal_parent_handle. This is the
/// ABI used by managed Window.Handle, whose value is an X11 Window or a
/// wl_surface rather than the private JaliumPlatformWindow wrapper.
JALIUM_PLATFORM_API uint32_t
jalium_window_get_portal_parent_handle_for_native_handle(
    intptr_t nativeHandle,
    char* utf8Buffer,
    uint32_t bufferSize);

/// Returns whether a Wayland surface owned by this platform backend may have
/// its first buffer committed. xdg-shell requires the initial configure to be
/// acknowledged before wl_surface.attach or vkQueuePresentKHR maps a buffer.
///
/// Unknown/external surfaces return 1 because their xdg role and configure
/// handshake are owned by the embedder. A null surface returns 0.
JALIUM_PLATFORM_API int32_t jalium_wayland_surface_is_ready(
    intptr_t waylandSurface);

/// Sets the event callback for a window. Only one callback per window.
/// Pass NULL to remove the callback.
JALIUM_PLATFORM_API void jalium_window_set_event_callback(
    JaliumPlatformWindow* window,
    JaliumEventCallback callback,
    void* userData);

/// Requests the window to be repainted (invalidates the client area).
JALIUM_PLATFORM_API void jalium_window_invalidate(JaliumPlatformWindow* window);

/// Sets the cursor shape for a window.
JALIUM_PLATFORM_API void jalium_window_set_cursor(
    JaliumPlatformWindow* window,
    JaliumCursorShape cursor);

/// Gets the window's client area size.
JALIUM_PLATFORM_API void jalium_window_get_client_size(
    JaliumPlatformWindow* window,
    int32_t* width,
    int32_t* height);

/// Gets the window's position (outer frame).
JALIUM_PLATFORM_API void jalium_window_get_position(
    JaliumPlatformWindow* window,
    int32_t* x,
    int32_t* y);

// ============================================================================
// Event Loop
// ============================================================================

/// Runs the platform event loop. Blocks until jalium_platform_quit() is called.
/// Dispatches events to window callbacks.
/// On Linux, must be called from the thread that initialized the platform.
/// @return The exit code passed to jalium_platform_quit(), or
/// JALIUM_ERROR_INVALID_STATE when called from a foreign Linux thread.
JALIUM_PLATFORM_API int32_t jalium_platform_run_message_loop(void);

/// Runs one iteration of the event loop without blocking. Returns the number
/// of events processed (0 if none were pending).
/// On Linux, only the platform initialization thread processes events; calls
/// from other threads return 0.
JALIUM_PLATFORM_API int32_t jalium_platform_poll_events(void);

/// Signals the event loop to exit with the given exit code.
JALIUM_PLATFORM_API void jalium_platform_quit(int32_t exitCode);

// ============================================================================
// Dispatcher (Cross-thread Wake)
// ============================================================================

/// Creates a new dispatcher for the calling thread.
/// Used to wake the event loop from another thread.
/// On Linux, the caller must be the platform initialization thread because the
/// X11/Wayland event loop has a single owner.
/// @param outDispatcher Receives the created dispatcher handle.
/// @return JALIUM_OK on success, or JALIUM_ERROR_INVALID_STATE when the Linux
/// caller does not own the platform event loop.
JALIUM_PLATFORM_API JaliumResult jalium_dispatcher_create(
    JaliumDispatcher** outDispatcher);

/// Destroys a dispatcher.
JALIUM_PLATFORM_API void jalium_dispatcher_destroy(JaliumDispatcher* dispatcher);

/// Wakes the dispatcher's associated thread from any thread.
/// Thread-safe. May be called from any thread.
JALIUM_PLATFORM_API void jalium_dispatcher_wake(JaliumDispatcher* dispatcher);

/// Sets the callback to invoke when the dispatcher is woken.
JALIUM_PLATFORM_API void jalium_dispatcher_set_callback(
    JaliumDispatcher* dispatcher,
    JaliumDispatcherCallback callback,
    void* userData);

// ============================================================================
// High-Resolution Timer
// ============================================================================

/// Creates a high-resolution timer.
/// @param outTimer Receives the created timer handle.
/// @return JALIUM_OK on success.
JALIUM_PLATFORM_API JaliumResult jalium_timer_create(JaliumTimer** outTimer);

/// Destroys a timer.
JALIUM_PLATFORM_API void jalium_timer_destroy(JaliumTimer* timer);

/// Arms the timer to fire after the specified interval.
/// @param timer The timer.
/// @param intervalMicroseconds Interval in microseconds.
JALIUM_PLATFORM_API void jalium_timer_arm(
    JaliumTimer* timer,
    int64_t intervalMicroseconds);

/// Arms the timer to fire repeatedly at the specified interval.
JALIUM_PLATFORM_API void jalium_timer_arm_repeating(
    JaliumTimer* timer,
    int64_t intervalMicroseconds);

/// Disarms (stops) the timer.
JALIUM_PLATFORM_API void jalium_timer_disarm(JaliumTimer* timer);

/// Sets the callback to invoke when the timer fires.
JALIUM_PLATFORM_API void jalium_timer_set_callback(
    JaliumTimer* timer,
    JaliumTimerCallback callback,
    void* userData);

/// Blocks the calling thread until the timer fires or the timeout elapses.
/// @param timer The timer.
/// @param timeoutMs Maximum time to wait in milliseconds (0 = infinite).
/// @return 1 if timer fired, 0 if timeout.
JALIUM_PLATFORM_API int32_t jalium_timer_wait(
    JaliumTimer* timer,
    uint32_t timeoutMs);

// ============================================================================
// DPI and Display
// ============================================================================

/// Gets the system-wide DPI scale factor (1.0 = 96 DPI).
JALIUM_PLATFORM_API float jalium_platform_get_system_dpi_scale(void);

/// Gets the DPI scale factor for a specific window.
JALIUM_PLATFORM_API float jalium_window_get_dpi_scale(
    JaliumPlatformWindow* window);

/// Gets the monitor refresh rate (in Hz) for the monitor containing the window.
JALIUM_PLATFORM_API int32_t jalium_window_get_monitor_refresh_rate(
    JaliumPlatformWindow* window);

/// Describes one attached monitor. Coordinates are physical pixels in the
/// global desktop space (X11 root / Win32 virtual screen). Wayland has no
/// global positions; outputs report their advertised logical geometry there.
typedef struct JaliumMonitorInfo {
    int32_t x;
    int32_t y;
    int32_t width;
    int32_t height;
    int32_t workX;
    int32_t workY;
    int32_t workWidth;
    int32_t workHeight;
    float   scale;
    int32_t refreshRate;
    int32_t isPrimary;
} JaliumMonitorInfo;

/// Returns the number of attached monitors (0 when enumeration is unavailable).
JALIUM_PLATFORM_API int32_t jalium_platform_get_monitor_count(void);

/// Fills info for the monitor at index (0-based). Returns JALIUM_OK, or
/// JALIUM_ERROR_INVALID_ARGUMENT for an out-of-range index / null info.
JALIUM_PLATFORM_API int32_t jalium_platform_get_monitor_info(
    int32_t index,
    JaliumMonitorInfo* info);

// ============================================================================
// Window management extensions
// ============================================================================

/// Sets the window's minimum and maximum client size in physical pixels.
/// Pass 0 for any bound to leave it unconstrained.
JALIUM_PLATFORM_API int32_t jalium_window_set_min_max_size(
    JaliumPlatformWindow* window,
    int32_t minWidth,
    int32_t minHeight,
    int32_t maxWidth,
    int32_t maxHeight);

/// Starts an interactive, window-system-driven move of the window. Must be
/// called from the handler of a mouse button press (the last press provides
/// the pointer serial/coordinates handed to the window manager).
JALIUM_PLATFORM_API int32_t jalium_window_begin_move_drag(
    JaliumPlatformWindow* window);

/// Edges for jalium_window_begin_resize_drag, matching xdg_toplevel and
/// _NET_WM_MOVERESIZE directions.
typedef enum JaliumResizeEdge {
    JALIUM_RESIZE_EDGE_TOP          = 1,
    JALIUM_RESIZE_EDGE_BOTTOM      = 2,
    JALIUM_RESIZE_EDGE_LEFT        = 4,
    JALIUM_RESIZE_EDGE_TOP_LEFT    = 5,
    JALIUM_RESIZE_EDGE_BOTTOM_LEFT = 6,
    JALIUM_RESIZE_EDGE_RIGHT       = 8,
    JALIUM_RESIZE_EDGE_TOP_RIGHT   = 9,
    JALIUM_RESIZE_EDGE_BOTTOM_RIGHT = 10,
} JaliumResizeEdge;

/// Starts an interactive, window-system-driven resize from the given edge.
JALIUM_PLATFORM_API int32_t jalium_window_begin_resize_drag(
    JaliumPlatformWindow* window,
    int32_t edge);

/// Sets the window icon from 32-bit BGRA pixels (row-major, premultiplication
/// not required). Pass NULL/0 to clear. Wayland uses the optional staging
/// xdg-toplevel-icon-v1 protocol when the compositor advertises it.
JALIUM_PLATFORM_API int32_t jalium_window_set_icon(
    JaliumPlatformWindow* window,
    const uint32_t* bgraPixels,
    int32_t width,
    int32_t height);

/// Toggles always-on-top for the window where the window system supports it.
JALIUM_PLATFORM_API int32_t jalium_window_set_topmost(
    JaliumPlatformWindow* window,
    int32_t topmost);

/// Enables or disables delivery of user input to the window.
JALIUM_PLATFORM_API int32_t jalium_window_set_enabled(
    JaliumPlatformWindow* window,
    int32_t enabled);

/// Sets whole-window opacity in the inclusive range [0, 1].
JALIUM_PLATFORM_API int32_t jalium_window_set_opacity(
    JaliumPlatformWindow* window,
    double opacity);

/// Controls whether the window is advertised in the desktop taskbar/switcher.
JALIUM_PLATFORM_API int32_t jalium_window_set_show_in_taskbar(
    JaliumPlatformWindow* window,
    int32_t showInTaskbar);

/// Toggles interactive resizing without discarding explicit min/max constraints.
JALIUM_PLATFORM_API int32_t jalium_window_set_resizable(
    JaliumPlatformWindow* window,
    int32_t resizable);

/// Toggles server-side decorations where the window system exposes that control.
JALIUM_PLATFORM_API int32_t jalium_window_set_decorated(
    JaliumPlatformWindow* window,
    int32_t decorated);

/// Updates the transient owner relationship. ownerNativeHandle == 0 clears it.
JALIUM_PLATFORM_API int32_t jalium_window_set_owner(
    JaliumPlatformWindow* window,
    intptr_t ownerNativeHandle);

/// Requests foreground activation where supported by the window system.
JALIUM_PLATFORM_API int32_t jalium_window_activate(
    JaliumPlatformWindow* window);

/// Requests the compositor/window-manager system menu at a client-local
/// position expressed in physical pixels. The call must be made while handling
/// a user input event on window systems that require an input serial.
JALIUM_PLATFORM_API int32_t jalium_window_show_system_menu(
    JaliumPlatformWindow* window,
    int32_t x,
    int32_t y);

/// Updates the native IME context. utf8Text may be NULL when surrounding text
/// is intentionally unavailable (for example password input). Cursor/anchor
/// offsets are UTF-8 byte offsets and the caret rectangle is client-local in
/// physical pixels.
JALIUM_PLATFORM_API int32_t jalium_window_update_ime_context(
    JaliumPlatformWindow* window,
    int32_t enabled,
    const char* utf8Text,
    int32_t cursorByteOffset,
    int32_t anchorByteOffset,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height);

// ============================================================================
// Input State Polling
// ============================================================================

/// Gets the current state of a virtual key.
/// @param jaliumVirtualKey Platform-neutral virtual key code.
/// @return Bitmask: bit 0 = currently pressed, bit 1 = toggled (e.g. Caps Lock).
JALIUM_PLATFORM_API int16_t jalium_input_get_key_state(int32_t jaliumVirtualKey);

/// Queries the currently attached touch hardware.
///
/// touchPresent is set to 1 when the active window system advertises at least
/// one touch device, otherwise 0. maxContacts is the maximum simultaneous
/// contact count when the protocol exposes it. A value of 0 with
/// touchPresent=1 means that touch is present but the maximum is unknown (for
/// example, core Wayland wl_touch does not publish a contact limit).
JALIUM_PLATFORM_API JaliumResult jalium_input_get_touch_capabilities(
    int32_t* touchPresent,
    int32_t* maxContacts);

/// Updates platform-wide multi-click thresholds from the active desktop
/// settings provider. Distance is expressed in platform input pixels.
JALIUM_PLATFORM_API JaliumResult jalium_platform_set_double_click_settings(
    uint32_t milliseconds,
    float distance);

/// Gets the current mouse cursor position in global screen coordinates.
///
/// X11 and Win32 expose a compositor-independent global coordinate space and
/// return JALIUM_OK. Wayland intentionally does not expose global pointer
/// coordinates; its pointer events are surface-local, so the Wayland backend
/// zeroes the outputs and returns JALIUM_ERROR_NOT_SUPPORTED instead of
/// mislabelling the last surface-local event position as a screen position.
JALIUM_PLATFORM_API JaliumResult jalium_input_get_cursor_pos(float* x, float* y);

// ============================================================================
// Drag and Drop
// ============================================================================

/// Result requested by the managed QueryContinueDrag callback.
typedef enum JaliumDragContinueAction {
    JALIUM_DRAG_CONTINUE = 0,
    JALIUM_DRAG_DROP     = 1,
    JALIUM_DRAG_CANCEL   = 2,
} JaliumDragContinueAction;

/// Notifies the source of the effect currently selected by the native target.
typedef void (*JaliumDragFeedbackCallback)(
    uint32_t effect,
    void* userData);

/// Queries whether the nested native drag loop should continue, drop, or
/// cancel. keyStates uses DragDropKeyStates-compatible bits 0x01..0x20.
typedef JaliumDragContinueAction (*JaliumDragQueryContinueCallback)(
    uint32_t keyStates,
    int32_t escapePressed,
    void* userData);

/// Sets the target-selected effect for the drag event currently being
/// dispatched. A stale sessionId is ignored. Call synchronously from the
/// window event callback handling ENTER/OVER/DROP.
JALIUM_PLATFORM_API void jalium_drag_set_effect(
    JaliumPlatformWindow* window,
    uint64_t sessionId,
    uint32_t effect);

/// Starts an OS drag from window. Text and URI-list sources should offer the
/// standard UTF-8 MIME types (text/plain;charset=utf-8 and text/uri-list).
/// Blocks in the platform drag loop until the drag is dropped or cancelled.
/// @param performedEffect Receives JALIUM_DRAG_EFFECT_NONE on cancellation.
JALIUM_PLATFORM_API JaliumResult jalium_drag_begin(
    JaliumPlatformWindow* window,
    const JaliumDragDataItem* items,
    uint32_t itemCount,
    uint32_t allowedEffects,
    uint32_t* performedEffect);

/// Extended drag source entry point with routed GiveFeedback and
/// QueryContinueDrag hooks. Callbacks execute synchronously on the thread that
/// called this function. Passing NULL callbacks preserves jalium_drag_begin's
/// default release-to-drop and Escape-to-cancel behavior.
JALIUM_PLATFORM_API JaliumResult jalium_drag_begin_ex(
    JaliumPlatformWindow* window,
    const JaliumDragDataItem* items,
    uint32_t itemCount,
    uint32_t allowedEffects,
    JaliumDragFeedbackCallback feedbackCallback,
    JaliumDragQueryContinueCallback queryContinueCallback,
    void* callbackUserData,
    uint32_t* performedEffect);

/// Extended drag source entry point with an optional platform-native drag
/// image. X11 presents it as an ARGB cursor and Wayland commits it to the
/// mandatory data-device icon surface. Passing NULL preserves the default
/// cursor/icon behavior.
JALIUM_PLATFORM_API JaliumResult jalium_drag_begin_with_image(
    JaliumPlatformWindow* window,
    const JaliumDragDataItem* items,
    uint32_t itemCount,
    uint32_t allowedEffects,
    JaliumDragFeedbackCallback feedbackCallback,
    JaliumDragQueryContinueCallback queryContinueCallback,
    void* callbackUserData,
    const JaliumDragImage* dragImage,
    uint32_t* performedEffect);

#ifdef JALIUM_PLATFORM_TEST_HOOKS
/// Test-only protocol callback injection. These entry points are compiled only
/// in explicit native-test builds and exercise the same listeners used by a
/// real Wayland compositor/device.
JALIUM_PLATFORM_API int32_t jalium_test_wayland_inject_touch(
    JaliumPlatformWindow* window,
    JaliumEventType type,
    int32_t touchId,
    float x,
    float y);
JALIUM_PLATFORM_API int32_t jalium_test_wayland_inject_tablet(
    JaliumPlatformWindow* window,
    JaliumEventType type,
    int32_t toolId,
    float x,
    float y,
    float pressure,
    float tiltX,
    float tiltY,
    float twist);
/// Extended tablet-v2 injection used to verify hover/proximity, physical tool
/// type, button state, distance, and slider aggregation through frame events.
JALIUM_PLATFORM_API int32_t jalium_test_wayland_inject_tablet_state(
    JaliumPlatformWindow* window,
    JaliumEventType type,
    int32_t toolId,
    float x,
    float y,
    float pressure,
    float tiltX,
    float tiltY,
    float twist,
    int32_t toolType,
    uint32_t buttons,
    float distance,
    float slider);
JALIUM_PLATFORM_API int32_t jalium_test_wayland_inject_decoration_configure(
    JaliumPlatformWindow* window,
    uint32_t mode);
JALIUM_PLATFORM_API uint32_t jalium_test_wayland_get_decoration_mode(
    JaliumPlatformWindow* window);
/// Replaces or removes one synthetic output in a Wayland window's entered
/// output set. This drives the same max-scale recomputation as wl_surface
/// enter/leave and wl_output.scale callbacks.
JALIUM_PLATFORM_API int32_t jalium_test_wayland_set_output(
    JaliumPlatformWindow* window,
    uint32_t outputId,
    int32_t scale,
    int32_t entered);
/// Clears the synthetic/real entered-output snapshot for deterministic scale
/// transition tests and restores the scale-1 fallback.
JALIUM_PLATFORM_API int32_t jalium_test_wayland_reset_outputs(
    JaliumPlatformWindow* window);
/// Returns the deterministic active output id used for Wayland refresh-rate
/// selection (highest scale, then lowest registry id), or zero when none.
JALIUM_PLATFORM_API uint32_t jalium_test_wayland_get_selected_output(
    JaliumPlatformWindow* window);
/// Forces the normal XRandR display-refresh path without changing server
/// topology. Returns NOT_SUPPORTED when XRandR is unavailable.
JALIUM_PLATFORM_API int32_t jalium_test_x11_notify_display_change(void);
/// Applies the X11 per-monitor physical-size validation and DPI fallback rule.
/// This keeps edge cases deterministic without requiring synthetic RandR
/// hardware in the test server.
JALIUM_PLATFORM_API float jalium_test_x11_compute_monitor_scale(
    int32_t width,
    int32_t height,
    int32_t widthMm,
    int32_t heightMm,
    float fallbackScale);
/// Overrides native multi-click thresholds and feeds one timestamped press
/// through the production click tracker. These retain X11/Wayland uint32
/// protocol timestamp wrap semantics.
JALIUM_PLATFORM_API int32_t jalium_test_override_double_click_settings(
    uint32_t milliseconds,
    float distance);
JALIUM_PLATFORM_API int32_t jalium_test_register_click(
    uint32_t time,
    float x,
    float y,
    int32_t button,
    int32_t reset);
/// Overrides touch capability discovery in native tests. Pass touchPresent=-1
/// to restore live platform discovery.
JALIUM_PLATFORM_API void jalium_test_override_touch_capabilities(
    int32_t touchPresent,
    int32_t maxContacts);
/// Converts one absolute XI2 scroll-valuator transition to Jalium wheel
/// units. Returns 1 when a finite, plausible delta was produced, 0 when the
/// transition must be treated as a new baseline, and -1 for null outputs.
JALIUM_PLATFORM_API int32_t jalium_test_xinput_smooth_scroll_delta(
    double previousValue,
    double currentValue,
    double increment,
    int32_t vertical,
    float* deltaX,
    float* deltaY);
/// Evaluates the XI2 pen-state flag mapping without requiring physical tablet
/// hardware on the Xvfb test server.
JALIUM_PLATFORM_API uint32_t jalium_test_xinput_pen_flags(
    int32_t toolType,
    int32_t inverted,
    int32_t inRange,
    int32_t inContact,
    uint32_t buttons);
/// Reads the per-window Wayland IME snapshot stored by
/// jalium_window_update_ime_context. The UTF-8 buffer includes a trailing NUL.
JALIUM_PLATFORM_API int32_t jalium_test_wayland_get_ime_context(
    JaliumPlatformWindow* window,
    int32_t* enabled,
    char* utf8Buffer,
    uint32_t bufferCapacity,
    int32_t* cursorByteOffset,
    int32_t* anchorByteOffset,
    int32_t* x,
    int32_t* y,
    int32_t* width,
    int32_t* height);
/// Injects the same adjacent UTF-8 deletion event delivered by text-input-v3.
JALIUM_PLATFORM_API int32_t jalium_test_wayland_inject_delete_surrounding(
    JaliumPlatformWindow* window,
    uint32_t beforeUtf8Bytes,
    uint32_t afterUtf8Bytes);
/// Reads the most recent system-menu request accepted by the native backend.
JALIUM_PLATFORM_API int32_t jalium_test_wayland_get_last_system_menu(
    JaliumPlatformWindow* window,
    int32_t* x,
    int32_t* y,
    uint32_t* inputSerial);
/// Returns non-zero when the compositor advertised xdg-activation-v1.
JALIUM_PLATFORM_API int32_t jalium_test_wayland_has_activation(void);
#endif

// ============================================================================
// Clipboard
// ============================================================================

/// One MIME representation published in a clipboard transaction. The native
/// backend copies both the MIME name and bytes before the call returns.
typedef JaliumDragDataItem JaliumClipboardDataItem;

/// Returns the MIME types currently offered by the clipboard as a newline-
/// separated UTF-8 string. The result must be freed with
/// jalium_platform_free(). An empty clipboard returns an allocated empty string.
JALIUM_PLATFORM_API JaliumResult jalium_clipboard_get_formats(char** outMimeTypes);

/// Reads one MIME representation. The returned byte buffer (including a
/// zero-length allocation when the format is present but empty) must be freed
/// with jalium_platform_free(). A missing format is reported by a NULL buffer
/// and zero length while still returning JALIUM_OK.
JALIUM_PLATFORM_API JaliumResult jalium_clipboard_get_data(
    const char* mimeType,
    uint8_t** outData,
    uint32_t* outDataSize);

/// Atomically publishes all supplied MIME representations. Passing zero items
/// clears the clipboard. Duplicate MIME names are resolved in favor of the
/// last item.
JALIUM_PLATFORM_API JaliumResult jalium_clipboard_set_data(
    const JaliumClipboardDataItem* items,
    uint32_t itemCount);

/// Clears all clipboard representations.
JALIUM_PLATFORM_API JaliumResult jalium_clipboard_clear(void);

/// Gets the clipboard text content. The returned string is UTF-16 and must be
/// freed by the caller using jalium_platform_free().
/// @param outText Receives a pointer to the text, or NULL if empty.
/// @return JALIUM_OK on success.
JALIUM_PLATFORM_API JaliumResult jalium_clipboard_get_text(JaliumUtf16Char** outText);

/// Sets the clipboard text content.
/// @param text UTF-16 text to place on the clipboard.
/// @return JALIUM_OK on success.
JALIUM_PLATFORM_API JaliumResult jalium_clipboard_set_text(const JaliumUtf16Char* text);

/// Frees memory allocated by platform API calls (e.g. jalium_clipboard_get_text).
JALIUM_PLATFORM_API void jalium_platform_free(void* ptr);

#ifdef __cplusplus
}
#endif
