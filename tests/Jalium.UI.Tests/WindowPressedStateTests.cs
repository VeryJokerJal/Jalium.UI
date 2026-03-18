using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class WindowPressedStateTests
{
    private static void ResetInputState()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        InputMethod.SetTarget(null);
        InputMethod.CancelComposition();
        UIElement.ForceReleaseMouseCapture();
        Window.SetKeyStateProviderForTesting(null);
    }

    [Fact]
    public void MouseDownUp_ShouldSetAndClearPressedState_OnHitChain()
    {
        ResetInputState();

        try
        {
            var (window, host, leaf) = CreateWindowTree();
            Assert.True(leaf.CaptureMouse());

            InvokeMouseButtonDown(window, MouseButton.Left, x: 20, y: 20);

            Assert.True(leaf.IsPressed);
            Assert.True(host.IsPressed);
            Assert.True(window.IsPressed);

            InvokeMouseButtonUp(window, MouseButton.Left, x: 20, y: 20);

            Assert.False(leaf.IsPressed);
            Assert.False(host.IsPressed);
            Assert.False(window.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void PreviewMouseUpHandled_ShouldStillClearPressedState()
    {
        ResetInputState();

        try
        {
            var (window, _, leaf) = CreateWindowTree();
            window.AddHandler(UIElement.PreviewMouseUpEvent, new RoutedEventHandler((_, e) =>
            {
                e.Handled = true;
            }));

            Assert.True(leaf.CaptureMouse());
            InvokeMouseButtonDown(window, MouseButton.Left, x: 30, y: 30);
            Assert.True(leaf.IsPressed);

            InvokeMouseButtonUp(window, MouseButton.Left, x: 30, y: 30);
            Assert.False(leaf.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void CaptureChanged_ShouldClearPressedState()
    {
        ResetInputState();

        try
        {
            var (window, host, leaf) = CreateWindowTree();
            Assert.True(leaf.CaptureMouse());
            InvokeMouseButtonDown(window, MouseButton.Left, x: 40, y: 40);

            Assert.True(leaf.IsPressed);
            Assert.True(host.IsPressed);
            Assert.True(window.IsPressed);

            InvokeWndProc(window, msg: 0x0215, wParam: nint.Zero, lParam: nint.Zero); // WM_CAPTURECHANGED

            Assert.False(leaf.IsPressed);
            Assert.False(host.IsPressed);
            Assert.False(window.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void Deactivate_ShouldReleaseMouseCapture_AndClearPressedState()
    {
        ResetInputState();

        try
        {
            var (window, host, leaf) = CreateWindowTree();
            Assert.True(leaf.CaptureMouse());
            InvokeMouseButtonDown(window, MouseButton.Left, x: 40, y: 40);

            Assert.True(leaf.IsPressed);
            Assert.True(host.IsPressed);
            Assert.True(window.IsPressed);
            Assert.Same(leaf, Mouse.Captured);

            InvokeActivate(window, state: 0);

            Assert.Null(Mouse.Captured);
            Assert.False(leaf.IsPressed);
            Assert.False(host.IsPressed);
            Assert.False(window.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void Deactivate_ShouldCloseOverlayLightDismissPopups()
    {
        ResetInputState();

        try
        {
            var root = new Grid
            {
                Width = 320,
                Height = 240
            };

            var popup = new Popup
            {
                Child = new Border
                {
                    Width = 120,
                    Height = 48
                },
                PlacementTarget = root,
                ShouldConstrainToRootBounds = true,
                StaysOpen = false
            };

            root.Children.Add(popup);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 240,
                Content = root
            };

            popup.IsOpen = true;
            window.Measure(new Size(320, 240));
            window.Arrange(new Rect(0, 0, 320, 240));

            Assert.True(popup.IsOpen);
            Assert.True(window.OverlayLayer.HasLightDismissPopups);

            InvokeActivate(window, state: 0);

            Assert.False(popup.IsOpen);
            Assert.False(window.OverlayLayer.HasLightDismissPopups);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void CancelMode_ShouldReleaseMouseCapture_AndCloseOverlayLightDismissPopups()
    {
        ResetInputState();

        try
        {
            var root = new Grid
            {
                Width = 320,
                Height = 240
            };

            var popup = new Popup
            {
                Child = new Border
                {
                    Width = 120,
                    Height = 48
                },
                PlacementTarget = root,
                ShouldConstrainToRootBounds = true,
                StaysOpen = false
            };

            var leaf = new TestElement();
            root.Children.Add(leaf);
            root.Children.Add(popup);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 240,
                Content = root
            };

            popup.IsOpen = true;
            window.Measure(new Size(320, 240));
            window.Arrange(new Rect(0, 0, 320, 240));
            Assert.True(leaf.CaptureMouse());

            InvokeWndProc(window, msg: 0x001F, wParam: nint.Zero, lParam: nint.Zero); // WM_CANCELMODE

            Assert.Null(Mouse.Captured);
            Assert.False(popup.IsOpen);
            Assert.False(window.OverlayLayer.HasLightDismissPopups);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void SpaceKey_ShouldSetAndClearPressedState_OnFocusChain()
    {
        ResetInputState();

        try
        {
            var (window, host, leaf) = CreateWindowTree();
            Assert.True(leaf.Focus());

            InvokeKeyDown(window, Key.Space, lParam: nint.Zero);
            Assert.True(leaf.IsPressed);
            Assert.True(host.IsPressed);
            Assert.True(window.IsPressed);

            InvokeKeyUp(window, Key.Space, lParam: nint.Zero);
            Assert.False(leaf.IsPressed);
            Assert.False(host.IsPressed);
            Assert.False(window.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void RepeatedSpaceKeyDown_ShouldNotActivatePressedState()
    {
        ResetInputState();

        try
        {
            var (window, host, leaf) = CreateWindowTree();
            Assert.True(leaf.Focus());

            nint repeatLParam = (nint)(1L << 30);
            InvokeKeyDown(window, Key.Space, repeatLParam);

            Assert.False(leaf.IsPressed);
            Assert.False(host.IsPressed);
            Assert.False(window.IsPressed);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void ReactivatedWindow_ShouldSuppressImmediateEscape_WhenEscapeWasAlreadyDown()
    {
        ResetInputState();

        try
        {
            var (window, _, cancelButton) = CreateWindowTreeWithCancelButton();
            int clickCount = 0;
            cancelButton.Click += (_, _) => clickCount++;

            Window.SetKeyStateProviderForTesting(key => key == 0x1B ? unchecked((short)0x8000) : (short)0);
            InvokeActivate(window, state: 1);

            Window.SetKeyStateProviderForTesting(_ => 0);
            InvokeKeyDown(window, Key.Escape, lParam: nint.Zero);
            InvokeKeyUp(window, Key.Escape, lParam: nint.Zero);

            Assert.Equal(0, clickCount);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void ReactivatedWindow_ShouldAllowEscape_WhenEscapeWasNotAlreadyDown()
    {
        ResetInputState();

        try
        {
            var (window, _, cancelButton) = CreateWindowTreeWithCancelButton();
            int clickCount = 0;
            cancelButton.Click += (_, _) => clickCount++;

            Window.SetKeyStateProviderForTesting(_ => 0);
            InvokeActivate(window, state: 1);
            InvokeKeyDown(window, Key.Escape, lParam: nint.Zero);

            Assert.Equal(1, clickCount);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void KillFocus_ShouldClearKeyboardFocus()
    {
        ResetInputState();

        try
        {
            var (window, _, leaf) = CreateWindowTree();
            Assert.True(leaf.Focus());
            Assert.Same(leaf, Keyboard.FocusedElement);

            InvokeWndProc(window, msg: 0x0008, wParam: nint.Zero, lParam: nint.Zero); // WM_KILLFOCUS

            Assert.Null(Keyboard.FocusedElement);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void KillFocus_ToPopupWindow_ShouldKeepExternalLightDismissPopupOpen()
    {
        ResetInputState();

        var popupHandle = (nint)0x2345;

        try
        {
            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 240
            };

            var popup = new Popup
            {
                StaysOpen = false
            };
            popup.IsOpen = true;
            window.ActiveExternalPopups.Add(popup);

            var popupRoot = new PopupRoot(popup, new Border { Width = 120, Height = 48 }, isLightDismiss: true);
            var popupWindow = new PopupWindow(window, popupRoot);
            RegisterPopupWindowForTest(popupHandle, popupWindow);

            InvokeWndProc(window, msg: 0x0008, wParam: popupHandle, lParam: nint.Zero); // WM_KILLFOCUS

            Assert.True(popup.IsOpen);
            Assert.Contains(popup, window.ActiveExternalPopups);
        }
        finally
        {
            UnregisterPopupWindowForTest(popupHandle);
            ResetInputState();
        }
    }

    [Fact]
    public void OverlayPopup_ClickInsideBounds_ShouldReachPopupContent()
    {
        ResetInputState();

        try
        {
            int clickCount = 0;
            var anchor = new Border
            {
                Width = 80,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            var popupButton = new Button
            {
                Content = "Popup",
                Width = 96,
                Height = 32
            };
            popupButton.Click += (_, _) => clickCount++;

            var popup = new Popup
            {
                Child = popupButton,
                PlacementTarget = anchor,
                ShouldConstrainToRootBounds = true,
                StaysOpen = false
            };

            var root = new Grid
            {
                Width = 320,
                Height = 240
            };
            root.Children.Add(anchor);
            root.Children.Add(popup);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 240,
                Content = root
            };

            popup.IsOpen = true;
            window.Measure(new Size(320, 240));
            window.Arrange(new Rect(0, 0, 320, 240));

            var popupRoot = GetPrivateField<PopupRoot>(popup, "_popupRoot");
            var bounds = popupRoot.VisualBounds;
            var clickPoint = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

            var hit = InvokeHitTestElement(window, clickPoint);

            InvokeMouseButtonDown(window, MouseButton.Left, x: (int)Math.Round(clickPoint.X), y: (int)Math.Round(clickPoint.Y));
            InvokeMouseButtonUp(window, MouseButton.Left, x: (int)Math.Round(clickPoint.X), y: (int)Math.Round(clickPoint.Y));

            Assert.True(popup.IsOpen, $"Popup closed early. Hit={hit?.GetType().Name ?? "<null>"} Bounds={bounds} Point={clickPoint}");
            Assert.True(clickCount == 1, $"Click did not reach popup content. Hit={hit?.GetType().Name ?? "<null>"} Bounds={bounds} Point={clickPoint}");
            Assert.NotSame(window.OverlayLayer, hit);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void OverlayPopup_WithStaysOpen_ShouldBypassCachedUnderlyingHit()
    {
        ResetInputState();

        try
        {
            int clickCount = 0;
            var background = new Border
            {
                Width = 320,
                Height = 240
            };

            var anchor = new Border
            {
                Width = 80,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            var popupButton = new Button
            {
                Content = "Popup",
                Width = 96,
                Height = 32
            };
            popupButton.Click += (_, _) => clickCount++;

            var popup = new Popup
            {
                Child = popupButton,
                PlacementTarget = anchor,
                ShouldConstrainToRootBounds = true,
                StaysOpen = true
            };

            var root = new Grid
            {
                Width = 320,
                Height = 240
            };
            root.Children.Add(background);
            root.Children.Add(anchor);
            root.Children.Add(popup);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 240,
                Content = root
            };

            window.Measure(new Size(320, 240));
            window.Arrange(new Rect(0, 0, 320, 240));

            var futureClickPoint = new Point(48, 40);
            var cachedHit = InvokeHitTestElement(window, futureClickPoint);
            Assert.Same(popup, cachedHit);

            popup.IsOpen = true;
            window.Measure(new Size(320, 240));
            window.Arrange(new Rect(0, 0, 320, 240));

            var popupRoot = GetPrivateField<PopupRoot>(popup, "_popupRoot");
            var bounds = popupRoot.VisualBounds;
            var clickPoint = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

            InvokeMouseButtonDown(window, MouseButton.Left, x: (int)Math.Round(clickPoint.X), y: (int)Math.Round(clickPoint.Y));
            InvokeMouseButtonUp(window, MouseButton.Left, x: (int)Math.Round(clickPoint.X), y: (int)Math.Round(clickPoint.Y));

            Assert.True(clickCount == 1, $"Click did not reach non-light-dismiss popup content. Bounds={bounds} Point={clickPoint}");
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void PopupWindow_NcHitTest_ShouldReturnClientArea()
    {
        var popupHandle = (nint)0x3456;

        try
        {
            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 240
            };

            var popup = new Popup { StaysOpen = false };
            var popupRoot = new PopupRoot(popup, new Border { Width = 120, Height = 48 }, isLightDismiss: true);
            var popupWindow = new PopupWindow(window, popupRoot);
            RegisterPopupWindowForTest(popupHandle, popupWindow);

            var wndProc = typeof(PopupWindow).GetMethod("PopupWndProc", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(wndProc);

            var hit = Assert.IsType<nint>(wndProc!.Invoke(null, new object[] { popupHandle, 0x0084u, nint.Zero, PackPointToLParam(10, 10) }));
            Assert.Equal((nint)1, hit);
        }
        finally
        {
            UnregisterPopupWindowForTest(popupHandle);
        }
    }

    [Fact]
    public void SetFocus_ShouldWakeRenderPipeline()
    {
        ResetInputState();

        try
        {
            var (window, _, _) = CreateWindowTree();
            SetWindowDispatcherForTest(window, Dispatcher.GetForCurrentThread());
            SetWindowHandleForTest(window, (nint)1);

            InvokeWndProc(window, msg: 0x0007, wParam: nint.Zero, lParam: nint.Zero); // WM_SETFOCUS

            Assert.True(GetRenderScheduledForTest(window));
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void SystemKeyWithoutHandler_ShouldRemainUnhandled()
    {
        ResetInputState();

        try
        {
            var (window, _, _) = CreateWindowTree();

            bool handled = InvokeKeyDownHandled(window, Key.Alt, lParam: nint.Zero);

            Assert.False(handled);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void RegularKeyWithoutHandler_ShouldRemainUnhandled()
    {
        ResetInputState();

        try
        {
            var (window, _, _) = CreateWindowTree();

            bool handled = InvokeKeyDownHandled(window, Key.A, lParam: nint.Zero);

            Assert.False(handled);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void WindowsKeys_ShouldBeReservedForShellHandling()
    {
        Assert.True(InvokeIsShellReservedVirtualKey(0x5B)); // VK_LWIN
        Assert.True(InvokeIsShellReservedVirtualKey(0x5C)); // VK_RWIN
        Assert.False(InvokeIsShellReservedVirtualKey((int)Key.Enter));
    }

    [Fact]
    public void CharWithoutFocusedElement_ShouldNotRouteTextInputToWindow()
    {
        ResetInputState();

        try
        {
            var (window, _, _) = CreateWindowTree();
            int textInputCount = 0;
            window.AddHandler(UIElement.TextInputEvent, new TextCompositionEventHandler((_, _) => textInputCount++));

            InvokeChar(window, 'a');

            Assert.Equal(0, textInputCount);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void CharWithFocusedElement_ShouldRouteTextInputToFocusedElement()
    {
        ResetInputState();

        try
        {
            var (window, _, leaf) = CreateWindowTree();
            int textInputCount = 0;
            leaf.AddHandler(UIElement.TextInputEvent, new TextCompositionEventHandler((_, _) => textInputCount++));
            Assert.True(leaf.Focus());

            InvokeChar(window, 'a');

            Assert.Equal(1, textInputCount);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void HitTestElement_ShouldReuseCachedSubtree_ForRepeatedMovesWithinSameBranch()
    {
        ResetInputState();

        try
        {
            var leaf = new CountingBorder { Width = 32, Height = 24 };
            var branchMid = new CountingBorder { Width = 32, Height = 24, Child = leaf };
            var branchRoot = new CountingBorder { Width = 32, Height = 24, Child = branchMid };
            var siblingBranch = new CountingBorder { Width = 32, Height = 24 };

            var host = new StackPanel { Orientation = Orientation.Horizontal };
            host.Children.Add(branchRoot);
            host.Children.Add(siblingBranch);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 96,
                Height = 40,
                Content = host
            };

            window.Measure(new Size(96, 40));
            window.Arrange(new Rect(0, 0, 96, 40));

            var firstHit = InvokeHitTestElement(window, new Point(10, 10));
            Assert.Same(leaf, firstHit);

            leaf.ResetHitTestCount();
            branchMid.ResetHitTestCount();
            branchRoot.ResetHitTestCount();
            siblingBranch.ResetHitTestCount();

            var secondHit = InvokeHitTestElement(window, new Point(11, 10));

            Assert.Same(leaf, secondHit);
            Assert.Equal(0, siblingBranch.HitTestCount);
            Assert.True(leaf.HitTestCount > 0);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void HitTestElement_AfterContentReplacement_ShouldIgnoreDetachedCachedElement()
    {
        ResetInputState();

        try
        {
            var oldLeaf = new CountingBorder { Width = 32, Height = 24 };
            var oldHost = new StackPanel();
            oldHost.Children.Add(oldLeaf);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 96,
                Height = 40,
                Content = oldHost
            };

            window.Measure(new Size(96, 40));
            window.Arrange(new Rect(0, 0, 96, 40));

            var initialHit = InvokeHitTestElement(window, new Point(10, 10));
            Assert.Same(oldLeaf, initialHit);

            var newLeaf = new CountingBorder { Width = 32, Height = 24 };
            var newHost = new StackPanel();
            newHost.Children.Add(newLeaf);

            oldLeaf.ResetHitTestCount();
            window.Content = newHost;
            window.Measure(new Size(96, 40));
            window.Arrange(new Rect(0, 0, 96, 40));

            var replacedHit = InvokeHitTestElement(window, new Point(10, 10));

            Assert.Same(newLeaf, replacedHit);
            Assert.Equal(0, oldLeaf.HitTestCount);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void HitTestElement_WithModalOverlay_ShouldBypassCachedUnderlyingElement()
    {
        ResetInputState();

        try
        {
            var leaf = new CountingBorder { Width = 32, Height = 24 };
            var host = new StackPanel();
            host.Children.Add(leaf);

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 96,
                Height = 40,
                Content = host
            };

            window.Measure(new Size(96, 40));
            window.Arrange(new Rect(0, 0, 96, 40));

            var initialHit = InvokeHitTestElement(window, new Point(10, 10));
            Assert.Same(leaf, initialHit);

            var modalRoot = new Border
            {
                Width = 96,
                Height = 40,
                Background = Jalium.UI.Media.Brushes.Transparent
            };
            window.OverlayLayer.AddModalRoot(modalRoot);
            window.Measure(new Size(96, 40));
            window.Arrange(new Rect(0, 0, 96, 40));

            leaf.ResetHitTestCount();
            var overlayHit = InvokeHitTestElement(window, new Point(10, 10));

            Assert.Same(modalRoot, overlayHit);
            Assert.Equal(0, leaf.HitTestCount);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void ImeWithoutFocusedImeTarget_ShouldNotStartComposition()
    {
        ResetInputState();

        try
        {
            var (window, _, leaf) = CreateWindowTree();
            Assert.True(leaf.Focus());

            InvokeImeStartComposition(window);

            Assert.False(InvokeCanHandleImeMessages(window));
            Assert.False(InputMethod.IsComposing);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void ImeWithFocusedImeTarget_ShouldStartComposition()
    {
        ResetInputState();

        try
        {
            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native
            };

            var host = new TestPanel();
            var imeLeaf = new TestImeElement();
            host.AddChild(imeLeaf);
            window.Content = host;

            Assert.True(imeLeaf.Focus());

            InvokeImeStartComposition(window);

            Assert.True(InvokeCanHandleImeMessages(window));
            Assert.True(InputMethod.IsComposing);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void ImeWithFocusedRichTextBox_ShouldStartComposition()
    {
        ResetInputState();

        try
        {
            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native
            };

            var host = new TestPanel();
            var richTextBox = new RichTextBox
            {
                Width = 240,
                Height = 120
            };
            host.AddChild(richTextBox);
            window.Content = host;

            richTextBox.Measure(new Size(240, 120));
            richTextBox.Arrange(new Rect(0, 0, 240, 120));

            Assert.True(richTextBox.Focus());

            InvokeImeStartComposition(window);

            Assert.True(InvokeCanHandleImeMessages(window));
            Assert.True(InputMethod.IsComposing);
            Assert.True(richTextBox.IsImeComposing);
            Assert.Same(richTextBox, InputMethod.Current);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void WndProc_ShouldSwallowKeyHandlerExceptions()
    {
        ResetInputState();

        try
        {
            var (window, _, _) = CreateWindowTree();
            window.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, _) => throw new InvalidOperationException("boom")));

            var exception = Record.Exception(() => InvokeWndProc(window, msg: 0x0100, wParam: (nint)(int)Key.A, lParam: nint.Zero)); // WM_KEYDOWN

            Assert.Null(exception);
        }
        finally
        {
            ResetInputState();
        }
    }

    [Fact]
    public void WndProc_ShouldSwallowTextInputHandlerExceptions()
    {
        ResetInputState();

        try
        {
            var (window, _, leaf) = CreateWindowTree();
            Assert.True(leaf.Focus());
            leaf.AddHandler(UIElement.TextInputEvent, new TextCompositionEventHandler((_, _) => throw new InvalidOperationException("boom")));

            var exception = Record.Exception(() => InvokeWndProc(window, msg: 0x0102, wParam: (nint)'a', lParam: nint.Zero)); // WM_CHAR

            Assert.Null(exception);
        }
        finally
        {
            ResetInputState();
        }
    }

    private static (Window window, TestPanel host, TestElement leaf) CreateWindowTree()
    {
        var window = new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native
        };

        var host = new TestPanel();
        var leaf = new TestElement();
        host.AddChild(leaf);
        window.Content = host;

        return (window, host, leaf);
    }

    private static (Window window, TestPanel host, Button cancelButton) CreateWindowTreeWithCancelButton()
    {
        var window = new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native
        };

        var host = new TestPanel();
        var cancelButton = new Button
        {
            IsCancel = true
        };

        host.AddChild(cancelButton);
        window.Content = host;

        return (window, host, cancelButton);
    }

    private static void InvokeMouseButtonDown(Window window, MouseButton button, int x, int y, int clickCount = 1)
    {
        var method = typeof(Window).GetMethod("OnMouseButtonDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        nint wParam = (nint)0x0001; // MK_LBUTTON
        nint lParam = PackPointToLParam(x, y);
        method!.Invoke(window, new object[] { button, wParam, lParam, clickCount });
    }

    private static void InvokeMouseButtonUp(Window window, MouseButton button, int x, int y)
    {
        var method = typeof(Window).GetMethod("OnMouseButtonUp", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        nint wParam = nint.Zero;
        nint lParam = PackPointToLParam(x, y);
        method!.Invoke(window, new object[] { button, wParam, lParam });
    }

    private static void InvokeKeyDown(Window window, Key key, nint lParam)
    {
        var method = typeof(Window).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, new object[] { (nint)(int)key, lParam });
    }

    private static bool InvokeKeyDownHandled(Window window, Key key, nint lParam)
    {
        var method = typeof(Window).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(window, new object[] { (nint)(int)key, lParam })!;
    }

    private static void InvokeKeyUp(Window window, Key key, nint lParam)
    {
        var method = typeof(Window).GetMethod("OnKeyUp", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, new object[] { (nint)(int)key, lParam });
    }

    private static void InvokeChar(Window window, char c)
    {
        var method = typeof(Window).GetMethod("OnChar", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, new object[] { (nint)c, nint.Zero });
    }

    private static void InvokeActivate(Window window, int state)
    {
        InvokeWndProc(window, msg: 0x0006, wParam: (nint)state, lParam: nint.Zero); // WM_ACTIVATE
    }

    private static void InvokeWndProc(Window window, uint msg, nint wParam, nint lParam)
    {
        var method = typeof(Window).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        _ = method!.Invoke(window, new object[] { nint.Zero, msg, wParam, lParam });
    }

    private static void SetWindowDispatcherForTest(Window window, Dispatcher dispatcher)
    {
        var field = typeof(Window).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(window, dispatcher);
    }

    private static void SetWindowHandleForTest(Window window, nint handle)
    {
        var field = typeof(Window).GetField("<Handle>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(window, handle);
    }

    private static bool GetRenderScheduledForTest(Window window)
    {
        var field = typeof(Window).GetField("_renderState", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var state = (int)field!.GetValue(window)!;
        const int RenderFlag_Scheduled = 1 << 0;
        return (state & RenderFlag_Scheduled) != 0;
    }

    private static UIElement? InvokeHitTestElement(Window window, Point point)
    {
        var method = typeof(Window).GetMethod("HitTestElement", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(window, new object[] { point, "test-hit" }) as UIElement;
    }

    private static T GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private static void RegisterPopupWindowForTest(nint handle, PopupWindow popupWindow)
    {
        var field = typeof(PopupWindow).GetField("_popupWindows", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var popupWindows = Assert.IsAssignableFrom<IDictionary<nint, PopupWindow>>(field!.GetValue(null));
        popupWindows[handle] = popupWindow;
    }

    private static void UnregisterPopupWindowForTest(nint handle)
    {
        var field = typeof(PopupWindow).GetField("_popupWindows", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var popupWindows = Assert.IsAssignableFrom<IDictionary<nint, PopupWindow>>(field!.GetValue(null));
        popupWindows.Remove(handle);
    }

    private static void InvokeImeStartComposition(Window window)
    {
        var method = typeof(Window).GetMethod("OnImeStartComposition", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, []);
    }

    private static bool InvokeCanHandleImeMessages(Window window)
    {
        var method = typeof(Window).GetMethod("CanHandleImeMessages", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(window, [])!;
    }

    private static bool InvokeIsShellReservedVirtualKey(int virtualKey)
    {
        var method = typeof(Window).GetMethod("IsShellReservedVirtualKey", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, new object[] { (nint)virtualKey })!;
    }

    private static nint PackPointToLParam(int x, int y)
    {
        int packed = (y << 16) | (x & 0xFFFF);
        return (nint)packed;
    }

    private sealed class TestPanel : FrameworkElement
    {
        public void AddChild(UIElement child)
        {
            AddVisualChild(child);
        }
    }

    private sealed class TestElement : FrameworkElement
    {
        public TestElement()
        {
            Focusable = true;
        }
    }

    private sealed class TestImeElement : FrameworkElement, IImeSupport
    {
        public TestImeElement()
        {
            Focusable = true;
        }

        public Point GetImeCaretPosition() => Point.Zero;

        public void OnImeCompositionStart()
        {
        }

        public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
        {
        }

        public void OnImeCompositionEnd(string? resultString)
        {
        }
    }

    private sealed class CountingBorder : Border
    {
        public int HitTestCount { get; private set; }

        protected override HitTestResult? HitTestCore(Point point)
        {
            HitTestCount++;
            return base.HitTestCore(point);
        }

        public void ResetHitTestCount()
        {
            HitTestCount = 0;
        }
    }
}
