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
#include <sys/timerfd.h>
#include <unistd.h>
#include <errno.h>
#include <limits.h>
#include <string.h>
#include <stdlib.h>
#include <time.h>
#include <poll.h>

#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <mutex>
#include <unordered_map>

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
    pid_t                                 ownerTid = -1;
    // Acquired reference to the exact looper on which eventFd was registered.
    // It is never inferred from the process-wide current run-loop slot.
    ALooper*                              registeredLooper = nullptr;
};

// DispatcherCore is thread-affine, including Dispatcher instances created by
// worker-side DispatcherObjects. Resolve the application message loop's wake
// fd by its owner thread instead of using a process-wide first/last-wins slot.
// A newly-created dispatcher replaces an older entry for the same TID. That
// makes TID reuse safe even when a retired worker's managed Dispatcher is
// still reachable; destroying the older handle cannot erase the replacement.
// g_looperMutex guards this registry and registeredLooper.
static std::unordered_map<pid_t, JaliumDispatcher*> g_dispatchersByThread;

static void ReleaseDispatcher(JaliumDispatcher* dispatcher)
{
    if (dispatcher &&
        dispatcher->references.fetch_sub(1, std::memory_order_acq_rel) == 1)
    {
        delete dispatcher;
    }
}

// ============================================================================
// Timer Structure
// ============================================================================

