using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Android.App;
using Android.OS;
using Android.Views;
using Jalium.UI.Controls.Platform;

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
    private Thread? _jaliumThread;
    private static volatile bool s_appStarted;

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

        var metrics = Resources!.DisplayMetrics!;
        float density = metrics.Density;
        int refreshRate = 60;
        if (WindowManager?.DefaultDisplay != null)
            refreshRate = (int)WindowManager.DefaultDisplay.RefreshRate;

        AndroidActivityBridge.Initialize(density, refreshRate);

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
        nint nativeWindow;
        try
        {
            nativeWindow = ANativeWindow_fromSurface(Android.Runtime.JNIEnv.Handle, surface);
        }
        catch
        {
            nativeWindow = nint.Zero;
        }

        AndroidActivityBridge.OnNativeWindowCreated(nativeWindow);

        if (!s_appStarted)
        {
            s_appStarted = true;
            _jaliumThread = new Thread(RunJaliumApp)
            {
                Name = "JaliumUI",
                IsBackground = true
            };
            _jaliumThread.Start();
        }
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        AndroidActivityBridge.OnNativeWindowDestroyed();
    }

    private void RunJaliumApp()
    {
        try
        {
            using var app = CreateHostedApp();
            app.Run();
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("JaliumUI", $"JaliumApp.Run FATAL: {ex}");
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
        AndroidActivityBridge.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidActivityBridge.OnResume();
    }

    protected override void OnDestroy()
    {
        AndroidActivityBridge.OnDestroy();
        base.OnDestroy();
    }

    public override void OnLowMemory()
    {
        base.OnLowMemory();
        AndroidActivityBridge.OnLowMemory();
    }

    #endregion

    [DllImport("android")]
    private static extern nint ANativeWindow_fromSurface(nint jniEnv, nint surface);
}
