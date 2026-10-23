using System.Runtime.ExceptionServices;

namespace Jalium.UI.Controls.Primitives;

public partial class Popup
{
    private long _macOSInteractionRevision;
    internal long MacOSInteractionRevision => _macOSInteractionRevision;

    protected override void OnIsHitTestVisibleChanged(bool oldValue, bool newValue)
    {
        base.OnIsHitTestVisibleChanged(oldValue, newValue);
        SynchronizeMacOSInteractionPolicy();
    }

    private void OnMacOSStaysOpenChanged()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Exception? failure = null;
        RunPolicyStep(() => SetCurrentValue(IsLightDismissEnabledProperty, !StaysOpen), ref failure);
        RunPolicyStep(SynchronizeMacOSInteractionPolicy, ref failure);
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void SynchronizeMacOSInteractionPolicy()
    {
        if (!OperatingSystem.IsMacOS() || !IsOpen || _popupRoot is not { } root) return;
        long revision = unchecked(++_macOSInteractionRevision);
        var host = _macOSPopupWindow;
        bool hitTestVisible = IsHitTestVisible, staysOpen = StaysOpen;
        bool disablePointerInput = !hitTestVisible && (bool)root.GetValue(IsHitTestVisibleProperty)!;
        bool IsCurrent() => revision == _macOSInteractionRevision && IsOpen &&
            ReferenceEquals(root, _popupRoot) && ReferenceEquals(host, _macOSPopupWindow) &&
            hitTestVisible == IsHitTestVisible && staysOpen == StaysOpen;

        root.IsLightDismiss = !staysOpen;
        _overlayLayer?.RefreshPopupRootLightDismiss(root);
        Exception? failure = null;
        void SetRoot() => root.SetCurrentValue(IsHitTestVisibleProperty, hitTestVisible);
        void SetHost()
        {
            if (host == null) return;
            try { host.SetCurrentValue(IsHitTestVisibleProperty, hitTestVisible); }
            catch (Exception error)
            {
                failure ??= error;
                // A user property callback can throw before OnPropertyChanged
                // reaches the native style bridge. Complete that current update.
                if (IsCurrent()) RunPolicyStep(host.RefreshMacOSPopupStyle, ref failure);
            }
        }
        // Keep the content gated until an enabling native update has completed;
        // disable content before calling the native style setter in the reverse path.
        RunPolicyStep(hitTestVisible ? SetHost : SetRoot, ref failure);
        if (IsCurrent()) RunPolicyStep(hitTestVisible ? SetRoot : SetHost, ref failure);
        if (IsCurrent() && disablePointerInput)
            RunPolicyStep(() => ((Window?)host ?? _parentWindow)?.RetireMacOSPopupPointerInput(root, IsCurrent), ref failure);
        if (IsCurrent()) RunPolicyStep(QueueMacOSPopupOwnerCheck, ref failure);
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RunPolicyStep(Action step, ref Exception? failure)
    {
        try { step(); }
        catch (Exception error) { failure ??= error; }
    }
}
