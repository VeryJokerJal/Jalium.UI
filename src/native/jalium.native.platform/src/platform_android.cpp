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
#include <time.h>
#include <poll.h>

#include <atomic>
#include <mutex>

#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, "JaliumPlatform", __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, "JaliumPlatform", __VA_ARGS__)

// ============================================================================
// Global State
// ============================================================================

static std::atomic<bool> g_quitRequested{false};
static std::atomic<int32_t> g_exitCode{0};
static float            g_density = 1.0f;  // DisplayMetrics density
static int32_t          g_refreshRate = 60;

// Platform initialization can run on Android's main thread while the Jalium
// message loop runs on a dedicated thread. Never cache a borrowed main-thread
// looper. The published run looper owns an acquired reference and exists only
// so cross-thread quit can wake the loop that is currently pumping.
static ALooper*         g_runLooper = nullptr;
static std::mutex       g_looperMutex;

// JNI state for clipboard and other system services. JavaVM is process-stable;
// the Activity global ref is protected by a mutex. Callers never borrow the
// global directly: while holding the same mutex they create a thread-local ref,
// which remains valid after an Activity replacement deletes the old global.
static std::atomic<JavaVM*> g_javaVM{nullptr};
static jobject              g_activityObj = nullptr;
static std::mutex           g_activityObjMutex;

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

// ANativeWindow_fromSurface() returns an acquired reference owned by the
// managed Activity.  The platform layer keeps its own reference so the
// Surface descriptor remains valid until the Jalium UI thread has torn down
// every render target that can still touch it.
static void ReplaceOwnedNativeWindow(ANativeWindow*& slot, ANativeWindow* next)
{
    if (slot == next)
        return;

    if (next)
        ANativeWindow_acquire(next);

    ANativeWindow* previous = slot;
    slot = next;

    if (previous)
        ANativeWindow_release(previous);
}

// Tracked globally so the message loop can re-register it with the correct looper.
static JaliumDispatcher* g_dispatcher = nullptr;

// ============================================================================
// Dispatcher Structure
// ============================================================================

struct JaliumDispatcher {
    int                     eventFd = -1;
    JaliumDispatcherCallback callback = nullptr;
    void*                   userData = nullptr;
    // Acquired reference to the exact looper on which eventFd was registered.
    // It is never inferred from the process-wide current run-loop slot.
    ALooper*                registeredLooper = nullptr;
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
    // The message-loop thread owns looper creation/registration. Preparing a
    // looper here would bind platform state to whichever Android callback
    // happened to initialize the process (normally the Java main thread).
    return JALIUM_OK;
}

void jalium_platform_shutdown_impl()
{
    // A surface can arrive before the managed Window is constructed.  If the
    // application shuts down in that interval, release the pending ownership
    // here rather than leaking it for the lifetime of the process.
    ReplaceOwnedNativeWindow(g_pendingNativeWindow, nullptr);

    JavaVM* vm = g_javaVM.load(std::memory_order_acquire);
    JNIEnv* env = nullptr;
    if (vm)
    {
        int status = vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6);
        if (status == JNI_EDETACHED &&
            vm->AttachCurrentThread(&env, nullptr) != JNI_OK)
        {
            env = nullptr;
        }
    }

    if (env)
    {
        std::lock_guard<std::mutex> lock(g_activityObjMutex);
        if (g_activityObj)
        {
            env->DeleteGlobalRef(g_activityObj);
            g_activityObj = nullptr;
        }
    }
    g_javaVM.store(nullptr, std::memory_order_release);

    ALooper* runLooper = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_looperMutex);
        runLooper = g_runLooper;
        g_runLooper = nullptr;
    }
    if (runLooper)
    {
        ALooper_wake(runLooper);
        ALooper_release(runLooper);
    }
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
        // Transfer (do not acquire/release) the platform-owned pending
        // reference into the newly-created window wrapper.
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
    ReplaceOwnedNativeWindow(window->nativeWindow, nullptr);
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

void jalium_window_set_title(JaliumPlatformWindow* window, const JaliumUtf16Char* title)
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
    if (!vm)
        return;

    // The setter is normally called from Android's main thread, but tolerate a
    // future call serialized onto JaliumUI by attaching that thread first.
    JNIEnv* env = nullptr;
    int status = vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6);
    if (status == JNI_EDETACHED &&
        vm->AttachCurrentThread(&env, nullptr) != JNI_OK)
    {
        return;
    }

    g_javaVM.store(vm, std::memory_order_release);
    std::lock_guard<std::mutex> lock(g_activityObjMutex);
    jobject replacement = activity ? env->NewGlobalRef(activity) : nullptr;
    if (activity && !replacement)
        return;

    jobject previous = g_activityObj;
    g_activityObj = replacement;
    if (previous)
    {
        // Every consumer creates its local ref while holding this mutex, so an
        // in-flight JNI operation no longer depends on this global reference.
        env->DeleteGlobalRef(previous);
    }
}

