#if defined(__ANDROID__)

#include "jalium_platform.h"

#include <jni.h>
#include <android/native_activity.h>
#include <android/native_window.h>
#include <android/input.h>
#include <android/looper.h>
#include <android/choreographer.h>
#include <android/log.h>
#include <android/configuration.h>

#include <sys/eventfd.h>
#include <unistd.h>
#include <string.h>
#include <stdlib.h>
#include <wchar.h>
#include <time.h>
#include <poll.h>

#include <atomic>
#include <mutex>
#include <vector>

#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, "JaliumPlatform", __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, "JaliumPlatform", __VA_ARGS__)

// ============================================================================
// Global State
// ============================================================================

static ALooper*         g_looper = nullptr;
static std::atomic<bool> g_quitRequested{false};
static std::atomic<int32_t> g_exitCode{0};
static float            g_density = 1.0f;  // DisplayMetrics density
static int32_t          g_refreshRate = 60;

// JNI state for clipboard and other system services
static JavaVM*          g_javaVM = nullptr;
static jobject          g_activityObj = nullptr;  // Global ref to Activity

// Looper callback IDs
enum {
    LOOPER_ID_DISPATCHER = 1,
    LOOPER_ID_TIMER      = 2,
};

// ============================================================================
// Window Structure (maps to ANativeWindow)
// ============================================================================

struct JaliumPlatformWindow {
    ANativeWindow*      nativeWindow = nullptr;
    JaliumEventCallback callback = nullptr;
    void*               userData = nullptr;
    int32_t             width = 0;
    int32_t             height = 0;
    float               dpiScale = 1.0f;
    uint32_t            style = 0;
    bool                destroyed = false;

    void DispatchEvent(const JaliumPlatformEvent& evt)
    {
        if (callback && !destroyed)
            callback(&evt, userData);
    }
};

// Single window instance for Android (typically only one visible Activity)
static JaliumPlatformWindow* g_mainWindow = nullptr;

// ANativeWindow arrives from Java (SurfaceChanged) before jalium_window_create() is called.
// Store it here so jalium_window_create() can pick it up.
static ANativeWindow* g_pendingNativeWindow = nullptr;

// Tracked globally so the message loop can re-register it with the correct looper.
static JaliumDispatcher* g_dispatcher = nullptr;

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
    int                 timerFd = -1;  // Not used on Android; use AChoreographer
    JaliumTimerCallback callback = nullptr;
    void*               userData = nullptr;
    bool                repeating = false;
    int64_t             intervalUs = 0;
};

// ============================================================================
// Platform Init / Shutdown
// ============================================================================

JaliumResult jalium_platform_init_impl()
{
    LOGI("jalium_platform_init_impl called");
    g_looper = ALooper_forThread();
    if (!g_looper)
    {
        g_looper = ALooper_prepare(ALOOPER_PREPARE_ALLOW_NON_CALLBACKS);
    }

    if (!g_looper)
    {
        LOGE("jalium_platform_init_impl: failed to get/prepare ALooper");
        return JALIUM_ERROR_INITIALIZATION_FAILED;
    }

    LOGI("jalium_platform_init_impl: looper=%p", g_looper);
    return JALIUM_OK;
}

void jalium_platform_shutdown_impl()
{
    g_looper = nullptr;
}

JaliumPlatform jalium_platform_get_current_impl()
{
    return JALIUM_PLATFORM_ANDROID;
}

// ============================================================================
// Window Management
// ============================================================================

JaliumPlatformWindow* jalium_window_create(const JaliumWindowParams* params)
{
    // On Android, window creation is driven by the Activity lifecycle.
    // This creates a wrapper that will be associated with an ANativeWindow
    // when onNativeWindowCreated is called.
    if (!params) return nullptr;

    LOGI("jalium_window_create: params w=%d h=%d, pendingNativeWindow=%p, density=%.2f",
         params->width, params->height, g_pendingNativeWindow, g_density);

    auto win = new JaliumPlatformWindow();
    win->style = params->style;
    win->width = params->width > 0 ? params->width : 0;
    win->height = params->height > 0 ? params->height : 0;
    win->dpiScale = g_density;

    // Pick up any ANativeWindow that arrived before this window was created
    if (g_pendingNativeWindow)
    {
        win->nativeWindow = g_pendingNativeWindow;
        if (win->width == 0)  win->width  = ANativeWindow_getWidth(g_pendingNativeWindow);
        if (win->height == 0) win->height = ANativeWindow_getHeight(g_pendingNativeWindow);
        g_pendingNativeWindow = nullptr;
    }

    LOGI("jalium_window_create: result win=%p nativeWindow=%p w=%d h=%d",
         win, win->nativeWindow, win->width, win->height);

    g_mainWindow = win;
    return win;
}

void jalium_window_destroy(JaliumPlatformWindow* window)
{
    if (!window) return;
    if (g_mainWindow == window) g_mainWindow = nullptr;
    delete window;
}

void jalium_window_show(JaliumPlatformWindow* window)
{
    // No-op on Android; window visibility is controlled by the Activity
    (void)window;
}

void jalium_window_hide(JaliumPlatformWindow* window)
{
    (void)window;
}

void jalium_window_set_title(JaliumPlatformWindow* window, const wchar_t* title)
{
    // No-op: Android Activity title is set via Java API
    (void)window;
    (void)title;
}

