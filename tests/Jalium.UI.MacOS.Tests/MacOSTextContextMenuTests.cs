using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSTextContextMenuTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void DefaultMenu_PreservesSelectionAndRaisesLifecycleEvents(int kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(kind);
        int opening = 0, closing = 0, calls = 0;
        ContextMenuService.AddContextMenuOpeningHandler(host.Editor, (_, _) => opening++);
        ContextMenuService.AddContextMenuClosingHandler(host.Editor, (_, _) => closing++);
        PlatformTextContextMenus.Show = request =>
        {
            calls++;
            Assert.True(request.IsCurrent());
            Assert.Equal(kind != 3, request.CanExecute(PlatformTextEditingCommand.Copy));
            Assert.True(request.CanExecute(PlatformTextEditingCommand.SelectAll));
            return true;
        };
        host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, MouseButton.Right));
        Assert.Equal((1, 1, 1), (calls, opening, closing));
        if (host.Editor is TextBox text) Assert.Equal("alpha", text.SelectedText);
        if (host.Editor is EditControl code) Assert.Equal(5, code.SelectionLength);
    }

    [Theory]
    [InlineData(MouseButton.Right, ModifierKeys.None, true)]
    [InlineData(MouseButton.Left, ModifierKeys.Control, true)]
    [InlineData(MouseButton.Left, ModifierKeys.Windows, false)]
    [InlineData(MouseButton.Left, ModifierKeys.None, false)]
    public void PointerGestures_KeepCommandClickSeparate(MouseButton button, ModifierKeys modifiers, bool expected)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        int calls = 0;
        PlatformTextContextMenus.Show = _ => { calls++; return true; };
        host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, button, modifiers));
        Assert.Equal(expected ? 1 : 0, calls);
    }

    [Theory]
    [InlineData(Key.F10, ModifierKeys.Shift)] [InlineData(Key.Apps, ModifierKeys.None)]
    public void KeyboardRequest_UsesDefaultMenu(Key key, ModifierKeys modifiers)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        int calls = 0;
        PlatformTextContextMenus.Show = _ => { calls++; return true; };
        var args = new KeyEventArgs(UIElement.KeyDownEvent, key, modifiers, true, false, 0);
        host.Editor.RaiseEvent(args);
        Assert.True(args.Handled);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, MouseButton.Right)] [InlineData(true, MouseButton.Right)]
    [InlineData(false, MouseButton.Left)] [InlineData(true, MouseButton.Left)]
    public void ExplicitMenuAndCancelledOpening_AreNotReplaced(bool cancel, MouseButton button)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var menu = new ContextMenu();
        ContextMenuService.SetContextMenu(host.Panel, menu);
        int openings = 0;
        ContextMenuService.AddContextMenuOpeningHandler(host.Panel, (_, e) => { openings++; e.Handled = cancel; });
        PlatformTextContextMenus.Show = _ => throw new Xunit.Sdk.XunitException("Explicit menu replaced");
        var modifiers = button == MouseButton.Left ? ModifierKeys.Control : ModifierKeys.None;
        host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, button, modifiers));
        if (button == MouseButton.Right) Assert.False(menu.IsOpen);
        var preview = Mouse(UIElement.PreviewMouseUpEvent, button, modifiers);
        host.Editor.RaiseEvent(preview);
        Assert.Equal(button == MouseButton.Left && !cancel, preview.Handled);
        if (!preview.Handled) host.Editor.RaiseEvent(Mouse(UIElement.MouseUpEvent, button, modifiers));
        Assert.Equal(!cancel, menu.IsOpen);
        Assert.Equal(1, openings);
        menu.Close();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OpeningCancellationOrFocusChange_PreventsNativePopup(bool changeFocus)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        ContextMenuService.AddContextMenuOpeningHandler(host.Editor, (_, e) =>
        {
            if (changeFocus) host.Other.Focus();
            else e.Handled = true;
        });
        PlatformTextContextMenus.Show = _ => throw new Xunit.Sdk.XunitException("Invalid popup opened");
        host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, MouseButton.Right));
    }

    [Theory]
    [InlineData(false, Key.F10, ModifierKeys.Shift)] [InlineData(true, Key.F10, ModifierKeys.Shift)]
    [InlineData(false, Key.Apps, ModifierKeys.None)] [InlineData(true, Key.Apps, ModifierKeys.None)]
    public void ExplicitKeyboardMenu_UsesOneOpeningRequest(bool cancel, Key key, ModifierKeys modifiers)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        var menu = new ContextMenu();
        ContextMenuService.SetContextMenu(host.Panel, menu);
        int openings = 0;
        ContextMenuService.AddContextMenuOpeningHandler(host.Panel, (_, e) => { openings++; e.Handled = cancel; });
        PlatformTextContextMenus.Show = _ => throw new Xunit.Sdk.XunitException("Explicit menu replaced");
        host.Editor.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, key, modifiers, true, false, 0));
        Assert.Equal(!cancel, menu.IsOpen);
        Assert.Equal(1, openings);
        menu.Close();
    }

    [Fact]
    public void ReadOnlySelection_ExportsButCannotMutate()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        ((TextBox)host.Editor).IsReadOnly = true;
        int calls = 0;
        PlatformTextContextMenus.Show = request =>
        {
            calls++;
            Assert.True(request.CanExecute(PlatformTextEditingCommand.Copy));
            Assert.True(request.CanExecute(PlatformTextEditingCommand.SelectAll));
            foreach (var command in new[] { PlatformTextEditingCommand.Cut, PlatformTextEditingCommand.Paste,
                PlatformTextEditingCommand.Undo, PlatformTextEditingCommand.Redo })
                Assert.False(request.CanExecute(command));
            return true;
        };
        host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, MouseButton.Right));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void RetainedRequest_CannotFollowFocusOrUnavailableTargets(int mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        PlatformTextContextMenuRequest? retained = null;
        PlatformTextContextMenus.Show = request => { retained = request; return true; };
        host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, MouseButton.Right));
        Assert.NotNull(retained);
        switch (mutation)
        {
            case 0: host.Other.Focus(); break;
            case 1: host.Editor.IsEnabled = false; break;
            case 2: host.Editor.Visibility = Visibility.Hidden; break;
            case 3: host.Panel.Children.Remove(host.Editor); break;
            case 4: host.SetHandle(0); break;
        }
        Assert.False(retained.IsCurrent());
        foreach (var command in Enum.GetValues<PlatformTextEditingCommand>()) Assert.False(retained.CanExecute(command));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DisabledEditorOrService_DoesNotOpen(bool disableService)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        if (disableService) ContextMenuService.SetIsEnabled(host.Editor, false);
        else host.Editor.IsEnabled = false;
        int calls = 0;
        PlatformTextContextMenus.Show = _ => { calls++; return true; };
        host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, MouseButton.Right));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void UnopenedMenu_PreservesControlClickAndRelease(int reason)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        PlatformTextContextMenus.Show = _ => false;
        if (reason == 0) ContextMenuService.SetIsEnabled(host.Editor, false);
        if (reason == 1)
            ContextMenuService.AddContextMenuOpeningHandler(host.Editor, (_, e) => e.Handled = true);
        if (reason == 3) host.SetHandle(0);

        host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, MouseButton.Left, ModifierKeys.Control));
        Assert.Equal(0, ((TextBox)host.Editor).SelectionLength);
        var release = Mouse(UIElement.PreviewMouseUpEvent, MouseButton.Left, ModifierKeys.Control);
        host.Editor.RaiseEvent(release);
        Assert.False(release.Handled);
        host.Editor.RaiseEvent(Mouse(UIElement.MouseUpEvent, MouseButton.Left, ModifierKeys.Control));
    }

    [Theory]
    [InlineData(Key.F10, ModifierKeys.Shift)] [InlineData(Key.Apps, ModifierKeys.None)]
    public void CancelledKeyboardMenu_DoesNotConsumeTheKey(Key key, ModifierKeys modifiers)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        ContextMenuService.AddContextMenuOpeningHandler(host.Editor, (_, e) => e.Handled = true);
        PlatformTextContextMenus.Show = _ => throw new Xunit.Sdk.XunitException("Cancelled menu opened");
        var args = new KeyEventArgs(UIElement.KeyDownEvent, key, modifiers, true, false, 0);
        host.Editor.RaiseEvent(args);
        Assert.False(args.Handled);
    }

    [Fact]
    public void NestedRequests_DoNotStartAnotherTrackingLoop()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        int calls = 0;
        PlatformTextContextMenus.Show = request =>
        {
            calls++;
            if (calls == 1) host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, MouseButton.Right));
            return true;
        };
        host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, MouseButton.Right));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ForeignThread_CannotQueryTheCapturedEditor()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        PlatformTextContextMenus.Show = request =>
        {
            bool current = true, canCopy = true;
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    current = request.IsCurrent();
                    canCopy = request.CanExecute(PlatformTextEditingCommand.Copy);
                }
                catch (Exception error) { failure = error; }
            });
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.False(current);
            Assert.False(canCopy);
            return true;
        };
        host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, MouseButton.Right));
    }

    [Theory]
    [InlineData(0x02, ModifierKeys.Control)]
    [InlineData(0x08, ModifierKeys.Windows)]
    [InlineData(0x0a, ModifierKeys.Control | ModifierKeys.Windows)]
    public void PlatformPointerEvents_RetainPhysicalModifierIdentity(int flags, ModifierKeys expected)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        ModifierKeys? observed = null;
        host.Editor.MouseMove += (_, e) => observed = e.PhysicalModifiers;
        var method = typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(host.Window, [new PlatformEvent
        { Type = PlatformEventType.MouseMove, MouseX = 24, MouseY = 10, Modifiers = flags }]);
        Assert.Equal(expected, observed);
    }

    [Fact]
    public void NativeTracking_ConsumesThePressWithoutWaitingForAMouseUp()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host();
        PlatformTextContextMenus.Show = _ => true;
        var method = typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(host.Window, [new PlatformEvent
        { Type = PlatformEventType.MouseDown, MouseX = 24, MouseY = 10, Button = 0, Modifiers = 0x02 }]);
        Assert.False(host.Editor.IsPressed);
        Assert.Equal(MouseButtonState.Released, Jalium.UI.Input.Mouse.LeftButton);
        Assert.Equal(MouseButtonState.Released, Jalium.UI.Input.Mouse.RightButton);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    [InlineData(3, false)] [InlineData(3, true)]
    public void MacOSPopupUsesAnOwnerOverlayForItsNativeEditingMenu(int kind, bool keyboard)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(kind);
        host.Panel.Children.Remove(host.Editor);
        var popup = new Popup
        {
            Child = host.Editor, PlacementTarget = host.Other,
            PreferExternalWindow = true, StaysOpen = true,
            PopupAnimation = PopupAnimation.None
        };
        try
        {
            popup.IsOpen = true;
            Assert.Same(host.Window, Window.GetWindow(host.Editor));
            Assert.Null(typeof(Popup).GetField("_popupWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup));
            Assert.True(host.Editor.Focus());
            int calls = 0;
            PlatformTextContextMenus.Show = request =>
            {
                calls++;
                Assert.Equal(host.Window.Handle, request.Owner);
                Assert.True(request.IsCurrent());
                Assert.True(request.CanExecute(PlatformTextEditingCommand.SelectAll));
                if (!keyboard)
                    Assert.Equal(host.Editor.TranslatePoint(new Point(24, 32), host.Window), request.Position);
                return true;
            };
            if (keyboard)
                host.Editor.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.F10, ModifierKeys.Shift, true, false, 0));
            else host.Editor.RaiseEvent(Mouse(UIElement.MouseDownEvent, MouseButton.Right));
            Assert.Equal(1, calls);
        }
        finally { popup.IsOpen = false; }
    }

    private static MouseButtonEventArgs Mouse(RoutedEvent routed, MouseButton button, ModifierKeys modifiers = ModifierKeys.None) =>
        new(routed, new Point(24, 32), button, routed == UIElement.MouseDownEvent ? MouseButtonState.Pressed : MouseButtonState.Released,
            1, MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, modifiers, 0);

    private sealed class Host : IDisposable
    {
        private readonly Func<PlatformTextContextMenuRequest, bool>? _previous = PlatformTextContextMenus.Show;
        internal readonly DisplayedTestWindow Window = new() { TitleBarStyle = WindowTitleBarStyle.Native };
        internal readonly StackPanel Panel = new();
        internal readonly TextBox Other = new() { Text = "other" };
        internal readonly Control Editor;
        internal Host(int kind = 0)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus();
            Editor = kind switch
            {
                1 => new RichTextBox(FlowDocument.FromText("alpha beta")),
                2 => new EditControl { Text = "alpha beta" },
                3 => new PasswordBox { Password = "alpha beta" },
                _ => new TextBox { Text = "alpha beta" }
            };
            Panel.Children.Add(Editor); Panel.Children.Add(Other); Window.Content = Panel;
            Window.Measure(new Size(500, 400)); Window.Arrange(new Rect(0, 0, 500, 400));
            Assert.True(Editor.Focus());
            switch (Editor)
            {
                case TextBox text: text.Select(0, 5); break;
                case RichTextBox rich: rich.SelectAll(); break;
                case EditControl code: code.Select(0, 5); break;
                case PasswordBox password: password.SelectAll(); break;
            }
            // Logical contract only. No native method dereferences this handle;
            // native menu integration runs separately on AppKit's main thread.
            SetHandle(123);
        }
        internal void SetHandle(nint value) => typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(Window, value);
        public void Dispose()
        {
            PlatformTextContextMenus.Show = _previous;
            Jalium.UI.Input.Mouse.Capture(null);
            SetHandle(0); Keyboard.ClearFocus(); Window.Close();
        }
    }
}