/// Helper: Attach current thread and get JNIEnv.
static JNIEnv* GetJNIEnv()
{
    JavaVM* vm = g_javaVM.load(std::memory_order_acquire);
    if (!vm) return nullptr;
    JNIEnv* env = nullptr;
    int status = vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6);
    if (status == JNI_EDETACHED)
    {
        if (vm->AttachCurrentThread(&env, nullptr) != JNI_OK)
            return nullptr;
    }
    return env;
}

/// Creates a local Activity reference for the calling JNI thread. The local
/// reference has its own lifetime, so config replacement may safely swap and
/// delete the old global immediately after this function releases the mutex.
static jobject BorrowActivityLocalRef(JNIEnv* env)
{
    if (!env) return nullptr;
    std::lock_guard<std::mutex> lock(g_activityObjMutex);
    return g_activityObj ? env->NewLocalRef(g_activityObj) : nullptr;
}

class ScopedJniLocalRef
{
public:
    ScopedJniLocalRef(JNIEnv* env, jobject value) : env_(env), value_(value) {}
    ~ScopedJniLocalRef()
    {
        if (env_ && value_)
            env_->DeleteLocalRef(value_);
    }

    ScopedJniLocalRef(const ScopedJniLocalRef&) = delete;
    ScopedJniLocalRef& operator=(const ScopedJniLocalRef&) = delete;

private:
    JNIEnv* env_;
    jobject value_;
};

/// Public C ABI: returns the JNIEnv for the calling thread, attaching it to
/// the JavaVM if necessary. Returns nullptr if the platform was never bound
/// to a JavaVM via jalium_android_set_jni_env. Used by jalium.native.media.android
/// to call Java APIs (BitmapFactory fallback, MediaCodecList probe).
extern "C" JNIEnv* jalium_android_get_jni_env(void)
{
    return GetJNIEnv();
}

/// Public C ABI: returns a NEW LOCAL Activity reference for the calling JNI
/// thread. The caller owns that local reference and must DeleteLocalRef it when
/// finished. Returning a local rather than the cached global makes config
/// replacement safe even when the caller continues using the old Activity.
extern "C" jobject jalium_android_get_activity(void)
{
    JNIEnv* env = GetJNIEnv();
    return BorrowActivityLocalRef(env);
}

/// Public C ABI: returns the cached JavaVM pointer (or nullptr).
extern "C" JavaVM* jalium_android_get_java_vm(void)
{
    return g_javaVM.load(std::memory_order_acquire);
}

// ============================================================================
// Android Native Activity Callbacks
// ============================================================================