void jalium_window_resize(JaliumPlatformWindow* window, int32_t width, int32_t height)
{
    // No-op: Android window size is controlled by the system
    (void)window;
    (void)width;
    (void)height;
}

void jalium_window_move(JaliumPlatformWindow* window, int32_t x, int32_t y)
{
    // No-op on Android
    (void)window;
    (void)x;
    (void)y;
}

void jalium_window_set_state(JaliumPlatformWindow* window, JaliumWindowState state)
{
    // No-op: controlled by Android system
    (void)window;
    (void)state;
}

JaliumWindowState jalium_window_get_state(JaliumPlatformWindow* window)
{
    (void)window;
    return JALIUM_WINDOW_STATE_FULLSCREEN; // Android windows are always "fullscreen"
}

intptr_t jalium_window_get_native_handle(JaliumPlatformWindow* window)
{
    if (!window) return 0;
    // On Android the "handle" is the window object pointer itself — the ANativeWindow
    // may arrive later via jalium_android_set_native_window(). The handle is only used
    // as a dictionary key in managed code; surface access goes through GetSurface().
    return reinterpret_cast<intptr_t>(window);
}

JaliumSurfaceDescriptor jalium_window_get_surface(JaliumPlatformWindow* window)
{
    JaliumSurfaceDescriptor desc{};
    if (window && window->nativeWindow)
    {
        desc.platform = JALIUM_PLATFORM_ANDROID;
        desc.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        desc.handle0 = reinterpret_cast<intptr_t>(window->nativeWindow);
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
    if (window)
    {
        JaliumPlatformEvent evt{};
        evt.type = JALIUM_EVENT_PAINT;
        evt.window = window;
        window->DispatchEvent(evt);
    }
}

void jalium_window_set_cursor(JaliumPlatformWindow* window, JaliumCursorShape cursor)
{
    // No cursor on Android (touch-based)
    (void)window;
    (void)cursor;
}

void jalium_window_get_client_size(JaliumPlatformWindow* window, int32_t* width, int32_t* height)
{
    if (!window)
    {
        if (width) *width = 0;
        if (height) *height = 0;
        return;
    }

    if (window->nativeWindow)
    {
        if (width) *width = ANativeWindow_getWidth(window->nativeWindow);
        if (height) *height = ANativeWindow_getHeight(window->nativeWindow);
    }
    else
    {
        if (width) *width = window->width;
        if (height) *height = window->height;
    }
}

void jalium_window_get_position(JaliumPlatformWindow* window, int32_t* x, int32_t* y)
{
    // Always (0,0) on Android
    if (x) *x = 0;
    if (y) *y = 0;
    (void)window;
}

// ============================================================================
// JNI Initialization
// ============================================================================

/// Call this from JNI_OnLoad or from the native activity startup to store
/// the JavaVM and Activity references needed for system services (clipboard, etc.).
extern "C" void jalium_android_set_jni_env(JavaVM* vm, jobject activity)
{
    g_javaVM = vm;
    if (g_activityObj)
    {
        JNIEnv* env = nullptr;
        if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) == JNI_OK)
            env->DeleteGlobalRef(g_activityObj);
        g_activityObj = nullptr;
    }
    if (vm && activity)
    {
        JNIEnv* env = nullptr;
        if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) == JNI_OK)
            g_activityObj = env->NewGlobalRef(activity);
    }
}

/// Helper: Attach current thread and get JNIEnv.
static JNIEnv* GetJNIEnv()
{
    if (!g_javaVM) return nullptr;
    JNIEnv* env = nullptr;
    int status = g_javaVM->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6);
    if (status == JNI_EDETACHED)
    {
        if (g_javaVM->AttachCurrentThread(&env, nullptr) != JNI_OK)
            return nullptr;
    }
    return env;
}

/// Public C ABI: returns the JNIEnv for the calling thread, attaching it to
/// the JavaVM if necessary. Returns nullptr if the platform was never bound
/// to a JavaVM via jalium_android_set_jni_env. Used by jalium.native.media.android
/// to call Java APIs (BitmapFactory fallback, MediaCodecList probe).
extern "C" JNIEnv* jalium_android_get_jni_env(void)
{
    return GetJNIEnv();
}

/// Public C ABI: returns the global Activity reference cached by
/// jalium_android_set_jni_env. Returns nullptr when not bound.
extern "C" jobject jalium_android_get_activity(void)
{
    return g_activityObj;
}

/// Public C ABI: returns the cached JavaVM pointer (or nullptr).
extern "C" JavaVM* jalium_android_get_java_vm(void)
{
    return g_javaVM;
}

// ============================================================================
// Android Native Activity Callbacks
// ============================================================================

