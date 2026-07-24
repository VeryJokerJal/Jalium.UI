using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI;

/// <summary>
/// Reports the system soft keyboard height and window safe-area insets to
/// <see cref="AndroidActivityBridge"/>, driving the framework's existing content
/// avoidance (<c>Window.SoftKeyboardHeight</c> / <c>SafeAreaInsets</c>). The
/// render Surface is kept full-size on every API level so it is never destroyed
/// and recreated when the keyboard toggles.
/// </summary>
/// <remarks>
/// On Android 11+ (API 30) the IME inset is read directly. On older devices
/// (including the Android 9 images common in emulators) there is no IME inset
/// type, so a zero-width resizing <see cref="PopupWindow"/> measures the keyboard
/// out of band while the main window stays in <see cref="SoftInput.AdjustNothing"/>.
/// </remarks>
[SupportedOSPlatform("android24.0")]
internal sealed class JaliumKeyboardObserver : Java.Lang.Object, View.IOnApplyWindowInsetsListener
{
    private readonly Activity _activity;
    private readonly View _contentView;
    private JaliumKeyboardHeightProvider? _legacyProvider;
    private int _lastKeyboardPx = -1;
    private (int Top, int Bottom, int Left, int Right) _lastSafeArea = (-1, -1, -1, -1);
    private bool _attached;

    public JaliumKeyboardObserver(Activity activity, View contentView)
    {
        _activity = activity;
        _contentView = contentView;
    }

    public void Attach()
    {
        if (_attached)
            return;
        _attached = true;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            // SetDecorFitsSystemWindows is the supported edge-to-edge API on
            // 30-34 and merely soft-deprecated on 35+; it remains functional.
#pragma warning disable CA1422
            _activity.Window?.SetDecorFitsSystemWindows(false);
#pragma warning restore CA1422
            _contentView.SetOnApplyWindowInsetsListener(this);
            _contentView.RequestApplyInsets();
        }
        else
        {
            // Keep the render Surface full-size; measure the keyboard out of band.
            _activity.Window?.SetSoftInputMode(SoftInput.AdjustNothing);
            ReportLegacySafeArea();
            _legacyProvider = new JaliumKeyboardHeightProvider(_activity, _contentView, ReportKeyboard);
            _contentView.Post(() => _legacyProvider?.Start());
        }
    }

    public void Detach()
    {
        if (!_attached)
            return;
        _attached = false;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            _contentView.SetOnApplyWindowInsetsListener(null);
        }
        else
        {
            _legacyProvider?.Stop();
            _legacyProvider = null;
        }
    }

    public WindowInsets OnApplyWindowInsets(View? v, WindowInsets? insets)
    {
        if (insets != null && OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            Insets ime = insets.GetInsets(WindowInsets.Type.Ime());
            Insets bars = insets.GetInsets(WindowInsets.Type.SystemBars());
            Insets cutout = insets.GetInsets(WindowInsets.Type.DisplayCutout());

            ReportKeyboard(ime.Bottom);
            ReportSafeArea(
                Math.Max(bars.Top, cutout.Top),
                Math.Max(bars.Bottom, cutout.Bottom),
                Math.Max(bars.Left, cutout.Left),
                Math.Max(bars.Right, cutout.Right));
        }

        return insets!;
    }

    private void ReportKeyboard(int px)
    {
        if (px < 0)
            px = 0;
        if (px == _lastKeyboardPx)
            return;
        _lastKeyboardPx = px;
        AndroidActivityBridge.OnKeyboardVisibilityChanged(px > 0, px);
    }

    private void ReportSafeArea(int top, int bottom, int left, int right)
    {
        var next = (top, bottom, left, right);
        if (next == _lastSafeArea)
            return;
        _lastSafeArea = next;
        AndroidActivityBridge.OnSafeAreaInsetsChanged(top, bottom, left, right);
    }

    private void ReportLegacySafeArea()
    {
        WindowInsets? insets = _contentView.RootWindowInsets;
        if (insets != null)
        {
            // StableInset* are the correct API below API 30, which is the only
            // range that reaches this legacy path.
#pragma warning disable CA1422
            ReportSafeArea(
                insets.StableInsetTop,
                insets.StableInsetBottom,
                insets.StableInsetLeft,
                insets.StableInsetRight);
#pragma warning restore CA1422
        }
    }
}

/// <summary>
/// A zero-width resizing <see cref="PopupWindow"/> that measures the soft
/// keyboard height on API levels without an IME inset type. It never shows its
/// own keyboard; it only observes how its full-height content shrinks when the
/// real keyboard (from the Jalium text-input view) appears.
/// </summary>
[SupportedOSPlatform("android24.0")]
internal sealed class JaliumKeyboardHeightProvider : PopupWindow
{
    private readonly View _popupView;
    private readonly View _anchor;
    private readonly Action<int> _onHeight;

    public JaliumKeyboardHeightProvider(Context context, View anchor, Action<int> onHeight)
        : base(context)
    {
        _anchor = anchor;
        _onHeight = onHeight;

        _popupView = new View(context);
        ContentView = _popupView;
        SetBackgroundDrawable(new ColorDrawable(Color.Transparent));

        Width = 0;
        Height = ViewGroup.LayoutParams.MatchParent;
        SoftInputMode = SoftInput.AdjustResize | SoftInput.StateAlwaysHidden;
        InputMethodMode = Android.Widget.InputMethod.Needed;

        if (_popupView.ViewTreeObserver != null)
            _popupView.ViewTreeObserver.GlobalLayout += OnGlobalLayout;
    }

    public void Start()
    {
        if (!IsShowing && _anchor.WindowToken != null)
            ShowAtLocation(_anchor, GravityFlags.NoGravity, 0, 0);
    }

    public void Stop()
    {
        if (_popupView.ViewTreeObserver != null)
            _popupView.ViewTreeObserver.GlobalLayout -= OnGlobalLayout;
        Dismiss();
    }

    private void OnGlobalLayout(object? sender, EventArgs e)
    {
        var frame = new Android.Graphics.Rect();
        _popupView.GetWindowVisibleDisplayFrame(frame);

        int keyboardHeight = GetUsableScreenHeight() - frame.Bottom;
        _onHeight(keyboardHeight < 0 ? 0 : keyboardHeight);
    }

    private int GetUsableScreenHeight()
    {
        // Display.GetSize excludes the navigation bar and shares the coordinate
        // space of GetWindowVisibleDisplayFrame, so a hidden keyboard yields ~0
        // rather than a false navigation-bar-sized "keyboard" height. (This
        // provider only runs below API 30, where GetSize is the right API.)
#pragma warning disable CA1422
        if (_anchor.Context?.GetSystemService(Context.WindowService) is IWindowManager wm &&
            wm.DefaultDisplay is { } display)
        {
            var size = new Android.Graphics.Point();
            display.GetSize(size);
            return size.Y;
        }
#pragma warning restore CA1422

        return (_anchor.RootView ?? _anchor).Height;
    }
}