struct JaliumTimer {
    int                 timerFd = -1;
    int                 destroyEventFd = -1;
    JaliumTimerCallback callback = nullptr;
    void*               userData = nullptr;
    std::mutex          stateMutex;
    std::condition_variable waitersDrained;
    uint32_t            activeWaiters = 0;
    bool                destroyed = false;
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

uint32_t jalium_window_get_portal_parent_handle(
    JaliumPlatformWindow*, char*, uint32_t)
{
    return 0;
}

uint32_t jalium_window_get_portal_parent_handle_for_native_handle(
    intptr_t, char*, uint32_t)
{
    return 0;
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

static uint32_t AndroidPointerButtons(int32_t buttonState)
{
    uint32_t buttons = JALIUM_POINTER_BUTTON_NONE;
    if ((buttonState & AMOTION_EVENT_BUTTON_PRIMARY) != 0)
        buttons |= JALIUM_POINTER_BUTTON_PRIMARY;
    if ((buttonState & AMOTION_EVENT_BUTTON_SECONDARY) != 0)
        buttons |= JALIUM_POINTER_BUTTON_SECONDARY;
    if ((buttonState & AMOTION_EVENT_BUTTON_TERTIARY) != 0)
        buttons |= JALIUM_POINTER_BUTTON_TERTIARY;
    if ((buttonState & AMOTION_EVENT_BUTTON_STYLUS_PRIMARY) != 0)
        buttons |= JALIUM_POINTER_BUTTON_BARREL;
    if ((buttonState & AMOTION_EVENT_BUTTON_STYLUS_SECONDARY) != 0)
        buttons |= JALIUM_POINTER_BUTTON_SECONDARY;
    return buttons;
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
            evt.pointer.pressure = eventType == JALIUM_EVENT_POINTER_UP ||
                eventType == JALIUM_EVENT_POINTER_CANCEL
                ? 0.0f
                : std::clamp(AMotionEvent_getPressure(event, i), 0.0f, 1.0f);
            constexpr float RadiansToDegrees = 57.29577951308232f;
            evt.pointer.tiltX = AMotionEvent_getAxisValue(
                event, AMOTION_EVENT_AXIS_TILT, i) * RadiansToDegrees;
            evt.pointer.tiltY = 0; // Android provides a single tilt angle, not X/Y separately
            evt.pointer.twist = AMotionEvent_getAxisValue(
                event, AMOTION_EVENT_AXIS_ORIENTATION, i) * RadiansToDegrees;
            if (evt.pointer.twist < 0.0f) evt.pointer.twist += 360.0f;
            evt.pointer.flags = i == 0 ? JALIUM_POINTER_FLAG_PRIMARY : 0;
            evt.pointer.buttons = AndroidPointerButtons(
                AMotionEvent_getButtonState(event));
            evt.pointer.toolType = JALIUM_POINTER_TOOL_UNKNOWN;

            int32_t toolType = AMotionEvent_getToolType(event, i);
            switch (toolType)
            {
            case AMOTION_EVENT_TOOL_TYPE_FINGER:
                evt.pointer.pointerType = JALIUM_POINTER_TOUCH;
                break;
            case AMOTION_EVENT_TOOL_TYPE_STYLUS:
                evt.pointer.pointerType = JALIUM_POINTER_PEN;
                evt.pointer.toolType = JALIUM_POINTER_TOOL_PEN;
                break;
            case AMOTION_EVENT_TOOL_TYPE_ERASER:
                evt.pointer.pointerType = JALIUM_POINTER_PEN;
                evt.pointer.toolType = JALIUM_POINTER_TOOL_ERASER;
                evt.pointer.flags |= JALIUM_POINTER_FLAG_ERASER;
                break;
            case AMOTION_EVENT_TOOL_TYPE_MOUSE:
                evt.pointer.pointerType = JALIUM_POINTER_MOUSE;
                evt.pointer.toolType = JALIUM_POINTER_TOOL_MOUSE;
                break;
            default:
                evt.pointer.pointerType = JALIUM_POINTER_TOUCH;
                break;
            }
            if (eventType == JALIUM_EVENT_POINTER_DOWN ||
                (eventType == JALIUM_EVENT_POINTER_MOVE &&
                 evt.pointer.pointerType != JALIUM_POINTER_MOUSE))
            {
                evt.pointer.flags |= JALIUM_POINTER_FLAG_IN_RANGE |
                                     JALIUM_POINTER_FLAG_IN_CONTACT;
                if (evt.pointer.buttons == JALIUM_POINTER_BUTTON_NONE)
                    evt.pointer.buttons = JALIUM_POINTER_BUTTON_PRIMARY;
            }
            if ((evt.pointer.buttons & JALIUM_POINTER_BUTTON_BARREL) != 0)
                evt.pointer.flags |= JALIUM_POINTER_FLAG_BARREL;

            evt.pointer.modifiers = JALIUM_MOD_NONE;
            int32_t metaState = AMotionEvent_getMetaState(event);
            if (metaState & AMETA_SHIFT_ON) evt.pointer.modifiers |= JALIUM_MOD_SHIFT;
            if (metaState & AMETA_CTRL_ON) evt.pointer.modifiers |= JALIUM_MOD_CTRL;
            if (metaState & AMETA_ALT_ON) evt.pointer.modifiers |= JALIUM_MOD_ALT;
            if (metaState & AMETA_META_ON) evt.pointer.modifiers |= JALIUM_MOD_META;
            evt.pointer.timestampMillis =
                AMotionEvent_getEventTime(event) / 1000000LL;

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
    int32_t action, int32_t pointerType, int32_t modifiers,
    int64_t eventTimeMillis)
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
    evt.pointer.pressure = eventType == JALIUM_EVENT_POINTER_UP ||
        eventType == JALIUM_EVENT_POINTER_CANCEL
        ? 0.0f : std::clamp(pressure, 0.0f, 1.0f);
    evt.pointer.tiltX = 0;
    evt.pointer.tiltY = 0;
    evt.pointer.twist = 0;
    evt.pointer.pointerType = pointerType;
    evt.pointer.modifiers = modifiers;
    evt.pointer.flags = JALIUM_POINTER_FLAG_PRIMARY;
    if (eventType == JALIUM_EVENT_POINTER_DOWN ||
        eventType == JALIUM_EVENT_POINTER_MOVE)
    {
        evt.pointer.flags |= JALIUM_POINTER_FLAG_IN_RANGE |
                             JALIUM_POINTER_FLAG_IN_CONTACT;
        evt.pointer.buttons = JALIUM_POINTER_BUTTON_PRIMARY;
    }
    evt.pointer.toolType = pointerType == JALIUM_POINTER_PEN
        ? JALIUM_POINTER_TOOL_PEN
        : (pointerType == JALIUM_POINTER_MOUSE
            ? JALIUM_POINTER_TOOL_MOUSE : JALIUM_POINTER_TOOL_UNKNOWN);
    evt.pointer.timestampMillis = eventTimeMillis;

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
    // The JaliumUI thread exclusively owns its looper. PlatformInit can be
    // called from Android's Java main thread and must not influence this choice.
    ALooper* threadLooper = ALooper_forThread();
    if (!threadLooper)
        threadLooper = ALooper_prepare(ALOOPER_PREPARE_ALLOW_NON_CALLBACKS);

    if (!threadLooper)
    {
        LOGE("jalium_platform_run_message_loop: failed to get/prepare thread looper");
        return JALIUM_ERROR_INITIALIZATION_FAILED;
    }

    JaliumDispatcher* dispatcher = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_looperMutex);
        const pid_t currentTid = gettid();
        const auto iterator = g_dispatchersByThread.find(currentTid);
        if (iterator == g_dispatchersByThread.end() ||
            iterator->second->destroyed.load(std::memory_order_acquire))
        {
            LOGE("jalium_platform_run_message_loop: no dispatcher for owner tid=%d",
                 static_cast<int>(currentTid));
            return JALIUM_ERROR_INVALID_STATE;
        }