// Called from NativeActivity.onCreate or when native window is created
extern "C" void jalium_android_set_native_window(ANativeWindow* nativeWindow)
{
    LOGI("jalium_android_set_native_window: win=%p, g_mainWindow=%p", nativeWindow, g_mainWindow);

    if (nativeWindow)
        ANativeWindow_setBuffersGeometry(nativeWindow, 0, 0, WINDOW_FORMAT_RGBA_8888);

    if (g_mainWindow)
    {
        g_mainWindow->nativeWindow = nativeWindow;
        if (nativeWindow)
        {
            g_mainWindow->width = ANativeWindow_getWidth(nativeWindow);
            g_mainWindow->height = ANativeWindow_getHeight(nativeWindow);

            LOGI("jalium_android_set_native_window: dispatching RESIZE w=%d h=%d",
                 g_mainWindow->width, g_mainWindow->height);

            JaliumPlatformEvent evt{};
            evt.type = JALIUM_EVENT_RESIZE;
            evt.window = g_mainWindow;
            evt.resize.width = g_mainWindow->width;
            evt.resize.height = g_mainWindow->height;
            g_mainWindow->DispatchEvent(evt);
        }
    }
    else
    {
        // Window not yet created — store for jalium_window_create() to pick up
        LOGI("jalium_android_set_native_window: storing as pendingNativeWindow");
        g_pendingNativeWindow = nativeWindow;
    }
}

extern "C" void jalium_android_set_density(float density)
{
    float oldDensity = g_density;
    g_density = density;
    if (g_mainWindow) {
        g_mainWindow->dpiScale = density;

        // Dispatch DPI change event so the managed layer can update layout and rendering
        if (density != oldDensity) {
            JaliumPlatformEvent evt{};
            evt.type = JALIUM_EVENT_DPI_CHANGED;
            evt.dpiChanged.dpiX = density * 96.0f;
            evt.dpiChanged.dpiY = density * 96.0f;
            evt.dpiChanged.suggestedX = 0;
            evt.dpiChanged.suggestedY = 0;
            evt.dpiChanged.suggestedWidth = g_mainWindow->width;
            evt.dpiChanged.suggestedHeight = g_mainWindow->height;
            g_mainWindow->DispatchEvent(evt);
        }
    }
}

extern "C" void jalium_android_set_refresh_rate(int32_t refreshRate)
{
    g_refreshRate = refreshRate;
}

extern "C" void jalium_android_on_pause()
{
    if (g_mainWindow)
    {
        JaliumPlatformEvent evt{};
        evt.type = JALIUM_EVENT_APP_PAUSE;
        evt.window = g_mainWindow;
        g_mainWindow->DispatchEvent(evt);
    }
}

extern "C" void jalium_android_on_resume()
{
    if (g_mainWindow)
    {
        JaliumPlatformEvent evt{};
        evt.type = JALIUM_EVENT_APP_RESUME;
        evt.window = g_mainWindow;
        g_mainWindow->DispatchEvent(evt);
    }
}

extern "C" void jalium_android_on_destroy()
{
    if (g_mainWindow)
    {
        JaliumPlatformEvent evt{};
        evt.type = JALIUM_EVENT_APP_DESTROY;
        evt.window = g_mainWindow;
        g_mainWindow->DispatchEvent(evt);
    }
}

extern "C" void jalium_android_on_low_memory()
{
    if (g_mainWindow)
    {
        JaliumPlatformEvent evt{};
        evt.type = JALIUM_EVENT_LOW_MEMORY;
        evt.window = g_mainWindow;
        g_mainWindow->DispatchEvent(evt);
    }
}

extern "C" void jalium_android_set_safe_area_insets(float top, float bottom, float left, float right)
{
    if (g_mainWindow)
    {
        JaliumPlatformEvent evt{};
        evt.type = JALIUM_EVENT_SAFE_AREA_CHANGED;
        evt.window = g_mainWindow;
        evt.safeArea.top = top;
        evt.safeArea.bottom = bottom;
        evt.safeArea.left = left;
        evt.safeArea.right = right;
        g_mainWindow->DispatchEvent(evt);
    }
}

extern "C" void jalium_android_set_keyboard_visible(int32_t visible, int32_t heightPx)
{
    if (g_mainWindow)
    {
        JaliumPlatformEvent evt{};
        evt.type = JALIUM_EVENT_KEYBOARD_CHANGED;
        evt.window = g_mainWindow;
        evt.keyboard.visible = visible;
        evt.keyboard.heightPx = heightPx;
        g_mainWindow->DispatchEvent(evt);
    }
}

extern "C" void jalium_android_set_orientation(int32_t orientation)
{
    if (g_mainWindow)
    {
        JaliumPlatformEvent evt{};
        evt.type = JALIUM_EVENT_ORIENTATION_CHANGED;
        evt.window = g_mainWindow;
        evt.orientationChanged.orientation = orientation;
        g_mainWindow->DispatchEvent(evt);
    }
}

// Input event processing from Android
static int32_t TranslateAndroidMotionAction(int32_t action)
{
    switch (action & AMOTION_EVENT_ACTION_MASK)
    {
    case AMOTION_EVENT_ACTION_DOWN:
    case AMOTION_EVENT_ACTION_POINTER_DOWN:
        return JALIUM_EVENT_POINTER_DOWN;
    case AMOTION_EVENT_ACTION_UP:
    case AMOTION_EVENT_ACTION_POINTER_UP:
        return JALIUM_EVENT_POINTER_UP;
    case AMOTION_EVENT_ACTION_MOVE:
        return JALIUM_EVENT_POINTER_MOVE;
    case AMOTION_EVENT_ACTION_CANCEL:
        return JALIUM_EVENT_POINTER_CANCEL;
    default:
        return JALIUM_EVENT_NONE;
    }
}

