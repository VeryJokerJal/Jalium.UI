using Jalium.UI.Threading;

namespace Jalium.UI;

public partial class Window
{
    private bool _macOSNativeIsKey;
    private static WeakReference<Window>? _macOSNativeKeyWindow;
    private static bool HasMacOSNativeKeyWindow => _macOSNativeKeyWindow != null &&
        _macOSNativeKeyWindow.TryGetTarget(out var window) && window._macOSNativeIsKey &&
        !window._isClosing && !window._managedTeardownStarted;
    private int _macOSNativeActivationGeneration;
    private object? _macOSPopupDeactivationToken;

    private void HandleMacOSPlatformActivation(bool active)
    {
        int nativeGeneration = unchecked(++_macOSNativeActivationGeneration);
        _macOSNativeIsKey = active;
        bool IsCurrent() => nativeGeneration == _macOSNativeActivationGeneration &&
            _macOSNativeIsKey == active && !_isClosing && !_managedTeardownStarted && IsMacOSPopupHostCurrent &&
            (!active || _macOSNativeKeyWindow?.TryGetTarget(out var currentKey) == true && ReferenceEquals(currentKey, this));
        if (active) _macOSNativeKeyWindow = new(this);
        else if (_macOSNativeKeyWindow?.TryGetTarget(out var keyWindow) == true && ReferenceEquals(keyWindow, this))
            _macOSNativeKeyWindow = null;
        var owner = MacOSInputOwner;
        if (IsMacOSPopupHost)
        {
            if (active)
            {
                owner._macOSPopupDeactivationToken = null;
                if (!IsCurrent()) return;
                SetIsActive(true);
                if (!IsCurrent()) return;
                RestoreMacOSKeyboardFocus();
                if (!IsCurrent()) return;
                if (!owner.IsActive) owner.HandleMacOSActivationChanged(active: true);
                else Application.Current?.SetPlatformActivationState(true);
                if (IsCurrent()) UpdateInputMethodAssociation();
            }
            else
            {
                var complete = _inputDispatcher.PrepareMacOSWindowDeactivation();
                try { complete(); }
                finally { if (IsCurrent()) SetIsActive(false); }
                if (!IsCurrent()) return;
                UpdateInputMethodAssociation();
                owner.QueueMacOSPopupGroupDeactivation();
            }
            return;
        }
        if (!active && _macOSPopupHosts.Count != 0)
        {
            var complete = _inputDispatcher.PrepareMacOSWindowDeactivation();
            complete();
            if (!IsCurrent()) return;
            UpdateInputMethodAssociation();
            QueueMacOSPopupGroupDeactivation();
            return;
        }
        _macOSPopupDeactivationToken = null;
        HandleMacOSActivationChanged(active);
    }

    private void QueueMacOSPopupGroupDeactivation()
    {
        if (_isClosing || _managedTeardownStarted || Dispatcher.HasShutdownStarted) return;
        var token = _macOSPopupDeactivationToken = new object();
        var operation = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!ReferenceEquals(token, _macOSPopupDeactivationToken)) return;
            _macOSPopupDeactivationToken = null;
            if (_isClosing || _managedTeardownStarted || _macOSNativeIsKey ||
                _macOSPopupHosts.Any(h => h._macOSNativeIsKey && h.IsMacOSPopupHostCurrent)) return;
            HandleMacOSActivationChanged(active: false);
        });
        operation.Aborted += (_, _) =>
        {
            if (ReferenceEquals(token, _macOSPopupDeactivationToken)) _macOSPopupDeactivationToken = null;
        };
    }

    internal void CloseMacOSPopupGroupOnDeactivate(Func<bool> isCurrent)
    {
        foreach (var popup in GetMacOSMousePopupGroup().Where(p => !p.StaysOpen))
        {
            if (!isCurrent()) break;
            popup.SetCurrentValue(Controls.Primitives.Popup.IsOpenProperty, false);
        }
    }
}
