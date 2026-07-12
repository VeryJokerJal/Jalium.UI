using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection(nameof(WpfParityFoundationBehaviorCollection))]
public sealed class InputStaticSurfaceWpfParityTests
{
    [Fact]
    public void KeyUsesWpfLogicalValuesAliasesAndVirtualKeyTranslation()
    {
        Assert.Equal(0, (int)Key.None);
        Assert.Equal(1, (int)Key.Cancel);
        Assert.Equal(2, (int)Key.Back);
        Assert.Equal(6, (int)Key.Return);
        Assert.Equal(Key.Return, Key.Enter);
        Assert.Equal(8, (int)Key.Capital);
        Assert.Equal(Key.Capital, Key.CapsLock);
        Assert.Equal(Key.KanaMode, Key.HangulMode);
        Assert.Equal(Key.HanjaMode, Key.KanjiMode);
        Assert.Equal(34, (int)Key.D0);
        Assert.Equal(44, (int)Key.A);
        Assert.Equal(69, (int)Key.Z);
        Assert.Equal(70, (int)Key.LWin);
        Assert.Equal(90, (int)Key.F1);
        Assert.Equal(113, (int)Key.F24);
        Assert.Equal(140, (int)Key.Oem1);
        Assert.Equal(Key.Oem1, Key.OemSemicolon);
        Assert.Equal(Key.Oem102, Key.OemBackslash);
        Assert.Equal(Key.OemAttn, Key.DbeAlphanumeric);
        Assert.Equal(Key.Pa1, Key.DbeEnterDialogConversionMode);
        Assert.Equal(172, (int)Key.DeadCharProcessed);

        Assert.Equal(0x08, KeyInterop.VirtualKeyFromKey(Key.Back));
        Assert.Equal(0x0D, KeyInterop.VirtualKeyFromKey(Key.Enter));
        Assert.Equal(0x41, KeyInterop.VirtualKeyFromKey(Key.A));
        Assert.Equal(0x7B, KeyInterop.VirtualKeyFromKey(Key.F12));
        Assert.Equal(0xA3, KeyInterop.VirtualKeyFromKey(Key.RightCtrl));
        Assert.Equal(0xE2, KeyInterop.VirtualKeyFromKey(Key.OemBackslash));
        Assert.Equal(Key.A, KeyInterop.KeyFromVirtualKey(0x41));
        Assert.Equal(Key.LeftShift, KeyInterop.KeyFromVirtualKey(0x10));
        Assert.Equal(Key.RightAlt, KeyInterop.KeyFromVirtualKey(0xA5));
        Assert.Equal(Key.None, KeyInterop.KeyFromVirtualKey(int.MaxValue));

        foreach (Key key in Enum.GetValues<Key>().Distinct())
        {
            if (key is Key.None or Key.LineFeed or Key.System or Key.DeadCharProcessed)
                continue;

            int virtualKey = KeyInterop.VirtualKeyFromKey(key);
            Assert.NotEqual(0, virtualKey);
            Assert.Equal(key, KeyInterop.KeyFromVirtualKey(virtualKey));
        }
    }

    [Fact]
    public void KeyboardStaticEventsDevicesAndProviderFocusContractsAreFunctional()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        RestoreFocusMode originalMode = Keyboard.DefaultRestoreFocusMode;
        var element = new Border { Focusable = true };
        int keyCalls = 0;
        int focusCalls = 0;
        int providerCalls = 0;

        KeyEventHandler keyHandler = (_, args) =>
        {
            keyCalls++;
            Assert.Equal(Key.A, args.Key);
        };
        KeyboardFocusChangedEventHandler focusHandler = (_, args) =>
        {
            focusCalls++;
            Assert.Same(element, args.NewFocus);
        };
        KeyboardInputProviderAcquireFocusEventHandler providerHandler = (_, args) =>
        {
            providerCalls++;
            Assert.True(args.FocusAcquired);
            Assert.Same(Keyboard.PrimaryDevice, args.KeyboardDevice);
        };

        try
        {
            Assert.NotNull(Keyboard.PrimaryDevice);
            Keyboard.DefaultRestoreFocusMode = RestoreFocusMode.None;
            Assert.Equal(RestoreFocusMode.None, Keyboard.PrimaryDevice.DefaultRestoreFocusMode);

            Keyboard.AddKeyDownHandler(element, keyHandler);
            element.RaiseEvent(new KeyEventArgs(
                Keyboard.KeyDownEvent,
                Key.A,
                ModifierKeys.Control,
                isDown: true,
                isRepeat: false,
                timestamp: 42));
            Assert.Equal(1, keyCalls);

            Keyboard.AddGotKeyboardFocusHandler(element, focusHandler);
            Assert.Same(element, Keyboard.Focus(element));
            Assert.Same(element, Keyboard.FocusedElement);
            Assert.Same(element, Keyboard.PrimaryDevice.FocusedElement);
            Assert.Equal(1, focusCalls);

            Keyboard.AddKeyboardInputProviderAcquireFocusHandler(element, providerHandler);
            element.RaiseEvent(new KeyboardInputProviderAcquireFocusEventArgs(
                Keyboard.PrimaryDevice,
                43,
                focusAcquired: true)
            {
                RoutedEvent = Keyboard.KeyboardInputProviderAcquireFocusEvent,
            });
            Assert.Equal(1, providerCalls);

            Keyboard.RemoveKeyDownHandler(element, keyHandler);
            Keyboard.RemoveGotKeyboardFocusHandler(element, focusHandler);
            Keyboard.RemoveKeyboardInputProviderAcquireFocusHandler(element, providerHandler);
        }
        finally
        {
            Keyboard.ClearFocus();
            Keyboard.DefaultRestoreFocusMode = originalMode;
        }