// Key Mapping: Android AKEYCODE -> Jalium Virtual Key (Win32 VK compatible)
static int32_t AndroidKeyCodeToJaliumVK(int32_t keyCode)
{
    // A-Z: AKEYCODE_A=29..AKEYCODE_Z=54 -> VK_A=0x41..VK_Z=0x5A
    if (keyCode >= 29 && keyCode <= 54) return 0x41 + (keyCode - 29);

    // 0-9: AKEYCODE_0=7..AKEYCODE_9=16 -> VK_0=0x30..VK_9=0x39
    if (keyCode >= 7 && keyCode <= 16) return 0x30 + (keyCode - 7);

    // F1-F12: AKEYCODE_F1=131..AKEYCODE_F12=142 -> VK_F1=0x70..VK_F12=0x7B
    if (keyCode >= 131 && keyCode <= 142) return 0x70 + (keyCode - 131);

    // Numpad 0-9: AKEYCODE_NUMPAD_0=144..AKEYCODE_NUMPAD_9=153 -> VK_NUMPAD0=0x60..VK_NUMPAD9=0x69
    if (keyCode >= 144 && keyCode <= 153) return 0x60 + (keyCode - 144);

    switch (keyCode)
    {
        case AKEYCODE_DEL:           return 0x08; // VK_BACK (Backspace)
        case AKEYCODE_TAB:           return 0x09; // VK_TAB
        case AKEYCODE_ENTER:         return 0x0D; // VK_RETURN
        case AKEYCODE_SHIFT_LEFT:
        case AKEYCODE_SHIFT_RIGHT:   return 0x10; // VK_SHIFT
        case AKEYCODE_CTRL_LEFT:
        case AKEYCODE_CTRL_RIGHT:    return 0x11; // VK_CONTROL
        case AKEYCODE_ALT_LEFT:
        case AKEYCODE_ALT_RIGHT:     return 0x12; // VK_MENU (Alt)
        case AKEYCODE_CAPS_LOCK:     return 0x14; // VK_CAPITAL
        case AKEYCODE_ESCAPE:        return 0x1B; // VK_ESCAPE
        case AKEYCODE_SPACE:         return 0x20; // VK_SPACE
        case AKEYCODE_PAGE_UP:       return 0x21; // VK_PRIOR
        case AKEYCODE_PAGE_DOWN:     return 0x22; // VK_NEXT
        case AKEYCODE_MOVE_END:      return 0x23; // VK_END
        case AKEYCODE_HOME:          return 0x24; // VK_HOME (Note: AKEYCODE_HOME=3 is system home)
        case AKEYCODE_DPAD_LEFT:     return 0x25; // VK_LEFT
        case AKEYCODE_DPAD_UP:       return 0x26; // VK_UP
        case AKEYCODE_DPAD_RIGHT:    return 0x27; // VK_RIGHT
        case AKEYCODE_DPAD_DOWN:     return 0x28; // VK_DOWN
        case AKEYCODE_INSERT:        return 0x2D; // VK_INSERT
        case AKEYCODE_FORWARD_DEL:   return 0x2E; // VK_DELETE
        case AKEYCODE_NUM_LOCK:      return 0x90; // VK_NUMLOCK
        case AKEYCODE_SCROLL_LOCK:   return 0x91; // VK_SCROLL
        case AKEYCODE_NUMPAD_ENTER:  return 0x0D; // VK_RETURN
        case AKEYCODE_NUMPAD_MULTIPLY: return 0x6A; // VK_MULTIPLY
        case AKEYCODE_NUMPAD_ADD:    return 0x6B; // VK_ADD
        case AKEYCODE_NUMPAD_SUBTRACT: return 0x6D; // VK_SUBTRACT
        case AKEYCODE_NUMPAD_DOT:    return 0x6E; // VK_DECIMAL
        case AKEYCODE_NUMPAD_DIVIDE: return 0x6F; // VK_DIVIDE
        case AKEYCODE_SEMICOLON:     return 0xBA; // VK_OEM_1 (;:)
        case AKEYCODE_EQUALS:        return 0xBB; // VK_OEM_PLUS (=+)
        case AKEYCODE_COMMA:         return 0xBC; // VK_OEM_COMMA (,<)
        case AKEYCODE_MINUS:         return 0xBD; // VK_OEM_MINUS (-_)
        case AKEYCODE_PERIOD:        return 0xBE; // VK_OEM_PERIOD (.>)
        case AKEYCODE_SLASH:         return 0xBF; // VK_OEM_2 (/?)
        case AKEYCODE_GRAVE:         return 0xC0; // VK_OEM_3 (`~)
        case AKEYCODE_LEFT_BRACKET:  return 0xDB; // VK_OEM_4 ([{)
        case AKEYCODE_BACKSLASH:     return 0xDC; // VK_OEM_5 (\|)
        case AKEYCODE_RIGHT_BRACKET: return 0xDD; // VK_OEM_6 (]})
        case AKEYCODE_APOSTROPHE:    return 0xDE; // VK_OEM_7 ('")
        case AKEYCODE_BACK:          return 0x1B; // Map Android Back button -> VK_ESCAPE
        default:                     return keyCode; // pass through unknown codes
    }
}

