using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls;

namespace Jalium.UI;

public partial class Window
{
    private bool _macOSTextMenuConsumedPointer;

    internal void MarkMacOSTextMenuPointerConsumed() => _macOSTextMenuConsumedPointer = true;

    private void CompleteMacOSTextMenuPointer()
    {
        if (!_macOSTextMenuConsumedPointer) return;
        _macOSTextMenuConsumedPointer = false;
        // The native tracking loop owns its terminating mouse-up. Complete
        // after mouse-to-pointer promotion so no stale pressed session survives.
        try { CompletePlatformDragInput(); }
        finally { _inputDispatcher.CleanupPointerSession(WindowInputDispatcher.MousePointerId); }
    }

    internal PlatformTextContextMenuRequest? CreateMacOSTextContextMenuRequest(UIElement target, Point position)
    {
        nint handle = Handle;
        int activationGeneration = _macOSActivationGeneration;
        bool IsCurrent() => Dispatcher.CheckAccess() && handle != 0 && Handle == handle &&
            activationGeneration == _macOSActivationGeneration &&
            !_isClosing && !_managedTeardownStarted && IsEnabled && IsVisible &&
            IsMacOSInputTargetValid(target) && ReferenceEquals(GetTextInputTarget(), target);
        if (!IsCurrent()) return null;
        return new(handle, target.TranslatePoint(position, this), IsCurrent,
            command => IsCurrent() && CanExecuteMacOSEditingCommand((int)command) && IsCurrent());
    }
}
