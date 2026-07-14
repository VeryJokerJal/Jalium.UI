using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Android.App;
using Android.OS;
using Android.Views;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Threading;

namespace Jalium.UI;

/// <summary>
/// Base <see cref="Activity"/> that bootstraps the Jalium.UI framework on Android.
/// Handles SurfaceView creation, native window lifecycle, touch/key input forwarding,
/// and Android lifecycle events. Subclasses override <see cref="CreateHostedApp"/>
/// to build the Jalium.UI <see cref="JaliumApp"/> (host + application) with views,
/// services, styles, and business logic.
/// </summary>
[SupportedOSPlatform("android24.0")]
public abstract class JaliumActivity : Activity, ISurfaceHolderCallback
{
    private SurfaceView? _surfaceView;
    private long _activityGeneration;

    private static readonly object s_appThreadGate = new();
    private static Thread? s_jaliumThread;
    private static WeakReference<JaliumActivity>? s_pendingActivity;

    /// <summary>
    /// Builds the <see cref="JaliumApp"/> that drives this activity. Typically this
    /// invokes <see cref="AppBuilder.CreateBuilder()"/>, registers services, calls
    /// <see cref="AppBuilder.Build"/>, then runs post-Build <c>app.UseApplication&lt;T&gt;()</c>
    /// / <c>app.UseDevTools()</c> / etc. Called on the dedicated Jalium UI thread,
    /// not the Android main thread.
    /// </summary>
    protected abstract JaliumApp CreateHostedApp();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _activityGeneration = AndroidActivityBridge.RegisterActivity();

        var metrics = Resources!.DisplayMetrics!;
        float density = metrics.Density;
        int refreshRate = 60;
        if (WindowManager?.DefaultDisplay != null)
            refreshRate = (int)WindowManager.DefaultDisplay.RefreshRate;

        AndroidActivityBridge.Initialize(density, refreshRate);

        try
        {
            Java.Interop.JniEnvironment.References.GetJavaVM(out nint javaVm);
            if (javaVm != nint.Zero)
                AndroidActivityBridge.SetJniEnv(javaVm, Handle);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("JaliumUI", $"Unable to initialize JNI bridge: {ex.Message}");
        }