extern "C" int32_t jalium_android_on_input_event(AInputEvent* event)
{
    if (!g_mainWindow || !event) return 0;

    int32_t type = AInputEvent_getType(event);

    if (type == AINPUT_EVENT_TYPE_MOTION)
    {
        int32_t action = AMotionEvent_getAction(event);
        int32_t eventType = TranslateAndroidMotionAction(action);
        if (eventType == JALIUM_EVENT_NONE) return 0;

        int32_t pointerIndex = (action & AMOTION_EVENT_ACTION_POINTER_INDEX_MASK)
                               >> AMOTION_EVENT_ACTION_POINTER_INDEX_SHIFT;

        // For MOVE events, dispatch all changed pointers
        int32_t pointerCount = AMotionEvent_getPointerCount(event);
        int32_t startIdx = pointerIndex;
        int32_t endIdx = pointerIndex + 1;
        if (eventType == JALIUM_EVENT_POINTER_MOVE || eventType == JALIUM_EVENT_POINTER_CANCEL)
        {
            startIdx = 0;
            endIdx = pointerCount;
        }

        for (int32_t i = startIdx; i < endIdx && i < pointerCount; i++)
        {
            JaliumPlatformEvent evt{};
            evt.type = static_cast<JaliumEventType>(eventType);
            evt.window = g_mainWindow;
            evt.pointer.pointerId = AMotionEvent_getPointerId(event, i);
            evt.pointer.x = AMotionEvent_getX(event, i);
            evt.pointer.y = AMotionEvent_getY(event, i);
            evt.pointer.pressure = AMotionEvent_getPressure(event, i);
            evt.pointer.tiltX = AMotionEvent_getAxisValue(event, AMOTION_EVENT_AXIS_TILT, i);
            evt.pointer.tiltY = 0; // Android provides a single tilt angle, not X/Y separately
            evt.pointer.twist = AMotionEvent_getAxisValue(event, AMOTION_EVENT_AXIS_ORIENTATION, i);

            int32_t toolType = AMotionEvent_getToolType(event, i);
            switch (toolType)
            {
            case AMOTION_EVENT_TOOL_TYPE_FINGER:
                evt.pointer.pointerType = JALIUM_POINTER_TOUCH;
                break;
            case AMOTION_EVENT_TOOL_TYPE_STYLUS:
            case AMOTION_EVENT_TOOL_TYPE_ERASER:
                evt.pointer.pointerType = JALIUM_POINTER_PEN;
                break;
            case AMOTION_EVENT_TOOL_TYPE_MOUSE:
                evt.pointer.pointerType = JALIUM_POINTER_MOUSE;
                break;
            default:
                evt.pointer.pointerType = JALIUM_POINTER_TOUCH;
                break;
            }

            evt.pointer.modifiers = JALIUM_MOD_NONE;
            int32_t metaState = AMotionEvent_getMetaState(event);
            if (metaState & AMETA_SHIFT_ON) evt.pointer.modifiers |= JALIUM_MOD_SHIFT;
            if (metaState & AMETA_CTRL_ON) evt.pointer.modifiers |= JALIUM_MOD_CTRL;
            if (metaState & AMETA_ALT_ON) evt.pointer.modifiers |= JALIUM_MOD_ALT;
            if (metaState & AMETA_META_ON) evt.pointer.modifiers |= JALIUM_MOD_META;

            g_mainWindow->DispatchEvent(evt);
        }
        return 1;
    }
    else if (type == AINPUT_EVENT_TYPE_KEY)
    {
        int32_t action = AKeyEvent_getAction(event);
        int32_t keyCode = AKeyEvent_getKeyCode(event);

        JaliumPlatformEvent evt{};
        evt.window = g_mainWindow;
        evt.type = (action == AKEY_EVENT_ACTION_DOWN) ? JALIUM_EVENT_KEY_DOWN : JALIUM_EVENT_KEY_UP;
        evt.key.keyCode = AndroidKeyCodeToJaliumVK(keyCode);
        evt.key.scanCode = AKeyEvent_getScanCode(event);
        evt.key.isRepeat = (AKeyEvent_getRepeatCount(event) > 0) ? 1 : 0;

        int32_t metaState = AKeyEvent_getMetaState(event);
        evt.key.modifiers = JALIUM_MOD_NONE;
        if (metaState & AMETA_SHIFT_ON) evt.key.modifiers |= JALIUM_MOD_SHIFT;
        if (metaState & AMETA_CTRL_ON) evt.key.modifiers |= JALIUM_MOD_CTRL;
        if (metaState & AMETA_ALT_ON) evt.key.modifiers |= JALIUM_MOD_ALT;
        if (metaState & AMETA_META_ON) evt.key.modifiers |= JALIUM_MOD_META;

        g_mainWindow->DispatchEvent(evt);
        return 1;
    }

    return 0;
}

// ============================================================================
// Managed-callable input injection (called from C# via P/Invoke)
// These accept raw parameters instead of AInputEvent*, allowing .NET Android
// Activity touch/key overrides to bridge input into the Jalium event pipeline.
// ============================================================================