        dispatcher = iterator->second;
        const int dispatcherFd = dispatcher->eventFd.load(std::memory_order_acquire);
        if (dispatcherFd < 0)
        {
            LOGE("jalium_platform_run_message_loop: dispatcher for tid=%d has no event fd",
                 static_cast<int>(currentTid));
            return JALIUM_ERROR_INVALID_STATE;
        }

        if (dispatcher->registeredLooper)
        {
            LOGE("jalium_platform_run_message_loop: dispatcher fd=%d is already registered on looper=%p",
                 dispatcherFd, dispatcher->registeredLooper);
            return JALIUM_ERROR_INITIALIZATION_FAILED;
        }

        if (g_runLooper)
        {
            LOGE("jalium_platform_run_message_loop: another application loop is already active on looper=%p",
                 g_runLooper);
            return JALIUM_ERROR_INVALID_STATE;
        }

        // Reset only after proving that this thread can publish the next loop.
        // A rejected concurrent loop must never clear the active loop's quit.
        g_quitRequested.store(false, std::memory_order_release);

        // The loop reference keeps the callback data alive even if a foreign
        // thread disposes this Dispatcher while the ALooper is polling.
        dispatcher->references.fetch_add(1, std::memory_order_relaxed);
        if (ALooper_addFd(threadLooper, dispatcherFd, LOOPER_ID_DISPATCHER,
                          ALOOPER_EVENT_INPUT, DispatcherLooperCallback, dispatcher) < 0)
        {
            LOGE("jalium_platform_run_message_loop: failed to register dispatcher fd=%d",
                 dispatcherFd);
            ReleaseDispatcher(dispatcher);
            return JALIUM_ERROR_INITIALIZATION_FAILED;
        }

        ALooper_acquire(threadLooper);
        dispatcher->registeredLooper = threadLooper;

