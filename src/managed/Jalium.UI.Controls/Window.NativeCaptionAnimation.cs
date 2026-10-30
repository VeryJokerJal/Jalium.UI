using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI;

public partial class Window
{
    private bool _suppressNativeCaptionForLiveResize;
    private bool _nativeSizingCommand;

    private void BeginLiveResizeCaptionSuppression()
    {
        if (!_nativeSizingCommand || TitleBarStyle != WindowTitleBarStyle.Custom || WindowStyle == WindowStyle.None || _isFullScreen)
            return;

        // USER32 can expose a native-caption snapshot when a resize outruns
        // composition. Keep that snapshot out of the interactive transaction.
        _suppressNativeCaptionForLiveResize = true;
        UpdateNativeCaptionForSnapHover(false);
    }

    private void EndLiveResizeCaptionSuppression()
    {
        ClearLiveResizeEdge();
        _nativeSizingCommand = false;
        _suppressNativeCaptionForLiveResize = false;
        UpdateNativeCaptionForSnapHover(false);
    }

    private bool ShowWin32WindowState(int command)
    {
        if (command is SW_MAXIMIZE or SW_MINIMIZE or SW_RESTORE)
        {
            // End the Snap hover exception before starting a native transition.
            // Leave the caption in place for the whole DWM animation.
            EndLiveResizeCaptionSuppression();
        }

        return ShowWindow(Handle, command);
    }

    private void UpdateNativeCaptionForSnapHover(bool isOverMaximizeButton)
    {
        if (!OperatingSystem.IsWindows() || Handle == nint.Zero ||
            TitleBarStyle != WindowTitleBarStyle.Custom || WindowStyle == WindowStyle.None || _isFullScreen)
        {
            return;
        }

        // Keep the native caption for normal window behavior and system animations.
        // Snap hover needs the full custom HTMAXBUTTON bounds; interactive
        // resizing also suppresses the native-caption snapshot until mouse-up.
        bool snapHover = isOverMaximizeButton && ShouldUseWin11SnapNcRouting() && !HasOpenLightDismissPopup();
        long style = GetWindowLong(Handle, GWL_STYLE);
        long updatedStyle = snapHover || _suppressNativeCaptionForLiveResize
            ? style & ~(long)WS_CAPTION
            : style | WS_CAPTION;
        if (updatedStyle != style)
        {
            _ = SetWindowLong(Handle, GWL_STYLE, updatedStyle);
        }
    }
}
