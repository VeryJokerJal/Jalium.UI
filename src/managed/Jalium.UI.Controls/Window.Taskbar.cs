using Jalium.UI.Controls.Platform;

namespace Jalium.UI;

public partial class Window
{
    private IPlatformTaskbarIntegration? _platformTaskbarIntegration;
    private bool _attachingTaskbarIntegration, _refreshTaskbarAfterAttach;

    internal void AttachPlatformTaskbarIntegration()
    {
        if (_attachingTaskbarIntegration) { _refreshTaskbarAfterAttach = true; return; }
        DetachPlatformTaskbarIntegration();
        if (TaskbarItemInfo == null || _platformWindow == null || IsMacOSPopupHost) return;
        var platform = _platformWindow;
        nint handle = Handle;
        _attachingTaskbarIntegration = true;
        try
        {
            var integration = PlatformTaskbarIntegration.Attach?.Invoke(this);
            if (ReferenceEquals(platform, _platformWindow) && handle == Handle &&
                !IsCloseRequestedForPlatformTermination && TaskbarItemInfo != null)
                _platformTaskbarIntegration = integration;
            else integration?.Dispose();
        }
        finally { _attachingTaskbarIntegration = false; }
        if (_refreshTaskbarAfterAttach)
        {
            _refreshTaskbarAfterAttach = false;
            _platformTaskbarIntegration?.Refresh();
        }
    }

    internal void DetachPlatformTaskbarIntegration()
    {
        var integration = _platformTaskbarIntegration;
        _platformTaskbarIntegration = null;
        integration?.Dispose();
    }

    private static void OnTaskbarItemInfoChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var window = (Window)sender;
        if (window._attachingTaskbarIntegration)
        {
            window._refreshTaskbarAfterAttach = true;
            return;
        }
        if (e.NewValue == null) window.DetachPlatformTaskbarIntegration();
        else if (window._platformTaskbarIntegration != null) window._platformTaskbarIntegration.Refresh();
        else if (window.Handle != 0 && !window.IsCloseRequestedForPlatformTermination)
            window.AttachPlatformTaskbarIntegration();
    }
}