        // Publish a separately-acquired ref for cross-thread quit/wake. A
        // previous loop is never used for fd removal and cannot clear a newer
        // loop's publication when it eventually exits.
        ALooper_acquire(threadLooper);
        g_runLooper = threadLooper;
    }

    LOGI("jalium_platform_run_message_loop: starting loop on looper=%p, owner tid=%d, dispatcher fd=%d",
         threadLooper, static_cast<int>(dispatcher->ownerTid),
         dispatcher->eventFd.load(std::memory_order_acquire));

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

    ALooper* dispatcherLooper = nullptr;
    ALooper* publishedLooper = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_looperMutex);
        if (dispatcher && dispatcher->registeredLooper == threadLooper)
        {
            dispatcherLooper = dispatcher->registeredLooper;
            const int dispatcherFd =
                dispatcher->eventFd.load(std::memory_order_acquire);
            if (dispatcherFd >= 0)
                ALooper_removeFd(dispatcherLooper, dispatcherFd);
            dispatcher->registeredLooper = nullptr;
        }
        if (g_runLooper == threadLooper)
        {
            publishedLooper = g_runLooper;
            g_runLooper = nullptr;
        }
    }

    // Only unregister from the exact looper that owns this dispatcher's fd.
    // In particular, an exiting old loop never removes a newly-created
    // dispatcher's numerically-reused eventfd from a replacement looper.
    if (dispatcherLooper)
        ALooper_release(dispatcherLooper);
    if (publishedLooper)
        ALooper_release(publishedLooper);
    ReleaseDispatcher(dispatcher);

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
    ALooper* runLooper = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_looperMutex);
        // Serialize quit publication with the message loop's reset/publish
        // sequence so a startup race cannot overwrite an accepted quit.
        g_exitCode.store(exitCode, std::memory_order_release);
        g_quitRequested.store(true, std::memory_order_release);
        if (g_runLooper)
        {
            ALooper_acquire(g_runLooper);
            runLooper = g_runLooper;
        }
    }
    if (runLooper)
    {
        ALooper_wake(runLooper);
        ALooper_release(runLooper);
    }
}

// ============================================================================
// Dispatcher (eventfd + ALooper)
// ============================================================================

static int DispatcherLooperCallback(int fd, int events, void* data)
{
    auto disp = static_cast<JaliumDispatcher*>(data);
    if (!disp || !(events & ALOOPER_EVENT_INPUT))
        return 1;

    const int activeFd = disp->eventFd.load(std::memory_order_acquire);
    if (disp->destroyed.load(std::memory_order_acquire) || activeFd != fd)
        return 0;

    uint64_t value;
    while (read(fd, &value, sizeof(value)) < 0 && errno == EINTR)
    {
    }

    std::lock_guard<std::recursive_mutex> callbackLock(disp->callbackMutex);
    if (!disp->destroyed.load(std::memory_order_acquire))
    {
        const auto callback = disp->callback.load(std::memory_order_acquire);
        if (callback)
            callback(disp->userData.load(std::memory_order_acquire));
    }
    return disp->destroyed.load(std::memory_order_acquire) ? 0 : 1;
}