// action: 0=DOWN, 1=UP, 2=MOVE, 3=CANCEL (matches Android MotionEvent actions)
extern "C" void jalium_android_inject_touch(
    int32_t pointerId, float x, float y, float pressure,
    int32_t action, int32_t pointerType, int32_t modifiers)
{
    if (!g_mainWindow) return;

    JaliumEventType eventType;
    switch (action)
    {
    case 0: // ACTION_DOWN / ACTION_POINTER_DOWN
        eventType = JALIUM_EVENT_POINTER_DOWN;
        break;
    case 1: // ACTION_UP / ACTION_POINTER_UP
        eventType = JALIUM_EVENT_POINTER_UP;
        break;
    case 2: // ACTION_MOVE
        eventType = JALIUM_EVENT_POINTER_MOVE;
        break;
    case 3: // ACTION_CANCEL
        eventType = JALIUM_EVENT_POINTER_CANCEL;
        break;
    default:
        return;
    }

    JaliumPlatformEvent evt{};
    evt.type = eventType;
    evt.window = g_mainWindow;
    evt.pointer.pointerId = pointerId;
    evt.pointer.x = x;
    evt.pointer.y = y;
    evt.pointer.pressure = pressure;
    evt.pointer.tiltX = 0;
    evt.pointer.tiltY = 0;
    evt.pointer.twist = 0;
    evt.pointer.pointerType = pointerType;
    evt.pointer.modifiers = modifiers;

    g_mainWindow->DispatchEvent(evt);
}

// action: 0=KEY_DOWN, 1=KEY_UP
extern "C" void jalium_android_inject_key(
    int32_t androidKeyCode, int32_t scanCode,
    int32_t action, int32_t metaState, int32_t repeatCount)
{
    if (!g_mainWindow) return;

    JaliumPlatformEvent evt{};
    evt.window = g_mainWindow;
    evt.type = (action == 0) ? JALIUM_EVENT_KEY_DOWN : JALIUM_EVENT_KEY_UP;
    evt.key.keyCode = AndroidKeyCodeToJaliumVK(androidKeyCode);
    evt.key.scanCode = scanCode;
    evt.key.isRepeat = (repeatCount > 0) ? 1 : 0;

    evt.key.modifiers = JALIUM_MOD_NONE;
    if (metaState & 0x01) evt.key.modifiers |= JALIUM_MOD_SHIFT;  // META_SHIFT_ON
    if (metaState & 0x1000) evt.key.modifiers |= JALIUM_MOD_CTRL; // META_CTRL_ON
    if (metaState & 0x02) evt.key.modifiers |= JALIUM_MOD_ALT;    // META_ALT_ON
    if (metaState & 0x10000) evt.key.modifiers |= JALIUM_MOD_META; // META_META_ON

    g_mainWindow->DispatchEvent(evt);
}

// Inject a character input event (Unicode codepoint)
extern "C" void jalium_android_inject_char(uint32_t codepoint)
{
    if (!g_mainWindow || codepoint == 0) return;

    JaliumPlatformEvent evt{};
    evt.type = JALIUM_EVENT_CHAR_INPUT;
    evt.window = g_mainWindow;
    evt.character.codepoint = codepoint;

    g_mainWindow->DispatchEvent(evt);
}

// ============================================================================
// Event Loop
// ============================================================================

// Forward declaration so jalium_platform_run_message_loop can reference it
// before the Dispatcher section below.
static int DispatcherLooperCallback(int fd, int events, void* data);

int32_t jalium_platform_run_message_loop(void)
{
    // Ensure this thread has an ALooper. If jalium_platform_init was called on
    // a different thread (e.g., Android main thread), g_looper belongs to that
    // thread. Prepare a new looper for the current thread if needed.
    ALooper* threadLooper = ALooper_forThread();
    if (!threadLooper)
    {
        ALooper* newLooper = ALooper_prepare(ALOOPER_PREPARE_ALLOW_NON_CALLBACKS);
        LOGI("jalium_platform_run_message_loop: prepared new looper=%p (was %p on main thread)", newLooper, g_looper);

        // If we created a new looper for this thread, re-register any existing
        // dispatcher so its eventfd fires on THIS thread (not the main thread).
        if (newLooper && newLooper != g_looper && g_dispatcher && g_dispatcher->eventFd >= 0)
        {
            // Remove from old looper if possible (best-effort, may be on another thread)
            if (g_looper)
                ALooper_removeFd(g_looper, g_dispatcher->eventFd);
            ALooper_addFd(newLooper, g_dispatcher->eventFd, LOOPER_ID_DISPATCHER,
                          ALOOPER_EVENT_INPUT, DispatcherLooperCallback, g_dispatcher);
            LOGI("jalium_platform_run_message_loop: re-registered dispatcher fd=%d on new looper", g_dispatcher->eventFd);
        }

        g_looper = newLooper;
    }
    else if (!g_looper)
    {
        g_looper = threadLooper;
    }

    LOGI("jalium_platform_run_message_loop: starting loop on looper=%p", g_looper);
    g_quitRequested = false;

    while (!g_quitRequested.load(std::memory_order_acquire))
    {
        int events;
        int fd;
        void* data;

        // Poll looper with timeout
        int result = ALooper_pollOnce(100, &fd, &events, &data);

        if (result == LOOPER_ID_DISPATCHER)
        {
            // Dispatcher callback — drain eventfd and invoke callback
            // This is handled by the dispatcher's looper callback
        }
    }

    return g_exitCode.load(std::memory_order_acquire);
}

int32_t jalium_platform_poll_events(void)
{
    int events;
    int fd;
    void* data;
    int count = 0;

    while (ALooper_pollOnce(0, &fd, &events, &data) >= 0)
    {
        count++;
    }
    return count;
}

void jalium_platform_quit(int32_t exitCode)
{
    g_exitCode = exitCode;
    g_quitRequested = true;
    ALooper_wake(g_looper);
}

