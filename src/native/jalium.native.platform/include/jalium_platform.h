#pragma once

#include "jalium_types.h"

// Platform-specific export macros
#ifdef _WIN32
    #if defined(JALIUM_PLATFORM_STATIC)
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

    // Application
    JALIUM_EVENT_QUIT             = 99,
} JaliumEventType;

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
    const wchar_t*  title;
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
    };
} JaliumPlatformEvent;

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
JALIUM_PLATFORM_API JaliumResult jalium_platform_init(void);

/// Shuts down the platform subsystem. Must be called once for each
/// successful jalium_platform_init() call.
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
    const wchar_t* title);

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
/// @return The exit code passed to jalium_platform_quit().
JALIUM_PLATFORM_API int32_t jalium_platform_run_message_loop(void);

/// Runs one iteration of the event loop without blocking. Returns the number
/// of events processed (0 if none were pending).
JALIUM_PLATFORM_API int32_t jalium_platform_poll_events(void);

/// Signals the event loop to exit with the given exit code.
JALIUM_PLATFORM_API void jalium_platform_quit(int32_t exitCode);

// ============================================================================
// Dispatcher (Cross-thread Wake)
// ============================================================================

/// Creates a new dispatcher for the calling thread.
/// Used to wake the event loop from another thread.
/// @param outDispatcher Receives the created dispatcher handle.
/// @return JALIUM_OK on success.
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

// ============================================================================
// Input State Polling
// ============================================================================

/// Gets the current state of a virtual key.
/// @param jaliumVirtualKey Platform-neutral virtual key code.
/// @return Bitmask: bit 0 = currently pressed, bit 1 = toggled (e.g. Caps Lock).
JALIUM_PLATFORM_API int16_t jalium_input_get_key_state(int32_t jaliumVirtualKey);

/// Gets the current mouse cursor position in screen coordinates.
JALIUM_PLATFORM_API void jalium_input_get_cursor_pos(float* x, float* y);

// ============================================================================
// Clipboard
// ============================================================================

/// Gets the clipboard text content. The returned string is UTF-16 and must be
/// freed by the caller using jalium_platform_free().
/// @param outText Receives a pointer to the text, or NULL if empty.
/// @return JALIUM_OK on success.
JALIUM_PLATFORM_API JaliumResult jalium_clipboard_get_text(wchar_t** outText);

/// Sets the clipboard text content.
/// @param text UTF-16 text to place on the clipboard.
/// @return JALIUM_OK on success.
JALIUM_PLATFORM_API JaliumResult jalium_clipboard_set_text(const wchar_t* text);

/// Frees memory allocated by platform API calls (e.g. jalium_clipboard_get_text).
JALIUM_PLATFORM_API void jalium_platform_free(void* ptr);

#ifdef __cplusplus
}
#endif