JaliumResult jalium_dispatcher_create(JaliumDispatcher** outDispatcher)
{
    if (!outDispatcher) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outDispatcher = nullptr;

    auto disp = new JaliumDispatcher();
    disp->ownerTid = gettid();
    const int eventFd = eventfd(0, EFD_NONBLOCK | EFD_CLOEXEC);
    disp->eventFd.store(eventFd, std::memory_order_release);
    if (eventFd < 0)
    {
        delete disp;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    {
        std::lock_guard<std::mutex> lock(g_looperMutex);
        const auto [iterator, inserted] =
            g_dispatchersByThread.emplace(disp->ownerTid, disp);
        if (!inserted)
        {
            LOGI("jalium_dispatcher_create: replacing stale dispatcher fd=%d for owner tid=%d",
                 iterator->second->eventFd.load(std::memory_order_acquire),
                 static_cast<int>(disp->ownerTid));
            iterator->second = disp;
        }
    }

    // Do NOT register on any looper yet. The JaliumUI thread's looper does not
    // exist until jalium_platform_run_message_loop calls ALooper_prepare().
    LOGI("jalium_dispatcher_create: fd=%d created for owner tid=%d (deferred registration until message loop)",
         eventFd, static_cast<int>(disp->ownerTid));

    *outDispatcher = disp;
    return JALIUM_OK;
}

void jalium_dispatcher_destroy(JaliumDispatcher* dispatcher)
{
    if (!dispatcher ||
        dispatcher->destroyed.exchange(true, std::memory_order_acq_rel))
    {
        return;
    }

    {
        // Wait for an in-flight managed callback. recursive_mutex also permits
        // the callback to dispose its own Dispatcher without deadlocking.
        std::lock_guard<std::recursive_mutex> callbackLock(dispatcher->callbackMutex);
        dispatcher->callback.store(nullptr, std::memory_order_release);
        dispatcher->userData.store(nullptr, std::memory_order_release);
    }

    ALooper* registeredLooper = nullptr;
    int eventFd = -1;
    {
        std::lock_guard<std::mutex> lock(g_looperMutex);
        const auto iterator = g_dispatchersByThread.find(dispatcher->ownerTid);
        if (iterator != g_dispatchersByThread.end() && iterator->second == dispatcher)
        {
            g_dispatchersByThread.erase(iterator);
        }

        registeredLooper = dispatcher->registeredLooper;
        dispatcher->registeredLooper = nullptr;
        eventFd = dispatcher->eventFd.exchange(-1, std::memory_order_acq_rel);
        if (registeredLooper && eventFd >= 0)
            ALooper_removeFd(registeredLooper, eventFd);
    }

    if (eventFd >= 0)
        close(eventFd);
    if (registeredLooper)
        ALooper_release(registeredLooper);
    ReleaseDispatcher(dispatcher);
}

void jalium_dispatcher_wake(JaliumDispatcher* dispatcher)
{
    if (!dispatcher) return;

    std::lock_guard<std::mutex> lock(g_looperMutex);
    if (!dispatcher->destroyed.load(std::memory_order_acquire))
    {
        const int eventFd = dispatcher->eventFd.load(std::memory_order_acquire);
        if (eventFd >= 0)
        {
            const uint64_t value = 1;
            ssize_t written;
            do
            {
                written = write(eventFd, &value, sizeof(value));
            }
            while (written < 0 && errno == EINTR);
        }
    }
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
// High-Resolution Timer
// ============================================================================

// timerfd_settime atomically replaces an outstanding deadline. In particular,
// arming a parked one-second frame timer for 1 us makes a thread blocked in
// poll() observable immediately; a relative clock_nanosleep cannot be
// interrupted that way. A separate eventfd provides deterministic destruction
// without relying on cross-thread close() semantics or fd reuse.

JaliumResult jalium_timer_create(JaliumTimer** outTimer)
{
    if (!outTimer) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outTimer = nullptr;

    auto timer = new JaliumTimer();
    timer->timerFd = timerfd_create(
        CLOCK_MONOTONIC, TFD_NONBLOCK | TFD_CLOEXEC);
    if (timer->timerFd < 0)
    {
        delete timer;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    timer->destroyEventFd = eventfd(0, EFD_NONBLOCK | EFD_CLOEXEC);
    if (timer->destroyEventFd < 0)
    {
        close(timer->timerFd);
        delete timer;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    *outTimer = timer;
    return JALIUM_OK;
}

void jalium_timer_destroy(JaliumTimer* timer)
{
    if (!timer) return;

    int timerFd = -1;
    int destroyEventFd = -1;
    {
        std::unique_lock<std::mutex> lock(timer->stateMutex);
        if (timer->destroyed) return;

        timer->destroyed = true;
        timer->callback = nullptr;
        timer->userData = nullptr;

        // Keep the cancellation fd readable until every waiter has observed
        // it. This wakes all concurrent pollers and avoids a close-vs-poll
        // race where a recycled descriptor could be mistaken for this timer.
        const uint64_t value = 1;
        ssize_t written;
        do
        {
            written = write(timer->destroyEventFd, &value, sizeof(value));
        }
        while (written < 0 && errno == EINTR);

        timer->waitersDrained.wait(lock, [timer]
        {
            return timer->activeWaiters == 0;
        });

        timerFd = timer->timerFd;
        destroyEventFd = timer->destroyEventFd;
        timer->timerFd = -1;
        timer->destroyEventFd = -1;
    }

    if (timerFd >= 0) close(timerFd);
    if (destroyEventFd >= 0) close(destroyEventFd);
    delete timer;
}

void jalium_timer_arm(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer || intervalMicroseconds <= 0) return;

    std::lock_guard<std::mutex> lock(timer->stateMutex);
    if (timer->destroyed || timer->timerFd < 0) return;

    struct itimerspec specification{};
    specification.it_value.tv_sec = intervalMicroseconds / 1000000;
    specification.it_value.tv_nsec =
        (intervalMicroseconds % 1000000) * 1000;
    if (timerfd_settime(timer->timerFd, 0, &specification, nullptr) != 0)
        LOGE("jalium_timer_arm: timerfd_settime failed: %s", strerror(errno));
}

void jalium_timer_arm_repeating(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer || intervalMicroseconds <= 0) return;

    std::lock_guard<std::mutex> lock(timer->stateMutex);
    if (timer->destroyed || timer->timerFd < 0) return;

    struct itimerspec specification{};
    specification.it_value.tv_sec = intervalMicroseconds / 1000000;
    specification.it_value.tv_nsec =
        (intervalMicroseconds % 1000000) * 1000;
    specification.it_interval = specification.it_value;
    if (timerfd_settime(timer->timerFd, 0, &specification, nullptr) != 0)
    {
        LOGE("jalium_timer_arm_repeating: timerfd_settime failed: %s",
             strerror(errno));
    }
}

void jalium_timer_disarm(JaliumTimer* timer)
{
    if (!timer) return;

    std::lock_guard<std::mutex> lock(timer->stateMutex);
    if (timer->destroyed || timer->timerFd < 0) return;

    struct itimerspec specification{};
    if (timerfd_settime(timer->timerFd, 0, &specification, nullptr) != 0)
        LOGE("jalium_timer_disarm: timerfd_settime failed: %s", strerror(errno));
}

void jalium_timer_set_callback(JaliumTimer* timer, JaliumTimerCallback callback, void* userData)
{
    if (!timer) return;

    std::lock_guard<std::mutex> lock(timer->stateMutex);
    if (timer->destroyed) return;
    timer->callback = callback;
    timer->userData = userData;
}

int32_t jalium_timer_wait(JaliumTimer* timer, uint32_t timeoutMs)
{
    if (!timer) return 0;

    int timerFd = -1;
    int destroyEventFd = -1;
    {
        std::lock_guard<std::mutex> lock(timer->stateMutex);
        if (timer->destroyed || timer->timerFd < 0 ||
            timer->destroyEventFd < 0)
        {
            return 0;
        }

        ++timer->activeWaiters;
        timerFd = timer->timerFd;
        destroyEventFd = timer->destroyEventFd;
    }

    struct pollfd descriptors[2]{};
    descriptors[0].fd = timerFd;
    descriptors[0].events = POLLIN;
    descriptors[1].fd = destroyEventFd;
    descriptors[1].events = POLLIN;

    const int timeout = timeoutMs == 0
        ? -1
        : static_cast<int>(std::min<uint32_t>(timeoutMs, INT_MAX));
    int pollResult;
    do
    {
        pollResult = poll(descriptors, 2, timeout);
    }
    while (pollResult < 0 && errno == EINTR);

    int32_t fired = 0;
    if (pollResult > 0 && !(descriptors[1].revents & POLLIN) &&
        (descriptors[0].revents & POLLIN))
    {
        uint64_t expirations = 0;
        ssize_t bytesRead;
        do
        {
            bytesRead = read(timerFd, &expirations, sizeof(expirations));
        }
        while (bytesRead < 0 && errno == EINTR);
        fired = bytesRead == static_cast<ssize_t>(sizeof(expirations)) &&
                expirations != 0 ? 1 : 0;
    }

    {
        std::lock_guard<std::mutex> lock(timer->stateMutex);
        --timer->activeWaiters;
        if (timer->destroyed && timer->activeWaiters == 0)
            timer->waitersDrained.notify_all();
    }
    return fired;
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

JaliumResult jalium_input_get_touch_capabilities(
    int32_t* touchPresent, int32_t* maxContacts)
{
    if (!touchPresent || !maxContacts)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    *touchPresent = 0;
    *maxContacts = 0;
    return JALIUM_ERROR_NOT_SUPPORTED;
}

JaliumResult jalium_platform_set_double_click_settings(uint32_t, float)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

JaliumResult jalium_input_get_cursor_pos(float* x, float* y)
{
    // Android touch positions are view-local, not global screen coordinates.
    if (x) *x = 0.0f;
    if (y) *y = 0.0f;
    return JALIUM_ERROR_NOT_SUPPORTED;
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

// Android keeps the established text ClipData bridge above. Export the MIME
// transaction ABI as well so one managed interop surface can be used on every
// platform; richer Android ClipData item support can be layered on without an
// ABI revision.
JaliumResult jalium_clipboard_get_formats(char** outMimeTypes)
{
    if (!outMimeTypes) return JALIUM_ERROR_INVALID_ARGUMENT;
    auto* empty = static_cast<char*>(malloc(1));
    if (!empty) return JALIUM_ERROR_OUT_OF_MEMORY;
    empty[0] = '\0';
    *outMimeTypes = empty;
    return JALIUM_OK;
}

JaliumResult jalium_clipboard_get_data(
    const char* mimeType, uint8_t** outData, uint32_t* outDataSize)
{
    if (!mimeType || !outData || !outDataSize)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    *outData = nullptr;
    *outDataSize = 0;
    return JALIUM_ERROR_NOT_SUPPORTED;
}

JaliumResult jalium_clipboard_set_data(
    const JaliumClipboardDataItem* items, uint32_t itemCount)
{
    if (!items && itemCount != 0) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (itemCount == 0) return jalium_clipboard_clear();
    return JALIUM_ERROR_NOT_SUPPORTED;
}

JaliumResult jalium_clipboard_clear(void)
{
    static const JaliumUtf16Char empty[] = {0};
    return jalium_clipboard_set_text(empty);
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

JaliumResult jalium_drag_begin_ex(
    JaliumPlatformWindow* window, const JaliumDragDataItem* items,
    uint32_t itemCount, uint32_t allowedEffects,
    JaliumDragFeedbackCallback, JaliumDragQueryContinueCallback, void*,
    uint32_t* performedEffect)
{
    return jalium_drag_begin(
        window, items, itemCount, allowedEffects, performedEffect);
}

JaliumResult jalium_drag_begin_with_image(
    JaliumPlatformWindow* window, const JaliumDragDataItem* items,
    uint32_t itemCount, uint32_t allowedEffects,
    JaliumDragFeedbackCallback feedbackCallback,
    JaliumDragQueryContinueCallback queryContinueCallback, void* userData,
    const JaliumDragImage*, uint32_t* performedEffect)
{
    return jalium_drag_begin_ex(
        window, items, itemCount, allowedEffects,
        feedbackCallback, queryContinueCallback, userData, performedEffect);
}

int32_t jalium_window_set_enabled(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_opacity(JaliumPlatformWindow*, double)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_show_in_taskbar(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_resizable(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_decorated(JaliumPlatformWindow*, int32_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_set_owner(JaliumPlatformWindow*, intptr_t)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_activate(JaliumPlatformWindow*)
{
    return JALIUM_ERROR_NOT_SUPPORTED;
}

int32_t jalium_window_show_system_menu(
    JaliumPlatformWindow* window, int32_t, int32_t)
{
    return window ? JALIUM_ERROR_NOT_SUPPORTED : JALIUM_ERROR_INVALID_ARGUMENT;
}

int32_t jalium_window_update_ime_context(
    JaliumPlatformWindow* window, int32_t, const char*, int32_t, int32_t,
    int32_t, int32_t, int32_t, int32_t)
{
    return window ? JALIUM_ERROR_NOT_SUPPORTED : JALIUM_ERROR_INVALID_ARGUMENT;
}

#endif // __ANDROID__