// ============================================================================
// Dispatcher (eventfd + ALooper)
// ============================================================================

static int DispatcherLooperCallback(int fd, int events, void* data)
{
    auto disp = static_cast<JaliumDispatcher*>(data);
    if (disp && (events & ALOOPER_EVENT_INPUT))
    {
        uint64_t val;
        read(fd, &val, sizeof(val));
        if (disp->callback)
            disp->callback(disp->userData);
    }
    return 1; // Continue receiving callbacks
}

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

    // Do NOT register on any looper yet.
    // The JaliumUI thread's looper doesn't exist until jalium_platform_run_message_loop
    // calls ALooper_prepare(). Registering on g_looper (main thread) would cause the
    // callback to fire on the wrong thread, violating Dispatcher thread affinity.
    // jalium_platform_run_message_loop will register on the correct thread's looper.
    LOGI("jalium_dispatcher_create: fd=%d created (deferred registration until message loop)", disp->eventFd);

    g_dispatcher = disp;
    *outDispatcher = disp;
    return JALIUM_OK;
}

void jalium_dispatcher_destroy(JaliumDispatcher* dispatcher)
{
    if (!dispatcher) return;
    if (dispatcher->eventFd >= 0)
    {
        if (g_looper)
            ALooper_removeFd(g_looper, dispatcher->eventFd);
        close(dispatcher->eventFd);
    }
    if (g_dispatcher == dispatcher)
        g_dispatcher = nullptr;
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
// High-Resolution Timer
// ============================================================================

// On Android, prefer AChoreographer for frame-aligned timing.
// For general-purpose timers, use clock_nanosleep.

JaliumResult jalium_timer_create(JaliumTimer** outTimer)
{
    if (!outTimer) return JALIUM_ERROR_INVALID_ARGUMENT;

    auto timer = new JaliumTimer();
    *outTimer = timer;
    return JALIUM_OK;
}

void jalium_timer_destroy(JaliumTimer* timer)
{
    if (!timer) return;
    delete timer;
}

void jalium_timer_arm(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer) return;
    timer->intervalUs = intervalMicroseconds;
    timer->repeating = false;
}

void jalium_timer_arm_repeating(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer) return;
    timer->intervalUs = intervalMicroseconds;
    timer->repeating = true;
}

void jalium_timer_disarm(JaliumTimer* timer)
{
    if (timer) timer->intervalUs = 0;
}

void jalium_timer_set_callback(JaliumTimer* timer, JaliumTimerCallback callback, void* userData)
{
    if (!timer) return;
    timer->callback = callback;
    timer->userData = userData;
}

int32_t jalium_timer_wait(JaliumTimer* timer, uint32_t timeoutMs)
{
    if (!timer || timer->intervalUs <= 0) return 0;

    struct timespec ts;
    ts.tv_sec = timer->intervalUs / 1000000;
    ts.tv_nsec = (timer->intervalUs % 1000000) * 1000;
    clock_nanosleep(CLOCK_MONOTONIC, 0, &ts, nullptr);
    return 1;
}

// ============================================================================
// DPI and Display
// ============================================================================

float jalium_platform_get_system_dpi_scale(void)
{
    return g_density;
}

float jalium_window_get_dpi_scale(JaliumPlatformWindow* window)
{
    if (!window) return g_density;
    return window->dpiScale;
}

int32_t jalium_window_get_monitor_refresh_rate(JaliumPlatformWindow* window)
{
    (void)window;
    return g_refreshRate;
}

// ============================================================================
// Input State Polling
// ============================================================================

int16_t jalium_input_get_key_state(int32_t jaliumVirtualKey)
{
    // Not available on Android through native API; would need JNI
    (void)jaliumVirtualKey;
    return 0;
}

void jalium_input_get_cursor_pos(float* x, float* y)
{
    // Not applicable on touch devices
    if (x) *x = 0;
    if (y) *y = 0;
}

// ============================================================================
// Clipboard (JNI bridge to android.content.ClipboardManager)
// ============================================================================

