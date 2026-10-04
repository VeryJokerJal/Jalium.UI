namespace Jalium.UI;

public partial class Window
{
    private int _dropTargetRootCount;
    private bool _dropTargetRegistrationStarted;
    private bool _dropTargetWindowClosing;

    /// <summary>
    /// Applies the net change for one reconciled subtree. DragDropPlatform batches
    /// inherited-value changes so replacing one top-most root with another in the
    /// same Window does not transiently revoke and re-register the native target.
    /// </summary>
    internal void ApplyDropTargetRootDelta(int delta)
    {
        int newCount = _dropTargetRootCount + delta;
        System.Diagnostics.Debug.Assert(newCount >= 0, "AllowDrop root tracking became unbalanced.");
        _dropTargetRootCount = Math.Max(0, newCount);
        RefreshNativeDropTargetRegistration();
    }

    internal int DropTargetRootCountForTesting => _dropTargetRootCount;

    private void RefreshNativeDropTargetRegistration()
    {
        if (!OperatingSystem.IsWindows() || _dropTargetWindowClosing || Handle == nint.Zero)
        {
            return;
        }

        if (_dropTargetRootCount != 0)
        {
            // RegisterWindow is idempotent. Calling it for a touched Window also gives
            // a failed OleInitialize/RegisterDragDrop attempt a later retry point.
            if (OleDropTarget.RegisterWindow(this))
            {
                _dropTargetRegistrationStarted = true;
            }
        }
        else if (_dropTargetRegistrationStarted)
        {
            _dropTargetRegistrationStarted = OleDropTarget.RequestRevokeWindow(
                Handle,
                nativeWindowAlive: true);
        }
    }

    internal void OnNativeHandleReadyForDropTargets()
    {
        RefreshNativeDropTargetRegistration();
    }

    internal void CloseNativeDropTarget(nint hwnd, bool nativeWindowAlive)
    {
        _dropTargetWindowClosing = true;
        if (!_dropTargetRegistrationStarted)
        {
            return;
        }

        OleDropTarget.RevokeWindow(hwnd, nativeWindowAlive);
        _dropTargetRegistrationStarted = false;
    }
}
