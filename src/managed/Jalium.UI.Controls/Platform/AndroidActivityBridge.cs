using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls.Platform;

// Force-load each backend .so and explicitly register it.
// Calling these P/Invokes causes the dynamic linker to load the library,
// which also fires __attribute__((constructor)), but we call jalium_*_init
// explicitly as a belt-and-suspenders measure in case the constructor ran
// before jalium_register_backend_ex was ready in the core library.
internal static partial class BackendPreloader
{
    private const string SoftwareLib = "jalium.native.software";
    private const string VulkanLib   = "jalium.native.vulkan";

    [LibraryImport(SoftwareLib, EntryPoint = "jalium_software_init")]
    internal static partial void SoftwareInit();

    [LibraryImport(VulkanLib, EntryPoint = "jalium_vulkan_init")]
    internal static partial void VulkanInit();
}

/// <summary>
/// Bridges Android Activity lifecycle to Jalium.UI Application + Window model.
///
/// On Android, there is typically a single full-screen window backed by an
/// ANativeWindow. This bridge:
/// - Maps onNativeWindowCreated → Window resize + surface creation
/// - Maps onPause/onResume → suspend/resume rendering
/// - Maps onDestroy → Application.Shutdown
/// - Maps touch input → Jalium.UI pointer events
///
/// Usage: Call AndroidActivityBridge.Initialize() from the native activity's
/// onCreate callback before running the Jalium.UI Application.
/// </summary>
public static class AndroidActivityBridge
{
    private static bool s_initialized;
    private static nint s_nativeWindow;
    private static float s_density = 1.0f;
    private static int s_refreshRate = 60;

    /// <summary>
    /// Initializes the Android bridge. Should be called from native activity startup.
    /// </summary>
    public static void Initialize(float density = 1.0f, int refreshRate = 60)
    {
        if (s_initialized) return;

        s_density = density;
        s_refreshRate = refreshRate;

        // Pre-load rendering backend libraries before triggering the NativeMethods static
        // constructor. This ensures their __attribute__((constructor)) functions run first,
        // registering backends into jalium.native.core's registry before ContextCreate is called.
        PreloadNativeBackends();

        PlatformFactory.InitializePlatform();

        // Forward density and refresh rate to the native platform layer
        NativeMethods.AndroidSetDensity(density);
        NativeMethods.AndroidSetRefreshRate(refreshRate);

        s_initialized = true;
    }

    private static void PreloadNativeBackends()
    {
        // P/Invoke into each backend's explicit init function.
        // This forces the dynamic linker to load the .so and registers the backend
        // with jalium.native.core's registry before PlatformInit / ContextCreate is called.
        try
        {
            BackendPreloader.SoftwareInit();
            Console.Error.WriteLine("[PreloadNativeBackends] Software backend registered OK");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PreloadNativeBackends] software init failed: {ex.Message}");
        }

        try
        {
            BackendPreloader.VulkanInit();
            Console.Error.WriteLine("[PreloadNativeBackends] Vulkan backend registered OK");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PreloadNativeBackends] vulkan init failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the JNI environment for native code (needed for clipboard, etc.).
    /// Call from the Activity with JNIEnv and Activity handles.
    /// </summary>
    public static void SetJniEnv(nint javaVM, nint activity)
    {
        NativeMethods.AndroidSetJniEnv(javaVM, activity);
    }

    /// <summary>
    /// Called when the native window is created (onNativeWindowCreated).
    /// </summary>
    public static void OnNativeWindowCreated(nint nativeWindow)
    {
        s_nativeWindow = nativeWindow;
        NativeMethods.AndroidSetNativeWindow(nativeWindow);
        NativeWindowCreated?.Invoke(nativeWindow);
    }

    /// <summary>
    /// Called when the native window is destroyed (onNativeWindowDestroyed).
    /// </summary>
    public static void OnNativeWindowDestroyed()
    {
        s_nativeWindow = nint.Zero;
        NativeMethods.AndroidSetNativeWindow(nint.Zero);
        NativeWindowDestroyed?.Invoke();
    }

    /// <summary>Called when the activity is paused.</summary>
    public static void OnPause()
    {
        NativeMethods.AndroidOnPause();
        Paused?.Invoke();
    }

    /// <summary>Called when the activity is resumed.</summary>
    public static void OnResume()
    {
        NativeMethods.AndroidOnResume();
        Resumed?.Invoke();
    }

    /// <summary>Called when the activity is being destroyed.</summary>
    public static void OnDestroy()
    {
        NativeMethods.AndroidOnDestroy();
        Destroying?.Invoke();
    }

    /// <summary>
    /// Called when the display density changes (e.g. foldable device fold/unfold,
    /// display settings change). Updates the native platform layer which dispatches
    /// a DpiChanged event to the managed Window.
    /// </summary>
    public static void OnDensityChanged(float density)
    {
        s_density = density;
        NativeMethods.AndroidSetDensity(density);
        DensityChanged?.Invoke(density);
    }