JaliumResult jalium_clipboard_get_text(wchar_t** outText)
{
    if (!outText) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outText = nullptr;

    JNIEnv* env = GetJNIEnv();
    if (!env || !g_activityObj) return JALIUM_ERROR_NOT_SUPPORTED;

    // Context.getSystemService("clipboard") -> ClipboardManager
    jclass contextClass = env->GetObjectClass(g_activityObj);
    if (!contextClass) return JALIUM_ERROR_UNKNOWN;

    jmethodID getSystemService = env->GetMethodID(contextClass, "getSystemService",
        "(Ljava/lang/String;)Ljava/lang/Object;");
    jstring clipboardStr = env->NewStringUTF("clipboard");
    jobject clipManager = env->CallObjectMethod(g_activityObj, getSystemService, clipboardStr);
    env->DeleteLocalRef(clipboardStr);
    env->DeleteLocalRef(contextClass);

    if (!clipManager)
        return JALIUM_OK; // No clipboard manager, return empty

    // ClipboardManager.getPrimaryClip() -> ClipData
    jclass cmClass = env->GetObjectClass(clipManager);
    jmethodID getPrimaryClip = env->GetMethodID(cmClass, "getPrimaryClip",
        "()Landroid/content/ClipData;");
    jobject clipData = env->CallObjectMethod(clipManager, getPrimaryClip);
    env->DeleteLocalRef(cmClass);

    if (!clipData)
    {
        env->DeleteLocalRef(clipManager);
        return JALIUM_OK; // No clip data
    }

    // ClipData.getItemAt(0) -> ClipData.Item
    jclass clipDataClass = env->GetObjectClass(clipData);
    jmethodID getItemAt = env->GetMethodID(clipDataClass, "getItemAt",
        "(I)Landroid/content/ClipData$Item;");
    jobject item = env->CallObjectMethod(clipData, getItemAt, 0);
    env->DeleteLocalRef(clipDataClass);
    env->DeleteLocalRef(clipData);

    if (!item)
    {
        env->DeleteLocalRef(clipManager);
        return JALIUM_OK;
    }

    // ClipData.Item.getText() -> CharSequence, then toString()
    jclass itemClass = env->GetObjectClass(item);
    jmethodID getText = env->GetMethodID(itemClass, "getText",
        "()Ljava/lang/CharSequence;");
    jobject charSeq = env->CallObjectMethod(item, getText);
    env->DeleteLocalRef(itemClass);
    env->DeleteLocalRef(item);
    env->DeleteLocalRef(clipManager);

    if (!charSeq)
        return JALIUM_OK;

    // CharSequence.toString() -> String
    jclass charSeqClass = env->GetObjectClass(charSeq);
    jmethodID toString = env->GetMethodID(charSeqClass, "toString",
        "()Ljava/lang/String;");
    jstring jstr = (jstring)env->CallObjectMethod(charSeq, toString);
    env->DeleteLocalRef(charSeqClass);
    env->DeleteLocalRef(charSeq);

    if (!jstr)
        return JALIUM_OK;

    // Convert Java UTF-16 string to wchar_t*
    const jchar* chars = env->GetStringChars(jstr, nullptr);
    jsize len = env->GetStringLength(jstr);

    wchar_t* result = (wchar_t*)malloc((len + 1) * sizeof(wchar_t));
    if (!result)
    {
        env->ReleaseStringChars(jstr, chars);
        env->DeleteLocalRef(jstr);
        return JALIUM_ERROR_OUT_OF_MEMORY;
    }

    for (jsize i = 0; i < len; i++)
        result[i] = (wchar_t)chars[i];
    result[len] = L'\0';

    env->ReleaseStringChars(jstr, chars);
    env->DeleteLocalRef(jstr);

    *outText = result;
    return JALIUM_OK;
}

JaliumResult jalium_clipboard_set_text(const wchar_t* text)
{
    if (!text) return JALIUM_ERROR_INVALID_ARGUMENT;

    JNIEnv* env = GetJNIEnv();
    if (!env || !g_activityObj) return JALIUM_ERROR_NOT_SUPPORTED;

    // Convert wchar_t* to Java String (UTF-16)
    size_t len = wcslen(text);
    jchar* jchars = (jchar*)malloc(len * sizeof(jchar));
    if (!jchars) return JALIUM_ERROR_OUT_OF_MEMORY;
    for (size_t i = 0; i < len; i++)
        jchars[i] = (jchar)text[i];

    jstring jstr = env->NewString(jchars, (jsize)len);
    free(jchars);

    if (!jstr) return JALIUM_ERROR_UNKNOWN;

    // Context.getSystemService("clipboard") -> ClipboardManager
    jclass contextClass = env->GetObjectClass(g_activityObj);
    jmethodID getSystemService = env->GetMethodID(contextClass, "getSystemService",
        "(Ljava/lang/String;)Ljava/lang/Object;");
    jstring clipboardStr = env->NewStringUTF("clipboard");
    jobject clipManager = env->CallObjectMethod(g_activityObj, getSystemService, clipboardStr);
    env->DeleteLocalRef(clipboardStr);
    env->DeleteLocalRef(contextClass);

    if (!clipManager)
    {
        env->DeleteLocalRef(jstr);
        return JALIUM_ERROR_NOT_SUPPORTED;
    }

    // ClipData.newPlainText("text", text) -> ClipData
    jclass clipDataClass = env->FindClass("android/content/ClipData");
    jmethodID newPlainText = env->GetStaticMethodID(clipDataClass, "newPlainText",
        "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;)Landroid/content/ClipData;");
    jstring label = env->NewStringUTF("text");
    jobject clipData = env->CallStaticObjectMethod(clipDataClass, newPlainText, label, jstr);
    env->DeleteLocalRef(label);
    env->DeleteLocalRef(jstr);
    env->DeleteLocalRef(clipDataClass);

    if (!clipData)
    {
        env->DeleteLocalRef(clipManager);
        return JALIUM_ERROR_UNKNOWN;
    }

    // ClipboardManager.setPrimaryClip(clipData)
    jclass cmClass = env->GetObjectClass(clipManager);
    jmethodID setPrimaryClip = env->GetMethodID(cmClass, "setPrimaryClip",
        "(Landroid/content/ClipData;)V");
    env->CallVoidMethod(clipManager, setPrimaryClip, clipData);
    env->DeleteLocalRef(cmClass);
    env->DeleteLocalRef(clipData);
    env->DeleteLocalRef(clipManager);

    // Check for JNI exceptions
    if (env->ExceptionCheck())
    {
        env->ExceptionClear();
        return JALIUM_ERROR_UNKNOWN;
    }

    return JALIUM_OK;
}

#endif // __ANDROID__
