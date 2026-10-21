using Jalium.UI.Input;

namespace Jalium.UI.Controls;

internal sealed partial class WindowInputDispatcher
{
    public void HandleMouseDown(MouseButton button, Point position, MouseButtonStates buttons,
        ModifierKeys modifiers, int clickCount, int timestamp)
    {
        Mouse.NotifyPress();
        long press = unchecked(++_mousePressEpoch), globalPress = Mouse.PressEpoch;
        bool nativeMouse = OperatingSystem.IsMacOS() && !SuppressMouseToPointerPromotion;
        _suppressedMousePointerReleases.Remove(button);
        // A nested Up may arrive before this packet ever reaches PointerDown.
        if (nativeMouse) _suppressedMousePointerReleases.Add(button);
        bool completed = false; Exception? failure = null;
        try { completed = HandleMouseDownCore(button, position, buttons, modifiers, clickCount, timestamp); }
        catch (Exception error) { failure = error; }
        if (nativeMouse && !completed && _mousePressEpoch == press)
        {
            void Notify(Action action)
            {
                try { action(); } catch (Exception error) { failure ??= error; }
            }
            bool SamePress() => _mousePressEpoch == press && Mouse.PressEpoch == globalPress;
            // Detach old ownership before notifications can install a successor.
            _suppressedMousePointerReleases.Add(button);
            if (buttons.Left == MouseButtonState.Pressed) _suppressedMousePointerReleases.Add(MouseButton.Left);
            if (buttons.Middle == MouseButtonState.Pressed) _suppressedMousePointerReleases.Add(MouseButton.Middle);
            if (buttons.Right == MouseButtonState.Pressed) _suppressedMousePointerReleases.Add(MouseButton.Right);
            if (buttons.XButton1 == MouseButtonState.Pressed) _suppressedMousePointerReleases.Add(MouseButton.XButton1);
            if (buttons.XButton2 == MouseButtonState.Pressed) _suppressedMousePointerReleases.Add(MouseButton.XButton2);
            Action? cancel = null;
            Notify(() => cancel = PrepareNativeMouseDragCompletion(inputLifetime: SamePress));
            var pressed = DetachPressedChain(_mousePressedChain);
            ClearDetachedPressedChain(pressed, _mousePressedChain, _keyboardPressedChain, Notify);
            if (cancel is not null) Notify(cancel);
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
