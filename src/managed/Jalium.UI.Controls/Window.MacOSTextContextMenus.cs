using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI;

public partial class Window
{
    private bool _macOSTextMenuConsumedPointer;

    internal void MarkMacOSTextMenuPointerConsumed() => _macOSTextMenuConsumedPointer = true;

    private void CompleteMacOSTextMenuPointer(MouseButton consumedButton, long precedingPressEpoch, long precedingWindowPressEpoch)
    {
        if (!_macOSTextMenuConsumedPointer) return;
        _macOSTextMenuConsumedPointer = false;
        CompleteMacOSTextMenuPointerCore(consumedButton, unchecked(precedingPressEpoch + 1),
            unchecked(precedingWindowPressEpoch + 1));
    }

    private void CompleteMacOSTextMenuPointerCore(MouseButton consumedButton, long epoch, long windowEpoch)
    {
        bool IsCurrent() => Mouse.PressEpoch == epoch;
        if (!IsCurrent())
        {
            if (consumedButton == MouseButton.Left && _inputDispatcher.MousePressEpoch == windowEpoch)
                _inputDispatcher.ClearMousePressedChain();
            return;
        }
        var buttons = GetPlatformMouseButtons(default).WithButton(consumedButton, MouseButtonState.Released);
        Mouse.UpdateState(Mouse.Position, UIElement.MouseDirectlyOverElement, buttons);
        if (consumedButton == MouseButton.Left) _inputDispatcher.ClearMousePressedChain();
        if (!IsCurrent()) return;
        buttons = GetPlatformMouseButtons(default);
        // Tracking consumes only its initiating button. Keep another button's
        // capture and shared pointer session until that button is released.
        if (buttons.Left == MouseButtonState.Pressed || buttons.Right == MouseButtonState.Pressed ||
            buttons.Middle == MouseButtonState.Pressed || buttons.XButton1 == MouseButtonState.Pressed ||
            buttons.XButton2 == MouseButtonState.Pressed) return;
        // The native tracking loop owns its terminating mouse-up. Complete
        // after mouse-to-pointer promotion so no stale pressed session survives.
        try { CompletePlatformDragInputCore(IsCurrent); }
        finally { if (IsCurrent()) _inputDispatcher.CleanupPointerSession(WindowInputDispatcher.MousePointerId); }
    }

    internal PlatformTextContextMenuRequest? CreateMacOSTextContextMenuRequest(UIElement target, Point position)
    {
        nint handle = Handle;
        int activationGeneration = _macOSActivationGeneration;
        int nativeActivationGeneration = _macOSNativeActivationGeneration;
        bool IsCurrent() => Dispatcher.CheckAccess() && handle != 0 && Handle == handle &&
            activationGeneration == _macOSActivationGeneration &&
            nativeActivationGeneration == _macOSNativeActivationGeneration &&
            !_isClosing && !_managedTeardownStarted && IsEnabled && IsVisible &&
            IsMacOSInputTargetValid(target) && ReferenceEquals(GetTextInputTarget(), target);
        if (!IsCurrent()) return null;
        return new(handle, target.TranslatePoint(position, this), IsCurrent,
            command => IsCurrent() && CanExecuteMacOSEditingCommand((int)command) && IsCurrent());
    }
}