    /// <summary>
    /// Called when window insets change (safe area / cutout / status bar / navigation bar).
    /// Insets are in physical pixels.
    /// </summary>
    public static void OnSafeAreaInsetsChanged(float top, float bottom, float left, float right)
    {
        NativeMethods.AndroidSetSafeAreaInsets(top, bottom, left, right);
    }

    /// <summary>
    /// Called when soft keyboard visibility or height changes.
    /// </summary>
    public static void OnKeyboardVisibilityChanged(bool visible, int heightPx)
    {
        NativeMethods.AndroidSetKeyboardVisible(visible ? 1 : 0, heightPx);
    }

    /// <summary>
    /// Called when device orientation changes.
    /// 0=portrait, 1=landscape, 2=portrait-reverse, 3=landscape-reverse.
    /// </summary>
    public static void OnOrientationChanged(int orientation)
    {
        NativeMethods.AndroidSetOrientation(orientation);
    }

    /// <summary>Called when the system is low on memory.</summary>
    public static void OnLowMemory()
    {
        NativeMethods.AndroidOnLowMemory();
        LowMemory?.Invoke();
    }

    /// <summary>Gets the current ANativeWindow handle.</summary>
    public static nint NativeWindow => s_nativeWindow;

    /// <summary>Gets the display density.</summary>
    public static float Density => s_density;

    /// <summary>Gets the display refresh rate.</summary>
    public static int RefreshRate => s_refreshRate;

    /// <summary>Gets whether running on Android.</summary>
    public static bool IsAndroid => PlatformFactory.IsAndroid;

    // ========================================================================
    // Input Injection (called from Activity touch/key event overrides)
    // ========================================================================

    /// <summary>
    /// Pointer type constants matching JALIUM_POINTER_* and Android MotionEvent tool types.
    /// </summary>
    public const int PointerTypeMouse = 0;
    public const int PointerTypeTouch = 1;
    public const int PointerTypePen = 2;

    /// <summary>
    /// Injects a touch/pointer event into the Jalium input pipeline.
    /// Called from Activity.DispatchTouchEvent().
    /// </summary>
    /// <param name="pointerId">The pointer/finger ID.</param>
    /// <param name="x">X coordinate in physical pixels.</param>
    /// <param name="y">Y coordinate in physical pixels.</param>
    /// <param name="pressure">Touch pressure (0.0-1.0).</param>
    /// <param name="action">0=DOWN, 1=UP, 2=MOVE, 3=CANCEL.</param>
    /// <param name="pointerType">PointerTypeTouch, PointerTypePen, or PointerTypeMouse.</param>
    /// <param name="modifiers">Modifier key flags.</param>
    public static void InjectTouch(int pointerId, float x, float y, float pressure,
        int action, int pointerType, int modifiers)
    {
        if (!s_initialized) return;
        // Marshal to the Jalium UI thread via Dispatcher to ensure thread safety.
        // Input events arrive on the Android main thread but the Jalium UI
        // tree must only be accessed from the JaliumUI thread.
        var dispatcher = Dispatcher.MainDispatcher;
        if (dispatcher != null)
        {
            dispatcher.BeginInvoke(() =>
                NativeMethods.AndroidInjectTouch(pointerId, x, y, pressure, action, pointerType, modifiers));
        }
        else
        {
            NativeMethods.AndroidInjectTouch(pointerId, x, y, pressure, action, pointerType, modifiers);
        }
    }

    /// <summary>
    /// Injects a key event into the Jalium input pipeline.
    /// Called from Activity.DispatchKeyEvent().
    /// </summary>
    /// <param name="androidKeyCode">Android KEYCODE_* value.</param>
    /// <param name="scanCode">Hardware scan code.</param>
    /// <param name="action">0=KEY_DOWN, 1=KEY_UP.</param>
    /// <param name="metaState">Android meta state flags.</param>
    /// <param name="repeatCount">Number of key repeats.</param>
    public static void InjectKey(int androidKeyCode, int scanCode,
        int action, int metaState, int repeatCount)
    {
        if (!s_initialized) return;
        var dispatcher = Dispatcher.MainDispatcher;
        if (dispatcher != null)
        {
            dispatcher.BeginInvoke(() =>
                NativeMethods.AndroidInjectKey(androidKeyCode, scanCode, action, metaState, repeatCount));
        }
        else
        {
            NativeMethods.AndroidInjectKey(androidKeyCode, scanCode, action, metaState, repeatCount);
        }
    }

    /// <summary>
    /// Injects a character input event (e.g., from IME or key press).
    /// </summary>
    /// <param name="codepoint">Unicode codepoint of the character.</param>
    public static void InjectChar(uint codepoint)
    {
        if (!s_initialized) return;
        var dispatcher = Dispatcher.MainDispatcher;
        if (dispatcher != null)
        {
            dispatcher.BeginInvoke(() =>
                NativeMethods.AndroidInjectChar(codepoint));
        }
        else
        {
            NativeMethods.AndroidInjectChar(codepoint);
        }
    }

    // Events
    public static event Action<nint>? NativeWindowCreated;
    public static event Action? NativeWindowDestroyed;
    public static event Action? Paused;
    public static event Action? Resumed;
    public static event Action? Destroying;
    public static event Action? LowMemory;
    public static event Action<float>? DensityChanged;
}