        _surfaceView = new SurfaceView(this);
        _surfaceView.Holder!.SetFormat(Android.Graphics.Format.Rgba8888);
        _surfaceView.Holder!.AddCallback(this);
        SetContentView(_surfaceView);
    }

    public void SurfaceCreated(ISurfaceHolder holder)
    {
    }

    public void SurfaceChanged(ISurfaceHolder holder, Android.Graphics.Format format, int width, int height)
    {
        nint surface = holder.Surface!.Handle;
        nint nativeWindow = nint.Zero;
        try
        {
            nativeWindow = ANativeWindow_fromSurface(Android.Runtime.JNIEnv.Handle, surface);
        }
        catch
        {
            nativeWindow = nint.Zero;
        }

        bool accepted = false;
        try
        {
            // Pass the authoritative post-rotation surface dimensions through.
            // The bridge synchronously serializes replacement/resize onto the
            // Jalium UI thread once its dispatcher is ready.
            accepted = AndroidActivityBridge.OnNativeWindowCreated(
                nativeWindow, width, height, _activityGeneration);
        }
        catch (Exception ex)
        {
            // Never let a lifecycle exception escape Android's main Looper.
            // The bridge has already put the window into a detached-safe state
            // when an attach operation fails.
            Android.Util.Log.Error("JaliumUI", $"SurfaceChanged failed: {ex}");
        }
        finally
        {
            // ANativeWindow_fromSurface() acquired this temporary reference.
            // The native platform bridge acquires its own reference before the
            // synchronous call returns, so the Activity must always release its
            // copy (including stale-generation and failure paths).
            if (nativeWindow != nint.Zero)
            {
                try { ANativeWindow_release(nativeWindow); }
                catch (Exception ex) { Android.Util.Log.Error("JaliumUI", $"ANativeWindow_release failed: {ex}"); }
            }
        }

        if (accepted)
            EnsureJaliumThreadStarted();
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        // This call is deliberately synchronous.  It does not return until the
        // Jalium UI thread has stopped touching the Surface and disposed its RT.
        try
        {
            _ = AndroidActivityBridge.OnNativeWindowDestroyed(_activityGeneration);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("JaliumUI", $"SurfaceDestroyed failed: {ex}");
        }
    }

    private void EnsureJaliumThreadStarted()
    {
        lock (s_appThreadGate)
        {
            s_pendingActivity = new WeakReference<JaliumActivity>(this);
            if (s_jaliumThread?.IsAlive == true)
                return;

            if (AndroidActivityBridge.TryReserveUiThreadStart(_activityGeneration))
                _ = StartJaliumThreadLocked(this);
        }
    }

    private static bool StartJaliumThreadLocked(JaliumActivity activity)
    {
        var thread = new Thread(activity.RunJaliumApp)
        {
            Name = "JaliumUI",
            IsBackground = true
        };
        s_jaliumThread = thread;
        try
        {
            thread.Start();
            return true;
        }
        catch (Exception ex)
        {
            s_jaliumThread = null;
            AndroidActivityBridge.CancelUiThreadStart(activity._activityGeneration);
            Android.Util.Log.Error("JaliumUI", $"Unable to start JaliumUI thread: {ex}");
            return false;
        }
    }

    private void RunJaliumApp()
    {
        Dispatcher? dispatcher = null;
        JaliumApp? app = null;
        ExitEventHandler? exitHandler = null;
        bool bridgeReady = false;
        try
        {
            // Activity callbacks must target this dedicated UI dispatcher, not
            // Android's main thread (and not a stale dispatcher from a prior run).
            Dispatcher.SetAsMainThread();
            dispatcher = Dispatcher.CurrentDispatcher;
            if (!AndroidActivityBridge.MarkUiThreadReady(_activityGeneration))
            {
                Android.Util.Log.Warn(
                    "JaliumUI",
                    $"UI thread start rejected for stale or unavailable Activity generation {_activityGeneration}.");
                return;
            }
            bridgeReady = true;
            app = CreateHostedApp();
            exitHandler = static (_, _) =>
                AndroidActivityBridge.MarkUiThreadStopping(terminalOnFailure: true);
            app.Application.Exit += exitHandler;
            app.Run();
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("JaliumUI", $"JaliumApp.Run FATAL: {ex}");
        }
        finally
        {
            // Exit normally marks Stopping before Application.Cleanup. This is
            // also a direct same-thread fallback for unexpected loop exits.
            bool stoppedSafely;
            if (bridgeReady)
            {
                stoppedSafely = AndroidActivityBridge.MarkUiThreadStopping(terminalOnFailure: true);
                if (!stoppedSafely)
                {
                    // Publish Failed before host/application disposal. Those can
                    // block, while Android's main thread may already be delivering
                    // another synchronous Surface callback.
                    AndroidActivityBridge.MarkUiThreadStopFailed();
                }
            }
            else
            {
                AndroidActivityBridge.CancelUiThreadStart(_activityGeneration);
                stoppedSafely = true;
            }

            if (app != null && exitHandler != null)
            {
                try { app.Application.Exit -= exitHandler; }
                catch { /* application cleanup may already have detached state */ }
            }

            try { app?.Dispose(); }
            catch (Exception ex) { Android.Util.Log.Error("JaliumUI", $"JaliumApp.Dispose failed: {ex}"); }

            if (bridgeReady && stoppedSafely)
            {
                AndroidActivityBridge.MarkUiThreadStopped();
            }
            else if (bridgeReady)
            {
                Android.Util.Log.Error(
                    "JaliumUI",
                    "UI loop exited before Android Surface/Window teardown completed; replacement thread suppressed.");
            }

            if (dispatcher != null)
            {
                try { dispatcher.ClearAsMainThread(); }
                catch (Exception ex) { Android.Util.Log.Error("JaliumUI", $"Dispatcher main-slot clear failed: {ex}"); }
                try { dispatcher.DisposeCore(); }
                catch (Exception ex) { Android.Util.Log.Error("JaliumUI", $"Dispatcher dispose failed: {ex}"); }
            }

            lock (s_appThreadGate)
            {
                if (ReferenceEquals(s_jaliumThread, Thread.CurrentThread))
                    s_jaliumThread = null;

                // If a new Activity appeared while the previous application was
                // already shutting down, make sure its one SurfaceChanged callback
                // is sufficient to start a replacement Jalium thread.
                if (stoppedSafely &&
                    s_pendingActivity?.TryGetTarget(out var pending) == true &&
                    AndroidActivityBridge.IsCurrentActivity(pending._activityGeneration) &&
                    AndroidActivityBridge.TryReserveUiThreadStart(pending._activityGeneration))
                {
                    _ = StartJaliumThreadLocked(pending);
                }
                else if (stoppedSafely)
                {
                    s_pendingActivity = null;
                }
            }
        }
    }

    #region Input Forwarding

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e != null)
            ForwardTouchEvent(e);
        return true;
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e != null)
            ForwardKeyEvent(e);
        return true;
    }

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (e != null)
            ForwardKeyEvent(e);
        return true;
    }

    public override bool OnKeyUp(Keycode keyCode, KeyEvent? e)
    {
        if (e != null)
            ForwardKeyEvent(e);
        return true;
    }

    public override bool DispatchGenericMotionEvent(MotionEvent? e)
    {
        if (e != null)
        {
            var source = e.Source;
            if ((source & InputSourceType.Mouse) == InputSourceType.Mouse ||
                (source & InputSourceType.Touchpad) == InputSourceType.Touchpad)
            {
                int actionMasked = (int)e.ActionMasked;
                if (actionMasked == (int)MotionEventActions.HoverMove)
                {
                    AndroidActivityBridge.InjectTouch(
                        0, e.GetX(0), e.GetY(0), 0f,
                        2, AndroidActivityBridge.PointerTypeMouse, GetModifiers(e));
                    return true;
                }
                else if (actionMasked == (int)MotionEventActions.Scroll)
                {
                    return true;
                }
            }
        }

        return base.DispatchGenericMotionEvent(e);
    }

    private static void ForwardTouchEvent(MotionEvent e)
    {
        int actionMasked = (int)e.ActionMasked;
        int pointerIndex = e.ActionIndex;

        int bridgeAction = actionMasked switch
        {
            (int)MotionEventActions.Down or
            (int)MotionEventActions.PointerDown => 0,
            (int)MotionEventActions.Up or
            (int)MotionEventActions.PointerUp => 1,
            (int)MotionEventActions.Move => 2,
            (int)MotionEventActions.Cancel => 3,
            _ => -1
        };

        if (bridgeAction < 0)
            return;

        int modifiers = GetModifiers(e);

        if (bridgeAction == 2 || bridgeAction == 3)
        {
            for (int i = 0; i < e.PointerCount; i++)
            {
                AndroidActivityBridge.InjectTouch(
                    e.GetPointerId(i),
                    e.GetX(i), e.GetY(i),
                    e.GetPressure(i),
                    bridgeAction, MapToolType(e.GetToolType(i)), modifiers);
            }
        }
        else
        {
            AndroidActivityBridge.InjectTouch(
                e.GetPointerId(pointerIndex),
                e.GetX(pointerIndex), e.GetY(pointerIndex),
                e.GetPressure(pointerIndex),
                bridgeAction, MapToolType(e.GetToolType(pointerIndex)), modifiers);
        }
    }

    private static void ForwardKeyEvent(KeyEvent e)
    {
        int action = e.Action == KeyEventActions.Down ? 0 : 1;
        AndroidActivityBridge.InjectKey((int)e.KeyCode, e.ScanCode, action, (int)e.MetaState, e.RepeatCount);

        if (e.Action == KeyEventActions.Down)
        {
            int unicodeChar = e.GetUnicodeChar((MetaKeyStates)e.MetaState);
            if (unicodeChar > 0)
                AndroidActivityBridge.InjectChar((uint)unicodeChar);
        }
    }

    private static int GetModifiers(MotionEvent e)
    {
        int metaState = (int)e.MetaState;
        int modifiers = 0;
        if ((metaState & (int)MetaKeyStates.ShiftOn) != 0) modifiers |= 0x01;
        if ((metaState & (int)MetaKeyStates.CtrlOn) != 0) modifiers |= 0x02;
        if ((metaState & (int)MetaKeyStates.AltOn) != 0) modifiers |= 0x04;
        if ((metaState & (int)MetaKeyStates.MetaOn) != 0) modifiers |= 0x08;
        return modifiers;
    }

    private static int MapToolType(MotionEventToolType toolType)
    {
        return toolType switch
        {
            MotionEventToolType.Finger => AndroidActivityBridge.PointerTypeTouch,
            MotionEventToolType.Stylus or
            MotionEventToolType.Eraser => AndroidActivityBridge.PointerTypePen,
            MotionEventToolType.Mouse => AndroidActivityBridge.PointerTypeMouse,
            _ => AndroidActivityBridge.PointerTypeTouch
        };
    }

    #endregion

    #region Lifecycle

    protected override void OnPause()
    {
        base.OnPause();
        AndroidActivityBridge.OnPause(_activityGeneration);
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidActivityBridge.OnResume(_activityGeneration);
    }

    protected override void OnDestroy()
    {
        // Configuration replacement keeps the process-level Jalium application
        // alive.  A late destroy from the old Activity generation must not shut
        // down a newer Activity or detach its Surface.
        if (!IsChangingConfigurations)
            AndroidActivityBridge.OnDestroy(_activityGeneration);
        base.OnDestroy();
    }

    public override void OnLowMemory()
    {
        base.OnLowMemory();
        AndroidActivityBridge.OnLowMemory(_activityGeneration);
    }

    #endregion

    [DllImport("android")]
    private static extern nint ANativeWindow_fromSurface(nint jniEnv, nint surface);

    [DllImport("android")]
    private static extern void ANativeWindow_release(nint nativeWindow);
}