        Assert.Throws<ArgumentException>(
            () => Keyboard.AddKeyDownHandler(new DependencyObject(), keyHandler));
    }

    [Fact]
    public void MouseStaticStateCaptureCursorAndAttachedEventsShareTheInputPipeline()
    {
        var captured = new Border();
        var outside = new Border();
        int gotCaptureCalls = 0;
        int lostCaptureCalls = 0;
        int downCalls = 0;
        int outsideCalls = 0;

        MouseEventHandler gotHandler = (_, _) => gotCaptureCalls++;
        MouseEventHandler lostHandler = (_, _) => lostCaptureCalls++;
        MouseButtonEventHandler downHandler = (_, _) => downCalls++;
        MouseButtonEventHandler outsideHandler = (_, args) =>
        {
            outsideCalls++;
            Assert.Same(captured, args.Source);
        };

        var pressed = new MouseButtonStates
        {
            Left = MouseButtonState.Pressed,
            Middle = MouseButtonState.Released,
            Right = MouseButtonState.Released,
            XButton1 = MouseButtonState.Pressed,
            XButton2 = MouseButtonState.Released,
        };

        try
        {
            Mouse.AddGotMouseCaptureHandler(captured, gotHandler);
            Mouse.AddLostMouseCaptureHandler(captured, lostHandler);
            Mouse.AddMouseDownHandler(captured, downHandler);
            Mouse.AddPreviewMouseDownOutsideCapturedElementHandler(captured, outsideHandler);

            Mouse.UpdateState(new Point(15, 25), outside, pressed);
            Assert.Same(outside, Mouse.DirectlyOver);
            Assert.Equal(new Point(15, 25), Mouse.GetPosition(null));
            Assert.Equal(MouseButtonState.Pressed, Mouse.LeftButton);
            Assert.Equal(MouseButtonState.Pressed, Mouse.XButton1);
            Assert.Equal(Mouse.LeftButton, Mouse.PrimaryDevice.LeftButton);

            Assert.True(Mouse.Capture(captured, CaptureMode.SubTree));
            Assert.Same(captured, Mouse.Captured);
            Assert.Equal(CaptureMode.SubTree, Mouse.CurrentCaptureMode);
            Assert.Equal(1, gotCaptureCalls);

            captured.RaiseEvent(new MouseButtonEventArgs(
                Mouse.MouseDownEvent,
                new Point(15, 25),
                MouseButton.Left,
                MouseButtonState.Pressed,
                clickCount: 1,
                pressed.Left,
                pressed.Middle,
                pressed.Right,
                pressed.XButton1,
                pressed.XButton2,
                ModifierKeys.None,
                timestamp: 44));
            Assert.Equal(1, downCalls);

            Mouse.RaiseOutsideCapturedElementEvent(
                true,
                outside,
                new Point(15, 25),
                MouseButton.Left,
                MouseButtonState.Pressed,
                1,
                pressed,
                ModifierKeys.None,
                45);
            Assert.Equal(1, outsideCalls);

            Point[] intermediate = new Point[8];
            int count = Mouse.GetIntermediatePoints(null, intermediate);
            Assert.InRange(count, 1, intermediate.Length);
            Assert.Contains(new Point(15, 25), intermediate.Take(count));

            Mouse.OverrideCursor = Cursors.Wait;
            Assert.Same(Cursors.Wait, Mouse.CurrentCursor);
            Assert.True(Mouse.SetCursor(Cursors.Hand));
            Assert.Same(Cursors.Wait, Mouse.CurrentCursor);

            Assert.True(Mouse.Capture(null));
            Assert.Null(Mouse.Captured);
            Assert.Equal(1, lostCaptureCalls);

            Mouse.RemoveMouseDownHandler(captured, downHandler);
            captured.RaiseEvent(new MouseButtonEventArgs(
                Mouse.MouseDownEvent,
                Point.Zero,
                MouseButton.Left,
                MouseButtonState.Released,
                1,
                MouseButtonState.Released,
                MouseButtonState.Released,
                MouseButtonState.Released,
                MouseButtonState.Released,
                MouseButtonState.Released,
                ModifierKeys.None,
                46));
            Assert.Equal(1, downCalls);
        }
        finally
        {
            Mouse.Capture(null);
            Mouse.OverrideCursor = null;
            Mouse.UpdateState(Point.Zero, null, MouseButtonStates.AllReleased);
        }

        Assert.Throws<ArgumentException>(
            () => Mouse.AddMouseDownHandler(new DependencyObject(), downHandler));
    }
}