// Called from NativeActivity.onCreate or when native window is created
extern "C" void jalium_android_set_native_window(ANativeWindow* nativeWindow, int width, int height)
{
    LOGI("jalium_android_set_native_window: win=%p, g_mainWindow=%p, w=%d h=%d",
         nativeWindow, g_mainWindow, width, height);

    if (nativeWindow)
        ANativeWindow_setBuffersGeometry(nativeWindow, 0, 0, WINDOW_FORMAT_RGBA_8888);

    if (g_mainWindow)
    {
        ReplaceOwnedNativeWindow(g_mainWindow->nativeWindow, nativeWindow);
        if (nativeWindow)
        {
            // Prefer the authoritative dimensions plumbed from
            // SurfaceHolder.Callback.surfaceChanged(width,height): they reflect the
            // post-rotation surface size immediately, whereas ANativeWindow_getWidth/
            // Height can lag during a device rotation. Fall back to the query only
            // when the caller didn't supply usable dims.
            int w = width  > 0 ? width  : ANativeWindow_getWidth(nativeWindow);
            int h = height > 0 ? height : ANativeWindow_getHeight(nativeWindow);
            g_mainWindow->width = w;
            g_mainWindow->height = h;

            LOGI("jalium_android_set_native_window: dispatching RESIZE w=%d h=%d", w, h);

            JaliumPlatformEvent evt{};
            evt.type = JALIUM_EVENT_RESIZE;
            evt.window = g_mainWindow;
            evt.resize.width = w;
            evt.resize.height = h;
            g_mainWindow->DispatchEvent(evt);
        }
    }
    else
    {
        // Window not yet created — store for jalium_window_create() to pick up. The
        // cold-start size resolves via ANativeWindow_getWidth/Height inside
        // jalium_window_create (reliable for the initial, non-rotated orientation).
        LOGI("jalium_android_set_native_window: storing as pendingNativeWindow");
        ReplaceOwnedNativeWindow(g_pendingNativeWindow, nativeWindow);
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

JaliumResult jalium_clipboard_get_text(JaliumUtf16Char** outText)
{
    if (!outText) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outText = nullptr;

    JNIEnv* env = GetJNIEnv();
    jobject activity = BorrowActivityLocalRef(env);
    ScopedJniLocalRef activityRef(env, activity);
    if (!env || !activity) return JALIUM_ERROR_NOT_SUPPORTED;

    // Context.getSystemService("clipboard") -> ClipboardManager
    jclass contextClass = env->GetObjectClass(activity);
    if (!contextClass) return JALIUM_ERROR_UNKNOWN;

    jmethodID getSystemService = env->GetMethodID(contextClass, "getSystemService",
        "(Ljava/lang/String;)Ljava/lang/Object;");
    jstring clipboardStr = env->NewStringUTF("clipboard");
    jobject clipManager = env->CallObjectMethod(activity, getSystemService, clipboardStr);
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

    // Copy Java UTF-16 code units to the fixed-width platform ABI.
    const jchar* chars = env->GetStringChars(jstr, nullptr);
    jsize len = env->GetStringLength(jstr);

    static_assert(sizeof(jchar) == sizeof(JaliumUtf16Char));
    auto result = static_cast<JaliumUtf16Char*>(
        malloc((static_cast<size_t>(len) + 1) * sizeof(JaliumUtf16Char)));
    if (!result)
    {
        env->ReleaseStringChars(jstr, chars);
        env->DeleteLocalRef(jstr);
        return JALIUM_ERROR_OUT_OF_MEMORY;
    }

    memcpy(result, chars, static_cast<size_t>(len) * sizeof(JaliumUtf16Char));
    result[len] = 0;

    env->ReleaseStringChars(jstr, chars);
    env->DeleteLocalRef(jstr);

    *outText = result;
    return JALIUM_OK;
}

JaliumResult jalium_clipboard_set_text(const JaliumUtf16Char* text)
{
    if (!text) return JALIUM_ERROR_INVALID_ARGUMENT;

    JNIEnv* env = GetJNIEnv();
    jobject activity = BorrowActivityLocalRef(env);
    ScopedJniLocalRef activityRef(env, activity);
    if (!env || !activity) return JALIUM_ERROR_NOT_SUPPORTED;

    // JaliumUtf16Char and jchar are both fixed-width UTF-16 code units.
    size_t len = 0;
    while (text[len] != 0) ++len;
    jstring jstr = env->NewString(
        reinterpret_cast<const jchar*>(text), static_cast<jsize>(len));

    if (!jstr) return JALIUM_ERROR_UNKNOWN;

    // Context.getSystemService("clipboard") -> ClipboardManager
    jclass contextClass = env->GetObjectClass(activity);
    jmethodID getSystemService = env->GetMethodID(contextClass, "getSystemService",
        "(Ljava/lang/String;)Ljava/lang/Object;");
    jstring clipboardStr = env->NewStringUTF("clipboard");
    jobject clipManager = env->CallObjectMethod(activity, getSystemService, clipboardStr);
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

void jalium_drag_set_effect(JaliumPlatformWindow*, uint64_t, uint32_t) {}

JaliumResult jalium_drag_begin(
    JaliumPlatformWindow*, const JaliumDragDataItem*, uint32_t, uint32_t,
    uint32_t* performedEffect)
{
    if (performedEffect) *performedEffect = JALIUM_DRAG_EFFECT_NONE;
    return JALIUM_ERROR_NOT_SUPPORTED;
}

// Window-management extensions do not apply to the single-activity Android
// surface; exported for symbol compatibility with the shared ABI header.
int32_t jalium_platform_get_monitor_count(void) { return 0; }

int32_t jalium_platform_get_monitor_info(int32_t, JaliumMonitorInfo* info)
{
    if (info) *info = JaliumMonitorInfo{};
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_min_max_size(JaliumPlatformWindow*, int32_t, int32_t, int32_t, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_begin_move_drag(JaliumPlatformWindow*)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_begin_resize_drag(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_icon(JaliumPlatformWindow*, const uint32_t*, int32_t, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_topmost(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

#endif // __ANDROID__
